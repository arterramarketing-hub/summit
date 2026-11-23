using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Cliffs across and beside the run, as pure functions of position like the rest of the mountain.
    ///
    /// Jump cliffs: twice a cycle in the cruise, the piste levels onto a bench and stops at a lip, with a 3-8 m
    /// band of rock below it and a long steep landing under that, like the start's own lip. Ride off it and
    /// you get proper air; the landing is steep enough that it never slams you.
    ///
    /// Deadly cliffs: once a cycle, in the danger stretch, the rough on one side of the piste ends at a rock
    /// wall 25-55 m high over a terrace, which runs on beside the run for a hundred metres or so.
    /// The wall turns the corner at its top end, so riding down through the rough on that side runs you off it
    /// too. Going over is the end of the run (see Rider.DeadlyDrop): signed and taped off along its top.
    ///
    /// Both are heightfield terms added to the open mountain (see Slope.HeightCore), so the ridges' canyons and
    /// everything else build on them.
    /// </summary>
    public static class Cliffs
    {
        public const bool On = true;

        // ---------------------------------------------------------------- jump cliffs

        // where in the cruise the lips are (metres into the cycle), give or take JumpJitter
        static readonly float[] jumpAt = { 44f, 132f };
        const float JumpJitter = 5f;
        public const float JumpLanding = 60f;   // how far the steep landing runs below the face
        const float JumpExtra = 6.5f;           // how far the landing drops below the line of the mountain, besides the face
        public const float JumpBand = 26f;      // past the piste's edge the cliff band fades out over this far

        public struct Jump
        {
            public float z, drop, rise, bench, face;
            public int cycle, k;
            public float Start => z - bench;
            public float End => z + face + JumpLanding;
        }

        /// <summary>Jump cliff k (0 or 1) of a cycle, if it is built there.</summary>
        public static bool JumpOf(int cycle, int k, out Jump j)
        {
            j = default;
            if (!On || cycle < 0 || k < 0 || k >= jumpAt.Length) return false;
            float z = Course.First + cycle * Course.Cycle + jumpAt[k] + (Slope.Hash01(cycle, k, 81) - 0.5f) * 2f * JumpJitter;
            j.cycle = cycle; j.k = k; j.z = z;
            j.drop = 3f + 4.5f * Slope.Hash01(cycle, k, 82);
            j.rise = j.drop + JumpExtra;
            // the in-run levels off onto the bench: the mountain falls away from the lip at its own pitch, so the
            // bench rises above that line by `rise` and its gradient at the lip is all but flat (a touch upward,
            // like the start's lip)
            j.bench = 2f * j.rise / 0.78f;
            // about 50 degrees at its steepest: rock, a clear line across the run, and two of the terrain's quads
            // deep at least, so the drawn snow falls away at the lip as soon as the slope under the board does
            // (a face shorter than a quad hides behind it, and the board grinds to a halt on the edge)
            j.face = Mathf.Max(3f, 1.15f * j.drop);
            if (j.End > Course.ClearFrom - 40f) return false;
            if (Course.At(j.Start - 10f) != Course.Section.Cruise || Course.At(j.End + 5f) != Course.Section.Cruise) return false;
            return true;
        }

        /// <summary>The jump cliff whose stretch (in-run to the foot of its landing) covers z, if any.</summary>
        public static bool JumpNear(float z, float margin, out Jump j)
        {
            j = default;
            // (cheaply ruled out over most of the cycle: this is asked for every height on the mountain)
            float u = Mathf.Repeat(z - Course.First, Course.Cycle);
            if (u > 205f + margin && u < Course.Cycle - 20f - margin) return false;
            int c = Course.CycleOf(z);
            for (int cc = c + 1; cc >= c - 1; cc--)   // (the first lip's in-run starts in the cycle before)
                for (int k = 0; k < jumpAt.Length; k++)
                    if (JumpOf(cc, k, out j) && z > j.Start - margin && z < j.End + margin) return true;
            j = default;
            return false;
        }

        /// <summary>Where the lip is, a across the run from the piste's centre line: it wanders a little, so the line is never ruler-straight.</summary>
        public static float LipZ(Jump j, float a) => j.z + (Slope.Noise(a * 0.06f + j.k * 13.1f, j.cycle * 3.7f + 2.2f) - 0.5f) * 5f;

        // across the run: the whole piste and a little either side, fading out into the rough
        static float Across(float a) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(a) - Slope.PisteHalf - 6f) / JumpBand));

        /// <summary>How far the jump cliffs lift (+) or drop (-) the mountain at (x, z) from its own line.</summary>
        public static float JumpHeight(float x, float z)
        {
            if (!On || z < Course.First) return 0f;
            float u0 = Mathf.Repeat(z - Course.First, Course.Cycle);
            if (u0 > 205f && u0 < Course.Cycle - 70f) return 0f;
            // every jump whose stretch covers z (one's landing can run into the next one's in-run)
            float sum = 0f;
            int c = Course.CycleOf(z);
            for (int cc = c + 1; cc >= c - 1; cc--)
                for (int k = 0; k < jumpAt.Length; k++)
                    if (JumpOf(cc, k, out var j) && z > j.Start - 8f && z < j.End + 8f) sum += Profile(j, x, z);
            return sum;
        }

        static float Profile(Jump j, float x, float z)
        {
            float a = x - Slope.Centre(j.z);
            float wx = Across(a);
            if (wx <= 0f) return 0f;
            float u = z - LipZ(j, a);
            float p;
            if (u < -j.bench) return 0f;
            if (u < 0f) { float s = (u + j.bench) / j.bench; p = j.rise * s * s; }   // the in-run, levelling onto the bench
            else if (u < j.face) { float s = u / j.face; p = j.rise - j.drop * s * s * (3f - 2f * s); }   // the rock band
            else if (u < j.face + JumpLanding)
            {
                // the landing: steepest a third of the way down it, where a flight off the lip comes down
                float s = (u - j.face) / JumpLanding;
                p = JumpExtra * (1f - s * s * (3f - 2f * s));
            }
            else return 0f;
            return p * wx;
        }

        /// <summary>On a jump cliff's face (drawn as rock even across the groomed piste).</summary>
        public static bool OnJumpFace(float x, float z)
        {
            if (!On || !JumpNear(z, 2f, out var j)) return false;
            float a = x - Slope.Centre(j.z);
            if (Across(a) < 0.35f) return false;
            float u = z - LipZ(j, a);
            return u > 0.15f * j.face && u < j.face + 0.6f;
        }

        /// <summary>On a jump cliff's landing (always snow: it is meant to be landed on).</summary>
        public static bool OnJumpLanding(float x, float z)
        {
            if (!On || !JumpNear(z, 2f, out var j)) return false;
            float a = x - Slope.Centre(j.z);
            if (Across(a) <= 0f) return false;
            float u = z - LipZ(j, a);
            return u >= j.face + 0.6f && u < j.face + JumpLanding;
        }

        // ---------------------------------------------------------------- deadly cliffs

        public struct Wall
        {
            public float z0, z1, z2;   // the wall's top end (where it turns the corner), full height from z1, gone by z2
            public float side, edge, height;
            public int cycle;
        }
        const float WallIn = 22f;      // how far down the run the wall's top end takes to reach full height
        const float WallHold = 60f;    // full height for this far
        const float WallTerrace = 80f, WallFar = 90f;   // the terrace at its foot, and the slope back up beyond it

        /// <summary>The deadly cliff of a cycle (in its danger stretch), if it is built there.</summary>
        public static bool WallOf(int cycle, out Wall w)
        {
            w = default;
            if (!On || cycle < 0) return false;
            float zb = Course.First + cycle * Course.Cycle + Course.DangerAt;
            w.cycle = cycle;
            w.side = Slope.Hash01(cycle, 1, 91) < 0.5f ? -1f : 1f;
            w.height = 25f + 30f * Slope.Hash01(cycle, 2, 91);
            w.edge = Slope.PisteHalf + 5f + 6f * Slope.Hash01(cycle, 3, 91);
            w.z0 = zb + 12f + 10f * Slope.Hash01(cycle, 4, 91);
            w.z1 = w.z0 + WallIn;
            // it dies away no faster than the terrace below can climb back to the mountain's line, so the terrace
            // never runs uphill
            w.z2 = w.z1 + WallHold + w.height / 0.4f;
            if (w.z2 > Course.ClearFrom - 40f) return false;
            return true;
        }

        public static bool WallNear(float z, float margin, out Wall w)
        {
            w = default;
            float u = Mathf.Repeat(z - Course.First, Course.Cycle);
            if (u < Course.DangerAt + 10f - margin || u > Course.DangerAt + 270f + margin) return false;
            int c = Course.CycleOf(z);
            for (int cc = c; cc >= c - 1; cc--)
                if (WallOf(cc, out w) && z > w.z0 - margin && z < w.z2 + margin) return true;
            w = default;
            return false;
        }

        /// <summary>How tall the wall is at z (0 above its top end and once it has died away).</summary>
        public static float WallHeight(Wall w, float z)
        {
            if (z <= w.z0 || z >= w.z2) return 0f;
            if (z < w.z1) { float s = (z - w.z0) / WallIn; return w.height * s * s * (3f - 2f * s); }
            if (z < w.z1 + WallHold) return w.height;
            float t = (z - w.z1 - WallHold) / (w.z2 - w.z1 - WallHold);
            return w.height * (1f - t * t * (3f - 2f * t));
        }

        /// <summary>The deadly cliffs' cut into the mountain at (x, z): 0 or negative.</summary>
        public static float WallCut(float x, float z)
        {
            if (!On || z < Course.First || !WallNear(z, 0f, out var w)) return 0f;
            float a = (x - Slope.Centre(z)) * w.side;   // + toward the cliff's side
            float hgt = WallHeight(w, z);
            if (hgt <= 0f || a <= w.edge) return 0f;
            // a rock face rounding off at its lip, leaning back further in some places than others (56 to 77 degrees
            // at its steepest), in three steeper and less steep bands down it, so it is never one plane; but never
            // a ledge flat enough to hold snow: a fall down it must not stop part way. Then the terrace below, the
            // mountain lowered, until a long slope climbs back up to it well out to the side (so the drawn terrain
            // never ends on a step)
            Face(w, z, hgt, out float start, out float width);
            float s = Mathf.Clamp01((a - start) / width);
            float u = s + 0.28f * Mathf.Sin(6f * Mathf.PI * s) / (6f * Mathf.PI);
            float depth = hgt * u * u * (3f - 2f * u);
            // a fan of rubble and snow banked against the foot of a tall wall, about 35 degrees (still a fall
            // that kills onto it: see Deadly)
            float fan = hgt * (0.04f + 0.1f * Slope.Noise(z * 0.06f + w.cycle * 3.1f, 8.1f)) * Mathf.Clamp01((hgt - 30f) / 15f);
            if (fan > 0.1f) depth = Mathf.Min(depth, hgt - fan * Mathf.Clamp01((start + width + fan * 0.7f - a) / (fan / 0.7f)));
            float far = Mathf.Clamp01((a - start - width - WallTerrace) / WallFar);
            return -depth * (1f - far * far * (3f - 2f * far));
        }

        /// <summary>
        /// Where a deadly cliff's face starts at z (at its lip, or a little out from it, where the top of a buttress
        /// stands back), and how far out it runs to its foot: wider where it leans back, narrower where it is
        /// steepest, varying along the wall like buttresses and the gullies between them.
        /// </summary>
        static void Face(Wall w, float z, float hgt, out float start, out float width)
        {
            start = w.edge + 1.5f * Slope.Noise(z * 0.11f + w.cycle * 2.3f, 6.2f);
            width = hgt * (0.45f + 0.22f * Slope.Noise(z * 0.045f + w.cycle * 1.7f, 3.9f));
        }

        /// <summary>On a deadly cliff's face (drawn as rock even near the piste).</summary>
        public static bool OnWallFace(float x, float z)
        {
            if (!On || !WallNear(z, 0f, out var w)) return false;
            float hgt = WallHeight(w, z);
            if (hgt < 3f) return false;
            float a = (x - Slope.Centre(z)) * w.side;
            Face(w, z, hgt, out float start, out float width);
            return a > start + 0.6f && a < start + width;
        }

        // ---------------------------------------------------------------- the ledge line (beside the run)
        // Once a cycle, beside the park (on the side of that cycle's deadly wall, above it, or on beside the danger stretch
        // on the other) or in the late cruise below the ridge, wherever no lift stands in it: a strip of open
        // snow 15-40 m off the piste's edge, cleared of trees, stepping down in a staircase of two to four rock ledges,
        // each a 3.5-10 m drop (lip to foot) off a level lip onto a clean, steep landing that runs into the next ledge's in-run. Stay on
        // the piste and you ride past it; peel off and link drop after drop, and rejoin the run lower down. Each ledge is
        // the jump cliff's shape (in-run levelling to a lip, a rock band, a steep landing) narrowed to the strip; the open
        // mountain's spurs and gullies are smoothed out of the strip (LedgeCalm) so every ledge is square to the line.

        public struct Ledge
        {
            public float z, drop, rise, bench, face;
            public float Start => z - bench;
            public float End => z + face + LedgeLanding;
        }

        public struct LedgeLine
        {
            public int cycle, count;
            public float side, off, half;   // which side, the strip's middle (out from the piste's centre line), its half-width
            public Ledge l0, l1, l2, l3;
            public float Start => l0.Start;
            public float End => this[count - 1].End;
            public Ledge this[int i] => i == 0 ? l0 : i == 1 ? l1 : i == 2 ? l2 : l3;
        }

        public const float LedgeLanding = 26f;   // each ledge's steep landing
        const float LedgeExtra = 3f;             // how far each landing drops below the mountain's line, besides the face
        public const float LedgeFade = 16f;      // the strip's sides ease back into the open mountain over this far
        const float LedgeFrom = Course.ParkAt + 18f, LedgeTo = Course.DangerAt + 120f;   // where in a cycle a line beside the park may run

        // every cycle's line, worked out at once for a mountain and published as one array (asked for every height near
        // it, on the terrain's worker threads too: no lock, and a race only works out the same lines twice)
        static LedgeLine[] ledgeLines;
        static int ledgeSeed = int.MinValue;
        /// <summary>Work the ledge lines out afresh (Course.ResetCaches).</summary>
        public static void ForgetLedges() { ledgeLines = null; ledgeSeed = int.MinValue; }

        /// <summary>A cycle's ledge line, if it has one.</summary>
        public static bool LedgeOf(int cycle, out LedgeLine L)
        {
            L = default;
            if (!On || cycle < 0 || LedgesHidden || Course.Laying) return false;   // (never worked out while the lifts or crevasses are laid)
            var lines = ledgeLines;
            if (lines == null || ledgeSeed != Slope.Seed)
            {
                int n = Mathf.CeilToInt(Slope.FinishZ / Course.Cycle) + 2;
                lines = new LedgeLine[n];
                for (int c = 0; c < n; c++) lines[c] = MakeLedges(c);
                ledgeLines = lines; ledgeSeed = Slope.Seed;
            }
            if (cycle >= lines.Length) return false;
            L = lines[cycle];
            return L.count > 0;
        }

        /// <summary>
        /// Ledges hidden (LiftLine sets this while it lays the day's lifts): the lifts go where they always went, on the
        /// mountain without ledges, and each ledge line is then put where no lift's tower, station or hill stands in it.
        /// </summary>
        public static bool LedgesHidden;

        static LedgeLine MakeLedges(int cycle)
        {
            var none = new LedgeLine { cycle = cycle };
            if (Slope.Hash01(cycle, 1, 93) < 0.15f) return none;   // now and then none
            var lifts = LiftLine.Lifts;   // (laid first, with the ledges hidden)
            float zb = Course.First + cycle * Course.Cycle;
            // the wall's side first, ending well above its top end; then the other side (the lifts' usual side: LiftLine
            // lays them through these stretches away from the wall), on beside the danger stretch. Each from a few
            // starting points, the first that has room for two ledges clear of every lift
            bool wall = WallOf(cycle, out var w);
            float first = wall ? w.side : Slope.Hash01(cycle, 2, 93) < 0.5f ? -1f : 1f;
            // two stretches, in a seeded order: beside the park (and on beside the danger stretch), or the late cruise,
            // from below a ridge's canyons (or from where a ridge would start, when the cycle has none) to the cycle's end
            float lateFrom = Ridge.Built(cycle) ? Ridge.EndOf(cycle) + Ridge.FadeAfter + 10f : Course.PipeAt + 10f;
            if (CliffBand.Of(cycle, out var band)) lateFrom = Course.Cycle;   // (none under a cliff band: its wide run, then the next cycle)
            if (Bowl.Of(cycle, out _)) lateFrom = Course.Cycle;   // (nor in the bowl)
            bool lateFirst = Slope.Hash01(cycle, 7, 93) < 0.5f;
            for (int stretch = 0; stretch < 2; stretch++)
            {
                bool late = (stretch == 0) == lateFirst;
                for (int pass = 0; pass < 2; pass++)
                {
                    float side = pass == 0 ? first : -first;
                    float from = zb + (late ? lateFrom : LedgeFrom) + 6f * Slope.Hash01(cycle, 6, 93);
                    float to = zb + (late ? Course.Cycle - 8f : LedgeTo);
                    if (!late && wall && side == w.side) to = Mathf.Min(to, w.z0 - 25f);
                    if (!late && Ridge.Built(cycle)) to = Mathf.Min(to, zb + Ridge.At - Ridge.FadeBefore - 10f);   // (above the ridge's canyons)
                    if (late && wall && side == w.side) from = Mathf.Max(from, w.z2 + 25f);   // (below the deadly wall and its terrace)
                    for (float at = from; at < to - 90f; at += 25f)
                    {
                        var L = Lay(cycle, side, at, to);
                        if (L.count >= 2 && Clear(L, lifts)) return L;
                    }
                }
            }
            return none;
        }

        static LedgeLine Lay(int cycle, float side, float z, float to)
        {
            var L = new LedgeLine { cycle = cycle, side = side };
            L.half = 10f + 5f * Slope.Hash01(cycle, 3, 93);
            L.off = Slope.PisteHalf + 15f + L.half + 6f * Slope.Hash01(cycle, 4, 93);
            int want = 2 + Mathf.FloorToInt(3f * Slope.Hash01(cycle, 5, 93) * 0.999f);
            for (int i = 0; i < want; i++)
            {
                // (the face's own fall adds to it: lip to foot, 3.5 to 10 m)
                var e = new Ledge { drop = 1.8f + 3.4f * Slope.Hash01(cycle, 10 + i, 93) };
                e.rise = e.drop + LedgeExtra;
                e.bench = 2f * e.rise / 0.78f;   // (levels onto a lip all but flat, as a jump cliff's)
                e.face = Mathf.Max(3f, 1.15f * e.drop);
                e.z = z + e.bench;
                if (e.End > to || e.End > Course.ClearFrom - 60f) break;
                if (i == 0) L.l0 = e; else if (i == 1) L.l1 = e; else if (i == 2) L.l2 = e; else L.l3 = e;
                L.count = i + 1;
                z = e.End;   // the next ledge's in-run starts where this one's landing ends
            }
            if (L.count < 2) L.count = 0;   // (a staircase: two at least)
            return L;
        }

        /// <summary>No lift's tower foot, station or hill (or a hill's way in) in the strip or its eased sides.</summary>
        static bool Clear(in LedgeLine L, LiftLine.Lift[] lifts)
        {
            float reach = L.half + LedgeFade + 4f;
            foreach (var l in lifts)
            {
                if (l.ZMax < L.Start - 40f || l.ZMin > L.End + 40f) continue;
                foreach (var f in l.foot) if (In(L, f.x, f.z, reach)) return false;
                if (In(L, l.top.x, l.top.z, reach + 10f) || In(L, l.bottom.x, l.bottom.z, reach + 10f)) return false;

            }
            for (float z = L.Start - 10f; z < L.End + 10f; z += 4f)
                for (float b = -reach; b <= reach; b += 4f)
                    if (LiftLine.OnHill(Slope.Centre(z) + L.side * (L.off + b), z, 2f)) return false;
            return true;
        }

        static bool In(in LedgeLine L, float x, float z, float reach)
        {
            if (z < L.Start - 30f || z > L.End + 30f) return false;
            return Mathf.Abs((x - Slope.Centre(z)) * L.side - L.off) < reach;
        }

        /// <summary>The ledge line whose stretch covers z (margin either end), if any.</summary>
        public static bool LedgeNear(float z, float margin, out LedgeLine L)
        {
            L = default;
            if (!On || LedgesHidden || Course.Laying || z < Course.First) return false;   // (crevasses are laid without them, as without hills)
            float u = Mathf.Repeat(z - Course.First, Course.Cycle);
            if (u < LedgeFrom - margin && u > margin) return false;   // (a late line can run to the cycle's very end)
            int c = Course.CycleOf(z);
            if (u <= margin && c > 0 && LedgeOf(c - 1, out L) && z < L.End + margin) return true;
            return LedgeOf(c, out L) && z > L.Start - margin && z < L.End + margin;
        }

        /// <summary>How much of the strip (x, z) is in: 1 across its middle, easing to 0 over LedgeFade either side.</summary>
        static float LedgeAcross(in LedgeLine L, float x, float z)
        {
            float a = (x - Slope.Centre(z)) * L.side - L.off;
            float d = Mathf.Abs(a) - L.half;
            if (d <= 0f) return 1f;
            float t = Mathf.Clamp01(d / LedgeFade);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>
        /// How much of the open mountain's roughness (its spurs, gullies and lumps) is smoothed out at (x, z) for a ledge
        /// line: all of it in the strip, along its whole stretch and a little beyond, easing back at its sides and ends.
        /// </summary>
        public static float LedgeCalm(float x, float z)
        {
            if (!LedgeNear(z, 30f, out var L)) return 0f;
            float ends = Mathf.Clamp01(Mathf.Min(z - (L.Start - 30f), L.End + 30f - z) / 30f);
            return LedgeAcross(L, x, z) * ends * ends * (3f - 2f * ends);
        }

        /// <summary>The lip's line across the strip: it wanders a little, as a jump cliff's does.</summary>
        public static float LedgeLipZ(in LedgeLine L, int i, float a) => L[i].z + (Slope.Noise(a * 0.07f + i * 5.3f, L.cycle * 2.9f + 8.4f) - 0.5f) * 4f;

        /// <summary>How far the ledges lift (+) or drop (-) the mountain at (x, z) from its own line.</summary>
        public static float LedgeHeight(float x, float z)
        {
            if (!LedgeNear(z, 8f, out var L)) return 0f;
            float wx = LedgeAcross(L, x, z);
            if (wx <= 0f) return 0f;
            float a = (x - Slope.Centre(z)) * L.side - L.off;
            float sum = 0f;
            for (int i = 0; i < L.count; i++)
            {
                var e = L[i];
                float u = z - LedgeLipZ(L, i, a);
                if (u < -e.bench) continue;
                if (u < 0f) { float s = (u + e.bench) / e.bench; sum += e.rise * s * s; }                       // the in-run
                else if (u < e.face) { float s = u / e.face; sum += e.rise - e.drop * s * s * (3f - 2f * s); }    // the rock band
                else if (u < e.face + LedgeLanding) { float s = (u - e.face) / LedgeLanding; sum += LedgeExtra * (1f - s * s * (3f - 2f * s)); }   // the landing
            }
            return sum * wx;
        }

        /// <summary>On a ledge's rock band (drawn as rock).</summary>
        public static bool OnLedgeFace(float x, float z)
        {
            if (!LedgeNear(z, 2f, out var L) || LedgeAcross(L, x, z) < 0.35f) return false;
            float a = (x - Slope.Centre(z)) * L.side - L.off;
            for (int i = 0; i < L.count; i++)
            {
                float u = z - LedgeLipZ(L, i, a);
                if (u > 0.15f * L[i].face && u < L[i].face + 0.6f) return true;
            }
            return false;
        }

        /// <summary>On a ledge's landing or in-run (always snow: it is meant to be ridden).</summary>
        public static bool OnLedgeSnow(float x, float z)
        {
            if (!LedgeNear(z, 2f, out var L) || LedgeAcross(L, x, z) < 0.5f) return false;   // (its eased sides may be steep enough for rock)
            return !OnLedgeFace(x, z);
        }

        /// <summary>True on or round a cliff: no trees, rocks, pickups or kickers there.</summary>
        public static bool Keep(float x, float z, float margin)
        {
            if (!On) return false;
            if (JumpNear(z, 10f + margin, out var j))
            {
                float a = x - Slope.Centre(j.z);
                if (Across(a) > 0.05f && z > j.Start - 12f - margin && z < j.z + j.face + 14f + margin) return true;
            }
            // the ledge line's strip, its sides and its run-in and run-out: open snow, a gap in the trees
            if (LedgeNear(z, 20f + margin, out var L))
            {
                float a = Mathf.Abs((x - Slope.Centre(z)) * L.side - L.off);
                if (a < L.half + LedgeFade * 0.6f + margin) return true;
            }
            if (WallNear(z, 6f + margin, out var w))
            {
                float a = (x - Slope.Centre(z)) * w.side;
                float hgt = Mathf.Max(WallHeight(w, z), WallHeight(w, z + 6f));
                // (the widest the face gets: see Face)
                if (hgt > 1f && a > w.edge - 4f - margin && a < w.edge + 1.5f + hgt * 0.67f + 3f + margin) return true;
            }
            return false;
        }
    }
}
