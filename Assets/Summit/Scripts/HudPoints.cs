using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// The points as they are made (the rest of the HUD is Hud.cs; the medical receipt is HudReceipt.cs).
    ///
    /// One place, top right under the falls: clear of the rider, the snow ahead and the power-up timers down the
    /// left. While a combo is live its running total ticks there with the multiplier beside it, the window as a
    /// thin bar under it, and the links under that as lines on a faint slip, name left and points right, newest on
    /// top: each new one stamps in (in large, slammed down to size, a tilt straightened) and a glint of frost
    /// crosses it, the older ones sliding down and fading. The number grows, warms and glows with its size. When
    /// the combo banks the lines fold up into the number, which then flies up into the score; when it is dropped
    /// every line tears in two (the name one way, the points the other) and falls, and the number goes red,
    /// shakes and falls after them. The medical receipt takes the same place while the rider is down.
    ///
    /// Nothing here reads the clock: the game steps it (StepPoints), so a pause holds it and the test tools can
    /// pose any moment of it.
    /// </summary>
    public partial class Hud
    {
        RectTransform pts;   // beside `safe`, not in it: the end screen's fade leaves the receipt alone while it carries over
        CanvasGroup comboGroup, linesGroup;
        Text comboNum, comboMult, chip;
        Image comboGlow, winBg, winFill, cSlip, cGlint, multBurst;
        const int LineCount = 5;
        readonly SlipRow[] cRows = new SlipRow[LineCount];
        readonly string[] lineName = new string[LineCount];
        readonly int[] linePts = new int[LineCount], lineCnt = new int[LineCount];
        const float Right = -40f, Top = -296f, NumSize = 104f, LineStep = 52f, CW = 560f;
        const int CFont = 44;

        enum Mode { Off, Live, Banking, Dropping }
        Mode mode;
        int comboTarget, comboMultNow, tier;
        float comboShown, numPunch, multPunch, linePunch, tierScale = 0.5f, textClock, tickClock, modeT, glowT, numWide;
        int comboShownInt = -1, multShownInt = -1, lineCount;
        float windowLeft, multWide; bool windowHeld, stampDue, bankPending;
        int bankTotal;

        // the bank: a copy of the number flies to the score, which punches and counts up
        float chipT = -1f; Vector2 chipFrom, chipTo; float chipScale0, chipScale1; bool chipBill;
        float scorePunch, scoreShown; int scoreTarget = -1, chipAmount; bool scoreCounting;

        // the fall's bill (the receipt, HudReceipt.cs) and the corner's total
        float billTarget, medShown, medPunch, medTotal;
        int medShownInt = -2; bool billOn, medCounting, medShow;

        /// <summary>A soft tick while a number is counting (the game plays it): points, and the bill.</summary>
        public System.Action PointTick, BillTick;

        static readonly float[] TierAt = { 500f, 2000f, 6000f, 15000f };
        static readonly float[] TierSize = { 0.5f, 0.62f, 0.76f, 0.9f, 1f };
        static readonly Color[] TierColor =
        {
            Color.white, new Color(1f, 0.86f, 0.32f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.32f, 0.55f), new Color(0.5f, 0.95f, 1f),
        };
        static readonly float[] BillAt = { 2000f, 10000f, 50000f, 150000f };
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
            if (panelSprite == null) panelSprite = MakePanel();
            var one = new Vector2(1f, 1f);

            var cg = new GameObject("Combo", typeof(RectTransform)).GetComponent<RectTransform>();
            cg.SetParent(pts, false); Stretch(cg);
            comboGroup = cg.gameObject.AddComponent<CanvasGroup>();
            comboGroup.blocksRaycasts = false; comboGroup.interactable = false;
            comboGlow = NewImage("Glow", cg, new Color(1f, 1f, 1f, 0f));
            comboGlow.sprite = soft;
            Place(comboGlow.rectTransform, one, new Vector2(Right + 60f, Top + 50f), new Vector2(620f, 230f), one);
            var lg = new GameObject("Links", typeof(RectTransform)).GetComponent<RectTransform>();
            lg.SetParent(cg, false); Stretch(lg);
            linesGroup = lg.gameObject.AddComponent<CanvasGroup>();
            linesGroup.blocksRaycasts = false; linesGroup.interactable = false;
            // the slip the links are printed on: faint, enough to read them over the snow
            cSlip = NewImage("Slip", lg, new Color(Paper.r, Paper.g, Paper.b, 0f));
            cSlip.sprite = panelSprite; cSlip.type = Image.Type.Sliced;
            Place(cSlip.rectTransform, one, new Vector2(Right + 18f, Top), new Vector2(CW + 40f, 100f), one);
            for (int i = 0; i < LineCount; i++) cRows[i] = NewRow(lg, CW, CFont);
            cGlint = NewImage("Glint", lg, new Color(0.85f, 0.94f, 1f, 0f));
            cGlint.sprite = soft;
            Place(cGlint.rectTransform, one, Vector2.zero, new Vector2(24f, CFont * 2f), new Vector2(0.5f, 0.5f));
            cGlint.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);

            multBurst = NewImage("MultBurst", cg, new Color(1f, 0.9f, 0.5f, 0f));
            multBurst.sprite = soft;
            Place(multBurst.rectTransform, one, new Vector2(Right, Top), new Vector2(160f, 160f), new Vector2(0.5f, 0.5f));
            comboNum = NewText("Total", cg, one, new Vector2(Right, Top), (int)NumSize, TextAnchor.UpperRight);
            comboNum.fontStyle = FontStyle.BoldAndItalic;
            comboMult = NewText("Mult", cg, one, new Vector2(Right, Top), 70, TextAnchor.UpperRight);
            comboMult.fontStyle = FontStyle.BoldAndItalic;
            winBg = NewImage("Window", cg, new Color(0f, 0f, 0f, 0.35f));
            Place(winBg.rectTransform, one, new Vector2(Right, Top), new Vector2(240f, 8f), one);
            winFill = NewImage("Fill", winBg.transform, Color.white);
            var wf = winFill.rectTransform; wf.anchorMin = Vector2.zero; wf.anchorMax = Vector2.one; wf.offsetMin = wf.offsetMax = Vector2.zero;
            comboNum.text = comboMult.text = "";
            comboGroup.alpha = 0f;
            // (over a snowfield or a white peak a shadow alone is not enough: a dark edge all round)
            Edge(comboNum, 3f); Edge(comboMult, 2.5f);

            BuildReceipt(soft);

            chip = NewText("Chip", pts, one, new Vector2(Right, Top), (int)NumSize, TextAnchor.UpperRight);
            chip.fontStyle = FontStyle.BoldAndItalic; chip.text = "";
            Edge(chip, 3f);
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
            if (mode != Mode.Live)
            {
                if (bankPending) LaunchBank();   // (a new combo straight after a bank: the bank goes up now)
                mode = Mode.Live; modeT = 0f; comboShown = 0f; comboShownInt = -1; multShownInt = -1; linesGroup.alpha = 1f; lineCount = 0;
                for (int i = 0; i < LineCount; i++) { lineName[i] = null; ResetRow(cRows[i]); }
            }
            comboTarget = c.Total; comboMultNow = Mathf.Max(1, c.Mult);
            windowLeft = c.WindowLeft; windowHeld = c.Held;
            // the links, newest first
            int n = Mathf.Min(LineCount, c.Lines.Count);
            bool fresh = liveDue; liveDue = false;   // (a link being held ticks at the texts' own pace, not every frame)
            for (int i = 0; i < LineCount; i++)
            {
                var row = cRows[i];
                if (i >= n) { if (lineName[i] != null) { lineName[i] = null; row.name.text = row.amount.text = ""; } continue; }
                var l = c.Lines[c.Lines.Count - 1 - i];
                bool same = ReferenceEquals(lineName[i], l.name) && lineCnt[i] == l.count;
                if (same && (linePts[i] == l.points || (l.live && !fresh))) continue;   // (a live link ticks at the text's own pace)
                lineName[i] = l.name; linePts[i] = l.points; lineCnt[i] = l.count;
                Color col = l.live ? new Color(1f, 0.95f, 0.6f) : l.sketchy ? new Color(1f, 0.7f, 0.5f) : Color.white;
                SetRow(row, l.count > 1 ? l.name + "  x" + l.count : l.name, "+" + l.points.ToString("N0"), col, CW, false);
                row.service = l.live;
            }
            lineCount = n;
            if (stampDue && n > 0)
            {
                stampDue = false;
                cRows[0].age = 0f;
                cGlint.transform.SetAsLastSibling();
            }
        }
        bool liveDue = true, scorePunched, medPunched;

        static void ResetRow(SlipRow r)
        {
            r.name.rectTransform.anchoredPosition = Vector2.zero; r.name.rectTransform.localRotation = Quaternion.identity;
            r.amount.rectTransform.anchoredPosition = Vector2.zero; r.amount.rectTransform.localRotation = Quaternion.identity;
            r.amount.rectTransform.localScale = Vector3.one;
            r.rt.localRotation = Quaternion.identity; r.age = 9f;
        }

        /// <summary>A link went in: the newest line stamps in and the rest move down; the multiplier punches if it rose.</summary>
        public void ComboAdded(Combo.Line line, bool multUp)
        {
            linePunch = 1f; stampDue = true;
            numPunch = Mathf.Max(numPunch, Mathf.Clamp(0.12f + line.points / 4000f, 0.12f, 0.5f));
            if (multUp) multPunch = 1f;
        }

        /// <summary>Banked: the lines fold up into the number, then it flies up into the score.</summary>
        public void ComboBanked(int total)
        {
            mode = Mode.Banking; modeT = 0f;
            bankTotal = total; bankPending = true;
            comboTarget = total; comboShown = total;
            comboNum.text = total.ToString("N0"); comboShownInt = total; numWide = Wide(comboNum);
        }

        void LaunchBank()
        {
            bankPending = false;
            chipAmount = bankTotal;
            StartChip("+" + bankTotal.ToString("N0"), comboNum.color, tierScale, false);
            comboNum.text = ""; comboMult.text = ""; comboShownInt = -1; multShownInt = -1;
        }

        /// <summary>Dropped: the lines tear and fall; the number goes red, shakes, and falls after them.</summary>
        public void ComboDropped(int total)
        {
            if (bankPending) LaunchBank();
            mode = Mode.Dropping; modeT = 0f;
            comboNum.text = total.ToString("N0"); comboShownInt = -1;
        }

        void StartChip(string text, Color c, float scale, bool bill)
        {
            chip.text = text; chip.color = c; chipT = 0f; chipBill = bill;
            chipFrom = new Vector2(Right, Top); chipScale0 = scale;
            // under the score, top centre
            float half = pts.rect.width * 0.5f, w = Wide(chip) * 0.4f;
            chipTo = new Vector2(-half + w * 0.5f, -60f); chipScale1 = 0.4f;
        }

        /// <summary>Everything here cleared (a new run).</summary>
        public void ClearPoints()
        {
            mode = Mode.Off; comboGroup.alpha = 0f; comboNum.text = comboMult.text = chip.text = "";
            for (int i = 0; i < LineCount; i++) { cRows[i].name.text = cRows[i].amount.text = ""; lineName[i] = null; ResetRow(cRows[i]); }
            chipT = -1f; billOn = false; billTarget = 0f; bankPending = false;
            medShown = 0f; medCounting = false; medShownInt = -2; scoreCounting = false; scorePunch = medPunch = 0f;
            comboGlow.color = new Color(1f, 1f, 1f, 0f);
            score.rectTransform.localScale = Vector3.one; medical.rectTransform.localScale = Vector3.one;
            ClearReceipt();
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
            for (int i = 0; i < LineCount; i++) if (cRows[i].age < 9f) cRows[i].age += dt;

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
                if (comboMultNow != multShownInt) { multShownInt = comboMultNow; comboMult.text = comboMultNow > 1 ? "x" + comboMultNow : ""; multWide = Wide(comboMult); }
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
                // the lines fold up into the number; when the last is in, the number goes up to the score (the chip)
                float fold = 0.16f + 0.035f * Mathf.Max(0, lineCount - 1);
                if (winBg.gameObject.activeSelf) winBg.gameObject.SetActive(false);
                if (bankPending)
                {
                    float fit = numWide * tierScale > 520f ? 520f / (numWide * tierScale) : 1f;
                    float k = Mathf.Clamp01(modeT / fold);
                    numPunch = Mathf.Max(numPunch, 0.35f * k * k);
                    Lay(tierScale * fit * (1f + numPunch), tierScale * fit, 0f, 0f, 1f);
                    if (modeT >= fold + 0.06f) LaunchBank();
                }
                else linesGroup.alpha = Mathf.MoveTowards(linesGroup.alpha, 0f, dt * 6f);
                comboGlow.color = new Color(comboGlow.color.r, comboGlow.color.g, comboGlow.color.b, Mathf.MoveTowards(comboGlow.color.a, 0f, dt * 2f));
                if (!bankPending && chipT < 0f) { mode = Mode.Off; alpha = 0f; }
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
                Lay(tierScale * fit, tierScale * fit, shake, -fall, 1f);
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

            // ---- the chip: a number on its way (points to the score)
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

            // ---- the fall's receipt, and a hit taken while riding (HudReceipt.cs)
            StepReceipt(dt, texts);
            StepRideHit(dt);

            // ---- the corner's total: counts up when a fall's receipt lands in it
            if (medCounting)
            {
                float target = medShow ? medTotal - (billOn ? billTarget : 0f) - rideHeld : 0f;
                if (!ReceiptFlying && !RideHitShowing && (chipT < 0f || !chipBill))
                {
                    float before = medShown;
                    medShown = Approach(medShown, target, dt, 900f);
                    if (medShown != before && tickClock <= 0f) { tickClock = 0.06f; BillTick?.Invoke(); }
                    if (Mathf.Abs(medShown - target) < 0.5f) { medShown = target; medCounting = false; }
                    if (texts || !medCounting) ShowMedical();
                }
            }
            // (under a riding hit's slip the corner's total steps aside, and comes back as the slip folds into it)
            float medA = RideHitShowing ? Mathf.Clamp01((rideT - RideHold) / RideFold) : 1f;
            if (Mathf.Abs(medical.color.a - medA) > 0.01f) { var mc = medical.color; mc.a = medA; medical.color = mc; }
            if (medPunch > 0f || medPunched)
            {
                medPunched = medPunch > 0f;
                float mp = 1f + 0.3f * medPunch * medPunch;
                medical.rectTransform.localScale = new Vector3(mp, mp, 1f);
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
            Vector2 mp = new Vector2(Right - numWide * rest - 22f * rest + jx, Top - NumSize * rest * 0.22f + jy);
            mr.anchoredPosition = mp;
            mr.localScale = new Vector3(ms, ms, 1f);
            // a burst of light behind the multiplier as it rises
            float mw = multWide * rest;
            multBurst.rectTransform.anchoredPosition = mp + new Vector2(-mw * 0.5f, -35f * rest);
            float bs = (0.6f + 1.4f * (1f - multPunch)) * rest * 2f;
            multBurst.rectTransform.localScale = new Vector3(bs, bs, 1f);
            multBurst.color = new Color(1f, 0.9f, 0.5f, 0.7f * multPunch * multPunch);

            float y = Top - NumSize * rest * 1.12f - 4f;
            winBg.rectTransform.anchoredPosition = new Vector2(Right, y);
            comboGlow.rectTransform.anchoredPosition = new Vector2(Right + 70f, Top + 60f * rest);
            comboGlow.rectTransform.sizeDelta = new Vector2((numWide + 260f) * rest + 120f, 300f * rest);
            y -= 24f;
            float slipTop = y + 6f, slipBottom = y;
            float numY = Top - NumSize * rest * 0.5f;
            int foldN = Mathf.Max(1, lineCount);
            for (int i = 0; i < LineCount; i++)
            {
                var row = cRows[i];
                if (i >= lineCount) { row.rt.gameObject.SetActive(false); continue; }
                if (!row.rt.gameObject.activeSelf) row.rt.gameObject.SetActive(true);
                // newest on top and largest; the older ones smaller and fainter, moving down as a new one comes in
                float size = i == 0 ? 1f : i == 1 ? 0.86f : i == 2 ? 0.78f : i == 3 ? 0.72f : 0.68f;
                // (wider as it is smaller, so every line's name starts and its points end where the newest one's do)
                float rw = CW / size;
                if (Mathf.Abs(row.rt.sizeDelta.x - rw) > 0.5f) row.rt.sizeDelta = new Vector2(rw, row.rt.sizeDelta.y);
                float a = i == 0 ? 1f : i == 1 ? 0.9f : i == 2 ? 0.76f : i == 3 ? 0.62f : 0.5f;
                float h = LineStep * size;
                float cy = y - h * 0.5f;
                float slide = i == 0 ? 0f : h * linePunch;
                float sc = size, x = Right, rot = 0f;
                if (i == 0 && row.age < 0.16f)
                {
                    // stamped: in large and tilted, slammed down to size
                    float k = Mathf.Clamp01(row.age / 0.16f), o = (1f - k) * (1f - k);
                    sc *= 1f + 0.6f * o; x += 46f * o; rot = -7f * o;
                    a *= 0.25f + 0.75f * k;
                }
                else if (i == 0 && row.age < 0.3f) sc *= 1f - 0.05f * Mathf.Sin((row.age - 0.16f) / 0.14f * Mathf.PI);   // (the bounce)
                Vector2 at = new Vector2(x, cy + slide);
                float rowA = a * lineAlpha;
                if (mode == Mode.Banking)
                {
                    // fold up into the number, the oldest first
                    float k = Mathf.Clamp01((modeT - (foldN - 1 - i) * 0.035f) / 0.16f);
                    at = Vector2.Lerp(at, new Vector2(Right - 40f, numY), k * k);
                    sc *= 1f - 0.75f * k; rowA *= 1f - k;
                }
                else if (mode == Mode.Dropping)
                {
                    // torn in two, the name one way and the points the other, and down they go
                    float t = Mathf.Max(0f, modeT - i * 0.03f), tear = Mathf.Clamp01(t / 0.08f);
                    float dir = i % 2 == 0 ? 1f : -1f;
                    var nrt = row.name.rectTransform; var art = row.amount.rectTransform;
                    nrt.anchoredPosition = new Vector2(-(18f * tear + 50f * t), -900f * t * t);
                    nrt.localRotation = Quaternion.Euler(0f, 0f, dir * (6f * tear + 80f * t));
                    art.anchoredPosition = new Vector2(18f * tear + 70f * t, -1000f * t * t);
                    art.localRotation = Quaternion.Euler(0f, 0f, -dir * (8f * tear + 110f * t));
                    rowA *= Mathf.Clamp01(1f - (t - 0.1f) / 0.45f);
                }
                row.rt.anchoredPosition = at;
                row.rt.localScale = new Vector3(sc, sc, 1f);
                row.rt.localRotation = Quaternion.Euler(0f, 0f, rot);
                var c = row.col; c.a = rowA; row.name.color = c; row.amount.color = c;
                slipBottom = cy - h * 0.5f;
                y -= h;
            }
            // the slip under them, and the glint across the newest
            float slipA = mode == Mode.Live ? 0.24f : mode == Mode.Banking ? 0.24f * Mathf.Clamp01(1f - modeT / 0.15f) : 0.24f * Mathf.Clamp01(1f - modeT / 0.12f);
            if (lineCount == 0) slipA = 0f;
            cSlip.color = new Color(Paper.r, Paper.g, Paper.b, slipA * lineAlpha);
            cSlip.rectTransform.anchoredPosition = new Vector2(Right + 18f, slipTop + 4f);
            cSlip.rectTransform.sizeDelta = new Vector2(CW + 40f, Mathf.Max(0f, slipTop - slipBottom) + 14f);
            var top = cRows[0];
            float g = lineCount > 0 && mode == Mode.Live ? (top.age - 0.07f) / 0.36f : -1f;
            if (g >= 0f && g <= 1f)
            {
                cGlint.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(Right - CW - 30f, Right + 20f, g), top.rt.anchoredPosition.y);
                cGlint.color = new Color(0.85f, 0.94f, 1f, 0.55f * Mathf.Sin(g * Mathf.PI));
            }
            else if (cGlint.color.a > 0f) cGlint.color = new Color(0.85f, 0.94f, 1f, 0f);
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
