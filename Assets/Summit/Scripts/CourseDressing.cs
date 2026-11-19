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
        Material metal, boxSide, railMat, post, ice, lipBand, poleOrange, poleBlack, signPark, signDanger, signPipe, signBack, snow, signRidge, signGorge, signLedge;
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

            // danger: the crevasses themselves (snow lips, ice walls into the dark) and marker poles
            foreach (var cv in Course.CrevassesOf(k))
            {
                BuildCrevasse(root.transform, cv);
                float zm = (cv.z0 + cv.z1) * 0.5f;
                for (float z = cv.z0; z <= cv.z1; z += 9f)   // (a pole every 9 m down the long slots)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float cx = cv.x + (Slope.Centre(z) - Slope.Centre(zm)) + s * (cv.half + 3.0f);
                        Pole(root.transform, cx, z);
                    }
            }
            // the rider's own sphere never touches the crevasse ice or the park features' colliders (it rides the snow's
            // height and grinds a feature by its line, see Rider): only the thrown body and the board do
            if (target != null && target.TryGetComponent<Collider>(out var own)) own.excludeLayers |= 1 << IceLayer;

            // a sign at the start of each section, at the side of the piste, facing up the hill
            if (zb + Course.ParkAt + 180f < Course.ClearFrom) SignAt(root.transform, zb + Course.ParkAt - 30f, signPark, 1f);
            if (Course.CrevassesOf(k).Length > 0) SignAt(root.transform, zb + Course.DangerAt - 30f, signDanger, -1f);
            if (Course.PipeWeight(zb + (Course.PipeAt + Course.PipeEnd) * 0.5f) > 0f) SignAt(root.transform, zb + Course.PipeAt - 30f, signPipe, 1f);
            if (Ridge.Built(k))
            {
                var form = Ridge.FormOf(k);   // a ridge, a valley's gorge, or a ledge between a cliff and a wall
                SignAt(root.transform, zb + Ridge.At - 30f, form == Ridge.Form.Ledge ? signLedge : form == Ridge.Form.Valley || form == Ridge.Form.ValleyOneSided ? signGorge : signRidge, -1f);
                RidgeDressing.Build(root.transform, k, poleOrange, poleBlack);
            }
            CliffDressing.Build(root.transform, k, post.shader, poleOrange, poleBlack, post, signBack, target != null ? target.GetComponent<Rider>() : null);
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

        static float Jag(float z, int side, float open) =>
            open <= 0f ? 0f : 0.3f * (Slope.Noise(z * 1.3f + side * 17.1f, 77.7f) - 0.5f) * 2f * Mathf.Min(1f, open);

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
            float[] apron = { 0f, 0.25f, 0.7f, 1.4f, 2.7f };
            float[] band = { 0f, 0.25f, 0.7f, 1.3f, 2.2f };
            const int Wall = 8, ColWall = 4;
            float ua = cv.z0 - 2.6f, ub = cv.z1 + 2.6f;
            int rows = Mathf.CeilToInt((ub - ua) / step) + 1, crows = Mathf.CeilToInt((ub - ua) / colStep) + 1;
            var sv = new List<Vector3>(); var suv = new List<Vector2>(); var st = new List<int>();
            var bv = new List<Vector3>(); var buv = new List<Vector2>(); var bt = new List<int>();
            var iv = new List<Vector3>(); var iuv = new List<Vector2>(); var it = new List<int>();
            // the ice's own mapping for its shader: (metres below the lip, 0 at the lip to 1 at the bottom), and where
            // on the wall in metres (along the crevasse, up), from the crevasse's start so the numbers stay small
            var iflat = new List<Vector2>();
            float yRef = Slope.Surface(cv.x, ua);
            var cvv = new List<Vector3>(); var ct = new List<int>();
            // laid out along z (u) and across it in x (w)
            static Vector3 P(float u, float w, float y) => new Vector3(w, y, u);
            static float Surf(float u, float w) => Slope.Surface(w, u);
            var iBases = new int[2];
            for (int side = -1; side <= 1; side += 2)
            {
                int sBase = sv.Count, bBase = bv.Count, iBase = iv.Count;
                iBases[side < 0 ? 0 : 1] = iBase;
                bool pos = side > 0;
                for (int r = 0; r < rows; r++)
                {
                    float u = Mathf.Min(ua + r * step, ub);
                    float open = Course.Opening(cv, u, out float ctr);
                    float e = Mathf.Max(0f, open + Jag(u, side, open));
                    float depth = Course.CrevasseDepth * Mathf.Sqrt(Mathf.Max(0f, open) / cv.half) - 1.5f;
                    // the snow either side, laid over the terrain's ragged hole; the lip rounds down a little
                    for (int c = 0; c < apron.Length; c++)
                    {
                        float w = ctr + side * (e + apron[c]);
                        var p = P(u, w, Surf(u, w) + (c == 0 ? -0.06f : c == 1 ? 0.0f : 0.025f));
                        sv.Add(p); suv.Add(new Vector2(p.x / 9f, p.z / 9f));
                    }
                    // a darker band of wind-scoured, shadowed snow along the lip, just over the snow: you see the
                    // line of it from well up the hill (it fades out where the crevasse pinches shut)
                    float fade = Mathf.Clamp01(open / 0.8f);
                    for (int c = 0; c < band.Length; c++)
                    {
                        float w = ctr + side * (e + band[c]);
                        // (over whichever is higher, the true snow or the drawn terrain's facets, so it is never under them)
                        var q = new Vector2(w, u);
                        float top = c == 0 ? Surf(u, w) - 0.04f : Mathf.Max(Surf(u, w), Slope.DrawnHeight(q.x, q.y)) + 0.04f;
                        bv.Add(new Vector3(q.x, top, q.y)); buv.Add(new Vector2(0.5f, 1f - fade * (1f - c / (band.Length - 1f))));
                    }
                    // the snow face under the lip (overhanging a touch), then the ice down into the dark
                    float w0 = ctr + side * e, y0 = Surf(u, w0) - 0.06f;
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
                    Quads(bt, bBase + r * bc, bc, pos);
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
                    it.Add(a); it.Add(d); it.Add(b); it.Add(b); it.Add(d); it.Add(e);
                }
            }
            // the collider: one U per metre along it, down one wall, across the floor and up the other, facing in
            const int cc = 2 * (ColWall + 1);
            for (int r = 0; r < crows; r++)
            {
                float u = Mathf.Min(ua + r * colStep, ub);
                float open = Course.Opening(cv, u, out float ctr);
                float depth = Course.CrevasseDepth * Mathf.Sqrt(Mathf.Max(0f, open) / cv.half) - 1.5f;
                for (int s = 0; s < 2; s++)
                    for (int k = 0; k <= ColWall; k++)
                    {
                        int side = s == 0 ? -1 : 1;
                        float f = (s == 0 ? k : ColWall - k) / (float)ColWall;
                        float e = Mathf.Max(0f, open + Jag(u, side, open));
                        float w0 = ctr + side * e, y0 = Surf(u, w0) - 0.06f;
                        // (a hand's width inside the drawn ice, so a body against the wall lies on it, not in it)
                        float w = ctr + side * Mathf.Max(0f, Mathf.Lerp(e + 0.22f, open * 0.12f, Mathf.Pow(f, 0.75f)) - 0.1f);
                        float y = f == 0f ? y0 : y0 - lipDrop - Mathf.Max(0f, depth - lipDrop) * f;
                        cvv.Add(P(u, w, y));
                    }
            }
            for (int r = 0; r < crows - 1; r++) Quads(ct, r * cc, cc, true);
            MeshObject(root, "CrevasseSnow", sv, suv, st, snow, true);
            MeshObject(root, "CrevasseLip", bv, buv, bt, lipBand, false);
            MeshObject(root, "CrevasseIce", iv, iuv, it, ice, false, iflat);
            IceCollider(root, cvv, ct);
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
