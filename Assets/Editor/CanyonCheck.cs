using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The ridge stretches' canyons and gorges, on a dozen mountains: no pocket a rider who goes down into one can be
/// held in. Every point of a 2 m grid over each stretch's canyons (the ground below the crest or the gorge's floor,
/// from the stretch's start to where its canyons have filled in), followed straight downhill (the steepest way down,
/// cell to cell) must reach the canyon's end or the run, not a hollow; a hollow is reported with how deep it is (how far
/// its rim stands above its bottom) and where; one over 1.5 m fails. Down on a canyon's floor (under 20 degrees across, below
/// a cliff or a snow side), along each line parallel to the run, the ground must not climb again by more than 0.5 m. Also a cross-section of seed 1000's first stretch at z 960-1040 on its
/// cliff side, metre by metre (CANYON_PROFILE=1).
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod CanyonCheck.Run -logFile canyon.log (CANYON_SEEDS=...)
/// Log lines: CANYON, CANYON-FAIL, CANYON done.
/// Ride: the real rider set down at the foot of the face where the farview session's bot was held for 50 s (seed 1000,
/// z 985, 32 m off the run on the cliff side) at a walk, the audit's bot steering back for the run (ScoreBot), and hands off:
/// it must get 80 m on down the canyon within 25 s. -executeMethod CanyonCheck.Ride
/// </summary>
public static class CanyonCheck
{
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("CANYON_SEEDS") ?? "1000,1077,1154,1231,1308,1385,1462,1539,1616,1693,2131,2208").Split(','), int.Parse);
    const float Step = 2f, Deepest = 0.25f, TooDeep = 1.5f;
    static int fails, stretches, worstSeed, worstClimbSeed; static float worst, worstClimb;

    public static void Run()
    {
        try
        {
            foreach (int seed in Seeds)
            {
                Slope.Init(seed); Course.ResetCaches();
                for (int c = 0; c <= Course.CycleOf(Course.ClearFrom); c++)
                    if (Ridge.Built(c)) Stretch(seed, c);
                if (seed == 1000 && System.Environment.GetEnvironmentVariable("CANYON_PROFILE") == "1") Profile();
                var want = System.Environment.GetEnvironmentVariable("CANYON_AT");   // seed,side,z0,z1: cross-sections
                if (want != null)
                {
                    var w = System.Array.ConvertAll(want.Split(','), float.Parse);
                    if ((int)w[0] == seed)
                        for (float z = w[2]; z <= w[3]; z += 6f)
                        {
                            var sb = new System.Text.StringBuilder();
                            for (float a = 0f; a <= 130f; a += 4f) sb.Append($"{Slope.Height(Slope.Centre(z) + w[1] * a, z) - Slope.FallLine(z):0} ");
                            Debug.Log($"CANYON x-section seed{seed} z={z:0} depth {Ridge.Depth(z):0.00} half {Ridge.HalfWidth(z):0} (every 4 m out): {sb}");
                        }
                }
            }
        }
        catch (System.Exception e) { fails++; Debug.Log("CANYON-FAIL exception " + e); }
        Debug.Log($"CANYON done: {stretches} stretches; deepest hollow {worst:0.00} m (seed {worstSeed}); most climbed along a floor {worstClimb:0.0} m (seed {worstClimbSeed}); {fails} failure(s)");
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static void Stretch(int seed, int c)
    {
        stretches++;
        float zb = Course.First + c * Course.Cycle;
        float z0 = zb + Ridge.At + 20f, z1 = zb + Ridge.EndOf(c) + Ridge.FadeAfter;
        var form = Ridge.FormOf(c);
        int nz = Mathf.CeilToInt((z1 - z0) / Step) + 1, nx = Mathf.CeilToInt(2f * 230f / Step) + 1;
        var h = new float[nx, nz];
        for (int j = 0; j < nz; j++)
        {
            float z = z0 + j * Step, cx = Slope.Centre(z);
            for (int i = 0; i < nx; i++) h[i, j] = Slope.Height(cx - 230f + i * Step, z);
        }
        // the hollows: a cell lower than all eight round it, below the crest's level (the canyon), not on the run
        int n = 0; float deep = 0f; string where = "";
        for (int j = 1; j < nz - 1; j++)
            for (int i = 1; i < nx - 1; i++)
            {
                float v = h[i, j];
                bool pit = true;
                for (int dj = -1; dj <= 1 && pit; dj++) for (int di = -1; di <= 1 && pit; di++) if ((di != 0 || dj != 0) && h[i + di, j + dj] <= v) pit = false;
                if (!pit) continue;
                float z = z0 + j * Step, a = -230f + i * Step;
                if (Mathf.Abs(a) < Ridge.HalfWidth(z) + 2f) continue;   // (the run's own rollers are not the canyon's business)
                var sk = Ridge.SideAt(z, a < 0f ? -1f : 1f);
                if (sk != Ridge.Side.Cliff && sk != Ridge.Side.Snow) continue;   // (nor a gorge's walls: its floor is the run)
                float d = Escape(h, i, j);
                if (d < Deepest) continue;
                n++;
                if (d > deep) { deep = d; where = $"z={z:0} {a:+0;-0} m off the middle (u {z - zb:0})"; }
            }
        if (deep > worst) { worst = deep; worstSeed = seed; }
        // the climbs: down on the floor (under 20 degrees across: on a fan at the face's foot, steeper, a rider slides
        // on out onto the floor), along each line parallel to the run, how far the ground rises again after a low point
        // (a rider held on a line there comes to a stop)
        int climbs = 0; float most = 0f; string at = "";
        for (int i = 0; i < nx; i++)
        {
            float a = -230f + i * Step;
            float low = float.MaxValue; int run = 0;
            for (int j = 0; j < nz; j++)
            {
                float z = z0 + j * Step;
                var kind = Ridge.SideAt(z, a < 0f ? -1f : 1f);   // (a canyon: below a cliff or a snow side; a gorge's walls and banks climb from the run itself)
                bool floor = (kind == Ridge.Side.Cliff || kind == Ridge.Side.Snow) && Mathf.Abs(a) > Ridge.HalfWidth(z) + 4f && i > 0 && i < nx - 1
                             && Mathf.Abs(h[i + 1, j] - h[i - 1, j]) < 0.36f * 2f * Step && Ridge.Depth(z) > 0.02f
                             && h[i, j] < Ridge.Top(z) - 4f;
                if (!floor) { low = float.MaxValue; run = 0; continue; }
                float v = h[i, j];
                if (v < low) { low = v; run = 0; continue; }
                float rise = v - low;
                if (rise > 0.5f && ++run == 1) climbs++;
                if (rise > most) { most = rise; at = $"z={z:0} {a:+0;-0} m off the middle (u {z - zb:0}, {kind}, from z={z - (v - low) * 0f:0}, depth {Ridge.Depth(z):0.00})"; }
            }
        }
        if (most > worstClimb) { worstClimb = most; worstClimbSeed = seed; }
        if (most > 0.5f && System.Environment.GetEnvironmentVariable("CANYON_PROFILE") == "1")
        {
            // along the worst line, every 4 m, 60 m either side of it: height off the fall line, and the depth
            var m = System.Text.RegularExpressions.Regex.Match(at, @"z=(-?\d+) ([+-]\d+) m");
            float zw = float.Parse(m.Groups[1].Value), aw = float.Parse(m.Groups[2].Value);
            var sb = new System.Text.StringBuilder();
            for (float z = zw - 60f; z <= zw + 20f; z += 4f) sb.Append($"{Slope.Height(Slope.Centre(z) + aw, z) - Slope.FallLine(z):0.0}/{Ridge.Depth(z):0.00} ");
            Debug.Log($"CANYON along seed{seed} at {aw:+0;-0} from z {zw - 60f:0}: {sb}");
        }
        string line = $"seed{seed} cycle{c} {form} z {z0:0}..{z1:0}: {n} hollow(s) over {Deepest} m deep" + (n > 0 ? $", the deepest {deep:0.00} m at {where}" : "")
                    + $"; {climbs} climb(s) over 0.5 m along the floor, the most {most:0.0} m" + (most > 0.5f ? $" at {at}" : "");
        Debug.Log("CANYON " + line);
        if (deep > TooDeep || most > 0.5f) { fails++; Debug.Log("CANYON-FAIL " + line); }
    }

    /// <summary>How far the water must rise in a hollow before it spills out (flood-filling from its bottom).</summary>
    static float Escape(float[,] h, int i0, int j0)
    {
        int nx = h.GetLength(0), nz = h.GetLength(1);
        float level = h[i0, j0];
        var seen = new HashSet<int>(); var edge = new SortedList<float, List<int>>();
        void Push(int i, int j) { int k = j * nx + i; if (!seen.Add(k)) return; float v = h[i, j]; if (!edge.TryGetValue(v, out var l)) edge[v] = l = new List<int>(); l.Add(k); }
        Push(i0, j0);
        float spill = level;
        int guard = 0;
        while (edge.Count > 0 && guard++ < 20000)
        {
            float v = edge.Keys[0]; var l = edge.Values[0]; int k = l[l.Count - 1]; l.RemoveAt(l.Count - 1); if (l.Count == 0) edge.RemoveAt(0);
            if (v < spill) return spill - level;   // (downhill again past the rim: out)
            spill = v;
            int i = k % nx, j = k / nx;
            if (i == 0 || j == 0 || i == nx - 1 || j == nz - 1) return spill - level;
            for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) if (di != 0 || dj != 0) Push(i + di, j + dj);
        }
        return spill - level;
    }

    static void Profile()
    {
        int c = 0;
        float side = Ridge.CliffSide(Course.First + Ridge.At + 100f, 1f) ? 1f : -1f;
        Debug.Log($"CANYON profile seed1000 cycle0 {Ridge.FormOf(c)}: cliff side {side:+0;-0}, ends {Course.First + Ridge.EndOf(c):0}, canyons to {Course.First + Ridge.EndOf(c) + Ridge.FadeAfter:0}");
        for (float z = 960f; z <= 1040f; z += 8f)
        {
            var sb = new System.Text.StringBuilder();
            float y0 = Slope.FallLine(z);
            for (float a = 0f; a <= 120f; a += 3f) sb.Append($"{Slope.Height(Slope.Centre(z) + side * a, z) - y0:0} ");
            Debug.Log($"CANYON z={z:0} depth {Ridge.Depth(z):0.00} (every 3 m out, height off the fall line): {sb}");
        }
        // along z, at fixed distances out
        foreach (float a in new[] { 30f, 40f, 50f, 60f, 70f, 80f })
        {
            var sb = new System.Text.StringBuilder();
            for (float z = 940f; z <= 1060f; z += 6f) sb.Append($"{Slope.Height(Slope.Centre(z) + side * a, z):0.0} ");
            Debug.Log($"CANYON along at {a:0} m out (every 6 m from z 940): {sb}");
        }
    }

    public static void Ride()
    {
        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        const float Dt = 1f / 120f;
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        r.input = null;
        var bot = typeof(MountainAudit).GetNestedType("ScoreBot", BindingFlags.NonPublic).GetMethod("Steer", BindingFlags.Public | BindingFlags.Static);
        TestBoot.NewMountain(game, 1000, terrain, props);
        float side = Ridge.CliffSide(Course.First + Ridge.At + 100f, 1f) ? 1f : -1f;
        foreach (float off in new[] { 26f, 32f, 40f })
            foreach (bool toRun in new[] { true, false })
            {
                float z0 = 985f, x0 = Slope.Centre(z0) + side * off;
                r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
                r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * 2f;
                terrain.Prewarm(); props.Prewarm();
                float t = 0f;
                for (int n = 0; t < 25f && r.Z < z0 + 80f && !r.Crashed; t += Dt, n++)
                {
                    r.Steer = toRun ? (float)bot.Invoke(null, new object[] { r, Slope.Centre(r.Z + 12f * Slope.Squash(r.Z)), t }) : 0f;   // (the audit's own bot)
                    r.TestContacts(Dt);
                    r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                    if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                    if (n % 4 == 0) body.Apply(4 * Dt);
                }
                string res = $"from {off:0} m out, {(toRun ? "steering for the run" : "hands off")}: {r.Z - z0:0} m down in {t:0.0} s, {r.Speed * 3.6f:0} km/h, {Mathf.Abs(r.X - Slope.Centre(r.Z)):0} m out" + (r.Crashed ? " (crashed)" : "");
                bool ok = r.Z >= z0 + 80f || r.Crashed;
                Debug.Log((ok ? "CANYON ride " : "CANYON-FAIL ride ") + res);
                if (!ok) fails++;
                if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
            }
        Debug.Log($"CANYON ride done: {fails} failure(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
