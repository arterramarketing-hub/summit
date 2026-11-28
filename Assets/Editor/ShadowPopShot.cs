using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (treeshadow) Trees' shadows popping on as you ride, measured, on the phone's quality level (its render asset: shadows
/// to 80 m, two cascades). At points down the run through the lower forest, the camera held where a chase camera would
/// be, the forest's centre (what it draws, and which trees cast) has its lists made again a cell on (6.5 m, as riding
/// does, several times a second at speed) and the view drawn again: the snow should not change, but a shadow appearing or going there
/// changes it. In the morning (the sun 8 degrees up: a fir's shadow some 150 m long), by day (48 degrees) and in the
/// evening. Logs SHADOWPOP lines: the share of the snow's pixels a step changed, the worst point's pictures written
/// (a, b and their difference). Exits 1 if any step changed over 0.3% of the snow.
/// SHADOW_SEED=seed, SHADOW_OUT=dir. Batch: -executeMethod ShadowPopShot.Run (no -nographics: it renders).
/// </summary>
public static class ShadowPopShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1280;
    static SummitGame game; static Camera cam; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Weather weather;
    static RenderTexture rt; static Texture2D tex;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        string outDir = System.Environment.GetEnvironmentVariable("SHADOW_OUT") ?? "shadow-shots";
        System.IO.Directory.CreateDirectory(outDir);
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's (Mobile)
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        terrain = game.terrain; props = game.props;
        dayNight = game.dayNight; weather = Get<Weather>("weather");
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("SHADOW_SEED"), out int ss) ? ss : 1000;
        TestBoot.NewMountain(game, seed, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        rt = new RenderTexture(W, H, 24); tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var focus = new GameObject("ShadowFocus").transform;     // what the forest draws round
        var ground = new GameObject("ShadowGround").transform;   // what the ground and the dressing are built round (held)
        props.target = focus; terrain.target = ground; if (dress != null) dress.target = ground;
        var urp = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
        Debug.Log($"SHADOWPOP seed {seed}: quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', shadows to {(urp != null ? urp.shadowDistance : -1f):0} m, {(urp != null ? urp.shadowCascadeCount : 0)} cascades");

        float worstAll = 0f;
        foreach (var (name, clock) in new[] { ("morning", 0.4f), ("day", 1.4f), ("evening", 2.4f) })
        {
            TimeOfDay.TestClock = clock; weather.Step(0f); dayNight.ApplyNow(); Surfaces.Day = default;
            float worst = 0f, sum = 0f; int steps = 0, popped = 0; string worstAt = ""; long shTris = 0; int shDraws = 0;
            for (float z = 1700f; z < Slope.FinishZ - 300f; z += 160f)
            {
                float x = Slope.Centre(z);
                Vector3 p = new Vector3(x, Slope.DrawnHeight(x, z), z);
                // a chase camera's view: 7 m back and 3.5 m up, looking 30 m on down the run
                Vector3 from = new Vector3(Slope.Centre(z - 7f), 0f, z - 7f); from.y = Slope.DrawnHeight(from.x, from.z) + 3.5f;
                Vector3 at = new Vector3(Slope.Centre(z + 30f), 0f, z + 30f); at.y = Slope.DrawnHeight(at.x, at.z);
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                ground.position = p; focus.position = p;
                if (dress != null) dress.Prewarm();
                Surfaces.Step(p, 0f, 0f, true);
                terrain.Prewarm(); props.Prewarm();
                props.Stats(out _, out _, out int sdraws, out long stris, out _);
                shDraws += sdraws; shTris += stris;
                Grab(null); var a = Grab(null);   // (the first frame drawn can come out without the snow's own look)
                // on a cell, as the rider rides
                focus.position = p + Vector3.forward * Forest.Cell;
                props.Prewarm();
                // (and back for the drawing: the trees' own hand-overs fade by the distance from it, which is not what
                // is measured here; only which trees cast changed)
                focus.position = p;
                var b = Grab(null);
                float share = Changed(a, b, out var diff);
                steps++; sum += share; if (share > 0.0005f) popped++;
                if (share > worst)
                {
                    worst = share; worstAt = $"z {z:0}";
                    System.IO.File.WriteAllBytes($"{outDir}/{name}_worst_a.png", Png(a));
                    System.IO.File.WriteAllBytes($"{outDir}/{name}_worst_b.png", Png(b));
                    System.IO.File.WriteAllBytes($"{outDir}/{name}_worst_diff.png", Png(diff));
                }
            }
            Debug.Log($"SHADOWPOP {name}: {steps} steps, {popped} changed the snow (over 0.05%), mean {sum / Mathf.Max(1, steps) * 100f:0.000}%, worst {worst * 100f:0.00}% at {worstAt}; into the shadow map on average {shDraws / Mathf.Max(1, steps)} draws, {shTris / Mathf.Max(1, steps) / 1000} k triangles");
            worstAll = Mathf.Max(worstAll, worst);
        }
        props.Stats(out int draws, out long tris, out int sd, out long st, out int inst);
        Debug.Log($"SHADOWPOP forest at the last point (evening): {draws} draws, {tris / 1000} k triangles; into the shadow map {sd} draws, {st / 1000} k triangles; {inst} instances");
        bool ok = worstAll < 0.003f;
        Debug.Log($"SHADOWPOP {(ok ? "PASS" : "FAIL")}: the most a step changed {worstAll * 100f:0.00}% of the snow");
        TimeOfDay.TestClock = -1f; Weather.TestChoice = -1;
        QualitySettings.SetQualityLevel(quality, true);
        TestPrefs.Restore();
        TestHarness.Exit(ok ? 0 : 1);
    }

    /// <summary>The share of the snow's pixels (pale, unsaturated: lit or in shade) that changed by more than 8 levels.</summary>
    static float Changed(Color32[] a, Color32[] b, out Color32[] d)
    {
        d = new Color32[a.Length];
        int snow = 0, n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int e = Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b)));
            byte v = (byte)Mathf.Min(255, e * 4); d[i] = new Color32(v, v, v, 255);
            if (!Snow(a[i]) && !Snow(b[i])) continue;
            snow++;
            if (e > 8) n++;
        }
        return n / (float)Mathf.Max(1, snow);
    }
    static bool Snow(Color32 c) { int lo = Mathf.Min(c.r, Mathf.Min(c.g, c.b)), hi = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); return lo > 105 && hi - lo < 75; }

    static Color32[] Grab(string file)
    {
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        cam.targetTexture = null; RenderTexture.active = null;
        return tex.GetPixels32();
    }

    static byte[] Png(Color32[] px) { var t = new Texture2D(W, H, TextureFormat.RGB24, false); t.SetPixels32(px); t.Apply(); var b = t.EncodeToPNG(); Object.DestroyImmediate(t); return b; }
}
