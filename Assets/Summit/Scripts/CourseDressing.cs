using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Builds what you see of each course section near the rider: park boxes and rails,
    /// crevasse ice with marker poles, and a printed sign at the start of each section.
    /// Rebuilt one cycle ahead; older cycles are thrown away.
    /// </summary>
    public class CourseDressing : MonoBehaviour
    {
        public Transform target;
        Material metal, boxSide, railMat, post, ice, lipBand, bridgeSnow, lipDark, lipMid, hairDark, poleOrange, poleBlack, signPark, signDanger, signPipe, signBack, snow, signRidge, signGorge, signLedge;
        readonly Dictionary<int, GameObject> built = new Dictionary<int, GameObject>();
        readonly List<int> drop = new List<int>();

        public void Build(Shader lit, Material snowSurface)
        {
            snow = snowSurface;
            Material M(Color c, float smooth, float metallic = 0f, Color emit = default)
            {
                var m = new Material(lit);
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metallic);
                if (emit.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit); }
                return m;
            }
            metal = M(new Color(0.78f, 0.80f, 0.84f), 0.75f, 0.8f);
            boxSide = M(new Color(0.10f, 0.42f, 0.85f), 0.4f);
            railMat = M(new Color(0.85f, 0.86f, 0.9f), 0.85f, 0.9f);
            post = M(new Color(0.15f, 0.15f, 0.17f), 0.3f);
            ice = IceMaterial(lit);
            lipBand = LipMaterial(lit);
            bridgeSnow = BridgeMaterial(lit);
            // the lip, and the hairline a crack runs out into: the snow's own material (its grain, its grooming lines
            // carried on to the edge), only darker. A painted band fading to white read as a ring round a pool
            lipDark = Shade(snowSurface, 0.56f, 0.64f, 0.78f);
            lipMid = Shade(snowSurface, 0.80f, 0.85f, 0.93f);
            hairDark = Shade(snowSurface, 0.36f, 0.43f, 0.57f);
            poleOrange = M(new Color(1f, 0.42f, 0.05f), 0.4f);
            poleBlack = M(new Color(0.08f, 0.08f, 0.09f), 0.3f);
            signBack = M(new Color(0.35f, 0.36f, 0.4f), 0.3f);
            signPark = Sign(lit, "Signs/SignPark");
            signDanger = Sign(lit, "Signs/SignDanger");
            signPipe = Sign(lit, "Signs/SignPipe");
            signRidge = Sign(lit, "Signs/SignRidge");
            signGorge = Sign(lit, "Signs/SignGorge");
            signLedge = Sign(lit, "Signs/SignLedge");
        }

        static Material Shade(Material snowSurface, float r, float g, float b)
        {
            var m = new Material(snowSurface);
            var c = snowSurface.HasProperty("_BaseColor") ? snowSurface.GetColor("_BaseColor") : Color.white;
            m.SetColor("_BaseColor", new Color(c.r * r, c.g * g, c.b * b, 1f));
            return m;
        }

        public static Material Sign(Shader lit, string path)
        {
            // a painted board lit by the scene, that also shines the rider's headlamp back (Resources/SignLit.shader)
            if (signShader == null) signShader = Resources.Load<Shader>("SignLit");
            var m = new Material(signShader != null ? signShader : lit);
            var t = Resources.Load<Texture2D>(path);
            if (t != null) m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.25f);
            return m;
        }
        static Shader signShader;

        void Update()
        {
            if (target == null || metal == null) return;
            int c = Course.CycleOf(target.position.z);
            for (int k = Mathf.Max(0, c); k <= c + 1; k++)
                if (!built.ContainsKey(k)) built[k] = BuildCycle(k);
            drop.Clear();
            foreach (var kv in built) if (kv.Key < c - 0 && target.position.z > Course.First + (kv.Key + 1) * Course.Cycle + 80f) drop.Add(kv.Key);
            foreach (int k in drop) { FreeMeshes(built[k]); Destroy(built[k]); built.Remove(k); }
        }

        public void Prewarm() { Update(); }

        GameObject BuildCycle(int k)
        {
            var root = new GameObject("Course " + k);
            root.transform.SetParent(transform, false);
            float zb = Course.First + k * Course.Cycle;

            // park: boxes and rails, in short straight pieces following the snow (see Course.FeaturePoint)
            foreach (var f in Course.FeaturesOf(k))
            {
                bool box = f.kind == Course.Kind.Box;
                float len = f.Length;
                int n = Mathf.CeilToInt(len / (box ? 2f : 2.5f));
                for (int i = 0; i < n; i++)
                {
                    Vector3 p0 = Course.FeaturePoint(f, i / (float)n), p1 = Course.FeaturePoint(f, (i + 1f) / n);
                    Vector3 mid = (p0 + p1) * 0.5f, d = p1 - p0;
                    float seg = d.magnitude;
                    var rot = Quaternion.LookRotation(d / seg, Vector3.up);
                    if (box)
                    {
                        // a metal top the width of the box over a block that runs down into the snow
                        float w = 2f * f.halfWidth, hgt = f.height + 0.25f;
                        Block(PrimitiveType.Cube, root.transform, mid - Vector3.up * 0.05f, rot, new Vector3(w + 0.06f, 0.1f, seg + 0.03f), metal);
                        Block(PrimitiveType.Cube, root.transform, mid - Vector3.up * (0.1f + hgt * 0.5f), rot, new Vector3(w, hgt, seg + 0.03f), boxSide);
                        var bc = BodyCollider(root.transform, "BoxCollider", mid, rot).AddComponent<BoxCollider>();
                        bc.center = new Vector3(0f, -0.5f, 0f); bc.size = new Vector3(w, 1f, seg); bc.sharedMaterial = metalFeel;
                    }
                    else
                    {
                        // a thick round rail (the pieces overlap a little at the bends), on a post every other piece
                        Block(PrimitiveType.Cylinder, root.transform, mid, Quaternion.FromToRotation(Vector3.up, d / seg), new Vector3(0.16f, seg * 0.5f + 0.04f, 0.16f), railMat);
                        var cc = BodyCollider(root.transform, "RailCollider", mid, rot).AddComponent<CapsuleCollider>();
                        cc.direction = 2; cc.height = seg + 0.16f; cc.radius = 0.08f; cc.sharedMaterial = metalFeel;
                        if (i % 2 == 0 || i == n - 1)
                        {
                            Vector3 top = i == n - 1 ? Vector3.Lerp(p0, p1, 0.85f) : Vector3.Lerp(p0, p1, 0.15f);
                            float ground = Slope.Height(top.x, top.z) - 0.1f, hgt = top.y - ground;
                            Block(PrimitiveType.Cylinder, root.transform, new Vector3(top.x, ground + hgt * 0.5f - 0.05f, top.z), Quaternion.identity, new Vector3(0.1f, hgt * 0.5f, 0.1f), post);
                        }
                    }
                }
            }

            // the glacier: the crevasses themselves (snow lips, ice walls into the dark), their snow bridges, wands
            // either side of a bridge you can see (a hidden crack shows only its sag), and a warning sign up the hill
            // of each field of them and of the bergschrund
            var signed = new HashSet<int>();
            foreach (var cv in Course.CrevassesOf(k))
            {
                BuildCrevasse(root.transform, cv);
                BuildBridge(root.transform, cv, cv.b0, cv.b1);
                BuildBridge(root.transform, cv, cv.c0, cv.c1);
                int group = cv.kind == Course.CrevasseKind.Bergschrund ? -2 : cv.field;
                if (group != -1 && signed.Add(group))
                {
                    float top = float.MaxValue;
                    foreach (var o in Course.CrevassesOf(k)) if ((o.kind == Course.CrevasseKind.Bergschrund ? -2 : o.field) == group) top = Mathf.Min(top, o.z0);
                    float sz = top - 22f;
                    if (sz > Slope.PadLip + 12f && !Course.CrevasseNear(Slope.Centre(sz) - Slope.PisteHalf + 1f, sz, 3f)) SignAt(root.transform, sz, signDanger, -1f);
                }
            }
            // the rider's own sphere never touches the crevasse ice or the park features' colliders (it rides the snow's
            // height and grinds a feature by its line, see Rider): only the thrown body and the board do
            if (target != null && target.TryGetComponent<Collider>(out var own)) own.excludeLayers |= 1 << IceLayer;

            // a sign at the start of each section, at the side of the piste, facing up the hill
            if (zb + Course.ParkAt + 180f < Course.ClearFrom) SignAt(root.transform, zb + Course.ParkAt - 30f, signPark, 1f);
            if (Course.PipeWeight(zb + (Course.PipeAt + Course.PipeEnd) * 0.5f) > 0f) SignAt(root.transform, zb + Course.PipeAt - 30f, signPipe, 1f);
            if (Ridge.Built(k))
            {
                var form = Ridge.FormOf(k);   // a ridge, a valley's gorge, or a ledge between a cliff and a wall
                SignAt(root.transform, zb + Ridge.At - 30f, form == Ridge.Form.Ledge ? signLedge : form == Ridge.Form.Valley || form == Ridge.Form.ValleyOneSided ? signGorge : signRidge, -1f);
                RidgeDressing.Build(root.transform, k, poleOrange, poleBlack);
            }
            CliffDressing.Build(root.transform, k, post.shader, poleOrange, poleBlack, post, signBack, target != null ? target.GetComponent<Rider>() : null);
            SlowZones.Build(root.transform, k, post.shader);   // SLOW banners, here and there (they tear when ridden into)
            CliffSpires.Build(root.transform, k, post.shader);   // jagged rock along the cliffs' edges and faces
            return root;
        }

        /// <summary>The crevasse meshes are made here, so they are freed here (primitives share Unity's).</summary>
        static void FreeMeshes(GameObject g)
        {
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.StartsWith("Crevasse") && mf.sharedMesh != null) { if (Application.isPlaying) Destroy(mf.sharedMesh); else DestroyImmediate(mf.sharedMesh); }
            foreach (var mc in g.GetComponentsInChildren<MeshCollider>(true))
                if (mc.name.StartsWith("Crevasse") && mc.sharedMesh != null) { var m = mc.sharedMesh; mc.sharedMesh = null; if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
        }

        /// <summary>A rebuilt mountain: drop everything built for the old one.</summary>
        public void Rebuild()
        {
            foreach (var kv in built) if (kv.Value != null) { FreeMeshes(kv.Value); if (Application.isPlaying) Destroy(kv.Value); else DestroyImmediate(kv.Value); }
            built.Clear();
            Update();
        }

        // ---------------------------------------------------------------- crevasses

        /// <summary>The depth, in metres, of the parallax step on the ice walls' relief. 0 turns it off.</summary>
        public const float IceParallax = 0.15f;

        /// <summary>
        /// Glacier ice (Resources/IceWall.shader): a photo-scanned fractured ice surface, blue by depth, in annual
        /// layers, glossy, with a glow of its own. Without the shader or its textures, the plain ice below.
        /// </summary>
        static Material IceMaterial(Shader lit)
        {
            var sh = Resources.Load<Shader>("IceWall");
            var albedo = Resources.Load<Texture2D>("Ice/IceAlbedo");
            var normal = Resources.Load<Texture2D>("Ice/IceNormal");
            if (sh == null || albedo == null || normal == null) return PlainIceMaterial(lit);
            var m = new Material(sh) { name = "CrevasseIce" };
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_Parallax", IceParallax);
            return m;
        }

        /// <summary>
        /// Plain ice from the lip down: snow white, pale blue, a luminous blue, then deep blue, darkening only at the very
        /// bottom. It glows a little (light through the ice), and is only half glossy: a mirror-smooth wall deep in
        /// the dark showed the grey sky instead of its own blue.
        /// </summary>
        static Material PlainIceMaterial(Shader lit)
        {
            var t = new Texture2D(2, 128, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 128; y++)
            {
                float v = y / 127f;   // v = 0 at the lip, 1 at the bottom
                Color c = v < 0.07f ? new Color(0.95f, 0.97f, 1f)
                        : v < 0.2f ? Color.Lerp(new Color(0.80f, 0.92f, 0.98f), new Color(0.50f, 0.80f, 0.94f), (v - 0.07f) / 0.13f)
                        : v < 0.55f ? Color.Lerp(new Color(0.50f, 0.80f, 0.94f), new Color(0.22f, 0.52f, 0.80f), (v - 0.2f) / 0.35f)
                        : v < 0.85f ? Color.Lerp(new Color(0.22f, 0.52f, 0.80f), new Color(0.09f, 0.28f, 0.55f), (v - 0.55f) / 0.3f)
                        : Color.Lerp(new Color(0.09f, 0.28f, 0.55f), new Color(0.03f, 0.09f, 0.22f), (v - 0.85f) / 0.15f);
                t.SetPixel(0, y, c); t.SetPixel(1, y, c);
            }
            t.Apply();
            var m = new Material(lit);
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.5f);
            m.EnableKeyword("_EMISSION");                       // light through the ice: a blue glow, even in the shade
            m.SetTexture("_EmissionMap", t);
            m.SetColor("_EmissionColor", new Color(0.30f, 0.46f, 0.58f));
            m.SetFloat("_Cull", 0f);                            // seen from above and from inside
            return m;
        }

        /// <summary>
        /// How far a drawn lip stands off the crack's true edge at u: ragged at two scales, and broken back in blocks
        /// (the lip calves off in pieces a hand to a forearm deep). Only drawn: what you fall into is Course.Opening.
        /// </summary>
        static float Jag(Course.Crevasse cv, float u, int side, float open)
        {
            if (open <= 0f) return 0f;
            float k = Mathf.Min(1f, open);
            float n1 = (Slope.Noise(u * 1.3f + side * 17.1f + cv.phase, 77.7f) - 0.5f) * 2f;
            float n2 = (Slope.Noise(u * 4.3f + side * 5.3f + cv.phase * 2f, 31.3f) - 0.5f) * 2f;
            int block = Mathf.FloorToInt(u / (1.2f + 0.8f * Slope.Hash01(cv.id, side, 91)) + cv.phase);
            float blockOff = (Slope.Hash01(block, side + cv.id * 3, 92) - 0.5f) * 2f;
            return k * (0.2f * n1 + 0.08f * n2 + 0.1f * blockOff);
        }

        /// <summary>
        /// The drawn crack's middle line steps sideways now and then (en echelon: a crack opens as a run of short offset
        /// pieces); a few centimetres to a few tens, smoothed over half a metre. Only drawn, as Jag.
        /// </summary>
        static float Step(Course.Crevasse cv, float u, float open)
        {
            if (cv.kind == Course.CrevasseKind.Bergschrund) return 0f;
            float seg = 4f + 3f * Slope.Hash01(cv.id, 0, 93);
            float f = u / seg + cv.phase * 0.1f;
            int i = Mathf.FloorToInt(f);
            float a = (Slope.Hash01(i, cv.id, 94) - 0.5f) * 2f, b = (Slope.Hash01(i + 1, cv.id, 94) - 0.5f) * 2f;
            float t = Mathf.Clamp01((f - i - 0.9f) / 0.1f);   // (each piece holds its offset, then steps across in its last tenth)
            return Mathf.Min(1f, open) * 0.14f * Mathf.Lerp(a, b, t * t * (3f - 2f * t));
        }

        /// <summary>The ice walls' layer: the thrown body and board hit them, the rider's own sphere does not.</summary>
        public const int IceLayer = 4;
        static PhysicsMaterial iceFeel;

        /// <summary>
        /// A crevasse, laid out along its length (z) and across it (x):
        /// the snow lips either side over the terrain's ragged hole, a darker band at each lip that shows it from
        /// up the hill, the ice walls down into the dark, and a coarse collider of the walls and floor that the
        /// thrown body tumbles down against and comes to rest on.
        /// </summary>
        void BuildCrevasse(Transform root, Course.Crevasse cv)
        {
            const float step = 0.4f;
            const float lipDrop = 0.75f, colStep = 1f;
            float[] apron = { 0f, 0.25f, 0.7f, 1.4f, 2.7f, 4.2f };
            // the lip's darker snow: a narrow strip, darkest at the edge (lipDark) and a little lighter behind (lipMid)
            float[] band = { 0f, 0.16f, 0.42f };
            const int Wall = 8, ColWall = 4;
            float ua = -4f, ub = cv.len + 4f;
            int rows = Mathf.CeilToInt((ub - ua) / step) + 1, crows = Mathf.CeilToInt((ub - ua) / colStep) + 1;
            var sv = new List<Vector3>(); var suv = new List<Vector2>(); var st = new List<int>();
            var bv = new List<Vector3>(); var buv = new List<Vector2>(); var bt = new List<int>(); var bt2 = new List<int>();
            var iv = new List<Vector3>(); var iuv = new List<Vector2>(); var it = new List<int>();
            // the ice's own mapping for its shader: (metres below the lip, 0 at the lip to 1 at the bottom), and where
            // on the wall in metres (along the crevasse, up), from the crevasse's start so the numbers stay small
            var iflat = new List<Vector2>();
            float yRef = Slope.Surface(cv.ax, cv.az);
            var cvv = new List<Vector3>(); var ct = new List<int>();
            // laid out along the crack (u) and across it (w), at whatever angle it lies
            Vector3 P(float u, float w, float y) { var q = cv.Point(u, w); return new Vector3(q.x, y, q.y); }
            float Surf(float u, float w) { var q = cv.Point(u, w); return Slope.Surface(q.x, q.y); }
            var iBases = new int[2];
            for (int side = -1; side <= 1; side += 2)
            {
                int sBase = sv.Count, bBase = bv.Count, iBase = iv.Count;
                iBases[side < 0 ? 0 : 1] = iBase;
                bool pos = side < 0;   // (w runs to the left of the crack's direction: the mirror of x)
                // which side is up the hill: its lip stands a little proud (wind-packed, a touch overhanging), the
                // lower one sags in
                float upHill = (side * cv.dx) < 0f ? 1f : (side * cv.dx) > 0f ? 0f : 0.5f;   // (outward is side * (-dz, dx): its z is side * dx)
                for (int r = 0; r < rows; r++)
                {
                    float u = Mathf.Min(ua + r * step, ub);
                    float open = Course.Opening(cv, u, out float ctr);
                    ctr += Step(cv, u, open);
                    float e = Mathf.Max(0f, open + Jag(cv, u, side, open));
                    float lift = Mathf.Min(1f, open / 0.5f) * Mathf.Lerp(-0.12f, 0.11f, upHill);
                    float depth = cv.depth * Mathf.Sqrt(Mathf.Max(0f, open) / cv.half) - 1.5f;
                    // the snow either side, laid over the terrain's ragged hole: proud of the snow at the lip (raised up the
                    // hill, sagging down it), and tucked under the drawn terrain further out, so it shows only where the
                    // terrain has a hole (no seam, no ring)
                    for (int c = 0; c < apron.Length; c++)
                    {
                        float w = ctr + side * (e + apron[c]);
                        float dy = c == 0 ? lift - 0.04f : c == 1 ? lift * 0.5f : c == 2 ? -0.03f : c == 3 ? -0.06f : -0.09f;
                        var p = P(u, w, Surf(u, w) + dy);
                        sv.Add(p); suv.Add(new Vector2(p.x / 9f, p.z / 9f));
                    }
                    // the lip's darker snow, just over the snow, narrowing to nothing where the crack runs out
                    float fade = Mathf.Clamp01((open - 0.08f) / 0.45f);
                    for (int c = 0; c < band.Length; c++)
                    {
                        float w = ctr + side * (e + band[c] * fade);
                        var q = cv.Point(u, w);
                        float top = c == 0 ? Surf(u, w) + lift - 0.03f : Mathf.Max(Surf(u, w), Slope.DrawnHeight(q.x, q.y)) + 0.03f + (c == 1 ? lift * 0.5f : 0f);
                        bv.Add(new Vector3(q.x, top, q.y)); buv.Add(new Vector2(q.x / 9f, q.y / 9f));
                    }
                    // the snow face under the lip (overhanging a touch), then the ice down into the dark
                    float w0 = ctr + side * e, y0 = Surf(u, w0) + lift - 0.06f;
                    iv.Add(P(u, w0, y0)); iuv.Add(new Vector2(0f, 0f)); iflat.Add(new Vector2(u - ua, y0 - yRef));
                    iv.Add(P(u, w0 + side * 0.22f, y0 - lipDrop)); iuv.Add(new Vector2(lipDrop, 0.06f)); iflat.Add(new Vector2(u - ua, y0 - lipDrop - yRef));
                    for (int k = 1; k <= Wall; k++)
                    {
                        float f = k / (float)Wall;
                        float wob = (Slope.Noise(u * 0.8f + side * 5f, k * 1.7f + 3f) - 0.5f) * 0.7f * Mathf.Min(1f, open);
                        float w = ctr + side * (Mathf.Lerp(e + 0.22f, open * 0.12f, Mathf.Pow(f, 0.75f)) + wob);
                        float y = y0 - lipDrop - Mathf.Max(0f, depth - lipDrop) * f;
                        iv.Add(P(u, w, y)); iuv.Add(new Vector2(y0 - y, Mathf.Lerp(0.07f, 1f, f))); iflat.Add(new Vector2(u - ua, y - yRef));
                    }
                }
                int sc = apron.Length, bc = band.Length, ic = Wall + 2;
                for (int r = 0; r < rows - 1; r++)
                {
                    Quads(st, sBase + r * sc, sc, pos);
                    Quad(bt, bBase + r * bc, bc, pos);                // the dark edge
                    Quad(bt2, bBase + r * bc + 1, bc, pos);           // the lighter snow behind it
                    // facing into the crevasse, so the walls light from the inside
                    Quads(it, iBase + r * ic, ic, !pos);
                }
            }
            // the floor: the deep blue ice joining the walls' feet
            {
                int ic = Wall + 2;
                for (int r = 0; r < rows - 1; r++)
                {
                    int a = iBases[0] + r * ic + ic - 1, b = iBases[1] + r * ic + ic - 1, d = a + ic, e = b + ic;
                    it.Add(a); it.Add(b); it.Add(d); it.Add(b); it.Add(e); it.Add(d);
                }
            }
            // the collider: one U per metre along it, down one wall, across the floor and up the other, facing in
            const int cc = 2 * (ColWall + 1);
            for (int r = 0; r < crows; r++)
            {
                float u = Mathf.Min(ua + r * colStep, ub);
                float open = Course.Opening(cv, u, out float ctr);
                float depth = cv.depth * Mathf.Sqrt(Mathf.Max(0f, open) / cv.half) - 1.5f;
                for (int s = 0; s < 2; s++)
                    for (int k = 0; k <= ColWall; k++)
                    {
                        int side = s == 0 ? -1 : 1;
                        float f = (s == 0 ? k : ColWall - k) / (float)ColWall;
                        float ctrS = ctr + Step(cv, u, open);
                        float e = Mathf.Max(0f, open + Jag(cv, u, side, open));
                        float w0 = ctrS + side * e, y0 = Surf(u, w0) - 0.06f;
                        // (a hand's width inside the drawn ice, so a body against the wall lies on it, not in it)
                        float w = ctrS + side * Mathf.Max(0f, Mathf.Lerp(e + 0.22f, open * 0.12f, Mathf.Pow(f, 0.75f)) - 0.1f);
                        float y = f == 0f ? y0 : y0 - lipDrop - Mathf.Max(0f, depth - lipDrop) * f;
                        cvv.Add(P(u, w, y));
                    }
            }
            for (int r = 0; r < crows - 1; r++) Quads(ct, r * cc, cc, false);
            MeshObject(root, "CrevasseSnow", sv, suv, st, snow, true);
            MeshObject(root, "CrevasseLip", bv, buv, bt, lipDark, false);
            MeshObject(root, "CrevasseLipSnow", bv, buv, bt2, lipMid, false);
            Hairlines(root, cv);
            MeshObject(root, "CrevasseIce", iv, iuv, it, ice, false, iflat);
            IceCollider(root, cvv, ct);
        }

        /// <summary>
        /// A snow bridge over the span u0..u1 of a crack: snow from lip to lip, sagging along the crack's line (the sag
        /// and its faint grey line are what show a hidden crack), its broken ends falling away into the slot. Wands
        /// either side of each end, unless the bridge hides the whole crack.
        /// </summary>
        void BuildBridge(Transform root, Course.Crevasse cv, float u0, float u1)
        {
            if (u1 <= u0) return;
            const int Cols = 9;
            float step = 0.4f;
            int rows = Mathf.Max(2, Mathf.CeilToInt((u1 - u0) / step) + 1);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int r = 0; r < rows; r++)
            {
                float u = Mathf.Lerp(u0, u1, r / (rows - 1f));
                float open = Course.Opening(cv, u, out float mid);
                float reach = open + 0.45f, sag = 0.06f + 0.1f * Mathf.Min(1.5f, open);
                for (int c = 0; c < Cols; c++)
                {
                    float f = c / (Cols - 1f) * 2f - 1f;                  // -1 .. 1 across, lip to lip
                    var q = cv.Point(u, mid + f * reach);
                    float y = Slope.Surface(q.x, q.y) - sag * (1f - f * f) + 0.03f;
                    v.Add(new Vector3(q.x, y, q.y)); uv.Add(new Vector2(q.x / 9f, q.y / 9f));
                }
            }
            for (int r = 0; r < rows - 1; r++) Quads(t, r * Cols, Cols, false);   // (facing up: w runs to the crack's left)
            // its two broken ends: a face of snow down into the slot
            for (int end = 0; end < 2; end++)
            {
                int row = end == 0 ? 0 : rows - 1, b = v.Count;
                for (int c = 0; c < Cols; c++)
                {
                    var top = v[row * Cols + c];
                    v.Add(top); uv.Add(new Vector2(top.x / 9f, top.z / 9f));
                    v.Add(top + Vector3.down * 0.9f); uv.Add(new Vector2(top.x / 9f, (top.z + 0.9f) / 9f));
                }
                for (int c = 0; c < Cols - 1; c++)
                {
                    int a = b + c * 2, a2 = a + 2;
                    if (end == 0) { t.Add(a); t.Add(a2); t.Add(a + 1); t.Add(a2); t.Add(a2 + 1); t.Add(a + 1); }
                    else { t.Add(a); t.Add(a + 1); t.Add(a2); t.Add(a2); t.Add(a + 1); t.Add(a2 + 1); }
                }
            }
            MeshObject(root, "CrevasseBridge", v, uv, t, snow, true);
            // the sag's line down the middle: a thin grey thread, the one sign of a crack the bridge hides
            var lv = new List<Vector3>(); var luv = new List<Vector2>(); var lt = new List<int>();
            for (int r = 0; r < rows; r++)
            {
                float u = Mathf.Lerp(u0, u1, r / (rows - 1f));
                float open = Course.Opening(cv, u, out float mid);
                float sag = 0.06f + 0.1f * Mathf.Min(1.5f, open), half = 0.03f + 0.03f * Mathf.Min(1f, open);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    var q = cv.Point(u, mid + sd * half);
                    lv.Add(new Vector3(q.x, Slope.Surface(q.x, q.y) - sag + 0.045f, q.y)); luv.Add(new Vector2(q.x / 9f, q.y / 9f));
                }
            }
            for (int r = 0; r < rows - 1; r++) Quad(lt, r * 2, 2, false);
            MeshObject(root, "CrevasseBridgeLine", lv, luv, lt, hairDark, false);
            if (u1 - u0 > cv.len - 4f) return;   // a hidden crack: nothing marks it but its sag
            foreach (float u in new[] { u0, u1 })
            {
                Course.Opening(cv, u, out float mid);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    var q = cv.Point(u, mid + sd * (cv.half + 1.4f));
                    Pole(root, q.x, q.y);
                }
            }
        }

        /// <summary>A snow bridge's top: the snow's own white at its lips, a faint cool grey along the sag in its middle.</summary>
        static Material BridgeMaterial(Shader lit)
        {
            var t = new Texture2D(2, 64, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++)
            {
                float v = y / 63f;   // 0 along the middle of the sag, 1 at the lips (and 1 at its broken ends)
                Color c = Color.Lerp(new Color(0.60f, 0.67f, 0.78f), new Color(0.93f, 0.95f, 0.98f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.55f, v)));
                t.SetPixel(0, y, c); t.SetPixel(1, y, c);
            }
            t.Apply();
            var m = new Material(lit);
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.3f);
            m.SetFloat("_Cull", 0f);
            return m;
        }

        /// <summary>One quad: points a and a + 1 of a row, and the same two of the next row (`stride` on).</summary>
        static void Quad(List<int> t, int a, int stride, bool one)
        {
            int b = a + 1, d = a + stride, e = d + 1;
            if (one) { t.Add(a); t.Add(d); t.Add(b); t.Add(b); t.Add(d); t.Add(e); }
            else { t.Add(a); t.Add(b); t.Add(d); t.Add(b); t.Add(e); t.Add(d); }
        }

        /// <summary>
        /// The hairline a crack runs out into past each end: a dark thread a few centimetres wide, wandering on 2-7 m
        /// over the unbroken snow and fading to nothing (no hole: the snow is whole there).
        /// </summary>
        void Hairlines(Transform root, Course.Crevasse cv)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int end = 0; end < 2; end++)
            {
                float reach = 2f + 5f * Slope.Hash01(cv.id, end, 95);
                Course.Opening(cv, end == 0 ? 0f : cv.len, out float mid0);
                int n = Mathf.Max(3, Mathf.CeilToInt(reach / 0.3f)), b = v.Count;
                for (int i = 0; i <= n; i++)
                {
                    float f = i / (float)n, u = end == 0 ? -f * reach : cv.len + f * reach;
                    float wob = 0.25f * Mathf.Sin(u * 0.9f + cv.phase) * f + 0.08f * (Slope.Noise(u * 3.1f + cv.phase, 12.3f) - 0.5f);
                    float half = 0.045f * (1f - f) + 0.004f;
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        var q = cv.Point(u, mid0 + wob + sd * half);
                        v.Add(new Vector3(q.x, Mathf.Max(Slope.Surface(q.x, q.y), Slope.DrawnHeight(q.x, q.y)) + 0.025f, q.y));
                        uv.Add(new Vector2(q.x / 9f, q.y / 9f));
                    }
                }
                for (int i = 0; i < n; i++) Quad(t, b + i * 2, 2, end == 0);
            }
            MeshObject(root, "CrevasseHair", v, uv, t, hairDark, false);
        }

        /// <summary>The quads between one row of a strip and the next (rows of n points, from `a`), facing one way or the other.</summary>
        static void Quads(List<int> t, int a0, int n, bool one)
        {
            for (int c = 0; c < n - 1; c++)
            {
                int a = a0 + c, b = a + 1, d = a + n, e = d + 1;
                if (one) { t.Add(a); t.Add(d); t.Add(b); t.Add(b); t.Add(d); t.Add(e); }
                else { t.Add(a); t.Add(b); t.Add(d); t.Add(b); t.Add(e); t.Add(d); }
            }
        }

        static void IceCollider(Transform root, List<Vector3> v, List<int> t)
        {
            if (iceFeel == null)
                iceFeel = new PhysicsMaterial("CrevasseIce") { dynamicFriction = 0.35f, staticFriction = 0.6f, bounciness = 0.15f,
                                                                 frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Average };
            var g = new GameObject("CrevasseIceCollider") { layer = IceLayer };
            g.transform.SetParent(root, false);
            var mesh = new Mesh { name = "CrevasseIceCollider" };
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            var mc = g.AddComponent<MeshCollider>();
            mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices;
            mc.sharedMesh = mesh;
            mc.sharedMaterial = iceFeel;
        }

        /// <summary>The lip band: shadowed blue-grey snow at the lip, out to the snow's own white.</summary>
        static Material LipMaterial(Shader lit)
        {
            var t = new Texture2D(2, 64, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++)
            {
                float v = y / 63f;   // 0 at the lip, 1 at the outer edge (and where it fades out)
                Color c = Color.Lerp(new Color(0.09f, 0.16f, 0.27f), new Color(0.93f, 0.95f, 0.98f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 1f, v)));
                t.SetPixel(0, y, c); t.SetPixel(1, y, c);
            }
            t.Apply();
            var m = new Material(lit);
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.35f);
            return m;
        }

        static void MeshObject(Transform root, string name, List<Vector3> v, List<Vector2> uv, List<int> t, Material m, bool shadows, List<Vector2> uv1 = null)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root, false);
            var mesh = new Mesh();
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v); mesh.SetUVs(0, uv); if (uv1 != null) mesh.SetUVs(1, uv1); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        static PhysicsMaterial metalFeel => metalFeelM != null ? metalFeelM : (metalFeelM = new PhysicsMaterial("ParkMetal") { dynamicFriction = 0.3f, staticFriction = 0.4f, bounciness = 0.1f });
        static PhysicsMaterial metalFeelM;

        /// <summary>
        /// A holder for a park feature's collider: on the ice's layer, which the rider's own sphere ignores (it grinds
        /// the feature by its line, see Rider), so only a thrown body and board hit the rail or box.
        /// </summary>
        static GameObject BodyCollider(Transform root, string name, Vector3 at, Quaternion rot)
        {
            var g = new GameObject(name) { layer = IceLayer };
            g.transform.SetParent(root, false);
            g.transform.SetPositionAndRotation(at, rot);
            return g;
        }

        /// <summary>An orange marker pole on a holder at its foot, so it can be knocked over (Breakable).</summary>
        void Pole(Transform root, float x, float z)
        {
            float y = Slope.Height(x, z);
            var pole = new GameObject("CrevassePole").transform;
            pole.SetParent(root, false);
            pole.position = new Vector3(x, y, z);
            const float tall = 1.6f, h = tall * 0.5f;
            Block(PrimitiveType.Cylinder, pole, new Vector3(x, y + h, z), Quaternion.identity, new Vector3(0.06f, h, 0.06f), poleOrange);
            Block(PrimitiveType.Cylinder, pole, new Vector3(x, y + tall - 0.15f, z), Quaternion.identity, new Vector3(0.065f, 0.12f, 0.065f), poleBlack);
            Breakable.Flag(pole, tall - 0.03f);
        }

        void SignAt(Transform root, float z, Material face, float side)
        {
            float x = Slope.Centre(z) + side * (Slope.PisteHalf - 1f);
            float y = Slope.Height(x, z);
            const float W = 6f, H = 1.5f, lift = 2.2f;
            // on a holder of its own, so it can be broken (Breakable)
            var sign = new GameObject("SectionSign").transform;
            sign.SetParent(root, false);
            sign.position = new Vector3(x, y, z);
            var brk = Breakable.Sign(sign);
            for (int s = -1; s <= 1; s += 2)
                brk.Post(Block(PrimitiveType.Cylinder, sign, new Vector3(x + s * W * 0.45f, y + lift * 0.5f + 0.4f, z), Quaternion.identity, new Vector3(0.12f, (lift + 0.8f) * 0.5f, 0.12f), post).transform);
            var back = Block(PrimitiveType.Cube, sign, new Vector3(x, y + lift + H * 0.5f, z + 0.05f), Quaternion.identity, new Vector3(W + 0.15f, H + 0.15f, 0.08f), signBack);
            // the printed face looks up the slope at the rider (a quad shows its face to -Z)
            var q = Block(PrimitiveType.Quad, sign, new Vector3(x, y + lift + H * 0.5f, z - 0.01f), Quaternion.identity, new Vector3(W, H, 1f), face);
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            brk.Board(back.transform, q.transform);
            brk.Arm();
        }

        static GameObject Block(PrimitiveType t, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }
    }
}
