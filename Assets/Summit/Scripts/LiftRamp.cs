using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The launch ramps under the lift lines: what puts a rider within reach of a cable to grind.
    ///
    /// The arithmetic that makes them necessary (Rider.cs's own numbers, G = 22): the mountain falls away at 36
    /// degrees, so what counts is the speed a jump has across the slope's own line. A pop off the snow has 8 m/s of
    /// it at most and tops out 2.2 m over the snow; a Super Jump, twice that speed, about 8 m; a kicker's lip at 30
    /// m/s, 4.7 m (13 m with a pop timed on the lip). The cable is never less than 9 m over the snow off the piste,
    /// or 13 m over it. So without help a cable is out of reach except off a kicker with a perfect pop, or off a
    /// jump cliff's level lip, where the line happens to pass over one.
    ///
    /// So each lift gets one ramp, where one will go: a steel kicker 13 m long standing under one of the two
    /// cables, on the lift's cleared swath, at least 3 m outside the groomed piste, with a lane cleared of trees
    /// from the piste's edge down to it and a sign at the top of that lane. It is boarded like a park box (ride
    /// into its foot, lined up) and throws the rider up the cable's own line. Its lip is as low as will do: for
    /// each spot the flight off it is worked out at a range of speeds with the game's own air physics, and the
    /// spot and height are chosen so the cable is reached at 24 m/s (86 km/h) if any will, never needing more than
    /// 30. Like the lifts, where a ramp stands is a pure function of the day's mountain.
    /// </summary>
    public static partial class LiftLine
    {
        /// <summary>A launch ramp under a lift's cable.</summary>
        public class Ramp
        {
            public int span;                 // the span it stands under (between cable nodes span and span + 1)
            public float side;               // which cable: +1 the one going up, -1 the one coming down
            public float s0, length, height; // where it starts along the line, how long it is in plan, its lip over the snow
            public float y0, y1;             // the snow at its foot and under its lip
            public float needSpeed;          // the least speed into it that reaches the cable (m/s)
            public int catchSpan;            // where that flight meets the cable
            public float catchAlong;
            public Vector3 laneFrom, sign;   // the top of the cleared lane at the piste's edge, and where the sign stands
        }

        /// <summary>The top of a ramp, t metres (in plan) down it from its foot: straight underneath, curving up to the lip.</summary>
        public static Vector3 RampPoint(Lift l, Ramp r, float t)
        {
            float u = Mathf.Clamp01(t / r.length);
            return l.At(r.s0 + u * r.length, r.side * Half, Mathf.Lerp(r.y0, r.y1, u) + r.height * u * u);
        }

        /// <summary>Which way a ramp's top runs at t (unit, down the line and, toward the lip, up).</summary>
        public static Vector3 RampDir(Lift l, Ramp r, float t)
        {
            float u = Mathf.Clamp01(t / r.length);
            float rise = (r.y1 - r.y0 + 2f * r.height * u) / r.length;   // per metre of plan
            return (l.dir + Vector3.up * rise).normalized;
        }

        /// <summary>How a board on a ramp or a cable gains or loses speed (m/s^2), running along a unit direction.</summary>
        public static float SlideAccel(float speed, float dirY) => CableTune.G * -dirY - CableTune.Friction * CableTune.G - CableTune.Drag * speed * speed;

        /// <summary>The speed a rider leaves a ramp's lip with, having ridden into its foot at v0.</summary>
        public static float RampLipSpeed(Lift l, Ramp r, float v0)
        {
            const float dt = 0.02f;
            float t = 0f, v = v0;
            for (int i = 0; i < 200 && t < r.length; i++)
            {
                Vector3 d = RampDir(l, r, t);
                v = Mathf.Max(CableTune.MinSpeed, v + SlideAccel(v, d.y) * dt);
                t += v * Mathf.Sqrt(Mathf.Max(0f, 1f - d.y * d.y)) * dt;
            }
            return v;
        }

        /// <summary>The span of a lift's cable that is over `along` (the last one if it is past the end).</summary>
        public static int SpanAt(Lift l, float along)
        {
            int i = 0;
            while (i < l.Spans - 1 && along > l.nodeS[i + 1]) i++;
            return i;
        }

        /// <summary>Can this span be boarded at `along`? Not the last span (it runs into the bottom station), nor close to either tower.</summary>
        public static bool Boardable(Lift l, int span, float along) =>
            span >= 0 && span < l.Spans - 1 && along > l.nodeS[span] + CableTune.ClearTower && along < l.nodeS[span + 1] - CableTune.ClearNext;

        /// <summary>
        /// Is a board whose underside is at height `bottom`, `along` the line under a cable and moving at (vPlan
        /// down the line, vy up), where CableGrind would put it on the cable?
        /// </summary>
        public static bool InCatch(Lift l, int span, float along, float bottom, float vPlan, float vy)
        {
            if (!Boardable(l, span, along)) return false;
            float cy = l.CableY(span, along);
            if (bottom < cy - CableTune.CatchBelow || bottom > cy + CableTune.CatchAbove) return false;
            return vy - CableSlope(l, span, along) * vPlan <= CableTune.CatchMaxRise;
        }

        /// <summary>The cable's rise per metre of plan down the line at `along` (negative: it runs downhill).</summary>
        public static float CableSlope(Lift l, int span, float along)
        {
            float len = l.nodeS[span + 1] - l.nodeS[span], u = (along - l.nodeS[span]) / len;
            return (l.nodeY[span + 1] - l.nodeY[span] - 4f * Sag(len) * (1f - 2f * u)) / len;
        }

        /// <summary>
        /// Does a rider who rides into this ramp at v0 and does nothing else reach the cable over it? The flight is
        /// stepped as Rider.AirStep steps it (gravity, then its air drag), straight down the line.
        /// </summary>
        public static bool Reaches(Lift l, Ramp r, float v0, out int span, out float along)
        {
            const float dt = 0.02f;
            float v = RampLipSpeed(l, r, v0);
            Vector3 d = RampDir(l, r, r.length);
            float plan = Mathf.Sqrt(Mathf.Max(0f, 1f - d.y * d.y));
            float vp = v * plan, vy = v * d.y;
            float y = RampPoint(l, r, r.length).y + CableTune.BoardR;
            along = r.s0 + r.length; span = r.span;
            for (int i = 0; i < 220; i++)
            {
                vy -= CableTune.G * dt;
                float k = 1f - Mathf.Min(1f, 0.0025f * Mathf.Sqrt(vp * vp + vy * vy) * dt);
                vp *= k; vy *= k;
                along += vp * dt; y += vy * dt;
                span = SpanAt(l, along);
                if (along > l.nodeS[l.Spans]) return false;
                if (InCatch(l, span, along, y - CableTune.BoardR, vp, vy)) return true;
                if (y < l.CableY(span, along) - 14f) return false;   // well under it and falling: it has gone by
            }
            return false;
        }

        /// <summary>The least speed into the ramp that reaches the cable, in steps of 2 m/s from 18 (99 if none up to RampMaxSpeed does, or a faster one misses).</summary>
        static float NeedSpeed(Lift l, Ramp r)
        {
            for (float v = 18f; v <= CableTune.RampMaxSpeed + 0.01f; v += 2f)
            {
                if (!Reaches(l, r, v, out int span, out float along)) continue;
                // (and every speed above it must reach too: a ramp that throws a fast rider clean over the span, or
                // onto the next tower, is no use)
                for (float w = v + 2f; w <= CableTune.RampTopSpeed + 0.01f; w += 2f)
                    if (!Reaches(l, r, w, out _, out _)) return 99f;
                r.catchSpan = span; r.catchAlong = along;
                return v;
            }
            return 99f;
        }

        /// <summary>Find a lift its ramp: the spot and the lip height from which the cable is reached at the least speed.</summary>
        static void PlaceRamp(Lift l, IList<Lift> laid = null, bool anywhere = false)
        {
            Ramp best = null; float bestScore = float.MaxValue;
            for (int i = 0; i < l.Spans - 1; i++)
            {
                if (Tangled(l, i, laid)) continue;   // (not where this line and another cross)
                if (l.gorge >= 0 && i >= l.gorge && !anywhere) continue;   // (a gorge line's: before the gorge, so the grind leads over it)
                float sa = l.nodeS[i] + (i == 0 ? 25f : CableTune.RampFromTower), sb = l.nodeS[i + 1] - 25f - CableTune.RampLength;
                for (float s0 = sa; s0 <= sb; s0 += 6f)
                {
                    // under whichever cable is nearer the piste here
                    Vector3 mid = l.At(s0 + CableTune.RampLength * 0.5f, 0f, 0f);
                    float side = (mid.x - Slope.Centre(mid.z)) * l.across.x > 0f ? -1f : 1f;
                    var r = new Ramp { span = i, side = side, s0 = s0, length = CableTune.RampLength };
                    if (!RampGround(l, r, out float off)) continue;
                    if (laid != null && RampInTheWay(l, r, laid)) continue;
                    // no taller than leaves a rider's height under the chairs passing over its lip
                    float room = l.CableY(i, s0 + r.length) - ChairDrop - r.y1 - CableTune.RampHeadroom;
                    foreach (float h in CableTune.RampHeights)
                    {
                        if (h > room) break;
                        r.height = h;
                        r.needSpeed = NeedSpeed(l, r);
                        if (r.needSpeed > CableTune.RampMaxSpeed) continue;
                        // (and the flight must come down on the cable with a fair slide still ahead of it)
                        float slide = l.nodeS[r.catchSpan + 1] - r.catchAlong;
                        if (slide < CableTune.RampMinSlide) continue;
                        if (r.catchSpan != i && Tangled(l, r.catchSpan, laid)) continue;
                        if (l.gorge >= 0 && !anywhere && r.catchSpan >= l.gorge) continue;
                        // the least speed first (any that 24 m/s reaches is as good as another), then the longest slide
                        // and the lowest lip, then the nearest the piste
                        float score = Mathf.Max(r.needSpeed, CableTune.RampWantSpeed) * 100f + h * 10f + off * 0.1f - Mathf.Min(slide, 60f) * 1.5f;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = new Ramp { span = i, side = side, s0 = s0, length = r.length, height = h, y0 = r.y0, y1 = r.y1,
                                              needSpeed = r.needSpeed, catchSpan = r.catchSpan, catchAlong = r.catchAlong };
                        }
                        if (r.needSpeed <= CableTune.RampWantSpeed) break;   // (a taller lip here would only be taller)
                    }
                }
            }
            if (best == null) return;
            // the lane down to it from the piste's edge, and the sign at the top of the lane
            Vector3 foot = RampPoint(l, best, 0f);
            float zl = foot.z - CableTune.RampLaneLength;
            float sgn = foot.x > Slope.Centre(foot.z) ? 1f : -1f;
            float xl = Slope.Centre(zl) + sgn * Slope.PisteHalf;
            best.laneFrom = new Vector3(xl, Slope.Height(xl, zl), zl);
            float xs = Slope.Centre(zl) + sgn * (Slope.PisteHalf + 2.5f);
            best.sign = new Vector3(xs, Slope.Height(xs, zl), zl);
            l.ramp = best;
            // (a second line's lane must not run into another line's tower or station: then it has no ramp)
            if (laid != null && LaneBlocked(l, laid)) l.ramp = null;
        }

        /// <summary>
        /// Will the ground take a ramp here? Off the piste, off kept ground, clear of anything else built, and
        /// the snow under it nowhere standing up through its underside. Fills in the snow's height at each end.
        /// </summary>
        static bool RampGround(Lift l, Ramp r, out float off)
        {
            off = 0f;
            for (int k = 0; k <= 4; k++)
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 p = l.At(r.s0 + r.length * 0.25f * k, r.side * Half + e * CableTune.RampHalfWidth, 0f);
                    float a = Mathf.Abs(p.x - Slope.Centre(p.z));
                    if (a < Slope.PisteHalf + CableTune.RampOffPiste) return false;
                    if (Kept(p.x, p.z) || (Occupied != null && Occupied(p.x, p.z))) return false;
                    off = Mathf.Max(off, a);
                }
            // Its underside is straight, from the snow at its foot to under its lip, and nowhere may the snow stand
            // up through it: so the far end is set as high as the highest snow along it asks (a rise under the
            // ramp lifts the whole lip a little; too much of one, and the ramp goes somewhere else)
            r.y0 = float.MinValue;
            for (int e = -1; e <= 1; e++)
            {
                Vector3 a0 = l.At(r.s0, r.side * Half + e * CableTune.RampHalfWidth, 0f);
                r.y0 = Mathf.Max(r.y0, Slope.Height(a0.x, a0.z));
            }
            float y1 = float.MinValue, under = 0f;
            for (int k = 1; k <= 4; k++)
                for (int e = -1; e <= 1; e++)
                {
                    Vector3 p = l.At(r.s0 + r.length * k / 4f, r.side * Half + e * CableTune.RampHalfWidth, 0f);
                    float g = Slope.Height(p.x, p.z);
                    y1 = Mathf.Max(y1, r.y0 + (g - r.y0) * 4f / k);
                    if (k == 4 && e == 0) under = g;
                }
            r.y1 = y1;
            return y1 - under <= 1.2f && y1 < r.y0 - 2f;
        }

        /// <summary>The lane cleared of trees from the piste's edge down to a lift's ramp.</summary>
        static bool InLane(Lift l, float x, float z, float margin)
        {
            var r = l.ramp;
            Vector3 b = RampPoint(l, r, 0f), a = r.laneFrom;
            if (z < a.z - CableTune.RampLane - margin || z > b.z + margin) return false;
            float dx = b.x - a.x, dz = b.z - a.z;
            float t = Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / (dx * dx + dz * dz));
            float ex = x - (a.x + dx * t), ez = z - (a.z + dz * t);
            float w = CableTune.RampLane + margin;
            return ex * ex + ez * ez < w * w;
        }

        /// <summary>A line of text for each ramp (SkiLift logs it with the lifts when SUMMIT_LIFTCHECK=1).</summary>
        public static string RampReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in Lifts)
            {
                var r = l.ramp;
                if (r == null) { sb.AppendLine($"lift {l.k}{(l.second ? "b" : "")}: no launch ramp (no spot reaches the cable at {CableTune.RampMaxSpeed:0} m/s or less)"); continue; }
                Vector3 foot = RampPoint(l, r, 0f), lip = RampPoint(l, r, r.length);
                Reaches(l, r, r.needSpeed, out int cs, out float ca);
                sb.AppendLine($"lift {l.k}{(l.second ? "b" : "")}: launch ramp under the {(r.side > 0f ? "up" : "down")}-going cable at x={foot.x:0} z={foot.z:0} ({Mathf.Abs(foot.x - Slope.Centre(foot.z)):0} m from the piste's centre, {Course.At(foot.z)}), lip {r.height:0.00} m; the cable is {l.CableY(r.span, r.s0 + r.length) - lip.y:0.0} m over the lip; reached from {r.needSpeed:0} m/s ({r.needSpeed * 3.6f:0} km/h) with no pop, meeting the cable {ca - r.s0 - r.length:0} m past the lip on span {cs}; sign at the piste's edge at z={r.sign.z:0}");
            }
            return sb.ToString();
        }
    }
}
