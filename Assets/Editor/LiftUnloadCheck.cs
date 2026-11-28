using System.Collections.Generic;
using UnityEngine;
using Summit;

/// <summary>
/// The tops' unload wings (liftlook: LiftLine.UnloadWing) on a sweep of mountains, no scene (pure functions):
///   - every top but the Summit lift's has a wing, and why not if not;
///   - its clear width: from the chairs' reach (LiftLine.ChairReach, SkiLift.Chairs' hit box) to its outer edge, at
///     least ChairsWide chairs across, and nothing standing on it: no other lift's tower or station, nothing LiftEnds
///     places (its people's places and boards keep off the lane: LiftEnds.Clear), no tree or rock (the forest's own
///     cells, Forest.At: the lane is Taken);
///   - the jump line: a rider leaving the lip at 8 to 28 m/s, straight down the line and turned 6 degrees either way, from
///     its inner third, its middle and its outer third, flown (Rider.G) until the snow: it meets no chair (any chair's
///     box, anywhere on either cable, swinging), no tower (its mast and pad, its head), no station (this lift's, another's)
///     and no tree or rock. Every straight flight must be clear, and the middle's turned ones up to TurnedTop (20 m/s:
///     faster, a flight is 60 m and more, and 6 degrees in carries it 6 m across, into the chairs); the rest are counted.
///   - another lift's line crossing the landing further down (a second line laid across the run): its chairs and towers
///     are counted, and fail only within CrossNear (30 m) of the lip at TurnedTop or slower;
///   - where a top has no wing, why: the snow alone fails; a hill, the park, a cliff's wall, another lift on both sides
///     is counted.
/// -executeMethod LiftUnloadCheck.Run (UNLOAD_SWEEP=n mountains as the game picks them, default 40, and seed 1000).
/// Log lines: UNLOAD (each top), UNLOAD-FAIL, UNLOAD done.
/// </summary>
public static class LiftUnloadCheck
{
    const float ChairsWide = 3f, ChairWidth = 2.2f, Swing = 0.12f, TurnedTop = 20f, CrossNear = 30f;
    static readonly float[] Speeds = { 8f, 12f, 16f, 20f, 24f, 28f };
    static readonly float[] Yaws = { 0f, 6f, -6f };
    static int fails;
    static void Fail(string s) { fails++; Debug.Log("UNLOAD-FAIL " + s); }

    public static void Run()
    {
        int n = int.Parse(System.Environment.GetEnvironmentVariable("UNLOAD_SWEEP") ?? "40");
        var r = new System.Random(20261127);
        var seeds = new List<int> { 1000 };
        for (int i = 0; i < n; i++) seeds.Add(r.Next(1, 999983));
        int tops = 0, wings = 0, runSide = 0, lines = 0, clearLines = 0, edgeTurned = 0, edgeTurnedClear = 0, crossings = 0, crossedTops = 0, access = 0;
        float minFly = 1e9f, maxFly = 0f, minLaunch = 1e9f, maxLaunch = 0f, minGap = 1e9f;
        var whyNone = new Dictionary<string, int>();
        var probe = new GameObject("LiftEndsProbe");
        var ends = probe.AddComponent<LiftEnds>();
        try
        {
            foreach (int seed in seeds)
            {
                Slope.Init(seed); Course.ResetCaches();
                var all = LiftLine.Lifts;
                foreach (var l in all)
                {
                    if (l.summit) continue;
                    tops++;
                    var w = LiftLine.UnloadWing(l);
                    if (w == null)
                    {
                        string k = l.wingWhy; whyNone[k] = whyNone.TryGetValue(k, out int c) ? c + 1 : 1;
                        // (where something stays put beside it on both sides, a cable grind's hill and its lane, the park, a
                        // cliff's wall, another lift, the run, it is counted; where only the snow stopped it, it fails)
                        bool fixedBy = k.Contains("hill") || k.Contains("kept") || k.Contains("lift") || k.Contains("run") || k.Contains("pad") || k.Contains("hamlet");
                        if (!fixedBy) Fail($"seed{seed} lift{l.id}: no unload wing ({l.wingWhy})");
                        else Debug.Log($"UNLOAD seed{seed} lift{l.id}: no unload wing ({l.wingWhy})");
                        continue;
                    }
                    wings++; if (w.runSide) runSide++; if (w.access) access++;
                    // the clear width, and nothing standing on the wing
                    if (w.Clear < ChairsWide * ChairWidth) Fail($"seed{seed} lift{l.id}: clear width {w.Clear:0.0} m < {ChairsWide * ChairWidth:0.0}");
                    string on = Standing(l, w);
                    if (on != null) Fail($"seed{seed} lift{l.id}: on the wing: {on}");
                    // the jump line
                    int here = 0, hereClear = 0; string firstHit = null; bool crossed = false;
                    float launch = w.P(l, w.x0 + LiftLine.WingWidth * 0.5f, LiftLine.LipRise, w.Lip).y - Slope.DrawnHeight(w.P(l, w.x0 + LiftLine.WingWidth * 0.5f, 0f, w.Lip).x, w.P(l, w.x0 + LiftLine.WingWidth * 0.5f, 0f, w.Lip).z);
                    minLaunch = Mathf.Min(minLaunch, launch); maxLaunch = Mathf.Max(maxLaunch, launch);
                    foreach (float x in new[] { w.x0 + LiftLine.WingWidth / 3f, w.x0 + LiftLine.WingWidth * 0.5f, w.x1 - LiftLine.WingWidth / 3f })
                        foreach (float yaw in Yaws)
                            foreach (float v in Speeds)
                            {
                                bool middle = Mathf.Abs(x - (w.x0 + LiftLine.WingWidth * 0.5f)) < 0.01f;
                                string hit = Fly(l, w, x, yaw, v, out float fly, out float gap);
                                lines++; here++;
                                if (hit == null) { clearLines++; hereClear++; minFly = Mathf.Min(minFly, fly); maxFly = Mathf.Max(maxFly, fly); if (yaw == 0f) minGap = Mathf.Min(minGap, gap); }
                                if (!middle && yaw != 0f) { edgeTurned++; if (hit == null) edgeTurnedClear++; }
                                // (every straight flight; from the middle, turned 6 degrees either way, up to TurnedTop. Another
                                // lift's chairs further down, where a second line crosses the landing, are counted unless they
                                // come within CrossNear at TurnedTop or slower: there is room to jump off before them)
                                var of = hit != null ? System.Text.RegularExpressions.Regex.Match(hit, @"of lift (\d+)") : null;
                                bool other = of != null && of.Success && of.Groups[1].Value != l.id.ToString();
                                if (other) { crossings++; crossed = true; if (fly >= CrossNear || v > TurnedTop) continue; }
                                if (hit != null && (yaw == 0f || middle && v <= TurnedTop))
                                    Fail($"seed{seed} lift{l.id}: off the lip at x {x:0.0}, {v:0} m/s, turned {yaw:0} deg: {hit}");
                                if (hit != null && firstHit == null) firstHit = $"x{x:0.0} {v:0}m/s {yaw:0}deg: {hit}";
                            }
                    if (crossed) crossedTops++;
                    // what LiftEnds placed there: none of its people's places on the lane
                    foreach (var e in ends.Ends)
                    {
                        if (!e.top || e.lift != l) continue;
                        foreach (var p in e.strap) if (LiftLine.InUnload(p.x, p.z, 0f)) Fail($"seed{seed} lift{l.id}: a strap-in place on the lane");
                        foreach (var p in new[] { e.goggles, e.point, e.sit }) if (p != Vector3.zero && LiftLine.InUnload(p.x, p.z, 0f)) Fail($"seed{seed} lift{l.id}: someone standing on the lane");
                    }
                    Debug.Log($"UNLOAD seed{seed} lift{l.id}{(l.park ? "p" : l.second ? "s" : "")}: {l.wingWhy}; clear {w.Clear:0.0} m ({w.Clear / ChairWidth:0.0} chairs), wing {LiftLine.WingWidth:0} x {w.Lip - w.z0:0.0} m{(w.access ? "" : " (in the air behind)")}, lip {launch:0.0} m over the snow; lines clear {hereClear}/{here}{(firstHit != null ? "; first hit " + firstHit : "")}");
                }
            }
        }
        finally { Object.DestroyImmediate(probe); }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in whyNone) sb.Append($" {kv.Key} x{kv.Value};");
        Debug.Log($"UNLOAD done: {seeds.Count} mountains, {tops} tops, {wings} with a wing ({runSide} on the run's side, {access} met by the snow behind){(sb.Length > 0 ? ", none:" + sb : "")}; " +
                  $"clear width {LiftLine.WingWidth + LiftLine.StationHalfWid - LiftLine.ChairReach:0.0} m; flights clear {clearLines}/{lines} (turned from the edges {edgeTurnedClear}/{edgeTurned}), " +
                  $"{minFly:0}-{maxFly:0} m long, the lip {minLaunch:0.0}-{maxLaunch:0.0} m over the snow, straight flights nearest their own chairs' box {minGap:0.0} m; " +
                  $"{crossedTops} tops with another lift's line across the landing further down ({crossings} flights meet its chairs or towers, none within {CrossNear:0} m at {TurnedTop:0} m/s or less); {fails} failures");
        if (Application.isBatchMode) EditorExit(fails == 0 ? 0 : 1);
    }

    static void EditorExit(int code) => UnityEditor.EditorApplication.Exit(code);

    /// <summary>Anything solid standing on the wing or its lane's first metres (another lift's tower or station, a tree, a rock).</summary>
    static string Standing(LiftLine.Lift l, LiftLine.Wing w)
    {
        for (float z = w.z0; z <= w.Lip; z += 1f)
            for (float x = LiftLine.ChairReach; x <= w.x1; x += 1f)
            {
                var p = w.P(l, x, 0f, z);
                string s = Solid(l, p, 1.2f, true);
                if (s != null) return s + $" at x {x:0} z {z:0}";
            }
        return null;
    }

    /// <summary>A flight off the lip: null if clear, else what it meets. fly: how far it went; gap: nearest a chair's box.</summary>
    static string Fly(LiftLine.Lift l, LiftLine.Wing w, float x, float yaw, float speed, out float fly, out float gap)
    {
        float ang = Mathf.Atan2(LiftLine.LipRise, LiftLine.LipLen);
        // (turned toward the line for a positive yaw)
        Vector3 along = Quaternion.AngleAxis(-yaw * w.s, Vector3.up) * l.dir;
        Vector3 v = along * (speed * Mathf.Cos(ang)) + Vector3.up * (speed * Mathf.Sin(ang));
        Vector3 p = w.P(l, x, LiftLine.LipRise + Rider.R, w.Lip), p0 = p;
        fly = 0f; gap = 1e9f;
        const float dt = 0.01f;
        for (float t = 0f; t < 8f; t += dt)
        {
            v.y -= Rider.G * dt;
            p += v * dt;
            float snow = Slope.DrawnHeight(p.x, p.z);
            if (p.y - Rider.R <= snow) { fly = new Vector2(p.x - p0.x, p.z - p0.z).magnitude; return null; }
            string hit = Chairs(l, p, ref gap) ?? Solid(l, p, 0f, false);
            if (hit != null) { fly = new Vector2(p.x - p0.x, p.z - p0.z).magnitude; return hit + $" {fly:0} m out"; }
        }
        fly = new Vector2(p.x - p0.x, p.z - p0.z).magnitude;
        return null;
    }

    /// <summary>Inside any chair's box (SkiLift.Chairs: 1.4 m either side of its cable, 0.9 to 4.5 m under the grip, from the
    /// rider's point 0.75 m over the board), wherever a chair is on either cable of any lift near.</summary>
    static string Chairs(LiftLine.Lift self, Vector3 p, ref float gap)
    {
        var q = p + Vector3.up * 0.75f;
        foreach (var l in LiftLine.Lifts)
        {
            float along = (q.x - l.top.x) * l.dir.x + (q.z - l.top.z) * l.dir.z;
            if (along < -LiftLine.StationHalfLen - 4f || along > l.plan + LiftLine.StationHalfLen + 4f) continue;
            float side = (q.x - l.top.x) * l.across.x + (q.z - l.top.z) * l.across.z;
            float cable = CableY(l, along);
            foreach (float c in new[] { LiftLine.Half, -LiftLine.Half })
            {
                float lat = Mathf.Abs(side - c) - (1.4f + Swing);
                float below = cable - q.y;   // (the box: 0.9 to 4.5 under the grip)
                float vert = below < 0.9f ? 0.9f - below : below > 4.5f ? below - 4.5f : 0f;
                float d = Mathf.Sqrt(Mathf.Max(0f, lat) * Mathf.Max(0f, lat) + vert * vert);
                if (l == self) gap = Mathf.Min(gap, d);
                if (lat < 0f && vert <= 0f) return $"a chair of lift {l.id}";
            }
        }
        return null;
    }

    /// <summary>The cable's height at `along` (through the stations: their cable height; between: the spans).</summary>
    static float CableY(LiftLine.Lift l, float along)
    {
        int n = l.Spans;
        if (along <= l.nodeS[0]) return l.top.y + LiftLine.StationCable;
        if (along >= l.nodeS[n]) return l.bottom.y + LiftLine.StationCable;
        for (int i = 0; i < n; i++) if (along <= l.nodeS[i + 1]) return l.CableY(i, along);
        return l.bottom.y + LiftLine.StationCable;
    }

    /// <summary>A tower (mast and pad, its head), a station (deck to roof, legs under it), a tree or a rock at p (pad: a
    /// margin round each, for things standing on the wing; ground: test at the snow, ignoring height).</summary>
    static string Solid(LiftLine.Lift self, Vector3 p, float pad, bool ground)
    {
        float R = Rider.R + pad;
        foreach (var l in LiftLine.Lifts)
        {
            float dx0 = p.x - l.top.x, dz0 = p.z - l.top.z;
            float along = dx0 * l.dir.x + dz0 * l.dir.z, side = dx0 * l.across.x + dz0 * l.across.z;
            if (along < -60f || along > l.plan + 60f) continue;
            for (int i = 0; i < l.foot.Length; i++)
            {
                Vector3 f = l.foot[i], h = l.head[i];
                float d = new Vector2(p.x - f.x, p.z - f.z).magnitude;
                if (d < 0.8f + R && (ground || p.y < h.y + 1f)) return $"tower {i} of lift {l.id}";
                // its head: the crossarm across the line, the sheave trains along it
                float ha = (p.x - h.x) * l.dir.x + (p.z - h.z) * l.dir.z, hs = (p.x - h.x) * l.across.x + (p.z - h.z) * l.across.z;
                if (!ground && Mathf.Abs(hs) < LiftLine.Half + 1.1f + R && Mathf.Abs(ha) < 2.7f + R && p.y > h.y - 3.4f - R && p.y < h.y + 3f + R) return $"the head of tower {i} of lift {l.id}";
            }
            // its stations (this lift's top: its own deck and wing are what the rider leaves from, so only its housing)
            foreach (bool top in new[] { true, false })
            {
                Vector3 st = top ? l.top : l.bottom;
                float a = (p.x - st.x) * l.dir.x + (p.z - st.z) * l.dir.z, s = (p.x - st.x) * l.across.x + (p.z - st.z) * l.across.z;
                if (Mathf.Abs(a) > LiftLine.StationHalfLen + R || Mathf.Abs(s) > LiftLine.StationHalfWid + R) continue;
                if (l == self && top && (ground || p.y > st.y - 0.5f) && p.y < st.y + LiftLine.StationCable) continue;
                if (ground || (p.y > st.y - 14.5f && p.y < st.y + 8.2f)) return $"the {(top ? "top" : "bottom")} station of lift {l.id}";
            }
        }
        // the forest's own cells round p
        int cx = Mathf.FloorToInt(p.x / Forest.Cell), cz = Mathf.FloorToInt(p.z / Forest.Cell);
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                var k = Forest.At(cx + i, cz + j, out Vector3 at, out float size, out _);
                if (k == Forest.Kind.None) continue;
                float d = new Vector2(p.x - at.x, p.z - at.z).magnitude;
                float r = k == Forest.Kind.Tree ? 1.8f * size : 1.0f * size, h = k == Forest.Kind.Tree ? 14f * size : 0.9f * size;
                if (d < r + R && (ground || p.y < at.y + h)) return k == Forest.Kind.Tree ? $"a tree ({d:0.0} m)" : $"a rock ({d:0.0} m)";
            }
        return null;
    }
}
