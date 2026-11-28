using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (groundspin) The flat spin on the snow, by real fingers (RiderInput.TestDrive, through FingerGesture):
///
/// - each ride: a swipe spins it the swipe's way and pays the ride's own name ("Butter 360", "Flat 360", "Spin 360",
///   "Kayak Flatspin 360", the tube's "Spin 360"); the car, the bear and the mountain bike do not spin. The board goes on where it was
///   going, as fast: against the same ride hands off, the way it goes and its speed once the spin is done.
/// - by length and by repeats: a short swipe is a 180 (a board and skis ride out switch; a second is a "Switch ... 180"
///   that brings them back), a long one a 540 (a 720 on the toboggan), a second swipe the same way mid-spin half a
///   turn more.
/// - it is nobody else's gesture: steering (held, or dragged and let go slowly), a tap, a double tap (still the
///   jump) and a flick up never spin; in a trick air a sideways throw is the air's spin, not a ground one.
/// - bailing: a tree clipped riding straight is a glance; clipped mid-spin it is a fall. A spin with nothing in the
///   way never is.
///
/// The whole table is ridden twice and the two must match line for line.
///
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod GroundSpinTest.Run -logFile groundspin.log
///      (lines start GSPIN)
/// </summary>
public static class GroundSpinTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider r; static RiderBody body; static RiderInput input; static SlopeStreamer terrain; static Forest props;
    static MethodInfo riderUpdate;
    static int fails, checks, jumps; static string crash, hits = "";
    static readonly List<string> links = new List<string>();
    static List<string> table = new List<string>();
    static float W => Mathf.Max(Screen.width, 320);
    static float H => Mathf.Max(Screen.height, 480);
    static Vector2 centre;

    public static void Run()
    {
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        r = game.rider;
        body = game.body;
        input = game.input;
        terrain = game.terrain;
        props = game.props;
        riderUpdate = typeof(Rider).GetMethod("Update", NP);
        r.Link += l => { if (l.kind == LinkKind.Trick) links.Add(l.name); };
        r.Crash += why => crash = why;
        r.Jumped += () => jumps++;
        r.HitObstacle += (ob, c, into) => hits += $" [{ob.label} into {into:0.0} angle {r.LastHitAngle:0} mid {r.MidSpin} spin {r.GroundSpinNow * Mathf.Rad2Deg:0}]";
        RiderInput.TestDrive = true;
        centre = new Vector2(W * 0.5f, H * 0.4f);
        try
        {
            TestBoot.NewMountain(game, 1000, terrain, props);
            var first = Table();
            var second = Table();
            int differ = 0;
            for (int i = 0; i < Mathf.Max(first.Count, second.Count); i++)
                if (i >= first.Count || i >= second.Count || first[i] != second[i]) { differ++; if (differ < 5) Debug.Log($"GSPIN !! differs: '{(i < first.Count ? first[i] : "")}' / '{(i < second.Count ? second[i] : "")}'"); }
            Expect("the table ridden twice matches line for line", differ == 0, $"{first.Count} lines, {differ} differ");
            Debug.Log(fails == 0 ? $"GSPIN PASS ({checks} checks)" : $"GSPIN FAIL ({fails} of {checks} checks)");
        }
        catch (System.Exception e) { Debug.LogError("GSPIN exception " + e); fails++; }
        RiderInput.TestDrive = false; RiderInput.TestFingers = 0;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0);
    }

    // ---------------------------------------------------------------- the stepping

    static void Step(int fingers, Vector2 pos)
    {
        RiderInput.TestFingers = fingers; RiderInput.TestPos = pos;
        input.TestStep(Dt);
        riderUpdate.Invoke(r, null);
        r.TestContacts(Dt);   // (the board's own look ahead: batchmode reports no collisions)
        TestHarness.Step(r, body, Dt);
        game.TestPoints(Dt);
    }
    static void Frames(float secs, int fingers, Vector2 pos) { for (float t = 0f; t < secs - 1e-4f; t += Dt) Step(fingers, pos); }

    /// <summary>A finger put down at `from`, thrown `across` screen widths sideways over `secs`, then held `hold` s and lifted.</summary>
    static void Swipe(float across, float secs = 0.15f, float hold = 0f, float up = 0f)
    {
        Vector2 from = centre - Vector2.right * W * across * 0.5f;
        Step(1, from);
        for (float t = Dt; t < secs + 1e-4f; t += Dt) Step(1, from + new Vector2(W * across * t / secs, H * up * t / secs));
        Vector2 end = from + new Vector2(W * across, H * up);
        if (hold > 0f) Frames(hold, 1, end);
        Step(0, end);
    }
    static void Tap(Vector2 at, float upFor = 0.12f) { Frames(0.08f, 1, at); Frames(upFor, 0, at); }

    static RideKind rideNow; static bool tubeNow;
    static void Ride(RideKind kind, bool tube) { body.SetRide(kind); r.SetRide(body.Ride); r.Tube = tube; body.SetTube(tube); rideNow = kind; tubeNow = tube; }
    static string RideName => tubeNow ? "TUBE" : Rides.Of(rideNow).Name;

    static float startX, startZ;
    static void Start(float z, float v0 = 16f, float x = float.NaN)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        Ride(rideNow, tubeNow);   // (ToReady puts the saved ride back)
        r.input = null;
        if (float.IsNaN(x)) x = Slope.Centre(z);
        startX = x; startZ = z;
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
        terrain.Prewarm(); props.Prewarm();
        RiderInput.TestFingers = 0;
        input.TestStep(Dt); input.SwallowCurrentPress();
        r.input = input;
        Frames(0.4f, 0, centre);   // (settled on the snow)
        links.Clear(); crash = null; jumps = 0;
    }

    static void Expect(string label, bool ok, string got)
    {
        checks++;
        if (!ok) fails++;
        Debug.Log($"GSPIN {(ok ? "  " : "!!")} {label,-66} {got}");
    }

    static string Links => links.Count == 0 ? "(none)" : string.Join(", ", links);
    static float TravelDeg => r.TravelHeading * Mathf.Rad2Deg;

    // ---------------------------------------------------------------- the table

    static List<string> Table()
    {
        table = new List<string>();
        float z = Course.First + 110f;
        var rides = new (RideKind k, bool tube, string name)[]
        {
            (RideKind.Snowboard, false, "Butter 360"), (RideKind.Skis, false, "Flat 360"), (RideKind.Toboggan, false, "Spin 360"),
            (RideKind.Kayak, false, "Kayak Flatspin 360"), (RideKind.Snowboard, true, "Spin 360"), (RideKind.Car, false, null), (RideKind.Bear, false, null), (RideKind.Bike, false, null),
        };

        // ---- each ride: a swipe right, a swipe left; against the same ride hands off
        foreach (var rd in rides)
        {
            Ride(rd.k, rd.tube);
            Start(z); Frames(1.6f, 0, centre);
            float offHead = TravelDeg, offSpeed = r.Speed;
            foreach (int dir in new[] { 1, -1 })
            {
                Start(z);
                Swipe(0.45f * dir);
                bool turning = r.GroundSpinning, rightWay = r.GroundSpinHalves * dir > 0;
                float most = 0f;
                for (float t = 0f; t < 1.6f - 0.16f; t += Dt) { Step(0, centre); most = Mathf.Max(most, Mathf.Abs(r.GroundSpinNow)); }
                float dh = Mathf.DeltaAngle(offHead, TravelDeg), ds = r.Speed / Mathf.Max(offSpeed, 0.1f) - 1f;
                string line = $"{RideName} swipe {(dir > 0 ? "right" : "left")}: spun {(turning ? (rightWay ? "its way" : "THE OTHER WAY") : "no")}, most {most * Mathf.Rad2Deg:0} deg, links {Links}, crash {crash ?? "none"}; vs hands off travel {dh:+0.0;-0.0} deg, speed {ds * 100f:+0.0;-0.0}%";
                table.Add(line);
                if (rd.name == null) Expect($"{RideName} swipe {(dir > 0 ? "right" : "left")}: no spin on it", !turning && links.Count == 0 && crash == null, line);
                else
                {
                    Expect($"{RideName} swipe {(dir > 0 ? "right" : "left")}: '{rd.name}' its way, nothing else", turning && rightWay && links.Count == 1 && links[0] == rd.name && crash == null && most > Mathf.PI * 1.9f, line);
                    Expect($"{RideName} swipe {(dir > 0 ? "right" : "left")}: goes on as it went (within 3 deg, 3%)", Mathf.Abs(dh) < 3f && Mathf.Abs(ds) < 0.03f, $"travel {dh:+0.0;-0.0} deg, speed {ds * 100f:+0.0;-0.0}%");
                    Expect($"{RideName} swipe {(dir > 0 ? "right" : "left")}: done facing forward", !r.GroundSpinning && !r.Switch && r.GroundSpinNow == 0f, $"switch {r.Switch}");
                }
            }
        }

        // ---- by length and by repeats
        Ride(RideKind.Snowboard, false);
        Start(z); Swipe(0.28f); Frames(1.2f, 0, centre);
        bool sw = r.Switch; string l180 = Links;
        Swipe(-0.28f); Frames(1.2f, 0, centre);
        Expect("board: a short swipe is a 180, ridden out switch", l180 == "Butter 180" && sw, $"{l180}, switch {sw}");
        Expect("board: another short swipe from switch: Switch Butter 180, back the normal way", Links == "Butter 180, Switch Butter 180" && !r.Switch, $"{Links}, switch {r.Switch}");
        table.Add($"board 180s: {Links}");
        Start(z); Swipe(0.7f); Frames(1.8f, 0, centre);
        Expect("board: a long swipe is a 540, ridden out switch", Links == "Butter 540" && r.Switch && crash == null, $"{Links}, switch {r.Switch}, crash {crash ?? "none"}");
        table.Add($"board long: {Links}");
        Start(z); Swipe(0.45f); Frames(0.1f, 0, centre); Swipe(0.45f); Frames(1.8f, 0, centre);
        Expect("board: a second swipe the same way mid-spin: 540", Links == "Butter 540" && crash == null, $"{Links}");
        table.Add($"board repeat: {Links}");
        Start(z); Swipe(0.45f); Frames(0.1f, 0, centre); Swipe(-0.45f); Frames(1.5f, 0, centre);
        Expect("board: a swipe the other way mid-spin changes nothing", Links == "Butter 360" && !r.Switch, $"{Links}");
        Ride(RideKind.Skis, false);
        Start(z); Swipe(0.28f); Frames(1.2f, 0, centre);
        Expect("skis: a short swipe is a Flat 180, ridden out switch", Links == "Flat 180" && r.Switch, $"{Links}, switch {r.Switch}");
        Ride(RideKind.Toboggan, false);
        Start(z); Swipe(0.28f); Frames(1.6f, 0, centre);
        Expect("toboggan: a short swipe is still a whole turn (no switch on it)", Links == "Spin 360" && !r.Switch, $"{Links}");
        Start(z); Swipe(0.7f);
        int tHalves = r.GroundSpinHalves;
        bool tAir = false;
        for (float t = 0f; t < 6f && !(t > 2.5f && !r.Airborne && !r.GroundSpinning); t += Dt) { Step(0, centre); if (r.Airborne) tAir = true; }
        Frames(0.3f, 0, centre);
        // (on this site a bump throws it off the snow part way round: the spin goes on as that air's own trick, a 720)
        Expect("toboggan: a long swipe is a 720 (on the snow, or carried into an air)", tHalves == 4 && crash == null && (tAir ? Links == "720" : Links == "Spin 720"), $"{Links}, committed {tHalves} halves, off the snow {tAir}, crash {crash ?? "none"}");
        table.Add($"toboggan long: {Links}");

        // ---- nobody else's gesture
        Ride(RideKind.Snowboard, false);
        Start(z); Swipe(0.45f, 0.15f, 0.8f); Frames(1f, 0, centre);
        Expect("steering: a drag held on is no spin", links.Count == 0 && !r.GroundSpinning, Links);
        Start(z); Swipe(0.45f, 0.5f); Frames(1f, 0, centre);
        Expect("steering: a slow drag let go is no spin", links.Count == 0, Links);
        Start(z); Swipe(0.15f); Frames(1f, 0, centre);
        Expect("steering: a short quick jab is no spin", links.Count == 0, Links);
        Start(z); Swipe(0.3f, 0.12f, 0f, 0.35f); Frames(1f, 0, centre);
        Expect("a throw mostly up the screen is no spin", links.Count == 0, Links);
        Start(z); Tap(centre); Frames(1f, 0, centre);
        Expect("a tap is no spin", links.Count == 0 && !r.GroundSpinning, Links);
        Start(z); Tap(centre); Frames(0.08f, 1, centre); Frames(0.1f, 0, centre);
        bool jumped = jumps > 0 && !r.GroundSpinning; Frames(2f, 0, centre);
        Expect("a double tap: the jump, no ground spin", jumped && !links.Exists(n => n.StartsWith("Butter")), $"jumps {jumps}, {Links}");
        // a trick air: up off a pop, then a sideways throw is the air's spin
        Start(z, 22f);
        r.QueuePop(0.8f);
        for (int i = 0; i < 60 && !r.Airborne; i++) Step(0, centre);
        Frames(0.12f, 0, centre);
        bool air = r.Airborne;
        Swipe(0.45f);
        bool groundInAir = r.GroundSpinning, airSpin = r.TrickName != null;
        for (int i = 0; i < 600 && r.Airborne; i++) Step(0, centre);
        Frames(0.5f, 0, centre);
        Expect("in a trick air a sideways throw is the air's spin, not the snow's", air && !groundInAir && airSpin && !links.Exists(n => n.StartsWith("Butter")), $"air {air}, ground spin {groundInAir}, air trick {airSpin}, {Links}, crash {crash ?? "none"}");
        table.Add($"air throw: {Links}");

        // ---- bailing: a tree clipped riding straight glances; mid-spin it is a fall
        foreach (bool spin in new[] { false, true })
        {
            Start(z);
            Vector3 p = r.Body.position, v = r.Body.linearVelocity; v.y = 0f;
            Vector3 fwd = v.normalized, side = Vector3.Cross(Vector3.up, fwd);
            Vector3 at = p + fwd * (v.magnitude * 0.42f) + side * (Rider.R + 0.18f);
            at.y = Slope.DrawnHeight(at.x, at.z);
            var tree = new GameObject("GSpinTree");
            var cap = tree.AddComponent<CapsuleCollider>(); cap.radius = 0.25f; cap.height = 6f; cap.center = Vector3.up * 2.5f;
            tree.transform.position = at;
            tree.AddComponent<Obstacle>().label = "Tree";
            Physics.SyncTransforms();
            if (spin) Swipe(0.45f); else Frames(0.16f, 0, centre);
            bool mid = false; hits = "";
            for (float t = 0f; t < 1.2f && crash == null; t += Dt) { if (r.MidSpin) mid = true; Step(0, centre); }
            Object.DestroyImmediate(tree);
            Physics.SyncTransforms();
            string line = $"tree clipped {(spin ? "mid-spin" : "riding straight")}: crash {crash ?? "none"}, mid-spin seen {mid}, links {Links}, hits{(hits == "" ? " none" : hits)}";
            table.Add(line);
            if (spin) Expect("a tree clipped mid-spin: a fall", crash == "Tree" && mid, line);
            else Expect("a tree clipped riding straight: a glance, no fall", crash == null, line);
        }
        return table;
    }
}
