using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The summit camp on its peak: walking up to the cliff's cable railing, looking over the drop, the flags by day
/// and night, and the camp from a few angles (from the gate, from above, out from the summit), at the phone's
/// quality level. Batch: -executeMethod SummitShot.Run  (writes Summit_*.png in the project folder)
/// </summary>
public static class SummitShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static Camera cam;
    static Camp camp; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static MethodInfo tUpd, fUpd;
    const float dt = 1f / 60f;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game, false);   // the game opens in the camp
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        body = Get<RiderBody>("body"); camp = Get<Camp>("camp"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        var dayNight = Get<DayNight>("dayNight");
        var slideView = Get<AvalancheView>("slideView");
        cam = Camera.main;
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        ChaseCamera.TestDt = dt;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(30f, true, true);
        Debug.Log($"SUMMIT avalanche drawn in the camp: {(slideView != null && slideView.gameObject.activeInHierarchy)}");
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        Settle(40);
        for (int i = 0; i < 3; i++) Shoot("Summit_warmup.png");
        var c0 = camp.ChaletPoint(0f, 0f, 0f);
        float cx = Slope.Centre(Slope.PadLip);
        Vector3 G(float dx, float z) => new Vector3(cx + dx, Slope.DrawnHeight(cx + dx, z), z);
        Debug.Log($"SUMMIT heights: start pad {Slope.DrawnHeight(cx, Slope.StartZ):0.0}, gate {G(0f, -76f).y:0.0}, camp {G(0f, -100f).y:0.0}, edge {G(25f, -100f).y:0.0}, below the cliff {G(40f, -100f).y:0.0}, valley {G(150f, -100f).y:0.0}, left slope {G(-60f, -100f).y:0.0}, behind {G(0f, -180f).y:0.0}");

        // 1: walking up to the railing
        camp.Teleport(14f, -99f, 90f);
        Walk(new Vector2(0f, 0.8f), 2.2f); Settle(20);
        Shoot("Summit1_to_rail.png");
        // 2: looking over the edge and down the drop
        var at = G(24f, -118f) + Vector3.up * 2.1f;
        Aim(at, at + new Vector3(0.6f, -1f, 0.35f), 72f);
        Shoot("Summit2_over_edge.png");
        // 3: out from the summit, across the drop to the peaks
        Aim(at, at + new Vector3(1f, -0.16f, 0.2f), 62f);
        Shoot("Summit3_out.png");
        // 4, 5: the flags, day and night
        var flagsFrom = G(12f, -104f) + Vector3.up * 1.6f;
        Aim(flagsFrom, G(22.7f, -97.5f) + Vector3.up * 4.5f, 58f);
        Shoot("Summit4_flags_day.png");
        for (int i = 0; i < 9; i++) { typeof(Camp).GetMethod("LateUpdate", NP).Invoke(camp, null); }   // the cloth moves on
        Aim(flagsFrom, G(22.7f, -97.5f) + Vector3.up * 4.5f, 58f);
        Shoot("Summit4b_flags_day_later.png");
        TimeOfDay.TestClock = 3.4f; dayNight.ApplyNow();
        Shoot("Summit5_flags_night.png");
        camp.Teleport(14f, -99f, 90f); Walk(new Vector2(0f, 0.8f), 2.2f); Settle(20);
        Shoot("Summit5b_rail_night.png");
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        // 6: up to the camp from the gate; 7: the whole summit from above and off to the side
        camp.Teleport(0f, -86f, 180f); Settle(40);
        Shoot("Summit6_from_gate.png");
        var over = G(0f, -100f) + new Vector3(-150f, 110f, 120f);
        Aim(over, G(5f, -105f), 55f);
        Shoot("Summit7_from_above.png");
        // 9: the start pad and run ahead, with the camp behind (nothing of the avalanche)
        var back = G(0f, -60f) + new Vector3(18f, 22f, 40f);
        Aim(back, G(0f, -100f), 60f);
        Shoot("Summit9_camp_from_the_run.png");

        ChaseCamera.TestDt = 0f;
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        QualitySettings.SetQualityLevel(quality, false);
        EditorApplication.Exit(0);
    }

    static void Settle(int n) { for (int i = 0; i < n; i++) Step(Vector2.zero); }
    static void Walk(Vector2 s, float secs) { for (float t = 0f; t < secs; t += dt) Step(s); }
    static void Step(Vector2 s)
    {
        camp.Walk(s, dt); body.PhysicsStep(); body.Apply(dt); camp.CameraStep(dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        typeof(Camp).GetMethod("LateUpdate", NP).Invoke(camp, null);
    }

    static void Aim(Vector3 from, Vector3 to, float fov)
    {
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from, Vector3.up));
        cam.fieldOfView = fov;
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
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
