using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// After a fall the game waits for the player's tap, and a tap carries on at once (ResumeTap). On the mountain,
/// through the game's own crash handling (SummitGame.CrashTapStep) with scripted fingers:
/// - an ordinary crash left alone for 15 s: still down, the camera's rider still on the body;
/// - a finger already down at the crash (steering) lifting, and a long drag (steering the tumble): not a tap;
/// - a tap 0.5 s in, the body still tumbling: up within a few frames;
/// - the run's last fall left alone for 15 s: still on the mountain, not the end screen; a tap mid-tumble: the end;
/// - a tap while the fall isn't decided yet (still dropping): waits, then gets up once it is.
/// In the camp, off the edge (Camp.Collapse): left alone 15 s, still watching the fall; the walking thumb lifting
/// isn't a tap; a tap mid-fall: back at the chalet.
/// Lines start TAPRESUME; exit 1 on a failure.
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod TapResumeTest.Run -logFile tap.log
/// </summary>
public static class TapResumeTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static int fails, checks;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;

    static void Check(string what, bool ok, string got = "")
    {
        checks++; if (!ok) fails++;
        Debug.Log($"TAPRESUME {(ok ? "PASS" : "FAIL")} {what} {got}");
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        RiderInput.TestDrive = true; RiderInput.TestFingers = 0;
        try { Mountain(); Camp(); }
        catch (System.Exception e) { Debug.LogError("TAPRESUME exception " + e); fails++; }
        RiderInput.TestDrive = false; RiderInput.TestFingers = 0;
        Summit.Camp.TestTouches = null;
        Debug.Log(fails == 0 ? $"TAPRESUME PASS ({checks} checks)" : $"TAPRESUME FAIL ({fails} of {checks})");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    static string State() => typeof(SummitGame).GetField("state", NP).GetValue(game).ToString();
    static Vector2 centre = new Vector2(400f, 800f);

    // ---- the mountain

    /// <summary>One frame of a crash as the game runs it (physics, the body, the game's tap handling). False once out of riding.</summary>
    static bool Frame(int fingers, Vector2 pos, bool holdFall = false)
    {
        RiderInput.TestFingers = fingers; RiderInput.TestPos = pos;
        for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
        body.Apply(Dt);
        if (holdFall && rider.Crashed) rider.HoldCrash = true;   // (a fall not decided yet, as a long drop is)
        return !game.CrashTapStep(Dt) && State() == "Riding";
    }

    static void Throw(float v0, bool lastFall, bool fingerDown = false)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float z = 320f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.forward * v0;
        terrain.Prewarm(); props.Prewarm();
        for (int i = 0; i < 20; i++) { rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); }
        if (lastFall)
        {
            int max = (int)typeof(SummitGame).GetField("MaxFalls", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public).GetValue(null);
            typeof(SummitGame).GetField("falls", NP).SetValue(game, Summit.Progress.Falls(max) - 1);
        }
        RiderInput.TestFingers = fingerDown ? 1 : 0; RiderInput.TestPos = centre;
        rider.DoCrash("Tree!");   // (the game hears it: OnCrash)
    }

    static void Mountain()
    {
        // left alone: down for good, the camera's rider on the body
        Throw(18f, false);
        bool still = true; float far = 0f;
        for (float t = 0f; t < 15f; t += Dt)
        {
            still &= Frame(0, centre) && rider.Crashed;
            if (rider.Crashed && body.RagdollForTest.Active)
            {
                Vector3 a = rider.transform.position, b = body.RagdollCentre; a.y = b.y = 0f;
                if (t > 1f) far = Mathf.Max(far, (a - b).magnitude);
            }
        }
        Check("a crash left alone 15 s stays down (never gets up by itself)", still && rider.Crashed, $"crashed={rider.Crashed}");
        Check("the camera's rider stays with the body all the while", rider.HasCrashFollow && far < 1.5f, $"follow={rider.HasCrashFollow} farthest {far:0.00} m");
        // a tap now: up
        for (int i = 0; i < 6; i++) Frame(1, centre);
        for (int i = 0; i < 6 && rider.Crashed; i++) Frame(0, centre);
        Check("a tap after watching it: up", !rider.Crashed);

        // a finger down at the crash (steering) lifting, then a long steering drag: neither is a tap
        Throw(18f, false, true);
        for (int i = 0; i < 30; i++) Frame(1, centre);
        for (int i = 0; i < 30; i++) Frame(0, centre);
        bool afterLift = rider.Crashed;
        for (int i = 0; i < 90; i++) Frame(1, centre + Vector2.right * (i * 4f));
        for (int i = 0; i < 20; i++) Frame(0, centre);
        Check("the steering finger lifting, and a long drag: still down", afterLift && rider.Crashed);
        for (int i = 0; i < 4; i++) Frame(1, centre);
        for (int i = 0; i < 6 && rider.Crashed; i++) Frame(0, centre);
        Check("then a tap: up", !rider.Crashed);

        // a tap mid-tumble: up at once, body still going
        Throw(30f, false);
        for (int i = 0; i < 30; i++) Frame(0, centre);   // half a second
        float speed = body.RagdollSpeed;
        for (int i = 0; i < 5; i++) Frame(1, centre);
        int frames = 0;
        for (; frames < 10 && rider.Crashed; frames++) Frame(0, centre);
        Check("a tap 0.5 s into a 30 m/s tumble: up at once", !rider.Crashed && frames <= 2, $"body at {speed:0.0} m/s when tapped, up after {frames} frame(s)");

        // the run's last fall: no end screen by itself, a tap mid-tumble ends it
        Throw(18f, true);
        still = true;
        for (float t = 0f; t < 15f && still; t += Dt) still = Frame(0, centre);
        Check("the last fall left alone 15 s: still watching the body (no end screen)", still && rider.Crashed && rider.StayDown, $"state={State()} stayDown={rider.StayDown}");
        Throw(18f, true);
        for (int i = 0; i < 24; i++) Frame(0, centre);
        bool tumbling = body.RagdollMoving;
        bool riding = true;
        for (int i = 0; i < 5; i++) riding &= Frame(1, centre);
        for (int i = 0; i < 3 && riding; i++) riding = Frame(0, centre);
        Check("a tap during the last fall's tumble: on to the end screen at once", !riding && State() == "Buried", $"state={State()}, body moving when tapped={tumbling}");

        // a tap while the fall isn't decided: it waits, then gets up once it is
        Throw(18f, false);
        for (int i = 0; i < 20; i++) Frame(0, centre, true);
        for (int i = 0; i < 5; i++) Frame(1, centre, true);
        for (int i = 0; i < 40; i++) Frame(0, centre, true);
        bool waited = rider.Crashed && game.CrashTapQueued;
        for (int i = 0; i < 10 && rider.Crashed; i++) Frame(0, centre);
        Check("a tap while still dropping waits for the landing, then gets up", waited && !rider.Crashed, $"waited={waited} up={!rider.Crashed}");
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
    }

    // ---- the camp

    static void Camp()
    {
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        var camp = (Summit.Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
        var meta = (MetaUi)typeof(SummitGame).GetField("meta", NP).GetValue(game);
        var list = new List<CampInput.Touch>();
        ChaseCamera.TestDt = Dt;
        void Step(int fingers)
        {
            list.Clear(); if (fingers > 0) list.Add(new CampInput.Touch(7, centre));
            Summit.Camp.TestTouches = list;
            camp.Step(Dt, meta);
            for (int k = 0; k < 2; k++) { body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
            body.Apply(Dt); camp.CameraStep(Dt);
        }
        foreach (bool thumbDown in new[] { false, true })
        {
            camp.Teleport(21.8f, -117f, 90f);
            for (int i = 0; i < 20; i++) Step(0);
            Summit.Camp.TestTouches = thumbDown ? new List<CampInput.Touch> { new CampInput.Touch(7, centre) } : new List<CampInput.Touch>();
            camp.Collapse(Vector3.right);
            Check($"camp: off the edge{(thumbDown ? " (the walking thumb down)" : "")}", camp.Falling);
            bool still = true;
            for (float t = 0f; t < 15f; t += Dt) { Step(thumbDown && t < 0.5f ? 1 : 0); still &= camp.Falling && !camp.FallTapped; }
            Check($"camp: left alone 15 s, still watching the fall{(thumbDown ? " (the thumb lifting isn't a tap)" : "")}", still);
            for (int i = 0; i < 5; i++) Step(1);
            int n = 0;
            for (; n < 90 && camp.Falling; n++) Step(0);
            Check("camp: a tap, back at the chalet", !camp.Falling, $"after {n} frames");
        }
        // a tap mid-fall: straight back
        camp.Teleport(21.8f, -117f, 90f);
        for (int i = 0; i < 20; i++) Step(0);
        Summit.Camp.TestTouches = new List<CampInput.Touch>();
        camp.Collapse(Vector3.right);
        for (int i = 0; i < 30; i++) Step(0);
        bool moving = body.RagdollMoving;
        for (int i = 0; i < 5; i++) Step(1);
        int m = 0;
        for (; m < 90 && camp.Falling; m++) Step(0);
        Check("camp: a tap mid-fall (half a second in): back at once", !camp.Falling && m <= 30, $"body moving when tapped={moving}, back after {m} frames (the fade)");
        ChaseCamera.TestDt = 0f;
    }
}
