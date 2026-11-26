using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The cable over the gorge (LiftGorge.cs), through the game's own rider and CableGrind: on every gorge line of
/// several mountains, the rider boards the cable on the span before the rim tower at entry speeds from 8 to 40 m/s,
/// grinds to the tower, is thrown on over it and catches the gorge span (or not), and grinds on; reported for each,
/// whether it got across (still on the cable at the far tower), how long that took and how fast it went. Then the
/// rider pops off the gorge span at points along it and falls: reported, how far it fell (from where it left the
/// cable, against the mountain's own line, as Rider.Land judges it) and whether it lived (Rider.DeathFall).
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod LiftGorgeTest.Run -logFile gorge.log
///   SUMMIT_GORGE_SEEDS  "5000,1000,1064,7,1,2"
/// </summary>
public static class LiftGorgeTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props; static SkiLift lift; static MethodInfo liftUpdate;
    static int rides, across, fails, pops, popsDead;
    static string lastCrash = ""; static float lastKilled = -1f;
    static readonly List<float> times = new List<float>(), tops = new List<float>(), falls = new List<float>();
    static double clock = 100.0;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        r = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        lift = Object.FindFirstObjectByType<SkiLift>();
        liftUpdate = typeof(SkiLift).GetMethod("Update", NP);
        r.input = null;
        r.Crash += why => lastCrash = why;
        body.Killed += fell => lastKilled = fell;
        string seeds = System.Environment.GetEnvironmentVariable("SUMMIT_GORGE_SEEDS") ?? "5000,1000,1064,7,1,2";
        try
        {
            foreach (string sd in seeds.Split(','))
            {
                int seed = int.Parse(sd);
                TestBoot.NewMountain(game, seed, terrain, props);
                lift.Clear();
                for (int i = lift.transform.childCount - 1; i >= 0; i--) if (lift.transform.GetChild(i).name.StartsWith("Lift ")) Object.DestroyImmediate(lift.transform.GetChild(i).gameObject);
                Debug.Log($"GORGE seed {seed}\n" + LiftLine.GorgeReport());
                foreach (var l in LiftLine.Lifts)
                {
                    if (l.gorge < 1) continue;
                    foreach (float v0 in new[] { 8f, 14f, 20f, 26f, 32f, 40f }) Across($"seed{seed} lift{l.k} v{v0:0}", l, v0);
                    foreach (float u in new[] { 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f }) Pop($"seed{seed} lift{l.k} pop at {u * 100f:0}%", l, u);
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("GORGE exception " + e); fails++; }
        times.Sort(); tops.Sort(); falls.Sort();
        System.Func<List<float>, string> R = v => v.Count == 0 ? "-" : $"{v[0]:0.0} / {v[v.Count / 2]:0.0} / {v[v.Count - 1]:0.0}";
        Debug.Log($"GORGE done: {across} of {rides} grinds got across the gorge, {fails} failure(s); across in {R(times)} s, top speed {R(tops)} m/s; pops off the gorge span: {popsDead} of {pops} killed, falls {R(falls)} m");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails > 0 ? 1 : 0);
    }

    static void Step(int n)
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        clock += Dt;
        if (n % 12 == 0) { terrain.Prewarm(); props.Prewarm(); SkiLift.TestClock = clock; liftUpdate.Invoke(lift, null); Physics.SyncTransforms(); }
        if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
    }

    static float Along(LiftLine.Lift l, Vector3 p) => (p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z;

    /// <summary>Put the rider in the air just over a cable at `s` along span i, going down it at v0, as a jump onto it would.</summary>
    static void OnCable(LiftLine.Lift l, int i, float s, float v0)
    {
        Vector3 on = l.At(s, LiftLine.Half, l.CableY(i, s));
        float slope = LiftLine.CableSlope(l, i, s);
        Vector3 dir = (l.dir + Vector3.up * slope).normalized;
        CableGrind.Reset();
        r.ResetAt(on.x, on.z); body.ResetVisual(); r.Begin();
        r.Body.position = on + Vector3.up * (Rider.R + 0.4f);
        r.transform.position = r.Body.position;
        r.Airborne = true;
        r.Heading = Mathf.Atan2(l.dir.x, l.dir.z);
        r.Body.linearVelocity = dir * v0 - Vector3.up * 1f;
        lastCrash = ""; lastKilled = -1f;
        terrain.Prewarm(); props.Prewarm();
        SkiLift.TestClock = clock; liftUpdate.Invoke(lift, null); Physics.SyncTransforms();
    }

    static void Across(string name, LiftLine.Lift l, float v0)
    {
        rides++;
        int g = l.gorge;
        float sa = l.nodeS[g], sb = l.nodeS[g + 1];
        // on the span before the rim tower, 30 m from it (or as far from its start as there is room)
        float s0 = Mathf.Max(l.nodeS[g - 1] + CableTune.ClearTower + 1f, sa - 30f);
        OnCable(l, g - 1, s0, v0);
        float t = 0f, onGorge = 0f, top = 0f, reached = s0; bool boarded = false, made = false;
        for (int n = 0; n < 120 * 40; n++, t += Dt)
        {
            Step(n);
            if (r.Crashed) break;
            Vector3 p = r.Body.position;
            float s = Along(l, p);
            if (r.OnCable)
            {
                boarded = true;
                if (s > sa + 2f && s < sb) { if (onGorge == 0f) onGorge = t; reached = Mathf.Max(reached, s); top = Mathf.Max(top, r.Speed); }
            }
            // got across: still on the cable, or just thrown over the far tower by the grind itself
            if (reached > sb - 18f && (r.OnCable || r.Airborne)) { made = true; break; }
            if (!r.OnCable && !r.Airborne && boarded) break;   // back on the snow
            if (s > sb + 40f) break;
        }
        if (made) { across++; times.Add(t - onGorge); tops.Add(top); }
        else { fails++; Debug.Log($"GORGE-FAIL {name}: got {reached - sa:0} of {sb - sa:0} m across the gorge ({(r.Crashed ? "crashed '" + lastCrash + "'" : boarded ? "came off" : "never boarded")})"); }
        Debug.Log($"GORGE {name}: {(made ? $"across the {sb - sa:0} m gorge span in {t - onGorge:0.0} s, {top:0} m/s at most" : $"not across ({reached - sa:0} m of {sb - sa:0})")}");
    }

    static void Pop(string name, LiftLine.Lift l, float u)
    {
        pops++;
        int g = l.gorge;
        float s = Mathf.Lerp(l.nodeS[g], l.nodeS[g + 1], u);
        OnCable(l, g, s, 24f);
        Vector3 from = Vector3.zero; bool off = false; float t = 0f;
        for (int n = 0; n < 120 * 30; n++, t += Dt)
        {
            if (!off && r.OnCable && t > 0.4f) { r.QueuePop(0.5f); }
            Step(n);
            if (!off && r.Airborne && t > 0.4f) { off = true; from = r.Body.position; }
            if (off && (r.Crashed || (!r.Airborne && !r.Grinding))) break;
        }
        // the body, thrown or not, comes to rest; a death is told by RiderBody.Killed
        for (int n = 0; n < 120 * 8 && r.Crashed && lastKilled < 0f; n++) Step(n);
        Vector3 at = r.Crashed ? body.RagdollCentre : r.Body.position;
        float fell = from.y - at.y - Slope.Fall * Mathf.Max(0f, at.z - from.z);
        bool dead = lastKilled >= 0f;
        if (dead) popsDead++;
        falls.Add(fell);
        Debug.Log($"GORGE {name}: off the cable {from.y - Slope.Height(from.x, from.z):0} m over the ground; fell {fell:0} m; {(dead ? $"killed ({lastKilled:0} m)" : r.Crashed ? "crashed '" + lastCrash + "', lived" : "landed and rode on")}");
        // the rule: a fall of DeathFall or more is death, and less is not
        if (dead && lastKilled < Rider.DeathFall - 0.5f) { fails++; Debug.Log($"GORGE-FAIL {name}: killed by a fall of only {lastKilled:0} m"); }
        if (!dead && fell >= Rider.DeathFall + 15f) { fails++; Debug.Log($"GORGE-FAIL {name}: lived through a fall of {fell:0} m"); }
    }
}
