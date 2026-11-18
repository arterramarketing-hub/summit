using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Ridges: once a cycle the piste narrows onto a crest a few metres wide and the mountain falls away
    /// beside it, 165 m of rock wall down into a canyon. Some ridges have cliffs on both sides; others have a
    /// cliff on one side and a long, steep but rideable snow slope on the other, so there is a way off for the careful.
    /// It takes the halfpipe's old stretch of the cycle (the pipe is switched off) and a little of the cruise after it.
    ///
    /// The ridge is laid over the open mountain as a pure function of position, like the rest of it:
    ///   - the crest sits at the piste's own height on its centre line, so riding onto it changes nothing underfoot
    ///   - past the crest's edge a short rounded shoulder (so the edge never kinks under the board) rolls into the cliff
    ///   - the cliff runs down until it meets the canyon floor, which is the open mountain lowered by 165 m
    ///   - a couple of hundred metres out the canyon's far wall climbs back to the open mountain, inside what the
    ///     terrain draws round the rider, so its edge never shows
    /// The canyons outlast the crest: past its end the piste widens again along a spine between them, and they
    /// fill back in slowly, so a rider down on a canyon floor always has a way on downhill to rejoin the run.
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
        public const float CliffDepth = 165f, SafeDepth = 85f;   // how far the canyons sit below the crest
        public const float SafeSteep = 0.62f;   // the rideable side of a one-sided ridge (about 32 degrees)
        // the canyons fill back in from here to here (metres into the cycle, past its end): steadily, so the canyon
        // floor keeps falling the whole way (165 m over 380 m against the mountain's 0.72 is still 12 degrees downhill)
        public const float FadeFrom = 700f, FadeEnd = 1080f;
        // the canyon's far wall, measured out from the crest's centre line
        public const float FarIn = 168f, FarOut = 236f;

        // which cycle's ridge a point belongs to (its canyons reach on into the next cycle), and how far into that
        // cycle the point is
        static int Owner(float z) => Mathf.Repeat(z - Course.First, Course.Cycle) >= At ? Course.CycleOf(z) : Course.CycleOf(z) - 1;
        static float Local(float z) => z - (Course.First + Owner(z) * Course.Cycle);

        /// <summary>A ridge is built in a cycle unless it (or its canyons) would run into the clear stretch before the finish.</summary>
        public static bool Built(int cycle) => On && cycle >= 0 && Course.First + cycle * Course.Cycle + FadeEnd < Course.ClearFrom;

        /// <summary>True anywhere along a ridge's crest, ramps included (no kickers, drops or park features there).</summary>
        public static bool Has(float z)
        {
            if (!On || z < Course.First) return false;
            float u = Local(z);
            return u >= At && u < End && Built(Owner(z));
        }

        /// <summary>True anywhere the canyons beside a ridge reach, from its start to where they have filled in.</summary>
        public static bool InCanyons(float z)
        {
            if (!On || z < Course.First) return false;
            float u = Local(z);
            return u >= At && u < FadeEnd && Built(Owner(z));
        }

        /// <summary>0 off the ridge, 1 along its full crest, easing in and out at the ends.</summary>
        public static float Weight(float z)
        {
            if (!Has(z)) return 0f;
            float u = Local(z);
            float w = Mathf.Clamp01(Mathf.Min((u - At) / RampIn, (End - u) / RampOut));
            return w * w * (3f - 2f * w);
        }

        /// <summary>How deep the canyons are here, 0..1 of their full depth: plunging in with the crest, filling back in slowly.</summary>
        public static float Depth(float z)
        {
            if (!InCanyons(z)) return 0f;
            float u = Local(z);
            float a = Mathf.Clamp01((u - At) / RampIn);
            return Mathf.Min(a * a * (3f - 2f * a), Ease(Mathf.Clamp01((FadeEnd - u) / (FadeEnd - FadeFrom))));
        }

        // 0..1 at a nearly steady rate, rounded off at both ends: a smoothstep would climb half as steeply again
        // in the middle, and the canyon floor would rise faster than the mountain falls
        static float Ease(float s)
        {
            const float b = 0.15f, c = 1f / (1f - b);
            if (s < b) return c * s * s / (2f * b);
            if (s > 1f - b) return 1f - c * (1f - s) * (1f - s) / (2f * b);
            return c * (b * 0.5f + s - b);
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
            int s = Sides(Owner(z));
            return s == 0 || s == (side < 0f ? -1 : 1);
        }

        /// <summary>Half the rideable top at z: the full piste narrowing down to the crest.</summary>
        public static float HalfWidth(float z) => Mathf.Lerp(Slope.PisteHalf, CrestHalf, Weight(z));

        /// <summary>How steep the cliff is here (rise over run): buttresses and slabs from about 65 to 74 degrees.</summary>
        static float Steep(float z, float side) => 2.2f + 1.2f * Slope.Noise(z * 0.03f + side * 17.3f, 5.5f);

        /// <summary>How far below the crest the canyon floor is on this side, in metres, here.</summary>
        public static float DepthHere(float z, float side) => (CliffSide(z, side) ? CliffDepth : SafeDepth) * Depth(z);

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
            float wd = Depth(z);
            if (wd <= 0f) return y;
            float w = Weight(z);
            float a = x - Slope.Centre(z), d = Mathf.Abs(a), side = a < 0f ? -1f : 1f;
            bool cliff = CliffSide(z, side);
            float half = Mathf.Lerp(Slope.PisteHalf, CrestHalf, w);
            float steep = cliff ? Steep(z, side) : SafeSteep;
            float t = Mathf.Min(d, half) / half;
            float e = d - half;
            // the top: the crest along the ridge, and past its end the piste itself, along the spine between the canyons
            float top = Mathf.Lerp(y, Top(z) - Crown * t * t, w);
            float ridge = top - Drop(e, steep);
            // the rock is broken into buttresses and ledges (none on the crest or its shoulder), ribs and gullies
            // running down it (they vary along the ridge, not down the face), and on a wall this tall, great slabs
            // and bays a stone's throw across
            if (cliff && e > Shoulder)
                ridge += Mathf.Clamp01((e - Shoulder) / 4f) * ((Slope.Noise(x * 0.16f + 31f, z * 0.11f) - 0.5f) * 4f
                                                            + (Slope.Noise(z * 0.15f + side * 9.7f, e * 0.04f + 3.1f) - 0.5f) * 5f)
                       + Mathf.Clamp01((e - Shoulder) / 14f) * (Slope.Noise(z * 0.035f + side * 4.3f, e * 0.025f + 8.8f) - 0.5f) * 22f;
            // the canyon floor: the open mountain, lowered, with its far wall climbing back up to it
            float depth = (cliff ? CliffDepth : SafeDepth) * wd * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - FarIn) / (FarOut - FarIn))));
            // (a shallow canyon gets a tighter foot: SmoothMax of two equal heights would raise them a quarter of k)
            return SmoothMax(ridge, y - depth, Mathf.Min(3f, 0.3f * depth + 0.01f));
        }

        /// <summary>Into how many strips across the slope a quad on the crest's rounded edge is cut (see Fine).</summary>
        public const int Split = 4;

        /// <summary>
        /// Is terrain quad (i, j) of the world grid (Slope.Quad apart) on the rounded edge of the crest? The edge
        /// bends too sharply for 1.5 m facets: one sags up to 40 cm under the curve, enough to throw the board at
        /// speed (or, ridden on the curve instead, to leave the rider hovering over what is drawn). So the terrain
        /// cuts these quads into Split strips across the slope (SlopeStreamer), and the rider stands on exactly
        /// those strips (MeshHeight), as everywhere else on the mountain: they bend a quarter as sharply, well
        /// inside what the board rides over without leaving the snow.
        /// </summary>
        public static bool Fine(int i, int j)
        {
            float q = Slope.Quad;
            return Across(i * q, j * q) || Across(i * q, (j + 1) * q);
        }

        /// <summary>
        /// Height of the drawn strips at (x, z), in a quad that is Fine. A strip's corners on a row of the grid sit
        /// on the true curve where the quads either side of that row are both cut; where one is whole they sit on
        /// its straight edge, so the two meet without a crack (SlopeStreamer builds them by the same rule).
        /// Each strip is split corner to corner the same way as a whole quad.
        /// </summary>
        public static float MeshHeight(float x, float z)
        {
            float q = Slope.Quad, gx = x / q, gz = z / q;
            int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
            int at = Corners(i, j);
            float u = (gx - i) * Split, v = gz - j;
            int s = Mathf.Clamp((int)u, 0, Split - 1);
            u -= s;
            float ha = cornerH[at + s], hb = cornerH[at + s + 1], hc = cornerH[at + Split + 1 + s], hd = cornerH[at + Split + 2 + s];
            if (u + v <= 1f) return ha + (hb - ha) * u + (hc - ha) * v;
            return hd + (hc - hd) * (1f - u) + (hb - hd) * (1f - v);
        }

        // the strips' corner heights of the last few dozen quads asked about (the rider, the snow layer and the
        // dressing ask about the same few quads over and over): lower row, then upper, Split + 1 each
        const int Cached = 64;
        static readonly int[] cacheI = new int[Cached], cacheJ = new int[Cached], cacheSeed = new int[Cached];
        static readonly bool[] cacheSet = new bool[Cached];
        static readonly float[] cornerH = new float[Cached * 2 * (Split + 1)];

        static int Corners(int i, int j)
        {
            int c = ((i * 73856093) ^ (j * 19349663)) & (Cached - 1);
            int at = c * 2 * (Split + 1);
            if (cacheSet[c] && cacheI[c] == i && cacheJ[c] == j && cacheSeed[c] == Slope.Seed) return at;
            Row(i, j, Fine(i, j - 1), at);
            Row(i, j + 1, Fine(i, j + 1), at + Split + 1);
            cacheI[c] = i; cacheJ[c] = j; cacheSeed[c] = Slope.Seed; cacheSet[c] = true;
            return at;
        }

        // the corners along quad column i's edge on grid row j, given whether the quad on its far side is cut too
        static void Row(int i, int j, bool bothCut, int at)
        {
            float q = Slope.Quad, x0 = i * q, z = j * q;
            float h0 = Slope.Height(x0, z), h1 = Slope.Height(x0 + q, z);
            cornerH[at] = h0; cornerH[at + Split] = h1;
            for (int s = 1; s < Split; s++)
                cornerH[at + s] = bothCut ? Slope.Height(x0 + s * (q / Split), z) : Mathf.Lerp(h0, h1, s / (float)Split);
        }

        // does the quad edge from x to x + Quad at z cross the rounded edge (with a little to spare either side)?
        // Both of a quad's edges are tested, so the two quads either side of an edge that crosses it are both cut.
        static bool Across(float x, float z)
        {
            // (along the spine past the crest's end too: the piste's edge rolls over into the canyons just as sharply)
            if (Depth(z) <= 0.01f) return false;
            float half = Mathf.Lerp(Slope.PisteHalf, CrestHalf, Weight(z));
            float lo = half - 0.5f, hi = half + Shoulder + 0.75f;
            float a0 = x - Slope.Centre(z), a1 = a0 + Slope.Quad;
            return (a1 > lo && a0 < hi) || (a1 > -hi && a0 < -lo);
        }

        /// <summary>How far out from the crest's centre line the cliff (or side slope) reaches before the canyon floor.</summary>
        public static float Reach(float z, float side)
        {
            bool cliff = CliffSide(z, side);
            return HalfWidth(z) + Shoulder + DepthHere(z, side) / (cliff ? Steep(z, side) : SafeSteep) + 3f;
        }

        /// <summary>True on the crest or its cliffs and side slopes, and the canyons' far walls (no trees, rocks or pickups there).</summary>
        public static bool Keep(float x, float z, float margin)
        {
            float a = x - Slope.Centre(z), d = Mathf.Abs(a);
            // (a little extra: trees are tested at their cell's centre and then nudged up to a few metres off it)
            if (InCanyons(z))
            {
                float side = a < 0f ? -1f : 1f;
                if (d < Reach(z, side) + margin + 3f) return true;
                if (DepthHere(z, side) > 8f && d > FarIn - 10f - margin && d < FarOut + 4f + margin) return true;
            }
            // and a clear piste onto the crest and off its end: you come off it locked onto the middle
            if (Has(z + 15f) || Has(z - 50f)) return d < Slope.PisteHalf + margin + 3f;
            return false;
        }

        /// <summary>How far short of Reach the rocks on a cliff stop (RidgeDressing), so where you get up is clear.</summary>
        public const float Clear = 4f;

        /// <summary>
        /// Somewhere to stand after a fall: off the rock, onto the nearest snow that is not too steep (normally the
        /// gully floor below, since the crest is a long way back up).
        /// </summary>
        public static float SafeX(float x, float z)
        {
            if (Depth(z) <= 0f) return x;
            float a = x - Slope.Centre(z), d = Mathf.Abs(a);
            float half = HalfWidth(z);
            if (d < half) return x;   // on the crest itself
            float dir = a < 0f ? -1f : 1f;
            // anywhere on a cliff, get up past its foot: a ledge part way down would only throw you off again, and
            // the rocks on the face and piled at its foot all stop short of there. Only a fall at the very top of the
            // edge, onto snow you can stand on, gets up where it is.
            if (CliffSide(z, dir) && d < Reach(z, dir) && (d > half + Shoulder * 0.5f || Slope.Normal(x, z).y <= 0.8f))
                x = Slope.Centre(z) + dir * Reach(z, dir);
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
