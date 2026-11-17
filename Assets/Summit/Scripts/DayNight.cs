using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Lights the world for the time of day (TimeOfDay.Clock) from a photographed sky per phase
    /// (Resources/Sky, made by Tools/MakeSkies.swift from Poly Haven HDRIs): the sky itself
    /// (Resources/Sky/SkyCycle.shader blends two of them), the sun or moon where the photo has it, the ambient
    /// light (measured from the photo) and reflections (a probe holding it) taken from that same sky, the fog in its horizon's colour, the grade on the photo
    /// backdrop, the snow's glints and unlit snow particles, a headlamp at dusk and night, and the summit camp's
    /// evening (Camp.SetNight). The colour filters (LookGrade) are post-processing and sit on top, untouched.
    /// Each phase is one look (a Key); the clock holds a phase's look most of the time and blends to the next
    /// one over the last part of it. Per frame it is a handful of property sets and no allocation.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class DayNight : MonoBehaviour
    {
        /// <summary>One time of day's light, on top of its photographed sky.</summary>
        struct Key
        {
            public int sky;                 // 0 morning, 1 day, 2 evening, 3 night (SkyInfo.json's order)
            public float exposure;          // how bright that sky is shown (they are stored at one level)
            public Color tint;              // a grade on the sky, and so on its ambient light and fog
            public float sunI, shadow;      // the sun's (or moon's) strength; its colour is the photo's
            public Color sunTint;
            public float ambient, fogGain, fogDensity, reflect;
            public Color bdLo, bdHi;        // the photo backdrop: forest tint, snow tint
            public float camp, headlamp, glintRate, signGain;
            public Color glint, particles;
        }

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        static readonly Key Morning = new Key
        {
            // dawn: a low gold sun ahead-left through scattered cloud, cool blue sky, pink on the peak
            sky = 0, exposure = 1.1f, tint = Color.white,
            sunI = 1.9f, shadow = 0.7f, sunTint = C(1f, 1.15f, 1.35f),   // the photo's sun is deep orange: a softer gold
            ambient = 1.25f, fogGain = 1.05f, fogDensity = 0.0024f, reflect = 0.8f,
            bdLo = C(0.66f, 0.64f, 0.78f), bdHi = C(1.05f, 0.88f, 0.84f),
            camp = 0.25f, headlamp = 0f, glintRate = 0.8f, signGain = 1f,
            glint = C(1f, 0.88f, 0.78f), particles = C(1f, 0.93f, 0.9f),
        };
        static readonly Key Day = new Key
        {
            // midday: a high white sun behind-left, blue sky and cumulus
            sky = 1, exposure = 1f, tint = Color.white,
            sunI = 1.15f, shadow = 0.8f, sunTint = Color.white,
            ambient = 0.45f, fogGain = 1.15f, fogDensity = 0.0021f, reflect = 1f,
            bdLo = Color.white, bdHi = Color.white,
            camp = 0f, headlamp = 0f, glintRate = 1f, signGain = 1f,
            glint = Color.white, particles = Color.white,
        };
        static readonly Key Golden = new Key
        {
            // early evening: the sun on the horizon ahead-right under a lid of cloud, gold light, violet shade
            sky = 2, exposure = 1.3f, tint = C(1.12f, 1f, 0.86f),
            sunI = 1.8f, shadow = 0.7f, sunTint = C(1f, 1.05f, 1.15f),
            ambient = 0.85f, fogGain = 1.2f, fogDensity = 0.0021f, reflect = 0.8f,
            bdLo = C(0.62f, 0.55f, 0.62f), bdHi = C(1.12f, 0.88f, 0.72f),
            camp = 0.3f, headlamp = 0f, glintRate = 0.9f, signGain = 1.05f,
            glint = C(1f, 0.84f, 0.6f), particles = C(1f, 0.88f, 0.78f),
        };
        static readonly Key Pink = new Key
        {
            // late evening: the same sky after the sun has gone, pinker and dimmer, the valley going blue
            sky = 2, exposure = 0.75f, tint = C(1f, 0.86f, 1.02f),
            sunI = 0.9f, shadow = 0.5f, sunTint = C(1f, 0.82f, 1.3f),
            ambient = 1.3f, fogGain = 1.2f, fogDensity = 0.0022f, reflect = 0.6f,
            bdLo = C(0.42f, 0.40f, 0.58f), bdHi = C(0.98f, 0.70f, 0.80f),
            camp = 0.65f, headlamp = 0.45f, glintRate = 0.55f, signGain = 1.4f,
            glint = C(1f, 0.74f, 0.86f), particles = C(0.82f, 0.7f, 0.8f),
        };
        static readonly Key Night = new Key
        {
            // night: the moon ahead-right, its photo (a long exposure) brought down to a dark blue with the stars
            // still in it, cold moonlight, the camp's lamps and the rider's headlamp. The snow still reads.
            sky = 3, exposure = 0.2f, tint = C(0.62f, 0.8f, 1.3f),
            sunI = 0.45f, shadow = 0.6f, sunTint = C(0.75f, 0.85f, 1f),
            ambient = 1.6f, fogGain = 1.2f, fogDensity = 0.0024f, reflect = 0.5f,
            bdLo = C(0.13f, 0.16f, 0.26f), bdHi = C(0.32f, 0.38f, 0.60f),
            camp = 1f, headlamp = 1f, glintRate = 0.35f, signGain = 2.2f,
            glint = C(0.62f, 0.72f, 1f), particles = C(0.34f, 0.40f, 0.56f),
        };

        /// <summary>The share of each phase it holds its look before blending into the next.</summary>
        const float Hold = 0.8f;
        /// <summary>The light never comes in lower than this (degrees), or the snow facing away would be all shade.</summary>
        const float MinLightEl = 7f;

        // ---------------------------------------------------------------- the photographed skies

        [System.Serializable] class PhaseInfo { public string name; public float sunAz, sunEl; public float[] sunColour, horizon, grid; }
        [System.Serializable] class SkyInfo { public PhaseInfo[] phases; public int gridW, gridH; }

        struct Sky
        {
            public Cubemap cube;
            public Vector3 sun;             // toward the sun or moon
            public Color sunCol, horizon;   // linear
            public Color ambSky, ambEq, ambGround;  // the sky's light on surfaces facing up, sideways and down (linear, exposure 1)
        }
        readonly Sky[] skies = new Sky[4];

        static Vector3 Dir(float azDeg, float elDeg)
        {
            float a = azDeg * Mathf.Deg2Rad, e = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(e) * Mathf.Sin(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(a));
        }

        /// <summary>Read SkyInfo.json: each sky's sun, horizon, and its radiance grid as an ambient probe.</summary>
        bool LoadSkies()
        {
            var txt = Resources.Load<TextAsset>("Sky/SkyInfo");
            if (txt == null) return false;
            var info = JsonUtility.FromJson<SkyInfo>(txt.text);
            string[] names = { "Morning", "Day", "Evening", "Night" };
            for (int i = 0; i < 4; i++)
            {
                var p = System.Array.Find(info.phases, q => q.name == names[i]);
                var cube = Resources.Load<Cubemap>("Sky/Sky" + names[i]);
                if (p == null || cube == null) return false;
                var s = new Sky { cube = cube };
                s.sun = Dir(p.sunAz, Mathf.Max(p.sunEl, MinLightEl));
                s.sunCol = C(p.sunColour[0], p.sunColour[1], p.sunColour[2]);
                s.horizon = C(p.horizon[0], p.horizon[1], p.horizon[2]);
                // each cell of the grid is a patch of sky shining on the world: added as a directional light of its
                // radiance times its solid angle over pi, which is what a surface would take from it in Unity's terms
                var sh = new SphericalHarmonicsL2();
                int gw = info.gridW, gh = info.gridH;
                for (int y = 0; y < gh; y++)
                {
                    float e0 = 90f - y * 180f / gh, e1 = 90f - (y + 1) * 180f / gh;
                    float omega = 2f * Mathf.PI / gw * (Mathf.Sin(e0 * Mathf.Deg2Rad) - Mathf.Sin(e1 * Mathf.Deg2Rad));
                    for (int x = 0; x < gw; x++)
                    {
                        int g = (y * gw + x) * 3;
                        var c = C(p.grid[g], p.grid[g + 1], p.grid[g + 2]);
                        // (AddDirectionalLight takes the direction toward the light)
                        sh.AddDirectionalLight(Dir((x + 0.5f) * 360f / gw, (e0 + e1) * 0.5f), c, omega / Mathf.PI);
                    }
                }
                // as the three colours of Unity's trilight ambient (the mode the game has always lit with)
                var dirs = new[] { Vector3.up, Vector3.forward, Vector3.right, Vector3.back, Vector3.left, Vector3.down };
                var res = new Color[dirs.Length];
                sh.Evaluate(dirs, res);
                s.ambSky = res[0]; s.ambEq = (res[1] + res[2] + res[3] + res[4]) * 0.25f; s.ambGround = res[5];
                skies[i] = s;
            }
            return true;
        }

        // ---------------------------------------------------------------- set up

        Light sun;
        Material skyMat;
        bool photoSky;
        Rider rider;
        Camp camp;
        SnowFX fx;
        Material spray, trail;
        Light headlamp;
        float shown = -1f;
        Color lastParticles = new Color(-1f, 0f, 0f), lastGlint = new Color(-1f, 0f, 0f);
        float lastSignGain = -1f;
        Cubemap lastReflection;
        ReflectionProbe probe;
        static readonly List<Material> reflective = new List<Material>();

        /// <summary>
        /// Called once the sun is set up (SummitGame.SetUpWorld): takes over the sky with the time-of-day one
        /// and puts the current time's light on straight away, so the first frame is already right.
        /// </summary>
        public static DayNight Build(Light sun)
        {
            var dn = new GameObject("DayNight").AddComponent<DayNight>();
            dn.sun = sun;
            var sh = Resources.Load<Shader>("Sky/SkyCycle");
            if (sh != null && dn.LoadSkies())
            {
                dn.photoSky = true;
                dn.skyMat = new Material(sh) { name = "SkyCycle" };
                RenderSettings.skybox = dn.skyMat;
                // the snow's reflections: of the photographed sky, through one probe round everything
                dn.probe = new GameObject("SkyReflection").AddComponent<ReflectionProbe>();
                dn.probe.transform.SetParent(dn.transform, false);
                dn.probe.mode = ReflectionProbeMode.Custom;
                dn.probe.size = Vector3.one * 100000f;
                dn.probe.boxProjection = false;
                dn.probe.importance = 0;
            }
            dn.ApplyNow();
            return dn;
        }

        /// <summary>The rest of the world it lights, once the game has made it.</summary>
        public void Hook(Rider r, SnowFX snowFx, Camp c, Material sprayMat, Material trailMat)
        {
            rider = r; fx = snowFx; camp = c; spray = sprayMat; trail = trailMat;
            headlamp = new GameObject("Headlamp").AddComponent<Light>();
            headlamp.transform.SetParent(transform, false);
            headlamp.type = LightType.Spot;
            headlamp.color = new Color(1f, 0.95f, 0.86f);
            headlamp.range = 48f; headlamp.spotAngle = 70f; headlamp.innerSpotAngle = 28f;
            headlamp.shadows = LightShadows.None;   // a real-time shadow map for it would cost too much on the phone
            headlamp.enabled = false;
            ApplyNow();
        }

        /// <summary>
        /// Signs and tape: give them the retro-reflective look at night (they shine back in the lamp and moonlight
        /// more than plain snow would). Done by raising their colour, not with emission, whose shader variant
        /// isn't in the build.
        /// </summary>
        public static void Reflective(Material m)
        {
            if (m == null || reflective.Contains(m)) return;
            reflective.RemoveAll(x => x == null);   // signs destroyed with an old camp or mountain
            reflective.Add(m);
            m.SetColor("_BaseColor", new Color(signGain, signGain, signGain, 1f));   // at tonight's brightness straight away
        }
        static float signGain = 1f;

        /// <summary>How dark it is, 0 midday to 1 night (the camp's lamps follow it).</summary>
        public float Darkness { get; private set; }

        /// <summary>The sky now being shown (the test tools check the light against it).</summary>
        public Vector3 SunDirection { get; private set; }

        // ---------------------------------------------------------------- per frame

        void LateUpdate()
        {
            TimeOfDay.Advance(Time.deltaTime);
            float target = TimeOfDay.Clock;
            // a change in the pause menu runs the day forward to it as a quick time-lapse (unscaled: the game is
            // paused then); the slow cycle itself is followed exactly
            float ahead = Mathf.Repeat(target - shown, 4f);
            if (shown < 0f || ahead < 0.02f || ahead > 3.98f) shown = target;
            else shown = Mathf.Repeat(shown + Mathf.Min(ahead, Time.unscaledDeltaTime * 1.8f), 4f);
            Apply(shown);
        }

        /// <summary>Put the light for the clock on now (the test tools call it; batchmode runs no LateUpdate).</summary>
        public void ApplyNow() { shown = TimeOfDay.Clock; Apply(shown); }

        void OnApplicationPause(bool paused) { if (paused) TimeOfDay.Save(); }
        void OnApplicationQuit() { TimeOfDay.Save(); }

        // the particle materials are project assets: never leave them dimmed in the editor
        void OnDestroy()
        {
            foreach (var m in new[] { spray, trail })
                if (m != null) { m.SetColor("_BaseColor", Color.white); m.SetColor("_Color", Color.white); }   // (the editor mirrors one into the other)
            foreach (var m in reflective) if (m != null) m.SetColor("_BaseColor", Color.white);
            reflective.Clear();
        }

        // ---------------------------------------------------------------- the look for a clock

        static Key StartOf(int p) => p == 0 ? Morning : p == 1 ? Day : p == 2 ? Golden : Night;
        static Key EndOf(int p) => p == 2 ? Pink : StartOf(p);

        static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);

        void Apply(float clock)
        {
            int p = Mathf.Clamp((int)clock, 0, 3);
            float f = clock - p;
            Key a, b; float t;
            if (f > Hold) { a = EndOf(p); b = StartOf((p + 1) % 4); t = Mathf.SmoothStep(0f, 1f, (f - Hold) / (1f - Hold)); }
            else if (p == 2) { a = Golden; b = Pink; t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, Hold, f)); }   // golden, then pink
            else { a = b = StartOf(p); t = 0f; }

            Darkness = Mathf.Lerp(a.camp, b.camp, t);
            // the camp first: its SetNight may still set the sun, sky light and fog for its own blue hour, which
            // everything after it puts back to the time of day (only its lamps are kept)
            if (camp != null) camp.SetNight(Darkness);

            // what each end shows: its sky, times its exposure and tint (linear)
            Color gradeA = a.tint * a.exposure, gradeB = b.tint * b.exposure;
            if (photoSky)
            {
                Sky sa = skies[a.sky], sb = skies[b.sky];
                SunDirection = SunBetween(sa.sun, sb.sun, t);

                skyMat.SetTexture("_CubeA", sa.cube);
                skyMat.SetTexture("_CubeB", sb.cube);
                skyMat.SetFloat("_Blend", a.sky == b.sky ? 0f : t);
                // the grade lives in the tint (exposure stays 1), so a blend between two grades is one colour
                skyMat.SetColor("_Tint", Color.Lerp(gradeA, gradeB, t));
                skyMat.SetFloat("_Exposure", 1f);

                // ambient: each sky's own light, graded the same way, then blended (RenderSettings wants gamma colours)
                Color ka = gradeA * a.ambient, kb = gradeB * b.ambient;
                RenderSettings.ambientSkyColor = Color.Lerp(Mul(sa.ambSky, ka), Mul(sb.ambSky, kb), t).gamma;
                RenderSettings.ambientEquatorColor = Color.Lerp(Mul(sa.ambEq, ka), Mul(sb.ambEq, kb), t).gamma;
                RenderSettings.ambientGroundColor = Color.Lerp(Mul(sa.ambGround, ka), Mul(sb.ambGround, kb), t).gamma;
                // fog: the horizon's colour (RenderSettings wants it gamma-encoded)
                Color fa = Mul(sa.horizon, gradeA) * a.fogGain, fb = Mul(sb.horizon, gradeB) * b.fogGain;
                RenderSettings.fogColor = Color.Lerp(fa, fb, t).gamma;
                // reflections: the nearer sky's cubemap (it is stored squeezed, so a little dimmer than it shows)
                var refl = t < 0.5f ? sa.cube : sb.cube;
                if (refl != lastReflection) { lastReflection = refl; probe.customBakedTexture = refl; }
                probe.intensity = Mathf.Lerp(a.reflect * a.exposure, b.reflect * b.exposure, t);

                if (sun != null)
                {
                    sun.transform.rotation = Quaternion.LookRotation(-SunDirection);
                    Color ca = Mul(sa.sunCol, a.sunTint), cb = Mul(sb.sunCol, b.sunTint);
                    ca /= Mathf.Max(ca.r, Mathf.Max(ca.g, ca.b)); cb /= Mathf.Max(cb.r, Mathf.Max(cb.g, cb.b));
                    sun.color = Color.Lerp(ca, cb, t).gamma;
                    sun.intensity = Mathf.Lerp(a.sunI, b.sunI, t);
                    sun.shadowStrength = Mathf.Lerp(a.shadow, b.shadow, t);
                }
            }
            RenderSettings.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);

            Shader.SetGlobalFloat("_BackdropOn", 1f);
            Shader.SetGlobalColor("_BackdropLo", Color.Lerp(a.bdLo, b.bdLo, t));
            Shader.SetGlobalColor("_BackdropHi", Color.Lerp(a.bdHi, b.bdHi, t));
            // the photo backdrop's own (daytime) sky always gives way to the photographed sky behind it
            Shader.SetGlobalFloat("_BackdropKey", photoSky ? 1f : 0f);

            // unlit snow particles (falling snow, mist, spray) would glow white in the dark: dim them with the light
            Color pc = Color.Lerp(a.particles, b.particles, t);
            if (!Same(pc, lastParticles))
            {
                lastParticles = pc;
                if (spray != null) spray.SetColor("_BaseColor", pc);
                if (trail != null) trail.SetColor("_BaseColor", pc);
            }
            Color gc = Color.Lerp(a.glint, b.glint, t);
            if (fx != null)
            {
                fx.glintRate = Mathf.Lerp(a.glintRate, b.glintRate, t);
                if (!Same(gc, lastGlint)) { lastGlint = gc; fx.TintGlints(gc); }
            }
            float sg = Mathf.Lerp(a.signGain, b.signGain, t);
            if (Mathf.Abs(sg - lastSignGain) > 0.002f)
            {
                lastSignGain = sg; signGain = sg;
                foreach (var m in reflective) if (m != null) m.SetColor("_BaseColor", new Color(sg, sg, sg, 1f));
            }
            Headlamp(Mathf.Lerp(a.headlamp, b.headlamp, t));
        }

        /// <summary>
        /// The light between two skies' suns: round the horizon the short way and down (or up) in step, not
        /// straight across the sky, which from the midday sun to a sunset behind you would pass right overhead.
        /// </summary>
        static Vector3 SunBetween(Vector3 a, Vector3 b, float t)
        {
            float azA = Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg, azB = Mathf.Atan2(b.x, b.z) * Mathf.Rad2Deg;
            float elA = Mathf.Asin(Mathf.Clamp(a.y, -1f, 1f)) * Mathf.Rad2Deg, elB = Mathf.Asin(Mathf.Clamp(b.y, -1f, 1f)) * Mathf.Rad2Deg;
            return Dir(Mathf.LerpAngle(azA, azB, t), Mathf.Lerp(elA, elB, t));
        }

        static bool Same(Color x, Color y) => Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b) < 0.003f;

        /// <summary>A lamp on the rider's helmet, aimed a little down the way the camera looks: a pool of light on
        /// the snow ahead. Off in the camp, which has its own lamps.</summary>
        void Headlamp(float k)
        {
            if (headlamp == null || rider == null) return;
            bool on = k > 0.01f && (camp == null || !camp.Active);
            headlamp.enabled = on;
            if (!on) return;
            headlamp.intensity = 7f * k;
            var cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            headlamp.transform.SetPositionAndRotation(rider.transform.position + Vector3.up * 1.7f + fwd * 0.2f,
                                                      Quaternion.LookRotation(fwd - Vector3.up * 0.32f));
        }
    }
}
