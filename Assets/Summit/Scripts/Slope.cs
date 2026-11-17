using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The mountain as one smooth function of (x, z). Downhill is +Z.
    /// A groomed piste meanders down the middle with kickers on it; either side is
    /// rougher snow that fills with trees and climbs into walls, so the line is always
    /// readable and the trees are where you go for a challenge.
    /// Everything is built from long, smooth waves: nothing shorter than a few metres,
    /// so the board never chatters. One mountain per day.
    /// </summary>
    public static class Slope
    {
        public const float Fall = 0.34f;      // mean gradient of the fall line (about 19 degrees)
        public const float Quad = 1.5f;       // terrain mesh spacing; the rider sits on exactly this mesh
        public const float PisteHalf = 20f;   // half the groomed width
        public static int Seed { get; private set; }
        static float ph, ox, oz;

        public static void Init(int seed)
        {
            Seed = seed;
            ph = (seed % 628) / 100f;
            // small offsets only: large ones would lose float precision and step the terrain
            ox = Mathf.Repeat(seed * 37.31f, 211f);
            oz = Mathf.Repeat(seed * 19.77f, 199f);
        }

        public static int TodaysSeed()
        {
            long day = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
            return (int)(day % 9973);
        }

        // ---------------------------------------------------------------- noise

        public static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                int h = (x + salt * 7919) * 374761393 + (y - salt * 104729) * 668265263;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967296f;
            }
        }

        /// <summary>Smooth value noise in [0, 1].</summary>
        public static float Noise(float x, float y)
        {
            x += ox; y += oz;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);   // quintic: no creases at cell edges
            float sy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float a = Hash01(x0, y0, 0), b = Hash01(x0 + 1, y0, 0), c = Hash01(x0, y0 + 1, 0), d = Hash01(x0 + 1, y0 + 1, 0);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        // ---------------------------------------------------------------- shape

        /// <summary>Centre of the piste at a given distance down the hill.</summary>
        public static float Centre(float z)
        {
            return 26f * Mathf.Sin(z * 0.0047f + ph)
                 + 11f * Mathf.Sin(z * 0.0113f + 1.3f + ph * 1.7f)
                 + 5f * Mathf.Sin(z * 0.0240f + 0.4f + ph * 2.3f);
        }

        /// <summary>0 on the groomed piste, 1 well into the rough either side.</summary>
        public static float Rough(float x, float z)
        {
            float a = Mathf.Abs(x - Centre(z));
            return Mathf.Clamp01((a - PisteHalf) / 10f);
        }

        public static float Height(float x, float z)
        {
            float y = -z * Fall;
            // long rollers down the fall line: steeper pitches and flatter run-outs
            y += 2.6f * Mathf.Sin(z * 0.031f + ph);
            y += 1.3f * Mathf.Sin(z * 0.073f + 2.0f + ph * 1.3f);
            // the cross-section: a shallow trough along the piste, walls climbing far off it
            float a = x - Centre(z), aa = Mathf.Abs(a);
            y += 0.0035f * a * a;
            if (aa > 34f) y += (aa - 34f) * (aa - 34f) * 0.010f;
            // broad lumps in the rough; a little side-to-side tilt on the piste
            float rough = Mathf.Clamp01((aa - PisteHalf) / 10f);
            y += (Noise(x * 0.045f, z * 0.045f) - 0.5f) * 2.6f * rough;
            y += (Noise(x * 0.011f + 50f, z * 0.009f) - 0.5f) * 7f;
            y += Kicker(x, z);
            return y;
        }

        // Kickers sit on the piste every so often: a curved ramp you can see coming,
        // a crisp lip, then a long smooth landing below it.
        public const float KickPeriod = 130f, KickRamp = 12f, KickHeight = 2.4f, KickDrop = 5f, KickWidth = 9f;

        public static float Kicker(float x, float z)
        {
            float cell = Mathf.Floor((z - 60f) / KickPeriod);
            if (cell < 0f) return 0f;
            if (Hash01((int)cell, 3, 9) < 0.25f) return 0f;          // every so often there is none
            float side = (Hash01((int)cell, 4, 9) - 0.5f) * 18f;      // left, middle or right of the piste
            float z0 = 60f + cell * KickPeriod + 30f;
            float u = z - z0;
            if (u < 0f || u > KickRamp + KickDrop) return 0f;
            float cx = Centre(z0) + side;
            float w = Mathf.Abs(x - cx) / KickWidth;
            if (w >= 1f) return 0f;
            float m = 1f - w * w; m *= m;                                // rounded shoulders
            float h;
            if (u < KickRamp) { float t = u / KickRamp; h = t * t; }   // curved ramp: the lip is steepest
            else { float t = (u - KickRamp) / KickDrop; h = 1f - t * t * (3f - 2f * t); }
            return h * KickHeight * m;
        }

        /// <summary>Where the next kicker is from z, for the HUD or a hint.</summary>
        public static bool NextKicker(float z, out Vector3 at)
        {
            at = Vector3.zero;
            float cell = Mathf.Max(0f, Mathf.Floor((z - 60f) / KickPeriod));
            for (int i = 0; i < 3; i++, cell++)
            {
                if (Hash01((int)cell, 3, 9) < 0.25f) continue;
                float z0 = 60f + cell * KickPeriod + 30f;
                if (z0 + KickRamp < z) continue;
                float cx = Centre(z0) + (Hash01((int)cell, 4, 9) - 0.5f) * 18f;
                at = new Vector3(cx, Height(cx, z0 + KickRamp), z0 + KickRamp);
                return true;
            }
            return false;
        }

        /// <summary>Height of the drawn triangle under (x, z): the rider stands on exactly what you see.</summary>
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

        public static Vector2 Gradient(float x, float z)
        {
            const float e = 0.75f;
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
