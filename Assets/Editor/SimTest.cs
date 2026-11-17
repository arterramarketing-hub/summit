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
            if (r.Airborne) { airT += Dt; if (!wasAir) takeoffs++; maxAir = Mathf.Max(maxAir, r.AboveGround); }
            wasAir = r.Airborne;
            float y = r.Body.position.y;
            if (!r.Airborne) maxStep = Mathf.Max(maxStep, Mathf.Abs((y - lastY) / Dt - r.Body.linearVelocity.y));
            lastY = y;
            if (i % 600 == 0) { var g = Slope.Gradient(r.Body.position.x, r.Body.position.z); Debug.Log($"SIM t={t:0} v={r.Speed*3.6f:0} vel={r.Body.linearVelocity} grad={g} air={r.Airborne} head={r.Heading:0.00}"); }
            float v = r.Speed * 3.6f; sumV += v; n++; maxV = Mathf.Max(maxV, v);
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
        Physics.simulationMode = SimulationMode.FixedUpdate;   // do not leave the project on manual physics
        EditorApplication.Exit(0);
    }
}
