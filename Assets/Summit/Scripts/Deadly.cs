using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Where the mountain's marked cliffs drop away: the ground at the foot of the walls in the danger stretch, a
    /// ridge's cliff-side canyon, the valley under the summit. Only geography now (where nothing is built, where a
    /// buddy's voice echoes): what kills is the height of a fall, wherever it is (Rider.DeathFall).
    /// </summary>
    public static class Deadly
    {
        /// <summary>True on the ground below a deadly cliff, where a fall from its top ends.</summary>
        public static bool Below(float x, float z)
        {
            // the terrace under a deadly cliff (Cliffs), signed and taped off along its top: wherever its cut has
            // lowered the mountain
            if (Cliffs.WallCut(x, z) < -3f) return true;
            // the slope below a cliff band's tall parts (its low places are an air, not a fall)
            if (CliffBand.BelowTall(x, z)) return true;
            // a ridge's canyon on a cliff side (a one-sided ridge's snow side is a long ride down, not a fall)
            if (Ridge.InCanyons(z))
            {
                float a = x - Slope.Centre(z);
                if (Ridge.CliffSide(z, a) && Mathf.Abs(a) > Ridge.HalfWidth(z) + Ridge.Shoulder) return true;
            }
            // the valley under the summit's cliff, beyond the camp's cable railing
            if (z < -41f && x - Slope.Centre(Slope.PadLip) > Slope.SummitRight) return true;
            return false;
        }

        /// <summary>
        /// True if a fall from `from` to `to` went over a deadly cliff: the ground under its line, somewhere between
        /// where it left the snow and where it came down, is the foot of one (a flight can carry right across a
        /// terrace and come down on the slope beyond it).
        /// </summary>
        public static bool Over(Vector3 from, Vector3 to)
        {
            float d = new Vector2(to.x - from.x, to.z - from.z).magnitude;
            int n = Mathf.Clamp(Mathf.CeilToInt(d / 2f), 1, 128);
            for (int i = n; i >= 0; i--)   // (from the landing back: there, most likely)
            {
                float t = i / (float)n;
                if (Below(Mathf.Lerp(from.x, to.x, t), Mathf.Lerp(from.z, to.z, t))) return true;
            }
            return false;
        }
    }
}
