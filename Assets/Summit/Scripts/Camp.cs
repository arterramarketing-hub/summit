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
    ///
    /// In parts: walking and its camera (CampWalk.cs), the cliff edge (CampEdge.cs), the lodge, the stairs, the
    /// fire, the cocoa, the board and the snow (Camp*.cs).
    /// </summary>
    public partial class Camp : MonoBehaviour
    {
        public enum Station { None, Missions, Upgrades, Style, DropIn, Lookout, Talk, Roast, Cocoa }   // (the last three: the fire and the kitchen, CampFire.cs)

        // where the camp sits: on the plateau behind the start pad (downhill is +Z), so the run is right
        // in front of you and the view down the mountain is past the Drop In gate. It stands close behind the gate:
        // the chalet's steps are some 35 m from DROP IN (it was 30 m further back, on ground the start no longer
        // uses now a run starts at the gate, and the walk down took over 20 seconds)
        public const float BackZ = -96f, HalfWidth = 23f;   // (the camp's extent, for keeping trees off it: you can walk anywhere)
        // (camplayout) the lookout's deck at the cliff's edge, its front out past the rail over the lip, facing out (+x);
        // the snowman beside the shovelled path to it, not on it, looking over the plaza toward the gate
        public const float LookoutX = 24.2f, LookoutZ = -66.5f, LookoutHalfW = 3f;
        public const float SnowmanX = 13.9f, SnowmanZ = -63.6f, SnowmanYaw = 300f;
        /// <summary>On the lookout's deck's boards, or its open back's step (dx from the run's line at the lip): for the camp's people's grid.</summary>
        public static bool OnLookoutDeck(float dx, float z) => dx > LookoutX - 2.6f && dx < LookoutX + 1.9f && Mathf.Abs(z - LookoutZ) < LookoutHalfW - 0.3f;
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
        public const float StairWalkSpeed = 2.0f, StairRunSpeed = 2.8f;   // up and down the stairs (CampStairs)
        bool onStairs, stairRun;

        Shader lit;
        Transform root;
        Material wood, logs, darkWood, roof, snowCap, stone, metal, glass, bulb, wire, ember, rope, carrot, coal, scarf, birch, path;
        readonly Material[] flagCols = new Material[5];
        Material[] noteMats;
        GameObject firePit, flags, lights, snowman;

        class Spot { public Station st; public Vector3 at; public float radius; public Transform root; }
        readonly List<Spot> spots = new List<Spot>();
        struct Box { public Vector2 c, h; public float cos, sin, top, drop, lo, hi; public GameObject owner; public bool floor; public float[] prof; public float profStep; }   // (prof: a floor's height over top along it from its -z end, every profStep: the stairs, CampStairs)
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
            corralGear = new Material[4];
            var gearCols = new[] { new Color(0.86f, 0.25f, 0.18f), new Color(0.15f, 0.42f, 0.78f), new Color(0.95f, 0.78f, 0.2f), new Color(0.12f, 0.12f, 0.13f) };
            for (int i = 0; i < gearCols.Length; i++) corralGear[i] = M(gearCols[i], 0.55f);
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

        Material M(Color c, float smooth, Texture2D tex = null, Vector2 tile = default, Color emit = default)
        {
            var m = Mats.Lit(c, smooth, lit, tex: tex, tile: tile, emit: emit, instanced: true);
            if (emit.maxColorComponent > 0f) m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
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
            StartCorral();    // the strap-in corral before the gate: a rack, the trail map and today's missions
            Lookout(Frame(LookoutX, LookoutZ, 90f));   // out at the cliff's edge, facing out over the drop (camplayout)
            Benches();
            firePit = FirePit(Frame(0f, -71f, 0f)).gameObject;
            snowman = Snowman(Frame(SnowmanX, SnowmanZ, SnowmanYaw)).gameObject;
            Campfire();       // Alyssa and the stumps at the fire (CampFire.cs)
            CocoaCorner();    // the hot chocolate in the kitchen (CampCocoa.cs)
            flags = new GameObject("Flags"); flags.transform.SetParent(root, false);
            lights = new GameObject("Lights"); lights.transform.SetParent(root, false);
            Strings();
            Trees();
            Birches();
            Mounds();
            Stairs();         // up the bank from the gate (CampStairs.cs), before the path to their ends
            Footpath();
            CliffEdge();
            Mist();
            CampSnow();       // the snow underfoot by place, the shovelled path, the piles you walk on (CampSnow.cs)
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
                if (LiftTop.Covers(Cx + where[i].x, where[i].y, 2f)) continue;   // (off a design version's lift top: LiftTop)
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
                if (NearSummitPath(p, s * 0.75f + 1.6f)) continue;   // (nor onto the Summit lift's path in: camplayout)
                if (LiftTop.Covers(p.x, p.z, s * 0.75f + 1f)) continue;   // (nor onto a design version's lift top: LiftTop)
                // (its middle no higher than the lowest snow under its rim: on a slope it bulged out over the snow below)
                float low = p.y; for (int k = 0; k < 8; k++) low = Mathf.Min(low, Slope.DrawnHeight(p.x + 0.7f * s * Mathf.Cos(k * 0.785f), p.z + 0.5f * s * Mathf.Sin(k * 0.785f)));
                if (p.y - low > Mathf.Min(0.5f, 0.1f * s + 0.08f)) continue;   // (none on steep ground, the bank up from the gate: it doesn't sit on it, and walked into from below it's in your knees)
                P(PrimitiveType.Sphere, t, new Vector3(p.x, Mathf.Min(p.y - s * 0.22f, low - 0.02f), p.z), new Vector3(s * 1.4f, s * 0.6f, s), new Vector3(0f, i * 41f, 0f), snowCap);
            }
        }

        /// <summary>Within reach of the Summit lift's path into the camp (LiftEnds.CampPath)?</summary>
        static bool NearSummitPath(Vector3 p, float reach)
        {
            var path = LiftEnds.SummitPath();
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector2 a = new Vector2(path[i].x, path[i].z), b = new Vector2(path[i + 1].x, path[i + 1].z), q = new Vector2(p.x, p.z);
                var d = b - a; float t = d.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, d) / d.sqrMagnitude) : 0f;
                if ((a + d * t - q).magnitude < reach) return true;
            }
            return false;
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

        /// <summary>
        /// (camplayout) The start corral before the gate, out of the DROP IN circle, where people get ready: a ski and
        /// board rack on the left, and the missions board beside the stairs' foot (campstairs: CampBoard.cs, the trail
        /// map on its back), turned to the gate. The waxing stand on the right is the camp's
        /// people's (CampLife); the bench by the drop and the strap-in places either side of the gate were there already.
        /// Solid as drawn.
        /// </summary>
        void StartCorral()
        {
            float gz = Slope.GateZ;
            // the rack: two posts and two rails, boards and skis leaning on its gate side
            var r = Frame(-8.6f, gz - 8.4f, 0f); r.name = "CorralRack";   // (campstairs: out of the way of the missions board, seen from the gate)
            for (int s = -1; s <= 1; s += 2) Log(r, new Vector3(s * 1.35f, -0.3f, 0f), new Vector3(s * 1.35f, 1.15f, 0f), 0.07f, logs);
            Log(r, new Vector3(-1.45f, 1.05f, 0f), new Vector3(1.45f, 1.05f, 0f), 0.06f, logs);
            Log(r, new Vector3(-1.45f, 0.5f, 0f), new Vector3(1.45f, 0.5f, 0f), 0.05f, logs);
            P(PrimitiveType.Cube, r, new Vector3(0f, 1.11f, 0f), new Vector3(2.9f, 0.04f, 0.12f), Vector3.zero, snowCap);
            // boards leaning, their noses on the top rail; a pair of skis and another board
            foreach (var (x, m, w, len) in new[] { (-1.0f, 0, 0.27f, 1.5f), (-0.55f, 1, 0.27f, 1.45f), (0.75f, 2, 0.26f, 1.55f) })
                P(PrimitiveType.Cube, r, new Vector3(x, len * 0.45f, 0.3f), new Vector3(w, len, 0.03f), new Vector3(-24f, 0f, 0f), corralGear[m]);
            foreach (var x in new[] { 0.1f, 0.2f })
                P(PrimitiveType.Cube, r, new Vector3(x, 0.78f, 0.3f), new Vector3(0.075f, 1.7f, 0.02f), new Vector3(-22f, 0f, 0f), corralGear[3]);
            Block(r, new Vector3(0f, 0f, 0.25f), new Vector2(1.45f, 0.45f));
            // (campstairs) the missions board, by the stairs' foot and turned to the gate: CampBoard.cs
            MissionsBoard();
        }
        Material[] corralGear;   // (the rack's boards and skis: made once in Build, kept; made in a build they'd go at the next one: purplefix)

        void Lookout(Transform t)
        {
            t.name = "Lookout";
            // its boards a short step up from the snow along its open back, wherever that is highest (out on the cliff's
            // lip the ground under it is uneven; standing on the plateau it was 0.45 over its middle): camplayout
            float backHi = -9f;
            for (float bx = -2.7f; bx <= 2.71f; bx += 0.9f)
            {
                var bp = t.TransformPoint(new Vector3(bx, 0f, -2.1f));
                backHi = Mathf.Max(backHi, Slope.DrawnHeight(bp.x, bp.z) - t.position.y);
            }
            float y = Mathf.Clamp(backHi + 0.22f, 0.3f, 0.6f);
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
            Block(t, new Vector3(0f, 0f, -0.25f), new Vector2(2.9f, 2.25f), null, true, y);   // (its floor to the boards' open back edge and a stride past it: you step up onto it there, not into it)
            // (its sides, where it stands up off the snow below: solid for anyone whose feet are lower than a step up onto
            // its boards, FloorAt's 0.55 m, so nobody walks along the snow under them where the ground dips: camplayout)
            Block(t, Vector3.zero, new Vector2(3f, 2.1f), null, false, 0f, 0f, -3f, y - 0.42f);
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
            Block(t, new Vector3(-3.4f, 0f, -2.33f), new Vector2(0.88f, 0.08f), null, false, 0f, 0f, 1.5f, 2.3f);   // (its board, at head height)
            lookFrame = t; lookTop = y; lookFwd = t.forward;
            spots.Add(new Spot { st = Station.Lookout, at = t.position, radius = 2.6f, root = t });
        }
        Transform lookFrame; float lookTop;
        /// <summary>The lookout deck's boards' height (world), for the people standing on it (CampLife).</summary>
        public float LookoutDeckY => lookFrame != null ? lookFrame.position.y + lookTop : Ground(LookoutX, LookoutZ).y + 0.45f;
        Vector3 lookFwd = Vector3.forward;   // the way the lookout's deck faces (the lodge's deck on the wing's roof faces down the mountain)
        bool onCampLookout;

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
                if (LiftTop.Covers(Cx + where[i].x, where[i].y, 2.5f)) continue;   // (off a design version's lift top: LiftTop)
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
            BoardRefresh();   // (the board by the gate: CampBoard.cs)
        }
    }
}
