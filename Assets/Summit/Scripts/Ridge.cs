using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Ridges: once a cycle the piste narrows onto a crest a few metres wide and the mountain falls away
    /// beside it into cliffs and steep gullies. Some ridges have cliffs on both sides; others have a cliff on
    /// one side and a steep but rideable snow slope on the other, so there is a way off for the careful.
    /// It takes the halfpipe's old stretch of the cycle (the pipe is switched off) and a little of the cruise after it.
    ///
    /// The ridge is laid over the open mountain as a pure function of position, like the rest of it:
    ///   - the crest sits at the piste's own height on its centre line, so riding onto it changes nothing underfoot
    ///   - past the crest's edge a short rounded shoulder (so the edge never kinks under the board) rolls into the cliff
    ///   - the cliff runs down until it meets the gully floor, which is the open mountain lowered by a few tens of metres
    ///   - far off to the side the gullies climb back out to the open mountain
    /// Everything eases in and out along the run, so the piste visibly funnels down onto the crest.
    /// </summary>
    public static class Ridge
    {
        public const bool On = true;
        // where the ridge stretch begins and ends within a cycle
        public const float At = Course.PipeAt, End = 840f;
        // the funnel in, and the long climb back out (long, so the gully floor never tilts back uphill)
        public const float RampIn = 80f, RampOut = 110f;
        public const float CrestHalf = 4f;      // half the crest's width: still rideable, but you have to hold your line
        public const float Shoulder = 2f;       // the rounded edge between the crest and the cliff
        public const float Crown = 0.3f;        // the crest is a touch higher in the middle than at its edges
        public const float CliffDepth = 34f, SafeDepth = 18f;   // how far the gullies sit below the crest
        public const float SafeSteep = 0.62f;   // the rideable side of a one-sided ridge (about 32 degrees)

        static float Local(float z) => Mathf.Repeat(z - Course.First, Course.Cycle);

        /// <summary>A ridge is built in a cycle unless it would run into the clear stretch before the finish.</summary>
        public static bool Built(int cycle) => On && cycle >= 0 && Course.First + cycle * Course.Cycle + End < Course.ClearFrom;

        /// <summary>True anywhere along a ridge's stretch, ramps included (no kickers, drops or park features there).</summary>
        public static bool Has(float z)
        {
            if (!On || z < Course.First) return false;
            float u = Local(z);
            return u >= At && u < End && Built(Course.CycleOf(z));
        }

        /// <summary>0 off the ridge, 1 along its full crest, easing in and out at the ends.</summary>
        public static float Weight(float z)
        {
            if (!Has(z)) return 0f;
            float u = Local(z);
            float w = Mathf.Clamp01(Mathf.Min((u - At) / RampIn, (End - u) / RampOut));
            return w * w * (3f - 2f * w);
        }

        /// <summary>Which side the cliffs are on: 0 both, -1 left only, +1 right only.</summary>
        public static int Sides(int cycle)
        {
            float h = Slope.Hash01(cycle, 5, 61);
            return h < 0.5f ? 0 : h < 0.75f ? -1 : 1;
        }

        /// <summary>True when the drop on this side of the crest (side = -1 left, +1 right) is a cliff.</summary>
        public static bool CliffSide(float z, float side)
        {
            int s = Sides(Course.CycleOf(z));
            return s == 0 || s == (side < 0f ? -1 : 1);
        }

        /// <summary>Half the rideable top at z: the full piste narrowing down to the crest.</summary>
        public static float HalfWidth(float z) => Mathf.Lerp(Slope.PisteHalf, CrestHalf, Weight(z));

        /// <summary>How steep the cliff is here (rise over run): buttresses and slabs from about 54 to 66 degrees.</summary>
        static float Steep(float z, float side) => 1.4f + 0.8f * Slope.Noise(z * 0.03f + side * 17.3f, 5.5f);

        /// <summary>
        /// Height of the crest's centre line: the piste's own height there, less the shortest of the fall line's
        /// rollers and half the next (see Slope.FallLine). Those are a fun little float on the open piste, but on a crest a few metres
        /// wide at full speed they would lift you off it.
        /// </summary>
        public static float Top(float z)
        {
            float c = Slope.Centre(z);
            return Slope.FallLine(z) - 0.8f * (Slope.Noise(z * 0.09f, 41.3f) - 0.5f) - 1.3f * (Slope.Noise(z * 0.037f, 29.1f) - 0.5f) + (Slope.Noise(c * 0.011f + 50f, z * 0.009f) - 0.5f) * 7f;
        }

        /// <summary>How far down past the crest's edge the cliff or side slope has dropped, at e metres past the edge.</summary>
        static float Drop(float e, float steep)
        {
            if (e <= 0f) return 0f;
            // a rounded shoulder (the gradient builds over its width), then the straight face
            return e < Shoulder ? steep * e * e / (2f * Shoulder) : steep * (e - Shoulder * 0.5f);
        }

        /// <summary>Smooth maximum: the foot of the cliff curves into the gully floor instead of creasing.</summary>
        static float SmoothMax(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
            return Mathf.Lerp(b, a, h) + k * h * (1f - h);
        }

        /// <summary>The mountain with the ridge laid over it, given the open mountain's height y at (x, z).</summary>
        public static float Shape(float x, float z, float y)
        {
            float w = Weight(z);
            if (w <= 0f) return y;
            float a = x - Slope.Centre(z), d = Mathf.Abs(a), side = a < 0f ? -1f : 1f;
            bool cliff = CliffSide(z, side);
            float half = Mathf.Lerp(Slope.PisteHalf, CrestHalf, w);
            float steep = cliff ? Steep(z, side) : SafeSteep;
            float t = Mathf.Min(d, half) / half;
            float e = d - half;
            float ridge = Top(z) - Crown * t * t - Drop(e, steep);
            // the rock is broken into buttresses and ledges (none on the crest or its shoulder)
            // and ribs and gullies running down it (they vary along the ridge, not down the face)
            if (cliff && e > Shoulder)
                ridge += Mathf.Clamp01((e - Shoulder) / 4f) * ((Slope.Noise(x * 0.16f + 31f, z * 0.11f) - 0.5f) * 4f
                                                            + (Slope.Noise(z * 0.15f + side * 9.7f, e * 0.04f + 3.1f) - 0.5f) * 5f);
            // the gully floor: the open mountain, lowered, climbing back out to it far to the side
            float depth = (cliff ? CliffDepth : SafeDepth) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 70f) / 100f)));
            float r = SmoothMax(ridge, y - depth, 3f);
            return Mathf.Lerp(y, r, w);
        }

        /// <summary>
        /// On the rounded edge of the crest, where the rider stands on the true surface rather than the drawn mesh
        /// (as in the halfpipe): its 1.5 m facets bend sharply enough there to jolt the board at speed, and the
        /// mesh sits only a few centimetres off the curve.
        /// </summary>
        public static bool OnShoulder(float x, float z)
        {
            float w = Weight(z);
            if (w <= 0.01f) return false;
            float d = Mathf.Abs(x - Slope.Centre(z)), half = Mathf.Lerp(Slope.PisteHalf, CrestHalf, w);
            return d > half - 1.5f && d < half + Shoulder + 1.5f;
        }

        /// <summary>How far out from the crest's centre line the cliff (or side slope) reaches before the gully floor.</summary>
        public static float Reach(float z, float side)
        {
            bool cliff = CliffSide(z, side);
            return HalfWidth(z) + Shoulder + (cliff ? CliffDepth / Steep(z, side) : SafeDepth / SafeSteep) + 3f;
        }

        /// <summary>
        /// Gone over the edge: past the crest's rideable shoulder on the cliff side, on rock too steep to ride (or in
        /// the air over it and already dropping below the crest). From here it is a tumble down the cliff, not a
        /// ride: a fall, not the end of the run.
        /// </summary>
        public static bool OverEdge(Vector3 p, bool airborne)
        {
            float w = Weight(p.z);
            if (w < 0.4f) return false;
            float a = p.x - Slope.Centre(p.z), d = Mathf.Abs(a), side = a < 0f ? -1f : 1f;
            if (!CliffSide(p.z, side)) return false;
            float half = HalfWidth(p.z), r = Reach(p.z, side);
            if (d < half + Shoulder * 0.5f || d > r - 3f) return false;
            // riding the gully into the foot of the cliff is a bump against a wall, not a fall from the top: only
            // the upper half of the cliff counts (you go over at the top, so that is where a fall starts)
            if (p.y < Top(p.z) - 0.5f * CliffDepth * w) return false;
            if (p.y < Slope.Height(Slope.Centre(p.z) + side * r, p.z) + 4f) return false;
            if (Slope.Normal(p.x, p.z).y >= 0.62f) return false;
            // a hop that drifts out over the edge and comes back is not a fall; dropping past the crest is
            return !airborne || p.y < Top(p.z) - 1.5f * w;
        }

        /// <summary>True on the crest or its cliffs and side slopes (no trees, rocks or pickups there).</summary>
        public static bool Keep(float x, float z, float margin)
        {
            float a = x - Slope.Centre(z);
            // (a little extra: trees are tested at their cell's centre and then nudged up to a few metres off it)
            if (Has(z)) return Mathf.Abs(a) < Reach(z, a < 0f ? -1f : 1f) + margin + 3f;
            // and a clear piste onto the crest and off its end: you come off it locked onto the middle
            if (Has(z + 15f) || Has(z - 50f)) return Mathf.Abs(a) < Slope.PisteHalf + margin + 3f;
            return false;
        }

        /// <summary>
        /// Somewhere to stand after a fall: off the rock, onto the nearest snow that is not too steep (normally the
        /// gully floor below, since the crest is a long way back up).
        /// </summary>
        public static float SafeX(float x, float z)
        {
            if (Weight(z) <= 0f || Slope.Normal(x, z).y > 0.8f) return x;
            float a = x - Slope.Centre(z);
            float half = HalfWidth(z);
            if (Mathf.Abs(a) < half) return x;   // on the crest itself
            float dir = a < 0f ? -1f : 1f;
            // beside a cliff, start past its foot: a ledge part way down would only throw you off again
            if (CliffSide(z, dir)) x = Slope.Centre(z) + dir * Mathf.Max(Mathf.Abs(a), Reach(z, dir));
            if (Slope.Normal(x, z).y > 0.8f) return x;
            for (int i = 1; i <= 40; i++)
            {
                float xi = x + dir * i * 1.5f;
                if (Slope.Normal(xi, z).y > 0.8f) return xi;
            }
            return x;
        }

        /// <summary>A sideways spot on the crest for something placed along the piste (a pickup): offset scaled to fit.</summary>
        public static float OnCrest(float z, float offset)
        {
            float half = HalfWidth(z) - 1.5f;
            return Slope.Centre(z) + Mathf.Clamp(offset, -1f, 1f) * Mathf.Max(0f, half);
        }
    }
}
