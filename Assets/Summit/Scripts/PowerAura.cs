using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// How the rider shows a power-up:
    ///  - Invincible: a shimmering aura that cycles through warm colours round the whole body, and a trail
    ///    of gold sparks; it flickers over the last two seconds so you know it is running out
    ///  - Super Jump: pink sparks fizzing off the board, more of them in the air
    /// </summary>
    public class PowerAura : MonoBehaviour
    {
        Rider rider;
        ParticleSystem glow, sparks, fizz;
        ParticleSystem.EmissionModule glowEm, sparksEm, fizzEm;

        public void Build(Rider r, Material soft)
        {
            rider = r;
            var add = new Material(soft);
            add.SetFloat("_Surface", 1f);
            add.SetFloat("_Blend", 2f);   // additive: it lights up whatever is behind it
            add.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            add.SetFloat("_DstBlend", (float)BlendMode.One);
            add.SetFloat("_ZWrite", 0f);
            add.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            add.renderQueue = (int)RenderQueue.Transparent + 10;
            add.SetColor("_BaseColor", new Color(1.6f, 1.6f, 1.6f, 1f));

            // the aura: big soft glows that ride with the body (local space), so it wraps the rider even at speed
            glow = System("Aura", add, 120, ParticleSystemSimulationSpace.Local);
            var gm = glow.main;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            gm.startSize = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            var gs = glow.shape; gs.enabled = true; gs.shapeType = ParticleSystemShapeType.Sphere; gs.radius = 0.45f;
            gs.position = new Vector3(0f, 0.6f, 0f);
            Pulse(glow, 0.32f);
            glowEm = glow.emission;

            // sparks left behind in the world as you go
            sparks = System("AuraSparks", add, 300, ParticleSystemSimulationSpace.World);
            var sm = sparks.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.4f, 1f), new Color(1f, 1f, 0.9f, 1f));
            sm.gravityModifier = -0.1f;
            var ss = sparks.shape; ss.enabled = true; ss.shapeType = ParticleSystemShapeType.Sphere; ss.radius = 0.5f;
            ss.position = new Vector3(0f, 0.6f, 0f);
            Pulse(sparks, 1f);
            sparksEm = sparks.emission;

            // super jump: pink fizz off the board
            fizz = System("JumpFizz", add, 200, ParticleSystemSimulationSpace.World);
            var fm = fizz.main;
            fm.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            fm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            fm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
            fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.75f, 1f), new Color(1f, 0.85f, 0.95f, 1f));
            fm.gravityModifier = 0.4f;
            var fs = fizz.shape; fs.enabled = true; fs.shapeType = ParticleSystemShapeType.Box; fs.scale = new Vector3(0.35f, 0.05f, 1.3f);
            fs.position = new Vector3(0f, -Rider.R, 0f);
            Pulse(fizz, 1f);
            fizzEm = fizz.emission;
        }

        ParticleSystem System(string name, Material m, int max, ParticleSystemSimulationSpace space)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = space; main.maxParticles = max; main.playOnAwake = false; main.startSpeed = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // fade in quickly, hold, fade out
        static void Pulse(ParticleSystem ps, float a)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a, 0.25f), new GradientAlphaKey(a, 0.6f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }

        void LateUpdate() { Step(Time.time); }

        /// <summary>Sets how much each effect emits this frame (public so the test tools can drive it).</summary>
        public void Step(float time)
        {
            if (rider == null) return;
            bool live = rider.Running && !rider.Crashed;
            float inv = live ? rider.InvincibleTime : 0f;
            // the last two seconds flicker
            bool on = inv > 0f && (inv > 2f || Mathf.Repeat(inv * 5f, 1f) < 0.6f);
            glowEm.rateOverTime = on ? 90f : 0f;
            sparksEm.rateOverTime = on ? 40f + rider.Speed * 2f : 0f;
            if (on)
            {
                // the aura's colour slides round gold, white, rose and back
                float h = Mathf.Repeat(time * 0.35f, 1f);
                var a = Color.HSVToRGB(Mathf.Lerp(0.08f, 0.16f, Mathf.PingPong(h * 2f, 1f)), 0.75f, 1f);
                var b = Color.HSVToRGB(Mathf.Repeat(0.95f + h * 0.25f, 1f), 0.45f, 1f);
                var gm = glow.main; gm.startColor = new ParticleSystem.MinMaxGradient(a, b);
            }
            float jump = live ? rider.SuperJumpTime : 0f;
            fizzEm.rateOverTime = jump > 0f && (jump > 2f || Mathf.Repeat(jump * 5f, 1f) < 0.6f) ? (rider.Airborne ? 70f : 22f) : 0f;
        }

        public void Clear() { glow?.Clear(); sparks?.Clear(); fizz?.Clear(); }
    }
}
