using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// The first screen: a helicopter circling the whole of Mt. Vertigo from far off, the game's name, two ways in
    /// (the camp, or straight to the start gate) and one control that opens every setting.
    ///
    /// The view is drawn live, not played from a file: the game's own camera flies the circle and sees only the far
    /// view's layer (FarMountain: this mountain's real run on the whole mountain of the backdrop photograph, the
    /// country round it and drifts of mist), so it shows the mountain you are about to ride, in the light of the
    /// time of day, through your own lens and filter, and adds nothing to the app's size. The flight is a pilot's,
    /// not a turntable's: the circle is wider where the mountain is, the height and distance wander, the aim floats
    /// and the horizon rolls a little; a rotor and the wind sit quietly under the music.
    ///
    /// The page's parts come up after the view has had a moment alone. Choosing a way in pushes the lens in on the
    /// summit and into cloud, and the game changes scene behind the white. The settings are rows handed over by
    /// SummitGame (AddRow): the very setters the pause menu uses, so one change shows in both.
    /// Hit-tested by hand like the rest of the game's menus (no EventSystem).
    /// </summary>
    public class StartPage : MonoBehaviour
    {
        // ---- the flight
        public const float Fov = 38f;              // degrees top to bottom: a long lens
        public const float OrbitSeconds = 380f;    // once round
        public const float StartAzimuth = 12f;     // degrees round from straight down the valley: the run's face, a little from its right
        public const float Fill = 1.18f;           // how much of the frame's width the massif takes
        public const float Altitude = 1150f;       // metres above the valley floor
        public const float AimHeight = 1480f;      // the point looked at, above the floor
        const float NearClip = 30f;

        // ---- the page
        const float ViewAlone = 2.6f, FadeIn = 1.5f;   // seconds the view has to itself, then how long the parts take to come up
        const float LeaveSeconds = 1.5f, CoverOut = 0.9f;
        const float SheetHeight = 900f, RowHeight = 104f;

        public bool Showing { get; private set; }
        public System.Action Clicked;
        /// <summary>The flight's clock, seconds (the test tools set it to look from anywhere round the circle).</summary>
        public float Clock;

        Camera cam;
        Atmosphere atmo;
        FarMountain far;
        Shader lit;
        Material soft;
        System.Func<float> darkness;
        System.Func<float> lensFov;   // the player's LENS setting, as extra degrees (the view takes a share of it)

        int keptMask; float keptFov, keptNear, keptFar; bool kept;
        Transform band; bool bandWas;

        public void Build(Camera camera, Atmosphere atmosphere, Shader litShader, Material softParticle, System.Func<float> dark, System.Func<float> lens)
        {
            cam = camera; atmo = atmosphere; lit = litShader; soft = softParticle; darkness = dark; lensFov = lens;
            far = new GameObject("FarMountain").AddComponent<FarMountain>();
            far.transform.SetParent(transform, false);
            far.gameObject.SetActive(false);
            BuildUi();
            BuildSound();
        }

        // ---------------------------------------------------------------- showing and hiding

        /// <summary>Take the camera up over the mountain. `first`: the game has just opened (the view gets longer alone).</summary>
        public void Show(bool first)
        {
            far.gameObject.SetActive(true);
            far.Build(lit);
            BuildClouds();
            if (!kept)
            {
                keptMask = cam.cullingMask; keptFov = cam.fieldOfView; keptNear = cam.nearClipPlane; keptFar = cam.farClipPlane;
                kept = true;
            }
            cam.cullingMask = 1 << FarMountain.Layer;
            cam.nearClipPlane = NearClip;
            cam.farClipPlane = far.Reach + 20000f;
            // the photo of the range beyond stays out of this view: the mountain being circled is that mountain, and
            // it would stand on the horizon as its own twin (it is on another layer and is not drawn here; hidden
            // as well, so that stays true whatever else changes)
            band = atmo != null ? atmo.PhotoBand : null;
            if (band != null) { bandWas = band.gameObject.activeSelf; band.gameObject.SetActive(false); }
            Showing = true;
            // the time-lapse picks up from the time the game is showing, so nothing jumps
            lapseT = LapseTimeOf(TimeOfDay.Own); lapsing = true;
            TimeOfDay.Shown = LapseClock(lapseT);
            age = first ? 0f : 1.4f;
            leaving = 0; leaveT = 0f; sheetOpen = false; sheet = 0f; pressed = null;
            page.gameObject.SetActive(true);
            ShowTab(0);
            Refresh();
            far.Shade(SunDir(), 0f, -1f);
            Frame(0f);
        }

        /// <summary>Hand the camera back as it was found.</summary>
        public void Hide()
        {
            if (!Showing) return;
            Showing = false; leaving = 0; leaveT = 0f; sheetOpen = false; sheet = 0f;
            if (kept)
            {
                cam.cullingMask = keptMask; cam.fieldOfView = keptFov; cam.nearClipPlane = keptNear; cam.farClipPlane = keptFar;
                kept = false;
            }
            if (band != null) { band.gameObject.SetActive(bandWas); band = null; }
            // back to the game's own time, at once (the cloud is closed over the screen as this happens)
            TimeOfDay.Shown = -1f; DayNight.SkyTurn = 0f;
            TimeJumped?.Invoke();
            far.gameObject.SetActive(false);
            if (cloudRoot != null) cloudRoot.gameObject.SetActive(false);
            page.gameObject.SetActive(false);
            sheetRoot.gameObject.SetActive(false);
        }
        void OnDestroy() { if (Showing) { TimeOfDay.Shown = -1f; DayNight.SkyTurn = 0f; } }

        // ---------------------------------------------------------------- the time-lapse

        /// <summary>Called when the time shown jumps back to the game's own (put the light on at once).</summary>
        public System.Action TimeJumped;

        // A day in 45 seconds, but not at one rate: TimeOfDay's clock holds each phase's look most of the phase and
        // changes over the last fifth of it (DayNight), and the evening goes from gold to pink inside its own phase.
        // So each stretch has its own seconds: long enough in the full day and the deep night to enjoy them, slower
        // still through the sunset and the dawn, and quick across the stretches where nothing changes.
        static readonly float[] LapseAt = { 0f, 0.8f, 1f, 1.8f, 2f, 2.8f, 3f, 3.8f, 4f };   // the clock: morning, day, evening, night
        static readonly float[] LapseSec = { 4.5f, 4f, 6.5f, 4f, 9f, 4f, 7f, 6f };         // seconds over each stretch
        /// <summary>Degrees a second the sky (and its sun or moon) turns while the time-lapse runs: the clouds race.</summary>
        public const float SkyTurnRate = 4.5f;
        public static float LapseSeconds { get { float t = 0f; foreach (float s in LapseSec) t += s; return t; } }

        float lapseT;
        bool lapsing;

        /// <summary>The clock the time-lapse shows `t` seconds into it.</summary>
        public static float LapseClock(float t)
        {
            t = Mathf.Repeat(t, LapseSeconds);
            for (int k = 0; k < LapseSec.Length; k++)
            {
                if (t < LapseSec[k]) return Mathf.Lerp(LapseAt[k], LapseAt[k + 1], t / LapseSec[k]);
                t -= LapseSec[k];
            }
            return 0f;
        }

        /// <summary>How far into the time-lapse it shows the clock `c`.</summary>
        public static float LapseTimeOf(float c)
        {
            c = Mathf.Repeat(c, 4f);
            float t = 0f;
            for (int k = 0; k < LapseSec.Length; k++)
            {
                if (c < LapseAt[k + 1]) return t + LapseSec[k] * Mathf.InverseLerp(LapseAt[k], LapseAt[k + 1], c);
                t += LapseSec[k];
            }
            return 0f;
        }

        /// <summary>The time-lapse's own clock, seconds (the test tools set it to show any moment of it).</summary>
        public float LapseTime { get => lapseT; set { lapseT = value; if (lapsing) TimeOfDay.Shown = LapseClock(lapseT); } }

        /// <summary>
        /// One frame of the time-lapse. With the settings open it stops and the game's own time is shown (so a change
        /// to TIME OF DAY shows, DayNight running the day round to it), and it carries on from there when they close.
        /// </summary>
        void StepLapse(float dt)
        {
            if (sheetOpen)
            {
                if (lapsing) { lapsing = false; TimeOfDay.Shown = -1f; }
                return;
            }
            if (!lapsing) { lapsing = true; lapseT = LapseTimeOf(TimeOfDay.Own); }
            lapseT += dt;
            TimeOfDay.Shown = LapseClock(lapseT);
            DayNight.SkyTurn = Mathf.Repeat(DayNight.SkyTurn + SkyTurnRate * dt, 360f);
        }

        static Vector3 SunDir() => RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : new Vector3(-0.5f, 0.7f, -0.5f);

        // ---------------------------------------------------------------- the flight

        float age, sheet, leaveT;
        int leaving;   // 0 staying, 1 to the camp, 2 to the start gate
        bool sheetOpen;

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Where the helicopter is and where its camera points, `t` seconds into the flight.</summary>
        public void Pose(float t, out Vector3 pos, out Quaternion rot, out float fov)
        {
            Vector3 mid = FarMountain.Middle;
            float floor = FarMountain.FloorY;
            // round the mountain, never quite at one rate
            float phi = (StartAzimuth + 360f * t / OrbitSeconds) * Mathf.Deg2Rad + 0.04f * Mathf.Sin(t * 0.071f + 1.3f);
            // the lens: the page's own long one, a share of the player's LENS setting on top, and the distance moved to
            // keep the mountain the same size in the frame, so a wider lens changes the perspective and not the framing
            float lens = lensFov != null ? lensFov() : 0f;
            fov = Fov + 0.45f * lens;
            float aspect = cam != null ? cam.aspect : 0.46f;
            float across = Mathf.Sqrt(Mathf.Pow(FarMountain.HalfAcross * Mathf.Cos(phi), 2f) + Mathf.Pow(FarMountain.HalfAlong * Mathf.Sin(phi), 2f));
            float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float r = across / (tanV * aspect * Fill);
            float tall = (FarMountain.SummitPoint.y - floor) / (2f * tanV * 0.42f);   // (a wide screen: the height is what has to fit)
            r = Mathf.Max(r, tall);
            r *= 1f + 0.035f * Mathf.Sin(t * 0.047f + 0.6f) + 0.012f * Mathf.Sin(t * 0.19f);
            float alt = floor + Altitude + 230f * Mathf.Sin(t * 0.037f + 2.1f) + 70f * Mathf.Sin(t * 0.113f);
            pos = new Vector3(mid.x + Mathf.Sin(phi) * r, alt, mid.z + Mathf.Cos(phi) * r);
            // never low over a ridge
            float ground = FarMountain.Height(pos.x, pos.z) + 520f;
            if (pos.y < ground + 300f) { float h = Mathf.Clamp01((ground + 300f - pos.y) / 600f); pos.y = Mathf.Lerp(pos.y, ground + 300f, h * h * (3f - 2f * h)); }
            pos.y += 9f * Mathf.Sin(t * 0.83f) + 5f * Mathf.Sin(t * 1.37f + 1f);   // the airframe riding the air
            Vector3 side = new Vector3(Mathf.Cos(phi), 0f, -Mathf.Sin(phi));
            // the aim floats, as a hand-held long lens does
            Vector3 aim = new Vector3(mid.x, floor + AimHeight, mid.z) + side * (150f * Mathf.Sin(t * 0.09f + 0.4f)) + Vector3.up * (90f * Mathf.Sin(t * 0.061f));
            float roll = 1.5f * Mathf.Sin(t * 0.13f + 0.5f) + 0.7f * Mathf.Sin(t * 0.41f) + 0.1f * Mathf.Sin(t * 2.3f);
            float pitch = 8.2f * sheet;   // the settings open: the mountain moves up the frame, clear of them
            if (leaving != 0)
            {
                // going in: the lens pushes in on the summit
                float k = Ease(leaveT);
                aim = Vector3.Lerp(aim, FarMountain.SummitPoint, k);
                fov = Mathf.Lerp(fov, 9f, k * k);
                roll *= 1f - k; pitch *= 1f - k;
            }
            rot = Quaternion.LookRotation(aim - pos, Vector3.up) * Quaternion.Euler(pitch, 0f, roll);
        }

        /// <summary>One frame of the flight (the test tools call it; in the game LateUpdate does).</summary>
        public void Frame(float dt)
        {
            if (!Showing || cam == null) return;
            Clock += dt;
            sheet = Mathf.MoveTowards(sheet, sheetOpen ? 1f : 0f, dt / 0.45f);
            Pose(Clock, out Vector3 pos, out Quaternion rot, out float fov);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
            StepLapse(dt);
            far.SetNight(darkness != null ? darkness() : 0f);
            far.Shade(SunDir(), dt);   // (at most a millisecond a frame: FarMountain.Shade)
            StepClouds(dt);
        }

        void LateUpdate() { Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f)); }

        /// <summary>Everything the page does by itself each frame: the flight, the cloud across the screen, the sound.</summary>
        public void Tick(float dt)
        {
            if (Showing) Frame(dt);
            StepCover(dt);
            StepSound(dt);
        }

        // ---------------------------------------------------------------- cloud

        Transform cloudRoot;
        Transform[] clouds; Vector3[] cloudAt; Material cloudMat; Texture2D cloudTex; int cloudSeed = int.MinValue;

        /// <summary>Banks of cloud standing off the mountain's flanks: they slide across it as the helicopter circles.</summary>
        void BuildClouds()
        {
            if (soft == null) return;
            if (cloudRoot == null)
            {
                cloudRoot = new GameObject("FarClouds").transform;
                cloudRoot.SetParent(transform, false);
                cloudTex = MakeCloud();
                cloudMat = new Material(soft) { name = "FarCloud" };
                cloudMat.SetTexture("_BaseMap", cloudTex); cloudMat.SetTexture("_MainTex", cloudTex);
                cloudMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 5;
                clouds = new Transform[7]; cloudAt = new Vector3[7];
                var quad = new Mesh { name = "FarCloudQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                for (int i = 0; i < clouds.Length; i++)
                {
                    var go = new GameObject("Cloud" + i) { layer = FarMountain.Layer };
                    go.transform.SetParent(cloudRoot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = quad;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = cloudMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                    clouds[i] = go.transform;
                }
            }
            cloudRoot.gameObject.SetActive(true);
            if (cloudSeed == Slope.Seed) return;
            cloudSeed = Slope.Seed;
            Vector3 mid = FarMountain.Middle;
            for (int i = 0; i < clouds.Length; i++)
            {
                float a = (i * 51f + 30f * Slope.Hash01(i, 3, 211)) * Mathf.Deg2Rad, d = 5000f + 5000f * Slope.Hash01(i, 4, 211);
                cloudAt[i] = new Vector3(mid.x + Mathf.Sin(a) * d, FarMountain.FloorY + 800f + 1100f * Slope.Hash01(i, 5, 211), mid.z + Mathf.Cos(a) * d);
                float w = 2600f + 2600f * Slope.Hash01(i, 6, 211);
                clouds[i].localScale = new Vector3(w * (Slope.Hash01(i, 8, 211) < 0.5f ? -1f : 1f), w * (0.34f + 0.12f * Slope.Hash01(i, 7, 211)), 1f);
            }
        }

        void StepClouds(float dt)
        {
            if (clouds == null || cloudRoot == null || !cloudRoot.gameObject.activeSelf) return;
            for (int i = 0; i < clouds.Length; i++)
            {
                cloudAt[i] += new Vector3(7f, 0f, 3f) * (dt * (lapsing ? 14f : 1f));   // the wind (racing in the time-lapse)
                clouds[i].SetPositionAndRotation(cloudAt[i], Quaternion.LookRotation(cloudAt[i] - cam.transform.position, Vector3.up));
            }
            // lit like the haze: the sky's light and a share of the sun's
            Color c = RenderSettings.ambientSkyColor.linear * 0.9f;
            if (RenderSettings.sun != null) c += RenderSettings.sun.color.linear * (RenderSettings.sun.intensity * 0.3f);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m > 1f) c /= m;
            c.a = 0.5f;
            cloudMat.SetColor("_BaseColor", c); cloudMat.SetColor("_Color", c);
        }

        /// <summary>A drift of mist: soft all the way out, thicker in places.</summary>
        static Texture2D MakeCloud()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "FarCloud", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float fall = Mathf.Clamp01(1f - (u * u + v * v));
                    float n = 0.6f * Mathf.PerlinNoise(x * 0.045f + 3f, y * 0.09f + 9f) + 0.4f * Mathf.PerlinNoise(x * 0.11f, y * 0.2f + 5f);
                    float a = Mathf.Clamp01(fall * fall * (0.25f + 1.3f * n));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }

        // ---------------------------------------------------------------- the rotor and the wind

        AudioSource rotor, wind;
        float soundLevel;

        void BuildSound()
        {
            rotor = gameObject.AddComponent<AudioSource>();
            rotor.clip = MakeRotor(); rotor.loop = true; rotor.playOnAwake = false; rotor.spatialBlend = 0f; rotor.volume = 0f; rotor.priority = 200;
            wind = gameObject.AddComponent<AudioSource>();
            wind.clip = Resources.Load<AudioClip>("Audio/wind_low"); wind.loop = true; wind.playOnAwake = false; wind.spatialBlend = 0f; wind.volume = 0f; wind.priority = 200;
            wind.pitch = 0.82f;
        }

        void StepSound(float dt)
        {
            if (rotor == null) return;
            // up gently with the view; away as the cloud closes
            soundLevel = Mathf.MoveTowards(soundLevel, Showing && leaving == 0 ? 1f : 0f, dt / (Showing && leaving == 0 ? 2.5f : 0.9f));
            bool on = soundLevel > 0.001f;
            if (on != rotor.isPlaying) { if (on) { rotor.Play(); if (wind.clip != null) wind.Play(); } else { rotor.Stop(); wind.Stop(); } }
            if (!on) return;
            // faint, under the music: the blades' beat, a little louder as the machine banks
            float sway = 0.5f + 0.5f * Mathf.Sin(Clock * 0.13f + 0.5f);
            rotor.volume = soundLevel * (0.05f + 0.014f * sway);
            rotor.pitch = 1f + 0.012f * Mathf.Sin(Clock * 0.21f);
            wind.volume = soundLevel * (0.055f + 0.03f * Mathf.Sin(Clock * 0.17f + 2f) * Mathf.Sin(Clock * 0.053f));
        }

        /// <summary>
        /// A helicopter heard from inside it, made here rather than shipped as a file: the blades' beat (a low thud
        /// 17.5 times a second and its overtones), the turbine's drone, and the wash of air, which beats with the
        /// blades. Every part is a whole number of cycles long, so the loop has no seam.
        /// </summary>
        static AudioClip MakeRotor()
        {
            const int rate = 22050; const float seconds = 2.4f, blade = 17.5f;
            int n = Mathf.RoundToInt(rate * seconds);
            var d = new float[n];
            var rnd = new System.Random(7);
            var wf = new float[40]; var wp = new float[40]; var wa = new float[40];
            for (int k = 0; k < wf.Length; k++)
            {
                wf[k] = Mathf.Round((140f + 760f * (float)rnd.NextDouble()) * seconds) / seconds;
                wp[k] = (float)rnd.NextDouble() * Mathf.PI * 2f; wa[k] = 0.5f + 0.5f * (float)rnd.NextDouble();
            }
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, w = 2f * Mathf.PI * t;
                float beat = 0f;
                for (int h = 1; h <= 6; h++) beat += Mathf.Sin(w * blade * h + h * 0.7f) / (h * (h < 3 ? 1f : 1.6f));
                float pulse = 0.5f + 0.5f * Mathf.Sin(w * blade - 0.6f);
                float drone = 0.22f * Mathf.Sin(w * blade * 5f) + 0.12f * Mathf.Sin(w * blade * 8f + 1f) + 0.05f * Mathf.Sin(w * blade * 24f);
                float wash = 0f;
                for (int k = 0; k < wf.Length; k++) wash += wa[k] * Mathf.Sin(w * wf[k] + wp[k]);
                wash *= 0.035f * (0.35f + 0.65f * pulse * pulse);
                d[i] = beat * 0.55f + drone + wash;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.7f / peak;
            var clip = AudioClip.Create("Rotor", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        // ---------------------------------------------------------------- the page

        Canvas canvas;
        Font face;
        RectTransform safe, page, sheetRoot, sheetSafe, titleRect, campBtn, runBtn, gearBtn, closeBtn;
        CanvasGroup titleGroup, choiceGroup, sheetGroup;
        Image cover, topShade, bottomShade;
        Text campText, runText;
        Rect lastSafe;
        Sprite fadeUp, chevron, sliders;
        RectTransform pressed; float pressT;

        // ---------------------------------------------------------------- the title

        // (the word alone: clear of the safe area either side even slanted, a little lower in the sky than it stood under
        // MOUNT, and its reveal and sweep a touch quicker across one word)
        const float TitleWidth = 880f, TitleY = 330f, TitleReveal = 2.1f, SweepAt = 1.7f, SweepTime = 1.3f;
        RawImage titleImage;
        Material titleMat;
        Texture2D titleTex;
        System.Threading.Tasks.Task<Color32[]> titleJob;
        int titleStyle = -1, titleJobStyle;

        /// <summary>Which of TitleArt.Styles the title is drawn in. `now`: build it at once (the test tools), else off the main thread.</summary>
        public void SetTitleStyle(int style, bool now)
        {
            style = Mathf.Clamp(style, 0, TitleArt.Styles.Length - 1);
            if (style == titleStyle && (titleTex != null || titleJob != null) && !now) return;
            titleStyle = style;
            var st = TitleArt.Styles[style];
            if (titleMat != null)
            {
                titleMat.SetColor("_Top", st.top); titleMat.SetColor("_Bottom", st.bottom); titleMat.SetColor("_SnowCol", st.snowCol); titleMat.SetColor("_ShadowCol", st.shadowCol);
                titleMat.SetFloat("_Spec", st.spec); titleMat.SetFloat("_Edge", st.edge); titleMat.SetFloat("_Frost", st.frost); titleMat.SetFloat("_ShadowA", st.shadow); titleMat.SetFloat("_Drift", st.drift);
                titleMat.SetVector("_Word", new Vector4(TitleArt.WordV0, TitleArt.WordV1, 0, 0));
                titleMat.SetFloat("_Spread", TitleArt.Spread); titleMat.SetVector("_TexSize", new Vector4(TitleArt.Width, TitleArt.Height, 0, 0));
            }
            if (now) { titleJob = null; UseTitle(TitleArt.Build(st)); return; }
            titleJobStyle = style;
            titleJob = System.Threading.Tasks.Task.Run(() => TitleArt.Build(st));
        }

        void UseTitle(Color32[] px)
        {
            if (titleTex != null) { if (Application.isPlaying) Destroy(titleTex); else DestroyImmediate(titleTex); }
            titleTex = TitleArt.ToTexture(px, "Title");
            titleImage.texture = titleTex;
            titleImage.enabled = titleMat != null;
        }

        /// <summary>The title's reveal and its light, each frame the page is drawn.</summary>
        void StepTitle()
        {
            if (titleJob != null && titleJob.IsCompleted)
            {
                var job = titleJob; titleJob = null;
                if (job.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && titleJobStyle == titleStyle) UseTitle(job.Result);
            }
            if (titleMat == null || titleTex == null) return;
            // the reveal runs on the page's own clock from when the page comes up, so a preview of any moment shows that
            // moment (the texture is ready long before: it takes a fraction of the view's time alone)
            float ta = age - ViewAlone;
            titleMat.SetFloat("_Reveal", Mathf.Clamp01(ta / TitleReveal));
            titleMat.SetFloat("_Sweep", Mathf.Lerp(-0.4f, 1.4f, Mathf.Clamp01((ta - SweepAt) / SweepTime)));
            // the light of the day, from where the sun or moon stands in the view
            var sun = RenderSettings.sun;
            Color lc = sun != null ? sun.color.linear * Mathf.Clamp(sun.intensity, 0.2f, 2f) : Color.white;
            float lm = Mathf.Max(lc.r, Mathf.Max(lc.g, lc.b));
            if (lm > 1.4f) lc *= 1.4f / lm;
            Color amb = RenderSettings.ambientSkyColor.linear;
            // (never so dark it can't be read: at night the moonlit ice keeps a floor)
            float am = Mathf.Max(amb.r, Mathf.Max(amb.g, amb.b));
            if (am < 0.4f) amb = Color.Lerp(amb, new Color(0.34f, 0.36f, 0.42f), 1f - am / 0.4f);   // (and greyer: moonlight shows little colour)
            // by moonlight colour goes: the light and the sky's light both greyed (or the blue ice would go a saturated blue)
            float dark = darkness != null ? Mathf.Clamp01(darkness()) : 0f;
            Color Grey(Color c, float l) { float y = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b; return Color.Lerp(c, new Color(y * 0.92f, y * 0.97f, y * 1.08f), l); }
            amb = Grey(amb, dark * 0.75f);
            lc = Grey(lc, dark * 0.75f) * (1f + 0.5f * dark);
            titleMat.SetColor("_Light", new Color(lc.r, lc.g, lc.b, 0.85f));
            titleMat.SetColor("_Ambient", amb * 0.8f);
            Vector3 toSun = SunDir();
            Vector2 ld = cam != null ? new Vector2(Vector3.Dot(toSun, cam.transform.right), Mathf.Max(0.25f, Vector3.Dot(toSun, cam.transform.up) + 0.35f)) : new Vector2(-0.5f, 0.8f);
            titleMat.SetVector("_LightDir", ld.normalized);
        }

        class Row
        {
            public int tab; public string label; public System.Func<string> value; public System.Action<int> step;
            public bool toggle; public System.Func<bool> live;
            public RectTransform rect, block; public Text name, val; public Image left, right;
        }
        readonly List<Row> rows = new List<Row>();
        readonly List<string> tabNames = new List<string>();
        readonly List<RectTransform> tabBtn = new List<RectTransform>();
        readonly List<Text> tabText = new List<Text>();
        Image tabMark;
        int tab;

        /// <summary>A page of settings.</summary>
        public int AddTab(string name) { tabNames.Add(name); return tabNames.Count - 1; }

        /// <summary>
        /// One setting: its name, what it reads now, and what a step does (-1 or +1; a `toggle` is only ever
        /// stepped +1). `live` false greys it out (it can't be changed just now).
        /// </summary>
        public void AddRow(int inTab, string label, System.Func<string> value, System.Action<int> step, bool toggle = false, System.Func<bool> live = null)
        {
            rows.Add(new Row { tab = inTab, label = label, value = value, step = step, toggle = toggle, live = live });
        }

        static readonly Color Ink = new Color(1f, 1f, 1f, 0.96f), Quiet = new Color(1f, 1f, 1f, 0.55f), Hair = new Color(1f, 1f, 1f, 0.14f);

        /// <summary>Letters set apart (uGUI's Text has no tracking: a small space goes between each pair).</summary>
        static string Track(string s, int gap)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ' ') { sb.Append("<size=").Append(gap).Append(">   </size>"); continue; }   // (small spaces, several: a big one would push the line down)
                sb.Append(s[i]);
                if (i < s.Length - 1 && s[i + 1] != ' ') sb.Append("<size=").Append(gap).Append("> </size>");
            }
            return sb.ToString();
        }

        static Font LoadFace()
        {
            Font f = null;
            // a lighter, wider face than the menus' where the system has one (the phone and the Mac both do)
            try { f = Font.CreateDynamicFontFromOSFont(new[] { "Avenir Next", "Avenir", "Helvetica Neue", "Arial" }, 64); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            return f;
        }

        static RectTransform New(string name, Transform parent)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            return r;
        }

        static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        static Image Img(string name, Transform parent, Color c, Sprite s = null)
        {
            var i = New(name, parent).gameObject.AddComponent<Image>();
            i.color = c; i.raycastTarget = false; i.sprite = s;
            return i;
        }

        Text Label(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Color c)
        {
            var t = New(name, parent).gameObject.AddComponent<Text>();
            t.font = face; t.fontSize = fontSize; t.fontStyle = FontStyle.Normal; t.alignment = align; t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            float px = align == TextAnchor.MiddleLeft ? 0f : align == TextAnchor.MiddleRight ? 1f : 0.5f;
            Place(t.rectTransform, anchor, pos, size, new Vector2(px, 0.5f));
            // a soft dark edge so it reads on snow and on night alike, without a plate behind it
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0.02f, 0.04f, 0.09f, 0.45f); sh.effectDistance = new Vector2(0f, -2f);
            return t;
        }

        void BuildUi()
        {
            face = LoadFace();
            fadeUp = Gradient(false); chevron = Chevron(64); sliders = Sliders(96);
            var cg = new GameObject("StartCanvas", typeof(RectTransform));
            cg.transform.SetParent(transform, false);
            canvas = cg.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;   // over the HUD (10) and the camp's menus (11)
            var scaler = cg.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            page = New("Page", cg.transform); Stretch(page);
            // the frame's top and bottom a little darker, so small type reads over cloud and snow (edge to edge, past the safe area)
            topShade = Img("TopShade", page, new Color(0.02f, 0.04f, 0.09f, 0.34f), Gradient(true));
            topShade.rectTransform.anchorMin = new Vector2(0f, 1f); topShade.rectTransform.anchorMax = Vector2.one;
            topShade.rectTransform.pivot = new Vector2(0.5f, 1f); topShade.rectTransform.sizeDelta = new Vector2(0f, 760f);
            bottomShade = Img("BottomShade", page, new Color(0.02f, 0.04f, 0.09f, 0.5f), fadeUp);
            bottomShade.rectTransform.anchorMin = Vector2.zero; bottomShade.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottomShade.rectTransform.pivot = new Vector2(0.5f, 0f); bottomShade.rectTransform.sizeDelta = new Vector2(0f, 820f);

            safe = New("Safe", page);
            ApplySafeArea();

            // the title, high in the sky
            titleRect = New("Title", safe);
            Place(titleRect, new Vector2(0.5f, 1f), new Vector2(0f, -TitleY), new Vector2(TitleWidth, TitleWidth * TitleArt.Height / TitleArt.Width), new Vector2(0.5f, 0.5f));
            titleGroup = titleRect.gameObject.AddComponent<CanvasGroup>();
            // the mountain's name, drawn (TitleArt: our own letterforms, ice and snow, made at start-up off the main
            // thread) and lit by the day (TitleLetters.shader). Until its texture is ready nothing shows
            titleImage = New("Lettering", titleRect).gameObject.AddComponent<RawImage>();
            titleImage.raycastTarget = false;
            Place(titleImage.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(TitleWidth, TitleWidth * TitleArt.Height / TitleArt.Width), new Vector2(0.5f, 0.5f));
            var tsh = Resources.Load<Shader>("TitleLetters");
            if (tsh != null && tsh.isSupported) titleMat = new Material(tsh) { name = "TitleLetters" };
            titleImage.material = titleMat;
            titleImage.enabled = false;
            SetTitleStyle(TitleArt.DefaultStyle, false);

            // the two ways in, side by side, and under them the one control for everything else
            var choices = New("Choices", safe); Stretch(choices);
            choiceGroup = choices.gameObject.AddComponent<CanvasGroup>();
            campBtn = New("VisitCamp", choices);
            Place(campBtn, new Vector2(0.5f, 0f), new Vector2(-232f, 318f), new Vector2(440f, 170f), new Vector2(0.5f, 0.5f));
            campText = Label("Text", campBtn, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 80f), 36, TextAnchor.MiddleCenter, Ink);
            campText.text = Track("VISIT CAMP", 30);
            runBtn = New("GoForARun", choices);
            Place(runBtn, new Vector2(0.5f, 0f), new Vector2(232f, 318f), new Vector2(440f, 170f), new Vector2(0.5f, 0.5f));
            runText = Label("Text", runBtn, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 80f), 36, TextAnchor.MiddleCenter, Ink);
            runText.text = Track("GO FOR A RUN", 30);
            var split = Img("Split", choices, new Color(1f, 1f, 1f, 0.38f));
            Place(split.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 318f), new Vector2(2f, 54f), new Vector2(0.5f, 0.5f));
            gearBtn = New("Settings", choices);
            Place(gearBtn, new Vector2(0.5f, 0f), new Vector2(0f, 136f), new Vector2(220f, 150f), new Vector2(0.5f, 0.5f));
            var gi = Img("Icon", gearBtn, new Color(1f, 1f, 1f, 0.82f), sliders);
            Place(gi.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f), new Vector2(0.5f, 0.5f));

            // the settings: a sheet over the lower part of the view, the mountain still in sight above it
            sheetRoot = New("Sheet", cg.transform);
            sheetRoot.anchorMin = Vector2.zero; sheetRoot.anchorMax = new Vector2(1f, 0f); sheetRoot.pivot = new Vector2(0.5f, 0f);
            sheetGroup = sheetRoot.gameObject.AddComponent<CanvasGroup>();
            var body = Img("Body", sheetRoot, new Color(0.025f, 0.04f, 0.075f, 0.9f));
            Stretch(body.rectTransform);
            var lip = Img("Lip", sheetRoot, new Color(0.025f, 0.04f, 0.075f, 0.9f), fadeUp);
            lip.rectTransform.anchorMin = new Vector2(0f, 1f); lip.rectTransform.anchorMax = Vector2.one; lip.rectTransform.pivot = new Vector2(0.5f, 0f);
            lip.rectTransform.sizeDelta = new Vector2(0f, 150f); lip.rectTransform.anchoredPosition = Vector2.zero;
            sheetSafe = New("SheetSafe", sheetRoot);
            sheetSafe.gameObject.SetActive(true);
            sheetRoot.gameObject.SetActive(false);

            // cloud over everything: what the game changes scene behind (it outlives the page)
            cover = Img("Cloud", cg.transform, new Color(1f, 1f, 1f, 0f));
            Stretch(cover.rectTransform);
            cover.gameObject.SetActive(false);
            page.gameObject.SetActive(false);
        }

        bool sheetBuilt;

        /// <summary>Lay the settings out (once every row has been added).</summary>
        void BuildSheet()
        {
            if (sheetBuilt) return;
            sheetBuilt = true;
            closeBtn = New("Close", sheetSafe);
            Place(closeBtn, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(260f, 96f), new Vector2(0.5f, 0.5f));
            var ch = Img("Chevron", closeBtn, new Color(1f, 1f, 1f, 0.6f), chevron);
            Place(ch.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f), new Vector2(0.5f, 0.5f));
            ch.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);   // the chevron points right as drawn: down, to put the sheet away
            float tabW = 940f / Mathf.Max(1, tabNames.Count);
            for (int k = 0; k < tabNames.Count; k++)
            {
                var b = New(tabNames[k] + "Tab", sheetSafe);
                Place(b, new Vector2(0.5f, 1f), new Vector2(-470f + tabW * (k + 0.5f), -112f), new Vector2(tabW, 110f), new Vector2(0.5f, 0.5f));
                var t = Label("Text", b, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(tabW, 70f), 30, TextAnchor.MiddleCenter, Quiet);
                t.text = Track(tabNames[k], 24);
                tabBtn.Add(b); tabText.Add(t);
            }
            var line = Img("TabLine", sheetSafe, Hair);
            Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(940f, 2f), new Vector2(0.5f, 0.5f));
            tabMark = Img("TabMark", sheetSafe, Ink);
            Place(tabMark.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(72f, 3f), new Vector2(0.5f, 0.5f));
            var count = new int[tabNames.Count];
            foreach (var r in rows)
            {
                int i = count[r.tab]++;
                r.rect = New(r.label, sheetSafe);
                Place(r.rect, new Vector2(0.5f, 1f), new Vector2(0f, -232f - i * RowHeight), new Vector2(940f, RowHeight), new Vector2(0.5f, 0.5f));
                r.name = Label("Name", r.rect, new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(460f, 60f), 27, TextAnchor.MiddleLeft, Quiet);
                r.name.text = Track(r.label, 20);
                r.block = New("Value", r.rect);
                Place(r.block, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(470f, RowHeight), new Vector2(1f, 0.5f));
                r.val = Label("Text", r.block, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 60f), 31, TextAnchor.MiddleCenter, Ink);
                if (!r.toggle)
                {
                    r.left = Img("Less", r.block, Quiet, chevron);
                    Place(r.left.rectTransform, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(30f, 30f), new Vector2(0.5f, 0.5f));
                    r.left.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                    r.right = Img("More", r.block, Quiet, chevron);
                    Place(r.right.rectTransform, new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(30f, 30f), new Vector2(0.5f, 0.5f));
                }
                if (i > 0)
                {
                    var hl = Img("Hair", r.rect, new Color(1f, 1f, 1f, 0.07f));
                    Place(hl.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(940f, 2f), new Vector2(0.5f, 0.5f));
                }
            }
            LayoutSheet();
        }

        void LayoutSheet()
        {
            Rect a = Screen.safeArea;
            float bottom = Screen.height > 0 ? a.yMin / Screen.height * ((RectTransform)canvas.transform).rect.height : 0f;
            sheetRoot.sizeDelta = new Vector2(0f, SheetHeight + bottom);
            sheetSafe.anchorMin = Vector2.zero; sheetSafe.anchorMax = Vector2.one;
            sheetSafe.offsetMin = new Vector2(0f, bottom); sheetSafe.offsetMax = Vector2.zero;
            // laid out 940 wide: on a narrower canvas the whole sheet's contents shrink to fit
            float room = ((RectTransform)canvas.transform).rect.width - 36f;
            sheetSafe.localScale = Vector3.one * (room > 1f && room < 940f ? room / 940f : 1f);
        }

        void ApplySafeArea()
        {
            Rect a = Screen.safeArea;
            lastSafe = a;
            if (Screen.width <= 0 || Screen.height <= 0) { Stretch(safe); return; }
            safe.anchorMin = new Vector2(a.xMin / Screen.width, a.yMin / Screen.height);
            safe.anchorMax = new Vector2(a.xMax / Screen.width, a.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            if (sheetBuilt) LayoutSheet();
        }

        void ShowTab(int k)
        {
            BuildSheet();
            tab = Mathf.Clamp(k, 0, Mathf.Max(0, tabNames.Count - 1));
            foreach (var r in rows) r.rect.gameObject.SetActive(r.tab == tab);
            for (int i = 0; i < tabText.Count; i++) tabText[i].color = i == tab ? Ink : Quiet;
            if (tabBtn.Count > 0) tabMark.rectTransform.anchoredPosition = new Vector2(tabBtn[tab].anchoredPosition.x, -166f);
        }

        /// <summary>Read every setting again (after a step: one can grey another out).</summary>
        public void Refresh()
        {
            BuildSheet();
            foreach (var r in rows)
            {
                bool on = r.live == null || r.live();
                r.val.text = Track(r.value != null ? r.value() : "", 12);
                r.val.color = on ? Ink : new Color(1f, 1f, 1f, 0.3f);
                r.name.color = on ? Quiet : new Color(1f, 1f, 1f, 0.28f);
                if (r.left != null) r.left.color = r.right.color = on ? Quiet : new Color(1f, 1f, 1f, 0.16f);
            }
        }

        Camera UiCam => canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;
        bool Inside(RectTransform r, Vector2 p) => r != null && r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, p, UiCam);

        void Press(RectTransform r) { pressed = r; pressT = 0.22f; Clicked?.Invoke(); }

        /// <summary>
        /// One frame of the page. `press`: a finger came down this frame, `at` where (screen pixels).
        /// Returns 1 (the camp) or 2 (the start gate) on the frame the cloud has closed and it is time to go;
        /// otherwise 0.
        /// </summary>
        public int Step(float dt, bool press, Vector2 at)
        {
            if (!Showing) return 0;
            if (safe != null && Screen.safeArea != lastSafe) ApplySafeArea();
            age += dt;
            if (leaving != 0)
            {
                leaveT = Mathf.MoveTowards(leaveT, 1f, dt / LeaveSeconds);
                coverWant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, leaveT)); coverRate = 0f;
                SetAlphas();
                if (leaveT < 1f) return 0;
                int go = leaving;
                coverA = 1f; coverWant = 0f; coverRate = 1f / CoverOut;
                return go;
            }
            if (press)
            {
                if (sheetOpen)
                {
                    if (Inside(closeBtn, at) || !Inside(sheetRoot, at)) { sheetOpen = false; Clicked?.Invoke(); }
                    else TapSheet(at);
                }
                else if (age < ViewAlone + FadeIn * 0.6f) age = ViewAlone + FadeIn * 0.6f;   // a tap while the view is alone: the page comes up now
                else if (Inside(campBtn, at)) { Press(campBtn); leaving = 1; }
                else if (Inside(runBtn, at)) { Press(runBtn); leaving = 2; }
                else if (Inside(gearBtn, at)) { Press(gearBtn); sheetOpen = true; sheetRoot.gameObject.SetActive(true); LayoutSheet(); Refresh(); }
            }
            SetAlphas();
            return 0;
        }

        void TapSheet(Vector2 at)
        {
            for (int k = 0; k < tabBtn.Count; k++)
                if (Inside(tabBtn[k], at)) { ShowTab(k); Clicked?.Invoke(); return; }
            foreach (var r in rows)
            {
                if (r.tab != tab || !Inside(r.rect, at)) continue;
                if (r.live != null && !r.live()) return;
                int dir = 1;
                if (!r.toggle && Inside(r.block, at))
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(r.block, at, UiCam, out Vector2 lp);
                    dir = lp.x < r.block.rect.center.x ? -1 : 1;
                }
                r.step?.Invoke(dir);
                Press(r.rect);
                Refresh();
                return;
            }
        }

        void SetAlphas()
        {
            float up = leaving != 0 ? 1f - Ease(leaveT * 3f) : 1f;
            float page01 = (1f - sheet) * up;
            titleGroup.alpha = Mathf.Clamp01((age - ViewAlone) / 0.3f) * page01;   // (the lettering reveals itself: StepTitle)
            choiceGroup.alpha = Ease((age - ViewAlone - 0.7f) / FadeIn) * page01;
            titleRect.anchoredPosition = new Vector2(0f, -TitleY + 10f * (1f - Ease((age - ViewAlone) / (FadeIn * 1.6f))));
            StepTitle();
            topShade.color = new Color(0.02f, 0.04f, 0.09f, 0.34f * titleGroup.alpha);
            bottomShade.color = new Color(0.02f, 0.04f, 0.09f, 0.5f * Mathf.Max(choiceGroup.alpha, sheet * up));
            // the sheet rises as it comes
            bool on = sheet > 0.001f && leaving == 0;
            if (sheetRoot.gameObject.activeSelf != on) sheetRoot.gameObject.SetActive(on);
            if (on) { float e = Ease(sheet); sheetGroup.alpha = e; sheetRoot.anchoredPosition = new Vector2(0f, -70f * (1f - e)); }
            // a touch answers: the thing touched dips and comes back
            if (pressT > 0f && pressed != null)
            {
                pressT -= Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(pressT / 0.22f);
                var g = pressed.GetComponent<CanvasGroup>();
                if (g == null) g = pressed.gameObject.AddComponent<CanvasGroup>();
                g.alpha = 1f - 0.55f * k;
            }
        }

        // ---------------------------------------------------------------- cloud across the screen

        float coverA, coverWant, coverRate;
        System.Action coverThen;

        /// <summary>The cloud is closing or open over the screen: taps wait.</summary>
        public bool Busy => coverThen != null || leaving != 0;

        /// <summary>Close cloud over the screen, do `then` behind it, and let it clear.</summary>
        public void Through(System.Action then)
        {
            if (Busy) return;
            coverThen = then; coverWant = 1f; coverRate = 1f / 0.45f;
        }

        void StepCover(float dt)
        {
            if (coverRate > 0f) coverA = Mathf.MoveTowards(coverA, coverWant, dt * coverRate);
            else coverA = coverWant;
            if (coverThen != null && coverA >= 1f)
            {
                var act = coverThen; coverThen = null;
                act();
                coverWant = 0f; coverRate = 1f / CoverOut;
            }
            bool on = coverA > 0.002f;
            if (cover.gameObject.activeSelf != on) cover.gameObject.SetActive(on);
            if (!on) return;
            // the colour of the day's haze (white at noon, dark at night), so going through it never flashes
            Color c = RenderSettings.fogColor; c.a = coverA;
            cover.color = c;
        }

        // ---------------------------------------------------------------- for the test tools

        /// <summary>Draw the page with a camera (overlay canvases don't draw into a render texture).</summary>
        public void UseCamera(Camera uiCam, float plane)
        {
            canvas.renderMode = uiCam != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = uiCam; canvas.planeDistance = plane;
        }
        public int UiLayer { set { foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = value; } }

        /// <summary>Show the page as it is `seconds` after it came up, with the settings open on a tab or shut.</summary>
        public void Preview(float seconds, bool settings, int onTab)
        {
            age = seconds; leaving = 0; leaveT = 0f;
            coverThen = null; coverA = coverWant = 0f; coverRate = 0f; StepCover(0f);
            sheetOpen = settings; sheet = settings ? 1f : 0f;
            ApplySafeArea();
            if (settings) { ShowTab(onTab); Refresh(); }
            SetAlphas();
        }
        /// <summary>Show a moment of the way in (0 the tap .. 1 the cloud closed), to `way` (1 the camp, 2 the start gate).</summary>
        public void PreviewLeaving(float t, int way)
        {
            age = 30f; sheetOpen = false; sheet = 0f; leaving = way; leaveT = Mathf.Clamp01(t);
            coverWant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, leaveT)); coverRate = 0f;
            SetAlphas(); StepCover(0f);
        }
        public int TabCount => tabNames.Count;
        /// <summary>The settings as they read now, and stepping one, for the test tools.</summary>
        public int RowCount => rows.Count;
        public string RowText(int i) => tabNames[rows[i].tab] + " / " + rows[i].label + " = " + (rows[i].value != null ? rows[i].value() : "") + (rows[i].toggle ? " (toggle)" : "") + (rows[i].live != null && !rows[i].live() ? " (greyed)" : "");
        public void RowStep(int i, int dir) { rows[i].step?.Invoke(dir); Refresh(); }
        public bool RowToggle(int i) => rows[i].toggle;
        /// <summary>The middle of one of the page's controls on the screen: "camp", "run" or "settings".</summary>
        public Vector2 ScreenPoint(string control)
        {
            var r = control == "camp" ? campBtn : control == "run" ? runBtn : gearBtn;
            return RectTransformUtility.WorldToScreenPoint(UiCam, r.TransformPoint(r.rect.center));
        }
        public float CoverAlpha => coverA;
        public FarMountain Far => far;

        // ---------------------------------------------------------------- the few pictures the page is drawn with

        /// <summary>Clear at the top and solid at the bottom, or (`down`) the other way up.</summary>
        static Sprite Gradient(bool down)
        {
            const int N = 64;
            var t = new Texture2D(1, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < N; y++) { float k = down ? y / (float)(N - 1) : 1f - y / (float)(N - 1); t.SetPixel(0, y, new Color(1f, 1f, 1f, k * k * (3f - 2f * k))); }
            t.Apply();
            // (the whole rectangle: a tight sprite would trim its clear end, and the fade would stop at a line)
            return Sprite.Create(t, new Rect(0, 0, 1, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float h = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - a - ab * h).magnitude;
        }

        static Sprite Draw(int px, System.Func<Vector2, float> dist, float half)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    var p = new Vector2((x + 0.5f) / px, (y + 0.5f) / px);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((half - dist(p)) * px + 0.5f)));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>A thin chevron pointing right.</summary>
        static Sprite Chevron(int px) => Draw(px, p => Mathf.Min(Seg(p, new Vector2(0.36f, 0.82f), new Vector2(0.68f, 0.5f)), Seg(p, new Vector2(0.68f, 0.5f), new Vector2(0.36f, 0.18f))), 0.045f);

        /// <summary>Three sliders: the one control for the settings.</summary>
        static Sprite Sliders(int px) => Draw(px, p =>
        {
            float d = 9f;
            float[] ys = { 0.78f, 0.5f, 0.22f }, knob = { 0.66f, 0.3f, 0.56f };
            for (int i = 0; i < 3; i++)
            {
                d = Mathf.Min(d, Seg(p, new Vector2(0.12f, ys[i]), new Vector2(0.88f, ys[i])) + 0.022f);   // a hair of a line
                d = Mathf.Min(d, (p - new Vector2(knob[i], ys[i])).magnitude - 0.055f);                    // and its knob
            }
            return d;
        }, 0.04f);
    }
}
