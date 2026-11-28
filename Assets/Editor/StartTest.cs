using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Plays the start of a run exactly as the game does (ToReady, then ToRiding) three times and logs where the rider
/// is. Fails (exit 1, START FAIL) if the rider is not held still on the snow while ready, goes under the snow, stops
/// short of 50 m down the hill in 14 s hands off, or has crashed by then.
/// </summary>
public static class StartTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Run()
    {
        var game = TestHarness.Boot();
        var rider = game.rider;
        var body = game.body;
        Physics.simulationMode = SimulationMode.Script;
        int fails = 0;
        void Fail(string what) { fails++; Debug.Log("START-FAIL " + what); }
        for (int run = 0; run < 3; run++)
        {
            if (run > 0) typeof(SummitGame).GetMethod("ToReady", NP, null, System.Type.EmptyTypes, null).Invoke(game, null);
            var p0 = rider.transform.position;
            Debug.Log($"START run{run} ready at {p0} ground={Slope.DrawnHeight(p0.x, p0.z):0.00} kinematic={rider.Body.isKinematic} gravity={rider.Body.useGravity} seed={Slope.Seed}");
            if (!rider.Body.isKinematic || rider.Body.useGravity) Fail($"run{run}: not held still while ready (kinematic={rider.Body.isKinematic} gravity={rider.Body.useGravity})");
            if (p0.y < Slope.DrawnHeight(p0.x, p0.z) - 0.1f) Fail($"run{run}: ready under the snow");
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            for (int i = 0; i < 120 * 14; i++)
            {
                rider.Tick(1f / 120f); body.PhysicsStep(); Physics.Simulate(1f / 120f);
                if (i % 30 == 0 && i > 120 * 7)
                {
                    var p = rider.Body.position;
                    float g = Slope.DrawnHeight(p.x, p.z);
                    Debug.Log($"START run{run} t={i / 120f:0.00} pos={p} ground={g:0.00} above={p.y - g:0.00} v={rider.Body.linearVelocity} air={rider.Airborne} crashed={rider.Crashed} running={rider.Running} kin={rider.Body.isKinematic}");
                    if (float.IsNaN(p.x) || p.y - g < -0.5f) { Fail($"run{run} t={i / 120f:0.00}: under the snow ({p.y - g:0.00})"); break; }
                }
            }
            var pe = rider.Body.position;
            if (pe.z - p0.z < 50f) Fail($"run{run}: only {pe.z - p0.z:0} m down the hill in 14 s");
            if (rider.Crashed) Fail($"run{run}: crashed hands off at the start");
        }
        Debug.Log(fails == 0 ? "START PASS" : $"START FAIL ({fails} failure(s))");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0);
    }
}
