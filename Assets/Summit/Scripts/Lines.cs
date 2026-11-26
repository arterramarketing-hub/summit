using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Feature lines (features.md): runs of hits down one side of the piste, laid so the landing of each delivers a
    /// rider to the next take-off at the speed they ride at. Two styles:
    ///   Natural (a freeride line): rollers, step-downs, spines, hips, wind lips off the run's edge, fallen logs to slide
    ///   Park (a jump line): kickers and tables, a step-up, a gap, boxes and rails
    ///
    /// Where: a line needs a long stretch of open piste, and the course had none to spare (every cycle's late stretch was
    /// a ridge, a valley or a cliff band). So the lines take a cycle's late stretch in their place (Claims: one of the
    /// lower ones on every mountain, and the high one, just below the glacier, on half of them), from the danger section's
    /// open side (away from its deadly wall) down to the next cycle's first jump cliff. That stretch is graded (Grade:
    /// no benches or roll-overs, the short rollers eased out, the long swell kept), as a park is built on a graded slope:
    /// at the speed riders ride, every roll-over throws them, onto a hit's ramp or past its landing. It is groomed, too
    /// (Surfaces: no ice, crud or crust on it).
    ///
    /// Every hit is snow (a shape added to the mountain's height, under Slope.Kicker, so everything that keeps clear of
    /// a kicker keeps clear of it) except the jibs (logs, boxes, rails), which are the park's own ride-on features
    /// (Course.FeaturesOf), slid the same way.
    ///
    /// The spacing is worked out, not tuned: each hit is laid, then a rider is ridden into it and off it over the snow as
    /// it now stands (RideIn: on the drawn snow, leaving it where the rider would, and flown with the rider's gravity and
    /// air drag) at the slow, typical and fast ends of the speed riders actually ride at (Speed*: LineSurvey's numbers),
    /// and at the typical speed with a pop off the lip. Its size is cut until every one of those leaves from its lip and
    /// lands softly where it should, and the next hit starts past the fast landing, with room to set up (SetUp; a jib a
    /// second further, to line up on it). The way onto each hit is ridden again with it laid (Approach), so nothing (the
    /// hit before, popped or flown fast) comes down hard on its ramp. A slow rider lands sooner and rides further on the
    /// snow to the next lip: a second or so, inside Rider.LineHold, so a line ridden clean is one chain (RiderLines).
    ///
    /// The other half of the piste is always left clear (a rider who does not want the line rides round it), and
    /// nothing else is laid on a line's lane (Keep): no trees, lights, lifts, ledges, kickers, or the hill's riders.
    ///
    /// A pure function of the mountain's seed, like the rest of it: each cycle's lines are laid the first time
    /// anything asks for them, on whatever thread asks (a race only lays them twice, the same).
    /// </summary>
    public static class Lines
    {
        public static bool On = System.Environment.GetEnvironmentVariable("LINES_OFF") != "1";
        /// <summary>For the test tools: told why each hit laid was refused.</summary>
        public static System.Action<string> Told;

        public enum Style { Natural, Park }
        public enum Kind { Kicker, Table, StepDown, StepUp, Gap, Roller, Spine, Hip, SideHit, Log, Box, Rail }

        // ---- the speeds lines are laid for (LineSurvey: a rider weaving the piste and one straight down it, three
        // mountains: 37 m/s at the 10th percentile, 44 typical, 48 at the 90th; drag holds it there on every pitch)
        public const float SpeedLo = 37f, Speed = 44f, SpeedHi = 48f;
        /// <summary>The pop a rider gives off a lip (Rider: 5.5 to 8 m/s, by how long the tap was held), flown at the typical speed.</summary>
        public const float PopSpeed = 6.8f;
        /// <summary>How far before a lip a rider pops (the board leaves a lip's drawn facets a metre early: popped any later, it is already flying).</summary>
        public const float PopBefore = 2.5f;
        /// <summary>Landings no harder than these (m/s into the snow, square on): typical; slow and fast; popped. Rider: hard over 18
        /// (ridden away, wobbling), a slam over 28 and square on: a popped landing may be a hard one, never a slam.</summary>
        public const float SoftLanding = 15f, FirmLanding = 17f, PopLanding = 21f;
        /// <summary>Room to set up for the next hit after the farthest landing (s at the typical speed).</summary>
        public const float SetUp = 0.38f;

        public struct Hit
        {
            public Kind kind;
            public int index;
            public float z0;       // where its shape starts, down the hill
            public float len;      // its length
            public float lane;     // the middle of its top, across from the run's centre line
            public float half;     // half its top's width
            public float yaw;      // hips and wind lips: how far the lip is turned off square (rad, + faces +x)
            public float H, Lr, P1, P2, P3, P4;   // its shape (by kind: see Profile)
            public float seed;     // its own wander (natural edges)
            // its flights as laid (for the notes and the tests): landing z at each speed, how hard, air time
            public float landLo, landD, landHi, landPop, impLo, impD, impHi, impPop, airD, snowLo;
            public int jib;        // its ride-on feature's id, or -1
            /// <summary>Its take-off down the hill, on its lane (a turned hit's lip crosses the lane further down; a roller's: its first crest; a jib's: its end).</summary>
            public float LipZ => IsJib ? z0 + len : yaw != 0f ? z0 + Lr / Mathf.Cos(yaw) : z0 + Lr;
            public float EndZ => z0 + len;
            public bool IsJib => kind == Kind.Log || kind == Kind.Box || kind == Kind.Rail;
            /// <summary>A hit that throws you (not rollers or jibs): a take-off to land from.</summary>
            public bool Air => !IsJib && kind != Kind.Roller;
        }

        public struct Line
        {
            public int cycle, k, id;
            public Style style;
            public float side;            // which half of the piste it takes (-1 or +1)
            public float z0, z1;          // its entry (where the run-in starts) and the end of its last landing
            public Hit[] hits;
            public string Name => style == Style.Natural ? "Freeride Line" : "Jump Line";
        }

        // ---------------------------------------------------------------- laying them out

        static Line[][] byCycle;
        static bool[] laid;
        static int forSeed = int.MinValue;
        static readonly Line[] none = new Line[0];

        [System.ThreadStatic] static bool laying;
        [System.ThreadStatic] static List<Hit> layingHits;
        [System.ThreadStatic] static float layFrom, layTo;

        /// <summary>A line is being laid on this thread (its flights read the snow without the lifts' hills or the ledges: those are laid after, around it).</summary>
        public static bool Laying => laying;

        public static void Forget() { byCycle = null; laid = null; forSeed = int.MinValue; windows = null; windowSeed = int.MinValue; }

        /// <summary>The lines of a course cycle (laid the first time it is asked for).</summary>
        public static Line[] Of(int cycle)
        {
            if (!On || cycle < 0 || laying) return none;
            var table = byCycle; var done = laid;
            if (table == null || forSeed != Slope.Seed)
            {
                int n = Mathf.CeilToInt(Slope.FinishZ / Course.Cycle) + 2;
                table = new Line[n][]; done = new bool[n];
                byCycle = table; laid = done; forSeed = Slope.Seed;
            }
            if (cycle >= table.Length) return none;
            if (!done[cycle])
            {
                var made = Lay(cycle);
                table[cycle] = made;
                done[cycle] = true;
            }
            return table[cycle] ?? none;
        }

        /// <summary>Every line on today's mountain, top to bottom.</summary>
        public static List<Line> All()
        {
            var list = new List<Line>();
            int n = Mathf.CeilToInt(Slope.FinishZ / Course.Cycle) + 2;
            for (int c = 0; c < n; c++) list.AddRange(Of(c));
            return list;
        }

        /// <summary>
        /// Is z free for a line on `side` of the piste (0: either): open cruising piste, or the danger stretch on the side
        /// away from its deadly wall, with nothing else built on it or beside it.
        /// </summary>
        public static bool Free(float z, float side = 0f)
        {
            if (z < Course.GlacierBottom + 60f || z > Course.ClearFrom - 20f) return false;
            var sec = Course.At(z);
            bool wallNear = Cliffs.WallNear(z, 30f, out var wall);
            if (sec == Course.Section.Danger)
            {
                // (past the park's end, its own features, and never on the wall's side)
                if (side == 0f || Mathf.Repeat(z - Course.First, Course.Cycle) < Course.DangerAt + 20f) return false;
                if (Cliffs.WallOf(Course.CycleOf(z), out var w0) && w0.side == side) return false;
            }
            else if (sec != Course.Section.Cruise) return false;
            if (wallNear && (side == 0f || wall.side == side)) return false;
            if (Ridge.Has(z) || Ridge.InCanyons(z)) return false;
            if (CliffBand.Near(z, 40f, out _) || Bowl.Near(z, 30f, out _)) return false;
            if (Cliffs.JumpNear(z, 25f, out _)) return false;
            if (Slope.StepNear(z, 25f, out _)) return false;
            // (nor a blind roll-over: what is past its lip is out of sight)
            int s = Mathf.FloorToInt(z / Slope.DropSeg);
            for (int k = s - 1; k <= s + 1; k++)
                if (Slope.DropOf(k, out var d) && d.blind && z > d.LipZ - 45f && z < d.FootZ + 25f) return false;
            return true;
        }

        /// <summary>Why z is not free for a line (for the test tools), or null.</summary>
        public static string Why(float z)
        {
            if (z < Course.GlacierBottom + 60f) return "glacier"; if (z > Course.ClearFrom - 20f) return "finish";
            if (Cliffs.WallOf(Course.CycleOf(z), out var w) && (Free(z, -w.side))) return null;
            if (Course.At(z) != Course.Section.Cruise) return Course.At(z).ToString();
            if (Ridge.Has(z) || Ridge.InCanyons(z)) return "canyons";
            if (CliffBand.Near(z, 40f, out _)) return "band"; if (Bowl.Near(z, 30f, out _)) return "bowl";
            if (Cliffs.JumpNear(z, 25f, out _)) return "jumpcliff"; if (Cliffs.WallNear(z, 30f, out _)) return "wall";
            if (Slope.StepNear(z, 25f, out _)) return "step";
            return Free(z) ? null : "blind";
        }

        /// <summary>
        /// Does cycle c's late stretch (below the danger section, where a ridge, a valley or a cliff band would be) belong to
        /// the feature lines instead? On every mountain one of the stretches below the middle (whichever the seed draws), and
        /// on some (ClaimHigh) the high one too, just below the glacier; a mountain always keeps a band or a ridge in the
        /// others. Asked by Ridge and CliffBand, so a pure function of the seed and the cycle, and nothing else.
        /// </summary>
        public static bool Claims(int c)
        {
            if (!On || !Eligible(c)) return false;
            if (c == 1) return Slope.Hash01(7, 1, 133) < ClaimHigh;
            // (of the lower ones, the one whose draw is lowest)
            float mine = Slope.Hash01(c, 2, 133);
            int n = Mathf.CeilToInt(Slope.FinishZ / Course.Cycle) + 2;
            for (int k = 2; k < n; k++) if (k != c && Eligible(k) && Slope.Hash01(k, 2, 133) < mine) return false;
            return true;
        }

        /// <summary>Is z in a claimed cycle's late stretch (where its lines go: graded, with no bench or roll-over: Slope.MakeDrop)?</summary>
        public static bool ClaimedAt(float z)
        {
            if (!On || z < Course.First) return false;
            int c = Course.CycleOf(z);
            for (int cc = c - 1; cc <= c; cc++)
            {
                if (!Claims(cc)) continue;
                float zb = Course.First + cc * Course.Cycle;
                if (z > zb + Course.DangerAt && z < zb + Course.Cycle + 80f) return true;
            }
            return false;
        }

        /// <summary>The share of mountains whose high stretch, just below the glacier, is the lines' too.</summary>
        public const float ClaimHigh = 0.5f;

        static bool Eligible(int c)
        {
            if (c < 1) return false;
            float zb = Course.First + c * Course.Cycle;
            return zb + Course.Cycle - 200f > Course.GlacierBottom + 60f && zb + 880f < Course.ClearFrom;
        }

        /// <summary>How often a stretch has a line, by how far down the mountain it is: some high up, most in the middle, fewer low down.</summary>
        static float Chance(float z)
        {
            float f = z / Slope.FinishZ;
            return f < 0.45f ? 0.6f : f < 0.78f ? 0.92f : 0.7f;
        }

        // ---- each cycle's stretch for lines (or none), worked out once for a mountain
        static Vector3[] windows; static int windowSeed = int.MinValue;

        /// <summary>
        /// Cycle c's stretch for lines, if it has one: the longest run of free piste (Free) below its danger section, on the
        /// side away from the deadly wall (open: that side), long enough for a line, and either claimed (Claims) or drawn
        /// by the seed (Chance). Its fall line is graded (Grade). Pure: the seed and the course's layout, nothing laid.
        /// </summary>
        public static bool Window(int c, out float from, out float to, out float open)
        {
            from = to = open = 0f;
            if (!On || c < 1) return false;
            var w = windows;
            if (w == null || windowSeed != Slope.Seed)
            {
                int n = Mathf.CeilToInt(Slope.FinishZ / Course.Cycle) + 2;
                w = new Vector3[n];
                for (int k = 0; k < n; k++) w[k] = MakeWindow(k);
                windows = w; windowSeed = Slope.Seed;
            }
            if (c >= w.Length || w[c].z == 0f) return false;
            from = w[c].x; to = w[c].y; open = w[c].z;
            return true;
        }

        static Vector3 MakeWindow(int cycle)
        {
            if (cycle < 1) return Vector3.zero;
            float zb = Course.First + cycle * Course.Cycle;
            float from = -1f, to = -1f, a0 = -1f;
            float open = Cliffs.WallOf(cycle, out var wl) ? -wl.side : (Slope.Hash01(cycle, 3, 131) < 0.5f ? -1f : 1f);
            for (float z = zb + Course.DangerAt + 20f; z <= zb + Course.Cycle + 80f; z += 5f)
            {
                bool f = Free(z, open) && z < zb + Course.Cycle + 80f;
                if (f && a0 < 0f) a0 = z;
                if (!f && a0 >= 0f) { if (z - a0 > to - from) { from = a0; to = z; } a0 = -1f; }
            }
            Told?.Invoke($"cycle {cycle}: free {from - zb:0}-{to - zb:0}, claimed {Claims(cycle)}");
            if (from < 0f || to - from < 190f) return Vector3.zero;
            if (!Claims(cycle) && Slope.Hash01(cycle, 1, 131) > Chance(from)) return Vector3.zero;
            return new Vector3(from, to, open);
        }

        /// <summary>The ramp, at each end of a line stretch, over which its grading comes in and goes.</summary>
        public const float GradeRamp = 50f;

        /// <summary>
        /// How much of the fall line's short rollers and its benches and roll-overs (Slope.FallLine) are graded out at z:
        /// 1 along a line stretch, easing in and out over GradeRamp inside its ends (a park is built on a graded slope: at
        /// the speed riders ride, every one of those throws them, onto a hit's ramp or past its landing).
        /// </summary>
        public static float Grade(float z)
        {
            if (!On || z < Course.GlacierBottom) return 0f;
            int c = Course.CycleOf(z);
            for (int cc = c - 1; cc <= c; cc++)
            {
                if (!Window(cc, out float a, out float b, out _)) continue;
                if (z <= a || z >= b) continue;
                return Ss(Mathf.Min(z - a, b - z) / GradeRamp);
            }
            return 0f;
        }

        static Line[] Lay(int cycle)
        {
            if (!Window(cycle, out float from, out float to, out float open)) return none;
            // a long stretch has room for two, on either side of the piste in turn
            var lines = new List<Line>();
            // one line as far down the stretch as its hits go, and a second below it if there is room for one (on the other side
            // if that side is free there, on the same side if not)
            float side = open, a = from;
            for (int k = 0; k < 2; k++)
            {
                float b = to;
                if (k == 1)
                {
                    float other = -side, a2 = a, b2 = to;
                    while (a2 < b2 && !Free(a2, other)) a2 += 5f;
                    for (float z = a2; z < b2; z += 5f) if (!Free(z, other)) { b2 = z; break; }
                    if (b2 - a2 >= 190f) { side = other; a = a2; b = b2; }
                    if (b - a < 190f) break;
                }
                float alt = a / Slope.FinishZ;
                // freeride lines high up, park lines lower down, either in the middle; a second line, often the other kind
                var style = Slope.Hash01(cycle, 10 + k, 131) < (alt < 0.5f ? 0.7f : alt < 0.75f ? 0.5f : 0.3f) ? Style.Natural : Style.Park;
                if (k == 1 && lines.Count > 0 && Slope.Hash01(cycle, 12, 131) < 0.7f) style = lines[0].style == Style.Natural ? Style.Park : Style.Natural;
                bool laid = LayLine(cycle, k, style, side, a, b, out var L);
                if (laid) { lines.Add(L); a = L.z1 + 35f; }
                else if (k == 0) a = b;
            }
            return lines.ToArray();
        }

        // the order of a line's hits (cycled through, as many as fit), one picked per line
        static readonly Kind[][] natural =
        {
            new[] { Kind.Roller, Kind.StepDown, Kind.Log, Kind.Spine, Kind.SideHit, Kind.StepDown },
            new[] { Kind.SideHit, Kind.Roller, Kind.Spine, Kind.Log, Kind.Hip, Kind.StepDown },
            new[] { Kind.StepDown, Kind.Log, Kind.Roller, Kind.Hip, Kind.Spine, Kind.StepDown },
            new[] { Kind.Roller, Kind.Spine, Kind.StepDown, Kind.SideHit, Kind.Log, Kind.Roller },
        };
        static readonly Kind[][] park =
        {
            new[] { Kind.Kicker, Kind.Table, Kind.Box, Kind.StepUp, Kind.Gap, Kind.Rail },
            new[] { Kind.Table, Kind.Rail, Kind.Kicker, Kind.Gap, Kind.Box, Kind.Hip },
            new[] { Kind.Kicker, Kind.Box, Kind.Gap, Kind.Table, Kind.Rail, Kind.StepUp },
            new[] { Kind.Table, Kind.Kicker, Kind.Rail, Kind.Hip, Kind.StepUp, Kind.Kicker },
        };

        static bool LayLine(int cycle, int k, Style style, float side, float from, float to, out Line L)
        {
            L = default;
            var order = (style == Style.Natural ? natural : park)[Mathf.FloorToInt(Slope.Hash01(cycle, 20 + k, 131) * 3.999f)];
            float lane = side * (7f + 2.5f * Slope.Hash01(cycle, 21 + k, 131));
            var hits = new List<Hit>();
            laying = true; layingHits = hits; layFrom = from; layTo = to;
            try
            {
                float z = from + 45f;   // the run-in: the sign, then a clear 45 m to the first hit
                // (and nothing on the run-in itself that throws a rider and comes down hard: a roll-over in it moves the line down)
                for (int tries = 0; tries < 8; tries++)
                {
                    bool soft = true;
                    foreach (float v in new[] { Speed, SpeedHi })
                        soft &= RideIn(lane, from, z - 10f, v, float.NaN, out _, out _, out float imp, out _) && imp <= SoftLanding;
                    if (soft) break;
                    from += 10f; z += 10f;
                }
                int jibs = 0;
                for (int i = 0; i < 9; i++)
                {
                    var kind = order[i % order.Length];
                    var alt2 = style == Style.Natural ? (i % 2 == 0 ? Kind.StepDown : Kind.Roller) : (i % 2 == 0 ? Kind.Kicker : Kind.Table);
                    bool got = false; Hit h = default;
                    // (where it would go, or a little further down if the hit before's popped flight would come down hard on it;
                    // and if it will not lie there, a smaller kind in its place, or the line ends)
                    // (a jib after a jump: room to line up on it after the fast landing, a second)
                    float z1 = z;
                    if (hits.Count > 0 && hits[hits.Count - 1].Air && (kind == Kind.Log || kind == Kind.Box || kind == Kind.Rail))
                        z1 = Mathf.Max(z, hits[hits.Count - 1].landHi + 1.0f * Speed);
                    for (int push = 0; push < 8 && !got; push++)
                    {
                        float zz = z1 + push * 7f;
                        got = TryHit(cycle, k, style, side, lane, kind, i, hits.Count, zz, to, ref jibs, out h) && Approach(hits, h, from);
                        if (!got && kind != alt2) got = TryHit(cycle, k, style, side, lane, alt2, i, hits.Count, zz, to, ref jibs, out h) && Approach(hits, h, from);
                    }
                    if (!got) break;
                    if (h.IsJib) jibs++;
                    hits.Add(h);
                    z = Next(h);
                }
                // the slow end's time on the snow from each landing to the next lip (for the notes: inside Rider.LineHold)
                for (int i = 0; i + 1 < hits.Count; i++)
                {
                    var h = hits[i];
                    float land = h.IsJib ? h.EndZ + 12f : h.kind == Kind.Roller ? h.EndZ : h.landLo;
                    h.snowLo = Mathf.Max(0f, ((hits[i + 1].IsJib ? hits[i + 1].z0 : hits[i + 1].LipZ) - land) / SpeedLo);
                    hits[i] = h;
                }
                if (hits.Count < 3) return false;
                var last = hits[hits.Count - 1];
                L = new Line
                {
                    cycle = cycle, k = k, id = cycle * 4 + k, style = style, side = side, z0 = from,
                    z1 = Mathf.Max(last.EndZ, last.Air ? Mathf.Max(last.landHi, last.landPop) + 10f : last.EndZ + 20f),
                    hits = hits.ToArray(),
                };
                return true;
            }
            finally { laying = false; layingHits = null; }
        }

        /// <summary>
        /// The way onto h with it laid: from the hit before (or the line's entry) down to past h's lip, at the slow,
        /// typical and fast speeds and popped off the hit before, every landing as soft as it must be (nothing thrown
        /// onto h's ramp off a roll-over or the hit before), and onto a jib on the snow, not flying over it.
        /// </summary>
        static bool Approach(List<Hit> hits, Hit h, float from)
        {
            int at = layingHits.Count;
            layingHits.Add(h);
            try
            {
                bool first = hits.Count == 0;
                var prev = first ? default : hits[hits.Count - 1];
                float zA = first ? from - 40f : prev.z0 - 15f;   // (a rider comes into a line from the run above it)
                float zB = h.IsJib ? h.z0 : (h.kind == Kind.Roller ? h.EndZ : h.LipZ) + 4f;
                foreach (float v in new[] { SpeedLo, Speed, SpeedHi })
                {
                    if (!RideIn(h.lane, zA, zB, v, float.NaN, out float tz, out float lz, out float imp, out _)) return false;
                    if (imp > (v == Speed ? SoftLanding : FirmLanding)) { Told?.Invoke($"  (onto {h.kind} at z{h.z0:0} at {v:0}: lands {imp:0.0} m/s at z{lz:0})"); return false; }
                    if (h.IsJib && tz < zB - 0.01f && lz > h.z0 - 5f) { Told?.Invoke($"  (onto {h.kind} at z{h.z0:0} at {v:0}: flies onto it, z{tz:0}-{lz:0})"); return false; }
                }
                if (!first && (prev.Air || prev.kind == Kind.Roller))
                {
                    if (!RideIn(prev.lane, zA, zB, Speed, prev.LipZ - PopBefore, out _, out float lz, out float imp, out _)) return false;
                    Told?.Invoke($"  (pop off {prev.kind} with {h.kind} at z{h.z0:0}: lands z{lz:0} {imp:0.0} m/s)");
                    if (imp > PopLanding) return false;
                }
                return true;
            }
            finally { layingHits.RemoveAt(at); }
        }

        /// <summary>Where the hit after h can start: past the farthest of its landings, with room to set up.</summary>
        static float Next(in Hit h)
        {
            if (h.IsJib) return h.EndZ + 30f;   // (off the end with a little hop, down, and lined up for the next)
            if (h.kind == Kind.Roller) return Mathf.Max(h.EndZ + 8f, h.landHi + SetUp * Speed * 0.6f);
            return Mathf.Max(h.EndZ + 4f, h.landHi + SetUp * Speed);
        }

        /// <summary>Lay one hit at z (its shape's start) and fly it; shrunk until every flight off it lands as it should.</summary>
        static bool TryHit(int cycle, int k, Style style, float side, float lane, Kind kind, int slot, int index, float z, float to, ref int jibs, out Hit h)
        {
            h = default;
            float r1 = Slope.Hash01(cycle * 16 + slot, 30 + k, 131), r2 = Slope.Hash01(cycle * 16 + slot, 31 + k, 131), r3 = Slope.Hash01(cycle * 16 + slot, 32 + k, 131);
            bool nat = style == Style.Natural;
            h.kind = kind; h.index = index; h.z0 = z; h.lane = lane; h.jib = -1; h.seed = r3 * 50f;
            h.half = nat ? 5.5f + 1.5f * r1 : 5f + 1f * r1;
            switch (kind)
            {
                case Kind.Kicker: h.H = 1.6f + 0.5f * r2; h.Lr = 10f + 2f * r1; h.P1 = 6f; h.P2 = 0f; break;
                case Kind.Table: h.H = 1.7f + 0.4f * r2; h.Lr = 10.5f + 2f * r1; h.P1 = 7f; h.P2 = 6f + 4f * r3; break;
                case Kind.StepDown: h.H = 0.8f + 0.5f * r2; h.Lr = 9f + 3f * r1; h.P1 = 1.8f + 1.2f * r3; h.P2 = 6f; h.P3 = 20f; break;
                case Kind.Spine: h.H = 1.9f + 0.6f * r2; h.Lr = 9f + 2f * r1; h.P1 = 1.2f + 0.8f * r3; h.P2 = 6.5f; h.P3 = 16f; h.half = 4.2f + 1f * r1; break;
                case Kind.StepUp: h.H = 2f + 0.4f * r2; h.Lr = 11f + 2f * r1; h.P1 = 10f + 4f * r3; h.P2 = 1.3f + 0.5f * r3; h.P3 = 4.5f; h.P4 = 30f; break;
                case Kind.Gap: h.H = 1.9f + 0.4f * r2; h.Lr = 11f + 2f * r1; h.P1 = 1.6f + 0.6f * r3; h.P2 = 5f; h.P3 = 8f + 6f * r1; h.P4 = 10f; break;
                case Kind.Roller: h.H = 0.9f + 0.6f * r2; h.P1 = 20f + 6f * r1; h.P2 = 2f + Mathf.Floor(r3 * 1.999f); h.Lr = h.P1 * 0.5f; h.half = 6.5f + 1.5f * r1; break;
                case Kind.Hip: h.H = 1.5f + 0.4f * r2; h.Lr = 10f + 2f * r1; h.P1 = 6f; h.P2 = 1.2f + 0.6f * r3; h.P3 = 16f; h.yaw = side * (0.3f + 0.12f * r3); h.half = 6f; break;
                case Kind.SideHit:
                    h.H = 1.1f + 0.4f * r2; h.Lr = 8f + 2f * r1; h.P1 = 5f; h.P2 = 0.8f + 0.5f * r3; h.P3 = 14f; h.half = 4f;
                    h.lane = side * (Slope.PisteHalf - 6f); h.yaw = -side * (0.14f + 0.08f * r3); break;
                case Kind.Log: h.Lr = h.len = 15f + 6f * r1; h.H = 0.45f; h.half = 0.26f; break;
                case Kind.Box: h.Lr = h.len = 14f + 6f * r1; h.H = 0.6f; h.half = 0.55f; break;
                case Kind.Rail: h.Lr = h.len = 16f + 8f * r1; h.H = 0.8f; h.half = 0.08f; break;
            }
            if (h.IsJib)
            {
                if (jibs >= 12 || z + h.len + 30f > to - 15f) return false;
                // (on a stretch that holds its pitch: a jib laid over a roll-over would be launched off it)
                float p0 = Pitch(lane, z), p1 = Pitch(lane, z + h.len);
                if (Mathf.Abs(p1 - p0) > 0.12f) return false;
                // (and on the snow coming onto it, at every speed: not flying over it off a roll above it)
                foreach (float v in new[] { SpeedLo, Speed, SpeedHi })
                {
                    if (!RideIn(lane, z - 28f, z, v, float.NaN, out float tz, out float lz, out _, out _)) return false;
                    if (tz < z - 0.01f && lz > z - 5f) return false;
                }
                h.jib = cycle * 16 + 4 + jibs;
                return true;
            }
            h.len = Length(h);
            for (int tries = 0; tries < 6; tries++)
            {
                if (z + h.len > to - 25f) return false;
                if (Check(ref h, to)) return true;
                // too big for here: smaller, and a gap or a step-up shorter across
                h.H *= 0.84f;
                if (kind == Kind.Gap) { h.P3 *= 0.8f; h.P1 *= 0.9f; }
                if (kind == Kind.StepUp) { h.P1 *= 0.85f; h.P2 *= 0.9f; h.H = h.H / 0.84f * 0.93f; }
                if (kind == Kind.Roller) h.P2 = Mathf.Max(1f, h.P2 - 1f);
                if (kind == Kind.StepDown || kind == Kind.Spine || kind == Kind.Hip || kind == Kind.SideHit) h.P1 = Mathf.Max(0.5f, h.P1 * 0.85f);
                h.len = Length(h);
            }
            return false;
        }

        /// <summary>The fall line's pitch along the lane at z (over 10 m).</summary>
        static float Pitch(float lane, float z) => (Slope.Height(Slope.Centre(z - 5f) + lane, z - 5f) - Slope.Height(Slope.Centre(z + 5f) + lane, z + 5f)) / 10f;

        static float Length(in Hit h)
        {
            switch (h.kind)
            {
                case Kind.Kicker: return h.Lr + h.P1;
                case Kind.Table: return h.Lr + h.P2 + h.P1;
                case Kind.StepDown: case Kind.Spine: return h.Lr + h.P2 + h.P3;
                case Kind.StepUp: return h.Lr + h.P1 + h.P3 + h.P4;
                case Kind.Gap: return h.Lr + h.P2 + h.P3 + h.P4;
                case Kind.Roller: return h.P1 * h.P2;
                case Kind.Hip: case Kind.SideHit: return h.Lr + h.P1 + h.P3 + h.half * Mathf.Abs(Mathf.Sin(h.yaw)) * 2f;
            }
            return h.len;
        }

        /// <summary>Ride the hit at every speed; true if every flight off it lands as it must. Fills in its flights.</summary>
        static bool Check(ref Hit h, float to)
        {
            int at = layingHits.Count;
            layingHits.Add(h);   // (the snow as it will be, the hit on it)
            try
            {
                float lipZ = h.LipZ;
                float zA = h.z0 - 15f, zB = h.kind == Kind.Roller ? h.EndZ + 4f : lipZ + 4f;
                bool ok = true;
                ok &= RideIn(h.lane, zA, zB, SpeedLo, float.NaN, out float t0, out h.landLo, out h.impLo, out _);
                ok &= RideIn(h.lane, zA, zB, Speed, float.NaN, out float t1, out h.landD, out h.impD, out h.airD);
                ok &= RideIn(h.lane, zA, zB, SpeedHi, float.NaN, out float t2, out h.landHi, out h.impHi, out _);
                ok &= RideIn(h.lane, zA, zB, Speed, lipZ - PopBefore, out _, out h.landPop, out h.impPop, out _);
                Told?.Invoke($"  {h.kind} z{h.z0:0} H{h.H:0.00}: off z{t0 - lipZ:0}/{t1 - lipZ:0}/{t2 - lipZ:0}, lands lo {h.landLo - lipZ:0}/{h.impLo:0.0} d {h.landD - lipZ:0}/{h.impD:0.0} air {h.airD:0.00} hi {h.landHi - lipZ:0}/{h.impHi:0.0} pop {h.landPop - lipZ:0}/{h.impPop:0.0} ok={ok}");
                if (!ok) return false;
                if (h.impD > SoftLanding || h.impHi > FirmLanding || h.impPop > PopLanding || h.impLo > FirmLanding) return false;
                // off this hit's own lip, at every speed (not off a roll-over above it, nor late)
                if (h.kind != Kind.Roller)
                {
                    float lo = lipZ - (h.yaw != 0f ? h.half + 6f : 6f), hi = lipZ + (h.yaw != 0f ? h.half + 4f : 4f);
                    foreach (float tz in new[] { t0, t1, t2 }) if (tz < lo || tz > hi) return false;
                }
                // the far landings stay in the stretch
                if (h.landHi + 10f > to) return false;
                // a gap or a step-up: the slow end clears it (lands past the far side's top, with room)
                if (h.kind == Kind.Gap && h.landLo < h.z0 + h.Lr + h.P2 + h.P3 + h.P4 * 0.7f) return false;
                if (h.kind == Kind.StepUp && h.landLo < h.z0 + h.Lr + h.P1 + h.P3 + 3f) return false;
                // the others: the slow end clears the lip's own knuckle, not landing on the flat top or its edge
                if ((h.kind == Kind.Kicker || h.kind == Kind.Hip || h.kind == Kind.SideHit || h.kind == Kind.StepDown || h.kind == Kind.Spine) && h.landLo < lipZ + 6f) return false;
                // a hit with any air in it: enough to score (Rider: over 0.45 s)
                if (h.kind != Kind.Roller && h.airD < 0.6f) return false;
                return true;
            }
            finally { layingHits.RemoveAt(at); }
        }

        /// <summary>
        /// A rider riding the lane from zA at speed v (held on the snow, as a rider at that speed is), the way the rider
        /// leaves the snow (RiderMove: where it falls away faster than gravity can follow, it flies, as it came; on the drawn
        /// snow, the rider's own, whose facets break a crest sooner than the smooth slope does), popping
        /// at popZ if given, until on the snow past zB: where it first took off, where it last came down and how hard
        /// (the hardest of its landings), and its longest air.
        /// </summary>
        public static bool RideIn(float lane, float zA, float zB, float v, float popZ, out float takeZ, out float landZ, out float impact, out float air)
        {
            const float dt = 1f / 120f;
            float g = Rider.G;
            takeZ = float.NaN; landZ = zA; impact = 0f; air = 0f;
            float z = zA, x = Slope.Centre(z) + lane, y = Slope.DrawnHeight(x, z);
            float vyPrev = float.NaN;
            bool popped = float.IsNaN(popZ);
            for (int n = 0; n < 1200; n++)
            {
                // the way along the lane, and the snow's slope that way
                float dxdz = (Slope.Centre(z + 1f) - Slope.Centre(z - 1f)) * 0.5f;
                var d = new Vector2(dxdz, 1f).normalized;
                float look = Mathf.Max(0.02f, v * dt);
                float here = Slope.DrawnHeight(x, z), ahead = Slope.DrawnHeight(x + d.x * look, z + d.y * look);
                float sl = (ahead - here) / look;
                float cosP = 1f / Mathf.Sqrt(1f + sl * sl), sinP = sl * cosP;
                float vy = v * sinP, flat = v * cosP;
                if (float.IsNaN(vyPrev)) vyPrev = vy;
                bool lip = vy < vyPrev - g * dt * 1.2f;
                bool pop = !popped && z >= popZ;
                if (lip || pop)
                {
                    var vel = new Vector3(d.x * flat, lip ? vyPrev : vy, d.y * flat);
                    if (pop)
                    {
                        popped = true;
                        var nrm = Slope.Normal(x, z);
                        vel += (nrm.z < 0f ? Vector3.Slerp(nrm, Vector3.up, 0.85f) : nrm) * PopSpeed;
                    }
                    if (float.IsNaN(takeZ)) takeZ = z;
                    if (!Fly(x, here, z, vel, out float lz, out float imp, out float t)) return false;
                    impact = Mathf.Max(impact, imp); air = Mathf.Max(air, t); landZ = lz;
                    // (down on the snow there, going on at the same speed)
                    z = lz; x = Slope.Centre(z) + lane; vyPrev = float.NaN;
                    if (z > zB) return true;
                    continue;
                }
                vyPrev = vy;
                x += d.x * flat * dt; z += d.y * flat * dt;
                if (z > zB) { if (float.IsNaN(takeZ)) { takeZ = z; landZ = z; } return true; }
            }
            return false;
        }

        /// <summary>
        /// A rider's flight from (x, y, z) at v over the snow as it is, as the rider flies (Rider.AirStep: its gravity
        /// and air drag, no steering): where it comes down, how hard into the snow (square on), how long it was up.
        /// </summary>
        public static bool Fly(float x, float y, float z, Vector3 v, out float landZ, out float impact, out float air)
        {
            const float dt = 1f / 90f;
            float g = Rider.G;
            var p = new Vector3(x, y + 0.02f, z);
            landZ = z; impact = 0f; air = 0f;
            for (float t = 0f; t < 4.5f; t += dt)
            {
                v.y -= g * dt;
                v -= v * Mathf.Min(1f, 0.0025f * v.magnitude * dt);
                var q = p + v * dt;
                float gy = Slope.DrawnHeight(q.x, q.z);
                if (q.y <= gy && t > 0.03f)
                {
                    // (where between the two steps it came down)
                    float g0 = Slope.DrawnHeight(p.x, p.z), a = (p.y - g0), b = (gy - q.y);
                    float f = a + b > 1e-4f ? a / (a + b) : 1f;
                    var hit = Vector3.Lerp(p, q, f);
                    var n = Slope.Normal(hit.x, hit.z);
                    impact = Mathf.Max(0f, -Vector3.Dot(v, n));
                    landZ = hit.z; air = t + f * dt;
                    return true;
                }
                if (q.y <= gy) { q.y = gy + 0.01f; }
                p = q;
            }
            return false;
        }

        // ---------------------------------------------------------------- the snow's shape

        static float Ss(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>A hit's height over the snow along it (u metres from its start), before its width is taken in.</summary>
        static float Profile(in Hit h, float u)
        {
            if (u <= 0f || u >= h.len) return 0f;
            float H = h.H, Lr = h.Lr;
            switch (h.kind)
            {
                case Kind.Kicker:
                case Kind.Table:
                {
                    // a curved ramp steepest at the lip, a flat top (a table's), then its knuckle down onto the slope
                    if (u < Lr) { float t = u / Lr; return H * t * t; }
                    u -= Lr;
                    if (u < h.P2) return H;
                    return H * (1f - Ss((u - h.P2) / h.P1));
                }
                case Kind.StepDown:
                case Kind.Spine:
                case Kind.Hip:
                case Kind.SideHit:
                {
                    // up onto a rounded lip, the face down past the slope's line, and the landing below easing back up to it
                    // (P1: how far below; P2: the face; P3: the landing)
                    float face = h.kind == Kind.Hip || h.kind == Kind.SideHit ? h.P1 : h.P2;
                    float land = h.P3, D = h.kind == Kind.Hip || h.kind == Kind.SideHit ? h.P2 : h.P1;
                    if (u < Lr) { float t = u / Lr; return H * t * t; }
                    u -= Lr;
                    if (u < face) return H - (H + D) * Ss(u / face);
                    u -= face;
                    if (u < land) return -D * (1f - Ss(u / land));
                    return 0f;
                }
                case Kind.StepUp:
                {
                    // the kicker, its knuckle back onto the slope, the gap, then a riser up onto the deck you land on
                    if (u < Lr) { float t = u / Lr; return H * t * t; }
                    u -= Lr;
                    if (u < h.P1) return H * (1f - Ss(u / (0.45f * h.P1)));
                    u -= h.P1;
                    if (u < h.P3) return h.P2 * Ss(u / h.P3);
                    u -= h.P3;
                    return h.P2 * (1f - Ss(u / h.P4));
                }
                case Kind.Gap:
                {
                    // the kicker, a wall down into the gap, its floor, and the far side back up to the slope's line
                    if (u < Lr) { float t = u / Lr; return H * t * t; }
                    u -= Lr;
                    if (u < h.P2) return H - (H + h.P1) * Ss(u / h.P2);
                    u -= h.P2;
                    if (u < h.P3) return -h.P1;
                    u -= h.P3;
                    return -h.P1 * (1f - Ss(u / h.P4));
                }
                case Kind.Roller:
                {
                    float c = Mathf.Cos(2f * Mathf.PI * u / h.P1);
                    return H * 0.5f * (1f - c);
                }
            }
            return 0f;
        }

        /// <summary>How much of a hit's height reaches w across from its middle: its top, its shoulders, and (natural) a wandering edge.</summary>
        static float Width(in Hit h, float w, float u, bool nat)
        {
            float a = Mathf.Abs(w);
            if (h.kind == Kind.Spine)
            {
                // a peaked ridge, dying away at its ends
                float half = h.half * (1f + 0.25f * (Slope.Noise(u * 0.11f + h.seed, 7.3f) - 0.5f));
                if (a >= half + 2f) return 0f;
                float t = a / (half + 2f); float m = 1f - t * t; return m * m;
            }
            float hw = h.half, soft = nat ? 4.5f : 3f;
            if (nat) hw *= 1f + 0.3f * (Slope.Noise(u * 0.09f + h.seed, 3.1f + w * 0.02f) - 0.5f);
            float s = Mathf.Clamp01((a - (hw - soft * 0.5f)) / soft);
            return 1f - Ss(s);
        }

        /// <summary>The point u along a hit and w across it (in its own frame: a turned hit's is turned), on the snow.</summary>
        public static Vector3 HitPoint(in Hit h, float u, float w)
        {
            float x, z;
            if (h.yaw != 0f)
            {
                float s = Mathf.Sin(h.yaw), c = Mathf.Cos(h.yaw);
                float xc = Slope.Centre(h.z0 + h.Lr) + h.lane;
                x = xc + u * s + w * c; z = h.z0 + u * c - w * s;
            }
            else { z = h.z0 + u; x = Slope.Centre(z) + h.lane + w; }
            return new Vector3(x, Slope.Height(x, z), z);
        }

        /// <summary>The height a hit adds at (x, z).</summary>
        static float HitHeight(in Hit h, float x, float z, bool nat)
        {
            if (h.IsJib) return 0f;
            float pad = h.half + 5f;
            if (h.yaw != 0f)
            {
                // turned: its own frame, from the middle of its start line
                float zc = h.z0, xc = Slope.Centre(h.z0 + h.Lr) + h.lane;
                float s = Mathf.Sin(h.yaw), c = Mathf.Cos(h.yaw);
                float dx = x - xc, dz = z - zc;
                float u = dz * c + dx * s, w = dx * c - dz * s;
                if (Mathf.Abs(w) > pad || u <= 0f || u >= h.len) return 0f;
                return Profile(h, u) * Width(h, w, u, nat);
            }
            float uu = z - h.z0;
            if (uu <= 0f || uu >= h.len) return 0f;
            float ww = x - (Slope.Centre(z) + h.lane);
            if (Mathf.Abs(ww) > pad) return 0f;
            return Profile(h, uu) * Width(h, ww, uu, nat);
        }

        /// <summary>The height every line adds at (x, z) (in Slope.Kicker).</summary>
        public static float Height(float x, float z)
        {
            if (!On || z < Course.GlacierBottom) return 0f;
            if (laying)
            {
                var hs = layingHits;
                if (hs == null || z < layFrom - 5f || z > layTo + 60f) return 0f;
                float y = 0f;
                for (int i = 0; i < hs.Count; i++) y += HitHeight(hs[i], x, z, true);
                return y;
            }
            int c = Course.CycleOf(z);
            float sum = 0f;
            for (int cc = c - 1; cc <= c; cc++)
            {
                if (cc < 1) continue;
                var ls = Of(cc);
                for (int i = 0; i < ls.Length; i++)
                {
                    ref var L = ref ls[i];
                    if (z < L.z0 || z > L.z1 + 30f) continue;
                    if (Mathf.Abs(x - Slope.Centre(z) - L.side * 11f) > 26f) continue;
                    bool nat = L.style == Style.Natural;
                    for (int j = 0; j < L.hits.Length; j++)
                    {
                        ref var h = ref L.hits[j];
                        if (z < h.z0 - 8f) break;
                        if (z > h.EndZ + 8f) continue;
                        sum += HitHeight(h, x, z, nat);
                    }
                }
            }
            return sum;
        }

        /// <summary>
        /// How much of (x, z) is a freeride line's shaped snow (its rollers, lips, spines and landings), 0..1: wind-packed
        /// there (Surfaces), so the shapes read. Eased by how far the snow is lifted or lowered (no faster than SurfaceTest's
        /// step allows across a hit's soft sides).
        /// </summary>
        public static float NaturalShare(float x, float z)
        {
            if (!On || z < Course.GlacierBottom) return 0f;
            if (!LineNear(z, 0f, out var L) || L.style != Style.Natural) return 0f;
            float y = 0f;
            foreach (var h in L.hits) if (!h.IsJib && z > h.z0 - 2f && z < h.EndZ + 2f) y += Mathf.Abs(HitHeight(h, x, z, true));
            return Ss(y / 2.3f);
        }

        // ---------------------------------------------------------------- what keeps clear

        /// <summary>The line whose stretch covers z (its run-in to its last landing), if any.</summary>
        public static bool LineNear(float z, float margin, out Line found)
        {
            found = default;
            if (!On || z < Course.GlacierBottom || laying) return false;
            int c = Course.CycleOf(z);
            for (int cc = c - 1; cc <= c; cc++)
            {
                if (cc < 1) continue;
                foreach (var L in Of(cc))
                    if (z > L.z0 - margin && z < L.z1 + margin) { found = L; return true; }
            }
            return false;
        }

        /// <summary>A cruise kicker is not built here (a line's stretch, or, while one is being laid, the stretch it is laid in).</summary>
        public static bool NoKicker(float z)
        {
            if (!On) return false;
            if (laying) return z > layFrom - 40f && z < layTo + 20f;
            return LineNear(z, 40f, out _);
        }

        /// <summary>On a line's lane (its hits, their landings and the run between them), with margin: nothing else stands there.</summary>
        public static bool Keep(float x, float z, float margin)
        {
            if (!LineNear(z, margin, out var L)) return false;
            float a = x - Slope.Centre(z);
            // the lane: its middle and its hits' widths (a wind lip's out at the edge)
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var h in L.hits) { lo = Mathf.Min(lo, h.lane - h.half - 4f); hi = Mathf.Max(hi, h.lane + h.half + 4f); }
            if (L.side > 0f) hi = Mathf.Max(hi, Slope.PisteHalf + 2f); else lo = Mathf.Min(lo, -Slope.PisteHalf - 2f);
            return a > lo - margin && a < hi + margin;
        }

        /// <summary>The run's half that a line leaves clear at z: (lo, hi) across from the centre line. False off every line.</summary>
        public static bool ClearSide(float z, out float lo, out float hi)
        {
            lo = -Slope.PisteHalf; hi = Slope.PisteHalf;
            if (!LineNear(z, 0f, out var L)) return false;
            if (L.side > 0f) hi = 2f; else lo = -2f;
            return true;
        }

        // ---------------------------------------------------------------- jibs: logs, boxes and rails

        /// <summary>The lines' ride-on features of a cycle (Course.FeaturesOf adds them to the park's).</summary>
        public static void JibsOf(int cycle, List<Course.Feature> into)
        {
            foreach (var L in Of(cycle))
                foreach (var h in L.hits)
                {
                    if (!h.IsJib) continue;
                    float z0 = h.z0, z1 = h.z0 + h.len;
                    // (a log lies a little across the line, the way it fell)
                    float skew = h.kind == Kind.Log ? (h.seed / 50f - 0.5f) * 1.6f : 0f;
                    float x0 = Slope.Centre(z0) + h.lane - skew, x1 = Slope.Centre(z1) + h.lane + skew;
                    var kind = h.kind == Kind.Log ? Course.Kind.Log : h.kind == Kind.Box ? Course.Kind.Box : Course.Kind.Rail;
                    into.Add(new Course.Feature
                    {
                        kind = kind, height = h.H, halfWidth = h.half, id = h.jib,
                        a = new Vector3(x0, Slope.Height(x0, z0) + h.H, z0), b = new Vector3(x1, Slope.Height(x1, z1) + h.H, z1),
                    });
                }
        }

        /// <summary>A line's jib near z (for the rider, who looks for ride-on features only where there are some).</summary>
        public static bool JibNear(float z)
        {
            if (!LineNear(z, 20f, out var L)) return false;
            foreach (var h in L.hits) if (h.IsJib && z > h.z0 - 20f && z < h.EndZ + 10f) return true;
            return false;
        }

        // ---------------------------------------------------------------- the rider on a line

        /// <summary>
        /// The hit a rider leaving the snow at p is taking off from (its lip within reach, on its lane), as a key
        /// (line id * 32 + hit index), or -1. A jib is found by its feature's id.
        /// </summary>
        public static int HitAt(Vector3 p, int jibId = -1)
        {
            if (!LineNear(p.z, 0f, out var L)) return -1;
            for (int i = 0; i < L.hits.Length; i++)
            {
                var h = L.hits[i];
                if (jibId >= 0) { if (h.jib == jibId) return L.id * 32 + i; continue; }
                if (h.IsJib) continue;
                float lipZ = h.LipZ;
                float lip0 = h.kind == Kind.Roller ? h.z0 : lipZ - 7f, lip1 = h.kind == Kind.Roller ? h.EndZ : lipZ + 5f;
                if (h.yaw != 0f) { lip0 -= h.half; lip1 += h.half; }
                if (p.z < lip0 || p.z > lip1) continue;
                float w = p.x - (Slope.Centre(p.z) + h.lane);
                if (Mathf.Abs(w) > h.half + 2.5f) continue;
                return L.id * 32 + i;
            }
            return -1;
        }

        /// <summary>
        /// A roller ridden over on its lane from z0 to z1 this step (on the snow or in the air: a slow rider rolls them,
        /// a fast one flies them), as a key (line id * 32 + hit index), or -1.
        /// </summary>
        public static int RollerPassed(Vector3 p, float z0)
        {
            if (!LineNear(p.z, 0f, out var L)) return -1;
            for (int i = 0; i < L.hits.Length; i++)
            {
                var h = L.hits[i];
                if (h.kind != Kind.Roller) continue;
                float end = h.EndZ - 2f;
                if (z0 < end && p.z >= end && Mathf.Abs(p.x - (Slope.Centre(p.z) + h.lane)) < h.half + 2.5f) return L.id * 32 + i;
            }
            return -1;
        }

        /// <summary>How many hits the line with this id has (0: none).</summary>
        public static int CountOf(int lineId)
        {
            foreach (var L in Of(lineId / 4)) if (L.id == lineId) return L.hits.Length;
            return 0;
        }
    }
}
