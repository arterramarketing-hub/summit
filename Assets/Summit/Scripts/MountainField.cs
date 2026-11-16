using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The whole mountain as one pure function of (x, z). Downhill is +Z, so a
    /// rider facing down the fall line has heading 0 and Unity's own yaw
    /// convention needs no correction anywhere.
    /// One mountain per day: everyone riding today gets the same face.
    /// </summary>
    public static class MountainField
    {
        public const float Fall = 0.40f;          // mean gradient of the fall line
        public const float Quad = 1.5f;           // terrain mesh spacing, shared with the streamer
        public static int Seed { get; private set; }
        static float sx0, sz0, sph;

        public static void Init(int seed)
        {
            Seed = seed;
            sx0 = seed * 137.31f;
            sz0 = seed * 91.77f;
            sph = (seed % 628) / 100f;
        }

        public static int TodaysSeed()
        {
            long day = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
            return (int)(day % 9973);
        }

        /// <summary>Deterministic hash in [0, 1) for a lattice cell and a salt.</summary>
        public static float Hash01(int x, int y, int salt)
        {
            return Hash(x + salt * 7919, y - salt * 104729);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967296f;
            }
        }

        /// <summary>Smooth value noise in [0, 1].</summary>
        public static float Noise(float x, float y)
        {
            x += sx0; y += sz0;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0), b = Hash(x0 + 1, y0), c = Hash(x0, y0 + 1), d = Hash(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        /// <summary>The lazy line the mountain drifts along: where the good snow is.</summary>
        public static float DriftX(float z)
        {
            return 30f * Mathf.Sin(z * 0.00210f + sph)
                 + 14f * Mathf.Sin(z * 0.00530f + 2.1f + sph * 1.7f)
                 + 11.5f * Mathf.Sin(z * 0.0392f + 0.7f + sph * 2.3f);
        }

        public static float Height(float x, float z)
        {
            float y = -z * Fall;
            // the shape of the face: spines, bowls and broad rollers
            y += Mathf.Sin(x * 0.0180f + z * 0.0042f) * 6.5f;
            y += Mathf.Sin(x * 0.0410f - z * 0.0075f + 1.7f) * 2.4f;
            y += (Noise(x * 0.0105f, z * 0.0085f) - 0.5f) * 16f;
            y += Mathf.Sin(x * 0.00420f + sph * 2f) * 15f;
            y += Mathf.Sin(x * 0.01050f - z * 0.0016f + 1.1f) * 5.5f;
            y += (Noise(x * 0.0046f, z * 0.0039f) - 0.5f) * 22f;
            // grain you feel underfoot but do not fly off
            y += (Noise(x * 0.0290f, z * 0.0250f) - 0.5f) * 2.0f;
            y += (Noise(x * 0.0980f, z * 0.0980f) - 0.5f) * 0.7f;
            // wind lips: a long ramp up and a short drop off the back, which
            // is what throws you into the air
            float lip = Mathf.Sin(z * 0.0376f + sph + Noise(x * 0.004f, z * 0.003f) * 6f);
            y += lip > 0f ? lip * lip * lip * 9.5f : -Mathf.Pow(-lip, 1.6f) * 4.2f;
            // the face is a valley: far off the line the walls rise
            float off = Mathf.Abs(x - DriftX(z));
            if (off > 120f) y += (off - 120f) * (off - 120f) * 0.004f;
            return y;
        }

        /// <summary>
        /// Height of the rendered triangle under (x, z). The mesh is a straight-line
        /// version of Height between lattice points; the board sits on this so it
        /// never sinks into a facet or floats over one.
        /// </summary>
        public static float DrawnHeight(float x, float z)
        {
            float gx = x / Quad, gz = z / Quad;
            int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
            float u = gx - i, v = gz - j;
            float x0 = i * Quad, z0 = j * Quad;
            float hb = Height(x0 + Quad, z0), hc = Height(x0, z0 + Quad);
            if (u + v <= 1f)
            {
                float ha = Height(x0, z0);
                return ha + (hb - ha) * u + (hc - ha) * v;
            }
            float hd = Height(x0 + Quad, z0 + Quad);
            return hd + (hc - hd) * (1f - u) + (hb - hd) * (1f - v);
        }

        /// <summary>dh/dx and dh/dz.</summary>
        public static Vector2 Gradient(float x, float z)
        {
            const float e = 0.6f;
            return new Vector2(
                (Height(x + e, z) - Height(x - e, z)) / (2f * e),
                (Height(x, z + e) - Height(x, z - e)) / (2f * e));
        }

        public static Vector3 Normal(float x, float z)
        {
            Vector2 g = Gradient(x, z);
            return new Vector3(-g.x, 1f, -g.y).normalized;
        }
    }
}
