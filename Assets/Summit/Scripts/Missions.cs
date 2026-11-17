using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Three daily missions, picked from the local date so everyone gets the same three on the same day,
    /// and fresh ones at local midnight. Progress comes from the game's own events (tricks, crashes,
    /// pickups, grinds) through Track, plus a per-frame Step while riding. A mission pays its points
    /// the moment it completes, mid-run, and says so through Completed.
    /// </summary>
    public static class Missions
    {
        public enum Kind { Trick, CleanDistance, Cans, Shields, Finish, Rails, Boxes, NearSlide, Score, Speed, CloseCalls, Tricks, TotalDistance }

        public class Mission
        {
            public Kind kind;
            public string match;       // part of a trick's name, for Trick missions
            public int target, reward;
            public bool perRun;        // has to happen inside one run (starts over each run until done)
            public string title;
            public float progress;
            public bool done;
            public float Fraction => Mathf.Clamp01(progress / target);
            public string Count => kind == Kind.CleanDistance || kind == Kind.TotalDistance
                ? Mathf.FloorToInt(Mathf.Min(progress, target)).ToString("N0") + " / " + target.ToString("N0") + " m"
                : kind == Kind.NearSlide ? Mathf.FloorToInt(Mathf.Min(progress, target)) + " / " + target + " s"
                : kind == Kind.Speed ? Mathf.FloorToInt(Mathf.Min(progress, target)) + " / " + target + " km/h"
                : Mathf.FloorToInt(Mathf.Min(progress, target)).ToString("N0") + " / " + target.ToString("N0");
        }

        public static readonly List<Mission> Today = new List<Mission>();
        public static int Day { get; private set; }
        /// <summary>Raised when a mission completes (its reward has already been paid).</summary>
        public static System.Action<Mission> Completed;
        /// <summary>Mission points paid during the current run.</summary>
        public static int RunPoints { get; private set; }
        /// <summary>Tricks landed this run (close calls don't count).</summary>
        public static int RunTricks { get; private set; }

        static int LocalDay(System.DateTime t) => t.Year * 10000 + t.Month * 100 + t.Day;

        /// <summary>Time until the next set of missions.</summary>
        public static System.TimeSpan ResetsIn => System.DateTime.Today.AddDays(1) - System.DateTime.Now;
        public static string ResetsInText { get { var r = ResetsIn; return r.Hours + "h " + r.Minutes.ToString("00") + "m"; } }

        public static int DoneCount { get { Refresh(); int n = 0; foreach (var m in Today) if (m.done) n++; return n; } }

        // ---------------------------------------------------------------- the day's three

        /// <summary>Make sure today's missions are loaded (a new day rolls new ones).</summary>
        public static void Refresh() => Refresh(System.DateTime.Now);

        public static void Refresh(System.DateTime now)
        {
            int d = LocalDay(now);
            if (d == Day && Today.Count > 0) return;
            Day = d;
            Today.Clear();
            Today.AddRange(Roll(d));
            // pick up progress saved earlier today
            string saved = PlayerPrefs.GetString("summit_missions", "");
            var parts = saved.Split('|');
            if (parts.Length == Today.Count + 1 && parts[0] == d.ToString())
                for (int i = 0; i < Today.Count; i++)
                {
                    var f = parts[i + 1].Split(',');
                    if (f.Length == 2 && float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float p))
                    { Today[i].progress = p; Today[i].done = f[1] == "1"; }
                }
        }

        static void Save()
        {
            var sb = new System.Text.StringBuilder(Day.ToString());
            foreach (var m in Today)
                sb.Append('|').Append(m.progress.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(m.done ? '1' : '0');
            PlayerPrefs.SetString("summit_missions", sb.ToString());
            PlayerPrefs.Save();
        }

        /// <summary>Three different kinds of mission for a day, from a seeded shuffle.</summary>
        public static List<Mission> Roll(int day)
        {
            var rng = new System.Random(day * 7919 + 17);
            int Pick(params int[] xs) => xs[rng.Next(xs.Length)];
            var makers = new List<System.Func<Mission>>
            {
                () => { int n = Pick(2, 3); return new Mission { kind = Kind.Trick, match = "Backflip", target = n, reward = 120 + 40 * n, title = "Land " + n + " backflips" }; },
                () => new Mission { kind = Kind.Trick, match = "Frontflip", target = 1, reward = 180, title = "Land a frontflip" },
                () => { int n = Pick(3, 4, 5); return new Mission { kind = Kind.Trick, match = "360", target = n, reward = 60 + 25 * n, title = "Land " + n + " 360s" }; },
                () => { string g = new[] { "Indy", "Melon", "Method", "Mute" }[rng.Next(4)]; return new Mission { kind = Kind.Trick, match = g, target = 2, reward = 110, title = "Land 2 " + g + " grabs" }; },
                () => { int n = Pick(1000, 1500, 2000); return new Mission { kind = Kind.CleanDistance, target = n, reward = n / 10 + 50, perRun = true, title = "Ride " + n.ToString("N0") + " m without falling" }; },
                () => { int n = Pick(4, 6, 8); return new Mission { kind = Kind.Cans, target = n, reward = 20 * n + 40, title = "Collect " + n + " Mountain Zoom cans" }; },
                () => { int n = Pick(3, 5); return new Mission { kind = Kind.Shields, target = n, reward = 30 * n + 40, title = "Pick up " + n + " shields" }; },
                () => new Mission { kind = Kind.Finish, target = 1, reward = 250, title = "Finish Mt. Vertigo" },
                () => { int n = Pick(3, 4, 5); return new Mission { kind = Kind.Rails, target = n, reward = 35 * n, title = "Grind " + n + " rails" }; },
                () => { int n = Pick(3, 5); return new Mission { kind = Kind.Boxes, target = n, reward = 30 * n, title = "Slide " + n + " boxes" }; },
                () => { int n = Pick(30, 45, 60); return new Mission { kind = Kind.NearSlide, target = n, reward = 3 * n + 40, perRun = true, title = "Survive " + n + " s with the avalanche within 50 m" }; },
                () => { int n = Pick(6000, 9000, 12000); return new Mission { kind = Kind.Score, target = n, reward = n / 50, perRun = true, title = "Score " + n.ToString("N0") + " in one run" }; },
                () => { int n = Pick(90, 100, 110); return new Mission { kind = Kind.Speed, target = n, reward = 2 * n - 60, title = "Hit " + n + " km/h" }; },
                () => { int n = Pick(5, 8, 12); return new Mission { kind = Kind.CloseCalls, target = n, reward = 12 * n + 40, title = "Get " + n + " close calls" }; },
                () => { int n = Pick(10, 15, 20); return new Mission { kind = Kind.Tricks, target = n, reward = 8 * n + 40, title = "Land " + n + " tricks" }; },
                () => { int n = Pick(5000, 8000); return new Mission { kind = Kind.TotalDistance, target = n, reward = n / 40, title = "Ride " + n.ToString("N0") + " m today" }; },
            };
            var list = new List<Mission>();
            var kinds = new HashSet<Kind>();
            while (list.Count < 3)
            {
                var m = makers[rng.Next(makers.Count)]();
                if (kinds.Add(m.kind)) list.Add(m);
            }
            return list;
        }

        // ---------------------------------------------------------------- tracking a run

        static bool active;
        static float cleanFrom, lastZ;
        static Rider rider;

        /// <summary>Listen to the rider's and the pickups' events (once, when the game is built).</summary>
        public static void Track(Rider r, PowerUps power)
        {
            rider = r;
            r.Trick += (name, pts) =>
            {
                if (!active || name == null) return;
                if (name == "Close call") { Bump(Kind.CloseCalls, 1f); return; }
                RunTricks++;
                Bump(Kind.Tricks, 1f);
                foreach (var m in Today) if (m.kind == Kind.Trick && !m.done && name.Contains(m.match)) Add(m, 1f);
            };
            r.Crash += why => { if (active) { cleanFrom = -1f; Restart(Kind.CleanDistance); } };
            r.GrindStart += f => { if (active) Bump(f.kind == Course.Kind.Rail ? Kind.Rails : Kind.Boxes, 1f); };
            if (power != null) power.Collected += k => { if (active) Bump(k == PowerUps.Kind.Soda ? Kind.Cans : Kind.Shields, 1f); };
        }

        public static void RunStart()
        {
            Refresh();
            active = true;
            RunPoints = 0; RunTricks = 0;
            cleanFrom = rider != null ? rider.Z : 0f;
            lastZ = cleanFrom;
            foreach (var m in Today) if (m.perRun && !m.done) m.progress = 0f;
        }

        /// <summary>Per frame while riding: distances, the slide, speed and score.</summary>
        public static void Step(float dt, float gap, int score)
        {
            if (!active || rider == null) return;
            float z = rider.Z;
            // after a fall the clean distance counts again from where you got back up
            if (cleanFrom < 0f && !rider.Crashed) cleanFrom = z;
            if (cleanFrom >= 0f) Max(Kind.CleanDistance, z - cleanFrom);
            if (z > lastZ) { Bump(Kind.TotalDistance, z - lastZ); lastZ = z; }
            if (gap < 50f && !rider.Crashed) Bump(Kind.NearSlide, dt);
            Max(Kind.Speed, rider.Speed * 3.6f);
            Max(Kind.Score, score);
        }

        public static void Finished() { if (active) Bump(Kind.Finish, 1f); }

        public static void RunEnd()
        {
            if (!active) return;
            active = false;
            Save();
        }

        static void Bump(Kind k, float by) { foreach (var m in Today) if (m.kind == k && !m.done) Add(m, by); }
        static void Max(Kind k, float v) { foreach (var m in Today) if (m.kind == k && !m.done && v > m.progress) { m.progress = v; Check(m); } }
        static void Restart(Kind k) { foreach (var m in Today) if (m.kind == k && !m.done) m.progress = 0f; }
        static void Add(Mission m, float by) { m.progress += by; Check(m); }

        static void Check(Mission m)
        {
            if (m.done || m.progress < m.target) return;
            m.done = true;
            RunPoints += m.reward;
            Progress.Add(m.reward);
            Save();
            Completed?.Invoke(m);
        }

        /// <summary>Complete one directly (test tools only).</summary>
        public static void ForceComplete(int i) { Refresh(); if (i < Today.Count) { Today[i].progress = Today[i].target; Check(Today[i]); } }
        /// <summary>Set progress directly (test tools only).</summary>
        public static void SetProgress(int i, float p) { Refresh(); if (i < Today.Count) Today[i].progress = p; }
    }
}
