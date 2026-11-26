using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The resort's furniture down the run, the way a ski area marks its pistes (mountain audit F1):
    ///  - bamboo piste poles with an orange disc, every 20-30 m along both edges of the groomed run;
    ///  - rope lines on bamboo poles, with crossed black-and-yellow poles ("closed") at their ends, where the edge is a
    ///    drop: along the piste side of the deadly walls, along a ridge's or a ledge's cliff side, and across the
    ///    cliff band where a jump cliff runs on out of the piste into the rough (and wherever RopeLine is asked for:
    ///    the gap entries of cliff bands to come);
    ///  - snow guns on short towers with a hydrant beside each, down one edge of the lower mountain;
    ///  - a patrol hut, red with a white cross, and its rescue toboggan, beside the top station of the highest lift.
    /// Laid out once per mountain (a pure function of it, like the rest), and drawn in one mesh per 200 m stretch
    /// (one draw, and its shadow) with Resources/PisteKit.shader, which lights it under the floodlights (NightLights)
    /// as the snow round it is lit, and lets it in through the far band (Farview).
    ///
    /// What can be hit: a pole is knocked over like any flag (Breakable: a trigger box, and while it stands it is
    /// part of the stretch's mesh; struck, it gets pieces of its own to topple, and a new run puts it back). A rope is
    /// ridden through: it gives (two halves fall to the snow from their poles), and a pole going down drops its ropes.
    /// A snow gun's mast and barrel, a hydrant and the hut are solid, with colliders the shape of what is drawn
    /// (Obstacle). The toboggan lies low enough to ride over. The trail signs are TrailSigns.
    /// </summary>
    public class PisteKit : MonoBehaviour
    {
        public enum Kind { EdgePole, RopePole, Closed, Gun, Hut }

        public struct Item
        {
            public Kind kind;
            public Vector3 foot;     // on the snow at its foot (the hut: the middle of its floor's footprint, on the snow)
            public Vector3 aim;      // a disc's face, a closed marker's plane, a gun's barrel (level unit vector)
            public float size;       // a pole's height; the hut's ground drop under its floor
            public int id;
        }

        public struct Rope { public int a, b; public int id; }   // between two rope poles (items)

        public const float EdgeH = 1.9f, RopeH = 1.3f, ClosedH = 1.7f, RopeAt = 0.95f;
        public const float BinLen = 200f, Ahead = 700f, Behind = 140f;
        public const float EdgeOut = 1.2f;            // the edge poles stand this far past the groomed run's edge
        public const float GunMast = 3.4f, GunR = 0.48f, GunLen = 1.15f, MastR = 0.075f, HydrantR = 0.11f, HydrantH = 0.85f;
        public const float HutW = 3.2f, HutD = 2.6f, HutWall = 2.35f;

        // ---------------------------------------------------------------- the layout

        static Item[] items = new Item[0];
        static Rope[] ropes = new Rope[0];
        static int laidFor = int.MinValue;
        static bool laying;
        static readonly List<List<Vector2>> asked = new List<List<Vector2>>();
        static readonly List<int> askedSeed = new List<int>();

        /// <summary>Everything on today's mountain (laid out the first time it is asked for).</summary>
        public static Item[] Items { get { Lay(); return items; } }
        public static Rope[] Ropes { get { Lay(); return ropes; } }

        /// <summary>
        /// A rope line along these points (x, z), on bamboo poles, with a crossed "closed" pair at each end: for
        /// a cliff band's gap entry, a closed traverse, anything another builder needs roped off. Lasts for this
        /// mountain (call it again for the next one).
        /// </summary>
        public static void RopeLine(IList<Vector2> points)
        {
            if (points == null || points.Count < 2) return;
            asked.Add(new List<Vector2>(points)); askedSeed.Add(Slope.Seed);
            laidFor = int.MinValue;   // laid out again, with it
            if (Instance != null) Instance.Settle();
        }

        /// <summary>Why things were not put up, by reason (the test tools log it).</summary>
        public static readonly Dictionary<string, int> Fails = new Dictionary<string, int>();
        static bool Fail(string why) { Fails.TryGetValue(why, out int n); Fails[why] = n + 1; return true; }

        /// <summary>Counts by kind (the test tools log it).</summary>
        public static string Stats()
        {
            var n = new int[5];
            foreach (var it in Items) n[(int)it.kind]++;
            return $"edge poles {n[0]}, rope poles {n[1]}, closed markers {n[2]}, ropes {ropes.Length}, snow guns {n[3]}, huts {n[4]}";
        }

        static void Lay()
        {
            if (laidFor == Slope.Seed || laying) return;
            laying = true;
            laidFor = Slope.Seed;
            try
            {
                var list = new List<Item>();
                var rl = new List<Rope>();
                Fails.Clear();
                LayWalls(list, rl);
                LayRidges(list, rl);
                LayJumpCliffs(list, rl);
                LayBands(list, rl);
                LayBlindLips(list);
                for (int i = 0; i < asked.Count; i++) if (askedSeed[i] == Slope.Seed) Line(list, rl, asked[i], true, true);
                LayEdges(list);
                LayFarSide(list, rl);
                LayGuns(list);
                LayHut(list);
                list.Sort((a, b) => a.foot.z.CompareTo(b.foot.z));
                // the ropes name their poles by place in the list: renumber after sorting
                var remap = new Dictionary<int, int>();
                for (int i = 0; i < list.Count; i++) remap[list[i].id] = i;
                for (int i = 0; i < list.Count; i++) { var it = list[i]; it.id = i; list[i] = it; }
                for (int i = 0; i < rl.Count; i++) { var r = rl[i]; r.a = remap[r.a]; r.b = remap[r.b]; r.id = i; rl[i] = r; }
                items = list.ToArray();
                ropes = rl.ToArray();
            }
            finally { laying = false; }
        }

        static int nextId;
        static Item Add(List<Item> list, Kind k, Vector3 foot, Vector3 aim, float size)
        {
            var it = new Item { kind = k, foot = foot, aim = aim, size = size, id = nextId++ };
            list.Add(it);
            return it;
        }

        static Vector3 Snow(float x, float z) => new Vector3(x, Slope.DrawnHeight(x, z), z);

        /// <summary>Anything else stands here (or a tree within r of it).</summary>
        static bool Blocked(float x, float z, float r)
        {
            if (Slope.OnPad(x, z)) return Fail("pad");
            if (LiftLine.Taken(x, z, 1.5f)) return Fail("lift");
            if (Hamlets.Taken(x, z)) return Fail("hamlet");
            if (NightLights.Taken(x, z)) return Fail("floodlight");
            if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f) return Fail("kicker");
            if (Course.CrevasseNear(x, z, 1.5f)) return Fail("crevasse");
            if (Course.NearFeature(x, z, 1.5f)) return Fail("park");
            if (TrailSigns.Near(x, z, 2.5f)) return Fail("sign");
            if (TreeNear(x, z, r)) return Fail("tree");
            return false;
        }

        /// <summary>A tree or a rock of the forest within r of (x, z) (Forest.At: the same pure layout).</summary>
        public static bool TreeNear(float x, float z, float r)
        {
            int cx0 = Mathf.FloorToInt((x - r - Forest.Cell) / Forest.Cell), cx1 = Mathf.FloorToInt((x + r + Forest.Cell) / Forest.Cell);
            int cz0 = Mathf.FloorToInt((z - r - Forest.Cell) / Forest.Cell), cz1 = Mathf.FloorToInt((z + r + Forest.Cell) / Forest.Cell);
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    var k = Forest.At(cx, cz, out var p, out float size, out _);
                    if (k == Forest.Kind.None) continue;
                    // (its trunk, or the rock: what a pole beside it would stand in)
                    float reach = r + (k == Forest.Kind.Rock ? 1.2f * size : 0.6f + 0.6f * size);
                    if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < reach * reach) return true;
                }
            return false;
        }

        // the stretches of each edge that are roped off instead of poled (filled in as the rope lines are laid)
        static readonly List<Vector3> roped = new List<Vector3>();   // x: side, y: z0, z: z1
        static bool Roped(float side, float z)
        {
            foreach (var r in roped) if (r.x == side && z > r.y && z < r.z) return true;
            return false;
        }

        /// <summary>A rope line along the points (x, z): a pole at each, ropes between, crossed poles at the ends.</summary>
        static void Line(List<Item> list, List<Rope> rl, IList<Vector2> pts, bool closedStart, bool closedEnd)
        {
            int last = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                float x = pts[i].x, z = pts[i].y;
                if (Blocked(x, z, 0.6f)) { last = -1; continue; }
                var dir = (i + 1 < pts.Count ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
                var p = Add(list, Kind.RopePole, Snow(x, z) - Vector3.up * 0.05f, new Vector3(dir.x, 0f, dir.y), RopeH);
                if (last >= 0) rl.Add(new Rope { a = last, b = p.id });
                last = p.id;
            }
            if (closedStart) Closed(list, pts[0], pts.Count > 1 ? pts[1] - pts[0] : Vector2.up, -1f);
            if (closedEnd) Closed(list, pts[pts.Count - 1], pts.Count > 1 ? pts[pts.Count - 1] - pts[pts.Count - 2] : Vector2.up, 1f);
        }

        // the crossed pair, just past an end of a line (ahead of it along d, by `way`)
        static void Closed(List<Item> list, Vector2 at, Vector2 d, float way)
        {
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
            Vector2 p = at + d * way * 1.6f;
            if (Blocked(p.x, p.y, 0.8f)) p = at;
            if (Blocked(p.x, p.y, 0.8f)) return;
            // its plane faces up the hill, at whoever is coming down
            Add(list, Kind.Closed, Snow(p.x, p.y) - Vector3.up * 0.05f, Vector3.forward, ClosedH);
        }

        static void LayWalls(List<Item> list, List<Rope> rl)
        {
            roped.Clear();
            for (int c = 0; c <= Course.CycleOf(Slope.FinishZ); c++)
            {
                if (!Cliffs.WallOf(c, out var w)) continue;
                // along the piste's edge on the wall's side, from above where it turns the corner to where it has died
                // down: the wall's one line (no caution tape along the top as well)
                var pts = new List<Vector2>();
                float z0 = w.z0 - 14f, z1 = w.z0;
                for (float z = z0; z < w.z2 - 20f; z += 9f)
                {
                    if (z > w.z1 && Cliffs.WallHeight(w, z) < 5f) break;
                    pts.Add(new Vector2(Slope.Centre(z) + w.side * (Slope.PisteHalf + 1.6f), z));
                    z1 = z;
                }
                if (pts.Count < 3) continue;
                Line(list, rl, pts, true, true);
                roped.Add(new Vector3(w.side, z0 - 8f, z1 + 8f));
            }
        }

        static void LayRidges(List<Item> list, List<Rope> rl)
        {
            for (int c = 0; c <= Course.CycleOf(Slope.FinishZ); c++)
            {
                if (!Ridge.Built(c)) continue;
                float zb = Course.First + c * Course.Cycle;
                float z0 = zb + Ridge.At + Ridge.RampIn * 0.35f, z1 = zb + Ridge.EndOf(c) - Ridge.RampOut * 0.4f;
                for (int s = -1; s <= 1; s += 2)
                {
                    if (!Ridge.CliffSide(z0 + 60f, s)) continue;
                    // on the shoulder, a metre past the orange markers (RidgeDressing), on the crest's own snow
                    var pts = new List<Vector2>();
                    for (float z = z0; z < z1; z += 10f)
                        pts.Add(new Vector2(Slope.Centre(z) + s * (Ridge.HalfWidth(z) + 1.3f), z));
                    if (pts.Count < 3) continue;
                    Line(list, rl, pts, true, false);
                    roped.Add(new Vector3(s, z0 - 30f, Ridge.Has(z1 + 20f) ? z1 + 60f : z1 + 20f));
                }
            }
        }

        static void LayJumpCliffs(List<Item> list, List<Rope> rl)
        {
            for (int c = 0; c <= Course.CycleOf(Slope.FinishZ); c++)
                for (int k = 0; k < 2; k++)
                {
                    if (!Cliffs.JumpOf(c, k, out var j)) continue;
                    // across the cliff band where it runs on out of the piste into the rough, a few metres above the lip
                    // (CliffDressing's black wands mark the lip itself, out to 4 m past the edge)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var pts = new List<Vector2>();
                        for (float a = Slope.PisteHalf + 6f; a <= Slope.PisteHalf + Cliffs.JumpBand - 4f; a += 6f)
                        {
                            float off = s * a;
                            float z = Cliffs.LipZ(j, off) - 3.5f;
                            pts.Add(new Vector2(Slope.Centre(j.z) + off, z));
                        }
                        if (pts.Count >= 2) Line(list, rl, pts, true, false);
                    }
                }
        }

        static void LayBands(List<Item> list, List<Rope> rl)
        {
            for (int c = 0; c <= Course.CycleOf(Slope.FinishZ); c++)
            {
                if (!CliffBand.Of(c, out var b)) continue;
                // the band's one line: a rope along the bench, a step in from its edge, wherever the band
                // stands tall, broken at each low place (the optional air: its lip is marked with black wands)
                var pts = new List<Vector2>();
                for (float zc = b.z1 - 8f; zc < b.z2 + 13f; zc += 7f)
                {
                    bool tall = !CliffBand.Low(b, zc) && CliffBand.HeightAt(b, zc) >= 6f;
                    if (tall) { pts.Add(CliffBand.PointAt(b, zc, CliffBand.EdgeAt(zc) - 2.6f)); continue; }
                    if (pts.Count >= 3) Line(list, rl, pts.ToArray(), true, true);
                    pts.Clear();
                }
                if (pts.Count >= 3) Line(list, rl, pts.ToArray(), true, true);
            }
        }

        static readonly List<Slope.Drop> blind = new List<Slope.Drop>();

        static void LayBlindLips(List<Item> list)
        {
            // a blind roll-over: the crossed pair on both edges of the piste just above the lip ("slow down, look before
            // you drop": the snow below is out of sight from the bench)
            Slope.BlindLips(Course.First, Slope.FinishZ, blind);
            foreach (var d in blind)
            {
                for (int e = -1; e <= 1; e += 2)
                {
                    // (a step up the hill, out from the edge or just in on it if something already stands there: a tree, a
                    // floodlight, a hamlet's fence)
                    foreach (var o in new[] { new Vector2(0f, 0f), new Vector2(0f, -2.5f), new Vector2(1.5f, 0f), new Vector2(1.5f, -2.5f), new Vector2(0f, -5f), new Vector2(3f, -2.5f),
                                             new Vector2(-1.6f, 0f), new Vector2(-1.6f, -3f), new Vector2(0f, -9f), new Vector2(-1.6f, -9f) })
                    {
                        float z = d.LipZ - 4f + o.y;
                        float x = Slope.Centre(z) + e * (Slope.HalfAt(z) + (0.8f + o.x) / Slope.Squash(z));
                        if (Blocked(x, z, 0.8f)) continue;
                        Add(list, Kind.Closed, Snow(x, z) - Vector3.up * 0.05f, Vector3.forward, ClosedH);
                        break;
                    }
                }
            }
        }

        static void LayEdges(List<Item> list)
        {
            int i = 0;
            for (float z = Course.First + 20f; z < Slope.FinishZ - 8f; i++)
            {
                float step = 20f + 10f * Slope.Hash01(i, 11, 77);
                for (int s = -1; s <= 1; s += 2)
                {
                    if (Roped(s, z)) { Fail("roped"); continue; }
                    // on a ridge's crest (or a valley's floor, or a ledge): its own edge, roped on a cliff side, and
                    // nothing against a rock wall
                    // (off a ridge, the groomed run's own edge: wide below a cliff band, measured along x where a band's
                    // traverse crosses the hill; a band's bench has its own rope)
                    if (CliffBand.Near(z, 10f, out _)) { Fail("band"); continue; }
                    float half = Ridge.Weight(z) > 0f ? Ridge.HalfWidth(z) : Slope.HalfAt(z);
                    if (Ridge.Weight(z) > 0.2f)
                    {
                        var side = Ridge.SideAt(z, s);
                        if (side == Ridge.Side.Cliff || side == Ridge.Side.Wall) { Fail("crest"); continue; }
                    }
                    float x = Slope.Centre(z) + s * (half + EdgeOut / Slope.Squash(z));
                    if (Cliffs.Keep(x, z, 1f)) { Fail("cliff"); continue; }
                    if (Blocked(x, z, 0.4f)) continue;
                    // the disc faces up the hill (and a little in toward the run)
                    Add(list, Kind.EdgePole, Snow(x, z) - Vector3.up * 0.05f, new Vector3(-s * 0.25f, 0f, -1f).normalized, EdgeH);
                }
                z += step;
            }
        }

        /// <summary>
        /// (lifttop) The Summit top's way down to the run: poles along both edges, and where an edge is open (the
        /// ground falling away past it) a rope line along it instead; and a rope line round the top's pad wherever the
        /// ground falls away past its rim (lifttop2: side 2, no poles).
        /// </summary>
        static void LayFarSide(List<Item> list, List<Rope> rl)
        {
            var edges = LiftTop.FarEdges(9f);
            foreach (int side in new[] { -1, 1, 2 })
            {
                var pts = new List<(Vector2 at, bool open)>();
                foreach (var e in edges) if (e.side == side) pts.Add((e.at, e.open));
                // (round the rim: start the walk round at a gap, so a rope line isn't split where the list wraps)
                if (side == 2 && pts.Count > 0)
                {
                    int gap = pts.FindIndex(q => !q.open);
                    if (gap > 0) { var rot = pts.GetRange(gap, pts.Count - gap); rot.AddRange(pts.GetRange(0, gap)); pts = rot; }
                }
                var run = new List<Vector2>();
                for (int i = 0; i <= pts.Count; i++)
                {
                    bool open = i < pts.Count && pts[i].open;
                    if (open) { run.Add(pts[i].at); continue; }
                    if (run.Count >= 2) Line(list, rl, run, false, false);
                    run.Clear();
                    if (side == 2) continue;
                    if (i >= pts.Count || (i & 1) == 1) continue;
                    var at = pts[i].at;
                    if (Blocked(at.x, at.y, 0.4f)) continue;
                    var d = (pts[Mathf.Min(pts.Count - 1, i + 1)].at - pts[Mathf.Max(0, i - 1)].at).normalized;
                    // the disc faces up the way, a little in toward the run's middle
                    var inward = new Vector2(-d.y, d.x) * side;
                    Add(list, Kind.EdgePole, Snow(at.x, at.y) - Vector3.up * 0.05f, new Vector3(-d.x - inward.x * 0.25f, 0f, -d.y - inward.y * 0.25f).normalized, EdgeH);
                }
            }
        }

        static void LayGuns(List<Item> list)
        {
            // the lower mountain, below the glacier: snowmaking down one edge (which one changes now and then)
            float from = Mathf.Max(Course.GlacierBottom + 500f, Slope.FinishZ * 0.55f);
            int n = 0;
            for (float z = from; z < Course.ClearFrom - 10f; z += 64f, n++)
            {
                if (Ridge.InCanyons(z) || Ridge.InCanyons(z + 10f)) continue;
                float s = Slope.Hash01(Course.CycleOf(z), 21, 77) < 0.5f ? -1f : 1f;
                bool done = false;
                for (int t = 0; t < 3 && !done; t++)
                {
                    float zz = z + t * 6f;
                    float x = Slope.Centre(zz) + s * (Slope.PisteHalf + 3.5f);
                    if (Course.Keep(x, zz, 3f) || Blocked(x, zz, 2.2f)) continue;
                    // the hydrant a step out and up the hill from the tower
                    float hx = x + s * 1.6f, hz = zz - 1.4f;
                    if (Blocked(hx, hz, 0.8f)) continue;
                    // aimed across the run and down it, so the plume settles on the piste
                    var aim = new Vector3(-s, 0f, 0.8f).normalized;
                    Add(list, Kind.Gun, Snow(x, zz) - Vector3.up * 0.1f, aim, GunMast);
                    Add(list, Kind.Gun, Snow(hx, hz) - Vector3.up * 0.05f, Vector3.zero, -1f);   // (size < 0: a hydrant)
                    done = true;
                }
            }
        }

        static void LayHut(List<Item> list)
        {
            // the patrol hut stands beside the top station of the highest lift, in its cleared ground
            LiftLine.Lift best = null;
            foreach (var l in LiftLine.Lifts) if (l != null && (best == null || l.top.z < best.top.z)) best = l;
            if (best == null) { Fail("hut: no lift"); return; }
            // across the line from the station, past its deck, inside the cleared swath: on its far side from the piste
            // first (out of the way of the unloading, which faces the run)
            float toPiste = Vector3.Dot(new Vector3(Slope.Centre(best.top.z) - best.top.x, 0f, 0f), best.across) >= 0f ? 1f : -1f;
            foreach (float side in new[] { -toPiste, toPiste })
                foreach (float along in new[] { 0f, -3f, 3f, -6f })
                {
                    Vector3 c = best.top + best.across * side * (LiftLine.StationHalfWid + 4.2f) + best.dir * along;
                    if (Slope.OnPad(c.x, c.z) || Hamlets.Taken(c.x, c.z) || NightLights.Taken(c.x, c.z) || Course.Keep(c.x, c.z, 2f)) { Fail("hut: taken"); continue; }
                    if (TreeNear(c.x, c.z, 3f)) { Fail("hut: tree"); continue; }
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int i = -1; i <= 1; i += 2)
                        for (int k = -1; k <= 1; k += 2)
                        {
                            float y = Slope.DrawnHeight(c.x + i * HutW * 0.5f, c.z + k * HutD * 0.5f);
                            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                        }
                    if (hi - lo > 3.6f) { Fail("hut: steep"); continue; }   // too steep for its stone plinth
                    // its front (and its cross) toward the piste
                    Add(list, Kind.Hut, new Vector3(c.x, hi + 0.15f, c.z), new Vector3(Slope.Centre(c.z) >= c.x ? 1f : -1f, 0f, 0f), hi + 0.15f - lo);
                    return;
                }
        }

        // ---------------------------------------------------------------- building

        Rider rider;
        Material kit;
        readonly Dictionary<int, Bin> bins = new Dictionary<int, Bin>();
        Material[] pieceMats;
        int builtFor = int.MinValue;
        Vector3 lastRider;
        bool haveLast;
        public static PisteKit Instance { get; private set; }
        /// <summary>For before/after pictures: nothing of the kit built.</summary>
        public static bool Disabled;

        /// <summary>Called by the game once the mountain's systems are set up (SummitGame).</summary>
        public static PisteKit Build(Rider r)
        {
            var go = new GameObject("PisteKit");
            var pk = go.AddComponent<PisteKit>();
            pk.rider = r;
            pk.Setup();
            Instance = pk;
            return pk;
        }

        void Setup()
        {
            kit = KitMaterial("PisteKit", Color.white, 1f, true);
            Slope.Changing += Settle;
            Breakable.Restored += TieUp;
        }

        /// <summary>The kit's material: vertex coloured (the stretches' meshes), or one colour (Unity's primitives).</summary>
        public static Material KitMaterial(string name, Color c, float smooth, bool vertexColour)
        {
            var sh = Resources.Load<Shader>("PisteKit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_VertexColor", vertexColour ? 1f : 0f);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        // a struck pole's own pieces: bamboo, its orange disc, a closed marker's yellow
        Material[] Pieces() => pieceMats ?? (pieceMats = new[] { KitMaterial("PisteKitBamboo", Bamboo, 0.35f, false), KitMaterial("PisteKitOrange", Orange, 0.5f, false), KitMaterial("PisteKitYellow", Yellow, 0.45f, false) });

        void OnDestroy()
        {
            Slope.Changing -= Settle;
            Breakable.Restored -= TieUp;
            Settle();
            if (Instance == this) Instance = null;
        }

        class Bin
        {
            public int key;
            public GameObject root;
            public Mesh mesh;
            public Vector3[] shown, live;
            public bool dirty;
            public readonly Dictionary<int, Vector2Int> range = new Dictionary<int, Vector2Int>();   // item or rope (-1 - id) -> verts
            public readonly Dictionary<int, Vector2Int> slack = new Dictionary<int, Vector2Int>();   // a rope's two fallen halves
            public readonly List<int> ropeIds = new List<int>();
        }

        // which ropes are down this run, and which poles
        readonly HashSet<int> ropeDown = new HashSet<int>();
        readonly HashSet<int> poleDown = new HashSet<int>();

        void Update()
        {
            if (rider == null) return;
            Stream(false);
            CrossRopes();
            foreach (var b in bins.Values) if (b.dirty) { b.mesh.SetVertices(b.live); b.dirty = false; }
        }

        /// <summary>Everything round the rider built now (the test tools; a new mountain's first frame).</summary>
        public void Prewarm() { Stream(true); foreach (var b in bins.Values) if (b.dirty) { b.mesh.SetVertices(b.live); b.dirty = false; } }

        void Stream(bool now)
        {
            if (Disabled) { Settle(); return; }
            if (builtFor != Slope.Seed || laidFor != Slope.Seed) { Settle(); Lay(); builtFor = Slope.Seed; }
            float z = rider.transform.position.z;
            int k0 = Mathf.FloorToInt((z - Behind) / BinLen), k1 = Mathf.FloorToInt((z + Ahead) / BinLen);
            drop.Clear();
            foreach (var kv in bins) if (kv.Key < k0 - 1 || kv.Key > k1 + 1) drop.Add(kv.Key);
            foreach (int k in drop) { Free(bins[k]); bins.Remove(k); }
            for (int k = k0; k <= k1; k++)
            {
                if (bins.ContainsKey(k)) continue;
                bins[k] = Make(k);
                if (!now) break;   // (one stretch a frame)
            }
            TrailSigns.Stream(transform, z - Behind, z + Ahead);
        }
        readonly List<int> drop = new List<int>();

        void Settle()
        {
            foreach (var b in bins.Values) Free(b);
            bins.Clear();
            ropeDown.Clear(); poleDown.Clear();
            haveLast = false;
            TrailSigns.Clear();
        }

        static void Free(Bin b)
        {
            if (b.mesh != null) Kill(b.mesh);
            if (b.root != null) Kill(b.root);
        }

        static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        Bin Make(int k)
        {
            var b = new Bin { key = k };
            float lo = k * BinLen, hi = lo + BinLen;
            var d = new MeshData();
            var its = items;
            int i0 = Lower(its, lo);
            b.root = new GameObject("PisteKit " + k);
            b.root.transform.SetParent(transform, false);
            for (int i = i0; i < its.Length && its[i].foot.z < hi; i++)
            {
                var it = its[i];
                int v0 = d.v.Count;
                Draw(d, it);
                b.range[it.id] = new Vector2Int(v0, d.v.Count);
                Solid(b.root.transform, it);
                poleDown.Remove(it.id);   // (built again: standing again)
            }
            // a rope belongs to the stretch its first pole is in
            for (int r = 0; r < ropes.Length; r++)
            {
                var rp = ropes[r];
                float za = its[rp.a].foot.z;
                if (za < lo || za >= hi) continue;
                int v0 = d.v.Count;
                RopeMesh(d, its[rp.a], its[rp.b]);
                b.range[-1 - r] = new Vector2Int(v0, d.v.Count);
                v0 = d.v.Count;
                Fallen(d, its[rp.a], its[rp.b]);
                Fallen(d, its[rp.b], its[rp.a]);
                b.slack[-1 - r] = new Vector2Int(v0, d.v.Count);
                b.ropeIds.Add(r);
            }
            b.mesh = d.Make("PisteKit " + k);
            if (b.mesh != null)
            {
                b.shown = b.mesh.vertices;
                b.live = (Vector3[])b.shown.Clone();
                var g = new GameObject("Kit");
                g.transform.SetParent(b.root.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = b.mesh;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kit;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                // the fallen halves hidden until a rope comes down; what is down this run stays down
                foreach (var kv in b.slack) Hide(b, kv.Value);
                foreach (int r in b.ropeIds) if (ropeDown.Contains(r) || poleDown.Contains(ropes[r].a) || poleDown.Contains(ropes[r].b)) DropRope(b, r);
            }
            return b;
        }

        static int Lower(Item[] its, float z)
        {
            int lo = 0, hi = its.Length;
            while (lo < hi) { int m = (lo + hi) >> 1; if (its[m].foot.z < z) lo = m + 1; else hi = m; }
            return lo;
        }

        static void Hide(Bin b, Vector2Int r)
        {
            if (r.y <= r.x) return;
            var p = b.shown[r.x];
            for (int i = r.x; i < r.y; i++) b.live[i] = p;
            b.dirty = true;
        }

        static void Show(Bin b, Vector2Int r)
        {
            for (int i = r.x; i < r.y; i++) b.live[i] = b.shown[i];
            b.dirty = true;
        }

        Bin BinOf(float z) => bins.TryGetValue(Mathf.FloorToInt(z / BinLen), out var b) ? b : null;

        void DropRope(Bin b, int r)
        {
            if (b.range.TryGetValue(-1 - r, out var whole)) Hide(b, whole);
            if (b.slack.TryGetValue(-1 - r, out var halves)) Show(b, halves);
        }

        void TieRope(Bin b, int r)
        {
            if (b.range.TryGetValue(-1 - r, out var whole)) Show(b, whole);
            if (b.slack.TryGetValue(-1 - r, out var halves)) Hide(b, halves);
        }

        /// <summary>A new run: the ropes tied up again (the poles are put back by Breakable, one by one).</summary>
        void TieUp()
        {
            ropeDown.Clear();
            foreach (var b in bins.Values)
                foreach (int r in b.ropeIds)
                    if (!poleDown.Contains(ropes[r].a) && !poleDown.Contains(ropes[r].b)) TieRope(b, r);
        }

        // ---------------------------------------------------------------- what can be hit

        void Solid(Transform root, Item it)
        {
            switch (it.kind)
            {
                case Kind.EdgePole:
                case Kind.RopePole:
                case Kind.Closed:
                {
                    var holder = new GameObject(it.kind == Kind.EdgePole ? "PistePole" : it.kind == Kind.Closed ? "ClosedPoles" : "RopePole").transform;
                    holder.SetParent(root, false);
                    holder.position = it.foot;
                    int id = it.id;
                    Breakable.Flag(holder, it.size, () => Reveal(holder, id), () => Conceal(holder, id));
                    break;
                }
                case Kind.Gun:
                {
                    var g = new GameObject(it.size < 0f ? "Hydrant" : "SnowGun");
                    g.transform.SetParent(root, false);
                    g.transform.position = it.foot;
                    g.AddComponent<Obstacle>().label = it.size < 0f ? "Hydrant" : "Snow Gun";
                    if (it.size < 0f)
                    {
                        var cc = g.AddComponent<CapsuleCollider>();
                        cc.radius = HydrantR; cc.height = HydrantH + 0.1f; cc.center = new Vector3(0f, (HydrantH + 0.1f) * 0.5f, 0f);
                        break;
                    }
                    var mast = g.AddComponent<CapsuleCollider>();
                    mast.radius = MastR; mast.height = GunMast + 0.1f; mast.center = new Vector3(0f, (GunMast + 0.1f) * 0.5f, 0f);
                    // the barrel: a capsule along its axis, as round as the drum (a child turned to it)
                    var bar = new GameObject("Barrel");
                    bar.transform.SetParent(g.transform, false);
                    GunFrame(it, out var mid, out var axis);
                    bar.transform.SetPositionAndRotation(mid, Quaternion.FromToRotation(Vector3.up, axis));
                    var bc = bar.AddComponent<CapsuleCollider>();
                    bc.radius = GunR; bc.height = GunLen; bc.direction = 1;
                    break;
                }
                case Kind.Hut:
                {
                    var g = new GameObject("PatrolHut");
                    g.transform.SetParent(root, false);
                    g.transform.position = it.foot;
                    g.AddComponent<Obstacle>().label = "Patrol Hut";
                    var bc = g.AddComponent<BoxCollider>();
                    // the walls and the plinth under them, from the lowest snow to the eaves
                    float drop = it.size;
                    bc.center = new Vector3(0f, (HutWall - drop) * 0.5f, 0f);
                    bc.size = new Vector3(HutW, HutWall + drop, HutD);
                    break;
                }
            }
        }

        /// <summary>Struck: out of the stretch's mesh, and pieces of its own on the holder to fall (Breakable.Flag).</summary>
        void Reveal(Transform holder, int id)
        {
            poleDown.Add(id);
            var it = items[id];
            var b = BinOf(it.foot.z);
            if (b != null && b.range.TryGetValue(id, out var r)) Hide(b, r);
            // its ropes fall with it
            for (int i = 0; i < ropes.Length; i++)
                if (ropes[i].a == id || ropes[i].b == id)
                {
                    var rb = BinOf(items[ropes[i].a].foot.z);
                    if (rb != null) DropRope(rb, i);
                }
            var m = Pieces();
            Vector3 foot = holder.position;
            if (it.kind == Kind.Closed)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    var p = Piece(PrimitiveType.Cylinder, holder, m[2]);
                    p.position = foot + Vector3.up * (ClosedH * 0.5f);
                    p.rotation = Quaternion.LookRotation(it.aim) * Quaternion.AngleAxis(s * 24f, Vector3.forward);
                    p.localScale = new Vector3(0.036f, ClosedH * 0.55f, 0.036f);
                }
                return;
            }
            float h = it.size;
            var pole = Piece(PrimitiveType.Cylinder, holder, m[0]);
            pole.position = foot + Vector3.up * (h * 0.5f - 0.05f);
            pole.localScale = new Vector3(0.032f, h * 0.5f + 0.05f, 0.032f);
            if (it.kind == Kind.EdgePole)
            {
                var disc = Piece(PrimitiveType.Cylinder, holder, m[1]);
                disc.position = foot + Vector3.up * 1.55f + it.aim * 0.03f;
                disc.rotation = Quaternion.FromToRotation(Vector3.up, it.aim);
                disc.localScale = new Vector3(0.34f, 0.004f, 0.34f);
            }
        }

        static Transform Piece(PrimitiveType t, Transform parent, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            Kill(go.GetComponent<Collider>());
            go.name = "Piece";
            go.transform.SetParent(parent, true);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            return go.transform;
        }

        /// <summary>Standing again (a new run): its own pieces away, and back in the stretch's mesh, ropes and all.</summary>
        void Conceal(Transform holder, int id)
        {
            for (int i = holder.childCount - 1; i >= 0; i--) Kill(holder.GetChild(i).gameObject);
            poleDown.Remove(id);
            var b = BinOf(items[id].foot.z);
            if (b != null && b.range.TryGetValue(id, out var r)) Show(b, r);
            for (int i = 0; i < ropes.Length; i++)
                if ((ropes[i].a == id || ropes[i].b == id) && !poleDown.Contains(ropes[i].a) && !poleDown.Contains(ropes[i].b) && !ropeDown.Contains(i))
                {
                    var rb = BinOf(items[ropes[i].a].foot.z);
                    if (rb != null) TieRope(rb, i);
                }
        }

        /// <summary>A rope the rider rode through gives: its halves fall from their poles, and it barely checks them.</summary>
        void CrossRopes()
        {
            Vector3 p = rider.transform.position;
            if (!haveLast || !rider.Running || rider.Crashed) { lastRider = p; haveLast = true; return; }
            Vector3 q = lastRider;
            lastRider = p;
            if ((p - q).sqrMagnitude > 30f * 30f) return;   // (a respawn)
            var b = BinOf(p.z);
            for (int pass = 0; pass < 2; pass++, b = BinOf(p.z - BinLen))
            {
                if (b == null) continue;
                foreach (int r in b.ropeIds)
                {
                    if (ropeDown.Contains(r)) continue;
                    var A = items[ropes[r].a].foot; var B = items[ropes[r].b].foot;
                    if (Mathf.Max(A.z, B.z) < Mathf.Min(p.z, q.z) - 1f || Mathf.Min(A.z, B.z) > Mathf.Max(p.z, q.z) + 1f) continue;
                    if (!Cross(new Vector2(q.x, q.z), new Vector2(p.x, p.z), new Vector2(A.x, A.z), new Vector2(B.x, B.z), out float t)) continue;
                    Vector3 at = Vector3.Lerp(A, B, t);
                    float ropeY = at.y + RopeAt - 0.15f;
                    // flying clean over it, or far under it in a hollow: no
                    if (p.y > ropeY + 0.1f || p.y < ropeY - 2.2f) continue;
                    ropeDown.Add(r);
                    DropRope(b, r);
                    if (rider.InvincibleTime <= 0f) rider.Breakthrough(Breakables.BannerKeep, Breakables.BannerWobble, 0f, float.MaxValue, "");
                }
            }
        }

        static bool Cross(Vector2 p0, Vector2 p1, Vector2 a, Vector2 b, out float t)
        {
            t = 0f;
            Vector2 r = p1 - p0, s = b - a;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            Vector2 d = a - p0;
            float u = (d.x * s.y - d.y * s.x) / den, v = (d.x * r.y - d.y * r.x) / den;
            t = v;
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        // ---------------------------------------------------------------- drawing

        // sRGB colours (written to the mesh as linear), and how glossy each paint is
        static readonly Color Bamboo = new Color(0.80f, 0.68f, 0.42f), BambooDark = new Color(0.58f, 0.47f, 0.26f);
        static readonly Color Orange = new Color(1f, 0.40f, 0.03f), Yellow = new Color(0.98f, 0.78f, 0.06f), Black = new Color(0.06f, 0.06f, 0.065f);
        static readonly Color RopeCol = new Color(1f, 0.52f, 0.06f);
        static readonly Color Steel = new Color(0.56f, 0.58f, 0.61f), Drum = new Color(0.93f, 0.93f, 0.92f), Grille = new Color(0.10f, 0.10f, 0.11f);
        static readonly Color HydrantRed = new Color(0.78f, 0.09f, 0.06f), Hose = new Color(0.08f, 0.08f, 0.09f);
        static readonly Color HutRed = new Color(0.66f, 0.07f, 0.06f), White = new Color(0.95f, 0.95f, 0.94f), Roof = new Color(0.20f, 0.21f, 0.23f);
        static readonly Color RoofSnow = new Color(0.95f, 0.96f, 0.99f), Stone = new Color(0.46f, 0.45f, 0.44f), Door = new Color(0.30f, 0.19f, 0.11f);
        static readonly Color Glass = new Color(0.12f, 0.16f, 0.22f), Sled = new Color(0.85f, 0.10f, 0.06f);

        static Color P(Color c, float smooth) { var l = c.linear; l.a = smooth; return l; }

        static void Draw(MeshData d, in Item it)
        {
            switch (it.kind)
            {
                case Kind.EdgePole:
                {
                    Vector3 f = it.foot;
                    // bamboo, its top painted orange, and the disc on it
                    BambooPole(d, f - Vector3.up * 0.15f, f + Vector3.up * 1.45f, 0.018f);
                    d.Tube(f + Vector3.up * 1.45f, f + Vector3.up * it.size, 0.0185f, 6, P(Orange, 0.5f), P(Orange, 0.5f), true);
                    d.Disc(f + Vector3.up * 1.55f + it.aim * 0.022f, it.aim, 0.17f, P(Orange, 0.5f), 14, true);
                    break;
                }
                case Kind.RopePole:
                    BambooPole(d, it.foot - Vector3.up * 0.15f, it.foot + Vector3.up * it.size, 0.018f);
                    break;
                case Kind.Closed:
                {
                    Vector3 across = Vector3.Cross(Vector3.up, it.aim).normalized;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 a = it.foot - across * s * 0.42f - Vector3.up * 0.15f, b = it.foot + across * s * 0.45f + Vector3.up * it.size;
                        Banded(d, a, b, 0.02f, 7);
                    }
                    break;
                }
                case Kind.Gun:
                    if (it.size < 0f) Hydrant(d, it); else Gun(d, it);
                    break;
                case Kind.Hut:
                    Hut(d, it);
                    break;
            }
        }

        static void BambooPole(MeshData d, Vector3 a, Vector3 b, float r)
        {
            // cane with darker nodes every 30 cm
            float len = (b - a).magnitude;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 0.3f));
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = Vector3.Lerp(a, b, i / (float)n), p1 = Vector3.Lerp(a, b, (i + 1f) / n);
                d.Tube(p0, p1, r, 5, P(i % 2 == 0 ? Bamboo : Color.Lerp(Bamboo, BambooDark, 0.35f), 0.35f), P(BambooDark, 0.3f), false);
            }
            d.Disc(b, Vector3.up, r, P(BambooDark, 0.3f), 5, false);
        }

        static void Banded(MeshData d, Vector3 a, Vector3 b, float r, int bands)
        {
            for (int i = 0; i < bands; i++)
            {
                Vector3 p0 = Vector3.Lerp(a, b, i / (float)bands), p1 = Vector3.Lerp(a, b, (i + 1f) / bands);
                var c = P(i % 2 == 0 ? Black : Yellow, 0.45f);
                d.Tube(p0, p1, r, 6, c, c, false);
            }
            d.Disc(b, (b - a).normalized, r, P(Black, 0.4f), 6, false);
        }

        static Vector3 RopePoint(in Item a, in Item b, float t)
        {
            Vector3 pa = a.foot + Vector3.up * RopeAt, pb = b.foot + Vector3.up * RopeAt;
            float span = (pb - pa).magnitude;
            return Vector3.Lerp(pa, pb, t) + Vector3.down * (0.025f * span * 4f * t * (1f - t));
        }

        static void RopeMesh(MeshData d, in Item a, in Item b)
        {
            const int n = 10;
            var c = P(RopeCol, 0.35f);
            for (int i = 0; i < n; i++) d.Tube(RopePoint(a, b, i / (float)n), RopePoint(a, b, (i + 1f) / n), 0.013f, 4, c, c, false);
        }

        /// <summary>Half a rope come down: hanging from its pole to the snow, then lying along where it ran.</summary>
        static void Fallen(MeshData d, in Item from, in Item to)
        {
            Vector3 pa = from.foot + Vector3.up * RopeAt, pb = to.foot;
            Vector3 dir = pb - pa; dir.y = 0f;
            float half = dir.magnitude * 0.48f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            var c = P(RopeCol, 0.35f);
            Vector3 prev = pa;
            const int n = 9;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                float along = Mathf.Lerp(0.15f, half, t * t);
                Vector3 g = from.foot + dir * along;
                g.y = Slope.DrawnHeight(g.x, g.z) + 0.02f;
                Vector3 p = i == 1 ? Vector3.Lerp(pa, g, 0.75f) : g;
                d.Tube(prev, p, 0.011f, 4, c, c, false);
                prev = p;
            }
        }

        static void GunFrame(in Item it, out Vector3 mid, out Vector3 axis)
        {
            axis = (it.aim + Vector3.up * 0.42f).normalized;   // about 23 degrees up
            mid = it.foot + Vector3.up * (it.size + GunR * 0.75f) + axis * 0.15f;
        }

        static void Gun(MeshData d, in Item it)
        {
            var steel = P(Steel, 0.55f);
            Vector3 top = it.foot + Vector3.up * it.size;
            d.Tube(it.foot - Vector3.up * 0.2f, top, MastR, 8, steel, steel, false);
            // a concrete footing round its foot, mostly under the snow
            d.Tube(it.foot - Vector3.up * 0.3f, it.foot + Vector3.up * 0.18f, 0.32f, 8, P(Stone, 0.2f), P(Stone, 0.2f), true);
            GunFrame(it, out var mid, out var axis);
            // the yoke from the mast's head up to the drum
            d.Tube(top, mid - axis * 0.15f, 0.06f, 6, steel, steel, false);
            Vector3 back = mid - axis * GunLen * 0.5f, front = mid + axis * GunLen * 0.5f;
            var drum = P(Drum, 0.6f);
            d.Tube(back, front, GunR, 16, drum, drum, false);
            // a yellow band round the drum, the dark fan grille behind, and the nozzle ring in front
            d.Tube(back + axis * 0.32f, back + axis * 0.46f, GunR + 0.008f, 16, P(Yellow, 0.5f), P(Yellow, 0.5f), false);
            d.Disc(back, -axis, GunR, P(Grille, 0.2f), 16, false);
            d.Tube(front, front + axis * 0.06f, GunR * 0.92f, 16, P(Grille, 0.4f), P(Grille, 0.4f), false);
            d.Disc(front + axis * 0.06f, axis, GunR * 0.92f, P(Black, 0.3f), 16, false);
            d.Disc(front - axis * 0.02f, axis, GunR * 0.75f, P(Grille, 0.2f), 16, false);
        }

        static void Hydrant(MeshData d, in Item it)
        {
            var red = P(HydrantRed, 0.5f);
            Vector3 f = it.foot;
            d.Tube(f - Vector3.up * 0.2f, f + Vector3.up * HydrantH, HydrantR, 10, red, red, false);
            d.Tube(f + Vector3.up * HydrantH, f + Vector3.up * (HydrantH + 0.08f), HydrantR * 0.8f, 10, P(Steel, 0.5f), P(Steel, 0.5f), true);
            // the coupling, and the hose across the snow to the tower
            Vector3 cup = f + Vector3.up * (HydrantH * 0.55f);
            Vector3 toGun = new Vector3(-Mathf.Sign(f.x - Slope.Centre(f.z)) * 1.6f, 0f, 1.4f);
            Vector3 side = toGun.normalized;
            d.Tube(cup, cup + side * 0.18f, 0.045f, 6, P(Steel, 0.5f), P(Steel, 0.5f), true);
            Vector3 prev = cup + side * 0.18f;
            const int n = 8;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 g = f + side * 0.18f + toGun * t;
                g.y = Slope.DrawnHeight(g.x, g.z) + 0.04f;
                Vector3 p = i == 1 ? Vector3.Lerp(prev, g, 0.6f) : g;
                d.Tube(prev, p, 0.035f, 6, P(Hose, 0.3f), P(Hose, 0.3f), false);
                prev = p;
            }
        }

        static void Hut(MeshData d, in Item it)
        {
            // laid out in the hut's own frame: x across (its front faces `aim`, toward the piste), z down the hill
            Vector3 c = it.foot, fx = it.aim.x >= 0f ? Vector3.right : Vector3.left, fz = Vector3.forward;
            float hw = HutW * 0.5f, hd = HutD * 0.5f, drop = it.size;
            Vector3 X(float x, float y, float z) => c + fx * x + Vector3.up * y + fz * z;
            // the stone plinth from the lowest snow up to the floor, the red walls, the roof's eaves
            d.Box(X(0f, -drop * 0.5f - 0.1f, 0f), fx, fz, new Vector3(HutW + 0.04f, drop + 0.2f, HutD + 0.04f), P(Stone, 0.15f));
            d.Box(X(0f, HutWall * 0.5f, 0f), fx, fz, new Vector3(HutW, HutWall, HutD), P(HutRed, 0.35f));
            // a white cross on the front, toward the piste (and on the back): symbols, no lettering
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 n = fx * side;
                Vector3 mid = X(side * (hw + 0.012f), HutWall * 0.55f, 0f);
                Vector3 tz = fz, ty = Vector3.up;
                d.Quad(mid, n, tz * 0.22f, ty * 0.75f, P(White, 0.4f));
                d.Quad(mid, n, tz * 0.75f, ty * 0.22f, P(White, 0.4f));
            }
            // a door and a little window down the hill
            d.Quad(X(-0.55f, 1.0f, hd + 0.012f), fz, fx * 0.45f, Vector3.up * 1.0f, P(Door, 0.3f));
            d.Quad(X(0.75f, 1.45f, hd + 0.012f), fz, fx * 0.4f, Vector3.up * 0.3f, P(Glass, 0.9f));
            d.Quad(X(0.75f, 1.45f, hd + 0.008f), fz, fx * 0.46f, Vector3.up * 0.36f, P(White, 0.4f));
            // a pitched roof, its ridge along x, overhanging; snow lying thick on both slopes
            float over = 0.35f, rise = 0.95f, th = 0.12f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 eave = X(0f, HutWall, s * (hd + over)), ridge = X(0f, HutWall + rise, 0f);
                Vector3 slopeDir = (ridge - eave).normalized, n = Vector3.Cross(fx, slopeDir) * -s;
                if (Vector3.Dot(n, Vector3.up) < 0f) n = -n;
                Vector3 mid = (eave + ridge) * 0.5f;
                float len = (ridge - eave).magnitude;
                d.Slab(mid, fx, slopeDir, n, HutW + 2f * over, len, th, P(Roof, 0.3f));
                d.Slab(mid + n * (th * 0.5f + 0.09f), fx, slopeDir, n, HutW + 2f * over - 0.1f, len - 0.06f, 0.18f, P(RoofSnow, 0.15f));
            }
            // the gables
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 n = fx * s;
                d.Tri(X(s * hw, HutWall, -hd), X(s * hw, HutWall, hd), X(s * hw, HutWall + rise * (hd / (hd + over)), 0f), n, P(HutRed, 0.35f));
            }
            // the rescue toboggan on the snow beside the door: a red tub with handles, along the fall line
            Vector3 t0 = X(-hw - 0.9f, 0f, hd + 0.6f);
            t0.y = Slope.DrawnHeight(t0.x, t0.z);
            Vector3 nrm = Slope.Normal(t0.x, t0.z);
            Vector3 along = Vector3.ProjectOnPlane(Vector3.forward, nrm).normalized, across = Vector3.Cross(nrm, along).normalized;
            var sled = P(Sled, 0.55f);
            d.Box(t0 + nrm * 0.08f, across, along, nrm, new Vector3(0.62f, 0.12f, 2.1f), sled);
            for (int s = -1; s <= 1; s += 2) d.Box(t0 + nrm * 0.2f + across * s * 0.29f, across, along, nrm, new Vector3(0.05f, 0.22f, 2.0f), sled);
            d.Box(t0 + nrm * 0.2f - along * 1.0f, across, along, nrm, new Vector3(0.6f, 0.22f, 0.05f), sled);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 h0 = t0 + nrm * 0.25f + across * s * 0.26f - along * 1.0f;
                d.Tube(h0, h0 - along * 1.1f + nrm * 0.25f, 0.02f, 5, P(Steel, 0.5f), P(Steel, 0.5f), false);
            }
        }

        // ---------------------------------------------------------------- mesh building

        public class MeshData
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();

            public Mesh Make(string name)
            {
                if (v.Count == 0) return null;
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v); m.SetNormals(n); m.SetColors(c);
                // (the helpers below wind counter-clockwise seen from the front; Unity's front faces are clockwise)
                for (int i = 0; i < t.Count; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                m.MarkDynamic();
                return m;
            }

            int V(Vector3 p, Vector3 nn, Color col) { v.Add(p); n.Add(nn); c.Add(col); return v.Count - 1; }

            /// <summary>A tube from a to b, `sides` round (c0 its side, c1 unused but for symmetry), capped at a if asked.</summary>
            public void Tube(Vector3 a, Vector3 b, float r, int sides, Color c0, Color c1, bool capTop)
            {
                Vector3 ax = b - a; float len = ax.magnitude;
                if (len < 1e-5f) return;
                ax /= len;
                Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) > 0.9f ? Vector3.right : Vector3.up).normalized, w = Vector3.Cross(ax, u);
                int s0 = v.Count;
                for (int i = 0; i <= sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides;
                    Vector3 nn = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                    V(a + nn * r, nn, c0); V(b + nn * r, nn, c0);
                }
                for (int i = 0; i < sides; i++)
                {
                    int k = s0 + i * 2;
                    t.Add(k); t.Add(k + 1); t.Add(k + 3);
                    t.Add(k); t.Add(k + 3); t.Add(k + 2);
                }
                if (capTop) Disc(b, ax, r, c0, sides, false);
            }

            public void Disc(Vector3 centre, Vector3 normal, float r, Color col, int sides, bool twoSided)
            {
                normal = normal.normalized;
                Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized, w = Vector3.Cross(normal, u);
                for (int f = 0; f < (twoSided ? 2 : 1); f++)
                {
                    Vector3 nn = f == 0 ? normal : -normal;
                    int c0 = V(centre + nn * 0.001f, nn, col);
                    for (int i = 0; i <= sides; i++)
                    {
                        float ang = i * Mathf.PI * 2f / sides;
                        V(centre + nn * 0.001f + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * r, nn, col);
                    }
                    for (int i = 0; i < sides; i++)
                    {
                        if (f == 0) { t.Add(c0); t.Add(c0 + 2 + i); t.Add(c0 + 1 + i); }
                        else { t.Add(c0); t.Add(c0 + 1 + i); t.Add(c0 + 2 + i); }
                    }
                }
            }

            /// <summary>A quad facing n, centred at mid, half-extents along ex and ey.</summary>
            public void Quad(Vector3 mid, Vector3 nn, Vector3 ex, Vector3 ey, Color col)
            {
                int a = V(mid - ex - ey, nn, col), b = V(mid + ex - ey, nn, col), c2 = V(mid + ex + ey, nn, col), d = V(mid - ex + ey, nn, col);
                // wound to face nn
                if (Vector3.Dot(Vector3.Cross(ex, ey), nn) > 0f) { t.Add(a); t.Add(c2); t.Add(b); t.Add(a); t.Add(d); t.Add(c2); }
                else { t.Add(a); t.Add(b); t.Add(c2); t.Add(a); t.Add(c2); t.Add(d); }
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c2, Vector3 nn, Color col)
            {
                int i0 = V(a, nn, col), i1 = V(b, nn, col), i2 = V(c2, nn, col);
                if (Vector3.Dot(Vector3.Cross(b - a, c2 - a), nn) < 0f) { t.Add(i0); t.Add(i1); t.Add(i2); }
                else { t.Add(i0); t.Add(i2); t.Add(i1); }
            }

            /// <summary>A box at mid, its axes x and z level (y up), its size along them.</summary>
            public void Box(Vector3 mid, Vector3 x, Vector3 z, Vector3 size, Color col) => Box(mid, x, z, Vector3.up, size, col);

            public void Box(Vector3 mid, Vector3 x, Vector3 z, Vector3 y, Vector3 size, Color col)
            {
                Vector3 hx = x * size.x * 0.5f, hy = y * size.y * 0.5f, hz = z * size.z * 0.5f;
                Quad(mid + hx, x, hz, hy, col); Quad(mid - hx, -x, hz, hy, col);
                Quad(mid + hy, y, hx, hz, col); Quad(mid - hy, -y, hx, hz, col);
                Quad(mid + hz, z, hx, hy, col); Quad(mid - hz, -z, hx, hy, col);
            }

            /// <summary>A slab: w along x, len along s, th along n (its face's normal).</summary>
            public void Slab(Vector3 mid, Vector3 x, Vector3 s, Vector3 nn, float w, float len, float th, Color col)
            {
                Vector3 hx = x * w * 0.5f, hs = s * len * 0.5f, hn = nn * th * 0.5f;
                Quad(mid + hn, nn, hx, hs, col); Quad(mid - hn, -nn, hx, hs, col);
                Quad(mid + hx, x, hs, hn, col); Quad(mid - hx, -x, hs, hn, col);
                Quad(mid + hs, s, hx, hn, col); Quad(mid - hs, -s, hx, hn, col);
            }
        }
    }
}
