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
        int summits = 0, parks = 0, across = 0, cycles = 0, lifts = 0;
        try
        {
            foreach (int seed in Seeds)
            {
                Slope.Init(seed); Course.ResetCaches();
                var all = LiftLine.Lifts;
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
        Debug.Log($"LAYOUT done: {lifts} lifts on {Seeds.Length} mountains; {summits} Summit lifts at the camp; {parks} park lifts and {across} lines across the run in {cycles} cycles with a first line; {fails} failure(s)");
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
