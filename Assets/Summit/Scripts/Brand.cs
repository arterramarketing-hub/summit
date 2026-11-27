using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// The game's look, in one place: the typeface (the menus' own, Avenir Next from the system), the colours,
    /// the type sizes, and the few parts every screen is built from (labels, buttons, chips, icon buttons, plates). The
    /// start page, the settings, the pause menu, the HUD and the camp's menus all draw from it, so they read as one game.
    ///
    /// The look: clean and quiet, like the title card of a mountain film. Widely spaced capitals; near-black plates
    /// where type sits over bright snow; one warm gold accent, used sparingly (the primary action, the selected tab,
    /// points). Never an underline. (The name itself is the carved ice lettering: TitleArt.)
    /// </summary>
    public static class Brand
    {
        // ---------------------------------------------------------------- colour
        /// <summary>The one accent.</summary>
        public static readonly Color Gold = Hex(0xC9A24A), GoldDeep = Hex(0x9E7C31);
        /// <summary>Near-black: type on gold, and the base of every plate.</summary>
        public static readonly Color Ink = Hex(0x0E1013);
        public static readonly Color Snow = Hex(0xF7F6F2), SnowMuted = A(Hex(0xF7F6F2), 0.64f), SnowFaint = A(Hex(0xF7F6F2), 0.32f), Hair = A(Hex(0xF7F6F2), 0.14f);
        /// <summary>Plates: a chip over the world (reads on snow and on night), and a sheet or card.</summary>
        public static readonly Color Panel = A(Hex(0x0E1013), 0.62f), PanelStrong = A(Hex(0x0E1013), 0.86f);
        /// <summary>The only other colour: danger (the slide close behind, the last fall, the bill, dropping in).</summary>
        public static readonly Color Alert = Hex(0xD8574A);

        public static Color A(Color c, float a) { c.a = a; return c; }
        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        // ---------------------------------------------------------------- type

        public enum Weight { ExtraLight, Light, Regular, SemiBold }
        static Font face;

        /// <summary>
        /// The menus' face: Avenir Next from the system, as the start page and the settings always had it (nothing is
        /// shipped). Every weight is that one face; SemiBold is set bold (Line).
        /// </summary>
        public static Font Face(Weight w)
        {
            if (face != null) return face;
            face = Os("Avenir Next", "Avenir", "Helvetica Neue", "Arial");
            if (face == null) face = Builtin();
            return face;
        }

        /// <summary>The first of these the system has, at 64 (null if none, or no system fonts at all).</summary>
        public static Font Os(params string[] names)
        {
            try { return Font.CreateDynamicFontFromOSFont(names, 64); } catch { return null; }
        }

        static Font builtin;
        /// <summary>
        /// Unity's own face (LegacyRuntime, or Arial in older editors), shipped with every build: the HUD's and the
        /// start page's bold type, and what the system faces fall back to.
        /// </summary>
        public static Font Builtin()
        {
            if (builtin != null) return builtin;
            try { builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (builtin == null) try { builtin = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            return builtin;
        }

        /// <summary>A step of the type scale: face, size (canvas units: ×0.4 for points on a phone) and letter spacing (em).</summary>
        public struct Style
        {
            public Weight weight; public int size; public float track;
            public Style(Weight w, int s, float t) { weight = w; size = s; track = t; }
        }
        /// <summary>The type scale.</summary>
        public static class Type
        {
            public static readonly Style Title = new Style(Weight.Light, 60, 0.30f);           // PAUSED, DAILY MISSIONS
            public static readonly Style Button = new Style(Weight.SemiBold, 32, 0.22f);       // buttons and tabs
            public static readonly Style Body = new Style(Weight.Regular, 30, 0.10f);          // settings values, mission titles
            public static readonly Style Label = new Style(Weight.SemiBold, 28, 0.22f);        // chips, row names, credit lines
            public static readonly Style Hint = new Style(Weight.SemiBold, 28, 0.06f);         // the controls hint (sentence case)
        }

        // ---------------------------------------------------------------- spacing and timing
        /// <summary>Screen margin, and the height of every control (45 pt on a phone: over Apple's 44).</summary>
        public const float Margin = 36f, Control = 112f, Radius = 14f, CardRadius = 24f;
        public const float Fast = 0.12f, Base = 0.24f, Slow = 0.48f;
        /// <summary>Ease-out cubic: the one curve things arrive on.</summary>
        public static float Out(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }

        // ---------------------------------------------------------------- parts

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
            if (s != null && s.border.sqrMagnitude > 0f) i.type = Image.Type.Sliced;
            return i;
        }

        /// <summary>
        /// Type in a style. `soft`: a faint dark edge under it, for type set straight over the picture with no plate
        /// behind (the wordmark, the pause title); type on a plate has none.
        /// </summary>
        public static Text Line(string name, Transform parent, Style st, TextAnchor align, Color c, bool soft = false)
        {
            var t = New(name, parent).gameObject.AddComponent<Text>();
            t.font = Face(st.weight); t.fontSize = st.size; t.fontStyle = st.weight == Weight.SemiBold ? FontStyle.Bold : FontStyle.Normal; t.alignment = align; t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = false;
            t.lineSpacing = 1.1f;
            // (the spacing first: a shadow added before it would be spaced as more letters)
            if (st.track != 0f) t.gameObject.AddComponent<Tracking>().em = st.track;
            if (soft)
            {
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = A(Ink, 0.35f); sh.effectDistance = new Vector2(0f, -2f);
            }
            t.rectTransform.sizeDelta = new Vector2(1000f, st.size * 1.4f);
            return t;
        }

        /// <summary>Type placed: `anchor` and `pos` as Place, pivot from the alignment.</summary>
        public static Text Line(string name, Transform parent, Style st, TextAnchor align, Color c, Vector2 anchor, Vector2 pos, float width, bool soft = false)
        {
            var t = Line(name, parent, st, align, c, soft);
            Place(t.rectTransform, anchor, pos, new Vector2(width, st.size * 1.4f), Pivot(align, anchor.y));
            return t;
        }

        public static Vector2 Pivot(TextAnchor align, float y)
        {
            int col = (int)align % 3;
            return new Vector2(col * 0.5f, y);
        }

        /// <summary>How wide a line of type stands, its spacing included (canvas units).</summary>
        public static float Width(Text t)
        {
            var tr = t.GetComponent<Tracking>();
            int n = 0; foreach (char ch in t.text) if (ch != ' ' && ch != '\n') n++;
            return t.preferredWidth + (tr != null ? tr.em * t.fontSize * Mathf.Max(0, n - 1) : 0f);
        }

        public enum Kind { Primary, Secondary, Quiet }

        /// <summary>
        /// A button: Primary (gold, near-black type: one per screen), Secondary (a dark plate with a fine light edge) or
        /// Quiet (type alone). Its rectangle is what a finger hits.
        /// </summary>
        public static RectTransform Button(string name, Transform parent, string label, Kind kind, Vector2 anchor, Vector2 pos, Vector2 size, out Text text)
        {
            var r = New(name, parent);
            Place(r, anchor, pos, size, new Vector2(0.5f, 0.5f));
            if (kind == Kind.Primary) Stretch(Img("Fill", r, Gold, Rounded).rectTransform);
            else if (kind == Kind.Secondary)
            {
                Stretch(Img("Fill", r, Panel, Rounded).rectTransform);
                Stretch(Img("Edge", r, A(Snow, 0.38f), Ring).rectTransform);
            }
            text = Line("Text", r, Type.Button, TextAnchor.MiddleCenter, kind == Kind.Primary ? Ink : Snow);
            Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(size.x, Type.Button.size * 1.4f), new Vector2(0.5f, 0.5f));
            text.text = label;
            return r;
        }

        public static RectTransform Button(string name, Transform parent, string label, Kind kind, Vector2 anchor, Vector2 pos, Vector2 size) => Button(name, parent, label, kind, anchor, pos, size, out _);

        /// <summary>A square button holding a line icon (sliders, a peak, a pause).</summary>
        public static RectTransform IconButton(string name, Transform parent, Sprite icon, Vector2 anchor, Vector2 pos, Vector2 pivot, bool plate = true)
        {
            var r = New(name, parent);
            Place(r, anchor, pos, new Vector2(Control, Control), pivot);
            if (plate) Stretch(Img("Fill", r, Panel, Rounded).rectTransform);
            Place(Img("Icon", r, Snow, icon).rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f), new Vector2(0.5f, 0.5f));
            return r;
        }

        /// <summary>A dark rounded plate for a chip, a hint or a card.</summary>
        public static Image Plate(string name, Transform parent, Color c, bool card = false) => Img(name, parent, c, card ? RoundedCard : Rounded);

        // ---------------------------------------------------------------- the few pictures (drawn here, nothing shipped)

        static Sprite rounded, roundedCard, ring, disc, discRing, peak, sliders, pause, close, chevron, fadeUp, fadeDown;
        /// <summary>A rounded rectangle, 9-sliced to any size (Radius).</summary>
        public static Sprite Rounded => rounded != null ? rounded : (rounded = RoundRect(64, (int)Radius, 0f));
        public static Sprite RoundedCard => roundedCard != null ? roundedCard : (roundedCard = RoundRect(96, (int)CardRadius, 0f));
        /// <summary>The fine edge of a secondary button.</summary>
        public static Sprite Ring => ring != null ? ring : (ring = RoundRect(64, (int)Radius, 2f));
        public static Sprite Disc => disc != null ? disc : (disc = Draw(96, p => (p - new Vector2(0.5f, 0.5f)).magnitude - 0.47f, 0f));
        public static Sprite DiscRing => discRing != null ? discRing : (discRing = Draw(128, p => Mathf.Abs((p - new Vector2(0.5f, 0.5f)).magnitude - 0.46f), 0.025f));
        /// <summary>The mark: a peak in two strokes, and the shoulder beside it.</summary>
        public static Sprite Peak => peak != null ? peak : (peak = Draw(128, p => Mathf.Min(Mathf.Min(Seg(p, new Vector2(0.08f, 0.24f), new Vector2(0.42f, 0.76f)), Seg(p, new Vector2(0.42f, 0.76f), new Vector2(0.60f, 0.48f))),
                                                                                       Mathf.Min(Seg(p, new Vector2(0.60f, 0.48f), new Vector2(0.70f, 0.60f)), Seg(p, new Vector2(0.70f, 0.60f), new Vector2(0.92f, 0.24f)))), 0.034f));
        /// <summary>Three sliders: the settings.</summary>
        public static Sprite Sliders => sliders != null ? sliders : (sliders = Draw(96, p =>
        {
            float d = 9f;
            float[] ys = { 0.76f, 0.5f, 0.24f }, knob = { 0.66f, 0.32f, 0.56f };
            for (int i = 0; i < 3; i++)
            {
                d = Mathf.Min(d, Seg(p, new Vector2(0.12f, ys[i]), new Vector2(0.88f, ys[i])) + 0.012f);
                d = Mathf.Min(d, (p - new Vector2(knob[i], ys[i])).magnitude - 0.05f);
            }
            return d;
        }, 0.034f));
        public static Sprite Pause => pause != null ? pause : (pause = Draw(96, p => Mathf.Min(Seg(p, new Vector2(0.38f, 0.26f), new Vector2(0.38f, 0.74f)), Seg(p, new Vector2(0.62f, 0.26f), new Vector2(0.62f, 0.74f))), 0.04f));
        public static Sprite Close => close != null ? close : (close = Draw(96, p => Mathf.Min(Seg(p, new Vector2(0.28f, 0.28f), new Vector2(0.72f, 0.72f)), Seg(p, new Vector2(0.28f, 0.72f), new Vector2(0.72f, 0.28f))), 0.03f));
        /// <summary>A thin chevron pointing right.</summary>
        public static Sprite Chevron => chevron != null ? chevron : (chevron = Draw(64, p => Mathf.Min(Seg(p, new Vector2(0.36f, 0.82f), new Vector2(0.68f, 0.5f)), Seg(p, new Vector2(0.68f, 0.5f), new Vector2(0.36f, 0.18f))), 0.04f));
        /// <summary>Clear at the top, solid at the bottom.</summary>
        public static Sprite FadeUp => fadeUp != null ? fadeUp : (fadeUp = Gradient(false));
        /// <summary>Solid at the top, clear at the bottom.</summary>
        public static Sprite FadeDown => fadeDown != null ? fadeDown : (fadeDown = Gradient(true));

        static Sprite RoundRect(int px, int radius, float edge)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (px - radius))), dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (px - radius)));
                    float inside = radius - Mathf.Sqrt(dx * dx + dy * dy);   // distance in from the edge
                    // (on the straight sides the distance is to the side itself)
                    float side = Mathf.Min(Mathf.Min(x + 0.5f, px - x - 0.5f), Mathf.Min(y + 0.5f, px - y - 0.5f));
                    if (dx <= 0f || dy <= 0f) inside = side;
                    float a = Mathf.Clamp01(inside + 0.5f);
                    if (edge > 0f) a *= Mathf.Clamp01(edge - inside + 0.5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite Gradient(bool down)
        {
            const int N = 64;
            var t = new Texture2D(1, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < N; y++) { float k = down ? y / (float)(N - 1) : 1f - y / (float)(N - 1); t.SetPixel(0, y, new Color(1f, 1f, 1f, k * k * (3f - 2f * k))); }
            t.Apply();
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

        // ---------------------------------------------------------------- the tap-to-carry-on cue

        /// <summary>After a fall, waiting for the tap: a dark dot with a gold ring spreading from it, fading in. No words.</summary>
        public class TapCue
        {
            readonly Image dot, ripple;
            float at = -1f, on;
            public TapCue(Transform parent, Vector2 anchor, Vector2 pos)
            {
                ripple = Img("TapRipple", parent, A(Gold, 0f), DiscRing);
                dot = Img("TapDot", parent, A(Ink, 0f), Disc);
                Place(ripple.rectTransform, anchor, pos, new Vector2(40f, 40f), new Vector2(0.5f, 0.5f));
                Place(dot.rectTransform, anchor, pos, new Vector2(40f, 40f), new Vector2(0.5f, 0.5f));
                var core = Img("Core", dot.transform, A(Gold, 1f), Disc);
                Place(core.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f), new Vector2(0.5f, 0.5f));
                dot.enabled = ripple.enabled = false; core.enabled = false;
            }
            public void Set(bool show)
            {
                if (show && at < 0f) at = Time.unscaledTime;
                if (!show) at = -1f;
                on = show ? Mathf.MoveTowards(on, 1f, Time.unscaledDeltaTime / 0.6f) : 0f;
                var core = dot.transform.GetChild(0).GetComponent<Image>();
                if (dot.enabled != show) dot.enabled = ripple.enabled = core.enabled = show;
                if (!show) return;
                float ph = ((Time.unscaledTime - at) / 1.4f) % 1f;
                dot.color = A(Ink, 0.5f * on);
                core.color = A(Gold, on);
                ripple.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(40f, 150f, Out(ph));
                ripple.color = A(Gold, 0.85f * (1f - ph) * on);
            }
            /// <summary>Show its middle frame now (the test tools).</summary>
            public void Freeze(float phase)
            {
                at = Time.unscaledTime - phase * 1.4f; on = 1f;
                dot.enabled = ripple.enabled = true; dot.transform.GetChild(0).GetComponent<Image>().enabled = true;
                dot.color = A(Ink, 0.5f); dot.transform.GetChild(0).GetComponent<Image>().color = Gold;
                ripple.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(40f, 150f, Out(phase));
                ripple.color = A(Gold, 0.85f * (1f - phase));
            }
        }
    }
}
