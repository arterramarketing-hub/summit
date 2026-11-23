using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The alpine bowl (Bowl, mountain audit A3), on a dozen mountains.
///  - Where: in the first cycle's ridge stretch, above the tree line (Forest.Low under Forest.TreeLine all the way), on
///    about two mountains in three; no ridge there (Ridge.FormOf Plain), the section reads Bowl.
///  - Shape: the shelf's pitch (about 25 degrees), the roll-over's and the floor's steepest (40-46), the funnel's; the
///    floor groomed wall to wall (Slope.Rough 0 out to Half), at least 170 m across; the walls standing 20 m or more
///    above the floor's middle at its edge. The profile closes: below the bowl the run is where it was.
///  - The reveal: from a rider's eye 1.6 m up on the shelf, 30 m above the roll's crest, how much of the snow past the
///    crest is out of sight (BlindRollTest's measure, over 150 m).
///  - Nothing floating: every tree, rock and piste kit item round each bowl and band stands on the snow (Floating).
///  - Nothing in it: no tree or rock (Forest.At) on its floor, no crevasse, no lift or lift hill, no kicker, no drop, no
///    ledge.
///  - Rides: the real rider down the middle at 15, 22 and 28 m/s; down a line 45 m off the middle each side at 22; and
///    carving wall to wall (a line swinging 70 m either way) at 22: no crash, out through the funnel.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod BowlTest.Run -logFile bowl.log (BOWL_SEEDS=...)
/// Shots (with the GPU): -executeMethod BowlTest.Shots (BOWL_OUT=dir; the first bowl among BOWL_SEEDS: one mountain a run). Log lines: BOWL, BOWL-FAIL, BOWL done.
/// </summary>
public static class BowlTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("BOWL_SEEDS") ?? "1000,1077,1154,1231,1308,1385,1462,1539,1616,1693,2131,2208").Split(','), int.Parse);
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static string lastCrash = "";
    static int fails, bowls, none, rides;
    static float hiddenSum;

    static void Fail(string s) { fails++; Debug.Log("BOWL-FAIL " + s); }

    static void Setup()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        r.input = null;
        r.Crash += why => lastCrash = why;
    }

    static void NewMountain(int seed)
    {
        TestBoot.NewMountain(game, seed, terrain, props);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) { var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt; dress.Prewarm(); }
    }

    static float Deg(float tan) => Mathf.Atan(tan) * Mathf.Rad2Deg;
    static float Mid(float z) => Slope.Height(Slope.Centre(z), z);
    /// <summary>The pitch down the middle at z, over 4 m.</summary>
    static float Pitch(float z) => (Mid(z - 2f) - Mid(z + 2f)) / 4f;

    /// <summary>How many metres of the middle past z0 are out of sight from 1.6 m up, 30 m above it (out to 150 m).</summary>
    static float Hidden(float z0)
    {
        float ze = z0 - 30f;
        Vector3 eye = new Vector3(Slope.Centre(ze), 0f, ze); eye.y = Slope.Height(eye.x, eye.z) + 1.6f;
        float hidden = 0f;
        for (float z = z0 + 1f; z < z0 + 150f; z += 1f)
        {
            Vector3 q = new Vector3(Slope.Centre(z), 0f, z); q.y = Slope.Height(q.x, q.z) + 0.1f;
            bool seen = true;
            for (int i = 1; i < 80 && seen; i++)
            {
                Vector3 p = Vector3.Lerp(eye, q, i / 80f);
                if (Slope.Height(p.x, p.z) > p.y + 0.02f) seen = false;
            }
            if (!seen) hidden += 1f;
        }
        return hidden;
    }

    public static void Run()
    {
        Setup();
        try
        {
            foreach (int seed in Seeds)
            {
                NewMountain(seed);
                for (int c = 0; c <= Course.CycleOf(Course.ClearFrom); c++)
                    if (CliffBand.Of(c, out var band)) Floating(seed, $"band cycle{c}", band.z0 - 40f, band.z3 + 60f);
                if (!Bowl.Of(0, out var s)) { none++; Debug.Log($"BOWL seed{seed}: none (drawn {(Bowl.Is(0) ? "yes, no room" : "no")}); stretch is {Ridge.FormOf(0)}"); continue; }
                bowls++;
                Check(seed, s);
                Floating(seed, $"bowl", s.zA - 40f, s.zE + 40f);
            }
        }
        catch (System.Exception e) { Fail("exception " + e); }
        Debug.Log($"BOWL done: {bowls} bowls on {bowls + none} mountains; out of sight past the crest from 30 m above: {hiddenSum / Mathf.Max(1, bowls):0} m on average; {rides} rides, {fails} failure(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static void Check(int seed, Bowl.Shape s)
    {
        string tag = $"seed{seed}";
        // where
        if (Forest.Low(s.zA) >= Forest.TreeLine || Forest.Low(s.zE) >= Forest.TreeLine) Fail($"{tag}: not all above the tree line (Low {Forest.Low(s.zA):0.00}..{Forest.Low(s.zE):0.00})");
        if (Ridge.FormOf(0) != Ridge.Form.Plain) Fail($"{tag}: the stretch has a {Ridge.FormOf(0)} too");
        if (Course.At(0.5f * (s.zF + s.zO)) != Course.Section.Bowl) Fail($"{tag}: the floor reads {Course.At(0.5f * (s.zF + s.zO))}");
        { bool sign = false; foreach (var sg in TrailSigns.Signs) if (sg.symbol == TrailSigns.Symbol.DoubleBlack && Mathf.Abs(sg.z - (s.zA - 25f)) < 12f) sign = true;
          if (!sign) Fail($"{tag}: no double black at the way in"); }
        // the profile
        float shelf = 0f; int ns = 0;
        for (float z = s.zA + 25f; z < s.zR - 2f; z += 2f) { shelf += Pitch(z); ns++; }
        shelf /= Mathf.Max(1, ns);
        float steep = 0f, steepZ = 0f;
        for (float z = s.zR; z < s.zO; z += 1f) { float p = Pitch(z); if (p > steep) { steep = p; steepZ = z; } }
        float funnel = 0f;
        for (float z = s.zO; z < s.zE; z += 1f) funnel = Mathf.Max(funnel, Pitch(z));
        float floorEnd = Pitch(s.zO - 6f);
        if (Deg(shelf) < 20f || Deg(shelf) > 31f) Fail($"{tag}: the shelf is {Deg(shelf):0} degrees");
        if (Deg(steep) < 37f || Deg(steep) > 48f) Fail($"{tag}: the roll-over's steepest is {Deg(steep):0} degrees at z={steepZ:0}");
        if (Deg(funnel) > 54f) Fail($"{tag}: the funnel is {Deg(funnel):0} degrees");
        float below = Mid(s.zE + 30f) - (Slope.FallLine(s.zE + 30f) + 0f);
        // the floor, at its middle
        float zm = 0.5f * (s.zF + s.zO), half = Slope.Half(zm), c = Slope.Centre(zm), y0 = Mid(zm);
        float rough = 0f; for (float a = -half + 1f; a <= half - 1f; a += 2f) rough = Mathf.Max(rough, Slope.Rough(c + a, zm));
        if (rough > 0.01f) Fail($"{tag}: the floor is rough ({rough:0.00}) inside its half-width {half:0}");
        if (2f * half < 170f) Fail($"{tag}: the floor is only {2f * half:0} m across");
        float wl = Slope.Height(c - Bowl.Reach, zm) - y0, wr = Slope.Height(c + Bowl.Reach, zm) - y0;
        if (Mathf.Min(wl, wr) < 18f) Fail($"{tag}: the walls stand {wl:0} / {wr:0} m above the floor's middle");
        // the steepest across the floor (it should be rideable everywhere: no more than 50 degrees off the fall line's own)
        float worst = 0f; string worstAt = "";
        for (float z = s.zA + 10f; z < s.zE - 5f; z += 4f)
            for (float a = -Slope.Half(z) + 1f; a <= Slope.Half(z) - 1f; a += 4f)
            {
                var n = Slope.Normal(Slope.Centre(z) + a, z);
                float g = Mathf.Acos(Mathf.Clamp01(n.y)) * Mathf.Rad2Deg;
                if (g > worst) { worst = g; worstAt = $"z={z:0} ({(z < s.zR ? "shelf" : z < s.zF ? "roll" : z < s.zO ? "floor" : "funnel")}) {a:+0;-0} m of half {Slope.Half(z):0}"; }
            }
        if (worst > 50f) Fail($"{tag}: {worst:0} degrees on the groomed bowl at {worstAt}");
        // the reveal
        float crest = s.zR + 0.5f * 40f;
        float hid = Hidden(crest);
        hiddenSum += hid;
        if (hid < 30f) Fail($"{tag}: only {hid:0} m out of sight past the crest from 30 m above");
        // nothing in it
        int trees = 0, crevs = 0, lifts = 0, kick = 0;
        for (float z = s.zA; z < s.zE; z += Forest.Cell)
            for (float a = -Slope.Half(z); a <= Slope.Half(z); a += Forest.Cell)
            {
                float x = Slope.Centre(z) + a;
                int cx = Mathf.FloorToInt(x / Forest.Cell), cz = Mathf.FloorToInt(z / Forest.Cell);
                var k = Forest.At(cx, cz, out var pos, out _, out _);
                if (k != Forest.Kind.None && Mathf.Abs(Slope.Across(pos.x, pos.z)) < Slope.Half(pos.z) - 1f && pos.z > s.zA && pos.z < s.zE) trees++;
                if (Course.CrevasseNear(x, z, 0f)) crevs++;
                if (LiftLine.Taken(x, z, 0f)) lifts++;
                if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f) kick++;
            }
        int drops = 0;
        for (int d = 1; d * Slope.DropSeg < s.zE + 20f; d++) if (Slope.DropOf(d, out var dr) && dr.LipZ > s.zA && dr.LipZ < s.zE) drops++;
        bool ledge = Cliffs.LedgeNear(0.5f * (s.zA + s.zE), 0.5f * (s.zE - s.zA), out _);
        if (trees + crevs + lifts + kick + drops > 0 || ledge) Fail($"{tag}: in the bowl: {trees} trees/rocks, {crevs} crevasse points, {lifts} lift points, {kick} kicker points, {drops} drops, ledges {ledge}");
        Debug.Log($"BOWL {tag}: z {s.zA:0}..{s.zE:0} (roll {s.zR:0}, floor {s.zF:0}..{s.zO:0}); shelf {Deg(shelf):0} deg, steepest {Deg(steep):0} at {steepZ - s.zR:0} m past the roll's start, floor's end {Deg(floorEnd):0}, funnel {Deg(funnel):0} (worst groomed {worst:0} at {worstAt}); floor {2f * half:0} m across, walls {wl:0}/{wr:0} m; {hid:0} m hidden past the crest; below it {below:+0.0;-0.0} m off the line");
        // the rides
        void Judge(string what, string res, bool crashed) { Debug.Log($"BOWL {tag} {what}: {res}"); if (crashed) Fail($"{tag} {what}: {res}"); }
        foreach (float v in new[] { 15f, 22f, 28f })
        {
            string res = Ride(s, z => 0f, v, out bool crashed);
            Judge($"middle at {v:0}", res, crashed);
        }
        foreach (float off in new[] { -45f, 45f })
        {
            string res = Ride(s, z => off * Mathf.Clamp01((z - s.zA) / 60f) * Mathf.Clamp01((s.zO - z) / 40f), 22f, out bool crashed);
            Judge($"line {off:+0;-0} at 22", res, crashed);
        }
        {
            string res = Ride(s, z => 70f * Mathf.Sin((z - s.zR) / 140f * Mathf.PI * 2f) * Mathf.Clamp01((z - s.zR) / 30f) * Mathf.Clamp01((s.zO - z) / 40f), 22f, out bool crashed);
            Judge("carving wall to wall at 22", res, crashed);
        }
    }

    /// <summary>
    /// Nothing floating: every tree and rock of the forest (Forest.At, the same pure layout it draws), and everything the
    /// piste kit puts up, from z0 to z1 and 300 m either side of the run, stands on the snow: its foot no more than 0.3 m
    /// above the ground the terrain draws there (Slope.DrawnHeight), nor 1 m above the mountain itself (Slope.Height),
    /// nor sunk more than 1 m into it.
    /// </summary>
    static void Floating(int seed, string what, float z0, float z1)
    {
        int n = 0, bad = 0; float worst = 0f; string at = "";
        int cz0 = Mathf.FloorToInt(z0 / Forest.Cell), cz1 = Mathf.CeilToInt(z1 / Forest.Cell);
        for (int cz = cz0; cz <= cz1; cz++)
        {
            float zc = (cz + 0.5f) * Forest.Cell, c = Slope.Centre(zc);
            int cx0 = Mathf.FloorToInt((c - 300f) / Forest.Cell), cx1 = Mathf.CeilToInt((c + 300f) / Forest.Cell);
            for (int cx = cx0; cx <= cx1; cx++)
            {
                var k = Forest.At(cx, cz, out var pos, out _, out _);
                if (k == Forest.Kind.None) continue;
                n++;
                // (on the snow as drawn: on steep curved ground the drawn facets stand a little off the true surface)
                float off = pos.y - Slope.DrawnHeight(pos.x, pos.z), offTrue = pos.y - Slope.Height(pos.x, pos.z);
                float sunk = -off;
                float e = Mathf.Max(Mathf.Max(off - 0.3f, offTrue - 1f), sunk - 1f);
                if (e > 0f) { bad++; if (e > worst) { worst = e; at = $"{k} at z={pos.z:0} {pos.x - Slope.Centre(pos.z):+0;-0} m off the middle, {off:+0.00;-0.00} m"; } }
            }
        }
        foreach (var it in PisteKit.Items)
        {
            if (it.foot.z < z0 || it.foot.z > z1) continue;
            n++;
            float off = it.foot.y - Slope.DrawnHeight(it.foot.x, it.foot.z);
            if (off > 0.3f || off < -1f) { bad++; if (Mathf.Abs(off) > worst) { worst = Mathf.Abs(off); at = $"{it.kind} at z={it.foot.z:0}, {off:+0.00;-0.00} m"; } }
        }
        Debug.Log($"BOWL seed{seed} {what}: {n} trees, rocks and piste kit standing; {bad} off the snow" + (bad > 0 ? $", the worst {at}" : ""));
        if (bad > 0) Fail($"seed{seed} {what}: {bad} things floating or sunk, the worst {at}");
    }

    static string Ride(Bowl.Shape s, System.Func<float, float> line, float v0, out bool crashed, System.Action<int> frame = null)
    {
        rides++;
        float z0 = s.zA - 20f, x0 = Slope.Centre(z0) + line(z0), zEnd = s.zE + 40f;
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * v0;
        lastCrash = ""; terrain.Prewarm(); props.Prewarm();
        { var dress = Object.FindFirstObjectByType<CourseDressing>(); if (dress != null) dress.Prewarm(); }
        float maxAir = 0f, peak = 0f, hardest = 0f, fastest = 0f, off = 0f;
        System.Action<float> landed = imp => hardest = Mathf.Max(hardest, imp);
        r.Landed += landed;
        int n = 0;
        try
        {
            for (float t = 0f; t < 40f && r.Z < zEnd; t += Dt, n++)
            {
                r.Steer = CarveBot.Onto(r, zz => Slope.Centre(zz) + line(zz), 1.2f, 10f, 8f);   // (carving for the line 8 m ahead)
                if (r.Speed > v0 * 1.25f + 4f) r.Body.linearVelocity *= 0.995f;   // (a rider checks their speed)
                r.TestContacts(Dt);
                r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (frame != null) { body.Apply(Dt); frame(n); }
                else if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
                if (r.Airborne) { maxAir = Mathf.Max(maxAir, r.AirTime); peak = Mathf.Max(peak, r.Peak); }
                fastest = Mathf.Max(fastest, r.Speed);
                off = Mathf.Max(off, Mathf.Abs(r.X - Slope.Centre(r.Z)));
                if (r.Crashed) break;
            }
        }
        finally { r.Landed -= landed; }
        crashed = r.Crashed;
        Vector3 p = r.transform.position;
        string res = $"air {maxAir:0.00} s (highest {peak:0.0} m), hardest landing {hardest:0.0} m/s, fastest {fastest * 3.6f:0} km/h, widest {off:0} m off the middle, "
                   + (r.Crashed ? $"CRASHED '{lastCrash}' ({(r.HitCollider != null ? r.HitCollider.transform.parent?.name + "/" + r.HitCollider.name : "-")}) at z={p.z:0} x-c={p.x - Slope.Centre(p.z):0.0}"
                                : r.Z < zEnd ? $"STOPPED at z={p.z:0} ({r.Speed * 3.6f:0} km/h)" : $"rode out at {r.Speed * 3.6f:0} km/h");
        if (!r.Crashed && r.Z < zEnd) crashed = true;
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
        return res;
    }

    // ---------------------------------------------------------------- pictures (needs the GPU)

    public static void Shots()
    {
        Setup();
        string outDir = System.Environment.GetEnvironmentVariable("BOWL_OUT") ?? "bowl-shots";
        System.IO.Directory.CreateDirectory(outDir);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        int taken = 0;
        // ONE mountain a run (BOWL_SEEDS=one seed): in a batch run the editor never ends a frame, so what is drawn
        // instanced (the forest's rocks and trees) piles up from shot to shot, and a second mountain's pictures showed the
        // first one's rocks hanging in its sky at the first one's heights
        foreach (int seed in Seeds)
        {
            if (taken >= 1) break;
            NewMountain(seed);
            if (!Bowl.Of(0, out var s)) continue;
            taken++;
            string tag = $"{outDir}/Bowl_s{seed}";
            float crest = s.zR + 20f;
            // the rider's eye on the shelf, 30 m above the crest: the snow drops out of sight
            Vector3 eye = P(0f, crest - 30f, 1.6f), ahead = P(0f, crest + 60f, 0f);
            Shot(cam, eye, ahead, tag + "_eye.png");
            // on the crest: the bowl opening below
            Shot(cam, P(0f, crest + 6f, 1.8f), P(0f, s.zO, 0f), tag + "_crest.png");
            // from the floor, looking up at the crest and the walls
            float zm = 0.5f * (s.zF + s.zO);
            Shot(cam, P(0f, s.zO - 10f, 2f), P(0f, s.zR, 0f), tag + "_up.png");
            // across the floor to a wall
            Shot(cam, P(-60f, zm, 2f), P(Bowl.Reach, zm - 10f, 10f), tag + "_wall.png");
            // the funnel, from just above it
            Shot(cam, P(0f, s.zO - 20f, 3f), P(0f, s.zE, 0f), tag + "_funnel.png");
            // the whole bowl, from high off to the side and below
            Shot(cam, P(-260f, s.zE + 120f, 160f), P(0f, zm, 0f), tag + "_aerial.png", false);
            Strip(cam, s, tag + "_ride.png");
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static Vector3 P(float a, float z, float up) { float x = Slope.Centre(z) + a; return new Vector3(x, Slope.Height(x, z) + up, z); }

    static void Shot(Camera cam, Vector3 from, Vector3 at, string file, bool streamAtCamera = true)
    {
        // (the terrain streams round the rider: round the camera for a view from the snow, round what is looked at for
        // the view from the air; round the far point, a view from the shelf had no snow under it)
        Vector3 c = streamAtCamera ? from : at;
        if (streamAtCamera) { var back = (from - at); back.y = 0f; c = from + back.normalized * 8f; }   // (the rider stands just behind the camera, out of the picture)
        r.ResetAt(c.x, c.z); terrain.Prewarm(); props.Prewarm();
        typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
        cam.fieldOfView = 60f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        // (particles do not move on in a test's still pictures: the snow thrown up when the rider is set down would hang
        // in the air where it was)
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.transform.parent != null && ps.transform.parent.name == "SnowFX") ps.Clear(true);
        const int W = 1100, H = 760;
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("BOWL-SHOT " + file);
    }

    static void Strip(Camera cam, Bowl.Shape s, string file)
    {
        const int W = 360, H = 300, Frames = 8;
        var strip = new Texture2D(W * Frames, H, TextureFormat.RGB24, false);
        var rt = new RenderTexture(W, H, 24);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        int got = 0, every = Mathf.RoundToInt(0.5f / Dt);
        Ride(s, z => 0f, 22f, out _, n =>
        {
            if (got >= Frames || n % every != 0 || r.Z < s.zR - 20f) return;
            Vector3 p = r.transform.position;
            Vector3 fwd = r.Body.linearVelocity.normalized;
            Vector3 from = p - fwd * 9f + Vector3.up * 3.5f;
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(p + fwd * 20f - from));
            typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
            cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            strip.SetPixels(got * W, 0, W, H, tex.GetPixels());
            got++;
        });
        cam.targetTexture = null; RenderTexture.active = null;
        strip.Apply();
        System.IO.File.WriteAllBytes(file, strip.EncodeToPNG());
        Debug.Log($"BOWL-SHOT {file} ({got} frames)");
    }
}
