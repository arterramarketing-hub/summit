using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The mountain beyond the streamed world: one coarse mesh of the very same heights (Slope.Height, every 24 m),
    /// from under the rider out to where the backdrop photograph takes over (about 1.5 km), lit and fogged like the
    /// terrain, with the forest's stands laid on as a dark tint where the trees grow (Forest.Cover) and bare rock
    /// where the ground is too steep to hold snow. There is always ground and forest out to the horizon; the streamed
    /// terrain, trees and everything on them only add detail over it as the band lets them in (Farview).
    ///
    /// Drawn by Resources/FarField.shader, a couple of metres under the true ground and after it, so wherever the
    /// terrain is drawn it hides this; inside the band's inner edge it discards itself outright (a coarse mesh would
    /// stand over a narrow gully's floor). Towards its far rim it dissolves into the photograph, which by then is in
    /// the same haze.
    ///
    /// One mesh, one draw call. The grid is fixed to the world, so rebuilding it round the rider (on a worker thread,
    /// every RebuildEvery metres) changes nothing where the old and new overlap: the swap is never seen.
    /// </summary>
    public class FarField : MonoBehaviour
    {
        public const float Cell = 24f;
        /// <summary>Its reach from where it is centred: either side, behind (up the hill) and ahead.</summary>
        public const float Side = 1488f, Behind = 480f, Ahead = 1680f;
        public const float RebuildEvery = 120f;
        /// <summary>How far under the true ground it lies (m).</summary>
        public const float Sink = 2.2f;

        public Transform target;
        Mesh[] meshes = new Mesh[2];
        int showing = -1;
        MeshFilter mf;
        MeshRenderer mr;
        Material mat;
        Vector2 builtAt = new Vector2(float.MaxValue, float.MaxValue);
        int builtSeed = int.MinValue;
        Job job;
        readonly Job[] jobs = { new Job(), new Job() };   // (reused: a rebuild allocates nothing)
        int nextJob;
        bool hooked;
        static readonly WaitCallback runOnWorker = o => ((Job)o).Run();

        public static FarField Create(Transform parent, Transform target, Material snow, Material foliage)
        {
            var go = new GameObject("FarField");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<FarField>();
            f.target = target;
            f.Setup(snow, foliage);
            return f;
        }

        void Setup(Material snow, Material foliage)
        {
            mf = gameObject.AddComponent<MeshFilter>();
            mr = gameObject.AddComponent<MeshRenderer>();
            var sh = Resources.Load<Shader>("FarField");
            if (sh == null || !sh.isSupported) { enabled = false; return; }
            mat = new Material(sh) { name = "FarField", renderQueue = (int)RenderQueue.AlphaTest + 10 };
            if (snow != null)
            {
                if (snow.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", snow.GetTexture("_BaseMap"));
                if (snow.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", snow.GetColor("_BaseColor"));
                if (snow.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", snow.GetFloat("_Smoothness"));
            }
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            for (int i = 0; i < 2; i++) meshes[i] = new Mesh { name = "FarField " + i };
            if (!hooked) { Slope.Changing += Settle; hooked = true; }
        }

        void OnDestroy()
        {
            Settle();
            if (hooked) { Slope.Changing -= Settle; hooked = false; }
            foreach (var m in meshes) if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
            if (mat != null) { if (Application.isPlaying) Destroy(mat); else DestroyImmediate(mat); }
        }

        /// <summary>Wait for a build that is reading the old mountain, and throw it away.</summary>
        void Settle()
        {
            if (job == null) return;
            while (!job.done) Thread.Sleep(0);
            job = null;
        }

        void Update() { Step(false); }

        /// <summary>Keep up with the rider; `now` builds it here and now (a new mountain, a test).</summary>
        public void Step(bool now)
        {
            if (target == null || mat == null) return;
            Vector3 p = target.position;
            var c = new Vector2(Mathf.Round(p.x / Cell) * Cell, Mathf.Round(p.z / Cell) * Cell);
            if (job != null && job.done)
            {
                if (job.error != null) Debug.LogException(job.error);
                else if (job.seed == Slope.Seed) Show(job);
                job = null;
            }
            bool stale = builtSeed != Slope.Seed || Mathf.Abs(c.x - builtAt.x) > RebuildEvery || Mathf.Abs(c.y - builtAt.y) > RebuildEvery;
            if (!stale) return;
            if (job != null)
            {
                if (!now && job.seed == Slope.Seed) return;   // (one under way already)
                Settle();
            }
            // what the heights read, laid out here on the main thread before a worker asks (as SlopeStreamer does)
            _ = Course.Crevasses; _ = LiftLine.Lifts;
            var j = jobs[nextJob]; nextJob = 1 - nextJob;
            j.cx = c.x; j.cz = c.y; j.seed = Slope.Seed; j.done = false; j.error = null;
            builtAt = c; builtSeed = Slope.Seed;
            if (now) { j.Run(); Show(j); return; }
            job = j;
            ThreadPool.QueueUserWorkItem(runOnWorker, j);
        }

        void Show(Job j)
        {
            int next = showing < 0 ? 0 : 1 - showing;
            var m = meshes[next];
            if (m.vertexCount != j.verts.Length) { m.Clear(); m.indexFormat = j.verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16; }
            m.SetVertices(j.verts); m.SetNormals(j.norms); m.SetColors(j.cols);
            m.SetTriangles(j.tris, 0, false);
            m.bounds = j.bounds;
            mf.sharedMesh = m;
            showing = next;
            Triangles = j.tris.Length / 3;
        }

        public int Triangles { get; private set; }

        sealed class Job
        {
            public float cx, cz;
            public int seed;
            public volatile bool done;
            public System.Exception error;
            public Vector3[] verts, norms;
            public Color32[] cols;
            public int[] tris;
            float[] h;
            public Bounds bounds;

            public void Run()
            {
                try { Build(); }
                catch (System.Exception e) { error = e; }
                finally { done = true; }
            }

            void Build()
            {
                int nx = Mathf.RoundToInt(2f * Side / Cell) + 1, nz = Mathf.RoundToInt((Behind + Ahead) / Cell) + 1;
                float x0 = cx - Side, z0 = cz - Behind;
                // heights with a border one deep, for the normals
                int gx = nx + 2, gz = nz + 2;
                if (h == null || h.Length != gx * gz) h = new float[gx * gz];
                for (int j = 0; j < gz; j++)
                    for (int i = 0; i < gx; i++)
                        h[j * gx + i] = Slope.Height(x0 + (i - 1) * Cell, z0 + (j - 1) * Cell);
                if (verts == null || verts.Length != nx * nz) { verts = new Vector3[nx * nz]; norms = new Vector3[nx * nz]; cols = new Color32[nx * nz]; }
                float lo = float.MaxValue, hi = float.MinValue;
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int g = (j + 1) * gx + (i + 1);
                        float x = x0 + i * Cell, z = z0 + j * Cell, y = h[g];
                        var n = new Vector3(-(h[g + 1] - h[g - 1]) / (2f * Cell), 1f, -(h[g + gx] - h[g - gx]) / (2f * Cell)).normalized;
                        // a hollow (the ground below its neighbours) holds shade; and the steeper the cell, the deeper
                        // the true ground can dip under a straight line between two corners: sink it further there
                        float lap = (h[g - 1] + h[g + 1] + h[g - gx] + h[g + gx]) * 0.25f - y;
                        float sink = Sink + Mathf.Max(0f, lap) * 0.6f + (1f - n.y) * 6f;
                        int v = j * nx + i;
                        verts[v] = new Vector3(x, y - sink, z);
                        norms[v] = n;
                        float rock = RockFace.Rockiness(n.y, x, z, Mathf.Max(0f, lap));
                        float cover = rock > 0.5f ? 0f : Forest.Cover(x, z);
                        cols[v] = new Color32((byte)(255f * Mathf.Clamp01(rock)), (byte)(255f * Mathf.Clamp01(cover)),
                                              (byte)(255f * Mathf.Clamp01(0.5f + lap * 0.02f)), 255);
                        lo = Mathf.Min(lo, y - sink); hi = Mathf.Max(hi, y);
                    }
                if (tris == null || tris.Length != (nx - 1) * (nz - 1) * 6) tris = new int[(nx - 1) * (nz - 1) * 6];
                int q = 0;
                for (int j = 0; j < nz - 1; j++)
                    for (int i = 0; i < nx - 1; i++)
                    {
                        int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                        // split along the shorter diagonal's lower side, as a gully would run
                        if (Mathf.Abs(verts[a].y - verts[d].y) < Mathf.Abs(verts[b].y - verts[c].y))
                        { tris[q++] = a; tris[q++] = c; tris[q++] = d; tris[q++] = a; tris[q++] = d; tris[q++] = b; }
                        else
                        { tris[q++] = a; tris[q++] = c; tris[q++] = b; tris[q++] = b; tris[q++] = c; tris[q++] = d; }
                    }
                var centre = new Vector3(cx, (lo + hi) * 0.5f, z0 + (Behind + Ahead) * 0.5f);
                bounds = new Bounds(centre, new Vector3(2f * Side, hi - lo + 2f, Behind + Ahead));
            }
        }
    }
}
