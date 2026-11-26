using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (lifttop) The hero's arrival on the Summit lift (LiftArrive), played as the game plays it (SummitGame.ToHub with
/// the arrival asked for, then its frames): it plays (a chair, the hero alone on it, standing up in the station,
/// gliding off and stopping on the bench, in about ten seconds); a tap skips it; with it switched off, and with it
/// failing half way, it falls back. Every way it must end with the hero on foot in the camp, standing on the snow of
/// the bench or its way in, with the camp's controls: a push on the stick walks them. Logs LIFTARRIVE lines; exits 1 on
/// any failure. Batch: -executeMethod LiftArriveTest.Run (no graphics needed).
/// </summary>
public static class LiftArriveTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Camp camp; static Rider rider; static RiderBody body; static SkiLift lift;
    static MethodInfo toHub, arriveStep, liftUpd;
    static FieldInfo stateF, arriveNextF, arriveF;
    static int fails;
    static double clock = 900.0;
    const float dt = 1f / 30f;

    static void Fail(string s) { fails++; Debug.Log("LIFTARRIVE-FAIL " + s); }
    static void Log(string s) => Debug.Log("LIFTARRIVE " + s);

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        camp = Get<Camp>("camp"); rider = Get<Rider>("rider"); body = Get<RiderBody>("body");
        lift = Object.FindFirstObjectByType<SkiLift>();
        toHub = typeof(SummitGame).GetMethod("ToHub", NP);
        arriveStep = typeof(SummitGame).GetMethod("ArriveStep", NP);
        liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        stateF = typeof(SummitGame).GetField("state", NP); arriveNextF = typeof(SummitGame).GetField("arriveNext", NP); arriveF = typeof(SummitGame).GetField("arrive", NP);
        ChaseCamera.TestDt = dt;
        try
        {
            Play("plays", 0f, false);
            Play("plays on skis", 0f, false, 0f, false, true);
            Play("skipped", 2.0f, false);
            Play("fails half way", 0f, false, 3.0f);
            Play("off", 0f, true);
            Play("plays again (a fresh mountain)", 0f, false, 0f, true);
        }
        catch (System.Exception e) { Fail("exception " + e); }
        ChaseCamera.TestDt = 0f; SkiLift.TestClock = -1.0; LiftArrive.On = true; LiftArrive.TestFailAt = 0f;
        TestPrefs.Restore();
        Log($"done: {fails} failure(s)");
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static string State() => stateF.GetValue(game).ToString();

    /// <summary>Into the camp on the lift; tap at `tapAt` s (0: never); fail on purpose at `failAt`; with it off; on a new mountain.</summary>
    static void Play(string name, float tapAt, bool off, float failAt = 0f, bool fresh = false, bool skis = false)
    {
        LiftArrive.On = !off; LiftArrive.TestFailAt = failAt;
        var rideWas = body.Ride;
        body.SetRide(skis ? RideKind.Skis : RideKind.Snowboard);
        var arrive = (LiftArrive)arriveF.GetValue(game);
        arriveNextF.SetValue(game, true);
        SkiLift.TestClock = clock;
        toHub.Invoke(game, new object[] { fresh });
        string first = State();
        if (off)
        {
            if (first != "Hub") Fail($"{name}: switched off, it should go straight into the camp, but the game is in {first}");
        }
        else if (first != "Arrive") { Fail($"{name}: it didn't start ({arrive.Why}); the game is in {first}"); }
        var phases = new List<string>();
        float t = 0f; bool ended = first == "Hub", tapped = false;
        int heroChairRiders = 0, framesOnChair = 0;
        // (lifttop2) pushing off the deck: on a board the back foot out beside it (its farthest off the line through
        // the front foot along the way); on skis the hands' swing along the way (planting the poles and pushing back)
        float footOut = 0f, handMin = float.MaxValue, handMax = float.MinValue, glideT = 0f;
        Vector3 prevHero = rider.transform.position;
        Vector3 lastHero = rider.transform.position;
        float maxJump = 0f;
        while (!ended && t < 25f)
        {
            t += dt; clock += dt; SkiLift.TestClock = clock;
            liftUpd.Invoke(lift, null);
            if (LiftEnds.Instance != null) LiftEnds.Instance.Step(dt, rider.transform.position);
            bool tap = tapAt > 0f && t >= tapAt && !tapped; if (tap) tapped = true;
            ended = (bool)arriveStep.Invoke(game, new object[] { dt, tap });
            body.Apply(dt);
            string ph = arrive.Now.ToString();
            if (phases.Count == 0 || phases[phases.Count - 1] != ph) phases.Add(ph);
            if (arrive.Now == LiftArrive.Phase.Ride)
            {
                framesOnChair++;
                // nobody else on the hero's chair (LiftRiders draws none there)
                var list = new List<(Matrix4x4 chair, int seat, int pose, PersonLook look, Person near)>();
                LiftRiders.Riders(list);
                var seatAt = rider.transform.position;
                foreach (var r in list) if (((Vector3)r.chair.GetColumn(3) - seatAt).magnitude < 4f) heroChairRiders++;
            }
            var hp = rider.transform.position;
            if (arrive.Now == LiftArrive.Phase.Glide && body.Hero != null && body.Hero.Ok)
            {
                glideT += dt;
                var way = hp - prevHero; way.y = 0f;
                if (glideT < 2.2f && way.sqrMagnitude > 1e-6f)
                {
                    way.Normalize();
                    var h = body.Hero; var across = Vector3.Cross(Vector3.up, way);
                    if (!skis)
                    {
                        int back = Vector3.Dot(h.Foot[0].position - h.Foot[1].position, way) < 0f ? 0 : 1;
                        footOut = Mathf.Max(footOut, Mathf.Abs(Vector3.Dot(h.Foot[back].position - h.Foot[1 - back].position, across)));
                    }
                    else
                        for (int sd = 0; sd < 2; sd++) { float f = Vector3.Dot(h.Hand[sd].position - hp, way); handMin = Mathf.Min(handMin, f); handMax = Mathf.Max(handMax, f); }
                }
            }
            prevHero = hp;
            if (!ended && t > dt * 2f) maxJump = Mathf.Max(maxJump, (hp - lastHero).magnitude / dt);
            lastHero = hp;
        }
        string st = State();
        Log($"{name}: {(first == "Arrive" ? $"{t:0.0} s" : "not played")}, phases {string.Join(" > ", phases)}, ended in {st}; {(arrive.Skipped ? "skipped; " : "")}{(arrive.Why != "" ? "why: " + arrive.Why + "; " : "")}fastest the hero moved {maxJump:0.0} m/s");
        if (!ended) Fail($"{name}: never ended (after {t:0.0} s)");
        if (st != "Hub") Fail($"{name}: ended in {st}, not the camp");
        if (!camp.Active) Fail($"{name}: the camp isn't active");
        if (!body.Walking) Fail($"{name}: the hero isn't on foot");
        if (body.CampPose == (RiderBody.ICampPose)arrive) Fail($"{name}: the arrival still poses the hero");
        if (LiftRiders.HeroChair >= 0) Fail($"{name}: the hero's chair is still kept empty");
        if (heroChairRiders > 0) Fail($"{name}: {heroChairRiders} frames with someone else on the hero's chair");
        if (!off && failAt <= 0f && tapAt <= 0f)
        {
            if (t < 7f || t > 13.5f) Fail($"{name}: it took {t:0.0} s (wanted about 8 to 12)");
            foreach (var want in new[] { "Ride", "Stand", "Glide", "Stop" }) if (!phases.Contains(want)) Fail($"{name}: never {want}");
            if (maxJump > 12f) Fail($"{name}: the hero jumped about ({maxJump:0} m/s)");
            if (framesOnChair < (LiftArrive.RideTime - 0.2f) * 30f) Fail($"{name}: only {framesOnChair} frames on the chair");
            if (!skis)
            {
                Log($"{name}: pushing off, the back foot out to {footOut:0.00} m beside the board");
                if (footOut < 0.18f) Fail($"{name}: the back foot never came off the board to push ({footOut:0.00} m)");
            }
            else
            {
                float swing = handMax - handMin;
                Log($"{name}: pushing off, the hands swung {swing:0.00} m along the way (planting the poles)");
                if (swing < 0.35f) Fail($"{name}: no push on the poles (the hands swung {swing:0.00} m)");
            }
        }
        if (tapAt > 0f && t > tapAt + 0.2f) Fail($"{name}: the tap at {tapAt:0.0} s didn't end it (ended at {t:0.0} s)");
        // standing on the snow of the bench or its way in, where it should be
        var feet = camp.WalkerPosition;
        float g = Slope.DrawnHeight(feet.x, feet.z);
        bool onTop = LiftTop.Covers(feet.x, feet.z, 1f);
        Log($"{name}: the hero at ({feet.x - Slope.Centre(Slope.PadLip):0.0}, {feet.z:0.0}), {feet.y - g:+0.00;-0.00} m over the snow, {(onTop ? "on the bench or its way in" : "OFF the bench")}, facing {camp.WalkerYaw:0}");
        if (Mathf.Abs(feet.y - g) > 0.35f) Fail($"{name}: not on the snow ({feet.y - g:0.00} m)");
        if (!onTop) Fail($"{name}: not on the bench or its way in");
        // the controls are the camp's: a push on the stick walks them
        var before = camp.WalkerPosition;
        for (int i = 0; i < 45; i++) { camp.Walk(new Vector2(0f, 1f), dt); body.Apply(dt); camp.CameraStep(dt); }
        float moved = Vector2.Distance(new Vector2(before.x, before.z), new Vector2(camp.WalkerPosition.x, camp.WalkerPosition.z));
        Log($"{name}: walked {moved:0.0} m in 1.5 s");
        if (moved < 2f) Fail($"{name}: no control afterwards (walked {moved:0.0} m)");
        LiftArrive.On = true; LiftArrive.TestFailAt = 0f;
        body.SetRide(rideWas);
    }
}
