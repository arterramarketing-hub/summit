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
    const float RealStep = 1f / 120f;   // real time per physics step outside a hit-stop (in one, Step says how much)
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static string lastCrash = "";
    static int crashes, launched, fast, rides, stopped, reversed, launchedRides, longFlights;
    static float flightTotal, flightMax;
    static readonly Dictionary<string, int> kinds = new Dictionary<string, int>();
    static int n;
    static readonly string trace = System.Environment.GetEnvironmentVariable("STRESS_TRACE");
    static readonly string only = System.Environment.GetEnvironmentVariable("STRESS_ONLY");
    static readonly string crashOnly = System.Environment.GetEnvironmentVariable("STRESS_CRASH");
    static readonly string watch = System.Environment.GetEnvironmentVariable("STRESS_WATCH");
    static readonly int Repeats = int.TryParse(System.Environment.GetEnvironmentVariable("STRESS_REPEAT"), out int rv) ? rv : 1;
    static bool Skip(string name) => crashOnly != null && !name.StartsWith(crashOnly);

    public static void Run()
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
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.Crash += why => lastCrash = why;
        // $STRESS_RIDE=Skis|Toboggan rides the other rides (the same takeoff, landing and crash code, their own numbers)
        var rideName = System.Environment.GetEnvironmentVariable("STRESS_RIDE");
        if (!string.IsNullOrEmpty(rideName) && System.Enum.TryParse<RideKind>(rideName, out var ride)) { body.SetRide(ride); r.SetRide(ride); }
        Debug.Log($"STRESS riding {r.Ride}");
        bool hitStop = System.Environment.GetEnvironmentVariable("STRESS_HITSTOP") != "0";
        SummitGame.TestHitStops = hitStop;
        int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("STRESS_SEEDS"), out int sv) ? sv : 4;
        try
        {
            for (int seed = 0; seed < seeds; seed++)
            {
                TestBoot.NewMountain(game, 2000 + seed * 131, terrain, props);
                var dress = Object.FindFirstObjectByType<CourseDressing>();
                if (dress != null) { r.ResetAt(Slope.Centre(Course.First), Course.First); dress.Rebuild(); }
                Crashes(seed);
                if (crashOnly == null) { Rides(seed); RockRides(seed); CrevasseRides(seed); LiftRides(seed); }
            }
        }
        catch (System.Exception e) { Debug.LogError("STRESS exception " + e); }
        SummitGame.TestHitStops = false;
        var sb = new System.Text.StringBuilder();
        foreach (var kv in kinds) sb.Append($"{kv.Key}={kv.Value} ");
        Debug.Log($"STRESS summary ({r.Ride}, hit-stop {(hitStop ? "on" : "off")}): {crashes} crashes, {launched} launched (>30 m up at the end), {fast} fast (>80 m/s after 2 s); " +
                  $"{rides} rides, {stopped} stopped dead, {reversed} thrown back up the hill, {launchedRides} launched; crevasses: {crossed} carried across, {fellIn} fell in, {fields} fields checked; lift ramps: {onCable} onto the cable; by kind: {sb}");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Time.timeScale = 1f; Time.fixedDeltaTime = 1f / 120f;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static void Count(string kind) { kinds.TryGetValue(kind, out int c); kinds[kind] = c + 1; }

    /// <summary>One physics step as the game runs it: the hit-stop's clock, then the rider, the body and PhysX, at the step the hit-stop leaves.</summary>
    static float Step()
    {
        // one physics step stands for this much real time: the hit-stop slows time, so the steps come further apart
        float realDt = Time.fixedDeltaTime / Mathf.Max(Time.timeScale, 1e-4f);
        game.TestTickHitStop(realDt);
        float dt = Time.fixedDeltaTime;
        // what OnCollisionEnter does on the phone (batchmode sends no collision messages): the board into an obstacle
        if (!r.Crashed && r.Running)
        {
            r.TestContacts(dt);   // (as Rider.OnCollisionEnter: ridden over, glanced off or crashed into)
        }
        r.Tick(dt); body.PhysicsStep(); Physics.Simulate(dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 2 == 0 || r.Crashed) body.Apply(r.Crashed ? dt : 2f * dt);
        n++;
        return realDt;
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
        if (only != null && !only.StartsWith("crash") && crashOnly == null) return;
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
            // into a crevasse: the widest crack in this cycle, ridden straight at from 14 m up the hill of its middle
            var cvs = Course.CrevassesOf(cyc);
            int widest = -1;
            for (int i = 0; i < cvs.Length; i++) if (widest < 0 || cvs[i].half > cvs[widest].half) widest = i;
            if (widest >= 0)
            {
                var cv = cvs[widest];
                var mid = Course.CrackSpot(cv);
                Free($"seed{seed} crevasse c{cyc} {cv.kind} 70kmh", new Vector3(mid.x, 0f, mid.z - 14f), 0f, 70f / 3.6f, 8f);
            }
        }
        // thrown just before a kicker's lip (a tree or a sign beside it): the body goes off the lip at the board's speed
        float zq = 60f; int kq = 0;
        while (kq < 3 && Slope.NextKicker(zq, out var kat))
        {
            zq = kat.z + 5f; kq++;
            foreach (float kmh in new[] { 120f, 200f })
            for (int rep = 0; rep < Repeats; rep++)
            {
                string name = $"seed{seed} lipcrash z{kat.z:0} {kmh:0}kmh";
                if (Skip(name)) continue;
                if (Repeats > 1) { Random.InitState(rep * 7919 + 13); name += $" rep{rep}"; }
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
        if (Skip(name)) return;
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
        if (Skip(name)) return;
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
        if (Skip(name)) return;
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
        bool hit1 = false, hit2 = false;
        for (float t = 0f; t < 8f; )
        {
            if (!hit1 && t >= 0.6f) { hit1 = true; HitStopNow(0.8f); }   // a tumble into something (RagdollHit)
            if (!hit2 && t >= 1.5f) { hit2 = true; HitStopNow(0.8f); }
            minScale = Mathf.Min(minScale, Time.timeScale);
            gameT += Time.fixedDeltaTime;
            t += Step();
            c = body.RagdollCentre;
            float up = c.y - Slope.DrawnHeight(c.x, c.z);
            if (watch != null && name == watch && Mathf.Repeat(t, 0.05f) < RealStep)
            {
                var parts = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
                string fastest = ""; float fv = 0f;
                foreach (var prb in parts) if (!prb.isKinematic && prb != r.Body && prb.linearVelocity.magnitude > fv) { fv = prb.linearVelocity.magnitude; fastest = $"{prb.name} {prb.linearVelocity} at up {prb.position.y - Slope.DrawnHeight(prb.position.x, prb.position.z):0.0}"; }
                Debug.Log($"STRESS-CTRACE t={t:0.00} c=({c.x - Slope.Centre(c.z):0.0},{c.z:0.0}) up={up:0.0} vel={((Ragdoll)typeof(RiderBody).GetField("ragdoll", NP).GetValue(body)).Velocity} scale={Time.timeScale:0.00} n=({Slope.Normal(c.x, c.z).x:0.00},{Slope.Normal(c.x, c.z).y:0.00},{Slope.Normal(c.x, c.z).z:0.00}) rock={RockFace.IsRock(Slope.Normal(c.x, c.z), c.x, c.z)} fastest {fastest} hitObj={body.RagdollHitObject}");
            }
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
        // hung in the air: at rest, but well off the ground (caught on something, or held up by nothing)
        if (endUp > 1.5f && !down && body.RagdollSpeed < 1f)
        {
            string under = "";
            foreach (var hit in Physics.OverlapSphere(c, 1.2f, ~0, QueryTriggerInteraction.Ignore)) if (hit.attachedRigidbody == null || hit.attachedRigidbody.isKinematic) under += hit.name + " ";
            Count("hung"); Debug.Log($"STRESS-FLAG {name}: body at rest {endUp:0.0} m over the ground at x-c={c.x - Slope.Centre(c.z):0} z={c.z:0}, on: {under}");
        }
        if (body.Dead || down) return;
        // and up again: tap as soon as the game allows, then ride two seconds from where it puts you
        float waited = 0f;
        Vector3 upAt = Vector3.zero;
        for (; waited < 8f && r.Crashed; waited += RealStep) { if (r.CanGetUp) r.RequestUp = true; Step(); if (!r.Crashed) upAt = r.Body.position; }
        // (kept down by the game itself, StayDown: the run's last fall, or the avalanche's: not a glitch)
        if (r.Crashed && !r.StayDown) { Count("can't get up"); Debug.Log($"STRESS-FLAG {name}: still down after 8 s more (hold={r.HoldCrash} body {body.RagdollSpeed:0.0} m/s)"); return; }
        if (r.Crashed) { Debug.Log($"STRESS-INFO {name}: kept down by the game (falls={typeof(SummitGame).GetField("falls", NP).GetValue(game)})"); return; }
        float airT = 0f;
        for (float t = 0f; t < 2f && !r.Crashed; t += RealStep) { r.Steer = 0f; Step(); if (r.Airborne) airT += RealStep; }
        // (riding on into the forest's trees after that is the forest: only a fall from the spot itself is a glitch)
        bool obstacle = lastCrash == "Tree" || lastCrash == "Rock" || lastCrash.StartsWith("Tree") || lastCrash == "Signpost!" || lastCrash == "Bear!";
        if (r.Crashed && !obstacle) { Count("down again"); Debug.Log($"STRESS-FLAG {name}: got up at x-c={upAt.x - Slope.Centre(upAt.z):0} n.y={Slope.Normal(upAt.x, upAt.z).y:0.00} and went straight down again ('{lastCrash}')"); }
        else if (airT > 1.2f && (r.transform.position - upAt).magnitude < 6f) { Count("hovering"); Debug.Log($"STRESS-FLAG {name}: got up at x-c={upAt.x - Slope.Centre(upAt.z):0} n.y={Slope.Normal(upAt.x, upAt.z).y:0.00} and hopped in the air on the spot"); }
    }

    // ---------------------------------------------------------------- rock and crevasses

    /// <summary>Across rock terraces off the piste: rock is ridden (slower, scraping), never a fall by itself.</summary>
    static void RockRides(int seed)
    {
        var spots = new List<Vector3>();
        for (float z = 150f; z < Course.First + 2f * Course.Cycle && spots.Count < 14; z += 23f)
            for (float a = 24f; a < 110f; a += 3f)
                foreach (float sd in new[] { -1f, 1f })
                {
                    float x = Slope.Centre(z) + sd * a;
                    Vector3 nn = Slope.Normal(x, z);
                    if (nn.y < 0.66f || !RockFace.IsRock(nn, x, z) || Course.CrevasseNear(x, z, 3f)) continue;
                    bool far = true; foreach (var q in spots) if ((q - new Vector3(x, 0f, z)).sqrMagnitude < 50f * 50f) far = false;
                    if (far) spots.Add(new Vector3(x, 0f, z));
                }
        foreach (var at in spots)
            foreach (float v0 in new[] { 8f, 16f, 26f })
                foreach (float h in new[] { -0.6f, 0f, 0.6f })
                {
                    string name = $"seed{seed} rock z{at.z:0} x-c{at.x - Slope.Centre(at.z):0} v{v0} h{h}";
                    if (only != null && !name.StartsWith(only)) continue;
                    rides++;
                    Vector3 from = at - new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * 12f;
                    StartAt(from, h, v0);
                    int scrapes = 0, rockSteps = 0; float hop = 0f; Vector3 last = r.transform.position; float still = 0f;
                    System.Action<float> sc = _ => scrapes++;
                    r.Scraped += sc;
                    try
                    {
                        for (float t = 0f; t < 4f && !r.Crashed; t += RealStep)
                        {
                            r.Steer = Mathf.Clamp((h - r.Heading) * 1.5f, -1f, 1f);
                            Step();
                            if (r.OnRock) rockSteps++;
                            if (r.Airborne && r.AboveGround < 1f && r.Speed < 4f) hop += RealStep;
                            still = (r.transform.position - last).magnitude < 0.02f ? still + RealStep : 0f;
                            last = r.transform.position;
                        }
                    }
                    finally { r.Scraped -= sc; }
                    Debug.Log($"STRESS-ROCK {name}: {rockSteps / 120f:0.0}s on rock, {scrapes} scrapes, crashed={r.Crashed} '{(r.Crashed ? lastCrash : "")}', {r.Speed * 3.6f:0} km/h at the end");
                    if (r.Crashed && lastCrash == "Hit the rocks!") { Count("rock crash"); Debug.Log($"STRESS-FLAG {name}: crashed for touching rock"); }
                    if (r.Crashed && lastCrash != "Hit the rocks!") PlayOut(name);   // (a boulder or a crag: a crash by collision, and up again)
                    if (hop > 1f) { Count("hovering"); Debug.Log($"STRESS-FLAG {name}: hopping in the air on the spot for {hop:0.0}s"); }
                    if (still > 1f && !r.Crashed) { Count("stuck"); Debug.Log($"STRESS-FLAG {name}: stuck, not moving for {still:0.0}s"); }
                }
    }

    /// <summary>Across crevasses at a range of speeds and angles (and along one): you fall in only below the snow's level.</summary>
    /// <summary>
    /// Across the glacier's crevasses: each kind, at a spread of speeds with and without a jump at the lip, through its
    /// pinches fast and slow (carried at speed; at a crawl the slot may take you), and a check of every field that a way
    /// down through it exists (FieldPaths).
    /// </summary>
    static void CrevasseRides(int seed)
    {
        var all = Course.Crevasses;
        FieldPaths(seed, all);
        // a sample: the bergschrund, the widest crack of each field, three chevrons and the splays
        var pick = new List<Course.Crevasse>();
        var bestOfField = new Dictionary<int, int>();
        int chev = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c.kind == Course.CrevasseKind.Transverse)
            {
                if (!bestOfField.TryGetValue(c.field, out int j) || all[j].half < c.half) bestOfField[c.field] = i;
            }
            else if (c.kind != Course.CrevasseKind.Chevron || chev++ < 3) pick.Add(c);
        }
        foreach (var kv in bestOfField) pick.Add(all[kv.Value]);
        foreach (var cv in pick)
        {
            var at = Course.CrackSpot(cv);
            // ridden over square-on, down the hill: straight down for a crack that lies across it, slanting in for one
            // that lies at an angle (a chevron), and in from either side for one that runs down it
            bool across = Mathf.Abs(cv.dx) > 0.85f, down = Mathf.Abs(cv.dx) < 0.45f;
            float nx = -cv.dz, nz = cv.dx;
            if (nz < 0f) { nx = -nx; nz = -nz; }
            float square = Mathf.Clamp(Mathf.Atan2(nx, nz), -1.2f, 1.2f);
            foreach (float sd in down ? new[] { -1f, 1f } : new[] { 0f })
                foreach (float v0 in new[] { 8f, 16f, 24f, 32f })
                    foreach (bool pop in new[] { false, true })
                    {
                        float head = across ? 0f : down ? sd * 1.0f : square;
                        var from = new Vector3(at.x - Mathf.Sin(head) * 14f, 0f, at.z - Mathf.Cos(head) * 14f);
                        Gap($"seed{seed} crevasse {cv.kind} #{cv.id} {(across ? "down" : !down ? "square" : sd < 0 ? "fromR" : "fromL")} v{v0}{(pop ? " pop" : "")}", from, head, v0, pop, cv);
                    }
            // its pinches: carried across at speed; at a crawl it may take you
            foreach (var span in new[] { new Vector2(cv.b0, cv.b1), new Vector2(cv.c0, cv.c1) })
            {
                if (span.y <= span.x) continue;
                float u = (span.x + span.y) * 0.5f;
                Course.Opening(cv, u, out float mid);
                var q = cv.Point(u, mid);
                foreach (float v0 in new[] { 4f, 16f })
                    Gap($"seed{seed} crevasse {cv.kind} #{cv.id} pinch v{v0}", new Vector3(q.x, 0f, q.y - 14f), 0f, v0, false, cv, true);
            }
        }
    }

    /// <summary>
    /// Every field of crevasses (and the bergschrund) must leave a way down through it: on a 1 m grid over the piste,
    /// a cell is a wall only over a crack's opening too wide to carry at a normal speed (Course.CarryHalf: a pinch is not
    /// one); from the field's top row to its bottom row there must be a path round the walls.
    /// </summary>
    static void FieldPaths(int seed, Course.Crevasse[] all)
    {
        var groups = new Dictionary<int, Vector2>();   // field (-2: the bergschrund) -> z range
        foreach (var c in all)
        {
            int g = c.kind == Course.CrevasseKind.Bergschrund ? -2 : c.kind == Course.CrevasseKind.Transverse ? c.field : int.MinValue;
            if (g == int.MinValue) continue;
            var zr = groups.TryGetValue(g, out var have) ? have : new Vector2(float.MaxValue, float.MinValue);
            groups[g] = new Vector2(Mathf.Min(zr.x, c.z0), Mathf.Max(zr.y, c.z1));
        }
        foreach (var kv in groups)
        {
            float za = kv.Value.x - 2f, zb = kv.Value.y + 2f;
            int nz = Mathf.CeilToInt(zb - za) + 1, nx = Mathf.FloorToInt(2f * Slope.PisteHalf) - 1;
            var wall = new bool[nz, nx];
            int walls = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float z = za + j, x = Slope.Centre(z) - Slope.PisteHalf + 1f + i;
                    if (Course.CrevasseCut(x, z) <= 0.3f || !Course.CrevasseNear(x, z, 0f, out var c)) continue;
                    float u = c.Along(x, z);
                    if (Course.Opening(c, u, out _) <= Course.CarryHalf) continue;
                    wall[j, i] = true; walls++;
                }
            // flood down from the top row
            var seen = new bool[nz, nx];
            var q = new Queue<Vector2Int>();
            for (int i = 0; i < nx; i++) if (!wall[0, i]) { seen[0, i] = true; q.Enqueue(new Vector2Int(i, 0)); }
            bool through = false;
            while (q.Count > 0 && !through)
            {
                var p = q.Dequeue();
                if (p.y == nz - 1) { through = true; break; }
                foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    var n = p + d;
                    if (n.x < 0 || n.x >= nx || n.y < 0 || n.y >= nz || seen[n.y, n.x] || wall[n.y, n.x]) continue;
                    seen[n.y, n.x] = true; q.Enqueue(n);
                }
            }
            Debug.Log($"STRESS-FIELD seed{seed} {(kv.Key == -2 ? "bergschrund" : $"field {kv.Key}")} z {za:0}..{zb:0}: {walls} m2 too wide to carry, {(through ? "a way through" : "NO WAY THROUGH")}");
            fields++;
            if (!through) { Count("crevasse field walled"); }
        }
    }

    static int fields;

    static int crossed, fellIn, onCable;

    /// <summary>Over each lift's hill and onto its cable, lined up, at and above the speed it needs.</summary>
    static void LiftRides(int seed)
    {
        foreach (var l in LiftLine.Lifts)
        {
            var rp = l.hill;
            if (rp == null) continue;
            Vector3 foot = l.At(rp.s - rp.rise, rp.side * LiftLine.Half, 0f);
            foot.y = Slope.Height(foot.x, foot.z);
            Vector3 flat = l.dir;
            float head = Mathf.Atan2(flat.x, flat.z);
            foreach (float v0 in new[] { rp.needSpeed, rp.needSpeed + 4f, 30f })
            {
                string name = $"seed{seed} lift k{l.k} hill v{v0:0}";
                if (only != null && !name.StartsWith(only)) continue;
                rides++;
                StartAt(foot - flat * 12f, head, v0);
                bool cable = false, flew = false;
                for (float t = 0f; t < 6f && !r.Crashed; t += RealStep)
                {
                    // aim at the hill's foot under the cable (and on along the line once past it)
                    Vector3 p = r.transform.position;
                    float ahead = Vector3.Dot(foot - p, flat);
                    Vector3 aim = ahead > 2f ? foot + flat * 2f : p + flat * 10f;
                    float want = Mathf.Atan2(aim.x - p.x, aim.z - p.z);
                    r.Steer = r.Grinding ? 0f : Mathf.Clamp((want - r.Heading) * 3f - r.YawRate * 0.2f, -1f, 1f);
                    Step();
                    flew |= r.Airborne; cable |= r.OnCable && r.AboveGround > 5f;
                }
                if (cable) onCable++;
                Debug.Log($"STRESS-LIFT {name}: needs {rp.needSpeed:0} m/s, {(cable ? "onto the cable" : flew ? "off the hill, not onto the cable" : "never left the snow")}, crashed={r.Crashed} '{(r.Crashed ? lastCrash : "")}', {r.Speed * 3.6f:0} km/h");
                if (r.Crashed) PlayOut(name);
            }
        }
    }
    static void Gap(string name, Vector3 from, float heading, float v0, bool pop, Course.Crevasse cv, bool pinch = false)
    {
        if (only != null && !name.StartsWith(only)) return;
        rides++;
        StartAt(from, heading, v0);
        bool popped = false, wasOver = false, early = false; float rimGap = 0f;
        System.Action<string> atCrash = why =>
        {
            if (why != "Crevasse!") return;
            Vector3 q = r.Body.position;
            rimGap = (q.y - Rider.R) - Slope.DrawnHeight(q.x, q.z);
            early = rimGap > -Rider.FallInDepth + 0.05f;
        };
        r.Crash += atCrash;
        for (float t = 0f; t < 4f && !r.Crashed; t += RealStep)
        {
            r.Steer = Mathf.Clamp((heading - r.Heading) * 1.5f, -1f, 1f);
            Vector3 p = r.transform.position;
            if (pop && !popped && !r.Airborne && Course.CrevasseNear(p.x, p.z, 1.2f)) { r.QueuePop(0.8f); popped = true; }
            Step();
            p = r.transform.position;
            bool over = Course.CrevasseCut(p.x, p.z) > 0.3f;
            wasOver |= over;
        }
        r.Crash -= atCrash;
        bool fell = r.Crashed && lastCrash == "Crevasse!";
        if (fell) fellIn++; else if (wasOver) crossed++;
        if (fell && pinch && v0 >= 2f * Course.PinchHalf / Course.SkimTime + 1f) { Count("crevasse pinch fall at speed"); Debug.Log($"STRESS-FLAG {name}: fell into a pinch at speed"); }
        else if (fell && pinch) Count("crevasse pinch fall slow");
        Debug.Log($"STRESS-GAP {name}: {(fell ? "fell in" : wasOver ? "carried across" : "never over it")}{(r.Crashed && !fell ? $", crashed '{lastCrash}'" : "")}, {r.Speed * 3.6f:0} km/h");
        if (early) { Count("crevasse early"); Debug.Log($"STRESS-FLAG {name}: fell in while still {rimGap:0.00} m from the snow's level"); }
        if (r.Crashed && !fell) PlayOut(name);
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
