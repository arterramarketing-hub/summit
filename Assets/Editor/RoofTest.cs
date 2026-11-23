using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The hamlets' roofs as ground (Roofs). Over several mountains, for every building of the first hamlets:
/// 1. Drawn against ridden: the top of what is drawn (the model's own triangles) against the roof the board rides,
///    on a half-metre grid over the roof (ROOF-MATCH).
/// 2. Drops: riders coming down onto the roof at a range of speeds, angles and headings, onto its middle, by its
///    ridge, on its eave and just past it; each ride's outcome (ROOF-DROP), and a FAIL for any that breaks a rule:
///    a fair landing that does not land, a crash into the house from above, a crash on the roof that the same landing
///    on the snow would not be, a board just past the edge thrown by the wall.
/// 3. Walls: riders on the snow straight into each wall, and up into the eaves from below (ROOF-WALL): those crash.
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod RoofTest.Run -logFile roof.log
/// ($ROOF_SEEDS mountains, default 4.) Shots: RoofTest.Shots, with the GPU (no -nographics, no -quit): a rider landing on
/// a chalet's roof, riding across it and leaving off the eave, day and night ($ROOF_OUT, the folder).
/// </summary>
public static class RoofTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static Hamlets hamlets; static MethodInfo hamUpd;
    static int n, fails;
    static readonly Dictionary<string, int> outcomes = new Dictionary<string, int>();

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
        hamlets = Object.FindFirstObjectByType<Hamlets>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP);
        dress = Object.FindFirstObjectByType<CourseDressing>();
    }

    static void NewMountain(int seed)
    {
        TestBoot.NewMountain(game, seed, terrain, props);
        if (dress != null) dress.Rebuild();
        r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ);
        hamUpd.Invoke(hamlets, null);
    }

    /// <summary>The hamlet of a cycle, built round a rider standing by it.</summary>
    static List<Roofs.Roof> Build(Hamlets.Hamlet h)
    {
        float z = (h.z0 + h.z1) * 0.5f;
        r.ResetAt(Slope.Centre(z), z);
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm();
        for (int i = 0; i < 60; i++) hamUpd.Invoke(hamlets, null);
        Physics.SyncTransforms();
        var list = new List<Roofs.Roof>();
        foreach (var roof in Roofs.Live) if (roof.o.z > h.z0 - 5f && roof.o.z < h.z1 + 5f) list.Add(roof);
        return list;
    }

    public static void Run()
    {
        Setup();
        int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("ROOF_SEEDS"), out int sv) ? sv : 4;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (int k = 0; k < seeds; k++)
            {
                int seed = 6100 + k * 311;
                NewMountain(seed);
                int done = 0;
                for (int c = 0; c <= Hamlets.LastCycle && done < 2; c++)
                {
                    var h = Hamlets.Of(c);
                    if (h == null) continue;
                    done++;
                    var roofs = Build(h);
                    Debug.Log($"ROOF seed {seed} cycle {c}: {roofs.Count} roofs ({string.Join(", ", roofs.ConvertAll(x => x.name))})");
                    foreach (var roof in roofs)
                    {
                        string tag = $"seed{seed} c{c} {roof.name}";
                        Match(roof, tag);
                        Drops(roof, tag);
                        Walls(roof, tag);
                    }
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("ROOF exception " + e); fails++; }
        var sb = new System.Text.StringBuilder();
        var keys = new List<string>(outcomes.Keys); keys.Sort();
        foreach (var key in keys) sb.Append($"\n  {outcomes[key],5}  {key}");
        Debug.Log($"ROOF-SUMMARY {fails} failures in {sw.Elapsed.TotalSeconds:0} s; outcomes:{sb}");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static void Count(string k) { outcomes.TryGetValue(k, out int c); outcomes[k] = c + 1; }
    static void Fail(string s) { fails++; Debug.Log("ROOF-FAIL " + s); }

    // ================================================================ 1. what is drawn against what is ridden

    static void Match(Roofs.Roof roof, string tag)
    {
        var tris = new List<Vector3>();
        foreach (var mr in roof.t.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.name.StartsWith("Far") || mr.name.StartsWith("Glass")) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var m = mr.transform.localToWorldMatrix; var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles;
            for (int i = 0; i < t.Length; i++) tris.Add(m.MultiplyPoint3x4(v[t[i]]));
        }
        var s = roof.s;
        int samples = 0, close = 0, none = 0; float sum = 0f, worst = 0f; Vector2 worstAt = Vector2.zero;
        for (float lx = s.x0 + 0.3f; lx <= s.x1 - 0.3f; lx += 0.5f)
            for (float lz = s.z0 + 0.3f; lz <= s.z1 - 0.3f; lz += 0.5f)
            {
                bool inStack = false;
                foreach (var st in s.stacks) if (Mathf.Abs(lx - st.x) < st.z + 0.4f && Mathf.Abs(lz - st.y) < st.z + 0.4f) inStack = true;
                if (inStack) continue;
                Vector3 w = roof.World(lx, 0f, lz);
                float top = roof.o.y + s.Top(lx, lz);
                float drawn = Highest(tris, w.x, w.z, top + 3f);
                if (float.IsNegativeInfinity(drawn)) { none++; continue; }
                float d = drawn - top;
                samples++; sum += Mathf.Abs(d);
                if (Mathf.Abs(d) < 0.12f) close++;
                if (Mathf.Abs(d) > Mathf.Abs(worst)) { worst = d; worstAt = new Vector2(lx, lz); }
            }
        // the edges: the drawn snow reaches the ridden roof's edge (sampled 0.15 m in) and stops before it (0.35 m out)
        int edgeIn = 0, edgeInDrawn = 0, edgeOut = 0, edgeOutDrawn = 0;
        for (float u = 0.1f; u < 0.95f; u += 0.1f)
        {
            var pts = new[] {
                (new Vector2(s.x0 + 0.15f, Mathf.Lerp(s.z0, s.z1, u)), true), (new Vector2(s.x1 - 0.15f, Mathf.Lerp(s.z0, s.z1, u)), true),
                (new Vector2(Mathf.Lerp(s.x0, s.x1, u), s.z0 + 0.15f), true), (new Vector2(Mathf.Lerp(s.x0, s.x1, u), s.z1 - 0.15f), true),
                (new Vector2(s.x0 - 0.35f, Mathf.Lerp(s.z0, s.z1, u)), false), (new Vector2(s.x1 + 0.35f, Mathf.Lerp(s.z0, s.z1, u)), false),
                (new Vector2(Mathf.Lerp(s.x0, s.x1, u), s.z0 - 0.35f), false), (new Vector2(Mathf.Lerp(s.x0, s.x1, u), s.z1 + 0.35f), false) };
            foreach (var (l, inside) in pts)
            {
                Vector3 w = roof.World(l.x, 0f, l.y);
                float top = roof.o.y + s.Top(Mathf.Clamp(l.x, s.x0, s.x1), Mathf.Clamp(l.y, s.z0, s.z1));
                float drawn = Highest(tris, w.x, w.z, top + 3f);
                bool there = !float.IsNegativeInfinity(drawn) && drawn > top - 0.35f;
                if (inside) { edgeIn++; if (there) edgeInDrawn++; } else { edgeOut++; if (there) edgeOutDrawn++; }
            }
        }
        string line = $"{tag}: drawn top vs ridden over {samples} points: mean {sum / Mathf.Max(1, samples):0.000} m, {close * 100 / Mathf.Max(1, samples)}% within 0.12 m, worst {worst:+0.00;-0.00} m at ({worstAt.x:0.0}, {worstAt.y:0.0}); {none} with nothing drawn; " +
                      $"edges: drawn at {edgeInDrawn}/{edgeIn} points 0.15 m inside, {edgeOutDrawn}/{edgeOut} 0.35 m outside";
        Debug.Log("ROOF-MATCH " + line);
        if (samples == 0 || sum / samples > 0.08f || Mathf.Abs(worst) > 0.3f || none > 0 || edgeInDrawn < edgeIn * 0.9f || edgeOutDrawn > edgeOut * 0.25f) Fail("match " + line);
    }

    /// <summary>The highest drawn triangle on the vertical line through (x, z) below maxY, or -infinity.</summary>
    static float Highest(List<Vector3> tris, float x, float z, float maxY)
    {
        float best = float.NegativeInfinity;
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-7f) continue;
            float wa = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
            float wb = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
            float wc = 1f - wa - wb;
            if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
            float y = wa * a.y + wb * b.y + wc * c.y;
            if (y < maxY && y > best) best = y;
        }
        return best;
    }

    // ================================================================ riding

    class Ride
    {
        public string crash = "", firstOn = ""; public bool landedRoof, landedSnow, grind, rooftopLink, leftRoof, onRoofEver;
        public float impact, angle, leaveSpeed, onFor; public Vector3 crashAt, crashLocal; public bool crashOverRoof;
        public int phase, crashPhase = -1;   // 0 coming in, 1 on the roof (or its ridge), 2 off it again (or down on the snow)
        public List<string> links = new List<string>();
    }
    static Ride cur;
    static bool hooked;
    static Vector3 vBefore;
    static bool trace;
    static readonly string traceOnly = System.Environment.GetEnvironmentVariable("ROOF_TRACE");

    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        r.Crash += why => { if (cur != null && cur.crash == "") { cur.crash = why; cur.crashAt = r.Body.position; cur.crashPhase = cur.phase; } };
        r.GrindStart += f =>
        {
            if (cur == null || f.id < Roofs.IdBase) return;
            if (!cur.grind && !cur.landedRoof && !cur.landedSnow)
            {
                // came down onto the ridge: how hard and how steeply, as Land would have measured it (it is level)
                cur.impact = Mathf.Max(0f, -vBefore.y); cur.angle = Mathf.Atan2(cur.impact, new Vector2(vBefore.x, vBefore.z).magnitude) * Mathf.Rad2Deg;
            }
            cur.grind = true;
        };
        r.Link += l => { if (cur != null) { cur.links.Add(l.name); if (l.name == "Rooftop") cur.rooftopLink = true; } };
    }

    /// <summary>One physics step as the game runs it (StressTest's): the obstacle contact found by hand, as batchmode
    /// sends no collision messages.</summary>
    static void Step()
    {
        float realDt = Time.fixedDeltaTime / Mathf.Max(Time.timeScale, 1e-4f);
        game.TestTickHitStop(realDt);
        float dt = Time.fixedDeltaTime;
        r.Tick(dt);
        // (after the rider's step, with the velocity it leaves for PhysX: what the board will sweep through this step)
        if (!r.Crashed && r.Running)
        {
            r.TestContacts(dt);   // (as Rider.OnCollisionEnter: ridden over, glanced off or crashed into)
        }
        Vector3 vSet = r.Body.linearVelocity;
        body.PhysicsStep(); Physics.Simulate(dt);
        // (and what PhysX itself stopped: a contact that took more than 6 m/s off the board, against an obstacle)
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

    static void EndHitStop() { for (int i = 0; i < 240; i++) game.TestTickHitStop(Time.fixedDeltaTime); }

    /// <summary>Puts the rider in the air at p with velocity v (or on the snow, if v is level and p on it).</summary>
    static void Launch(Vector3 p, Vector3 v, bool air)
    {
        EndHitStop();
        r.ResetAt(p.x, p.z); body.ResetVisual();
        r.Begin();
        if (air)
        {
            r.Body.position = p; r.transform.position = p;
            r.Airborne = true; r.DropFrom = p;
        }
        r.Heading = Mathf.Clamp(Mathf.Atan2(v.x, v.z), -1.45f, 1.45f);
        r.Body.linearVelocity = v;
        Physics.SyncTransforms();
    }

    /// <summary>Rides until it is down, back on the snow a moment, or the time is up; what happened.</summary>
    static Ride Watch(Roofs.Roof roof, float seconds)
    {
        Hook();
        var w = new Ride(); cur = w;
        bool wasOn = false; float snowFor = 0f, t = 0f, afterCrash = 0f;
        while (t < seconds)
        {
            bool airBefore = r.Airborne;
            vBefore = r.Velocity;
            Step(); t += Time.fixedDeltaTime;
            if (trace) Debug.Log($"ROOF-TRACE t={t:0.000} p={r.Body.position} v={r.Velocity} air={r.Airborne} grind={r.Grinding} roof={(r.OnRoof != null)} crashed={r.Crashed} land={r.LastLandImpact:0.0}/{r.LastLandAngle:0}");
            bool on = r.OnRoof == roof || (r.Grinding && r.GrindFeature.id == roof.ridge.id && roof.hasRidge);
            if (airBefore && !r.Airborne && !r.Crashed)
            {
                if (on || r.Grinding) { if (!w.landedRoof && !w.landedSnow) { w.landedRoof = true; if (!r.Grinding) { w.impact = r.LastLandImpact; w.angle = r.LastLandAngle; } } }
                else if (!w.landedRoof && !w.landedSnow) { w.landedSnow = true; w.impact = r.LastLandImpact; w.angle = r.LastLandAngle; }
            }
            if (on) { w.onRoofEver = true; w.onFor += Time.fixedDeltaTime; if (w.phase == 0) w.phase = 1; }
            if (wasOn && !on && r.Airborne && !r.Crashed) { w.leftRoof = true; w.leaveSpeed = r.Velocity.magnitude; w.phase = 2; }
            if (w.landedSnow) w.phase = 2;
            wasOn = on;
            if (r.Crashed) { afterCrash += Time.fixedDeltaTime; if (afterCrash > 0.2f) break; }
            if (!r.Crashed && !r.Airborne && !r.Grinding && r.OnRoof == null) { snowFor += Time.fixedDeltaTime; if (snowFor > 0.25f && (w.leftRoof || w.landedSnow)) break; }
            else snowFor = 0f;
        }
        if (w.crash != "") w.crashOverRoof = Roofs.Under(w.crashAt.x, w.crashAt.z, w.crashAt.y + 5f, out _, out _, out var rr) && rr == roof;
        // a slam is a landing too (judged as the board met the roof, before it was ever down on it)
        if (w.crash == "Slammed!" && !w.landedRoof && !w.landedSnow)
        {
            if (w.crashOverRoof) w.landedRoof = true; else w.landedSnow = true;
            w.impact = r.LastLandImpact; w.angle = r.LastLandAngle;
        }
        if (w.crash != "") { var l = roof.Local(w.crashAt.x, w.crashAt.z); w.crashLocal = new Vector3(l.x, w.crashAt.y - roof.o.y - roof.s.Top(Mathf.Clamp(l.x, roof.s.x0, roof.s.x1), Mathf.Clamp(l.y, roof.s.z0, roof.s.z1)), l.y); }
        cur = null;
        return w;
    }

    // ================================================================ 2. drops onto the roof

    static void Drops(Roofs.Roof roof, string tag)
    {
        var s = roof.s;
        // (the middle of the pitch away from the chimney: coming down past a chimney is a crash into it, fairly)
        float away = s.stacks.Length > 0 && s.stacks[0].x > 0f ? -1f : 1f;
        float zm = (s.z0 + s.z1) * 0.5f, xm = s.gable ? away * s.x1 * 0.55f : (s.x0 + s.x1) * 0.5f;
        // where it comes down, in the roof's frame: the middle of a pitch, by the ridge (on it, for a chalet or the lodge),
        // the eave's snow (0.2 m in), just past the eave (past its reach: no roof under the board's middle)
        var targets = new List<(string name, Vector2 at, bool onIt)>
        {
            ("middle", new Vector2(xm, zm), true),
            (s.gable ? "ridge" : "back", s.gable ? new Vector2(0.15f, zm - 1f) : new Vector2(xm, s.z0 + 0.8f), true),
            ("eave", new Vector2(away > 0f ? s.x1 - 0.2f : s.x0 + 0.2f, zm), true),
            ("past the eave", new Vector2(away > 0f ? s.px1 + s.reach + 0.05f : s.px0 - s.reach - 0.05f, zm), false),
            ("past the front", new Vector2(xm * 0.5f, s.pz1 + s.reach + 0.05f), false),
        };
        float[] speeds = { 9f, 16f, 24f };
        float[] angles = { 12f, 35f, 60f, 80f };
        // headings: down the fall line, and across it at 50 degrees each way
        float[] heads = { 0f, 50f, -50f };
        foreach (var tg in targets)
            foreach (float sp in speeds)
                foreach (float ang in angles)
                    foreach (float hd in heads)
                    {
                        Vector3 at = roof.World(tg.at.x, 0f, tg.at.y);
                        float top = roof.o.y + s.Top(Mathf.Clamp(tg.at.x, s.x0, s.x1), Mathf.Clamp(tg.at.y, s.z0, s.z1));
                        at.y = (tg.onIt ? top : top - 0.2f) + Rider.R;
                        float h = hd * Mathf.Deg2Rad;
                        Vector3 vImp = new Vector3(Mathf.Sin(h) * sp, -sp * Mathf.Tan(ang * Mathf.Deg2Rad), Mathf.Cos(h) * sp);
                        // back along the flight, as far as 0.35 s, while it is clear above this roof (and every other one)
                        float T = 0f;
                        for (float tb = 0.01f; tb <= 0.35f; tb += 0.01f)
                        {
                            Vector3 q = at - vImp * tb + Vector3.down * (0.5f * Rider.G * tb * tb);
                            if (Clash(q)) break;
                            T = tb;
                        }
                        if (T < 0.06f) { Count("drop: no clear way in (skipped)"); continue; }
                        Vector3 p0 = at - vImp * T + Vector3.down * (0.5f * Rider.G * T * T);
                        Vector3 v0 = vImp + Vector3.up * (Rider.G * T);
                        Launch(p0, v0, true);
                        string me = $"{tag} {tg.name} {sp:0} m/s {ang:0}deg heading {hd:+0;-0;0}";
                        trace = traceOnly != null && me.Contains(traceOnly);
                        if (trace) Debug.Log($"ROOF-TRACE {me}: from {p0} v {v0} (T {T:0.00}) to {at} v {vImp}");
                        var w = Watch(roof, 3f);
                        trace = false;
                        string what = Outcome(w);
                        // (just past the edge and moving in toward the house, it goes on under the eave into it: a hit, fairly)
                        Vector3 toHouse = roof.o - at; toHouse.y = 0f;
                        bool inward = !tg.onIt && Vector3.Dot(new Vector3(vImp.x, 0f, vImp.z), toHouse.normalized) > 1f;
                        string rule = inward ? null : Judge(roof, tg.name, tg.onIt, w, s);
                        Count($"drop {(tg.onIt ? "onto the roof" : inward ? "just past its edge, moving in under it" : "just past its edge")}: {what}");
                        string line = $"{tag} {tg.name} {sp:0} m/s {ang:0}deg heading {hd:+0;-0;0}: {what}{(w.crash != "" ? $" at roof ({w.crashLocal.x:0.0}, {w.crashLocal.z:0.0}) {w.crashLocal.y:+0.0;-0.0} m off its top" : "")} (impact {w.impact:0.0} m/s at {w.angle:0} deg, on the roof {w.onFor:0.00} s{(w.leftRoof ? $", left it at {w.leaveSpeed:0.0} m/s" : "")}; links {string.Join(" ", w.links)})";
                        if (rule != null) Fail(line + " :: " + rule);
                        else Debug.Log("ROOF-DROP " + line);
                    }
    }

    /// <summary>Would a board here be in something: under a roof's top over it, or in any collider?</summary>
    static bool Clash(Vector3 q)
    {
        foreach (var roof in Roofs.Live)
        {
            var l = roof.Local(q.x, q.z); var s = roof.s;
            if (l.x < s.px0 - s.reach || l.x > s.px1 + s.reach || l.y < s.pz0 - s.reach || l.y > s.pz1 + s.reach) continue;
            float top = roof.o.y + s.Top(Mathf.Clamp(l.x, s.x0, s.x1), Mathf.Clamp(l.y, s.z0, s.z1));
            if (q.y < top + Rider.R - 0.02f) return true;   // (under where it comes down: it would be in the roof)
        }
        if (q.y < Slope.DrawnHeight(q.x, q.z) + Rider.R) return true;
        return Physics.CheckSphere(q, Rider.R + 0.02f, ~0, QueryTriggerInteraction.Ignore);
    }

    static string Outcome(Ride w)
    {
        string landed = w.landedRoof ? (w.grind ? "landed on the ridge, slid it" : "landed on the roof") : w.landedSnow ? "came down on the snow" : w.grind ? "slid the ridge" : "no landing";
        if (w.crash != "") return $"{landed}, crashed: {w.crash}{(w.crashOverRoof ? " (over the roof)" : "")}";
        if (w.landedRoof || w.grind) return landed + (w.leftRoof ? ", rode off it" : ", still on it");
        return landed;
    }

    /// <summary>The rules: null if this ride kept them.</summary>
    static string Judge(Roofs.Roof roof, string target, bool onIt, Ride w, Roofs.Shape s)
    {
        // (a crash once off the roof again, in the yard or on the snow below, is the snow's business)
        bool houseHit = w.crash == "Cabin!" && w.crashPhase < 2;
        if (houseHit && w.crashPhase == 1) return "hit the house riding on its roof";
        if (!onIt)
        {
            // past the edge: no roof under the board's middle, so nothing to land on, and nothing of the house to hit
            // by the eave (falling on past it, onto a terrace or a deck further down, is a hit like any other)
            if (houseHit && w.crashLocal.y > -1f) return "thrown by the house from just past its edge";
            return null;
        }
        if (houseHit) return "hit the house coming down onto its roof";
        if (!w.landedRoof && !w.grind && w.crash == "") return "came down onto the roof and did not land on it";
        if (w.landedRoof && w.crash == "Slammed!" && w.crashPhase == 0)
        {
            // the same rule as on the snow: hard and square on
            bool slam = w.impact > Rider.SlamImpact && w.angle > Rider.MaxLandAngle;
            if (!slam) return "slammed on the roof where the snow would have ridden it";
        }
        if ((w.landedRoof || w.grind) && w.crashPhase != 0 && w.impact > Rider.SlamImpact && w.angle > Rider.MaxLandAngle) return "rode away from a slam";
        return null;
    }

    // ================================================================ 3. the walls

    static void Walls(Roofs.Roof roof, string tag)
    {
        var s = roof.s;
        // on the snow beside each wall, 7 m out, straight at it, at board height
        var from = new List<(string name, Vector2 at, Vector2 dir)>
        {
            ("left wall", new Vector2(-s.wx - 4.5f, (s.wz0 + s.wz1) * 0.5f), new Vector2(1f, 0f)),
            ("right wall", new Vector2(s.wx + 4.5f, (s.wz0 + s.wz1) * 0.5f), new Vector2(-1f, 0f)),
            ("back wall", new Vector2(0.3f, s.wz0 - 4.5f), new Vector2(0f, 1f)),
        };
        foreach (var f in from)
            foreach (float sp in new[] { 11f, 18f })
            {
                Vector3 p = roof.World(f.at.x, 0f, f.at.y);
                Vector3 d = roof.WorldDir(new Vector3(f.dir.x, 0f, f.dir.y));
                p.y = Slope.DrawnHeight(p.x, p.z) + Rider.R;
                if (Physics.CheckSphere(p, 1.2f, ~0, QueryTriggerInteraction.Ignore)) { Count("wall: start blocked"); continue; }
                Launch(p, d * sp, false);
                var w = Watch(roof, 1.5f);
                string what = w.crash != "" ? $"crashed: {w.crash}" : w.onRoofEver ? "rode onto the roof" : w.landedSnow || !r.Airborne ? "missed it" : "in the air";
                Count($"wall, {(f.name == "back wall" ? "uphill" : "side")}: {what}");
                Debug.Log($"ROOF-WALL {tag} {f.name} {sp:0} m/s: {what}");
                // from uphill, the hill can stand as high as the eaves: riding onto the roof from there is fair
                if (w.onRoofEver && f.name != "back wall") Fail($"{tag} {f.name} {sp:0} m/s: rode up the wall onto the roof");
            }
        // up into the eave's underside from below (half way out along the overhang), and up past the eave from outside
        // to come down on the roof
        // (just out from the snow's edge, near enough to clip it with the board's side)
        float ox = s.px1 + Rider.R - 0.1f, zm = (s.wz0 + s.wz1) * 0.5f;
        {
            float under = roof.o.y + s.Top(ox, zm) - 1.6f;
            Vector3 p = roof.World(ox, 0f, zm); p.y = under;
            Launch(p, roof.WorldDir(new Vector3(0f, 9f, 3f)), true);
            trace = traceOnly != null && $"{tag} up into the eave".Contains(traceOnly);
            if (trace) Debug.Log($"ROOF-TRACE {tag} up into the eave: from {p} local ({ox:0.00}, {under - roof.o.y:0.00}, {zm:0.00}); lip {s.Top(Mathf.Clamp(ox, s.x0, s.x1), zm) - Roofs.Lip:0.00}, px1 {s.px1:0.00}");
            var w = Watch(roof, 1.5f);
            trace = false;
            string what = w.crash != "" ? $"crashed: {w.crash}" : w.landedRoof || w.onRoofEver ? "on the roof" : "glanced off it (a scrape), kept off the roof";
            Count($"up into the eave from below: {what}");
            Debug.Log($"ROOF-WALL {tag} up into the eave from below: {what}");
            if (w.onRoofEver && w.crash == "") Fail($"{tag} up into the eave from below: put onto the roof");
        }
        {
            float top = roof.o.y + s.Top(s.x1, zm);
            Vector3 p = roof.World(s.px1 + 0.8f, 0f, zm); p.y = top - 1.2f;
            Launch(p, roof.WorldDir(new Vector3(-2.2f, 10f, 1f)), true);
            var w = Watch(roof, 2.5f);
            string what = w.crash != "" ? $"crashed: {w.crash}" : w.landedRoof ? "came down on the roof" : w.landedSnow ? "came down on the snow" : "no landing";
            Count($"up past the eave, down onto the roof: {what}");
            Debug.Log($"ROOF-WALL {tag} up past the eave from outside: {what}");
            if (w.crash == "Cabin!") Fail($"{tag} up past the eave from outside: {what}");
        }
    }

    // ================================================================ renders

    static Camera cam; static DayNight dayNight; static Atmosphere atmo;
    static readonly List<System.Action> steps = new List<System.Action>();
    static int at;

    public static void Shots()
    {
        string outDir = System.Environment.GetEnvironmentVariable("ROOF_OUT") ?? "RoofShots";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);
        Setup();
        dayNight = F<DayNight>("dayNight"); atmo = Object.FindFirstObjectByType<Atmosphere>();
        cam = Camera.main; cam.GetComponent<ChaseCamera>().enabled = false;
        // the first chalet on the first mountains
        Roofs.Roof roof = null;
        for (int k = 0; k < 6 && roof == null; k++)
        {
            NewMountain(6100 + k * 311);
            for (int c = 0; c <= Hamlets.LastCycle && roof == null; c++)
            {
                var h = Hamlets.Of(c);
                if (h == null) continue;
                foreach (var x in Build(h)) if (x.name == "Chalet") { roof = x; break; }
            }
        }
        if (roof == null) { Debug.LogError("ROOF shots: no chalet"); EditorApplication.Exit(1); return; }
        var s = roof.s;
        // down onto the right pitch half way out, heading down the ridge's line: lands, rides along the pitch, off the front eave
        // (the pitch away from the chimney)
        float px = (s.stacks.Length > 0 && s.stacks[0].x > 0f ? -1f : 1f) * s.x1 * 0.45f;
        Vector3 land = roof.World(px, 0f, s.z0 + 2f); land.y = roof.o.y + s.Top(px, s.z0 + 2f) + Rider.R;
        Vector3 dir = roof.WorldDir(new Vector3(0f, 0f, 1f));
        Vector3 vImp = dir * 13f + Vector3.down * 7f;
        float T = 0.45f;
        Vector3 p0 = land - vImp * T + Vector3.down * (0.5f * Rider.G * T * T), v0 = vImp + Vector3.up * (Rider.G * T);
        // the ride once, to find its moments
        Launch(p0, v0, true);
        int stepsTaken = 0, tLand = -1, tLeave = -1, tAway = -1; bool wasOn = false;
        for (int i = 0; i < 400; i++)
        {
            Step(); stepsTaken++;
            bool on = r.OnRoof == roof || r.Grinding;
            if (on && tLand < 0) tLand = i;
            if (wasOn && !on && r.Airborne && tLeave < 0) tLeave = i;
            if (tLeave >= 0 && i == tLeave + 30) { tAway = i; break; }
            wasOn = on;
        }
        Debug.Log($"ROOF shots: chalet at {roof.o}, landed at step {tLand}, left at {tLeave}; crashed {r.Crashed}");
        int tMid = tLand >= 0 && tLeave > tLand ? (tLand + tLeave) / 2 : tLand + 40;
        var moments = new List<(string name, int step)> { ("1-landing", Mathf.Max(0, tLand + 2)), ("2-riding", tMid), ("3-off-the-eave", tLeave >= 0 ? tLeave + 8 : tMid + 40) };
        // the camera: off the right side and a little ahead, at roof height, looking across at the roof
        Vector3 side = roof.WorldDir(new Vector3(Mathf.Sign(px), 0f, 0f));
        Vector3 roofMid = roof.World(0f, s.Top(0f, 0f) - 1f, (s.z0 + s.z1) * 0.5f);
        Vector3 eye = roofMid + side * 17f + dir * 7f; eye.y = Mathf.Max(roofMid.y + 3f, Slope.DrawnHeight(eye.x, eye.z) + 2f);
        steps.Clear(); at = 0;
        steps.Add(() => { TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); Pose(p0, v0, 2, eye, roofMid); Shoot(Path.Combine(outDir, "warmup.png")); });
        foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
            foreach (var m in moments)
            {
                steps.Add(() =>
                {
                    TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                    for (int i = 0; i < 3; i++) hamUpd.Invoke(hamlets, null);
                    Pose(p0, v0, m.step, eye, roofMid);
                    Shoot(Path.Combine(outDir, $"roof-{m.name}-{when}.png"));
                    // and from behind, as the chase camera sees it
                    Vector3 rp = r.transform.position, vv = r.Velocity; vv.y = 0f;
                    Vector3 back = vv.sqrMagnitude > 1f ? vv.normalized : dir;
                    Vector3 e2 = rp - back * 7.5f + Vector3.up * 3.2f;
                    e2.y = Mathf.Max(e2.y, Slope.DrawnHeight(e2.x, e2.z) + 1.5f);
                    cam.transform.SetPositionAndRotation(e2, Quaternion.LookRotation(rp + back * 4f + Vector3.down * 0.6f - e2));
                    Shoot(Path.Combine(outDir, $"roof-{m.name}-{when}-chase.png"));
                });
            }
        steps.Add(() =>
        {
            TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow(); TimeOfDay.TestClock = -1f;
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
            catch (System.Exception e) { Debug.LogError("ROOF shots exception " + e); at = steps.Count; }
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    static void Pose(Vector3 p0, Vector3 v0, int stepsTo, Vector3 eye, Vector3 look)
    {
        Launch(p0, v0, true);
        for (int i = 0; i <= stepsTo; i++) Step();
        body.Apply(0f);
        Debug.Log($"ROOF shot at step {stepsTo}: rider {r.transform.position} air={r.Airborne} roof={(r.OnRoof != null)} grind={r.Grinding} crashed={r.Crashed} v={r.Velocity.magnitude:0.0}");
        cam.fieldOfView = 55f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        terrain.Prewarm(); props.Prewarm();
        typeof(SlopeStreamer).GetMethod("Update", NP)?.Invoke(terrain, null);
        typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
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
