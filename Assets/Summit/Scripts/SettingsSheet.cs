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
        public const float Width = 940f, RowHeight = 104f, SliderHeight = 76f, TrackW = 300f;
        const float FirstRow = 232f, MinHeight = 900f, Foot = 40f;

        public static readonly Color Ink = Brand.Snow, Quiet = Brand.SnowMuted, Hair = Brand.Hair;

        class Row
        {
            public int tab; public string label; public System.Func<string> value; public System.Action<int> step;
            public bool toggle; public System.Func<bool> live;
            public RectTransform rect, block; public Text name, val; public Image left, right, track, knob;
            // (steerB) a slider: where it sits (0..1), set from a finger along it, and the drag let go; a button: one word
            public bool slider, button; public System.Func<float> at; public System.Action<float> setAt; public System.Action done;
            public Image fill, rail;
            public float Height => slider || button ? SliderHeight : RowHeight;
        }
        readonly List<Row> rows = new List<Row>();
        readonly List<string> tabNames = new List<string>();
        readonly List<RectTransform> tabBtn = new List<RectTransform>();
        readonly List<Text> tabText = new List<Text>();
        Image tabMark;
        int tab;
        bool built;
        Row dragging;
        /// <summary>The sheet's height changed (a tab of another length shown): what sits on top of it moves with it.</summary>
        public System.Action Resized;

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
            Root = Brand.New("Settings", frame);
            Root.anchorMin = Vector2.zero; Root.anchorMax = new Vector2(1f, 0f); Root.pivot = new Vector2(0.5f, 0f);
            var body = Brand.Img("Body", Root, Brand.PanelStrong);
            Brand.Stretch(body.rectTransform);
            var lip = Brand.Img("Lip", Root, Brand.PanelStrong, FadeUp);
            lip.rectTransform.anchorMin = new Vector2(0f, 1f); lip.rectTransform.anchorMax = Vector2.one; lip.rectTransform.pivot = new Vector2(0.5f, 0f);
            lip.rectTransform.sizeDelta = new Vector2(0f, 150f); lip.rectTransform.anchoredPosition = Vector2.zero;
            Safe = Brand.New("SettingsSafe", Root);
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

        /// <summary>
        /// (steerB) A slider: its name, its value in words, where it sits (0..1), what a finger along it sets, and what
        /// happens when the drag is let go (keep it). Dragged with `Drag` while the finger stays down.
        /// </summary>
        public void AddSlider(int inTab, string label, System.Func<string> value, System.Func<float> at, System.Action<float> setAt, System.Action done)
        {
            rows.Add(new Row { tab = inTab, label = label, value = value, slider = true, at = at, setAt = setAt, done = done });
        }

        /// <summary>(steerB) A row that does one thing when tapped: `word` in the brand's gold on its right.</summary>
        public void AddButton(int inTab, string label, string word, System.Action press)
        {
            rows.Add(new Row { tab = inTab, label = label, value = () => word, button = true, step = d => press() });
        }

        /// <summary>How tall the sheet stands above the home indicator: room for the tab shown (steerB: the TUNE tab's
        /// sliders are many and shorter; the other tabs keep the height they had).</summary>
        public float Height
        {
            get
            {
                float most = 0f, here = 0f;
                var sum = new float[Mathf.Max(1, tabNames.Count)];
                foreach (var r in rows) { sum[r.tab] += r.Height; if (r.tab == tab) here = sum[r.tab]; }
                foreach (var r in rows) if (!r.slider && !r.button) most = Mathf.Max(most, sum[r.tab]);
                float rows_ = Mathf.Max(most, here);
                return Mathf.Max(MinHeight, FirstRow + rows_ - 0.5f * RowHeight + Foot);
            }
        }

        /// <summary>Lay the settings out (once every row has been added).</summary>
        void Build()
        {
            if (built) return;
            built = true;
            if (closable)
            {
                closeBtn = Brand.New("Close", Safe);
                Brand.Place(closeBtn, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(260f, 96f), new Vector2(0.5f, 0.5f));
                var ch = Brand.Img("Chevron", closeBtn, new Color(1f, 1f, 1f, 0.6f), Chevron);
                Brand.Place(ch.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f), new Vector2(0.5f, 0.5f));
                ch.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);   // the chevron points right as drawn: down, to put the sheet away
            }
            float tabW = Width / Mathf.Max(1, tabNames.Count);
            for (int k = 0; k < tabNames.Count; k++)
            {
                var b = Brand.New(tabNames[k] + "Tab", Safe);
                Brand.Place(b, new Vector2(0.5f, 1f), new Vector2(-Width * 0.5f + tabW * (k + 0.5f), -112f), new Vector2(tabW, 110f), new Vector2(0.5f, 0.5f));
                var t = Brand.Line("Text", b, Brand.Type.Button, TextAnchor.MiddleCenter, Quiet, new Vector2(0.5f, 0.5f), Vector2.zero, tabW);
                t.text = tabNames[k];
                tabBtn.Add(b); tabText.Add(t);
            }
            var line = Brand.Img("TabLine", Safe, Hair);
            Brand.Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(Width, 2f), new Vector2(0.5f, 0.5f));
            tabMark = Brand.Img("TabMark", Safe, Brand.Gold);   // the selected tab: the brand's accent
            Brand.Place(tabMark.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(72f, 4f), new Vector2(0.5f, 0.5f));
            var count = new int[tabNames.Count];
            var down = new float[tabNames.Count];
            foreach (var r in rows)
            {
                int i = count[r.tab]++;
                float y = -FirstRow - down[r.tab] - (r.Height - RowHeight) * 0.5f;
                down[r.tab] += r.Height;
                r.rect = Brand.New(r.label, Safe);
                Brand.Place(r.rect, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(Width, r.Height), new Vector2(0.5f, 0.5f));
                r.name = Brand.Line("Name", r.rect, Brand.Type.Label, TextAnchor.MiddleLeft, Quiet, new Vector2(0f, 0.5f), new Vector2(26f, 0f), 460f);
                r.name.text = r.label;
                if (r.slider || r.button)
                {
                    // (steerB) a slider: a rail with the brand's gold up to where it sits, a knob, the value beside it
                    r.block = Brand.New("Value", r.rect);
                    Brand.Place(r.block, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(TrackW + 190f, r.Height), new Vector2(1f, 0.5f));
                    r.val = Brand.Line("Text", r.block, Brand.Type.Body, TextAnchor.MiddleRight, r.button ? Brand.Gold : Ink, new Vector2(1f, 0.5f), new Vector2(-18f, 0f), 170f);
                    r.val.rectTransform.pivot = new Vector2(1f, 0.5f);
                    if (r.slider)
                    {
                        r.rail = Brand.Img("Rail", r.block, Brand.A(Brand.Snow, 0.18f), Brand.Rounded);
                        Brand.Place(r.rail.rectTransform, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(TrackW, 10f), new Vector2(0f, 0.5f));
                        r.fill = Brand.Img("Fill", r.rail.transform, Brand.Gold, Brand.Rounded);
                        Brand.Place(r.fill.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 10f), new Vector2(0f, 0.5f));
                        r.knob = Brand.Img("Knob", r.rail.transform, Ink, Brand.Disc);
                        Brand.Place(r.knob.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(40f, 40f), new Vector2(0.5f, 0.5f));
                    }
                    if (i > 0)
                    {
                        var hl0 = Brand.Img("Hair", r.rect, new Color(1f, 1f, 1f, 0.07f));
                        Brand.Place(hl0.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Width, 2f), new Vector2(0.5f, 0.5f));
                    }
                    continue;
                }
                r.block = Brand.New("Value", r.rect);
                Brand.Place(r.block, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(470f, RowHeight), new Vector2(1f, 0.5f));
                r.val = Brand.Line("Text", r.block, Brand.Type.Body, TextAnchor.MiddleCenter, Ink, new Vector2(0.5f, 0.5f), Vector2.zero, 360f);
                if (r.toggle)
                {
                    // an ON / OFF setting reads as a switch (Refresh shows it in place of the words); one between two
                    // named choices keeps its words and the ‹ › of a stepper, since a tap anywhere steps it
                    r.track = Brand.Img("Switch", r.block, Hair, Brand.Rounded);
                    Brand.Place(r.track.rectTransform, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(92f, 48f), new Vector2(1f, 0.5f));
                    r.knob = Brand.Img("Knob", r.track.transform, Ink, Brand.Disc);
                    Brand.Place(r.knob.rectTransform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(38f, 38f), new Vector2(0.5f, 0.5f));
                }
                {
                    r.left = Brand.Img("Less", r.block, Quiet, Chevron);
                    Brand.Place(r.left.rectTransform, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(30f, 30f), new Vector2(0.5f, 0.5f));
                    r.left.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                    r.right = Brand.Img("More", r.block, Quiet, Chevron);
                    Brand.Place(r.right.rectTransform, new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(30f, 30f), new Vector2(0.5f, 0.5f));
                }
                if (i > 0)
                {
                    var hl = Brand.Img("Hair", r.rect, new Color(1f, 1f, 1f, 0.07f));
                    Brand.Place(hl.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Width, 2f), new Vector2(0.5f, 0.5f));
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
        public string TabName(int k) => tabNames[k];

        public void ShowTab(int k)
        {
            Build();
            float was = Height;
            tab = Mathf.Clamp(k, 0, Mathf.Max(0, tabNames.Count - 1));
            dragging = null;
            if (!Mathf.Approximately(was, Height)) { Layout(); Resized?.Invoke(); }
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
                string v = r.value != null ? r.value() : "";
                if (r.button) { r.val.text = v; r.val.color = Brand.Gold; continue; }
                if (r.slider)
                {
                    r.val.text = v;
                    float t = Mathf.Clamp01(r.at());
                    r.fill.rectTransform.sizeDelta = new Vector2(TrackW * t, 10f);
                    r.knob.rectTransform.anchoredPosition = new Vector2(TrackW * t, 0f);
                    r.knob.color = r == dragging ? Brand.Gold : Ink;
                    continue;
                }
                bool sw = r.toggle && (v == "ON" || v == "OFF");
                r.val.text = v;
                r.val.enabled = !sw;
                r.val.color = on ? Ink : Brand.SnowFaint;
                r.name.color = on ? Quiet : Brand.A(Brand.Snow, 0.28f);
                if (r.left != null) { r.left.enabled = r.right.enabled = !sw; r.left.color = r.right.color = on ? Quiet : Brand.A(Brand.Snow, 0.16f); }
                if (r.track != null)
                {
                    r.track.enabled = r.knob.enabled = sw;
                    bool lit = v == "ON";
                    r.track.color = !on ? Brand.A(Brand.Snow, 0.08f) : lit ? Brand.Gold : Brand.A(Brand.Snow, 0.22f);
                    r.knob.color = lit ? Brand.Ink : Brand.Snow;
                    r.knob.rectTransform.anchoredPosition = new Vector2(lit ? 68f : 24f, 0f);
                }
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
                if (r.slider)
                {
                    // (a finger on the rail, or anywhere right of the name: the slider follows it while it stays down)
                    if (!Inside(r.block, at, ui)) return true;
                    dragging = r;
                    SlideTo(r, at, ui);
                    Clicked?.Invoke();
                    return true;
                }
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

        /// <summary>
        /// (steerB) Each frame the finger is down after a `Tap` on a slider: the slider follows it; let go, its `done` runs
        /// (the value kept). True while a slider is held.
        /// </summary>
        public bool Drag(Vector2 at, bool held)
        {
            if (dragging == null) return false;
            var r = dragging;
            if (!held) { dragging = null; r.done?.Invoke(); Refresh(); return false; }
            SlideTo(r, at, UiCam);
            return true;
        }

        void SlideTo(Row r, Vector2 at, Camera ui)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(r.rail.rectTransform, at, ui, out Vector2 lp);
            float t = Mathf.Clamp01((lp.x - r.rail.rectTransform.rect.xMin) / TrackW);
            r.setAt(t);
            Refresh();
        }
        /// <summary>For the test tools: a screen point along a slider's rail, t of the way.</summary>
        public Vector2 SliderPoint(int i, float t)
        {
            Build();
            var rr = rows[i].rail.rectTransform;
            return RectTransformUtility.WorldToScreenPoint(UiCam, rr.TransformPoint(new Vector3(rr.rect.xMin + TrackW * t, 0f, 0f)));
        }
        public bool RowSlider(int i) => rows[i].slider;

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
        public string RowText(int i) => tabNames[rows[i].tab] + " / " + rows[i].label + " = " + (rows[i].value != null ? rows[i].value() : "") + (rows[i].toggle ? " (toggle)" : "") + (rows[i].live != null && !rows[i].live() ? " (greyed)" : "");
        public void RowStep(int i, int dir) { rows[i].step?.Invoke(dir); Refresh(); }
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
                face = Brand.Face(Brand.Weight.Regular);
                if (face == null) face = Brand.Builtin();
                return face;
            }
        }

        public static Text Label(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Color c)
        {
            // (Brand's type, spaced; a soft dark edge so it reads where there's no plate behind it)
            var t = Brand.Line(name, parent, new Brand.Style(Brand.Weight.Regular, fontSize, 0.2f), align, c, true);
            float px = align == TextAnchor.MiddleLeft ? 0f : align == TextAnchor.MiddleRight ? 1f : 0.5f;
            Brand.Place(t.rectTransform, anchor, pos, size, new Vector2(px, 0.5f));
            return t;
        }

        static Sprite fadeUp, fadeDown, chevron;
        /// <summary>Clear at the top, solid at the bottom.</summary>
        public static Sprite FadeUp => fadeUp != null ? fadeUp : (fadeUp = Gradient(false));
        /// <summary>Solid at the top, clear at the bottom.</summary>
        public static Sprite FadeDown => fadeDown != null ? fadeDown : (fadeDown = Gradient(true));
        /// <summary>A thin chevron pointing right.</summary>
        public static Sprite Chevron => Brand.Chevron;

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
