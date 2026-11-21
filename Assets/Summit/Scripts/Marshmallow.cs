using UnityEngine;

namespace Summit
{
    /// <summary>
    /// A marshmallow on a roasting stick, and the s'more it ends up in (the camp's fire: Alyssa's and the hero's).
    /// Its colour is the whole story, no words: pale and soft, then golden, then toasted brown, then charred black, and
    /// it swells a little as it cooks. Held in the flames too long it catches: a little fire of its own on the end of the
    /// stick until it's blown out. Built from primitives, one material each, so it costs next to nothing.
    /// </summary>
    public class Marshmallow
    {
        /// <summary>How cooked it is: 0 raw, about 0.45 golden, 0.7 toasted, 0.88 and on burnt.</summary>
        public float Toast;
        public bool Alight { get; private set; }
        public readonly Transform Stick, Tip, Mallow;
        public Transform Smore { get; private set; }
        /// <summary>From the hand to the marshmallow, metres.</summary>
        public const float Length = 1.27f;
        readonly Material mat;
        readonly ParticleSystem flame;
        Transform crackerTop, crackerBottom, choc;
        readonly Transform[] parts = new Transform[3];   // (the s'more's layers, for Bite: no new array each frame)
        float alightFor;

        // the colours it goes through, and where (by Toast)
        static readonly Color Raw = new Color(0.96f, 0.94f, 0.89f), Golden = new Color(0.90f, 0.63f, 0.30f),
                              Toasted = new Color(0.56f, 0.31f, 0.12f), Burnt = new Color(0.07f, 0.055f, 0.05f);
        public const float GoldenAt = 0.45f, ToastedAt = 0.7f, BurntAt = 0.88f;

        /// <summary>What it came out as, in a word, for the test tools and the reward (never shown).</summary>
        public static string Grade(float t) => t >= BurntAt ? "burnt" : t >= ToastedAt - 0.06f ? "toasted" : t >= GoldenAt - 0.12f ? "golden" : "pale";

        public Marshmallow(Transform parent, Material stickMat)
        {
            Stick = new GameObject("RoastingStick").transform;
            Stick.SetParent(parent, false);
            // a peeled green stick, 1.3 m: the hand holds it near this end (its origin), the tip is +Z
            var s = Prim(PrimitiveType.Cylinder, Stick, new Vector3(0f, 0f, 0.65f), new Vector3(0.018f, 0.65f, 0.018f), stickMat);
            s.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Tip = new GameObject("Tip").transform; Tip.SetParent(Stick, false); Tip.localPosition = new Vector3(0f, 0f, Length);
            mat = new Material(Lit()) { name = "Marshmallow" };
            mat.SetFloat("_Smoothness", 0.25f);
            Mallow = Prim(PrimitiveType.Cylinder, Tip, Vector3.zero, new Vector3(0.055f, 0.03f, 0.055f), mat);
            Mallow.localRotation = Quaternion.Euler(90f, 0f, 0f);   // speared through its middle, along the stick
            // its own little fire, when it catches
            var go = new GameObject("MallowFlame");
            go.transform.SetParent(Tip, false);
            flame = go.AddComponent<ParticleSystem>();
            flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = flame.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(2.4f, 1.3f, 0.35f, 0.9f), new Color(2.6f, 0.7f, 0.12f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.6f;   // flames rise
            main.maxParticles = 30;
            var sh = flame.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.03f;
            var em = flame.emission; em.rateOverTime = 45f;
            var col = flame.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FlameMat();
            Refresh(0f);
        }

        /// <summary>
        /// Cook it for dt seconds at heat (0 nothing, 1 just above the embers, 3 in the flames). Too long in the
        /// flames and it catches; alight, it chars fast until blown out.
        /// </summary>
        public void Cook(float dt, float heat)
        {
            if (Smore != null) return;
            float rate = 0.02f * heat * heat;   // the flames brown it far faster than the glow above them (golden in ten seconds over the embers, two or three in the flames)
            if (Alight) rate = 0.32f;
            Toast = Mathf.Min(1.2f, Toast + rate * dt);
            // a toasted one in the flames is ready to catch: the longer, the likelier (never in the first seconds)
            if (!Alight && heat > 2f && Toast > 0.55f) { alightFor += dt * (heat - 2f); if (alightFor > 1.1f) Light(); }
            else alightFor = Mathf.Max(0f, alightFor - dt);
            Refresh(dt);
        }

        public void Light() { if (Alight || Smore != null) return; Alight = true; flame.Play(); }
        public void BlowOut() { if (!Alight) return; Alight = false; alightFor = 0f; flame.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

        /// <summary>The colour and swell of the toast it has now (and the flicker on it while alight).</summary>
        public void Refresh(float dt)
        {
            float t = Toast;
            Color c = t < GoldenAt ? Color.Lerp(Raw, Golden, Mathf.SmoothStep(0f, 1f, t / GoldenAt))
                    : t < ToastedAt ? Color.Lerp(Golden, Toasted, (t - GoldenAt) / (ToastedAt - GoldenAt))
                    : Color.Lerp(Toasted, Burnt, Mathf.Clamp01((t - ToastedAt) / (BurntAt + 0.08f - ToastedAt)));
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Smoothness", Mathf.Lerp(0.2f, 0.55f, Mathf.Clamp01(t / ToastedAt)) * (t > BurntAt ? 0.4f : 1f));   // a glaze as the sugar melts
            if (mat.HasProperty("_EmissionColor"))
            {
                float glow = Alight ? 0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 9f, 0.3f) : 0f;
                mat.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.12f) * glow);
                if (glow > 0f) mat.EnableKeyword("_EMISSION"); else mat.DisableKeyword("_EMISSION");
            }
            float swell = 1f + 0.22f * Mathf.Clamp01(t / ToastedAt) - 0.12f * Mathf.Clamp01((t - BurntAt) / 0.3f);
            if (Smore == null) Mallow.localScale = new Vector3(0.055f * swell, 0.03f * swell, 0.055f * swell);
        }

        /// <summary>A fresh one on the stick (the last one eaten).</summary>
        public void Fresh()
        {
            BlowOut();
            if (Smore != null) { Object.Destroy(Smore.gameObject); Smore = null; }
            Mallow.SetParent(Tip, false); Mallow.localPosition = Vector3.zero; Mallow.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Mallow.gameObject.SetActive(true);
            Toast = 0f; Refresh(0f);
        }

        /// <summary>
        /// The s'more, in the hand: a graham cracker with a square of chocolate on it, the marshmallow slid off the stick
        /// onto that and squashed under the other cracker. Parented to `hand`; it shows from `built` 0..1 (the crackers
        /// come together), and Bite takes it away a bite at a time.
        /// </summary>
        public Transform MakeSmore(Transform hand, Material cracker, Material chocolate)
        {
            BlowOut();
            Smore = new GameObject("Smore").transform;
            Smore.SetParent(hand, false);
            crackerBottom = Prim(PrimitiveType.Cube, Smore, new Vector3(0f, -0.022f, 0f), new Vector3(0.075f, 0.009f, 0.075f), cracker);
            choc = Prim(PrimitiveType.Cube, Smore, new Vector3(0f, -0.014f, 0f), new Vector3(0.05f, 0.007f, 0.05f), chocolate);
            Mallow.SetParent(Smore, false);
            Mallow.localPosition = new Vector3(0f, 0.002f, 0f); Mallow.localRotation = Quaternion.identity;
            crackerTop = Prim(PrimitiveType.Cube, Smore, new Vector3(0f, 0.04f, 0f), new Vector3(0.075f, 0.009f, 0.075f), cracker);
            Squash(0f);
            return Smore;
        }

        /// <summary>The top cracker coming down on it: 0 apart, 1 squashed flat, the marshmallow oozing out a little.</summary>
        public void Squash(float k)
        {
            if (Smore == null) return;
            float h = Mathf.Lerp(0.03f, 0.014f, k), w = Mathf.Lerp(0.06f, 0.08f, k);
            Mallow.localScale = new Vector3(w, h, w);
            crackerTop.localPosition = new Vector3(0f, Mathf.Lerp(0.04f, 0.022f, k), 0f);
        }

        /// <summary>Eaten down to `left` (1 whole .. 0 gone): bites come off its far edge.</summary>
        public void Bite(float left)
        {
            if (Smore == null) return;
            left = Mathf.Clamp01(left);
            Smore.gameObject.SetActive(left > 0.02f);
            // (a bite off the side away from the hand: the stack narrows toward it)
            parts[0] = crackerBottom; parts[1] = crackerTop; parts[2] = choc;
            foreach (var t in parts)
            {
                var s = t.localScale; float full = t == choc ? 0.05f : 0.075f;
                t.localScale = new Vector3(s.x, s.y, full * left);
                t.localPosition = new Vector3(t.localPosition.x, t.localPosition.y, -full * (1f - left) * 0.5f);
            }
            var m = Mallow.localScale; Mallow.localScale = new Vector3(m.x, m.y, 0.075f * left);
            Mallow.localPosition = new Vector3(0f, Mallow.localPosition.y, -0.0375f * (1f - left));
        }

        public void SetStickVisible(bool on) { Stick.gameObject.SetActive(on); }

        /// <summary>Gone: the stick, the s'more, and its own material (its colour is its toast, so it isn't shared).</summary>
        public void Destroy()
        {
            if (Smore != null) Kill(Smore.gameObject);
            if (Stick != null) Kill(Stick.gameObject);
            Kill(mat);
        }

        // ---------------------------------------------------------------- bits

        static Shader lit;
        public static Shader Lit() { if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit"); return lit; }

        /// <summary>
        /// A plain lit colour, shared: one material per colour and sheen for the whole game, however often the camp is
        /// rebuilt (so nothing piles up). Never change one of these: make your own for anything that changes.
        /// </summary>
        public static Material Plain(Color c, float smooth)
        {
            var key = (c, smooth);
            if (plain.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Lit()) { name = "Plain" }; m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            plain[key] = m;
            return m;
        }
        static readonly System.Collections.Generic.Dictionary<(Color, float), Material> plain = new System.Collections.Generic.Dictionary<(Color, float), Material>();

        /// <summary>Destroy a thing made at runtime, in play or in the editor's test tools.</summary>
        public static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        /// <summary>The game's soft particle material (Camp hands it over), for the little flames and the steam.</summary>
        public static Material Soft;
        static Material flameMat;
        public static Material FlameMat()
        {
            if (flameMat != null) return flameMat;
            if (Soft != null) { flameMat = new Material(Soft) { name = "MallowFlame" }; flameMat.SetColor("_BaseColor", Color.white); return flameMat; }
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            flameMat = new Material(sh != null ? sh : Lit()) { name = "MallowFlame" };
            flameMat.SetColor("_BaseColor", Color.white);
            // additive, so the little flame glows over the dark
            flameMat.SetFloat("_Surface", 1f); flameMat.SetFloat("_Blend", 2f);
            flameMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            flameMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            flameMat.SetFloat("_ZWrite", 0f);
            flameMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            flameMat.renderQueue = 3000;
            var tex = Resources.Load<Texture2D>("Particles/Soft");
            if (tex != null) flameMat.SetTexture("_BaseMap", tex);
            return flameMat;
        }

        public static Transform Prim(PrimitiveType t, Transform parent, Vector3 at, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // too small to matter: no shadow pass for them
            return go.transform;
        }
    }
}
