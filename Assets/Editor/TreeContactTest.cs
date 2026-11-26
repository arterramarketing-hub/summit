using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// treeline: does the forest only break where the rider really touches it?
///  - Rides: a bot weaves through the lower forest beside the run (the subalpine glades and the full forest, low on the
///    mountain), and every branch torn out (TreeBreak.Brushed) and every thin trunk or top snapped (TreeBreak.Broke) is
///    checked against the tree as it is drawn: the nearest the rider's body (a segment from the board up to the
///    shoulders) came to any of the tree's own triangles that step. Further than TreeBreak.BodyR + Clear m off: broken
///    without contact. Judged: none.
///  - Hits: the same bot aimed into trees' crowns and at small trees' trunks must still break them.
///  - Afterwards: every broken top and torn branch has come down and lies on the snow (none left hanging).
/// The same rides with the old rule (TreeBreak.Envelope) are reported too: the before.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod TreeContactTest.Run -logFile tree.log
/// Log lines: TREE, TREE-FAIL, TREECONTACT PASS/FAIL.
/// </summary>
public static class TreeContactTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    const float Clear = 0.15f;   // m past the body's own radius that still counts as touching (the cards' fuzzy edges)
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static int fails;
    static void Fail(string s) { fails++; Debug.Log("TREE-FAIL " + s); }

    struct Tally { public int brushes, breaks, touched, grazed, missed; public float worst; }
    static Tally T;
    static Vector3 stepFrom, stepTo;   // the board this step: where it is, and where the step takes it

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
        r.input = null;
        try
        {
            if (!RealFir.Ready || RealFir.TestVerts == null) { Fail("the realistic trees are not loaded"); }
            else
            {
                var tb = TreeBreak.Instance;
                tb.Brushed += (at, side, depth) => Judge(at, false);
                tb.Broke += (at, speed, top) => Judge(at, true);
                foreach (bool envelope in new[] { true, false })
                {
                    TreeBreak.Envelope = envelope;
                    string rule = envelope ? "before (the crown's envelope)" : "after (the tree as drawn)";
                    // weaving beside the run through the lower forest
                    T = default;
                    foreach (int seed in new[] { 1000, 4242, 77 })
                    {
                        TestBoot.NewMountain(game, seed, terrain, props);
                        foreach (float side in new[] { -1f, 1f })
                            foreach (float edge in new[] { 3f, 9f })
                                Weave(seed, side, edge);
                    }
                    Debug.Log($"TREE {rule}, weaving the lower forest: {T.brushes} branch tears and {T.breaks} trunks or tops snapped; " +
                              $"{T.touched} where the body met the drawn tree, {T.grazed} within {Clear:0.00} m of it, {T.missed} without contact (the furthest {T.worst:0.00} m clear of it)");
                    if (!envelope && T.missed > 0) Fail($"{T.missed} branches or trunks broke without the rider touching them");
                    var weave = T;
                    // aimed into the trees: real hits must still break
                    T = default;
                    foreach (int seed in new[] { 1000, 4242 })
                    {
                        TestBoot.NewMountain(game, seed, terrain, props);
                        Aimed(seed);
                    }
                    Debug.Log($"TREE {rule}, aimed into trees: {T.brushes} branch tears and {T.breaks} trunks or tops snapped; {T.touched} touched, {T.grazed} grazed, {T.missed} without contact");
                    if (!envelope && T.brushes + T.breaks == 0) Fail("riding into trees broke nothing: real hits must still break");
                    if (!envelope && T.missed > 0) Fail($"aimed rides: {T.missed} broke without contact");
                    // and it all comes down
                    for (int i = 0; i < 120 * 6; i++) tb.Step(Dt);
                    float hung = tb.HighestRest(), lies = tb.HighestSprig(out int flying);
                    Debug.Log($"TREE {rule}, 6 s after: the broken tops lie at most {hung:0.00} m off the snow ({tb.Count} lying), the torn branches at most {lies:0.00} m ({tb.SprigCount} in all, {flying} still in the air)");
                    if (!envelope && (hung > 0.6f || flying > 0 || lies > 0.1f)) Fail($"broken pieces left in the air: tops {hung:0.00} m up, {flying} branches flying, {lies:0.00} m up");
                }
                // tall trees' tops knocked off slowly (the butt barely pushed off its stump): do they come down?
                foreach (bool old in new[] { true, false })
                {
                    float hold = TreeBreak.StumpHold;
                    if (old) TreeBreak.StumpHold = 1e6f;
                    TestBoot.NewMountain(game, 1000, terrain, props);
                    int tops = Tops(tb);
                    for (int i = 0; i < 120 * 8; i++) tb.Step(Dt);
                    float hung = tb.HighestRest();
                    Debug.Log($"TREE {(old ? "before (a butt may rest on its stump for good)" : "after")}: {tops} tall trees' tops knocked off slowly; 8 s on they lie at most {hung:0.00} m off the snow");
                    if (!old && hung > 0.6f) Fail($"a broken top is left hanging {hung:0.00} m off the snow");
                    TreeBreak.StumpHold = hold;
                }
            }
        }
        catch (System.Exception e) { Fail("exception " + e); }
        TreeBreak.Envelope = false;
        Debug.Log($"TREECONTACT {(fails == 0 ? "PASS" : "FAIL")} ({fails} failures)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>The tall trees nearest the run in the lower forest: each one's top knocked off a metre above its solid
    /// post, slowly (5 m/s: the butt hardly pushed off the stump). How many broke.</summary>
    static int Tops(TreeBreak tb)
    {
        var brk = typeof(TreeBreak).GetMethod("BreakThin", BindingFlags.NonPublic | BindingFlags.Instance);
        var near = new List<Forest.Tree>(); int n = 0;
        for (float z = FirstLowZ() + 30f; z < Slope.FinishZ - 200f && n < 6; z += 61f)
            foreach (float side in new[] { -1f, 1f })
            {
                if (n >= 6) break;
                float x = Slope.Centre(z) + side * (Slope.Half(z) + 4f);
                r.ResetAt(x, z); terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
                props.TreesNear(new Vector3(x, 0f, z), near);
                foreach (var t in near)
                {
                    if (!t.realistic || t.girth < Forest.ThinGirth || t.top < t.solid + 4f) continue;
                    var args = new object[] { t, t.solid + 1f, Vector3.forward * 5f, 5f, true };
                    if ((bool)brk.Invoke(tb, args)) { n++; break; }
                }
            }
        return n;
    }

    /// <summary>A break just reported at `at`: how near the rider's body came to the tree it was in, as drawn.</summary>
    static void Judge(Vector3 at, bool trunk)
    {
        if (trunk) T.breaks++; else T.brushes++;
        float best = float.MaxValue;
        var near = new List<Forest.Tree>();
        props.TreesNear(at, near);
        foreach (var t in near)
        {
            if (!t.realistic || RealFir.TestVerts[t.idx] == null) continue;
            if (new Vector2(t.at.x - at.x, t.at.z - at.z).magnitude > 14f) continue;
            best = Mathf.Min(best, Clearance(t, trunk));
        }
        // (a small tree snapped at the snow is gone from the forest by the time it is reported: judged by the board's
        // nearest to where its trunk broke, against the body's reach and a thin trunk's girth)
        if (trunk && best == float.MaxValue)
        {
            Vector3 d = at - stepFrom; d.y = 0f;
            best = Mathf.Max(0f, d.magnitude - Forest.ThinGirth);
        }
        if (best <= TreeBreak.BodyR) T.touched++;
        else if (best <= TreeBreak.BodyR + Clear) T.grazed++;
        else
        {
            T.missed++; T.worst = Mathf.Max(T.worst, best - TreeBreak.BodyR);
            if (T.missed <= 5)
            {
                string trees = "";
                foreach (var t in near) if (new Vector2(t.at.x - at.x, t.at.z - at.z).magnitude < 8f) trees += $" [idx {t.idx} real {t.realistic} at {t.at} h-of-body {Vector3.Dot(stepFrom - t.at, t.up):0.0} flat {new Vector2(t.at.x - stepFrom.x, t.at.z - stepFrom.z).magnitude:0.0} top {t.top:0.0} clear {(t.realistic && RealFir.TestVerts[t.idx] != null ? Clearance(t, trunk) - TreeBreak.BodyR : -1f):0.00}]";
                Debug.Log($"TREE-MISS {(trunk ? "trunk" : "branches")} at {at} broke {best - TreeBreak.BodyR:0.00} m clear of the body; rider {stepFrom} air {r.Airborne} crashed {r.Crashed} speed {r.Speed:0.0}; trees{trees}");
            }
        }
    }

    /// <summary>The nearest the body's segment (the board, this step, up to the shoulders) comes to tree t's triangles.</summary>
    static float Clearance(Forest.Tree t, bool whole)
    {
        var v = RealFir.TestVerts[t.idx]; var tri = RealFir.TestTris[t.idx];
        Vector3 W(Vector3 m) => Forest.ModelToWorld(t, m);
        // (only what still stands: above a clipped top is gone. A top just snapped is judged on the tree as it was met)
        float topY = whole ? float.MaxValue : Forest.ModelTop(t);
        // (the body as TreeBreak takes it: from the board's sphere up 1.1 m, at the start and the end of the step)
        var pts = new Vector3[16];
        for (int i = 0; i < 16; i++)
        {
            float a = (i / 2) / 7f;
            Vector3 board = (i % 2) == 0 ? stepFrom : stepTo;
            pts[i] = board + Vector3.up * (1.1f * a);
        }
        float best = float.MaxValue;
        for (int k = 0; k + 2 < tri.Length; k += 3)
        {
            Vector3 ma = v[tri[k]], mb = v[tri[k + 1]], mc = v[tri[k + 2]];
            if (ma.y > topY && mb.y > topY && mc.y > topY) continue;
            Vector3 a = W(ma), b = W(mb), c = W(mc);
            // (a quick reject: the triangle's box is far from the body)
            Vector3 lo = Vector3.Min(Vector3.Min(a, b), c), hi = Vector3.Max(Vector3.Max(a, b), c);
            Vector3 pl = Vector3.Min(stepFrom, stepTo), ph = Vector3.Max(stepFrom, stepTo) + Vector3.up * 1.1f;
            if (best > 50f) best = 50f;
            if (lo.x > ph.x + best || hi.x < pl.x - best || lo.z > ph.z + best || hi.z < pl.z - best || lo.y > ph.y + best || hi.y < pl.y - best) continue;
            foreach (var p in pts) best = Mathf.Min(best, (Closest(p, a, b, c) - p).magnitude);
        }
        return best;
    }

    /// <summary>The point of triangle abc nearest p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float den = 1f / (va + vb + vc);
        return a + ab * (vb * den) + ac * (vc * den);
    }

    static void Step()
    {
        Vector3 p = r.Body.position, v = r.Body.linearVelocity;
        stepFrom = p; stepTo = p + v * Dt;
        r.TestContacts(Dt);
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
    }

    static float FirstLowZ()
    {
        for (float z = Course.First; z < Slope.FinishZ; z += 20f) if (Forest.Low(z) > Forest.Subalpine) return z;
        return Slope.FinishZ * 0.6f;
    }

    /// <summary>Weaving `edge` m beyond the run's edge on one side, through the lower forest, 30 s.</summary>
    static void Weave(int seed, float side, float edge)
    {
        float z0 = FirstLowZ();
        float x0 = Slope.Centre(z0) + side * (Slope.Half(z0) + edge);
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * 14f;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        int n = 0;
        for (float t = 0f; t < 30f && r.Z < Slope.FinishZ - 50f; t += Dt, n++)
        {
            float ph = seed * 0.37f + side;
            r.Steer = r.Crashed ? 0f : CarveBot.Onto(r, zz => Slope.Centre(zz) + side * (Slope.Half(zz) + edge + 3f * Mathf.Sin(zz / 23f + ph)), 1.2f, 8f, 4f);
            if (r.Speed > 22f && !r.Airborne) r.Body.linearVelocity *= 0.995f;   // (a tree-weaving pace)
            Step();
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (r.Crashed) { if (r.CanGetUp) r.RequestUp = true; }
        }
    }

    /// <summary>At the trees nearest the run's edges in the lower forest: aimed at their crowns (half way out) and, for
    /// small trees, at the trunk. 12 trees a mountain.</summary>
    static void Aimed(int seed)
    {
        float z = FirstLowZ() + 40f; int done = 0;
        var near = new List<Forest.Tree>();
        for (; z < Slope.FinishZ - 200f && done < 12; z += 37f)
            foreach (float side in new[] { -1f, 1f })
            {
                if (done >= 12) break;
                float x = Slope.Centre(z) + side * (Slope.Half(z) + 5f);
                r.ResetAt(Slope.Centre(z - 40f) + side * (Slope.Half(z) - 2f), z - 40f); body.ResetVisual();
                terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
                props.TreesNear(new Vector3(x, 0f, z), near);
                bool found = false; Forest.Tree pick = default; float bestD = float.MaxValue;
                foreach (var t in near)
                {
                    if (!t.realistic) continue;
                    float d = Mathf.Abs(t.at.z - z) + Mathf.Abs(t.at.x - x) * 0.5f;
                    if (d < bestD) { bestD = d; pick = t; found = true; }
                }
                if (!found) continue;
                done++;
                bool small = pick.girth < Forest.ThinGirth;
                float aimOff = small ? 0f : 0.5f * Forest.CrownAt(pick, 0.8f);
                Vector3 aim = pick.at + Vector3.right * (aimOff * -side);
                Vector3 from = aim - new Vector3(0f, 0f, 30f);
                from.x += (aim.x - from.x) * 0f;
                r.ResetAt(from.x, from.z); body.ResetVisual(); r.Begin();
                r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(from.x, from.z)).normalized * 12f;
                terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
                for (float t = 0f; t < 4f && r.Z < aim.z + 6f && !r.Crashed; t += Dt)
                {
                    float ax = aim.x;
                    r.Steer = CarveBot.Onto(r, zz => ax, 2f, 8f);
                    if (r.Speed > 18f && !r.Airborne) r.Body.linearVelocity *= 0.99f;
                    Step();
                }
                if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 400 && r.Crashed; i++) { r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); if (r.CanGetUp) r.RequestUp = true; } }
            }
    }
}
