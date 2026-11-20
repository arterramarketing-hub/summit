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
    ///   - It is short (160 to 380 m down the mountain) and goes from 62 to 104 m out on one side of the piste to as
    ///     far out on the other, so it runs at 20 to 70 degrees to the first line and crosses the piste on one span.
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
        static readonly float[] SecondLengths = { 280f, 230f, 330f, 190f, 380f, 160f };
        static readonly Vector2[] SecondOffsets =
        {
            new Vector2(62f, 62f), new Vector2(76f, 62f), new Vector2(62f, 76f), new Vector2(76f, 76f), new Vector2(90f, 76f),
            new Vector2(76f, 90f), new Vector2(90f, 90f), new Vector2(104f, 90f), new Vector2(90f, 104f),
        };

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
            // its stations: clear of every other line's swath and stations, and of the lanes to their ramps
            for (int end = 0; end < 2; end++)
            {
                Vector3 st = end == 0 ? l.top : l.bottom;
                for (int i = -2; i <= 2; i++)
                    for (int a = 0; a < StationAcross.Length; a++)
                        if (TakenBy(laid, st.x + l.dir.x * i * StationHalfLen * 0.5f + l.across.x * StationAcross[a],
                                          st.z + l.dir.z * i * StationHalfLen * 0.5f + l.across.z * StationAcross[a], 8f)) return false;
            }
            // nor over a jump station's run in or landing field: nothing hangs in the way of that flight
            if (OverJump(l, laid)) return false;
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
                if (Mathf.Abs(det) < 0.26f)
                {
                    // near enough parallel (within 15 degrees): then they must simply keep apart
                    for (float s = 0f; s <= l.plan; s += 8f)
                        if (InSwath(o, l.top.x + l.dir.x * s, l.top.z + l.dir.z * s, Swath)) return false;
                    continue;
                }
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
