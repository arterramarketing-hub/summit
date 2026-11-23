using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The nudge log: a bot rides ordinary runs with a finger that only moves in steps (held still for half a second at
/// a time), and every time the rider's sideways speed or line changes while the finger is still, that is a nudge:
/// logged with what caused it. Also counted: the kinks in the board's height on the snow, airs nobody asked for, and
/// jolts of the chase camera.
///  - NUDGE-RUN: one line per run. NUDGE-TABLE: nudges by cause, per minute. NUDGE-RESULT: the totals.
///  - "open piste" is the cruising piste, within the groomed width, away from the park and the danger stretch.
///  - Hands off: straight runs on open piste with no finger at all: how far the board is carried off its line.
///  - Garden: a run through rocks set on the piste (0.2-0.55 m, the ride-over heights).
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod NudgeTest.Run -logFile nudge.log
/// Strips (with the GPU): -executeMethod NudgeTest.Strips (NUDGE_OUT=dir): the chase camera's view through the rocks.
/// </summary>
public static class NudgeTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    const float Settle = 0.25f;          // s after the finger moves: what changes then is the finger's doing...
    const float SettleMost = 1.5f;       // ...and on until the sideways speed first gets to what it asks for (or this long: in the air it comes slowly)
    const float FullSide = 15f;          // m/s sideways a full drag asks for (Rider.SideSpeed at TURN 1.0)
    const float AccOn = 2.5f;            // m/s^2 sideways with the finger still: a nudge has begun
    const float MinSwing = 0.25f;        // m/s the sideways speed must move for it to count
    const float KinkVy = 0.8f;           // m/s the board's vertical speed jumps in one step on the snow: a kink
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static ChaseCamera chase; static MethodInfo camStep;
    static FieldInfo fWobble, fSlow, fKnock;
    static float tHop = -9f, tGlance = -9f, tHit = -9f, tLand = -9f, tJump = -9f, tCrash = -9f, tUp = -9f, tGrind = -9f, tBrush = -9f, now;
    static int brushes;
    // NUDGE_TRACE="seed77 run,6.2,6.5": every step of that ride between those times
    static string traceName; static float traceA, traceB;

    class Tally
    {
        public float secs, openSecs, airSecs, groundSecs;
        public int kinks, micro, airs, shortAirs, crashes, camJolts, camJoltsOpen; public float airLen, speedSum, speedMax;
        public double vAcc2; public int vAccN;
        public readonly Dictionary<string, (int n, int open, float sum, float max)> by = new Dictionary<string, (int, int, float, float)>();
        public void Add(string why, bool open, float swing)
        {
            by.TryGetValue(why, out var a);
            by[why] = (a.n + 1, a.open + (open ? 1 : 0), a.sum + swing, Mathf.Max(a.max, swing));
        }
        public int Count(bool openOnly) { int n = 0; foreach (var kv in by) n += openOnly ? kv.Value.open : kv.Value.n; return n; }
    }

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
        chase = Object.FindFirstObjectByType<ChaseCamera>();
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        fWobble = typeof(Rider).GetField("wobble", NP); fSlow = typeof(Rider).GetField("slowFor", NP); fKnock = typeof(Rider).GetField("knockFor", NP);
        r.input = null;
        r.Hopped += _ => tHop = now;
        r.Glanced += _ => tGlance = now;
        r.HitObstacle += (ob, at, into) => tHit = now;
        r.Landed += _ => tLand = now;
        r.Jumped += () => tJump = now;
        r.Crash += _ => tCrash = now;
        r.GrindStart += _ => tGrind = now;
        var dressing = Object.FindFirstObjectByType<CourseDressing>();   // (built for the boot mountain, as in GlitchTest)
        if (dressing != null) { var tgt = dressing.target; dressing.target = null; dressing.Rebuild(); dressing.target = tgt; }
    }

    static float F(FieldInfo f) => f != null ? (float)f.GetValue(r) : 0f;

    /// <summary>What is acting on the board now, the likeliest cause first.</summary>
    static string Why(Vector3 p)
    {
        if (r.Crashed || now - tUp < 1f || now - tCrash < 0.3f) return "crash / getting up";
        if (r.Grinding) tGrind = now;   // (and the moment after it lets go: the flight off its end is the rail's line, not the finger's yet)
        if (r.Grinding || now - tGrind < 0.6f) return "rail, box or cable";
        if (now - tGlance < 0.35f) return "glancing hit";
        if (now - tHit < 0.35f) return "hit / scrape";
        if (now - tHop < 0.45f) return "ride-over";
        if (now - tBrush < 0.35f) return "tree branches";
        if (F(fSlow) > 0f || F(fKnock) > 0f) return "sign / flag / snapped tree";
        if (now - tLand < 0.3f) return "landing";
        if (r.Airborne && r.AboveGround < 0.4f && Slope.Normal(p.x, p.z).y < 0.62f) return "falling down a face too steep to ride";
        if (r.Airborne) return Course.InPipe(p.x, p.z) ? "pipe air" : "in the air";
        if (r.Undercut > 0f) return "avalanche";
        if (F(fWobble) > 0f) return r.OnRock ? "wobble on rock" : "wobble (after a landing or a knock)";
        if (r.Speed < 6f) return "crawling (fall-line rule)";
        if (Course.InPipe(p.x, p.z)) return "pipe wall";
        if (r.OnRock) return "rock under the board";
        return "terrain (the snow's own tilt)";
    }

    static bool Open(Vector3 p) => Course.At(p.z) == Course.Section.Cruise && Mathf.Abs(p.x - Slope.Centre(p.z)) < Slope.PisteHalf - 2f && !Course.InPipe(p.x, p.z);

    static float Quant(float s) => Mathf.Round(Mathf.Clamp(s, -1f, 1f) * 4f) / 4f;

    /// <summary>
    /// One ride of `secs` from (x, z) at v0, the finger from `finger` (asked every half second and held between;
    /// null: no finger). Tallies into `T`. `frame` (the strips): called each camera frame.
    /// </summary>
    static void Ride(string name, float x, float z, float v0, float secs, System.Func<Rider, float> finger, Tally T, bool log, System.Action<int> frame = null, bool hold = false)
    {
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Heading = 0f; r.Steer = 0f;
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        if (chase != null) { chase.rider = r; chase.Snap(); }
        if (TreeBreak.Instance != null) brushes = TreeBreak.Instance.Brushes;
        tHop = tGlance = tHit = tLand = tJump = tCrash = tUp = tGrind = tBrush = -9f;
        bool reached = false; float lastFinger = 0f, decide = 0f, steer = 0f;   // (the start itself is a move of the finger)
        Vector3 vPrev = r.Body.linearVelocity; bool airPrev = false, crashedPrev = false; float airStart = 0f, airPeak = 0f; bool airPop = false;
        // the nudge under way
        bool inEvent = false; float evStart = 0f, evLast = 0f, evVx0 = 0f, evSwing = 0f, evPeak = 0f; string evWhy = null, evInfo = null; bool evOpen = false; Vector3 evAt = default; int evRank = 99;
        // the camera
        Vector3 camRelPrev = default, camRelVelPrev = default, camPosPrev = default; float camVyPrev = 0f; float camYawPrev = 0f, camYawRatePrev = 0f; int camN = 0; float camJoltT = -9f;
        float x0 = x, drift = 0f; int n = 0;
        var ranks = new List<string>();
        int Rank(string w) { int i = ranks.IndexOf(w); if (i < 0) { ranks.Add(w); i = ranks.Count - 1; } return i; }
        void Close()
        {
            if (!inEvent) return;
            inEvent = false;
            if (evSwing < MinSwing) return;
            T.Add(evWhy, evOpen, evSwing);
            if (log) Debug.Log($"NUDGE {name} t={evStart:0.00} z={evAt.z:0} x-c={evAt.x - Slope.Centre(evAt.z):+0.0;-0.0}: sideways speed moved {evSwing:0.00} m/s (peak {evPeak:0} m/s^2) over {evLast - evStart + Dt:0.00} s, finger still at {steer:+0.00;-0.00}: {evWhy}{(evOpen ? " [open piste]" : "")} ({evInfo})");
        }
        for (float t = 0f; t < secs; t += Dt, n++)
        {
            now = t;
            if (finger != null && t >= decide && !r.Crashed)
            {
                decide = t + 0.5f;
                float s = Quant(finger(r));
                if (!Mathf.Approximately(s, steer)) { steer = s; lastFinger = t; reached = false; }
            }
            r.Steer = r.Crashed ? 0f : steer;
            if (hold && r.Speed > v0 && !r.Airborne && !r.Crashed) r.Body.linearVelocity *= v0 / r.Speed;
            r.TestContacts(Dt);
            if (!r.Crashed) r.Tick(Dt); else r.Tick(Dt);
            body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 2 == 0) body.Apply(2 * Dt);
            Vector3 p = r.Body.position, v = r.Body.linearVelocity;
            if (r.Grinding) tGrind = t;
            if (TreeBreak.Instance != null && TreeBreak.Instance.Brushes != brushes) { brushes = TreeBreak.Instance.Brushes; tBrush = t; }
            if (r.Crashed && !crashedPrev) T.crashes++;
            if (r.Crashed) { if (r.CanGetUp) r.RequestUp = true; }
            else if (crashedPrev) { tUp = t; lastFinger = t; reached = false; }
            bool open = Open(p);
            if (!r.Crashed) { T.secs += Dt; if (open) T.openSecs += Dt; if (r.Airborne) T.airSecs += Dt; else T.groundSecs += Dt; float h = new Vector2(v.x, v.z).magnitude; T.speedSum += h * Dt; T.speedMax = Mathf.Max(T.speedMax, h); }
            // airs: nobody asked for one that did not come off a pop
            if (r.Airborne && !airPrev) { airStart = t; airPeak = 0f; airPop = now - tJump < 0.1f; }
            if (r.Airborne) airPeak = Mathf.Max(airPeak, r.AboveGround);
            if (!r.Airborne && airPrev && !r.Crashed && !airPop && !r.Grinding)
            {
                T.airs++; if (t - airStart < 0.55f) T.shortAirs++; T.airLen += t - airStart;
                if (t - airStart < 0.35f && airPeak < 0.35f) { T.micro++; if (log) Debug.Log($"NUDGE-AIR {name} t={airStart:0.00} z={p.z:0}: off the snow for {t - airStart:0.00} s, {airPeak:0.00} m up{(now - tHop < 1f ? " (ride-over)" : "")}"); }
            }
            // the board's height on the snow: a kink is its vertical speed jumping in one step
            if (!r.Airborne && !airPrev && !r.Crashed && !crashedPrev && !r.Grinding && now - tLand > 0.05f)
            {
                float dvy = Mathf.Abs(v.y - vPrev.y);
                if (dvy > KinkVy) T.kinks++;
                T.vAcc2 += (double)(dvy / Dt) * (dvy / Dt); T.vAccN++;
            }
            // the nudge: sideways speed changing with the finger still
            if (Mathf.Abs(v.x - steer * FullSide) < 0.1f) reached = true;
            bool settled = t - lastFinger > (reached ? Settle : SettleMost) && !r.Crashed && !crashedPrev && now - tUp > 0.05f;
            float ax = (v.x - vPrev.x) / Dt;
            if (settled && Mathf.Abs(ax) > AccOn)
            {
                if (!inEvent) { inEvent = true; evStart = t; evVx0 = vPrev.x; evSwing = 0f; evPeak = 0f; evWhy = null; evOpen = open; evAt = p; evRank = 99;
                    evInfo = $"{Course.At(p.z)}, {r.Speed * 3.6f:0} km/h, {(r.Airborne ? $"air {r.AirTime:0.00} s {r.AboveGround:0.0} m up" : "on the snow")}, slope n.y {Slope.Normal(p.x, p.z).y:0.00}{(r.OnRock ? ", rock" : "")}, vx {vPrev.x:0.0}->{v.x:0.0}"; }
                evLast = t;
                evSwing = Mathf.Max(evSwing, Mathf.Abs(v.x - evVx0)); evPeak = Mathf.Max(evPeak, Mathf.Abs(ax));
                string w = Why(p);
                // (the first cause seen names it, unless a likelier one turns up: a landing's wobble is the landing's)
                if (evWhy == null) { evWhy = w; evRank = Rank(w); }
                else if (w == "glancing hit" || w == "ride-over" || w == "landing" || w == "hit / scrape") evWhy = w;
            }
            else if (inEvent && (t - evLast > 0.08f || !settled)) Close();
            if (finger == null && !r.Crashed) drift = Mathf.Max(drift, Mathf.Abs(p.x - x0));
            // the chase camera, at 60 frames a second
            if (chase != null && n % 2 == 1)
            {
                ChaseCamera.TestDt = 2 * Dt;
                camStep.Invoke(chase, null);
                Vector3 rel = chase.transform.position - r.transform.position;
                float yaw = chase.transform.eulerAngles.y;
                if (camN > 0)
                {
                    Vector3 relVel = (rel - camRelPrev) / (2 * Dt);
                    // (its own motion: round the rider across the map, and its height over the world; the rider's own
                    // rise and fall under it is not the camera's)
                    float camVy = (chase.transform.position.y - camPosPrev.y) / (2 * Dt);
                    relVel.y = camVy;
                    float yawRate = Mathf.DeltaAngle(camYawPrev, yaw) / (2 * Dt);
                    if (camN > 1 && settled && !r.Crashed)
                    {
                        bool jolt = (relVel - camRelVelPrev).magnitude > 1.2f || Mathf.Abs(yawRate - camYawRatePrev) > 25f;
                        if (jolt && t - camJoltT > 0.2f)
                        {
                            T.camJolts++; if (open) T.camJoltsOpen++;
                            if (log) Debug.Log($"NUDGE-CAM {name} t={t:0.00} z={p.z:0}: the camera's own speed jumped {(relVel - camRelVelPrev).magnitude:0.0} m/s, its swing {Mathf.Abs(yawRate - camYawRatePrev):0} deg/s, in a frame (by {relVel - camRelVelPrev}): {Why(p)}{(open ? " [open piste]" : "")}");
                        }
                        if (jolt) camJoltT = t;
                    }
                    camRelVelPrev = relVel; camYawRatePrev = yawRate;
                }
                camRelPrev = rel; camYawPrev = yaw; camPosPrev = chase.transform.position; camN++;
                frame?.Invoke(camN);
            }
            if (traceName != null && name.StartsWith(traceName) && t >= traceA && t <= traceB)
                Debug.Log($"NUDGE-TRACE t={t:0.000} p=({p.x:0.00},{p.y:0.00},{p.z:0.00}) v=({v.x:0.00},{v.y:0.00},{v.z:0.00}) speed={r.Speed:0.0} head={r.Heading:0.000} air={r.Airborne} above={r.AboveGround:0.00} steer={r.Steer:0.00} grind={r.Grinding} solid={Course.Solid(p.x, p.z, r.Speed)} drawn={Slope.DrawnHeight(p.x, p.z):0.00} why={Why(p)}");
            vPrev = v; airPrev = r.Airborne; crashedPrev = r.Crashed;
            if (float.IsNaN(p.x)) { Debug.Log($"NUDGE-FAIL {name}: NaN"); break; }
        }
        Close();
        ChaseCamera.TestDt = 0f;
        if (finger == null) Debug.Log($"NUDGE-HANDSOFF {name}: carried {drift:0.00} m off its line in {secs:0} s, hands off, from {v0 * 3.6f:0} km/h (now {r.Speed * 3.6f:0} km/h at z={r.Z:0}, crashed={r.Crashed})");
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 400 && r.Crashed; i++) { r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); if (r.CanGetUp) r.RequestUp = true; } }
    }

    static void Print(string title, Tally T)
    {
        float min = T.secs / 60f, openMin = Mathf.Max(1e-3f, T.openSecs / 60f);
        var sb = new System.Text.StringBuilder($"NUDGE-TABLE {title}: {T.secs:0} s ridden ({T.openSecs:0} s on open piste), by cause (count, per minute, on open piste, mean and most m/s of sideways speed):\n");
        var keys = new List<string>(T.by.Keys); keys.Sort((a, b) => T.by[b].n.CompareTo(T.by[a].n));
        foreach (var k in keys) { var a = T.by[k]; sb.Append($"  {k,-40} {a.n,4}  {a.n / min,6:0.0}/min  open {a.open,4}  mean {a.sum / a.n:0.00}  most {a.max:0.00}\n"); }
        sb.Append($"NUDGE-RESULT {title}: {T.Count(false) / min:0.0} nudges/min over the whole run ({T.Count(false)}), {T.Count(true) / openMin:0.0}/min on open piste ({T.Count(true)}); " +
                  $"height kinks {T.kinks / Mathf.Max(1e-3f, T.groundSecs / 60f):0}/min (rms {Mathf.Sqrt((float)(T.vAcc2 / Mathf.Max(1, T.vAccN))):0} m/s^2 up and down on the snow); " +
                  $"airs not asked for {T.airs / min:0.0}/min, of them under 0.55 s {T.shortAirs / min:0.0}/min and hops under 0.35 s {T.micro / min:0.0}/min, {T.airLen / Mathf.Max(1, T.airs):0.00} s each on average; in the air {100f * T.airSecs / Mathf.Max(1e-3f, T.secs):0}% of the time; " +
                  $"camera jolts {T.camJolts / min:0.0}/min ({T.camJoltsOpen / openMin:0.0}/min on open piste); crashes {T.crashes}; speed over the ground {T.speedSum / Mathf.Max(1e-3f, T.secs) * 3.6f:0} km/h on average, {T.speedMax * 3.6f:0} at most");
        Debug.Log(sb.ToString());
    }

    static System.Func<Rider, float> Bot(int seed) => rr =>
    {
        // down the piste, weaving a third of its width: aims at a line, by a sideways speed, the finger in quarter steps
        float z = rr.Z, target = Slope.Centre(z + 25f) + 7f * Mathf.Sin(z / 45f + seed);
        float err = target - rr.X;
        return Mathf.Abs(err) < 1.2f ? 0f : err / 1.2f / 15f;
    };

    public static void Run()
    {
        Setup();
        var tr = System.Environment.GetEnvironmentVariable("NUDGE_TRACE");
        if (!string.IsNullOrEmpty(tr)) { var q = tr.Split(','); traceName = q[0]; traceA = float.Parse(q[1]); traceB = float.Parse(q[2]); }
        try
        {
            var all = new Tally();
            int[] seeds = { 1000, 1077, 4242, 77 };
            foreach (int seed in seeds)
            {
                TestBoot.NewMountain(game, seed, terrain, props);
                var T = new Tally();
                float z0 = Course.First + 20f;
                Ride($"seed{seed} run", Slope.Centre(z0), z0, 8f, 75f, Bot(seed), T, true);
                Debug.Log($"NUDGE-RUN seed{seed}: to z={r.Z:0}, {T.Count(false)} nudges ({T.Count(true)} on open piste) in {T.secs:0} s, {T.kinks} height kinks, {T.airs} airs ({T.micro} hops), {T.camJolts} camera jolts, {T.crashes} crashes");
                Merge(all, T);
            }
            Print("runs", all);

            // hands off on open piste: nothing but the mountain acts on the board
            var hands = new Tally();
            TestBoot.NewMountain(game, 4242, terrain, props);
            var spots = OpenSpots(Course.First + 60f, 6, 70f);
            foreach (float z in spots)
                foreach (float v0 in new[] { 15f, 25f })
                    Ride($"hands off z{z:0} v{v0:0}", Slope.Centre(z) + 4f, z, v0, 2.5f, null, hands, true, null, true);
            Print("hands off", hands);

            // through the rocks
            var garden = new Tally();
            foreach (float v0 in new[] { 12f, 20f, 28f })
            {
                float z = spots[0];
                var rocks = Garden(z, 7);
                try { Ride($"garden v{v0:0}", Slope.Centre(z), z, v0, 60f / v0, rr => 0f, garden, true, null, true); }
                finally { foreach (var g in rocks) Object.DestroyImmediate(g); }
            }
            Print("rock garden", garden);
        }
        catch (System.Exception e) { Debug.LogError("NUDGE exception " + e); }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    /// <summary>One of GlitchTest's rides, step by step: NUDGE_DEBUG="seed,z,dx,v0,amp,rate,t0,t1" (the finger a sine of
    /// that size and rate; every step between t0 and t1 logged).</summary>
    public static void Debug1()
    {
        Setup();
        var q = (System.Environment.GetEnvironmentVariable("NUDGE_DEBUG") ?? "1000,-1,0,14,0.9,1.3,2.9,3.4").Split(',');
        float F(int i) => float.Parse(q[i], System.Globalization.CultureInfo.InvariantCulture);
        TestBoot.NewMountain(game, (int)F(0), terrain, props);
        float z = F(1) < 0f ? Course.First + Course.PipeAt + 40f : F(1);
        r.ResetAt(Slope.Centre(z) + F(2), z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = F(4) >= 9f ? Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z), z)).normalized * F(3) : Vector3.forward * F(3);
        if (q.Length > 8 && q[8] == "inv") r.AddInvincible(60f);
        terrain.Prewarm(); props.Prewarm();
        int n = 0;
        for (float t = 0f; t < F(7); t += Dt, n++)
        {
            r.Steer = F(4) >= 9f ? (t < 1.5f ? 0f : Mathf.Clamp((0.75f - r.Heading) * 2f, -1f, 1f)) : F(4) * Mathf.Sin(t * F(5));   // (9: GlitchTest's ride off a ridge's snow side)
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 4 == 0) body.Apply(4 * Dt);
            Vector3 p = r.Body.position, v = r.Body.linearVelocity;
            if (t >= F(6))
                Debug.Log($"NUDGE-TRACE t={t:0.000} p=({p.x:0.00},{p.y:0.00},{p.z:0.00}) x-c={p.x - Slope.Centre(p.z):0.0} v=({v.x:0.00},{v.y:0.00},{v.z:0.00}) speed={r.Speed:0.0} head={r.Heading:0.000} air={r.Airborne} above={r.AboveGround:0.00} steer={r.Steer:0.00} grind={r.Grinding} crashed={r.Crashed} solid={Course.Solid(p.x, p.z, r.Speed)} over={p.y - Rider.R - Slope.DrawnHeight(p.x, p.z):0.00} n.y={Slope.Normal(p.x, p.z).y:0.00} sec={Course.At(p.z)} crev={Course.CrevasseCut(p.x, p.z):0.00} roof={(r.OnRoof != null)}");
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static void Merge(Tally a, Tally b)
    {
        a.secs += b.secs; a.openSecs += b.openSecs; a.airSecs += b.airSecs; a.groundSecs += b.groundSecs;
        a.kinks += b.kinks; a.micro += b.micro; a.airs += b.airs; a.shortAirs += b.shortAirs; a.airLen += b.airLen; a.speedSum += b.speedSum; a.speedMax = Mathf.Max(a.speedMax, b.speedMax); a.crashes += b.crashes; a.camJolts += b.camJolts; a.camJoltsOpen += b.camJoltsOpen;
        a.vAcc2 += b.vAcc2; a.vAccN += b.vAccN;
        foreach (var kv in b.by) { a.by.TryGetValue(kv.Key, out var x); a.by[kv.Key] = (x.n + kv.Value.n, x.open + kv.Value.open, x.sum + kv.Value.sum, Mathf.Max(x.max, kv.Value.max)); }
    }

    /// <summary>Open cruising piste with `len` m clear down the middle: no crevasse, nothing standing within 10 m.</summary>
    static List<float> OpenSpots(float z, int count, float len)
    {
        var spots = new List<float>();
        for (; z < Course.First + 3f * Course.Cycle && spots.Count < count; z += 5f)
        {
            bool bad = false;
            for (float zz = z - 5f; zz < z + len && !bad; zz += 2f)
            {
                float x = Slope.Centre(zz);
                bad = Course.At(zz) != Course.Section.Cruise || !Course.Solid(x, zz, 10f) || Course.CrevasseNear(x, zz, 8f) || Slope.Normal(x, zz).y < 0.7f;
            }
            if (bad) continue;
            r.ResetAt(Slope.Centre(z), z); terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
            float zm = z + len * 0.5f;
            foreach (var c in Physics.OverlapBox(new Vector3(Slope.Centre(zm), Slope.Height(Slope.Centre(zm), zm), zm), new Vector3(12f, 60f, len * 0.5f + 5f)))
                if (c.GetComponentInParent<Obstacle>() != null) { bad = true; break; }
            if (bad) continue;
            spots.Add(z); z += len;
        }
        if (spots.Count < count) Debug.Log($"NUDGE-FAIL only {spots.Count} open stretches of {len} m");
        while (spots.Count < count) spots.Add(Course.First + 120f + spots.Count * 80f);
        return spots;
    }

    /// <summary>Rocks on the piste from z + 12 on, every `every` m down the line of the ride and a little either side of
    /// it: squat eggs standing 0.2 to 0.55 m out of the snow (all ridden over).</summary>
    static List<GameObject> Garden(float z, float every)
    {
        var made = new List<GameObject>();
        float[] hs = { 0.25f, 0.4f, 0.55f, 0.3f, 0.5f, 0.2f, 0.45f };
        float[] dx = { 0f, 0.5f, -0.4f, 0.2f, -0.6f, 0.1f, 0.4f };
        for (int i = 0; i < 7; i++)
        {
            float zz = z + 12f + i * every, x = Slope.Centre(z) + dx[i], h = hs[i];
            Vector3 n = Slope.Normal(x, zz);
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            float ry = Mathf.Max(h, 0.6f);
            g.transform.localScale = new Vector3(1.5f + 0.2f * (i % 3), 2f * ry, 1.3f + 0.3f * (i % 2));
            g.transform.SetPositionAndRotation(new Vector3(x, Slope.DrawnHeight(x, zz), zz) + n * (h - ry), Quaternion.FromToRotation(Vector3.up, n));
            var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh; mc.convex = true;
            var mr = g.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null) { var m = new Material(mr.sharedMaterial); m.color = new Color(0.33f, 0.33f, 0.36f); mr.sharedMaterial = m; }
            g.AddComponent<Obstacle>().label = "Rock";
            g.name = "NudgeRock";
            made.Add(g);
        }
        Physics.SyncTransforms();
        return made;
    }

    // ---------------------------------------------------------------- frame strips (needs the GPU)

    /// <summary>The chase camera's own view down a run through the rocks: a frame every 0.1 s, in a strip.</summary>
    public static void Strips()
    {
        Setup();
        string outDir = System.Environment.GetEnvironmentVariable("NUDGE_OUT") ?? "nudge-strips";
        System.IO.Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        TestBoot.NewMountain(game, 4242, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        var cam = Camera.main;
        chase.enabled = false;   // (stepped by hand, on the test's own clock)
        var spots = OpenSpots(Course.First + 60f, 1, 70f);
        foreach (float v0 in new[] { 14f, 24f })
        {
            float z = spots[0];
            var rocks = Garden(z, 7);
            const int W = 236, H = 512, Frames = 12;
            var strip = new Texture2D(W * 6, H * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(W, H, 24);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            int taken = 0, first = -1;
            var T = new Tally();
            try
            {
                Ride($"strip v{v0:0}", Slope.Centre(z), z, v0, 62f / v0, rr => 0f, T, true, f =>
                {
                    if (taken >= Frames) return;
                    if (first < 0 && r.Z > z + 9f) first = f;
                    int every = Mathf.Max(1, Mathf.RoundToInt(48f / v0 / Frames * 60f));
                    if (first < 0 || (f - first) % every != 0) return;
                    cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                    strip.SetPixels((taken % 6) * W, (1 - taken / 6) * H, W, H, tex.GetPixels());
                    taken++;
                }, true);
            }
            finally { foreach (var g in rocks) Object.DestroyImmediate(g); }
            cam.targetTexture = null; RenderTexture.active = null;
            strip.Apply();
            string file = $"{outDir}/Rocks_{v0 * 3.6f:0}kmh.png";
            System.IO.File.WriteAllBytes(file, strip.EncodeToPNG());
            Print($"strip {v0 * 3.6f:0} km/h", T);
            Debug.Log($"NUDGE-STRIP {file}: {taken} frames");
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }
}
