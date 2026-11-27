using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// The medical bill as a receipt, while the hero is down: top right, where the combo lives, in the same family
    /// (the same lines, name left and amount right, the same edges and glint), on a dark slip of paper with a torn
    /// edge. Each line prints the moment its injury happens (Receipt.Printed through the game): the name runs out
    /// left to right like a thermal printer's, the amount stamps in after it, a glint of frost crosses it. The bad
    /// ones land harder: a red flash on the line, the slip jolts, the total punches. The total ticks up at the
    /// bottom. A long one keeps its newest lines and scrolls the oldest up under the header; it never grows down
    /// past the top half of the screen, where the body lies. When the rider gets up the slip tears off and flies
    /// into the corner total. On the run's last fall it carries to the top of the end screen as the run's whole
    /// itemized receipt (the same item from different falls on one line, x n) with its TOTAL, and if that is
    /// longer than there is room for it scrolls slowly through it and back.
    /// </summary>
    public partial class Hud
    {
        const float RW = 640f, RHead = 58f, RRow = 46f, RTotal = 82f, REdge = 14f, RPadX = 24f;
        const int RFont = 36;

        /// <summary>One line on a slip: a name on the left, an amount on the right (the combo uses them too).</summary>
        class SlipRow
        {
            public RectTransform rt; public Text name, amount;
            public string full; public int chars = -1;
            public float age = 9f, fit = 1f, y;
            public int grade; public bool service, typed;
            public Color col;
        }

        enum RMode { Off, Live, Tear, End }
        RMode rmode;
        RectTransform rcpt, rRows;
        CanvasGroup rGroup;
        Image rPaper, rEdge, rEdgeTop, rRuleHead, rRuleTotal, rFlash, rGlint;
        Text rHeader, rTotalLabel, rTotal;
        readonly List<SlipRow> rLines = new List<SlipRow>();
        readonly List<SlipRow> rPool = new List<SlipRow>();
        struct Pending { public string name; public int cost, grade, count; public bool service; }
        readonly Queue<Pending> rQueue = new Queue<Pending>();
        float rPrintClock, rT, rShake, rRowsH, rScroll, rTotalShown, rTotalPunch, rTotalTarget, rTotalScale = 1f, rHold, rMoveT = -1f;
        int rTotalShownInt = -1, rMaxRows = 7, rFlashRow = -1, rScrollDir, rLaidCount = -1;
        float rLaidScroll = -1f, rTotalWide;
        Vector2 rPos, rFrom;
        static Sprite panelSprite, zigSprite, dashSprite;

        // a hit taken while still riding: one line on a small slip, over the corner's total, that folds into it
        const float RideW = 540f, RideH = 56f, RideHold = 1.35f, RideFold = 0.3f;
        RectTransform rideRt; Image rideSlip, rideGlint; SlipRow rideRow;
        float rideT = -1f, rideHeld;

        static readonly Color Paper = new Color(0.02f, 0.04f, 0.1f, 0.68f);
        static readonly Color Ice = new Color(0.66f, 0.8f, 0.96f);
        static readonly Color ServiceCol = new Color(0.78f, 0.86f, 0.96f);

        static Color GradeCol(int g, bool service) =>
            service ? ServiceCol : g <= 2 ? Color.white : g == 3 ? new Color(1f, 0.8f, 0.74f) : g == 4 ? new Color(1f, 0.56f, 0.46f) : new Color(1f, 0.36f, 0.3f);

        void BuildReceipt(Sprite soft)
        {
            if (panelSprite == null) panelSprite = MakePanel();
            if (zigSprite == null) zigSprite = MakeZig();
            if (dashSprite == null) dashSprite = MakeDash();
            var one = new Vector2(1f, 1f);
            rcpt = new GameObject("Receipt", typeof(RectTransform)).GetComponent<RectTransform>();
            rcpt.SetParent(pts, false);
            Brand.Place(rcpt, one, new Vector2(Right, Top), new Vector2(RW, 400f), one);
            rGroup = rcpt.gameObject.AddComponent<CanvasGroup>();
            rGroup.alpha = 0f; rGroup.blocksRaycasts = false; rGroup.interactable = false;

            rPaper = Brand.Img("Paper", rcpt, Paper);
            rPaper.sprite = panelSprite; rPaper.type = Image.Type.Sliced;
            Brand.Place(rPaper.rectTransform, one, Vector2.zero, new Vector2(RW, 200f), one);
            rEdge = Brand.Img("Torn", rcpt, Paper);
            rEdge.sprite = zigSprite; rEdge.type = Image.Type.Tiled;
            Brand.Place(rEdge.rectTransform, one, Vector2.zero, new Vector2(RW, REdge), one);
            // the top, torn from the printer as it goes (only while it flies off: Tear)
            rEdgeTop = Brand.Img("TornTop", rcpt, new Color(Paper.r, Paper.g, Paper.b, 0f));
            rEdgeTop.sprite = zigSprite; rEdgeTop.type = Image.Type.Tiled;
            Brand.Place(rEdgeTop.rectTransform, one, new Vector2(0f, 1f), new Vector2(RW, REdge), one);   // (flipped: the teeth up)
            rEdgeTop.rectTransform.localScale = new Vector3(1f, -1f, 1f);

            rHeader = NewText("Header", rcpt, one, new Vector2(-RW * 0.5f, -14f), 26, TextAnchor.UpperCenter);
            rHeader.rectTransform.pivot = new Vector2(0.5f, 1f);
            rHeader.color = Ice; rHeader.text = "MT. VERTIGO  MEDICAL";
            rRuleHead = Rule("Rule", rcpt, -RHead + 6f);
            rRuleTotal = Rule("RuleTotal", rcpt, -RHead);

            rRows = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            rRows.SetParent(rcpt, false);
            Brand.Place(rRows, one, new Vector2(0f, -RHead), new Vector2(RW, 0f), one);
            rRows.gameObject.AddComponent<RectMask2D>();
            rFlash = Brand.Img("Flash", rRows, new Color(1f, 0.15f, 0.1f, 0f));
            rFlash.sprite = panelSprite; rFlash.type = Image.Type.Sliced;
            Brand.Place(rFlash.rectTransform, one, Vector2.zero, new Vector2(RW - 12f, RRow - 4f), new Vector2(1f, 0.5f));
            rGlint = Brand.Img("Glint", rRows, new Color(0.85f, 0.94f, 1f, 0f));
            rGlint.sprite = soft;
            Brand.Place(rGlint.rectTransform, one, Vector2.zero, new Vector2(22f, RRow * 1.8f), new Vector2(0.5f, 0.5f));
            rGlint.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);

            // the riding hit's slip: over the corner's total (where it folds to), above the combo
            rideRt = new GameObject("RideHit", typeof(RectTransform)).GetComponent<RectTransform>();
            rideRt.SetParent(pts, false);
            Brand.Place(rideRt, one, new Vector2(-24f, -214f), new Vector2(RideW, RideH), new Vector2(1f, 0.5f));
            rideRt.anchoredPosition = new Vector2(-24f, -214f - RideH * 0.5f);
            rideSlip = Brand.Img("Slip", rideRt, Paper);
            rideSlip.sprite = panelSprite; rideSlip.type = Image.Type.Sliced;
            Brand.Stretch(rideSlip.rectTransform);
            rideRow = NewRow(rideRt, RideW - 36f, 34, false);
            rideRow.rt.anchorMin = rideRow.rt.anchorMax = new Vector2(1f, 0.5f);
            rideRow.rt.anchoredPosition = new Vector2(-18f, 0f);
            rideGlint = Brand.Img("Glint", rideRt, new Color(0.85f, 0.94f, 1f, 0f));
            rideGlint.sprite = soft;
            Brand.Place(rideGlint.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(22f, RideH * 1.5f), new Vector2(0.5f, 0.5f));
            rideGlint.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);
            rideRt.gameObject.SetActive(false);

            rTotalLabel = NewText("TotalLabel", rcpt, one, Vector2.zero, 34, TextAnchor.MiddleLeft);
            rTotalLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
            rTotalLabel.color = Ice; rTotalLabel.text = "TOTAL";
            rTotal = NewText("Total", rcpt, one, Vector2.zero, 60, TextAnchor.MiddleRight);
            rTotal.rectTransform.pivot = new Vector2(1f, 0.5f);
            rTotal.fontStyle = FontStyle.BoldAndItalic; rTotal.text = "";
            Edge(rTotal, 3f); Edge(rHeader, 1.5f); Edge(rTotalLabel, 1.5f);
        }

        Image Rule(string name, Transform parent, float y)
        {
            var r = Brand.Img(name, parent, new Color(Ice.r, Ice.g, Ice.b, 0.5f));
            r.sprite = dashSprite; r.type = Image.Type.Tiled;
            Brand.Place(r.rectTransform, new Vector2(1f, 1f), new Vector2(-RPadX, y), new Vector2(RW - RPadX * 2f, 3f), new Vector2(1f, 0.5f));
            return r;
        }

        /// <summary>A slip row: a name at the left of `w`, the amount at its right (pivot at the row's right middle).</summary>
        SlipRow NewRow(Transform parent, float w, int size, bool edge = true)
        {
            var row = new SlipRow();
            row.rt = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.rt.SetParent(parent, false);
            Brand.Place(row.rt, new Vector2(1f, 1f), Vector2.zero, new Vector2(w, size * 1.25f), new Vector2(1f, 0.5f));
            row.name = NewText("Name", row.rt, new Vector2(0f, 0.5f), Vector2.zero, size, TextAnchor.MiddleLeft);
            row.name.rectTransform.pivot = new Vector2(0f, 0.5f);
            row.amount = NewText("Amount", row.rt, new Vector2(1f, 0.5f), Vector2.zero, size, TextAnchor.MiddleRight);
            row.amount.rectTransform.pivot = new Vector2(1f, 0.5f);
            row.name.text = row.amount.text = "";
            if (edge) { Edge(row.name, 2f); Edge(row.amount, 2f); }   // (over the snow; the receipt's rows sit on its slip)
            return row;
        }

        /// <summary>Set a row's words: the name fitted into what the amount leaves (shrunk, never below 0.6).</summary>
        static void SetRow(SlipRow row, string name, string amount, Color c, float w, bool typed)
        {
            row.full = name; row.col = c;
            row.amount.text = amount;
            row.name.text = name;
            float room = w - Wide(row.amount) - 26f;
            float nw = Wide(row.name);
            row.fit = nw > room && nw > 0f ? Mathf.Max(0.6f, room / nw) : 1f;
            row.name.rectTransform.localScale = new Vector3(row.fit, row.fit, 1f);
            row.name.color = c; row.amount.color = c;
            row.typed = typed;
            row.chars = typed ? 0 : name.Length;
            if (typed) row.name.text = "";
        }

        SlipRow TakeRow()
        {
            SlipRow r;
            if (rPool.Count > 0) { r = rPool[rPool.Count - 1]; rPool.RemoveAt(rPool.Count - 1); r.rt.gameObject.SetActive(true); }
            else r = NewRow(rRows, RW - RPadX * 2f, RFont, false);
            r.rt.localRotation = Quaternion.identity; r.rt.localScale = Vector3.one;
            r.amount.rectTransform.localScale = Vector3.one;
            return r;
        }

        void ClearReceiptRows()
        {
            foreach (var r in rLines) { r.rt.gameObject.SetActive(false); rPool.Add(r); }
            rLines.Clear(); rQueue.Clear();
            rRowsH = 0f; rScroll = 0f; rFlashRow = -1; rLaidCount = -1; rShake = 0f; rTotalShown = 0f; rTotalTarget = 0f; rTotalShownInt = -1; rTotal.text = "";
            rFlash.color = new Color(1f, 0.15f, 0.1f, 0f); rGlint.color = new Color(0.85f, 0.94f, 1f, 0f);
        }

        // ---------------------------------------------------------------- the game's side

        /// <summary>A line on the fall's receipt (the name as the blood setting has it): it prints in turn.</summary>
        public void BillLine(string name, int cost, int grade, bool service)
        {
            if (cost <= 0 || rcpt == null) return;
            if (rmode != RMode.Live)
            {
                // a fresh slip, top right
                ClearReceiptRows();
                rEdgeTop.color = new Color(Paper.r, Paper.g, Paper.b, 0f);
                rmode = RMode.Live; rT = 0f; rMoveT = -1f;
                rMaxRows = 9;
                rPos = new Vector2(Right, Top);
                rcpt.anchoredPosition = rPos; rcpt.localScale = Vector3.one; rcpt.localRotation = Quaternion.identity;
                rHeader.text = "MT. VERTIGO  MEDICAL";
                rPrintClock = 0f;
                billOn = true; billTarget = 0f;
            }
            billTarget += cost;
            rQueue.Enqueue(new Pending { name = name, cost = cost, grade = grade, service = service, count = 1 });
        }

        /// <summary>The rider is up again: the slip tears off and flies into the total in the corner.</summary>
        public void BillBank()
        {
            if (!billOn) return;
            billOn = false;
            while (rQueue.Count > 0) PrintRow(rQueue.Dequeue(), true);
            rmode = RMode.Tear; rT = 0f; rPos = rcpt.anchoredPosition;
            medCounting = true;
        }

        /// <summary>The run is over: the whole run's receipt (items merged across falls), at the top of the end screen.</summary>
        public void BillEnd(List<Receipt.Item> items, int total, bool gore)
        {
            billOn = false;
            if (total <= 0 || items == null || items.Count == 0) { rmode = RMode.Off; ClearReceiptRows(); rGroup.alpha = 0f; return; }
            bool carried = rmode == RMode.Live && rGroup.alpha > 0.05f;
            Vector2 from = rcpt.anchoredPosition;
            float keepH = carried ? rRowsH : 0f;
            ClearReceiptRows();
            rRowsH = keepH;
            rmode = RMode.End; rT = 0f;
            rHeader.text = "MT. VERTIGO  MEDICAL  ·  ITEMIZED";
            foreach (var it in items)
                rQueue.Enqueue(new Pending { name = it.Name(gore) + (it.count > 1 ? "  x" + it.count : ""), cost = it.cost, grade = it.grade, service = it.service, count = it.count });
            // room: from near the top of the screen down to just above the title
            float h = pts.rect.height > 1f ? pts.rect.height : 1920f, w = pts.rect.width > 1f ? pts.rect.width : 1080f;
            float topY = -70f, bottomY = -(h * 0.5f - 350f);
            rMaxRows = Mathf.Clamp(Mathf.FloorToInt((topY - bottomY - RHead - RTotal - REdge) / RRow), 3, 12);
            int vis = Mathf.Min(items.Count, rMaxRows);
            float full = RHead + vis * RRow + RTotal + REdge;
            Vector2 to = new Vector2(-(w - RW) * 0.5f, Mathf.Min(topY, bottomY + full));
            rPos = to;
            if (carried) { rFrom = from; rMoveT = 0f; }
            else { rMoveT = -1f; rcpt.anchoredPosition = to; rGroup.alpha = 0f; }
            rcpt.localScale = Vector3.one; rcpt.localRotation = Quaternion.identity;
            rPrintClock = carried ? 0.35f : 0.2f; rHold = 0f; rScrollDir = 0;
        }

        void PrintRow(Pending p, bool quiet = false)
        {
            var row = TakeRow();
            row.grade = p.grade; row.service = p.service; row.age = quiet ? 9f : 0f;
            SetRow(row, p.name, "$" + p.cost.ToString("N0"), GradeCol(p.grade, p.service), RW - RPadX * 2f, !quiet);
            row.rt.anchoredPosition = new Vector2(-RPadX, -(rLines.Count * RRow + RRow * 0.5f));
            rLines.Add(row);
            rTotalTarget += p.cost;
            if (quiet) return;
            rGlint.transform.SetAsLastSibling();
            bool bad = !p.service && p.grade >= 4;
            if (bad)
            {
                rFlashRow = rLines.Count - 1;
                rShake = Mathf.Max(rShake, 7f + 6f * (p.grade - 4));
                rTotalPunch = Mathf.Max(rTotalPunch, 0.5f + 0.15f * (p.grade - 4));
            }
            else rTotalPunch = Mathf.Max(rTotalPunch, p.cost >= 5000 ? 0.35f : 0.2f);
        }

        // ---------------------------------------------------------------- each frame

        void StepReceipt(float dt, bool texts)
        {
            if (rcpt == null) return;
            if (rmode == RMode.Off)
            {
                if (rGroup.alpha > 0f) rGroup.alpha = Mathf.MoveTowards(rGroup.alpha, 0f, dt * 4f);
                return;
            }
            rT += dt;
            rShake = Mathf.Max(0f, rShake - dt * (12f + rShake * 3f));
            rTotalPunch = Mathf.Max(0f, rTotalPunch - dt * 3f);

            // (a combo just lost is still shaking in this spot: the slip waits for it to fall out of the way)
            bool blocked = rmode == RMode.Live && mode == Mode.Dropping && modeT < 0.5f;
            if (!blocked && rmode != RMode.Tear && (rmode != RMode.End || rMoveT < 0f || rMoveT > 0.45f))
            {
                rPrintClock -= dt;
                if (rQueue.Count > 0 && rPrintClock <= 0f)
                {
                    var p = rQueue.Peek(); rQueue.Dequeue();
                    PrintRow(p);
                    rPrintClock = rmode == RMode.End ? 0.075f : !p.service && p.grade >= 4 ? 0.34f : 0.16f;
                }
            }

            // the rows: typed out, the amount stamped in after
            for (int i = 0; i < rLines.Count; i++)
            {
                var r = rLines[i];
                if (r.age > 1f) continue;
                r.age += dt;
                if (r.typed)
                {
                    int n = Mathf.Clamp(Mathf.CeilToInt(r.full.Length * r.age / 0.13f), 0, r.full.Length);
                    if (n != r.chars) { r.chars = n; r.name.text = r.full.Substring(0, n); }
                }
                float k = Mathf.Clamp01((r.age - 0.1f) / 0.12f);
                float s = 1f + 0.8f * (1f - k) * (1f - k);
                r.amount.rectTransform.localScale = new Vector3(s, s, 1f);
                var ac = r.col; ac.a = k; r.amount.color = ac;
            }

            // grows a line at a time; past its rows, the oldest scroll up under the header
            int count = rLines.Count, vis = Mathf.Min(count, rMaxRows);
            rRowsH = Mathf.MoveTowards(rRowsH, vis * RRow, dt * 520f);
            float bottom = Mathf.Max(0f, (count - rMaxRows) * RRow);
            float target = bottom;
            if (rmode == RMode.End && rQueue.Count == 0 && bottom > 0f)
            {
                // the end screen: hold at the end, scroll back up through it, hold, and down again
                if (rScrollDir == 0) { rHold += dt; if (rHold > 2.4f) { rScrollDir = -1; rHold = 0f; } target = bottom; }
                else if (rScrollDir < 0) { target = 0f; if (rScroll <= 0.5f) { rHold += dt; if (rHold > 1.8f) { rScrollDir = 1; rHold = 0f; } } }
                else { target = bottom; if (rScroll >= bottom - 0.5f) { rScrollDir = 0; rHold = 0f; } }
                rScroll = Mathf.MoveTowards(rScroll, target, dt * (rScrollDir != 0 ? 95f : 400f));
            }
            else rScroll = Mathf.MoveTowards(rScroll, target, dt * Mathf.Max(300f, Mathf.Abs(target - rScroll) * 10f));
            if (rScroll != rLaidScroll || count != rLaidCount)
            {
                // (only when they move: the rows are most of what this canvas has to batch)
                rLaidScroll = rScroll; rLaidCount = count;
                for (int i = 0; i < count; i++)
                {
                    var r = rLines[i];
                    float y = -(i * RRow + RRow * 0.5f) + rScroll;
                    r.rt.anchoredPosition = new Vector2(-RPadX, y);
                }
            }
            float paperH = RHead + rRowsH + RTotal;
            rPaper.rectTransform.sizeDelta = new Vector2(RW, paperH);
            rRows.sizeDelta = new Vector2(RW, rRowsH);
            rRuleTotal.rectTransform.anchoredPosition = new Vector2(-RPadX, -(RHead + rRowsH) - 4f);
            rEdge.rectTransform.anchoredPosition = new Vector2(0f, -paperH + 1f);
            float ty = -(RHead + rRowsH) - RTotal * 0.5f - 4f;
            rTotalLabel.rectTransform.anchoredPosition = new Vector2(-RW + RPadX, ty);

            // the red flash on a bad line, and the frost glint across the newest
            if (rFlashRow >= 0 && rFlashRow < count)
            {
                var r = rLines[rFlashRow];
                float a = 0.62f * Mathf.Clamp01(1f - r.age / 0.5f);
                rFlash.color = new Color(1f, 0.15f, 0.1f, a);
                rFlash.rectTransform.anchoredPosition = new Vector2(-6f, r.rt.anchoredPosition.y);
                if (a <= 0f) rFlashRow = -1;
            }
            else if (rFlash.color.a > 0f) rFlash.color = new Color(1f, 0.15f, 0.1f, 0f);
            if (count > 0)
            {
                var r = rLines[count - 1];
                float u = (r.age - 0.08f) / 0.34f;
                if (u >= 0f && u <= 1f)
                {
                    rGlint.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-RW - 30f, 10f, u), r.rt.anchoredPosition.y);
                    rGlint.color = new Color(0.85f, 0.94f, 1f, 0.55f * Mathf.Sin(u * Mathf.PI));
                }
                else if (rGlint.color.a > 0f) rGlint.color = new Color(0.85f, 0.94f, 1f, 0f);
            }

            // the total, ticking up
            float before = rTotalShown;
            rTotalShown = Approach(rTotalShown, rTotalTarget, dt, 900f);
            if (rTotalShown != before && tickClock <= 0f) { tickClock = 0.06f; BillTick?.Invoke(); }
            int shown = Mathf.RoundToInt(rTotalShown);
            if (shown != rTotalShownInt && (texts || Mathf.Abs(rTotalShown - rTotalTarget) < 0.5f)) { rTotalShownInt = shown; rTotal.text = "$" + shown.ToString("N0"); rTotalWide = Wide(rTotal); }
            int bt = TierOf(rTotalTarget, BillAt);
            Color tc = BillColor[bt];
            rTotal.color = Color.Lerp(tc, Color.white, Mathf.Clamp01(rTotalPunch * 0.9f));
            float tw = rTotalWide, room = RW - RPadX * 2f - 150f;
            float fit = tw > room ? room / tw : 1f;
            rTotalScale = fit * (1f + 0.45f * rTotalPunch);
            rTotal.rectTransform.localScale = new Vector3(rTotalScale, rTotalScale, 1f);
            rTotal.rectTransform.anchoredPosition = new Vector2(-RPadX, ty);

            // where the slip is
            Vector2 jolt = new Vector2(Mathf.Sin(glowT * 83f), Mathf.Cos(glowT * 71f)) * rShake;
            if (rmode == RMode.Live)
            {
                if (!blocked) rGroup.alpha = Mathf.MoveTowards(rGroup.alpha, 1f, dt * 8f);
                rcpt.anchoredPosition = rPos + jolt;
            }
            else if (rmode == RMode.End)
            {
                rGroup.alpha = Mathf.MoveTowards(rGroup.alpha, 1f, dt * 5f);
                if (rMoveT >= 0f)
                {
                    rMoveT += dt;
                    float u = Mathf.Clamp01(rMoveT / 0.6f), e = u * u * (3f - 2f * u);
                    rcpt.anchoredPosition = Vector2.Lerp(rFrom, rPos, e) + jolt;
                    if (u >= 1f) rMoveT = 99f;
                }
                else rcpt.anchoredPosition = rPos + jolt;
            }
            else if (rmode == RMode.Tear)
            {
                // a jerk as it tears, then off to the corner's total, turning as it goes
                var corner = new Vector2(-40f, -226f);
                // the top edge torn ragged, and a slip of paper left behind for a moment where it tore
                rEdgeTop.color = new Color(Paper.r, Paper.g, Paper.b, Paper.a);
                if (rT < 0.12f)
                {
                    rcpt.anchoredPosition = rPos + new Vector2(0f, -16f * Mathf.Sin(Mathf.PI * rT / 0.12f));
                    rcpt.localRotation = Quaternion.Euler(0f, 0f, 2.5f * Mathf.Sin(Mathf.PI * rT / 0.12f));
                }
                else
                {
                    float u = Mathf.Clamp01((rT - 0.12f) / 0.46f), e = u * u * (3f - 2f * u);
                    e *= e;
                    rcpt.anchoredPosition = Vector2.Lerp(rPos, corner, e) + new Vector2(0f, Mathf.Sin(u * Mathf.PI) * 50f);
                    float s = Mathf.Lerp(1f, 0.1f, e);
                    rcpt.localScale = new Vector3(s, s, 1f);
                    rcpt.localRotation = Quaternion.Euler(0f, 0f, -16f * e);
                    rGroup.alpha = u < 0.75f ? 1f : (1f - u) / 0.25f;
                    if (u >= 1f)
                    {
                        rmode = RMode.Off; rGroup.alpha = 0f; ClearReceiptRows(); rEdgeTop.color = new Color(Paper.r, Paper.g, Paper.b, 0f);
                        rcpt.localScale = Vector3.one; rcpt.localRotation = Quaternion.identity;
                        medPunch = 1f; medCounting = true;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- a hit taken while riding

        /// <summary>A hit taken while still riding (a sign, a banner's stake): one line pops up over the corner's
        /// total, holds a moment, and folds into it. Another while it shows takes its place (both go in the total).</summary>
        public void RideHit(string name, int cost, int grade)
        {
            if (rideRt == null || cost <= 0) return;
            rideHeld += cost;
            rideT = 0f;
            rideRt.gameObject.SetActive(true);
            SetRow(rideRow, name, "$" + cost.ToString("N0"), GradeCol(grade, false), RideW - 36f, true);
            rideRow.age = 0f;
            rideRt.localScale = Vector3.one;
        }

        void StepRideHit(float dt)
        {
            if (rideT < 0f) return;
            rideT += dt;
            var r = rideRow;
            r.age += dt;
            int n = Mathf.Clamp(Mathf.CeilToInt(r.full.Length * r.age / 0.12f), 0, r.full.Length);
            if (n != r.chars) { r.chars = n; r.name.text = r.full.Substring(0, n); }
            float k = Mathf.Clamp01((r.age - 0.09f) / 0.12f);
            float s = 1f + 0.8f * (1f - k) * (1f - k);
            r.amount.rectTransform.localScale = new Vector3(s, s, 1f);
            var ac = r.col; ac.a = k; r.amount.color = ac;
            float a = 1f; Vector3 sc = Vector3.one; float x = 0f;
            if (rideT < 0.14f) { float u = rideT / 0.14f, o = (1f - u) * (1f - u); sc = Vector3.one * (1f + 0.25f * o); x = 30f * o; a = u; }
            else if (rideT > RideHold)
            {
                // folds up into the total (which it sits on), a strip closing to a line
                float u = Mathf.Clamp01((rideT - RideHold) / RideFold), e = u * u;
                sc = new Vector3(Mathf.Lerp(1f, 0.35f, e), Mathf.Lerp(1f, 0.05f, e), 1f); a = 1f - e;
                if (u >= 1f)
                {
                    rideT = -1f; rideHeld = 0f; rideRt.gameObject.SetActive(false);
                    medPunch = 1f; medCounting = true;
                    return;
                }
            }
            rideRt.localScale = sc;
            rideRt.anchoredPosition = new Vector2(-24f + x, -214f - RideH * 0.5f);
            rideSlip.color = new Color(Paper.r, Paper.g, Paper.b, Paper.a * a);
            var nc = r.col; nc.a = a; r.name.color = nc;
            if (rideT > 0.14f) { var c2 = r.col; c2.a = a; r.amount.color = c2; }
            float g = (r.age - 0.06f) / 0.34f;
            if (g >= 0f && g <= 1f)
            {
                rideGlint.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-RideW - 20f, 10f, g), 0f);
                rideGlint.color = new Color(0.85f, 0.94f, 1f, 0.55f * Mathf.Sin(g * Mathf.PI));
            }
            else if (rideGlint.color.a > 0f) rideGlint.color = new Color(0.85f, 0.94f, 1f, 0f);
        }

        /// <summary>The corner's total is under the riding hit's slip just now (it hides while that shows).</summary>
        bool RideHitShowing => rideT >= 0f;

        void ClearReceipt()
        {
            if (rcpt == null) return;
            rideT = -1f; rideHeld = 0f; if (rideRt != null) rideRt.gameObject.SetActive(false);
            rmode = RMode.Off; ClearReceiptRows(); rGroup.alpha = 0f;
            rcpt.localScale = Vector3.one; rcpt.localRotation = Quaternion.identity;
            rEdgeTop.color = new Color(Paper.r, Paper.g, Paper.b, 0f);
        }

        /// <summary>The receipt is on its way to the corner (the corner's total waits for it).</summary>
        bool ReceiptFlying => rmode == RMode.Tear;

        // ---------------------------------------------------------------- the slip's sprites

        /// <summary>A rounded panel, nine-sliced.</summary>
        static Sprite MakePanel()
        {
            const int px = 32, r = 10;
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false);
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, px - r), cy = Mathf.Clamp(y + 0.5f, r, px - r);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        /// <summary>The torn edge: a row of teeth hanging down, tiled along the bottom of the slip.</summary>
        static Sprite MakeZig()
        {
            const int w = 28, h = 14;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // a tooth: full at the top (y = h), down to a point at the middle of the tile
                    float fromMid = Mathf.Abs(x + 0.5f - w * 0.5f) / (w * 0.5f);   // 0 at the point, 1 at the tile's edges
                    float depth = (h - y - 0.5f) / h;                                  // 0 at the top, 1 at the bottom
                    float a = Mathf.Clamp01((1.08f - fromMid - depth) * h * 0.9f + 0.5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), 100f);
        }

        /// <summary>A dashed rule.</summary>
        static Sprite MakeDash()
        {
            const int w = 14, h = 2;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, new Color(1f, 1f, 1f, x < 8 ? 1f : 0f));
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
