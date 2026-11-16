using UnityEngine;

namespace Summit
{
    public enum Sfx { Land, Jump, Crash, Trick, CloseCall, Click, Buried }

    /// <summary>
    /// All sound is synthesised, so there are no audio files:
    /// stereo wind that rises and gusts with speed, snow scrape that follows the
    /// load on the edge, a low rumble as the slide closes in, and short one-shot
    /// effects for jumps, landings, crashes, tricks, close calls and buttons.
    /// Every level is smoothed per sample, so nothing clicks or zips.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class WindAudio : MonoBehaviour
    {
        // targets: written on the main thread, read on the audio thread
        volatile float tSpeed, tCarve, tRumble, tMaster = 1f, tSnow, tPan;
        // smoothed copies, audio thread only
        float sSpeed, sCarve, sRumble, sMaster, sSnow, sPan;
        float sr = 48000f, smoothK = 0.001f;
        uint rngA = 2463534242u, rngB = 88675123u, rngC = 362436069u;

        struct Svf { public float low, band; }
        Svf windL, windR, roarL, roarR, scrapeHi, scrapeLo, rumbleF;
        float gust = 1f, gustTarget = 1f; int gustLeft;
        float crunch = 1f; int crunchLeft;

        struct Voice { public bool on; public Sfx kind; public float t, amp, pitch, ph, ph2; public Svf f; }
        readonly Voice[] voices = new Voice[12];
        readonly object queueLock = new object();
        readonly Sfx[] qKind = new Sfx[32];
        readonly float[] qAmt = new float[32];
        int qCount;

        void Start()
        {
            sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000f;
            smoothK = 1f - Mathf.Exp(-1f / (sr * 0.06f));   // about 60 ms glide on every level
            var src = GetComponent<AudioSource>();
            // a silent looping clip keeps the source running so the filter below is called
            src.clip = AudioClip.Create("silence", 1024, 1, (int)sr, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();
        }

        /// <summary>speed01: 0..1 · carve: edge load 0..1 · rumble: slide closeness 0..1 · onSnow: 1 when riding on the snow · pan: -1 left .. 1 right.</summary>
        public void Set(float speed01, float carve, float rumble, float master, float onSnow, float pan)
        {
            tSpeed = Mathf.Clamp01(speed01);
            tCarve = Mathf.Clamp01(carve);
            tRumble = Mathf.Clamp01(rumble);
            tMaster = Mathf.Clamp01(master);
            tSnow = Mathf.Clamp01(onSnow);
            tPan = Mathf.Clamp(pan, -1f, 1f);
        }

        /// <summary>Queue a one-shot. amount scales loudness (or picks the note for a trick).</summary>
        public void Play(Sfx kind, float amount = 1f)
        {
            lock (queueLock)
            {
                if (qCount >= qKind.Length) return;
                qKind[qCount] = kind; qAmt[qCount] = amount; qCount++;
            }
        }

        // ---------------------------------------------------------------- dsp helpers

        static float Noise(ref uint r)
        {
            r ^= r << 13; r ^= r >> 17; r ^= r << 5;
            return (r / 4294967296f) * 2f - 1f;
        }

        float F(float fc) { return 2f * Mathf.Sin(Mathf.PI * Mathf.Min(fc, sr * 0.16f) / sr); }

        // band-pass that brings white noise out at roughly unit loudness whatever its centre and width
        float Gain(float fc, float q) { return q / Mathf.Sqrt(Mathf.Max(1e-6f, fc * q / (sr * 0.5f) / 3f)); }

        static float Band(ref Svf s, float x, float f, float q)
        {
            s.low += f * s.band;
            float high = x - s.low - q * s.band;
            s.band += f * high;
            return s.band;
        }

        static float SoftLimit(float x)
        {
            x = Mathf.Clamp(x, -3f, 3f);
            return x * (27f + x * x) / (27f + 9f * x * x);
        }

        // ---------------------------------------------------------------- render

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (queueLock)
            {
                for (int q = 0; q < qCount; q++) StartVoice(qKind[q], qAmt[q]);
                qCount = 0;
            }

            // filter centres move slowly, so set them once per buffer from the smoothed speed
            float s = sSpeed;
            float fcWind = (170f + 250f * s + 900f * s * s) * gust;
            float fW = F(fcWind), gW = Gain(fcWind, 1.5f);
            float fRoar = F(85f), gRoar = Gain(85f, 1.2f);
            float fcHi = 1100f + 1300f * s + 900f * sCarve;
            float fHi = F(fcHi), gHi = Gain(fcHi, 0.9f);
            float fLo = F(420f), gLo = Gain(420f, 1.6f);
            float fRum = F(48f), gRum = Gain(48f, 1.0f);

            int frames = data.Length / channels;
            for (int i = 0; i < frames; i++)
            {
                sSpeed += (tSpeed - sSpeed) * smoothK;
                sCarve += (tCarve - sCarve) * smoothK;
                sRumble += (tRumble - sRumble) * smoothK;
                sMaster += (tMaster - sMaster) * smoothK;
                sSnow += (tSnow - sSnow) * smoothK;
                sPan += (tPan - sPan) * smoothK;

                // gusts: a slow random walk on the wind's pitch and strength
                if (--gustLeft <= 0)
                {
                    gustLeft = (int)(sr * (0.8f + 1.4f * (Noise(ref rngC) * 0.5f + 0.5f)));
                    gustTarget = 1f + 0.22f * Noise(ref rngC);
                }
                gust += (gustTarget - gust) * (smoothK * 0.08f);

                float sp = sSpeed;
                // a soft bed of wind that only really opens up at speed
                float windAmp = (0.006f + 0.11f * sp * sp) * (0.8f + 0.2f * gust);
                float roarAmp = 0.004f + 0.045f * sp * sp;
                float l = Band(ref windL, Noise(ref rngA), fW, 1.5f) * gW * windAmp
                        + Band(ref roarL, Noise(ref rngA), fRoar, 1.2f) * gRoar * roarAmp;
                float r = Band(ref windR, Noise(ref rngB), fW, 1.5f) * gW * windAmp
                        + Band(ref roarR, Noise(ref rngB), fRoar, 1.2f) * gRoar * roarAmp;

                // snow: a crunchy grain under the board, louder when the edge bites
                if (--crunchLeft <= 0)
                {
                    crunchLeft = 18 + (int)((Noise(ref rngC) * 0.5f + 0.5f) * 90f);
                    crunch = 0.8f + 0.4f * (Noise(ref rngC) * 0.5f + 0.5f);
                }
                // the board on snow: a quiet glide, and a clear swoosh that swells as the edge bites
                float scrapeAmp = sSnow * ((0.008f + 0.03f * sp) + sCarve * sCarve * (0.06f + 0.20f * sp));
                float n = Noise(ref rngC);
                float scrape = (Band(ref scrapeHi, n, fHi, 0.9f) * gHi * 0.8f + 0.7f * Band(ref scrapeLo, n, fLo, 1.6f) * gLo)
                             * scrapeAmp * crunch;
                float pr = 0.5f + 0.3f * sPan;   // the edge you are on is a little louder on its side
                l += scrape * (1f - pr) * 1.4f;
                r += scrape * pr * 1.4f;

                // the slide: a deep rumble that swells as it closes
                float rum = Band(ref rumbleF, Noise(ref rngC), fRum, 1.0f) * gRum * sRumble * sRumble * 0.32f;
                l += rum; r += rum;

                float fx = Voices() * 0.6f;
                l = SoftLimit((l * sMaster + fx) * 1.1f);
                r = SoftLimit((r * sMaster + fx) * 1.1f);

                int o = i * channels;
                if (channels == 1) data[o] = 0.5f * (l + r);
                else
                {
                    data[o] = l; data[o + 1] = r;
                    for (int ch = 2; ch < channels; ch++) data[o + ch] = 0.5f * (l + r);
                }
            }
        }

        // ---------------------------------------------------------------- one-shots

        void StartVoice(Sfx kind, float amount)
        {
            int slot = 0; float oldest = -1f;
            for (int v = 0; v < voices.Length; v++)
            {
                if (!voices[v].on) { slot = v; break; }
                if (voices[v].t > oldest) { oldest = voices[v].t; slot = v; }
            }
            voices[slot] = new Voice { on = true, kind = kind, t = 0f, amp = Mathf.Clamp(amount, 0f, 1.5f), pitch = amount };
        }

        float Voices()
        {
            float dt = 1f / sr, sum = 0f;
            for (int v = 0; v < voices.Length; v++)
            {
                if (!voices[v].on) continue;
                Voice vc = voices[v];
                float t = vc.t, y, dur;
                switch (vc.kind)
                {
                    case Sfx.Land:   // a dull thump with a puff of snow
                    {
                        dur = 0.45f;
                        float f0 = 42f + 55f * Mathf.Exp(-t * 28f);
                        vc.ph += 2f * Mathf.PI * f0 * dt;
                        float puff = Band(ref vc.f, Noise(ref rngC), F(260f), 1.2f) * Gain(260f, 1.2f);
                        y = (Mathf.Sin(vc.ph) * 0.55f * Mathf.Exp(-t * 11f) + puff * 0.10f * Mathf.Exp(-t * 26f))
                          * (0.25f + 0.75f * vc.amp);
                        break;
                    }
                    case Sfx.Jump:   // a short rising whoosh as the board pops
                    {
                        dur = 0.28f;
                        float k = Mathf.Clamp01(t / dur);
                        float fc = 450f + 2400f * k;
                        y = Band(ref vc.f, Noise(ref rngC), F(fc), 1.3f) * Gain(fc, 1.3f) * 0.09f * Mathf.Sin(Mathf.PI * k);
                        break;
                    }
                    case Sfx.Crash:  // a heavy body-hits-snow burst
                    {
                        dur = 0.9f;
                        vc.ph += 2f * Mathf.PI * 52f * dt;
                        y = Band(ref vc.f, Noise(ref rngC), F(320f), 1.4f) * Gain(320f, 1.4f) * 0.16f * Mathf.Exp(-t * 4.5f)
                          + Mathf.Sin(vc.ph) * 0.45f * Mathf.Exp(-t * 9f);
                        break;
                    }
                    case Sfx.Trick:  // two bright bells; the note climbs with the chain
                    {
                        dur = 0.6f;
                        float baseHz = 660f * Mathf.Pow(2f, Mathf.Clamp(vc.pitch, 0f, 8f) * 2f / 12f);
                        vc.ph += 2f * Mathf.PI * baseHz * dt;
                        vc.ph2 += 2f * Mathf.PI * baseHz * 1.5f * dt;
                        float env = Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t * 7f);
                        y = (Mathf.Sin(vc.ph) + 0.5f * Mathf.Sin(vc.ph2) * Mathf.Exp(-t * 4f)) * 0.075f * env;
                        break;
                    }
                    case Sfx.CloseCall:  // a falling swish past a tree
                    {
                        dur = 0.32f;
                        float k = Mathf.Clamp01(t / dur);
                        float fc = 2600f - 1900f * k;
                        y = Band(ref vc.f, Noise(ref rngC), F(fc), 1.0f) * Gain(fc, 1.0f) * 0.08f * Mathf.Sin(Mathf.PI * k);
                        break;
                    }
                    case Sfx.Click:
                    {
                        dur = 0.07f;
                        vc.ph += 2f * Mathf.PI * 1250f * dt;
                        y = Mathf.Sin(vc.ph) * 0.10f * Mathf.Exp(-t * 70f);
                        break;
                    }
                    default:         // Buried: the slide rolls over you
                    {
                        dur = 1.6f;
                        float k = Mathf.Clamp01(t / dur);
                        y = Band(ref vc.f, Noise(ref rngC), F(110f), 1.2f) * Gain(110f, 1.2f) * 0.30f * Mathf.Sin(Mathf.PI * Mathf.Sqrt(k));
                        break;
                    }
                }
                sum += y;
                vc.t += dt;
                if (vc.t >= dur) vc.on = false;
                voices[v] = vc;
            }
            return sum;
        }
    }
}
