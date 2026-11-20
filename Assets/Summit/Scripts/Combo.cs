using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>What a link in a combo is (Rider.Link, Combo.Add): how it is counted, never how it is shown.</summary>
    public enum LinkKind
    {
        Trick,      // an air trick the player built (spins, flips, grabs)
        Air,        // the air itself: hang time
        Grind,      // a stance held on a box, a rail or a lift cable
        Chair,      // a chair's grip skimmed on a cable
        CloseCall,  // past a tree or a rock at speed
        Smash,      // through something while invincible
        Brush,      // a treetop, a branch: clipped without a crash (the trees session's event goes here)
        Bonk,       // a toboggan clattering off a rail or a cable
        Bonus,      // anything else
    }

    /// <summary>One thing that scored: its name as shown, its points before the multiplier.</summary>
    public struct TrickLink
    {
        public string name;
        public int points;
        public LinkKind kind;
        /// <summary>Landed short of clean: paid (already halved), but it does not raise the multiplier.</summary>
        public bool sketchy;
    }

    /// <summary>
    /// The running combo. Links (tricks, grinds, close calls, smashes, big airs) go into a pot; the multiplier is
    /// the number of different links in it; a short window on the snow keeps it alive between links; it banks
    /// (pot x multiplier, into the run's score) when the window closes, and a crash loses it.
    ///
    /// No Unity objects and no clock of its own: the game steps it (Step) and listens (Added, Banked, Dropped),
    /// and the HUD reads it. The test tools drive it the same way.
    /// </summary>
    public class Combo
    {
        // ---- tuning
        public static float Window = 2.2f;          // seconds on the snow between links
        public static float SketchyWindow = 1.1f;   // after a landing that was not clean
        public const int MaxMult = 8;
        public static float StaleStep = 0.15f, StaleFloor = 0.4f;   // the same trick again in a run: 15% less each time, to 40%

        public struct Line { public string name; public int points, count; public bool live, sketchy; }

        /// <summary>The links of the combo in hand, oldest first (repeats of the last one are folded into it).</summary>
        public readonly List<Line> Lines = new List<Line>();
        /// <summary>Points in the pot, before the multiplier (not counting the live link).</summary>
        public int Pot { get; private set; }
        public int Mult { get; private set; }
        /// <summary>The link being held right now (a grind paying by the metre, a trick being built in the air), or null.</summary>
        public string LiveName { get; private set; }
        public int LivePoints { get; private set; }
        float liveFactor = 1f;
        bool liveKey, liveUp;   // the live link put its own name in the combo / raised the multiplier with it
        /// <summary>What the combo would bank this moment.</summary>
        public int Total => (Pot + LivePoints) * Mathf.Max(1, Mult);
        public bool Active => Lines.Count > 0 || LiveName != null;
        /// <summary>The window left, 0..1 (1 while it is held open in the air or on a rail).</summary>
        public float WindowLeft => windowFull > 0f ? Mathf.Clamp01(window / windowFull) : 0f;
        public bool Held { get; private set; }
        /// <summary>Everything banked this run.</summary>
        public int Banked { get; private set; }
        /// <summary>The biggest single bank this run, and how many links the longest combo had.</summary>
        public int BestBank { get; private set; }
        public int BestLinks { get; private set; }
        int links;

        float window, windowFull = Window;
        readonly HashSet<string> keys = new HashSet<string>();           // the different links in this combo
        readonly Dictionary<string, int> uses = new Dictionary<string, int>();   // how often each trick has been landed this run

        /// <summary>A link went in: the line as shown (points after staleness), and whether it raised the multiplier.</summary>
        public System.Action<Line, bool> Added;
        /// <summary>Banked: the total paid, the pot and the multiplier it came from.</summary>
        public System.Action<int, int, int> BankedNow;
        /// <summary>Lost to a crash: what it would have paid.</summary>
        public System.Action<int> Dropped;

        public void Reset()
        {
            Lines.Clear(); keys.Clear(); uses.Clear();
            Pot = 0; Mult = 0; LiveName = null; LivePoints = 0; window = 0f; windowFull = Window; Held = false; liveKey = liveUp = false;
            Banked = 0; BestBank = 0; BestLinks = 0; links = 0;
        }

        static bool Stales(LinkKind k) => k == LinkKind.Trick || k == LinkKind.Grind;

        /// <summary>What the next landing of this trick is worth, 1 down to StaleFloor.</summary>
        public float Fresh(string name, LinkKind kind)
        {
            if (!Stales(kind)) return 1f;
            uses.TryGetValue(Key(name), out int n);
            return Mathf.Max(StaleFloor, 1f - StaleStep * n);
        }

        // ("Sketchy 360 Indy" is the same trick as "360 Indy")
        static string Key(string name) => name.StartsWith("Sketchy ") ? name.Substring(8) : name;

        /// <summary>A link: into the pot, the window refilled.</summary>
        public void Add(TrickLink l)
        {
            if (string.IsNullOrEmpty(l.name)) return;
            string key = Key(l.name);
            float fresh;
            bool wasLive = false;
            if (LiveName != null && (l.kind == LinkKind.Trick || l.kind == LinkKind.Grind))
            {
                if (key == Key(LiveName) && !l.sketchy)
                {
                    // the live link, finished as it stood: it has been in the multiplier since it began
                    fresh = liveFactor; wasLive = true;
                    LiveName = null; LivePoints = 0; liveKey = liveUp = false;
                    RemoveLive();
                }
                else { CancelLive(); fresh = Fresh(l.name, l.kind); }   // landed as something else: that is the link
            }
            else fresh = Fresh(l.name, l.kind);
            if (Stales(l.kind)) { uses.TryGetValue(key, out int n); uses[key] = n + 1; }
            int pts = Mathf.RoundToInt(l.points * fresh);
            bool up = false;
            if (!wasLive && !l.sketchy && keys.Add(key) && Mult < MaxMult) { Mult++; up = true; }
            if (Mult < 1) Mult = 1;
            Pot += pts;
            links++;
            Line line;
            int last = LastSolid();
            if (last >= 0 && Lines[last].name == l.name)
            {
                // the same again (a run of close calls): one line, counting up
                line = Lines[last]; line.points += pts; line.count++;
                Lines[last] = line;
            }
            else
            {
                line = new Line { name = l.name, points = pts, count = 1, sketchy = l.sketchy };
                int at = Lines.Count > 0 && Lines[Lines.Count - 1].live ? Lines.Count - 1 : Lines.Count;   // (under the live one)
                Lines.Insert(at, line);
            }
            windowFull = l.sketchy ? SketchyWindow : Window;
            window = windowFull;
            Added?.Invoke(line, up);
        }

        int LastSolid()
        {
            for (int i = Lines.Count - 1; i >= 0; i--) if (!Lines[i].live) return i;
            return -1;
        }

        void RemoveLive()
        {
            for (int i = Lines.Count - 1; i >= 0; i--) if (Lines[i].live) Lines.RemoveAt(i);
        }

        /// <summary>
        /// The link in progress, each frame it is: its name as it stands and what it has made so far. A new name
        /// is a new link (it counts toward the multiplier at once). Null: nothing in progress.
        /// </summary>
        public void SetLive(string name, int points, LinkKind kind)
        {
            if (name == null) { CancelLive(); return; }
            if (name != LiveName)
            {
                // (a trick growing in the air changes its name as it goes: "180", "360", "360 Indy". One link, renamed)
                bool renamed = LiveName != null && kind == LinkKind.Trick;
                bool hadUp = liveUp;
                if (liveKey) keys.Remove(Key(LiveName));
                if (liveUp) Mult--;
                if (!renamed) RemoveLive();
                LiveName = name;
                liveFactor = Fresh(name, kind);
                liveKey = keys.Add(Key(name));
                liveUp = liveKey && Mult < MaxMult;
                if (liveUp) Mult++;
                if (Mult < 1) Mult = 1;
                var line = new Line { name = name, points = 0, count = 1, live = true };
                int at = Lines.Count - 1;
                if (renamed && at >= 0 && Lines[at].live) Lines[at] = line; else Lines.Add(line);
                windowFull = Window; window = windowFull;
                Added?.Invoke(line, liveUp && !(renamed && hadUp));
            }
            LivePoints = Mathf.RoundToInt(points * liveFactor);
            int li = Lines.Count - 1;
            if (li >= 0 && Lines[li].live) { var ln = Lines[li]; ln.points = LivePoints; Lines[li] = ln; }
        }

        /// <summary>
        /// A live trick that came to nothing (the air ended with no trick landed): it leaves the combo as if it
        /// had never been named.
        /// </summary>
        public void CancelLive()
        {
            if (LiveName == null) return;
            if (liveKey) keys.Remove(Key(LiveName));
            if (liveUp) Mult--;
            liveKey = liveUp = false;
            LiveName = null; LivePoints = 0; RemoveLive();
            if (Lines.Count == 0) { Mult = 0; Pot = 0; }
        }

        /// <summary>Time passing. hold: in the air or on a rail, where the window does not run.</summary>
        public void Step(float dt, bool hold)
        {
            Held = hold || LiveName != null;
            if (!Active) return;
            if (Held) { window = windowFull; return; }
            window -= dt;
            if (window <= 0f) Bank();
        }

        /// <summary>Pay it out now (the window closed, or the finish line).</summary>
        public void Bank()
        {
            if (!Active) return;
            int total = Total, pot = Pot + LivePoints, mult = Mathf.Max(1, Mult);
            Banked += total;
            if (total > BestBank) BestBank = total;
            if (links > BestLinks) BestLinks = links;
            Clear();
            if (total > 0) BankedNow?.Invoke(total, pot, mult);
        }

        /// <summary>A crash: the pot is gone.</summary>
        public void Drop()
        {
            if (!Active) return;
            int total = Total;
            Clear();
            if (total > 0) Dropped?.Invoke(total);
        }

        void Clear()
        {
            Lines.Clear(); keys.Clear();
            Pot = 0; Mult = 0; LiveName = null; LivePoints = 0; window = 0f; links = 0; Held = false; liveKey = liveUp = false;
        }
    }
}
