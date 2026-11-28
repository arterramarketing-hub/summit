using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The near/far tree hand-over in every time of day: stands on the piste a set distance from a stand of
/// realistic firs and looks at it, once per phase (TimeOfDay), so a row of runs at 170, 150 ... 90 m shows
/// the far pictures (impostors) turning into the near card trees as you approach.
/// Run: TREEFADE_D=130 Unity -batchmode -projectPath . -executeMethod TreeFadeShot.Run -logFile fade.log
/// (no -nographics). One distance a run: in batchmode the instanced draws queued for one render are never
/// flushed, so a forest refreshed for another distance would show through. Writes TreeFade_{d}_{phase}.png;
/// with TREEFADE_WIDE=1 also the run ahead at dusk and night with and without the far forest's night lift.
/// </summary>
public static class TreeFadeShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly (string name, float clock)[] Times =
        { ("1morning", 0.4f), ("2day", 1.4f), ("3golden", 2.15f), ("4pink", 2.75f), ("5night", 3.4f) };

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();   // (before the tree look is set: the boot pins only once)
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

        float d = float.Parse(System.Environment.GetEnvironmentVariable("TREEFADE_D") ?? "130", System.Globalization.CultureInfo.InvariantCulture);
        // the stand: in the trees off the piste's right edge; we stand on the piste up the hill from it
        float z0 = Course.First + 420f;
        var stand = new Vector3(Slope.Centre(z0) + Slope.PisteHalf + 26f, 0f, z0);
        stand.y = Slope.Surface(stand.x, stand.z);
        // on the piste's centre line, up the hill from the stand, d metres from it (across the ground)
        float ex = Slope.Centre(z0), dx = stand.x - ex, ez = z0 - Mathf.Sqrt(Mathf.Max(1f, d * d - dx * dx));
        ex = Slope.Centre(ez);
        dx = stand.x - ex; ez = z0 - Mathf.Sqrt(Mathf.Max(1f, d * d - dx * dx));
        var eye = new Vector3(ex, Slope.Surface(ex, ez) + 3.5f, ez);
        // the rider (whom the forest counts distance from) stands where the camera is
        rider.ResetAt(ex, ez);
        rider.transform.position = new Vector3(ex, eye.y - 2f, ez);
        props.target = rider.transform;
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
        rider.gameObject.SetActive(false);   // only the trees in frame

        cam.fieldOfView = 26f;
        // aim a little past the stand so it sits in the frame with forest around it
        var aim = stand + Vector3.up * 5f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
        Debug.Log($"TREEFADE d={d} eye={eye} stand={stand} horiz={Vector2.Distance(new Vector2(eye.x, eye.z), new Vector2(stand.x, stand.z)):0.0}");

        var draw = typeof(Forest).GetMethod("Draw", NP);
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        // warm up (the first renders in batchmode come out with placeholder materials)
        for (int i = 0; i < 2; i++) { aLate.Invoke(atmo, null); draw.Invoke(props, null); Shoot(cam, null); }
        foreach (var (name, clock) in Times)
        {
            TimeOfDay.TestClock = clock; dayNight.ApplyNow();
            aLate.Invoke(atmo, null);
            if (fx != null) fx.Clear();
            for (int k = 0; k < 2; k++) { draw.Invoke(props, null); Shoot(cam, k == 1 ? $"TreeFade_{d:000}_{name}.png" : null); }
        }
        if (System.Environment.GetEnvironmentVariable("TREEFADE_WIDE") == "1")
        {
            // the run ahead at dusk and night: the far forest with and without its moonlit lift
            cam.fieldOfView = 55f;
            var look = new Vector3(Slope.Centre(ez + 260f), 0f, ez + 260f); look.y = Slope.Surface(look.x, look.z);
            cam.transform.rotation = Quaternion.LookRotation(look - eye);
            foreach (var (name, clock) in Times)
            {
                if (clock < 2.5f) continue;
                TimeOfDay.TestClock = clock; dayNight.ApplyNow(); aLate.Invoke(atmo, null);
                var lift = Shader.GetGlobalColor("_FirLift");
                for (int k = 0; k < 2; k++) { draw.Invoke(props, null); Shoot(cam, k == 1 ? $"TreeFade_wide_{name}_lift.png" : null); }
                Shader.SetGlobalColor("_FirLift", Color.clear);
                draw.Invoke(props, null); Shoot(cam, $"TreeFade_wide_{name}_nolift.png");
                Debug.Log($"TREEFADE wide {name} lift={lift}");
            }
        }
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
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
        RenderTexture.active = null; cam.targetTexture = null; rt.Release();
    }
}
