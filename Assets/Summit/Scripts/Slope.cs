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
        public const float Fall = 0.72f;      // mean gradient of the fall line (about 36 degrees)
        public const float Quad = 1.5f;       // terrain mesh spacing; the rider sits on exactly this mesh
        public const float PisteHalf = 20f;   // half the groomed width
        public static int Seed { get; private set; }
        static float ph, ox, oz;

        /// <summary>
        /// Just before the mountain changes (a new seed, or Course's caches reset): whatever is reading it on another
        /// thread (SlopeStreamer's workers) finishes first.
        /// </summary>
        public static event System.Action Changing;
        public static void BeforeChange() => Changing?.Invoke();

        public static void Init(int seed)
        {
            BeforeChange();
            Seed = seed;
            ph = (seed % 628) / 100f;
            // small offsets only: large ones would lose float precision and step the terrain
            ox = Mathf.Repeat(seed * 37.31f, 211f);
            oz = Mathf.Repeat(seed * 19.77f, 199f);
            Course.ResetCaches();
        }

        /// <summary>A new mountain for every run.</summary>
        public static int RandomSeed() => Random.Range(1, 999983);

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
                int h = (x + salt * 7919 + Seed * 1013) * 374761393 + (y - salt * 104729 + Seed * 7) * 668265263;
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

        /// <summary>
        /// The line straight down the mountain: the mean pitch, seeded rollers, and every so often a
        /// bench that eases off before a roll-over into a much steeper drop (natural air off the top).
        /// </summary>
        public static float FallLine(float z)
        {
            if (z > FinishZ)
            {
                // the base: the pitch eases off into a long flat run-out past the finish
                float u = z - FinishZ;
                return -FinishZ * Fall - Fall * Runout * (1f - Mathf.Exp(-u / Runout));
            }
            float y = -z * Fall;
            // the rollers and drops die away over the last stretch, so the finish is a clean straight
            float k = Mathf.Clamp01((FinishZ - z) / 250f);
            y += k * (6f * (Noise(z * 0.014f, 13.7f) - 0.5f) + 2.6f * (Noise(z * 0.037f, 29.1f) - 0.5f) + 0.8f * (Noise(z * 0.09f, 41.3f) - 0.5f) + Drops(z));
            return y;
        }

        // ---- the whole mountain: 10,000 ft (3,048 m) from the summit to the finish at the base
        public const float VerticalFeet = 10000f, Vertical = 3048f, Runout = 70f;
        public static float FinishZ => Vertical / Fall;
        /// <summary>Height above the finish, in feet, at a distance z down the mountain.</summary>
        public static float AltitudeFeet(float z) => Mathf.Max(0f, (FallLine(z) - FallLine(FinishZ)) * 3.2808f);

        /// <summary>Ridged noise in [0, 1]: sharp crests, broad hollows.</summary>
        public static float Ridged(float x, float y) { float n = 1f - Mathf.Abs(Noise(x, y) * 2f - 1f); return n * n; }

        public const float DropSeg = 95f;

        /// <summary>Benches and roll-overs: zero at both ends of each segment, so they never add up.</summary>
        public static float Drops(float z)
        {
            if (z < 90f) return 0f;
            int s = Mathf.FloorToInt(z / DropSeg);
            if (Hash01(s, 11, 41) < 0.25f) return 0f;               // some stretches just keep their pitch
            float zs = s * DropSeg;
            if (Course.At(zs) == Course.Section.Pipe || Course.At(zs + DropSeg) == Course.Section.Pipe) return 0f;
            if (Ridge.Has(zs) || Ridge.Has(zs + DropSeg)) return 0f;   // a roll-over on a crest would throw you off it
            if (Cliffs.JumpNear(zs + DropSeg * 0.5f, DropSeg * 0.5f + 20f, out _)) return 0f;   // nor run into a jump cliff's bench
            float u = z / DropSeg - s;
            float H = 2f + 7f * Hash01(s, 12, 41);
            float u0 = 0.35f + 0.4f * Hash01(s, 13, 41), w = 0.09f + 0.08f * Hash01(s, 14, 41);   // never a sudden kink
            float t = Mathf.Clamp01((u - (u0 - w)) / (2f * w));
            return H * (u - t * t * (3f - 2f * t));
        }

        /// <summary>The snow surface, without any crevasse cut into it.</summary>
        public static float Surface(float x, float z) => HeightCore(x, z, false);

        /// <summary>
        /// The snow you ride on. Crevasses are not part of it: they are holes cut in the drawn mesh with
        /// their own walls, and riding into one drops you in (see Rider and Course.CrevasseCut).
        /// </summary>
        public static float Height(float x, float z) => HeightCore(x, z, false);

        static float HeightCore(float x, float z, bool cut)
        {
            float y = FallLine(z);
            // the cross-section: a shallow trough along the piste, walls climbing far off it,
            // and a camber that leans one way and then the other
            float a = x - Centre(z), aa = Mathf.Abs(a);
            // the run sits in a shallow valley that levels off: no walls, the mountainside goes on either side
            y += 0.0035f * a * a / (1f + a * a / 6400f);
            y += Mathf.Clamp(a, -80f, 80f) * 0.07f * (Noise(z * 0.009f, 51.7f) - 0.5f) * 2f;
            // off the run, open mountain: spurs and gullies running down the fall line, bumps on them,
            // so you can go anywhere and every line is different
            float wild = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((aa - PisteHalf - 6f) / 55f));
            if (wild > 0f)
                y += wild * (30f * (Ridged(x * 0.0075f + 3.1f, z * 0.0022f) - 0.4f) + 12f * (Noise(x * 0.017f + 9f, z * 0.006f) - 0.5f)
                             + 4f * (Noise(x * 0.05f, z * 0.03f + 7f) - 0.5f));
            // broad lumps in the rough; a little side-to-side tilt on the piste
            float rough = Mathf.Clamp01((aa - PisteHalf) / 10f);
            y += (Noise(x * 0.045f, z * 0.045f) - 0.5f) * 2.6f * rough;
            y += (Noise(x * 0.011f + 50f, z * 0.009f) - 0.5f) * 7f;
            y += Kicker(x, z);
            y += Cliffs.JumpHeight(x, z) + Cliffs.WallCut(x, z);   // cliffs to jump off, and cliffs to stay away from
            // the halfpipe: a U cut along the piste, with decks either side, easing in and out
            float pw = Course.PipeWeight(z);
            if (pw > 0f)
            {
                float c = Centre(z);
                float floor = FallLine(z) + (Noise(c * 0.011f + 50f, z * 0.009f) - 0.5f) * 7f;
                float deckEnd = Course.PipeHalfWidth + Course.PipeDeck;
                float yp = floor + Course.PipeProfile(a);
                if (aa > deckEnd) yp = Mathf.Lerp(yp, y, Mathf.Clamp01((aa - deckEnd) / 10f));
                y = Mathf.Lerp(y, yp, pw);
            }
            y = Ridge.Shape(x, z, y);   // a narrow crest with cliffs either side, once a cycle
            if (cut) y -= Course.CrevasseCut(x, z);
            return Start(x, z, y);
        }

        // ---------------------------------------------------------------- the start
        // A groomed pad cut into the top of the run: you stand at the back, push off,
        // ride down a gentle in-run and launch off a lip into the steep face below.
        public const float PadBack = -70f, PadLip = 0f, PadHalf = 13f, PadDrop = 4.5f, LipRise = 0.8f, LipLen = 7f;
        /// <summary>The start gate (StartGate.cs), 36 m above the lip, where the in-run steepens.</summary>
        public const float GateZ = PadLip - 36f;
        /// <summary>
        /// Where a run starts: at the gate, ready to go, just far enough behind it that the waiting view has the whole
        /// board across the top of a phone held upright. (It was at the back of the pad, 20 m further up, and that ride
        /// was on the pad's near-flat back: seven seconds from GO to the first jump with nothing to do.)
        /// </summary>
        public static float StartZ => GateZ - 6f;

        static float Start(float x, float z, float y)
        {
            if (z > PadLip + 0.01f) return y;
            // the summit: a sunlit plateau above the face, with the in-run down its middle
            float cx = Centre(PadLip);
            float back = PadLip - z;   // the in-run: 0.22 for the last 38 m (from just above the gate), nearly flat behind that
            float pad = BaseAt(cx, PadLip) + PadDrop + (back < 38f ? back * 0.22f : 38f * 0.22f + (back - 38f) * 0.07f);
            float u = (z - (PadLip - LipLen)) / LipLen;
            float d = Mathf.Abs(x - cx) - PadHalf;
            if (u > 0f) pad += LipRise * u * u * Mathf.Clamp01(1f - d / 3f);
            // gentle banks either side; steep ones grew into cliffs (with trees on top) that shaded the whole pad
            if (d > 0f) pad += Mathf.Min(d, 28f) * Mathf.Min(d, 28f) * 0.006f + (Noise(x * 0.05f, z * 0.05f) - 0.5f) * 2f * Mathf.Clamp01(d / 10f);
            // Past 28 m out the banks give way: by the lip, to the open mountain's own shape (so the seam with the
            // run's terrain is smooth), and further back to ground falling away, the start sitting on the
            // mountain's top shoulder, open to the sky
            if (d > 28f)
            {
                float far = Mathf.SmoothStep(0f, 1f, (d - 28f) / 14f);
                float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-15f, -55f, z));
                // (falling away in ribs and gullies and tiers of rock and snow, not one plane at the snow line's angle)
                float away = pad - FallAway(d - 28f, z, 0.9f, 0f, Mathf.Sign(x - cx) * 3.9f);
                float open = y - Mathf.Max(0f, FallLine(z) - FallLine(PadLip));   // the open mountain, not climbing on up behind the lip
                pad = Mathf.Lerp(pad, Mathf.Lerp(open, away, fall), far);
            }
            return Summit(x, z, pad);
        }

        // ---------------------------------------------------------------- the summit
        // Behind the start, the camp's plateau is the mountain's top: a few metres above the start pad (the gate
        // leads down to it), nearly level, and falling away on every side but the front: steeply on the left and
        // behind, and on the right a great cliff and rock buttresses some 430 m down to a valley (the cable-railed
        // edge of the camp; its far end closes in a steep wall before the start pad, so the run is untouched).
        // Shaped after real north faces (the Eiger's, the Grandes Jorasses'): no face is one plane or one angle.
        // The shoulders roll over and steepen into rock in tiers, ribs standing out and gullies cut between them;
        // the cliff leans back from its rounded, snow-capped lip in steep bands between sloping ledges, steepest on its
        // buttresses and lying back in its gullies, and its foot spreads into fans of snow and rubble.
        public const float SummitLeft = -30f, SummitRight = 24.8f, SummitBack = -110f;

        static float Summit(float x, float z, float pad)
        {
            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-41f, -53f, z));   // none at the pad (from just behind where a run starts), all of it from the camp's front
            if (w <= 0f) return pad;
            float cx = Centre(PadLip), a = x - cx;
            float back = PadLip - z;
            float top = BaseAt(cx, PadLip) + PadDrop + 38f * 0.22f + Mathf.Max(0f, back - 38f) * 0.07f
                      + 6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-44f, -62f, z))   // up from the gate to the camp, a short bank (a walk, not a climb)
                      + (Noise(x * 0.07f, z * 0.07f) - 0.5f) * 0.8f;
            // the lodge's level pad (Camp.LodgePad): level under it, the ground in front of its terrace a metre lower
            float lp = Camp.LodgePad(a, z, out float lower);
            if (lp > 0f) top = Mathf.Lerp(top, BaseAt(cx, PadLip) + PadDrop + 38f * 0.22f + (PadLip - Camp.LodgeZ - 38f) * 0.07f + 6f - lower, lp);
            float y = top;
            // a snowy shoulder rolling over, steepening into rock further down
            if (a < SummitLeft) y -= FallAway(SummitLeft - a, z, 0.5f, 0.0065f, 1.3f);
            if (z < SummitBack) y -= FallAway(SummitBack - z, x, 0.45f, 0.006f, 4.7f);   // the far side of the peak
            // (the edge wanders out a little, never in past the railing)
            float edge = SummitRight + 0.5f * Noise(z * 0.09f + 4.1f, 2.7f);
            if (a > edge)
            {
                float e = a - edge;
                // along the edge the face swings between buttresses, standing out and steepest, and gullies set back
                // between them, lying back and holding snow: its average slope 66 to 80 degrees
                float rib = Noise(z * 0.055f + 3.3f, 1.9f);
                float lean = 2.2f + 2.4f * rib * rib + 0.6f * Noise(z * 0.16f, 4.4f);
                // the lip: snow rounding over for a few metres before the rock (it never drops straight from the rail)
                const float cap = 4f;
                float face;
                if (e < cap) face = lean * 0.5f * e * e / cap;
                else
                {
                    // then the face in tiers: a steep band of rock, a sloping ledge holding snow, every 22 to 40 m of
                    // height (wandering along the face, as strata do)
                    float f = e - cap;
                    float period = (22f + 18f * Noise(z * 0.03f + 7.7f, 2.2f)) / lean;
                    float phase = 1.5f * Noise(z * 0.05f, 9.1f);
                    face = lean * cap * 0.5f + lean * f
                         - lean * 0.78f * period / (2f * Mathf.PI) * Mathf.Sin(2f * Mathf.PI * (f / period + phase)) * Mathf.Clamp01(f / 3f);
                }
                // the face drops 170 to 260 m (varying along it), then rock buttresses run on down to the valley floor,
                // spurs and gullies down them, spreading at the foot in fans of snow and rubble
                float sheer = 170f + 90f * Noise(z * 0.035f + 1.7f, 6.1f);
                float spurs = 0.75f + 0.7f * Ridged(z * 0.03f + 0.4f, a * 0.012f);
                float onFace = cap + (sheer - lean * cap * 0.5f) / lean;   // about where the face's tiers reach the buttresses
                float drop = Mathf.Min(face, sheer + Mathf.Max(0f, e - onFace) * 1.8f * spurs);
                drop = SmoothMin(drop, 430f, 30f);
                drop += Mathf.Clamp01((e - 110f) / 60f) * (Ridged(x * 0.004f + 2.3f, z * 0.004f) - 0.4f) * 40f;   // the valley's own lumps
                // toward the start the cliff gives way, over 50 m, to a spur falling away into the valley
                y -= Mathf.Lerp(e * 0.9f, drop, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-34f, -84f, z)));
            }
            return Mathf.Lerp(pad, y, w);
        }

        /// <summary>
        /// How far a summit shoulder has fallen e metres out from its edge (`along` runs along the edge): rolling
        /// over and steepening as k1 * e + k2 * e^2, with ribs and gullies running down it (the gullies cut in deeper
        /// the further down), and, once it is steep enough to be rock, stepping down in tiers, ledges of snow
        /// between bands of rock, rather than one cone of a slope.
        /// </summary>
        static float FallAway(float e, float along, float k1, float k2, float s)
        {
            float d = e * k1 + e * e * k2;
            float rib = Ridged(along * 0.022f + s, e * 0.006f + s * 2f);
            d += (0.35f - rib) * Mathf.Clamp01(e / 25f) * (6f + e * 0.18f);
            float slope = k1 + 2f * k2 * e;
            float tiers = Mathf.Clamp01((slope - 0.7f) / 0.4f);
            if (tiers <= 0f) return d;
            // The tiers: no two alike, or they read as contour lines. Their spacing wanders down the face (t runs
            // unevenly with e) and along it; each tier has its own riser, steep or broken, and its own ledge, wide or
            // a mere step (both from a noise keyed by the tier, so they change slowly along it); and they pinch out
            // here and there, and die away across the ribs, leaving the rock there unbroken
            float period = 6f + 9f * Noise(along * 0.017f + s, 5.5f);
            float t = e / period + 1.3f * Noise(e * 0.035f + s, along * 0.021f) + 1.2f * Noise(along * 0.03f, s);
            float tier = Mathf.Floor(t), f = t - tier;
            float riser = 0.22f + 0.45f * Noise(tier * 3.1f + s, along * 0.025f);   // how much of the tier is the step
            float at = Mathf.Lerp(riser * 0.5f, 1f - riser * 0.5f, Noise(tier * 2.3f + 11f + s, along * 0.02f));
            float step = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - at) / riser + 0.5f));
            float held = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.62f, Noise(along * 0.013f + s * 1.7f, e * 0.017f)))
                       * (1f - 0.8f * Mathf.SmoothStep(0.45f, 0.85f, rib))
                       // (and each tier its own way: one runs on along the face, its neighbour fades out within a few
                       // tens of metres, so the ledges come and go rather than ring the shoulder like contour lines)
                       * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.58f, Noise(tier * 1.9f + 5f + s, along * 0.03f)));
            d += tiers * held * slope * period * (step - f) * 0.85f;
            return d;
        }

        static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        /// <summary>The slope without the start pad (where the pad's height is measured from).</summary>
        static float BaseAt(float x, float z)
        {
            float y = FallLine(z);
            float a = x - Centre(z);
            return y + 0.0035f * a * a + (Noise(x * 0.011f + 50f, z * 0.009f) - 0.5f) * 7f;
        }

        /// <summary>True on the start pad (no trees, no pickups).</summary>
        public static bool OnPad(float x, float z) => (z < PadLip + 8f && z > PadBack - 20f && Mathf.Abs(x - Centre(PadLip)) < PadHalf + 12f) || Camp.InCamp(x, z);

        // Kickers sit on the piste every so often: a curved ramp you can see coming,
        // a crisp lip, then a long smooth landing below it.
        public const float KickPeriod = 130f, KickRamp = 12f, KickHeight = 2.4f, KickDrop = 5f, KickWidth = 9f;

        public static float Kicker(float x, float z)
        {
            var sec = Course.At(z);
            if (sec == Course.Section.Park)
            {
                // the park's jump line: two big kickers in the middle of the piste
                float zs = Course.CycleBase(z) + Course.ParkAt;
                foreach (float at in Course.ParkKickers)
                {
                    float z0p = zs + at;
                    float hk = KickShape(x, z, z0p, Centre(z0p), 1.15f);
                    if (hk > 0f) return hk;
                }
                return 0f;
            }
            if (sec != Course.Section.Cruise) return 0f;
            float cell = Mathf.Floor((z - 60f) / KickPeriod);
            if (cell < 0f) return 0f;
            if (Hash01((int)cell, 3, 9) < 0.25f) return 0f;          // every so often there is none
            float side = (Hash01((int)cell, 4, 9) - 0.5f) * 18f;      // left, middle or right of the piste
            float z0 = 60f + cell * KickPeriod + 30f;
            if (z0 > FinishZ - 260f) return 0f;
            if (Course.At(z0) != Course.Section.Cruise || Course.At(z0 + KickRamp + KickDrop) != Course.Section.Cruise) return 0f;
            if (Cliffs.JumpNear(z0, 30f, out _)) return 0f;   // the jump cliff is the jump here
            return KickShape(x, z, z0, Centre(z0) + side, 1f);
        }

        static float KickShape(float x, float z, float z0, float cx, float scale)
        {
            float ramp = KickRamp * scale, drop = KickDrop * scale;
            float u = z - z0;
            if (u < 0f || u > ramp + drop) return 0f;
            float w = Mathf.Abs(x - cx) / (KickWidth * scale);
            if (w >= 1f) return 0f;
            float m = 1f - w * w; m *= m;                                // rounded shoulders
            float h;
            if (u < ramp) { float t = u / ramp; h = t * t; }           // curved ramp: the lip is steepest
            else { float t = (u - ramp) / drop; h = 1f - t * t * (3f - 2f * t); }
            return h * KickHeight * scale * m;
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
            // in the halfpipe, ride the true curve: its 1.5 m facets would jolt the board at every edge
            // (the drawn facets sit at most a few centimetres off it)
            if (Course.PipeWeight(z) > 0.01f && Mathf.Abs(x - Centre(z)) < Course.PipeHalfWidth + 1.5f) return Height(x, z);
            float gx = x / Quad, gz = z / Quad;
            int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
            // the rounded edge of a ridge's crest, where the mesh is cut into narrower strips to follow the curve
            if (Ridge.Fine(i, j)) return Ridge.MeshHeight(x, z);
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
            float gz;
            // never sample across the drop at the start lip: on the pad, look only uphill
            if (z > PadLip - 2f * e && z <= PadLip + 0.01f) gz = (Height(x, z) - Height(x, z - e)) / e;
            else if (z > PadLip && z < PadLip + e) gz = (Height(x, z + e) - Height(x, z)) / e;
            else gz = (Height(x, z + e) - Height(x, z - e)) / (2f * e);
            return new Vector2((Height(x + e, z) - Height(x - e, z)) / (2f * e), gz);
        }

        public static Vector3 Normal(float x, float z)
        {
            Vector2 g = Gradient(x, z);
            return new Vector3(-g.x, 1f, -g.y).normalized;
        }
    }
}
