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
        public enum Cat { Jacket, Pants, Helmet, Goggles, Gloves, Boots, Pack, Board, Ride }
        public const int Count = 9;
        public enum Pattern { Plain, Stripes, Camo, Block, Plaid }

        public class Item
        {
            public string name;
            public int cost;
            public Color a, b, c;            // main colour; second (a pattern's, or the gear's trim); a third (the pack's accent)
            public Pattern pattern;
            public float scale = 7f, at = 1.3f;   // the pattern's repeats a metre; a colour block's height (negative: below it)
            public float smooth = -1f;       // sheen, 0..1 (-1: the garment's own)
            public float metal;              // boards only
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
        };

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
