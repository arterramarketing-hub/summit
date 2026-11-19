using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Stress test for two playtest bugs: a rider (or thrown body) launched who never comes down, and a rider stopped dead
/// or thrown backwards by a small bump or jump.
///
/// Crashes: into trees, rocks and crags, signs, over the deadly cliffs, into crevasses and in mid-air, at 40-200
/// km/h, with the game's own hit-stop running (SummitGame.TestHitStops: time all but stops on the impact, physics
/// steps shrink with it, exactly as on the phone). Flags a body more than 30 m above the ground at the end, or faster
/// than 80 m/s after the first two seconds.
///
/// Rides: over every kicker, jump cliff, rail and box and down the open piste at a range of speeds and headings.
/// Flags any step where the speed along the way you were going drops by more than 60% in 0.2 s without a crash, or
/// the travel turns back up the hill.
///
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod StressTest.Run -logFile stress.log
/// ($STRESS_SEEDS sets how many mountains; $STRESS_HITSTOP=0 runs the crashes without the hit-stop.)
/// </summary>
public static class StressTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float RealStep = 1f / 120f;   // real time per physics step (the hit-stop shrinks the step, not their rate)
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static string lastCrash = "";
    static int crashes, launched, fast, rides, stopped, reversed, launchedRides, longFlights;
    static float flightTotal, flightMax;
    static readonly Dictionary<string, int> kinds = new Dictionary<string, int>();
    static int n;
    static readonly string trace = System.Environment.GetEnvironmentVariable("STRESS_TRACE");
    static readonly string only = System.Environment.GetEnvironmentVariable("STRESS_ONLY");

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.Crash += why => lastCrash = why;
        bool hitStop = System.Environment.GetEnvironmentVariable("STRESS_HITSTOP") != "0";
        SummitGame.TestHitStops = hitStop;
        int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("STRESS_SEEDS"), out int sv) ? sv : 4;
        try
        {
            for (int seed = 0; seed < seeds; seed++)
            {
                Slope.Init(2000 + seed * 131);
                terrain.Rebuild(); props.Rebuild();
                var dress = Object.FindFirstObjectByType<CourseDressing>();
                if (dress != null) { r.ResetAt(Slope.Centre(Course.First), Course.First); dress.Rebuild(); }
                Crashes(seed);
                Rides(seed);
            }
        }
        catch (System.Exception e) { Debug.LogError("STRESS exception " + e); }
        SummitGame.TestHitStops = false;
        var sb = new System.Text.StringBuilder();
        foreach (var kv in kinds) sb.Append($"{kv.Key}={kv.Value} ");
        Debug.Log($"STRESS summary (hit-stop {(hitStop ? "on" : "off")}): {crashes} crashes, {launched} launched (>30 m up at the end), {fast} fast (>80 m/s after 2 s); " +
                  $"{rides} rides, {stopped} stopped dead, {reversed} thrown back up the hill, {launchedRides} launched; by kind: {sb}");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Time.timeScale = 1f; Time.fixedDeltaTime = 1f / 120f;
        EditorApplication.Exit(0);
    }

    static void Count(string kind) { kinds.TryGetValue(kind, out int c); kinds[kind] = c + 1; }

    /// <summary>One physics step as the game runs it: the hit-stop's clock, then the rider, the body and PhysX, at the step the hit-stop leaves.</summary>
    static void Step()
    {
        game.TestTickHitStop(RealStep);
        float dt = Time.fixedDeltaTime;
        // what OnCollisionEnter does on the phone (batchmode sends no collision messages): the board into an obstacle
        if (!r.Crashed && r.Running)
        {
            Vector3 p = r.Body.position, v = r.Velocity;
            foreach (var c in Physics.OverlapCapsule(p, p + v * dt, Rider.R + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.attachedRigidbody == r.Body) continue;
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob != null && r.InvincibleTime <= 0f) { r.DoCrash(ob.label); break; }
            }
        }
        r.Tick(dt); body.PhysicsStep(); Physics.Simulate(dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 2 == 0 || r.Crashed) body.Apply(r.Crashed ? dt : 2f * dt);
        n++;
    }

    static void HitStopNow(float strength)
    {
        typeof(SummitGame).GetMethod("HitStop", NP).Invoke(game, new object[] { strength });
    }

    static void EndHitStop() { for (int i = 0; i < 240; i++) game.TestTickHitStop(RealStep); }

    static void StartAt(Vector3 at, float heading, float v0)
    {
        EndHitStop();
        typeof(SummitGame).GetField("falls", NP).SetValue(game, 0);
        r.ResetAt(at.x, at.z); body.ResetVisual(); r.Begin();
        r.Heading = Mathf.Clamp(heading, -1.4f, 1.4f);
        Vector3 dir = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
        r.Body.linearVelocity = Vector3.ProjectOnPlane(dir, Slope.Normal(at.x, at.z)).normalized * v0;
        lastCrash = "";
        terrain.Prewarm(); props.Prewarm();
    }

    // ---------------------------------------------------------------- crashes

    static void Crashes(int seed)
    {
        if (only != null && !only.StartsWith("crash")) return;
        // obstacles: trees, rocks and crags along the first two cycles, near enough the piste to be ridden at
        var targets = new List<(Vector3 at, string label)>();
        for (float z = 150f; z < Course.First + 2f * Course.Cycle && targets.Count < 24; z += 70f)
        {
            r.ResetAt(Slope.Centre(z), z); terrain.Prewarm(); props.Prewarm();
            Obstacle best = null; float bestD = 1e9f;
            foreach (var ob in Object.FindObjectsByType<Obstacle>(FindObjectsSortMode.None))
            {
                Vector3 c = ob.transform.position;
                if (c.z < z || c.z > z + 60f) continue;
                if (ob.label == "Bear!") continue;
                float off = Mathf.Abs(c.x - Slope.Centre(c.z));
                // a mix: a rock or crag when there is one about, else the nearest tree to the piste
                float score = off + (ob.label == "Rock" && targets.Count % 2 == 1 ? -40f : 0f);
                if (score < bestD) { bestD = score; best = ob; }
            }
            if (best != null) targets.Add((best.transform.position, best.label));
        }
        int k = 0;
        foreach (var t in targets)
        {
            float kmh = new[] { 40f, 80f, 120f, 160f, 200f }[k++ % 5];
            CrashInto($"seed{seed} {t.label} z{t.at.z:0} {kmh:0}kmh", t.at, kmh / 3.6f);
        }
        // signs at the start of the sections: straight through the posts, fast
        foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
        {
            if (b.name != "SectionSign") continue;
            Vector3 c = b.transform.position;
            float x = c.x - Mathf.Sign(c.x - Slope.Centre(c.z)) * 2.6f;
            CrashInto($"seed{seed} sign z{c.z:0} 120kmh", new Vector3(x, c.y, c.z), 120f / 3.6f, true);
            break;
        }
        // over the deadly cliffs, and off a ridge's cliff side
        for (int cyc = 0; cyc < 2; cyc++)
        {
            if (Cliffs.WallOf(cyc, out var w))
                foreach (float kmh in new[] { 60f, 140f })
                {
                    float z = w.z1 + 10f;
                    Vector3 at = new Vector3(Slope.Centre(z) + w.side * (w.edge - 12f), 0f, z);
                    Free($"seed{seed} wall c{cyc} {kmh:0}kmh", at, w.side * 0.9f, kmh / 3.6f, 10f);
                }
            if (Ridge.Built(cyc))
            {
                float z = Course.First + cyc * Course.Cycle + Ridge.At + Ridge.RampIn + 30f;
                for (int s = -1; s <= 1; s += 2)
                    if (Ridge.CliffSide(z, s)) Free($"seed{seed} ridge c{cyc} side{s} 90kmh", new Vector3(Slope.Centre(z), 0f, z), s * 0.7f, 25f, 12f);
            }
            // into a crevasse
            var cvs = Course.CrevassesOf(cyc);
            if (cvs.Length > 0)
            {
                var cv = cvs[0];
                float z = cv.z0 + 5f;
                Free($"seed{seed} crevasse c{cyc} 70kmh", new Vector3(cv.x - 12f, 0f, z), 1.0f, 70f / 3.6f, 8f);
            }
        }
        // thrown just before a kicker's lip (a tree or a sign beside it): the body goes off the lip at the board's speed
        float zq = 60f; int kq = 0;
        while (kq < 3 && Slope.NextKicker(zq, out var kat))
        {
            zq = kat.z + 5f; kq++;
            foreach (float kmh in new[] { 120f, 200f })
            {
                string name = $"seed{seed} lipcrash z{kat.z:0} {kmh:0}kmh";
                StartAt(kat - new Vector3(0f, 0f, 30f), 0f, kmh / 3.6f);
                for (float t = 0f; t < 4f && !r.Crashed; t += RealStep)
                {
                    r.Steer = Mathf.Clamp((kat.x - r.X) * 0.08f - r.Heading * 0.5f, -1f, 1f);
                    Step();
                    if (!r.Airborne && r.Z > kat.z - 2.5f) { r.DoCrash("Tree"); break; }
                }
                if (r.Crashed) PlayOut(name); else Debug.Log($"STRESS-INFO {name}: no crash");
            }
        }
        // thrown in mid-air: a crash high off a jump cliff (as a sign or a tree hit in the air would)
        for (int cyc = 0; cyc < 2; cyc++)
            for (int j = 0; j < 2; j++)
                if (Cliffs.JumpOf(cyc, j, out var jc))
                    foreach (float kmh in new[] { 90f, 180f })
                        AirCrash($"seed{seed} aircrash c{cyc}k{j} {kmh:0}kmh", jc, kmh / 3.6f);
    }

    /// <summary>Ride straight at a point (a tree, a rock, a sign's post) from 25 m up the hill.</summary>
    static void CrashInto(string name, Vector3 at, float v0, bool sign = false)
    {
        Vector3 from = at - new Vector3(0f, 0f, 25f);
        from.x += (at.x - from.x) * 0f;
        float head = Mathf.Atan2(at.x - from.x, at.z - from.z);
        StartAt(from, head, v0);
        for (int i = 0; i < 360 && !r.Crashed; i++)
        {
            Vector3 p = r.transform.position;
            r.Heading = Mathf.Clamp(Mathf.Atan2(at.x - p.x, Mathf.Max(0.5f, at.z - p.z)), -1.4f, 1.4f);
            r.Steer = 0f;
            Step();
            if (p.z > at.z + 3f) break;
        }
        if (!r.Crashed) { Debug.Log($"STRESS-INFO {name}: no crash (missed it, z={r.Z:0} x-c={r.X - Slope.Centre(r.Z):0.0})"); return; }
        PlayOut(name);
    }

    /// <summary>Ride from a point heading a fixed way for a while (over a cliff, into a crevasse), until it crashes.</summary>
    static void Free(string name, Vector3 from, float heading, float v0, float secs)
    {
        StartAt(from, heading, v0);
        for (float t = 0f; t < secs && !r.Crashed; t += RealStep)
        {
            r.Steer = Mathf.Clamp((heading - r.Heading) * 2f, -1f, 1f);
            Step();
        }
        if (!r.Crashed) { Debug.Log($"STRESS-INFO {name}: no crash (z={r.Z:0} x-c={r.X - Slope.Centre(r.Z):0.0})"); return; }
        PlayOut(name);
    }

    /// <summary>Off a jump cliff's lip, and thrown from the board at the top of the flight.</summary>
    static void AirCrash(string name, Cliffs.Jump j, float v0)
    {
        StartAt(new Vector3(Slope.Centre(j.Start - 20f), 0f, j.Start - 20f), 0f, v0);
        for (float t = 0f; t < 10f && !r.Crashed; t += RealStep)
        {
            r.Steer = Mathf.Clamp((Slope.Centre(r.Z + 12f) - r.X) * 0.05f - r.Heading * 0.5f, -1f, 1f);
            Step();
            if (r.Airborne && r.AirTime > 0.5f && r.Velocity.y < 0f) { r.DoCrash("Tree"); break; }
        }
        if (!r.Crashed) { Debug.Log($"STRESS-INFO {name}: no crash"); return; }
        PlayOut(name);
    }

    /// <summary>After a crash: 8 s of real time, hit-stops on the impact and again mid-tumble, watching the body and the board's sphere.</summary>
    static void PlayOut(string name)
    {
        crashes++;
        string why = lastCrash;
        float maxV = 0f, maxVAt = 0f, maxUp = 0f, maxUpAt = 0f, maxRiderV = 0f, maxRiderAt = 0f, gameT = 0f, minScale = 1f;
        float flying = 0f, longestFlight = 0f, longestUp = 0f;
        Vector3 c = Vector3.zero;
        for (float t = 0f; t < 8f; t += RealStep)
        {
            if (Mathf.Abs(t - 0.6f) < RealStep * 0.5f || Mathf.Abs(t - 1.5f) < RealStep * 0.5f) HitStopNow(0.8f);   // a tumble into something (RagdollHit)
            minScale = Mathf.Min(minScale, Time.timeScale);
            gameT += Time.fixedDeltaTime;
            Step();
            c = body.RagdollCentre;
            float up = c.y - Slope.DrawnHeight(c.x, c.z);
            float v = body.RagdollSpeed;
            float rv = r.Body.linearVelocity.magnitude;
            if (rv > maxRiderV) { maxRiderV = rv; maxRiderAt = t; }
            // in the air: more than 3 m over the snow (and not down a crevasse or over a deadly drop)
            if (up > 3f && !Course.CrevasseNear(c.x, c.z, 1f, out _) && !Deadly.Below(c.x, c.z) && !Ridge.InCanyons(c.z)) { flying += Time.fixedDeltaTime; longestUp = Mathf.Max(longestUp, up); }
            else { longestFlight = Mathf.Max(longestFlight, flying); flying = 0f; }
            // (falling off a ridge's cliff into its canyon, or down a deadly cliff, is fast by right)
            if (t > 2f && v > maxV && !Ridge.InCanyons(c.z) && !Deadly.Below(c.x, c.z)) { maxV = v; maxVAt = t; }
            if (t > 2f && up > maxUp) { maxUp = up; maxUpAt = t; }
            if (float.IsNaN(c.x)) { Count("NaN"); Debug.Log($"STRESS-FLAG {name}: NaN"); return; }
        }
        longestFlight = Mathf.Max(longestFlight, flying);
        float endUp = c.y - Slope.DrawnHeight(c.x, c.z);
        bool down = Course.CrevasseNear(c.x, c.z, 1f, out _);
        Debug.Log($"STRESS-CRASH {name}: '{why}' body ends {endUp:0.0} m over the snow{(down ? " (in a crevasse)" : "")} at x-c={c.x - Slope.Centre(c.z):0} z={c.z:0}, " +
                  $"after 2 s fastest {maxV:0.0} m/s (t={maxVAt:0.0}) highest {maxUp:0.0} m (t={maxUpAt:0.0}); board sphere peaked {maxRiderV:0} m/s at t={maxRiderAt:0.00}; " +
                  $"dead={body.Dead}, game time {gameT:0.0}s, slowest hit-stop x{minScale:0.00}; longest flight {longestFlight:0.0}s of game time, up to {longestUp:0} m");
        flightTotal += longestFlight; flightMax = Mathf.Max(flightMax, longestFlight);
        if (longestFlight > 2.5f) { longFlights++; Count("flight>2.5s"); Debug.Log($"STRESS-FLAG {name}: thrown body in the air {longestFlight:0.0} s, up to {longestUp:0} m over the snow"); }
        if (endUp > 30f) { launched++; Count("launched"); Debug.Log($"STRESS-FLAG {name}: launched, ends {endUp:0} m above the snow"); }
        if (maxV > 80f) { fast++; Count("fast"); Debug.Log($"STRESS-FLAG {name}: body at {maxV:0} m/s at t={maxVAt:0.0}"); }
        if (maxRiderV > 200f) { Count("sphere>200m/s"); Debug.Log($"STRESS-FLAG {name}: the board's sphere at {maxRiderV:0} m/s at t={maxRiderAt:0.00}"); }
    }

    // ---------------------------------------------------------------- rides

    static void Rides(int seed)
    {
        float[] speeds = { 8f, 14f, 20f, 28f, 36f };
        // cruise kickers and the park's two
        var kickers = new List<Vector3>();
        float zk = 60f;
        while (kickers.Count < 6 && Slope.NextKicker(zk, out var at) && at.z < Course.First + 2f * Course.Cycle) { kickers.Add(at); zk = at.z + 5f; }
        for (int cyc = 0; cyc < 2; cyc++)
            foreach (float pk in Course.ParkKickers)
            {
                float z0 = Course.First + cyc * Course.Cycle + Course.ParkAt + pk + Slope.KickRamp * 1.15f;
                kickers.Add(new Vector3(Slope.Centre(z0), Slope.Height(Slope.Centre(z0), z0), z0));
            }
        foreach (var at in kickers)
            foreach (float v0 in speeds)
                foreach (float h in new[] { -0.5f, 0f, 0.5f })
                    Ride($"seed{seed} kicker z{at.z:0} v{v0} h{h}", at - new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * 30f, h, v0, 5f, null);
        // jump cliffs
        for (int cyc = 0; cyc < 2; cyc++)
            for (int j = 0; j < 2; j++)
                if (Cliffs.JumpOf(cyc, j, out var jc))
                    foreach (float v0 in speeds)
                        foreach (float h in new[] { -0.3f, 0f, 0.3f })
                        {
                            var lip = new Vector3(Slope.Centre(jc.z), 0f, jc.z);
                            Ride($"seed{seed} jumpcliff c{cyc}k{j} v{v0} h{h}", lip - new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * (jc.bench + 15f), h, v0, 7f, null);
                        }
        // rails and boxes: onto each one's start, along it and across it, centred and off to either side
        for (int cyc = 0; cyc < 2; cyc++)
            foreach (var f in Course.FeaturesOf(cyc))
            {
                float fh = Mathf.Atan2(f.Dir.x, f.Dir.z);
                foreach (float v0 in new[] { 6f, 10f, 16f, 24f })
                    foreach (float h in new[] { -0.4f, 0f, 0.4f })
                        foreach (float side in new[] { -1.2f, -0.6f, 0f, 0.6f, 1.2f })
                        {
                            Vector3 lat = new Vector3(f.Dir.z, 0f, -f.Dir.x) * side;
                            Vector3 dir = new Vector3(Mathf.Sin(fh + h), 0f, Mathf.Cos(fh + h));
                            Vector3 from = f.a + lat - dir * 14f;
                            Ride($"seed{seed} {f.kind} {f.id} v{v0} h{h} side{side}", from, fh + h, v0, 4.5f, null);
                        }
            }
        // the open piste: long carving runs across every bump and roller of the first cycle and a half
        for (float z0 = 80f; z0 < Course.First + 1.5f * Course.Cycle; z0 += 220f)
            foreach (float off in new[] { -14f, -5f, 5f, 14f })
                foreach (float v0 in new[] { 15f, 30f })
                    Ride($"seed{seed} piste z{z0:0} off{off} v{v0}", new Vector3(Slope.Centre(z0) + off, 0f, z0), 0f, v0, 10f,
                         (t, rr) => Mathf.Clamp(0.6f * Mathf.Sin(t * 1.2f + off) + (Slope.Centre(rr.Z + 12f) + off - rr.X) * 0.05f - rr.Heading * 0.5f, -1f, 1f));
    }

    /// <summary>
    /// One ride, flagging the first sudden stop (speed along the old way of travel down to 40% of it within 0.2 s, no
    /// crash) and the first turn back up the hill (on the snow, moving up the fall line, or travel reversed in 0.2 s).
    /// </summary>
    static void Ride(string name, Vector3 from, float heading, float v0, float secs, System.Func<float, Rider, float> steer)
    {
        if (only != null && !name.StartsWith(only)) return;
        rides++;
        StartAt(from, heading, v0);
        const int Back = 24;   // 0.2 s
        var hist = new Vector3[Back]; var air = new bool[Back]; var where = new string[Back]; var ys = new float[Back];
        int i = 0; bool flaggedStop = false, flaggedBack = false, flaggedLaunch = false, popped = false;
        string lastEvt = "start";
        System.Action<float> onLand = imp => lastEvt = $"landed {imp:0.0} m/s at {r.LastLandAngle:0} deg";
        System.Action onJump = () => { lastEvt = "popped"; popped = true; };
        System.Action<Course.Feature> onGrind = f => lastEvt = $"onto {f.kind} {f.id}";
        r.Landed += onLand; r.Jumped += onJump; r.GrindStart += onGrind;
        bool wasAir = false, wasGrind = false;
        try
        {
            for (float t = 0f; t < secs; t += RealStep, i++)
            {
                r.Steer = steer != null ? steer(t, r) : Mathf.Clamp((heading - r.Heading) * 1.5f, -1f, 1f);
                Step();
                if (r.Airborne && !wasAir) lastEvt = $"took off at {r.Velocity.magnitude:0.0} m/s";
                if (!r.Grinding && wasGrind) lastEvt = "off the end of the feature";
                wasAir = r.Airborne; wasGrind = r.Grinding;
                if (r.Crashed) { Debug.Log($"STRESS-INFO {name}: crashed '{lastCrash}' at t={t:0.00} z={r.Z:0} x-c={r.X - Slope.Centre(r.Z):0.0} ({lastEvt})"); return; }
                Vector3 v = r.Velocity, vh = new Vector3(v.x, 0f, v.z), p = r.transform.position;
                if (trace != null && name == trace)
                    Debug.Log($"STRESS-TRACE t={t:0.000} z={p.z:0.00} x-c={p.x - Slope.Centre(p.z):0.00} y-snow={p.y - Rider.R - Slope.DrawnHeight(p.x, p.z):0.00} v=({v.x:0.0},{v.y:0.0},{v.z:0.0}) air={r.Airborne} grind={r.Grinding} head={r.Heading:0.00} n=({Slope.Normal(p.x, p.z).x:0.00},{Slope.Normal(p.x, p.z).y:0.00},{Slope.Normal(p.x, p.z).z:0.00}) cut={Course.CrevasseCut(p.x, p.z):0.00} jump={Cliffs.JumpHeight(p.x, p.z):0.00} kick={Slope.Kicker(p.x, p.z):0.00} {lastEvt}");
                string here = $"z={p.z:0.0} x-c={p.x - Slope.Centre(p.z):0.0} sec={Course.At(p.z)} air={r.Airborne} grind={r.Grinding} n.y={Slope.Normal(p.x, p.z).y:0.00} above={r.AboveGround:0.0} v=({v.x:0.0},{v.y:0.0},{v.z:0.0})";
                if (i >= Back)
                {
                    Vector3 old = hist[i % Back];
                    float oh = old.magnitude;
                    if (oh > 5f)
                    {
                        float along = Vector3.Dot(vh, old / oh);
                        if (!flaggedStop && along < 0.4f * oh)
                        {
                            flaggedStop = true; stopped++; Count("stopped");
                            Debug.Log($"STRESS-FLAG {name}: stopped, {oh:0.0} -> {along:0.0} m/s along the way it was going in 0.2 s at t={t:0.00} {here}; 0.2 s before: {where[i % Back]}; last: {lastEvt}");
                        }
                        if (!flaggedBack && along < 0f)
                        {
                            flaggedBack = true; reversed++; Count("reversed");
                            Debug.Log($"STRESS-FLAG {name}: thrown back, travel reversed at t={t:0.00} {here}; 0.2 s before: {where[i % Back]}; last: {lastEvt}");
                        }
                    }
                }
                // launched: shot upward far faster than any pop, rail exit or lip throws you (they leave at under 15 m/s
                // up; a Super Jump's pop is about 16), or flying high over snow that never dropped away that far
                // (the climb over 0.1 s, not one step's speed: a landing puts the board back onto the snow in a step,
                // which reads as tens of m/s for that step alone)
                float climb = i >= 12 ? (p.y - ys[(i - 12) % Back]) / 0.1f : 0f;
                if (!flaggedLaunch && climb > 20f && !popped)
                {
                    flaggedLaunch = true; launchedRides++; Count("shot up");
                    Debug.Log($"STRESS-FLAG {name}: shot up, climbing {climb:0.0} m/s at t={t:0.00} {here}; 0.2 s before: {where[(i + 1) % Back]}; last: {lastEvt}");
                }
                // (over a ridge's canyon or below a deadly cliff, a long way down is the mountain, not a launch)
                if (!flaggedLaunch && r.AboveGround > 40f && !Ridge.InCanyons(p.z) && !Deadly.Below(p.x, p.z))
                {
                    flaggedLaunch = true; launchedRides++; Count("high");
                    Debug.Log($"STRESS-FLAG {name}: {r.AboveGround:0} m over the snow at t={t:0.00} {here}; last: {lastEvt}");
                }
                if (!flaggedBack && !r.Airborne && !r.Grinding && v.z < -2f && !Course.InPipe(p.x, p.z))
                {
                    flaggedBack = true; reversed++; Count("uphill");
                    Debug.Log($"STRESS-FLAG {name}: riding back up the hill vz={v.z:0.0} at t={t:0.00} {here}; 0.2 s before: {where[i % Back]}; last: {lastEvt}");
                }
                hist[i % Back] = vh; air[i % Back] = r.Airborne; where[i % Back] = here; ys[i % Back] = p.y;
            }
        }
        finally { r.Landed -= onLand; r.Jumped -= onJump; r.GrindStart -= onGrind; }
    }
}
