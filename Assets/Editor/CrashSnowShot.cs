using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The snow a crash leaves (crashsnow), at phone size: on groomed corduroy, in deep powder and on ice, by day and at
/// night under a floodlight. Each case rides a clear stretch at speed (a chase frame of the corduroy first), crashes,
/// lets the body slide to rest, and shoots the game's own crash camera, straight down from 3.5 m over the body, and
/// a low close look across the furrow. Also a close look at the corduroy alone, and one on the glacier.
/// Batch: -executeMethod CrashSnowShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes into $CRASHSNOW_OUT (or CrashSnowShots/), names prefixed by $CRASHSNOW_TAG (before/after).
/// </summary>
public static class CrashSnowShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float Dt = 1f / 60f;
    static SummitGame game; static Camera cam; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase; static NightLights lights;
    static MethodInfo camStep, aLate, tUpd, fUpd;
    static string outDir, tag;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("CRASHSNOW_OUT") ?? "CrashSnowShots";
        tag = System.Environment.GetEnvironmentVariable("CRASHSNOW_TAG") ?? "shot";
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
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        cam.aspect = W / (float)H;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;

        float dayZ = Stretch(Course.GlacierBottom + 200f, -1f), nightZ = dayZ;
        foreach (var l in NightLights.Lamps)
            if (l.kind == NightLights.Kind.Piste && l.head.z > dayZ) { float z = Stretch(l.head.z - 45f, l.head.z - 30f); if (z > 0f) { nightZ = z; break; } }
        Debug.Log($"CRASHSNOW stretches: day z {dayZ:0}, night z {nightZ:0}");
        string only = System.Environment.GetEnvironmentVariable("CRASHSNOW_ONLY");

        foreach (var (light, z, clock) in new[] { ("day", dayZ, 1.4f), ("night", nightZ, 3.4f) })
        {
            Light(clock);
            foreach (var (name, snow) in new[] { ("groomed", Snow.Corduroy), ("powder", Snow.Powder), ("ice", Snow.Ice) })
            {
                if (!string.IsNullOrEmpty(only) && !name.StartsWith(only)) continue;
                Surfaces.Force = SnowMix.Only(snow);
                Crash($"{light}_{name}", z);
            }
            Surfaces.Force = null;
            // the corduroy alone, close: 1.4 m over it, looking down the run
            Close($"{light}_corduroy", z + 40f);
            // the glacier: no grooming lines up there
            Close($"{light}_glacier", Mathf.Lerp(Course.GlacierTop, Course.GlacierBottom, 0.55f));
        }

        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static float Stretch(float from, float near)
    {
        float lo = near > 0f ? near - 60f : from, hi = near > 0f ? near + 60f : Slope.FinishZ - 400f;
        for (float z = lo; z < hi; z += 10f)
        {
            bool ok = true;
            for (float d = -30f; d <= 70f && ok; d += 10f)
            {
                if (Course.At(z + d) != Course.Section.Cruise || Ridge.InCanyons(z + d) || Cliffs.JumpNear(z + d, 10f, out _)) ok = false;
                else if (Slope.Normal(Slope.Centre(z + d), z + d).y < 0.74f) ok = false;
                for (float a = -12f; a <= 12f && ok; a += 6f) if (Mathf.Abs(Slope.Kicker(Slope.Centre(z + d) + a, z + d)) > 0.01f) ok = false;
            }
            if (ok) return z;
        }
        if (near > 0f) return -1f;
        // (nothing that clear: the first cruise stretch below the glacier)
        for (float z = from; z < Slope.FinishZ - 400f; z += 10f) if (Course.At(z) == Course.Section.Cruise && Course.At(z + 60f) == Course.Section.Cruise && !Ridge.InCanyons(z)) return z;
        return Course.First + 500f;
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

    static void Step()
    {
        for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        body.Apply(Dt);
        chase.enabled = true;
        ChaseCamera.TestDt = Dt;
        camStep.Invoke(chase, null);
        Surfaces.Step(rider.Crashed ? body.RagdollCentre : rider.Body.position, rider.RunNow, Dt, true);
        Frame(Dt);
    }

    /// <summary>Ridden into a crash at 20 m/s on a clear stretch; the crash camera, the overhead and the close look once it lies still.</summary>
    static void Crash(string name, float z)
    {
        body.ResetVisual(); SnowLayer.Instance?.Clear();
        rider.ResetAt(Slope.Centre(z), z); rider.Begin();
        rider.Heading = 0f; rider.Steer = 0f;
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z), z)).normalized * 20f;
        terrain.Prewarm(); props.Prewarm(); lights?.Prewarm(); Physics.SyncTransforms();
        chase.rider = rider; chase.Snap();
        for (float t = 0f; t < 1.5f; t += Dt)
        {
            rider.Steer = Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.X) * 0.06f - rider.Heading * 0.8f, -1f, 1f);
            Step();
        }
        Shoot($"{tag}_{name}_ride.png");
        Vector3 start = rider.transform.position;
        rider.DoCrash("test");
        float rest = 0f;
        for (float t = 0f; t < 8f; t += Dt)
        {
            rider.Steer = 0f;
            Step();
            if (t > 1f && !body.RagdollMoving) { rest += Dt; if (rest > 0.4f) break; }
        }
        Vector3 c = body.RagdollCentre;
        Debug.Log($"CRASHSNOW {name}: slid {Vector3.Distance(start, c):0.0} m to rest at {c}");
        // the game's own crash camera, as it is now
        Shoot($"{tag}_{name}_crashcam.png");
        chase.enabled = false;
        var fov = cam.fieldOfView;
        // straight down from over the body (a little up the hill of it, so the slide's path shows)
        Vector3 over = c + Vector3.up * 3.5f - Vector3.forward * 0.8f;
        cam.fieldOfView = 55f;
        cam.transform.SetPositionAndRotation(over, Quaternion.LookRotation(c - over + Vector3.forward * 1.2f, Vector3.forward));
        Shoot($"{tag}_{name}_overhead.png");
        // low and close, across the furrow from below
        Vector3 eye = c + new Vector3(2.4f, 0f, 2.2f); eye.y = Slope.DrawnHeight(eye.x, eye.z) + 0.9f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c - eye - Vector3.forward * 1.5f));
        Shoot($"{tag}_{name}_close.png");
        cam.fieldOfView = fov;
        chase.enabled = true;
    }

    /// <summary>The snow alone, 1.4 m up, looking down the run at 35 degrees (the corduroy's look close to).</summary>
    static void Close(string name, float z)
    {
        body.ResetVisual(); SnowLayer.Instance?.Clear();
        rider.ResetAt(Slope.Centre(z - 30f), z - 30f); rider.Begin();
        terrain.Prewarm(); props.Prewarm(); lights?.Prewarm();
        Surfaces.Step(new Vector3(Slope.Centre(z), Slope.Height(Slope.Centre(z), z), z), rider.RunNow, 0.1f, true);
        chase.enabled = false;
        Vector3 eye = new Vector3(Slope.Centre(z) + 3f, 0f, z);
        eye.y = Slope.DrawnHeight(eye.x, eye.z) + 1.4f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Quaternion.AngleAxis(35f, Vector3.right) * Vector3.forward));
        Frame(0f);
        Shoot($"{tag}_{name}_close.png");
        chase.enabled = true;
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
        SnowLayer.Instance?.Flush();
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
}
