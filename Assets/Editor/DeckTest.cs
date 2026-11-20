using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Riding across the lift stations' decks (SkiLift.Stream, Roofs.Deck), over several mountains: for every station,
/// riders come down the line onto its uphill end from the snow, at a range of speeds and on lines across it (down the
/// open strip either side of the chairs, past the column, through the chairs' own path, into the hut and the
/// railing), with and without a jump tapped at the far edge, and with the chairs at different places round their loop.
/// Each ride's outcome is logged (DECK-RIDE): how it came onto the deck, whether it crossed it, what it hit, how high
/// the edge stood over the snow, the air off it (time, height, distance) and the landing (how hard, how steep, on
/// what). A summary per kind of station and line (DECK-SUMMARY), and a FAIL (DECK-FAIL) for anything unfair: tripped
/// at the deck's uphill lip, hit by a chair or the station anywhere on the open strips, or a fair landing off the
/// edge that crashes on something hidden.
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod DeckTest.Run -logFile deck.log
/// ($DECK_SEEDS mountains, default 4.) Shots: DeckTest.Shots with the GPU (no -nographics, no -quit): a rider onto a
/// deck, across it past the column, and off its edge, with and without a jump, day and night ($DECK_OUT).
/// </summary>
public static class DeckTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static SkiLift lifts; static MethodInfo liftUpd, chairsM; static FieldInfo liftRefresh;
    static int n, fails;
    static FieldInfo graceField;
    static double clock0;

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);

    static void Setup()
    {
        TestPrefs.Pin();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        lifts = Object.FindFirstObjectByType<SkiLift>();
        liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        liftRefresh = typeof(SkiLift).GetField("refreshClock", NP);
        chairsM = typeof(SkiLift).GetMethod("Chairs", NP);
        // (the chairs spare a rider only for a moment after a cable or a ramp, timed by Time.time, which batchmode
        // leaves at 0: without this they would never hit anyone here)
        graceField = typeof(CableGrind).GetField("graceUntil", BindingFlags.NonPublic | BindingFlags.Static);
        dress = Object.FindFirstObjectByType<CourseDressing>();
        r.Crash += why => { if (cur != null && cur.crash == "") { cur.crash = why; cur.crashAt = r.Body.position; cur.crashOnDeck = cur.onDeck && !cur.left; } };
        r.Landed += imp => { if (cur != null && cur.left && cur.landImpact < 0f) { cur.landImpact = imp; cur.landAngle = r.LastLandAngle; } };
    }

    static void NewMountain(int seed)
    {
        TestBoot.NewMountain(game, seed, terrain, props);
        if (dress != null) dress.Rebuild();
        lifts.Clear();
    }

    /// <summary>Everything round the rider as the game's Update would build it (batchmode calls none).</summary>
    static void Stream()
    {
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm();
        typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
        liftRefresh.SetValue(lifts, 0f);
        liftUpd.Invoke(lifts, null);
        // (what edit mode would not Destroy: stations streamed out, lingering. Put away for real)
        foreach (Transform t in lifts.transform)
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (c != null && c.name == "LiftStation" && (c.position - r.transform.position).sqrMagnitude > 260f * 260f) Object.DestroyImmediate(c.gameObject);
        Physics.SyncTransforms();
    }

    // ================================================================ the rides

    class Ride
    {
        public string crash = ""; public Vector3 crashAt; public bool crashOnDeck;
        public bool onDeck, left, popped, chairHit; public float onAt = -1f, leftAt = -1f, edgeOver, peak, landImpact = -1f, landAngle, landAt, flown;
        public Vector3 leftFrom, landPos, crashLocal; public float speedOn, speedOff, maxStepUp;
    }
    static Ride cur;

    struct Station { public LiftLine.Lift l; public bool top; public Vector3 o, z, x; }

    /// <summary>A station's frame: its deck's middle, its own z (along the line, from the uphill end toward the far one
    /// as the rider rides it) and x across.</summary>
    static Station StationOf(LiftLine.Lift l, bool top)
    {
        var st = new Station { l = l, top = top, o = top ? l.top : l.bottom };
        st.z = l.dir;                                 // the rider rides down the line across both (a top station from its back)
        st.x = new Vector3(st.z.z, 0f, -st.z.x);
        return st;
    }

    public static void Run()
    {
        Setup();
        int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("DECK_SEEDS"), out int sv) ? sv : 4;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stats = new Dictionary<string, List<Ride>>();
        var edges = new List<string>();
        try
        {
            for (int k = 0; k < seeds; k++)
            {
                int seed = 8200 + k * 523;
                NewMountain(seed);
                foreach (var l in LiftLine.Lifts)
                    foreach (bool top in new[] { false, true })
                    {
                        var st = StationOf(l, top);
                        string kind = top ? "top" : "bottom";
                        // how high its far (downhill) edge stands over the snow, across the open strips
                        float W = LiftLine.StationHalfWid, L = LiftLine.StationHalfLen;
                        float edgeMin = float.MaxValue, edgeMax = float.MinValue;
                        foreach (float x in new[] { -4.7f, 4.7f })
                        {
                            Vector3 e = st.o + st.z * (L + 0.3f) + st.x * x;
                            float over = st.o.y - Slope.DrawnHeight(e.x, e.z);
                            edgeMin = Mathf.Min(edgeMin, over); edgeMax = Mathf.Max(edgeMax, over);
                        }
                        Vector3 e0 = st.o - st.z * (L + 0.3f);
                        edges.Add($"seed{seed} lift {l.k}{(l.second ? "b" : "")} {kind}: far edge {edgeMin:0.0}-{edgeMax:0.0} m over the snow, uphill edge {st.o.y - Slope.DrawnHeight(e0.x, e0.z):0.0} m");
                        int phase = 0;
                        foreach (float c in new[] { 4.7f, -4.7f, 0f, 2.6f })
                            foreach (float v0 in new[] { 6f, 12f, 18f, 26f })
                                foreach (bool pop in new[] { false, true })
                                {
                                    if (c == 0f || c == 2.6f) { if (pop) continue; }
                                    clock0 = 1000.0 + 7.3 * phase++ + seed;   // (the chairs somewhere else round their loop each time)
                                    var w = RideAcross(st, c, v0, pop);
                                    string line = c == 0f ? "past the column" : c == 2.6f ? "the chairs' path" : "the open strip";
                                    string key = $"{kind} station, {line}{(pop ? ", jump tapped at the edge" : "")}";
                                    if (!stats.TryGetValue(key, out var list)) stats[key] = list = new List<Ride>();
                                    list.Add(w);
                                    string what = Describe(w);
                                    string tag = $"seed{seed} lift {l.k}{(l.second ? "b" : "")} {kind} x={c:+0.0;-0.0;0} {v0:0} m/s{(pop ? " +jump" : "")}";
                                    string rule = Judge(st, c, w);
                                    if (rule != null) { fails++; Debug.Log($"DECK-FAIL {tag}: {what} :: {rule}"); }
                                    else Debug.Log($"DECK-RIDE {tag}: {what}");
                                }
                    }
            }
        }
        catch (System.Exception e) { Debug.LogError("DECK exception " + e); fails++; }
        var sb = new System.Text.StringBuilder();
        var keys = new List<string>(stats.Keys); keys.Sort();
        foreach (var key in keys) sb.Append("\n  " + key + ": " + Sum(stats[key]));
        sb.Append("\n  decks' edges:"); foreach (var e in edges) sb.Append("\n    " + e);
        Debug.Log($"DECK-SUMMARY {fails} unfair in {sw.Elapsed.TotalSeconds:0} s{sb}");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        SkiLift.TestClock = -1.0;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static string Sum(List<Ride> rs)
    {
        int crossed = 0, left = 0, landed = 0; var crashes = new Dictionary<string, int>();
        float air = 0f, airMax = 0f, peak = 0f, peakMax = 0f, dist = 0f, imp = 0f, impMax = 0f;
        foreach (var w in rs)
        {
            if (w.onDeck) crossed++;
            if (w.left) { left++; air += w.landAt - w.leftAt; airMax = Mathf.Max(airMax, w.landAt - w.leftAt); peak += w.peak; peakMax = Mathf.Max(peakMax, w.peak); dist += w.flown; }
            if (w.landImpact >= 0f) { landed++; imp += w.landImpact; impMax = Mathf.Max(impMax, w.landImpact); }
            if (w.crash != "") { string k = w.crash + (w.crashOnDeck ? " on the deck" : w.left ? " after the edge" : " before the deck"); crashes.TryGetValue(k, out int c); crashes[k] = c + 1; }
        }
        var cs = new System.Text.StringBuilder();
        foreach (var kv in crashes) cs.Append($"{kv.Value} {kv.Key}; ");
        return $"{rs.Count} rides, {crossed} onto the deck, {left} off its edge, {landed} landed (air {air / Mathf.Max(1, left):0.00} s mean / {airMax:0.00} most; " +
               $"{peak / Mathf.Max(1, left):0.0} m over the snow mean / {peakMax:0.0} most; {dist / Mathf.Max(1, left):0} m flown mean; into the snow {imp / Mathf.Max(1, landed):0.0} m/s mean / {impMax:0.0} most); crashes: {(cs.Length == 0 ? "none" : cs.ToString())}";
    }

    static string Describe(Ride w)
    {
        var s = new System.Text.StringBuilder();
        s.Append(w.onDeck ? $"onto the deck at {w.speedOn:0.0} m/s (step up {w.maxStepUp:0.00} m)" : "never on the deck");
        if (w.left) s.Append($", off its edge at {w.speedOff:0.0} m/s {w.edgeOver:0.0} m over the snow{(w.popped ? " with a jump" : "")}: {w.landAt - w.leftAt:0.00} s of air, {w.peak:0.0} m up at most, {w.flown:0} m flown");
        if (w.landImpact >= 0f) s.Append($", landed {w.landImpact:0.0} m/s into the snow at {w.landAngle:0} deg");
        if (w.crash != "") s.Append($", crashed: {w.crash}{(w.crashOnDeck ? " on the deck" : w.left ? " after the edge" : " before the deck")} at ({w.crashLocal.x:0.0}, {w.crashLocal.y:+0.0;-0.0}, {w.crashLocal.z:0.0})");
        return s.ToString();
    }

    /// <summary>The rules: null if this ride kept them.</summary>
    static string Judge(Station st, float c, Ride w)
    {
        // (judged by where the board was: the open strip is 4.3 to 5.0 m out from the line, clear of the chairs' swing
        // and of the railing; a rider who has drifted into either meets it fairly. The hut stands at one end of one
        // strip: riding into it is fair)
        bool strip = Mathf.Abs(c) > 4f, inStrip = Mathf.Abs(w.crashLocal.x) > 4.3f && Mathf.Abs(w.crashLocal.x) < 5.0f;
        if (w.crash == "Chairlift!" && strip && inStrip) return "a chair hit the rider on the open strip";
        if (strip && inStrip && w.crashOnDeck && w.crash == "Lift station" && !HutAhead(st, c)) return "the station stopped a rider on the open strip";
        if (w.onDeck && w.maxStepUp > 0.45f) return "a step up onto the deck's uphill end";
        return null;
    }

    /// <summary>Is the hut on this line? (The hut stands on the station's own +x, near its own +z end.)</summary>
    static bool HutAhead(Station st, float c)
    {
        // the station's own frame: a bottom one faces up the line (its x is -across), a top one down it (+across)
        float ownX = st.top ? c : -c;
        return ownX > 3.5f;
    }

    /// <summary>One ride: from the snow 14 m up the line from the deck's uphill end, at v0, on the line c across it.</summary>
    static Ride RideAcross(Station st, float c, float v0, bool pop)
    {
        float L = LiftLine.StationHalfLen;
        Vector3 start = st.o - st.z * (L + 14f) + st.x * c;
        r.ResetAt(start.x, start.z); body.ResetVisual();
        SkiLift.TestClock = clock0;
        Stream();
        r.Begin();
        float hd = Mathf.Atan2(st.z.x, st.z.z);
        r.Heading = Mathf.Clamp(hd, -1.45f, 1.45f);
        Vector3 n0 = Slope.Normal(start.x, start.z);
        r.Body.linearVelocity = Vector3.ProjectOnPlane(st.z, n0).normalized * v0;
        graceField.SetValue(null, -1f);   // (see Setup)
        var w = new Ride(); cur = w;
        float t = 0f, dt = Time.fixedDeltaTime, afterCrash = 0f, lastY = r.Body.position.y;
        while (t < 7f)
        {
            Vector3 p = r.Body.position;
            float along = Vector3.Dot(p - st.o, st.z), across = Vector3.Dot(p - st.o, st.x);
            // hold the line across the deck (a rider's own small corrections), straight down the line
            r.Steer = Mathf.Clamp((c - across) * 0.7f - Mathf.DeltaAngle(hd * Mathf.Rad2Deg, Mathf.Atan2(r.Velocity.x, r.Velocity.z) * Mathf.Rad2Deg) * 0.06f, -1f, 1f);
            bool on = r.OnRoof != null && r.OnRoof.s == Roofs.Deck;
            if (pop && on && !w.popped && along > L - 1.2f) { r.QueuePop(Rider.TapPop); w.popped = true; }
            SkiLift.TestClock = clock0 + t;
            Step();
            if (n % 6 == 0) { liftRefresh.SetValue(lifts, 0f); liftUpd.Invoke(lifts, null); }
            else chairsM.Invoke(lifts, new object[] { r.Body.position });   // (a chair hit is found every step)
            t += dt;
            p = r.Body.position;
            bool onNow = r.OnRoof != null && r.OnRoof.s == Roofs.Deck;
            if (onNow && !w.onDeck) { w.onDeck = true; w.onAt = t; w.speedOn = r.Speed; }
            if (onNow && !r.Airborne) w.maxStepUp = Mathf.Max(w.maxStepUp, (p.y - lastY) - Mathf.Max(0f, 0f));
            if (w.onDeck && !w.left && r.Airborne && !onNow && Vector3.Dot(p - st.o, st.z) > L - 0.5f)
            {
                w.left = true; w.leftAt = t; w.leftFrom = p; w.speedOff = r.Velocity.magnitude;
                w.edgeOver = p.y - Rider.R - Slope.DrawnHeight(p.x, p.z);
            }
            if (w.left && w.landImpact < 0f && r.Airborne) w.peak = Mathf.Max(w.peak, r.AboveGround);
            if (w.left && w.landAt <= 0f && (!r.Airborne || r.Crashed)) { w.landAt = t; w.landPos = p; w.flown = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(w.leftFrom.x, 0f, w.leftFrom.z)); }
            lastY = p.y;
            if (r.Crashed) { afterCrash += dt; if (afterCrash > 0.2f) break; }
            if (w.landAt > 0f && t > w.landAt + 0.4f) break;
        }
        if (w.left && w.landAt <= 0f) { w.landAt = t; w.flown = Vector3.Distance(new Vector3(r.Body.position.x, 0f, r.Body.position.z), new Vector3(w.leftFrom.x, 0f, w.leftFrom.z)); }
        if (w.crash != "") w.crashLocal = new Vector3(Vector3.Dot(w.crashAt - st.o, st.x), w.crashAt.y - st.o.y, Vector3.Dot(w.crashAt - st.o, st.z));
        cur = null;
        return w;
    }

    /// <summary>One physics step as the game runs it, the obstacle contact found by hand (batchmode sends no collision
    /// messages): the board's sweep this step, and what PhysX itself stopped.</summary>
    static void Step()
    {
        float realDt = Time.fixedDeltaTime / Mathf.Max(Time.timeScale, 1e-4f);
        game.TestTickHitStop(realDt);
        float dt = Time.fixedDeltaTime;
        r.Tick(dt);
        if (!r.Crashed && r.Running)
        {
            Vector3 p = r.Body.position, v = r.Velocity;
            foreach (var c in Physics.OverlapCapsule(p, p + v * dt, Rider.R + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.attachedRigidbody == r.Body) continue;
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob == null || r.InvincibleTime > 0f || r.Invulnerable) continue;
                Vector3 to = c.ClosestPoint(p) - p;
                float into = to.sqrMagnitude > 1e-6f ? Vector3.Dot(v, to.normalized) : v.magnitude;
                if (into > 6f || ob.always) { r.DoCrash(ob.label); break; }
            }
        }
        Vector3 vSet = r.Body.linearVelocity;
        body.PhysicsStep(); Physics.Simulate(dt);
        if (!r.Crashed && r.Running && r.InvincibleTime <= 0f && !r.Invulnerable && (r.Body.linearVelocity - vSet).magnitude > 6f)
            foreach (var c in Physics.OverlapSphere(r.Body.position, Rider.R + 0.06f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.attachedRigidbody == r.Body) continue;
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob != null) { r.DoCrash(ob.label); break; }
            }
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 2 == 0 || r.Crashed) body.Apply(r.Crashed ? dt : 2f * dt);
        n++;
    }

    // ================================================================ building lifts a piece a frame

    /// <summary>
    /// SkiLift building a lift a few pieces a frame, its ramp's collider baked off the main thread (SpreadBuild, as on
    /// the phone), against all at once (as the batch tools have it): down a whole mountain in 25 m steps, one lift
    /// Update a step, the longest Update each way; and that with spreading on, every lift in range still ends up with
    /// all its cable spans, its ramp and the ramp's collider. Log: DECK-SPREAD.
    /// </summary>
    public static void Spread()
    {
        Setup();
        var spans = typeof(SkiLift).GetField("live", NP);
        foreach (bool spread in new[] { false, true, false, true })
        {
            NewMountain(8200);
            SkiLift.SpreadBuild = spread;
            double worst = 0, sum = 0, worstRide = 0; int calls = 0; float worstZ = 0f;
            var sw = new System.Diagnostics.Stopwatch();
            for (float z = Course.First; z < Course.First + 3f * Course.Cycle; z += 25f)
            {
                r.ResetAt(Slope.Centre(z), z);
                terrain.Prewarm(); props.Prewarm();
                liftRefresh.SetValue(lifts, 0f);
                sw.Restart(); liftUpd.Invoke(lifts, null); sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds; sum += ms; calls++;
                if (ms > worst) { worst = ms; worstZ = z; }
                if (z > Course.First + 100f && ms > worstRide) worstRide = ms;
                // (and the frames after it, as the game would run them: what is left to build goes on there)
                for (int f = 0; f < 6; f++) { sw.Restart(); liftUpd.Invoke(lifts, null); sw.Stop(); ms = sw.Elapsed.TotalMilliseconds; sum += ms; calls++; if (ms > worst) { worst = ms; worstZ = z; } if (z > Course.First + 100f && ms > worstRide) worstRide = ms; }
            }
            // let the last bakes land, then count what is missing
            for (int f = 0; f < 60; f++) { System.Threading.Thread.Sleep(5); liftUpd.Invoke(lifts, null); }
            int missingSpans = 0, ramps = 0, rampCols = 0;
            foreach (var lv in (System.Collections.IEnumerable)spans.GetValue(lifts))
            {
                var spanArr = (GameObject[])lv.GetType().GetField("span").GetValue(lv);
                var l = (LiftLine.Lift)lv.GetType().GetField("lift").GetValue(lv);
                for (int i = 0; i < spanArr.Length; i++)
                {
                    float za = l.top.z + l.dir.z * l.nodeS[i], zb = l.top.z + l.dir.z * l.nodeS[i + 1];
                    if (spanArr[i] == null && Mathf.Max(za, zb) > r.Z - 150f && Mathf.Min(za, zb) < r.Z + 650f) missingSpans++;
                }
                var root = (GameObject)lv.GetType().GetField("root").GetValue(lv);
                foreach (Transform t in root.transform) if (t.name == "LaunchRamp") { ramps++; if (t.GetComponent<MeshCollider>() != null) rampCols++; }
            }
            Debug.Log($"DECK-SPREAD {(spread ? "a few pieces a frame" : "all at once")}: the longest lift Update {worst:0.00} ms (at z={worstZ:0}; past the first 100 m, {worstRide:0.00} ms), mean {sum / calls:0.000} ms over {calls}; at the end {missingSpans} cable spans in range not built, {ramps} ramps, {rampCols} with their collider");
        }
        SkiLift.SpreadBuild = false;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    // ================================================================ renders

    static Camera cam; static DayNight dayNight; static Atmosphere atmo;
    static readonly List<System.Action> steps = new List<System.Action>();
    static int at;

    public static void Shots()
    {
        string outDir = System.Environment.GetEnvironmentVariable("DECK_OUT") ?? "DeckShots";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);
        Setup();
        dayNight = F<DayNight>("dayNight"); atmo = Object.FindFirstObjectByType<Atmosphere>();
        cam = Camera.main; cam.GetComponent<ChaseCamera>().enabled = false;
        // a bottom station whose far edge stands well over the snow, on the first mountains that have one
        Station st = default; bool found = false;
        for (int k = 0; k < 6 && !found; k++)
        {
            NewMountain(8200 + k * 523);
            foreach (var l in LiftLine.Lifts)
            {
                var s = StationOf(l, false);
                Vector3 e = s.o + s.z * (LiftLine.StationHalfLen + 0.3f) + s.x * 4.7f;
                if (s.o.y - Slope.DrawnHeight(e.x, e.z) > 3f) { st = s; found = true; break; }
            }
        }
        if (!found) { Debug.LogError("DECK shots: no station"); EditorApplication.Exit(1); return; }
        const float c = 4.7f, v0 = 16f;
        clock0 = 1200.0;
        // the rides once each, to find their moments
        int tOn = -1, tMid = -1, tOff = -1, tOffPop = -1;
        foreach (bool pop in new[] { false, true })
        {
            var w = RideAcross(st, c, v0, pop);
            Debug.Log($"DECK shots ride{(pop ? " +jump" : "")}: {Describe(w)}");
            int dtSteps(float s) => Mathf.RoundToInt(s / Time.fixedDeltaTime);
            if (!pop) { tOn = dtSteps(w.onAt) + 3; tMid = dtSteps(w.onAt + 0.45f * (LiftLine.StationHalfLen * 2f) / Mathf.Max(4f, w.speedOn)); tOff = dtSteps(w.leftAt + 0.35f); }
            else tOffPop = dtSteps(w.leftAt + 0.35f);
        }
        var moments = new List<(string name, int step, bool pop)> { ("1-onto-the-deck", tOn, false), ("2-past-the-column", tMid, false), ("3-off-the-edge", tOff, false), ("4-off-the-edge-jump", tOffPop, true) };
        Vector3 mid = st.o + Vector3.up * 1f;
        // (from the rider's own side, out past the railing, a little down the line)
        Vector3 sideEye = st.o + st.x * (Mathf.Sign(c) * 15f) + st.z * 9f; sideEye.y = Mathf.Max(st.o.y + 1.5f, Slope.DrawnHeight(sideEye.x, sideEye.z) + 2f);
        steps.Clear(); at = 0;
        steps.Add(() => { TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); Pose(st, c, v0, false, 2, sideEye, mid); Shoot(Path.Combine(outDir, "warmup.png")); });
        foreach (var (when, clk) in new[] { ("day", 1.4f), ("night", 3.4f) })
            foreach (var m in moments)
                steps.Add(() =>
                {
                    TimeOfDay.TestClock = clk; dayNight.ApplyNow();
                    Pose(st, c, v0, m.pop, m.step, sideEye, mid);
                    Shoot(Path.Combine(outDir, $"deck-{m.name}-{when}.png"));
                    Vector3 rp = r.transform.position, vv = r.Velocity; vv.y = 0f;
                    Vector3 back = vv.sqrMagnitude > 1f ? vv.normalized : st.z;
                    Vector3 e2 = rp - back * 8f + Vector3.up * 3.4f;
                    e2.y = Mathf.Max(e2.y, Slope.DrawnHeight(e2.x, e2.z) + 1.5f);
                    cam.transform.SetPositionAndRotation(e2, Quaternion.LookRotation(rp + back * 5f + Vector3.down * 1f - e2));
                    Shoot(Path.Combine(outDir, $"deck-{m.name}-{when}-chase.png"));
                });
        steps.Add(() =>
        {
            TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow(); TimeOfDay.TestClock = -1f;
            SkiLift.TestClock = -1.0;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            QualitySettings.SetQualityLevel(quality, false);
            TestPrefs.Restore();
        });
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (at < steps.Count)
        {
            try { steps[at++](); }
            catch (System.Exception e) { Debug.LogError("DECK shots exception " + e); at = steps.Count; }
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    /// <summary>The ride again, to its `stepsTo`th step, and the camera set.</summary>
    static void Pose(Station st, float c, float v0, bool pop, int stepsTo, Vector3 eye, Vector3 look)
    {
        float L = LiftLine.StationHalfLen;
        Vector3 start = st.o - st.z * (L + 14f) + st.x * c;
        r.ResetAt(start.x, start.z); body.ResetVisual();
        SkiLift.TestClock = clock0;
        Stream();
        r.Begin();
        float hd = Mathf.Atan2(st.z.x, st.z.z);
        r.Heading = Mathf.Clamp(hd, -1.45f, 1.45f);
        r.Body.linearVelocity = Vector3.ProjectOnPlane(st.z, Slope.Normal(start.x, start.z)).normalized * v0;
        bool popped = false;
        for (int i = 0; i <= stepsTo; i++)
        {
            Vector3 p = r.Body.position;
            float along = Vector3.Dot(p - st.o, st.z), across = Vector3.Dot(p - st.o, st.x);
            r.Steer = Mathf.Clamp((c - across) * 0.7f - Mathf.DeltaAngle(hd * Mathf.Rad2Deg, Mathf.Atan2(r.Velocity.x, r.Velocity.z) * Mathf.Rad2Deg) * 0.06f, -1f, 1f);
            if (pop && !popped && r.OnRoof != null && along > L - 1.2f) { r.QueuePop(Rider.TapPop); popped = true; }
            SkiLift.TestClock = clock0 + i * Time.fixedDeltaTime;
            Step();
        }
        body.Apply(0f);
        Debug.Log($"DECK shot at step {stepsTo}: rider {r.transform.position} air={r.Airborne} deck={(r.OnRoof != null)} crashed={r.Crashed} v={r.Velocity.magnitude:0.0} above={r.AboveGround:0.0}");
        cam.fieldOfView = 62f;
        // (looking at the rider, a little toward the deck)
        Vector3 aim = Vector3.Lerp(r.transform.position, look, 0.25f);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
        liftRefresh.SetValue(lifts, 0f);
        for (int i = 0; i < 3; i++) liftUpd.Invoke(lifts, null);
        typeof(SlopeStreamer).GetMethod("Update", NP)?.Invoke(terrain, null);
        typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear(true);
    }

    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        typeof(Atmosphere).GetMethod("LateUpdate", NP)?.Invoke(atmo, null);
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; hidden.Add(sk); baked.Add(g);
        }
        int w = 886, h = 1920;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }
}
