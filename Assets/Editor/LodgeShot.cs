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
    // the spaces inside, each seen from a person's height in it: from, to (the lodge's frame), field of view
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
        // LODGE_COVERED=1: the covered section at the low end, the floor where the wing meets the hall, the café and
        // the hot chocolate, at the phone's quality (LODGE_TAG names the set: before, after)
        if (System.Environment.GetEnvironmentVariable("LODGE_COVERED") == "1") { Covered(); return; }
        // LODGE_TEX=1: the lodge's surfaces close enough to see their textures (the hall, the café and the hot
        // chocolate, the loft, the covered section, the outside), at the phone's quality
        if (System.Environment.GetEnvironmentVariable("LODGE_TEX") == "1") { Textures(); return; }
        // LODGE5=1: the lounge, the loft, the balcony over the wing and its view, the lodge's details, at the phone's quality
        if (System.Environment.GetEnvironmentVariable("LODGE5") == "1") { Lodge5(); return; }
        // CAMP_SNOW=1: the camp's snow underfoot (the plaza, the fire, the path, the lodge's wing and the piles there, the
        // gate, close up and from above) and the hero walking into a pile, by day and at night
        if (System.Environment.GetEnvironmentVariable("CAMP_SNOW") == "1") { CampSnow(); return; }
        // CAMP_FIRE=1: the fire and the corner round it (CampFirePit.cs), by day and at night
        if (System.Environment.GetEnvironmentVariable("CAMP_FIRE") == "1") { CampFire(); return; }
        // the inside (LODGE_FULL=1 also renders the outside, the walk in and the spaces inside)
        Inside();
        if (System.Environment.GetEnvironmentVariable("LODGE_FULL") != "1") return;
        Cost("lodge");

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
        // 10-16: inside, by day: each space from a person's height, then the hero's own views
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
    /// The inside, by day and at dusk: from the door and from the far end at eye height, the ground floor and the loft,
    /// the back wall with its stair, the loft from its back corner; the hero coming in at the door, up the stair into
    /// the loft and under it; the wardrobe; and what it costs to draw.
    /// </summary>
    static void Inside()
    {
        Vector3 C(float x, float y, float z) => camp.ChaletPoint(x, y, z);
        float cy = camp.ChaletYaw;
        // (first: the editor's renders slow down over a long run of shots here, so the timing comes before them;
        // LODGE_COST_ONLY=1 stops after it)
        CostRounds();
        if (System.Environment.GetEnvironmentVariable("LODGE_COST_ONLY") == "1") return;
        camp.Rebuild();
        Simulate();
        meta.Refresh();
        Cost("lodge", false);
        foreach (var (name, clock) in new[] { Times[0], Times[1] })
        {
            SetTime(clock);
            EyeCam(C(4.0f, 1.65f, 4.4f), C(-6.5f, 2.1f, -3.4f), 88f); Shoot($"LodgeInside_1door_{name}.png", 720, 960);
            EyeCam(C(-10.4f, 1.65f, 2.6f), C(6.0f, 1.9f, -2.0f), 88f); Shoot($"LodgeInside_2far_{name}.png", 720, 960);
            EyeCam(C(1.4f, 1.65f, 0.8f), C(-9.0f, 3.2f, 3.6f), 88f); Shoot($"LodgeInside_8ground_{name}.png", 720, 960);
            EyeCam(C(-5.8f, 5.45f, 5.3f), C(-9.6f, 4.8f, -4.6f), 88f); Shoot($"LodgeInside_9upstairs_{name}.png", 720, 960);
            EyeCam(C(-0.6f, 1.65f, 5.6f), C(-2.4f, 2.4f, -6.7f), 88f); Shoot($"LodgeInside_7backwall_{name}.png", 720, 960);
            EyeCam(C(-6.0f, 5.45f, -5.9f), C(-9.0f, 4.6f, 4.0f), 88f); Shoot($"LodgeInside_9loftback_{name}.png", 720, 960);
        }
        SetTime(1.4f);
        camp.TeleportTo(C(4.5f, 0f, 9.0f), cy + 180f);
        WalkTo(C(4.5f, 0f, 3.6f), 0.8f); Frames(20);
        Shoot("LodgeInside_3hero_in.png");
        // up the stair along the back wall into the loft (the camera kept clear of its floor)
        var path = new[] { (2.6f, -4.6f), (0.4f, -5.9f), (-2.2f, -5.9f), (-5.0f, -5.9f), (-6.6f, -5.3f), (-7.4f, -3.6f) };
        camp.TeleportTo(C(path[0].Item1, 0f, path[0].Item2), cy);
        for (int i = 1; i < path.Length; i++)
        {
            WalkTo(C(path[i].Item1, 0f, path[i].Item2), 0.8f);
            Debug.Log($"LODGESHOT at mark {i}: feet {rider.transform.position.y - Rider.R - C(0, 0, 0).y:0.00} above the floor, camera {cam.transform.position.y - C(0, 0, 0).y:0.00}");
            if (i == 2) { Frames(6); Shoot("LodgeInside_4hero_stair.png"); }
        }
        Frames(30);
        Shoot("LodgeInside_5hero_upstairs.png");
        // and below the loft, the camera under its slab
        camp.TeleportTo(C(-5.6f, 0f, -1.2f), cy - 90f);
        WalkTo(C(-8.6f, 0f, -1.2f), 0.8f); Frames(20);
        Shoot("LodgeInside_6hero_under.png");
        var at = camp.StationPoint(Camp.Station.Style);
        camp.TeleportTo(at, cy + 180f); Frames(40);
        meta.SetContext(camp.Near);
        Shoot("LodgeInside_7wardrobe.png");
        Debug.Log($"LODGESHOT at the wardrobe: near={camp.Near}");
        meta.SetContext(Camp.Station.None);
    }

    /// <summary>
    /// The lodge at the phone's quality (Mobile: render scale 0.8, its shadows; phone-size frames): the floor
    /// where the wing (the hot chocolate) meets the hall (the café's tables under the loft) from the hall, from the wing,
    /// close, and as the hero walks through; the covered section at the low end (the racks and picnic tables under the
    /// roof's tail) from its front, the side door, its back corner, the snow outside, above, and the hero walking out the
    /// side door; the café and the hot chocolate corner. Files: {LODGE_TAG}_*.png.
    /// </summary>
    static void Covered()
    {
        Vector3 C(float x, float y, float z) => camp.ChaletPoint(x, y, z);
        float cy = camp.ChaletYaw;
        string tag = System.Environment.GetEnvironmentVariable("LODGE_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "now";
        int keepQ = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // (the phone's: ProjectSettings' iPhone default)
        try
        {
            Simulate();
            Debug.Log($"LODGESHOT covered: quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            foreach (var (name, clock) in new[] { Times[0], Times[1] })
            {
                SetTime(clock);
                // the floor where the wing meets the hall
                EyeCam(C(-7.6f, 1.65f, 1.6f), C(-12.6f, 0.0f, -1.6f), 70f);
                Shoot($"{tag}_flicker_1hall_to_wing_{name}.png");
                EyeCam(C(-13.8f, 1.65f, 2.6f), C(-9.0f, 0.0f, -0.8f), 70f);
                Shoot($"{tag}_flicker_2wing_to_hall_{name}.png");
                EyeCam(C(-9.9f, 2.0f, -2.4f), C(-11.3f, 0.0f, 0.4f), 58f);
                Shoot($"{tag}_flicker_3close_{name}.png");
                // the covered section
                EyeCam(C(15.0f, 1.65f, 11.6f), C(15.0f, 0.6f, -6.0f), 80f);
                Shoot($"{tag}_covered_1front_{name}.png");
                EyeCam(C(11.7f, 1.65f, 3.95f), C(18.0f, 0.6f, -3.0f), 84f);
                Shoot($"{tag}_covered_2sidedoor_{name}.png");
                EyeCam(C(18.0f, 1.75f, -6.3f), C(12.0f, 0.6f, 6.0f), 84f);
                Shoot($"{tag}_covered_3backcorner_{name}.png");
                PhotoCam(new Vector3(27f, 0f, 21f), new Vector3(13f, 1.6f, 1.0f), 52f);
                Shoot($"{tag}_covered_4outside_{name}.png", 960, 720);
                EyeCam(C(31f, 20f, 24f), C(12f, 0f, 1f), 50f);
                Shoot($"{tag}_covered_5above_{name}.png", 960, 720);
                // the café and the hot chocolate corner
                EyeCam(C(-8.4f, 1.65f, 2.0f), C(-14.6f, 1.0f, -2.4f), 80f);
                Shoot($"{tag}_cafe_1hall_{name}.png");
                EyeCam(C(-11.6f, 1.65f, 4.0f), C(-14.0f, 0.9f, -4.4f), 80f);
                Shoot($"{tag}_cafe_2cocoa_{name}.png");
            }
            SetTime(1.4f);
            // the hero's own views (the chase camera): from the café's tables into the wing and back out
            camp.TeleportTo(C(-6.8f, 0f, 0.8f), cy - 90f);
            WalkTo(C(-10.0f, 0f, 0.8f), 0.6f); Frames(12); noUi = true;
            Shoot($"{tag}_flicker_4hero_into_wing.png");
            camp.TeleportTo(C(-14.2f, 0f, -1.6f), cy + 80f);
            WalkTo(C(-11.4f, 0f, -1.2f), 0.6f); Frames(12); noUi = true;
            Shoot($"{tag}_flicker_5hero_out_of_wing.png");
            // out of the side door into the covered section, and to the racks
            camp.TeleportTo(C(8.6f, 0f, 3.95f), cy + 90f);
            WalkTo(C(14.4f, 0f, 3.95f), 0.7f); Frames(12); noUi = true;
            Shoot($"{tag}_covered_6hero_sidedoor.png");
            WalkTo(C(14.9f, 0f, -1.9f), 0.7f); Frames(12); noUi = true;
            Shoot($"{tag}_covered_7hero_racks.png");
            Debug.Log($"LODGESHOT covered: hero at {camp.ChaletPoint(0, 0, 0)} + local {Quaternion.Inverse(Quaternion.Euler(0f, cy, 0f)) * (rider.transform.position - camp.ChaletPoint(0, 0, 0))}, camera {cam.transform.position.y - C(0, 0, 0).y:0.00} above the floor");
        }
        finally { QualitySettings.SetQualityLevel(keepQ, true); }
    }

    /// <summary>
    /// The lodge's surfaces at the phone's quality (Mobile, phone-size frames), by day and at dusk: the hall
    /// from the door, the lounge (the fire's hearth, the sofas, the rug), the back wall and its stair, the ceiling; the
    /// café, its counter and the hot chocolate; the loft; the covered section; the photo's view and the cladding close;
    /// the hero coming in and at the racks. Files: {LODGE_TAG}_tex_*.png.
    /// </summary>
    static void Textures()
    {
        Vector3 C(float x, float y, float z) => camp.ChaletPoint(x, y, z);
        float cy = camp.ChaletYaw;
        string tag = System.Environment.GetEnvironmentVariable("LODGE_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "now";
        int keepQ = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // (the phone's)
        try
        {
            Simulate();
            Debug.Log($"LODGESHOT textures: quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            foreach (var (name, clock) in new[] { Times[0], Times[1] })
            {
                SetTime(clock);
                EyeCam(C(4.0f, 1.65f, 4.4f), C(-6.5f, 2.1f, -3.4f), 88f); Shoot($"{tag}_tex_hall_1door_{name}.png");
                EyeCam(C(1.2f, 1.5f, -0.6f), C(-2.2f, 0.45f, -3.2f), 72f); Shoot($"{tag}_tex_hall_2fire_{name}.png");
                EyeCam(C(-0.6f, 1.65f, 5.6f), C(-2.4f, 2.4f, -6.7f), 88f); Shoot($"{tag}_tex_hall_3backwall_{name}.png");
                EyeCam(C(2.0f, 1.6f, 3.0f), C(-1.0f, 6.5f, -2.0f), 80f); Shoot($"{tag}_tex_hall_4ceiling_{name}.png");
                EyeCam(C(-8.4f, 1.65f, 2.0f), C(-14.6f, 1.0f, -2.4f), 80f); Shoot($"{tag}_tex_cafe_1hall_{name}.png");
                EyeCam(C(-11.6f, 1.65f, 4.0f), C(-14.0f, 0.9f, -4.4f), 80f); Shoot($"{tag}_tex_cafe_2cocoa_{name}.png");
                EyeCam(C(-12.4f, 1.55f, 1.4f), C(-14.0f, 0.95f, 4.5f), 74f); Shoot($"{tag}_tex_cafe_3counter_{name}.png");
                EyeCam(C(-5.8f, 5.45f, 5.3f), C(-9.6f, 4.8f, -4.6f), 88f); Shoot($"{tag}_tex_loft_1_{name}.png");
                EyeCam(C(-6.0f, 5.45f, -5.9f), C(-9.0f, 4.6f, 4.0f), 88f); Shoot($"{tag}_tex_loft_2back_{name}.png");
                EyeCam(C(15.0f, 1.65f, 11.6f), C(15.0f, 0.6f, -6.0f), 80f); Shoot($"{tag}_tex_covered_1front_{name}.png");
                EyeCam(C(18.0f, 1.75f, -6.3f), C(12.0f, 0.6f, 6.0f), 84f); Shoot($"{tag}_tex_covered_2backcorner_{name}.png");
                PhotoCam(new Vector3(11f, 0f, 29f), new Vector3(-2f, 4.0f, 0f), 46f); Shoot($"{tag}_tex_outside_1photo_{name}.png", 1200, 800);
                EyeCam(C(9.4f, 1.6f, 9.2f), C(5.0f, 2.2f, 6.9f), 70f); Shoot($"{tag}_tex_outside_2cladding_{name}.png");
            }
            SetTime(1.4f);
            camp.TeleportTo(C(4.5f, 0f, 9.0f), cy + 180f);
            WalkTo(C(4.5f, 0f, 3.6f), 0.8f); Frames(20); noUi = true;
            Shoot($"{tag}_tex_hero_1in.png");
            camp.TeleportTo(C(8.6f, 0f, 3.95f), cy + 90f);
            WalkTo(C(14.4f, 0f, 3.95f), 0.7f); WalkTo(C(14.9f, 0f, -1.9f), 0.7f); Frames(12); noUi = true;
            Shoot($"{tag}_tex_hero_2racks.png");
        }
        finally { QualitySettings.SetQualityLevel(keepQ, true); }
    }

    /// <summary>
    /// The camp's fire at the phone's quality (Mobile), by day and at night (CampFirePit.cs): from the open front as you
    /// come to it, the woodpile and the chopping block in the back corner, down into the ring, low across it.
    /// Files: {LODGE_TAG}_fire_*.png.
    /// </summary>
    static void CampFire()
    {
        Vector3 G(float dx, float z, float up = 0f) => Camp.GroundAt(dx, z) + Vector3.up * up;
        string tag = System.Environment.GetEnvironmentVariable("LODGE_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "now";
        int keepQ = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // (the phone's)
        try
        {
            Simulate();
            foreach (var (name, clock) in new[] { Times[0], Times[2] })
            {
                SetTime(clock);
                EyeCam(G(1.8f, -64.6f, 1.65f), G(0.4f, -72f, 0.2f), 62f); Shoot($"{tag}_fire_1front_{name}.png");
                EyeCam(G(-0.8f, -68.2f, 1.55f), G(3.0f, -73.6f, 0.35f), 60f); Shoot($"{tag}_fire_2corner_{name}.png");
                EyeCam(G(0.6f, -69.4f, 2.5f), G(0f, -71f), 60f); Shoot($"{tag}_fire_3down_{name}.png");
                EyeCam(G(1.6f, -69.6f, 0.55f), G(0f, -71.1f, 0.3f), 58f); Shoot($"{tag}_fire_4low_{name}.png");
            }
        }
        finally { QualitySettings.SetQualityLevel(keepQ, true); }
    }

    /// <summary>
    /// The camp's snow at the phone's quality (Mobile), by day and at night: the plaza from the lodge's steps, the fire's
    /// ring, the path, the lodge's wing and the piles of snow beside it, the way to the gate, the snow close up at the
    /// steps' foot and by the fire, the camp from above; the hero walking across the plaza and into the drift
    /// against the terrace's front. Files: {LODGE_TAG}_snow_*.png.
    /// </summary>
    static void CampSnow()
    {
        Vector3 G(float dx, float z, float up = 0f) => Camp.GroundAt(dx, z) + Vector3.up * up;
        string tag = System.Environment.GetEnvironmentVariable("LODGE_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "now";
        int keepQ = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // (the phone's)
        try
        {
            Simulate();
            var foot = camp.StairsFoot;
            foreach (var (name, clock) in new[] { Times[0], Times[2] })
            {
                SetTime(clock);
                EyeCam(foot + Vector3.up * 1.7f + (G(0f, -60f) - foot).normalized * 0.5f, G(0f, -62f), 70f); Shoot($"{tag}_snow_1plaza_{name}.png");
                EyeCam(G(3.6f, -65.5f, 1.6f), G(0f, -71f), 66f); Shoot($"{tag}_snow_2fire_{name}.png");
                EyeCam(G(-6f, -64f, 1.7f), G(-22f, -84f), 70f); Shoot($"{tag}_snow_3wing_{name}.png");
                EyeCam(foot + Vector3.up * 1.25f + Vector3.forward * 1.2f, foot + Vector3.forward * 3.6f, 60f); Shoot($"{tag}_snow_4steps_close_{name}.png");
                EyeCam(G(1.6f, -68f, 1.1f), G(0.4f, -70f), 60f); Shoot($"{tag}_snow_5fire_close_{name}.png");
                EyeCam(G(-4f, -60f, 1.7f), G(0f, Slope.GateZ), 70f); Shoot($"{tag}_snow_6gate_{name}.png");
                EyeCam(G(-2f, -58f, 34f), G(-6f, -80f), 60f); Shoot($"{tag}_snow_7above_{name}.png", 960, 720);
                EyeCam(G(-27f, -66f, 1.7f), G(-24f, -84f), 66f); Shoot($"{tag}_snow_8piles_{name}.png");
            }
            SetTime(Times[0].clock);
            // the hero across the plaza, then into a drift against the lodge's terrace
            camp.TeleportTo(G(4f, -60f), camp.ChaletYaw + 180f);
            WalkTo(G(-2f, -66f), 0.8f); Frames(12); noUi = true;
            Shoot($"{tag}_snow_9hero_plaza.png");
            // into the drift against the terrace's front wall, left of the steps, from the open snow in front (the lodge's
            // frame: fixed points)
            var fPos = typeof(Camp).GetField("pos", NP);
            var to = camp.ChaletPoint(-8.4f, 0f, 13.0f); to.y = Slope.DrawnHeight(to.x, to.z);
            var from = camp.ChaletPoint(-8.4f, 0f, 19.5f); from.y = Slope.DrawnHeight(from.x, from.z);
            camp.TeleportTo(from, Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg);
            WalkTo(to, 0.8f); Frames(12); noUi = true;
            Shoot($"{tag}_snow_10hero_pile.png");
            var at = (Vector3)fPos.GetValue(camp);
            Debug.Log($"LODGESHOT snow: walked at the terrace's drift: stopped {new Vector2(at.x - to.x, at.z - to.z).magnitude:0.00} m short, feet {at.y - Slope.DrawnHeight(at.x, at.z):0.00} over the ground");
        }
        finally { QualitySettings.SetQualityLevel(keepQ, true); }
    }

    /// <summary>
    /// The lodge at the phone's quality (Mobile, phone-size frames), by day and at dusk: the lounge round the
    /// fire from the door, from the glass, from the low end and from the loft's rail; the loft and its end (the doors
    /// out); the balcony over the wing: its view (the slope and the mountain), back at the doors, and from the snow; the
    /// photo's view; the hero walking up into the loft and out onto the balcony. Files: {LODGE_TAG}_*.png.
    /// </summary>
    static void Lodge5()
    {
        Vector3 C(float x, float y, float z) => camp.ChaletPoint(x, y, z);
        float cy = camp.ChaletYaw;
        string tag = System.Environment.GetEnvironmentVariable("LODGE_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "now";
        int keepQ = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // (the phone's)
        try
        {
            Simulate();
            Debug.Log($"LODGESHOT lodge5: quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            foreach (var (name, clock) in new[] { Times[0], Times[1] })
            {
                SetTime(clock);
                EyeCam(C(4.0f, 1.65f, 4.4f), C(-3.0f, 0.9f, -2.2f), 84f); Shoot($"{tag}_lounge_1door_{name}.png");
                EyeCam(C(-4.9f, 1.7f, 6.3f), C(0.2f, 0.7f, -2.6f), 84f); Shoot($"{tag}_lounge_2glass_{name}.png");
                EyeCam(C(4.4f, 1.7f, -1.2f), C(-5.0f, 0.8f, 0.4f), 84f); Shoot($"{tag}_lounge_3lowend_{name}.png");
                EyeCam(C(-5.7f, 5.5f, 4.2f), C(-1.2f, 0.0f, -1.2f), 76f); Shoot($"{tag}_lounge_4fromloft_{name}.png");
                EyeCam(C(-5.8f, 5.45f, 5.3f), C(-9.6f, 4.8f, -4.6f), 88f); Shoot($"{tag}_loft_1_{name}.png");
                EyeCam(C(-6.0f, 5.45f, -1.4f), C(-12.0f, 5.0f, 1.4f), 84f); Shoot($"{tag}_loft_2end_{name}.png");
                EyeCam(C(-12.2f, 5.45f, -0.6f), C(-24.0f, 3.0f, 22.0f), 84f); Shoot($"{tag}_balcony_1view_{name}.png");
                EyeCam(C(-16.4f, 5.45f, 5.9f), C(-11.0f, 4.9f, -1.0f), 84f); Shoot($"{tag}_balcony_2back_{name}.png");
                EyeCam(C(-15.0f, 5.45f, -4.6f), C(-17.0f, 3.0f, 30.0f), 84f); Shoot($"{tag}_balcony_3front_{name}.png");
                PhotoCam(new Vector3(-27f, 0f, 22f), new Vector3(-13f, 4.2f, 0f), 50f); Shoot($"{tag}_balcony_4outside_{name}.png", 960, 720);
                PhotoCam(new Vector3(11f, 0f, 29f), new Vector3(-2f, 4.0f, 0f), 46f); Shoot($"{tag}_outside_photo_{name}.png", 1200, 800);
            }
            SetTime(1.4f);
            // the hero: up the stair into the loft, across it and out onto the balcony, to its corner
            camp.TeleportTo(C(-6.4f, 3.8f, -5.6f), cy - 90f);
            WalkTo(C(-7.6f, 3.8f, -3.6f), 0.7f); WalkTo(C(-9.6f, 3.8f, -0.4f), 0.7f); Frames(12); noUi = true;
            Shoot($"{tag}_hero_1loft.png");
            WalkTo(C(-12.4f, 3.8f, -0.4f), 0.7f); WalkTo(C(-14.6f, 3.8f, 3.6f), 0.7f); Frames(16); noUi = true;
            Shoot($"{tag}_hero_2balcony.png");
            camp.TeleportTo(C(4.5f, 0f, 9.0f), cy + 180f);
            WalkTo(C(4.0f, 0f, 3.2f), 0.8f); Frames(20); noUi = true;
            Shoot($"{tag}_hero_3lounge.png");
            Debug.Log($"LODGESHOT lodge5: hero at {Quaternion.Inverse(Quaternion.Euler(0f, cy, 0f)) * (rider.transform.position - camp.ChaletPoint(0, 0, 0))} in the lodge's frame");
        }
        finally { QualitySettings.SetQualityLevel(keepQ, true); }
    }

    /// <summary>
    /// What the lodge costs to draw, measured fairly on a busy machine: rounds of renders (so whatever else the Mac is
    /// doing falls on each alike), each timing the view in from the door at dusk with its inside on and off,
    /// interleaved, and the photo's view at night with the whole lodge on and off.
    /// </summary>
    static void CostRounds()
    {
        const int Rounds = 4, N = 15;
        var inOn = new List<double>(); var inOff = new List<double>(); var outOn = new List<double>(); var outOff = new List<double>();
        var rt = new RenderTexture(1170, 2532, 24);
        var tmp = new Texture2D(1, 1);
        double Med(List<double> l) { var c = new List<double>(l); c.Sort(); return c[c.Count / 2]; }
        for (int r = 0; r < Rounds; r++)
            {
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
                // no end of frame to clear them every later render draws them all; the queue stays as it is for each)
                Away(true);
                TimeOfDay.TestClock = 2.75f; dayNight.ApplyNow();
                var eye = camp.ChaletPoint(4.0f, 1.65f, 4.4f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(camp.ChaletPoint(-6.5f, 2.1f, -3.4f) - eye, Vector3.up)); cam.fieldOfView = 88f;
                Clock(inside.gameObject, inOn, inOff);
                TimeOfDay.TestClock = 3.4f; dayNight.ApplyNow();
                PhotoCam(new Vector3(11f, 0f, 29f), new Vector3(-2f, 4.0f, 0f), 46f, false, true);
                Clock(root.gameObject, outOn, outOff);
                noUi = false;
            }
        Object.DestroyImmediate(tmp); rt.Release();
        Debug.Log($"LODGECOST phone-size renders, editor on this Mac, {Rounds} rounds x {N} interleaved (median of the rounds' medians): " +
                  $"in from the door at dusk {Med(inOn):0.00} ms, the same with its inside off {Med(inOff):0.00} ms (the inside: +{Med(inOn) - Med(inOff):0.00} ms); " +
                  $"the photo's view at night {Med(outOn):0.00} ms, without the lodge {Med(outOff):0.00} ms; rounds in from the door: {string.Join(" / ", inOn.ConvertAll(x => x.ToString("0.0")))}");
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
        // and inside: the view from the door at dusk, every lamp on
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
        CampLife.Current?.Step(dt, cam.transform.position);   // (the camp's people and the lodge's, as in the game)
        LodgeFolk.Current?.Step(dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        aLate.Invoke(atmo, null);
    }

    static bool noUi;

    static void Shoot(string file, int w = 590, int h = 1278)
    {
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        // (the people's levels of detail from this camera, and everyone posed)
        CampLife.Current?.Step(1e-4f, cam.transform.position);
        if (LodgeFolk.Current != null) for (int k = 0; k < 3; k++) LodgeFolk.Current.Step(1e-4f);
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
