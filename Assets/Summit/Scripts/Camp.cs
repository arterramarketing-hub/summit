using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Summit
{
    /// <summary>
    /// The summit camp: a little walkable camp on the plateau behind the start pad, where you land before
    /// every run. A log lodge, the daily-missions board, a tune-up bench (the upgrades shop), a board rack
    /// (outfits), a lookout over Mt. Vertigo and the Drop In gate down to the start. Decorations bought in
    /// the shop (fire pit, flags, lights, a snowman) appear here.
    ///
    /// Built from primitives and the textures in Resources/Camp (Tools/MakeCampSigns.swift). It also walks
    /// the rider around (thumb joystick, third-person camera) while the game is in its hub state.
    /// </summary>
    public class Camp : MonoBehaviour
    {
        public enum Station { None, Missions, Upgrades, Style, DropIn, Lookout }

        // where the camp sits: on the plateau behind the start pad (downhill is +Z), so the run is right
        // in front of you and the view down the mountain is past the Drop In gate
        public const float GateZ = -76f, BackZ = -126f, HalfWidth = 23f;
        static float Cx => Slope.Centre(Slope.PadLip);
        /// <summary>True inside the camp and its framing trees (the forest leaves this ground alone).</summary>
        public static bool InCamp(float x, float z) => z < -68f && z > BackZ - 16f && Mathf.Abs(x - Cx) < HalfWidth + 10f;

        public bool Active { get; private set; }
        public Station Near { get; private set; }
        public bool OnLookout { get; private set; }
        /// <summary>The camera shows the rider from the front (while picking an outfit).</summary>
        public bool Showcase;
        public const float MaxSpeed = 3.4f;

        Shader lit;
        Transform root;
        Material wood, logs, darkWood, roof, snowCap, stone, metal, glass, bulb, wire, ember, rope, carrot, coal, scarf, birch, path;
        readonly Material[] flagCols = new Material[5];
        Material[] noteMats;
        GameObject firePit, flags, lights, snowman;
        Renderer benchBoard;

        class Spot { public Station st; public Vector3 at; public float radius; public Transform root; }
        readonly List<Spot> spots = new List<Spot>();
        struct Box { public Vector2 c, h; public float cos, sin, top, drop; public GameObject owner; public bool floor; }
        readonly List<Box> boxes = new List<Box>();

        // ---------------------------------------------------------------- materials

        public void Build(Shader litShader)
        {
            lit = litShader;
            var woodTex = Resources.Load<Texture2D>("Camp/Wood");
            var logTex = Resources.Load<Texture2D>("Camp/Logs");
            wood = M(new Color(0.95f, 0.88f, 0.8f), 0.18f, woodTex);
            logs = M(new Color(1f, 0.92f, 0.85f), 0.12f, logTex, new Vector2(1f, 2f));
            darkWood = M(new Color(0.55f, 0.45f, 0.38f), 0.2f, woodTex);
            roof = M(new Color(0.28f, 0.20f, 0.17f), 0.15f, woodTex, new Vector2(2f, 3f));
            snowCap = M(new Color(0.92f, 0.95f, 1f), 0.35f);
            stone = M(new Color(0.52f, 0.52f, 0.55f), 0.1f);
            metal = M(new Color(0.35f, 0.37f, 0.4f), 0.75f); metal.SetFloat("_Metallic", 0.8f);
            // (emission is given in gamma space and raised per channel in linear: keep it near 1 and scale it)
            glass = M(new Color(0.25f, 0.2f, 0.14f), 0.2f, null, default, new Color(1f, 0.7f, 0.38f) * 1.45f);   // warm rooms behind the glass
            birch = M(Color.white, 0.15f, Resources.Load<Texture2D>("Camp/Birch"));
            path = Imprint(Resources.Load<Texture2D>("Camp/Bootprints"));
            bulb = M(new Color(0.3f, 0.27f, 0.2f), 0.6f, null, default, new Color(1f, 0.8f, 0.5f) * 1.4f);
            wire = M(new Color(0.08f, 0.08f, 0.09f), 0.3f);
            ember = M(new Color(1f, 0.4f, 0.1f), 0.3f, null, default, new Color(2.2f, 0.6f, 0.1f));
            rope = M(new Color(0.75f, 0.68f, 0.52f), 0.1f);
            carrot = M(new Color(0.98f, 0.45f, 0.08f), 0.3f);
            coal = M(new Color(0.05f, 0.05f, 0.06f), 0.5f);
            scarf = M(new Color(0.85f, 0.12f, 0.12f), 0.15f);
            Color[] fc = { new Color(0.15f, 0.35f, 0.85f), new Color(0.95f, 0.95f, 0.95f), new Color(0.85f, 0.15f, 0.12f), new Color(0.15f, 0.6f, 0.25f), new Color(0.98f, 0.8f, 0.12f) };
            for (int i = 0; i < fc.Length; i++) { flagCols[i] = M(fc[i], 0.1f); flagCols[i].SetFloat("_Cull", 0f); }
            noteMats = new Material[3];
            for (int i = 0; i < 3; i++) noteMats[i] = Sign("Camp/Note" + i);
            Progress.Changed += Redress;
            CaptureDay();
        }

        Material M(Color c, float smooth, Texture2D tex = null, Vector2 tile = default, Color emit = default)
        {
            var m = new Material(lit);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            if (tex != null) { m.SetTexture("_BaseMap", tex); if (tile != default) m.SetTextureScale("_BaseMap", tile); }
            if (emit.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emit);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m.enableInstancing = true;
            return m;
        }

        Material Sign(string path)
        {
            var m = CourseDressing.Sign(lit, path);
            m.SetFloat("_Smoothness", 0.15f);
            // a little self-light, so the carved signs still read in the evening
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
            m.SetColor("_EmissionColor", new Color(0.3f, 0.3f, 0.3f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        // ---------------------------------------------------------------- building

        /// <summary>Build the camp for the current mountain (call after Slope.Init; it sits on the snow).</summary>
        public void Rebuild()
        {
            if (lit == null) return;
            if (root != null) { if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject); }
            root = new GameObject("Camp").transform;
            root.SetParent(transform, false);
            spots.Clear(); boxes.Clear(); lamps.Clear();

            Lodge(Frame(-14f, -118f, 15f));
            Board(Frame(-11f, -99f, 90f));
            Bench(Frame(12f, -104f, -90f));
            Rack(Frame(-19.6f, -105.2f, 25f));
            Gate();
            Lookout(Frame(16f, -84f, 0f));
            Benches();
            firePit = FirePit(Frame(0f, -101f, 0f)).gameObject;
            snowman = Snowman(Frame(11.5f, -91f, 180f)).gameObject;
            flags = new GameObject("Flags"); flags.transform.SetParent(root, false);
            lights = new GameObject("Lights"); lights.transform.SetParent(root, false);
            Strings();
            Trees();
            Birches();
            Mounds();
            Footpath();
            Physics.SyncTransforms();
            Redress();
            SetNight(Night);   // the new lamps start at the evening's brightness
        }

        /// <summary>A building spot: on the snow at (dx from the run's centre, z), turned by yaw degrees.</summary>
        Transform Frame(float dx, float z, float yaw, Transform parent = null)
        {
            var t = new GameObject("Spot").transform;
            t.SetParent(parent != null ? parent : root, false);
            float x = Cx + dx;
            t.SetPositionAndRotation(new Vector3(x, Slope.DrawnHeight(x, z), z), Quaternion.Euler(0f, yaw, 0f));
            return t;
        }

        static Vector3 Ground(float dx, float z) { float x = Cx + dx; return new Vector3(x, Slope.DrawnHeight(x, z), z); }

        static Transform P(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material m, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (!shadows || type == PrimitiveType.Quad) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>A round log (or post) from a to b, in the parent's space.</summary>
        static Transform Log(Transform parent, Vector3 a, Vector3 b, float d, Material m)
        {
            var t = P(PrimitiveType.Cylinder, parent, (a + b) * 0.5f, new Vector3(d, Vector3.Distance(a, b) * 0.5f, d), Vector3.zero, m);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            return t;
        }

        /// <summary>A sign face (a quad turned to face +Z of its parent).</summary>
        Transform Face(Transform parent, Vector3 pos, float w, float h, Material m) =>
            P(PrimitiveType.Quad, parent, pos, new Vector3(w, h, 1f), new Vector3(0f, 180f, 0f), m);

        /// <summary>Somewhere the walker can't go: a box in the parent's frame (half sizes), or a floor it steps onto.</summary>
        void Block(Transform frame, Vector3 local, Vector2 half, GameObject owner = null, bool floor = false, float top = 0f, float drop = 0f)
        {
            Vector3 w = frame.TransformPoint(local);
            float yaw = frame.eulerAngles.y * Mathf.Deg2Rad;
            boxes.Add(new Box { c = new Vector2(w.x, w.z), h = half, cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw), owner = owner, floor = floor, top = frame.position.y + top, drop = drop });
        }

        /// <summary>A station: where you stand to use it, and a collider so a tap on it opens it.</summary>
        void Stand(Transform frame, Station st, Vector3 standLocal, float radius)
        {
            spots.Add(new Spot { st = st, at = frame.TransformPoint(standLocal), radius = radius, root = frame });
            var rs = frame.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var col = frame.gameObject.AddComponent<BoxCollider>();
            col.center = frame.InverseTransformPoint(b.center);
            var e = frame.InverseTransformVector(b.size);
            col.size = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
        }

        static Mesh gableMesh;
        /// <summary>A triangular wedge (base 1 wide on the floor, apex 1 up, 1 deep), for gable ends.</summary>
        static Mesh Gable()
        {
            if (gableMesh != null) return gableMesh;
            Vector3 a = new Vector3(-0.5f, 0f, 0.5f), b = new Vector3(0.5f, 0f, 0.5f), c = new Vector3(0f, 1f, 0.5f);
            Vector3 a2 = a - Vector3.forward, b2 = b - Vector3.forward, c2 = c - Vector3.forward;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Quad(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
            {
                int i = v.Count; v.Add(p); v.Add(q); v.Add(r); v.Add(s);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            void Tri(Vector3 p, Vector3 q, Vector3 r)
            {
                int i = v.Count; v.Add(p); v.Add(q); v.Add(r);
                foreach (var x in new[] { p, q, r }) uv.Add(new Vector2(x.x + 0.5f, x.y) * 2f);
                tri.AddRange(new[] { i, i + 1, i + 2 });
            }
            Tri(a, b, c); Tri(b2, a2, c2);
            Quad(a2, a, c, c2); Quad(b, b2, c2, c);
            gableMesh = new Mesh { name = "Gable" };
            gableMesh.SetVertices(v); gableMesh.SetUVs(0, uv); gableMesh.SetTriangles(tri, 0);
            gableMesh.RecalculateNormals(); gableMesh.RecalculateBounds();
            return gableMesh;
        }

        // the lodge: an A-frame chalet. A timber lower storey, a steep snowy roof down to a wraparound deck,
        // and a front gable that is all glass in a timber grid, glowing from inside
        const float LodgeHalf = 5f, LodgePitch = 57f, Eave = 1.9f;
        Vector3 lodgeApex, deckL, deckR, stairsFoot;

        void Lodge(Transform t)
        {
            t.name = "Lodge";
            // level on the highest snow under the house and its deck; a stone footing fills down to the snow
            float baseY = t.position.y;
            foreach (var c in new[] { new Vector2(-6.4f, -5.4f), new Vector2(7.8f, -5.4f), new Vector2(-6.4f, 7.9f), new Vector2(7.8f, 7.9f) })
            {
                var w = t.TransformPoint(new Vector3(c.x, 0f, c.y));
                baseY = Mathf.Max(baseY, Slope.DrawnHeight(w.x, w.z));
            }
            t.position = new Vector3(t.position.x, baseY, t.position.z);
            float half = LodgeHalf, rise = half * Mathf.Tan(LodgePitch * Mathf.Deg2Rad);
            P(PrimitiveType.Cube, t, new Vector3(0f, -1.1f, 0f), new Vector3(10.6f, 2.4f, 10.6f), Vector3.zero, stone);
            P(PrimitiveType.Cube, t, new Vector3(0f, Eave * 0.5f, 0f), new Vector3(10.2f, Eave, 10.2f), Vector3.zero, darkWood);

            // the lower storey's lit windows and door, under the deck
            foreach (var x in new[] { -3.4f, -1.6f, 4.0f }) Window(t, new Vector3(x, 1.0f, 5.1f), 0f, 1.1f, 0.85f);
            Window(t, new Vector3(5.1f, 1.0f, 2.2f), 90f, 1.1f, 0.85f);
            Window(t, new Vector3(-5.1f, 1.0f, 0f), -90f, 1.1f, 0.85f);
            P(PrimitiveType.Cube, t, new Vector3(1.4f, 0.9f, 5.12f), new Vector3(1.1f, 1.8f, 0.1f), Vector3.zero, wood);
            P(PrimitiveType.Cube, t, new Vector3(1.4f, 1.15f, 5.16f), new Vector3(0.6f, 0.9f, 0.06f), Vector3.zero, glass, false);

            // the roof: two steep planes from the deck to the ridge, deep snow on both
            Vector3 ridge = new Vector3(0f, Eave + rise, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 eave = new Vector3(s * (half + 0.55f), Eave - 0.55f * Mathf.Tan(LodgePitch * Mathf.Deg2Rad), 0f);
                Slab(t, ridge + new Vector3(0f, 0.2f, 0f), eave, 11.4f, 0.32f, roof, 0.38f);
            }
            // the back gable in boards; the front one all glass, a timber grid over it
            var back = Wedge(t, new Vector3(0f, Eave, -5.1f), 2f * half, rise, 0.25f, darkWood);
            var front = Wedge(t, new Vector3(0f, Eave, 5.05f), 2f * half - 0.25f, rise - 0.15f, 0.1f, glass);
            front.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var x in new[] { -3.3f, -1.65f, 0f, 1.65f, 3.3f })
            {
                float h = rise * (1f - Mathf.Abs(x) / half) - 0.1f;
                P(PrimitiveType.Cube, t, new Vector3(x, Eave + h * 0.5f, 5.12f), new Vector3(0.15f, h, 0.16f), Vector3.zero, darkWood);
            }
            for (int k = 1; k <= 3; k++)
            {
                float y = rise * k / 4f, w = 2f * half * (1f - y / rise);
                P(PrimitiveType.Cube, t, new Vector3(0f, Eave + y, 5.12f), new Vector3(w, 0.15f, 0.16f), Vector3.zero, darkWood);
            }
            P(PrimitiveType.Cube, t, new Vector3(0f, Eave + 0.1f, 5.12f), new Vector3(2f * half + 0.2f, 0.25f, 0.24f), Vector3.zero, darkWood);
            // a dormer wing on the right slope, its own window lit
            float dTop = Eave + rise * (1f - 1.2f / half);
            P(PrimitiveType.Cube, t, new Vector3(2.75f, (Eave + 2.4f + dTop) * 0.5f, -1.8f), new Vector3(3.1f, dTop - Eave - 2.4f, 3.2f), Vector3.zero, darkWood);
            Window(t, new Vector3(4.32f, (Eave + 2.4f + dTop) * 0.5f, -1.8f), 90f, 1.4f, 1.0f);
            Slab(t, new Vector3(1.0f, dTop + 0.25f, -1.8f), new Vector3(4.7f, Eave + 2.3f + 0.25f, -1.8f), 3.8f, 0.22f, roof, 0.3f);
            // chimney on the left slope, smoking
            var ch = P(PrimitiveType.Cube, t, new Vector3(-2.6f, Eave + rise * 0.62f + 1.1f, -2.2f), new Vector3(0.95f, 3.2f, 0.95f), Vector3.zero, stone);
            Smoke(t, new Vector3(-2.6f, ch.localPosition.y + 1.7f, -2.2f));
            P(PrimitiveType.Cube, t, new Vector3(-2.6f, ch.localPosition.y + 1.62f, -2.2f), new Vector3(1.1f, 0.18f, 1.1f), Vector3.zero, snowCap);

            // the deck: across the front and down the right side, on posts, with a rail round it
            float d = Eave - 0.1f;
            P(PrimitiveType.Cube, t, new Vector3(0.7f, d, 6.45f), new Vector3(14f, 0.2f, 2.9f), Vector3.zero, wood);
            P(PrimitiveType.Cube, t, new Vector3(6.4f, d, 1.8f), new Vector3(2.6f, 0.2f, 12f), Vector3.zero, wood);
            foreach (var pp in new[] { new Vector2(-6.2f, 7.75f), new Vector2(-2.2f, 7.75f), new Vector2(0.95f, 7.75f), new Vector2(3.05f, 7.75f), new Vector2(7.6f, 7.75f),
                                       new Vector2(7.6f, 3.6f), new Vector2(7.6f, -0.4f), new Vector2(7.6f, -4.1f) })
                Log(t, new Vector3(pp.x, -1.6f, pp.y), new Vector3(pp.x, d, pp.y), 0.24f, logs);
            Rail(t, new Vector3(-6.25f, Eave, 5.2f), new Vector3(-6.25f, Eave, 7.75f));
            Rail(t, new Vector3(-6.25f, Eave, 7.75f), new Vector3(0.95f, Eave, 7.75f));
            Rail(t, new Vector3(3.05f, Eave, 7.75f), new Vector3(7.65f, Eave, 7.75f));
            Rail(t, new Vector3(7.65f, Eave, 7.75f), new Vector3(7.65f, Eave, -4.15f));
            Rail(t, new Vector3(7.65f, Eave, -4.15f), new Vector3(5.1f, Eave, -4.15f));
            // warm lamps under the deck, lighting the snow
            foreach (var x in new[] { -5f, -2.6f, 4.6f, 6.9f })
                P(PrimitiveType.Cube, t, new Vector3(x, d - 0.18f, 7.6f), new Vector3(0.3f, 0.08f, 0.12f), Vector3.zero, bulb, false);
            Lamp(t, new Vector3(0.6f, 0.8f, 6.8f), 7f, 1.6f);
            // the glass wall's glow falling on the deck and the snow in front
            Lamp(t, new Vector3(0f, Eave + 2.6f, 6.6f), 12f, 2.4f);
            // the camp's name hung on the front rail
            P(PrimitiveType.Cube, t, new Vector3(-2.6f, Eave + 0.55f, 7.82f), new Vector3(3.3f, 0.86f, 0.08f), Vector3.zero, darkWood);
            Face(t, new Vector3(-2.6f, Eave + 0.55f, 7.87f), 3.2f, 0.8f, Sign("Camp/SignCamp"));

            // stairs down from the deck to the snow
            var footW = t.TransformPoint(new Vector3(2f, 0f, 11f));
            float foot = Slope.DrawnHeight(footW.x, footW.z) - baseY;   // the snow at the foot, in the lodge's frame
            const int steps = 7; const float run = 3.2f;
            for (int k = 0; k < steps; k++)
            {
                float top = Mathf.Lerp(Eave, foot, (k + 1f) / (steps + 1f)), z = 7.8f + (k + 0.5f) * run / steps;
                P(PrimitiveType.Cube, t, new Vector3(2f, (top + foot - 0.4f) * 0.5f, z), new Vector3(2f, top - foot + 0.4f, run / steps + 0.02f), Vector3.zero, wood);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                float x = 2f + s * 1.05f;
                Log(t, new Vector3(x, Eave + 0.95f, 7.75f), new Vector3(x, foot + 0.95f, 7.8f + run), 0.08f, darkWood);
                Log(t, new Vector3(x, foot - 0.3f, 7.8f + run), new Vector3(x, foot + 1f, 7.8f + run), 0.12f, logs);
                Block(t, new Vector3(x, 0f, 7.8f + run * 0.5f), new Vector2(0.08f, run * 0.5f));
            }

            // snow drifted against the house
            for (int k = 0; k < 6; k++)
            {
                float u = k / 5f;
                P(PrimitiveType.Sphere, t, new Vector3(-5.6f, 0.1f, -4.5f + u * 9f), new Vector3(1.6f, 1.1f, 2.0f), Vector3.zero, snowCap);
                P(PrimitiveType.Sphere, t, new Vector3(-5f + u * 10f, 0.1f, -5.6f), new Vector3(2.0f, 1.2f, 1.5f), Vector3.zero, snowCap);
            }

            // where you can and can't walk: round the house, up the stairs, along the deck inside its rail
            Block(t, Vector3.zero, new Vector2(5.3f, 5.3f));
            Block(t, new Vector3(0.7f, 0f, 6.45f), new Vector2(7f, 1.45f), null, true, Eave);
            Block(t, new Vector3(6.4f, 0f, 1.8f), new Vector2(1.3f, 6f), null, true, Eave);
            Block(t, new Vector3(2f, 0f, 7.8f + run * 0.5f), new Vector2(0.97f, run * 0.5f), null, true, Eave, Eave - foot);
            Block(t, new Vector3(-6.25f, 0f, 6.45f), new Vector2(0.1f, 1.4f));
            Block(t, new Vector3(-2.65f, 0f, 7.75f), new Vector2(3.6f, 0.1f));
            Block(t, new Vector3(5.35f, 0f, 7.75f), new Vector2(2.3f, 0.1f));
            Block(t, new Vector3(7.65f, 0f, 1.8f), new Vector2(0.1f, 6f));
            Block(t, new Vector3(6.4f, 0f, -4.15f), new Vector2(1.3f, 0.1f));
            // a collider round the house, so the camera stays out of it
            var lc = t.gameObject.AddComponent<BoxCollider>();
            lc.center = new Vector3(0f, (Eave + rise) * 0.5f, 0f); lc.size = new Vector3(10.6f, Eave + rise + 0.4f, 10.6f);

            lodgeApex = t.TransformPoint(new Vector3(0f, Eave + rise - 0.6f, 5.3f));
            deckL = t.TransformPoint(new Vector3(-6.25f, Eave + 1.1f, 7.75f));
            deckR = t.TransformPoint(new Vector3(7.65f, Eave + 1.1f, 7.75f));
            stairsFoot = t.TransformPoint(new Vector3(2f, foot, 7.8f + run + 0.6f));
        }

        void Window(Transform t, Vector3 at, float yaw, float w = 1.3f, float h = 1.0f)
        {
            var win = new GameObject("Window").transform;
            win.SetParent(t, false); win.localPosition = at; win.localRotation = Quaternion.Euler(0f, yaw, 0f);
            P(PrimitiveType.Cube, win, Vector3.zero, new Vector3(w + 0.3f, h + 0.3f, 0.14f), Vector3.zero, wood);
            P(PrimitiveType.Cube, win, new Vector3(0f, 0f, 0.03f), new Vector3(w, h, 0.12f), Vector3.zero, glass, false);
            P(PrimitiveType.Cube, win, new Vector3(0f, 0f, 0.1f), new Vector3(0.07f, h, 0.04f), Vector3.zero, darkWood);
            P(PrimitiveType.Cube, win, new Vector3(0f, 0f, 0.1f), new Vector3(w, 0.07f, 0.04f), Vector3.zero, darkWood);
            P(PrimitiveType.Cube, win, new Vector3(0f, -h * 0.5f - 0.2f, 0.12f), new Vector3(w + 0.5f, 0.1f, 0.3f), Vector3.zero, snowCap);
        }

        /// <summary>A flat slab (a roof plane) from a to b, depth across, with snow lying on its top face.</summary>
        void Slab(Transform t, Vector3 a, Vector3 b, float depth, float thick, Material m, float snow)
        {
            Vector3 along = b - a, mid = (a + b) * 0.5f;
            // the slab's local X runs from a to b, Y is its outward (upper) face
            var rot = Quaternion.LookRotation(Vector3.forward, Vector3.Cross(Vector3.forward, along.normalized));
            if ((rot * Vector3.up).y < 0f) rot = Quaternion.LookRotation(Vector3.forward, -(rot * Vector3.up));
            var slab = P(PrimitiveType.Cube, t, mid, new Vector3(along.magnitude, thick, depth), Vector3.zero, m);
            slab.localRotation = rot;
            if (snow <= 0f) return;
            var cap = P(PrimitiveType.Cube, t, mid + rot * Vector3.up * (thick + snow) * 0.5f, new Vector3(along.magnitude * 0.98f, snow, depth + 0.1f), Vector3.zero, snowCap);
            cap.localRotation = rot;
            // lumps along the eave where it has piled up
            Vector3 low = a.y < b.y ? a : b;
            for (int i = 0; i < 4; i++)
                P(PrimitiveType.Sphere, t, low + rot * Vector3.up * (thick + snow) + new Vector3(0f, 0f, -depth * 0.4f + i * depth * 0.27f), new Vector3(0.9f, 0.45f, depth * 0.3f), Vector3.zero, snowCap);
        }

        Transform Wedge(Transform t, Vector3 at, float w, float h, float depth, Material m)
        {
            var g = new GameObject("Gable", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            g.SetParent(t, false);
            g.localPosition = at + new Vector3(0f, 0f, depth * 0.5f);
            g.localScale = new Vector3(w, h, depth);
            g.GetComponent<MeshFilter>().sharedMesh = Gable();
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        /// <summary>A deck rail from a to b (on the deck at a.y): posts, a top rail with snow on it, a mid rail.</summary>
        void Rail(Transform t, Vector3 a, Vector3 b)
        {
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.6f));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)n);
                P(PrimitiveType.Cube, t, p + Vector3.up * 0.5f, new Vector3(0.12f, 1f, 0.12f), Vector3.zero, wood);
            }
            var dir = (b - a).normalized;
            var rot = Quaternion.LookRotation(dir, Vector3.up).eulerAngles;
            P(PrimitiveType.Cube, t, (a + b) * 0.5f + Vector3.up * 1.0f, new Vector3(0.12f, 0.1f, len), rot, wood);
            P(PrimitiveType.Cube, t, (a + b) * 0.5f + Vector3.up * 1.08f, new Vector3(0.16f, 0.07f, len), rot, snowCap);
            P(PrimitiveType.Cube, t, (a + b) * 0.5f + Vector3.up * 0.5f, new Vector3(0.07f, 0.07f, len), rot, wood);
            // balusters
            int m = Mathf.FloorToInt(len / 0.32f);
            for (int i = 1; i < m; i++)
                P(PrimitiveType.Cube, t, Vector3.Lerp(a, b, i / (float)m) + Vector3.up * 0.5f, new Vector3(0.05f, 0.95f, 0.05f), rot, darkWood, false);
        }

        // ---------------------------------------------------------------- birches, lumpy snow, the trodden path

        /// <summary>Tall bare birches round the lodge: white trunks, a few dark forks of branches.</summary>
        void Birches()
        {
            var where = new[] { new Vector2(-22f, -109f), new Vector2(-21f, -114.5f), new Vector2(-5.5f, -123f), new Vector2(-17.5f, -93f), new Vector2(-25.5f, -100f), new Vector2(-1f, -126f) };
            for (int i = 0; i < where.Length; i++)
            {
                var t = Frame(where[i].x, where[i].y, i * 53f);
                t.name = "Birch";
                float h = 9f + 4f * Slope.Hash01(i, 5, 31), lean = (Slope.Hash01(i, 6, 31) - 0.5f) * 6f;
                t.localRotation *= Quaternion.Euler(lean, 0f, lean * 0.5f);
                var trunk = Log(t, new Vector3(0f, -0.4f, 0f), new Vector3(0f, h, 0f), 0.26f, birch);
                for (int k = 0; k < 7; k++)
                {
                    float y = h * (0.45f + 0.08f * k), a = k * 137f * Mathf.Deg2Rad, len = (1.2f + 1.6f * Slope.Hash01(i, k, 32)) * (1f - k * 0.08f);
                    var from = new Vector3(0f, y, 0f);
                    var to = from + new Vector3(Mathf.Cos(a) * len, len * 1.1f, Mathf.Sin(a) * len);
                    Log(t, from, to, 0.06f, darkWood);
                    Log(t, to, to + new Vector3(Mathf.Cos(a + 0.6f) * len * 0.5f, len * 0.6f, Mathf.Sin(a + 0.6f) * len * 0.5f), 0.03f, darkWood);
                }
                Block(t, Vector3.zero, new Vector2(0.25f, 0.25f));
            }
        }

        /// <summary>Deep, lumpy snow round the edges of camp.</summary>
        void Mounds()
        {
            var t = new GameObject("Mounds").transform; t.SetParent(root, false);
            for (int i = 0; i < 26; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float dx = side * (22f + 6f * Slope.Hash01(i, 1, 41)), z = Mathf.Lerp(-124f, -80f, Slope.Hash01(i, 2, 41));
                if (i >= 20) { dx = Mathf.Lerp(-14f, 14f, Slope.Hash01(i, 3, 41)); z = -127f - 3f * Slope.Hash01(i, 4, 41); }
                var p = Ground(dx, z);
                float s = 1.4f + 2.2f * Slope.Hash01(i, 5, 41);
                P(PrimitiveType.Sphere, t, p + Vector3.down * s * 0.22f, new Vector3(s * 1.4f, s * 0.6f, s), new Vector3(0f, i * 41f, 0f), snowCap);
            }
        }

        /// <summary>A path trodden through the snow from the fire to the lodge's stairs, boot prints and all.</summary>
        void Footpath()
        {
            var pts = new List<Vector3> { Ground(-1f, -97.5f), Ground(-4.5f, -100.5f), Ground(-7.5f, -104.5f), stairsFoot };
            // sample a smooth curve through them, laid on the snow
            var line = new List<Vector3>();
            for (int i = 0; i < pts.Count - 1; i++)
                for (int k = 0; k < 8; k++)
                {
                    float u = k / 8f;
                    Vector3 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
                    line.Add(0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u));
                }
            line.Add(pts[pts.Count - 1]);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float dist = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                var a = line[Mathf.Max(0, i - 1)]; var b = line[Mathf.Min(line.Count - 1, i + 1)];
                var dir = b - a; dir.y = 0f; dir.Normalize();
                var side = new Vector3(dir.z, 0f, -dir.x) * 0.75f;
                if (i > 0) dist += Vector3.Distance(line[i - 1], line[i]);
                foreach (var e in new[] { -1f, 1f })
                {
                    var q = line[i] + side * e;
                    q.y = Slope.DrawnHeight(q.x, q.z) + 0.03f;
                    v.Add(q); uv.Add(new Vector2(e < 0f ? 0f : 1f, dist / 2.6f));
                }
                if (i > 0) { int k = v.Count - 4; tri.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            }
            var mesh = new Mesh { name = "Footpath" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Footpath", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = path; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>A lit, see-through material (for the path over the snow), as RiderBody makes its snow marks.</summary>
        Material Imprint(Texture2D tex)
        {
            var baseMat = Resources.Load<Material>("ImprintBase");
            var m = baseMat != null ? new Material(baseMat) : new Material(lit);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_Smoothness", 0.1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 2600;
            return m;
        }

        // ---------------------------------------------------------------- blue hour in camp

        // The camp is lit like evening: a cold blue sky and snow, warm light from the lodge, the deck and the
        // fire. Dropping in blends back to the run's daytime over a second, and the camp's lamps go out.
        readonly List<Light> lamps = new List<Light>();
        readonly List<float> lampPower = new List<float>();
        public float Night { get; private set; }
        Light sun; Color sunDay, ambSky, ambEq, ambGround, fogDay, skyTintDay; float sunDayI, skyExpDay;
        Material sky;
        bool dayKnown;

        Light Lamp(Transform t, Vector3 at, float range, float power)
        {
            var l = new GameObject("Lamp").AddComponent<Light>();
            l.transform.SetParent(t, false); l.transform.localPosition = at;
            // light colours are linearised per channel too: a pale warm white comes out amber, a saturated one red
            l.type = LightType.Point; l.color = new Color(1f, 0.8f, 0.6f); l.range = range; l.intensity = power; l.shadows = LightShadows.None;
            lamps.Add(l); lampPower.Add(power);
            return l;
        }

        /// <summary>Remember the daytime look the game set up, to blend from and back to.</summary>
        void CaptureDay()
        {
            sun = RenderSettings.sun;
            if (sun != null) { sunDay = sun.color; sunDayI = sun.intensity; }
            ambSky = RenderSettings.ambientSkyColor; ambEq = RenderSettings.ambientEquatorColor; ambGround = RenderSettings.ambientGroundColor;
            fogDay = RenderSettings.fogColor;
            sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure")) { skyExpDay = sky.GetFloat("_Exposure"); skyTintDay = sky.HasProperty("_SkyTint") ? sky.GetColor("_SkyTint") : Color.white; }
            dayKnown = true;
        }

        /// <summary>Set how far into the evening the camp is (0 the run's daylight, 1 blue hour).</summary>
        public void SetNight(float n)
        {
            Night = Mathf.Clamp01(n);
            if (!dayKnown) return;
            float k = Mathf.SmoothStep(0f, 1f, Night);
            if (sun != null) { sun.color = Color.Lerp(sunDay, new Color(0.55f, 0.66f, 1f), k); sun.intensity = Mathf.Lerp(sunDayI, 0.32f, k); }
            RenderSettings.ambientSkyColor = Color.Lerp(ambSky, new Color(0.20f, 0.27f, 0.50f), k);
            RenderSettings.ambientEquatorColor = Color.Lerp(ambEq, new Color(0.16f, 0.21f, 0.38f), k);
            RenderSettings.ambientGroundColor = Color.Lerp(ambGround, new Color(0.09f, 0.11f, 0.19f), k);
            RenderSettings.fogColor = Color.Lerp(fogDay, new Color(0.12f, 0.17f, 0.31f), k);
            if (sky != null && sky.HasProperty("_Exposure"))
            {
                sky.SetFloat("_Exposure", Mathf.Lerp(skyExpDay, 0.3f, k));
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", Color.Lerp(skyTintDay, new Color(0.16f, 0.24f, 0.55f), k));
            }
            // the photo backdrop is unlit: its shader takes the evening tint from this. (Not a colour grade on
            // the post profile: a cool filter with lower exposure tipped every warm light to pure red.)
            Shader.SetGlobalFloat("_SummitNight", k);
            for (int i = 0; i < lamps.Count; i++)
                if (lamps[i] != null) { lamps[i].intensity = lampPower[i] * k; lamps[i].enabled = k > 0.01f; }
        }

        void Update()
        {
            float want = Active ? 1f : 0f;
            if (Mathf.Abs(Night - want) > 0.0001f) SetNight(Mathf.MoveTowards(Night, want, Time.deltaTime / 1.2f));
        }

        // never leave the project's sky or grade in the evening (in the editor they are assets)
        void OnDisable() { if (dayKnown) SetNight(0f); }

        void Board(Transform t)
        {
            t.name = "MissionBoard";
            for (int s = -1; s <= 1; s += 2) Log(t, new Vector3(s * 1.35f, -0.4f, 0f), new Vector3(s * 1.35f, 2.95f, 0f), 0.18f, logs);
            P(PrimitiveType.Cube, t, new Vector3(0f, 1.55f, 0f), new Vector3(2.5f, 1.55f, 0.1f), Vector3.zero, wood);
            P(PrimitiveType.Cube, t, new Vector3(0f, 0.75f, 0.1f), new Vector3(2.6f, 0.08f, 0.16f), Vector3.zero, darkWood);
            for (int s = -1; s <= 1; s += 2)
            {
                var sl = P(PrimitiveType.Cube, t, new Vector3(s * 0.75f, 3.12f, 0f), new Vector3(1.75f, 0.1f, 0.8f), new Vector3(0f, 0f, -s * 22f), roof);
                P(PrimitiveType.Cube, sl, new Vector3(0f, 1.2f, 0f), new Vector3(0.95f, 0.9f, 1f), Vector3.zero, snowCap);
            }
            Face(t, new Vector3(0f, 2.65f, 0.06f), 2.5f, 0.62f, Sign("Camp/SignMissions"));
            notes = new Renderer[3];
            for (int i = 0; i < 3; i++)
                notes[i] = Face(t, new Vector3(-0.78f + i * 0.78f, 1.55f, 0.06f), 0.6f, 0.75f, noteMats[i]).GetComponent<Renderer>();
            notes[0].transform.localRotation *= Quaternion.Euler(0f, 0f, 4f);
            notes[2].transform.localRotation *= Quaternion.Euler(0f, 0f, -3f);
            Block(t, Vector3.zero, new Vector2(1.5f, 0.3f));
            Stand(t, Station.Missions, new Vector3(0f, 0f, 2.2f), 2.6f);
        }
        Renderer[] notes;

        void Bench(Transform t)
        {
            t.name = "TuneUpBench";
            // a lean-to: plank back wall, four posts, a roof sloping to the front
            P(PrimitiveType.Cube, t, new Vector3(0f, 1.35f, -1.35f), new Vector3(4.2f, 2.9f, 0.14f), Vector3.zero, wood);
            P(PrimitiveType.Cube, t, new Vector3(0f, 1.75f, -1.26f), new Vector3(2.8f, 1.1f, 0.04f), Vector3.zero, M(new Color(0.18f, 0.16f, 0.15f), 0.1f));
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Log(t, new Vector3(sx * 2.05f, -0.3f, -1.35f), new Vector3(sx * 2.05f, 3.05f, -1.35f), 0.2f, logs);
                Log(t, new Vector3(sx * 2.05f, -0.3f, 1.3f), new Vector3(sx * 2.05f, 2.5f, 1.3f), 0.2f, logs);
            }
            var rf = P(PrimitiveType.Cube, t, new Vector3(0f, 2.85f, 0f), new Vector3(4.8f, 0.16f, 3.3f), new Vector3(11f, 0f, 0f), roof);
            P(PrimitiveType.Cube, rf, new Vector3(0f, 1.7f, 0f), new Vector3(0.98f, 1.6f, 0.98f), Vector3.zero, snowCap);
            Face(t, new Vector3(0f, 2.3f, 1.42f), 3.2f, 0.8f, Sign("Camp/SignUpgrades"));
            // the workbench, with a board clamped on it for a wax
            P(PrimitiveType.Cube, t, new Vector3(0f, 0.95f, -0.55f), new Vector3(3.2f, 0.12f, 1.0f), Vector3.zero, wood);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    P(PrimitiveType.Cube, t, new Vector3(sx * 1.45f, 0.45f, -0.55f + sz * 0.4f), new Vector3(0.1f, 1f, 0.1f), Vector3.zero, darkWood);
            var bd = new GameObject("Board").transform;
            bd.SetParent(t, false); bd.localPosition = new Vector3(-0.2f, 1.05f, -0.5f);
            benchBoard = P(PrimitiveType.Cube, bd, Vector3.zero, new Vector3(1.4f, 0.03f, 0.29f), Vector3.zero, M(Color.white, 0.6f)).GetComponent<Renderer>();
            P(PrimitiveType.Cylinder, bd, new Vector3(0.7f, 0f, 0f), new Vector3(0.29f, 0.015f, 0.29f), Vector3.zero, benchBoard.sharedMaterial);
            P(PrimitiveType.Cylinder, bd, new Vector3(-0.7f, 0f, 0f), new Vector3(0.29f, 0.015f, 0.29f), Vector3.zero, benchBoard.sharedMaterial);
            for (int s = -1; s <= 1; s += 2) P(PrimitiveType.Cube, bd, new Vector3(s * 0.62f, 0.02f, 0f), new Vector3(0.12f, 0.1f, 0.36f), Vector3.zero, metal);
            // wax iron, wax bars and a scraper; tools hung on the wall
            P(PrimitiveType.Cube, t, new Vector3(1.05f, 1.06f, -0.75f), new Vector3(0.28f, 0.1f, 0.16f), Vector3.zero, metal);
            P(PrimitiveType.Cube, t, new Vector3(1.05f, 1.17f, -0.75f), new Vector3(0.22f, 0.04f, 0.04f), Vector3.zero, coal);
            Color[] wax = { new Color(0.2f, 0.5f, 1f), new Color(0.95f, 0.3f, 0.5f), new Color(0.98f, 0.85f, 0.2f) };
            for (int i = 0; i < 3; i++) P(PrimitiveType.Cube, t, new Vector3(1.0f + i * 0.14f, 1.04f, -0.32f), new Vector3(0.1f, 0.07f, 0.2f), Vector3.zero, M(wax[i], 0.5f));
            for (int i = 0; i < 5; i++)
            {
                float x = -1.1f + i * 0.55f;
                P(PrimitiveType.Cube, t, new Vector3(x, 1.85f, -1.2f), new Vector3(0.06f, 0.5f, 0.03f), Vector3.zero, metal);
                P(PrimitiveType.Cube, t, new Vector3(x, 2.1f - (i % 2) * 0.5f, -1.2f), new Vector3(0.16f, 0.1f, 0.03f), Vector3.zero, i % 2 == 0 ? metal : scarf);
            }
            P(PrimitiveType.Sphere, t, new Vector3(0f, 2.45f, -0.3f), Vector3.one * 0.16f, Vector3.zero, bulb, false);
            Log(t, new Vector3(0f, 2.53f, -0.3f), new Vector3(0f, 2.85f, -0.3f), 0.02f, wire);
            Block(t, new Vector3(0f, 0f, -0.1f), new Vector2(2.2f, 1.5f));
            Stand(t, Station.Upgrades, new Vector3(0f, 0f, 2.4f), 2.6f);
        }

        void Rack(Transform t)
        {
            t.name = "BoardRack";
            for (int s = -1; s <= 1; s += 2) Log(t, new Vector3(s * 1.1f, -0.3f, -0.2f), new Vector3(s * 1.1f, 1.55f, -0.2f), 0.14f, logs);
            Log(t, new Vector3(-1.2f, 1.35f, -0.2f), new Vector3(1.2f, 1.35f, -0.2f), 0.1f, logs);
            Log(t, new Vector3(-1.2f, 0.25f, 0.15f), new Vector3(1.2f, 0.25f, 0.15f), 0.1f, logs);
            rackBoards = new Renderer[5];
            for (int i = 0; i < 5; i++)
            {
                var b = P(PrimitiveType.Cube, t, new Vector3(-0.84f + i * 0.42f, 0.78f, 0f), new Vector3(0.28f, 1.45f, 0.03f), new Vector3(-14f, 0f, 0f), M(Progress.Boards[(i + 1) % Progress.Boards.Length], 0.6f));
                rackBoards[i] = b.GetComponent<Renderer>();
                P(PrimitiveType.Cube, b, new Vector3(0f, 0.13f, 0.9f), new Vector3(0.75f, 0.08f, 2.5f), Vector3.zero, wire);
                P(PrimitiveType.Cube, b, new Vector3(0f, -0.15f, 0.9f), new Vector3(0.75f, 0.08f, 2.5f), Vector3.zero, wire);
            }
            Block(t, Vector3.zero, new Vector2(1.3f, 0.45f));
            Stand(t, Station.Style, new Vector3(0f, 0f, 1.9f), 1.9f);
        }
        Renderer[] rackBoards;

        void Gate()
        {
            // built square to the world: the sign faces up the hill, at the camp
            var t = Frame(0f, GateZ, 0f);
            t.name = "DropInGate";
            for (int s = -1; s <= 1; s += 2)
            {
                Log(t, new Vector3(s * 2.9f, -0.5f, 0f), new Vector3(s * 2.9f, 4.4f, 0f), 0.4f, logs);
                P(PrimitiveType.Sphere, t, new Vector3(s * 2.9f, 4.45f, 0f), new Vector3(0.5f, 0.3f, 0.5f), Vector3.zero, snowCap);
                P(PrimitiveType.Cube, t, new Vector3(s * 2.9f, 3.2f, -0.3f), new Vector3(0.24f, 0.34f, 0.24f), Vector3.zero, bulb, false);
                Block(t, new Vector3(s * 2.9f, 0f, 0f), new Vector2(0.3f, 0.3f));
            }
            Log(t, new Vector3(-3.5f, 4.05f, 0f), new Vector3(3.5f, 4.05f, 0f), 0.36f, logs);
            P(PrimitiveType.Cube, t, new Vector3(0f, 4.27f, 0f), new Vector3(6.8f, 0.14f, 0.4f), Vector3.zero, snowCap);
            P(PrimitiveType.Cube, t, new Vector3(0f, 3.25f, 0.02f), new Vector3(4.5f, 1.15f, 0.1f), Vector3.zero, darkWood);
            P(PrimitiveType.Quad, t, new Vector3(0f, 3.25f, -0.04f), new Vector3(4.3f, 1.07f, 1f), Vector3.zero, Sign("Camp/SignDropIn"));
            for (int s = -1; s <= 1; s += 2) Log(t, new Vector3(s * 1.2f, 4.0f, 0f), new Vector3(s * 1.2f, 3.82f, 0f), 0.03f, rope);
            gateFrame = t;
            Stand(t, Station.DropIn, new Vector3(0f, 0f, -2.6f), 3.2f);
        }
        Transform gateFrame;

        void Lookout(Transform t)
        {
            t.name = "Lookout";
            float y = 0.45f;
            P(PrimitiveType.Cube, t, new Vector3(0f, y - 0.1f, 0f), new Vector3(6f, 0.2f, 4.2f), Vector3.zero, wood);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Log(t, new Vector3(sx * 2.85f, -1.5f, sz * 1.95f), new Vector3(sx * 2.85f, y + 1.05f, sz * 1.95f), 0.18f, logs);
                }
            // a rail round the front and sides; open at the back where you step up
            P(PrimitiveType.Cube, t, new Vector3(0f, y + 1f, 2f), new Vector3(5.9f, 0.1f, 0.1f), Vector3.zero, wood);
            P(PrimitiveType.Cube, t, new Vector3(0f, y + 0.55f, 2f), new Vector3(5.9f, 0.08f, 0.08f), Vector3.zero, wood);
            for (int s = -1; s <= 1; s += 2)
            {
                P(PrimitiveType.Cube, t, new Vector3(s * 2.9f, y + 1f, 0f), new Vector3(0.1f, 0.1f, 4f), Vector3.zero, wood);
                P(PrimitiveType.Cube, t, new Vector3(s * 2.9f, y + 0.55f, 0f), new Vector3(0.08f, 0.08f, 4f), Vector3.zero, wood);
                Block(t, new Vector3(s * 2.95f, 0f, 0f), new Vector2(0.12f, 2.1f));
            }
            P(PrimitiveType.Cube, t, new Vector3(0f, y + 1.08f, 2f), new Vector3(5.9f, 0.08f, 0.2f), Vector3.zero, snowCap);
            Block(t, new Vector3(0f, 0f, 2.05f), new Vector2(3f, 0.12f));
            Block(t, Vector3.zero, new Vector2(2.9f, 2f), null, true, y);
            // the coin telescope, aimed down the mountain
            Log(t, new Vector3(1.2f, y, 1.2f), new Vector3(1.2f, y + 1.05f, 1.2f), 0.12f, metal);
            var scope = P(PrimitiveType.Cylinder, t, new Vector3(1.2f, y + 1.25f, 1.25f), new Vector3(0.2f, 0.38f, 0.2f), new Vector3(78f, 0f, 0f), metal);
            P(PrimitiveType.Cylinder, scope, new Vector3(0f, 1.05f, 0f), new Vector3(1.25f, 0.08f, 1.25f), Vector3.zero, glass, false);
            P(PrimitiveType.Cube, t, new Vector3(1.2f, y + 1.0f, 1.2f), new Vector3(0.24f, 0.3f, 0.18f), Vector3.zero, M(new Color(0.12f, 0.35f, 0.25f), 0.6f));
            Block(t, new Vector3(1.2f, 0f, 1.2f), new Vector2(0.2f, 0.2f));
            // its sign, on a post at the back corner, read from the camp
            Log(t, new Vector3(-3.4f, -0.3f, -2.3f), new Vector3(-3.4f, 2.3f, -2.3f), 0.14f, logs);
            var sg = P(PrimitiveType.Quad, t, new Vector3(-3.4f, 2.0f, -2.38f), new Vector3(1.6f, 0.4f, 1f), Vector3.zero, Sign("Camp/SignLookout"));
            P(PrimitiveType.Cube, t, new Vector3(-3.4f, 2.0f, -2.32f), new Vector3(1.7f, 0.48f, 0.08f), Vector3.zero, darkWood);
            Block(t, new Vector3(-3.4f, 0f, -2.3f), new Vector2(0.15f, 0.15f));
            lookFrame = t; lookTop = y;
            spots.Add(new Spot { st = Station.Lookout, at = t.position, radius = 2.6f, root = t });
        }
        Transform lookFrame; float lookTop;

        void Benches()
        {
            // log seats round the fire: left, right and behind; the front stays open to the view
            var t = Frame(0f, -101f, 0f);
            t.name = "Benches";
            foreach (var c in new[] { new Vector3(-2.9f, 0f, 0f), new Vector3(2.9f, 0f, 0f), new Vector3(0f, 0f, -2.9f) })
            {
                var side = c.z != 0f ? Vector3.right : Vector3.forward;
                Log(t, c - side * 1.1f + Vector3.up * 0.28f, c + side * 1.1f + Vector3.up * 0.28f, 0.42f, logs);
                Block(t, c, c.z != 0f ? new Vector2(1.2f, 0.3f) : new Vector2(0.3f, 1.2f));
            }
        }

        Transform FirePit(Transform t)
        {
            t.name = "FirePit";
            for (int i = 0; i < 11; i++)
            {
                float a = i / 11f * Mathf.PI * 2f;
                P(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(a) * 0.9f, 0.08f, Mathf.Sin(a) * 0.9f), new Vector3(0.42f, 0.3f, 0.36f), new Vector3(0f, i * 37f, 0f), stone);
            }
            P(PrimitiveType.Cylinder, t, new Vector3(0f, 0.02f, 0f), new Vector3(1.5f, 0.02f, 1.5f), Vector3.zero, coal);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.55f;
                Log(t, -dir + Vector3.up * (0.12f + i * 0.04f), dir + Vector3.up * (0.12f + i * 0.04f), 0.14f, logs);
            }
            P(PrimitiveType.Sphere, t, new Vector3(0f, 0.15f, 0f), new Vector3(0.7f, 0.25f, 0.7f), Vector3.zero, ember, false);
            Flames(t, new Vector3(0f, 0.25f, 0f));
            Lamp(t, new Vector3(0f, 1.1f, 0f), 9f, 2.2f).color = new Color(1f, 0.72f, 0.45f);
            Block(t, Vector3.zero, new Vector2(1.1f, 1.1f), t.gameObject);
            return t;
        }

        Transform Snowman(Transform t)
        {
            t.name = "Snowman";
            P(PrimitiveType.Sphere, t, new Vector3(0f, 0.5f, 0f), Vector3.one * 1.15f, Vector3.zero, snowCap);
            P(PrimitiveType.Sphere, t, new Vector3(0f, 1.3f, 0f), Vector3.one * 0.85f, Vector3.zero, snowCap);
            P(PrimitiveType.Sphere, t, new Vector3(0f, 1.95f, 0f), Vector3.one * 0.6f, Vector3.zero, snowCap);
            P(PrimitiveType.Cylinder, t, new Vector3(0f, 1.72f, 0f), new Vector3(0.66f, 0.07f, 0.66f), Vector3.zero, scarf);
            P(PrimitiveType.Cube, t, new Vector3(0.18f, 1.45f, 0.3f), new Vector3(0.14f, 0.45f, 0.05f), new Vector3(10f, 0f, 8f), scarf);
            var nose = P(PrimitiveType.Cylinder, t, new Vector3(0f, 1.95f, 0.38f), new Vector3(0.07f, 0.16f, 0.07f), new Vector3(90f, 0f, 0f), carrot);
            for (int s = -1; s <= 1; s += 2)
            {
                P(PrimitiveType.Sphere, t, new Vector3(s * 0.11f, 2.06f, 0.26f), Vector3.one * 0.07f, Vector3.zero, coal);
                Log(t, new Vector3(s * 0.38f, 1.35f, 0f), new Vector3(s * 0.95f, 1.75f, 0.05f), 0.035f, darkWood);
            }
            for (int i = 0; i < 3; i++) P(PrimitiveType.Sphere, t, new Vector3(0f, 1.15f + i * 0.17f, 0.42f - Mathf.Abs(i - 1) * 0.02f), Vector3.one * 0.07f, Vector3.zero, coal);
            P(PrimitiveType.Cylinder, t, new Vector3(0f, 2.35f, 0f), new Vector3(0.42f, 0.2f, 0.42f), Vector3.zero, coal);
            P(PrimitiveType.Cylinder, t, new Vector3(0f, 2.18f, 0f), new Vector3(0.62f, 0.02f, 0.62f), Vector3.zero, coal);
            Block(t, Vector3.zero, new Vector2(0.6f, 0.6f), t.gameObject);
            return t;
        }

        void Flames(Transform t, Vector3 at)
        {
            var go = new GameObject("Flames");
            go.transform.SetParent(t, false); go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(2.4f, 1.2f, 0.3f, 0.9f), new Color(2.6f, 0.6f, 0.1f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.3f;
            var em = ps.emission; em.rateOverTime = 40f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.5f), new GradientColorKey(new Color(0.5f, 0.1f, 0.05f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            Soft(go.GetComponent<ParticleSystemRenderer>());
            ps.Play();
        }

        void Smoke(Transform t, Vector3 at)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(t, false); go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startColor = new Color(0.82f, 0.84f, 0.88f, 0.4f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 0.2f;
            var em = ps.emission; em.rateOverTime = 6f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.3f, 0.6f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.45f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            Soft(go.GetComponent<ParticleSystemRenderer>());
            ps.Play();
        }

        /// <summary>The game's soft particle material, if it gave us one.</summary>
        public Material soft;
        void Soft(ParticleSystemRenderer r)
        {
            if (soft == null) return;
            var m = new Material(soft);
            m.SetColor("_BaseColor", Color.white);
            r.sharedMaterial = m;
        }

        void Strings()
        {
            Vector3 pa = Ground(5f, -112f), pb = Ground(6f, -90f);
            // two tall poles for the flags and lights to hang from
            var poles = new GameObject("Poles").transform; poles.SetParent(root, false);
            Log(poles, pa + Vector3.down * 0.3f, pa + Vector3.up * 6.6f, 0.16f, logs);
            Log(poles, pb + Vector3.down * 0.3f, pb + Vector3.up * 6.2f, 0.16f, logs);
            Block(poles, poles.InverseTransformPoint(pa), new Vector2(0.2f, 0.2f));
            Block(poles, poles.InverseTransformPoint(pb), new Vector2(0.2f, 0.2f));
            Vector3 apex = lodgeApex;
            // high over the camp, clear of the camera
            Flags(apex, pa + Vector3.up * 6.5f);
            Flags(pa + Vector3.up * 6.5f, pb + Vector3.up * 6.1f);
            Vector3 porchR = deckR, porchL = deckL;
            Vector3 benchFL = Ground(12f, -104f) + Quaternion.Euler(0f, -90f, 0f) * new Vector3(2.2f, 2.55f, 1.4f);
            Vector3 boardTop = Ground(-11f, -99f) + new Vector3(0f, 3.15f, 0f);
            Lights(porchR, pa + Vector3.up * 5.2f);
            Lights(pa + Vector3.up * 5.2f, benchFL);
            Lights(porchL + Vector3.up * 0.15f, porchR + Vector3.up * 0.15f, 0.012f);   // along the deck rail, pulled tight
            Lights(boardTop, pb + Vector3.up * 5.0f);
        }

        static Vector3 Sag(Vector3 a, Vector3 b, float u, float sag) => Vector3.Lerp(a, b, u) + Vector3.down * sag * (1f - (2f * u - 1f) * (2f * u - 1f));

        void Line(Transform parent, Vector3 a, Vector3 b, float sag, float thick, Material m, out int count)
        {
            count = Mathf.Max(6, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.8f));
            for (int i = 0; i < count; i++)
                Log(parent, Sag(a, b, i / (float)count, sag), Sag(a, b, (i + 1f) / count, sag), thick, m).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Flags(Vector3 a, Vector3 b)
        {
            var t = flags.transform;
            float len = Vector3.Distance(a, b), sag = len * 0.07f;
            Line(t, a, b, sag, 0.02f, rope, out _);
            var along = (b - a); along.y = 0f; along.Normalize();
            int n = Mathf.FloorToInt(len / 0.5f);
            for (int i = 1; i < n; i++)
            {
                float u = i / (float)n;
                var p = Sag(a, b, u, sag);
                var f = P(PrimitiveType.Quad, t, p + Vector3.down * 0.17f, new Vector3(0.26f, 0.32f, 1f), Vector3.zero, flagCols[i % flagCols.Length]);
                f.rotation = Quaternion.LookRotation(Vector3.Cross(along, Vector3.up), Vector3.up) * Quaternion.Euler(Mathf.Sin(i * 1.7f) * 8f, 0f, 0f);
            }
        }

        void Lights(Vector3 a, Vector3 b, float droop = 0.09f)
        {
            var t = lights.transform;
            float len = Vector3.Distance(a, b), sag = len * droop;
            Line(t, a, b, sag, 0.015f, wire, out _);
            int n = Mathf.FloorToInt(len / 0.55f);
            for (int i = 1; i < n; i++)
                P(PrimitiveType.Sphere, t, Sag(a, b, i / (float)n, sag) + Vector3.down * 0.07f, new Vector3(0.11f, 0.15f, 0.11f), Vector3.zero, bulb, false);
        }

        /// <summary>Snowy pines round the back and sides of the camp, so it sits in a clearing.</summary>
        void Trees()
        {
            var kinds = Resources.LoadAll<GameObject>("Trees");
            if (kinds.Length == 0) return;
            var where = new[] { new Vector2(-27f, -126f), new Vector2(-19f, -134f), new Vector2(-6f, -137f), new Vector2(7f, -135f), new Vector2(19f, -131f),
                                new Vector2(28f, -121f), new Vector2(-30f, -112f), new Vector2(-29f, -97f), new Vector2(30f, -106f), new Vector2(29f, -93f),
                                new Vector2(-24f, -138f), new Vector2(13f, -140f), new Vector2(-31f, -84f) };
            for (int i = 0; i < where.Length; i++)
            {
                var g = Instantiate(kinds[i % kinds.Length], root, false);
                foreach (var c in g.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
                var rs = g.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float want = 7.5f + 3.5f * Slope.Hash01(i, 3, 77);
                g.transform.localScale *= want / Mathf.Max(0.1f, b.size.y);
                var at = Ground(where[i].x, where[i].y);
                g.transform.position = at + Vector3.down * 0.3f;
                g.transform.rotation = Quaternion.Euler(0f, i * 67f, 0f) * g.transform.rotation;
            }
        }

        // ---------------------------------------------------------------- what you own shows up

        /// <summary>Show bought decorations, today's mission notes, and the board on the bench.</summary>
        public void Redress()
        {
            if (root == null) return;
            if (firePit != null) firePit.SetActive(Progress.Has(Progress.Up.FirePit));
            if (flags != null) flags.SetActive(Progress.Has(Progress.Up.Flags));
            if (lights != null) lights.SetActive(Progress.Has(Progress.Up.Lights));
            if (snowman != null) snowman.SetActive(Progress.Has(Progress.Up.Snowman));
            if (benchBoard != null) benchBoard.sharedMaterial.SetColor("_BaseColor", Progress.Boards[Progress.Board]);
            Missions.Refresh();
            if (notes != null)
                for (int i = 0; i < notes.Length && i < Missions.Today.Count; i++)
                    notes[i].sharedMaterial.SetColor("_BaseColor", Missions.Today[i].done ? new Color(0.62f, 0.95f, 0.62f) : Color.white);
        }

        // ---------------------------------------------------------------- walking

        Rider rider; RiderBody body; Camera cam;
        bool entered;
        Vector3 pos; float yaw, speed, camYaw, lookBlend, showBlend;
        bool snap;
        public Vector3 WalkerPosition => pos;

        /// <summary>Put the rider in the camp on foot, by the lodge, and take over the camera.</summary>
        public void Enter(Rider r, RiderBody b, Camera c)
        {
            rider = r; body = b; cam = c;
            Active = true;
            // arrive on the path by the fire, looking up at the lodge
            pos = Ground(-1.5f, -94f);
            var toLodge = lodgeApex - pos;
            yaw = Mathf.Atan2(toLodge.x, toLodge.z); camYaw = yaw; speed = 0f;
            if (!entered) { entered = true; SetNight(1f); }   // the game opens straight into the evening camp
            snap = true;
            Near = Station.None;
            body.SetWalking(true);
            Place();
            Redress();
        }

        public void Leave()
        {
            if (!Active) return;
            Active = false;
            Showcase = false;
            if (body != null) body.SetWalking(false);
        }

        /// <summary>Walk with a stick (x right, y away from the camera; length 0..1). True once you've walked through the gate.</summary>
        public bool Walk(Vector2 stick, float dt)
        {
            if (!Active) return false;
            float mag = Mathf.Clamp01(stick.magnitude);
            if (mag > 0.08f)
            {
                // the stick is relative to the camera: up the screen is the way it looks
                float want = camYaw + Mathf.Atan2(stick.x, stick.y);
                yaw = Mathf.LerpAngle(yaw * Mathf.Rad2Deg, want * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 10f)) * Mathf.Deg2Rad;
            }
            speed = Mathf.MoveTowards(speed, mag * MaxSpeed, dt * 12f);
            var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 next = pos + f * speed * dt;
            // through the gate's opening, and you're off to the start
            float gx = next.x - Cx;
            bool inGate = Mathf.Abs(gx) < 2.5f;
            if (next.z > GateZ + 0.6f && inGate) { pos = next; Place(); return true; }
            next.x = Cx + Mathf.Clamp(gx, -HalfWidth, HalfWidth);
            next.z = Mathf.Clamp(next.z, BackZ, inGate ? GateZ + 1f : GateZ - 0.6f);
            var p2 = new Vector2(next.x, next.z);
            for (int it = 0; it < 2; it++)
                foreach (var b in boxes)
                {
                    if (b.floor || (b.owner != null && !b.owner.activeInHierarchy)) continue;
                    p2 = PushOut(b, p2, 0.38f);
                }
            pos = new Vector3(p2.x, 0f, p2.y);
            Place();
            // the nearest station you're standing at
            Near = Station.None; OnLookout = false;
            float best = float.MaxValue;
            foreach (var s in spots)
            {
                var d = new Vector2(s.at.x - pos.x, s.at.z - pos.z).magnitude;
                if (d < s.radius && d < best) { best = d; Near = s.st; }
            }
            if (Near == Station.Lookout) { OnLookout = true; Near = Station.None; }
            return false;
        }

        static Vector2 PushOut(Box b, Vector2 p, float r)
        {
            var d = p - b.c;
            // into the box's own frame
            var q = new Vector2(d.x * b.cos - d.y * b.sin, d.x * b.sin + d.y * b.cos);
            var cl = new Vector2(Mathf.Clamp(q.x, -b.h.x, b.h.x), Mathf.Clamp(q.y, -b.h.y, b.h.y));
            var off = q - cl;
            if (off.sqrMagnitude > r * r) return p;
            if (off.sqrMagnitude < 1e-6f)
            {
                // inside: out the nearest side
                float ex = b.h.x - Mathf.Abs(q.x), ez = b.h.y - Mathf.Abs(q.y);
                if (ex < ez) q.x = Mathf.Sign(q.x) * (b.h.x + r); else q.y = Mathf.Sign(q.y) * (b.h.y + r);
            }
            else q = cl + off.normalized * r;
            return b.c + new Vector2(q.x * b.cos + q.y * b.sin, -q.x * b.sin + q.y * b.cos);
        }

        float FloorAt(Vector3 p)
        {
            float y = Slope.DrawnHeight(p.x, p.z);
            foreach (var b in boxes)
            {
                if (!b.floor) continue;
                var d = new Vector2(p.x, p.z) - b.c;
                var q = new Vector2(d.x * b.cos - d.y * b.sin, d.x * b.sin + d.y * b.cos);
                // a ramp (the deck stairs) falls by drop from its -z end to its +z end
                if (Mathf.Abs(q.x) <= b.h.x && Mathf.Abs(q.y) <= b.h.y) y = Mathf.Max(y, b.top - b.drop * (q.y + b.h.y) / (2f * b.h.y));
            }
            return y;
        }

        void Place()
        {
            pos.y = FloorAt(pos);
            var p = pos + Vector3.up * Rider.R;
            rider.Body.position = p;
            rider.transform.position = p;
            // the model faces the board's toe edge (+X): turn it a quarter so it faces where it walks
            rider.Heading = yaw - Mathf.PI * 0.5f;
            body.WalkPace = speed / MaxSpeed;
        }

        /// <summary>The station under a tap on the screen, if any.</summary>
        public Station StationAt(Vector2 screen)
        {
            if (cam == null) return Station.None;
            var ray = cam.ScreenPointToRay(screen);
            foreach (var h in Physics.RaycastAll(ray, 80f, ~0, QueryTriggerInteraction.Collide))
                foreach (var s in spots)
                    if (h.collider.transform == s.root) return s.st;
            return Station.None;
        }

        // ---------------------------------------------------------------- the controls

        bool wasDown, dragging, pressOnUi;
        Vector2 pressAt, lastP;
        float pressT;

        /// <summary>
        /// One frame in camp: a thumb dragged anywhere is the walk stick, a quick tap on a station opens it,
        /// and the menus take their own taps. True when it's time to drop in.
        /// </summary>
        public bool Step(float dt, MetaUi ui)
        {
            bool down = Pointer(out Vector2 p);
            Vector2 stick = Vector2.zero;
            if (down && !wasDown)
            {
                pressAt = p; pressT = 0f; dragging = false;
                string id = ui.Hit(p);
                pressOnUi = id != null || ui.PanelOpen;
                if (id != null)
                {
                    wasDown = true;
                    if (id == "context") return Open(Near, ui);
                    ui.Act(id);
                    return false;
                }
            }
            float radius = Mathf.Max(60f, Mathf.Min(Screen.width, Screen.height) * 0.14f);
            if (down && !pressOnUi)
            {
                pressT += dt;
                var d = p - pressAt;
                if (!dragging && d.magnitude > radius * 0.12f) dragging = true;
                // the stick's centre trails the thumb past full throw, so turning round is instant
                if (d.magnitude > radius) { pressAt = p - d.normalized * radius; d = p - pressAt; }
                if (dragging) stick = d / radius;
            }
            else if (!down && wasDown && !pressOnUi && !dragging && pressT < 0.4f)
            {
                wasDown = false;
                if (Open(StationAt(lastP), ui)) return true;
            }
            if (down) lastP = p;
            wasDown = down;
            ui.SetStick(down && dragging && !pressOnUi, pressAt, p);
            stick = ui.PanelOpen ? Vector2.zero : stick + Keys();
            Showcase = ui.PanelOpen && ui.ShopTab == 1;
            if (Walk(stick, dt)) return true;
            ui.SetContext(Near);
            return false;
        }

        bool Open(Station st, MetaUi ui)
        {
            if (st == Station.Missions) ui.ShowMissions();
            else if (st == Station.Upgrades) ui.ShowShop(0);
            else if (st == Station.Style) ui.ShowShop(1);
            return st == Station.DropIn;
        }

        static bool Pointer(out Vector2 p)
        {
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.isPressed) { p = ts.primaryTouch.position.ReadValue(); return true; }
            var ms = Mouse.current;
            if (ms != null && ms.leftButton.isPressed) { p = ms.position.ReadValue(); return true; }
            p = Vector2.zero; return false;
#else
            if (Input.touchCount > 0) { p = Input.GetTouch(0).position; return true; }
            p = Input.mousePosition; return Input.GetMouseButton(0);
#endif
        }

        /// <summary>WASD / arrow keys, for the editor.</summary>
        static Vector2 Keys()
        {
            var v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return v;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1f;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1f;
            if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1f;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1f;
#else
            v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
            return Vector2.ClampMagnitude(v, 1f);
        }

        // ---------------------------------------------------------------- camera

        void LateUpdate() { if (Active) CameraStep(Time.deltaTime); }

        /// <summary>Move the camera (public for the test tools, which have no frame time).</summary>
        public void CameraStep(float dt)
        {
            if (cam == null) return;
            if (ChaseCamera.TestDt > 0f) dt = ChaseCamera.TestDt;
            float k(float rate) => snap ? 1f : 1f - Mathf.Exp(-dt * rate);
            // follow behind as you walk; standing still, it holds where it is
            if (speed > 0.4f) camYaw = Mathf.LerpAngle(camYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, k(1.4f) * Mathf.Clamp01(speed / MaxSpeed)) * Mathf.Deg2Rad;
            lookBlend = Mathf.Lerp(lookBlend, OnLookout && speed < 0.5f ? 1f : 0f, k(2f));
            showBlend = Mathf.Lerp(showBlend, Showcase ? 1f : 0f, k(4f));
            var f = new Vector3(Mathf.Sin(camYaw), 0f, Mathf.Cos(camYaw));
            Vector3 target = pos + Vector3.up * 1.25f;
            Vector3 want = target - f * 6.4f + Vector3.up * 2.7f;
            Vector3 look = target + f * 4f + Vector3.up * 0.15f;
            // pull in if the lodge or a station is in the way
            Vector3 dir = want - target;
            foreach (var h in Physics.RaycastAll(target, dir.normalized, dir.magnitude, ~0, QueryTriggerInteraction.Collide))
                if (h.collider.transform.IsChildOf(root) && h.distance < dir.magnitude)
                    want = target + dir.normalized * Mathf.Max(1.2f, h.distance - 0.3f);
            want.y = Mathf.Max(want.y, Slope.DrawnHeight(want.x, want.z) + 0.8f);
            // at the lookout, stand back and take in the whole mountain below
            Vector3 lookPos = pos + new Vector3(-0.8f, 4.2f, -5.6f), lookAt = pos + new Vector3(0f, -55f, 190f);
            want = Vector3.Lerp(want, lookPos, lookBlend); look = Vector3.Lerp(look, lookAt, lookBlend);
            // picking an outfit: face the rider, close, with room for the menu below
            var front = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 showPos = pos + front * 6f + Vector3.up * 1.6f, showAt = pos - Vector3.up * 0.1f;
            want = Vector3.Lerp(want, showPos, showBlend); look = Vector3.Lerp(look, showAt, showBlend);
            Vector3 cp = snap ? want : Vector3.Lerp(cam.transform.position, want, k(6f));
            cam.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(look - cp, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(Mathf.Lerp(62f, 54f, lookBlend), 42f, showBlend);
            snap = false;
        }

        /// <summary>Test tools: stand here, facing this way.</summary>
        public void Teleport(float dx, float z, float yawDeg, bool snapCam = true)
        {
            pos = Ground(dx, z); yaw = yawDeg * Mathf.Deg2Rad; camYaw = yaw; speed = 0f;
            snap = snapCam;
            Place();
            Walk(Vector2.zero, 0f);
        }
    }
}
