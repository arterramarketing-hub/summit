using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The second line of each cycle: what makes the mountain a lift network, not a lift.
    ///
    /// From a chair at a real resort the sky is crossed by other lines: cables at other heights and angles, towers
    /// on the far side of the run. One line beside the piste per cycle (LiftLine.Lay) never gave that. So each
    /// cycle that has a first line gets a second where the ground allows one, laid by the same rules and the very
    /// same code (LiftLine.Try: stations on sound ground, towers 27 m or more off the piste's centre and never on
    /// kept ground, every chair clear of the snow by 5 m and of the piste by 9), with these differences:
    ///   - It is short (130 to 260 m down the mountain) and goes from 62 to 118 m out on one side of the piste to as
    ///     far out on the other, so it crosses the piste on one span, and the first line squarely: no two lifts are
    ///     ever side by side (Crowds: none within CrowdAngle of another and within CrowdNear of it for CrowdRun,
    ///     no station within StationApart of another's), as no resort would build them.
    ///   - It is over the piste only above the cycle's danger stretch (no cable over the crevasses), and if it runs
    ///     down beside that stretch it ends on the side the first line keeps to, away from the deadly cliff.
    ///   - Where it meets a line already laid it is carried over it: the lowest thing on its chairs clears the
    ///     other's cable by OverTop + OverClear (5 m: a tower's head with its catwalk and beacon stands 3 m over
    ///     its cable, and 2 m to spare), for which its towers may be taller (HMaxOver). It never passes over
    ///     another line's station, nor crosses one within CrossFromStation of a portal, and none of its towers
    ///     or stations stands in another line's swath or in the lane to another line's launch ramp.
    ///   - Its own launch ramp (LiftRamp.cs) goes only on a span that crosses no other line, and is given up if
    ///     the lane to it would run into another line's tower.
    /// Like the first lines it is a pure function of the day's mountain. The first lines are laid exactly as they
    /// were before there were second ones. Tools/LiftHarness checks every pair of lines that cross.
    /// </summary>
    public static partial class LiftLine
    {
        public const bool Network = true;
        public const float HMaxOver = 32f;            // a tower of a line that is carried over another
        public const float OverTop = 3f;              // what stands above a line's cable: a tower's head, to its beacon
        public const float OverClear = 2f;            // and the room left between that and the chairs passing over
        public const float OverSide = 3.7f;           // either side of the lower line's cables: two chairs' half widths and their swing
        public const float CrossFromStation = 22f;    // no crossing nearer than this to the lower line's portals
        // (short, and from well out on one side to well out on the other: so they cross at CrowdAngle or more)
        static readonly float[] SecondLengths = { 170f, 150f, 200f, 130f, 230f, 260f };
        static readonly Vector2[] SecondOffsets =
        {
            new Vector2(76f, 76f), new Vector2(90f, 76f), new Vector2(76f, 90f), new Vector2(90f, 90f), new Vector2(104f, 90f),
            new Vector2(90f, 104f), new Vector2(104f, 104f), new Vector2(118f, 104f), new Vector2(104f, 118f), new Vector2(118f, 118f),
            new Vector2(62f, 76f), new Vector2(76f, 62f),
        };
        // No two lifts side by side: lines within CrowdAngle of each other never come within CrowdNear of each other
        // for CrowdRun or more; no two stations within StationApart (Crowds)
        public const float CrowdAngle = 45f, CrowdNear = 120f, CrowdRun = 40f, StationApart = 60f;

        /// <summary>Lay cycle k's second line, across the piste and over the lines already laid. Null if none will go.</summary>
        static Lift LaySecond(int k, IList<Lift> laid)
        {
            if (!Range(k, out float zt, out float zb)) return null;
            Lift first = null;
            foreach (var o in laid) if (o.k == k && !o.second) first = o;
            if (first == null) return null;
            float b = Course.First + k * Course.Cycle;
            float danger = k < LastCycle ? b + Course.DangerAt - 10f : float.MaxValue;   // (the last cycle is cut short by the finish)
            float lo = zt + 10f, hi = zb - 10f;
            float flip = Slope.Hash01(k, 3, 171) < 0.5f ? -1f : 1f;
            float safe = first.offBottom > 0f ? 1f : -1f;   // the side the first line keeps to through the danger stretch: away from its cliff
            int tries = 0;
            foreach (float len in SecondLengths)
            {
                if (hi - lo < len) continue;
                // where it starts, every 40 m of the room there is, from a place drawn from the day's mountain
                int n = Mathf.FloorToInt((hi - lo - len) / 40f) + 1;
                int from = Mathf.Min(n - 1, (int)(Slope.Hash01(k, 4, 171) * n));
                for (int j = 0; j < n; j++)
                {
                    float z0 = lo + ((from + j) % n) * 40f;
                    for (int e = 0; e < 2; e++)
                    {
                        float st = e == 0 ? flip : -flip;
                        // one that runs down into the danger stretch ends on the safe side of it, as the first line does
                        if (z0 + len > danger && -st != safe) continue;
                        foreach (var pr in SecondOffsets)
                        {
                            tries++;
                            var l = Try(k, z0, z0 + len, st * pr.x, -st * pr.y, laid);
                            if (l == null) continue;
                            if (z0 + len > danger && !ClearOfDanger(l, danger)) continue;
                            l.tries = tries;
                            PlaceRamp(l, laid);
                            return l;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// A line that ends in the danger stretch: it is over the piste only above that stretch (never over the
        /// crevasses), and no part of it is over the foot of the deadly cliff.
        /// </summary>
        static bool ClearOfDanger(Lift l, float danger)
        {
            for (float s = 0f; s <= l.plan; s += 3f)
                for (int side = -1; side <= 1; side++)
                {
                    Vector3 p = l.At(s, side * Half, 0f);
                    if (Deadly.Below(p.x, p.z)) return false;
                    if (p.z > danger && Mathf.Abs(p.x - Slope.Centre(p.z)) < Slope.PisteHalf + 6f) return false;
                }
            return true;
        }

        /// <summary>
        /// Can this line (its stations set, nothing else yet) live with the lines already laid? Finds the ones it
        /// crosses (l.under), which its towers must then carry it over.
        /// </summary>
        static bool Meets(Lift l, IList<Lift> laid)
        {
            if (Crowds(l, laid)) return false;
            // its stations: clear of every other line's swath and stations, and of the lanes to their ramps
            for (int end = 0; end < 2; end++)
            {
                Vector3 st = end == 0 ? l.top : l.bottom;
                for (int i = -2; i <= 2; i++)
                    for (int a = 0; a < StationAcross.Length; a++)
                        if (TakenBy(laid, st.x + l.dir.x * i * StationHalfLen * 0.5f + l.across.x * StationAcross[a],
                                          st.z + l.dir.z * i * StationHalfLen * 0.5f + l.across.z * StationAcross[a], 8f)) return false;
            }
            List<Lift> over = null;
            for (int q = 0; q < laid.Count; q++)
            {
                var o = laid[q];
                if (o.ZMax < l.ZMin - 60f || o.ZMin > l.ZMax + 60f) continue;
                // it passes over no station
                for (int end = 0; end < 2; end++)
                {
                    Vector3 st = end == 0 ? o.top : o.bottom;
                    float along = (st.x - l.top.x) * l.dir.x + (st.z - l.top.z) * l.dir.z, side = (st.x - l.top.x) * l.across.x + (st.z - l.top.z) * l.across.z;
                    if (along > -Swath && along < l.plan + Swath && Mathf.Abs(side) < Swath + StationHalfWid + 2f) return false;
                }
                float det = l.dir.x * o.dir.z - l.dir.z * o.dir.x;   // the sine of the angle between the two lines
                // (near enough parallel: Crowds has kept them well apart)
                if (Mathf.Abs(det) < 0.26f) continue;
                float dx = o.top.x - l.top.x, dz = o.top.z - l.top.z;
                float sAt = (dx * o.dir.z - dz * o.dir.x) / det, tAt = (dx * l.dir.z - dz * l.dir.x) / det;   // where they meet, along each
                float reach = 2f * Swath / Mathf.Abs(det);            // how far either side of that their swaths overlap
                if (sAt < -reach || sAt > l.plan + reach || tAt < -reach || tAt > o.plan + reach) continue;   // they do not meet
                if (tAt < StationHalfLen + CrossFromStation || tAt > o.plan - StationHalfLen - CrossFromStation) return false;
                if (sAt < StationHalfLen + 30f || sAt > l.plan - StationHalfLen - 30f) return false;         // (no room to climb over it)
                (over ?? (over = new List<Lift>())).Add(o);
            }
            l.under = over != null ? over.ToArray() : NoLifts;
            return true;
        }

        /// <summary>
        /// Would this line stand beside one already laid? Two lines within CrowdAngle of each other that come within
        /// CrowdNear of each other for CrowdRun or more run side by side, which no resort builds; and no station
        /// stands within StationApart of another line's. (Lines that cross more squarely than that are carried
        /// over one another: Meets.)
        /// </summary>
        public static bool Crowds(Lift l, IList<Lift> laid)
        {
            float cosMax = Mathf.Cos(CrowdAngle * Mathf.Deg2Rad);
            for (int q = 0; q < laid.Count; q++)
            {
                var o = laid[q];
                if (o == l) continue;
                for (int e = 0; e < 2; e++)
                    for (int f = 0; f < 2; f++)
                    {
                        Vector3 a = e == 0 ? l.top : l.bottom, b = f == 0 ? o.top : o.bottom;
                        if ((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z) < StationApart * StationApart) return true;
                    }
                if (o.ZMax < l.ZMin - CrowdNear || o.ZMin > l.ZMax + CrowdNear) continue;
                if (Mathf.Abs(l.dir.x * o.dir.x + l.dir.z * o.dir.z) < cosMax) continue;
                if (Alongside(l, o) >= CrowdRun || Alongside(o, l) >= CrowdRun) return true;
            }
            return false;
        }

        /// <summary>
        /// How many metres of a run beside b: alongside it (between b's stations, looking square across from b) and
        /// within CrowdNear of it. (Two lines end to end are not side by side: their stations are kept apart.)
        /// </summary>
        public static float Alongside(Lift a, Lift b)
        {
            float run = 0f;
            // (only the part of a that is abreast of b: where a's stations project onto b's line, clipped to b)
            float ta = (a.top.x - b.top.x) * b.dir.x + (a.top.z - b.top.z) * b.dir.z, tb = (a.bottom.x - b.top.x) * b.dir.x + (a.bottom.z - b.top.z) * b.dir.z;
            if (Mathf.Max(ta, tb) < 0f || Mathf.Min(ta, tb) > b.plan) return 0f;
            for (float s = 0f; s <= a.plan; s += 4f)
            {
                Vector3 p = a.At(s, 0f, 0f);
                float t = (p.x - b.top.x) * b.dir.x + (p.z - b.top.z) * b.dir.z;
                if (t < 0f || t > b.plan) continue;
                float dx = p.x - (b.top.x + b.dir.x * t), dz = p.z - (b.top.z + b.dir.z * t);
                if (dx * dx + dz * dz < CrowdNear * CrowdNear) run += 4f;
            }
            return run;
        }

        /// <summary>Is (x, z) within `half` of line o's centre, between its bullwheels?</summary>
        public static bool InSwath(Lift o, float x, float z, float half)
        {
            float along = (x - o.top.x) * o.dir.x + (z - o.top.z) * o.dir.z;
            if (along < -WheelBack - 3f || along > o.plan + WheelBack + 3f) return false;
            return Mathf.Abs((x - o.top.x) * o.across.x + (z - o.top.z) * o.across.z) < half;
        }

        /// <summary>
        /// The height that the lines under l put in its chairs' way at (x, z): the lower line's cable there and
        /// what stands over it. float.MinValue where there is no line underneath.
        /// </summary>
        public static float Roof(Lift l, float x, float z)
        {
            float roof = float.MinValue;
            for (int q = 0; q < l.under.Length; q++)
            {
                var o = l.under[q];
                if (!InSwath(o, x, z, Half + OverSide)) continue;
                float along = Mathf.Clamp((x - o.top.x) * o.dir.x + (z - o.top.z) * o.dir.z, o.nodeS[0], o.nodeS[o.Spans]);
                roof = Mathf.Max(roof, o.CableY(SpanAt(o, along), along) + OverTop);
            }
            return roof;
        }

        /// <summary>Does span i of l cross another line (over it or under it)? No launch ramp goes there.</summary>
        static bool Tangled(Lift l, int i, IList<Lift> laid)
        {
            if (laid == null) return false;
            for (int q = 0; q < laid.Count; q++)
            {
                var o = laid[q];
                if (o == l) continue;
                for (float s = l.nodeS[i]; s <= l.nodeS[i + 1]; s += 5f)
                    if (InSwath(o, l.top.x + l.dir.x * s, l.top.z + l.dir.z * s, Swath + Half + 2f)) return true;
            }
            return false;
        }

        /// <summary>Would this ramp stand in another line's swath, or in the lane to its ramp?</summary>
        static bool RampInTheWay(Lift l, Ramp r, IList<Lift> laid)
        {
            for (int k = 0; k <= 4; k++)
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 p = l.At(r.s0 + r.length * 0.25f * k, r.side * Half + e * CableTune.RampHalfWidth, 0f);
                    if (TakenBy(laid, p.x, p.z, 4f)) return true;
                }
            return false;
        }

        /// <summary>Does the lane to l's ramp run into another line's tower or station?</summary>
        static bool LaneBlocked(Lift l, IList<Lift> laid)
        {
            for (int q = 0; q < laid.Count; q++)
            {
                var o = laid[q];
                if (o == l) continue;
                for (int i = 0; i < o.foot.Length; i++) if (InLane(l, o.foot[i].x, o.foot[i].z, 3f)) return true;
                if (InLane(l, o.top.x, o.top.z, StationHalfLen + 3f) || InLane(l, o.bottom.x, o.bottom.z, StationHalfLen + 3f)) return true;
            }
            return false;
        }
    }
}
