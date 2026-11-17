using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Streams the mountain in square chunks around a target, a couple per
    /// frame so building one never costs a dropped frame. Chunks sit on a
    /// fixed world grid, so nothing re-triangulates as the rider moves.
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
        public int buildsPerFrame = 1;

        readonly Dictionary<Vector2Int, GameObject> live = new Dictionary<Vector2Int, GameObject>();
        readonly Queue<GameObject> spare = new Queue<GameObject>();
        readonly List<Vector2Int> wanted = new List<Vector2Int>();
        readonly List<Vector2Int> drop = new List<Vector2Int>();

        int lastCx = int.MinValue, lastCz = int.MinValue, sortCx, sortCz;
        bool pending;
        System.Comparison<Vector2Int> byDistance;

        void Awake() { Init(); }

        void Init()
        {
            quads = Mathf.RoundToInt(chunkSize / Slope.Quad);
            byDistance = (a, b) =>
                (Mathf.Abs(a.x - sortCx) + Mathf.Abs(a.y - sortCz)).CompareTo(Mathf.Abs(b.x - sortCx) + Mathf.Abs(b.y - sortCz));
        }
        void Start() { Refresh(true); }
        void Update() { Refresh(false); }

        /// <summary>Build everything around the target now (on a respawn or at start).</summary>
        public void Prewarm() { Refresh(true); }

        void Refresh(bool all)
        {
            if (target == null) return;
            if (byDistance == null) Init();
            int cx = Mathf.FloorToInt(target.position.x / chunkSize);
            int cz = Mathf.FloorToInt(target.position.z / chunkSize);
            // nothing to do until the rider crosses into a new cell, or while chunks are still queued
            if (!all && cx == lastCx && cz == lastCz && !pending) return;
            lastCx = cx; lastCz = cz;

            wanted.Clear();
            for (int dz = -behindChunks; dz <= aheadChunks; dz++)
                for (int dx = -sideChunks; dx <= sideChunks; dx++)
                    wanted.Add(new Vector2Int(cx + dx, cz + dz));
            // nearest first
            sortCx = cx; sortCz = cz;
            wanted.Sort(byDistance);

            drop.Clear();
            foreach (var kv in live)
                if (kv.Key.x < cx - sideChunks - 1 || kv.Key.x > cx + sideChunks + 1 ||
                    kv.Key.y < cz - behindChunks - 1 || kv.Key.y > cz + aheadChunks + 1)
                    drop.Add(kv.Key);
            foreach (var k in drop) { live[k].SetActive(false); spare.Enqueue(live[k]); live.Remove(k); }

            // a full rebuild (a new mountain) builds only what is round the rider at once; the far chunks
            // stream in over the next frames, so PLAY AGAIN does not freeze
            int budget = all ? 48 : buildsPerFrame;
            pending = false;
            foreach (var k in wanted)
            {
                if (budget <= 0) { pending = true; break; }
                if (live.ContainsKey(k)) continue;
                live[k] = Build(k);
                budget--;
            }
        }

        GameObject Build(Vector2Int key)
        {
            GameObject go;
            Mesh mesh;
            if (spare.Count > 0)
            {
                go = spare.Dequeue();
                go.SetActive(true);
                mesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
            else
            {
                go = new GameObject("Chunk");
                go.transform.SetParent(transform, false);
                mesh = new Mesh();
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                // snow, and bare rock on the faces too steep to hold it (the second submesh: see SplitRock)
                mr.sharedMaterials = new[] { snowMaterial, RockFace.Material(snowMaterial.shader) };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
            go.name = "Chunk " + key.x + "," + key.y;
            Fill(mesh, key);
            return go;
        }

        Vector3[] verts, norms;
        Vector2[] uvs;
        Color[] cols;
        int[] tris;
        float[] grid;   // heights with a one-cell border, reused between chunks
        bool[] holeMask;
        readonly List<int> holed = new List<int>();

        readonly List<Vector3> allV = new List<Vector3>(), allN = new List<Vector3>();
        readonly List<Vector2> allUV = new List<Vector2>();
        readonly List<Color> allC = new List<Color>();
        readonly List<int> snowTris = new List<int>(), rockTris = new List<int>();
        int[] rockOf;

        // a ridge crest's rounded edge, cut finer (see Ridge.Fine): which quads, from one row below the chunk to one
        // above it (so the rows on its edges know both their quads), and the extra vertices along each quad's lower
        // edge, Split - 1 of them from fineAt (or -1 for none)
        bool[] fineQ;
        int[] fineAt;
        readonly List<Vector3> fineV = new List<Vector3>(), fineN = new List<Vector3>();
        readonly List<Vector2> fineUV = new List<Vector2>();
        readonly List<Color> fineC = new List<Color>();
        readonly List<int> cut = new List<int>();

        /// <summary>
        /// Sorts the triangles into snow and rock. A rock triangle gets its own copies of its corners, with the
        /// texture mapped straight onto the face (across and down it), so a cliff's rock is never smeared out the
        /// way the snow's top-down mapping would smear it. The copies keep the snow's smooth normals, so the
        /// light runs on unbroken across the snow line.
        /// </summary>
        void SplitRock(int[] use)
        {
            allV.Clear(); allN.Clear(); allUV.Clear(); allC.Clear();
            allV.AddRange(verts); allN.AddRange(norms); allUV.AddRange(uvs); allC.AddRange(cols);
            allV.AddRange(fineV); allN.AddRange(fineN); allUV.AddRange(fineUV); allC.AddRange(fineC);
            snowTris.Clear(); rockTris.Clear();
            int count = allV.Count;
            if (rockOf == null || rockOf.Length < count) rockOf = new int[count];
            for (int i = 0; i < count; i++) rockOf[i] = -1;
            for (int t = 0; t < use.Length; t += 3)
            {
                int a = use[t], b = use[t + 1], c = use[t + 2];
                Vector3 va = allV[a], vb = allV[b], vc = allV[c];
                Vector3 fn = Vector3.Cross(vb - va, vc - va);
                if (!RockFace.IsRock(fn, (va.x + vb.x + vc.x) / 3f, (va.z + vb.z + vc.z) / 3f))
                {
                    snowTris.Add(a); snowTris.Add(b); snowTris.Add(c);
                    continue;
                }
                for (int k = 0; k < 3; k++)
                {
                    int i = use[t + k];
                    if (rockOf[i] < 0)
                    {
                        rockOf[i] = allV.Count;
                        Vector3 p = allV[i], nn = allN[i];
                        allV.Add(p); allN.Add(nn); allC.Add(Color.white);
                        // project onto the plane the face mostly looks across: cliffs run down the hill, so mostly x
                        allUV.Add(Mathf.Abs(nn.x) >= Mathf.Abs(nn.z) ? new Vector2(p.z, p.y) / RockFace.Tile : new Vector2(p.x, p.y) / RockFace.Tile);
                    }
                    rockTris.Add(rockOf[i]);
                }
            }
        }

        /// <summary>
        /// Where a ridge crest's rounded edge runs through the chunk, adds the vertices that cut its quads into
        /// strips (Ridge.Fine). On a row edge between two cut quads they sit on the true curve; on one shared with a
        /// whole quad they sit on that quad's straight edge, so the two meet without a crack (the same rule as
        /// Ridge.MeshHeight, which the rider stands on). Their normals are blended from the corners', as the whole
        /// quad's would be across it, so the shading runs on unchanged. False when there is nothing to cut.
        /// </summary>
        bool Refine(Vector2Int key, int n)
        {
            fineV.Clear(); fineN.Clear(); fineUV.Clear(); fineC.Clear();
            int i0 = key.x * quads, j0 = key.y * quads;
            float q = Slope.Quad;
            if (!Ridge.Has((j0 - 1) * q) && !Ridge.Has((j0 + quads + 1) * q)) return false;   // a ridge is far longer than a chunk
            if (fineQ == null || fineQ.Length != (quads + 2) * quads) { fineQ = new bool[(quads + 2) * quads]; fineAt = new int[n * quads]; }
            bool any = false;
            for (int r = 0; r < quads + 2; r++)
                for (int i = 0; i < quads; i++)
                    any |= fineQ[r * quads + i] = Ridge.Fine(i0 + i, j0 + r - 1);
            if (!any) return false;
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
                    }
                }
            return true;
        }

        /// <summary>A new mountain: throw away every chunk and build again around the target.</summary>
        public void Rebuild()
        {
            foreach (var kv in live) { kv.Value.SetActive(false); spare.Enqueue(kv.Value); }
            live.Clear();
            lastCx = lastCz = int.MinValue;
            Refresh(true);
        }

        void Fill(Mesh mesh, Vector2Int key)
        {
            int n = quads + 1, g = n + 2;
            float q = Slope.Quad;
            float x0 = key.x * chunkSize, z0 = key.y * chunkSize;
            // sample once, with a border, and take normals and shading from the samples:
            // about a ninth of the height calls of sampling each one separately
            if (grid == null || grid.Length != g * g) grid = new float[g * g];
            for (int j = 0; j < g; j++)
                for (int i = 0; i < g; i++)
                    grid[j * g + i] = Slope.Height(x0 + (i - 1) * q, z0 + (j - 1) * q);

            if (verts == null || verts.Length != n * n)
            {
                verts = new Vector3[n * n]; norms = new Vector3[n * n];
                uvs = new Vector2[n * n]; cols = new Color[n * n];
            }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int c = (j + 1) * g + (i + 1);
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
                }
            bool fine = Refine(key, n);
            if (tris == null)
            {
                tris = new int[quads * quads * 6];
                int t = 0;
                for (int j = 0; j < quads; j++)
                    for (int i = 0; i < quads; i++)
                    {
                        int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    }
            }
            // where a crevasse opens, leave a hole: its walls and lips are their own meshes (CourseDressing)
            int[] use = tris;
            bool holes = false;
            if (Course.At(z0) == Course.Section.Danger || Course.At(z0 + chunkSize) == Course.Section.Danger)
            {
                if (holeMask == null || holeMask.Length != n * n) holeMask = new bool[n * n];
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        bool h = Course.CrevasseCut(x0 + i * q, z0 + j * q) > 0.25f;
                        holeMask[j * n + i] = h; holes |= h;
                    }
            }
            if (holes || fine)
            {
                cut.Clear();
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
                use = cut.ToArray();
            }
            SplitRock(use);
            mesh.Clear();
            mesh.SetVertices(allV);
            mesh.SetNormals(allN);
            mesh.SetUVs(0, allUV);
            mesh.SetColors(allC);
            // always two submeshes, even with no rock: a renderer with more materials than submeshes draws the
            // last submesh again in each extra material
            mesh.subMeshCount = 2;
            mesh.SetTriangles(snowTris, 0);
            mesh.SetTriangles(rockTris, 1);
            mesh.RecalculateTangents();   // the snow's normal map needs them
            mesh.RecalculateBounds();
        }
    }
}
