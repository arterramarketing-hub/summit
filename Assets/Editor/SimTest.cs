using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// Rides the mountain headless for a minute with a simple bot and prints how it went,
/// then measures how fast the board answers the stick.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod SimTest.Run -logFile sim.log
/// </summary>
public static class SimTest
{
    const float Dt = 1f / 120f;

    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        Physics.simulationMode = SimulationMode.Script;
        TestPrefs.Pin();   // (the forest's look is a saved setting)
        Slope.Init(1234);
        var rg = new GameObject("Rider");
        var r = rg.AddComponent<Rider>();
        var forest = new GameObject("Forest").AddComponent<Forest>();
        forest.target = rg.transform;

        int jumps = 0, crashes = 0, scrapes = 0, rough = 0, calls = 0;
        float maxAir = 0f;
        r.Jumped += () => jumps++;
        r.Crash += _ => crashes++;
        r.Scraped += _ => scrapes++;
        r.RoughLanding += _ => rough++;
        r.Landed += imp => { if (r.Peak > 3f) Debug.Log($"SIMAIR peak={r.Peak:0.0} air={r.AirTime:0.00} z={r.Z:0} sec={Course.At(r.Z)} x-c={r.Body.position.x - Slope.Centre(r.Z):0.0} imp={imp:0}"); };
        r.Trick += (n, p) => Debug.Log($"SIMTRICK {n} {p} z={r.Z:0} sec={Course.At(r.Z)}");
        r.Crash += w => Debug.Log($"SIMCRASH {w} z={r.Z:0} x-c={r.Body.position.x - Slope.Centre(r.Z):0.0} sec={Course.At(r.Z)}");
        r.Trick += (n, p) => { if (n == "Close call") calls++; };
        int takeoffs = 0; bool wasAir = false;

        r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ);
        forest.Prewarm();
        r.Begin();
        float t = 0f, airT = 0f, sumV = 0f, maxV = 0f; int n = 0;
        float maxStep = 0f; float lastY = r.Body.position.y;
        for (int i = 0; i < 120 * 60; i++, t += Dt)
        {
            // bot: carve S-turns around the piste centre
            float aimX = Slope.Centre(r.Z + 12f);
            if (Slope.NextKicker(r.Z, out Vector3 kk) && kk.z - r.Z < 70f) aimX = kk.x;
            float off = aimX - r.Body.position.x;
            r.Steer = Mathf.Clamp(0.4f * Mathf.Sin(t * 1.1f) + off * 0.06f - (r.Heading) * 0.8f, -1f, 1f);
            r.Tick(Dt);
            Physics.Simulate(Dt);
            if (i % 12 == 0) forest.Prewarm();
            if (r.Airborne) { airT += Dt; if (!wasAir) { takeoffs++; var bp = r.Body.position; Debug.Log($"SIMTAKEOFF z={bp.z:0} x-c={bp.x - Slope.Centre(bp.z):0.0} vel={r.Body.linearVelocity} ny={Slope.Normal(bp.x, bp.z).y:0.00} pipe={Course.InPipe(bp.x, bp.z)} head={r.Heading:0.00}"); } maxAir = Mathf.Max(maxAir, r.AboveGround); }
            if (r.Airborne && r.AboveGround > 20f && i % 12 == 0) { var bp = r.Body.position; Debug.Log($"SIMHIGH z={bp.z:0} x-c={bp.x - Slope.Centre(bp.z):0.0} y={bp.y:0.0} ground={Slope.Height(bp.x, bp.z):0.0} vel={r.Body.linearVelocity}"); }
            wasAir = r.Airborne;
            float y = r.Body.position.y;
            if (!r.Airborne) maxStep = Mathf.Max(maxStep, Mathf.Abs((y - lastY) / Dt - r.Body.linearVelocity.y));
            lastY = y;
            if (i % 600 == 0) { var g = Slope.Gradient(r.Body.position.x, r.Body.position.z); Debug.Log($"SIM t={t:0} v={r.Speed*3.6f:0} vel={r.Body.linearVelocity} grad={g} air={r.Airborne} head={r.Heading:0.00}"); }
            float v = r.Speed * 3.6f; sumV += v; n++; if (v > maxV + 10f && v > 140f) Debug.Log($"SIMFAST v={v:0} z={r.Z:0} air={r.Airborne} crashed={r.Crashed} grind={r.Grinding} vel={r.Body.linearVelocity}"); maxV = Mathf.Max(maxV, v);
        }
        Debug.Log($"SIM ride: dist={r.Distance:0} m  avg={sumV / n:0} km/h  max={maxV:0} km/h  airborne={100f * airT / 60f:0.0}%  takeoffs={takeoffs}  maxHeight={maxAir:0.0} m  crashes={crashes}  scrapes={scrapes}  rough={rough}  closeCalls={calls}");

        // response: get up to speed straight, then full stick right
        r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ);
        r.Begin();
        for (int i = 0; i < 120 * 8; i++) { r.Steer = Mathf.Clamp((Slope.Centre(r.Z + 12f) - r.Body.position.x) * 0.06f - r.Heading, -1f, 1f); r.Tick(Dt); Physics.Simulate(Dt); }
        float th0 = r.TravelHeading, sp0 = r.Speed * 3.6f, tt = 0f, maxSlip = 0f;
        while (tt < 2f && Mathf.Abs(Mathf.DeltaAngle(th0 * Mathf.Rad2Deg, r.TravelHeading * Mathf.Rad2Deg)) < 20f)
        {
            r.Steer = 1f; r.Tick(Dt); Physics.Simulate(Dt); tt += Dt;
            maxSlip = Mathf.Max(maxSlip, Mathf.Abs(Mathf.DeltaAngle(r.Heading * Mathf.Rad2Deg, r.TravelHeading * Mathf.Rad2Deg)));
        }
        Debug.Log($"SIM steer: at {sp0:0} km/h full stick turns travel 20 deg in {tt:0.00} s; board-vs-travel slip peaked at {maxSlip:0.0} deg");
        // turn rate and radius after 0.6 s of holding a given steer, from ~same speed
        foreach (float st in new[] { 0.07f, 0.2f, 0.5f, 1f })
        {
            r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ); r.Begin();
            for (int i = 0; i < 120 * 8; i++) { r.Steer = Mathf.Clamp((Slope.Centre(r.Z + 12f) - r.Body.position.x) * 0.06f - r.Heading, -1f, 1f); r.Tick(Dt); Physics.Simulate(Dt); }
            float h0 = r.Heading, v0 = r.Speed;
            for (int i = 0; i < 72; i++) { r.Steer = st; r.Tick(Dt); Physics.Simulate(Dt); }
            Debug.Log($"SIM steer {st:0.00} (thumb {(st >= 1f ? 1f : Mathf.Pow(st / 0.8f, 1f / 3f)):0.00} approx) at {v0 * 3.6f:0} km/h: yaw {Mathf.Abs(r.YawRate) * Mathf.Rad2Deg:0} deg/s, radius {r.Speed / Mathf.Max(1e-3f, Mathf.Abs(r.YawRate)):0} m, heading moved {Mathf.Abs(r.Heading - h0) * Mathf.Rad2Deg:0} deg in 0.6 s");
        }

        // step response: from riding straight at ~60 km/h, the thumb goes to a fixed throw and stays
        foreach (float thumb in new[] { 0.3f, 0.6f, 1f })
        {
            r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ); r.Begin();
            for (int i = 0; i < 120 * 8; i++) { r.Steer = Mathf.Clamp((Slope.Centre(r.Z + 12f) - r.Body.position.x) * 0.06f - r.Heading, -1f, 1f); r.Tick(Dt); Physics.Simulate(Dt); }
            float v0 = r.Speed, tr0 = r.TravelHeading, steer = 0f, target = RiderInput.Curve(thumb);
            float t50 = -1f, t90 = -1f, slip = 0f, travel25 = 0f, travel50 = 0f, yawSS = 0f;
            var yaws = new float[120];
            for (int i = 0; i < 120; i++)
            {
                steer = Mathf.Lerp(steer, target, 1f - Mathf.Exp(-Dt * 60f));
                r.Steer = steer; r.Tick(Dt); Physics.Simulate(Dt);
                yaws[i] = Mathf.Abs(r.YawRate);
                if (false && thumb >= 1f && i % 12 == 0) Debug.Log($"SIM trace t={i * Dt:0.00} yaw={r.YawRate * Mathf.Rad2Deg:0} lean={r.Lean * Mathf.Rad2Deg:0} v={r.Speed * 3.6f:0} head={r.Heading * Mathf.Rad2Deg:0} travel={r.TravelHeading * Mathf.Rad2Deg:0} skid={r.Skid:0.00} air={r.Airborne}");
                slip = Mathf.Max(slip, Mathf.Abs(Mathf.DeltaAngle(r.Heading * Mathf.Rad2Deg, r.TravelHeading * Mathf.Rad2Deg)));
                if (i == 29) travel25 = Mathf.Abs(Mathf.DeltaAngle(tr0 * Mathf.Rad2Deg, r.TravelHeading * Mathf.Rad2Deg));
                if (i == 59) travel50 = Mathf.Abs(Mathf.DeltaAngle(tr0 * Mathf.Rad2Deg, r.TravelHeading * Mathf.Rad2Deg));
            }
            yawSS = yaws[119];
            for (int i = 0; i < 120; i++) { if (t50 < 0f && yaws[i] >= 0.5f * yawSS) t50 = i * Dt; if (t90 < 0f && yaws[i] >= 0.9f * yawSS) t90 = i * Dt; }
            Debug.Log($"SIM step thumb={thumb:0.0} edge={target:0.00} at {v0 * 3.6f:0} km/h: yaw {yawSS * Mathf.Rad2Deg:0} deg/s (radius {r.Speed / Mathf.Max(1e-3f, yawSS):0} m), 50% in {t50:0.00}s, 90% in {t90:0.00}s, travel turned {travel25:0} deg by 0.25s, {travel50:0} deg by 0.5s, board-travel slip peak {slip:0.0} deg");
        }

        // let go: after a full-lock turn the thumb is released; how fast does the board come back down the hill?
        {
            r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ); r.Begin();
            for (int i = 0; i < 120 * 8; i++) { r.Steer = Mathf.Clamp((Slope.Centre(r.Z + 12f) - r.Body.position.x) * 0.06f - r.Heading, -1f, 1f); r.Tick(Dt); Physics.Simulate(Dt); }
            for (int i = 0; i < 60; i++) { r.Steer = 1f; r.Tick(Dt); Physics.Simulate(Dt); }
            float h0 = r.Heading * Mathf.Rad2Deg; string trace = "";
            for (int i = 0; i <= 180; i++) { r.Steer = 0f; r.Tick(Dt); Physics.Simulate(Dt); if (i % 30 == 0) trace += $" {i * Dt:0.0}s:{r.Heading * Mathf.Rad2Deg:0}";  }
            Debug.Log($"SIM release: heading after the turn {h0:0} deg, then with the thumb off:{trace}");
        }

        // straight line vs constant carving, 25 s each from the start
        foreach (float amp in new[] { 0f, 0.8f })
        {
            r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ); r.Begin();
            float sum = 0f, mx = 0f; int cnt = 0;
            for (int i = 0; i < 120 * 25; i++)
            {
                float tt2 = i * Dt;
                r.Steer = Mathf.Clamp(amp * Mathf.Sin(tt2 * 1.6f) + (Slope.Centre(r.Z + 12f) - r.Body.position.x) * 0.05f - r.Heading * 0.6f, -1f, 1f);
                r.Tick(Dt); Physics.Simulate(Dt);
                if (i > 120 * 8) { sum += r.Speed * 3.6f; cnt++; mx = Mathf.Max(mx, r.Speed * 3.6f); }
            }
            Debug.Log($"SIM style={(amp == 0f ? "straight" : "carving")} avg={sum / cnt:0} km/h max={mx:0} km/h");
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;   // do not leave the project on manual physics
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }
}
