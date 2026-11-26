using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Where the chairlifts run: a pure function of the day's mountain, like everything else on it.
    ///
    /// One long lift from the finish to the summit would not suit this mountain: it is 4.2 km long and falls
    /// 3,048 m (no chairlift does that), and once a cycle a ridge stretch opens canyons or a gorge up to 240 m
    /// out on either side of the piste for half a kilometre, which no line beside the run could cross. So there
    /// is a lift for every cycle of the course instead, each with its own two stations: its bottom station where
    /// the danger stretch ends, just above the ridge (the last one's is at the finish), and its top station as far
    /// up as the open ground goes: below the foot of the canyons of the ridge above, or beside the start for the
    /// first. That is five lifts, 290 to 530 m long in plan (850 m where the ridge above was never built). Each
    /// cycle then gets a second, shorter line across the piste and over the first, where one will go
    /// (LiftNetwork.cs): this file lays both kinds. A station's deck is ridden like a cabin's roof (Roofs): onto
    /// it from the snow or out of the air, across it beside the chairs, and off its open end, a launch wherever the
    /// mountain falls away under it. And where it can, a first line starts higher still, above the ridge stretch of the cycle above, and is
    /// carried over that ridge's gorge on one long span (LiftGorge.cs); every mountain that has a gorge a line can
    /// cross gets at least one.
    ///
    /// A lift is dead straight in plan, as real ones are, while the piste meanders beside it or under it:
    ///   - Each end is set 38 to 84 m to one side of the piste. Through the danger stretch the line keeps to the
    ///     side away from that stretch's deadly cliff; whether its top end is on the same side is drawn from the
    ///     day's mountain. If it is not, the line crosses over the piste on one long span.
    ///     (That stretch's own hazards are the crevasses, which are on the piste, and the cliff: the line and its
    ///     towers pass on the far side from both. BottomAt = 365 would end every lift above the stretch instead,
    ///     at about half its length.)
    ///   - Towers stand every 70 m or so (40 to 92; up to 140 for a span over the piste or a jump cliff's band),
    ///     only on ground that can hold them: 27 m or more from the piste's centre line (7 m off the groomed
    ///     edge), off the cliffs, never in a ridge stretch or a canyon, not too steep; and by choice on knolls
    ///     and the lips of benches, so the cable is carried over the rises. They lean out from the slope, half
    ///     way to square with the cable.
    ///   - Each tower starts 10 m tall and is raised (to 24 m at most) until the lowest thing on every chair, a
    ///     rider's skis 4 m under the cable, clears the snow by 5 m everywhere and by 9 m over the piste, with
    ///     the cable sagging between towers by its span squared over 3,500.
    /// Lines are tried from the nearest to the piste outward and the first that meets all of that is the lift
    /// (two or three tries as a rule; a mountain's five lifts take a few hundredths of a second to lay, once).
    /// Tools/LiftHarness runs this very file outside Unity, on the mountain's own code, and checks every lift of
    /// thousands of mountains; Tools/LiftCheck.py is the same again in Python, for the preview pictures.
    ///
    /// The chairs are a pure function of time: they sit every Spacing metres round the loop (up the side that is
    /// on the right looking down the line, round the top bullwheel, down the other side, round the bottom one) and the
    /// whole loop moves at Speed, so no chair has any state of its own and none can bunch or pop.
    ///
    /// Taken() is the lift's cleared swath, for the forest (and anything else placed on the mountain) to ask.
    /// </summary>
    public static partial class LiftLine
    {
        public const bool On = true;

        public const float Gauge = 5.2f, Half = Gauge / 2f;      // between the cable going up and the one coming down
        public const float ChairDrop = 4.0f;                      // from the cable to the lowest thing on a chair
        public const float StationCable = 4.6f;                   // the cable's height over a station's deck
        public const float StationHalfLen = 6f, StationHalfWid = 5.5f, WheelBack = 2.4f;   // (Tools/MakeLift.py's station)
        public const float Swath = 10f;                           // half the cleared swath under the line
        public const float TowerOff = Slope.PisteHalf + 7f;       // no tower's foot nearer the piste's centre line
        public const float StationOff = Slope.PisteHalf + 14f;
        public const float HMin = 10f, HMax = 24f;
        public const float ClearOff = 5f, ClearPiste = 9f;
        const float Step = 4f;                                    // tower spots are tried this far apart along the line
        // (the limits sit half a metre off the spots' 4 m grid, so rounding never decides whether a span is allowed)
        const float SpanMin = 39.5f, SpanWant = 70f, SpanEasy = 92f, SpanMax = 140.5f;
        const float EndMin = 22f, EndWant = 45f, EndMax = 80f;
        static readonly float[] Offsets = { 38f, 46f, 54f, 62f, 72f, 84f };
        public const float BottomAt = 505f;                       // a lift's bottom station, metres into its cycle
        const float Knoll = 0.8f;                                 // what a metre of knoll under a tower is worth to the chain
        const float SagK = 1f / 3500f;

        public const float Speed = 5f;                            // m/s: a detachable's line speed
        public const float SpacingWanted = 18f;                   // between chairs (each loop rounds it to fit)

        /// <summary>
        /// How far a span's cable hangs below the straight line between its supports, at its middle: the square of its
        /// length over 3,500 (5.6 m for a 140 m span); a longer one, only ever over a gorge (LiftGorge.cs), is hauled
        /// tighter and sags only as much more again as its extra length (22 m for 350 m: 6%, an aerial lift's).
        /// </summary>
        public static float Sag(float length) => length <= SagBend ? length * length * SagK : SagBend * SagBend * SagK + 2f * SagBend * SagK * (length - SagBend);
        const float SagBend = 141f;

        /// <summary>
        /// Whatever else is built beside the piste can say where it stands (set this once, before the mountain is
        /// first asked for anything): a line whose swath would pass through such a place is not used, and the
        /// next one out, or the one on the other side, is tried instead. The hamlets (Hamlets.cs) are there from
        /// the start, so no lift is laid through a house, its fence or its clearing.
        /// </summary>
        public static System.Func<float, float, bool> Occupied = Hamlets.Taken;

        /// <summary>How far the lowest thing on a chair must clear the snow at (x, z).</summary>
        public static float WantClear(float x, float z)
        {
            float a = Mathf.Abs(x - Slope.Centre(z));
            return Mathf.Lerp(ClearPiste, ClearOff, Mathf.Clamp01((a - (Slope.PisteHalf + 4f)) / 6f));
        }

        /// <summary>One lift, laid out.</summary>
        public class Lift
        {
            public int k;                         // the course cycle it serves
            public int id;                        // which of the day's lifts it is (its place in Lifts)
            public bool second;                   // a cycle's second line: the park lift, or one laid across the piste (LiftNetwork.cs)
            public bool summit;                   // the Summit lift: cycle 0's first line, its top station at the camp (LaySummit)
            public bool park;                     // a cycle's park lift: from the park's run-out to its top (LiftNetwork.LayPark)
            public Lift[] under = NoLifts;        // the lines it is carried over: its chairs clear them by OverClear
            public Vector3 top, bottom;           // the stations: the middle of each deck
            public Vector3 dir, across;           // level unit vectors: down the line, and to its right looking down
            public float plan;                    // between the stations, in plan
            public float lean;                    // how far the towers lean out from upright (radians)
            public float offTop, offBottom;       // how far each end is from the piste's centre (signed)
            public float[] s, h;                  // each tower: where its top is along the line (from the top station), its mast's length
            public Vector3[] foot, head;          // each tower's foot on the snow, and where the cable's centre line crosses it
            public float[] nodeS, nodeY;          // what the cable hangs between: top portal, towers, bottom portal
            public int tries;
            public Hill hill;                     // the hill under its cable, if a spot was found for one (LiftHill.cs)
            public int gorge = -1;                // the span that crosses a ridge's gorge, if it does (LiftGorge.cs)
            public float gorgeDrop;               // and the most its chairs are over the ground there
            // the chairs' loop: its pieces in the order the chairs travel them, from leaving the bottom bullwheel
            public float[] cum;                   // loop distance at the start of each piece (and the whole length last)
            public float loop, spacing;
            public int chairs;
            public int Spans => nodeS.Length - 1;
            public int Pieces => cum.Length - 1;
            public Vector3 Axis => Vector3.up * Mathf.Cos(lean) + dir * Mathf.Sin(lean);   // up a tower's mast
            public float ZMin => Mathf.Min(top.z, bottom.z) - StationHalfLen;
            public float ZMax => Mathf.Max(top.z, bottom.z) + StationHalfLen;

            /// <summary>A point of the line's centre, s metres along it from the top station, at height y.</summary>
            public Vector3 At(float along, float side, float y) => new Vector3(top.x + dir.x * along + across.x * side, y, top.z + dir.z * along + across.z * side);

            /// <summary>The cable's height on span i (between nodes i and i + 1) at s along the line.</summary>
            public float CableY(int i, float along)
            {
                float u = (along - nodeS[i]) / (nodeS[i + 1] - nodeS[i]);
                return Mathf.Lerp(nodeY[i], nodeY[i + 1], u) - 4f * Sag(nodeS[i + 1] - nodeS[i]) * u * (1f - u);
            }

            /// <summary>
            /// Where the loop is at distance d round it (0 leaving the bottom bullwheel on the way up): the point on
            /// the cable, the way the chairs are travelling, whether this is the way up, and the cable node it last
            /// passed with how far back that was (for the swing a chair picks up crossing a tower's sheaves).
            /// </summary>
            public void Loop(float d, out Vector3 pos, out Vector3 travel, out bool up, out float sinceTower)
            {
                d = Mathf.Repeat(d, loop);
                int n = Spans, lo = 0, hi = Pieces - 1;
                while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (cum[mid] <= d) lo = mid; else hi = mid - 1; }
                int piece = lo;
                float len = cum[piece + 1] - cum[piece], u = len > 1e-4f ? (d - cum[piece]) / len : 0f;
                float yT = top.y + StationCable, yB = bottom.y + StationCable;
                float sT = nodeS[0], sB = nodeS[n];
                sinceTower = 1000f;
                up = piece <= n + 1;
                if (piece == 0) { pos = At(Mathf.Lerp(plan + WheelBack, sB, u), Half, yB); travel = -dir; }
                else if (piece <= n)
                {
                    // up the right-hand side: spans from the bottom portal to the top one
                    int i = n - piece;
                    float along = Mathf.Lerp(nodeS[i + 1], nodeS[i], u);
                    pos = At(along, Half, CableY(i, along));
                    travel = new Vector3(-dir.x * (nodeS[i + 1] - nodeS[i]), nodeY[i] - nodeY[i + 1], -dir.z * (nodeS[i + 1] - nodeS[i])).normalized;
                    if (i + 1 < n) sinceTower = u * len;
                }
                else if (piece == n + 1) { pos = At(Mathf.Lerp(sT, -WheelBack, u), Half, yT); travel = -dir; }
                else if (piece == n + 2)
                {
                    float a = u * Mathf.PI;
                    pos = At(-WheelBack - Half * Mathf.Sin(a), Half * Mathf.Cos(a), yT);
                    travel = -dir * Mathf.Cos(a) - across * Mathf.Sin(a);
                }
                else if (piece == n + 3) { pos = At(Mathf.Lerp(-WheelBack, sT, u), -Half, yT); travel = dir; }
                else if (piece <= 2 * n + 3)
                {
                    int i = piece - (n + 4);
                    float along = Mathf.Lerp(nodeS[i], nodeS[i + 1], u);
                    pos = At(along, -Half, CableY(i, along));
                    travel = new Vector3(dir.x * (nodeS[i + 1] - nodeS[i]), nodeY[i + 1] - nodeY[i], dir.z * (nodeS[i + 1] - nodeS[i])).normalized;
                    if (i > 0) sinceTower = u * len;
                }
                else if (piece == 2 * n + 4) { pos = At(Mathf.Lerp(sB, plan + WheelBack, u), -Half, yB); travel = dir; }
                else
                {
                    float a = u * Mathf.PI;
                    pos = At(plan + WheelBack + Half * Mathf.Sin(a), -Half * Mathf.Cos(a), yB);
                    travel = dir * Mathf.Cos(a) + across * Mathf.Sin(a);
                }
            }

            /// <summary>How far round the loop a chair going up is as it crosses tower i (the cable node i + 1).</summary>
            public float LoopAtTowerUp(int i) => cum[Spans - i];

            internal void MakeLoop()
            {
                int n = Spans;
                cum = new float[2 * n + 7];
                float straight = StationHalfLen + WheelBack, arc = Mathf.PI * Half, d = 0f;
                int p = 0;
                cum[p++] = d; d += straight;
                for (int i = n - 1; i >= 0; i--) { cum[p++] = d; d += SpanLength(i); }
                cum[p++] = d; d += straight;
                cum[p++] = d; d += arc;
                cum[p++] = d; d += straight;
                for (int i = 0; i < n; i++) { cum[p++] = d; d += SpanLength(i); }
                cum[p++] = d; d += straight;
                cum[p++] = d; d += arc;
                cum[p] = d;
                loop = d;
                chairs = Mathf.Max(2, Mathf.RoundToInt(loop / SpacingWanted));
                spacing = loop / chairs;
            }

            float SpanLength(int i)
            {
                float ds = nodeS[i + 1] - nodeS[i], dy = nodeY[i + 1] - nodeY[i];
                return Mathf.Sqrt(ds * ds + dy * dy);
            }
        }

        // ---------------------------------------------------------------- today's lifts

        static readonly Lift[] NoLifts = new Lift[0];
        static Lift[] lifts;
        static int laidFor = int.MinValue;

        /// <summary>Every lift on today's mountain, from the summit down (laid out the first time it is asked for).</summary>
        public static Lift[] Lifts
        {
            get
            {
                if (lifts == null || laidFor != Slope.Seed)
                {
                    laidFor = Slope.Seed;
                    LiftTop.Warm();   // (the Summit top's ground laid out here, on the main thread, before anything else asks)
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    lifts = new Lift[0];   // (so nothing that asks about the ground while they are laid sees the old ones)
                    HideHills();           // (and they are laid on the mountain without hills: LiftHill.cs)
                    bool ledges = Cliffs.LedgesHidden;
                    Cliffs.LedgesHidden = true;   // (nor ledge lines: those go where the lifts leave room, Cliffs)
                    var list = new List<Lift>();
                    if (On)
                    {
                        var plains = new Lift[LastCycle + 1];
                        bool any = false;
                        for (int k = 0; k <= LastCycle; k++)
                        {
                            var l = Lay(k, list, out plains[k]);
                            if (l == null) continue;
                            list.Add(l);
                            if (l.gorge >= 0) any = true;
                        }
                        // if no line has gone over a gorge, the first that can is given one: every mountain with a gorge a line will cross gets one. (It is
                        // laid clear of all the others; the rest are as they were, none of them laid against it)
                        if (!any)
                        {
                            gorgeAny = true;
                            for (int k = 1; k <= LastCycle && !any; k++)
                            {
                                var others = new List<Lift>();
                                foreach (var o in list) if (o != plains[k]) others.Add(o);
                                var gl = LayGorge(k, others, plains[k]);
                                if (gl == null || gl.gorge < 0) continue;
                                int at = plains[k] != null ? list.IndexOf(plains[k]) : -1;
                                if (at >= 0) list[at] = gl; else list.Add(gl);
                                any = true;
                            }
                            gorgeAny = false;
                        }
                    }
                    // then each cycle's second line, across the piste and over whatever the first lines put in its way
                    if (On && Network)
                        for (int k = 0; k <= LastCycle; k++)
                        {
                            // the park lift, where one will go; otherwise the line across the run (LiftNetwork.cs)
                            var l = (ParkOn ? LayPark(k, list) : null) ?? LaySecond(k, list);
                            if (l != null) list.Add(l);
                        }
                    for (int i = 0; i < list.Count; i++) list[i].id = i;
                    SettleHills(list);
                    lifts = list.ToArray();
                    PublishHills(lifts);
                    Cliffs.LedgesHidden = ledges;
                    LaidMs = watch.Elapsed.TotalMilliseconds;
                }
                return lifts;
            }
        }

        /// <summary>
        /// True under a lift's line or on one of its stations: its cleared swath, where no tree, rock or building
        /// may stand. The same shape as Course.Keep, so placement can ask both alike.
        /// </summary>
        public static bool Taken(float x, float z, float margin) => TakenBy(Lifts, x, z, margin) || OnHill(x, z, margin);

        static bool TakenBy(IList<Lift> all, float x, float z, float margin)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var l = all[i];
                if (z < l.ZMin - 60f || z > l.ZMax + 60f) continue;
                if (l.hill != null && InLane(l, x, z, margin)) return true;   // the way in to its hill (the hill itself: Taken)
                float along = (x - l.top.x) * l.dir.x + (z - l.top.z) * l.dir.z;
                if (along < -StationHalfLen - 6f - margin || along > l.plan + StationHalfLen + 6f + margin) continue;
                float side = (x - l.top.x) * l.across.x + (z - l.top.z) * l.across.z;
                if (Mathf.Abs(side) < Swath + margin) return true;
            }
            return false;
        }

        static int LastCycle => Course.CycleOf(Slope.FinishZ);

        /// <summary>Where lift k's stations go down the mountain (z of its top and bottom); false if there is no room.</summary>
        public static bool Range(int k, out float zt, out float zb)
        {
            float b = Course.First + k * Course.Cycle;
            zb = k == LastCycle ? Slope.FinishZ + 15f : b + BottomAt;
            if (k == 0) zt = 40f;                                                         // beside the start
            else if (Ridge.Built(k - 1)) zt = b + Ridge.EndOf(k - 1) + Ridge.FadeAfter - Course.Cycle + 30f;   // below the canyons' foot
            else if (CliffBand.Of(k - 1, out var band)) zt = (band.ledge ? band.End : CliffBand.WideEnd(band)) + 40f;   // below a cliff band's gap and the wide run under it
            else if (Bowl.Of(k - 1, out var bowl)) zt = bowl.zE + 40f;                     // below the bowl's funnel
            else zt = b - Course.Cycle + 640f;                                            // no ridge above: on up past it
            return zb - zt >= 180f;
        }

        static Lift Lay(int k, IList<Lift> laid, out Lift plain)
        {
            plain = LayPlain(k);
            // on up over the gorge of the ridge above, if it will go (LiftGorge.cs)
            return LayGorge(k, laid, plain) ?? plain;
        }

        static Lift LayPlain(int k)
        {
            if (!Range(k, out float zt, out float zb)) return null;
            if (k == 0 && SummitOn) { var s = LaySummit(zb); if (s != null) return s; }
            return LayTo(k, zt, zb);
        }

        // ---------------------------------------------------------------- the Summit lift
        // The lift that brings you home: cycle 0's first line, from that cycle's gathering point (the danger stretch's
        // end, where the run funnels into the bowl or the ridge) up the summit face beside the start, to a top station
        // on the camp's left shoulder (the right is the cliff: Slope.Summit) beside the lodge's wing, its deck about level
        // with the camp's plateau and a short walk from the lodge (LiftEnds lays the path, the outrun and the people; the
        // hero arrives at its unload after a run: Camp.Enter). Before, its top was at z 40, below
        // the start's lip and 38 to 84 m to one side: down the face from the camp and out of sight of it.
        public const bool SummitOn = true;
        // where its top station may stand: beside the lodge's wing (camplayout: the lift arrives at the lodge, so the
        // camp's loop runs lift, lodge, fire, gate with no going back), as near the plateau's edge as the station's
        // footprint allows; failing that, where it stood before, in front of the camp's left front corner (the camp
        // spans z -38 to -96, and 33 m either side of the run with its trees)
        static readonly float[][] SummitZ = { new[] { -88f, -86f, -90f, -84f, -92f, -82f, -94f, -80f }, new[] { -62f, -58f, -66f, -54f, -70f, -50f } };
        static readonly float[] SummitOff = { -41f, -44f, -47f, -50f, -54f };

        public static readonly Dictionary<string, int> SummitWhy = new Dictionary<string, int>();

        static Lift LaySummit(float zb)
        {
            SummitWhy.Clear();
            // The top goes on the camp's left (the right is the cliff). Through the danger stretch a line keeps away
            // from the deadly wall (LayTo); with the wall on the left that would carry it diagonally across the run, a
            // span too long for any tower chain, so then it ends above the wall instead, still on the left: at the
            // park's run-out, where the park's lines gather (cycle 0's park lift then goes on the right: LayPark).
            bool wallLeft = LastCycle > 0 && Cliffs.WallOf(0, out var w) && w.side < 0f;
            float b = Course.First;
            float cx = Slope.Centre(Slope.PadLip);
            int tries = 0;
            // (lifttop: on the summit bench behind the lodge, LiftTop; should the bench not take it on some mountain,
            // where it stood before, beside the lodge's wing, then in front of the camp's left corner)
            var places = new List<(float[] zs, float[] offs)>();
            if (LiftTop.Station(out var vz, out var voff)) places.Add((vz, voff));
            foreach (var zs in SummitZ) places.Add((zs, SummitOff));
            // (the nearest to the camp first; then, on a summit face too steep for the usual towers, taller ones, as
            // real summit lifts have)
            // (every height and offset beside the lodge first; only then the old places in front of the camp)
            foreach (var (zs, offList) in places)
            foreach (float hm in new[] { HMax, SummitHMax })
            foreach (float off in offList)
                foreach (float zt in zs)
                    foreach (float zBottom in wallLeft ? new[] { b + Course.DangerAt - 12f } : new[] { zb, b + Course.DangerAt - 12f })
                        foreach (float ob in SummitBottom)
                        {
                            hMaxSummit = hm;
                            tries++;
                            float ot = cx + off - Slope.Centre(zt);   // (from the camp's own centre line: the run wanders)
                            var l = Try(0, zt, zBottom, ot, -ob);
                            hMaxSummit = 0f;
                            if (l == null) { SummitWhy[Why] = SummitWhy.TryGetValue(Why, out int c) ? c + 1 : 1; continue; }
                            // its deck about level with the camp's plateau, a walk from it
                            if (l.top.y < Camp.GroundAt(-20f, zt).y - SummitDrop) { SummitWhy["low"] = SummitWhy.TryGetValue("low", out int c2) ? c2 + 1 : 1; continue; }
                            l.summit = true; l.tries = tries;
                            PlaceHill(l);
                            return l;
                        }
            return null;
        }
        const float SummitDrop = 6f, SummitHMax = 30f;
        static float hMaxSummit;   // (while the Summit lift is tried: its towers' height limit)
        static readonly float[] SummitBottom = { 38f, 46f, 54f, 62f, 72f, 84f, 96f, 110f };
        static Lift LayTo(int k, float zt, float zb)
        {
            // through the danger stretch, the side away from its deadly cliff
            float sb = k < LastCycle && Cliffs.WallOf(k, out var w) ? -w.side : Slope.Hash01(k, 1, 171) < 0.5f ? -1f : 1f;
            bool cross = Slope.Hash01(k, 2, 171) < 0.4f;
            int tries = 0;
            var pairs = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                bool same = cross ? pass == 1 : pass == 0;
                float st = same ? sb : -sb;
                pairs.Clear();
                foreach (float a in Offsets) foreach (float b in Offsets) pairs.Add(new Vector2(a, b));
                // beside the piste: the nearest first. Across it: from 54 m out either side (nearer, the span over
                // the piste would be too long), then further
                if (same) pairs.Sort((p, q) => Compare(Mathf.Max(p.x, p.y), Mathf.Max(q.x, q.y), p.x + p.y, q.x + q.y, p.x, q.x));
                else pairs.Sort((p, q) =>
                {
                    int np = Mathf.Min(p.x, p.y) < 54f ? 1 : 0, nq = Mathf.Min(q.x, q.y) < 54f ? 1 : 0;
                    return np != nq ? np.CompareTo(nq) : Compare(Mathf.Max(p.x, p.y), Mathf.Max(q.x, q.y), p.x + p.y, q.x + q.y, p.x, q.x);
                });
                int n = same ? pairs.Count : 10;
                for (int i = 0; i < n; i++)
                {
                    tries++;
                    var l = Try(k, zt, zb, st * pairs[i].x, sb * pairs[i].y);
                    if (l == null) continue;
                    l.tries = tries; PlaceHill(l); return l;
                }
            }
            return null;
        }

        static int Compare(float a0, float b0, float a1, float b1, float a2, float b2) =>
            a0 != b0 ? a0.CompareTo(b0) : a1 != b1 ? a1.CompareTo(b1) : a2.CompareTo(b2);

        /// <summary>Ground that can hold a tower's foot: off the piste, off the cliffs, out of the canyons, not too steep.</summary>
        static bool GroundOk(float x, float z, float off)
        {
            if (Mathf.Abs(x - Slope.Centre(z)) < off) return false;
            if (Kept(x, z)) return false;
            if (Cliffs.WallCut(x - 4f, z) < -0.01f || Cliffs.WallCut(x + 4f, z) < -0.01f) return false;
            float gx = (Slope.Height(x + 3f, z) - Slope.Height(x - 3f, z)) / 6f;
            float gz = (Slope.Height(x, z + 3f) - Slope.Height(x, z - 3f)) / 6f;
            return Mathf.Abs(gx) <= 0.55f && gz <= 0.3f && gz >= -1.25f;
        }

        // (a lift stays out of a ridge stretch altogether, canyons, gorge and all, not just off its rock)
        // (asked with a small margin as well as a wide one: near two jump cliffs at once, Cliffs.Keep's wide look can
        // find the further one first and miss the nearer)
        static bool Kept(float x, float z) => Ridge.InCanyons(z) || CliffBand.Near(z, 30f, out _) || Course.Keep(x, z, 3f) || Course.Keep(x, z, 0.5f) || Cliffs.WallCut(x, z) < -0.01f || Slope.OnPad(x, z)
            || (z > -100f && z < 80f && LiftTop.Covers(x, z, 2.5f));   // (nor on the Summit top's ways: its far side runs beside the line, and under it)

        static readonly float[] StationAcross = { -StationHalfWid, -Half, 0f, Half, StationHalfWid };

        /// <summary>A station's deck: just over the highest snow under it. False if the ground will not do.</summary>
        static bool StationAt(Lift l, float x, float z, out float deck)
        {
            deck = float.MinValue;
            // (under its edges, its middle, and the two cables: the chairs leave it only just above its deck)
            for (int i = -2; i <= 2; i++)
                for (int s = 0; s < StationAcross.Length; s++)
                {
                    float px = x + l.dir.x * i * StationHalfLen * 0.5f + l.across.x * StationAcross[s];
                    float pz = z + l.dir.z * i * StationHalfLen * 0.5f + l.across.z * StationAcross[s];
                    // (clear of the run: behind the gate there is none, only the camp on the run's line at the lip, so
                    // the line's wander back there means nothing; the Summit lift's top, beside the lodge: camplayout)
                    float runX = pz < Slope.GateZ ? Slope.Centre(Slope.PadLip) : Slope.Centre(pz);
                    if (Mathf.Abs(px - runX) < StationOff - StationHalfWid - 1f) return false;
                    if (Kept(px, pz)) return false;
                    deck = Mathf.Max(deck, Slope.Height(px, pz));
                }
            deck += 0.3f;
            return true;
        }

        /// <summary>Lay lift k along the straight line from ot beside the piste at zt to ob beside it at zb. Null if it will not go.</summary>
        /// <summary>Why the last line tried did not go (for checking).</summary>
        public static string Why = "";

        static Lift Try(int k, float zt, float zb, float ot, float ob, IList<Lift> laid = null, float gorge = 0f)
        {
            // (a gorge line, gorge = the cliff's side, is a first line laid after others: it is kept clear of them too)
            var l = new Lift { k = k, offTop = ot, offBottom = ob, second = laid != null && gorge == 0f };
            float xt = Slope.Centre(zt) + ot, xb = Slope.Centre(zb) + ob;
            float plan = Mathf.Sqrt((xb - xt) * (xb - xt) + (zb - zt) * (zb - zt));
            l.plan = plan;
            l.dir = new Vector3((xb - xt) / plan, 0f, (zb - zt) / plan);
            l.across = new Vector3(l.dir.z, 0f, -l.dir.x);
            l.top = new Vector3(xt, 0f, zt); l.bottom = new Vector3(xb, 0f, zb);
            if (!StationAt(l, xt, zt, out float deckT) || !StationAt(l, xb, zb, out float deckB)) { Why = "station"; return null; }
            l.top.y = deckT; l.bottom.y = deckB;
            if (Occupied != null)
                for (float s = -StationHalfLen - 6f; s <= plan + StationHalfLen + 6f; s += 5f)
                    for (int side = -1; side <= 1; side++)
                        if (Occupied(xt + l.dir.x * s + l.across.x * side * Swath, zt + l.dir.z * s + l.across.z * side * Swath)) { Why = "occupied"; return null; }
            l.lean = 0.5f * Mathf.Atan2(deckT - deckB, plan);
            float sl = Mathf.Sin(l.lean);
            // a second line: clear of the lines already laid (their stations, their hills' lanes), and over them
            // only where it can be (LiftNetwork.cs)
            float hMax = hMaxSummit > 0f ? hMaxSummit : HMax;
            if (gorge != 0f && !GorgeSide(l, gorge)) return null;
            if (laid != null)
            {
                if (!Meets(l, laid)) return null;
                if (l.under.Length > 0) hMax = HMaxOver;
                if (gorge != 0f && l.under.Length > 0) return null;   // (a gorge line is carried over nothing)
            }
            if (gorge != 0f) hMax = HMaxGorge;

            // where a tower may stand: spots every Step along the line, between the stations' portals
            float s0 = StationHalfLen, s1 = plan - StationHalfLen;
            int n = Mathf.FloorToInt((s1 - s0 - 2f * EndMin) / Step) + 1;
            if (n < 1) { Why = "short"; return null; }
            float first = s0 + EndMin + ((s1 - s0 - 2f * EndMin) - (n - 1) * Step) * 0.5f;
            var spot = new float[n]; var ok = new bool[n]; var knoll = new float[n];
            var deep = gorge != 0f ? new int[n + 1] : null;   // (how many spots up to each stand over the deep canyon: OverGorge, quickly)
            for (int i = 0; i < n; i++)
            {
                float s = first + i * Step;
                spot[i] = s;
                // (wherever its height puts its foot: it leans, so its foot is up the line from its top)
                ok[i] = gorge != 0f
                    ? GorgeGround(xt + l.dir.x * s, zt + l.dir.z * s, TowerOff, gorge)
                      && GorgeGround(xt + l.dir.x * (s - HMin * sl), zt + l.dir.z * (s - HMin * sl), TowerOff, gorge)
                      && GorgeGround(xt + l.dir.x * (s - hMax * sl), zt + l.dir.z * (s - hMax * sl), TowerOff, gorge)
                    : GroundOk(xt + l.dir.x * s, zt + l.dir.z * s, TowerOff)
                      && GroundOk(xt + l.dir.x * (s - HMin * sl), zt + l.dir.z * (s - HMin * sl), TowerOff)
                      && GroundOk(xt + l.dir.x * (s - hMax * sl), zt + l.dir.z * (s - hMax * sl), TowerOff);
                // (and no tower of a second line in another line's swath, under its chairs, or in the lane to its hill)
                // (its towers may be taller, so its foot may be further up the line: looked at every few metres of that)
                if (ok[i] && laid != null)
                    for (float back = 0f; back <= hMax + 0.01f && ok[i]; back += hMax * (gorge != 0f ? 0.25f : 0.125f))
                        ok[i] = (gorge != 0f ? GorgeGround(xt + l.dir.x * (s - back * sl), zt + l.dir.z * (s - back * sl), TowerOff, gorge)
                                             : GroundOk(xt + l.dir.x * (s - back * sl), zt + l.dir.z * (s - back * sl), TowerOff))
                             && !TakenBy(laid, xt + l.dir.x * (s - back * sl), zt + l.dir.z * (s - back * sl), 3f);
                knoll[i] = ok[i] ? KnollAt(l, s) : 0f;
                if (deep != null) deep[i + 1] = deep[i] + (Ridge.Depth(zt + l.dir.z * s) > 0.3f ? 1 : 0);
            }

            // the cheapest chain of spans from the top portal to the bottom one
            const float Inf = 1e18f;
            var cost = new float[n]; var prev = new int[n];
            for (int i = 0; i < n; i++)
            {
                cost[i] = Inf; prev[i] = -1;
                if (!ok[i]) continue;
                float g = spot[i] - s0;
                if (g <= EndMax) cost[i] = EndCost(g) - knoll[i];
                for (int j = i - 1; j >= 0; j--)
                {
                    g = spot[i] - spot[j];
                    if (g > (gorge != 0f ? GorgeSpanMax : SpanMax)) break;
                    if (g < SpanMin || cost[j] >= Inf) continue;
                    if (g > SpanMax && (deep == null || deep[i] - deep[j + 1] <= 0)) continue;   // (only the span over the gorge is that long)
                    float c = cost[j] + SpanCost(g) - knoll[i];
                    if (c < cost[i]) { cost[i] = c; prev[i] = j; }
                }
            }
            float best = Inf; int at = -1;
            for (int i = 0; i < n; i++)
            {
                float g = s1 - spot[i];
                if (cost[i] < Inf && g <= EndMax && cost[i] + EndCost(g) < best) { best = cost[i] + EndCost(g); at = i; }
            }
            if (at < 0) { Why = "chain"; return null; }
            var chain = new List<float>();
            for (; at >= 0; at = prev[at]) chain.Add(spot[at]);
            chain.Reverse();
            l.s = chain.ToArray();
            l.h = new float[l.s.Length];
            for (int i = 0; i < l.h.Length; i++) l.h[i] = HMin;
            l.foot = new Vector3[l.s.Length]; l.head = new Vector3[l.s.Length];
            l.nodeS = new float[l.s.Length + 2]; l.nodeY = new float[l.s.Length + 2];

            // heights: every tower starts at its least and is raised until the chairs clear the snow everywhere
            bool done = false;
            for (int pass = 0; pass < 14 && !done; pass++)
            {
                Nodes(l);
                done = true;
                for (int i = 0; i < l.Spans; i++)
                {
                    float d = Deficit(l, i, i == 0 || i == l.Spans - 1 ? 1.5f : 2f, 0.15f);   // (by a station, as finely as the checks look)
                    if (d <= 0.01f) continue;
                    done = false;
                    d += 0.25f;
                    if (i > 0) l.h[i - 1] += d;
                    if (i < l.h.Length) l.h[i] += d;
                }
                for (int i = 0; i < l.h.Length; i++) if (l.h[i] > hMax) { Why = "tall"; return null; }
            }
            if (!done) { Why = "clearance"; return null; }
            Nodes(l);
            // (and where each foot has ended up, the snow must not be too steep to stand a tower on: about 50 degrees)
            for (int i = 0; i < l.foot.Length; i++) if (Slope.Normal(l.foot[i].x, l.foot[i].z).y < 0.64f) { Why = "steepfoot"; return null; }
            // (a gorge line's feet, wherever their heights have put them, on ground that will hold them; and a real drop under it)
            if (gorge != 0f)
            {
                for (int i = 0; i < l.foot.Length; i++) if (!GorgeGround(l.foot[i].x, l.foot[i].z, TowerOff, gorge)) return null;
                if (!FindGorge(l)) return null;
            }
            l.MakeLoop();
            return l;
        }

        static float EndCost(float g)
        {
            if (g < EndMin) return 1e18f;
            float d = (g - EndWant) / 15f;
            return d * d;
        }

        static float SpanCost(float g)
        {
            float d = (g - SpanWant) / 12f;
            return d * d + (g > SpanEasy ? 6f + (g - SpanEasy) * 0.25f : 0f);
        }

        /// <summary>
        /// How far the ground at s stands above the line between the ground 35 m up and down the lift's line: a
        /// tower on a knoll or a bench's lip carries the cable over it; one in a hollow leaves the next rise in
        /// the chairs' way.
        /// </summary>
        static float KnollAt(Lift l, float s) => Knoll * Mathf.Clamp(Ground(l, s) - 0.5f * (Ground(l, s - 35f) + Ground(l, s + 35f)), -3f, 3f);

        static float Ground(Lift l, float along)
        {
            along = Mathf.Clamp(along, 0f, l.plan);
            return Slope.Height(l.top.x + l.dir.x * along, l.top.z + l.dir.z * along);
        }

        /// <summary>Each tower's foot and head for its present height, and the nodes the cable hangs between.</summary>
        static void Nodes(Lift l)
        {
            float sl = Mathf.Sin(l.lean), cl = Mathf.Cos(l.lean);
            int n = l.s.Length;
            l.nodeS[0] = StationHalfLen; l.nodeY[0] = l.top.y + StationCable;
            for (int i = 0; i < n; i++)
            {
                float back = l.h[i] * sl;
                float fx = l.top.x + l.dir.x * (l.s[i] - back), fz = l.top.z + l.dir.z * (l.s[i] - back);
                float fy = Slope.Height(fx, fz);
                l.foot[i] = new Vector3(fx, fy, fz);
                l.head[i] = new Vector3(l.top.x + l.dir.x * l.s[i], fy + l.h[i] * cl, l.top.z + l.dir.z * l.s[i]);
                l.nodeS[i + 1] = l.s[i]; l.nodeY[i + 1] = l.head[i].y;
            }
            l.nodeS[n + 1] = l.plan - StationHalfLen; l.nodeY[n + 1] = l.bottom.y + StationCable;
        }

        /// <summary>How far short of its clearance the lowest chair on span i comes (negative: clear by that much).</summary>
        public static float Deficit(Lift l, int i, float step = 3f, float extra = 0f)
        {
            float sa = l.nodeS[i], sb = l.nodeS[i + 1];
            float worst = -1e9f;
            int m = Mathf.Max(2, Mathf.CeilToInt((sb - sa) / step)), last = l.Spans - 1;
            for (int q = 0; q <= m; q++)
            {
                float s = sa + (sb - sa) * q / m;
                float y = l.CableY(i, s) - ChairDrop;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = l.top.x + l.dir.x * s + l.across.x * side * Half, z = l.top.z + l.dir.z * s + l.across.z * side * Half;
                    float need = WantClear(x, z);
                    // coming out of a station the chairs start at its deck: the clearance builds over the first metres
                    if (i == 0) need = Mathf.Min(need, 0.4f + 0.2f * (s - sa));
                    if (i == last) need = Mathf.Min(need, 0.4f + 0.2f * (sb - s));
                    need += extra;
                    float d = Slope.Height(x, z) + need - y;
                    // over another line: its cable, and whatever stands above its cable, count as the ground
                    if (l.under.Length > 0)
                    {
                        float roof = Roof(l, x, z);
                        if (roof > float.MinValue) d = Mathf.Max(d, roof + OverClear + extra - y);
                    }
                    // (a span from a station can only be lifted at its tower end; right at the portal, not at all)
                    if (i == 0 && q > 0) { if (d > 0f) d /= q / (float)m; }
                    else if (i == last && q < m) { if (d > 0f) d /= 1f - q / (float)m; }
                    else if ((i == 0 && q == 0) || (i == last && q == m)) d = d < 0.3f ? Mathf.Min(d, 0f) : 99f;
                    if (d > worst) worst = d;
                }
            }
            return worst;
        }

        /// <summary>
        /// A line of text for each lift, with its least clearances measured afresh every 1.5 m: for checking a
        /// mountain in the editor (SkiLift logs it when SUMMIT_LIFTCHECK=1).
        /// </summary>
        public static string Report()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in Lifts)
            {
                float worst = -1e9f, tall = 0f, longest = 0f;
                for (int i = 0; i < l.Spans; i++) { worst = Mathf.Max(worst, Deficit(l, i, 1.5f)); longest = Mathf.Max(longest, l.nodeS[i + 1] - l.nodeS[i]); }
                foreach (float h in l.h) tall = Mathf.Max(tall, h);
                float near = float.MaxValue;
                foreach (var f in l.foot) near = Mathf.Min(near, Mathf.Abs(f.x - Slope.Centre(f.z)));
                sb.AppendLine($"lift {l.k}{(l.second ? "b" : "")}{(l.summit ? " (Summit)" : l.park ? " (park)" : "")}: z {l.top.z:0} to {l.bottom.z:0}, {l.offTop:+0;-0} and {l.offBottom:+0;-0} m off the piste, {l.plan:0} m, {l.s.Length} towers (tallest {tall:0.0} m, nearest the piste {near:0.0} m), longest span {longest:0} m, {l.chairs} chairs {l.spacing:0.0} m apart, clearance to spare {-worst:0.00} m (0 at a station's portal), try {l.tries}{(l.under.Length > 0 ? ", carried over lift " + l.under[0].k : "")}");
            }
            return sb.ToString();
        }
    }
}
