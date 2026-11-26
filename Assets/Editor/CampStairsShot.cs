using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// The lodge's path and the missions board (campstairs): the path from the slope below the lodge looking up it (the
/// user's angle), from its side, from the lodge's steps down it, and the board at the gate up close; with the height
/// along the camp's paths (each half metre: the ground's grade, and where the path's own treads stand).
/// Batch (with the GPU): -executeMethod CampStairsShot.Run  (writes to $STAIRS_SHOTS)
/// </summary>
public static class CampStairsShot
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
        dir = System.Environment.GetEnvironmentVariable("STAIRS_SHOTS");
        if (string.IsNullOrEmpty(dir)) dir = ".";
        Directory.CreateDirectory(dir);
        ShaderUtil.allowAsyncCompilation = false;
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        try { Shots(); }
        catch (System.Exception e) { Debug.LogError("CAMPSTAIRS " + e); }
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
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(3f, true, true);
        SetTime(1.4f);
        Frames(30);
        LiftEnds.Instance?.Step(dt, Slope.Centre(Slope.PadLip) * Vector3.right + new Vector3(0f, 0f, -60f));
        for (int i = 0; i < 4; i++) { Shoot("warmup.png", 400, 300); Frames(2); }

        Profiles();

        Vector3 G(float dx, float z, float up = 0f) => Camp.GroundAt(dx, z) + Vector3.up * up;
        var foot = camp.StairsFoot;
        var deck = camp.ChaletPoint(3f, 1.2f, 9f);
        // the user's angle: on the slope below the lodge, looking up the path to its deck
        EyeCam(G(-0.6f, -47.5f, 1.65f), deck, 60f); Shoot("up1_from_gate_end.png", 1280, 720);
        EyeCam(G(-2.2f, -54f, 1.65f), deck, 62f); Shoot("up2_below_lodge.png", 1280, 720);
        EyeCam(G(-3.4f, -60.5f, 1.65f), deck, 64f); Shoot("up3_mid_path.png", 1280, 720);
        EyeCam(G(1.5f, -58f, 1.65f), deck, 60f); Shoot("up4_below_right.png", 590, 1278);
        // from its side, the profile of the climb; and from the steps down it
        EyeCam(G(9f, -63f, 1.8f), G(-5f, -65f, 1.0f), 64f); Shoot("side1_profile.png", 1280, 720);
        EyeCam(G(-16f, -60f, 2.4f), G(-4.5f, -66f, 0.5f), 64f); Shoot("side2_profile_left.png", 1280, 720);
        EyeCam(foot + Vector3.up * 1.65f, G(0f, -44f, 1.0f), 64f); Shoot("down1_from_steps.png", 1280, 720);
        // the missions board at the gate, and the gate it stands by
        Transform board = null, gate = null;
        foreach (var t in camp.GetComponentsInChildren<Transform>(true)) { if (t.name == "CorralBoard" || t.name == "MissionsBoard") board = t; if (t.name == "StartGate" || t.name == "Gate") gate = gate ?? t; }
        if (board != null)
        {
            var f = board.name == "MissionsBoard" ? board.forward : -board.forward;   // (the old one's face toward -z of its frame, the new one's +z)
            var c = board.position + Vector3.up * 1.6f;
            EyeCam(c + f * 2.6f + Vector3.up * 0.1f, c, 52f); Shoot("board1_close.png", 1280, 720);
            EyeCam(c + f * 6.5f + board.right * -2.5f + Vector3.up * 0.2f, c + Vector3.down * 0.3f, 60f); Shoot("board2_approach.png", 1280, 720);
            EyeCam(c + f * 11f + board.right * 3f + Vector3.up * 0.4f, G(0f, Slope.GateZ, 2.5f), 62f); Shoot("board3_with_gate.png", 1280, 720);
            Debug.Log($"CAMPSTAIRS board at {board.position} fwd {board.forward}");
        }
        // the stairs' joints: the top deck where the path meets it, the first landing, the foot
        if (System.Type.GetType("Summit.Camp, Assembly-CSharp")?.GetField("StairX") != null)
        {
            float sx = (float)typeof(Camp).GetField("StairX").GetValue(null), sb = (float)typeof(Camp).GetField("StairBottomZ").GetValue(null), st = (float)typeof(Camp).GetField("StairTopZ").GetValue(null);
            EyeCam(G(sx + 2.6f, st - 3.2f, 1.7f), G(sx, st + 1.5f, 0f), 60f); Shoot("joint1_top.png", 1280, 720);
            EyeCam(G(sx + 4.5f, (sb + st) * 0.5f + 1.5f, 1.2f), G(sx, (sb + st) * 0.5f, 0.5f), 60f); Shoot("joint2_landing_side.png", 1280, 720);
            EyeCam(G(sx + 1.8f, sb + 3.2f, 1.6f), G(sx - 0.6f, sb - 2f, 0.4f), 62f); Shoot("joint3_foot.png", 1280, 720);
            EyeCam(G(sx, sb + 9f, 1.65f), G(sx, st, 2.0f), 58f); Shoot("up5_from_gate_axis.png", 590, 1278);
        }
        // the missions station inside the lodge (where it was) and the lodge's door
        EyeCam(camp.ChaletPoint(6.5f, 1.6f, 3.6f), camp.ChaletPoint(2.4f, 1.6f, 3.6f), 64f); Shoot("lodge_missions_spot.png", 1280, 720);
        // the shovelled path to the lookout
        EyeCam(G(2.5f, -69f, 1.65f), G(Camp.LookoutX, Camp.LookoutZ, 1.0f), 62f); Shoot("path_lookout.png", 1280, 720);
        // dusk: the lamps on the climb
        SetTime(3.0f);
        EyeCam(G(-2.2f, -54f, 1.65f), deck, 62f); Shoot("up2_below_lodge_dusk.png", 1280, 720);
    }

    /// <summary>The ground's height along the lodge's path and the shovelled one, each half metre.</summary>
    static void Profiles()
    {
        Vector3 G(float dx, float z) => Camp.GroundAt(dx, z);
        var paths = new Dictionary<string, List<Vector3>>
        {
            ["lodge"] = new List<Vector3> { camp.StairsFoot, G(-5.6f, -71.5f), G(-4.9f, -64.5f), G(-3.4f, -57f), G(-1.2f, -50.5f), G(0f, -44f) },
            ["lookout"] = new List<Vector3> { G(1.6f, -69.4f), G(6f, -67.6f), G(11f, -66.8f), G(16f, -66.5f), G(Camp.LookoutX - 2.4f, Camp.LookoutZ) },
            ["gateToFire"] = new List<Vector3> { G(0f, -44f), G(0f, -60f), G(0f, -70f) },
        };
        var lift = LiftEnds.SummitPath();
        if (lift.Count > 1) paths["summitLiftPathIn"] = lift;   // (the lifttop branch's: looked at, not changed)
        paths["wingSteps"] = new List<Vector3> { camp.ChaletPoint(-13f, 0f, 17.5f), camp.ChaletPoint(-13f, 0f, 24f), G(-6f, -66f) };
        float cx = Slope.Centre(Slope.PadLip);
        // (after campstairs) the way a walker goes now: the path, the stairs, the path; the ramps' grades (the stairs are stairs)
        if (typeof(Camp).GetField("StairX") != null)
        {
            float sx = (float)typeof(Camp).GetField("StairX").GetValue(null), sb = (float)typeof(Camp).GetField("StairBottomZ").GetValue(null), st = (float)typeof(Camp).GetField("StairTopZ").GetValue(null);
            var route = new List<Vector3> { camp.StairsFoot, G(-5.6f, -71.5f), G(-4.4f, -67.2f), G(-2.7f, -64.4f), G(sx, st - 1.0f), G(sx, sb), G(sx, -45.6f), G(-1.6f, -43.9f), G(0f, -42.4f) };
            float along = 0f, feet = route[0].y, worstRamp = 0f, rampAt = 0f; var sbr = new System.Text.StringBuilder();
            var onStairs = typeof(Camp).GetMethod("OnStairs");
            for (int i = 0; i + 1 < route.Count; i++)
            {
                float len = Vector2.Distance(new Vector2(route[i].x, route[i].z), new Vector2(route[i + 1].x, route[i + 1].z));
                for (float d = 0f; d < len; d += 0.25f)
                {
                    var p = Vector3.Lerp(route[i], route[i + 1], d / len);
                    float y = camp.FloorHeight(new Vector3(p.x, feet + 0.3f, p.z), out _);
                    bool stairs = (bool)onStairs.Invoke(null, new object[] { p, 0.3f });
                    float g = Mathf.Abs(y - feet) / 0.25f;
                    if (!stairs && along > 0.5f && g > worstRamp) { worstRamp = g; rampAt = p.z; }
                    if (Mathf.Round(along * 4f) % 8 == 0) sbr.Append($"\n  {along:0.0}m ({p.x - cx:0.0},{p.z:0.0}) y {y:0.00}{(stairs ? " stairs" : $" grade {g * 100f:0}%")}");
                    feet = y; along += 0.25f;
                }
            }
            Debug.Log($"CAMPSTAIRS the way down now: {along:0} m; the paths' steepest {worstRamp * 100f:0}% (at z {rampAt:0.0}), the rest stairs" + sbr);
        }
        foreach (var kv in paths)
        {
            var pts = kv.Value; float along = 0f; var sb = new System.Text.StringBuilder();
            float prevY = Slope.DrawnHeight(pts[0].x, pts[0].z), maxG = 0f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                float len = Vector2.Distance(new Vector2(pts[i].x, pts[i].z), new Vector2(pts[i + 1].x, pts[i + 1].z));
                for (float s = 0f; s < len; s += 0.5f)
                {
                    var p = Vector3.Lerp(pts[i], pts[i + 1], s / len);
                    float y = Slope.DrawnHeight(p.x, p.z);
                    float g = (along > 0f) ? (y - prevY) / 0.5f : 0f;
                    maxG = Mathf.Max(maxG, Mathf.Abs(g));
                    if (Mathf.Round(along * 2f) % 2 == 0) sb.Append($"\n  {along:0.0}m ({p.x - cx:0.0},{p.z:0.0}) y {y:0.00} grade {g * 100f:0}%");
                    prevY = y; along += 0.5f;
                }
            }
            Debug.Log($"CAMPSTAIRS profile {kv.Key}: {along:0} m, steepest {maxG * 100f:0}%" + sb);
        }
        if (System.Type.GetType("Summit.Camp, Assembly-CSharp")?.GetMethod("TreadUnder") != null)
        {
            var tu = typeof(Camp).GetMethod("TreadUnder");
            float sx = (float)typeof(Camp).GetField("StairX").GetValue(null), sb = (float)typeof(Camp).GetField("StairBottomZ").GetValue(null), st = (float)typeof(Camp).GetField("StairTopZ").GetValue(null);
            float worst = -9f, worstAt = 0f, worstLat = 0f, over = 0f; int n = 0, through = 0;
            for (float z = sb + 0.02f; z > st - 0.98f; z -= 0.02f)
                for (float l = -1.15f; l <= 1.151f; l += 0.115f)
                {
                    var w = new Vector3(cx + sx + l, 0f, z);
                    float tread = (float)tu.Invoke(null, new object[] { w });
                    if (float.IsNaN(tread)) continue;
                    float snow = Slope.DrawnHeight(w.x, w.z), d = snow - tread;
                    n++; if (d > -0.03f) through++;
                    if (d > worst) { worst = d; worstAt = z; worstLat = l; }
                    over = Mathf.Max(over, -d);
                }
            Debug.Log($"CAMPSTAIRS snow vs treads: {n} points, {through} within 3 cm of a tread or through it; the snow's highest {worst:0.000} m against the tread (at z {worstAt:0.00}, {worstLat:0.00} across); the treads at most {over:0.00} m over the snow");
            // the walking floor at each tread's middle against the tread
            float ferr = 0f;
            for (int j = 0; j < 42; j++)
            {
                float r = (float)typeof(Camp).GetMethod("StairRiserAt").Invoke(null, new object[] { j });
                var w = new Vector3(cx + sx, 0f, sb - r - 0.155f);
                float tread = (float)tu.Invoke(null, new object[] { w }); w.y = tread; float f = camp.FloorHeight(w, out _);
                ferr = Mathf.Max(ferr, Mathf.Abs(f - tread));
            }
            Debug.Log($"CAMPSTAIRS the walking floor over the treads' middles: off by {ferr:0.000} m at most");
        }
        Debug.Log($"CAMPSTAIRS lodge floor {camp.ChaletPoint(0, 0, 0).y:0.00}, steps foot {camp.StairsFoot.y:0.00} at ({camp.StairsFoot.x - cx:0.0},{camp.StairsFoot.z:0.0}), steps showing {camp.LodgeStepsShowing}, drop {camp.LodgeFrontDrop:0.00}");
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
        Debug.Log($"CAMPSTAIRS {file} cam={cam.transform.position}");
    }
}
