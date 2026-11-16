using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The slide behind you. It paces itself on how fast you have been going,
    /// leans in when you open a lead, and closes the gap every time you crash.
    /// </summary>
    public class Avalanche
    {
        public float Z { get; private set; }
        public float Elapsed { get; private set; }
        float ema;

        public void Reset(float riderZ)
        {
            Z = riderZ - 120f;
            ema = 0f;
            Elapsed = 0f;
        }

        public float Gap(Rider r) { return r.Z - Z; }

        public void Step(float dt, Rider r)
        {
            Elapsed += dt;
            ema = Mathf.Lerp(ema, r.Vz, 1f - Mathf.Exp(-dt / 6f));
            float d = Mathf.Clamp01(Elapsed / 240f);        // harder over four minutes
            float target = Mathf.Lerp(130f, 75f, d);
            float gap = Gap(r);
            float pace;
            if (Elapsed < 8f) pace = 8f;                     // grace while you get going
            else pace = ema * 0.94f + Mathf.Clamp((gap - target) * 0.10f, -3f, 14f);
            pace = Mathf.Max(pace, 6f + 10f * d);
            Z += pace * dt;
        }
    }
}
