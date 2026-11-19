using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Snow effects that happen out in the world:
    ///  - glints: fresh snow catching the sun, tiny star points twinkling on the surface ahead of you
    ///  - tree shake: when you hit a tree, the snow on its branches comes down in clumps and powder
    ///  - puffs: snow kicked up where the thrown body hits and slides
    ///  - thud: a few soft puffs where the thrown body lands hard (the hole itself is in SnowLayer)
    /// </summary>
    public class SnowFX : MonoBehaviour
    {
        public Camera cam;
        ParticleSystem glints, clumps, mist, puffs, chunks;
        Transform groundPlane;
        float glintCarry;
        /// <summary>Glints per second relative to full sun, and their colour (DayNight: fewer and bluer by moonlight).</summary>
        public float glintRate = 1f;
        Material glintMat;
        public void TintGlints(Color c) { if (glintMat != null) glintMat.SetColor("_BaseColor", new Color(2.1f * c.r, 2.1f * c.g, 2.1f * c.b, 1f)); }

        public void Build(Camera c, Material soft)
        {
            cam = c;
            var add = Additive(Sparkle());
            glintMat = add;
            glints = System("Glints", add, 1400);
            var gm = glints.main;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.5f);
            gm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
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

            clumps = System("TreeSnow", soft, 1400);
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

            mist = System("TreeMist", soft, 700);
            var mm = mist.main;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
            mm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.45f), new Color(0.84f, 0.89f, 0.97f, 0.45f));
            mm.gravityModifier = 0.12f;
            Fade(mist, 0.5f);
            var msz = mist.sizeOverLifetime; msz.enabled = true; msz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

            puffs = System("ImpactPuffs", soft, 400);
            var pm = puffs.main;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            pm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            pm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.85f), new Color(0.80f, 0.86f, 0.95f, 0.85f));   // some in shade, so a burst reads against the snow
            pm.gravityModifier = 0.35f;
            Fade(puffs, 0.8f);
            var psz = puffs.sizeOverLifetime; psz.enabled = true; psz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.7f));

            // lumps of snow thrown by an impact: they arc out and drop
            chunks = System("Chunks", soft, 500);
            var km = chunks.main;
            km.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            km.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
            km.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(0.85f, 0.89f, 0.96f, 1f));
            km.gravityModifier = 1.3f;
            var ksz = chunks.sizeOverLifetime; ksz.enabled = true; ksz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
            Fade(chunks, 1f);
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
            m.SetColor("_BaseColor", new Color(2.1f, 2.1f, 2.1f, 1f));   // brighter than white snow, so bloom gives each a soft halo
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
            glintCarry += Time.deltaTime * 420f * glintRate;
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
        /// <summary>
        /// All the snow on a tree lets go at once (it is hit hard, snapped or smashed): clumps and slabs off every
        /// bough from the foot of the crown to its top, the broadest low down, and a veil of powder coming down round
        /// it. foot: where the trunk meets the snow; height: how much of the tree stands above it (m); crown: its
        /// widest reach (m); force: how hard it was hit (m/s): a glancing knock dumps less.
        /// </summary>
        public void TreeDump(Vector3 foot, float height, float crown, float force)
        {
            float g = Slope.DrawnHeight(foot.x, foot.z);
            Ground(foot);
            float k = Mathf.Clamp01(force / 14f);
            height = Mathf.Max(height, 2f); crown = Mathf.Clamp(crown, 0.8f, 5f);
            int n = Mathf.RoundToInt(Mathf.Lerp(45f, 150f, k) * Mathf.Clamp(height / 9f, 0.7f, 1.9f));
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                float f = Random.Range(0.1f, 0.97f);
                Vector2 d = Random.insideUnitCircle * crown * (1.05f - f * 0.85f);
                ep.position = new Vector3(foot.x + d.x, g + f * height, foot.z + d.y);
                ep.velocity = new Vector3(d.x * 0.35f, Random.Range(-0.8f, 0.4f), d.y * 0.35f);
                bool slab = Random.value < 0.22f;   // whole slabs off the boughs, among the clumps
                ep.startSize = slab ? Random.Range(0.45f, 1.0f) : Random.Range(0.12f, 0.4f);
                clumps.Emit(ep, 1);
            }
            for (int i = 0; i < n / 3; i++)
            {
                float f = Random.Range(0.1f, 0.9f);
                Vector2 d = Random.insideUnitCircle * crown * (1.1f - f * 0.7f);
                ep.position = new Vector3(foot.x + d.x, g + f * height, foot.z + d.y);
                ep.velocity = new Vector3(d.x * 0.25f, Random.Range(-1.2f, -0.3f), d.y * 0.25f);
                ep.startSize = Random.Range(1.0f, 2.2f);
                mist.Emit(ep, 1);
            }
        }

        /// <summary>
        /// A falling top shedding its snow as it goes over: clumps and slabs drop off along the crown and a curtain
        /// of powder hangs where it swept through. Call every frame while it falls; tipVel is how fast its tip
        /// moves, left the share of its snow still on it (it runs out).
        /// </summary>
        public void TreeTrail(Vector3 butt, Vector3 tip, Vector3 tipVel, float dt, float left)
        {
            if (left <= 0.02f || dt <= 0f) return;
            float speed = tipVel.magnitude;
            trailCarry += (60f + speed * 26f) * left * dt;
            int n = (int)trailCarry; trailCarry -= n;
            if (n <= 0) return;
            Ground(Vector3.Lerp(butt, tip, 0.5f));
            var ep = new ParticleSystem.EmitParams();
            float len = Vector3.Distance(butt, tip);
            for (int i = 0; i < n; i++)
            {
                float s = Random.Range(0.25f, 1f);
                ep.position = Vector3.Lerp(butt, tip, s) + Random.insideUnitSphere * Mathf.Lerp(0.4f, 1.6f, 1f - s) * Mathf.Min(1f, len / 8f);
                bool powder = Random.value < 0.5f;
                if (powder)
                {
                    // the curtain: powder left hanging behind the crown, sinking slowly
                    ep.velocity = tipVel * s * 0.12f + Vector3.down * Random.Range(0.2f, 0.8f);
                    ep.startSize = Random.Range(1.2f, 2.6f);
                    mist.Emit(ep, 1);
                }
                else
                {
                    // the snow it was carrying lets go and drops, with some of the crown's swing
                    ep.velocity = tipVel * s * Random.Range(0.25f, 0.6f) + Random.insideUnitSphere * 0.6f;
                    ep.startSize = Random.value < 0.25f ? Random.Range(0.4f, 0.9f) : Random.Range(0.12f, 0.38f);
                    clumps.Emit(ep, 1);
                }
            }
        }
        float trailCarry;

        /// <summary>A falling top hits the snow: a burst of powder along where the crown lands, rolling out low.</summary>
        public void TopLands(Vector3 butt, Vector3 tip, float force)
        {
            for (int i = 0; i < 4; i++)
            {
                float s = 0.3f + 0.22f * i;
                Impact(Vector3.Lerp(butt, tip, s), force * (0.55f + 0.45f * s), Vector3.zero);
            }
            var ep = new ParticleSystem.EmitParams();
            Vector3 along = tip - butt; along.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.forward);
            int n = Mathf.RoundToInt(Mathf.Lerp(10f, 34f, Mathf.Clamp01(force / 14f)));
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Vector3.Lerp(butt, tip, Random.Range(0.25f, 1f));
                p.y = Slope.DrawnHeight(p.x, p.z) + 0.4f;
                ep.position = p;
                ep.velocity = side * Random.Range(-3f, 3f) + Vector3.up * Random.Range(0.3f, 1.6f);
                ep.startSize = Random.Range(1.1f, 2.4f);
                mist.Emit(ep, 1);
            }
        }

        void Ground(Vector3 at)
        {
            float g = Slope.DrawnHeight(at.x, at.z);
            groundPlane.SetPositionAndRotation(new Vector3(at.x, g, at.z), Quaternion.FromToRotation(Vector3.up, Slope.Normal(at.x, at.z)));
        }

        /// <summary>The snow shaken off a tree by a knock (the older call: a tree of the forest's usual size).</summary>
        public void TreeShake(Vector3 trunk, float force) { TreeDump(trunk, 7.5f, 2.2f, force); }

        /// <summary>Snow kicked up where something hits the snow.</summary>
        public void Puff(Vector3 at, float force)
        {
            var ep = new ParticleSystem.EmitParams();
            int k = Mathf.RoundToInt(Mathf.Lerp(2f, 8f, Mathf.Clamp01(force / 10f)));
            for (int i = 0; i < k; i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.2f;
                ep.velocity = (Random.insideUnitSphere + Vector3.up * 1.2f) * Random.Range(0.8f, 2.2f) * Mathf.Clamp(force / 6f, 0.5f, 1.6f);
                puffs.Emit(ep, 1);
            }
        }

        /// <summary>
        /// A body slamming into the snow: an explosion of powder, lumps thrown out in arcs, and a cloud that
        /// hangs and drifts. force is the impact speed (m/s).
        /// </summary>
        public void Impact(Vector3 at, float force, Vector3 carry)
        {
            float k = Mathf.Clamp01(force / 12f);
            var ep = new ParticleSystem.EmitParams();
            Vector3 n = Slope.Normal(at.x, at.z);
            int nPuff = Mathf.RoundToInt(Mathf.Lerp(3f, 12f, k));   // a puff of snow, not smoke
            for (int i = 0; i < nPuff; i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.3f;
                Vector3 d = (Random.insideUnitSphere * 0.9f + n * 1.4f).normalized;
                ep.velocity = d * Random.Range(1.2f, 4.5f) * (0.6f + k) + carry * 0.25f;
                ep.startSize = Random.Range(0.2f, 0.55f) * (0.7f + 0.5f * k);
                puffs.Emit(ep, 1);
            }
            int nChunk = Mathf.RoundToInt(Mathf.Lerp(5f, 22f, k));
            for (int i = 0; i < nChunk; i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.2f;
                Vector3 d = (Random.insideUnitSphere + n * 1.6f).normalized;
                ep.velocity = d * Random.Range(2.5f, 7f) * (0.5f + k) + carry * 0.35f;
                ep.startSize = Random.Range(0.06f, 0.2f);
                chunks.Emit(ep, 1);
            }
            for (int i = 0; i < Mathf.RoundToInt(k * 2f); i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.6f + n * 0.4f;
                ep.velocity = Random.insideUnitSphere * 0.6f + carry * 0.15f;
                ep.startSize = Random.Range(0.8f, 1.4f);
                mist.Emit(ep, 1);
            }
        }

        /// <summary>
        /// Coming down from a jump: a ring of powder blasted out low along the snow, a spray thrown
        /// ahead in the direction you are going, and lumps for a hard one. impact is the speed into the snow.
        /// </summary>
        public void Landing(Vector3 at, Vector3 n, float impact, Vector3 travel)
        {
            float k = Mathf.Clamp01((impact - 2f) / 12f);
            var ep = new ParticleSystem.EmitParams();
            Vector3 t1 = Vector3.Cross(n, Vector3.right).normalized, t2 = Vector3.Cross(n, t1);
            Vector3 fwd = Vector3.ProjectOnPlane(travel, n);
            int ring = Mathf.RoundToInt(Mathf.Lerp(14f, 44f, k));
            for (int i = 0; i < ring; i++)
            {
                float a = i / (float)ring * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                Vector3 d = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                ep.position = at + d * 0.35f + n * 0.1f;
                ep.velocity = d * Random.Range(2.5f, 5.5f) * (0.6f + k) + n * Random.Range(0.4f, 1.6f) + fwd * 0.35f;
                ep.startSize = Random.Range(0.3f, 0.8f) * (0.8f + 0.6f * k);
                puffs.Emit(ep, 1);
            }
            int ahead = Mathf.RoundToInt(Mathf.Lerp(8f, 26f, k));
            for (int i = 0; i < ahead; i++)
            {
                ep.position = at + n * 0.15f;
                ep.velocity = fwd * Random.Range(0.45f, 0.8f) + n * Random.Range(1.5f, 4f) * (0.6f + k) + Random.insideUnitSphere * 1.2f;
                ep.startSize = Random.Range(0.25f, 0.6f);
                puffs.Emit(ep, 1);
            }
            int lumps = Mathf.RoundToInt(Mathf.Lerp(0f, 22f, k));
            for (int i = 0; i < lumps; i++)
            {
                ep.position = at + n * 0.1f;
                ep.velocity = (Random.insideUnitSphere + n * 1.3f).normalized * Random.Range(2f, 5.5f) + fwd * 0.5f;
                ep.startSize = Random.Range(0.05f, 0.16f);
                chunks.Emit(ep, 1);
            }
        }

        /// <summary>A bone giving way: a little burst of snow (the hit itself is shown by the hole it leaves: SnowLayer).</summary>
        public void Crack(Vector3 at) { Thud(at, 9f, Vector3.zero); }

        /// <summary>
        /// A body landing hard in the snow: a few soft puffs of powder, low and quick, drifting on with the body.
        /// No lumps flying about: the snow it moved is in the hole and its rims (SnowLayer). force is the impact speed.
        /// </summary>
        public void Thud(Vector3 at, float force, Vector3 carry)
        {
            float k = Mathf.Clamp01((force - 4f) / 10f);
            var ep = new ParticleSystem.EmitParams();
            Vector3 n = Slope.Normal(at.x, at.z);
            int count = Mathf.RoundToInt(Mathf.Lerp(2f, 5f, k));
            for (int i = 0; i < count; i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.2f + n * 0.08f;
                ep.velocity = (Random.insideUnitSphere * 0.7f + n) * Random.Range(0.6f, 1.6f) * (0.7f + 0.6f * k) + carry * 0.2f;
                ep.startSize = Random.Range(0.25f, 0.5f) * (0.8f + 0.4f * k);
                puffs.Emit(ep, 1);
            }
        }

        public void Clear()
        {
            if (clumps == null) return;
            clumps.Clear(); mist.Clear(); puffs.Clear(); chunks.Clear();
        }
    }
}
