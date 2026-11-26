using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The camp's HUD with the fixed walk stick (needs the GPU: never with -nographics), through the camp's own frame
/// (Camp.Step, fed made-up fingers): standing about (the stick at rest, the controls hint beside it), walking (a thumb
/// on the stick pushed up and right: the rim gold), and at the gate (the DROP IN button beside the stick).
///   Unity -batchmode -projectPath . -executeMethod CampStickShot.Run -logFile stick.log   (CAMPSTICK_OUT=dir)
/// Writes CampStick_rest.png, CampStick_walk.png, CampStick_gate.png and CampStick_sheet.png (the three side by side).
/// </summary>
public static class CampStickShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float dt = 1f / 60f;
    const int W = 590, H = 1278;
    static SummitGame game; static Camp camp; static MetaUi meta; static RiderBody body;
    static SlopeStreamer terrain; static Forest props; static Camera cam; static Atmosphere atmo;
    static MethodInfo tUpd, fUpd, aLate;
    static readonly List<CampInput.Touch> fingers = new List<CampInput.Touch>();

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("CAMPSTICK_OUT") ?? "campstick-shots";
        Directory.CreateDirectory(outDir);
        try
        {
            SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
            EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
            TestPrefs.Pin();
            game = Object.FindFirstObjectByType<SummitGame>();
            typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
            T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
            camp = Get<Camp>("camp"); meta = Get<MetaUi>("meta"); body = Get<RiderBody>("body");
            terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
            var hud = Get<Hud>("hud");
            cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>();
            tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
            meta.Refresh();
            // overlay canvases don't draw into a render texture: hand them the camera
            meta.UseCamera(cam);
            var hc = hud.GetComponent<Canvas>(); hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
            hud.SetRun(0f, 0, 1f, 120f, 160f, false); hud.SetPower(0f, 0, false); hud.SetFalls(3, 3, false); hud.SetAltitude(0f, false); hud.SetMedical(0f, false);
            var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
            typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
            ChaseCamera.TestDt = dt;
            Camp.TestTouches = fingers;
            // (the render texture's size from the start, so the canvas lays out for it: the stick where the shot has it)
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt; cam.aspect = W / (float)H;
            for (int i = 0; i < 3; i++) Shoot(rt, null);   // (the first frames: placeholder materials, the grading LUT)

            var stairs = (Vector3)typeof(Camp).GetField("stairsFoot", NP).GetValue(camp);
            float cx = Slope.Centre(Slope.PadLip);
            var gate = new Vector3(cx, 0f, Slope.GateZ - 2.6f);
            float toGate = Mathf.Atan2(gate.x - stairs.x, gate.z - stairs.z) * Mathf.Rad2Deg;
            // 1. standing about: the stick at rest, the hint beside it
            camp.TeleportTo(stairs + (gate - stairs).normalized * 4f, toGate);
            Frames(60, null);
            Shoot(rt, null); Frames(2, null);   // (the first real frame can still come out with a placeholder suit)
            var rest = Shoot(rt, $"{outDir}/CampStick_rest.png");
            {
                var cv = meta.GetComponent<Canvas>();
                var sh = (RectTransform)typeof(MetaUi).GetField("stickHome", NP).GetValue(meta);
                var ring = (RectTransform)typeof(MetaUi).GetField("stickRing", NP).GetValue(meta);
                Debug.Log($"CAMPSTICK layout: screen {Screen.width}x{Screen.height} safe {Screen.safeArea} cam {cam.pixelRect} canvas {((RectTransform)meta.transform).rect} sf {cv.scaleFactor} mode {cv.renderMode}; home rt world {sh.position} screen {RectTransformUtility.WorldToScreenPoint(cam, sh.position)}; ring world {ring.position} screen {RectTransformUtility.WorldToScreenPoint(cam, ring.position)}; Controls.Home {camp.Controls.Home}");
            }
            // 2. a thumb on the stick, pushed up and to the right: walking, the rim gold
            var home = camp.Controls.Home ?? Vector2.zero; float R = camp.Controls.Radius;
            var push = new Vector2(0.45f, 0.85f).normalized * R * 0.9f;
            Frames(70, t => home + push * Mathf.Min(1f, t / 0.25f));
            var walk = Shoot(rt, $"{outDir}/CampStick_walk.png");
            Debug.Log($"CAMPSTICK walking: move {camp.Controls.Move} shown {camp.Controls.MoveShown}");
            Frames(30, null);
            // 3. at the gate: DROP IN beside the stick
            camp.TeleportTo(gate, toGate);
            Frames(40, null);
            var atGate = Shoot(rt, $"{outDir}/CampStick_gate.png");
            Debug.Log($"CAMPSTICK at the gate: near {camp.Near}");
            var sheet = new Texture2D(W * 3, H, TextureFormat.RGB24, false);
            sheet.SetPixels(0, 0, W, H, rest.GetPixels()); sheet.SetPixels(W, 0, W, H, walk.GetPixels()); sheet.SetPixels(2 * W, 0, W, H, atGate.GetPixels());
            sheet.Apply();
            File.WriteAllBytes($"{outDir}/CampStick_sheet.png", sheet.EncodeToPNG());
            cam.targetTexture = null;
            Debug.Log($"CAMPSTICK wrote {outDir}/CampStick_sheet.png; the stick at {home} (radius {R:0} px) on a {W}x{H} shot");
        }
        catch (System.Exception e) { Debug.LogError("CAMPSTICK exception " + e); }
        Camp.TestTouches = null;
        ChaseCamera.TestDt = 0f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>n camp frames, a finger at thumb(t) each (null: none down).</summary>
    static void Frames(int n, System.Func<float, Vector2> thumb)
    {
        for (int i = 0; i < n; i++)
        {
            fingers.Clear();
            if (thumb != null) fingers.Add(new CampInput.Touch(1, thumb(i * dt)));
            camp.Step(dt, meta);
            body.PhysicsStep();
            body.Apply(dt);
            camp.CameraStep(dt);
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            aLate.Invoke(atmo, null);
        }
    }

    static Texture2D Shoot(RenderTexture rt, string file)
    {
        Canvas.ForceUpdateCanvases();
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        int mask = cam.cullingMask; cam.cullingMask |= 1 << meta.gameObject.layer;
        foreach (var sk in skins) { var m = new Mesh(); sk.BakeMesh(m); var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer; g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; baked.Add(g); }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        cam.cullingMask = mask;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = null;
        if (file != null) { File.WriteAllBytes(file, tex.EncodeToPNG()); Debug.Log($"CAMPSTICK wrote {file}"); }
        return tex;
    }
}
