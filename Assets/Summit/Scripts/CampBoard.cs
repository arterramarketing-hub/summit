using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// (campstairs) The daily missions board, by the gate under MT. VERTIGO: the only one now (the lodge's, inside its
    /// front glass, is gone). A proper signboard: two glulam posts under a small gabled roof of dark steel with snow on
    /// it, a warm strip light under its front eave; a charcoal face in a thin black steel frame, and on it, in the
    /// game's clean type (light, widely spaced capitals, one gold accent; not ultra-light: at a distance its strokes
    /// vanish): DAILY MISSIONS, when the next ones come, and today's three, each with its reward, a hairline bar of how
    /// far along it is and the count (done: DONE, in gold).
    /// The trail map is on its back, for the way down from the camp. Tap it, or stand at it, for the missions card.
    /// The writing is live (a world-space canvas): Redress keeps it up to date.
    /// </summary>
    public partial class Camp
    {
        /// <summary>Where the board stands (dx from the run's centre, z) and the way its face looks (degrees): beside the stairs' foot, turned to the gate.</summary>
        public const float BoardX = -5.2f, BoardZ = -44.6f, BoardYaw = 27f;   // (off the way from the stairs' foot to the bench by the drop)
        static readonly Color BoardGold = new Color(0.788f, 0.635f, 0.29f);   // #C9A24A
        static readonly Color BoardInk = new Color(0.94f, 0.93f, 0.9f), BoardDim = new Color(0.94f, 0.93f, 0.9f, 0.55f);

        Text boardResets;
        readonly Text[] cardIndex = new Text[3], cardTitle = new Text[3], cardReward = new Text[3], cardCount = new Text[3];
        readonly RectTransform[] cardFill = new RectTransform[3];
        readonly Image[] cardFillImg = new Image[3];

        static Font boardFont, boardThin;
        static Font BoardFont(bool thin)
        {
            if (thin && boardThin == null)
                try { boardThin = Font.CreateDynamicFontFromOSFont(new[] { "Avenir Next Regular", "AvenirNext-Regular", "Avenir Next", "Helvetica Neue", "Arial" }, 64); } catch { }
            if (!thin && boardFont == null)
                try { boardFont = Font.CreateDynamicFontFromOSFont(new[] { "Avenir Next Medium", "AvenirNext-Medium", "Avenir Next", "Helvetica Neue", "Arial" }, 64); } catch { }
            var f = thin ? boardThin : boardFont;
            if (f == null) f = SettingsSheet.Face;
            return f;
        }

        void MissionsBoard()
        {
            var b = Frame(BoardX, BoardZ, BoardYaw); b.name = "MissionsBoard";
            // (its posts on the lower of the snow under them: on the bank's foot the ground falls toward the gate)
            float lo = Mathf.Min(Slope.DrawnHeight(b.TransformPoint(Vector3.left * 1.12f).x, b.TransformPoint(Vector3.left * 1.12f).z),
                                 Slope.DrawnHeight(b.TransformPoint(Vector3.right * 1.12f).x, b.TransformPoint(Vector3.right * 1.12f).z));
            b.position = new Vector3(b.position.x, Mathf.Min(b.position.y, lo + 0.05f), b.position.z);
            var steel = Resources.Load<Material>("Camp/LodgeTex/Materials/Steel"); if (steel == null) steel = metal;
            var glulam = Resources.Load<Material>("Camp/LodgeTex/Materials/Glulam"); if (glulam == null) glulam = wood;
            var clad = Resources.Load<Material>("Camp/LodgeTex/Materials/Cladding"); if (clad == null) clad = darkWood;
            var face = M(new Color(0.075f, 0.08f, 0.09f), 0.3f);
            var gold = M(BoardGold, 0.55f); gold.SetFloat("_Metallic", 0.6f);

            // the posts, a header beam, the roof (two dark steel planes, snow on them) and its ridge
            for (int s = -1; s <= 1; s += 2)
            {
                P(PrimitiveType.Cube, b, new Vector3(s * 1.12f, 1.0f, 0f), new Vector3(0.14f, 3.0f, 0.14f), Vector3.zero, glulam);
                Block(b, new Vector3(s * 1.12f, 0f, 0f), new Vector2(0.1f, 0.1f));
            }
            P(PrimitiveType.Cube, b, new Vector3(0f, 2.44f, 0f), new Vector3(2.5f, 0.14f, 0.16f), Vector3.zero, glulam);
            const float ridge = 2.78f, eave = 2.5f, deep = 0.44f;
            float pitch = Mathf.Atan2(ridge - eave, deep) * Mathf.Rad2Deg, plane = Mathf.Sqrt((ridge - eave) * (ridge - eave) + deep * deep);
            for (int s = -1; s <= 1; s += 2)
            {
                var n = new Vector3(0f, Mathf.Cos(pitch * Mathf.Deg2Rad), s * Mathf.Sin(pitch * Mathf.Deg2Rad));
                var mid = new Vector3(0f, (ridge + eave) * 0.5f, s * deep * 0.5f);
                P(PrimitiveType.Cube, b, mid, new Vector3(2.7f, 0.035f, plane + 0.06f), new Vector3(s * pitch, 0f, 0f), roof);
                P(PrimitiveType.Cube, b, mid + n * 0.04f, new Vector3(2.62f, 0.05f, plane - 0.02f), new Vector3(s * pitch, 0f, 0f), snowCap);
            }
            P(PrimitiveType.Cube, b, new Vector3(0f, ridge - 0.05f, 0f), new Vector3(2.72f, 0.09f, 0.09f), Vector3.zero, glulam);
            // a warm strip light under the front eave (and a lamp in it at dusk)
            P(PrimitiveType.Cube, b, new Vector3(0f, 2.36f, 0.09f), new Vector3(2.0f, 0.025f, 0.03f), Vector3.zero, bulb, false);
            Lamp(b, new Vector3(0f, 2.2f, 0.7f), 4.5f, 0.9f);
            // the board: a backing of dark-stained boards, the charcoal face, a thin black steel frame round it
            P(PrimitiveType.Cube, b, new Vector3(0f, 1.56f, 0f), new Vector3(2.12f, 1.46f, 0.06f), Vector3.zero, clad);
            P(PrimitiveType.Cube, b, new Vector3(0f, 1.56f, 0.035f), new Vector3(2.0f, 1.34f, 0.012f), Vector3.zero, face);
            foreach (var (c, sz) in new[] { (new Vector3(0f, 2.24f, 0.045f), new Vector3(2.04f, 0.025f, 0.03f)), (new Vector3(0f, 0.88f, 0.045f), new Vector3(2.04f, 0.025f, 0.03f)),
                                            (new Vector3(-1.01f, 1.56f, 0.045f), new Vector3(0.025f, 1.38f, 0.03f)), (new Vector3(1.01f, 1.56f, 0.045f), new Vector3(0.025f, 1.38f, 0.03f)) })
                P(PrimitiveType.Cube, b, c, sz, Vector3.zero, steel);
            // the trail map on its back
            P(PrimitiveType.Quad, b, new Vector3(0f, 1.6f, -0.034f), new Vector3(1.9f, 1.07f, 1f), Vector3.zero, Sign("Signs/TrailMap"));
            Block(b, Vector3.zero, new Vector2(1.06f, 0.06f), null, false, 0f, 0f, 0.75f, 2.4f);   // (the board, at head height)

            BoardWriting(b, gold);
            Stand(b, Station.Missions, new Vector3(0f, 0f, 1.7f), 1.9f);
        }

        /// <summary>The board's face: a world-space canvas, a millimetre a unit, 1960 x 1300, its middle at the face's.</summary>
        void BoardWriting(Transform b, Material gold)
        {
            var go = new GameObject("Writing", typeof(RectTransform));
            go.transform.SetParent(b, false);
            go.transform.localPosition = new Vector3(0f, 1.56f, 0.043f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // (a canvas faces its -z: the board's face looks +z)
            go.transform.localScale = Vector3.one * 0.001f;
            var cv = go.AddComponent<Canvas>(); cv.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(1960f, 1300f);
            var root = (RectTransform)go.transform;

            BoardTracked(root, "DAILY MISSIONS", 84, 0.32f, BoardInk, new Vector2(-880f, 535f), true);
            boardResets = BoardLabel(root, "", 34, BoardDim, new Vector2(880f, 535f), new Vector2(700f, 56f), TextAnchor.MiddleRight, false);
            BoardRule(root, BoardGold, new Vector2(0f, 468f), new Vector2(1760f, 3f));
            for (int i = 0; i < 3; i++)
            {
                float y = 318f - i * 345f;
                cardIndex[i] = BoardLabel(root, "0" + (i + 1), 36, BoardGold, new Vector2(-880f, y + 58f), new Vector2(90f, 60f), TextAnchor.MiddleLeft, false);
                cardTitle[i] = BoardLabel(root, "", 66, BoardInk, new Vector2(-780f, y + 100f), new Vector2(1240f, 150f), TextAnchor.UpperLeft, true);
                cardReward[i] = BoardLabel(root, "", 64, BoardGold, new Vector2(880f, y + 58f), new Vector2(320f, 80f), TextAnchor.MiddleRight, false);
                Rect0(root, y);
                cardFillImg[i] = BoardRule(root, BoardGold, new Vector2(-880f, y - 92f), new Vector2(0f, 8f));
                cardFill[i] = cardFillImg[i].rectTransform; cardFill[i].pivot = new Vector2(0f, 0.5f); cardFill[i].anchoredPosition = new Vector2(-880f, y - 92f);
                cardCount[i] = BoardLabel(root, "", 38, BoardDim, new Vector2(880f, y - 40f), new Vector2(620f, 56f), TextAnchor.MiddleRight, false);
                if (i < 2) BoardRule(root, new Color(1f, 1f, 1f, 0.07f), new Vector2(0f, y - 172f), new Vector2(1760f, 2f));
            }
            BoardRefresh();
        }

        void Rect0(RectTransform root, float y) => BoardRule(root, new Color(1f, 1f, 1f, 0.16f), new Vector2(0f, y - 92f), new Vector2(1760f, 8f));

        /// <summary>Today's missions on the board, as they stand.</summary>
        void BoardRefresh()
        {
            if (boardResets == null) return;
            boardResets.text = "NEW IN " + Missions.ResetsInText.ToUpperInvariant();
            for (int i = 0; i < 3; i++)
            {
                if (cardTitle[i] == null) continue;
                bool have = i < Missions.Today.Count;
                var m = have ? Missions.Today[i] : null;
                cardTitle[i].text = have ? Missions.TitleFor(m) : "";
                cardTitle[i].color = have && m.done ? BoardDim : BoardInk;
                cardReward[i].text = have ? "+" + m.reward : "";
                cardCount[i].text = !have ? "" : m.done ? "DONE" : m.Count.ToUpperInvariant();
                cardCount[i].color = have && m.done ? BoardGold : BoardDim;
                cardFill[i].sizeDelta = new Vector2(1760f * (have ? (m.done ? 1f : m.Fraction) : 0f), 8f);
            }
        }

        // ---- the writing's pieces

        Text BoardLabel(RectTransform parent, string s, int size, Color c, Vector2 at, Vector2 box, TextAnchor anchor, bool wrap, bool thin = false)
        {
            var g = new GameObject("Text", typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var t = g.AddComponent<Text>();
            t.font = BoardFont(thin); t.fontSize = size; t.color = c; t.alignment = anchor; t.text = s;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 0.95f; t.raycastTarget = false;
            var r = t.rectTransform;
            // (placed by the edge it reads from: a left-aligned label's left edge at `at`, a right-aligned one's right)
            float px = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft ? 0f
                     : anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight ? 1f : 0.5f;
            float py = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.UpperRight ? 1f
                     : anchor == TextAnchor.LowerLeft || anchor == TextAnchor.LowerCenter || anchor == TextAnchor.LowerRight ? 0f : 0.5f;
            r.pivot = new Vector2(px, py); r.sizeDelta = box; r.anchoredPosition = at;
            return t;
        }

        Image BoardRule(RectTransform parent, Color c, Vector2 at, Vector2 size)
        {
            var g = new GameObject("Rule", typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var im = g.AddComponent<Image>(); im.color = c; im.raycastTarget = false;
            im.rectTransform.sizeDelta = size; im.rectTransform.anchoredPosition = at;
            return im;
        }

        /// <summary>Capitals spaced out properly: one label a letter, each placed by the font's own advance plus `track` of the size.</summary>
        void BoardTracked(RectTransform parent, string s, int size, float track, Color c, Vector2 at, bool thin, bool right = false)
        {
            var f = BoardFont(thin);
            f.RequestCharactersInTexture(s, size);
            var adv = new float[s.Length]; float w = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                adv[i] = f.GetCharacterInfo(s[i], out var ci, size) ? ci.advance : size * 0.62f;
                w += adv[i] + (i < s.Length - 1 ? size * track : 0f);
            }
            float x = right ? at.x - w : at.x;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != ' ') BoardLabel(parent, s[i].ToString(), size, c, new Vector2(x, at.y), new Vector2(size * 1.2f, size * 1.4f), TextAnchor.MiddleLeft, false, thin);
                x += adv[i] + size * track;
            }
        }
    }
}
