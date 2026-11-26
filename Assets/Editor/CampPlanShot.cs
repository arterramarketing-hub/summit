using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// The camp's layout audit (camplayout): a straight-down plan of the camp and its approaches (an orthographic render,
/// 20 px a metre) with a dump of where everything is (plan.json: the stations, the walker's blocks, the people's
/// places, the lift's top, the paths, the piles, the lamps, every named piece's footprint), and eye-level views of the
/// player's loop: arriving, the lift's top, leaving the lodge, the fire, the gate, the lookout.
/// Batch (with the GPU): -executeMethod CampPlanShot.Run  (writes to $PLAN_SHOTS)
/// </summary>
public static class CampPlanShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Camp camp; static MetaUi meta; static RiderBody body; static Rider rider;
    static SlopeStreamer terrain; static Forest props; static Camera cam; static Atmosphere atmo; static DayNight dayNight;
    static MethodInfo tUpd, fUpd, aLate;
    static string dir;
    const float dt = 1f / 60f;
    // the plan's extent: dx from the run's centre at the lip, and z
    const float X0 = -85f, X1 = 40f, Z0 = -125f, Z1 = 12f, PPM = 20f;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        dir = System.Environment.GetEnvironmentVariable("PLAN_SHOTS");
        if (string.IsNullOrEmpty(dir)) dir = ".";
        Directory.CreateDirectory(dir);
        ShaderUtil.allowAsyncCompilation = false;   // (this session only: not the project setting)
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        try { Shots(); }
        catch (System.Exception e) { Debug.LogError("CAMPPLAN " + e); }
        finally
        {
            ChaseCamera.TestDt = 0f; TimeOfDay.TestClock = -1f;
            Progress.ResetAll(); PlayerPrefs.DeleteKey("summit_missions");
            TestPrefs.Restore();
        }
        TestHarness.Exit(0);
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
        Frames(30);   // (the camp's people settle into their places)
        // the lift's ends round the camp
        LiftEnds.Instance?.Step(dt, Slope.Centre(Slope.PadLip) * Vector3.right + new Vector3(0f, 0f, -60f));

        // (shaders compiled before anything is kept: the editor draws a cyan stand-in while one compiles)
        for (int i = 0; i < 4; i++) { Shoot("warmup.png", 400, 300, false); Frames(2); }
        Plan("warmup.png");
        camp.TeleportTo(Camp.GroundAt(-1.5f, -64f), 0f); Frames(4);
        camp.Enter(rider, body, cam); Frames(40);
        // what the player sees first: the arrival, as the game frames it (the hero by the fire, looking at the lodge)
        Shoot("eye0_arrive_hero.png", 590, 1278, false);
        Dump();
        Plan("plan_day.png");

        Vector3 G(float dx, float z, float up = 0f) => Camp.GroundAt(dx, z) + Vector3.up * up;
        // the Summit lift's top: stepping off, the path in
        LiftEnds.End top = null;
        if (LiftEnds.Instance != null) foreach (var e in LiftEnds.Instance.Ends) if (e.top && e.lift.summit) top = e;
        if (top != null)
        {
            var a = top.load + Vector3.up * 1.65f;
            var aim = top.toCamp.Count > 0 ? top.toCamp[top.toCamp.Count - 1] : G(0f, -70f);
            EyeCam(a, aim + Vector3.up * 1.2f, 62f); Shoot("eye1_lift_unload.png");
            EyeCam(a, aim + Vector3.up * 1.2f, 62f); Shoot("eye1_lift_unload_wide.png", 1280, 720);
            if (top.toCamp.Count > 2)
            {
                var m = top.toCamp[top.toCamp.Count / 2] + Vector3.up * 1.65f;
                EyeCam(m, aim + Vector3.up * 1.2f, 62f); Shoot("eye2_lift_path_mid.png", 1280, 720);
                var e1 = top.toCamp[top.toCamp.Count - 1] + Vector3.up * 1.65f;
                EyeCam(e1, G(0f, -70f, 1.0f), 66f); Shoot("eye3_lift_path_end.png", 1280, 720);
            }
        }
        // leaving the lodge: on its terrace at the door, looking out the way you go (down the steps, to the gate)
        var door = camp.ChaletPoint(4.5f, 1.65f, 9.0f);
        var outLook = camp.ChaletPoint(4.5f, 0.5f, 40f);
        EyeCam(door, outLook, 64f); Shoot("eye4_lodge_door.png", 1280, 720);
        EyeCam(camp.StairsFoot + Vector3.up * 1.65f, G(0f, Slope.GateZ, 1.5f), 64f); Shoot("eye5_lodge_steps_foot.png", 1280, 720);
        // the fire: from the benches toward the gate and toward the lodge
        EyeCam(G(0f, -73.6f, 1.2f), G(0f, Slope.GateZ, 1.0f), 66f); Shoot("eye6_fire_to_gate.png", 1280, 720);
        EyeCam(G(1.5f, -66.5f, 1.65f), camp.ChaletPoint(0f, 3f, 0f), 66f); Shoot("eye7_fire_to_lodge.png", 1280, 720);
        // walking down to the gate, and the gate itself
        EyeCam(G(-1.2f, -52f, 1.65f), G(0f, Slope.GateZ, 1.6f), 64f); Shoot("eye8_to_gate.png", 1280, 720);
        EyeCam(G(0f, Slope.GateZ - 5f, 1.65f), G(0f, Slope.PadLip + 40f, -10f), 64f); Shoot("eye9_gate_out.png", 1280, 720);
        EyeCam(G(0f, Slope.GateZ + 2f, 1.65f), G(-4f, -80f, 3f), 66f); Shoot("eye10_gate_back.png", 1280, 720);
        // the lookout, and the cliff's rail
        // (standing on its deck behind the rail: the deck is 0.45 m over the snow at its middle)
        Vector3 Ahead(Vector3 p, float yawDeg, float pitchDeg) => p + Quaternion.Euler(pitchDeg, yawDeg, 0f) * Vector3.forward * 50f;
        var lk = G(Camp.LookoutX - 0.6f, Camp.LookoutZ, 0.45f + 1.65f);
        EyeCam(lk + Vector3.up * 0.0f, Ahead(lk, 90f, 9f), 64f); Shoot("eye11_lookout.png", 1280, 720);
        EyeCam(G(8f, -66.8f, 1.65f), G(Camp.LookoutX, Camp.LookoutZ, 1.2f), 62f); Shoot("eye11b_path_to_lookout.png", 1280, 720);
        // and from the cliff's rail, out over the drop (the view a deck there would have)
        var rl = G(22.6f, -67f, 1.65f);
        EyeCam(rl, Ahead(rl, 90f, 12f), 70f); Shoot("eye13_cliff_rail_out.png", 1280, 720);
        EyeCam(rl, Ahead(rl, 45f, 10f), 70f); Shoot("eye13b_cliff_rail_downhill.png", 1280, 720);
        // a high view from the front over the gate, and one from behind the lodge down the mountain
        EyeCam(G(-6f, -20f, 28f), G(-8f, -72f, 0f), 60f); Shoot("over1_front.png", 1600, 1000);
        EyeCam(G(-40f, -105f, 30f), G(10f, -45f, 0f), 60f); Shoot("over2_back_left.png", 1600, 1000);
        EyeCam(G(-62f, -40f, 26f), G(-8f, -72f, 0f), 60f); Shoot("over3_lift_side.png", 1600, 1000);
        // the walk from the closet (upstairs in the wing) out: the stairs' foot outside, toward the gate
        EyeCam(G(-12f, -74f, 1.65f), G(0f, Slope.GateZ, 1.5f), 66f); Shoot("eye12_wing_corner_to_gate.png", 1280, 720);
        // the plan at night too (the lamps, the lit path)
        SetTime(3.4f);
        Plan("plan_night.png");
    }

    // ---------------------------------------------------------------- the plan

    static void Plan(string file)
    {
        float cx = Slope.Centre(Slope.PadLip);
        float midX = cx + (X0 + X1) * 0.5f, midZ = (Z0 + Z1) * 0.5f;
        int w = Mathf.RoundToInt((X1 - X0) * PPM), h = Mathf.RoundToInt((Z1 - Z0) * PPM);
        float y = Slope.DrawnHeight(cx, -70f) + 220f;
        // the hero out of the way: off the plan, behind the camp
        camp.TeleportTo(Camp.GroundAt(0f, -64f), 0f);
        // the mist over the drops covers the cliff's edge from above: not in the plan
        var mist = new List<GameObject>();
        foreach (var t in camp.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("Mist") && t.gameObject.activeSelf) { t.gameObject.SetActive(false); mist.Add(t.gameObject); }
        // (the lift's ropes keep a few pixels' thickness by their distance in perspective: from straight above, orthographic, they swell into a black band)
        var ropes = new List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (r.enabled && r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.Contains("Rope")) { r.enabled = false; ropes.Add(r); }
        bool fog = RenderSettings.fog; RenderSettings.fog = false;
        float sd = QualitySettings.shadowDistance; QualitySettings.shadowDistance = 600f;
        var keep = (cam.orthographic, cam.orthographicSize, cam.nearClipPlane, cam.farClipPlane);
        cam.transform.SetPositionAndRotation(new Vector3(midX, y, midZ), Quaternion.Euler(90f, 0f, 0f));
        cam.orthographic = true; cam.orthographicSize = (Z1 - Z0) * 0.5f; cam.nearClipPlane = 1f; cam.farClipPlane = 600f;
        for (int i = 0; i < 6; i++) { tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); }
        noUi = true;
        Shoot(file, w, h, true);
        cam.orthographic = keep.Item1; cam.orthographicSize = keep.Item2; cam.nearClipPlane = keep.Item3; cam.farClipPlane = keep.Item4;
        QualitySettings.shadowDistance = sd; RenderSettings.fog = fog;
        foreach (var g in mist) g.SetActive(true);
        foreach (var r in ropes) r.enabled = true;
    }

    // ---------------------------------------------------------------- the dump

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string V(Vector3 p) { float cx = Slope.Centre(Slope.PadLip); return $"[{F(p.x - cx)},{F(p.z)},{F(p.y)}]"; }
    static string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static void Dump()
    {
        float cx = Slope.Centre(Slope.PadLip);
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"\"plan\":{{\"x0\":{F(X0)},\"x1\":{F(X1)},\"z0\":{F(Z0)},\"z1\":{F(Z1)},\"ppm\":{F(PPM)}}},\n");
        sb.Append($"\"slope\":{{\"gateZ\":{F(Slope.GateZ)},\"startZ\":{F(Slope.StartZ)},\"padLip\":{F(Slope.PadLip)},\"padBack\":{F(Slope.PadBack)},\"padHalf\":{F(Slope.PadHalf)},\"summitLeft\":{F(Slope.SummitLeft)},\"summitRight\":{F(Slope.SummitRight)},\"campBack\":{F(Camp.BackZ)},\"campHalf\":{F(Camp.HalfWidth)}}},\n");
        sb.Append($"\"lodge\":{{\"o\":{V(camp.ChaletPoint(0, 0, 0))},\"yaw\":{F(camp.ChaletYaw)},\"stairsFoot\":{V(camp.StairsFoot)}");
        // its outlines (its own frame -> world)
        void Rect(string n, float x0, float z0, float x1, float z1)
        {
            sb.Append($",\"{n}\":[{V(camp.ChaletPoint(x0, 0, z0))},{V(camp.ChaletPoint(x1, 0, z0))},{V(camp.ChaletPoint(x1, 0, z1))},{V(camp.ChaletPoint(x0, 0, z1))}]");
        }
        Rect("hall", -11f, -7f, 11f, 7f); Rect("wing", -16f, -5.5f, -11f, 5f); Rect("all", -17.6f, -8.4f, 19.4f, 12.8f);
        sb.Append($",\"door\":{V(camp.ChaletPoint(4.5f, 0f, 6.2f))}}},\n");
        // the stations
        sb.Append("\"spots\":[");
        var spots = (System.Collections.IList)typeof(Camp).GetField("spots", NP).GetValue(camp);
        bool first = true;
        foreach (var s in spots)
        {
            var ty = s.GetType();
            var st = ty.GetField("st").GetValue(s); var at = (Vector3)ty.GetField("at").GetValue(s); var r = (float)ty.GetField("radius").GetValue(s);
            sb.Append(first ? "" : ",").Append($"\n {{\"st\":{S(st.ToString())},\"at\":{V(at)},\"r\":{F(r)}}}"); first = false;
        }
        sb.Append("],\n");
        // the walker's blocks
        sb.Append("\"boxes\":[");
        var boxes = (System.Collections.IList)typeof(Camp).GetField("boxes", NP).GetValue(camp);
        first = true;
        foreach (var b in boxes)
        {
            var ty = b.GetType();
            var c = (Vector2)ty.GetField("c").GetValue(b); var hh = (Vector2)ty.GetField("h").GetValue(b);
            float cs = (float)ty.GetField("cos").GetValue(b), sn = (float)ty.GetField("sin").GetValue(b);
            var owner = (GameObject)ty.GetField("owner").GetValue(b); bool floor = (bool)ty.GetField("floor").GetValue(b);
            sb.Append(first ? "" : ",").Append($"\n {{\"c\":[{F(c.x - cx)},{F(c.y)}],\"h\":[{F(hh.x)},{F(hh.y)}],\"cos\":{F(cs)},\"sin\":{F(sn)},\"owner\":{S(owner != null ? owner.name : "")},\"floor\":{(floor ? "true" : "false")}}}");
            first = false;
        }
        sb.Append("],\n");
        // the camp's people: their places, the circles they keep out of, where they are now
        var life = CampLife.Current;
        sb.Append("\"points\":[");
        first = true;
        if (life != null)
            foreach (var p in life.Points) { sb.Append(first ? "" : ",").Append($"\n {{\"name\":{S(p.name)},\"at\":{V(p.at)},\"yaw\":{F(p.yaw)}}}"); first = false; }
        sb.Append("],\n\"keep\":[");
        first = true;
        if (life != null)
        {
            var keep = (System.Collections.IList)typeof(CampLife).GetField("keep", NP).GetValue(life);
            foreach (var k in keep)
            {
                var ty = k.GetType();
                sb.Append(first ? "" : ",").Append($"\n {{\"name\":{S((string)ty.GetField("name").GetValue(k))},\"c\":{V((Vector3)ty.GetField("c").GetValue(k))},\"r\":{F((float)ty.GetField("r").GetValue(k))},\"walk\":{(((bool)ty.GetField("walk").GetValue(k)) ? "true" : "false")}}}");
                first = false;
            }
        }
        sb.Append("],\n\"agents\":[");
        first = true;
        if (life != null)
            foreach (var a in life.Agents) { sb.Append(first ? "" : ",").Append($"\n {{\"home\":{S(a.home)},\"doing\":{S(a.doing.ToString())},\"at\":{V(a.pos)},\"spot\":{S(a.spot != null ? a.spot.name : "")}}}"); first = false; }
        sb.Append("],\n");
        // Val and Breanna's places
        sb.Append("\"couple\":[");
        first = true;
        if (camp.Couple != null)
        {
            typeof(CampCouple).GetMethod("MakeSpots", NP)?.Invoke(camp.Couple, null);
            var cs = (System.Collections.IDictionary)typeof(CampCouple).GetField("spots", NP).GetValue(camp.Couple);
            if (cs != null)
                foreach (System.Collections.DictionaryEntry kv in cs)
                {
                    var at = (Vector3)kv.Value.GetType().GetField("at").GetValue(kv.Value);
                    sb.Append(first ? "" : ",").Append($"\n {{\"name\":{S((string)kv.Key)},\"at\":{V(at)}}}"); first = false;
                }
        }
        sb.Append("],\n");
        // the lift ends near the camp
        sb.Append("\"liftEnds\":[");
        first = true;
        if (LiftEnds.Instance != null)
            foreach (var e in LiftEnds.Instance.Ends)
            {
                if (Mathf.Abs(e.st.x - cx) > 200f || e.st.z < -200f || e.st.z > 150f) continue;
                sb.Append(first ? "" : ",").Append($"\n {{\"summit\":{(e.lift.summit ? "true" : "false")},\"top\":{(e.top ? "true" : "false")},\"st\":{V(e.st)},\"dir\":[{F(e.dir.x)},{F(e.dir.z)}],\"side\":[{F(e.side.x)},{F(e.side.z)}],\"load\":{V(e.load)}");
                sb.Append(",\"toCamp\":["); for (int i = 0; i < e.toCamp.Count; i++) sb.Append(i > 0 ? "," : "").Append(V(e.toCamp[i])); sb.Append("]");
                sb.Append(",\"strap\":["); for (int i = 0; i < e.strap.Count; i++) sb.Append(i > 0 ? "," : "").Append(V(e.strap[i])); sb.Append("]");
                sb.Append($",\"hutDoor\":{V(e.hutDoor)},\"rack\":{V(e.rack)},\"sit\":{V(e.sit)},\"point\":{V(e.point)}");
                sb.Append(",\"people\":["); for (int i = 0; i < e.people.Count; i++) sb.Append(i > 0 ? "," : "").Append(V(e.people[i].pos)); sb.Append("]}");
                first = false;
            }
        sb.Append("],\n\"lifts\":[");
        first = true;
        foreach (var l in LiftLine.Lifts)
        {
            if (Mathf.Abs(l.top.x - cx) > 250f || l.top.z > 300f) continue;
            sb.Append(first ? "" : ",").Append($"\n {{\"id\":{l.id},\"summit\":{(l.summit ? "true" : "false")},\"top\":{V(l.top)},\"bottom\":{V(l.bottom)},\"foot\":[");
            for (int i = 0; i < l.foot.Length; i++) sb.Append(i > 0 ? "," : "").Append(V(l.foot[i]));
            sb.Append("]}"); first = false;
        }
        sb.Append("],\n");
        // the piles of snow (their footprints, coarse)
        sb.Append("\"piles\":[");
        first = true;
        foreach (var p in camp.Piles)
        {
            float mx0 = 1e9f, mx1 = -1e9f, mz0 = 1e9f, mz1 = -1e9f; int n = 0;
            for (int j = 0; j < p.nz; j += 2) for (int i = 0; i < p.nx; i += 2) { var q = p.World(i, j); if (float.IsNaN(q.y)) continue; n++; mx0 = Mathf.Min(mx0, q.x); mx1 = Mathf.Max(mx1, q.x); mz0 = Mathf.Min(mz0, q.z); mz1 = Mathf.Max(mz1, q.z); }
            if (n == 0) continue;
            sb.Append(first ? "" : ",").Append($"\n {{\"name\":{S(p.name)},\"x0\":{F(mx0 - cx)},\"x1\":{F(mx1 - cx)},\"z0\":{F(mz0)},\"z1\":{F(mz1)},\"tall\":{F(p.tallest)},\"mid\":{V(p.middle)}}}");
            first = false;
        }
        sb.Append("],\n");
        // the lamps
        sb.Append("\"lamps\":[");
        first = true;
        foreach (var lt in camp.GetComponentsInChildren<Light>(true)) { sb.Append(first ? "" : ",").Append(V(lt.transform.position)); first = false; }
        sb.Append("],\n");
        // every named piece under the camp's root (two levels), with its footprint
        sb.Append("\"pieces\":[");
        first = true;
        var root = camp.transform.Find("Camp");
        void Piece(Transform t, int depth)
        {
            var rs = t.GetComponentsInChildren<Renderer>(false);
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.Append(first ? "" : ",").Append($"\n {{\"name\":{S(t.name)},\"depth\":{depth},\"pos\":{V(t.position)},\"x0\":{F(b.min.x - cx)},\"x1\":{F(b.max.x - cx)},\"z0\":{F(b.min.z)},\"z1\":{F(b.max.z)},\"y1\":{F(b.max.y)},\"n\":{rs.Length}}}");
                first = false;
            }
            if (depth < 2) foreach (Transform c in t) Piece(c, depth + 1);
        }
        if (root != null) foreach (Transform c in root) Piece(c, 1);
        // and the people's own things (CampLife's), and the hero
        if (life != null) foreach (Transform c in life.transform) if (c.name == "CampLife") foreach (Transform d in c) Piece(d, 1);
        sb.Append("],\n");
        sb.Append($"\"hero\":{V(rider.transform.position)},\n");
        sb.Append($"\"heroYaw\":{F(rider.transform.eulerAngles.y)}\n");
        sb.Append("}\n");
        File.WriteAllText(Path.Combine(dir, "plan.json"), sb.ToString());
        Debug.Log("CAMPPLAN wrote plan.json");
    }

    // ---------------------------------------------------------------- cameras and shots (as LodgeShot)

    static void EyeCam(Vector3 p, Vector3 q, float vfov)
    {
        camp.TeleportTo(camp.ChaletPoint(0f, 0f, -11f) + Vector3.up * 50f, 0f);
        Frames(4);
        noUi = true;
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

    static bool noUi;
    static MethodInfo liftUpd;

    static void Shoot(string file, int w = 590, int h = 1278, bool still = true)
    {
        if (!still) noUi = false;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        CampLife.Current?.Step(1e-4f, cam.transform.position);
        if (LodgeFolk.Current != null) for (int k = 0; k < 3; k++) LodgeFolk.Current.Step(1e-4f);
        // the lifts draw themselves each frame (instanced): once, for this picture
        var lifts = Object.FindObjectsByType<SkiLift>(FindObjectsSortMode.None);
        if (liftUpd == null) liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        if (liftUpd != null) foreach (var l in lifts) liftUpd.Invoke(l, null);
        Canvas.ForceUpdateCanvases();
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        int mask = cam.cullingMask; cam.cullingMask |= 1 << meta.gameObject.layer;
        var canvases = noUi ? meta.GetComponentsInChildren<Canvas>() : new Canvas[0];
        foreach (var c in canvases) c.enabled = false;
        noUi = false;
        var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in skins) { if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue; var m = new Mesh(); sk.BakeMesh(m); var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer; g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; off.Add(sk); baked.Add(g); }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in off) sk.enabled = true;
        cam.cullingMask = mask;
        foreach (var c in canvases) c.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex); rt.Release();
        Debug.Log($"CAMPPLAN {file} cam={cam.transform.position}");
    }
}
