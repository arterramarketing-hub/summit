using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The fun rides' sounds (rides): the rally car's engine, the kayak's hull hissing over the snow and its paddle
    /// strokes, the ridden bear's gallop, grunts and roar. The engine and the hull are made here (no recordings: like
    /// the branches' swish, BranchClips); the bear's are the wildlife bear's own clips (bear_steps, bear_growl,
    /// bear_roar). Fed each frame from RideSound (vehicle, rpm, throttle) by SoundMix.Set.
    /// </summary>
    public partial class SoundMix
    {
        Layer engine, hull, bearRun;
        AudioSource bearVoice;
        float bearVoiceT = 9f, bearVoiceFor;
        AudioClip strokeSwish;

        void BuildVehicles()
        {
            engine = Made(EngineClip(), 0f);
            hull = Made(HullClip(), 0f);
            bearRun = Loop("bear_steps");
            bearVoice = gameObject.AddComponent<AudioSource>();
            bearVoice.playOnAwake = false; bearVoice.spatialBlend = 0f; bearVoice.loop = false;
            bearVoice.clip = C("bear_growl");
            strokeSwish = whoosh;
        }

        Layer Made(AudioClip clip, float at)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0f; s.spatialBlend = 0f; s.playOnAwake = false;
            if (clip != null) { s.time = clip.length * at; s.Play(); }
            return new Layer { src = s };
        }

        /// <summary>
        /// A small four-cylinder's note, as a 2 s loop to be pitched with the revs: firing pulses at 55 Hz (each a short
        /// decaying buzz, a little different from the last, so it chugs rather than hums), its harmonics, a sub at half
        /// that, and the exhaust's rasp (noise gated by the firing). Every part repeats a whole number of times in the
        /// loop, so it loops without a seam.
        /// </summary>
        static AudioClip EngineClip()
        {
            const int rate = 44100, n = rate * 2;
            var d = new float[n];
            var rs = Random.state; Random.InitState(7317);
            const float fire = 55f;
            int per = Mathf.RoundToInt(rate / fire);     // a firing every `per` samples: 110 of them in the loop
            int firings = n / per;
            float lp = 0f;
            var amp = new float[firings + 1];
            for (int k = 0; k <= firings; k++) amp[k] = 0.75f + 0.25f * Random.value;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                int k = i / per; float ph = (i % per) / (float)per;
                float pulse = Mathf.Exp(-ph * 5.5f) * amp[Mathf.Min(k, firings)];
                float tone = 0.55f * Mathf.Sin(2f * Mathf.PI * fire * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * fire * 2f * t + 0.6f)
                           + 0.16f * Mathf.Sin(2f * Mathf.PI * fire * 3f * t + 1.3f) + 0.35f * Mathf.Sin(2f * Mathf.PI * fire * 0.5f * t);
                float w = Random.value * 2f - 1f;
                lp += (w - lp) * 0.25f;
                d[i] = (tone * (0.55f + 0.45f * pulse) + lp * pulse * 0.9f) * 0.42f;
            }
            // the last firings blended into the first, so the noise's seam is not heard
            int fade = per * 4;
            for (int i = 0; i < fade; i++) { float a = i / (float)fade; d[n - fade + i] = Mathf.Lerp(d[n - fade + i], d[i], a); }
            Random.state = rs;
            Normalize(d, 0.7f);
            var clip = AudioClip.Create("CarEngine", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// <summary>A plastic hull sliding on snow: a soft, hollow hiss (noise, band-limited low, a slow swell in it), 2 s, looped.</summary>
        static AudioClip HullClip()
        {
            const int rate = 44100, n = rate * 2;
            var d = new float[n];
            var rs = Random.state; Random.InitState(4421);
            float a1 = 0f, a2 = 0f, body = 0f, bv = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float w = Random.value * 2f - 1f;
                a1 += (w - a1) * 0.18f; a2 += (a1 - a2) * 0.05f;        // a hiss without its top
                // the hull's hollow: a soft resonance near 180 Hz the hiss rings
                float f = 2f * Mathf.PI * 180f / rate;
                bv += -body * f * f - bv * 0.05f + (a1 - a2) * 0.002f;
                body += bv;
                float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 1.5f * t) * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
                d[i] = ((a1 - a2) * 0.9f + body * 0.6f) * swell;
            }
            int fade = rate / 5;
            for (int i = 0; i < fade; i++) { float a = i / (float)fade; d[n - fade + i] = Mathf.Lerp(d[n - fade + i], d[i], a); }
            Random.state = rs;
            Normalize(d, 0.6f);
            var clip = AudioClip.Create("KayakHull", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 1e-6f;
            foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        /// <summary>SoundMix.Set's part: the engine, the hull, the bear's paws.</summary>
        void SetVehicles(RideSound r, float dt)
        {
            if (engine == null) return;
            float s = Mathf.Clamp01(r.speed);
            bool car = r.vehicle == (int)RideKind.Car, kayak = r.vehicle == (int)RideKind.Kayak, bear = r.vehicle == (int)RideKind.Bear;
            // the engine: idling at the gate, pitched by the revs through the gears, louder pulling (the throttle), and
            // a little quieter off the throttle; up in the air it revs on
            float rpm = r.riding ? r.rpm : 0.22f;
            Mix(engine, car && !r.ended ? 0.30f + 0.25f * r.throttle + 0.1f * s : 0f, 0.55f + 1.55f * rpm, 6f, 3f, dt);
            // the hull on the snow
            Mix(hull, kayak && r.onSnow ? 0.15f + 0.45f * s : 0f, 0.75f + 0.5f * s, 20f, 15f, dt);
            // the bear's paws (the wildlife bear's gallop), quicker with the gait
            Mix(bearRun, bear && r.onSnow && r.riding ? 0.35f + 0.35f * s : 0f, Mathf.Clamp(0.75f + 0.25f * r.gait, 0.75f, 1.45f), 12f, 6f, dt);
            // a grunt playing out: faded away after its moment
            if (bearVoice != null && bearVoice.isPlaying)
            {
                bearVoiceT += dt;
                bearVoice.volume = Mathf.Lerp(0f, 0.85f, Mathf.Clamp01(bearVoiceT / 0.05f)) * Mathf.Clamp01((bearVoiceFor - bearVoiceT) / 0.25f);
                if (bearVoiceT >= bearVoiceFor) bearVoice.Stop();
            }
        }

        /// <summary>A paddle stroke on the left (+1) or the right (-1): a swish of the blade through the snow, from that side.</summary>
        public void Stroke(int side)
        {
            if (strokeSwish == null || shots == null) return;
            var src = shots[nextShot]; nextShot = (nextShot + 1) % shots.Length;
            src.pitch = Random.Range(1.25f, 1.45f); src.panStereo = side > 0 ? -0.35f : 0.35f;
            src.PlayOneShot(strokeSwish, 0.32f);
            src.panStereo = 0f;
        }

        /// <summary>The ridden bear grunts (a landing, a hit): a moment of its growl, from somewhere in it.</summary>
        public void BearGrunt(float amount)
        {
            if (bearVoice == null || bearVoice.clip == null) return;
            bearVoice.Stop();
            bearVoice.time = Random.Range(0f, Mathf.Max(0f, bearVoice.clip.length - 1f));
            bearVoice.pitch = Random.Range(0.92f, 1.08f);
            bearVoiceT = 0f; bearVoiceFor = Mathf.Lerp(0.45f, 0.9f, Mathf.Clamp01(amount));
            bearVoice.volume = 0f;
            bearVoice.Play();
        }

        /// <summary>The ridden bear roars (it leaps).</summary>
        public void BearRoar() { Play(Sfx.BearRoar, 0.7f); }
    }
}
