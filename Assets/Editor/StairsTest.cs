using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The stairs up the bank (campstairs, CampStairs.cs): the hero walks and runs up them and down them, and the feet
/// are checked against the treads every frame (the foot carrying the body on the tread under it, not over the nosing
/// or through it); then three minutes of the camp's life: how its people get up and down the bank (the stairs, or
/// its steep snow beside them) and their feet on the treads too.
/// Batch: -executeMethod StairsTest.Run   (exit 1 on a failure; STAIRS_TRACE=1 logs the camp's people one by one,
/// where each one's planted ankle was taken, and the snow, floor and treads across the top of the stairs)
/// </summary>
public static class StairsTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float dt = 1f / 60f;
    static Camp camp; static RiderBody body; static FieldInfo fPos, fCamYaw;
    static int fails;

    public static void Run()
    {
        var game = TestHarness.Boot();
        camp = game.camp;
        body = game.body;
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = dt;
        fPos = typeof(Camp).GetField("pos", NP); fCamYaw = typeof(Camp).GetField("camYaw", NP);
        try
        {
            float cx = Slope.Centre(Slope.PadLip);
            Debug.Log($"STAIRS {Camp.StairRisers} risers of {Camp.StairRise * 100f:0.0} cm on a {Camp.StairGoing * 100f:0} cm going, {Camp.StairFlights} flights, landings {Camp.StairLanding:0.00} m; {Camp.StairLength:0.0} m from the bottom riser to the top one, {Camp.StairRisers * Camp.StairRise:0.00} m up");
            var bottom = new Vector3(cx + Camp.StairX, 0f, Camp.StairBottomZ + 1.6f);
            var top = new Vector3(cx + Camp.StairX, 0f, Camp.StairTopZ - Camp.StairTopDeck - 1.4f);
            Leg("up the stairs, walking", bottom, top, 0.85f);
            Leg("down the stairs, walking", top, bottom, 0.85f);
            Leg("up the stairs, running", bottom, top, 1f);
            Leg("down the stairs, running", top, bottom, 1f);
            Leg("up the stairs, slowly", bottom, top, 0.45f);
            People();
        }
        catch (System.Exception e) { fails++; Debug.LogError("STAIRS " + e); }
        Debug.Log($"STAIRS done: {fails} fail(s)");
        ChaseCamera.TestDt = 0f;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>How far a foot's sole is above the tread under it (its heel and toes both on that tread: on); NaN off the stairs.</summary>
    static float SoleOver(Vector3 ankle, float rest, Vector3 facing, out bool onEdge)
    {
        float heel = Camp.TreadUnder(ankle - facing * StairFeet.Heel), toe = Camp.TreadUnder(ankle + facing * StairFeet.Toe), at = Camp.TreadUnder(ankle);
        onEdge = !float.IsNaN(heel) && !float.IsNaN(toe) && Mathf.Abs(heel - toe) > 0.01f;
        if (float.IsNaN(at)) return float.NaN;
        float tread = float.IsNaN(heel) || float.IsNaN(toe) ? at : Mathf.Max(heel, toe);
        return ankle.y - rest - tread;
    }

    /// <summary>The planted ankle's height over the floor walking on the level (the clips' own): the lowest a foot comes, on the plateau.</summary>
    static float Rest
    {
        get
        {
            if (rest >= 0f) return rest;
            float cx = Slope.Centre(Slope.PadLip);
            camp.TeleportTo(new Vector3(cx + 4f, 0f, -66.8f), 90f);
            for (int i = 0; i < 30; i++) { camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt); }
            float r = float.MaxValue, rs = float.MaxValue;
            for (int i = 0; i < 150; i++)
            {
                float head = Mathf.PI * 0.5f - (float)fCamYaw.GetValue(camp);
                camp.Walk(new Vector2(Mathf.Sin(head), Mathf.Cos(head)) * 0.85f, dt);
                body.Apply(dt); camp.CameraStep(dt);
                var p = (Vector3)fPos.GetValue(camp);
                float lo = Mathf.Min(body.Hero.Foot[0].position.y, body.Hero.Foot[1].position.y) - p.y;
                if (i > 60) r = Mathf.Min(r, lo);
            }
            rest = r;
            Debug.Log($"STAIRS the planted ankle walking on the level: {rest * 100f:0.0} cm over the floor");
            return rest;
        }
    }
    static float rest = -1f;

    /// <summary>The same measure walking on the level (the plateau) at the same stick: how far the clips' own feet come off the floor.</summary>
    static (float mean, float over) Level(float stick)
    {
        float rest = Rest, cx = Slope.Centre(Slope.PadLip);
        camp.TeleportTo(new Vector3(cx + 4f, 0f, -66.8f), 90f);
        for (int i = 0; i < 30; i++) { camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt); }
        float sum = 0f; int n = 0, over = 0;
        for (int i = 0; i < 160; i++)
        {
            float head = Mathf.PI * 0.5f - (float)fCamYaw.GetValue(camp);
            camp.Walk(new Vector2(Mathf.Sin(head), Mathf.Cos(head)) * stick, dt);
            body.Apply(dt); camp.CameraStep(dt);
            if (i < 50) continue;
            var p = (Vector3)fPos.GetValue(camp);
            float e0 = body.Hero.Foot[0].position.y - rest - p.y, e1 = body.Hero.Foot[1].position.y - rest - p.y;
            float e = Mathf.Min(Mathf.Abs(e0), Mathf.Abs(e1));
            if (e > 0.07f) continue;
            sum += e; n++; if (e > 0.05f) over++;
        }
        return (n > 0 ? sum / n : 0f, n > 0 ? over / (float)n : 0f);
    }

    static void Leg(string name, Vector3 from, Vector3 to, float stick)
    {
        float rest = Rest;
        var level = Level(stick);
        float yaw = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
        camp.TeleportTo(from, yaw);
        var h = body.Hero;
        for (int i = 0; i < 30; i++) { camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt); }
        float t = 0f; int frames = 0, edge = 0, off = 0, through = 0, flight = 0; float sum = 0f, worst = 0f, worstThrough = 0f;
        float stairT = 0f;
        bool ok = false;
        while (t < 25f)
        {
            var p = (Vector3)fPos.GetValue(camp);
            if (new Vector2(to.x - p.x, to.z - p.z).magnitude < 0.6f) { ok = true; break; }
            float head = Mathf.Atan2(to.x - p.x, to.z - p.z) - (float)fCamYaw.GetValue(camp);
            camp.Walk(new Vector2(Mathf.Sin(head), Mathf.Cos(head)) * stick, dt);
            body.Apply(dt); camp.CameraStep(dt);
            t += dt;
            p = (Vector3)fPos.GetValue(camp);
            if (!Camp.OnStairs(p)) continue;
            stairT += dt;
            var facing = body.FeetFacing;
            float e0 = SoleOver(h.Foot[0].position, rest, facing, out bool g0), e1 = SoleOver(h.Foot[1].position, rest, facing, out bool g1);
            if (float.IsNaN(e0) || float.IsNaN(e1)) continue;
            // the foot carrying the body: the one nearer its tread (none, both off: a running stride's flight)
            float e = Mathf.Abs(e0) < Mathf.Abs(e1) ? e0 : e1; bool g = Mathf.Abs(e0) < Mathf.Abs(e1) ? g0 : g1;
            if (e > 0.07f) { flight++; continue; }
            frames++; sum += Mathf.Abs(e); worst = Mathf.Max(worst, Mathf.Abs(e));
            if (Mathf.Abs(e) > 0.05f) off++;
            if (g) edge++;
            float low = Mathf.Min(e0, e1);
            if (low < -0.04f) { through++; worstThrough = Mathf.Min(worstThrough, low); }
        }
        var end = (Vector3)fPos.GetValue(camp);
        float mean = frames > 0 ? sum / frames : 0f;
        // (against the same walk on the level: the clips lift the heel as the foot rolls off it, a few cm, anywhere)
        bool feetOk = frames > 30 && mean < level.mean + 0.015f && off < frames * (level.over + 0.15f) && through < frames * 0.04f && edge < frames * 0.15f;
        if (!ok || !feetOk) fails++;
        string msg = $"STAIRS {name}: {(ok ? "there" : "NEVER GOT THERE, at " + end)} in {t:0.0} s ({stairT:0.0} s on the stairs); the foot carrying the body {mean * 100f:0.0} cm off its tread on average (on the level {level.mean * 100f:0.0}, more than 5 cm in {level.over * 100f:0}%), {worst * 100f:0} cm at worst, more than 5 cm in {off} of {frames} frames ({flight} more with both feet off), over a nosing in {edge}; a foot into a tread (over 4 cm) in {through} (deepest {-worstThrough * 100f:0} cm)";
        if (ok && feetOk) Debug.Log(msg); else Debug.LogError(msg);
    }

    /// <summary>Three minutes of the camp's people: up and down the bank by the stairs or its steep snow; their feet on the treads.</summary>
    static void People()
    {
        var life = camp.GetComponent<CampLife>();
        if (life == null) { Debug.Log("STAIRS (no camp life)"); return; }
        var cam = Camera.main;
        float cx = Slope.Centre(Slope.PadLip);
        // the way up the bank the people's paths take (from its foot, the bench and the gate, to the camp)
        {
            var fp = typeof(CampLife).GetMethod("FindPath", BindingFlags.NonPublic | BindingFlags.Instance);
            var sbw = new System.Text.StringBuilder("STAIRS   walkable up the stairs' middle:");
            for (float z = Camp.StairBottomZ + 1f; z > Camp.StairTopZ - 2f; z -= 1f) sbw.Append(life.Walkable(new Vector3(cx + Camp.StairX, 0f, z)) ? " o" : " X");
            Debug.Log(sbw.ToString());
            if (System.Environment.GetEnvironmentVariable("STAIRS_TRACE") == "1")
                foreach (float dx in new[] { -1.0f, 0f, 1.0f })
                {
                    var sbt = new System.Text.StringBuilder($"STAIRS   the top, {dx:+0.0;-0.0} m across: deck {Camp.TreadUnder(new Vector3(cx + Camp.StairX + dx, 0f, Camp.StairTopZ - 0.5f)):0.000}; z: snow / floor / tread");
                    for (float z = Camp.StairTopZ + 0.3f; z > Camp.StairTopZ - Camp.StairTopDeck - 2.2f; z -= 0.2f)
                    {
                        var w = new Vector3(cx + Camp.StairX + dx, 0f, z);
                        float sn = Slope.DrawnHeight(w.x, w.z);
                        sbt.Append($" {z:0.0}: {sn:0.000}/{camp.FloorHeight(new Vector3(w.x, sn, w.z), sn + 0.3f):0.000}/{Camp.TreadUnder(w):0.000}");
                    }
                    Debug.Log(sbt.ToString());
                }
            foreach (var (fx, fz, tx, tz) in new[] { (-9f, -46f, -6f, -66f), (0f, -42f, 0f, -68f), (8f, -44f, 18f, -62f), (-9f, -46f, 8f, -67f) })
            {
                var path = new List<Vector3>();
                bool okp = (bool)fp.Invoke(life, new object[] { Camp.GroundAt(fx, fz), Camp.GroundAt(tx, tz), path });
                var sbp = new System.Text.StringBuilder($"STAIRS   a path from ({fx}, {fz}) to ({tx}, {tz}): {okp}");
                foreach (var q in path) sbp.Append($" ({q.x - cx:0.0},{q.z:0.0})");
                Debug.Log(sbp.ToString());
            }
        }
        // the hero out of the way, by the lookout
        camp.TeleportTo(new Vector3(cx + 18f, 0f, -66.5f), 90f);
        int frames = Mathf.RoundToInt(180f / dt);
        int onStairs = 0, onBank = 0, feetFrames = 0, feetOff = 0, levelN = 0, levelOver = 0; float feetSum = 0f, levelSum = 0f;
        var rest = new Dictionary<CampLife.Agent, float>();
        var used = new HashSet<CampLife.Agent>(); var steep = new HashSet<CampLife.Agent>();
        bool trace = System.Environment.GetEnvironmentVariable("STAIRS_TRACE") == "1"; int traced = 0;
        var per = new Dictionary<string, (int, float, int, float)>();
        for (int f = 0; f < frames; f++)
        {
            camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt);
            life.Step(dt, cam.transform.position);
            life.Tick(dt);
            foreach (var a in life.Agents)
            {
                if (a.p == null || !a.p.gameObject.activeSelf || a.doing != CampLife.Doing.Walk) continue;
                bool st = Camp.OnStairs(a.pos, 0.1f);
                float g = 0f;
                {
                    float y0 = Slope.DrawnHeight(a.pos.x, a.pos.z - 0.5f), y1 = Slope.DrawnHeight(a.pos.x, a.pos.z + 0.5f);
                    g = Mathf.Abs(y1 - y0);
                }
                bool snow = a.pos.y - Slope.DrawnHeight(a.pos.x, a.pos.z) < 0.05f;
                if (st) { onStairs++; used.Add(a); }
                else if (snow && g > 0.3f && a.pos.z < -49f && a.pos.z > -60f)   // (up or down the bank's steep middle: not the bench on its foot)
                {
                    onBank++;
                    if (steep.Add(a) || f % 120 == 0) Debug.Log($"STAIRS   on the bank: the camp's {a.role} ({a.spot?.name}) at ({a.pos.x - cx:0.0}, {a.pos.z:0.0}) going {Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward}, {g / 1f * 100f:0}% there");
                }
                if (a.p.Lod > 1 || a.p.Rig == null) continue;
                var rig = a.p.Rig;
                if (!st)
                {
                    // (the clips' own planted ankle: on plain ground only, no foot over the stairs. Coming off the
                    // plateau a foot reaches down onto the top deck, below the bank the body is still on, and StairFeet
                    // sets it there: that is not the clip's own, and taken for it every tread after read 4 cm high)
                    // (and walking: starting off or stopping, the standing clip's ankle is lower than the walk's)
                    if (!float.IsNaN(Camp.TreadUnder(rig.Foot[0].position)) || !float.IsNaN(Camp.TreadUnder(rig.Foot[1].position)) || Camp.OnStairs(a.pos, 0.6f) || a.p.pace < 0.5f) continue;
                    float r = Mathf.Min(rig.Foot[0].position.y, rig.Foot[1].position.y) - a.pos.y;
                    if (trace && r < 0.06f && (!rest.TryGetValue(a, out float rr) || r < rr - 0.003f))
                        Debug.Log($"STAIRS   rest dips {a.role}:{a.spot?.name} to {r * 100f:0.0} cm at ({a.pos.x - cx:0.00}, {a.pos.y:0.000}, {a.pos.z:0.00}) drawn y {a.p.transform.position.y:0.000} snow {Slope.DrawnHeight(a.pos.x, a.pos.z):0.000} near stairs {Camp.OnStairs(a.pos, 0.6f)} speed {a.speed:0.00} pace {a.p.pace:0.00} step {a.p.StairStep:0.00} feet {rig.Foot[0].position.y - a.pos.y:0.000} {rig.Foot[1].position.y - a.pos.y:0.000} hips {rig.Hips.position.y - a.pos.y:0.000} lod {a.p.Lod} f{f}");
                    rest[a] = rest.TryGetValue(a, out float r0) ? Mathf.Min(r0, r) : r;
                    // the same measure on the level (the plateau's paving and snow, not the bank): the clips' own
                    if (rest.ContainsKey(a) && g < 0.06f && a.pos.z < -64f)
                    {
                        float l = Mathf.Min(Mathf.Abs(rig.Foot[0].position.y - rest[a] - a.pos.y), Mathf.Abs(rig.Foot[1].position.y - rest[a] - a.pos.y));
                        if (l < 0.07f) { levelN++; levelSum += l; if (l > 0.05f) levelOver++; }
                    }
                    continue;
                }
                if (!rest.TryGetValue(a, out float ar) || !Camp.OnStairs(a.pos)) continue;
                var facing = a.p.transform.forward;
                float e0 = SoleOver(rig.Foot[0].position, ar, facing, out _), e1 = SoleOver(rig.Foot[1].position, ar, facing, out _);
                if (float.IsNaN(e0) || float.IsNaN(e1)) continue;
                float e = Mathf.Abs(e0) < Mathf.Abs(e1) ? e0 : e1;
                if (e > 0.07f) continue;   // (both feet off: between steps)
                feetFrames++; feetSum += Mathf.Abs(e); if (Mathf.Abs(e) > 0.05f) feetOff++;
                if (trace)
                {
                    var k = $"{a.role}:{a.spot?.name} s{a.p.Scale:0.00} lod{a.p.Lod} step{a.p.StairStep:0.00} rest{ar * 100f:0.0}";
                    var v = per.TryGetValue(k, out var v0) ? v0 : (0, 0f, 0, 0f);
                    per[k] = (v.Item1 + 1, v.Item2 + Mathf.Abs(e), v.Item3 + (Mathf.Abs(e) > 0.05f ? 1 : 0), v.Item4 + e);
                    if (f % 7 == 0 && traced++ < 120) Debug.Log($"STAIRS   trace {k} at ({a.pos.x - cx:0.00}, {a.pos.y:0.00}, {a.pos.z:0.00}) e0 {e0 * 100f:0.0} e1 {e1 * 100f:0.0} foot0 {rig.Foot[0].position.y - a.pos.y:0.000} foot1 {rig.Foot[1].position.y - a.pos.y:0.000} tread0 {Camp.TreadUnder(rig.Foot[0].position):0.000} tread1 {Camp.TreadUnder(rig.Foot[1].position):0.000} speed {a.speed:0.00} pace {a.p.pace:0.00}");
                }
            }
        }
        if (trace) foreach (var kv in per) Debug.Log($"STAIRS   per person {kv.Key}: {kv.Value.Item1} frames, {kv.Value.Item2 / kv.Value.Item1 * 100f:0.0} cm off on average (signed {kv.Value.Item4 / kv.Value.Item1 * 100f:0.0}), over 5 cm {kv.Value.Item3}");
        float mean = feetFrames > 0 ? feetSum / feetFrames : 0f, lmean = levelN > 0 ? levelSum / levelN : 0f, lover = levelN > 0 ? levelOver / (float)levelN : 0f;
        bool feetOk = feetFrames == 0 || (mean < lmean + 0.015f && feetOff < feetFrames * (lover + 0.15f));
        bool wayOk = onBank <= onStairs;
        if (!feetOk || !wayOk) fails++;
        string msg = $"STAIRS the camp's people, 3 min: {used.Count} went by the stairs ({onStairs} walking frames on them), {steep.Count} over the bank's steep snow beside them ({onBank} frames); feet on the treads {mean * 100f:0.0} cm off on average, over 5 cm in {feetOff} of {feetFrames} frames (on the level {lmean * 100f:0.0} cm, over 5 cm {lover * 100f:0}%)";
        if (feetOk && wayOk) Debug.Log(msg); else Debug.LogError(msg);
    }
}
