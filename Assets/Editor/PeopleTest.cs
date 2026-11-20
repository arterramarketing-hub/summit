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
public static class PeopleTest
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
        foreach (float height in new[] { 0.88f, 0.97f, 1.07f })
            foreach (float build in new[] { 0.92f, 1.06f })
                for (int pose = 0; pose < LiftRiders.Poses; pose++)
                    foreach (bool pack in new[] { false, true })
                        foreach (int foot in new[] { 0, 1 })
                        {
                            if (pose < 2 && foot == 1) continue;   // (skiers have no front foot)
                            var look = PersonLook.Random(77 + cases);
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
                                string name = $"h{height:0.00} b{build:0.00} {(LiftRiders.Pose)pose}{(pose >= 2 ? (foot == 0 ? " left-foot" : " right-foot") : "")}{(pack ? " pack" : "")} seat {seat}";
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

    // ---------------------------------------------------------------- the camp

    const System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    /// <summary>
    /// The camp's people for several minutes of game time (SUMMIT_CAMP_MINUTES, 4), the hero walking and running
    /// between the lodge, the lookout, the gate and the rail, and the edge giving way once: collisions (people into
    /// each other, the hero, the camp's solid things), people stuck, anyone near the edge, how far planted feet slide,
    /// and what it costs a frame. Batch: -executeMethod PeopleTest.Camp. Exit 1 on overlaps, anyone off the edge or
    /// feet sliding.
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
        double stepMs = 0, stepMax = 0, applyMs = 0;
        int lod0 = 0, lod1 = 0, maxSkins = 0; long trisSum = 0, trisMax = 0;
        var slide = new Dictionary<CampLife.Agent, (Vector3 foot, int side)>();
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
                    float d = new Vector2(a.pos.x - o.pos.x, a.pos.z - o.pos.z).magnitude;
                    if (d < 0.5f) { overlapPeople++; if (0.5f - d > worstPeople) { worstPeople = 0.5f - d; worstPeopleWhat = $"{a.role}:{a.doing} and {o.role}:{o.doing} at {a.pos}"; } }
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
                worstEdge = Mathf.Max(worstEdge, edge);
                if (edge > 22f || a.pos.y < Slope.DrawnHeight(a.pos.x, a.pos.z) - 0.5f) nearEdge++;
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
                    }
                    walkDist += a.speed * dt;
                    slide[a] = planted ? (footP, low) : (footP, -1);
                }
                else slide.Remove(a);
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
        sb.AppendLine($"CAMP stuck (no nearer their goal in 4 s, path found again): {life.StuckCount} (last: {life.LastStuck})");
        {
            var where = new Dictionary<Vector2Int, int>();
            foreach (var w in life.StuckAt) { var k = new Vector2Int(Mathf.RoundToInt(w.x / 3f) * 3, Mathf.RoundToInt(w.y / 3f) * 3); where[k] = (where.TryGetValue(k, out int c0) ? c0 : 0) + 1; }
            var list = new List<KeyValuePair<Vector2Int, int>>(where); list.Sort((x, y) => y.Value.CompareTo(x.Value));
            var t = new StringBuilder(); for (int i = 0; i < Mathf.Min(4, list.Count); i++) t.Append($"({list[i].Key.x}, {list[i].Key.y}) x{list[i].Value}  ");
            sb.AppendLine("CAMP   held up most at: " + t);
        }
        sb.AppendLine($"CAMP off the edge: {nearEdge} frames; furthest toward the cliff {worstEdge:0.0} m from the run's centre (the rail is at 24.1)");
        sb.AppendLine($"CAMP planted feet slid {slideDist * 100.0:0.0} cm over {walkDist:0} m walked ({(walkDist > 0 ? slideDist / walkDist * 100.0 : 0):0.00} cm per metre; worst {slideWorst * 100f:0} cm/s)");
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
        bool ok = overlapPeople == 0 && overlapHero < frameCount / 200 && nearEdge == 0 && (walkDist <= 0 || slideDist / walkDist < 0.02);
        EditorApplication.Exit(ok ? 0 : 1);
    }
    static string SlotName(int s) => s < 0 || s >= slotNames.Length ? "?" : slotNames[s];
    static readonly string[] slotNames = { "skin", "jacket", "jacket2", "pants", "head", "lens", "frame", "glove", "boot", "board", "dark", "pack", "hair", "gaiter", "metal", "pompom" };
}
