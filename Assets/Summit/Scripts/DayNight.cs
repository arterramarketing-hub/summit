using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Lights the world for the time of day (TimeOfDay.Clock): the sun or moon, the sky light and fog, the
    /// sky itself (Resources/Sky/SkyCycle.shader blending painted panoramas, stars and the moon), the grade on
    /// the photo backdrop, the snow's glints and the unlit snow particles, a headlamp on the rider at dusk and
    /// night, and the summit camp's evening (Camp.SetNight). The colour filters (LookGrade) are post-processing
    /// and sit on top of all of this untouched.
    /// Each phase is one look (a Key); the clock holds a phase's look most of the time and blends to the next
    /// one over the last part of it. Everything per frame is a handful of property sets, no allocation.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class DayNight : MonoBehaviour
    {
        /// <summary>One time of day's light.</summary>
        struct Key
        {
            public Vector3 sun;             // toward the sun (or moon) from the world
            public Color sunCol; public float sunI, shadow;
            public Color ambSky, ambEq, ambGround, fog; public float fogDensity;
            public float reflect;           // how much the snow's sheen reflects the baked (daytime) sky
            public int sky; public Color skyTint; public float skyExp, stars, moon;
            public Color bdLo, bdHi; public float bdKey;   // the photo backdrop: forest tint, snow tint, its sky let through
            public float camp, headlamp, glintRate, signGain;
            public Color glint, particles;
        }

        // where the moon hangs (azimuth from straight down the mountain, toward +X; elevation): ahead and a
        // little right of the big peak, above the photo's top. Its glow is painted there (Tools/MakeSkyCycle.swift).
        const float MoonAz = 22f, MoonE = 14f;

        static Vector3 Dir(float azDeg, float eDeg)
        {
            float a = azDeg * Mathf.Deg2Rad, e = eDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(e) * Mathf.Sin(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(a));
        }

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        // the looks. Sky textures: 0 morning, 1 day (the original SkyPanorama), 2 evening, 3 night.
        // A phase's fog is the colour its sky is painted down to at the horizon, so slope and sky meet.
        static readonly Key Morning = new Key
        {
            // dawn: a low warm sun ahead-left (low suns sit ahead, or the slope falling away from them would be
            // all in shade), long shadows, pink-lavender sky light, a soft valley haze
            sun = Dir(290f, 13f), sunCol = C(1f, 0.80f, 0.60f), sunI = 1.95f, shadow = 0.7f,
            ambSky = C(0.60f, 0.62f, 0.84f), ambEq = C(0.88f, 0.74f, 0.70f), ambGround = C(0.50f, 0.46f, 0.54f),
            fog = C(0.86f, 0.77f, 0.80f), fogDensity = 0.0025f, reflect = 0.75f,
            sky = 0, skyTint = Color.white, skyExp = 1f, stars = 0f, moon = 0f,
            bdLo = C(0.62f, 0.60f, 0.78f), bdHi = C(1.04f, 0.86f, 0.86f), bdKey = 1f,
            camp = 0.25f, headlamp = 0f, glintRate = 0.8f, signGain = 1f,
            glint = C(1f, 0.88f, 0.80f), particles = C(1f, 0.92f, 0.92f),
        };
        static readonly Key Day = new Key
        {
            // the run's clear day, as it was before there was a clock (SummitGame.SetUpWorld)
            sun = Dir(230f, 50f), sunCol = C(1f, 0.97f, 0.92f), sunI = 1.45f, shadow = 0.8f,
            ambSky = C(0.60f, 0.70f, 0.90f), ambEq = C(0.66f, 0.72f, 0.82f), ambGround = C(0.50f, 0.54f, 0.62f),
            fog = C(0.80f, 0.86f, 0.94f), fogDensity = 0.0021f, reflect = 1f,
            sky = 1, skyTint = Color.white, skyExp = 1f, stars = 0f, moon = 0f,
            bdLo = Color.white, bdHi = Color.white, bdKey = 0f,
            camp = 0f, headlamp = 0f, glintRate = 1f, signGain = 1f,
            glint = Color.white, particles = Color.white,
        };
        static readonly Key Golden = new Key
        {
            // early evening: a gold sun low ahead-right, warm faces and violet shade
            sun = Dir(70f, 15f), sunCol = C(1f, 0.74f, 0.44f), sunI = 1.75f, shadow = 0.72f,
            ambSky = C(0.54f, 0.54f, 0.76f), ambEq = C(0.82f, 0.64f, 0.54f), ambGround = C(0.44f, 0.40f, 0.46f),
            fog = C(0.90f, 0.72f, 0.64f), fogDensity = 0.0021f, reflect = 0.75f,
            sky = 2, skyTint = C(1.10f, 0.98f, 0.80f), skyExp = 1.05f, stars = 0f, moon = 0f,
            bdLo = C(0.62f, 0.54f, 0.62f), bdHi = C(1.10f, 0.86f, 0.70f), bdKey = 1f,
            camp = 0.3f, headlamp = 0f, glintRate = 0.9f, signGain = 1.05f,
            glint = C(1f, 0.84f, 0.60f), particles = C(1f, 0.88f, 0.78f),
        };
        static readonly Key Pink = new Key
        {
            // late evening: the sun on the horizon, pink alpenglow on the snow, the valley going blue, first stars
            sun = Dir(70f, 5f), sunCol = C(1f, 0.58f, 0.60f), sunI = 1.05f, shadow = 0.5f,
            ambSky = C(0.40f, 0.40f, 0.64f), ambEq = C(0.56f, 0.44f, 0.54f), ambGround = C(0.28f, 0.27f, 0.38f),
            fog = C(0.78f, 0.62f, 0.72f), fogDensity = 0.0022f, reflect = 0.5f,
            sky = 2, skyTint = C(0.94f, 0.80f, 1.0f), skyExp = 0.82f, stars = 0.12f, moon = 0.25f,
            bdLo = C(0.40f, 0.38f, 0.56f), bdHi = C(0.98f, 0.68f, 0.78f), bdKey = 1f,
            camp = 0.65f, headlamp = 0.45f, glintRate = 0.55f, signGain = 1.4f,
            glint = C(1f, 0.74f, 0.86f), particles = C(0.82f, 0.70f, 0.80f),
        };
        static readonly Key Night = new Key
        {
            // night: cold moonlight from ahead (the moon is in the sky there), deep blue shade, a starry sky,
            // the camp's lamps and the rider's headlamp. Snow still reads: it is the brightest thing about.
            sun = Dir(22f, 38f), sunCol = C(0.56f, 0.68f, 1f), sunI = 0.42f, shadow = 0.6f,
            ambSky = C(0.19f, 0.26f, 0.48f), ambEq = C(0.15f, 0.20f, 0.37f), ambGround = C(0.07f, 0.09f, 0.17f),
            fog = C(0.10f, 0.14f, 0.25f), fogDensity = 0.0024f, reflect = 0.18f,
            sky = 3, skyTint = Color.white, skyExp = 1f, stars = 1f, moon = 1f,
            bdLo = C(0.13f, 0.16f, 0.26f), bdHi = C(0.32f, 0.38f, 0.60f), bdKey = 1f,
            camp = 1f, headlamp = 1f, glintRate = 0.35f, signGain = 2.2f,
            glint = C(0.62f, 0.72f, 1f), particles = C(0.34f, 0.40f, 0.56f),
        };

        /// <summary>The share of each phase it holds its look before blending into the next.</summary>
        const float Hold = 0.8f;

        // ---------------------------------------------------------------- set up

        Light sun;
        Material skyMat;
        readonly Texture[] skies = new Texture[4];
        Rider rider;
        Camp camp;
        SnowFX fx;
        Material spray, trail;
        Light headlamp;
        float shown = -1f;
        Color lastParticles = new Color(-1f, 0f, 0f), lastGlint = new Color(-1f, 0f, 0f);
        float lastSignGain = -1f;
        static readonly List<Material> reflective = new List<Material>();

        /// <summary>
        /// Called once the sun is set up (SummitGame.SetUpWorld): takes over the sky with the time-of-day one
        /// and puts the current time's light on straight away, so the first frame is already right.
        /// </summary>
        public static DayNight Build(Light sun)
        {
            var dn = new GameObject("DayNight").AddComponent<DayNight>();
            dn.sun = sun;
            var old = RenderSettings.skybox;
            dn.skies[1] = old != null && old.HasProperty("_MainTex") ? old.GetTexture("_MainTex") : null;
            dn.skies[0] = Resources.Load<Texture2D>("Sky/SkyMorning");
            dn.skies[2] = Resources.Load<Texture2D>("Sky/SkyEvening");
            dn.skies[3] = Resources.Load<Texture2D>("Sky/SkyNight");
            if (dn.skies[1] == null) dn.skies[1] = dn.skies[0];
            var sh = Resources.Load<Shader>("Sky/SkyCycle");
            if (sh != null && dn.skies[0] != null)
            {
                dn.skyMat = new Material(sh) { name = "SkyCycle" };
                dn.skyMat.SetVector("_MoonDir", Dir(MoonAz, MoonE));
                RenderSettings.skybox = dn.skyMat;
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
        public static void Reflective(Material m) { if (m != null && !reflective.Contains(m)) reflective.Add(m); }

        /// <summary>How dark it is, 0 midday to 1 night (the camp's lamps follow it).</summary>
        public float Darkness { get; private set; }

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

            if (sun != null)
            {
                Vector3 to = Vector3.Slerp(a.sun, b.sun, t);
                sun.transform.rotation = Quaternion.LookRotation(-to);
                sun.color = Color.Lerp(a.sunCol, b.sunCol, t);
                sun.intensity = Mathf.Lerp(a.sunI, b.sunI, t);
                sun.shadowStrength = Mathf.Lerp(a.shadow, b.shadow, t);
            }
            RenderSettings.ambientSkyColor = Color.Lerp(a.ambSky, b.ambSky, t);
            RenderSettings.ambientEquatorColor = Color.Lerp(a.ambEq, b.ambEq, t);
            RenderSettings.ambientGroundColor = Color.Lerp(a.ambGround, b.ambGround, t);
            RenderSettings.fogColor = Color.Lerp(a.fog, b.fog, t);
            RenderSettings.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);
            RenderSettings.reflectionIntensity = Mathf.Lerp(a.reflect, b.reflect, t);

            if (skyMat != null)
            {
                skyMat.SetTexture("_TexA", skies[a.sky]);
                skyMat.SetTexture("_TexB", skies[b.sky]);
                skyMat.SetFloat("_Blend", a.sky == b.sky ? 0f : t);
                skyMat.SetColor("_Tint", Color.Lerp(a.skyTint, b.skyTint, t));
                skyMat.SetFloat("_Exposure", Mathf.Lerp(a.skyExp, b.skyExp, t));
                skyMat.SetFloat("_Stars", Mathf.Lerp(a.stars, b.stars, t));
                skyMat.SetFloat("_Moon", Mathf.Lerp(a.moon, b.moon, t));
            }

            Shader.SetGlobalFloat("_BackdropOn", 1f);
            Shader.SetGlobalColor("_BackdropLo", Color.Lerp(a.bdLo, b.bdLo, t));
            Shader.SetGlobalColor("_BackdropHi", Color.Lerp(a.bdHi, b.bdHi, t));
            Shader.SetGlobalFloat("_BackdropKey", Mathf.Lerp(a.bdKey, b.bdKey, t));

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
                lastSignGain = sg;
                foreach (var m in reflective) if (m != null) m.SetColor("_BaseColor", new Color(sg, sg, sg, 1f));
            }
            Headlamp(Mathf.Lerp(a.headlamp, b.headlamp, t));
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
