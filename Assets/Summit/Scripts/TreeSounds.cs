using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Wood breaking, made rather than recorded (no clip in Resources/Audio has it), in layers SoundMix plays together:
    ///   Twig:  a thin twig's sharp, bright crack, a micro-splinter or two after it (and now and then a second break).
    ///   Limb:  a limb's deeper snap: the fibres ticking as it bends, the crack (a woody ring an octave or two lower
    ///          than a twig's), and the splintering as the fibres tear apart one after another.
    ///   Trunk: a trunk giving: a heavy, low crack (often doubled), splinters, and the creak of the wood as it leans
    ///          over (stick-slip: a train of little catches, gliding in pitch, through the wood's resonances).
    ///   Thud:  what's broken landing: a low whump into the snow, the crown's branches crunching, and the spray.
    ///   Snow:  a soft dump of snow off the branches: clumps falling (low, muffled puffs) and a patter of pellets.
    /// Each is struck as noise through resonators (two-pole band-passes, tuned and damped like wood), and every take
    /// is different (its own seed: the number of cracks, where they fall, how the wood rings). SoundMix varies the
    /// pitch and level again with the size of what broke and how fast it was hit, and never plays a take twice running.
    /// </summary>
    public static class TreeSounds
    {
        public const int Rate = 44100, LowRate = 32000;

        public static AudioClip[] Twigs() => Takes("TwigCrack", 10, Rate, 0.26f, 7100, Twig);
        public static AudioClip[] Limbs() => Takes("LimbSnap", 8, Rate, 0.7f, 7300, Limb);
        public static AudioClip[] Trunks() => Takes("TrunkCrack", 6, Rate, 2.0f, 7500, Trunk);
        public static AudioClip[] Thuds() => Takes("TreeThud", 6, LowRate, 1.2f, 7700, Thud);
        public static AudioClip[] Snow() => Takes("SnowDump", 8, LowRate, 1.3f, 7900, SnowDump);

        delegate void Make(float[] d, int rate, System.Random rng);

        static AudioClip[] Takes(string name, int count, int rate, float seconds, int seed, Make make)
        {
            var takes = new AudioClip[count];
            int n = (int)(rate * seconds);
            for (int k = 0; k < count; k++)
            {
                var rng = new System.Random(seed + k * 131);
                var d = new float[n];
                make(d, rate, rng);
                // a short fade at the end (nothing is cut off with a click) and normalised
                int fade = Mathf.Min(n / 4, rate / 50);
                for (int i = 0; i < fade; i++) d[n - 1 - i] *= i / (float)fade;
                float peak = 1e-4f; foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
                for (int i = 0; i < n; i++) d[i] *= 0.9f / peak;
                takes[k] = AudioClip.Create(name + k, n, 1, rate, false);
                takes[k].SetData(d, 0);
            }
            return takes;
        }

        // ---- the pieces

        static float R(System.Random rng, float a, float b) => a + (b - a) * (float)rng.NextDouble();
        static float W(System.Random rng) => (float)rng.NextDouble() * 2f - 1f;

        /// <summary>A strike at `at` s: `len` s of noise dying away over `tau` s, rung through resonant modes
        /// (frequency, decay, level), plus `click` of the raw noise (the bright edge of the break).</summary>
        static void Strike(float[] d, int rate, System.Random rng, float at, float amp, float len, float tau, float click, params Vector3[] modes)
        {
            int start = (int)(at * rate);
            if (start >= d.Length) return;
            int ex = Mathf.Max(1, (int)(len * rate));
            float longest = 0f; foreach (var m in modes) longest = Mathf.Max(longest, m.y);
            int span = Mathf.Min(d.Length - start, ex + (int)(longest * 7f * rate));
            var y1 = new float[modes.Length]; var y2 = new float[modes.Length];
            var c = new float[modes.Length]; var r2 = new float[modes.Length]; var g = new float[modes.Length];
            for (int m = 0; m < modes.Length; m++)
            {
                float w = 2f * Mathf.PI * Mathf.Min(modes[m].x, rate * 0.45f) / rate;
                float r = Mathf.Exp(-1f / (modes[m].y * rate));
                c[m] = 2f * r * Mathf.Cos(w); r2[m] = r * r;
                g[m] = Mathf.Sin(w) * modes[m].z;   // (a unit impulse rings at about the mode's level)
            }
            // the noise that strikes the modes, scaled so a strike of any length rings them about as loud
            float norm = 1f / Mathf.Sqrt(Mathf.Max(1f, tau * rate));
            for (int i = 0; i < span; i++)
            {
                float t = i / (float)rate;
                float raw = i < ex ? W(rng) * Mathf.Exp(-t / tau) : 0f;
                float x = raw * norm;
                float s = raw * click;
                for (int m = 0; m < modes.Length; m++)
                {
                    float y = x * g[m] + c[m] * y1[m] - r2[m] * y2[m];
                    y2[m] = y1[m]; y1[m] = y; s += y;
                }
                d[start + i] += s * amp;
            }
        }

        /// <summary>Band-limited noise (one-pole high-pass at `lo`, low-pass at `hi`) under an envelope of
        /// `rise` s up and `fall` s down, from `at`, for `len` s, `grain`: 0 smooth .. 1 gated in little bursts.</summary>
        static void Hiss(float[] d, int rate, System.Random rng, float at, float len, float amp, float lo, float hi, float rise, float fall, float grain = 0f)
        {
            int start = (int)(at * rate), n = Mathf.Min(d.Length - start, (int)(len * rate));
            float aLo = 1f - Mathf.Exp(-2f * Mathf.PI * lo / rate), aHi = 1f - Mathf.Exp(-2f * Mathf.PI * hi / rate);
            float l1 = 0f, l2 = 0f, l3 = 0f, gate = 1f; int nextGate = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Min(1f, t / Mathf.Max(1e-3f, rise)) * Mathf.Exp(-Mathf.Max(0f, t - rise) / fall);
                if (grain > 0f && i >= nextGate)
                {
                    gate = rng.NextDouble() < 0.5f + 0.5f * (1f - grain) ? R(rng, 0.5f, 1f) : R(rng, 0f, 0.15f);
                    nextGate = i + (int)(R(rng, 0.002f, 0.012f) * rate);
                }
                l1 += (W(rng) - l1) * aHi; l2 += (l1 - l2) * aHi;   // (two poles down: softer than one)
                l3 += (l2 - l3) * aLo;
                d[start + i] += (l2 - l3) * env * amp * gate;
            }
        }

        /// <summary>A low thump: a sine at `f` falling by `drop` (a fraction) over the first 80 ms, dying over `tau`.</summary>
        static void Thump(float[] d, int rate, float at, float amp, float f, float drop, float tau)
        {
            int start = (int)(at * rate), n = Mathf.Min(d.Length - start, (int)(tau * 7f * rate));
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float fi = f * (1f - drop * Mathf.Min(1f, t / 0.08f));
                ph += 2f * Mathf.PI * fi / rate;
                d[start + i] += Mathf.Sin(ph) * Mathf.Min(1f, t / 0.003f) * Mathf.Exp(-t / tau) * amp;
            }
        }

        static Vector3 Mode(System.Random rng, float lo, float hi, float tauLo, float tauHi, float lvl) => new Vector3(R(rng, lo, hi), R(rng, tauLo, tauHi), lvl);

        /// <summary>Fibres tearing: `count` little cracks from `from` over `over` s, closer together at first, quieter on.</summary>
        static void Splinters(float[] d, int rate, System.Random rng, float from, float over, int count, float amp, float lo, float hi)
        {
            float t = from;
            for (int i = 0; i < count; i++)
            {
                float u = i / (float)Mathf.Max(1, count - 1);
                t += over / count * R(rng, 0.2f, 1.8f) * (0.4f + 1.2f * u);
                if (t > from + over) break;
                Strike(d, rate, rng, t, amp * R(rng, 0.3f, 1f) * (1f - 0.6f * u), 0.0008f, 0.0004f, 0.15f,
                       Mode(rng, lo, hi, 0.003f, 0.008f, 1f), Mode(rng, lo * 1.7f, hi * 1.7f, 0.002f, 0.005f, 0.5f));
            }
        }

        // ---- the layers

        static void Twig(float[] d, int rate, System.Random rng)
        {
            float t0 = 0.004f;
            Strike(d, rate, rng, t0, 1f, 0.0015f, 0.0006f, 0.35f,
                   Mode(rng, 2200f, 3600f, 0.006f, 0.012f, 1f), Mode(rng, 3800f, 5600f, 0.004f, 0.008f, 0.7f), Mode(rng, 1300f, 2000f, 0.008f, 0.014f, 0.45f));
            Splinters(d, rate, rng, t0 + 0.004f, R(rng, 0.02f, 0.06f), rng.Next(1, 5), 0.45f, 2500f, 4800f);
            if (rng.NextDouble() < 0.4)   // the twig goes in two places
                Strike(d, rate, rng, t0 + R(rng, 0.04f, 0.12f), R(rng, 0.35f, 0.7f), 0.0012f, 0.0005f, 0.3f,
                       Mode(rng, 2600f, 4600f, 0.005f, 0.01f, 1f), Mode(rng, 1500f, 2400f, 0.006f, 0.012f, 0.5f));
            Hiss(d, rate, rng, 0f, 0.2f, R(rng, 0.03f, 0.07f), 2500f, 9000f, 0.01f, 0.05f);   // the needles round it
        }

        static void Limb(float[] d, int rate, System.Random rng)
        {
            // the bend: fibres ticking, faster and faster
            float bend = R(rng, 0.02f, 0.07f);
            Splinters(d, rate, rng, 0f, bend, rng.Next(3, 8), 0.12f, 1000f, 2200f);
            float t0 = bend + 0.004f;
            // the crack: lower and fuller than a twig's, with the limb's own body under it
            Strike(d, rate, rng, t0, 1f, 0.003f, 0.0012f, 0.22f,
                   Mode(rng, 700f, 1100f, 0.02f, 0.04f, 1f), Mode(rng, 1200f, 1800f, 0.012f, 0.025f, 0.7f),
                   Mode(rng, 2200f, 3200f, 0.006f, 0.012f, 0.45f), Mode(rng, 260f, 420f, 0.04f, 0.07f, 0.6f));
            // splintering: the fibres tearing apart one after another, and the crunch of them
            Splinters(d, rate, rng, t0 + 0.006f, R(rng, 0.12f, 0.28f), rng.Next(12, 28), 0.5f, 600f, 1800f);
            Hiss(d, rate, rng, t0, 0.3f, R(rng, 0.08f, 0.14f), 500f, 2600f, 0.004f, 0.07f, 0.8f);
            if (rng.NextDouble() < 0.35)   // a last fibre giving
                Strike(d, rate, rng, t0 + R(rng, 0.18f, 0.35f), R(rng, 0.25f, 0.45f), 0.002f, 0.0008f, 0.15f,
                       Mode(rng, 900f, 1500f, 0.012f, 0.02f, 1f), Mode(rng, 1800f, 2600f, 0.006f, 0.01f, 0.5f));
            Hiss(d, rate, rng, t0, 0.5f, R(rng, 0.02f, 0.04f), 2200f, 7000f, 0.03f, 0.14f);   // the needles shaken
        }

        static void Trunk(float[] d, int rate, System.Random rng)
        {
            float t0 = 0.006f;
            // the crack: heavy and low, the trunk's body ringing under it, and a thump through the ground
            Strike(d, rate, rng, t0, 1f, 0.005f, 0.002f, 0.12f,
                   Mode(rng, 140f, 260f, 0.09f, 0.16f, 1f), Mode(rng, 300f, 480f, 0.05f, 0.09f, 0.8f),
                   Mode(rng, 600f, 950f, 0.025f, 0.045f, 0.6f), Mode(rng, 1300f, 2000f, 0.01f, 0.02f, 0.35f));
            Thump(d, rate, t0, 0.5f, R(rng, 55f, 80f), 0.25f, 0.11f);
            if (rng.NextDouble() < 0.7)   // and again, as the rest of it goes
                Strike(d, rate, rng, t0 + R(rng, 0.03f, 0.1f), R(rng, 0.5f, 0.8f), 0.004f, 0.0016f, 0.1f,
                       Mode(rng, 180f, 320f, 0.07f, 0.12f, 1f), Mode(rng, 500f, 850f, 0.03f, 0.05f, 0.7f), Mode(rng, 1100f, 1700f, 0.01f, 0.02f, 0.35f));
            Splinters(d, rate, rng, t0 + 0.01f, R(rng, 0.3f, 0.5f), rng.Next(14, 26), 0.32f, 600f, 1900f);
            Hiss(d, rate, rng, t0, 0.5f, 0.12f, 350f, 1800f, 0.005f, 0.12f, 0.8f);
            // the creak as it leans over: stick-slip catches through the wood's resonances, gliding in pitch, in one
            // to three goes
            float at = t0 + R(rng, 0.12f, 0.3f);
            int goes = rng.Next(1, 4);
            for (int k = 0; k < goes && at < 1.7f; k++)
            {
                float len = Mathf.Min(1.85f - at, R(rng, 0.25f, 0.7f) / Mathf.Sqrt(goes));
                Creak(d, rate, rng, at, len, R(rng, 0.25f, 0.4f) * (k == 0 ? 1f : 0.7f), R(rng, 80f, 170f), R(rng, -0.35f, 0.25f));
                at += len + R(rng, 0.03f, 0.15f);
            }
        }

        /// <summary>Wood creaking: catches at `f` Hz (gliding by `glide`, a fraction, over `len`), each a tiny strike
        /// through two formants of the wood, swelling and wavering.</summary>
        static void Creak(float[] d, int rate, System.Random rng, float at, float len, float amp, float f, float glide)
        {
            var e = new float[(int)(len * rate) + 1];
            float t = 0f, wob = R(rng, 3f, 7f), ph = R(rng, 0f, 6.28f);
            while (t < len)
            {
                int i = (int)(t * rate); if (i >= e.Length) break;
                float u = t / len;
                float env = Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 1.4f)) * (0.75f + 0.25f * Mathf.Sin(ph + 2f * Mathf.PI * wob * t));
                e[i] += env * R(rng, 0.6f, 1f) * (rng.NextDouble() < 0.06 ? 1.8f : 1f);   // (now and then a bigger catch)
                float fi = f * (1f + glide * u);
                t += 1f / fi * R(rng, 0.9f, 1.1f);
            }
            // the catches through the formants
            Vector3[] modes = { Mode(rng, 350f, 650f, 0.006f, 0.01f, 1f), Mode(rng, 1000f, 1500f, 0.004f, 0.007f, 0.6f), Mode(rng, 180f, 260f, 0.01f, 0.018f, 0.4f) };
            int start = (int)(at * rate);
            for (int m = 0; m < modes.Length; m++)
            {
                float w = 2f * Mathf.PI * modes[m].x / rate, r = Mathf.Exp(-1f / (modes[m].y * rate));
                float c = 2f * r * Mathf.Cos(w), r2 = r * r, g = Mathf.Sin(w) * modes[m].z;
                float y1 = 0f, y2 = 0f;
                for (int i = 0; i < e.Length && start + i < d.Length; i++)
                {
                    float y = e[i] * g + c * y1 - r2 * y2; y2 = y1; y1 = y;
                    d[start + i] += y * amp;
                }
            }
        }

        static void Thud(float[] d, int rate, System.Random rng)
        {
            float t0 = 0.005f;
            // into the snow: a low whump, the snow squeezed out from under it
            Thump(d, rate, t0, 1f, R(rng, 48f, 68f), R(rng, 0.15f, 0.3f), R(rng, 0.11f, 0.16f));
            Hiss(d, rate, rng, t0, 0.5f, 0.9f, 40f, 380f, 0.006f, 0.09f);
            // the crown's branches crunching under it, and a bounce
            Splinters(d, rate, rng, t0 + 0.004f, R(rng, 0.12f, 0.25f), rng.Next(8, 18), 0.25f, 400f, 1200f);
            float bounce = t0 + R(rng, 0.07f, 0.17f);
            Thump(d, rate, bounce, R(rng, 0.3f, 0.5f), R(rng, 55f, 80f), 0.2f, 0.08f);
            Hiss(d, rate, rng, bounce, 0.3f, 0.35f, 60f, 450f, 0.004f, 0.06f);
            // the snow thrown up
            Hiss(d, rate, rng, t0 + 0.01f, 1.1f, R(rng, 0.18f, 0.26f), 300f, 2200f, 0.03f, 0.3f, 0.5f);
        }

        static void SnowDump(float[] d, int rate, System.Random rng)
        {
            // clumps falling off the branches: soft, muffled puffs, the first the biggest
            int clumps = rng.Next(3, 8);
            for (int k = 0; k < clumps; k++)
            {
                float at = k == 0 ? R(rng, 0f, 0.03f) : R(rng, 0.03f, 0.6f);
                float amp = (k == 0 ? 1f : R(rng, 0.3f, 0.75f));
                float hi = R(rng, 300f, 900f);
                Hiss(d, rate, rng, at, 0.7f, amp, 60f, hi, R(rng, 0.015f, 0.04f), R(rng, 0.07f, 0.22f));
            }
            // a patter of pellets on the snow and the branches below
            int grains = rng.Next(30, 80);
            for (int k = 0; k < grains; k++)
            {
                float u = (float)rng.NextDouble();
                float at = 0.04f + 0.85f * u * u;   // (thick at first, thinning out)
                Hiss(d, rate, rng, at, 0.03f, R(rng, 0.05f, 0.2f) * (1f - 0.6f * u), 150f, R(rng, 600f, 1800f), 0.001f, R(rng, 0.003f, 0.008f));
            }
            // the snow sliding off the needles
            Hiss(d, rate, rng, 0f, 0.6f, R(rng, 0.03f, 0.05f), 1000f, 3500f, 0.04f, 0.16f, 0.6f);
        }
    }
}
