using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Summit
{
    /// <summary>
    /// The weather on the mountain, rolled at random for each new mountain (each camp visit, each new run), weighted by
    /// the time of day: CLEAR (calm, or windy with a plume off the summit and snow snaking over the slope), SNOW (a
    /// powder day), STORM, FOG (the run inside a cloud) and CLOUD SEA (a sea of cloud in the valleys that you ride down
    /// into). Some clear and snowy mountains have a storm roll in during the run, foreshadowed by a halo round the sun.
    /// Nothing on screen names it: the player reads it from the sky, the light, the air, the snow and the sound.
    ///
    /// Each weather is one Look (a set of numbers), so any two blend: a front coming in, the cloud sea by the camera's
    /// height, a new choice easing in. It works on top of the time of day: right after DayNight lights the world each
    /// frame (DayNight.Applied) it takes that light and turns it overcast, foggy or dark, so night weather is night.
    /// What it touches, and the particles and cloud it adds (WeatherFX), are listed in weather.md.
    ///
    /// The test harnesses run in batchmode with the weather off (the pre-weather look) unless they ask for one
    /// (TestChoice) or SUMMIT_WEATHER is set.
    /// </summary>
    [DefaultExecutionOrder(-20)]   // before the chase camera: its gust shake goes on what the game set this frame
    public class Weather : MonoBehaviour
    {
        public enum Kind { Clear, Snow, Storm, Fog, CloudSea }
        /// <summary>The WEATHER row's choices (start page, LOOK): 0 rolls at random, the rest hold one weather.</summary>
        public static readonly string[] ChoiceName = { "RANDOM", "CLEAR", "WINDY", "SNOW", "STORM", "FOG", "CLOUD SEA", "FRONT" };
        public const string ChoicePref = "summit_weather";
        /// <summary>The test tools pin a choice here (-1: the player's, or off in batchmode).</summary>
        public static int TestChoice = -1;
        /// <summary>The test tools set the seconds into the run here to show a front at a moment (-1: off).</summary>
        public static float TestRunTime = -1f;
        /// <summary>The weather on now (null: off).</summary>
        public static Weather Active { get; private set; }
        /// <summary>How much the weather hushes the world's sound (SoundMix low-passes by it): 0 none.</summary>
        public static float Hush { get; private set; }

        // ---------------------------------------------------------------- the looks

        /// <summary>Everything one weather is, as numbers that blend.</summary>
        public struct Look
        {
            public float cloud, dark, cloudAmp, cirrus, halo;   // the sky: overcast cover, how dark its deck, its masses; high veil; halo
            public float fog, fogLift;                          // extra fog (exp2 density) and the fog's brightness in the light
            public float snow, flake, fall, streak;            // falling snow: amount, size (0 fine .. 1 big), fall speed, driven streaks
            public float wind, drift, dust, lens;              // wind 0..1, ground drift, diamond dust, flakes on the lens
            public float hush, howl;                           // sound: muffling, the storm's wind bed
            public float powder, crust;                        // the snow underfoot
            public float veil, ceil, ceilV, sea, seaV, cap, plume;   // the photo backdrop: haze, summit in cloud, sea of cloud, cap, plume
            public float sat, contrast;                        // the grade, on top of the player's filter
            public float deck;                                 // the cloud sea's deck seen from above (WeatherFX)

            public static Look Lerp(Look a, Look b, float t)
            {
                float L(float x, float y) => x + (y - x) * t;
                return new Look
                {
                    cloud = L(a.cloud, b.cloud), dark = L(a.dark, b.dark), cloudAmp = L(a.cloudAmp, b.cloudAmp), cirrus = L(a.cirrus, b.cirrus), halo = L(a.halo, b.halo),
                    fog = L(a.fog, b.fog), fogLift = L(a.fogLift, b.fogLift),
                    snow = L(a.snow, b.snow), flake = L(a.flake, b.flake), fall = L(a.fall, b.fall), streak = L(a.streak, b.streak),
                    wind = L(a.wind, b.wind), drift = L(a.drift, b.drift), dust = L(a.dust, b.dust), lens = L(a.lens, b.lens),
                    hush = L(a.hush, b.hush), howl = L(a.howl, b.howl), powder = L(a.powder, b.powder), crust = L(a.crust, b.crust),
                    veil = L(a.veil, b.veil), ceil = L(a.ceil, b.ceil), ceilV = L(a.ceilV, b.ceilV), sea = L(a.sea, b.sea), seaV = L(a.seaV, b.seaV),
                    cap = L(a.cap, b.cap), plume = L(a.plume, b.plume), sat = L(a.sat, b.sat), contrast = L(a.contrast, b.contrast), deck = L(a.deck, b.deck),
                };
            }
        }

        static readonly Look ClearLook = new Look { fogLift = 1f, fall = 1.5f, wind = 0.2f, ceilV = 1f, seaV = 0.3f };
        // bluebird and blowing: the sky still clear, a plume off the summit and a cap on it, snow snaking down the slope
        static readonly Look WindyLook = new Look
        {
            fogLift = 1f, snow = 0.14f, flake = 0f, fall = 1.2f, streak = 1f, wind = 1f, drift = 1f, lens = 0.12f, howl = 0.45f,
            crust = 1f, ceilV = 1f, seaV = 0.3f, cap = 0.85f, plume = 1f, contrast = 4f,
        };
        // a powder day: a bright grey deck, no shadow, big slow flakes, the summit gone into cloud, the world gone quiet
        static readonly Look SnowLook = new Look
        {
            cloud = 1f, dark = 0.12f, cloudAmp = 0.22f, fog = 0.0042f, fogLift = 1.04f,
            snow = 1f, flake = 1f, fall = 1.0f, wind = 0.3f, lens = 0.45f, hush = 0.55f, powder = 1f,
            veil = 0.45f, ceil = 1f, ceilV = 0.7f, seaV = 0.3f, sat = -10f,
        };
        // a blizzard: a dark moving sky, snow driven sideways, trees bending, the camera buffeted, the range gone
        static readonly Look StormLook = new Look
        {
            cloud = 1f, dark = 0.55f, cloudAmp = 0.8f, fog = 0.0095f, fogLift = 0.86f,
            snow = 1f, flake = 0.35f, fall = 2.2f, streak = 0.8f, wind = 1f, drift = 1f, lens = 1f, hush = 0.15f, howl = 1f,
            powder = 0.7f, veil = 0.85f, ceil = 1f, ceilV = 0.5f, seaV = 0.3f, sat = -25f, contrast = -4f,
        };
        // inside the cloud: white, flat, no horizon, quiet; a few fine crystals; it thickens and thins as you ride
        static readonly Look FogLook = new Look
        {
            cloud = 1f, cloudAmp = 0f, fog = 0.0095f, fogLift = 0.94f, snow = 0.08f, flake = 0f, fall = 0.6f,
            wind = 0.1f, lens = 0.22f, hush = 0.75f, powder = 0.4f, veil = 1f, ceil = 1f, ceilV = 0.3f, seaV = 0.3f,
            sat = -15f, contrast = -3f,
        };
        // under the cloud sea: a low grey ceiling, dull light, the summit hidden
        static readonly Look BelowLook = new Look
        {
            cloud = 1f, dark = 0.25f, cloudAmp = 0.3f, fog = 0.0035f, fogLift = 0.95f, fall = 1f, wind = 0.15f, hush = 0.3f,
            veil = 0.4f, ceil = 1f, ceilV = 0.55f, seaV = 0.3f, sat = -12f,
        };

        /// <summary>The fog never closes in further than this (exp2 density): about half contrast at 50 m, so the next
        /// hazard shows a couple of seconds ahead at speed.</summary>
        const float MaxFog = 0.0125f;

        // ---------------------------------------------------------------- the odds

        /// <summary>Weights per time of day (morning, day, evening, night) for CLEAR, SNOW, STORM, FOG, CLOUD SEA.</summary>
        static readonly float[,] Odds =
        {
            { 30f, 45f, 40f, 40f },
            { 20f, 20f, 20f, 30f },
            { 5f, 12f, 18f, 12f },
            { 25f, 10f, 10f, 10f },
            { 20f, 13f, 12f, 8f },
        };
        const float WindyChance = 0.35f, FrontAfterClear = 0.2f, FrontAfterSnow = 0.25f, StormEases = 0.25f;

        // ---------------------------------------------------------------- state

        public Kind Base { get; private set; }
        public bool Windy { get; private set; }
        /// <summary>Seconds into the run a front starts rolling in (-1: none); to what; and how long it takes.</summary>
        public float FrontAt { get; private set; } = -1f;
        public Kind FrontTo { get; private set; }
        const float FrontSeconds = 45f;
        /// <summary>The cloud sea: its top and how thick it is (world y, m).</summary>
        public float SeaTop { get; private set; }
        public float SeaThick { get; private set; }
        /// <summary>Whether the start page is up (its own weather: far snow, the far mountain's haze, the summit's cloud).</summary>
        public bool OnStartPage => wfx != null && wfx.OnStartPage;
        public string FxState => wfx != null ? wfx.State : "no fx";
        /// <summary>The look being shown now.</summary>
        public Look Now => shown;
        /// <summary>How far a front has come in, 0..1.</summary>
        public float FrontProgress { get; private set; }

        Look shown, target;
        bool snapped;
        int seed = int.MinValue, choiceShown = -99;
        float runT, fogPatch, gustShake, shakeBase, shakeWritten = -1f;
        Rider rider;
        Camp camp;
        Camera cam;
        ChaseCamera chase;
        Atmosphere atmo;
        SnowFX fx;
        Material snowMat;
        WeatherFX wfx;
        float baseFriction;
        Color baseSnowCol; float baseSmooth = -1f, baseBump = 1f;
        Volume gradeVol; ColorAdjustments gradeCa, mainCa;
        AudioSource bed, howl;
        Texture2D cloudTex;

        /// <summary>The player's WEATHER row (PlayerPrefs; read on first use: not in a static initializer).</summary>
        public static int Choice
        {
            get
            {
                if (TestChoice >= 0) return TestChoice;
                if (choiceSetting < 0)
                {
                    choiceSetting = Mathf.Clamp(PlayerPrefs.GetInt(ChoicePref, 0), 0, ChoiceName.Length - 1);
                    // device testing: SUMMIT_WEATHER=STORM (any of the row's names, or its number) holds that weather
                    var env = System.Environment.GetEnvironmentVariable("SUMMIT_WEATHER");
                    if (!string.IsNullOrEmpty(env))
                    {
                        int i = System.Array.IndexOf(ChoiceName, env.ToUpperInvariant().Replace('_', ' '));
                        if (i < 0 && int.TryParse(env, out int n)) i = n;
                        if (i >= 0 && i < ChoiceName.Length) choiceSetting = i;
                    }
                }
                return choiceSetting;
            }
        }
        static int choiceSetting = -1;
        public static string ChoiceLabel => ChoiceName[Choice];

        /// <summary>Step the WEATHER row; the new weather comes in at once.</summary>
        public static void StepChoice(int dir)
        {
            int c = (Choice + dir + ChoiceName.Length) % ChoiceName.Length;
            if (TestChoice >= 0) TestChoice = c; else { choiceSetting = c; PlayerPrefs.SetInt(ChoicePref, c); PlayerPrefs.Save(); }
            if (Active != null) { Active.seed = int.MinValue; Active.Step(0f); }
        }

        /// <summary>
        /// Made once the world is (SummitGame, after DayNight.Hook). Off in batchmode unless a test asks for a weather,
        /// so the harnesses see the game as it was.
        /// </summary>
        public static Weather Build(Rider r, Camp c, Camera camera, Atmosphere a, SnowFX snowFx, Material snow, SoundMix sound)
        {
            bool asked = TestChoice >= 0 || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SUMMIT_WEATHER"));
            if (Application.isBatchMode && !asked) return null;
            var w = new GameObject("Weather").AddComponent<Weather>();
            w.rider = r; w.camp = c; w.cam = camera; w.atmo = a; w.fx = snowFx; w.snowMat = snow;
            w.chase = camera != null ? camera.GetComponent<ChaseCamera>() : null;
            w.baseFriction = Rider.Friction;
            if (snow != null)
            {
                w.baseSnowCol = snow.HasProperty("_BaseColor") ? snow.GetColor("_BaseColor") : Color.white;
                w.baseSmooth = snow.HasProperty("_Smoothness") ? snow.GetFloat("_Smoothness") : -1f;
                w.baseBump = snow.HasProperty("_BumpScale") ? snow.GetFloat("_BumpScale") : 1f;
            }
            w.cloudTex = CloudNoise();
            w.wfx = new WeatherFX(w, r, camera, w.cloudTex);
            w.BuildGrade();
            w.BuildSound(sound != null ? sound.gameObject : w.gameObject);
            Active = w;
            DayNight.Applied += w.OnDayNight;
            w.Step(0f);
            return w;
        }

        void OnDestroy()
        {
            DayNight.Applied -= OnDayNight;
            if (Active == this) Active = null;
            Hush = 0f;
            Rider.Friction = baseFriction;
            TreeWind.Strength = 1f;
            DayNight.BeamGain = 1f;
            if (atmo != null) atmo.SnowAmount = 1f;
            // the snow material is a project asset: never leave it changed in the editor
            if (snowMat != null && lastPowder >= 0f)
            {
                if (snowMat.HasProperty("_BaseColor")) snowMat.SetColor("_BaseColor", baseSnowCol);
                if (baseSmooth >= 0f) snowMat.SetFloat("_Smoothness", baseSmooth);
                if (snowMat.HasProperty("_BumpScale")) snowMat.SetFloat("_BumpScale", baseBump);
            }
            Shader.SetGlobalVector(VeilId, Vector4.zero); Shader.SetGlobalVector(CapId, Vector4.zero);
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty(OvercastId)) { sky.SetFloat(OvercastId, 0f); sky.SetFloat(CirrusId, 0f); sky.SetFloat(HaloId, 0f); }
            wfx?.Destroy();
        }

        // ---------------------------------------------------------------- rolling the weather

        void Roll()
        {
            seed = Slope.Seed;
            int choice = Choice;
            choiceShown = choice;
            FrontAt = -1f; Windy = false;
            int phase = Mathf.Clamp((int)TimeOfDay.Own, 0, 3);
            var rng = new System.Random(unchecked(Slope.Seed * 7919 + rolls++ * 104729 + Random.Range(0, 1 << 30)));
            float R() => (float)rng.NextDouble();
            switch (choice)
            {
                case 1: Base = Kind.Clear; break;
                case 2: Base = Kind.Clear; Windy = true; break;
                case 3: Base = Kind.Snow; break;
                case 4: Base = Kind.Storm; break;
                case 5: Base = Kind.Fog; break;
                case 6: Base = Kind.CloudSea; break;
                case 7: Base = Kind.Clear; FrontAt = 8f; FrontTo = Kind.Storm; break;
                default:
                {
                    float sum = 0f;
                    for (int k = 0; k < 5; k++) sum += Odds[k, phase];
                    float pick = R() * sum;
                    int kind = 0;
                    for (; kind < 4; kind++) { pick -= Odds[kind, phase]; if (pick < 0f) break; }
                    Base = (Kind)kind;
                    if (Base == Kind.Clear) Windy = R() < WindyChance;
                    float front = Base == Kind.Clear ? FrontAfterClear : Base == Kind.Snow ? FrontAfterSnow : Base == Kind.Storm ? StormEases : 0f;
                    if (R() < front) { FrontAt = Mathf.Lerp(25f, 70f, R()); FrontTo = Base == Kind.Storm ? Kind.Snow : Kind.Storm; }
                    break;
                }
            }
            // the cloud sea: its top a third to half way down the mountain, a hundred or two metres thick
            SeaTop = Slope.FallLine(Slope.FinishZ * Mathf.Lerp(0.3f, 0.5f, R()));
            SeaThick = Mathf.Lerp(130f, 240f, R());
            fogPatch = R() * 100f;
            snapped = false;
            Debug.Log($"WEATHER {Base}{(Windy ? " windy" : "")}{(FrontAt >= 0f ? $", {FrontTo} rolling in at {FrontAt:0}s" : "")} ({ChoiceName[choice]}, {TimeOfDay.PhaseName[phase]})");
        }
        int rolls;

        static Look Preset(Kind k, bool windy) =>
            k == Kind.Snow ? SnowLook : k == Kind.Storm ? StormLook : k == Kind.Fog ? FogLook : windy ? WindyLook : ClearLook;

        /// <summary>The look for now: the weather, the time of day, a front, and for the cloud sea the camera's height.</summary>
        Look Target()
        {
            Look l = Preset(Base, Windy);
            float clock = TimeOfDay.Clock;
            float coldness = Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(clock - 0.3f) / 0.5f), Mathf.Clamp01(1f - Mathf.Abs(clock - 3.5f) / 0.6f));
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            if (Base == Kind.Clear && !Windy) l.dust = coldness;   // diamond dust: the coldest clear mornings and nights
            if (Base == Kind.Fog)
            {
                // the cloud thickens and thins down the mountain: visibility opens and closes as you ride
                float z = rider != null ? rider.transform.position.z : 0f;
                l.fog *= Mathf.Lerp(0.62f, 1.08f, Mathf.PerlinNoise(z / 160f + fogPatch, 0.37f));
            }
            if (Base == Kind.CloudSea)
            {
                Look above = ClearLook; above.sea = 1f; above.deck = 1f; above.dust = coldness * 0.6f;
                float h = wfx != null && wfx.OnStartPage ? 2000f : cp.y - SeaTop;
                // the sea's top as the backdrop shows it: down toward the valley from high above, up to the horizon close to it
                above.seaV = Mathf.Clamp((Mathf.Atan2(-Mathf.Max(h, 1f), 3000f) + 0.95f) / 1.16f, 0.25f, 0.8f);
                Look inside = FogLook; inside.seaV = above.seaV;
                Look below = BelowLook;
                if (h > 0f) l = Look.Lerp(above, inside, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(40f, -10f, h)));
                else if (h > -SeaThick) l = Look.Lerp(inside, below, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-SeaThick + 60f, -SeaThick - 20f, h)));
                else l = below;
            }
            // a front: foreshadowed from the start by a veil of cirrus and a halo, then the sky greys from upwind, the
            // wind rises, and the snow comes in last
            FrontProgress = 0f;
            if (FrontAt >= 0f)
            {
                float t = TestRunTime >= 0f ? TestRunTime : runT;
                float f = Mathf.Clamp01((t - FrontAt) / FrontSeconds);
                FrontProgress = f;
                if (FrontTo == Kind.Storm)
                {
                    l.cirrus = Mathf.Max(l.cirrus, 0.6f * (1f - f));
                    l.halo = Mathf.Max(l.halo, (1f - Mathf.SmoothStep(0f, 0.35f, f)) * (1f - l.cloud));
                }
                if (f > 0f) l = Toward(l, Preset(FrontTo, false), f);
            }
            l.fog = Mathf.Min(l.fog, MaxFog);
            return l;
        }

        /// <summary>A front coming in: each part of the weather on its own schedule (sky first, snow last).</summary>
        static Look Toward(Look a, Look b, float f)
        {
            float S(float x, float y) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(x, y, f));
            Look r = Look.Lerp(a, b, S(0f, 1f));
            float sky = S(0f, 0.55f), wind = S(0.1f, 0.6f), snow = S(0.3f, 0.95f), fog = S(0.35f, 1f);
            r.cloud = Mathf.Lerp(a.cloud, b.cloud, sky); r.dark = Mathf.Lerp(a.dark, b.dark, sky); r.cloudAmp = Mathf.Lerp(a.cloudAmp, b.cloudAmp, sky);
            r.wind = Mathf.Lerp(a.wind, b.wind, wind); r.howl = Mathf.Lerp(a.howl, b.howl, wind); r.drift = Mathf.Lerp(a.drift, b.drift, wind);
            r.snow = Mathf.Lerp(a.snow, b.snow, snow); r.lens = Mathf.Lerp(a.lens, b.lens, snow); r.flake = Mathf.Lerp(a.flake, b.flake, snow);
            r.fog = Mathf.Lerp(a.fog, b.fog, fog); r.veil = Mathf.Lerp(a.veil, b.veil, fog);
            return r;
        }

        // ---------------------------------------------------------------- per frame

        void LateUpdate() { Step(Time.deltaTime); }

        /// <summary>Run the weather on by dt (the test tools call it: batchmode runs no LateUpdate).</summary>
        public void Step(float dt)
        {
            if (seed != Slope.Seed || choiceShown != Choice) Roll();
            bool running = rider != null && rider.Running && (camp == null || !camp.Active);
            runT = running ? runT + dt : 0f;
            target = Target();
            if (!snapped || dt <= 0f) { shown = target; snapped = true; }
            else shown = Look.Lerp(shown, target, 1f - Mathf.Exp(-dt * (Base == Kind.CloudSea ? 3f : 0.8f)));
            var l = shown;

            // the wind: through the trees, the falling snow and the gusts you hear (TreeWind); a buffet on the camera
            TreeWind.Strength = 0.6f + 1.6f * l.wind;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            float gust = TreeWind.Gust(cp.x, cp.z);
            gustShake = Mathf.Lerp(gustShake, l.wind * l.wind * gust * 0.16f, 1f - Mathf.Exp(-dt * 6f));
            if (chase != null && chase.enabled)
            {
                // on top of what the game set this frame (if it set nothing, on top of what it set last)
                float game = Mathf.Abs(chase.shake - shakeWritten) < 1e-6f ? shakeBase : chase.shake;
                shakeBase = game; shakeWritten = game + gustShake; chase.shake = shakeWritten;
            }

            // the snow underfoot: fresh powder is slower, wind crust a touch faster (small: a percent or two of top speed)
            Rider.Friction = baseFriction * (1f + 0.8f * l.powder - 0.15f * l.crust);
            SnowSurface(l);

            // the falling snow (Atmosphere's): how much, how big, how fast, how driven
            if (atmo != null) atmo.Weather(l.snow, l.flake, l.fall, l.streak);
            Hush = l.hush;
            DayNight.BeamGain = 1f + 1.6f * Mathf.Clamp01(l.fog / 0.006f) + 0.8f * l.snow;   // the headlamp's beam lit up in the snow and fog
            Grade(l);
            Sound(l, gust, dt);
            wfx.Step(l, dt, gust);
        }

        float lastSmooth = -1f, lastPowder = -1f;
        void SnowSurface(Look l)
        {
            // (the snow material is a project asset: in the editor it is only changed in play mode, which always ends in
            // OnDestroy putting it back; a test tool exiting with the weather still on would save it changed)
            if (Application.isEditor && !Application.isPlaying) return;
            if (snowMat == null || (Mathf.Abs(l.powder - lastPowder) < 0.01f && Mathf.Abs(l.crust - lastSmooth) < 0.01f)) return;
            lastPowder = l.powder; lastSmooth = l.crust;
            // fresh powder: whiter and matte, its grain softer; wind crust: polished (a sheen) and its ripples sharper
            if (snowMat.HasProperty("_BaseColor")) snowMat.SetColor("_BaseColor", Color.Lerp(baseSnowCol, new Color(0.95f, 0.96f, 0.98f, baseSnowCol.a), l.powder * 0.6f));
            if (baseSmooth >= 0f) snowMat.SetFloat("_Smoothness", Mathf.Clamp01(baseSmooth + 0.2f * l.crust - 0.14f * l.powder));
            if (snowMat.HasProperty("_BumpScale")) snowMat.SetFloat("_BumpScale", baseBump * (1f + 0.7f * l.crust - 0.45f * l.powder));
        }

        // ---------------------------------------------------------------- the light, after DayNight's

        static readonly int OvercastId = Shader.PropertyToID("_Overcast"), ZenithId = Shader.PropertyToID("_OvercastZenith"),
                            HorizonId = Shader.PropertyToID("_OvercastHorizon"), CloudTexId = Shader.PropertyToID("_CloudTex"),
                            CloudAmpId = Shader.PropertyToID("_CloudAmp"), ScrollId = Shader.PropertyToID("_CloudScroll"),
                            FrontId = Shader.PropertyToID("_Front"), CirrusId = Shader.PropertyToID("_Cirrus"), HaloId = Shader.PropertyToID("_Halo"),
                            SunWId = Shader.PropertyToID("_SunW"),
                            VeilId = Shader.PropertyToID("_BackdropVeil"), SeaId = Shader.PropertyToID("_BackdropSea"),
                            CapId = Shader.PropertyToID("_BackdropCap"), CloudId = Shader.PropertyToID("_BackdropCloud"),
                            CloudTexGlobalId = Shader.PropertyToID("_WeatherCloudTex"), CloudScrollGlobalId = Shader.PropertyToID("_WeatherCloudScroll");
        /// <summary>Where the photo backdrop's summit is (uv; found from its sky mask).</summary>
        static readonly Vector2 PhotoSummit = new Vector2(0.528f, 0.922f);
        Vector2 scroll;
        float lastCover = -1f, lastHalo = -1f;

        static float Lum(Color c) => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
        static Color Opaque(Color c) { c.a = 1f; return c; }

        /// <summary>
        /// DayNight has just lit the world for the time of day: turn that light into the weather's. Overcast takes the
        /// sun's direct light away and gives part of it back as flat light from everywhere; the fog closes in, in a grey
        /// as bright as the light allows; the sky gets its deck; the snow loses its glints.
        /// </summary>
        void OnDayNight(DayNight dn)
        {
            var l = shown;
            var sun = RenderSettings.sun;
            float cover = l.cloud;
            // the light that was the sun's, before the cloud
            Color sunLin = sun != null ? sun.color.linear * sun.intensity : Color.black;
            if (sun != null)
            {
                sun.intensity *= 1f - 0.9f * cover;
                sun.shadowStrength *= 1f - 0.95f * cover;
            }
            Color sky = RenderSettings.ambientSkyColor.linear, eq = RenderSettings.ambientEquatorColor.linear, gr = RenderSettings.ambientGroundColor.linear;
            Color mean = (sky + eq + gr) / 3f;
            // the deck's light: the sun's, scattered through it, plus the sky's own; darker as the deck thickens. By
            // night the moon is simply gone behind it (an overcast night is darker than a moonlit one)
            float day = 1f - dn.Darkness;
            Color diffuse = (mean * (1f - 0.35f * dn.Darkness) + sunLin * (0.32f * day)) * (1f - 0.55f * l.dark);
            Color flat = Color.Lerp(diffuse, new Color(Lum(diffuse), Lum(diffuse), Lum(diffuse)), 0.55f);
            RenderSettings.ambientSkyColor = Opaque(Color.Lerp(sky, flat * 1.05f, cover)).gamma;
            RenderSettings.ambientEquatorColor = Opaque(Color.Lerp(eq, flat, cover)).gamma;
            RenderSettings.ambientGroundColor = Opaque(Color.Lerp(gr, flat * 0.85f, cover)).gamma;

            // the fog: the horizon's colour drained to grey as the cloud closes, as bright as the light under it
            Color fogLin = RenderSettings.fogColor.linear;
            // (as bright as the horizon was: by night that is dark, so a night storm is a dark one)
            // (keeping a little of the horizon's own hue: the blue of a night, the warmth of a dusk)
            float light = Lum(fogLin) * (1f - 0.38f * l.dark) * l.fogLift;
            Color hue = fogLin * (light / Mathf.Max(Lum(fogLin), 1e-4f));
            Color grey = Opaque(Color.Lerp(new Color(light * 0.97f, light * 0.99f, light * 1.05f), hue, 0.35f + 0.3f * dn.Darkness));
            float fogK = Mathf.Clamp01(Mathf.Max(cover, l.fog / 0.006f));
            Color fogNow = Color.Lerp(fogLin, grey, fogK * 0.9f);
            bool startPage = wfx.OnStartPage;
            if (startPage && l.sea > 0.01f) fogNow = Color.Lerp(fogNow, CloudLit() * 0.95f, 0.5f * l.sea * (1f - 0.75f * dn.Darkness));   // the start page's sea of cloud, sunlit
            RenderSettings.fogColor = Opaque(fogNow).gamma;
            RenderSettings.fogDensity = Mathf.Sqrt(RenderSettings.fogDensity * RenderSettings.fogDensity + l.fog * l.fog);

            // glints need the sun on the snow
            if (fx != null) fx.glintRate *= (1f - cover) * (1f + 0.5f * l.crust);

            // the sky: the deck, its colour from the fog's at the horizon, darker and heavier overhead in a storm
            var mat = RenderSettings.skybox;
            if (mat != null && mat.HasProperty(OvercastId))
            {
                if (mat.GetTexture(CloudTexId) != cloudTex) mat.SetTexture(CloudTexId, cloudTex);
                mat.SetFloat(OvercastId, cover);
                // (SetColor takes gamma colours and turns them linear: these are linear already)
                mat.SetColor(HorizonId, fogNow.gamma);
                Color zen = fogNow * Mathf.Lerp(0.97f, 0.6f, l.dark); zen.a = 1f;
                mat.SetColor(ZenithId, zen.gamma);
                mat.SetFloat(CloudAmpId, l.cloudAmp);
                mat.SetVector(ScrollId, new Vector4(scroll.x, scroll.y, 0.11f, 0f));
                Vector3 up = new Vector3(-TreeWind.Dir.x, 0.25f, -TreeWind.Dir.y).normalized;   // a front comes in from upwind
                mat.SetVector(FrontId, new Vector4(up.x, up.y, up.z, FrontAt >= 0f && FrontTo == Kind.Storm && FrontProgress < 1f ? 1.2f : 0f));
                mat.SetFloat(CirrusId, l.cirrus);
                mat.SetFloat(HaloId, l.halo);
                Vector3 s = dn.SunDirection;
                mat.SetVector(SunWId, new Vector4(s.x, s.y, s.z, 0f));
                // the snow's reflections are of the sky: draw them again as it changes
                if (Mathf.Abs(cover - lastCover) > 0.02f || Mathf.Abs(l.halo - lastHalo) > 0.05f) { lastCover = cover; lastHalo = l.halo; DayNight.SkyVersion++; }
            }

            // the photo backdrop: haze, the summit in cloud, the sea of cloud, a cap and a plume on the summit
            Shader.SetGlobalVector(VeilId, new Vector4(Mathf.Clamp01(l.veil), l.ceil, l.ceilV, l.sea));
            float seaH = cam != null ? Mathf.Max(cam.transform.position.y - SeaTop, 30f) : 1000f;
            Shader.SetGlobalVector(SeaId, new Vector4(l.seaV, 0.012f, seaH, 0f));
            Shader.SetGlobalTexture(CloudTexGlobalId, cloudTex);
            Shader.SetGlobalVector(CloudScrollGlobalId, new Vector4(scroll.x, scroll.y, 0f, 0f));
            Shader.SetGlobalVector(CapId, new Vector4(PhotoSummit.x, PhotoSummit.y, l.cap * (1f - cover), l.plume * (1f - cover)));
            Color cl = CloudLit();
            Shader.SetGlobalVector(CloudId, new Vector4(cl.r, cl.g, cl.b, Time.time));
            wfx.Light(cl, fogNow, l);
        }

        /// <summary>Cloud in the light now: the sun on its top and the sky's light (linear).</summary>
        public static Color CloudLit()
        {
            Color c = RenderSettings.ambientSkyColor.linear * 0.7f;
            var sun = RenderSettings.sun;
            if (sun != null) c += sun.color.linear * (sun.intensity * 0.42f);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m > 1.1f) c *= 1.1f / m;
            c.a = 1f;
            return c;
        }

        // ---------------------------------------------------------------- the grade

        void BuildGrade()
        {
            var main = FindFirstObjectByType<Volume>();
            if (main == null || main.profile == null || !main.profile.TryGet(out mainCa)) return;
            var go = new GameObject("WeatherGrade");
            go.transform.SetParent(transform, false);
            gradeVol = go.AddComponent<Volume>();
            gradeVol.isGlobal = true; gradeVol.priority = main.priority + 10f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "WeatherGrade";
            gradeCa = p.Add<ColorAdjustments>(false);
            gradeVol.sharedProfile = p;
        }

        void Grade(Look l)
        {
            if (gradeCa == null || mainCa == null) return;
            // on top of whatever the player's filter set: a little colour drained in the grey weathers, contrast down in fog
            gradeCa.saturation.Override(mainCa.saturation.value + l.sat);
            gradeCa.contrast.Override(mainCa.contrast.value + l.contrast);
            gradeCa.postExposure.Override(mainCa.postExposure.value);
            gradeCa.saturation.overrideState = gradeCa.contrast.overrideState = Mathf.Abs(l.sat) + Mathf.Abs(l.contrast) > 0.1f;
            gradeCa.postExposure.overrideState = false;
        }

        // ---------------------------------------------------------------- the sound

        void BuildSound(GameObject on)
        {
            bed = Loop(on, "wind_low"); howl = Loop(on, "wind_high");
        }

        static AudioSource Loop(GameObject on, string clip)
        {
            var s = on.AddComponent<AudioSource>();
            s.clip = Resources.Load<AudioClip>("Audio/" + clip);
            s.loop = true; s.volume = 0f; s.spatialBlend = 0f; s.playOnAwake = false;
            if (s.clip != null) { s.time = s.clip.length * Random.value * 0.9f; s.Play(); }
            return s;
        }

        float bedVol, howlVol, howlPitch = 1f;
        void Sound(Look l, float gust, float dt)
        {
            if (bed == null) return;
            // a storm's wind: a deep roar under everything, and a howl that climbs with each gust
            float k = 1f - Mathf.Exp(-Mathf.Max(dt, 0f) * 2f);
            bedVol = Mathf.Lerp(bedVol, l.howl * (0.32f + 0.18f * gust), k);
            howlVol = Mathf.Lerp(howlVol, l.howl * (0.06f + 0.3f * gust * gust), k);
            howlPitch = Mathf.Lerp(howlPitch, 0.7f + 0.45f * gust, k);
            bed.volume = bedVol; bed.pitch = 0.62f + 0.1f * gust;
            howl.volume = howlVol; howl.pitch = howlPitch;
        }

        // ---------------------------------------------------------------- the cloud noise

        /// <summary>A tiling cloud noise (r: soft masses, g: finer detail), made once.</summary>
        static Texture2D CloudNoise()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "WeatherCloud", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float a = Fbm(u, v, 4, 5, 3), b = Fbm(u, v, 12, 3, 17);
                    px[y * N + x] = new Color32((byte)(Mathf.Clamp01(a) * 255f), (byte)(Mathf.Clamp01(b) * 255f), 0, 255);
                }
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }

        // tiling fractal noise: `freq` cells across at the first octave
        static float Fbm(float u, float v, int freq, int octaves, int salt)
        {
            float s = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int f = freq << o;
                s += TileNoise(u * f, v * f, f, salt + o * 31) * amp; norm += amp; amp *= 0.5f;
            }
            return Mathf.Clamp01((s / norm - 0.5f) * 1.6f + 0.5f);
        }

        static float TileNoise(float x, float y, int period, int salt)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float H(int i, int j)
            {
                i = ((i % period) + period) % period; j = ((j % period) + period) % period;
                uint h = (uint)(i * 374761393 + j * 668265263 + salt * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xffff) / 65535f;
            }
            return Mathf.Lerp(Mathf.Lerp(H(xi, yi), H(xi + 1, yi), fx), Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), fx), fy);
        }

        /// <summary>The cloud drifting: the start page's time-lapse races it.</summary>
        public void Drift(float dt, float rate) { scroll += new Vector2(TreeWind.Dir.x, TreeWind.Dir.y) * (dt * rate); scroll.x = Mathf.Repeat(scroll.x, 64f); scroll.y = Mathf.Repeat(scroll.y, 64f); }
        /// <summary>How far the cloud has drifted (noise uv).</summary>
        public Vector2 CloudScroll => scroll;
    }
}
