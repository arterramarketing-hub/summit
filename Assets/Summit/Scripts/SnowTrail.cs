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

        struct Pt { public Vector3 p, r, n; public float s, edge, skid; }

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

        /// <summary>
        /// Across the track: snow, bright berm, shaded lip, packed floor, and back. The bottom row is a
        /// clean carve (a dark, polished cut); the top row a skid (a wide, scraped, lighter smear).
        /// </summary>
        static Texture2D Strip()
        {
            const int n = 64;
            var t = new Texture2D(n, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int row = 0; row < 2; row++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, e = Mathf.Abs(u - 0.5f) * 2f;   // 0 centre .. 1 edge
                    Color snow = new Color(0.93f, 0.95f, 1f), berm = new Color(1f, 1f, 1f);
                    Color lip = row == 0 ? new Color(0.55f, 0.64f, 0.86f) : new Color(0.72f, 0.78f, 0.92f);
                    Color floor = row == 0 ? new Color(0.38f, 0.49f, 0.80f) : new Color(0.66f, 0.73f, 0.90f);
                    Color col = e < 0.28f ? Color.Lerp(floor, lip, e / 0.28f)
                              : e < 0.6f ? Color.Lerp(lip, berm, (e - 0.28f) / 0.32f)
                              : Color.Lerp(berm, snow, (e - 0.6f) / 0.4f);
                    t.SetPixel(x, row, col);
                }
            t.Apply();
            return t;
        }

        float sEdge, sSkid;

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

        /// <summary>
        /// Called every frame with the board's contact point. travel: the direction you are moving
        /// (the track runs along it, not along the board); edge: -1..1 how far onto which edge
        /// (positive = turning right); skid: 0..1 how sideways the board is sliding.
        /// </summary>
        public void Feed(Vector3 at, Vector3 normal, float travel, bool onSnow, float scale, float edge, float skid, float dt)
        {
            if (!onSnow) { if (wasOn) Break(); wasOn = false; sEdge = 0f; sSkid = 0f; return; }
            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * 10f);
            sEdge = Mathf.Lerp(sEdge, Mathf.Clamp(edge, -1f, 1f), k);
            sSkid = Mathf.Lerp(sSkid, Mathf.Clamp01(skid), k);
            var dir = new Vector3(Mathf.Sin(travel), 0f, Mathf.Cos(travel));
            var right = Vector3.Cross(normal, Vector3.ProjectOnPlane(dir, normal).normalized);
            var pt = new Pt { p = at, r = right, n = normal, s = scale, edge = sEdge, skid = sSkid };
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
                float a = Mathf.Abs(q.edge), sk = q.skid;
                // carving: a narrow, clean cut with snow thrown up into a tall berm on the outside of the
                // turn; skidding: the board sweeps a wide, shallow smear and pushes a long low ridge
                float hw = (0.13f - 0.03f * a + 0.42f * sk) * q.s;
                float bwBase = 0.10f * q.s * (1f + 1.6f * sk);
                float bhBase = 0.03f * q.s;
                float outBh = bhBase * (1f + 2.4f * a + 1.2f * sk), inBh = bhBase * (1f - 0.65f * a) * (1f - 0.4f * sk);
                float outBw = bwBase * (1f + 0.9f * a), inBw = bwBase * (1f - 0.3f * a);
                float shelf = 0.035f * a * q.s;            // the floor slants up to the outside edge, like a carved shelf
                // which side is outside: turning right (edge > 0) throws snow to the left (-r)
                bool outLeft = q.edge >= 0f;
                float lBh = outLeft ? outBh : inBh, rBh = outLeft ? inBh : outBh;
                float lBw = outLeft ? outBw : inBw, rBw = outLeft ? inBw : outBw;
                float lSh = outLeft ? shelf : 0f, rSh = outLeft ? 0f : shelf;
                Vector3 o = q.p + q.n * 0.014f;
                int k = i * Across;
                float row = 0.25f + 0.5f * sk;
                v[k + 0] = o - q.r * (hw + lBw);
                v[k + 1] = o - q.r * (hw + lBw * 0.35f) + q.n * lBh;
                v[k + 2] = o - q.r * hw + q.n * (0.01f + lSh);
                v[k + 3] = o + q.n * (lSh + rSh) * 0.4f;
                v[k + 4] = o + q.r * hw + q.n * (0.01f + rSh);
                v[k + 5] = o + q.r * (hw + rBw * 0.35f) + q.n * rBh;
                v[k + 6] = o + q.r * (hw + rBw);
                for (int j = 0; j < Across; j++) c[k + j] = new Vector2(U[j], row);
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
