using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// The points, measured.
///
/// ScoreTest.Run: airs and grinds with scripted inputs (no input, taps only, directions only, both, more rotation
/// than the air has room for; each ride), printing the trick that was built, its points, how the landing went and
/// what the combo banked. The whole table is ridden twice and the two must match line for line; no line's ride
/// changes under it, and nothing but the snowboard is given a snowboard's grind names (rides2: a mission done
/// mid-grind used to put the toboggan back on the board, Boardslide and Noseslide on a sled, in one pass only).
///
/// ScoreTest.Runs: whole runs by bots of three kinds (hands off, steering only, some trick input, heavy trick
/// input) on five mountains: score, what was banked, the longest combo, falls.
///
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod ScoreTest.Run -logFile score.log
///      (lines start SCORE; ScoreTest.Runs lines start SCORERUN)
/// </summary>
public static class ScoreTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static readonly List<string> lines = new List<string>();
    static string links, crash, rough; static int fails, landings, firstBank;

    struct In { public float x, y; public bool hold, tap; }
    delegate In Script(float t, float t0);   // air (or grind) time now, and a step ago
    static bool At(float t, float t0, float when) => t0 < when && t >= when;

    static void Boot()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        r.Link += l => links += $" [{l.name} {l.points}{(l.sketchy ? " sketchy" : "")}]";
        r.Crash += why => crash = why;
        r.RoughLanding += why => rough = why;
        r.Landed += impact => landings++;
        game.Points.BankedNow += (total, pot, mult) => { if (firstBank < 0) firstBank = total; };
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

    static void Ride(RideKind kind, bool tube)
    {
        body.SetRide(kind); r.SetRide(body.Ride); r.Tube = tube; body.SetTube(tube);
        rideNow = r.Ride;
    }
    static RideKind rideNow;
    static readonly string[] BoardGrinds = { "Boardslide", "Noseslide", "Tailslide", "Lipslide", "Nose Press", "50-50" };

    /// <summary>A fresh run, the rider put down at (x, z) going v0 down the fall line.</summary>
    static void Start(float x, float z, float v0)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
        terrain.Prewarm(); props.Prewarm();
        links = ""; crash = null; rough = null; landings = 0; firstBank = -1;
    }

    /// <summary>The ride is over (it has landed): stand still while the combo's window runs out and it banks.</summary>
    static void Settle(ref int n)
    {
        for (float t = 0f; t < 3f && r.Airborne && !r.Crashed; t += Dt, n++) { r.TrickX = r.TrickY = 0f; r.GrabHeld = false; Step(n); }   // (a toboggan skips before it settles)
        r.Stop();
        for (float t = 0f; t < 4f && game.Points.Active; t += Dt, n++) game.TestPoints(Dt);
    }

    static void Step(int n)
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        game.TestPoints(Dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 2 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 2f * Dt);
    }

    static void Say(string s)
    {
        lines.Add(s); Debug.Log("SCORE " + s);
        // (rides2) whatever happened on the way (a mission done, points banked), the ride is the one it started on
        if (r != null && r.Ride != rideNow) { Debug.Log($"SCORE FAIL the ride changed under it, {rideNow} -> {r.Ride}: {s}"); fails++; }
    }

    // ---------------------------------------------------------------- airs

    enum Jump { Pop, Kicker, Super }

    static void Air(string label, Jump jump, Script script, bool steerToo = false, bool steerOnly = false)
    {
        float x, z, v0;
        if (jump == Jump.Kicker) { Slope.NextKicker(100f, out Vector3 at); x = at.x; z = at.z - 30f; v0 = 30f; }
        else { z = Course.First + 110f; x = Slope.Centre(z); v0 = 18f; }
        Start(x, z, v0);
        float dx = x - Slope.Centre(z), air = 0f, peak = 0f, lastAir = 0f, x0 = 0f, sideways = 0f;
        bool popped = false, flew = false; int n = 0; string built = null;
        for (float t = 0f; t < 16f; t += Dt, n++)
        {
            if (!popped && !r.Airborne && jump != Jump.Kicker && t > 1f)
            {
                if (jump == Jump.Super) r.AddSuperJump(30f);
                r.QueuePop(jump == Jump.Super ? 0.8f : 0.5f); popped = true;
            }
            bool counted = r.Airborne && (jump == Jump.Kicker ? r.Z > z + 25f : popped);
            In i = counted ? script(r.AirTime, lastAir) : default;
            if (counted && !flew) { flew = true; x0 = r.transform.position.x; landings = 0; }
            r.TrickX = steerOnly ? 0f : i.x; r.TrickY = i.y; r.GrabHeld = i.hold;
            if (i.tap) r.TrickTap();
            r.Steer = r.Airborne ? (steerToo ? i.x : 0f) : Mathf.Clamp((Slope.Centre(r.Z + 12f) + dx - r.transform.position.x) * 0.06f - r.Heading * 0.8f, -1f, 1f);
            lastAir = r.Airborne ? r.AirTime : 0f;
            if (counted) { air = r.AirTime; peak = Mathf.Max(peak, r.AboveGround); if (r.TrickName != null) built = r.TrickName; }
            Step(n);
            if (flew && (landings > 0 || r.Crashed)) { sideways = r.transform.position.x - x0; break; }
        }
        if (!r.Crashed) Settle(ref n);
        string how = crash != null ? "CRASH '" + crash + "'" : links.Contains(" sketchy]") ? "sketchy" : "clean";
        Say($"{label,-36} air {air:0.00}s peak {peak:0.0}m | asked: {built ?? "-"} | landed:{(links == "" ? " -" : links)} | {how} | banked {Mathf.Max(0, firstBank)}" + (steerToo ? $" | drifted {sideways:0.0} m" : ""));
    }

    static void Airs()
    {
        Say("---- airs: a tapped jump on the piste (pop), a kicker at 108 km/h, a Super Jump on the piste");
        Script none = (t, t0) => default;
        Air("pop, no input", Jump.Pop, none);
        Air("kicker, no input", Jump.Kicker, none);
        Air("super, no input", Jump.Super, none);
        Say("---- taps only");
        Air("pop, 1 tap", Jump.Pop, (t, t0) => new In { tap = At(t, t0, 0.12f) });
        Air("kicker, 2 taps", Jump.Kicker, (t, t0) => new In { tap = At(t, t0, 0.12f) || At(t, t0, 0.3f) });
        Air("kicker, tap and hold 0.8 s", Jump.Kicker, (t, t0) => new In { tap = At(t, t0, 0.12f), hold = t > 0.12f && t < 0.92f });
        Air("super, 4 taps", Jump.Super, (t, t0) => new In { tap = At(t, t0, 0.2f) || At(t, t0, 0.5f) || At(t, t0, 0.8f) || At(t, t0, 1.1f) });
        Air("super, 6 taps", Jump.Super, (t, t0) => new In { tap = At(t, t0, 0.2f) || At(t, t0, 0.45f) || At(t, t0, 0.7f) || At(t, t0, 0.95f) || At(t, t0, 1.2f) || At(t, t0, 1.45f) });
        Air("pop, tap held into the landing", Jump.Pop, (t, t0) => new In { tap = At(t, t0, 0.3f), hold = t > 0.3f });
        Say("---- directions only");
        Air("pop, left held", Jump.Pop, (t, t0) => new In { x = t > 0.08f ? -1f : 0f });
        Air("kicker, right held", Jump.Kicker, (t, t0) => new In { x = t > 0.08f ? 1f : 0f });
        Air("kicker, one flick left", Jump.Kicker, (t, t0) => new In { x = t > 0.1f && t < 0.2f ? -1f : 0f });
        Air("kicker, down held (backflip)", Jump.Kicker, (t, t0) => new In { y = t > 0.08f ? -1f : 0f });
        Air("kicker, up held (frontflip)", Jump.Kicker, (t, t0) => new In { y = t > 0.08f ? 1f : 0f });
        Air("super, right held", Jump.Super, (t, t0) => new In { x = t > 0.08f ? 1f : 0f });
        Air("super, down held", Jump.Super, (t, t0) => new In { y = t > 0.08f ? -1f : 0f });
        Air("super, left + down held (cork)", Jump.Super, (t, t0) => new In { x = t > 0.08f ? -1f : 0f, y = t > 0.08f ? -1f : 0f });
        Air("super, right + up held (misty)", Jump.Super, (t, t0) => new In { x = t > 0.08f ? 1f : 0f, y = t > 0.08f ? 1f : 0f });
        Say("---- both");
        Air("pop, left held + 1 tap", Jump.Pop, (t, t0) => new In { x = t > 0.08f ? -1f : 0f, tap = At(t, t0, 0.15f) });
        Air("kicker, right held + 2 taps", Jump.Kicker, (t, t0) => new In { x = t > 0.08f ? 1f : 0f, tap = At(t, t0, 0.15f) || At(t, t0, 0.4f) });
        Air("kicker, down held + tap", Jump.Kicker, (t, t0) => new In { y = t > 0.08f ? -1f : 0f, tap = At(t, t0, 0.2f) });
        Air("super, left+down held + 3 taps", Jump.Super, (t, t0) => new In { x = t > 0.08f ? -1f : 0f, y = t > 0.08f ? -1f : 0f, tap = At(t, t0, 0.2f) || At(t, t0, 0.5f) || At(t, t0, 0.8f) });
        Air("super, everything (6 taps)", Jump.Super, (t, t0) => new In { x = t > 0.08f ? 1f : 0f, y = t > 0.08f ? -1f : 0f, tap = At(t, t0, 0.2f) || At(t, t0, 0.4f) || At(t, t0, 0.6f) || At(t, t0, 0.8f) || At(t, t0, 1f) || At(t, t0, 1.2f) });
        Say("---- too much rotation for the air");
        Air("pop, backflip pushed late (0.35 s)", Jump.Pop, (t, t0) => new In { y = t > 0.35f ? -1f : 0f });
        Air("pop, backflip pushed at once", Jump.Pop, (t, t0) => new In { y = t > 0.03f && t < 0.2f ? -1f : 0f });
        Air("pop, 3 flicks left", Jump.Pop, (t, t0) => new In { x = (t > 0.05f && t < 0.12f) || (t > 0.2f && t < 0.27f) || (t > 0.35f && t < 0.42f) ? -1f : 0f });
        Air("kicker, 5 flicks right", Jump.Kicker, (t, t0) => new In { x = Mathf.Repeat(t, 0.16f) < 0.08f && t > 0.05f && t < 0.85f ? 1f : 0f });
        Air("kicker, 2 backflips pushed", Jump.Kicker, (t, t0) => new In { y = (t > 0.05f && t < 0.15f) || (t > 0.3f && t < 0.4f) ? -1f : 0f });
        Air("kicker, held, then 1 flick more", Jump.Kicker, (t, t0) => new In { x = t > 0.08f && !(t > 1.62f && t < 1.68f) ? 1f : 0f });
        Air("kicker, held, then 2 flicks more", Jump.Kicker, (t, t0) => new In { x = t > 0.08f && !(t > 1.5f && t < 1.56f) && !(t > 1.66f && t < 1.72f) ? 1f : 0f });
        Air("kicker, 3 backflips pushed", Jump.Kicker, (t, t0) => new In { y = (t > 0.05f && t < 0.15f) || (t > 0.3f && t < 0.4f) || (t > 0.55f && t < 0.65f) ? -1f : 0f });
        Say("---- air control beside trick input (the stick steers the flight as it always did, spin or no spin)");
        Air("kicker, stick 0.5 right (no spin)", Jump.Kicker, (t, t0) => new In { x = 0.5f }, true);
        Air("kicker, stick full right (spins)", Jump.Kicker, (t, t0) => new In { x = 1f }, true);
        Air("the same with trick input off", Jump.Kicker, (t, t0) => new In { x = 1f }, true, true);
        Say("---- the other rides: the same inputs (left + down held, 2 taps) off the same kicker");
        Script same = (t, t0) => new In { x = t > 0.08f ? -1f : 0f, y = t > 0.08f ? -1f : 0f, tap = At(t, t0, 0.15f) || At(t, t0, 0.4f) };
        Ride(RideKind.Skis, false); Air("skis", Jump.Kicker, same);
        Ride(RideKind.Toboggan, false); Air("toboggan", Jump.Kicker, same);
        Ride(RideKind.Snowboard, true); Air("snow tube", Jump.Kicker, same);
        Air("snow tube, 3 flicks (any angle lands)", Jump.Kicker, (t, t0) => new In { x = (t > 0.05f && t < 0.12f) || (t > 0.2f && t < 0.27f) || (t > 0.35f && t < 0.42f) ? -1f : 0f });
        // (mtb) the mountain bike: the same, a 360 with a grab, a backflip, and a grab held through its tricks
        Ride(RideKind.Bike, false); Air("mountain bike", Jump.Kicker, same);
        Air("mountain bike, 360 + tap", Jump.Kicker, (t, t0) => new In { x = t > 0.08f && t < 0.9f ? 1f : 0f, tap = At(t, t0, 0.2f) });
        Air("mountain bike, backflip", Jump.Kicker, (t, t0) => new In { y = t > 0.05f && t < 0.2f ? -1f : 0f });
        Air("mountain bike, 4 taps (its tricks in turn)", Jump.Kicker, (t, t0) => new In { tap = At(t, t0, 0.15f) || At(t, t0, 0.35f) || At(t, t0, 0.55f) || At(t, t0, 0.75f) });
        Ride(RideKind.Snowboard, false);
    }

    // ---------------------------------------------------------------- grinds

    static void Grind(string label, Course.Feature f, Script script)
    {
        Start(f.a.x, f.a.z - 18f, 14f);
        float on = 0f, last = 0f, since = 0f; bool was = false, off = false; int n = 0; int topMult = 0, topTotal = 0;
        for (float t = 0f; t < 14f; t += Dt, n++)
        {
            float tx = r.Z < f.a.z ? f.a.x : Mathf.Lerp(f.a.x, f.b.x, Mathf.InverseLerp(f.a.z, f.b.z, r.Z));
            r.Steer = Mathf.Clamp((tx - r.transform.position.x) * 0.4f - r.Heading * 1.5f, -1f, 1f);
            if (r.Grinding) { on += Dt; was = true; }
            In i = was && !off ? script(on, last) : default;
            last = on;
            r.TrickX = i.x; r.TrickY = i.y; r.GrabHeld = i.hold;
            if (i.tap) { if (r.Grinding) r.QueuePop(0.5f); else r.TrickTap(); }
            Step(n);
            topMult = Mathf.Max(topMult, game.Points.Mult); topTotal = Mathf.Max(topTotal, game.Points.Total);
            if (was && !r.Grinding && !off) { off = true; landings = 0; }
            if ((off && landings > 0) || r.Crashed) break;
        }
        if (!r.Crashed) Settle(ref n);
        since = 0f;
        Say($"{label,-34} on it {on:0.00}s |{(links == "" ? " -" : links)} | {(crash != null ? "CRASH '" + crash + "'" : "rode away")} | combo peaked x{topMult} at {topTotal} | banked {game.Points.Banked}");
        // (rides2) whatever happens on the way (a mission done, points banked), the ride is the one it started on, and
        // only a snowboard's grinds are called a snowboard's
        if (rideNow != RideKind.Snowboard)
            foreach (var board in BoardGrinds)
                if (links.Contains("[" + board + " ") || links.Contains(" " + board + " ")) { Debug.Log($"SCORE FAIL {label}: a snowboard's grind ({board}) on the {rideNow}"); fails++; break; }
    }

    static void Grinds()
    {
        Course.Feature? rail = null, box = null;
        for (int c = 0; c < 3 && (rail == null || box == null); c++)
            foreach (var f in Course.FeaturesOf(c)) { if (f.kind == Course.Kind.Rail && rail == null) rail = f; if (f.kind == Course.Kind.Box && box == null) box = f; }
        if (rail == null || box == null) { Say("no rail or box found"); fails++; return; }
        Say("---- grinds: a rail (45 m) and a box (26 m)");
        Script none = (t, t0) => default;
        Script flick3 = (t, t0) => new In { x = t > 0.4f && t < 0.5f ? 1f : t > 1.2f && t < 1.3f ? -1f : 0f, y = t > 0.8f && t < 0.9f ? 1f : 0f };
        Script spam = (t, t0) => new In { x = Mathf.Repeat(t, 0.3f) < 0.1f ? 1f : 0f };
        Script popSpin = (t, t0) => new In { tap = At(t, t0, 0.9f), x = t > 0.95f || t == 0f ? 1f : 0f };
        Grind("rail, no input", rail.Value, none);
        Grind("rail, 2 switch-ups and a press", rail.Value, flick3);
        Grind("rail, the same flick over and over", rail.Value, spam);
        Grind("box, no input", box.Value, none);
        Grind("box, 2 switch-ups and a press", box.Value, flick3);
        Grind("rail, pop off at 0.9 s", rail.Value, (t, t0) => new In { tap = At(t, t0, 0.9f) });
        Ride(RideKind.Skis, false);
        Grind("skis, rail, switch-up and a press", rail.Value, flick3);
        Ride(RideKind.Toboggan, false);
        Grind("toboggan, rail (bonks off)", rail.Value, none);
        Grind("toboggan, box, a flick", box.Value, flick3);
        Ride(RideKind.Snowboard, false);
    }

    /// <summary>Over a lift's hill and onto its cable, with switch-ups; then whatever the towers and the next spans give.</summary>
    static void Cable(string label, Script script)
    {
        foreach (var l in LiftLine.Lifts)
        {
            var rp = l.hill;
            if (rp == null) continue;
            Vector3 foot = l.At(rp.s - rp.rise, rp.side * LiftLine.Half, 0f);
            foot.y = Slope.Height(foot.x, foot.z);
            Vector3 flat = l.dir;
            Vector3 from = foot - flat * 12f;
            Start(from.x, from.z, rp.needSpeed + 4f);
            r.Heading = Mathf.Atan2(flat.x, flat.z);
            r.Body.linearVelocity = Vector3.ProjectOnPlane(flat, Slope.Normal(from.x, from.z)).normalized * (rp.needSpeed + 4f);
            float on = 0f, last = 0f, since = 0f; bool was = false; int n = 0, topMult = 0, topTotal = 0;
            for (float t = 0f; t < 30f; t += Dt, n++)
            {
                Vector3 p = r.transform.position;
                float ahead = Vector3.Dot(foot - p, flat);
                Vector3 aim = ahead > 2f ? foot + flat * 2f : p + flat * 10f;
                float want = Mathf.Atan2(aim.x - p.x, aim.z - p.z);
                r.Steer = r.Grinding ? 0f : Mathf.Clamp((want - r.Heading) * 3f - r.YawRate * 0.2f, -1f, 1f);
                if (r.OnCable) { on += Dt; was = true; }
                In i = r.OnCable ? script(on, last) : default;
                last = on;
                r.TrickX = i.x; r.TrickY = i.y;
                Step(n);
                topMult = Mathf.Max(topMult, game.Points.Mult); topTotal = Mathf.Max(topTotal, game.Points.Total);
                if (was && r.Grinding) landings = 0;
                if ((was && !r.Grinding && landings > 0) || r.Crashed) break;
            }
            if (!r.Crashed) Settle(ref n);
            since = 0f;
            Say($"{label,-34} on the cable {on:0.00}s |{(links == "" ? " -" : links)} | {(crash != null ? "CRASH '" + crash + "'" : "rode away")} | combo peaked x{topMult} at {topTotal} | banked {game.Points.Banked}");
            return;
        }
        Say($"{label}: no lift with a hill on this mountain");
    }

    static void Cables()
    {
        Say("---- a lift cable, off its hill");
        r.ResetAt(Slope.Centre(Course.First), Course.First); terrain.Prewarm(); props.Prewarm();
        Cable("cable, no input", (t, t0) => default);
        Cable("cable, switch-ups and presses", (t, t0) => new In { x = Mathf.Repeat(t, 1.2f) > 0.5f && Mathf.Repeat(t, 1.2f) < 0.6f ? 1f : 0f, y = Mathf.Repeat(t, 1.2f) > 1f && Mathf.Repeat(t, 1.2f) < 1.1f ? 1f : 0f });
    }

    public static void Run()
    {
        Boot();
        try
        {
            var first = new List<string>();
            for (int pass = 0; pass < 2; pass++)
            {
                lines.Clear();
                Mountain(1000);
                Airs(); Grinds(); Cables();
                if (pass == 0) first.AddRange(lines);
            }
            int diff = 0;
            for (int i = 0; i < Mathf.Max(first.Count, lines.Count); i++)
                if (i >= first.Count || i >= lines.Count || first[i] != lines[i]) { diff++; Debug.Log($"SCORE DIFFERS line {i}:\n  1: {(i < first.Count ? first[i] : "")}\n  2: {(i < lines.Count ? lines[i] : "")}"); }
            Debug.Log($"SCORE {(diff == 0 ? "PASS" : "FAIL")}: the table ridden twice, {first.Count} lines, {diff} different; {fails} failure(s)");
            if (diff > 0) fails++;
            // (rides2) the progress changing mid-run (a mission's reward) leaves what is ridden alone
            Ride(RideKind.Toboggan, false);
            Progress.Changed?.Invoke();
            bool kept = r.Ride == RideKind.Toboggan;
            Debug.Log($"SCORE {(kept ? "PASS" : "FAIL")}: the progress changing mid-run leaves the toboggan a toboggan (now {r.Ride})");
            if (!kept) fails++;
        }
        catch (System.Exception e) { Debug.LogError("SCORE exception " + e); fails++; }
        Done();
    }

    // ---------------------------------------------------------------- whole runs

    /// <summary>A rider who looks where they are going: down the piste, round whatever is in the way.</summary>
    static class Bot
    {
        static float shift, nextLook;
        public static void Reset() { shift = 0f; nextLook = 0f; }

        static bool Clear(Vector3 p, Vector3 q, Rigidbody own)
        {
            Vector3 d = q - p; float len = d.magnitude;
            for (int i = 1; i <= 6; i++)
            {
                Vector3 s = Vector3.Lerp(p, q, i / 6f);
                if (Course.CrevasseNear(s.x, s.z, 2f) || Deadly.Below(s.x, s.z)) return false;
                if (Slope.Normal(s.x, s.z).y < 0.7f) return false;
            }
            var from = p + Vector3.up * 0.8f;
            foreach (var h in Physics.SphereCastAll(from, 1.3f, d / len, len, ~0, QueryTriggerInteraction.Ignore))
                if (h.rigidbody != own && h.collider.GetComponentInParent<Obstacle>() != null) return false;
            return true;
        }

        public static float Steer(Rider r, float aimX, float t)
        {
            Vector3 p = r.Body.position;
            float ahead = Mathf.Clamp(r.Speed * 1.1f, 14f, 45f);
            if (t >= nextLook && !r.Airborne)
            {
                nextLook = t + 0.1f;
                float best = 0f; bool found = false;
                foreach (float dx in new[] { 0f, 3f, -3f, 6f, -6f, 10f, -10f, 15f, -15f, 22f, -22f })
                {
                    var q = new Vector3(aimX + dx, 0f, p.z + ahead); q.y = Slope.DrawnHeight(q.x, q.z);
                    var mid = new Vector3(Mathf.Lerp(p.x, q.x, 0.5f), 0f, p.z + ahead * 0.5f); mid.y = Slope.DrawnHeight(mid.x, mid.z);
                    if (Clear(p, mid, r.Body) && Clear(mid, q, r.Body)) { best = dx; found = true; break; }
                }
                shift = found ? best : shift;
            }
            float wantX = aimX + shift;
            float want = Mathf.Atan2(wantX - p.x, ahead);
            return Mathf.Clamp((want - r.Heading) * 2.2f + (shift == 0f ? 0.25f * Mathf.Sin(t * 1.1f) : 0f), -1f, 1f);
        }
    }

    static readonly string[] BotName = { "hands off", "steers, no tricks", "some tricks", "heavy tricks" };

    public static void Runs()
    {
        Boot();
        int crashes = 0, dropped = 0, bankedN = 0; string log = "";
        r.Crash += why => crashes++;
        game.Points.Dropped += total => dropped += total;
        game.Points.BankedNow += (total, pot, mult) => { bankedN++; if (log.Length < 1400) log += $" [{pot} x{mult}]"; };
        var totals = new Dictionary<int, List<int>>();
        try
        {
            var near = new List<Course.Feature>();
            foreach (int seed in new[] { 1000, 1077, 1154, 1231, 1308 })
                for (int bot = 0; bot < 4; bot++)
                {
                    Mountain(seed);
                    typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
                    terrain.Prewarm(); props.Prewarm();
                    typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
                    r.input = null;
                    crashes = 0; dropped = 0; bankedN = 0; log = ""; links = "";
                    Bot.Reset();
                    float t = 0f, nextPop = 3f, maxZ = r.Z, downFor = 0f, grindT = 0f; bool finished = false, killed = false; int n = 0, flicks = 0;
                    System.Action<float> onKill = fell => killed = true;
                    body.Killed += onKill;
                    float lastAir = 0f; int dirSign = 1;
                    for (; t < 400f; t += Dt, n++)
                    {
                        Vector3 p = r.Body.position;
                        r.TrickX = 0f; r.TrickY = 0f; r.GrabHeld = false;
                        if (bot == 0) r.Steer = 0f;   // hands off: what the autoplay device check does
                        else
                        {
                            float aimX = Slope.Centre(r.Z + 12f * Slope.Squash(r.Z));   // (12 m along the run: on a cliff band's traverse, less down the hill)
                            if (Slope.NextKicker(r.Z, out Vector3 kk) && kk.z - r.Z < 70f) aimX = kk.x;
                            Course.FeaturesNear(r.Z, near);
                            foreach (var f in near)
                                if (f.a.z > r.Z - 2f && f.a.z - r.Z < 60f && Mathf.Abs(f.a.x - p.x) < 12f) { aimX = f.a.x; break; }
                            r.Steer = Bot.Steer(r, aimX, t);
                            if (bot >= 2 && !r.Airborne && !r.Crashed && !r.Grinding)
                            {
                                bool lip = Slope.NextKicker(r.Z - 2f, out Vector3 k2) && k2.z - r.Z < 0.8f && k2.z - r.Z > -0.2f && Mathf.Abs(k2.x - p.x) < 4f;
                                if (lip || t > nextPop) { r.QueuePop(0.5f); nextPop = t + 5f; }
                            }
                            // tricks. Some: hold a spin through the air (it only turns as far as fits) and grab once.
                            // Heavy: that, a flip pushed whenever there is a clear second of air left for it, the grab
                            // stepped up three times, and switch-ups and presses along every rail.
                            if (bot >= 2 && r.Airborne && !r.Crashed)
                            {
                                float a = r.AirTime, left = r.TrickTime - a;
                                if (a < lastAir) dirSign = -dirSign;
                                r.TrickX = a > 0.08f ? dirSign : 0f;
                                if (At(a, lastAir, 0.15f)) r.TrickTap();
                                if (bot == 3)
                                {
                                    if (At(a, lastAir, 0.35f) || At(a, lastAir, 0.55f)) r.TrickTap();
                                    r.GrabHeld = a > 0.15f && left > 0.35f;
                                    r.TrickY = a > 0.1f && left > 1.05f / r.Feel.Flip ? -1f : 0f;
                                }
                                lastAir = a;
                            }
                            else lastAir = 0f;
                            if (bot == 3 && r.GrindLive)
                            {
                                grindT += Dt;
                                float ph = Mathf.Repeat(grindT, 0.8f);
                                r.TrickX = ph > 0.3f && ph < 0.4f ? 1f : 0f;
                                r.TrickY = ph > 0.7f && ph < 0.8f ? 1f : 0f;
                                if (ph > 0.3f && ph - Dt <= 0.3f) flicks++;
                            }
                            else grindT = 0f;
                        }
                        // batchmode sends no collision messages: the board into an obstacle, as Rider.OnCollisionEnter
                        if (!r.Crashed && r.Running)
                        {
                            r.TestContacts(Dt);   // (as Rider.OnCollisionEnter: ridden over, glanced off or crashed into)
                        }
                        if (r.Crashed && r.CanGetUp) r.RequestUp = true;
                        maxZ = Mathf.Max(maxZ, r.Z);
                        downFor = r.Crashed ? downFor + Dt : 0f;
                        if (killed || downFor > 12f) break;
                        if (r.Crashed && body.RagdollBelowSnow > 3f) break;   // lost down a crevasse
                        if (r.Crashed && r.StayDown && !body.RagdollMoving) break;
                        Step(n);
                        if (!r.Crashed && !r.Airborne && r.Z > Slope.FinishZ + 30f) { finished = true; game.Points.Bank(); break; }
                    }
                    body.Killed -= onKill;
                    float dist = Mathf.Max(0f, maxZ - r.StartZ);
                    var c2 = game.Points;
                    int score = Mathf.RoundToInt(dist) + c2.Banked + (finished ? 5000 : 0);
                    if (!totals.TryGetValue(bot, out var list)) totals[bot] = list = new List<int>();
                    list.Add(score);
                    Debug.Log($"SCORERUN seed={seed} bot={bot} ({BotName[bot]}) score={score} banked={c2.Banked} in {bankedN} combos, best {c2.BestBank} (longest {c2.BestLinks} links), dropped {dropped}, dist={dist:0} time={t:0.0} falls={crashes} finished={finished}");
                    Debug.Log($"SCORERUN seed={seed} bot={bot} banks:{log}");
                }
            foreach (var kv in totals)
            {
                kv.Value.Sort();
                Debug.Log($"SCORERUN bot={kv.Key} ({BotName[kv.Key]}): median {kv.Value[kv.Value.Count / 2]}, low {kv.Value[0]}, high {kv.Value[kv.Value.Count - 1]}");
            }
        }
        catch (System.Exception e) { Debug.LogError("SCORERUN exception " + e); fails++; }
        Done();
    }
}
