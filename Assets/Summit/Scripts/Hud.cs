using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>The heads-up display, built in code so there is nothing to wire up.</summary>
    public partial class Hud : MonoBehaviour
    {
        int lastKmh = -2, lastPts = -2, lastGap = -2;
        RectTransform canvasRect, stickRing, stickKnob;
        Canvas canvasRef;
        Text diag;
        RectTransform playBtn, restartBtn, modeBtn, riderBtn;
        Text riderLabel;
        /// <summary>The start pad's RIDER label: what is being ridden, by name ("SNOWBOARD", "SKIS", "TOBOGGAN", "SNOW TUBE").</summary>
        public void SetRider(string ride) { riderLabel.text = "RIDE   " + ride; }
        public bool OnRider(Vector2 p) => Inside(riderBtn, p);
        Text modeLabel;
        Text playLabel;
        Text speed, score, slide, big, small, hint, timer, altitude, medical, prompt;
        int lastAlt = -1;
        Image[] shieldPips;
        Image[] timerBg, timerFill;
        Text[] timerText;
        int[] lastTimer;
        int lastTenths = -2;
        Image slideFill, whiteout, hurt;
        GameObject slideBar;   // the avalanche's bar: its dark track and the fill in it
        Image[] fallPips;
        Text fallsLabel;
        float hurtA;
        RectTransform safe;
        float flashT;
        Rect lastSafe;
        Font font;

        public void Build()
        {
            font = Brand.Builtin();
            var canvas = gameObject.AddComponent<Canvas>();
            canvasRef = canvas;
            canvasRect = (RectTransform)transform;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // the full-screen washes, on a canvas of their own: the whiteout changes every frame once the slide is
            // loose, and the red flash as it fades, and a canvas is re-batched whole whenever anything in it changes
            var washes = NestedCanvas("Washes", transform);
            // full-screen white that rises as the slide closes in
            whiteout = Brand.Img("Whiteout", washes, new Color(1f, 1f, 1f, 0f));
            Brand.Stretch(whiteout.rectTransform);
            whiteout.raycastTarget = false;
            whiteout.canvasRenderer.cullTransparentMesh = true;   // (nothing drawn while it's clear: two full-screen blends saved)

            // a red flash when something breaks
            hurt = Brand.Img("Hurt", washes, new Color(0.85f, 0.05f, 0.05f, 0f));
            Brand.Stretch(hurt.rectTransform);
            hurt.raycastTarget = false;
            hurt.canvasRenderer.cullTransparentMesh = true;

            safe = new GameObject("Safe", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(transform, false);
            ApplySafeArea();

            speed = NewText("Speed", safe, new Vector2(0f, 1f), new Vector2(40f, -40f), 54, TextAnchor.UpperLeft);
            score = NewText("Score", safe, new Vector2(0.5f, 1f), new Vector2(0f, -34f), 84, TextAnchor.UpperCenter);
            timer = NewText("Timer", safe, new Vector2(0f, 1f), new Vector2(40f, -104f), 46, TextAnchor.UpperLeft);
            timer.color = Brand.Snow;
            medical = NewText("Medical", safe, new Vector2(1f, 1f), new Vector2(-40f, -232f - PauseRoom), 40, TextAnchor.UpperRight);
            medical.color = Brand.Alert;
            prompt = NewText("Prompt", safe, new Vector2(0.5f, 0f), new Vector2(0f, 230f), 58, TextAnchor.LowerCenter);
            altitude = NewText("Altitude", safe, new Vector2(0f, 1f), new Vector2(40f, -160f), 40, TextAnchor.UpperLeft);
            altitude.color = Brand.SnowMuted;
            // the avalanche's distance and bar, on their own canvas too: both change every frame while it's on its way
            var slideBox = NestedCanvas("Slide", safe);
            slide = NewText("Slide", slideBox, new Vector2(1f, 1f), new Vector2(-40f, -40f - PauseRoom), 44, TextAnchor.UpperRight);

            var barBg = Brand.Img("SlideBar", slideBox, new Color(0f, 0f, 0f, 0.35f));
            slideBar = barBg.gameObject;
            slideBar.SetActive(false);   // (shown only while the avalanche is on its way: SetRun)
            Brand.Place(barBg.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -104f - PauseRoom), new Vector2(300f, 16f), new Vector2(1f, 1f));
            slideFill = Brand.Img("Fill", barBg.transform, new Color(1f, 1f, 1f, 0.95f));
            var fr = slideFill.rectTransform;
            fr.anchorMin = new Vector2(0f, 0f);
            fr.anchorMax = new Vector2(1f, 1f);
            fr.offsetMin = fr.offsetMax = Vector2.zero;

            big = NewText("Big", safe, new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), 110, TextAnchor.MiddleCenter);
            small = NewText("Small", safe, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), 50, TextAnchor.MiddleCenter);
            // the start pad's and the end screen's lines: on dark plates, so they read on bright snow
            smallPlate = Brand.Plate("SmallPlate", safe, Brand.Panel); smallPlate.enabled = false;
            hintPlate = Brand.Plate("HintPlate", safe, Brand.Panel); hintPlate.enabled = false;
            hint = NewText("Hint", safe, new Vector2(0.5f, 0f), new Vector2(0f, 120f), 32, TextAnchor.LowerCenter);
            hint.color = Brand.Snow; hint.lineSpacing = 1.25f;

            diag = NewText("Diag", safe, new Vector2(0f, 0f), new Vector2(20f, 70f), 30, TextAnchor.UpperLeft);
            diag.color = new Color(1f, 1f, 0.6f, 0.9f);

            // the drag stick: a ring where the finger lands and a knob that follows it
            var circle = MakeCircle(96);

            // shields held (up to three) and the boost left in the tank
            shieldPips = new Image[Rider.MaxShields];
            for (int i = 0; i < shieldPips.Length; i++)
            {
                var pip = Brand.Img("Shield" + i, safe, Brand.A(Brand.Snow, 0.95f));
                pip.sprite = circle;
                Brand.Place(pip.rectTransform, new Vector2(0f, 1f), new Vector2(40f + i * 54f, -218f), new Vector2(44f, 44f), new Vector2(0f, 1f));
                shieldPips[i] = pip;
            }
            // (ridercrash) the free hit used: a small red diamond under the shields, for the rest of the run
            hitMark = Brand.Img("HitMark", safe, new Color(0.9f, 0.12f, 0.1f, 0.95f));
            Brand.Place(hitMark.rectTransform, new Vector2(0f, 1f), new Vector2(46f, -276f), new Vector2(30f, 30f), new Vector2(0f, 1f));
            hitMark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            hitMark.raycastTarget = false;
            hitMark.enabled = false;
            // falls left this run: three red crosses, top right under the slide bar
            fallsLabel = NewText("FallsLabel", safe, new Vector2(1f, 1f), new Vector2(-40f, -136f - PauseRoom), 30, TextAnchor.UpperRight);
            fallsLabel.text = "";
            fallPips = new Image[4];   // three, and a fourth with the spare-helmet upgrade
            var cross = MakeCross(64);
            for (int i = 0; i < fallPips.Length; i++)
            {
                var pip = Brand.Img("Fall" + i, safe, Brand.Alert);
                pip.sprite = cross;
                Brand.Place(pip.rectTransform, new Vector2(1f, 1f), new Vector2(-40f - i * 52f, -176f - PauseRoom), new Vector2(42f, 42f), new Vector2(1f, 1f));
                fallPips[i] = pip;
            }
            // the timed power-ups running (Mountain Zoom, Super Jump, Invincible): a name, the seconds left
            // and a bar draining, one row each, stacked under the shields
            timerBg = new Image[TimerNames.Length]; timerFill = new Image[TimerNames.Length];
            timerText = new Text[TimerNames.Length]; lastTimer = new int[TimerNames.Length];
            for (int i = 0; i < TimerNames.Length; i++)
            {
                timerText[i] = NewText("Timer" + i, safe, new Vector2(0f, 1f), Vector2.zero, 34, TextAnchor.UpperLeft);
                timerText[i].color = TimerColors[i];
                timerBg[i] = Brand.Img("TimerBar" + i, safe, new Color(0f, 0f, 0f, 0.35f));
                Brand.Place(timerBg[i].rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(260f, 14f), new Vector2(0f, 1f));
                timerFill[i] = Brand.Img("Fill", timerBg[i].transform, TimerColors[i]);
                var bf = timerFill[i].rectTransform;
                bf.anchorMin = Vector2.zero; bf.anchorMax = Vector2.one; bf.offsetMin = bf.offsetMax = Vector2.zero;
                timerText[i].enabled = timerBg[i].enabled = timerFill[i].enabled = false;
                lastTimer[i] = -1;
            }
            var ring = Brand.Img("StickRing", transform, new Color(1f, 1f, 1f, 0.22f));
            ring.sprite = circle; stickRing = ring.rectTransform;
            var knob = Brand.Img("StickKnob", transform, new Color(1f, 1f, 1f, 0.7f));
            knob.sprite = circle; stickKnob = knob.rectTransform;
            stickRing.gameObject.SetActive(false); stickKnob.gameObject.SetActive(false);
            // down after a fall: no words, a soft ripple where the old prompt was (a tap carries on)
            tapCue = new Brand.TapCue(safe, new Vector2(0.5f, 0f), new Vector2(0f, 250f));

            // buttons (hit-tested by SummitGame, so no EventSystem is needed)
            // the start pad: PLAY (the gold one) and, under it, what you steer with and what you ride, each a tap to change
            playBtn = Brand.Button("PlayButton", safe, "PLAY", Brand.Kind.Primary, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(560f, 132f), out playLabel);
            playLabel.fontSize = 40;

            // the pause: a small square with the two bars, no word, in the top right corner (steerB; where the camp and
            // the start pad keep their settings button, which is never up during a run). The right-hand column (the
            // avalanche, the falls, the bill, the combo) starts under it (PauseRoom)
            restartBtn = Brand.IconButton("RestartButton", safe, Brand.Pause, new Vector2(1f, 1f), new Vector2(-Brand.Margin, -Brand.Margin), new Vector2(1f, 1f));
            BuildPause();
            modeBtn = Brand.Button("ModeButton", safe, "", Brand.Kind.Secondary, new Vector2(0.5f, 0.5f), new Vector2(0f, -258f), new Vector2(560f, Brand.Control), out modeLabel);
            riderBtn = Brand.Button("RiderButton", safe, "", Brand.Kind.Secondary, new Vector2(0.5f, 0.5f), new Vector2(0f, -258f - Brand.Control - 20f), new Vector2(560f, Brand.Control), out riderLabel);
            BuildPoints();   // the combo and the medical bill as they run up (HudPoints.cs)
            SetButtons(false, false, "PLAY");
        }

        static Sprite MakeCross(int px)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false);
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float u = Mathf.Abs(x - (px - 1) * 0.5f) / px, v = Mathf.Abs(y - (px - 1) * 0.5f) / px;
                    bool on = (u < 0.15f && v < 0.44f) || (v < 0.15f && u < 0.44f);
                    bool edge = (u < 0.19f && v < 0.48f) || (v < 0.19f && u < 0.48f);
                    t.SetPixel(x, y, on ? Color.white : edge ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0f));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Falls left this run (shown as red crosses; a used one goes dark).</summary>
        public void SetFalls(int left, int max, bool show)
        {
            for (int i = 0; i < fallPips.Length; i++)
            {
                fallPips[i].enabled = show && i < max;
                fallPips[i].color = i < left ? Brand.Alert : Brand.A(Brand.Ink, 0.45f);
            }
            fallsLabel.text = show ? "FALLS" : "";
        }

        /// <summary>A red flash across the screen.</summary>
        public void Hurt(float a = 0.45f) { hurtA = Mathf.Max(hurtA, a); }
        Image hitMark; float hitPop;
        /// <summary>(ridercrash) The free hit used: the marker shown (it pops in when it first comes on).</summary>
        public void SetHitMark(bool on)
        {
            if (hitMark == null) return;
            if (on && !hitMark.enabled) hitPop = 1f;
            if (hitMark.enabled != on) hitMark.enabled = on;
            if (hitPop > 0f) { hitPop = Mathf.MoveTowards(hitPop, 0f, Time.unscaledDeltaTime * 2.5f); hitMark.rectTransform.localScale = Vector3.one * (1f + 0.8f * hitPop * hitPop); }
        }
        /// <summary>True while the free-hit marker shows (the tests).</summary>
        public bool HitMarkShown => hitMark != null && hitMark.enabled;

        static Sprite MakeCircle(int px)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false);
            float c = (px - 1) * 0.5f;
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 6f)));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Draws the stick at the finger's start point; positions are screen pixels.</summary>
        public void SetStick(bool show, Vector2 origin, Vector2 pos, float radiusPx)
        {
            if (stickRing.gameObject.activeSelf != show)
            {
                stickRing.gameObject.SetActive(show); stickKnob.gameObject.SetActive(show);
            }
            if (!show) return;
            float sc = Mathf.Max(0.01f, canvasRef.scaleFactor);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, origin, null, out Vector2 o);
            float kx = Mathf.Clamp(pos.x - origin.x, -radiusPx, radiusPx) / sc;
            stickRing.anchoredPosition = o;
            stickRing.sizeDelta = Vector2.one * (radiusPx * 2f / sc);
            stickKnob.anchoredPosition = o + new Vector2(kx, 0f);
            stickKnob.sizeDelta = Vector2.one * (radiusPx * 0.9f / sc);
        }

        public void SetDiag(string t) { diag.text = t; }

        public void SetMode(bool tilt) { modeLabel.text = tilt ? "STEER   TILT" : "STEER   DRAG"; }
        public bool OnMode(Vector2 p) => Inside(modeBtn, p);

        CanvasGroup safeGroup;
        /// <summary>Fade the whole HUD (the end screen fades in over the last of the run).</summary>
        public void SetAlpha(float a)
        {
            if (safeGroup == null) safeGroup = safe.gameObject.AddComponent<CanvasGroup>();
            safeGroup.alpha = a;
        }

        public void SetButtons(bool play, bool restart, string playText)
        {
            playBtn.gameObject.SetActive(play);
            modeBtn.gameObject.SetActive(play && playText == "PLAY");   // steering is set at the start, not on the results
            riderBtn.gameObject.SetActive(play && playText == "PLAY");   // choose your rider before a run, not on the results
            restartBtn.gameObject.SetActive(restart);
            playLabel.text = playText;
        }

        static bool Inside(RectTransform r, Vector2 p) =>
            r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, p, null);
        public bool OnPlay(Vector2 p) => Inside(playBtn, p);
        public bool OnRestart(Vector2 p) => Inside(restartBtn, p);

        // ---- the pause menu: PAUSED, RESUME and RESTART RUN side by side, and under them every setting on the same sheet
        //      as the start page's (SettingsSheet, given the same rows by SummitGame.FillSettings), up for as long as
        //      the game is paused. The frozen run shows above it, dimmed. A hidden tab's rows are inactive, so they
        //      can't be hit.

        GameObject pausePanel;
        RectTransform pauseTop, resumeBtn, againBtn, doneBtn;
        Text pauseTitle;
        bool runMenu;
        /// <summary>The pause menu's settings: every one of them, as on the start page.</summary>
        public SettingsSheet PauseSettings { get; private set; }
        /// <summary>The settings' tabs, in the order SummitGame.FillSettings adds them.</summary>
        /// <summary>(steerB) How far the HUD's right-hand column starts under the top, to clear the pause button there.</summary>
        public const float PauseRoom = Brand.Control + 24f;
        public static readonly string[] TabName = { "VIEW", "LOOK", "GAME", "SOUND", "TUNE" };

        void BuildPause()
        {
            var dim = Brand.Img("Pause", transform, Brand.A(Brand.Ink, 0.42f));
            Brand.Stretch(dim.rectTransform);
            pausePanel = dim.gameObject;
            var root = dim.rectTransform;
            // the top of the frame a little darker as on the start page, so the type reads over sky and snow
            var shade = Brand.Img("TopShade", root, Brand.A(Brand.Ink, 0.3f), SettingsSheet.FadeDown);
            shade.rectTransform.anchorMin = new Vector2(0f, 1f); shade.rectTransform.anchorMax = Vector2.one;
            shade.rectTransform.pivot = new Vector2(0.5f, 1f); shade.rectTransform.sizeDelta = new Vector2(0f, 760f);
            PauseSettings = new SettingsSheet(root, false);
            PauseSettings.Resized = () => { if (pausePanel != null && pausePanel.activeSelf) LayoutPause(); };   // (the TUNE tab is taller)
            // over the sheet: PAUSED, and the two ways on, set as the start page's VISIT CAMP | GO FOR A RUN
            pauseTop = Brand.New("PauseTop", root);
            Brand.Place(pauseTop, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(SettingsSheet.Width, 0f), new Vector2(0.5f, 0f));
            pauseTitle = Brand.Line("PausedTitle", pauseTop, Brand.Type.Title, TextAnchor.MiddleCenter, Brand.Snow, new Vector2(0.5f, 0f), new Vector2(0f, 300f), 900f, true);
            pauseTitle.text = "PAUSED";
            // RESUME is the gold one; starting the run again is the quieter choice beside it
            againBtn = Brand.Button("RestartRun", pauseTop, "RESTART RUN", Brand.Kind.Secondary, new Vector2(0.5f, 0f), new Vector2(-232f, 130f), new Vector2(440f, Brand.Control));
            resumeBtn = Brand.Button("Resume", pauseTop, "RESUME", Brand.Kind.Primary, new Vector2(0.5f, 0f), new Vector2(232f, 130f), new Vector2(440f, Brand.Control));
            // the same sheet opened from the camp or the start pad: SETTINGS, and one way out
            doneBtn = Brand.Button("Done", pauseTop, "DONE", Brand.Kind.Primary, new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(440f, Brand.Control));
            pausePanel.SetActive(false);
        }

        /// <summary>Fit the pause menu to the screen: the sheet above the home indicator, PAUSED and the two choices on top of it.</summary>
        void LayoutPause()
        {
            PauseSettings.Layout();
            pauseTop.localScale = PauseSettings.Safe.localScale;
            pauseTop.anchoredPosition = new Vector2(0f, PauseSettings.Top + 20f);
        }

        bool restartWas;   // the PAUSE button was showing when the menu came up (it would sit on the sheet: hidden meanwhile)
        /// <summary>The pause menu, on a run: PAUSED, RESTART RUN and RESUME over every setting.</summary>
        public void ShowPause(bool on) => ShowMenu(on, true);
        /// <summary>The settings from the camp or the start pad (MetaUi's sliders): SETTINGS and DONE over the same sheet.</summary>
        public void ShowSettings(bool on) => ShowMenu(on, false);

        void ShowMenu(bool on, bool run)
        {
            if (on) SetMenuKind(run);
            if (on == pausePanel.activeSelf) { if (on) PauseSettings.Refresh(); return; }
            pausePanel.SetActive(on);
            // over the camp's menus (11) while it's up; under them again after
            canvasRef.sortingOrder = on ? 13 : 10;
            if (on) { restartWas = restartBtn.gameObject.activeSelf; restartBtn.gameObject.SetActive(false); }
            else if (restartWas) restartBtn.gameObject.SetActive(true);
            if (!on) return;
            pausePanel.transform.SetAsLastSibling();
            // a passing message ("Close call +175") would sit in the middle of the menu: let it go
            if (flashT > 0f) { flashT = 0f; big.text = ""; small.text = ""; }
            LayoutPause();
            PauseSettings.Refresh();
        }
        void SetMenuKind(bool run)
        {
            runMenu = run;
            pauseTitle.text = run ? "PAUSED" : "SETTINGS";
            againBtn.gameObject.SetActive(run); resumeBtn.gameObject.SetActive(run);
            doneBtn.gameObject.SetActive(!run);
        }
        /// <summary>The pause menu or the settings are up.</summary>
        public bool Paused => pausePanel.activeSelf;
        public bool OnDone(Vector2 p) => Inside(doneBtn, p);
        public bool OnResume(Vector2 p) => Inside(resumeBtn, p);
        public bool OnPauseRestart(Vector2 p) => Inside(againBtn, p);
        /// <summary>A finger on the pause menu's settings: a tab or a row, stepped. True if it hit one.</summary>
        public bool OnPauseSettings(Vector2 p) => PauseSettings.Tap(p);
        /// <summary>(steerB) Each frame the menu is up: a TUNE slider held follows the finger. True while one is held.</summary>
        public bool PauseDrag(Vector2 p, bool held) => pausePanel != null && pausePanel.activeSelf && PauseSettings.Drag(p, held);
        /// <summary>Read every setting again, if the menu is up (after a setting changed from anywhere).</summary>
        public void RefreshPause() { if (pausePanel != null && pausePanel.activeSelf) PauseSettings.Refresh(); }
        public int PauseTab => PauseSettings.Tab;
        public void ShowTab(int k) => PauseSettings.ShowTab(k);

        Text NewText(string name, Transform parent, Vector2 anchor, Vector2 pos, int size, TextAnchor align)
        {
            var go = Brand.New(name, parent).gameObject;
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            Vector2 pivot = new Vector2(align == TextAnchor.UpperLeft ? 0f : align == TextAnchor.UpperRight ? 1f : 0.5f,
                                        anchor.y);
            Brand.Place(t.rectTransform, anchor, pos, new Vector2(900f, size * 1.3f), pivot);
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = Brand.A(Brand.Ink, 0.6f);
            sh.effectDistance = new Vector2(0f, -3f);
            return t;
        }

        /// <summary>A canvas inside the HUD's, filling its parent and drawn in its place among the parent's children.</summary>
        static RectTransform NestedCanvas(string name, Transform parent)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            Brand.Stretch(r);
            r.gameObject.AddComponent<Canvas>();
            return r;
        }

        void ApplySafeArea()
        {
            Rect a = Screen.safeArea;
            lastSafe = a;
            if (Screen.width <= 0 || Screen.height <= 0) { Brand.Stretch(safe); if (pts != null) Brand.Stretch(pts); return; }
            safe.anchorMin = new Vector2(a.xMin / Screen.width, a.yMin / Screen.height);
            safe.anchorMax = new Vector2(a.xMax / Screen.width, a.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            if (pts != null) { pts.anchorMin = safe.anchorMin; pts.anchorMax = safe.anchorMax; pts.offsetMin = pts.offsetMax = Vector2.zero; }   // (the points and the bill: HudPoints)
            if (PauseSettings != null && PauseSettings.IsBuilt) LayoutPause();
        }

        void Update()
        {
            if (hurtA > 0f) { hurtA = Mathf.MoveTowards(hurtA, 0f, Time.deltaTime * 1.2f); hurt.color = new Color(0.85f, 0.05f, 0.05f, hurtA); }
            if (safe != null && Screen.safeArea != lastSafe) ApplySafeArea();
            if (pausePanel != null && pausePanel.activeSelf) PauseSettings.Step(Time.unscaledDeltaTime);
            if (flashT > 0f)
            {
                flashT -= Time.deltaTime;
                float a = Mathf.Clamp01(flashT / 0.35f);
                big.color = new Color(1f, 1f, 1f, a);
                small.color = new Color(1f, 1f, 1f, a);
            }
        }

        // ---------------------------------------------------------------- API

        public void SetRun(float kmh, int points, float mult, float gap, float gapMax, bool showRun)
        {
            // only touch the Text objects when the number changes: rebuilding them every frame causes hitches
            int k0 = showRun ? Mathf.RoundToInt(kmh) : -1;
            if (k0 != lastKmh) { lastKmh = k0; speed.text = showRun ? k0 + " km/h" : ""; }
            // the score: as it is, except that what a combo has just banked waits for its number to fly up, then counts
            // up (HudPoints). The multiplier is the combo's own now, shown with it (the "x1.5 chain" line is gone)
            if (!showRun) { scoreTarget = -1; scoreCounting = false; if (lastPts != -1) { lastPts = -1; score.text = ""; } }
            else
            {
                scoreTarget = points;
                if (!scoreCounting) { scoreShown = points - (bankPending ? bankTotal : chipT >= 0f && !chipBill ? chipAmount : 0); ShowScore(); }
            }
            int s0 = showRun ? Mathf.Max(0, Mathf.RoundToInt(gap)) : -1;
            if (s0 != lastGap) { lastGap = s0; slide.text = showRun && s0 < 9999 ? "SLIDE " + s0 + " m" : ""; }   // 9999: not released yet
            slide.color = gap < 40f ? Brand.Alert : Brand.Snow;
            // the bar only while riding with the slide released, like its "SLIDE n m" (its dark track used to stay
            // up always: on the start pad, the end screen, and every other screen the HUD sits under)
            bool bar = showRun && s0 < 9999;
            if (slideBar.activeSelf != bar) slideBar.SetActive(bar);
            float k = Mathf.Clamp01(gap / gapMax);
            slideFill.rectTransform.anchorMax = new Vector2(showRun ? k : 0f, 1f);
            slideFill.color = gap < 40f ? Brand.Alert : Brand.Snow;
        }

        /// <summary>Run time and shields held; hidden when not riding.</summary>
        public void SetPower(float seconds, int shields, bool showRun)
        {
            int tenths = showRun ? Mathf.FloorToInt(seconds * 10f) : -1;
            if (tenths != lastTenths) { lastTenths = tenths; timer.text = showRun ? FormatTime(seconds) : ""; }
            for (int i = 0; i < shieldPips.Length; i++)
            {
                bool on = showRun && i < shields;
                if (shieldPips[i].enabled != on) shieldPips[i].enabled = on;
            }
        }

        static readonly string[] TimerNames = { "MOUNTAIN ZOOM", "SUPER JUMP", "INVINCIBLE" };
        static readonly Color[] TimerColors = { Brand.Gold, Brand.Gold, Brand.Gold };   // (one accent: the name tells them apart)

        /// <summary>
        /// The timed power-ups: seconds left of each (0 = not running) and how long a full one lasts.
        /// The running ones stack up from the top, with the last couple of seconds blinking.
        /// </summary>
        public void SetTimers(bool show, float zoom, float zoomFull, float jump, float jumpFull, float invincible, float invincibleFull)
        {
            int row = 0;
            for (int i = 0; i < TimerNames.Length; i++)
            {
                float left = i == 0 ? zoom : i == 1 ? jump : invincible, full = i == 0 ? zoomFull : i == 1 ? jumpFull : invincibleFull;
                bool on = show && left > 0f;
                if (timerBg[i].enabled != on) timerText[i].enabled = timerBg[i].enabled = timerFill[i].enabled = on;
                if (!on) { lastTimer[i] = -1; continue; }
                float y = -262f - row * 66f;
                row++;
                timerText[i].rectTransform.anchoredPosition = new Vector2(40f, y);
                timerBg[i].rectTransform.anchoredPosition = new Vector2(40f, y - 42f);
                timerFill[i].rectTransform.anchorMax = new Vector2(Mathf.Clamp01(left / Mathf.Max(full, left)), 1f);
                int tenths = Mathf.CeilToInt(left * 10f);
                if (tenths != lastTimer[i]) { lastTimer[i] = tenths; timerText[i].text = TimerNames[i] + "  " + (tenths / 10f).ToString("0.0") + "s"; }
                float a = left < 2f && Mathf.Repeat(left * 4f, 1f) < 0.4f ? 0.35f : 1f;
                var c = TimerColors[i]; c.a *= a;
                timerText[i].color = c; timerFill[i].color = c;
            }
        }

        /// <summary>The run's medical bill so far (hidden until there is one).</summary>
        public void SetMedical(float dollars, bool show)
        {
            // (less the fall being paid for right now: that is the big figure, and lands here when the rider gets up: HudPoints)
            medTotal = dollars; medShow = show;
            if (!show) medCounting = false;
            if (medCounting) return;
            medShown = show ? dollars - (billOn ? billTarget : 0f) - rideHeld : 0f;
            ShowMedical();
        }

        /// <summary>A pulsing instruction near the bottom (empty to hide).</summary>
        Brand.TapCue tapCue;

        /// <summary>Down after a fall, waiting for the tap: a dark dot with a gold ring spreading from it, fading in, no words (Brand.TapCue).</summary>
        public void SetTapCue(bool on) { if (tapCue != null) tapCue.Set(on); }

        public void SetPrompt(string text)
        {
            prompt.text = text;
            if (text.Length > 0) prompt.color = new Color(1f, 1f, 1f, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f)));
        }

        /// <summary>Height left above the finish at the base, in feet.</summary>
        public void SetAltitude(float feet, bool show)
        {
            int a = show ? Mathf.RoundToInt(feet / 10f) * 10 : -1;
            if (a == lastAlt) return;
            lastAlt = a;
            altitude.text = show ? a.ToString("N0") + " ft" : "";
        }

        public static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            float sec = seconds - m * 60f;
            return m + ":" + sec.ToString("00.0");
        }

        public void SetWhiteout(float a) { whiteout.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a)); }

        /// <summary>A message that fades out on its own.</summary>
        public void Flash(string line, string sub = "", float seconds = 1.1f)
        {
            big.text = line;
            small.text = sub;
            flashT = seconds;
            big.color = Color.white;
            small.color = Color.white;
        }

        /// <summary>A message that stays until replaced.</summary>
        public void Show(string line, string sub, string bottom)
        {
            big.text = line;
            small.text = sub;
            hint.text = bottom;
            // (the hint is the one long line here: fitted to the screen, where it used to run off both sides)
            // (a portrait phone is narrower than the 1080 the layout is drawn on: about 980 across)
            float room = safe.rect.width > 200f ? safe.rect.width - 70f - 48f : 860f;
            float hw = Wide(hint), hs = hw > room ? room / hw : 1f;
            hint.rectTransform.localScale = new Vector3(hs, hs, 1f);
            PlateUnder(hintPlate, hint, hs, 120f);
            PlateUnder(smallPlate, small, 1f, small.rectTransform.anchoredPosition.y);
            flashT = 0f;
            big.color = Color.white;
            small.color = Color.white;
        }

        Image hintPlate, smallPlate;
        /// <summary>A dark plate just bigger than a line of type (none under no type).</summary>
        void PlateUnder(Image plate, Text t, float scale, float y)
        {
            plate.enabled = !string.IsNullOrEmpty(t.text);
            if (!plate.enabled) return;
            var tr = t.rectTransform;
            float w = Wide(t) * scale + 56f, h = t.preferredHeight * scale + 28f;
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = tr.anchorMin;
            plate.rectTransform.pivot = new Vector2(0.5f, tr.pivot.y);
            plate.rectTransform.sizeDelta = new Vector2(w, h);
            // (the type's box is taller than its letters: the plate is centred on the letters)
            float off = tr.pivot.y < 0.25f ? -14f : tr.pivot.y > 0.75f ? 14f : 0f;
            plate.rectTransform.anchoredPosition = new Vector2(0f, y + off);
            plate.transform.SetSiblingIndex(t.transform.GetSiblingIndex());
        }

        public void Clear()
        {
            if (hintPlate != null) hintPlate.enabled = smallPlate.enabled = false;
            big.text = small.text = hint.text = "";
            flashT = 0f;
            if (rmode == RMode.End) ClearReceipt();   // (the end screen's receipt goes with it: HudReceipt)
        }
    }
}
