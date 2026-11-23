using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The blind roll-overs (Slope.DropOf: blind) and the rock steps under some of them, on six mountains.
///  - Blind: from a rider's eye (1.6 m up on the piste's centre line) 30 m above the lip, how many metres of snow
///    past the lip are out of sight; the same for the plain roll-overs, to compare. The landing's steepest pitch.
///  - Markers: crossed poles at both edges of the piste at each blind lip.
///  - Steps: the drop outside the lane and inside it (none), nothing solid in the lane, rock either side of it.
///  - Rides: the real rider over each blind roll at 15, 25 and 35 m/s held to the step's lane (or the centre line):
///    no crash. And at 25 m/s down the centre line through a step (outside its lane): reported, not judged.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod BlindRollTest.Run -logFile blind.log
/// Shots (with the GPU): -executeMethod BlindRollTest.Shots (BLIND_OUT=dir). Log lines: BLIND, BLIND-FAIL, BLIND done.
/// </summary>
public static class BlindRollTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("BLIND_SEEDS") ?? "1000,1077,1154,1231,1308,2131").Split(','), int.Parse);
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static string lastCrash = "";
    static int fails, blinds, steps, plains, rides;
    static float hiddenBlind, hiddenPlain;

    static void Fail(string s) { fails++; Debug.Log("BLIND-FAIL " + s); }

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

    static float Deg(float rise, float run) => Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

    /// <summary>How many metres of the centre line past z0 are out of sight from 1.6 m up, 30 m above it.</summary>
    static float Hidden(float z0, float x0)
    {
        float ze = z0 - 30f;
        Vector3 eye = new Vector3(Slope.Centre(ze) + x0, 0f, ze); eye.y = Slope.Height(eye.x, eye.z) + 1.6f;
        float hidden = 0f;
        for (float z = z0 + 1f; z < z0 + 50f; z += 1f)
        {
            Vector3 q = new Vector3(Slope.Centre(z) + x0, 0f, z); q.y = Slope.Height(q.x, q.z) + 0.1f;
            bool seen = true;
            for (int i = 1; i < 60 && seen; i++)
            {
                Vector3 p = Vector3.Lerp(eye, q, i / 60f);
                if (Slope.Height(p.x, p.z) > p.y + 0.02f) seen = false;
            }
            if (seen) break;
            hidden += 1f;
        }
        return hidden;
    }

    public static void Run()
    {
        Setup();
        var all = new List<Slope.Drop>();
        try
        {
            foreach (int seed in Seeds)
            {
                NewMountain(seed);
                int b0 = blinds;
                for (int s = 1; s * Slope.DropSeg < Slope.FinishZ - 300f; s++)
                {
                    if (!Slope.DropOf(s, out var d)) continue;
                    if (!d.blind) { plains++; hiddenPlain += Hidden(d.LipZ, 0f); continue; }
                    blinds++;
                    Blind(seed, d);
                }
                Debug.Log($"BLIND seed{seed}: {blinds - b0} blind rolls");
            }
        }
        catch (System.Exception e) { Fail("exception " + e); }
        Debug.Log($"BLIND done: {blinds} blind rolls ({steps} with a rock step), {plains} plain; out of sight past the lip from 30 m above: blind {hiddenBlind / Mathf.Max(1, blinds):0.0} m on average, plain {hiddenPlain / Mathf.Max(1, plains):0.0} m; {rides} rides, {fails} failure(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static void Blind(int seed, Slope.Drop d)
    {
        float lip = d.LipZ, cx = Slope.Centre(lip);
        float hid = Hidden(lip, 0f);
        hiddenBlind += hid;
        float land = 0f;
        for (float z = lip; z < d.FootZ + 5f; z += 1f) land = Mathf.Max(land, Deg(Slope.Height(Slope.Centre(z), z) - Slope.Height(Slope.Centre(z + 1f), z + 1f), 1f));
        // the crossed poles
        int poles = 0;
        r.ResetAt(cx, lip); terrain.Prewarm(); props.Prewarm();
        var dress = Object.FindFirstObjectByType<CourseDressing>(); if (dress != null) dress.Prewarm();   // (the cycle's dressing, built round the rider)
        foreach (var it in PisteKit.Items)
            if (it.kind == PisteKit.Kind.Closed && it.foot.z > lip - 14f && it.foot.z < lip - 2.5f && Mathf.Abs(Mathf.Abs(it.foot.x - Slope.Centre(it.foot.z)) - Slope.HalfAt(it.foot.z)) < 4.5f && it.foot.z > lip - 14f) poles++;
        string step = "";
        if (d.step)
        {
            steps++;
            float a = d.lane + Mathf.Sign(-d.lane + 0.001f) * (Slope.StepLane + 5f);   // outside the lane, toward the middle
            float lz = Slope.StepLipZ(d, a), face = Mathf.Max(2f, 1.1f * d.stepH);
            float dropOut = Slope.Height(Slope.Centre(d.stepZ) + a, lz - 0.3f) - Slope.Height(Slope.Centre(d.stepZ) + a, lz + face + 0.3f) - Slope.Fall * (face + 0.6f);
            float laneMax = 0f;
            for (float z = d.stepZ - 12f; z < d.stepZ + 15f; z += 1f)
            {
                float x = Slope.Centre(d.stepZ) + d.lane;
                laneMax = Mathf.Max(laneMax, Deg(Slope.Height(x, z) - Slope.Height(x, z + 1f), 1f));
            }
            // nothing solid in the lane
            int solid = 0;
            r.ResetAt(Slope.Centre(d.stepZ) + d.lane, d.stepZ); terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
            for (float z = d.stepZ - 12f; z < d.stepZ + 20f; z += 1.5f)
                for (float b = -(Slope.StepLane - 1f); b <= Slope.StepLane - 1f; b += 1f)
                {
                    float x = Slope.Centre(d.stepZ) + d.lane + b, y = Slope.Height(x, z);
                    foreach (var col in Physics.OverlapCapsule(new Vector3(x, y + 0.4f, z), new Vector3(x, y + 1.6f, z), 0.4f, ~0, QueryTriggerInteraction.Ignore))
                        if (col.attachedRigidbody != r.Body && col.GetComponentInParent<Obstacle>() != null) solid++;
                }
            step = $"; a rock step {d.stepH:0.0} m ({dropOut:0.0} m more than the slope, outside the lane) {d.stepZ - d.FootZ:0} m below the roll, lane at {d.lane:+0;-0} m: steepest {laneMax:0} deg, {solid} solid hit(s) in it";
            if (dropOut < 1.2f) Fail($"seed{seed} drop{d.s}: the step outside its lane is only {dropOut:0.0} m");
            if (laneMax > 50f) Fail($"seed{seed} drop{d.s}: the lane is a step too ({laneMax:0} deg)");
            if (solid > 0) Fail($"seed{seed} drop{d.s}: {solid} solid hit(s) in the lane");
        }
        Debug.Log($"BLIND seed{seed} drop{d.s} z={lip:0} ({Course.At(lip)}): knoll {d.knoll:0.0} m, roll {d.H:0.0} m over {2f * d.w * Slope.DropSeg:0} m, {hid:0} m out of sight past the lip, landing up to {land:0} deg, {poles} crossed pairs{step}");
        if (poles < 2)
        {
            // what stands at the edges there (PisteKit keeps its poles off it)
            string why = ""; bool hamlet = false;
            for (int e = -1; e <= 1; e += 2)
            {
                { float hz = lip - 4f, hx = Slope.Centre(hz) + e * (Slope.HalfAt(hz) + 0.8f); hamlet |= Hamlets.Taken(hx, hz); }
                float z = lip - 4f, x = Slope.Centre(z) + e * (Slope.HalfAt(z) + 0.8f);
                why += $" {(e < 0 ? "left" : "right")}: lift {LiftLine.Taken(x, z, 1.5f)} hamlet {Hamlets.Taken(x, z)} light {NightLights.Taken(x, z)} sign {TrailSigns.Near(x, z, 2.5f)} tree {PisteKit.TreeNear(x, z, 0.8f)} crevasse {Course.CrevasseNear(x, z, 1.5f)} pad {Slope.OnPad(x, z)};";
            }
            if (poles == 1 && hamlet) Debug.Log($"BLIND seed{seed} drop{d.s}: one crossed pair; a hamlet's fence on the other edge;{why}");
            else Fail($"seed{seed} drop{d.s}: {poles} crossed pairs at the lip (want one each side);{why}");
        }
        if (hid < 5f) Fail($"seed{seed} drop{d.s}: only {hid:0} m out of sight past the lip");
        // rides: held to the lane (or the centre line)
        float line = d.step ? d.lane : 0f;
        foreach (float v0 in new[] { 15f, 25f, 35f })
        {
            string res = Ride(lip, line, v0, d.step ? d.stepZ + 25f : d.FootZ + 15f, out bool crashed);
            // (the piste has the odd tree island and rock of the forest's on it, by design; held to one line the bot can
            // ride into one: that is the forest, not this roll)
            bool forest = crashed && r.HitCollider != null && r.HitCollider.transform.parent != null && r.HitCollider.transform.parent.name == "Trees";
            Debug.Log($"BLIND seed{seed} drop{d.s} ride {v0:0} m/s {(d.step ? "in the lane" : "centre")}: {res}{(forest ? " (a forest island on the line: not judged)" : "")}");
            if (crashed && !forest) Fail($"seed{seed} drop{d.s} ride {v0:0} m/s: {res}");
        }
        if (d.step && Mathf.Abs(d.lane) > Slope.StepLane + 4f)
            Debug.Log($"BLIND seed{seed} drop{d.s} ride 25 m/s centre, through the step: {Ride(lip, 0f, 25f, d.stepZ + 25f, out _)}");
    }

    static string Ride(float lip, float line, float v0, float zEnd, out bool crashed, System.Action<int> frame = null)
    {
        rides++;
        float z0 = lip - 45f, x0 = Slope.Centre(z0) + line;
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * v0;
        lastCrash = ""; terrain.Prewarm(); props.Prewarm();
        { var dress = Object.FindFirstObjectByType<CourseDressing>(); if (dress != null) dress.Prewarm(); }
        float maxAir = 0f, peak = 0f, hardest = 0f;
        System.Action<float> landed = imp => hardest = Mathf.Max(hardest, imp);
        r.Landed += landed;
        int n = 0;
        try
        {
            for (float t = 0f; t < 10f && r.Z < zEnd; t += Dt, n++)
            {
                r.Steer = Mathf.Clamp((Slope.Centre(r.Z + 6f) + line - r.X) * 0.4f, -1f, 1f);
                if (r.Speed > v0 * 1.2f) r.Body.linearVelocity *= 0.995f;
                r.TestContacts(Dt);
                r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (frame != null) { body.Apply(Dt); frame(n); }
                else if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
                if (r.Airborne) { maxAir = Mathf.Max(maxAir, r.AirTime); peak = Mathf.Max(peak, r.Peak); }
                if (r.Crashed) break;
            }
        }
        finally { r.Landed -= landed; }
        crashed = r.Crashed;
        Vector3 p = r.transform.position;
        string res = $"air {maxAir:0.00} s (highest {peak:0.0} m), hardest landing {hardest:0.0} m/s, " + (r.Crashed ? $"CRASHED '{lastCrash}' ({(r.HitCollider != null ? r.HitCollider.transform.parent?.name + "/" + r.HitCollider.name : "-")}) at z={p.z:0} ({p.z - lip:+0;-0} m past the lip) x-c={p.x - Slope.Centre(p.z):0.0}" : $"rode on at {r.Speed * 3.6f:0} km/h");
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
        return res;
    }

    // ---------------------------------------------------------------- pictures (needs the GPU)

    public static void Shots()
    {
        Setup();
        string outDir = System.Environment.GetEnvironmentVariable("BLIND_OUT") ?? "blind-shots";
        System.IO.Directory.CreateDirectory(outDir);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        int taken = 0, withStep = 0;
        foreach (int seed in new[] { 1000, 1154, 2131 })
        {
            NewMountain(seed);
            for (int s = 1; s * Slope.DropSeg < Slope.FinishZ - 300f && taken < 6; s++)
            {
                if (!Slope.DropOf(s, out var d) || !d.blind) continue;
                if (d.step) withStep++; else if (taken - withStep >= 3) continue;
                taken++;
                float lip = d.LipZ;
                string tag = $"{outDir}/Blind_s{seed}_d{s}{(d.step ? "_step" : "")}";
                // what a rider sees from 25 m above the lip, eye height
                Vector3 eye = new Vector3(Slope.Centre(lip - 25f), 0f, lip - 25f); eye.y = Slope.Height(eye.x, eye.z) + 1.6f;
                Vector3 ahead = new Vector3(Slope.Centre(lip + 25f), 0f, lip + 25f); ahead.y = Slope.Height(ahead.x, ahead.z);
                Shot(cam, eye, ahead, tag + "_eye.png");
                // from the side, the knoll and the drop below it
                Vector3 mid = new Vector3(Slope.Centre(lip + 5f), Slope.Height(Slope.Centre(lip + 5f), lip + 5f), lip + 5f);
                Vector3 side = mid + new Vector3(-(Slope.PisteHalf + 18f), 6f, -4f);
                Shot(cam, side, mid, tag + "_side.png");
                if (d.step)
                {
                    // the step from below, the lane in the middle of the view
                    float zs = d.stepZ + 22f;
                    Vector3 below = new Vector3(Slope.Centre(zs) + d.lane, 0f, zs); below.y = Slope.Height(below.x, below.z) + 3f;
                    Vector3 lane = new Vector3(Slope.Centre(d.stepZ) + d.lane, Slope.Height(Slope.Centre(d.stepZ) + d.lane, d.stepZ), d.stepZ);
                    Shot(cam, below, lane, tag + "_below.png");
                    Strip(cam, d, tag + "_ride.png");
                }
            }
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static void Shot(Camera cam, Vector3 from, Vector3 at, string file)
    {
        r.ResetAt(at.x, at.z); terrain.Prewarm(); props.Prewarm();
        typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
        cam.fieldOfView = 55f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        const int W = 1100, H = 760;
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("BLIND-SHOT " + file);
    }

    static void Strip(Camera cam, Slope.Drop d, string file)
    {
        const int W = 360, H = 300, Frames = 8;
        var strip = new Texture2D(W * Frames, H, TextureFormat.RGB24, false);
        var rt = new RenderTexture(W, H, 24);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        int got = 0, every = Mathf.RoundToInt(0.22f / Dt);
        Ride(d.LipZ, d.lane, 25f, d.stepZ + 25f, out _, n =>
        {
            if (got >= Frames || n % every != 0 || r.Z < d.LipZ - 15f) return;
            Vector3 p = r.transform.position;
            Vector3 from = p + new Vector3(-(Mathf.Sign(d.lane + 0.01f)) * 14f, 5f, -7f);
            cam.fieldOfView = 50f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(p + Vector3.up * 0.8f - from));
            typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
            cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            strip.SetPixels(got * W, 0, W, H, tex.GetPixels());
            got++;
        });
        cam.targetTexture = null; RenderTexture.active = null;
        strip.Apply();
        System.IO.File.WriteAllBytes(file, strip.EncodeToPNG());
        Debug.Log($"BLIND-SHOT {file} ({got} frames)");
    }
}
