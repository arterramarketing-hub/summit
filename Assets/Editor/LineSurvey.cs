using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// How fast a rider goes: bots ride whole runs down the piste and the speed is binned by the pitch of the fall line
/// under them (over 30 m) and by section, with every air's take-off speed, hang time and how far down the hill it
/// carried. For the feature lines' spacing (Lines.cs, features.md).
///   cruise: a weave across the middle of the piste (a rider riding it, not racing it)
///   race:   the straight line down the middle (hands nearly off)
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod LineSurvey.Run -logFile survey.log
/// </summary>
public static class LineSurvey
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;

    static readonly float[] bandEdge = { 0.45f, 0.55f, 0.65f, 0.75f, 0.85f, 0.95f, 9f };

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        r.input = null;
        try
        {
            foreach (string style in new[] { "cruise", "race" })
            {
                var sum = new float[bandEdge.Length]; var cnt = new int[bandEdge.Length]; var mx = new float[bandEdge.Length];
                var secSum = new Dictionary<Course.Section, float>(); var secCnt = new Dictionary<Course.Section, int>();
                var all = new List<float>();
                foreach (int seed in new[] { 1000, 1077, 1154 })
                {
                    TestBoot.NewMountain(game, seed, terrain, props);
                    float z0 = 60f;
                    r.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); r.Begin();
                    r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z0), z0)).normalized * 15f;
                    terrain.Prewarm(); props.Prewarm();
                    int n = 0, crashes = 0; float t = 0f;
                    float airFrom = -1f, airV = 0f; Vector3 airAt = Vector3.zero;
                    for (; t < 260f && r.Z < Slope.FinishZ - 30f; t += Dt, n++)
                    {
                        float wobble = style == "cruise" ? 9f * Mathf.Sin(r.Z * 0.045f) : 0f;
                        r.Steer = CarveBot.Onto(r, z => Slope.Centre(z) + wobble, 1.2f, 8f, 10f);
                        bool wasAir = r.Airborne;
                        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                        if (n % 4 == 0) body.Apply(4 * Dt);
                        if (r.Crashed)
                        {
                            crashes++;
                            Vector3 c = r.transform.position;
                            float zc = c.z + 20f;
                            r.ResetAt(Slope.Centre(zc), zc); body.ResetVisual(); r.Begin();
                            r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(zc), zc)).normalized * 15f;
                            terrain.Prewarm(); props.Prewarm(); airFrom = -1f;
                            continue;
                        }
                        if (!wasAir && r.Airborne) { airFrom = t; airV = r.Speed; airAt = r.transform.position; }
                        if (wasAir && !r.Airborne && airFrom >= 0f)
                        {
                            float at = t - airFrom; Vector3 q = r.transform.position;
                            if (at > 0.45f)
                            {
                                string what = Mathf.Abs(Slope.Kicker(airAt.x, airAt.z - 1f)) > 0.01f ? "kicker" : Cliffs.JumpNear(airAt.z, 15f, out _) ? "jumpcliff" : Cliffs.LedgeNear(airAt.z, 15f, out _) ? "ledge" : "roll";
                                Debug.Log($"SURVEY-AIR {style} seed{seed} {what} z{airAt.z:0} v{airV:0.0} air{at:0.00}s carried {q.z - airAt.z:0.0} m down, lands {r.LastLandImpact:0.0} m/s, then {r.Speed:0.0}");
                            }
                            airFrom = -1f;
                        }
                        if (n % 12 != 0 || r.Airborne) continue;
                        float zz = r.Z;
                        float pitch = (Slope.FallLine(zz - 15f) - Slope.FallLine(zz + 15f)) / 30f;
                        int b = 0; while (pitch > bandEdge[b]) b++;
                        float sp = r.Speed;
                        sum[b] += sp; cnt[b]++; mx[b] = Mathf.Max(mx[b], sp); all.Add(sp);
                        var sec = Course.At(zz);
                        secSum.TryGetValue(sec, out float ss); secSum[sec] = ss + sp;
                        secCnt.TryGetValue(sec, out int sc); secCnt[sec] = sc + 1;
                    }
                    Debug.Log($"SURVEY-RUN {style} seed{seed}: {t:0} s to z{r.Z:0}, {crashes} crash(es)");
                }
                all.Sort();
                for (int b = 0; b < bandEdge.Length; b++)
                {
                    if (cnt[b] == 0) continue;
                    string lo = b == 0 ? "0" : bandEdge[b - 1].ToString("0.00"), hi = bandEdge[b] > 5f ? "+" : bandEdge[b].ToString("0.00");
                    Debug.Log($"SURVEY-BAND {style} pitch {lo}-{hi} ({Mathf.Atan(b == 0 ? 0.4f : bandEdge[b - 1]) * Mathf.Rad2Deg:0}deg+): mean {sum[b] / cnt[b]:0.0} m/s ({sum[b] / cnt[b] * 3.6f:0} km/h), max {mx[b]:0.0}, n={cnt[b]}");
                }
                foreach (var kv in secSum) Debug.Log($"SURVEY-SEC {style} {kv.Key}: mean {kv.Value / secCnt[kv.Key]:0.0} m/s, n={secCnt[kv.Key]}");
                if (all.Count > 0) Debug.Log($"SURVEY-ALL {style}: p10 {all[all.Count / 10]:0.0} p25 {all[all.Count / 4]:0.0} median {all[all.Count / 2]:0.0} p75 {all[all.Count * 3 / 4]:0.0} p90 {all[all.Count * 9 / 10]:0.0} m/s");
            }
        }
        catch (System.Exception e) { Debug.LogError("SURVEY exception " + e); }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }
}
