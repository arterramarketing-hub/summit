using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// (tube) What the snow tube looks like: a heavy-duty inflatable ring as the resorts' tubes are, made here in code.
    ///  - The ring: a torus pumped up round, a little flatter than a circle across, its bottom flattened where it sits on the
    ///    snow, and its top pressed down where the rider sits in it (deepest behind, at the back rest, and in front, under
    ///    the knees).
    ///  - The cover: deep ink nylon over its top half, in eight panels, a ripstop weave, each seam a groove with a line of
    ///    stitching either side and a hem stitched along each edge. Gold piping round its middle where it meets the
    ///    bottom (the board design's rail colour, when there is one: RiderBodyWalk.Wear paints it).
    ///  - The bottom half: thick black vinyl, dull, scuffed along the way it slides and greyed with snow wear.
    ///  - Two grab handles, one each side where the hands hold on: a loop of gold webbing stitched down at both ends, a
    ///    black rubber grip round its top.
    ///  - A tow rope: white braided cord from a gold ring at the front, lying coiled on the cover beside the knees, a
    ///    black toggle at its end.
    /// URP's Lit only, with a base texture and nothing else (no keywords: ShaderKeep); the textures are drawn once and kept.
    /// The rider faces the tube's +X; its sides are +-Z.
    /// </summary>
    public partial class RiderBody
    {
        public const float TubeR = 0.62f, TubeTubeR = 0.22f;   // the ring's radius to the middle of its tube, and the tube's own
        static readonly Color TubeCover = new Color(0.11f, 0.15f, 0.21f), TubeBottom = new Color(0.075f, 0.08f, 0.09f);
        static readonly Color TubeWebbing = new Color(0.76f, 0.62f, 0.33f), TubeStitch = new Color(0.93f, 0.92f, 0.88f);
        static Texture2D tubeCoverTex, tubeBottomTex, tubeWebTex;

        /// <summary>The ring, the piping, the handles and the tow rope, under `shape` (the frame the bounce squashes: RiderBodyTube).</summary>
        void BuildTubeLook(Transform shape, Shader sh, Material dark)
        {
            MakeTubeTextures();
            var cover = Mats.Lit(Color.white, 0.62f, sh, 0f, tubeCoverTex, new Vector2(8f, 1f));
            var bottom = Mats.Lit(Color.white, 0.22f, sh, 0f, tubeBottomTex, new Vector2(3f, 1f));
            var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(shape, false);
            ring.GetComponent<MeshFilter>().sharedMesh = TubeRing(56, 24);
            ring.GetComponent<MeshRenderer>().sharedMaterials = new[] { cover, bottom };
            // the piping round the outer edge (the accent: gold, or the board design's rail colour)
            tubeBand = Mats.Lit(TubeBandPlain, 0.7f, sh, 0.35f);
            var piping = new GameObject("Piping", typeof(MeshFilter), typeof(MeshRenderer));
            piping.transform.SetParent(shape, false);
            piping.GetComponent<MeshFilter>().sharedMesh = Piping(64, 8);
            piping.GetComponent<MeshRenderer>().sharedMaterial = tubeBand;
            // the handles: webbing loops, a grip round each
            var web = Mats.Lit(Color.white, 0.3f, sh, 0f, tubeWebTex, new Vector2(1f, 6f));
            var grip = Mats.Lit(new Color(0.06f, 0.06f, 0.07f), 0.35f, sh, 0f);
            for (int s = -1; s <= 1; s += 2) Handle(shape, s, web, grip);
            // the tow rope, coiled on the cover beside the right knee, from its ring at the front
            var rope = Mats.Lit(new Color(0.92f, 0.92f, 0.9f), 0.25f, sh, 0f);
            var metal = Mats.Lit(TubeBandPlain, 0.75f, sh, 0.8f);
            TowRope(shape, rope, metal, grip);
        }


        /// <summary>The ring's surface at (a round the ring, b round its tube: 0 out, pi/2 up): pumped round, sat on, sat in.</summary>
        static Vector3 TubePoint(float a, float b)
        {
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a), cb = Mathf.Cos(b), sb = Mathf.Sin(b);
            float r = TubeTubeR;
            float y = r * 0.93f * sb;                                     // a little flatter than round across
            // flattened underneath where it sits on the snow (eased into the curve)
            float floor = -r * 0.80f;
            if (y < floor + 0.02f) y = floor + 0.02f * Mathf.Exp((y - floor - 0.02f) / 0.02f);
            float across = r * cb;
            if (sb < 0f) across *= 1f + 0.06f * -sb;                      // (bulging out a little where it presses down)
            // pressed down where the rider sits: on its top, toward the hole, deepest behind and in front
            float top = Mathf.Clamp01(sb), inner = Mathf.Clamp01(-cb * 0.7f + 0.5f);
            float sag = 0.04f * top * top * inner * (0.55f + 0.45f * Mathf.Abs(ca));
            y -= sag;
            float rad = TubeR + across;
            return new Vector3(ca * rad, y + r, sa * rad);   // (its bottom on the snow at y 0, near enough)
        }

        // the cover runs from its outer edge (round its middle), over the top, to its inner edge; the black bottom the
        // rest, so a side view shows the two halves meeting at the piping, as a real tube's do
        const float CoverFrom = 0.02f * Mathf.PI, CoverTo = 1.02f * Mathf.PI;

        static Mesh TubeRing(int seg, int side)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var cov = new List<int>(); var bot = new List<int>();
            // two strips of the tube's cross-section: the cover's b range and the bottom's, each its own rows
            void Strip(float b0, float b1, int rows, List<int> tris)
            {
                int start = v.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    for (int j = 0; j <= rows; j++)
                    {
                        float b = Mathf.Lerp(b0, b1, j / (float)rows);
                        v.Add(TubePoint(a, b));
                        uv.Add(new Vector2(i / (float)seg, j / (float)rows));
                    }
                }
                for (int i = 0; i < seg; i++)
                    for (int j = 0; j < rows; j++)
                    {
                        int p = start + i * (rows + 1) + j, q = p + 1, r = p + rows + 1, s2 = r + 1;
                        tris.Add(p); tris.Add(q); tris.Add(r); tris.Add(q); tris.Add(s2); tris.Add(r);
                    }
            }
            Strip(CoverFrom, CoverTo, side, cov);
            Strip(CoverTo, CoverFrom + 2f * Mathf.PI, side / 2, bot);
            var m = new Mesh { name = "TubeRing" };
            m.SetVertices(v); m.SetUVs(0, uv);
            m.subMeshCount = 2; m.SetTriangles(cov, 0); m.SetTriangles(bot, 1);
            m.RecalculateNormals();
            // (the two strips meet in a seam: the normals there averaged, so the shading runs on across it)
            var nr = new List<Vector3>(); m.GetNormals(nr);
            var at = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 2000f), Mathf.RoundToInt(p.y * 2000f), Mathf.RoundToInt(p.z * 2000f));
            for (int i = 0; i < v.Count; i++) { var k = Key(v[i]); at[k] = at.TryGetValue(k, out var s) ? s + nr[i] : nr[i]; }
            for (int i = 0; i < v.Count; i++) nr[i] = at[Key(v[i])].normalized;
            m.SetNormals(nr);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The piping: a thin round cord along the cover's outer edge.</summary>
        static Mesh Piping(int seg, int side)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            const float pr = 0.012f;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 c = TubePoint(a, CoverFrom), outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= side; j++)
                {
                    float b = j / (float)side * Mathf.PI * 2f;
                    v.Add(c + outw * (pr * Mathf.Cos(b) + pr * 0.4f) + Vector3.up * pr * Mathf.Sin(b));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int p = i * (side + 1) + j, q = p + 1, r = p + side + 1, s = r + 1;
                    t.Add(p); t.Add(r); t.Add(q); t.Add(q); t.Add(r); t.Add(s);
                }
            var m = new Mesh { name = "TubePiping" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// A grab handle on side s (+1: +Z): a loop of webbing stitched down at both ends on the cover, standing up off it
        /// over the top of the tube where the hand holds on, with a rubber grip round its top.
        /// </summary>
        void Handle(Transform shape, int s, Material web, Material grip)
        {
            // where the hand holds on (the sit clip's own grip, near enough): over the ring's top at the rider's side
            float a = s * Mathf.PI / 2f;
            Vector3 Top(float da) { var p = TubePoint(a + da, Mathf.PI / 2f); return p; }
            var pts = new List<Vector3>(); var ups = new List<Vector3>();
            const int N = 14;
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                // along the ring, back to front either side of the grip, rising off the cover in the middle
                float da = Mathf.Lerp(-0.42f, 0.30f, t) * -s;   // (-x behind the hand .. +x in front of it)
                var p = Top(da) + Vector3.up * (0.006f + 0.09f * Mathf.Sin(Mathf.PI * Mathf.SmoothStep(0f, 1f, t)));
                pts.Add(p); ups.Add(new Vector3(Mathf.Cos(a + da), 0f, Mathf.Sin(a + da)));
            }
            var strap = new GameObject(s > 0 ? "HandleL" : "HandleR", typeof(MeshFilter), typeof(MeshRenderer));
            strap.transform.SetParent(shape, false);
            strap.GetComponent<MeshFilter>().sharedMesh = Ribbon(pts, ups, 0.046f, 0.0045f);
            strap.GetComponent<MeshRenderer>().sharedMaterial = web;
            // stitched down at both ends: a tab of webbing flat on the cover
            for (int e = 0; e <= N; e += N)
            {
                var tab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(tab.GetComponent<Collider>());
                tab.name = "Tab"; tab.transform.SetParent(shape, false);
                Vector3 at = pts[e], tabWay = (pts[e == 0 ? 1 : N - 1] - pts[e]); tabWay.y = 0f; tabWay.Normalize();
                tab.transform.localPosition = at + Vector3.up * 0.002f;
                tab.transform.localRotation = Quaternion.LookRotation(tabWay, Vector3.up);
                tab.transform.localScale = new Vector3(0.056f, 0.008f, 0.07f);
                tab.GetComponent<MeshRenderer>().sharedMaterial = web;
            }
            // the grip: a short rubber sleeve round the strap's top, along it
            Vector3 mid = pts[N / 2], along = (pts[N / 2 + 2] - pts[N / 2 - 2]).normalized;
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = "Grip"; g.transform.SetParent(shape, false);
            g.transform.localPosition = mid + Vector3.up * 0.004f;
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, along);
            g.transform.localScale = new Vector3(0.036f, 0.065f, 0.036f);
            g.GetComponent<MeshRenderer>().sharedMaterial = grip;
        }

        /// <summary>A flat strap along pts, its face toward ups, w wide and thick deep.</summary>
        static Mesh Ribbon(List<Vector3> pts, List<Vector3> ups, float w, float thick)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float len = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) len += Vector3.Distance(pts[i], pts[i - 1]);
                Vector3 along = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 face = Vector3.ProjectOnPlane(Vector3.up, along).normalized;
                Vector3 side = Vector3.Cross(along, face).normalized;
                // four corners round the strap's cross-section
                v.Add(pts[i] - side * w * 0.5f + face * thick); v.Add(pts[i] + side * w * 0.5f + face * thick);
                v.Add(pts[i] + side * w * 0.5f - face * thick); v.Add(pts[i] - side * w * 0.5f - face * thick);
                for (int k = 0; k < 4; k++) uv.Add(new Vector2(k < 2 ? k : 3 - k, len / w));
            }
            for (int i = 0; i < pts.Count - 1; i++)
                for (int k = 0; k < 4; k++)
                {
                    int a = i * 4 + k, b = i * 4 + (k + 1) % 4, c = a + 4, d = b + 4;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            var m = new Mesh { name = "Webbing" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>The tow rope: a gold ring at the front, white cord lying in a loose coil on the cover by the right knee, a toggle.</summary>
        void TowRope(Transform shape, Material rope, Material metal, Material toggle)
        {
            // the ring, stood up on the front of the cover
            var front = TubePoint(0f, 0.25f);
            var ringGo = new GameObject("TowRing", typeof(MeshFilter), typeof(MeshRenderer));
            ringGo.transform.SetParent(shape, false);
            ringGo.transform.localPosition = front + new Vector3(0.012f, 0f, 0f);
            ringGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(0f, 0f, -20f);
            ringGo.GetComponent<MeshFilter>().sharedMesh = Torus(0.028f, 0.006f, 20, 6);
            ringGo.GetComponent<MeshRenderer>().sharedMaterial = metal;
            // the cord: from the ring up over the front, round in a loose loop on the cover beside the right knee
            var path = new List<Vector3>();
            float[,] at = { { 0.00f, 0.30f }, { 0.08f, 0.85f }, { 0.22f, 1.30f }, { 0.42f, 1.55f }, { 0.62f, 1.25f }, { 0.66f, 0.80f },
                            { 0.52f, 0.62f }, { 0.34f, 0.85f }, { 0.30f, 1.20f }, { 0.40f, 1.50f }, { 0.55f, 1.62f }, { 0.72f, 1.50f } };
            var key = new List<Vector3>();
            for (int i = 0; i < at.GetLength(0); i++)
            {
                var p = TubePoint(-at[i, 0], at[i, 1]);   // (-a: toward -Z, the rider's right)
                var n = (p - new Vector3(Mathf.Cos(-at[i, 0]) * TubeR, TubeTubeR, Mathf.Sin(-at[i, 0]) * TubeR)).normalized;
                key.Add(p + n * 0.012f);
            }
            key[0] = ringGo.transform.localPosition + new Vector3(0.02f, 0.01f, 0f);
            for (int i = 0; i < key.Count - 1; i++)
                for (int k = 0; k < 6; k++)
                {
                    float t = k / 6f;
                    Vector3 p0 = key[Mathf.Max(0, i - 1)], p1 = key[i], p2 = key[i + 1], p3 = key[Mathf.Min(key.Count - 1, i + 2)];
                    path.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                }
            path.Add(key[key.Count - 1]);
            var cord = new GameObject("TowRope", typeof(MeshFilter), typeof(MeshRenderer));
            cord.transform.SetParent(shape, false);
            cord.GetComponent<MeshFilter>().sharedMesh = Cord(path, 0.0085f, 6);
            cord.GetComponent<MeshRenderer>().sharedMaterial = rope;
            // the toggle at its end
            var tg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(tg.GetComponent<Collider>());
            tg.name = "TowToggle"; tg.transform.SetParent(shape, false);
            Vector3 end = path[path.Count - 1], dir = (path[path.Count - 1] - path[path.Count - 3]).normalized;
            tg.transform.localPosition = end + dir * 0.03f;
            tg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.Cross(dir, Vector3.up).normalized);
            tg.transform.localScale = new Vector3(0.026f, 0.05f, 0.026f);
            tg.GetComponent<MeshRenderer>().sharedMaterial = toggle;
        }

        /// <summary>A round cord along a path.</summary>
        static Mesh Cord(List<Vector3> path, float r, int side)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 along = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 a = Vector3.Cross(along, Vector3.up); if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(along, Vector3.right);
                a.Normalize(); Vector3 b = Vector3.Cross(along, a);
                for (int j = 0; j <= side; j++) { float q = j / (float)side * Mathf.PI * 2f; v.Add(path[i] + (a * Mathf.Cos(q) + b * Mathf.Sin(q)) * r); }
            }
            for (int i = 0; i < path.Count - 1; i++)
                for (int j = 0; j < side; j++)
                {
                    int p = i * (side + 1) + j, q = p + 1, rr = p + side + 1, s = rr + 1;
                    t.Add(p); t.Add(q); t.Add(rr); t.Add(q); t.Add(s); t.Add(rr);
                }
            var m = new Mesh { name = "Cord" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- the textures (drawn once)

        static float Hash(int x, int y) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xffffff) / 16777215f; } }
        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        }

        static void MakeTubeTextures()
        {
            if (tubeCoverTex != null && tubeBottomTex != null && tubeWebTex != null) return;
            // the cover: one of its eight panels (u across the panel, round the ring; v from the outer edge over the top
            // to the inner edge): ripstop, a seam at each side with stitching either side, a stitched hem at each end
            const int W = 256, H = 256;
            var c = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    float k = 1f;
                    // the weave: a faint grid of heavier threads, and the fabric's own unevenness
                    if (x % 16 == 0 || y % 16 == 0) k *= 1.06f;
                    k *= 0.96f + 0.08f * Noise(x * 0.07f, y * 0.07f);
                    // the seam: a groove at the panel's edge, shaded either side of it
                    float e = Mathf.Min(u, 1f - u) * W;   // px from the seam
                    if (e < 2.5f) k *= 0.45f + 0.2f * e;
                    else if (e < 7f) k *= 0.88f + 0.02f * e;
                    var col = TubeCover * k;
                    // stitching: dashes either side of the seam, and along each hem
                    bool dashV = ((y / 5) % 2) == 0, dashU = ((x / 5) % 2) == 0;
                    if (Mathf.Abs(e - 10f) < 1.1f && dashV) col = Color.Lerp(col, TubeStitch, 0.85f);
                    float hem0 = v * H, hem1 = (1f - v) * H;
                    if ((Mathf.Abs(hem0 - 9f) < 1.1f || Mathf.Abs(hem1 - 9f) < 1.1f) && dashU) col = Color.Lerp(col, TubeStitch, 0.85f);
                    if (hem0 < 5f || hem1 < 5f) col *= 0.82f;   // (folded under at the hems)
                    col.a = 1f;
                    c[y * W + x] = col;
                }
            tubeCoverTex = Tex("TubeCover", W, H, c);
            // the bottom: black vinyl, scuffed along the way it slides, greyed with snow wear, worst underneath
            const int BW = 256, BH = 128;
            var b = new Color[BW * BH];
            for (int y = 0; y < BH; y++)
                for (int x = 0; x < BW; x++)
                {
                    float v = y / (float)BH;
                    float under = Mathf.Sin(v * Mathf.PI);   // (the middle of the strip is the very bottom)
                    float k = 0.92f + 0.12f * Noise(x * 0.05f, y * 0.11f);
                    // scuffs: thin pale streaks round the ring, long and broken
                    float s = Noise(x * 0.018f, y * 0.9f) * Noise(x * 0.1f + 7f, y * 0.35f);
                    float scuff = Mathf.Clamp01((s - 0.42f) * 5f) * (0.35f + 0.65f * under);
                    // snow wear: a frosted grey haze underneath
                    float wear = under * under * (0.5f + 0.5f * Noise(x * 0.03f + 3f, y * 0.06f));
                    var col = TubeBottom * k;
                    col = Color.Lerp(col, new Color(0.42f, 0.44f, 0.47f), 0.55f * scuff);
                    col = Color.Lerp(col, new Color(0.62f, 0.65f, 0.69f), 0.28f * wear);
                    col.a = 1f;
                    b[y * BW + x] = col;
                }
            tubeBottomTex = Tex("TubeBottom", BW, BH, b);
            // the webbing: a woven strap (u across it, v along it), stitched at both ends in a box with a cross
            const int WW = 32, WH = 64;
            var w = new Color[WW * WH];
            for (int y = 0; y < WH; y++)
                for (int x = 0; x < WW; x++)
                {
                    float k = ((x + y / 2) % 4 < 2 ? 1.04f : 0.94f) * (x < 2 || x > WW - 3 ? 0.8f : 1f);
                    var col = TubeWebbing * k; col.a = 1f;
                    if ((x == 6 || x == WW - 7) && (y / 3) % 2 == 0) col = Color.Lerp(col, TubeStitch, 0.7f);
                    w[y * WW + x] = col;
                }
            tubeWebTex = Tex("TubeWebbing", WW, WH, w);
        }

        static Texture2D Tex(string name, int w, int h, Color[] px)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels(px); t.Apply(true, true);   // (no longer readable: only the GPU's copy kept)
            return t;
        }
    }
}
