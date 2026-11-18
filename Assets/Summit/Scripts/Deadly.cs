using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Where a fall kills: the ground at the foot of the cliffs the mountain marks as ones to keep off. Anywhere else
    /// a big air landed on the board, down a snow slope, is ridden away from however tall it was (see Rider.Land);
    /// going over one of these and reaching the ground below is the end of the run (Rider.Land, RiderBody.FallStep).
    /// </summary>
    public static class Deadly
    {
        /// <summary>True on the ground below a deadly cliff, where a fall from its top ends.</summary>
        public static bool Below(float x, float z)
        {
            // the terrace under a deadly cliff (Cliffs), signed and taped off along its top: wherever its cut has
            // lowered the mountain
            if (Cliffs.WallCut(x, z) < -3f) return true;
            // a ridge's canyon on a cliff side (a one-sided ridge's snow side is a long ride down, not a fall)
            if (Ridge.InCanyons(z))
            {
                float a = x - Slope.Centre(z);
                if (Ridge.CliffSide(z, a) && Mathf.Abs(a) > Ridge.HalfWidth(z) + Ridge.Shoulder) return true;
            }
            // the valley under the summit's cliff, beyond the camp's cable railing
            if (z < -58f && x - Slope.Centre(Slope.PadLip) > Slope.SummitRight) return true;
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
