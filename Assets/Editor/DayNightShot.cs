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
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props");
        var dayNight = Get<DayNight>("dayNight"); var camp = Get<Camp>("camp"); var fx = Get<SnowFX>("fx");
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
        float[] at = { 1.2f, 22f };
        string[] where = { "start", "midrun" };
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
            foreach (var (name, clock) in Times)
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                fx.Sprinkle(400);
                Shoot($"DayNight_{where[next]}_{name}.png");
            }
            if (next == 1)
            {
                // the pause menu with its TIME row, at phone size (an overlay canvas doesn't reach a render texture)
                var hud = Get<Hud>("hud"); var hc = hud.GetComponent<Canvas>();
                hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
                hud.ShowPause(true); hud.SetTime(TimeOfDay.LockName[4]);
                Canvas.ForceUpdateCanvases();
                Shoot("DayNight_pause.png");
                hud.ShowPause(false); hc.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            Debug.Log($"DAYNIGHTSHOT {where[next]} t={t:0.0} rider={rider.transform.position} cam={cam.transform.position}");
            next++;
        }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;

        // the camp: back up to it on foot, and look back up at it from the gate
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = dt;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(3f, true, true);
        camp.Teleport(1.5f, -82f, 185f);
        for (int i = 0; i < 40; i++)
        {
            camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt);
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); aLate.Invoke(atmo, null);
        }
        foreach (var (name, clock) in Times)
        {
            TimeOfDay.TestClock = clock; dayNight.ApplyNow();
            Shoot($"DayNight_camp_{name}.png");
        }
        ChaseCamera.TestDt = 0f;
        // back to day before quitting: the particle materials are project assets, and the editor saves them on exit
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        EditorApplication.Exit(0);
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
