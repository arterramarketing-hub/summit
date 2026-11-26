using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// The lift layout on a dozen mountains (no scene: pure functions): every lift's report line (LiftLine.Report), and the
/// checks the layout keeps:
///   - the Summit lift: there on every mountain, its top station beside the camp (its deck within SummitReach of the
///     camp's left edge and no more than SummitDrop below the camp's plateau), its bottom at cycle 0's gathering point;
///   - the park lifts: where laid, bottom at the park's run-out and top at its top, on the other side of the run from the
///     first line, beside it no nearer than LiftLine.ParkApart, never carried over another line;
///   - no two stations within LiftLine.StationApart (Crowds), as before.
/// -executeMethod LiftLayoutCheck.Run (LAYOUT_SEEDS=...). Log lines: LAYOUT, LAYOUT-FAIL, LAYOUT done.
/// </summary>
public static class LiftLayoutCheck
{
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("LAYOUT_SEEDS") ?? "1000,1077,1154,1231,1308,1385,1462,1539,1616,1693,2131,2208").Split(','), int.Parse);
    const float SummitReach = 32f, SummitDrop = 6f;
    static int fails;
    static void Fail(string s) { fails++; Debug.Log("LAYOUT-FAIL " + s); }

    public static void Run()
    {
        int summits = 0, parks = 0, across = 0, cycles = 0, lifts = 0, onPad = 0;
        try
        {
            foreach (int seed in Seeds)
            {
                Slope.Init(seed); Course.ResetCaches();
                var all = LiftLine.Lifts;
                if (System.Environment.GetEnvironmentVariable("LAYOUT_PROBE") == "1")
                {
                    // (the ground down the line from the top's pad toward the usual bottom: shaped / the mountain's own, and its fall)
                    float cxp = Slope.Centre(Slope.PadLip);
                    var a = new Vector3(cxp - 50f, 0f, -34f); var b = new Vector3(Slope.Centre(428f) - 72f, 0f, 428f);
                    var d = (b - a).normalized; var sbp = new System.Text.StringBuilder();
                    float y0 = Slope.DrawnHeight(cxp, 0f), prev = float.NaN;
                    for (float s0 = 0f; s0 <= 160f; s0 += 4f)
                    {
                        var p = a + d * s0; float h = Slope.Height(p.x, p.z);
                        sbp.Append($"{s0:0}:{h - y0:0.0}/{LiftTop.RawHeight(p.x, p.z) - y0:0.0}{(float.IsNaN(prev) ? "" : $"({(h - prev) / 4f:0.00})")} ");
                        prev = h;
                    }
                    Debug.Log($"LAYOUT seed{seed} probe down the line from the pad: {sbp}");
                    var gok = typeof(LiftLine).GetMethod("GroundOk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var kept = typeof(LiftLine).GetMethod("Kept", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var toff = (float)typeof(LiftLine).GetField("TowerOff", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).GetValue(null);
                    var sbg = new System.Text.StringBuilder();
                    for (float s0 = 20f; s0 <= 260f; s0 += 6f)
                    {
                        var p = a + d * s0;
                        bool g = (bool)gok.Invoke(null, new object[] { p.x, p.z, toff }), k = (bool)kept.Invoke(null, new object[] { p.x, p.z });
                        var nn = Slope.Normal(p.x, p.z); sbg.Append($"{s0:0}:{(g ? "ok" : k ? "KEPT" : "no" + nn.y.ToString("0.00"))}{(Course.Keep(p.x, p.z, 3f) ? "c" : "")}{(Cliffs.WallCut(p.x, p.z) < -0.01f ? "w" : "")}{(LiftTop.OnWayDown(p.x, p.z, 2.5f) ? "W" : "")} ");
                    }
                    Debug.Log($"LAYOUT seed{seed} tower spots down the line: {sbg}");
                }
                lifts += all.Length;
                Debug.Log($"LAYOUT seed{seed}: {all.Length} lifts in {LiftLine.LaidMs:0} ms\n" + LiftLine.Report());
                LiftLine.Lift summit = null;
                foreach (var l in all) if (l.summit) summit = l;
                if (summit == null)
                {
                    var sbw = new System.Text.StringBuilder();
                    foreach (var kv in LiftLine.SummitWhy) sbw.Append($" {kv.Key} x{kv.Value}");
                    Fail($"seed{seed}: no Summit lift (tries:{sbw}); wall side {(Cliffs.WallOf(0, out var ww) ? ww.side : 0f)}");
                }
                else
                {
                    summits++;
                    float cx = Slope.Centre(Slope.PadLip);
                    float plateau = Camp.GroundAt(-20f, summit.top.z).y;
                    float edge = Mathf.Abs(summit.top.x - cx) - Camp.HalfWidth;
                    Debug.Log($"LAYOUT seed{seed} Summit: top at z {summit.top.z:0}, {summit.top.x - cx:+0;-0} m off the run ({edge:0} m past the camp's edge), deck {summit.top.y - plateau:+0.0;-0.0} m against the plateau; bottom at z {summit.bottom.z:0} ({Course.At(summit.bottom.z)}); {summit.plan:0} m, {summit.top.y - summit.bottom.y:0} m up, {summit.s.Length} towers");
                    if (edge > SummitReach) Fail($"seed{seed}: the Summit lift's top is {edge:0} m past the camp's edge");
                    // (lifttop2: its top's place is on the pad at the gate's level, LiftTop; say why not, where it isn't)
                    if (LiftTop.On && LiftTop.AtBench(summit))
                    {
                        onPad++;
                        // (lifttop2) its deck flush with the snow: beside its sides and off its uphill end (people get off
                        // there) the snow within a few centimetres of the deck's top; off its downhill end, under the chairs
                        // coming in, falling away from it
                        float worst = 0f; string where = "";
                        float W = LiftLine.StationHalfWid, L = LiftLine.StationHalfLen;
                        foreach (var (u, v, name) in new[] { (W + 0.4f, 0f, "a side"), (-W - 0.4f, 0f, "a side"), (W + 0.4f, -L + 1f, "a side"), (-W - 0.4f, -L + 1f, "a side"), (0f, -L - 0.4f, "the uphill end"), (W - 1f, -L - 0.4f, "the uphill end"), (-W + 1f, -L - 0.4f, "the uphill end") })
                        {
                            var p = summit.top + summit.across * u + summit.dir * v;
                            float gap = summit.top.y - Slope.Height(p.x, p.z);
                            if (Mathf.Abs(gap) > Mathf.Abs(worst)) { worst = gap; where = name; }
                        }
                        var dp = summit.top + summit.dir * (L + 3f);
                        float fall = summit.top.y - Slope.Height(dp.x, dp.z);
                        Debug.Log($"LAYOUT seed{seed} Summit: on the top's pad; its deck {worst:+0.00;-0.00} m over the snow at most ({where}), {fall:0.0} m over it 3 m past its downhill end");
                        if (Mathf.Abs(worst) > 0.12f) Fail($"seed{seed}: the Summit lift's deck isn't flush with the snow ({worst:+0.00;-0.00} m at {where})");
                    }
                    else if (LiftTop.On)
                    {
                        var sbw = new System.Text.StringBuilder();
                        foreach (var kv in LiftLine.SummitWhy) sbw.Append($" {kv.Key} x{kv.Value}");
                        Debug.Log($"LAYOUT seed{seed} Summit: not on the top's pad (tries:{sbw})");
                    }
                    if (plateau - summit.top.y > SummitDrop) Fail($"seed{seed}: the Summit lift's deck is {plateau - summit.top.y:0.0} m below the camp");
                }
                for (int k = 0; k < 8; k++)
                {
                    LiftLine.Lift first = null, second = null;
                    foreach (var l in all) if (l.k == k) { if (l.second) second = l; else first = l; }
                    if (first == null) continue;
                    cycles++;
                    if (second == null) continue;
                    if (!second.park) { across++; continue; }
                    parks++;
                    float b = Course.First + k * Course.Cycle;
                    if (Course.At(second.top.z) != Course.Section.Cruise && Course.At(second.top.z) != Course.Section.Park) Fail($"seed{seed} cycle{k}: the park lift's top is in the {Course.At(second.top.z)}");
                    if (second.under.Length > 0) Fail($"seed{seed} cycle{k}: the park lift is carried over another line");
                    float sp = Mathf.Sign(second.offTop), sf = Mathf.Sign(Mathf.Lerp(first.offTop, first.offBottom, 0.5f));
                    float near = Mathf.Min(LiftLine.Alongside(second, first, LiftLine.ParkApart), LiftLine.Alongside(first, second, LiftLine.ParkApart));
                    Debug.Log($"LAYOUT seed{seed} cycle{k} park lift: z {second.top.z - b:0}..{second.bottom.z - b:0} into the cycle, {second.offTop:+0;-0}/{second.offBottom:+0;-0} m off the run (first line {first.offTop:+0;-0}/{first.offBottom:+0;-0}), {second.plan:0} m; {(second.hill != null ? "a hill" : "no hill")}");
                    if (near >= LiftLine.CrowdRun) Fail($"seed{seed} cycle{k}: the park lift runs within {LiftLine.ParkApart} m of the first line for {near:0} m");
                }
                foreach (var l in all) if (LiftLine.Crowds(l, all) && l.park == false && !l.summit && l.second) Fail($"seed{seed}: lift {l.k}b crowds another");
            }
        }
        catch (System.Exception e) { Fail("exception " + e); }
        Debug.Log($"LAYOUT done: {lifts} lifts on {Seeds.Length} mountains; {summits} Summit lifts at the camp ({onPad} on the top's pad at the gate's level); {parks} park lifts and {across} lines across the run in {cycles} cycles with a first line; {fails} failure(s)");
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }
}
