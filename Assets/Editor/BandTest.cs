using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The cliff band traverses (CliffBand), on six mountains: their shape along the run, and rides along them.
///  - Shape: the heading along the traverse, the bench's pitch along the run and its tilt across it, the band's drop
///    below the downhill edge (tall, and at its low places), the gap's pitch, the groomed width square to the run, and
///    that no tree stands on the bench and the ground below the tall band is Deadly.Below.
///  - Rides: the real rider carving the run's line (a bot steering its edge for the line ahead) from 60 m above the
///    turn in to 60 m below the gap at 15, 22 and 30 m/s: how far it stays on the bench, whether it goes over the
///    band, its lowest speed, and how much edge into the hill the traverse took. Judged: no crash and on the bench the
///    whole traverse. (The bench is cut level across and pitched along the run, so gravity on it runs along it: the
///    edge into the hill is what carves the board onto the bench at the way in, and little is needed to stay on it.)
///  - Hands off (carve: there is no line to hold; gravity and the board's own way have it): the board must NOT stay on
///    the run. Judged: it is in danger (at the band's edge, over it, or down) within a few seconds of the turn in.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod BandTest.Run -logFile band.log
/// Shots (with the GPU): -executeMethod BandTest.Shots (BAND_OUT=dir). Log lines: BAND, BAND-FAIL, BAND done.
/// </summary>
public static class BandTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("BAND_SEEDS") ?? "1000,1077,1154,1231,1308,2131").Split(','), int.Parse);
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static string lastCrash = "";
    static int fails, bands, rides;
    static bool judgeRides = System.Environment.GetEnvironmentVariable("BAND_JUDGE_RIDES") != "0";

    static void Fail(string s) { fails++; Debug.Log("BAND-FAIL " + s); }

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
    }

    static void NewMountain(int seed)
    {
        TestBoot.NewMountain(game, seed, terrain, props);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) { var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt; dress.Prewarm(); }
    }

    static float Deg(float rise, float run) => Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

    /// <summary>A point on the run's middle, and the unit vectors along the run and square across it (toward downhill).</summary>
    static void Frame(float z, out Vector3 p, out Vector3 along, out Vector3 down)
    {
        float k = (Slope.Centre(z + 0.5f) - Slope.Centre(z - 0.5f));
        along = new Vector3(k, 0f, 1f).normalized;
        down = new Vector3(-along.z * Mathf.Sign(k + 1e-6f), 0f, along.x * Mathf.Sign(k + 1e-6f));
        if (down.z < 0f) down = -down;
        p = new Vector3(Slope.Centre(z), 0f, z); p.y = Slope.Height(p.x, p.z);
    }

    static float H(Vector3 p) => Slope.Height(p.x, p.z);

    public static void Run()
    {
        Setup();
        try
        {
            foreach (int seed in Seeds)
            {
                NewMountain(seed);
                int n = 0;
                for (int c = 0; c < 8; c++)
                {
                    if (!CliffBand.Of(c, out var b)) continue;
                    n++; bands++;
                    Shape(seed, b);
                    foreach (float v0 in new[] { 15f, 22f, 30f }) Ride(seed, b, v0, null);
                    Ride(seed, b, 22f, null, hands: true);
                    if (!b.ledge && b.l0 > 0f) Ride(seed, b, 22f, null, drop: true);
                    foreach (float dr in new[] { -1f, 1f }) Drag(seed, b, 22f, dr);
                }
                Debug.Log($"BAND seed{seed}: {n} band(s); the line swung {CliffBand.Swing(Slope.FinishZ):+0;-0} m by the finish");
            }
        }
        catch (System.Exception e) { Fail("exception " + e); }
        Debug.Log($"BAND done: {bands} bands, {rides} rides, {fails} failure(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    static void Shape(int seed, CliffBand.Band b)
    {
        if (System.Environment.GetEnvironmentVariable("BAND_PROFILE") == "1")
        {
            float zm = (b.z1 + b.z2) * 0.5f;
            Frame(zm, out var pm, out var am, out var dm);
            var sb = new System.Text.StringBuilder($"BAND-PROFILE seed{seed} z={zm:0} heading {Slope.RunHeading(zm) * Mathf.Rad2Deg:0} down=({dm.x:0.00},{dm.z:0.00}) across(+down)/height-rel/rough/deadly:");
            for (float s2 = -50f; s2 <= 80f; s2 += 2.5f)
            {
                Vector3 q = pm + dm * s2;
                sb.Append($" {s2:0}:{H(q) - H(pm):0.0}/{Slope.Across(q.x, q.z):0}/{(Deadly.Below(q.x, q.z) ? "D" : "")}");
            }
            Debug.Log(sb.ToString());
        }
        float maxHead = 0f, minPitch = 90f, maxPitch = 0f, maxTilt = 0f, minTall = 999f, maxTall = 0f, lowest = 999f, gapPitch = 0f, minWidth = 999f;
        int trees = 0, notDeadly = 0, samples = 0;
        for (float z = b.z0; z < b.z3; z += 3f)
        {
            Frame(z, out var p, out var along, out var down);
            maxHead = Mathf.Max(maxHead, Mathf.Abs(Slope.RunHeading(z)) * Mathf.Rad2Deg);
            if (z < b.z1 + 5f || z > b.z2 - 5f)
            {
                if (z > b.z2) { Vector3 q = p + along * 4f; gapPitch = Mathf.Max(gapPitch, Deg(H(p) - H(q), 4f)); }
                continue;
            }
            samples++;
            Vector3 ahead = p + along * 4f;
            float pitch = Deg(H(p) - H(ahead), 4f);
            minPitch = Mathf.Min(minPitch, pitch); maxPitch = Mathf.Max(maxPitch, pitch);
            float half = Slope.Half(z), edge = CliffBand.EdgeAt(z);
            Vector3 l = p - down * (half - 2f), rr = p + down * (half - 2f);
            maxTilt = Mathf.Max(maxTilt, Mathf.Abs(Deg(H(l) - H(rr), 2f * (half - 2f))));
            // the band's drop: from its lip to well below its face
            // the band's drop: its lip to its foot, less the slope's own fall over the face's width (its own height here)
            float hz = CliffBand.HeightAt(b, z), wF = 0.45f * Mathf.Max(3f, hz);
            Vector3 lip = p + down * (edge + 0.1f), foot = p + down * (edge + wF + 0.3f);
            float drop = H(lip) - H(foot);   // (lip to foot: the face, and the slope's own fall over its width)
            bool low = CliffBand.Low(b, z);
            bool tall = hz >= 0.95f * b.height;   // (not where it eases into a low place or out at its ends)
            if (low && hz < b.lowH + 0.5f) lowest = Mathf.Min(lowest, drop);
            else if (tall) { minTall = Mathf.Min(minTall, drop); maxTall = Mathf.Max(maxTall, drop); }
            // the groomed width square to the run
            float w = 0f;
            for (float s = -40f; s <= 40f; s += 1f) { Vector3 q = p + down * s; if (Slope.Rough(q.x, q.z) <= 0.01f) w += 1f; }
            minWidth = Mathf.Min(minWidth, w);
            if (tall && hz > 14f && z > b.z1 + 10f && z < b.z2 - 10f)
            {
                Vector3 below = p + down * (edge + wF + 6f);
                if (!Deadly.Below(below.x, below.z)) { notDeadly++; if (notDeadly <= 2) Debug.Log($"BAND-NOTDEADLY z={z:0} hz={hz:0.0} across {Slope.Across(below.x, below.z):0.0} weight {CliffBand.Weight(below.z):0.00}"); }
            }
            for (float s = -(half - 1f); s <= half - 1f; s += 2f)
            { Vector3 q = p + down * s; if (Course.Keep(q.x, q.z, 0f) == false && Slope.Rough(q.x, q.z) > 0.5f) trees++; }
        }
        float gapW = 999f, wideW = 0f;
        for (float z = b.z2; z < b.z3 + 200f; z += 4f)
        {
            Frame(z, out var p2, out var a2, out var d2);
            float w2 = 0f; for (float s2 = -60f; s2 <= 60f; s2 += 1f) { Vector3 q = p2 + d2 * s2; if (Slope.Rough(q.x, q.z) <= 0.01f) w2 += 1f; }
            if (z < b.z3) gapW = Mathf.Min(gapW, w2); else wideW = Mathf.Max(wideW, w2);
        }
        Debug.Log($"BAND seed{seed} cycle{b.cycle}: widths: gap {gapW:0} m, below it up to {wideW:0} m");
        Debug.Log($"BAND seed{seed} cycle{b.cycle}: {(b.ledge ? "ANGLED LEDGE" : "BAND")} {(b.side > 0 ? "right" : "left")}, z {b.z0:0}-{b.z3:0}, traverse {(b.z2 - b.z1) / Mathf.Cos(Mathf.Atan(b.k)):0} m along the run at up to {maxHead:0} deg, the line moved {b.D:0} m; bench pitch {minPitch:0}-{maxPitch:0} deg, tilt across up to {maxTilt:0} deg, width {minWidth:0} m; band {minTall:0}-{maxTall:0} m tall, {lowest:0.0} m at its low places ({(b.l1 > 0f ? 2 : 1)}); gap up to {gapPitch:0} deg; {notDeadly}/{samples} tall samples not deadly below; {trees} rough spots on the bench");
        // the rope line along the bench's edge (PisteKit), and the double black at the way in (TrailSigns)
        int ropePoles = 0, edgePoles = 0;
        foreach (var it in PisteKit.Items)
        {
            if (it.foot.z < b.z1 - 12f || it.foot.z > b.z2 + 16f) continue;
            if (it.kind == PisteKit.Kind.RopePole) ropePoles++;
            if (it.kind == PisteKit.Kind.EdgePole && it.foot.z > b.z0 && it.foot.z < b.z3) edgePoles++;
        }
        bool sign = false;
        foreach (var sg in TrailSigns.Signs) if (sg.symbol == TrailSigns.Symbol.DoubleBlack && Mathf.Abs(sg.z - (b.z0 - 30f)) < 12f) sign = true;
        Debug.Log($"BAND seed{seed} cycle{b.cycle}: {ropePoles} rope poles along the bench's edge, {edgePoles} piste edge poles on the traverse, double black at the way in: {sign}");
        if (minTall > 6f && ropePoles < 4) Fail($"seed{seed} cycle{b.cycle}: only {ropePoles} rope poles along the band");
        if (edgePoles > 0) Fail($"seed{seed} cycle{b.cycle}: {edgePoles} piste edge poles on the band's traverse (its bench has its rope)");
        if (!sign) Fail($"seed{seed} cycle{b.cycle}: no double black at the way in");
        if (maxHead < (b.ledge ? 28f : 55f)) Fail($"seed{seed} cycle{b.cycle}: the traverse only turns {maxHead:0} deg");
        if (maxPitch > (b.ledge ? 36f : 28f) || minPitch < 8f) Fail($"seed{seed} cycle{b.cycle}: the bench's pitch along the run is {minPitch:0}-{maxPitch:0} deg");
        if (maxTilt > 10f) Fail($"seed{seed} cycle{b.cycle}: the bench tilts {maxTilt:0} deg across");
        if (minTall < 0.85f * (b.ledge ? 45f : 15f)) Fail($"seed{seed} cycle{b.cycle}: the band is only {minTall:0} m tall in places");
        if (!b.ledge && (lowest > 11f || lowest < 3.5f)) Fail($"seed{seed} cycle{b.cycle}: its low place drops {lowest:0.0} m");
        float wantW = b.ledge ? 13f : 30f;
        if (minWidth < wantW || (b.ledge && minWidth > 20f)) Fail($"seed{seed} cycle{b.cycle}: the groomed run is {minWidth:0} m wide square to it");
        if (notDeadly > 0) Fail($"seed{seed} cycle{b.cycle}: {notDeadly} places below the tall band not marked deadly");
    }

    /// <summary>(bandfix2) How far out past the band's edge, at the low place, the optional air is aimed: far enough that the board
    /// crosses the edge across it rather than slicing along it (aimed 2 m out, it left nearly along the band and came down on
    /// the face at up to 60 deg). BAND_DROPOUT to try others.</summary>
    static readonly float DropOut = float.TryParse(System.Environment.GetEnvironmentVariable("BAND_DROPOUT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d_) ? d_ : 10f;

    static void Ride(int seed, CliffBand.Band b, float v0, System.Action<int> frame, bool hands = false, bool drop = false)
    {
        rides++;
        float z0 = b.z0 - 60f, x0 = Slope.Centre(z0);
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * v0;
        lastCrash = ""; terrain.Prewarm(); props.Prewarm();
        { var dress = Object.FindFirstObjectByType<CourseDressing>(); if (dress != null) dress.Prewarm(); }
        float worstOff = 0f, minSpeed = 999f; bool over = false; int n = 0; bool dropping = false, dropped = false;
        float tIn = -1f, tDanger = -1f, edgeIn = 0f, edgeMost = 0f, pullSum = 0f; int onBench = 0;
        for (float t = 0f; t < 30f && r.Z < b.z3 + 60f; t += Dt, n++)
        {
            // the bot: aim at the run's middle 12 m ahead along the run
            float zA = r.Z + 12f * Slope.Squash(r.Z);
            float want = Slope.Centre(zA);
            float lead = Mathf.Atan2(want - r.X, zA - r.Z);
            r.Steer = Rider.Unshape(Mathf.Clamp((lead - r.TravelHeading) * 1.5f + (want - r.X) * 0.02f, -1f, 1f));   // (feel: the input's curve undone, so the bot asks for what it always did)
            if (hands) r.Steer = 0f;   // (hands off the whole way: nothing holds the run's line for it)
            // the optional air: off the downhill side at the first low place, steering hard for it from 12 m before
            // (steer3: a drag is a sideways speed off the run's heading, quick and strong, so held hard from 12 m before
            // it the board reached the edge well short of the low place, over the tall band; the rider aims at the low
            // place's lip instead, a couple of metres out past the edge, from 40 m before it)
            if (drop && r.Z > b.l0 - 40f && !dropped) { dropping = true; }
            if (dropping)
            {
                var lip = CliffBand.PointAt(b, b.l0, CliffBand.EdgeAt(b.l0) + DropOut);
                float aim = Mathf.Atan2(lip.x - r.X, lip.y - r.Z);
                // (bandfix2: asked as the way to be going, through the steering's own mapping (CarveBot.ForSide, steerB's
                // push hold included), not as a heading error through the input's curve undone: that loop reached the edge
                // 17-19 m past the low place at speed, over the tall band)
                Vector3 u = r.Velocity;
                r.Steer = CarveBot.ForSide(r, Mathf.Max(u.z, 1f) * Mathf.Tan(Mathf.Clamp(aim, -1.3f, 1.3f)));
                if (r.Airborne && !dropped) Debug.Log($"BAND seed{seed} cycle{b.cycle} the drop leaves {Vector2.Distance(new Vector2(r.X, r.Z), CliffBand.PointAt(b, b.l0, CliffBand.EdgeAt(b.l0))):0.0} m from the low place's edge, {Mathf.Abs(Slope.Across(r.X, r.Z)) - CliffBand.EdgeAt(r.Z):+0.0;-0.0} m past the edge, going {(r.TravelHeading - Mathf.Atan2(lip.x - r.X, lip.y - r.Z)) * Mathf.Rad2Deg:+0;-0} deg off the aim, {r.Speed * 3.6f:0} km/h, finger {r.Steer:+0.00;-0.00}");
                if (r.Airborne) dropped = true; if (dropped && !r.Airborne) dropping = false;
            }
            // (bandfix2) down off the low place: ridden on as it landed, hands off, to 30 m past it. The bot's own line is
            // the run's middle, up on the bench above the band: steered back toward it, a good landing rode straight into
            // the rock and the trees at the band's foot
            else if (dropped) r.Steer = 0f;
            r.TestContacts(Dt);
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (frame != null) { body.Apply(Dt); frame(n); } else if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
            if (r.Z > b.z0 && tIn < 0f) tIn = t;   // (the way in: where the run begins to swing)
            // the edge into the turn and the hill (away from the band: b.side), from the way in to the end of the bench
            if (r.Z > b.z0 && r.Z < b.z2 && !r.Airborne) edgeMost = Mathf.Max(edgeMost, r.Steer * b.side);
            if (r.Z > b.z1 && r.Z < b.z2)
            {
                float off = Slope.Across(r.X, r.Z);
                worstOff = Mathf.Max(worstOff, Mathf.Abs(off));
                if (Mathf.Abs(off) > CliffBand.EdgeAt(r.Z) + 2f) over = true;
                minSpeed = Mathf.Min(minSpeed, r.Speed);
                // the edge held into the hill (away from the band: b.side), and gravity's pull across the path toward the band
                if (!r.Airborne) { onBench++; edgeIn += r.Steer * b.side; pullSum += -r.SideSlope * b.side; }
                if (tDanger < 0f && Mathf.Abs(off) > CliffBand.EdgeAt(r.Z) - 2f) tDanger = t;
            }
            if (tDanger < 0f && tIn >= 0f && (r.Crashed || over)) tDanger = t;
            if (r.Crashed) break;
            if (hands && tDanger >= 0f && t - tDanger > 2f) break;   // (hands off: seen where it leads)
            if (dropped && !dropping && r.Z > b.l0 + 30f) break;   // (down off the ledge and riding away below the band)
        }
        Vector3 p = r.transform.position;
        if (drop)
        {
            bool deadly = Deadly.Below(p.x, p.z);
            Debug.Log($"BAND seed{seed} cycle{b.cycle} optional air at the low place ({b.lowH:0.0} m): {(dropped ? "flew it" : "never left the bench")}, peak {r.Peak:0.0} m, last landing {r.LastLandImpact:0.0} m/s at {r.LastLandAngle:0} deg, " + (r.Crashed ? $"CRASHED '{lastCrash}'" : $"riding on at {r.Speed * 3.6f:0} km/h") + $", deadly ground {deadly}");
            if (judgeRides && (r.Crashed || !dropped)) Fail($"seed{seed} cycle{b.cycle}: the optional air off the low place {(r.Crashed ? "crashed '" + lastCrash + "'" : "never happened")}");
            if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
            return;
        }
        if (hands)
        {
            // carve: nothing holds the run's line. Hands off, the board carries on the way it was going while the run
            // swings away under it, and gravity along the bench is all that turns it: the band's edge in seconds
            float took = tDanger >= 0f && tIn >= 0f ? tDanger - tIn : -1f;
            Debug.Log($"BAND seed{seed} cycle{b.cycle} ride {v0:0} m/s hands off: furthest off the middle on the traverse {worstOff:0.0} m, " + (took >= 0f ? $"in danger {took:0.0} s after the way in ({(over ? "over the band" : r.Crashed ? "down: '" + lastCrash + "'" : "at the band's edge")})" : "STAYED ON THE BENCH the whole traverse") + $", gravity across its path toward the band {(onBench > 0 ? pullSum / onBench : 0f):+0.0;-0.0} m/s^2 on average");
            if (judgeRides && frame == null && (took < 0f || took > 6f)) Fail($"seed{seed} cycle{b.cycle} ride {v0:0} m/s hands off: {(took < 0f ? "stayed on the bench the whole traverse" : $"only in danger after {took:0.0} s")}: something is holding the run's line for it");
            if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
            return;
        }
        Debug.Log($"BAND seed{seed} cycle{b.cycle} ride {v0:0} m/s: furthest off the middle on the traverse {worstOff:0.0} m, {(over ? "WENT OVER THE BAND" : "stayed on the bench")}, lowest speed {minSpeed * 3.6f:0} km/h, the finger up the hill {100f * edgeMost:0}% of the whole drag at most from the way in, {(onBench > 0 ? 100f * edgeIn / onBench : 0f):0}% on average along the bench (gravity across the path toward the band there {(onBench > 0 ? pullSum / onBench : 0f):+0.0;-0.0} m/s^2 on average), " + (r.Crashed ? $"CRASHED '{lastCrash}' at z={p.z:0} ({p.z - b.z0:0} m into it)" : $"rode out at {r.Speed * 3.6f:0} km/h"));
        if (judgeRides && frame == null)
        {
            if (r.Crashed) Fail($"seed{seed} cycle{b.cycle} ride {v0:0} m/s: crashed '{lastCrash}'");
            if (over) Fail($"seed{seed} cycle{b.cycle} ride {v0:0} m/s: went over the band following the run");
        }
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
    }

    /// <summary>
    /// A full drag either way on the bench (carve: the whole edge, a skidded turn): following the run onto the
    /// traverse, then the finger held hard one way for 1.5 s. It must take the rider well off the run's line (8 m
    /// or more square to it): uphill into the bank, or downhill toward the band's edge (stopped there: not judged on
    /// going over).
    /// </summary>
    static void Drag(int seed, CliffBand.Band b, float v0, float dir)
    {
        rides++;
        float z0 = b.z0 - 60f, x0 = Slope.Centre(z0), zGo = Mathf.Lerp(b.z1, b.z2, 0.35f);
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * v0;
        lastCrash = ""; terrain.Prewarm(); props.Prewarm();
        float start = float.NaN, most = 0f, held = 0f; int n = 0;
        for (float t = 0f; t < 30f && held < 1.5f; t += Dt, n++)
        {
            float zA = r.Z + 12f * Slope.Squash(r.Z), want = Slope.Centre(zA), lead = Mathf.Atan2(want - r.X, zA - r.Z);
            r.Steer = Rider.Unshape(Mathf.Clamp((lead - r.TravelHeading) * 1.5f + (want - r.X) * 0.02f, -1f, 1f));   // (feel: the input's curve undone, so the bot asks for what it always did)
            if (r.Z > zGo) { if (float.IsNaN(start)) start = Slope.Across(r.X, r.Z); r.Steer = dir; held += Dt; }
            r.TestContacts(Dt);
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
            if (!float.IsNaN(start)) most = Mathf.Max(most, Mathf.Abs(Slope.Across(r.X, r.Z) - start));
            if (r.Crashed) break;
            if (!float.IsNaN(start) && Mathf.Abs(Slope.Across(r.X, r.Z)) > CliffBand.EdgeAt(r.Z) - 1f) break;   // (at the band's edge, or up the bank: far enough)
        }
        // which way is that: toward the band (downhill side) or up into the bank
        float toBand = -b.side;
        string way = Mathf.Sign(dir) == Mathf.Sign(toBand) ? "toward the band" : "up into the bank";
        Debug.Log($"BAND seed{seed} cycle{b.cycle} full drag {(dir > 0 ? "right" : "left")} ({way}) at {v0:0} m/s: {most:0.0} m off the run's line in {held:0.0} s" + (r.Crashed ? $", crashed '{lastCrash}'" : ""));
        if (judgeRides && most < 8f) Fail($"seed{seed} cycle{b.cycle}: a full drag {(dir > 0 ? "right" : "left")} ({way}) only moved {most:0.0} m off the run's line in {held:0.0} s");
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Dt); }
    }

    // ---------------------------------------------------------------- pictures (needs the GPU)

    public static void Shots()
    {
        Setup();
        string outDir = System.Environment.GetEnvironmentVariable("BAND_OUT") ?? "band-shots";
        System.IO.Directory.CreateDirectory(outDir);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        int taken = 0;
        // (one mountain a run gives clean pictures: in a batch run the editor never ends a frame, so the forest's
        // instanced rocks and trees pile up from shot to shot, and a second mountain's pictures show the first one's
        // hanging at its heights. BAND_SHOT_SEEDS=one seed)
        var shotSeeds = System.Environment.GetEnvironmentVariable("BAND_SHOT_SEEDS");
        foreach (int seed in shotSeeds != null ? System.Array.ConvertAll(shotSeeds.Split(','), int.Parse) : new[] { 1000, 1154, 2131 })
        {
            NewMountain(seed);
            for (int c = 0; c < 8 && taken < 4; c++)
            {
                if (!CliffBand.Of(c, out var b)) continue;
                taken++;
                string tag = $"{outDir}/Band_s{seed}_c{c}";
                float zm = (b.z1 + b.z2) * 0.5f;
                Frame(zm, out var p, out var along, out var down);
                // from the bench, looking along the traverse
                Vector3 eye = p - along * 30f + Vector3.up * 2f; eye.y = Slope.Height(eye.x, eye.z) + 2f;
                Shot(cam, eye, p + along * 20f, tag + "_along.png");
                // from out over the valley, the whole band
                Vector3 far = p + down * 140f - along * 20f; far.y = H(p) + 25f;
                Shot(cam, far, p + down * 10f, tag + "_valley.png");
                // the gap, from above it
                Frame(b.z2 + 5f, out var g, out var ga, out var gd);
                Vector3 gEye = g - ga * 25f; gEye.y = H(gEye) + 3f;
                Shot(cam, gEye, g + ga * 25f, tag + "_gap.png");
                // the low place, from below
                Frame(b.l0, out var lp, out var la, out var ld);
                Vector3 lEye = lp + ld * 40f; lEye.y = H(lEye) + 3f;
                Shot(cam, lEye, lp + ld * (CliffBand.Edge + 2f), tag + "_low.png");
            }
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Shot(Camera cam, Vector3 from, Vector3 at, string file)
    {
        r.ResetAt(at.x, at.z); terrain.Prewarm(); props.Prewarm();
        { var dress = Object.FindFirstObjectByType<CourseDressing>(); if (dress != null) dress.Prewarm(); }
        typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
        cam.fieldOfView = 60f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        const int W = 1100, Hh = 760;
        var rt = new RenderTexture(W, Hh, 24);
        cam.targetTexture = rt; cam.aspect = (float)W / Hh; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, Hh, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, Hh), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("BAND-SHOT " + file);
    }
}
