using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The cliff band traverse ("wrap the band", mountain audit A1): in some cycles the ridge's stretch is instead a
    /// hard traverse along the top of a rock band. The run turns 60-70 degrees across the hill for 150-250 m along it,
    /// cut into the mountainside as a level bench (a snow bank, or rock, rising on the uphill side; the band dropping
    /// 15-40 m below the downhill edge, with the valley in view), then breaks through a gap at the band's end, a short
    /// steep ramp that hooks back down the fall line, into open snow below. At one or two places along it the band is
    /// low, a 4-8 m ledge over a clean landing: an optional air off the downhill side, a way down below the band.
    /// Everywhere else it is too tall, poled and taped along its edge.
    ///
    /// The run's line swings (Swing: added to Slope.Centre) and stays where it ends up: everything keyed to the line
    /// follows it. Bands go to alternate sides, so the line wanders back and forth and never drifts off. Across the run
    /// the width is measured square to it (Squash: Slope's cross-section, Slope.Across, Slope.HalfAt), so the groomed
    /// run is as wide on the traverse as anywhere. Pure functions of position, worked out once per mountain and published
    /// lock-free (asked for every height, on the terrain's worker threads).
    /// </summary>
    public static class CliffBand
    {
        public const bool On = true;
        public const float Share = 0.45f;            // of the cycles that could have one
        const float From = Ridge.At + 10f;            // where in a cycle a band may start (the ridge's stretch, which it takes)
        public const float Le = 60f, Lg = 48f;        // the turn in, and the gap (z): heading up to full, and back to the fall line (a 35-45 m radius, not a hairpin)
        const float EdgeOut = 3.5f;                   // the band's lip, this far out past the groomed run's edge (square to it)
        /// <summary>The band's lip, out from the run's middle at zc (square to it): past the groomed run's own edge.</summary>
        public static float EdgeAt(float zc) => Slope.Half(zc) + EdgeOut;
        /// <summary>(The lip on a full-width traverse, for the test tools.)</summary>
        public const float Edge = Slope.PisteHalf + EdgeOut;
        const float FaceK = 0.45f;                    // the face's width per metre of its height (about 66 degrees)
        const float Below = 110f;                     // under the band the ground climbs back to the mountain's over this far

        public struct Band
        {
            public int cycle;
            public float side;            // +1: the line swings toward +x
            public float z0, z1, z2, z3;  // the turn in starts, full heading, the gap begins, back on the fall line
            public float k;               // the traverse's sideways run per metre down the hill (tan of its heading)
            public float height;          // the band, below the downhill edge
            public float l0, l1;          // its low places (along z: their middles; 0 none) and their height
            public float lowH;
            public bool ledge;            // an angled ledge (A4): 30-45 degrees, a narrow bench, a rock wall above, a tall cliff below
            public float D => k * (Le * 0.5f + (z2 - z1) + Lg * 0.5f);   // how far the line ends up shifted
            public float Start => z0;
            public float End => z3;
        }

        // ---------------------------------------------------------------- the day's bands

        static Band[] bands; static bool[] has; static float[] before; static int bandSeed = int.MinValue;

        /// <summary>Work them out afresh (Course.ResetCaches).</summary>
        public static void Forget() { has = null; bandSeed = int.MinValue; }

        static void Ensure()
        {
            if (has != null && bandSeed == Slope.Seed) return;
            int n = Mathf.CeilToInt(Slope.FinishZ / Course.Cycle) + 2;
            var b = new Band[n]; var h = new bool[n]; var pre = new float[n + 1];
            float side = Slope.Hash01(3, 2, 201) < 0.5f ? -1f : 1f;
            for (int c = 0; c < n; c++)
            {
                pre[c + 1] = pre[c];
                if (!Make(c, side, out b[c])) continue;
                h[c] = true;
                pre[c + 1] += b[c].side * b[c].D;
                side = -side;   // (the next one swings back the other way)
            }
            bands = b; has = h; before = pre; bandSeed = Slope.Seed;
        }

        /// <summary>Is cycle c's ridge stretch a band? (Ridge asks: such a cycle has no ridge.)</summary>
        public static bool Is(int cycle) => IsBand(cycle) || IsLedge(cycle);

        /// <summary>A ridge stretch that would have been a ledge (Ridge.FormOf's own draw), angled across the hill instead (A4).</summary>
        public static bool IsLedge(int cycle) => On && cycle >= 0 && Eligible(cycle) && Slope.Hash01(cycle, 5, 61) >= 0.78f;

        /// <summary>A cliff band traverse (A1).</summary>
        static bool IsBand(int cycle)
        {
            if (!On || cycle < 0 || !Eligible(cycle) || IsLedge(cycle)) return false;
            if (Slope.Hash01(cycle, 1, 201) < Share) return true;
            // every mountain has at least one: if none of its cycles drew one, the one that came nearest gets it
            int best = -1; float bestH = 2f;
            for (int c = 0; c < 8; c++)
            {
                if (!Eligible(c) || IsLedge(c)) continue;
                float h = Slope.Hash01(c, 1, 201);
                if (h < Share) return false;
                if (h < bestH) { bestH = h; best = c; }
            }
            return cycle == best;
        }

        static bool Eligible(int cycle)
        {
            float zb = Course.First + cycle * Course.Cycle;
            return zb + From > Course.GlacierBottom + 80f && zb + 880f < Course.ClearFrom;   // (off the glacier's crevasses, short of the finish)
        }

        static bool Make(int c, float side, out Band b)
        {
            b = default;
            if (!Is(c)) return false;
            float zb = Course.First + c * Course.Cycle;
            b.cycle = c;
            // the side away from this cycle's deadly wall, if it decides it (the wall runs on past the danger stretch);
            // and starting below where the wall has died away
            float start = zb + From;
            if (Cliffs.WallOf(c, out var w)) { side = -w.side; start = Mathf.Max(start, w.z2 + 20f); }
            b.side = side;
            b.ledge = IsLedge(c);
            float heading = (b.ledge ? 30f + 15f * Slope.Hash01(c, 3, 201) : 60f + 8f * Slope.Hash01(c, 3, 201)) * Mathf.Deg2Rad;
            b.k = Mathf.Tan(heading);
            float along = b.ledge ? 260f + 70f * Slope.Hash01(c, 4, 201) : 150f + 100f * Slope.Hash01(c, 4, 201);   // the traverse, along the run
            float lz = along * Mathf.Cos(heading);
            b.z0 = start; b.z1 = b.z0 + Le; b.z2 = b.z1 + lz; b.z3 = b.z2 + Lg;
            if (b.z3 > zb + Ridge.End) { b.z2 = zb + Ridge.End - Lg; b.z3 = zb + Ridge.End; }
            if (b.z2 - b.z1 < 40f) return false;
            b.height = b.ledge ? 45f + 25f * Slope.Hash01(c, 5, 201) : 17f + 23f * Slope.Hash01(c, 5, 201);
            if (b.ledge) return true;   // (no low places: a ledge's cliff is all exposure)
            // one or two low places along the traverse, a third and two thirds of the way along
            b.lowH = 4f + 4f * Slope.Hash01(c, 6, 201);
            b.l0 = Mathf.Lerp(b.z1, b.z2, 0.3f + 0.1f * Slope.Hash01(c, 7, 201));
            b.l1 = Slope.Hash01(c, 8, 201) < 0.5f ? Mathf.Lerp(b.z1, b.z2, 0.7f + 0.1f * Slope.Hash01(c, 9, 201)) : 0f;
            return true;
        }

        /// <summary>Cycle c's band, if it has one.</summary>
        public static bool Of(int cycle, out Band b)
        {
            b = default;
            if (!On || cycle < 0) return false;
            Ensure();
            if (cycle >= has.Length || !has[cycle]) return false;
            b = bands[cycle];
            return true;
        }

        /// <summary>The band whose stretch (the turn in to the gap's end) covers z, with a margin, if any.</summary>
        public static bool Near(float z, float margin, out Band b)
        {
            b = default;
            if (!On || z < Course.First) return false;
            float u = Mathf.Repeat(z - Course.First, Course.Cycle);
            if (u < From - margin && u > margin) return false;
            int c = Course.CycleOf(z);
            for (int cc = c; cc >= c - 1; cc--)
                if (Of(cc, out b) && z > b.z0 - margin && z < b.z3 + margin) return true;
            b = default;
            return false;
        }

        /// <summary>True anywhere in a band's stretch (the course's Band section: no kickers, drops or park features).</summary>
        public static bool Has(float z) => Near(z, 0f, out _);

        // ---------------------------------------------------------------- the run's width (B4)

        /// <summary>
        /// The groomed run's width at z, against its usual (B4): narrow through a band's gap (a 26 m ramp) and then wide
        /// open below it to the end of the cycle (some 70 m: the open snow under the band), and a narrow track (16 m) the
        /// length of an angled ledge. 1 everywhere else.
        /// </summary>
        public static float Width(float z)
        {
            if (!On || z < Course.First) return 1f;
            int c = Course.CycleOf(z);
            float w = 1f;
            for (int cc = c; cc >= c - 1; cc--)
            {
                if (!Of(cc, out var b)) continue;
                if (b.ledge) { w = Mathf.Min(w, 1f - 0.6f * Turn(b, z)); continue; }
                // the gap: narrowing over the traverse's end and opening out as it hooks back down
                float g = Mathf.Clamp01(1f - Mathf.Abs(z - (b.z2 + Lg * 0.6f)) / (Lg * 0.8f));
                w = Mathf.Min(w, 1f - 0.35f * g * g * (3f - 2f * g));
                // then wide open to the end of its cycle
                float end = Course.First + (cc + 1) * Course.Cycle - 10f;
                float open = Mathf.Min(Mathf.Clamp01((z - b.z3 - 5f) / 30f), Mathf.Clamp01((end - z) / 40f));
                if (open > 0f) w = Mathf.Max(w, 1f + 0.7f * open * open * (3f - 2f * open));
            }
            return w;
        }

        /// <summary>Where a band's wide run below it ends (lifts start their next line below it).</summary>
        public static float WideEnd(in Band b) => Course.First + (b.cycle + 1) * Course.Cycle - 10f;

        // ---------------------------------------------------------------- the swing

        /// <summary>The traverse's share of its full heading at z: 0 before it and after the gap, 1 along it.</summary>
        static float Turn(in Band b, float z)
        {
            if (z <= b.z0 || z >= b.z3) return 0f;
            if (z < b.z1) { float u = (z - b.z0) / Le; return u * u * (3f - 2f * u); }
            if (z <= b.z2) return 1f;
            float v = (z - b.z2) / Lg; return 1f - v * v * (3f - 2f * v);
        }

        /// <summary>How far the bands have moved the line sideways by z.</summary>
        public static float Swing(float z)
        {
            if (!On || z < Course.First) return 0f;
            Ensure();
            int c = Course.CycleOf(z);
            if (c < 0) return 0f;
            if (c >= has.Length) return before[has.Length];
            float s = before[c];
            if (!has[c]) return s;
            var b = bands[c];
            if (z <= b.z0) return s;
            float g;
            if (z < b.z1) { float u = (z - b.z0) / Le; g = Le * (u * u * u - 0.5f * u * u * u * u); }
            else if (z <= b.z2) g = Le * 0.5f + (z - b.z1);
            else if (z < b.z3) { float v = (z - b.z2) / Lg; g = Le * 0.5f + (b.z2 - b.z1) + Lg * (v - (v * v * v - 0.5f * v * v * v * v)); }
            else g = Le * 0.5f + (b.z2 - b.z1) + Lg * 0.5f;
            return s + b.side * b.k * g;
        }

        /// <summary>The swing's own sideways run per metre down the hill at z (0 off a band).</summary>
        public static float SwingSlope(float z) => Near(z, 0f, out var b) ? b.side * b.k * Turn(b, z) : 0f;

        /// <summary>A band's turn at z: 0 off a band, rising to 1 along its traverse (its heading's share).</summary>
        public static float TurnShare(float z) => Near(z, 0f, out var b) ? Turn(b, z) : 0f;

        /// <summary>How much of a band's traverse is on at z (0..1, its heading's share): the cut bench and the band go with it.</summary>
        public static float Weight(float z) { if (!Near(z, 0f, out var b)) return 0f; float t = Turn(b, z); return t * t; }

        // ---------------------------------------------------------------- the bench and the band

        /// <summary>How tall the band is at z (along z), its low places in: 0 before the traverse and after the gap.</summary>
        public static float HeightAt(in Band b, float z)
        {
            float h = b.height * Mathf.Clamp01(Turn(b, z) * 1.6f - 0.6f);
            foreach (float l in new[] { b.l0, b.l1 })
            {
                if (l <= 0f) continue;
                float d = Mathf.Abs(z - l) / 9f;   // (a low place about 12 m long along z, easing in over 6 m)
                if (d < 1.5f) { float t = Mathf.Clamp01(d - 0.5f); h = Mathf.Lerp(Mathf.Min(h, b.lowH), h, t * t * (3f - 2f * t)); }
            }
            return h;
        }

        /// <summary>True along a band's low places (the optional air): its edge is not taped there.</summary>
        public static bool Low(in Band b, float z)
        {
            foreach (float l in new[] { b.l0, b.l1 }) if (l > 0f && Mathf.Abs(z - l) < 6f) return true;
            return false;
        }

        /// <summary>
        /// Where (x, z) is against a band's run: the z of the point on the run's middle square across from it (z_c: the
        /// bench, the band and its height go by that, not by the point's own z, which lies 30-50 m further down the hill
        /// under the band), the traverse's share there, and how far the point is out toward the downhill side.
        /// </summary>
        static bool Foot(float x, float z, out Band b, out float zc, out float wt, out float down, out float sn)
        {
            zc = z; wt = 0f; down = 0f; sn = 0f;
            if (!Near(z, 70f, out b)) return false;
            // (two steps: the square-on point's own heading)
            for (int i = 0; i < 2; i++)
            {
                float k = b.side * b.k * Turn(b, zc) + Slope.BaseSlope(zc);
                float cs = 1f / Mathf.Sqrt(1f + k * k);
                sn = k * cs;
                zc = z + (x - Slope.Centre(zc) - (z - zc) * k) * sn * cs;
            }
            wt = Turn(b, zc);
            if (wt <= 0f) return false;
            float kk = b.side * b.k * wt + Slope.BaseSlope(zc);
            float c2 = 1f / Mathf.Sqrt(1f + kk * kk);
            sn = kk * c2;
            // square to the run: (x, z) less the run's middle at z_c, along the across direction (cs, -sn)
            float across = (x - Slope.Centre(zc)) * c2 - (z - zc) * sn;
            down = -across * Mathf.Sign(kk);
            return true;
        }

        /// <summary>
        /// The bench and the band at (x, z), given the mountain y there without them, and the run's own cross-section
        /// there (its shallow trough): the run's surface cut level across it, a bank up to the mountain on the uphill
        /// side, the band down to the slope below on the downhill side. y unchanged off a band.
        /// </summary>
        public static float Shape(float x, float z, float y, float trough)
        {
            if (!Foot(x, z, out var b, out float zc, out float wt, out float down, out float sn)) return y;
            float wb = wt * wt;
            float yb = Slope.FallLine(zc) + trough;   // the run cut level across: its height only by how far along it is
            float yOut;
            float edge = EdgeAt(zc);
            if (down < edge)
            {
                // the bench, and on the uphill side a bank up to the mountain over 24 m (a ledge's: a wall, over 6 m)
                float up = Mathf.Clamp01((-down - Slope.Half(zc) - 2f) / (b.ledge ? 6f : 24f));
                yOut = Mathf.Lerp(yb, Mathf.Max(y, yb), up * up * (3f - 2f * up));
            }
            else
            {
                // over the edge: the band (its lip rounded), then the slope below falling at the mountain's grade square
                // to the run, easing back to the mountain's own ground far out
                float hgt = Mathf.Max(3f, HeightAt(b, zc));
                float d = down - edge, wF = FaceK * hgt;
                float grade = Slope.Fall * Mathf.Abs(sn);
                float s = Mathf.Clamp01(d / wF);
                float face = hgt * s * s * (3f - 2f * s);
                float yBand = yb - face - grade * Mathf.Max(0f, d - wF);
                float far = Mathf.Clamp01((d - wF) / Below);
                yOut = Mathf.Lerp(yBand, y, far * far * (3f - 2f * far));
            }
            return Mathf.Lerp(y, yOut, wb);
        }

        /// <summary>On a band's face (drawn as rock).</summary>
        public static bool OnFace(float x, float z)
        {
            if (!Foot(x, z, out var b, out float zc, out float wt, out float down, out _) || wt * wt < 0.3f) return false;
            float hgt = HeightAt(b, zc);
            float edge = EdgeAt(zc);
            return hgt > 2.5f && down > edge + 0.8f && down < edge + FaceK * hgt + 0.4f;
        }

        /// <summary>On the bench itself (always snow), out to the band's lip.</summary>
        public static bool OnBench(float x, float z)
        {
            if (!Foot(x, z, out _, out float zc, out float wt, out float down, out _) || wt * wt < 0.2f) return false;
            return down < EdgeAt(zc) && down > -(Slope.Half(zc) + 2f);
        }

        /// <summary>The ground below a band's tall parts, where a fall from its edge ends (Deadly.Below).</summary>
        public static bool BelowTall(float x, float z)
        {
            if (!Foot(x, z, out var b, out float zc, out float wt, out float down, out _) || wt * wt < 0.3f) return false;
            float hgt = HeightAt(b, zc);
            if (hgt < 12f) return false;
            float edge = EdgeAt(zc);
            return down > edge + FaceK * hgt * 0.7f && down < edge + FaceK * hgt + 30f;
        }

        /// <summary>The world (x, z) `down` metres square out toward the downhill side from the run's middle at zc, on a band.</summary>
        public static Vector2 PointAt(in Band b, float zc, float down)
        {
            float k = b.side * b.k * Turn(b, zc) + Slope.BaseSlope(zc);
            float cs = 1f / Mathf.Sqrt(1f + k * k), sn = k * cs;
            float sg = -Mathf.Sign(k);   // (the downhill side: against the across direction (cs, -sn) where the run swings to +x)
            return new Vector2(Slope.Centre(zc) + sg * cs * down, zc - sg * sn * down);
        }

        /// <summary>Keep-out: the bench and its edge, the face (no trees, rocks, pickups or kickers there).</summary>
        public static bool Keep(float x, float z, float margin)
        {
            if (!Foot(x, z, out var b, out float zc, out float wt, out float down, out _) || wt < 0.1f) return false;
            return down > -(Slope.Half(zc) + 6f + margin) && down < EdgeAt(zc) + FaceK * b.height + 2f + margin;
        }
    }
}
