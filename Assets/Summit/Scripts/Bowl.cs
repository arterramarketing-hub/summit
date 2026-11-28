using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The alpine bowl (mountain audit A3): high up, in the open snow above the tree line (Forest.Low), the first cycle's
    /// ridge stretch is instead a big bowl on most mountains. The run eases off onto a 27-degree shelf, then rolls over a
    /// long convex lip, steepening to 40 degrees, where the snow ahead drops out of sight and only the sky and the far
    /// valley show; under it a treeless bowl some 250 m across, its floor concave (40 degrees easing to 30) and its walls
    /// curving up 25-30 m either side, groomed wall to wall (Slope.Half: anywhere goes); at its foot the walls close in to a
    /// steep funnel, the outlet, back onto the run. Its profile adds up to nothing, so the mountain below is as it was.
    /// A pure function of position, worked out once per mountain.
    /// </summary>
    public static class Bowl
    {
        public const bool On = true;
        public const float Share = 0.67f;   // of mountains
        public const float Reach = 125f;    // the floor's half-width (its walls curve up from here)

        public struct Shape
        {
            public int cycle;
            public float zA, zR, zF, zO, zE;   // the approach starts, the roll starts, the floor starts, the funnel starts, back on the run
            public float wall;                // how high the walls stand at the floor's edge
            public float funnel;              // the funnel's pitch (tan), worked out so the profile closes
            public float[] P;                 // the profile against the mountain's own line, every metre from zA
        }

        static Shape[] shapes; static bool[] has; static int seedFor = int.MinValue;
        public static void Forget() { has = null; seedFor = int.MinValue; }

        const float Ein = 20f, Roll = 40f, Ff = 15f, Rim = 15f;
        const float Approach = 0.51f, Lip = 0.84f, FloorEnd = 0.58f;   // tan 27, 40, 30 degrees

        static void Ensure()
        {
            if (has != null && seedFor == Slope.Seed) return;
            int n = 2;
            var sh = new Shape[n]; var h = new bool[n];
            for (int c = 0; c < n; c++) h[c] = Make(c, out sh[c]);
            shapes = sh; has = h; seedFor = Slope.Seed;
        }

        /// <summary>Does cycle c's ridge stretch hold the bowl? (Ridge asks: then it has no ridge.) Only the first cycle's is in
        /// the alpine zone, above the tree line, on most mountains.</summary>
        public static bool Is(int cycle) => On && cycle == 0 && Slope.Hash01(0, 1, 211) < Share;

        static bool Make(int c, out Shape s)
        {
            s = default;
            if (!Is(c)) return false;
            float zb = Course.First + c * Course.Cycle;
            float start = zb + Ridge.At;
            if (Cliffs.WallOf(c, out var w)) start = Mathf.Max(start, w.z2 + 15f);   // (below the deadly wall and its terrace)
            float end = zb + Course.Cycle - 4f;                                       // (short of the next cycle's first jump cliff)
            float room = end - start;
            if (room < 260f) return false;
            if (end > Course.GlacierBottom) return false;   // (in the alpine zone: Forest's tree line is at the glacier's foot)
            s.cycle = c;
            s.zA = start;
            float approach = Mathf.Min(70f, 40f + 0.1f * (room - 260f));
            s.zR = s.zA + Ein + approach;
            s.zF = s.zR + Roll;
            float floor = Mathf.Min(120f, room - (Ein + approach + Roll) - 115f);   // (the rest is the funnel: long enough to stay under about 45 degrees)
            s.zO = s.zF + floor;
            s.zE = end;
            s.wall = 25f + 6f * Slope.Hash01(c, 2, 211);
            // the funnel's pitch: whatever brings the profile back to the mountain's line by its end
            float lenF = s.zE - s.zO;
            float before = Integral(s, s.zO);
            // through the funnel: FloorEnd -> f over Ff, f, f -> Fall over Ff
            // integral (p - Fall) = (FloorEnd + f)/2*Ff + f*(lenF - 2Ff) + (f + Fall)/2*Ff - Fall*lenF = -before
            float a = 0.5f * Ff + (lenF - 2f * Ff) + 0.5f * Ff;
            float k = 0.5f * FloorEnd * Ff + 0.5f * Slope.Fall * Ff - Slope.Fall * lenF;
            s.funnel = (-before - k) / a;
            if (s.funnel > 1.3f) return false;   // (no wall of a funnel: over 52 degrees)
            int m = Mathf.CeilToInt(s.zE - s.zA) + 2;
            s.P = new float[m];
            float acc = 0f;
            for (int i = 1; i < m; i++) { float z = s.zA + i - 0.5f; acc += Slope.Fall - Pitch(s, z); s.P[i] = acc; }   // (above the mountain's line where it is gentler)
            return true;
        }

        static float Integral(in Shape s, float z1)
        {
            float acc = 0f;
            for (float z = s.zA + 0.5f; z < z1; z += 1f) acc += Pitch(s, z) - Slope.Fall;
            return acc;
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>The bowl's pitch (tan) along its middle at z.</summary>
        static float Pitch(in Shape s, float z)
        {
            if (z < s.zA || z > s.zE) return Slope.Fall;
            if (z < s.zA + Ein) return Mathf.Lerp(Slope.Fall, Approach, Smooth((z - s.zA) / Ein));
            if (z < s.zR) return Approach;
            if (z < s.zF) return Mathf.Lerp(Approach, Lip, Smooth((z - s.zR) / Roll));
            if (z < s.zO) return Mathf.Lerp(Lip, FloorEnd, (z - s.zF) / (s.zO - s.zF));
            float f = s.funnel;
            if (z < s.zO + Ff) return Mathf.Lerp(FloorEnd, f, Smooth((z - s.zO) / Ff));
            if (z < s.zE - Ff) return f;
            return Mathf.Lerp(f, Slope.Fall, Smooth((z - (s.zE - Ff)) / Ff));
        }

        public static bool Of(int cycle, out Shape s)
        {
            s = default;
            if (!On || cycle < 0) return false;
            Ensure();
            if (cycle >= has.Length || !has[cycle]) return false;
            s = shapes[cycle];
            return true;
        }

        public static bool Near(float z, float margin, out Shape s)
        {
            s = default;
            if (!On || z < Course.First || z > Course.First + 2f * Course.Cycle) return false;
            int c = Course.CycleOf(z);
            for (int cc = c; cc >= c - 1; cc--)
                if (Of(cc, out s) && z > s.zA - margin && z < s.zE + margin) return true;
            s = default;
            return false;
        }

        public static bool Has(float z) => Near(z, 0f, out _);

        /// <summary>On the bowl's groomed floor (and within margin of it): nothing is built or grows there.</summary>
        public static bool Keep(float x, float z, float margin)
        {
            if (!Near(z, margin, out var s)) return false;
            Walls(s, Mathf.Clamp(z, s.zA, s.zE), out _, out float reach);
            return Mathf.Abs(Slope.Across(x, z)) < Mathf.Max(Slope.PisteHalf, reach) + margin;
        }

        /// <summary>How the walls stand at z: their height at the floor's edge, and the floor's half-width.</summary>
        static void Walls(in Shape s, float z, out float wall, out float reach)
        {
            // they rise through the approach and the roll, stand the length of the floor, and close in through the funnel
            float up = Smooth((z - s.zA) / (s.zF - s.zA));
            float close = Smooth((z - s.zO) / (s.zE - s.zO));
            wall = s.wall * up * (1f - 0.8f * close) * (1f - Smooth((z - (s.zE - 25f)) / 25f));
            reach = Mathf.Lerp(Mathf.Lerp(Slope.PisteHalf + 10f, Reach, up), 42f, close);
        }

        /// <summary>The groomed run's width against its usual at z (Slope.Half): wall to wall in the bowl, a funnel at its foot.</summary>
        public static float Width(float z)
        {
            if (!Near(z, 0f, out var s)) return 1f;
            Walls(s, z, out _, out float reach);
            float w = Mathf.Max(1f, 0.85f * reach / Slope.PisteHalf);   // (short of the walls' steep upper part: the groomed floor stays under about 45 degrees)
            return Mathf.Lerp(1f, w, Smooth((z - s.zA) / 30f) * Smooth((s.zE - z) / 40f));   // (back to the run's own width through the funnel's foot)
        }

        /// <summary>The bowl at (x, z), given the mountain y there without it and how far (x, z) is from the run's middle.</summary>
        public static float ShapeAt(float x, float z, float y, float a)
        {
            if (!Near(z, 0f, out var s)) return y;
            float wz = Smooth((z - s.zA) / 25f) * Smooth((s.zE - z) / 25f);
            int i = Mathf.Clamp(Mathf.FloorToInt(z - s.zA), 0, s.P.Length - 2);
            float t = Mathf.Clamp01(z - s.zA - i);
            float line = Slope.FallLine(z) + Mathf.Lerp(s.P[i], s.P[i + 1], t);
            Walls(s, z, out float wall, out float reach);
            float d = Mathf.Abs(a) / Mathf.Max(reach, 1f);
            // the walls: a gentle curve up across the floor, steepening toward its edge, then on up at the mountain's own
            // grade, easing back into the open mountain beyond
            // (the wall's pitch at the floor's edge, rounding over into a rim a little higher: then the open mountain,
            // the way a cirque's side ridges fall away to the next gully)
            float edge = 2.8f * wall / Mathf.Max(reach, 1f);
            float cross = d < 1f ? wall * d * d * (0.6f + 0.4f * d * d) : wall + edge * Rim * (1f - Mathf.Exp(-(Mathf.Abs(a) - reach) / Rim));
            float yb = line + cross + (Slope.Noise(x * 0.02f + 3.3f, z * 0.02f) - 0.5f) * 2f;
            float wx = 1f - Smooth((Mathf.Abs(a) - reach - 25f) / 80f);
            return Mathf.Lerp(y, yb, wz * wx);
        }
    }
}
