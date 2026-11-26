using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The wind through a mixed stand, as frame strips (TreeWind): from the edge of the piste looking across at the
/// forest (rich and plain card firs, pines, snags, young trees and saplings, and the far pictures behind them), and
/// in the camp (its firs and the lodge's birches). For each view and wind (a windy day's gust, a storm's) a sheet of
/// 12 frames a third of a second apart as a gust arrives, and a motion map: how far each pixel moved from the first
/// frame at any point (black: still; white: moving), which shows at a glance which trees move and which stand still.
/// The weather's look is left clear so the trees can be seen; only the wind's strength is the weather's.
/// Run: Unity -batchmode -projectPath . -executeMethod WindStrip.Run -logFile strip.log (no -nographics).
/// WINDSTRIP_OUT: the folder to write into (default the project's), WINDSTRIP_TAG: a prefix (before/after).
/// </summary>
public static class WindStrip
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 480, H = 360, Frames = 12;

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("WINDSTRIP_OUT") ?? ".";
        string tag = System.Environment.GetEnvironmentVariable("WINDSTRIP_TAG") ?? "now";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        PlayerPrefs.SetInt(Forest.TreesPref, 1);
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props");
        var dayNight = Get<DayNight>("dayNight");
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        rider.input = null;
        var draw = typeof(Forest).GetMethod("Draw", NP);
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();

        // ---- the stand: a third of the way down, from the edge of the piste looking across into the trees
        Slope.Init(424242);
        float z = Slope.FinishZ * 0.3f;
        float ex = Slope.Centre(z) + Slope.PisteHalf - 24f;
        var eye = new Vector3(ex, Slope.Surface(ex, z) + 2.2f, z);
        rider.ResetAt(ex, z); rider.transform.position = eye + Vector3.down * 2f;
        props.target = rider.transform;
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
        rider.gameObject.SetActive(false);
        float tx = ex + 55f, tz = z + 6f;
        var aim = new Vector3(tx, Slope.Surface(tx, tz) + 9f, tz);
        cam.fieldOfView = 55f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) { ps.Clear(); ps.Stop(); }
        for (int i = 0; i < 2; i++) { aLate.Invoke(atmo, null); draw.Invoke(props, null); Shoot(cam); }
        foreach (var (wname, wind, howl) in new[] { ("windy", 1f, 0.45f), ("storm", 1f, 1f) })
            Strip(cam, () => draw.Invoke(props, null), new Vector2(ex + 40f, z), wind, howl, Path.Combine(outDir, $"{tag}_stand_{wname}"));

        // ---- the camp: its firs and the birches round the lodge
        var camp = Object.FindFirstObjectByType<Camp>();
        try
        {
            typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
            if (camp != null) camp.SetNight(0f);
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            var birches = new List<Vector3>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == "Birch") birches.Add(t.position);
            Debug.Log($"WINDSTRIP camp birches {birches.Count}");
            if (birches.Count > 0)
            {
                // the birches on the camp's left, from inside the camp
                Vector3 c = Vector3.zero; int n = 0;
                float cx = Slope.Centre(Slope.PadLip);
                foreach (var b in birches) if (b.x < cx) { c += b; n++; }
                if (n == 0) { foreach (var b in birches) c += b; n = birches.Count; }
                c /= n;
                var from = new Vector3(cx + 4f, 0f, c.z - 6f); from.y = Slope.DrawnHeight(from.x, from.z) + 1.7f;
                cam.fieldOfView = 60f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(c + Vector3.up * 6f - from));
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) { ps.Clear(); ps.Stop(); }
                for (int i = 0; i < 2; i++) { draw.Invoke(props, null); Shoot(cam); }
                foreach (var (wname, wind, howl) in new[] { ("windy", 1f, 0.45f), ("storm", 1f, 1f) })
                    Strip(cam, () => draw.Invoke(props, null), new Vector2(c.x, c.z), wind, howl, Path.Combine(outDir, $"{tag}_camp_{wname}"));
            }
        }
        catch (System.Exception e) { Debug.LogWarning("WINDSTRIP camp view skipped: " + e); }

        TreeWind.Still();
        TimeOfDay.TestClock = -1f;
        TestHarness.Exit(0);
    }

    /// <summary>The weather's wind strength: TreeWind.ForWeather where there is one, else the old sum (Weather.Step's).</summary>
    static float Strength(float wind, float howl)
    {
        var m = typeof(TreeWind).GetMethod("ForWeather", BindingFlags.Public | BindingFlags.Static);
        return m != null ? (float)m.Invoke(null, new object[] { wind, howl }) : 0.6f + 1.6f * wind;
    }

    static void Strip(Camera cam, System.Action draw, Vector2 at, float wind, float howl, string file)
    {
        float k = Strength(wind, howl);
        // the moment a gust reaches `at`: the first time it rises through 0.5 after a calm
        float onset = 30f; bool calm = false;
        for (float c = 20f; c < 900f; c += 0.1f)
        {
            TreeWind.Set(c, k);
            float g = TreeWind.Gust(at.x, at.y);
            if (g < 0.1f) calm = true;
            if (calm && g > 0.5f) { onset = c; break; }
        }
        var sheet = new Texture2D(W * 4, H * 3, TextureFormat.RGB24, false);
        Color[] first = null; var moved = new float[W * H];
        for (int i = 0; i < Frames; i++)
        {
            float c = onset - 2f + i / 3f;
            TreeWind.Set(c, k);
            draw();
            var px = Shoot(cam);
            if (first == null) first = px;
            for (int p = 0; p < px.Length; p++)
            {
                var d = px[p] - first[p];
                moved[p] = Mathf.Max(moved[p], Mathf.Abs(d.r) + Mathf.Abs(d.g) + Mathf.Abs(d.b));
            }
            int col = i % 4, row = 2 - i / 4;
            sheet.SetPixels(col * W, row * H, W, H, px);
            Debug.Log($"WINDSTRIP {Path.GetFileName(file)} frame {i:00} t={c - onset:+0.00;-0.00} strength {k:0.00} gust {TreeWind.Gust(at.x, at.y):0.00}");
        }
        sheet.Apply();
        File.WriteAllBytes(file + "_sheet.png", sheet.EncodeToPNG());
        var map = new Texture2D(W, H, TextureFormat.RGB24, false);
        var mc = new Color[W * H];
        int movingPx = 0;
        for (int p = 0; p < mc.Length; p++)
        {
            float v = Mathf.Clamp01(moved[p] * 2.5f);
            if (moved[p] > 0.08f) movingPx++;
            // still: the first frame dimmed; moving: hot
            var b = first[p] * 0.25f;
            mc[p] = Color.Lerp(b, new Color(1f, 0.85f, 0.2f), v);
        }
        map.SetPixels(mc); map.Apply();
        File.WriteAllBytes(file + "_motion.png", map.EncodeToPNG());
        Debug.Log($"WINDSTRIP {Path.GetFileName(file)} pixels that moved: {100f * movingPx / mc.Length:0.0}%");
        Object.DestroyImmediate(sheet); Object.DestroyImmediate(map);
    }

    static Color[] Shoot(Camera cam)
    {
        DynamicGI.UpdateEnvironment();
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        var px = tex.GetPixels();
        RenderTexture.active = null; cam.targetTexture = null; rt.Release();
        Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
        return px;
    }
}
