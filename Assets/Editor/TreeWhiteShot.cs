using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The snow on the forest's trees through the day: a thick stand beside the run (the cells Forest.At fills most,
/// down the mountain), seen from the piste at 12, 30 and 70 m, in the morning, by day, at the golden and pink hours
/// and at night. TREEWHITE_TAG names the run (before, after). Writes TreeWhite_{tag}_{d}_{time}.png.
/// Run: TREEWHITE_TAG=after Unity -batchmode -projectPath . -executeMethod TreeWhiteShot.Run -logFile tw.log
/// (no -nographics)
/// </summary>
public static class TreeWhiteShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly (string name, float clock)[] Times =
        { ("1morning", 0.4f), ("2day", 1.4f), ("3golden", 2.15f), ("4pink", 2.75f), ("5night", 3.4f) };
    static readonly float[] Dists = { 12f, 30f, 70f };

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();   // (Boot clears the player's points and upgrades: put back on the way out)
        PlayerPrefs.SetInt(Forest.TreesPref, 1);
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = game.rider; var terrain = game.terrain; var props = game.props;
        var dayNight = game.dayNight; var fx = Get<SnowFX>("fx");
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        rider.input = null;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear();
        Slope.Init(424242);

        // the thickest stand off the piste's right edge, below the tree line
        float bestZ = 0f; int best = -1; Vector3 stand = Vector3.zero;
        for (float z = Course.First + 150f; z < Slope.FinishZ - 150f; z += 13f)
        {
            float cx = Slope.Centre(z) + Slope.Half(z) + 4f;
            int n = 0; Vector3 nearest = Vector3.zero; float nd = float.MaxValue;
            for (int i = 0; i < 6; i++)
                for (int j = -2; j <= 2; j++)
                {
                    int gx = Mathf.FloorToInt((cx + i * Forest.Cell) / Forest.Cell), gz = Mathf.FloorToInt((z + j * Forest.Cell) / Forest.Cell);
                    if (Forest.At(gx, gz, out var pos, out _, out _) != Forest.Kind.Tree) continue;
                    n++;
                    float d = Mathf.Abs(pos.x - cx) + Mathf.Abs(pos.z - z);
                    if (d < nd) { nd = d; nearest = pos; }
                }
            if (n > best) { best = n; bestZ = z; stand = nearest; }
        }
        Debug.Log($"TREEWHITE stand z={bestZ} trees={best} at={stand}");

        var draw = typeof(Forest).GetMethod("Draw", NP);
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        string tag = System.Environment.GetEnvironmentVariable("TREEWHITE_TAG") ?? "now";
        rider.gameObject.SetActive(true);
        foreach (float d in Dists)
        {
            // on the piste, across and up the hill from the stand, d metres off
            float ez = stand.z - d * 0.6f, ex = stand.x - d * 0.8f;
            ex = Mathf.Max(ex, Slope.Centre(ez) - Slope.Half(ez) + 2f);
            var eye = new Vector3(ex, Slope.Surface(ex, ez) + 2.2f, ez);
            rider.gameObject.SetActive(true);
            rider.ResetAt(ex, ez);
            rider.transform.position = new Vector3(ex, eye.y - 2f, ez);
            props.target = rider.transform;
            terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
            terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
            rider.gameObject.SetActive(false);   // only the trees in frame
            cam.fieldOfView = d < 20f ? 50f : 34f;
            var aim = stand + Vector3.up * Mathf.Min(6f, d * 0.2f + 2f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (int i = 0; i < 2; i++) { aLate.Invoke(atmo, null); draw.Invoke(props, null); Shoot(cam, null); }
            foreach (var (name, clock) in Times)
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                aLate.Invoke(atmo, null);
                if (fx != null) fx.Clear();
                for (int k = 0; k < 2; k++) { draw.Invoke(props, null); Shoot(cam, k == 1 ? $"TreeWhite_{tag}_{d:000}_{name}.png" : null); }
            }
            Debug.Log($"TREEWHITE d={d} eye={eye}");
        }
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Shoot(Camera cam, string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 800, h = 600;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        cam.Render();
        if (file != null)
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
        }
        cam.targetTexture = null;
        RenderTexture.active = null; rt.Release();
    }
}
