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

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        rider.input = null;
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
                // crashes: throw the ragdoll at different speeds
                foreach (float v0 in new[] { 8f, 18f, 30f })
                    Crash($"seed{seed} crash v{v0}", rider, body, terrain, props, 300f + seed * 40f, v0);
            }
        }
        catch (System.Exception e) { Debug.LogError("GLITCH exception " + e); issues++; }
        Debug.Log($"GLITCH done: {issues} issue(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void Report(string what) { issues++; if (issues < 120) Debug.Log("GLITCH " + what); }

    static void Ride(string name, Rider r, RiderBody body, SlopeStreamer terrain, Forest props, float z, float dx, float v0, float secs, System.Func<float, Rider, float> steer)
    {
        r.ResetAt(Slope.Centre(z) + dx, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.forward * v0;
        terrain.Prewarm(); props.Prewarm();
        Vector3 p1 = r.transform.position, p0 = p1, vprev = r.Body.linearVelocity;
        float h0 = r.Heading; bool wasAir = false;
        int n = 0;
        for (float t = 0f; t < secs; t += Dt, n++)
        {
            r.Steer = steer(t, r);
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 4 == 0) body.Apply(4 * Dt);
            Vector3 p = r.Body.position, v = r.Body.linearVelocity;
            if (float.IsNaN(p.x) || float.IsNaN(v.x)) { Report($"{name}: NaN at t={t:0.00}"); return; }
            if (r.Crashed) return;   // a crash is not a glitch here
            // a jerk: velocity changing far faster than gravity, landings or grip could manage
            float acc = (v - vprev).magnitude / Dt;
            bool landing = wasAir && !r.Airborne;
            if (acc > 400f && !landing) Report($"{name}: jerk {acc:0} m/s^2 at t={t:0.00} z={p.z:0} x-c={p.x - Slope.Centre(p.z):0.0} air={r.Airborne} pipe={Course.InPipe(p.x, p.z)} v={v} prev={vprev}");
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
            p0 = p1; p1 = p; vprev = v; h0 = r.Heading; wasAir = r.Airborne;
        }
    }

    static void Crash(string name, Rider r, RiderBody body, SlopeStreamer terrain, Forest props, float z, float v0)
    {
        r.ResetAt(Slope.Centre(z), z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.forward * v0;
        terrain.Prewarm(); props.Prewarm();
        for (int i = 0; i < 30; i++) { r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); }
        r.DoCrash("test");
        var rbs = new System.Collections.Generic.List<Rigidbody>();
        float maxV = 0f, maxW = 0f, maxSpread = 0f, maxBelow = 0f, stopT = -1f; string spinner = ""; int jitterFrames = 0, frames = 0;
        for (float t = 0f; t < 8f; t += Dt)
        {
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt);
            if (rbs.Count == 0) foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (!rb.isKinematic && rb != r.Body) rbs.Add(rb);
            Vector3 c = body.RagdollCentre;
            foreach (var rb in rbs)
            {
                if (rb == null) continue;
                maxV = Mathf.Max(maxV, rb.linearVelocity.magnitude);
                if (rb.angularVelocity.magnitude > maxW) { maxW = rb.angularVelocity.magnitude; spinner = rb.name; }
                if (rb.angularVelocity.magnitude > 40f) jitterFrames++;
                if (rb.name != "Board") maxSpread = Mathf.Max(maxSpread, (rb.position - c).magnitude);
                if (float.IsNaN(rb.position.x)) { Report($"{name}: ragdoll NaN"); return; }
            }
            maxBelow = Mathf.Max(maxBelow, Slope.DrawnHeight(c.x, c.z) - c.y);
            frames++;
            if (!r.Crashed) { stopT = t; break; }
        }
        Debug.Log($"GLITCH-INFO {name}: maxPartSpeed={maxV:0.0} maxSpin={maxW:0.0} rad/s ({spinner}) spread={maxSpread:0.00} m belowSnow={maxBelow:0.00} m up after {stopT:0.0}s jitter {100f * jitterFrames / Mathf.Max(1, frames * rbs.Count):0.0}% of part-frames");
        if (maxV > v0 * 1.6f + 8f) Report($"{name}: ragdoll part flung at {maxV:0.0} m/s");
        if (maxSpread > 1.4f) Report($"{name}: ragdoll came apart ({maxSpread:0.00} m from the pelvis)");
        if (maxBelow > 0.6f) Report($"{name}: ragdoll sank {maxBelow:0.00} m into the snow");
        if (stopT < 0f) Report($"{name}: never got up (8 s)");
    }
}
