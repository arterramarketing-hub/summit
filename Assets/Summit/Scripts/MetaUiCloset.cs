using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Summit
{
    /// <summary>
    /// The closet (the board rack's station): a wardrobe screen in the manner of a dressing-room menu. Down the left,
    /// a dark see-through panel: the categories as tabs, and the category's items in a list you drag or flick, the
    /// highlighted row white. On the right, the hero in a little studio (ClosetStage), wearing whatever row is
    /// highlighted, before you buy it; drag there to turn them round. Under both, BACK and BUY / WEAR.
    ///
    /// A try-on only ever paints the rider (MetaUi tells the game through TriedOn; TryOn is the outfit to show); what
    /// is worn changes only through BUY or WEAR (Progress). BACK, or anything else that shuts the closet, puts the
    /// worn outfit back on, so leaving never keeps anything you didn't buy or pick.
    /// The camp hands its touches straight to ClosetStep while the closet is open.
    /// </summary>
    public partial class MetaUi
    {
        /// <summary>The studio the hero is shown in (set by the game).</summary>
        public ClosetStage Stage;
        /// <summary>Raised when the rider should be repainted: with TryOn while the closet is open, else what is worn.</summary>
        public System.Action TriedOn;
        /// <summary>The outfit on show while the closet is open (what is worn, with the highlighted row tried on); else null.</summary>
        public int[] TryOn { get; private set; }
        public bool ClosetOpen { get; private set; }
        /// <summary>Test tools: the category and row highlighted.</summary>
        public Wardrobe.Cat ClosetCat => cat;
        public int ClosetItem => hi;

        static readonly Color Ink = new Color(0.05f, 0.07f, 0.13f), PanelBg = new Color(0.02f, 0.03f, 0.07f, 0.8f),
                              Banner = Brand.PanelStrong, Worn = Brand.Gold,
                              WornDark = new Color(0.08f, 0.50f, 0.20f), GoldDark = new Color(0.62f, 0.40f, 0.02f);
        const float RowH = 96f, RowGap = 8f;

        Wardrobe.Cat cat; int hi;
        readonly List<Wardrobe.Cat> cats = new List<Wardrobe.Cat>();
        RectTransform closetRoot, listView, listContent, buyBtn, backBtn, previewZone;
        Image dimImg, buyImg, buyCoin;
        Text buyText, catText, countText, detailText, msgText, balText;
        class RowView { public RectTransform rt; public Image bg, chip, chip2, coin, thumb; public Text name, stat; public int item; }
        readonly List<RowView> rows = new List<RowView>();
        class TabView { public RectTransform rt; public Image bg; public Text text; public Wardrobe.Cat c; }
        readonly List<TabView> tabs = new List<TabView>();
        float scroll, scrollVel;
        // the finger on the closet
        enum Zone { None, Tab, List, Preview, Back, Buy }
        bool pDown, pMoved; Zone pZone; int pTab; Vector2 pStart, pLast; float pT, scrollAtDown;

        // ---------------------------------------------------------------- opening and closing

        public void ShowCloset()
        {
            // a fresh panel with nothing of a card's in it: the studio shows through, not the dim
            foreach (var g in panelBits) { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }
            panelBits.Clear();
            buttons.RemoveAll(b => b.Key == null || b.Key.IsChildOf(panelRoot));
            panelRoot.gameObject.SetActive(true);
            if (dimImg != null) dimImg.gameObject.SetActive(false);
            hubRoot.gameObject.SetActive(false);   // (its chips sit where the closet's header goes)
            ShopTab = 1;
            ClosetOpen = true;
            cats.Clear();
            foreach (var c in Wardrobe.Order) cats.Add(c);   // (who you are and the set of clothes first)
            BuildCloset();
            if (Stage != null) { Stage.Show(Progress.Ride); }
            SelectCat(Wardrobe.Cat.Jacket, false);
            Clicked?.Invoke();
        }

        /// <summary>Shut the closet: the worn outfit goes back on, the camera goes back to the camp.</summary>
        void EndCloset(bool click)
        {
            ClosetOpen = false;
            TryOn = null;
            TriedOn?.Invoke();
            if (Stage != null) Stage.Hide();
            if (dimImg != null) dimImg.gameObject.SetActive(true);
            hubRoot.gameObject.SetActive(Current == Mode.Hub);
            panelRoot.gameObject.SetActive(false);
            ShopTab = -1;
            pDown = false;
            if (click) Clicked?.Invoke();
            Refresh();
        }

        // ---------------------------------------------------------------- building it

        void BuildCloset()
        {
            closetRoot = New("Closet", panelRoot); panelBits.Add(closetRoot.gameObject);
            CopySafe(closetRoot);
            float W = closetRoot.rect.width, H = closetRoot.rect.height;
            if (W < 10f) { W = 1080f; H = 1920f; }
            float panelW = Mathf.Round(W * 0.58f) - 24f, footer = 190f;
            tabs.Clear(); rows.Clear();

            // the money, top right over the studio
            var bc = Img("Balance", closetRoot, new Color(0f, 0f, 0f, 0.45f), round);
            Place(bc.rectTransform, new Vector2(1f, 1f), new Vector2(-24f, -28f), new Vector2(300f, 92f), new Vector2(1f, 1f));
            Place(Img("Coin", bc.transform, Color.white, coin).rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(62f, 62f), new Vector2(0f, 0.5f));
            balText = Label(bc.transform, new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(200f, 70f), 48, TextAnchor.MiddleRight);
            balText.color = Gold;

            // the panel down the left
            var panel = Img("ClosetPanel", closetRoot, PanelBg, round).rectTransform;
            panel.anchorMin = new Vector2(0f, 0f); panel.anchorMax = new Vector2(0f, 1f); panel.pivot = new Vector2(0f, 0.5f);
            panel.offsetMin = new Vector2(20f, footer); panel.offsetMax = new Vector2(20f + panelW, -28f);
            float PH = H - footer - 28f;
            var ban = Img("Banner", panel, Banner, round);
            Place(ban.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(panelW - 20f, 112f), new Vector2(0.5f, 1f));
            Label(ban.transform, new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(panelW - 60f, 90f), 54, TextAnchor.MiddleLeft).text = "WARDROBE";

            // the tabs: three to a row under the banner
            int perRow = Mathf.Min(3, cats.Count), tabRows = Mathf.CeilToInt(cats.Count / (float)perRow);
            float tabGap = 8f, tabH = 58f, tabW = (panelW - 20f - (perRow - 1) * tabGap) / perRow;
            for (int k = 0; k < cats.Count; k++)
            {
                int row = k / perRow, col = k % perRow;
                int inRow = Mathf.Min(perRow, cats.Count - row * perRow);
                float rowW = inRow * tabW + (inRow - 1) * tabGap;
                var tb = Img("Tab", panel, Grey, round);
                Place(tb.rectTransform, new Vector2(0.5f, 1f), new Vector2(-rowW * 0.5f + tabW * 0.5f + col * (tabW + tabGap), -134f - row * (tabH + tabGap)), new Vector2(tabW, tabH), new Vector2(0.5f, 1f));
                var tt = Label(tb.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(tabW - 10f, tabH - 12f), 26, TextAnchor.MiddleCenter);
                tt.resizeTextForBestFit = true; tt.resizeTextMinSize = 18; tt.resizeTextMaxSize = 28;
                tt.horizontalOverflow = HorizontalWrapMode.Wrap; tt.verticalOverflow = VerticalWrapMode.Truncate;
                tabs.Add(new TabView { rt = tb.rectTransform, bg = tb, text = tt, c = cats[k] });
            }
            float tabsBottom = 134f + tabRows * tabH + (tabRows - 1) * tabGap + 14f;
            // the category and where you are in it
            catText = Label(panel, new Vector2(0f, 1f), new Vector2(28f, -tabsBottom), new Vector2(panelW * 0.7f, 56f), 40, TextAnchor.UpperLeft);
            catText.color = Gold;
            countText = Label(panel, new Vector2(1f, 1f), new Vector2(-28f, -tabsBottom - 6f), new Vector2(200f, 50f), 32, TextAnchor.UpperRight);
            countText.color = Dim;
            float listTop = tabsBottom + 62f, detailH = 130f;

            // the list, in a window it scrolls behind
            listView = New("List", panel);
            listView.anchorMin = new Vector2(0f, 0f); listView.anchorMax = new Vector2(1f, 1f);
            listView.offsetMin = new Vector2(12f, detailH); listView.offsetMax = new Vector2(-12f, -listTop);
            listView.gameObject.AddComponent<RectMask2D>();
            listContent = New("Rows", listView);
            listContent.anchorMin = new Vector2(0f, 1f); listContent.anchorMax = new Vector2(1f, 1f); listContent.pivot = new Vector2(0.5f, 1f);
            listContent.offsetMin = listContent.offsetMax = Vector2.zero;
            listContent.sizeDelta = new Vector2(0f, 10f);

            // what the highlighted item is, and what just happened
            detailText = Label(panel, new Vector2(0f, 0f), new Vector2(28f, 72f), new Vector2(panelW - 56f, 44f), 30, TextAnchor.LowerLeft);
            detailText.color = Dim;
            msgText = Label(panel, new Vector2(0f, 0f), new Vector2(28f, 22f), new Vector2(panelW - 56f, 44f), 30, TextAnchor.LowerLeft);
            foreach (var tx in new[] { detailText, msgText })
            {
                tx.horizontalOverflow = HorizontalWrapMode.Wrap; tx.verticalOverflow = VerticalWrapMode.Truncate;
                tx.resizeTextForBestFit = true; tx.resizeTextMinSize = 20; tx.resizeTextMaxSize = 30;
            }

            // BACK and BUY / WEAR along the bottom
            var bk = Img("Back", closetRoot, new Color(0f, 0f, 0f, 0.6f), round);
            Place(bk.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 36f), new Vector2(W * 0.36f, 124f), new Vector2(0f, 0f));
            Label(bk.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W * 0.34f, 100f), 48, TextAnchor.MiddleCenter).text = "BACK";
            backBtn = bk.rectTransform;
            buyImg = Img("Buy", closetRoot, Green, round);
            Place(buyImg.rectTransform, new Vector2(1f, 0f), new Vector2(-20f, 36f), new Vector2(W - W * 0.36f - 60f, 124f), new Vector2(1f, 0f));
            buyBtn = buyImg.rectTransform;
            buyCoin = Img("Coin", buyBtn, Color.white, coin);
            Place(buyCoin.rectTransform, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(60f, 60f), new Vector2(0f, 0.5f));
            buyText = Label(buyBtn, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W * 0.55f, 100f), 46, TextAnchor.MiddleCenter);

            // the studio's side: drag there to turn
            previewZone = New("Preview", closetRoot);
            previewZone.anchorMin = new Vector2(0f, 0f); previewZone.anchorMax = new Vector2(1f, 1f);
            previewZone.offsetMin = new Vector2(20f + panelW, footer); previewZone.offsetMax = new Vector2(0f, -130f);
            var hint = Label(closetRoot, new Vector2(1f, 1f), new Vector2(-30f, -132f), new Vector2(W - panelW - 60f, 40f), 28, TextAnchor.UpperRight);
            hint.text = "drag to turn"; hint.color = new Color(1f, 1f, 1f, 0.6f);
        }

        void BuildRows()
        {
            foreach (var r in rows) if (r.rt != null) { if (Application.isPlaying) Destroy(r.rt.gameObject); else DestroyImmediate(r.rt.gameObject); }
            rows.Clear();
            var items = Wardrobe.Items(cat);
            int n = 0;
            for (int i = 0; i < items.Length; i++)
            {
                var it = Shown(i);
                var bg = Img("Row", listContent, Row, round);
                var rt = bg.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(0f, -(n * (RowH + RowGap) + RowH)); rt.offsetMax = new Vector2(0f, -n * (RowH + RowGap));
                var v = new RowView { rt = rt, bg = bg, item = i };
                var art = cat == Wardrobe.Cat.Graphic && i > 0 ? BoardArt.Thumb(i - 1) : null;
                if (art != null)
                {
                    // a graphic: the deck itself, seen from above
                    v.thumb = Img("Deck", rt, Color.white, art);
                    v.thumb.preserveAspect = true;
                    Place(v.thumb.rectTransform, new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(136f, 26f), new Vector2(0f, 0.5f));
                }
                else if (cat != Wardrobe.Cat.Ride)
                {
                    // a swatch: the colour, with the pattern's (or the trim's) inside it
                    v.chip = Img("Chip", rt, it.a, disc);
                    Place(v.chip.rectTransform, new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(58f, 58f), new Vector2(0f, 0.5f));
                    if (it.b.a > 0f && (it.Patterned || cat >= Wardrobe.Cat.Helmet))
                    {
                        v.chip2 = Img("Chip2", v.chip.transform, it.b, disc);
                        Place(v.chip2.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f), new Vector2(0.5f, 0.5f));
                    }
                }
                // (a graphic's name sits right of its little board, short of the price's coin)
                float nameX = cat == Wardrobe.Cat.Ride ? 28f : cat == Wardrobe.Cat.Graphic ? 158f : 96f;
                v.name = Label(rt, new Vector2(0f, 0.5f), new Vector2(nameX, 0f), new Vector2(cat == Wardrobe.Cat.Graphic ? 150f : 270f, 70f), 34, TextAnchor.MiddleLeft);
                v.name.text = it.name;
                v.name.horizontalOverflow = HorizontalWrapMode.Wrap; v.name.verticalOverflow = VerticalWrapMode.Truncate;
                v.name.resizeTextForBestFit = true; v.name.resizeTextMinSize = cat == Wardrobe.Cat.Graphic ? 18 : 22; v.name.resizeTextMaxSize = cat == Wardrobe.Cat.Graphic ? 30 : 34;   // (the long names shrink, short of the price)
                v.stat = Label(rt, new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(170f, 60f), 30, TextAnchor.MiddleRight);
                v.coin = Img("Coin", rt, Color.white, coin);
                Place(v.coin.rectTransform, new Vector2(1f, 0.5f), new Vector2(-110f, 0f), new Vector2(40f, 40f), new Vector2(1f, 0.5f));
                // (the labels' shadows are for the dark panel; on the white highlight they'd blur the dark text)
                rows.Add(v);
                n++;
            }
            listContent.sizeDelta = new Vector2(0f, n * (RowH + RowGap));
        }

        // ---------------------------------------------------------------- what's on show

        void SelectCat(Wardrobe.Cat c, bool click)
        {
            cat = c;
            hi = Progress.Worn(c);
            TryOn = Wardrobe.Worn();   // every other category back to what's worn
            if (Stage != null)
            {
                // (a graphic is printed on the board and the skis: on the toboggan, show it on the board)
                bool art = c == Wardrobe.Cat.Graphic;
                Stage.ShowRide(art && (Progress.Ride == RideKind.Toboggan || Rides.Of(Progress.Ride).Vehicle) ? RideKind.Snowboard : Progress.Ride, c == Wardrobe.Cat.Board || c == Wardrobe.Cat.Ride || art);
                Stage.SpinRide(art);
                Stage.Face(c == Wardrobe.Cat.Pack ? 160f : -22f);   // the pack is on their back: turn them round for it
            }
            for (int k = 0; k < tabs.Count; k++) tabs[k].text.text = Wardrobe.Title(tabs[k].c);
            BuildRows();
            scroll = 0f; scrollVel = 0f;
            ScrollTo(RowOf(hi), 0.3f);
            msgText.text = "";
            if (click) Clicked?.Invoke();
            Highlight(hi, false);
        }

        /// <summary>A row's item as it shows on the clothes tried on (on pyjamas the first jacket, trousers and boots are the pyjamas' own).</summary>
        Wardrobe.Item Shown(int i) => Wardrobe.Shown(cat, i, TryOn != null ? TryOn[(int)Wardrobe.Cat.Outfit] : Progress.Worn(Wardrobe.Cat.Outfit));

        int RowOf(int item) { for (int r = 0; r < rows.Count; r++) if (rows[r].item == item) return r; return 0; }

        void Highlight(int item, bool click)
        {
            hi = item;
            if (cat == Wardrobe.Cat.Ride) { if (Stage != null) Stage.ShowRide((RideKind)item); }
            else if (TryOn != null)
            {
                TryOn[(int)cat] = item;
                // a rider shows in their own clothes, and a set of clothes in the colours it was last worn in
                if (cat == Wardrobe.Cat.Rider) { Progress.TryOutfit(TryOn, Wardrobe.OwnOutfit(item)); TryOn[(int)Wardrobe.Cat.Graphic] = Progress.GraphicFor(item); }
                if (cat == Wardrobe.Cat.Outfit) Progress.TryOutfit(TryOn, item);
                if (cat == Wardrobe.Cat.Board) TryOn[(int)Wardrobe.Cat.Graphic] = 0;   // (a colour shows plain: wearing it takes the graphic off)
            }
            TriedOn?.Invoke();
            if (click) Clicked?.Invoke();
            Restyle();
        }

        void ScrollTo(int row, float at)
        {
            float viewH = listView.rect.height, contentH = listContent.sizeDelta.y;
            float target = row * (RowH + RowGap) - (viewH - RowH) * at;
            scroll = Mathf.Clamp(target, 0f, Mathf.Max(0f, contentH - viewH));
        }

        /// <summary>The rows, the tabs, the button and the lines of text, for what's highlighted now.</summary>
        void Restyle()
        {
            int pts = Progress.Points;
            balText.text = pts.ToString("N0");
            catText.text = Wardrobe.Title(cat);
            countText.text = (RowOf(hi) + 1) + " / " + rows.Count;
            foreach (var t in tabs)
            {
                bool on = t.c == cat;
                t.bg.color = on ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.12f);
                t.text.color = on ? Ink : Color.white;
                var sh = t.text.GetComponent<Shadow>(); if (sh != null) sh.enabled = !on;
            }
            var items = Wardrobe.Items(cat);
            int worn = Progress.Worn(cat);
            if (cat == Wardrobe.Cat.Board && Progress.Worn(Wardrobe.Cat.Graphic) != 0) worn = -1;   // (under a graphic no colour is on show)
            foreach (var r in rows)
            {
                var it = Shown(r.item);
                bool sel = r.item == hi, owns = Progress.Owns(cat, r.item), isWorn = r.item == worn;
                bool afford = owns || pts >= it.cost;
                r.bg.color = sel ? new Color(1f, 1f, 1f, 0.94f) : Row;
                r.name.color = sel ? Ink : afford ? Color.white : new Color(1f, 1f, 1f, 0.42f);
                if (r.chip != null) r.chip.color = afford || sel ? it.a : new Color(it.a.r, it.a.g, it.a.b, 0.45f);
                if (r.thumb != null) r.thumb.color = afford || sel ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                if (cat == Wardrobe.Cat.Graphic && r.item == 0 && r.chip != null) r.chip.color = Wardrobe.Get(Wardrobe.Cat.Board, Progress.Worn(Wardrobe.Cat.Board)).a;   // (plain: the colour under BOARDS)
                var sh = r.name.GetComponent<Shadow>(); if (sh != null) sh.enabled = !sel;
                sh = r.stat.GetComponent<Shadow>(); if (sh != null) sh.enabled = !sel;
                bool price = !owns;
                r.coin.gameObject.SetActive(price);
                if (cat == Wardrobe.Cat.Ride) r.stat.text = isWorn ? "RIDING" : "FREE";
                else if (cat == Wardrobe.Cat.Rider) r.stat.text = isWorn ? "YOU" : "FREE";
                else if (cat == Wardrobe.Cat.Outfit) r.stat.text = isWorn ? "WORN" : "FREE";
                else r.stat.text = isWorn ? "WORN" : owns ? "OWNED" : it.cost.ToString("N0");
                r.stat.color = isWorn ? (sel ? WornDark : Worn) : owns ? (sel ? new Color(0.25f, 0.28f, 0.36f) : Dim)
                             : afford ? (sel ? GoldDark : Gold) : (sel ? new Color(0.4f, 0.4f, 0.45f) : new Color(1f, 1f, 1f, 0.42f));
                r.coin.color = afford || sel ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
            // the button
            var cur = Shown(hi);
            bool own = Progress.Owns(cat, hi), on2 = hi == worn;
            buyCoin.gameObject.SetActive(!own);
            if (on2) { buyText.text = cat == Wardrobe.Cat.Ride ? "RIDING" : cat == Wardrobe.Cat.Rider ? "THIS IS YOU" : "WORN"; buyImg.color = new Color(0.02f, 0.03f, 0.07f, 0.55f); buyText.color = new Color(1f, 1f, 1f, 0.55f); }
            else if (own) { buyText.text = cat == Wardrobe.Cat.Ride ? "RIDE THIS" : cat == Wardrobe.Cat.Rider ? "PLAY AS" : "WEAR"; buyImg.color = Blue; buyText.color = Brand.Ink; }
            else
            {
                buyText.text = "BUY  " + cur.cost.ToString("N0");
                bool can = pts >= cur.cost;
                buyImg.color = can ? Green : new Color(0.02f, 0.03f, 0.07f, 0.55f); buyText.color = can ? Brand.Ink : new Color(1f, 1f, 1f, 0.6f);
            }
            detailText.text = hi == 0 && cur != items[0] ? "The pyjamas as they were made" : Detail(cat, cur);
            if (cat == Wardrobe.Cat.Graphic) { detailText.text = cur.tier >= 2 ? "RARE" : cur.tier == 1 ? "PREMIUM" : ""; detailText.color = cur.tier >= 1 ? Gold : Dim; }
            else detailText.color = Dim;
        }

        static string Detail(Wardrobe.Cat c, Wardrobe.Item it)
        {
            if (c == Wardrobe.Cat.Ride) return "Free to swap: on your feet at the gate";
            if (c == Wardrobe.Cat.Rider) return "Who you ride as: free to swap";
            if (c == Wardrobe.Cat.Outfit) return "A whole set of clothes: anyone can wear it";
            string s;
            switch (it.pattern)
            {
                case Wardrobe.Pattern.Stripes: s = "Stripes"; break;
                case Wardrobe.Pattern.Camo: s = "Camo"; break;
                case Wardrobe.Pattern.Block: s = c == Wardrobe.Cat.Pants ? "Two-tone, from the knee" : "Two-tone, over the shoulders"; break;
                case Wardrobe.Pattern.Plaid: s = "Plaid"; break;
                case Wardrobe.Pattern.Nordic: s = "Nordic knit, tone on tone"; break;
                default: s = c == Wardrobe.Cat.Board ? "Paints the board, skis or sled" : c >= Wardrobe.Cat.Helmet && it.b.a > 0f ? "With its own trim" : "Solid colour"; break;
            }
            if (it.metal > 0.5f) s += " · metal finish";
            else if (it.smooth >= 0.8f) s += " · high shine";
            return s;
        }

        // ---------------------------------------------------------------- buying and wearing

        void BuyWear()
        {
            var it = Shown(hi);
            if (cat == Wardrobe.Cat.Ride)
            {
                if (hi == Progress.Worn(cat)) return;
                Progress.PickRide((RideKind)hi); Clicked?.Invoke();
                Say(it.name + " at the gate", true);
            }
            else if (hi == Progress.Worn(cat) && !(cat == Wardrobe.Cat.Board && Progress.Worn(Wardrobe.Cat.Graphic) != 0)) return;
            else if (Progress.Owns(cat, hi)) { Progress.Wear(cat, hi); Clicked?.Invoke(); Say((cat == Wardrobe.Cat.Rider ? "Riding as " : cat == Wardrobe.Cat.Graphic ? "Riding " : "Wearing ") + it.name, true); }
            else if (Progress.Wear(cat, hi)) { Bought?.Invoke(); Say(it.name + ": yours", true); }
            else { Denied?.Invoke(); Say("Need " + (it.cost - Progress.Points).ToString("N0") + " more points: ride to earn them", false); }
            for (int k = 0; k < tabs.Count; k++) tabs[k].text.text = Wardrobe.Title(tabs[k].c);   // (the board's tab follows the ride)
            Restyle();
        }

        void Say(string s, bool good) { msgText.text = s; msgText.color = good ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.5f, 0.42f); }

        // ---------------------------------------------------------------- the finger

        /// <summary>A frame of the closet, given the fingers down now (the camp passes them on while it's open).</summary>
        public void ClosetStep(List<CampInput.Touch> touches, float dt)
        {
            if (!ClosetOpen) return;
            float scale = CanvasScale();
            bool down = touches != null && touches.Count > 0;
            Vector2 p = down ? touches[0].pos : pLast;
            float tapPx = Mathf.Max(10f, Screen.height * 0.012f);
            if (down && !pDown)
            {
                pDown = true; pMoved = false; pStart = pLast = p; pT = 0f;
                pZone = ZoneAt(p, out pTab);
                scrollAtDown = scroll; scrollVel = 0f;
            }
            else if (down)
            {
                var d = p - pLast;
                pT += dt;
                if ((p - pStart).magnitude > tapPx) pMoved = true;
                if (pZone == Zone.List && pMoved)
                {
                    // dragged past either end it gives, at half the pull
                    float want = scrollAtDown + (p.y - pStart.y) / scale, max = MaxScroll();
                    scroll = want < 0f ? want * 0.5f : want > max ? max + (want - max) * 0.5f : want;
                    if (dt > 0f) scrollVel = Mathf.Lerp(scrollVel, d.y / scale / dt, 0.5f);
                }
                if (pZone == Zone.Preview && pMoved && Stage != null) Stage.Turn(-d.x / scale * 0.45f, dt);
                pLast = p;
            }
            else if (pDown)
            {
                pDown = false;
                if (pZone == Zone.Preview && Stage != null) Stage.Release();
                if (!pMoved && pT < 0.6f) Tap(pZone, pTab, pStart);
            }
            if (!pDown || pZone != Zone.List)
            {
                // let go: it carries on and slows, and eases back if it was pulled past an end
                float max = MaxScroll();
                scroll += scrollVel * dt;
                scrollVel *= Mathf.Exp(-dt * 3.2f);
                if (scroll < 0f) { scroll = Mathf.Lerp(scroll, 0f, 1f - Mathf.Exp(-dt * 14f)); if (scrollVel < 0f) scrollVel *= 0.5f; }
                else if (scroll > max) { scroll = Mathf.Lerp(scroll, max, 1f - Mathf.Exp(-dt * 14f)); if (scrollVel > 0f) scrollVel *= 0.5f; }
                if (Mathf.Abs(scrollVel) < 2f) scrollVel = 0f;
            }
            listContent.anchoredPosition = new Vector2(0f, scroll);
            Keys();
        }

        float MaxScroll() => Mathf.Max(0f, listContent.sizeDelta.y - listView.rect.height);

        float CanvasScale() { var cv = GetComponent<Canvas>(); return cv != null && cv.scaleFactor > 0f ? cv.scaleFactor : 1f; }

        Zone ZoneAt(Vector2 p, out int tab)
        {
            tab = -1;
            var cam = UiCam;
            for (int k = 0; k < tabs.Count; k++) if (RectTransformUtility.RectangleContainsScreenPoint(tabs[k].rt, p, cam)) { tab = k; return Zone.Tab; }
            if (RectTransformUtility.RectangleContainsScreenPoint(backBtn, p, cam)) return Zone.Back;
            if (RectTransformUtility.RectangleContainsScreenPoint(buyBtn, p, cam)) return Zone.Buy;
            if (RectTransformUtility.RectangleContainsScreenPoint(listView, p, cam)) return Zone.List;
            if (RectTransformUtility.RectangleContainsScreenPoint(previewZone, p, cam)) return Zone.Preview;
            return Zone.None;
        }

        void Tap(Zone z, int tab, Vector2 p)
        {
            switch (z)
            {
                case Zone.Tab: if (tab >= 0 && tabs[tab].c != cat) SelectCat(tabs[tab].c, true); break;
                case Zone.Back: EndCloset(true); break;
                case Zone.Buy: BuyWear(); break;
                case Zone.List:
                    foreach (var r in rows)
                        if (RectTransformUtility.RectangleContainsScreenPoint(r.rt, p, UiCam)) { if (r.item != hi) Highlight(r.item, true); break; }
                    break;
            }
        }

        /// <summary>The keyboard, in the editor: up and down the list, left and right through the tabs, Enter, Escape.</summary>
        void Keys()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return;
            if (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame) { EndCloset(true); return; }
            if (k.enterKey.wasPressedThisFrame) BuyWear();
            int r = RowOf(hi);
            if (k.downArrowKey.wasPressedThisFrame && r < rows.Count - 1) { Highlight(rows[r + 1].item, true); ScrollTo(r + 1, 0.5f); }
            if (k.upArrowKey.wasPressedThisFrame && r > 0) { Highlight(rows[r - 1].item, true); ScrollTo(r - 1, 0.5f); }
            int ci = cats.IndexOf(cat);
            if (k.rightArrowKey.wasPressedThisFrame) SelectCat(cats[(ci + 1) % cats.Count], true);
            if (k.leftArrowKey.wasPressedThisFrame) SelectCat(cats[(ci + cats.Count - 1) % cats.Count], true);
#endif
        }

        // ---------------------------------------------------------------- test tools

        /// <summary>Test tools: open a category and highlight an item in it (tried on, not bought).</summary>
        public void ClosetTry(Wardrobe.Cat c, int item)
        {
            if (!ClosetOpen) ShowCloset();
            if (c != cat) SelectCat(c, false);
            Highlight(item, false);
            ScrollTo(RowOf(item), 0.4f);
            listContent.anchoredPosition = new Vector2(0f, scroll);
        }
        /// <summary>Test tools: press BUY / WEAR, or BACK.</summary>
        public void ClosetBuy() => BuyWear();
        public void ClosetBack() => EndCloset(false);
    }
}
