using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// The meta game's screens, on their own canvas over the HUD: the camp's points and missions chips,
    /// the walk stick, the missions board, the shop, mission-complete toasts and the points earned at the
    /// end of a run. Built in code like Hud; buttons are hit-tested by the caller (Hit), no EventSystem.
    /// </summary>
    public partial class MetaUi : MonoBehaviour
    {
        public enum Mode { Off, Hub, Ready, Riding, End }
        public Mode Current { get; private set; }
        public System.Action Clicked, Bought, Denied;

        // (the brand's colours: Brand. The old names stay so the closet and the talk read as before; blue and green are gold now)
        static readonly Color Navy = Brand.PanelStrong, Row = new Color(1f, 1f, 1f, 0.07f),
                              Blue = Brand.Gold, Green = Brand.Gold,
                              Gold = Brand.Gold, Grey = new Color(1f, 1f, 1f, 0.14f), Dim = Brand.SnowMuted;

        Font font;
        Sprite round, coin, disc, ring;
        RectTransform safe, hubRoot, panelRoot, card, endRoot, toastRoot, stickRing, stickKnob, campBtn, rideBtn, contextBtn, settingsBtn;
        RectTransform lookRing, lookKnob;   // the camp's second stick: the camera
        RectTransform stickHome;            // where the walk stick sits (bottom left, in the safe area)
        Image stickBase, stickRim, stickKnobImg;
        float stickR = 137f;                // the walk stick's radius on the canvas (StickHome)
        Image contextImg, fadeImg;

        /// <summary>Black over the whole screen, 0..1.</summary>
        public void SetFade(float a)
        {
            if (fadeImg == null) return;
            bool on = a > 0.001f;
            if (fadeImg.gameObject.activeSelf != on) fadeImg.gameObject.SetActive(on);
            if (on) fadeImg.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a));
        }
        Text pointsText, missionsText, contextText, hintText, toastHead, toastTitle, toastReward, endTotal, endLines, endBalance, cardMsg;
        CanvasGroup toastGroup;
        readonly List<KeyValuePair<RectTransform, string>> buttons = new List<KeyValuePair<RectTransform, string>>();
        Rect lastSafe;

        public void Build()
        {
            font = LoadFont();
            round = Brand.Rounded; coin = Coin(96); disc = Disc(128, false); ring = Disc(128, true);
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            safe = New("Safe", transform);
            ApplySafeArea();

            // ---- the camp: points and missions up top, the stick, and a button for the station you're at
            hubRoot = New("Hub", safe); Stretch(hubRoot);
            // the camp's chips, one row along the top, all a control high (45 pt): the points, the way back to the
            // start page (the brand's peak, in a square), today's missions, and in the corner the settings (settingsBtn)
            float H = Brand.Control;
            var pc = Brand.Plate("PointsChip", hubRoot, Brand.Panel);
            Place(pc.rectTransform, new Vector2(0f, 1f), new Vector2(Brand.Margin, -Brand.Margin), new Vector2(280f, H), new Vector2(0f, 1f));
            Place(Img("Coin", pc.transform, Color.white, coin).rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(60f, 60f), new Vector2(0f, 0.5f));
            pointsText = Brand.Line("Points", pc.transform, new Brand.Style(Brand.Weight.SemiBold, 40, 0.04f), TextAnchor.MiddleLeft, Brand.Snow, new Vector2(0f, 0.5f), new Vector2(98f, 1f), 170f);
            var tb0 = Brand.IconButton("TitleChip", hubRoot, Brand.Peak, new Vector2(0f, 1f), new Vector2(Brand.Margin + 280f + 16f, -Brand.Margin), new Vector2(0f, 1f));
            tb0.Find("Icon").GetComponent<Image>().color = Brand.Gold;
            buttons.Add(new KeyValuePair<RectTransform, string>(tb0, "title"));
            var mc = Brand.Plate("MissionsChip", hubRoot, Brand.Panel);
            Place(mc.rectTransform, new Vector2(1f, 1f), new Vector2(-Brand.Margin - H - 16f, -Brand.Margin), new Vector2(340f, H), new Vector2(1f, 1f));
            missionsText = Brand.Line("Missions", mc.transform, Brand.Type.Label, TextAnchor.MiddleCenter, Brand.Snow, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), 320f);
            buttons.Add(new KeyValuePair<RectTransform, string>(mc.rectTransform, "missions"));
            contextBtn = Brand.Button("Context", hubRoot, "", Brand.Kind.Primary, new Vector2(0.5f, 0f), new Vector2(0f, 300f), new Vector2(600f, 132f), out contextText);
            contextImg = contextBtn.Find("Fill").GetComponent<Image>();
            contextText.fontSize = 36;
            buttons.Add(new KeyValuePair<RectTransform, string>(contextBtn, "context"));
            // the controls, on a plate, fitted to the screen (SetContext)
            hintPlate = Brand.Plate("HintPlate", hubRoot, Brand.Panel);
            Place(hintPlate.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(600f, 80f), new Vector2(0.5f, 0.5f));
            hintText = Brand.Line("Hint", hubRoot, Brand.Type.Hint, TextAnchor.MiddleCenter, Brand.Snow, new Vector2(0.5f, 0f), new Vector2(0f, 96f), 1000f);
            // the walk stick, fixed in the bottom left corner: a translucent dark base with a fine rim (gold while it's
            // walking) and a white knob. stickHome marks its centre in the safe area; the parts are placed on it
            stickHome = New("StickHome", hubRoot);
            Place(stickHome, Vector2.zero, new Vector2(Brand.Margin + stickR, Brand.Margin + stickR), Vector2.zero, new Vector2(0.5f, 0.5f));
            stickBase = Brand.Img("StickRing", transform, Brand.A(Brand.Ink, 0.45f), Brand.Disc); stickRing = stickBase.rectTransform;
            stickRim = Brand.Img("StickRim", stickRing, Brand.SnowFaint, Brand.DiscRing);
            Place(stickRim.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            stickKnobImg = Brand.Img("StickKnob", transform, Brand.Snow, Brand.Disc); stickKnob = stickKnobImg.rectTransform;
            stickBase.raycastTarget = stickRim.raycastTarget = stickKnobImg.raycastTarget = false;
            stickRing.gameObject.SetActive(false); stickKnob.gameObject.SetActive(false);
            var lr = Img("LookRing", transform, new Color(0.75f, 0.88f, 1f, 0.2f), ring); lookRing = lr.rectTransform;
            var lk = Img("LookKnob", transform, new Color(0.75f, 0.88f, 1f, 0.65f), disc); lookKnob = lk.rectTransform;
            lookRing.gameObject.SetActive(false); lookKnob.gameObject.SetActive(false);
            // down after a fall off the camp's edge: a soft ripple, no words (a tap brings you back)
            tapCue = new Brand.TapCue(safe, new Vector2(0.5f, 0f), new Vector2(0f, 250f));
            tapDot = Img("TapDot", safe, new Color(1f, 1f, 1f, 0f), disc);
            Place(tapDot.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 250f), new Vector2(26f, 26f), new Vector2(0.5f, 0.5f));
            tapRipple = Img("TapRipple", safe, new Color(1f, 1f, 1f, 0f), disc);
            Place(tapRipple.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 250f), new Vector2(26f, 26f), new Vector2(0.5f, 0.5f));
            tapDot.raycastTarget = tapRipple.raycastTarget = false;
            tapDot.enabled = tapRipple.enabled = false;

            // ---- the settings, from the camp and the start pad: the sliders in the top right corner (SummitGame opens
            //      the HUD's settings sheet: Hud.ShowSettings). On a run the pause menu has them
            settingsBtn = Brand.IconButton("SettingsButton", safe, Brand.Sliders, new Vector2(1f, 1f), new Vector2(-Brand.Margin, -Brand.Margin), new Vector2(1f, 1f));
            buttons.Add(new KeyValuePair<RectTransform, string>(settingsBtn, "settings"));

            // ---- at the start pad: back to camp
            campBtn = Brand.Button("CampButton", safe, "CAMP", Brand.Kind.Secondary, new Vector2(0f, 1f), new Vector2(Brand.Margin + 120f, -Brand.Margin - Brand.Control * 0.5f), new Vector2(240f, Brand.Control), out var campLabel);
            campLabel.rectTransform.anchoredPosition = new Vector2(14f, 1f);
            var back = Img("Back", campBtn, Brand.Snow, Brand.Chevron);
            Place(back.rectTransform, new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(30f, 30f), new Vector2(0.5f, 0.5f));
            back.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            buttons.Add(new KeyValuePair<RectTransform, string>(campBtn, "camp"));

            // ---- the end of a run: what it earned, and a quick way straight back to the start
            endRoot = New("End", safe);
            Place(endRoot, new Vector2(0.5f, 0.5f), new Vector2(0f, -330f), new Vector2(900f, 400f), new Vector2(0.5f, 1f));
            var eb = Img("EndCard", endRoot, Brand.PanelStrong, round); Stretch(eb.rectTransform);
            var eh = Label(endRoot, new Vector2(0f, 1f), new Vector2(40f, -26f), new Vector2(400f, 60f), 40, TextAnchor.UpperLeft);
            eh.text = "POINTS EARNED"; eh.color = Dim;
            Place(Img("EndCoin", endRoot, Color.white, coin).rectTransform, new Vector2(1f, 1f), new Vector2(-250f, -20f), new Vector2(64f, 64f), new Vector2(1f, 1f));
            endTotal = Label(endRoot, new Vector2(1f, 1f), new Vector2(-40f, -16f), new Vector2(220f, 80f), 64, TextAnchor.UpperRight);
            endTotal.color = Gold;
            endLines = Label(endRoot, new Vector2(0f, 1f), new Vector2(40f, -96f), new Vector2(820f, 220f), 34, TextAnchor.UpperLeft);
            endLines.verticalOverflow = VerticalWrapMode.Overflow; endLines.lineSpacing = 1.1f;
            endBalance = Label(endRoot, new Vector2(0f, 0f), new Vector2(40f, 26f), new Vector2(820f, 50f), 36, TextAnchor.LowerLeft);
            rideBtn = Brand.Button("RideAgain", safe, "RIDE AGAIN", Brand.Kind.Secondary, new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(560f, Brand.Control));
            buttons.Add(new KeyValuePair<RectTransform, string>(rideBtn, "ride"));

            // ---- mission-complete toast: below the score, as ever, except during a run, where the combo and the medical
            //      bill run up there now (HudPoints): then it comes up at the bottom, over the pause button (see Update)
            toastRoot = New("Toast", safe);
            Place(toastRoot, new Vector2(0.5f, 0f), new Vector2(0f, ToastY), new Vector2(860f, 170f), new Vector2(0.5f, 0f));
            toastGroup = toastRoot.gameObject.AddComponent<CanvasGroup>();
            var tb = Img("ToastCard", toastRoot, Brand.PanelStrong, round); Stretch(tb.rectTransform);
            var tl = Img("ToastEdge", toastRoot, Gold, null);
            Place(tl.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 170f), new Vector2(0f, 0f));
            toastHead = Label(toastRoot, new Vector2(0f, 1f), new Vector2(44f, -20f), new Vector2(560f, 50f), 34, TextAnchor.UpperLeft);
            toastHead.color = Gold; toastHead.text = "MISSION COMPLETE";
            toastTitle = Label(toastRoot, new Vector2(0f, 1f), new Vector2(44f, -70f), new Vector2(600f, 90f), 40, TextAnchor.UpperLeft);
            toastTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(Img("ToastCoin", toastRoot, Color.white, coin).rectTransform, new Vector2(1f, 0.5f), new Vector2(-170f, 0f), new Vector2(60f, 60f), new Vector2(1f, 0.5f));
            toastReward = Label(toastRoot, new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(140f, 70f), 52, TextAnchor.MiddleRight);
            toastReward.color = Gold;
            toastRoot.gameObject.SetActive(false);

            // ---- the panel: a dim over everything and a card at the bottom (contents built per panel)
            panelRoot = New("Panel", transform); Stretch(panelRoot);
            var dim = Img("Dim", panelRoot, new Color(0.02f, 0.04f, 0.09f, 0.55f), null); Stretch(dim.rectTransform);
            dimImg = dim;   // (the closet has no dim: its studio shows through)
            panelRoot.gameObject.SetActive(false);
            // a black fade over everything (the camp's rescue after a fall)
            fadeImg = Img("Fade", transform, new Color(0f, 0f, 0f, 0f), null); Stretch(fadeImg.rectTransform);
            fadeImg.gameObject.SetActive(false);
            SetMode(Mode.Off);
        }

        // ---------------------------------------------------------------- modes

        CanvasGroup endGroup, rideGroup;
        /// <summary>Fade the end screen's points card and RIDE AGAIN in.</summary>
        public void SetEndAlpha(float a)
        {
            if (endGroup == null) endGroup = endRoot.gameObject.AddComponent<CanvasGroup>();
            if (rideGroup == null) rideGroup = rideBtn.gameObject.AddComponent<CanvasGroup>();
            endGroup.alpha = a; rideGroup.alpha = a;
        }

        public void SetMode(Mode m)
        {
            Current = m;
            hubRoot.gameObject.SetActive(m == Mode.Hub);
            campBtn.gameObject.SetActive(m == Mode.Ready);
            settingsBtn.gameObject.SetActive((m == Mode.Hub || m == Mode.Ready) && !PanelOpen);
            endRoot.gameObject.SetActive(m == Mode.End);
            rideBtn.gameObject.SetActive(m == Mode.End);
            if (m != Mode.Hub) { ClosePanel(false); SetStick(false, Vector2.zero, Vector2.zero); }
            if (m == Mode.Hub) { Refresh(); SetContext(Camp.Station.None); }
        }

        /// <summary>The balance and today's missions on the camp's chips.</summary>
        public void Refresh()
        {
            pointsText.text = Progress.Points.ToString("N0");
            missionsText.text = "MISSIONS  " + Missions.DoneCount + "/" + Missions.Today.Count;
        }

        /// <summary>The camp's say over the bottom button and the hint: a label of its own (null: the station's), and a mug in hand.</summary>
        public string ContextLabel; public bool Sipping, Quiet;
        Image hintPlate;

        public void SetContext(Camp.Station st)
        {
            string label = st == Camp.Station.Missions ? "DAILY MISSIONS" : st == Camp.Station.Upgrades ? "UPGRADES"
                         : st == Camp.Station.Style ? "WARDROBE" : st == Camp.Station.DropIn ? "DROP IN"
                         : st == Camp.Station.Talk ? "TALK" : st == Camp.Station.Roast ? "ROAST A MARSHMALLOW"
                         : st == Camp.Station.Cocoa ? "MAKE HOT CHOCOLATE" : null;
            if (ContextLabel != null) { label = ContextLabel; st = Camp.Station.None; }   // the fire's own button (CampFire: TAKE IT OUT)
            bool on = label != null && !PanelOpen;
            if (contextBtn.gameObject.activeSelf != on) contextBtn.gameObject.SetActive(on);
            if (on)
            {
                contextText.text = label;
                // dropping in is the one gold button that's a danger: it takes the alert colour
                bool drop = st == Camp.Station.DropIn;
                contextImg.color = drop ? Brand.Alert : Brand.Gold;
                contextText.color = drop ? Brand.Snow : Brand.Ink;
            }
            // (the stick shows how to walk: the line is for the rest)
            hintText.text = PanelOpen || on || Quiet ? "" : Sipping ? "Tap to sip" : "Drag to look   ·   tap to jump   ·   swipe to roll";
            FitHint();
        }

        /// <summary>The controls line fits the screen (two lines, then smaller, when it won't), on a plate just bigger than it.</summary>
        float hintFitW = -1f;
        void FitHint()
        {
            hintFitW = safe.rect.width;
            BottomRow(out float midX, out float roomW);
            bool show = !string.IsNullOrEmpty(hintText.text);
            hintPlate.enabled = show;
            if (!show) return;
            float room = roomW - 48f;
            string one = hintText.text.Replace("\n", "   ·   ");
            hintText.text = one;
            float w = Brand.Width(hintText);
            if (w > room)
            {
                // break after the middle separator
                int mid = one.Length / 2, cut = one.IndexOf("   ·   ", mid);
                if (cut < 0) cut = one.LastIndexOf("   ·   ", mid);
                if (cut > 0) hintText.text = one.Substring(0, cut) + "\n" + one.Substring(cut + 7);
                w = 0f;
                foreach (var line in hintText.text.Split('\n')) { hintText.text = line; w = Mathf.Max(w, Brand.Width(hintText)); }
                hintText.text = cut > 0 ? one.Substring(0, cut) + "\n" + one.Substring(cut + 7) : one;
            }
            float sc = w > room ? room / w : 1f;
            hintText.rectTransform.localScale = new Vector3(sc, sc, 1f);
            int lines = hintText.text.Split('\n').Length;
            float h = (lines * Brand.Type.Hint.size * 1.35f) * sc + 32f;
            hintPlate.rectTransform.sizeDelta = new Vector2(w * sc + 56f, h);
            // level with the stick (a plate taller than it sits on the bottom margin)
            hintPlate.rectTransform.anchoredPosition = hintText.rectTransform.anchoredPosition = new Vector2(midX, Mathf.Max(Brand.Margin + stickR, Brand.Margin + h * 0.5f));
        }

        /// <summary>The bottom row: the walk stick in the left corner, and the room to its right (where the station's
        /// button and the controls hint go, centred in it): its middle (x from the safe area's centre) and width.</summary>
        void BottomRow(out float midX, out float roomW)
        {
            // (never wider than a portrait phone's canvas, ~978: the test tools' editor canvas can stand wider than the shot)
            float w = Mathf.Min(safe.rect.width > 200f ? safe.rect.width : 978f, 978f);
            float left = Brand.Margin + 2f * stickR + 28f;
            roomW = w - left - Brand.Margin;
            midX = left + roomW * 0.5f - w * 0.5f;
            if (contextBtn == null) return;
            float bw = Mathf.Min(600f, roomW);
            contextBtn.sizeDelta = new Vector2(bw, 132f);
            contextBtn.anchoredPosition = new Vector2(midX, Brand.Margin + stickR);
            contextText.rectTransform.sizeDelta = new Vector2(bw - 24f, contextText.rectTransform.sizeDelta.y);
            contextText.resizeTextForBestFit = true; contextText.resizeTextMaxSize = 36; contextText.resizeTextMinSize = 24;
        }

        Brand.TapCue tapCue;
        Image tapDot, tapRipple;
        float tapCueAt = -1f, tapCueOn;

        /// <summary>Waiting for the tap after a fall: a small dot with a ripple spreading from it, fading in, no words.</summary>
        public void SetTapCue(bool on)
        {
            if (tapCue != null) { tapCue.Set(on); return; }
            if (tapDot == null) return;
            if (on && tapCueAt < 0f) tapCueAt = Time.unscaledTime;
            if (!on) tapCueAt = -1f;
            tapCueOn = on ? Mathf.MoveTowards(tapCueOn, 1f, Time.unscaledDeltaTime / 0.6f) : 0f;
            if (tapDot.enabled != on) tapDot.enabled = tapRipple.enabled = on;
            if (!on) return;
            float ph = ((Time.unscaledTime - tapCueAt) / 1.4f) % 1f;
            tapDot.color = new Color(1f, 1f, 1f, 0.55f * tapCueOn);
            tapRipple.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(26f, 120f, ph);
            tapRipple.color = new Color(1f, 1f, 1f, 0.28f * (1f - ph) * tapCueOn);
        }

        /// <summary>The camera stick under the right thumb (screen pixels), tinted so the two sticks read apart.</summary>
        public void SetLookStick(bool show, Vector2 origin, Vector2 pos, float radiusPx = 0f)
        {
            if (lookRing.gameObject.activeSelf != show) { lookRing.gameObject.SetActive(show); lookKnob.gameObject.SetActive(show); }
            if (!show) return;
            Vector2 o = ToCanvas(origin), k = ToCanvas(pos);
            float r = StickR(radiusPx);
            lookRing.anchoredPosition = o; lookRing.sizeDelta = Vector2.one * r * 2f;
            lookKnob.anchoredPosition = o + Vector2.ClampMagnitude(k - o, r); lookKnob.sizeDelta = Vector2.one * r * 0.9f;
        }

        /// <summary>A stick's ring radius on the canvas: the input's throw (screen pixels) when given, so the knob meets
        /// the rim just as the stick reaches full strength.</summary>
        float StickR(float radiusPx)
        {
            if (radiusPx <= 0f) return 150f;
            var cv = GetComponent<Canvas>();
            return radiusPx / Mathf.Max(0.01f, cv != null ? cv.scaleFactor : 1f);
        }

        /// <summary>
        /// Where the walk stick sits for a throw this big (screen pixels): its centre on the screen. It lays the bottom
        /// row out round it (the station's button and the hint to its right) when its size changes.
        /// </summary>
        public Vector2 StickHome(float radiusPx)
        {
            float r = StickR(radiusPx);
            if (!Mathf.Approximately(r, stickR)) { stickR = r; FitHint(); }
            stickHome.anchoredPosition = new Vector2(Brand.Margin + stickR, Brand.Margin + stickR);
            var cr = (RectTransform)transform;
            Vector2 local = cr.InverseTransformPoint(stickHome.position);
            return (local - cr.rect.min) * Scale;
        }

        /// <summary>Canvas units to screen pixels (the canvas spans the screen, or the camera's picture).</summary>
        float Scale { get { var cv = GetComponent<Canvas>(); return Mathf.Max(0.01f, cv != null ? cv.scaleFactor : 1f); } }
        /// <summary>A screen point (pixels) on the canvas's own plane. (Not through the camera: a camera-drawn canvas
        /// in the test tools follows its camera a frame late.)</summary>
        Vector2 ToCanvas(Vector2 px) { var cr = (RectTransform)transform; return px / Scale + cr.rect.min; }

        /// <summary>The walk stick (screen pixels): its base at origin, the knob at pos; active, while it walks (the rim
        /// turns gold).</summary>
        public void SetStick(bool show, Vector2 origin, Vector2 pos, float radiusPx = 0f, bool active = true)
        {
            if (stickRing.gameObject.activeSelf != show) { stickRing.gameObject.SetActive(show); stickKnob.gameObject.SetActive(show); }
            if (!show) return;
            Vector2 o = ToCanvas(origin), k = ToCanvas(pos);
            float r = StickR(radiusPx);
            stickRing.anchoredPosition = o; stickRing.sizeDelta = Vector2.one * r * 2f;
            stickRim.rectTransform.sizeDelta = Vector2.one * r * 2f / 0.92f;   // (the ring is drawn at 0.46 of its square)
            stickRim.color = active ? Brand.Gold : Brand.SnowFaint;
            stickBase.color = Brand.A(Brand.Ink, active ? 0.55f : 0.45f);
            stickKnob.anchoredPosition = o + Vector2.ClampMagnitude(k - o, r); stickKnob.sizeDelta = Vector2.one * r * 0.9f;
            stickKnobImg.color = Brand.A(Brand.Snow, active ? 1f : 0.85f);
        }

        /// <summary>The button under a screen point, or null. Panels sit on top of everything else.</summary>
        public string Hit(Vector2 p)
        {
            for (int i = buttons.Count - 1; i >= 0; i--)
            {
                var b = buttons[i];
                if (b.Key == null || !b.Key.gameObject.activeInHierarchy) continue;
                if (PanelOpen && !b.Key.IsChildOf(panelRoot)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(b.Key, p, UiCam)) return b.Value;
            }
            return null;
        }

        // ---------------------------------------------------------------- panels

        public bool PanelOpen => panelRoot.gameObject.activeSelf;
        public int ShopTab { get; private set; } = -1;
        readonly List<GameObject> panelBits = new List<GameObject>();

        RectTransform OpenCard(float height, string title, string sub)
        {
            if (ClosetOpen) EndCloset(false);
            foreach (var g in panelBits) { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }
            panelBits.Clear();
            buttons.RemoveAll(b => b.Key == null || b.Key.IsChildOf(panelRoot));
            panelRoot.gameObject.SetActive(true);
            var cardSafe = New("CardSafe", panelRoot); panelBits.Add(cardSafe.gameObject);
            CopySafe(cardSafe);
            var c = Img("Card", cardSafe, Navy, round);
            Place(c.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1010f, height), new Vector2(0.5f, 0f));
            card = c.rectTransform;
            // laid out 1010 wide; on a narrower screen (a 19.5:9 phone's canvas is ~978) the whole card shrinks to fit
            float room = cardSafe.rect.width - 40f;
            if (room > 1f && room < 1010f) card.localScale = Vector3.one * (room / 1010f);
            var t = Brand.Line("Title", card, new Brand.Style(Brand.Weight.Light, 52, 0.26f), TextAnchor.UpperCenter, Brand.Snow, new Vector2(0.5f, 1f), new Vector2(0f, -44f), 800f);
            t.text = title;
            if (sub != null) { var s = Brand.Line("Sub", card, Brand.Type.Label, TextAnchor.UpperCenter, Dim, new Vector2(0.5f, 1f), new Vector2(0f, -122f), 800f); s.text = sub.ToUpperInvariant(); }
            var x = Brand.IconButton("Close", card, Brand.Close, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(1f, 1f));
            x.Find("Fill").GetComponent<Image>().color = Grey;
            buttons.Add(new KeyValuePair<RectTransform, string>(x, "close"));
            cardMsg = Label(card, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(900f, 50f), 34, TextAnchor.LowerCenter);
            cardMsg.color = new Color(1f, 0.5f, 0.42f);
            return card;
        }

        public void ClosePanel(bool click = true)
        {
            if (!PanelOpen) return;
            if (ClosetOpen) { EndCloset(click); return; }
            if (click) Clicked?.Invoke();
            panelRoot.gameObject.SetActive(false);
            ShopTab = -1;
            Refresh();
        }

        public void ShowMissions()
        {
            Missions.Refresh();
            var c = OpenCard(1180f, "DAILY MISSIONS", "new missions in " + Missions.ResetsInText);
            for (int i = 0; i < Missions.Today.Count; i++)
            {
                var m = Missions.Today[i];
                var r = Img("Mission", c, m.done ? Brand.A(Brand.Gold, 0.16f) : Row, round);
                Place(r.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f - i * 290f), new Vector2(930f, 260f), new Vector2(0.5f, 1f));
                var tt = Label(r.transform, new Vector2(0f, 1f), new Vector2(36f, -28f), new Vector2(600f, 120f), 46, TextAnchor.UpperLeft);
                tt.horizontalOverflow = HorizontalWrapMode.Wrap; tt.text = Missions.TitleFor(m);
                Place(Img("Coin", r.transform, Color.white, coin).rectTransform, new Vector2(1f, 1f), new Vector2(-170f, -32f), new Vector2(60f, 60f), new Vector2(1f, 1f));
                var rw = Label(r.transform, new Vector2(1f, 1f), new Vector2(-30f, -28f), new Vector2(140f, 70f), 50, TextAnchor.UpperRight);
                rw.text = "+" + m.reward; rw.color = Gold;
                var bg = Img("Bar", r.transform, new Color(0f, 0f, 0f, 0.4f), round);
                Place(bg.rectTransform, new Vector2(0f, 0f), new Vector2(36f, 40f), new Vector2(560f, 30f), new Vector2(0f, 0f));
                var fill = Img("Fill", bg.transform, m.done ? Green : Gold, round);
                fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.04f, m.Fraction), 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var ct = Label(r.transform, new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(300f, 50f), 36, TextAnchor.LowerRight);
                ct.text = m.done ? "DONE" : m.Count; ct.color = m.done ? Brand.Gold : Dim;
            }
            var hint = Label(c, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(900f, 50f), 32, TextAnchor.LowerCenter);
            hint.text = "Missions pay out the moment you finish them, mid-run."; hint.color = Dim;
            Clicked?.Invoke();
        }

        public void ShowShop(int tab)
        {
            if (tab == 1) { ShowCloset(); return; }   // the outfits are the closet now (MetaUiCloset)
            ShopTab = tab;
            var c = OpenCard(tab == 0 ? 1640f : 1180f, tab == 0 ? "UPGRADES" : "OUTFITS & RIDE", null);
            // balance, top left of the card
            Place(Img("Coin", c, Color.white, coin).rectTransform, new Vector2(0f, 1f), new Vector2(36f, -40f), new Vector2(64f, 64f), new Vector2(0f, 1f));
            var bal = Label(c, new Vector2(0f, 1f), new Vector2(112f, -40f), new Vector2(260f, 70f), 50, TextAnchor.UpperLeft);
            bal.text = Progress.Points.ToString("N0"); bal.color = Gold;
            for (int k = 0; k < 2; k++)
            {
                var tb = Img("Tab", c, k == tab ? Brand.Snow : Grey, round);
                Place(tb.rectTransform, new Vector2(0.5f, 1f), new Vector2(-230f + k * 460f, -130f), new Vector2(440f, 96f), new Vector2(0.5f, 1f));
                var tl = Brand.Line("Text", tb.transform, Brand.Type.Button, TextAnchor.MiddleCenter, k == tab ? Brand.Ink : Brand.Snow, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), 420f);
                tl.text = k == 0 ? "UPGRADES" : "OUTFITS";
                buttons.Add(new KeyValuePair<RectTransform, string>(tb.rectTransform, "tab" + k));
            }
            GearRows(c);
        }

        void GearRows(RectTransform c)
        {
            for (int i = 0; i < Progress.Shop.Length; i++)
            {
                var it = Progress.Shop[i];
                int lvl = Progress.Level(it.id);
                bool maxed = Progress.Maxed(it.id);
                int cost = Progress.NextCost(it.id);
                var r = Img("Item", c, Row, round);
                Place(r.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -256f - i * 152f), new Vector2(930f, 140f), new Vector2(0.5f, 1f));
                var tt = Label(r.transform, new Vector2(0f, 1f), new Vector2(30f, -18f), new Vector2(500f, 60f), 42, TextAnchor.UpperLeft);
                tt.text = it.title;
                var bl = Label(r.transform, new Vector2(0f, 0f), new Vector2(30f, 18f), new Vector2(560f, 50f), 30, TextAnchor.LowerLeft);
                bl.text = it.blurb; bl.color = Dim;
                if (it.Max > 1)
                    for (int k = 0; k < it.Max; k++)
                        Place(Img("Pip", r.transform, k < lvl ? Gold : new Color(1f, 1f, 1f, 0.2f), round).rectTransform,
                              new Vector2(0f, 1f), new Vector2(470f + k * (it.Max > 5 ? 19f : 46f), it.Max > 5 ? -36f : -28f),
                              it.Max > 5 ? new Vector2(15f, 15f) : new Vector2(34f, 34f), new Vector2(0f, 1f));   // ten levels: small pips
                bool afford = !maxed && Progress.Points >= cost;
                var b = Img("Buy", r.transform, maxed ? new Color(1f, 1f, 1f, 0.08f) : afford ? Green : Grey, round);
                Place(b.rectTransform, new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(250f, 104f), new Vector2(1f, 0.5f));
                if (maxed)
                    Label(b.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 90f), 40, TextAnchor.MiddleCenter).text = it.Max > 1 ? "MAXED" : "OWNED";
                else
                {
                    Place(Img("Coin", b.transform, Color.white, coin).rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(52f, 52f), new Vector2(0f, 0.5f));
                    var cl = Label(b.transform, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(160f, 90f), 40, TextAnchor.MiddleRight);
                    cl.text = cost.ToString("N0"); if (afford) { cl.color = Brand.Ink; cl.GetComponent<Shadow>().enabled = false; }
                    buttons.Add(new KeyValuePair<RectTransform, string>(b.rectTransform, "buy" + (int)it.id));
                }
            }
        }

        /// <summary>The camp's peak chip was touched: SummitGame takes it from here (back to the start page).</summary>
        public bool TitleAsked;
        /// <summary>The sliders were touched (the camp or the start pad): SummitGame opens the settings.</summary>
        public bool SettingsAsked;

        /// <summary>Do what a panel button says. Returns false if the id isn't a panel/menu action.</summary>
        public bool Act(string id)
        {
            if (id == "close") { ClosePanel(); return true; }
            if (id == "missions") { ShowMissions(); return true; }
            if (id == "title") { TitleAsked = true; Clicked?.Invoke(); return true; }
            if (id == "settings") { SettingsAsked = true; Clicked?.Invoke(); return true; }
            if (id == "tab0" || id == "tab1") { Clicked?.Invoke(); ShowShop(id == "tab0" ? 0 : 1); return true; }
            if (id.StartsWith("buy"))
            {
                var u = (Progress.Up)int.Parse(id.Substring(3));
                int cost = Progress.NextCost(u);
                if (Progress.Buy(u)) { Bought?.Invoke(); ShowShop(0); cardMsg.text = Progress.Find(u).title + (Progress.Find(u).decor ? " is up in camp" : " fitted"); cardMsg.color = new Color(0.5f, 1f, 0.55f); }
                else { Denied?.Invoke(); cardMsg.text = "Need " + (cost - Progress.Points).ToString("N0") + " more points: ride to earn them"; cardMsg.color = new Color(1f, 0.5f, 0.42f); }
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- toasts and the end of a run

        readonly Queue<Missions.Mission> toasts = new Queue<Missions.Mission>();
        float toastT = -1f;
        const float ToastIn = 0.3f, ToastHold = 2.6f, ToastOut = 0.45f, ToastY = 165f;
        bool toastLow;

        public void Toast(Missions.Mission m) { toasts.Enqueue(m); }

        public void ShowEnd(Progress.Reward r, int metres, bool finished)
        {
            endTotal.text = "+" + r.Total.ToString("N0");
            var sb = new System.Text.StringBuilder();
            sb.Append("Score  +").Append(r.fromScore).Append("      Distance  ").Append(metres.ToString("N0")).Append(" m  +").Append(r.fromDistance).Append('\n');
            sb.Append("Tricks  ").Append(r.tricks).Append("  +").Append(r.fromTricks);
            if (finished) sb.Append("      Finished  +").Append(r.fromFinish);
            sb.Append('\n');
            sb.Append(r.fromMissions > 0 ? "Missions  +" + r.fromMissions : "Missions  " + Missions.DoneCount + " / " + Missions.Today.Count + " done today");
            endLines.text = sb.ToString();
            endBalance.text = "BALANCE  " + Progress.Points.ToString("N0");
        }

        void Update()
        {
            if (safe != null && Screen.safeArea != lastSafe) ApplySafeArea();
            // (the hint is fitted to the screen's width: again whenever that changes)
            if (hintPlate != null && hintPlate.enabled && !Mathf.Approximately(safe.rect.width, hintFitW)) FitHint();
            // the sliders: the camp and the start pad, not over a card or the closet
            if (settingsBtn != null)
            {
                bool sOn = (Current == Mode.Hub || Current == Mode.Ready) && !PanelOpen && !ClosetOpen;
                if (settingsBtn.gameObject.activeSelf != sOn) settingsBtn.gameObject.SetActive(sOn);
            }
            float dt = Time.unscaledDeltaTime;
            // paused (time stopped): the toast waits, hidden, so it never covers the pause menu's RESUME
            bool paused = Time.timeScale == 0f;
            if (paused) { if (toastT >= 0f) toastGroup.alpha = 0f; return; }
            if (toastT < 0f && toasts.Count > 0)
            {
                var m = toasts.Dequeue();
                toastTitle.text = Missions.TitleFor(m); toastReward.text = "+" + m.reward;
                toastRoot.gameObject.SetActive(true);
                toastT = 0f;
                toastLow = Current == Mode.Riding;
                toastRoot.anchorMin = toastRoot.anchorMax = toastRoot.pivot = new Vector2(0.5f, toastLow ? 0f : 1f);
            }
            if (toastT >= 0f)
            {
                toastT += dt;
                float a = toastT < ToastIn ? toastT / ToastIn : toastT < ToastIn + ToastHold ? 1f : 1f - (toastT - ToastIn - ToastHold) / ToastOut;
                toastGroup.alpha = Mathf.Clamp01(a);
                float slide = 40f * (1f - Mathf.Clamp01(toastT / ToastIn));
                toastRoot.anchoredPosition = toastLow ? new Vector2(0f, ToastY - slide) : new Vector2(0f, -300f + slide);
                if (toastT > ToastIn + ToastHold + ToastOut) { toastT = -1f; toastRoot.gameObject.SetActive(false); }
            }
        }

        /// <summary>Test tools: show a toast's middle frame now.</summary>
        public void ToastNow(Missions.Mission m) { toasts.Clear(); toasts.Enqueue(m); toastT = -1f; Update(); toastT = ToastIn + 0.5f; Update(); }

        // ---------------------------------------------------------------- building blocks

        static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            return f;
        }

        static RectTransform New(string name, Transform parent)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            return r;
        }

        static Image Img(string name, Transform parent, Color c, Sprite s)
        {
            var i = New(name, parent).gameObject.AddComponent<Image>();
            i.color = c; i.raycastTarget = false;
            if (s != null) { i.sprite = s; i.type = s.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple; }
            return i;
        }

        Text Label(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align)
        {
            var t = New("Text", parent).gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = fontSize; t.fontStyle = FontStyle.Bold; t.alignment = align; t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            float px = align == TextAnchor.UpperLeft || align == TextAnchor.MiddleLeft || align == TextAnchor.LowerLeft ? 0f
                     : align == TextAnchor.UpperRight || align == TextAnchor.MiddleRight || align == TextAnchor.LowerRight ? 1f : 0.5f;
            Place(t.rectTransform, anchor, pos, size, new Vector2(px, anchor.y));
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = Brand.A(Brand.Ink, 0.45f); sh.effectDistance = new Vector2(0f, -2f);
            return t;
        }

        static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        void ApplySafeArea()
        {
            lastSafe = Screen.safeArea;
            CopySafe(safe);
        }

        static void CopySafe(RectTransform r)
        {
            Rect a = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) { Stretch(r); return; }
            r.anchorMin = new Vector2(a.xMin / Screen.width, a.yMin / Screen.height);
            r.anchorMax = new Vector2(a.xMax / Screen.width, a.yMax / Screen.height);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        /// <summary>A white rounded square, 9-sliced so it stretches to any button.</summary>
        static Sprite Rounded(int px, int radius)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (px - radius))), dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (px - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d + 0.5f)));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite Disc(int px, bool hollow)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (px - 1) * 0.5f;
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(c - d);
                    if (hollow) a *= Mathf.Clamp01(d - (c - 10f));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>A mountain in two strokes: a peak and the shoulder beside it.</summary>
        static Sprite Peak(int px)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Vector2[] pts = { new Vector2(0.12f, 0.26f), new Vector2(0.44f, 0.78f), new Vector2(0.60f, 0.52f), new Vector2(0.70f, 0.62f), new Vector2(0.90f, 0.26f) };
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    var p = new Vector2((x + 0.5f) / px, (y + 0.5f) / px);
                    float d = 9f;
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        Vector2 ab = pts[i + 1] - pts[i]; float h = Mathf.Clamp01(Vector2.Dot(p - pts[i], ab) / ab.sqrMagnitude);
                        d = Mathf.Min(d, (p - pts[i] - ab * h).magnitude);
                    }
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((0.04f - d) * px + 0.5f)));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>The points coin: a gold disc with a rim, a shine and a mountain stamped in the middle.</summary>
        static Sprite Coin(int px)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (px - 1) * 0.5f;
            Color rim = new Color(0.78f, 0.52f, 0.10f), face = new Color(1f, 0.80f, 0.25f), mark = new Color(0.85f, 0.58f, 0.12f);
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                    Color col = d > 0.82f ? rim : face;
                    // a two-peak mountain
                    float u = dx / c, v = dy / c;
                    bool peak = v > -0.38f && (v < 0.42f - Mathf.Abs(u + 0.14f) * 1.5f || v < 0.18f - Mathf.Abs(u - 0.3f) * 1.5f);
                    if (d < 0.7f && peak) col = mark;
                    col = Color.Lerp(col, Color.white, Mathf.Clamp01(0.35f - Mathf.Abs(u + v + 0.6f) * 1.5f) * (d < 0.82f ? 1f : 0f));
                    col.a = Mathf.Clamp01((1f - d) * c);
                    t.SetPixel(x, y, col);
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Test tools: render the overlay with a camera (overlay canvases don't draw into render textures).</summary>
        /// <summary>The camera the menus are drawn by (null when they're an overlay, as on the phone).</summary>
        Camera UiCam { get { var cv = GetComponent<Canvas>(); return cv != null && cv.renderMode == RenderMode.ScreenSpaceCamera ? cv.worldCamera : null; } }

        public void UseCamera(Camera cam)
        {
            var cv = GetComponent<Canvas>();
            cv.renderMode = cam != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            cv.worldCamera = cam; cv.planeDistance = 0.5f;
            Canvas.ForceUpdateCanvases();
            if (hintPlate != null && hintPlate.enabled) FitHint();
        }
    }
}
