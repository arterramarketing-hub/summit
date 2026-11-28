using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Trees you ride through (TreeBreak): only a full-size tree's trunk is a crash; its branches are brushed through,
/// a small tree's thin trunk and a tall tree's top snap, and you ride on.
///
/// Run (counts, no GPU needed): Unity -batchmode -nographics -projectPath . -executeMethod BranchTest.Run -logFile b.log
///  - the forest's size spread: the trunk's radius where a rider meets it at the snow, and how much of the tree
///    shows, for the trees within reach of the piste, down the mountain (what Forest.ThinGirth was chosen from);
///  - rides through the forest beside the piste on BRANCH_SEEDS mountains (default 4), 8 lanes at 15 and 25 m/s,
///    two cycles long, starting again past each fall: counts trunk crashes, branch brushes, treetops clipped,
///    small trees snapped, and checks that none of the last three ever ends a run; and what a brush costs.
/// Batchmode has no collision callbacks: a rider meeting a solid shape is handled here as Rider.OnCollisionEnter
/// does (after Rider.Tick, before the physics step, as in the game's FixedUpdate).
///
/// Renders (needs the GPU): -executeMethod BranchTest.Shots: a treetop clipped in the air, a small tree snapped, a
/// brush through branches on each side, and a trunk hit that still crashes; each before, during, after and the tree
/// afterwards, by day and by night. Writes BranchShot_*.png in the project folder. Log lines start BRANCH.
/// </summary>
public static class BranchTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static TreeBreak breaker; static Smasher smasher;
    static Camera cam; static ChaseCamera chase; static MethodInfo late; static Atmosphere atmo; static DayNight dayNight;
    static int n;
    static bool shooting;
    static string lastCrash = "";
    static float clock, lastEvent = -9f;
    static Collider hitBy;

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static void Log(string s) => Debug.Log("BRANCH " + s);

    static void Boot()
    {
        TestPrefs.Pin();
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        breaker = F<TreeBreak>("treeBreak"); smasher = F<Smasher>("smasher"); dayNight = F<DayNight>("dayNight");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.Crash += why => lastCrash = why;
        breaker.Brushed += (a, b, c) => lastEvent = clock;
        breaker.Broke += (a, b, c) => lastEvent = clock;
    }

    static void Mountain(int seed) => TestBoot.NewMountain(game, 5000 + seed * 977, terrain, props);

    // ------------------------------------------------------------------ the counts

    public static void Run()
    {
        int code = 0;
        try
        {
            Boot();
            Log($"forest realistic={props.IsRealistic} ThinGirth={Forest.ThinGirth} m");
            int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("BRANCH_SEEDS"), out int s) ? s : 4;
            for (int i = 0; i < seeds; i++) { Mountain(i); Sizes(i); }
            TreeBreak.Profile = true;
            for (int i = 0; i < seeds; i++) { Mountain(i); Rides(i); }
            Report();
            Timing();
        }
        catch (System.Exception e) { Debug.LogError("BRANCH exception " + e); code = 1; }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(code);
    }

    // the trunk's radius at the snow and the height shown, of every tree within reach of the piste, by band down the mountain
    static readonly List<float>[] girths = { new List<float>(), new List<float>(), new List<float>(), new List<float>() };
    static readonly List<float>[] shows = { new List<float>(), new List<float>(), new List<float>(), new List<float>() };

    static void Sizes(int seed)
    {
        r.ResetAt(Slope.Centre(Course.First + 30f), Course.First + 30f); props.Prewarm();
        int z0 = Mathf.FloorToInt(Course.First / Forest.Cell), z1 = Mathf.FloorToInt((Course.First + 2f * Course.Cycle) / Forest.Cell);
        for (int cz = z0; cz < z1; cz++)
        {
            float wz = (cz + 0.5f) * Forest.Cell;
            int band = Mathf.Min(3, (int)(Forest.Low(wz) * 4f));
            int cxc = Mathf.FloorToInt(Slope.Centre(wz) / Forest.Cell);
            for (int cx = cxc - 14; cx <= cxc + 14; cx++)
            {
                if (!props.TreeIn(cx, cz, out var t)) continue;
                if (Mathf.Abs(t.at.x - Slope.Centre(t.at.z)) > Slope.PisteHalf + 40f) continue;
                girths[band].Add(t.girth); shows[band].Add(t.vis);
            }
        }
    }

    static float Pct(List<float> l, float q) { if (l.Count == 0) return 0f; var c = new List<float>(l); c.Sort(); return c[Mathf.Clamp((int)(q * (c.Count - 1)), 0, c.Count - 1)]; }
    static float Below(List<float> l, float x) { int k = 0; foreach (var v in l) if (v < x) k++; return l.Count > 0 ? 100f * k / l.Count : 0f; }

    // what the rides met
    static int trunkCrashes, otherCrashes, crashAfterEvent, rides;
    static readonly Dictionary<string, int> crashWhy = new Dictionary<string, int>();
    static readonly List<(float deep, float share)> brushCosts = new List<(float, float)>();
    static int brushes, deepBrushes, tops, smalls, trunkSnaps;
    static float metres;

    static void Rides(int seed)
    {
        var rng = new System.Random(seed * 13 + 5);
        float zEnd = Course.First + 2f * Course.Cycle;
        float[] lanes = { -(Slope.PisteHalf + 4f), -(Slope.PisteHalf + 11f), -(Slope.PisteHalf + 19f), -(Slope.PisteHalf + 30f),
                          Slope.PisteHalf + 4f, Slope.PisteHalf + 11f, Slope.PisteHalf + 19f, Slope.PisteHalf + 30f };
        breaker.BrushDone = (deep, speedIn, lost) => { if (speedIn > 1f) brushCosts.Add((deep, lost / speedIn)); };
        foreach (float v0 in new[] { 15f, 25f })
            foreach (float lane in lanes)
            {
                float z = Course.First - 20f;
                int guard = 0;
                while (z < zEnd && guard++ < 300)
                {
                    float x = Slope.Centre(z) + lane;
                    breaker.Clear(); smasher.Clear();
                    r.ResetAt(x, z); r.Begin();
                    r.Heading = 0f;
                    r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
                    lastCrash = ""; lastEvent = -9f;
                    props.Prewarm(); terrain.Prewarm();
                    float phase = (float)rng.NextDouble() * 10f, amp = 0.3f + 0.5f * (float)rng.NextDouble();
                    float t = 0f, z0 = r.Z;
                    rides++;
                    while (!r.Crashed && t < 90f && r.Z < zEnd)
                    {
                        float want = Slope.Centre(r.Z + 14f) + lane + 5f * Mathf.Sin(t * 0.5f + phase);
                        r.Steer = Mathf.Clamp(amp * Mathf.Sin(t * 1.3f + phase) * 0.5f + (want - r.X) * 0.06f - r.Heading * 0.6f, -1f, 1f);
                        Step(); t += Time.fixedDeltaTime;
                        if (r.Speed > v0 * 1.3f) r.Body.linearVelocity *= 0.98f;
                        if (r.Speed < v0 * 0.6f && !r.Airborne) r.Body.linearVelocity *= 1.02f;   // (the forest's brushes and the deep snow: keep it moving)
                    }
                    metres += Mathf.Max(0f, r.Z - z0);
                    brushes += breaker.Brushes; deepBrushes += breaker.DeepBrushes; tops += breaker.Tops; smalls += breaker.SmallTrees; trunkSnaps += breaker.TrunkSnaps;
                    if (r.Crashed) Crashed(seed, lane, v0);
                    z = r.Z + 25f;
                    if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Time.fixedDeltaTime); }
                }
            }
        Log($"seed{seed}: after {rides} rides, {metres / 1000f:0.0} km: trunk crashes {trunkCrashes}, other crashes {otherCrashes}, brushes {brushes} (deep {deepBrushes}), treetops {tops}, small trees {smalls}, fast trunk snaps {trunkSnaps}");
    }

    static void Crashed(int seed, float lane, float v0)
    {
        bool tree = hitBy != null && hitBy.name == "TreeHit";
        if (lastCrash == "Tree" || lastCrash == "Tree snapped!") trunkCrashes++; else otherCrashes++;
        crashWhy[lastCrash] = crashWhy.TryGetValue(lastCrash, out int k) ? k + 1 : 1;
        bool soon = clock - lastEvent < 0.4f;
        if (soon) crashAfterEvent++;
        string note = "";
        if (tree && props.TreeOf(hitBy, out var t))
        {
            Vector3 rel = r.transform.position - t.at; float h = Vector3.Dot(rel, t.up);
            float d = (rel - t.up * h).magnitude;
            note = $" trunk r={Forest.TrunkAt(t, h):0.00} at {h:0.0} m up, board {d:0.00} m from its axis (girth {t.girth:0.00}, shows {t.vis:0.0} m, solid to {t.solid:0.0} m)";
        }
        if (lastCrash.StartsWith("Tree") || soon)
            Debug.Log($"BRANCH-CRASH seed{seed} lane{lane:0} v{v0:0}: '{lastCrash}' at z={r.Z:0} {r.Speed * 3.6f:0} km/h airborne={r.Airborne}{(soon ? " (0.4 s after a brush or a break)" : "")}{note}");
    }

    static void Report()
    {
        for (int b = 0; b < 4; b++)
        {
            var g = girths[b]; var v = shows[b];
            Log($"sizes band {b} (low {b * 0.25f:0.00}-{(b + 1) * 0.25f:0.00}): {g.Count} trees; trunk radius at the snow p10 {Pct(g, 0.1f):0.00} p25 {Pct(g, 0.25f):0.00} p50 {Pct(g, 0.5f):0.00} p75 {Pct(g, 0.75f):0.00} p90 {Pct(g, 0.9f):0.00} m; " +
                $"shows p10 {Pct(v, 0.1f):0.0} p50 {Pct(v, 0.5f):0.0} p90 {Pct(v, 0.9f):0.0} m; under 0.10: {Below(g, 0.10f):0}%, 0.12: {Below(g, 0.12f):0}%, 0.15: {Below(g, 0.15f):0}%, 0.18: {Below(g, 0.18f):0}%, 0.22: {Below(g, 0.22f):0}%");
        }
        var all = new List<float>(); var allV = new List<float>();
        for (int b = 0; b < 4; b++) { all.AddRange(girths[b]); allV.AddRange(shows[b]); }
        var thinV = new List<float>(); for (int i = 0; i < all.Count; i++) if (all[i] < Forest.ThinGirth) thinV.Add(allV[i]);
        Log($"sizes all: {all.Count} trees; under ThinGirth {Forest.ThinGirth}: {Below(all, Forest.ThinGirth):0}%, they show p10 {Pct(thinV, 0.1f):0.0} p50 {Pct(thinV, 0.5f):0.0} p90 {Pct(thinV, 0.9f):0.0} m");
        Log($"rides: {rides} through the forest, {metres / 1000f:0.0} km");
        Log($"rides: trunk crashes {trunkCrashes}; other crashes {otherCrashes}; branch brushes {brushes} (of them deep, past halfway in: {deepBrushes}); treetops clipped {tops}; small trees snapped {smalls}; young trunks/snags snapped fast (a crash) {trunkSnaps}");
        foreach (var kv in crashWhy) Log($"crash '{kv.Key}': {kv.Value}");
        Log($"crashes within 0.4 s of a brush or a break: {crashAfterEvent} (each listed in BRANCH-CRASH lines: what was hit)");
        brushCosts.Sort((a, b) => a.deep.CompareTo(b.deep));
        foreach (var (lo, hi) in new[] { (0f, 0.25f), (0.25f, 0.5f), (0.5f, 0.75f), (0.75f, 1.01f) })
        {
            var sh = new List<float>(); foreach (var c in brushCosts) if (c.deep >= lo && c.deep < hi) sh.Add(c.share);
            float mean = 0f; foreach (var x in sh) mean += x; mean /= Mathf.Max(1, sh.Count);
            Log($"brush cost, {lo:0.00}-{Mathf.Min(1f, hi):0.00} deep: {sh.Count} brushes, speed lost mean {100f * mean:0.0}% (p90 {100f * Pct(sh, 0.9f):0.0}%)");
        }
    }

    /// <summary>What it costs (in the editor, on this Mac): the per-step look round the rider, a damaged tree's
    /// move out of the instanced batches, and the full rebuild of the forest's lists that a felled tree still takes.</summary>
    static void Timing()
    {
        double us = 1e6 / System.Diagnostics.Stopwatch.Frequency;
        Log($"timing: RiderAhead {TreeBreak.ProfileTicks * us / Mathf.Max(1, TreeBreak.ProfileCalls):0.0} us a physics step on average, over {TreeBreak.ProfileCalls} steps");
        Mountain(0);
        float z = Course.First + 400f; r.ResetAt(Slope.Centre(z), z); props.Prewarm();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) props.Prewarm();
        Log($"timing: a full rebuild of the forest's draw lists (what Fell still does): {sw.Elapsed.TotalMilliseconds / 10:0.00} ms");
        var list = new List<Forest.Tree>(); int n = 0; sw.Restart();
        for (int dz = 0; dz < 6; dz++)
        {
            props.TreesNear(new Vector3(Slope.Centre(z + dz * 7f) + 22f, 0f, z + dz * 7f), list);
            foreach (var t in list) { props.TearBranches(t, t.at + t.up, 1f); n++; }
        }
        Log($"timing: tearing branches in {n} trees (each moved out of the instanced lists): {sw.Elapsed.TotalMilliseconds * 1000 / Mathf.Max(1, n):0} us each");
    }

    static void Step()
    {
        float dt = Time.fixedDeltaTime;
        clock += dt;
        r.Tick(dt);
        // the physics step about to move the board: what it would run into (Rider.OnCollisionEnter, from the board's sphere)
        if (!r.Crashed && r.Running) Contact(dt);
        body.PhysicsStep();
        Physics.Simulate(dt);
        breaker.Step(dt); smasher.Step(dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (shooting && n % 4 == 0)
        {
            body.Apply(4 * dt);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.name != "Snowfall") ps.Simulate(4 * dt, false, false, false);
            late.Invoke(chase, null);
        }
        n++;
    }

    static readonly Collider[] near = new Collider[16];
    static void Contact(float dt)
    {
        hitBy = null;
        Vector3 p = r.Body.position, v = r.Body.linearVelocity;
        int k = Physics.OverlapCapsuleNonAlloc(p, p + v * dt, Rider.R, near, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < k; i++)
        {
            var c = near[i];
            if (c.attachedRigidbody == r.Body || !c.enabled) continue;
            var ob = c.GetComponentInParent<Obstacle>();
            if (ob == null) continue;
            if (r.InvincibleTime > 0f) continue;
            Vector3 cp = c.ClosestPoint(p); Vector3 nrm = p - cp; nrm = nrm.sqrMagnitude > 1e-6f ? nrm.normalized : -v.normalized;
            float into = Mathf.Abs(Vector3.Dot(v, nrm));
            if (ob.always) into = Mathf.Max(into, 10f);
            hitBy = c;
            if (into > 6f && !r.Invulnerable) { r.DoCrash(ob.label); return; }
            r.Body.linearVelocity = v * 0.85f;   // a scrape
            return;
        }
    }

    // ------------------------------------------------------------------ the renders

    public static void Shots()
    {
        int code = 0;
        try
        {
            Boot();
            shooting = true;
            ChaseCamera.TestDt = 4 * Time.fixedDeltaTime;
            int seed = int.TryParse(System.Environment.GetEnvironmentVariable("BRANCH_SEED"), out int s) ? s : 1;
            Mountain(seed);
            Log($"shots on seed {seed}, realistic={props.IsRealistic}");
            // one shot per editor tick: Graphics.RenderMesh draws stay queued until the frame ends, and a batchmode
            // method that never returns never ends one (the tree as it stood before a hit would show in every shot after)
            run = ShotsCo(seed).GetEnumerator();
            EditorApplication.update += Pump;
            return;
        }
        catch (System.Exception e) { Debug.LogError("BRANCH exception " + e); code = 1; }
        Finish(code);
    }

    static IEnumerator<object> run;
    static void Pump()
    {
        bool more;
        try { more = run.MoveNext(); }
        catch (System.Exception e) { Debug.LogError("BRANCH exception " + e); Finish(1); return; }
        if (!more) Finish(0);
    }

    static IEnumerable<object> ShotsCo(int seed)
    {
        foreach (var (time, clockAt) in new[] { ("day", 1.4f), ("night", 3.4f) })
        {
            TimeOfDay.TestClock = clockAt; dayNight.ApplyNow();
            foreach (var what in new[] { "Top", "Small", "BrushL", "BrushR", "Trunk" })
            {
                Mountain(seed);   // every tree whole again
                foreach (var x in Scene(what, time)) yield return x;
            }
        }
    }

    static void Finish(int code)
    {
        EditorApplication.update -= Pump;
        ChaseCamera.TestDt = 0f;
        TimeOfDay.TestClock = -1f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(code);
    }

    /// <summary>A tree for each scene, near the piste with a clear run at it from up the hill.</summary>
    static bool Pick(string what, int skip, out Forest.Tree tree)
    {
        tree = default;
        r.ResetAt(Slope.Centre(Course.First + 30f), Course.First + 30f); props.Prewarm();
        int z0 = Mathf.FloorToInt((Course.First + 120f) / Forest.Cell), z1 = Mathf.FloorToInt((Course.First + 2f * Course.Cycle) / Forest.Cell);
        for (int cz = z0; cz < z1; cz++)
        {
            float wz = (cz + 0.5f) * Forest.Cell;
            int cxc = Mathf.FloorToInt(Slope.Centre(wz) / Forest.Cell);
            for (int cx = cxc - 9; cx <= cxc + 9; cx++)
            {
                if (!props.TreeIn(cx, cz, out var t)) continue;
                Vector3 p = t.at;
                if (Mathf.Abs(p.x - Slope.Centre(p.z)) > Slope.PisteHalf + 14f) continue;
                bool ok = what switch
                {
                    "Top" => t.girth >= Forest.ThinGirth && t.vis > 5f && t.vis < 13f && t.solid < t.vis - 3f,
                    "Small" => t.girth < Forest.ThinGirth && t.vis > 2.5f && t.vis < 9f,
                    "Trunk" => t.girth >= Forest.ThinGirth + 0.04f && !props.Snappable(cx, cz, out _),
                    _ => t.girth >= Forest.ThinGirth && Forest.CrownAt(t, 0.6f) > 1.3f && Forest.CrownAt(t, 1.2f) > 1.2f,   // a brush: branches down to the snow
                };
                if (!ok) continue;
                bool smooth = true;
                for (float dz = -30f; dz <= 8f && smooth; dz += 2f)
                    if (Course.CrevasseCut(p.x, p.z + dz) > 0.01f || Mathf.Abs(Slope.Kicker(p.x, p.z + dz)) > 0.01f || RockFace.IsRock(Slope.Normal(p.x, p.z + dz), p.x, p.z + dz)) smooth = false;
                if (!smooth) continue;
                bool clear = true;   // nothing else in the way for 40 m up the hill, nor just past it
                for (int dz = -7; dz <= 2 && clear; dz++)
                    for (int dx = -2; dx <= 2 && clear; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        if (Forest.At(cx + dx, cz + dz, out Vector3 q, out _, out _) == Forest.Kind.None) continue;
                        if (Mathf.Abs(q.x - p.x) < 5.5f && q.z < p.z + 12f) clear = false;
                    }
                if (!clear) continue;
                if (skip-- > 0) continue;
                tree = t;
                return true;
            }
        }
        return false;
    }

    static bool rideOk;
    static IEnumerable<object> Scene(string what, string time)
    {
        var only = System.Environment.GetEnvironmentVariable("BRANCH_ONLY");   // e.g. "Top,Small" or "Top:day"
        if (!string.IsNullOrEmpty(only) && !only.Contains(what)) yield break;
        if (!string.IsNullOrEmpty(only) && only.Contains(":") && !only.Contains(time)) yield break;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Mountain(int.TryParse(System.Environment.GetEnvironmentVariable("BRANCH_SEED"), out int s) ? s : 1);
            if (!Pick(what, attempt, out var t)) { Log($"{what}: no tree found"); yield break; }
            foreach (var x in Ride(what, time, t)) yield return x;
            if (rideOk) yield break;
        }
        Log($"{what} {time}: no attempt came out as wanted");
    }

    static IEnumerable<object> Ride(string what, string time, Forest.Tree t)
    {
        rideOk = false;
        string file = $"BranchShot_{what}_{time}";
        breaker.Clear(); smasher.Clear();
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 60 && r.Crashed; i++) Step(); }
        bool air = what == "Top", brush = what.StartsWith("Brush");
        float v0 = what == "Trunk" ? 16f : what == "Small" ? 18f : air ? 21f : 20f;
        // where to pass: through the trunk, or beside it, halfway into the branches at the board
        float lateral = 0f;
        if (brush)
        {
            float c = Forest.CrownAt(t, 0.5f), tr = Forest.TrunkAt(t, 0.5f);
            float ds = c + 0.2f - 0.5f * (c + 0.2f - tr);
            lateral = what == "BrushL" ? ds : -ds;   // BrushL: the tree on the rider's left (riding down +z, left is -x)
        }
        float back = air ? 13f : 24f;
        float x = t.at.x + lateral, z = t.at.z - back;
        r.ResetAt(x, z); body.ResetVisual(); r.Begin(); r.Heading = 0f;
        Vector3 dir = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized;
        r.Body.linearVelocity = dir * v0;
        if (air)
        {
            // flying at the tree's top: up at the height to meet it a little below its tip, on a falling line
            float h = Mathf.Max(t.solid + 1f, t.vis - 2.5f), tt = back / v0;
            float y = t.at.y + h + 0.5f * Rider.G * tt * tt;
            r.Body.position = new Vector3(x, y, z); r.transform.position = r.Body.position;
            r.Airborne = true;
            r.Body.linearVelocity = new Vector3(0f, 0f, v0);
        }
        terrain.Prewarm(); props.Prewarm(); chase.Snap();
        int b0 = breaker.Brushes, t0 = breaker.Tops, s0 = breaker.SmallTrees;
        bool shot0 = false, shot1 = false; float tEvent = -1f; string got = "", seen = "";
        Vector3 hitAt = t.at + t.up * 1f;
        System.Action<Vector3, Vector3, float> onBrush = (a, sd, dp) => { if (!brush || seen == "") hitAt = a; seen += $"brush({dp:0.00}) "; };
        System.Action<Vector3, float, bool> onBreak = (a, into, top) => { hitAt = a; seen += top ? "top " : "small "; };
        breaker.Brushed += onBrush; breaker.Broke += onBreak;
        float vIn = 0f;
        for (float tm = 0f; tm < 4.5f; tm += Time.fixedDeltaTime)
        {
            if (!r.Crashed) r.Steer = Mathf.Clamp((t.at.x + lateral - r.X) * 0.8f - r.Heading * 1.5f, -1f, 1f);
            if (!shot0 && t.at.z - r.Z < (air ? 9f : 10f)) { shot0 = true; Frame(file + "_0before.png"); vIn = r.Speed; yield return null; }
            Step();
            if (tEvent < 0f)
            {
                // this scene's own outcome (a brush comes first on the way into any tree)
                if (breaker.Tops > t0) got = "top clipped";
                else if (breaker.SmallTrees > s0) got = "small tree snapped";
                else if (r.Crashed) got = "crashed '" + lastCrash + "'";
                else if (brush && breaker.Brushes > b0) got = "brushed";
                if (got != "") { tEvent = 0f; Log($"{what} {time}: {got} at {r.Speed * 3.6f:0} km/h ({vIn * 3.6f:0} in), {r.transform.position.y - Slope.DrawnHeight(r.X, r.Z):0.0} m up"); }
            }
            else
            {
                tEvent += Time.fixedDeltaTime;
                if (!shot1 && tEvent > (brush ? 0.12f : 0.1f)) { shot1 = true; Frame(file + "_1during.png"); yield return null; }
                if (tEvent > 0.9f) { Frame(file + "_2after.png"); yield return null; break; }
            }
        }
        breaker.Brushed -= onBrush; breaker.Broke -= onBreak;
        Log($"{what} {time}: on the way: {seen}; crashed={r.Crashed} '{(r.Crashed ? lastCrash : "")}'");
        if (tEvent < 0f) { Log($"{what} {time}: nothing happened (rider at {r.transform.position}, tree at {t.at})"); yield break; }
        bool wanted = what switch { "Top" => got == "top clipped", "Small" => got == "small tree snapped", "Trunk" => got.StartsWith("crashed"), _ => got == "brushed" };
        // after a brush: did you ride on, and what did it cost
        Log($"{what} {time}: 0.9 s on: crashed={r.Crashed} at {r.Speed * 3.6f:0} km/h; tree damage on record={props.Damaged}; sprigs={breaker.SprigCount}; tops={breaker.Count}");
        if (breaker.TopEnds(out Vector3 butt, out Vector3 tip))
            Log($"{what} {time}: top piece butt {butt - t.at} tip {tip - t.at} (from the trunk's foot), {(tip - butt).magnitude:0.0} m long");
        Log($"{what} {time}: {ListsHolding(t.cell)} instanced copies of the tree still listed");
        // the tree afterwards, from the side the rider came past, a little up the hill
        float sideX = brush ? Mathf.Sign(lateral) : Mathf.Sign(Slope.Centre(t.at.z) - t.at.x + 0.01f);
        chase.enabled = false; float fov = cam.fieldOfView; cam.fieldOfView = 55f;
        // close on where it was hit: the gap in the branches, the broken top, the stump
        float far = what == "Top" ? 9f : 6f;
        Vector3 from = hitAt + new Vector3(sideX * far * 0.75f, 0f, -far * 0.65f);
        from.y = Mathf.Max(Slope.DrawnHeight(from.x, from.z) + 1.2f, what == "Top" ? hitAt.y - 3f : hitAt.y + 1.0f);
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(hitAt - from));
        DrawAll(); ShootBaked(file + "_3tree.png");
        yield return null;
        // and the whole tree, from further off
        float tall = Mathf.Min(t.vis, 14f);
        Vector3 from2 = t.at + new Vector3(sideX * (5f + tall * 0.55f), 0f, -(4f + tall * 0.5f));
        from2.y = Slope.DrawnHeight(from2.x, from2.z) + 1.6f + tall * 0.25f;
        cam.transform.SetPositionAndRotation(from2, Quaternion.LookRotation(t.at + t.up * (tall * 0.45f) - from2));
        DrawAll(); ShootBaked(file + "_4whole.png");
        yield return null;
        if (System.Environment.GetEnvironmentVariable("BRANCH_DEBUG") == "1")
        {
            // the standing tree alone (no falling top, no sprigs), and with a big gap torn in it, to see the damage draws
            breaker.Clear(); smasher.Clear();
            DrawAll(); ShootBaked(file + "_5alone.png");
            yield return null;
            if (props.TreeIn(t.cell.x, t.cell.y, out var tt)) props.TearBranches(tt, t.at + t.up * Mathf.Min(3f, t.vis * 0.4f) + Vector3.right * sideX * 1f, 2.5f);
            DrawAll(); ShootBaked(file + "_6biggap.png");
            yield return null;
        }
        cam.fieldOfView = fov; chase.enabled = true;
        if (!wanted) Log($"{what} {time}: got '{got}', not what this scene is for");
        rideOk = wanted;
    }

    /// <summary>How many of the forest's instanced lists still hold this tree (a damaged one should be in none).</summary>
    static int ListsHolding(Vector2Int cell)
    {
        var pl = typeof(Forest).GetMethod("Get", NP).Invoke(props, new object[] { cell.x, cell.y });
        var m = (Matrix4x4)pl.GetType().GetField("m").GetValue(pl);
        int k = 0;
        foreach (var name in new[] { "nearL", "farL", "richL" })
            foreach (var l in ((Dictionary<int, List<Matrix4x4>>)typeof(Forest).GetField(name, NP).GetValue(props)).Values)
                foreach (var x in l) if (x == m) k++;
        var imp = (List<Matrix4x4>[])typeof(Forest).GetField("impL", NP).GetValue(props);
        if (imp != null) foreach (var l in imp) foreach (var x in l) if (x == m) k++;
        return k;
    }

    static void DrawAll()
    {
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
        smasher.Draw(); breaker.Draw();
    }

    static void Frame(string file)
    {
        body.Apply(Time.fixedDeltaTime);
        late.Invoke(chase, null);
        DrawAll();
        ShootBaked(file);
    }

    static void ShootBaked(string file)
    {
        // batchmode renders do not re-skin meshes, so bake the skinned meshes to see the real pose
        var baked = new List<GameObject>();
        var skins = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            skins.Add(sk);
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g);
        }
        Shoot(file);
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
    }

    static bool warm;
    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        Canvas.ForceUpdateCanvases();
        if (!warm) { warm = true; cam.Render(); }   // the first render comes out unlit
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        Log("wrote " + file);
    }
}
