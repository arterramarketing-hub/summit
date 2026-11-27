using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Builds the whole game at start-up and runs the loop:
    /// start page → camp → ready → riding → buried → camp. Drop it on one empty GameObject.
    /// The Summit ▸ Build Scene menu does that for you and makes the materials.
    ///
    /// In parts: the world built at start-up (Awake, SummitGameWorld.cs), every setting and its row on the two
    /// settings sheets (SummitGameSettings.cs), and the buddy's voice watching the run (SummitGameVoice.cs). The
    /// start gate and everything round it is StartGate.BuildSite.
    /// </summary>
    public partial class SummitGame : MonoBehaviour
    {
        [Header("Materials (made by Summit ▸ Build Scene; plain ones are made if empty)")]
        public Material snow;
        public Material foliage, bark;
        public Material jacket, pants, board, skin, dark;
        public Material trail, spray;

        enum State { Hub, Ready, Riding, Buried, Title, Arrive }   // Hub: walking round the summit camp before a run; Title: the start page; Arrive: riding the lift up into it (LiftArrive)
        /// <summary>(lifttop) Coming into the camp on the Summit lift (from the start page, or back after a run): LiftArrive.</summary>
        readonly LiftArrive arrive = new LiftArrive();
        bool arriveNext;
        State state;
        Camp camp;
        MetaUi meta;
        StartPage start;   // the first screen (StartPage.cs): made when it is first shown

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
        string endTitle = "WIPED OUT";
        bool crevasseWatch;   // went over a crevasse: lost only if the body actually drops in

        // the medical bill: what this run's falls are costing you, as a receipt (Receipt). Every line comes from
        // something that happened in the fall (RiderBody.Injured, LimbLost, Killed, a crevasse, the avalanche) and
        // what followed it once the body stopped; each prints on the fall's slip the moment it is billed (HudReceipt)
        float medical;
        bool downBefore;
        float downT;
        readonly Receipt receipt = new Receipt();
        /// <summary>The run's medical receipt (for the test tools).</summary>
        public Receipt Bill => receipt;
        void OnBilled(Receipt.Item it)
        {
            medical += it.cost;
            string name = it.Name(blood == null || blood.On);
            // (a hit taken while still riding, a sign or a banner's stake: one line that pops and folds into the
            // corner's total; a fall's lines go on its slip)
            if (it.riding) hud.RideHit(name, it.cost, it.grade);
            else hud.BillLine(name, it.cost, it.grade, it.service);
        }
        static BodyPart LostPart(string limb)
        {
            bool left = limb.StartsWith("Left");
            if (limb.EndsWith("forearm")) return left ? BodyPart.ForearmL : BodyPart.ForearmR;
            if (limb.EndsWith("lower leg")) return left ? BodyPart.ShinL : BodyPart.ShinR;
            if (limb.EndsWith("arm")) return left ? BodyPart.ArmL : BodyPart.ArmR;
            return left ? BodyPart.ThighL : BodyPart.ThighR;
        }

        bool RiderTapped()
        {
            if (!hud.OnRider(RiderInput.PointerPosition())) return false;
            audioFx.Play(Sfx.Click);
            StepRide(1);
            return true;
        }

        /// <summary>What is ridden: round the rides, then the snow tube, then back to the first (or the other way).</summary>
        void StepRide(int dir)
        {
            int n = Rides.Count + 1, at = rider.Tube ? Rides.Count : (int)Progress.Ride;
            at = (at + (dir < 0 ? -1 : 1) + n) % n;
            bool tube = at == Rides.Count;
            if (!tube) Progress.PickRide((RideKind)at);
            rider.Tube = tube; body.SetTube(tube); ShowRider();
            PlayerPrefs.SetInt("summit_tube", tube ? 1 : 0); PlayerPrefs.Save();
            if (state == State.Ready) body.ResetVisual();   // (standing on the pad: shown at once; anywhere else the next start does it)
        }
        string RideName => rider.Tube ? "SNOW TUBE" : Rides.Of(rider.Ride).Name;
        /// <summary>The start pad's RIDER label: the ride's own name, or the tube.</summary>
        void ShowRider() => hud.SetRider(RideName);
        // ---------------------------------------------------------------- the start page

        /// <summary>Make the start page (its settings are the HUD's: they open from the camp and the start pad).</summary>
        void BuildStart()
        {
            start = new GameObject("StartPage").AddComponent<StartPage>();
            start.Build(chase.GetComponent<Camera>(), atmo, snow.shader, spray, () => dayNight != null ? dayNight.Darkness : 0f, () => LensFov[lensLevel]);
            start.Body = body;
            start.Clicked = () => audioFx.Play(Sfx.Click);
            start.TimeJumped = () => dayNight.ApplyNow();
        }

        bool titleShown;
        /// <summary>To the start page: the helicopter's view of the whole mountain, with the camp and the run a touch away.</summary>
        void ToTitle()
        {
            if (start == null) BuildStart();
            if (paused) SetPaused(false);
            CloseSettings();
            state = State.Title;
            hud.Clear(); hud.SetButtons(false, false, "PLAY"); hud.SetPrompt("");
            meta.SetMode(MetaUi.Mode.Off);
            camp.Leave(); chase.enabled = false;
            if (slideView != null) slideView.gameObject.SetActive(false);
            start.Show(!titleShown);
            titleShown = true;
        }

        void OnApplicationPause(bool away)
        {
            if (!away) return;
            PerfMarks.Flush();
            if (state == State.Riding && !paused) SetPaused(true);   // a call or the home swipe mid-run: come back paused
            Missions.Flush();
            PlayerPrefs.Save();
        }

        void OnDestroy() { Progress.Changed -= ApplyOutfit; Missions.Completed = null; }

        Collider lastShaken; float lastShakeTime = -9f;

        /// <summary>
        /// A tree knocked but standing (the rider into it, or a tumbling body): its snow comes down off it, from the
        /// foot of its crown to the top, and it shudders (softer than a snap: by how hard it was hit).
        /// </summary>
        void ShakeTree(Collider c, float into, Vector3 knock)
        {
            if (c == null || fx == null) return;
            if (c == lastShaken && Time.time - lastShakeTime < 0.6f) return;   // a body tumbling against it: once
            lastShaken = c; lastShakeTime = Time.time;
            Vector3 foot = c.transform.position;
            float h = 7.5f, crown = 2.2f;
            if (props.TreeOf(c, out var tree))
            {
                // the tree as drawn: as tall as it stands, as wide as its widest boughs
                h = Mathf.Max(2f, tree.top); crown = 0.8f;
                for (int i = 1; i <= 4; i++) crown = Mathf.Max(crown, Forest.CrownAt(tree, tree.top * i / 5f));
            }
            fx.TreeDump(foot, h, crown, into * 0.65f);
            TreeWind.Shake(foot, knock, into);
        }

        // ---- the settings from the camp and the start pad (MetaUi's sliders): the pause menu's sheet, with DONE.
        //      While it's up the camp stands still and a tap is the sheet's
        bool settingsOpen;
        void OpenSettings()
        {
            settingsOpen = true;
            hud.ShowSettings(true);
            meta.SetStick(false, Vector2.zero, Vector2.zero); meta.SetLookStick(false, Vector2.zero, Vector2.zero);
            input.SwallowCurrentPress();
        }
        void CloseSettings()
        {
            if (!settingsOpen) return;
            settingsOpen = false;
            hud.ShowSettings(false);
            input.SwallowCurrentPress();
        }
        /// <summary>A frame with the settings up: DONE closes them, any other tap is the sheet's. False when they're not up.</summary>
        bool SettingsStep()
        {
            if (!settingsOpen) return false;
            if (hud.PauseDrag(RiderInput.PointerPosition(), RiderInput.PointerHeld())) return true;   // (a TUNE slider held)
            if (input.AnyPress())
            {
                var p = RiderInput.PointerPosition();
                if (hud.OnDone(p)) { audioFx.Play(Sfx.Click); CloseSettings(); }
                else hud.OnPauseSettings(p);   // (a tab or a setting: the sheet plays the click)
            }
            return true;
        }

        // ---- (ridercrash) the free hit's warning: no words. A red pulse round the screen's edges (the look's own vignette,
        //      turned red and drawn in for a moment), a thump and a tap in the hand, the other rider's grunt (MountainRiders),
        //      and a red diamond under the shields for the rest of the run (Hud.SetHitMark)
        float hitPulse = -1f, vignetteWas; Color vignetteColWas;
        void FreeHit()
        {
            if (state != State.Riding) return;
            if (hitPulse < 0f && post != null && post.TryGet(out UnityEngine.Rendering.Universal.Vignette vg)) { vignetteWas = vg.intensity.value; vignetteColWas = vg.color.value; }
            hitPulse = 1f;
            FreeHits++;
            audioFx.Play(Sfx.Thump, 0.8f);
#if UNITY_IOS || UNITY_ANDROID
            Handheld.Vibrate();
#endif
        }
        /// <summary>The free hit's marker and pulse, a frame (Update; the tests call it).</summary>
        void HitWarningStep(float dt)
        {
            hud.SetHitMark(state == State.Riding && rider.FreeHitUsed);
            HitPulse(dt);
        }
        /// <summary>Free hits warned of (for the tests).</summary>
        public static int FreeHits;
        void HitPulse(float dt)
        {
            if (hitPulse < 0f || post == null || !post.TryGet(out UnityEngine.Rendering.Universal.Vignette vg)) return;
            hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt / 0.5f);
            float k = hitPulse * hitPulse;
            vg.color.Override(Color.Lerp(vignetteColWas, new Color(0.85f, 0.04f, 0.03f), k));
            vg.intensity.Override(Mathf.Lerp(vignetteWas, Mathf.Max(vignetteWas, 0.52f), k));
            if (hitPulse <= 0f) { vg.color.Override(vignetteColWas); vg.intensity.Override(vignetteWas); hitPulse = -1f; }
        }
        /// <summary>The warning's pulse now (0..1; -1 none), for the tests.</summary>
        public float HitPulseNow => hitPulse;

        void SetPaused(bool on)
        {
            paused = on;
            hitStopT = -1f; Time.fixedDeltaTime = FixedStep;   // a pause ends any crash slow-mo
            Time.timeScale = on ? 0f : 1f;
            if (!on) { rider.CancelPop(); input.SwallowCurrentPress(); }   // a menu tap is never a jump
            AudioListener.pause = on;
            hud.ShowPause(on);   // (it reads every setting as it comes up)
            input.SwallowCurrentPress();
        }

        void SetCamLevel(int k)
        {
            camLevel = Mathf.Clamp(k, 0, CamDist.Length - 1);
            chase.distScale = CamDist[camLevel];
            hud.RefreshPause();
            PlayerPrefs.SetInt("summit_camdist", camLevel); PlayerPrefs.Save();
        }   // went over a crevasse: lost only if the body actually drops into it
        bool endBuried;
        readonly Avalanche slide = new Avalanche();
        AvalancheView slideView;

        PowerUps power;
        Smasher smasher;
        Breakables breaks;
        TreeBreak treeBreak;
        PowerAura aura;
        const float ZoomSeconds = 3f, SuperJumpSeconds = 10f, InvincibleSeconds = 8f;   // how long each pickup lasts
        CourseDressing dressing;
        Wildlife wild;
        FallFlags flags;
        // the points: what has been banked (trickPoints), and the combo still in hand (Combo.cs), which is only
        // worth anything once it banks
        int trickPoints;
        readonly Combo combo = new Combo();
        int liveSteps;
        float stateTimer, best, runTime;

        int Score => Mathf.RoundToInt(rider.Distance) + trickPoints;
        /// <summary>The run's combo (for the test tools).</summary>
        public Combo Points => combo;

        float traceT, traceNext, readyT;
        float screamT, screamSlowT;   // how long this fall's screaming has gone on, and how long the body has been slow
        bool ledgeCued, ledgeFading;   // the first-ledge sting has played this run / is on its way out
        static readonly bool autoplay = System.Environment.GetEnvironmentVariable("SUMMIT_AUTOPLAY") == "1";

        // ---------------------------------------------------------------- the meta game: camp, points, missions

        void BuildMeta()
        {
            meta = new GameObject("MetaUi").AddComponent<MetaUi>();
            meta.Build();
            meta.Clicked = () => audioFx.Play(Sfx.Click);
            meta.Bought = () => audioFx.Play(Sfx.ShieldGet, 2);
            meta.Denied = () => audioFx.Play(Sfx.Scrape, 0.4f);
            // the closet's studio, and the rider repainted for each try-on (ApplyOutfit shows meta.TryOn while it's open)
            var closet = new GameObject("Closet").AddComponent<ClosetStage>();
            closet.Setup(body, Camera.main);
            meta.Stage = closet;
            meta.TriedOn = ApplyOutfit;
            camp = new GameObject("SummitCamp").AddComponent<Camp>();
            camp.soft = spray;
            camp.Build(snow.shader);
            Missions.Track(rider, power);
            // the camp's edges give way: a crack and powder, the screams, then back at the chalet (Camp.Collapse)
            camp.Collapsed += (at, dir) => { audioFx.Play(Sfx.Crevasse); fx?.Thud(at, 10f, dir * 3f); audioFx.StartScreaming(); audioFx.NoooFor(1.5f); };
            camp.Rescued += () => { audioFx.StopScreaming(); };
            // jumping and rolling in the camp: a grunt off the snow, a thump and a puff landing, a whoosh and a crunch
            // rolling, and the marks they leave (two deep boot prints are the camp's own; the roll's furrow is a crater
            // trail, like a fall's)
            camp.Jumped += at => audioFx.Play(Sfx.Grunt);
            camp.Landed += (at, travel, hard) => { audioFx.Play(hard > 3f ? Sfx.Thump : Sfx.Land); fx?.Landing(at, Vector3.up, hard + 2f, travel); };
            camp.Rolled += (at, dir) => { audioFx.Play(Sfx.Spin); audioFx.Play(Sfx.Scrape); };
            camp.Furrowed += (at, travel) => { body.SnowMark(at, travel, 0.4f, 0.07f); fx?.Puff(at, 1.2f); };
            Missions.Completed += m => { meta.Toast(m); audioFx.Play(Sfx.ShieldFull); };
            Progress.Changed += ApplyOutfit;
            ApplyOutfit();
        }

        void ApplyOutfit()
        {
            // what's worn, or, while the closet is open, what's being tried on (Wardrobe; RiderBody.Wear)
            int built = body.Built;
            body.Wear(meta != null && meta.TryOn != null ? meta.TryOn : Wardrobe.Worn());
            // another rider or set of clothes is a new body: the closet's studio copies it afresh
            if (body.Built != built && meta != null && meta.Stage != null) meta.Stage.Recopy();
            // what was picked at the rack (Progress.Ride): the rider's physics and what you see of them
            // (the physics follows what the body could actually put on: without the gear's files it stays a snowboard).
            // Only when the pick itself changed: the progress changes for other things too (a mission's reward, mid-run),
            // and then whatever the rider is on stays as it is. (rides2: checked against the rider's own ride, a mission
            // done mid-grind put a toboggan back on the board: Boardslide and Noseslide on a sled)
            if (Progress.Ride != ridePicked) { ridePicked = Progress.Ride; body.SetRide(Progress.Ride); rider.SetRide(body.Ride); if (hud != null) ShowRider(); }
        }
        RideKind? ridePicked;

        /// <summary>To the camp: on foot, with the menus. A fresh mountain unless you just came up from the start pad.</summary>
        void ToHub(bool fresh = true)
        {
            bool arriving = arriveNext; arriveNext = false;
            arrive.End();
            if (fresh) ToReady();
            CloseSettings();
            state = State.Hub;
            hud.Clear(); hud.SetButtons(false, false, "PLAY"); hud.SetPrompt("");
            chase.enabled = false;
            terrain.behindChunks = 4; props.behindCells = 16;   // you can turn round and look up the hill here
            // (lifttop) up on the Summit lift and off it into the camp; failing that, straight into the camp as before
            if (arriving)
            {
                camp.Leave();
                if (arrive.Begin(rider, body, chase.GetComponent<Camera>()))
                {
                    state = State.Arrive;
                    if (slideView != null) slideView.gameObject.SetActive(false);
                    lensFx.Strength = 0.3f;
                    terrain.Prewarm(); props.Prewarm();
                    meta.SetMode(MetaUi.Mode.Off);
                    input.SwallowCurrentPress();
                    return;
                }
                Debug.Log("LiftArrive: straight into the camp (" + arrive.Why + ")");
            }
            camp.Enter(rider, body, chase.GetComponent<Camera>(), !fresh && !arriving);   // (back up from the start pad: at the gate; else off the Summit lift, on its bench)
            if (slideView != null) slideView.gameObject.SetActive(false);   // its toe waits just uphill of the camp: not drawn here
            lensFx.Strength = 0.3f;   // the camp's gentle lens at once (it's a cut); dropping in eases back to yours
            terrain.Prewarm(); props.Prewarm();
            meta.SetMode(MetaUi.Mode.Hub);
        }

        /// <summary>
        /// (lifttop) A frame of the arrival on the lift (a tap skips it). True when it has just ended and the camp has the
        /// hero: where the glide stopped (or, had it given up, where the camp puts them).
        /// </summary>
        bool ArriveStep(float dt, bool tapped)
        {
            if (state != State.Arrive) return false;
            if (!arrive.Step(dt, tapped)) return false;
            arrive.End();
            state = State.Hub;
            var cam = chase.GetComponent<Camera>();
            if (arrive.EndPos != Vector3.zero) camp.EnterAt(rider, body, cam, arrive.EndPos, arrive.EndYaw, !arrive.Skipped && arrive.Why == "");
            else camp.Enter(rider, body, cam, false);
            meta.SetMode(MetaUi.Mode.Hub);
            return true;
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
            var m = Mats.Lit(c, smooth, instanced: true);
            m.SetColor("_Color", c); m.SetFloat("_Glossiness", smooth);   // (the names other shaders read them by)
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
            if (hitStopT >= 0f) { hitStopT = -1f; if (!paused) SetTimeScale(1f); }
            cliffNooo = false; crevNooo = false;
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
            trickPoints = 0; combo.Reset(); liveSteps = 0; downBefore = false; hud.ClearPoints(); runTime = 0f;
            power.ResetRun();
            wild.ResetRun();
            smasher.Clear(); aura.Clear(); treeBreak.Clear();
            TreeWind.Avalanche(false, 0f, 0f);
            Breakable.RestoreAll();   // every sign and flag standing again
            terrain.Prewarm();
            props.Prewarm();
            dressing.Prewarm();
            chase.Snap();
            hud.SetWhiteout(0f);
            falls = 0; wipeoutTimer = 0f; hurtShake = 0f; crevasseWatch = false; medical = 0f; receipt.Reset(); downT = 0f;
            flags.Clear();
            hud.SetPrompt(""); hud.SetTapCue(false);
            if (paused) SetPaused(false);
            CloseSettings();
            chase.peek = 0f;
            if (atmo != null) atmo.ResetParallax();
            if (fx != null) fx.Clear();
            if (blood != null) blood.Clear();
            ShowReadyHint();
            hud.SetButtons(true, false, "PLAY");
            hud.SetMode(input.useTilt);
            meta.SetMode(MetaUi.Mode.Ready);
        }

        /// <summary>The start pad's words: the best, and how it's played (one finger, the jump as the JUMP setting has it).</summary>
        void ShowReadyHint()
        {
            string jump = JumpWords;
            hud.Show("", "Best " + Mathf.RoundToInt(best).ToString("N0"),
                Application.isMobilePlatform ? (input.useTilt ? "Tilt to steer" : "Drag to steer") + "  ·  " + jump + " to jump\nIn the air: push a direction to spin or flip  ·  tap to grab"
                              : "A / D to steer  ·  tap Space to jump, hold it to tuck\nIn the air: A / D spin  ·  W / S flip  ·  Space grabs");
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
            hud.Flash("GO", "", 1.0f);
        }

        void ToBuried() => ToEnd("WIPED OUT", true);

        float endFade = 1f;
        void ToEnd(string title, bool buried, bool rollOn = false)
        {
            if (rider.Crashed) flags.Plant(body.RagdollCentre, falls);   // the fall that ended it gets its flag too
            combo.Bank();   // (anything still in hand at the end counts: a crash has already taken what it takes)
            audioFx.StopCue();
            state = State.Buried;
            hud.SetPrompt(""); hud.SetTapCue(false);
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
                     "   ·   " + Mathf.RoundToInt(rider.Distance) + " m   ·   " + Hud.FormatTime(runTime));
            // the bill: the last fall's slip carries up to the top of the screen as the run's whole receipt (HudReceipt)
            if (receipt.Open) { if (buried) receipt.Avalanche(); receipt.Rest(); receipt.Close(); }
            hud.BillEnd(receipt.Merged(blood == null || blood.On), receipt.RunTotal, blood == null || blood.On);
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
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", "SUMMIT HITCH " + (r * 1000f).ToString("0") + " ms  state=" + state + "  air=" + rider.Airborne +
                          "  speed=" + rider.Speed.ToString("0.0") + "  gc=" + System.GC.CollectionCount(0));   // (no stack trace: measuring must not cost)
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
            // the device checks' markers (PerfMarks), written out only at a quiet moment: not in the first 16 s of the
            // ride, where the frame rate is logged
            PerfMarks.Tick(!(state == State.Riding && traceT < 16f));
            float dt = Time.deltaTime;
            stateTimer += dt;
            WatchScream(dt);
            if (!(Application.isBatchMode && TestHitStops)) TickHitStop(Time.unscaledDeltaTime);
            WatchDrop();
            WatchWoah();
            WatchWarn();
            // a gentler lens in the camp (its camera is close, among straight lines), the player's own on the slope
            // (none at all in the closet's studio: it would bend the hero)
            lensFx.Strength = ClosetStage.Showing != null ? 0f : Mathf.MoveTowards(lensFx.Strength, state == State.Hub ? 0.3f : 1f, Time.unscaledDeltaTime * 1.4f);
            switch (state)
            {
                case State.Arrive:
                    if (ArriveStep(dt, input.AnyPress())) input.SwallowCurrentPress();
                    break;

                case State.Hub:
                    if (autoplay)
                    {
                        // device testing: a look at the camp, then on to the run
                        float before = readyT; readyT += Time.unscaledDeltaTime;
                        if (before < 2.5f && readyT >= 2.5f) AutoShot("auto-hub.png");
                        if (readyT >= 3.5f) { readyT = 0f; ToReady(false); }
                    }
                    else if (SettingsStep()) { }
                    else if (camp.Step(dt, meta)) { audioFx.Play(Sfx.Play); ToReady(false); input.SwallowCurrentPress(); }
                    else if (meta.SettingsAsked) { meta.SettingsAsked = false; OpenSettings(); }
                    // the camp's way back to the start page: through cloud, the scene changed behind it
                    else if (meta.TitleAsked) { meta.TitleAsked = false; if (start == null) BuildStart(); start.Through(ToTitle); }
                    break;

                case State.Title:
                {
                    // the start page: it answers once the cloud has closed over the view, and the camp or the
                    // start gate is there when it clears
                    int go = start.Step(Time.unscaledDeltaTime, input.AnyPress(), RiderInput.PointerPosition());
                    if (go == 0) break;
                    start.Hide();
                    if (go == 1) { arriveNext = true; ToHub(false); } else { audioFx.Play(Sfx.Play); ToReady(false); }
                    input.SwallowCurrentPress();
                    break;
                }

                case State.Ready:
                    traceT = 0f;
                    if (autoplay && (readyT += dt) > 3f) { readyT = 0f; ToRiding(); }
                    else if (autoplay && readyT > 2f && readyT - dt <= 2f) AutoShot("auto-ready.png");
                    else if (SettingsStep()) { }
                    else if (input.AnyPress() && meta.Hit(RiderInput.PointerPosition()) == "settings") { audioFx.Play(Sfx.Click); OpenSettings(); }
                    else if (input.AnyPress() && ModeTapped()) { }
                    else if (input.AnyPress() && RiderTapped()) { }
                    else if (input.AnyPress() && meta.Hit(RiderInput.PointerPosition()) == "camp") { audioFx.Play(Sfx.Click); ToHub(false); }
                    else if (input.AnyPress() && (Tapped(true) || SpacePressed())) { audioFx.Play(Sfx.Play); ToRiding(); }
                    break;

                case State.Riding:
                    if (paused)
                    {
                        hud.SetTapCue(false);
                        if (hud.PauseDrag(RiderInput.PointerPosition(), RiderInput.PointerHeld())) break;   // (a TUNE slider held)
                        if (input.AnyPress())
                        {
                            var pp = RiderInput.PointerPosition();
                            if (hud.OnResume(pp)) { audioFx.Play(Sfx.Click); SetPaused(false); }
                            else if (hud.OnPauseRestart(pp)) { audioFx.Play(Sfx.Click); SetPaused(false); ToReady(); input.SwallowCurrentPress(); }
                            else hud.OnPauseSettings(pp);   // (a tab or a setting: the sheet plays the click)
                        }
                        break;
                    }
                    if (input.AnyPress() && hud.OnRestart(RiderInput.PointerPosition())) { audioFx.Play(Sfx.Click); SetPaused(true); break; }
                    // down: the camera stays on the body, for as long as you like, until you tap. A tap gets you up at
                    // once, even mid-tumble; after the run's last fall (or a fatal one) it goes on to the end. A tap
                    // while the fall isn't decided yet (still dropping off something high) waits for the answer
                    if (CrashTapStep(dt)) break;
                    hud.SetPrompt("");
                    hud.SetTapCue(rider.Crashed && !slide.Caught && !autoplay);
                    runTime += dt;
                    slide.Step(dt, rider, body);
                    TreeWind.Avalanche(slide.Released, slide.Z, slide.Speed);   // its air blast bends the trees ahead of it
                    if (slide.JustReleased) { audioFx.Play(Sfx.SlideCrack); hud.Flash("AVALANCHE!", "", 1.6f); }
                    // the first ledge: the suspense sting as you roll up to the lip, eased out once you've landed below it
                    if (!ledgeCued && !rider.Crashed && rider.Z > Slope.PadLip - 14f && rider.Z < Slope.PadLip + 1f) { ledgeCued = true; audioFx.Play(Sfx.Ledge); }
                    if (ledgeCued && (ledgeFading || rider.Crashed || (rider.Z > Slope.PadLip + 8f && !rider.Airborne))) { ledgeFading = true; audioFx.FadeCue(dt); }
                    Missions.Step(dt, slide.Gap(rider), Score);
                    if (crevasseWatch)
                    {
                        if (body.RagdollBelowSnow > 3f) { crevasseWatch = false; rider.StayDown = true; wipeoutTimer = 2.0f; endTitle = "WIPED OUT"; receipt.Crevasse(); }
                        else if (!rider.Crashed) crevasseWatch = false;   // caught the lip: you live
                    }
                    if (!rider.Crashed && !rider.Airborne && !slide.Caught && rider.Z > Slope.FinishZ + 30f)
                    {
                        // through the finish at the base: the whole mountain, done
                        combo.Bank();
                        trickPoints += 5000;
                        Missions.Finished();
                        audioFx.Play(Sfx.TrickBig, 4);
                        ToEnd("YOU SURVIVED", false, true);   // carry on through the arch while the screen fades in
                        break;
                    }
                    // (only the device-test autoplay, with nobody to tap, moves on from a fatal fall by itself)
                    if (autoplay && wipeoutTimer > 0f && !slide.Caught) { if (!body.RagdollMoving) wipeoutTimer -= dt; if (wipeoutTimer <= 0f) { ToEnd(endTitle, false); break; } }
                    StepCombo(dt);
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
                        arriveNext = true;
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
                if (before < 5.0f && traceT >= 5.0f) AutoShot("auto-pause.png");
                if (before < 5.5f && traceT >= 5.5f) SetPaused(false);
                foreach (float at in AutoShotsAt)
                    if (before < at && traceT >= at) AutoShot("auto-" + at.ToString("0.0") + ".png");
                if (traceNext <= 0f)
                {
                    traceNext = 0.25f;
                    var rp = rider.Body.position; var tp = rider.transform.position;
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", $"SUMMIT TRACE t={traceT:0.00} body={rp} tf={tp} ground={Slope.DrawnHeight(rp.x, rp.z):0.00} v={rider.Body.linearVelocity} " +
                              $"air={rider.Airborne} crash={rider.Crashed} kin={rider.Body.isKinematic} run={rider.Running} cam={chase.transform.position} fps={1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):0}");
                }
            }

            // no stick graphic: the thumb steers the rider directly and the rider's lean shows the steering
            hud.SetStick(false, input.StickOrigin, input.StickPos, 1f);

            float gap = state == State.Ready || state == State.Hub || state == State.Title ? 120f : !slide.Released ? 9999f : Mathf.Max(0f, slide.Gap(rider));   // 9999: still holding, no slide to show
            float close = Mathf.Clamp01(1f - gap / 45f);
            hud.SetRun(rider.Speed * 3.6f, Score, 1f, gap, 160f, state == State.Riding);
            // the combo and the bill, as they run up (held while paused; real time, so a crash's slow motion does not slow them)
            if (!paused) { hud.ComboShow(state == State.Riding ? combo : null); hud.StepPoints(Time.unscaledDeltaTime); }
            hud.SetPower(runTime, rider.Shields, state == State.Riding);
            HitWarningStep(Time.unscaledDeltaTime);
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
            chase.shake = (state == State.Riding ? slide.Shake + (rider.Crashed && !slide.Caught ? 0.2f * Mathf.Clamp01((body.RagdollSpeed - 2.8f) / 12f) : 0f) : 0f) + hurtShake * hurtShake * 0.8f;
            hud.SetFalls(Progress.Falls(MaxFalls) - falls, Progress.Falls(MaxFalls), state == State.Riding);
            bool onSnow = state == State.Riding && !rider.Airborne && !rider.Crashed;
            bool riding = state == State.Riding;
            Vector3 rpos = rider.transform.position;
            // the snow underfoot (Surfaces): the day's part of it, and the map the terrain draws it from kept painted
            // round the rider (round the camera, away from the run)
            Surfaces.Step(riding ? rpos : chase.transform.position, riding ? rider.RunNow : 0f, dt);
            var snowNow = rider.Underfoot;
            audioFx.Set(new RideSound
            {
                speed = rider.Speed / 36f, carve = rider.Tube ? 0f : Mathf.Max(rider.Carve, rider.Bite), skid = rider.Tube ? 0.15f * Mathf.Clamp01(rider.Speed / 20f) : rider.Skid,
                rough = Mathf.Clamp01(snowNow.powder + 0.6f * snowNow.slush), ice = snowNow.ice, chatter = rider.SnowFeel.chatter, slush = snowNow.slush,
                avalanche = riding ? slide.Closeness(rider) : 0f, pan = rider.Steer,
                muffle = state == State.Buried ? (endBuried ? 1f : 0.45f) : rider.Crashed ? 0.3f : 0f,
                onSnow = onSnow && !rider.Grinding, air = riding && rider.Airborne, riding = riding,
                camp = state == State.Hub || state == State.Title, ended = state == State.Buried,
                boost = riding && rider.BoostTime > 0f,
                grind = riding && rider.Grinding ? (rider.GrindFeature.kind == Course.Kind.Rail ? 1 : 2) : 0,
                vehicle = body.OnVehicleNow && !rider.Crashed ? (int)body.Ride : -1, rpm = rider.Rpm, throttle = rider.Throttle, gait = rider.GaitRate,
            });
            AirSounds(riding);
        }

        // device testing: the autoplay's screenshots. Each costs the phone a frame of about 50 ms (the picture read back
        // and saved), which shows as a dip in the fps logged next: they are marked in the log ("SUMMIT PERF autoplay
        // screenshot"), and SUMMIT_NOSHOTS=1 leaves them out for runs that measure the frame rate
        static readonly float[] AutoShotsAt = { 3f, 7.6f, 8.4f, 10f, 14f };
        static readonly bool noShots = System.Environment.GetEnvironmentVariable("SUMMIT_NOSHOTS") == "1";
        static void AutoShot(string file)
        {
            if (noShots) return;
            PerfMarks.Note("autoplay screenshot " + file);
            ScreenCapture.CaptureScreenshot(file);
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
            // (a rough landing shows and sounds rough: no caption)
            if (why == "Hard landing") audioFx.Play(Sfx.BuddyOuch);
            else if (why == "Sketchy landing") audioFx.Play(Sfx.Scrape, 3f);   // the board catching as it comes round late
        }

        // ---------------------------------------------------------------- points

        /// <summary>The combo's events to the HUD and the sound (once, when the game is built).</summary>
        void HookPoints()
        {
            combo.Added += (line, up) =>
            {
                hud.ComboAdded(line, up);
                // a link being held or built (a grind begun, a trick growing in the air): a soft note, a step up each time it grows
                if (line.live) { liveSteps++; audioFx.Play(Sfx.PointTick, combo.Mult - 1 + liveSteps); }
                if (up && combo.Mult == 3) audioFx.Play(Sfx.BuddyCheer);   // a combo getting long
            };
            combo.BankedNow += (total, pot, mult) =>
            {
                trickPoints += total;
                hud.ComboBanked(total);
                if (total >= 200) audioFx.Play(Sfx.Bank, total < 500 ? 0 : total < 2000 ? 1 : total < 6000 ? 2 : total < 15000 ? 3 : 4);
                if (total >= 4000) audioFx.Play(Sfx.BuddyCheer);
            };
            combo.Dropped += total => { hud.ComboDropped(total); if (total >= 300) audioFx.Play(Sfx.ComboLost); };
            hud.PointTick = () => audioFx.Play(Sfx.PointTick, combo.Active ? combo.Mult - 1 : 3);
            hud.BillTick = () => audioFx.Play(Sfx.BillTick);
        }

        /// <summary>
        /// Anything that scores, from anywhere: a link into the run's combo. (The rider's own tricks, grinds and
        /// close calls arrive through Rider.Link; Rider.Score is the same door from the rider's side.)
        /// </summary>
        public void Link(string name, int points, LinkKind kind) => OnLink(new TrickLink { name = name, points = points, kind = kind });

        void OnLink(TrickLink l)
        {
            if (state != State.Riding) return;
            combo.Add(l);
            liveSteps = 0;
            if (l.kind == LinkKind.CloseCall) { audioFx.Play(Sfx.CloseCall); audioFx.Play(Sfx.BuddyClose); }
            else
            {
                audioFx.Play(l.points >= 700 ? Sfx.TrickBig : Sfx.Trick, combo.Mult - 1);   // the chime climbs with the multiplier
                if (l.points >= 600 && !l.sketchy) audioFx.Play(Sfx.BuddyCheer);              // a big trick
            }
        }

        /// <summary>One frame of the combo while riding: the link in progress, the window, and a fall's bill banked as the rider gets up.</summary>
        void StepCombo(float dt)
        {
            if (state != State.Riding) return;
            string live = rider.Crashed ? null : rider.LiveName;
            if (live != null) combo.SetLive(live, rider.LivePoints, rider.LiveKind);
            else if (combo.LiveName != null) { combo.SetLive(null, 0, LinkKind.Trick); liveSteps = 0; }
            combo.Step(dt, rider.Airborne || rider.Grinding || rider.InLineFlow || rider.GroundSpinning);   // (a flat spin on the snow holds it too: groundspin)   // (and between a feature line's hits: RiderLines)
            // the fall's receipt: what followed is billed once the body has stopped (or the rider is up); up again,
            // it tears off into the corner's total
            if (rider.Crashed && receipt.Open)
            {
                body.ReportBreaks();   // (a bone the wipeout broke with no hit to carry it: RiderBodyInjury)
                downT += dt;
                if (!receipt.Served && !slide.Caught && downT > 1f && !body.RagdollMoving) receipt.Rest();   // (under the avalanche: billed at the end, buried; up early, billed as you get up)
            }
            if (downBefore && !rider.Crashed) { receipt.Rest(); receipt.Close(); hud.BillBank(); }
            downBefore = rider.Crashed;
        }

        /// <summary>For the test tools (batchmode never runs Update): a frame of the points, on their clock.</summary>
        public void TestPoints(float dt) { StepCombo(dt); hud.ComboShow(state == State.Riding ? combo : null); hud.StepPoints(dt); }

        void OnPickup(PowerUps.Kind kind)
        {
            if (state != State.Riding) return;
            if (kind == PowerUps.Kind.Soda)
            {
                rider.AddBoost(ZoomSeconds * Progress.PowerUpTime);
                hud.Flash("MOUNTAIN ZOOM", "", 0.9f);
                audioFx.Play(Sfx.Soda);
            }
            else if (kind == PowerUps.Kind.SuperJump)
            {
                rider.AddSuperJump(SuperJumpSeconds * Progress.PowerUpTime);
                hud.Flash("SUPER JUMP", JumpWords + " for big air", 1.2f);
                audioFx.Play(Sfx.PowerJump);
            }
            else if (kind == PowerUps.Kind.Invincible)
            {
                rider.AddInvincible(InvincibleSeconds * Progress.PowerUpTime);
                hud.Flash("INVINCIBLE", "", 1.2f);
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
        void RebuildGate()
        {
            if (gateRoot != null) { if (Application.isPlaying) Destroy(gateRoot.gameObject); else DestroyImmediate(gateRoot.gameObject); }
            BuildStartGate();
        }

        void BuildStartGate() => gateRoot = StartGate.BuildSite(snow.shader, gateMats, rider, () => audioFx.Play(Sfx.Clack));

        // airborne sounds: a whoosh as you leave the snow without popping, and one for each half turn of a spin
        bool popped, wasAir;
        float spinSounded, groundSounded, nextImpactSound;
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
            // (and a flat spin on the snow: one for each half turn, groundspin)
            if (!rider.GroundSpinning) groundSounded = 0f;
            else if (Mathf.Abs(rider.GroundSpinNow) > groundSounded + 0.35f) { audioFx.Play(Sfx.Spin); groundSounded += Mathf.PI; }
            if (air && rider.TrickName != null)
            {
                float turned = Mathf.Abs(rider.SpinNow) + Mathf.Abs(rider.FlipNow);
                if (turned > spinSounded + 0.35f) { audioFx.Play(Sfx.Spin); spinSounded += Mathf.PI; }
            }
        }

        // the hit-stop: on a crash, time all but stops for a split moment and then eases back, to sell the impact.
        // Only time slows: the physics step stays at FixedStep, and the rider, the thrown body and the board are
        // interpolated between steps, so the slow motion is still smooth. (Shrinking the step with it, down to 1/3000 s,
        // made PhysX's joint and contact corrections, which scale with 1/step, throw the body tens of metres into the
        // air, once 660 m: launched, and never coming down)
        const float FixedStep = 1f / 120f;
        float hitStopT = -1f, hitStopHold, hitStopScale, nextHitStop;
        const float HitStopEase = 0.4f;

        /// <summary>
        /// For the test tools: the hit-stop runs in batchmode too, on a clock they advance themselves (TestTickHitStop,
        /// one call per physics step with the real time it stands for), since batchmode never calls Update.
        /// </summary>
        public static bool TestHitStops;
        float hitClock;
        public void TestTickHitStop(float realDt) => TickHitStop(realDt);

        void HitStop(float strength)
        {
            if (paused || (Application.isBatchMode && !TestHitStops) || hitClock < nextHitStop) return;
            strength = Mathf.Clamp01(strength);
            if (hitStopT >= 0f && hitStopT < hitStopHold && strength < 0.5f) return;   // don't stack small ones on a big one
            hitStopHold = Mathf.Lerp(0.05f, 0.14f, strength);
            hitStopScale = Mathf.Lerp(0.3f, 0.04f, strength);
            hitStopT = 0f;
            nextHitStop = hitClock + 0.7f;
            SetTimeScale(hitStopScale);
        }

        void TickHitStop(float realDt)
        {
            hitClock += realDt;
            if (paused || hitStopT < 0f) return;
            hitStopT += realDt;
            if (hitStopT < hitStopHold) { SetTimeScale(hitStopScale); return; }
            float k = (hitStopT - hitStopHold) / HitStopEase;
            if (k >= 1f) { hitStopT = -1f; SetTimeScale(1f); return; }
            SetTimeScale(Mathf.Lerp(hitStopScale, 1f, k * k * (3f - 2f * k)));
        }

        static void SetTimeScale(float s) { Time.timeScale = s; Time.fixedDeltaTime = FixedStep; }

        // ---- after a fall: wait for the player's tap (ResumeTap)

        readonly ResumeTap crashTap = new ResumeTap();
        bool crashTapQueued;
        /// <summary>Test tools: whether a tap that ends a fall is waiting for the fall to be decided.</summary>
        public bool CrashTapQueued => crashTapQueued;

        /// <summary>
        /// One frame down in the snow: a tap (or one waiting) gets the rider up, or after a fall that ends the run goes
        /// on to the end. True when it left the riding state. Nothing happens without a tap.
        /// </summary>
        public bool CrashTapStep(float dt)
        {
            if (!rider.Crashed || slide.Caught) { crashTapQueued = false; return false; }
            if (crashTap.Step(RiderInput.PointerHeld(), RiderInput.PointerPosition(), RiderInput.SpaceHeld(), dt, Mathf.Min(Screen.width, Screen.height)))
                crashTapQueued = true;
            if (!crashTapQueued) return false;
            if (rider.StayDown && wipeoutTimer > 0f)
            {
                crashTapQueued = false;
                ToEnd(endTitle, false);
                return true;
            }
            if (rider.CanGetUp)
            {
                crashTapQueued = false;
                rider.RequestUp = true;
                input.SwallowCurrentPress();
                audioFx.Play(Sfx.Groan);
            }
            // (otherwise the fall isn't decided yet: the tap waits for it to land, or for the end)
            return false;
        }

        void OnCrash(string why)
        {
            if (state != State.Riding) return;
            crashTap.Arm(RiderInput.PointerHeld(), RiderInput.SpaceHeld()); crashTapQueued = false;
            if (why == Avalanche.Cause)
            {
                // the slide took the board out from under you: no fall counted, no getting up (see Avalanche)
                combo.Drop();
                receipt.Ride = rider.Ride; receipt.Tube = rider.Tube; receipt.Begin();   // (what the burial did is billed at the end: ToEnd)
                audioFx.Play(Sfx.SlideHit);
                StartScreaming();
#if UNITY_IOS || UNITY_ANDROID
                Handheld.Vibrate();
#endif
                return;
            }
            combo.Drop();   // whatever was in hand is gone
            falls++;
            int left = Progress.Falls(MaxFalls) - falls;   // a spare helmet from the camp adds one
            receipt.Ride = rider.Ride; receipt.Tube = rider.Tube; receipt.Begin(); downT = 0f;   // a new fall on the bill
            // the impact, frozen for a heartbeat: into a tree, rock or sign, or the ground at the foot of a cliff
            // (not a crevasse, a slam on landing or the avalanche)
            if (why != "Slammed!" && !why.StartsWith("Crevasse")) HitStop((rider.Speed * 3.6f - 30f) / 110f);
            if (why.StartsWith("Crevasse")) audioFx.Play(Sfx.Nooo);   // down a crevasse: the long "nooo"
            else StartScreaming();                                     // the player's screams, from the impact until the body slows
            if (why.StartsWith("Crevasse")) { crevasseWatch = true; if (left <= 0) { wipeoutTimer = 2.0f; endTitle = "WIPED OUT"; rider.StayDown = true; } }
            else if (left <= 0) { wipeoutTimer = 2.0f; endTitle = "WIPED OUT"; rider.StayDown = true; }   // the last fall: no getting up   // let the tumble play out, then it's over
            if (body.OnVehicleNow && body.Ride == RideKind.Bear) audioFx.BearGrunt(1f);   // thrown off the bear: it grunts (rides)
            if (why.StartsWith("Tree")) { audioFx.Play(Sfx.HitTree); audioFx.Play(Sfx.Crash, 0.6f); }
            else if (why.StartsWith("Rock") || why.StartsWith("Hit the rocks")) { audioFx.Play(Sfx.HitRock); audioFx.Play(Sfx.Crash, 0.6f); }
            else if (why.StartsWith("Crevasse")) { }   // only the "nooo": the old synthesized fall (a falling whoosh that read as a voice) is gone
            else if (why.StartsWith("Bear")) { audioFx.Play(Sfx.Crash); audioFx.Play(Sfx.BearRoar); }
            else audioFx.Play(Sfx.Crash);
#if UNITY_IOS || UNITY_ANDROID
            Handheld.Vibrate();
#endif
        }
    }
}
