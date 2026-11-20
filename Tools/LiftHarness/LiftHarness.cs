// Runs the game's own lift layout (Assets/Summit/Scripts/LiftLine.cs) on the game's own mountain (Slope, Course,
// Ridge, Cliffs, Deadly), outside Unity, and checks every lift it lays. UnityStub.cs stands in for the few
// UnityEngine maths types those files use. With mono installed (apt-get install mono-mcs):
//
//   S=Assets/Summit/Scripts
//   mcs -langversion:latest -out:/tmp/lift.exe Tools/LiftHarness/*.cs $S/Slope.cs $S/Course.cs $S/Ridge.cs $S/Cliffs.cs $S/Deadly.cs $S/LiftLine.cs $S/LiftNetwork.cs $S/LiftRamp.cs $S/CableTune.cs
//   mono /tmp/lift.exe 1 3000          seeds 1..3000: a summary, and every problem found
//   mono /tmp/lift.exe 4242            one mountain: every lift, tower and span listed
//
// What it confirms: every tower and station off the piste, off kept ground (Course.Keep), out of the ridge
// stretches and their canyons, not under a deadly cliff; the lowest thing on every chair clear of the snow by
// the margins LiftLine asks for, along both cables every 1.5 m; the chairs' loop continuous and closed; and each
// lift's launch ramp (LiftRamp.cs): off the piste, on sound ground, under the chairs with room to spare, and the
// cable over it reached by the flight off its lip at the speed it claims. And for each cycle's second line
// (LiftNetwork.cs), all of the above, and wherever two lines cross: the upper one's chairs clear of the lower one's
// cable and tower heads, no tower or station of either under the other's chairs.
// (Without mono, Unity's own compiler will do: Tools/LiftHarness/run.py builds and runs it with the editor's Roslyn.)
using System;
using System.Collections.Generic;
using UnityEngine;
using Summit;

public static class LiftHarness
{
    static int lifts, towers, problems, crossing, dangerTowers, missing, tallOver15, longSpans, spans;
    static float minOff = 1e9f, minPiste = 1e9f, maxH, maxSpan, minSpan = 1e9f, minTowerOff = 1e9f, maxTries, maxAngle, minPlan = 1e9f, maxPlan, maxCross;
    static readonly List<float> heights = new List<float>(), spanList = new List<float>(), plans = new List<float>();
    static int ramps, rampsAtWant, mountainsWithRamp, mountainsAtWant;
    static readonly int[] needHist = new int[8];
    static float minRampOff = 1e9f, maxLip, minHeadroom = 1e9f;
    static int grinds, relands; static float minHopClear = 1e9f, maxGrindSpeed, sumGrindTime, sumSteer, minLandClear = 1e9f;
    static int seconds, secondCycles, secondsOver, secondRamps, secondTowers, mountainsNetworked, crossings; static float minOver = 1e9f, maxSecondH, minSecondAngle = 1e9f, maxSecondAngle, maxSecondTries;
    static readonly List<float> secondH = new List<float>();

    public static void Main(string[] args)
    {
        int lo = int.Parse(args[0]), hi = args.Length > 1 ? int.Parse(args[1]) : lo;
        bool verbose = lo == hi;
        for (int seed = lo; seed <= hi; seed++)
        {
            Slope.Init(seed);
            var all = LiftLine.Lifts;
            int want = Course.CycleOf(Slope.FinishZ) + 1, firsts = 0, had = seconds;
            foreach (var l in all) if (!l.second) firsts++;
            missing += want - firsts;
            secondCycles += firsts;
            for (int i = 0; i < all.Length; i++) if (all[i].id != i) Bad(seed, all[i], "its id is not its place in Lifts");
            foreach (var l in all) Check(seed, l, verbose);
            foreach (var l in all) if (l.second) CheckSecond(seed, l, all, verbose);
            CheckCrowds(seed, all);
            int gb = gorges;
            foreach (var l in all) if (l.gorge >= 0) CheckGorge(seed, l, verbose);
            if (gorges > gb) mountainsGorged++;
            {
                bool can = false; int last = Course.CycleOf(Slope.FinishZ);
                for (int g = 0; g < last; g++)
                {
                    float ws = Cliffs.WallOf(g, out var w) ? w.side : 0f;
                    foreach (float cs in new[] { -1f, 1f }) if (Ridge.Built(g) && Ridge.CliffSide(Course.First + g * Course.Cycle + Ridge.At + 100f, cs) && cs != ws) can = true;
                }
                if (can) { gorgeable++; if (gorges == gb) noGorge.Add(seed); }
            }
            if (verbose) Console.Write(LiftLine.GorgeReport());
            CheckPairs(seed, all, verbose);
            if (seconds > had) mountainsNetworked++;
            int before = ramps, beforeWant = rampsAtWant;
            foreach (var l in all) CheckRamp(seed, l, verbose);
            if (ramps > before) mountainsWithRamp++;
            if (rampsAtWant > beforeWant) mountainsAtWant++;
            if (verbose) Console.Write(LiftLine.RampReport());
        }
        heights.Sort(); spanList.Sort(); plans.Sort();
        Console.WriteLine("{0} lifts on {1} mountains ({2} first lines not built); {3} towers", lifts, hi - lo + 1, missing, towers);
        Console.WriteLine("PROBLEMS: {0}", problems);
        Console.WriteLine("lift length in plan: {0:0} to {1:0} m, median {2:0}", minPlan, maxPlan, plans[plans.Count / 2]);
        Console.WriteLine("spans between towers: {0:0} to {1:0} m, median {2:0}; over 95 m: {3:0.0}%", minSpan, maxSpan, spanList[spanList.Count / 2], 100f * longSpans / spans);
        Console.WriteLine("tower heights: {0:0.0} to {1:0.0} m, median {2:0.0}, 90% under {3:0.0}; over 15 m: {4:0.0}%", heights[0], maxH, heights[heights.Count / 2], heights[heights.Count * 9 / 10], 100f * tallOver15 / towers);
        Console.WriteLine("nearest a tower's foot comes to the piste's centre line: {0:0.0} m", minTowerOff);
        Console.WriteLine("least clearance under a chair: {0:0.00} m off the piste, {1:0.00} m over it", minOff, minPiste);
        Console.WriteLine("lifts with chairs over the piste: {0} ({1:0}%); longest span over it {2:0} m", crossing, 100f * crossing / lifts, maxCross);
        Console.WriteLine("towers in a danger stretch (the side away from its cliff): {0}", dangerTowers);
        Console.WriteLine("most lines tried for one first line: {0:0}; steepest first line off the fall line: {1:0.0} deg", maxTries, maxAngle);
        Console.WriteLine("launch ramps: {0} of {1} lifts have one ({2:0}%); {3} ({4:0}%) reach the cable at {5:0} m/s or less", ramps, lifts, 100f * ramps / lifts, rampsAtWant, 100f * rampsAtWant / Mathf.Max(1, lifts), CableTune.RampWantSpeed);
        Console.WriteLine("mountains with at least one ramp: {0} of {1} ({2:0.0}%); with one reached at {3:0} m/s or less: {4} ({5:0.0}%)", mountainsWithRamp, hi - lo + 1, 100f * mountainsWithRamp / (hi - lo + 1), CableTune.RampWantSpeed, mountainsAtWant, 100f * mountainsAtWant / (hi - lo + 1));
        Console.Write("speed a ramp needs (m/s: ramps):"); for (int i = 0; i < needHist.Length; i++) if (needHist[i] > 0) Console.Write("  {0}: {1}", 18 + 2 * i, needHist[i]); Console.WriteLine();
        Console.WriteLine("a pop alone (8 m/s across the slope, no ramp) tops out {0:0.0} m over the snow; a Super Jump {1:0.0} m; the lowest cable is {2:0.0} m up", 9.9f * 9.9f / 44f, 18.8f * 18.8f / 44f, minOff + LiftLine.ChairDrop);
        if (grinds > 0) Console.WriteLine("grinds from each ramp's meeting point: {0}; they last {1:0.0} s on average and reach {2:0} m/s at most; the hop clears the cable at the tower by {3:0.00} m at least; {4} ({5:0}%) come down on the next span if steered back (by {6:0.0} m on average)", grinds, sumGrindTime / grinds, maxGrindSpeed, minHopClear, relands, 100f * relands / grinds, relands > 0 ? sumSteer / relands : 0f);
        Console.WriteLine("ramps: nearest the piste's centre {0:0.0} m; tallest lip {1:0.00} m; least room between a lip and the chairs {2:0.00} m", minRampOff, maxLip, minHeadroom);
        Console.WriteLine("side by side (within {0:0} deg and {1:0} m of each other for {2:0} m or more): {3} pairs; stations within {4:0} m of another's: {5}", LiftLine.CrowdAngle, LiftLine.CrowdNear, LiftLine.CrowdRun, crowdPairs, LiftLine.StationApart, crowdStations);
        Console.WriteLine("gorge lines: {0} on {1} mountains; {2} mountains have a ridge with a cliff side clear of its danger stretch's deadly wall, {3} of them without a gorge line{4}", gorges, mountainsGorged, gorgeable, noGorge.Count, noGorge.Count > 0 && noGorge.Count <= 30 ? " (seeds " + string.Join(",", noGorge) + ")" : "");
        if (gorges > 0)
        {
            foreach (var v in new[] { gSpan, gDrop, gSag, gFell, gTime, gTop }) v.Sort();
            Func<List<float>, string> R = v => v.Count == 0 ? "-" : string.Format("{0:0} / {1:0} / {2:0}", v[0], v[v.Count / 2], v[v.Count - 1]);
            Console.WriteLine("gorge lines (least / median / most): span over the gorge {0} m, its sag {1} m, chairs over the ground {2} m, tallest tower {3} m; {4} ({5:0}%) have a launch ramp leading onto the gorge", R(gSpan), R(gSag), R(gDrop), R(gTop), gorgeRamps, 100f * gorgeRamps / gorges);
            Console.WriteLine("gorge grinds: {0} of {1} rides from the rim tower's hop at 8 to 40 m/s get across, in {2} s; pops off the gorge span: {3} of {4} ({5:0}%) are falls of 100 m or more (Rider.DeathFall), the fall {6} m", across, rides, R(gTime), popsDead, pops, 100f * popsDead / Mathf.Max(1, pops), R(gFell));
        }
        secondH.Sort();
        Console.WriteLine("second lines: {0} of {1} cycles with a first line ({2:0}%); mountains with at least one: {3} of {4} ({5:0.0}%); most lines tried for one: {6:0}", seconds, secondCycles, 100f * seconds / Mathf.Max(1, secondCycles), mountainsNetworked, hi - lo + 1, 100f * mountainsNetworked / (hi - lo + 1), maxSecondTries);
        if (seconds > 0)
        {
            Console.WriteLine("second lines: {0:0} to {1:0} deg off their cycle's first line; {2} ({3:0}%) are carried over another line, {4} crossings in all; least room between the upper line's chairs and the lower line's cable {5:0.00} m (wanted {6:0.0})", minSecondAngle, maxSecondAngle, secondsOver, 100f * secondsOver / seconds, crossings, minOver, LiftLine.OverTop + LiftLine.OverClear);
            Console.WriteLine("second lines: {0} towers, {1:0.0} to {2:0.0} m tall, median {3:0.0}; {4} ({5:0}%) have a launch ramp", secondTowers, secondH[0], maxSecondH, secondH[secondH.Count / 2], secondRamps, 100f * secondRamps / seconds);
        }
    }

    /// <summary>A cycle's second line: across the piste, above the danger stretch, and standing clear of every other line.</summary>
    // ---------------------------------------------------------------- no two lifts side by side (LiftNetwork.Crowds)
    static int crowdPairs, crowdStations;

    static void CheckCrowds(int seed, LiftLine.Lift[] all)
    {
        for (int i = 0; i < all.Length; i++)
            for (int j = i + 1; j < all.Length; j++)
            {
                var a = all[i]; var b = all[j];
                for (int e = 0; e < 2; e++)
                    for (int f = 0; f < 2; f++)
                    {
                        Vector3 p = e == 0 ? a.top : a.bottom, q = f == 0 ? b.top : b.bottom;
                        float d = new Vector2(p.x - q.x, p.z - q.z).magnitude;
                        if (d < LiftLine.StationApart) { crowdStations++; Bad(seed, a, $"a station {d:0} m from lift {b.k}{(b.second ? "b" : "")}'s"); }
                    }
                float cos = Mathf.Abs(a.dir.x * b.dir.x + a.dir.z * b.dir.z);
                if (cos < Mathf.Cos(LiftLine.CrowdAngle * Mathf.Deg2Rad)) continue;
                float run = Mathf.Max(LiftLine.Alongside(a, b), LiftLine.Alongside(b, a));
                if (run >= LiftLine.CrowdRun) { crowdPairs++; Bad(seed, a, $"side by side with lift {b.k}{(b.second ? "b" : "")} for {run:0} m"); }
            }
    }

    // ---------------------------------------------------------------- the lift over the gorge (LiftGorge.cs)
    static int gorges, mountainsGorged, gorgeable, gorgeRamps, rides, across, pops, popsDead; static float gorgeTall;
    static readonly List<int> noGorge = new List<int>();
    static readonly List<float> gSpan = new List<float>(), gDrop = new List<float>(), gSag = new List<float>(), gFell = new List<float>(), gTime = new List<float>(), gTop = new List<float>();

    /// <summary>
    /// A gorge line: its span over the canyon and the towers either end of it; grinding it across from the tower
    /// hop at the rim (no nudge outward there) at speeds from 8 to 40 m/s, as CableGrind slides it; and popping off
    /// it at points along it, flown down to standable ground as Rider.AirStep flies it (sliding down any face too
    /// steep to stand on), which must be a fall of Rider.DeathFall or more where the canyon is deep.
    /// </summary>
    static void CheckGorge(int seed, LiftLine.Lift l, bool verbose)
    {
        gorges++;
        int i = l.gorge;
        float len = l.nodeS[i + 1] - l.nodeS[i];
        gSpan.Add(len); gDrop.Add(l.gorgeDrop); gSag.Add(LiftLine.Sag(len));
        if (l.gorgeDrop < LiftLine.GorgeDrop - 0.5f) Bad(seed, l, "its gorge span is only " + l.gorgeDrop + " m over the ground");
        float top = 0f; foreach (float h in l.h) top = Mathf.Max(top, h); gTop.Add(top); gorgeTall = Mathf.Max(gorgeTall, top);
        if (l.ramp != null && l.ramp.span < i) gorgeRamps++;
        if (i < 1) Bad(seed, l, "its gorge span starts at the top station: no way onto it");
        // across: caught just past the rim tower (as the hop from the span before puts the rider on it)
        foreach (float v0 in new[] { 8f, 14f, 20f, 26f, 32f, 40f })
        {
            rides++;
            if (HopOnto(l, i - 1, v0, out float along, out float speed)) { }
            else { along = l.nodeS[i] + CableTune.ClearTower + 1f; speed = v0; }
            float t = 0f; bool made = false;
            for (int n = 0; n < 4000; n++)
            {
                float slope = LiftLine.CableSlope(l, i, along);
                float dirY = slope / Mathf.Sqrt(1f + slope * slope);
                speed = Mathf.Max(CableTune.MinSpeed, speed + LiftLine.SlideAccel(speed, dirY) * 0.02f);
                along += speed * 0.02f / Mathf.Sqrt(1f + slope * slope); t += 0.02f;
                if (along >= l.nodeS[i + 1] - speed * CableTune.HopLead / Mathf.Sqrt(1f + slope * slope)) { made = true; break; }
            }
            if (made) { across++; gTime.Add(t); }
            else Bad(seed, l, $"a grind onto its gorge span at {v0:0} m/s never gets across");
        }
        // off it: popped off at points along the gorge span, at the speed a grind has there
        for (float u = 0.1f; u < 0.95f; u += 0.1f)
        {
            pops++;
            float s = Mathf.Lerp(l.nodeS[i], l.nodeS[i + 1], u);
            float slope = LiftLine.CableSlope(l, i, s), k = 1f / Mathf.Sqrt(1f + slope * slope);
            float sp = 30f;
            Vector3 p = l.At(s, LiftLine.Half, l.CableY(i, s) + CableTune.BoardR), from = p;
            Vector3 v = (l.dir * k + Vector3.up * slope * k) * sp + Vector3.up * CableTune.PopHop;
            for (int n = 0; n < 3000; n++)
            {
                v.y -= CableTune.G * 0.02f;
                v -= v * Mathf.Min(1f, 0.0025f * v.magnitude * 0.02f);
                Vector3 q = p + v * 0.02f;
                float g = Slope.Height(q.x, q.z) + CableTune.BoardR;
                if (q.y <= g)
                {
                    Vector3 nrm = Slope.Normal(q.x, q.z);
                    if (nrm.y >= 0.62f) { p = q; break; }
                    // a face too steep to stand on: down it, as AirStep slides the board down one
                    float into = Vector3.Dot(v, nrm); if (into < 0f) v -= nrm * into;
                    q.y = g;
                }
                p = q;
            }
            float fell = from.y - p.y - Slope.Fall * Mathf.Max(0f, p.z - from.z);
            gFell.Add(fell);
            if (fell >= 100f) popsDead++;
        }
        if (verbose) Console.WriteLine($"   gorge span {i}: {len:0} m, sag {LiftLine.Sag(len):0.0} m, chairs up to {l.gorgeDrop:0} m over the ground; towers {(i > 0 ? l.h[i - 1] : 0):0.0} and {(i < l.h.Length ? l.h[i] : 0):0.0} m");
    }

    static bool OverDeep(LiftLine.Lift l, float sa, float sb)
    {
        for (float s = sa; s <= sb; s += 4f) if (Ridge.Depth(l.At(s, 0f, 0f).z) > 0.3f) return true;
        return false;
    }

    /// <summary>The hop off span i at its tower (straight on, at a gorge's rim) onto span i + 1: where it is caught, at what speed.</summary>
    static bool HopOnto(LiftLine.Lift l, int i, float v0, out float along, out float speed)
    {
        const float dt = 0.02f;
        along = l.nodeS[i + 1] - 1f; speed = v0;
        float slope = LiftLine.CableSlope(l, i, along), k0 = 1f / Mathf.Sqrt(1f + slope * slope);
        float vp = v0 * k0, vy = v0 * slope * k0 + CableTune.TowerHop;
        float y = l.CableY(i, along) + CableTune.BoardR;
        for (int n = 0; n < 600; n++)
        {
            vy -= CableTune.G * dt;
            float k = 1f - Mathf.Min(1f, 0.0025f * Mathf.Sqrt(vp * vp + vy * vy) * dt);
            vp *= k; vy *= k;
            along += vp * dt; y += vy * dt;
            int at = LiftLine.SpanAt(l, along);
            if (at == i + 1 && along > l.nodeS[i + 1] && LiftLine.InCatch(l, at, along, y - CableTune.BoardR, vp, vy))
            {
                float s2 = LiftLine.CableSlope(l, at, along);
                speed = (vp + vy * s2) / Mathf.Sqrt(1f + s2 * s2);
                return true;
            }
            if (along > l.nodeS[i + 2]) return false;
        }
        return false;
    }

    static void CheckSecond(int seed, LiftLine.Lift l, LiftLine.Lift[] all, bool verbose)
    {
        seconds++;
        maxSecondTries = Mathf.Max(maxSecondTries, l.tries);
        if (l.under.Length > 0) secondsOver++;
        if (l.ramp != null) secondRamps++;
        if (l.offTop * l.offBottom >= 0f) Bad(seed, l, "a second line that does not cross the piste");
        if (Course.At(l.bottom.z) == Course.Section.Ridge) Bad(seed, l, "a second line ending in the ridge stretch");
        // over the piste only above the danger stretch (never over its crevasses)
        for (float s = 0f; s <= l.plan; s += 1.5f)
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = l.At(s, side * LiftLine.Half, 0f);
                if (Course.At(p.z) == Course.Section.Danger && Mathf.Abs(p.x - Slope.Centre(p.z)) < Slope.PisteHalf + 4f) { Bad(seed, l, "over the piste in the danger stretch at z=" + p.z); s = 1e9f; break; }
            }
        foreach (var o in all)
            if (o.k == l.k && !o.second)
            {
                float a = Mathf.Abs(Mathf.Asin(Mathf.Clamp(l.dir.x * o.dir.z - l.dir.z * o.dir.x, -1f, 1f))) * Mathf.Rad2Deg;
                minSecondAngle = Mathf.Min(minSecondAngle, a); maxSecondAngle = Mathf.Max(maxSecondAngle, a);
            }
        for (int i = 0; i < l.s.Length; i++)
        {
            secondTowers++; secondH.Add(l.h[i]); maxSecondH = Mathf.Max(maxSecondH, l.h[i]);
            if (l.h[i] > (l.under.Length > 0 ? LiftLine.HMaxOver : LiftLine.HMax) + 0.01f) Bad(seed, l, "tower " + i + " is " + l.h[i] + " m tall");
            // none of its towers in another line's swath (under its chairs), nor in the lane to its ramp
            for (float up = 0f; up <= 1f; up += 0.25f)
            {
                Vector3 p = Vector3.Lerp(l.foot[i], l.head[i], up);
                foreach (var o in all)
                {
                    if (o == l) continue;
                    if (LiftLine.InSwath(o, p.x, p.z, LiftLine.Swath)) Bad(seed, l, "tower " + i + " stands in lift " + o.k + "'s swath");
                    if (o.ramp != null && Dist(p, o.ramp.laneFrom, LiftLine.RampPoint(o, o.ramp, 0f)) < CableTune.RampLane) Bad(seed, l, "tower " + i + " stands in the lane to lift " + o.k + "'s ramp");
                }
            }
        }
        foreach (var o in all)
        {
            if (o == l) continue;
            foreach (var st in new[] { l.top, l.bottom })
                if (LiftLine.InSwath(o, st.x, st.z, LiftLine.Swath + LiftLine.StationHalfWid)) Bad(seed, l, "its station at z=" + st.z + " is in lift " + o.k + "'s swath");
            foreach (var st in new[] { o.top, o.bottom })
                if (LiftLine.InSwath(l, st.x, st.z, LiftLine.Swath + LiftLine.StationHalfWid)) Bad(seed, l, "it passes over lift " + o.k + "'s station at z=" + st.z);
            // the lane to its ramp runs into none of the other's towers
            if (l.ramp != null)
                foreach (var f in o.foot)
                    if (Dist(f, l.ramp.laneFrom, LiftLine.RampPoint(l, l.ramp, 0f)) < CableTune.RampLane) Bad(seed, l, "the lane to its ramp runs into lift " + o.k + "'s tower");
        }
        if (verbose && l.under.Length > 0) Console.WriteLine("   carried over lift {0}", l.under[0].k);
    }

    /// <summary>How far p is, in plan, from the line a to b.</summary>
    static float Dist(Vector3 p, Vector3 a, Vector3 b)
    {
        float dx = b.x - a.x, dz = b.z - a.z;
        float t = Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.z) * dz) / (dx * dx + dz * dz));
        float ex = p.x - (a.x + dx * t), ez = p.z - (a.z + dz * t);
        return Mathf.Sqrt(ex * ex + ez * ez);
    }

    /// <summary>
    /// Wherever one line's chairs are over or under another's (either cable, sampled every 1.5 m): the upper
    /// one's lowest point clears the lower one's cable by what a tower's head needs and the margin on top.
    /// </summary>
    static void CheckPairs(int seed, LiftLine.Lift[] all, bool verbose)
    {
        for (int ia = 0; ia < all.Length; ia++)
            for (int ib = 0; ib < all.Length; ib++)
            {
                var a = all[ia]; var b = all[ib];
                if (ia == ib || !a.second) continue;          // (each crossing once, from its second line: two first lines never meet)
                if (b.ZMax < a.ZMin - 30f || b.ZMin > a.ZMax + 30f) continue;
                bool meet = false; float least = 1e9f;
                for (float s = -LiftLine.WheelBack; s <= a.plan + LiftLine.WheelBack; s += 1.5f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 p = a.At(s, side * LiftLine.Half, 0f);
                        if (!LiftLine.InSwath(b, p.x, p.z, LiftLine.Half + 2.2f)) continue;
                        float sa = Mathf.Clamp(s, a.nodeS[0], a.nodeS[a.Spans]);
                        float ya = a.CableY(LiftLine.SpanAt(a, sa), sa);
                        float tb = Mathf.Clamp((p.x - b.top.x) * b.dir.x + (p.z - b.top.z) * b.dir.z, b.nodeS[0], b.nodeS[b.Spans]);
                        float yb = b.CableY(LiftLine.SpanAt(b, tb), tb);
                        meet = true;
                        float room = Mathf.Max(ya - LiftLine.ChairDrop - yb, yb - LiftLine.ChairDrop - ya);   // whichever is the upper one
                        least = Mathf.Min(least, room);
                        if (yb > ya && !b.second) Bad(seed, a, "it passes under lift " + b.k + " at " + s + " m");
                    }
                if (!meet) continue;
                crossings++;
                minOver = Mathf.Min(minOver, least);
                if (least < LiftLine.OverTop + LiftLine.OverClear - 0.05f) Bad(seed, a, "its chairs clear lift " + b.k + (b.second ? "b" : "") + "'s cable by only " + least + " m");
                if (System.Array.IndexOf(a.under, b) < 0 && System.Array.IndexOf(b.under, a) < 0) Bad(seed, a, "it crosses lift " + b.k + " without knowing it");
                if (verbose) Console.WriteLine("lift {0}b crosses lift {1}{2}: its chairs clear the cable below by {3:0.0} m at least", a.k, b.k, b.second ? "b" : "", least);
            }
    }

    static void CheckRamp(int seed, LiftLine.Lift l, bool verbose)
    {
        var r = l.ramp;
        if (r == null) return;
        ramps++;
        if (r.needSpeed <= CableTune.RampWantSpeed) rampsAtWant++;
        needHist[Mathf.Clamp((int)((r.needSpeed - 18f) / 2f + 0.5f), 0, needHist.Length - 1)]++;
        maxLip = Mathf.Max(maxLip, r.height);
        if (r.span < 0 || r.span >= l.Spans - 1) Bad(seed, l, "ramp under a span that cannot be boarded");
        if (r.s0 < l.nodeS[r.span] + 10f || r.s0 + r.length > l.nodeS[r.span + 1] - 20f) Bad(seed, l, "ramp too near a tower");
        // its ground: every corner off the piste and off kept ground, its top never under the snow
        for (int k = 0; k <= 4; k++)
            for (int e = -1; e <= 1; e += 2)
            {
                Vector3 top = LiftLine.RampPoint(l, r, r.length * k / 4f);
                Vector3 p = top + l.across * (e * CableTune.RampHalfWidth);
                float off = Mathf.Abs(p.x - Slope.Centre(p.z));
                minRampOff = Mathf.Min(minRampOff, off);
                if (off < Slope.PisteHalf + CableTune.RampOffPiste - 0.01f) Bad(seed, l, "ramp " + off + " m from the piste's centre");
                if (Course.Keep(p.x, p.z, 1f) || Ridge.InCanyons(p.z) || Deadly.Below(p.x, p.z)) Bad(seed, l, "ramp on kept ground");
                if (Course.At(p.z) == Course.Section.Ridge) Bad(seed, l, "ramp in a ridge section");
                if (top.y < Slope.Height(p.x, p.z) - 0.75f) Bad(seed, l, "the snow stands " + (Slope.Height(p.x, p.z) - top.y) + " m through the ramp");
                if (!LiftLine.Taken(p.x, p.z, 0f)) Bad(seed, l, "ramp outside the cleared swath");
            }
        Vector3 lip = LiftLine.RampPoint(l, r, r.length);
        float room = l.CableY(r.span, r.s0 + r.length) - LiftLine.ChairDrop - lip.y;
        minHeadroom = Mathf.Min(minHeadroom, room);
        if (room < CableTune.RampHeadroom - 0.01f) Bad(seed, l, "only " + room + " m between the ramp's lip and the chairs");
        // the lane down to it is clear, start to finish, and starts at the piste's edge
        Vector3 foot = LiftLine.RampPoint(l, r, 0f);
        for (int k = 0; k <= 10; k++)
        {
            Vector3 q = Vector3.Lerp(r.laneFrom, foot, k / 10f);
            if (!LiftLine.Taken(q.x, q.z, 0f)) Bad(seed, l, "the lane to the ramp is not cleared");
        }
        if (Mathf.Abs(Mathf.Abs(r.laneFrom.x - Slope.Centre(r.laneFrom.z)) - Slope.PisteHalf) > 0.01f) Bad(seed, l, "the lane does not start at the piste's edge");
        // what it claims to reach, it reaches; and going faster still does
        if (!LiftLine.Reaches(l, r, r.needSpeed, out int cs, out float ca)) Bad(seed, l, "ramp does not reach the cable at its own speed");
        else if (!LiftLine.Boardable(l, cs, ca)) Bad(seed, l, "ramp's flight meets the cable where it cannot be boarded");
        for (float v = r.needSpeed + 2f; v <= 40f; v += 2f)
            if (!LiftLine.Reaches(l, r, v, out _, out _)) { Bad(seed, l, "ramp reaches the cable at " + r.needSpeed + " m/s but not at " + v); break; }
        if (r.needSpeed > 18.5f && LiftLine.Reaches(l, r, r.needSpeed - 2f, out _, out _)) Bad(seed, l, "ramp's speed is not the least that reaches");
        Grind(seed, l, cs, ca, LiftLine.RampLipSpeed(l, r, r.needSpeed));
    }

    /// <summary>
    /// The grind itself, stepped as CableGrind.Step steps it from where a ramp's flight meets the cable: how fast
    /// it gets, how long it lasts, whether the hop at the tower clears the tower's head, and whether the next span
    /// can be landed on from that hop (and how far the rider has to steer back to do it).
    /// </summary>
    static void Grind(int seed, LiftLine.Lift l, int span, float along, float speed)
    {
        const float dt = 0.02f;
        grinds++;
        float t = 0f, slope = 0f;
        for (int i = 0; i < 2000; i++)
        {
            slope = LiftLine.CableSlope(l, span, along);
            float dirY = slope / Mathf.Sqrt(1f + slope * slope);
            speed = Mathf.Max(CableTune.MinSpeed, speed + LiftLine.SlideAccel(speed, dirY) * dt);
            along += speed * dt / Mathf.Sqrt(1f + slope * slope);
            t += dt;
            maxGrindSpeed = Mathf.Max(maxGrindSpeed, speed);
            if (along >= l.nodeS[span + 1] - speed * CableTune.HopLead / Mathf.Sqrt(1f + slope * slope)) break;
        }
        sumGrindTime += t;
        if (along > l.nodeS[span + 1]) Bad(seed, l, "the grind ran past its tower");
        // the hop: Rider.AirStep's flight from there
        float k0 = 1f / Mathf.Sqrt(1f + slope * slope);
        float vp = speed * k0, vy = speed * slope * k0 + CableTune.TowerHop;
        float y = l.CableY(span, along) + CableTune.BoardR, air = 0f;
        bool passed = false, landed = false;
        for (int i = 0; i < 300 && !landed; i++)
        {
            vy -= CableTune.G * dt;
            float k = 1f - Mathf.Min(1f, 0.0025f * Mathf.Sqrt(vp * vp + vy * vy) * dt);
            vp *= k; vy *= k;
            float before = along;
            along += vp * dt; y += vy * dt; air += dt;
            if (!passed && along >= l.nodeS[span + 1])
            {
                passed = true;
                // over the tower: the crossarm's top is 1.16 m over the cable there (Tools/MakeLift.py)
                minHopClear = Mathf.Min(minHopClear, y - CableTune.BoardR - l.nodeY[span + 1]);
            }
            int at = LiftLine.SpanAt(l, along);
            if (along > l.nodeS[l.Spans]) break;
            if (passed && at == span + 1 && LiftLine.InCatch(l, at, along, y - CableTune.BoardR, vp, vy) && y - CableTune.BoardR <= l.CableY(at, along) + 0.3f)
            {
                landed = true; relands++;
                sumSteer += CableTune.TowerNudge * air;
            }
            float ground = Slope.Height(l.At(along, LiftLine.Half, 0f).x, l.At(along, LiftLine.Half, 0f).z);
            if (y - CableTune.BoardR < ground) break;
        }
        if (!passed) Bad(seed, l, "the hop fell short of the tower");
    }

    static void Bad(int seed, LiftLine.Lift l, string what)
    {
        problems++;
        if (problems <= 40) Console.WriteLine("PROBLEM seed {0} lift {1}{2}: {3}", seed, l.k, l.second ? "b" : "", what);
    }

    static void Check(int seed, LiftLine.Lift l, bool verbose)
    {
        lifts++; towers += l.s.Length;
        plans.Add(l.plan); minPlan = Mathf.Min(minPlan, l.plan); maxPlan = Mathf.Max(maxPlan, l.plan);
        if (!l.second) maxTries = Mathf.Max(maxTries, l.tries);
        if (!l.second) maxAngle = Mathf.Max(maxAngle, Mathf.Abs(Mathf.Atan2(l.dir.x, l.dir.z)) * Mathf.Rad2Deg);
        if (verbose)
            Console.WriteLine("lift {0}{13}: top station z={1:0} ({2:+0;-0} m off the piste), bottom z={3:0} ({4:+0;-0} m); {5:0} m in plan, {6:0} m of rise; {7} towers leaning {8:0.0} deg; {9} chairs {10:0.0} m apart on a {11:0} m loop; line {12} tried",
                l.k, l.top.z, l.offTop, l.bottom.z, l.offBottom, l.plan, l.top.y - l.bottom.y, l.s.Length, l.lean * Mathf.Rad2Deg, l.chairs, l.spacing, l.loop, l.tries, l.second ? "b (second line)" : "");
        // towers
        for (int i = 0; i < l.s.Length; i++)
        {
            Vector3 f = l.foot[i];
            float off = Mathf.Abs(f.x - Slope.Centre(f.z));
            minTowerOff = Mathf.Min(minTowerOff, off);
            heights.Add(l.h[i]); maxH = Mathf.Max(maxH, l.h[i]); if (l.h[i] > 15f) tallOver15++;
            if (off < LiftLine.TowerOff - 0.01f) Bad(seed, l, "tower " + i + " is " + off + " m from the piste's centre");
            if (Course.Keep(f.x, f.z, 2f)) Bad(seed, l, "tower " + i + " on kept ground");
            // (a gorge line's towers may stand in a ridge's canyons, on the floor where it is no deeper than GorgeFloor)
            bool gorgeFoot = l.gorge >= 0 && Ridge.InCanyons(f.z);
            if (gorgeFoot && Ridge.DepthHere(f.z, Mathf.Sign(f.x - Slope.Centre(f.z))) > LiftLine.GorgeFloor + 0.01f) Bad(seed, l, "tower " + i + " on a canyon floor deeper than " + LiftLine.GorgeFloor + " m");
            if (Ridge.InCanyons(f.z) && !gorgeFoot) Bad(seed, l, "tower " + i + " in a ridge stretch");
            if (Deadly.Below(f.x, f.z) && !gorgeFoot) Bad(seed, l, "tower " + i + " under a deadly cliff");
            if (Cliffs.WallCut(f.x, f.z) < -0.01f) Bad(seed, l, "tower " + i + " on a deadly wall's terrace");
            if (l.h[i] > (l.gorge >= 0 ? LiftLine.HMaxGorge : l.under.Length > 0 ? LiftLine.HMaxOver : LiftLine.HMax) + 0.5f) Bad(seed, l, "tower " + i + " is " + l.h[i] + " m tall");
            if (Mathf.Abs(Slope.Height(f.x, f.z) - f.y) > 0.01f) Bad(seed, l, "tower " + i + " not on the snow");
            if (Slope.Normal(f.x, f.z).y < 0.63f) Bad(seed, l, "tower " + i + " on ground steeper than 51 degrees");
            // the head is where the foot, the lean and the height put it
            Vector3 top = f + l.Axis * l.h[i];
            if ((top - l.head[i]).magnitude > 0.02f) Bad(seed, l, "tower " + i + " head off its mast by " + (top - l.head[i]).magnitude);
            var sec = Course.At(f.z);
            if (sec == Course.Section.Danger) dangerTowers++;
            if (sec == Course.Section.Ridge && !gorgeFoot) Bad(seed, l, "tower " + i + " in a ridge section");
            if (verbose) Console.WriteLine("   tower {0}: foot x={1:0.0} z={2:0.0}, {3:0.0} m from the piste's centre, {4:0.0} m tall, {5}", i + 1, f.x, f.z, off, l.h[i], sec);
        }
        // stations
        foreach (var st in new[] { l.top, l.bottom })
        {
            if (Mathf.Abs(st.x - Slope.Centre(st.z)) < LiftLine.StationOff - 0.01f) Bad(seed, l, "station at z=" + st.z + " too near the piste");
            if (Ridge.InCanyons(st.z) || Course.At(st.z) == Course.Section.Ridge) Bad(seed, l, "station at z=" + st.z + " in a ridge stretch");
            for (int c = 0; c < 4; c++)
            {
                Vector3 q = st + l.dir * ((c & 1) == 0 ? -1 : 1) * LiftLine.StationHalfLen + l.across * ((c & 2) == 0 ? -1 : 1) * LiftLine.StationHalfWid;
                if (Slope.Height(q.x, q.z) > st.y) Bad(seed, l, "station at z=" + st.z + ": snow above its deck");
                if (Mathf.Abs(q.x - Slope.Centre(q.z)) < Slope.PisteHalf + 6f) Bad(seed, l, "station at z=" + st.z + ": a corner within 6 m of the piste");
            }
        }
        // spans: clearance along both cables
        bool over = false;
        for (int i = 0; i < l.Spans; i++)
        {
            float sa = l.nodeS[i], sb = l.nodeS[i + 1], len = sb - sa;
            if (i > 0 && i < l.Spans - 1 && i != l.gorge) { spans++; spanList.Add(len); maxSpan = Mathf.Max(maxSpan, len); minSpan = Mathf.Min(minSpan, len); if (len > 95f) longSpans++; }
            if (len > 140.6f && !(l.gorge >= 0 && OverDeep(l, sa, sb))) Bad(seed, l, "span " + i + " is " + len + " m long and not over a gorge");
            if (len > LiftLine.GorgeSpanMax) Bad(seed, l, "span " + i + " is " + len + " m long");
            bool overThis = false; float least = 1e9f;
            int m = Mathf.CeilToInt(len / 1.5f);
            for (int q = 0; q <= m; q++)
            {
                float s = sa + len * q / m;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = l.At(s, side * LiftLine.Half, l.CableY(i, s));
                    float clear = p.y - LiftLine.ChairDrop - Slope.Height(p.x, p.z);
                    float need = LiftLine.WantClear(p.x, p.z);
                    if (i == 0) need = Mathf.Min(need, 0.4f + 0.2f * (s - sa));
                    if (i == l.Spans - 1) need = Mathf.Min(need, 0.4f + 0.2f * (sb - s));
                    bool onPiste = Mathf.Abs(p.x - Slope.Centre(p.z)) < Slope.PisteHalf + 4f;
                    bool byStation = (i == 0 && s - sa < 25f) || (i == l.Spans - 1 && sb - s < 25f);
                    if (clear < need - 0.03f) Bad(seed, l, "span " + i + " at " + s + " m: clear by " + clear + ", wants " + need);
                    if (clear < 0.3f) Bad(seed, l, "span " + i + " at " + s + " m: a chair in the snow");
                    if (onPiste) { minPiste = Mathf.Min(minPiste, clear); overThis = true; }
                    else if (!byStation) minOff = Mathf.Min(minOff, clear);
                    least = Mathf.Min(least, clear);
                    // (a gorge line is over the canyon on purpose; never over a deadly wall's terrace)
                    if (Ridge.InCanyons(p.z) && l.gorge < 0) Bad(seed, l, "the line is over a ridge stretch at z=" + p.z);
                    if (Deadly.Below(p.x, p.z) && !(l.gorge >= 0 && Ridge.InCanyons(p.z))) Bad(seed, l, "the line is over a deadly cliff's foot at z=" + p.z);
                    if (!LiftLine.Taken(p.x, p.z, 0f)) Bad(seed, l, "the swath does not cover the cable at z=" + p.z);
                }
            }
            if (overThis) { over = true; maxCross = Mathf.Max(maxCross, len); }
            if (verbose) Console.WriteLine("   span {0}: {1:0} m, sag {2:0.0} m, least clearance {3:0.0} m{4}", i, len, LiftLine.Sag(len), least, overThis ? "  << over the piste, about z=" + (l.top.z + l.dir.z * (sa + sb) * 0.5f).ToString("0") + " (" + Course.At(l.top.z + l.dir.z * (sa + sb) * 0.5f) + ")" : "");
        }
        if (over) crossing++;
        // the chairs' loop: continuous, closed, and every chair the same distance from the next
        Vector3 prev = Vector3.zero; float jump = 0f;
        for (float d = 0f; d < l.loop; d += 0.5f)
        {
            l.Loop(d, out Vector3 pos, out Vector3 travel, out bool up, out float since);
            if (d > 0f) jump = Mathf.Max(jump, (pos - prev).magnitude);
            prev = pos;
        }
        l.Loop(0f, out Vector3 p0, out _, out _, out _);
        jump = Mathf.Max(jump, (prev - p0).magnitude);
        if (jump > 0.75f) Bad(seed, l, "the chairs' loop jumps " + jump + " m");
        if (Mathf.Abs(l.chairs * l.spacing - l.loop) > 0.01f) Bad(seed, l, "the chairs do not fill the loop evenly");
        if (l.spacing < 15f || l.spacing > 21f) Bad(seed, l, "chairs " + l.spacing + " m apart");
        // a chair going up crosses each tower where LoopAtTowerUp says
        for (int i = 0; i < l.s.Length; i++)
        {
            l.Loop(l.LoopAtTowerUp(i), out Vector3 pos, out _, out bool up, out float since);
            Vector3 want = l.head[i] + l.across * LiftLine.Half;
            if (!up || (pos - want).magnitude > 0.05f || since > 0.05f) Bad(seed, l, "LoopAtTowerUp(" + i + ") is not at the tower");
        }
    }
}
