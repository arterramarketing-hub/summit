using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The groomed snow's lines (SnowSurface.shader's corduroy, the snow material's wind grooves), at phone size: a strip
/// of the chase camera's frames riding a groomed cruise stretch at speed, 1/30 s apart (strobing and crawling lines
/// show as differences between neighbouring frames), by day and at night under a floodlight; a close view of the
/// snow at the board, and a far view down the run. Writes into $GROOVES_OUT (or GroovesShots/), names prefixed by
/// $GROOVES_TAG (before/after).
/// Batch: -executeMethod GroovesShot.Run -logFile x.log   (no -nographics: it renders)
/// </summary>
public static class GroovesShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float Dt = 1f / 60f;
    static SummitGame game; static Camera cam; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase; static NightLights lights;
    static MethodInfo camStep, aLate;
    static string outDir, tag;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("GROOVES_OUT") ?? "GroovesShots";
        tag = System.Environment.GetEnvironmentVariable("GROOVES_TAG") ?? "shot";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight"); weather = Get<Weather>("weather");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); atmo = Object.FindFirstObjectByType<Atmosphere>();
        lights = NightLights.Instance;
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        cam.aspect = W / (float)H;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;

        // a groomed cruise stretch: corduroy all the way, not too steep, 160 m of it; and one with a piste floodlight
        float dayZ = Stretch(Course.First + 500f, -1f);
        float nightZ = dayZ;
        foreach (var l in NightLights.Lamps)
            if (l.kind == NightLights.Kind.Piste && l.head.z > dayZ) { float z = Stretch(l.head.z - 70f, l.head.z - 20f); if (z > 0f) { nightZ = z; break; } }
        Debug.Log($"GROOVES stretches: day z {dayZ:0}, night z {nightZ:0}");

        foreach (var (name, z, clock) in new[] { ("day", dayZ, 1.4f), ("night", nightZ, 3.4f) })
        {
            Light(clock);
            Strip(name, z);
            Close(name, z + 60f);
            Far(name, z + 30f);
        }

        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    /// <summary>The first z from `from` (or about `near`) with groomed cruise snow from 40 m before it to 160 m after.</summary>
    static float Stretch(float from, float near)
    {
        float lo = near > 0f ? near - 60f : from, hi = near > 0f ? near + 60f : Slope.FinishZ - 400f;
        for (float z = lo; z < hi; z += 10f)
        {
            bool ok = true;
            for (float d = -40f; d <= 160f && ok; d += 10f)
            {
                var sec = Course.At(z + d);
                if (sec != Course.Section.Cruise || Ridge.InCanyons(z + d) || Cliffs.JumpNear(z + d, 10f, out _)) ok = false;
                else if (Slope.Normal(Slope.Centre(z + d), z + d).y < 0.74f || Surfaces.Surface(Slope.Centre(z + d), z + d).Corduroy < 0.95f) ok = false;
            }
            if (ok) return z;
        }
        return near > 0f ? -1f : Course.First + 500f;
    }

    static void Light(float clock)
    {
        Weather.TestChoice = 1; TimeOfDay.TestClock = clock;
        weather.Step(0f);
        for (int i = 0; i < 30; i++) Frame(1f / 30f);
        dayNight.ApplyNow();
        lights?.Prewarm();
    }

    static void Frame(float dt)
    {
        TreeWind.Advance(dt, cam.transform.position);
        weather.Step(dt);
        aLate.Invoke(atmo, null);
        dayNight.ApplyNow();
    }

    /// <summary>Ridden at 25 m/s down the middle of the run: six frames of the chase camera, 1/30 s apart.</summary>
    static void Strip(string name, float z)
    {
        const float V = 25f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        rider.Heading = 0f; rider.Steer = 0f;
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z), z)).normalized * V;
        terrain.Prewarm(); props.Prewarm(); lights?.Prewarm(); Physics.SyncTransforms();
        chase.rider = rider; chase.Snap();
        var files = new List<string>();
        int shots = 0;
        for (int n = 0; n < 60 * 5 && shots < 6; n++)
        {
            rider.Steer = rider.Crashed ? 0f : Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.X) * 0.05f, -0.3f, 0.3f);
            if (rider.Speed > V && !rider.Airborne) rider.Body.linearVelocity *= V / rider.Speed;
            rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 30 == 0) { terrain.Prewarm(); props.Prewarm(); lights?.Prewarm(); }
            if (n % 2 == 0)
            {
                body.Apply(2 * Dt);
                ChaseCamera.TestDt = 2 * Dt;
                camStep.Invoke(chase, null);
                Surfaces.Step(rider.Body.position, rider.RunNow, 2 * Dt, true);
                Frame(2 * Dt);
                // after 3 s at speed, every frame (1/30 s apart)
                if (n >= 180) { string f = $"{tag}_{name}_ride{shots}.png"; Shoot(f); files.Add(f); shots++; }
            }
        }
        Sheet($"{tag}_{name}_strip.png", files.ToArray());
        Debug.Log($"GROOVES {name} strip at z {rider.Z:0}, {rider.Speed * 3.6f:0} km/h, crashed {rider.Crashed}");
    }

    /// <summary>The snow at the board: 1.5 m over the run, looking down it at 40 degrees.</summary>
    static void Close(string name, float z)
    {
        rider.ResetAt(Slope.Centre(z - 30f), z - 30f); rider.Begin();
        terrain.Prewarm(); props.Prewarm(); lights?.Prewarm();
        Surfaces.Step(new Vector3(Slope.Centre(z), Slope.Height(Slope.Centre(z), z), z), rider.RunNow, 0.1f, true);
        Vector3 eye = new Vector3(Slope.Centre(z) + 3f, 0f, z);
        eye.y = Slope.DrawnHeight(eye.x, eye.z) + 1.5f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Quaternion.AngleAxis(40f, Vector3.right) * Vector3.forward));
        Frame(0f);
        Shoot($"{tag}_{name}_close.png");
    }

    /// <summary>Far down the run: 4 m up, looking 300 m down it.</summary>
    static void Far(string name, float z)
    {
        rider.ResetAt(Slope.Centre(z), z); rider.Begin();
        terrain.Prewarm(); props.Prewarm(); lights?.Prewarm();
        Surfaces.Step(new Vector3(Slope.Centre(z), Slope.Height(Slope.Centre(z), z), z), rider.RunNow, 0.1f, true);
        Vector3 eye = new Vector3(Slope.Centre(z), 0f, z);
        eye.y = Slope.DrawnHeight(eye.x, eye.z) + 4f;
        Vector3 at = new Vector3(Slope.Centre(z + 300f), 0f, z + 300f);
        at.y = Slope.DrawnHeight(at.x, at.z);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
        Frame(0f);
        Shoot($"{tag}_{name}_far.png");
    }

    static void Shoot(string file)
    {
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g);
        }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
    }

    static void Sheet(string file, params string[] names)
    {
        int w = W / 2, h = H / 2;
        var sheet = new Texture2D(w * names.Length, h, TextureFormat.RGB24, false);
        for (int k = 0; k < names.Length; k++)
        {
            var p = Path.Combine(outDir, names[k]);
            if (!File.Exists(p)) continue;
            var t = new Texture2D(2, 2);
            if (!t.LoadImage(File.ReadAllBytes(p))) continue;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    sheet.SetPixel(k * w + x, y, t.GetPixel(x * 2, y * 2));
            Object.DestroyImmediate(t);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }
}
