using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Summit;

/// <summary>
/// The summit camp's trees and their shadows at day, golden evening and night: logs what casts and what the
/// light and pipeline allow, and renders the camp from where its trees and their shadows show.
/// Batch: -executeMethod CampShadowShot.Run  (writes CampShadow_*.png in the project folder)
/// </summary>
public static class CampShadowShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly (string name, float clock)[] Times = { ("day", 1.4f), ("golden", 2.15f), ("night", 3.4f) };
    static Camera cam;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's: Mobile, two shadow cascades
        TestBoot.Boot(game, false);   // the game opens in the camp
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var body = Get<RiderBody>("body"); var dayNight = Get<DayNight>("dayNight"); var camp = Get<Camp>("camp");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props");
        cam = Camera.main;
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); var fUpd = typeof(Forest).GetMethod("Update", NP);
        const float dt = 1f / 60f;
        ChaseCamera.TestDt = dt;
        void Settle() { for (int i = 0; i < 40; i++) { camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt); tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); } }

        // what casts, and what lets it
        int trees = 0, casting = 0;
        foreach (var r in camp.GetComponentsInChildren<Renderer>(true))
        {
            bool tree = r.name.StartsWith("CampFir") || r.transform.parent != null && r.transform.parent.name == "Birch" || r.GetComponentInParent<Transform>().name.Contains("Pine");
            if (!tree) continue;
            trees++;
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casting++;
        }
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var sun = RenderSettings.sun;
        Debug.Log($"CAMPSHADOW trees={trees} casting={casting} realistic={Forest.Realistic} urpShadowDistance={(urp != null ? urp.shadowDistance : -1)} cascades={(urp != null ? urp.shadowCascadeCount : -1)} " +
                  $"sunShadows={(sun != null ? sun.shadows.ToString() : "none")} camShadows={cam.GetUniversalAdditionalCameraData().renderShadows} quality={QualitySettings.shadows}");

        Settle();
        for (int i = 0; i < 3; i++) Shoot("CampShadow_warmup.png");
        // 1: the spruces and birches by the chalet, from the path; 2: the camp's back edge from the fire
        var views = new (string name, Vector3 at, Vector3 look)[]
        {
            ("chalet", camp.ChaletPoint(14f, 0f, 16f), camp.ChaletPoint(-4f, 0f, 2f)),
            ("back", camp.ChaletPoint(10f, 0f, 30f), camp.ChaletPoint(14f, 0f, -12f)),
        };
        foreach (var v in views)
        {
            camp.TeleportTo(v.at, Mathf.Atan2(v.look.x - v.at.x, v.look.z - v.at.z) * Mathf.Rad2Deg);
            Settle();
            foreach (var (name, clock) in Times)
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                Debug.Log($"CAMPSHADOW {v.name} {name}: sun={sun.intensity:0.00} strength={sun.shadowStrength:0.00} shadows={sun.shadows} dir={sun.transform.forward} distance={UniversalRenderPipeline.asset.shadowDistance}");
                Shoot($"CampShadow_{v.name}_{name}.png");
                if (name == "night") continue;
                // before: the cards casting for themselves, no stand-ins, the run's 140 m
                Before(camp, true);
                Shoot($"CampShadow_{v.name}_{name}_before.png");
                Before(camp, false);
            }
        }
        // from above: the whole camp's trees and their shadows
        foreach (var (name, clock) in Times)
        {
            if (name == "night") continue;
            TimeOfDay.TestClock = clock; dayNight.ApplyNow();
            var over = camp.ChaletPoint(8f, 0f, 14f);
            cam.transform.SetPositionAndRotation(over + new Vector3(0f, 34f, 22f), Quaternion.Euler(52f, 180f + camp.ChaletYaw, 0f));
            cam.fieldOfView = 60f;
            Shoot($"CampShadow_above_{name}.png");
            Before(camp, true); Shoot($"CampShadow_above_{name}_before.png"); Before(camp, false);
        }
        ChaseCamera.TestDt = 0f;
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        QualitySettings.SetQualityLevel(quality, false);   // (the editor saves the current level into ProjectSettings)
        TestHarness.Exit(0);
    }

    static float keptDistance;
    /// <summary>Put the camp's trees back as they were before the shadow stand-ins (or undo that).</summary>
    static void Before(Camp camp, bool on)
    {
        foreach (var r in camp.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.StartsWith("CampFir")) r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (r.name == "FirShadow" || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) r.enabled = !on;
        }
        var urp = UniversalRenderPipeline.asset;
        if (on) { keptDistance = urp.shadowDistance; urp.shadowDistance = 140f; } else urp.shadowDistance = keptDistance;
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
    }
}
