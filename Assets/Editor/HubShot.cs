using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;   // not UnityEditor.Progress

/// <summary>
/// Renders the summit camp (the hub) as the game builds it: standing about, walking, each station with
/// its menu, the lookout, the gate, then a run's mission toast and the end screen's points.
/// Batch: -executeMethod HubShot.Run  (writes HubShot*.png in the project folder)
/// </summary>
public static class HubShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Camp camp; static MetaUi meta; static RiderBody body; static Rider rider;
    static SlopeStreamer terrain; static Forest props; static Camera cam; static Atmosphere atmo;
    static MethodInfo tUpd, fUpd, aLate;
    const float dt = 1f / 60f;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        // a camp with something in it: points to spend, the decorations bought, some missions under way
        Progress.ResetAll();
        PlayerPrefs.DeleteKey("summit_missions");
        Progress.Grant(1840, Progress.Up.FirePit, Progress.Up.Flags, Progress.Up.Lights, Progress.Up.Snowman, Progress.Up.Grip);
        Progress.PickJacket(1);
        game = Object.FindFirstObjectByType<SummitGame>();
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        camp = Get<Camp>("camp"); meta = Get<MetaUi>("meta"); body = Get<RiderBody>("body"); rider = Get<Rider>("rider");
        terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        var hud = Get<Hud>("hud");
        cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        Missions.SetProgress(0, Missions.Today[0].target * 0.5f);
        Missions.ForceComplete(2);
        meta.Refresh(); camp.Redress();
        Debug.Log("HUBSHOT materials: " + body.MaterialNames());
        Debug.Log("HUBSHOT missions: " + string.Join(" / ", Missions.Today.ConvertAll(m => m.title + " +" + m.reward)));
        // overlay canvases don't draw into a render texture: hand them the camera
        meta.UseCamera(cam);
        var hc = hud.GetComponent<Canvas>(); hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
        ChaseCamera.TestDt = dt;
        // what SummitGame.Update does every frame outside a run: the riding widgets hidden
        hud.SetRun(0f, 0, 1f, 120f, 160f, false); hud.SetPower(0f, 0, false); hud.SetFalls(3, 3, false); hud.SetAltitude(0f, false); hud.SetMedical(0f, false);
        // the barrel lens bends a camera-space canvas; the real overlay UI is flat
        var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
        if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(3f, true, true);

        // the first frames show placeholder materials, and the colour-grading LUT isn't built yet (warm lights come out red)
        for (int i = 0; i < 3; i++) Shoot("HubShot_warmup.png");
        // 0: where you arrive, by the lodge
        Frames(40);
        // the first real frame still comes out with the warm lights red (batchmode only; the next is right)
        Shoot("HubShot0_arrive.png"); Frames(2);
        Shoot("HubShot0_arrive.png");
        // 0b: up the lodge's stairs onto the deck
        camp.Teleport(-9.7f, -108.6f, 195f);
        Walk(new Vector2(0f, 1f), 1.1f);
        Shoot("HubShot0b_stairs.png");
        Walk(new Vector2(0f, 1f), 0.5f); Walk(new Vector2(-1f, 0f), 0.8f); Frames(20);
        Shoot("HubShot0c_deck.png");
        // 1: walking across camp (mid stride)
        camp.Teleport(-4f, -110f, 70f);
        Walk(new Vector2(0f, 1f), 0.9f);
        Shoot("HubShot1_walk.png");
        Walk(new Vector2(0f, 1f), 0.27f);
        Shoot("HubShot1b_walk.png");
        // 2: looking back up at the camp from the gate
        camp.Teleport(1.5f, -82f, 185f);
        Frames(40);
        Shoot("HubShot2_camp.png");
        // 3, 4: the mission board and its panel
        camp.Teleport(-7.5f, -99.5f, -95f);
        Walk(new Vector2(0f, 0.6f), 0.35f); Frames(30);
        meta.SetContext(camp.Near);
        Shoot("HubShot3_board.png");
        meta.ShowMissions(); meta.SetContext(camp.Near);
        Shoot("HubShot4_missions.png");
        meta.ClosePanel(false);
        // 5, 6: the tune-up bench and the upgrades shop
        camp.Teleport(8.8f, -104.5f, 95f);
        Walk(new Vector2(0f, 0.6f), 0.35f); Frames(30);
        meta.SetContext(camp.Near);
        Shoot("HubShot5_bench.png");
        meta.ShowShop(0); meta.SetContext(camp.Near);
        Shoot("HubShot6_upgrades.png");
        meta.Act("buy" + (int)Progress.Up.Fizz);
        Shoot("HubShot6b_bought.png");
        meta.ClosePanel(false);
        // 7: the board rack, outfits, the camera round in front
        camp.Teleport(-6.6f, -111.8f, 180f);
        Frames(10);
        meta.SetContext(camp.Near);
        meta.ShowShop(1); camp.Showcase = true; meta.SetContext(camp.Near);
        Frames(60);
        Shoot("HubShot7_outfits.png");
        camp.Showcase = false; meta.ClosePanel(false);
        // 8: the lookout
        camp.Teleport(16f, -84.5f, 0f);
        Frames(120);
        Shoot("HubShot8_lookout.png");
        // 9: the Drop In gate
        camp.Teleport(0.5f, -79.5f, 0f);
        Frames(40);
        meta.SetContext(camp.Near);
        Shoot("HubShot9_gate.png");

        // 10: through the gate, at the start pad (the camp button top left)
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        camp.SetNight(0f);   // in the game it blends back to day over a second
        var chase = cam.GetComponent<ChaseCamera>(); var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        for (int i = 0; i < 20; i++) { body.Apply(dt); late.Invoke(chase, null); }
        Shoot("HubShot10_ready.png");
        // 11: riding, a mission completes
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        for (float t = 0f; t < 6f; t += dt)
        {
            for (int k = 0; k < 2; k++) { rider.Tick(dt / 2f); body.PhysicsStep(); Physics.Simulate(dt / 2f); }
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            body.Apply(dt); late.Invoke(chase, null); aLate.Invoke(atmo, null);
        }
        Missions.ForceComplete(1);
        meta.ToastNow(Missions.Today[1]);
        Shoot("HubShot11_toast.png");
        // 12: the end of the run, with the points it earned
        typeof(SummitGame).GetField("trickPoints", NP).SetValue(game, 9200);
        typeof(SummitGame).GetMethod("ToEnd", NP).Invoke(game, new object[] { "YOU SURVIVED", false });
        hud.SetWhiteout(0f);
        Shoot("HubShot12_end.png");
        Debug.Log($"HUBSHOT done: points={Progress.Points} jacket={Progress.Jacket} fizz={Progress.Level(Progress.Up.Fizz)} grip={Rider.GripLimit:0.0}");

        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        // leave the editor's own save as it was found
        Progress.ResetAll();
        PlayerPrefs.DeleteKey("summit_missions");
        EditorApplication.Exit(0);
    }

    static void Frames(int n)
    {
        for (int i = 0; i < n; i++) Step(Vector2.zero);
    }

    static void Walk(Vector2 stick, float seconds)
    {
        for (float t = 0f; t < seconds; t += dt) Step(stick);
    }

    static void Step(Vector2 stick)
    {
        camp.Walk(stick, dt);
        body.Apply(dt);
        camp.CameraStep(dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        aLate.Invoke(atmo, null);
    }

    static void Shoot(string file)
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
        cam.targetTexture = null;
        var p = rider.transform.position;
        Debug.Log($"HUBSHOT {file} walker={p} near={camp.Near} lookout={camp.OnLookout} cam={cam.transform.position} fov={cam.fieldOfView:0}");
    }
}
