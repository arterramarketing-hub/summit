using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Streams the mountain in square chunks around a target. Chunks sit on a fixed world grid, so nothing
    /// re-triangulates as the rider moves.
    ///
    /// A chunk's heights, normals, colours, rock and tangents are all worked out on worker threads (the mountain is
    /// one pure function of x and z: see Slope); the main thread only hands the finished arrays to the mesh, a few
    /// chunks a frame within a small time budget. The ring just outside the visible area (one row further down the
    /// hill, one column further out either side) is built ahead of time and kept hidden, so crossing into a new row or
    /// column only switches chunks on. Rows from farRows down the hill (about 200 m and more) are drawn at half
    /// resolution, every other corner with its own full-resolution normal, colour and rock, and a skirt hanging from
    /// their edges over any gap to a full chunk beside them; they are rebuilt at full resolution as you come within
    /// farRows of them. (Not near a crevasse, whose lips lie on the snow, nor where a ridge's crest is cut finer. About
    /// a third fewer triangles; from up the hill the only difference is in the facets of distant rock faces.)
    /// </summary>
    public class SlopeStreamer : MonoBehaviour
    {
        public Transform target;
        public Material snowMaterial;
        public float chunkSize = 48f;
        int quads;
        public int sideChunks = 5;      // either side of the rider (open mountain: you can see across it)
        public int aheadChunks = 12;    // down the hill (~580 m)
        public int behindChunks = 1;
        public int buildsPerFrame = 1;  // the fewest finished chunks put in each frame (more while the budget lasts)
        /// <summary>The main thread's time each frame for putting finished chunks in (ms).</summary>
        public float frameBudgetMs = 1f;
        /// <summary>
        /// A snow triangle steeper than this (its face's normal.y) is mapped across its face, not from above: from
        /// above, a triangle on a near-vertical face (1.5 m wide and tens of metres tall on a wall) smears the snow's
        /// texture into long vertical streaks. (About 57 degrees: the stretch from above is 1.8 times there.) The
        /// crags' drifts (CliffSpires) go by it too.
        /// </summary>
        public const float SteepSnow = 0.55f;
        /// <summary>Rows this far down the hill or further are drawn at half resolution (see above).</summary>
        public int farRows = 5;

        readonly Dictionary<Vector2Int, GameObject> live = new Dictionary<Vector2Int, GameObject>();
        readonly Dictionary<Vector2Int, int> lodOf = new Dictionary<Vector2Int, int>();
        readonly Dictionary<Vector2Int, Work> inFlight = new Dictionary<Vector2Int, Work>();
        readonly Queue<GameObject> spare = new Queue<GameObject>();
        readonly List<Vector2Int> wanted = new List<Vector2Int>();    // the visible area, nearest first
        readonly List<Vector2Int> ring = new List<Vector2Int>();      // just outside it, built ahead and kept hidden
        readonly List<Vector2Int> drop = new List<Vector2Int>();
        readonly List<Work> landed = new List<Work>();
        readonly Stack<Work> pool = new Stack<Work>();
        readonly ByDistance byDistance = new ByDistance();
        readonly WorkByDistance workByDistance = new WorkByDistance();

        int lastCx = int.MinValue, lastCz = int.MinValue;
        bool pending, hooked;
        int maxInFlight;

        sealed class ByDistance : IComparer<Vector2Int>
        {
            public int cx, cz;
            public int Compare(Vector2Int a, Vector2Int b) =>
                (Mathf.Abs(a.x - cx) + Mathf.Abs(a.y - cz)).CompareTo(Mathf.Abs(b.x - cx) + Mathf.Abs(b.y - cz));
        }

        sealed class WorkByDistance : IComparer<Work>
        {
            public readonly ByDistance by = new ByDistance();
            public int Compare(Work a, Work b) => by.Compare(a.key, b.key);
        }

        void Awake() { Init(); }

        void Init()
        {
            quads = Mathf.RoundToInt(chunkSize / Slope.Quad);
            maxInFlight = Mathf.Clamp(SystemInfo.processorCount - 2, 1, 3);
            if (!hooked) { Slope.Changing += Settle; hooked = true; }
        }

        void OnDestroy()
        {
            Settle();
            if (hooked) { Slope.Changing -= Settle; hooked = false; }
        }

        void Start() { Refresh(true); }
        void Update() { Refresh(false); Look(); }

        /// <summary>Build everything around the target now (on a respawn or at start).</summary>
        public void Prewarm() { Refresh(true); }

        bool InView(Vector2Int k, int cx, int cz) =>
            k.x >= cx - sideChunks && k.x <= cx + sideChunks && k.y >= cz - behindChunks && k.y <= cz + aheadChunks;

        int LodFor(Vector2Int k, int cz) => k.y - cz >= farRows ? 1 : 0;

        void Refresh(bool all)
        {
            if (target == null) return;
            if (quads == 0) Init();
            int cx = Mathf.FloorToInt(target.position.x / chunkSize);
            int cz = Mathf.FloorToInt(target.position.z / chunkSize);
            // nothing to do until the rider crosses into a new cell, or while chunks are still being built
            if (!all && cx == lastCx && cz == lastCz && !pending) return;
            long t0 = PerfMarks.Now;
            bool moved = cx != lastCx || cz != lastCz;
            lastCx = cx; lastCz = cz;

            if (moved || all)
            {
                wanted.Clear(); ring.Clear();
                for (int dz = -behindChunks; dz <= aheadChunks + 1; dz++)
                    for (int dx = -sideChunks - 1; dx <= sideChunks + 1; dx++)
                    {
                        var k = new Vector2Int(cx + dx, cz + dz);
                        if (InView(k, cx, cz)) wanted.Add(k); else ring.Add(k);
                    }
                // nearest first
                byDistance.cx = cx; byDistance.cz = cz;
                wanted.Sort(byDistance); ring.Sort(byDistance);

                drop.Clear();
                foreach (var kv in live)
                    if (kv.Key.x < cx - sideChunks - 1 || kv.Key.x > cx + sideChunks + 1 ||
                        kv.Key.y < cz - behindChunks - 1 || kv.Key.y > cz + aheadChunks + 1)
                        drop.Add(kv.Key);
                foreach (var k in drop) { EndGhost(k); live[k].SetActive(false); spare.Enqueue(live[k]); live.Remove(k); lodOf.Remove(k); lookOf.Remove(k); }
                // what has come into view (built ahead while it was just outside) is shown; what has left it stays
                // until it is dropped, as before
                foreach (var k in wanted) if (live.TryGetValue(k, out var go) && !go.activeSelf) go.SetActive(true);
            }

            // the crevasses and the lifts (with their hills, which are in the height function) are laid out here, on the
            // main thread, before any worker asks where they are
            _ = Course.Crevasses; _ = LiftLine.Lifts; Cliffs.LedgeOf(0, out _);

            if (all) BuildNearNow(cx, cz);

            // put in what the workers have finished, nearest first, while the frame's budget lasts
            Land(cx, cz, all ? double.MaxValue : frameBudgetMs);
            Schedule(cx, cz);
            pending = inFlight.Count > 0 || Missing(cx, cz);
            if (!all) PerfMarks.MarkIfOver("terrain chunks", t0);
        }

        /// <summary>Is anything in view or just outside it still to be built (or rebuilt at full resolution)?</summary>
        bool Missing(int cx, int cz)
        {
            foreach (var k in wanted) if (!live.ContainsKey(k) || lodOf[k] > LodFor(k, cz)) return true;
            foreach (var k in ring) if (!live.ContainsKey(k)) return true;
            return false;
        }

        void Schedule(int cx, int cz)
        {
            if (inFlight.Count >= maxInFlight) return;
            // the visible area first (missing, then too coarse for how near it now is), then the ring
            foreach (var k in wanted)
                if (!live.ContainsKey(k) && !inFlight.ContainsKey(k)) { Queue(k, LodFor(k, cz)); if (inFlight.Count >= maxInFlight) return; }
            foreach (var k in wanted)
                if (live.ContainsKey(k) && lodOf[k] > LodFor(k, cz) && !inFlight.ContainsKey(k)) { Queue(k, 0); if (inFlight.Count >= maxInFlight) return; }
            foreach (var k in ring)
                if (!live.ContainsKey(k) && !inFlight.ContainsKey(k)) { Queue(k, LodFor(k, cz)); if (inFlight.Count >= maxInFlight) return; }
        }

        Work Take(Vector2Int k, int lod)
        {
            var w = pool.Count > 0 ? pool.Pop() : new Work();
            w.key = k; w.lod = lod; w.chunkSize = chunkSize; w.quads = quads; w.done = false; w.error = null;
            return w;
        }

        static readonly WaitCallback runOnWorker = o => ((Work)o).Run();

        void Queue(Vector2Int k, int lod)
        {
            var w = Take(k, lod);
            inFlight[k] = w;
            ThreadPool.QueueUserWorkItem(runOnWorker, w);
        }

        void Land(int cx, int cz, double budgetMs)
        {
            if (inFlight.Count == 0) return;
            landed.Clear();
            foreach (var kv in inFlight) if (kv.Value.done) landed.Add(kv.Value);
            if (landed.Count == 0) return;
            // nearest first
            workByDistance.by.cx = cx; workByDistance.by.cz = cz;
            landed.Sort(workByDistance);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            int put = 0;
            foreach (var w in landed)
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (put >= buildsPerFrame && ms > budgetMs) break;
                inFlight.Remove(w.key);
                if (w.error != null)
                {
                    // never leave a hole in the mountain: build it here instead
                    Debug.LogException(w.error);
                    w.done = false; w.error = null; w.Run();
                    if (w.error != null) { Debug.LogException(w.error); pool.Push(w); continue; }
                }
                Put(w, cx, cz);
                put++;
            }
        }

        /// <summary>A finished chunk into the scene (or into the chunk it replaces), unless it is no longer wanted.</summary>
        void Put(Work w, int cx, int cz)
        {
            var k = w.key;
            bool keep = k.x >= cx - sideChunks - 1 && k.x <= cx + sideChunks + 1 && k.y >= cz - behindChunks - 1 && k.y <= cz + aheadChunks + 1;
            if (keep)
            {
                if (!live.TryGetValue(k, out var go))
                {
                    go = Take();
                    go.name = "Chunk " + k.x + "," + k.y;
                    live[k] = go;
                    go.SetActive(InView(k, cx, cz));
                    lookOf[k] = -1;
                }
                var mf = go.GetComponent<MeshFilter>();
                if (lodOf.TryGetValue(k, out int was) && was > w.lod && go.activeSelf && Farview.On && lookOf.TryGetValue(k, out int look) && look != Hidden)
                {
                    // rebuilt finer where it can be seen: the coarse one stays a moment and the two hand over
                    var old = mf.sharedMesh;
                    var fresh = spareMeshes.Count > 0 ? spareMeshes.Pop() : NewMesh();
                    w.Apply(fresh);
                    mf.sharedMesh = fresh;
                    StartGhost(k, go, old);
                }
                else w.Apply(mf.sharedMesh);
                lodOf[k] = w.lod;
            }
            pool.Push(w);
        }

        /// <summary>
        /// A full rebuild (a new mountain, a respawn) puts in only what is round the rider at once, the workers and this
        /// thread building it together; the far chunks stream in over the next frames, so PLAY AGAIN does not freeze.
        /// </summary>
        void BuildNearNow(int cx, int cz)
        {
            int budget = 48;
            Schedule(cx, cz);   // (the workers take the nearest few)
            foreach (var k in wanted)
            {
                if (budget <= 0) break;
                if (live.ContainsKey(k)) continue;
                budget--;
                if (inFlight.ContainsKey(k)) continue;
                var w = Take(k, LodFor(k, cz));
                w.Run();
                if (w.error != null) { Debug.LogException(w.error); pool.Push(w); continue; }
                Put(w, cx, cz);
            }
            // and wait for the workers' share of them
            while (true)
            {
                bool waiting = false;
                foreach (var kv in inFlight) if (!kv.Value.done) { waiting = true; break; }
                if (!waiting) break;
                Thread.Sleep(0);
            }
        }

        // ---------------------------------------------------------------- the far band (Farview)

        // how each chunk is drawn: plainly, with the band (the chunks it crosses, and those handing over), or not at all
        // (wholly past the band's outer edge: nothing of it would show)
        const int Plain = 0, Edge = 1, Hidden = 2;
        readonly Dictionary<Vector2Int, int> lookOf = new Dictionary<Vector2Int, int>();
        Material[] plainMats, edgeMats;
        Material[] PlainMats { get { if (plainMats == null) Mats(); return plainMats; } }
        /// <summary>The snow the near terrain is drawn with (for a lift top's unload wing to match it: SkiLift, liftlook).</summary>
        public Material NearSnow => snowMaterial != null ? PlainMats[0] : null;
        Material[] EdgeMats { get { if (edgeMats == null) Mats(); return edgeMats; } }

        void Mats()
        {
            var rock = RockFace.TerrainMaterial(snowMaterial.shader);
            // (the snow underfoot is drawn into the terrain's snow by place: Surfaces, SnowSurface.shader)
            plainMats = new[] { Surfaces.Terrain(snowMaterial, false), rock };
            // (the rock always lets the snow through pixel by pixel, so it takes the band as it is: RockLit.shader)
            edgeMats = new[] { Surfaces.Terrain(snowMaterial, true), rock };
        }

        /// <summary>Draw each chunk plainly, with the band, or not at all, for where the rider is now.</summary>
        void Look()
        {
            if (target == null || live.Count == 0) return;
            bool on = Farview.On;
            Vector3 p = target.position;
            const float Margin = 4f;   // (a chunk drawn plainly is clear of the band by this much: a frame's ride and more)
            foreach (var kv in live)
            {
                var go = kv.Value;
                if (!go.activeSelf) continue;
                var k = kv.Key;
                int want = Plain;
                if (on)
                {
                    float x0 = k.x * chunkSize - p.x, x1 = x0 + chunkSize, z0 = k.y * chunkSize - p.z, z1 = z0 + chunkSize;
                    float across = Mathf.Max(Mathf.Abs(x0), Mathf.Abs(x1)), acrossNear = x0 > 0f ? x0 : x1 < 0f ? -x1 : 0f;
                    if (acrossNear >= Farview.AcrossOut || z0 >= Farview.DownOut) want = Hidden;
                    else if (across > Farview.AcrossIn - Margin || z1 > Farview.DownIn - Margin) want = Edge;
                    if (GhostOf(k) >= 0) want = Edge;
                }
                if (lookOf.TryGetValue(k, out int now) && now == want) continue;
                lookOf[k] = want;
                var mr = go.GetComponent<MeshRenderer>();
                mr.enabled = want != Hidden;
                if (want != Hidden) mr.sharedMaterials = want == Edge ? EdgeMats : PlainMats;
            }
            StepGhosts();
        }

        // a chunk rebuilt finer, handing over from its coarse self: the coarse mesh drawn by a ghost for SwapTime
        class Ghost { public Vector2Int key; public GameObject go; public MeshRenderer chunk; public float t0; }
        readonly List<Ghost> ghosts = new List<Ghost>();
        readonly Stack<GameObject> spareGhosts = new Stack<GameObject>();
        readonly Stack<Mesh> spareMeshes = new Stack<Mesh>();
        MaterialPropertyBlock swapBlock;
        static readonly int SwapId = Shader.PropertyToID("_FarviewSwap");

        static Mesh NewMesh() { var m = new Mesh(); m.MarkDynamic(); return m; }

        int GhostOf(Vector2Int k)
        {
            for (int i = 0; i < ghosts.Count; i++) if (ghosts[i].key == k) return i;
            return -1;
        }

        void StartGhost(Vector2Int k, GameObject chunk, Mesh old)
        {
            EndGhost(k);
            GameObject go;
            if (spareGhosts.Count > 0) go = spareGhosts.Pop();
            else
            {
                go = new GameObject("Ghost");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            go.name = "Ghost " + k.x + "," + k.y;
            go.GetComponent<MeshFilter>().sharedMesh = old;
            var gr = go.GetComponent<MeshRenderer>();
            gr.sharedMaterials = EdgeMats;
            go.SetActive(true);
            var cr = chunk.GetComponent<MeshRenderer>();
            float t0 = Farview.Clock;
            if (swapBlock == null) swapBlock = new MaterialPropertyBlock();
            swapBlock.Clear(); swapBlock.SetVector(SwapId, new Vector4(t0, -1f, 0f, 0f)); gr.SetPropertyBlock(swapBlock);
            swapBlock.Clear(); swapBlock.SetVector(SwapId, new Vector4(t0, 1f, 0f, 0f)); cr.SetPropertyBlock(swapBlock);
            ghosts.Add(new Ghost { key = k, go = go, chunk = cr, t0 = t0 });
            // (drawn with the band until it is done: Look keeps it so)
            cr.enabled = true; cr.sharedMaterials = EdgeMats; lookOf[k] = Edge;
        }

        void EndGhost(Vector2Int k)
        {
            int i = GhostOf(k);
            if (i < 0) return;
            var g = ghosts[i];
            ghosts.RemoveAt(i);
            var mf = g.go.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) spareMeshes.Push(mf.sharedMesh);
            mf.sharedMesh = null;
            g.go.SetActive(false);
            spareGhosts.Push(g.go);
            if (g.chunk != null) g.chunk.SetPropertyBlock(null);
            lookOf[k] = -1;
        }

        void StepGhosts()
        {
            float now = Farview.Clock;
            for (int i = ghosts.Count - 1; i >= 0; i--)
                if (now - ghosts[i].t0 >= Farview.SwapTime || !Farview.On) EndGhost(ghosts[i].key);
        }

        /// <summary>How many chunks are drawn with the band, and how many hand over now (the test tools log it).</summary>
        public void BandStats(out int edge, out int hidden, out int handing)
        {
            edge = hidden = 0;
            foreach (var v in lookOf.Values) { if (v == Edge) edge++; else if (v == Hidden) hidden++; }
            handing = ghosts.Count;
        }

        GameObject Take()
        {
            if (spare.Count > 0) return spare.Dequeue();
            var go = new GameObject("Chunk");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = NewMesh();
            var mr = go.AddComponent<MeshRenderer>();
            // snow, and bare rock on the faces too steep to hold it (the second submesh: see Work.SplitRock)
            mr.sharedMaterials = PlainMats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go;
        }

        /// <summary>
        /// Before the mountain changes (Slope.Init, Course.ResetCaches): wait for the workers, who are reading it, and
        /// throw away what they built from the old one.
        /// </summary>
        void Settle()
        {
            foreach (var kv in inFlight) while (!kv.Value.done) Thread.Sleep(0);
            foreach (var kv in inFlight) pool.Push(kv.Value);
            inFlight.Clear();
        }

        /// <summary>A new mountain: throw away every chunk and build again around the target.</summary>
        public void Rebuild()
        {
            Settle();
            for (int i = ghosts.Count - 1; i >= 0; i--) EndGhost(ghosts[i].key);
            foreach (var kv in live) { kv.Value.SetActive(false); spare.Enqueue(kv.Value); }
            live.Clear(); lodOf.Clear(); lookOf.Clear();
            lastCx = lastCz = int.MinValue;
            Refresh(true);
        }

        /// <summary>One chunk built at full resolution into `mesh`, here and now (StreamPerf compares it with the old builder's).</summary>
        public void BuildForTest(Mesh mesh, Vector2Int key)
        {
            if (quads == 0) Init();
            _ = Course.Crevasses; _ = LiftLine.Lifts; Cliffs.LedgeOf(0, out _);
            var w = Take(key, 0);
            w.Run();
            if (w.error != null) throw w.error;
            w.Apply(mesh);
            pool.Push(w);
        }

        // ================================================================ one chunk's build, on any thread

        /// <summary>
        /// Everything one chunk's build needs, so several can be built at once on worker threads; reused from chunk to
        /// chunk. Run reads only the mountain's pure functions (Slope, Ridge, Cliffs, RockFace, the crevasses Course has
        /// already laid out); Apply, on the main thread, hands the result to a mesh.
        /// </summary>
        sealed class Work
        {
            public Vector2Int key;
            public int lod, quads;
            public float chunkSize;
            public volatile bool done;
            public System.Exception error;

            Vector3[] verts, norms;
            Vector2[] uvs;
            Color[] cols;
            float[] rocky;   // how bare each corner is (RockFace.Rockiness)
            float[] grid;    // heights with a border two deep
            bool[] holeMask;
            static int[] fullTris, farTris;

            readonly List<Vector3> allV = new List<Vector3>(), allN = new List<Vector3>();
            readonly List<Vector2> allUV = new List<Vector2>();
            readonly List<Color> allC = new List<Color>();
            readonly List<float> allR = new List<float>();
            readonly List<int> snowTris = new List<int>(), rockTris = new List<int>();
            readonly List<Vector4> tangents = new List<Vector4>();
            Vector3[] tan1 = new Vector3[0], tan2 = new Vector3[0];
            Bounds bounds;
            int[] sideCopy = new int[0];   // a corner's copy mapped across x (2i) or z (2i + 1), or -1
            const float Facet = 0.85f;   // how far a rock corner's normal leans from the smooth one to its own triangle's
            const float FacetTilt = 0.7f;   // how far each rock facet is tilted its own way

            // the vertices past the grid's own: a ridge crest's finer cut (Refine), or a far chunk's skirt
            bool[] fineQ;
            int[] fineAt;
            readonly List<Vector3> fineV = new List<Vector3>(), fineN = new List<Vector3>();
            readonly List<Vector2> fineUV = new List<Vector2>();
            readonly List<Color> fineC = new List<Color>();
            readonly List<float> fineR = new List<float>();
            readonly List<int> cut = new List<int>();

            public void Run()
            {
                try { Build(); }
                catch (System.Exception e) { error = e; }
                finally { done = true; }
            }

            void Build()
            {
                int n = quads + 1, g = n + 4;
                float q = Slope.Quad;
                float x0 = key.x * chunkSize, z0 = key.y * chunkSize;
                // sample once, with a border two deep, and take normals and shading from the samples:
                // about a ninth of the height calls of sampling each one separately
                if (grid == null || grid.Length != g * g) grid = new float[g * g];
                for (int j = 0; j < g; j++)
                    for (int i = 0; i < g; i++)
                        grid[j * g + i] = Slope.Height(x0 + (i - 2) * q, z0 + (j - 2) * q);

                if (verts == null || verts.Length != n * n)
                {
                    verts = new Vector3[n * n]; norms = new Vector3[n * n];
                    uvs = new Vector2[n * n]; cols = new Color[n * n]; rocky = new float[n * n];
                }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int c = (j + 2) * g + (i + 2);
                        float y = grid[c];
                        float hl = grid[c - 1], hr = grid[c + 1], hd = grid[c - g], hu = grid[c + g];
                        float x = x0 + i * q, z = z0 + j * q;
                        int v = j * n + i;
                        verts[v] = new Vector3(x, y, z);
                        norms[v] = new Vector3(-(hr - hl) / (2f * q), 1f, -(hu - hd) / (2f * q)).normalized;
                        uvs[v] = new Vector2(x / 9f, z / 9f);
                        // hollows go blue, crests catch the light
                        float lap = (hl + hr + hd + hu) * 0.25f - y;
                        float k = Mathf.Clamp(lap * 2.4f, -0.8f, 0.8f);
                        Color c0 = k > 0f ? Color.Lerp(Color.white, new Color(0.72f, 0.80f, 0.95f), k * 0.55f)
                                          : Color.Lerp(Color.white, new Color(1f, 0.98f, 0.94f), -k * 0.4f);
                        // the powder either side is a touch bluer than the groomed piste, so the line reads at speed
                        cols[v] = Color.Lerp(c0, c0 * new Color(0.90f, 0.94f, 1.0f), Slope.Rough(x, z));
                        // how bare the ground is at this corner (the rock and snow are drawn from it: see SplitRock), from
                        // the lie of the ground over 6 m rather than 3: the snow line follows the slope's shape, not each
                        // facet's, so it does not jump from one corner to the next (a jump cliff's short band excepted)
                        float hl2 = grid[c - 2], hr2 = grid[c + 2], hd2 = grid[c - 2 * g], hu2 = grid[c + 2 * g];
                        float wideY = new Vector3(-(hr2 - hl2) / (4f * q), 1f, -(hu2 - hd2) / (4f * q)).normalized.y;
                        float ny = Cliffs.OnJumpFace(x, z) || Cliffs.OnLedgeFace(x, z) || Slope.OnStepFace(x, z) || CliffBand.OnFace(x, z) ? norms[v].y : Mathf.Lerp(wideY, norms[v].y, 0.3f);
                        rocky[v] = RockFace.Rockiness(ny, x, z, (hl2 + hr2 + hd2 + hu2) * 0.25f - y);
                    }
                // never at half resolution near a crevasse (its lips and hairlines lie on the snow) or where a ridge's
                // crest is cut finer
                if (lod > 0 && (NearCrevasse(x0, z0) || FineHere())) lod = 0;
                if (lod > 0) BuildFar(n, g);
                else BuildFull(n);
                Tangents();
                Vector3 lo = allV[0], hi = allV[0];
                for (int i = 1; i < allV.Count; i++) { lo = Vector3.Min(lo, allV[i]); hi = Vector3.Max(hi, allV[i]); }
                bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            }

            bool NearCrevasse(float x0, float z0)
            {
                if (z0 > Course.GlacierBottom + 80f || z0 + chunkSize < Course.GlacierTop - 80f) return false;
                foreach (var c in Course.Crevasses)
                    if (c.x1 + 12f > x0 && c.x0 - 12f < x0 + chunkSize && c.z1 + 12f > z0 && c.z0 - 12f < z0 + chunkSize) return true;
                return false;
            }

            void BuildFull(int n)
            {
                float q = Slope.Quad;
                float x0 = key.x * chunkSize, z0 = key.y * chunkSize;
                bool fine = Refine(n);
                if (fullTris == null || fullTris.Length != quads * quads * 6) fullTris = GridTris(quads, n, 1);
                // where a crevasse opens, leave a hole: its walls and lips are their own meshes (CourseDressing)
                bool holes = false;
                if (z0 < Course.GlacierBottom + 20f && z0 + chunkSize > Course.GlacierTop - 20f)   // (only the glacier has crevasses)
                {
                    if (holeMask == null || holeMask.Length != n * n) holeMask = new bool[n * n];
                    for (int j = 0; j < n; j++)
                        for (int i = 0; i < n; i++)
                        {
                            bool h = Course.NearOpening(x0 + i * q, z0 + j * q, 0.8f * q);   // (any quad that touches an opening: see NearOpening)
                            holeMask[j * n + i] = h; holes |= h;
                        }
                }
                cut.Clear();
                if (holes || fine)
                {
                    for (int j = 0; j < quads; j++)
                        for (int i = 0; i < quads; i++)
                        {
                            int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                            if (holes && (holeMask[a] || holeMask[b] || holeMask[c] || holeMask[d])) continue;
                            if (fine && fineQ[(j + 1) * quads + i])
                            {
                                // strips across the quad, split corner to corner the same way as a whole one
                                int lo = fineAt[j * quads + i], hi = fineAt[(j + 1) * quads + i];
                                for (int s = 0; s < Ridge.Split; s++)
                                {
                                    int sa = s == 0 ? a : lo + s - 1, sb = s == Ridge.Split - 1 ? b : lo + s;
                                    int sc = s == 0 ? c : hi + s - 1, sd = s == Ridge.Split - 1 ? d : hi + s;
                                    cut.Add(sa); cut.Add(sc); cut.Add(sb); cut.Add(sb); cut.Add(sc); cut.Add(sd);
                                }
                                continue;
                            }
                            cut.Add(a); cut.Add(c); cut.Add(b); cut.Add(b); cut.Add(c); cut.Add(d);
                        }
                }
                else cut.AddRange(fullTris);
                SplitRock(verts, norms, uvs, cols, rocky, n * n, cut);
            }

            static int[] GridTris(int quads, int n, int step)
            {
                int m = quads / step, nn = m + 1;
                var t = new int[m * m * 6];
                int at = 0;
                for (int j = 0; j < m; j++)
                    for (int i = 0; i < m; i++)
                    {
                        int a = j * nn + i, b = a + 1, c = a + nn, d = c + 1;
                        t[at++] = a; t[at++] = c; t[at++] = b;
                        t[at++] = b; t[at++] = c; t[at++] = d;
                    }
                return t;
            }

            // a far chunk's corners: every other one of the full grid's, each exactly as the full chunk has it
            Vector3[] fv, fn; Vector2[] fuv; Color[] fc; float[] fr;

            /// <summary>
            /// Half resolution: every other corner (with the full grid's own normal, colour and rock at it), and a skirt
            /// down from each edge, as deep as the full grid's middle corner on that edge stands below this one's straight
            /// edge (and a little more), so a full chunk beside it never shows a gap.
            /// </summary>
            void BuildFar(int n, int g)
            {
                int m = quads / 2, nn = m + 1;
                if (fv == null || fv.Length != nn * nn) { fv = new Vector3[nn * nn]; fn = new Vector3[nn * nn]; fuv = new Vector2[nn * nn]; fc = new Color[nn * nn]; fr = new float[nn * nn]; }
                for (int j = 0; j < nn; j++)
                    for (int i = 0; i < nn; i++)
                    {
                        int s = (2 * j) * n + 2 * i, d = j * nn + i;
                        fv[d] = verts[s]; fn[d] = norms[s]; fuv[d] = uvs[s]; fc[d] = cols[s]; fr[d] = rocky[s];
                    }
                if (farTris == null || farTris.Length != m * m * 6) farTris = GridTris(quads, n, 2);
                cut.Clear(); cut.AddRange(farTris);
                fineV.Clear(); fineN.Clear(); fineUV.Clear(); fineC.Clear(); fineR.Clear();
                // the four edges: (start corner, step along, and whether its triangles wind the other way to face out)
                Skirt(n, nn, 0, 1, false);                  // the up-hill edge (z0), facing up the hill
                Skirt(n, nn, m * nn, 1, true);              // the down-hill edge, facing down it
                Skirt(n, nn, 0, nn, true);                  // the left edge (x0), facing left
                Skirt(n, nn, m, nn, false);                 // the right edge, facing right
                SplitRock(fv, fn, fuv, fc, fr, nn * nn, cut);
            }

            void Skirt(int n, int nn, int start, int step, bool flip)
            {
                int m = nn - 1, baseIdx = nn * nn + fineV.Count;
                for (int e = 0; e <= m; e++)
                {
                    // how far below this corner the full grid's middle corners either side of it stand off the straight edge
                    float drop = 0.3f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int e2 = e + side;
                        if (e2 < 0 || e2 > m) continue;
                        int a = start + e * step, b = start + e2 * step;
                        // the middle corner in the full grid: halfway between a and b
                        int ia = a % nn, ja = a / nn, ib = b % nn, jb = b / nn;
                        int mid = (ja + jb) * n + (ia + ib);   // (2*ja + 2*jb)/2 row, (2*ia + 2*ib)/2 column
                        float off = (fv[a].y + fv[b].y) * 0.5f - verts[mid].y;
                        drop = Mathf.Max(drop, Mathf.Abs(off) + 0.3f);
                    }
                    int at = start + e * step;
                    fineV.Add(fv[at] - new Vector3(0f, drop, 0f)); fineN.Add(fn[at]); fineUV.Add(fuv[at]); fineC.Add(fc[at]); fineR.Add(fr[at]);
                }
                for (int e = 0; e < m; e++)
                {
                    int ta = start + e * step, tb = start + (e + 1) * step, ba = baseIdx + e, bb = baseIdx + e + 1;
                    if (!flip) { cut.Add(ta); cut.Add(tb); cut.Add(ba); cut.Add(tb); cut.Add(bb); cut.Add(ba); }
                    else { cut.Add(ta); cut.Add(ba); cut.Add(tb); cut.Add(tb); cut.Add(ba); cut.Add(bb); }
                }
            }

            /// <summary>
            /// Sorts the triangles into snow and rock, by how bare each corner is: a triangle with any snow in it is
            /// drawn as snow, and one with any rock in it is drawn again over that as rock, which lets the snow through
            /// pixel by pixel (RockFace.TerrainMaterial), so the line between them wanders across the triangles rather
            /// than following their edges. A rock triangle gets its own copies of its corners, with the
            /// texture mapped straight onto the face (across and down it), so a cliff's rock is never smeared out the
            /// way the snow's top-down mapping would smear it. Their normals lean most of the way to the triangle's own,
            /// so each facet takes the light its own way: fractured rock, like the rock models', rather than a smooth
            /// grey sheet (a 165 m wall shaded smooth reads as a painted plane).
            /// </summary>
            void SplitRock(Vector3[] bv, Vector3[] bn, Vector2[] buv, Color[] bc, float[] br, int count, List<int> use)
            {
                allV.Clear(); allN.Clear(); allUV.Clear(); allC.Clear(); allR.Clear();
                for (int i = 0; i < count; i++) { allV.Add(bv[i]); allN.Add(bn[i]); allUV.Add(buv[i]); allC.Add(bc[i]); allR.Add(br[i]); }
                allV.AddRange(fineV); allN.AddRange(fineN); allUV.AddRange(fineUV); allC.AddRange(fineC); allR.AddRange(fineR);
                snowTris.Clear(); rockTris.Clear();
                int corners = allV.Count;
                if (sideCopy.Length < corners * 2) sideCopy = new int[corners * 2 + corners / 2];
                for (int i = 0; i < corners * 2; i++) sideCopy[i] = -1;
                for (int t = 0; t < use.Count; t += 3)
                {
                    int a = use[t], b = use[t + 1], c = use[t + 2];
                    float ra = allR[a], rb = allR[b], rc = allR[c];
                    // (the rock's own patches move the line up to about 0.4 either way: see RockLit.shader)
                    if (Mathf.Min(ra, Mathf.Min(rb, rc)) < 0.9f)
                    {
                        Vector3 sn = Vector3.Cross(allV[b] - allV[a], allV[c] - allV[a]);
                        float len = sn.magnitude;
                        if (len > 1e-12f && sn.y < SteepSnow * len)
                        {
                            // a steep face: the snow mapped across it, as the rock is (along the wall and up it), from
                            // corners shared with the next steep triangle mapped the same way, so a wall's snow runs on
                            // across its triangles without a seam; its normals stay the ground's own
                            int axis = Mathf.Abs(sn.x) >= Mathf.Abs(sn.z) ? 0 : 1;
                            for (int k = 0; k < 3; k++) snowTris.Add(SideCopy(use[t + k], axis));
                        }
                        else { snowTris.Add(a); snowTris.Add(b); snowTris.Add(c); }
                    }
                    if (Mathf.Max(ra, Mathf.Max(rb, rc)) < 0.12f) continue;
                    Vector3 va = allV[a], vb = allV[b], vc = allV[c];
                    Vector3 fnn = Vector3.Cross(vb - va, vc - va);
                    if (fnn.sqrMagnitude < 1e-12f) continue;
                    Vector3 face = fnn.normalized;
                    // and each facet tilted a little its own way (fixed by where it is, so a rebuilt chunk matches),
                    // so neighbours on a smooth face still break the light up like fractured granite
                    Vector3 mid = (va + vb + vc) / 3f;
                    int hx = Mathf.FloorToInt(mid.x * 2f), hz = Mathf.FloorToInt(mid.z * 2f);
                    face = (face + FacetTilt * new Vector3(Slope.Hash01(hx, hz, 71) - 0.5f, Slope.Hash01(hx, hz, 72) - 0.5f, Slope.Hash01(hx, hz, 73) - 0.5f)).normalized;
                    // project onto the plane the face mostly looks across: cliffs run down the hill, so mostly x
                    bool acrossX = Mathf.Abs(face.x) >= Mathf.Abs(face.z);
                    for (int k = 0; k < 3; k++)
                    {
                        int i = use[t + k];
                        Vector3 p = allV[i];
                        rockTris.Add(allV.Count);
                        allV.Add(p); allN.Add(Vector3.Lerp(allN[i], face, Facet).normalized); allC.Add(new Color(allN[i].y, 1f, 1f, allR[i]));   // (the smooth slope, for the rock's snow: RockLit)
                        allUV.Add(acrossX ? new Vector2(p.z, p.y) / RockFace.Tile : new Vector2(p.x, p.y) / RockFace.Tile);
                    }
                }
            }

            /// <summary>A copy of corner i with the snow's texture mapped across a face looking along x (axis 0) or z (1), at the scale from above.</summary>
            int SideCopy(int i, int axis)
            {
                int at = sideCopy[i * 2 + axis];
                if (at >= 0) return at;
                at = allV.Count;
                Vector3 p = allV[i];
                allV.Add(p); allN.Add(allN[i]); allC.Add(allC[i]); allR.Add(allR[i]);
                allUV.Add(axis == 0 ? new Vector2(p.z / 9f, p.y / 9f) : new Vector2(p.x / 9f, p.y / 9f));
                sideCopy[i * 2 + axis] = at;
                return at;
            }

            /// <summary>
            /// Where a ridge crest's rounded edge runs through the chunk, adds the vertices that cut its quads into
            /// strips (Ridge.Fine). On a row edge between two cut quads they sit on the true curve; on one shared with a
            /// whole quad they sit on that quad's straight edge, so the two meet without a crack (the same rule as
            /// Ridge.MeshHeight, which the rider stands on). Their normals are blended from the corners', as the whole
            /// quad's would be across it, so the shading runs on unchanged. False when there is nothing to cut.
            /// </summary>
            bool Refine(int n)
            {
                fineV.Clear(); fineN.Clear(); fineUV.Clear(); fineC.Clear(); fineR.Clear();
                if (!FineHere()) return false;
                if (fineAt == null || fineAt.Length != n * quads) fineAt = new int[n * quads];
                float q = Slope.Quad;
                int k = Ridge.Split;
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < quads; i++)
                    {
                        bool below = fineQ[j * quads + i], above = fineQ[(j + 1) * quads + i];
                        if (!below && !above) { fineAt[j * quads + i] = -1; continue; }
                        fineAt[j * quads + i] = n * n + fineV.Count;
                        int va = j * n + i, vb = va + 1;
                        Vector3 pa = verts[va], pb = verts[vb];
                        for (int s = 1; s < k; s++)
                        {
                            float t = s / (float)k, x = pa.x + t * q;
                            float y = below && above ? Slope.Height(x, pa.z) : Mathf.Lerp(pa.y, pb.y, t);
                            fineV.Add(new Vector3(x, y, pa.z));
                            fineN.Add(Vector3.Lerp(norms[va], norms[vb], t).normalized);
                            fineUV.Add(new Vector2(x / 9f, pa.z / 9f));
                            fineC.Add(Color.Lerp(cols[va], cols[vb], t));
                            fineR.Add(Mathf.Lerp(rocky[va], rocky[vb], t));
                        }
                    }
                return true;
            }

            /// <summary>Which quads a ridge's crest cuts finer, from one row below the chunk to one above it (fineQ); false if none.</summary>
            bool FineHere()
            {
                int i0 = key.x * quads, j0 = key.y * quads;
                float q = Slope.Quad;
                if (!Ridge.InCanyons((j0 - 1) * q) && !Ridge.InCanyons((j0 + quads + 1) * q)) return false;   // a ridge is far longer than a chunk
                if (fineQ == null || fineQ.Length != (quads + 2) * quads) fineQ = new bool[(quads + 2) * quads];
                bool any = false;
                for (int r = 0; r < quads + 2; r++)
                    for (int i = 0; i < quads; i++)
                        any |= fineQ[r * quads + i] = Ridge.Fine(i0 + i, j0 + r - 1);
                return any;
            }

            /// <summary>
            /// The tangents the snow's and rock's normal maps need, as Mesh.RecalculateTangents makes them (each corner's
            /// texture direction from each of its triangles, weighted by the triangle's angle at the corner, then square
            /// to its normal: MikkTSpace's way), but here off the main thread.
            /// </summary>
            void Tangents()
            {
                int nv = allV.Count;
                if (tan1.Length < nv) { tan1 = new Vector3[nv + nv / 4]; tan2 = new Vector3[tan1.Length]; }
                System.Array.Clear(tan1, 0, nv); System.Array.Clear(tan2, 0, nv);
                Accumulate(snowTris); Accumulate(rockTris);
                tangents.Clear();
                for (int i = 0; i < nv; i++)
                {
                    Vector3 nrm = allN[i], t = tan1[i];
                    Vector3 o = t - nrm * Vector3.Dot(nrm, t);
                    float len = o.magnitude;
                    if (len > 1e-20f) o /= len;
                    else
                    {
                        // (a corner no triangle uses: any direction square to the normal)
                        o = Vector3.Cross(nrm, Mathf.Abs(nrm.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
                    }
                    float w = Vector3.Dot(Vector3.Cross(nrm, t), tan2[i]) < 0f ? -1f : 1f;
                    tangents.Add(new Vector4(o.x, o.y, o.z, w));
                }
            }

            void Accumulate(List<int> tris)
            {
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    Vector3 v1 = allV[a], v2 = allV[b], v3 = allV[c];
                    Vector2 w1 = allUV[a], w2 = allUV[b], w3 = allUV[c];
                    float x1 = v2.x - v1.x, x2 = v3.x - v1.x, y1 = v2.y - v1.y, y2 = v3.y - v1.y, z1 = v2.z - v1.z, z2 = v3.z - v1.z;
                    float s1 = w2.x - w1.x, s2 = w3.x - w1.x, t1 = w2.y - w1.y, t2 = w3.y - w1.y;
                    float det = s1 * t2 - s2 * t1;
                    if (Mathf.Abs(det) < 1e-20f) continue;
                    float r = 1f / det;
                    var sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r).normalized;
                    var tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);
                    Vector3 ab = (v2 - v1).normalized, ac = (v3 - v1).normalized, bc = (v3 - v2).normalized;
                    tan1[a] += sdir * Angle(ab, ac); tan1[b] += sdir * Angle(-ab, bc); tan1[c] += sdir * Angle(-ac, -bc);
                    tan2[a] += tdir; tan2[b] += tdir; tan2[c] += tdir;
                }
            }

            static float Angle(Vector3 u, Vector3 v) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(u, v), -1f, 1f));

            /// <summary>The finished chunk into its mesh (main thread only).</summary>
            public void Apply(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(allV);
                mesh.SetNormals(allN);
                mesh.SetTangents(tangents);   // the snow's normal map needs them
                mesh.SetUVs(0, allUV);
                mesh.SetColors(allC);
                // always two submeshes, even with no rock: a renderer with more materials than submeshes draws the
                // last submesh again in each extra material
                mesh.subMeshCount = 2;
                mesh.SetTriangles(snowTris, 0, false);
                mesh.SetTriangles(rockTris, 1, false);
                mesh.bounds = bounds;
            }
        }
    }
}
