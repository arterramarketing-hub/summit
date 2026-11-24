using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// The game's settings: tabs along the top, and under them one row per setting, its name on the left and its value on
    /// the right, with ‹ › either side of a value that steps (a toggle is tapped anywhere along it). Drawn as a dark sheet
    /// over the bottom of the screen, so the picture stays in sight above it.
    ///
    /// The start page and the pause menu each have one, filled with the same rows by SummitGame (FillSettings): the very
    /// setters, so a change made in one shows in the other. Hit-tested by hand like the rest of the game's menus (no
    /// EventSystem): `Tap` takes a finger's screen point.
    /// </summary>
    public class SettingsSheet
    {
        public const float Width = 940f, RowHeight = 104f;
        const float FirstRow = 232f, MinHeight = 900f, Foot = 40f;

        public static readonly Color Ink = new Color(1f, 1f, 1f, 0.96f), Quiet = new Color(1f, 1f, 1f, 0.55f), Hair = new Color(1f, 1f, 1f, 0.14f);

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
        bool built;

        readonly RectTransform frame;   // the screen, edge to edge, in the canvas's units
        readonly bool closable;
        /// <summary>The sheet (its dark body and the fade above it), anchored along the bottom of the frame.</summary>
        public readonly RectTransform Root;
        /// <summary>Its contents, clear of the home indicator, laid out `Width` wide (narrower screens shrink them).</summary>
        public readonly RectTransform Safe;
        RectTransform closeBtn;
        RectTransform pressed; float pressT;

        /// <summary>A setting was touched (the click).</summary>
        public System.Action Clicked;

        /// <summary>
        /// A sheet in `frame` (a rectangle filling the screen). `closable`: a chevron at its top puts it away (the start
        /// page's; the pause menu's stays up while it is paused).
        /// </summary>
        public SettingsSheet(RectTransform frame, bool closable)
        {
            this.frame = frame; this.closable = closable;
            Root = New("Settings", frame);
            Root.anchorMin = Vector2.zero; Root.anchorMax = new Vector2(1f, 0f); Root.pivot = new Vector2(0.5f, 0f);
            var body = Img("Body", Root, new Color(0.025f, 0.04f, 0.075f, 0.9f));
            Stretch(body.rectTransform);
            var lip = Img("Lip", Root, new Color(0.025f, 0.04f, 0.075f, 0.9f), FadeUp);
            lip.rectTransform.anchorMin = new Vector2(0f, 1f); lip.rectTransform.anchorMax = Vector2.one; lip.rectTransform.pivot = new Vector2(0.5f, 0f);
            lip.rectTransform.sizeDelta = new Vector2(0f, 150f); lip.rectTransform.anchoredPosition = Vector2.zero;
            Safe = New("SettingsSafe", Root);
        }

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

        /// <summary>How tall the sheet stands above the home indicator: room for its longest tab.</summary>
        public float Height
        {
            get
            {
                var count = new int[Mathf.Max(1, tabNames.Count)];
                int most = 0;
                foreach (var r in rows) most = Mathf.Max(most, ++count[r.tab]);
                return Mathf.Max(MinHeight, FirstRow + (most - 0.5f) * RowHeight + Foot);
            }
        }

        /// <summary>Lay the settings out (once every row has been added).</summary>
        void Build()
        {
            if (built) return;
            built = true;
            if (closable)
            {
                closeBtn = New("Close", Safe);
                Place(closeBtn, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(260f, 96f), new Vector2(0.5f, 0.5f));
                var ch = Img("Chevron", closeBtn, new Color(1f, 1f, 1f, 0.6f), Chevron);
                Place(ch.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f), new Vector2(0.5f, 0.5f));
                ch.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);   // the chevron points right as drawn: down, to put the sheet away
            }
            float tabW = Width / Mathf.Max(1, tabNames.Count);
            for (int k = 0; k < tabNames.Count; k++)
            {
                var b = New(tabNames[k] + "Tab", Safe);
                Place(b, new Vector2(0.5f, 1f), new Vector2(-Width * 0.5f + tabW * (k + 0.5f), -112f), new Vector2(tabW, 110f), new Vector2(0.5f, 0.5f));
                var t = Label("Text", b, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(tabW, 70f), 30, TextAnchor.MiddleCenter, Quiet);
                t.text = Track(tabNames[k], 24);
                tabBtn.Add(b); tabText.Add(t);
            }
            var line = Img("TabLine", Safe, Hair);
            Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(Width, 2f), new Vector2(0.5f, 0.5f));
            tabMark = Img("TabMark", Safe, Ink);
            Place(tabMark.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(72f, 3f), new Vector2(0.5f, 0.5f));
            var count = new int[tabNames.Count];
            foreach (var r in rows)
            {
                int i = count[r.tab]++;
                r.rect = New(r.label, Safe);
                Place(r.rect, new Vector2(0.5f, 1f), new Vector2(0f, -FirstRow - i * RowHeight), new Vector2(Width, RowHeight), new Vector2(0.5f, 0.5f));
                r.name = Label("Name", r.rect, new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(460f, 60f), 27, TextAnchor.MiddleLeft, Quiet);
                r.name.text = Track(r.label, 20);
                r.block = New("Value", r.rect);
                Place(r.block, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(470f, RowHeight), new Vector2(1f, 0.5f));
                r.val = Label("Text", r.block, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 60f), 31, TextAnchor.MiddleCenter, Ink);
                if (!r.toggle)
                {
                    r.left = Img("Less", r.block, Quiet, Chevron);
                    Place(r.left.rectTransform, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(30f, 30f), new Vector2(0.5f, 0.5f));
                    r.left.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                    r.right = Img("More", r.block, Quiet, Chevron);
                    Place(r.right.rectTransform, new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(30f, 30f), new Vector2(0.5f, 0.5f));
                }
                if (i > 0)
                {
                    var hl = Img("Hair", r.rect, new Color(1f, 1f, 1f, 0.07f));
                    Place(hl.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Width, 2f), new Vector2(0.5f, 0.5f));
                }
            }
            Layout();
            ShowTab(0);
        }

        /// <summary>Fit the sheet to the screen: above the home indicator, and its contents shrunk on a narrow screen.</summary>
        public void Layout()
        {
            Build();
            Rect a = Screen.safeArea;
            float bottom = Screen.height > 0 ? a.yMin / Screen.height * frame.rect.height : 0f;
            Root.sizeDelta = new Vector2(0f, Height + bottom);
            Safe.anchorMin = Vector2.zero; Safe.anchorMax = Vector2.one;
            Safe.offsetMin = new Vector2(0f, bottom); Safe.offsetMax = Vector2.zero;
            // laid out `Width` wide: on a narrower canvas the whole sheet's contents shrink to fit
            float room = frame.rect.width - 36f;
            Safe.localScale = Vector3.one * (room > 1f && room < Width ? room / Width : 1f);
        }

        /// <summary>The top of the sheet, up from the bottom of the frame (in the frame's units).</summary>
        public float Top => Root.sizeDelta.y;

        public int Tab => tab;
        /// <summary>Laid out yet (the rows are laid out the first time the sheet is shown).</summary>
        public bool IsBuilt => built;
        public int TabCount => tabNames.Count;
        public string TabName(int k) => tabNames[k];

        public void ShowTab(int k)
        {
            Build();
            tab = Mathf.Clamp(k, 0, Mathf.Max(0, tabNames.Count - 1));
            foreach (var r in rows) r.rect.gameObject.SetActive(r.tab == tab);
            for (int i = 0; i < tabText.Count; i++) tabText[i].color = i == tab ? Ink : Quiet;
            if (tabBtn.Count > 0) tabMark.rectTransform.anchoredPosition = new Vector2(tabBtn[tab].anchoredPosition.x, -166f);
        }

        /// <summary>Read every setting again (after a step: one can grey another out).</summary>
        public void Refresh()
        {
            Build();
            foreach (var r in rows)
            {
                bool on = r.live == null || r.live();
                r.val.text = Track(r.value != null ? r.value() : "", 12);
                r.val.color = on ? Ink : new Color(1f, 1f, 1f, 0.3f);
                r.name.color = on ? Quiet : new Color(1f, 1f, 1f, 0.28f);
                if (r.left != null) r.left.color = r.right.color = on ? Quiet : new Color(1f, 1f, 1f, 0.16f);
            }
        }

        Camera UiCam
        {
            get
            {
                var c = frame.GetComponentInParent<Canvas>();
                if (c != null) c = c.rootCanvas;
                return c != null && c.renderMode == RenderMode.ScreenSpaceCamera ? c.worldCamera : null;
            }
        }
        bool Inside(RectTransform r, Vector2 p, Camera ui) => r != null && r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, p, ui);

        /// <summary>Is a screen point on the sheet?</summary>
        public bool Contains(Vector2 at) => Inside(Root, at, UiCam);
        /// <summary>Is a screen point on the chevron that puts the sheet away?</summary>
        public bool OnClose(Vector2 at) => Inside(closeBtn, at, UiCam);

        /// <summary>A finger came down at `at`: a tab, or a row (its left half steps down, its right half up). True if it hit one.</summary>
        public bool Tap(Vector2 at)
        {
            Build();
            var ui = UiCam;
            for (int k = 0; k < tabBtn.Count; k++)
                if (Inside(tabBtn[k], at, ui)) { ShowTab(k); Clicked?.Invoke(); return true; }
            foreach (var r in rows)
            {
                if (r.tab != tab || !Inside(r.rect, at, ui)) continue;
                if (r.live != null && !r.live()) return true;
                int dir = 1;
                if (!r.toggle && Inside(r.block, at, ui))
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(r.block, at, ui, out Vector2 lp);
                    dir = lp.x < r.block.rect.center.x ? -1 : 1;
                }
                r.step?.Invoke(dir);
                Press(r.rect);
                Refresh();
                return true;
            }
            return false;
        }

        /// <summary>A touch answers: the thing touched dips and comes back (`Step` each frame plays it).</summary>
        public void Press(RectTransform r) { pressed = r; pressT = 0.22f; Clicked?.Invoke(); }

        /// <summary>One frame (real time): the touched control's dip.</summary>
        public void Step(float dt)
        {
            if (pressT <= 0f || pressed == null) return;
            pressT -= dt;
            float k = Mathf.Clamp01(pressT / 0.22f);
            var g = pressed.GetComponent<CanvasGroup>();
            if (g == null) g = pressed.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 1f - 0.55f * k;
        }

        // ---------------------------------------------------------------- for the test tools
        public int RowCount => rows.Count;
        public string RowText(int i) => tabNames[rows[i].tab] + " / " + rows[i].label + " = " + (rows[i].value != null ? rows[i].value() : "") + (rows[i].toggle ? " (toggle)" : "") + (rows[i].live != null && !rows[i].live() ? " (greyed)" : "");
        public void RowStep(int i, int dir) { rows[i].step?.Invoke(dir); Refresh(); }
        public bool RowToggle(int i) => rows[i].toggle;
        /// <summary>Where on the screen a finger would tap row `i`: its value's left half (`right` false) or right half.</summary>
        public Vector2 RowPoint(int i, bool right)
        {
            Build();
            var b = rows[i].block;
            return RectTransformUtility.WorldToScreenPoint(UiCam, b.TransformPoint(b.rect.center + new Vector2((right ? 0.3f : -0.3f) * b.rect.width, 0f)));
        }
        /// <summary>Where on the screen a finger would tap tab `k`.</summary>
        public Vector2 TabPoint(int k) { Build(); return RectTransformUtility.WorldToScreenPoint(UiCam, tabBtn[k].TransformPoint(tabBtn[k].rect.center)); }
        /// <summary>The row with this tab and label (-1: none).</summary>
        public int RowIndex(string tabName, string label) { for (int i = 0; i < rows.Count; i++) if (tabNames[rows[i].tab] == tabName && rows[i].label == label) return i; return -1; }
        /// <summary>Every row's label, in order, for the test tools to compare two sheets.</summary>
        public string Labels() { var sb = new System.Text.StringBuilder(); foreach (var r in rows) sb.Append(tabNames[r.tab]).Append('/').Append(r.label).Append(' '); return sb.ToString(); }

        // ---------------------------------------------------------------- the type and the few pictures, shared by the start page and the pause menu

        static Font face;
        /// <summary>A lighter, wider face than the HUD's where the system has one (the phone and the Mac both do).</summary>
        public static Font Face
        {
            get
            {
                if (face != null) return face;
                try { face = Font.CreateDynamicFontFromOSFont(new[] { "Avenir Next", "Avenir", "Helvetica Neue", "Arial" }, 64); } catch { }
                if (face == null) try { face = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                if (face == null) try { face = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
                return face;
            }
        }

        /// <summary>Letters set apart (uGUI's Text has no tracking: a small space goes between each pair).</summary>
        public static string Track(string s, int gap)
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

        public static RectTransform New(string name, Transform parent)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            return r;
        }

        public static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        }

        public static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        public static Image Img(string name, Transform parent, Color c, Sprite s = null)
        {
            var i = New(name, parent).gameObject.AddComponent<Image>();
            i.color = c; i.raycastTarget = false; i.sprite = s;
            return i;
        }

        public static Text Label(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Color c)
        {
            var t = New(name, parent).gameObject.AddComponent<Text>();
            t.font = Face; t.fontSize = fontSize; t.fontStyle = FontStyle.Normal; t.alignment = align; t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            float px = align == TextAnchor.MiddleLeft ? 0f : align == TextAnchor.MiddleRight ? 1f : 0.5f;
            Place(t.rectTransform, anchor, pos, size, new Vector2(px, 0.5f));
            // a soft dark edge so it reads on snow and on night alike, without a plate behind it
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0.02f, 0.04f, 0.09f, 0.45f); sh.effectDistance = new Vector2(0f, -2f);
            return t;
        }

        static Sprite fadeUp, fadeDown, chevron;
        /// <summary>Clear at the top, solid at the bottom.</summary>
        public static Sprite FadeUp => fadeUp != null ? fadeUp : (fadeUp = Gradient(false));
        /// <summary>Solid at the top, clear at the bottom.</summary>
        public static Sprite FadeDown => fadeDown != null ? fadeDown : (fadeDown = Gradient(true));
        /// <summary>A thin chevron pointing right.</summary>
        public static Sprite Chevron => chevron != null ? chevron : (chevron = Draw(64, p => Mathf.Min(Seg(p, new Vector2(0.36f, 0.82f), new Vector2(0.68f, 0.5f)), Seg(p, new Vector2(0.68f, 0.5f), new Vector2(0.36f, 0.18f))), 0.045f));

        static Sprite Gradient(bool down)
        {
            const int N = 64;
            var t = new Texture2D(1, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < N; y++) { float k = down ? y / (float)(N - 1) : 1f - y / (float)(N - 1); t.SetPixel(0, y, new Color(1f, 1f, 1f, k * k * (3f - 2f * k))); }
            t.Apply();
            // (the whole rectangle: a tight sprite would trim its clear end, and the fade would stop at a line)
            return Sprite.Create(t, new Rect(0, 0, 1, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        public static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float h = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - a - ab * h).magnitude;
        }

        /// <summary>A white shape `px` square from a distance function (0..1 across), `half` its half-thickness.</summary>
        public static Sprite Draw(int px, System.Func<Vector2, float> dist, float half)
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
    }
}
