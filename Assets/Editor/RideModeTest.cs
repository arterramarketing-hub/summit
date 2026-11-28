using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// (rides) The fun rides, each as the game plays it: the kayak, the rally car, the bear, the mountain bike (mtb).
/// - Picked as the closet picks it (Progress.PickRide): the rider's physics and body both on it, its gear showing,
///   the board not.
/// - A whole run from the start gate to the finish with a simple autopilot (aiming down the run's middle, weaving):
///   it never sticks (some ground made every 20 s), never sinks through the snow (the rider's sphere and the
///   vehicle on it), and every fall on the way is got up from with a tap (SummitGame.CrashTapStep).
/// - A crash on purpose, and a tap: thrown (the ragdoll; the hull, the car or the paddle flying off, the bear
///   running on by itself), then up again within a few frames with everything back under the rider, riding on.
/// - Each one's own: the kayak strokes (more on the outside of a turn) and spins out thrown from one way to the
///   other at speed; the car keeps itself going on the flat (its engine), and thrown hard across at speed leans on
///   its springs but stays on its wheels (rides2: it used to roll over); the bear gallops (its paws) and jumps big;
///   the bike pedals itself on where it is slow (the cranks go round with the feet on them), coasts at speed (the
///   freewheel), skids braked into a hard turn, hops, and keeps the hands on its grips as the bar steers.
/// Lines start RIDES; exits 1 on any failure. With $RIDES_SHOTS (and graphics) it writes pictures of each in action.
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod RideModeTest.Run -logFile rides.log
/// </summary>
public static class RideModeTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static int fails, checks;
    static SummitGame game; static Summit.Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static Camera cam;
    static string shots;
    static int strokes, strokesOut, spins, paws, crashCount; static string lastCrash = "";
    static readonly Vector2 centre = new Vector2(400f, 800f);

    static void Check(string what, bool ok, string got = "")
    {
        checks++; if (!ok) fails++;
        Debug.Log($"RIDES {(ok ? "PASS" : "FAIL")} {what} {got}");
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        int rideWas = (int)Progress.Ride;
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        rider = game.rider; body = game.body;
        terrain = game.terrain; props = game.props;
        cam = Camera.main;
        shots = System.Environment.GetEnvironmentVariable("RIDES_SHOTS");
        if (!string.IsNullOrEmpty(shots)) Directory.CreateDirectory(shots);
        RiderInput.TestDrive = true; RiderInput.TestFingers = 0;
        rider.Stroke += side => { strokes++; if (side != 0 && Mathf.Sign(rider.Steer) == side) strokesOut++; };
        rider.SpunOut += sp => spins++;
        rider.Pawfall += () => paws++;
        rider.Crash += why => { lastCrash = why; crashCount++; };
        try
        {
            var only = System.Environment.GetEnvironmentVariable("RIDES_ONLY");   // (one of them: Kayak, Car, Bear or Bike)
            foreach (var k in new[] { RideKind.Kayak, RideKind.Car, RideKind.Bear, RideKind.Bike }) if (string.IsNullOrEmpty(only) || only == k.ToString()) OneRide(k);
            // and back on a board: nothing of the fun rides left showing
            Progress.PickRide(RideKind.Snowboard);
            ToStart();
            Check("back on the board: no kayak, car, bear or bike showing", !Shown(body.KayakGear) && !Shown(body.CarGear) && !Shown(body.BearGear) && !Shown(body.BikeGear) && Shown(body.BoardGear) && rider.Ride == RideKind.Snowboard);
        }
        catch (System.Exception e) { Debug.LogError("RIDES exception " + e); fails++; }
        RiderInput.TestDrive = false; RiderInput.TestFingers = 0;
        Progress.PickRide((RideKind)rideWas);
        Debug.Log(fails == 0 ? $"RIDES PASS ({checks} checks)" : $"RIDES FAIL ({fails} of {checks})");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    static bool Shown(Transform t) => t != null && t.gameObject.activeInHierarchy;
    static Transform Gear(RideKind k) => k == RideKind.Kayak ? body.KayakGear : k == RideKind.Car ? body.CarGear : k == RideKind.Bike ? body.BikeGear : body.BearGear;
    static string GearPart(RideKind k) => k == RideKind.Kayak ? "Hull" : k == RideKind.Bike ? "Frame" : "Body";
    static string State() => typeof(SummitGame).GetField("state", NP).GetValue(game).ToString();

    /// <summary>Onto the start pad and away, as PLAY does: a new run from the gate.</summary>
    static void ToStart()
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        for (int i = 0; i < 10; i++) { body.Apply(Dt); }
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        terrain.Prewarm(); props.Prewarm();
    }

    /// <summary>One frame of riding as the game steps it (two physics steps, the body, the game's tap handling).</summary>
    static void Frame(float steer, int fingers = 0)
    {
        rider.Steer = steer;
        RiderInput.TestFingers = fingers; RiderInput.TestPos = centre;
        for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
        body.Apply(Dt);
        game.CrashTapStep(Dt);
        typeof(SummitGame).GetField("falls", NP).SetValue(game, 0);   // (never the last fall: the whole run is ridden)
    }

    /// <summary>A tap, as the player gives one: down for a few frames, then up.</summary>
    static int TapUp()
    {
        // (a tap waits while the fall isn't decided yet, as the game's does: the player taps once it can get up)
        for (int i = 0; i < 600 && rider.Crashed && !rider.CanGetUp; i++) Frame(0f, 0);
        for (int i = 0; i < 4; i++) Frame(0f, 1);
        int n = 0;
        for (; n < 30 && rider.Crashed; n++) Frame(0f, 0);
        return n;
    }

    /// <summary>The autopilot: down the run's middle 30 m ahead, with a slow weave across it.</summary>
    static float Pilot(float t)
    {
        float z = rider.Z, x = rider.X;
        float aim = Slope.Centre(z + 30f) + 6f * Mathf.Sin(t * 0.45f);
        float want = Mathf.Atan2(aim - x, 30f);
        return Mathf.Clamp((want - rider.TravelHeading) * 2.5f, -1f, 1f);
    }

    static void OneRide(RideKind k)
    {
        string n = Rides.Of(k).Name;
        strokes = strokesOut = spins = paws = 0; crashCount = 0;
        // ---- picked as the closet picks it
        Progress.PickRide(k);
        Check($"{n}: picked, the rider is on it", rider.Ride == k && body.Ride == k, $"rider {rider.Ride} body {body.Ride}");
        ToStart();
        if (shots != null && System.Environment.GetEnvironmentVariable("RIDES_CLOSE") == "1")
        {
            // close looks at it standing on the start pad: from the side, the front, above
            typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
            for (int i = 0; i < 30; i++) body.Apply(Dt);
            var p0 = rider.transform.position;
            Shoot($"{k}_0a_side.png", new View { at = p0 + Vector3.right * 3.2f + Vector3.up * 1.0f, look = p0 + Vector3.up * 0.5f });
            Shoot($"{k}_0b_front.png", new View { at = p0 + Vector3.forward * 3.5f + Vector3.up * 1.2f + Vector3.right * 0.6f, look = p0 + Vector3.up * 0.5f });
            Shoot($"{k}_0c_above.png", new View { at = p0 + Vector3.up * 4.5f + Vector3.forward * 0.3f, look = p0 });
            ToStart();
        }
        Check($"{n}: its gear shows and the board doesn't", Shown(Gear(k)) && !Shown(body.BoardGear), $"gear {Shown(Gear(k))} board {Shown(body.BoardGear)}");
        Check($"{n}: the run starts", State() == "Riding" && rider.Running, State());

        // ---- the whole run, from the gate to the finish
        float t = 0f, lastZ = rider.Z, lastT = 0f, worstSink = 0f, worstGear = 0f, maxSpeed = 0f, warm = 0f;
        int ups = 0, crashFrames = 0; bool stuck = false, finished = false; string sinkAt = "", gearAt = "";
        int shot = 0; float shotAt = 5f;
        while (t < 420f)
        {
            float steer = Pilot(t);
            if (rider.Crashed)
            {
                crashFrames++;
                Frame(0f);
                if (crashFrames > 45 && rider.CanGetUp) { int f = TapUp(); if (!rider.Crashed) ups++; crashFrames = 0; t += f * Dt; }
                t += Dt;
                continue;
            }
            Frame(steer);
            t += Dt;
            maxSpeed = Mathf.Max(maxSpeed, rider.Speed);
            Vector3 p = rider.Body.position;
            // never through the snow: the rider's sphere on it (unless over a crevasse's opening, falling in)
            if (!rider.Crashed && Course.Solid(p.x, p.z, rider.Speed))
            {
                float sink = Slope.DrawnHeight(p.x, p.z) + Summit.Rider.R - p.y;
                if (sink > worstSink) { worstSink = sink; sinkAt = $"z {p.z:0} speed {rider.Speed:0.0} air {rider.Airborne}"; }
                // the vehicle on the snow: its root no deeper than a few centimetres under it
                var g = Gear(k);
                if (g != null && !rider.Airborne)
                {
                    Vector3 gp = k == RideKind.Bear ? g.position : g.position;
                    float gs = Slope.DrawnHeight(gp.x, gp.z) - gp.y;
                    if (gs > worstGear) { worstGear = gs; gearAt = $"z {gp.z:0}"; }
                }
            }
            if (rider.Z > lastZ + 15f) { lastZ = rider.Z; lastT = t; }
            else if (t - lastT > 20f) { stuck = true; break; }
            if (rider.Z > Slope.FinishZ + 30f) { finished = true; break; }
            warm += Dt;
            if (warm > 1f) { warm = 0f; terrain.Prewarm(); props.Prewarm(); }
            if (shots != null && t > shotAt && shot < 3 && !rider.Crashed)
            {
                if (shot == 0) Shoot($"{k}_1_riding.png", Chase());
                else if (shot == 1 && (k != RideKind.Kayak || rider.PaddleSide != 0)) Shoot($"{k}_2_side.png", Side(4.6f));
                else if (shot == 2 && rider.Airborne) Shoot($"{k}_3_air.png", Side(6f));
                else if (shot == 1 || shot == 2) { continue; }
                shot++; shotAt = t + (shot == 2 ? 0.1f : 6f);
            }
        }
        Debug.Log($"RIDES {n}: run {t:0.0} s, to z {rider.Z:0} of {Slope.FinishZ:0}, top speed {maxSpeed * 3.6f:0} km/h, {crashCount} falls ({lastCrash}), {ups} got up, strokes {strokes} ({strokesOut} on the outside), spin-outs {spins}, paws {paws}");
        Check($"{n}: never stuck", !stuck, $"stuck at z {rider.Z:0} after {t - lastT:0.0} s");
        Check($"{n}: the whole run, to the finish", finished, $"z {rider.Z:0} of {Slope.FinishZ:0}");
        Check($"{n}: never through the snow", worstSink < 0.35f, $"worst {worstSink:0.00} m ({sinkAt})");
        Check($"{n}: its gear never under the snow", worstGear < 0.3f, $"worst {worstGear:0.00} m ({gearAt})");
        Check($"{n}: every fall got up from", crashCount == 0 || ups >= crashCount - (rider.Crashed ? 1 : 0), $"{crashCount} falls, {ups} up");
        if (k == RideKind.Kayak) Check("KAYAK: it paddles (strokes, mostly on the outside of a turn)", strokes > 20 && strokesOut > strokes / 3, $"{strokes} strokes, {strokesOut} outside");
        if (k == RideKind.Bear) Check("BEAR: it gallops (its paws)", paws > 100, $"{paws}");

        // ---- a crash on purpose, and a tap
        ToStart();
        for (int i = 0; i < 360 && rider.Speed < 15f; i++) Frame(Pilot(i * Dt));
        for (int i = 0; i < 30; i++) Frame(0f);
        Vector3 before = rider.Body.position;
        rider.DoCrash("Test spill!");
        for (int i = 0; i < 12; i++) Frame(0f);
        var g0 = Gear(k);
        bool thrown = body.RagdollForTest != null && body.RagdollForTest.Active;
        bool gearOff = k == RideKind.Bear ? body.BearLoose : FindChild(g0, GearPart(k)) == null;
        Check($"{n}: thrown in a crash (the ragdoll)", rider.Crashed && thrown, $"crashed {rider.Crashed} ragdoll {thrown}");
        Check($"{n}: its gear comes off (the bear runs on by itself)", gearOff);
        if (k == RideKind.Kayak) Check("KAYAK: thrown out of it, the legs are drawn again", !body.LegsHidden);
        if (shots != null) { for (int i = 0; i < 20; i++) Frame(0f); Shoot($"{k}_4_crash.png", Watch(body.RagdollCentre, 7f)); }
        if (k == RideKind.Bear)
        {
            Vector3 b0 = body.BearAt;
            for (int i = 0; i < 90; i++) Frame(0f);
            Vector3 b1 = body.BearAt;
            Check("BEAR: thrown off, it runs on, on the snow, and stops", (b1 - b0).magnitude > 2f && Mathf.Abs(b1.y - Slope.DrawnHeight(b1.x, b1.z)) < 0.2f, $"ran {(b1 - b0).magnitude:0.0} m, {b1.y - Slope.DrawnHeight(b1.x, b1.z):0.00} m off the snow");
        }
        for (int i = 0; i < 60; i++) Frame(0f);
        int took = TapUp();
        for (int i = 0; i < 3; i++) Frame(0f);
        bool home = k == RideKind.Bear ? !body.BearLoose && body.BearGear.parent == body.RollFrame : FindChild(Gear(k), GearPart(k)) != null;
        Check($"{n}: a tap, and up within a few frames", !rider.Crashed && took < 12, $"{took} frames");
        Check($"{n}: everything back under the rider", home && Shown(Gear(k)));
        float z0 = rider.Z;
        for (int i = 0; i < 180; i++) Frame(Pilot(i * Dt));
        Check($"{n}: riding on after", !rider.Crashed && rider.Z > z0 + 5f, $"{rider.Z - z0:0.0} m in 3 s");

        // ---- each one's own
        if (k == RideKind.Kayak) Kayak();
        else if (k == RideKind.Car) Car();
        else if (k == RideKind.Bike) Bike();
        else Bear();
    }

    static Transform FindChild(Transform t, string name)
    {
        if (t == null) return null;
        for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).name == name) return t.GetChild(i);
        return null;
    }

    /// <summary>Up to speed down the run's middle (the autopilot), for up to `most` s.</summary>
    static void UpTo(float speed, float most)
    {
        for (float t = 0f; t < most && rider.Speed < speed; t += Dt) { if (rider.Crashed) { if (rider.CanGetUp) TapUp(); else Frame(0f); continue; } Frame(Pilot(t)); }
    }

    static void Kayak()
    {
        ToStart();
        UpTo(17f, 30f);
        // the paddle in both fists: its shaft through both grips
        var paddle = FindChild(body.KayakGear, "Paddle");
        var h = body.Hero;
        float off = 9f;
        if (paddle != null && h != null)
        {
            Vector3 up = body.RollFrame.up, ax = paddle.right;
            off = 0f;
            for (int s = 0; s < 2; s++)
            {
                Vector3 grip = h.Hand[s].position - up * 0.065f - paddle.position;
                off = Mathf.Max(off, (grip - ax * Vector3.Dot(grip, ax)).magnitude);
            }
        }
        Check("KAYAK: the paddle in both fists (its shaft through both grips)", off < 0.06f, $"{off:0.000} m off");
        // the legs in the hull (out of sight under the deck): hips, knees and ankles in the hull's own frame, riding and
        // through a stroke
        var hull = FindChild(body.KayakGear, "Hull");
        float worstX = 0f, worstY = -9f; string legAt = "";
        for (int i = 0; i < 90 && hull != null && h != null && !rider.Crashed; i++)
        {
            Frame(i < 45 ? 0f : 0.6f);
            Transform[] parts = { h.Hips, h.LowerLeg[0], h.LowerLeg[1], h.Foot[0], h.Foot[1] };
            string[] names = { "hips", "knee L", "knee R", "ankle L", "ankle R" };
            for (int j = 0; j < parts.Length; j++)
            {
                if (parts[j] == null) continue;
                Vector3 l = hull.InverseTransformPoint(parts[j].position);
                if (i % 30 == 0) Debug.Log($"RIDES kayak frame {i} {names[j]} in the hull ({l.x:0.00}, {l.y:0.00}, {l.z:0.00})");
                if (j > 0 && Mathf.Abs(l.x) > worstX) { worstX = Mathf.Abs(l.x); legAt = $"{names[j]} ({l.x:0.00}, {l.y:0.00}, {l.z:0.00})"; }
                if (j > 0) worstY = Mathf.Max(worstY, l.y);
            }
        }
        Check("KAYAK: the legs inside the hull (knees and ankles within its sides, under its deck)", worstX < 0.24f && worstY < 0.30f, $"widest {worstX:0.00} m ({legAt}), highest {worstY:0.00} m");
        Check("KAYAK: sat in it, the legs (under the deck) are not drawn", body.LegsHidden);

        // thrown from one way to the other at speed: it spins out, and comes round again
        int s0 = spins;
        float maxSpin = 0f;
        for (float t = 0f; t < 4f && !rider.Crashed; t += Dt) { Frame(Mathf.Repeat(t, 0.9f) < 0.45f ? 1f : -1f); maxSpin = Mathf.Max(maxSpin, Mathf.Abs(rider.SpinOut)); if (shots != null && spins > s0 && maxSpin > 1.2f && maxSpin < 1.3f) Shoot("Kayak_5_spinout.png", Chase()); }
        float round = -1f;
        for (float t = 0f; t < 4f && !rider.Crashed; t += Dt)
        {
            Frame(Pilot(t));
            if (round < 0f && Mathf.Abs(rider.SpinOut) < 0.05f && Mathf.Abs(rider.HullSpinRate) < 0.3f) round = t;   // (settled, not passing through)
        }
        Check("KAYAK: thrown across at speed, it spins out", spins > s0 && maxSpin > 1f, $"{spins - s0} spin-outs, {maxSpin * Mathf.Rad2Deg:0} deg round");
        Check("KAYAK: and comes round again (within 4 s of the last throw)", round >= 0f || rider.Crashed, round >= 0f ? $"{round:0.0} s" : $"still {rider.SpinOut * Mathf.Rad2Deg:0} deg");
    }

    static void Car()
    {
        // the engine: on the start pad's flat it pulls itself on
        ToStart();
        for (int i = 0; i < 60; i++) Frame(0f);
        Check("CAR: its engine turns (revs, a gear)", rider.Rpm > 0.2f, $"rpm {rider.Rpm:0.00} gear {rider.Gear}");
        // thrown hard from one way to the other at speed: it leans right over on its springs, and stays on its wheels
        // (rides2: past a tip of 2.1 it used to roll, throwing the driver out; nobody wants a turn to do that)
        UpTo(27f, 40f);
        int c0 = crashCount; float most = 0f;
        for (float t = 0f; t < 6f && crashCount == c0; t += Dt) { Frame(Mathf.Repeat(t, 1.0f) < 0.5f ? 1f : -1f); most = Mathf.Max(most, Mathf.Abs(rider.Tip)); }
        Check("CAR: thrown hard across at speed, it leans on its springs and stays on its wheels", crashCount == c0 && most > 0.5f, $"tip {most:0.00}, crash \"{(crashCount > c0 ? lastCrash : "none")}\"");
        if (shots != null) Shoot("Car_5_leaning.png", Chase());
    }

    static void Bear()
    {
        ToStart();
        UpTo(14f, 30f);
        for (int i = 0; i < 30; i++) Frame(Pilot(i * Dt));
        // a jump: the bear leaps big
        rider.QueuePop(Summit.Rider.TapPop);
        float peak = 0f; bool up = false;
        for (int i = 0; i < 240; i++) { Frame(0f); if (rider.Airborne) { up = true; peak = Mathf.Max(peak, rider.Peak); if (shots != null && i == 18) Shoot("Bear_5_jump.png", Side(6f)); } else if (up) break; }
        Check("BEAR: it jumps big", up && peak > 0.9f, $"peak {peak:0.00} m");
        Check("BEAR: and lands running", !rider.Crashed, lastCrash);
    }

    static void Bike()
    {
        // slow on the start's flat: pedalled on, the cranks going round with the feet on the pedals
        ToStart();
        float turned = 0f, last = body.BikeCrank; int pedalled = 0;
        for (int i = 0; i < 120; i++)
        {
            Frame(0f);
            if (rider.Pedalling) pedalled++;
            turned += Mathf.Abs(Mathf.DeltaAngle(last, body.BikeCrank)); last = body.BikeCrank;
        }
        Check("BIKE: slow, it is pedalled on (the cranks go round)", pedalled > 60 && turned > 180f, $"{pedalled} frames pedalled, cranks {turned:0} deg");
        if (shots != null) Shoot("Bike_5_pedalling.png", Side(3.2f));
        // the feet on the pedals as they go round
        float worst = 0f;
        var pl = FindDeep(body.BikeGear, "BikePedalL"); var pr = FindDeep(body.BikeGear, "BikePedalR");
        for (int i = 0; i < 30; i++)
        {
            Frame(0f);
            if (pl != null && pr != null && body.Hero != null)
            {
                worst = Mathf.Max(worst, Vector3.Distance(Mid(pl), body.Hero.Foot[0].position));
                worst = Mathf.Max(worst, Vector3.Distance(Mid(pr), body.Hero.Foot[1].position));
            }
        }
        Check("BIKE: the feet stay on the pedals as they go round", pl != null && pr != null && worst < 0.2f, $"farthest ankle {worst:0.00} m from its pedal");
        // fast: it coasts (the freewheel)
        UpTo(17f, 30f);
        int coast = 0;
        for (int i = 0; i < 60; i++) { Frame(Pilot(i * Dt)); if (!rider.Pedalling && !rider.Crashed) coast++; }
        Check("BIKE: fast, it coasts (the freewheel)", coast > 40, $"{coast} of 60 frames coasting at {rider.Speed:0.0} m/s");
        // turned hard at speed: the back wheel skids, it slows, and it stays up
        UpTo(14f, 20f);
        float skid = 0f, v0 = rider.Speed; int c1 = crashCount;
        for (int i = 0; i < 50; i++) { Frame(1f); skid = Mathf.Max(skid, rider.BikeSkid); if (shots != null && i == 30) Shoot("Bike_6_skid.png", Chase()); }
        Check("BIKE: braked into a hard turn the back wheel skids, and it stays up", skid > 0.6f && crashCount == c1, $"skid {skid:0.00}, {v0:0.0} -> {rider.Speed:0.0} m/s");
        // a hop off the snow
        for (int i = 0; i < 40; i++) Frame(Pilot(i * Dt));
        rider.QueuePop(Summit.Rider.TapPop);
        float peak = 0f; bool up = false;
        for (int i = 0; i < 240; i++) { Frame(0f); if (rider.Airborne) { up = true; peak = Mathf.Max(peak, rider.Peak); } else if (up) break; }
        Check("BIKE: it hops", up && peak > 0.3f, $"peak {peak:0.00} m");
        Check("BIKE: and lands riding", !rider.Crashed, lastCrash);
        // the hands on the grips, straight and with the bar steered
        var gl = FindDeep(body.BikeGear, "GripL");
        for (int i = 0; i < 20; i++) Frame(0f);
        float hand = gl != null && body.Hero != null ? Vector3.Distance(gl.position + gl.up * 0.065f, body.Hero.Hand[0].position) : 9f;
        for (int i = 0; i < 20; i++) Frame(0.7f);
        float handSteered = gl != null && body.Hero != null ? Vector3.Distance(gl.position + gl.up * 0.065f, body.Hero.Hand[0].position) : 9f;
        Check("BIKE: the hands stay on the grips, the bar steered too", hand < 0.08f && handSteered < 0.08f, $"{hand:0.000} m, steered {handSteered:0.000} m");
        if (shots != null) Shoot("Bike_7_riding.png", Side(4.2f));
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t == null) return null;
        foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
        return null;
    }
    static Vector3 Mid(Transform t) { var r = t.GetComponentInChildren<Renderer>(); return r != null ? r.bounds.center : t.position; }

    // ---------------------------------------------------------------- pictures

    struct View { public Vector3 at, look; }
    static View Chase()
    {
        var p = rider.transform.position; var v = rider.Velocity; v.y = 0f;
        var f = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
        return new View { at = p - f * 6.5f + Vector3.up * 2.6f, look = p + f * 3f + Vector3.up * 0.6f };
    }
    static View Side(float d)
    {
        var p = rider.transform.position; var v = rider.Velocity; v.y = 0f;
        var f = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
        var r = Vector3.Cross(Vector3.up, f);
        return new View { at = p + r * d + f * (d * 0.55f) + Vector3.up * 1.4f, look = p + Vector3.up * 0.7f };
    }
    static View Watch(Vector3 at, float d)
    {
        var p = rider.transform.position; var v = rider.Velocity; v.y = 0f;
        var f = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
        var r = Vector3.Cross(Vector3.up, f);
        return new View { at = at - f * d * 0.6f + r * d * 0.6f + Vector3.up * 2.5f, look = at + Vector3.up * 0.5f };
    }

    static int shotLog;
    static void Shoot(string file, View view)
    {
        terrain.Prewarm(); props.Prewarm();
        cam.transform.SetPositionAndRotation(view.at, Quaternion.LookRotation(view.look - view.at, Vector3.up));
        int w = 780, h = 1100;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        // skinned meshes don't skin in a batch render: a baked copy of each (with its scale: the bear's file scales itself)
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            if (shotLog++ < 12) Debug.Log($"RIDES skin {sk.name} scale {sk.transform.lossyScale} bones {sk.bones.Length}");
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr2 = g.GetComponent<MeshRenderer>(); mr2.sharedMaterials = sk.sharedMaterials;
            var mpb = new MaterialPropertyBlock(); sk.GetPropertyBlock(mpb); mr2.SetPropertyBlock(mpb);
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            // (a skin with no bones is drawn with its renderer's whole scale, which a bake leaves out: the bear's file
            // keys x100 on its root. Drawn as the phone draws it: rides2)
            if (sk.bones.Length == 0) g.transform.localScale = sk.transform.lossyScale;
            sk.enabled = false; off.Add(sk); baked.Add(g);
        }
        Canvas.ForceUpdateCanvases();
        cam.Render();
        foreach (var g in baked) { Object.DestroyImmediate(g.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(g); }
        foreach (var sk in off) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(shots, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("RIDES picture " + file);
    }
}
