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
        TerrainStreamer terrain;
        PropField props;
        ChaseCamera chase;
        Hud hud;
        WindAudio audioFx;
        readonly Avalanche slide = new Avalanche();

        int trickPoints, chainCount;
        float chainTimer, stateTimer, best;

        int Score => Mathf.RoundToInt(rider.Distance) + trickPoints;
        float Mult => 1f + 0.5f * chainCount;

        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;
            MountainField.Init(MountainField.TodaysSeed());
            best = PlayerPrefs.GetInt("summit_best", 0);
            MakeMissingMaterials();
            SetUpWorld();

            input = gameObject.AddComponent<RiderInput>();

            var rg = new GameObject("Rider");
            rider = rg.AddComponent<Rider>();
            rider.input = input;
            rider.Build(jacket, pants, board, skin, dark, trail, spray);
            rider.Trick += OnTrick;
            rider.Crash += OnCrash;
            rider.RoughLanding += OnRough;

            var tg = new GameObject("Terrain");
            terrain = tg.AddComponent<TerrainStreamer>();
            terrain.snowMaterial = snow;
            terrain.target = rg.transform;

            var pg = new GameObject("Trees");
            props = pg.AddComponent<PropField>();
            props.foliage = foliage;
            props.bark = bark;
            props.target = rg.transform;

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

            var hg = new GameObject("HUD");
            hud = hg.AddComponent<Hud>();
            hud.Build();

            var ag = new GameObject("Audio");
            ag.AddComponent<AudioSource>();
            audioFx = ag.AddComponent<WindAudio>();

            ToReady();
        }

        // ---------------------------------------------------------------- world

        void SetUpWorld()
        {
            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var lg = new GameObject("Sun");
                var l = lg.AddComponent<Light>();
                l.type = LightType.Directional;
                l.color = new Color(1f, 0.95f, 0.86f);
                l.intensity = 1.1f;
                l.shadows = LightShadows.Soft;
                lg.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0052f;
            RenderSettings.fogColor = new Color(0.80f, 0.87f, 0.95f);
            if (RenderSettings.skybox == null)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.88f);
                RenderSettings.ambientEquatorColor = new Color(0.70f, 0.76f, 0.84f);
                RenderSettings.ambientGroundColor = new Color(0.55f, 0.58f, 0.66f);
            }
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
            float x = MountainField.DriftX(0f);
            rider.ResetAt(x, 0f);
            slide.Reset(0f);
            trickPoints = 0; chainCount = 0; chainTimer = 0f;
            terrain.Prewarm();
            props.Prewarm();
            chase.Snap();
            hud.SetWhiteout(0f);
            hud.Show("SUMMIT", "Best " + Mathf.RoundToInt(best).ToString("N0"),
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

        void ToBuried()
        {
            state = State.Buried;
            stateTimer = 0f;
            rider.Running = false;
            int s = Score;
            bool record = s > best;
            if (record) { best = s; PlayerPrefs.SetInt("summit_best", s); PlayerPrefs.Save(); }
            hud.Show("BURIED", "Score " + s.ToString("N0"),
                     (record ? "NEW BEST  " : "Best ") + Mathf.RoundToInt(best).ToString("N0") +
                     "   ·   " + Mathf.RoundToInt(rider.Distance) + " m");
            hud.SetButtons(true, false, "PLAY AGAIN");
        }

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
                hud.SetDiag((diagFrames / diagSum).ToString("0") + " fps  worst " + (diagMax * 1000f).ToString("0") +
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
                    else if (input.AnyPress() && (Tapped(true) || SpacePressed())) ToRiding();
                    break;

                case State.Riding:
                    if (input.AnyPress() && hud.OnRestart(RiderInput.PointerPosition())) { ToReady(); input.SwallowCurrentPress(); break; }
                    slide.Step(dt, rider);
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

            bool stick = state == State.Riding && !input.useTilt && input.StickActive;
            hud.SetStick(stick, input.StickOrigin, input.StickPos, Mathf.Max(60f, Screen.width * input.stickRadiusFrac));

            float gap = state == State.Ready ? 120f : slide.Gap(rider);
            float close = Mathf.Clamp01(1f - gap / 45f);
            hud.SetRun(rider.Speed * 3.6f, Score, Mult, gap, 160f, state == State.Riding);
            hud.SetWhiteout(state == State.Buried ? Mathf.Min(0.85f, stateTimer * 2f) : close * close * 0.5f);
            chase.shake = state == State.Riding ? close * close + (rider.Crashed ? 0.2f : 0f) : 0f;
            audioFx.Set(rider.Speed / 40f, rider.Airborne ? 0f : Mathf.Abs(rider.Steer), close, state == State.Buried ? 0.3f : 1f);
        }

        bool ModeTapped()
        {
            if (!hud.OnMode(RiderInput.PointerPosition())) return false;
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
        }

        void OnCrash(string why)
        {
            if (state != State.Riding) return;
            chainCount = 0;
            hud.Flash(why, "the slide is gaining", 1.3f);
#if UNITY_IOS || UNITY_ANDROID
            Handheld.Vibrate();
#endif
        }
    }
}
