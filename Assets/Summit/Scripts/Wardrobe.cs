using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The closet's stock: what the hero can wear, by category, with prices in points. Pure data; what is owned and
    /// worn is kept by Progress, the closet screen is MetaUi's, the try-on studio is ClosetStage's, and RiderBody.Wear
    /// puts an outfit on.
    ///
    /// Everything here is paint: the hero has one set of clothes and gear (Tools/MakeHero.py), so a jacket is a colour
    /// and maybe a pattern (Summit/SnowyCloth draws the patterns on the rest pose), and the helmet, goggles, gloves,
    /// boots and pack are swatches in the gear palette. The first item of a category is what you start with.
    /// The first six jackets and boards are the ones sold before the closet, at their old prices and indices, so old
    /// saves keep what they bought and wore.
    /// </summary>
    public static class Wardrobe
    {
        // (Rider and Outfit were added after the rest, at the end, so the saved keys and the gear's places in the
        // palette keep their numbers: who you ride as, and which whole set of clothes they have on)
        // (Graphic, the board's and skis' printed top sheets, came after those, at the end too)
        public enum Cat { Jacket, Pants, Helmet, Goggles, Gloves, Boots, Pack, Board, Ride, Rider, Outfit, Graphic }
        public const int Count = 12;
        public enum Pattern { Plain, Stripes, Camo, Block, Plaid, Nordic }
        /// <summary>The closet's tabs, in the order shown: who you are and what set of clothes first, then their colours.</summary>
        public static readonly Cat[] Order = { Cat.Rider, Cat.Outfit, Cat.Jacket, Cat.Pants, Cat.Helmet, Cat.Goggles, Cat.Gloves, Cat.Boots, Cat.Pack, Cat.Board, Cat.Graphic, Cat.Ride };

        public class Item
        {
            public string name;
            public int cost;
            public Color a, b, c;            // main colour; second (a pattern's, or the gear's trim); a third (the pack's accent)
            public Pattern pattern;
            public float scale = 7f, at = 1.3f;   // the pattern's repeats a metre; a colour block's height (negative: below it)
            public float smooth = -1f;       // sheen, 0..1 (-1: the garment's own)
            public float metal;              // boards and graphics only
            public string art;               // graphics: the design's texture (Resources/BoardArt/art)
            public int tier;                 // graphics: 0 the everyday ones, 1 premium, 2 rare
            public bool Patterned => pattern != Pattern.Plain;
        }

        static Color C(float r, float g, float b) => new Color(r, g, b);
        // the gear's starting colours, as MakeHero.py's palette has them: linear values, which the palette's PNG
        // stores sRGB-encoded (every colour here is as it looks on screen, the way the palette texture keeps it)
        static Color L(float r, float g, float b) => new Color(r, g, b).gamma;
        static readonly Color Keep = new Color(0f, 0f, 0f, 0f);   // (a trim left as the palette has it)
        static Item I(string n, int cost, Color a) => new Item { name = n, cost = cost, a = a, b = Keep, c = Keep };
        static Item I(string n, int cost, Color a, Color b) => new Item { name = n, cost = cost, a = a, b = b, c = Keep };
        static Item P(string n, int cost, Color a, Color b, Pattern p, float scale = 7f, float at = 1.3f)
            => new Item { name = n, cost = cost, a = a, b = b, c = Keep, pattern = p, scale = scale, at = at };

        // jackets and boards: the first six kept as they were sold (100 + 50 a step), the rest added with the closet
        static readonly Item[] jackets =
        {
            I("ORANGE", 0, C(0.93f, 0.31f, 0.14f)), I("COBALT", 150, C(0.12f, 0.45f, 0.92f)), I("SUNSHINE", 200, C(0.98f, 0.82f, 0.10f)),
            I("PINE", 250, C(0.15f, 0.62f, 0.32f)), I("MAGENTA", 300, C(0.85f, 0.22f, 0.62f)), I("STEALTH", 350, C(0.10f, 0.11f, 0.13f)),
            I("GLACIER", 300, C(0.62f, 0.82f, 0.95f)), I("CRIMSON", 300, C(0.72f, 0.08f, 0.12f)), I("OLIVE", 300, C(0.36f, 0.40f, 0.20f)),
            I("SAND", 350, C(0.82f, 0.70f, 0.50f)), I("TEAL", 350, C(0.05f, 0.55f, 0.58f)), I("ARCTIC", 400, C(0.93f, 0.94f, 0.96f)),
            P("PATROL STRIPE", 600, C(0.80f, 0.10f, 0.10f), C(0.95f, 0.95f, 0.95f), Pattern.Stripes, 6f),
            P("COLOUR BLOCK", 650, C(0.10f, 0.16f, 0.36f), C(0.98f, 0.55f, 0.08f), Pattern.Block),
            P("ALPINE CAMO", 750, C(0.30f, 0.36f, 0.22f), C(0.55f, 0.50f, 0.34f), Pattern.Camo, 6f),
            P("SNOW CAMO", 750, C(0.86f, 0.88f, 0.92f), C(0.55f, 0.60f, 0.68f), Pattern.Camo, 6f),
            P("LUMBERJACK", 800, C(0.70f, 0.08f, 0.08f), C(0.08f, 0.08f, 0.10f), Pattern.Plaid, 9f),
            new Item { name = "GOLD RUSH", cost = 1500, a = C(0.95f, 0.72f, 0.20f), b = Keep, c = Keep, smooth = 0.85f },
            // (the knit of Breanna's fleece, for any jacket: cream with paler diamonds, zigzags and dots in bands)
            P("NORDIC KNIT", 700, C(0.84f, 0.78f, 0.66f), C(0.97f, 0.95f, 0.90f), Pattern.Nordic, 8f),
        };

        /// <summary>Her name as the player sees it (one place, so a change of spelling is one edit).</summary>
        public const string Breanna = "BREANNA";

        // Who you ride as, and which set of clothes they have on. All free, all there from the start: they are who
        // you are, not prizes. The summit rider comes as a man or a woman (the same kit, cut for each); Val, Breanna and
        // Paul each bring their own clothes, and anyone can wear anyone's. (Rows are only ever added at the end: saves
        // keep the row's number.)
        static readonly Item[] riders =
        {
            I("SUMMIT RIDER · MALE", 0, C(0.93f, 0.31f, 0.14f)), I("SUMMIT RIDER · FEMALE", 0, C(0.98f, 0.55f, 0.35f)),
            I("PAJAMA BUDDY VAL", 0, C(0.10f, 0.25f, 0.80f)), I(Breanna, 0, C(0.93f, 0.90f, 0.82f)),   // (she arrives in her fleece now, not her pyjamas; Val keeps the name he was given)
            I("PAUL", 0, C(0.06f, 0.06f, 0.07f)),
        };
        /// <summary>The RIDER row that is Paul (a skier: picking him puts him on skis).</summary>
        public const int PaulRow = 4;
        static readonly Item[] outfits =
        {
            I("SUMMIT KIT", 0, C(0.93f, 0.31f, 0.14f)), I("VAL'S PYJAMAS", 0, C(0.07f, 0.09f, 0.30f)), I(Breanna + "'S PYJAMAS", 0, C(0.66f, 0.07f, 0.13f)),
            I("PAUL'S PUFFER", 0, C(0.60f, 0.06f, 0.06f)), I("PAUL'S HOODIE", 0, C(0.50f, 0.51f, 0.53f)), I(Breanna + "'S FLEECE", 0, C(0.93f, 0.90f, 0.82f)),
        };
        /// <summary>The file each set of clothes is taken from (Resources/Rider/...; the summit kit's is cut per build).</summary>
        public static readonly string[] OutfitModels = { "Hero", "Buddy", "BreannaPJ", "Paul", "PaulB", "Breanna" };
        /// <summary>Whose each set of clothes is (RiderBody's people: 0 the summit rider, 1 Val, 2 Breanna, 3 Paul): on anyone
        /// else they're fitted (a collar folded down, a hood tucked away; RiderBodyHero.FitShapes).</summary>
        public static readonly int[] OutfitOwner = { 0, 1, 2, 3, 3, 2 };
        // each set as it was made: what the first row of JACKETS, PANTS and BOOTS stands for while it is worn
        // (any other row dyes it, pattern and all)
        static readonly Item[] ownFleece =
        {
            null, I("VAL'S BLUE", 0, C(0.08f, 0.24f, 0.72f)),
            P(Breanna + "'S NORDIC", 0, C(0.86f, 0.81f, 0.70f), C(0.98f, 0.96f, 0.91f), Pattern.Nordic, 8f),
            I("BLACK PUFFER", 0, C(0.035f, 0.035f, 0.04f)), I("BLACK VEST", 0, C(0.035f, 0.035f, 0.04f)), I("CREAM FLEECE", 0, C(0.90f, 0.86f, 0.77f)),
        };
        static readonly Item[] ownPants =
        {
            null, I("POLAR BEARS", 0, C(0.07f, 0.09f, 0.30f)), I("RED PLAID", 0, C(0.66f, 0.07f, 0.13f)),
            I("RED SKI PANTS", 0, C(0.60f, 0.06f, 0.06f)), I("CHARCOAL", 0, C(0.09f, 0.09f, 0.10f)), I("BLACK", 0, C(0.025f, 0.025f, 0.03f)),
        };
        static readonly Item[] ownShoes =
        {
            null, I("WHITE SNEAKERS", 0, C(0.93f, 0.93f, 0.92f)), I("SLIPPER BOOTS", 0, C(0.66f, 0.52f, 0.37f)),
            I("BLUE SKI BOOTS", 0, C(0.10f, 0.26f, 0.58f)), I("BLUE SKI BOOTS", 0, C(0.10f, 0.26f, 0.58f)), I("SLIPPER BOOTS", 0, C(0.66f, 0.52f, 0.37f)),
        };
        /// <summary>How many sets of clothes there are.</summary>
        public static int OutfitCount => outfits.Length;

        /// <summary>The clothes a rider comes in (an OUTFIT row for a RIDER row): Val his pyjamas, Breanna her fleece, Paul his puffer.</summary>
        public static int OwnOutfit(int rider) => rider == 2 ? 1 : rider == 3 ? 5 : rider == PaulRow ? 3 : 0;

        /// <summary>
        /// An outfit as the one number the rider's model is built from (RiderBody.SetCharacter): whose head and hands
        /// (0 the summit rider, 1 Val, 2 Breanna, 3 Paul), + 10 x the set of clothes, + 100 when the summit kit is cut for
        /// a woman, + 1000 when pyjama trousers are dyed (plain cloth then, in place of their print).
        /// </summary>
        public static int Look(int[] o)
        {
            int r = o[(int)Cat.Rider], set = Mathf.Clamp(o[(int)Cat.Outfit], 0, outfits.Length - 1);
            int who = r == 2 ? 1 : r == 3 ? 2 : r == PaulRow ? 3 : 0;
            bool her = r == 1 || r == 3;
            return who + 10 * set + (her ? 100 : 0) + ((set == 1 || set == 2) && o[(int)Cat.Pants] != 0 ? 1000 : 0);
        }

        /// <summary>
        /// An item as it shows on a set of clothes. The first jacket, trousers and boots are the summit kit's own
        /// colours; on any other set the first row is that set as it was made instead.
        /// </summary>
        public static Item Shown(Cat c, int i, int outfit)
        {
            if (i == 0 && outfit > 0 && outfit < outfits.Length)
            {
                if (c == Cat.Jacket) return ownFleece[outfit];
                if (c == Cat.Pants) return ownPants[outfit];
                if (c == Cat.Boots) return ownShoes[outfit];
            }
            return Get(c, i);
        }

        /// <summary>Free and there from the start (who you are, what you ride): nothing to buy.</summary>
        public static bool Free(Cat c) => c == Cat.Ride || c == Cat.Rider || c == Cat.Outfit;

        static readonly Item[] pants =
        {
            I("CHARCOAL", 0, C(0.17f, 0.19f, 0.24f)), I("BLACK", 150, C(0.06f, 0.06f, 0.07f)), I("NAVY", 200, C(0.08f, 0.12f, 0.28f)),
            I("OLIVE", 200, C(0.30f, 0.33f, 0.18f)), I("SAND", 250, C(0.74f, 0.64f, 0.46f)), I("SKY", 300, C(0.36f, 0.62f, 0.88f)),
            I("RED", 300, C(0.70f, 0.10f, 0.10f)), I("WHITE", 350, C(0.90f, 0.91f, 0.93f)),
            P("GAITERED", 500, C(0.12f, 0.14f, 0.18f), C(0.95f, 0.55f, 0.10f), Pattern.Block, 7f, -0.45f),
            P("WOODLAND CAMO", 650, C(0.28f, 0.32f, 0.20f), C(0.48f, 0.42f, 0.28f), Pattern.Camo, 6f),
            P("TARTAN", 700, C(0.12f, 0.20f, 0.42f), C(0.15f, 0.42f, 0.22f), Pattern.Plaid, 9f),
        };

        // gear: the colour, then its trim (the helmet's vents, the goggles' frame, the gloves' cuffs)
        static readonly Item[] helmets =
        {
            I("WHITE", 0, L(0.90f, 0.91f, 0.93f), L(0.55f, 0.57f, 0.60f)), I("BLACK", 150, C(0.08f, 0.08f, 0.09f), C(0.30f, 0.31f, 0.34f)),
            I("RED", 250, C(0.80f, 0.12f, 0.10f)), I("ORANGE", 250, C(0.98f, 0.45f, 0.08f)), I("YELLOW", 250, C(0.98f, 0.82f, 0.12f)),
            I("BLUE", 250, C(0.12f, 0.40f, 0.88f)), I("MATTE OLIVE", 300, C(0.32f, 0.36f, 0.22f)),
            new Item { name = "GOLD", cost = 1200, a = C(0.92f, 0.70f, 0.22f), b = C(0.20f, 0.16f, 0.08f), c = Keep, smooth = 0.9f },
            new Item { name = "CHROME", cost = 1500, a = C(0.55f, 0.58f, 0.64f), b = C(0.15f, 0.16f, 0.18f), c = Keep, smooth = 0.97f },   // (darker than it reads: the shine brings it up)
        };

        static readonly Item[] goggles =
        {
            I("AMBER", 0, L(1.00f, 0.52f, 0.08f), L(0.05f, 0.05f, 0.06f)), I("SMOKE", 200, C(0.16f, 0.17f, 0.20f)), I("ROSE", 250, C(0.95f, 0.38f, 0.55f)),
            I("ICE BLUE", 300, C(0.30f, 0.70f, 1.00f)), I("EMERALD", 300, C(0.10f, 0.78f, 0.45f)), I("VIOLET", 300, C(0.55f, 0.30f, 0.95f)),
            I("WHITE FRAME", 350, L(1.00f, 0.52f, 0.08f), C(0.90f, 0.91f, 0.93f)),
            new Item { name = "GOLD MIRROR", cost = 900, a = C(1.00f, 0.78f, 0.25f), b = C(0.10f, 0.10f, 0.11f), c = Keep, smooth = 0.98f },
        };

        static readonly Item[] gloves =
        {
            I("GRAPHITE", 0, L(0.11f, 0.115f, 0.13f), L(0.16f, 0.17f, 0.19f)), I("ASH", 100, C(0.62f, 0.64f, 0.68f)), I("RED", 200, C(0.75f, 0.12f, 0.10f), C(0.20f, 0.06f, 0.06f)),
            I("ORANGE", 200, C(0.96f, 0.45f, 0.10f)), I("BLUE", 200, C(0.12f, 0.38f, 0.85f)), I("YELLOW", 250, C(0.98f, 0.80f, 0.15f)),
            I("WHITE", 250, C(0.90f, 0.91f, 0.93f), C(0.55f, 0.57f, 0.60f)), I("LEATHER", 400, C(0.45f, 0.27f, 0.14f), C(0.20f, 0.12f, 0.07f)),
        };

        static readonly Item[] boots =
        {
            I("TAUPE", 0, L(0.14f, 0.12f, 0.11f)), I("BLACK", 100, C(0.05f, 0.05f, 0.06f)), I("GREY", 150, C(0.36f, 0.37f, 0.40f)),
            I("TAN", 200, C(0.62f, 0.46f, 0.28f)), I("RED", 300, C(0.70f, 0.12f, 0.10f)), I("WHITE", 300, C(0.88f, 0.89f, 0.91f)),
        };

        static readonly Item[] packs =
        {
            new Item { name = "CHARCOAL", cost = 0, a = L(0.13f, 0.14f, 0.16f), b = L(0.20f, 0.21f, 0.24f), c = L(0.92f, 0.72f, 0.16f) },
            new Item { name = "RED", cost = 200, a = C(0.70f, 0.10f, 0.10f), b = C(0.50f, 0.08f, 0.08f), c = C(0.95f, 0.95f, 0.95f) },
            new Item { name = "BLUE", cost = 200, a = C(0.12f, 0.32f, 0.70f), b = C(0.09f, 0.22f, 0.50f), c = C(0.98f, 0.82f, 0.12f) },
            new Item { name = "OLIVE", cost = 200, a = C(0.30f, 0.33f, 0.18f), b = C(0.22f, 0.24f, 0.13f), c = C(0.95f, 0.45f, 0.08f) },
            new Item { name = "ORANGE", cost = 250, a = C(0.95f, 0.45f, 0.08f), b = C(0.70f, 0.32f, 0.06f), c = C(0.12f, 0.12f, 0.14f) },
            new Item { name = "WHITE", cost = 300, a = C(0.88f, 0.89f, 0.91f), b = C(0.66f, 0.68f, 0.72f), c = C(0.12f, 0.40f, 0.88f) },
        };

        static readonly Item[] boards =
        {
            I("BLUE", 0, C(0.10f, 0.45f, 0.90f)), I("RED", 150, C(0.90f, 0.12f, 0.12f)), I("WHITE", 200, C(0.95f, 0.95f, 0.97f)),
            I("LIME", 250, C(0.40f, 0.85f, 0.20f)), I("TANGERINE", 300, C(0.98f, 0.55f, 0.05f)), I("VIOLET", 350, C(0.55f, 0.30f, 0.85f)),
            I("BLACK", 300, C(0.06f, 0.06f, 0.07f)), I("NAVY", 300, C(0.08f, 0.12f, 0.30f)), I("MINT", 350, C(0.55f, 0.92f, 0.78f)),
            I("PINK", 350, C(0.98f, 0.50f, 0.70f)), I("ICE", 350, C(0.75f, 0.90f, 1.00f)),
            new Item { name = "GOLD", cost = 1200, a = C(0.95f, 0.72f, 0.22f), b = Keep, c = Keep, smooth = 0.85f, metal = 1f },
            new Item { name = "CHROME", cost = 1500, a = C(0.85f, 0.87f, 0.90f), b = Keep, c = Keep, smooth = 0.95f, metal = 1f },
        };

        // the board's graphics: printed top sheets and bases, also on the skis (and the poles and the tube's trim take
        // the design's rail colour). The first row is none: the board in the colour picked under BOARDS. Each one is
        // painted by Tools/MakeBoardArt.py; rows are only ever added at the end (saves keep the row's number).
        static Item G(string n, int cost, string art, int tier, Color chip, float smooth, float metal = 0f)
            => new Item { name = n, cost = cost, a = chip, b = Keep, c = Keep, art = art, tier = tier, smooth = smooth, metal = metal };
        static readonly Item[] graphics =
        {
            I("PLAIN", 0, C(0.10f, 0.45f, 0.90f)),
            G("SUNSET STACK", 500, "SunsetStack", 0, C(1.00f, 0.55f, 0.26f), 0.6f),
            G("TOPO", 400, "Topo", 0, C(0.12f, 0.24f, 0.18f), 0.45f),
            G("TIMBER", 550, "Timber", 0, C(0.79f, 0.56f, 0.32f), 0.35f),
            G("TIDE", 600, "Tide", 0, C(0.11f, 0.16f, 0.34f), 0.55f),
            G("RIDGELINE", 650, "Ridgeline", 0, C(0.55f, 0.42f, 0.62f), 0.55f),
            G("BACKCOUNTRY CAMO", 450, "Camo", 0, C(0.34f, 0.39f, 0.23f), 0.3f),
            G("PRIMARY", 600, "Primary", 0, C(0.84f, 0.16f, 0.16f), 0.55f),
            G("CHECKMATE", 400, "Checkmate", 0, C(0.07f, 0.07f, 0.07f), 0.6f),
            G("NORDIC", 500, "Nordic", 0, C(0.70f, 0.13f, 0.16f), 0.4f),
            G("POP DOTS", 450, "PopDots", 0, C(0.90f, 0.22f, 0.27f), 0.6f),
            G("VOLT", 550, "Volt", 0, C(0.97f, 0.91f, 0.20f), 0.65f),
            G("MELT", 1200, "Melt", 1, C(0.48f, 0.17f, 0.75f), 0.65f),
            G("FOX", 1400, "Fox", 1, C(0.91f, 0.45f, 0.16f), 0.55f),
            G("SNOW LEOPARD", 1100, "Leopard", 1, C(0.85f, 0.84f, 0.81f), 0.5f),
            G("KOI", 1300, "Koi", 1, C(0.89f, 0.34f, 0.18f), 0.65f),
            G("AURORA", 2800, "Aurora", 2, C(0.18f, 0.77f, 0.71f), 0.7f),
            G("GOLD LEAF", 3200, "GoldLeaf", 2, C(0.83f, 0.65f, 0.23f), 0.8f, 0.7f),
            G("PRISM", 4000, "Prism", 2, C(0.80f, 0.70f, 0.95f), 0.92f, 0.8f),
        };

        static Item[] rides;
        static Item[] RideItems()
        {
            if (rides != null) return rides;
            rides = new Item[Rides.Count];
            for (int k = 0; k < rides.Length; k++) rides[k] = I(Rides.Of((RideKind)k).Name, 0, Color.white);   // all free, all there from the start
            return rides;
        }

        public static Item[] Items(Cat c)
        {
            switch (c)
            {
                case Cat.Jacket: return jackets;
                case Cat.Pants: return pants;
                case Cat.Helmet: return helmets;
                case Cat.Goggles: return goggles;
                case Cat.Gloves: return gloves;
                case Cat.Boots: return boots;
                case Cat.Pack: return packs;
                case Cat.Board: return boards;
                case Cat.Rider: return riders;
                case Cat.Outfit: return outfits;
                case Cat.Graphic: return graphics;
                default: return RideItems();
            }
        }

        public static Item Get(Cat c, int i) { var it = Items(c); return it[Mathf.Clamp(i, 0, it.Length - 1)]; }

        /// <summary>The tab's name (the board's follows what you ride: its colour paints the skis or the toboggan too).</summary>
        public static string Title(Cat c)
        {
            switch (c)
            {
                case Cat.Jacket: return "JACKETS";
                case Cat.Pants: return "PANTS";
                case Cat.Helmet: return "HELMETS";
                case Cat.Goggles: return "GOGGLES";
                case Cat.Gloves: return "GLOVES";
                case Cat.Boots: return "BOOTS";
                case Cat.Pack: return "PACKS";
                case Cat.Board: return Progress.Ride == RideKind.Snowboard ? "BOARDS" : Rides.Of(Progress.Ride).Name;
                case Cat.Rider: return "RIDER";
                case Cat.Outfit: return "OUTFIT";
                case Cat.Graphic: return "GRAPHICS";
                default: return "RIDE";
            }
        }

        /// <summary>
        /// True when the rider is the previous model (the "summit_oldhero" switch): it has a jacket and a board to
        /// paint but none of the hero's gear, so the closet shows only jackets (plain ones), boards and the ride.
        /// </summary>
        public static bool Classic;

        public static bool Shows(Cat c) => !Classic || c == Cat.Jacket || c == Cat.Board || c == Cat.Ride;
        public static bool Shows(Cat c, int i) => Shows(c) && !(Classic && Items(c)[i].Patterned);

        /// <summary>What is worn now, one item a category.</summary>
        public static int[] Worn()
        {
            var o = new int[Count];
            for (int c = 0; c < Count; c++) o[c] = Progress.Worn((Cat)c);
            return o;
        }
    }
}
