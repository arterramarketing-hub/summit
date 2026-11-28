using UnityEngine;

namespace Summit
{
    /// <summary>The camp fire's flicker: its light (round whatever the evening has set it to), the embers' and the burning
    /// logs' glow, and its smoke, pale by day and dark against the night.</summary>
    public class CampFireFlicker : MonoBehaviour
    {
        public Camp camp;
        public Light lamp;
        public Material[] glows;
        public Color[] glowBase;
        public Material smoke;
        float baseI, lastSet = -1f;
        Vector3 home;
        bool homed;
        static readonly Color SmokeDay = new Color(0.82f, 0.84f, 0.88f, 1f), SmokeNight = new Color(0.22f, 0.2f, 0.2f, 1f);

        void LateUpdate()
        {
            float t = Time.time;
            float f = 0.78f + 0.24f * Mathf.PerlinNoise(t * 6.5f, 0.3f) + 0.14f * Mathf.PerlinNoise(t * 17f, 2.1f);
            if (lamp != null)
            {
                if (!homed) { home = lamp.transform.localPosition; homed = true; }
                // (Camp.SetNight sets the lamps every frame it runs: whatever it set is the base; ours is never it)
                if (!Mathf.Approximately(lamp.intensity, lastSet)) baseI = lamp.intensity;
                lamp.intensity = lastSet = baseI * f;
                lamp.transform.localPosition = home + new Vector3(Mathf.PerlinNoise(t * 3f, 9f) - 0.5f, Mathf.PerlinNoise(t * 4f, 4f) - 0.5f, Mathf.PerlinNoise(t * 3f, 1f) - 0.5f) * 0.12f;
            }
            if (glows != null)
            {
                float g = 0.8f + 0.3f * Mathf.PerlinNoise(t * 2.2f, 5.7f) + 0.1f * (f - 0.9f);
                for (int i = 0; i < glows.Length; i++)
                    if (glows[i] != null) glows[i].SetColor("_EmissionColor", glowBase[i] * g);
            }
            if (smoke != null && camp != null)
                smoke.SetColor("_BaseColor", Color.Lerp(SmokeDay, SmokeNight, camp.Night));
        }
    }
}
