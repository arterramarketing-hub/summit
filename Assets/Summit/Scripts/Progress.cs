using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What the player keeps between runs: the points balance, the upgrades bought with it and the
    /// outfit and the ride (snowboard, skis or toboggan) picked. Everything lives in PlayerPrefs, so it survives quitting and app updates.
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
            new Item { id = Up.Grip, title = "SHARP EDGES", blurb = "the edge holds harder in a carve", cost = new[] { 250, 450, 700, 1000, 1400, 1900, 2500, 3200, 4000, 5000 } },
            new Item { id = Up.Helmet, title = "SPARE HELMET", blurb = "a 4th fall before the run ends", cost = new[] { 2000 } },
            new Item { id = Up.Fizz, title = "EXTRA FIZZ", blurb = "power-ups last longer", cost = new[] { 200, 350, 550, 800, 1100, 1500, 2000, 2600, 3300, 4100 } },
            new Item { id = Up.HeadStart, title = "EARLY WARNING", blurb = "the avalanche starts further back", cost = new[] { 250, 400, 650, 950, 1300, 1750, 2300, 2900, 3600, 4400 } },
            new Item { id = Up.FirePit, title = "FIRE PIT", blurb = "a campfire in the middle of camp", cost = new[] { 400 }, decor = true },
            new Item { id = Up.Flags, title = "PRAYER FLAGS", blurb = "strings of flags over the camp", cost = new[] { 250 }, decor = true },
            new Item { id = Up.Lights, title = "STRING LIGHTS", blurb = "warm bulbs from the lodge", cost = new[] { 350 }, decor = true },
            new Item { id = Up.Snowman, title = "SNOWMAN", blurb = "a friend by the lookout", cost = new[] { 200 }, decor = true },
        };

        public static Item Find(Up id) { foreach (var it in Shop) if (it.id == id) return it; return null; }

        // outfits (the closet's stock is Wardrobe's): the colours and names of the jackets and boards, for the camp's
        // board rack and the older callers
        public static Color[] Jackets => Colours(Wardrobe.Cat.Jacket);
        public static Color[] Boards => Colours(Wardrobe.Cat.Board);
        static Color[] Colours(Wardrobe.Cat c) { var it = Wardrobe.Items(c); var r = new Color[it.Length]; for (int i = 0; i < r.Length; i++) r[i] = it[i].a; return r; }
        public static int OutfitCost(int i) => Wardrobe.Get(Wardrobe.Cat.Jacket, i).cost;

        // ---------------------------------------------------------------- state

        static bool loaded;
        static int points, lifetime;
        static readonly int[] levels = new int[System.Enum.GetValues(typeof(Up)).Length];
        static int ride;
        // per closet category: what is worn, and a bit for each item owned (the first is everyone's). The jackets and
        // boards keep the keys they had before the closet, so old saves carry over; the ride has its own key
        static readonly int[] worn = new int[Wardrobe.Count], owned = new int[Wardrobe.Count];
        static string WornKey(Wardrobe.Cat c) => c == Wardrobe.Cat.Jacket ? "summit_jacket" : c == Wardrobe.Cat.Board ? "summit_boardcol" : "summit_wear_" + c;
        // Each set of clothes keeps its own colours: the jacket, trousers and boots picked for the summit kit stay with
        // the kit, and pyjamas are as they were made until you dye them. What the sets not on now were last worn in:
        static readonly Wardrobe.Cat[] Dyes = { Wardrobe.Cat.Jacket, Wardrobe.Cat.Pants, Wardrobe.Cat.Boots };
        static readonly int[,] kept = new int[3, 3];   // [set of clothes, which of Dyes]
        static string KeptKey(int set, int k) => "summit_set" + set + "_" + Dyes[k];
        /// <summary>Test tools: every key the sets' own colours are saved under.</summary>
        public static System.Collections.Generic.IEnumerable<string> SetKeys()
        {
            for (int o = 0; o < kept.GetLength(0); o++) for (int k = 0; k < Dyes.Length; k++) yield return KeptKey(o, k);
        }
        static string OwnedKey(Wardrobe.Cat c) => c == Wardrobe.Cat.Jacket ? "summit_jackets" : c == Wardrobe.Cat.Board ? "summit_boards" : "summit_own_" + c;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            points = PlayerPrefs.GetInt("summit_points", 0);
            lifetime = PlayerPrefs.GetInt("summit_points_life", 0);
            for (int i = 0; i < levels.Length; i++) levels[i] = PlayerPrefs.GetInt("summit_up_" + (Up)i, 0);
            ride = Mathf.Clamp(PlayerPrefs.GetInt("summit_ride", 0), 0, Rides.Count - 1);
            for (int c = 0; c < Wardrobe.Count; c++)
            {
                var cat = (Wardrobe.Cat)c;
                if (cat == Wardrobe.Cat.Ride) continue;
                owned[c] = PlayerPrefs.GetInt(OwnedKey(cat), 1) | 1;
                int w = PlayerPrefs.GetInt(WornKey(cat), 0);
                worn[c] = w >= 0 && w < Wardrobe.Items(cat).Length && (Wardrobe.Free(cat) || (owned[c] & (1 << w)) != 0) ? w : 0;
            }
            for (int o = 0; o < kept.GetLength(0); o++)
                for (int k = 0; k < Dyes.Length; k++)
                {
                    int w = PlayerPrefs.GetInt(KeptKey(o, k), 0);
                    kept[o, k] = w > 0 && w < Wardrobe.Items(Dyes[k]).Length && (owned[(int)Dyes[k]] & (1 << w)) != 0 ? w : 0;
                }
        }

        static void Save()
        {
            PlayerPrefs.SetInt("summit_points", points);
            PlayerPrefs.SetInt("summit_points_life", lifetime);
            for (int i = 0; i < levels.Length; i++) PlayerPrefs.SetInt("summit_up_" + (Up)i, levels[i]);
            PlayerPrefs.SetInt("summit_ride", ride);
            for (int c = 0; c < Wardrobe.Count; c++)
            {
                var cat = (Wardrobe.Cat)c;
                if (cat == Wardrobe.Cat.Ride) continue;
                PlayerPrefs.SetInt(WornKey(cat), worn[c]);
                PlayerPrefs.SetInt(OwnedKey(cat), owned[c]);
            }
            for (int o = 0; o < kept.GetLength(0); o++) for (int k = 0; k < Dyes.Length; k++) PlayerPrefs.SetInt(KeptKey(o, k), kept[o, k]);
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

        public static int Jacket => Worn(Wardrobe.Cat.Jacket);
        public static int Board => Worn(Wardrobe.Cat.Board);
        public static bool OwnsJacket(int i) => Owns(Wardrobe.Cat.Jacket, i);
        public static bool OwnsBoard(int i) => Owns(Wardrobe.Cat.Board, i);
        public static bool PickJacket(int i) => Wear(Wardrobe.Cat.Jacket, i);
        public static bool PickBoard(int i) => Wear(Wardrobe.Cat.Board, i);

        /// <summary>The item worn in a closet category (the ride: what you ride).</summary>
        public static int Worn(Wardrobe.Cat c) { Load(); return c == Wardrobe.Cat.Ride ? ride : worn[(int)c]; }
        /// <summary>Who is riding and in which clothes, as the one number the rider's model is built from (Wardrobe.Look).</summary>
        public static int RiderLook => Wardrobe.Look(Wardrobe.Worn());

        public static bool Owns(Wardrobe.Cat c, int i)
        {
            Load();
            if (Wardrobe.Free(c) || i == 0) return true;
            return i > 0 && i < 31 && (owned[(int)c] & (1 << i)) != 0;
        }

        /// <summary>Wear something from the closet, buying it first if needed; false if you can't afford it.</summary>
        public static bool Wear(Wardrobe.Cat c, int i)
        {
            Load();
            if (i < 0 || i >= Wardrobe.Items(c).Length) return false;
            if (c == Wardrobe.Cat.Ride) { PickRide((RideKind)i); return true; }
            if (!Owns(c, i)) { if (!Spend(Wardrobe.Items(c)[i].cost)) return false; owned[(int)c] |= 1 << i; }
            // a rider arrives in their own clothes (the OUTFIT tab then puts them in anyone's), and a set of clothes
            // comes in the colours it was last worn in
            if (c == Wardrobe.Cat.Rider) PutOn(Wardrobe.OwnOutfit(i));
            if (c == Wardrobe.Cat.Outfit) PutOn(i); else worn[(int)c] = i;
            Save(); Changed?.Invoke();
            return true;
        }

        static void PutOn(int set)
        {
            int was = worn[(int)Wardrobe.Cat.Outfit];
            if (set == was || set < 0 || set >= kept.GetLength(0)) return;
            for (int k = 0; k < Dyes.Length; k++) { kept[was, k] = worn[(int)Dyes[k]]; worn[(int)Dyes[k]] = kept[set, k]; }
            worn[(int)Wardrobe.Cat.Outfit] = set;
        }

        /// <summary>What a set of clothes is worn with (the jacket, trousers or boots row), now or when it was last worn.</summary>
        public static int SetWorn(int set, Wardrobe.Cat c)
        {
            Load();
            int k = System.Array.IndexOf(Dyes, c);
            if (k < 0 || set < 0 || set >= kept.GetLength(0)) return 0;
            return set == worn[(int)Wardrobe.Cat.Outfit] ? worn[(int)c] : kept[set, k];
        }

        /// <summary>The closet's try-on: this set of clothes on an outfit, in the colours it was last worn in.</summary>
        public static void TryOutfit(int[] o, int set)
        {
            Load();
            if (o == null || set < 0 || set >= kept.GetLength(0)) return;
            bool on = set == worn[(int)Wardrobe.Cat.Outfit];
            for (int k = 0; k < Dyes.Length; k++) o[(int)Dyes[k]] = on ? worn[(int)Dyes[k]] : kept[set, k];
            o[(int)Wardrobe.Cat.Outfit] = set;
        }

        /// <summary>What the rider rides. Every ride is free and there from the start: it is a way to play, not a prize.</summary>
        public static RideKind Ride { get { Load(); return (RideKind)ride; } }

        public static void PickRide(RideKind k)
        {
            Load();
            ride = Mathf.Clamp((int)k, 0, Rides.Count - 1);
            Save(); Changed?.Invoke();
        }

        // ---------------------------------------------------------------- what the upgrades do (the hooks)

        /// <summary>Multiplier on the edge's grip (Rider.GripLimit and Rider.Grip).</summary>
        public static float GripScale => 1f + 0.035f * Level(Up.Grip);   // ten levels, up to +35%
        /// <summary>Falls allowed in a run, given the base rule.</summary>
        public static int Falls(int baseFalls) => baseFalls + Level(Up.Helmet);
        /// <summary>Multiplier on how long power-ups last.</summary>
        public static float PowerUpTime => 1f + 0.1f * Level(Up.Fizz);   // ten levels, up to twice as long
        /// <summary>Extra metres between you and the avalanche at the start.</summary>
        public static float HeadStart => 6f * Level(Up.HeadStart);   // ten levels, up to 60 m

        static float gripLimit0 = -1f, grip0;
        /// <summary>Set the rider's tuning from the upgrades (called as a run starts).</summary>
        public static void ApplyRider()
        {
            if (gripLimit0 < 0f) { gripLimit0 = Rider.GripLimit; grip0 = Rider.Grip; }   // the stock values, once
            Rider.GripLimit = gripLimit0 * GripScale;
            Rider.Grip = grip0 * GripScale;
        }

        // ---------------------------------------------------------------- what a run earns

        public const int ScoreKnee = 50000;
        public struct Reward { public int fromScore, fromDistance, fromTricks, fromFinish, fromMissions, tricks; public int Total => fromScore + fromDistance + fromTricks + fromFinish + fromMissions; }

        /// <summary>Points for a finished run. Mission rewards were paid as they completed; they are listed, not paid twice.</summary>
        public static Reward EndRun(int score, float metres, int tricks, bool finished, int missionPoints)
        {
            var r = new Reward
            {
                // (a fiftieth of the score, as ever, up to 50,000; a long combo can bank several times that in one go now, and
                // past it the camp pays a 250th, so one jackpot run is a good day and not the whole shop)
                fromScore = Mathf.Min(Mathf.Max(0, score), ScoreKnee) / 50 + Mathf.Max(0, score - ScoreKnee) / 250,
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
            ride = 0;
            for (int c = 0; c < Wardrobe.Count; c++) { worn[c] = 0; owned[c] = 1; }
            System.Array.Clear(kept, 0, kept.Length);
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
