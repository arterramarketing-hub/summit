using System.Collections.Generic;
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
    ///     tower's sheaves. More than half the chairs going up carry one to four seated riders, in jackets of
    ///     eight colours, skis on the footrest or a board hanging; hardly any coming down do. Who sits where is
    ///     drawn afresh each time a chair leaves the bottom station.
    ///
    /// A first line's bottom station is, where the ground allows, the big one you ski through and jump off
    /// (LiftJumpStation: laid out in LiftJump.cs, ridden by CableGrind, built here once as one mesh).
    ///
    /// Solid: a tower's mast and pad, and each station, are colliders marked as obstacles, so riding into one off
    /// the piste is a crash like a tree ("Lift tower", "Lift station"). A station is solid where it is drawn: its
    /// deck and legs, not the air under its deck; a jump station's podium, walls, railings, posts and masts, not its
    /// lane, which the rider is carried along. A chair is solid too: a jump high enough to
    /// reach one is a crash ("Chairlift!", or a shield spent), found by a simple test against the few nearest
    /// chairs. The cable never knocks anyone down: it is there to be ground (CableGrind.cs), from the launch ramp
    /// that stands under each lift (LiftRamp.cs), and while a rider is on it the chairs skim under the board and the
    /// rope dips and bounces with them. Nothing hangs within 9 m of the piste's snow (LiftLine).
    ///
    /// At dusk and night the work lamps under each crossarm, the red beacon on each tower and the stations'
    /// windows and floodlights glow (the palette's glow picture, brought up with DayNight.Darkness), and one real
    /// light sits at whichever lamp is nearest the rider.
    /// </summary>
    public class SkiLift : MonoBehaviour
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
            s.Build(lit);
            return s;
        }

        /// <summary>For the render tools (Assets/Editor/LiftShot.cs): the chairs' clock, in seconds, when it is not the game's own.</summary>
        public static double TestClock = -1.0;
        static double Clock => TestClock >= 0.0 ? TestClock : Time.timeAsDouble;

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
            public readonly List<Matrix4x4> list = new List<Matrix4x4>();
            public int Tris => mesh != null ? (int)(mesh.GetIndexCount(0) / 3) : 0;
        }
        Part mast, mastLod, head, headLod, pad, sheaves, sheavesLod, chair, chairLod, station;
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
            for (int i = 0; i < 4; i++)
            {
                riders[i] = P("Riders" + (char)('A' + i), false);
                ridersLod[i] = P("Riders" + (char)('A' + i) + "Lod", false);
            }
            if (chair.mesh == null || mast.mesh == null) return;

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

            var urp = Shader.Find("Universal Render Pipeline/Lit");
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
            rampMat = new Material(paint) { name = "LiftRamp" };   // (the palette again, seen from both sides: no winding to get wrong)
            rampMat.SetFloat("_Cull", 0f);
            litShader = lit;
            // the rider's own sphere never touches a launch ramp's collider (it is carried over the ramp by CableGrind)
            if (target != null && target.TryGetComponent<Collider>(out var own)) own.excludeLayers |= 1 << CourseDressing.IceLayer;
            CableGrind.Hook(rider);
            CableGrind.ChairTick = at => { if (sound != null) sound.LiftClack(at, 0.6f); };

            var lg = new GameObject("LiftLamp");
            lg.transform.SetParent(transform, false);
            lamp = lg.AddComponent<Light>();
            lamp.type = LightType.Point; lamp.color = new Color(1f, 0.9f, 0.74f); lamp.range = 34f; lamp.shadows = LightShadows.None;
            lamp.enabled = false;
            ready = true;
            if (System.Environment.GetEnvironmentVariable("SUMMIT_LIFTCHECK") == "1") Debug.Log("SkiLift\n" + LiftLine.Report() + LiftLine.RampReport() + LiftLine.JumpReport());
        }

        // ---------------------------------------------------------------- what is built near the rider

        class Live
        {
            public LiftLine.Lift lift;
            public GameObject root;
            public GameObject[] span, tower;        // each span's cable; each tower's collider
            public GameObject stationTop, stationBottom;
            public Mesh station;                    // a station with a lane through it: what is drawn of it
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
                Refresh(p);
            }
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
            refreshClock = 0f;
            clackTower = -1;
            bent = null;
            CableGrind.Reset();
        }

        void Free(Live lv)
        {
            if (lv.root == null) return;
            foreach (var mf in lv.root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Lift")) Destroy(mf.sharedMesh);   // the cables' and the ramp's meshes are ours (the sign's are Unity's)
            foreach (var mc in lv.root.GetComponentsInChildren<MeshCollider>(true)) if (mc.sharedMesh != null && mc.sharedMesh.name.StartsWith("LiftJump")) Destroy(mc.sharedMesh);
            Destroy(lv.root);
            lv.root = null;
        }

        bool InRange(float zMin, float zMax, float z) => zMax > z - behind && zMin < z + FarRange;

        void Refresh(Vector3 p)
        {
            var lifts = LiftLine.Lifts;
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
                var made = new Live { lift = l, root = root, span = new GameObject[l.Spans], tower = new GameObject[l.s.Length] };
                live.Add(made);
                Ramp(made);
                JumpStation(made);
            }

            foreach (var part in parts) if (part != chair && part != chairLod && System.Array.IndexOf(riders, part) < 0 && System.Array.IndexOf(ridersLod, part) < 0) part.list.Clear();
            foreach (var lv in live)
            {
                var l = lv.lift;
                Vector3 axis = l.Axis, fwd = l.dir * Mathf.Cos(l.lean) - Vector3.up * Mathf.Sin(l.lean);
                // the cable, span by span
                for (int i = 0; i < l.Spans; i++)
                {
                    float za = l.top.z + l.dir.z * l.nodeS[i], zb = l.top.z + l.dir.z * l.nodeS[i + 1];
                    bool want = InRange(Mathf.Min(za, zb), Mathf.Max(za, zb), p.z);
                    if (want && lv.span[i] == null) lv.span[i] = Cable(lv, i);
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
                    if (solid && lv.tower[i] == null) lv.tower[i] = TowerCollider(lv, i, axis, fwd);
                    else if (!solid && lv.tower[i] != null) { Destroy(lv.tower[i]); lv.tower[i] = null; }
                    // (an invincible rider switches off what it rides through: back on once it has gone by)
                    if (lv.tower[i] != null && d2 > 8f * 8f) foreach (var c in lv.tower[i].GetComponents<Collider>()) if (!c.enabled) c.enabled = true;
                    if (!drawn) continue;
                    bool near = d2 < NearRange * NearRange;
                    var mastM = Frame(f - axis * 1.5f, l.across, axis * (l.h[i] + 1.5f), fwd);
                    (near ? mast : mastLod).list.Add(mastM * (near ? mast : mastLod).local);
                    (near ? head : headLod).list.Add(Frame(h, l.across, axis, fwd) * (near ? head : headLod).local);
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
                    var train = d2 < TrainNear * TrainNear ? sheaves : sheavesLod;
                    for (int side = -1; side <= 1; side += 2)
                        train.list.Add(Frame(h + l.across * (side * LiftLine.Half), l.across * side, n * k, t * (side * k)) * train.local);
                }
                // the stations
                bool topIn = InRange(l.top.z - 8f, l.top.z + 8f, p.z), bottomIn = InRange(l.bottom.z - 8f, l.bottom.z + 8f, p.z);
                if (topIn) station.list.Add(Frame(l.top, l.across, Vector3.up, l.dir) * station.local);
                if (bottomIn) station.list.Add(Frame(l.bottom, -l.across, Vector3.up, -l.dir) * station.local);
                Stream(ref lv.stationTop, topIn && (l.top - p).sqrMagnitude < 240f * 240f, lv, l.top, l.dir);
                // (a station with a lane through it is solid from the moment its lift is in range: JumpStation)
                Stream(ref lv.stationBottom, l.jump == null && bottomIn && (l.bottom - p).sqrMagnitude < 240f * 240f, lv, l.bottom, -l.dir);
            }
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

        // ---------------------------------------------------------------- the launch ramp (LiftRamp.cs)

        static Vector2 Paint(int cell) => new Vector2((cell % 8 + 0.5f) / 8f, 1f - (cell / 8 + 0.5f) / 8f);   // a colour of LiftPaint.png (Tools/MakeLift.py's palette)

        /// <summary>
        /// A lift's launch ramp: a steel kicker standing under one cable, its sides
        /// running down into the snow; and a jump sign at the piste's edge where the cleared lane to it starts.
        /// The rider is carried over it by CableGrind, so its collider is only for a thrown body and the board.
        /// </summary>
        void Ramp(Live lv)
        {
            var l = lv.lift; var r = l.ramp;
            if (r == null) return;
            const int N = 8;
            const int Deck = 7, Steel = 0, Accent = 6, Dark = 1;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Vector3 origin = LiftLine.RampPoint(l, r, 0f);
            System.Action<Vector3, Vector3, Vector3, Vector3, int> Quad = (a, b, c, d, cell) =>
            {
                int n = v.Count;
                v.Add(a - origin); v.Add(b - origin); v.Add(c - origin); v.Add(d - origin);
                for (int q = 0; q < 4; q++) uv.Add(Paint(cell));
                tri.Add(n); tri.Add(n + 1); tri.Add(n + 2); tri.Add(n); tri.Add(n + 2); tri.Add(n + 3);
            };
            Vector3 w = l.across * CableTune.RampHalfWidth;
            for (int k = 0; k < N; k++)
            {
                Vector3 p0 = LiftLine.RampPoint(l, r, r.length * k / N), p1 = LiftLine.RampPoint(l, r, r.length * (k + 1f) / N);
                // (the sides go down to a metre under the snow, wherever the snow is)
                Vector3 g0 = new Vector3(0f, Mathf.Min(p0.y, Slope.Height(p0.x, p0.z)) - 1f - p0.y, 0f), g1 = new Vector3(0f, Mathf.Min(p1.y, Slope.Height(p1.x, p1.z)) - 1f - p1.y, 0f);
                Vector3 rim = Vector3.down * 0.18f;
                Quad(p0 - w, p0 + w, p1 + w, p1 - w, Deck);                                   // the top: steel plate, like a park box
                for (int e = -1; e <= 1; e += 2)
                {
                    Quad(p0 + w * e, p1 + w * e, p1 + w * e + rim, p0 + w * e + rim, k % 2 == 0 ? Accent : Dark);   // a striped rim
                    Quad(p0 + w * e + rim, p1 + w * e + rim, p1 + w * e + g1, p0 + w * e + g0, Steel);              // the side
                }
                if (k == N - 1) Quad(p1 - w, p1 + w, p1 + w + g1, p1 - w + g1, Steel);        // the back, under the lip
            }
            var mesh = new Mesh { name = "LiftRamp" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("LaunchRamp") { layer = CourseDressing.IceLayer };
            go.transform.SetParent(lv.root.transform, false);
            go.transform.position = origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = rampMat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            // the sign: a board on a post, facing up the hill
            if (signMat == null) signMat = CourseDressing.Sign(litShader, "Signs/SignJump");
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(post.GetComponent<Collider>());
            post.name = "LiftRampSignPost";
            post.transform.SetParent(lv.root.transform, false);
            post.transform.position = r.sign + Vector3.up * 1.2f;
            post.transform.localScale = new Vector3(0.12f, 3.4f, 0.12f);
            post.GetComponent<MeshRenderer>().sharedMaterial = postMat;
            var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(board.GetComponent<Collider>());
            board.name = "LiftRampSign";
            board.transform.SetParent(lv.root.transform, false);
            // (a quad shows its face to whoever looks along its own +z: turned to face the rider coming down the lane)
            Vector3 lane = LiftLine.RampPoint(l, r, 0f) - r.laneFrom; lane.y = 0f;
            board.transform.SetPositionAndRotation(r.sign + Vector3.up * 2.4f - lane.normalized * 0.08f, Quaternion.LookRotation(lane.normalized, Vector3.up));
            board.transform.localScale = new Vector3(1.7f, 1.7f, 1f);
            board.GetComponent<MeshRenderer>().sharedMaterial = signMat;
        }
        Material rampMat, signMat;
        Shader litShader;

        // ---------------------------------------------------------------- the station you ski through (LiftJump.cs)

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

        /// <summary>
        /// A bottom station with a lane through it and a jump off its far end: the terminal is the plain station's
        /// (drawn with the others, standing on its deck), and round it this builds what makes it big and ridable.
        /// The lane on the side toward the piste, its top leaving the snow up the hill, level past the terminal and
        /// up into a lip; the deck carried on past the bullwheel as a railed plaza; the walls that close all of it
        /// in down to the snow (a podium: nothing is open underneath, so what is solid is what is drawn); the
        /// railings, a gantry over the lane with a jump sign and lamps, two light masts at the lip.
        /// Solid: everything but the lane's top, which the rider is carried along (CableGrind) and only a thrown body
        /// touches. Built once when the lift comes into range, in the lift's one material: one more draw call.
        /// </summary>
        void JumpStation(Live lv)
        {
            var l = lv.lift; var j = l.jump;
            if (j == null) return;
            const int Steel = 0, Dark = 1, Pad = 3, Accent = 6, Deck = 7, Lamp = 9, Beacon = 10, Snow = 19;
            const float In = LiftLine.JumpLaneIn, Out = LiftLine.JumpLaneOut, Mid = LiftLine.JumpLaneMid;
            const float SolidUnder = 0.4f;
            const float Far = -LiftLine.StationHalfWid, Back = -LiftLine.StationHalfLen, Front = LiftLine.StationHalfLen, Edge = LiftLine.StationHalfWid;
            float sd = j.side, yS = l.bottom.y, end = j.end;
            Vector3 up = Vector3.up, dir = l.dir, toPiste = l.across * sd;
            var draw = new Shape { origin = l.bottom }; var lane = new Shape { origin = l.bottom }; var solid = new Shape { origin = l.bottom };
            System.Func<float, float, float, Vector3> P = (t, c, y) => l.At(l.plan + t, sd * c, y);
            System.Func<float, float, float> SnowAt = (t, c) => { Vector3 q = l.At(l.plan + t, sd * c, 0f); return Slope.DrawnHeight(q.x, q.z); };
            System.Func<float, float, float> Top = (t, c) => LiftLine.JumpTop(l, t, sd * c);

            // ---- the lane's top, from the first row any of it stands clear of the snow
            float t0 = end;
            foreach (float c in new[] { In, Mid, Out }) t0 = Mathf.Min(t0, LiftLine.JumpStart(l, sd * c));
            t0 = Mathf.Floor(t0) - 1f;
            int rows = Mathf.CeilToInt(end - t0);
            float[] cols = { In, In + 0.45f, In + 2.6f, Mid, Out - 2.6f, Out - 0.45f, Out };
            var rowT = new float[rows + 1];
            for (int r = 0; r <= rows; r++) rowT[r] = Mathf.Lerp(t0, end, r / (float)rows);
            for (int r = 0; r < rows; r++)
                for (int q = 0; q < cols.Length - 1; q++)
                {
                    float ta = rowT[r], tb = rowT[r + 1], ca = cols[q], cb = cols[q + 1];
                    float h00 = Top(ta, ca), h01 = Top(ta, cb), h10 = Top(tb, ca), h11 = Top(tb, cb);
                    float o00 = h00 - SnowAt(ta, ca), o01 = h01 - SnowAt(ta, cb), o10 = h10 - SnowAt(tb, ca), o11 = h11 - SnowAt(tb, cb);
                    if (Mathf.Max(Mathf.Max(o00, o01), Mathf.Max(o10, o11)) < 0.02f) continue;   // (still the snow itself)
                    // (where a corner is the snow it is tucked just under it, so the two never flicker)
                    const float tuck = 0.1f;
                    Vector3 a = P(ta, ca, h00 - (o00 < 0.02f ? tuck : 0f)), b = P(ta, cb, h01 - (o01 < 0.02f ? tuck : 0f));
                    Vector3 c2 = P(tb, cb, h11 - (o11 < 0.02f ? tuck : 0f)), d = P(tb, ca, h10 - (o10 < 0.02f ? tuck : 0f));
                    int cell = q == 0 || q == cols.Length - 2 ? Pad : tb > end - 0.95f ? Accent : Snow;   // orange kerbs, a red line on the lip
                    draw.Quad(a, b, c2, d, cell, up); lane.Quad(a, b, c2, d, cell, up);
                }

            // ---- walls, from a top edge down into the snow: an orange rim, a dark line under it, galvanised steel
            bool rib = true;   // (a post at this panel's first edge: not at every metre of the lane's sides)
            System.Action<Vector3, Vector3, float, float, Vector3, bool> Wall = (a, b, ga, gb, outward, rim) =>
            {
                // a, b: the top edge's ends; ga, gb: the snow under them
                if (a.y - ga < 0.02f && b.y - gb < 0.02f) return;
                Vector3 a0 = new Vector3(a.x, Mathf.Min(a.y, ga) - 1f, a.z), b0 = new Vector3(b.x, Mathf.Min(b.y, gb) - 1f, b.z);
                // (solid to a board's thickness under its top edge: the board is carried over the lip with its
                // underside on that edge, and must not catch it)
                if (a.y - ga > SolidUnder || b.y - gb > SolidUnder) solid.Quad(a + Vector3.down * SolidUnder, b + Vector3.down * SolidUnder, b0, a0, Steel, outward);
                if (!rim) { draw.Quad(a, b, b0, a0, Deck, outward); return; }
                Vector3 r1 = Vector3.down * 0.45f, r2 = Vector3.down * 0.6f;
                draw.Quad(a, b, b + r1, a + r1, Pad, outward);
                draw.Quad(a + r1, b + r1, b + r2, a + r2, Dark, outward);
                // grey cladding between galvanised posts, a waler across it every few metres down: a steel frame, not a slab
                draw.Quad(a + r2, b + r2, b0, a0, Deck, outward);
                Vector3 proud = outward * 0.07f;
                if (rib && a.y - ga > 1.2f) draw.Bar(a + r2 + proud, a0 + proud, 0.26f, 0.14f, Steel, false);
                for (float down = 3.4f; down < Mathf.Max(a.y - ga, b.y - gb) - 0.6f; down += 3.2f)
                {
                    // (stopped where it would run into the snow)
                    Vector3 wa = a + Vector3.down * down + proud, wb = b + Vector3.down * down + proud;
                    if (wa.y < ga + 0.3f && wb.y < gb + 0.3f) break;
                    if (wa.y < ga + 0.3f) wa = Vector3.Lerp(wb, wa, Mathf.InverseLerp(b.y - gb, a.y - ga, down + 0.3f));
                    if (wb.y < gb + 0.3f) wb = Vector3.Lerp(wa, wb, Mathf.InverseLerp(a.y - ga, b.y - gb, down + 0.3f));
                    draw.Bar(wa, wb, 0.1f, 0.2f, Steel, false);
                }
            };
            // the lane's outer side, and its inner side above the terminal
            for (int r = 0; r < rows; r++)
            {
                float ta = rowT[r], tb = rowT[r + 1];
                rib = (rows - r) % 3 == 0;
                Wall(P(ta, Out, Top(ta, Out)), P(tb, Out, Top(tb, Out)), SnowAt(ta, Out), SnowAt(tb, Out), toPiste, true);
                if (tb <= Back + 0.01f) Wall(P(ta, In, Top(ta, In)), P(tb, In, Top(tb, In)), SnowAt(ta, In), SnowAt(tb, In), -toPiste, true);
                else
                {
                    // beside the terminal and the plaza the lane and the deck share this wall: whichever stands higher shows its side
                    float ha = Top(ta, In), hb = Top(tb, In);
                    if (yS > ha + 0.02f || yS > hb + 0.02f) Wall(P(ta, In, yS), P(tb, In, yS), Mathf.Min(ha, yS), Mathf.Min(hb, yS), toPiste, true);
                    if (ha > yS + 0.02f || hb > yS + 0.02f) Wall(P(ta, In, ha), P(tb, In, hb), Mathf.Min(ha, yS), Mathf.Min(hb, yS), -toPiste, true);
                }
            }
            // the lane's front, under the lip
            for (int q = 0; q < cols.Length - 1; q++, rib = q != 1)
                Wall(P(end, cols[q], Top(end, cols[q])), P(end, cols[q + 1], Top(end, cols[q + 1])), SnowAt(end, cols[q]), SnowAt(end, cols[q + 1]), dir, true);
            rib = true;
            // the podium under the terminal and the plaza: its front, its far side, its uphill face
            const int Seg = 4;
            for (int q = 0; q < Seg; q++)
            {
                float ca = Mathf.Lerp(Far, In, q / (float)Seg), cb = Mathf.Lerp(Far, In, (q + 1f) / Seg);
                Wall(P(end, ca, yS), P(end, cb, yS), SnowAt(end, ca), SnowAt(end, cb), dir, true);
                Wall(P(Back, ca, yS), P(Back, cb, yS), SnowAt(Back, ca), SnowAt(Back, cb), -dir, false);
            }
            int along = Mathf.CeilToInt((end - Back) / 2.5f);
            for (int q = 0; q < along; q++)
            {
                float ta = Mathf.Lerp(Back, end, q / (float)along), tb = Mathf.Lerp(Back, end, (q + 1f) / along);
                Wall(P(ta, Far, yS), P(tb, Far, yS), SnowAt(ta, Far), SnowAt(tb, Far), -toPiste, true);
            }

            // ---- the deck: the plaza past the bullwheel, the strip between the terminal's own deck and the lane;
            // and, for whatever comes down on it, the terminal's own deck (drawn with the terminal)
            draw.Quad(P(Front, Far, yS), P(Front, Edge, yS), P(end, Edge, yS), P(end, Far, yS), Deck, up);
            draw.Quad(P(Back, Edge, yS), P(Back, In, yS), P(end, In, yS), P(end, Edge, yS), Deck, up);
            solid.Quad(P(Back, Far, yS), P(Back, In, yS), P(end, In, yS), P(end, Far, yS), Deck, up);

            // ---- railings: posts, two rails, and for the board a thin wall the height of the rail
            System.Action<float, float, float, float, System.Func<float, float>, bool> Rail = (ta, ca, tb, cb, yAt, padded) =>
            {
                float len = Mathf.Sqrt((tb - ta) * (tb - ta) + (cb - ca) * (cb - ca));
                if (len < 0.4f) return;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 2.5f));
                Vector3 last = Vector3.zero;
                for (int i = 0; i <= n; i++)
                {
                    float u = i / (float)n, t = Mathf.Lerp(ta, tb, u), c = Mathf.Lerp(ca, cb, u);
                    Vector3 foot = P(t, c, yAt(u));
                    if (i == 0 && padded) draw.Bar(foot - up * 0.3f, foot + up * 1.25f, 0.36f, 0.36f, Pad);   // its uphill end, padded like a tower's foot
                    else draw.Bar(foot - up * 0.1f, foot + up * 1.1f, 0.08f, 0.08f, Steel, false);
                    if (i > 0)
                    {
                        draw.Bar(last + up * 1.1f, foot + up * 1.1f, 0.07f, 0.07f, Steel, false);
                        draw.Bar(last + up * 0.58f, foot + up * 0.58f, 0.05f, 0.05f, Steel, false);
                        solid.Bar(last + up * 0.5f, foot + up * 0.5f, 0.12f, 1.3f, Steel);
                    }
                    last = foot;
                }
            };
            // the lane's outer edge, from where its top stands a step over the snow; its inner edge above the terminal likewise
            float railOut = end, railIn = Back;
            for (int r = rows; r >= 0; r--) if (Top(rowT[r], Out) - SnowAt(rowT[r], Out) > 0.9f) railOut = rowT[r];
            for (int r = rows; r >= 0; r--) if (rowT[r] < Back && Top(rowT[r], In) - SnowAt(rowT[r], In) > 0.9f) railIn = rowT[r];
            float e0 = railOut, e1 = end - 0.25f;
            Rail(e0, Out + 0.05f, e1, Out + 0.05f, u => Top(Mathf.Lerp(e0, e1, u), Out), true);
            if (railIn < Back - 0.5f) { float i0 = railIn; Rail(i0, In - 0.05f, Back, In - 0.05f, u => Mathf.Lerp(Top(i0, In), Mathf.Max(yS, Top(Back, In)), u), true); }
            // between the plaza and the lane, the plaza's far side and its front
            Rail(Front, In - 0.05f, e1, In - 0.05f, u => Mathf.Max(yS, Top(Mathf.Lerp(Front, e1, u), In)), false);
            Rail(Front, Far + 0.1f, end - 0.1f, Far + 0.1f, u => yS, false);
            Rail(end - 0.1f, Far + 0.1f, end - 0.1f, In - 0.05f, u => yS, false);
            // (the terminal's own railings, drawn with it: solid too)
            foreach (float c in new[] { -(Edge - 0.1f), Edge - 0.1f })
                solid.Bar(P(Back + 0.1f, c, yS + 0.55f), P(Front - 0.1f, c, yS + 0.55f), 0.12f, 1.1f, Steel);

            // ---- the gantry over the lane's mouth: two posts and a beam, a banner with the jump sign, lamps
            float tg = Mathf.Clamp(j.start * 0.5f, Back, -1.5f);
            float gIn = Mathf.Max(Top(tg, In), tg >= Back ? yS : float.MinValue), gOut = Top(tg, Out);
            float beam = Mathf.Max(gIn, Top(tg, Mid)) + 5.7f;
            Vector3 pIn = P(tg, In - 0.05f, gIn), pOut = P(tg, Out + 0.05f, gOut);
            Vector3 bIn = new Vector3(pIn.x, beam, pIn.z), bOut = new Vector3(pOut.x, beam, pOut.z);
            foreach (var (foot, head) in new[] { (pIn, bIn), (pOut, bOut) })
            {
                draw.Bar(foot - up * 0.5f, head, 0.3f, 0.3f, Steel, false); solid.Bar(foot - up * 0.5f, head, 0.3f, 0.3f, Steel, false);
                draw.Bar(foot - up * 0.5f, foot + up * 1.5f, 0.62f, 0.62f, Pad);   // padded, like a tower's foot
                solid.Bar(foot - up * 0.5f, foot + up * 1.5f, 0.62f, 0.62f, Pad);
            }
            draw.Bar(bIn - toPiste * 0.3f, bOut + toPiste * 0.3f, 0.34f, 0.42f, Steel);
            draw.Bar(bIn + up * -0.75f, bOut + up * -0.75f, 0.1f, 1.0f, Pad);              // the banner
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = Vector3.Lerp(bIn, bOut, 0.2f + 0.3f * i) + dir * 0.35f - up * 0.3f;
                draw.Bar(at - toPiste * 0.35f, at + toPiste * 0.35f, 0.3f, 0.12f, Dark);
                draw.Bar(at - toPiste * 0.3f - up * 0.07f, at + toPiste * 0.3f - up * 0.07f, 0.24f, 0.03f, Lamp);
            }
            // ---- a light mast either side of the lip, a red light on top
            foreach (float c in new[] { In - 0.05f, Out + 0.05f })
            {
                float tm = end - 0.3f, y = c < Mid ? Mathf.Max(yS, Top(tm, In)) : Top(tm, Out);
                Vector3 foot = P(tm, c, y), head = foot + up * 4.6f;
                draw.Bar(foot, head, 0.16f, 0.16f, Steel, false); solid.Bar(foot, head, 0.16f, 0.16f, Steel, false);
                draw.Bar(head - up * 0.25f + dir * 0.1f, head - up * 0.25f + dir * 0.55f, 0.5f, 0.16f, Dark);
                draw.Bar(head - up * 0.34f + dir * 0.14f, head - up * 0.34f + dir * 0.51f, 0.42f, 0.03f, Lamp);
                draw.Bar(head, head + up * 0.22f, 0.16f, 0.16f, Beacon);
            }

            var go = new GameObject("LiftJumpStation");
            go.transform.SetParent(lv.root.transform, false);
            go.transform.position = l.bottom;
            lv.station = draw.Make("LiftJumpStation");
            go.AddComponent<MeshFilter>().sharedMesh = lv.station;
            go.AddComponent<MeshRenderer>().sharedMaterial = paint;
            go.AddComponent<MeshCollider>().sharedMesh = solid.Make("LiftJumpSolid");
            go.AddComponent<Obstacle>().label = "Lift station";
            // the terminal overhead on its column, and the hut
            var term = new GameObject("LiftTerminal");
            term.transform.SetParent(go.transform, false);
            term.transform.SetPositionAndRotation(l.bottom, Quaternion.LookRotation(-l.dir, Vector3.up));
            var box = term.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, LiftLine.StationCable + 1.6f, -0.15f); box.size = new Vector3(LiftLine.Gauge + 2.3f, 3.1f, 11.6f);
            box = term.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 2.5f, -0.2f); box.size = new Vector3(0.9f, 5f, 1.1f);
            box = term.AddComponent<BoxCollider>();
            box.center = new Vector3(LiftLine.StationHalfWid - 0.85f, 1.3f, 3.2f); box.size = new Vector3(1.6f, 2.6f, 2.5f);
            // and the podium is solid right through, not a shell: nothing gets inside it, and nobody gets up inside it
            // after a fall (Rider.Recover looks for somewhere that is not inside anything solid). Blocks under the deck
            // and under each few metres of the lane, their tops a board's thickness under the top that is drawn
            var core = new GameObject("LiftPodium");
            core.transform.SetParent(go.transform, false);
            core.transform.SetPositionAndRotation(l.bottom, Quaternion.LookRotation(l.dir, Vector3.up));   // x: across, z: down the line
            System.Action<float, float, float, float, float, float> Block = (ta, tb, ca, cb, yTop, yBottom) =>
            {
                if (yTop - yBottom < 0.3f) return;
                var bc = core.AddComponent<BoxCollider>();
                bc.center = new Vector3(sd * (ca + cb) * 0.5f, (yTop + yBottom) * 0.5f - l.bottom.y, (ta + tb) * 0.5f);
                bc.size = new Vector3(Mathf.Abs(cb - ca), yTop - yBottom, tb - ta);
            };
            System.Func<float, float, float, float, float> Lowest = (ta, tb, ca, cb) =>
                Mathf.Min(Mathf.Min(SnowAt(ta, ca), SnowAt(ta, cb)), Mathf.Min(SnowAt(tb, ca), SnowAt(tb, cb)));
            Block(Back, end, Far, In, yS - SolidUnder, Lowest(Back, end, Far, In) - 1f);
            for (int r = 0; r < rows; r += 3)
            {
                float ta = rowT[r], tb = rowT[Mathf.Min(r + 3, rows)];
                float lowTop = float.MaxValue;
                for (int k = r; k <= Mathf.Min(r + 3, rows); k++) foreach (float c in cols) lowTop = Mathf.Min(lowTop, Top(rowT[k], c));
                Block(ta, tb, In, Out, lowTop - SolidUnder, Lowest(ta, tb, In, Out) - 1f);
            }

            // the lane's top: for a thrown body (the rider's own sphere is carried along it and never touches it)
            // (not under the station's Obstacle: like a launch ramp, the lane is ridden, never hit)
            var top = new GameObject("LiftJumpDeck") { layer = CourseDressing.IceLayer };
            top.transform.SetParent(lv.root.transform, false);
            top.transform.position = l.bottom;
            top.AddComponent<MeshCollider>().sharedMesh = lane.Make("LiftJumpLane");

            // the jump sign: on the gantry's banner, facing up the hill, and on a post at the piste's edge where the lane to it starts
            if (signMat == null) signMat = CourseDressing.Sign(litShader, "Signs/SignJump");
            var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(board.GetComponent<Collider>());
            board.name = "LiftJumpSign";
            board.transform.SetParent(go.transform, false);
            board.transform.SetPositionAndRotation((bIn + bOut) * 0.5f - up * 0.75f - dir * 0.08f, Quaternion.LookRotation(dir, Vector3.up));
            board.transform.localScale = new Vector3(1.9f, 1.9f, 1f);
            board.GetComponent<MeshRenderer>().sharedMaterial = signMat;
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(post.GetComponent<Collider>());
            post.name = "LiftJumpSignPost";
            post.transform.SetParent(go.transform, false);
            post.transform.position = j.sign + Vector3.up * 1.2f;
            post.transform.localScale = new Vector3(0.12f, 3.4f, 0.12f);
            post.GetComponent<MeshRenderer>().sharedMaterial = postMat;
            var edge = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(edge.GetComponent<Collider>());
            edge.name = "LiftJumpLaneSign";
            edge.transform.SetParent(go.transform, false);
            Vector3 way = P(j.start - 6f, Mid, 0f) - j.laneFrom; way.y = 0f;
            edge.transform.SetPositionAndRotation(j.sign + Vector3.up * 2.4f - way.normalized * 0.08f, Quaternion.LookRotation(way.normalized, Vector3.up));
            edge.transform.localScale = new Vector3(1.7f, 1.7f, 1f);
            edge.GetComponent<MeshRenderer>().sharedMaterial = signMat;
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

        void Stream(ref GameObject go, bool want, Live lv, Vector3 deck, Vector3 outward)
        {
            if (want && go == null)
            {
                go = new GameObject("LiftStation");
                go.transform.SetParent(lv.root.transform, false);
                go.transform.SetPositionAndRotation(deck, Quaternion.LookRotation(outward, Vector3.up));
                // solid where it is drawn: the deck with its frame (and a rider's height under it: whoever rides in under
                // the deck where it is that low meets its edge), the six legs that stand under it, down into the snow
                // (between them, where the deck stands high over falling ground, there is nothing and you can ride
                // through); the terminal overhead on its column; the hut
                const float legs = 14f, W = LiftLine.StationHalfWid, L = LiftLine.StationHalfLen;
                var b = go.AddComponent<BoxCollider>();
                b.center = new Vector3(0f, -0.7f, 0f); b.size = new Vector3(2f * W, 1.4f, 2f * L);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz++)
                    {
                        b = go.AddComponent<BoxCollider>();
                        b.center = new Vector3(sx * (W - 0.5f), -legs * 0.5f - 0.3f, sz * (L - 0.5f)); b.size = new Vector3(0.5f, legs, 0.5f);
                    }
                b = go.AddComponent<BoxCollider>();
                b.center = new Vector3(0f, LiftLine.StationCable + 1.6f, -0.15f); b.size = new Vector3(LiftLine.Gauge + 2.3f, 3.1f, 11.6f);
                b = go.AddComponent<BoxCollider>();
                b.center = new Vector3(0f, 2.5f, -0.2f); b.size = new Vector3(0.9f, 5f, 1.1f);
                b = go.AddComponent<BoxCollider>();
                b.center = new Vector3(LiftLine.StationHalfWid - 0.85f, 1.3f, 3.2f); b.size = new Vector3(1.6f, 2.6f, 2.5f);
                go.AddComponent<Obstacle>().label = "Lift station";
            }
            else if (!want && go != null) { Destroy(go); go = null; }
            else if (go != null) foreach (var c in go.GetComponents<Collider>()) if (!c.enabled && (deck - target.position).sqrMagnitude > 20f * 20f) c.enabled = true;
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
                    (near ? chair : chairLod).list.Add(m * (near ? chair : chairLod).local);
                    // who is aboard: drawn when the chair left the bottom station, and gone at the top
                    float h = Slope.Hash01(c + l.id * 977, lap, 151);
                    int who = up ? (h < 0.56f ? Mathf.Min(3, (int)(h / 0.56f * 4f)) : -1) : (h > 0.95f ? 2 : -1);
                    if (who >= 0) { var r = near ? riders[who] : ridersLod[who]; r.list.Add(m * r.local); }
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
                var rp = new RenderParams(paint) { shadowCastingMode = part.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                for (int i = 0; i < part.list.Count; i += 1000)
                    Graphics.RenderMeshInstanced(rp, part.mesh, 0, part.list, Mathf.Min(1000, part.list.Count - i), i);
            }
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
                // a station with a lane through it, and its two signs
                if (lv.station != null) { draws += 3; tris += lv.station.GetIndexCount(0) / 3 + 4; }
            }
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
