using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Rides into things at several heights, angles and speeds and reports what each did: ridden over (and whether
/// popped over), glanced off, or a crash. The things are made here on open piste, sat on the snow's own slope: a
/// rock (a squat egg, its sides rising out of the snow), a stump (a flat-topped post) and a block (a slab with square sides).
/// The angle is between the way the board goes and straight into the face it meets (0 is dead on).
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod RideOverCheck.Run -logFile rideover.log
/// Strips (with the GPU): -executeMethod RideOverCheck.Strips (RIDEOVER_OUT=dir): frames from beside a few rides.
/// Log lines: RIDEOVER (one per ride), RIDEOVER-TABLE (the summary), RIDEOVER-FAIL (against the rule).
/// </summary>
public static class RideOverCheck
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static string lastCrash; static bool hopped, glanced, met; static float glanceInto;
    static int fails;
    static readonly string only = System.Environment.GetEnvironmentVariable("RIDEOVER_ONLY");

    static readonly string[] Shapes = { "rock", "stump", "block" };
    static readonly float[] Heights = { 0.2f, 0.4f, 0.55f, 0.75f, 0.9f, 1.5f };
    static readonly float[] Angles = { 0f, 20f, 35f, 55f };
    static readonly float[] Speeds = { 8f, 16f, 26f };

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
        r.Hopped += _ => hopped = true;
        r.Glanced += into => { glanced = true; glanceInto = Mathf.Max(glanceInto, into); };
        r.HitObstacle += (ob, at, into) => met = true;
        TestBoot.NewMountain(game, 4242, terrain, props);
    }

    public static void Run()
    {
        Setup();
        var table = new Dictionary<string, int>();
        try
        {
            if (only == null) Survey();
            var spots = OpenSpots(Course.First + 60f, 6);
            Debug.Log($"RIDEOVER places z={string.Join(", ", spots.ConvertAll(q => q.ToString("0")))} slope n.y={Slope.Normal(Slope.Centre(spots[0]), spots[0]).y:0.00} StepUp={Rider.StepUp} deadOn={Rider.DeadOnAngle} deg crashInto={Rider.CrashInto} m/s");
            int i = 0;
            foreach (string shape in Shapes)
                foreach (float h in Heights)
                    foreach (float ang in Angles)
                        foreach (float v0 in Speeds)
                        {
                            float z = spots[i++ % 6];
                            if (only != null && only != $"{shape},{h:0.00},{ang:0},{v0:0}") continue;
                            var res = Ride(shape, h, ang, v0, z, null);
                            string key = $"{shape} h{h:0.00} a{ang:0}";
                            Debug.Log($"RIDEOVER {shape} top {h:0.00} m, {ang:0} deg, {v0:0} m/s: {res.outcome}{(res.hop ? " (popped over)" : "")}; speed {res.vIn:0.0} -> {res.vOut:0.0} m/s, highest {res.peak:0.00} m over the snow{(res.into > 0f ? $", {res.into:0.0} m/s into it" : "")}{(res.angle >= 0f ? $", met {res.angle:0} deg off square" : "")}, {res.offLine:+0.00;-0.00} m off its line");
                            table.TryGetValue(key + " " + res.outcome, out int c); table[key + " " + res.outcome] = c + 1;
                            // the rule: low enough is ridden over; taller, only dead on (and fast enough) crashes
                            bool low = h <= Rider.StepUp - 0.03f;
                            bool deadOn = ang <= Rider.DeadOnAngle - 3f;
                            // (a round rock's face is met only roughly at the angle asked for: near the line, either may be right)
                            bool rough = shape == "rock" && ang > 0f && ang < Rider.DeadOnAngle + 10f;
                            string want = low ? "over" : (deadOn && !rough && v0 > 8.5f ? "crash" : null);
                            if (want != null && res.outcome != want) { fails++; Debug.Log($"RIDEOVER-FAIL {shape} top {h:0.00} m {ang:0} deg {v0:0} m/s: {res.outcome}, the rule says {want}"); }
                            if (!low && !deadOn && !rough && res.outcome == "crash") { fails++; Debug.Log($"RIDEOVER-FAIL {shape} top {h:0.00} m {ang:0} deg {v0:0} m/s: a crash on a glancing hit"); }
                            if (!low && res.angle >= 0f && v0 > 8.5f)
                            {
                                bool sq = res.angle <= Rider.DeadOnAngle;
                                if (sq && res.outcome != "crash" && res.into <= 0f) { fails++; Debug.Log($"RIDEOVER-FAIL {shape} top {h:0.00} m {ang:0} deg {v0:0} m/s: met {res.angle:0} deg off square, {res.outcome}, not a crash"); }
                                if (!sq && res.outcome == "crash") { fails++; Debug.Log($"RIDEOVER-FAIL {shape} top {h:0.00} m {ang:0} deg {v0:0} m/s: met {res.angle:0} deg off square, a crash"); }
                            }
                            if (Mathf.Abs(res.offLine) > 0.05f) { fails++; Debug.Log($"RIDEOVER-FAIL {shape} top {h:0.00} m {ang:0} deg {v0:0} m/s: the board came {res.offLine:+0.00;-0.00} m off its line (the angle asked for is not the one met)"); }
                        }
        }
        catch (System.Exception e) { fails++; Debug.LogError("RIDEOVER exception " + e); }
        var sb = new System.Text.StringBuilder("RIDEOVER-TABLE (rides of 3 speeds each; over / deflect / crash / stopped)\n");
        foreach (string shape in Shapes)
            foreach (float h in Heights)
            {
                sb.Append($"  {shape,-5} top {h:0.00} m:");
                foreach (float ang in Angles)
                {
                    string key = $"{shape} h{h:0.00} a{ang:0}";
                    int o = Get(table, key + " over"), d = Get(table, key + " deflect"), c = Get(table, key + " crash"), s = Get(table, key + " stopped");
                    sb.Append($"   {ang,2:0} deg {o}/{d}/{c}/{s}");
                }
                sb.Append('\n');
            }
        sb.Append($"RIDEOVER result: {fails} against the rule");
        Debug.Log(sb.ToString());
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>How far the mountain's own obstacles stand out of the snow (Rider.StandsOut), by kind, over two cycles
    /// of the course and its margins: what the ride-over height lets you ride over.</summary>
    static void Survey()
    {
        var seen = new HashSet<Collider>();
        float[] edges = { 0.3f, 0.6f, 0.9f, 1.5f, 3f };
        var hist = new SortedDictionary<string, int[]>();
        for (float z = Course.First; z < Course.First + 2f * Course.Cycle; z += 30f)
        {
            float cx = Slope.Centre(z);
            r.ResetAt(cx, z); Stream();
            foreach (var c in Physics.OverlapBox(new Vector3(cx, Slope.Height(cx, z), z), new Vector3(Slope.PisteHalf + 45f, 400f, 15f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob == null || ob.always || ob.roof || !seen.Add(c)) continue;
                float h = Rider.StandsOut(c);
                string kind = ob.label + " " + c.name;
                if (!hist.TryGetValue(kind, out var a)) hist[kind] = a = new int[edges.Length + 1];
                int k = 0; while (k < edges.Length && h > edges[k]) k++;
                a[k]++;
            }
        }
        var sb = new System.Text.StringBuilder($"RIDEOVER-SURVEY standing out of the snow (<0.3 / 0.3-0.6 / 0.6-0.9 / 0.9-1.5 / 1.5-3 / >3 m), StepUp {Rider.StepUp}:\n");
        foreach (var kv in hist) sb.Append($"  {kv.Key,-28} {string.Join(" / ", kv.Value)}\n");
        Debug.Log(sb.ToString());
    }

    static int Get(Dictionary<string, int> d, string k) => d.TryGetValue(k, out int v) ? v : 0;

    /// <summary>
    /// `count` open places on plain piste from z, at least 40 m apart, each with room for a ride into a thing: 22 m up
    /// the hill of it and 8 m past, cruising (not the park, whose boxes and rails catch the board), solid, no crevasse,
    /// no obstacle within 10 m of the line, no roof or lift deck to ride up on. Too few is a failure of the check, not a
    /// quiet fallback (it once fell back into the park: a ride caught on a box there, 1 m up and off its line).
    /// </summary>
    static List<float> OpenSpots(float z, int count)
    {
        const float Before = 22f, After = 8f;
        var spots = new List<float>();
        var why = new Dictionary<string, int>();
        void No(string k) { why.TryGetValue(k, out int n); why[k] = n + 1; }
        for (; z < Course.First + 3f * Course.Cycle && spots.Count < count; z += 5f)
        {
            string bad = null;
            for (float zz = z - Before; zz < z + After && bad == null; zz += 2f)
            {
                float x = Slope.Centre(zz);
                if (Course.At(zz) != Course.Section.Cruise) bad = "not cruise";
                else if (!Course.Solid(x, zz, 10f) || Course.CrevasseNear(x, zz, 8f)) bad = "crevasse";
                else if (Slope.Normal(x, zz).y < 0.7f) bad = "steep";
            }
            if (bad == null)
            {
                r.ResetAt(Slope.Centre(z), z); Stream();
                float zm = z + (After - Before) * 0.5f;
                foreach (var c in Physics.OverlapBox(new Vector3(Slope.Centre(zm), Slope.Height(Slope.Centre(zm), zm), zm), new Vector3(10f, 40f, (After + Before) * 0.5f)))
                    if (c.GetComponentInParent<Obstacle>() != null) { bad = "obstacle"; break; }
                for (float zz = z - Before; zz < z + After && bad == null; zz += 2f)
                    for (float dx = -10f; dx <= 10f && bad == null; dx += 2f)
                    {
                        float x = Slope.Centre(zz) + dx;
                        if (Roofs.Near(new Vector3(x, Slope.DrawnHeight(x, zz), zz), 3f)) bad = "roof";
                    }
            }
            if (bad == null) { spots.Add(z); z += 35f; continue; }
            No(bad);
        }
        if (spots.Count < count)
        {
            var sb = new System.Text.StringBuilder($"RIDEOVER-FAIL only {spots.Count} open places on the piste:");
            foreach (var kv in why) sb.Append($" {kv.Key} {kv.Value}");
            Debug.Log(sb.ToString());
            fails++;
            while (spots.Count < count) spots.Add(Course.First + 120f + spots.Count * 40f);
        }
        return spots;
    }

    static void Stream() { terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms(); }

    struct Result { public string outcome; public bool hop; public float vIn, vOut, peak, into, angle, offLine; }

    /// <summary>A thing `shape` standing h over the snow at (centre, z), met at `ang` degrees off dead on at v0.</summary>
    static GameObject Make(string shape, float h, float ang, float x, float z, out float side)
    {
        Vector3 n = Slope.Normal(x, z);
        float y = Slope.DrawnHeight(x, z);
        var tilt = Quaternion.FromToRotation(Vector3.up, n);
        GameObject g;
        side = 0f;
        if (shape == "block")
        {
            // a slab with square sides, 4 m long and 0.8 m through, turned by the angle (so its long face is met that far
            // off square), sunk a metre
            g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.localScale = new Vector3(4f, h + 1f, 0.8f);
            g.transform.SetPositionAndRotation(new Vector3(x, y, z) + n * ((h + 1f) * 0.5f - 1f), tilt * Quaternion.Euler(0f, ang, 0f));
            g.AddComponent<BoxCollider>();
            g.name = "RideOverBlock";
        }
        else if (shape == "stump")
        {
            // a flat-topped post 0.6 m across: met off its middle, its face is that far off square
            g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.localScale = new Vector3(0.6f, (h + 1f) * 0.5f, 0.6f);
            g.transform.SetPositionAndRotation(new Vector3(x, y, z) + n * ((h + 1f) * 0.5f - 1f), tilt);
            var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh; mc.convex = true;
            side = (Rider.R + 0.3f) * Mathf.Sin(ang * Mathf.Deg2Rad);
            g.name = "RideOverStump";
        }
        else
        {
            // a squat egg 1.6 m across, its top h over the snow (its sides come up out of the snow at a slant)
            g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            float ry = Mathf.Max(h, 0.7f);
            g.transform.localScale = new Vector3(1.6f, 2f * ry, 1.6f);
            g.transform.SetPositionAndRotation(new Vector3(x, y, z) + n * (h - ry), tilt);
            var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh; mc.convex = true;
            // where on its round side the board meets it, for that angle: found by the height there
            side = (Rider.R + 0.8f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((ry - h) / ry, 2f)))) * Mathf.Sin(ang * Mathf.Deg2Rad);
            g.name = "RideOverRock";
        }
        g.AddComponent<Obstacle>().label = shape == "stump" ? "Tree" : "Rock";
        Physics.SyncTransforms();
        return g;
    }

    static Result Ride(string shape, float h, float ang, float v0, float z, System.Action<int, Vector3> frame)
    {
        float x = Slope.Centre(z);
        var g = Make(shape, h, ang, x, z, out float side);
        var res = new Result { outcome = "?" };
        try
        {
            float zs = z - 16f, xs = x + side;
            r.ResetAt(xs, zs); r.Begin();
            r.Heading = 0f; r.Steer = 0f;
            Vector3 n = Slope.Normal(xs, zs);
            r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, n).normalized * v0;
            Stream();
            if (only != null)
                for (float dz = 0f; dz <= 2f; dz += 0.5f)
                    for (float dx = -0.5f; dx <= 2f; dx += 0.5f)
                        Debug.Log($"RIDEOVER-START ground dx={dx} dz={dz}: height {Slope.Height(xs + dx, zs + dz) - Slope.Height(xs, zs):0.00} drawn {Slope.DrawnHeight(xs + dx, zs + dz) - Slope.DrawnHeight(xs, zs):0.00} hill {LiftLine.HillHeight(xs + dx, zs + dz):0.00} n.y {Slope.Normal(xs + dx, zs + dz).y:0.00} section {Course.At(zs + dz)} crev {Course.CrevasseCut(xs + dx, zs + dz):0.00}");
            if (only != null)
                foreach (var c in Physics.OverlapSphere(r.Body.position, 3f, ~0, QueryTriggerInteraction.Collide))
                    Debug.Log($"RIDEOVER-START near {c.name} ({c.GetType().Name}) layer {c.gameObject.layer} trigger={c.isTrigger} at {c.bounds.center - r.Body.position} size {c.bounds.size} parent {(c.transform.parent != null ? c.transform.parent.name : "-")}");
            lastCrash = null; hopped = false; glanced = false; glanceInto = 0f; met = false;
            float t = 0f, vNear = v0;
            int step = 0;
            while (t < 4f)
            {
                // hold the line straight at it (and the speed) until there
                if (r.Z < z - 1.5f && !glanced)
                {
                    // (the finger sets the sideways speed: steer2's direct steering. Held on the position alone; the old
                    // carve's "- Heading * 2" damping now held the board off its line, a 20-degree hit met at 33)
                    r.Steer = Mathf.Clamp((xs - r.X) * 1.5f, -1f, 1f);
                    if (r.Speed > v0) r.Body.linearVelocity *= v0 / r.Speed;
                    vNear = r.Speed;
                }
                else r.Steer = 0f;
                r.TestContacts(Dt);
                if (!r.Crashed) r.Tick(Dt);
                if (frame != null) { body.PhysicsStep(); }
                Physics.Simulate(Dt);
                if (frame != null) { body.Apply(Dt); frame(step, g.transform.position); }
                step++;
                t += Dt;
                if (only != null)
                {
                    var o = r.RidingOver; string os = "";
                    foreach (var c in o) if (c != null) os += $" {c.name} top {c.bounds.max.y - Slope.DrawnHeight(c.bounds.center.x, c.bounds.center.z):0.00}";
                    Debug.Log($"RIDEOVER-TRACE t={t:0.000} z-zo={r.Z - z:0.00} x-xo={r.X - x:0.00} up={r.Body.position.y - Rider.R - Slope.DrawnHeight(r.X, r.Z):0.00} v={r.Body.linearVelocity} air={r.Airborne} crashed={r.Crashed} lift={r.Lift:0.00} roof={(r.OnRoof != null)} hop={hopped} speed={r.Speed:0.0} heading={r.Heading:0.00} steer={r.Steer:0.00} over:{os}");
                }
                if (Mathf.Abs(r.Z - z) < 2.5f) res.peak = Mathf.Max(res.peak, r.Body.position.y - Rider.R - Slope.DrawnHeight(r.X, r.Z));
                if (r.Z < z - 1.5f) res.offLine = r.X - xs;
                if (r.Crashed) { res.outcome = "crash"; break; }
                if (r.Z > z + 4f) { res.outcome = glanced ? "deflect" : "over"; break; }
                if (r.Speed < 1f && t > 0.5f) { res.outcome = glanced ? "deflect" : "stopped"; break; }
            }
            if (res.outcome == "?") res.outcome = glanced ? "deflect" : "stopped";
            res.hop = hopped; res.vIn = vNear; res.vOut = r.Speed; res.into = glanceInto;
            res.angle = met ? r.LastHitAngle : -1f;
        }
        finally
        {
            Object.DestroyImmediate(g);
            if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
        }
        return res;
    }

    // ---------------------------------------------------------------- frame strips (needs the GPU)

    public static void Strips()
    {
        Setup();
        string outDir = System.Environment.GetEnvironmentVariable("RIDEOVER_OUT") ?? "rideover-strips";
        System.IO.Directory.CreateDirectory(outDir);
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        var spots = OpenSpots(Course.First + 60f, 6);
        var cases = new (string shape, float h, float ang, float v)[]
        {
            ("rock", 0.4f, 0f, 16f), ("block", 0.5f, 0f, 16f), ("stump", 0.4f, 0f, 20f),
            ("block", 1.5f, 35f, 18f), ("stump", 1.5f, 45f, 18f), ("block", 1.5f, 0f, 18f),
        };
        int k = 0;
        foreach (var c in cases)
        {
            float z = spots[k % 6];
            const int W = 360, H = 300, Frames = 6;
            var strip = new Texture2D(W * Frames, H, TextureFormat.RGB24, false);
            var rt = new RenderTexture(W, H, 24);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var shots = new List<(int step, Vector3 at)>();
            int taken = 0;
            // the frames: from 0.3 s before it, every 0.11 s, from beside
            int every = Mathf.Max(1, Mathf.RoundToInt(0.11f / Dt));
            int startStep = -1;
            var res = Ride(c.shape, c.h, c.ang, c.v, z, (step, at) =>
            {
                if (taken >= Frames) return;
                if (startStep < 0 && r.Z > at.z - c.v * 0.3f) startStep = step;
                if (startStep < 0 || (step - startStep) % every != 0) return;
                Vector3 look = new Vector3(at.x, Slope.DrawnHeight(at.x, at.z) + 0.6f, at.z);
                Vector3 from = look + new Vector3(-7f, 1.2f, 0.5f);
                cam.fieldOfView = 40f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
                cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                strip.SetPixels(taken * W, 0, W, H, tex.GetPixels());
                taken++;
            });
            cam.targetTexture = null; RenderTexture.active = null;
            strip.Apply();
            string file = $"{outDir}/RideOver_{c.shape}_{c.h:0.0}m_{c.ang:0}deg_{c.v:0}ms_{res.outcome}.png";
            System.IO.File.WriteAllBytes(file, strip.EncodeToPNG());
            Debug.Log($"RIDEOVER-STRIP {file}: {res.outcome}{(res.hop ? " (popped over)" : "")}, {taken} frames");
            k++;
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }
}
