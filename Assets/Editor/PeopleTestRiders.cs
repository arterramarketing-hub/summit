using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

public static partial class PeopleTest
{
    // ---------------------------------------------------------------- the mountain's other riders

    class RiderStats
    {
        public int frames, riderFrames, drawnFrames, through, offRun, inHole, inThings, ghosts, solidOff, pops, popOuts;
        public float worstThrough, worstOff, worstSolid;
        public string firstThing = "", firstPop = "", firstThrough = "", firstOff = "";
        public long shown, people, draws, tris, active; public int drawsMax, peopleMax, shownMax; public long trisMax;
        public double ms, msMax;
        public readonly HashSet<int> seen = new HashSet<int>();
        public readonly List<string> holes = new List<string>();
        public readonly Dictionary<int, (Vector3 p, float air, int f, bool drawn)> last = new Dictionary<int, (Vector3, float, int, bool)>();
        public int belowCliff, checkedNear;
        public string worstParts = "";
        public readonly List<(Vector3 at, string what)> near = new List<(Vector3, string)>();
    }

    /// <summary>
    /// The other riders (MountainRiders): the camp for a minute and a half, the camera turning round; a fair day's ride
    /// down the whole mountain at 22 m/s, the camera chasing; a night storm's ride for a minute. Each frame, for every
    /// rider: on the groomed run (never off it, never in a crevasse or under a cliff), never under the drawn snow,
    /// never in a tree, a rock (the forest's own layout), a lift tower, a cabin or a floodlight; solid only while drawn,
    /// the solid where the pose is; and none coming into sight, or going from it, inside the view (not at its edges or
    /// at the edge of the drawing range): a pop. What they cost. Then the player into one: square on (the run's first:
    /// they go down and the player rides on, the free hit; the run's second: a crash too, ridercrash), a
    /// glancing touch (no crash: past it), passing close by (nothing). -executeMethod PeopleTest.Riders (no graphics
    /// needed). Logs/people-riders.txt; exit 1 on any of it.
    /// </summary>
    public static void Riders()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1.4f;
        TestBoot.Boot(game);
        var mr = MountainRiders.Current;
        if (mr == null) { Debug.LogError("RIDERS no MountainRiders"); TestHarness.Exit(1); return; }
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        cam.fieldOfView = 62f;
        const float dt = 1f / 30f;
        var sb = new StringBuilder();
        RiderStats st = null;
        mr.Appeared = r => { if (st != null && r.d < mr.Range * 0.9f && Deep(cam, r.pos)) { st.pops++; if (st.pops <= 3) st.firstPop += $"{r.look.name} {r.act} at {r.d:0} m, z {r.pos.z:0}, range {mr.Range:0}, {(st.last.TryGetValue(r.serial, out var w) ? $"a frame before at ({w.p.x:0.0}, {w.p.y:0.0}, {w.p.z:0.0}) {(Deep(cam, w.p) ? "deep in view" : "not deep in view")}, now ({r.pos.x:0.0}, {r.pos.y:0.0}, {r.pos.z:0.0}), air {r.air:0.0}, frame {w.f}" : "new")}; "; } };
        mr.Went = r => { if (st != null && r.active && r.d < mr.Range * 0.9f && Deep(cam, r.pos)) { st.popOuts++; if (st.firstPop == "") st.firstPop = $"gone: {r.look.name} {r.act} at {r.d:0} m"; } };

        // the camp
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        var camp = (Summit.Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
        var life = camp.GetComponent<CampLife>();
        Vector3 centre = life != null && life.P("door") != null ? life.P("door").at : new Vector3(Slope.Centre(-60f), Slope.DrawnHeight(Slope.Centre(-60f), -60f), -60f);
        cam.transform.SetPositionAndRotation(centre + Vector3.up * 1.7f, Quaternion.Euler(4f, 0f, 0f));
        mr.Step(dt, cam);   // (a new mountain: everyone placed, in sight or not: the world is new)
        st = new RiderStats();
        for (int f = 0; f < 2700; f++)
        {
            float yaw = f * dt * 360f / 45f;
            cam.transform.SetPositionAndRotation(centre + Vector3.up * 1.7f, Quaternion.Euler(4f, yaw, 0f));
            mr.Step(dt, cam);
            Check(mr, cam, st, f);
        }
        Verify(st, rider, props);
        Report(sb, "the camp, 90 s, the camera turning", st, mr);
        var all = st;

        // a fair day's ride down the mountain
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        st = null;
        {
            float x0 = Slope.Centre(0f);
            cam.transform.SetPositionAndRotation(new Vector3(x0, Slope.DrawnHeight(x0, 0f), 0f) + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.28f, 1f)));
            mr.Step(dt, cam);   // (the cut from the camp to the start: a new view, nothing to pop)
        }
        st = new RiderStats();
        const float speed = 22f;
        int fr = 0;
        for (float z = 0f; z < Slope.FinishZ; z += speed * dt, fr++)
        {
            float x = Slope.Centre(z);
            rider.ResetAt(x, z); rider.Stop();
            var at = new Vector3(x, Slope.DrawnHeight(x, z), z);
            cam.transform.SetPositionAndRotation(at + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.28f, 1f)));
            mr.Step(dt, cam);
            Check(mr, cam, st, fr);
        }
        Verify(st, rider, props);
        Report(sb, $"a clear day's ride, the top to the finish at {speed} m/s", st, mr);
        Add(all, st);

        // a night storm
        Weather.TestChoice = 4; TimeOfDay.TestClock = 3.4f;
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { true });
        if (Weather.Active != null) typeof(Weather).GetMethod("Roll", NP).Invoke(Weather.Active, null);   // (as its Update does on a new mountain)
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        {
            float x0 = Slope.Centre(0f);
            cam.transform.SetPositionAndRotation(new Vector3(x0, Slope.DrawnHeight(x0, 0f), 0f) + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.28f, 1f)));
            mr.Step(dt, cam);   // (the new mountain's riders placed)
        }
        var stormStats = new RiderStats();
        st = stormStats;
        fr = 0;
        for (float z = 0f; z < 60f * speed; z += speed * dt, fr++)
        {
            float x = Slope.Centre(z);
            rider.ResetAt(x, z); rider.Stop();
            var at = new Vector3(x, Slope.DrawnHeight(x, z), z);
            cam.transform.SetPositionAndRotation(at + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.28f, 1f)));
            mr.Step(dt, cam);
            Check(mr, cam, st, fr);
        }
        Verify(st, rider, props);
        Report(sb, $"a night storm's ride, 60 s (the weather {(Weather.Active != null ? Weather.Active.Base.ToString() : "?")}, {TimeOfDay.Current})", st, mr);
        Add(all, st);
        st = null;

        // the player into one
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1.4f;
        Physics.simulationMode = SimulationMode.Script;
        string lastCrash = "";
        rider.Crash += why => lastCrash = why;
        MountainRiders.TestWant = 1;
        // (on a cruising stretch, nothing of the park's near: its features steer the board)
        float zt = 100f;
        bool Plain(float z)
        {
            float x = Slope.Centre(z), h0 = Slope.Height(x, z - 18f), h1 = Slope.Height(x, z + 4f);
            for (float zz = z - 18f; zz <= z + 4f; zz += 2f)
            {
                if (Course.At(zz) != Course.Section.Cruise || Course.NearFeature(x, zz, 12f) || !MountainRiders.Clear(x, zz, 3f) || Mathf.Abs(Slope.Kicker(x, zz)) > 0f) return false;
                if (Mathf.Abs(Slope.Height(x, zz) - Mathf.Lerp(h0, h1, (zz - z + 18f) / 22f)) > 0.25f) return false;   // (an even pitch: no roll to fly off)
                if (Mathf.Abs(Slope.Centre(zz) - x) > 0.5f) return false;   // (straight)
            }
            return true;
        }
        while (zt < Slope.FinishZ - 100f && !Plain(zt)) zt += 7f;
        var spot = new Vector3(Slope.Centre(zt), Slope.DrawnHeight(Slope.Centre(zt), zt), zt);
        var results = new List<string>();
        bool headOn = false, side = true, past = true;
        foreach (var (name, off, ski) in new[] { ("square on, a boarder", 0f, false), ("square on, a skier", 0.1f, true), ("a glancing touch", 0.48f, false), ("past, close by", 1.3f, true) })
        {
            var ai = mr.TestOnly(spot, ski);
            rider.ResetAt(spot.x + off, spot.z - 16f); body.ResetVisual(); rider.Begin();
            // (ridercrash: the first rider hit in a run is free, the warning; the skier square on is the run's second)
            if (ski && off < 0.2f) rider.FreeHitUsed = true;
            rider.Heading = 0f;
            rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(spot.x + off, spot.z - 16f)).normalized * 15f;
            lastCrash = "";
            var aim = spot + Vector3.right * off;
            string startInfo = $"spot ({spot.x:0.0}, {spot.z:0.0}), it at ({ai.pos.x:0.0}, {ai.pos.z:0.0}), player from ({rider.transform.position.x:0.0}, {rider.transform.position.z:0.0})";
            bool touched = false; float nearest = 99f, lateral = 99f;
            var trace = new List<string>();
            for (int i = 0; i < 480 && !rider.Crashed; i++)
            {
                Vector3 p = rider.transform.position;
                rider.Heading = Mathf.Clamp(Mathf.Atan2(aim.x - p.x, Mathf.Max(0.5f, aim.z - p.z)), -1.4f, 1.4f);
                rider.Steer = 0f;
                var vel = rider.Body.linearVelocity;   // (held to its line: the test's thumb)
                vel.x = Mathf.Clamp((aim.x - p.x) * 4f, -3f, 3f) + vel.z * (Slope.Centre(p.z + 1f) - Slope.Centre(p.z)) * 0f;
                rider.Body.linearVelocity = vel;
                if (!rider.Crashed && rider.Running) rider.TestContacts(1f / 120f);
                rider.Tick(1f / 120f); body.PhysicsStep(); Physics.Simulate(1f / 120f);
                if (i % 4 == 0)
                {
                    cam.transform.SetPositionAndRotation(p + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.28f, 1f)));
                    mr.Step(1f / 30f, cam);
                }
                var d = rider.transform.position - ai.pos; d.y = 0f;
                if (d.magnitude < 1.6f && off < 0.2f) trace.Add($"RIDERS     step {i}: the player at ({rider.transform.position.x - ai.pos.x:0.00}, {rider.transform.position.y - ai.ground:0.00}, {rider.transform.position.z - ai.pos.z:0.00}) from it, v {rider.Velocity.z:0.0}, crashed {rider.Crashed}; its solid {(ai.solid != null ? $"on {ai.solid.gameObject.activeInHierarchy} at ({ai.solid.transform.position.x - ai.pos.x:0.00}, {ai.solid.transform.position.y - ai.ground:0.00}, {ai.solid.transform.position.z - ai.pos.z:0.00}) h {ai.solid.height:0.00}" : "none")}, {ai.act}, drawn {ai.drawn}");
                if (d.magnitude < nearest) { nearest = d.magnitude; lateral = Mathf.Abs(d.x); }
                if (ai.act == MountainRiders.Act.Fall) touched = true;
                if (p.z > spot.z + 4f) break;
            }
            string what = rider.Crashed ? $"crashed ({lastCrash})" : "rode on";
            if (off < 0.2f && ski && !rider.Crashed) results.AddRange(trace);   // (the run's second square on that didn't crash: how it went)
            if (nearest > 1.5f) results.Add("RIDERS   (" + startInfo + $", it ended at ({ai.pos.x:0.0}, {ai.pos.z:0.0}) {ai.act}, active {ai.active}, the player at ({rider.transform.position.x:0.0}, {rider.transform.position.z:0.0}))");
            results.Add($"RIDERS the player into one, {name} ({off:0.00} m off its middle, 54 km/h): {what}; came within {nearest:0.00} m of it ({lateral:0.00} m to its side); it {(touched ? "went down" : "stayed up")}");
            if (off > 0.2f && off < 1f && rider.Crashed) side = false;
            if (off > 1f && rider.Crashed) past = false;
        }
        // (square on, the run's first: they go down, the player rides on; the run's second: the player goes down too)
        headOn = results.Exists(l => l.Contains("into one, square on, a boarder") && l.Contains("rode on") && l.Contains("went down"))
              && results.Exists(l => l.Contains("into one, square on, a skier") && l.Contains("crashed (Skier!)"));
        MountainRiders.TestWant = -1;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        foreach (var l in results) sb.AppendLine(l);

        sb.AppendLine($"RIDERS what they did, all told: {MountainRiders.Laps} laps begun; ended at a lift's bottom {MountainRiders.LapsAtLift}, above where the run is blocked across {MountainRiders.LapsBlocked}; stopped alone {MountainRiders.StopsAlone}, with friends {MountainRiders.StopsGroup} times; sat down {MountainRiders.SatDown}; fell {MountainRiders.Falls}; hopped {MountainRiders.Hops}; found no place to stop {MountainRiders.NoPlace} (places refused: not to stand on {MountainRiders.NoStand}, the way there {MountainRiders.NoWay}: something in it {MountainRiders.WayClear}, a drop {MountainRiders.WayDrop}); Clear refused: past the finish {MountainRiders.Refused[0]}, off the edge {MountainRiders.Refused[1]}, a kicker {MountainRiders.Refused[2]}, the park {MountainRiders.Refused[3]}, a crack {MountainRiders.Refused[4]}, a step {MountainRiders.Refused[5]}, lights or cabins {MountainRiders.Refused[6]}, a thing {MountainRiders.Refused[7]}");
        bool ok = all.through == 0 && all.offRun == 0 && all.inHole == 0 && all.inThings == 0 && all.ghosts == 0 && all.solidOff == 0 && all.pops == 0 && all.popOuts == 0 && headOn && side && past;
        sb.AppendLine(ok ? "RIDERS all well" : "RIDERS FAILED");
        Debug.Log(sb.ToString());
        System.IO.File.WriteAllText("Logs/people-riders.txt", sb.ToString());
        Weather.TestChoice = -1; TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(ok ? 0 : 1);
    }

    static readonly bool Trace = System.Environment.GetEnvironmentVariable("RIDERS_TRACE") == "1";
    static readonly Dictionary<int, Queue<string>> hist = new Dictionary<int, Queue<string>>();
    static readonly HashSet<int> traced = new HashSet<int>();

    /// <summary>Well inside the picture (not at its edges, where things come in and go out as the view moves).</summary>
    static bool Deep(Camera cam, Vector3 at)
    {
        var v = cam.WorldToViewportPoint(at + Vector3.up * 0.9f);
        return v.z > 0f && v.x > 0.08f && v.x < 0.92f && v.y > 0.08f && v.y < 0.92f;
    }

    static void Check(MountainRiders mr, Camera cam, RiderStats st, int f)
    {
        st.frames++;
        foreach (var r in mr.All)
        {
            if (!r.active) continue;
            st.riderFrames++; st.seen.Add(r.serial);
            float x = r.pos.x, z = r.pos.z;
            if (r.drawn)
            {
                st.drawnFrames++;
                float under = Slope.DrawnHeight(x, z) - r.pos.y;
                if (under > 0.03f) { st.through++; if (under > st.worstThrough) { st.worstThrough = under; st.firstThrough = $"{r.look.name} {r.act} {under * 100f:0} cm under at z {z:0}"; } }
            }
            float off = Mathf.Abs(x - Slope.Centre(z)) - Slope.HalfAt(z);
            if (off > 0f) { st.offRun++; if (off > st.worstOff) { st.worstOff = off; st.firstOff = $"{r.look.name} {r.act} {off:0.0} m past the edge at z {z:0}"; } }
            bool crev = Course.CrevasseNear(x, z, 0.3f), below = false;
            if (Deadly.Below(x, z)) st.belowCliff++;
            // (stepped down a wall: more drop in a frame than the hill gives)
            if (st.last.TryGetValue(r.serial, out var was) && was.f == f - 1 && r.drawn && was.drawn)
            {
                float run = new Vector2(x - was.p.x, z - was.p.z).magnitude, drop = was.p.y - r.pos.y;
                if (was.air <= 0f && r.air <= 0f && run > 0.05f && drop > run * 1.5f + 0.2f) { below = true; }
            }
            st.last[r.serial] = (r.pos, r.air, f, r.drawn);
            if (Trace)
            {
                if (!hist.TryGetValue(r.serial, out var q)) hist[r.serial] = q = new Queue<string>();
                q.Enqueue($"f{f} {r.act} ({x - Slope.Centre(z):0.00}, {z:0.0}) y {r.pos.y:0.00} g {r.ground:0.00} air {r.air:0.00} airV {r.airV:0.0} gV {r.groundV:0.0} sp {r.speed:0.0} hd {r.heading:0.00} lane {r.lane:0.0}/{r.laneMid:0.0} kick {Slope.Kicker(x, z):0.00} keep {Lines.Keep(x, z, 0f)} grade {Lines.Grade(z):0.00} near {r.near != null} drawn {r.drawn}");
                while (q.Count > 40) q.Dequeue();
                if (r.air > 3f && traced.Add(r.serial)) Debug.Log("RIDERS-TRACE " + r.look.name + "\n  " + string.Join("\n  ", q));
            }
            if (r.air > 3f) below = true;   // (flown off a drop: more than a hop or a roll)
            if (crev || below) { st.inHole++; if (st.holes.Count < 6 && (st.holes.Count == 0 || !st.holes[st.holes.Count - 1].StartsWith(r.look.name + " "))) st.holes.Add($"{r.look.name} {r.act} at ({x - Slope.Centre(z):0.0}, {z:0}) {(crev ? $"on a crack at {r.speed:0.0} m/s" : "")}{(below ? (r.air > 3f ? $"flying {r.air:0} m up off a drop (y {r.pos.y:0.0}, snow {r.ground:0.0}, drawn snow {Slope.DrawnHeight(x, z):0.0}, up {r.airV:0.0} m/s, snow's rate {r.groundV:0.0}, {r.speed:0.0} m/s, drawn {r.drawn}, d {r.d:0}, track {r.trail != null})" : "down a wall") : "")} [{Course.At(z)}, half {Slope.HalfAt(z):0}]"); }
            if (f % 3 == r.id % 3 && InThing(x, z, out string what)) { st.near.Add((new Vector3(x, r.pos.y, z), $"{r.look.name} {r.act} at ({x - Slope.Centre(z):0.0}, {z:0}): {what}")); }
            if (r.solid != null)
            {
                if (!r.drawn || !r.solid.gameObject.activeInHierarchy) st.ghosts++;
                var foot = r.solid.transform.position;
                float sep = new Vector2(foot.x - x, foot.z - z).magnitude + Mathf.Abs(foot.y - (r.ground + r.air));
                if (sep > 0.1f) { st.solidOff++; st.worstSolid = Mathf.Max(st.worstSolid, sep); }
            }
        }
        st.shown += mr.Shown; st.shownMax = Mathf.Max(st.shownMax, mr.Shown);
        st.people += mr.PeopleShown; st.peopleMax = Mathf.Max(st.peopleMax, mr.PeopleShown);
        st.draws += mr.DrawCalls; st.drawsMax = Mathf.Max(st.drawsMax, mr.DrawCalls);
        st.tris += mr.Triangles; st.trisMax = System.Math.Max(st.trisMax, mr.Triangles);
        st.active += mr.Active;
        st.ms += mr.LastMs; if (mr.LastMs > st.msMax) { st.msMax = mr.LastMs; st.worstParts = mr.LastParts; }
    }

    /// <summary>In a tree or a rock (the forest's layout), a lift's tower or station, a cabin, a floodlight.</summary>
    static bool InThing(float x, float z, out string what)
    {
        what = "";
        int cx0 = Mathf.FloorToInt((x - Forest.Cell) / Forest.Cell), cx1 = Mathf.FloorToInt((x + Forest.Cell) / Forest.Cell);
        int cz0 = Mathf.FloorToInt((z - Forest.Cell) / Forest.Cell), cz1 = Mathf.FloorToInt((z + Forest.Cell) / Forest.Cell);
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cz = cz0; cz <= cz1; cz++)
            {
                var k = Forest.At(cx, cz, out var p, out float size, out _);
                if (k == Forest.Kind.None) continue;
                float reach = 0.3f + (k == Forest.Kind.Rock ? size : 0.25f + 0.2f * size);
                float d = new Vector2(p.x - x, p.z - z).magnitude;
                if (d < reach) { what = $"{k} (size {size:0.0}) {d:0.0} m off"; return true; }
            }
        foreach (var l in LiftLine.Lifts)
        {
            if (l.foot != null) foreach (var t in l.foot) if (new Vector2(t.x - x, t.z - z).magnitude < 1.2f) { what = "a lift tower"; return true; }
            foreach (var e in new[] { l.top, l.bottom }) if (new Vector2(e.x - x, e.z - z).magnitude < 5f) { what = "a lift station"; return true; }
        }
        if (Hamlets.Taken(x, z)) { what = "a cabin"; return true; }
        if (NightLights.Taken(x, z)) { what = "a floodlight"; return true; }
        return false;
    }

    /// <summary>
    /// Where the forest's layout put a rider near a tree or a rock (its sizes are rough): the forest built round that
    /// place, as it is round the player, and the rider's body (a capsule) tested against what the player would hit there.
    /// </summary>
    static void Verify(RiderStats st, Rider player, Forest props)
    {
        var done = new HashSet<string>();
        foreach (var (at, what) in st.near)
        {
            string who = what.Substring(0, what.IndexOf(' '));
            if (!done.Add(who) || done.Count > 40) continue;
            player.ResetAt(at.x, at.z - 3f); player.Stop();
            props.Prewarm();
            Physics.SyncTransforms();
            var g = new Vector3(at.x, Slope.DrawnHeight(at.x, at.z), at.z);
            foreach (var c in Physics.OverlapCapsule(g + Vector3.up * 0.35f, g + Vector3.up * 1.3f, 0.3f))
            {
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob == null || c.name == "RiderSolid" || c.attachedRigidbody == player.Body) continue;
                st.inThings++;
                if (st.firstThing == "") st.firstThing = what + $" (the {ob.label}'s own hit shape)";
                break;
            }
        }
        st.checkedNear = done.Count;
    }

    static void Add(RiderStats a, RiderStats b)
    {
        a.through += b.through; a.offRun += b.offRun; a.inHole += b.inHole; a.inThings += b.inThings; a.ghosts += b.ghosts; a.solidOff += b.solidOff; a.pops += b.pops; a.popOuts += b.popOuts;
    }

    static void Report(StringBuilder sb, string what, RiderStats s, MountainRiders mr)
    {
        float n = Mathf.Max(1, s.frames);
        sb.AppendLine($"RIDERS {what}: {s.frames} frames, {s.active / n:0} riding on average ({s.seen.Count} different riders), {s.shown / n:0.0} drawn on average ({s.shownMax} at most), as people {s.people / n:0.0} ({s.peopleMax} at most)");
        sb.AppendLine($"RIDERS   Clear() {MountainRiders.ClearCalls / n:0.0} calls a frame, {MountainRiders.ClearMs / n:0.000} ms");
        MountainRiders.ClearCalls = 0; MountainRiders.ClearMs = 0;
        sb.AppendLine($"RIDERS   draw calls {s.draws / n:0.0} on average, {s.drawsMax} at most; triangles {s.tris / n:0} on average, {s.trisMax} at most; their frame (editor) {s.ms / n:0.000} ms on average, {s.msMax:0.00} ms at most ({s.worstParts})");
        sb.AppendLine($"RIDERS   under the snow {s.through} rider-frames{(s.through > 0 ? " (" + s.firstThrough + ")" : "")}; off the run {s.offRun}{(s.offRun > 0 ? " (" + s.firstOff + ")" : "")}; on a crack, down a wall or off a drop {s.inHole} (on the snow under a cliff, which is fine: {s.belowCliff}); in a tree, rock or thing {s.inThings} (of {s.near.Count} rider-frames the forest's layout put near one, {s.checkedNear} riders looked at with the hit shapes built there){(s.inThings > 0 ? ": " + s.firstThing : "")}");
        foreach (var h in s.holes) sb.AppendLine("RIDERS     " + h);
        sb.AppendLine($"RIDERS   solid while not drawn {s.ghosts}; solid off the pose {s.solidOff} (worst {s.worstSolid * 100f:0} cm); popped into sight {s.pops}, out of it {s.popOuts}{(s.firstPop != "" ? " (" + s.firstPop + ")" : "")}");
    }
}
