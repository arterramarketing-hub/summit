using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// How tight and how quick the downhill steering is, measured the way a rider feels it: a slalom between pairs of
/// "trees" (gates 3 m wide), at a few held speeds and at a few settings of the TURN slider (Rider.TurnScale).
/// For each: the full-lock turn radius, how long the turn takes to build (90% of its turn rate, and the line of
/// travel swung 20 degrees), and how many gates a bot makes. The bot is not a tuned racer: for each case it tries a
/// small sweep of its own gains and look-ahead and the best of them is reported, so the number is what the board can
/// do, not how well one controller happens to fit it.
/// The speed is held (the board's speed is reset to it every step) so each case rides the same line at the same
/// pace; one free run then shows what weaving costs in speed.
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

    static float Scale
    {
        get { var p = typeof(Rider).GetProperty("TurnScale"); return p != null ? (float)p.GetValue(null) : 1f; }
        set { var p = typeof(Rider).GetProperty("TurnScale"); if (p != null && p.CanWrite) p.SetValue(null, value); }
    }
    static bool HasScale => typeof(Rider).GetProperty("TurnScale") != null;

    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        Physics.simulationMode = SimulationMode.Script;
        TestPrefs.Pin();
        Slope.Init(1234);
        var r = new GameObject("Rider").AddComponent<Rider>();   // (no forest: the trees here are the gates)
        float scale0 = Scale;

        FindStretch(r);
        Debug.Log($"STEER stretch x={x0:0} z={z0:0} (x-centre {x0 - Slope.Centre(z0):0}) slider={(HasScale ? "yes" : "none: the old steering")}");

        float[] speeds = { 15f, 22f, 30f };
        float[] scales = HasScale ? new[] { 0.5f, 1f, 1.5f, 2.5f } : new[] { 1f };
        foreach (float sc in scales)
        {
            Scale = sc;
            foreach (float v in speeds)
            {
                Response(r, v, out float radius, out float t90, out float t20, out float skid);
                string line = $"STEER scale={sc:0.0} v={v:0} m/s ({v * 3.6f:0} km/h): radius {radius:0.0} m, 90% turn rate in {t90:0.00} s, travel 20 deg in {t20:0.00} s, skid {skid:0.00}";
                foreach (float throw_ in new[] { 1f, 0.5f })
                {
                    float s = RiderInput.Curve(throw_);
                    Sideways(r, v, s, out float l3, out float l90, out float lss);
                    Reverse(r, v, s, out float r0, out float r3);
                    Debug.Log($"STEER side scale={sc:0.0} v={v:0} m/s ({v * 3.6f:0} km/h) drag {throw_ * 100f:0}% (steer {s:0.00}): sideways 3 m/s in {T(l3)}, 90% of its sideways speed ({lss:0.0} m/s at 1 s) in {T(l90)}{(sideAir > 0 ? $" (off the snow for {sideAir * Dt:0.00} s of it)" : "")} | reverse left->right from -{RevSpeed:0} m/s: through zero in {T(r0)}, +{RevSpeed:0} m/s in {T(r3)}");
                }
                foreach (var c in Courses)
                {
                    if (quick) break;
                    int best = 0; string how = "";
                    foreach (float look in new[] { 0.12f, 0.2f, 0.3f, 0.45f })
                        foreach (float kp in new[] { 2f, 4f, 8f, 16f })
                            foreach (float kd in new[] { 0f, 0.25f })
                            {
                                int made = Slalom(r, c, v, look, kp, kd, true, out _, out _);
                                if (made > best) { best = made; how = $"look {look:0.00}s kp {kp:0} kd {kd:0.00}"; }
                            }
                    line += $" | {c.name} {best}/{c.gates} ({how})";
                }
                Debug.Log(line);
            }
        }

        // what weaving costs in speed: the glade at the default setting, from 22 m/s, the speed left to itself
        Scale = 1f;
        {
            int made = 0, bestMade = -1; float endV = 0f, bestEnd = 0f, bestStart = 0f;
            foreach (float look in new[] { 0.12f, 0.2f, 0.3f, 0.45f })
                foreach (float kp in new[] { 2f, 4f, 8f, 16f })
                    foreach (float kd in new[] { 0f, 0.25f })
                    {
                        made = Slalom(r, Courses[0], 22f, look, kp, kd, false, out endV, out float startV);
                        if (made > bestMade) { bestMade = made; bestEnd = endV; bestStart = startV; }
                    }
            float straightEnd = Straight(r, 22f, Courses[0].spacing * Courses[0].gates + 30f);
            Debug.Log($"STEER free glade from 22 m/s along the snow: {bestMade}/{Courses[0].gates} gates, {bestStart * 3.6f:0} -> {bestEnd * 3.6f:0} km/h (straight down the same stretch: {straightEnd * 3.6f:0} km/h)");
        }

        // where the run swings across the hill (a traverse along a cliff band): a lifted thumb holds the run's line, and
        // a drag steers off it
        // (on corduroy, as a band's traverse is; then on ice and on powder, which are slower to answer the finger: the
        // line must be held hands off just the same)
        if (!quick || System.Environment.GetEnvironmentVariable("STEER_TRAVERSE") == "1")
            foreach (var kind in new[] { Snow.Corduroy, Snow.Ice, Snow.Powder }) Traverse(r, kind);

        // the snow underfoot (Surfaces): the same response rides on each kind laid everywhere. Ice and powder are
        // slower to come round, by design; none of them may leave the board short of the sideways speed asked for
        Scale = 1f;
        foreach (Snow kind in System.Enum.GetValues(typeof(Snow)))
        {
            Surfaces.Force = SnowMix.Only(kind);
            foreach (float v in new[] { 22f, 30f })
            {
                Sideways(r, v, 1f, out float l3, out float l90, out float lss);
                Reverse(r, v, 1f, out float r0, out float r3);
                Debug.Log($"STEER snow {kind.ToString().ToLower(),-8} v={v:0} m/s ({v * 3.6f:0} km/h) full drag: sideways 3 m/s in {T(l3)}, 90% of its sideways speed ({lss:0.0} m/s at 1 s) in {T(l90)} | reverse left->right from -2 m/s: through zero in {T(r0)}, +2 m/s in {T(r3)}");
                if (lss < 14.5f) Debug.Log($"STEER-FAIL snow {kind}: a full drag gives {lss:0.0} m/s sideways at {v * 3.6f:0} km/h, not 15");
            }
        }
        Surfaces.Force = null;

        Scale = scale0;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// A made-up traverse: Rider.RunHeading swung to 65 degrees across the hill (eased in over 40 m), each way, from
    /// well to the other side of the piste so the line crosses it. Hands off for 200 m of it: how far the board comes
    /// off the run's line (as the terrain's own heading would draw it). Then a full drag each way from the line: how
    /// fast it leaves it. STEER traverse lines; STEER-FAIL if the line is not held within a metre or a drag does not
    /// leave it at 3 m/s within 0.2 s.
    /// </summary>
    static void Traverse(Rider r, Snow kind = Snow.Corduroy)
    {
        var was = Rider.RunHeading; float xWas = x0, zWas = z0;
        Scale = 1f;
        Surfaces.Force = SnowMix.Only(kind);
        string on = kind == Snow.Corduroy ? "" : " on " + kind.ToString().ToLower();
        try
        {
            foreach (float side in new[] { 1f, -1f })
                foreach (float v in new[] { 22f, 30f })
                {
                    bool done = false;
                    foreach (float z in new[] { zWas, 400f, 600f, 800f, 1000f, 1200f, 1500f, 1700f, 1900f, 2100f })
                    {
                        z0 = z; x0 = Slope.Centre(z) - side * 85f;
                        float zA = z0 + 10f, rad = 65f * Mathf.Deg2Rad * side;
                        System.Func<float, float> run = zz => rad * Mathf.SmoothStep(0f, 1f, (zz - zA) / 40f);
                        // hands off: 200 m along it
                        Rider.RunHeading = zz => 0f;
                        Place(r, v);
                        Rider.RunHeading = run;
                        float xi = r.Body.position.x, zPrev = r.Z, gone = 0f, dev = 0f, air = 0f; int n = 0;
                        Vector3 pPrev = r.Body.position;
                        for (; n < 120 * 20 && gone < 200f && !r.Crashed; n++)
                        {
                            r.Steer = 0f; StepHeld(r, v);
                            Vector3 p = r.Body.position;
                            // (the line the run's heading draws from where it began, by the ground covered down the hill)
                            float zm = 0.5f * (zPrev + p.z);
                            xi += Mathf.Tan(run(zm)) * (p.z - zPrev);
                            if (p.z > zA) gone += new Vector2(p.x - pPrev.x, p.z - pPrev.z).magnitude;
                            dev = Mathf.Max(dev, Mathf.Abs(p.x - xi) * Mathf.Cos(run(p.z)));
                            if (r.Airborne) air += Dt;
                            zPrev = p.z; pPrev = p;
                        }
                        if (r.Crashed || r.Grinding) continue;   // (a tree or a rail on that line: another stretch)
                        float heading = r.TravelHeading * Mathf.Rad2Deg;
                        // a full drag each way, from on the line: the speed across the line 0.2 s on, and how far off it after 0.5 s
                        string drags = ""; bool left = true;
                        foreach (float drag in new[] { -1f, 1f })
                        {
                            Rider.RunHeading = zz => 0f;
                            Place(r, v);
                            Rider.RunHeading = run;
                            for (int i = 0; i < 120 * 20 && r.Z < zA + 60f && !r.Crashed; i++) { r.Steer = 0f; StepHeld(r, v); }
                            Vector3 p0 = r.Body.position; float h0 = run(p0.z), steer = 0f, across02 = 0f, off05 = 0f;
                            var acrossDir = new Vector2(Mathf.Cos(h0), -Mathf.Sin(h0));   // (to the right of the run's line)
                            Vector2 start = new Vector2(p0.x, p0.z), along = new Vector2(Mathf.Sin(h0), Mathf.Cos(h0));
                            for (int i = 0; i < 60; i++)
                            {
                                steer = SteerTo(steer, drag); r.Steer = steer; StepHeld(r, v);
                                Vector3 u = r.Body.linearVelocity;
                                if (i == 23) across02 = Vector2.Dot(new Vector2(u.x, u.z), acrossDir);
                                Vector2 q = new Vector2(r.Body.position.x, r.Body.position.z) - start;
                                off05 = Vector2.Dot(q - along * Vector2.Dot(q, along), acrossDir);
                            }
                            drags += $" | full drag {(drag < 0f ? "left" : "right")}: {across02:+0.0;-0.0} m/s across the line after 0.2 s, {off05:+0.0;-0.0} m off it after 0.5 s";
                            if (Mathf.Abs(across02) < 3f || Mathf.Sign(across02) != drag) left = false;
                        }
                        Debug.Log($"STEER traverse{on} 65 deg {(side > 0f ? "right" : "left")} at {v * 3.6f:0} km/h (z={z0:0}): hands off for {gone:0} m, {dev:0.00} m off the run's line at most, heading {heading:0} deg at the end, {air:0.0} s of it in the air{drags}");
                        if (dev > 1f) Debug.Log($"STEER-FAIL traverse{on} {(side > 0f ? "right" : "left")} {v * 3.6f:0} km/h: {dev:0.00} m off the run's line, hands off");
                        if (!left && kind == Snow.Corduroy) Debug.Log($"STEER-FAIL traverse {(side > 0f ? "right" : "left")} {v * 3.6f:0} km/h: a full drag does not leave the line at 3 m/s within 0.2 s");
                        done = true;
                        break;
                    }
                    if (!done) Debug.Log($"STEER-FAIL traverse {(side > 0f ? "right" : "left")} {v * 3.6f:0} km/h: no stretch where the ride stays up");
                }
        }
        finally { Rider.RunHeading = was; x0 = xWas; z0 = zWas; Surfaces.Force = null; }
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
                        Sideways(r, v, s, out _, out _, out _);
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

    // full lock from riding straight: the radius it settles to, how long it takes to get there
    static void Response(Rider r, float v, out float radius, out float t90, out float t20, out float skid)
    {
        Place(r, v);
        float tr0 = r.TravelHeading, steer = 0f;
        var yaws = new float[180]; t20 = -1f; skid = 0f;
        for (int i = 0; i < yaws.Length; i++)
        {
            steer = Mathf.Lerp(steer, 1f, 1f - Mathf.Exp(-Dt * 60f));   // (RiderInput's own low-pass)
            r.Steer = steer; StepHeld(r, v);
            yaws[i] = Mathf.Abs(r.YawRate);
            skid = Mathf.Max(skid, r.Skid);
            if (t20 < 0f && Mathf.Abs(Mathf.DeltaAngle(tr0 * Mathf.Rad2Deg, r.TravelHeading * Mathf.Rad2Deg)) >= 20f) t20 = (i + 1) * Dt;
            if (Mathf.Abs(r.Heading) > 1.3f) { System.Array.Resize(ref yaws, i + 1); break; }   // (the heading limit, across the hill)
        }
        float ss = 0f; foreach (float y in yaws) ss = Mathf.Max(ss, y);
        t90 = -1f;
        for (int i = 0; i < yaws.Length; i++) if (yaws[i] >= 0.9f * ss) { t90 = (i + 1) * Dt; break; }
        radius = v / Mathf.Max(1e-3f, ss);
    }

    static string T(float t) => t < 0f ? "never" : $"{t:0.00} s";

    // sideways speed, from what the board does left to itself: on the line, then half a second hands off (each
    // steering's own idea of straight), and the sideways speed it has then is the zero
    const float RevSpeed = 2f;   // the reversal starts going left this fast
    static float lat0;
    static float Lat(Rider r) => r.Body.linearVelocity.x - lat0;
    static void Settle(Rider r, float v)
    {
        Place(r, v);
        for (int i = 0; i < 60; i++) { r.Steer = 0f; StepHeld(r, v); }
        lat0 = 0f; lat0 = Lat(r);
    }

    static float SteerTo(float steer, float target) => Mathf.Lerp(steer, target, 1f - Mathf.Exp(-Dt * 60f));   // (RiderInput's own low-pass)

    // a drag held from riding straight: how soon the sideways speed comes, and how much of it there is
    static readonly bool trace = System.Environment.GetEnvironmentVariable("STEER_TRACE") == "1";
    static int sideAir;   // steps of the last Sideways spent off the snow (a real air there steers at AirControl, not as on the snow)
    static readonly bool quick = System.Environment.GetEnvironmentVariable("STEER_QUICK") == "1";   // only the response times
    static void Sideways(Rider r, float v, float s, out float t3, out float t90, out float steady)
    {
        Settle(r, v);
        var lat = new float[120]; float steer = 0f; t3 = -1f; sideAir = 0;
        for (int i = 0; i < lat.Length; i++)
        {
            steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v);
            lat[i] = Lat(r); if (r.Airborne || r.Grinding) sideAir++;
            if (trace && v > 29f) { var q = r.Body.position; var u = r.Body.linearVelocity; Debug.Log($"STEER-TRACE i={i} z={q.z:0.00} x={q.x:0.00} over={q.y - Rider.R - Slope.DrawnHeight(q.x, q.z):0.000} v=({u.x:0.00},{u.y:0.00},{u.z:0.00}) grind={r.Grinding} air={r.Airborne} steer={r.Steer:0.00} drawnSlopeAhead={(Slope.DrawnHeight(q.x, q.z + 0.5f) - Slope.DrawnHeight(q.x, q.z)) / 0.5f:0.000}"); }
            if (t3 < 0f && lat[i] >= 3f) t3 = (i + 1) * Dt;
        }
        steady = lat[lat.Length - 1];
        t90 = -1f;
        for (int i = 0; i < lat.Length; i++) if (lat[i] >= 0.9f * steady) { t90 = (i + 1) * Dt; break; }
    }

    // going left at RevSpeed, then the finger flipped right: how soon the old way is gone, and how soon the new one comes
    static void Reverse(Rider r, float v, float s, out float t0, out float t3)
    {
        Settle(r, v);
        float steer = 0f; t0 = -1f; t3 = -1f;
        int i = 0;
        for (; i < 180 && Lat(r) > -RevSpeed; i++) { steer = SteerTo(steer, -s); r.Steer = steer; StepHeld(r, v); }
        if (Lat(r) > -RevSpeed) return;   // (never got going left that fast)
        for (int j = 0; j < 180; j++)
        {
            steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v);
            float l = Lat(r);
            if (t0 < 0f && l >= 0f) t0 = (j + 1) * Dt;
            if (l >= RevSpeed) { t3 = (j + 1) * Dt; break; }
        }
    }

    static int Slalom(Rider r, Course c, float v, float look, float kp, float kd, bool held, out float endV, out float startV)
    {
        Place(r, v);
        startV = r.Speed;
        int made = 0, next = 0;
        float zEnd = z0 + 30f + (c.gates - 1) * c.spacing + 1f;
        float prevZ = r.Z, prevX = r.Body.position.x;
        for (int i = 0; i < 120 * 30 && next < c.gates && !r.Crashed; i++)
        {
            float sp = Mathf.Max(5f, r.Speed);
            float la = Mathf.Max(2f, sp * look);
            // aim at the line through the gates a look-ahead ahead, but never past the next gate's own centre
            float gz = z0 + 30f + next * c.spacing;
            float az = Mathf.Min(r.Z + la, gz);
            float ax = az >= gz - 0.01f ? GateX(next, c) : PathX(az, c.swing, c.spacing);
            if (az - r.Z < 1f) { az = r.Z + la; ax = PathX(az, c.swing, c.spacing); }
            float want = Mathf.Atan2(ax - r.Body.position.x, az - r.Z);
            r.Steer = Mathf.Clamp(kp * Mathf.DeltaAngle(r.TravelHeading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad - kd * r.YawRate, -1f, 1f);
            StepHeld(r, held ? v : 0f);
            float z = r.Z, x = r.Body.position.x;
            while (next < c.gates && prevZ < gz && z >= gz)
            {
                float xc = Mathf.Lerp(prevX, x, (gz - prevZ) / Mathf.Max(1e-4f, z - prevZ));
                if (Mathf.Abs(xc - GateX(next, c)) <= GateW * 0.5f) made++;
                next++; gz = z0 + 30f + next * c.spacing;
            }
            prevZ = z; prevX = x;
            if (z > zEnd) break;
        }
        endV = r.Speed;
        return made;
    }

    static float Straight(Rider r, float v, float len)
    {
        Place(r, v);
        for (int i = 0; i < 120 * 30 && r.Z < z0 + 30f + len; i++) { r.Steer = Hold(r, PathX(r.Z + 10f, 0f, 1f)); StepHeld(r, 0f); }
        return r.Speed;
    }
}
