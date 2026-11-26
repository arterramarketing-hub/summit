using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Checks on the mountain's people.
///   Chairs: every lift pose (skis or board, hands on the lap or the bar) at every body size, on every seat, posed
///   as the lifts pose them, its full mesh tested against the chair's solid parts (Tools/MakeLift.py's seat, back,
///   bar, footrests, frame): how many vertices go into any of them and how deep, and how far anyone reaches into
///   the next seat. Batch: -executeMethod PeopleTest.Chairs (no graphics needed). Exit 1 on any clipping.
/// </summary>
public static partial class PeopleTest
{
    // ---------------------------------------------------------------- the chair's solid parts (its own frame)

    struct Tube { public Vector3 a, b; public float r; public string name; }
    struct Box { public Vector3 lo, hi; public string name; }
    static readonly List<Tube> tubes = new List<Tube>();
    static readonly List<Box> boxes = new List<Box>();

    static void ChairParts()
    {
        tubes.Clear(); boxes.Clear();
        const float SEAT = 2.9f, CW = 1.1f, BAIL = -1.75f, BACKZ = -0.27f, BARZ = 0.5f;
        void T(string n, Vector3 a, Vector3 b, float r) => tubes.Add(new Tube { a = a, b = b, r = r, name = n });
        boxes.Add(new Box { name = "seat", lo = new Vector3(-1.07f, -SEAT - 0.01f, -0.25f), hi = new Vector3(1.07f, -SEAT + 0.05f, 0.25f) });
        foreach (var y in new[] { 0.2f, 0.47f })
        {
            float zc = BACKZ + (0.36f - y) * 0.12f;
            boxes.Add(new Box { name = "back", lo = new Vector3(-1.07f, -SEAT + y - 0.1f, zc - 0.037f), hi = new Vector3(1.07f, -SEAT + y + 0.1f, zc + 0.037f) });
        }
        T("backrail", new Vector3(-CW, -SEAT + 0.64f, BACKZ - 0.04f), new Vector3(CW, -SEAT + 0.64f, BACKZ - 0.04f), 0.026f);
        T("bar", new Vector3(-CW, -SEAT + 0.42f, BARZ), new Vector3(CW, -SEAT + 0.42f, BARZ), 0.028f);
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * CW;
            T("frame", new Vector3(x, BAIL - 0.16f, -0.02f), new Vector3(x, -SEAT + 0.55f, BACKZ), 0.03f);
            T("frame", new Vector3(x, -SEAT + 0.55f, BACKZ), new Vector3(x, -SEAT - 0.02f, BACKZ + 0.03f), 0.03f);
            T("frame", new Vector3(x, -SEAT - 0.02f, BACKZ + 0.03f), new Vector3(x, -SEAT - 0.02f, 0.27f), 0.03f);
            T("armrest", new Vector3(x, -SEAT + 0.2f, BACKZ + 0.02f), new Vector3(x, -SEAT + 0.2f, 0.2f), 0.022f);
            T("bararm", new Vector3(x, BAIL - 0.25f, -0.02f), new Vector3(x, -SEAT + 0.42f, BARZ), 0.026f);
            T("footpost", new Vector3(s * 0.55f, -SEAT + 0.42f, BARZ), new Vector3(s * 0.55f, -SEAT - 0.5f, BARZ + 0.14f), 0.022f);
            T("footrest", new Vector3(s * 0.55f - 0.42f, -SEAT - 0.5f, BARZ + 0.14f), new Vector3(s * 0.55f + 0.42f, -SEAT - 0.5f, BARZ + 0.14f), 0.028f);
        }
    }

    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    public static void Chairs()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        ChairParts();
        PeopleKit.Load();
        var sb = new StringBuilder();
        const float tol = 0.008f;   // a cushion gives a centimetre
        int bad = 0, cases = 0;
        float worstNb = 0f; string worstNbCase = "";
        var depth = new Dictionary<string, float>();
        var count = new Dictionary<string, int>();
        var worstCase = new Dictionary<string, string>();
        var chair = Matrix4x4.TRS(new Vector3(0f, 500f, 0f), Quaternion.identity, Vector3.one);
        var pos = new List<Vector3>(); var slot = new List<int>(); var tris = new List<int>();
        Person p = null;
        foreach (bool female in new[] { false, true })
        foreach (float height in new[] { 0.88f, 0.97f, 1.07f })
            foreach (float build in new[] { 0.92f, 1.06f })
                for (int pose = 0; pose < LiftRiders.Poses; pose++)
                    foreach (bool pack in new[] { false, true })
                        foreach (int foot in new[] { 0, 1 })
                        {
                            if (pose < 2 && foot == 1) continue;   // (skiers have no front foot)
                            var look = PersonLook.Random(77 + cases);
                            look.female = female;   // (the summit kit cut for a man, Rider/Hero, or a woman, Rider/HeroF)
                            look.height = height; look.build = build; look.shoulders = build > 1f ? 1.03f : 0.88f;
                            look.pack = pack ? Wardrobe.Get(Wardrobe.Cat.Pack, 1) : null;
                            look.hairStyle = PersonHair.Ponytail; look.head = PersonHead.Helmet;
                            look.gear = pose < 2 ? PersonGear.Skis : PersonGear.Snowboard;
                            if (p == null) p = Person.Make(look); else p.Dress(look);
                            for (int seat = 0; seat < 4; seat++)
                            {
                                cases++;
                                // (as LiftRiders deals them: a pack on an end seat with narrower shoulders)
                                look.shoulders = (seat == 0 || seat == 3) && pack ? Mathf.Min(look.shoulders, 0.98f) : (build > 1f ? 1.03f : 0.88f);
                                p.Dress(look);
                                LiftRiders.Place(p, look, pose);
                                if (pose >= 2) p.boardFoot = foot;
                                var seatM = chair * LiftRiders.SeatLocal(seat);
                                p.transform.SetPositionAndRotation(seatM.GetColumn(3), seatM.rotation);
                                LiftRiders.TestHands(p, pose, chair, seat);
                                for (int k = 0; k < 3; k++) p.Apply(0f);
                                pos.Clear(); slot.Clear(); tris.Clear();
                                PeopleKit.Gather(p, chair.inverse, pos, slot, tris);
                                string name = $"{(female ? "F" : "M")} h{height:0.00} b{build:0.00} {(LiftRiders.Pose)pose}{(pose >= 2 ? (foot == 0 ? " left-foot" : " right-foot") : "")}{(pack ? " pack" : "")} seat {seat}";
                                bool caseBad = false;
                                float nbMax = 0f;
                                float sx = LiftRiders.SeatX[seat];
                                var drawn = new bool[pos.Count];
                                foreach (var k in tris) drawn[k] = true;
                                for (int vi = 0; vi < pos.Count; vi++)
                                {
                                    if (!drawn[vi]) continue;
                                    var v = pos[vi];
                                    curV = v;
                                    if (slot[vi] < 0) continue;
                                    string sl = SlotName(slot[vi]);
                                    foreach (var t in tubes)
                                    {
                                        float dd = t.r - SegDist(v, t.a, t.b);
                                        if (dd > tol) { Note(t.name + "/" + sl, dd, name); caseBad = true; }
                                    }
                                    foreach (var b in boxes)
                                    {
                                        if (v.x < b.lo.x || v.x > b.hi.x || v.y < b.lo.y || v.y > b.hi.y || v.z < b.lo.z || v.z > b.hi.z) continue;
                                        float dd = Mathf.Min(Mathf.Min(v.x - b.lo.x, b.hi.x - v.x), Mathf.Min(Mathf.Min(v.y - b.lo.y, b.hi.y - v.y), Mathf.Min(v.z - b.lo.z, b.hi.z - v.z)));
                                        if (dd > tol) { Note(b.name + "/" + sl, dd, name); caseBad = true; }
                                    }
                                    // into the next seat's room (the seats are 0.55 apart: half each), above the footrest
                                    float over = Mathf.Abs(v.x - sx) - 0.275f;
                                    if (seat == 0 && v.x < sx) over = 0f;   // (the outer seats' outer side: the frame, tested above)
                                    if (seat == 3 && v.x > sx) over = 0f;
                                    if (v.y > -3.3f && over > nbMax) { nbMax = over; nbWhat = SlotName(slot[vi]) + " at " + v.ToString("F2"); }
                                }
                                if (nbMax > worstNb) { worstNb = nbMax; worstNbCase = name + " " + nbWhat; }
                                if (caseBad) { bad++; if (bad <= 40) sb.AppendLine("CLIP " + name + " " + Last()); }
                            }
                        }
        sb.AppendLine($"CHAIRS {cases} riders posed, {bad} touching the chair; deepest by part: " + string.Join(", ", Parts()));
        sb.AppendLine($"CHAIRS furthest into the next seat's half: {worstNb * 100f:0.0} cm ({worstNbCase})");
        Debug.Log(sb.ToString());
        System.IO.File.WriteAllText("Logs/people-chairs.txt", sb.ToString());
        // shoulder to shoulder is a quad chair; more than 6 cm into the next seat would put two people in one place
        EditorApplication.Exit(bad == 0 && worstNb < 0.06f ? 0 : 1);

        void Note(string part, float dd, string who)
        {
            if (!depth.TryGetValue(part, out var d0) || dd > d0) { depth[part] = dd; worstCase[part] = who + $" at {curV.ToString("F2")}"; }
            count[part] = (count.TryGetValue(part, out var c0) ? c0 : 0) + 1;
            lastPart = part; lastDepth = dd;
        }
        string Last() => $"{lastPart} {lastDepth * 100f:0.0} cm";
        IEnumerable<string> Parts() { foreach (var kv in depth) yield return $"\n  {kv.Key} {kv.Value * 100f:0.0} cm ({count[kv.Key]} vertices; worst {worstCase[kv.Key]})"; }
    }
    static string lastPart; static float lastDepth; static Vector3 curV; static string nbWhat;

    // ---------------------------------------------------------------- feet

    /// <summary>One person walking straight at a few speeds: how far the planted foot slides. -executeMethod PeopleTest.Feet</summary>
    public static void Feet()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        PeopleKit.Load();
        var p = Person.Make(PersonLook.Random(5));
        var sb = new StringBuilder();
        const float dt = 1f / 60f;
        foreach (float frac in new[] { 0.5f, 0.8f, 1f })
        {
            p.transform.position = Vector3.zero; p.phase = 0f;
            p.mode = Person.Mode.Walk; p.pace = frac;
            float v = p.NaturalSpeed * frac;
            for (int i = 0; i < 120; i++) { p.transform.position += Vector3.forward * v * dt; p.Advance(v * dt); p.Apply(dt); }
            double slid = 0, walked = 0; int lastSide = -1; Vector3 last = Vector3.zero;
            for (int i = 0; i < 600; i++)
            {
                p.transform.position += Vector3.forward * v * dt; p.Advance(v * dt); p.Apply(dt);
                walked += v * dt;
                var r = p.Rig;
                int low = r.Foot[0].position.y < r.Foot[1].position.y ? 0 : 1;
                var f = r.Foot[low].position;
                bool planted = f.y < 0.099f * p.Scale && Mathf.Abs(r.Foot[0].position.y - r.Foot[1].position.y) > 0.02f;
                if (planted && lastSide == low) slid += new Vector2(f.x - last.x, f.z - last.z).magnitude;
                lastSide = planted ? low : -1; last = f;
                if (frac == 1f && i < 60) sb.AppendLine($"FEET ph {p.phase:0.000} L {r.Foot[0].position.ToString("F3")} R {r.Foot[1].position.ToString("F3")} body z {p.transform.position.z:0.000}");
            }
            sb.AppendLine($"FEET pace {frac}: {v:0.00} m/s, slid {slid * 100.0:0.0} cm over {walked:0.0} m ({slid / walked * 100.0:0.00} cm per metre)");
        }
        Debug.Log(sb.ToString());
        System.IO.File.WriteAllText("Logs/people-feet.txt", sb.ToString());
        EditorApplication.Exit(0);
    }

    // ---------------------------------------------------------------- lift riders: there from as far as their chairs

    /// <summary>
    /// Ride down the mountain past its lifts (SUMMIT_LIFT_SEED, 4242), the camera behind as the chase camera has it,
    /// the lifts built a few pieces a frame as in the game. Each frame: the chairs in view, and of those that carry
    /// people, how many are drawn without them; per chair, how far off it first came into view and how far off its
    /// riders first did. And whether a build keeps the riders' instanced shader (a GPU-instancing material of it in
    /// Resources: with "Strip Unused" instancing variants, a shader only ever used by a material made at run time loses
    /// them, and its instanced draws come out as nothing on the phone). -executeMethod PeopleTest.LiftPop
    /// </summary>
    public static void LiftPop()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var lift = Object.FindFirstObjectByType<SkiLift>();
        var lUpd = typeof(SkiLift).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_SEED"), out int sd) ? sd : 4242;
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild();
        SkiLift.SpreadBuild = true;   // (as in the game: a few pieces a frame)
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        cam.fieldOfView = 62f;
        var sb = new StringBuilder();
        // what a build would keep
        bool kept = false;
        foreach (var m in Resources.LoadAll<Material>("")) if (m != null && m.shader != null && m.shader.name == "Summit/PeopleLit" && m.enableInstancing) kept = true;
        sb.AppendLine($"LIFTPOP the riders' instanced shader kept by a build: {(kept ? "yes (an instancing material of it in Resources)" : "NO: no instancing material of Summit/PeopleLit in Resources, so 'Strip Unused' drops its instanced variants and every rider past the near ones draws as nothing on the phone")}");
        sb.AppendLine($"LIFTPOP the instanced riders' material: {PeopleKit.LodMaterial.name}, GPU instancing {PeopleKit.LodMaterial.enableInstancing}");
        // the ride
        float zTop = float.MaxValue, zBot = float.MinValue;
        foreach (var l in LiftLine.Lifts) { zTop = Mathf.Min(zTop, l.ZMin); zBot = Mathf.Max(zBot, l.ZMax); }
        float z0 = Mathf.Max(0f, zTop - 100f), z1 = zBot + 50f;
        const float dt = 1f / 30f, speed = 22f;
        var first = new Dictionary<long, Vector2>();   // x: how far the chair was when first in view, y: its riders (-1: not yet)
        int frames = 0, inViewWithPeople = 0, missingInView = 0, missingFrames = 0, worstMissing = 0, chairsInView = 0;
        float missingNearest = float.MaxValue;
        var planes = new Plane[6];
        Vector3 eye = Vector3.zero;
        LiftRiders.Seen = (id, c, lap, pos, count, shown) =>
        {
            var b = new Bounds(pos + Vector3.down * 3.0f, new Vector3(2.6f, 2.4f, 2.2f));
            if (!GeometryUtility.TestPlanesAABB(planes, b)) return;
            float d = (pos - eye).magnitude;
            if (d > cam.farClipPlane) return;
            chairsInView++;
            long key = ((long)id << 48) ^ ((long)c << 32) ^ (long)(lap & 0xffffffff);
            var f = first.TryGetValue(key, out var have) ? have : new Vector2(d, -1f);
            if (count > 0)
            {
                inViewWithPeople++;
                if (shown) { if (f.y < 0f) f.y = d; }
                else { missingInView++; frameMissing++; missingNearest = Mathf.Min(missingNearest, d); }
            }
            else if (f.y < 0f) f.y = -2f;   // (nobody aboard: nothing to wait for)
            first[key] = f;
        };
        for (float z = z0; z < z1; z += speed * dt)
        {
            float x = Slope.Centre(z);
            var at = new Vector3(x, Slope.DrawnHeight(x, z), z);
            rider.ResetAt(x, z); body.ResetVisual(); rider.Stop();
            cam.transform.SetPositionAndRotation(at + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.28f, 1f)));
            eye = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            SkiLift.TestClock = 300.0 + frames * dt;
            frameMissing = 0;
            lUpd.Invoke(lift, null);
            frames++;
            if (frameMissing > 0) missingFrames++;
            worstMissing = Mathf.Max(worstMissing, frameMissing);
        }
        LiftRiders.Seen = null;
        // how far off each chair came into view, and how much nearer it was when its riders did
        var chairD = new List<float>(); var lateBy = new List<float>(); int neverRiders = 0;
        foreach (var kv in first)
        {
            chairD.Add(kv.Value.x);
            if (kv.Value.y >= 0f) lateBy.Add(kv.Value.x - kv.Value.y);
            else if (kv.Value.y == -1f) neverRiders++;
        }
        chairD.Sort(); lateBy.Sort();
        float Median(List<float> l) => l.Count == 0 ? 0f : l[l.Count / 2];
        float Most(List<float> l) => l.Count == 0 ? 0f : l[l.Count - 1];
        sb.AppendLine($"LIFTPOP seed {seed}: {LiftLine.Lifts.Length} lifts, ridden from z {z0:0} to {z1:0} at {speed} m/s, {frames} frames");
        sb.AppendLine($"LIFTPOP chair-frames in view {chairsInView}, {inViewWithPeople} with people aboard; drawn without them {missingInView} ({(inViewWithPeople > 0 ? missingInView * 100f / inViewWithPeople : 0f):0.0}%), in {missingFrames} frames (most at once {worstMissing}; nearest {(missingNearest < float.MaxValue ? missingNearest.ToString("0") + " m" : "none")})");
        sb.AppendLine($"LIFTPOP chairs first seen at {Median(chairD):0} m (median), {Most(chairD):0} m (furthest); their riders came in {Median(lateBy):0} m nearer than the chair (median), {Most(lateBy):0} m (most); chairs with people never shown with them: {neverRiders}");
        Debug.Log(sb.ToString());
        System.IO.File.WriteAllText("Logs/people-liftpop.txt", sb.ToString());
        SkiLift.TestClock = -1.0;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(kept && missingInView == 0 ? 0 : 1);
    }
    static int frameMissing;

    // ---------------------------------------------------------------- the camp

    const System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    /// <summary>
    /// The camp's people for several minutes of game time (SUMMIT_CAMP_MINUTES, 4), the hero walking and running
    /// between the lodge, the lookout, the gate and the rail, and the edge giving way once: collisions (people into
    /// each other, the hero, the camp's solid things), people stuck, anyone near the edge, how far planted feet slide,
    /// and what it costs a frame. Batch: -executeMethod PeopleTest.Camp. Exit 1 on overlaps, anyone off the edge or
    /// feet sliding (SUMMIT_PEOPLE_LOG=1: why each one was stuck, and where feet slid, by 3 m square, with the worst steps).
    /// </summary>
    public static void Camp()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        var camp = (Summit.Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = 1f / 30f;
        var life = camp.GetComponent<CampLife>();
        var sb = new StringBuilder();
        if (life == null || life.Agents.Count == 0) { Debug.LogError("PEOPLETEST no camp life"); EditorApplication.Exit(1); return; }
        float minutes = float.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_CAMP_MINUTES"), out float mm) ? mm : 4f;
        // two people of someone else's, through the API other sessions use: one wandered by the camp, one held still
        var guestLook = PersonLook.Random(424242); guestLook.name = "Guest";
        var guest = Person.Make(guestLook);
        life.Add(guest, "snowman");
        guest.transform.position = life.P("door").at + Quaternion.Euler(0f, life.P("door").yaw, 0f) * Vector3.forward * 2.5f;
        life.Moved(guest);
        var heldLook = PersonLook.Random(434343); heldLook.name = "Held";
        var held = Person.Make(heldLook);
        var heldAt = life.P("chat.3").at + Vector3.right * 2.2f;
        held.transform.SetPositionAndRotation(heldAt, Quaternion.Euler(0f, 200f, 0f));
        life.Hold(held); life.Moved(held);
        held.mode = Person.Mode.Stand;
        const float dt = 1f / 30f;
        int frames = Mathf.RoundToInt(minutes * 60f / dt);
        var fPos = typeof(Summit.Camp).GetField("pos", NP); var fCamYaw = typeof(Summit.Camp).GetField("camYaw", NP);
        var cam = Camera.main;
        float cx = Slope.Centre(Slope.PadLip);
        var stairs = life.P("door").at;
        var targets = new[] { stairs, life.P("lookout.1").at + Vector3.back * 3.5f, new Vector3(cx, 0f, Slope.GateZ - 3f), life.P("rail.2").at + Vector3.left * 2f, life.P("chat.1").at + Vector3.left * 2.5f, life.P("bench.1").at + Vector3.forward * 1.5f, life.P("wax").at + Vector3.back * 2f };
        int ti = 0; float runFor = 0f;
        // counters
        int overlapPeople = 0, overlapHero = 0, inThings = 0, nearEdge = 0, frameCount = 0;
        float worstPeople = 0f, worstHero = 0f, worstThing = 0f, worstEdge = -99f;
        string worstPeopleWhat = "", worstThingWhat = "";
        int overlapOthers = 0; float worstOthers = 0f; int othersSeen = 0;
        double stepMs = 0, stepMax = 0, applyMs = 0;
        int lod0 = 0, lod1 = 0, maxSkins = 0; long trisSum = 0, trisMax = 0;
        var slide = new Dictionary<CampLife.Agent, (Vector3 foot, int side)>();
        bool slideLog = System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_LOG") == "1";
        var slideBy = new Dictionary<string, (float, float)>(); var slideLines = new List<string>();
        var lastAt = new Dictionary<CampLife.Agent, Vector3>();
        double slideDist = 0, walkDist = 0; float slideWorst = 0f;
        int collapseAt = frames / 2;
        var reactLook = new List<float>();
        var activities = new Dictionary<CampLife.Doing, int>();
        bool collapsed = false;
        float guestWalk = 0f;
        string reactLine = "";
        for (int f = 0; f < frames; f++)
        {
            // the hero: on to the next place, now and then at a run (and the edge gives way once, half way)
            var p = (Vector3)fPos.GetValue(camp);
            var to = targets[ti % targets.Length];
            if (new Vector2(to.x - p.x, to.z - p.z).magnitude < 1.5f) { ti++; runFor = (ti % 3 == 1) ? 6f : 0f; }
            float head = Mathf.Atan2(to.x - p.x, to.z - p.z) - (float)fCamYaw.GetValue(camp);
            camp.Walk(new Vector2(Mathf.Sin(head), Mathf.Cos(head)) * (runFor > 0f ? 1f : 0.85f), dt);
            runFor -= dt;
            body.Apply(dt); camp.CameraStep(dt);
            if (f == collapseAt && !collapsed)
            {
                collapsed = true;
                // (the edge's own collapse throws the hero into the ragdoll: the people's side of it is what's tested)
                typeof(CampLife).GetMethod("OnCollapse", NP).Invoke(life, new object[] { new Vector3(cx + 23.5f, Slope.DrawnHeight(cx + 23.5f, -66f), -66f), Vector3.right });
            }
            held.Apply(dt);   // (its owner poses it)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            life.Step(dt, cam.transform.position);
            life.Tick(dt);
            double ms = sw.Elapsed.TotalMilliseconds;
            stepMs += ms; stepMax = System.Math.Max(stepMax, ms); frameCount++;
            if (collapsed && f == collapseAt + 90)
            {
                var r = new StringBuilder("CAMP   3 s after: ");
                foreach (var a in life.Agents) if (a.p.gameObject.activeSelf) r.Append($"{a.role}:{a.doing}{(a.goer ? " goer" : "")} turn {a.turn:0} facing {Vector3.Angle(a.p.Facing, new Vector3(cx + 23.5f, a.p.HeadPosition.y, -66f) - a.p.HeadPosition):0}deg lod{a.p.Lod}; ");
                reactLine = r.ToString();
            }
            // after the collapse: are they looking at it?
            if (collapsed && f > collapseAt + 30 && f < collapseAt + 120)
                foreach (var a in life.Agents) if (a.p.gameObject.activeSelf && a.doing != CampLife.Doing.Ride) reactLook.Add(Vector3.Angle(a.p.Facing, new Vector3(cx + 23.5f, 0f, -66f) - a.p.HeadPosition));
            // the checks
            othersSeen = Mathf.Max(othersSeen, life.OthersNow.Count);
            // (the couple and Paul live their own lives: CampCouple steps them as the game does)
            if (camp.Couple != null) camp.Couple.Step(dt);
            int skins = 0; long tris = 0;
            var hero = (Vector3)fPos.GetValue(camp);
            foreach (var a in life.Agents)
            {
                if (!a.p.gameObject.activeSelf) continue;
                activities[a.doing] = (activities.TryGetValue(a.doing, out int n0) ? n0 : 0) + 1;
                if (a.p.Lod == 0) { lod0++; skins += 3; tris += 14500; } else if (a.p.Lod == 1) { lod1++; skins++; tris += a.p.LodSkin.sharedMesh.GetIndexCount(0) / 3; }
                if (a.doing == CampLife.Doing.Ride) continue;
                foreach (var o in life.Agents)
                {
                    if (o == a || !o.p.gameObject.activeSelf || o.doing == CampLife.Doing.Ride) continue;
                    if (Mathf.Abs(a.pos.y - o.pos.y) > CampLife.OtherFloor) continue;   // (on different floors: the lodge's deck over its café)
                    float d = new Vector2(a.pos.x - o.pos.x, a.pos.z - o.pos.z).magnitude;
                    if (d < 0.5f) { overlapPeople++; if (0.5f - d > worstPeople) { worstPeople = 0.5f - d; worstPeopleWhat = $"{a.role}:{a.doing} and {o.role}:{o.doing} at {a.pos}"; } }
                }
                foreach (var o in life.OthersNow)
                {
                    if (a.doing == CampLife.Doing.Sit || a.doing == CampLife.Doing.Strap) continue;
                    float d = new Vector2(a.pos.x - o.x, a.pos.z - o.z).magnitude;
                    if (d < 0.5f) { overlapOthers++; worstOthers = Mathf.Max(worstOthers, 0.5f - d); }
                }
                float dh = new Vector2(a.pos.x - hero.x, a.pos.z - hero.z).magnitude;
                if (dh < 0.45f) { overlapHero++; worstHero = Mathf.Max(worstHero, 0.45f - dh); }
                if (a.doing == CampLife.Doing.Walk || a.doing == CampLife.Doing.Stand || a.doing == CampLife.Doing.Look || a.doing == CampLife.Doing.Chat)
                {
                    var q = camp.PushOutOfThings(new Vector2(a.pos.x, a.pos.z), 0.2f, a.pos.y);
                    float m = (q - new Vector2(a.pos.x, a.pos.z)).magnitude;
                    if (m > 0.02f) { inThings++; if (m > worstThing) { worstThing = m; worstThingWhat = $"{a.role}:{a.doing} at {a.pos} ({a.pos.x - cx:0.0}, {a.pos.z:0.0}) spot {a.spot?.name}"; } }
                }
                float edge = a.pos.x - cx;
                bool onDeck = Summit.Camp.OnLookoutDeck(edge, a.pos.z);   // (the lookout's deck goes out past the rail, over the lip: camplayout)
                if (!onDeck) worstEdge = Mathf.Max(worstEdge, edge);
                if ((edge > 22f && !onDeck) || (!onDeck && a.pos.y < Slope.DrawnHeight(a.pos.x, a.pos.z) - 0.5f)) nearEdge++;
                // foot slide: the planted foot of a walker in the full model stays put on the ground
                if (a.doing == CampLife.Doing.Walk && a.p.Lod == 0 && a.speed > 0.4f)
                {
                    var rig = a.p.Rig;
                    int low = rig.Foot[0].position.y < rig.Foot[1].position.y ? 0 : 1;
                    var footP = rig.Foot[low].position;
                    bool planted = footP.y - a.pos.y < 0.099f * a.p.Scale && Mathf.Abs(rig.Foot[0].position.y - rig.Foot[1].position.y) > 0.02f;
                    if (planted && slide.TryGetValue(a, out var prev) && prev.side == low)
                    {
                        float sl = new Vector2(footP.x - prev.foot.x, footP.z - prev.foot.z).magnitude;
                        slideDist += sl; slideWorst = Mathf.Max(slideWorst, sl / dt);
                        if (slideLog)
                        {
                            bool st = Summit.Camp.OnStairs(a.pos, 0.3f);
                            string zone = st ? "stairs" : $"({Mathf.RoundToInt((a.pos.x - cx) / 3f) * 3}, {Mathf.RoundToInt(a.pos.z / 3f) * 3})";
                            var z0 = slideBy.TryGetValue(zone, out var z1) ? z1 : (0f, 0f); slideBy[zone] = (z0.Item1 + sl, z0.Item2);
                            Vector3 mv = lastAt.TryGetValue(a, out var la) ? a.pos - la : Vector3.zero; mv.y = 0f;
                            float mvYaw = mv.sqrMagnitude > 1e-8f ? Mathf.Atan2(mv.x, mv.z) * Mathf.Rad2Deg : float.NaN;
                            var fw = Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward * 0.5f;
                            if (sl > 0.01f && slideLines.Count < 80) slideLines.Add($"CAMP     slide {sl * 100f:0.0} cm {a.role}:{a.spot?.name} at ({a.pos.x - cx:0.00}, {a.pos.y:0.00}, {a.pos.z:0.00}) {(st ? "on the stairs" : "")} foot{low} {footP.y - a.pos.y:0.000} other {rig.Foot[1 - low].position.y - a.pos.y:0.000} speed {a.speed:0.00} moved {mv.magnitude * 100f:0.0} cm yaw {a.yaw:0} moving {mvYaw:0} ahead walkable {life.Walkable(a.pos + fw)} drawn yaw {a.p.transform.eulerAngles.y:0} pace {a.p.pace:0.00} f{f}");
                        }
                    }
                    walkDist += a.speed * dt;
                    if (slideLog) { bool st2 = Summit.Camp.OnStairs(a.pos, 0.3f); string zn = st2 ? "stairs" : $"({Mathf.RoundToInt((a.pos.x - cx) / 3f) * 3}, {Mathf.RoundToInt(a.pos.z / 3f) * 3})"; var w0 = slideBy.TryGetValue(zn, out var w1) ? w1 : (0f, 0f); slideBy[zn] = (w0.Item1, w0.Item2 + a.speed * dt); }
                    slide[a] = planted ? (footP, low) : (footP, -1);
                }
                else slide.Remove(a);
                lastAt[a] = a.pos;
            }
            maxSkins = Mathf.Max(maxSkins, skins); trisSum += tris; trisMax = System.Math.Max(trisMax, tris);
            var ga = life.Agents[life.Agents.Count - 2];
            if (ga.doing == CampLife.Doing.Walk) guestWalk += ga.speed * dt;
        }
        // what one person's posing costs (the full model), apart from the walking about
        {
            var a = life.Agents[0];
            a.p.SetLod(0);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++) a.p.Apply(dt);
            applyMs = sw.Elapsed.TotalMilliseconds / 2000.0;
        }
        float avgReact = 0f; foreach (var v in reactLook) avgReact += v; if (reactLook.Count > 0) avgReact /= reactLook.Count;
        sb.AppendLine($"CAMP {minutes:0.0} min of game time, {life.Agents.Count} people (the camp's {CampLife.Crowd}, one added and one held through the API), {frameCount} frames at 30 fps");
        sb.AppendLine($"CAMP the added guest ended {life.Agents[life.Agents.Count - 2].doing} at ({guest.transform.position.x - cx:0.0}, {guest.transform.position.z:0.0}), walked {guestWalk:0} m");
        sb.AppendLine($"CAMP overlaps: people into people {overlapPeople} frames (deepest {worstPeople * 100f:0} cm), into the hero {overlapHero} (deepest {worstHero * 100f:0} cm), into the camp's things {inThings} (deepest {worstThing * 100f:0} cm)");
        sb.AppendLine($"CAMP   worst people overlap: {worstPeopleWhat}; worst in a thing: {worstThingWhat}");
        sb.AppendLine($"CAMP into the camp's characters (Val, Breanna, Paul, Alyssa; up to {othersSeen} about at once): {overlapOthers} frames (deepest {worstOthers * 100f:0} cm)");
        sb.AppendLine($"CAMP stuck (no nearer their goal in 4 s, path found again): {life.StuckCount} (last: {life.LastStuck})");
        {
            var where = new Dictionary<Vector2Int, int>();
            foreach (var w in life.StuckAt) { var k = new Vector2Int(Mathf.RoundToInt(w.x / 3f) * 3, Mathf.RoundToInt(w.y / 3f) * 3); where[k] = (where.TryGetValue(k, out int c0) ? c0 : 0) + 1; }
            var list = new List<KeyValuePair<Vector2Int, int>>(where); list.Sort((x, y) => y.Value.CompareTo(x.Value));
            var t = new StringBuilder(); for (int i = 0; i < Mathf.Min(4, list.Count); i++) t.Append($"({list[i].Key.x}, {list[i].Key.y}) x{list[i].Value}  ");
            sb.AppendLine("CAMP   held up most at: " + t);
            if (System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_LOG") == "1") foreach (var why in life.StuckWhy) sb.AppendLine("CAMP     " + why);
        }
        sb.AppendLine($"CAMP off the edge: {nearEdge} frames; furthest toward the cliff {worstEdge:0.0} m from the run's centre, off the lookout's deck (the rail is at 24.1)");
        sb.AppendLine($"CAMP planted feet slid {slideDist * 100.0:0.0} cm over {walkDist:0} m walked ({(walkDist > 0 ? slideDist / walkDist * 100.0 : 0):0.00} cm per metre; worst {slideWorst * 100f:0} cm/s)");
        if (slideLog)
        {
            foreach (var kv in slideBy) if (kv.Value.Item2 > 0.5f) sb.AppendLine($"CAMP     slid at {kv.Key}: {kv.Value.Item1 * 100f:0.0} cm over {kv.Value.Item2:0.0} m ({kv.Value.Item1 / kv.Value.Item2 * 100f:0.00} cm/m)");
            foreach (var l in slideLines) sb.AppendLine(l);
        }
        sb.AppendLine($"CAMP after the edge gave way: facing it within {avgReact:0} degrees on average");
        sb.AppendLine(reactLine);
        sb.AppendLine($"CAMP cost (editor): CampLife.Step {stepMs / frameCount:0.00} ms a frame on average, {stepMax:0.00} ms at most; one full-model person posed {applyMs * 1000.0:0} us");
        sb.AppendLine($"CAMP drawn: full model {lod0 / (float)frameCount:0.0} people on average, simplified {lod1 / (float)frameCount:0.0}; skinned meshes at most {maxSkins}; triangles {trisSum / frameCount} on average, {trisMax} at most");
        var act = new StringBuilder(); foreach (var kv in activities) act.Append($"{kv.Key} {kv.Value * 100f / (frameCount * life.Agents.Count):0}%  ");
        sb.AppendLine("CAMP time spent: " + act);
        Debug.Log(sb.ToString());
        System.IO.File.WriteAllText("Logs/people-camp.txt", sb.ToString());
        ChaseCamera.TestDt = 0f;
        TestPrefs.Restore();
        bool ok = overlapPeople == 0 && overlapOthers < frameCount / 200 && overlapHero < frameCount / 200 && inThings == 0 && nearEdge == 0 && (walkDist <= 0 || slideDist / walkDist < 0.02);
        EditorApplication.Exit(ok ? 0 : 1);
    }
    // ---------------------------------------------------------------- the lodge's own people

    /// <summary>
    /// The lodge's people (LodgeFolk, Camp.LodgeFolkAt) as the camp builds them, each posed over a minute of their
    /// looks, talk, sips and reaches, its full mesh (and gear, and mug) tested against what they sit at and stand by
    /// (the picnic tables' tops, benches, A-frames and bars; the café's tables, chairs, stools and counter; the racks):
    /// vertices into any of them, and into the floor; the seated: hips resting on the seat (none floating over it or
    /// sunk into it), the feet on the floor (or hanging from the stool); the standing: feet on the floor; nobody into
    /// anybody (their meshes, cell by cell). And what posing them costs a frame. Batch: -executeMethod PeopleTest.Lodge
    /// (no graphics needed). Exit 1 on any clipping, floating or overlap.
    /// </summary>
    public static void Lodge()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        var camp = (Summit.Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        var folk = LodgeFolk.Current;
        var sb = new StringBuilder();
        if (folk == null || folk.All.Count == 0) { Debug.LogError("PEOPLETEST no lodge folk"); EditorApplication.Exit(1); return; }
        var toLocal = Matrix4x4.TRS(camp.ChaletPoint(0f, 0f, 0f), Quaternion.Euler(0f, camp.ChaletYaw, 0f), Vector3.one).inverse;
        // what they sit at and stand by (MakeLodge.py's picnic(), bistro(), stool(), cafe(), rack()), in the lodge's frame
        tubes.Clear(); boxes.Clear();
        void B(string n, Vector3 lo, Vector3 hi) => boxes.Add(new Box { name = n, lo = lo, hi = hi });
        void T(string n, Vector3 a, Vector3 b, float r) => tubes.Add(new Tube { name = n, a = a, b = b, r = r });
        foreach (var (x, z) in Summit.Camp.CoveredTables)
        {
            B("table top", new Vector3(x - 1.1f, 0.72f, z - 0.4f), new Vector3(x + 1.1f, 0.78f, z + 0.4f));
            foreach (float s in new[] { -1f, 1f })
            {
                B("bench", new Vector3(x - 1.1f, 0.39f, z + s * 0.72f - 0.15f), new Vector3(x + 1.1f, 0.44f, z + s * 0.72f + 0.15f));
                foreach (float xx in new[] { x - 0.85f, x + 0.85f }) T("A-frame", new Vector3(xx, 0f, z + s * 0.85f), new Vector3(xx, 0.74f, z + s * 0.12f), 0.025f);
            }
            foreach (float xx in new[] { x - 0.85f, x + 0.85f }) B("bench bar", new Vector3(xx - 0.025f, 0.35f, z - 0.87f), new Vector3(xx + 0.025f, 0.39f, z + 0.87f));
        }
        var cyls = new List<(string n, Vector3 c, float r, float y0, float y1)>();
        foreach (var (x, z) in new[] { (-14.4f, 0.7f), (-12.6f, -0.9f), (-9.2f, 3.6f), (-8.4f, 5.6f) })
        {
            cyls.Add(("café table", new Vector3(x, 0f, z), 0.38f, 0.72f, 0.76f));
            cyls.Add(("café table's foot", new Vector3(x, 0f, z), 0.03f, 0f, 0.72f));
            cyls.Add(("café table's base", new Vector3(x, 0f, z), 0.25f, 0f, 0.03f));
            foreach (float a in new[] { 0f, 180f })
            {
                float cx = x + (a == 0f ? 0.7f : -0.7f), bx = cx + (a == 0f ? 0.2f : -0.2f);
                B("chair seat", new Vector3(cx - 0.21f, 0.4f, z - 0.21f), new Vector3(cx + 0.21f, 0.43f, z + 0.21f));
                B("chair back", new Vector3(bx - 0.015f, 0.43f, z - 0.21f), new Vector3(bx + 0.015f, 0.85f, z + 0.21f));
                foreach (var (dx, dz) in new[] { (-0.18f, -0.18f), (0.18f, -0.18f), (0.18f, 0.18f), (-0.18f, 0.18f) })
                    B("chair leg", new Vector3(cx + dx - 0.015f, 0f, z + dz - 0.015f), new Vector3(cx + dx + 0.015f, 0.4f, z + dz + 0.015f));
            }
        }
        const float WX0 = -15.7f, WX1 = -11f, WZF = 4.7f;
        for (int i = 0; i < 5; i++)
        {
            float sx = WX0 + 0.75f + i * 0.85f, sz = WZF - 0.95f;
            cyls.Add(("stool seat", new Vector3(sx, 0f, sz), 0.19f, 0.75f, 0.8f));
            cyls.Add(("stool pole", new Vector3(sx, 0f, sz), 0.025f, 0f, 0.75f));
        }
        B("counter top", new Vector3(WX0 + 0.15f, 1.02f, WZF - 0.5f), new Vector3(WX1 - 0.35f, 1.07f, WZF));
        B("counter front", new Vector3(WX0 + 0.2f, 0f, WZF - 0.12f), new Vector3(WX1 - 0.4f, 1.02f, WZF - 0.04f));
        // the lookout's deck on the wing's roof (MakeLodge.py's deck()): its stools, the oak ledge on the front rail and
        // its brackets, the rail itself (glass, posts, the top rail), the viewer, the telescope
        const float MY = Summit.Camp.LodgeUpper, DX0 = -16.9f, DZ1 = 6.6f, XH = -11f;
        foreach (float sx in new[] { DX0 + 1.35f, DX0 + 2.2f, DX0 + 3.05f, DX0 + 3.9f })
        {
            cyls.Add(("deck stool seat", new Vector3(sx, MY, DZ1 - 0.81f), 0.19f, 0.75f, 0.8f));
            cyls.Add(("deck stool pole", new Vector3(sx, MY, DZ1 - 0.81f), 0.025f, 0f, 0.75f));
        }
        B("deck ledge", new Vector3(DX0 + 0.9f, MY + 1.02f, DZ1 - 0.36f), new Vector3(XH - 1.3f, MY + 1.07f, DZ1 - 0.06f));
        foreach (float bx in new[] { DX0 + 1.1f, (DX0 + XH) / 2f - 0.2f, XH - 1.5f })
            B("deck ledge bracket", new Vector3(bx - 0.02f, MY + 0.72f, DZ1 - 0.3f), new Vector3(bx + 0.02f, MY + 1.02f, DZ1 - 0.05f));
        B("deck rail", new Vector3(DX0, MY, DZ1 - 0.035f), new Vector3(XH, MY + 1.05f, DZ1 + 0.035f));
        B("deck viewer", new Vector3(DX0 + 0.38f - 0.22f, MY, 2.6f - 0.22f), new Vector3(DX0 + 0.38f + 0.22f, MY + 1.42f, 2.6f + 0.22f));
        B("deck telescope", new Vector3(DX0 + 0.42f - 0.35f, MY, DZ1 - 0.5f - 0.35f), new Vector3(DX0 + 0.42f + 0.35f, MY + 1.45f, DZ1 - 0.5f + 0.35f));
        foreach (var (x0, x1, z) in Summit.Camp.CoveredRacks)
        {
            B("rack end", new Vector3(x0, 0f, z - 0.25f), new Vector3(x0 + 0.05f, 1.1f, z + 0.25f));
            B("rack end", new Vector3(x1 - 0.05f, 0f, z - 0.25f), new Vector3(x1, 1.1f, z + 0.25f));
            B("rack rail", new Vector3(x0, 0.18f, z - 0.25f), new Vector3(x1, 0.24f, z + 0.25f));
            B("rack's boards", new Vector3(x0, 0f, z - 0.3f), new Vector3(x1, 1.7f, z + 0.3f));
        }
        const float tol = 0.01f;
        int bad = 0, cases = 0, floats = 0, sunk = 0, feetOff = 0, overlaps = 0;
        float worstSeat = 0f, worstFeet = 0f;
        var depth = new Dictionary<string, float>(); var count = new Dictionary<string, int>(); var worstCase = new Dictionary<string, string>();
        var pos = new List<Vector3>(); var slot = new List<int>(); var tris = new List<int>();
        var cells = new Dictionary<Vector3Int, int>[folk.All.Count];
        // a minute of their lives, sampled every 1.5 s (each posed in the full model)
        const float dt = 1f / 30f;
        for (int sample = 0; sample < 40; sample++)
        {
            foreach (var f in folk.All) f.p.SetLod(0);
            for (int k = 0; k < 45; k++) folk.Step(dt);
            for (int i = 0; i < folk.All.Count; i++)
            {
                var f = folk.All[i];
                cases++;
                pos.Clear(); slot.Clear(); tris.Clear();
                PeopleKit.Gather(f.p, toLocal, pos, slot, tris);
                if (f.mug != null)
                    foreach (var mf in f.mug.GetComponentsInChildren<MeshFilter>())
                        foreach (var v in mf.sharedMesh.vertices) { pos.Add(toLocal.MultiplyPoint3x4(mf.transform.TransformPoint(v))); slot.Add(-2); }
                string who = $"{i}:{f.pose} at ({toLocal.MultiplyPoint3x4(f.seat).x:0.00}, {toLocal.MultiplyPoint3x4(f.seat).z:0.00}) t{sample * 1.5f:0.0}s";
                bool caseBad = false;
                var occ = cells[i] = new Dictionary<Vector3Int, int>();
                float lowest = 99f, lowFoot = 99f; string lowWhat = "";
                var seatL = toLocal.MultiplyPoint3x4(f.seat);
                float floorL = f.floorY - camp.ChaletPoint(0f, 0f, 0f).y;   // (their floor: the hall's, or the deck's)
                bool sitting = LodgeFolk.Sits(f);
                float seatTop = Summit.Camp.LodgeFolkAt[i].at.y;   // (the seat's top, in the lodge's frame)
                var toPerson = f.p.transform.worldToLocalMatrix * toLocal.inverse;
                for (int vi = 0; vi < pos.Count; vi++)
                {
                    var v = pos[vi];
                    string sl = slot[vi] == -2 ? "mug" : SlotName(slot[vi]);
                    curV = v;
                    if (slot[vi] != -2) { var c = new Vector3Int(Mathf.FloorToInt(v.x / 0.02f), Mathf.FloorToInt(v.y / 0.02f), Mathf.FloorToInt(v.z / 0.02f)); occ[c] = 1; }
                    if (v.y < floorL - tol) { Note("the floor/" + sl, floorL - v.y, who); caseBad = true; }
                    foreach (var t in tubes) { float dd = t.r - SegDist(v, t.a, t.b); if (dd > tol) { Note(t.name + "/" + sl, dd, who); caseBad = true; } }
                    foreach (var b in boxes)
                    {
                        if (v.x < b.lo.x || v.x > b.hi.x || v.y < b.lo.y || v.y > b.hi.y || v.z < b.lo.z || v.z > b.hi.z) continue;
                        float dd = Mathf.Min(Mathf.Min(v.x - b.lo.x, b.hi.x - v.x), Mathf.Min(Mathf.Min(v.y - b.lo.y, b.hi.y - v.y), Mathf.Min(v.z - b.lo.z, b.hi.z - v.z)));
                        if (dd > tol) { Note(b.name + "/" + sl, dd, who); caseBad = true; }
                    }
                    foreach (var c in cyls)
                    {
                        if (v.y < c.y0 || v.y > c.y1) continue;
                        float dd = Mathf.Min(c.r - new Vector2(v.x - c.c.x, v.z - c.c.z).magnitude, Mathf.Min(v.y - c.y0, c.y1 - v.y));
                        if (dd > tol) { Note(c.n + "/" + sl, dd, who); caseBad = true; }
                    }
                    // what rests on the seat: the lowest of them over the seat's top (the bench's depth, the chair's
                    // square, the stool's round top, in their own frame: as MakeLodge.py builds them)
                    if (sitting && slot[vi] >= 0)
                    {
                        var q = toPerson.MultiplyPoint3x4(v);
                        bool over = LodgeFolk.OverSeat(f, q);
                        if (over && v.y < lowest) { lowest = v.y; lowWhat = $"{sl} at {q.ToString("F3")} (their frame)"; }
                    }
                    if (sl == "boot") lowFoot = Mathf.Min(lowFoot, v.y);
                }
                if (sitting)
                {
                    float gap = lowest - seatTop;   // + floating over the seat, - into it
                    if (sample == 0)
                    {
                        var rg = f.p.Rig; var tp = f.p.transform;
                        string J(Transform t) => tp.InverseTransformPoint(t.position).ToString("F2");
                        sb.AppendLine($"LODGEFOLK hands {who}: left {tp.InverseTransformPoint(rg.Hand[0].position).ToString("F2")} (wanted {tp.InverseTransformPoint(f.p.handAt[0]).ToString("F2")} w{f.p.handW[0]:0.0}), right {tp.InverseTransformPoint(rg.Hand[1].position).ToString("F2")} (wanted {tp.InverseTransformPoint(f.p.handAt[1]).ToString("F2")} w{f.p.handW[1]:0.0}); shoulders {J(rg.UpperArm[0])} {J(rg.UpperArm[1])}");
                        sb.AppendLine($"LODGEFOLK seat {who}: raised {f.lift * 100f:0.0} cm, the lowest over the seat {gap * 100f:0.0} cm from its top ({lowWhat}); scale {f.p.Scale:0.00} hips {J(rg.Hips)} hip joints {J(rg.UpperLeg[0])} {J(rg.UpperLeg[1])} knees {J(rg.LowerLeg[0])} {J(rg.LowerLeg[1])} ankles {J(rg.Foot[0])} {J(rg.Foot[1])}");
                    }
                    if (Mathf.Abs(gap) > Mathf.Abs(worstSeat)) worstSeat = gap;
                    if (gap > 0.02f) { floats++; caseBad = true; Note("floating over the seat", gap, who); }
                    if (gap < -tol) { sunk++; caseBad = true; Note("sunk into the seat", -gap, who); }
                }
                if (f.pose != "stool")
                {
                    float fg = lowFoot - floorL;   // (above their floor)
                    if (Mathf.Abs(fg) > Mathf.Abs(worstFeet)) worstFeet = fg;
                    if (fg > 0.04f || fg < -tol) { feetOff++; caseBad = true; Note(fg > 0f ? "feet off the floor" : "feet into the floor", Mathf.Abs(fg), who); }
                }
                if (caseBad) { bad++; if (bad <= 30) sb.AppendLine("LODGEFOLK BAD " + who + " " + Last()); }
            }
            // nobody into anybody: a cell of one's mesh (2 cm) in another's
            for (int i = 0; i < folk.All.Count; i++)
                for (int j = i + 1; j < folk.All.Count; j++)
                {
                    int shared = 0;
                    foreach (var c in cells[i].Keys) if (cells[j].ContainsKey(c)) shared++;
                    if (shared > 0) { overlaps++; if (overlaps <= 10) sb.AppendLine($"LODGEFOLK OVERLAP {i}:{folk.All[i].pose} and {j}:{folk.All[j].pose} share {shared} cells at t{sample * 1.5f:0.0}s"); }
                }
        }
        // what posing them costs (the editor, this Mac): everyone in the full model, and as CampLife's levels leave them
        var swc = System.Diagnostics.Stopwatch.StartNew();
        foreach (var f in folk.All) f.p.SetLod(0);
        for (int k = 0; k < 300; k++) folk.Step(dt);
        double fullMs = swc.Elapsed.TotalMilliseconds / 300.0;
        foreach (var f in folk.All) f.p.SetLod(1);
        swc.Restart();
        for (int k = 0; k < 300; k++) folk.Step(dt);
        double simpleMs = swc.Elapsed.TotalMilliseconds / 300.0;
        long simpleTris = 0; foreach (var f in folk.All) if (f.p.LodSkin != null) simpleTris += f.p.LodSkin.sharedMesh.GetIndexCount(0) / 3;
        sb.AppendLine($"LODGEFOLK {folk.All.Count} people ({cases} poses over a minute of their time): {bad} with something wrong; touching furniture or the floor, deepest by part: " + string.Join(", ", Parts()));
        sb.AppendLine($"LODGEFOLK seated: {floats} floating over the seat, {sunk} sunk into it (the worst {worstSeat * 100f:0.0} cm); feet {feetOff} off or into the floor (the worst {worstFeet * 100f:0.0} cm); into each other {overlaps}");
        sb.AppendLine($"LODGEFOLK cost (editor, this Mac): posing all {folk.All.Count} in the full model {fullMs:0.000} ms a frame; in the simplified one (every third frame each) {simpleMs:0.000} ms a frame; simplified bodies {simpleTris} triangles together, {folk.All.Count} skins, no shadows");
        Debug.Log(sb.ToString());
        System.IO.File.WriteAllText("Logs/people-lodge.txt", sb.ToString());
        TestPrefs.Restore();
        EditorApplication.Exit(bad == 0 && overlaps == 0 ? 0 : 1);

        void Note(string part, float dd, string who)
        {
            if (!depth.TryGetValue(part, out var d0) || dd > d0) { depth[part] = dd; worstCase[part] = who + $" at {curV.ToString("F2")}"; }
            count[part] = (count.TryGetValue(part, out var c0) ? c0 : 0) + 1;
            lastPart = part; lastDepth = dd;
        }
        string Last() => $"{lastPart} {lastDepth * 100f:0.0} cm";
        IEnumerable<string> Parts() { foreach (var kv in depth) yield return $"\n  {kv.Key} {kv.Value * 100f:0.0} cm ({count[kv.Key]} vertices; worst {worstCase[kv.Key]})"; }
    }

    static string SlotName(int s) => s < 0 || s >= slotNames.Length ? "?" : slotNames[s];
    static readonly string[] slotNames = { "skin", "jacket", "jacket2", "pants", "head", "lens", "frame", "glove", "boot", "board", "dark", "pack", "hair", "gaiter", "metal", "pompom" };
}
