using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The snow guns making snow in the morning (SnowGuns.cs), at phone size: a gun side-on against the sky, the chase
/// camera riding down past them, and close under the nozzle; before dawn, in the morning and (off) at midday. Logs
/// the plumes' particles alive and their overdraw (every fragment counted, an upper bound).
/// $SNOWGUN_OFF=1 renders the same with the guns held off (the look before this branch).
/// Batch: -executeMethod SnowGunShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes into $SNOWGUN_OUT (or SnowGunShots/), names prefixed by $SNOWGUN_TAG.
/// </summary>
public static class SnowGunShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float Dt = 1f / 60f;
    static SummitGame game; static Camera cam; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase; static NightLights lights; static PisteKit kit; static SnowGuns guns;
    static MethodInfo camStep, aLate, tUpd, fUpd;
    static string outDir, tag;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("SNOWGUN_OUT") ?? "SnowGunShots";
        tag = System.Environment.GetEnvironmentVariable("SNOWGUN_TAG") ?? "shot";
        bool off = System.Environment.GetEnvironmentVariable("SNOWGUN_OFF") == "1";
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
        lights = NightLights.Instance; kit = PisteKit.Instance; guns = SnowGuns.Instance;
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        cam.aspect = W / (float)H;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        if (guns == null) { Debug.LogError("SNOWGUN: no SnowGuns"); EditorApplication.Exit(1); return; }
        SnowGuns.TestOn = off ? 0f : -1f;

        // the first gun on a clear stretch of the lower mountain
        PisteKit.Item gun = default; bool found = false;
        foreach (var it in PisteKit.Items)
            if (it.kind == PisteKit.Kind.Gun && it.size > 0f && Course.At(it.foot.z) == Course.Section.Cruise && Course.At(it.foot.z - 60f) == Course.Section.Cruise) { gun = it; found = true; break; }
        if (!found) foreach (var it in PisteKit.Items) if (it.kind == PisteKit.Kind.Gun && it.size > 0f) { gun = it; found = true; break; }
        if (!found) { Debug.LogError("SNOWGUN: no gun on this mountain"); EditorApplication.Exit(1); return; }
        Debug.Log($"SNOWGUN gun at {gun.foot}, aim {gun.aim}; morning level at 0.4: {SnowGuns.MorningLevel(0.4f):0.00}, 3.9: {SnowGuns.MorningLevel(3.9f):0.00}, 1.4: {SnowGuns.MorningLevel(1.4f):0.00}, 2.4: {SnowGuns.MorningLevel(2.4f):0.00}");

        foreach (var (light, clock) in new[] { ("dawn", 3.9f), ("morning", 0.4f), ("midday", 1.4f) })
        {
            Light(clock);
            float s = Mathf.Sign(gun.foot.x - Slope.Centre(gun.foot.z));
            // the rider down the run a little above the gun (everything streams round them)
            Place(new Vector3(Slope.Centre(gun.foot.z - 25f), 0f, gun.foot.z - 25f));
            // square-on to the jet, from below it: the plume against the sky
            Vector3 side = Vector3.Cross(Vector3.up, gun.aim);
            if (side.z < 0f) side = -side;   // (from down the hill)
            Vector3 mid = gun.foot + gun.aim * 6f;
            Vector3 eye = Ground(mid.x + side.x * 24f, mid.z + side.z * 24f, 2.2f);
            Vector3 at = mid + Vector3.up * 2.5f;
            Look(eye, at, 62f); Settle(6f); Where(); Shoot($"{tag}_{light}_side.png");
            if (light == "morning") Cost();
            // close under the nozzle
            eye = Ground(gun.foot.x - s * 6f, gun.foot.z - 5f, 1.6f);
            Look(eye, gun.foot + Vector3.up * 3.6f + gun.aim * 2.5f, 55f); Settle(2f); Shoot($"{tag}_{light}_close.png");
            // a rider's eye coming down the run toward it
            eye = Ground(Slope.Centre(gun.foot.z - 32f), gun.foot.z - 32f, 2.4f);
            Look(eye, gun.foot + gun.aim * 8f + Vector3.up * 2f, 60f); Settle(4f); Shoot($"{tag}_{light}_ride.png");
        }

        SnowGuns.TestOn = -1f;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static Vector3 Ground(float x, float z, float up) => new Vector3(x, Slope.DrawnHeight(x, z) + up, z);

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

    static void Place(Vector3 at)
    {
        rider.ResetAt(at.x, at.z); rider.Begin();
        rider.Body.linearVelocity = Vector3.zero;
        terrain.Prewarm(); props.Prewarm(); lights?.Prewarm(); kit.Prewarm();
        // the lifts, as their Update would (batchmode calls none)
        var lifts = Object.FindFirstObjectByType<SkiLift>();
        if (lifts != null) typeof(SkiLift).GetMethod("Update", NP).Invoke(lifts, null);
        guns.ClearPlumes();
    }

    /// <summary>The plumes run for a while (the guns stepped, their particles simulated), the camera where it is.</summary>
    static void Settle(float secs)
    {
        var keep = (cam.transform.position, cam.transform.rotation);
        for (float t = 0f; t < secs; t += 1f / 30f)
        {
            guns.Step(1f / 30f);
            foreach (var ps in guns.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(1f / 30f, false, false, false);
            Frame(1f / 30f);
        }
        cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
    }

    static void Where()
    {
        foreach (Transform t in guns.transform)
        {
            int n = 0; foreach (var ps in t.GetComponentsInChildren<ParticleSystem>()) n += ps.particleCount;
            if (t.name.StartsWith("SnowGunPlume")) Debug.Log($"SNOWGUN {t.name} at {t.position}, {n} particles; camera {cam.transform.position}");
        }
    }

    static void Look(Vector3 eye, Vector3 at, float fov)
    {
        chase.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
        guns.Step(0f);
    }

    /// <summary>Ridden down the middle at 20 m/s from z for `secs` (the guns running round it), the chase camera following.</summary>
    static void Ride(float z, float secs)
    {
        Place(new Vector3(Slope.Centre(z), 0f, z));
        rider.Heading = 0f;
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z), z)).normalized * 20f;
        chase.enabled = true; chase.rider = rider; chase.Snap();
        Settle(4f);
        for (float t = 0f; t < secs; t += Dt)
        {
            rider.Steer = Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.X) * 0.06f - rider.Heading * 0.8f, -1f, 1f);
            for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            body.Apply(Dt);
            ChaseCamera.TestDt = Dt;
            camStep.Invoke(chase, null);
            kit.Prewarm();
            guns.Step(Dt);
            foreach (var ps in guns.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(Dt, false, false, false);
            Frame(Dt);
        }
    }

    static void Cost()
    {
        var count = new Material(Shader.Find("Hidden/NightLightsCount"));
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf);
        var px = new Texture2D(W, H, TextureFormat.RGBAHalf, false);
        var rs = new List<ParticleSystemRenderer>(guns.GetComponentsInChildren<ParticleSystemRenderer>());
        var layers = new List<int>(); var mats = new List<Material[]>();
        foreach (var r in rs) { layers.Add(r.gameObject.layer); mats.Add(r.sharedMaterials); r.gameObject.layer = 31; r.sharedMaterial = count; }
        int mask = cam.cullingMask; var clear = cam.clearFlags; var bg = cam.backgroundColor;
        cam.cullingMask = 1 << 31; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear;
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
        RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, W, H), 0, 0); px.Apply(); RenderTexture.active = null;
        double sum = 0; foreach (var c in px.GetPixels()) sum += c.r;
        for (int i = 0; i < rs.Count; i++) { rs[i].gameObject.layer = layers[i]; rs[i].sharedMaterials = mats[i]; }
        cam.cullingMask = mask; cam.clearFlags = clear; cam.backgroundColor = bg;
        rt.Release(); Object.DestroyImmediate(px); Object.DestroyImmediate(count);
        Debug.Log($"SNOWGUN sound: {guns.SoundState}");
        Debug.Log($"SNOWGUN cost: level {guns.Level:0.00}, {guns.Alive} particles alive in the plumes (at most {SnowGuns.Plumes * (SnowGuns.JetMax + SnowGuns.MistMax + SnowGuns.FlakeMax)}), overdraw {sum / (W * H):0.00} screens of fragments in this view");
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
}
