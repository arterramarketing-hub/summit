using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The snow underfoot (Surfaces.cs, mountain audit E1), checked.
///
/// Run (no graphics needed):
///   Unity -batchmode -nographics -projectPath . -executeMethod SurfaceTest.Run -logFile surface.log
///  - where each kind lies on a few mountains, on four days (calm and clear, snowy, windy, a warm afternoon): its share
///    of the groomed run and of the snow beside it, and how long the run's ice patches are. A map of each mountain is
///    written to SURFACE_OUT (default surface-shots);
///  - that the snow never changes in a step: the most any kind's share changes over a metre, anywhere;
///  - what each kind does to the pace: the same line ridden hands off on each kind laid everywhere, and the speed a
///    board settles at on the mountain's mean pitch;
///  - that the snow never pushes and never lets go (slide): on each kind half a drag held gives the same sideways
///    speed, the way the board goes turns by the finger's push and gravity's pull and by nothing else, and riding
///    from corduroy onto each kind with the finger held leaves the sideways speed as it was.
///
/// Shots (needs the GPU):
///   Unity -batchmode -projectPath . -executeMethod SurfaceTest.Shots -logFile shots.log
///  - the chase camera's view riding each kind, where it lies on the mountain by itself, into SURFACE_OUT.
/// </summary>
public static class SurfaceTest
{
    const float Dt = 1f / 120f;
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Snow[] Kinds = { Snow.Corduroy, Snow.Powder, Snow.Ice, Snow.Crud, Snow.Crust, Snow.Slush };
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("SURFACE_SEEDS") ?? "1000,1077,4242,77").Split(','), int.Parse);
    static string OutDir => System.Environment.GetEnvironmentVariable("SURFACE_OUT") ?? "surface-shots";
    static int fails;
    static void Fail(string s) { fails++; Debug.Log("SURFACE-FAIL " + s); }

    struct DayOf { public string name; public Surfaces.Weathering day; }
    static readonly DayOf[] Days =
    {
        new DayOf { name = "calm, cold, clear" },
        new DayOf { name = "snowy", day = new Surfaces.Weathering { fresh = 1f, wind = 0.1f } },
        new DayOf { name = "windy", day = new Surfaces.Weathering { wind = 1f } },
        new DayOf { name = "warm afternoon", day = new Surfaces.Weathering { warm = 1f } },
    };

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        Physics.simulationMode = SimulationMode.Script;
        TestPrefs.Pin();
        System.IO.Directory.CreateDirectory(OutDir);
        Surfaces.Force = null;

        // ---- where each kind lies
        foreach (int seed in Seeds)
        {
            Slope.Init(seed);
            {
                // (the run's own pitch, as the ice goes by it: rise over run down the fall line, over 6 m)
                var ps = new List<float>();
                for (float z = Slope.StartZ + 100f; z < Slope.FinishZ; z += 1f) ps.Add((Slope.FallLine(z - 3f) - Slope.FallLine(z + 3f)) / 6f);
                ps.Sort();
                float P(float q) => ps[Mathf.Clamp(Mathf.RoundToInt(q * (ps.Count - 1)), 0, ps.Count - 1)];
                Debug.Log($"SURFACE pitch seed {seed}: median {P(0.5f):0.00}, a quarter over {P(0.75f):0.00}, a tenth over {P(0.9f):0.00}, 5% over {P(0.95f):0.00}, 2% over {P(0.98f):0.00}, the most {P(1f):0.00} (ice from {Surfaces.IceFrom:0.00}, all of it by {Surfaces.IceFull:0.00})");
            }
            foreach (var d in Days)
            {
                Surfaces.Day = d.day;
                var on = new double[6]; var off = new double[6]; long nOn = 0, nOff = 0;
                var patches = new List<float>(); float patchFrom = -1f;
                float z0 = Slope.StartZ + 10f, z1 = Slope.FinishZ;
                for (float z = z0; z < z1; z += 2f)
                {
                    float c = Slope.Centre(z), half = Slope.HalfAt(z);
                    for (float a = -half + 1f; a <= half - 1f; a += 2f) { var m = Surfaces.Surface(c + a, z); for (int k = 0; k < 6; k++) on[k] += m[Kinds[k]]; nOn++; }
                    foreach (float side in new[] { -1f, 1f })
                        for (float a = half + 12f; a <= half + 60f; a += 4f) { var m = Surfaces.Surface(c + side * a, z); for (int k = 0; k < 6; k++) off[k] += m[Kinds[k]]; nOff++; }
                    // (ice down the middle: how long each patch is)
                    float ice = 0f;
                    for (float a = -6f; a <= 6f; a += 3f) ice = Mathf.Max(ice, Surfaces.Surface(c + a, z).ice);
                    if (ice > 0.4f) { if (patchFrom < 0f) patchFrom = z; }
                    else if (patchFrom >= 0f) { patches.Add(z - patchFrom); patchFrom = -1f; }
                }
                string S(double[] v, long n) { var sb = new System.Text.StringBuilder(); for (int k = 0; k < 6; k++) sb.Append($" {Kinds[k].ToString().ToLower()} {100.0 * v[k] / n:0.0}%"); return sb.ToString(); }
                float total = 0f, longest = 0f; foreach (float p in patches) { total += p; longest = Mathf.Max(longest, p); }
                Debug.Log($"SURFACE where seed {seed}, {d.name}: on the run{S(on, nOn)}; beside it{S(off, nOff)}; ice down the middle in {patches.Count} patches, {total:0} m of {z1 - z0:0} m, the longest {longest:0} m");
                if (d.name.StartsWith("calm")) Map(seed);
            }
        }
        Surfaces.Day = default;

        // ---- never a step: the most any share changes over a metre, down lines and across them
        foreach (int seed in Seeds)
        {
            Slope.Init(seed);
            float worst = 0f, wx = 0f, wz = 0f; string wk = "";
            void Pair(SnowMix a, SnowMix b, float per, float x, float z)
            {
                for (int k = 0; k < 6; k++) { float g = Mathf.Abs(a[Kinds[k]] - b[Kinds[k]]) / per; if (g > worst) { worst = g; wx = x; wz = z; wk = Kinds[k].ToString().ToLower(); } }
            }
            foreach (var d in Days)
            {
                Surfaces.Day = d.day;
                for (float off = -60f; off <= 60f; off += 4f)
                {
                    var prev = Surfaces.Surface(Slope.Centre(Slope.StartZ) + off, Slope.StartZ);
                    for (float z = Slope.StartZ + 0.25f; z < Slope.FinishZ + 60f; z += 0.25f)
                    {
                        float x = Slope.Centre(z) + off;
                        var m = Surfaces.Surface(x, z);
                        Pair(prev, m, 0.25f, x, z); prev = m;
                    }
                }
                for (float z = Slope.StartZ; z < Slope.FinishZ + 60f; z += 7f)
                {
                    float c = Slope.Centre(z);
                    var prev = Surfaces.Surface(c - 90f, z);
                    for (float a = -89.75f; a <= 90f; a += 0.25f) { var m = Surfaces.Surface(c + a, z); Pair(prev, m, 0.25f, c + a, z); prev = m; }
                }
            }
            Surfaces.Day = default;
            Debug.Log($"SURFACE steps seed {seed}: the most a share changes is {worst:0.00} per metre ({wk}, at x-c={wx - Slope.Centre(wz):+0;-0} z={wz:0})");
            if (System.Environment.GetEnvironmentVariable("SURFACE_PROBE") == "1")
                for (float dz = -1f; dz <= 1f; dz += 0.25f)
                    for (float dx = -0.5f; dx <= 0.5f; dx += 0.5f)
                    {
                        var m = Surfaces.Surface(wx + dx, wz + dz);
                        Debug.Log($"SURFACE probe x={wx + dx:0.00} z={wz + dz:0.00}: half {Slope.Half(wz + dz):0.00} across {Slope.Across(wx + dx, wz + dz):0.00} cord {m.Corduroy:0.00} powder {m.powder:0.00} ice {m.ice:0.00} crud {m.crud:0.00} crust {m.crust:0.00}");
                    }
            if (worst > 0.75f) Fail($"seed {seed}: {wk} changes {worst:0.00} per metre at x-c={wx - Slope.Centre(wz):+0;-0} z={wz:0}");
        }

        // ---- the pace on each kind
        Slope.Init(1000);
        var r = new GameObject("Rider").AddComponent<Rider>();
        r.input = null;
        // (the speed a board settles at standing up on the mountain's mean pitch, by the board's own numbers)
        float pitch = Mathf.Atan(Slope.Fall), basePace = 0f, baseAvg = 0f;
        var lines = new List<string>();
        foreach (var k in Kinds)
        {
            Surfaces.Force = SnowMix.Only(k);
            float v = 20f;
            for (int i = 0; i < 4000; i++)
            {
                var f = Surfaces.Feel(Surfaces.Force.Value, v);
                float acc = Rider.G * Mathf.Sin(pitch) - (Rider.DragOpen + f.drag) * v * v - Rider.Friction * f.friction * Rider.G * Mathf.Cos(pitch);
                v = Mathf.Max(0f, v + acc * 0.02f);
            }
            // (and ridden: the same line, hands off but for holding it, from a standing start, 1500 m of the run)
            Free(r, 30f, out float avg, out float top, out float secs, out float air, out bool crashed);
            if (k == Snow.Corduroy) { basePace = v; baseAvg = avg; }
            lines.Add($"SURFACE pace {k.ToString().ToLower(),-8}: settles at {v * 3.6f:0} km/h on the mean pitch ({100f * (v / basePace - 1f):+0;-0}%); 1500 m of the run ridden in {secs:0.0} s, {avg * 3.6f:0} km/h on average ({100f * (avg / baseAvg - 1f):+0;-0}%), {top * 3.6f:0} at most, {100f * air:0}% of it in the air{(crashed ? ", CRASHED" : "")}");
        }
        foreach (var l in lines) Debug.Log(l);

        // ---- the snow never pushes and never lets go: on each kind half a drag held gives the same sideways speed,
        //      and the way the board goes turns by the finger's push and gravity's pull and by nothing else (what
        //      NudgeTest counts as a nudge is 2.5 m/s^2 of anything else)
        float halfSide = 0.5f * r.SideFull(22f);
        foreach (var k in Kinds)
        {
            Surfaces.Force = SnowMix.Only(k);
            Carved(r, 22f, 0.5f, null, out _, out float side, out float off, out _);
            Carved(r, 22f, 0f, null, out _, out float side0, out float off0, out _);
            Debug.Log($"SURFACE held {k.ToString().ToLower(),-8}: half a drag held at 79 km/h gives {side:0.0} m/s sideways (the board's own: {halfSide:0.0}), {off:0.00} m/s^2 of the turn is nothing of the push's or gravity's; hands off {side0:0.00} m/s sideways, {off0:0.00} not accounted for");
            if (off > 0.5f || off0 > 0.5f) Fail($"{k}: the way the board goes turns by {Mathf.Max(off, off0):0.00} m/s^2 that is not the push's or gravity's, the finger still");
            if (side < 0.9f * halfSide) Fail($"{k}: halfSide a drag gives {side:0.0} m/s sideways, not {halfSide:0.0}");
        }
        // from corduroy onto each kind over 4 m, half a drag held: the sideways speed as it was
        foreach (var k in Kinds)
        {
            Carved(r, 22f, 0.5f, SnowMix.Only(k), out float before, out float after, out float off, out _);
            Debug.Log($"SURFACE onto {k.ToString().ToLower(),-8} off corduroy over 4 m at 79 km/h, half a drag held: sideways {before:0.0} -> {after:0.0} m/s, {off:0.00} m/s^2 not accounted for");
            if (off > 0.5f) Fail($"onto {k}: the way the board goes turns by {off:0.00} m/s^2 that is not the push's or gravity's");
            if (Mathf.Abs(after - before) > 0.1f * Mathf.Max(before, 1f)) Fail($"onto {k}: the sideways speed goes from {before:0.0} to {after:0.0} m/s with the finger still");
        }
        Surfaces.Force = null;

        Debug.Log($"SURFACETEST {(fails == 0 ? "PASS" : "FAIL")} ({fails} failures)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- rides (a bare rider on the mountain: no trees)

    const float PaceZ = 520f, PaceOff = 12f;
    static float Hold(Rider r, float tx) => Mathf.Clamp((tx - r.Body.position.x) * 0.15f - r.Heading * 1.2f, -1f, 1f);

    static void Free(Rider r, float v0, out float avg, out float top, out float secs, out float air, out bool crashed)
    {
        r.ResetAt(Slope.Centre(PaceZ) + PaceOff, PaceZ); r.Begin();
        r.Body.linearVelocity = new Vector3(0f, 0f, v0);
        top = 0f; int n = 0, inAir = 0; crashed = false;
        for (; n < 120 * 120 && r.Z < PaceZ + 1500f; n++)
        {
            r.Steer = Hold(r, Slope.Centre(r.Z + 12f) + PaceOff);
            r.Tick(Dt); Physics.Simulate(Dt);
            if (r.Crashed) { crashed = true; break; }
            if (r.Airborne) inAir++;
            top = Mathf.Max(top, r.Speed);
        }
        secs = n * Dt; avg = (r.Z - PaceZ) / Mathf.Max(secs, 1e-3f); air = inAir / (float)Mathf.Max(1, n);
    }

    // where the held-speed rides are made: found once, on corduroy as the mountain lays it, clear of airs
    static float lineX, lineZ; static bool lineFound;
    static void FindLine(Rider r)
    {
        if (lineFound) return;
        var force = Surfaces.Force; Surfaces.Force = SnowMix.Only(Snow.Corduroy);
        foreach (float z in new[] { 600f, 800f, 1000f, 1200f, 400f, 1500f, 1700f, 1900f, 2100f })
            foreach (float off in new[] { 10f, -10f, 0f })
            {
                lineX = Slope.Centre(z) + off; lineZ = z;
                bool bad = false;
                Start(r, 22f);
                for (int i = 0; i < 120 * 5 && !bad; i++) { r.Steer = 0f; StepHeld(r, 22f); if (r.Airborne || r.Crashed || r.Grinding) bad = true; }
                if (!bad) { lineFound = true; Surfaces.Force = force; Debug.Log($"SURFACE line rides at x-c={off:+0;-0} z={z:0}"); return; }
            }
        Surfaces.Force = force;
        Debug.LogWarning("SURFACE no clean line found; using the last one tried");
        lineFound = true;
    }

    static void Start(Rider r, float v)
    {
        r.ResetAt(lineX, lineZ - 20f); r.Begin();
        r.Body.linearVelocity = new Vector3(0f, 0f, v);
        for (int i = 0; i < 120 * 2 && r.Z < lineZ; i++) { r.Steer = 0f; StepHeld(r, v); }
    }

    static void StepHeld(Rider r, float v)
    {
        r.Tick(Dt);
        if (!r.Airborne && !r.Crashed) { var vel = r.Body.linearVelocity; float h = vel.magnitude; if (h > 0.5f) r.Body.linearVelocity = vel * (v / h); }
        Physics.Simulate(Dt);
    }

    /// <summary>
    /// The finger thrown to `s` and held for a second at v, on the snow as it is forced (or, with `to`, going from
    /// corduroy to `to` over 4 m of the ride from half a second in): the sideways speed across the fall line before
    /// (0.3 to 0.5 s in) and after (the last 0.2 s) in m/s, the most of the way it goes turning that is neither the
    /// finger's push nor gravity's pull (m/s^2, over a tenth of a second), and whether the board was thrown across.
    /// </summary>
    static void Carved(Rider r, float v, float s, SnowMix? to, out float before, out float after, out float off, out bool slid)
    {
        FindLine(r);
        var force = Surfaces.Force;
        if (to.HasValue) Surfaces.Force = default(SnowMix);
        Start(r, v);
        float steer = 0f, zFrom = 0f; double b = 0, a = 0; int nb = 0, na = 0; off = 0f; slid = false;
        Vector3 vPrev = r.Body.linearVelocity; var gap = new float[12]; float gapSum = 0f;
        for (int i = 0; i < 120; i++)
        {
            steer = Mathf.Lerp(steer, s, 1f - Mathf.Exp(-Dt * 60f)); r.Steer = steer;
            if (to.HasValue && i >= 60)
            {
                if (i == 60) zFrom = r.Z;
                float t = Mathf.Clamp01((r.Z - zFrom) / 4f); t = t * t * (3f - 2f * t);
                var m = to.Value;
                Surfaces.Force = new SnowMix { powder = m.powder * t, ice = m.ice * t, crud = m.crud * t, crust = m.crust * t, slush = m.slush * t };
            }
            StepHeld(r, v);
            Vector3 u = r.Body.linearVelocity;
            float h = Mathf.Sqrt(vPrev.x * vPrev.x + vPrev.z * vPrev.z);
            float path = h > 2f && !r.Airborne ? (vPrev.z * u.x - vPrev.x * u.z) / h / Dt : r.SideEdge + r.SideSlope;
            float g = path - (r.SideEdge + r.SideSlope);
            gapSum += g - gap[i % gap.Length]; gap[i % gap.Length] = g;
            if (i >= gap.Length) off = Mathf.Max(off, Mathf.Abs(gapSum) / gap.Length);
            if (i >= 36 && i < 60) { b += Mathf.Abs(r.Side); nb++; }
            if (i >= 96) { a += Mathf.Abs(r.Side); na++; }
            if (r.Sliding) slid = true;
            vPrev = u;
        }
        before = nb > 0 ? (float)(b / nb) : 0f; after = na > 0 ? (float)(a / na) : 0f;
        Surfaces.Force = force;
    }

    // ---------------------------------------------------------------- the map

    static readonly Color32[] Tint =
    {
        new Color32(236, 240, 246, 255),   // corduroy
        new Color32(255, 255, 255, 255),   // powder (drawn as white with a blue cast below)
        new Color32(70, 150, 235, 255),    // ice
        new Color32(190, 150, 110, 255),   // crud
        new Color32(170, 120, 200, 255),   // crust
        new Color32(110, 120, 130, 255),   // slush
    };

    /// <summary>The whole run from above, 160 m across, a pixel every 2 m: what snow is where (in strips side by side, the top of the mountain at the top left).</summary>
    static void Map(int seed)
    {
        const int W = 80, StripH = 440, Gap = 6;
        float z0 = Slope.StartZ, z1 = Slope.FinishZ + 60f;
        int rows = Mathf.CeilToInt((z1 - z0) / 2f), strips = Mathf.CeilToInt(rows / (float)StripH);
        var tex = new Texture2D(strips * (W + Gap), StripH, TextureFormat.RGB24, false);
        var px = new Color32[tex.width * tex.height];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(40, 44, 52, 255);
        for (int row = 0; row < rows; row++)
        {
            float z = z0 + row * 2f, c = Slope.Centre(z), half = Slope.HalfAt(z);
            int s = row / StripH, y = StripH - 1 - row % StripH;
            for (int i = 0; i < W; i++)
            {
                float a = (i - W / 2 + 0.5f) * 2f;
                var m = Surfaces.Surface(c + a, z);
                Vector3 col = Vector3.zero;
                for (int k = 0; k < 6; k++) { var t = k == 1 ? new Color32(205, 222, 244, 255) : Tint[k]; col += new Vector3(t.r, t.g, t.b) * m[Kinds[k]]; }
                if (Mathf.Abs(Mathf.Abs(a) - half) < 1f) col *= 0.8f;   // (the groomed run's edge)
                px[y * tex.width + s * (W + Gap) + i] = new Color32((byte)col.x, (byte)col.y, (byte)col.z, 255);
            }
        }
        tex.SetPixels32(px); tex.Apply();
        string file = $"{OutDir}/map_s{seed}.png";
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log($"SURFACE map {file} (corduroy pale, powder light blue, ice blue, crud brown, crust violet, slush grey; 2 m a pixel, {StripH * 2} m a strip)");
    }

    // ---------------------------------------------------------------- shots (needs the GPU)

    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static ChaseCamera chase; static MethodInfo camStep;

    public static void Shots()
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
        chase = Object.FindFirstObjectByType<ChaseCamera>();
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        System.IO.Directory.CreateDirectory(OutDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        int seed = int.Parse(System.Environment.GetEnvironmentVariable("SURFACE_SEED") ?? "1000");
        TestBoot.NewMountain(game, seed, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        chase.enabled = false;   // (stepped by hand, on the test's own clock)
        var cam = Camera.main;
        const int W = 590, H = 1280;
        var rt = new RenderTexture(W, H, 24);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        var sheet = new Texture2D(W * 6, H, TextureFormat.RGB24, false);

        for (int k = 0; k < Kinds.Length; k++)
        {
            var kind = Kinds[k];
            Surfaces.Force = null;
            Surfaces.Day = new Surfaces.Weathering { wind = kind == Snow.Crust ? 1f : 0f };
            TimeOfDay.TestClock = kind == Snow.Slush ? 1.9f : 1f + TimeOfDay.HeldAt;
            Surfaces.Day.warm = Surfaces.Warmth(TimeOfDay.TestClock);
            // where the mountain lays the most of it, with 60 m of it to ride into (the run's middle for what lies on
            // the run, beside it for what lies there)
            float bestZ = 0f, bestA = 0f, best = -1f;
            float[] offs = kind == Snow.Powder ? new[] { 34f, -34f } : kind == Snow.Crud ? new[] { 8f, -8f, 14f, -14f } : kind == Snow.Crust ? new[] { 0f, 10f, -10f } : new[] { 0f, 5f, -5f };
            for (float z = Course.First + 200f; z < Slope.FinishZ - 60f; z += 5f)
                foreach (float a in offs)
                {
                    float least = 1f;
                    for (float d = 0f; d <= (kind == Snow.Ice ? 20f : 60f); d += 5f) least = Mathf.Min(least, Surfaces.Surface(Slope.Centre(z + d) + a, z + d)[kind]);   // (the ice lies in patches: 20 m of it)
                    // (open snow: not in the park, the trees or the rocks, and not where the ride would leave the ground)
                    var sec = Course.At(z + 20f); var sec0 = Course.At(z - 30f);
                    if (sec == Course.Section.Danger || sec0 == Course.Section.Danger || (kind != Snow.Crud && (sec == Course.Section.Park || sec0 == Course.Section.Park))) continue;
                    bool steepHere = false;
                    for (float d = -30f; d <= 40f; d += 10f) if (Slope.Normal(Slope.Centre(z + d) + a, z + d).y < 0.74f) steepHere = true;
                    if (steepHere) continue;
                    if (least > best + 0.02f) { best = least; bestZ = z; bestA = a; }
                }
            float x = Slope.Centre(bestZ) + bestA, v0 = 15f;
            rider.ResetAt(Slope.Centre(bestZ - 25f) + bestA, bestZ - 25f); body.ResetVisual(); rider.Begin();
            rider.Heading = 0f; rider.Steer = 0f;
            rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, bestZ - 25f)).normalized * v0;
            terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
            chase.rider = rider; chase.Snap();
            // ride into it, carving a little so the spray shows
            for (int n = 0; n < 120 * 6 && rider.Z < bestZ + (kind == Snow.Ice ? 6f : 22f); n++)
            {
                rider.Steer = rider.Crashed ? 0f : 0.45f * Mathf.Sin(n * Dt * 2.6f) + Mathf.Clamp((Slope.Centre(rider.Z + 10f) + bestA - rider.X) * 0.05f, -0.3f, 0.3f);
                if (rider.Speed > v0 && !rider.Airborne && !rider.Crashed) rider.Body.linearVelocity *= v0 / rider.Speed;
                rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (n % 2 == 0)
                {
                    body.Apply(2 * Dt);
                    ChaseCamera.TestDt = 2 * Dt;
                    camStep.Invoke(chase, null);
                    Surfaces.Step(rider.Body.position, rider.RunNow, 2 * Dt, true);
                    Surfaces.Day.warm = Surfaces.Warmth(TimeOfDay.TestClock);
                }
            }
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.isPlaying) ps.Simulate(0.0001f, false, false, false);
            cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            string file = $"{OutDir}/{k}_{kind.ToString().ToLower()}.png";
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            sheet.SetPixels(k * W, 0, W, H, tex.GetPixels());
            var here = rider.Underfoot;
            Debug.Log($"SURFACE shot {file}: seed {seed} z={rider.Z:0} x-c={rider.X - Slope.Centre(rider.Z):+0;-0}, {100f * here[kind]:0}% {kind.ToString().ToLower()} under the board ({100f * best:0}% at least over the 60 m), {rider.Speed * 3.6f:0} km/h{(rider.Crashed ? ", CRASHED" : "")}{(rider.Airborne ? ", in the air" : "")}, map {100f * Surfaces.Painted(rider.Body.position):0}% painted");
        }
        sheet.Apply();
        System.IO.File.WriteAllBytes($"{OutDir}/surfaces_sheet.png", sheet.EncodeToPNG());

        // and each kind laid on the same stretch of the run, in the same light: only the snow differs
        Surfaces.Day = default; TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        float sameZ = 0f;
        for (float z = Course.First + 500f; z < Slope.FinishZ - 300f && sameZ == 0f; z += 10f)
        {
            bool ok = Course.At(z - 30f) == Course.Section.Cruise && Course.At(z + 60f) == Course.Section.Cruise;
            for (float d = -30f; d <= 60f && ok; d += 10f) if (Slope.Normal(Slope.Centre(z + d), z + d).y < 0.76f || Surfaces.Surface(Slope.Centre(z + d), z + d).Corduroy < 0.99f) ok = false;
            if (ok) sameZ = z;
        }
        for (int k = 0; k < Kinds.Length; k++)
        {
            Surfaces.Force = SnowMix.Only(Kinds[k]);
            rider.ResetAt(Slope.Centre(sameZ - 25f), sameZ - 25f); body.ResetVisual(); rider.Begin();
            rider.Heading = 0f; rider.Steer = 0f;
            rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(sameZ - 25f), sameZ - 25f)).normalized * 15f;
            terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
            chase.rider = rider; chase.Snap();
            for (int n = 0; n < 120 * 6 && rider.Z < sameZ + 22f; n++)
            {
                rider.Steer = rider.Crashed ? 0f : 0.45f * Mathf.Sin(n * Dt * 2.6f) + Mathf.Clamp((Slope.Centre(rider.Z + 10f) - rider.X) * 0.05f, -0.3f, 0.3f);
                if (rider.Speed > 15f && !rider.Airborne && !rider.Crashed) rider.Body.linearVelocity *= 15f / rider.Speed;
                rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (n % 2 == 0) { body.Apply(2 * Dt); ChaseCamera.TestDt = 2 * Dt; camStep.Invoke(chase, null); Surfaces.Step(rider.Body.position, rider.RunNow, 2 * Dt, true); }
            }
            cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            sheet.SetPixels(k * W, 0, W, H, tex.GetPixels());
            Debug.Log($"SURFACE same spot {Kinds[k].ToString().ToLower()}: z={rider.Z:0}, {rider.Speed * 3.6f:0} km/h{(rider.Crashed ? ", CRASHED" : "")}{(rider.Airborne ? ", in the air" : "")}");
        }
        cam.targetTexture = null; RenderTexture.active = null;
        sheet.Apply();
        System.IO.File.WriteAllBytes($"{OutDir}/surfaces_same_spot.png", sheet.EncodeToPNG());
        Debug.Log($"SURFACE sheet {OutDir}/surfaces_same_spot.png (each kind laid on the same stretch at z={sameZ:0}: corduroy, powder, ice, crud, crust, slush)");
        Debug.Log($"SURFACE sheet {OutDir}/surfaces_sheet.png (corduroy, powder, ice, crud, crust, slush)");
        Surfaces.Force = null; Surfaces.Day = default; TimeOfDay.TestClock = -1f; ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }
}
