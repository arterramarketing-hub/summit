using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (cordflicker) The groomed lines on a cliff band's traverse, at speed: a bot rides the run's line from above a band's
/// turn in, through it and along the traverse, at 160 km/h with the chase camera, and the frames are written as a strip
/// (frame_NNN.png, 30 a second of the ride) for a clip. And the lines' own stillness, measured: at points on the turn in
/// and the traverse the camera is held where it is, the rider hidden, and the ride run on 1/30 s; the snow drawn again
/// from the same camera should not change (the lines are on the snow, not on the rider). Logs CORDSHOT lines, with the
/// share of the snow's pixels that changed; exits 1 if over 0.5% did at any point (before cordflicker: 6-30%).
/// CORD_SEED=seed, CORD_OUT=dir. Batch: -executeMethod CordShot.Shots (no -nographics: it renders).
/// </summary>
public static class CordShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1280;
    const float Dt = 1f / 120f, Speed = 160f / 3.6f;
    static SummitGame game; static Camera cam; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase;
    static MethodInfo camStep;
    static RenderTexture rt; static Texture2D tex;

    public static void Shots()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        string outDir = System.Environment.GetEnvironmentVariable("CORD_OUT") ?? "cord-shots";
        System.IO.Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        r = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight"); weather = Get<Weather>("weather");
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        Physics.simulationMode = SimulationMode.Script;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("CORD_SEED"), out int ss) ? ss : 1000;
        TestBoot.NewMountain(game, seed, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        if (chase == null) chase = Object.FindFirstObjectByType<ChaseCamera>();
        chase.enabled = false;
        rt = new RenderTexture(W, H, 24); tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1.4f; weather.Step(0f); dayNight.ApplyNow(); Surfaces.Day = default;
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) dress.target = r.transform;
        terrain.target = r.transform; props.target = r.transform;

        // the first full band (not a ledge) on the mountain
        CliffBand.Band b = default; bool found = false;
        for (int c = 0; c < 64 && !found; c++) if (CliffBand.Of(c, out b) && !b.ledge) found = true;
        if (!found) { Debug.LogError("CORDSHOT no band"); Leave(1); return; }
        Debug.Log($"CORDSHOT seed {seed}: band {b.cycle} turns in z {b.z0:0}..{b.z1:0}, traverses to {b.z2:0} at {Mathf.Atan(b.k) * Mathf.Rad2Deg:0} deg");

        float z0 = b.z0 - 50f;
        r.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(r.X, z0)).normalized * Speed;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        if (dress != null) dress.Prewarm();
        chase.rider = r; chase.Snap();
        float[] still = { b.z0 + CliffBand.Le * 0.35f, b.z0 + CliffBand.Le * 0.7f, (b.z1 + b.z2) * 0.5f };
        int nextStill = 0, frame = 0, worst = 0; float worstShare = 0f;
        for (int n = 0; n < 120 * 20 && r.Z < b.z2 - 10f; n++)
        {
            r.Steer = CarveBot.Onto(r, Slope.Centre, 2.4f, 14f, 6f);
            if (!r.Airborne) { var vel = r.Body.linearVelocity; if (vel.magnitude > 1f) r.Body.linearVelocity = vel.normalized * Speed; }
            Step(n);
            if (r.Crashed) { Debug.Log($"CORDSHOT crashed at z {r.Z:0}"); break; }
            if (n % 4 == 0 && r.Z > b.z0 - 20f)
            {
                Grab($"{outDir}/frame_{frame:000}.png");
                frame++;
            }
            if (nextStill < still.Length && r.Z > still[nextStill])
            {
                float share = Still($"{outDir}/still{nextStill}", n);
                Debug.Log($"CORDSHOT still {nextStill} at z {r.Z:0} (heading of the run {Slope.RunHeading(r.Z) * Mathf.Rad2Deg:0} deg, {r.Speed * 3.6f:0} km/h): {share * 100f:0.00}% of the snow's pixels changed in 1/30 s with the camera held");
                if (share > worstShare) { worstShare = share; worst = nextStill; }
                nextStill++;
            }
        }
        Debug.Log($"CORDSHOT {frame} frames; rode to z {r.Z:0}");
        bool ok = nextStill == still.Length && worstShare < 0.005f;
        Debug.Log($"CORDSHOT {(ok ? "PASS" : "FAIL")}: the most that changed with the camera held {worstShare * 100f:0.00}% (still {worst}), {nextStill} of {still.Length} points reached");
        Leave(ok ? 0 : 1);
    }

    static void Step(int n)
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 2 == 0)
        {
            body.Apply(2 * Dt);
            ChaseCamera.TestDt = 2 * Dt;
            camStep.Invoke(chase, null);
            Surfaces.Step(r.Body.position, r.RunNow, 2 * Dt, true);
        }
    }

    /// <summary>The camera held, the rider hidden: drawn, the ride run on 1/30 s (the snow's look stepped with it), drawn
    /// again. The share of the snow's pixels (bright ones) that changed by more than 2 levels (of 255).</summary>
    static float Still(string id, int n)
    {
        var rends = body.GetComponentsInChildren<Renderer>(true);
        var was = new bool[rends.Length];
        for (int i = 0; i < rends.Length; i++) { was[i] = rends[i].enabled; rends[i].enabled = false; }
        var pose = (cam.transform.position, cam.transform.rotation);
        var a = Grab(id + "_a.png");
        for (int k = 0; k < 4; k++)
        {
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (k % 2 == 1) { body.Apply(2 * Dt); Surfaces.Step(r.Body.position, r.RunNow, 2 * Dt, true); }
        }
        foreach (var x in rends) x.enabled = false;
        cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
        var c = Grab(id + "_b.png");
        int changed = 0, snow = 0; long sum = 0;
        var d = new Color32[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            int e = Mathf.Max(Mathf.Abs(a[i].r - c[i].r), Mathf.Max(Mathf.Abs(a[i].g - c[i].g), Mathf.Abs(a[i].b - c[i].b)));
            // (the snow only: bright. The far trees' cross-fade dithers as the rider moves on, by design)
            if (Mathf.Min(a[i].r, Mathf.Min(a[i].g, a[i].b)) > 170)
            {
                snow++; sum += e;
                if (e > 2) changed++;
            }
            byte v = (byte)Mathf.Min(255, e * 8);
            d[i] = new Color32(v, v, v, 255);
        }
        var dt = new Texture2D(W, H, TextureFormat.RGB24, false);
        dt.SetPixels32(d); dt.Apply();
        System.IO.File.WriteAllBytes(id + "_diff.png", dt.EncodeToPNG());
        Object.DestroyImmediate(dt);
        for (int i = 0; i < rends.Length; i++) rends[i].enabled = was[i];
        Debug.Log($"CORDSHOT {System.IO.Path.GetFileName(id)}: {snow} snow pixels, mean change {sum / (float)Mathf.Max(1, snow):0.000} levels");
        return changed / (float)Mathf.Max(1, snow);
    }

    static Color32[] Grab(string file)
    {
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        return tex.GetPixels32();
    }

    static void Leave(int code)
    {
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        if (weather != null) { weather.Step(0f); dayNight.ApplyNow(); Object.DestroyImmediate(weather.gameObject); }
        TimeOfDay.TestClock = -1f; Weather.TestChoice = -1;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(code);
    }
}
