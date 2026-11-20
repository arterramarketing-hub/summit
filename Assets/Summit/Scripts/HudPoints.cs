using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// The points as they are made, and the medical bill as it is run up (the rest of the HUD is Hud.cs).
    ///
    /// Both live in one place, top right under the falls: clear of the rider, the snow ahead and the power-up
    /// timers down the left. While a combo is live its running total ticks there with the multiplier beside it,
    /// the window as a thin bar under it and the links stacking below, newest on top; it grows, warms and glows
    /// with its size, flies up into the score when it banks, and goes red, shakes and falls away when it is
    /// dropped. While the rider is down, the same place holds the fall's bill: a red dollar figure that jumps
    /// with every hit and flies into the corner total when they get up (or into the end screen's line).
    ///
    /// Nothing here reads the clock: the game steps it (StepPoints), so a pause holds it and the test tools can
    /// pose any moment of it.
    /// </summary>
    public partial class Hud
    {
        RectTransform pts;   // beside `safe`, not in it: the end screen's fade leaves the bill alone while it carries over
        CanvasGroup comboGroup, linesGroup;
        Text comboNum, comboMult, chip, billNum, endBill;
        Image comboGlow, billGlow, winBg, winFill;
        const int LineCount = 5;
        readonly Text[] lineText = new Text[LineCount];
        readonly string[] lineName = new string[LineCount];
        readonly int[] linePts = new int[LineCount], lineCnt = new int[LineCount];
        const float Right = -40f, Top = -296f, NumSize = 104f, LineStep = 50f, MaxWide = 620f;

        enum Mode { Off, Live, Banking, Dropping }
        Mode mode;
        int comboTarget, comboMultNow, tier;
        float comboShown, numPunch, multPunch, linePunch, tierScale = 0.5f, textClock, tickClock, modeT, glowT, numWide;
        int comboShownInt = -1, multShownInt = -1, lineCount;
        float windowLeft; bool windowHeld;

        // the bank: a copy of the number flies to the score, which punches and counts up
        float chipT = -1f; Vector2 chipFrom, chipTo; float chipScale0, chipScale1; bool chipBill;
        float scorePunch, scoreShown; int scoreTarget = -1, chipAmount; bool scoreCounting;

        // the bill
        float billTarget, billShown, billPunch, billShake, billScale = 0.55f, medShown, medPunch, medTotal;
        int billShownInt = -1, medShownInt = -2; bool billOn, medCounting, medShow;
        float endTarget = -1f, endShown; int endShownInt = -1;

        /// <summary>A soft tick while a number is counting (the game plays it): points, and the bill.</summary>
        public System.Action PointTick, BillTick;

        static readonly float[] TierAt = { 500f, 2000f, 6000f, 15000f };
        static readonly float[] TierSize = { 0.5f, 0.62f, 0.76f, 0.9f, 1f };
        static readonly Color[] TierColor =
        {
            Color.white, new Color(1f, 0.86f, 0.32f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.32f, 0.55f), new Color(0.5f, 0.95f, 1f),
        };
        static readonly float[] BillAt = { 2000f, 10000f, 50000f, 150000f };
        static readonly float[] BillSize = { 0.5f, 0.62f, 0.76f, 0.9f, 1f };
        static readonly Color[] BillColor =
        {
            new Color(1f, 0.6f, 0.55f), new Color(1f, 0.42f, 0.36f), new Color(1f, 0.26f, 0.2f), new Color(1f, 0.13f, 0.1f), new Color(1f, 0.9f, 0.85f),
        };
        static int TierOf(float v, float[] at) { int t = 0; while (t < at.Length && v >= at[t]) t++; return t; }

        void BuildPoints()
        {
            pts = new GameObject("Points", typeof(RectTransform)).GetComponent<RectTransform>();
            pts.SetParent(transform, false);
            pts.SetSiblingIndex(safe.GetSiblingIndex() + 1);
            // its own canvas: this is the one part of the HUD that moves every frame, and a canvas is re-batched whole
            // whenever anything in it moves (the rest only changes when a number does)
            pts.gameObject.AddComponent<Canvas>();
            ApplySafeArea();
            var soft = MakeGlow(64);
            var one = new Vector2(1f, 1f);

            var cg = new GameObject("Combo", typeof(RectTransform)).GetComponent<RectTransform>();
            cg.SetParent(pts, false); Stretch(cg);
            comboGroup = cg.gameObject.AddComponent<CanvasGroup>();
            comboGroup.blocksRaycasts = false; comboGroup.interactable = false;
            comboGlow = NewImage("Glow", cg, new Color(1f, 1f, 1f, 0f));
            comboGlow.sprite = soft;
            Place(comboGlow.rectTransform, one, new Vector2(Right + 60f, Top + 50f), new Vector2(620f, 230f), one);
            comboNum = NewText("Total", cg, one, new Vector2(Right, Top), (int)NumSize, TextAnchor.UpperRight);
            comboNum.fontStyle = FontStyle.BoldAndItalic;
            comboMult = NewText("Mult", cg, one, new Vector2(Right, Top), 70, TextAnchor.UpperRight);
            comboMult.fontStyle = FontStyle.BoldAndItalic;
            winBg = NewImage("Window", cg, new Color(0f, 0f, 0f, 0.35f));
            Place(winBg.rectTransform, one, new Vector2(Right, Top), new Vector2(240f, 8f), one);
            winFill = NewImage("Fill", winBg.transform, Color.white);
            var wf = winFill.rectTransform; wf.anchorMin = Vector2.zero; wf.anchorMax = Vector2.one; wf.offsetMin = wf.offsetMax = Vector2.zero;
            var lg = new GameObject("Links", typeof(RectTransform)).GetComponent<RectTransform>();
            lg.SetParent(cg, false); Stretch(lg);
            linesGroup = lg.gameObject.AddComponent<CanvasGroup>();
            linesGroup.blocksRaycasts = false; linesGroup.interactable = false;
            for (int i = 0; i < LineCount; i++)
            {
                lineText[i] = NewText("Link" + i, lg, one, new Vector2(Right, Top), 46, TextAnchor.UpperRight);
                lineText[i].text = "";
            }
            comboNum.text = comboMult.text = "";
            comboGroup.alpha = 0f;
            // (over a snowfield or a white peak a shadow alone is not enough: a dark edge all round)
            Edge(comboNum, 3f); Edge(comboMult, 2.5f);
            for (int i = 0; i < LineCount; i++) Edge(lineText[i], 2f);

            billGlow = NewImage("BillGlow", pts, new Color(1f, 0.1f, 0.05f, 0f));
            billGlow.sprite = soft;
            Place(billGlow.rectTransform, one, new Vector2(Right + 60f, Top + 50f), new Vector2(680f, 240f), one);
            billNum = NewText("Bill", pts, one, new Vector2(Right, Top), (int)NumSize, TextAnchor.UpperRight);
            billNum.fontStyle = FontStyle.BoldAndItalic; billNum.text = "";
            chip = NewText("Chip", pts, one, new Vector2(Right, Top), (int)NumSize, TextAnchor.UpperRight);
            chip.fontStyle = FontStyle.BoldAndItalic; chip.text = "";
            Edge(billNum, 3f); Edge(chip, 3f);

            // the end screen's bill: in the hint's second line (Show leaves it empty), counting up to the total
            endBill = NewText("EndBill", safe, new Vector2(0.5f, 0f), new Vector2(0f, 120f), 48, TextAnchor.LowerCenter);
            endBill.color = new Color(1f, 0.42f, 0.36f); endBill.text = "";
        }

        static void Edge(Text t, float px)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.03f, 0.05f, 0.12f, 0.55f);
            o.effectDistance = new Vector2(px, -px);
        }

        static Sprite MakeGlow(int px)
        {
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false);
            float c = (px - 1) * 0.5f;
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                    float a = (1f - d) * (1f - d);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>How wide a text is as it stands (reference pixels, before its own scale).</summary>
        static float Wide(Text t)
        {
            if (t.font == null || string.IsNullOrEmpty(t.text)) return 0f;
            return t.cachedTextGeneratorForLayout.GetPreferredWidth(t.text, t.GetGenerationSettings(Vector2.zero)) / t.pixelsPerUnit;
        }

        // ---------------------------------------------------------------- the combo

        /// <summary>The combo as it stands, each frame: what it would bank, its multiplier, its window, its links.</summary>
        public void ComboShow(Combo c)
        {
            if (c == null || !c.Active)
            {
                if (mode == Mode.Live) { mode = Mode.Off; }
                return;
            }
            if (mode != Mode.Live) { mode = Mode.Live; modeT = 0f; comboShown = 0f; comboShownInt = -1; multShownInt = -1; linesGroup.alpha = 1f; lineCount = 0; for (int i = 0; i < LineCount; i++) lineName[i] = null; }
            comboTarget = c.Total; comboMultNow = Mathf.Max(1, c.Mult);
            windowLeft = c.WindowLeft; windowHeld = c.Held;
            // the links, newest first
            int n = Mathf.Min(LineCount, c.Lines.Count);
            bool fresh = liveDue; liveDue = false;   // (a link being held ticks at the texts' own pace, not every frame)
            for (int i = 0; i < LineCount; i++)
            {
                if (i >= n) { if (lineName[i] != null) { lineName[i] = null; lineText[i].text = ""; } continue; }
                var l = c.Lines[c.Lines.Count - 1 - i];
                bool same = ReferenceEquals(lineName[i], l.name) && lineCnt[i] == l.count;
                if (same && (linePts[i] == l.points || (l.live && !fresh))) continue;   // (a live link ticks at the text's own pace)
                lineName[i] = l.name; linePts[i] = l.points; lineCnt[i] = l.count;
                var t = lineText[i];
                t.text = (l.count > 1 ? l.name + " x" + l.count : l.name) + "  +" + l.points.ToString("N0");
                t.color = l.live ? new Color(1f, 0.95f, 0.6f) : l.sketchy ? new Color(1f, 0.7f, 0.5f) : Color.white;
                // fitted to the width; a name too long to stay readable that way goes onto two lines instead
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                float w = Wide(t);
                lineFit[i] = w > MaxWide ? MaxWide / w : 1f; lineRows[i] = 1;
                if (lineFit[i] < MinFit)
                {
                    lineFit[i] = MinFit; lineRows[i] = 2;
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    t.rectTransform.sizeDelta = new Vector2(MaxWide / MinFit, t.rectTransform.sizeDelta.y);
                }
            }
            lineCount = n;
        }
        readonly float[] lineFit = { 1f, 1f, 1f, 1f, 1f };
        readonly int[] lineRows = { 1, 1, 1, 1, 1 };
        bool liveDue = true, scorePunched, medPunched;
        float billWide;
        const float MinFit = 0.74f;

        /// <summary>A link went in: the newest line punches in and the rest move down; the multiplier punches if it rose.</summary>
        public void ComboAdded(Combo.Line line, bool multUp)
        {
            linePunch = 1f;
            numPunch = Mathf.Max(numPunch, Mathf.Clamp(0.12f + line.points / 4000f, 0.12f, 0.5f));
            if (multUp) multPunch = 1f;
        }

        /// <summary>Banked: the number flies up into the score.</summary>
        public void ComboBanked(int total)
        {
            mode = Mode.Banking; modeT = 0f;
            chipAmount = total;
            StartChip("+" + total.ToString("N0"), comboNum.color, tierScale, false);
            comboNum.text = ""; comboMult.text = ""; comboShownInt = -1; multShownInt = -1;
        }

        /// <summary>Dropped: it goes red, shakes, and falls away.</summary>
        public void ComboDropped(int total)
        {
            mode = Mode.Dropping; modeT = 0f;
            comboNum.text = total.ToString("N0"); comboShownInt = -1;
        }

        void StartChip(string text, Color c, float scale, bool bill)
        {
            chip.text = text; chip.color = c; chipT = 0f; chipBill = bill;
            chipFrom = new Vector2(Right, Top); chipScale0 = scale;
            if (bill) { chipTo = new Vector2(-40f, -226f); chipScale1 = 40f / NumSize; }   // the corner's total
            else
            {
                // under the score, top centre
                float half = pts.rect.width * 0.5f, w = Wide(chip) * 0.4f;
                chipTo = new Vector2(-half + w * 0.5f, -60f); chipScale1 = 0.4f;
            }
        }

        // ---------------------------------------------------------------- the bill

        /// <summary>The fall's bill just went up by this much: the figure jumps, harder for more.</summary>
        public void BillHit(float amount)
        {
            if (amount <= 0f) return;
            if (!billOn) { billOn = true; billShown = 0f; billShownInt = -1; billTarget = 0f; }
            billTarget += amount;
            float k = Mathf.Clamp01(Mathf.Log10(Mathf.Max(amount, 50f) / 50f) / 3.4f);   // 50: 0 .. 850: 0.36 .. 9,400: 0.67 .. 124,000: 1
            billPunch = Mathf.Max(billPunch, 0.18f + 0.9f * k);
            billShake = Mathf.Max(billShake, k);
        }

        /// <summary>The rider is up again: the fall's figure flies into the total in the corner.</summary>
        public void BillBank()
        {
            if (!billOn) return;
            billOn = false;
            StartChip("$" + Mathf.RoundToInt(billTarget).ToString("N0"), billNum.color, billScale, true);
            billNum.text = ""; billShownInt = -1; billTarget = 0f; billShown = 0f;
            medCounting = true;
        }

        /// <summary>The run is over: the figure carries into the end screen's line, which counts up to the whole bill.</summary>
        public void BillEnd(float total)
        {
            float carried = billOn ? billTarget : 0f;
            billOn = false; billNum.text = ""; billShownInt = -1; billTarget = 0f; billShown = 0f;
            chipT = -1f; chip.text = "";
            if (total <= 0f) { endTarget = -1f; endBill.text = ""; return; }
            endTarget = total; endShown = Mathf.Max(0f, total - carried); endShownInt = -1;
            if (carried <= 0f) endShown = total;
            ShowEndBill();
        }

        void ShowEndBill()
        {
            int d = Mathf.RoundToInt(endShown);
            if (d == endShownInt) return;
            endShownInt = d;
            endBill.text = "MEDICAL BILL  $" + d.ToString("N0");
        }

        /// <summary>Everything here cleared (a new run).</summary>
        public void ClearPoints()
        {
            mode = Mode.Off; comboGroup.alpha = 0f; comboNum.text = comboMult.text = chip.text = billNum.text = endBill.text = "";
            for (int i = 0; i < LineCount; i++) { lineText[i].text = ""; lineName[i] = null; }
            chipT = -1f; billOn = false; billTarget = billShown = 0f; billShownInt = -1; endTarget = -1f; endShownInt = -1;
            medShown = 0f; medCounting = false; medShownInt = -2; scoreCounting = false; scorePunch = medPunch = 0f;
            comboGlow.color = billGlow.color = new Color(1f, 1f, 1f, 0f);
            score.rectTransform.localScale = Vector3.one; medical.rectTransform.localScale = Vector3.one;
        }

        // ---------------------------------------------------------------- each frame

        static float Approach(float shown, float target, float dt, float least)
        {
            float d = target - shown;
            if (Mathf.Abs(d) < 0.5f) return target;
            return Mathf.MoveTowards(shown, target, Mathf.Max(Mathf.Abs(d) * 9f, least) * dt);
        }

        /// <summary>Move every number and animation on by dt (the game passes real time, and holds it while paused).</summary>
        public void StepPoints(float dt)
        {
            if (pts == null) return;
            textClock -= dt; tickClock -= dt; glowT += dt; modeT += dt;
            bool texts = textClock <= 0f;
            if (texts) { textClock = 0.045f; liveDue = true; }
            numPunch = Mathf.Max(0f, numPunch - dt * (2.5f + 9f * numPunch));
            multPunch = Mathf.Max(0f, multPunch - dt * 4.5f);
            linePunch = Mathf.Max(0f, linePunch - dt * 6f);
            scorePunch = Mathf.Max(0f, scorePunch - dt * 3.5f);
            medPunch = Mathf.Max(0f, medPunch - dt * 3.5f);
            billPunch = Mathf.Max(0f, billPunch - dt * (2.2f + 6f * billPunch));
            billShake = Mathf.Max(0f, billShake - dt * 2.4f);

            // ---- the combo
            float alpha = comboGroup.alpha;
            if (mode == Mode.Live)
            {
                alpha = Mathf.MoveTowards(alpha, 1f, dt * 10f);
                float before = comboShown;
                comboShown = Approach(comboShown, comboTarget, dt, 300f);
                if (comboShown != before && tickClock <= 0f && comboTarget - before > 8f) { tickClock = 0.11f; PointTick?.Invoke(); }
                int t = TierOf(comboTarget, TierAt);
                if (t > tier) numPunch = Mathf.Max(numPunch, 0.45f);   // up a size
                tier = t;
                tierScale = Mathf.MoveTowards(tierScale, TierSize[tier], dt * 1.6f);
                int shown = Mathf.RoundToInt(comboShown);
                if (shown != comboShownInt && (texts || shown == comboTarget))
                {
                    comboShownInt = shown;
                    comboNum.text = shown.ToString("N0");
                    numWide = Wide(comboNum);
                }
                if (comboMultNow != multShownInt) { multShownInt = comboMultNow; comboMult.text = comboMultNow > 1 ? "x" + comboMultNow : ""; }
                Color c = TierColor[tier];
                comboNum.color = c;
                comboMult.color = Color.Lerp(new Color(1f, 0.86f, 0.32f), Color.white, multPunch);
                float fit = numWide * tierScale > 520f ? 520f / (numWide * tierScale) : 1f;
                float s = tierScale * fit * (1f + numPunch);
                float jx = tier >= 4 ? Mathf.Sin(glowT * 47f) * 2.5f : 0f, jy = tier >= 4 ? Mathf.Cos(glowT * 39f) * 2.5f : 0f;
                Lay(s, tierScale * fit, jx, jy, 1f);
                float ga = tier <= 1 ? 0f : tier == 2 ? 0.16f : tier == 3 ? 0.3f : 0.4f + 0.12f * Mathf.Sin(glowT * 9f);
                comboGlow.color = new Color(c.r, c.g, c.b, ga + 0.25f * numPunch * (tier >= 2 ? 1f : 0f));
                if (!winBg.gameObject.activeSelf) winBg.gameObject.SetActive(true);
                winFill.rectTransform.anchorMin = new Vector2(1f - (windowHeld ? 1f : windowLeft), 0f);
                winFill.color = windowHeld ? new Color(c.r, c.g, c.b, 0.55f + 0.3f * Mathf.Sin(glowT * 6f)) : windowLeft < 0.3f ? new Color(1f, 0.4f, 0.3f) : c;
                linesGroup.alpha = 1f;
            }
            else if (mode == Mode.Banking)
            {
                // the number has gone up to the score (the chip); the links fade where they are
                linesGroup.alpha = Mathf.MoveTowards(linesGroup.alpha, 0f, dt * 4f);
                if (winBg.gameObject.activeSelf) winBg.gameObject.SetActive(false);
                comboGlow.color = new Color(comboGlow.color.r, comboGlow.color.g, comboGlow.color.b, Mathf.MoveTowards(comboGlow.color.a, 0f, dt * 2f));
                if (linesGroup.alpha <= 0f && chipT < 0f) { mode = Mode.Off; alpha = 0f; }
            }
            else if (mode == Mode.Dropping)
            {
                // lost: red, a shake, then it falls away
                float u = modeT / 0.85f;
                var red = new Color(1f, 0.22f, 0.18f);
                comboNum.color = red; comboMult.color = red;
                float shake = Mathf.Sin(modeT * 70f) * 16f * Mathf.Clamp01(1f - modeT / 0.3f);
                float fall = modeT > 0.25f ? (modeT - 0.25f) * (modeT - 0.25f) * 900f : 0f;
                float fit = numWide * tierScale > 520f ? 520f / (numWide * tierScale) : 1f;
                Lay(tierScale * fit, tierScale * fit, shake, -fall, Mathf.Clamp01(1f - modeT / 0.22f));   // (the links go at once: the number falls alone)
                if (winBg.gameObject.activeSelf) winBg.gameObject.SetActive(false);
                comboGlow.color = new Color(1f, 0.1f, 0.05f, 0.3f * Mathf.Clamp01(1f - u));
                alpha = Mathf.Clamp01(1.6f - u * 1.6f);
                if (u >= 1f) { mode = Mode.Off; alpha = 0f; comboNum.text = comboMult.text = ""; }
            }
            else
            {
                alpha = Mathf.MoveTowards(alpha, 0f, dt * 6f);
                if (alpha <= 0f && comboShownInt != -1) { comboShownInt = -1; multShownInt = -1; comboNum.text = comboMult.text = ""; tier = 0; tierScale = TierSize[0]; }
            }
            comboGroup.alpha = alpha;

            // ---- the chip: a number on its way (points to the score, a fall's bill to the corner)
            if (chipT >= 0f)
            {
                chipT += dt;
                float u = Mathf.Clamp01(chipT / 0.42f), e = u * u * (3f - 2f * u);
                e *= e;   // slow off the mark, fast into the score
                chip.rectTransform.anchoredPosition = Vector2.Lerp(chipFrom, chipTo, e) + new Vector2(0f, Mathf.Sin(u * Mathf.PI) * 40f);
                float s = Mathf.Lerp(chipScale0 * 1.15f, chipScale1, e);
                chip.rectTransform.localScale = new Vector3(s, s, 1f);
                var cc = chip.color; cc.a = u < 0.85f ? 1f : (1f - u) / 0.15f; chip.color = cc;
                if (u >= 1f)
                {
                    chipT = -1f; chip.text = "";
                    if (chipBill) { medPunch = 1f; medCounting = true; }
                    else { scorePunch = 1f; scoreCounting = true; }
                }
            }

            // ---- the score: counts up to what was banked, with a punch
            if (scoreCounting)
            {
                float before = scoreShown;
                scoreShown = Approach(scoreShown, scoreTarget, dt, 600f);
                if (scoreShown >= scoreTarget - 0.5f) { scoreShown = scoreTarget; scoreCounting = false; }
                if (texts || !scoreCounting) ShowScore();
                if (scoreShown != before && tickClock <= 0f) { tickClock = 0.06f; PointTick?.Invoke(); }
            }
            if (scorePunch > 0f || scorePunched)
            {
                scorePunched = scorePunch > 0f;
                float sp = 1f + 0.28f * scorePunch * scorePunch;
                score.rectTransform.localScale = new Vector3(sp, sp, 1f);
                score.color = Color.Lerp(Color.white, new Color(1f, 0.9f, 0.45f), scorePunch);
            }

            // ---- the bill
            // (a combo just lost is still shaking in this spot: the bill waits for it to fall out of the way)
            if (billOn && mode == Mode.Dropping && modeT < 0.5f) { }
            else if (billOn)
            {
                float before = billShown;
                billShown = Approach(billShown, billTarget, dt, 900f);
                if (billShown != before && tickClock <= 0f) { tickClock = 0.06f; BillTick?.Invoke(); }
                int bt = TierOf(billTarget, BillAt);
                billScale = Mathf.MoveTowards(billScale, BillSize[bt], dt * 2.2f);
                int shown = Mathf.RoundToInt(billShown);
                if (shown != billShownInt && (texts || Mathf.Abs(billShown - billTarget) < 0.5f)) { billShownInt = shown; billNum.text = "$" + shown.ToString("N0"); billWide = Wide(billNum); }
                Color c = BillColor[bt];
                billNum.color = Color.Lerp(c, Color.white, Mathf.Clamp01(billPunch * 0.8f));
                float fit = billWide * billScale > 600f ? 600f / (billWide * billScale) : 1f;
                float s = billScale * fit * (1f + billPunch * 0.55f);
                float sh = billShake * billShake * 22f + (bt >= 4 ? 2.5f : 0f);
                billNum.rectTransform.anchoredPosition = new Vector2(Right + Mathf.Sin(glowT * 83f) * sh, Top + Mathf.Cos(glowT * 71f) * sh);
                billNum.rectTransform.localScale = new Vector3(s, s, 1f);
                billGlow.color = new Color(1f, 0.1f, 0.05f, (bt <= 0 ? 0f : bt == 1 ? 0.14f : bt == 2 ? 0.26f : 0.4f) + 0.3f * billPunch);
            }
            else if (billGlow.color.a > 0f) billGlow.color = new Color(1f, 0.1f, 0.05f, Mathf.MoveTowards(billGlow.color.a, 0f, dt * 3f));

            // ---- the corner's total: counts up when a fall's bill lands in it
            if (medCounting)
            {
                float target = medShow ? medTotal - (billOn ? billTarget : 0f) : 0f;
                if (chipT < 0f || !chipBill)
                {
                    float before = medShown;
                    medShown = Approach(medShown, target, dt, 900f);
                    if (medShown != before && tickClock <= 0f) { tickClock = 0.06f; BillTick?.Invoke(); }
                    if (Mathf.Abs(medShown - target) < 0.5f) { medShown = target; medCounting = false; }
                    if (texts || !medCounting) ShowMedical();
                }
            }
            if (medPunch > 0f || medPunched)
            {
                medPunched = medPunch > 0f;
                float mp = 1f + 0.3f * medPunch * medPunch;
                medical.rectTransform.localScale = new Vector3(mp, mp, 1f);
            }

            // ---- the end screen's line
            if (endTarget >= 0f && endShown < endTarget)
            {
                endShown = Approach(endShown, endTarget, dt, 1500f);
                if (tickClock <= 0f) { tickClock = 0.06f; BillTick?.Invoke(); }
                if (texts || endShown >= endTarget) ShowEndBill();
            }
        }

        /// <summary>Lay the number, the multiplier, the window and the links out for the number's size (s with its punch, rest without).</summary>
        void Lay(float s, float rest, float jx, float jy, float lineAlpha)
        {
            var nr = comboNum.rectTransform;
            nr.anchoredPosition = new Vector2(Right + jx, Top + jy);
            nr.localScale = new Vector3(s, s, 1f);
            float ms = rest * (1f + 0.6f * multPunch);
            var mr = comboMult.rectTransform;
            mr.anchoredPosition = new Vector2(Right - numWide * rest - 22f * rest + jx, Top - NumSize * rest * 0.22f + jy);
            mr.localScale = new Vector3(ms, ms, 1f);
            float y = Top - NumSize * rest * 1.12f - 4f;
            winBg.rectTransform.anchoredPosition = new Vector2(Right, y);
            comboGlow.rectTransform.anchoredPosition = new Vector2(Right + 70f, Top + 60f * rest);
            comboGlow.rectTransform.sizeDelta = new Vector2((numWide + 260f) * rest + 120f, 300f * rest);
            y -= 22f;
            for (int i = 0; i < LineCount; i++)
            {
                var lt = lineText[i];
                if (i >= lineCount) continue;
                // newest on top and largest; the older ones smaller and fainter, moving down as a new one comes in
                float size = i == 0 ? 1f : i == 1 ? 0.88f : i == 2 ? 0.8f : i == 3 ? 0.74f : 0.7f;
                float a = i == 0 ? 1f : i == 1 ? 0.88f : i == 2 ? 0.74f : i == 3 ? 0.6f : 0.48f;
                float slide = i == 0 ? 0f : LineStep * size * linePunch;
                float ls = size * lineFit[i] * (i == 0 ? 1f + 0.3f * linePunch : 1f);
                lt.rectTransform.anchoredPosition = new Vector2(Right, y + slide);
                lt.rectTransform.localScale = new Vector3(ls, ls, 1f);
                var c = lt.color; c.a = a * lineAlpha * (i == 0 ? 1f - 0.5f * linePunch * linePunch : 1f); lt.color = c;
                y -= LineStep * size * (lineRows[i] > 1 ? 1f + lineFit[i] : 1f);
            }
        }

        void ShowScore()
        {
            int p0 = Mathf.RoundToInt(scoreShown);
            if (p0 == lastPts) return;
            lastPts = p0;
            score.text = p0.ToString("N0");
        }

        void ShowMedical()
        {
            int d = medShow ? Mathf.RoundToInt(medShown) : -1;
            if (d == medShownInt) return;
            medShownInt = d;
            medical.text = medShow && d > 0 ? "MEDICAL  $" + d.ToString("N0") : "";
        }
    }
}
