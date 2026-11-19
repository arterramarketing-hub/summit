// Runs the game's own lift layout (Assets/Summit/Scripts/LiftLine.cs) on the game's own mountain (Slope, Course,
// Ridge, Cliffs, Deadly), outside Unity, and checks every lift it lays. UnityStub.cs stands in for the few
// UnityEngine maths types those files use. With mono installed (apt-get install mono-mcs):
//
//   S=Assets/Summit/Scripts
//   mcs -langversion:latest -out:/tmp/lift.exe Tools/LiftHarness/*.cs $S/Slope.cs $S/Course.cs $S/Ridge.cs $S/Cliffs.cs $S/Deadly.cs $S/LiftLine.cs
//   mono /tmp/lift.exe 1 3000          seeds 1..3000: a summary, and every problem found
//   mono /tmp/lift.exe 4242            one mountain: every lift, tower and span listed
//
// What it confirms: every tower and station off the piste, off kept ground (Course.Keep), out of the ridge
// stretches and their canyons, not under a deadly cliff; the lowest thing on every chair clear of the snow by
// the margins LiftLine asks for, along both cables every 1.5 m; the chairs' loop continuous and closed.
using System;
using System.Collections.Generic;
using UnityEngine;
using Summit;

public static class LiftHarness
{
    static int lifts, towers, problems, crossing, dangerTowers, missing, tallOver15, longSpans, spans;
    static float minOff = 1e9f, minPiste = 1e9f, maxH, maxSpan, minSpan = 1e9f, minTowerOff = 1e9f, maxTries, maxAngle, minPlan = 1e9f, maxPlan, maxCross;
    static readonly List<float> heights = new List<float>(), spanList = new List<float>(), plans = new List<float>();

    public static void Main(string[] args)
    {
        int lo = int.Parse(args[0]), hi = args.Length > 1 ? int.Parse(args[1]) : lo;
        bool verbose = lo == hi;
        for (int seed = lo; seed <= hi; seed++)
        {
            Slope.Init(seed);
            var all = LiftLine.Lifts;
            int want = Course.CycleOf(Slope.FinishZ) + 1;
            missing += want - all.Length;
            foreach (var l in all) Check(seed, l, verbose);
        }
        heights.Sort(); spanList.Sort(); plans.Sort();
        Console.WriteLine("{0} lifts on {1} mountains ({2} not built); {3} towers", lifts, hi - lo + 1, missing, towers);
        Console.WriteLine("PROBLEMS: {0}", problems);
        Console.WriteLine("lift length in plan: {0:0} to {1:0} m, median {2:0}", minPlan, maxPlan, plans[plans.Count / 2]);
        Console.WriteLine("spans between towers: {0:0} to {1:0} m, median {2:0}; over 95 m: {3:0.0}%", minSpan, maxSpan, spanList[spanList.Count / 2], 100f * longSpans / spans);
        Console.WriteLine("tower heights: {0:0.0} to {1:0.0} m, median {2:0.0}, 90% under {3:0.0}; over 15 m: {4:0.0}%", heights[0], maxH, heights[heights.Count / 2], heights[heights.Count * 9 / 10], 100f * tallOver15 / towers);
        Console.WriteLine("nearest a tower's foot comes to the piste's centre line: {0:0.0} m", minTowerOff);
        Console.WriteLine("least clearance under a chair: {0:0.00} m off the piste, {1:0.00} m over it", minOff, minPiste);
        Console.WriteLine("lifts with chairs over the piste: {0} ({1:0}%); longest span over it {2:0} m", crossing, 100f * crossing / lifts, maxCross);
        Console.WriteLine("towers in a danger stretch (the side away from its cliff): {0}", dangerTowers);
        Console.WriteLine("most lines tried for one lift: {0:0}; steepest line off the fall line: {1:0.0} deg", maxTries, maxAngle);
    }

    static void Bad(int seed, LiftLine.Lift l, string what)
    {
        problems++;
        if (problems <= 40) Console.WriteLine("PROBLEM seed {0} lift {1}: {2}", seed, l.k, what);
    }

    static void Check(int seed, LiftLine.Lift l, bool verbose)
    {
        lifts++; towers += l.s.Length;
        plans.Add(l.plan); minPlan = Mathf.Min(minPlan, l.plan); maxPlan = Mathf.Max(maxPlan, l.plan);
        maxTries = Mathf.Max(maxTries, l.tries);
        maxAngle = Mathf.Max(maxAngle, Mathf.Abs(Mathf.Atan2(l.dir.x, l.dir.z)) * Mathf.Rad2Deg);
        if (verbose)
            Console.WriteLine("lift {0}: top station z={1:0} ({2:+0;-0} m off the piste), bottom z={3:0} ({4:+0;-0} m); {5:0} m in plan, {6:0} m of rise; {7} towers leaning {8:0.0} deg; {9} chairs {10:0.0} m apart on a {11:0} m loop; line {12} tried",
                l.k, l.top.z, l.offTop, l.bottom.z, l.offBottom, l.plan, l.top.y - l.bottom.y, l.s.Length, l.lean * Mathf.Rad2Deg, l.chairs, l.spacing, l.loop, l.tries);
        // towers
        for (int i = 0; i < l.s.Length; i++)
        {
            Vector3 f = l.foot[i];
            float off = Mathf.Abs(f.x - Slope.Centre(f.z));
            minTowerOff = Mathf.Min(minTowerOff, off);
            heights.Add(l.h[i]); maxH = Mathf.Max(maxH, l.h[i]); if (l.h[i] > 15f) tallOver15++;
            if (off < LiftLine.TowerOff - 0.01f) Bad(seed, l, "tower " + i + " is " + off + " m from the piste's centre");
            if (Course.Keep(f.x, f.z, 2f)) Bad(seed, l, "tower " + i + " on kept ground");
            if (Ridge.InCanyons(f.z)) Bad(seed, l, "tower " + i + " in a ridge stretch");
            if (Deadly.Below(f.x, f.z)) Bad(seed, l, "tower " + i + " under a deadly cliff");
            if (Mathf.Abs(Slope.Height(f.x, f.z) - f.y) > 0.01f) Bad(seed, l, "tower " + i + " not on the snow");
            if (Slope.Normal(f.x, f.z).y < 0.63f) Bad(seed, l, "tower " + i + " on ground steeper than 51 degrees");
            // the head is where the foot, the lean and the height put it
            Vector3 top = f + l.Axis * l.h[i];
            if ((top - l.head[i]).magnitude > 0.02f) Bad(seed, l, "tower " + i + " head off its mast by " + (top - l.head[i]).magnitude);
            var sec = Course.At(f.z);
            if (sec == Course.Section.Danger) dangerTowers++;
            if (sec == Course.Section.Ridge) Bad(seed, l, "tower " + i + " in a ridge section");
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
            if (i > 0 && i < l.Spans - 1) { spans++; spanList.Add(len); maxSpan = Mathf.Max(maxSpan, len); minSpan = Mathf.Min(minSpan, len); if (len > 95f) longSpans++; }
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
                    if (Ridge.InCanyons(p.z)) Bad(seed, l, "the line is over a ridge stretch at z=" + p.z);
                    if (Deadly.Below(p.x, p.z)) Bad(seed, l, "the line is over a deadly cliff's foot at z=" + p.z);
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
