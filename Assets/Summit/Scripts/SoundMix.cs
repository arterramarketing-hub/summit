using UnityEngine;

namespace Summit
{
    public enum Sfx { Land, Jump, Crash, Trick, CloseCall, Click, Buried, Scrape }

    /// <summary>
    /// The sound of the ride, mixed from recorded clips in Resources/Audio:
    ///   wind  (loop)  rises and brightens with speed
    ///   carve (loop)  the edge on snow: a soft hiss gliding, a loud bite when you carve
    ///   land, crash, click, jump, trick (one-shots; any can be missing)
    /// A clip that is missing is replaced by a quiet generated stand-in, so the game always has sound.
    /// </summary>
    public class SoundMix : MonoBehaviour
    {
        AudioSource wind, carve;
        AudioSource[] shots;
        int nextShot;
        AudioClip land, crash, click, jump, trick;
        float windVol, carveVol, gust;

        public void Build()
        {
            wind = Loop("Audio/wind", MakeNoise(4f, 0.06f, 44100, 7));
            carve = Loop("Audio/carve", MakeNoise(3f, 0.35f, 44100, 11));
            shots = new AudioSource[6];
            for (int i = 0; i < shots.Length; i++)
            {
                shots[i] = gameObject.AddComponent<AudioSource>();
                shots[i].playOnAwake = false;
                shots[i].spatialBlend = 0f;
            }
            land = Load("Audio/land") ?? MakeThump(0.25f, 70f);
            crash = Load("Audio/crash") ?? land;
            click = Load("Audio/click") ?? MakeThump(0.04f, 900f);
            jump = Load("Audio/jump");
            trick = Load("Audio/trick");
        }

        static AudioClip Load(string path)
        {
            var c = Resources.Load<AudioClip>(path);
            return c;
        }

        AudioSource Loop(string path, AudioClip fallback)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = Load(path) ?? fallback;
            s.loop = true;
            s.volume = 0f;
            s.spatialBlend = 0f;
            s.playOnAwake = false;
            // start each loop somewhere different so they never phase together
            s.time = s.clip.length * Random.value * 0.9f;
            s.Play();
            return s;
        }

        /// <summary>
        /// speed01: 0 standing, 1 flat out. carve: 0..1 how hard the edge bites.
        /// master: overall level (dims when buried). onSnow: the board is on the snow. pan: -1..1.
        /// </summary>
        public void Set(float speed01, float carveAmt, float master, float onSnow, float pan)
        {
            if (wind == null) return;
            float dt = Time.deltaTime, s = Mathf.Clamp01(speed01);
            gust = Mathf.PerlinNoise(Time.time * 0.23f, 3.1f);
            // the wind: barely there when slow, a proper rush when flat out
            float wv = (0.035f + 0.40f * s * s) * (0.85f + 0.3f * gust) * master;
            windVol = Mathf.Lerp(windVol, wv, 1f - Mathf.Exp(-dt * 4f));
            wind.volume = windVol;
            wind.pitch = 0.85f + 0.35f * s + 0.05f * gust;
            // the edge: a quiet hiss when gliding, loud when the board bites
            float cv = onSnow * Mathf.Clamp01(s * 2.5f) * (0.10f + 0.75f * carveAmt * carveAmt + 0.15f * carveAmt) * master;
            carveVol = Mathf.Lerp(carveVol, cv, 1f - Mathf.Exp(-dt * (cv > carveVol ? 18f : 8f)));
            carve.volume = carveVol;
            carve.pitch = 0.85f + 0.25f * s + 0.12f * carveAmt;
            carve.panStereo = Mathf.Clamp(pan * 0.25f, -0.25f, 0.25f);
        }

        public void Play(Sfx kind, float amount = 1f)
        {
            AudioClip c = null; float vol = 0.6f, pitch = 1f;
            switch (kind)
            {
                case Sfx.Land: c = land; vol = 0.25f + 0.55f * Mathf.Clamp01(amount); pitch = Random.Range(0.92f, 1.08f); break;
                case Sfx.Crash: c = crash; vol = 0.9f; pitch = 0.8f; break;
                case Sfx.Buried: c = crash; vol = 0.7f; pitch = 0.6f; break;
                case Sfx.Scrape: c = crash; vol = 0.35f; pitch = 1.2f; break;
                case Sfx.Click: c = click; vol = 0.5f; break;
                case Sfx.Jump: c = jump; vol = 0.4f; pitch = Random.Range(0.95f, 1.05f); break;
                case Sfx.Trick: c = trick; vol = 0.4f; pitch = Mathf.Pow(1.0595f, Mathf.Clamp(amount, 0f, 8f) * 2f); break;
                case Sfx.CloseCall: c = trick; vol = 0.25f; pitch = 1.5f; break;
            }
            if (c == null) return;
            var s = shots[nextShot];
            nextShot = (nextShot + 1) % shots.Length;
            s.pitch = pitch;
            s.PlayOneShot(c, vol);
        }

        // ---------------------------------------------------------------- stand-ins

        /// <summary>A seamless loop of soft filtered noise (lower cutoff = darker).</summary>
        static AudioClip MakeNoise(float seconds, float cutoff, int rate, int seed)
        {
            int n = (int)(seconds * rate);
            var d = new float[n];
            var rng = new System.Random(seed);
            float a = 0f, b = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)rng.NextDouble() * 2f - 1f;
                a += (w - a) * cutoff;
                b += (a - b) * cutoff;
                d[i] = b;
            }
            // crossfade the ends so the loop has no click
            int fade = rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                d[i] = d[i] * t + d[n - fade + i] * (1f - t);
            }
            System.Array.Resize(ref d, n - fade);
            float peak = 1e-4f;
            foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
            for (int i = 0; i < d.Length; i++) d[i] *= 0.7f / peak;
            var clip = AudioClip.Create("noise", d.Length, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        static AudioClip MakeThump(float seconds, float hz)
        {
            int rate = 44100, n = (int)(seconds * rate);
            var d = new float[n];
            var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, env = Mathf.Exp(-t / (seconds * 0.25f));
                d[i] = env * (0.7f * Mathf.Sin(2f * Mathf.PI * hz * t) + 0.3f * ((float)rng.NextDouble() * 2f - 1f));
            }
            var clip = AudioClip.Create("thump", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
