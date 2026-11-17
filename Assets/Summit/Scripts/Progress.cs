using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What the player keeps between runs: the points balance, the upgrades bought with it and the
    /// outfit picked. Everything lives in PlayerPrefs, so it survives quitting and app updates.
    /// Gameplay code asks the query helpers (GripScale, Falls, PowerUpTime, HeadStart, Has) and
    /// never reads the levels itself, so a balance change happens in one place.
    /// </summary>
    public static class Progress
    {
        // ---------------------------------------------------------------- the shop's stock

        public enum Up { Grip, Helmet, Fizz, HeadStart, FirePit, Flags, Lights, Snowman }

        public class Item
        {
            public Up id; public string title, blurb; public int[] cost;   // one price per level
            public bool decor;                                              // shows up in the camp
            public int Max => cost.Length;
        }

        public static readonly Item[] Shop =
        {
            new Item { id = Up.Grip, title = "SHARP EDGES", blurb = "the edge holds harder in a carve", cost = new[] { 300, 700, 1500 } },
            new Item { id = Up.Helmet, title = "SPARE HELMET", blurb = "a 4th fall before the run ends", cost = new[] { 2000 } },
            new Item { id = Up.Fizz, title = "EXTRA FIZZ", blurb = "power-ups last longer", cost = new[] { 250, 600, 1200 } },
            new Item { id = Up.HeadStart, title = "EARLY WARNING", blurb = "the avalanche starts further back", cost = new[] { 300, 700, 1400 } },
            new Item { id = Up.FirePit, title = "FIRE PIT", blurb = "a campfire in the middle of camp", cost = new[] { 400 }, decor = true },
            new Item { id = Up.Flags, title = "PRAYER FLAGS", blurb = "strings of flags over the camp", cost = new[] { 250 }, decor = true },
            new Item { id = Up.Lights, title = "STRING LIGHTS", blurb = "warm bulbs from the lodge", cost = new[] { 350 }, decor = true },
            new Item { id = Up.Snowman, title = "SNOWMAN", blurb = "a friend by the lookout", cost = new[] { 200 }, decor = true },
        };

        public static Item Find(Up id) { foreach (var it in Shop) if (it.id == id) return it; return null; }

        // outfits: the first of each is the one you start with
        public static readonly Color[] Jackets =
        {
            new Color(0.93f, 0.31f, 0.14f), new Color(0.12f, 0.45f, 0.92f), new Color(0.98f, 0.82f, 0.10f),
            new Color(0.15f, 0.62f, 0.32f), new Color(0.85f, 0.22f, 0.62f), new Color(0.10f, 0.11f, 0.13f),
        };
        public static readonly string[] JacketNames = { "ORANGE", "COBALT", "SUNSHINE", "PINE", "MAGENTA", "STEALTH" };
        public static readonly Color[] Boards =
        {
            new Color(0.10f, 0.45f, 0.90f), new Color(0.90f, 0.12f, 0.12f), new Color(0.95f, 0.95f, 0.97f),
            new Color(0.40f, 0.85f, 0.20f), new Color(0.98f, 0.55f, 0.05f), new Color(0.55f, 0.30f, 0.85f),
        };
        public static readonly string[] BoardNames = { "BLUE", "RED", "WHITE", "LIME", "TANGERINE", "VIOLET" };
        public static int OutfitCost(int i) => i == 0 ? 0 : 100 + 50 * i;

        // ---------------------------------------------------------------- state

        static bool loaded;
        static int points, lifetime;
        static readonly int[] levels = new int[System.Enum.GetValues(typeof(Up)).Length];
        static int jacket, board, jacketsOwned = 1, boardsOwned = 1;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            points = PlayerPrefs.GetInt("summit_points", 0);
            lifetime = PlayerPrefs.GetInt("summit_points_life", 0);
            for (int i = 0; i < levels.Length; i++) levels[i] = PlayerPrefs.GetInt("summit_up_" + (Up)i, 0);
            jacket = PlayerPrefs.GetInt("summit_jacket", 0);
            board = PlayerPrefs.GetInt("summit_boardcol", 0);
            jacketsOwned = PlayerPrefs.GetInt("summit_jackets", 1) | 1;
            boardsOwned = PlayerPrefs.GetInt("summit_boards", 1) | 1;
        }

        static void Save()
        {
            PlayerPrefs.SetInt("summit_points", points);
            PlayerPrefs.SetInt("summit_points_life", lifetime);
            for (int i = 0; i < levels.Length; i++) PlayerPrefs.SetInt("summit_up_" + (Up)i, levels[i]);
            PlayerPrefs.SetInt("summit_jacket", jacket);
            PlayerPrefs.SetInt("summit_boardcol", board);
            PlayerPrefs.SetInt("summit_jackets", jacketsOwned);
            PlayerPrefs.SetInt("summit_boards", boardsOwned);
            PlayerPrefs.Save();
        }

        /// <summary>Raised whenever the balance or anything owned changes (the camp redresses itself).</summary>
        public static System.Action Changed;

        public static int Points { get { Load(); return points; } }
        public static int Lifetime { get { Load(); return lifetime; } }

        public static void Add(int pts)
        {
            Load();
            if (pts <= 0) return;
            points += pts; lifetime += pts;
            Save(); Changed?.Invoke();
        }

        static bool Spend(int cost)
        {
            Load();
            if (cost > points) return false;
            points -= cost;
            return true;
        }

        public static int Level(Up u) { Load(); return levels[(int)u]; }
        public static bool Has(Up u) => Level(u) > 0;
        public static bool Maxed(Up u) => Level(u) >= Find(u).Max;
        public static int NextCost(Up u) { var it = Find(u); int l = Level(u); return l < it.Max ? it.cost[l] : 0; }

        /// <summary>Buy the next level of an upgrade; false if it is maxed or you are short.</summary>
        public static bool Buy(Up u)
        {
            if (Maxed(u) || !Spend(NextCost(u))) return false;
            levels[(int)u]++;
            Save(); Changed?.Invoke();
            return true;
        }

        public static int Jacket { get { Load(); return jacket; } }
        public static int Board { get { Load(); return board; } }
        public static bool OwnsJacket(int i) { Load(); return (jacketsOwned & (1 << i)) != 0; }
        public static bool OwnsBoard(int i) { Load(); return (boardsOwned & (1 << i)) != 0; }

        /// <summary>Wear a jacket, buying it first if needed; false if you can't afford it.</summary>
        public static bool PickJacket(int i)
        {
            if (!OwnsJacket(i)) { if (!Spend(OutfitCost(i))) return false; jacketsOwned |= 1 << i; }
            jacket = i; Save(); Changed?.Invoke();
            return true;
        }

        public static bool PickBoard(int i)
        {
            if (!OwnsBoard(i)) { if (!Spend(OutfitCost(i))) return false; boardsOwned |= 1 << i; }
            board = i; Save(); Changed?.Invoke();
            return true;
        }

        // ---------------------------------------------------------------- what the upgrades do (the hooks)

        /// <summary>Multiplier on the edge's grip (Rider.GripLimit and Rider.Grip).</summary>
        public static float GripScale => 1f + 0.07f * Level(Up.Grip);
        /// <summary>Falls allowed in a run, given the base rule.</summary>
        public static int Falls(int baseFalls) => baseFalls + Level(Up.Helmet);
        /// <summary>Multiplier on how long power-ups last.</summary>
        public static float PowerUpTime => 1f + 0.25f * Level(Up.Fizz);
        /// <summary>Extra metres between you and the avalanche at the start.</summary>
        public static float HeadStart => 15f * Level(Up.HeadStart);

        static float gripLimit0 = -1f, grip0;
        /// <summary>Set the rider's tuning from the upgrades (called as a run starts).</summary>
        public static void ApplyRider()
        {
            if (gripLimit0 < 0f) { gripLimit0 = Rider.GripLimit; grip0 = Rider.Grip; }   // the stock values, once
            Rider.GripLimit = gripLimit0 * GripScale;
            Rider.Grip = grip0 * GripScale;
        }

        // ---------------------------------------------------------------- what a run earns

        public struct Reward { public int fromScore, fromDistance, fromTricks, fromFinish, fromMissions, tricks; public int Total => fromScore + fromDistance + fromTricks + fromFinish + fromMissions; }

        /// <summary>Points for a finished run. Mission rewards were paid as they completed; they are listed, not paid twice.</summary>
        public static Reward EndRun(int score, float metres, int tricks, bool finished, int missionPoints)
        {
            var r = new Reward
            {
                fromScore = Mathf.Max(0, score) / 50,
                fromDistance = Mathf.FloorToInt(Mathf.Max(0f, metres) / 50f),
                fromTricks = tricks * 4,
                fromFinish = finished ? 150 : 0,
                fromMissions = missionPoints,
                tricks = tricks,
            };
            Add(r.Total - missionPoints);
            return r;
        }

        /// <summary>Forget everything (test tools only).</summary>
        public static void ResetAll()
        {
            Load();
            points = lifetime = 0;
            for (int i = 0; i < levels.Length; i++) levels[i] = 0;
            jacket = board = 0; jacketsOwned = boardsOwned = 1;
            Save(); Changed?.Invoke();
        }

        /// <summary>Set the balance and levels directly (test tools only).</summary>
        public static void Grant(int pts, params Up[] owned)
        {
            Load();
            points = pts;
            foreach (var u in owned) levels[(int)u] = Mathf.Max(levels[(int)u], 1);
            Save(); Changed?.Invoke();
        }
    }
}
