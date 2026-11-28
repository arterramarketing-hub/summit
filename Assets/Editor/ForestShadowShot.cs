using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Summit;

/// <summary>
/// The forest's shadows as the phone draws them (the Mobile quality level: two cascades), by day and in the golden
/// evening, from the chase camera riding down the run: near the start, and twice further down. FORESTSHADOW_DIST sets
/// the shadow distance (m) for the whole run (default: the asset's own); the forest's shadow band follows it
/// (Forest.ShadowRange). One distance a run: in batchmode the instanced draws queued for one render are never
/// flushed (TreeFadeShot), so the two must not share one. Writes ForestShadow_{dist}_{where}_{time}.png.
/// Run: FORESTSHADOW_DIST=140 Unity -batchmode -projectPath . -executeMethod ForestShadowShot.Run -logFile fs.log
/// (no -nographics)
/// </summary>
public static class ForestShadowShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly (string name, float clock)[] Times = { ("day", 1.4f), ("golden", 2.15f) };
    static Camera cam;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's: Mobile
        var urp = UniversalRenderPipeline.asset;
        float kept = urp.shadowDistance;
        string env = System.Environment.GetEnvironmentVariable("FORESTSHADOW_DIST");
        if (!string.IsNullOrEmpty(env)) urp.shadowDistance = float.Parse(env, System.Globalization.CultureInfo.InvariantCulture);
        string tag = Mathf.RoundToInt(urp.shadowDistance).ToString();
        // the player's own settings and points kept and put back (Boot clears the points and upgrades); and a fresh
        // player's start, with Unity's random numbers seeded, so both runs ride the same mountain down the same line
        TestPrefs.Pin();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = game.rider; var body = game.body;
        var terrain = game.terrain; var props = game.props;
        var dayNight = game.dayNight; var fx = Get<SnowFX>("fx");
        cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var fUpd = typeof(Forest).GetMethod("Update", NP);
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        Debug.Log($"FORESTSHADOW quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} distance {urp.shadowDistance} cascades {urp.shadowCascadeCount} realistic {props.IsRealistic}");
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 1f / 60f;
        const float dt = 1f / 60f;
        float[] at = { 1.5f, 8f, 16f };
        string[] where = { "start", "run1", "run2" };
        int next = 0;
        for (float t = 0f; next < at.Length; t += dt)
        {
            for (int k = 0; k < 2; k++) { rider.Tick(dt / 2f); body.PhysicsStep(); Physics.Simulate(dt / 2f); }
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            body.Apply(dt);
            late.Invoke(chase, null);
            aLate.Invoke(atmo, null);
            if (t < at[next]) continue;
            if (next == 0) for (int i = 0; i < 3; i++) Shoot("ForestShadow_warmup.png");   // placeholder materials, then the real ones
            foreach (var (name, clock) in Times)
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                fx.Sprinkle(400);
                Shoot($"ForestShadow_{tag}_{where[next]}_{name}.png");
            }
            Debug.Log($"FORESTSHADOW {where[next]} t={t:0.0} rider={rider.transform.position} cam={cam.transform.position}");
            next++;
        }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        // back to day, the asset's own distance and the editor's quality level before quitting (all saved on exit)
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        urp.shadowDistance = kept;
        QualitySettings.SetQualityLevel(quality, false);
        File.Delete("ForestShadow_warmup.png");
        TestPrefs.Restore();
        TestHarness.Exit(0);
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
