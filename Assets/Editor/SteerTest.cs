using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// The carving model, measured the way a rider feels it (carve: the finger sets the board's edge angle; the edge
/// carves an arc, and skids past its grip). At a few held speeds and settings of the TURN slider (Rider.TurnScale):
///  - the time from the finger to the edge (90% of the angle asked for);
///  - the arc: the radius the board's travel settles to, its slip angle, whether the edge has let go and what the
///    skid takes off the speed, at a quarter, half, three quarters and the whole of the throw;
///  - how soon a full drag has the board going 3 m/s sideways, and the reversal: from 2 m/s one way, the finger
///    flipped, through zero and to 2 m/s the other way;
///  - a slalom between pairs of "trees" (gates 3 m wide): the old pursuit bot, and a racer that rides a line through
///    the gates with the turn fed forward, both at a held speed (a pure carve) and with the speed left to itself
///    (it skids to take speed off when it is going too fast for the gates: a hard edge and a skid).
/// Then: the fastest held speed at which each course is made clean (what cruising speed is comfortable in the
/// trees), what the pace knob (Rider.Pace) does to the speed a straight run reaches, the board left to itself across
/// a slope (gravity's drift to the fall line: how slow, how gentle) and held across it on an edge, and the same arc
/// on each kind of snow.
/// The speed is held (reset every step) where it says so, so each case rides the same line at the same pace.
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

    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        Physics.simulationMode = SimulationMode.Script;
        TestPrefs.Pin();
        Slope.Init(1234);
        var r = new GameObject("Rider").AddComponent<Rider>();   // (no forest: the trees here are the gates)
        float scale0 = Scale, pace0 = Rider.Pace;

        FindStretch(r);
        Debug.Log($"STEER stretch x={x0:0} z={z0:0} (x-centre {x0 - Slope.Centre(z0):0}), pitch {Mathf.Asin(Mathf.Sqrt(1f - Mathf.Pow(Slope.Normal(x0, z0 + 40f).y, 2f))) * Mathf.Rad2Deg:0} deg; edge time {Rider.EdgeTime:0.00} s, carve radius {Rider.CarveRadius:0.0} m, grip {Rider.EdgeGrip:0.0} g ({Rider.EdgeGrip * Rider.G:0} m/s^2), knee {Rider.EdgeKnee:0.00}, pace {Rider.Pace:0.00}");

        float[] speeds = { 15f, 22f, 30f };
        foreach (float sc in new[] { 0.5f, 1f, 1.5f, 2.5f })
        {
            Scale = sc;
            foreach (float v in speeds)
            {
                float tEdge = EdgeTime(r, v, 1f, out float edgeDeg);
                Sideways(r, v, 1f, out float l3, out float acc);
                Reverse(r, v, 1f, out float r0, out float r3);
                Debug.Log($"STEER edge scale={sc:0.0} v={v:0} m/s ({v * 3.6f:0} km/h) full drag: finger to edge (90% of {edgeDeg:0} deg) in {T(tEdge)}; sideways 3 m/s in {T(l3)}, {acc:0} m/s^2 sideways at most | reversal from -{RevSpeed:0} m/s: through zero in {T(r0)}, +{RevSpeed:0} m/s in {T(r3)}");
                if (sc == 1f && (tEdge < 0f || tEdge > 0.16f)) Debug.Log($"STEER-FAIL the edge takes {T(tEdge)} from the finger at {v * 3.6f:0} km/h (0.1 s asked)");
                string arcs = "";
                foreach (float throw_ in new[] { 0.25f, 0.5f, 0.75f, 1f })
                {
                    Arc(r, v, RiderInput.Curve(throw_), out float radius, out float slipDeg, out bool sliding, out float brake, out float edge);
                    arcs += $" | drag {throw_ * 100f:0}% (edge {edge:0} deg): radius {(radius > 999f ? "straight" : radius.ToString("0.0") + " m")}, slip {slipDeg:0} deg{(sliding ? $", SKID taking {brake:0} m/s^2 off" : "")}";
                }
                Debug.Log($"STEER arc scale={sc:0.0} v={v:0} m/s ({v * 3.6f:0} km/h){arcs}");
                if (quick || sc != 1f) continue;   // (the slaloms at the stock setting)
                string line = $"STEER slalom scale={sc:0.0} v={v:0} m/s ({v * 3.6f:0} km/h)";
                foreach (var c in Courses)
                {
                    int old = BestPursuit(r, c, v);
                    Result held = BestRacer(r, c, v, true), free = BestRacer(r, c, v, false);
                    line += $" | {c.name}: pursuit bot {old}/{c.gates}, racer held {held.made}/{c.gates}, racer with the speed its own {free.made}/{c.gates} (down to {free.minV * 3.6f:0} km/h, {free.skid:0.0} s skidding)";
                }
                Debug.Log(line);
            }
        }
        Scale = 1f;

        // the fastest held speed at which each course is made clean, carving: what is comfortable in the trees
        if (!quick)
            foreach (var c in Courses)
            {
                float clean = 0f, most = 0f;
                for (float v = 8f; v <= 34f; v += 2f)
                {
                    var m = BestRacer(r, c, v, true);
                    if (m.made == c.gates) most = v;
                    if (m.made == c.gates && (clean == v - 2f || v == 8f)) clean = v;
                }
                Debug.Log($"STEER comfortable {c.name} ({c.swing * 2f:0} m side to side every {c.spacing:0} m): every gate at every held speed up to {clean * 3.6f:0} km/h (the fastest clean run: {most * 3.6f:0} km/h)");
            }

        // the pace knob: a straight run down the stretch from 22 m/s, the speed left to itself
        foreach (float pace in new[] { 1f, 0.8f, 0.64f, 0.5f, 0.36f, 0.25f })
        {
            Rider.Pace = pace;
            float end = Straight(r, 22f, 400f, out float most);
            Debug.Log($"STEER pace {pace:0.00}: straight down 400 m of the stretch from 79 km/h: {end * 3.6f:0} km/h at the end, {most * 3.6f:0} at most");
        }
        Rider.Pace = pace0;

        // what weaving costs in speed: the glade from 22 m/s, the speed left to itself
        if (!quick)
        {
            var m = BestRacer(r, Courses[0], 22f, false);
            float straightEnd = Straight(r, 22f, Courses[0].spacing * Courses[0].gates + 30f, out _);
            Debug.Log($"STEER free glade from 22 m/s along the snow: {m.made}/{Courses[0].gates} gates, 79 -> {m.endV * 3.6f:0} km/h (straight down the same stretch: {straightEnd * 3.6f:0} km/h)");
        }

        Across(r);

        // the snow underfoot (Surfaces): the same arcs on each kind laid everywhere. Each kind holds less than corduroy:
        // the same edge lets go sooner on it
        foreach (Snow kind in System.Enum.GetValues(typeof(Snow)))
        {
            Surfaces.Force = SnowMix.Only(kind);
            foreach (float v in new[] { 22f, 30f })
            {
                float lets = -1f; string arcs = "";
                for (float throw_ = 0.1f; throw_ <= 1.001f; throw_ += 0.05f)
                {
                    Arc(r, v, RiderInput.Curve(throw_), out _, out _, out bool sl, out _, out _);
                    if (sl) { lets = throw_; break; }
                }
                foreach (float throw_ in new[] { 0.5f, 0.75f, 1f })
                {
                    Arc(r, v, RiderInput.Curve(throw_), out float radius, out float slipDeg, out bool sliding, out float brake, out _);
                    arcs += $" | drag {throw_ * 100f:0}%: radius {radius:0.0} m, slip {slipDeg:0} deg{(sliding ? $", SKID -{brake:0} m/s^2" : "")}";
                }
                Sideways(r, v, 1f, out float l3, out float acc);
                Debug.Log($"STEER snow {kind.ToString().ToLower(),-8} v={v:0} m/s ({v * 3.6f:0} km/h): lets go at {(lets < 0f ? "no drag" : $"{lets * 100f:0}% of the drag")}; {acc:0} m/s^2 sideways at most, 3 m/s sideways in {T(l3)}{arcs}");
            }
        }
        Surfaces.Force = null;

        Scale = scale0;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Gravity across the path, on the stretch (its pitch is the hill's own): the board set going 35 degrees across the
    /// fall line and left to itself: how the way it goes comes round to the fall line (the time to half way and to
    /// within 5 degrees, the fastest it turns), then the same start held on an edge into the hill: the least edge that
    /// holds the traverse for 3 s (within 5 degrees of how it started). STEER-FAIL if hands off it does not come round,
    /// comes round in under a second (a snap) or turns faster than gravity alone could turn it.
    /// </summary>
    static void Across(Rider r)
    {
        foreach (float v in new[] { 15f, 22f, 30f })
            foreach (float side in new[] { 1f, -1f })
            {
                float a0 = 35f * Mathf.Deg2Rad * side;
                float from = Launch(r, v, a0);
                float tHalf = -1f, t5 = -1f, mostRate = 0f, prev = r.TravelHeading, most = 0f, gMost = 0f; bool off = false;
                for (int i = 0; i < 120 * 10; i++)
                {
                    r.Steer = 0f; StepHeld(r, v);
                    if (r.Airborne || r.Crashed || r.Grinding) { off = true; break; }
                    float h = r.TravelHeading - r.FallLine, rate = Mathf.Abs(r.TravelHeading - prev) / Dt; prev = r.TravelHeading;
                    if (i > 5) mostRate = Mathf.Max(mostRate, rate);
                    most = Mathf.Max(most, Mathf.Abs(r.SideEdge)); gMost = Mathf.Max(gMost, Mathf.Abs(r.SideSlope));
                    if (tHalf < 0f && Mathf.Abs(h) < Mathf.Abs(a0) * 0.5f) tHalf = (i + 1) * Dt;
                    if (t5 < 0f && Mathf.Abs(h) < 5f * Mathf.Deg2Rad) { t5 = (i + 1) * Dt; break; }
                }
                // held on an edge into the hill (the finger the way the board is already going: up the hill's side)
                float least = -1f;
                for (float s = 0.02f; s <= 0.6f && least < 0f; s += 0.02f)
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
                Debug.Log($"STEER across {35f * side:+0;-0} deg at {v * 3.6f:0} km/h, hands off: half way to the fall line in {T(tHalf)}, within 5 deg of it in {T(t5)}{(off ? " (left the snow first)" : "")}; it turned {mostRate * Mathf.Rad2Deg:0} deg/s at most, by gravity's {gMost:0.0} m/s^2 across the path (the flat board's own edge: {most:0.0} m/s^2) | held on an edge into the hill: {(least < 0f ? "not held by 60% of the drag" : $"{least * 100f:0}% of the drag holds the traverse")}");
                if (!off && t5 < 0f) Debug.Log($"STEER-FAIL across {35f * side:+0;-0} at {v * 3.6f:0} km/h: hands off, the board never came round to the fall line in 10 s");
                if (tHalf >= 0f && tHalf < 0.5f) Debug.Log($"STEER-FAIL across {35f * side:+0;-0} at {v * 3.6f:0} km/h: hands off it came half way round in {tHalf:0.00} s: a snap");
                if (mostRate * v > gMost + most + 1.5f) Debug.Log($"STEER-FAIL across {35f * side:+0;-0} at {v * 3.6f:0} km/h: it turned at {mostRate * v:0.0} m/s^2, more than gravity ({gMost:0.0}) and the flat board's edge ({most:0.0}) pull it");
            }
    }

    /// <summary>On the stretch at v, the board and its travel both `angle` off straight down (rad), edge flat.</summary>
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
        foreach (float z in new[] { 400f, 600f, 800f, 1000f, 1200f, 300f, 1500f, 1700f, 1900f, 2100f, 2300f, 2500f })
            foreach (float off in new[] { 12f, -12f, 0f, 16f, -16f })
            {
                x0 = Slope.Centre(z) + off; z0 = z;
                bool air = false;
                // (and corduroy, as the mountain lays it, where the response times are taken: the board's own numbers)
                for (float d = -25f; d <= 80f && !air; d += 5f)
                    for (float a = -4f; a <= 4f; a += 4f)
                        if (Surfaces.Surface(Slope.Centre(z + d) + off + a, z + d).Corduroy < 0.98f) { air = true; break; }
                if (air) continue;
                // (at every speed the test rides it at: a box boarded only at 108 km/h read as a slow response there)
                foreach (float v in new[] { 22f, 15f, 30f })
                {
                    Place(r, v);
                    // (the whole stretch at the slalom's 79 km/h; at the others, the 80 m the response times are taken on)
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
                        Sideways(r, v, s, out _, out _);
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

    // sideways speed, from what the board does left to itself: on the line, then half a second hands off, and the
    // sideways speed it has then is the zero
    const float RevSpeed = 2f;   // the reversal starts going left this fast
    static float lat0;
    static float Lat(Rider r) => r.Body.linearVelocity.x - lat0;
    static void Settle(Rider r, float v)
    {
        Place(r, v);
        for (int i = 0; i < 60; i++) { r.Steer = 0f; StepHeld(r, v); }
        lat0 = 0f; lat0 = Lat(r);
    }

    static int sideAir;   // steps of the last Sideways spent off the snow

    /// <summary>The finger thrown to `s` from riding straight: how long until the edge is 90% of the angle it settles at (deg).</summary>
    static float EdgeTime(Rider r, float v, float s, out float edgeDeg)
    {
        Settle(r, v);
        var lean = new float[60]; float steer = 0f;
        for (int i = 0; i < lean.Length; i++) { steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v); lean[i] = Mathf.Abs(r.Lean); }
        float full = lean[lean.Length - 1];
        edgeDeg = full * Mathf.Rad2Deg;
        for (int i = 0; i < lean.Length; i++) if (lean[i] >= 0.9f * full) return (i + 1) * Dt;
        return -1f;
    }

    /// <summary>A drag held from riding straight: how soon the board is going 3 m/s sideways, and the most the edge pulls it sideways (m/s^2) in the first second.</summary>
    static void Sideways(Rider r, float v, float s, out float t3, out float acc)
    {
        Settle(r, v);
        float steer = 0f; t3 = -1f; acc = 0f; sideAir = 0;
        for (int i = 0; i < 120; i++)
        {
            steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v);
            if (r.Airborne || r.Grinding) sideAir++;
            acc = Mathf.Max(acc, Mathf.Abs(r.SideEdge));
            if (t3 < 0f && Lat(r) >= 3f) t3 = (i + 1) * Dt;
            if (Mathf.Abs(r.Heading) > 1.2f) break;   // (round across the hill)
        }
    }

    /// <summary>
    /// A drag held from riding straight, 0.45 s on (the arc has settled; a full edge has not yet brought the board
    /// round across the hill): the radius of the way it is going, the slip, whether the edge has let go and what
    /// that takes off the speed (m/s^2), and the edge angle.
    /// </summary>
    static void Arc(Rider r, float v, float s, out float radius, out float slipDeg, out bool sliding, out float brake, out float edgeDeg)
    {
        Settle(r, v);
        float steer = 0f, turned = 0f, prev = r.TravelHeading, slip = 0f, br = 0f; int n = 0, slid = 0;
        for (int i = 0; i < 54; i++)
        {
            steer = SteerTo(steer, s); r.Steer = steer;
            float before = r.Body.linearVelocity.magnitude;
            r.Tick(Dt);
            float after = r.Body.linearVelocity.magnitude;
            if (!r.Airborne && !r.Crashed) { var vel = r.Body.linearVelocity; float h = vel.magnitude; if (h > 0.5f) r.Body.linearVelocity = vel * (v / h); }
            Physics.Simulate(Dt);
            float th = r.TravelHeading;
            if (i >= 42) { turned += Mathf.Abs(th - prev); slip += Mathf.Abs(r.Slip); n++; if (r.Sliding) { slid++; br += Mathf.Abs(r.SideEdge) * Mathf.Abs(Mathf.Tan(r.Slip)); } }
            prev = th;
        }
        float rate = turned / (n * Dt);
        radius = rate > 1e-4f ? v / rate : 9999f;
        slipDeg = slip / n * Mathf.Rad2Deg;
        sliding = slid > n / 2;
        brake = slid > 0 ? br / slid : 0f;
        edgeDeg = Mathf.Abs(r.Lean) * Mathf.Rad2Deg;
    }

    // going left at RevSpeed, then the finger flipped right: how soon the old way is gone, and how soon the new one comes
    static void Reverse(Rider r, float v, float s, out float t0, out float t3)
    {
        Settle(r, v);
        float steer = 0f; t0 = -1f; t3 = -1f;
        int i = 0;
        for (; i < 180 && Lat(r) > -RevSpeed; i++) { steer = SteerTo(steer, -s); r.Steer = steer; StepHeld(r, v); }
        if (Lat(r) > -RevSpeed) return;   // (never got going left that fast)
        for (int j = 0; j < 240; j++)
        {
            steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v);
            float l = Lat(r);
            if (t0 < 0f && l >= 0f) t0 = (j + 1) * Dt;
            if (l >= RevSpeed) { t3 = (j + 1) * Dt; break; }
        }
    }

    // ---- the old bot: aims at the line through the gates a look-ahead ahead, steering by the angle to it. It was
    //      written for a board that goes where the finger points; it is kept as the plainest rider there is
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

    // ---- the racer: rides a cosine through the gates (inside their middles by a margin), the sideways pull that line
    //      needs fed forward a moment early (the edge's own lag) as the arc to ask for, and its error steered out. With
    //      the speed its own, it throws the whole edge (a skid) while it is going faster than it can make the gates at
    struct Result { public int made; public float endV, minV, skid; }

    /// <summary>The steer that asks the edge for the arc `kappa` (1/m, + right) at speed sp: ArcAt inverted.</summary>
    static float SteerFor(Rider r, float kappa, float sp)
    {
        float knee = r.ArcAt(Rider.EdgeKnee, sp);   // (the arc at the knee: all the snow holds)
        float k = Mathf.Min(Mathf.Abs(kappa) / Mathf.Max(knee, 1e-5f), Rider.SkidOver);
        float share = k <= 1f ? Mathf.Atan(k * Mathf.Tan(Rider.MaxLean)) / Rider.MaxLean * Rider.EdgeKnee
                              : Rider.EdgeKnee + (k - 1f) / (Rider.SkidOver - 1f) * (1f - Rider.EdgeKnee);
        return Mathf.Clamp(Mathf.Sign(kappa) * share / Mathf.Clamp(Scale, 0.25f, 4f), -1f, 1f);
    }

    static Result Racer(Rider r, Course c, float v, float amp, float lead, float kp, float kd, bool held, float vt)
    {
        Place(r, v);
        var res = new Result { minV = v };
        int next = 0;
        float zEnd = z0 + 30f + (c.gates - 1) * c.spacing + 1f, g0 = z0 + 30f;
        float prevZ = r.Z, prevX = r.Body.position.x, steer = 0f;
        for (int i = 0; i < 120 * 40 && next < c.gates && !r.Crashed; i++)
        {
            float sp = Mathf.Max(5f, r.Speed), om = Mathf.PI * sp / c.spacing;
            Vector3 p = r.Body.position, vel = r.Body.linearVelocity;
            float baseX = PathX(p.z, 0f, 1f), baseV = (PathX(p.z + 1f, 0f, 1f) - baseX) * vel.z;
            float zl = p.z + sp * lead;
            float ad = zl < g0 - c.spacing ? 0f : amp * om * om * Mathf.Cos(Mathf.PI * (zl - g0) / c.spacing);
            float xd = p.z < g0 - c.spacing ? -amp : -amp * Mathf.Cos(Mathf.PI * (p.z - g0) / c.spacing);
            float vd = p.z < g0 - c.spacing ? 0f : amp * om * Mathf.Sin(Mathf.PI * (p.z - g0) / c.spacing);
            float a = ad + kp * (xd - (p.x - baseX)) + kd * (vd - (vel.x - baseV));
            float want = SteerFor(r, a / (sp * sp), sp);
            if (!held && r.Speed > vt) want = Mathf.Abs(a) > 1f ? Mathf.Sign(a) : (steer >= 0f ? 1f : -1f);   // (too fast for these gates: the whole edge, a skid)
            steer = SteerTo(steer, want); r.Steer = steer;
            StepHeld(r, held ? v : 0f);
            res.minV = Mathf.Min(res.minV, r.Speed);
            if (r.Sliding) res.skid += Dt;
            Gates(r, c, ref next, ref res.made, ref prevZ, ref prevX);
            if (r.Z > zEnd) break;
        }
        res.endV = r.Speed;
        return res;
    }

    static Result BestRacer(Rider r, Course c, float v, bool held)
    {
        var best = new Result { made = -1 };
        foreach (float amp in new[] { c.swing - 1.1f, c.swing - 0.6f })
            foreach (float lead in new[] { 0.06f, 0.12f, 0.2f })
                foreach (float kp in new[] { 50f })
                    foreach (float kd in new[] { 9f })
                        foreach (float vt in held ? new[] { 0f } : new[] { 11f, 13f, 15f, 18f })
                        {
                            var m = Racer(r, c, v, amp, lead, kp, kd, held, vt);
                            if (m.made > best.made || (m.made == best.made && m.minV > best.minV)) best = m;
                            if (held && best.made == c.gates) return best;
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
