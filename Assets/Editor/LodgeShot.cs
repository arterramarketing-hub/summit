using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;   // not UnityEditor.Progress

/// <summary>
/// Renders the camp's lodge (Camp.UseLodge, CampLodge.cs): the photo's viewpoint (front left, standing on the snow
/// below the terrace) by day, at dusk and at night, landscape like the photo; then the hero's own views walking up the
/// steps, across the terrace, in at the door and round the hall, by day and at dusk; the old chalet from the same spot
/// for comparison; and what the lodge costs to draw.
/// Batch (with the GPU, not -nographics): -executeMethod LodgeShot.Run  (writes Lodge*.png to $LODGE_SHOTS or the project folder)
/// </summary>
public static class LodgeShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Camp camp; static MetaUi meta; static RiderBody body; static Rider rider;
    static SlopeStreamer terrain; static Forest props; static Camera cam; static Atmosphere atmo; static DayNight dayNight;
    static MethodInfo tUpd, fUpd, aLate;
    static string dir;
    const float dt = 1f / 60f;
    static readonly (string name, float clock)[] Times = { ("day", 1.4f), ("dusk", 2.75f), ("night", 3.4f) };
    // the spaces inside (layout A), each seen from a person's height in it: from, to (the lodge's frame), field of view
    static readonly (string file, Vector3 from, Vector3 to, float fov)[] Rooms =
    {
        ("Lodge10_outfitters", new Vector3(-2.0f, 1.65f, 1.2f), new Vector3(-7.0f, 3.4f, -6.7f), 70f),
        ("Lodge11_lounge", new Vector3(-1.6f, 1.65f, 0.6f), new Vector3(-7.4f, 1.6f, 5.8f), 72f),
        ("Lodge12_boardshop", new Vector3(3.4f, 1.65f, 1.8f), new Vector3(8.6f, 1.2f, -4.8f), 72f),
        ("Lodge13_tuning", new Vector3(6.4f, 1.65f, 2.6f), new Vector3(10.0f, 1.0f, 6.4f), 70f),
        ("Lodge14_cafe", new Vector3(-10.2f, 1.65f, -2.6f), new Vector3(-14.8f, 1.4f, 3.6f), 74f),
        ("Lodge15_foyer", new Vector3(4.5f, 1.65f, 6.2f), new Vector3(-2.0f, 2.4f, -4.0f), 74f),
        ("Lodge16_hall_wide", new Vector3(-9.5f, 1.6f, -5.5f), new Vector3(8f, 2.2f, 4f), 74f),
        ("Lodge16b_clerestory", new Vector3(0f, 1.6f, 4.5f), new Vector3(-4f, 7.5f, -3f), 70f),
    };

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        dir = System.Environment.GetEnvironmentVariable("LODGE_SHOTS");
        if (string.IsNullOrEmpty(dir)) dir = ".";
        Directory.CreateDirectory(dir);
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        try { Shots(); }
        finally
        {
            ChaseCamera.TestDt = 0f; TimeOfDay.TestClock = -1f; Camp.UseLodge = true;
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
        Simulate();
        SetTime(1.4f);
        for (int i = 0; i < 3; i++) Shoot("Lodge_warmup.png", 400, 300);
        Debug.Log($"LODGESHOT lodge at {camp.ChaletPoint(0, 0, 0)} yaw {camp.ChaletYaw:0.0}; steps' foot {camp.StairsFoot}, front drop {camp.LodgeFrontDrop:0.00} m, {camp.LodgeStepsShowing} steps showing");
        // the three layouts inside (LODGE_FULL=1 also renders the outside, the walk in and layout A's spaces as before)
        Layouts();
        if (System.Environment.GetEnvironmentVariable("LODGE_FULL") != "1") return;
        Cost("A");

        // 1: the photo's viewpoint: on the snow in front and to the left (the low end), looking up at the whole front
        foreach (var (name, clock) in Times)
        {
            SetTime(clock);
            PhotoCam(new Vector3(11f, 0f, 29f), new Vector3(-2f, 4.0f, 0f), 46f);
            Shoot($"Lodge1_photo_{name}.png", 1200, 800);
            PhotoCam(new Vector3(9f, 0f, 27f), new Vector3(-2.5f, 4.4f, 2f), 52f);
            Shoot($"Lodge1b_photo_close_{name}.png", 1200, 800);
        }
        // 2: the hero's way in, from the fire up the steps, by day and at dusk
        foreach (var (name, clock) in new[] { Times[0], Times[1] })
        {
            SetTime(clock);
            var foot = camp.StairsFoot;
            var outFront = camp.ChaletPoint(4.5f, 0f, 30f);
            camp.TeleportTo(new Vector3(outFront.x, 0f, outFront.z), camp.ChaletYaw + 180f);
            Frames(40);
            Shoot($"Lodge2_arrive_{name}.png");
            camp.TeleportTo(foot + camp.ChaletPoint(0f, 0f, 1f) - camp.ChaletPoint(0f, 0f, 0f), camp.ChaletYaw + 180f);
            WalkTo(camp.ChaletPoint(0f, 0f, 14.2f), 0.8f);
            Shoot($"Lodge3_steps_{name}.png");
            WalkTo(camp.ChaletPoint(1.5f, 0f, 9.5f), 0.8f);
            Shoot($"Lodge4_terrace_{name}.png");
            Debug.Log($"LODGESHOT after the steps ({name}): feet {rider.transform.position.y - Rider.R - camp.ChaletPoint(0, 0, 0).y:0.00} above the floor");
            WalkTo(camp.ChaletPoint(4.5f, 0f, 8.4f), 0.8f); WalkTo(camp.ChaletPoint(4.5f, 0f, 7.6f), 0.6f); Frames(10);
            Shoot($"Lodge5_door_{name}.png");
            WalkTo(camp.ChaletPoint(4.5f, 0f, 3.5f), 0.8f); Frames(30);
            Shoot($"Lodge6_inside_{name}.png");
            // the terrace: along it toward the fire table and the tall glass, and back toward the low end's racks
            camp.TeleportTo(camp.ChaletPoint(-2.5f, 0f, 8.6f), camp.ChaletYaw - 80f);
            Frames(40);
            Shoot($"Lodge7_terrace_west_{name}.png");
            camp.TeleportTo(camp.ChaletPoint(9.5f, 0f, 8.6f), camp.ChaletYaw + 60f);
            Frames(40);
            Shoot($"Lodge8_terrace_low_{name}.png");
            camp.TeleportTo(camp.ChaletPoint(12.6f, 0f, 2.2f), camp.ChaletYaw + 180f);
            Frames(40);
            Shoot($"Lodge9_side_terrace_{name}.png");
        }
        // 10-16: inside (layout A), by day: each space from a person's height, then the hero's own views
        SetTime(1.4f);
        Vector3 C(float x, float y, float z) => camp.ChaletPoint(x, y, z);
        foreach (var (file, from, to, fov) in Rooms)
        {
            EyeCam(C(from.x, from.y, from.z), C(to.x, to.y, to.z), fov);
            Shoot(file + "_day.png");
        }
        camp.TeleportTo(C(6.5f, 0f, 2.5f), camp.ChaletYaw - 80f);
        Frames(60); Shoot("Lodge17_hero_hall.png");
        camp.TeleportTo(C(-2f, 0f, -3f), camp.ChaletYaw + 10f);
        Frames(60); Shoot("Lodge17b_hero_out.png");
        // the stations, from where you stand to use them (the upgrades: the board shop's counter and the tuning bench)
        var stationShots = new List<(Vector3 at, Camp.Station st, string file)> { (camp.StationPoint(Camp.Station.Style), Camp.Station.Style, "Lodge18_wardrobe.png"), (camp.StationPoint(Camp.Station.Missions), Camp.Station.Missions, "Lodge18c_missions.png") };
        int u = 0;
        foreach (var at in camp.StationPoints(Camp.Station.Upgrades)) stationShots.Add((at, Camp.Station.Upgrades, $"Lodge18b_upgrades{u++}.png"));
        foreach (var (at, st, file) in stationShots)
        {
            var l = Quaternion.Euler(0f, -camp.ChaletYaw, 0f) * (at - C(0, 0, 0));
            camp.TeleportTo(at, camp.ChaletYaw + (l.z > 2f ? 0f : 180f));   // facing the station (the tuning bench is on the front wall)
            Frames(40);
            meta.SetContext(camp.Near);
            Shoot(file);
            Debug.Log($"LODGESHOT at {st} ({file}): near={camp.Near}");
            meta.SetContext(Camp.Station.None);
        }
        // the closet opens from the wardrobe and comes back to the lodge
        camp.TeleportTo(camp.StationPoint(Camp.Station.Style), camp.ChaletYaw + 180f);
        Frames(10);
        meta.ShowShop(1);
        for (int i = 0; i < 30; i++) { meta.Stage.Pose(dt); Step(Vector2.zero); }
        Shoot("Lodge19_closet.png");
        Debug.Log($"LODGESHOT closet open={meta.ClosetOpen}");
        meta.ClosetBack();
        Frames(20);
        Shoot("Lodge19b_closet_back.png");
        // 20, 21: dusk and night inside: the hero looking out through the glass, the lounge and the café
        SetTime(2.75f);
        camp.TeleportTo(camp.ChaletPoint(-4f, 0f, -2f), camp.ChaletYaw - 20f);
        Frames(40); Shoot("Lodge20_inside_dusk.png");
        foreach (var (file, from, to, fov) in new[] { Rooms[1], Rooms[4] })
        {
            EyeCam(C(from.x, from.y, from.z), C(to.x, to.y, to.z), fov);
            Shoot(file + "_dusk.png");
        }
        SetTime(3.4f);
        camp.TeleportTo(camp.ChaletPoint(-4f, 0f, -2f), camp.ChaletYaw - 20f);
        Frames(40); Shoot("Lodge21_inside_night.png");
        EyeCam(C(Rooms[1].from.x, Rooms[1].from.y, Rooms[1].from.z), C(Rooms[1].to.x, Rooms[1].to.y, Rooms[1].to.z), Rooms[1].fov);
        Shoot(Rooms[1].file + "_night.png");
        camp.TeleportTo(camp.ChaletPoint(-5.0f, 0f, 8.4f), camp.ChaletYaw - 105f);
        Frames(40); Shoot("Lodge22_firetable_night.png");
        // 23: the whole camp from above at dusk: the lodge, the path and its lamps, the fire, the gate
        SetTime(2.75f);
        EyeCam(camp.ChaletPoint(14f, 32f, 46f), camp.ChaletPoint(2f, 0f, 12f), 55f);
        Shoot("Lodge23_camp_above_dusk.png", 900, 1200);
        SetTime(1.4f);
        noUi = true;
        Shoot("Lodge23_camp_above_day.png", 900, 1200);

        // the old chalet from the same viewpoints, for comparison
        Camp.UseLodge = false;
        camp.Rebuild();
        Simulate();
        foreach (var (name, clock) in new[] { Times[0], Times[1] })
        {
            SetTime(clock);
            PhotoCam(new Vector3(11f, 0f, 29f), new Vector3(-2f, 4.0f, 0f), 46f, true);
            Shoot($"Lodge0_before_{name}.png", 1200, 800);
        }
        EyeCam(camp.ChaletPoint(14f, 32f, 46f), camp.ChaletPoint(2f, 0f, 12f), 55f);
        Shoot("Lodge0_before_above_day.png", 900, 1200);
        Camp.UseLodge = true;
        camp.Rebuild();
    }

    /// <summary>
    /// Each layout in turn, as the LODGE setting shows it: from the door and from the far end at eye height, by day and
    /// at dusk; the hero coming in (the camp's buttons, the LODGE chip with them); the hero where each layout is its
    /// own (A by the hanging fire, B walking round the open one, C on the stair, under the mezzanine and up on it);
    /// the wardrobe; and what each costs to draw.
    /// </summary>
    static void Layouts()
    {
        Vector3 C(float x, float y, float z) => camp.ChaletPoint(x, y, z);
        float cy = camp.ChaletYaw;
        // (first: the editor's renders slow down over a long run of shots here, so the timing comes before them;
        // LODGE_COST_ONLY=1 stops after it)
        CostRounds();
        if (System.Environment.GetEnvironmentVariable("LODGE_COST_ONLY") == "1") return;
        foreach (int k in Chosen())
        {
            string n = Camp.LodgeLayoutKeys[k];
            PlayerPrefs.SetInt(Camp.LodgePref, k);
            camp.Rebuild();
            Simulate();
            meta.Refresh();
            Debug.Log($"LODGESHOT ===== layout {Camp.LodgeLayoutName}");
            Cost(n, false);
            foreach (var (name, clock) in new[] { Times[0], Times[1] })
            {
                SetTime(clock);
                EyeCam(C(4.0f, 1.65f, 4.4f), C(-6.5f, 2.1f, -3.4f), 88f);
                Shoot($"LodgeLayout{n}_1door_{name}.png", 720, 960);
                EyeCam(C(-10.4f, 1.65f, 2.6f), C(6.0f, 1.9f, -2.0f), 88f);
                Shoot($"LodgeLayout{n}_2far_{name}.png", 720, 960);
            }
            SetTime(1.4f);
            // the hero coming in at the door (the camp's buttons up: the LODGE chip shows the layout)
            camp.TeleportTo(C(4.5f, 0f, 9.0f), cy + 180f);
            WalkTo(C(4.5f, 0f, 3.6f), 0.8f); Frames(20);
            Shoot($"LodgeLayout{n}_3hero_in.png");
            if (k == 0)
            {
                camp.TeleportTo(C(-2.6f, 0f, 1.2f), cy - 120f); Frames(40);
                Shoot($"LodgeLayout{n}_4hero_fire.png");
            }
            else if (k == 1)
            {
                camp.TeleportTo(C(-3.0f, 0f, -2.05f), cy + 90f);
                WalkTo(C(-0.1f, 0f, -2.05f), 0.6f); WalkTo(C(0.6f, 0f, -3.2f), 0.6f); Frames(10);
                Shoot($"LodgeLayout{n}_4hero_roundfire.png");
                WalkTo(C(-0.1f, 0f, -4.35f), 0.6f); WalkTo(C(-3.0f, 0f, -4.35f), 0.6f); Frames(10);
                Shoot($"LodgeLayout{n}_5hero_behindfire.png");
                camp.TeleportTo(C(-6.0f, 0f, 0.8f), cy - 90f);
                WalkTo(C(-11.6f, 0f, 0.8f), 0.8f); Frames(10);
                Shoot($"LodgeLayout{n}_6hero_tocafe.png");
            }
            else if (k >= 3)
            {
                // B with an upstairs: the ground floor and the upstairs from eye height, then the hero on the stair
                // and up there (the camera kept clear of the floors)
                foreach (var (name, clock) in new[] { Times[0], Times[1] })
                {
                    SetTime(clock);
                    // (B2 and B2R from the same places: before and after its stair moved to the back wall)
                    var (g0, g1, u0, u1) = n == "B1" ? (C(2.0f, 1.65f, 3.6f), C(-6.0f, 3.4f, -5.0f), C(3.7f, 5.45f, -5.5f), C(-8.0f, 4.6f, -2.0f))
                                         : n == "B2" || n == "B2R" ? (C(1.4f, 1.65f, 0.8f), C(-9.0f, 3.2f, 3.6f), C(-5.8f, 5.45f, 5.3f), C(-9.6f, 4.8f, -4.6f))
                                                  : (C(3.4f, 1.65f, 0.4f), C(-4.6f, 3.0f, 5.4f), C(-3.6f, 5.45f, 1.4f), C(-10.2f, 4.9f, 5.6f));
                    EyeCam(g0, g1, 88f);
                    Shoot($"LodgeLayout{n}_8ground_{name}.png", 720, 960);
                    EyeCam(u0, u1, 88f);
                    Shoot($"LodgeLayout{n}_9upstairs_{name}.png", 720, 960);
                    if (n == "B2" || n == "B2R")
                    {
                        // the back wall from the front glass (where B2R's stair went), and the loft from its back corner
                        EyeCam(C(-0.6f, 1.65f, 5.6f), C(-2.4f, 2.4f, -6.7f), 88f);
                        Shoot($"LodgeLayout{n}_7backwall_{name}.png", 720, 960);
                        EyeCam(C(-6.0f, 5.45f, -5.9f), C(-9.0f, 4.6f, 4.0f), 88f);
                        Shoot($"LodgeLayout{n}_9loftback_{name}.png", 720, 960);
                    }
                }
                SetTime(1.4f);
                var path = n == "B1" ? new[] { (3.9f, 4.0f), (3.9f, 0.6f), (3.9f, -4.0f), (3.9f, -5.5f), (-2.0f, -5.6f) }
                         : n == "B2" ? new[] { (1.6f, 5.3f), (0.4f, 5.35f), (-2.2f, 5.35f), (-5.0f, 5.35f), (-6.6f, 3.0f) }
                         : n == "B2R" ? new[] { (2.6f, -4.6f), (0.4f, -5.9f), (-2.2f, -5.9f), (-5.0f, -5.9f), (-6.6f, -5.3f), (-7.4f, -3.6f) }
                                  : new[] { (-4.0f, 5.2f), (-3.4f, 5.2f), (-0.6f, 5.2f), (0.7f, 4.4f), (-0.2f, 3.8f), (-3.4f, 3.8f), (-6.0f, 3.0f) };
                camp.TeleportTo(C(path[0].Item1, 0f, path[0].Item2), cy);
                for (int i = 1; i < path.Length; i++)
                {
                    WalkTo(C(path[i].Item1, 0f, path[i].Item2), 0.8f);
                    float feet = rider.transform.position.y - Rider.R - C(0, 0, 0).y;
                    Debug.Log($"LODGESHOT {n} at mark {i}: feet {feet:0.00} above the floor, camera {cam.transform.position.y - C(0, 0, 0).y:0.00}");
                    if (i == 2) { Frames(6); Shoot($"LodgeLayout{n}_4hero_stair.png"); }
                }
                Frames(30);
                Shoot($"LodgeLayout{n}_5hero_upstairs.png");
                // and below it, the camera under its slab
                var under = n == "B1" ? (-8.6f, -3.6f) : n == "B2" || n == "B2R" ? (-8.6f, -1.2f) : (-6.0f, 2.8f);
                camp.TeleportTo(C(under.Item1 + 3.0f, 0f, under.Item2), cy - 90f);
                WalkTo(C(under.Item1, 0f, under.Item2), 0.8f); Frames(20);
                Shoot($"LodgeLayout{n}_6hero_under.png");
                Debug.Log($"LODGESHOT under {n}'s upper floor: feet {rider.transform.position.y - Rider.R - C(0, 0, 0).y:0.00}, camera {cam.transform.position.y - C(0, 0, 0).y:0.00} above the floor");
            }
            else
            {
                camp.TeleportTo(C(1.1f, 0f, 5.4f), cy + 180f);
                WalkTo(C(1.1f, 0f, 1.6f), 0.8f); Frames(6);
                Shoot($"LodgeLayout{n}_4hero_stair.png");
                Debug.Log($"LODGESHOT on C's stair: feet {rider.transform.position.y - Rider.R - C(0, 0, 0).y:0.00} above the floor");
                WalkTo(C(1.1f, 0f, -2.6f), 0.8f); WalkTo(C(-2.0f, 0f, -3.1f), 0.8f); Frames(20);
                Debug.Log($"LODGESHOT on C's mezzanine: feet {rider.transform.position.y - Rider.R - C(0, 0, 0).y:0.00} above the floor");
                camp.TeleportTo(C(-4.0f, 3.8f, -2.6f), cy + 20f); Frames(40);
                Shoot($"LodgeLayout{n}_5hero_mezzanine.png");
                camp.TeleportTo(C(2.8f, 0f, -2.8f), cy - 90f);
                WalkTo(C(-5.5f, 0f, -2.8f), 0.8f); Frames(10);
                Shoot($"LodgeLayout{n}_6hero_undermezz.png");
                Debug.Log($"LODGESHOT under C's mezzanine: feet {rider.transform.position.y - Rider.R - C(0, 0, 0).y:0.00}, camera {cam.transform.position.y - C(0, 0, 0).y:0.00} above the floor");
            }
            var at = camp.StationPoint(Camp.Station.Style);
            camp.TeleportTo(at, cy + 180f); Frames(40);
            meta.SetContext(camp.Near);
            Shoot($"LodgeLayout{n}_7wardrobe.png");
            Debug.Log($"LODGESHOT layout {n} at the wardrobe: near={camp.Near}");
            meta.SetContext(Camp.Station.None);
        }
        PlayerPrefs.SetInt(Camp.LodgePref, 0);
        camp.Rebuild();
        meta.Refresh();
    }

    /// <summary>
    /// What each layout costs to draw, measured fairly on a busy machine: rounds of A, B, C in turn (so whatever else
    /// the Mac is doing falls on all three alike), each timing the view in from the door at dusk with its inside on
    /// and off, interleaved, and the photo's view at night with the whole lodge on and off.
    /// </summary>
    /// <summary>The layouts to render and time: LODGE_ONLY (keys, comma-separated, e.g. "B1,B2,B3"), or all of them.</summary>
    static List<int> Chosen()
    {
        var only = System.Environment.GetEnvironmentVariable("LODGE_ONLY");
        var l = new List<int>();
        for (int k = 0; k < Camp.LodgeLayoutKeys.Length; k++)
            if (string.IsNullOrEmpty(only) || System.Array.IndexOf(only.Split(','), Camp.LodgeLayoutKeys[k]) >= 0) l.Add(k);
        return l;
    }

    static void CostRounds()
    {
        const int Rounds = 4, N = 15;
        int NL = Camp.LodgeLayoutKeys.Length;
        var inOn = new List<double>[NL]; var inOff = new List<double>[NL]; var outOn = new List<double>[NL]; var outOff = new List<double>[NL];
        for (int k = 0; k < NL; k++) { inOn[k] = new List<double>(); inOff[k] = new List<double>(); outOn[k] = new List<double>(); outOff[k] = new List<double>(); }
        var chosen = Chosen();
        var rt = new RenderTexture(1170, 2532, 24);
        var tmp = new Texture2D(1, 1);
        double Med(List<double> l) { var c = new List<double>(l); c.Sort(); return c[c.Count / 2]; }
        for (int r = 0; r < Rounds; r++)
            foreach (int k in chosen)
            {
                PlayerPrefs.SetInt(Camp.LodgePref, k);
                camp.Rebuild();
                Transform root = null, inside = null;
                foreach (var t in camp.GetComponentsInChildren<Transform>(true)) if (t.name == "Lodge") { root = t; break; }
                foreach (Transform c in root) if (c.name.StartsWith("Inside")) inside = c;
                double One(GameObject g, bool on)
                {
                    g.SetActive(on);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    cam.Render();
                    RenderTexture.active = rt; tmp.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;
                    return sw.Elapsed.TotalMilliseconds;
                }
                void Clock(GameObject g, List<double> on, List<double> off)
                {
                    cam.targetTexture = rt; cam.aspect = 1170f / 2532f;
                    for (int i = 0; i < 3; i++) { One(g, true); One(g, false); }
                    var a = new List<double>(); var b = new List<double>();
                    for (int i = 0; i < N; i++) { a.Add(One(g, true)); b.Add(One(g, false)); }
                    g.SetActive(true);
                    cam.targetTexture = null;
                    on.Add(Med(a)); off.Add(Med(b));
                }
                // (no frames stepped in here: in a batch run each step queues the forest's instanced draws again, and with
                // no end of frame to clear them every later render draws them all; the queue stays as it is for all three)
                Away(true);
                TimeOfDay.TestClock = 2.75f; dayNight.ApplyNow();
                var eye = camp.ChaletPoint(4.0f, 1.65f, 4.4f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(camp.ChaletPoint(-6.5f, 2.1f, -3.4f) - eye, Vector3.up)); cam.fieldOfView = 88f;
                Clock(inside.gameObject, inOn[k], inOff[k]);
                TimeOfDay.TestClock = 3.4f; dayNight.ApplyNow();
                PhotoCam(new Vector3(11f, 0f, 29f), new Vector3(-2f, 4.0f, 0f), 46f, false, true);
                Clock(root.gameObject, outOn[k], outOff[k]);
                noUi = false;
            }
        Object.DestroyImmediate(tmp); rt.Release();
        foreach (int k in chosen)
            Debug.Log($"LODGECOST [{Camp.LodgeLayoutKeys[k]}] phone-size renders, editor on this Mac, {Rounds} rounds of the layouts in turn x {N} interleaved (median of the rounds' medians): " +
                      $"in from the door at dusk {Med(inOn[k]):0.00} ms, the same with its inside off {Med(inOff[k]):0.00} ms (the inside: +{Med(inOn[k]) - Med(inOff[k]):0.00} ms); " +
                      $"the photo's view at night {Med(outOn[k]):0.00} ms, without the lodge {Med(outOff[k]):0.00} ms; rounds in from the door: {string.Join(" / ", inOn[k].ConvertAll(x => x.ToString("0.0")))}");
        Simulate();
    }

    // the lodge's own frame (fixed: the old chalet sits elsewhere, so its comparison shots use the lodge's spot)
    static Vector3 L(float x, float y, float z)
    {
        var o = new Vector3(Slope.Centre(Slope.PadLip) + Camp.LodgeDx, 0f, Camp.LodgeZ);
        var w = o + Quaternion.Euler(0f, Camp.LodgeYaw, 0f) * new Vector3(x, 0f, z);
        return new Vector3(w.x, y, w.z);
    }

    /// <summary>A still camera standing on the snow at (x, z) in the lodge's frame, eye height, looking at a point on it.</summary>
    static void PhotoCam(Vector3 at, Vector3 look, float vfov, bool fixedFrame = false, bool still = false)
    {
        var p = fixedFrame ? L(at.x, 0f, at.z) : camp.ChaletPoint(at.x, 0f, at.z);
        p.y = Slope.DrawnHeight(p.x, p.z) + 2.6f;   // (the photo is taken from a little above the plaza)
        var floor = fixedFrame ? Slope.DrawnHeight(p.x, p.z) + 1.5f : camp.ChaletPoint(0f, 0f, 0f).y;
        var q = fixedFrame ? L(look.x, 0f, look.z) : camp.ChaletPoint(look.x, 0f, look.z);
        q.y = floor + look.y;
        if (!still) Away();
        cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(q - p, Vector3.up));
        cam.fieldOfView = vfov;
        noUi = true;
    }

    /// <summary>The hero out of the shot: behind the lodge, on the plateau (not off its edge: that's the cliff).</summary>
    static void Away(bool still = false)
    {
        camp.TeleportTo(L(0f, 0f, -11f) + Vector3.up * 50f, 0f);
        if (still) body.Apply(dt); else Frames(4);
    }

    static void EyeCam(Vector3 p, Vector3 q, float vfov)
    {
        Away();
        noUi = true;
        cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(q - p, Vector3.up));
        cam.fieldOfView = vfov;
    }

    static void SetTime(float clock)
    {
        TimeOfDay.TestClock = clock;
        dayNight.ApplyNow();
        Frames(2);
    }

    static void Simulate()
    {
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(3f, true, true);
    }

    /// <summary>What the lodge costs to draw: its pieces, triangles and lamps, and the time to render the photo's view with and without it.</summary>
    static void Cost(string label, bool timing = true)
    {
        var lodge = camp.ChaletPoint(0, 0, 0);
        Transform root = null;
        foreach (var t in camp.GetComponentsInChildren<Transform>(true)) if (t.name == "Lodge") { root = t; break; }
        if (root == null) { Debug.LogError("LODGESHOT no lodge"); return; }
        int rends = 0, tris = 0, casters = 0; var mats = new HashSet<Material>();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            rends++; mats.UnionWith(r.sharedMaterials);
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++;
            var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
        }
        int lights = root.GetComponentsInChildren<Light>(true).Length, inTris = 0, inRends = 0;
        foreach (Transform c in root)
            if (c.name.StartsWith("Inside"))
                foreach (var r in c.GetComponentsInChildren<MeshRenderer>()) { inRends++; inTris += r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3; }
        int pathTris = 0, pathRends = 0;
        foreach (var t in camp.GetComponentsInChildren<Transform>(true))
            if (t.name == "Path" || t.name == "PathBanks" || t.name == "LampPosts")
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) { pathRends++; pathTris += r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3; }
        Debug.Log($"LODGECOST [{label}] inside: {inRends} meshes, {inTris} triangles");
        Debug.Log($"LODGECOST [{label}] lodge: {rends} meshes ({casters} cast shadows), {mats.Count} materials, {tris} triangles, {lights} lamps; path and {camp.LampPosts} lamp posts: {pathRends} meshes, {pathTris} triangles");
        if (!timing) return;
        // render time of the photo's view (main thread, editor; a rough comparison, not the phone's GPU)
        SetTime(3.4f);
        PhotoCam(new Vector3(11f, 0f, 29f), new Vector3(-2f, 4.0f, 0f), 46f);
        var rt = new RenderTexture(1170, 2532, 24); cam.targetTexture = rt; cam.aspect = 1170f / 2532f;
        // interleaved, with and without the lodge, so the machine's other work falls on both alike; each render waits for the GPU
        var tmp = new Texture2D(1, 1);
        double One(bool on)
        {
            root.gameObject.SetActive(on);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            cam.Render();
            RenderTexture.active = rt; tmp.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;
            return sw.Elapsed.TotalMilliseconds;
        }
        for (int i = 0; i < 6; i++) { One(true); One(false); }
        var with = new List<double>(); var without = new List<double>();
        for (int i = 0; i < 61; i++) { with.Add(One(true)); without.Add(One(false)); }
        with.Sort(); without.Sort();
        root.gameObject.SetActive(true);
        Object.DestroyImmediate(tmp);
        cam.targetTexture = null;
        // and inside: the view from the door at dusk, every lamp on (the same view in each layout)
        SetTime(2.75f);
        EyeCam(camp.ChaletPoint(4.0f, 1.65f, 4.4f), camp.ChaletPoint(-6.5f, 2.1f, -3.4f), 88f);
        cam.targetTexture = rt; cam.aspect = 1170f / 2532f;
        tmp = new Texture2D(1, 1);
        var inside = new List<double>();
        for (int i = 0; i < 6; i++) One(true);
        for (int i = 0; i < 61; i++) inside.Add(One(true));
        inside.Sort();
        Object.DestroyImmediate(tmp);
        cam.targetTexture = null;
        noUi = false;
        Debug.Log($"LODGECOST [{label}] render of the view in from the door at dusk, phone size, editor on this Mac, median of 61: {inside[30]:0.00} ms (quartiles {inside[15]:0.00}-{inside[45]:0.00})");
        Debug.Log($"LODGECOST [{label}] render of the photo's view at night, phone size (1170 x 2532), editor on this Mac, median of 61 interleaved: with the lodge {with[30]:0.00} ms, without {without[30]:0.00} ms (quartiles {with[15]:0.00}-{with[45]:0.00} / {without[15]:0.00}-{without[45]:0.00})");
    }

    static void Frames(int n) { for (int i = 0; i < n; i++) Step(Vector2.zero); }

    /// <summary>Walk to a point with the stick (camera-relative, as a player steers), at most 8 s.</summary>
    static void WalkTo(Vector3 to, float stick)
    {
        var fPos = typeof(Camp).GetField("pos", NP); var fCamYaw = typeof(Camp).GetField("camYaw", NP);
        for (float t = 0f; t < 8f; t += dt)
        {
            var p = (Vector3)fPos.GetValue(camp);
            if (new Vector2(to.x - p.x, to.z - p.z).magnitude < 0.5f) break;
            float head = Mathf.Atan2(to.x - p.x, to.z - p.z) - (float)fCamYaw.GetValue(camp);
            Step(new Vector2(Mathf.Sin(head), Mathf.Cos(head)) * stick);
        }
    }

    static void Walk(Vector2 stick, float seconds) { for (float t = 0f; t < seconds; t += dt) Step(stick); }

    static void Step(Vector2 stick)
    {
        camp.Walk(stick, dt);
        body.PhysicsStep();
        body.Apply(dt);
        camp.CameraStep(dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        aLate.Invoke(atmo, null);
    }

    static bool noUi;

    static void Shoot(string file, int w = 590, int h = 1278)
    {
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        Canvas.ForceUpdateCanvases();
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        int mask = cam.cullingMask; cam.cullingMask |= 1 << meta.gameObject.layer;
        // (the still views without the camp's buttons)
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
        Debug.Log($"LODGESHOT {file} walker={rider.transform.position} near={camp.Near} cam={cam.transform.position}");
    }
}
