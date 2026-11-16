using UnityEngine;

namespace Summit
{
    /// <summary>
    /// All sound is synthesised: wind that rises with speed, the hiss of the
    /// edge while carving, and a low rumble as the slide closes in.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class WindAudio : MonoBehaviour
    {
        // written on the main thread, read on the audio thread
        volatile float speed01, carve, rumble, master = 1f;
        uint rng = 2463534242u;
        float w1, w2, c1, c2, r1, r2;

        void Start()
        {
            var src = GetComponent<AudioSource>();
            // a silent looping clip keeps the source running so the filter below is called
            var clip = AudioClip.Create("silence", 4410, 1, 44100, false);
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();
        }

        public void Set(float speed01, float carve, float rumble, float master)
        {
            this.speed01 = Mathf.Clamp01(speed01);
            this.carve = Mathf.Clamp01(carve);
            this.rumble = Mathf.Clamp01(rumble);
            this.master = Mathf.Clamp01(master);
        }

        float White()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng / 4294967296f) * 2f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float s = speed01, c = carve, r = rumble, m = master;
            float kw = 0.015f + 0.12f * s;      // wind brightens as you go faster
            float vw = (0.05f + 0.55f * s * s) * m;
            float vc = c * (0.25f + 0.5f * s) * m;
            float vr = r * 0.9f * m;
            for (int i = 0; i < data.Length; i += channels)
            {
                float n = White();
                w1 += (n - w1) * kw; w2 += (w1 - w2) * kw;
                c1 += (n - c1) * 0.45f; c2 += (c1 - c2) * 0.08f;   // band-passed hiss
                r1 += (n - r1) * 0.004f; r2 += (r1 - r2) * 0.004f;
                float v = w2 * vw * 3f + (c1 - c2) * vc + r2 * vr * 30f;
                v = v / (1f + Mathf.Abs(v));   // soft clip
                for (int ch = 0; ch < channels; ch++) data[i + ch] = v;
            }
        }
    }
}
