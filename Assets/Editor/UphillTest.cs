using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Hunts for a rider pulled uphill: on the ground (a roof or a lift station's deck included), moving up the ground's own
/// slope and gaining speed doing it, over several steps, with nothing to explain it (no jump, no kicker's ramp, no
/// boost). Rides many lines over several mountains: bots weaving down the piste, scripted lines across the width
/// (into the valleys' walls, over rock and the ridges, through the trees), across each lift's bottom station deck and
/// down its lane, across the park, slow starts on every kind of ground and getting up after crashes. Each stretch
/// flagged is logged (UPHILL-FLAG) with where, on what, and which of the rider's code was at work, and counted.
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod UphillTest.Run -logFile uphill.log
/// ($UPHILL_SEEDS mountains, default 4.)
/// </summary>
public static class UphillTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static Hamlets hamlets; static SkiLift lifts;
    static MethodInfo hamUpd, liftUpd, forestUpd;
    static FieldInfo fSlowFor, fWobble, fShove, fPop, fBrushing;
    static int n, flags, steps, seedNow;
    static readonly Dictionary<string, int> byCode = new Dictionary<string, int>(), bySurface = new Dictionary<string, int>(), byLine = new Dictionary<string, int>();
    static readonly Dictionary<string, int> examples = new Dictionary<string, int>();

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);

    public static void Run()
    {
        TestPrefs.Pin();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        dress = Object.FindFirstObjectByType<CourseDressing>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP); liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        forestUpd = typeof(Forest).GetMethod("Update", NP);
        fSlowFor = typeof(Rider).GetField("slowFor", NP); fWobble = typeof(Rider).GetField("wobble", NP);
        fShove = typeof(Rider).GetField("shove", NP); fPop = typeof(Rider).GetField("popPending", NP);
        fBrushing = typeof(TreeBreak).GetField("brushing", NP);
        int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("UPHILL_SEEDS"), out int sv) ? sv : 4;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (int k = 0; k < seeds; k++)
            {
                seedNow = 7100 + k * 409;
                TestBoot.NewMountain(game, seedNow, terrain, props);
                if (dress != null) dress.Rebuild();
                r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ);
                Stream(true);
                Lines();
            }
        }
        catch (System.Exception e) { Debug.LogError("UPHILL exception " + e); }
        Debug.Log($"UPHILL-SUMMARY {flags} stretches pulled uphill in {steps} steps ridden over {seeds} mountains ({sw.Elapsed.TotalSeconds:0} s)" +
                  $"\n  by the code at work: {Dump(byCode)}\n  by the ground: {Dump(bySurface)}\n  by the line ridden: {Dump(byLine)}");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static string Dump(Dictionary<string, int> d)
    {
        var keys = new List<string>(d.Keys); keys.Sort((a, b) => d[b].CompareTo(d[a]));
        var sb = new System.Text.StringBuilder();
        foreach (var k in keys) sb.Append($"\n    {d[k],5}  {k}");
        return keys.Count == 0 ? "none" : sb.ToString();
    }

    static void Bump(Dictionary<string, int> d, string k) { d.TryGetValue(k, out int c); d[k] = c + 1; }

    static void Stream(bool full)
    {
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm();
        if (hamlets != null) for (int i = 0; i < (full ? 60 : 2); i++) hamUpd.Invoke(hamlets, null);
        if (lifts != null) liftUpd.Invoke(lifts, null);
        forestUpd?.Invoke(props, null);
        Physics.SyncTransforms();
    }

    // ================================================================ the lines

    delegate float Steering(float t);

    static void Lines()
    {
        float z0 = Course.First + 5f, zEnd = Mathf.Min(Course.ClearFrom - 30f, Course.First + 3f * Course.Cycle);
        var rng = new System.Random(seedNow);
        // bots down the whole stretch: weaving, holding a line off the centre, and wandering
        foreach (float off in new[] { -16f, -6f, 0f, 6f, 16f })
        {
            Ride($"bot weave {off:+0;-0;0}", Slope.Centre(z0) + off, z0, 6f, 60f, t => Mathf.Clamp(0.55f * Mathf.Sin(t * 1.3f + off) + (Slope.Centre(r.Z + 12f) + off - r.Body.position.x) * 0.05f - r.Heading * 0.5f, -1f, 1f), zEnd, jumps: true);
        }
        // across the width at the top of every 40 m: straight down, and hard across into the walls either way
        for (float z = z0; z < zEnd; z += 40f)
            foreach (float off in new[] { -38f, -26f, -14f, -4f, 4f, 14f, 26f, 38f })
            {
                float x = Slope.Centre(z) + off;
                string where = Course.At(z).ToString().ToLowerInvariant();
                Ride($"{where} straight {off:+0;-0}", x, z, 9f, 7f, t => 0f, zEnd);
                Ride($"{where} across {off:+0;-0}", x, z, 9f, 7f, t => off > 0f ? -1f : 1f, zEnd);
                // a slow start (as after getting up), wandering
                float ph = (float)rng.NextDouble() * 6f;
                Ride($"{where} slow {off:+0;-0}", x, z, 1.5f, 7f, t => 0.7f * Mathf.Sin(t * 0.9f + ph), zEnd);
            }
        // across each lift's bottom station: down the line onto its deck beside the chairs, along it, off its end
        foreach (var l in LiftLine.Lifts)
            foreach (float back in new[] { 25f, 12f })
                foreach (float c in new[] { 4.7f, -4.7f })
                    foreach (float v0 in new[] { 3f, 10f, 22f })
                    {
                        Vector3 at = l.At(l.plan - back, c, 0f);
                        at.y = Slope.DrawnHeight(at.x, at.z);
                        Ride($"lift deck {back:0} m up, {c:+0;-0;0} off, {v0:0} m/s", at.x, at.z, v0, 6f, t => 0f, zEnd, dir: l.dir);
                    }
        // the park's kickers, straight on, and the crashes: down hard into whatever is near, then up and away
        for (int c = 0; c < 3; c++)
            foreach (float kz in Course.ParkKickers)
            {
                float z = Course.First + c * Course.Cycle + Course.ParkAt + kz - 30f;
                if (z > zEnd) continue;
                Ride("kicker", Slope.Centre(z), z, 16f, 5f, t => 0f, zEnd);
            }
        for (float z = z0 + 20f; z < zEnd; z += 70f)
            foreach (float off in new[] { -20f, 0f, 20f })
                Ride($"get up {off:+0;-0;0}", Slope.Centre(z) + off, z, 14f, 9f, t => 0.4f * Mathf.Sin(t * 2f), zEnd, crashAt: 1.0f);
    }

    // ================================================================ one ride

    struct Sample { public Vector3 p, v; public float speed, e, up; public bool ground; public string code, surface; }
    static readonly List<Sample> win = new List<Sample>();

    static void Ride(string line, float x, float z, float v0, float seconds, Steering steer, float zEnd, bool jumps = false, Vector3? dir = null, float crashAt = -1f)
    {
        if (z > zEnd) return;
        r.ResetAt(x, z); body.ResetVisual();
        Stream(false);
        foreach (var c in Physics.OverlapSphere(r.Body.position, 1f, ~0, QueryTriggerInteraction.Ignore))
            if (c.attachedRigidbody != r.Body && c.GetComponentInParent<Obstacle>() != null) return;   // (starting in something)
        r.Begin();
        Vector3 d = dir ?? Vector3.forward; d.y = 0f; d.Normalize();
        r.Heading = Mathf.Clamp(Mathf.Atan2(d.x, d.z), -1.45f, 1.45f);
        Vector3 n0 = Slope.Normal(x, z);
        r.Body.linearVelocity = Vector3.ProjectOnPlane(d, n0).normalized * v0;
        win.Clear();
        float t = 0f, lastJump = -9f, nextJump = 2.5f, getUp = -1f, dt = Time.fixedDeltaTime;
        bool flagged = false;
        while (t < seconds && r.Z < zEnd)
        {
            r.Steer = steer(t);
            if (jumps && t >= nextJump && !r.Airborne) { r.QueuePop(Rider.TapPop); lastJump = t; nextJump = t + 3.1f; }
            if (crashAt > 0f && t >= crashAt && !r.Crashed && getUp < 0f) { r.DoCrash("Test"); getUp = t + 1.2f; }
            if (r.Crashed && getUp > 0f && t >= getUp) { r.RequestUp = true; r.HoldCrash = false; }
            Step();
            t += dt; steps++;
            if (n % 30 == 0) Stream(false);
            var s = Now();
            win.Add(s);
            if (win.Count > 30) win.RemoveAt(0);
            // a stretch: 0.25 s on the ground, moving up the ground's slope throughout, faster at the end
            // than at the start, and more energy (speed and height) than it began with: nothing but the code put it there
            if (win.Count == 30 && !flagged && t - lastJump > 0.6f)
            {
                bool all = true; string code = null;
                foreach (var w in win) { if (!w.ground || w.up < 0.4f) { all = false; break; } }
                if (all)
                {
                    var a = win[0]; var b = win[win.Count - 1];
                    float gain = b.up - a.up, de = b.e - a.e;
                    if (gain > 0.4f && de > 1.5f)
                    {
                        // the code at work: the most frequent over the stretch
                        var counts = new Dictionary<string, int>();
                        foreach (var w in win) Bump(counts, w.code);
                        foreach (var kv in counts) if (code == null || kv.Value > counts[code]) code = kv.Key;
                        flagged = true; flags++;
                        string key = Kind(line);
                        Bump(byCode, code); Bump(bySurface, b.surface); Bump(byLine, key);
                        Bump(examples, code + key);
                        if (examples[code + key] <= 4)
                            Debug.Log($"UPHILL-FLAG seed{seedNow} '{line}' t={t:0.00} at z={b.p.z:0.0} x-c={b.p.x - Slope.Centre(b.p.z):+0.0;-0.0} on {b.surface}: up the slope {a.up:0.00} -> {b.up:0.00} m/s, speed {a.speed:0.00} -> {b.speed:0.00}, energy +{de:0.0} J/kg over 0.25 s; code: {code}; heading {r.Heading * Mathf.Rad2Deg:0} deg");
                    }
                }
            }
            if (!r.Crashed && !r.Airborne && r.Speed < 0.3f && t > 2f) break;
        }
    }

    static string Kind(string line)
    {
        int i = line.IndexOfAny("+-0123456789".ToCharArray());
        return (i > 0 ? line.Substring(0, i) : line).Trim();
    }

    /// <summary>The rider this step: on the ground, which way up the ground's slope it is going and how fast, its
    /// energy (speed and height, per kg), and which of its code is at work.</summary>
    static Sample Now()
    {
        var s = new Sample();
        Vector3 p = r.Body.position, v = r.Velocity;
        bool deck = r.OnRoof != null && r.OnRoof.s == Roofs.Deck;
        s.ground = !r.Crashed && !r.Airborne && !r.Grinding;
        Vector3 nrm = r.GroundNormal(p);
        Vector3 down = Vector3.ProjectOnPlane(Vector3.down, nrm);
        float steep = down.magnitude;
        // (the uphill direction of the ground; on near-level ground there is none to speak of)
        s.up = steep > 0.05f ? Vector3.Dot(new Vector3(v.x, 0f, v.z), -new Vector3(down.x, 0f, down.z).normalized) : 0f;
        s.speed = r.Speed; s.p = p; s.v = v;
        // (height on the smooth slope the board steers by, not the drawn mesh it is put on: the mesh's 1.5 m facets stand a
        // few centimetres off it, worth 2 J/kg either way; on a roof or a deck, the board's own height)
        float h = r.OnRoof != null || deck ? p.y - Rider.R : Slope.Height(p.x, p.z);
        s.e = 0.5f * r.Speed * r.Speed + Rider.G * h;
        var parts = new List<string>();
        if (deck) parts.Add("lift deck");
        else if (r.Grinding) parts.Add("grind");
        if (r.BoostTime > 0f) parts.Add("boost");
        if (r.Speed < 3f) parts.Add("stalled push (speed < 3)");
        if ((float)fSlowFor.GetValue(r) > 0f) parts.Add("Breakthrough slow");
        if ((float)fWobble.GetValue(r) > 0f) parts.Add("wobble");
        if (r.Invulnerable) parts.Add("just up after a crash");
        if (r.OnRock) parts.Add("rock");
        if (TreeBreak.Instance != null && fBrushing != null && ((System.Collections.IDictionary)fBrushing.GetValue(TreeBreak.Instance)).Count > 0) parts.Add("TreeBreak brush");
        s.code = parts.Count == 0 ? "GroundStep (plain)" : string.Join(" + ", parts);
        string sec = Course.At(p.z).ToString();
        string ground = r.OnRoof != null ? "a roof" : deck ? "lift deck" : r.OnRock ? "rock" : Course.InPipe(p.x, p.z) ? "pipe" : "snow";
        s.surface = $"{ground} ({sec}, slope {Mathf.Acos(Mathf.Clamp01(nrm.y)) * Mathf.Rad2Deg:0} deg)";
        return s;
    }

    static void Step()
    {
        float realDt = Time.fixedDeltaTime / Mathf.Max(Time.timeScale, 1e-4f);
        game.TestTickHitStop(realDt);
        float dt = Time.fixedDeltaTime;
        r.Tick(dt); body.PhysicsStep(); Physics.Simulate(dt);
        if (n % 2 == 0 || r.Crashed) body.Apply(r.Crashed ? dt : 2f * dt);
        n++;
    }
}
