using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Sparks off a rail: while the board slides along a steel rail a few small hot sparks come off where it
    /// touches, thrown along and out and falling away behind. Kept subtle: a handful a second, a little more with
    /// speed, each a short bright streak that is gone in a third of a second. Boxes are plastic-topped: no sparks.
    /// </summary>
    public partial class RiderBody
    {
        ParticleSystem sparks;
        float sparkCarry;
        public static float SparksSlow = 12f, SparksFast = 42f;   // a second, at a crawl and flat out

        /// <summary>Test tools: how many sparks are in the air.</summary>
        public int SparkCount => sparks != null ? sparks.particleCount : 0;

        void GrindSparks(Vector3 p, float dt)
        {
            bool on = rider.Running && !rider.Crashed && rider.Grinding && rider.GrindFeature.kind == Course.Kind.Rail && rider.Speed > 3f;
            if (!on || dt <= 0f) { sparkCarry = 0f; return; }
            if (sparks == null) BuildSparks();
            Vector3 v = rider.Velocity, along = v.sqrMagnitude > 0.01f ? v.normalized : rider.GrindFeature.Dir;
            Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
            Vector3 at = p + Vector3.down * (Rider.R - 0.02f);   // where the board's base meets the rail
            sparkCarry += dt * Mathf.Lerp(SparksSlow, SparksFast, Mathf.Clamp01((rider.Speed - 4f) / 16f));
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            while (sparkCarry >= 1f)
            {
                sparkCarry -= 1f;
                // ground off the steel: carried along nearly as fast as the board (so they hang just behind it
                // rather than being left metres back), flicked out to the sides and a little up, then they drop
                ep.position = at + along * Random.Range(-0.12f, 0.12f) + side * Random.Range(-0.04f, 0.04f);
                ep.velocity = v * Random.Range(0.72f, 0.97f) + side * Random.Range(-1.5f, 1.5f) + Vector3.up * Random.Range(0.4f, 2.0f);
                ep.startLifetime = Random.Range(0.16f, 0.36f);
                ep.startSize = Random.Range(0.02f, 0.038f);
                sparks.Emit(ep, 1);
            }
        }

        void BuildSparks()
        {
            var g = new GameObject("GrindSparks");
            g.transform.SetParent(transform.parent, false);   // left in the world as the rider slides on
            sparks = g.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.gravityModifier = 1.2f;
            main.startColor = new Color(1f, 0.86f, 0.55f, 1f);
            var em = sparks.emission; em.rateOverTime = 0f;
            var sh = sparks.shape; sh.enabled = false;
            // white-hot, cooling through orange as it dies
            var col = sparks.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.97f, 0.85f), 0f), new GradientColorKey(new Color(1f, 0.62f, 0.2f), 0.5f), new GradientColorKey(new Color(0.9f, 0.3f, 0.08f), 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = g.GetComponent<ParticleSystemRenderer>();
            // drawn as short streaks along the way each one is flying
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.004f; r.lengthScale = 1.6f;   // (they fly at the board's speed: a little stretch goes a long way)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            // the snow spray's soft dot, but added to the picture so it glows (the same blend as the snow glints)
            var basis = spray != null ? spray.GetComponent<ParticleSystemRenderer>().sharedMaterial : null;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = shader != null ? new Material(shader) : new Material(basis);
            if (basis != null && basis.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", basis.GetTexture("_BaseMap"));
            m.SetColor("_BaseColor", new Color(2.4f, 2.0f, 1.4f, 1f));   // brighter than the snow, so bloom gives each a small halo
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            r.material = m;
            sparks.Play();
        }
    }
}
