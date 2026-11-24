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
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        // a camp with something in it: points to spend, the decorations bought, some missions under way
        Progress.ResetAll();
        PlayerPrefs.DeleteKey("summit_missions");
        Progress.Grant(1840, Progress.Up.FirePit, Progress.Up.Flags, Progress.Up.Lights, Progress.Up.Snowman, Progress.Up.Grip);
        Progress.PickJacket(1);
        game = Object.FindFirstObjectByType<SummitGame>();
        var trees = System.Environment.GetEnvironmentVariable("SUMMIT_TREES");   // classic|real, as TestBoot
        if (!string.IsNullOrEmpty(trees)) PlayerPrefs.SetInt(Forest.TreesPref, trees == "classic" ? 0 : 1);
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
        // 0: where you arrive, looking up at the chalet (daylight: the day/night cycle owns the time of day)
        Frames(40);
        // the first real frame still comes out with the warm lights red (batchmode only; the next is right)
        Shoot("HubShot0_arrive.png"); Frames(2);
        Shoot("HubShot0_arrive.png");
        // the lens: the player's own (default WIDE) as the run has it, then the camp's gentler one
        var lensFx = (LensFX)typeof(SummitGame).GetField("lensFx", NP).GetValue(game);
        Debug.Log($"HUBSHOT saved lens level={typeof(SummitGame).GetField("lensLevel", NP).GetValue(game)} (shots use the default WIDE)");
        typeof(SummitGame).GetField("lensLevel", NP).SetValue(game, 1);
        typeof(SummitGame).GetMethod("ApplyCamera", NP).Invoke(game, null);
        float cy0 = camp.ChaletYaw;
        var lensAt = camp.ChaletPoint(-2f, 0f, 16f);
        camp.TeleportTo(lensAt, cy0 + 180f + 20f);
        Frames(30);
        var lensComp = (UnityEngine.Rendering.Universal.LensDistortion)typeof(SummitGame).GetField("lens", NP).GetValue(game);
        lensFx.Strength = 1f; Shoot("HubShotL1_lens_before.png");
        Debug.Log($"HUBSHOT lens at strength 1: intensity={lensComp.intensity.value} override={lensComp.intensity.overrideState} active={lensComp.active} post={cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing}");
        lensFx.Strength = 0.3f; Shoot("HubShotL2_lens_after.png");
        Debug.Log($"HUBSHOT lens at strength 0.3: intensity={lensComp.intensity.value}");
        camp.TeleportTo(camp.ChaletPoint(1f, 0f, 3.5f), cy0 + 200f);
        Frames(40);
        lensFx.Strength = 1f; Shoot("HubShotL3_inside_before.png");
        lensFx.Strength = 0.3f; Shoot("HubShotL4_inside_after.png");
        lensFx.Strength = 0f;   // the rest flat, so the menus (drawn by this camera here) aren't bent
        float cy = camp.ChaletYaw;
        Vector3 C(float x, float y, float z) => camp.ChaletPoint(x, y, z);
        float Toward(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
        // 0b: the chalet from across the camp
        var from = camp.ChaletPoint(9f, 0f, 22f);
        camp.TeleportTo(from, Toward(from, C(0f, 0f, 0f)));
        Frames(40);
        Shoot("HubShot0b_chalet.png");
        // (the lodge, CampLodge.cs, or the old chalet: where each view stands)
        bool lodge = Camp.UseLodge;
        // 1, 2: up the terrace steps and in through the doors
        camp.TeleportTo(lodge ? C(4.5f, 0f, 16f) : C(0f, 0f, 13f), cy + 180f);
        Walk(new Vector2(0f, 0.8f), 1.9f);
        Shoot("HubShot1_walkin.png");
        Debug.Log("HUBSHOT steps walking outside: " + camp.FootstepsState);
        Walk(new Vector2(0f, 0.8f), 1.7f); Frames(20);
        Shoot("HubShot2_inside.png");
        Debug.Log("HUBSHOT steps inside, stopped: " + camp.FootstepsState);
        // 3: the great room (the chalet's sunken lounge; the lodge's hall toward its tall glass and the wing)
        camp.TeleportTo(lodge ? C(6.5f, 0f, 2.5f) : C(-3f, 0f, 4.8f), lodge ? cy - 80f : cy + 118f);
        Frames(60);
        Shoot("HubShot3_greatroom.png");
        // 4: from the lounge back to the kitchen island under the mezzanine (the lodge: the hall, the wing beyond)
        camp.TeleportTo(lodge ? C(-6f, 0f, 3f) : C(3.6f, -0.4f, 3.4f), lodge ? cy - 70f : cy + 200f);
        Frames(60);
        Shoot("HubShot4_kitchen.png");
        // 5, 6: the mission board and its panel
        if (lodge) camp.TeleportTo(camp.StationPoint(Camp.Station.Missions), cy + 180f); else camp.TeleportTo(C(5.4f, 0f, -3f), cy + 90f);
        Frames(40);
        meta.SetContext(camp.Near);
        Shoot("HubShot5_board.png");
        meta.ShowMissions(); meta.SetContext(camp.Near);
        Shoot("HubShot6_missions.png");
        meta.ClosePanel(false);
        // 7: up the floating stairs (the lodge has none: across the hall into the wing)
        camp.TeleportTo(lodge ? C(-6f, 0f, 0.5f) : C(-7.1f, 0f, 4.4f), lodge ? cy - 90f : cy + 180f);
        Walk(new Vector2(0f, 0.8f), 2.0f);
        Shoot("HubShot7_stairs.png");
        Debug.Log("HUBSHOT steps on the stairs: " + camp.FootstepsState);
        for (int i = 0; i < 6; i++)
        {
            Walk(new Vector2(0f, 0.8f), 0.2f);
            var lp = Quaternion.Inverse(Quaternion.Euler(0f, cy, 0f)) * (rider.transform.position - C(0f, 0f, 0f));
            Debug.Log($"HUBSHOT stairs {i}: local={lp} {camp.DebugHere()}");
        }
        Debug.Log($"HUBSHOT after stairs: walker y={rider.transform.position.y - camp.ChaletPoint(0, 0, 0).y:0.00} (mezzanine 3.6)");
        // 8, 9: the tune-up bench on the mezzanine and the upgrades shop
        if (lodge) camp.TeleportTo(camp.StationPoint(Camp.Station.Upgrades), cy + 180f); else camp.TeleportTo(C(4.6f, 3.6f, -3.0f), cy + 180f);
        Frames(40);
        meta.SetContext(camp.Near);
        Shoot("HubShot8_bench.png");
        meta.ShowShop(0); meta.SetContext(camp.Near);
        meta.Act("buy" + (int)Progress.Up.Fizz);
        Shoot("HubShot9_upgrades.png");
        meta.ClosePanel(false);
        // 10: the wardrobe, from the board wall (the closet lays itself out as it opens: at the phone's shape)
        if (lodge) camp.TeleportTo(camp.StationPoint(Camp.Station.Style), cy + 180f); else camp.TeleportTo(C(-3.3f, 3.6f, -3.4f), cy);
        Frames(10);
        meta.SetContext(camp.Near);
        Debug.Log($"HUBSHOT at the board wall: near={camp.Near}");
        cam.targetTexture = new RenderTexture(590, 1278, 24); cam.aspect = 590f / 1278f;
        Canvas.ForceUpdateCanvases();
        meta.ShowShop(1);
        for (int i = 0; i < 40; i++) { meta.Stage.Pose(dt); Step(Vector2.zero); }
        Shoot("HubShot10_outfits.png");
        meta.ClosePanel(false);
        // 11: from the mezzanine over the great room and out through the glass (the lodge: from the back of the hall)
        camp.TeleportTo(lodge ? C(-2f, 0f, -3f) : C(1.5f, 3.6f, -1.3f), lodge ? cy + 10f : cy);
        Frames(60);
        Shoot("HubShot11_mezzview.png");
        // 12: running across the camp (stick pushed right out)
        camp.Teleport(-2f, -54f, 180f);
        Walk(new Vector2(0f, 1f), 1.6f);
        Shoot("HubShot12_run.png");
        Debug.Log("HUBSHOT steps running: " + camp.FootstepsState);
        Walk(new Vector2(0f, 1f), 0.17f);
        Shoot("HubShot12b_run.png");
        // footprints: walk a loop on open snow, then run; look back along them
        camp.Teleport(-4f, -58f, 90f);
        Walk(new Vector2(0f, 0.75f), 2.6f);
        Walk(new Vector2(-0.5f, 0.6f), 1.2f);
        Frames(30);
        Debug.Log($"HUBSHOT footprints walking: {camp.FootprintCount}");
        Shoot("HubShotP1_walked.png");
        PrintCam(camp.WalkerPosition, -2.5f);
        Shoot("HubShotP2_walked_close.png");
        Walk(new Vector2(0f, 1f), 1.6f); Frames(30);
        Debug.Log($"HUBSHOT footprints after running: {camp.FootprintCount}");
        Shoot("HubShotP3_ran.png");
        PrintCam(camp.WalkerPosition, -3.5f);
        Shoot("HubShotP4_ran_close.png");
        // 13: the chalet at night (as the day/night cycle would set it)
        camp.SetNight(1f);
        camp.TeleportTo(from, Toward(from, C(0f, 0f, 0f)));
        Frames(40);
        Shoot("HubShot13_night.png");
        camp.TeleportTo(lodge ? C(-4f, 0f, -2f) : C(-3f, 0f, 4.8f), lodge ? cy - 20f : cy + 118f);
        Frames(60);
        Shoot("HubShot13b_night_inside.png");
        camp.SetNight(0f);
        // 8: the lookout
        camp.Teleport(16f, -54.5f, 0f);
        Frames(120);
        Shoot("HubShot14_lookout.png");
        // 9: DROP IN, under the MT. VERTIGO banner above the start pad
        camp.Teleport(0f, -46f, 0f);
        Walk(new Vector2(0f, 0.6f), 2.6f); Frames(20);
        meta.SetContext(camp.Near);
        Debug.Log($"HUB at the banner: near={camp.Near}");
        Shoot("HubShot15_dropin.png");

        // 10: through the gate, at the start pad (the camp button top left)
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        var chase = cam.GetComponent<ChaseCamera>(); var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        for (int i = 0; i < 20; i++) { body.Apply(dt); late.Invoke(chase, null); }
        Shoot("HubShot16_ready.png");
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
        Shoot("HubShot17_toast.png");
        // 12: the end of the run, with the points it earned
        typeof(SummitGame).GetField("trickPoints", NP).SetValue(game, 9200);
        typeof(SummitGame).GetMethod("ToEnd", NP).Invoke(game, new object[] { "YOU SURVIVED", false, false });
        hud.SetWhiteout(0f);
        Shoot("HubShot18_end.png");
        Debug.Log($"HUBSHOT done: points={Progress.Points} jacket={Progress.Jacket} fizz={Progress.Level(Progress.Up.Fizz)} grip={Rider.SideGrip:0.00}");

        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        // leave the editor's own save as it was found
        Progress.ResetAll();
        PlayerPrefs.DeleteKey("summit_missions");
        EditorApplication.Exit(0);
    }

    /// <summary>The camera looking down at the snow just behind the walker (the prints), not following.</summary>
    static void PrintCam(Vector3 walker, float back)
    {
        var f = Quaternion.Euler(0f, camp.WalkerYaw, 0f) * Vector3.forward;
        var look = walker + f * back;
        var at = look - f * 1.6f + Vector3.up * 2.2f + Vector3.Cross(Vector3.up, f) * 0.8f;
        cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(look - at, Vector3.up));
        cam.fieldOfView = 55f;
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
        body.PhysicsStep();   // keeps the deformable snow round the walker (as FixedUpdate does)
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
        int mask = cam.cullingMask; cam.cullingMask |= 1 << meta.gameObject.layer;   // (the canvas draws with this camera here; the closet's studio limits what it sees)
        foreach (var sk in skins) { var m = new Mesh(); sk.BakeMesh(m); var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer; g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; baked.Add(g); }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        cam.cullingMask = mask;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        var p = rider.transform.position;
        Debug.Log($"HUBSHOT {file} walker={p} near={camp.Near} lookout={camp.OnLookout} cam={cam.transform.position} fov={cam.fieldOfView:0}");
    }
}
