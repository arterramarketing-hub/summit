using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What Alyssa says at the fire (MetaUiTalk shows it, Camp starts it). A conversation is a run of her lines; a line
    /// can offer two or three replies, each going on to another line, ending the talk, or handing you a stick to roast
    /// your own. Five of them, taken in turn, one a visit, so she doesn't say the same thing every time you walk up
    /// (which one is next is remembered between sessions). Warm, short, about s'mores, with a tip about the mountain.
    /// </summary>
    public static class CampTalk
    {
        public const int End = -1, Roast = -2;

        public class Reply
        {
            public string text; public int next;
            public Reply(string t, int n) { text = t; next = n; }
        }

        public class Line
        {
            public string text; public Reply[] replies;
            public int then = int.MinValue;   // where tapping on goes (when it isn't simply the next line)
            public Line(string t, params Reply[] r) { text = t; replies = r; }
            public Line Then(int i) { then = i; return this; }
        }

        static Reply R(string t, int next) => new Reply(t, next);
        static Line L(string t, params Reply[] r) => new Line(t, r);

        public static readonly Line[][] Conversations =
        {
            // 1. hello, and how she does hers
            new[]
            {
                L("Oh, hey! Pull up a stump. I'm on my fourth s'more and I regret nothing."),
                L("The trick is patience. Hold it over the embers, not the flames, and keep it turning.",
                  R("How do you know when it's done?", 2), R("Patience? I'm on a mountain called Vertigo.", 3)),
                L("When it's golden all the way round and starting to slump on the stick. Then it's ready.", R("Can I try one?", Roast), R("I'll just watch.", 4)),
                L("Ha! Fair. The mountain's in a hurry. The marshmallow isn't.", R("Can I try one?", Roast), R("Next time.", 4)),
                L("Suit yourself. There's always a stick here for you."),
            },
            // 2. golden versus burnt
            new[]
            {
                L("Settle something for me. Golden, or set it on fire and call it caramelised?",
                  R("Golden. Every time.", 1), R("Burnt. Crispy outside, molten inside.", 2), R("Whatever happens happens.", 3)),
                L("A person of taste. Golden's a whole minute of not looking away. Most people can't.", R("Let me prove it.", Roast), R("I believe you.", 4)),
                L("Bold. Valid, honestly. Just blow it out before it drips on your boots.", R("Hand me a stick.", Roast), R("Noted.", 4)),
                L("That's how I ride, too. Mixed results."),
                L("Anyway. Mine's going golden. Probably."),
            },
            // 3. a tip about the mountain
            new[]
            {
                L("Did you drop in today? How was the lip at the start?",
                  R("Clean. Landed it.", 1), R("I may have tumbled.", 2)),
                L("Nice. Here's a tip: stay off the right-hand side below the camp. That edge doesn't forgive anyone.").Then(3),
                L("Everybody does, the first few times. Keep your weight forward off the lip and you'll stick it."),
                L("And if the snow starts sliding behind you, don't look back. Just go.", R("Good tip. Thanks.", 4), R("Can I roast one before I go?", Roast)),
                L("Any time. The fire's always on."),
            },
            // 4. the s'more itself
            new[]
            {
                L("You know what makes a s'more? Not the marshmallow. The chocolate."),
                L("You put the square on the cracker first, then the hot marshmallow melts it from the top. Physics.",
                  R("That's beautiful.", 2), R("I've been doing it wrong.", 3)),
                L("It is. I think about it a normal amount.", R("Show me, then.", Roast), R("Ha. Fair.", 4)),
                L("Everyone has. Don't worry, it's a judgement-free fire.", R("Show me, then.", Roast), R("I'll remember.", 4)),
                L("Grab a stick whenever you like."),
            },
            // 5. night and the stars
            new[]
            {
                L("I come out here when it gets dark. You can see every star up here."),
                L("And the mountain goes all quiet. Just the fire, and somebody's marshmallow catching light. Usually mine.",
                  R("Sounds perfect.", 2), R("Do you ever ride at night?", 3)),
                L("It is. Want to make one while it's quiet?", R("Yes, please.", Roast), R("Maybe later.", 4)),
                L("Only with a good headlamp. The signs light up when it shines on them. Spooky, honestly."),
                L("Stay warm out there."),
            },
        };

        const string Pref = "summit_alyssa_talk";

        /// <summary>The conversation for this visit (and moves on to the next one for the visit after).</summary>
        public static Line[] Next()
        {
            int i = PlayerPrefs.GetInt(Pref, 0);
            PlayerPrefs.SetInt(Pref, (i + 1) % Conversations.Length);
            return Conversations[((i % Conversations.Length) + Conversations.Length) % Conversations.Length];
        }

        /// <summary>Where a line with no replies goes when you tap on: its own next, or the following line, or the end.</summary>
        public static int After(Line[] c, int i)
        {
            if (c[i].then != int.MinValue) return c[i].then;
            return i + 1 < c.Length ? i + 1 : End;
        }
    }
}
