using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// What gets a rider up to the lift cables: each lift's hill (or, on code from before the hills, its launch ramp).
///   Ride:  the game's own rider, set going straight down the lift's line under the cable at several speeds from
///          25 m above the hill (or ramp), steered only to hold that line, never popping: does it end up grinding a
///          cable? Logs each try and a total. -executeMethod LiftHillCheck.Ride (-nographics is fine)
///          $HILL_SEEDS mountains (default 6).
///   Shots: each hill (ramp) of two mountains from the piste up the hill of it, from beside it, and from a rider's
///          height on the way in; and the same view with every solid collider near it drawn in see-through red.
///          -executeMethod LiftHillCheck.Shots (no -nographics). $HILL_TAG names them, $HILL_SHOTS the folder.
/// </summary>
public static class LiftHillCheck
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags PS = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>The lift's hill or ramp: where along the line to start a rider from, which cable, and the spot itself.</summary>
    static bool Target(LiftLine.Lift l, out float start, out float side, out float at, out string what)
    {
        start = side = at = 0f; what = "";
        var hf = typeof(LiftLine.Lift).GetField("hill", PS);
        var rf = typeof(LiftLine.Lift).GetField("ramp", PS);
        object o = hf?.GetValue(l) ?? rf?.GetValue(l);
        if (o == null) return false;
        var t = o.GetType();
        side = (float)t.GetField("side").GetValue(o);
        if (t.GetField("s0") != null)
        {
            float s0 = (float)t.GetField("s0").GetValue(o), len = (float)t.GetField("length").GetValue(o);
            start = s0 - 25f; at = s0 + len; what = "ramp";
        }
        else
        {
            float s = (float)t.GetField("s").GetValue(o), rise = (float)t.GetField("rise").GetValue(o);
            start = s - rise - 16f; at = s; what = "hill";
        }
        return true;
    }

    static readonly string stepOf = System.Environment.GetEnvironmentVariable("HILL_STEP");   // "seed,lift,speed": that ride, step by step
    public static void Ride()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        // a fresh player's settings (TestPrefs): the ride is the one the prefs hold, and one another run left on a
        // toboggan, kayak, car or bear (none of them take a cable) grinds nothing at all
        TestPrefs.Pin();
        TestBoot.Boot(game);
        var rider = game.rider; var body = game.body;
        var terrain = game.terrain; var props = game.props;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("HILL_SEEDS"), out var n) ? n : 6;
        float[] speeds = { 14f, 18f, 22f, 26f, 30f, 34f, 38f };
        // two fingers ride each: the one this check always had (it steers by where the board is and how fast it is
        // crossing the line: tuned for the old carve, under direct steering it sits a metre or two beside a line that
        // runs across the fall line), and one that asks for the sideways speed the cable's line itself needs
        int direct = 0;
        int lifts = 0, with = 0, tries = 0, grinds = 0, grindsDirect = 0;
        var bySpeed = new int[speeds.Length]; var bySpeedDirect = new int[speeds.Length];
        const float Dt = 1f / 60f;
        for (int si = 0; si < seeds; si++)
        {
            int seed = 4242 + si * 977;
            TestBoot.NewMountain(game, seed, terrain, props);
            foreach (var l in LiftLine.Lifts)
            {
                lifts++;
                if (!Target(l, out float s0, out float side, out float at, out string what)) continue;
                with++;
                var sb = new System.Text.StringBuilder();
                float heading = Mathf.Atan2(l.dir.x, l.dir.z);
                for (direct = 0; direct < 2; direct++)
                for (int vi = 0; vi < speeds.Length; vi++)
                {
                    if (direct == 0) tries++;
                    Vector3 p0 = l.At(s0, side * LiftLine.Half, 0f);
                    CableGrind.Reset();   // (or the span just ridden can't be boarded again: CableGrind's lastKey)
                    rider.ResetAt(p0.x, p0.z); body.ResetVisual(); rider.Begin();
                    rider.Heading = heading;
                    Vector3 n0 = Slope.Normal(p0.x, p0.z);
                    rider.Body.linearVelocity = Vector3.ProjectOnPlane(l.dir, n0).normalized * speeds[vi];
                    bool grind = false, flew = false; string end = "rode on";
                    bool trace = System.Environment.GetEnvironmentVariable("HILL_TRACE") == "1";
                    string off = ""; int touches = 0; float bestRel = 99f, bestSide = 0f, bestAt = 0f, bestRise = 0f; bool bestOk = false;
                    for (float t = 0f; t < 7f; t += Dt)
                    {
                        Vector3 p = rider.transform.position;
                        float c = (p.x - l.top.x) * l.across.x + (p.z - l.top.z) * l.across.z;
                        // hold the cable's line: toward it, and against any speed across it (on the snow and in the air alike)
                        Vector3 vel = rider.Body.linearVelocity;
                        float across = vel.x * l.across.x + vel.z * l.across.z;
                        // (slide: a line off the hill's fall line is held with the finger held: the line's own sideways speed
                        // is asked for (CarveBot.ForSide), and the old feedback on where the board is rides on top)
                        float alongNow = vel.x * l.dir.x + vel.z * l.dir.z;
                        rider.Steer = Mathf.Clamp(CarveBot.ForSide(rider, alongNow * l.dir.x) + (side * LiftLine.Half - c) * 0.12f - across * 0.15f, -1f, 1f);   // (gains for main's 3x tighter turns: 9f67fee)
                        if (direct == 1)
                        {
                            // the sideways speed that keeps it on the cable's line: the line's own, and back onto it at 2/s
                            float back = Mathf.Clamp((side * LiftLine.Half - c) * 2f, -6f, 6f);
                            float alongV = vel.x * l.dir.x + vel.z * l.dir.z;
                            rider.Steer = CarveBot.ForSide(rider, alongV * l.dir.x + back * l.across.x);
                        }
                        for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
                        if (stepOf == $"{seed},{l.id},{speeds[vi]:0}")
                            Debug.Log($"HILLSTEP t={t:0.00} along={(p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z - at:0.0} c-line={c - side * LiftLine.Half:0.00} across={across:0.00} steer={rider.Steer:0.00} air={rider.Airborne} v={rider.Body.linearVelocity} liftdir=({l.dir.x:0.000},{l.dir.z:0.000}) head={rider.Heading:0.000} above={rider.AboveGround:0.0}");
                        if (rider.OnCable) { grind = true; end = "grinding"; break; }
                        if (rider.Crashed) { end = "crashed"; break; }
                        if (rider.Airborne)
                        {
                            Vector3 q = rider.transform.position, vv = rider.Body.linearVelocity;
                            float al = (q.x - l.top.x) * l.dir.x + (q.z - l.top.z) * l.dir.z;
                            float cc = (q.x - l.top.x) * l.across.x + (q.z - l.top.z) * l.across.z;
                            int sp = LiftLine.SpanAt(l, al);
                            if (sp < l.Spans)
                            {
                                float rel = q.y - Rider.R - l.CableY(sp, al);
                                if (Mathf.Abs(rel) < Mathf.Abs(bestRel)) { bestRel = rel; bestSide = cc - side * LiftLine.Half; bestAt = al - at; bestOk = LiftLine.Boardable(l, sp, al); bestRise = vv.y - LiftLine.CableSlope(l, sp, al) * (vv.x * l.dir.x + vv.z * l.dir.z); }
                            }
                        }
                        if (rider.Airborne && !flew && off == "")
                        {
                            Vector3 q = rider.transform.position, vv = rider.Body.linearVelocity;
                            float al = (q.x - l.top.x) * l.dir.x + (q.z - l.top.z) * l.dir.z;
                            off = $" took off at {al - at:0.0} m from the crest, plan {vv.x * l.dir.x + vv.z * l.dir.z:0.0} up {vv.y:0.0} m/s, {q.y - Slope.Height(q.x, q.z):0.00} over the snow";
                        }
                        if (rider.Airborne) flew = true;
                        else if (flew) { end = "landed short"; flew = false; touches++; }
                        float along = (p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z;
                        if (along > at + 120f) { end = "past it"; break; }
                    }
                    if (grind) { if (direct == 0) { grinds++; bySpeed[vi]++; } else { grindsDirect++; bySpeedDirect[vi]++; } }
                    var testRide = typeof(LiftLine).GetMethod("TestRide");   // (only with hills)
                    if (trace && what == "hill" && testRide != null)
                    {
                        var args = new object[] { l, speeds[vi], null, null, null };
                        bool m = (bool)testRide.Invoke(null, args);
                        var mo = (Vector4)args[2];
                        Debug.Log($"HILLTRACE seed {seed} lift {l.id} v {speeds[vi]:0}: game {(grind ? "GRIND" : end)}{off}; nearest the cable's height {bestRel:0.00} m (board under it: -), {bestSide:0.00} m to the side, {bestAt:0} m past the crest, boardable {bestOk}, rising {bestRise:0.0} m/s; model {(m ? "reaches" : "misses")}, took off at {mo.x - at:0.0} m from the crest, plan {mo.y:0.0} up {mo.z:0.0} m/s");
                    }
                    if (vi == 0 && direct == 1) sb.Append(" | asking for the line's sideways speed:");
                    sb.Append($" {speeds[vi]:0}:{(grind ? "GRIND" : end)}{(touches > 0 ? "/" + touches : "")}");
                }
                Debug.Log($"HILLRIDE seed {seed} lift {l.id}{(l.second ? "b" : "")} {what} at {at:0} m along:{sb}");
            }
        }
        // what the hills cost to lay out: the same mountains' lifts laid with them and without
        double msWith = 0, msWithout = 0;
        for (int si = 0; si < seeds; si++)
        {
            int seed = 4242 + si * 977;
            foreach (bool on in new[] { false, true })
            {
                LiftLine.HillsOn = on;
                Slope.Init(seed + 1); _ = LiftLine.Lifts; Slope.Init(seed);   // (another mountain between: everything laid afresh)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _ = LiftLine.Lifts;
                if (on) msWith += sw.Elapsed.TotalMilliseconds; else msWithout += sw.Elapsed.TotalMilliseconds;
            }
        }
        LiftLine.HillsOn = true;
        Debug.Log($"HILLRIDE laying a mountain's lifts: {msWithout / seeds:0} ms without hills, {msWith / seeds:0} ms with (mean of {seeds})");
        var bs = new System.Text.StringBuilder();
        for (int i = 0; i < speeds.Length; i++) bs.Append($" {speeds[i]:0} m/s {bySpeed[i]}/{with};");
        Debug.Log($"HILLRIDE done: {seeds} mountains, {lifts} lifts, {with} with a hill or ramp; {grinds} of {tries} rides reached a cable grind.{bs}");
        var bd = new System.Text.StringBuilder();
        for (int i = 0; i < speeds.Length; i++) bd.Append($" {speeds[i]:0} m/s {bySpeedDirect[i]}/{with};");
        Debug.Log($"HILLRIDE done, asking for the line's sideways speed: {grindsDirect} of {tries} rides reached a cable grind.{bd}");
        // (main: 203 of 266, 76%; fewer than 60% reaching a cable, or no lift with a hill at all, means the hills broke)
        bool pass = with > 0 && tries > 0 && grinds * 100 >= tries * 60;
        Debug.Log(pass ? "HILLRIDE PASS" : $"HILLRIDE FAIL: {grinds} of {tries} rides reached a cable grind (at least 60% wanted), {with} lifts with a hill or ramp");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(pass);
    }

    public static void Shots()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        var game = TestHarness.Boot();
        var rider = game.rider; var body = game.body;
        var terrain = game.terrain; var props = game.props; var dayNight = game.dayNight;
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var lifts = Object.FindFirstObjectByType<SkiLift>();
        var hamlets = Object.FindFirstObjectByType<Hamlets>();
        var liftUpd = typeof(SkiLift).GetMethod("Update", NP); var hamUpd = typeof(Hamlets).GetMethod("Update", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var cam = Camera.main; cam.GetComponent<ChaseCamera>().enabled = false;
        string tag = System.Environment.GetEnvironmentVariable("HILL_TAG"); if (string.IsNullOrEmpty(tag)) tag = "now";
        string dir = System.Environment.GetEnvironmentVariable("HILL_SHOTS"); if (string.IsNullOrEmpty(dir)) dir = ".";
        Directory.CreateDirectory(dir);
        var ghostMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        ghostMat.SetFloat("_Surface", 1f); ghostMat.SetFloat("_Blend", 0f);
        ghostMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); ghostMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ghostMat.SetFloat("_ZWrite", 0f); ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); ghostMat.renderQueue = 3000;
        ghostMat.SetColor("_BaseColor", new Color(1f, 0f, 0f, 0.45f));
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        bool warm = false;
        foreach (int seed in new[] { 4242, 2131 })
        {
            TestBoot.NewMountain(game, seed, terrain, props);
            int shots = 0;
            foreach (var l in LiftLine.Lifts)
            {
                if (shots >= 3) break;
                if (!Target(l, out float s0, out float side, out float at, out string what)) continue;
                shots++;
                Vector3 spot = l.At(at, side * LiftLine.Half, 0f); spot.y = Slope.Height(spot.x, spot.z);
                rider.ResetAt(spot.x, spot.z - 60f); body.ResetVisual(); rider.Stop();
                for (int k = 0; k < 3; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
                for (int k = 0; k < 6; k++) { liftUpd.Invoke(lifts, null); hamUpd?.Invoke(hamlets, null); }
                typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
                if (!warm) { Shoot(cam, atmo, lifts, liftUpd, Path.Combine(dir, "HillWarmup.png")); warm = true; }
                float sgn = Mathf.Sign(Slope.Centre(spot.z) - spot.x);
                // (from the piste side: up the hill on the run, from across the line a little below, high above it, and
                // a rider's eye on the way in)
                var views = new (string name, Vector3 eye, Vector3 look, float up)[]
                {
                    ("piste", new Vector3(Slope.Centre(spot.z - 50f) - sgn * 8f, 0f, spot.z - 50f), spot + Vector3.up * 4f, 3f),
                    ("side", spot + new Vector3(sgn * 32f, 0f, 14f), spot + Vector3.up * 2f, 6f),
                    ("high", spot + new Vector3(sgn * 45f, 0f, -45f), spot, 28f),
                    ("in", l.At(s0 + 2f, side * LiftLine.Half, 0f), spot + Vector3.up * 6f, 1.8f),
                };
                foreach (var v in views)
                {
                    Vector3 eye = v.eye; eye.y = Slope.DrawnHeight(eye.x, eye.z) + v.up;
                    cam.fieldOfView = v.name == "in" ? 70f : 60f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(v.look - eye));
                    Shoot(cam, atmo, lifts, liftUpd, Path.Combine(dir, $"Hill_{tag}_s{seed}_lift{l.id}_{v.name}.png"));
                }
                // the same view from beside it, every solid collider within 30 m in see-through red
                Physics.SyncTransforms();
                var ghosts = new List<GameObject>();
                foreach (var c in Physics.OverlapBox(spot, new Vector3(30f, 30f, 30f)))
                {
                    if (c.isTrigger || !c.enabled) continue;
                    GameObject g = null;
                    if (c is MeshCollider mc && mc.sharedMesh != null) { g = new GameObject("ghost", typeof(MeshFilter), typeof(MeshRenderer)); g.GetComponent<MeshFilter>().sharedMesh = mc.sharedMesh; g.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation); g.transform.localScale = c.transform.lossyScale; }
                    else if (c is BoxCollider bc) { g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(g.GetComponent<Collider>()); g.transform.SetPositionAndRotation(c.transform.TransformPoint(bc.center), c.transform.rotation); g.transform.localScale = Vector3.Scale(bc.size, c.transform.lossyScale); }
                    else if (c is CapsuleCollider || c is SphereCollider) { g = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(g.GetComponent<Collider>()); g.transform.position = c.bounds.center; g.transform.localScale = c.bounds.size; }
                    if (g == null) continue;
                    g.GetComponent<MeshRenderer>().sharedMaterial = ghostMat;
                    ghosts.Add(g);
                }
                {
                    var v = views[1];
                    Vector3 eye = v.eye; eye.y = Slope.DrawnHeight(eye.x, eye.z) + v.up;
                    cam.fieldOfView = 60f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(v.look - eye));
                    Shoot(cam, atmo, lifts, liftUpd, Path.Combine(dir, $"Hill_{tag}_s{seed}_lift{l.id}_colliders.png"));
                }
                foreach (var g in ghosts) Object.DestroyImmediate(g);
                Debug.Log($"HILLSHOT seed {seed} lift {l.id}: {what} at x {spot.x:0} z {spot.z:0}, {ghosts.Count} solid colliders within 30 m");
            }
        }
        TestHarness.Exit(0);
    }

    static void Shoot(Camera cam, Atmosphere atmo, SkiLift lifts, MethodInfo liftUpd, string file)
    {
        DynamicGI.UpdateEnvironment();
        if (atmo != null) typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        liftUpd.Invoke(lifts, null); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(tex); rt.Release();
    }
}
