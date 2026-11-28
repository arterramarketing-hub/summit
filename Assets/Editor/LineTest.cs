using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The feature lines (Lines), ridden.
///
/// LineTest.Plan: every line on a few mountains, hit by hit: what it is, where, how big, and its flights as laid (where
/// the slow, typical, fast and popped flights come down and how hard, the air at the typical speed, and the slow end's
/// time on the snow to the next lip).
///
/// LineTest.Run: on seven mountains, a bot rides every line down its lane:
///   - at the typical speed (44 m/s, held on the snow): every hit taken off from (or slid, a jib) and landed, in order,
///     none of them hard, no crash, and the whole line chained (Rider.LineHit every hit, "Full Line!");
///   - at the slow and fast ends (37 and 48 m/s, held): every hit landed, no crash;
///   - at the typical speed, popping off every lip: no crash;
///   - down the half of the piste the line leaves clear (riding round it): nothing of the line under it, no crash;
///   - and the hill's riders (MountainRiders.Clear) never pick a spot on its lane.
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod LineTest.Run -logFile line.log
///      (lines start LINE; failures LINE-FAIL)
/// </summary>
public static class LineTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props; static SummitGame game;
    static int fails, rides;
    static string lastCrash, rough;
    static readonly string trace = System.Environment.GetEnvironmentVariable("LINE_TRACE") ?? "";

    static readonly int[] seeds = { 1000, 1077, 1154, 1231, 1308, 2131, 1777 };

    static void Boot()
    {
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        r = game.rider;
        body = game.body;
        terrain = game.terrain;
        props = game.props;
        r.input = null;
        r.Crash += why => lastCrash = why;
        r.RoughLanding += why => { if (rough == null) rough = why; };
    }

    static void Done()
    {
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0);
    }

    static void Mountain(int seed)
    {
        TestBoot.NewMountain(game, seed, terrain, props);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) { var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt; }
    }

    static void Fail(string what) { fails++; Debug.Log("LINE-FAIL " + what); }

    // ---------------------------------------------------------------- the plan

    public static void Plan()
    {
        Boot();
        try
        {
            int total = 0, hits = 0; var kinds = new Dictionary<Lines.Kind, int>();
            if (System.Environment.GetEnvironmentVariable("LINE_WHY") == "1") Lines.Told = s => Debug.Log("LINE-TOLD " + s);
            foreach (int seed in seeds) { Mountain(seed); var all = Lines.All(); total += all.Count; foreach (var L in all) { hits += L.hits.Length; foreach (var h in L.hits) { kinds.TryGetValue(h.kind, out int c); kinds[h.kind] = c + 1; } } Describe(seed, all); }
            string ks = ""; foreach (var kv in kinds) ks += $" {kv.Key} {kv.Value}";
            Debug.Log($"LINE-PLAN {total} lines on {seeds.Length} mountains, {hits} hits:{ks}");
        }
        catch (System.Exception e) { Debug.LogError("LINE exception " + e); fails++; }
        Done();
    }

    static void Describe(int seed, List<Lines.Line> all)
    {
        Debug.Log($"LINE-PLAN seed{seed}: {all.Count} line(s)");
        if (System.Environment.GetEnvironmentVariable("LINE_WHY") == "1")
            for (int c = 1; c < 5; c++)
            {
                float zb = Course.First + c * Course.Cycle; string s = ""; string last = "x";
                for (float z = zb + Course.PipeAt - 140f; z < zb + Course.Cycle + 80f; z += 10f) { string w = Lines.Why(z) ?? "FREE"; if (w != last) { s += $" {z - zb:0}:{w}"; last = w; } }
                Debug.Log($"LINE-WHY seed{seed} cycle {c} (z {zb:0}):{s}");
            }
        foreach (var L in all)
        {
            Debug.Log($"LINE-PLAN seed{seed} line {L.id} ({L.style}, cycle {L.cycle}, side {L.side:+0;-0}): z {L.z0:0}-{L.z1:0} ({L.z1 - L.z0:0} m), {L.hits.Length} hits, altitude {Slope.AltitudeFeet(L.z0):0} ft");
            foreach (var h in L.hits)
            {
                if (h.IsJib) { Debug.Log($"LINE-PLAN   {h.index} {h.kind} z {h.z0:0}-{h.EndZ:0} lane {h.lane:0.0}, {h.len:0} m long, {h.H:0.00} m up; then {h.snowLo:0.00} s on the snow (slow) to the next"); continue; }
                Debug.Log($"LINE-PLAN   {h.index} {h.kind} z {h.z0:0} lip {h.LipZ:0} end {h.EndZ:0} lane {h.lane:0.0}{(h.yaw != 0f ? $" turned {h.yaw * Mathf.Rad2Deg:0} deg" : "")}: H {h.H:0.00} Lr {h.Lr:0.0} [{h.P1:0.0} {h.P2:0.0} {h.P3:0.0} {h.P4:0.0}]; lands " +
                          $"slow {h.landLo - h.LipZ:0} m ({h.impLo:0.0} m/s), typical {h.landD - h.LipZ:0} m ({h.impD:0.0}, {h.airD:0.00} s up), fast {h.landHi - h.LipZ:0} m ({h.impHi:0.0}), popped {h.landPop - h.LipZ:0} m ({h.impPop:0.0}); then {h.snowLo:0.00} s on the snow (slow) to the next lip");
            }
        }
    }

    /// <summary>The lifts near a blind roll's lip (LINE_SEED, LINE_DROP: the fall line's segment), for a check.</summary>
    public static void LiftProbe()
    {
        Boot();
        int seed = int.Parse(System.Environment.GetEnvironmentVariable("LINE_SEED") ?? "1231"), s = int.Parse(System.Environment.GetEnvironmentVariable("LINE_DROP") ?? "40");
        Mountain(seed);
        if (Slope.DropOf(s, out var d)) Debug.Log($"LINE-PROBE drop{s} lip z{d.LipZ:0} blind {d.blind}, edges at x-c +-{Slope.HalfAt(d.LipZ - 4f):0.0}");
        foreach (var l in LiftLine.Lifts)
            if (l.ZMax > d.LipZ - 150f && l.ZMin < d.LipZ + 150f)
                Debug.Log($"LINE-PROBE lift {l.k}{(l.second ? "b" : "")}: z {l.ZMin:0}-{l.ZMax:0}, top ({l.top.x - Slope.Centre(l.top.z):0} from the centre, z{l.top.z:0}), dir ({l.dir.x:0.00},{l.dir.z:0.00}), hill {(l.hill != null)}");
        for (int e = -1; e <= 1; e += 2) { float z = d.LipZ - 4f, x = Slope.Centre(z) + e * (Slope.HalfAt(z) + 0.8f); Debug.Log($"LINE-PROBE edge {e}: taken {LiftLine.Taken(x, z, 1.5f)} onHill {LiftLine.OnHill(x, z, 1.5f)} hillOrLane {LiftLine.HillOrLane(x, z, 1.5f)}"); }
        Done();
    }

    // ---------------------------------------------------------------- the rides

    public static void Run()
    {
        Boot();
        int lines = 0;
        try
        {
            foreach (int seed in seeds)
            {
                Mountain(seed);
                var all = Lines.All();
                Describe(seed, all);
                foreach (var L in all)
                {
                    lines++;
                    RideLine(seed, L, Lines.Speed, false, true);
                    RideLine(seed, L, Lines.SpeedLo, false, false);
                    RideLine(seed, L, Lines.SpeedHi, false, false);
                    RideLine(seed, L, Lines.Speed, true, false);
                    RideFree(seed, L, true);
                    RideFree(seed, L, false);
                    HillRiders(seed, L);
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("LINE exception " + e); fails++; }
        Debug.Log($"LINE done: {lines} lines, {rides} rides, {fails} failure(s)");
        Done();
    }

    static void Start(float x, float z, float v0)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
        terrain.Prewarm(); props.Prewarm();
        lastCrash = null; rough = null;
    }

    static void Step(int n)
    {
        TestHarness.Step(r, body, Dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
    }

    /// <summary>The lane's line at z: the next hit's (a jib's own line as it nears it).</summary>
    public static float LaneX(Lines.Line L, List<Course.Feature> jibs, float z)
    {
        for (int i = 0; i < L.hits.Length; i++)
        {
            var h = L.hits[i];
            if (z > h.EndZ + 2f) continue;
            if (h.IsJib)
                foreach (var f in jibs)
                    if (f.id == h.jib)
                    {
                        float t = Mathf.Clamp01((z - f.a.z) / (f.b.z - f.a.z));
                        float fx = Mathf.Lerp(f.a.x, f.b.x, t);
                        // (above it: its start's place across the run, carried up the hill with the run)
                        if (z < f.a.z) fx = Slope.Centre(z) + (f.a.x - Slope.Centre(f.a.z));
                        return fx;
                    }
            return Slope.Centre(z) + h.lane;
        }
        return Slope.Centre(z) + L.hits[L.hits.Length - 1].lane;
    }

    /// <summary>Down the line's lane at speed v (held on the snow), popping off every lip if pop.</summary>
    static void RideLine(int seed, Lines.Line L, float v, bool pop, bool typical)
    {
        rides++;
        string name = $"seed{seed} line {L.id} {L.style} v{v:0}{(pop ? " popped" : "")}";
        var jibs = new List<Course.Feature>();
        Lines.JibsOf(L.cycle, jibs);
        float z0 = L.z0 - 30f;   // (riding in from above the line's entry)
        Start(LaneX(L, jibs, z0), z0, v);
        var credited = new List<int>(); int chain = 0; bool full = false;
        string links = "";
        System.Action<int, int, int> onHit = (line, i, c) => { if (line == L.id) { credited.Add(i); chain = c; } };
        System.Action<TrickLink> onLink = l => { if (l.name.StartsWith("Line") || l.name.StartsWith("Full")) links += $" [{l.name} {l.points}]"; if (l.name == "Full Line!") full = true; };
        r.LineHit += onHit; r.Link += onLink;
        var hardAt = new List<string>(); var air = new List<string>();
        float offZ = 0f;
        System.Action<float> landed = imp => { if (imp > 18f) hardAt.Add($"{imp:0.0} m/s at z{r.Z:0}"); if (r.AirTime > 0.3f) air.Add($"z{offZ:0}-{r.Z:0}:{r.AirTime:0.00}s/{imp:0.0}"); };
        r.Landed += landed;
        bool crashed = false; Vector3 crashAt = Vector3.zero; int n = 0;
        float lastPopZ = -1f;
        try
        {
            for (float t = 0f; t < 30f && r.Z < L.z1 + 25f; t += Dt, n++)
            {
                float lx = LaneX(L, jibs, r.Z + 8f);
                r.Steer = CarveBot.Onto(r, z => LaneX(L, jibs, z), 2.4f, 12f, 1.5f);   // (looked at just ahead: further, and on a slant it holds the line that far ahead, off it here)
                if (!r.Airborne && !r.Grinding)
                {
                    // (the speed held on the snow: a rider at this speed, not one drag lets go to its own)
                    var vel = r.Body.linearVelocity;
                    if (vel.magnitude > 1f) r.Body.linearVelocity = vel.normalized * v;
                    if (pop)
                        foreach (var h in L.hits)
                            if (h.Air && Mathf.Abs(r.Z - (h.LipZ - Lines.PopBefore)) < 0.5f && h.LipZ != lastPopZ) { r.QueuePop(0.8f); lastPopZ = h.LipZ; }
                }
                bool was = r.Airborne; float zWas = r.Z;
                if (trace == $"{seed}:{L.id}:{v:0}" && n % 15 == 0) Debug.Log($"LINE-TRACE z{r.Z:0.0} x-lane {r.X - lx:0.00} (lane x {lx - Slope.Centre(r.Z):0.0}) h {r.Heading * Mathf.Rad2Deg:0} steer {r.Steer:0.00} air {r.Airborne} grind {r.Grinding} v {r.Speed:0.0} y-snow {r.transform.position.y - Slope.Height(r.X, r.Z):0.00}");
                Step(n);
                foreach (var f in jibs)
                    if (zWas < f.a.z && r.Z >= f.a.z)
                        air.Add($"(onto jib {f.kind} at z{f.a.z:0}: {r.X - f.a.x:0.00} m off its line, {r.transform.position.y - 0.25f - (f.a.y - f.height):0.00} m over the snow, heading {r.Heading * Mathf.Rad2Deg:0} deg, grinding={r.Grinding}, air={r.Airborne})");
                if (!was && r.Airborne) offZ = r.Z;
                if (r.Crashed) { crashed = true; crashAt = r.transform.position; break; }
            }
        }
        finally { r.LineHit -= onHit; r.Link -= onLink; r.Landed -= landed; }
        string got = string.Join(",", credited);
        Debug.Log($"LINE {name}: hits landed in the chain [{got}] of {L.hits.Length}, chain {chain}, full={full}, airs {string.Join(" ", air)}, hard {hardAt.Count}, " +
                  $"{(crashed ? $"CRASHED '{lastCrash}' at z{crashAt.z:0} x-lane {crashAt.x - Slope.Centre(crashAt.z):0.0}" : "rode out")}{links}");
        if (crashed) Fail($"{name}: crashed '{lastCrash}' at z{crashAt.z:0} (x {crashAt.x - Slope.Centre(crashAt.z):0.0} from the centre)");
        else if (!pop)
        {
            // every hit, in order
            for (int i = 0; i < L.hits.Length; i++)
                if (!credited.Contains(i)) Fail($"{name}: hit {i} ({L.hits[i].kind} at z{L.hits[i].z0:0}) not landed in the chain (got [{got}])");
            if (typical)
            {
                if (hardAt.Count > 0) Fail($"{name}: hard landing(s) at the typical speed: {string.Join(", ", hardAt)}");
                if (!full) Fail($"{name}: not chained as one line (chain {chain} of {L.hits.Length})");
            }
        }
    }

    /// <summary>Down the half of the piste the line leaves clear: nothing of the line on that side, and it rides out.</summary>
    static void RideFree(int seed, Lines.Line L, bool fast)
    {
        rides++;
        float v = fast ? Lines.SpeedHi : 30f;
        string name = $"seed{seed} line {L.id} round it v{v:0}";
        float off = -L.side * 10f;
        // nothing of the line on the clear side
        for (float z = L.z0; z < L.z1; z += 2f)
            for (float a = -L.side * 18f; Mathf.Abs(a) >= 2.5f; a += L.side * 1.5f)
                if (Mathf.Abs(Lines.Height(Slope.Centre(z) + a, z)) > 0.01f) { Fail($"{name}: the line reaches the clear side at z{z:0}, {a:0.0} from the centre"); z = L.z1; break; }
        Start(Slope.Centre(L.z0) + off, L.z0, v);
        bool crashed = false; int n = 0;
        for (float t = 0f; t < 30f && r.Z < L.z1 + 10f; t += Dt, n++)
        {
            r.Steer = CarveBot.Onto(r, z => Slope.Centre(z) + off, 1.4f, 8f, 6f);
            Step(n);
            if (r.Crashed) { crashed = true; break; }
        }
        Debug.Log($"LINE {name}: {(crashed ? $"CRASHED '{lastCrash}' at z{r.Z:0}" : "rode out")}");
        // (a crevasse or a tree is the mountain, not the line: there are none on the run here)
        if (crashed) Fail($"{name}: crashed '{lastCrash}' at z{r.Z:0}");
    }

    /// <summary>The hill's riders never choose a spot on the line's lane.</summary>
    static void HillRiders(int seed, Lines.Line L)
    {
        int bad = 0;
        for (float z = L.z0; z < L.z1; z += 3f)
            foreach (var h in L.hits)
            {
                if (z < h.z0 || z > h.EndZ) continue;
                if (MountainRiders.Clear(Slope.Centre(z) + h.lane, z)) bad++;
            }
        if (bad > 0) Fail($"seed{seed} line {L.id}: the hill's riders may stand on its lane at {bad} spot(s)");
    }
}
