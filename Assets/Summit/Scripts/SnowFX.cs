using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Snow effects that happen out in the world:
    ///  - glints: fresh snow catching the sun, tiny star points twinkling on the surface ahead of you
    ///  - tree shake: when you hit a tree, the snow on its branches comes down in clumps and powder
    ///  - puffs: snow kicked up where the thrown body hits and slides
    ///  - crack: a burst of white stars and snow where a bone gives way
    /// </summary>
    public class SnowFX : MonoBehaviour
    {
        public Camera cam;
        ParticleSystem glints, clumps, mist, puffs, stars;
        Transform groundPlane;
        float glintCarry;

        public void Build(Camera c, Material soft)
        {
            cam = c;
            var add = Additive(Sparkle());
            glints = System("Glints", add, 1400);
            var gm = glints.main;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.5f);
            gm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            // mostly white, some with the faint colour a snow crystal splits the sun into
            var tints = new Gradient();
            tints.mode = GradientMode.Fixed;
            tints.SetKeys(new[] { new GradientColorKey(new Color(1f, 1f, 1f), 0.55f), new GradientColorKey(new Color(0.75f, 0.92f, 1f), 0.7f),
                                  new GradientColorKey(new Color(1f, 0.93f, 0.7f), 0.85f), new GradientColorKey(new Color(1f, 0.8f, 0.92f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            gm.startColor = new ParticleSystem.MinMaxGradient(tints) { mode = ParticleSystemGradientMode.RandomColor };
            var gsz = glints.sizeOverLifetime; gsz.enabled = true;     // a twinkle: flash up and gone
            gsz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
            var grot = glints.main; grot.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);

            clumps = System("TreeSnow", soft, 600);
            var cm = clumps.main;
            cm.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
            cm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
            cm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(0.86f, 0.9f, 0.97f, 1f));
            cm.gravityModifier = 1.0f;
            var col = clumps.collision;             // land on the snow (a plane under the tree) and settle
            col.enabled = true; col.type = ParticleSystemCollisionType.Planes;
            groundPlane = new GameObject("TreeSnowGround").transform; groundPlane.SetParent(transform, false);
            col.SetPlane(0, groundPlane);
            col.dampen = 0.9f; col.bounce = 0.05f; col.lifetimeLoss = 0.35f;
            Fade(clumps, 0.85f);

            mist = System("TreeMist", soft, 300);
            var mm = mist.main;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
            mm.startColor = new Color(1f, 1f, 1f, 0.45f);
            mm.gravityModifier = 0.12f;
            Fade(mist, 0.5f);
            var msz = mist.sizeOverLifetime; msz.enabled = true; msz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

            puffs = System("ImpactPuffs", soft, 400);
            var pm = puffs.main;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            pm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            pm.startColor = new Color(1f, 1f, 1f, 0.8f);
            pm.gravityModifier = 0.35f;
            Fade(puffs, 0.8f);
            var psz = puffs.sizeOverLifetime; psz.enabled = true; psz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.7f));

            stars = System("CrackStars", add, 200);
            var sm = stars.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(1f, 0.92f, 0.6f, 1f));
            var ssz = stars.sizeOverLifetime; ssz.enabled = true; ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        }

        ParticleSystem System(string name, Material m, int max)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Fade(ParticleSystem ps, float a)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(a, 0f), new GradientAlphaKey(a, 0.6f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }

        /// <summary>A four-point glint with a soft core.</summary>
        static Texture2D Sparkle()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Abs(x - c) / c, dy = Mathf.Abs(y - c) / c, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(1f - d * 2.2f);
                    float rays = Mathf.Clamp01(1f - dx * 1.05f) * Mathf.Clamp01(1f - dy * 9f) + Mathf.Clamp01(1f - dy * 1.05f) * Mathf.Clamp01(1f - dx * 9f);
                    float a = Mathf.Clamp01(core * core * 1.5f + rays * rays);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static Material Additive(Texture2D tex)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = new Material(sh);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(3.2f, 3.2f, 3.2f, 1f));   // far brighter than white snow, so bloom gives each a halo
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        void LateUpdate()
        {
            if (cam == null || glints == null) return;
            // fresh snow glitters: a steady sprinkle of glints on the surface in view, densest close by
            glintCarry += Time.deltaTime * 1000f;
            int n = (int)glintCarry; glintCarry -= n;
            Sprinkle(n);
        }

        /// <summary>Scatter n glints on the snow in view (public so the test harness can call it).</summary>
        public void Sprinkle(int n)
        {
            var ep = new ParticleSystem.EmitParams();
            Vector3 cp = cam.transform.position, f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return;
            f.Normalize();
            Vector3 r = new Vector3(f.z, 0f, -f.x);
            for (int i = 0; i < n; i++)
            {
                float u = Random.value, dist = 2.5f + 32f * u * u;
                float side = (Random.value * 2f - 1f) * dist * 0.62f;
                Vector3 p = cp + f * dist + r * side;
                p.y = Slope.DrawnHeight(p.x, p.z) + 0.03f;
                ep.position = p;
                glints.Emit(ep, 1);
            }
        }

        /// <summary>Snow shaken off a tree you hit: clumps from the branches and a cloud of powder.</summary>
        public void TreeShake(Vector3 trunk, float force)
        {
            float g = Slope.DrawnHeight(trunk.x, trunk.z);
            Vector3 n = Slope.Normal(trunk.x, trunk.z);
            groundPlane.SetPositionAndRotation(new Vector3(trunk.x, g, trunk.z), Quaternion.FromToRotation(Vector3.up, n));
            int k = Mathf.RoundToInt(Mathf.Lerp(50f, 140f, Mathf.Clamp01(force / 14f)));
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < k; i++)
            {
                Vector2 d = Random.insideUnitCircle * 2.2f;
                float h = Random.Range(1.6f, 7f);
                ep.position = new Vector3(trunk.x + d.x * (1.1f - h / 9f), g + h, trunk.z + d.y * (1.1f - h / 9f));
                ep.velocity = new Vector3(d.x * 0.4f, Random.Range(-0.5f, 0.6f), d.y * 0.4f);
                clumps.Emit(ep, 1);
            }
            for (int i = 0; i < k / 4; i++)
            {
                Vector2 d = Random.insideUnitCircle * 2.5f;
                ep.position = new Vector3(trunk.x + d.x, g + Random.Range(1f, 6f), trunk.z + d.y);
                ep.velocity = new Vector3(d.x * 0.3f, -0.3f, d.y * 0.3f);
                mist.Emit(ep, 1);
            }
        }

        /// <summary>Snow kicked up where something hits the snow.</summary>
        public void Puff(Vector3 at, float force)
        {
            var ep = new ParticleSystem.EmitParams();
            int k = Mathf.RoundToInt(Mathf.Lerp(4f, 18f, Mathf.Clamp01(force / 10f)));
            for (int i = 0; i < k; i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.2f;
                ep.velocity = (Random.insideUnitSphere + Vector3.up * 1.2f) * Random.Range(0.8f, 2.2f) * Mathf.Clamp(force / 6f, 0.5f, 1.6f);
                puffs.Emit(ep, 1);
            }
        }

        /// <summary>A bone giving way: a flash of white stars and a burst of snow.</summary>
        public void Crack(Vector3 at)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < 26; i++)
            {
                ep.position = at;
                ep.velocity = Random.onUnitSphere * Random.Range(2f, 5f) + Vector3.up;
                stars.Emit(ep, 1);
            }
            Puff(at, 10f);
        }

        public void Clear()
        {
            if (clumps == null) return;
            clumps.Clear(); mist.Clear(); puffs.Clear(); stars.Clear();
        }
    }
}
