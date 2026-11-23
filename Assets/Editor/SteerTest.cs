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
                    Debug.Log($"STEER side scale={sc:0.0} v={v:0} m/s ({v * 3.6f:0} km/h) drag {throw_ * 100f:0}% (steer {s:0.00}): sideways 3 m/s in {T(l3)}, 90% of its sideways speed ({lss:0.0} m/s at 1 s) in {T(l90)} | reverse left->right from -{RevSpeed:0} m/s: through zero in {T(r0)}, +{RevSpeed:0} m/s in {T(r3)}");
                }
                foreach (var c in Courses)
                {
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

        Scale = scale0;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    // ---- a 400 m stretch where a straight run stays on the snow (no kickers, no drops)
    static void FindStretch(Rider r)
    {
        foreach (float z in new[] { 400f, 600f, 800f, 1000f, 1200f, 300f, 1500f })
            foreach (float off in new[] { 12f, -12f, 0f, 16f, -16f })
            {
                x0 = Slope.Centre(z) + off; z0 = z;
                bool air = false;
                Place(r, 22f);
                for (int i = 0; i < 120 * 18 && r.Z < z0 + 400f; i++)
                {
                    r.Steer = Hold(r, PathX(r.Z + 10f, 0f, 1f));
                    StepHeld(r, 22f);
                    if (r.Airborne || r.Crashed) { air = true; break; }
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
    static void Sideways(Rider r, float v, float s, out float t3, out float t90, out float steady)
    {
        Settle(r, v);
        var lat = new float[120]; float steer = 0f; t3 = -1f;
        for (int i = 0; i < lat.Length; i++)
        {
            steer = SteerTo(steer, s); r.Steer = steer; StepHeld(r, v);
            lat[i] = Lat(r);
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
