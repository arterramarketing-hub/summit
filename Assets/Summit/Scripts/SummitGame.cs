using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Builds the whole game at start-up and runs the loop:
    /// camp → ready → riding → buried → camp. Drop it on one empty GameObject.
    /// The Summit ▸ Build Scene menu does that for you and makes the materials.
    /// </summary>
    public class SummitGame : MonoBehaviour
    {
        [Header("Materials (made by Summit ▸ Build Scene; plain ones are made if empty)")]
        public Material snow;
        public Material foliage, bark;
        public Material jacket, pants, board, skin, dark;
        public Material trail, spray;

        enum State { Hub, Ready, Riding, Buried }   // Hub: walking round the summit camp before a run
        State state;
        Camp camp;
        MetaUi meta;

        Rider rider;
        RiderInput input;
        SlopeStreamer terrain;
        Forest props;
        RiderBody body;
        ChaseCamera chase;
        Hud hud;
        SoundMix audioFx;
        SnowFX fx;
        Blood blood;
        Atmosphere atmo;
        // three falls a run; the third one ends it
        public const int MaxFalls = 3;
        int falls;
        float wipeoutTimer, hurtShake;
        string fallsNote = "", endTitle = "WIPED OUT";
        bool crevasseWatch;   // went over a crevasse: lost only if the body actually drops in

        // the medical bill: what this run's falls are costing you
        float medical;
        static int BoneCost(string limb) =>
            limb == "Head" ? 12500 : limb == "Ribs" ? 7800 : limb.EndsWith("leg") ? 9400 : limb.EndsWith("arm") ? 4600 : 3000;

        bool RiderTapped()
        {
            if (!hud.OnRider(RiderInput.PointerPosition())) return false;
            audioFx.Play(Sfx.Click);
            bool tube = !rider.Tube;
            rider.Tube = tube; body.SetTube(tube); hud.SetRider(tube);
            PlayerPrefs.SetInt("summit_tube", tube ? 1 : 0); PlayerPrefs.Save();
            body.ResetVisual();
            return true;
        }
        // pause menu and the camera distance setting
        bool paused;
        static readonly float[] CamDist = { 0.65f, 0.82f, 1f, 1.22f, 1.5f };
        static readonly string[] CamName = { "CLOSEST", "CLOSE", "NORMAL", "FAR", "FARTHEST" };
        int camLevel = 2;
        // camera angle (height) and lens (field of view and barrel distortion), from the pause menu
        static readonly float[] AngleHeight = { 0.55f, 0.8f, 1f, 1.5f, 2.2f };
        static readonly string[] AngleName = { "GROUND", "LOW", "NORMAL", "HIGH", "OVERHEAD" };
        static readonly float[] LensFov = { -14f, 0f, 8f, 18f };
        static readonly float[] LensWarp = { 0f, -0.28f, -0.42f, -0.6f };
        static readonly string[] LensName = { "STANDARD", "WIDE", "WIDER", "FISHEYE" };
        int angleLevel = 1, lensLevel = 1;
        UnityEngine.Rendering.Universal.LensDistortion lens;
        // colour filter (LookGrade) and its strength in tenths, and the lens type (LensFX), from the pause menu
        int filterLevel, filterStrength = 10, lensMode;
        int halation;   // the film glow round highlights (LensFX): OFF, SUBTLE, STRONG
        LensFX lensFx;
        DayNight dayNight;   // the time of day's light (TimeOfDay, DayNight.cs), and the pause menu's TIME row
        UnityEngine.Rendering.VolumeProfile post;

        void ApplyCamera()
        {
            chase.heightScale = AngleHeight[angleLevel];
            chase.extraFov = LensFov[lensLevel];
            lensFx.Apply(lens, LensWarp[lensLevel], lensLevel, lensMode);
            hud.SetAngle(AngleName[angleLevel]); hud.SetLens(LensName[lensLevel]); hud.SetLensType(LensFX.ModeName[lensMode]);
            PlayerPrefs.SetInt("summit_angle", angleLevel); PlayerPrefs.SetInt("summit_lens", lensLevel); PlayerPrefs.SetInt("summit_lenstype", lensMode); PlayerPrefs.Save();
        }

        void ApplyLook()
        {
            atmo.BaseVignette = LookGrade.Apply(post, filterLevel, filterStrength / 10f);
            hud.SetFilter(LookGrade.Names[filterLevel]); hud.SetFilterStrength(filterStrength * 10 + "%");
            lensFx.SetHalation(halation); hud.SetHalation(LensFX.HalationName[halation]);
            PlayerPrefs.SetInt("summit_filter", filterLevel); PlayerPrefs.SetInt("summit_filterstrength", filterStrength); PlayerPrefs.SetInt("summit_halation", halation); PlayerPrefs.Save();
        }

        // the SOUND tab's music volume, in steps of 10%
        void StepMusic(int dir)
        {
            int v = Mathf.Clamp(Mathf.RoundToInt(AudioPrefs.MusicVolume * 10f) + dir, 0, 10);
            AudioPrefs.MusicVolume = v / 10f;
            hud.SetMusic(v * 10 + "%");
        }

        void OnApplicationPause(bool away)
        {
            if (!away) return;
            if (state == State.Riding && !paused) SetPaused(true);   // a call or the home swipe mid-run: come back paused
            Missions.Flush();
            PlayerPrefs.Save();
        }

        void OnDestroy() { Progress.Changed -= ApplyOutfit; Missions.Completed = null; }

        void SetPaused(bool on)
        {
            paused = on;
            Time.timeScale = on ? 0f : 1f;
            if (!on) { rider.CancelPop(); input.SwallowCurrentPress(); }   // a menu tap is never a jump
            AudioListener.pause = on;
            hud.ShowPause(on);
            hud.SetCamera(CamName[camLevel]);
            hud.SetAngle(AngleName[angleLevel]); hud.SetLens(LensName[lensLevel]);
            hud.SetPauseSteer(input.useTilt);
            hud.SetPauseTrees(props.IsRealistic);
            if (blood != null) hud.SetBlood(blood.On);
            hud.SetTime(TimeOfDay.LockLabel);
            hud.SetMusic(Mathf.RoundToInt(AudioPrefs.MusicVolume * 10f) * 10 + "%");
            input.SwallowCurrentPress();
        }

        void SetCamLevel(int k)
        {
            camLevel = Mathf.Clamp(k, 0, CamDist.Length - 1);
            chase.distScale = CamDist[camLevel];
            hud.SetCamera(CamName[camLevel]);
            PlayerPrefs.SetInt("summit_camdist", camLevel); PlayerPrefs.Save();
        }   // went over a crevasse: lost only if the body actually drops into it
        bool endBuried;
        readonly Avalanche slide = new Avalanche();
        AvalancheView slideView;

        PowerUps power;
        Smasher smasher;
        Breakables breaks;
        PowerAura aura;
        const float ZoomSeconds = 3f, SuperJumpSeconds = 10f, InvincibleSeconds = 8f;   // how long each pickup lasts
        CourseDressing dressing;
        Wildlife wild;
        FallFlags flags;
        int trickPoints, chainCount;
        float chainTimer, stateTimer, best, runTime;

        int Score => Mathf.RoundToInt(rider.Distance) + trickPoints;
        float Mult => 1f + 0.5f * chainCount;

        float traceT, traceNext, readyT;
        float screamT, screamSlowT;   // how long this fall's screaming has gone on, and how long the body has been slow
        bool ledgeCued, ledgeFading;   // the first-ledge sting has played this run / is on its way out
        static readonly bool autoplay = System.Environment.GetEnvironmentVariable("SUMMIT_AUTOPLAY") == "1";

        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 1f / 120f;   // physics in small steps; the rider is interpolated between them
            Physics.simulationMode = SimulationMode.FixedUpdate;   // never left on manual by a test tool
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;
            Slope.Init(Slope.TodaysSeed());
            best = PlayerPrefs.GetInt("summit_best", 0);
            MakeMissingMaterials();
            SetUpWorld();

            input = gameObject.AddComponent<RiderInput>();

            var rg = new GameObject("Rider");
            rider = rg.AddComponent<Rider>();
            rider.input = input;
            _ = rider.Body;   // its physics body exists before the ragdoll is built (as on device; edit-mode tools skip Awake)
            body = rg.AddComponent<RiderBody>();
            body.Build(rider, jacket, pants, board, skin, dark, trail, spray);
            rider.Trick += OnTrick;
            rider.Crash += OnCrash;
            rider.RoughLanding += OnRough;
            rider.Jumped += () => { if (state == State.Riding) { audioFx.Play(Sfx.Pop); popped = true; } };
            rider.Landed += impact =>
            {
                if (state != State.Riding || impact <= 3f) return;
                if (impact > 11f) audioFx.Play(Sfx.LandHard);
                hurtShake = Mathf.Max(hurtShake, Mathf.Clamp01((impact - 7f) / 12f) * 0.7f);
                audioFx.Play(Sfx.Land, Mathf.Clamp01(impact / 14f));
            };
            rider.Scraped += hit => { if (state == State.Riding) audioFx.Play(Sfx.Scrape, hit); };
            body.BoneBroke += (limb, at) =>
            {
                if (state != State.Riding) return;   // the bill is closed once the run is over
                audioFx.Play(Sfx.Bone);
                hud.Hurt(0.5f);
                int cost = BoneCost(limb);
                medical += cost;
                hud.Flash("CRACK!", limb + " broken  ·  +$" + cost.ToString("N0"), 1.6f);
                hurtShake = 1f;
            };
            rider.HitObstacle += (ob, at, into) =>
            {
                if (ob.label == "Tree" && fx != null) fx.TreeShake(at, into);   // the branches dump their snow
            };
            body.RagdollHit += (v, isBoard) =>
            {
                if (!isBoard && v > 6f) hurtShake = Mathf.Max(hurtShake, Mathf.Clamp01((v - 6f) / 10f) * 0.8f);
                if (v < 1.5f || Time.time < nextImpactSound) return;
                if (!isBoard && v > 5f && state == State.Riding) medical += (v - 5f) * 45f;   // bruises, by how hard you hit
                // mid-tumble: a fresh scream on each big hit, above all against trees, rocks and signs
                if (audioFx.Screaming && rider.Crashed && !isBoard && (v > 14f || (v > 7f && body.RagdollHitObject)))
                    audioFx.ScreamHit();
                nextImpactSound = Time.time + 0.07f;
                audioFx.Play(isBoard ? Sfx.Clack : Sfx.Thump, Mathf.Clamp01(v / 10f));
            };

            var tg = new GameObject("Terrain");
            terrain = tg.AddComponent<SlopeStreamer>();
            terrain.snowMaterial = snow;
            terrain.target = rg.transform;
            SnowLayer.Create(snow);   // the snow the thrown body ploughs into (see RiderBody.PhysicsStep)

            var pg = new GameObject("Trees");
            props = pg.AddComponent<Forest>();
            props.foliage = foliage;
            props.bark = bark;
            props.target = rg.transform;

            power = new GameObject("PowerUps").AddComponent<PowerUps>();
            power.rider = rider;
            power.Build(snow.shader, trail, spray);
            power.Collected += OnPickup;
            rider.ShieldHit += () =>
            {
                if (state != State.Riding) return;
                hud.Flash("SHIELD SAVED YOU", rider.Shields + " left", 1.1f);
                audioFx.Play(Sfx.ShieldHit);
            };
            BuildStartGate();
            dressing = new GameObject("Course").AddComponent<CourseDressing>();
            dressing.target = rg.transform;
            dressing.Build(snow.shader, snow);
            wild = new GameObject("Wildlife").AddComponent<Wildlife>();
            wild.rider = rider;
            wild.Build(snow.shader);
            wild.BearSpotted += () => { if (state == State.Riding) hud.Flash("BEAR!", "outrun it - keep going straight", 1.6f); };
            rider.GrindStart += f => { if (state == State.Riding) audioFx.Play(f.kind == Course.Kind.Rail ? Sfx.GrindRail : Sfx.GrindBox); };
            // a numbered flag wherever a fall came to rest, for the rest of the run
            flags = new GameObject("FallFlags").AddComponent<FallFlags>();
            flags.Build(jacket != null ? jacket.shader : null);
            body.FallRested += at => { if (state == State.Riding) flags.Plant(at, falls); };
            body.Killed += fell =>
            {
                // off a cliff and into the snow far below: it's over (see RiderBodyFall)
                if (state != State.Riding) return;
                bool huge = fell > 80f;
                int bill = huge ? 95000 : 68000;
                rider.StayDown = true; wipeoutTimer = 1.6f; endTitle = huge ? "SENT IT" : "CLIFFED"; medical += bill;
                hud.Flash(endTitle, "helicopter recovery  +$" + bill.ToString("N0"), 2.4f);
                audioFx.Play(Sfx.Bone); audioFx.Play(Sfx.Crash);
            };

            var cam = Camera.main;
            if (cam == null)
            {
                var cg = new GameObject("Main Camera");
                cg.tag = "MainCamera";
                cam = cg.AddComponent<Camera>();
                cg.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 2300f;
            chase = cam.gameObject.GetComponent<ChaseCamera>();
            if (chase == null) chase = cam.gameObject.AddComponent<ChaseCamera>();
            chase.rider = rider;
            chase.floor = slide.Depth;   // the camera rides up over the avalanche's debris
            camLevel = Mathf.Clamp(PlayerPrefs.GetInt("summit_camdist", 2), 0, CamDist.Length - 1);
            chase.distScale = CamDist[camLevel];
            angleLevel = Mathf.Clamp(PlayerPrefs.GetInt("summit_angle", 1), 0, AngleHeight.Length - 1);
            lensLevel = Mathf.Clamp(PlayerPrefs.GetInt("summit_lens", 1), 0, LensFov.Length - 1);
            lensMode = Mathf.Clamp(PlayerPrefs.GetInt("summit_lenstype", 0), 0, LensFX.ModeName.Length - 1);
            filterLevel = Mathf.Clamp(PlayerPrefs.GetInt("summit_filter", 0), 0, LookGrade.Names.Length - 1);
            filterStrength = Mathf.Clamp(PlayerPrefs.GetInt("summit_filterstrength", 10), 0, 10);
            halation = Mathf.Clamp(PlayerPrefs.GetInt("summit_halation", 0), 0, LensFX.HalationName.Length - 1);

            fx = new GameObject("SnowFX").AddComponent<SnowFX>();
            fx.Build(cam, spray);
            body.fx = fx;
            // blood on the hardest crashes (can be switched off in the pause menu: it raises the age rating)
            blood = new GameObject("Blood").AddComponent<Blood>();
            blood.Build(spray);
            blood.SetOn(PlayerPrefs.GetInt("summit_blood", 1) == 1);
            body.blood = blood;
            blood.forest = props;

            // invincible: what you run through is smashed, not crashed into
            smasher = new GameObject("Smasher").AddComponent<Smasher>();
            smasher.Build(fx, props, wild, spray);
            aura = rg.AddComponent<PowerAura>();
            aura.Build(rider, spray);
            rider.Smashed += (ob, at, v) =>
            {
                if (state != State.Riding) return;
                smasher.Hit(ob, at, v);
                audioFx.Play(ob.label == "Rock" ? Sfx.SmashRock : Sfx.SmashWood);
                if (ob.always) audioFx.Play(Sfx.BearRoar);
                hurtShake = Mathf.Max(hurtShake, 0.35f);
                OnTrick(ob.always ? "BEAR BOWLED" : ob.label == "Rock" ? "ROCK SMASH" : "TIMBER!", ob.always ? 500 : 100);
            };
            // signs and flags: ridden into (or tumbled into), signs break and flags go down (Breakables)
            breaks = new GameObject("Breakables").AddComponent<Breakables>();
            breaks.Build(rider, fx, smasher);
            breaks.Broke += (b, crashed, invincible, thrown) =>
            {
                if (state != State.Riding) return;
                bool sign = b.kind == Breakable.Kind.Sign;
                if (sign) audioFx.Play(Sfx.SmashWood, crashed || invincible ? 1f : 0.8f);
                else audioFx.Play(Sfx.Clack, 0.7f);
                if (!sign || thrown) return;
                hurtShake = Mathf.Max(hurtShake, crashed ? 0.8f : 0.45f);
                if (invincible) OnTrick("SIGN SMASH", 100);
            };

            atmo = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atmo.Build(cam, rider, spray);

            slideView = new GameObject("Avalanche").AddComponent<AvalancheView>();
            slideView.Build(cam, rider, body, slide, snow, spray);

            var hg = new GameObject("HUD");
            hud = hg.AddComponent<Hud>();
            hud.Build();

            var ag = new GameObject("Audio");
            audioFx = ag.AddComponent<SoundMix>();
            audioFx.Build();
            audioFx.Track(wild);

            var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            post = vol != null ? vol.profile : null;
            lensFx = cam.gameObject.AddComponent<LensFX>();
            lensFx.Build(cam);
            ApplyCamera();
            ApplyLook();
            bool tube = PlayerPrefs.GetInt("summit_tube", 0) == 1;
            rider.Tube = tube; body.SetTube(tube); hud.SetRider(tube);
            rider.Bounced += into => { if (state != State.Riding) return; body.TubeKick(into); audioFx.Play(Sfx.Thump, Mathf.Clamp01(into / 12f)); hud.Flash("BOING", "", 0.6f); };
            BuildMeta();
            dayNight.Hook(rider, fx, camp, spray, trail);
            ToHub();
            // device testing: launched with SUMMIT_TREEBENCH=1 (and SUMMIT_AUTOPLAY=1) it times both tree looks
            if (System.Environment.GetEnvironmentVariable("SUMMIT_TREEBENCH") == "1") gameObject.AddComponent<TreeBench>().forest = props;
        }

        // ---------------------------------------------------------------- the meta game: camp, points, missions

        void BuildMeta()
        {
            meta = new GameObject("MetaUi").AddComponent<MetaUi>();
            meta.Build();
            meta.Clicked = () => audioFx.Play(Sfx.Click);
            meta.Bought = () => audioFx.Play(Sfx.ShieldGet, 2);
            meta.Denied = () => audioFx.Play(Sfx.Scrape, 0.4f);
            camp = new GameObject("SummitCamp").AddComponent<Camp>();
            camp.soft = spray;
            camp.Build(snow.shader);
            Missions.Track(rider, power);
            Missions.Completed += m => { meta.Toast(m); audioFx.Play(Sfx.ShieldFull); };
            Progress.Changed += ApplyOutfit;
            ApplyOutfit();
        }

        void ApplyOutfit() => body.SetColours(Progress.Jackets[Progress.Jacket], Progress.Boards[Progress.Board]);

        /// <summary>To the camp: on foot, with the menus. A fresh mountain unless you just came up from the start pad.</summary>
        void ToHub(bool fresh = true)
        {
            if (fresh) ToReady();
            state = State.Hub;
            hud.Clear(); hud.SetButtons(false, false, "PLAY"); hud.SetPrompt("");
            chase.enabled = false;
            terrain.behindChunks = 4; props.behindCells = 16;   // you can turn round and look up the hill here
            camp.Enter(rider, body, chase.GetComponent<Camera>());
            if (slideView != null) slideView.gameObject.SetActive(false);   // its toe waits just uphill of the camp: not drawn here
            lensFx.Strength = 0.3f;   // the camp's gentle lens at once (it's a cut); dropping in eases back to yours
            terrain.Prewarm(); props.Prewarm();
            meta.SetMode(MetaUi.Mode.Hub);
        }

        // ---------------------------------------------------------------- world

        void SetUpWorld()
        {
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            // clear daytime: a high, near-white sun from the side, crisp shadows, bright blue sky light
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 1.45f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sun.transform.rotation = Quaternion.Euler(50f, 50f, 0f);   // behind and to the left of the rider: faces you see are lit
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.60f, 0.70f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.66f, 0.72f, 0.82f);
            RenderSettings.ambientGroundColor = new Color(0.50f, 0.54f, 0.62f);
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_SkyTint"))
            {
                sky.SetColor("_SkyTint", new Color(0.42f, 0.56f, 0.82f));
                sky.SetFloat("_AtmosphereThickness", 0.8f);
                sky.SetFloat("_SunSize", 0.04f);
                sky.SetFloat("_Exposure", 1.25f);
            }
            var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            if (vol != null && vol.profile != null)
            {
                if (vol.profile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments ca)) { ca.contrast.Override(14f); ca.saturation.Override(10f); ca.postExposure.Override(0.1f); }
                if (vol.profile.TryGet(out UnityEngine.Rendering.Universal.Bloom bl)) { bl.intensity.Override(0.6f); bl.threshold.Override(1.0f); }
                if (vol.profile.TryGet(out UnityEngine.Rendering.Universal.Vignette vg)) vg.intensity.Override(0.2f);
                // a wide-angle lens: barrel distortion, set from the pause menu
                if (!vol.profile.TryGet(out lens)) lens = vol.profile.Add<UnityEngine.Rendering.Universal.LensDistortion>(true);
                lens.active = true;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0021f;   // just a touch of haze: the mountain below stays in view
            RenderSettings.fogColor = new Color(0.80f, 0.86f, 0.94f);
            dayNight = DayNight.Build(sun);   // morning, day, evening or night: relights all of the above
        }

        void MakeMissingMaterials()
        {
            if (snow == null) snow = Plain(new Color(0.86f, 0.89f, 0.95f), 0.25f);
            if (foliage == null) foliage = Plain(new Color(0.12f, 0.25f, 0.21f), 0.05f);
            if (bark == null) bark = Plain(new Color(0.30f, 0.22f, 0.16f), 0.05f);
            if (jacket == null) jacket = Plain(new Color(0.95f, 0.36f, 0.14f), 0.3f);
            if (pants == null) pants = Plain(new Color(0.13f, 0.15f, 0.22f), 0.2f);
            if (board == null) board = Plain(new Color(0.10f, 0.45f, 0.90f), 0.6f);
            if (skin == null) skin = Plain(new Color(0.88f, 0.70f, 0.58f), 0.3f);
            if (dark == null) dark = Plain(new Color(0.05f, 0.05f, 0.07f), 0.9f);
            if (trail == null) trail = Particle();
            else trail = new Material(trail);   // a copy: the time of day tints it, and the asset itself must stay as made
            if (spray == null) spray = Particle();
        }

        static Material Plain(Color c, float smooth)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Glossiness", smooth);
            m.enableInstancing = true;
            return m;
        }

        static Material Particle()
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            m.SetColor("_BaseColor", Color.white);
            return m;
        }

        // ---------------------------------------------------------------- loop

        void ToReady() => ToReady(true);

        void ToReady(bool newMountain)
        {
            state = State.Ready;
            ledgeCued = false; ledgeFading = false; if (audioFx != null) { audioFx.StopCue(); audioFx.StopScreaming(true); }
            rider.input = input; endFade = 1f; if (hud != null) hud.SetAlpha(1f); if (meta != null) meta.SetEndAlpha(1f);   // hands back on, screen solid
            camp.Leave(); chase.enabled = true; terrain.behindChunks = 1; props.behindCells = 3;
            if (newMountain)
            {
                // a new mountain every run: new pitches, drops, kickers, crevasses and trees
                Slope.Init(Slope.RandomSeed());
                rider.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ);   // build the new mountain round the start, once
                terrain.Rebuild();
                props.Rebuild();
                dressing.Rebuild();
                RebuildGate();
                camp.Rebuild();
            }
            float x = Slope.Centre(Slope.PadLip);
            rider.ResetAt(x, Slope.StartZ);
            body.ResetVisual();
            slide.Reset(Slope.StartZ);
            if (slideView != null) { slideView.gameObject.SetActive(true); slideView.Clear(); }
            Missions.RunEnd();   // a run abandoned from the pause menu still saves its mission progress
            chase.hold = 0f;
            trickPoints = 0; chainCount = 0; chainTimer = 0f; runTime = 0f;
            power.ResetRun();
            wild.ResetRun();
            smasher.Clear(); aura.Clear();
            Breakable.RestoreAll();   // every sign and flag standing again
            terrain.Prewarm();
            props.Prewarm();
            dressing.Prewarm();
            chase.Snap();
            hud.SetWhiteout(0f);
            falls = 0; wipeoutTimer = 0f; hurtShake = 0f; crevasseWatch = false; medical = 0f;
            flags.Clear();
            hud.SetPrompt("");
            if (paused) SetPaused(false);
            chase.peek = 0f;
            if (atmo != null) atmo.ResetParallax();
            if (fx != null) fx.Clear();
            if (blood != null) blood.Clear();
            hud.Show("", "Best " + Mathf.RoundToInt(best).ToString("N0"),
                Application.isMobilePlatform ? "Drag a finger left / right to steer  ·  add a second finger to tuck  ·  lift it to jump"
                              : "A / D to steer  ·  hold Space to tuck  ·  let go to jump");
            hud.SetButtons(true, false, "PLAY");
            hud.SetMode(input.useTilt);
            meta.SetMode(MetaUi.Mode.Ready);
        }

        void ToRiding()
        {
            state = State.Riding;
            input.Calibrate();
            input.SwallowCurrentPress();
            Progress.ApplyRider();   // upgrades bought in camp
            Missions.RunStart();
            meta.SetMode(MetaUi.Mode.Riding);
            rider.Begin();
            hud.Clear();
            hud.SetButtons(false, true, "");
            hud.Flash("GO", "drop in", 1.2f);
        }

        void ToBuried() => ToEnd("BURIED", true);

        float endFade = 1f;
        void ToEnd(string title, bool buried, bool rollOn = false)
        {
            if (rider.Crashed) flags.Plant(body.RagdollCentre, falls);   // the fall that ended it gets its flag too
            audioFx.StopCue();
            state = State.Buried;
            hud.SetPrompt("");
            wipeoutTimer = 0f;
            endBuried = buried;
            if (buried) audioFx.Play(Sfx.Buried);   // the world goes muffled under the snow (see the mix below)
            stateTimer = 0f;
            if (rollOn) { rider.input = null; rider.Steer = 0f; }   // hands off: the board runs on out through the finish and slows on the flat
            else rider.Stop();
            endFade = 0f; hud.SetAlpha(0f); meta.SetEndAlpha(0f);   // the end screen fades in
            int s = Score;
            bool record = s > best;
            if (record) { best = s; PlayerPrefs.SetInt("summit_best", s); PlayerPrefs.Save(); }
            hud.Show(title, (title == "YOU SURVIVED" ? "MT. VERTIGO  ·  " : "") + "Score " + s.ToString("N0"),
                     (record ? "NEW BEST  " : "Best ") + Mathf.RoundToInt(best).ToString("N0") +
                     "   ·   " + Mathf.RoundToInt(rider.Distance) + " m   ·   " + Hud.FormatTime(runTime) +
                     (medical > 0f ? "\nMEDICAL BILL  $" + Mathf.RoundToInt(medical).ToString("N0") : "\nNo medical bills. Clean run!"));
            hud.SetButtons(true, false, "TO CAMP");
            // points for the run (mission rewards were paid as they happened); the score mission sees the final
            // score, finish bonus and all
            Missions.Step(0f, 999f, s);
            Missions.RunEnd();
            var reward = Progress.EndRun(s, rider.Distance, Missions.RunTricks, title == "YOU SURVIVED", Missions.RunPoints);
            meta.ShowEnd(reward, Mathf.RoundToInt(rider.Distance), title == "YOU SURVIVED");
            meta.SetMode(MetaUi.Mode.End);
        }

        const bool showDiag = false;
        float diagT, diagSum, diagMax; int diagFrames, gc0;
        void Diagnostics()
        {
            float r = Time.unscaledDeltaTime;
            diagFrames++; diagSum += r; if (r > diagMax) diagMax = r; diagT += r;
            if (r > 0.034f && (showDiag || autoplay))   // logging every hitch on the phone makes the hitch worse
                Debug.Log("SUMMIT HITCH " + (r * 1000f).ToString("0") + " ms  state=" + state + "  air=" + rider.Airborne +
                          "  speed=" + rider.Speed.ToString("0.0") + "  gc=" + System.GC.CollectionCount(0));
            if (diagT >= 0.5f)
            {
                if (showDiag) hud.SetDiag((diagFrames / diagSum).ToString("0") + " fps  worst " + (diagMax * 1000f).ToString("0") +
                            " ms  gc " + (System.GC.CollectionCount(0) - gc0));
                diagT = diagSum = diagMax = 0f; diagFrames = 0;
            }
        }

        void Update()
        {
            Diagnostics();
            float dt = Time.deltaTime;
            stateTimer += dt;
            WatchScream(dt);
            // a gentler lens in the camp (its camera is close, among straight lines), the player's own on the slope
            lensFx.Strength = Mathf.MoveTowards(lensFx.Strength, state == State.Hub ? 0.3f : 1f, Time.unscaledDeltaTime * 1.4f);
            switch (state)
            {
                case State.Hub:
                    if (autoplay)
                    {
                        // device testing: a look at the camp, then on to the run
                        float before = readyT; readyT += Time.unscaledDeltaTime;
                        if (before < 2.5f && readyT >= 2.5f) ScreenCapture.CaptureScreenshot("auto-hub.png");
                        if (readyT >= 3.5f) { readyT = 0f; ToReady(false); }
                    }
                    else if (camp.Step(dt, meta)) { audioFx.Play(Sfx.Play); ToReady(false); input.SwallowCurrentPress(); }
                    break;

                case State.Ready:
                    traceT = 0f;
                    if (autoplay && (readyT += dt) > 3f) { readyT = 0f; ToRiding(); }
                    else if (autoplay && readyT > 2f && readyT - dt <= 2f) ScreenCapture.CaptureScreenshot("auto-ready.png");
                    else if (input.AnyPress() && ModeTapped()) { }
                    else if (input.AnyPress() && RiderTapped()) { }
                    else if (input.AnyPress() && meta.Hit(RiderInput.PointerPosition()) == "camp") { audioFx.Play(Sfx.Click); ToHub(false); }
                    else if (input.AnyPress() && (Tapped(true) || SpacePressed())) { audioFx.Play(Sfx.Play); ToRiding(); }
                    break;

                case State.Riding:
                    if (paused)
                    {
                        if (input.AnyPress())
                        {
                            var pp = RiderInput.PointerPosition();
                            if (hud.OnResume(pp)) { audioFx.Play(Sfx.Click); SetPaused(false); }
                            else if (hud.OnPauseTab(pp)) audioFx.Play(Sfx.Click);
                            else if (hud.OnHalationMinus(pp) || hud.OnHalationPlus(pp)) { audioFx.Play(Sfx.Click); halation = Mathf.Clamp(halation + (hud.OnHalationPlus(pp) ? 1 : -1), 0, LensFX.HalationName.Length - 1); ApplyLook(); }
                            else if (hud.OnMusicMinus(pp) || hud.OnMusicPlus(pp)) { audioFx.Play(Sfx.Click); StepMusic(hud.OnMusicPlus(pp) ? 1 : -1); }
                            else if (hud.OnCamMinus(pp)) { audioFx.Play(Sfx.Click); SetCamLevel(camLevel - 1); }
                            else if (hud.OnCamPlus(pp)) { audioFx.Play(Sfx.Click); SetCamLevel(camLevel + 1); }
                            else if (hud.OnAngMinus(pp)) { audioFx.Play(Sfx.Click); angleLevel = Mathf.Max(0, angleLevel - 1); ApplyCamera(); }
                            else if (hud.OnAngPlus(pp)) { audioFx.Play(Sfx.Click); angleLevel = Mathf.Min(AngleHeight.Length - 1, angleLevel + 1); ApplyCamera(); }
                            else if (hud.OnLensMinus(pp)) { audioFx.Play(Sfx.Click); lensLevel = Mathf.Max(0, lensLevel - 1); ApplyCamera(); }
                            else if (hud.OnLensPlus(pp)) { audioFx.Play(Sfx.Click); lensLevel = Mathf.Min(LensFov.Length - 1, lensLevel + 1); ApplyCamera(); }
                            else if (hud.OnLensTypeMinus(pp) || hud.OnLensTypePlus(pp)) { audioFx.Play(Sfx.Click); lensMode = (lensMode + 1) % LensFX.ModeName.Length; ApplyCamera(); }
                            else if (hud.OnFilterMinus(pp)) { audioFx.Play(Sfx.Click); filterLevel = (filterLevel + LookGrade.Names.Length - 1) % LookGrade.Names.Length; ApplyLook(); }
                            else if (hud.OnFilterPlus(pp)) { audioFx.Play(Sfx.Click); filterLevel = (filterLevel + 1) % LookGrade.Names.Length; ApplyLook(); }
                            else if (hud.OnStrengthMinus(pp)) { audioFx.Play(Sfx.Click); filterStrength = Mathf.Max(0, filterStrength - 1); ApplyLook(); }
                            else if (hud.OnStrengthPlus(pp)) { audioFx.Play(Sfx.Click); filterStrength = Mathf.Min(10, filterStrength + 1); ApplyLook(); }
                            else if (hud.OnTimeMinus(pp) || hud.OnTimePlus(pp)) { audioFx.Play(Sfx.Click); TimeOfDay.StepLock(hud.OnTimePlus(pp) ? 1 : -1); hud.SetTime(TimeOfDay.LockLabel); }
                            else if (hud.OnPauseSteer(pp)) { audioFx.Play(Sfx.Click); input.SetTilt(!input.useTilt); hud.SetPauseSteer(input.useTilt); hud.SetMode(input.useTilt); }
                            else if (hud.OnPauseTrees(pp)) { audioFx.Play(Sfx.Click); props.SetRealistic(!props.IsRealistic); hud.SetPauseTrees(props.IsRealistic); }
                            else if (hud.OnPauseBlood(pp)) { audioFx.Play(Sfx.Click); blood.SetOn(!blood.On); hud.SetBlood(blood.On); PlayerPrefs.SetInt("summit_blood", blood.On ? 1 : 0); PlayerPrefs.Save(); }
                            else if (hud.OnPauseRestart(pp)) { audioFx.Play(Sfx.Click); SetPaused(false); ToReady(); input.SwallowCurrentPress(); }
                        }
                        break;
                    }
                    if (input.AnyPress() && hud.OnRestart(RiderInput.PointerPosition())) { audioFx.Play(Sfx.Click); SetPaused(true); break; }
                    // down in the snow: the camera stays on the body until you tap to get back up
                    if (rider.CanGetUp && input.AnyPress()) { rider.RequestUp = true; input.SwallowCurrentPress(); audioFx.Play(Sfx.Groan); }
                    else if (rider.StayDown && rider.Crashed && wipeoutTimer > 0f && input.AnyPress() && !body.RagdollMoving) wipeoutTimer = 0.01f;
                    hud.SetPrompt(rider.CanGetUp ? "TAP TO GET UP" : rider.StayDown && rider.Crashed && !body.RagdollMoving && !slide.Caught ? "TAP TO CONTINUE" : "");
                    runTime += dt;
                    slide.Step(dt, rider, body);
                    if (slide.JustReleased) { audioFx.Play(Sfx.SlideCrack); hud.Flash("AVALANCHE!", "the slope has let go behind you", 2f); }
                    // the first ledge: the suspense sting as you roll up to the lip, eased out once you've landed below it
                    if (!ledgeCued && !rider.Crashed && rider.Z > Slope.PadLip - 14f && rider.Z < Slope.PadLip + 1f) { ledgeCued = true; audioFx.Play(Sfx.Ledge); }
                    if (ledgeCued && (ledgeFading || rider.Crashed || (rider.Z > Slope.PadLip + 8f && !rider.Airborne))) { ledgeFading = true; audioFx.FadeCue(dt); }
                    Missions.Step(dt, slide.Gap(rider), Score);
                    if (crevasseWatch)
                    {
                        if (body.RagdollBelowSnow > 3f) { crevasseWatch = false; rider.StayDown = true; wipeoutTimer = 1.0f; endTitle = "LOST IN A CREVASSE"; medical += 42000f; hud.Flash("GONE", "helicopter rescue  +$42,000", 2f); }
                        else if (!rider.Crashed) crevasseWatch = false;   // caught the lip: you live
                    }
                    if (!rider.Crashed && !rider.Airborne && !slide.Caught && rider.Z > Slope.FinishZ + 30f)
                    {
                        // through the finish at the base: the whole mountain, done
                        trickPoints += 5000;
                        Missions.Finished();
                        audioFx.Play(Sfx.TrickBig, 4);
                        ToEnd("YOU SURVIVED", false, true);   // carry on through the arch while the screen fades in
                        break;
                    }
                    if (wipeoutTimer > 0f && !slide.Caught) { if (!body.RagdollMoving) wipeoutTimer -= dt; if (wipeoutTimer <= 0f) { ToEnd(endTitle, false); break; } }
                    if (chainCount > 0)
                    {
                        chainTimer -= dt;
                        if (chainTimer <= 0f) chainCount = 0;
                    }
                    if (slide.Done) ToBuried();   // caught, carried off and buried, and the camera has held on the pile
                    break;

                case State.Buried:
                    if (endFade < 1f) { endFade = Mathf.MoveTowards(endFade, 1f, Time.unscaledDeltaTime / 1.4f); hud.SetAlpha(endFade); meta.SetEndAlpha(endFade); }
                    if (stateTimer > 1.2f && input.AnyPress() && ModeTapped()) { }

                    else if (stateTimer > 1.2f && input.AnyPress() && meta.Hit(RiderInput.PointerPosition()) == "ride")
                    {
                        audioFx.Play(Sfx.Click);
                        ToReady();
                        input.SwallowCurrentPress();
                    }
                    else if (stateTimer > 1.2f && input.AnyPress() && (Tapped(true) || SpacePressed()))
                    {
                        ToHub();
                        input.SwallowCurrentPress();
                    }
                    break;
            }

            if (autoplay && state == State.Riding && traceT < 16f)
            {
                // device testing: launched with SUMMIT_AUTOPLAY=1 the game starts runs by itself and logs the first seconds
                float before = traceT, udt = Time.unscaledDeltaTime;   // real time: it keeps counting while paused
                traceT += udt; traceNext -= udt;
                // a look at the pause menu as it is on the phone
                if (before < 4.5f && traceT >= 4.5f) SetPaused(true);
                if (before < 5.0f && traceT >= 5.0f) ScreenCapture.CaptureScreenshot("auto-pause.png");
                if (before < 5.5f && traceT >= 5.5f) SetPaused(false);
                foreach (float at in new[] { 3f, 7.6f, 8.4f, 10f, 14f })
                    if (before < at && traceT >= at) ScreenCapture.CaptureScreenshot("auto-" + at.ToString("0.0") + ".png");
                if (traceNext <= 0f)
                {
                    traceNext = 0.25f;
                    var rp = rider.Body.position; var tp = rider.transform.position;
                    Debug.Log($"SUMMIT TRACE t={traceT:0.00} body={rp} tf={tp} ground={Slope.DrawnHeight(rp.x, rp.z):0.00} v={rider.Body.linearVelocity} " +
                              $"air={rider.Airborne} crash={rider.Crashed} kin={rider.Body.isKinematic} run={rider.Running} cam={chase.transform.position} fps={1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):0}");
                }
            }

            // no stick graphic: the thumb steers the rider directly and the rider's lean shows the steering
            hud.SetStick(false, input.StickOrigin, input.StickPos, 1f);

            float gap = state == State.Ready || state == State.Hub ? 120f : !slide.Released ? 9999f : Mathf.Max(0f, slide.Gap(rider));   // 9999: still holding, no slide to show
            float close = Mathf.Clamp01(1f - gap / 45f);
            hud.SetRun(rider.Speed * 3.6f, Score, Mult, gap, 160f, state == State.Riding);
            hud.SetPower(runTime, rider.Shields, state == State.Riding);
            hud.SetTimers(state == State.Riding, rider.BoostTime, ZoomSeconds, rider.SuperJumpTime, SuperJumpSeconds, rider.InvincibleTime, InvincibleSeconds);
            hud.SetAltitude(Slope.AltitudeFeet(rider.Z), state == State.Riding);
            hud.SetMedical(medical, state == State.Riding);
            // the slide: white powder in the air when the camera is in it, and a softer white over the pile at the end
            hud.SetWhiteout(state == State.Buried ? Mathf.Min(endBuried ? 0.55f : 0.85f, stateTimer * (endBuried ? 0.7f : 2f))
                            : Mathf.Max(slideView.Whiteout, close * close * 0.12f));
            chase.hold = Mathf.MoveTowards(chase.hold, state != State.Ready && slide.Caught && slide.Cover > 0.2f ? 1f : 0f, dt * (state == State.Ready ? 10f : 0.55f));
            chase.holdAt = slide.BuryAt;
            hurtShake = Mathf.MoveTowards(hurtShake, 0f, dt * 2.5f);
            // fell into a crevasse: the camera leans over the edge and looks down after the body
            bool deep = rider.Crashed && body.RagdollBelowSnow > 0.8f;
            chase.peek = Mathf.MoveTowards(chase.peek, deep ? 1f : 0f, Time.unscaledDeltaTime * (deep ? 1.2f : 3f));
            chase.peekAt = body.RagdollCentre;
            chase.shake = (state == State.Riding ? slide.Shake + (rider.Crashed && !slide.Caught ? 0.2f : 0f) : 0f) + hurtShake * hurtShake * 0.8f;
            hud.SetFalls(Progress.Falls(MaxFalls) - falls, Progress.Falls(MaxFalls), state == State.Riding);
            bool onSnow = state == State.Riding && !rider.Airborne && !rider.Crashed;
            bool riding = state == State.Riding;
            Vector3 rpos = rider.transform.position;
            audioFx.Set(new RideSound
            {
                speed = rider.Speed / 36f, carve = rider.Tube ? 0f : rider.Carve, skid = rider.Tube ? 0.15f * Mathf.Clamp01(rider.Speed / 20f) : rider.Skid, rough = Slope.Rough(rpos.x, rpos.z),
                avalanche = riding ? slide.Closeness(rider) : 0f, pan = rider.Steer,
                muffle = state == State.Buried ? (endBuried ? 1f : 0.45f) : rider.Crashed ? 0.3f : 0f,
                onSnow = onSnow && !rider.Grinding, air = riding && rider.Airborne, riding = riding,
                camp = state == State.Hub, ended = state == State.Buried,
                boost = riding && rider.BoostTime > 0f,
                grind = riding && rider.Grinding ? (rider.GrindFeature.kind == Course.Kind.Rail ? 1 : 2) : 0,
            });
            AirSounds(riding);
        }

        bool ModeTapped()
        {
            if (!hud.OnMode(RiderInput.PointerPosition())) return false;
            audioFx.Play(Sfx.Click);
            input.SetTilt(!input.useTilt);
            hud.SetMode(input.useTilt);
            return true;
        }

        bool Tapped(bool play) => hud.OnPlay(RiderInput.PointerPosition());
        static bool SpacePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }

        void OnRough(string why)
        {
            if (state != State.Riding) return;
            hud.Flash(why, "speed lost", 1.0f);
        }

        void OnTrick(string name, int pts)
        {
            if (state != State.Riding) return;
            int got = Mathf.RoundToInt(pts * Mult);
            trickPoints += got;
            chainCount = Mathf.Min(chainCount + 1, 8);
            chainTimer = 4f;
            hud.Flash(name, "+" + got);
            if (name == "Close call") audioFx.Play(Sfx.CloseCall);
            else audioFx.Play(pts >= 700 ? Sfx.TrickBig : Sfx.Trick, chainCount - 1);
        }

        void OnPickup(PowerUps.Kind kind)
        {
            if (state != State.Riding) return;
            if (kind == PowerUps.Kind.Soda)
            {
                rider.AddBoost(ZoomSeconds * Progress.PowerUpTime);
                hud.Flash("MOUNTAIN ZOOM", "speed boost", 0.9f);
                audioFx.Play(Sfx.Soda);
            }
            else if (kind == PowerUps.Kind.SuperJump)
            {
                rider.AddSuperJump(SuperJumpSeconds * Progress.PowerUpTime);
                hud.Flash("SUPER JUMP", "lift your thumb for big air", 1.2f);
                audioFx.Play(Sfx.PowerJump);
            }
            else if (kind == PowerUps.Kind.Invincible)
            {
                rider.AddInvincible(InvincibleSeconds * Progress.PowerUpTime);
                hud.Flash("INVINCIBLE", "smash through anything", 1.2f);
                audioFx.Play(Sfx.PowerStar);
            }
            else if (rider.Shields >= Rider.MaxShields)
            {
                trickPoints += 250;
                hud.Flash("SHIELDS FULL", "+250", 0.9f);
                audioFx.Play(Sfx.ShieldFull);
            }
            else
            {
                rider.AddShield();
                hud.Flash("SHIELD", rider.Shields + " / " + Rider.MaxShields, 0.9f);
                audioFx.Play(Sfx.ShieldGet, rider.Shields);
            }
        }

        // the start: a gate over the in-run and flags along the lip you jump from
        Transform gateRoot;
        // the start gate is rebuilt for every new mountain; its materials are made once and reused, so they neither
        // leak nor pile up in DayNight's list of reflective signs
        readonly System.Collections.Generic.Dictionary<string, Material> gateMats = new System.Collections.Generic.Dictionary<string, Material>();
        Material GateSign(string path)
        {
            if (!gateMats.TryGetValue(path, out var m)) { m = CourseDressing.Sign(snow.shader, path); gateMats[path] = m; }
            return m;
        }

        void RebuildGate()
        {
            if (gateRoot != null) { if (Application.isPlaying) Destroy(gateRoot.gameObject); else DestroyImmediate(gateRoot.gameObject); }
            BuildStartGate();
        }

        void BuildStartGate()
        {
            var sh = snow.shader;
            Material M(Color c)
            {
                string k = "c" + c;
                if (!gateMats.TryGetValue(k, out var m)) { m = new Material(sh); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.3f); gateMats[k] = m; }
                return m;
            }
            var red = M(new Color(0.85f, 0.12f, 0.10f)); var white = M(new Color(0.95f, 0.95f, 0.97f)); var wood = M(new Color(0.45f, 0.30f, 0.18f));
            var root = new GameObject("StartGate").transform;
            gateRoot = root;
            float cx = Slope.Centre(Slope.PadLip), gz = Slope.PadLip - 36f;   // well ahead of where you wait, so the whole banner is in view
            float gy = Slope.DrawnHeight(cx, gz);
            // the gate breaks as one: hit a post and it snaps, and the banner comes down with it
            var gate = Holder("Gate", root, new Vector3(cx, gy, gz));
            var gateBrk = Breakable.Sign(gate);
            for (int sgn = -1; sgn <= 1; sgn += 2)
                gateBrk.Post(Block(PrimitiveType.Cube, gate, new Vector3(cx + sgn * 4.2f, gy + 2.2f, gz), new Vector3(0.35f, 4.4f, 0.35f), red));
            // the banner: a printed vinyl strip in a frame, hung between the posts
            var frame = Block(PrimitiveType.Cube, gate, new Vector3(cx, gy + 4.3f, gz + 0.06f), new Vector3(8.6f, 1.2f, 0.08f), red);
            var banner = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(banner.GetComponent<Collider>());
            banner.transform.SetParent(gate, false);
            banner.transform.SetPositionAndRotation(new Vector3(cx, gy + 4.3f, gz - 0.01f), Quaternion.identity);   // a quad shows its face to -Z: up the hill, at the rider
            banner.transform.localScale = new Vector3(8.4f, 1.05f, 1f);
            banner.GetComponent<MeshRenderer>().sharedMaterial = GateSign("Signs/BannerSummit");
            gateBrk.Board(frame, banner.transform);
            gateBrk.Arm();
            // flags lining the lip and a wooden kicker edge
            for (int i = 0; i < 6; i++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float z = Slope.PadLip - 2f - i * 6f, x = cx + sgn * (Slope.PadHalf - 1f);
                    float y = Slope.DrawnHeight(x, z);
                    var flag = Holder("LipFlag", root, new Vector3(x, y, z));
                    Block(PrimitiveType.Cylinder, flag, new Vector3(x, y + 0.9f, z), new Vector3(0.06f, 0.9f, 0.06f), white);
                    Block(PrimitiveType.Cube, flag, new Vector3(x - sgn * 0.3f, y + 1.55f, z), new Vector3(0.6f, 0.4f, 0.03f), (i % 2 == 0) ? red : white);
                    Breakable.Flag(flag, 1.8f);
                }
            // the finish line at the base of the mountain: a blue arch across the run
            {
                float fz = Slope.FinishZ + 30f, fx = Slope.Centre(fz), fy = Slope.DrawnHeight(fx, fz);
                var blue = M(new Color(0.08f, 0.30f, 0.70f));
                var arch = Holder("FinishArch", root, new Vector3(fx, fy, fz));
                var archBrk = Breakable.Sign(arch);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    archBrk.Post(Block(PrimitiveType.Cube, arch, new Vector3(fx + sgn * 9f, fy + 2.6f, fz), new Vector3(0.45f, 5.2f, 0.45f), blue));
                var archFrame = Block(PrimitiveType.Cube, arch, new Vector3(fx, fy + 5.0f, fz + 0.06f), new Vector3(18.6f, 1.6f, 0.08f), blue);
                var fb = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(fb.GetComponent<Collider>());
                fb.transform.SetParent(arch, false);
                fb.transform.SetPositionAndRotation(new Vector3(fx, fy + 5.0f, fz - 0.01f), Quaternion.identity);
                fb.transform.localScale = new Vector3(18.2f, 1.4f, 1f);
                fb.GetComponent<MeshRenderer>().sharedMaterial = GateSign("Signs/BannerFinish");
                archBrk.Board(archFrame, fb.transform);
                archBrk.Arm();
            }
            // the last chance: safety signs either side of the in-run before the lip, and caution tape strung
            // chest-high between the last pair and on across the mountain, tree to tree (ride through it and it tears)
            var post = M(new Color(0.55f, 0.57f, 0.6f));
            WarningSign(root, cx, Slope.PadLip - 22f, -1f, "Signs/SignTurnAround", post);
            WarningSign(root, cx, Slope.PadLip - 22f, 1f, "Signs/SignRemote", post);
            var wl = WarningSign(root, cx, Slope.PadLip - 11f, -1f, "Signs/SignWarning", post);
            var wr = WarningSign(root, cx, Slope.PadLip - 11f, 1f, "Signs/SignDangerStart", post);
            StartTape.Build(root, wl, wr, SignPostX, rider, GateSign("Signs/Tape"), () => audioFx.Play(Sfx.Clack));
            float ly = Slope.DrawnHeight(cx, Slope.PadLip - 0.3f);
            Block(PrimitiveType.Cube, root, new Vector3(cx, ly - 0.15f, Slope.PadLip - 0.2f), new Vector3(2f * Slope.PadHalf - 4f, 0.35f, 0.3f), wood);
        }

        const float SignW = 3.6f, SignPostX = SignW * 0.4f;

        /// <summary>A 3:2 safety sign on two posts beside the in-run (side -1 / 1), turned in toward the rider coming down.</summary>
        Transform WarningSign(Transform root, float cx, float z, float side, string face, Material post)
        {
            const float W = SignW, H = W / 1.5f, lift = 1.3f;
            float x = cx + side * 6.5f;
            var t = new GameObject("WarningSign").transform;
            t.SetParent(root, false);
            t.SetPositionAndRotation(new Vector3(x, Slope.DrawnHeight(x, z), z), Quaternion.Euler(0f, side * 28f, 0f));
            Transform Local(PrimitiveType type, Vector3 at, Vector3 scale, Material m)
            {
                var go = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(t, false);
                go.transform.localPosition = at; go.transform.localScale = scale;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                if (type == PrimitiveType.Quad) go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go.transform;
            }
            var brk = Breakable.Sign(t);
            // posts run a little into the snow, so they stand on the bank whatever its tilt
            float top = lift + H;
            for (int s = -1; s <= 1; s += 2)
                brk.Post(Local(PrimitiveType.Cylinder, new Vector3(s * SignPostX, top * 0.5f - 0.3f, 0.08f), new Vector3(0.1f, (top + 0.6f) * 0.5f, 0.1f), post));
            var back = Local(PrimitiveType.Cube, new Vector3(0f, lift + H * 0.5f, 0.03f), new Vector3(W + 0.1f, H + 0.1f, 0.04f), post);
            brk.Board(back, Local(PrimitiveType.Quad, new Vector3(0f, lift + H * 0.5f, -0.001f), new Vector3(W, H, 1f), GateSign(face)));
            brk.Arm();
            return t;
        }

        static Transform Block(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        /// <summary>An empty holder at a spot on the snow, for something that can be broken or knocked down (Breakable).</summary>
        static Transform Holder(string name, Transform parent, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = at;
            return t;
        }

        // airborne sounds: a whoosh as you leave the snow without popping, and one for each half turn of a spin
        bool popped, wasAir;
        float spinSounded, nextImpactSound;
        void AirSounds(bool riding)
        {
            bool air = riding && rider.Airborne;
            if (air && !wasAir)
            {
                if (!popped) audioFx.Play(Sfx.Takeoff, Mathf.Clamp01(rider.Speed / 30f));
                spinSounded = 0f;
            }
            if (!air) popped = false;
            wasAir = air;
            if (air && rider.TrickName != null)
            {
                float turned = (Mathf.Abs(rider.TrickSpin) + Mathf.Abs(rider.TrickFlip)) * rider.TrickProgress;
                if (turned > spinSounded + 0.35f) { audioFx.Play(Sfx.Spin); spinSounded += Mathf.PI; }
            }
        }

        void StartScreaming()
        {
            screamT = 0f; screamSlowT = 0f; audioFx.StartScreaming();
            if (autoplay || showDiag) Debug.Log("SUMMIT SCREAM start v=" + (rider.Speed * 3.6f).ToString("F0") + " km/h");
        }

        // the screaming goes on, clip after clip, until the body has slowed below 10 km/h (held for a moment, so a
        // bounce doesn't cut it) or you're back on your feet; then it dies away
        void WatchScream(float dt)
        {
            if (audioFx == null || !audioFx.Screaming || dt <= 0f) return;
            screamT += dt;
            bool slow = body.RagdollSpeed * 3.6f < 10f;
            screamSlowT = slow ? screamSlowT + dt : 0f;
            if (!rider.Crashed || (screamT > 0.8f && screamSlowT > 0.3f))
            {
                audioFx.StopScreaming();
                if (autoplay || showDiag) Debug.Log("SUMMIT SCREAM stop after " + screamT.ToString("F1") + " s, body at " + (body.RagdollSpeed * 3.6f).ToString("F0") + " km/h");
            }
        }

        void OnCrash(string why)
        {
            if (state != State.Riding) return;
            if (why == Avalanche.Cause)
            {
                // the slide took the board out from under you: no fall counted, no getting up (see Avalanche)
                chainCount = 0;
                hud.Flash("CAUGHT", "the mountain has you", 1.8f);
                audioFx.Play(Sfx.SlideHit);
                StartScreaming();
#if UNITY_IOS || UNITY_ANDROID
                Handheld.Vibrate();
#endif
                return;
            }
            chainCount = 0;
            falls++;
            int left = Progress.Falls(MaxFalls) - falls;   // a spare helmet from the camp adds one
            fallsNote = left <= 0 ? "that's " + (Progress.Falls(MaxFalls) == 4 ? "four" : "three") : left == 1 ? "1 fall left" : left + " falls left";
            hud.Flash(why, fallsNote, 1.3f);
            medical += rider.Tube ? 400f : 850f;   // the trip to the clinic
            if (why.StartsWith("Crevasse")) audioFx.Play(Sfx.Nooo);   // down a crevasse: the long "nooo"
            else StartScreaming();                                     // the player's screams, from the impact until the body slows
            if (why.StartsWith("Crevasse")) { crevasseWatch = true; hud.Flash("CREVASSE!", fallsNote, 1.6f); if (left <= 0) { wipeoutTimer = 0.9f; endTitle = "WIPED OUT"; rider.StayDown = true; } }
            else if (left <= 0) { wipeoutTimer = 0.9f; endTitle = "WIPED OUT"; rider.StayDown = true; }   // the last fall: no getting up   // let the tumble play out, then it's over
            if (why.StartsWith("Tree")) { audioFx.Play(Sfx.HitTree); audioFx.Play(Sfx.Crash, 0.6f); }
            else if (why.StartsWith("Rock") || why.StartsWith("Hit the rocks")) { audioFx.Play(Sfx.HitRock); audioFx.Play(Sfx.Crash, 0.6f); }
            else if (why.StartsWith("Crevasse")) audioFx.Play(Sfx.Crevasse);
            else if (why.StartsWith("Bear")) { audioFx.Play(Sfx.Crash); audioFx.Play(Sfx.BearRoar); }
            else audioFx.Play(Sfx.Crash);
#if UNITY_IOS || UNITY_ANDROID
            Handheld.Vibrate();
#endif
        }
    }
}
