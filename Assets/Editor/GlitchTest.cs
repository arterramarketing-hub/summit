using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Hunts for glitches: rides the halfpipe, kickers and drops, and throws the ragdoll, and reports
/// anything that jumps, jerks or flips. Run: Unity -batchmode -nographics -projectPath . -executeMethod GlitchTest.Run
/// </summary>
public static class GlitchTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static int issues;
    static string lastCrash = "";
    static bool diedOnRock;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        rider.input = null;
        rider.Crash += why => lastCrash = why;
        // every death must be a fall of Rider.DeathFall or more (one rule, wherever it happens)
        body.Killed += fell =>
        {
            if (fell < Rider.DeathFall) Report($"killed by a fall of only {fell:0} m (the rule is {Rider.DeathFall:0} m)");
            Vector3 c = body.RagdollCentre;
            diedOnRock = RockFace.IsRock(Slope.Normal(c.x, c.z), c.x, c.z);   // (killed on rock: nothing to bury it in, by design)
        };
        // no course dressing in the rides (signs and flags can now be hit): it was built for the boot mountain, not
        // for the seeds ridden here; BreakableRides builds it for its own rides
        var dressing = Object.FindFirstObjectByType<CourseDressing>();
        if (dressing != null) { var tgt = dressing.target; dressing.target = null; dressing.Rebuild(); dressing.target = tgt; }
        try
        {
            int onlySeed = int.TryParse(System.Environment.GetEnvironmentVariable("GLITCH_SEED"), out int os) ? os : -1;   // (one mountain only, to chase a report)
            for (int seed = 0; seed < 4; seed++)
            {
                if (onlySeed >= 0 && seed != onlySeed) continue;
                TestBoot.NewMountain(game, 1000 + seed * 77, terrain, props);
                // the halfpipe: weave wall to wall at different speeds
                if (traceEdge) { RidgeRides(seed, rider, body, terrain, props); break; }
                foreach (float v0 in new[] { 14f, 22f, 30f })
                    Ride($"seed{seed} pipe v{v0}", rider, body, terrain, props, Course.First + Course.PipeAt + 40f, 0f, v0, 7f,
                         (t, r) => Mathf.Sin(t * 1.3f) * 0.9f);
                // kickers and drops: straight through, and carving, from a few start points
                foreach (float z0 in new[] { 120f, 260f, 640f, 1000f })
                    foreach (float steerAmp in new[] { 0f, 0.6f })
                        Ride($"seed{seed} jumps z{z0} amp{steerAmp}", rider, body, terrain, props, z0, 0f, 24f, 8f,
                             (t, r) => Mathf.Clamp(steerAmp * Mathf.Sin(t * 1.1f) + (Slope.Centre(r.Z + 12f) - r.transform.position.x) * 0.05f - r.Heading * 0.5f, -1f, 1f));
                // ridges: hold the crest at different speeds (it must never throw you off or shake you), weave on
                // it, then ride off the edge on purpose (a tumble down the cliff, and up again below it)
                RidgeRides(seed, rider, body, terrain, props);
                // cliffs: off every jump cliff at a crawl, a cruise and flat out (proper air, never a crash), and over
                // the deadly one's edge on purpose (it must kill), and past it on the piste (it must not)
                CliffRides(seed, rider, body, terrain, props);
                // Super Jump: pop every couple of seconds down the open piste and through the kickers
                foreach (float z0 in new[] { 260f, 640f, 1000f })
                {
                    int pops = 0, hard = 0;
                    System.Action<string> rough = why => { if (why == "Hard landing") hard++; };
                    rider.RoughLanding += rough;
                    Ride($"seed{seed} superjump z{z0}", rider, body, terrain, props, z0, 0f, 22f, 10f, (t, r) =>
                    {
                        if (r.SuperJumpTime <= 0f) r.AddSuperJump(60f);
                        if (!r.Airborne && t > 0.5f + pops * 2.2f) { r.QueuePop(0.8f); pops++; }
                        return Mathf.Clamp((Slope.Centre(r.Z + 12f) - r.transform.position.x) * 0.05f - r.Heading * 0.5f, -1f, 1f);
                    });
                    rider.RoughLanding -= rough;
                    Debug.Log($"GLITCH-INFO seed{seed} superjump z{z0}: {pops} pops, {hard} hard landings, crashed={rider.Crashed} {lastCrash} at z={rider.Z:0} x-c={rider.transform.position.x - Slope.Centre(rider.Z):0.0}");
                    // falling into a crevasse or off a ridge is not a jump glitch, nor is coming down before the trick came
                    // round (an upside-down or sideways landing crashes: see Rider.BadLanding), nor landing off the piste
                    // into a tree (a tree that snaps is found by a query, TreeBreak; batchmode never fires the collision)
                    if (rider.Crashed && !Fell(lastCrash) && lastCrash != "Landed upside down!" && lastCrash != "Landed sideways!" && lastCrash != "Tree snapped!") Report($"seed{seed} superjump z{z0}: crashed ({lastCrash})");
                }
                // Invincible: straight down through the forest beside the piste, which would otherwise be a crash
                foreach (float side in new[] { -1f, 1f })
                {
                    int smashed = 0;
                    System.Action<Obstacle, Vector3, Vector3> count = (o, at, v) => smashed++;
                    rider.Smashed += count;
                    Ride($"seed{seed} invincible side{side}", rider, body, terrain, props, 400f + seed * 60f, side * (Slope.PisteHalf + 10f), 18f, 8f, (t, r) =>
                    {
                        if (r.InvincibleTime <= 0f) r.AddInvincible(60f);
                        return Mathf.Clamp(-r.Heading * 0.6f, -1f, 1f);
                    });
                    rider.Smashed -= count;
                    Debug.Log($"GLITCH-INFO seed{seed} invincible side{side}: smashed {smashed}, speed at end {rider.Speed * 3.6f:0} km/h, crashed={rider.Crashed} {lastCrash} at z={rider.Z:0} x-c={rider.transform.position.x - Slope.Centre(rider.Z):0.0}");
                    if (rider.Crashed && !Fell(lastCrash)) Report($"seed{seed} invincible side{side}: crashed while invincible ({lastCrash})");   // it smashes what you hit, it does not stop you falling
                }
                // crashes: throw the ragdoll at different speeds
                foreach (float v0 in new[] { 8f, 18f, 30f })
                    Crash($"seed{seed} crash v{v0}", rider, body, terrain, props, 300f + seed * 40f, v0);
                // and steered while thrown (a controlled fall): hard one way, and wagging side to side
                foreach (float v0 in new[] { 8f, 18f, 30f })
                {
                    Crash($"seed{seed} crash v{v0} steer+1", rider, body, terrain, props, 300f + seed * 40f, v0, t => 1f);
                    Crash($"seed{seed} crash v{v0} wag", rider, body, terrain, props, 300f + seed * 40f, v0, t => Mathf.Sign(Mathf.Sin(t * 4f)));
                }
                // signs and flags: down the line of flags on the start lip, and into section signs
                BreakableRides(seed, game, rider, body, terrain, props);
            }
        }
        catch (System.Exception e) { Debug.LogError("GLITCH exception " + e); issues++; }
        Debug.Log($"GLITCH done: {issues} issue(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    /// <summary>One jump-cliff ride traced step by step ($TRACE_SEED, cycle $TRACE_C, jump $TRACE_K, speed $TRACE_V).</summary>
    public static void Trace()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        rider.input = null;
        string E(string k, string d) => System.Environment.GetEnvironmentVariable(k) ?? d;
        TestBoot.NewMountain(game, int.Parse(E("TRACE_SEED", "1154")), terrain, props);
        Cliffs.JumpOf(int.Parse(E("TRACE_C", "1")), int.Parse(E("TRACE_K", "1")), out var j);
        float v0 = float.Parse(E("TRACE_V", "10"));
        int n = 0;
        rider.Crash += why => Debug.Log("TRACE crash " + why);
        Ride("trace", rider, body, terrain, props, j.Start - 25f, 0f, v0, 4f,
             (t, rr) => Mathf.Clamp((Slope.Centre(rr.Z + 12f) - rr.transform.position.x) * 0.05f - rr.Heading * 0.5f, -1f, 1f),
             (t, rr) =>
             {
                 Vector3 p = rr.transform.position, v = rr.Velocity;
                 if (t > 1f && rr.Speed < 8f && n % 30 == 0)
                     foreach (var c in Physics.OverlapSphere(p, 3f, ~0, QueryTriggerInteraction.Collide))
                         Debug.Log($"TRACE near: {c.name} ({c.GetType().Name}) at {c.bounds.center} trigger={c.isTrigger} parent={(c.transform.parent != null ? c.transform.parent.name : "-")}");
                 if (n++ % 3 == 0) Debug.Log($"TRACE t={t:0.000} z={p.z:0.00} x-c={p.x - Slope.Centre(p.z):0.00} y-ground={p.y - Slope.DrawnHeight(p.x, p.z) - Rider.R:0.00} v=({v.x:0.0},{v.y:0.0},{v.z:0.0}) air={rr.Airborne} n.y={Slope.Normal(p.x, p.z).y:0.00} fine={Ridge.Fine(Mathf.FloorToInt(p.x / Slope.Quad), Mathf.FloorToInt(p.z / Slope.Quad))}");
             }, true);
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    /// <summary>What else is near a ragdoll part (a tree, a rock...), besides the body, the board and the snow slabs.</summary>
    static string Near(Vector3 p, System.Collections.Generic.List<Rigidbody> own)
    {
        string s = "";
        foreach (var c in Physics.OverlapSphere(p, 1.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.attachedRigidbody != null && (own.Contains(c.attachedRigidbody) || c.attachedRigidbody.isKinematic)) continue;
            s += c.name + " ";
        }
        return s == "" ? "nothing" : s;
    }

    static void Report(string what) { issues++; if (issues < 2000) Debug.Log("GLITCH " + what); }

    /// <summary>Falling into a crevasse, onto rock or off a cliff is the mountain, not a glitch in a jump or a smash.</summary>
    static bool Fell(string why) => why == "Crevasse!" || why == "Hit the rocks!" || why == "Cliffed!";

    /// <summary>One physics step of everything a fall needs.</summary>
    static void Step(Rider r, RiderBody body, SlopeStreamer terrain, Forest props, int n)
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
    }

    /// <summary>After a crash, play the fall out until it kills, the body rests, or time runs out. Returns how deep it lies.</summary>
    static float PlayOut(string name, Rider r, RiderBody body, SlopeStreamer terrain, Forest props, ref int n, float secs, out float tDead, out float sankRock)
    {
        tDead = -1f; sankRock = 0f;
        float rest = 0f;
        for (float t = 0f; t < secs; t += Dt, n++)
        {
            Step(r, body, terrain, props, n);
            Vector3 c = body.RagdollCentre;
            if (float.IsNaN(c.x)) { Report($"{name}: ragdoll NaN"); return 0f; }
            if (!body.Dead && RockFace.IsRock(Slope.Normal(c.x, c.z), c.x, c.z)) sankRock = Mathf.Max(sankRock, Slope.DrawnHeight(c.x, c.z) - c.y);
            if (body.Dead && tDead < 0f) tDead = t;
            if (body.Dead && !body.RagdollMoving) { rest += Dt; if (rest > 1.5f) break; }
        }
        return body.RagdollBelowSnow;
    }

    static void CliffRides(int seed, Rider r, RiderBody body, SlopeStreamer terrain, Forest props)
    {
        for (int cyc = 0; cyc < 2; cyc++)
        {
            for (int k = 0; k < 2; k++)
            {
                if (!Cliffs.JumpOf(cyc, k, out var j)) continue;
                foreach (float v0 in new[] { 10f, 20f, 32f })
                {
                    string name = $"seed{seed} jumpcliff c{cyc}k{k} drop{j.drop:0.0} v{v0}";
                    float peak = 0f, air = 0f, fastest = 0f, peakZ = 0f, peakX = 0f; int hard = 0; bool crashed = false;
                    System.Action<string> rough = why => { if (why == "Hard landing") hard++; };
                    System.Action<float> landed = imp => fastest = Mathf.Max(fastest, imp);
                    r.RoughLanding += rough; r.Landed += landed;
                    Ride(name, r, body, terrain, props, j.Start - 25f, 0f, v0, Mathf.Min(9f, (j.End - j.Start + 25f) / v0 + 2.5f),
                         (t, rr) => Mathf.Clamp((Slope.Centre(rr.Z + 12f) - rr.transform.position.x) * 0.05f - rr.Heading * 0.5f, -1f, 1f),
                         (t, rr) => { if (rr.AboveGround > peak) { peak = rr.AboveGround; peakZ = rr.Z; peakX = rr.transform.position.x - Slope.Centre(rr.Z); } if (rr.Airborne) air = Mathf.Max(air, rr.AirTime); crashed |= rr.Crashed; }, true);
                    r.RoughLanding -= rough; r.Landed -= landed;
                    Debug.Log($"GLITCH-INFO {name}: {air:0.00}s of air, {peak:0.0} m up at most, hardest landing {fastest:0.0} m/s, {hard} hard, crashed={crashed} {(crashed ? lastCrash : "")} at z={r.Z:0} (lip {j.z:0})");
                    if (crashed) Report($"{name}: crashed ({lastCrash}) at z={r.Z:0}");
                    if (peak > j.rise + 6f)
                    {
                        // far higher than the lip could throw you: show the ground along the line
                        var sb = new System.Text.StringBuilder();
                        for (float zz = j.Start - 30f; zz < j.End + 20f; zz += 3f)
                        {
                            float xx = Slope.Centre(zz) + peakX;
                            sb.Append($"{zz:0}:{Slope.Height(xx, zz) - Slope.FallLine(zz):0.0}/{Cliffs.JumpHeight(xx, zz):0.0}/{Ridge.Depth(zz):0.00} ");
                        }
                        Report($"{name}: {peak:0} m up at z={peakZ:0} x-c={peakX:0.0}; ground along it (z:height off the line/jump/canyon) {sb}");
                    }
                    if (peak < 0.5f * j.drop) Report($"{name}: no air off the lip (peak {peak:0.0} m)");
                    if (r.Z < j.End) Report($"{name}: never got down the landing (z={r.Z:0}, it ends at {j.End:0})");
                }
            }
            if (!Cliffs.WallOf(cyc, out var w)) continue;
            // straight down the piste past it: nothing happens
            // (between the crevasses and the wall: the edge of the piste on the wall's side)
            float lane = w.side * (w.edge - 7f);
            Ride($"seed{seed} wall c{cyc} piste", r, body, terrain, props, w.z0 - 12f, lane, 22f, 6f,
                 (t, rr) => Mathf.Clamp((Slope.Centre(rr.Z + 12f) + lane - rr.transform.position.x) * 0.05f - rr.Heading * 0.5f, -1f, 1f), null, true);
            if (r.Crashed && lastCrash != "Crevasse!") Report($"seed{seed} wall c{cyc} piste: crashed ({lastCrash}) riding past the deadly cliff");
            // and over its edge: on the board until it hits something, then dead and buried
            foreach (float at in new[] { 0.3f, 0.6f })
            {
                string name = $"seed{seed} wall c{cyc} over{at:0.0} h{w.height:0}";
                float z = Mathf.Lerp(w.z1, w.z1 + 60f, at) - 25f, x0 = Slope.Centre(z) + w.side * (w.edge - 7f);
                r.ResetAt(x0, z); body.ResetVisual(); r.Begin();
                r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z)).normalized * 20f;
                terrain.Prewarm(); props.Prewarm();
                int n = 0; float midAir = -1f;
                for (float t = 0f; t < 8f && !r.Crashed; t += Dt, n++)
                {
                    r.Steer = Mathf.Clamp(w.side * 0.9f - r.Heading * 0.3f, -1f, 1f);
                    Step(r, body, terrain, props, n);
                    if (r.Crashed) midAir = r.transform.position.y - Slope.DrawnHeight(r.transform.position.x, r.Z) - Rider.R;
                }
                // (a wall shorter than Rider.DeathFall is a hard landing or a crash you get up from: it may be ridden out)
                if (!r.Crashed)
                {
                    float offW = (r.transform.position.x - Slope.Centre(r.Z)) * w.side;
                    if (offW < w.edge) Report($"{name}: rode on without going over (z={r.Z:0} off={offW:0.0})");
                    else Debug.Log($"GLITCH-INFO {name}: went over the {w.height:0} m wall and rode on (off={offW:0.0})");
                    continue;
                }
                string why = lastCrash;
                float buried = PlayOut(name, r, body, terrain, props, ref n, 16f, out float tDead, out float sank);
                Debug.Log($"GLITCH-INFO {name}: '{why}' {midAir:0.00} m off the ground, dead={body.Dead} after {tDead:0.0}s, lies {buried:0.00} m under the snow");
                if (midAir > 1.2f) Report($"{name}: fell off the board in mid-air ({midAir:0.0} m up)");
                if (!body.Dead && w.height >= Rider.DeathFall + 5f) Report($"{name}: survived going over a {w.height:0} m cliff ('{why}')");
                // (dead on the rocks at the wall's foot is a death too: only on snow does the body go under)
                else if (body.Dead && buried < 0.6f && !RockFace.IsRock(Slope.Normal(body.RagdollCentre.x, body.RagdollCentre.z), body.RagdollCentre.x, body.RagdollCentre.z)) Report($"{name}: dead but not buried ({buried:0.00} m)");
                if (sank > 0.6f) Report($"{name}: ragdoll sank {sank:0.00} m into the rock");
            }
        }
    }

    static void Ride(string name, Rider r, RiderBody body, SlopeStreamer terrain, Forest props, float z, float dx, float v0, float secs, System.Func<float, Rider, float> steer,
                     System.Action<float, Rider> check = null, bool alongSlope = false)
    {
        r.ResetAt(Slope.Centre(z) + dx, z); body.ResetVisual(); r.Begin();
        // (straight ahead and level is a jerk of its own on the first step: alongSlope starts on the snow's own line)
        r.Body.linearVelocity = alongSlope ? Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z) + dx, z)).normalized * v0 : Vector3.forward * v0;
        terrain.Prewarm(); props.Prewarm();
        Vector3 p1 = r.transform.position, p0 = p1, vprev = r.Body.linearVelocity;
        float h0 = r.Heading; bool wasAir = false;
        int n = 0;
        bool popped = false;   // a jump's pop is a deliberate kick, not a jerk
        System.Action onPop = () => popped = true;
        r.Jumped += onPop;
        try {
        for (float t = 0f; t < secs; t += Dt, n++)
        {
            r.Steer = steer(t, r);
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 4 == 0) body.Apply(4 * Dt);
            Vector3 p = r.Body.position, v = r.Body.linearVelocity;
            if (float.IsNaN(p.x) || float.IsNaN(v.x)) { Report($"{name}: NaN at t={t:0.00}"); return; }
            check?.Invoke(t, r);
            if (r.Crashed) return;   // a crash is not a glitch here
            // a jerk: velocity changing far faster than gravity, landings or grip could manage
            float acc = (v - vprev).magnitude / Dt;
            bool landing = wasAir && !r.Airborne;
            if (acc > 400f && !landing && !popped) Report($"{name}: jerk {acc:0} m/s^2 at t={t:0.00} z={p.z:0} x-c={p.x - Slope.Centre(p.z):0.0} air={r.Airborne} pipe={Course.InPipe(p.x, p.z)} v={v} prev={vprev}");
            // a flip: the board turning more than 60 degrees in one step
            float dh = Mathf.Abs(Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, r.Heading * Mathf.Rad2Deg));
            if (dh > 60f) Report($"{name}: heading jumped {dh:0} deg at t={t:0.00} z={p.z:0} air={r.Airborne} landing={landing} pipe={Course.InPipe(p.x, p.z)}");
            // backwards: moving up the hill while on the snow outside the pipe
            if (!r.Airborne && v.z < -3f && !Course.InPipe(p.x, p.z)) Report($"{name}: travelling backwards vz={v.z:0.0} at t={t:0.00} z={p.z:0}");
            // board facing against travel
            if (r.Speed > 6f && !r.Airborne)
            {
                float rel = Mathf.Abs(Mathf.DeltaAngle(r.Heading * Mathf.Rad2Deg, r.TravelHeading * Mathf.Rad2Deg));
                if (rel > 100f) Report($"{name}: board facing backwards ({rel:0} deg off travel) at t={t:0.00} z={p.z:0} pipe={Course.InPipe(p.x, p.z)}");
            }
            // a teleport
            if ((p - p1).magnitude > v.magnitude * Dt * 2f + 0.5f) Report($"{name}: jumped {(p - p1).magnitude:0.0} m in a step at t={t:0.00}");
            p0 = p1; p1 = p; vprev = v; h0 = r.Heading; wasAir = r.Airborne; popped = false;
        }
        } finally { r.Jumped -= onPop; }
    }

    /// <summary>
    /// Ride through the flags along the start lip (slow enough to bend them, fast enough to snap them), and into
    /// section signs: at a speed you ride through, at one that should knock you down, and invincible. The course
    /// dressing is built for these rides only, and taken down again after, so the other rides are as before.
    /// </summary>
    static void BreakableRides(int seed, SummitGame game, Rider r, RiderBody body, SlopeStreamer terrain, Forest props)
    {
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var breaks = (Breakables)typeof(SummitGame).GetField("breaks", NP).GetValue(game);
        if (dress == null || breaks == null) { Report($"seed{seed} breakables: no course dressing or Breakables"); return; }
        string ride = ""; float rideT = 0f; int downs = 0;
        System.Action<Breakable, bool, bool, bool> onBroke = (b, crashed, inv, thrown) =>
        {
            downs++;
            Debug.Log($"GLITCH-INFO {ride}: {b.kind} {b.name} down at t={rideT:0.00} z={b.transform.position.z:0} {r.Speed * 3.6f:0} km/h crashed={crashed} invincible={inv} thrown={thrown}");
        };
        breaks.Broke += onBroke;
        try
        {
            typeof(SummitGame).GetMethod("RebuildGate", NP).Invoke(game, null);
            float cx = Slope.Centre(Slope.PadLip);
            foreach (float v0 in new[] { 11f, 20f })
            {
                ride = $"seed{seed} lip flags v{v0}"; downs = 0; Breakable.RestoreAll();
                float x = cx + Slope.PadHalf - 1f, z0 = Slope.PadLip - 42f;
                int before = issues;
                Ride(ride, r, body, terrain, props, z0, x - Slope.Centre(z0), v0, 3.2f,
                     (t, rr) => { rideT = t; return Mathf.Clamp((x - rr.transform.position.x) * 0.8f - rr.Heading * 1.5f, -1f, 1f); }, null, true);
                Debug.Log($"GLITCH-INFO {ride}: {downs} down, {r.Speed * 3.6f:0} km/h at the end, crashed={r.Crashed} {lastCrash}, {issues - before} issue(s) in this ride");
                if (r.Crashed) Report($"{ride}: crashed on the flags ({lastCrash})");
            }
            // the section signs of the first two cycles
            r.ResetAt(Slope.Centre(Course.First), Course.First);
            dress.Rebuild();
            var signs = new System.Collections.Generic.List<Breakable>();
            foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None)) if (b.name == "SectionSign") signs.Add(b);
            signs.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));
            var cases = new[] { (v: 17f, inv: false, name: "through"), (v: 30f, inv: false, name: "hard"), (v: 30f, inv: true, name: "invincible") };
            for (int i = 0; i < cases.Length && signs.Count > 0; i++)
            {
                var sign = signs[0]; var cs = cases[i];   // (the same sign each time: each ride puts it back up first)
                Vector3 c = sign.transform.position;
                float x = c.x - Mathf.Sign(c.x - Slope.Centre(c.z)) * 2.6f;   // its inner post
                ride = $"seed{seed} sign {cs.name} v{cs.v}"; downs = 0; Breakable.RestoreAll();
                float vIn = 0f; bool overIt = false; string passing = "";
                int before = issues;
                float z0 = c.z - (cs.v > 20f ? 8f : 16f);   // (fast: start close, or a few centimetres of drift misses a post)
                // (short: through the sign and out of its wobble, not on into the park or the forest beyond)
                Ride(ride, r, body, terrain, props, z0, x - Slope.Centre(z0), cs.v, cs.v > 20f ? 1.4f : 2.1f, (t, rr) =>
                {
                    rideT = t;
                    if (cs.inv && rr.InvincibleTime <= 0f) rr.AddInvincible(60f);
                    if (!sign.Down) vIn = rr.Speed;
                    if (!sign.Down && Mathf.Abs(rr.Z - c.z) < 1f && rr.AboveGround > 1.5f) overIt = true;   // flew over it
                    if (passing == "" && rr.Z > c.z) passing = $"passed {rr.transform.position.x - x:0.00} m off the post, {rr.AboveGround:0.0} m up, air={rr.Airborne}";
                    return Mathf.Clamp((x - rr.transform.position.x) * 0.8f - rr.Heading * 1.5f, -1f, 1f);
                }, null, true);
                // (missing the post at speed is the test's aim, not a glitch: only crashing while invincible is one)
                Debug.Log($"GLITCH-INFO {ride}: hit={sign.Down}{(overIt ? " (flew over it)" : "")} {(sign.Down ? "" : passing)} in {vIn * 3.6f:0} km/h, out {r.Speed * 3.6f:0} km/h, crashed={r.Crashed} {(r.Crashed ? lastCrash : "")}, {issues - before} issue(s) in this ride");
                if (r.Crashed && cs.inv && lastCrash != "Crevasse!" && lastCrash != "Off the ridge!") Report($"{ride}: crashed while invincible ({lastCrash})");
            }
        }
        finally
        {
            breaks.Broke -= onBroke;
            Breakable.RestoreAll();
            // take the dressing down again (with no target it builds nothing back)
            var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt;
        }
    }

    /// <summary>Steer for a point a little ahead on the crest's centre line, offset sideways by dx.</summary>
    static float FollowCrest(Rider r, float dx)
    {
        Vector3 p = r.transform.position;
        float look = Mathf.Max(7f, r.Speed * 0.5f);
        float want = Mathf.Atan2(Slope.Centre(r.Z + look) + dx - p.x, look);
        return Mathf.Clamp((want - r.Heading) * 4f - r.YawRate * 0.25f, -1f, 1f);
    }

    static void RidgeRides(int seed, Rider r, RiderBody body, SlopeStreamer terrain, Forest props)
    {
        // the first two stretches built (each a ridge, a valley or a ledge, as the mountain drew them)
        for (int cyc = 0, done = 0; cyc < 4 && done < 2; cyc++)
            if (Ridge.Built(cyc)) { RidgeRides(seed, cyc, r, body, terrain, props); done++; }
    }

    static void RidgeRides(int seed, int cyc, Rider r, RiderBody body, SlopeStreamer terrain, Forest props)
    {
        float zr = Course.First + cyc * Course.Cycle + Ridge.At;
        Debug.Log($"GLITCH-INFO seed{seed} ridge cycle {cyc} from z={zr:0} form={Ridge.FormOf(cyc)} left={Ridge.SideOf(cyc, -1f)} right={Ridge.SideOf(cyc, 1f)}");
        // on the crest, a fall or a launch is a glitch (a jump off a roll-over is not: there are none on a ridge)
        float maxOff = 0f, lastT = 0f; bool fell = false;
        System.Action<float, Rider> onCrest(string name) => (t, rr) =>
        {
            Vector3 p = rr.transform.position;
            float off = Mathf.Abs(p.x - Slope.Centre(p.z));
            bool crest = Ridge.Weight(p.z) > 0.5f && off < Ridge.HalfWidth(p.z);
            if (Ridge.Weight(p.z) > 0.5f) maxOff = Mathf.Max(maxOff, off);
            lastT = t; fell = rr.Crashed;
            if (rr.Crashed && crest) Report($"{name}: crashed on the crest at t={t:0.00} z={p.z:0} off={off:0.0}");
            if (rr.Airborne && crest && rr.AboveGround > 0.3f) Report($"{name}: launched off the crest at t={t:0.00} z={p.z:0} off={off:0.0} above={rr.AboveGround:0.00}");
        };
        void Done(string name) { Debug.Log($"GLITCH-INFO {name}: {(fell ? "fell off" : "held the crest")} for {lastT:0.0}s, furthest {maxOff:0.0} m off the middle at {r.Speed * 3.6f:0} km/h"); maxOff = 0f; fell = false; }
        // (along the stretch and a little past its end, at about the speed riders reach on it: the open mountain
        // beyond is ridden in every other test)
        float secs = Mathf.Min(12f, (Ridge.EndOf(cyc) - Ridge.At + 20f) / 33f);
        foreach (float v0 in new[] { 16f, 24f, 32f })
        {
            Ride($"seed{seed} ridge v{v0}", r, body, terrain, props, zr + 20f, 0f, v0, secs, (t, rr) => FollowCrest(rr, 0f), onCrest($"seed{seed} ridge v{v0}"), true);
            Done($"seed{seed} ridge v{v0}");
        }
        // weave across the crest, a metre and a half either side of the middle
        Ride($"seed{seed} ridge weave", r, body, terrain, props, zr + 20f, 0f, 20f, secs,
             (t, rr) => FollowCrest(rr, 1.5f * Mathf.Sin(t * 1.4f)), onCrest($"seed{seed} ridge weave"), true);
        Done($"seed{seed} ridge weave");
        // and off the edge on each side (a cliff, or the long snow slope of a one-sided ridge)
        for (int s = -1; s <= 1; s += 2)
            OffTheEdge($"seed{seed} ridge off {(s < 0 ? "left" : "right")}", r, body, terrain, props, zr + Ridge.RampIn + 20f, s);
    }

    /// <summary>
    /// Ride the crest and steer off an edge. Over a cliff: still on the board until it hits the rock (never in
    /// mid-air), then a tumble down the wall, and from its full height, dead and buried in the canyon's snow. Over
    /// the snow side of a one-sided ridge: ridden all the way down into the canyon.
    /// </summary>
    static readonly bool traceEdge = System.Environment.GetEnvironmentVariable("GLITCH_EDGE") == "1";
    static void OffTheEdge(string name, Rider r, RiderBody body, SlopeStreamer terrain, Forest props, float z, float side)
    {
        var kind = Ridge.SideAt(z, side);
        bool cliff = kind == Ridge.Side.Cliff || kind == Ridge.Side.Wall;   // (into a valley's wall, as over a cliff)
        r.ResetAt(Slope.Centre(z), z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z), z)).normalized * 24f;
        terrain.Prewarm(); props.Prewarm();
        int n = 0; float midAir = -1f, airT = 0f, t = 0f;
        for (; t < (cliff ? 14f : 9f) && !r.Crashed; t += Dt, n++)
        {
            // over the edge, or diagonally down the snow side toward the canyon floor
            r.Steer = t < 1.5f ? FollowCrest(r, 0f) : cliff ? side * 0.8f : Mathf.Clamp((side * 0.75f - r.Heading) * 2f, -1f, 1f);
            Step(r, body, terrain, props, n);
            if (traceEdge && n % 20 == 0) { var q = r.transform.position; Debug.Log($"GLITCH-EDGE {name} t={t:0.00} z={q.z:0.0} off={q.x - Slope.Centre(q.z):0.0} v={r.Velocity} speed={r.Speed:0.0} head={r.Heading:0.00} steer={r.Steer:0.00} air={r.Airborne} n.y={Slope.Normal(q.x, q.z).y:0.00}"); }
            if (r.Airborne) airT += Dt;
            if (r.Crashed) midAir = r.transform.position.y - Slope.DrawnHeight(r.transform.position.x, r.Z) - Rider.R;
        }
        Vector3 at = r.transform.position;
        float below = Ridge.Top(at.z) - at.y, off = at.x - Slope.Centre(at.z);
        if (!cliff)
        {
            Debug.Log($"GLITCH-INFO {name} ({kind} side): after {t:0.0}s at off={off:0.0}, {below:0} m below the crest, {airT:0.0}s in the air, crashed={r.Crashed} {(r.Crashed ? lastCrash : "")}");
            // (beside a plain side the rough is the open mountain, rock and all: a crash there is the mountain's)
            // (the open mountain beyond, rock, trees and all, is the mountain's: only the slope itself must ride clean)
            if (r.Crashed && !((kind == Ridge.Side.Plain || kind == Ridge.Side.Bank) && Fell(lastCrash)) && !lastCrash.StartsWith("Tree")) Report($"{name}: crashed ({lastCrash}) riding down the snow side");
            else if (Mathf.Abs(off) < Ridge.HalfWidth(at.z) + 15f) Report($"{name}: never got off the crest down the snow side (off={off:0.0})");
            return;
        }
        if (kind == Ridge.Side.Wall)
        {
            // into the wall: rock is ridden now (no fall for touching it); if it does throw you, a tumble back down to the
            // floor, and up again there
            if (!r.Crashed) { Debug.Log($"GLITCH-INFO {name} (wall side): rode into the valley wall without a fall, at off={off:0.0}"); return; }
            string hit = lastCrash;
            float tc = 0f;
            for (; tc < 20f && r.Crashed; tc += Dt, n++)
            {
                if (!r.HoldCrash && tc > 1f) r.RequestUp = true;
                Step(r, body, terrain, props, n);
            }
            Vector3 up = r.transform.position;
            float upOff = up.x - Slope.Centre(up.z);
            Debug.Log($"GLITCH-INFO {name} (wall side): '{hit}' {midAir:0.00} m off the ground, dead={body.Dead}, up after {tc:0.0}s at off={upOff:0.0} (floor half {Ridge.HalfWidth(up.z):0.0})");
            if (midAir > 1.2f) Report($"{name}: fell off the board in mid-air ({midAir:0.0} m up)");
            if (body.Dead) Report($"{name}: killed riding into the valley wall");
            else if (r.Crashed) Report($"{name}: never got up (20 s)");
            else if (Mathf.Abs(upOff) > Ridge.HalfWidth(up.z) + 1f) Report($"{name}: got up on the wall at off={upOff:0.0}, not on the floor");
            return;
        }
        // (rock is rideable: a ledge part way down can stop the fall, and the rest of the cliff be ridden down; the rule
        // that matters is that a fall of Rider.DeathFall kills, checked by Killed above)
        if (!r.Crashed) { Debug.Log($"GLITCH-INFO {name}: rode off the edge without a fall (14 s) at z={r.Z:0} off={off:0.0} {below:0} m below the crest air={r.Airborne} above={r.AboveGround:0.0} n.y={Slope.Normal(at.x, at.z).y:0.00}"); return; }
        string why = lastCrash;
        float buried = PlayOut(name, r, body, terrain, props, ref n, 30f, out float tDead, out float sank);
        Vector3 c = body.RagdollCentre;
        Debug.Log($"GLITCH-INFO {name}: '{why}' after {t:0.0}s, {midAir:0.00} m off the ground after {airT:0.00}s in the air, dead={body.Dead} after {tDead:0.0}s, " +
                  $"lies {Ridge.Top(c.z) - c.y:0} m below the crest and {buried:0.00} m under the snow");
        if (!Fell(why)) Report($"{name}: fell for '{why}', not onto the rock (or the canyon floor)");
        if (midAir > 1.2f) Report($"{name}: fell off the board in mid-air ({midAir:0.0} m up)");
        if (!body.Dead && why == "Cliffed!") Report($"{name}: fell {Ridge.Top(c.z) - c.y:0} m for 'Cliffed!' and lived");
        else if (body.Dead && buried < 0.6f && !diedOnRock) Report($"{name}: dead but not buried ({buried:0.00} m)");
        if (sank > 0.6f) Report($"{name}: ragdoll sank {sank:0.00} m into the rock");
    }

    static void Crash(string name, Rider r, RiderBody body, SlopeStreamer terrain, Forest props, float z, float v0, System.Func<float, float> steer = null)
    {
        r.Steer = 0f;
        Random.InitState(4242);   // the same throw every time, so a steered crash compares with its unsteered twin
        r.ResetAt(Slope.Centre(z), z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.forward * v0;
        terrain.Prewarm(); props.Prewarm();
        for (int i = 0; i < 30; i++) { r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); }
        r.DoCrash("test");
        var rbs = new System.Collections.Generic.List<Rigidbody>();
        float maxV = 0f, maxW = 0f, maxSpread = 0f, maxBelow = 0f, stopT = -1f; string spinner = ""; int jitterFrames = 0, frames = 0;
        Vector3 rest = Vector3.zero; string spikeNote = "";
        for (float t = 0f; t < 8f; t += Dt)
        {
            r.Steer = steer != null ? steer(t) : 0f;
            if (r.CanGetUp && !body.RagdollMoving) r.RequestUp = true;   // tap to get up once the body has come to rest (a tap allowed mid-tumble would cut the fall short: the whole tumble is what's measured here)
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt);
            if (rbs.Count == 0) foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (!rb.isKinematic && rb != r.Body) rbs.Add(rb);
            Vector3 c = body.RagdollCentre;
            foreach (var rb in rbs)
            {
                if (rb == null) continue;
                if (rb.linearVelocity.magnitude > maxV && rb.linearVelocity.magnitude > v0 + 6f) spikeNote = $"{rb.name} {rb.linearVelocity.magnitude:0.0} m/s at t={t:0.00} near {Near(rb.position, rbs)}";
                maxV = Mathf.Max(maxV, rb.linearVelocity.magnitude);
                if (rb.angularVelocity.magnitude > maxW) { maxW = rb.angularVelocity.magnitude; spinner = rb.name; }
                if (rb.angularVelocity.magnitude > 40f) jitterFrames++;
                // (only while thrown: on the step you get up the body is put away and the rider steps clear of the tree
                // that stopped it, a few metres off, which is not the body coming apart)
                if (r.Crashed && rb.name != "Board" && (rb.position - c).magnitude > maxSpread) { maxSpread = (rb.position - c).magnitude; if (maxSpread > 1.3f) spikeNote += $" | spread {rb.name} {maxSpread:0.00} at t={t:0.00} near {Near(rb.position, rbs)}"; }
                if (float.IsNaN(rb.position.x)) { Report($"{name}: ragdoll NaN"); return; }
            }
            maxBelow = Mathf.Max(maxBelow, Slope.DrawnHeight(c.x, c.z) - c.y);
            if (name == System.Environment.GetEnvironmentVariable("GLITCH_TRACE") && (frames % 30 == 0 || Slope.DrawnHeight(c.x, c.z) - c.y > 0.6f && frames % 6 == 0))
                Debug.Log($"GLITCH-TRACE t={t:0.00} c={c} snow={Slope.DrawnHeight(c.x, c.z):0.00} x-c={c.x - Slope.Centre(c.z):0.0} crevasse={Course.CrevasseNear(c.x, c.z, 4f)} cut={Course.CrevasseCut(c.x, c.z):0.0} v={body.RagdollSpeed:0.0} moving={body.RagdollMoving} canUp={r.CanGetUp} hold={r.HoldCrash} pending={body.FallPending} dead={body.Dead} buried={body.RagdollBelowSnow:0.0} near {Near(c, rbs)}");
            if (r.Crashed) rest = c;
            frames++;
            if (!r.Crashed) { stopT = t; break; }
        }
        r.Steer = 0f;
        if (spikeNote != "") Debug.Log($"GLITCH-SPIKE {name}: {spikeNote}");
        if (body.Dead) Report($"{name}: an ordinary crash on the piste killed the rider");
        Debug.Log($"GLITCH-INFO {name}: rest x-c={rest.x - Slope.Centre(rest.z):0.0} z={rest.z - z:0.0} maxPartSpeed={maxV:0.0} maxSpin={maxW:0.0} rad/s ({spinner}) spread={maxSpread:0.00} m belowSnow={maxBelow:0.00} m up after {stopT:0.0}s jitter {100f * jitterFrames / Mathf.Max(1, frames * rbs.Count):0.0}% of part-frames");
        if (maxV > v0 * 1.6f + 8f) Report($"{name}: ragdoll part flung at {maxV:0.0} m/s");
        if (maxSpread > 1.4f) Report($"{name}: ragdoll came apart ({maxSpread:0.00} m from the pelvis)");
        if (maxBelow > 0.6f) Report($"{name}: ragdoll sank {maxBelow:0.00} m into the snow");
        if (stopT < 0f) Report($"{name}: never got up (8 s)");
    }
}
