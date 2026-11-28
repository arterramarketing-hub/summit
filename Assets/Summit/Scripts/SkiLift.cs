using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The chairlifts you see: detachable four-seat chairs running up one side of a line of steel towers and
    /// back down the other, between a station at each end. Where they run is LiftLine's business (a pure function
    /// of the day's mountain: a line beside the piste in each cycle, and a second across it and over the first,
    /// LiftNetwork.cs, so there is more than one line in the sky); this draws them near the rider and makes them
    /// solid. They are modelled on the lifts at Whistler: tall plain galvanised tube towers with a T crossarm, a
    /// railed catwalk across the head and the ladder's cage under it, an orange-red pad at the snow.
    ///
    /// The models are made in Blender by Tools/MakeLift.py (Resources/Lift/Lift.fbx), every part painted from one
    /// small palette picture, so the whole lift is one material:
    ///   - Towers (a mast stretched to each tower's height, a head with its crossarm and platforms, a safety pad,
    ///     two sheave trains set along the cable), chairs, the riders on them and the stations are drawn in
    ///     instanced batches, one draw call a part. Within NearRange the full models, casting shadows; beyond it
    ///     plain versions, out to FarRange, where the fog has all but taken them. (Chairs, their riders and the
    ///     sheave trains go plain sooner, at ChairNear and TrainNear.)
    ///   - The cable is one small mesh for each span (both ropes, sagging), built as the span comes into range
    ///     and freed behind the rider, with the towers' and stations' colliders. It has its own shader
    ///     (Resources/LiftRope.shader): a haul rope's six strands and the wear on them up close, moving with the
    ///     line; far off, a line never thinner than a pixel and a half, so the other lifts' cables stay drawn
    ///     across the sky and do not crawl.
    ///   - The chairs have no state: each frame every chair's place on the loop is worked out from the clock
    ///     (LiftLine.Lift.Loop), with a slow swing of its own and a kick that dies away as it comes off each
    ///     tower's sheaves. Who rides them is LiftRiders' business (the mountain's people, from the hero's own
    ///     model, seated on the chairs and drawn by distance): about three chairs in four going up carry one to
    ///     four people, hardly any coming down do, drawn afresh each time a chair leaves the bottom station.
    ///     (LiftRiders.On = false brings back the old block figures from Lift.fbx.)
    ///
    /// A station's deck is ridden like a cabin's roof (Roofs): Stream registers it while it is solid.
    ///
    /// Solid: a tower's mast and pad, and each station, are colliders marked as obstacles, so riding into one off
    /// the piste is a crash like a tree ("Lift tower", "Lift station"). A station is solid where it is drawn: its
    /// deck, legs, bracing and railings, not the air under its deck (Stream). A chair is solid too: a jump high enough to
    /// reach one is a crash ("Chairlift!", or a shield spent), found by a simple test against the few nearest
    /// chairs. The cable never knocks anyone down: it is there to be ground (CableGrind.cs), reached off the hill that
    /// rises under each lift (LiftHill.cs), and while a rider is on it the chairs skim under the board and the
    /// rope dips and bounces with them. Nothing hangs within 9 m of the piste's snow (LiftLine).
    ///
    /// At dusk and night the work lamps under each crossarm, the red beacon on each tower and the stations'
    /// windows and floodlights glow (the palette's glow picture, brought up with DayNight.Darkness), and one real
    /// light sits at whichever lamp is nearest the rider.
    /// </summary>
    public partial class SkiLift : MonoBehaviour
    {
        public const float NearRange = 140f;       // full towers and shadows (the render asset's shadow distance)
        public const float ChairNear = 64f;        // full chairs and riders (there are two lines to a cycle now: twice the chairs)
        public const float TrainNear = 80f;        // full sheave trains, wheel by wheel
        public const float FarRange = 650f;        // nothing of the lift is drawn or built past here
        public float behind = 150f;                // kept this far up the hill behind the rider
        public const float ColliderRange = 170f;

        /// <summary>The one call the game makes: the lifts of whatever mountain is current, round this rider.</summary>
        public static SkiLift Create(Transform target, Shader lit)
        {
            var s = new GameObject("SkiLift").AddComponent<SkiLift>();
            s.target = target;
            s.rider = target != null ? target.GetComponent<Rider>() : null;
            SpreadBuild = !Application.isBatchMode;
            s.Build(lit);
            LiftEnds.Create(s.transform, target, lit);   // the ground at each end of each lift, and its people (LiftEnds.cs)
            return s;
        }

        /// <summary>For the render tools (Assets/Editor/LiftShot.cs): the chairs' clock, in seconds, when it is not the game's own.</summary>
        public static double TestClock = -1.0;

        /// <summary>
        /// A lift coming into range is built a few pieces a frame (its cable span by span, its hill's sign), not all in
        /// one: it comes into range 650 m ahead, long before any of it is near. Colliders made here are baked off the
        /// main thread before they are put on. (The batch tools, which call Update a few times and look, get it all at once.)
        /// </summary>
        public static bool SpreadBuild;
        const int BuildsPerRefresh = 2;
        int buildBudget;
        bool buildMore;
        const MeshColliderCookingOptions Cook = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning
                                                | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
        class Baking { public GameObject go; public Mesh mesh; public Task task; public bool freed; }
        readonly List<Baking> baking = new List<Baking>();

        /// <summary>A collider for this mesh, its cooking done on a worker thread (Physics.BakeMesh) and put on when ready.</summary>
        void BakeCollider(GameObject go, Mesh mesh)
        {
            int id = mesh.GetInstanceID();
            if (!SpreadBuild) { Physics.BakeMesh(id, false, Cook); Attach(go, mesh); return; }
            baking.Add(new Baking { go = go, mesh = mesh, task = Task.Run(() => Physics.BakeMesh(id, false, Cook)) });
        }

        static void Attach(GameObject go, Mesh mesh)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.cookingOptions = Cook;   // (as it was baked: the baked data is used, nothing is cooked here)
            mc.sharedMesh = mesh;
        }

        /// <summary>Colliders whose baking has finished go on; a mesh whose lift went meanwhile is let go now.</summary>
        void FinishBakes()
        {
            for (int i = baking.Count - 1; i >= 0; i--)
            {
                var b = baking[i];
                if (!b.task.IsCompleted) continue;
                if (b.freed || b.go == null) { if (b.mesh != null) Destroy(b.mesh); }
                else Attach(b.go, b.mesh);
                baking.RemoveAt(i);
            }
        }

        bool Baking_(Mesh m) { foreach (var b in baking) if (b.mesh == m) { b.freed = true; return true; } return false; }
        static double Clock => TestClock >= 0.0 ? TestClock : Time.timeAsDouble;
        // ---------------------------------------------------------------- the restraint bar (lifttop2)

        /// <summary>Where the bar's arms pivot on the chair's frame (the chair's own frame: the grip at the origin), and how far it swings up.</summary>
        public static readonly Vector3 BarPivot = new Vector3(0f, -2.0f, -0.02f);
        public const float BarSwing = 105f;
        static float Ramp(float x, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>
        /// How far up a chair's bar is (0 down .. 1 up) at a place on its loop: lowered just after loading, raised on the
        /// last span before the top (the riders lift it), up through the station and round, lowered again on the way
        /// down, and up again coming into the bottom to load.
        /// </summary>
        public static float BarUp(LiftLine.Lift l, float d)
        {
            if (l.cum == null) return 0f;
            int n = l.Spans;
            if (d < l.cum[0] + 14f) return 1f - Ramp(d, l.cum[0] + 5f, l.cum[0] + 14f);
            if (d < l.cum[n + 1] - 24f) return 0f;
            if (d < l.cum[n + 4] + 8f) return Ramp(d, l.cum[n + 1] - 24f, l.cum[n + 1] - 9f);
            if (d < l.cum[2 * n + 4] - 16f) return 1f - Ramp(d, l.cum[n + 4] + 8f, l.cum[n + 4] + 20f);
            return Ramp(d, l.cum[2 * n + 4] - 16f, l.cum[2 * n + 4] - 4f);
        }

        /// <summary>The bar's frame relative to the chair's at a lift of `up` (0..1).</summary>
        public static Matrix4x4 BarLocal(float up) => Matrix4x4.Translate(BarPivot) * Matrix4x4.Rotate(Quaternion.Euler(-BarSwing * up, 0f, 0f)) * Matrix4x4.Translate(-BarPivot);

        /// <summary>The bar's middle at a seat's x (chair frame), at a lift of `up`: where a hand takes it.</summary>
        public static Vector3 BarPoint(float x, float up) => BarLocal(up).MultiplyPoint3x4(new Vector3(x, LiftRiders.BarY, LiftRiders.BarZ));

        /// <summary>
        /// Split the chair's model in two: the restraint bar (its arms, the bar, the footrests on it) and the rest, so the
        /// bar can swing up on its own. By shape, in the chair's frame: everything ahead of the seat, and the arms along
        /// their line from the pivot. The rest keeps the chair's part; the bar gets one of its own.
        /// </summary>
        Part SplitBar(Part ch)
        {
            if (ch.mesh == null || !ch.mesh.isReadable) return null;
            var src = ch.mesh;
            var v = src.vertices;
            var g = new Vector3[v.Length];
            float cw = 0f;
            for (int i = 0; i < v.Length; i++) { g[i] = ch.local.MultiplyPoint3x4(v[i]); cw = Mathf.Max(cw, Mathf.Abs(g[i].x)); }
            Vector3 armA = BarPivot, armB = new Vector3(0f, LiftRiders.BarY, LiftRiders.BarZ);
            bool IsBar(Vector3 p)
            {
                if (p.z > 0.33f && p.y < -2.25f) return true;
                if (Mathf.Abs(p.x) < cw - 0.09f) return false;
                // along an arm: from the pivot to the bar, at the chair's ends
                var q = new Vector3(0f, p.y, p.z); var ab = armB - armA;
                float t = Mathf.Clamp01(Vector3.Dot(q - armA, ab) / ab.sqrMagnitude);
                return (armA + ab * t - q).magnitude < 0.055f && t > 0.12f;
            }
            var bodyTri = new List<int>(); var barTri = new List<int>();
            var subs = new List<int[]>();
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var tri = src.GetTriangles(sm);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    bool bar = IsBar(g[tri[i]]) && IsBar(g[tri[i + 1]]) && IsBar(g[tri[i + 2]]);
                    (bar ? barTri : bodyTri).AddRange(new[] { tri[i], tri[i + 1], tri[i + 2] });
                }
            }
            if (barTri.Count == 0) return null;
            Mesh Make(List<int> t, string name)
            {
                var m = Object.Instantiate(src); m.name = name;
                m.subMeshCount = 1; m.SetTriangles(t, 0); m.RecalculateBounds();
                return m;
            }
            ch.mesh = Make(bodyTri, "ChairBody");
            var bp = new Part { mesh = Make(barTri, "ChairBar"), local = ch.local, shadows = ch.shadows, props = ch.props };
            parts.Add(bp);
            return bp;
        }

        /// <summary>The lifts' clock (what every chair's place is worked out from).</summary>
        public static double Now => Clock;

        /// <summary>
        /// (lifttop: LiftArrive) One chair at a moment, as Chairs draws it: its frame (the grip, +z the way it goes,
        /// swinging as it does), how far round its loop it is, and which lap.
        /// </summary>
        public static Matrix4x4 ChairAt(LiftLine.Lift l, int c, double clock, out float d, out int lap)
        {
            double along = c * (double)l.spacing + clock * LiftLine.Speed;
            lap = (int)System.Math.Floor(along / l.loop);
            d = (float)(along - lap * (double)l.loop);
            l.Loop(d, out Vector3 pos, out Vector3 travel, out _, out float since);
            float t = (float)(clock % 3600.0);
            Vector3 flat = new Vector3(travel.x, 0f, travel.z);
            if (flat.sqrMagnitude < 1e-6f) flat = -l.dir;
            float kick = since < 60f ? 3.2f * Mathf.Exp(-since / 9f) * Mathf.Sin(since * 0.42f) : 0f;
            float pitch = 1.6f * Mathf.Sin(t * 1.3f + c * 1.7f) + kick;
            float roll = 1.1f * Mathf.Sin(t * 1.05f + c * 2.9f);
            if (since < 4f) pos.y += 0.035f * Mathf.Exp(-since / 1.2f) * Mathf.Sin(since * 5.2f);
            return Matrix4x4.TRS(pos, Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(pitch, 0f, roll), Vector3.one);
        }

        Transform target;
        Rider rider;
        Material paint, cableMat, cableFar, postMat;
        public const float RopeR = 0.03f;          // the haul rope: 60 mm
        public const float RopeNear = 120f;        // nearer than this a span is drawn opaque, its strands showing
        const int RopeSides = 6;
        bool ready;
        int seed = int.MinValue;

        // ---------------------------------------------------------------- the models

        class Part
        {
            public Mesh mesh; public Matrix4x4 local = Matrix4x4.identity; public bool shadows;
            public Material mat;   // (liftlook: a station's textured parts; the rest are painted from the palette)
            // with the world's far band on (Farview), a part with a near and a far version hands over by distance:
            // both drawn through the hand-over, each pixel one or the other (Shaders/Farview.hlsl, _FarviewNearFar)
            public MaterialPropertyBlock props;
            public readonly List<Matrix4x4> list = new List<Matrix4x4>();
            public int Tris => mesh != null ? (int)(mesh.GetIndexCount(0) / 3) : 0;
        }
        Part mast, mastLod, head, headLod, pad, sheaves, sheavesLod, chair, chairLod, station, chairBar;
        // (liftlook) a station's textured parts, its railings and its hut, one of each for either side
        Part stationDeck, stationGalv, stationShell, stationAccent, stationCowl, stationMat, stationLine, railR, railL, hutR, hutL, driftR, driftL;
        Part[] stationBody, stationNear;
        readonly Part[] riders = new Part[4], ridersLod = new Part[4];
        readonly List<Part> parts = new List<Part>();

        void Build(Shader lit)
        {
            var prefab = Resources.Load<GameObject>("Lift/Lift");
            if (prefab == null) { Debug.LogWarning("SkiLift: Resources/Lift/Lift.fbx is missing"); return; }
            var found = new Dictionary<string, Part>();
            var rootInv = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                // relative to the root, keeping the root's own rotation and scale (how Blender's axes and units come in:
                // its FBX holds the meshes in centimetres under a root scaled by 100, and without the root's scale every
                // part was drawn a hundredth of its size, which is to say not at all)
                found[mf.name] = new Part { mesh = mf.sharedMesh, local = Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale) * rootInv * mf.transform.localToWorldMatrix };
            }
            System.Func<string, bool, Part> P = (name, shadows) =>
            {
                if (!found.TryGetValue(name, out var p)) { Debug.LogWarning("SkiLift: no part called " + name + " in Lift.fbx"); p = new Part(); }
                p.shadows = shadows;
                parts.Add(p);
                return p;
            };
            mast = P("Mast", true); mastLod = P("MastLod", false);
            head = P("Head", true); headLod = P("HeadLod", false);
            pad = P("Pad", false);
            sheaves = P("Sheaves", false); sheavesLod = P("SheavesLod", false);
            chair = P("Chair", true); chairLod = P("ChairLod", false);
            station = P("Station", true);
            stationDeck = P("StationDeck", false); stationGalv = P("StationGalv", true); stationShell = P("StationShell", true);
            stationAccent = P("StationAccent", false); stationCowl = P("StationCowl", false); stationMat = P("StationMat", false);
            stationLine = P("StationLine", false); railR = P("RailR", false); railL = P("RailL", false); hutR = P("HutR", true); hutL = P("HutL", true);
            driftR = P("DriftR", false); driftL = P("DriftL", false);
            stationBody = new[] { station, stationDeck, stationGalv, stationShell, stationAccent };
            stationNear = new[] { stationCowl, stationMat, stationLine };
            for (int i = 0; i < 4; i++)
            {
                riders[i] = P("Riders" + (char)('A' + i), false);
                ridersLod[i] = P("Riders" + (char)('A' + i) + "Lod", false);
            }
            if (chair.mesh == null || mast.mesh == null) return;
            Pair(mast, mastLod, NearRange); Pair(head, headLod, NearRange); Pair(sheaves, sheavesLod, TrainNear); Pair(chair, chairLod, ChairNear);
            chairBar = SplitBar(chair);   // (lifttop2: the restraint bar its own piece, raised as a chair nears a station)
            for (int i = 0; i < 4; i++) Pair(riders[i], ridersLod[i], ChairNear);

            // The file is made in metres, but it says its unit is the centimetre (UnitScaleFactor 1), so the importer
            // brings every part in a hundred times too small: towers, heads, chairs and stations a few centimetres
            // across, drawn as nothing, while their colliders (built here in metres) stand full size - a rider hit an
            // invisible tower or station. Measure a chair (a couple of metres) and scale the parts back up if so
            {
                var wb = chair.local.MultiplyVector(chair.mesh.bounds.size);
                float chairSize = new Vector3(Mathf.Abs(wb.x), Mathf.Abs(wb.y), Mathf.Abs(wb.z)).magnitude;
                if (chairSize < 0.2f)
                {
                    var up = Matrix4x4.Scale(Vector3.one * 100f);
                    foreach (var p in parts) p.local = p.local * up;
                }
            }
            // The file is made in the game's axes, a chair facing +z with its seat below its grip. If the import
            // turned it round, turn every part back (and say so: the importer settings in Lift.fbx.meta have changed)
            var cb = chair.mesh.bounds;
            Vector3 cc = chair.local.MultiplyPoint3x4(cb.center);
            if (cc.y > -0.5f) Debug.LogWarning("SkiLift: Lift.fbx did not come in y-up (a chair's middle is at " + cc + ")");
            else if (cc.z < 0f)
            {
                Debug.LogWarning("SkiLift: Lift.fbx came in facing backward; turned round");
                var turn = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));
                foreach (var p in parts) p.local = turn * p.local;
            }

            var urp = Mats.LitShader;
            if (urp != null) lit = urp;   // (whatever the snow is drawn with these days, the lift wants the plain lit shader)
            var paintTex = Resources.Load<Texture2D>("Lift/LiftPaint");
            var glowTex = Resources.Load<Texture2D>("Lift/LiftGlow");
            paint = new Material(lit) { name = "LiftPaint", enableInstancing = true };
            paint.SetColor("_BaseColor", Color.white);
            if (paintTex != null) paint.SetTexture("_BaseMap", paintTex);
            paint.SetFloat("_Smoothness", 0.42f); paint.SetFloat("_Metallic", 0.15f);
            paint.EnableKeyword("_EMISSION");
            if (glowTex != null) paint.SetTexture("_EmissionMap", glowTex);
            paint.SetColor("_EmissionColor", Color.black);
            Farview.Adopt(paint);   // (the towers, chairs and stations come in through the world's far band)
            // the haul rope has a shader of its own (Resources/LiftRope.shader): stranded steel up close, a thin dark
            // line far off. Two materials of it: near spans opaque, far ones blended (a rope kept a pixel and a half
            // wide on screen is drawn fainter by as much)
            var rope = Resources.Load<Shader>("LiftRope");
            if (rope != null)
            {
                cableMat = new Material(rope) { name = "LiftRope" };
                cableMat.SetFloat("_Radius", RopeR); cableMat.SetFloat("_Speed", LiftLine.Speed);
                cableFar = new Material(cableMat) { name = "LiftRopeFar", renderQueue = (int)RenderQueue.Transparent - 20 };
                cableFar.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); cableFar.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                cableFar.SetFloat("_ZWrite", 0f);
                cableFar.SetOverrideTag("RenderType", "Transparent");
            }
            else
            {
                Debug.LogWarning("SkiLift: Resources/LiftRope.shader is missing; the cables are drawn plain");
                cableMat = new Material(lit) { name = "LiftCable" };
                cableMat.SetColor("_BaseColor", new Color(0.13f, 0.13f, 0.14f));
                cableMat.SetFloat("_Cull", 0f);
                cableFar = cableMat;
            }
            postMat = new Material(lit) { name = "LiftPost" };
            postMat.SetColor("_BaseColor", new Color(0.13f, 0.13f, 0.14f));
            postMat.SetFloat("_Smoothness", 0.5f); postMat.SetFloat("_Metallic", 0.6f);
            Farview.Adopt(postMat);
            // (liftlook) the stations' textures: Tools/MakeLiftTex.py's (Resources/Lift/Tex), and the lodge's planks and
            // concrete (CC0 sets: Resources/Camp/LodgeTex), all through the far band like the rest of the lift
            galvMat = TexMat(lit, "LiftGalv", "LiftGalv", new Color(0.97f, 0.97f, 0.98f), 0.32f, 0.35f);
            shellMat = TexMat(lit, "LiftShell", "LiftPanel", new Color(0.88f, 0.89f, 0.9f), 0.45f, 0.1f);
            accentMat = TexMat(lit, "LiftAccent", "LiftPanel", new Color(0.82f, 0.12f, 0.09f), 0.45f, 0.1f);
            cowlMat = TexMat(lit, "LiftCowl", "LiftPanel", new Color(0.26f, 0.27f, 0.29f), 0.4f, 0.4f);
            rubberMat = TexMat(lit, "LiftRubber", "LiftRubber", Color.white, 0.15f);
            hazardMat = TexMat(lit, "LiftHazard", "LiftHazard", Color.white, 0.3f);
            netMat = TexMat(lit, "LiftNet", "LiftNet", Color.white, 0.2f);
            snowMat = TexMat(lit, "LiftSnow", "LiftSnow", new Color(0.9f, 0.93f, 0.98f), 0.22f);   // (groomed: a shade under the powder round it)
            deckMat = LodgeMat("Decking", new Color(0.78f, 0.74f, 0.70f), galvMat);
            concreteMat = LodgeMat("Concrete", new Color(0.70f, 0.70f, 0.68f), galvMat);
            timberMat = LodgeMat("Glulam", new Color(0.62f, 0.50f, 0.40f), deckMat);   // (the wing's deck and trestle: weathered glulam)
            stationDeck.mat = TexMat(lit, "LiftDeck", "LiftDeck", Color.white, 0.2f); stationGalv.mat = galvMat; stationShell.mat = shellMat; stationAccent.mat = accentMat;
            stationCowl.mat = cowlMat; stationMat.mat = rubberMat; stationLine.mat = hazardMat; railR.mat = railL.mat = galvMat;
            rampMat = new Material(paint) { name = "LiftRamp" };   // (the palette again, seen from both sides: no winding to get wrong)
            rampMat.SetFloat("_Cull", 0f);
            litShader = lit;
            // the rider's own sphere never touches the body-only colliders on the ice layer (CourseDressing)
            if (target != null && target.TryGetComponent<Collider>(out var own)) own.excludeLayers |= 1 << CourseDressing.IceLayer;
            CableGrind.Hook(rider);
            CableGrind.ChairTick = at => { if (sound != null) sound.LiftClack(at, 0.6f); };

            var lg = new GameObject("LiftLamp");
            lg.transform.SetParent(transform, false);
            lamp = lg.AddComponent<Light>();
            lamp.type = LightType.Point; lamp.color = new Color(1f, 0.9f, 0.74f); lamp.range = 34f; lamp.shadows = LightShadows.None;
            lamp.enabled = false;
            ready = true;
            LiftRiders.Prewarm();   // the riders' poses baked for distance, now rather than when first seen
            if (System.Environment.GetEnvironmentVariable("SUMMIT_LIFTCHECK") == "1") Debug.Log("SkiLift\n" + LiftLine.Report() + LiftLine.HillReport());
        }

        // ---------------------------------------------------------------- what is built near the rider

        class Live
        {
            public LiftLine.Lift lift;
            public GameObject root;
            public GameObject[] span, tower;        // each span's cable; each tower's collider
            public GameObject stationTop, stationBottom;
            public Roofs.Roof deckTop, deckBottom;  // their decks, ridden (Roofs)
            public Roofs.Roof wingTop, lipTop;      // the top's unload wing and its lip (liftlook)
            public Collider[] stationTopCols, stationBottomCols;
            public Collider[][] towerCols;          // (kept, so the look round for switched-off colliders allocates nothing)
            public bool signBuilt;
        }
        readonly List<Live> live = new List<Live>();
        float refreshClock;
        Vector3 refreshedAt = new Vector3(0f, -1e9f, 0f);

        void Update()
        {
            if (!ready || target == null) return;
            if (seed != Slope.Seed) { Clear(); seed = Slope.Seed; }
            Vector3 p = target.position;
            refreshClock -= Time.unscaledDeltaTime;
            if (refreshClock <= 0f || (p - refreshedAt).sqrMagnitude > 12f * 12f)
            {
                refreshClock = 0.25f; refreshedAt = p;
                long t0 = PerfMarks.Now;
                Refresh(p);
                PerfMarks.MarkIfOver("lifts", t0, 0.5);
            }
            if (baking.Count > 0) FinishBakes();
            Chairs(p);
            Bend();
            Draw();
            Glow(p);
            Sound(p);
        }

        /// <summary>A new mountain: everything built for the old one goes.</summary>
        public void Clear()
        {
            foreach (var lv in live) Free(lv);
            live.Clear();
            foreach (var part in parts) part.list.Clear();
            LiftRiders.Clear();
            refreshClock = 0f;
            clackTower = -1;
            bent = null;
            CableGrind.Reset();
        }

        void Free(Live lv)
        {
            Roofs.Remove(lv.deckTop); Roofs.Remove(lv.deckBottom); lv.deckTop = lv.deckBottom = null;
            Roofs.Remove(lv.wingTop); Roofs.Remove(lv.lipTop); lv.wingTop = lv.lipTop = null;
            if (lv.root == null) return;
            // the cables' meshes are ours (the sign's are Unity's); one still being baked is let go once that is done
            foreach (var mf in lv.root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Lift") && !Baking_(mf.sharedMesh)) Destroy(mf.sharedMesh);
            Destroy(lv.root);
            lv.root = null;
        }

        bool InRange(float zMin, float zMax, float z) => zMax > z - behind && zMin < z + FarRange;

        void Refresh(Vector3 p)
        {
            var lifts = LiftLine.Lifts;
            buildBudget = SpreadBuild ? BuildsPerRefresh : int.MaxValue; buildMore = false;
            // lifts coming into range, and going out of it
            for (int i = live.Count - 1; i >= 0; i--)
                if (!InRange(live[i].lift.ZMin, live[i].lift.ZMax, p.z)) { Free(live[i]); live.RemoveAt(i); }
            foreach (var l in lifts)
            {
                if (!InRange(l.ZMin, l.ZMax, p.z)) continue;
                bool have = false;
                foreach (var lv in live) if (lv.lift == l) have = true;
                if (have) continue;
                var root = new GameObject("Lift " + l.k + (l.second ? "b" : ""));
                root.transform.SetParent(transform, false);
                var made = new Live { lift = l, root = root, span = new GameObject[l.Spans], tower = new GameObject[l.s.Length], towerCols = new Collider[l.s.Length][] };
                live.Add(made);
            }

            foreach (var part in parts) if (part != chair && part != chairLod && System.Array.IndexOf(riders, part) < 0 && System.Array.IndexOf(ridersLod, part) < 0) part.list.Clear();
            foreach (var lv in live)
            {
                var l = lv.lift;
                Vector3 axis = l.Axis, fwd = l.dir * Mathf.Cos(l.lean) - Vector3.up * Mathf.Sin(l.lean);
                if (!lv.signBuilt) { if (buildBudget > 0) { buildBudget--; lv.signBuilt = true; HillSign(lv); } else buildMore = true; }
                // the cable, span by span
                for (int i = 0; i < l.Spans; i++)
                {
                    float za = l.top.z + l.dir.z * l.nodeS[i], zb = l.top.z + l.dir.z * l.nodeS[i + 1];
                    bool want = InRange(Mathf.Min(za, zb), Mathf.Max(za, zb), p.z);
                    if (want && lv.span[i] == null) { if (buildBudget > 0) { buildBudget--; lv.span[i] = Cable(lv, i); } else buildMore = true; }
                    else if (!want && lv.span[i] != null) { Destroy(lv.span[i].GetComponent<MeshFilter>().sharedMesh); Destroy(lv.span[i]); lv.span[i] = null; }
                    if (lv.span[i] != null)
                    {
                        // near the rider it is opaque steel; further off, the blended line
                        float along = Mathf.Clamp((p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z, l.nodeS[i], l.nodeS[i + 1]);
                        Vector3 nearest = l.At(along, 0f, l.CableY(i, along));
                        var mat = (nearest - p).sqrMagnitude < RopeNear * RopeNear ? cableMat : cableFar;
                        var mr = lv.span[i].GetComponent<MeshRenderer>();
                        if (mr.sharedMaterial != mat) mr.sharedMaterial = mat;
                    }
                }
                // the towers: drawn, and solid when near
                for (int i = 0; i < l.s.Length; i++)
                {
                    Vector3 f = l.foot[i], h = l.head[i];
                    bool drawn = InRange(f.z, f.z, p.z);
                    float d2 = (f - p).sqrMagnitude;
                    bool solid = drawn && d2 < ColliderRange * ColliderRange;
                    if (solid && lv.tower[i] == null) { lv.tower[i] = TowerCollider(lv, i, axis, fwd); lv.towerCols[i] = lv.tower[i].GetComponents<Collider>(); }
                    else if (!solid && lv.tower[i] != null) { Destroy(lv.tower[i]); lv.tower[i] = null; lv.towerCols[i] = null; }
                    // (an invincible rider switches off what it rides through: back on once it has gone by)
                    if (lv.tower[i] != null && d2 > 8f * 8f) foreach (var c in lv.towerCols[i]) if (c != null && !c.enabled) c.enabled = true;
                    if (!drawn) continue;
                    float d = Flat(f, p);
                    var mastM = Frame(f - axis * 1.5f, l.across, axis * (l.h[i] + 1.5f), fwd);
                    Put(mast, mastLod, NearRange, d, mastM, Slack);
                    Put(head, headLod, NearRange, d, Frame(h, l.across, axis, fwd), Slack);
                    if (d2 < 320f * 320f) pad.list.Add(Frame(f, l.across, axis, fwd) * pad.local);
                    // the sheave trains: along the cable as it crosses the tower, under it; or over it, where the
                    // cable would otherwise lift off (a hollow in the line)
                    int j = i + 1;
                    float lenIn = l.nodeS[j] - l.nodeS[j - 1], lenOut = l.nodeS[j + 1] - l.nodeS[j];
                    float slopeIn = (l.nodeY[j] - l.nodeY[j - 1] + 4f * LiftLine.Sag(lenIn)) / lenIn;
                    float slopeOut = (l.nodeY[j + 1] - l.nodeY[j] - 4f * LiftLine.Sag(lenOut)) / lenOut;
                    Vector3 t = (l.dir + Vector3.up * (0.5f * (slopeIn + slopeOut))).normalized;
                    Vector3 n = Vector3.Cross(t, l.across).normalized;   // square to the cable, upward
                    float k = slopeOut > slopeIn + 0.01f ? -1f : 1f;      // a hold-down train is the same one turned over
                    for (int side = -1; side <= 1; side += 2)
                        Put(sheaves, sheavesLod, TrainNear, d, Frame(h + l.across * (side * LiftLine.Half), l.across * side, n * k, t * (side * k)), Slack);
                }
                // the stations
                bool topIn = InRange(l.top.z - 8f, l.top.z + 8f, p.z), bottomIn = InRange(l.bottom.z - 8f, l.bottom.z + 8f, p.z);
                // (the top's unload wing opens one side, liftlook: no railing there, and the hut on the other side)
                var wing = LiftLine.UnloadWing(l);
                if (topIn) PutStation(Frame(l.top, l.across, Vector3.up, l.dir), Flat(l.top, p), wing == null ? 0 : wing.s > 0f ? 1 : -1);
                if (bottomIn) PutStation(Frame(l.bottom, -l.across, Vector3.up, -l.dir), Flat(l.bottom, p), 0);
                Stream(ref lv.stationTop, ref lv.deckTop, ref lv.stationTopCols, topIn && (l.top - p).sqrMagnitude < 240f * 240f, lv, l.top, l.dir, wing);
                Stream(ref lv.stationBottom, ref lv.deckBottom, ref lv.stationBottomCols, bottomIn && (l.bottom - p).sqrMagnitude < 240f * 240f, lv, l.bottom, -l.dir, null);
            }
            // (pieces left to build: on with them next frame)
            if (buildMore) refreshClock = 0f;
        }

        /// <summary>
        /// A station's parts (liftlook): its body out to FarRange; its matting, painted lines, skirt, railings and hut
        /// only near (NearRange). open: the side its unload wing is on in the station's frame (+1 its +x, -1 its -x, 0
        /// none): no railing there, and the hut on the other side.
        /// </summary>
        void PutStation(Matrix4x4 f, float d, int open)
        {
            foreach (var part in stationBody) if (part.mesh != null) part.list.Add(f * part.local);
            if (d > NearRange + Slack) return;
            foreach (var part in stationNear) if (part.mesh != null) part.list.Add(f * part.local);
            if (open != 1 && railR.mesh != null) { railR.list.Add(f * railR.local); if (driftR.mesh != null) driftR.list.Add(f * driftR.local); }
            if (open != -1 && railL.mesh != null) { railL.list.Add(f * railL.local); if (driftL.mesh != null) driftL.list.Add(f * driftL.local); }
            var hut = open == 1 ? hutL : hutR;
            if (hut.mesh != null) hut.list.Add(f * hut.local);
        }

        // ---------------------------------------------------------------- near and far versions

        /// <summary>How wide a hand-over between a part's near and far versions is, either side of its range (m); and
        /// how much further each list reaches, for the rider's ride between two refreshes (Refresh: 12 m, a quarter second).</summary>
        const float Hand = 8f, Slack = 14f;

        static void Pair(Part near, Part far, float range)
        {
            if (near == null || far == null) return;
            near.props = new MaterialPropertyBlock(); near.props.SetVector(NearFarId, new Vector4(range - Hand, range + Hand, 1f, 0f));
            far.props = new MaterialPropertyBlock(); far.props.SetVector(NearFarId, new Vector4(range - Hand, range + Hand, -1f, 0f));
        }
        static readonly int NearFarId = Shader.PropertyToID("_FarviewNearFar");

        /// <summary>Across the ground, as the shader measures the hand-over (Shaders/Farview.hlsl).</summary>
        static float Flat(Vector3 a, Vector3 b) { float x = a.x - b.x, z = a.z - b.z; return Mathf.Sqrt(x * x + z * z); }

        /// <summary>Something `d` metres off with a near and a far version: in one list, or (the band on) in both while it
        /// may be handing over.</summary>
        static void Put(Part near, Part far, float range, float d, Matrix4x4 m, float slack)
        {
            if (!Farview.On) { var p = d < range ? near : far; p.list.Add(m * p.local); return; }
            if (d < range + Hand + slack) near.list.Add(m * near.local);
            if (d > range - Hand - slack) far.list.Add(m * far.local);
        }

        /// <summary>A matrix from a place and three axes (the second may carry a stretch).</summary>
        static Matrix4x4 Frame(Vector3 at, Vector3 x, Vector3 y, Vector3 z)
        {
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            m.SetColumn(3, new Vector4(at.x, at.y, at.z, 1f));
            return m;
        }

        // ---------------------------------------------------------------- the cable

        /// <summary>
        /// One span's cable: both ropes from one support to the next, hanging in their sag. The first and last
        /// spans carry on level into their station, to the bullwheel. Each vertex is a point of a rope's centre
        /// line, with the way out from it as its normal and the way along it as its tangent: the shader gives the
        /// rope its thickness (and keeps a far one from vanishing between pixels).
        /// </summary>
        GameObject Cable(Live lv, int i)
        {
            var l = lv.lift;
            var v = new List<Vector3>(); var tri = new List<int>();
            var nrm = new List<Vector3>(); var tan = new List<Vector4>(); var uv = new List<Vector3>();
            Vector3 origin = l.At(l.nodeS[i], 0f, l.nodeY[i]);
            CableVerts(l, i, v, 0f, 0f, 0f, nrm, tan, uv);
            const int per = RopeSides + 1;   // (the seam's vertex twice: the strands' pattern runs right round)
            int ring = v.Count / (2 * per);  // points along each rope
            for (int side = 0; side < 2; side++)
                for (int q = 0; q < ring - 1; q++)
                    for (int e = 0; e < RopeSides; e++)
                    {
                        int a0 = (side * ring + q) * per + e, a1 = a0 + 1, b0 = a0 + per, b1 = b0 + 1;
                        tri.Add(a0); tri.Add(b0); tri.Add(a1);
                        tri.Add(a1); tri.Add(b0); tri.Add(b1);
                    }
            var mesh = new Mesh { name = "LiftCable" };
            mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetTangents(tan); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            RopeBounds(mesh);
            var go = new GameObject("Cable " + i);
            go.transform.SetParent(lv.root.transform, false);
            go.transform.position = origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = cableFar;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return go;
        }

        /// <summary>(the vertices are the centre line: room for the rope's own thickness, and for a far one's fattening)</summary>
        static void RopeBounds(Mesh mesh)
        {
            mesh.RecalculateBounds();
            var b = mesh.bounds; b.Expand(1.2f); mesh.bounds = b;
        }

        /// <summary>
        /// A span's cable as vertices round its own first support: the rope coming down (side -1), then the rope
        /// going up; RopeSides + 1 vertices at each point of a rope's centre line. With a rider on one of them
        /// (bendSide), that rope hangs in a V under the board, `dip` deep. Given the other lists, fills them too.
        /// </summary>
        void CableVerts(LiftLine.Lift l, int i, List<Vector3> v, float bendSide, float bendAlong, float dip,
                        List<Vector3> nrm = null, List<Vector4> tan = null, List<Vector3> uv = null)
        {
            float sa = l.nodeS[i], sb = l.nodeS[i + 1];
            int m = Mathf.Max(4, Mathf.CeilToInt((sb - sa) / 8f));
            Vector3 origin = l.At(sa, 0f, l.nodeY[i]);
            v.Clear();
            for (int side = -1; side <= 1; side += 2)
            {
                cablePts.Clear();
                if (i == 0) cablePts.Add(l.At(-LiftLine.WheelBack, side * LiftLine.Half, l.nodeY[0]));
                for (int q = 0; q <= m; q++)
                {
                    float s = Mathf.Lerp(sa, sb, q / (float)m);
                    float y = l.CableY(i, s);
                    if (dip != 0f && side == (int)bendSide)
                        y -= dip * Mathf.Clamp01(s < bendAlong ? (s - sa) / Mathf.Max(0.5f, bendAlong - sa) : (sb - s) / Mathf.Max(0.5f, sb - bendAlong));
                    cablePts.Add(l.At(s, side * LiftLine.Half, y));
                }
                if (i == l.Spans - 1) cablePts.Add(l.At(l.plan + LiftLine.WheelBack, side * LiftLine.Half, l.nodeY[l.Spans]));
                float run = l.nodeS[i] * 1.03f;   // metres along the rope, near enough: the strands' and the wear's pattern
                for (int q = 0; q < cablePts.Count; q++)
                {
                    Vector3 c = cablePts[q] - origin;
                    for (int e = 0; e <= RopeSides; e++) v.Add(c);
                    if (nrm == null) continue;
                    if (q > 0) run += (cablePts[q] - cablePts[q - 1]).magnitude;
                    Vector3 t = (cablePts[Mathf.Min(q + 1, cablePts.Count - 1)] - cablePts[Mathf.Max(q - 1, 0)]).normalized;
                    Vector3 up = Vector3.Cross(t, l.across).normalized;
                    for (int e = 0; e <= RopeSides; e++)
                    {
                        float a = e * (2f * Mathf.PI / RopeSides);
                        nrm.Add(up * Mathf.Cos(a) + l.across * Mathf.Sin(a));
                        tan.Add(new Vector4(t.x, t.y, t.z, 1f));
                        uv.Add(new Vector3(e / (float)RopeSides, run, side));
                    }
                }
            }
        }
        readonly List<Vector3> cablePts = new List<Vector3>(), bendVerts = new List<Vector3>();
        Mesh bent;   // the span's mesh that is hanging under the rider just now

        /// <summary>The cable under a grinding rider dips and bounces with them, and springs back when they leave it.</summary>
        void Bend()
        {
            Mesh now = null;
            if (CableGrind.Riding(out var l, out int i, out float side, out float along, out float dip))
                foreach (var lv in live)
                    if (lv.lift == l && i < lv.span.Length && lv.span[i] != null)
                    {
                        now = lv.span[i].GetComponent<MeshFilter>().sharedMesh;
                        // (the board is carried along a little ahead of the rope's middle point: draw the V where it is)
                        CableVerts(l, i, bendVerts, side, along, dip);
                        now.SetVertices(bendVerts); RopeBounds(now);
                    }
            if (bent != null && bent != now)
                foreach (var lv in live)
                    for (int k = 0; k < lv.span.Length; k++)
                        if (lv.span[k] != null && lv.span[k].GetComponent<MeshFilter>().sharedMesh == bent)
                        {
                            CableVerts(lv.lift, k, bendVerts, 0f, 0f, 0f);
                            bent.SetVertices(bendVerts); RopeBounds(bent);
                        }
            bent = now;
        }

        static Vector2 Paint(int cell) => new Vector2((cell % 8 + 0.5f) / 8f, 1f - (cell / 8 + 0.5f) / 8f);   // a colour of LiftPaint.png (Tools/MakeLift.py's palette)

        // ---------------------------------------------------------------- the hill's sign (LiftHill.cs)

        /// <summary>
        /// A jump sign at the piste's edge where the cleared lane down to a lift's hill starts. (The hill itself is the
        /// mountain's own snow: Slope.Height has it, so the terrain draws it and the rider rides it.)
        /// </summary>
        void HillSign(Live lv)
        {
            var l = lv.lift; var h = l.hill;
            if (h == null) return;
            // the sign: a board on a post, facing up the hill
            if (signMat == null) signMat = CourseDressing.Sign(litShader, "Signs/SignJump");
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(post.GetComponent<Collider>());
            post.name = "LiftHillSignPost";
            post.transform.SetParent(lv.root.transform, false);
            post.transform.position = h.sign + Vector3.up * 1.2f;
            post.transform.localScale = new Vector3(0.12f, 3.4f, 0.12f);
            post.GetComponent<MeshRenderer>().sharedMaterial = postMat;
            var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(board.GetComponent<Collider>());
            board.name = "LiftHillSign";
            board.transform.SetParent(lv.root.transform, false);
            // (a quad shows its face to whoever looks along its own +z: turned to face the rider coming down the lane)
            Vector3 lane = l.At(h.s - h.rise, h.side * LiftLine.Half, 0f) - h.laneFrom; lane.y = 0f;
            board.transform.SetPositionAndRotation(h.sign + Vector3.up * 2.4f - lane.normalized * 0.08f, Quaternion.LookRotation(lane.normalized, Vector3.up));
            board.transform.localScale = new Vector3(1.7f, 1.7f, 1f);
            board.GetComponent<MeshRenderer>().sharedMaterial = signMat;
        }
        Material rampMat, signMat;
        Material galvMat, shellMat, accentMat, cowlMat, rubberMat, hazardMat, netMat, snowMat, deckMat, concreteMat, timberMat;

        /// <summary>A plain lit material with one of the lift's own textures (liftlook), through the far band.</summary>
        static Material TexMat(Shader lit, string name, string tex, Color c, float smooth, float metal = 0f)
        {
            var m = new Material(lit) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", c);
            var t = Resources.Load<Texture2D>("Lift/Tex/" + tex);
            if (t != null) m.SetTexture("_BaseMap", t); else Debug.LogWarning("SkiLift: no texture Lift/Tex/" + tex);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return Farview.Adopt(m);
        }

        /// <summary>One of the lodge's PBR materials (its planks, its concrete), tinted for the lift (a copy: the lodge's own stays as it is).</summary>
        static Material LodgeMat(string name, Color tint, Material fallback)
        {
            var a = Resources.Load<Material>("Camp/LodgeTex/Materials/" + name);
            if (a == null) return fallback;
            var m = new Material(a) { name = "Lift" + name, enableInstancing = true };
            m.SetColor("_BaseColor", tint);
            return Farview.Adopt(m);
        }
        Shader litShader;

        // ---------------------------------------------------------------- meshes built here

        /// <summary>A mesh built quad by quad: each one colour of the palette, each facing the way it is told to.</summary>
        class Shape
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> tri = new List<int>();
            public Vector3 origin;
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int cell, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a), outward) < 0f) { var s = b; b = d; d = s; }
                int n = v.Count;
                v.Add(a - origin); v.Add(b - origin); v.Add(c - origin); v.Add(d - origin);
                var p = Paint(cell); uv.Add(p); uv.Add(p); uv.Add(p); uv.Add(p);
                tri.Add(n); tri.Add(n + 1); tri.Add(n + 2); tri.Add(n); tri.Add(n + 2); tri.Add(n + 3);
            }
            /// <summary>A bar from a to b, w wide and h deep.</summary>
            public void Bar(Vector3 a, Vector3 b, float w, float h, int cell, bool caps = true)
            {
                Vector3 d = (b - a).normalized;
                Vector3 s = Vector3.Cross(Vector3.up, d); if (s.sqrMagnitude < 1e-4f) s = Vector3.right;
                s = s.normalized * (w * 0.5f);
                Vector3 u = Vector3.Cross(d, s).normalized * (h * 0.5f);
                Quad(a - s + u, a + s + u, b + s + u, b - s + u, cell, u);
                Quad(a - s - u, a + s - u, b + s - u, b - s - u, cell, -u);
                Quad(a + s - u, a + s + u, b + s + u, b + s - u, cell, s);
                Quad(a - s - u, a - s + u, b - s + u, b - s - u, cell, -s);
                if (!caps) return;
                Quad(a - s - u, a + s - u, a + s + u, a - s + u, cell, -d);
                Quad(b - s - u, b + s - u, b + s + u, b - s + u, cell, d);
            }
            public Mesh Make(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ---------------------------------------------------------------- solid things

        GameObject TowerCollider(Live lv, int i, Vector3 axis, Vector3 fwd)
        {
            var l = lv.lift;
            var go = new GameObject("LiftTower");
            go.transform.SetParent(lv.root.transform, false);
            go.transform.SetPositionAndRotation(l.foot[i], Quaternion.LookRotation(fwd, axis));
            // the pad round its foot, and the mast above it
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1; cap.radius = 0.82f; cap.height = 3.4f; cap.center = new Vector3(0f, 0.6f, 0f);
            var up = go.AddComponent<CapsuleCollider>();
            up.direction = 1; up.radius = 0.42f; up.height = l.h[i] + 1f; up.center = new Vector3(0f, l.h[i] * 0.5f, 0f);
            go.AddComponent<Obstacle>().label = "Lift tower";
            return go;
        }

        /// <summary>
        /// A station's solid parts, built while the rider is near, and its deck as ground to ride (Roofs.Deck). Solid
        /// where it is drawn (Tools/MakeLift.py): the deck and its frame, a little under its top (a board riding on it
        /// never touches them; a rider's height under it, so whoever rides in under it where it is that low meets its
        /// edge); the six legs down into the snow, and the beams that brace them, along each side and across under
        /// the deck at two heights (between them, where the deck stands high over falling ground, there is room to
        /// ride through); the railings along both sides; the column in the middle of the chairs' loop; the terminal
        /// overhead; the hut. Both ends are open: where an end meets the snow, a bank of snow eases the board up
        /// onto the deck; where the ground has fallen away under it, it is a launch.
        /// </summary>
        void Stream(ref GameObject go, ref Roofs.Roof roof, ref Collider[] cols, bool want, Live lv, Vector3 deck, Vector3 outward, LiftLine.Wing wing)
        {
            if (want && go == null && buildBudget <= 0) { buildMore = true; return; }   // (next frame: SpreadBuild)
            if (want && go == null)
            {
                buildBudget--;
                go = new GameObject("LiftStation");
                go.transform.SetParent(lv.root.transform, false);
                go.transform.SetPositionAndRotation(deck, Quaternion.LookRotation(outward, Vector3.up));
                const float legs = 14f, W = LiftLine.StationHalfWid, L = LiftLine.StationHalfLen;
                var t = go.transform;
                int open = wing == null ? 0 : wing.s > 0f ? 1 : -1;   // (the side its unload wing opens, in its frame: its +x is the lift's across)
                System.Action<string, Vector3, Vector3, Quaternion, bool> Box = (name, at, size, rot, ground) =>
                {
                    var g = new GameObject(name);
                    g.transform.SetParent(t, false);
                    g.transform.localPosition = at; g.transform.localRotation = rot;
                    g.AddComponent<BoxCollider>().size = size;
                    var ob = g.AddComponent<Obstacle>(); ob.label = "Lift station"; ob.roof = ground;
                };
                // the deck and its frame: from Roofs.Margin under its top (it is ridden: Roofs) to a rider's height under it
                Box("Deck", new Vector3(0f, -(Roofs.Margin + 1.4f) * 0.5f, 0f), new Vector3(2f * W, 1.4f - Roofs.Margin, 2f * L), Quaternion.identity, true);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    float x = sx * (W - 0.5f);
                    // (up to the deck's solid, not past it: a board riding over a leg's head never touches it)
                    for (int sz = -1; sz <= 1; sz++)
                        Box("Leg", new Vector3(x, -legs * 0.5f - Roofs.Margin, sz * (L - 0.5f)), new Vector3(0.5f, legs, 0.5f), Quaternion.identity, false);
                    // the side's bracing: crossed beams between the legs, and a beam along at each crossing
                    foreach (var (z0, z1) in new[] { (-L + 0.5f, 0f), (0f, L - 0.5f) })
                        foreach (var (ya, yb) in new[] { (-0.6f, -5.2f), (-5.2f, -0.6f), (-5.2f, -9.8f), (-9.8f, -5.2f) })
                            Beam(t, new Vector3(x, ya, z0), new Vector3(x, yb, z1), 0.14f);
                    foreach (float y in new[] { -5.2f, -9.8f }) Beam(t, new Vector3(x, y, -L + 0.5f), new Vector3(x, y, L - 0.5f), 0.2f);
                    // the railing: posts and two rails, 1.1 m high (as one thin wall); none on the unload wing's side
                    if (sx != open) Box("Railing", new Vector3(sx * (W - 0.1f), 0.58f, 0f), new Vector3(0.1f, 1.16f, 2f * L - 0.12f), Quaternion.identity, false);
                }
                foreach (float y in new[] { -5.2f, -9.8f })
                    foreach (float z in new[] { -L + 0.5f, 0f, L - 0.5f })
                        Beam(t, new Vector3(-(W - 0.5f), y, z), new Vector3(W - 0.5f, y, z), 0.2f);
                // the terminal overhead, its column, the column's foot, the hut
                Box("Terminal", new Vector3(0f, LiftLine.StationCable + 1.6f, -0.15f), new Vector3(LiftLine.Gauge + 2.3f, 3.1f, 11.6f), Quaternion.identity, false);
                Box("Column", new Vector3(0f, 2.5f, -0.2f), new Vector3(0.9f, 5f, 1.1f), Quaternion.identity, false);
                Box("ColumnFoot", new Vector3(0f, 0.15f, -0.2f), new Vector3(1.5f, 0.3f, 1.7f), Quaternion.identity, false);
                Box("Hut", new Vector3((open == 1 ? -1f : 1f) * (W - 0.85f), 1.3f, 3.2f), new Vector3(1.6f, 2.6f, 2.5f), Quaternion.identity, false);
                // its deck is ground, with the snow banked against whichever end meets the snow
                roof = Roofs.Add(Roofs.Deck, t, "LiftDeck");
                Drifts(lv, roof);
                // (liftlook) the top's unload wing, ridden and solid; and what stands round it: footings, pads, fences
                if (wing != null) Wing(lv, t, wing, open);
                Dressing(t, wing, open);
                cols = go.GetComponentsInChildren<Collider>();
            }
            else if (!want && go != null)
            {
                Roofs.Remove(roof); roof = null;
                if (wing != null) { Roofs.Remove(lv.wingTop); Roofs.Remove(lv.lipTop); lv.wingTop = lv.lipTop = null; }
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) Destroy(mf.sharedMesh);   // (its snow banks)
                Destroy(go); go = null; cols = null;
            }
            else if (go != null && cols != null && (deck - target.position).sqrMagnitude > 20f * 20f) foreach (var c in cols) if (c != null && !c.enabled) c.enabled = true;
        }

        /// <summary>A beam of the station's bracing, solid as drawn: from a to b (station frame), w square.</summary>
        static void Beam(Transform t, Vector3 a, Vector3 b, float w)
        {
            var g = new GameObject("Brace");
            g.transform.SetParent(t, false);
            g.transform.localPosition = (a + b) * 0.5f;
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, b - a);
            g.AddComponent<BoxCollider>().size = new Vector3(w, w, (b - a).magnitude);
            g.AddComponent<Obstacle>().label = "Lift station";
        }

        /// <summary>
        /// Snow banked against the ends of a station's deck where the snow comes up near it: a bank sloping down from
        /// the deck's edge at Roofs.RampSlope until it meets the snow, so the board rides up onto the deck instead of
        /// tripping on its lip. None where the snow is further down than RampMax of that slope reaches: that end is a
        /// launch. Drawn as one mesh of the lift's snow, and ridden (Roofs: the roof's rampFront, rampBack).
        /// </summary>
        void Drifts(Live lv, Roofs.Roof roof, bool drawn = true)
        {
            const int SnowCell = 19;
            var s = roof.s;
            int n = Mathf.RoundToInt((s.x1 - s.x0) / Roofs.RampStep) + 1;
            var draw = new Shape { origin = roof.o };
            for (int e = 0; e < 2; e++)
            {
                float sign = e == 0 ? 1f : -1f, edge = e == 0 ? s.z1 : s.z0;
                var len = new float[n];
                int any = 0;
                for (int i = 0; i < n; i++)
                {
                    float x = s.x0 + i * Roofs.RampStep;
                    // where the bank's slope comes down to the snow (none if the snow is above the deck, or too far below)
                    float lo = 0f, hi = Roofs.RampMax;
                    System.Func<float, float> Over = d => { Vector3 w = roof.World(x, 0f, edge + sign * d); return roof.o.y - Roofs.RampSlope * d - Slope.DrawnHeight(w.x, w.z); };
                    if (Over(0f) <= 0.02f || Over(hi) > 0f) { len[i] = 0f; continue; }
                    for (int k = 0; k < 14; k++) { float m = (lo + hi) * 0.5f; if (Over(m) > 0f) lo = m; else hi = m; }
                    len[i] = hi; any++;
                }
                if (any < 2) continue;
                if (e == 0) roof.rampFront = len; else roof.rampBack = len;
                Vector3 up = Vector3.up;
                for (int i = 0; i + 1 < n; i++)
                {
                    if (len[i] <= 0f || len[i + 1] <= 0f) continue;
                    float xa = s.x0 + i * Roofs.RampStep, xb = xa + Roofs.RampStep;
                    Vector3 a0 = roof.World(xa, 0f, edge), b0 = roof.World(xb, 0f, edge);
                    Vector3 a1 = roof.World(xa, -Roofs.RampSlope * len[i], edge + sign * len[i]);
                    Vector3 b1 = roof.World(xb, -Roofs.RampSlope * len[i + 1], edge + sign * len[i + 1]);
                    draw.Quad(a0, b0, b1, a1, SnowCell, up);
                    // (a skirt down into the snow under its foot, so no gap shows at its edge)
                    draw.Quad(a1, b1, b1 + Vector3.down * 0.4f, a1 + Vector3.down * 0.4f, SnowCell, roof.WorldDir(new Vector3(0f, 0f, sign)));
                }
            }
            if (draw.v.Count == 0 || !drawn) return;   // (a wing's banks are drawn with its snow: SnowSkin)
            var g = new GameObject("LiftDeckDrift");
            g.transform.SetParent(roof.t, true);
            g.transform.SetPositionAndRotation(roof.o, Quaternion.identity);
            g.AddComponent<MeshFilter>().sharedMesh = draw.Make("LiftDeckDrift");
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rampMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- the chairs

        /// <summary>
        /// Every chair of every lift in range, from the clock alone: where it is on its loop, which way it hangs,
        /// who is on it. And the one way a chair can touch the rider: by being jumped into.
        /// </summary>
        void Chairs(Vector3 p)
        {
            chair.list.Clear(); chairLod.list.Clear();
            for (int i = 0; i < 4; i++) { riders[i].list.Clear(); ridersLod[i].list.Clear(); }
            double clock = Clock;
            float t = (float)(clock % 3600.0);
            bool canHit = rider != null && rider.Running && !rider.Crashed && CableGrind.ChairsHit;   // (on a cable the board skims over the chairs' grips)
            var camNow = Camera.main;
            LiftRiders.Begin(camNow != null ? camNow.transform.position : p, p, rider != null);   // the people aboard (LiftRiders.cs)
            foreach (var lv in live)
            {
                var l = lv.lift;
                double moved = clock * LiftLine.Speed;
                for (int c = 0; c < l.chairs; c++)
                {
                    double along = c * (double)l.spacing + moved;
                    int lap = (int)System.Math.Floor(along / l.loop);
                    l.Loop((float)(along - lap * (double)l.loop), out Vector3 pos, out Vector3 travel, out bool up, out float since);
                    if (pos.z < p.z - behind || pos.z > p.z + FarRange) continue;
                    Vector3 flat = new Vector3(travel.x, 0f, travel.z);
                    if (flat.sqrMagnitude < 1e-6f) continue;
                    // it hangs straight down from its grip, swinging a little: its own slow sway, and a kick fore and aft
                    // coming off a tower's sheaves that dies away over the next twenty metres
                    float kick = since < 60f ? 3.2f * Mathf.Exp(-since / 9f) * Mathf.Sin(since * 0.42f) : 0f;
                    float pitch = 1.6f * Mathf.Sin(t * 1.3f + c * 1.7f) + kick;
                    float roll = 1.1f * Mathf.Sin(t * 1.05f + c * 2.9f);
                    if (since < 4f) pos.y += 0.035f * Mathf.Exp(-since / 1.2f) * Mathf.Sin(since * 5.2f);   // ticking over the wheels
                    var rot = Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(pitch, 0f, roll);
                    var m = Matrix4x4.TRS(pos, rot, Vector3.one);
                    float d2 = (pos - p).sqrMagnitude;
                    bool near = d2 < ChairNear * ChairNear;
                    float dc = Flat(pos, p);
                    Put(chair, chairLod, ChairNear, dc, m, 0f);
                    // (its bar: swung up near the stations, drawn with the near chair)
                    if (chairBar != null && (!Farview.On ? dc < ChairNear : dc < ChairNear + Hand)) chairBar.list.Add(m * BarLocal(LiftArrive.HeroBar >= 0f && l.id == LiftRiders.HeroLift && c == LiftRiders.HeroChair ? LiftArrive.HeroBar : BarUp(l, (float)(along - lap * (double)l.loop))) * chairBar.local);
                    // who is aboard: drawn when the chair left the bottom station, and gone at the top
                    if (LiftRiders.On) LiftRiders.Chair(l, c, lap, (float)(along - lap * (double)l.loop), m);
                    else
                    {
                        float h = Slope.Hash01(c + l.id * 977, lap, 151);
                        int who = up ? (h < 0.56f ? Mathf.Min(3, (int)(h / 0.56f * 4f)) : -1) : (h > 0.95f ? 2 : -1);
                        if (who >= 0) Put(riders[who], ridersLod[who], ChairNear, dc, m, 0f);
                    }
                    // jumped into: the seat, its frame and whoever is on it, as a box under the grip
                    if (canHit && d2 < 8f * 8f)
                    {
                        Vector3 q = Quaternion.Inverse(rot) * (p + Vector3.up * 0.75f - pos);
                        if (Mathf.Abs(q.x) < 1.4f && q.y > -4.5f && q.y < -0.9f && Mathf.Abs(q.z - 0.2f) < 1.1f)
                        {
                            canHit = false;
                            rider.Breakthrough(1f, 0f, 99f, 0f, "Chairlift!");   // a crash, unless a shield takes it; invincible, straight through
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- drawing

        void Draw()
        {
            foreach (var part in parts)
            {
                if (part.mesh == null || part.list.Count == 0) continue;
                var rp = new RenderParams(part.mat != null ? part.mat : paint) { shadowCastingMode = part.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                if (part.props != null && Farview.On) rp.matProps = part.props;
                for (int i = 0; i < part.list.Count; i += 1000)
                    Graphics.RenderMeshInstanced(rp, part.mesh, 0, part.list, Mathf.Min(1000, part.list.Count - i), i);
            }
            LiftRiders.Draw(Time.deltaTime);
        }

        /// <summary>What the lift asks the GPU for this frame: draw calls (instanced batches and cable spans) and triangles.</summary>
        public void Stats(out int draws, out long tris, out int instances)
        {
            draws = 0; tris = 0; instances = 0;
            foreach (var part in parts)
            {
                if (part.mesh == null || part.list.Count == 0) continue;
                draws += (part.list.Count + 999) / 1000; tris += (long)part.Tris * part.list.Count; instances += part.list.Count;
            }
            foreach (var lv in live)
            {
                foreach (var g in lv.span)
                    if (g != null) { draws++; tris += g.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3; }
            }
            if (LiftRiders.On) { draws += LiftRiders.DrawCalls; tris += LiftRiders.Triangles; instances += LiftRiders.MidShown + LiftRiders.FarShown; }
        }

        // ---------------------------------------------------------------- dusk and night

        DayNight dayNight;
        Light lamp;
        float glowShown = -1f, lampK, findClock;
        Vector3 lampAt;
        bool lampSet;

        void Glow(Vector3 p)
        {
            if (dayNight == null || sound == null)
            {
                // (both are made by the game after this is; looked for once a second until they are there)
                findClock -= Time.unscaledDeltaTime;
                if (findClock <= 0f)
                {
                    findClock = 1f;
                    if (dayNight == null) dayNight = Object.FindFirstObjectByType<DayNight>();
                    if (sound == null) sound = Object.FindFirstObjectByType<SoundMix>();
                }
            }
            float dark = dayNight != null ? dayNight.Darkness : 0f;
            float glow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.27f, 0.62f, dark));
            if (Mathf.Abs(glow - glowShown) > 0.01f)
            {
                glowShown = glow;
                paint.SetColor("_EmissionColor", Color.white * (2.6f * glow));
            }
            // one real light, at the nearest lamp ahead: a station's floodlights, or the work lamps under a crossarm
            bool any = false; Vector3 best = Vector3.zero; float bestD = 220f * 220f, power = 0f;
            if (glow > 0.02f)
                foreach (var lv in live)
                {
                    var l = lv.lift;
                    for (int i = -2; i < l.head.Length; i++)
                    {
                        Vector3 at = i == -2 ? l.top + Vector3.up * 4.4f + l.dir * 4.5f : i == -1 ? l.bottom + Vector3.up * 4.4f - l.dir * 4.5f : l.head[i] + l.Axis * 0.3f;
                        float d = (at - p).sqrMagnitude;
                        if (at.z < p.z - 25f || d >= bestD) continue;
                        bestD = d; best = at; power = i < 0 ? 26f : 70f; any = true;
                    }
                }
            // (it fades out, moves, and fades in again, rather than jumping from one lamp to the next)
            bool move = any && (!lampSet || (best - lampAt).sqrMagnitude > 1f);
            lampK = Mathf.MoveTowards(lampK, any && !move ? 1f : 0f, (TestClock >= 0.0 ? 1f : Time.deltaTime) * 3f);
            if (move && lampK <= 0.001f) { lampAt = best; lampSet = true; lampPower = power; lamp.transform.position = best; }
            lamp.intensity = lampPower * glow * lampK;
            lamp.enabled = lamp.intensity > 0.05f;
        }
        float lampPower;

        // ---------------------------------------------------------------- sound

        SoundMix sound;
        int clackTower = -1, clackUp, clackDown;
        LiftLine.Lift clackLift;

        /// <summary>A hum from the nearest point of the nearest line, and a clack each time a chair crosses the nearest tower.</summary>
        void Sound(Vector3 p)
        {
            if (sound == null) return;
            LiftLine.Lift nearest = null; float bestD = 130f * 130f; Vector3 at = p;
            foreach (var lv in live)
            {
                var l = lv.lift;
                float along = Mathf.Clamp((p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z, 0f, l.plan);
                Vector3 q = l.At(along, 0f, Mathf.Lerp(l.top.y, l.bottom.y, along / l.plan) + 10f);
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; nearest = l; at = q; }
            }
            sound.LiftHum(at, nearest != null && Time.timeScale > 0f ? 1f : 0f);
            if (nearest == null) { clackTower = -1; return; }
            int tower = -1; float td = 110f * 110f;
            for (int i = 0; i < nearest.head.Length; i++)
            {
                float d = (nearest.head[i] - p).sqrMagnitude;
                if (d < td) { td = d; tower = i; }
            }
            if (tower < 0) { clackTower = -1; return; }
            // how many chairs have crossed this tower so far, each way: when the count goes up, one just has
            double moved = Clock * LiftLine.Speed;
            int n = nearest.Spans;
            int up = (int)System.Math.Floor((moved - nearest.cum[n - tower]) / nearest.spacing);
            int down = (int)System.Math.Floor((moved - nearest.cum[n + 5 + tower]) / nearest.spacing);
            if (tower == clackTower && nearest == clackLift)
            {
                if (up != clackUp) sound.LiftClack(nearest.head[tower] + nearest.across * LiftLine.Half, 0.5f);
                if (down != clackDown) sound.LiftClack(nearest.head[tower] - nearest.across * LiftLine.Half, 0.4f);
            }
            clackTower = tower; clackLift = nearest; clackUp = up; clackDown = down;
        }
    }
}
