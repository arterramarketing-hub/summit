using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The board's track as real geometry: a packed, shadowed floor between two berms of
    /// pushed-up snow, so it reads as a groove cut into the slope and catches the sun.
    /// Built as a chain of small meshes; only the newest one is rebuilt as you ride.
    /// Deeper and wider in powder, harder-edged when carving.
    /// </summary>
    public class SnowTrail
    {
        const int PerChunk = 40, MaxChunks = 24, Across = 7;
        const float Step = 0.35f;

        struct Pt { public Vector3 p, r, n; public float s, berm; }

        readonly Material mat;
        readonly Transform root;
        readonly List<Pt> pts = new List<Pt>(PerChunk + 1);
        readonly Queue<GameObject> chunks = new Queue<GameObject>();
        readonly Queue<GameObject> spare = new Queue<GameObject>();
        GameObject current;
        Mesh currentMesh;
        bool wasOn;
        Vector3[] v = new Vector3[(PerChunk + 1) * Across];
        Vector2[] c = new Vector2[(PerChunk + 1) * Across];
        static readonly float[] U = { 0.02f, 0.2f, 0.36f, 0.5f, 0.64f, 0.8f, 0.98f };
        int[] tri;

        /// <summary>Across the track: snow, bright berm, shaded lip, packed blue floor, and back.</summary>
        static Texture2D Strip()
        {
            const int n = 64;
            var t = new Texture2D(n, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, e = Mathf.Abs(u - 0.5f) * 2f;   // 0 centre .. 1 edge
                Color snow = new Color(0.93f, 0.95f, 1f), berm = new Color(1f, 1f, 1f), lip = new Color(0.58f, 0.66f, 0.86f), floor = new Color(0.42f, 0.52f, 0.80f);
                Color col = e < 0.28f ? Color.Lerp(floor, lip, e / 0.28f)
                          : e < 0.6f ? Color.Lerp(lip, berm, (e - 0.28f) / 0.32f)
                          : Color.Lerp(berm, snow, (e - 0.6f) / 0.4f);
                t.SetPixel(x, 0, col); t.SetPixel(x, 1, col);
            }
            t.Apply();
            return t;
        }

        public SnowTrail(Shader lit)
        {
            // an opaque lit material; the colours across the track come from a small strip texture
            mat = new Material(lit);
            mat.SetTexture("_BaseMap", Strip());
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.3f);
            root = new GameObject("SnowTrail").transform;
            tri = new int[PerChunk * (Across - 1) * 6];
            int t = 0;
            for (int i = 0; i < PerChunk; i++)
                for (int j = 0; j < Across - 1; j++)
                {
                    int a = i * Across + j, b = a + 1, d = a + Across, e = d + 1;
                    tri[t++] = a; tri[t++] = d; tri[t++] = b;
                    tri[t++] = b; tri[t++] = d; tri[t++] = e;
                }
        }

        public void Clear()
        {
            while (chunks.Count > 0) Recycle(chunks.Dequeue());
            if (current != null) Recycle(current);
            current = null; pts.Clear(); wasOn = false;
        }

        void Recycle(GameObject g) { g.SetActive(false); spare.Enqueue(g); }

        /// <summary>Called every frame with the board's contact point.</summary>
        public void Feed(Vector3 at, Vector3 normal, float heading, bool onSnow, float scale, float carve)
        {
            if (!onSnow) { if (wasOn) Break(); wasOn = false; return; }
            var dir = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            var right = Vector3.Cross(normal, Vector3.ProjectOnPlane(dir, normal).normalized);
            var pt = new Pt { p = at, r = right, n = normal, s = scale, berm = 1f + carve * 0.8f };
            if (!wasOn || pts.Count == 0) { Begin(pt); wasOn = true; return; }
            float d = Vector3.Distance(pts[pts.Count - 1].p, at);
            if (d < Step) return;
            pts.Add(pt);
            if (pts.Count > PerChunk) { var last = pts[pts.Count - 1]; Seal(); Begin(pts.Count > 0 ? last : pt); return; }
            Rebuild();
        }

        void Begin(Pt first)
        {
            current = spare.Count > 0 ? spare.Dequeue() : NewChunk();
            current.SetActive(true);
            currentMesh = current.GetComponent<MeshFilter>().sharedMesh;
            currentMesh.Clear();
            pts.Clear();
            pts.Add(first);
        }

        void Break()
        {
            if (current != null && pts.Count > 1) Seal();
            else if (current != null) Recycle(current);
            current = null; pts.Clear();
        }

        void Seal()
        {
            Rebuild();
            chunks.Enqueue(current);
            while (chunks.Count > MaxChunks) Recycle(chunks.Dequeue());
            var keep = pts[pts.Count - 1];
            pts.Clear();
            pts.Add(keep);
            current = null;
        }

        GameObject NewChunk()
        {
            var g = new GameObject("Track");
            g.transform.SetParent(root, false);
            var m = new Mesh();
            m.MarkDynamic();
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            return g;
        }

        void Rebuild()
        {
            int n = pts.Count;
            if (n < 2 || currentMesh == null) return;
            for (int i = 0; i < n; i++)
            {
                var q = pts[i];
                float hw = 0.14f * q.s, bw = 0.10f * q.s, bh = 0.035f * q.s * q.berm;
                Vector3 o = q.p + q.n * 0.014f;
                int k = i * Across;
                float along = i * 0.1f;
                v[k + 0] = o - q.r * (hw + bw);
                v[k + 1] = o - q.r * (hw + bw * 0.35f) + q.n * bh;
                v[k + 2] = o - q.r * hw + q.n * 0.01f;
                v[k + 3] = o;
                v[k + 4] = o + q.r * hw + q.n * 0.01f;
                v[k + 5] = o + q.r * (hw + bw * 0.35f) + q.n * bh;
                v[k + 6] = o + q.r * (hw + bw);
                for (int j = 0; j < Across; j++) c[k + j] = new Vector2(U[j], along);
            }
            currentMesh.Clear();
            int vc = n * Across;
            currentMesh.SetVertices(v, 0, vc);
            currentMesh.SetUVs(0, c, 0, vc);
            currentMesh.SetTriangles(tri, 0, (n - 1) * (Across - 1) * 6, 0);
            currentMesh.RecalculateNormals();
            currentMesh.RecalculateBounds();
        }
    }
}
