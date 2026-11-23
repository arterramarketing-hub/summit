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
    public partial class Camp : MonoBehaviour
    {
        public enum Station { None, Missions, Upgrades, Style, DropIn, Lookout, Talk, Roast, Cocoa }   // (the last three: the fire and the kitchen, CampFire.cs)

        // where the camp sits: on the plateau behind the start pad (downhill is +Z), so the run is right
        // in front of you and the view down the mountain is past the Drop In gate. It stands close behind the gate:
        // the chalet's steps are some 35 m from DROP IN (it was 30 m further back, on ground the start no longer
        // uses now a run starts at the gate, and the walk down took over 20 seconds)
        public const float BackZ = -96f, HalfWidth = 23f;   // (the camp's extent, for keeping trees off it: you can walk anywhere)
        static float Cx => Slope.Centre(Slope.PadLip);
        /// <summary>True inside the camp and its framing trees (the forest leaves this ground alone).</summary>
        public static bool InCamp(float x, float z) => z < -38f && z > BackZ - 16f && Mathf.Abs(x - Cx) < HalfWidth + 10f
            || z < -34f && z > -360f && x - Cx > Slope.SummitRight - 1f && x - Cx < 175f;   // (and no trees down the cliff's rock)

        public bool Active { get; private set; }
        public Station Near { get; private set; }
        public bool OnLookout { get; private set; }
        /// <summary>The camera shows the rider from the front (while picking an outfit).</summary>
        public bool Showcase;
        public const float MaxSpeed = 3.4f, RunSpeed = 7.2f;   // walking; running with the stick pushed right out

        Shader lit;
        Transform root;
        Material wood, logs, darkWood, roof, snowCap, stone, metal, glass, bulb, wire, ember, rope, carrot, coal, scarf, birch, path;
        readonly Material[] flagCols = new Material[5];
        Material[] noteMats;
        GameObject firePit, flags, lights, snowman;

        class Spot { public Station st; public Vector3 at; public float radius; public Transform root; }
        readonly List<Spot> spots = new List<Spot>();
        struct Box { public Vector2 c, h; public float cos, sin, top, drop, lo, hi; public GameObject owner; public bool floor; }
        readonly List<Box> boxes = new List<Box>();

        // ---------------------------------------------------------------- materials

        public void Build(Shader litShader)
        {
            lit = litShader;
            if (CampLife.On && GetComponent<CampLife>() == null) gameObject.AddComponent<CampLife>().Setup(this);   // the people living here (CampLife.cs)
            if (LodgeFolk.On && GetComponent<LodgeFolk>() == null) gameObject.AddComponent<LodgeFolk>().Setup(this);   // and the lodge's own, in their places (LodgeFolk.cs)
            var woodTex = Resources.Load<Texture2D>("Camp/Wood");
            var logTex = Resources.Load<Texture2D>("Camp/Logs");
            wood = M(new Color(0.95f, 0.88f, 0.8f), 0.18f, woodTex);
            logs = M(new Color(1f, 0.92f, 0.85f), 0.12f, logTex, new Vector2(1f, 2f));
            darkWood = M(new Color(0.55f, 0.45f, 0.38f), 0.2f, woodTex);
            roof = M(new Color(0.28f, 0.20f, 0.17f), 0.15f, woodTex, new Vector2(2f, 3f));
            snowCap = M(new Color(0.92f, 0.95f, 1f), 0.35f);
            stone = M(new Color(0.95f, 0.95f, 0.95f), 0.1f, Resources.Load<Texture2D>("Camp/Stone"), new Vector2(1f, 1f));
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
            shadowMat = M(Color.white, 0f); shadowMat.SetFloat("_Cull", 0f);   // both sides: whichever way the light comes
            Color[] fc = { new Color(0.15f, 0.35f, 0.85f), new Color(0.95f, 0.95f, 0.95f), new Color(0.85f, 0.15f, 0.12f), new Color(0.15f, 0.6f, 0.25f), new Color(0.98f, 0.8f, 0.12f) };
            for (int i = 0; i < fc.Length; i++) { flagCols[i] = M(fc[i], 0.1f); flagCols[i].SetFloat("_Cull", 0f); }
            noteMats = new Material[3];
            for (int i = 0; i < 3; i++) noteMats[i] = Sign("Camp/Note" + i);
            Progress.Changed += Redress;
            // Val and Breanna, living here while they aren't the one being played (CampCouple)
            Couple = new GameObject("Couple").AddComponent<CampCouple>();
            Couple.transform.SetParent(transform, false);
            Couple.Setup(this);
            CaptureDay();
            SetNight(0f);   // daylight until the day/night cycle says otherwise
        }

        // ---- what each build makes for itself (its materials, meshes, textures): gone with the camp it was made for.
        // Camp.Rebuild runs at every run start (and when the LODGE setting changes layout); destroying only the root
        // GameObject left these behind (with no scene loads, UnloadUnusedAssets never comes for them).
        readonly List<Object> made = new List<Object>();
        bool building;
        /// <summary>The original trees' swaying copies of their materials, by the material copied: made once, kept.</summary>
        readonly Dictionary<Material, Material> swayed = new Dictionary<Material, Material>();
        /// <summary>Something this build made: destroyed when the camp is next rebuilt (made outside a build, e.g. in Build(), it stays).</summary>
        T Made<T>(T o) where T : Object { if (building && o != null) made.Add(o); return o; }
        void Unmake()
        {
            foreach (var o in made) if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
            made.Clear();
            // and forget what a build keeps to reuse within it (made once, behind `if (x == null)`): Destroy only takes
            // effect at the end of the frame, so in the Rebuild that follows they still test as alive, were handed to
            // the new camp, and were gone a moment later: the cable railing, its kerb, the flags and the lodge's path
            // drawn magenta (an empty material slot) every second camp (purplefix)
            railMetal = kerbMat = flagMat = paver = null;
        }
        /// <summary>Test tools: how many things the current build made for itself.</summary>
        public int MadeCount => made.Count;

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
            return Made(m);
        }

        Material Sign(string path)
        {
            var m = Made(CourseDressing.Sign(lit, path));
            m.SetFloat("_Smoothness", 0.15f);
            // (no self-light: lit by the camp's lamps and the sky like real carved boards; SignLit.shader)
            return m;
        }

        // ---------------------------------------------------------------- building

        /// <summary>Build the camp for the current mountain (call after Slope.Init; it sits on the snow).</summary>
        public void Rebuild()
        {
            if (lit == null) return;
            if (alyssa != null) { alyssa.Release(); alyssa = null; }   // (what she made for herself: CampFire.cs; before she goes with the root)
            if (root != null) { if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject); }
            Unmake();   // (the last build's materials and meshes with it)
            building = true;
            root = new GameObject("Camp").transform;
            root.SetParent(transform, false);
            spots.Clear(); boxes.Clear(); lamps.Clear(); lampPower.Clear(); lampDay.Clear(); cameraWalls.Clear();

            tapRoots.Clear();
            if (UseLodge) Lodge(Frame(LodgeDx, LodgeZ, LodgeYaw));   // (CampLodge.cs)
            else Chalet(Frame(-9f, -88.5f, 12f));
            StartBanner();
            Lookout(Frame(16f, -54f, 0f));
            Benches();
            firePit = FirePit(Frame(0f, -71f, 0f)).gameObject;
            snowman = Snowman(Frame(11.5f, -61f, 180f)).gameObject;
            Campfire();       // Alyssa and the stumps at the fire (CampFire.cs)
            CocoaCorner();    // the hot chocolate in the kitchen (CampCocoa.cs)
            flags = new GameObject("Flags"); flags.transform.SetParent(root, false);
            lights = new GameObject("Lights"); lights.transform.SetParent(root, false);
            Strings();
            Trees();
            Birches();
            Mounds();
            Footpath();
            CliffEdge();
            Mist();
            Physics.SyncTransforms();
            Redress();
            SetNight(Night);   // the new lamps start at the evening's brightness
            if (Couple != null) Couple.Rebuilt();   // (their spots moved with the camp)
            building = false;
            Built?.Invoke(root);   // (the camp's people: CampLife)
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
        /// <summary>(lo and hi: the heights it blocks at, in the frame; a walker is blocked if its body overlaps them.)</summary>
        void Block(Transform frame, Vector3 local, Vector2 half, GameObject owner = null, bool floor = false, float top = 0f, float drop = 0f, float lo = -1000f, float hi = 1000f)
        {
            Vector3 w = frame.TransformPoint(local);
            float yaw = frame.eulerAngles.y * Mathf.Deg2Rad;
            float y0 = frame.position.y;
            boxes.Add(new Box { c = new Vector2(w.x, w.z), h = half, cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw), owner = owner, floor = floor, top = y0 + top, drop = drop, lo = y0 + lo, hi = y0 + hi });
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

        // ---------------------------------------------------------------- the chalet

        // Modelled in Blender by Tools/MakeChalet.py (Resources/Camp/Chalet.fbx): a Swiss chalet outside, a
        // modern great room inside with the stations in it. Its helper boxes say where the walls, floors,
        // stairs, stations and lamps are; this turns them into walking, the camera's walls and lights.
        public const float ChaletHalfX = 8f, ChaletHalfZ = 6f;
        Transform chalet;
        Vector3 lodgeApex, deckL, deckR, stairsFoot;
        Material boardPaint;
        readonly Material[] swatches = new Material[5];

        void Chalet(Transform t)
        {
            t.name = "Chalet";
            chalet = t;
            // the floor sits level on the highest snow under the house; the stone base runs down into the rest
            float baseY = float.MinValue;
            for (int i = 0; i <= 6; i++)
                for (int j = 0; j <= 4; j++)
                {
                    var w = t.TransformPoint(new Vector3(-ChaletHalfX + i * 2f * ChaletHalfX / 6f, 0f, -ChaletHalfZ + j * 2f * ChaletHalfZ / 4f));
                    baseY = Mathf.Max(baseY, Slope.DrawnHeight(w.x, w.z));
                }
            t.position = new Vector3(t.position.x, baseY + 0.25f, t.position.z);
            var prefab = Resources.Load<GameObject>("Camp/Chalet");
            if (prefab == null) { Debug.LogWarning("Camp: Resources/Camp/Chalet.fbx is missing"); return; }
            var model = Instantiate(prefab, t, false).transform;
            model.name = "Model";
            // (keep the importer's own rotation and scale on the root: it's how Blender's units and axes come in)
            model.localPosition = Vector3.zero;
            // the file is made with its front door on +Z: check, and turn it round if the axes came in otherwise
            var front = Find(model, "MARK_front");
            if (front != null)
            {
                var fl = t.InverseTransformPoint(front.GetComponent<Renderer>().bounds.center); fl.y = 0f;
                float turn = fl.sqrMagnitude > 0.01f ? Vector3.SignedAngle(fl, Vector3.forward, Vector3.up) : 0f;
                if (Mathf.Abs(turn) > 1f) { Debug.LogWarning("Camp: chalet came in turned " + turn.ToString("0") + " degrees"); model.RotateAround(t.position, t.up, turn); }
            }
            Physics.SyncTransforms();
            var mats = new Dictionary<string, Material>();
            var helpers = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("__")) { helpers.Add(r); continue; }
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = ChaletMat(ms[i] != null ? ms[i].name.Split(' ')[0] : r.name, mats);
                r.sharedMaterials = ms;
                bool glassy = r.name == "Glass" || r.name == "LED";
                r.shadowCastingMode = glassy ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // the helper boxes: their extents, in the chalet's own frame
            foreach (var r in helpers)
            {
                var mf = r.GetComponent<MeshFilter>();
                var b = mf.sharedMesh.bounds;
                Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
                for (int c = 0; c < 8; c++)
                {
                    var p = t.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
                Helper(t, r.name.Substring(0, r.name.IndexOf("__")), lo, hi);
                if (Application.isPlaying) Destroy(r.gameObject); else DestroyImmediate(r.gameObject);
            }
            // a stone terrace in front under the balcony, and steps down to the snow where it's lower
            float ty = 0f;
            P(PrimitiveType.Cube, t, new Vector3(0f, -0.7f, 7.3f), new Vector3(2f * ChaletHalfX + 0.2f, 1.4f, 2.6f), Vector3.zero, stone);
            Block(t, new Vector3(0f, 0f, 7.3f), new Vector2(ChaletHalfX + 0.1f, 1.3f), null, true, ty);
            var footW = t.TransformPoint(new Vector3(0f, 0f, 10.6f));
            float foot = Mathf.Min(0f, Slope.DrawnHeight(footW.x, footW.z) - t.position.y);
            const int steps = 5; const float run = 2f;
            for (int k = 0; k < steps; k++)
            {
                float top = Mathf.Lerp(ty, foot, (k + 1f) / (steps + 1f)), z = 8.6f + (k + 0.5f) * run / steps;
                P(PrimitiveType.Cube, t, new Vector3(0f, (top + foot - 0.6f) * 0.5f, z), new Vector3(4f, top - foot + 0.6f, run / steps + 0.02f), Vector3.zero, stone);
            }
            Block(t, new Vector3(0f, 0f, 8.6f + run * 0.5f), new Vector2(2f, run * 0.5f), null, true, ty, ty - foot);
            // the terrace's edges either side of the steps: a low wall
            for (int s = -1; s <= 1; s += 2)
            {
                P(PrimitiveType.Cube, t, new Vector3(s * 5.1f, 0.25f, 8.55f), new Vector3(6f, 0.5f, 0.3f), Vector3.zero, stone);
                Block(t, new Vector3(s * 5.1f, 0f, 8.55f), new Vector2(3f, 0.15f), null, false, 0f, 0f, -2f, 0.5f);
            }
            lodgeApex = t.TransformPoint(new Vector3(0f, 9.3f, 6.2f));
            deckL = t.TransformPoint(new Vector3(-7.7f, 4.5f, 7.65f));
            deckR = t.TransformPoint(new Vector3(7.7f, 4.5f, 7.65f));
            stairsFoot = t.TransformPoint(new Vector3(0f, foot, 11.2f));
        }

        static Transform Find(Transform t, string prefix)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name.StartsWith(prefix)) return c;
            return null;
        }

        /// <summary>What one of the model's helper boxes means (see Tools/MakeChalet.py).</summary>
        void Helper(Transform t, string name, Vector3 lo, Vector3 hi)
        {
            Vector3 c = (lo + hi) * 0.5f, size = hi - lo;
            var half = new Vector2(size.x * 0.5f, size.z * 0.5f);
            Vector3 at = new Vector3(c.x, 0f, c.z);
            float y0 = t.position.y;
            if (name.StartsWith("COL_") || name.StartsWith("CAM_") || name.StartsWith("TAP_"))
            {
                // (the camera stops at anything named COL_; CAM_ ones don't block walking: you climb up past a slab's edge)
                var g = new GameObject(name.StartsWith("CAM_") ? "COL_" + name : name).transform;
                g.SetParent(t, false); g.localPosition = c;
                var bc = g.gameObject.AddComponent<BoxCollider>();
                bc.size = size;
                if (!name.StartsWith("TAP_")) cameraWalls.Add(bc);
                if (name.StartsWith("TAP_"))
                {
                    var st = name == "TAP_Missions" ? Station.Missions : name == "TAP_Upgrades" ? Station.Upgrades : Station.Style;
                    tapRoots[st] = g;
                    return;
                }
                if (name.StartsWith("COL_")) Block(t, at, half, null, false, 0f, 0f, lo.y, hi.y);
            }
            else if (name.StartsWith("BLK_")) Block(t, at, half, null, false, 0f, 0f, lo.y, hi.y);
            else if (name.StartsWith("FLR_")) Block(t, at, half, null, true, hi.y);
            else if (name.StartsWith("RMP_")) Block(t, at, half, null, true, hi.y, hi.y - lo.y);
            else if (name.StartsWith("SPOT_"))
            {
                var st = name == "SPOT_Missions" ? Station.Missions : name == "SPOT_Upgrades" ? Station.Upgrades : Station.Style;
                spots.Add(new Spot { st = st, at = t.TransformPoint(c), radius = 1.7f, root = tapRoots.TryGetValue(st, out var tr) ? tr : t });
            }
            else if (name == "FX_fire") Flames(t, c);
            else if (name.StartsWith("LIGHT_"))
            {
                bool outside = name == "LIGHT_porch";
                var l = Lamp(t, c, name == "LIGHT_lounge" ? 10f : name == "LIGHT_porch" ? 7f : 7.5f, name == "LIGHT_lounge" ? 2.2f : 1.5f, outside ? 0f : 0.5f);
                if (name == "LIGHT_fire") l.color = new Color(1f, 0.72f, 0.45f);
            }
        }
        readonly Dictionary<Station, Transform> tapRoots = new Dictionary<Station, Transform>();
        // what stops the camera (the chalet's COL_ and CAM_ boxes), and a reused buffer for its ray (no garbage a frame)
        readonly HashSet<Collider> cameraWalls = new HashSet<Collider>();
        readonly RaycastHit[] rayHits = new RaycastHit[24];

        /// <summary>The game's material for one of the model's (by its name in the file).</summary>
        Material ChaletMat(string name, Dictionary<string, Material> made)
        {
            if (made.TryGetValue(name, out var have)) return have;
            var woodTex = Resources.Load<Texture2D>("Camp/Wood");
            var oakTex = Resources.Load<Texture2D>("Camp/Oak");
            Material m;
            switch (name)
            {
                case "Timber": m = M(new Color(1f, 0.84f, 0.64f), 0.15f, oakTex); break;          // honey, weathered
                case "TimberDark": m = M(new Color(0.8f, 0.64f, 0.48f), 0.15f, woodTex); break;
                case "Stone": m = stone; break;
                case "Roof": m = M(new Color(0.42f, 0.33f, 0.27f), 0.12f, woodTex, new Vector2(1f, 3f)); break;
                case "Glass": m = Glass(); break;
                case "Plaster": m = M(new Color(0.93f, 0.92f, 0.89f), 0.08f); break;
                case "Oak": m = M(Color.white, 0.35f, oakTex); break;
                case "Concrete": m = M(new Color(0.62f, 0.62f, 0.62f), 0.55f); break;
                case "Steel": m = M(new Color(0.05f, 0.05f, 0.06f), 0.65f); m.SetFloat("_Metallic", 0.6f); break;
                case "LED": m = M(new Color(0.3f, 0.26f, 0.2f), 0.5f, null, default, new Color(1f, 0.78f, 0.5f) * 1.6f); break;
                case "Ember": m = ember; break;
                case "Fabric": m = M(new Color(0.66f, 0.63f, 0.58f), 0.05f); break;
                case "Rug": m = M(new Color(0.84f, 0.8f, 0.72f), 0.02f); break;
                case "Plant": m = M(new Color(0.16f, 0.38f, 0.2f), 0.2f); break;
                case "ArtA": m = M(new Color(0.88f, 0.48f, 0.22f), 0.3f); break;
                case "ArtB": m = M(new Color(0.12f, 0.27f, 0.52f), 0.3f); break;
                case "BoardPaint": m = boardPaint = M(Progress.Boards[Progress.Board], 0.6f); break;
                case "SignCamp": case "SignMissions": case "SignUpgrades": m = Sign("Camp/" + name); break;
                default:
                    if (name.StartsWith("Note") && int.TryParse(name.Substring(4), out int ni) && ni < noteMats.Length) { m = noteMats[ni]; break; }
                    if (name.StartsWith("Swatch") && int.TryParse(name.Substring(6), out int si)) { m = swatches[si - 1] = M(Progress.Boards[si % Progress.Boards.Length], 0.6f); break; }
                    m = M(new Color(0.7f, 0.7f, 0.7f), 0.2f); break;
            }
            made[name] = m;
            return m;
        }

        /// <summary>Clear glass (Resources/Camp/GlassBase is a transparent Lit material, so the build keeps that shader variant).</summary>
        Material Glass()
        {
            var b = Resources.Load<Material>("Camp/GlassBase");
            var m = b != null ? Made(new Material(b)) : M(Color.white, 0.95f);
            m.SetColor("_BaseColor", new Color(0.62f, 0.74f, 0.82f, 0.22f));
            m.SetFloat("_Smoothness", 0.95f);
            return m;
        }

        /// <summary>A point in the chalet (its own frame) in the world: for the test tools.</summary>
        public Vector3 ChaletPoint(float x, float y, float z) => chalet != null ? chalet.TransformPoint(new Vector3(x, y, z)) : Vector3.zero;
        public float ChaletYaw => chalet != null ? chalet.eulerAngles.y : 0f;
        /// <summary>True inside the chalet's walls.</summary>
        bool Indoors => InsideAt(pos);
        /// <summary>Is this point (world) inside the lodge (or the chalet)? (No weather in there: the people's clothes, ClothWind.)</summary>
        public bool InsideAt(Vector3 world) => chalet != null && (UseLodge ? LodgeInsideAt(chalet.InverseTransformPoint(world)) : InHouse(chalet.InverseTransformPoint(world)));
        static bool InHouse(Vector3 l) => Mathf.Abs(l.x) < ChaletHalfX - 0.2f && Mathf.Abs(l.z) < ChaletHalfZ - 0.2f;

        // ---------------------------------------------------------------- birches, lumpy snow, the trodden path

        /// <summary>Tall bare birches round the lodge: white trunks, a few dark forks of branches.</summary>
        void Birches()
        {
            var where = UseLodge ? LodgeBirches : new[] { new Vector2(-22.5f, -78f), new Vector2(-22f, -86f), new Vector2(3f, -94f), new Vector2(-17.5f, -63f), new Vector2(-25.5f, -70f), new Vector2(6f, -91f) };
            for (int i = 0; i < where.Length; i++)
            {
                var t = Frame(where[i].x, where[i].y, i * 53f);
                t.name = "Birch";
                float h = 9f + 4f * Slope.Hash01(i, 5, 31), lean = (Slope.Hash01(i, 6, 31) - 0.5f) * 6f;
                t.localRotation *= Quaternion.Euler(lean, 0f, lean * 0.5f);
                var trunk = Log(t, new Vector3(0f, -0.4f, 0f), new Vector3(0f, h, 0f), 0.26f, birch);
                // its shadow: a thicker post, shadow only (the real trunk and twigs are too thin for the phone's shadow map)
                var post = P(PrimitiveType.Cylinder, t, new Vector3(0f, h * 0.45f, 0f), new Vector3(0.42f, h * 0.45f, 0.42f), Vector3.zero, shadowMat).GetComponent<MeshRenderer>();
                post.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                trunk.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                for (int k = 0; k < 7; k++)
                {
                    float y = h * (0.45f + 0.08f * k), a = k * 137f * Mathf.Deg2Rad, len = (1.2f + 1.6f * Slope.Hash01(i, k, 32)) * (1f - k * 0.08f);
                    var from = new Vector3(0f, y, 0f);
                    var to = from + new Vector3(Mathf.Cos(a) * len, len * 1.1f, Mathf.Sin(a) * len);
                    Log(t, from, to, 0.06f, darkWood);
                    Log(t, to, to + new Vector3(Mathf.Cos(a + 0.6f) * len * 0.5f, len * 0.6f, Mathf.Sin(a + 0.6f) * len * 0.5f), 0.03f, darkWood);
                }
                Block(t, Vector3.zero, new Vector2(0.25f, 0.25f));
                SwayFromFoot(t, TreeWind.FlexBirch);   // in the wind with the firs round it (TreeWind), looser
            }
        }

        /// <summary>
        /// A tree built of parts (a birch: its trunk, branches and twigs, and its shadow post) made to move in the wind as
        /// one: the parts merged into one mesh for each material and way of casting shadows, standing on the tree's own
        /// foot, with swaying copies of their materials (TreeWind.hlsl bends a renderer by the height above its origin).
        /// </summary>
        void SwayFromFoot(Transform tree, float flex)
        {
            var groups = new Dictionary<(Material, UnityEngine.Rendering.ShadowCastingMode), List<CombineInstance>>();
            var toLocal = tree.worldToLocalMatrix;
            foreach (var mf in tree.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || mf.sharedMesh == null) continue;
                var key = (r.sharedMaterial, r.shadowCastingMode);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toLocal * mf.transform.localToWorldMatrix });
                DestroyImmediate(mf.gameObject);
            }
            foreach (var kv in groups)
            {
                var mesh = Made(new Mesh { name = tree.name + "Wood" });
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var g = new GameObject(tree.name + "Wood", typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(tree, false);
                g.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = g.GetComponent<MeshRenderer>();
                mr.sharedMaterial = SwayingCopy(kv.Key.Item1, flex);
                mr.shadowCastingMode = kv.Key.Item2;
                if (kv.Key.Item2 == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) mr.receiveShadows = false;
            }
        }

        /// <summary>The swaying copies of the camp's own materials for its trees (TreeWind.Swaying), by material and give: made once, kept.</summary>
        readonly Dictionary<(Material, float), Material> swayedBy = new Dictionary<(Material, float), Material>();
        Material SwayingCopy(Material m, float flex)
        {
            if (m == null) return null;
            if (!swayedBy.TryGetValue((m, flex), out var s) || s == null) swayedBy[(m, flex)] = s = TreeWind.Swaying(m, flex);
            return s;
        }

        /// <summary>Deep, lumpy snow round the edges of camp.</summary>
        void Mounds()
        {
            var t = new GameObject("Mounds").transform; t.SetParent(root, false);
            for (int i = 0; i < 26; i++)
            {
                // along the left edge (the right is the cliff), and a few across the back
                float dx = -(22f + 6f * Slope.Hash01(i, 1, 41)), z = Mathf.Lerp(-94f, -50f, Slope.Hash01(i, 2, 41));
                if (i >= 20) { dx = Mathf.Lerp(-14f, 14f, Slope.Hash01(i, 3, 41)); z = -97f - 3f * Slope.Hash01(i, 4, 41); }
                var p = Ground(dx, z);
                float s = 1.4f + 2.2f * Slope.Hash01(i, 5, 41);
                if (UseLodge && !LodgeClear(p, s * 0.7f)) continue;   // (not into the lodge or onto its terrace)
                P(PrimitiveType.Sphere, t, p + Vector3.down * s * 0.22f, new Vector3(s * 1.4f, s * 0.6f, s), new Vector3(0f, i * 41f, 0f), snowCap);
            }
        }

        /// <summary>A path trodden through the snow from the fire to the lodge's stairs, boot prints and all.</summary>
        void Footpath()
        {
            if (UseLodge) { LodgePath(); return; }   // (a paved path with lamp posts: CampLodge.cs)
            var pts = new List<Vector3> { Ground(-1f, -67.5f), Ground(-4.5f, -70.5f), Ground(-7.5f, -74.5f), stairsFoot };
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
            var mesh = Made(new Mesh { name = "Footpath" });
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

        // ---------------------------------------------------------------- day and night in camp

        // The camp doesn't choose its own time of day: the game's day/night cycle calls SetNight (0 day,
        // 1 night) and this follows it: a cold blue sky and snow, and warm light from the chalet, its porch and
        // the fire. The chalet's indoor lamps stay on (dimmer) by day; the outdoor ones only light at dusk.
        readonly List<Light> lamps = new List<Light>();
        readonly List<float> lampPower = new List<float>();
        public float Night { get; private set; }
        Light sun; Color sunDay, ambSky, ambEq, ambGround, fogDay, skyTintDay; float sunDayI, skyExpDay;
        Material sky;
        bool dayKnown;

        readonly List<float> lampDay = new List<float>();

        /// <summary>A warm point light; dayShare: how much of it stays on in daylight (indoor lamps do).</summary>
        Light Lamp(Transform t, Vector3 at, float range, float power, float dayShare = 0f)
        {
            var l = new GameObject("Lamp").AddComponent<Light>();
            l.transform.SetParent(t, false); l.transform.localPosition = at;
            // light colours are linearised per channel too: a pale warm white comes out amber, a saturated one red
            l.type = LightType.Point; l.color = new Color(1f, 0.8f, 0.6f); l.range = range; l.intensity = power; l.shadows = LightShadows.None;
            lamps.Add(l); lampPower.Add(power); lampDay.Add(dayShare);
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
            LodgeNight(k);
            if (mistMat != null) mistMat.SetColor("_BaseColor", Color.Lerp(new Color(1f, 1f, 1f, 0.75f), new Color(0.26f, 0.31f, 0.44f, 0.6f), k));
            for (int i = 0; i < lamps.Count; i++)
                if (lamps[i] != null) { float w = Mathf.Lerp(lampDay[i], 1f, k); lamps[i].intensity = lampPower[i] * w; lamps[i].enabled = w > 0.01f; }
        }

        // (no fade of its own any more: DayNight calls SetNight every frame with the time of day's darkness)

        // never leave the project's sky or grade in the evening (in the editor they are assets)
        void OnDisable() { if (dayKnown) SetNight(0f); CampShadows(false); }

        /// <summary>
        /// DROP IN is at the run's own start gate (StartGate.cs, 36 m above the lip; a run starts just behind it): walk
        /// down to it from the camp and the button comes up. Its posts and the warning signs below it are solid.
        /// </summary>
        void StartBanner()
        {
            float gz = Slope.GateZ;
            var t = Frame(0f, gz, 0f);
            t.name = "DropInBanner";
            var tap = new GameObject("TAP_DropIn").transform;
            tap.SetParent(t, false); tap.localPosition = new Vector3(0f, 3.2f, 0f);
            // a trigger: the tap ray sees triggers, and the run goes straight through under the banner (a solid box
            // here stops the rider dead at the start)
            var tapBox = tap.gameObject.AddComponent<BoxCollider>();
            tapBox.size = new Vector3(2f * StartGate.PostX + 0.6f, 6.6f, 1f); tapBox.isTrigger = true;   // posts, board and beam
            spots.Add(new Spot { st = Station.DropIn, at = Ground(0f, gz - 2.6f), radius = 4.2f, root = tap });
            for (int s = -1; s <= 1; s += 2)
            {
                Block(t, new Vector3(s * StartGate.PostX, 0f, 0f), new Vector2(StartGate.PostHalf, StartGate.PostHalf));
                // the safety signs on their posts either side of the in-run
                foreach (var z in new[] { Slope.PadLip - 22f, Slope.PadLip - 11f }) Block(Frame(s * 6.5f, z, 0f), Vector3.zero, new Vector2(1.6f, 0.3f));
            }
        }


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
            var t = Frame(0f, -71f, 0f);
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
            var m = Made(new Material(soft));
            m.SetColor("_BaseColor", Color.white);
            r.sharedMaterial = m;
        }

        void Strings()
        {
            Vector3 pa = UseLodge ? Ground(10.8f, -75f) : Ground(5f, -82f), pb = Ground(6f, -60f);
            // two tall poles for the flags and lights to hang from
            var poles = new GameObject("Poles").transform; poles.SetParent(root, false);
            Log(poles, pa + Vector3.down * 0.3f, pa + Vector3.up * 6.6f, 0.16f, logs);
            Log(poles, pb + Vector3.down * 0.3f, pb + Vector3.up * 6.2f, 0.16f, logs);
            Block(poles, poles.InverseTransformPoint(pa), new Vector2(0.2f, 0.2f));
            Block(poles, poles.InverseTransformPoint(pb), new Vector2(0.2f, 0.2f));
            // high over the camp, clear of the camera
            Flags(pa + Vector3.up * 6.5f, pb + Vector3.up * 6.1f);
            Vector3 porchR = deckR, porchL = deckL;
            Lights(porchR, pa + Vector3.up * 5.2f);
            Lights(pa + Vector3.up * 5.2f, pb + Vector3.up * 5.0f);
            Lights(porchL, porchR, 0.012f);   // along the balcony rail, pulled tight
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
            bool real = Forest.Realistic && RealFir.Load();   // the forest's own look
            var kinds = real ? new GameObject[0] : Resources.LoadAll<GameObject>("Trees");
            if (!real && kinds.Length == 0) return;
            var where = UseLodge ? LodgeTrees : new[] { new Vector2(-27f, -96f), new Vector2(-19f, -104f), new Vector2(-6f, -107f), new Vector2(7f, -105f), new Vector2(19f, -101f),
                                new Vector2(21f, -104f), new Vector2(-30f, -82f), new Vector2(-29f, -67f), new Vector2(-26.5f, -92f), new Vector2(-27f, -74f),   // (none on the cliff side)
                                new Vector2(-24f, -108f), new Vector2(13f, -110f), new Vector2(-31f, -54f),
                                new Vector2(-20.5f, -81f), new Vector2(2.6f, -84.5f) };   // the last two: spruces beside the chalet
            for (int i = 0; i < where.Length; i++)
            {
                if (real)
                {
                    // grown and buried in the snow like the forest's trees up here, and casting shadows in the camp
                    var fir = Forest.PlantLike(root, Ground(where[i].x, where[i].y) + Vector3.down * 0.05f, Slope.Hash01(i, 3, 77), i * 67f, "CampFir", out _, out _);
                    if (fir != null) FirShadow(fir);
                    continue;
                }
                var g = Instantiate(kinds[i % kinds.Length], root, false);
                TreeWind.SwayAll(g, swayed);   // moves in the wind like the forest's (one swaying copy per material, kept)
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

        // ---------------------------------------------------------------- the cliff edge

        // The camp's right-hand side is the top of a sheer cliff (Slope.Summit): a cable railing along the edge, as
        // at a summit lookout (square grey posts every 2.5 m on a concrete kerb, six taut steel cables), and three
        // tall flagpoles inside it with red-and-white flags streaming in the wind off the drop. The railing is one
        // mesh per material; the flags are small cloth grids moved on the CPU each frame (a few hundred vertices).
        const float EdgeX = 24.1f, EdgeZ0 = -108f, EdgeZ1 = -41f;
        Material railMetal, kerbMat, flagMat, mistMat;
        readonly List<Mesh> flagMeshes = new List<Mesh>();
        readonly List<Vector3> flagRoots = new List<Vector3>();
        readonly List<Renderer> flagRends = new List<Renderer>();
        Vector3[] flagWork;
        float flagClock;
        const int FlagNx = 14, FlagNy = 7;
        const float FlagL = 2.6f, FlagH = 1.3f;
        static readonly Vector3 Wind = new Vector3(0.15f, 0f, 1f).normalized;   // off the mountain, down past the camp

        void CliffEdge()
        {
            if (railMetal == null)
            {
                railMetal = M(new Color(0.52f, 0.54f, 0.56f), 0.55f); railMetal.SetFloat("_Metallic", 0.7f);
                kerbMat = M(new Color(0.66f, 0.65f, 0.62f), 0.15f);
                flagMat = M(Color.white, 0.1f, Resources.Load<Texture2D>("Camp/Flag")); flagMat.SetFloat("_Cull", 0f);
            }
            var t = new GameObject("CliffRail").transform; t.SetParent(root, false);
            int n = Mathf.RoundToInt((EdgeZ1 - EdgeZ0) / 2.5f);
            var posts = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) posts[i] = Ground(EdgeX, Mathf.Lerp(EdgeZ0, EdgeZ1, i / (float)n));
            for (int i = 0; i <= n; i++)
            {
                P(PrimitiveType.Cube, t, posts[i] + Vector3.up * 0.6f, new Vector3(0.08f, 1.22f, 0.08f), Vector3.zero, railMetal);
                P(PrimitiveType.Cube, t, posts[i] + Vector3.up * 1.22f, new Vector3(0.11f, 0.03f, 0.11f), Vector3.zero, railMetal);   // the cap
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 a = posts[i], b = posts[i + 1];
                // the kerb, and six cables pulled tight between the posts
                var k = Log(t, a + Vector3.up * 0.1f, b + Vector3.up * 0.1f, 0.3f, kerbMat);
                k.localScale = new Vector3(0.36f, k.localScale.y, 0.24f);
                for (int c = 0; c < 6; c++)
                {
                    float h = 0.24f + c * 0.18f;
                    Log(t, a + Vector3.up * h, b + Vector3.up * h, 0.012f, railMetal);
                }
            }
            Combine(t);
            // you walk up to it, not through it (the camp's edge stops you first anyway)
            Block(Frame(EdgeX + 0.4f, (EdgeZ0 + EdgeZ1) * 0.5f, 0f), Vector3.zero, new Vector2(0.45f, (EdgeZ1 - EdgeZ0) * 0.5f + 1f));

            // three flagpoles just inside the rail
            flagMeshes.Clear(); flagRoots.Clear(); flagRends.Clear();
            var poleM = M(new Color(0.88f, 0.89f, 0.9f), 0.6f); poleM.SetFloat("_Metallic", 0.5f);
            var gold = M(new Color(0.9f, 0.72f, 0.3f), 0.8f); gold.SetFloat("_Metallic", 0.9f);
            foreach (var z in new[] { -74f, -67.5f, -61f })
            {
                var g = Ground(EdgeX - 1.6f, z);
                Log(t.parent, g + Vector3.down * 0.3f, g + Vector3.up * 6f, 0.11f, poleM);
                P(PrimitiveType.Sphere, t.parent, g + Vector3.up * 6.08f, Vector3.one * 0.16f, Vector3.zero, gold);
                P(PrimitiveType.Cylinder, t.parent, g + Vector3.up * 0.15f, new Vector3(0.5f, 0.15f, 0.5f), Vector3.zero, kerbMat);
                Block(Frame(EdgeX - 1.6f, z, 0f), Vector3.zero, new Vector2(0.25f, 0.25f));
                var top = g + Vector3.up * 5.85f;
                flagRoots.Add(top);
                var fo = new GameObject("Flag", typeof(MeshFilter), typeof(MeshRenderer));
                fo.transform.SetParent(root, false);
                var mesh = FlagMesh();
                fo.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = fo.GetComponent<MeshRenderer>(); mr.sharedMaterial = flagMat;
                flagMeshes.Add(mesh); flagRends.Add(mr);
                // the middle one has a lamp at its foot, lighting all three from below at night
                if (z > -70f && z < -65f) Lamp(t.parent, g + Vector3.up * 1.2f + Vector3.left * 0.8f, 11f, 1.8f);
            }
            WaveFlags(0f);
        }

        /// <summary>A flag's cloth: a grid, both sides (each with its own normals), u along the fly, v up.</summary>
        Mesh FlagMesh()
        {
            int nv = (FlagNx + 1) * (FlagNy + 1);
            var uv = new Vector2[nv * 2];
            var tri = new List<int>();
            for (int j = 0; j <= FlagNy; j++)
                for (int i = 0; i <= FlagNx; i++)
                {
                    var c = new Vector2(i / (float)FlagNx, j / (float)FlagNy);
                    uv[j * (FlagNx + 1) + i] = c; uv[nv + j * (FlagNx + 1) + i] = c;
                }
            for (int j = 0; j < FlagNy; j++)
                for (int i = 0; i < FlagNx; i++)
                {
                    int a = j * (FlagNx + 1) + i, b = a + 1, c = a + FlagNx + 1, d = c + 1;
                    tri.AddRange(new[] { a, c, b, b, c, d });
                    tri.AddRange(new[] { nv + a, nv + b, nv + c, nv + b, nv + d, nv + c });   // the other side
                }
            var m = Made(new Mesh { name = "Flag" });
            m.MarkDynamic();
            m.vertices = new Vector3[nv * 2]; m.uv = uv; m.SetTriangles(tri, 0);
            return m;
        }

        /// <summary>
        /// The flags stream out hard downwind: waves run from the pole to the fly end, growing as they go, with a
        /// quicker flutter on top, the cloth sagging a little and the fly end whipping.
        /// </summary>
        void WaveFlags(float dt)
        {
            if (flagMeshes.Count == 0) return;
            bool seen = false;
            foreach (var r in flagRends) if (r != null && (r.isVisible || dt <= 0f || ChaseCamera.TestDt > 0f)) seen = true;
            if (!seen) return;
            flagClock += dt;
            int nv = (FlagNx + 1) * (FlagNy + 1);
            if (flagWork == null || flagWork.Length != nv * 2) flagWork = new Vector3[nv * 2];
            var side = Vector3.Cross(Vector3.up, Wind);
            for (int f = 0; f < flagMeshes.Count; f++)
            {
                float ph = f * 1.7f, t = flagClock;
                for (int j = 0; j <= FlagNy; j++)
                    for (int i = 0; i <= FlagNx; i++)
                    {
                        float u = i / (float)FlagNx, v = j / (float)FlagNy;
                        float amp = 0.22f * Mathf.Pow(u, 1.1f);
                        float wave = Mathf.Sin(u * 9.5f - t * 11f + ph + v * 0.9f) + 0.35f * Mathf.Sin(u * 23f - t * 27f + ph * 2f + v * 2.1f);
                        float along = u * FlagL * (1f - 0.04f * Mathf.Abs(wave) * u);
                        var p = Wind * along + Vector3.up * (v * FlagH - FlagH - 0.18f * u * u + 0.05f * u * Mathf.Sin(t * 7f + u * 6f + ph))
                              + side * (amp * wave);
                        flagWork[j * (FlagNx + 1) + i] = flagRoots[f] + p;
                        flagWork[nv + j * (FlagNx + 1) + i] = flagRoots[f] + p;
                    }
                var m = flagMeshes[f];
                m.vertices = flagWork;
                m.RecalculateNormals();
                m.RecalculateBounds();
            }
        }

        /// <summary>Merge everything under t into one mesh per material (the railing's two hundred pieces: two draws).</summary>
        void Combine(Transform t)
        {
            var byMat = new Dictionary<Material, List<CombineInstance>>();
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                var mat = mf.GetComponent<MeshRenderer>().sharedMaterial;
                if (!byMat.TryGetValue(mat, out var list)) byMat[mat] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = t.worldToLocalMatrix * mf.transform.localToWorldMatrix });
            }
            for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i).gameObject; if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            foreach (var kv in byMat)
            {
                var mesh = Made(new Mesh { name = "Combined", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var g = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(t, false);
                g.GetComponent<MeshFilter>().sharedMesh = mesh;
                g.GetComponent<MeshRenderer>().sharedMaterial = kv.Key;
            }
        }

        /// <summary>Cloud drifting below the summit, over the drop on the right and down the slopes on the left.</summary>
        void Mist()
        {
            if (mistMat == null && soft != null)
            {
                mistMat = new Material(soft);
                mistMat.SetTexture("_BaseMap", Resources.Load<Texture2D>("Camp/Cloud"));
                mistMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.75f));
            }
            if (mistMat == null) return;
            float top = Ground(0f, -70f).y;
            MistBank(new Vector3(Cx + 200f, top - 150f, -120f), new Vector3(300f, 220f, 420f), 26, 70f, 160f);   // over the drop
            MistBank(new Vector3(Cx - 190f, top - 150f, -90f), new Vector3(200f, 120f, 320f), 12, 50f, 110f);   // below the left slopes
        }

        void MistBank(Vector3 centre, Vector3 size, int count, float small, float big)
        {
            var go = new GameObject("Mist");
            go.transform.SetParent(root, false);
            go.transform.position = centre;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 240f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(small, big);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
            var em = ps.emission; em.rateOverTime = count / 240f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 0.4f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(1.5f, 1.5f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mistMat;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.maxParticleSize = 3f;
            ps.Play();
        }

        // ---------------------------------------------------------------- the edges give way

        // There is no fence round the camp. Walk too near any real drop (the cliff past the ends of its railing, the
        // start pad's lip, the summit's slopes where they steepen into rock) and the snow's edge breaks off under you:
        // a cornice splits away with a crack and a burst of powder, and you go down with it, thrown as a crash
        // throws you on the run (RiderBody's ragdoll), tumbling all the way. The camera watches from the top, then
        // follows; when the body has come to rest, or after a few seconds, the screen fades and you're back at the
        // chalet. The game adds the screams, the "nooo" and the thump (Collapsed, Rescued, RiderBody.Killed).
        /// <summary>The edge broke under the walker: where, and which way it fell.</summary>
        public System.Action<Vector3, Vector3> Collapsed;
        /// <summary>Back at the chalet after a fall.</summary>
        public System.Action Rescued;
        public bool Falling => falling;
        const float EdgeDrop = 1.3f;   // more than this lower within a stride of you is an edge (about 55 degrees)
        bool falling;
        float fallT, restFor, fade;
        Vector3 fallDir, fallCamFrom, fallLook;
        GameObject cornice;
        Material fracture;
        Vector3 fallEdge;
        MetaUi fadeUi;
        readonly Collider[] near = new Collider[12];

        /// <summary>Stand clear of the trees and rocks round you (the forest's own colliders): real things block you.</summary>
        Vector3 OffTrees(Vector3 p)
        {
            int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * 0.9f, 0.45f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = near[i];
                if (c == null || c.GetComponentInParent<Obstacle>() == null) continue;
                Vector3 at = p + Vector3.up * 0.9f, cp = c.ClosestPoint(at);
                var d = at - cp; d.y = 0f;
                float m = d.magnitude;
                if (m < 1e-4f) { d = p - c.bounds.center; d.y = 0f; m = d.magnitude; if (m < 1e-4f) continue; }
                if (m < 0.45f) p += d / m * (0.45f - m);
            }
            return p;
        }

        /// <summary>Is there a drop beside you, in the way you're walking or right round your feet? Then it goes.</summary>
        void EdgeCheck(Vector3 f)
        {
            if (falling) return;
            float here = Slope.DrawnHeight(pos.x, pos.z), worst = 0f;
            Vector3 dir = f;
            if (speed > 0.2f)
            {
                float ahead = here - Slope.DrawnHeight(pos.x + f.x * 1.3f, pos.z + f.z * 1.3f);
                if (ahead > worst) { worst = ahead; dir = f; }
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float drop = here - Slope.DrawnHeight(pos.x + d.x * 0.85f, pos.z + d.z * 0.85f);
                if (drop > worst) { worst = drop; dir = d; }
            }
            if (worst > EdgeDrop) Collapse(dir);
        }

        /// <summary>The cornice breaks: a chunk of the lip falls away, and you with it.</summary>
        public void Collapse(Vector3 dir)
        {
            if (falling || rider == null) return;
            falling = true; fallT = 0f; restFor = 0f;
            dir.y = 0f; fallDir = dir.normalized;
            speed = 0f;
            fallCamFrom = cam != null ? cam.transform.position : pos + Vector3.up * 3f;
            fallLook = pos + Vector3.up;
            fallEdge = pos;
            // the chunk: lumpy snow, half a metre deep, falling out and away (it only has to look right as it goes)
            if (cornice != null) { if (Application.isPlaying) Destroy(cornice); else DestroyImmediate(cornice); }
            cornice = new GameObject("Cornice");
            cornice.transform.SetParent(root, false);
            cornice.transform.SetPositionAndRotation(pos + fallDir * 0.7f + Vector3.down * 0.35f, Quaternion.LookRotation(fallDir));
            P(PrimitiveType.Sphere, cornice.transform, Vector3.zero, new Vector3(2.4f, 0.8f, 1.8f), Vector3.zero, snowCap);
            P(PrimitiveType.Sphere, cornice.transform, new Vector3(0.7f, -0.2f, 0.3f), new Vector3(1.3f, 0.7f, 1.2f), new Vector3(0f, 30f, 0f), snowCap);
            P(PrimitiveType.Sphere, cornice.transform, new Vector3(-0.8f, 0.05f, -0.1f), new Vector3(1.1f, 0.6f, 1.1f), new Vector3(0f, 70f, 0f), snowCap);
            // the fresh break on its back face: packed, icy, blue-grey, so it reads against the snow it came from
            if (fracture == null) fracture = M(new Color(0.55f, 0.66f, 0.82f), 0.55f);
            P(PrimitiveType.Cube, cornice.transform, new Vector3(0f, -0.05f, -0.55f), new Vector3(2.2f, 0.7f, 0.5f), new Vector3(-12f, 0f, 0f), fracture);
            P(PrimitiveType.Cube, cornice.transform, new Vector3(0.6f, -0.1f, -0.45f), new Vector3(0.9f, 0.55f, 0.4f), new Vector3(-8f, 25f, 4f), fracture);
            var rb = cornice.AddComponent<Rigidbody>();
            rb.mass = 300f; rb.linearDamping = 0.1f;
            rb.linearVelocity = fallDir * 1.4f + Vector3.down * 0.6f;
            rb.angularVelocity = Vector3.Cross(Vector3.up, fallDir) * 1.6f;
            // and you: thrown out after it, as a crash throws you on the run
            body.Tumble(fallDir * 2.2f + Vector3.up * 0.8f);
            rider.DoCrash("Cornice");
            Collapsed?.Invoke(pos, fallDir);
        }

        void FallStep(float dt)
        {
            fallT += dt;
            Footsteps(dt);   // (fades the steps out)
            // lying still a while (or long enough falling): fade, and back to the chalet
            restFor = body.RagdollMoving || fallT < 2.5f ? 0f : restFor + dt;
            bool done = restFor > 1.4f || fallT > 10f;
            fade = Mathf.MoveTowards(fade, done ? 1f : 0f, dt / 0.6f);
            if (fadeUi != null) fadeUi.SetFade(fade);
            if (cornice != null && fallT > 9f) { if (Application.isPlaying) Destroy(cornice); else DestroyImmediate(cornice); }
            if (done && fade >= 1f) Rescue();
        }

        /// <summary>Back on your feet at the chalet, patched up (the fade lifts as the camp comes back).</summary>
        void Rescue()
        {
            falling = false;
            if (cornice != null) { if (Application.isPlaying) Destroy(cornice); else DestroyImmediate(cornice); }
            var at = stairsFoot + Quaternion.Euler(0f, ChaletYaw, 0f) * Vector3.forward * 2f;
            rider.ResetAt(at.x, at.z);
            body.ResetVisual();
            body.SetWalking(true);
            pos = Ground(at.x - Cx, at.z);
            yaw = ChaletYaw * Mathf.Deg2Rad; camYaw = yaw; speed = 0f;   // looking out from the chalet
            snap = true;
            Place();
            lastStep = body.WalkPhase;
            fadeIn = true;
            Rescued?.Invoke();
        }
        bool fadeIn;

        /// <summary>The camera during a fall: holding at the top a moment, looking over after the body, then going down with it.</summary>
        void FallCamera(float dt)
        {
            Vector3 c = body.RagdollCentre;
            // a moment out over the edge, looking down after it (any longer and the cliff's mist fills the view); then
            // down with it, out in the open air off the face and a little above, looking back at it against the rock
            // (from behind, the lip it went over hides it)
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1.9f, fallT));
            Vector3 side = Vector3.Cross(Vector3.up, fallDir);
            Vector3 over = fallEdge - fallDir * 1.2f + Vector3.up * 3.2f + side * 1.5f;
            Vector3 follow = c + fallDir * 6f + Vector3.up * 3.5f + side * 4f;
            Vector3 want = Vector3.Lerp(over, follow, s);
            want.y = Mathf.Max(want.y, Slope.DrawnHeight(want.x, want.z) + 1.5f);
            // keep the body in sight: where the slope between it and the camera would hide it (a lip it went over, a
            // buttress), lift the camera until the line clears (seven height samples, only while falling)
            Vector3 from = c + Vector3.up * 0.4f;
            float lift = 0f;
            for (int i = 1; i <= 7; i++)
            {
                float t = i / 8f;
                var q = Vector3.Lerp(from, want, t);
                float under = Slope.DrawnHeight(q.x, q.z) + 0.5f - q.y;
                if (under > 0f) lift = Mathf.Max(lift, under / t);
            }
            want.y += Mathf.Min(lift, 25f);
            float k = 1f - Mathf.Exp(-dt * 5f);
            var cp = Vector3.Lerp(cam.transform.position, want, k);
            fallLook = Vector3.Lerp(fallLook, c, 1f - Mathf.Exp(-dt * 8f));
            cam.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(fallLook - cp, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 60f, k);
        }

        // ---------------------------------------------------------------- tree shadows

        // The camp's firs are branch cards, alpha-tested: in the phone's shadow map (two cascades out to 140 m, a
        // quarter-metre a texel by the camp's edge) the needles cover too little of each texel and the trees cast
        // nothing. Each gets a solid stand-in instead, drawn only into the shadow map: three cone tiers and a trunk,
        // the fir's own size; the cards themselves stop casting (cheaper, too). Only these camp trees have them.
        Material shadowMat;
        static Mesh firShadowMesh;

        void FirShadow(GameObject fir)
        {
            var r = fir.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var b = r.bounds;
            float crown = 0.82f * (b.extents.x + b.extents.z) * 0.5f, height = b.size.y;
            var g = new GameObject("FirShadow", typeof(MeshFilter), typeof(MeshRenderer));
            g.transform.SetParent(fir.transform.parent, false);
            g.transform.SetPositionAndRotation(new Vector3(b.center.x, b.min.y, b.center.z), fir.transform.rotation);
            g.transform.localScale = new Vector3(crown, height, crown);
            g.GetComponent<MeshFilter>().sharedMesh = FirShadowMesh();
            var mr = g.GetComponent<MeshRenderer>();
            // (its foot where the fir's is, so its shadow sways with the fir: TreeWind)
            var fm = r.sharedMaterial;
            mr.sharedMaterial = SwayingCopy(shadowMat, fm != null && fm.HasProperty("_WindFlex") ? fm.GetFloat("_WindFlex") : TreeWind.FlexFir);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            mr.receiveShadows = false;
        }

        /// <summary>A fir's silhouette, 1 high and 1 wide at the bottom: three stacked cones and a trunk.</summary>
        static Mesh FirShadowMesh()
        {
            if (firShadowMesh != null) return firShadowMesh;
            var v = new List<Vector3>(); var tri = new List<int>();
            void Cone(float y0, float r0, float y1, float r1)
            {
                const int n = 10;
                int b0 = v.Count;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    v.Add(new Vector3(Mathf.Cos(a) * r0, y0, Mathf.Sin(a) * r0));
                    v.Add(new Vector3(Mathf.Cos(a) * r1, y1, Mathf.Sin(a) * r1));
                }
                int c = v.Count; v.Add(new Vector3(0f, y0, 0f));
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    int lo0 = b0 + i * 2, hi0 = lo0 + 1, lo1 = b0 + j * 2, hi1 = lo1 + 1;
                    tri.AddRange(new[] { lo0, hi0, hi1, lo0, hi1, lo1, c, lo0, lo1 });   // side, and the underside
                }
            }
            Cone(0.14f, 1.0f, 0.55f, 0.28f);
            Cone(0.38f, 0.78f, 0.8f, 0.14f);
            Cone(0.62f, 0.52f, 1.0f, 0f);
            Cone(0f, 0.07f, 0.2f, 0.07f);   // the trunk
            firShadowMesh = new Mesh { name = "FirShadow" };
            firShadowMesh.SetVertices(v); firShadowMesh.SetTriangles(tri, 0);
            firShadowMesh.RecalculateNormals(); firShadowMesh.RecalculateBounds();
            return firShadowMesh;
        }

        // The phone draws shadows out to 140 m for the run's long views; the camp's are all close, so while you're in
        // it the same two cascades cover 80 m instead: the trees' and the chalet's shadows come out sharper. Put back
        // as you leave (and if the camp goes away), since the pipeline asset is shared.
        float runShadowDistance = -1f;

        void CampShadows(bool on)
        {
            var urp = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
            if (urp == null) return;
            if (on)
            {
                if (runShadowDistance < 0f) runShadowDistance = urp.shadowDistance;
                urp.shadowDistance = Mathf.Min(runShadowDistance, 80f);
            }
            else if (runShadowDistance >= 0f) { urp.shadowDistance = runShadowDistance; runShadowDistance = -1f; }
        }

        // ---------------------------------------------------------------- what you own shows up

        /// <summary>Show bought decorations, today's mission notes, and the board on the bench.</summary>
        public void Redress()
        {
            if (root == null) return;
            if (firePit != null) firePit.SetActive(Progress.Has(Progress.Up.FirePit));
            FireRedress();   // Alyssa and the stumps come with the fire (CampFire.cs)
            if (flags != null) flags.SetActive(Progress.Has(Progress.Up.Flags));
            if (lights != null) lights.SetActive(Progress.Has(Progress.Up.Lights));
            if (snowman != null) snowman.SetActive(Progress.Has(Progress.Up.Snowman));
            if (boardPaint != null) boardPaint.SetColor("_BaseColor", Progress.Boards[Progress.Board]);
            if (Active) Missions.Refresh();   // (not mid-run: a mission completing pays out, and Progress.Changed lands here)
            for (int i = 0; i < noteMats.Length && i < Missions.Today.Count; i++)
                noteMats[i].SetColor("_BaseColor", Missions.Today[i].done ? new Color(0.62f, 0.95f, 0.62f) : Color.white);
        }

        // ---------------------------------------------------------------- walking

        Rider rider; RiderBody body; Camera cam;
        Vector3 pos; float yaw, speed, camYaw, camPitch, lookBlend, showBlend, inBlend;
        // a jump or a roll under way: 0 none, 1 jump, 2 roll; how long it has been going; height off the ground
        int moveKind; float moveT, jumpY, jumpV, rollSpeed, nextFurrow; bool airborne;
        Vector3 rollDir;
        public const float JumpCrouch = 0.12f, JumpPush = 3.4f, JumpLength = 31f / 30f, RollLength = 0.8f;   // (the lengths of the hero's CampJump and CampRoll clips)
        /// <summary>The hero left the snow (a jump).</summary>
        public System.Action<Vector3> Jumped;
        /// <summary>Back down from a jump: where, going which way (m/s), how hard (m/s down).</summary>
        public System.Action<Vector3, Vector3, float> Landed;
        /// <summary>A roll started: where, which way.</summary>
        public System.Action<Vector3, Vector3> Rolled;
        /// <summary>The rolling body pressing into the snow: where, which way (m/s). A few times a roll.</summary>
        public System.Action<Vector3, Vector3> Furrowed;
        /// <summary>A jump or a roll under way (0 none, 1 jump, 2 roll), and how far into it (s).</summary>
        public int MoveKind => moveKind;
        public float MoveTime => moveT;
        bool snap;
        float lookRest = 99f;   // seconds since the camera was last turned by hand
        bool padMode;           // a controller's sticks are in use (two sticks: the camera stays where the right one leaves it)
        public Vector3 WalkerPosition => pos;
        /// <summary>The way the walker faces (degrees).</summary>
        public float WalkerYaw => yaw * Mathf.Rad2Deg;

        /// <summary>Put the rider in the camp on foot, by the lodge, and take over the camera.</summary>
        public void Enter(Rider r, RiderBody b, Camera c)
        {
            rider = r; body = b; cam = c;
            Active = true;
            // arrive on the path by the fire, looking up at the lodge
            pos = Ground(-1.5f, -64f);
            var toLodge = lodgeApex - pos;
            yaw = Mathf.Atan2(toLodge.x, toLodge.z); camYaw = yaw; speed = 0f;
            snap = true;
            Near = Station.None;
            body.SetWalking(true);
            ClearFootprints();   // a fresh visit: fresh snow
            falling = false; fadeIn = false; fade = 0f; fadeUi?.SetFade(0f);
            CampShadows(true);
            // a finger already down (the tap on TO CAMP or < CAMP that brought you here) is not a walk or a tap here:
            // ignore it until it lifts
            input.IgnoreCurrent(Touches());
            EdgeGestures(true);
            moveKind = 0; moveT = 0f; jumpY = 0f; camPitch = 0f; lookRest = 99f;
            body.CampPose = this;   // sitting at the fire, a mug in hand (CampFire.cs)
            if (alyssa != null) alyssa.enabled = true;   // (still while you're out on the mountain: nothing to pose)
            Place();
            lastStep = body.WalkPhase;
            Redress();
        }

        public void Leave()
        {
            if (!Active) return;
            Active = false;
            Showcase = false;
            EndActs(fadeUi);
            if (alyssa != null) alyssa.enabled = false;
            if (body != null) body.CampPose = null;
            CampShadows(false);
            if (stepPool != null) { foreach (var src in stepPool) src.Stop(); stepVol = 0f; }
            if (body != null) body.SetWalking(false);
            EdgeGestures(false);
        }

#if UNITY_IOS && !UNITY_EDITOR
        UnityEngine.iOS.SystemGestureDeferMode gesturesWere;
        bool gesturesHeld;
#endif
        /// <summary>
        /// In camp the walk stick floats wherever the thumb lands, often down by the bottom edge: there iOS holds the
        /// touch back for its own home swipe and may take it away mid-walk. So in camp the edges are the game's (a
        /// swipe in from an edge goes to the game; a second one still goes home), and as they were when you leave.
        /// </summary>
        void EdgeGestures(bool camp)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (camp == gesturesHeld) return;
            gesturesHeld = camp;
            if (camp) { gesturesWere = UnityEngine.iOS.Device.deferSystemGesturesMode; UnityEngine.iOS.Device.deferSystemGesturesMode = UnityEngine.iOS.SystemGestureDeferMode.All; }
            else UnityEngine.iOS.Device.deferSystemGesturesMode = gesturesWere;
#endif
        }

        /// <summary>A jump: a crouch, the push off, the air, and a landing that soaks it up. Not mid-jump or mid-roll.</summary>
        public void Jump()
        {
            if (!Active || moveKind != 0 || falling) return;
            moveKind = 1; moveT = 0f; jumpY = 0f; jumpV = 0f; airborne = false;
        }

        /// <summary>A forward roll the way of a swipe on the screen (up = away from the camera); you come up running.</summary>
        public void Roll(Vector2 screenDir)
        {
            if (!Active || moveKind != 0 || falling || screenDir.sqrMagnitude < 1e-4f) return;
            float head = camYaw + Mathf.Atan2(screenDir.x, screenDir.y);
            yaw = head;   // turn straight into it
            rollDir = new Vector3(Mathf.Sin(head), 0f, Mathf.Cos(head));
            rollSpeed = Mathf.Max(speed, 4.6f);
            moveKind = 2; moveT = 0f; nextFurrow = 0.14f;
            Rolled?.Invoke(pos, rollDir);
        }

        /// <summary>The jump or roll under way, each frame: true while it has the walker (no steering, no footsteps).</summary>
        bool MoveStep(float dt)
        {
            if (moveKind == 0) return false;
            moveT += dt;
            if (moveKind == 1)
            {
                // the crouch, then off the snow; in the air you carry on the way you were going
                if (!airborne && jumpY <= 0f && moveT >= JumpCrouch && moveT < JumpCrouch + 0.1f)
                { airborne = true; jumpV = JumpPush; Jumped?.Invoke(pos); }
                if (airborne)
                {
                    jumpV -= 9.81f * dt; jumpY += jumpV * dt;
                    if (jumpY <= 0f)
                    {
                        float hard = -jumpV;
                        jumpY = 0f; airborne = false;
                        var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                        Landed?.Invoke(pos, f * speed, hard);
                        if (onSnow)
                        {
                            // two boots pressed in deep, side by side
                            var side = new Vector3(-f.z, 0f, f.x);
                            AddPrint(pos + side * 0.12f + f * 0.04f, f, true, 1f);
                            AddPrint(pos - side * 0.12f - f * 0.03f, f, false, 1f);
                        }
                    }
                }
                if (moveT >= JumpLength && !airborne) { moveKind = 0; return false; }
                return airborne;   // on the ground (the crouch and the landing) you can still steer
            }
            // the roll: tucked and over, carried along, slowing a little; you come up running
            float k = moveT / RollLength;
            speed = Mathf.Lerp(rollSpeed, Mathf.Max(rollSpeed * 0.8f, MaxSpeed), k);
            if (onSnow && moveT >= nextFurrow && moveT < RollLength * 0.85f) { nextFurrow += 0.17f; Furrowed?.Invoke(pos, rollDir * speed); }
            if (moveT >= RollLength) { moveKind = 0; speed = Mathf.Max(speed, MaxSpeed * 1.05f); return false; }
            return true;
        }

        /// <summary>Walk with a stick (x right, y away from the camera; length 0..1). True once you've walked through the gate.</summary>
        public bool Walk(Vector2 stick, float dt)
        {
            if (!Active) return false;
            bool held = MoveStep(dt);
            float mag = held ? 0f : Mathf.Clamp01(stick.magnitude);
            if (mag > 0.08f)
            {
                // the stick is relative to the camera: up the screen is the way it looks
                float head = camYaw + Mathf.Atan2(stick.x, stick.y);
                yaw = Mathf.LerpAngle(yaw * Mathf.Rad2Deg, head * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 10f)) * Mathf.Deg2Rad;
            }
            // pushed right out, the walk breaks into a run (and eases back down)
            float want = mag > 0.92f ? RunSpeed : mag * MaxSpeed;
            if (!held) speed = Mathf.MoveTowards(speed, want, dt * (want > speed ? 9f : 14f));
            var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 next = pos + f * speed * dt;
            // (no edge to the camp: you can walk anywhere; only real things stop you, and the mountain's edges
            // give way under you, see Collapse)
            next = OffTrees(next);
            var p2 = new Vector2(next.x, next.z);
            float feet = pos.y;
            for (int it = 0; it < 2; it++)
                foreach (var b in boxes)
                {
                    if (b.floor || (b.owner != null && !b.owner.activeInHierarchy)) continue;
                    if (b.lo > feet + 1.7f || b.hi < feet + 0.1f) continue;   // above your head or under your feet
                    p2 = PushOut(b, p2, 0.38f);
                }
            if (Couple != null) p2 = Couple.PushOut(p2, 0.38f);   // (and round Val and Breanna, if they're about)
            if (PushWalker != null) p2 = PushWalker(p2);   // (the camp's people: you don't walk through them)
            pos = new Vector3(p2.x, feet, p2.y);
            Place();
            // the nearest station you're standing at
            Near = Station.None; OnLookout = false;
            float best = float.MaxValue;
            foreach (var s in spots)
            {
                if (s.root != null && !s.root.gameObject.activeInHierarchy) continue;   // (not built yet: the fire before it's bought)
                var d = new Vector2(s.at.x - pos.x, s.at.z - pos.z).magnitude;
                if (d < s.radius && d < best && Mathf.Abs(s.at.y - pos.y) < 1.5f) { best = d; Near = s.st; }   // on its floor
            }
            if (Near == Station.Lookout) { OnLookout = true; Near = Station.None; }
            if (Near == Station.None && UseLodge && LodgeLookoutAt(pos)) OnLookout = true;   // (the lodge's deck on the wing's roof: CampLodge)
            // feet on the snow: footsteps and prints (a jump or a roll makes its own marks)
            if (moveKind == 0) { Footsteps(dt); Footprints(dt); }
            else lastStep = body.WalkPhase;
            if (onSnow) EdgeCheck(f);
            body.CampMove = moveKind; body.CampMoveT = moveT;
            return false;
        }

        // ---- the camp's own people (CampCouple): where things are, and walking round what's in the way as the hero does

        /// <summary>Val and Breanna (made in Build).</summary>
        public CampCouple Couple { get; private set; }
        /// <summary>A point on the snow at (dx from the run's centre, z).</summary>
        public static Vector3 GroundAt(float dx, float z) => Ground(dx, z);
        /// <summary>
        /// A step from one place toward another for someone of radius r: round the trees and walls, onto the floors,
        /// decks and steps, as the walker goes (the walker's own state is left as it was).
        /// </summary>
        public Vector3 StepTo(Vector3 from, Vector3 to, float r)
        {
            bool keep = onSnow;
            var p2 = new Vector2(to.x, to.z);
            var t = OffTrees(new Vector3(to.x, from.y, to.z)); p2 = new Vector2(t.x, t.z);
            for (int it = 0; it < 2; it++)
                foreach (var b in boxes)
                {
                    if (b.floor || (b.owner != null && !b.owner.activeInHierarchy)) continue;
                    if (b.lo > from.y + 1.7f || b.hi < from.y + 0.1f) continue;
                    p2 = PushOut(b, p2, r);
                }
            var q = new Vector3(p2.x, from.y, p2.y);
            q.y = FloorAt(q, from.y);
            onSnow = keep;
            return q;
        }

        // ---------------------------------------------------------------- for the camp's people (CampLife.cs)

        /// <summary>The camp was built (or rebuilt for a new mountain): its root, which anything added to the camp goes under.</summary>
        public System.Action<Transform> Built;
        /// <summary>Moves the walker out of anyone standing in the way (set by CampLife).</summary>
        public System.Func<Vector2, Vector2> PushWalker;
        /// <summary>How fast the walker is going (m/s).</summary>
        public float WalkerSpeed => speed;

        /// <summary>A point pushed out of everything solid in the camp at a body's height, for a body of radius r.</summary>
        public Vector2 PushOutOfThings(Vector2 p, float r, float feet)
        {
            for (int it = 0; it < 2; it++)
                foreach (var b in boxes)
                {
                    if (b.floor || (b.owner != null && !b.owner.activeInHierarchy)) continue;
                    if (b.lo > feet + 1.7f || b.hi < feet + 0.1f) continue;
                    p = PushOut(b, p, r);
                }
            return p;
        }

        /// <summary>What someone stands on at p coming from height feet (the snow, a deck, a step).</summary>
        public float FloorHeight(Vector3 p, float feet)
        {
            bool was = onSnow;
            float y = FloorAt(p, feet);
            onSnow = was;
            return y;
        }

        /// <summary>Something solid added to the camp (a bench, a stand): a box on the snow, half sizes, turned by yaw.</summary>
        public void AddBlock(Vector3 at, Vector2 half, float yawDeg)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            boxes.Add(new Box { c = new Vector2(at.x, at.z), h = half, cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw), top = at.y, lo = at.y - 1000f, hi = at.y + 1000f });
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

        /// <summary>
        /// What you stand on at p, coming from height feet: the highest floor no more than a step above you
        /// (so the mezzanine overhead doesn't lift you up, but stairs and the terrace do, a step at a time).
        /// </summary>
        float FloorAt(Vector3 p, float feet)
        {
            float y = Slope.DrawnHeight(p.x, p.z);
            onSnow = true;
            foreach (var b in boxes)
            {
                if (!b.floor) continue;
                var d = new Vector2(p.x, p.z) - b.c;
                var q = new Vector2(d.x * b.cos - d.y * b.sin, d.x * b.sin + d.y * b.cos);
                if (Mathf.Abs(q.x) > b.h.x || Mathf.Abs(q.y) > b.h.y) continue;
                // a ramp (stairs) falls by drop from its -z end to its +z end
                float top = b.top - b.drop * (q.y + b.h.y) / (2f * b.h.y);
                if (top <= feet + 0.55f && top > y) { y = top; onSnow = false; }   // on a floor, a deck or steps
            }
            return y;
        }

        void Place()
        {
            pos.y = FloorAt(pos, pos.y);
            var p = pos + Vector3.up * (Rider.R + jumpY);
            rider.Body.position = p;
            rider.transform.position = p;
            // the model faces the board's toe edge (+X): turn it a quarter so it faces where it walks
            rider.Heading = yaw - Mathf.PI * 0.5f;
            body.WalkPace = Mathf.Clamp01(speed / MaxSpeed);
            body.Cadence = Cadence;
            body.RunBlend = Mathf.Clamp01((speed - MaxSpeed) / (RunSpeed - MaxSpeed));
        }

        /// <summary>The station under a tap on the screen, if any.</summary>
        public Station StationAt(Vector2 screen)
        {
            if (cam == null) return Station.None;
            var ray = cam.ScreenPointToRay(screen);
            int nh = Physics.RaycastNonAlloc(ray, rayHits, 80f, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < nh; i++)
                foreach (var s in spots)
                    if (rayHits[i].collider.transform == s.root) return s.st;
            return Station.None;
        }

        // ---------------------------------------------------------------- footsteps

        // One footstep each time a foot lands, on the beat of the feet at any pace. The player's own recording of
        // boots crunching through snow (Resources/Audio/user_snow_steps) is cut into single steps by Tools/CutSteps.py
        // (Resources/Audio/Steps: each clip's crunch starts 4 ms in). The walk cycle says when each heel strikes
        // (the left at pi/2, the right at 3pi/2, as the footprints and the hero's walk and run clips have it), so each
        // step is scheduled for the exact moment the cycle will reach the next strike, never a frame late. Running,
        // they come harder and a touch higher; indoors on the chalet's floor, softer and duller.
        AudioSource[] stepPool;
        AudioLowPassFilter[] stepTones;
        AudioClip[] stepClips;
        int stepNext, stepClipLast = -1, stepPlanned = int.MinValue;
        float stepIn, stepPhLast, stepRate, stepVol;
        /// <summary>Test tools: a step sound was planned: seconds from now until it sounds, which foot, which strike.</summary>
        public System.Action<float, bool, int> StepPlanned;

        void Footsteps(float dt)
        {
            if (stepPool == null)
            {
                stepClips = Resources.LoadAll<AudioClip>("Audio/Steps");
                System.Array.Sort(stepClips, (a, b) => string.CompareOrdinal(a.name, b.name));
                if (stepClips.Length == 0) return;
                foreach (var clip in stepClips) clip.LoadAudioData();   // loaded now, so no step is late waiting for it
                var g = new GameObject("Footsteps");
                g.transform.SetParent(transform, false);
                // a few voices, so a step's tail rings on under the next one
                stepPool = new AudioSource[4]; stepTones = new AudioLowPassFilter[4];
                for (int i = 0; i < stepPool.Length; i++)
                {
                    var v = new GameObject("Step" + i); v.transform.SetParent(g.transform, false);
                    var voice = v.AddComponent<AudioSource>();
                    voice.playOnAwake = false; voice.loop = false; voice.spatialBlend = 0f;   // your own feet: in both ears
                    stepPool[i] = voice;
                    stepTones[i] = v.AddComponent<AudioLowPassFilter>(); stepTones[i].cutoffFrequency = 22000f;
                }
            }
            float run = Mathf.Clamp01((speed - MaxSpeed) / (RunSpeed - MaxSpeed));
            stepIn = Mathf.MoveTowards(stepIn, Indoors ? 1f : 0f, dt * 4f);
            RiderBody.Shelter = stepIn;   // (and no wind in the clothes indoors)
            for (int i = 0; i < stepTones.Length; i++) stepTones[i].cutoffFrequency = Mathf.Lerp(22000f, 1400f, stepIn);
            // how fast the cycle turns (it moves on in the body's own frame update, after this one)
            float ph = body.WalkPhase;
            if (dt > 0f)
            {
                float r = (ph - stepPhLast) / dt;
                stepRate = r >= 0f && r < 40f ? Mathf.Lerp(stepRate, r, 0.5f) : 0f;   // a reset cycle jumps back
            }
            stepPhLast = ph;
            bool walking = speed > 0.3f && body.WalkPace > 0.12f && stepRate > 0.5f;
            stepVol = walking ? Mathf.Lerp(0.72f, 0.95f, run) * Mathf.Lerp(1f, 0.45f, stepIn) * Mathf.Lerp(0.7f, 1f, body.WalkPace) : 0f;
            if (!walking) { stepPlanned = int.MinValue; return; }
            // the next strike: the phase will be ph + rate * dt when the body shows this frame
            float shown = ph + stepRate * dt;
            int k = Mathf.CeilToInt((shown - Mathf.PI * 0.5f) / Mathf.PI);
            if (k <= stepPlanned) k = stepPlanned + 1;
            float strike = Mathf.PI * 0.5f + k * Mathf.PI;
            float wait = (strike - shown) / stepRate;   // seconds after this frame is shown
            if (wait > dt + 0.06f) return;             // not yet: it gets planned a frame or so ahead
            stepPlanned = k;
            bool left = (k % 2 + 2) % 2 == 0;
            // never the same crunch twice running
            int c = Random.Range(0, stepClips.Length);
            if (c == stepClipLast) c = (c + 1) % stepClips.Length;
            stepClipLast = c;
            var src = stepPool[stepNext]; stepNext = (stepNext + 1) % stepPool.Length;
            src.clip = stepClips[c];
            src.volume = stepVol * Random.Range(0.88f, 1f) * (left ? 1f : 0.94f);
            src.pitch = Mathf.Lerp(0.98f, 1.08f, run) * Random.Range(0.96f, 1.04f);
            src.PlayScheduled(AudioSettings.dspTime + Mathf.Max(0f, wait));
            StepPlanned?.Invoke(Mathf.Max(0f, wait), left, k);
        }

        /// <summary>Test tools: how the footsteps are playing right now.</summary>
        public string FootstepsState => stepPool == null ? "no clips" : $"clips={stepClips.Length} vol={stepVol:0.00} rate={stepRate / (2f * Mathf.PI):0.00} cycles/s planned={stepPlanned} cutoff={stepTones[0].cutoffFrequency:0}";

        // ---------------------------------------------------------------- footprints

        // A boot print for every step, left and right in turn as each heel strikes (RiderRig's walk cycle), along
        // the way you're going. Running, the strides are longer and the prints deeper (darker). None on floors,
        // decks or steps. Up to 200 at once in one mesh, the oldest fading after a minute and a half; a new visit
        // starts on clean snow. (Not pressed into SnowLayer: in the camp its touched cells draw as lighter, faceted
        // patches that don't match the camp's snow, so a print was a pale polygon round a dent.)
        const int MaxPrints = 200;
        const float PrintLife = 90f, PrintFade = 30f;
        Mesh printMesh;
        Vector3[] printV;
        Color[] printC;
        float[] printBorn, printAlpha;
        int printNext;
        float lastStep, fadeTick, printClock;
        bool onSnow = true;

        void Footprints(float dt)
        {
            printClock += dt;
            if (printMesh == null) BuildPrints();
            // a heel strikes each time the cycle passes pi/2 (left) or 3pi/2 (right)
            float ph = body.WalkPhase;
            if (ph < lastStep - 0.01f) lastStep = ph;   // the cycle was reset
            if (speed > 0.25f && onSnow)
            {
                float k0 = Mathf.Floor((lastStep - Mathf.PI * 0.5f) / Mathf.PI), k1 = Mathf.Floor((ph - Mathf.PI * 0.5f) / Mathf.PI);
                if (k1 > k0) Step(Mathf.RoundToInt(k1) % 2 == 0);
            }
            lastStep = ph;
            // fade the old ones, a few times a second
            fadeTick -= dt;
            if (fadeTick <= 0f)
            {
                fadeTick = 0.25f;
                bool changed = false;
                for (int i = 0; i < MaxPrints; i++)
                {
                    if (printAlpha[i] <= 0f) continue;
                    float age = printClock - printBorn[i];
                    float a = printAlpha[i] * Mathf.Clamp01(1f - (age - PrintLife) / PrintFade);
                    if (age > PrintLife + PrintFade) { printAlpha[i] = 0f; a = 0f; }
                    var c = new Color(1f, 1f, 1f, a);
                    if (printC[i * 4] != c) { printC[i * 4] = printC[i * 4 + 1] = printC[i * 4 + 2] = printC[i * 4 + 3] = c; changed = true; }
                }
                if (changed) printMesh.colors = printC;
            }
        }

        /// <summary>Put a foot down: a print where it lands, in front of you and to its side.</summary>
        void Step(bool left)
        {
            float run = body.RunAmount, amp = Mathf.Clamp01(speed / MaxSpeed);
            var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var side = new Vector3(-f.z, 0f, f.x);   // to your left
            float stride = Mathf.Lerp(0.36f, 0.62f, run) * amp;
            Vector3 at = pos + f * stride + side * (left ? 0.11f : -0.11f);
            AddPrint(at, f, left, Mathf.Lerp(0.7f, 0.95f, run));
        }

        /// <summary>
        /// Walk cycles a second (two steps each) for the speed you're going: about a metre a step walking, nearly two
        /// running, so the feet keep up with the ground and the prints fall a stride apart.
        /// </summary>
        float Cadence => speed / (2f * Mathf.Lerp(1.0f, 1.8f, Mathf.Clamp01((speed - MaxSpeed) / (RunSpeed - MaxSpeed))));

        void AddPrint(Vector3 at, Vector3 f, bool left, float alpha)
        {
            int i = printNext;
            printNext = (printNext + 1) % MaxPrints;
            var r = new Vector3(f.z, 0f, -f.x);   // to the right
            const float hw = 0.07f, hl = 0.15f;
            Vector3 P(float u, float v) { var p = at + r * u + f * v; p.y = Slope.DrawnHeight(p.x, p.z) + 0.012f; return p; }
            printV[i * 4] = P(-hw, -hl); printV[i * 4 + 1] = P(hw, -hl); printV[i * 4 + 2] = P(hw, hl); printV[i * 4 + 3] = P(-hw, hl);
            // the texture is a left boot: a right one is it mirrored
            float u0 = left ? 0f : 1f, u1 = left ? 1f : 0f;
            var uv = printMesh.uv;
            uv[i * 4] = new Vector2(u0, 0f); uv[i * 4 + 1] = new Vector2(u1, 0f); uv[i * 4 + 2] = new Vector2(u1, 1f); uv[i * 4 + 3] = new Vector2(u0, 1f);
            printBorn[i] = printClock; printAlpha[i] = alpha;
            var c = new Color(1f, 1f, 1f, alpha);
            printC[i * 4] = printC[i * 4 + 1] = printC[i * 4 + 2] = printC[i * 4 + 3] = c;
            printMesh.vertices = printV; printMesh.uv = uv; printMesh.colors = printC;
            printMesh.RecalculateBounds();
        }

        void BuildPrints()
        {
            printV = new Vector3[MaxPrints * 4];
            printC = new Color[MaxPrints * 4];
            printBorn = new float[MaxPrints]; printAlpha = new float[MaxPrints];
            var tri = new int[MaxPrints * 6];
            for (int i = 0; i < MaxPrints; i++)
            {
                int v = i * 4;
                tri[i * 6] = v; tri[i * 6 + 1] = v + 2; tri[i * 6 + 2] = v + 1;
                tri[i * 6 + 3] = v; tri[i * 6 + 4] = v + 3; tri[i * 6 + 5] = v + 2;
            }
            printMesh = new Mesh { name = "Footprints" };
            printMesh.MarkDynamic();
            printMesh.vertices = printV; printMesh.uv = new Vector2[MaxPrints * 4]; printMesh.colors = printC;
            printMesh.triangles = tri;
            var go = new GameObject("Footprints", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = printMesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Imprint(Resources.Load<Texture2D>("Camp/Boot"));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void ClearFootprints()
        {
            if (printMesh != null)
            {
                System.Array.Clear(printV, 0, printV.Length);
                System.Array.Clear(printC, 0, printC.Length);
                System.Array.Clear(printAlpha, 0, printAlpha.Length);
                printMesh.vertices = printV; printMesh.colors = printC;
                printMesh.RecalculateBounds();
            }
            printNext = 0;
        }

        /// <summary>Test tools: how many prints are on the snow.</summary>
        public int FootprintCount { get { int n = 0; if (printAlpha != null) foreach (var a in printAlpha) if (a > 0f) n++; return n; } }

        // ---------------------------------------------------------------- the controls

        readonly CampInput input = new CampInput();
        readonly List<CampInput.Touch> touchList = new List<CampInput.Touch>();
        /// <summary>Test tools: touches to use instead of the screen's (null: the real ones).</summary>
        public static List<CampInput.Touch> TestTouches;
        /// <summary>Test tools: the camp's touch controls.</summary>
        public CampInput Controls => input;

        /// <summary>
        /// One frame in camp. One thumb, anywhere, walks, and the camera follows round behind; a second finger held
        /// down turns the camera by hand; a quick tap jumps (or, standing on a station, opens it), a flick rolls; the
        /// menus take their own taps. A plugged-in controller has its two sticks: left walks, right turns the camera.
        /// True when it's time to drop in.
        /// </summary>
        public bool Step(float dt, MetaUi ui)
        {
            fadeUi = ui;
            if (LodgeSwitchStep(dt, ui)) return false;   // a change of the lodge's layout, behind a dip to black (CampLodge.cs)
            if (fadeIn) { fade = Mathf.MoveTowards(fade, 0f, dt / 0.6f); ui.SetFade(fade); fadeIn = fade > 0f; }
            if (falling) { FallStep(dt); ui.SetStick(false, Vector2.zero, Vector2.zero); ui.SetLookStick(false, Vector2.zero, Vector2.zero); ui.SetContext(Station.None); return false; }
            var touches = Touches();
            if (ui.ClosetOpen)
            {
                // the closet takes every finger (its list scrolls, its hero turns); nothing walks or looks meanwhile
                ui.ClosetStep(touches, dt);
                ui.SetStick(false, Vector2.zero, Vector2.zero); ui.SetLookStick(false, Vector2.zero, Vector2.zero);
                Showcase = false;
                return false;
            }
            input.Step(touches, dt, cam != null ? cam.pixelWidth : Screen.width, cam != null ? cam.pixelHeight : Screen.height, ui.Hit, ui.PanelOpen);
            if (input.Button != null) ui.Act(input.Button);
            if (ActStep(dt, ui, touches)) return false;   // talking, roasting, at the stove (CampFire.cs)
            if (input.ContextTapped && Open(Near, ui)) return true;
            if (input.Tapped && !ui.PanelOpen)
            {
                // standing still, a tap on a station opens it (the gate drops you in); otherwise it's a jump
                var st = speed < 0.5f ? StationAt(input.TapAt) : Station.None;
                if (st != Station.None) { if (Open(st, ui)) return true; }
                else if (!Sip()) Jump();   // (a mug in hand: a tap is a sip, CampCocoa)
            }
            if (input.Swiped && !ui.PanelOpen && !HoldingMug) Roll(input.SwipeDir);
            if (KeyJump() && !Sip()) Jump();
            if (KeyRoll() && !HoldingMug) Roll(Vector2.up);
            // a controller: its sticks take over until the screen is touched again
            bool padUsed = Pad(out Vector2 padMove, out Vector2 padLook, out bool padJump, out bool padRoll);
            if (padUsed) padMode = true; else if (touches.Count > 0) padMode = false;
            if (ui.PanelOpen) { padMove = padLook = Vector2.zero; }
            else
            {
                // the bottom button: standing at a station it opens it (the gate drops you in), otherwise a jump
                if (padJump) { if (speed < 0.5f && Near != Station.None) { if (Open(Near, ui)) return true; } else if (!Sip()) Jump(); }
                if (padRoll && !HoldingMug) Roll(Vector2.up);
            }
            Look(input.Look + padLook, dt);
            lookRest = input.Look.sqrMagnitude > 0.0025f ? 0f : lookRest + dt;
            ui.SetStick(input.MoveShown && !ui.PanelOpen, input.MoveOrigin, input.MovePos, input.Radius);
            ui.SetLookStick(input.LookShown && !ui.PanelOpen, input.LookOrigin, input.LookPos, input.Radius);
            var stick = ui.PanelOpen ? Vector2.zero : Vector2.ClampMagnitude(input.Move + padMove + Keys(), 1f);
            if (HoldingMug) stick = Vector2.ClampMagnitude(stick, 0.9f);   // a full mug: you walk, you don't run
            Showcase = ui.PanelOpen && ui.ShopTab == 1;
            if (Walk(stick, dt)) return true;
            ui.SetContext(Near);
            return false;
        }

        /// <summary>
        /// Turn the camera round the hero with the look stick: sideways swings it round, up and down tilts it a
        /// little, within limits.
        /// </summary>
        public void Look(Vector2 look, float dt)
        {
            if (look.sqrMagnitude < 0.0025f) return;
            camYaw += look.x * 2.6f * dt;
            camPitch = Mathf.Clamp(camPitch - look.y * 40f * dt, -12f, 22f);
        }

        /// <summary>Every finger on the screen (the mouse is one finger, for the editor).</summary>
        List<CampInput.Touch> Touches()
        {
            if (TestTouches != null) return TestTouches;
            touchList.Clear();
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                    if (t.press.isPressed) touchList.Add(new CampInput.Touch(t.touchId.ReadValue(), t.position.ReadValue()));
            var ms = Mouse.current;
            if (touchList.Count == 0 && ms != null && ms.leftButton.isPressed) touchList.Add(new CampInput.Touch(-1, ms.position.ReadValue()));
#else
            for (int i = 0; i < UnityEngine.Input.touchCount; i++) { var t = UnityEngine.Input.GetTouch(i); touchList.Add(new CampInput.Touch(t.fingerId, t.position)); }
            if (touchList.Count == 0 && UnityEngine.Input.GetMouseButton(0)) touchList.Add(new CampInput.Touch(-1, UnityEngine.Input.mousePosition));
#endif
            return touchList;
        }

        /// <summary>
        /// A plugged-in controller, twin sticks: the left one walks, the right one turns the camera, the bottom face
        /// button jumps and the right one rolls. True while any of it is in use.
        /// </summary>
        static bool Pad(out Vector2 move, out Vector2 look, out bool jump, out bool roll)
        {
            move = look = Vector2.zero; jump = roll = false;
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            if (g == null) return false;
            Vector2 Dead(Vector2 v) { float m = v.magnitude; return m < 0.15f ? Vector2.zero : v / m * Mathf.Clamp01((m - 0.15f) / 0.8f); }
            move = Dead(g.leftStick.ReadValue()); look = Dead(g.rightStick.ReadValue());
            jump = g.buttonSouth.wasPressedThisFrame; roll = g.buttonEast.wasPressedThisFrame;
            return move != Vector2.zero || look != Vector2.zero || jump || roll;
#else
            return false;
#endif
        }

        static bool KeyJump()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k.spaceKey.wasPressedThisFrame;
#else
            return UnityEngine.Input.GetKeyDown(KeyCode.Space);
#endif
        }

        static bool KeyRoll()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k.rKey.wasPressedThisFrame;
#else
            return UnityEngine.Input.GetKeyDown(KeyCode.R);
#endif
        }

        bool Open(Station st, MetaUi ui)
        {
            if (OpenFireStation(st, ui)) return false;
            if (st == Station.DropIn) LeaveMug();   // (it waits on the gate's post)
            if (st == Station.Missions) ui.ShowMissions();
            else if (st == Station.Upgrades) ui.ShowShop(0);
            else if (st == Station.Style) ui.ShowShop(1);
            return st == Station.DropIn;
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
            v = Vector2.ClampMagnitude(v, 1f) * (k.shiftKey.isPressed ? 1f : 0.85f);   // shift to run
#else
            v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
            return Vector2.ClampMagnitude(v, 1f);
        }

        // ---------------------------------------------------------------- camera

        void LateUpdate()
        {
            if (Active) CameraStep(Time.deltaTime);
            WaveFlags(ChaseCamera.TestDt > 0f ? ChaseCamera.TestDt : Time.deltaTime);
        }

        /// <summary>Move the camera (public for the test tools, which have no frame time).</summary>
        public void CameraStep(float dt)
        {
            if (cam == null) return;
            if (ChaseCamera.TestDt > 0f) dt = ChaseCamera.TestDt;
            if (falling) { FallCamera(dt); return; }
            if (ClosetStage.Showing != null) { ClosetStage.Showing.Frame(cam); snap = true; return; }   // the closet's studio has the camera
            float k(float rate) => snap ? 1f : 1f - Mathf.Exp(-dt * rate);
            // one thumb walks and turns: the camera swings round behind you as you go, as it first did. Turned by
            // hand (a second finger held), it stays where you left it for a moment, then eases back to following.
            // With a controller's two sticks it stays where the right stick leaves it
            float follow = padMode ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.2f, 2.6f, lookRest));
            if (speed > 0.4f && follow > 0f)
                camYaw = Mathf.LerpAngle(camYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, k(1.4f) * Mathf.Clamp01(speed / MaxSpeed) * follow) * Mathf.Deg2Rad;
            lookBlend = Mathf.Lerp(lookBlend, OnLookout && speed < 0.5f ? 1f : 0f, k(2f));
            showBlend = Mathf.Lerp(showBlend, Showcase ? 1f : 0f, k(4f));
            var f = new Vector3(Mathf.Sin(camYaw), 0f, Mathf.Cos(camYaw));
            // indoors the camera comes in close and low, under the ceilings
            inBlend = Mathf.Lerp(inBlend, Indoors ? 1f : 0f, k(3f));
            Vector3 target = pos + Vector3.up * (Mathf.Lerp(1.25f, 1.35f, inBlend) + jumpY * 0.6f);
            Vector3 want = target - f * Mathf.Lerp(6.4f, 4.6f, inBlend) + Vector3.up * Mathf.Lerp(2.7f, 1.3f, inBlend);
            // the look stick's tilt: higher and looking down, or lower and level, round the hero
            var tiltAxis = new Vector3(f.z, 0f, -f.x);
            want = target + Quaternion.AngleAxis(camPitch, tiltAxis) * (want - target);
            Vector3 look = target + f * Mathf.Lerp(4f, 3f, inBlend) + Vector3.up * Mathf.Lerp(0.15f, 0.1f, inBlend);
            // at the lookout, stand back and take in the whole mountain below
            Vector3 lookPos = pos + new Vector3(-0.8f, 4.2f, -5.6f), lookAt = pos + new Vector3(0f, -55f, 190f);
            want = Vector3.Lerp(want, lookPos, lookBlend); look = Vector3.Lerp(look, lookAt, lookBlend);
            // picking an outfit: face the rider, close, with room for the menu below
            var front = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 showPos = pos + front * Mathf.Lerp(6f, 5f, inBlend) + Vector3.up * 1.4f, showAt = pos - Vector3.up * Mathf.Lerp(0.1f, 0.3f, inBlend);
            want = Vector3.Lerp(want, showPos, showBlend); look = Vector3.Lerp(look, showAt, showBlend);
            // never through a wall, a floor or the roof: come in in front of it
            Vector3 dir = want - target;
            float reach = dir.magnitude;
            int nh = Physics.RaycastNonAlloc(target, dir / Mathf.Max(reach, 1e-4f), rayHits, reach, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < nh; i++)
                if (cameraWalls.Contains(rayHits[i].collider) && rayHits[i].distance - 0.3f < reach)
                    reach = Mathf.Max(0.6f, rayHits[i].distance - 0.3f);
            // pulled in close: rise a little and look down, so you don't fill the screen
            float pulled = Mathf.Clamp01((dir.magnitude - reach) / Mathf.Max(1f, dir.magnitude));
            want = target + dir.normalized * reach + Vector3.up * (0.9f * pulled * (1f - showBlend));
            if (!Indoors) want.y = Mathf.Max(want.y, Slope.DrawnHeight(want.x, want.z) + 0.8f);
            Vector3 cp = snap ? want : Vector3.Lerp(cam.transform.position, want, k(6f));
            cam.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(look - cp, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(62f, 64f, inBlend), 54f, lookBlend), Mathf.Lerp(42f, 50f, inBlend), showBlend);
            snap = false;
        }

        /// <summary>Test tools: stand here, facing this way.</summary>
        public void Teleport(float dx, float z, float yawDeg, bool snapCam = true) => TeleportTo(Ground(dx, z), yawDeg, snapCam);

        /// <summary>Test tools: what's pushing on the walker here (blocks within reach, and their heights).</summary>
        public string DebugHere()
        {
            var sb = new System.Text.StringBuilder();
            var p2 = new Vector2(pos.x, pos.z);
            foreach (var b in boxes)
            {
                if (b.floor) continue;
                if ((PushOut(b, p2, 0.4f) - p2).sqrMagnitude > 1e-6f) sb.Append($"[block c={b.c} h={b.h} y={b.lo:0.0}..{b.hi:0.0}] ");
            }
            return $"feet={pos.y:0.00} " + sb;
        }

        /// <summary>Test tools: stand at a world point (on whatever floor is there), facing this way.</summary>
        public void TeleportTo(Vector3 at, float yawDeg, bool snapCam = true)
        {
            pos = at; yaw = yawDeg * Mathf.Deg2Rad; camYaw = yaw; speed = 0f;
            snap = snapCam;
            Place();
            Walk(Vector2.zero, 0f);
        }
    }
}
