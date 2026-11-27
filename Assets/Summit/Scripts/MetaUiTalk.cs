using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// Talking to Alyssa at the fire (CampTalk has her lines; Camp opens it from TALK). A card at the bottom of the
    /// screen, in the title's icy palette: deep navy glass, a thin line of ice along its top, her name in pale ice
    /// blue, slanted like the VERTIGO lettering, and her words typing on in calm white. Tap anywhere to finish a line or
    /// go on to the next; when she asks something, two or three replies come up as frosted buttons to pick from. The
    /// card is laid out 1000 wide and shrinks to fit a narrower phone, as the panels do; the camp's own buttons hide
    /// while it's up. These are the only words the camp shows of its own (a conversation the player chose to have).
    /// </summary>
    public partial class MetaUi
    {
        static readonly Color TalkGlass = new Color(0.035f, 0.065f, 0.13f, 0.88f), Ice = new Color(0.72f, 0.88f, 1f),
                              IceLine = new Color(0.62f, 0.84f, 1f, 0.95f), Frost = new Color(0.75f, 0.88f, 1f, 0.13f);
        const float TalkW = 1000f, TalkPad = 48f, ReplyH = 104f, ReplyGap = 16f, TypeRate = 48f;

        RectTransform talkRoot, talkCard, talkMore;
        Text talkName, talkBody;
        readonly List<RectTransform> talkReplies = new List<RectTransform>();
        readonly List<Text> talkReplyText = new List<Text>();
        CampTalk.Line[] talk;
        int talkAt;
        float talkShown, talkT;

        public bool TalkOpen => talkRoot != null && talkRoot.gameObject.activeSelf;
        /// <summary>The talk ended with a stick in your hand: roast one (Camp starts it).</summary>
        public bool TalkRoast { get; private set; }
        /// <summary>The line she's on (test tools).</summary>
        public int TalkLine => talkAt;
        public int TalkRepliesShown { get { int n = 0; foreach (var r in talkReplies) if (r.gameObject.activeSelf) n++; return n; } }

        void BuildTalk()
        {
            talkRoot = New("Talk", safe); Stretch(talkRoot);
            var card = Img("TalkCard", talkRoot, TalkGlass, round);
            talkCard = card.rectTransform;
            Place(talkCard, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(TalkW, 400f), new Vector2(0.5f, 0f));
            // the ice along the top edge, and a fainter one under the name
            var edge = Img("TalkIce", talkCard, IceLine, round);
            Place(edge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(TalkW - 70f, 6f), new Vector2(0.5f, 1f));
            talkName = Label(talkCard, new Vector2(0f, 1f), new Vector2(TalkPad, -34f), new Vector2(600f, 64f), 50, TextAnchor.UpperLeft);
            talkName.fontStyle = FontStyle.BoldAndItalic;   // slanted, as the VERTIGO letters are
            talkName.color = Ice;
            var sh = talkName.GetComponent<Shadow>(); sh.effectColor = new Color(0.1f, 0.3f, 0.6f, 0.8f); sh.effectDistance = new Vector2(3f, -3f);
            var under = Img("TalkRule", talkCard, new Color(0.62f, 0.84f, 1f, 0.3f), null);
            Place(under.rectTransform, new Vector2(0f, 1f), new Vector2(TalkPad, -104f), new Vector2(180f, 3f), new Vector2(0f, 1f));
            talkBody = Label(talkCard, new Vector2(0f, 1f), new Vector2(TalkPad, -128f), new Vector2(TalkW - 2f * TalkPad, 200f), 44, TextAnchor.UpperLeft);
            talkBody.fontStyle = FontStyle.Normal;   // calm: plain weight for her words
            talkBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            talkBody.lineSpacing = 1.12f;
            talkBody.color = new Color(0.96f, 0.98f, 1f);
            // the "go on" mark: a small diamond of ice, breathing, bottom right
            var more = Img("TalkMore", talkCard, Ice, null);
            talkMore = more.rectTransform;
            Place(talkMore, new Vector2(1f, 0f), new Vector2(-TalkPad - 6f, 34f), new Vector2(20f, 20f), new Vector2(0.5f, 0.5f));
            talkMore.localRotation = Quaternion.Euler(0f, 0f, 45f);
            for (int i = 0; i < 3; i++)
            {
                var b = Img("TalkReply" + i, talkCard, Frost, round);
                var bar = Img("Ice", b.transform, IceLine, null);
                Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(6f, ReplyH - 40f), new Vector2(0f, 0.5f));
                var tx = Label(b.transform, new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(TalkW - 2f * TalkPad - 70f, ReplyH - 10f), 40, TextAnchor.MiddleLeft);
                tx.horizontalOverflow = HorizontalWrapMode.Wrap;
                tx.resizeTextForBestFit = true; tx.resizeTextMinSize = 28; tx.resizeTextMaxSize = 40;
                talkReplies.Add(b.rectTransform); talkReplyText.Add(tx);
                buttons.Add(new KeyValuePair<RectTransform, string>(b.rectTransform, "talk" + i));
            }
            talkRoot.gameObject.SetActive(false);
        }

        /// <summary>Open a conversation at its first line.</summary>
        public void ShowTalk(CampTalk.Line[] conversation)
        {
            if (talkRoot == null) BuildTalk();
            if (ClosetOpen) EndCloset(false);
            talk = conversation; TalkRoast = false;
            hubRoot.gameObject.SetActive(false);   // the camp's chips and prompt make way
            talkRoot.gameObject.SetActive(true);
            talkName.text = Alyssa.Name.ToUpperInvariant();
            // laid out 1000 wide; on a narrower screen the whole card shrinks to fit
            float room = ((RectTransform)safe).rect.width - 40f;
            talkCard.localScale = Vector3.one * (room > 1f && room < TalkW ? room / TalkW : 1f);
            GoTo(0);
        }

        void GoTo(int i)
        {
            if (i < 0 || talk == null || i >= talk.Length) { TalkRoast = i == CampTalk.Roast; EndTalk(); return; }
            talkAt = i; talkShown = 0f; talkT = 0f;
            Layout();
        }

        /// <summary>
        /// One frame of the talk. tapped: a tap anywhere but a reply (finishes the line, or goes on); button: a reply
        /// touched this frame ("talk0".."talk2"). Returns false once the talk is over.
        /// </summary>
        public bool TalkStep(float dt, bool tapped, string button)
        {
            if (!TalkOpen) return false;
            var line = talk[talkAt];
            talkT += dt;
            bool done = talkShown >= line.text.Length;
            if (!done) talkShown = Mathf.Min(line.text.Length, talkShown + dt * TypeRate);
            if (button != null && button.StartsWith("talk") && done && line.replies != null && line.replies.Length > 0)
            {
                int k = button[4] - '0';
                if (k >= 0 && k < line.replies.Length) { Clicked?.Invoke(); GoTo(line.replies[k].next); return TalkOpen; }
            }
            if (tapped)
            {
                if (!done) talkShown = line.text.Length;   // the rest of the line at once
                else if (line.replies == null || line.replies.Length == 0) GoTo(CampTalk.After(talk, talkAt));
            }
            if (!TalkOpen) return false;
            Show();
            return true;
        }

        /// <summary>Test tools and the camp: pick a reply as if it were tapped (the line typed out first).</summary>
        public void TalkReply(int k) { if (!TalkOpen) return; talkShown = talk[talkAt].text.Length; TalkStep(0f, false, "talk" + k); }

        public void EndTalk()
        {
            if (talkRoot == null || !talkRoot.gameObject.activeSelf) return;
            talkRoot.gameObject.SetActive(false);
            if (Current == Mode.Hub) hubRoot.gameObject.SetActive(true);
        }

        /// <summary>Size the card for this line: her words, then a button for each reply.</summary>
        void Layout()
        {
            var line = talk[talkAt];
            talkBody.text = line.text;
            float textH = Mathf.Max(56f, talkBody.preferredHeight);
            talkBody.rectTransform.sizeDelta = new Vector2(TalkW - 2f * TalkPad, textH);
            int n = line.replies != null ? line.replies.Length : 0;
            float repliesH = n > 0 ? n * ReplyH + (n - 1) * ReplyGap + 36f : 0f;
            float h = 128f + textH + 40f + repliesH + (n > 0 ? 30f : 50f);
            talkCard.sizeDelta = new Vector2(TalkW, h);
            for (int i = 0; i < talkReplies.Count; i++)
            {
                bool on = i < n;
                talkReplies[i].gameObject.SetActive(false);   // they come up once the line has typed on (Show)
                if (!on) continue;
                talkReplyText[i].text = line.replies[i].text;
                float y = 30f + (n - 1 - i) * (ReplyH + ReplyGap);
                Place(talkReplies[i], new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(TalkW - 2f * TalkPad, ReplyH), new Vector2(0.5f, 0f));
            }
            talkBody.text = "";
            Show();
        }

        void Show()
        {
            var line = talk[talkAt];
            int shown = Mathf.Clamp(Mathf.FloorToInt(talkShown), 0, line.text.Length);
            // (the rest of the line is there but clear, so the words don't jump about as they wrap while typing)
            talkBody.supportRichText = true;
            talkBody.text = shown >= line.text.Length ? line.text : line.text.Substring(0, shown) + "<color=#00000000>" + line.text.Substring(shown) + "</color>";
            bool done = shown >= line.text.Length;
            int n = line.replies != null ? line.replies.Length : 0;
            for (int i = 0; i < n; i++) if (talkReplies[i].gameObject.activeSelf != done) talkReplies[i].gameObject.SetActive(done);
            bool more = done && n == 0;
            if (talkMore.gameObject.activeSelf != more) talkMore.gameObject.SetActive(more);
            if (more) { float p = 0.75f + 0.25f * Mathf.Sin(talkT * 4f); talkMore.localScale = Vector3.one * p; }
        }
    }
}
