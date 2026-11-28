using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The ledge lines beside the run (Cliffs.LedgeOf): on six mountains, every line's shape, its clearance, and rides down it.
///  - Shape, along the strip's middle: each ledge's drop (lip to the foot of its face), how level its lip is, how steep its
///    landing (it must be steep enough never to slam you), and the strip's own tilt across.
///  - Clearance: nothing solid in the strip (trees, rocks, lift towers, buildings), the sign and wands in place.
///  - Rides: the real rider down the strip from 25 m above its first ledge at 15, 21 and 28 m/s, held to its middle:
///    every ledge flown off (an air), no crash.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod LedgeTest.Run -logFile ledge.log
/// Shots (with the GPU): -executeMethod LedgeTest.Shots (LEDGE_OUT=dir): each line from the piste and from below, and a
/// frame strip of a ride. Log lines: LEDGE (one per ledge and ride), LEDGE-FAIL, LEDGE done.
/// </summary>
public static class LedgeTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("LEDGE_SEEDS") ?? "1000,1077,1154,1231,1308,2131").Split(','), int.Parse);
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static string lastCrash = "";
    static int fails, lines, ledges, rides;

    static void Fail(string s) { fails++; Debug.Log("LEDGE-FAIL " + s); }

    static void Setup()
    {
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        r = game.rider;
        body = game.body;
        terrain = game.terrain;
        props = game.props;
        r.input = null;
        r.Crash += why => lastCrash = why;
    }

    static string PathOf(Transform t) { string s = t.name; for (int i = 0; i < 2 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; } return s; }
    /// <summary>The mountain, and the course's dressing for it (its crags, signs and wands: NewMountain leaves the old ones).</summary>
    static void NewMountain(int seed)
    {
        TestBoot.NewMountain(game, seed, terrain, props);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) { var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt; dress.Prewarm(); }
    }

    static float LineX(in Cliffs.LedgeLine L, float z) => Slope.Centre(z) + L.side * L.off;
    static float Deg(float rise, float run) => Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

    public static void Run()
    {
        Setup();
        try
        {
            foreach (int seed in Seeds)
            {
                NewMountain(seed);
                int cycles = Mathf.CeilToInt(Slope.FinishZ / Course.Cycle) + 1;
                for (int c = 0; c < cycles; c++)
                {
                    if (!Cliffs.LedgeOf(c, out var L)) { Debug.Log($"LEDGE seed{seed} cycle{c}: no ledge line"); continue; }
                    lines++;
                    Shape(seed, c, L);
                    Clearance(seed, c, L);
                    foreach (float v0 in new[] { 15f, 21f, 28f }) Ride(seed, c, L, v0);
                }
            }
        }
        catch (System.Exception e) { Fail("exception " + e); }
        Debug.Log($"LEDGE done: {lines} ledge lines, {ledges} ledges, {rides} rides, {fails} failure(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    static void Shape(int seed, int c, Cliffs.LedgeLine L)
    {
        Debug.Log($"LEDGE seed{seed} cycle{c}: {L.count} ledges on the {(L.side < 0f ? "left" : "right")}, {L.off - L.half - Slope.PisteHalf:0} to {L.off + L.half - Slope.PisteHalf:0} m off the piste's edge, z {L.Start:0}-{L.End:0} ({L.End - L.Start:0} m), sec {Course.At(L.Start)}..{Course.At(L.End)}");
        {
            bool sign = false;
            foreach (var sg in TrailSigns.Signs) if (sg.symbol == TrailSigns.Symbol.Black && Mathf.Abs(sg.z - (L.Start - 22f)) < 12f) sign = true;
            Debug.Log($"LEDGE seed{seed} cycle{c}: black diamond at the way in: {sign}; height zone Low {Forest.Low(L.Start):0.00}..{Forest.Low(L.End):0.00} ({(Forest.Low(L.End) < Forest.TreeLine ? "above the tree line" : Forest.Low(L.Start) < Forest.TreeLine ? "across the tree line" : "below the tree line")})");
            if (!sign) Fail($"seed{seed} cycle{c}: no black diamond at the ledges' way in");
        }
        for (int i = 0; i < L.count; i++)
        {
            ledges++;
            var e = L[i];
            float lip = Cliffs.LedgeLipZ(L, i, 0f);
            float x0 = LineX(L, lip - 0.3f), x1 = LineX(L, lip + e.face + 0.3f);
            float drop = Slope.Height(x0, lip - 0.3f) - Slope.Height(x1, lip + e.face + 0.3f);
            float lipPitch = Deg(Slope.Height(LineX(L, lip - 4f), lip - 4f) - Slope.Height(x0, lip - 0.3f), 3.7f);
            float land = 0f;
            for (float u = e.face + 1f; u < e.face + Cliffs.LedgeLanding * 0.6f; u += 1f)
            {
                float za = lip + u, zb = za + 1f;
                land = Mathf.Max(land, Deg(Slope.Height(LineX(L, za), za) - Slope.Height(LineX(L, zb), zb), 1f));
            }
            float zm = lip + e.face + 6f;
            float tilt = Deg(Mathf.Abs(Slope.Height(LineX(L, zm) - L.half * 0.7f, zm) - Slope.Height(LineX(L, zm) + L.half * 0.7f, zm)), L.half * 1.4f);
            Debug.Log($"LEDGE seed{seed} cycle{c} ledge{i}: drop {drop:0.0} m (planned {e.drop:0.0}), lip {lipPitch:0} deg down, landing up to {land:0} deg, across the strip {tilt:0} deg");
            if (drop < 2.5f || drop > 11.5f) Fail($"seed{seed} cycle{c} ledge{i}: a drop of {drop:0.0} m");
            if (lipPitch > 20f) Fail($"seed{seed} cycle{c} ledge{i}: its lip falls away at {lipPitch:0} deg, not a lip");
            if (land < 34f) Fail($"seed{seed} cycle{c} ledge{i}: its landing is only {land:0} deg (a flat landing slams)");
            if (tilt > 22f) Fail($"seed{seed} cycle{c} ledge{i}: the strip tilts {tilt:0} deg across its landing");
        }
    }

    static void Clearance(int seed, int c, Cliffs.LedgeLine L)
    {
        int solid = 0; string what = "";
        for (float z = L.Start - 10f; z < L.End + 10f; z += 12f)
        {
            float x = LineX(L, z);
            r.ResetAt(x, z); terrain.Prewarm(); props.Prewarm();
            typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
            Physics.SyncTransforms();
            // the ridden middle (its eased sides apart): anything solid there from the snow to head height
            var seen = new HashSet<Collider>();
            for (float zz = z - 6f; zz < z + 6f; zz += 1.5f)
                for (float b = -(L.half - 2f); b <= L.half - 2f; b += 1.2f)
                {
                    float xx = Slope.Centre(zz) + L.side * (L.off + b), yy = Slope.Height(xx, zz);
                    foreach (var col in Physics.OverlapCapsule(new Vector3(xx, yy + 0.6f, zz), new Vector3(xx, yy + 1.8f, zz), 0.6f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (col.attachedRigidbody == r.Body || col.GetComponentInParent<Obstacle>() == null || !seen.Add(col)) continue;
                        float ca = (col.bounds.center.x - Slope.Centre(col.bounds.center.z)) * L.side - L.off;
                        solid++; if (solid <= 4) what += $"{col.name}({PathOf(col.transform)})@z{zz:0} b{b:+0.0;-0.0} its middle at {ca:+0.0;-0.0} size{col.bounds.size} ";
                    }
                }
        }
        Debug.Log($"LEDGE seed{seed} cycle{c}: {solid} solid thing(s) in the strip {what}");
        if (solid > 0) Fail($"seed{seed} cycle{c}: {solid} solid thing(s) in the strip: {what}");
    }

    static void Ride(int seed, int c, Cliffs.LedgeLine L, float v0, System.Action<int> frame = null)
    {
        rides++;
        float z0 = L.Start - 25f;
        float x0 = LineX(L, z0);
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * v0;
        lastCrash = ""; terrain.Prewarm(); props.Prewarm();
        int airs = 0; float maxAir = 0f, maxPeak = 0f, hardest = 0f; bool wasAir = false;
        System.Action<float> landed = imp => { hardest = Mathf.Max(hardest, imp); };
        r.Landed += landed;
        int n = 0;
        try
        {
            for (float t = 0f; t < 20f && r.Z < L.End + 15f; t += Dt, n++)
            {
                var ledge = L;
                r.Steer = CarveBot.Onto(r, zz => LineX(ledge, zz), 1.5f, 10f, 8f);   // (carving for the ledge's line 8 m ahead)
                r.TestContacts(Dt);
                TestHarness.Step(r, body, Dt);
                if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (frame != null) { body.Apply(Dt); frame(n); }
                else if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
                if (r.Airborne) { maxAir = Mathf.Max(maxAir, r.AirTime); maxPeak = Mathf.Max(maxPeak, r.Peak); }
                if (wasAir && !r.Airborne && r.AirTime > 0.25f) airs++;
                if (!wasAir && r.Airborne) { }
                wasAir = r.Airborne;
                if (r.Crashed) break;
            }
        }
        finally { r.Landed -= landed; }
        Vector3 p = r.transform.position;
        string where = $"ended at z={p.z:0} ({p.z - L.End:+0;-0} m past its end) off its middle {(p.x - LineX(L, p.z)) * L.side:+0.0;-0.0} m, n.y {Slope.Normal(p.x, p.z).y:0.00}, sec {Course.At(p.z)}";
        if (!r.Crashed && r.Speed < 3f) Fail($"seed{seed} cycle{c} ride {v0:0} m/s: stopped, {where}");
        string end = r.Crashed ? $"CRASHED '{lastCrash}' at z={p.z:0} ({p.z - L.Start:0} m into the line) off its middle {(p.x - LineX(L, p.z)) * L.side:+0.0;-0.0} m" : $"rode it out at {r.Speed * 3.6f:0} km/h ({where})";
        Debug.Log($"LEDGE seed{seed} cycle{c} ride {v0:0} m/s: {airs} airs (longest {maxAir:0.00} s, highest {maxPeak:0.0} m), hardest landing {hardest:0.0} m/s; {end}");
        if (frame == null)
        {
            if (r.Crashed) Fail($"seed{seed} cycle{c} ride {v0:0} m/s: crashed '{lastCrash}'");
            if (airs < 1) Fail($"seed{seed} cycle{c} ride {v0:0} m/s: no air off a ledge");
        }
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
    }

    // ---------------------------------------------------------------- pictures (needs the GPU)

    public static void Shots()
    {
        Setup();
        string outDir = System.Environment.GetEnvironmentVariable("LEDGE_OUT") ?? "ledge-shots";
        System.IO.Directory.CreateDirectory(outDir);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        int taken = 0;
        // (one mountain a run gives clean pictures: in a batch run the editor never ends a frame, so the forest's
        // instanced rocks and trees pile up from shot to shot, and a second mountain's pictures show the first one's
        // hanging at its heights. LEDGE_SHOT_SEEDS=one seed)
        var shotSeeds = System.Environment.GetEnvironmentVariable("LEDGE_SHOT_SEEDS");
        foreach (int seed in shotSeeds != null ? System.Array.ConvertAll(shotSeeds.Split(','), int.Parse) : new[] { 1000, 1154 })
        {
            NewMountain(seed);
            for (int c = 0; c < 6 && taken < 6; c++)
            {
                if (!Cliffs.LedgeOf(c, out var L)) continue;
                taken++;
                float zm = (L.Start + L.End) * 0.5f;
                Vector3 mid = new Vector3(LineX(L, zm), Slope.Height(LineX(L, zm), zm), zm);
                // from the piste's far edge, looking across and down the staircase
                float px = Slope.Centre(L.Start - 10f) - L.side * (Slope.PisteHalf - 4f);
                Vector3 from = new Vector3(px, 0f, L.Start - 25f); from.y = Slope.Height(from.x, from.z) + 4f;
                Shot(cam, from, mid, $"{outDir}/Ledges_s{seed}_c{c}_piste.png");
                // from below the last ledge, looking back up the line
                float zb = L.End + 25f;
                Vector3 below = new Vector3(LineX(L, zb), 0f, zb); below.y = Slope.Height(below.x, below.z) + 5f;   // (in the cleared strip's middle)
                Shot(cam, below, new Vector3(LineX(L, L.Start + 20f), Slope.Height(LineX(L, L.Start + 20f), L.Start + 20f), L.Start + 20f), $"{outDir}/Ledges_s{seed}_c{c}_below.png");
                // the first ledge from beside, close
                var e0 = L[0];
                Vector3 lip = new Vector3(LineX(L, e0.z), Slope.Height(LineX(L, e0.z), e0.z), e0.z + e0.face * 0.5f);
                Vector3 side = lip + new Vector3(L.side * (L.half + 14f), 3f, 2f); side.y = Mathf.Max(side.y, Slope.Height(side.x, side.z) + 2f);
                Shot(cam, side, lip, $"{outDir}/Ledges_s{seed}_c{c}_side.png");
                // a ride down it, frames from a camera beside and above
                if (taken <= 2) Strip(cam, seed, c, L, $"{outDir}/Ledges_s{seed}_c{c}_ride.png");
            }
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Shot(Camera cam, Vector3 from, Vector3 at, string file)
    {
        r.ResetAt(at.x, at.z); terrain.Prewarm(); props.Prewarm();
        var dress = Object.FindFirstObjectByType<CourseDressing>(); if (dress != null) dress.Prewarm();
        typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
        cam.fieldOfView = 55f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
        Render(cam, file, 1100, 760);
    }

    static void Render(Camera cam, string file, int W, int H)
    {
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("LEDGE-SHOT " + file);
    }

    static void Strip(Camera cam, int seed, int c, Cliffs.LedgeLine L, string file)
    {
        const int W = 360, H = 300, Frames = 8;
        var strip = new Texture2D(W * Frames, H, TextureFormat.RGB24, false);
        var rt = new RenderTexture(W, H, 24);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        int got = 0, every = Mathf.RoundToInt(0.3f / Dt);
        Ride(seed, c, L, 21f, n =>
        {
            if (got >= Frames || n % every != 0 || r.Z < L.Start - 8f) return;
            Vector3 p = r.transform.position;
            Vector3 from = p + new Vector3(-L.side * 16f, 6f, -6f);   // (from the piste's side, over ground that is built)
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
        Debug.Log($"LEDGE-SHOT {file} ({got} frames)");
    }
}
