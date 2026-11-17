using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Builds the whole game at start-up and runs the loop:
    /// ready → riding → buried → ready. Drop it on one empty GameObject.
    /// The Summit ▸ Build Scene menu does that for you and makes the materials.
    /// </summary>
    public class SummitGame : MonoBehaviour
    {
        [Header("Materials (made by Summit ▸ Build Scene; plain ones are made if empty)")]
        public Material snow;
        public Material foliage, bark;
        public Material jacket, pants, board, skin, dark;
        public Material trail, spray;

        enum State { Ready, Riding, Buried }
        State state;

        Rider rider;
        RiderInput input;
        SlopeStreamer terrain;
        Forest props;
        RiderBody body;
        ChaseCamera chase;
        Hud hud;
        SoundMix audioFx;
        SnowFX fx;
        // three falls a run; the third one ends it
        public const int MaxFalls = 3;
        int falls;
        float wipeoutTimer, hurtShake;
        string fallsNote = "", endTitle = "WIPED OUT";
        bool endBuried;
        readonly Avalanche slide = new Avalanche();

        PowerUps power;
        CourseDressing dressing;
        Wildlife wild;
        int trickPoints, chainCount;
        float chainTimer, stateTimer, best, runTime;

        int Score => Mathf.RoundToInt(rider.Distance) + trickPoints;
        float Mult => 1f + 0.5f * chainCount;

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
                if (state != State.Riding && state != State.Buried) return;
                audioFx.Play(Sfx.Bone);
                hud.Hurt(0.5f);
                hud.Flash("CRACK!", limb + " broken  ·  " + fallsNote, 1.4f);
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
                nextImpactSound = Time.time + 0.07f;
                audioFx.Play(isBoard ? Sfx.Clack : Sfx.Thump, Mathf.Clamp01(v / 10f));
            };

            var tg = new GameObject("Terrain");
            terrain = tg.AddComponent<SlopeStreamer>();
            terrain.snowMaterial = snow;
            terrain.target = rg.transform;

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

            var cam = Camera.main;
            if (cam == null)
            {
                var cg = new GameObject("Main Camera");
                cg.tag = "MainCamera";
                cam = cg.AddComponent<Camera>();
                cg.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 900f;
            chase = cam.gameObject.GetComponent<ChaseCamera>();
            if (chase == null) chase = cam.gameObject.AddComponent<ChaseCamera>();
            chase.rider = rider;

            fx = new GameObject("SnowFX").AddComponent<SnowFX>();
            fx.Build(cam, spray);
            body.fx = fx;

            var atmo = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atmo.Build(cam, rider, spray);

            var hg = new GameObject("HUD");
            hud = hg.AddComponent<Hud>();
            hud.Build();

            var ag = new GameObject("Audio");
            audioFx = ag.AddComponent<SoundMix>();
            audioFx.Build();
            audioFx.Track(wild);

            ToReady();
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
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0056f;
            RenderSettings.fogColor = new Color(0.80f, 0.86f, 0.94f);
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

        void ToReady()
        {
            state = State.Ready;
            // a new mountain every run: new pitches, drops, kickers, crevasses and trees
            Slope.Init(Slope.RandomSeed());
            terrain.Rebuild();
            props.Rebuild();
            dressing.Rebuild();
            RebuildGate();
            float x = Slope.Centre(Slope.PadLip);
            rider.ResetAt(x, Slope.StartZ);
            body.ResetVisual();
            slide.Reset(Slope.StartZ);
            trickPoints = 0; chainCount = 0; chainTimer = 0f; runTime = 0f;
            power.ResetRun();
            wild.ResetRun();
            terrain.Prewarm();
            props.Prewarm();
            dressing.Prewarm();
            chase.Snap();
            hud.SetWhiteout(0f);
            falls = 0; wipeoutTimer = 0f; hurtShake = 0f;
            if (fx != null) fx.Clear();
            hud.Show("", "Best " + Mathf.RoundToInt(best).ToString("N0"),
                Application.isMobilePlatform ? "Drag a finger left / right to steer  ·  add a second finger to tuck  ·  lift it to jump"
                              : "A / D to steer  ·  hold Space to tuck  ·  let go to jump");
            hud.SetButtons(true, false, "PLAY");
            hud.SetMode(input.useTilt);
        }

        void ToRiding()
        {
            state = State.Riding;
            input.Calibrate();
            input.SwallowCurrentPress();
            rider.Begin();
            hud.Clear();
            hud.SetButtons(false, true, "");
            hud.Flash("GO", "the mountain is moving", 1.2f);
        }

        void ToBuried() => ToEnd("BURIED", true);

        void ToEnd(string title, bool buried)
        {
            state = State.Buried;
            wipeoutTimer = 0f;
            endBuried = buried;
            if (buried) audioFx.Play(Sfx.Buried);   // the world goes muffled under the snow (see the mix below)
            stateTimer = 0f;
            rider.Stop();
            int s = Score;
            bool record = s > best;
            if (record) { best = s; PlayerPrefs.SetInt("summit_best", s); PlayerPrefs.Save(); }
            hud.Show(title, "Score " + s.ToString("N0"),
                     (record ? "NEW BEST  " : "Best ") + Mathf.RoundToInt(best).ToString("N0") +
                     "   ·   " + Mathf.RoundToInt(rider.Distance) + " m   ·   " + Hud.FormatTime(runTime));
            hud.SetButtons(true, false, "PLAY AGAIN");
        }

        const bool showDiag = false;
        float diagT, diagSum, diagMax; int diagFrames, gc0;
        void Diagnostics()
        {
            float r = Time.unscaledDeltaTime;
            diagFrames++; diagSum += r; if (r > diagMax) diagMax = r; diagT += r;
            if (r > 0.034f)
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
            switch (state)
            {
                case State.Ready:
                    if (input.AnyPress() && ModeTapped()) { }
                    else if (input.AnyPress() && (Tapped(true) || SpacePressed())) { audioFx.Play(Sfx.Play); ToRiding(); }
                    break;

                case State.Riding:
                    if (input.AnyPress() && hud.OnRestart(RiderInput.PointerPosition())) { audioFx.Play(Sfx.Click); ToReady(); input.SwallowCurrentPress(); break; }
                    runTime += dt;
                    slide.Step(dt, rider);
                    if (wipeoutTimer > 0f) { if (!body.RagdollMoving) wipeoutTimer -= dt; if (wipeoutTimer <= 0f) { ToEnd(endTitle, false); break; } }
                    if (chainCount > 0)
                    {
                        chainTimer -= dt;
                        if (chainTimer <= 0f) chainCount = 0;
                    }
                    if (slide.Gap(rider) <= 0f) ToBuried();
                    break;

                case State.Buried:
                    if (stateTimer > 1.2f && input.AnyPress() && ModeTapped()) { }
                    else if (stateTimer > 1.2f && input.AnyPress() && (Tapped(true) || SpacePressed()))
                    {
                        ToReady();
                        input.SwallowCurrentPress();
                    }
                    break;
            }

            // no stick graphic: the thumb steers the rider directly and the rider's lean shows the steering
            hud.SetStick(false, input.StickOrigin, input.StickPos, 1f);

            float gap = state == State.Ready ? 120f : slide.Gap(rider);
            float close = Mathf.Clamp01(1f - gap / 45f);
            hud.SetRun(rider.Speed * 3.6f, Score, Mult, gap, 160f, state == State.Riding);
            hud.SetPower(runTime, rider.Shields, rider.BoostTime / 4f, state == State.Riding);
            hud.SetWhiteout(state == State.Buried ? Mathf.Min(0.85f, stateTimer * 2f) : close * close * 0.5f);
            hurtShake = Mathf.MoveTowards(hurtShake, 0f, dt * 2.5f);
            chase.shake = (state == State.Riding ? close * close + (rider.Crashed ? 0.2f : 0f) : 0f) + hurtShake * hurtShake * 0.8f;
            hud.SetFalls(MaxFalls - falls, MaxFalls, state == State.Riding);
            bool onSnow = state == State.Riding && !rider.Airborne && !rider.Crashed;
            bool riding = state == State.Riding;
            Vector3 rpos = rider.transform.position;
            audioFx.Set(new RideSound
            {
                speed = rider.Speed / 36f, carve = rider.Carve, skid = rider.Skid, rough = Slope.Rough(rpos.x, rpos.z),
                avalanche = riding ? close : 0f, pan = rider.Steer,
                muffle = state == State.Buried ? (endBuried ? 1f : 0.45f) : rider.Crashed ? 0.3f : 0f,
                onSnow = onSnow && !rider.Grinding, air = riding && rider.Airborne, riding = riding,
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
                rider.AddBoost(3f);
                hud.Flash("SUMMIT DEW", "speed boost", 0.9f);
                audioFx.Play(Sfx.Soda);
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
        void RebuildGate()
        {
            if (gateRoot != null) { if (Application.isPlaying) Destroy(gateRoot.gameObject); else DestroyImmediate(gateRoot.gameObject); }
            BuildStartGate();
        }

        void BuildStartGate()
        {
            var sh = snow.shader;
            Material M(Color c) { var m = new Material(sh); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.3f); return m; }
            var red = M(new Color(0.85f, 0.12f, 0.10f)); var white = M(new Color(0.95f, 0.95f, 0.97f)); var wood = M(new Color(0.45f, 0.30f, 0.18f));
            var root = new GameObject("StartGate").transform;
            gateRoot = root;
            float cx = Slope.Centre(Slope.PadLip), gz = Slope.PadLip - 36f;   // well ahead of where you wait, so the whole banner is in view
            float gy = Slope.DrawnHeight(cx, gz);
            for (int sgn = -1; sgn <= 1; sgn += 2)
                Block(PrimitiveType.Cube, root, new Vector3(cx + sgn * 4.2f, gy + 2.2f, gz), new Vector3(0.35f, 4.4f, 0.35f), red);
            // the banner: a printed vinyl strip in a frame, hung between the posts
            Block(PrimitiveType.Cube, root, new Vector3(cx, gy + 4.3f, gz + 0.06f), new Vector3(8.6f, 1.2f, 0.08f), red);
            var banner = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(banner.GetComponent<Collider>());
            banner.transform.SetParent(root, false);
            banner.transform.SetPositionAndRotation(new Vector3(cx, gy + 4.3f, gz - 0.01f), Quaternion.identity);   // a quad shows its face to -Z: up the hill, at the rider
            banner.transform.localScale = new Vector3(8.4f, 1.05f, 1f);
            banner.GetComponent<MeshRenderer>().sharedMaterial = CourseDressing.Sign(snow.shader, "Signs/BannerSummit");
            // flags lining the lip and a wooden kicker edge
            for (int i = 0; i < 6; i++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float z = Slope.PadLip - 2f - i * 6f, x = cx + sgn * (Slope.PadHalf - 1f);
                    float y = Slope.DrawnHeight(x, z);
                    Block(PrimitiveType.Cylinder, root, new Vector3(x, y + 0.9f, z), new Vector3(0.06f, 0.9f, 0.06f), white);
                    Block(PrimitiveType.Cube, root, new Vector3(x - sgn * 0.3f, y + 1.55f, z), new Vector3(0.6f, 0.4f, 0.03f), (i % 2 == 0) ? red : white);
                }
            float ly = Slope.DrawnHeight(cx, Slope.PadLip - 0.3f);
            Block(PrimitiveType.Cube, root, new Vector3(cx, ly - 0.15f, Slope.PadLip - 0.2f), new Vector3(2f * Slope.PadHalf - 4f, 0.35f, 0.3f), wood);
        }

        static void Block(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
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

        void OnCrash(string why)
        {
            if (state != State.Riding) return;
            chainCount = 0;
            falls++;
            int left = MaxFalls - falls;
            fallsNote = left <= 0 ? "that's three" : left == 1 ? "1 fall left" : left + " falls left";
            hud.Flash(why, fallsNote, 1.3f);
            if (why.StartsWith("Crevasse")) { wipeoutTimer = 1.2f; endTitle = "LOST IN A CREVASSE"; hud.Flash("CREVASSE!", "you fell in", 2f); }
            else if (left <= 0) { wipeoutTimer = 0.9f; endTitle = "WIPED OUT"; }   // let the tumble play out, then it's over
            if (why.StartsWith("Tree")) { audioFx.Play(Sfx.HitTree); audioFx.Play(Sfx.Crash, 0.6f); }
            else if (why.StartsWith("Rock")) { audioFx.Play(Sfx.HitRock); audioFx.Play(Sfx.Crash, 0.6f); }
            else if (why.StartsWith("Crevasse")) audioFx.Play(Sfx.Crevasse);
            else if (why.StartsWith("Bear")) { audioFx.Play(Sfx.Crash); audioFx.Play(Sfx.BearRoar); }
            else audioFx.Play(Sfx.Crash);
#if UNITY_IOS || UNITY_ANDROID
            Handheld.Vibrate();
#endif
        }
    }
}
