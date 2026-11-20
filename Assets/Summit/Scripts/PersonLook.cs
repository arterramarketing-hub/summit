using UnityEngine;

namespace Summit
{
    public enum PersonGear { None, Snowboard, Skis }
    public enum PersonHead { Helmet, Beanie }
    public enum PersonHair { None, Ponytail, Pigtails, Long }

    /// <summary>
    /// What one of the mountain's people looks like: the hero's own body (Resources/Rider/Hero.fbx) made taller or
    /// shorter, broader or slighter, in clothes and gear from the closet's stock (Wardrobe), with a skin tone, hair
    /// showing under a helmet or a beanie, and a board or skis. Pure data: People builds the person, and anyone can
    /// fill one in by hand (a named character keeps the same look every day) or draw one from a seed.
    /// </summary>
    public class PersonLook
    {
        public string name;                 // a named character's (empty for the crowd)
        public bool female;
        public float height = 1f;           // on the hero's 1.77 m: 0.9 is 1.60 m, 1.06 is 1.88 m
        public float build = 1f;            // girth through the body and limbs: 0.92 slight .. 1.12 broad
        public float shoulders = 1f;        // across the shoulders, on top of the build
        public Color skin = new Color(0.91f, 0.79f, 0.70f), hair = new Color(0.22f, 0.14f, 0.09f);
        public PersonHair hairStyle;
        public PersonHead head;
        public Color beanie = new Color(0.75f, 0.12f, 0.12f), pompom = Color.white;
        public bool hasPompom;
        public bool faceShows;              // the neck warmer pulled down: the chin and cheeks show (skin)
        public Color gaiter = new Color(0.14f, 0.15f, 0.18f);
        public Wardrobe.Item jacket, pants, helmet, goggles, gloves, boots, board;
        public Wardrobe.Item pack;          // null: no backpack
        public PersonGear gear = PersonGear.Snowboard;

        /// <summary>The meshes this look needs (the cut, the head, hair, neck warmer and pack change their shape; colours don't).</summary>
        public int ShapeKey => (int)head | (hasPompom && head == PersonHead.Beanie ? 2 : 0) | ((int)hairStyle << 2) | (pack != null ? 16 : 0) | (faceShows ? 32 : 0) | (female ? 64 : 0);

        // ---------------------------------------------------------------- the crowd

        // skin tones, light to deep, as they look on screen (the gear palette keeps on-screen colours)
        public static readonly Color[] Skins =
        {
            new Color(0.96f, 0.82f, 0.72f), new Color(0.93f, 0.75f, 0.62f), new Color(0.86f, 0.65f, 0.50f), new Color(0.78f, 0.57f, 0.42f),
            new Color(0.66f, 0.46f, 0.32f), new Color(0.54f, 0.36f, 0.24f), new Color(0.42f, 0.27f, 0.18f), new Color(0.30f, 0.19f, 0.13f),
        };
        public static readonly Color[] Hairs =
        {
            new Color(0.07f, 0.06f, 0.06f), new Color(0.18f, 0.12f, 0.08f), new Color(0.33f, 0.21f, 0.12f), new Color(0.48f, 0.22f, 0.11f),
            new Color(0.60f, 0.46f, 0.28f), new Color(0.84f, 0.71f, 0.44f), new Color(0.90f, 0.86f, 0.76f), new Color(0.60f, 0.59f, 0.58f),
        };
        // knit colours for beanies (and their pompoms): what people really wear on a hill, muted and bright
        static readonly Color[] Knits =
        {
            new Color(0.75f, 0.12f, 0.12f), new Color(0.10f, 0.12f, 0.16f), new Color(0.88f, 0.86f, 0.80f), new Color(0.95f, 0.62f, 0.10f),
            new Color(0.15f, 0.33f, 0.62f), new Color(0.28f, 0.42f, 0.24f), new Color(0.55f, 0.55f, 0.57f), new Color(0.85f, 0.45f, 0.60f),
            new Color(0.45f, 0.30f, 0.20f), new Color(0.10f, 0.50f, 0.52f),
        };

        static float H(int seed, int k) => Slope.Hash01(seed, k, 9173);
        static int Pick(int seed, int k, int n) => Mathf.Min(n - 1, (int)(H(seed, k) * n));

        /// <summary>
        /// Someone off the mountain, the same every time for the same seed: men and women in about equal numbers,
        /// every skin tone, mostly helmets, plenty of beanies, a few packs, skis a little more often than boards.
        /// skiShare: how many of them are on skis (0..1).
        /// </summary>
        public static PersonLook Random(int seed, float skiShare = 0.55f)
        {
            var l = new PersonLook();
            l.female = H(seed, 1) < 0.48f;
            // heights by the usual spread (women about 1.64 m, men 1.77 m, each give or take 7 cm)
            float g = (H(seed, 2) + H(seed, 3) + H(seed, 4)) / 3f - 0.5f;   // roughly bell-shaped, -0.5..0.5
            l.height = (l.female ? 0.928f : 1.0f) + g * 0.12f;
            l.build = Mathf.Lerp(0.93f, 1.1f, Mathf.Pow(H(seed, 5), 1.4f)) * (l.female ? 0.97f : 1f);
            l.shoulders = l.female ? Mathf.Lerp(0.88f, 0.95f, H(seed, 6)) : Mathf.Lerp(0.98f, 1.06f, H(seed, 6));
            l.skin = Skins[Pick(seed, 7, Skins.Length)];
            l.hair = Hairs[Pick(seed, 8, l.skin.r < 0.6f ? 3 : Hairs.Length)];   // (darker skin: darker hair, mostly)
            float age = H(seed, 9);
            if (age > 0.88f) l.hair = Hairs[7];   // some grey
            l.head = H(seed, 10) < 0.7f ? PersonHead.Helmet : PersonHead.Beanie;
            l.beanie = Knits[Pick(seed, 11, Knits.Length)];
            l.pompom = H(seed, 12) < 0.5f ? Knits[Pick(seed, 13, Knits.Length)] : l.beanie * 0.85f + Color.white * 0.15f;
            l.hasPompom = H(seed, 14) < 0.55f;
            // hair that shows: most women's, some men's
            float hs = H(seed, 15);
            l.hairStyle = l.female ? (hs < 0.45f ? PersonHair.Ponytail : hs < 0.62f ? PersonHair.Pigtails : hs < 0.9f ? PersonHair.Long : PersonHair.None)
                                   : (hs < 0.12f ? PersonHair.Long : hs < 0.18f ? PersonHair.Ponytail : PersonHair.None);
            l.jacket = Item(Wardrobe.Cat.Jacket, seed, 20, 0.12f);
            l.pants = Item(Wardrobe.Cat.Pants, seed, 21, 0.15f);
            // trousers mostly dark or neutral; now and then the same colour as the jacket (a one-piece look)
            if (H(seed, 22) < 0.55f) l.pants = Wardrobe.Get(Wardrobe.Cat.Pants, Pick(seed, 23, 4));
            l.helmet = Item(Wardrobe.Cat.Helmet, seed, 24, 0.04f);
            l.goggles = Item(Wardrobe.Cat.Goggles, seed, 25, 0.06f);
            l.gloves = Item(Wardrobe.Cat.Gloves, seed, 26, 0f);
            l.boots = Item(Wardrobe.Cat.Boots, seed, 27, 0f);
            l.pack = H(seed, 28) < 0.18f ? Item(Wardrobe.Cat.Pack, seed, 29, 0f) : null;
            l.board = Item(Wardrobe.Cat.Board, seed, 30, 0.05f);
            l.gear = H(seed, 31) < skiShare ? PersonGear.Skis : PersonGear.Snowboard;
            // the neck warmer: up to the goggles on most, down round the neck on some; dark, or a colour
            l.faceShows = H(seed, 32) < 0.45f;
            float gc = H(seed, 33);
            l.gaiter = gc < 0.5f ? new Color(0.12f, 0.13f, 0.15f) : gc < 0.7f ? Knits[Pick(seed, 34, Knits.Length)] : gc < 0.85f ? l.jacket.a * 0.8f : new Color(0.5f, 0.52f, 0.55f);
            return l;
        }

        /// <summary>An item of a category: anything in stock, the dearest (gold, chrome) only rarely.</summary>
        static Wardrobe.Item Item(Wardrobe.Cat c, int seed, int k, float rare)
        {
            var items = Wardrobe.Items(c);
            for (int tries = 0; tries < 4; tries++)
            {
                var it = items[Pick(seed, k * 7 + tries, items.Length)];
                if (it.cost < 1000 || H(seed, k * 7 + tries + 50) < rare) return it;
            }
            return items[0];
        }
    }
}
