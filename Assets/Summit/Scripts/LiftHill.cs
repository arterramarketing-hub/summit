using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The hills under the lift lines: what puts a rider within reach of a cable to grind. No ramp, nothing built:
    /// the mountain itself rises under the line in a smooth hump of snow, and a rider who rides over its crest along
    /// the line is thrown up to the cable as off any roll-over.
    ///
    /// Why one is needed (Rider.cs's own numbers, G = 22): the mountain falls away at 36 degrees, so what counts is
    /// a jump's speed across the slope's own line. A pop off the snow has 8 m/s of it at most and tops out 2.2 m over
    /// the snow; a kicker's lip at 30 m/s, 4.7 m. The cable is never less than 9 m over the snow off the piste. Off a
    /// crest that leaves the board level, though, the snow drops away at 36 degrees under a rider still going level:
    /// at 20-30 m/s the slope is 10 m below within a second, and the cable is reached on the way.
    ///
    /// The shape (Profile, Across): seen along the line, the slope bulges up over its uphill face (a curve that
    /// starts flush with the slope and steepens), rounds over a crest, and falls back to the slope's own line below
    /// it; across the line it is flat over the middle and blends out on both sides. Every piece meets the next with
    /// the same slope, so there is no edge or seam anywhere, and it is part of the height function (Slope.Height:
    /// HillHeight), so the terrain, the rider, the snow layer, the trees and the avalanche all see it alike, and it
    /// has no collider of its own.
    ///
    /// Where: like the ramps before them, for each lift the spot, height and shape are worked out from the game's own
    /// physics (a rider on the snow along the line, taking off where the snow falls away faster than gravity can
    /// follow, on the drawn snow's facets, then its flight with the air's drag; calibrated against the game's rider,
    /// LiftHillCheck). A hill is made only where the speed most riders bring (HillTypical, 22 m/s) reaches the cable,
    /// as does the least speed that will and most speeds from there to HillTopSpeed; the best is the one the slowest
    /// rider reaches. Best of all is a low hill where the cable sags lowest over the slope: its crest a rider's
    /// height under the chairs. With a rider's height to spare under the chairs everywhere over it; its raised part off
    /// the piste (its gentle run-up and faint edges may come onto the piste's edge); clear of the towers, the stations,
    /// the other lines, the hamlets and everything the course keeps clear (Kept). A lane is cleared of trees from the
    /// piste's edge down to it, with a jump sign at its top. The lifts themselves (towers, cables) are laid on the
    /// mountain without hills, and the hills never reach a tower's foot, so they stand exactly where they did.
    /// </summary>
    public static partial class LiftLine
    {
        /// <summary>A hill under a lift's cable.</summary>
        public class Hill
        {
            public int span;                 // the span it stands under
            public float side;               // which cable: +1 the one going up, -1 the one coming down
            public float s;                  // its crest, metres along the line from the top station
            public float height, rise, fall, lip;   // how high it stands over the slope; its uphill face and its fall below the crest (m, in plan); the slope its face reaches at the crest
            public float blend;              // m either side of its full-height middle over which it eases into the slope
            public float facet;              // how far apart the drawn snow's facet edges are along its line (Slope.Quad, more on a slant)
            public float needSpeed;          // the least speed into it that reaches the cable (m/s)
            public float share;              // of the speeds from that up to HillTopSpeed, how many reach it
            public int catchSpan;            // where that flight meets the cable
            public float catchAlong;
            public Vector3 laneFrom, sign;   // the top of the cleared lane at the piste's edge, and where the sign stands
            public Vector3 crest, dir, across;   // the crest on its middle line (y unused), and the line's level directions
            public float x0, x1, z0, z1;     // its footprint's bounds in plan
            public float Reach => CableTune.HillTop + blend;   // half its width
        }

        // ---------------------------------------------------------------- its shape

        /// <summary>
        /// How far the hill stands over the slope u metres down the line from its lip (negative: up the hill of it): from
        /// flush with the slope at -rise, a curve up its face (steepening: a cubic) to `height` at the lip, where it rises
        /// `lip` per metre more than the slope; then rounding over a short crest and down to the slope's own line again
        /// at `fall` (a cubic too, flat at its end). The pieces meet with the same slope, so there is no edge anywhere.
        /// </summary>
        public static float Profile(float u, float height, float rise, float fall, float lip)
        {
            if (u <= -rise || u >= fall) return 0f;
            if (u < 0f)
            {
                float t = (u + rise) / rise, m = lip * rise;
                return (3f * height - m) * t * t + (m - 2f * height) * t * t * t;
            }
            float f = u / fall, m0 = lip * fall;
            return height * (2f * f * f * f - 3f * f * f + 1f) + m0 * (f * f * f - 2f * f * f + f);
        }

        /// <summary>Its highest point over the slope (on the crest, just past the lip).</summary>
        public static float Peak(float height, float lip)
        {
            float fall = Fall(height, lip), top = height;
            for (float u = 0f; u < fall; u += 0.5f) top = Mathf.Max(top, Profile(u, height, 1f, fall, lip));
            return top;
        }

        /// <summary>How much of the hill there is w metres across from its middle line: all of it over the top, easing to none over `blend`.</summary>
        public static float Across(float w, float blend)
        {
            float a = Mathf.Abs(w) - CableTune.HillTop;
            if (a <= 0f) return 1f;
            if (a >= blend) return 0f;
            float t = 1f - a / blend;
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>Its flanks ease out over more ground the taller it is, so they are never steeper than about 45 degrees.</summary>
        static float Blend(float height) => Mathf.Min(CableTune.HillBlendMax, CableTune.HillBlendMin + CableTune.HillBlendK * height);

        static float Rise(float height, float lip) => Mathf.Max(CableTune.HillRiseMin, CableTune.HillRiseK * height / lip);
        static float Fall(float height, float lip) => Mathf.Max(CableTune.HillFallMin, 3f * height);

        // the day's hills, for the height function: set once the lifts are laid (none while they are being laid, so the
        // lifts are laid on the mountain without them)
        static Hill[] hills = new Hill[0];
        static int hillsFor = int.MinValue;

        /// <summary>
        /// The hills' height over the slope at (x, z): Slope.Height adds it. 0 until today's lifts are laid (whoever asks
        /// for them first lays them; the terrain's streamer asks before it builds anything). Reads only: any thread.
        /// </summary>
        public static float HillHeight(float x, float z)
        {
            var hs = hills;
            // (the crevasses are laid on the snow without them, whenever that is: Glacier.Build reads it at the piste's edges)
            if (hs.Length == 0 || hillsFor != Slope.Seed || Course.Laying) return 0f;
            float y = 0f;
            for (int i = 0; i < hs.Length; i++)
            {
                var h = hs[i];
                if (x < h.x0 || x > h.x1 || z < h.z0 || z > h.z1) continue;
                float dx = x - h.crest.x, dz = z - h.crest.z;
                float u = dx * h.dir.x + dz * h.dir.z, w = dx * h.across.x + dz * h.across.z;
                y += Profile(u, h.height, h.rise, h.fall, h.lip) * Across(w, h.blend);
            }
            return y;
        }

        /// <summary>Is (x, z) on a hill (anywhere its snow stands over the slope)?</summary>
        public static bool OnHill(float x, float z, float margin = 0f)
        {
            var hs = hills;
            if (hillsFor != Slope.Seed) return false;
            for (int i = 0; i < hs.Length; i++)
            {
                var h = hs[i];
                if (x < h.x0 - margin || x > h.x1 + margin || z < h.z0 - margin || z > h.z1 + margin) continue;
                float dx = x - h.crest.x, dz = z - h.crest.z;
                float u = dx * h.dir.x + dz * h.dir.z, w = dx * h.across.x + dz * h.across.z;
                if (u > -h.rise - margin && u < h.fall + margin && Mathf.Abs(w) < h.Reach + margin) return true;
            }
            return false;
        }

        /// <summary>How long the day's lifts and hills took to lay (ms).</summary>
        public static double LaidMs;

        /// <summary>The lifts are laid: their hills go into the height function (LiftLine.Lifts calls it).</summary>
        static void PublishHills(Lift[] all)
        {
            var list = new List<Hill>();
            foreach (var l in all) if (l.hill != null) list.Add(l.hill);
            hills = list.ToArray();
            hillsFor = Slope.Seed;
        }

        /// <summary>While lifts are laid, there are no hills (the lifts stand on the mountain without them).</summary>
        static void HideHills() { hills = new Hill[0]; hillsFor = int.MinValue; grounds.Clear(); }

        // ---------------------------------------------------------------- the cable (CableGrind boards it by these)

        /// <summary>How a board on a cable gains or loses speed (m/s^2), running along a unit direction.</summary>
        public static float SlideAccel(float speed, float dirY) => CableTune.G * -dirY - CableTune.Friction * CableTune.G - CableTune.Drag * speed * speed;

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

        // ---------------------------------------------------------------- the ride over it

        /// <summary>A lift's ground along each cable's line, without hills, every Grain metres: what the hills are worked out on.</summary>
        class HillGround
        {
            public const float Grain = 1f;
            public float[] up, down;   // the up-going cable's line (+1) and the down-going one's (-1)
            public float At(float side, float s)
            {
                var a = side > 0f ? up : down;
                float f = Mathf.Clamp(s / Grain, 0f, a.Length - 1.001f);
                int i = (int)f;
                return Mathf.Lerp(a[i], a[i + 1], f - i);
            }
        }

        // (each line's, once while the lifts are laid: the hills are placed, and then settled, on the same)
        static readonly Dictionary<Lift, HillGround> grounds = new Dictionary<Lift, HillGround>();

        static HillGround HillGroundOf(Lift l)
        {
            if (grounds.TryGetValue(l, out var had)) return had;
            int n = Mathf.CeilToInt(l.plan / HillGround.Grain) + 2;
            var g = new HillGround { up = new float[n], down = new float[n] };
            for (int i = 0; i < n; i++)
            {
                Vector3 a = l.At(i * HillGround.Grain, Half, 0f), b = l.At(i * HillGround.Grain, -Half, 0f);
                // (the mountain without its hills, whenever this is asked)
                g.up[i] = Slope.Height(a.x, a.z) - HillHeight(a.x, a.z); g.down[i] = Slope.Height(b.x, b.z) - HillHeight(b.x, b.z);
            }
            grounds[l] = g;
            return g;
        }

        /// <summary>The snow on hill h's middle line (its own cable's), s along the line: the slope and the hill.</summary>
        static float SnowOn(HillGround g, Hill h, float s) => g.At(h.side, s) + Profile(s - h.s, h.height, h.rise, h.fall, h.lip);

        /// <summary>
        /// The same, as the drawn snow has it, which the rider rides: flat facets Slope.Quad long, so the crest's curve is
        /// cut across (a rider leaves it with less upward speed than the true curve would give, a couple of m/s at speed).
        /// </summary>
        static float SnowRidden(HillGround g, Hill h, float s)
        {
            float q = h.facet, f = Mathf.Floor(s / q) * q;
            return Mathf.Lerp(SnowOn(g, h, f), SnowOn(g, h, f + q), (s - f) / q);
        }

        /// <summary>
        /// Ride over hill h at v0, straight down the line under its cable, and say whether the rider is put on the
        /// cable. On the snow as Rider.Tick rides it (gravity along the snow, its friction, the air's drag), leaving it
        /// where Rider does (the snow falling away under the path faster than gravity can follow, and clear of it a
        /// moment on: Rider.FallsAway); then the flight as Rider.AirStep steps it, until the board meets a cable it can
        /// be put on (InCatch), comes down on the snow, or has gone by.
        /// </summary>
        static bool RidesUp(Lift l, HillGround g, Hill h, float v0, out int span, out float along)
        {
            const float dt = 1f / 120f, R = CableTune.BoardR;
            span = h.span; along = h.s;
            float s = h.s - h.rise - 16f;   // (from 16 m up the hill of its foot: v0 is the speed there)
            float sp = v0, groundVy = float.NaN;
            bool flying = false;
            float vp = 0f, vy = 0f, y = 0f;
            for (int i = 0; i < 1200 && !flying; i++)
            {
                if (s > h.s + h.fall + 4f) return false;   // never left the snow
                float y0 = SnowRidden(g, h, s), slope = (SnowRidden(g, h, s + 0.05f) - SnowRidden(g, h, s - 0.05f)) / 0.1f;
                float len = Mathf.Sqrt(1f + slope * slope), dy = slope / len, ny = 1f / len;
                sp += (-CableTune.G * dy - Rider.Friction * CableTune.G * ny - CableTune.Drag * sp * sp) * dt;
                if (sp < 1f) return false;
                vp = sp / len; vy = sp * dy;
                float surfVy = (SnowRidden(g, h, s + vp * dt) - y0) / dt;
                if (!float.IsNaN(groundVy) && sp > 7f && surfVy < groundVy - CableTune.G * dt * 1.2f)
                {
                    float vUp = Mathf.Clamp(groundVy, vy - 2f, vy + 2f);
                    if (FallsAway(g, h, s, y0 + R, vp, vUp))
                    {
                        // (the game's rider comes over the crest a little slower than this model of it, and leaves it rising
                        // a little less: the carving that holds its line and the drawn snow's facets cost it that, at every
                        // speed, measured with LiftHillCheck)
                        flying = true; vp *= CableTune.HillSpeedKeep; vy = vUp - CableTune.HillRiseLoss; y = y0 + R; break;
                    }
                }
                groundVy = surfVy;
                s += vp * dt;
            }
            if (!flying) return false;
            TraceOff = new Vector4(s, vp, vy, y);
            // the flight
            const float fdt = 0.02f;
            along = s;
            for (int i = 0; i < 300; i++)
            {
                vy -= CableTune.G * fdt;
                float k = 1f - Mathf.Min(1f, 0.0025f * Mathf.Sqrt(vp * vp + vy * vy) * fdt);
                vp *= k; vy *= k;
                along += vp * fdt; y += vy * fdt;
                if (along > l.nodeS[l.Spans]) return false;
                span = SpanAt(l, along);
                // (a margin inside the game's own window, as the rider's line is never quite the model's)
                if (Boardable(l, span, along))
                {
                    float cy = l.CableY(span, along), b = y - R;
                    if (b > cy - CableTune.CatchBelow + CableTune.HillCatchMargin && b < cy + CableTune.CatchAbove - CableTune.HillCatchMargin
                        && vy - CableSlope(l, span, along) * vp <= CableTune.CatchMaxRise) return true;
                }
                if (y - R < SnowOn(g, h, along)) return false;   // down on the snow short of it
                if (y < l.CableY(span, along) - 14f && vy < 0f) return false;   // well under it and falling: gone by
            }
            return false;
        }

        /// <summary>For the test tools: ride over a lift's hill at v0 in the model above; where it left the snow (along, plan and vertical speed, height).</summary>
        public static bool TestRide(Lift l, float v0, out Vector4 off, out int span, out float along)
        {
            TraceOff = Vector4.zero;
            bool ok = RidesUp(l, HillGroundOf(l), l.hill, v0, out span, out along);
            off = TraceOff;
            return ok;
        }
        static Vector4 TraceOff;

        // Rider.FallsAway: clear of the snow by TakeoffClearance 0.12 s on, and never under it on the way there
        static bool FallsAway(HillGround g, Hill h, float s, float y, float vp, float vy)
        {
            const float T = 0.12f; const int N = 6;
            for (int i = 1; i <= N; i++)
            {
                float t = T * i / N;
                float by = y + vy * t - 0.5f * CableTune.G * t * t;
                float gy = SnowRidden(g, h, s + vp * t) + CableTune.BoardR;
                if (i < N && by < gy - 0.02f) return false;
                if (i == N) return by - gy > Rider.TakeoffClearance;
            }
            return false;
        }

        /// <summary>
        /// The least speed into it that reaches the cable (it and the next speed up both do), in steps of 2 m/s from 14;
        /// 99 if none up to HillMaxSpeed does, or if fewer than HillShare of the speeds from there up to HillTopSpeed do.
        /// (Not every one: a hill throws a rider up past the cable, and one going a little faster rises through it too
        /// fast to be put on it, and may come down on it again only past the next tower. A ramp's flight is the same.)
        /// Fills in where its flight meets the cable, and the share of speeds that reach.
        /// </summary>
        static float NeedSpeed(Lift l, HillGround g, Hill h)
        {
            int n = Mathf.RoundToInt((CableTune.HillTopSpeed - 14f) / 2f) + 1;
            var hit = new bool[n]; var spans = new int[n]; var alongs = new float[n];
            for (int i = 0; i < n; i++) hit[i] = RidesUp(l, g, h, 14f + 2f * i, out spans[i], out alongs[i]);
            for (int i = 0; i + 1 < n && 14f + 2f * i <= CableTune.HillMaxSpeed + 0.01f; i++)
            {
                if (!hit[i] || !hit[i + 1]) continue;
                int ok = 0;
                for (int j = i; j < n; j++) if (hit[j]) ok++;
                h.share = ok / (float)(n - i);
                if (h.share < CableTune.HillShare) return 99f;
                h.catchSpan = spans[i]; h.catchAlong = alongs[i];
                return 14f + 2f * i;
            }
            return 99f;
        }

        // ---------------------------------------------------------------- where

        /// <summary>
        /// Ground a hill may cover, on a grid along the line (every Cell m) and across it: off the piste, out of
        /// everything kept clear, off the other lines, away from this line's towers and stations.
        /// </summary>
        class Room
        {
            public const float Cell = 3f;
            public Lift l; public IList<Lift> laid;
            public float w0; public int cols, rows;
            public sbyte[] state;   // 0 not looked at yet; else 1 + 2 if nothing is in the way, + 4 if also off the piste's middle, + 8 if off the piste
            /// <summary>Can the hill's snow be here, standing `rise` metres over the slope? (Its faint edges may reach onto the piste's edge.)</summary>
            public bool Free(float s, float w, float rise)
            {
                int r = Mathf.RoundToInt(s / Cell), c = Mathf.RoundToInt((w - w0) / Cell);
                if (r < 0 || r >= rows || c < 0 || c >= cols) return false;
                int i = r * cols + c;
                if (state[i] == 0)
                {
                    Vector3 p = l.At(r * Cell, w0 + c * Cell, 0f);
                    bool open = Open(l, p.x, p.z, r * Cell, laid);
                    float a = Mathf.Abs(p.x - Slope.Centre(p.z));
                    bool edge = a >= Slope.PisteHalf - CableTune.HillRunUpOnPiste, off = a >= Slope.PisteHalf + CableTune.HillOffPiste;
                    state[i] = (sbyte)(1 + (open ? 2 : 0) + (open && edge ? 4 : 0) + (open && off ? 8 : 0));
                }
                // (its faint edges may come furthest onto the piste's edge, its gentle run-up a little way, the hill itself not)
                return (state[i] & (rise > CableTune.HillRunUp ? 8 : rise > CableTune.HillEdge ? 4 : 2)) != 0;
            }
        }

        static Room RoomOf(Lift l, IList<Lift> laid)
        {
            float reach = CableTune.HillTop + Blend(CableTune.HillHeights[CableTune.HillHeights.Length - 1]);
            var room = new Room { l = l, laid = laid, w0 = -Half - reach - Room.Cell, rows = Mathf.CeilToInt(l.plan / Room.Cell) + 1 };
            room.cols = Mathf.CeilToInt(2f * (Half + reach + Room.Cell) / Room.Cell) + 1;
            room.state = new sbyte[room.rows * room.cols];
            return room;
        }

        // why cells were not free, for HillReport: the stations, the piste, kept ground, a hamlet, a tower, another line, its tower, its hill
        static readonly int[] blocked = new int[8];
        static bool No(int why) { blocked[why]++; return false; }

        static bool Open(Lift l, float x, float z, float s, IList<Lift> laid)
        {
            if (s < StationHalfLen + 14f || s > l.plan - StationHalfLen - 14f) return No(0);   // the stations and their decks' run-outs
            float a = Mathf.Abs(x - Slope.Centre(z));
            if (a < Slope.PisteHalf - CableTune.HillEdgeOnPiste) return No(1);   // (its faint edge at most on the piste's edge: Room.Free)
            if (Kept(x, z)) return No(2);
            if (Occupied != null && Occupied(x, z)) return No(3);
            for (int i = 0; i < l.foot.Length; i++)
            {
                float dx = x - l.foot[i].x, dz = z - l.foot[i].z;
                if (dx * dx + dz * dz < CableTune.HillFromTower * CableTune.HillFromTower) return No(4);
            }
            if (laid != null)
                for (int q = 0; q < laid.Count; q++)
                {
                    var o = laid[q];
                    if (o == l) continue;
                    if (InSwath(o, x, z, Swath + 2f)) return No(5);
                    for (int i = 0; i < o.foot.Length; i++)
                    {
                        float dx = x - o.foot[i].x, dz = z - o.foot[i].z;
                        if (dx * dx + dz * dz < CableTune.HillFromTower * CableTune.HillFromTower) return No(6);
                    }
                    if (o.hill != null && Covers(o.hill, x, z, 4f)) return No(7);
                }
            return true;
        }

        static bool Covers(Hill h, float x, float z, float margin)
        {
            float dx = x - h.crest.x, dz = z - h.crest.z;
            float u = dx * h.dir.x + dz * h.dir.z, w = dx * h.across.x + dz * h.across.z;
            return u > -h.rise - margin && u < h.fall + margin && Mathf.Abs(w) < h.Reach + margin;
        }

        /// <summary>Find a lift its hill: the spot, height and shape from which the cable is reached at the least speed.</summary>
        /// <summary>Why the last lift asked about got no hill (for HillReport).</summary>
        static readonly Dictionary<Lift, string> why = new Dictionary<Lift, string>();

        /// <summary>Make hills at all (the test tools switch it off to time or test the lifts without them; SUMMIT_NO_HILLS=1 too).</summary>
        public static bool HillsOn = System.Environment.GetEnvironmentVariable("SUMMIT_NO_HILLS") != "1";

        static void PlaceHill(Lift l, IList<Lift> laid = null, bool anywhere = false)
        {
            if (!HillsOn) { l.hill = null; return; }
            PlaceHillCore(l, laid, anywhere);
        }

        static void PlaceHillCore(Lift l, IList<Lift> laid, bool anywhere)
        {
            l.hill = null;
            System.Array.Clear(blocked, 0, blocked.Length);
            int tried = 0, fit = 0, room0 = 0, fast = 0, slid = 0;
            var g = HillGroundOf(l);
            var room = RoomOf(l, laid);
            Hill best = null; float bestScore = float.MaxValue;
            for (int i = 0; i < l.Spans - 1; i++)
            {
                if (Tangled(l, i, laid)) continue;   // (not where this line and another cross)
                if (l.gorge >= 0 && i >= l.gorge && !anywhere) continue;   // (a gorge line's: before the gorge, so the grind leads over it)
                // under the cable nearer the piste here first, then the other
                Vector3 mid = l.At((l.nodeS[i] + l.nodeS[i + 1]) * 0.5f, 0f, 0f);
                float nearer = (mid.x - Slope.Centre(mid.z)) * l.across.x > 0f ? -1f : 1f;
                for (int pass = 0; pass < 2; pass++)
                {
                float side = pass == 0 ? nearer : -nearer;
                foreach (float height in CableTune.HillHeights)
                {
                    bool found = false;
                    foreach (float lip in CableTune.HillLips)
                    {
                        float rise = Rise(height, lip), fall = Fall(height, lip);
                        if (lip * rise > 2.8f * height) continue;   // (the face would dip before it rose)
                        float sa = l.nodeS[i] + rise, sb = l.nodeS[i + 1] - 25f - fall;
                        for (float sc = sa; sc <= sb; sc += CableTune.HillStep)
                        {
                            var h = new Hill { span = i, side = side, s = sc, height = height, rise = rise, fall = fall, lip = lip, blend = Blend(Peak(height, lip)),
                                               facet = Slope.Quad / Mathf.Max(Mathf.Abs(l.dir.x), Mathf.Abs(l.dir.z)) };
                            tried++;
                            // (a crest too far under the cable is out of reach of the speeds most riders bring: not worth riding)
                            if (l.CableY(i, sc) - (g.At(side, sc) + Peak(height, lip)) > CableTune.HillReachMax) continue;
            if (!Fits(l, g, room, h, out bool roomy)) { if (roomy) room0++; continue; }
                            fit++;
                            // (first the speed most riders bring: a spot that does not reach the cable from it is no use)
                            if (!RidesUp(l, g, h, CableTune.HillTypical, out _, out _)) continue;
                            h.needSpeed = NeedSpeed(l, g, h);
                            if (h.needSpeed > CableTune.HillMaxSpeed) continue;
                            fast++;
                            float slide = l.nodeS[h.catchSpan + 1] - h.catchAlong;
                            if (slide < CableTune.HillMinSlide) continue;
                            slid++;
                            if (h.catchSpan != i && Tangled(l, h.catchSpan, laid)) continue;
                            if (l.gorge >= 0 && !anywhere && h.catchSpan >= l.gorge) continue;
                            // the least speed first (any that 24 m/s reaches is as good as another), then the lowest hill,
                            // then the longest slide
                            // the least speed that reaches first, then the most speeds above it, then the lowest, then the longest slide
                            float score = Mathf.Max(h.needSpeed, CableTune.HillWantSpeed) * 100f - h.share * 300f + height * 5f - Mathf.Min(slide, 60f) * 1.5f;
                            if (score < bestScore) { bestScore = score; best = h; }
                            if (h.needSpeed <= CableTune.HillWantSpeed) found = true;
                        }
                    }
                    if (found) break;   // (a taller hill here would only be taller)
                }
                }
            }
            why[l] = $"{tried} spots tried, {fit + room0} with room, {fit} with headroom under the chairs too, {fast} reaching the cable at {CableTune.HillMaxSpeed:0} m/s or less (and {CableTune.HillShare:P0} of the speeds up to {CableTune.HillTopSpeed:0}), {slid} with a slide left; ground ruled out by the stations/piste/kept/hamlets/towers/other lines/their towers/their hills: {string.Join(" ", blocked)} cells";
            if (best == null) return;
            Frame(l, best);
            // the lane down to it from the piste's edge, and the sign at the top of the lane
            Vector3 foot = l.At(best.s - best.rise, best.side * Half, 0f);
            float zl = foot.z - CableTune.HillLaneLength;
            float sgn = foot.x > Slope.Centre(foot.z) ? 1f : -1f;
            float xl = Slope.Centre(zl) + sgn * Slope.PisteHalf;
            best.laneFrom = new Vector3(xl, Slope.Height(xl, zl), zl);
            float xs = Slope.Centre(zl) + sgn * (Slope.PisteHalf + 2.5f);
            best.sign = new Vector3(xs, Slope.Height(xs, zl), zl);
            l.hill = best;
            // (a lane must not run into another line's tower or station: then it has no hill)
            if (laid != null && LaneBlocked(l, laid)) l.hill = null;
        }

        /// <summary>Its world frame and plan bounds.</summary>
        static void Frame(Lift l, Hill h)
        {
            h.crest = l.At(h.s, h.side * Half, 0f); h.dir = l.dir; h.across = l.across;
            float lo = -h.rise, hi = h.fall, r = h.Reach;
            h.x0 = h.z0 = float.MaxValue; h.x1 = h.z1 = float.MinValue;
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 2; b++)
                {
                    Vector3 p = h.crest + h.dir * (a == 0 ? lo : hi) + h.across * (b == 0 ? -r : r);
                    h.x0 = Mathf.Min(h.x0, p.x); h.x1 = Mathf.Max(h.x1, p.x); h.z0 = Mathf.Min(h.z0, p.z); h.z1 = Mathf.Max(h.z1, p.z);
                }
        }

        /// <summary>
        /// Will the ground take this hill? All of its footprint free (Room), and a rider's height to spare under the
        /// chairs everywhere over it, on both cables' lines.
        /// </summary>
        static bool Fits(Lift l, HillGround g, Room room, Hill h) => Fits(l, g, room, h, out _);

        static bool Fits(Lift l, HillGround g, Room room, Hill h, out bool roomy)
        {
            roomy = false;
            float w0 = h.side * Half, reach = h.Reach;
            for (float u = -h.rise; u <= h.fall + 0.01f; u += Room.Cell)
                for (float w = -reach; w <= reach + 0.01f; w += Room.Cell)
                    if (!room.Free(h.s + u, w0 + w, Profile(u, h.height, h.rise, h.fall, h.lip) * Across(w, h.blend))) return false;
            roomy = true;
            for (float u = -h.rise; u <= h.fall; u += 1f)
            {
                float s = h.s + u, bump = Profile(u, h.height, h.rise, h.fall, h.lip);
                int i = SpanAt(l, s);
                float chairs = l.CableY(i, s) - ChairDrop - CableTune.HillHeadroom;
                for (int e = -1; e <= 1; e += 2)
                    if (g.At(e, s) + bump * Across(e * Half - w0, h.blend) > chairs) return false;
            }
            return true;
        }

        /// <summary>The lane cleared of trees from the piste's edge down to a lift's hill.</summary>
        /// <summary>On a lift's hill, or in the cleared lane that leads to it from the piste (nothing may stand there:
        /// LiftEnds keeps its things off it, so the way to a cable grind is never blocked).</summary>
        public static bool HillOrLane(float x, float z, float margin)
        {
            if (OnHill(x, z, margin)) return true;
            foreach (var l in Lifts) if (l.hill != null && InLane(l, x, z, margin)) return true;
            return false;
        }

        static bool InLane(Lift l, float x, float z, float margin)
        {
            var h = l.hill;
            Vector3 b = l.At(h.s - h.rise, h.side * Half, 0f), a = h.laneFrom;
            if (z < a.z - CableTune.HillLane - margin || z > b.z + margin) return false;
            float dx = b.x - a.x, dz = b.z - a.z;
            float t = Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / (dx * dx + dz * dz));
            float ex = x - (a.x + dx * t), ez = z - (a.z + dz * t);
            float w = CableTune.HillLane + margin;
            return ex * ex + ez * ez < w * w;
        }

        /// <summary>
        /// Once every line is laid: each hill clear of all of them (a line laid later may have put a tower or its swath
        /// where an earlier line's hill was found room); one that is not is found another spot among all of them.
        /// </summary>
        static void SettleHills(List<Lift> all)
        {
            foreach (var l in all)
            {
                if (l.hill == null) continue;
                bool clear = true;
                var h = l.hill;
                var g = HillGroundOf(l);
                var room = RoomOf(l, all);
                if (!Fits(l, g, room, h)) clear = false;
                if (clear && LaneBlocked(l, all)) clear = false;
                if (!clear) PlaceHill(l, all, l.gorge >= 0 && h.catchSpan >= l.gorge);
            }
        }

        /// <summary>A line of text for each hill (SkiLift logs it with the lifts when SUMMIT_LIFTCHECK=1).</summary>
        public static string HillReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"(the lifts and their hills were laid in {LaidMs:0} ms)");
            foreach (var l in Lifts)
            {
                var h = l.hill;
                if (h == null) { sb.AppendLine($"lift {l.k}{(l.second ? "b" : "")}: no hill ({(why.TryGetValue(l, out var w) ? w : "none tried")})"); continue; }
                Vector3 c = h.crest; c.y = Slope.Height(c.x, c.z);
                sb.AppendLine($"lift {l.k}{(l.second ? "b" : "")}: hill under the {(h.side > 0f ? "up" : "down")}-going cable, crest at x={c.x:0} z={c.z:0} ({Mathf.Abs(c.x - Slope.Centre(c.z)):0} m from the piste's centre, {Course.At(c.z)}), lip {h.height:0.0} m over the slope (crest {Peak(h.height, h.lip):0.0} m), face {h.rise:0} m, {h.lip:0.00} per m steeper than the slope at the lip; the cable is {l.CableY(h.span, h.s) - c.y:0.0} m over the crest; reached from {h.needSpeed:0} m/s ({h.needSpeed * 3.6f:0} km/h) with no pop ({h.share:P0} of the speeds from there to {CableTune.HillTopSpeed:0}), meeting the cable {h.catchAlong - h.s:0} m past the crest on span {h.catchSpan}; sign at the piste's edge at z={h.sign.z:0}");
            }
            return sb.ToString();
        }
    }
}
