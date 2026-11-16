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
        RectTransform playBtn, restartBtn, modeBtn;
        Text modeLabel;
        Text playLabel;
        Text speed, score, chain, slide, big, small, hint;
        Image slideFill, whiteout;
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

            safe = new GameObject("Safe", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(transform, false);
            ApplySafeArea();

            speed = NewText("Speed", safe, new Vector2(0f, 1f), new Vector2(40f, -40f), 54, TextAnchor.UpperLeft);
            score = NewText("Score", safe, new Vector2(0.5f, 1f), new Vector2(0f, -34f), 84, TextAnchor.UpperCenter);
            chain = NewText("Chain", safe, new Vector2(0.5f, 1f), new Vector2(0f, -132f), 44, TextAnchor.UpperCenter);
            chain.color = new Color(1f, 0.82f, 0.35f);
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

            // the drag stick: a ring where the finger lands and a knob that follows it
            var circle = MakeCircle(96);
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
            rl.text = "RESTART";
            var mb = NewImage("ModeButton", safe, new Color(0f, 0f, 0f, 0.4f));
            Place(mb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -280f), new Vector2(620f, 100f), new Vector2(0.5f, 0.5f));
            modeBtn = mb.rectTransform;
            modeLabel = NewText("ModeLabel", mb.transform, new Vector2(0.5f, 0.5f), Vector2.zero, 40, TextAnchor.MiddleCenter);
            SetButtons(false, false, "PLAY");
        }

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

        public void SetMode(bool tilt) { modeLabel.text = tilt ? "STEERING: TILT  (tap to change)" : "STEERING: DRAG STICK  (tap to change)"; }
        public bool OnMode(Vector2 p) => Inside(modeBtn, p);

        public void SetButtons(bool play, bool restart, string playText)
        {
            playBtn.gameObject.SetActive(play);
            modeBtn.gameObject.SetActive(play);
            restartBtn.gameObject.SetActive(restart);
            playLabel.text = playText;
        }

        static bool Inside(RectTransform r, Vector2 p) =>
            r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, p, null);
        public bool OnPlay(Vector2 p) => Inside(playBtn, p);
        public bool OnRestart(Vector2 p) => Inside(restartBtn, p);

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
            if (s0 != lastGap) { lastGap = s0; slide.text = showRun ? "SLIDE " + s0 + " m" : ""; }
            slide.color = gap < 40f ? new Color(1f, 0.45f, 0.35f) : Color.white;
            float k = Mathf.Clamp01(gap / gapMax);
            slideFill.rectTransform.anchorMax = new Vector2(showRun ? k : 0f, 1f);
            slideFill.color = gap < 40f ? new Color(1f, 0.45f, 0.35f) : Color.white;
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
