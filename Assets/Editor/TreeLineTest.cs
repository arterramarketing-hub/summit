using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (arcadetrees) The open lines through the trees (Forest.LaneKeep):
///  - Placed: on five mountains, along all four lines from the top of the run to the finish, nothing the forest plants
///    (tree or rock) stands on a line's core. The dense bands (stretches of 150 m where the forest beside a line is thick)
///    are counted, each must have its line open, and the forest kept beside the lines is reported (it should read full).
///  - Ridden: in the densest stretches of the lower forest on each mountain, a bot riding the line at a tree-riding speed
///    (held to 16 m/s) for 200 m touches no tree. The same rides with the lines switched off (Forest.LanesOn) are
///    reported as the before.
/// Logs TREELINE lines; exits 1 on any failure.
/// Batch: -batchmode -nographics -projectPath . -executeMethod TreeLineTest.Run
/// </summary>
public static class TreeLineTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f, RideSpeed = 16f, RideLen = 200f, Band = 150f;
    static readonly int[] Seeds = { 1000, 1154, 2131, 4242, 7777 };
    static int fails, checks;
    static (int side, int k) riding = (0, -1);
    static SummitGame game; static Summit.Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;

    static void Check(string what, bool ok, string got = "")
    {
        checks++; if (!ok) fails++;
        Debug.Log($"TREELINE {(ok ? "PASS" : "FAIL")} {what} {got}");
    }

    public static void Run()
    {
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        rider = game.rider; body = game.body;
        terrain = game.terrain; props = game.props;
        int treeHits = 0; string why = null;
        rider.HitObstacle += (ob, at, into) =>
        {
            if (ob.label != "Tree") return;
            treeHits++;
            if (Forest.LanesOn && riding.k >= 0)
            {
                var p = rider.Body.position;
                Debug.Log($"TREELINE touch at z {p.z:0}: the rider {p.x - Forest.LaneX(riding.side, riding.k, p.z):0.0} m off the line, the tree {at.x - Forest.LaneX(riding.side, riding.k, at.z):0.0} m off it, {into:0.0} m/s into it, air {rider.Airborne}");
            }
        };
        rider.Crash += w => why = w;
        try
        {
            int allBands = 0, allRides = 0, hitsOn = 0, hitsOff = 0;
            foreach (int seed in Seeds)
            {
                Forest.LanesOn = true;
                TestBoot.NewMountain(game, seed, terrain, props);
                // ---- placed
                int onCore = 0, bands = 0, openBands = 0;
                var dense = new List<(int side, int k, float z, float thick)>();
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 2; k++)
                    {
                        for (float z = 80f; z < Slope.FinishZ - 40f; z += Forest.Cell) onCore += OnCore(side, k, z);
                        for (float z0 = 80f; z0 + Band < Slope.FinishZ - 40f; z0 += Band)
                        {
                            float thick = Beside(side, k, z0, z0 + Band);
                            if (thick < 0.3f) continue;
                            bands++;
                            int blocked = 0;
                            for (float z = z0; z < z0 + Band; z += Forest.Cell) blocked += OnCore(side, k, z);
                            if (blocked == 0) openBands++;
                            dense.Add((side, k, z0, thick));
                        }
                    }
                Check($"seed {seed}: nothing stands on any line's core", onCore == 0, $"{onCore} in the way");
                Check($"seed {seed}: every dense band has its line open", openBands == bands, $"{openBands} of {bands} dense bands");
                allBands += bands;
                // the forest beside the lines and away from them: still full?
                Forest.LanesOn = false; float offBeside = Beside(-1, 1, 1600f, Slope.FinishZ - 200f) + Beside(1, 1, 1600f, Slope.FinishZ - 200f);
                Forest.LanesOn = true; float onBeside = Beside(-1, 1, 1600f, Slope.FinishZ - 200f) + Beside(1, 1, 1600f, Slope.FinishZ - 200f);
                float offAll = Count(false), onAll = Count(true);
                Debug.Log($"TREELINE seed {seed}: {bands} dense bands; trees beside the outer lines {offBeside * 50f:0}% -> {onBeside * 50f:0}% of cells; trees off the piste in the lower forest {offAll:0} -> {onAll:0} ({100f * onAll / Mathf.Max(1f, offAll):0}% kept)");
                // ---- ridden: the densest lower-forest stretches whose line runs over open, even ground
                dense.Sort((a, b) => b.thick.CompareTo(a.thick));
                int rides = 0;
                foreach (var d in dense)
                {
                    if (rides >= 3) break;
                    if (d.z < 1500f || d.z + RideLen > Slope.FinishZ - 60f || !Rideable(d.side, d.k, d.z, d.z + RideLen)) continue;
                    rides++; allRides++;
                    Forest.LanesOn = true; props.Rebuild();
                    treeHits = 0; why = null;
                    float got = Ride(d.side, d.k, d.z, out bool crashed);
                    int on = treeHits; string whyOn = why;
                    Forest.LanesOn = false; props.Rebuild();
                    treeHits = 0; why = null;
                    float gotOff = Ride(d.side, d.k, d.z, out bool crashedOff);
                    int off = treeHits;
                    Forest.LanesOn = true; props.Rebuild();
                    hitsOn += on; hitsOff += off;
                    Check($"seed {seed}: line {(d.side < 0 ? "left" : "right")} {(d.k == 0 ? "inner" : "outer")} from z {d.z:0} (forest beside it {d.thick * 100f:0}%): ridden {got:0} m at {RideSpeed:0} m/s touching no tree",
                          on == 0 && !(crashed && whyOn != null && whyOn.StartsWith("Tree")) && got > RideLen * 0.9f,
                          $"tree touches {on}{(crashed ? ", crashed '" + whyOn + "'" : "")}; before (no lines): {off} touches over {gotOff:0} m{(crashedOff ? ", down" : "")}");
                }
                if (rides == 0) Debug.Log($"TREELINE seed {seed}: no dense band on open even ground to ride");
            }
            Check("dense bands found on every mountain", allBands >= Seeds.Length, $"{allBands}");
            Check("lines ridden", allRides >= Seeds.Length, $"{allRides} rides: tree touches with the lines {hitsOn}, without {hitsOff}");
        }
        catch (System.Exception e) { Debug.LogError("TREELINE exception " + e); fails++; }
        Forest.LanesOn = true;
        Debug.Log(fails == 0 ? $"TREELINE PASS ({checks} checks)" : $"TREELINE FAIL ({fails} of {checks})");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>What the forest plants within a line's core (less a little, for rounding), in the cell row at z.</summary>
    static int OnCore(int side, int k, float z)
    {
        float lx = Forest.LaneX(side, k, z);
        int cz = Mathf.FloorToInt(z / Forest.Cell), n = 0;
        int c0 = Mathf.FloorToInt((lx - Forest.LaneCore * 2.5f) / Forest.Cell), c1 = Mathf.FloorToInt((lx + Forest.LaneCore * 2.5f) / Forest.Cell);
        for (int cx = c0; cx <= c1; cx++)
        {
            if (Forest.At(cx, cz, out Vector3 p, out _, out _) == Forest.Kind.None) continue;
            if (Mathf.Abs(Slope.Across(p.x, p.z)) < Slope.Half(p.z) - 3f) continue;   // (the groomed run's own tree islands: not a line's business)
            if (Mathf.Abs(p.x - Forest.LaneX(side, k, p.z)) * Slope.Squash(p.z) < Forest.LaneCore - 0.3f) n++;
        }
        return n;
    }

    /// <summary>The share of cells holding a tree in the strips beside a line (from its thinning's edge out 15 m), z0..z1.</summary>
    static float Beside(int side, int k, float z0, float z1)
    {
        int cells = 0, trees = 0;
        for (float z = z0; z < z1; z += Forest.Cell)
            foreach (float s in new[] { -1f, 1f })
                for (float o = Forest.LaneEdge; o < Forest.LaneEdge + 15f; o += Forest.Cell)
                {
                    float x = Forest.LaneX(side, k, z) + s * o / Slope.Squash(z);   // (square to the line)
                    cells++;
                    if (Forest.At(Mathf.FloorToInt(x / Forest.Cell), Mathf.FloorToInt(z / Forest.Cell), out _, out _, out _) == Forest.Kind.Tree) trees++;
                }
        return cells > 0 ? trees / (float)cells : 0f;
    }

    /// <summary>Trees off the piste in the lower forest (z 1600 to the finish, out to 100 m either side), lines on or off.</summary>
    static float Count(bool lanes)
    {
        Forest.LanesOn = lanes;
        int n = 0;
        for (float z = 1600f; z < Slope.FinishZ - 200f; z += Forest.Cell)
            for (float a = -100f; a <= 100f; a += Forest.Cell)
            {
                float x = Slope.Centre(z) + a;
                if (Mathf.Abs(Slope.Across(x, z)) < Slope.Half(z) + 3f) continue;
                if (Forest.At(Mathf.FloorToInt(x / Forest.Cell), Mathf.FloorToInt(z / Forest.Cell), out _, out _, out _) == Forest.Kind.Tree) n++;
            }
        Forest.LanesOn = true;
        return n;
    }

    /// <summary>The line's ground from z0 to z1: open, even snow (no cliff, crevasse, deadly drop, lift, feature or hamlet).</summary>
    static bool Rideable(int side, int k, float z0, float z1)
    {
        for (float z = z0 - 20f; z <= z1; z += 2f)
        {
            float lx = Forest.LaneX(side, k, z);
            for (float dx = -2.5f; dx <= 2.5f; dx += 2.5f)
            {
                float x = lx + dx;
                if (Slope.Normal(x, z).y < 0.6f || Deadly.Below(x, z) || Course.CrevasseNear(x, z, 3f) || LiftLine.Taken(x, z, 4f)) return false;
                if (Course.Keep(x, z, 2f) || Hamlets.Taken(x, z) || NightLights.Taken(x, z)) return false;
            }
        }
        return true;
    }

    /// <summary>Down a line from z0 at RideSpeed (held there: a rider checking speed in the trees), steering along it; how far it got.</summary>
    static float Ride(int side, int k, float z0, out bool crashed)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float x0 = Forest.LaneX(side, k, z0);
        riding = (side, k);
        rider.ResetAt(x0, z0); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        // (already riding along the line, as a rider in it would be: where it starts swung across a sidehill, a bot set off
        // straight down the fall line had the hill pulling it the other way and never got back onto it)
        float along = Mathf.Atan2(Forest.LaneX(side, k, z0 + 6f) - Forest.LaneX(side, k, z0 - 6f), 12f);
        rider.Heading = along;
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(new Vector3(Mathf.Sin(along), 0f, Mathf.Cos(along)), Slope.Normal(x0, z0)).normalized * RideSpeed;
        int n = 0;
        for (float t = 0f; t < 40f && rider.Body.position.z < z0 + RideLen; t += Dt, n++)
        {
            var p = rider.Body.position;
            float ahead = 12f, want = Mathf.Atan2(Forest.LaneX(side, k, p.z + ahead) - p.x, ahead);
            rider.Steer = Mathf.Clamp((want - rider.Heading) * 2.2f, -1f, 1f);
            var v = rider.Body.linearVelocity;
            if (v.magnitude > RideSpeed) rider.Body.linearVelocity = v * (RideSpeed / v.magnitude);
            rider.TestContacts(Dt);
            TestHarness.Step(rider, body, Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 2 == 0 || rider.Crashed) body.Apply(rider.Crashed ? Dt : 2f * Dt);
            if (rider.Crashed) break;
            if (Forest.LanesOn && n % 120 == 0 && System.Environment.GetEnvironmentVariable("TREELINE_TRACE") == "1")
                Debug.Log($"TREELINE trace z {p.z:0}: {p.x - Forest.LaneX(side, k, p.z):0.0} m off, heading {rider.Heading * Mathf.Rad2Deg:0} want {want * Mathf.Rad2Deg:0} steer {rider.Steer:0.00}, ground tilt across {Mathf.Atan(Slope.Gradient(p.x, p.z).x) * Mathf.Rad2Deg:0} deg, speed {rider.Speed:0.0}, air {rider.Airborne}");
        }
        crashed = rider.Crashed;
        return rider.Body.position.z - z0;
    }
}
