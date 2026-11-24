using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The camp's lodge: a contemporary timber ski lodge after the user's photo (Tools/art/reference/lodge-reference.jpg),
    /// modelled in Blender by Tools/MakeLodge.py (Resources/Camp/Lodge.fbx), in place of the old Swiss chalet. One long
    /// shed roof rising from the low end (by the fire) to the high end, deep eaves on glulam rafters, a tall volume
    /// behind it with a glazed clerestory, a lower wing; dark board cladding, black-framed glass; a raised concrete
    /// terrace with wide steps down toward the fire and the gate. Inside: one open hall and the wing, the game's three
    /// stations (missions board, tune-up bench, wardrobe) and open floor for what comes next (lodge.md).
    ///
    /// This file builds it and the paved path with its lamp posts; Camp.cs calls in here where it used to build the
    /// chalet (Rebuild, Indoors, SetNight, the trees round it).
    /// </summary>
    public partial class Camp
    {
        /// <summary>The lodge (true) or the old chalet (false, for comparison shots).</summary>
        public static bool UseLodge = true;
        /// <summary>Where the lodge stands: its hall's middle at (dx from the run's centre, z), turned to face the gate.</summary>
        public const float LodgeDx = -7f, LodgeZ = -92f, LodgeYaw = 7f;
        // its outline in its own frame (MakeLodge.py): the hall and the wing, and everything with the terrace and eaves
        static readonly Rect LodgeHall = Rect.MinMaxRect(-11f, -7f, 11f, 7f), LodgeWing = Rect.MinMaxRect(-16f, -5.5f, -11f, 5f);
        static readonly Rect LodgeAll = Rect.MinMaxRect(-17.6f, -8.4f, 19.4f, 12.8f);   // (to x 19: the covered section under the roof's tail)
        const float LodgeTerraceEdge = 12.6f, LodgeRise = 0.17f, LodgeRun = 0.36f;
        /// <summary>The upper floors' level in the lodge's frame (MakeLodge.py's MY): the loft's, and the lookout's deck on the wing's roof.</summary>
        public const float LodgeUpper = 3.8f;
        const int LodgeSteps = 14;

        // the trees round the camp, with the lodge where the chalet was: those that stood where it now stands moved out
        // round its ends and back (the rest as Camp.Trees and Camp.Birches have them)
        // (the lookout's deck on the wing's roof looks out over the front and the high end: the trees that stood in its
        // view, in front of it and off the wing's end, moved out to the camp's edge on that side and back, framing it)
        static readonly Vector2[] LodgeTrees = { new Vector2(-29f, -99f), new Vector2(-19f, -104f), new Vector2(-6f, -107f), new Vector2(7f, -105f), new Vector2(19f, -101f),
                                                 new Vector2(21f, -104f), new Vector2(-31.8f, -83f), new Vector2(-32.4f, -66.5f), new Vector2(-30.5f, -104.5f), new Vector2(-31.2f, -74.5f),
                                                 new Vector2(-24f, -108f), new Vector2(13f, -110f), new Vector2(-31f, -54f),
                                                 new Vector2(-27f, -110f), new Vector2(17.3f, -86.6f) };   // (the last moved out past the covered section's end)
        static readonly Vector2[] LodgeBirches = { new Vector2(-30.6f, -78.5f), new Vector2(-32.8f, -94f), new Vector2(15.9f, -98.3f), new Vector2(-30.8f, -60.8f), new Vector2(-32.8f, -70.8f), new Vector2(17.2f, -92.0f) };   // (two moved out past it)

        readonly List<(Rect r, float lo, float hi)> lodgeInside = new List<(Rect, float, float)>();
        readonly List<(Vector3 lo, Vector3 hi)> lodgeLook = new List<(Vector3, Vector3)>();
        readonly Dictionary<string, Transform> lodgeTaps = new Dictionary<string, Transform>();
        readonly Dictionary<string, Vector3> lodgeSpotsAt = new Dictionary<string, Vector3>();
        readonly List<Vector3> lodgeStairs = new List<Vector3>();
        Material lodgeGlass, paint, paver;
        Texture2D palette;
        /// <summary>Test tools: the lodge's floor above the snow at the foot of its steps, and how many steps show.</summary>
        public float LodgeFrontDrop { get; private set; }
        public int LodgeStepsShowing { get; private set; }
        /// <summary>Test tools: lamp posts along the paved path.</summary>
        public int LampPosts { get; private set; }

        /// <summary>
        /// Room kept for the hot-chocolate corner (the hub session's CampCocoa station): in the wing, against its back
        /// wall, where you stand to make it in the lodge's frame (face -z, toward the wall). The wing is otherwise empty.
        /// </summary>
        public static readonly Vector3 LodgeCocoaAt = new Vector3(-13.5f, 0f, -3.7f);

        /// <summary>
        /// The places the camp's people use that are the lodge's: the camp's own (CampLife: the
        /// lodge's door, the waxing stand beside its steps) and Val and Breanna's way in and the window they look out of
        /// (CampCouple). Called as the lodge is built.
        /// </summary>
        void LodgeRepoint()
        {
            Vector3 L(float x, float z) => chalet.TransformPoint(new Vector3(x, 0f, z));
            float cy = ChaletYaw;
            var life = GetComponent<CampLife>();
            if (life != null)
            {
                life.SetPoint("door", L(4.5f, 6.2f), cy);
                var wax = L(-7.0f, 15.6f); wax.y = Slope.DrawnHeight(wax.x, wax.z);
                life.SetPoint("wax", wax, cy + 90f);
            }
            if (Couple != null)
            {
                var window = LodgeWindow;
                var front = L(0f, 16.5f); front.y = Slope.DrawnHeight(front.x, front.z);
                Couple.Repoint("ChaletFront", front);
                Couple.Repoint("ChaletSteps", L(0f, 12.2f));
                Couple.Repoint("ChaletTerrace", L(4.5f, 8.4f));
                Couple.Repoint("ChaletDoor", L(4.5f, 5.4f));
                Couple.Repoint("ChaletWindow", L(window.x, window.y));
                Couple.Repoint("ChaletView", L(window.x, 34f));
            }
        }
        /// <summary>Where Val and Breanna stand to look out of the big glass (clear of the lounge in front of it).</summary>
        public static readonly Vector2 LodgeWindow = new Vector2(-1.0f, 5.6f);

        /// <summary>
        /// Places for people in and round the lodge (the camp's NPCs), in its own frame: where they stand or sit (x, z on
        /// the floor; y the height of the seat above the lodge's floor, or of the loft's or the deck's floor, 0 standing
        /// in the hall), the way they face (degrees from +z, the front), and what for: "sit" (the lounge's
        /// seats, the café's stools and tables, the terrace's chairs and table), "browse" (the gear wall, the display
        /// tables and rails, the board walls and tree), "queue" (the board shop's counter), "staff" (behind it, and at
        /// the tuning bench), "stand" (the foyer, by the glass). camp.ChaletPoint turns one into the world.
        /// </summary>
        public static (string what, Vector3 at, float yaw)[] LodgePeopleSpots => PeopleSpots;

        static readonly (string, Vector3, float)[] Shared =
        {
            ("sit", new Vector3(-14.95f, 0.8f, 3.75f), 0f), ("sit", new Vector3(-14.1f, 0.8f, 3.75f), 0f), ("sit", new Vector3(-13.25f, 0.8f, 3.75f), 0f),
            ("sit", new Vector3(-12.4f, 0.8f, 3.75f), 0f), ("sit", new Vector3(-11.55f, 0.8f, 3.75f), 0f),
            ("sit", new Vector3(-15.1f, 0.48f, 0.7f), 90f), ("sit", new Vector3(-13.7f, 0.48f, 0.7f), -90f),
            ("sit", new Vector3(-13.3f, 0.48f, -0.9f), 90f), ("sit", new Vector3(-11.9f, 0.48f, -0.9f), -90f),
            ("sit", new Vector3(-10.9f, 0.38f, 10.0f), 90f), ("sit", new Vector3(-6.7f, 0.38f, 10.0f), -90f),
            ("sit", new Vector3(7.3f, 0.44f, 9.48f), 0f), ("sit", new Vector3(8.5f, 0.44f, 10.92f), 180f),
            ("browse", new Vector3(9.6f, 0f, -4.0f), 90f), ("browse", new Vector3(6.3f, 0f, -3.0f), 180f),
            ("queue", new Vector3(7.0f, 0f, -0.45f), 180f), ("queue", new Vector3(7.0f, 0f, 0.6f), 180f),
            ("staff", new Vector3(7.0f, 0f, -2.4f), 0f), ("staff", new Vector3(9.75f, 0f, 5.1f), 0f),
            ("stand", new Vector3(3.0f, 0f, 2.2f), 30f),
            // the wing's far end, by the hot chocolate (clear of where you stand to make it): room for two to stand and talk
            ("stand", new Vector3(-14.9f, 0f, -1.55f), 120f), ("stand", new Vector3(-13.95f, 0f, -2.1f), -60f),
            // the lookout's deck on the wing's roof: the stools at the front rail's ledge,
            // room to stand and talk by the viewer, a place at the rail
            ("sit", new Vector3(-15.55f, LodgeUpper + 0.8f, 5.73f), 0f), ("sit", new Vector3(-14.7f, LodgeUpper + 0.8f, 5.73f), 0f),
            ("sit", new Vector3(-13.85f, LodgeUpper + 0.8f, 5.73f), 0f), ("sit", new Vector3(-13.0f, LodgeUpper + 0.8f, 5.73f), 0f),
            ("stand", new Vector3(-15.6f, LodgeUpper, 3.3f), 60f), ("stand", new Vector3(-14.75f, LodgeUpper, 3.8f), -120f),
            ("stand", new Vector3(-11.9f, LodgeUpper, 5.95f), 0f),
        };
        /// <summary>The covered section's picnic tables (along x: benches at z +-0.72) and racks (x0, x1, z; facing +z), as MakeLodge.py builds them.</summary>
        public static readonly (float x, float z)[] CoveredTables = { (12.9f, -0.2f), (16.5f, -0.2f), (12.9f, 6.6f), (16.5f, 6.6f), (16.5f, 9.2f) };
        public static readonly (float x0, float x1, float z)[] CoveredRacks = { (11.5f, 14.2f, -6.2f), (15.4f, 18.0f, -6.2f), (11.5f, 14.2f, -3.3f), (15.4f, 18.0f, -3.3f) };
        public const float PicnicSeat = 0.44f;
        /// <summary>
        /// The hips' place from a picnic table's middle: toward the bench's front edge (its middle is 0.72 out, 0.3 deep).
        /// Feet on the floor at a bench's height the thighs fall from the hips to the knees: perched forward, they fall
        /// clear of the bench, not through its front edge.
        /// </summary>
        public const float PicnicHips = 0.61f;

        /// <summary>
        /// The covered section at the low end (under the roof's tail, MakeLodge.py's RACKS and TABLES): the picnic tables'
        /// seats (both benches, three a side; the hips toward the bench's front edge, PicnicHips), in front of
        /// each rack (browsing it), and room to stand and talk between the racks (the shell's).
        /// </summary>
        static readonly (string, Vector3, float)[] Covered = CoveredSpots();
        static (string, Vector3, float)[] CoveredSpots()
        {
            var l = new List<(string, Vector3, float)>();
            foreach (var (tx, tz) in CoveredTables)
                foreach (float sx in new[] { -0.55f, 0f, 0.55f })
                {
                    l.Add(("sit", new Vector3(tx + sx, PicnicSeat, tz - PicnicHips), 0f));
                    l.Add(("sit", new Vector3(tx + sx, PicnicSeat, tz + PicnicHips), 180f));
                }
            foreach (var (x0, x1, z) in CoveredRacks)
            {
                l.Add(("browse", new Vector3(x0 + 0.75f, 0f, z + 0.95f), 180f));
                l.Add(("browse", new Vector3(x1 - 0.75f, 0f, z + 0.95f), 180f));
            }
            l.Add(("stand", new Vector3(16.1f, 0f, -4.75f), 90f)); l.Add(("stand", new Vector3(17.3f, 0f, -4.75f), -90f));
            return l.ToArray();
        }

        /// <summary>
        /// The lodge's own people (LodgeFolk.cs), each at one of the places above: how they're there
        /// (table: on a picnic bench at the table; stool: at the café's counter with a mug; rack: choosing at a rack;
        /// chat: talking to the next one), where (the seat's height in y, as the spots), the
        /// way they face, and the gear they have with them. (mugchat: standing with a mug, talking to the next one.)
        /// </summary>
        public static readonly (string pose, Vector3 at, float yaw, PersonGear gear)[] LodgeFolkAt =
        {
            // the covered section: three at the first table, two across the next, two across the one in front (those
            // across from each other half a seat apart, so their knees and feet don't meet under it)
            ("table", new Vector3(12.35f, PicnicSeat, -0.81f), 0f, PersonGear.None), ("table", new Vector3(13.45f, PicnicSeat, -0.81f), 0f, PersonGear.None),
            ("table", new Vector3(12.9f, PicnicSeat, 0.41f), 180f, PersonGear.None),
            ("table", new Vector3(15.95f, PicnicSeat, -0.81f), 0f, PersonGear.None), ("table", new Vector3(16.5f, PicnicSeat, 0.41f), 180f, PersonGear.None),
            ("table", new Vector3(12.35f, PicnicSeat, 5.99f), 0f, PersonGear.None), ("table", new Vector3(12.9f, PicnicSeat, 7.21f), 180f, PersonGear.None),
            // at the racks: one choosing, a board under the arm; two talking between the racks, skis and a board
            ("rack", new Vector3(12.25f, 0f, -5.25f), 180f, PersonGear.Snowboard),
            ("chat", new Vector3(16.1f, 0f, -4.75f), 90f, PersonGear.Skis), ("chat", new Vector3(17.3f, 0f, -4.75f), -90f, PersonGear.Snowboard),
            // the café in the wing: two at the counter with their hot chocolate, looking out; two with mugs standing
            // talking at the far end, by where it's made
            ("stool", new Vector3(-14.95f, 0.8f, 3.69f), 0f, PersonGear.None), ("stool", new Vector3(-14.1f, 0.8f, 3.69f), 0f, PersonGear.None),   // (the hips at the stools' backs: the knees under the counter)
            ("mugchat", new Vector3(-14.9f, 0f, -1.55f), 120f, PersonGear.None), ("mugchat", new Vector3(-13.95f, 0f, -2.1f), -60f, PersonGear.None),
            // out on the lookout's deck: two on the stools at the front rail with their hot chocolate, looking down the
            // mountain; two standing with mugs by the viewer, talking
            ("stool", new Vector3(-14.7f, LodgeUpper + 0.8f, 5.73f), 0f, PersonGear.None), ("stool", new Vector3(-13.85f, LodgeUpper + 0.8f, 5.73f), 0f, PersonGear.None),
            ("mugchat", new Vector3(-15.6f, LodgeUpper, 3.3f), 60f, PersonGear.None), ("mugchat", new Vector3(-14.75f, LodgeUpper, 3.8f), -120f, PersonGear.None),
        };

        static readonly (string, Vector3, float)[] PeopleSpots =
            // the lounge round the fire (a pair of armchairs at each end of it) and facing the front glass; the snug
            // and the café's tables under the loft; the loft's outfitters
            Join(Join(Shared, Covered), new[] {
                ("sit", new Vector3(-3.2f, 0.45f, -4.52f), 0f), ("sit", new Vector3(-1.8f, 0.45f, -4.52f), 0f), ("sit", new Vector3(-0.4f, 0.45f, -4.52f), 0f),
                ("sit", new Vector3(-3.2f, 0.45f, -0.9f), 180f), ("sit", new Vector3(-1.8f, 0.45f, -0.9f), 180f), ("sit", new Vector3(-0.4f, 0.45f, -0.9f), 180f),
                ("sit", new Vector3(-5.35f, 0.45f, -3.65f), 72f), ("sit", new Vector3(-5.35f, 0.45f, -1.75f), 108f),
                ("sit", new Vector3(1.75f, 0.45f, -3.65f), -72f), ("sit", new Vector3(1.75f, 0.45f, -1.75f), -108f),
                ("sit", new Vector3(-3.3f, 0.45f, 3.0f), 0f), ("sit", new Vector3(-2.0f, 0.45f, 3.0f), 0f), ("sit", new Vector3(-0.7f, 0.45f, 3.0f), 0f),
                ("sit", new Vector3(-4.6f, 0.45f, 3.9f), 64f), ("sit", new Vector3(0.6f, 0.45f, 3.9f), -64f),
                ("sit", new Vector3(-9.3f, 0.45f, -5.15f), 0f), ("sit", new Vector3(-8.1f, 0.45f, -5.15f), 0f), ("sit", new Vector3(-9.6f, 0.45f, -2.6f), 180f),
                ("sit", new Vector3(-9.9f, 0.45f, 3.6f), 90f), ("sit", new Vector3(-7.7f, 0.45f, 5.6f), -90f),
                ("browse", new Vector3(6.4f, 0f, -5.6f), 180f),
                ("stand", new Vector3(2.3f, 0f, 4.4f), 0f), ("stand", new Vector3(-4.4f, 0f, 5.9f), 0f),
                ("browse", new Vector3(-9.5f, 3.8f, -5.6f), 180f), ("browse", new Vector3(-8.0f, 3.8f, -5.6f), 180f), ("browse", new Vector3(-8.6f, 3.8f, -1.6f), 180f),
                ("browse", new Vector3(-8.6f, 3.8f, 2.6f), 0f), ("browse", new Vector3(-8.6f, 3.8f, 4.6f), 180f) });

        static (string, Vector3, float)[] Join((string, Vector3, float)[] a, (string, Vector3, float)[] b)
        {
            var r = new (string, Vector3, float)[a.Length + b.Length];
            a.CopyTo(r, 0); b.CopyTo(r, a.Length);
            return r;
        }

        /// <summary>Test tools: where a station is used (its spot), or the world origin if there is none.</summary>
        public Vector3 StationPoint(Station st)
        {
            foreach (var s in spots) if (s.st == st) return s.at;
            return Vector3.zero;
        }

        /// <summary>Test tools: every spot a station is used from (the upgrades has two in the lodge: the board shop's counter, the tuning bench).</summary>
        public List<Vector3> StationPoints(Station st)
        {
            var l = new List<Vector3>();
            foreach (var s in spots) if (s.st == st) l.Add(s.at);
            return l;
        }

        /// <summary>Test tools: the foot of the lodge's (or chalet's) front steps.</summary>
        public Vector3 StairsFoot => stairsFoot;

        /// <summary>Test tools: where you stand on the lookout.</summary>
        public Vector3 StationPointLookout() => lookFrame != null ? lookFrame.position : Vector3.zero;

        /// <summary>Test tools: the walking's solid boxes (centre and half size on the ground, turn in degrees, the heights they block).</summary>
        public List<(Vector2 c, Vector2 h, float yaw, float lo, float hi)> SolidBoxes() => BoxList(false);
        /// <summary>Test tools: the floors (lo is the top at the box's -z end, hi the drop to its +z end, for steps).</summary>
        public List<(Vector2 c, Vector2 h, float yaw, float lo, float hi)> FloorBoxes() => BoxList(true);

        List<(Vector2, Vector2, float, float, float)> BoxList(bool floors)
        {
            var l = new List<(Vector2, Vector2, float, float, float)>();
            foreach (var b in boxes)
                if (b.floor == floors && (b.owner == null || b.owner.activeInHierarchy))
                    l.Add((b.c, b.h, Mathf.Atan2(b.sin, b.cos) * Mathf.Rad2Deg, floors ? b.top : b.lo, floors ? b.drop : b.hi));
            return l;
        }

        /// <summary>Test tools: would a walker standing at w (on whatever floor is there) be pushed out of something solid.</summary>
        public bool WalkBlocked(Vector3 w, float r = 0.38f, float above = 0f)
        {
            float feet = FloorHeight(new Vector3(w.x, (chalet != null ? chalet.position.y : w.y) + above, w.z), out _);   // (above: coming from that high, a mezzanine's floor)
            var p2 = new Vector2(w.x, w.z);
            foreach (var b in boxes)
            {
                if (b.floor || (b.owner != null && !b.owner.activeInHierarchy)) continue;
                if (b.lo > feet + 1.7f || b.hi < feet + 0.1f) continue;
                if ((PushOut(b, p2, r) - p2).sqrMagnitude > 1e-8f) return true;
            }
            return false;
        }

        /// <summary>Test tools: the height a walker coming from height `from` stands at here, and whether that is the snow.</summary>
        public float FloorHeight(Vector3 at, out bool snow)
        {
            bool was = onSnow;
            float y = FloorAt(at, at.y);
            snow = onSnow; onSnow = was;
            return y;
        }

        /// <summary>A point in the lodge's own frame is inside it (the hall or the wing: under the wing's ceiling, not out on the deck over it).</summary>
        bool LodgeInsideAt(Vector3 l)
        {
            var p = new Vector2(l.x, l.z);
            foreach (var (r, lo, hi) in lodgeInside) if (r.Contains(p) && l.y > lo - 0.5f && l.y < hi) return true;
            return false;
        }

        /// <summary>Standing at the front of the lookout's deck on the wing's roof (LOOK_deck): the camera takes in the mountain, as at the camp's lookout.</summary>
        bool LodgeLookoutAt(Vector3 world)
        {
            if (chalet == null || !UseLodge) return false;
            var l = chalet.InverseTransformPoint(world);
            foreach (var (lo, hi) in lodgeLook)
                if (l.x >= lo.x && l.x <= hi.x && l.z >= lo.z && l.z <= hi.z && l.y > lo.y - 0.3f && l.y < hi.y) return true;
            return false;
        }

        /// <summary>A circle on the ground (world) is clear of the lodge and its terrace (for snow mounds and trees).</summary>
        bool LodgeClear(Vector3 w, float radius)
        {
            if (chalet == null) return true;
            var l = chalet.InverseTransformPoint(w);
            var r = LodgeAll;
            r.xMin -= radius; r.yMin -= radius; r.xMax += radius; r.yMax += radius;
            return !r.Contains(new Vector2(l.x, l.z));
        }

        // ---------------------------------------------------------------- the ground under it

        static readonly float lodgeCos = Mathf.Cos(LodgeYaw * Mathf.Deg2Rad), lodgeSin = Mathf.Sin(LodgeYaw * Mathf.Deg2Rad);
        const float PadFade = 9f, TerraceRise = 1.0f;

        /// <summary>
        /// The lodge's level pad, for Slope.Summit: how far the summit's ground at (a: x from the run's centre, z) is
        /// levelled for the lodge (1 under it and its terrace, easing to 0 nine metres out), and how far below the
        /// floor's ground it is wanted there: 0 under the house, falling across the terrace (which hides it) so the
        /// ground in front is a metre below the floor (the terrace's front wall and six or seven steps, as in the photo),
        /// then on down at the plateau's own slope. (The plateau rises 7 cm a metre toward the back and wanders 40 cm
        /// either way; set on its highest point, the terrace stood two metres above the snow in front.)
        /// </summary>
        public static float LodgePad(float a, float z, out float lower)
        {
            lower = 0f;
            if (!UseLodge) return 0f;
            float dx = a - LodgeDx, dz = z - LodgeZ;
            float lx = dx * lodgeCos - dz * lodgeSin, lz = dx * lodgeSin + dz * lodgeCos;   // into the lodge's own frame
            float ex = Mathf.Max(0f, Mathf.Max(LodgeAll.xMin - lx, lx - LodgeAll.xMax)), ez = Mathf.Max(0f, Mathf.Max(LodgeAll.yMin - lz, lz - LodgeAll.yMax));
            float d = Mathf.Sqrt(ex * ex + ez * ez);
            if (d >= PadFade) return 0f;
            float u = Mathf.Clamp01((lz - 7f) / (LodgeTerraceEdge - 7f));
            lower = TerraceRise * u * u * (3f - 2f * u) + 0.07f * Mathf.Max(0f, lz - LodgeTerraceEdge);
            float w = 1f - d / PadFade;
            return w * w * (3f - 2f * w);
        }

        void Lodge(Transform t)
        {
            t.name = "Lodge";
            chalet = t;
            lodgeInside.Clear(); lodgeStairs.Clear(); lodgeLook.Clear();
            // the floor sits level on the highest snow under the lodge and its terrace; the concrete base and the
            // terrace's walls run down into the rest (the hill rises behind it, so its front stands proud: the steps)
            float baseY = float.MinValue;
            for (float x = LodgeAll.xMin; x <= LodgeAll.xMax + 0.01f; x += 1.6f)
                for (float z = LodgeAll.yMin; z <= LodgeTerraceEdge + 0.01f; z += 1.6f)
                {
                    var w = t.TransformPoint(new Vector3(x, 0f, z));
                    baseY = Mathf.Max(baseY, Slope.DrawnHeight(w.x, w.z));
                }
            t.position = new Vector3(t.position.x, baseY + 0.12f, t.position.z);
            // the shell, then its inside (LodgeInside), both in the lodge's own frame
            var mats = new Dictionary<string, Material>();
            lodgeTaps.Clear(); lodgeSpotsAt.Clear();
            if (!LodgePart(t, "Camp/Lodge", "Model", mats)) return;
            LodgePart(t, "Camp/LodgeInside", "Inside", mats);
            // the stations: each spot with its own tap target (the board shop's counter and the tuning bench both open
            // the upgrades: "Upgrades" and "Upgrades_2")
            foreach (var kv in lodgeSpotsAt)
            {
                var st = kv.Key.StartsWith("Missions") ? Station.Missions : kv.Key.StartsWith("Upgrades") ? Station.Upgrades : Station.Style;
                spots.Add(new Spot { st = st, at = kv.Value, radius = 1.7f, root = lodgeTaps.TryGetValue(kv.Key, out var tap) ? tap : t });
            }
            LodgeFinish(t);
        }

        /// <summary>One of the lodge's model files under its frame: its materials the game's, its helper boxes read and dropped.</summary>
        bool LodgePart(Transform t, string res, string name, Dictionary<string, Material> mats)
        {
            var prefab = Resources.Load<GameObject>(res);
            if (prefab == null) { Debug.LogWarning("Camp: Resources/" + res + ".fbx is missing"); return false; }
            var model = Instantiate(prefab, t, false).transform;
            model.name = name;
            model.localPosition = Vector3.zero;
            var front = Find(model, "MARK_front");
            if (front != null)
            {
                var fl = t.InverseTransformPoint(front.GetComponent<Renderer>().bounds.center); fl.y = 0f;
                float turn = fl.sqrMagnitude > 0.01f ? Vector3.SignedAngle(fl, Vector3.forward, Vector3.up) : 0f;
                if (Mathf.Abs(turn) > 1f) { Debug.LogWarning("Camp: lodge came in turned " + turn.ToString("0") + " degrees"); model.RotateAround(t.position, t.up, turn); }
            }
            Physics.SyncTransforms();
            var helpers = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("__")) { helpers.Add(r); continue; }
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = LodgeMat(ms[i] != null ? ms[i].name.Split(' ')[0] : r.name, mats);
                r.sharedMaterials = ms;
                var kind = r.name.Split('_')[0];   // ("Glulam_in": the inside's glulam, the same material)
                bool flat = kind.StartsWith("Glass") || kind == "LED" || kind == "Ember" || kind == "Chalk" || kind.StartsWith("Note") || kind.StartsWith("Sign");
                r.shadowCastingMode = flat ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var r in helpers)
            {
                var b = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
                for (int c = 0; c < 8; c++)
                {
                    var p = t.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
                LodgeHelper(t, r.name.Substring(0, r.name.IndexOf("__")), lo, hi);
                if (Application.isPlaying) Destroy(r.gameObject); else DestroyImmediate(r.gameObject);
            }
            return true;
        }

        /// <summary>The lodge built: the foot of its steps, the camp's other things that hang off it, its people's points.</summary>
        void LodgeFinish(Transform t)
        {
            // the way down the main steps: where they meet the snow (the rest of the flight is buried)
            var top = lodgeStairs.Count > 0 ? lodgeStairs[0] : Vector3.forward * LodgeTerraceEdge;
            foreach (var s in lodgeStairs) if (Mathf.Abs(s.x) < Mathf.Abs(top.x)) top = s;   // (the main flight, by the door; the other is by the wing)
            float footD = LodgeSteps * LodgeRun;
            LodgeStepsShowing = LodgeSteps;
            for (int k = 1; k <= LodgeSteps; k++)
            {
                var w = t.TransformPoint(new Vector3(top.x, 0f, top.z + (k - 0.5f) * LodgeRun));
                if (t.position.y - LodgeRise * k <= Slope.DrawnHeight(w.x, w.z) + 0.04f) { footD = k * LodgeRun; LodgeStepsShowing = k - 1; break; }
            }
            var footW = t.TransformPoint(new Vector3(top.x, 0f, top.z + footD + 0.5f));
            LodgeFrontDrop = t.position.y - Slope.DrawnHeight(footW.x, footW.z);
            if (LodgeStepsShowing >= LodgeSteps) Debug.LogWarning($"Camp: the lodge's steps don't reach the snow ({LodgeFrontDrop:0.00} m below the floor)");
            stairsFoot = new Vector3(footW.x, Slope.DrawnHeight(footW.x, footW.z), footW.z);
            lodgeApex = t.TransformPoint(new Vector3(1f, 6.5f, 0f));
            // the string of lights runs along the front eave, under its edge, to where it kinks onto its tail
            deckR = t.TransformPoint(new Vector3(11f, 2.62f, 10.06f));
            deckL = t.TransformPoint(new Vector3(-3f, 6.95f, 10.06f));
            LodgeRepoint();
        }

        /// <summary>One of the lodge's helper boxes (MakeLodge.py): its own (the inside, the stairs, its lamps), or the chalet's kinds.</summary>
        void LodgeHelper(Transform t, string name, Vector3 lo, Vector3 hi)
        {
            Vector3 c = (lo + hi) * 0.5f;
            if (name.StartsWith("TAP_"))
            {
                var g = new GameObject(name).transform;
                g.SetParent(t, false); g.localPosition = c;
                g.gameObject.AddComponent<BoxCollider>().size = hi - lo;
                lodgeTaps[name.Substring(4)] = g;
                return;
            }
            if (name.StartsWith("SPOT_")) { lodgeSpotsAt[name.Substring(5)] = t.TransformPoint(c); return; }
            if (name.StartsWith("AREA_")) { lodgeInside.Add((Rect.MinMaxRect(lo.x, lo.z, hi.x, hi.z), lo.y, hi.y)); return; }
            if (name.StartsWith("LOOK_")) { lodgeLook.Add((lo, hi)); return; }
            if (name.StartsWith("STEP_")) { lodgeStairs.Add(c); return; }
            if (name.StartsWith("LIGHT_"))
            {
                // indoor lamps stay on (dimmer) by day; the porch and the fire table's light come up at dusk
                switch (name)
                {
                    case "LIGHT_hall": Lamp(t, c, 18f, 2.6f, 0.45f); break;
                    case "LIGHT_low": Lamp(t, c, 11f, 1.7f, 0.45f); break;
                    case "LIGHT_fire": Lamp(t, c, 8f, 2.0f, 0.5f).color = new Color(1f, 0.72f, 0.45f); break;
                    case "LIGHT_wing": Lamp(t, c, 8f, 1.5f, 0.45f); break;
                    case "LIGHT_porch": Lamp(t, c, 14f, 2.6f); break;
                    case "LIGHT_firetable": Lamp(t, c, 7f, 1.8f).color = new Color(1f, 0.72f, 0.45f); break;
                    case "LIGHT_deck": Lamp(t, c, 8f, 1.6f).color = new Color(1f, 0.76f, 0.5f); break;   // (the lookout's deck, at dusk)
                }
                return;
            }
            if (name.StartsWith("RMP"))
            {
                // the flight: its treads' middles on the slope (half a riser under the terrace at its top). RMP_ climbs
                // toward -z (its top there), RMPS_ toward +z, RMPW_ toward -x, RMPE_ toward +x: a frame turned so its -z
                // is the way up
                char way = name.Length > 3 ? name[3] : '_';
                float turn = way == 'S' ? 180f : way == 'W' ? 90f : way == 'E' ? -90f : 0f;
                var half = new Vector2((hi.x - lo.x) * 0.5f, (hi.z - lo.z) * 0.5f);
                Transform f = t;
                if (turn != 0f)
                {
                    f = new GameObject("Flight").transform;
                    f.SetParent(t, false);
                    f.localPosition = new Vector3(c.x, 0f, c.z); f.localRotation = Quaternion.Euler(0f, turn, 0f);
                    if (way != 'S') half = new Vector2(half.y, half.x);
                }
                Block(f, f == t ? new Vector3(c.x, 0f, c.z) : Vector3.zero, half, null, true, hi.y - LodgeRise * 0.5f, hi.y - lo.y);
                return;
            }
            Helper(t, name, lo, hi);
        }

        /// <summary>The game's material for one of the lodge's (by its name in the file).</summary>
        Material LodgeMat(string name, Dictionary<string, Material> made)
        {
            if (made.TryGetValue(name, out var have)) return have;
            // the lodge's PBR materials (Resources/Camp/LodgeTex: CC0 textures, a normal map and a packed metallic-gloss /
            // occlusion mask each, Assets/Editor/LodgeTex.cs): the timber, the boards, the concrete, the stone, the steel,
            // the fabrics and the leather. Assets, shared, not made per build.
            var pbr = Resources.Load<Material>("Camp/LodgeTex/Materials/" + name);
            if (pbr != null) { made[name] = pbr; return pbr; }
            var woodTex = Resources.Load<Texture2D>("Camp/Wood");
            var oakTex = Resources.Load<Texture2D>("Camp/Oak");
            Material m;
            switch (name)
            {
                case "Cladding": m = M(new Color(0.42f, 0.37f, 0.34f), 0.12f, woodTex); break;                 // dark-stained boards, upright
                case "Glulam": m = M(new Color(1f, 0.84f, 0.62f), 0.3f, oakTex); break;                        // warm, glued-laminated
                case "Steel": m = M(new Color(0.075f, 0.075f, 0.08f), 0.5f); m.SetFloat("_Metallic", 0.5f); break;   // charcoal edges, black frames
                case "Concrete": m = M(new Color(0.74f, 0.73f, 0.7f), 0.12f); break;
                case "Lining": m = M(new Color(1f, 0.93f, 0.84f), 0.12f, oakTex); break;                        // pale timber boards inside, upright
                case "Floor": m = M(new Color(0.6f, 0.58f, 0.55f), 0.45f); break;                                // polished concrete
                case "Snow": case "SnowPile": m = snowCap; break;   // (SnowPile: the drifts against the terrace, walked on: CampSnow.cs)
                case "Glass": m = lodgeGlass = LodgeGlass(); break;                                             // the outside walls' glass, facing out
                case "GlassIn": m = Glass(); m.SetColor("_BaseColor", new Color(0.55f, 0.66f, 0.74f, 0.16f)); break;   // facing in, and the panes inside
                case "Paint": m = paint = Palette(); break;
                case "Chalk": m = Sign("Camp/LodgeChalk"); break;
                case "Signs": m = Sign("Camp/LodgeSigns"); break;                                                // the shops' signs and the café's menu
                case "Art": m = Sign("Camp/LodgeArt"); break;                                                    // the trail map, the posters, the LOOKOUT sign
                case "Shade": m = M(new Color(0.95f, 0.88f, 0.74f), 0.1f, null, default, new Color(1f, 0.72f, 0.42f) * 1.15f); break;   // the lamps' linen shades, lit
                case "Bark": m = M(new Color(0.36f, 0.26f, 0.19f), 0.08f, Resources.Load<Texture2D>("Camp/Wood")); break;   // logs, the shovel's handle
                case "Fir": m = M(new Color(0.13f, 0.27f, 0.18f), 0.12f); break;                                 // the deck's potted firs
                default: m = ChaletMat(name, made); break;   // oak, LEDs, embers, the bench's board, notes, signs
            }
            made[name] = m;
            return m;
        }

        /// <summary>The lodge's glass: clear by day, glowing warm from inside as the evening comes (LodgeNight).</summary>
        Material LodgeGlass()
        {
            var b = Resources.Load<Material>("Camp/GlassGlow");
            var m = b != null ? Made(new Material(b)) : Glass();
            m.SetColor("_BaseColor", new Color(0.55f, 0.66f, 0.74f, 0.24f));
            m.SetFloat("_Smoothness", 0.95f);
            if (b != null) { m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            return m;
        }

        /// <summary>
        /// The small coloured things' one material: a 16-colour strip (MakeLodge.py's PALETTE): the five boards, the
        /// chairs' orange and green, canvas, wool, leather, charcoal, white, leaf, red, navy, cream.
        /// </summary>
        Material Palette()
        {
            if (palette == null)
            {
                palette = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "LodgePalette" };
                var px = new Color[16];
                for (int i = 0; i < 5; i++) px[i] = Progress.Boards[(i + 1) % Progress.Boards.Length];
                px[5] = new Color(0.88f, 0.48f, 0.22f); px[6] = new Color(0.16f, 0.42f, 0.36f); px[7] = new Color(0.66f, 0.63f, 0.58f);
                px[8] = new Color(0.78f, 0.7f, 0.6f);    // wool (the sofa, the rug)
                px[9] = new Color(0.5f, 0.28f, 0.16f);   // cognac leather
                px[10] = new Color(0.15f, 0.16f, 0.18f); // charcoal
                px[11] = new Color(0.93f, 0.93f, 0.92f); // white
                px[12] = new Color(0.2f, 0.42f, 0.22f);  // leaf
                px[13] = new Color(0.78f, 0.16f, 0.14f); // red
                px[14] = new Color(0.14f, 0.2f, 0.38f);  // navy
                px[15] = new Color(0.94f, 0.9f, 0.82f);  // cream (the mugs)
                palette.SetPixels(px); palette.Apply(false, true);
            }
            return M(Color.white, 0.3f, palette);
        }

        /// <summary>The evening (0 day .. 1 night): the glass glows with the rooms behind it.</summary>
        void LodgeNight(float k)
        {
            if (lodgeGlass == null) return;
            lodgeGlass.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.28f) * Mathf.Lerp(0.0f, 0.9f, k));
            lodgeGlass.SetColor("_BaseColor", Color.Lerp(new Color(0.5f, 0.6f, 0.68f, 0.26f), new Color(0.3f, 0.24f, 0.18f, 0.22f), k));
        }

        // ---------------------------------------------------------------- the paved path and its lamps

        /// <summary>
        /// A paved path cleared through the snow from the foot of the lodge's steps, round the fire and down to the gate,
        /// low banks of snow along its edges, and lamp posts along it (two of them real lamps at dusk).
        /// </summary>
        void LodgePath()
        {
            if (paver == null) { paver = M(new Color(0.58f, 0.58f, 0.6f), 0.18f, Resources.Load<Texture2D>("Camp/Stone"), new Vector2(1f, 1f)); }
            var pts = new List<Vector3> { stairsFoot - chalet.forward * 0.6f, Ground(-5.6f, -71.5f), Ground(-4.9f, -64.5f), Ground(-3.4f, -57f), Ground(-1.2f, -50.5f), Ground(0f, -44f) };
            var line = Spline(pts, 10);
            const float half = 1.15f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var bv = new List<Vector3>(); var btri = new List<int>();
            float dist = 0f;
            int across = 5;
            for (int i = 0; i < line.Count; i++)
            {
                var a = line[Mathf.Max(0, i - 1)]; var b = line[Mathf.Min(line.Count - 1, i + 1)];
                var dir = b - a; dir.y = 0f; dir.Normalize();
                var side = new Vector3(dir.z, 0f, -dir.x);
                if (i > 0) dist += Vector3.Distance(line[i - 1], line[i]);
                for (int k = 0; k < across; k++)
                {
                    float e = Mathf.Lerp(-half, half, k / (across - 1f));
                    var q = line[i] + side * e;
                    q.y = Slope.DrawnHeight(q.x, q.z) + 0.04f;
                    v.Add(q); uv.Add(new Vector2(q.x / 1.2f, q.z / 1.2f));
                }
                if (i > 0)
                    for (int k = 0; k < across - 1; k++)
                    {
                        int p0 = (i - 1) * across + k, p1 = i * across + k;
                        tri.AddRange(new[] { p0, p1, p0 + 1, p0 + 1, p1, p1 + 1 });
                    }
                // the banks: a low ridge of shovelled snow each side
                foreach (var s in new[] { -1f, 1f })
                    foreach (var (off, up) in new[] { (half - 0.02f, 0.02f), (half + 0.2f, 0.16f), (half + 0.55f, 0.0f) })
                    {
                        var q = line[i] + side * s * off;
                        q.y = Slope.DrawnHeight(q.x, q.z) + 0.03f + up;
                        bv.Add(q);
                    }
                if (i > 0)
                    for (int s = 0; s < 2; s++)
                        for (int k = 0; k < 2; k++)
                        {
                            int p0 = (i - 1) * 6 + s * 3 + k, p1 = i * 6 + s * 3 + k;
                            if (s == 0) btri.AddRange(new[] { p0, p0 + 1, p1, p0 + 1, p1 + 1, p1 });
                            else btri.AddRange(new[] { p0, p1, p0 + 1, p0 + 1, p1, p1 + 1 });
                        }
            }
            Strip("Path", v, uv, tri, paver);
            Strip("PathBanks", bv, null, btri, snowCap);
            // five lamp posts along it, seven metres apart
            var posts = new GameObject("LampPosts").transform; posts.SetParent(root, false);
            float along = 0f, next = 4f; int n = 0;
            for (int i = 1; i < line.Count && n < 6; i++)
            {
                along += Vector3.Distance(line[i - 1], line[i]);
                if (along < next) continue;
                var dir = line[i] - line[i - 1]; dir.y = 0f; dir.Normalize();
                var side = new Vector3(dir.z, 0f, -dir.x);
                if (side.x > 0f) side = -side;   // (all on its outer side, away from the fire and the middle of the camp)
                var at = line[i] + side * (half + 0.45f);
                at.y = Slope.DrawnHeight(at.x, at.z);
                LampPost(posts, at, n == 1 || n == 3);
                next += 7f; n++;
            }
            LampPosts = n;
            Combine(posts);
            foreach (var r in posts.GetComponentsInChildren<MeshRenderer>())
                if (r.sharedMaterial == bulb) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Strip(string name, List<Vector3> v, List<Vector2> uv, List<int> tri, Material m)
        {
            var mesh = Made(new Mesh { name = name });
            mesh.SetVertices(v); if (uv != null) mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static List<Vector3> Spline(List<Vector3> pts, int per)
        {
            var line = new List<Vector3>();
            for (int i = 0; i < pts.Count - 1; i++)
                for (int k = 0; k < per; k++)
                {
                    float u = k / (float)per;
                    Vector3 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
                    line.Add(0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u));
                }
            line.Add(pts[pts.Count - 1]);
            return line;
        }

        /// <summary>A black lamp post with a lantern head and a cap of snow; lit: a real lamp in it too (dusk only).</summary>
        void LampPost(Transform parent, Vector3 at, bool lit)
        {
            var t = new GameObject("LampPost").transform;
            t.SetParent(parent, false); t.position = at;
            P(PrimitiveType.Cylinder, t, new Vector3(0f, 1.6f, 0f), new Vector3(0.1f, 1.75f, 0.1f), Vector3.zero, metal);
            P(PrimitiveType.Cylinder, t, new Vector3(0f, 0.1f, 0f), new Vector3(0.22f, 0.12f, 0.22f), Vector3.zero, metal);
            P(PrimitiveType.Cube, t, new Vector3(0f, 3.48f, 0f), new Vector3(0.3f, 0.05f, 0.3f), Vector3.zero, metal);
            P(PrimitiveType.Cube, t, new Vector3(0f, 3.28f, 0f), new Vector3(0.22f, 0.36f, 0.22f), Vector3.zero, bulb, false);
            P(PrimitiveType.Cube, t, new Vector3(0f, 3.09f, 0f), new Vector3(0.28f, 0.04f, 0.28f), Vector3.zero, metal);
            P(PrimitiveType.Cube, t, new Vector3(0f, 3.54f, 0f), new Vector3(0.28f, 0.08f, 0.28f), Vector3.zero, snowCap);   // (a box, not a 760-triangle sphere)
            Block(t, Vector3.zero, new Vector2(0.12f, 0.12f));
            if (lit) Lamp(root, root.InverseTransformPoint(at + Vector3.up * 3.1f), 10f, 1.6f);   // (not under the posts: they are merged)
        }
    }
}
