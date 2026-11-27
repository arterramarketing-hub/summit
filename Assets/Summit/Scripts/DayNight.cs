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
            public Color bdLo, bdHi;        // the photo backdrop: shade tint (forest, snow out of the sun), light tint
            public float bdSat;             // its colour kept (moonlight shows little)
            public Vector2 bdGlow;          // the band of height (photo v) up which its snow is in the sun: at dusk
                                            // only the high snowfields catch it (-1..0: all of it)
            public float camp, headlamp, glintRate, glow;   // glow: RiderGlow's strength
            public Color glint, particles;
        }

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        static readonly Key Morning = new Key
        {
            // dawn: a low gold sun ahead-left through scattered cloud, cool blue sky, pink on the peak
            sky = 0, exposure = 1.1f, tint = Color.white,
            sunI = 1.9f, shadow = 0.7f, sunTint = C(1f, 1.15f, 1.35f),   // the photo's sun is deep orange: a softer gold
            ambient = 1.25f, fogGain = 1.05f, fogDensity = 0.0024f, reflect = 0.8f,
            bdLo = C(0.70f, 0.68f, 0.80f), bdHi = C(1.10f, 0.94f, 0.84f), bdSat = 0.95f, bdGlow = new Vector2(-1f, 0f),   // soft warm light
            camp = 0.25f, headlamp = 0f, glintRate = 0.8f, glow = 0.25f,
            glint = C(1f, 0.88f, 0.78f), particles = C(1f, 0.93f, 0.9f),
        };
        static readonly Key Day = new Key
        {
            // midday: a high white sun behind-left, blue sky and cumulus
            sky = 1, exposure = 1f, tint = Color.white,
            sunI = 1.15f, shadow = 0.8f, sunTint = Color.white,
            ambient = 0.45f, fogGain = 1.15f, fogDensity = 0.0021f, reflect = 1f,
            bdLo = Color.white, bdHi = Color.white, bdSat = 1f, bdGlow = new Vector2(-1f, 0f),
            camp = 0f, headlamp = 0f, glintRate = 1f, glow = 0.08f,
            glint = Color.white, particles = Color.white,
        };
        static readonly Key Golden = new Key
        {
            // early evening: the sun on the horizon ahead-right under a lid of cloud, gold light, violet shade
            sky = 2, exposure = 1.3f, tint = C(1.12f, 1f, 0.86f),
            sunI = 1.8f, shadow = 0.7f, sunTint = C(1f, 1.05f, 1.15f),
            ambient = 0.85f, fogGain = 1.2f, fogDensity = 0.0021f, reflect = 0.8f,
            // alpenglow: the upper snowfields gold-orange, the lower slopes and forest already in blue shade
            bdLo = C(0.34f, 0.31f, 0.42f), bdHi = C(1.30f, 0.86f, 0.55f), bdSat = 0.9f, bdGlow = new Vector2(0.60f, 0.74f),
            camp = 0.3f, headlamp = 0f, glintRate = 0.9f, glow = 0.45f,
            glint = C(1f, 0.84f, 0.6f), particles = C(1f, 0.88f, 0.78f),
        };
        static readonly Key Pink = new Key
        {
            // late evening: the same sky after the sun has gone, pinker and dimmer, the valley going blue
            sky = 2, exposure = 0.75f, tint = C(1f, 0.86f, 1.02f),
            sunI = 0.9f, shadow = 0.5f, sunTint = C(1f, 0.82f, 1.3f),
            ambient = 1.3f, fogGain = 1.2f, fogDensity = 0.0022f, reflect = 0.6f,
            // the last pink light on the summit snow only
            bdLo = C(0.22f, 0.22f, 0.36f), bdHi = C(1.20f, 0.55f, 0.72f), bdSat = 0.9f, bdGlow = new Vector2(0.78f, 0.90f),
            camp = 0.65f, headlamp = 0.45f, glintRate = 0.55f, glow = 0.75f,
            glint = C(1f, 0.74f, 0.86f), particles = C(0.82f, 0.7f, 0.8f),
        };
        static readonly Key Night = new Key
        {
            // night: the moon ahead-right, its photo (a long exposure) brought down to a dark blue with the stars
            // still in it, cold moonlight, the camp's lamps and the rider's headlamp. The snow still reads.
            sky = 3, exposure = 0.2f, tint = C(0.62f, 0.8f, 1.3f),
            sunI = 0.45f, shadow = 0.6f, sunTint = C(0.75f, 0.85f, 1f),
            ambient = 1.6f, fogGain = 1.2f, fogDensity = 0.0024f, reflect = 0.5f,
            // moonlit: about five times darker than day, nearly colourless and blue, the snow just catching the
            // moon and the forest dark, its shapes faint
            bdLo = C(0.17f, 0.21f, 0.33f), bdHi = C(0.11f, 0.135f, 0.22f),   // (the forest lifted just enough for its shapes to read) bdSat = 0.3f, bdGlow = new Vector2(-1f, 0f),
            camp = 1f, headlamp = 1f, glintRate = 0.35f, glow = 1f,
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
        Transform beam; Material beamMat;   // the lamp's light seen in the cold air: a soft cone
        RiderBody body;
        Transform torch; Material torchLens; Vector3 torchLocalPos; Quaternion torchLocalRot = Quaternion.identity;   // the little flashlight strapped to the jacket: the light comes from it
        /// <summary>The player's FLASHLIGHT setting (pause menu): on, it lights up at dusk and night; off, never.</summary>
        public static bool FlashlightOn
        {
            get { if (torchSetting < 0) torchSetting = PlayerPrefs.GetInt("summit_flashlight", 1); return torchSetting == 1; }
            set { torchSetting = value ? 1 : 0; }
        }
        static int torchSetting = -1;   // (read on first use: Unity refuses PlayerPrefs while a component is being built)
        float shown = -1f;
        Color lastParticles = new Color(-1f, 0f, 0f), lastGlint = new Color(-1f, 0f, 0f);
        RiderGlow glowFx;
        Cubemap lastReflection;
        ReflectionProbe probe;

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
            headlamp.range = 64f; headlamp.spotAngle = 68f; headlamp.innerSpotAngle = 12f;   // strong, but a wide soft falloff
            headlamp.shadows = LightShadows.None;   // a real-time shadow map for it would cost too much on the phone
            headlamp.enabled = false;
            BuildBeam();
            body = r != null ? r.GetComponent<RiderBody>() : null;
            BuildTorch();
            // (the old glow round the rider is gone: at night the lamp's own light and beam pick the rider out)
            ApplyNow();
        }


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
        public void ApplyNow() { shown = TimeOfDay.Clock; captureNow = true; Apply(shown); }

        /// <summary>Whether the reflections are the captured sky (true) or the nearer photo (false): the tests log it.</summary>
        public bool ReflectionCaptured => reflCam != null && !reflFailed;

        void OnApplicationPause(bool paused) { if (paused) TimeOfDay.Save(); }
        void OnApplicationQuit() { TimeOfDay.Save(); }

        // the particle materials are project assets: never leave them dimmed in the editor
        void OnDestroy()
        {
            foreach (var m in new[] { spray, trail })
                if (m != null) { m.SetColor("_BaseColor", Color.white); m.SetColor("_Color", Color.white); }   // (the editor mirrors one into the other)
        }

        // ---------------------------------------------------------------- the look for a clock

        static Key StartOf(int p) => p == 0 ? Morning : p == 1 ? Day : p == 2 ? Golden : Night;
        static Key EndOf(int p) => p == 2 ? Pink : StartOf(p);

        static readonly int FirLiftId = Shader.PropertyToID("_FirLift");
        static readonly int FirSnowCoolId = Shader.PropertyToID("_FirSnowCool");
        static readonly Color FirMoonLift = new Color(0.15f, 0.19f, 0.29f, 0f);

        static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);

        /// <summary>
        /// Keep the reflection cubemap showing the sky as it is now (64 a face: the snow's sheen is rough, it needs no
        /// more). It is drawn again only when the sky's look has changed (a blend between phases moving on, the evening
        /// going pink, the start page turning the sky, the weather's cloud: SkyVersion), never while a phase holds,
        /// at most every 3 seconds, and then one face a frame into a second cube that takes over once all six are
        /// drawn, so no frame carries more than one face of it. A time jump (ApplyNow) draws all six at once.
        /// False where it can't.
        /// </summary>
        bool CaptureReflection(Vector4 look)
        {
            if (reflFailed) return false;
            if (reflCam == null)
            {
                reflRT = Cube("SkyReflection"); reflBack = Cube("SkyReflectionNext");
                var go = new GameObject("SkyReflectionCamera");
                go.transform.SetParent(transform, false);
                reflCam = go.AddComponent<Camera>();
                reflCam.enabled = false; reflCam.cullingMask = 0; reflCam.clearFlags = CameraClearFlags.Skybox; reflCam.allowHDR = true;
                reflCam.nearClipPlane = 0.1f; reflCam.farClipPlane = 10f;
                var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                data.renderPostProcessing = false; data.renderShadows = false;
                captureNow = true;
            }
            if (captureNow)
            {
                captureNow = false; face = -1;
                long t0 = PerfMarks.Now;
                if (!reflCam.RenderToCubemap(reflRT)) { reflFailed = true; return false; }
                reflRT.GenerateMips();
                capturedLook = look; capturedVersion = SkyVersion;
                if (probe.customBakedTexture != reflRT) probe.customBakedTexture = reflRT;
                PerfMarks.Mark("reflections all six", t0);
                return true;
            }
            if (face < 0)
            {
                bool changed = capturedVersion != SkyVersion || Mathf.Abs(look.x - capturedLook.x) > 0.01f ||
                               Mathf.Abs(look.y - capturedLook.y) > 0.03f || Mathf.Abs(look.z - capturedLook.z) > 0.04f ||
                               Mathf.Abs(Mathf.DeltaAngle(look.w, capturedLook.w)) > 4f;
                if (!changed || Time.realtimeSinceStartup < nextCapture) return true;
                face = 0; pendingLook = look; pendingVersion = SkyVersion; nextCapture = Time.realtimeSinceStartup + CaptureEvery;
            }
            long f0 = PerfMarks.Now;
            if (!reflCam.RenderToCubemap(reflBack, 1 << face)) { reflFailed = true; return false; }
            FacesDrawn++;
            PerfMarks.MarkIfOver("reflections face", f0, 0.5, FaceName[face]);
            if (++face == 6)
            {
                // all six drawn: it takes over
                reflBack.GenerateMips();
                (reflRT, reflBack) = (reflBack, reflRT);
                probe.customBakedTexture = reflRT;
                capturedLook = pendingLook; capturedVersion = pendingVersion; face = -1;
            }
            return true;
        }

        static readonly string[] FaceName = { "1/6", "2/6", "3/6", "4/6", "5/6", "6/6" };
        /// <summary>Seconds at least between starting one redraw of the reflections and the next.</summary>
        const float CaptureEvery = 3f;
        /// <summary>Faces drawn into the reflections so far (the test tools count them).</summary>
        public static int FacesDrawn;

        static RenderTexture Cube(string name)
        {
            var rt = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGBHalf)
                { dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = false, name = name };
            rt.Create();
            return rt;
        }
        RenderTexture reflRT, reflBack;
        Camera reflCam;
        bool reflFailed, captureNow;
        int face = -1;
        float nextCapture;
        Vector4 capturedLook = new Vector4(-99f, 0f, 0f, 0f), pendingLook;
        int capturedVersion = -1, pendingVersion;
        /// <summary>Bumped by whatever else changes the sky (Weather's cloud): the reflections are drawn again.</summary>
        public static int SkyVersion;
        /// <summary>Called at the end of each lighting pass (every frame, and on ApplyNow): Weather turns this light into its own.</summary>
        public static event System.Action<DayNight> Applied;
        /// <summary>How strongly the headlamp's beam shows in the air (Weather: more in falling snow and fog).</summary>
        public static float BeamGain = 1f;

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
            // the far forest by moonlight: a faint cold light on the realistic trees (Shaders/Fir.hlsl), more the
            // further off they are, so their shapes still read against the dark instead of going black
            float lift = Mathf.Clamp01((Darkness - 0.3f) / 0.7f);
            Shader.SetGlobalColor(FirLiftId, FirMoonLift * lift);
            // the snow on the boughs: white by day, the cool blue of the old look from the pink hour into the night
            // (Shaders/Fir.hlsl FirLight)
            Shader.SetGlobalFloat(FirSnowCoolId, Mathf.Clamp01((Darkness - 0.15f) / 0.5f));

            // what each end shows: its sky, times its exposure and tint (linear)
            Color gradeA = a.tint * a.exposure, gradeB = b.tint * b.exposure;
            if (photoSky)
            {
                Sky sa = skies[a.sky], sb = skies[b.sky];
                // (the start page's time-lapse turns the whole sky, its sun or moon with it: SkyTurn)
                SunDirection = Quaternion.Euler(0f, SkyTurn, 0f) * SunBetween(sa.sun, sb.sun, t);

                skyMat.SetTexture("_CubeA", sa.cube);
                skyMat.SetTexture("_CubeB", sb.cube);
                skyMat.SetFloat("_Blend", a.sky == b.sky ? 0f : t);
                // the grade lives in the tint (exposure stays 1), so a blend between two grades is one colour
                skyMat.SetColor("_Tint", Color.Lerp(gradeA, gradeB, t));
                skyMat.SetFloat("_Exposure", 1f);
                skyMat.SetFloat(RotationId, SkyTurn);

                // ambient: each sky's own light, graded the same way, then blended (RenderSettings wants gamma colours)
                Color ka = gradeA * a.ambient, kb = gradeB * b.ambient;
                RenderSettings.ambientSkyColor = Color.Lerp(Mul(sa.ambSky, ka), Mul(sb.ambSky, kb), t).gamma;
                RenderSettings.ambientEquatorColor = Color.Lerp(Mul(sa.ambEq, ka), Mul(sb.ambEq, kb), t).gamma;
                RenderSettings.ambientGroundColor = Color.Lerp(Mul(sa.ambGround, ka), Mul(sb.ambGround, kb), t).gamma;
                // fog: the horizon's colour (RenderSettings wants it gamma-encoded)
                Color fa = Mul(sa.horizon, gradeA) * a.fogGain, fb = Mul(sb.horizon, gradeB) * b.fogGain;
                RenderSettings.fogColor = Color.Lerp(fa, fb, t).gamma;
                // reflections: the nearer sky's cubemap (it is stored squeezed, so a little dimmer than it shows)
                // reflections: the sky as shown (both photos blended, graded), drawn into a small cubemap now and
                // then as the light changes, so they turn with the day rather than jump half-way through a blend
                // (what the cube shows: which skies, how far between them, their grade, how far the sky is turned)
                Color tint = Color.Lerp(gradeA, gradeB, t);
                if (CaptureReflection(new Vector4(a.sky * 4 + b.sky, a.sky == b.sky ? 0f : t, tint.r + tint.g + tint.b, SkyTurn)))
                    probe.intensity = Mathf.Lerp(a.reflect, b.reflect, t);
                else
                {
                    // (no cubemap capture on this platform: the nearer photo, stored squeezed so a little dimmer)
                    var refl = t < 0.5f ? sa.cube : sb.cube;
                    if (refl != lastReflection) { lastReflection = refl; probe.customBakedTexture = refl; }
                    probe.intensity = Mathf.Lerp(a.reflect * a.exposure, b.reflect * b.exposure, t);
                }

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
            Shader.SetGlobalFloat("_BackdropSat", Mathf.Lerp(a.bdSat, b.bdSat, t));
            Shader.SetGlobalVector("_BackdropGlow", Vector2.Lerp(a.bdGlow, b.bdGlow, t));
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
            Headlamp(Mathf.Lerp(a.headlamp, b.headlamp, t));
            PlaceTorch(headlamp != null && headlamp.enabled);
            if (glowFx != null) glowFx.Step(0f);
            Applied?.Invoke(this);
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
            bool on = k > 0.01f && (camp == null || !camp.Active) && !HeadlampOff && FlashlightOn;
            headlamp.enabled = on;
            bool beamOn = on && !rider.Crashed;   // (the lamp tumbles with the body after a fall: no beam then)
            if (beam != null) beam.gameObject.SetActive(beamOn);
            // the signs shine this lamp's light back (Resources/SignLit.shader): where it is, where it points, how strong
            Shader.SetGlobalVector("_LampPos", new Vector4(0f, 0f, 0f, on ? 1f : 0f));
            if (!on) return;
            headlamp.intensity = 32f * k;   // (falls off with the square of distance: about 1 on the snow ahead)
            var cam = Camera.main;
            // the flashlight strapped to the jacket, pointed down the line the rider is riding: the pool of light
            // swings with the carves out ahead on the snow
            float th = rider.TravelHeading;
            Vector3 fwd = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
            Vector3 torchAt = torch != null ? torch.position : rider.transform.position + Vector3.up * 1.25f + fwd * 0.2f;
            // aimed at the snow a few metres ahead (the slope falls away at ~36 degrees, so a fixed tilt would
            // light the hill far below): a pool of light right in front of the rider
            Vector3 spot = rider.transform.position + fwd * 7f;
            spot.y = Slope.DrawnHeight(spot.x, spot.z);
            Vector3 aim = spot - torchAt;
            if (aim.sqrMagnitude < 0.01f || rider.Airborne) aim = fwd - Vector3.up * 0.9f;
            if (rider.Crashed && torch != null) aim = torch.forward;   // strapped to the tumbling body: it points where the chest does
            Quaternion aimRot = Quaternion.LookRotation(aim.normalized);
            // the light itself sits a little out along the beam, past the rider's own arms and board: from right at
            // the jacket it would blaze the hands and helmet white (light falls off with the square of distance)
            headlamp.transform.SetPositionAndRotation(torchAt + aimRot * Vector3.forward * 1.3f, aimRot);
            Vector3 lp = headlamp.transform.position;
            if (beamOn)
            {
                beam.SetPositionAndRotation(torchAt + headlamp.transform.forward * 0.1f, headlamp.transform.rotation);
                // seen from behind, straight down the beam, a cone is just a disc of glow round the rider: show the
                // beam only as it swings across the view
                float along = cam != null ? Vector3.Dot(cam.transform.forward, headlamp.transform.forward) : 1f;
                float side = 1f - Mathf.SmoothStep(0.78f, 0.96f, along);
                beamMat.SetColor("_BaseColor", new Color(headlamp.color.r, headlamp.color.g, headlamp.color.b, Mathf.Min(0.6f, 0.22f * k * side * BeamGain)));
            }
            Shader.SetGlobalVector("_LampPos", new Vector4(lp.x, lp.y, lp.z, 1f));
            Shader.SetGlobalVector("_LampDir", headlamp.transform.forward);
            Shader.SetGlobalColor("_LampColor", headlamp.color.linear * (headlamp.intensity * SignReturn));
            Shader.SetGlobalVector("_LampCone", new Vector4(Mathf.Cos(headlamp.spotAngle * 0.5f * Mathf.Deg2Rad),
                                                            Mathf.Cos(headlamp.innerSpotAngle * 0.5f * Mathf.Deg2Rad), SignReach, 0f));
        }

        /// <summary>
        /// The beam: a cone out of the lamp, additive and faint, brightest at the lamp and gone by its far end, so
        /// it reads as light in cold air rather than a solid shape. One small mesh, no lighting cost.
        /// </summary>
        void BuildBeam()
        {
            const int seg = 20; const float len = 11f, spread = 0.36f;   // about 20 degrees either side
            float[] zs = { 0f, 0.6f, len * 0.35f, len };
            float[] alpha = { 0.0f, 0.05f, 0.55f, 0f };   // (nothing at the lamp itself: a hot spot there reads as a glow on the rider)
            var verts = new System.Collections.Generic.List<Vector3>(); var cols = new System.Collections.Generic.List<Color>();
            var tris = new System.Collections.Generic.List<int>();
            for (int ring = 0; ring < zs.Length; ring++)
            {
                float rad = 0.06f + zs[ring] * spread;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    verts.Add(new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, zs[ring]));
                    cols.Add(new Color(1f, 1f, 1f, alpha[ring]));
                }
            }
            for (int ring = 0; ring < zs.Length - 1; ring++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = ring * (seg + 1) + i, b0 = a0 + seg + 1;
                    tris.AddRange(new[] { a0, b0, a0 + 1, a0 + 1, b0, b0 + 1 });
                }
            var mesh = new Mesh { name = "LampBeam" };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            var go = new GameObject("LampBeam");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            beamMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "LampBeam" };
            beamMat.SetFloat("_Surface", 1f); beamMat.SetFloat("_Blend", 2f);   // additive
            beamMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); beamMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            beamMat.SetFloat("_ZWrite", 0f); beamMat.SetFloat("_Cull", 0f);
            beamMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            beamMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mr.sharedMaterial = beamMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            beam = go.transform;
            go.SetActive(false);
        }

        /// <summary>
        /// The flashlight on the jacket: a small dark body with a lens that glows when it's on, strapped to the
        /// chest a little to the left, facing the way the rider faces. Placed every frame from the chest bone, so it
        /// rides with every pose and tumbles with the body.
        /// </summary>
        void BuildTorch()
        {
            var lensSh0 = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Mats.LitShader;
            var root = new GameObject("ChestTorch").transform;
            root.SetParent(transform, false);
            var bodyGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Kill(bodyGo.GetComponent<Collider>());
            bodyGo.transform.SetParent(root, false);
            bodyGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bodyGo.transform.localScale = new Vector3(0.045f, 0.045f, 0.045f);   // 4.5 cm across, 9 cm long
            var darkSh = Mats.LitShader;
            if (darkSh == null || lensSh0 == null) { Kill(root.gameObject); return; }   // (never stop the game for a prop)
            var dark = new Material(darkSh) { name = "TorchBody" };
            dark.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f)); dark.SetFloat("_Smoothness", 0.6f);
            bodyGo.GetComponent<MeshRenderer>().sharedMaterial = dark;
            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Kill(lens.GetComponent<Collider>());
            lens.transform.SetParent(root, false);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.045f);
            lens.transform.localScale = new Vector3(0.042f, 0.042f, 0.012f);
            // (Particles/Unlit, which the beam uses too: plain URP/Unlit is stripped from the phone build)
            torchLens = new Material(lensSh0) { name = "TorchLens" };
            lens.GetComponent<MeshRenderer>().sharedMaterial = torchLens;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            torch = root;
        }

        static void Kill(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        void PlaceTorch(bool lit)
        {
            if (torch == null) return;
            var chest = body != null ? body.ChestBone : null;
            bool show = chest != null && body.gameObject.activeInHierarchy;
            if (torch.gameObject.activeSelf != show) torch.gameObject.SetActive(show);
            if (!show) return;
            if (rider != null && rider.Crashed)
            {
                // strapped on: once the body is thrown it goes wherever the chest goes
                torch.SetPositionAndRotation(chest.TransformPoint(torchLocalPos), chest.rotation * torchLocalRot);
            }
            else
            {
                // on the jacket's front, a little to the left, facing the way the rider rides
                float th = rider != null ? rider.TravelHeading : 0f;
                Vector3 face = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                Vector3 left = Vector3.Cross(face, Vector3.up).normalized;
                torch.SetPositionAndRotation(chest.position + face * 0.17f + left * 0.07f + Vector3.up * 0.02f,
                                             Quaternion.LookRotation(face - Vector3.up * 0.35f));
                torchLocalPos = chest.InverseTransformPoint(torch.position);
                torchLocalRot = Quaternion.Inverse(chest.rotation) * torch.rotation;
            }
            torchLens.SetColor("_BaseColor", lit ? new Color(1.6f, 1.5f, 1.3f) : new Color(0.25f, 0.25f, 0.27f));
        }

        /// <summary>How much more of the headlamp a reflective sign sends back than a matte board would.</summary>
        const float SignReturn = 0.16f;   // (scaled with the lamp: 0.6 at its old strength of 13)
        /// <summary>How far a sign still shines the lamp back (a reflector sends it home efficiently: further than the
        /// lamp lights the snow).</summary>
        const float SignReach = 110f;

        /// <summary>
        /// Degrees the whole sky is turned round the vertical, its sun or moon and clouds with it: the start page's
        /// time-lapse turns it to race the clouds and swing the light round (0 everywhere else).
        /// </summary>
        public static float SkyTurn;
        static readonly int RotationId = Shader.PropertyToID("_Rotation");

        /// <summary>The test tools switch the headlamp off to compare signs with and without it.</summary>
        public static bool HeadlampOff;
    }
}
