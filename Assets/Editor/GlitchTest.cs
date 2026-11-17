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

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        rider.input = null;
        rider.Crash += why => lastCrash = why;
        try
        {
            for (int seed = 0; seed < 4; seed++)
            {
                Slope.Init(1000 + seed * 77);
                terrain.Rebuild(); props.Rebuild();
                // the halfpipe: weave wall to wall at different speeds
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
                    if (rider.Crashed && lastCrash != "Crevasse!" && lastCrash != "Off the ridge!") Report($"seed{seed} superjump z{z0}: crashed ({lastCrash})");   // falling into a crevasse or off a ridge is not a jump glitch
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
                    if (rider.Crashed && lastCrash != "Crevasse!" && lastCrash != "Off the ridge!") Report($"seed{seed} invincible side{side}: crashed while invincible ({lastCrash})");   // it smashes what you hit, it does not stop you falling
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
            }
        }
        catch (System.Exception e) { Debug.LogError("GLITCH exception " + e); issues++; }
        Debug.Log($"GLITCH done: {issues} issue(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
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

    static void Report(string what) { issues++; if (issues < 120) Debug.Log("GLITCH " + what); }

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
        int cyc = 0;
        while (!Ridge.Built(cyc) && cyc < 3) cyc++;
        float zr = Course.First + cyc * Course.Cycle + Ridge.At;
        int sides = Ridge.Sides(cyc);
        Debug.Log($"GLITCH-INFO seed{seed} ridge cycle {cyc} from z={zr:0} sides={sides}");
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
        foreach (float v0 in new[] { 16f, 24f, 32f })
        {
            Ride($"seed{seed} ridge v{v0}", r, body, terrain, props, zr + 20f, 0f, v0, 12f, (t, rr) => FollowCrest(rr, 0f), onCrest($"seed{seed} ridge v{v0}"), true);
            Done($"seed{seed} ridge v{v0}");
        }
        // weave across the crest, a metre and a half either side of the middle
        Ride($"seed{seed} ridge weave", r, body, terrain, props, zr + 20f, 0f, 20f, 12f,
             (t, rr) => FollowCrest(rr, 1.5f * Mathf.Sin(t * 1.4f)), onCrest($"seed{seed} ridge weave"), true);
        Done($"seed{seed} ridge weave");
        // and off the edge on each cliff side
        for (int s = -1; s <= 1; s += 2)
            if (sides == 0 || sides == s) OffTheEdge($"seed{seed} ridge off {(s < 0 ? "left" : "right")}", r, body, terrain, props, zr + Ridge.RampIn + 20f, s);
    }

    /// <summary>Ride the crest, steer off a cliff edge, and check the fall: a crash, a tumble that stays on the rock, and up again below it.</summary>
    static void OffTheEdge(string name, Rider r, RiderBody body, SlopeStreamer terrain, Forest props, float z, float side)
    {
        r.ResetAt(Slope.Centre(z), z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z), z)).normalized * 24f;
        terrain.Prewarm(); props.Prewarm();
        float t = 0f; int n = 0;
        string why = null;
        System.Action<string> heard = w => why = w;
        r.Crash += heard;
        try
        {
            for (; t < 6f && !r.Crashed; t += Dt, n++)
            {
                r.Steer = t < 1.5f ? FollowCrest(r, 0f) : side * 0.8f;
                r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (n % 4 == 0) body.Apply(4 * Dt);
            }
            if (!r.Crashed) { Report($"{name}: rode off the edge without a fall (6 s) at z={r.Z:0} off={r.transform.position.x - Slope.Centre(r.Z):0.0}"); return; }
            float speed = r.Speed;
            float maxBelow = 0f, tc = 0f;
            for (; tc < 12f; tc += Dt, n++)
            {
                if (!r.HoldCrash && tc > 1f) r.RequestUp = true;
                r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt);
                if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                Vector3 c = body.RagdollCentre;
                if (float.IsNaN(c.x)) { Report($"{name}: ragdoll NaN"); return; }
                maxBelow = Mathf.Max(maxBelow, Slope.DrawnHeight(c.x, c.z) - c.y);
                if (!r.Crashed) break;
            }
            Vector3 up = r.transform.position;
            Debug.Log($"GLITCH-INFO {name}: '{why}' at {speed * 3.6f:0} km/h, up after {tc:0.0}s at off={up.x - Slope.Centre(up.z):0.0} n.y={Slope.Normal(up.x, up.z).y:0.00} sank={maxBelow:0.00}");
            if (why != "Off the ridge!") Report($"{name}: fell for '{why}', not off the ridge");
            if (maxBelow > 0.6f) Report($"{name}: ragdoll sank {maxBelow:0.00} m into the rock");
            if (r.Crashed) { Report($"{name}: never got up (12 s)"); return; }
            if (Slope.Normal(up.x, up.z).y < 0.7f) Report($"{name}: got up on a face too steep to ride (n.y={Slope.Normal(up.x, up.z).y:0.00})");
            // and ride on from there: it must not throw you straight off again
            for (float t2 = 0f; t2 < 3f; t2 += Dt, n++)
            {
                r.Steer = 0f;
                r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                if (n % 4 == 0) body.Apply(4 * Dt);
                if (r.Crashed) { Report($"{name}: fell again ('{why}') {t2:0.0}s after getting up at z={r.Z:0} off={r.transform.position.x - Slope.Centre(r.Z):0.0}"); break; }
            }
        }
        finally { r.Crash -= heard; }
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
            if (r.CanGetUp) r.RequestUp = true;   // tap to get up as soon as the game allows
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
                if (rb.name != "Board" && (rb.position - c).magnitude > maxSpread) { maxSpread = (rb.position - c).magnitude; if (maxSpread > 1.3f) spikeNote += $" | spread {rb.name} {maxSpread:0.00} at t={t:0.00} near {Near(rb.position, rbs)}"; }
                if (float.IsNaN(rb.position.x)) { Report($"{name}: ragdoll NaN"); return; }
            }
            maxBelow = Mathf.Max(maxBelow, Slope.DrawnHeight(c.x, c.z) - c.y);
            if (r.Crashed) rest = c;
            frames++;
            if (!r.Crashed) { stopT = t; break; }
        }
        r.Steer = 0f;
        if (spikeNote != "") Debug.Log($"GLITCH-SPIKE {name}: {spikeNote}");
        Debug.Log($"GLITCH-INFO {name}: rest x-c={rest.x - Slope.Centre(rest.z):0.0} z={rest.z - z:0.0} maxPartSpeed={maxV:0.0} maxSpin={maxW:0.0} rad/s ({spinner}) spread={maxSpread:0.00} m belowSnow={maxBelow:0.00} m up after {stopT:0.0}s jitter {100f * jitterFrames / Mathf.Max(1, frames * rbs.Count):0.0}% of part-frames");
        if (maxV > v0 * 1.6f + 8f) Report($"{name}: ragdoll part flung at {maxV:0.0} m/s");
        if (maxSpread > 1.4f) Report($"{name}: ragdoll came apart ({maxSpread:0.00} m from the pelvis)");
        if (maxBelow > 0.6f) Report($"{name}: ragdoll sank {maxBelow:0.00} m into the snow");
        if (stopT < 0f) Report($"{name}: never got up (8 s)");
    }
}
