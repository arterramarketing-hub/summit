using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// How the rider shows a power-up:
    ///  - Invincible: the rider's own colours flash through the rainbow, several times a second: the clothes, the gear,
    ///    face and hands, and the board, skis and poles, sled or tube. Nothing glows and nothing is added round the body.
    ///    The colours go on as property blocks over the rider's own materials. The materials themselves are never touched,
    ///    so clearing the blocks gives back exactly the colours that were there. Over the last two seconds the flashing
    ///    slows down and flickers back to the rider's own colours, so you know it is running out.
    ///  - Super Jump: pink sparks fizzing off the board, more of them in the air
    /// </summary>
    public class PowerAura : MonoBehaviour
    {
        Rider rider;
        RiderBody body;
        ParticleSystem fizz;
        ParticleSystem.EmissionModule fizzEm;

        // ---- the rainbow
        const float Fast = 10f;     // colour steps a second while it lasts
        const float Slow = 3.5f;    // ... slowing to this as it runs out
        const float Warn = 2f;      // the last seconds: slower, and every other step the rider's own colours
        const float TexTint = 0.7f; // pictures (the gear palette, a face, a print) are tinted, not painted over
        static readonly Color[] Rainbow =
        {
            new Color(1f, 0.12f, 0.12f), new Color(1f, 0.55f, 0.05f), new Color(1f, 0.92f, 0.1f),
            new Color(0.15f, 0.9f, 0.22f), new Color(0.12f, 0.5f, 1f), new Color(0.78f, 0.2f, 1f),
        };
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Color2 = Shader.PropertyToID("_Color2"),
                            BaseMap = Shader.PropertyToID("_BaseMap");

        /// <summary>One material of one renderer of the rider, and how it takes the rainbow.</summary>
        struct Slot
        {
            public Renderer r;
            public int index;       // the material's place on the renderer
            public bool picture;    // a texture: tinted (its detail stays), else painted with the colour
            public bool second;     // a pattern's second colour too (SnowyCloth)
            public int shift;       // steps along the rainbow, so the trousers aren't the jacket's colour
            public float shade;     // darker garments stay a little darker
            public float alpha;
        }
        Slot[] slots = new Slot[48];
        int slotCount, seenBuilt = -1, shown = -1;
        bool painted;
        float phase, lastTime = -1f;
        MaterialPropertyBlock block;
        readonly List<Renderer> found = new List<Renderer>();
        readonly List<Material> mats = new List<Material>();

        /// <summary>True while the rainbow is on the rider (the test tools read it).</summary>
        public bool Painted => painted;
        /// <summary>How many material slots the rainbow paints (the test tools read it).</summary>
        public int Slots => slotCount;

        public void Build(Rider r, Material soft)
        {
            rider = r;
            body = GetComponent<RiderBody>();
            block = new MaterialPropertyBlock();
            var add = new Material(soft);
            add.SetFloat("_Surface", 1f);
            add.SetFloat("_Blend", 2f);   // additive: it lights up whatever is behind it
            add.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            add.SetFloat("_DstBlend", (float)BlendMode.One);
            add.SetFloat("_ZWrite", 0f);
            add.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            add.renderQueue = (int)RenderQueue.Transparent + 10;
            add.SetColor("_BaseColor", new Color(1.6f, 1.6f, 1.6f, 1f));

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

        /// <summary>Sets this frame's look of each power-up (public so the test tools can drive it).</summary>
        public void Step(float time)
        {
            if (rider == null) return;
            float dt = lastTime < 0f ? 0f : Mathf.Clamp(time - lastTime, 0f, 0.1f);
            lastTime = time;
            bool live = rider.Running && !rider.Crashed;
            Colours(live ? rider.InvincibleTime : 0f, dt);
            float jump = live ? rider.SuperJumpTime : 0f;
            fizzEm.rateOverTime = jump > 0f && (jump > 2f || Mathf.Repeat(jump * 5f, 1f) < 0.6f) ? (rider.Airborne ? 70f : 22f) : 0f;
        }

        /// <summary>Everything back as it was: the rider's own colours, no fizz.</summary>
        public void Clear() { Unpaint(); phase = 0f; lastTime = -1f; fizz?.Clear(); }

        // ---- invincible: the rainbow

        void Colours(float left, float dt)
        {
            if (left <= 0f || body == null) { Unpaint(); phase = 0f; return; }
            if (body.Built != seenBuilt || slotCount == 0) Collect();   // a new body (another rider or set of clothes)
            // fast while it lasts; over the last seconds slowing down, and every other step the rider's own colours
            phase += dt * (left > Warn ? Fast : Mathf.Lerp(Slow, Fast, left / Warn));
            int step = (int)phase;
            if (left <= Warn && (step & 1) == 1) { Unpaint(); return; }
            int show = step % Rainbow.Length;
            if (painted && show == shown) return;
            Paint(show);
        }

        /// <summary>Finds every material drawn of the rider. Only when the rainbow starts or the body is rebuilt.</summary>
        void Collect()
        {
            Unpaint();
            slotCount = 0; seenBuilt = body.Built;
            var root = body.Drawn;
            if (root == null) return;
            root.GetComponentsInChildren(true, found);
            foreach (var r in found)
            {
                // (the spray and the bow wave are particles; the head's shadow in first person is never seen)
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                r.GetSharedMaterials(mats);
                for (int i = 0; i < mats.Count; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty(BaseColor)) continue;
                    var tex = m.HasProperty(BaseMap) ? m.GetTexture(BaseMap) : null;
                    bool picture = tex != null && tex != Texture2D.whiteTexture;
                    Color c = m.GetColor(BaseColor);
                    // the trousers (or a pyjama print's bottoms) and the board the far side of the rainbow from the rest
                    bool lower = body.Lower(m) || (picture && tex.name.Contains("Pants"));
                    if (slotCount == slots.Length) global::System.Array.Resize(ref slots, slots.Length * 2);
                    slots[slotCount++] = new Slot
                    {
                        r = r, index = i, picture = picture, second = m.HasProperty(Color2),
                        shift = lower ? Rainbow.Length / 2 : 0, shade = 0.75f + 0.25f * Mathf.Sqrt(Mathf.Clamp01(c.maxColorComponent)),
                        alpha = c.a,
                    };
                }
            }
            found.Clear(); mats.Clear();
        }

        void Paint(int show)
        {
            for (int i = 0; i < slotCount; i++)
            {
                ref var s = ref slots[i];
                if (s.r == null) continue;
                Color c = Rainbow[(show + s.shift) % Rainbow.Length];
                c = s.picture ? Color.Lerp(Color.white, c, TexTint) : c * s.shade;
                c.a = s.alpha;
                block.Clear();
                block.SetColor(BaseColor, c);
                if (s.second) block.SetColor(Color2, Color.Lerp(Rainbow[(show + s.shift + 3) % Rainbow.Length], Color.white, 0.3f));
                s.r.SetPropertyBlock(block, s.index);
            }
            painted = true; shown = show;
        }

        void Unpaint()
        {
            if (!painted) return;
            for (int i = 0; i < slotCount; i++) if (slots[i].r != null) slots[i].r.SetPropertyBlock(null, slots[i].index);
            painted = false; shown = -1;
        }
    }
}
