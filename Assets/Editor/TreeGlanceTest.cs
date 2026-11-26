using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (arcadetrees) Trees forgive a glancing hit: ridden into a real tree of the forest, as the game plays it (the rider's own
/// step and its look ahead for what it meets):
///  - clipped off-centre at speed: turned along it, staggering, some speed lost (10-25%), still riding, and the run's free
///    hit (for riders, ridercrash) not used;
///  - square on but slow: bumped aside, still riding;
///  - square on and fast: down.
/// On three mountains, a lone tree each (nothing else on the way in). Logs TREEGLANCE lines; exits 1 on any failure.
/// Batch: -batchmode -nographics -projectPath . -executeMethod TreeGlanceTest.Run
/// </summary>
public static class TreeGlanceTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static int fails, checks;
    static SummitGame game; static Summit.Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;

    static void Check(string what, bool ok, string got = "")
    {
        checks++; if (!ok) fails++;
        Debug.Log($"TREEGLANCE {(ok ? "PASS" : "FAIL")} {what} {got}");
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Summit.Rider>("rider"); body = Get<RiderBody>("body");
        terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        string why = null; rider.Crash += w => why = w;
        try
        {
            foreach (int seed in new[] { 1000, 1154, 2131 })
            {
                TestBoot.NewMountain(game, seed, terrain, props);
                if (!LoneTree(out Vector3 foot))
                {
                    Check($"seed {seed}: a lone tree to ride into", false);
                    continue;
                }
                // glancing: past it off-centre, the board's sphere just catching its hit shape
                foreach (float off in new[] { 0.5f, 0.8f })
                {
                    var res = RideAt(foot, off, 19f, ref why, true);
                    Check($"seed {seed}: clipped {off:0.00} off centre at {res.into0:0} m/s: still riding", !res.crashed, $"crash '{why}'");
                    Check($"seed {seed}: clipped {off:0.00}: a glance", res.glances == 1, $"glances {res.glances}");
                    Check($"seed {seed}: clipped {off:0.00}: 10-25% of the speed lost", res.after >= res.before * 0.72f && res.after <= res.before * 0.93f, $"{res.before:0.0} -> {res.after:0.0} m/s ({100f * (1f - res.after / Mathf.Max(0.01f, res.before)):0}% lost)");
                    Check($"seed {seed}: clipped {off:0.00}: turned along it", res.turn > 3f, $"heading changed {res.turn:0.0} deg");
                    Check($"seed {seed}: clipped {off:0.00}: the free hit not used", !rider.FreeHitUsed);
                }
                // square on, slow: bumped aside
                var slow = RideAt(foot, 0f, 7f, ref why);
                Check($"seed {seed}: square on at {slow.before:0} m/s: bumped aside, still riding", !slow.crashed && slow.glances >= 1, $"crash '{why}', glances {slow.glances}");
                // square on, fast: down
                var hard = RideAt(foot, 0f, 19f, ref why);
                Check($"seed {seed}: square on at {hard.before:0} m/s: down", hard.crashed && why != null && why.StartsWith("Tree"), $"crash '{why}', glances {hard.glances}");
            }
        }
        catch (System.Exception e) { Debug.LogError("TREEGLANCE exception " + e); fails++; }
        Debug.Log(fails == 0 ? $"TREEGLANCE PASS ({checks} checks)" : $"TREEGLANCE FAIL ({fails} of {checks})");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>A tree in the lower forest with nothing else standing in a lane 20 m up the hill from it, on open, even ground.</summary>
    static bool LoneTree(out Vector3 foot)
    {
        foot = default;
        // (the lower forest, either side, out to 140 m: wherever a thick lone tree stands on open, even ground)
        for (float z = 1900f; z < Slope.FinishZ - 200f; z += 13f)
            for (int side = -1; side <= 1; side += 2)
                for (float a = 26f; a < 140f; a += 6.5f)
                {
                    float x = Slope.Centre(z) + side * a;
                    int cx = Mathf.FloorToInt(x / Forest.Cell), cz = Mathf.FloorToInt(z / Forest.Cell);
                    if (Forest.At(cx, cz, out Vector3 p, out _, out _) != Forest.Kind.Tree) continue;
                    if (!Clear(p)) continue;
                    // its hit shape, where the forest builds it round the rider
                    rider.ResetAt(p.x, p.z - 14f); props.Prewarm(); terrain.Prewarm();
                    if (TreeCollider(p) == null) continue;
                    if (!props.TreeIn(cx, cz, out var tr) || tr.girth < 0.3f) continue;   // (a thin trunk is ridden through: TreeBreak)
                    foot = p;
                    Debug.Log($"TREEGLANCE tree at {p} (cell {cx},{cz})");
                    return true;
                }
        return false;
    }

    static bool Clear(Vector3 p)
    {
        int c0x = Mathf.FloorToInt((p.x - 7f) / Forest.Cell), c1x = Mathf.FloorToInt((p.x + 7f) / Forest.Cell);
        int c0z = Mathf.FloorToInt((p.z - 24f) / Forest.Cell), c1z = Mathf.FloorToInt((p.z + 6f) / Forest.Cell);
        for (int cz = c0z; cz <= c1z; cz++)
            for (int cx = c0x; cx <= c1x; cx++)
            {
                if (Forest.At(cx, cz, out Vector3 q, out _, out _) == Forest.Kind.None) continue;
                if ((q - p).sqrMagnitude < 0.01f) continue;
                if (Mathf.Abs(q.x - p.x) < 6f && q.z > p.z - 24f && q.z < p.z + 6f) return false;
            }
        for (float z = p.z - 22f; z <= p.z + 4f; z += 1f)
            for (float x = p.x - 3f; x <= p.x + 3f; x += 1.5f)
            {
                if (Slope.Normal(x, z).y < 0.74f || Deadly.Below(x, z) || Course.CrevasseNear(x, z, 2f) || LiftLine.Taken(x, z, 4f)) return false;
                if (Course.Keep(x, z, 2f) || Mathf.Abs(Slope.Across(x, z)) < Slope.Half(z) + 4f) return false;
            }
        return true;
    }

    static Collider TreeCollider(Vector3 foot)
    {
        foreach (var c in Physics.OverlapSphere(foot + Vector3.up * 0.6f, 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            var ob = c.GetComponentInParent<Obstacle>();
            if (ob != null && ob.label == "Tree") return c;
        }
        return null;
    }

    /// <summary>How far to the side of the trunk the board's sphere, coming straight down at it from 14 m, still meets its hit shape.</summary>
    static float Reach(Vector3 foot)
    {
        var c = TreeCollider(foot);
        if (c == null) return 1f;
        float best = 0f;
        for (float d = 0f; d < 5f; d += 0.05f)
        {
            float x = foot.x + d;
            var a = new Vector3(x, Slope.DrawnHeight(x, foot.z - 14f) + Summit.Rider.R, foot.z - 14f);
            var b = new Vector3(x, Slope.DrawnHeight(x, foot.z + 1f) + Summit.Rider.R, foot.z + 1f);
            if (c.Raycast(new Ray(a, (b - a).normalized), out _, (b - a).magnitude)) { best = d; continue; }
            foreach (var h in Physics.SphereCastAll(a, Summit.Rider.R, (b - a).normalized, (b - a).magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider == c) { best = d; break; }
        }
        return Mathf.Max(0.3f, best);
    }

    static float Radius(Vector3 foot)
    {
        var c = TreeCollider(foot);
        if (c == null) return 0.5f;
        // the hit shape's reach at the board's height: how far its surface stands from the trunk, either way across
        float r = 0f;
        foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            r = Mathf.Max(r, Vector3.Distance(Obstacle.Nearest(c, foot + Vector3.up * 0.3f + dir * 8f), foot + Vector3.up * 0.3f));
        return Mathf.Clamp(r, 0.2f, 4f);
    }

    struct Result { public bool crashed; public int glances; public float before, after, turn, into0; }

    /// <summary>A new run 14 m up the hill from the tree, `d` m to the side of it, going `speed` straight down at it: ridden until 1 s past it.</summary>
    static Result RideAt(Vector3 foot, float d, float speed, ref string why, bool share = false)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null; why = null;
        float x0 = foot.x + d, z0 = foot.z - 14f;
        props.Rebuild();   // (every ride into the same tree as it stood: none broken or felled by the last)
        rider.ResetAt(x0, z0); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        Physics.SyncTransforms();
        // (a share of how far to the side the board's sphere still meets the hit shape, as the forest has built it now)
        if (share) { float reach = Reach(foot); d *= reach; Debug.Log($"TREEGLANCE ride: its hit shape is met out to {reach:0.00} m to the side; {d:0.00} off"); x0 = foot.x + d; rider.ResetAt(x0, z0); body.ResetVisual(); rider.Begin(); }
        var n = Slope.Normal(x0, z0);
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, n).normalized * speed;
        var res = new Result();
        float hit = -1f, h0 = 0f, closest = 99f; int dbg = 0;
        var tb = TreeBreak.Instance; int s0 = tb != null ? tb.SmallTrees + tb.Tops + tb.TrunkSnaps : 0, b0 = tb != null ? tb.Brushes : 0;
        for (float t = 0f; t < 4f; t += Dt)
        {
            rider.Steer = 0f;
            // the line straight at it held (no carving away before it): the heading kept down the hill until it is met
            if (hit < 0f)
            {
                // (aimed at the point d to the side of the trunk, level with it: the slope's camber would carry it off)
                var q = rider.Body.position; var v = rider.Body.linearVelocity; float s = v.magnitude;
                var aim = new Vector3(foot.x + d - q.x, 0f, Mathf.Max(0.5f, foot.z - q.z));
                rider.Body.linearVelocity = Vector3.ProjectOnPlane(aim.normalized, Slope.Normal(q.x, q.z)).normalized * Mathf.Min(s, speed);
                rider.Heading = Mathf.Atan2(aim.x, aim.z);
            }
            int g0 = rider.TreeGlances;
            float before = rider.Body.linearVelocity.magnitude;
            Vector3 vb = rider.Body.linearVelocity;
            rider.TestContacts(Dt);
            var tcol = TreeCollider(foot); bool would = false; float wd = 0f; Vector3 wn = default;
            if (tcol != null && vb.sqrMagnitude > 0.01f)
                foreach (var h in Physics.SphereCastAll(rider.Body.position, Summit.Rider.R, vb.normalized, vb.magnitude * Dt + 0.04f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider == tcol) { would = true; wd = h.distance; wn = h.normal; }
            rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (would && hit < 0f && rider.TreeGlances == g0 && !rider.Crashed && dbg++ < 3)
                Debug.Log($"TREEGLANCE debug: the cast meets it (d {wd:0.000}, normal {wn}) but nothing happened: layer {tcol.gameObject.layer} ignore {Physics.GetIgnoreLayerCollision(rider.gameObject.layer, tcol.gameObject.layer)}, enabled {tcol.enabled}, ob {(tcol.GetComponentInParent<Obstacle>() != null)}, rides over {rider.RidesOver(tcol)}, crashed {rider.Crashed}, running {rider.Running}, air {rider.Airborne}, grinding {rider.Grinding}");
            if (hit < 0f && (rider.TreeGlances > g0 || rider.Crashed))
            {
                hit = t; res.before = before; res.into0 = before; h0 = Mathf.Atan2(vb.x, vb.z) * Mathf.Rad2Deg;
                res.after = rider.Body.linearVelocity.magnitude;   // (the step it was met: later, the slope speeds it up again)
            }
            if (hit >= 0f && t <= hit + 0.25f && !rider.Crashed)
            {
                var va = rider.Body.linearVelocity;
                res.turn = Mathf.Max(res.turn, Mathf.Abs(Mathf.DeltaAngle(h0, Mathf.Atan2(va.x, va.z) * Mathf.Rad2Deg)));
            }
            var rp = rider.Body.position; closest = Mathf.Min(closest, new Vector2(rp.x - foot.x, rp.z - foot.z).magnitude);
            if (rider.Crashed) break;
            if (hit >= 0f && t > hit + 1f) break;
            if (rider.Body.position.z > foot.z + 6f && hit < 0f) break;   // (missed it)
        }
        // (down against this tree: turned off along it, the rider can go on into another in the forest round it, which is
        // that tree's business, not this one's)
        float fromFoot = rider.Crashed ? new Vector2(rider.HitPoint.x - foot.x, rider.HitPoint.z - foot.z).magnitude : 0f;
        res.crashed = rider.Crashed && fromFoot < 2.5f; res.glances = rider.TreeGlances;
        if (rider.Crashed && !res.crashed) Debug.Log($"TREEGLANCE note: after the glance, down against another tree {fromFoot:0.0} m on ('{why}')");
        if (hit < 0f)
        {
            var tc = TreeCollider(foot);
            Debug.Log($"TREEGLANCE note: d {d:0.00} at {speed:0} m/s never met the tree (passed at x {rider.Body.position.x - foot.x:0.00} off, y {rider.Body.position.y - Slope.DrawnHeight(rider.Body.position.x, rider.Body.position.z):0.00} over the snow, air {rider.Airborne}); its hit shape {(tc == null ? "gone" : tc.enabled ? "there" : "off")}, rides over {rider.RidesOver(tc)}; closest to the trunk {closest:0.00} m; tree breaks {(tb != null ? tb.SmallTrees + tb.Tops + tb.TrunkSnaps - s0 : -1)}, brushes {(tb != null ? tb.Brushes - b0 : -1)}");
        }
        return res;
    }
}
