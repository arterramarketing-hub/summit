using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders each time of day (TimeOfDay, DayNight.cs) at the start of a run, mid-run, and in the summit camp.
/// Batch: -executeMethod DayNightShot.Run  (writes DayNight_*.png in the project folder)
/// </summary>
public static class DayNightShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    // the moments: a held phase sits at TimeOfDay.HeldAt into it; evening is shown golden and then pink
    static readonly (string name, float clock)[] Times =
    {
        ("1morning", 0.4f), ("2day", 1.4f), ("3golden", 2.15f), ("4pink", 2.75f), ("5night", 3.4f),
        ("6day-to-evening", 1.9f), ("7night-to-morning", 3.92f),   // mid-blend, as the cycle passes between them
    };
    static Camera cam;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var signSh = Resources.Load<Shader>("SignLit");
        if (signSh != null)
        {
            Debug.Log($"DAYNIGHTSHOT SignLit supported {signSh.isSupported} errors {ShaderUtil.ShaderHasError(signSh)} passes {signSh.passCount}");
            foreach (var m in ShaderUtil.GetShaderMessages(signSh)) Debug.Log($"DAYNIGHTSHOT SignLit: {m.severity} {m.message} line {m.line} {m.messageDetails}");
        }
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = game.rider; var body = game.body;
        var terrain = game.terrain; var props = game.props;
        var dayNight = game.dayNight; var camp = game.camp; var fx = Get<SnowFX>("fx");
        cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var fUpd = typeof(Forest).GetMethod("Update", NP);
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 1f / 60f;
        const float dt = 1f / 60f;
        float[] at = { 1.2f, 12f, 22f };
        string[] where = { "start", "midrun", "late" };
        int next = 0;
        for (float t = 0f; next < at.Length; t += dt)
        {
            for (int k = 0; k < 2; k++) { rider.Tick(dt / 2f); body.PhysicsStep(); Physics.Simulate(dt / 2f); }
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            body.Apply(dt);
            late.Invoke(chase, null);
            foreach (var tp in Object.FindObjectsByType<CautionTape>(FindObjectsSortMode.None)) typeof(CautionTape).GetMethod("LateUpdate", NP).Invoke(tp, null);
            aLate.Invoke(atmo, null);
            if (t < at[next]) continue;
            if (next == 0) for (int i = 0; i < 3; i++) Shoot("DayNight_warmup.png");   // placeholder materials, then the real ones
            if (next == 0) { SkyScan(dayNight); SkyViews(dayNight); }
            foreach (var (name, clock) in Times)
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                fx.Sprinkle(400);
                Shoot($"DayNight_{where[next]}_{name}.png");
            }
            if (next == 2)
            {
                // the pause menu with its TIME row, at phone size (an overlay canvas doesn't reach a render texture)
                var hud = game.hud; var hc = hud.GetComponent<Canvas>();
                hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
                hud.ShowPause(true); hud.ShowTab(1);   // (LOOK: the TIME OF DAY row)
                Canvas.ForceUpdateCanvases();
                Shoot("DayNight_pause.png");
                hud.ShowPause(false); hc.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            if (next == 0)
            {
                // the start gate's signs at night, in the headlamp and with it off
                TimeOfDay.TestClock = 3.4f; dayNight.ApplyNow(); Shoot("DayNight_signs_lamp_on.png");
                DayNight.HeadlampOff = true; dayNight.ApplyNow(); Shoot("DayNight_signs_lamp_off.png");
                DayNight.HeadlampOff = false;
            }
            Debug.Log($"DAYNIGHTSHOT {where[next]} t={t:0.0} rider={rider.transform.position} cam={cam.transform.position}");
            next++;
        }
        // a fall: the glow follows the ragdoll's middle
        rider.DoCrash("test");
        for (float t = 0f; t < 1.4f; t += dt)
        {
            for (int k = 0; k < 2; k++) { rider.Tick(dt / 2f); body.PhysicsStep(); Physics.Simulate(dt / 2f); }
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            body.Apply(dt); late.Invoke(chase, null); aLate.Invoke(atmo, null);
        }
        foreach (var (name, clock) in Times) { TimeOfDay.TestClock = clock; dayNight.ApplyNow(); Shoot($"DayNight_ragdoll_{name}.png"); }
        Debug.Log($"DAYNIGHTSHOT ragdoll centre {body.RagdollCentre} rider {rider.transform.position}");
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;

        // the camp: back up to it on foot, and look back up at it from the gate
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = dt;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(3f, true, true);
        void Settle() { for (int i = 0; i < 40; i++) { camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt); tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); aLate.Invoke(atmo, null); } }
        // where you arrive, then the chalet from across the camp (HubShot's views)
        Settle();
        foreach (var (name, clock) in Times) { TimeOfDay.TestClock = clock; dayNight.ApplyNow(); Shoot($"DayNight_camp_{name}.png"); }
        var from = camp.ChaletPoint(9f, 0f, 22f); var to = camp.ChaletPoint(0f, 0f, 0f);
        camp.TeleportTo(from, Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg);
        Settle();
        foreach (var (name, clock) in Times) { TimeOfDay.TestClock = clock; dayNight.ApplyNow(); Shoot($"DayNight_chalet_{name}.png"); }
        ChaseCamera.TestDt = 0f;
        // back to day before quitting: the particle materials are project assets, and the editor saves them on exit
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        TestHarness.Exit(0);
    }

    /// <summary>
    /// Is the light where the sky's sun is? For each phase, turn a sky-only camera round at the light's height
    /// and find the brightest heading; log it next to the light's own heading.
    /// </summary>
    static void SkyScan(DayNight dayNight)
    {
        var go = new GameObject("SkyScan"); var c = go.AddComponent<Camera>();
        c.cullingMask = 0; c.clearFlags = CameraClearFlags.Skybox; c.fieldOfView = 12f; c.aspect = 1f;
        var rt = new RenderTexture(16, 16, 24); c.targetTexture = rt;
        var tex = new Texture2D(16, 16, TextureFormat.RGB24, false);
        foreach (var (name, clock) in Times)
        {
            if (name.Contains("-to-")) continue;
            TimeOfDay.TestClock = clock; dayNight.ApplyNow();
            Vector3 s = dayNight.SunDirection;
            float el = Mathf.Asin(s.y) * Mathf.Rad2Deg, az = Mathf.Atan2(s.x, s.z) * Mathf.Rad2Deg;
            float best = -1f, bestAz = 0f;
            for (int k = 0; k < 120; k++)
            {
                float h = k * 3f;
                go.transform.rotation = Quaternion.Euler(-el, h, 0f);
                c.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); tex.Apply();
                float sum = 0f; foreach (var p in tex.GetPixels()) sum += p.grayscale;
                if (sum > best) { best = sum; bestAz = h; }
            }
            Debug.Log($"DAYNIGHTSHOT skyscan {name}: light az {Mathf.Repeat(az, 360f):0} el {el:0.0}, brightest sky at az {bestAz:0}; ambient sky {RenderSettings.ambientSkyColor} eq {RenderSettings.ambientEquatorColor} fog {RenderSettings.fogColor} sun {RenderSettings.sun.color} x{RenderSettings.sun.intensity:0.00}");
        }
        RenderTexture.active = null; c.targetTexture = null; rt.Release(); Object.DestroyImmediate(go);
        var probe = Object.FindFirstObjectByType<ReflectionProbe>();
        var cube = probe != null ? probe.customBakedTexture as Cubemap : null;
        Debug.Log($"DAYNIGHTSHOT reflections captured from the sky: {dayNight.ReflectionCaptured}");
        Debug.Log($"DAYNIGHTSHOT reflection cube {(cube != null ? cube.name + " " + cube.width + " mips " + cube.mipmapCount : "none")}, ambient {RenderSettings.ambientMode} sky {RenderSettings.ambientSkyColor} eq {RenderSettings.ambientEquatorColor} ground {RenderSettings.ambientGroundColor}");
    }

    /// <summary>The sky alone: up overhead (no pinch at the top) and round behind (no seam), in each phase.</summary>
    static void SkyViews(DayNight dayNight)
    {
        var keep = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
        foreach (var (name, clock) in Times)
        {
            if (name.Contains("-to-")) continue;
            TimeOfDay.TestClock = clock; dayNight.ApplyNow();
            cam.fieldOfView = 70f;
            cam.transform.rotation = Quaternion.Euler(-75f, 0f, 0f); Shoot($"DayNight_up_{name}.png");
            cam.transform.rotation = Quaternion.Euler(-12f, 180f, 0f); Shoot($"DayNight_back_{name}.png");
        }
        cam.transform.SetPositionAndRotation(keep.Item1, keep.Item2); cam.fieldOfView = keep.Item3;
    }

    static void Shoot(string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new System.Collections.Generic.List<GameObject>();
        foreach (var sk in skins) { var m = new Mesh(); sk.BakeMesh(m); var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; baked.Add(g); }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null; rt.Release();
    }
}
