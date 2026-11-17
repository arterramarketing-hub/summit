using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The board's track as real geometry: a trench cut into the snow. A packed, shaded floor, steep
    /// inner walls, and on either side a ragged berm of snow the board has thrown up: lumpy, uneven
    /// in height and width, with clods broken off along its crest and a frayed outer edge, never the
    /// same twice. Carving throws a tall berm to the outside of the turn and slices a narrow cut;
    /// skidding sweeps a wide smear and a long low ridge. Deeper and wider in powder.
    /// Built as a chain of small meshes; only the newest one is rebuilt as you ride.
    /// </summary>
    public class SnowTrail
    {
        const int PerChunk = 56, MaxChunks = 24, Across = 15;
        const float Step = 0.25f;

        struct Pt { public Vector3 p, r, n; public float s, edge, skid, d; }

        readonly Material mat;
        readonly Transform root;
        readonly List<Pt> pts = new List<Pt>(PerChunk + 1);
        readonly Queue<GameObject> chunks = new Queue<GameObject>();
        readonly Queue<GameObject> spare = new Queue<GameObject>();
        GameObject current;
        Mesh currentMesh;
        bool wasOn;
        float travelled;
        Vector3[] v = new Vector3[(PerChunk + 1) * Across];
        Vector2[] c = new Vector2[(PerChunk + 1) * Across];
        // across the track (left to right): frayed edge, outer slope, clod, crest, lip, wall, floor ... and back
        static readonly float[] U = { 0.0f, 0.12f, 0.22f, 0.27f, 0.33f, 0.38f, 0.43f, 0.5f, 0.57f, 0.62f, 0.67f, 0.73f, 0.78f, 0.88f, 1.0f };
        int[] tri;

        /// <summary>
        /// Across the track: snow, bright thrown-up crest, shaded inner wall, packed floor, and back.
        /// Bottom row: a clean carve (dark, polished cut); top row: a skid (scraped, lighter smear).
        /// </summary>
        public static Texture2D Strip()
        {
            const int n = 128;
            var t = new Texture2D(n, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int row = 0; row < 2; row++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, e = Mathf.Abs(u - 0.5f) * 2f;   // 0 centre .. 1 edge
                    Color snow = new Color(0.90f, 0.92f, 0.97f), crest = new Color(0.97f, 0.98f, 1f);
                    // packed snow is shaded, not blue: only the faintest cool tint in the cut
                    Color wall = row == 0 ? new Color(0.84f, 0.87f, 0.93f) : new Color(0.88f, 0.90f, 0.95f);
                    Color floor = row == 0 ? new Color(0.74f, 0.78f, 0.86f) : new Color(0.83f, 0.86f, 0.92f);
                    Color col = e < 0.26f ? Color.Lerp(floor, wall, Mathf.SmoothStep(0f, 1f, e / 0.26f))
                              : e < 0.44f ? Color.Lerp(wall, crest, Mathf.SmoothStep(0f, 1f, (e - 0.26f) / 0.18f))
                              : Color.Lerp(crest, snow, (e - 0.44f) / 0.56f);
                    t.SetPixel(x, row, col);
                }
            t.Apply();
            return t;
        }

        float sEdge, sSkid;

        public SnowTrail(Shader lit)
        {
            mat = new Material(lit);
            mat.SetTexture("_BaseMap", Strip());
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.12f);   // matte, so every lump in the berm shades
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
            var pt = new Pt { p = at, r = right, n = normal, s = scale, edge = sEdge, skid = sSkid, d = travelled };
            if (!wasOn || pts.Count == 0) { Begin(pt); wasOn = true; return; }
            float d = Vector3.Distance(pts[pts.Count - 1].p, at);
            if (d < Step) return;
            travelled += d;
            pt.d = travelled;
            pts.Add(pt);
            if (pts.Count > PerChunk) { var last = pts[pts.Count - 1]; Seal(); Begin(last); return; }
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

        // ---- noise along the track, so the berms are lumpy and never repeat

        static float Hash(int i, int seed)
        {
            uint h = (uint)(i * 374761393 + seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
        }

        /// <summary>Smooth 1-D value noise, 0..1, features about `len` metres long.</summary>
        static float N(float d, float len, int seed)
        {
            float x = d / len;
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash(i, seed), Hash(i + 1, seed), f);
        }

        /// <summary>Lumps at three sizes, centred on 1: about 0.45 .. 1.6.</summary>
        static float Lumps(float d, int seed) =>
            0.35f + 0.55f * N(d, 1.9f, seed) + 0.45f * N(d, 0.6f, seed + 7) + 0.3f * N(d, 0.26f, seed + 13);

        /// <summary>A clod of snow broken off the crest: 0 most of the way, bumps up now and then.</summary>
        static float Clod(float d, int seed)
        {
            float x = d / 0.35f;
            int i = Mathf.FloorToInt(x);
            float h = Hash(i, seed);
            if (h < 0.55f) return 0f;
            float f = x - i;
            return (h - 0.55f) / 0.45f * Mathf.Sin(f * Mathf.PI);
        }

        void Rebuild()
        {
            int n = pts.Count;
            if (n < 2 || currentMesh == null) return;
            for (int i = 0; i < n; i++)
            {
                var q = pts[i];
                float a = Mathf.Abs(q.edge), sk = q.skid, s = q.s;
                // the cut: narrow when carving, a wide sweep when the board is sideways
                float hw = (0.13f - 0.03f * a + 0.42f * sk) * s;
                float bwBase = 0.13f * s * (1f + 1.6f * sk);
                float bhBase = 0.03f * s;   // low: seen from a low camera, taller berms read as walls
                // carving throws a tall berm to the outside of the turn; skidding a long low ridge either side
                float outBh = bhBase * (1f + 2.2f * a + 1.0f * sk), inBh = bhBase * (1f - 0.4f * a) * (1f - 0.3f * sk);
                float outBw = bwBase * (1f + 0.9f * a), inBw = bwBase * (1f - 0.25f * a);
                bool outLeft = q.edge >= 0f;   // turning right throws snow to the left (-r)
                float shelf = 0.03f * a * s;   // the floor slants up toward the outside edge

                Vector3 o = q.p + q.n * 0.012f;
                int k = i * Across;
                float row = 0.25f + 0.5f * sk;
                v[k + 7] = o + q.n * shelf * 0.4f;
                c[k + 7] = new Vector2(U[7], row);
                for (int side = -1; side <= 1; side += 2)
                {
                    bool outside = (side < 0) == outLeft;
                    int seed = side < 0 ? 11 : 37;
                    float lump = Lumps(q.d, seed), widen = Lumps(q.d + 17.3f, seed + 3), clod = Clod(q.d, seed + 5);
                    float bh = (outside ? outBh : inBh) * lump;
                    float bw = (outside ? outBw : inBw) * (0.65f + 0.5f * widen);
                    float sh = outside ? shelf : 0f;
                    float fray = (N(q.d, 0.3f, seed + 9) - 0.5f) * 0.16f * s;   // ragged outer edge
                    float jag = (N(q.d, 0.22f, seed + 15) - 0.5f) * 0.25f;          // the crest wanders in and out
                    Vector3 r = q.r * side;
                    // centre outward: floor edge, inner wall, lip, crest, clod, outer slope, frayed edge
                    var prof = new[]
                    {
                        (hw * 0.55f,                      0.008f + sh * 0.7f),
                        (hw * 0.86f,                      bh * 0.35f + sh),
                        (hw * 1.00f,                      bh * 0.80f + sh),
                        (hw + bw * (0.22f + jag),         bh * (1.00f + 0.6f * clod)),
                        (hw + bw * (0.42f + 0.2f * clod), bh * (0.70f + 0.9f * clod)),
                        (hw + bw * (0.70f - jag),         bh * (0.30f + 0.25f * N(q.d, 0.3f, seed + 31))),
                        (hw + bw * 1.05f + fray,          0.004f),
                    };
                    for (int j = 0; j < prof.Length; j++)
                    {
                        int col = side < 0 ? 6 - j : 8 + j;
                        v[k + col] = o + r * prof[j].Item1 + q.n * prof[j].Item2;
                        // a little colour jitter along the crest and slope, so the snow is not one flat tone
                        float jit = j >= 3 ? (N(q.d, 0.35f, seed + 21 + j) - 0.5f) * 0.05f : 0f;
                        c[k + col] = new Vector2(Mathf.Clamp01(U[col] + jit * -side), row);
                    }
                }
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
