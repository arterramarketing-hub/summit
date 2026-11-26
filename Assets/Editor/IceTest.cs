using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The icy stretches (Surfaces.IceSections, SurfacesIce.cs) on a dozen mountains.
///  - Rate: how many SectionLen stretches got one against the sum of their chances (the intended rate: within three
///    standard deviations), how many of those found fair room (most of them), how many a mountain has and how much of
///    the run is icy (each in a band).
///  - Ice: an icy stretch is ice: most of the run's middle over its length.
///  - Fair: no ice anywhere on the run at a roll's lip, face or landing, past a blind roll's lip while it is out of
///    sight, a rock step, a jump cliff, a kicker, a deadly wall, a ledge line or a crevasse's lips (checked against those
///    features themselves, not against Surfaces.Fair); and 30 m of open snow above each stretch.
///  - Rides (ICE_RIDE_SEEDS, four of them): the real rider through every stretch down the centre line at 18 and 28 m/s and
///    carving across it at 15: no crash, still going, on the run, and the board on ice most of the way.
/// Run: -executeMethod IceTest.Run (ICE_SEEDS=...). Log lines: ICE, ICE-FAIL, ICE done.
/// Pictures (with the GPU): -executeMethod IceTest.Shots (ICE_OUT=dir, ICE_SHOT_SEED=one seed).
/// </summary>
public static class IceTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("ICE_SEEDS") ?? "1000,1077,1154,1231,1308,1385,1462,1539,1616,1693,2131,2208").Split(','), int.Parse);
    static readonly int[] RideSeeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("ICE_RIDE_SEEDS") ?? "1000,1154,1308,2131").Split(','), int.Parse);
    const float PerMountainMin = 1f, PerMountainMax = 5f, ShareMin = 0.01f, ShareMax = 0.1f, LaidShare = 0.65f, WithSome = 0.6f;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static string lastCrash = "";
    static int fails;

    static void Fail(string s) { fails++; Debug.Log("ICE-FAIL " + s); }

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
        // (a calm, cold, clear day: no fresh snow over the ice)
        Surfaces.Day = default;
    }

    static void NewMountain(int seed)
    {
        TestBoot.NewMountain(game, seed, terrain, props);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) { var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt; dress.Prewarm(); }
    }

    /// <summary>The most ice across the groomed run at z.</summary>
    static float IceAcross(float z)
    {
        float c = Slope.Centre(z), h = Slope.Half(z), most = 0f;
        for (float a = -h; a <= h; a += 1.5f) most = Mathf.Max(most, Surfaces.Surface(c + a, z).ice);
        return most;
    }

    /// <summary>The ice down the run's middle third at z, on average.</summary>
    static float IceMiddle(float z)
    {
        float c = Slope.Centre(z), h = Slope.Half(z) / 3f, sum = 0f; int n = 0;
        for (float a = -h; a <= h; a += 1f, n++) sum += Surfaces.Surface(c + a, z).ice;
        return sum / Mathf.Max(1, n);
    }

    public static void Run()
    {
        Setup();
        int hits = 0, laid = 0, stretches = 0, mountains = 0, rides = 0, noneOn = 0;
        float expect = 0f, vr = 0f, icyM = 0f, runM = 0f;
        try
        {
            foreach (int seed in Seeds)
            {
                bool ride = System.Array.IndexOf(RideSeeds, seed) >= 0;
                if (ride) NewMountain(seed); else { Slope.Init(seed); Course.ResetCaches(); }
                mountains++;
                var secs = Surfaces.IceSections;
                int h = 0;
                foreach (var c in Surfaces.IceChances) { expect += c.x; vr += c.x * (1f - c.x); if (c.y > 0f) h++; }
                hits += h; laid += secs.Length; stretches += Surfaces.IceChances.Count;
                if (secs.Length == 0) noneOn++;
                float run = Slope.FinishZ - Slope.StartZ, icy = 0f;
                foreach (var s in secs) icy += s.z1 - s.z0;
                icyM += icy; runM += run;
                var sb = new System.Text.StringBuilder();
                foreach (var s in secs) sb.Append($" z {s.z0:0}-{s.z1:0} ({Course.At(s.z0)}, chance {s.chance:0.00}: steep {s.steep:0.0} wind {s.wind:0.0} shade {s.shade:0.0})");
                Debug.Log($"ICE seed{seed}: {Surfaces.IceChances.Count} stretches, {h} got one, {secs.Length} laid, {icy:0} m icy of {run:0} m ({icy / run:P1});{sb}");

                // each stretch is ice: most of the run's middle over its length
                foreach (var s in secs)
                {
                    // (where it may lie: Fair cuts it out round a lip or a landing in it)
                    float sum = 0f; int n = 0, all = 0;
                    for (float z = s.z0; z <= s.z1; z += 2f, all++) if (Surfaces.IceRoom(z) > 0.99f) { sum += IceMiddle(z); n++; }
                    float mean = sum / Mathf.Max(1, n);
                    if (mean < 0.6f) Fail($"seed{seed} stretch z {s.z0:0}-{s.z1:0}: only {mean:0.00} ice down the middle");
                    if (n < Surfaces.MostOpen * all - 1) Fail($"seed{seed} stretch z {s.z0:0}-{s.z1:0}: ice may lie on only {n} of {all} rows of it");
                    // seen coming: open snow above it (no roll's lip, no blind crest), and its ice starting on open snow
                    for (float z = s.z0 - Surfaces.IceFade - Surfaces.SeenFrom; z < s.z0 + Surfaces.StartsOn; z += 2f)
                        if (Surfaces.IceRoom(z) < 0.99f) { Fail($"seed{seed} stretch z {s.z0:0}: not open snow {s.z0 - z:0} m above its start (z {z:0})"); break; }
                }
                Fair(seed);
                if (ride) foreach (var s in secs) rides += RideThrough(seed, s);
            }
        }
        catch (System.Exception e) { Fail("exception " + e); }
        float sd = Mathf.Sqrt(vr);
        float per = laid / (float)Mathf.Max(1, mountains), share = icyM / Mathf.Max(1f, runM);
        Debug.Log($"ICE rate: {hits} of {stretches} stretches got one against {expect:0.0} expected (sd {sd:0.0}); {laid} laid ({laid / (float)Mathf.Max(1, hits):P0} found fair room); {per:0.0} a mountain, {noneOn} mountain(s) with none; {share:P1} of the run icy");
        if (Mathf.Abs(hits - expect) > 3f * sd + 1f) Fail($"rate: {hits} stretches got one, {expect:0.0} expected");
        if (laid < LaidShare * hits) Fail($"rate: only {laid} of {hits} found fair room");
        if (per < PerMountainMin || per > PerMountainMax) Fail($"rate: {per:0.0} icy stretches a mountain (want {PerMountainMin}-{PerMountainMax})");
        if (share < ShareMin || share > ShareMax) Fail($"rate: {share:P1} of the run icy (want {ShareMin:P0}-{ShareMax:P0})");
        if (mountains - noneOn < WithSome * mountains) Fail($"rate: only {mountains - noneOn} of {mountains} mountains have an icy stretch");
        Debug.Log($"ICE done: {laid} icy stretches on {mountains} mountains, {rides} rides; {fails} failure(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>No ice at a lip, a landing or the like, by the features themselves.</summary>
    static void Fair(int seed)
    {
        int checkedRanges = 0;
        void None(string what, float z0, float z1)
        {
            checkedRanges++;
            for (float z = z0; z <= z1; z += 1f)
            {
                float ice = IceAcross(z);
                if (ice > 0.03f) { Fail($"seed{seed}: {ice:0.00} ice at z {z:0}, {what} ({z0:0}-{z1:0})"); return; }
            }
        }
        for (int s = 1; s * Slope.DropSeg < Slope.FinishZ; s++)
            if (Slope.DropOf(s, out var d))
            {
                None(d.blind ? "a blind roll" : "a roll's lip and landing", d.LipZ - 10f, Mathf.Max(d.FootZ + 15f, d.blind ? d.LipZ + 50f : 0f));
                if (d.step) None("a rock step", d.stepZ - 10f, d.stepZ + 25f);
            }
        for (int c = 0; Course.First + c * Course.Cycle < Slope.FinishZ; c++)
        {
            float zc = Course.First + c * Course.Cycle;
            for (float z = zc; z < zc + Course.Cycle; z += 20f)
                if (Cliffs.JumpNear(z, 0f, out var j)) { None("a jump cliff", j.Start - 5f, j.End + 5f); break; }
            if (Cliffs.WallOf(c, out var w)) None("the deadly wall", w.z0 - 10f, w.z2);
            for (float z = zc; z < zc + Course.Cycle; z += 20f)
                if (Cliffs.LedgeNear(z, 0f, out var L)) { None("a ledge line", L.Start - 5f, L.End + 5f); break; }
        }
        for (float z = 60f; z < Slope.FinishZ;)
        {
            if (!Slope.NextKicker(z, out var k)) break;
            None("a kicker", k.z - 20f, k.z + 30f);
            z = k.z + 40f;
        }
        foreach (var cv in Course.Crevasses) None("a crevasse", cv.z0 - 5f, cv.z1 + 5f);
        Debug.Log($"ICE seed{seed} fair: {checkedRanges} lips, landings and the like checked");
    }

    static int RideThrough(int seed, Surfaces.IceSection s)
    {
        int n = 0;
        foreach (var (v0, carve) in new[] { (18f, false), (28f, false), (15f, true) })
        {
            n++;
            string res = Ride(s, v0, carve, out bool bad, out float onIce);
            Debug.Log($"ICE seed{seed} stretch z {s.z0:0}-{s.z1:0} ride {v0:0} m/s {(carve ? "carving" : "centre")}: {res}, on ice {onIce:P0} of the stretch");
            if (bad) Fail($"seed{seed} stretch z {s.z0:0} ride {v0:0} m/s {(carve ? "carving" : "centre")}: {res}");
            if (onIce < 0.5f) Fail($"seed{seed} stretch z {s.z0:0} ride {v0:0} m/s: on ice only {onIce:P0} of the stretch");
        }
        return n;
    }

    static string Ride(Surfaces.IceSection s, float v0, bool carve, out bool bad, out float onIce, System.Action<int> frame = null)
    {
        float z0 = s.z0 - 40f, x0 = Slope.Centre(z0);
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * v0;
        lastCrash = ""; terrain.Prewarm(); props.Prewarm();
        { var dress = Object.FindFirstObjectByType<CourseDressing>(); if (dress != null) dress.Prewarm(); }
        float zEnd = s.z1 + 30f, wide = 0f, slowest = float.MaxValue;
        int iceN = 0, inN = 0, k = 0;
        System.Func<float, float> line = carve ? (zz => Slope.Centre(zz) + 0.45f * Slope.Half(zz) * Mathf.Sin((zz - z0) / 16f)) : (System.Func<float, float>)(zz => Slope.Centre(zz));
        for (float t = 0f; t < 14f && r.Z < zEnd; t += Dt, k++)
        {
            r.Steer = CarveBot.Onto(r, line, 1.5f, 10f, 6f);
            if (r.Speed > v0 * 1.25f) r.Body.linearVelocity *= 0.995f;
            r.TestContacts(Dt);
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (k % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (frame != null) { body.Apply(Dt); frame(k); }
            else if (k % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
            if (r.Crashed) break;
            Vector3 p = r.transform.position;
            if (p.z > s.z0 && p.z < s.z1)
            {
                inN++;
                if (r.Underfoot.ice > 0.5f) iceN++;
                wide = Mathf.Max(wide, Mathf.Abs(p.x - Slope.Centre(p.z)) - Slope.Half(p.z));
                slowest = Mathf.Min(slowest, r.Speed);
            }
        }
        onIce = iceN / (float)Mathf.Max(1, inN);
        // (the piste has the odd tree island and rock of the forest's on it: not the ice)
        bool forest = r.Crashed && r.HitCollider != null && r.HitCollider.transform.parent != null && r.HitCollider.transform.parent.name == "Trees";
        bad = (r.Crashed && !forest) || (!r.Crashed && (r.Z < zEnd - 1f || slowest < 4f || wide > 3f));
        Vector3 q = r.transform.position;
        string res = r.Crashed ? $"CRASHED '{lastCrash}' at z={q.z:0} x-c={q.x - Slope.Centre(q.z):0.0}{(forest ? " (a forest island: not judged)" : "")}"
                               : $"through at {r.Speed * 3.6f:0} km/h, slowest {slowest * 3.6f:0} km/h, {Mathf.Max(0f, wide):0.0} m past the run's edge at most{(r.Z < zEnd - 1f ? $", STOPPED at z {q.z:0}" : "")}";
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
        return res;
    }

    // ---------------------------------------------------------------- pictures (needs the GPU)

    /// <summary>
    /// An icy stretch on one mountain (ICE_SHOT_SEED, ICE_SHOT_PICK: which of its stretches), through the game's own chase
    /// camera at the phone's shape: riding up to it (it is seen coming), and carving across it (the shine, the streaks,
    /// the chips, the thin track). ICE_OUT=dir.
    /// </summary>
    public static void Shots()
    {
        Setup();
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        string outDir = System.Environment.GetEnvironmentVariable("ICE_OUT") ?? "ice-shots";
        System.IO.Directory.CreateDirectory(outDir);
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("ICE_SHOT_SEED"), out int ss) ? ss : 1000;
        TestBoot.NewMountain(game, seed, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        var chase = Object.FindFirstObjectByType<ChaseCamera>();
        var camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        chase.enabled = false;
        var secs = Surfaces.IceSections;
        if (secs.Length == 0) { Debug.Log($"ICE-SHOT seed{seed}: no icy stretch"); EditorApplication.Exit(1); return; }
        var s = secs[Mathf.Clamp(int.TryParse(System.Environment.GetEnvironmentVariable("ICE_SHOT_PICK"), out int pk) ? pk : 0, 0, secs.Length - 1)];
        var cam = Camera.main;
        const int W = 590, H = 1280;
        var rt = new RenderTexture(W, H, 24);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        string tag = $"{outDir}/Ice_s{seed}_z{s.z0:0}";
        // in from 45 m above, down the middle, then carving across it
        float z0 = s.z0 - 45f, v0 = 16f;
        r.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); r.Begin();
        r.Heading = 0f; r.Steer = 0f;
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z0), z0)).normalized * v0;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        chase.rider = r; chase.Snap();
        bool approach = false, onIce = false, edge = false;
        for (int n = 0; n < 120 * 12 && r.Z < s.z1; n++)
        {
            bool carving = r.Z > s.z0 + 4f;
            r.Steer = r.Crashed ? 0f : carving ? 0.5f * Mathf.Sin((r.Z - s.z0) / 9f) + Mathf.Clamp((Slope.Centre(r.Z + 10f) - r.X) * 0.05f, -0.3f, 0.3f)
                                               : Mathf.Clamp((Slope.Centre(r.Z + 10f) - r.X) * 0.08f, -0.4f, 0.4f);
            if (r.Speed > v0 && !r.Airborne && !r.Crashed) r.Body.linearVelocity *= v0 / r.Speed;
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 2 == 0)
            {
                body.Apply(2 * Dt);
                ChaseCamera.TestDt = 2 * Dt;
                camStep.Invoke(chase, null);
                Surfaces.Step(r.Body.position, r.RunNow, 2 * Dt, true);
            }
            if (!approach && r.Z > s.z0 - IceTestSeen) { Grab(cam, rt, tex, tag + "_coming.png"); approach = true; }
            if (!edge && r.Z > s.z0 - 2f)
            {
                // the stretch's top edge, close and from above: where the ice comes in, in the hollows first
                float ze = s.z0 - Surfaces.IceFade * 0.5f;
                Vector3 at = new Vector3(Slope.Centre(ze) - 3f, 0f, ze); at.y = Slope.Height(at.x, at.z);
                Vector3 from = at + new Vector3(-4f, 0f, -7f); from.y = Slope.Height(from.x, from.z) + 6f;   // (6 m over the snow there: the slope is steep)
                at.z += 4f; at.y = Slope.Height(at.x, at.z);
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                Surfaces.Step(at, r.RunNow, 0f, true);
                Grab(cam, rt, tex, tag + "_edge.png"); edge = true;
                var sb = new System.Text.StringBuilder();
                for (float dz = -24f; dz <= 12f; dz += 4f) sb.Append($" {dz:+0;-0}:{Surfaces.Surface(Slope.Centre(s.z0 + dz) - 3f, s.z0 + dz).ice:0.00}");
                Debug.Log($"ICE-SHOT edge: ice 3 m left of the middle, metres from the stretch's start:{sb}; painted {Surfaces.Painted(at):P0}");
            }
            if (!onIce && r.Z > s.z0 + 26f && r.Underfoot.ice > 0.6f && Mathf.Abs(r.Lean) > 0.3f * Rider.MaxLean) { Grab(cam, rt, tex, tag + "_carve.png"); onIce = true; }
            if (r.Crashed) break;
        }
        Debug.Log($"ICE-SHOT seed{seed} stretch z {s.z0:0}-{s.z1:0}: {(approach ? "coming" : "no coming")}, {(onIce ? "carve" : "no carve")}; {(r.Crashed ? "CRASHED" : $"{r.Speed * 3.6f:0} km/h")}, {r.Underfoot.ice:P0} ice under the board at the end");
        cam.targetTexture = null; RenderTexture.active = null;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    const float IceTestSeen = 22f;   // (the approach picture: this far above the stretch's start)

    static void Grab(Camera cam, RenderTexture rt, Texture2D tex, string file)
    {
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.isPlaying) ps.Simulate(0.0001f, false, false, false);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        cam.targetTexture = rt; cam.aspect = (float)rt.width / rt.height; cam.Render();
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        Debug.Log("ICE-SHOT " + file);
    }
}
