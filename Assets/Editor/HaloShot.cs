using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders halation OFF / SUBTLE / STRONG at each time of day, mid-run, and the pause menu's four tabs.
/// Batch: -executeMethod HaloShot.Run  (writes HaloShots/*.png in the project folder)
/// </summary>
public static class HaloShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly (string name, float clock)[] Times = { ("day", 1.4f), ("golden", 2.15f), ("pink", 2.75f), ("night", 3.4f) };

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        Random.InitState(7);
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props");
        var dayNight = Get<DayNight>("dayNight"); var hud = Get<Hud>("hud");
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var fUpd = typeof(Forest).GetMethod("Update", NP);
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        var applyLook = typeof(SummitGame).GetMethod("ApplyLook", NP);
        var applyCam = typeof(SummitGame).GetMethod("ApplyCamera", NP);
        var setPaused = typeof(SummitGame).GetMethod("SetPaused", NP);
        void Set(string f, int v) => typeof(SummitGame).GetField(f, NP).SetValue(game, v);
        Directory.CreateDirectory("HaloShots");

        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 1f / 60f;
        const float dt = 1f / 60f;
        float[] at = { 5f, 22f };
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
            if (next == 0) for (int i = 0; i < 3; i++) Shoot(cam, "HaloShots/warmup.png");   // placeholder materials, then the real ones

            // under NATURAL and the default lens, so only halation and the light change
            Set("filterLevel", 0); Set("filterStrength", 10); Set("lensLevel", 1); Set("lensMode", 0);
            applyCam.Invoke(game, null);
            foreach (var (name, clock) in Times)
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                for (int h = 0; h < LensFX.HalationName.Length; h++)
                {
                    Set("halation", h); applyLook.Invoke(game, null);
                    Shoot(cam, $"HaloShots/t{next}_{name}_halation_{LensFX.HalationName[h].ToLowerInvariant()}.png");
                }
            }
            // with a filter: FILM, strong halation, golden evening
            TimeOfDay.TestClock = 2.15f; dayNight.ApplyNow();
            Set("filterLevel", 4); Set("halation", 2); applyLook.Invoke(game, null);
            Shoot(cam, $"HaloShots/t{next}_golden_film_strong.png");
            Set("filterLevel", 0); Set("halation", 0); applyLook.Invoke(game, null);
            Debug.Log($"HALOSHOT {next} t={t:0.00} rider={rider.transform.position}");
            next++;
        }

        // the pause menu, each tab: overlay canvases don't draw into a render texture, so hand it the camera,
        // and take the lens off so the menu comes out flat as it is on the phone
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        Set("lensLevel", 0); applyCam.Invoke(game, null);
        var hc = hud.GetComponent<Canvas>(); hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
        setPaused.Invoke(game, new object[] { true });
        for (int k = 0; k < Hud.TabName.Length; k++)
        {
            hud.ShowTab(k);
            Shoot(cam, $"HaloShots/pause_{k}_{Hud.TabName[k].ToLowerInvariant()}.png");
        }
        setPaused.Invoke(game, new object[] { false });
        Time.timeScale = 1f;
        TimeOfDay.TestClock = -1f;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        Canvas.ForceUpdateCanvases();
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
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
