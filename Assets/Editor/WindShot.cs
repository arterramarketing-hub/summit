using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// A gust passing through a stand of trees (TreeWind): from the edge of the piste, looking across at the forest,
/// a frame every half second from the calm before a gust reaches the stand until it has gone through, by day and
/// by night. Run: WINDSHOT=Real Unity -batchmode -projectPath . -executeMethod WindShot.Run -logFile wind.log
/// (no -nographics). WINDSHOT: Real or Classic (the tree look). Writes WindShot_{look}_{day|night}_{nn}.png and
/// WIND lines (the gust at the stand in each frame). One look a run (instanced draws queued in batchmode are never
/// flushed; the wind itself is only shader globals, so every frame's draws are the same and can pile up).
/// </summary>
public static class WindShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        string look = System.Environment.GetEnvironmentVariable("WINDSHOT") ?? "Real";
        TestPrefs.Pin();   // (before the tree look is set: the boot pins only once)
        PlayerPrefs.SetInt(Forest.TreesPref, look == "Classic" ? 0 : 1);
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = game.rider; var terrain = game.terrain; var props = game.props;
        var dayNight = game.dayNight; var fx = Get<SnowFX>("fx");
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        rider.input = null;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear();
        Slope.Init(424242);

        // at the edge of the piste a third of the way down, looking across into the trees (the gust comes down
        // the hill, so it crosses the frame)
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

        // find a gust at the stand: its peak, and start from the calm before it
        Vector2 stand = new Vector2(ex + 30f, z);
        float peak = 0f, best = -1f;
        for (float c = 20f; c < 400f; c += 0.25f)
        {
            TreeWind.Set(c);
            float g = TreeWind.Gust(stand.x, stand.y);
            if (g > best + 1e-3f) { best = g; peak = c; }
            if (best > 0.95f && g < 0.2f) break;
        }
        Debug.Log($"WIND {look} stand={stand} peak at {peak:0.0} s gust {best:0.00}");

        var draw = typeof(Forest).GetMethod("Draw", NP);
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        for (int i = 0; i < 2; i++) { aLate.Invoke(atmo, null); draw.Invoke(props, null); Shoot(cam, null); }
        foreach (var (phase, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
        {
            TimeOfDay.TestClock = clock; dayNight.ApplyNow();
            aLate.Invoke(atmo, null);
            if (fx != null) fx.Clear();
            int n = 0;
            for (float c = peak - 7f; c <= peak + 4f + 1e-3f; c += 0.5f, n++)
            {
                TreeWind.Set(c);
                draw.Invoke(props, null);
                Shoot(cam, $"WindShot_{look}_{phase}_{n:00}.png");
                Debug.Log($"WIND {look} {phase} frame {n:00} t={c - peak:+0.0;-0.0} gust at the stand {TreeWind.Gust(stand.x, stand.y):0.00}, at the camera {TreeWind.Gust(eye.x, eye.z):0.00}");
            }
        }
        TreeWind.Still();
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        TestHarness.Exit(0);
    }

    static void Shoot(Camera cam, string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 720, h = 540;
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
        RenderTexture.active = null; cam.targetTexture = null; rt.Release();
    }
}
