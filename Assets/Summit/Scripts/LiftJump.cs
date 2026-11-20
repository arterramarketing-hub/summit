using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The bottom station you ski through: a lift's loading station, built big, with a lane across it for the
    /// rider and its far end as a jump.
    ///
    /// A station's deck is level and the mountain falls away under it at 36 degrees, so the downhill end of even a
    /// small deck stands a storey or two over the snow. This makes that the point of the thing. Beside the terminal
    /// (the chairs, the bullwheel, the hut: all as they were, behind a railing) the deck carries a lane 9 m wide
    /// on the side toward the piste. Its top leaves the snow up the hill from the station without a step (the snow
    /// and the deck's level are blended, JumpTop: where the snow is the higher of the two the top IS the snow),
    /// eases level along the terminal, runs out 12 m past the station's middle and turns up into a lip. Off that
    /// lip the rider is 13 m or so over the snow and flying: two to three seconds of air, 50 to 110 m down the
    /// mountain.
    ///
    /// That flight has to come down somewhere fit to ride away from, and where the first lines used to end
    /// (BottomAt, 15 m above the ridge stretch) it would not: on most mountains the ground past there is a
    /// canyon's funnel, a 60 to 200 m fall. So a line that carries a jump ends higher, at JumpAt, where the park
    /// gives way to the danger stretch: the flight comes down beside that stretch, on the side away from its cliff
    /// (the side the line already keeps to), outside the piste and its crevasses, with the piste a few metres away
    /// to rejoin before the ridge. For each line the flight is worked out with the game's own numbers at speeds
    /// from 6 to 46 m/s on five lines across the lane (PlaceJump), and the jump is only built if every
    /// one of them lands on open snow: off kept ground, no cliff, crevasse or building, not too steep, never a
    /// slam, and above the ridge. If none will go, the line is laid as it always was, with a plain station.
    ///
    /// The ground the jump needs is kept clear like the line's own swath (InJumpField, part of Taken): the run in
    /// under the line, a lane from the piste's edge with a sign at its top, the station, and the landing field
    /// from the lip down, widening toward the piste. No second line crosses it (LiftNetwork.Meets).
    ///
    /// CableGrind carries the rider through (its Deck mode); SkiLift draws the station and makes it solid.
    /// </summary>
    public static partial class LiftLine
    {
        public const bool Jumps = true;
        public const float JumpAt = 365f;             // a jump station, metres into its cycle (a plain one: BottomAt)
        public static readonly float[] JumpHigher = { 0f, 30f, 60f };   // or this much higher, where the ground at JumpAt will not take one
        public const float JumpMinPlan = 120f;        // the shortest line worth building to one
        // the lane, in metres across from the line's centre toward the piste
        public const float JumpLaneIn = 6f, JumpLaneOut = 15f, JumpLaneMid = 10.5f;
        public const float JumpRideHalf = 4f;         // how far either side of the lane's middle the board may be
        public static readonly float[] JumpEnds = { 12f, 9.5f, 7f };   // the lip, metres down the line past the station's middle: the longest deck whose lip is not too high
        public const float JumpEndMax = 12f;
        public const float JumpLipLen = 7f, JumpLipRise = 0.7f;
        public const float JumpDropMin = 7f, JumpDropMax = 20f;        // the lip over the snow under it
        public const float JumpBlend = 6f;            // how softly the snow turns into the deck (m of height), on the mountain's mean slope
        public const float JumpCrossFall = 0.5f;      // how much of the ground's fall at the lane's mouth may be across it (about 27 degrees off the lane)
        public const float JumpCorner = 8f;           // where the lane's level meets the snow, metres up the line from the station's middle
        public const float JumpReach = 30f;           // the furthest up the line from the station's middle the lane's top can leave the snow
        public const float JumpField = 175f;          // cleared from the lip down the line
        public const float JumpFlightClear = 135f;    // no other line's cable over the flight, this far past the lip
        public const float JumpRunIn = 95f;           // and up the line from the station's middle
        public const float JumpGuard = 2.5f;          // m to spare round every landing worked out
        public const float JumpSlam = 28f, JumpSlamAngle = 65f;   // Rider.SlamImpact, Rider.MaxLandAngle
        // the rides every jump is worked out for: these speeds onto the lane, on these lines across it (from its middle)
        public static readonly float[] JumpSpeeds = { 26f, 46f, 6f, 38f, 14f, 30f, 42f, 10f, 34f, 18f, 22f };
        public static readonly float[] JumpLines = { 0f, 3.7f, -3.7f, 1.85f, -1.85f };

        /// <summary>A bottom station's ski-through lane and jump.</summary>
        public class Jump
        {
            public float side;                 // which side of the line the lane is on, in the line's `across`: toward the piste
            public float y0;                   // the lane's level (the terminal's deck, beside it, is at the station's own: l.bottom.y)
            public float end;                  // the lip, metres down the line past the station's middle
            public float blend;                // how softly the snow turns into the deck here (less on gentler ground, so the hollow is as long)
            public float start;                // where the lane's top leaves the snow on its middle line (m past the station's middle: negative)
            public float drop;                 // the lip over the snow under it
            public Vector3 laneFrom, sign;     // the top of the cleared lane at the piste's edge, and where its sign stands
            public Flight typical;             // at 26 m/s down the middle
            public float farthest;             // how far past the lip the longest flight worked out comes down
        }

        /// <summary>One ride through a station and off its lip, worked out.</summary>
        public struct Flight
        {
            public float lipSpeed, air, distance, peak, impact, angle, fell;
            public Vector3 from, land, velocity;
            public bool landed;
        }

        /// <summary>The deck's own height t metres past the station's middle: level, then up to the lip.</summary>
        public static float JumpDeck(Lift l, float t)
        {
            float u = Mathf.Clamp01((t - (l.jump.end - JumpLipLen)) / JumpLipLen);
            return l.jump.y0 + JumpLipRise * u * u;
        }

        /// <summary>
        /// The top of the lane at t past the station's middle and c across the line: the snow up the hill, the deck
        /// down it, and a round hollow where the one turns into the other (never under either).
        /// </summary>
        public static float JumpTop(Lift l, float t, float c)
        {
            Vector3 p = l.At(l.plan + t, c, 0f);
            float g = Slope.DrawnHeight(p.x, p.z), d = JumpDeck(l, t), k = l.jump.blend;
            float h = Mathf.Max(k - Mathf.Abs(g - d), 0f) / k;
            return Mathf.Max(g, d) + h * h * k * 0.25f;
        }

        /// <summary>How far the lane's top stands over the snow there (0 where it is the snow).</summary>
        public static float JumpOver(Lift l, float t, float c)
        {
            Vector3 p = l.At(l.plan + t, c, 0f);
            return JumpTop(l, t, c) - Slope.DrawnHeight(p.x, p.z);
        }

        /// <summary>Which way the lane's top runs there (unit: down the line, and up or down with the top).</summary>
        public static Vector3 JumpDir(Lift l, float t, float c)
        {
            float a = Mathf.Min(t, l.jump.end - 0.5f);
            float rise = JumpTop(l, a + 0.5f, c) - JumpTop(l, a - 0.5f, c);
            return (l.dir + Vector3.up * rise).normalized;
        }

        /// <summary>Where the lane's top first stands clear of the snow on the line c across (m past the station's middle).</summary>
        public static float JumpStart(Lift l, float c)
        {
            for (float t = -JumpReach; t < l.jump.end; t += 0.5f) if (JumpOver(l, t, c) > 0.03f) return t;
            return l.jump.end;
        }

        /// <summary>
        /// A rider who comes onto the lane at v0 on the line c across and does nothing else: through the station as
        /// CableGrind carries them, off the lip, and down as Rider.AirStep flies them, to the snow.
        /// </summary>
        public static Flight JumpFlight(Lift l, float v0, float c, List<Vector3> path = null)
        {
            const float dt = 0.02f, step = 0.5f;
            var f = new Flight();
            float end = l.jump.end;
            // the lane's top along this line, every half metre (the same for every speed down it: kept while this
            // line's rides are worked out, so a mountain's lifts are laid in a fraction of the time)
            if (profileOf != l.jump || profileAt != c || profileEnd != end)
            {
                profileOf = l.jump; profileAt = c; profileEnd = end;
                for (int k = 0; k < profile.Length; k++) profile[k] = JumpTop(l, Mathf.Min(-JumpReach - step + k * step, end + step), c);
                profileStart = end;
                for (int k = 1; k < profile.Length && -JumpReach - step + k * step < end; k++)
                {
                    float tk = -JumpReach - step + k * step;
                    Vector3 g = l.At(l.plan + tk, c, 0f);
                    if (profile[k] - Slope.DrawnHeight(g.x, g.z) > 0.03f) { profileStart = tk; break; }
                }
            }
            float t = profileStart, v = v0;
            for (int i = 0; i < 400 && t < end; i++)
            {
                // (as JumpDir: the top a step's width on, less the top a step's width back)
                float a = (Mathf.Min(t, end - 0.5f) + JumpReach + step) / step;
                int k = Mathf.Clamp((int)a, 1, profile.Length - 3); float w = Mathf.Clamp01(a - k);
                float r = Mathf.Lerp(profile[k + 1] - profile[k - 1], profile[k + 2] - profile[k], w), dy = r / Mathf.Sqrt(1f + r * r);
                v = Mathf.Max(CableTune.MinSpeed, v + SlideAccel(v, dy) * dt);
                t += v * Mathf.Sqrt(Mathf.Max(0f, 1f - dy * dy)) * dt;
            }
            f.lipSpeed = v;
            Vector3 p = l.At(l.plan + end, c, JumpTop(l, end, c) + CableTune.BoardR);
            Vector3 u = JumpDir(l, end, c) * v;
            f.from = p;
            float under = Slope.DrawnHeight(p.x, p.z) + CableTune.BoardR;
            for (int i = 0; i < 600; i++)
            {
                u.y -= CableTune.G * dt;
                u -= u * Mathf.Min(1f, 0.0025f * u.magnitude * dt);
                Vector3 q = p + u * dt;
                float gy = Slope.DrawnHeight(q.x, q.z) + CableTune.BoardR;
                f.peak = Mathf.Max(f.peak, p.y - under);
                f.air += dt;
                if (q.y <= gy) { f.landed = true; break; }
                p = q; under = gy;
                if (path != null && i % 5 == 0) path.Add(p);
            }
            f.land = p; f.velocity = u;
            f.distance = new Vector2(p.x - f.from.x, p.z - f.from.z).magnitude;
            Vector3 n = Slope.Normal(p.x, p.z);
            f.impact = Mathf.Max(0f, -Vector3.Dot(u, n));
            f.angle = Mathf.Atan2(f.impact, (u - n * Vector3.Dot(u, n)).magnitude) * Mathf.Rad2Deg;
            f.fell = f.from.y - p.y - Slope.Fall * Mathf.Max(0f, p.z - f.from.z);
            return f;
        }

        static Jump profileOf; static float profileAt, profileStart, profileEnd;
        static readonly float[] profile = new float[Mathf.CeilToInt((JumpReach + JumpEndMax) / 0.5f) + 6];

        /// <summary>
        /// Why this flight's landing will not do (null: it will). With `guard` metres to spare all round it, as each
        /// jump is worked out: so that the rides between the ones worked out, a little faster or slower or to one
        /// side, land well too.
        /// </summary>
        public static string JumpLandFault(Lift l, Flight f, float guard = 0f)
        {
            if (!f.landed) return "never comes down";
            Vector3 p = f.land;
            float b = Course.First + l.k * Course.Cycle;
            if (p.z > b + BottomAt - 3f * guard) return "comes down in the ridge stretch";
            if (f.impact > JumpSlam && f.angle > JumpSlamAngle) return "a slam";
            if (f.fell > 45f) return "falls " + f.fell.ToString("0") + " m below the mountain's line";
            // where it comes down, and the first of the ride away from there (as far as the ridge stretch: past its
            // start the ground beside the piste is the ridge's own business, as it is for anyone riding there)
            Vector3 on = new Vector3(f.velocity.x, 0f, f.velocity.z).normalized;
            int steep = guard > 0f ? 1 : 0;
            float wide = 3f + guard;
            // (on the same 4 m grid with or without the guard, two steps further each way with it)
            float reach = guard > 0f ? 8f : 0f;
            for (float d = -4f - reach; d <= 36f + reach; d += 4f)
            {
                if (d > 4f + reach && p.z + on.z * d > b + Course.PipeAt - 2f) break;
                for (int e = -1; e <= 1; e++)
                {
                    float x = p.x + on.x * d + on.z * e * wide, z = p.z + on.z * d - on.x * e * wide;
                    if (Kept(x, z)) return "onto kept ground";
                    if (Course.CrevasseNear(x, z, 3f)) return "by a crevasse";
                    if (Deadly.Below(x, z)) return "under a cliff";
                    if (Occupied != null && Occupied(x, z)) return "onto a building's ground";
                    // (Rider: a face under 0.62 is fallen down, not ridden. A short pitch of that under the lip is only
                    // a slide to the easier ground below it; a long one is a cliff)
                    if (e == 0 && Slope.Normal(x, z).y < 0.64f && ++steep > 4) return "onto ground too steep";
                }
            }
            return null;
        }

        /// <summary>
        /// Give this line's bottom station its lane and jump, if the ground will take the station and every flight
        /// off it lands well. Leaves l.jump null if not.
        /// </summary>
        /// <summary>Set to a dictionary to have every refusal counted by its reason (Tools/LiftHarness lists them).</summary>
        public static Dictionary<string, int> JumpWhy;
        static void Why(string w) { if (JumpWhy == null) return; JumpWhy.TryGetValue(w, out int n); JumpWhy[w] = n + 1; }
        static void PlaceJump(Lift l)
        {
            l.jump = null;
            // the lane goes on the side toward the piste
            Vector3 a = l.At(l.plan, JumpLaneMid, 0f), b = l.At(l.plan, -JumpLaneMid, 0f);
            float side = Mathf.Abs(a.x - Slope.Centre(a.z)) < Mathf.Abs(b.x - Slope.Centre(b.z)) ? 1f : -1f;
            var j = new Jump { side = side };
            l.jump = j;
            bool ok = JumpGround(l, j);
            if (ok)
                foreach (float e in JumpLines)
                {
                    foreach (float v0 in JumpSpeeds)
                    {
                        var f = JumpFlight(l, v0, side * (JumpLaneMid + e));
                        string fault = JumpLandFault(l, f, JumpGuard);
                        if (fault != null) { ok = false; Why((fault.StartsWith("falls") ? "falls" : fault) + " @" + v0); break; }
                        j.farthest = Mathf.Max(j.farthest, f.distance);
                        if (e == 0f && v0 == 26f) j.typical = f;
                    }
                    if (!ok) break;
                }
            if (!ok) { l.jump = null; return; }
            // the lane down to it from the piste's edge, and the sign at the top of that lane
            Vector3 mouth = l.At(l.plan + j.start - 6f, side * JumpLaneMid, 0f);
            float zl = mouth.z - 60f;
            float sgn = mouth.x > Slope.Centre(mouth.z) ? 1f : -1f;
            float xl = Slope.Centre(zl) + sgn * Slope.PisteHalf;
            j.laneFrom = new Vector3(xl, Slope.Height(xl, zl), zl);
            float xs = Slope.Centre(zl) + sgn * (Slope.PisteHalf + 2.5f);
            j.sign = new Vector3(xs, Slope.Height(xs, zl), zl);
        }

        /// <summary>Will the ground take the bigger station? Off the piste, off kept ground, the snow falling away under the deck as it should.</summary>
        static bool JumpGround(Lift l, Jump j)
        {
            for (float t = -JumpReach; t <= JumpEndMax + 2f; t += 3f)
                foreach (float c in new[] { -StationHalfWid, 0f, JumpLaneIn, JumpLaneMid, JumpLaneOut })
                {
                    Vector3 p = l.At(l.plan + t, j.side * c, 0f);
                    if (Mathf.Abs(p.x - Slope.Centre(p.z)) < Slope.PisteHalf + CableTune.RampOffPiste) { Why("g: too near the piste"); return false; }
                    if (Kept(p.x, p.z) || (Occupied != null && Occupied(p.x, p.z))) { Why("g: kept"); return false; }
                }
            // how softly the snow turns into the deck: by how steep the ground is, so the hollow is as long on any slope
            Vector3 up = l.At(l.plan - 22f, j.side * JumpLaneMid, 0f), dn = l.At(l.plan - 4f, j.side * JumpLaneMid, 0f);
            float fall = (Slope.Height(up.x, up.z) - Slope.Height(dn.x, dn.z)) / 18f;
            j.blend = JumpBlend * Mathf.Clamp(fall / Slope.Fall, 0.45f, 1.3f);
            // the lane's own level: the snow across it a little above the terminal, so its top leaves the snow there.
            // (The piste lies in a shallow valley, so the lane, on the piste's side, is as a rule a few metres lower
            // than the terminal's deck, which stands over the highest snow under itself: the loading deck is then a
            // raised platform beside the lane.) Never higher than that deck
            j.y0 = float.MinValue;
            foreach (float c in new[] { JumpLaneIn, JumpLaneMid, JumpLaneOut })
            {
                Vector3 p = l.At(l.plan - JumpCorner, j.side * c, 0f);
                j.y0 = Mathf.Max(j.y0, Slope.Height(p.x, p.z));
            }
            j.y0 = Mathf.Min(j.y0, l.bottom.y);
            // the longest deck whose lip is not too high over the snow
            foreach (float end in JumpEnds)
            {
                j.end = end;
                float lo = float.MaxValue, hi = float.MinValue; bool fits = true;
                foreach (float c in new[] { JumpLaneIn, JumpLaneMid, JumpLaneOut })
                {
                    // the top leaves the snow somewhere up the hill from the terminal: not out at the end of its reach
                    // (snow that high beside the station would bury the lane), nor only down beside the bullwheel
                    float s = JumpStart(l, j.side * c);
                    if (s < -JumpReach + 3f || s > -3f) { Why(s > -3f ? "g: starts late" : "g: starts early"); return false; }
                    // level from the middle on, the snow well under it
                    for (float t = 2f; t <= end; t += 2f) if (JumpOver(l, t, j.side * c) < 1.2f) { Why("g: snow under the deck"); return false; }
                    float d = JumpOver(l, end, j.side * c);
                    lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
                }
                if (lo < JumpDropMin) { Why("g: low lip"); return false; }
                if (hi > JumpDropMax) { if (end == JumpEnds[JumpEnds.Length - 1]) Why("g: high lip"); continue; }
                j.start = JumpStart(l, j.side * JumpLaneMid);
                j.drop = JumpOver(l, end, j.side * JumpLaneMid);
                // and the snow must run into the lane, not across its mouth: where the ground there falls away to one
                // side by more than it falls down the line, a board is carried off sideways before it is on the deck
                foreach (float t in new[] { j.start - 12f, j.start - 4f })
                {
                    Vector3 m = l.At(l.plan + t, j.side * JumpLaneMid, 0f);
                    Vector2 g = Slope.Gradient(m.x, m.z);
                    float down = -(g.x * l.dir.x + g.y * l.dir.z), sideways = Mathf.Abs(g.x * l.across.x + g.y * l.across.z);
                    if (down < 0.2f || sideways > JumpCrossFall * down) { Why("g: the ground falls across the mouth"); return false; }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// The ground a jump station needs clear, beyond the line's own swath: the run in beside the line and the
        /// lane to it from the piste, the station itself, and the landing field from the lip down, which widens and
        /// reaches across to the piste so there is a way back onto it.
        /// </summary>
        public static bool InJumpField(Lift l, float x, float z, float margin)
        {
            var j = l.jump;
            float t = (x - l.top.x) * l.dir.x + (z - l.top.z) * l.dir.z - l.plan;
            if (t < -JumpRunIn - margin || t > j.end + JumpField + margin) return false;
            float c = ((x - l.top.x) * l.across.x + (z - l.top.z) * l.across.z) * j.side;   // toward the piste
            if (t <= j.end + 4f)
            {
                // the run in and the station: from the line's own swath across to past the lane's outer edge
                if (c > -Swath - margin && c < JumpLaneOut + 8f + margin) return true;
                // and the lane from the piste's edge
                Vector3 m = l.At(l.plan + j.start - 6f, j.side * JumpLaneMid, 0f), a = j.laneFrom;
                if (z < a.z - CableTune.RampLane - margin || z > m.z + margin) return false;
                float dx = m.x - a.x, dz = m.z - a.z;
                float u = Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / (dx * dx + dz * dz));
                float ex = x - (a.x + dx * u), ez = z - (a.z + dz * u);
                float w = CableTune.RampLane + 1f + margin;
                return ex * ex + ez * ez < w * w;
            }
            // the landing field
            float wide = 11f + 0.05f * (t - j.end) + margin;
            if (c < JumpLaneMid - wide) return false;
            if (c < JumpLaneMid + wide) return true;
            // on toward the piste: everything between the flight's line and the groomed edge
            Vector3 mid = l.At(l.plan + t, j.side * JumpLaneMid, 0f);
            float sgn = mid.x > Slope.Centre(mid.z) ? 1f : -1f;
            float off = (x - Slope.Centre(z)) * sgn;
            return off > 0f && off < (mid.x - Slope.Centre(mid.z)) * sgn + wide;
        }

        /// <summary>Would this line (its stations set) pass over a jump station's lane or the flight off it?</summary>
        static bool OverJump(Lift l, IList<Lift> laid)
        {
            for (int q = 0; q < laid.Count; q++)
            {
                var o = laid[q];
                if (o.jump == null) continue;
                // (the station and the flight off it: the run in is on the snow, and a cable may cross high over that)
                for (float t = -JumpReach; t <= o.jump.end + JumpFlightClear; t += 5f)
                {
                    Vector3 p = o.At(o.plan + t, o.jump.side * JumpLaneMid, 0f);
                    if (InSwath(l, p.x, p.z, Swath + 12f + (t > o.jump.end ? 0.05f * (t - o.jump.end) : 0f))) return true;
                }
            }
            return false;
        }

        /// <summary>A line of text for each jump (SkiLift logs it with the lifts when SUMMIT_LIFTCHECK=1).</summary>
        public static string JumpReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in Lifts)
            {
                if (l.second) continue;
                var j = l.jump;
                if (j == null) { sb.AppendLine($"lift {l.k}: a plain bottom station at z={l.bottom.z:0} (no jump)"); continue; }
                var f = j.typical;
                Vector3 lip = l.At(l.plan + j.end, j.side * JumpLaneMid, 0f);
                sb.AppendLine($"lift {l.k}: jump station at z={l.bottom.z:0}, lane on the {(j.side * l.across.x > 0f ? "+x" : "-x")} side {Mathf.Abs(lip.x - Slope.Centre(lip.z)):0} m from the piste's centre; its top leaves the snow {-j.start:0.0} m above the station's middle and its lip is {j.end:0.0} m below it; the lip is {j.drop:0.0} m over the snow; at 26 m/s: {f.air:0.0} s of air, {f.distance:0} m, {f.peak:0.0} m over the snow at most, lands at {f.velocity.magnitude:0} m/s ({f.impact:0} m/s into the snow at {f.angle:0} deg); longest flight {j.farthest:0} m");
            }
            return sb.ToString();
        }
    }
}
