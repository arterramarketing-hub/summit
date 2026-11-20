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

        /// <summary>True on or round a cliff: no trees, rocks, pickups or kickers there.</summary>
        public static bool Keep(float x, float z, float margin)
        {
            if (!On) return false;
            if (JumpNear(z, 10f + margin, out var j))
            {
                float a = x - Slope.Centre(j.z);
                if (Across(a) > 0.05f && z > j.Start - 12f - margin && z < j.z + j.face + 14f + margin) return true;
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
