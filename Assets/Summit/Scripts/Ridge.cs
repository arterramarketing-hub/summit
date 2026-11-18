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
    ///
    /// Valleys are cut into the mountain: the run sinks into it, down a long steepening ramp, onto a floor a couple
    /// of dozen metres wide 40 to 52 m below the ground either side, and climbs back out as the gorge ends. The
    /// ground beyond the rims is the mountain's own, trees and all, so a gorge sits in the world like a trench, not
    /// a pair of towers. Its sides are rock walls from the floor to the rim (or, on a one-sided valley's other side,
    /// a steep snow bank). Ride into a wall and it is rock: a tumble back down to the floor.
    ///
    /// Which a stretch gets is drawn for each one from the day's mountain (FormOf): a ridge (cliffs both sides,
    /// or a cliff and a snow slope), a valley (walls both sides, or one), a ledge (a cliff on one side and a wall
    /// on the other), or nothing at all. How deep or tall, how long and how steep varies from one to the next.
    /// </summary>
    public static class Ridge
    {
        public const bool On = true;
        // where the ridge stretch begins within a cycle, and the latest it ends (each one ends at its own EndOf)
        public const float At = Course.PipeAt, End = 850f;
        // the funnel in, and the long climb back out (long, so the gully floor never tilts back uphill)
        public const float RampIn = 80f, RampOut = 110f;
        public const float CrestHalf = 4f;      // half the crest's width: still rideable, but you have to hold your line
        public const float Shoulder = 2f;       // the rounded edge between the crest and the cliff
        public const float Crown = 0.3f;        // the crest is a touch higher in the middle than at its edges
        public const float CliffDepth = 165f, SafeDepth = 85f;   // how far the canyons sit below the crest
        public const float SafeSteep = 0.62f;   // the rideable side of a one-sided ridge (about 32 degrees)
        // the canyons fill back in from this far before the crest's end to this far after it: steadily, so the
        // canyon floor keeps falling the whole way (198 m at most over 380 m, against the mountain's 0.72, is still
        // about 10 degrees downhill)
        public const float FadeBefore = 140f, FadeAfter = 240f;
        /// <summary>The latest any stretch's canyons (or walls) reach into the cycle, for loops over them (each stops at its own).</summary>
        public const float FadeEnd = End + FadeAfter;
        // the canyon's far wall, measured out from the crest's centre line
        public const float FarIn = 168f, FarOut = 236f;

        // which cycle's ridge a point belongs to (its canyons reach on into the next cycle), and how far into that
        // cycle the point is
        static int Owner(float z) => Mathf.Repeat(z - Course.First, Course.Cycle) >= At ? Course.CycleOf(z) : Course.CycleOf(z) - 1;
        static float Local(float z) => z - (Course.First + Owner(z) * Course.Cycle);

        // ---------------------------------------------------------------- what each stretch gets

        /// <summary>What a stretch is: nothing, a ridge (cliffs both sides, or one and a snow slope), a valley (walls both sides, or one), or a ledge.</summary>
        public enum Form { Plain, Ridge, RidgeOneSided, Valley, ValleyOneSided, Ledge }
        /// <summary>What lies beside the run on one side: nothing, a cliff down, a long snow slope down, a wall up (to the ground beside a gorge), or a steep snow bank up.</summary>
        public enum Side { Plain, Cliff, Snow, Wall, Bank }

        /// <summary>The stretch a cycle gets: drawn from the day's mountain, the same every time for that cycle.</summary>
        public static Form FormOf(int cycle)
        {
            float h = Slope.Hash01(cycle, 5, 61);
            return h < 0.1f ? Form.Plain : h < 0.32f ? Form.Ridge : h < 0.45f ? Form.RidgeOneSided
                 : h < 0.65f ? Form.Valley : h < 0.78f ? Form.ValleyOneSided : Form.Ledge;
        }

        /// <summary>What lies on side `side` (-1 left, +1 right) of a cycle's stretch.</summary>
        public static Side SideOf(int cycle, float side)
        {
            bool odd = (side < 0f ? -1 : 1) == (Slope.Hash01(cycle, 6, 61) < 0.5f ? -1 : 1);   // the side that differs, if one does
            switch (FormOf(cycle))
            {
                case Form.Ridge: return Side.Cliff;
                case Form.RidgeOneSided: return odd ? Side.Cliff : Side.Snow;
                case Form.Valley: return Side.Wall;
                case Form.ValleyOneSided: return odd ? Side.Wall : Side.Bank;
                case Form.Ledge: return odd ? Side.Cliff : Side.Wall;
                default: return Side.Plain;
            }
        }

        /// <summary>
        /// Which sides of a cycle's stretch are cliffs, as the tools have always asked it: 0 both, -1 left only,
        /// +1 right only, 2 neither (a valley, or nothing). See SideOf for the whole story.
        /// </summary>
        public static int Sides(int cycle)
        {
            bool l = SideOf(cycle, -1f) == Side.Cliff, r = SideOf(cycle, 1f) == Side.Cliff;
            return l && r ? 0 : l ? -1 : r ? 1 : 2;
        }

        /// <summary>
        /// Snow however steep it looks: a one-sided ridge's long snow side (32 degrees across and the mountain's own
        /// fall down it puts it right at the line where the terrain starts drawing rock), and a wall's snow apron.
        /// </summary>
        public static bool SnowAt(float x, float z)
        {
            if (Depth(z) <= 0.05f) return false;
            float a = x - Slope.Centre(z), side = a < 0f ? -1f : 1f;
            var k = SideAt(z, side);
            if (k == Side.Snow) return true;
            if (k == Side.Bank) return Mathf.Abs(a) < Reach(z, side);
            return k == Side.Wall && Mathf.Abs(a) < HalfWidth(z) + Shoulder * 0.5f + Apron(x, z, side) * 0.8f;
        }

        /// <summary>What lies on this side of the run here.</summary>
        public static Side SideAt(float z, float side) => SideOf(Owner(z), side);

        // how deep (or tall) this stretch's rock runs, against the standard 165 m: 124 to 198 m
        static float Scale(int cycle) => 0.75f + 0.45f * Slope.Hash01(cycle, 7, 61);
        // how steep its rock is: some stretches sheer, some merely very steep
        static float SteepBase(int cycle) => 1.9f + 0.7f * Slope.Hash01(cycle, 9, 61);

        /// <summary>Where a cycle's crest (or floor) ends, metres into the cycle: they run for 260 to 330 m.</summary>
        public static float EndOf(int cycle) => 780f + 70f * Slope.Hash01(cycle, 8, 61);

        // half the rideable top along the full stretch: a crest, a valley's floor, or a ledge
        static float HalfOf(int cycle)
        {
            switch (FormOf(cycle))
            {
                case Form.Valley: return 9f + 4f * Slope.Hash01(cycle, 10, 61);
                case Form.ValleyOneSided: return 11f + 4f * Slope.Hash01(cycle, 10, 61);
                case Form.Ledge: return 5.5f;
                default: return CrestHalf;
            }
        }

        // a crest is a touch higher in the middle; a valley's floor a touch lower, so it draws you to the middle
        static float CrownOf(int cycle) { var f = FormOf(cycle); return f == Form.Valley || f == Form.ValleyOneSided ? -0.6f : f == Form.Ledge ? 0f : Crown; }

        /// <summary>A stretch is built in a cycle unless it is plain, or it (or its canyons) would run into the clear stretch before the finish.</summary>
        public static bool Built(int cycle) => On && cycle >= 0 && Course.First + cycle * Course.Cycle + EndOf(cycle) + FadeAfter < Course.ClearFrom && FormOf(cycle) != Form.Plain;

        /// <summary>True anywhere along a ridge's crest (or a valley's floor), ramps included (no kickers, drops or park features there).</summary>
        public static bool Has(float z)
        {
            if (!On || z < Course.First) return false;
            int c = Owner(z);
            float u = Local(z);
            return u >= At && u < EndOf(c) && Built(c);
        }

        /// <summary>True anywhere the canyons (or walls) beside a stretch reach, from its start to where they have gone.</summary>
        public static bool InCanyons(float z)
        {
            if (!On || z < Course.First) return false;
            int c = Owner(z);
            float u = Local(z);
            return u >= At && u < EndOf(c) + FadeAfter && Built(c);
        }

        /// <summary>0 off the ridge, 1 along its full crest, easing in and out at the ends.</summary>
        public static float Weight(float z)
        {
            if (!Has(z)) return 0f;
            float u = Local(z);
            float w = Mathf.Clamp01(Mathf.Min((u - At) / RampIn, (EndOf(Owner(z)) - u) / RampOut));
            return w * w * (3f - 2f * w);
        }

        /// <summary>How deep the canyons (or tall the walls) are here, 0..1 of their full size: in with the crest, gone again slowly.</summary>
        public static float Depth(float z)
        {
            if (!InCanyons(z)) return 0f;
            float u = Local(z), end = EndOf(Owner(z)) + FadeAfter;
            float a = Mathf.Clamp01((u - At) / RampIn);
            return Mathf.Min(a * a * (3f - 2f * a), Ease(Mathf.Clamp01((end - u) / (FadeBefore + FadeAfter))));
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

        /// <summary>True when the drop on this side of the crest (side = -1 left, +1 right) is a cliff.</summary>
        public static bool CliffSide(float z, float side) => SideAt(z, side) == Side.Cliff;

        /// <summary>True when a rock wall rises on this side of the run.</summary>
        public static bool WallSide(float z, float side) => SideAt(z, side) == Side.Wall;

        /// <summary>Half the rideable top at z: the full piste narrowing down to the crest (or the valley's floor).</summary>
        public static float HalfWidth(float z) => Mathf.Lerp(Slope.PisteHalf, HalfOf(Owner(z)), Weight(z));

        /// <summary>How steep the rock is here (rise over run): buttresses and slabs from about 62 to 75 degrees.</summary>
        static float Steep(float z, float side) => SteepBase(Owner(z)) + 1.2f * Slope.Noise(z * 0.03f + side * 17.3f, 5.5f);

        /// <summary>How far below the crest the canyon floor is on this side (or how far above the floor the wall's top), in metres, here.</summary>
        public static float DepthHere(float z, float side)
        {
            var k = SideAt(z, side);
            if (k == Side.Plain) return 0f;
            if (k == Side.Wall || k == Side.Bank) return Cut(z);   // up to the ground beside the gorge
            // (a ledge's cliff drops from the sunk path, so it is shallower by the cut)
            return Mathf.Max(0f, (k == Side.Snow ? SafeDepth : CliffDepth) * Scale(Owner(z)) * Depth(z) - Cut(z));
        }

        // how deep a stretch's gorge is cut (valleys and ledges; a ridge is not cut at all)
        static float CutOf(int cycle)
        {
            var f = FormOf(cycle);
            return f == Form.Valley || f == Form.ValleyOneSided || f == Form.Ledge ? 40f + 12f * Slope.Hash01(cycle, 11, 61) : 0f;
        }
        // the path ramps down into the gorge over this far from the stretch's start (at most about a quarter steeper
        // than the mountain: 43 degrees), and back out over this far, ending this far past its end (never level, never uphill)
        const float CutIn = 285f, CutOut = 190f, CutPast = 170f;

        /// <summary>How far the run has sunk into the mountain here, in metres (0 outside a valley or ledge).</summary>
        public static float Cut(float z)
        {
            if (!InCanyons(z)) return 0f;
            int c = Owner(z);
            float depth = CutOf(c);
            if (depth <= 0f) return 0f;
            float u = Local(z);
            float a = Ease(Mathf.Clamp01((u - At) / CutIn)), b = Ease(Mathf.Clamp01((EndOf(c) + CutPast - u) / CutOut));
            // the lesser of the two, rounded where they cross (a plain minimum kinks there: a jolt underfoot)
            const float k = 0.15f;
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return depth * Mathf.Max(0f, Mathf.Min(a, b) - h * h * k * 0.25f);
        }

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
            int owner = Owner(z);
            float a = x - Slope.Centre(z), d = Mathf.Abs(a), side = a < 0f ? -1f : 1f;
            var kind = SideOf(owner, side);
            bool cliff = kind == Side.Cliff;
            float half = Mathf.Lerp(Slope.PisteHalf, HalfOf(owner), w);
            float t = Mathf.Min(d, half) / half;
            float e = d - half;
            // the top: the crest along the ridge (or the valley's floor), and past its end the piste itself; in a
            // gorge, sunk into the mountain
            float cut = Cut(z);
            float top = Mathf.Lerp(y, Top(z) - CrownOf(owner) * t * t, w) - cut;
            // nothing this side: the top eases back into the open mountain past its edge
            if (kind == Side.Plain) return Mathf.Lerp(top, y, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 10f)));
            float steep = kind == Side.Snow ? SafeSteep : Steep(z, side);
            float far = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - FarIn) / (FarOut - FarIn)));
            if (kind == Side.Wall || kind == Side.Bank)
            {
                // a gorge's side, climbing from the sunk floor until it meets the mountain's own ground, which goes on
                // beyond the rim untouched (a rounded, snowy edge, then the drop).
                // A wall is rock: at its foot an apron of snow (avalanche debris and drift) as wide as the snow has
                // made it here, so the snow meets the rock along a wandering line, not the grid's; then the face,
                // climbing in strata. A bank is a steep snow slope, rideable back up to the rim.
                float side2;
                if (kind == Side.Bank) side2 = top + Drop(e, SafeSteep);
                else
                {
                    float apron = Apron(x, z, side);
                    side2 = top + Drop(e, ApronSteep) + Drop(e - apron, steep - ApronSteep) + Strata(e - apron, steep, z, side)
                          + Buttresses(z, e - apron, side) + Drifts(x, z, e, apron);
                }
                return -SmoothMax(-side2, -y, Mathf.Min(3f, 0.3f * cut + 0.01f));
            }
            float ridge = top - Drop(e, steep);
            // the rock is broken into buttresses and ledges (none on the crest or its shoulder), ribs and gullies
            // running down it (they vary along the ridge, not down the face), and on a wall this tall, great slabs
            // and bays a stone's throw across
            if (cliff) ridge += Rough(x, z, e, side);
            // the canyon floor: the open mountain, lowered, with its far wall climbing back up to it
            float depth = (cliff ? CliffDepth : SafeDepth) * Scale(owner) * wd * far;
            // (a shallow canyon gets a tighter foot: SmoothMax of two equal heights would raise them a quarter of k;
            // the snow side's foot is a long runout onto the canyon floor, ridden out at speed, not a crease)
            return SmoothMax(ridge, y - depth, Mathf.Min(kind == Side.Snow ? 14f : 3f, 0.3f * depth + 0.01f));
        }

        // a wall's snow apron: about 35 degrees, rideable (a fair way to find the wall), 1.5 to 8.5 m wide
        const float ApronSteep = 0.7f;
        static float Apron(float x, float z, float side) => 1.5f + 7f * Slope.Noise(z * 0.07f + side * 5.3f, 2.9f);

        // lumps of drifted snow on the apron, petering out up the face, so its edge is broken still further
        static float Drifts(float x, float z, float e, float apron)
        {
            if (e <= 0f) return 0f;
            float k = Mathf.Clamp01(e / 2f) * Mathf.Clamp01(1f - (e - apron) / 4f);
            return k * (Slope.Noise(x * 0.35f + 4.1f, z * 0.35f) - 0.5f) * 1.6f;
        }

        // A wall's face climbs in steps: ledges every 10 to 20 m of height, near enough level to hold snow (the
        // terrain draws any face flatter than about 46 degrees as snow: see RockFace), between near-sheer bands of
        // rock. The steps wander along the wall, as strata do. (Not on a ridge's cliffs, which must not be a stair
        // a rider could hop down.)
        static float Strata(float e, float steep, float z, float side)
        {
            if (e <= 0f) return 0f;
            const float a = 0.86f;   // the face's slope swings between steep * (1 - a) and steep * (1 + a)
            float period = 8f + 4f * Slope.Noise(z * 0.02f + side * 7.7f, 0.3f);
            float phase = 1.6f * Slope.Noise(z * 0.045f + side * 2.1f, 6.6f);
            return -steep * a * period / (2f * Mathf.PI) * Mathf.Sin(2f * Mathf.PI * (e / period + phase)) * Mathf.Clamp01(e / 3f);
        }

        // a wall's relief: great buttresses and bays along it, which move the face in and out without tilting its
        // strata (relief that varied up the face would break the ledges, and their snow, into scraps)
        static float Buttresses(float z, float e, float side)
        {
            if (e <= 0f) return 0f;
            return Mathf.Clamp01(e / 14f) * ((Slope.Noise(z * 0.035f + side * 4.3f, 8.8f) - 0.5f) * 20f
                                            + (Slope.Noise(z * 0.12f + side * 1.9f, 3.3f) - 0.5f) * 4f);
        }

        // the rock's relief, past the rounded edge (or foot)
        static float Rough(float x, float z, float e, float side)
        {
            if (e <= Shoulder) return 0f;
            return Mathf.Clamp01((e - Shoulder) / 4f) * ((Slope.Noise(x * 0.16f + 31f, z * 0.11f) - 0.5f) * 4f
                                                      + (Slope.Noise(z * 0.15f + side * 9.7f, e * 0.04f + 3.1f) - 0.5f) * 5f)
                 + Mathf.Clamp01((e - Shoulder) / 14f) * (Slope.Noise(z * 0.035f + side * 4.3f, e * 0.025f + 8.8f) - 0.5f) * 22f;
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

        /// <summary>How far out from the crest's centre line the cliff (or side slope, or wall) reaches before the canyon floor (or the wall's top).</summary>
        public static float Reach(float z, float side)
        {
            var k = SideAt(z, side);
            if (k == Side.Plain) return HalfWidth(z) + 10f;
            // (a wall also has its snow apron, and the ground at its rim stands a little above the floor's own line)
            if (k == Side.Wall) return HalfWidth(z) + Shoulder + 8.5f + (DepthHere(z, side) * 1.15f + 3f) / Steep(z, side) + 3f;
            if (k == Side.Bank) return HalfWidth(z) + Shoulder + (DepthHere(z, side) * 1.15f + 3f) / SafeSteep + 3f;
            return HalfWidth(z) + Shoulder + DepthHere(z, side) / (k == Side.Snow ? SafeSteep : Steep(z, side)) + 3f;
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
                var k = SideAt(z, side);
                if ((k == Side.Cliff || k == Side.Snow) && DepthHere(z, side) > 8f && d > FarIn - 10f - margin && d < FarOut + 4f + margin) return true;   // a canyon's far wall
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
            // up against a gorge's wall (or on its bank): back down on its floor
            if (WallSide(z, dir) || SideAt(z, dir) == Side.Bank) return Slope.Centre(z) + dir * Mathf.Max(0f, half - 1.5f);
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
