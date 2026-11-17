using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>Plays the start of a run exactly as the game does (ToReady, then ToRiding) and logs where the rider is.</summary>
public static class StartTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        Physics.simulationMode = SimulationMode.Script;
        for (int run = 0; run < 3; run++)
        {
            if (run > 0) typeof(SummitGame).GetMethod("ToReady", NP).Invoke(game, null);
            var p0 = rider.transform.position;
            Debug.Log($"START run{run} ready at {p0} ground={Slope.DrawnHeight(p0.x, p0.z):0.00} kinematic={rider.Body.isKinematic} gravity={rider.Body.useGravity} seed={Slope.Seed}");
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
                }
            }
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }
}
