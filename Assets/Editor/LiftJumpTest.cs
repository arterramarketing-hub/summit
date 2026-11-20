using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Riding through the bottom stations and off their lips (LiftJump.cs), through the game's own rider: at five speeds
/// down the lane's middle and both its edges, on every jump station of several mountains, each ride followed until
/// 2.5 s after it lands. Reported for each: the speed onto the lane and off the lip, the air (seconds, the most
/// height over the snow, metres flown), the landing (speed, how hard and how steeply into the snow), what the air
/// scored through the game's own trick feed, and whether it was ridden away. Then the lines that miss the lane:
/// just outside its outer edge and further out (a pass by), between the lane and the terminal and straight at the
/// terminal (the station, hit where it is drawn), and past the far side (a pass by).
/// Batchmode sends no collision messages, so what the board touches is looked for each step and a touch on an
/// obstacle is the crash it would be in the game.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod LiftJumpTest.Run -logFile jump.log
///   SUMMIT_JUMP_SEEDS  "4242,1000,1077"       SUMMIT_JUMP_VERBOSE  a ride's name, to list it step by step
/// </summary>
public static class LiftJumpTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props; static SkiLift lift; static MethodInfo liftUpdate;
    static int rides, fails, away, boarded;
    static string lastCrash = "", lastTrick = ""; static int lastPoints; static bool scoring;
    static readonly List<float> airs = new List<float>(), peaks = new List<float>(), dists = new List<float>(), lands = new List<float>(), impacts = new List<float>();
    static readonly Collider[] near = new Collider[24];
    static double clock = 100.0;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        r = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        lift = Object.FindFirstObjectByType<SkiLift>();
        liftUpdate = typeof(SkiLift).GetMethod("Update", NP);
        r.input = null;
        r.Crash += why => lastCrash = why;
        // (what the big air itself scored: the first thing the game's feed is given as it lands)
        r.Trick += (name, pts) => { if (scoring && lastTrick == "") { lastTrick = name; lastPoints = pts; } };
        var dressing = Object.FindFirstObjectByType<CourseDressing>();
        string seeds = System.Environment.GetEnvironmentVariable("SUMMIT_JUMP_SEEDS") ?? "4242,1000,1077";
        try
        {
            foreach (string sd in seeds.Split(','))
            {
                int seed = int.Parse(sd);
                TestBoot.NewMountain(game, seed, terrain, props);
                // (batchmode refuses Destroy, so the last mountain's lifts, colliders and all, would stand on in this one)
                lift.Clear();
                for (int i = lift.transform.childCount - 1; i >= 0; i--) if (lift.transform.GetChild(i).name.StartsWith("Lift ")) Object.DestroyImmediate(lift.transform.GetChild(i).gameObject);
                if (dressing != null) { var tgt = dressing.target; dressing.target = null; dressing.Rebuild(); dressing.target = tgt; }
                Debug.Log($"JUMP seed {seed}\n" + LiftLine.JumpReport());
                foreach (var l in LiftLine.Lifts)
                {
                    if (l.jump == null) continue;
                    string name = $"seed{seed} lift{l.k}";
                    foreach (float v0 in new[] { 10f, 18f, 26f, 34f, 42f })
                        foreach (float off in new[] { 0f, -3f, 3f })
                            Ride($"{name} v{v0:0} line{off:+0.0;-0.0;0}", l, LiftLine.JumpLaneMid + off, v0, true);
                    // the lines that miss it
                    foreach (float v0 in new[] { 18f, 30f })
                    {
                        Ride($"{name} v{v0:0} outside +1", l, LiftLine.JumpLaneOut + 1f, v0, false);
                        Ride($"{name} v{v0:0} outside +5", l, LiftLine.JumpLaneOut + 5f, v0, false);
                        Ride($"{name} v{v0:0} between lane and terminal", l, LiftLine.JumpLaneIn - 0.8f, v0, false);
                        Ride($"{name} v{v0:0} at the terminal", l, 0f, v0, false);
                        Ride($"{name} v{v0:0} far side -8", l, -LiftLine.StationHalfWid - 2.5f, v0, false);
                    }
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("JUMP exception " + e); fails++; }
        airs.Sort(); peaks.Sort(); dists.Sort(); lands.Sort(); impacts.Sort();
        System.Func<List<float>, string> R = v => v.Count == 0 ? "-" : $"{v[0]:0.0} / {v[v.Count / 2]:0.0} / {v[v.Count - 1]:0.0}";
        Debug.Log($"JUMP done: {rides} rides down a lane, {boarded} carried through, {away} ridden away, {fails} failure(s). Least / median / most: air {R(airs)} s; height over the snow {R(peaks)} m; flown {R(dists)} m; landing speed {R(lands)} m/s; into the snow {R(impacts)} m/s");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails > 0 ? 1 : 0);
    }

    static void Fail(string what) { fails++; Debug.Log("JUMP-FAIL " + what); }

    static void Step(int n)
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        clock += Dt;
        if (n % 12 == 0) { terrain.Prewarm(); props.Prewarm(); SkiLift.TestClock = clock; liftUpdate.Invoke(lift, null); Physics.SyncTransforms(); }
        if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
    }

    /// <summary>What solid thing the board is touching, if any (the game would be told by a collision).</summary>
    static Obstacle Touching(out string what)
    {
        what = null;
        Vector3 p = r.Body.position;
        int k = Physics.OverlapSphereNonAlloc(p, Rider.R + 0.03f, near, ~(1 << CourseDressing.IceLayer), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < k; i++)
        {
            if (near[i].attachedRigidbody == r.Body || !near[i].enabled) continue;
            var ob = near[i].GetComponentInParent<Obstacle>();
            if (ob == null) continue;
            what = ob.label + " (" + near[i].name + ")";
            return ob;
        }
        return null;
    }

    /// <summary>
    /// One ride at a station: from 30 m up the line from where the lane's top leaves the snow, on the line c metres
    /// across from the lift's centre toward the piste, at v0, held to that line. In the lane it must be carried
    /// through, fly, land and be ridden away; off it, it is told what happened.
    /// </summary>
    static void Ride(string name, LiftLine.Lift l, float c, float v0, bool lane)
    {
        var j = l.jump;
        bool verbose = System.Environment.GetEnvironmentVariable("SUMMIT_JUMP_VERBOSE") == name;
        Vector3 from = l.At(l.plan + j.start - 30f, j.side * c, 0f);
        r.ResetAt(from.x, from.z); body.ResetVisual(); r.Begin();
        r.Heading = Mathf.Atan2(l.dir.x, l.dir.z);
        r.Body.linearVelocity = Vector3.ProjectOnPlane(l.dir, Slope.Normal(from.x, from.z)).normalized * v0;
        lastCrash = ""; lastTrick = ""; lastPoints = 0; scoring = false;
        terrain.Prewarm(); props.Prewarm();
        SkiLift.TestClock = clock; liftUpdate.Invoke(lift, null); liftUpdate.Invoke(lift, null); Physics.SyncTransforms();

        bool on = false, flew = false, landed = false;
        float onSpeed = 0f, lipSpeed = 0f, air = 0f, peak = 0f, landSpeed = 0f, impact = 0f, angle = 0f, since = 0f, flown = 0f;
        Vector3 lip = Vector3.zero; string hit = null;
        System.Action<float> onLand = imp =>
        {
            if (!flew || landed) return;
            landed = true; scoring = true; air = r.AirTime; peak = r.Peak; landSpeed = r.Velocity.magnitude; impact = r.LastLandImpact; angle = r.LastLandAngle;
            Vector3 p = r.Body.position; flown = new Vector2(p.x - lip.x, p.z - lip.z).magnitude;
        };
        r.Landed += onLand;
        int n = 0;
        try
        {
            for (float t = 0f; t < 16f; t += Dt, n++)
            {
                Vector3 p = r.Body.position;
                // held to its line across the lift
                float across = ((p.x - l.top.x) * l.across.x + (p.z - l.top.z) * l.across.z) * j.side;
                float want = Mathf.Atan2(l.dir.x, l.dir.z);
                // (aimed back at its line: the way it is going, against the lift's own direction plus a little toward the line)
                float aim = want + Mathf.Clamp((c - across) * 0.09f, -0.4f, 0.4f) * j.side * Mathf.Sign(l.across.x);
                r.Steer = Mathf.Clamp(Mathf.DeltaAngle(r.TravelHeading * Mathf.Rad2Deg, aim * Mathf.Rad2Deg) * Mathf.Deg2Rad * 5f, -1f, 1f);
                bool deck = r.Grinding && r.GrindFeature.id == -3;
                if (deck && !on) { on = true; onSpeed = r.Speed; }
                if (on && deck) lipSpeed = r.Speed;
                if (on && !deck && !flew && r.Airborne) { flew = true; lip = p; }
                if (verbose && n % 6 == 0) Debug.Log($"JUMP-V t={t:0.00} along={(p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z - l.plan:0.0} across={across:0.0} y={p.y:0.00} snow={Slope.DrawnHeight(p.x, p.z):0.00} v={r.Velocity} deck={deck} air={r.Airborne} above={r.AboveGround:0.0}");
                Step(n);
                if (!r.Crashed)
                {
                    var ob = Touching(out string what);
                    if (ob != null)
                    {
                        Vector3 q = r.Body.position;
                        float qt = (q.x - l.top.x) * l.dir.x + (q.z - l.top.z) * l.dir.z - l.plan, qc = ((q.x - l.top.x) * l.across.x + (q.z - l.top.z) * l.across.z) * j.side;
                        hit = what + $" [{qt:0.0} m past the station's middle, {qc:0.0} m across, board {q.y - Rider.R - LiftLine.JumpTop(l, qt, qc * j.side):0.00} m over the lane's top, {q.y - Rider.R - Slope.DrawnHeight(q.x, q.z):0.00} over the snow; deck {l.bottom.y - j.y0:0.0} m over the lane's level, lane from {j.start:0.0} to {j.end:0.0}]";
                        r.DoCrash(ob.label);
                    }
                }
                if (r.Crashed) break;
                if (landed) { scoring = false; since += Dt; if (since > 2.5f) break; }
                // (off the lane: until well past the station)
                if (!lane && (p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z - l.plan > j.end + 2f && !r.Airborne) break;
            }
        }
        finally { r.Landed -= onLand; }

        Vector3 at = r.Body.position;
        string where = $"z={at.z:0} {Mathf.Abs(at.x - Slope.Centre(at.z)):0} m from the piste's centre";
        string end = r.Crashed ? $"CRASHED '{lastCrash}'{(hit != null ? " on " + hit : "")} at {where}" : $"rode on at {r.Speed:0} m/s ({where})";
        if (!lane)
        {
            Debug.Log($"JUMP {name}: {(on ? "carried through the lane, " : "")}{(flew ? $"flew {air:0.0} s, " : "")}{end}");
            // a miss is a pass by, or the station hit where it is drawn; never anything else
            // (straight at the terminal is straight down the line itself: its last tower stands there)
            if (r.Crashed && lastCrash != "Lift station" && lastCrash != "Lift tower") Fail($"{name}: {end}");
            return;
        }
        rides++;
        if (on) boarded++;
        bool ok = on && flew && landed && !r.Crashed && r.Speed > 5f;
        if (ok) { away++; airs.Add(air); peaks.Add(peak); dists.Add(flown); lands.Add(landSpeed); impacts.Add(impact); }
        Debug.Log($"JUMP {name}: onto the lane at {onSpeed:0.0} m/s, off the lip at {lipSpeed:0.0}; {air:0.00} s of air, {peak:0.0} m over the snow at most, {flown:0} m flown; lands at {landSpeed:0.0} m/s ({impact:0.0} into the snow at {angle:0} deg); scored '{lastTrick}' {lastPoints}; {end}");
        if (!on) Fail($"{name}: never carried onto the lane ({end})");
        else if (!flew) Fail($"{name}: carried through but never flew ({end})");
        else if (!landed) Fail($"{name}: never came down ({end})");
        else if (r.Crashed) Fail($"{name}: {end}, landing {impact:0.0} m/s at {angle:0} deg");
        else if (r.Speed <= 5f) Fail($"{name}: landed but did not ride away ({end})");
    }
}
