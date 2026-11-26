using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// The lodge's shop (lodgeshop): the outfitters by the front glass (the rail, the mannequins, the display table and the
/// till), the board shop (its counter, the board walls and tree, the helmet and goggle stand), the tuning bench, the
/// coats by the side door and the old skis up the back wall, at eye height by day.
/// Batch (with the GPU): -executeMethod LodgeShopShot.Run  (writes to $SHOP_SHOTS)
/// </summary>
public static class LodgeShopShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Camp camp; static MetaUi meta; static RiderBody body; static Rider rider;
    static SlopeStreamer terrain; static Forest props; static Camera cam; static Atmosphere atmo; static DayNight dayNight;
    static MethodInfo tUpd, fUpd, aLate;
    static string dir;
    const float dt = 1f / 60f;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        dir = System.Environment.GetEnvironmentVariable("SHOP_SHOTS");
        if (string.IsNullOrEmpty(dir)) dir = ".";
        Directory.CreateDirectory(dir);
        ShaderUtil.allowAsyncCompilation = false;
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        try { Shots(); }
        catch (System.Exception e) { Debug.LogError("SHOPSHOT " + e); }
        finally
        {
            ChaseCamera.TestDt = 0f; TimeOfDay.TestClock = -1f;
            Progress.ResetAll(); PlayerPrefs.DeleteKey("summit_missions");
            TestPrefs.Restore();
        }
        EditorApplication.Exit(0);
    }

    static void Shots()
    {
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Progress.Grant(1840, Progress.Up.FirePit, Progress.Up.Flags, Progress.Up.Lights, Progress.Up.Snowman);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        camp = Get<Camp>("camp"); meta = Get<MetaUi>("meta"); body = Get<RiderBody>("body"); rider = Get<Rider>("rider");
        terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props"); dayNight = Get<DayNight>("dayNight");
        cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        meta.UseCamera(cam);
        ChaseCamera.TestDt = dt;
        var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
        if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
        camp.Redress();
        SetTime(1.4f);
        Frames(30);
        for (int i = 0; i < 4; i++) { Shoot("warmup.png", 400, 300); Frames(2); }
        Vector3 C(float x, float y, float z) => camp.ChaletPoint(x, y, z);
        const float MZ = -6.7f;
        // the shop inside the lodge: the outfitters by the front glass, the board shop at the low end, the tuning bench
        EyeCam(C(0.6f, 1.6f, 0.6f), C(-3.4f, 1.2f, 4.6f), 60f); Shoot("shop1_outfitters.png", 1280, 720);
        EyeCam(C(-1.0f, 1.55f, 1.0f), C(0.3f, 0.95f, 3.2f), 58f); Shoot("shop2_table_till.png", 1280, 720);
        EyeCam(C(-1.5f, 1.5f, 4.95f), C(-3.6f, 1.1f, 6.15f), 58f); Shoot("shop3_mannequins_close.png", 1280, 720);
        EyeCam(C(4.2f, 1.6f, 1.2f), C(7.0f, 0.9f, -1.6f), 58f); Shoot("shop4_counter.png", 1280, 720);
        EyeCam(C(3.6f, 1.7f, -2.2f), C(6.6f, 0.95f, -6.0f), 60f); Shoot("shop5_boards_tree.png", 1280, 720);
        EyeCam(C(9.0f, 1.45f, -4.6f), C(9.0f, 0.75f, MZ), 60f); Shoot("shop6_helmets_goggles.png", 1280, 720);
        EyeCam(C(8.3f, 1.6f, 3.6f), C(9.9f, 1.0f, 6.2f), 60f); Shoot("shop7_tuning_bench.png", 1280, 720);
        EyeCam(C(8.4f, 1.5f, 2.3f), C(10.7f, 1.15f, 2.3f), 60f); Shoot("shop8_coat_rack.png", 1280, 720);
        EyeCam(C(-2.4f, 1.7f, 1.8f), C(-2.4f, 5.6f, MZ), 56f); Shoot("shop9_ski_wall.png", 1280, 720);
        EyeCam(C(4.5f, 1.65f, 5.2f), C(-1.5f, 1.4f, -0.5f), 66f); Shoot("shop10_hall_from_door.png", 1280, 720);
        EyeCam(C(10.0f, 1.65f, 2.6f), C(4.0f, 1.0f, -3.5f), 66f); Shoot("shop11_low_end.png", 1280, 720);
    }

    // ---------------------------------------------------------------- cameras and shots (as CampPlanShot)

    static void EyeCam(Vector3 p, Vector3 q, float vfov)
    {
        camp.TeleportTo(camp.ChaletPoint(0f, 0f, -11f) + Vector3.up * 50f, 0f);
        Frames(4);
        cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(q - p, Vector3.up));
        cam.fieldOfView = vfov;
        for (int i = 0; i < 3; i++) { tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); }
        LiftEnds.Instance?.Step(dt, p);
    }

    static void SetTime(float clock)
    {
        TimeOfDay.TestClock = clock;
        dayNight.ApplyNow();
        Frames(2);
    }

    static void Frames(int n) { for (int i = 0; i < n; i++) Step(Vector2.zero); }

    static void Step(Vector2 stick)
    {
        camp.Walk(stick, dt);
        body.PhysicsStep();
        body.Apply(dt);
        camp.CameraStep(dt);
        CampLife.Current?.Step(dt, cam.transform.position);
        LodgeFolk.Current?.Step(dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        aLate.Invoke(atmo, null);
    }

    static MethodInfo liftUpd;

    static void Shoot(string file, int w = 590, int h = 1278)
    {
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        CampLife.Current?.Step(1e-4f, cam.transform.position);
        if (LodgeFolk.Current != null) for (int k = 0; k < 3; k++) LodgeFolk.Current.Step(1e-4f);
        var lifts = Object.FindObjectsByType<SkiLift>(FindObjectsSortMode.None);
        if (liftUpd == null) liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        if (liftUpd != null) foreach (var l in lifts) liftUpd.Invoke(l, null);
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        var canvases = meta.GetComponentsInChildren<Canvas>();
        foreach (var c in canvases) c.enabled = false;
        var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in skins) { if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue; var m = new Mesh(); sk.BakeMesh(m); var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer; g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; off.Add(sk); baked.Add(g); }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in off) sk.enabled = true;
        foreach (var c in canvases) c.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex); rt.Release();
        Debug.Log($"SHOPSHOT {file} cam={cam.transform.position}");
    }
}
