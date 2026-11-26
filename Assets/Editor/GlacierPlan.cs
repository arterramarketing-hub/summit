using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The upper mountain from straight above, on three seeds: where the crevasses lie, as a map. Uses nothing but
/// Course.CrevasseCut, so the same tool shows any crevasse layout (before and after a change). Each shot is 300 m of
/// run, downhill at the top of the frame. Also logs how much of each 100 m of run is open crevasse.
/// Run: Unity -batchmode -projectPath . -executeMethod GlacierPlan.Run -logFile x.log (no -nographics).
/// $PLAN_TAG names the shots (GlacierPlan_{tag}_s{seed}_{n}.png, in the project folder).
/// </summary>
public static class GlacierPlan
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly int[] Seeds = { 2000, 2131, 2262 };
    const float Span = 300f;
    static float Z0 = 60f;     // $PLAN_Z0: where the first shot starts
    static int Shots = 5;      // $PLAN_SHOTS: how many (60 .. 1560 m down the run)

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>();
        string tag = System.Environment.GetEnvironmentVariable("PLAN_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "now";
        if (float.TryParse(System.Environment.GetEnvironmentVariable("PLAN_Z0"), out float z0)) Z0 = z0;
        if (int.TryParse(System.Environment.GetEnvironmentVariable("PLAN_SHOTS"), out int ns)) Shots = ns;
        var only = System.Environment.GetEnvironmentVariable("PLAN_SEED");
        try
        {
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            foreach (int seed in Seeds)
            {
                if (!string.IsNullOrEmpty(only) && only != seed.ToString()) continue;
                TestBoot.NewMountain(game, seed, terrain, props);
                var dress = Object.FindFirstObjectByType<CourseDressing>();
                rider.ResetAt(Slope.Centre(Z0), Z0);
                if (dress != null) dress.Rebuild();
                // how much of the run is open crevasse, 100 m at a time (1 m grid, 60 m either side of the middle)
                var sb = new System.Text.StringBuilder();
                for (float z = 0f; z < 2000f; z += 100f)
                {
                    int open = 0;
                    for (float zz = z; zz < z + 100f; zz += 1f)
                    {
                        float c = Slope.Centre(zz);
                        for (float x = -60f; x <= 60f; x += 1f) if (Course.CrevasseCut(c + x, zz) > 0.3f) open++;
                    }
                    if (open > 0) sb.Append($" {z:0}-{z + 100f:0}:{open}m2");
                }
                for (int k = 0; k < 2; k++) if (Cliffs.JumpOf(0, k, out var j)) Debug.Log($"PLAN seed{seed} jump cliff {k}: bench from z={j.Start:0}, lip {j.z:0}, landing to {j.End:0}");
                for (float zk = 60f; Slope.NextKicker(zk, out var kat) && kat.z < 1400f; zk = kat.z + 5f) Debug.Log($"PLAN seed{seed} kicker at z={kat.z:0} x-c={kat.x - Slope.Centre(kat.z):0}");
                foreach (var l in LiftLine.Lifts) if (l.top.z < 1400f) Debug.Log($"PLAN seed{seed} lift {l.id} (cycle {l.k}{(l.second ? ", across" : "")}): top {l.top.z:0} x-c={l.offTop:0}, bottom {l.bottom.z:0} x-c={l.offBottom:0}");
                {
                    var all = Course.Crevasses; var kinds = new Dictionary<Course.CrevasseKind, int>(); var fieldIds = new HashSet<int>();
                    foreach (var c in all) { kinds.TryGetValue(c.kind, out int n); kinds[c.kind] = n + 1; if (c.field >= 0) fieldIds.Add(c.field); }
                    var ks = new System.Text.StringBuilder(); foreach (var kv in kinds) ks.Append($"{kv.Value} {kv.Key} ");
                    var rj = new System.Text.StringBuilder(); foreach (var kv in Glacier.Rejected) rj.Append($"{kv.Key}={kv.Value} ");
                    Debug.Log($"PLAN seed{seed} glacier: {all.Length} cracks ({ks}) in {fieldIds.Count} fields; turned down: {rj}; steepening spots (z:k):{Glacier.Spots}");
                }
                Debug.Log($"PLAN seed{seed} free for crevasses down the middle (and what ends each stretch):{Glacier.FreeStretches(0f, 2200f)}");
                Debug.Log($"PLAN seed{seed}: open crevasse by stretch of run:{sb} (alt at z=0 {Slope.AltitudeFeet(0f):0} ft, at z=1200 {Slope.AltitudeFeet(1200f):0} ft)");
                for (int n = 0; n < Shots; n++)
                {
                    float zm = Z0 + Span * (n + 0.5f), cx = Slope.Centre(zm);
                    rider.ResetAt(cx, zm); body.ResetVisual(); rider.Stop();
                    terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
                    typeof(SlopeStreamer).GetMethod("Update", NP)?.Invoke(terrain, null);
                    typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
                    typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
                    float top = Slope.Height(cx, zm - Span * 0.5f) + 120f;
                    chase.enabled = false;
                    cam.orthographic = true; cam.orthographicSize = Span * 0.5f;
                    cam.nearClipPlane = 1f; cam.farClipPlane = 1500f;
                    cam.transform.SetPositionAndRotation(new Vector3(cx, top, zm), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    bool fog = RenderSettings.fog; RenderSettings.fog = false;
                    Shoot(cam, atmo, $"GlacierPlan_{tag}_s{seed}_{n}.png");
                    RenderSettings.fog = fog;
                    cam.orthographic = false;
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("PLAN exception " + e); }
        Debug.Log("PLAN done");
        TestHarness.Exit(0);
    }

    static void Shoot(Camera cam, Atmosphere atmo, string file)
    {
        DynamicGI.UpdateEnvironment();
        if (atmo != null) typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
    }
}
