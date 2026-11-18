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
    public class MetaUi : MonoBehaviour
    {
        public enum Mode { Off, Hub, Ready, Riding, End }
        public Mode Current { get; private set; }
        public System.Action Clicked, Bought, Denied;

        static readonly Color Navy = new Color(0.06f, 0.09f, 0.16f, 0.98f), Row = new Color(1f, 1f, 1f, 0.07f),
                              Blue = new Color(0.10f, 0.45f, 0.90f, 0.97f), Green = new Color(0.20f, 0.70f, 0.32f, 0.97f),
                              Gold = new Color(1f, 0.80f, 0.25f), Grey = new Color(1f, 1f, 1f, 0.14f), Dim = new Color(1f, 1f, 1f, 0.7f);

        Font font;
        Sprite round, coin, disc, ring;
        RectTransform safe, hubRoot, panelRoot, card, endRoot, toastRoot, stickRing, stickKnob, campBtn, rideBtn, contextBtn;
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
            round = Rounded(64, 26); coin = Coin(96); disc = Disc(128, false); ring = Disc(128, true);
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
            var pc = Img("PointsChip", hubRoot, new Color(0f, 0f, 0f, 0.45f), round);
            Place(pc.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -36f), new Vector2(340f, 100f), new Vector2(0f, 1f));
            Place(Img("Coin", pc.transform, Color.white, coin).rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(72f, 72f), new Vector2(0f, 0.5f));
            pointsText = Label(pc.transform, new Vector2(0f, 0.5f), new Vector2(104f, 0f), new Vector2(230f, 80f), 56, TextAnchor.MiddleLeft);
            var mc = Img("MissionsChip", hubRoot, new Color(0f, 0f, 0f, 0.45f), round);
            Place(mc.rectTransform, new Vector2(1f, 1f), new Vector2(-36f, -36f), new Vector2(400f, 100f), new Vector2(1f, 1f));
            missionsText = Label(mc.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 80f), 40, TextAnchor.MiddleCenter);
            buttons.Add(new KeyValuePair<RectTransform, string>(mc.rectTransform, "missions"));
            var cb = Img("Context", hubRoot, Blue, round);
            Place(cb.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(660f, 140f), new Vector2(0.5f, 0f));
            contextBtn = cb.rectTransform;
            contextImg = cb;
            contextText = Label(cb.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 120f), 52, TextAnchor.MiddleCenter);
            buttons.Add(new KeyValuePair<RectTransform, string>(contextBtn, "context"));
            hintText = Label(hubRoot, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1000f, 60f), 38, TextAnchor.MiddleCenter);
            hintText.color = Dim;
            var sr = Img("StickRing", transform, new Color(1f, 1f, 1f, 0.2f), ring); stickRing = sr.rectTransform;
            var sk = Img("StickKnob", transform, new Color(1f, 1f, 1f, 0.65f), disc); stickKnob = sk.rectTransform;
            stickRing.gameObject.SetActive(false); stickKnob.gameObject.SetActive(false);

            // ---- at the start pad: back to camp
            var cbk = Img("CampButton", safe, new Color(0f, 0f, 0f, 0.45f), round);
            Place(cbk.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -36f), new Vector2(300f, 100f), new Vector2(0f, 1f));
            Label(cbk.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 80f), 44, TextAnchor.MiddleCenter).text = "< CAMP";
            campBtn = cbk.rectTransform;
            buttons.Add(new KeyValuePair<RectTransform, string>(campBtn, "camp"));

            // ---- the end of a run: what it earned, and a quick way straight back to the start
            endRoot = New("End", safe);
            Place(endRoot, new Vector2(0.5f, 0.5f), new Vector2(0f, -330f), new Vector2(900f, 400f), new Vector2(0.5f, 1f));
            var eb = Img("EndCard", endRoot, new Color(0.03f, 0.06f, 0.12f, 0.78f), round); Stretch(eb.rectTransform);
            var eh = Label(endRoot, new Vector2(0f, 1f), new Vector2(40f, -26f), new Vector2(400f, 60f), 40, TextAnchor.UpperLeft);
            eh.text = "POINTS EARNED"; eh.color = Dim;
            Place(Img("EndCoin", endRoot, Color.white, coin).rectTransform, new Vector2(1f, 1f), new Vector2(-250f, -20f), new Vector2(64f, 64f), new Vector2(1f, 1f));
            endTotal = Label(endRoot, new Vector2(1f, 1f), new Vector2(-40f, -16f), new Vector2(220f, 80f), 64, TextAnchor.UpperRight);
            endTotal.color = Gold;
            endLines = Label(endRoot, new Vector2(0f, 1f), new Vector2(40f, -96f), new Vector2(820f, 220f), 34, TextAnchor.UpperLeft);
            endLines.verticalOverflow = VerticalWrapMode.Overflow; endLines.lineSpacing = 1.1f;
            endBalance = Label(endRoot, new Vector2(0f, 0f), new Vector2(40f, 26f), new Vector2(820f, 50f), 36, TextAnchor.LowerLeft);
            var rb = Img("RideAgain", safe, new Color(0f, 0f, 0f, 0.5f), round);
            Place(rb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(520f, 100f), new Vector2(0.5f, 0.5f));
            Label(rb.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 90f), 44, TextAnchor.MiddleCenter).text = "RIDE AGAIN";
            rideBtn = rb.rectTransform;
            buttons.Add(new KeyValuePair<RectTransform, string>(rideBtn, "ride"));

            // ---- mission-complete toast, below the score
            toastRoot = New("Toast", safe);
            Place(toastRoot, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(860f, 170f), new Vector2(0.5f, 1f));
            toastGroup = toastRoot.gameObject.AddComponent<CanvasGroup>();
            var tb = Img("ToastCard", toastRoot, new Color(0.08f, 0.10f, 0.18f, 0.9f), round); Stretch(tb.rectTransform);
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
            endRoot.gameObject.SetActive(m == Mode.End);
            rideBtn.gameObject.SetActive(m == Mode.End);
            if (m != Mode.Hub) { ClosePanel(false); SetStick(false, Vector2.zero, Vector2.zero); }
            if (m == Mode.Hub) { Refresh(); SetContext(Camp.Station.None); }
        }

        /// <summary>The balance and today's missions on the camp's chips.</summary>
        public void Refresh()
        {
            pointsText.text = Progress.Points.ToString("N0");
            missionsText.text = "MISSIONS  " + Missions.DoneCount + " / " + Missions.Today.Count;
        }

        public void SetContext(Camp.Station st)
        {
            string label = st == Camp.Station.Missions ? "DAILY MISSIONS" : st == Camp.Station.Upgrades ? "UPGRADES"
                         : st == Camp.Station.Style ? "OUTFITS" : st == Camp.Station.DropIn ? "DROP IN" : null;
            bool on = label != null && !PanelOpen;
            if (contextBtn.gameObject.activeSelf != on) contextBtn.gameObject.SetActive(on);
            if (on) { contextText.text = label; contextImg.color = st == Camp.Station.DropIn ? new Color(0.85f, 0.2f, 0.12f, 0.97f) : Blue; }
            hintText.text = PanelOpen ? "" : on ? "" : "Drag to walk  ·  tap the board, bench, rack or gate";
        }

        /// <summary>The walk stick under the thumb (screen pixels).</summary>
        public void SetStick(bool show, Vector2 origin, Vector2 pos)
        {
            if (stickRing.gameObject.activeSelf != show) { stickRing.gameObject.SetActive(show); stickKnob.gameObject.SetActive(show); }
            if (!show) return;
            var cr = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(cr, origin, null, out Vector2 o);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(cr, pos, null, out Vector2 k);
            float r = 150f;
            stickRing.anchoredPosition = o; stickRing.sizeDelta = Vector2.one * r * 2f;
            stickKnob.anchoredPosition = o + Vector2.ClampMagnitude(k - o, r); stickKnob.sizeDelta = Vector2.one * r * 0.9f;
        }

        /// <summary>The button under a screen point, or null. Panels sit on top of everything else.</summary>
        public string Hit(Vector2 p)
        {
            for (int i = buttons.Count - 1; i >= 0; i--)
            {
                var b = buttons[i];
                if (b.Key == null || !b.Key.gameObject.activeInHierarchy) continue;
                if (PanelOpen && !b.Key.IsChildOf(panelRoot)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(b.Key, p, null)) return b.Value;
            }
            return null;
        }

        // ---------------------------------------------------------------- panels

        public bool PanelOpen => panelRoot.gameObject.activeSelf;
        public int ShopTab { get; private set; } = -1;
        readonly List<GameObject> panelBits = new List<GameObject>();

        RectTransform OpenCard(float height, string title, string sub)
        {
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
            var t = Label(card, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(900f, 90f), 66, TextAnchor.UpperCenter);
            t.text = title;
            if (sub != null) { var s = Label(card, new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(900f, 50f), 34, TextAnchor.UpperCenter); s.text = sub; s.color = Dim; }
            var x = Img("Close", card, Grey, disc);
            Place(x.rectTransform, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(104f, 104f), new Vector2(1f, 1f));
            Label(x.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(100f, 100f), 56, TextAnchor.MiddleCenter).text = "X";
            buttons.Add(new KeyValuePair<RectTransform, string>(x.rectTransform, "close"));
            cardMsg = Label(card, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(900f, 50f), 34, TextAnchor.LowerCenter);
            cardMsg.color = new Color(1f, 0.5f, 0.42f);
            return card;
        }

        public void ClosePanel(bool click = true)
        {
            if (!PanelOpen) return;
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
                var r = Img("Mission", c, m.done ? new Color(0.2f, 0.7f, 0.32f, 0.18f) : Row, round);
                Place(r.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f - i * 290f), new Vector2(930f, 260f), new Vector2(0.5f, 1f));
                var tt = Label(r.transform, new Vector2(0f, 1f), new Vector2(36f, -28f), new Vector2(600f, 120f), 46, TextAnchor.UpperLeft);
                tt.horizontalOverflow = HorizontalWrapMode.Wrap; tt.text = m.title;
                Place(Img("Coin", r.transform, Color.white, coin).rectTransform, new Vector2(1f, 1f), new Vector2(-170f, -32f), new Vector2(60f, 60f), new Vector2(1f, 1f));
                var rw = Label(r.transform, new Vector2(1f, 1f), new Vector2(-30f, -28f), new Vector2(140f, 70f), 50, TextAnchor.UpperRight);
                rw.text = "+" + m.reward; rw.color = Gold;
                var bg = Img("Bar", r.transform, new Color(0f, 0f, 0f, 0.4f), round);
                Place(bg.rectTransform, new Vector2(0f, 0f), new Vector2(36f, 40f), new Vector2(560f, 30f), new Vector2(0f, 0f));
                var fill = Img("Fill", bg.transform, m.done ? Green : Gold, round);
                fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.04f, m.Fraction), 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var ct = Label(r.transform, new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(300f, 50f), 36, TextAnchor.LowerRight);
                ct.text = m.done ? "DONE" : m.Count; ct.color = m.done ? new Color(0.5f, 1f, 0.55f) : Dim;
            }
            var hint = Label(c, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(900f, 50f), 32, TextAnchor.LowerCenter);
            hint.text = "Missions pay out the moment you finish them, mid-run."; hint.color = Dim;
            Clicked?.Invoke();
        }

        public void ShowShop(int tab)
        {
            ShopTab = tab;
            var c = OpenCard(tab == 0 ? 1640f : 900f, tab == 0 ? "UPGRADES" : "OUTFITS", null);
            // balance, top left of the card
            Place(Img("Coin", c, Color.white, coin).rectTransform, new Vector2(0f, 1f), new Vector2(36f, -40f), new Vector2(64f, 64f), new Vector2(0f, 1f));
            var bal = Label(c, new Vector2(0f, 1f), new Vector2(112f, -40f), new Vector2(260f, 70f), 50, TextAnchor.UpperLeft);
            bal.text = Progress.Points.ToString("N0"); bal.color = Gold;
            for (int k = 0; k < 2; k++)
            {
                var tb = Img("Tab", c, k == tab ? Blue : Grey, round);
                Place(tb.rectTransform, new Vector2(0.5f, 1f), new Vector2(-230f + k * 460f, -130f), new Vector2(440f, 96f), new Vector2(0.5f, 1f));
                Label(tb.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 90f), 42, TextAnchor.MiddleCenter).text = k == 0 ? "UPGRADES" : "OUTFITS";
                buttons.Add(new KeyValuePair<RectTransform, string>(tb.rectTransform, "tab" + k));
            }
            if (tab == 0) GearRows(c); else StyleRows(c);
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
                    Label(b.transform, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(160f, 90f), 44, TextAnchor.MiddleRight).text = cost.ToString("N0");
                    buttons.Add(new KeyValuePair<RectTransform, string>(b.rectTransform, "buy" + (int)it.id));
                }
            }
        }

        void StyleRows(RectTransform c)
        {
            void Swatches(string label, float y, Color[] cols, string[] names, int worn, System.Func<int, bool> owns, string id)
            {
                var l = Label(c, new Vector2(0f, 1f), new Vector2(50f, y), new Vector2(600f, 60f), 40, TextAnchor.UpperLeft);
                l.text = label + "  ·  " + names[worn]; l.color = Dim;
                for (int i = 0; i < cols.Length; i++)
                {
                    float x = -375f + i * 150f;
                    if (i == worn)
                        Place(Img("Worn", c, Color.white, disc).rectTransform, new Vector2(0.5f, 1f), new Vector2(x, y - 62f), new Vector2(138f, 138f), new Vector2(0.5f, 1f));
                    var s = Img("Swatch", c, cols[i], disc);
                    Place(s.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, y - 70f), new Vector2(122f, 122f), new Vector2(0.5f, 1f));
                    buttons.Add(new KeyValuePair<RectTransform, string>(s.rectTransform, id + i));
                    if (!owns(i))
                    {
                        var pr = Label(c, new Vector2(0.5f, 1f), new Vector2(x, y - 200f), new Vector2(150f, 44f), 32, TextAnchor.UpperCenter);
                        pr.text = Progress.OutfitCost(i).ToString(); pr.color = Progress.Points >= Progress.OutfitCost(i) ? Gold : new Color(1f, 1f, 1f, 0.45f);
                    }
                }
            }
            Swatches("JACKET", -260f, Progress.Jackets, Progress.JacketNames, Progress.Jacket, Progress.OwnsJacket, "jacket");
            Swatches("BOARD", -540f, Progress.Boards, Progress.BoardNames, Progress.Board, Progress.OwnsBoard, "board");
        }

        /// <summary>Do what a panel button says. Returns false if the id isn't a panel/menu action.</summary>
        public bool Act(string id)
        {
            if (id == "close") { ClosePanel(); return true; }
            if (id == "missions") { ShowMissions(); return true; }
            if (id == "tab0" || id == "tab1") { Clicked?.Invoke(); ShowShop(id == "tab0" ? 0 : 1); return true; }
            if (id.StartsWith("buy"))
            {
                var u = (Progress.Up)int.Parse(id.Substring(3));
                int cost = Progress.NextCost(u);
                if (Progress.Buy(u)) { Bought?.Invoke(); ShowShop(0); cardMsg.text = Progress.Find(u).title + (Progress.Find(u).decor ? " is up in camp" : " fitted"); cardMsg.color = new Color(0.5f, 1f, 0.55f); }
                else { Denied?.Invoke(); cardMsg.text = "Need " + (cost - Progress.Points).ToString("N0") + " more points: ride to earn them"; cardMsg.color = new Color(1f, 0.5f, 0.42f); }
                return true;
            }
            if (id.StartsWith("jacket") || id.StartsWith("board"))
            {
                bool jk = id.StartsWith("jacket");
                int i = int.Parse(id.Substring(jk ? 6 : 5));
                bool owned = jk ? Progress.OwnsJacket(i) : Progress.OwnsBoard(i);
                if (jk ? Progress.PickJacket(i) : Progress.PickBoard(i)) { if (owned) Clicked?.Invoke(); else Bought?.Invoke(); ShowShop(1); }
                else { Denied?.Invoke(); cardMsg.text = "Need " + (Progress.OutfitCost(i) - Progress.Points).ToString("N0") + " more points"; }
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- toasts and the end of a run

        readonly Queue<Missions.Mission> toasts = new Queue<Missions.Mission>();
        float toastT = -1f;
        const float ToastIn = 0.3f, ToastHold = 2.6f, ToastOut = 0.45f;

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
            float dt = Time.unscaledDeltaTime;
            // paused (time stopped): the toast waits, hidden, so it never covers the pause menu's RESUME
            bool paused = Time.timeScale == 0f;
            if (paused) { if (toastT >= 0f) toastGroup.alpha = 0f; return; }
            if (toastT < 0f && toasts.Count > 0)
            {
                var m = toasts.Dequeue();
                toastTitle.text = m.title; toastReward.text = "+" + m.reward;
                toastRoot.gameObject.SetActive(true);
                toastT = 0f;
            }
            if (toastT >= 0f)
            {
                toastT += dt;
                float a = toastT < ToastIn ? toastT / ToastIn : toastT < ToastIn + ToastHold ? 1f : 1f - (toastT - ToastIn - ToastHold) / ToastOut;
                toastGroup.alpha = Mathf.Clamp01(a);
                toastRoot.anchoredPosition = new Vector2(0f, -300f + 40f * (1f - Mathf.Clamp01(toastT / ToastIn)));
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
            sh.effectColor = new Color(0.02f, 0.04f, 0.1f, 0.5f); sh.effectDistance = new Vector2(2f, -2f);
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
        public void UseCamera(Camera cam)
        {
            var cv = GetComponent<Canvas>();
            cv.renderMode = cam != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            cv.worldCamera = cam; cv.planeDistance = 0.5f;
        }
    }
}
