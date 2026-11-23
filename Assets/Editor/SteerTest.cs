using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// The steering, measured the way a rider feels it (slide: the finger is a push sideways across the fall line; the
/// board answers at once, holds a sideways speed for a held drag, and eases back down the fall line when let go).
/// At a few held speeds and settings of the TURN slider (Rider.TurnScale):
///  - the time from the finger to the first sideways motion (1 m/s, and 3 m/s), and how much there is 0.05 s on;
///  - the sideways speed a whole drag settles at, against the speed down the hill;
///  - the reversal: from a whole drag held one way, the finger flipped: the time until the board is going the other
///    way (through zero, at 2 m/s, at half of the whole);
///  - the release: from a whole drag held, the finger lifted: the time until a tenth of the sideways speed is left and
///    until the way it goes is within 5 degrees of the fall line, and the hardest it is pulled back;
///  - a slalom between pairs of "trees" (gates 3 m wide) at held speeds: the old pursuit bot and a racer that rides a
///    line through the gates with the sideways speed it needs fed forward.
/// Then: the fastest held speed at which all three courses are made clean (what the pace knob may be set to), what
/// a sustained whole drag costs in speed, what the pace knob does to a straight run, the board set going across a
/// slope and left to itself, and the same pushes on each kind of snow.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod SteerTest.Run -logFile steer.log
/// </summary>
public static class SteerTest
{
    const float Dt = 1f / 120f;
    const float GateW = 3f;   // the gap between the two trees of a gate

    struct Course { public string name; public float spacing, swing; public int gates; }
    static readonly Course[] Courses =
    {
        new Course { name = "glade", spacing = 14f, swing = 3f, gates = 16 },     // 6 m side to side every 14 m
        new Course { name = "trees", spacing = 9f, swing = 2.5f, gates = 20 },    // the forest's own 6.5 m grid, weaving
        new Course { name = "tight", spacing = 7f, swing = 2.5f, gates = 24 },    // tight trees: 5 m side to side every 7 m
    };

    static float x0, z0;   // where the test stretch starts
    static float Scale { get => Rider.TurnScale; set => Rider.TurnScale = value; }
    static readonly bool quick = System.Environment.GetEnvironmentVariable("STEER_QUICK") == "1";   // no slaloms
    // STEER_PACE=1.15: the whole test at that setting of the pace knob (Rider.Pace)
    static readonly string paceEnv = System.Environment.GetEnvironmentVariable("STEER_PACE");
    static int fails;
    static void Fail(string s) { fails++; Debug.Log("STEER-FAIL " + s); }

    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        Physics.simulationMode = SimulationMode.Script;
        TestPrefs.Pin();
        Slope.Init(1234);
        var r = new GameObject("Rider").AddComponent<Rider>();   // (no forest: the trees here are the gates)
        float scale0 = Scale, paceWas = Rider.Pace;
        if (paceEnv != null) Rider.Pace = float.Parse(paceEnv, System.Globalization.CultureInfo.InvariantCulture);
        float pace0 = Rider.Pace;

        FindStretch(r);
        Debug.Log($"STEER stretch x={x0:0} z={z0:0} (x-centre {x0 - Slope.Centre(z0):0}), pitch {Mathf.Asin(Mathf.Sqrt(1f - Mathf.Pow(Slope.Normal(x0, z0 + 40f).y, 2f))) * Mathf.Rad2Deg:0} deg; ramp {Rider.RampTime:0.00} s, coast {Rider.CoastTime:0.00} s, return {Rider.ReturnIn:0.00} + {Rider.ReturnTime:0.00} s, a whole drag {Rider.SideShare:0.00} of the speed ({Rider.SideLeast:0} to {Rider.SideCap:0} m/s), side drag {Rider.SideDrag:0.000}, pace {Rider.Pace:0.00}");

        float[] speeds = { 15f, 22f, 30f };
        foreach (float sc in new[] { 0.5f, 1f, 1.5f, 2.5f })
        {
            Scale = sc;
            foreach (float v in speeds)
            {
                First(r, v, 1f, out float t1, out float t3, out float at05, out float steady);
                float t90 = firstT90;
                Reverse(r, v, 1f, out float r0, out float r2, out float rHalf);
                Release(r, v, 1f, out float tTenth, out float t5deg, out float back);
                float coast = releaseCoast;
                First(r, v, RiderInput.Curve(0.25f), out _, out _, out _, out float quarter);
                Debug.Log($"STEER push scale={sc:0.0} v={v:0} m/s ({v * 3.6f:0} km/h) whole drag: sideways 1 m/s in {T(t1)}, 3 m/s in {T(t3)}, {at05:0.0} m/s after 0.05 s, 90% of what it settles at in {T(t90)}; it settles at {steady:0.0} m/s sideways ({100f * steady / v:0}% of the speed, {Mathf.Asin(Mathf.Clamp(steady / v, -1f, 1f)) * Mathf.Rad2Deg:0} deg off the fall line); a quarter of the drag settles at {quarter:0.0} m/s | flipped the other way: through zero in {T(r0)}, 2 m/s the other way in {T(r2)}, half of the whole in {T(rHalf)} | let go: it coasts {T(coast)} before a tenth of its sideways speed is gone, a tenth of it left in {T(tTenth)}, within 5 deg of the fall line in {T(t5deg)}{(releaseAir ? " (it left the snow on the way)" : "")}, pulled back at {back:0} m/s^2 at most");
                if (sc == 1f)
                {
                    if (t1 < 0f || t1 > 0.08f) Fail($"{v * 3.6f:0} km/h: the first sideways motion (1 m/s) takes {T(t1)} from the finger (0.08 s asked)");
                    if (t90 < 0.2f || t90 > 0.45f) Fail($"{v * 3.6f:0} km/h: a whole drag's sideways speed is there (90%) in {T(t90)} (0.3 to 0.4 s asked)");
                    if (r2 < 0f || r2 > 0.32f) Fail($"{v * 3.6f:0} km/h: flipped, the board is going the other way (2 m/s) only after {T(r2)} (0.25 to 0.3 s asked)");
                    // (judged from 79 km/h: slower, a tenth of the slide is under a metre a second, and the hill's own fall
                    // line wandering under the board is as much of it as the coast)
                    if (v >= 22f && coast < 0.25f) Fail($"{v * 3.6f:0} km/h: let go, the board coasts only {T(coast)} on its slide (0.3 to 0.4 s asked)");
                    if (!releaseAir && (t5deg < 1.3f || t5deg > 2.3f)) Fail($"{v * 3.6f:0} km/h: let go, the board is back within 5 deg of the fall line in {T(t5deg)} (1.5 to 2 s asked)");
                }
                if (quick || sc != 1f) continue;   // (the slaloms at the stock setting)
                string line = $"STEER slalom scale={sc:0.0} v={v:0} m/s ({v * 3.6f:0} km/h), speed held";
                bool cleanAll = true;
                foreach (var c in Courses)
                {
                    int old = BestPursuit(r, c, v);
                    Result held = BestRacer(r, c, v);
                    line += $" | {c.name}: pursuit bot {old}/{c.gates}, racer {held.made}/{held.tried}{(held.tried < c.gates ? $" (then off a roller; {c.gates} gates set)" : "")}";
                    if (!held.Clean || held.tried < c.gates / 3) cleanAll = false;
                }
                Debug.Log(line);
                Debug.Log($"STEER slalom at {v * 3.6f:0} km/h, pace {Rider.Pace:0.00}: {(cleanAll ? "every gate reached on the snow made, all three courses" : "NOT every gate made")}");
            }
        }
        Scale = 1f;

        // the fastest held speed at which each course is made clean, and all three: what the pace may be
        if (!quick)
        {
            var clean = new float[Courses.Length]; float all = 0f; bool allSoFar = true;
            string table = "";
            for (float v = 10f; v <= 54f; v += 2f)
            {
                bool every = true; table += $" {v * 3.6f:0}:";
                for (int k = 0; k < Courses.Length; k++)
                {
                    var m = BestRacer(r, Courses[k], v);
                    table += $"{(k > 0 ? " " : "")}{m.made}/{m.tried}";
                    if (m.Clean && m.tried >= Courses[k].gates / 3) { if (clean[k] == v - 2f || v == 10f) clean[k] = v; } else every = false;
                }
                if (every && allSoFar) all = v; else allSoFar = false;
            }
            Debug.Log($"STEER gates made of those reached on the snow, by held speed (km/h: glade of 16, trees of 20, tight of 24):{table}");
            for (int k = 0; k < Courses.Length; k++)
                Debug.Log($"STEER comfortable {Courses[k].name} ({Courses[k].swing * 2f:0} m side to side every {Courses[k].spacing:0} m): every gate at every held speed up to {clean[k] * 3.6f:0} km/h");
            Debug.Log($"STEER comfortable all three courses: every gate at every held speed up to {all * 3.6f:0} km/h");
        }

        // deep powder (off the groomed run, through the trees): the speed a board settles at there, and the slaloms
        // at that speed, held
        {
            Surfaces.Force = SnowMix.Only(Snow.Powder);
            float cruiseP = Straight(r, 22f, 400f, out _);
            Surfaces.Force = SnowMix.Only(Snow.Corduroy);
            float cruiseC = Straight(r, 22f, 400f, out _);
            Surfaces.Force = SnowMix.Only(Snow.Ice);
            float cruiseI = Straight(r, 22f, 400f, out _);
            Debug.Log($"STEER cruise down 400 m of the stretch from 79 km/h, pace {Rider.Pace:0.00}: groomed {cruiseC * 3.6f:0} km/h, ice {cruiseI * 3.6f:0} km/h, deep powder {cruiseP * 3.6f:0} km/h");
            if (!quick)
            {
                Surfaces.Force = SnowMix.Only(Snow.Powder);
                float vP = Mathf.Max(12f, cruiseP);
                string line = $"STEER slalom in deep powder at its own cruise, {vP * 3.6f:0} km/h, speed held";
                bool all = true;
                // (judged on the glade and the forest's own grid; the tight trees, 5 m side to side every 7 m, are reported)
                foreach (var c in Courses) { Result m = BestRacer(r, c, vP); line += $" | {c.name}: racer {m.made}/{m.tried}"; if (c.name != "tight" && (!m.Clean || m.tried < c.gates / 3)) all = false; }
                Debug.Log(line + (all ? " | the glade and the trees: every gate reached on the snow made" : " | NOT every gate made in the glade and the trees"));
                if (!all) Fail($"slalom in deep powder at {vP * 3.6f:0} km/h: not every gate made in the glade and the trees");
            }
            Surfaces.Force = null;
        }

        // what a sustained whole drag costs: the stretch from 79 km/h with the speed its own, straight, and thrown
        // a whole drag one way and the other across a 10 m lane
        {
            // (on corduroy everywhere: the weave goes 5 m either side of the line, and off the groomed run powder's own
            // drag would be counted as the drag's)
            Surfaces.Force = SnowMix.Only(Snow.Corduroy);
            Free(r, 22f, 300f, false, out float meanS, out float endS); string noteS = freeNote;
            Free(r, 22f, 300f, true, out float meanW, out float endW);
            Surfaces.Force = null;
            Debug.Log($"STEER cost rides: straight {noteS} | whole drag {freeNote}");
            Debug.Log($"STEER cost of a whole drag held (one way and the other, 300 m of the stretch on corduroy from 79 km/h, the speed its own): {meanW * 3.6f:0} km/h on average and {endW * 3.6f:0} at the end, against {meanS * 3.6f:0} and {endS * 3.6f:0} ridden straight: {100f * (1f - meanW / meanS):0}% and {100f * (1f - endW / endS):0}% slower");
            if (endW < 0.88f * endS) Fail($"a whole drag held costs {100f * (1f - endW / endS):0}% of the speed (10% at most asked)");
        }

        // the pace knob: a straight run down the stretch from 22 m/s, the speed left to itself
        foreach (float pace in new[] { 1f, 1.15f, 1.3f, 0.8f, 0.64f, 0.5f })
        {
            Rider.Pace = pace;
            float end = Straight(r, 22f, 400f, out float most);
            Debug.Log($"STEER pace {pace:0.00}: straight down 400 m of the stretch from 79 km/h: {end * 3.6f:0} km/h at the end, {most * 3.6f:0} at most");
        }
        Rider.Pace = pace0;

        Across(r);

        // the snow underfoot (Surfaces): the same pushes on each kind laid everywhere. Softer snow and ice answer a
        // little later and slide on a little longer; none of them gives less sideways speed, and none lets go
        foreach (Snow kind in System.Enum.GetValues(typeof(Snow)))
        {
            Surfaces.Force = SnowMix.Only(kind);
            foreach (float v in new[] { 22f, 30f })
            {
                First(r, v, 1f, out float t1, out float t3, out float at05, out float steady);
                Reverse(r, v, 1f, out float r0, out float r2, out _);
                Release(r, v, 1f, out float tTenth, out float t5deg, out _);
                Debug.Log($"STEER snow {kind.ToString().ToLower(),-8} v={v:0} m/s ({v * 3.6f:0} km/h) whole drag: sideways 1 m/s in {T(t1)}, 3 m/s in {T(t3)}, settles at {steady:0.0} m/s | flipped: through zero in {T(r0)}, 2 m/s the other way in {T(r2)} | let go: a tenth left in {T(tTenth)}, within 5 deg of the fall line in {T(t5deg)}");
                if (steady < 0.9f * r.SideFull(v)) Fail($"snow {kind}: a whole drag gives {steady:0.0} m/s sideways at {v * 3.6f:0} km/h, not {r.SideFull(v):0.0}");
            }
        }
        Surfaces.Force = null;

        Debug.Log($"STEERTEST {(fails == 0 ? "PASS" : "FAIL")} ({fails} failures)");
        Scale = scale0; Rider.Pace = paceWas;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// The board set going 20 degrees across the fall line on the stretch and left to itself: how the way it goes
    /// comes round to the fall line (half way, within 5 degrees), the fastest it turns, and what pulls it (the snow's
    /// resistance to the slide, and gravity across the path). Then the same start with the finger held up the hill's
    /// side: the least drag that holds the traverse for 3 s. STEER-FAIL if hands off it does not come round in 3 s, or
    /// comes round in under 0.3 s (a snap).
    /// </summary>
    static void Across(Rider r)
    {
        foreach (float v in new[] { 15f, 22f, 30f })
            foreach (float side in new[] { 1f, -1f })
            {
                float a0 = 20f * Mathf.Deg2Rad * side;
                float from = Launch(r, v, a0);   // (just let go: within the ease)
                float tHalf = -1f, t5 = -1f, mostRate = 0f, prev = r.TravelHeading, most = 0f, gMost = 0f; bool off = false;
                for (int i = 0; i < 120 * 6; i++)
                {
                    r.Steer = 0f; StepHeld(r, v);
                    if (r.Airborne || r.Crashed || r.Grinding) { off = true; break; }
                    float h = r.TravelHeading - r.FallLine, rate = Mathf.Abs(r.TravelHeading - prev) / Dt; prev = r.TravelHeading;
                    if (i > 5) mostRate = Mathf.Max(mostRate, rate);
                    most = Mathf.Max(most, Mathf.Abs(r.SideEdge)); gMost = Mathf.Max(gMost, Mathf.Abs(r.SideSlope));
                    if (tHalf < 0f && Mathf.Abs(h) < Mathf.Abs(a0) * 0.5f) tHalf = (i + 1) * Dt;
                    if (t5 < 0f && Mathf.Abs(h) < 5f * Mathf.Deg2Rad) { t5 = (i + 1) * Dt; break; }
                }
                float least = -1f;
                for (float s = 0.05f; s <= 1f && least < 0f; s += 0.05f)
                {
                    from = Launch(r, v, a0);
                    bool held = true;
                    for (int i = 0; i < 120 * 3 && held; i++)
                    {
                        r.Steer = s * side; StepHeld(r, v);
                        if (r.Airborne || r.Crashed) break;
                        if ((r.TravelHeading - from) * side < -5f * Mathf.Deg2Rad) held = false;
                    }
                    if (held) least = s;
                }
                Debug.Log($"STEER across {20f * side:+0;-0} deg at {v * 3.6f:0} km/h, hands off: half way to the fall line in {T(tHalf)}, within 5 deg of it in {T(t5)}{(off ? " (left the snow first)" : "")}; it turned {mostRate * Mathf.Rad2Deg:0} deg/s at most, pulled {most:0} m/s^2 by the snow's resistance to the slide and {gMost:0.0} by gravity across the path | the finger held up the hill: {(least < 0f ? "not held by the whole drag" : $"{least * 100f:0}% of the drag holds the traverse")}");
                if (!off && t5 < 0f) Fail($"across {20f * side:+0;-0} at {v * 3.6f:0} km/h: hands off, the board did not come round to the fall line in 6 s");
                if (tHalf >= 0f && tHalf < 0.12f) Fail($"across {20f * side:+0;-0} at {v * 3.6f:0} km/h: hands off it came half way round in {tHalf:0.00} s: a snap");
            }
    }

    /// <summary>On the stretch at v, the board and its travel both `angle` off the fall line (rad), hands off.</summary>
    static float Launch(Rider r, float v, float angle)
    {
        Place(r, v);
        for (int i = 0; i < 30; i++) { r.Steer = 0f; StepHeld(r, v); }
        float a = r.FallLine + angle;
        var vel = r.Body.linearVelocity; float h = new Vector2(vel.x, vel.z).magnitude;
        r.Body.linearVelocity = new Vector3(Mathf.Sin(a) * h, vel.y, Mathf.Cos(a) * h);
        r.Heading = a;
        return a;
    }

    // ---- a 400 m stretch where a straight run stays on the snow (no kickers, no drops)
    static void FindStretch(Rider r)
    {
        foreach (float z in new[] { 400f, 600f, 800f, 1000f, 1200f, 300f, 1500f, 1700f, 1900f, 2100f })
            foreach (float off in new[] { 12f, -12f, 0f, 16f, -16f })
            {
                x0 = Slope.Centre(z) + off; z0 = z;
                bool air = false;
                // (not where the run itself swings across the hill: a cliff band's traverse is BandTest's)
                for (float d = -30f; d <= 420f && !air; d += 10f) if (Mathf.Abs(Slope.RunHeading(z + d)) > 0.02f) air = true;
                if (air) continue;
                // (and corduroy, as the mountain lays it, where the response times are taken: the board's own numbers)
                for (float d = -25f; d <= 80f && !air; d += 5f)
                    for (float a = -4f; a <= 4f; a += 4f)
                        if (Surfaces.Surface(Slope.Centre(z + d) + off + a, z + d).Corduroy < 0.98f) { air = true; break; }
                if (air) continue;
                // (at every speed the test rides it at: a box boarded only at 108 km/h read as a slow response there)
                foreach (float v in new[] { 22f, 15f, 30f })
                {
                    Place(r, v);
                    // (the whole stretch at the slalom's 79 km/h; at the others, the 80 m the response times are taken on.
                    // Faster than that every stretch of this mountain throws the board off a roller somewhere: the
                    // slaloms count the gates up to there)
                    float far = v == 22f ? 400f : 80f;
                    for (int i = 0; i < 120 * 18 && r.Z < z0 + far && !air; i++)
                    {
                        r.Steer = Hold(r, PathX(r.Z + 10f, 0f, 1f));
                        StepHeld(r, v);
                        if (r.Airborne || r.Crashed || r.Grinding) air = true;   // (nor onto a box, a rail or a lift's ramp)
                    }
                    if (air) break;
                }
                // (and the response rides themselves, as they are ridden: on the snow all the way, at each speed)
                foreach (float v in new[] { 15f, 22f, 30f })
                    foreach (float s in new[] { 1f, RiderInput.Curve(0.5f) })
                    {
                        if (air) break;
                        First(r, v, s, out _, out _, out _, out _);
                        if (sideAir > 0) air = true;
                    }
                if (!air) return;
            }
        Debug.LogWarning("STEER no clean stretch found; using the last one tried");
    }

    // piste-following line, offset by `swing` cosine-weaving through gates `spacing` apart
    static float PathX(float z, float swing, float spacing)
    {
        float baseX = x0 + Slope.Centre(z) - Slope.Centre(z0);
        if (swing <= 0f) return baseX;
        float g0 = z0 + 30f;
        if (z < g0 - spacing) return baseX - swing;
        return baseX + swing * -Mathf.Cos(Mathf.PI * (z - g0) / spacing);
    }
    static float GateX(int k, Course c) => PathX(z0 + 30f + k * c.spacing, c.swing, c.spacing);

    static float Hold(Rider r, float tx) => Mathf.Clamp((tx - r.Body.position.x) * 0.15f - r.Heading * 1.2f, -1f, 1f);

    static void Place(Rider r, float v)
    {
        r.ResetAt(x0, z0 - 25f);
        r.Begin();
        r.Body.linearVelocity = new Vector3(0f, 0f, v);
        // settle onto the line, at speed, before the stretch starts
        for (int i = 0; i < 120 * 3 && r.Z < z0; i++) { r.Steer = Hold(r, PathX(r.Z + 8f, 0f, 1f)); StepHeld(r, v); }
    }

    static void StepHeld(Rider r, float v)
    {
        r.Tick(Dt);
        if (v > 0f && !r.Airborne && !r.Crashed)
        {
            // (the speed along the snow, which is what Speed reads and what the turn's physics run on)
            var vel = r.Body.linearVelocity; float h = vel.magnitude;
            if (h > 0.5f) r.Body.linearVelocity = vel * (v / h);
        }
        Physics.Simulate(Dt);
    }

    static string T(float t) => t < 0f ? "never" : $"{t:0.00} s";

    static float SteerTo(float steer, float target) => Mathf.Lerp(steer, target, 1f - Mathf.Exp(-Dt * 60f));   // (RiderInput's own low-pass)

    // the sideways speed across the fall line (Rider.Side), against what the board has left to itself (the zero)
    static float lat0;
    static float Lat(Rider r) => r.Side - lat0;
    static void Settle(Rider r, float v)
    {
        Place(r, v);
        // (hands off long enough for any slide left from settling onto the line to have died away: feel's slide coasts
        // and returns over a second and a half)
        for (int i = 0; i < 240; i++) { r.Steer = 0f; StepHeld(r, v); }
        lat0 = 0f; lat0 = Lat(r);
    }

    static int sideAir;   // steps of the last First spent off the snow
    static float firstT90, releaseCoast; static bool releaseAir;   // (First: how soon 90% of what it settles at; Release: how long it coasts)

    /// <summary>The finger thrown to `s` from riding straight: how soon the board is going 1 and 3 m/s sideways, how much
    /// 0.05 s on, and what it settles at (the mean of 0.8 to 1 s).</summary>
    static void First(Rider r, float v, float s, out float t1, out float t3, out float at05, out float steady)
    {
        Settle(r, v);
        float steer = 0f; t1 = -1f; t3 = -1f; at05 = 0f; sideAir = 0; double sum = 0; int n = 0;
        for (int i = 0; i < 120; i++)
        {
            steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v);
            if (r.Airborne || r.Grinding) sideAir++;
            float l = Lat(r) * Mathf.Sign(s);
            if (t1 < 0f && l >= 1f) t1 = (i + 1) * Dt;
            if (t3 < 0f && l >= 3f) t3 = (i + 1) * Dt;
            if (i == 5) at05 = l;
            trace[i] = l;
            if (i >= 96) { sum += l; n++; }
        }
        steady = (float)(sum / Mathf.Max(1, n));
        firstT90 = -1f;
        for (int i = 0; i < trace.Length; i++) if (trace[i] >= 0.9f * steady) { firstT90 = (i + 1) * Dt; break; }
    }
    static readonly float[] trace = new float[120];

    /// <summary>A whole drag held one way for a second, then flipped: how soon the sideways speed is through zero, 2 m/s the
    /// other way, and half of the whole the other way.</summary>
    static void Reverse(Rider r, float v, float s, out float t0, out float t2, out float tHalf)
    {
        Settle(r, v);
        float steer = 0f; t0 = -1f; t2 = -1f; tHalf = -1f;
        for (int i = 0; i < 120; i++) { steer = SteerTo(steer, -s); r.Steer = steer; StepHeld(r, v); }
        float was = Mathf.Abs(Lat(r));
        for (int j = 0; j < 120; j++)
        {
            steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v);
            float l = Lat(r) * Mathf.Sign(s);
            if (t0 < 0f && l >= 0f) t0 = (j + 1) * Dt;
            if (t2 < 0f && l >= 2f) t2 = (j + 1) * Dt;
            if (l >= 0.5f * was) { tHalf = (j + 1) * Dt; break; }
        }
    }

    /// <summary>A whole drag held for a second, then the finger lifted: how soon a tenth of the sideways speed is left, how
    /// soon the way it goes is within 5 degrees of the fall line, and the hardest it is pulled back (m/s^2).</summary>
    static void Release(Rider r, float v, float s, out float tTenth, out float t5deg, out float back)
    {
        Settle(r, v);
        float steer = 0f; tTenth = -1f; t5deg = -1f; back = 0f;
        for (int i = 0; i < 120; i++) { steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v); }
        float was = Mathf.Abs(r.Side);
        releaseCoast = -1f; releaseAir = false;
        for (int j = 0; j < 480; j++)
        {
            steer = SteerTo(steer, 0f); r.Steer = steer; StepHeld(r, v);
            back = Mathf.Max(back, Mathf.Abs(r.SideEdge));
            if (releaseCoast < 0f && Mathf.Abs(r.Side) <= 0.9f * was) releaseCoast = (j + 1) * Dt;
            if (r.Airborne) releaseAir = true;
            if (tTenth < 0f && Mathf.Abs(r.Side) <= 0.1f * was) tTenth = (j + 1) * Dt;
            if (t5deg < 0f && Mathf.Abs(r.TravelHeading - r.FallLine) <= 5f * Mathf.Deg2Rad) t5deg = (j + 1) * Dt;
            if (tTenth >= 0f && t5deg >= 0f) break;
        }
    }

    /// <summary>`len` m down the stretch from v0 with the speed its own: straight, or (weave) a whole drag one way and the
    /// other across a 10 m lane. The mean speed along the snow and the speed at the end.</summary>
    static string freeNote;
    static void Free(Rider r, float v0, float len, bool weave, out float mean, out float end)
    {
        Place(r, v0);
        double sum = 0; int n = 0; float steer = 0f, way = 1f, air = 0f, offMost = 0f, rough = 0f;
        for (int i = 0; i < 120 * 30 && r.Z < z0 + len && !r.Crashed; i++)
        {
            // (the whole drag all the time: toward the line whenever it is 5 m off it, so the ride stays on the piste)
            // (looked at half a second ahead, so the turn back starts before the lane's edge, not past it)
            float offLine = r.Body.position.x - PathX(r.Z, 0f, 1f), offSoon = offLine + (r.Velocity.x - (PathX(r.Z + 1f, 0f, 1f) - PathX(r.Z, 0f, 1f)) * r.Velocity.z) * 0.5f;
            if (weave && Mathf.Abs(offSoon) > 5f) way = -Mathf.Sign(offSoon);
            float want = weave ? way : Hold(r, PathX(r.Z + 10f, 0f, 1f));
            steer = SteerTo(steer, want); r.Steer = steer;
            StepHeld(r, 0f);
            sum += r.Speed; n++;
            if (r.Airborne) air += Dt;
            offMost = Mathf.Max(offMost, Mathf.Abs(offLine)); rough = Mathf.Max(rough, 1f - r.Underfoot.Corduroy);
        }
        mean = (float)(sum / Mathf.Max(1, n)); end = r.Speed;
        freeNote = $"{n * Dt:0.0} s, {air:0.0} s of it in the air, {offMost:0.0} m off the line at most, snow other than corduroy {rough * 100f:0}% at most{(r.Crashed ? ", CRASHED" : "")}";
    }

    // ---- the old bot: aims at the line through the gates a look-ahead ahead, steering by the angle to it
    static int BestPursuit(Rider r, Course c, float v)
    {
        int best = 0;
        foreach (float look in new[] { 0.12f, 0.3f })
            foreach (float kp in new[] { 2f, 8f })
                foreach (float kd in new[] { 0f, 0.25f })
                    best = Mathf.Max(best, Pursuit(r, c, v, look, kp, kd));
        return best;
    }

    static int Pursuit(Rider r, Course c, float v, float look, float kp, float kd)
    {
        Place(r, v);
        int made = 0, next = 0;
        float zEnd = z0 + 30f + (c.gates - 1) * c.spacing + 1f;
        float prevZ = r.Z, prevX = r.Body.position.x;
        for (int i = 0; i < 120 * 30 && next < c.gates && !r.Crashed; i++)
        {
            float sp = Mathf.Max(5f, r.Speed);
            float la = Mathf.Max(2f, sp * look);
            float gz = z0 + 30f + next * c.spacing;
            float az = Mathf.Min(r.Z + la, gz);
            float ax = az >= gz - 0.01f ? GateX(next, c) : PathX(az, c.swing, c.spacing);
            if (az - r.Z < 1f) { az = r.Z + la; ax = PathX(az, c.swing, c.spacing); }
            float want = Mathf.Atan2(ax - r.Body.position.x, az - r.Z);
            r.Steer = Mathf.Clamp(kp * Mathf.DeltaAngle(r.TravelHeading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad - kd * r.YawRate, -1f, 1f);
            StepHeld(r, v);
            Gates(r, c, ref next, ref made, ref prevZ, ref prevX);
            if (r.Z > zEnd) break;
        }
        return made;
    }

    static void Gates(Rider r, Course c, ref int next, ref int made, ref float prevZ, ref float prevX)
    {
        float z = r.Z, x = r.Body.position.x, gz = z0 + 30f + next * c.spacing;
        while (next < c.gates && prevZ < gz && z >= gz)
        {
            float xc = Mathf.Lerp(prevX, x, (gz - prevZ) / Mathf.Max(1e-4f, z - prevZ));
            if (Mathf.Abs(xc - GateX(next, c)) <= GateW * 0.5f) made++;
            next++; gz = z0 + 30f + next * c.spacing;
        }
        prevZ = z; prevX = x;
    }

    // ---- the racer: rides a cosine through the gates (inside their middles by a margin): the sideways speed that
    //      line has a moment ahead (the board's own lag), and its error steered out, asked of the finger (CarveBot.ForSide)
    struct Result { public int made, tried; public float air; public bool Clean => made == tried; }

    static Result Racer(Rider r, Course c, float v, float amp, float lead, float kp)
    {
        Place(r, v);
        var res = new Result();
        int next = 0;
        float zEnd = z0 + 30f + (c.gates - 1) * c.spacing + 1f, g0 = z0 + 30f;
        float prevZ = r.Z, prevX = r.Body.position.x, steer = 0f;
        for (int i = 0; i < 120 * 40 && next < c.gates && !r.Crashed; i++)
        {
            float sp = Mathf.Max(5f, r.Speed), om = Mathf.PI * sp / c.spacing;
            Vector3 p = r.Body.position, vel = r.Body.linearVelocity;
            float baseX = PathX(p.z, 0f, 1f), baseV = (PathX(p.z + 1f, 0f, 1f) - baseX) * vel.z;
            float zl = p.z + sp * lead;
            float xd = p.z < g0 - c.spacing ? -amp : -amp * Mathf.Cos(Mathf.PI * (p.z - g0) / c.spacing);
            float vd = zl < g0 - c.spacing ? 0f : amp * om * Mathf.Sin(Mathf.PI * (zl - g0) / c.spacing);
            float want = CarveBot.ForSide(r, baseV + vd + kp * (xd - (p.x - baseX)));
            steer = SteerTo(steer, want); r.Steer = steer;
            StepHeld(r, v);
            if (r.Airborne) res.air += Dt;
            if (res.air > 0.1f) break;   // (off a roller: what comes after is the air's, not the steering's)
            Gates(r, c, ref next, ref res.made, ref prevZ, ref prevX);
            res.tried = next;
            if (r.Z > zEnd) break;
        }
        return res;
    }

    static Result BestRacer(Rider r, Course c, float v)
    {
        var best = new Result { made = -1 };
        foreach (float amp in new[] { c.swing - 1.1f, c.swing - 0.6f, c.swing - 1.4f })
            foreach (float lead in new[] { 0.04f, 0.08f, 0.13f, 0.19f })
                foreach (float kp in new[] { 6f, 14f })
                {
                    var m = Racer(r, c, v, amp, lead, kp);
                    if (best.made < 0 || m.tried - m.made < best.tried - best.made || (m.tried - m.made == best.tried - best.made && m.made > best.made)) best = m;
                    if (best.Clean && best.tried == c.gates) return best;
                }
        return best;
    }

    static float Straight(Rider r, float v, float len, out float most)
    {
        Place(r, v);
        most = 0f;
        for (int i = 0; i < 120 * 40 && r.Z < z0 + 30f + len; i++) { r.Steer = Hold(r, PathX(r.Z + 10f, 0f, 1f)); StepHeld(r, 0f); most = Mathf.Max(most, r.Speed); }
        return r.Speed;
    }
}
