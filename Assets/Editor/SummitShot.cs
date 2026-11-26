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
    static Camp camp; static RiderBody body; static MetaUi meta; static SlopeStreamer terrain; static Forest props;
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
        body = Get<RiderBody>("body"); camp = Get<Camp>("camp"); meta = Get<MetaUi>("meta"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
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
        Debug.Log($"SUMMIT heights: start pad {Slope.DrawnHeight(cx, Slope.StartZ):0.0}, gate {G(0f, -76f).y:0.0}, camp {G(0f, -70f).y:0.0}, edge {G(25f, -70f).y:0.0}, below the cliff {G(40f, -70f).y:0.0}, valley {G(150f, -70f).y:0.0}, left slope {G(-60f, -70f).y:0.0}, behind {G(0f, -150f).y:0.0}");

        // 1: walking up to the railing
        camp.Teleport(14f, -69f, 90f);
        Walk(new Vector2(0f, 0.8f), 2.2f); Settle(20);
        Shoot("Summit1_to_rail.png");
        // 2: looking over the edge and down the drop
        var at = G(24f, -88f) + Vector3.up * 2.1f;
        Aim(at, at + new Vector3(0.6f, -1f, 0.35f), 72f);
        Shoot("Summit2_over_edge.png");
        // 3: out from the summit, across the drop to the peaks
        Aim(at, at + new Vector3(1f, -0.16f, 0.2f), 62f);
        Shoot("Summit3_out.png");
        // 4, 5: the flags, day and night
        var flagsFrom = G(12f, -74f) + Vector3.up * 1.6f;
        Aim(flagsFrom, G(22.7f, -67.5f) + Vector3.up * 4.5f, 58f);
        Shoot("Summit4_flags_day.png");
        for (int i = 0; i < 9; i++) { typeof(Camp).GetMethod("LateUpdate", NP).Invoke(camp, null); }   // the cloth moves on
        Aim(flagsFrom, G(22.7f, -67.5f) + Vector3.up * 4.5f, 58f);
        Shoot("Summit4b_flags_day_later.png");
        TimeOfDay.TestClock = 3.4f; dayNight.ApplyNow();
        Shoot("Summit5_flags_night.png");
        camp.Teleport(14f, -69f, 90f); Walk(new Vector2(0f, 0.8f), 2.2f); Settle(20);
        Shoot("Summit5b_rail_night.png");
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        // 6: up to the camp from the gate; 7: the whole summit from above and off to the side
        camp.Teleport(0f, -56f, 180f); Settle(40);
        Shoot("Summit6_from_gate.png");
        var over = G(0f, -70f) + new Vector3(-150f, 110f, 120f);
        Aim(over, G(5f, -75f), 55f);
        Shoot("Summit7_from_above.png");
        // 9: the start pad and run ahead, with the camp behind (nothing of the avalanche)
        var back = G(0f, -30f) + new Vector3(18f, 22f, 40f);
        Aim(back, G(0f, -70f), 60f);
        Shoot("Summit9_camp_from_the_run.png");

        // the edge giving way: past the railing's far end, walking out to the cliff
        meta.UseCamera(cam);
        var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
        if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
        Physics.simulationMode = SimulationMode.Script;
        camp.Teleport(21.8f, -117f, 90f); Settle(30);
        Shoot("Summit10_edge_before.png");
        float walked = 0f;
        while (!camp.Falling && walked < 4f) { Step(new Vector2(0f, 0.45f)); walked += dt; }
        Debug.Log($"SUMMIT edge gave way: {camp.Falling} after {walked:0.00}s at {body.transform.position}");
        float t = FallShots(new[] { 0.35f, 1.6f, 3.4f, 6.5f, 9.3f },
                            new[] { "Summit11_collapse.png", "Summit12_falling.png", "Summit13_tumbling.png", "Summit13b_far_below.png", "Summit13c_end_of_fall.png" });
        Debug.Log($"SUMMIT rescued: falling={camp.Falling} after {t:0.0}s, walker at {camp.WalkerPosition}");
        FadeIn();
        Shoot("Summit15_rescued.png");
        // a shorter one: over the start pad's lip, coming to rest on the face below
        camp.Teleport(0f, -3f, 0f); Settle(10);
        Shoot("Summit17_lip_before.png");
        walked = 0f;
        while (!camp.Falling && walked < 4f) { Step(new Vector2(0f, 0.5f)); walked += dt; }
        Debug.Log($"SUMMIT lip gave way: {camp.Falling} after {walked:0.00}s");
        FallShots(new[] { 0.6f }, new[] { "Summit18_lip_falling.png" }, "Summit19_at_rest.png");
        FadeIn();
        Physics.simulationMode = SimulationMode.FixedUpdate;
        // DROP IN, at the MT. VERTIGO banner above the start pad
        camp.Teleport(0f, -46f, 0f);
        Walk(new Vector2(0f, 0.6f), 2.6f); Settle(20);
        meta.SetContext(camp.Near);
        Debug.Log($"SUMMIT at the banner: near={camp.Near}");
        Shoot("Summit16_dropin_banner.png");
        ChaseCamera.TestDt = 0f;
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        QualitySettings.SetQualityLevel(quality, false);
        EditorApplication.Exit(0);
    }

    /// <summary>Run a fall to its rescue, shooting at the given times (and once at rest, if rest is named).</summary>
    static float FallShots(float[] times, string[] names, string rest = null)
    {
        float t = 0f; int shot = 0; bool rested = false;
        while (camp.Falling && t < 14f)
        {
            // the fall waits for a tap (ResumeTap): tap once the last picture is taken
            bool tap = shot >= times.Length && (rest == null || rested) && t > 0.2f;
            Camp.TestTouches = tap && ((int)(t / dt)) % 8 < 3 ? new System.Collections.Generic.List<CampInput.Touch> { new CampInput.Touch(91, new Vector2(300f, 600f)) } : new System.Collections.Generic.List<CampInput.Touch>();
            camp.Step(dt, meta); Fall();
            t += dt;
            if (shot < times.Length && t >= times[shot]) { Shoot(names[shot]); Debug.Log($"SUMMIT fall {t:0.0}s body={body.RagdollCentre} moving={body.RagdollMoving}"); shot++; }
            if (rest != null && !rested && t > 1.2f && !body.RagdollMoving) { rested = true; Shoot(rest); Debug.Log($"SUMMIT at rest {t:0.0}s at {body.RagdollCentre}"); }
        }
        Camp.TestTouches = null;
        Debug.Log($"SUMMIT rescued after {t:0.0}s: walker at {camp.WalkerPosition}");
        return t;
    }

    /// <summary>A frame of a fall: the physics in two steps, as the game's FixedUpdate runs it.</summary>
    static void Fall()
    {
        for (int k = 0; k < 2; k++) { body.PhysicsStep(); Physics.Simulate(dt / 2f); }
        body.Apply(dt); camp.CameraStep(dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
    }

    static void Settle(int n) { for (int i = 0; i < n; i++) Step(Vector2.zero); }
    /// <summary>The camp's own frame (as the game runs it), so the fade after a rescue lifts.</summary>
    static void FadeIn() { for (int i = 0; i < 45; i++) { camp.Step(dt, meta); Step(Vector2.zero); } }
    static void Walk(Vector2 s, float secs) { for (float t = 0f; t < secs; t += dt) Step(s); }
    static void Step(Vector2 s)
    {
        camp.Walk(s, dt); body.PhysicsStep(); if (Physics.simulationMode == SimulationMode.Script) Physics.Simulate(dt); body.Apply(dt); camp.CameraStep(dt);
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
