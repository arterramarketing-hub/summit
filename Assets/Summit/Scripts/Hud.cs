using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>The heads-up display, built in code so there is nothing to wire up.</summary>
    public class Hud : MonoBehaviour
    {
        int lastKmh = -2, lastPts = -2, lastMult = -1, lastGap = -2;
        RectTransform canvasRect, stickRing, stickKnob;
        Canvas canvasRef;
        Text diag;
        RectTransform playBtn, restartBtn, modeBtn, riderBtn;
        Text riderLabel;
        public void SetRider(bool tube) { riderLabel.text = tube ? "RIDER: SNOW TUBE  (tap to change)" : "RIDER: SNOWBOARD  (tap to change)"; }
        public bool OnRider(Vector2 p) => Inside(riderBtn, p);
        Text modeLabel;
        Text playLabel;
        Text speed, score, chain, slide, big, small, hint, timer, altitude, medical, prompt;
        int lastBill = -1;
        int lastAlt = -1;
        Image[] shieldPips;
        Image[] timerBg, timerFill;
        Text[] timerText;
        int[] lastTimer;
        int lastTenths = -2;
        Image slideFill, whiteout, hurt;
        Image[] fallPips;
        Text fallsLabel;
        float hurtA;
        RectTransform safe;
        float flashT;
        Rect lastSafe;
        Font font;

        public void Build()
        {
            font = LoadFont();
            var canvas = gameObject.AddComponent<Canvas>();
            canvasRef = canvas;
            canvasRect = (RectTransform)transform;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // full-screen white that rises as the slide closes in
            whiteout = NewImage("Whiteout", transform, new Color(1f, 1f, 1f, 0f));
            Stretch(whiteout.rectTransform);
            whiteout.raycastTarget = false;

            // a red flash when something breaks
            hurt = NewImage("Hurt", transform, new Color(0.85f, 0.05f, 0.05f, 0f));
            Stretch(hurt.rectTransform);
            hurt.raycastTarget = false;

            safe = new GameObject("Safe", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(transform, false);
            ApplySafeArea();

            speed = NewText("Speed", safe, new Vector2(0f, 1f), new Vector2(40f, -40f), 54, TextAnchor.UpperLeft);
            score = NewText("Score", safe, new Vector2(0.5f, 1f), new Vector2(0f, -34f), 84, TextAnchor.UpperCenter);
            chain = NewText("Chain", safe, new Vector2(0.5f, 1f), new Vector2(0f, -132f), 44, TextAnchor.UpperCenter);
            chain.color = new Color(1f, 0.82f, 0.35f);
            timer = NewText("Timer", safe, new Vector2(0f, 1f), new Vector2(40f, -104f), 46, TextAnchor.UpperLeft);
            timer.color = new Color(0.85f, 0.93f, 1f);
            medical = NewText("Medical", safe, new Vector2(1f, 1f), new Vector2(-40f, -232f), 40, TextAnchor.UpperRight);
            medical.color = new Color(1f, 0.45f, 0.4f);
            prompt = NewText("Prompt", safe, new Vector2(0.5f, 0f), new Vector2(0f, 230f), 58, TextAnchor.LowerCenter);
            altitude = NewText("Altitude", safe, new Vector2(0f, 1f), new Vector2(40f, -160f), 40, TextAnchor.UpperLeft);
            altitude.color = new Color(0.85f, 0.93f, 1f, 0.95f);
            slide = NewText("Slide", safe, new Vector2(1f, 1f), new Vector2(-40f, -40f), 44, TextAnchor.UpperRight);

            var barBg = NewImage("SlideBar", safe, new Color(0f, 0f, 0f, 0.35f));
            Place(barBg.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -104f), new Vector2(300f, 16f), new Vector2(1f, 1f));
            slideFill = NewImage("Fill", barBg.transform, new Color(1f, 1f, 1f, 0.95f));
            var fr = slideFill.rectTransform;
            fr.anchorMin = new Vector2(0f, 0f);
            fr.anchorMax = new Vector2(1f, 1f);
            fr.offsetMin = fr.offsetMax = Vector2.zero;

            big = NewText("Big", safe, new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), 110, TextAnchor.MiddleCenter);
            small = NewText("Small", safe, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), 50, TextAnchor.MiddleCenter);
            hint = NewText("Hint", safe, new Vector2(0.5f, 0f), new Vector2(0f, 120f), 44, TextAnchor.LowerCenter);
            hint.color = new Color(1f, 1f, 1f, 0.9f);

            diag = NewText("Diag", safe, new Vector2(0f, 0f), new Vector2(20f, 70f), 30, TextAnchor.UpperLeft);
            diag.color = new Color(1f, 1f, 0.6f, 0.9f);

            // the drag stick: a ring where the finger lands and a knob that follows it
            var circle = MakeCircle(96);

            // shields held (up to three) and the boost left in the tank
            shieldPips = new Image[Rider.MaxShields];
            for (int i = 0; i < shieldPips.Length; i++)
            {
                var pip = NewImage("Shield" + i, safe, new Color(0.45f, 0.78f, 1f, 0.95f));
                pip.sprite = circle;
                Place(pip.rectTransform, new Vector2(0f, 1f), new Vector2(40f + i * 54f, -218f), new Vector2(44f, 44f), new Vector2(0f, 1f));
                shieldPips[i] = pip;
            }
            // falls left this run: three red crosses, top right under the slide bar
            fallsLabel = NewText("FallsLabel", safe, new Vector2(1f, 1f), new Vector2(-40f, -136f), 30, TextAnchor.UpperRight);
            fallsLabel.text = "";
            fallPips = new Image[4];   // three, and a fourth with the spare-helmet upgrade
            var cross = MakeCross(64);
            for (int i = 0; i < fallPips.Length; i++)
            {
                var pip = NewImage("Fall" + i, safe, new Color(0.95f, 0.2f, 0.18f, 0.95f));
                pip.sprite = cross;
                Place(pip.rectTransform, new Vector2(1f, 1f), new Vector2(-40f - i * 52f, -176f), new Vector2(42f, 42f), new Vector2(1f, 1f));
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
                timerBg[i] = NewImage("TimerBar" + i, safe, new Color(0f, 0f, 0f, 0.35f));
                Place(timerBg[i].rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(260f, 14f), new Vector2(0f, 1f));
                timerFill[i] = NewImage("Fill", timerBg[i].transform, TimerColors[i]);
                var bf = timerFill[i].rectTransform;
                bf.anchorMin = Vector2.zero; bf.anchorMax = Vector2.one; bf.offsetMin = bf.offsetMax = Vector2.zero;
                timerText[i].enabled = timerBg[i].enabled = timerFill[i].enabled = false;
                lastTimer[i] = -1;
            }
            var ring = NewImage("StickRing", transform, new Color(1f, 1f, 1f, 0.22f));
            ring.sprite = circle; stickRing = ring.rectTransform;
            var knob = NewImage("StickKnob", transform, new Color(1f, 1f, 1f, 0.7f));
            knob.sprite = circle; stickKnob = knob.rectTransform;
            stickRing.gameObject.SetActive(false); stickKnob.gameObject.SetActive(false);

            // buttons (hit-tested by SummitGame, so no EventSystem is needed)
            var pb = NewImage("PlayButton", safe, new Color(0.10f, 0.45f, 0.90f, 0.95f));
            Place(pb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(520f, 150f), new Vector2(0.5f, 0.5f));
            playBtn = pb.rectTransform;
            playLabel = NewText("PlayLabel", pb.transform, new Vector2(0.5f, 0.5f), Vector2.zero, 70, TextAnchor.MiddleCenter);
            playLabel.text = "PLAY";

            var rb = NewImage("RestartButton", safe, new Color(0f, 0f, 0f, 0.4f));
            Place(rb.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(300f, 90f), new Vector2(0.5f, 0f));
            restartBtn = rb.rectTransform;
            var rl = NewText("RestartLabel", rb.transform, new Vector2(0.5f, 0.5f), Vector2.zero, 44, TextAnchor.MiddleCenter);
            rl.text = "II  PAUSE";
            BuildPause();
            var mb = NewImage("ModeButton", safe, new Color(0f, 0f, 0f, 0.4f));
            Place(mb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -280f), new Vector2(620f, 100f), new Vector2(0.5f, 0.5f));
            modeBtn = mb.rectTransform;
            modeLabel = NewText("ModeLabel", mb.transform, new Vector2(0.5f, 0.5f), Vector2.zero, 40, TextAnchor.MiddleCenter);
            var rbtn = NewImage("RiderButton", safe, new Color(0f, 0f, 0f, 0.4f));
            Place(rbtn.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -400f), new Vector2(620f, 100f), new Vector2(0.5f, 0.5f));
            riderBtn = rbtn.rectTransform;
            riderLabel = NewText("RiderLabel", rbtn.transform, new Vector2(0.5f, 0.5f), Vector2.zero, 40, TextAnchor.MiddleCenter);
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
                fallPips[i].color = i < left ? new Color(0.95f, 0.2f, 0.18f, 0.95f) : new Color(0.2f, 0.2f, 0.22f, 0.6f);
            }
            fallsLabel.text = show ? "FALLS" : "";
        }

        /// <summary>A red flash across the screen.</summary>
        public void Hurt(float a = 0.45f) { hurtA = Mathf.Max(hurtA, a); }

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

        public void SetMode(bool tilt) { modeLabel.text = tilt ? "STEERING: TILT  (tap to change)" : "STEERING: DRAG STICK  (tap to change)"; }
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

        // ---- the pause menu: RESUME always, then four tabs. VIEW: chase camera or first person, camera distance,
        //      angle (those two are for the chase camera: greyed out in first person), lens, lens type.
        //      LOOK: filter, filter strength, halation, time of day. GAME: steering, blood, trees, restart run.
        //      SOUND: music volume. A hidden tab's buttons are inactive, so they can't be hit.

        GameObject pausePanel;
        RectTransform resumeBtn, camMinus, camPlus, angMinus, angPlus, lensMinus, lensPlus, steerBtn, againBtn, treesBtn;
        Text camLabel, angLabel, lensLabel, steerLabel, treesLabel;
        RectTransform lensTypeMinus, lensTypePlus, filterMinus, filterPlus, strengthMinus, strengthPlus;
        Text lensTypeLabel, filterLabel, strengthLabel;
        RectTransform timeMinus, timePlus; Text timeLabel;
        RectTransform halationMinus, halationPlus, musicMinus, musicPlus; Text halationLabel, musicLabel;
        public static readonly string[] TabName = { "VIEW", "LOOK", "GAME", "SOUND" };
        RectTransform[] tabBtn; GameObject[] tabPage; int tab;
        static readonly Color TabOn = new Color(0.10f, 0.45f, 0.90f, 0.95f), TabOff = new Color(1f, 1f, 1f, 0.16f);

        RectTransform Button(Transform parent, string label, Vector2 pos, Vector2 size, Color c, out Text text, int fontSize = 52)
        {
            var b = NewImage(label + "Btn", parent, c);
            Place(b.rectTransform, new Vector2(0.5f, 0.5f), pos, size, new Vector2(0.5f, 0.5f));
            text = NewText(label + "Label", b.transform, new Vector2(0.5f, 0.5f), Vector2.zero, fontSize, TextAnchor.MiddleCenter);
            text.text = label;
            return b.rectTransform;
        }

        void BuildPause()
        {
            var dim = NewImage("Pause", transform, new Color(0.03f, 0.06f, 0.12f, 0.72f));
            Stretch(dim.rectTransform);
            pausePanel = dim.gameObject;
            var panel = new GameObject("PauseSafe", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(pausePanel.transform, false);
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.offsetMin = panel.offsetMax = Vector2.zero;
            var title = NewText("PausedTitle", panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 700f), 90, TextAnchor.MiddleCenter);
            title.text = "PAUSED";
            Color blue = new Color(0.10f, 0.45f, 0.90f, 0.95f), grey = new Color(1f, 1f, 1f, 0.16f);
            // the layout is 1920 high; a portrait iPhone's safe area is about +-940 of it: everything sits in +750..-500
            resumeBtn = Button(panel, "RESUME", new Vector2(0f, 560f), new Vector2(560f, 120f), blue, out _, 60);
            tabBtn = new RectTransform[TabName.Length]; tabPage = new GameObject[TabName.Length];
            for (int k = 0; k < TabName.Length; k++)
            {
                tabBtn[k] = Button(panel, TabName[k], new Vector2(-342f + 228f * k, 380f), new Vector2(214f, 110f), grey, out _, 42);
                var page = new GameObject(TabName[k] + "Tab", typeof(RectTransform)).GetComponent<RectTransform>();
                page.SetParent(panel, false);
                page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one; page.offsetMin = page.offsetMax = Vector2.zero;
                tabPage[k] = page.gameObject;
            }
            // a setting with - and + either side of its value; rows 200 apart, big buttons
            Text Row(int page, string label, float y, out RectTransform minus, out RectTransform plus)
            {
                var parent = tabPage[page].transform;
                var t = NewText(label + "Title", parent, new Vector2(0.5f, 0.5f), new Vector2(0f, y + 80f), 38, TextAnchor.MiddleCenter);
                t.text = label; t.color = new Color(1f, 1f, 1f, 0.75f);
                minus = Button(parent, "-", new Vector2(-270f, y), new Vector2(150f, 120f), grey, out _, 80);
                plus = Button(parent, "+", new Vector2(270f, y), new Vector2(150f, 120f), grey, out _, 80);
                return NewText(label + "Value", parent, new Vector2(0.5f, 0.5f), new Vector2(0f, y), 54, TextAnchor.MiddleCenter);
            }
            // VIEW: the first-person switch on top, and the four rows closed up under it (173 apart) to make room
            povBtn = Button(tabPage[0].transform, "FirstPerson", new Vector2(0f, 262f), new Vector2(700f, 100f), grey, out povLabel, 38);
            camLabel = Row(0, "CAMERA DISTANCE", 85f, out camMinus, out camPlus);
            angLabel = Row(0, "CAMERA ANGLE", -88f, out angMinus, out angPlus);
            lensLabel = Row(0, "LENS", -261f, out lensMinus, out lensPlus);
            lensTypeLabel = Row(0, "LENS TYPE", -434f, out lensTypeMinus, out lensTypePlus);
            // LOOK
            filterLabel = Row(1, "FILTER", 180f, out filterMinus, out filterPlus);
            strengthLabel = Row(1, "FILTER STRENGTH", -20f, out strengthMinus, out strengthPlus);
            halationLabel = Row(1, "HALATION", -220f, out halationMinus, out halationPlus);
            timeLabel = Row(1, "TIME OF DAY", -420f, out timeMinus, out timePlus);   // CYCLE, or one phase held
            // GAME
            var game = tabPage[2].transform;
            steerBtn = Button(game, "STEERING", new Vector2(0f, 200f), new Vector2(700f, 120f), grey, out steerLabel, 38);
            bloodBtn = Button(game, "BLOOD", new Vector2(0f, 50f), new Vector2(700f, 120f), grey, out bloodLabel, 38);
            treesBtn = Button(game, "TREES", new Vector2(0f, -100f), new Vector2(700f, 120f), grey, out treesLabel, 38);
            torchBtn = Button(game, "FLASHLIGHT", new Vector2(0f, -250f), new Vector2(700f, 120f), grey, out torchLabel, 38);
            againBtn = Button(game, "RESTART RUN", new Vector2(0f, -400f), new Vector2(560f, 120f), new Color(0.8f, 0.2f, 0.15f, 0.9f), out _, 48);
            // SOUND
            musicLabel = Row(3, "MUSIC", 180f, out musicMinus, out musicPlus);
            ShowTab(0);
            pausePanel.SetActive(false);
        }

        public void ShowPause(bool on)
        {
            pausePanel.SetActive(on);
            if (!on) return;
            pausePanel.transform.SetAsLastSibling();
            // a passing message ("Close call +175") would sit in the middle of the menu: let it go
            if (flashT > 0f) { flashT = 0f; big.text = ""; small.text = ""; }
        }
        public bool Paused => pausePanel.activeSelf;
        public void SetCamera(string name) { camLabel.text = name; }
        public void SetPauseSteer(bool tilt) { steerLabel.text = tilt ? "STEERING: TILT  (tap to change)" : "STEERING: DRAG  (tap to change)"; }
        public bool OnResume(Vector2 p) => Inside(resumeBtn, p);
        public void SetAngle(string name) { angLabel.text = name; }
        public void SetLens(string name) { lensLabel.text = name; }
        public bool OnAngMinus(Vector2 p) => Inside(angMinus, p);
        public bool OnAngPlus(Vector2 p) => Inside(angPlus, p);
        public bool OnLensMinus(Vector2 p) => Inside(lensMinus, p);
        public bool OnLensPlus(Vector2 p) => Inside(lensPlus, p);
        public bool OnCamMinus(Vector2 p) => Inside(camMinus, p);
        public bool OnCamPlus(Vector2 p) => Inside(camPlus, p);
        public bool OnPauseSteer(Vector2 p) => Inside(steerBtn, p);
        public bool OnPauseRestart(Vector2 p) => Inside(againBtn, p);
        RectTransform bloodBtn; Text bloodLabel;
        RectTransform torchBtn; Text torchLabel;
        public void SetFlashlight(bool on) { torchLabel.text = on ? "FLASHLIGHT: ON  (tap to change)" : "FLASHLIGHT: OFF  (tap to change)"; }
        public bool OnPauseFlashlight(Vector2 p) => Inside(torchBtn, p);
        public void SetBlood(bool on) { bloodLabel.text = on ? "BLOOD: ON  (tap to change)" : "BLOOD: OFF  (tap to change)"; }
        public bool OnPauseBlood(Vector2 p) => Inside(bloodBtn, p);
        public void SetLensType(string name) { lensTypeLabel.text = name; }
        public void SetFilter(string name) { filterLabel.text = name; }
        public void SetFilterStrength(string value) { strengthLabel.text = value; }
        public bool OnLensTypeMinus(Vector2 p) => Inside(lensTypeMinus, p);
        public bool OnLensTypePlus(Vector2 p) => Inside(lensTypePlus, p);
        public bool OnFilterMinus(Vector2 p) => Inside(filterMinus, p);
        public bool OnFilterPlus(Vector2 p) => Inside(filterPlus, p);
        public bool OnStrengthMinus(Vector2 p) => Inside(strengthMinus, p);
        public bool OnStrengthPlus(Vector2 p) => Inside(strengthPlus, p);
        public bool OnPauseTrees(Vector2 p) => Inside(treesBtn, p);
        public void SetPauseTrees(bool realistic) { treesLabel.text = realistic ? "TREES: REALISTIC  (tap to change)" : "TREES: ORIGINAL  (tap to change)"; }
        RectTransform povBtn; Text povLabel;
        public bool OnPausePov(Vector2 p) => Inside(povBtn, p);
        /// <summary>The VIEW button's state. In first person the distance and angle rows are greyed out: they set the chase camera.</summary>
        public void SetPausePov(bool firstPerson)
        {
            povLabel.text = firstPerson ? "VIEW: FIRST PERSON  (tap to change)" : "VIEW: CHASE CAMERA  (tap to change)";
            camLabel.color = angLabel.color = new Color(1f, 1f, 1f, firstPerson ? 0.35f : 1f);
            var c = new Color(1f, 1f, 1f, firstPerson ? 0.06f : 0.16f);
            camMinus.GetComponent<Image>().color = camPlus.GetComponent<Image>().color = c;
            angMinus.GetComponent<Image>().color = angPlus.GetComponent<Image>().color = c;
        }
        public void SetTime(string value) { timeLabel.text = value; }
        public void SetHalation(string value) { halationLabel.text = value; }
        public bool OnHalationMinus(Vector2 p) => Inside(halationMinus, p);
        public bool OnHalationPlus(Vector2 p) => Inside(halationPlus, p);
        public void SetMusic(string value) { musicLabel.text = value; }
        public bool OnMusicMinus(Vector2 p) => Inside(musicMinus, p);
        public bool OnMusicPlus(Vector2 p) => Inside(musicPlus, p);
        public int PauseTab => tab;
        /// <summary>A tap on a tab: shows that tab and returns true.</summary>
        public bool OnPauseTab(Vector2 p)
        {
            for (int k = 0; k < tabBtn.Length; k++)
                if (Inside(tabBtn[k], p)) { ShowTab(k); return true; }
            return false;
        }
        public void ShowTab(int k)
        {
            tab = Mathf.Clamp(k, 0, TabName.Length - 1);
            for (int i = 0; i < tabPage.Length; i++)
            {
                tabPage[i].SetActive(i == tab);
                tabBtn[i].GetComponent<Image>().color = i == tab ? TabOn : TabOff;
            }
        }
        public bool OnTimeMinus(Vector2 p) => Inside(timeMinus, p);
        public bool OnTimePlus(Vector2 p) => Inside(timePlus, p);

        static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            return f;
        }

        Text NewText(string name, Transform parent, Vector2 anchor, Vector2 pos, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
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
            Place(t.rectTransform, anchor, pos, new Vector2(900f, size * 1.3f), pivot);
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0.05f, 0.08f, 0.16f, 0.55f);
            sh.effectDistance = new Vector2(3f, -3f);
            return t;
        }

        static Image NewImage(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var i = go.AddComponent<Image>();
            i.color = c;
            i.raycastTarget = false;
            return i;
        }

        static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        void ApplySafeArea()
        {
            Rect a = Screen.safeArea;
            lastSafe = a;
            if (Screen.width <= 0 || Screen.height <= 0) { Stretch(safe); return; }
            safe.anchorMin = new Vector2(a.xMin / Screen.width, a.yMin / Screen.height);
            safe.anchorMax = new Vector2(a.xMax / Screen.width, a.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
        }

        void Update()
        {
            if (hurtA > 0f) { hurtA = Mathf.MoveTowards(hurtA, 0f, Time.deltaTime * 1.2f); hurt.color = new Color(0.85f, 0.05f, 0.05f, hurtA); }
            if (safe != null && Screen.safeArea != lastSafe) ApplySafeArea();
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
            int p0 = showRun ? points : -1;
            if (p0 != lastPts) { lastPts = p0; score.text = showRun ? points.ToString("N0") : ""; }
            int m0 = showRun && mult > 1.01f ? Mathf.RoundToInt(mult * 10f) : 0;
            if (m0 != lastMult) { lastMult = m0; chain.text = m0 > 0 ? "x" + mult.ToString("0.#") + " chain" : ""; }
            int s0 = showRun ? Mathf.Max(0, Mathf.RoundToInt(gap)) : -1;
            if (s0 != lastGap) { lastGap = s0; slide.text = showRun && s0 < 9999 ? "SLIDE " + s0 + " m" : ""; }   // 9999: not released yet
            slide.color = gap < 40f ? new Color(1f, 0.45f, 0.35f) : Color.white;
            float k = Mathf.Clamp01(gap / gapMax);
            slideFill.rectTransform.anchorMax = new Vector2(showRun ? k : 0f, 1f);
            slideFill.color = gap < 40f ? new Color(1f, 0.45f, 0.35f) : Color.white;
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
        static readonly Color[] TimerColors = { new Color(0.55f, 1f, 0.3f, 0.95f), new Color(1f, 0.5f, 0.78f, 0.95f), new Color(1f, 0.85f, 0.25f, 0.95f) };

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
            int d = show ? Mathf.RoundToInt(dollars) : -1;
            if (d == lastBill) return;
            lastBill = d;
            medical.text = show && d > 0 ? "MEDICAL  $" + d.ToString("N0") : "";
        }

        /// <summary>A pulsing instruction near the bottom (empty to hide).</summary>
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
            flashT = 0f;
            big.color = Color.white;
            small.color = Color.white;
        }

        public void Clear()
        {
            big.text = small.text = hint.text = "";
            flashT = 0f;
        }
    }
}
