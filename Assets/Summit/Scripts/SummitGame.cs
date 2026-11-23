using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Builds the whole game at start-up and runs the loop:
    /// start page → camp → ready → riding → buried → camp. Drop it on one empty GameObject.
    /// The Summit ▸ Build Scene menu does that for you and makes the materials.
    /// </summary>
    public class SummitGame : MonoBehaviour
    {
        [Header("Materials (made by Summit ▸ Build Scene; plain ones are made if empty)")]
        public Material snow;
        public Material foliage, bark;
        public Material jacket, pants, board, skin, dark;
        public Material trail, spray;

        enum State { Hub, Ready, Riding, Buried, Title }   // Hub: walking round the summit camp before a run; Title: the start page
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
        // pause menu and the camera distance setting
        bool paused;
        static readonly float[] CamDist = { 0.65f, 0.82f, 1f, 1.22f, 1.5f };
        static readonly string[] CamName = { "CLOSEST", "CLOSE", "NORMAL", "FAR", "FARTHEST" };
        int camLevel = 2;
        // camera angle (height) and lens (field of view and barrel distortion), from the pause menu
        static readonly float[] AngleHeight = { 0.62f, 0.9f, 1.1f, 1.55f, 2.2f };   // times ChaseCamera's Lift/Clear heights
        static readonly string[] AngleName = { "GROUND", "LOW", "NORMAL", "HIGH", "OVERHEAD" };
        static readonly float[] LensFov = { -14f, 0f, 8f, 18f };
        static readonly float[] LensWarp = { 0f, -0.28f, -0.42f, -0.6f };
        static readonly string[] LensName = { "STANDARD", "WIDE", "WIDER", "FISHEYE" };
        int angleLevel = 1, lensLevel = 1;
        bool pov;   // first person instead of the chase camera (the VIEW button; ChaseCamera does the rest)
        UnityEngine.Rendering.Universal.LensDistortion lens;
        // colour filter (LookGrade) and its strength in tenths, and the lens type (LensFX), from the pause menu
        int filterLevel, filterStrength = 10, lensMode;
        int halation;   // the film glow round highlights (LensFX): OFF, SUBTLE, STRONG
        LensFX lensFx;
        DayNight dayNight;   // the time of day's light (TimeOfDay, DayNight.cs), and the pause menu's TIME row
        Weather weather;     // rolled for each new mountain (Weather.cs); null in the test tools unless they ask for one
        UnityEngine.Rendering.VolumeProfile post;

        void ApplyCamera()
        {
            chase.heightScale = AngleHeight[angleLevel];
            chase.extraFov = LensFov[lensLevel];
            lensFx.Apply(lens, LensWarp[lensLevel], lensLevel, lensMode);
            hud.SetAngle(AngleName[angleLevel]); hud.SetLens(LensName[lensLevel]); hud.SetLensType(LensFX.ModeName[lensMode]);
            ShowView();
            PlayerPrefs.SetInt("summit_angle", angleLevel); PlayerPrefs.SetInt("summit_lens", lensLevel); PlayerPrefs.SetInt("summit_lenstype", lensMode); PlayerPrefs.Save();
        }

        // the VIEW tab's top button, and the two rows that only mean something to the chase camera
        void ShowView()
        {
            hud.SetPausePov(pov);
            hud.SetCamera(pov ? "N/A" : CamName[camLevel]);
            hud.SetAngle(pov ? "N/A" : AngleName[angleLevel]);
        }

        void SetPov(bool on)
        {
            pov = on; chase.pov = on;
            ShowView();
            PlayerPrefs.SetInt(ChaseCamera.PovPref, on ? 1 : 0); PlayerPrefs.Save();
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

        // the GAME tab's TURN: how sharp the carve is (SteerPrefs, Rider.TurnScale), felt the moment the run resumes
        void StepTurn(int dir) => SetTurnStop(SteerPrefs.Stop + dir);
        void SetTurnStop(int k)
        {
            SteerPrefs.Stop = k;
            hud.SetTurn(SteerPrefs.Label, SteerPrefs.Fraction);
        }
        bool turnDrag;   // a finger that went down on the TURN bar, sliding it

        // ---- every setting has one method here, which the pause menu and the start page's settings both call
        //      (BuildStart hands them to StartPage as rows), so the two can never disagree and a change made in
        //      one shows in the other
        void StepCam(int dir) => SetCamLevel(camLevel + dir);
        void StepAngle(int dir) { angleLevel = Mathf.Clamp(angleLevel + dir, 0, AngleHeight.Length - 1); ApplyCamera(); }
        void StepLens(int dir) { lensLevel = Mathf.Clamp(lensLevel + dir, 0, LensFov.Length - 1); ApplyCamera(); }
        void StepLensType() { lensMode = (lensMode + 1) % LensFX.ModeName.Length; ApplyCamera(); }
        void StepFilter(int dir) { filterLevel = (filterLevel + dir + LookGrade.Names.Length) % LookGrade.Names.Length; ApplyLook(); }
        void StepStrength(int dir) { filterStrength = Mathf.Clamp(filterStrength + dir, 0, 10); ApplyLook(); }
        void StepHalation(int dir) { halation = Mathf.Clamp(halation + dir, 0, LensFX.HalationName.Length - 1); ApplyLook(); }
        void StepTime(int dir) { TimeOfDay.StepLock(dir); hud.SetTime(TimeOfDay.LockLabel); }
        void ToggleSteer() { input.SetTilt(!input.useTilt); hud.SetPauseSteer(input.useTilt); hud.SetMode(input.useTilt); }
        // how the one finger jumps: a double tap (the default), a tap, or a flick up (RiderInput, FingerGesture)
        void StepJump(int dir) { input.SetJumpStyle((JumpStyle)(((int)input.jumpStyle + dir + 3) % 3)); hud.SetPauseJump(JumpName); if (state == State.Ready) ShowReadyHint(); }
        string JumpName => RiderInput.JumpNames[(int)input.jumpStyle];
        /// <summary>The jump, in words, for the hints: as the JUMP setting has it.</summary>
        string JumpWords => input.jumpStyle == JumpStyle.Tap ? "tap" : input.jumpStyle == JumpStyle.FlickUp ? "flick up" : "double-tap";
        // (tilt steering the other way round: the game has always read "summit_invert", but nothing could set it)
        void ToggleInvert() { input.invertTilt = !input.invertTilt; PlayerPrefs.SetInt("summit_invert", input.invertTilt ? 1 : 0); PlayerPrefs.Save(); }
        void ToggleTrees() { props.SetRealistic(!props.IsRealistic); hud.SetPauseTrees(props.IsRealistic); }
        void ToggleVoice() { SoundMix.VoiceLines = !SoundMix.VoiceLines; hud.SetVoiceLines(SoundMix.VoiceLines); PlayerPrefs.SetInt("summit_voicelines", SoundMix.VoiceLines ? 1 : 0); PlayerPrefs.Save(); }
        void ToggleFlashlight() { DayNight.FlashlightOn = !DayNight.FlashlightOn; hud.SetFlashlight(DayNight.FlashlightOn); PlayerPrefs.SetInt("summit_flashlight", DayNight.FlashlightOn ? 1 : 0); PlayerPrefs.Save(); dayNight.ApplyNow(); }
        void ToggleBlood() { blood.SetOn(!blood.On); hud.SetBlood(blood.On); PlayerPrefs.SetInt("summit_blood", blood.On ? 1 : 0); PlayerPrefs.Save(); }

        // ---------------------------------------------------------------- the start page

        /// <summary>
        /// Make the start page and hand it every setting there is, a row each, under the pause menu's four headings.
        /// </summary>
        void BuildStart()
        {
            start = new GameObject("StartPage").AddComponent<StartPage>();
            start.Build(chase.GetComponent<Camera>(), atmo, snow.shader, spray, () => dayNight != null ? dayNight.Darkness : 0f, () => LensFov[lensLevel]);
            start.Clicked = () => audioFx.Play(Sfx.Click);
            start.TimeJumped = () => dayNight.ApplyNow();
            string OnOff(bool on) => on ? "ON" : "OFF";
            int view = start.AddTab("VIEW"), look = start.AddTab("LOOK"), game = start.AddTab("GAME"), sound = start.AddTab("SOUND");
            start.AddRow(view, "VIEW", () => pov ? "FIRST PERSON" : "CHASE CAMERA", d => SetPov(!pov), true);
            start.AddRow(view, "CAMERA DISTANCE", () => pov ? "N/A" : CamName[camLevel], StepCam, false, () => !pov);
            start.AddRow(view, "CAMERA ANGLE", () => pov ? "N/A" : AngleName[angleLevel], StepAngle, false, () => !pov);
            start.AddRow(view, "LENS", () => LensName[lensLevel], StepLens);
            start.AddRow(view, "LENS TYPE", () => LensFX.ModeName[lensMode], d => StepLensType(), true);
            start.AddRow(look, "TIME OF DAY", () => TimeOfDay.LockLabel, StepTime);
            start.AddRow(look, "WEATHER", () => Weather.ChoiceLabel, Weather.StepChoice);
            start.AddRow(look, "FILTER", () => LookGrade.Names[filterLevel], StepFilter);
            start.AddRow(look, "FILTER STRENGTH", () => filterStrength * 10 + "%", StepStrength);
            start.AddRow(look, "HALATION", () => LensFX.HalationName[halation], StepHalation);
            start.AddRow(look, "LODGE (PLAYTEST)", () => Camp.LodgeLayoutShort, d => camp.StepLodgeLayout(d));   // (a playtest switch: the camp's lodge, three ways inside)
            start.AddRow(game, "TURN", () => SteerPrefs.Label, StepTurn);
            start.AddRow(game, "STEERING", () => input.useTilt ? "TILT" : "DRAG", d => ToggleSteer(), true);
            start.AddRow(game, "JUMP", () => JumpName, StepJump);
            start.AddRow(game, "INVERT TILT", () => OnOff(input.invertTilt), d => ToggleInvert(), true, () => input.useTilt);
            start.AddRow(game, "RIDE", () => RideName, StepRide);
            start.AddRow(game, "TREES", () => props.IsRealistic ? "REALISTIC" : "ORIGINAL", d => ToggleTrees(), true);
            start.AddRow(game, "FLASHLIGHT", () => OnOff(DayNight.FlashlightOn), d => ToggleFlashlight(), true);
            start.AddRow(game, "BLOOD", () => OnOff(blood.On), d => ToggleBlood(), true);
            start.AddRow(sound, "MUSIC", () => Mathf.RoundToInt(AudioPrefs.MusicVolume * 10f) * 10 + "%", StepMusic);
            start.AddRow(sound, "VOICE LINES", () => OnOff(SoundMix.VoiceLines), d => ToggleVoice(), true);
        }

        bool titleShown;
        /// <summary>To the start page: the helicopter's view of the whole mountain, with the camp and the run a touch away.</summary>
        void ToTitle()
        {
            if (start == null) BuildStart();
            if (paused) SetPaused(false);
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

        void SetPaused(bool on)
        {
            paused = on;
            hitStopT = -1f; Time.fixedDeltaTime = FixedStep;   // a pause ends any crash slow-mo
            Time.timeScale = on ? 0f : 1f;
            if (!on) { rider.CancelPop(); input.SwallowCurrentPress(); }   // a menu tap is never a jump
            AudioListener.pause = on;
            hud.ShowPause(on);
            hud.SetLens(LensName[lensLevel]);
            ShowView();
            hud.SetPauseSteer(input.useTilt);
            hud.SetPauseJump(JumpName);
            hud.SetPauseTrees(props.IsRealistic);
            if (blood != null) hud.SetBlood(blood.On);
            hud.SetFlashlight(DayNight.FlashlightOn);
            hud.SetVoiceLines(SoundMix.VoiceLines);
            hud.SetTime(TimeOfDay.LockLabel);
            hud.SetMusic(Mathf.RoundToInt(AudioPrefs.MusicVolume * 10f) * 10 + "%");
            hud.SetTurn(SteerPrefs.Label, SteerPrefs.Fraction);
            turnDrag = false;
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

        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 1f / 120f;   // physics in small steps; the rider is interpolated between them
            // after a long frame, catch up at most 50 ms of physics (6 steps), not a third of a second's worth, which only
            // made the next frame long too (set here: the batch tools leave TimeManager.asset changed)
            Time.maximumDeltaTime = 0.05f;
            Physics.simulationMode = SimulationMode.FixedUpdate;   // never left on manual by a test tool
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;
            Slope.Init(Slope.TodaysSeed());
            best = PlayerPrefs.GetInt("summit_best", 0);
            SteerPrefs.Apply();   // (the TURN setting)
            MakeMissingMaterials();
            SetUpWorld();

            input = gameObject.AddComponent<RiderInput>();

            var rg = new GameObject("Rider");
            rider = rg.AddComponent<Rider>();
            rider.input = input;
            _ = rider.Body;   // its physics body exists before the ragdoll is built (as on device; edit-mode tools skip Awake)
            body = rg.AddComponent<RiderBody>();
            body.Build(rider, jacket, pants, board, skin, dark, trail, spray);
            rider.Link += OnLink;
            rider.Crash += OnCrash;
            rider.RoughLanding += OnRough;
            rider.Jumped += () => { if (state == State.Riding) { audioFx.Play(Sfx.Pop); popped = true; } };
            rider.Landed += impact =>
            {
                if (state != State.Riding || impact <= 3f) return;
                if (impact > 11f) audioFx.Play(Sfx.LandHard);
                if (impact > 13f && impact <= 18f && !rider.Crashed) audioFx.Play(Sfx.BuddyClose);   // a big landing ridden away (harder is the ouch)
                hurtShake = Mathf.Max(hurtShake, Mathf.Clamp01((impact - 7f) / 12f) * 0.7f);
                audioFx.Play(Sfx.Land, Mathf.Clamp01(impact / 14f));
            };
            rider.Scraped += hit => { if (state == State.Riding) audioFx.Play(Sfx.Scrape, hit); };
            body.BoneBroke += (limb, at) =>
            {
                if (state != State.Riding) return;   // the bill is closed once the run is over
                // a harsh crack for each kind of break: the neck, the spine, a big bone in the leg, an arm
                audioFx.Play(limb == "Head" ? Sfx.BoneNeck : limb == "Ribs" ? Sfx.BoneBack : limb.EndsWith("leg") ? Sfx.BoneLeg : Sfx.BoneArm);
                hud.Hurt(0.5f);
                hurtShake = 1f;   // (the bill hears the hit itself, with the break: body.Injured)
            };
            body.LimbLost += (limb, at, whole) =>
            {
                // a limb torn off: it goes on the bill, and you get up and ride on without it (RiderBodyLimbs).
                // (the crash it came off in has already counted as a fall)
                if (state != State.Riding) return;
                receipt.Lost(LostPart(limb), whole);
                hud.Hurt(1f); hurtShake = 1f;
                audioFx.Play(Sfx.Bone); audioFx.Play(Sfx.Crash);
                if (audioFx.Screaming) audioFx.ScreamHit();
            };
            // a tree hit and still standing: it shudders and its snow comes down off it (SnowFX, TreeWind)
            rider.HitObstacle += (ob, at, into) => { if (ob.label == "Tree") ShakeTree(ob.GetComponent<Collider>(), into, rider.Body.linearVelocity); };
            body.RagdollStruck += (c, at, into) =>
            {
                var ob = c != null ? c.GetComponentInParent<Obstacle>() : null;
                if (ob != null && ob.label == "Tree" && into > 4f) ShakeTree(c, into, c.transform.position - at);
            };
            body.RagdollHit += (v, isBoard) =>
            {
                if (!isBoard && v > 6f) hurtShake = Mathf.Max(hurtShake, Mathf.Clamp01((v - 6f) / 10f) * 0.8f);
                if (v < 1.5f || Time.time < nextImpactSound) return;
                // mid-tumble: a fresh scream on each big hit, above all against trees, rocks and signs
                if (audioFx.Screaming && rider.Crashed && !isBoard && (v > 14f || (v > 7f && body.RagdollHitObject)))
                    audioFx.ScreamHit();
                // a hard hit into a tree, rock or sign mid-tumble gets its own beat (never the rolling on the snow)
                if (rider.Crashed && !isBoard && state == State.Riding && v > 8f && body.RagdollHitObject)
                    HitStop(0.25f + (v - 8f) / 30f);
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
            Farview.Create(rg.transform, snow, foliage);   // the world's far band and the far field beyond it (Farview.cs)

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
            Hamlets.Create(rg.transform, snow.shader, spray);   // hamlets beside the piste (Hamlets.cs): they stream and rebuild themselves
            wild = new GameObject("Wildlife").AddComponent<Wildlife>();
            wild.rider = rider;
            wild.Build(snow.shader);
            wild.BearSpotted += () => { };   // (the bear is there to see and hear: no caption)
            SkiLift.Create(rg.transform, snow.shader);   // ski lifts beside the run (SkiLift.cs): they stream, and follow a new mountain, by themselves
            rider.GrindStart += f => { if (state == State.Riding) audioFx.Play(f.kind == Course.Kind.Rail ? Sfx.GrindRail : Sfx.GrindBox); };
            // a numbered flag wherever a fall came to rest, for the rest of the run
            flags = new GameObject("FallFlags").AddComponent<FallFlags>();
            flags.Build(jacket != null ? jacket.shader : null);
            body.Injured += inj => { if (state == State.Riding) receipt.Hit(inj); };   // each hit on the receipt (Receipt)
            receipt.Printed += OnBilled;
            body.FallRested += at => { if (state == State.Riding) flags.Plant(at, falls); };
            body.Killed += fell =>
            {
                // off a cliff and into the snow far below: it's over (see RiderBodyFall)
                if (state == State.Hub) { HitStop(1f); audioFx.Play(Sfx.Crash); return; }   // off the camp's edge: just the thump
                if (state != State.Riding) return;
                Vector3 c = body.RagdollCentre;
                rider.StayDown = true; wipeoutTimer = 2.6f; endTitle = "WIPED OUT";
                receipt.Killed(fell, RockFace.IsRock(Slope.Normal(c.x, c.z), c.x, c.z));
                HitStop(1f);   // the body hitting the snow at the foot of the cliff
                audioFx.Play(Sfx.BoneBack); audioFx.Play(Sfx.Crash);
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
                Link(ob.always ? "BEAR BOWLED" : ob.label == "Rock" ? "ROCK SMASH" : "TIMBER!", ob.always ? 500 : 100, LinkKind.Smash);
            };
            // young trees and dead snags hit fast enough snap: the top falls, you go down but carry on (TreeBreak)
            treeBreak = new GameObject("TreeBreak").AddComponent<TreeBreak>();
            treeBreak.Build(props, fx, smasher);
            treeBreak.Snapped += (at, into, byRider) =>
            {
                if (state != State.Riding) return;
                audioFx.Play(Sfx.SmashWood, byRider ? 1f : 0.85f);
                hurtShake = Mathf.Max(hurtShake, byRider ? 0.8f : 0.5f);
            };
            // a thin trunk ridden through (a small tree, a treetop) and branches brushed: you ride on. Heard, felt in the
            // camera, the hero flinching from them (the rest - the snow, the needles, the pieces - is TreeBreak's)
            treeBreak.Broke += (at, into, top) =>
            {
                if (state != State.Riding) return;
                audioFx.Play(Sfx.Branches, 1f);
                audioFx.Play(Sfx.SmashWood, top ? 0.55f : 0.65f);
                hurtShake = Mathf.Max(hurtShake, top ? 0.4f : 0.5f);
                if (!rider.Crashed) body.Flinch(at, 1f);
            };
            treeBreak.Brushed += (at, side, depth) =>
            {
                if (state != State.Riding) return;
                audioFx.Play(Sfx.Branches, depth);
                hurtShake = Mathf.Max(hurtShake, 0.2f + 0.3f * depth);
                if (!rider.Crashed) body.Flinch(at, depth);
            };
            // signs and flags: ridden into (or tumbled into), signs break and flags go down (Breakables)
            breaks = new GameObject("Breakables").AddComponent<Breakables>();
            breaks.Build(rider, fx, smasher);
            breaks.Broke += (b, crashed, invincible, thrown) =>
            {
                if (state != State.Riding) return;
                bool sign = b.kind == Breakable.Kind.Sign;
                if (sign) audioFx.Play(Sfx.SmashWood, crashed || invincible ? 1f : 0.8f);
                else if (b.kind == Breakable.Kind.Fence) { audioFx.Play(Sfx.SmashWood, 0.55f); if (!thrown) hurtShake = Mathf.Max(hurtShake, 0.2f); }
                else if (b.kind == Breakable.Kind.Banner) audioFx.Play(Sfx.ClothRip, thrown ? 0.5f : Mathf.Clamp01(rider.Speed / 25f));
                else audioFx.Play(Sfx.Clack, 0.7f);
                if (!sign || thrown) return;
                hurtShake = Mathf.Max(hurtShake, crashed ? 0.8f : 0.45f);
                if (invincible) Link("SIGN SMASH", 100, LinkKind.Smash);
            };

            // a sign (or a banner's stake) ridden into: you stay up, and it hurts. The camera shakes, the hero flinches
            // from it, and it goes on the receipt while you ride: one line that pops up and folds into the corner's total
            breaks.RiderHit += (kind, speed, at, part, cost) =>
            {
                if (state != State.Riding) return;
                float k = Mathf.Clamp01(speed / 30f);
                hurtShake = Mathf.Max(hurtShake, kind == Breakable.Kind.Sign ? 0.4f + 0.45f * k : 0.25f);
                body.Flinch(at, 0.5f + 0.5f * k);
                if (kind == Breakable.Kind.Sign && speed > 12f) audioFx.Play(Sfx.Grunt);
                receipt.RideHit(kind == Breakable.Kind.Sign, part, speed, cost);
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
            HookPoints();

            var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            post = vol != null ? vol.profile : null;
            lensFx = cam.gameObject.AddComponent<LensFX>();
            lensFx.Build(cam);
            pov = PlayerPrefs.GetInt(ChaseCamera.PovPref, 0) == 1;
            chase.pov = pov; chase.body = body; chase.lensFx = lensFx;
            ApplyCamera();
            ApplyLook();
            bool tube = PlayerPrefs.GetInt("summit_tube", 0) == 1;
            rider.Tube = tube; body.SetTube(tube); ShowRider();
            rider.Bounced += into => { if (state != State.Riding) return; body.TubeKick(into); audioFx.Play(Sfx.Thump, Mathf.Clamp01(into / 12f)); };
            BuildMeta();
            dayNight.Hook(rider, fx, camp, spray, trail);
            weather = Weather.Build(rider, camp, cam, atmo, fx, snow, audioFx, () => start);   // the weather on top of the time of day (Weather.cs)
            NightLights.Build(rider, cam, dayNight, atmo, spray);   // floodlights down the mountain at dusk and night (NightLights.cs)
            PisteKit.Build(rider);   // piste poles, rope lines, snow guns, the patrol hut, trail signs (PisteKit.cs, TrailSigns.cs)
            ToHub();
            // the game opens on the start page (StartPage.cs), over the camp it has just set up. Not for the
            // device-test autoplay, which carries straight on into a run, nor for the editor's test tools, which
            // call Awake themselves outside play mode and expect to find themselves in the camp
            if (!autoplay && Application.isPlaying) ToTitle();
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
            // the closet's studio, and the rider repainted for each try-on (ApplyOutfit shows meta.TryOn while it's open)
            Wardrobe.Classic = body.Hero == null;
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
            // (the physics follows what the body could actually put on: without the gear's files it stays a snowboard)
            if (rider.Ride != Progress.Ride) { body.SetRide(Progress.Ride); rider.SetRide(body.Ride); if (hud != null) ShowRider(); }
        }

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
            hud.SetPrompt("");
            if (paused) SetPaused(false);
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
                case State.Hub:
                    if (autoplay)
                    {
                        // device testing: a look at the camp, then on to the run
                        float before = readyT; readyT += Time.unscaledDeltaTime;
                        if (before < 2.5f && readyT >= 2.5f) AutoShot("auto-hub.png");
                        if (readyT >= 3.5f) { readyT = 0f; ToReady(false); }
                    }
                    else if (camp.Step(dt, meta)) { audioFx.Play(Sfx.Play); ToReady(false); input.SwallowCurrentPress(); }
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
                    if (go == 1) ToHub(false); else { audioFx.Play(Sfx.Play); ToReady(false); }
                    input.SwallowCurrentPress();
                    break;
                }

                case State.Ready:
                    traceT = 0f;
                    if (autoplay && (readyT += dt) > 3f) { readyT = 0f; ToRiding(); }
                    else if (autoplay && readyT > 2f && readyT - dt <= 2f) AutoShot("auto-ready.png");
                    else if (input.AnyPress() && ModeTapped()) { }
                    else if (input.AnyPress() && RiderTapped()) { }
                    else if (input.AnyPress() && meta.Hit(RiderInput.PointerPosition()) == "camp") { audioFx.Play(Sfx.Click); ToHub(false); }
                    else if (input.AnyPress() && (Tapped(true) || SpacePressed())) { audioFx.Play(Sfx.Play); ToRiding(); }
                    break;

                case State.Riding:
                    if (paused)
                    {
                        // the TURN bar, slid: it follows the finger, a click at each stop it passes
                        if (turnDrag)
                        {
                            if (!RiderInput.PointerHeld()) turnDrag = false;
                            else
                            {
                                int k = Mathf.RoundToInt(hud.TurnBarAt(RiderInput.PointerPosition()) * (SteerPrefs.Stops.Length - 1));
                                if (k != SteerPrefs.Stop) { audioFx.Play(Sfx.Click); SetTurnStop(k); }
                            }
                        }
                        if (input.AnyPress())
                        {
                            var pp = RiderInput.PointerPosition();
                            if (hud.OnResume(pp)) { audioFx.Play(Sfx.Click); SetPaused(false); }
                            else if (hud.OnTurnMinus(pp) || hud.OnTurnPlus(pp)) { audioFx.Play(Sfx.Click); StepTurn(hud.OnTurnPlus(pp) ? 1 : -1); }
                            else if (hud.OnTurnBar(pp))
                            {
                                turnDrag = true; audioFx.Play(Sfx.Click);
                                SetTurnStop(Mathf.RoundToInt(hud.TurnBarAt(pp) * (SteerPrefs.Stops.Length - 1)));
                            }
                            else if (hud.OnPauseTab(pp)) audioFx.Play(Sfx.Click);
                            else if (hud.OnHalationMinus(pp) || hud.OnHalationPlus(pp)) { audioFx.Play(Sfx.Click); StepHalation(hud.OnHalationPlus(pp) ? 1 : -1); }
                            else if (hud.OnMusicMinus(pp) || hud.OnMusicPlus(pp)) { audioFx.Play(Sfx.Click); StepMusic(hud.OnMusicPlus(pp) ? 1 : -1); }
                            else if (hud.OnPausePov(pp)) { audioFx.Play(Sfx.Click); SetPov(!pov); }
                            else if (pov && (hud.OnCamMinus(pp) || hud.OnCamPlus(pp) || hud.OnAngMinus(pp) || hud.OnAngPlus(pp))) { }   // the chase camera's rows: greyed out
                            else if (hud.OnCamMinus(pp) || hud.OnCamPlus(pp)) { audioFx.Play(Sfx.Click); StepCam(hud.OnCamPlus(pp) ? 1 : -1); }
                            else if (hud.OnAngMinus(pp) || hud.OnAngPlus(pp)) { audioFx.Play(Sfx.Click); StepAngle(hud.OnAngPlus(pp) ? 1 : -1); }
                            else if (hud.OnLensMinus(pp) || hud.OnLensPlus(pp)) { audioFx.Play(Sfx.Click); StepLens(hud.OnLensPlus(pp) ? 1 : -1); }
                            else if (hud.OnLensTypeMinus(pp) || hud.OnLensTypePlus(pp)) { audioFx.Play(Sfx.Click); StepLensType(); }
                            else if (hud.OnFilterMinus(pp) || hud.OnFilterPlus(pp)) { audioFx.Play(Sfx.Click); StepFilter(hud.OnFilterPlus(pp) ? 1 : -1); }
                            else if (hud.OnStrengthMinus(pp) || hud.OnStrengthPlus(pp)) { audioFx.Play(Sfx.Click); StepStrength(hud.OnStrengthPlus(pp) ? 1 : -1); }
                            else if (hud.OnTimeMinus(pp) || hud.OnTimePlus(pp)) { audioFx.Play(Sfx.Click); StepTime(hud.OnTimePlus(pp) ? 1 : -1); }
                            else if (hud.OnPauseSteer(pp)) { audioFx.Play(Sfx.Click); ToggleSteer(); }
                            else if (hud.OnPauseJump(pp)) { audioFx.Play(Sfx.Click); StepJump(1); }
                            else if (hud.OnPauseTrees(pp)) { audioFx.Play(Sfx.Click); ToggleTrees(); }
                            else if (hud.OnPauseVoiceLines(pp)) { audioFx.Play(Sfx.Click); ToggleVoice(); }
                            else if (hud.OnPauseFlashlight(pp)) { audioFx.Play(Sfx.Click); ToggleFlashlight(); }
                            else if (hud.OnPauseBlood(pp)) { audioFx.Play(Sfx.Click); ToggleBlood(); }
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
                    if (wipeoutTimer > 0f && !slide.Caught) { if (!body.RagdollMoving) wipeoutTimer -= dt; if (wipeoutTimer <= 0f) { ToEnd(endTitle, false); break; } }
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
            audioFx.Set(new RideSound
            {
                speed = rider.Speed / 36f, carve = rider.Tube ? 0f : rider.Carve, skid = rider.Tube ? 0.15f * Mathf.Clamp01(rider.Speed / 20f) : rider.Skid, rough = Slope.Rough(rpos.x, rpos.z),
                avalanche = riding ? slide.Closeness(rider) : 0f, pan = rider.Steer,
                muffle = state == State.Buried ? (endBuried ? 1f : 0.45f) : rider.Crashed ? 0.3f : 0f,
                onSnow = onSnow && !rider.Grinding, air = riding && rider.Airborne, riding = riding,
                camp = state == State.Hub || state == State.Title, ended = state == State.Buried,
                boost = riding && rider.BoostTime > 0f,
                grind = riding && rider.Grinding ? (rider.GrindFeature.kind == Course.Kind.Rail ? 1 : 2) : 0,
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
            combo.Step(dt, rider.Airborne || rider.Grinding);
            // the fall's receipt: what followed is billed once the body has stopped (or the rider is up); up again,
            // it tears off into the corner's total
            if (rider.Crashed && receipt.Open)
            {
                body.ReportBreaks();   // (a bone the wipeout broke with no hit to carry it: RiderBodyInjury)
                downT += dt;
                if (!receipt.Served && !slide.Caught && (rider.CanGetUp || (downT > 1f && !body.RagdollMoving))) receipt.Rest();   // (under the avalanche: billed at the end, buried)
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
            float cx = Slope.Centre(Slope.PadLip);
            // the gate, where every run starts (StartGate.cs)
            StartGate.Build(root, cx, (c, smooth) =>
            {
                string k = "c" + c + smooth;
                if (!gateMats.TryGetValue(k, out var m)) { m = new Material(sh); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); gateMats[k] = m; }
                return m;
            });
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
        /// <summary>The time scale the hit-stop has set right now (1 when none is running).</summary>
        public static float HitStopScaleNow => Time.timeScale;

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

        // the "nooo": down a crevasse it fades with the depth; off a cliff, a short one the moment you are clearly
        // falling far below where you left the snow
        bool cliffNooo, crevNooo;
        // the warnings, a beat before it happens: "don't go down there" with a deadly drop or a crevasse ahead on the
        // line you're riding, "lookout" with a tree, rock or sign right in the way
        float nextWarnCheck;
        void WatchWarn()
        {
            if (state != State.Riding || audioFx == null || rider.Crashed || rider.Airborne || paused) return;
            if (Time.unscaledTime < nextWarnCheck) return;
            nextWarnCheck = Time.unscaledTime + 0.25f;
            Vector3 p = rider.transform.position, v = rider.Body.linearVelocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float sp = flat.magnitude;
            if (sp < 8f) return;
            Vector3 dir = flat / sp;
            for (float t = 0.8f; t <= 1.6f; t += 0.4f)
            {
                Vector3 q = p + dir * sp * t;
                if (Deadly.Below(q.x, q.z) || Course.CrevasseNear(q.x, q.z, 1.5f)) { audioFx.Play(Sfx.BuddyDeep); return; }
            }
            if (Physics.SphereCast(p + Vector3.up * 0.8f, 0.5f, dir, out RaycastHit hit, sp * 0.9f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<Obstacle>() != null)
                audioFx.Play(Sfx.BuddyWarn);
        }

        // the woahs: high off a jump (once per air), and now and then at full tilt
        bool airWoah; float nextSpeedWoah;
        void WatchWoah()
        {
            if (state != State.Riding || audioFx == null || rider.Crashed) { airWoah = false; return; }
            if (!rider.Airborne) airWoah = false;
            else if (!airWoah && rider.AboveGround > 5f) { airWoah = true; audioFx.Play(Sfx.BuddyWoah); }
            if (!rider.Airborne && rider.Speed * 3.6f > 150f && Time.unscaledTime > nextSpeedWoah)
            {
                nextSpeedWoah = Time.unscaledTime + 25f;
                if (Random.value < 0.5f) audioFx.Play(Sfx.BuddyWoah);
            }
        }
        void WatchDrop()
        {
            if (audioFx == null) return;
            if (state != State.Riding && state != State.Buried) return;
            Vector3 c = rider.Crashed ? body.RagdollCentre : rider.transform.position;
            bool overCrevasse = Course.CrevasseNear(c.x, c.z, 2.5f);
            if (rider.Crashed)
            {
                audioFx.NoooDepth(body.RagdollBelowSnow);
                // into a crevasse, however you got there: only the "nooo", never a scream over it
                if (!crevNooo && overCrevasse && body.RagdollBelowSnow > 0.8f)
                {
                    crevNooo = true;
                    audioFx.StopScreaming(true);
                    if (!audioFx.NoooPlaying) audioFx.Play(Sfx.Nooo);
                }
            }
            if (state != State.Riding) return;
            if (!rider.Airborne && !rider.Crashed) { cliffNooo = false; return; }
            if (cliffNooo || crevasseWatch || overCrevasse || audioFx.NoooPlaying) return;
            // off a cliff: only once the snow under you is further down than anyone survives (a jump, however big,
            // comes down onto a slope that isn't that far below, so it never cries out)
            Vector3 d = rider.DropFrom;
            float ground = Slope.DrawnHeight(c.x, c.z);
            float toFall = d.y - ground - Slope.Fall * Mathf.Max(0f, c.z - d.z);
            float falling = rider.Crashed ? -1f : rider.Body.linearVelocity.y;
            if (toFall > Rider.DeathFall && c.y - ground > 3f && falling < -1f) { cliffNooo = true; audioFx.StopScreaming(true); audioFx.NoooFor(1.5f); }
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
