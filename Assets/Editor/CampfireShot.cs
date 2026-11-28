using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;   // not UnityEditor.Progress

/// <summary>
/// The camp's fire and kitchen, driven through the camp's own input (Camp.Step with made-up touches) and rendered by
/// day and night: Alyssa roasting, checking, blowing out a flare, building and eating her s'more; the TALK box and its
/// replies; the hero sitting to roast one (pale, golden, alight and blown out, burnt) and eating it; the hot chocolate
/// made at the stove (pour, stir, marshmallows on top); walking with the mug and sipping it to the end; and a mug
/// left on the gate's post. Checks along the way: no tap ever jumps or rolls while talking, roasting or holding a
/// mug, a held finger browns faster, the sips count down to nothing, and the stations still answer.
/// Batch: -executeMethod CampfireShot.Run  (writes Fire*.png in the project folder; exits 1 if a check fails)
/// </summary>
public static class CampfireShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Camp camp; static MetaUi meta; static RiderBody body; static Rider rider;
    static SlopeStreamer terrain; static Forest props; static Camera cam; static Atmosphere atmo; static DayNight dayNight;
    static MethodInfo tUpd, fUpd, aLate;
    static FieldInfo fMove;
    const float dt = 1f / 60f;
    static int fails, jumps;
    static readonly List<CampInput.Touch> touches = new List<CampInput.Touch>();

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game, false);
        camp = game.camp; meta = game.meta; body = game.body; rider = game.rider;
        terrain = game.terrain; props = game.props; dayNight = game.dayNight;
        var hud = game.hud;
        cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        fMove = typeof(Camp).GetField("moveKind", NP);
        Progress.Grant(0, Progress.Up.FirePit);   // (the fire is a bought decoration: Alyssa and the stumps come with it)
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        camp.Redress();
        meta.UseCamera(cam);
        var hc = hud.GetComponent<Canvas>(); hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
        hud.SetRun(0f, 0, 1f, 120f, 160f, false); hud.SetPower(0f, 0, false); hud.SetFalls(3, 3, false); hud.SetAltitude(0f, false); hud.SetMedical(0f, false);
        var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
        if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
        ChaseCamera.TestDt = dt;
        Camp.TestTouches = touches;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(3f, true, true);
        StartGate.Letters(true);
        var al = camp.Alyssa;
        Check(al != null, "Alyssa is at the fire");
        if (al == null) { Done(); return; }
        foreach (var smr in al.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var m = new Mesh(); smr.BakeMesh(m);
            Log($"Alyssa skin {smr.name}: enabled={smr.enabled} active={smr.gameObject.activeInHierarchy} verts={m.vertexCount} bakedBounds={m.bounds} rendererBounds={smr.bounds} lossy={smr.transform.lossyScale} mats={string.Join(",", System.Array.ConvertAll(smr.sharedMaterials, x => x != null ? x.shader.name : "null"))}");
        }
        if (System.Environment.GetEnvironmentVariable("FIRE_QUICK") == "1") { Shoot("Fire_debug.png"); Done(); return; }
        int points0 = Progress.Points;

        // ---- Alyssa: from just off her shoulder, then across the fire
        for (int i = 0; i < 3; i++) Shoot("Fire_warmup.png");
        var seat = al.Seat;
        void LookAtHer(Vector3 from) { camp.TeleportTo(new Vector3(from.x, Slope.DrawnHeight(from.x, from.z), from.z), Mathf.Atan2(seat.x - from.x, seat.z - from.z) * Mathf.Rad2Deg); Frames(20, false); }
        // her round played through from its start, stopping to look at each moment (the marshmallow as it really is by then)
        al.RoundTime = 0f;
        foreach (var (file, at) in new[] { ("Fire1_alyssa_roasting", 5f), ("Fire2_alyssa_checks", 8.6f), ("Fire3_alyssa_flare", 11.3f), ("Fire4_alyssa_blows", 11.65f), ("Fire5_alyssa_smore", 14.3f), ("Fire6_alyssa_eats", 17.2f), ("Fire7_alyssa_warms", 26f) })
        {
            while (al.RoundTime < at - 0.34f) al.Step(dt);
            LookAtHer(seat + new Vector3(2.6f, 0f, -2.4f));   // (20 frames: she steps on to the moment)
            // close, from across the corner of the fire (the camp camera stands behind you; this is the view walking up)
            cam.transform.position = seat + new Vector3(2.3f, 0.95f, -2.5f);
            cam.transform.LookAt(seat + new Vector3(0.85f, 0.45f, 0f));
            Times(file);
            Log($"Alyssa at {al.RoundTime:0.0} s: marshmallow {Marshmallow.Grade(al.Mallow.Toast)} ({al.Mallow.Toast:0.00}){(al.Mallow.Alight ? " alight" : "")}");
        }

        // ---- a selfie: held up in front, the peace sign, the flash; then with you standing close, turned to get you in
        {
            Vector3 near = seat + new Vector3(0.9f, 0f, -1.3f); near.y = Slope.DrawnHeight(near.x, near.z);
            camp.TeleportTo(near, Mathf.Atan2(seat.x - near.x, seat.z - near.z) * Mathf.Rad2Deg); Frames(10);
            int before = al.Selfies;
            al.TestSelfie(0);
            for (int i = 0; i < 139; i++) al.Step(dt);   // to the flash (2.3 s in)
            // from in front of her, on her left: her face turned to the phone up on her right, the peace sign by her cheek
            cam.transform.position = seat + new Vector3(1.55f, 1.0f, 1.25f);
            cam.transform.LookAt(seat + new Vector3(0.1f, 0.78f, -0.1f));
            Times("Fire30_selfie");
            // and as the phone sees her: from just behind it
            {
                Vector3 hd = al.HeadBone.position + Vector3.up * 0.12f, ph = al.Phone.position;
                // in front of her on the phone's side: her face turned to it (and so toward you), the peace sign by her cheek
                Vector3 fw = Vector3.right, rt = Vector3.Cross(Vector3.up, fw);   // (she faces +X across the fire)
                cam.transform.position = hd + fw * 1.35f + rt * 0.15f - Vector3.up * 0.08f;
                cam.transform.LookAt(hd + rt * 0.16f + Vector3.up * 0.02f);
                float fov = cam.fieldOfView; cam.fieldOfView = 70f;   // (a phone's wide front lens)
                Times("Fire30c_selfie_phone_view");
                cam.fieldOfView = fov;
            }
            Check(al.TakingSelfie && al.Selfies == before + 1, $"she takes a selfie (the flash went: {al.Selfies - before})");
            for (int i = 0; i < 70; i++) al.Step(dt);
            Times("Fire30b_selfie_checks_photo");
            for (int i = 0; i < 120; i++) al.Step(dt);
            Check(!al.TakingSelfie, "and puts the phone away");
            // you, standing beside her (on her right, a step behind): where a friend stands to get in a selfie
            Vector3 beside = seat + new Vector3(-0.15f, 0f, -0.95f); beside.y = Slope.DrawnHeight(beside.x, beside.z);
            camp.TeleportTo(beside, 90f); Frames(10);
            al.TestSelfie(2);
            for (int i = 0; i < 132; i++) al.Step(dt);
            {
                Vector3 hd = al.HeadBone.position + Vector3.up * 0.12f, ph = al.Phone.position;
                // the two of you from in front: her phone up, you beside her in its shot
                Vector3 mid = (hd + rider.transform.position + Vector3.up * 1.4f) * 0.5f;
                cam.transform.position = mid + Vector3.right * 2.5f + Vector3.up * 0.1f;
                cam.transform.LookAt(mid);
            }
            { float fov = cam.fieldOfView; cam.fieldOfView = 70f; Times("Fire31_selfie_with_you"); cam.fieldOfView = fov; }
            for (int i = 0; i < 200; i++) al.Step(dt);
            // never mid-conversation: talking to her puts it away
            al.TestSelfie(0); for (int i = 0; i < 80; i++) al.Step(dt);
            al.TalkingTo = rider.transform; for (int i = 0; i < 60; i++) al.Step(dt);
            Check(!al.TakingSelfie, "talking to her, the phone goes away");
            al.TalkingTo = null;
        }

        // ---- TALK: walk up to her, the prompt, open it with the button, tap through, pick a reply
        var talkSpot = al.TalkSpot; talkSpot.y = Slope.DrawnHeight(talkSpot.x, talkSpot.z);
        camp.TeleportTo(talkSpot + new Vector3(0f, 0f, -0.3f), Mathf.Atan2(seat.x - talkSpot.x, seat.z - talkSpot.z) * Mathf.Rad2Deg);
        Frames(20);
        Check(camp.Near == Camp.Station.Talk, $"TALK comes up beside her (near={camp.Near})");
        Shoot("Fire8_talk_prompt_day.png");
        Open(Camp.Station.Talk);
        Check(meta.TalkOpen && camp.Activity == "Talk", "the talk opens");
        Frames(40);
        Times("Fire9_talk_box");
        // her face, close, as she turns to talk to you
        {
            var hb = al.HeadBone; Vector3 toHero = al.FaceDir; toHero.y = 0f; toHero.Normalize();
            cam.transform.position = hb.position + toHero * 0.75f + Vector3.up * 0.12f + Vector3.Cross(Vector3.up, toHero) * 0.12f;
            cam.transform.LookAt(hb.position + Vector3.up * 0.1f);
            Times("Fire9b_alyssa_face");
            Check(al.FaceUncovered, "the hero's goggles and gaiter are folded off her face (the palette's swatches found)");
            // and at the camp's own distance: you, standing across the fire from her, the camp camera behind you
            meta.EndTalk(); al.TalkingTo = null;
            typeof(Camp).GetField("act", NP).SetValue(camp, System.Enum.ToObject(typeof(Camp).GetField("act", NP).FieldType, 0));
            Vector3 across = seat + Vector3.right * 4.0f + Vector3.forward * 2.2f; across.y = Slope.DrawnHeight(across.x, across.z);
            camp.TeleportTo(across, Mathf.Atan2(seat.x - across.x, seat.z - across.z) * Mathf.Rad2Deg);
            al.RoundTime = 23.5f; Frames(40);   // (warming her hands, face up to the fire)
            Times("Fire29_alyssa_face_camp");
            Open(Camp.Station.Talk); Frames(40);
            bool bare = false;
            foreach (var smr in al.GetComponentsInChildren<SkinnedMeshRenderer>(false)) if (smr.name.StartsWith("HeroHands")) bare = true;
            Check(!bare, "no bare hands under her gloves");
            var hand = al.GetComponentsInChildren<Transform>(true);
            Transform rh = null; foreach (var tr in hand) if (tr.name == "RightHand") rh = tr;
            if (rh != null)
            {
                cam.transform.position = rh.position + toHero * 0.55f + Vector3.up * 0.18f;
                cam.transform.LookAt(rh.position);
                Times("Fire9c_alyssa_hands");
            }
        }
        // taps: the first finishes typing (or goes on), never a jump
        for (int i = 0; i < 6 && meta.TalkOpen && meta.TalkRepliesShown == 0; i++) { Tap(); Frames(50); }
        Check(meta.TalkRepliesShown >= 2, $"her question offers replies ({meta.TalkRepliesShown})");
        Times("Fire10_talk_replies");
        // the conversations are taken in turn; whichever this is, find a reply that hands you a stick, if there is one
        int roastReply = -1;
        var conv = (CampTalk.Line[])typeof(MetaUi).GetField("talk", NP).GetValue(meta);
        var line = conv[meta.TalkLine];
        for (int k = 0; k < line.replies.Length; k++) if (line.replies[k].next == CampTalk.Roast) roastReply = k;
        meta.TalkReply(roastReply >= 0 ? roastReply : 0);
        for (int i = 0; i < 12 && meta.TalkOpen; i++)
        {
            Frames(70);
            if (meta.TalkRepliesShown > 0)
            {
                line = conv[meta.TalkLine]; int pick = 0;
                for (int k = 0; k < line.replies.Length; k++) if (line.replies[k].next == CampTalk.Roast) pick = k;
                meta.TalkReply(pick);
            }
            else Tap();
        }
        Frames(5);
        Check(!meta.TalkOpen, "the talk ends");
        Log($"talk ended in: {camp.Activity}");

        // ---- ROAST: from the talk, or from the stump's own prompt
        if (camp.Activity != "Roast")
        {
            var stand = camp.ChaletPoint(0f, 0f, 0f);   // (anywhere: then to the stump)
            Vector3 heroSeat = seat + Vector3.right * 3.5f;
            Vector3 rs = heroSeat + Vector3.right * 0.6f; rs.y = Slope.DrawnHeight(rs.x, rs.z);
            camp.TeleportTo(rs, -90f); Frames(20);
            Check(camp.Near == Camp.Station.Roast, $"ROAST A MARSHMALLOW at the free stump (near={camp.Near})");
            Shoot("Fire11_roast_prompt_day.png");
            Open(Camp.Station.Roast);
        }
        Check(camp.Activity == "Roast", "sat down to roast");
        Frames(60);   // sitting down
        Times("Fire12_roast_pale"); MallowClose(); Times("Fire12b_roast_pale_close");
        // over the embers, then down in the flames (a finger held), comparing how fast it browns
        float t0 = camp.HeroMallow.Toast; Frames(120); float slow = camp.HeroMallow.Toast - t0;
        Camp.TestHold = true; t0 = camp.HeroMallow.Toast; Frames(120); float fast = camp.HeroMallow.Toast - t0; Camp.TestHold = false;
        Check(fast > slow * 3f, $"held in the flames it browns faster ({fast:0.000} vs {slow:0.000} in 2 s)");
        while (camp.HeroMallow.Toast < 0.5f) Frames(10);
        Times("Fire13_roast_golden"); MallowClose(); Times("Fire13b_roast_golden_close");
        // down in the flames until it catches; tap to blow it out (a tap that must not jump)
        Camp.TestHold = true;
        for (int i = 0; i < 900 && !camp.HeroMallow.Alight; i++) Frames(1);
        Check(camp.HeroMallow.Alight, "left in the flames, it catches");
        Times("Fire14_roast_alight"); MallowClose(); Times("Fire14b_roast_alight_close");
        Camp.TestHold = false;
        Tap(); Frames(50);
        Check(!camp.HeroMallow.Alight, "a tap blows it out");
        Times("Fire15_roast_burnt"); MallowClose(); Times("Fire15b_roast_burnt_close");
        Log($"blown out at {camp.HeroMallow.Toast:0.00} ({Marshmallow.Grade(camp.HeroMallow.Toast)})");
        camp.TestTakeOut();
        Frames(70); Times("Fire16_roast_smore"); SmoreClose(); Times("Fire16b_roast_smore_close");
        Frames(130); Times("Fire17_roast_eating");
        Frames(200);
        Check(camp.Activity == "None", $"done and back on your feet ({camp.Activity})");
        Check(Progress.Points > points0, $"a few points for it ({Progress.Points - points0}, {camp.LastRoast})");
        Check(camp.HadSmore, "a s'more eaten");
        // and a golden one, for the record of what golden looks like close up
        camp.TeleportTo(new Vector3(seat.x + 4.1f, Slope.DrawnHeight(seat.x + 4.1f, seat.z), seat.z), -90f); Frames(10);
        Open(Camp.Station.Roast); Frames(60);
        while (camp.HeroMallow.Toast < Marshmallow.GoldenAt) Frames(5);
        MallowClose(); Times("Fire18_roast_golden_close");
        camp.TestTakeOut(); Frames(400);
        Log($"second one came out {camp.LastRoast}");

        // ---- TALK started in the middle of a jump: the jump ends where you stand
        {
            camp.TeleportTo(talkSpot + new Vector3(0f, 0f, -0.3f), Mathf.Atan2(seat.x - talkSpot.x, seat.z - talkSpot.z) * Mathf.Rad2Deg);
            Frames(10);
            camp.Jump(); Frames(30, false);
            float up = (float)typeof(Camp).GetField("jumpY", NP).GetValue(camp);
            Log($"mid-jump: {up:0.00} m up, move {(int)fMove.GetValue(camp)}");
            Open(Camp.Station.Talk); Frames(2);
            Check((int)fMove.GetValue(camp) == 0 && (float)typeof(Camp).GetField("jumpY", NP).GetValue(camp) == 0f, $"TALK from mid-jump ({up:0.00} m up): the jump ends, feet on the snow");
            Frames(30);
            Times("Fire28_talk_midjump");
            meta.EndTalk(); Frames(5);
        }

        // ---- HOT CHOCOLATE: in the kitchen, at the stove
        var cs = (Vector3)typeof(Camp).GetField("cocoaStand", NP).GetValue(camp);
        var cf = (Vector3)typeof(Camp).GetField("cocoaFace", NP).GetValue(camp);
        camp.TeleportTo(cs - cf * 0.3f, Mathf.Atan2(cf.x, cf.z) * Mathf.Rad2Deg); Frames(30);
        Check(camp.Near == Camp.Station.Cocoa, $"MAKE HOT CHOCOLATE at the stove (near={camp.Near})");
        Shoot("Fire19_cocoa_prompt_day.png");
        Open(Camp.Station.Cocoa);
        Frames(75); Times("Fire20_cocoa_pour");
        Frames(80); Times("Fire21_cocoa_stir");
        Frames(130); Times("Fire22_cocoa_marshmallows"); MugClose(); Times("Fire22b_cocoa_marshmallows_close");
        Frames(110);
        Check(camp.HoldingMug && camp.SipsLeft == Camp.Sips, $"a full mug in hand ({camp.SipsLeft} sips)");
        // out of the door and across the camp, walking (pushed right out: still a walk), steam trailing
        var door = camp.ChaletPoint(0f, 0f, 9f);
        camp.TeleportTo(door, camp.ChaletYaw); Frames(10);
        float runSpeed = 0f;
        for (int i = 0; i < 90; i++) { Hold(new Vector2(0f, 1f)); runSpeed = Mathf.Max(runSpeed, (float)typeof(Camp).GetField("speed", NP).GetValue(camp)); }
        Release();
        Check(runSpeed <= Camp.MaxSpeed + 0.05f, $"with a mug you walk, never run (top speed {runSpeed:0.0} m/s)");
        for (int i = 0; i < 30; i++) Hold(new Vector2(0f, 1f));
        Times("Fire23_mug_walking");
        FrontView(); Times("Fire23b_mug_walking_front");
        Release(); Frames(20);
        // sips: tap, tap ... each one a sip, none a jump; a swipe doesn't roll
        Tap(); Frames(30); Times("Fire24_mug_sip"); FrontView(); Times("Fire24b_mug_sip_front");
        Frames(40);
        Swipe(); Frames(30);
        Check((int)fMove.GetValue(camp) == 0, "a swipe with a mug doesn't roll");
        int sips = 1;
        for (int i = 0; i < 20 && camp.HoldingMug; i++) { Tap(); Frames(75); sips++; if (camp.SipsLeft == 3) { FrontView(); Times("Fire25_mug_low_front"); } }
        Frames(60);
        Check(!camp.HoldingMug, $"finished after {Camp.Sips} sips, the mug gone ({sips} taps)");
        // and dropping in with one: it's left on the gate's post
        camp.TeleportTo(cs - cf * 0.3f, Mathf.Atan2(cf.x, cf.z) * Mathf.Rad2Deg); Frames(10);
        Open(Camp.Station.Cocoa); Frames(400);
        Check(camp.HoldingMug, "a second mug");
        // roasting with it in hand: it's set down by the stump, and picked up again after
        {
            Vector3 rs = seat + Vector3.right * 4.1f; rs.y = Slope.DrawnHeight(rs.x, rs.z);
            camp.TeleportTo(rs, -90f); Frames(10);
            Open(Camp.Station.Roast); Frames(90);
            Check(camp.MugParked && !camp.HoldingMug, "roasting, the mug is down on the snow by the stump");
            var m = (Transform)typeof(Camp).GetField("mug", NP).GetValue(camp);
            Check(m != null && m.position.y - Slope.DrawnHeight(m.position.x, m.position.z) < 0.05f, $"standing on the snow ({(m != null ? m.position.y - Slope.DrawnHeight(m.position.x, m.position.z) : -1f):0.00} m up)");
            Times("Fire27_roast_with_mug");
            camp.TestTakeOut(); Frames(420);
            Check(camp.HoldingMug, "and back in hand after the s'more");
        }
        float cx = Slope.Centre(Slope.PadLip);
        camp.TeleportTo(new Vector3(cx, Slope.DrawnHeight(cx, Slope.GateZ - 2.6f), Slope.GateZ - 2.6f), 0f); Frames(20);
        Check(camp.Near == Camp.Station.DropIn, $"DROP IN at the gate with a mug (near={camp.Near})");
        Open(Camp.Station.DropIn);
        Check(camp.MugOnPost && !camp.HoldingMug, "the mug is left on the gate's post");
        camp.TeleportTo(new Vector3(cx + 2.2f, Slope.DrawnHeight(cx + 2.2f, Slope.GateZ - 5f), Slope.GateZ - 5f), -25f); Frames(20);
        Times("Fire26_mug_on_post");

        Check(jumps == 0, $"no tap or swipe jumped or rolled while talking, roasting or holding a mug ({jumps} frames moving)");

        // ---- RESTART (the end screen's RIDE AGAIN, the pause menu's RESTART): a new mountain, the camp rebuilt, a run.
        // The new Alyssa is built but kept still: her pose, IK and marshmallow don't run while you ride
        typeof(SummitGame).GetMethod("ToReady", NP, null, System.Type.EmptyTypes, null).Invoke(game, null);
        var al2 = camp.Alyssa;
        Check(al2 != null && al2 != al, "a new Alyssa for the new mountain");
        Check(al2 != null && !al2.isActiveAndEnabled, $"after RESTART she's still: no Update for her during the run (enabled={(al2 != null && al2.enabled)})");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        int steps0 = Alyssa.Steps;
        Physics.simulationMode = SimulationMode.Script;
        for (int i = 0; i < 240; i++) { TestHarness.Step(rider, body, dt); }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Check(Alyssa.Steps == steps0, $"four seconds of the run: she was moved on {Alyssa.Steps - steps0} times");
        Log($"riding at z={rider.Z:0} after the restart");
        Done();
    }

    // ---------------------------------------------------------------- driving it

    static void Open(Camp.Station st)
    {
        typeof(Camp).GetMethod("Open", NP).Invoke(camp, new object[] { st, meta });
    }

    /// <summary>Close on your marshmallow: from over your right shoulder, a little above and behind it.</summary>
    static void MallowClose()
    {
        var mm = camp.HeroMallow; if (mm == null) return;
        Vector3 tip = mm.Tip.position, along = (tip - mm.Stick.position).normalized; along.y = 0f; along.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, along);
        cam.transform.position = tip - along * 0.75f + side * 0.35f + Vector3.up * 0.3f;
        cam.transform.LookAt(tip - Vector3.up * 0.05f);
    }

    /// <summary>Close on the s'more in your left hand.</summary>
    static void SmoreClose()
    {
        var mm = camp.HeroMallow; if (mm == null || mm.Smore == null) return;
        Vector3 at = mm.Smore.position;
        cam.transform.position = at + new Vector3(0.45f, 0.35f, -0.45f);
        cam.transform.LookAt(at);
    }

    /// <summary>Close on the mug at the stove, looking into it.</summary>
    static void MugClose()
    {
        var m = (Transform)typeof(Camp).GetField("workMug", NP).GetValue(camp);
        if (m == null) return;
        cam.transform.position = m.position + new Vector3(0f, 0.55f, 0f) - m.forward * 0.35f + m.right * 0.15f;
        cam.transform.LookAt(m.position + Vector3.up * 0.08f);
    }

    /// <summary>The camera out in front of you, a little to your right: the mug and its steam, your face.</summary>
    static void FrontView()
    {
        float y = camp.WalkerYaw * Mathf.Deg2Rad;
        Vector3 f = new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y)), r = Vector3.Cross(Vector3.up, f), p = rider.transform.position;
        cam.transform.position = p + f * 1.9f + r * 0.9f + Vector3.up * 1.2f;
        cam.transform.LookAt(p + Vector3.up * 0.95f + r * 0.15f);
    }

    static Vector2 Screen => new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.72f);

    /// <summary>A frame of the camp as the game runs it: its input and walking, the body, the camera, Alyssa.</summary>
    static void Frame(bool checkStill = true)
    {
        bool busy = camp.Busy || camp.HoldingMug;
        camp.Step(dt, meta);
        if (checkStill && busy && (int)fMove.GetValue(camp) != 0) jumps++;
        body.Apply(dt);
        camp.CameraStep(dt);
        if (camp.Alyssa != null) camp.Alyssa.Step(dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); aLate.Invoke(atmo, null);
    }

    static void Frames(int n, bool checkStill = true) { for (int i = 0; i < n; i++) Frame(checkStill); }

    static void Tap() { touches.Clear(); touches.Add(new CampInput.Touch(7, Screen)); Frame(); touches.Clear(); Frame(); }

    static void Swipe()
    {
        touches.Clear();
        for (int i = 0; i < 6; i++) { touches.Clear(); touches.Add(new CampInput.Touch(8, Screen + new Vector2(0f, -i * cam.pixelHeight * 0.05f))); Frame(); }
        touches.Clear(); Frame();
    }

    /// <summary>The move stick held over (x, y) of its throw.</summary>
    static void Hold(Vector2 stick)
    {
        var p0 = camp.Controls.Home ?? new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.3f);   // (the walk stick, fixed bottom left)
        float r = Mathf.Max(60f, Mathf.Min(cam.pixelWidth, cam.pixelHeight) * 0.14f);
        touches.Clear();
        if (!holding) { touches.Add(new CampInput.Touch(9, p0)); Frame(); holding = true; }
        touches.Clear(); touches.Add(new CampInput.Touch(9, p0 + stick * r)); Frame();
    }
    static bool holding;
    static void Release() { touches.Clear(); Frame(); holding = false; }

    static void Times(string file)
    {
        // (particles don't run in batch: bring the little flames and the steam up to a running state for the picture)
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.isPlaying && !ps.transform.IsChildOf(camp.transform.Find("Camp") ?? camp.transform) || ps.name == "MallowFlame" || ps.name == "Steam")
                ps.Simulate(0.5f, true, false);
        foreach (var (name, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
        {
            TimeOfDay.TestClock = clock; dayNight.ApplyNow();
            Shoot($"{file}_{name}.png");
        }
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
    }

    static void Check(bool ok, string what) { if (!ok) fails++; Debug.Log($"FIRE {(ok ? "ok  " : "FAIL")} {what}"); }
    static void Log(string s) => Debug.Log("FIRE " + s);

    static void Done()
    {
        Camp.TestTouches = null; Camp.TestHold = false;
        ChaseCamera.TestDt = 0f;
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow(); TimeOfDay.TestClock = -1f;
        Debug.Log($"FIRE done: {fails} failure(s)");
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    static void Shoot(string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        Canvas.ForceUpdateCanvases();
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        int mask = cam.cullingMask; cam.cullingMask |= 1 << meta.gameObject.layer;
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g); off.Add(sk);
        }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in off) sk.enabled = true;
        cam.cullingMask = mask;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
    }
}
