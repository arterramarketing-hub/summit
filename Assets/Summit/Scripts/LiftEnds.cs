using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The ground at each end of a lift, and the people on it (lifts.md, in the notes: the plan and the research).
    ///
    /// At the bottom, where the chairs load, beside the station on the run's side (out of the chairs' way, which pass
    /// over the deck's uphill end): the load maze (posts and ropes on the snow, two lanes and a singles lane, narrowing to
    /// the preload at the deck's corner), ticket gates at its head, the liftie's hut beside the load point on the side
    /// the chairs come from, a rack of skis and boards, the trail map, the lift's name. People queue in it in groups of
    /// up to four and move up as each chair leaves, the front group stepping onto the deck to load; two friends chat by
    /// the rack, someone clicks into their skis, the liftie watches the load and waves the chairs on.
    ///
    /// At the top, where they unload: the deck's uphill end meets the snow, and beside it a flat to strap in on, the
    /// unload sign and the trail map. People come off the chairs (as LiftRiders lets them go inside the station), glide
    /// clear, strap in (snowboarders sitting on the snow) and push off down the run; others stand about: one pulling
    /// their goggles down, one pointing out a line, one sitting in the snow. At the Summit lift (LiftLine.LaySummit) the
    /// flat leads on along a lit path into the camp, the camp's sign at its start, and now and then someone carries
    /// their board along it.
    ///
    /// Where things stand: a strip beside the station from 7 to 13 m off the line (inside the lift's cleared swath, so no
    /// tree stands in it), off the groomed run, off anything kept (Course.Keep), off a hill and the lane to it (the way to
    /// a cable grind is never blocked: LiftLine.HillOrLane), on snow not too steep. A pure function of the lift.
    ///
    /// Solid things are solid as drawn (Obstacle): the hut, the rack, the gates, the boards and their posts, and the
    /// people (a capsule each, while they are drawn); the maze's posts and ropes are not solid (nothing invisible is).
    /// Plain URP Lit materials, one colour or one picture each, no keywords switched at run time and no instancing: a
    /// build keeps their variants with the rest (InstancedAssets). People are the mountain's own (Person), pooled, posed
    /// by this, drawn by distance (the full model near, the simplified one further, none past PeopleRange).
    /// </summary>
    public class LiftEnds : MonoBehaviour, IPersonHit
    {
        public static bool On = true;
        public static LiftEnds Instance { get; private set; }
        public const float BuildRange = 300f, DropRange = 360f, PeopleRange = 140f, FullRange = 24f;
        public const int PeopleCap = 22, FullCap = 6;
        // the strip beside a station: u, square off the line toward the run; v, along it (negative: up the line)
        public const float U0 = 7f, U1 = 13f, V0 = -18f, V1 = 6f;

        Transform target;
        Shader lit;
        Material white, wood, red, metal, dark, rope, yellow, path, orange, glass;
        readonly Dictionary<string, Material> signs = new Dictionary<string, Material>();
        readonly List<Mesh> meshes = new List<Mesh>();

        /// <summary>One end of one lift: where it is, and what was built there.</summary>
        public class End
        {
            public LiftLine.Lift lift;
            public bool top;
            public Vector3 st;        // the station's deck, its middle (deck height)
            public Vector3 dir;       // level, down the line
            public Vector3 side;      // level, square to the line toward the run
            public GameObject root;
            public readonly List<Vector3> queue = new List<Vector3>();   // the queue's places, from the front (bottom)
            public Vector3 load;      // where the front group steps up to load (bottom), where the unloaders come off (top)
            public readonly List<Vector3> strap = new List<Vector3>();   // places to strap in (top)
            public Vector3 hutDoor, rack, chatA, chatB, clickIn, sit, point, goggles;
            public readonly List<Vector3> toCamp = new List<Vector3>();  // the path into the camp (the Summit lift's top)
            public int walkEnd;       // how far along toCamp its walkers go (to the lodge's steps; the rest of the path is drawn on to the paved path)
            public readonly List<Vector3> pushWay = new List<Vector3>();  // the Summit top's riders' way off: round behind the station, down the far side
            public readonly List<Vector3> rideWay = new List<Vector3>();  // (the Summit top, LiftTop) the way into the camp, for the share who ride in
            public readonly List<(PersonLook look, int seat, Vector3 at, Vector3 travel)> arriving = new List<(PersonLook, int, Vector3, Vector3)>();
            public float lastTick = -1f;
            public int seenAt = -1;   // (the chairs' count when it last ticked: LiftRiders.SeenCount)
            public readonly List<Agent> people = new List<Agent>();
            public Vector3 P(float u, float v) => Snow(st + side * u + dir * v);
        }

        public enum Job { Queue, Load, Liftie, Chat, ClickIn, Unload, Strap, Push, Goggles, Point, Sit, Walk, Doff, Camp }

        public class Agent
        {
            public Person p;
            public Job job;
            public End end;
            public Vector3 pos, goal;
            public float yaw, t, until, speed;
            public int slot, group, seatSlot;
            public Agent partner;
            public CapsuleCollider hit;
            public bool intoCamp;     // (the Summit top, LiftTop: riding into the camp, not off the far side)
            public bool handed;       // off a chair as the same person who sat on it (LiftRiders.Unloaded): gear on, gliding
            public bool shown;
            public Ragdoll down;      // run down (rides2): thrown, and lying where they landed
            public float downT, downStill;
        }

        readonly List<End> ends = new List<End>();
        /// <summary>Groups that have stepped up to load, and people who have pushed off down the run (for the tests).</summary>
        public int Loaded, Pushed;
        readonly List<Agent> pool = new List<Agent>();
        int seedFor = int.MinValue, spawned;
        float clock;

        /// <summary>Made with the lifts (SkiLift.Create): the ends of whatever mountain is current, round this target.</summary>
        public static LiftEnds Create(Transform parent, Transform target, Shader lit)
        {
            var go = new GameObject("LiftEnds");
            go.transform.SetParent(parent, false);
            var e = go.AddComponent<LiftEnds>();
            e.target = target; e.lit = lit;
            Instance = e;
            LiftRiders.Unloaded = e.OffChair;
            return e;
        }

        void OnDestroy()
        {
            Clear();
            foreach (var m in meshes) if (m != null) Destroy(m);
            if (Instance == this) { Instance = null; LiftRiders.Unloaded = null; }
        }

        static Vector3 Snow(Vector3 p) { p.y = Slope.DrawnHeight(p.x, p.z); return p; }

        // ---------------------------------------------------------------- materials

        Material Mat(string name, Color c, float smooth = 0.2f)
        {
            var sh = lit != null ? lit : Mats.LitShader;
            var m = new Material(sh) { name = "LiftEnds" + name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        Material Sign(string name)
        {
            if (signs.TryGetValue(name, out var m)) return m;
            var tex = Resources.Load<Texture2D>("Signs/" + name);
            m = Mat("Sign" + name, Color.white, 0.35f);
            if (tex != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            signs[name] = m;
            return m;
        }

        void Materials()
        {
            if (white != null) return;
            white = Mat("White", new Color(0.93f, 0.94f, 0.95f));
            wood = Mat("Wood", new Color(0.42f, 0.28f, 0.17f));
            red = Mat("Red", new Color(0.72f, 0.12f, 0.10f), 0.35f);
            metal = Mat("Metal", new Color(0.62f, 0.64f, 0.66f), 0.55f);
            dark = Mat("Dark", new Color(0.12f, 0.13f, 0.15f), 0.4f);
            rope = Mat("Rope", new Color(0.10f, 0.32f, 0.78f), 0.2f);
            yellow = Mat("Yellow", new Color(0.98f, 0.78f, 0.10f), 0.4f);
            orange = Mat("Orange", new Color(1.0f, 0.45f, 0.05f), 0.4f);
            path = Mat("Path", new Color(0.86f, 0.89f, 0.94f), 0.05f);
            glass = Mat("Glass", new Color(0.20f, 0.30f, 0.38f), 0.85f);
        }

        // ---------------------------------------------------------------- where each end is

        /// <summary>Every lift end on today's mountain (pure: worked out once a mountain).</summary>
        public List<End> Ends { get { Plan(); return ends; } }

        void Plan()
        {
            if (seedFor == Slope.Seed) return;
            Clear();
            seedFor = Slope.Seed;
            foreach (var l in LiftLine.Lifts)
                for (int e = 0; e < 2; e++)
                {
                    var end = new End { lift = l, top = e == 0 };
                    end.st = end.top ? l.top : l.bottom;
                    end.dir = l.dir;
                    // square to the line, toward the run
                    float a = end.st.x - Slope.Centre(end.st.z);
                    end.side = l.across * (Vector3.Dot(l.across, Vector3.right) * (a > 0f ? -1f : 1f) >= 0f ? 1f : -1f);
                    Lay(end);
                    ends.Add(end);
                }
        }

        /// <summary>Is (x, z) ground the end's things may stand on?</summary>
        public static string Why = "";

        static bool Clear(float x, float z, bool camp = false)
        {
            // (off the groomed run; behind the gate there is none, and the line's wander back there means nothing: the
            // Summit lift's top beside the lodge, camplayout)
            Why = "run"; if (z > Slope.GateZ && Mathf.Abs(Slope.Across(x, z)) < Slope.Half(z) + 2.5f) return false;
            Why = "kept"; if (Course.Keep(x, z, 1f) || Deadly.Below(x, z) || Cliffs.WallCut(x, z) < -0.05f) return false;
            Why = "hill"; if (LiftLine.HillOrLane(x, z, 1.5f)) return false;                         // (the way to a cable grind stays open)
            Why = "hamlet"; if (Hamlets.Taken(x, z) || TrailSigns.Near(x, z, 2f)) return false;
            Why = "pad"; if (!camp && Slope.OnPad(x, z)) return false;
            // (not on a face or bare rock: the mountain's own pitch is 36 degrees, so a station's ground is seldom flat;
            // a resort would grade it, this stands its people on it as it is, up to about 50 degrees)
            var n = Slope.Normal(x, z);
            Why = "steep"; if (n.y < 0.64f) return false;
            Why = "rock"; return !RockFace.IsRock(n, x, z);
        }

        /// <summary>
        /// The Summit lift's path from its unload into the camp (camplayout). Beside the lodge's wing (its top's usual
        /// place now): round the wing's front corner, clear of the drifts against it, to the foot of the terrace's steps by
        /// the wing (where its walkers go in: walkEnd), and on along the terrace's front to join the paved path to the gate.
        /// Where the top had to stand in front of the camp's left corner (its old place): in to the camp's left edge as
        /// before, and on to the paved path. The camp keeps its mounds off this line, and the hero arrives at its start.
        /// </summary>
        public static List<Vector3> CampPath(LiftLine.Lift l, out int walkEnd)
        {
            // (lifttop: on the summit bench, from the unload's flat, the way it rides in along)
            if (LiftTop.AtBench(l))
            {
                var ride = LiftTop.RideIn(out walkEnd);
                if (ride.Count > 1)
                {
                    float L0 = LiftLine.StationHalfLen;
                    float a1 = l.top.x - Slope.Centre(l.top.z);
                    var side1 = l.across * (Vector3.Dot(l.across, Vector3.right) * (a1 > 0f ? -1f : 1f) >= 0f ? 1f : -1f);
                    ride.Insert(0, Snow(l.top + side1 * 8f + l.dir * (-L0 - 2f)));
                    walkEnd++;
                    return ride;
                }
            }
            var pts = new List<Vector3>();
            float L = LiftLine.StationHalfLen, cx = Slope.Centre(Slope.PadLip);
            var st = l.top;
            float a0 = st.x - Slope.Centre(st.z);
            var side = l.across * (Vector3.Dot(l.across, Vector3.right) * (a0 > 0f ? -1f : 1f) >= 0f ? 1f : -1f);
            var start = st + side * 8f + l.dir * (-L - 2f);
            var way = new List<Vector3> { start };
            int stop;
            if (st.z < -76f)
            {
                // (in the lodge's own frame, MakeLodge.py's: round the wing's end and its front corner, to the foot of the
                // steps by the wing (STAIRS' second flight), then along the terrace's front to the paved path)
                foreach (var (x, z) in new[] { (-23.1f, -0.8f), (-22.6f, 7.3f), (-20.9f, 14.1f), (-16.7f, 16.9f), (-13.4f, 18.4f) }) way.Add(Camp.LodgeGround(x, z));
                stop = way.Count - 1;
                foreach (var (x, z) in new[] { (-8.4f, 18.9f), (-3.9f, 19.6f), (-1.3f, 20.1f) }) way.Add(Camp.LodgeGround(x, z));
            }
            else
            {
                var b = new Vector3(cx - Camp.HalfWidth + 2f, 0f, Mathf.Clamp(st.z - 6f, Camp.BackZ + 20f, -44f));
                for (int i = 1; i <= 4; i++)
                {
                    float t = i / 4f;
                    way.Add(Vector3.Lerp(start, b, t) + Vector3.Cross(Vector3.up, (b - start).normalized) * Mathf.Sin(t * Mathf.PI) * 2.5f);
                }
                stop = way.Count - 1;
                way.Add(new Vector3(cx - 14f, 0f, b.z + 1f)); way.Add(new Vector3(cx - 5.2f, 0f, -65.5f));
            }
            // a smooth line through them (Catmull-Rom), a point every metre and a half or so
            walkEnd = 0;
            for (int i = 0; i < way.Count - 1; i++)
            {
                Vector3 p0 = way[Mathf.Max(0, i - 1)], p1 = way[i], p2 = way[i + 1], p3 = way[Mathf.Min(way.Count - 1, i + 2)];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(new Vector3(p1.x, 0f, p1.z), new Vector3(p2.x, 0f, p2.z)) / 1.5f));
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                    var q = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
                    pts.Add(Snow(q));
                }
                if (i + 1 == stop) walkEnd = pts.Count;
            }
            pts.Add(Snow(way[way.Count - 1]));
            return pts;
        }

        /// <summary>The Summit lift's path into the camp on today's mountain (empty if it has none): for the camp's mounds and the hero's arrival.</summary>
        public static List<Vector3> SummitPath()
        {
            if (summitPathFor != Slope.Seed)
            {
                summitPathFor = Slope.Seed; summitPath.Clear();
                foreach (var l in LiftLine.Lifts) if (l.summit) summitPath.AddRange(CampPath(l, out _));
            }
            return summitPath;
        }
        static readonly List<Vector3> summitPath = new List<Vector3>();
        static int summitPathFor = int.MinValue;

        bool Ok(End e, float u, float v) { var p = e.st + e.side * u + e.dir * v; return Clear(p.x, p.z, e.lift.summit && e.top); }

        /// <summary>Why each place of the strip would not do, as a grid (for checking).</summary>
        public string Grid(End e)
        {
            var sb = new System.Text.StringBuilder();
            for (float v = V0 - 6f; v <= V1; v += 3f)
            {
                sb.Append($"v{v:+0;-0}:");
                for (float u = U0; u <= U1; u += 1.5f) sb.Append(Ok(e, u, v) ? " ok" : " " + Why);
                sb.Append(" | ");
            }
            return sb.ToString();
        }

        void Lay(End e)
        {
            float L = LiftLine.StationHalfLen;
            if (!e.top)
            {
                // the queue: from the preload at the deck's uphill corner, back along the maze's two lanes (snaking)
                e.load = e.P(3.2f, -L + 0.6f); e.load.y = e.st.y;   // (on the deck, its uphill end: under the chairs as they start up)
                for (int i = 0; i < 12; i++)
                {
                    int lane = (i / 6);
                    float v = lane == 0 ? -L - 1.2f - i * 1.6f : -L - 1.2f - (11 - i) * 1.6f;
                    float u = lane == 0 ? 8.1f : 9.7f;
                    if (!Ok(e, u, v)) continue;
                    e.queue.Add(e.P(u, v));
                }
                e.hutDoor = Ok(e, U0 + 1.0f, 1.5f) ? e.P(U0 + 1.0f, 1.5f) : Vector3.zero;
                e.rack = e.P(U1 - 0.6f, -2f);
                bool chat = Ok(e, U1 - 1.4f, 1.2f) && Ok(e, U1 - 1.4f, 2.4f);
                e.chatA = chat ? e.P(U1 - 1.4f, 1.2f) : Vector3.zero; e.chatB = chat ? e.P(U1 - 1.4f, 2.4f) : Vector3.zero;
                e.clickIn = Ok(e, U1 - 0.5f, -L - 13f) ? e.P(U1 - 0.5f, -L - 13f) : Vector3.zero;
            }
            else
            {
                // off the deck at its uphill end, then clear of it onto the flat beside the station
                e.load = e.P(6.2f, -L - 0.6f);
                foreach (var (u, v) in new[] { (9f, -L - 4f), (11.5f, -L - 6f), (8.5f, -L - 8f), (11f, -L - 10.5f), (12.5f, -L - 3f) })
                    if (Ok(e, u, v)) e.strap.Add(e.P(u, v));
                e.goggles = Ok(e, U0 + 0.8f, -L - 11f) ? e.P(U0 + 0.8f, -L - 11f) : Vector3.zero;
                e.point = Ok(e, U1 - 0.4f, -L - 8.5f) ? e.P(U1 - 0.4f, -L - 8.5f) : Vector3.zero;
                e.sit = Ok(e, U1 - 1f, -L - 13f) ? e.P(U1 - 1f, -L - 13f) : Vector3.zero;
                if (e.lift.summit)
                {
                    // on along a path into the camp (CampPath), and the riders' way off down the shoulder on the station's
                    // far side, round behind its uphill end: not across the camp (camplayout)
                    e.toCamp.AddRange(CampPath(e.lift, out e.walkEnd));
                    // (lifttop2: at the gate's level some ride along the way to the gate and go up into the camp, the rest
                    // on into the in-run, over the lip and on down the run)
                    if (LiftTop.AtBench(e.lift))
                    {
                        var ride = LiftTop.RideIn(out int rideEnd); for (int i = 0; i < rideEnd; i++) e.rideWay.Add(ride[i]);
                        var far = LiftTop.FarSide();
                        e.pushWay.AddRange(far);
                        if (far.Count > 0)
                        {
                            var last = far[far.Count - 1];
                            float off = last.x - Slope.Centre(last.z);
                            for (float z = last.z + 12f; z < last.z + 260f; z += 12f)
                            {
                                float k = Mathf.Clamp01((z - last.z) / 40f);
                                e.pushWay.Add(Snow(new Vector3(Slope.Centre(z) + Mathf.Lerp(off, -Slope.HalfAt(z) * 0.55f, k), 0f, z)));
                            }
                        }
                    }
                    else
                    foreach (var (u, v) in new[] { (4f, -L - 12f), (-9f, -L - 10f), (-14f, 10f), (-16f, 45f) }) e.pushWay.Add(e.P(u, v));
                }
            }
        }

        // ---------------------------------------------------------------- building what stands there

        void Build(End e)
        {
            Materials();
            e.root = new GameObject($"LiftEnd {e.lift.id} {(e.top ? "top" : "bottom")}");
            e.root.transform.SetParent(transform, false);
            float L = LiftLine.StationHalfLen;
            Quaternion along = Quaternion.LookRotation(e.dir, Vector3.up);   // +z down the line
            Quaternion facingUp = Quaternion.LookRotation(-e.dir, Vector3.up);
            Quaternion facingRun = Quaternion.LookRotation(e.side, Vector3.up);
            string liftSign = e.lift.summit ? "LiftSummit" : e.lift.park ? "LiftPark" : "LiftChair";
            if (!e.top)
            {
                // the maze: three rows of posts, ropes between, from the gates down to the preload
                for (int row = 0; row < 3; row++)
                {
                    float u = 7.3f + row * 1.6f;
                    Vector3 last = Vector3.zero; bool have = false;
                    for (float v = -L - 0.8f; v >= -L - 19.5f; v -= 2.4f)
                    {
                        if (!Ok(e, u, v)) { have = false; continue; }
                        var foot = e.P(u, v);
                        Post(e.root.transform, foot, 0.95f, 0.04f, metal);
                        var top = foot + Vector3.up * 0.88f;
                        if (have) Rope(e.root.transform, last, top);
                        last = top; have = true;
                    }
                }
                // the ticket gates at the maze's head
                for (int g = 0; g < 2; g++)
                {
                    float u = 8.1f + g * 1.6f, v = -L - 20.5f;
                    if (!Ok(e, u, v)) continue;
                    Solid(e.root.transform, "Gate", PrimitiveType.Cube, e.P(u, v) + Vector3.up * 0.55f, along, new Vector3(0.35f, 1.1f, 0.5f), yellow, "Ticket gate");
                    Solid(e.root.transform, "GateTop", PrimitiveType.Cube, e.P(u, v) + Vector3.up * 1.12f, along, new Vector3(0.38f, 0.06f, 0.55f), dark, "Ticket gate");
                }
                // the liftie's hut, by the load point on the side the chairs come from
                if (Ok(e, U0 + 1.6f, 3.5f)) Hut(e.root.transform, e.P(U0 + 1.6f, 3.5f), facingRun, liftSign);
                // the rack, skis and boards leaning on it
                if (Ok(e, U1 - 0.4f, -2f)) Rack(e.root.transform, e.P(U1 - 0.4f, -2f), along, e.lift.id);
                // the lift's name over the maze's head, the singles lane's sign, the trail map facing the way in
                if (Ok(e, U0, -L - 22f)) Board(e.root.transform, e.P(U0, -L - 22f), facingUp, liftSign, 2.6f, 0.65f, 1.9f);
                if (Ok(e, 7.3f + 2f * 1.6f + 0.6f, -L - 19.5f)) Board(e.root.transform, e.P(7.3f + 2f * 1.6f + 0.6f, -L - 19.5f), facingUp, "LiftSingles", 0.9f, 0.45f, 1.3f);
                if (Ok(e, U1, -L - 22.5f)) Board(e.root.transform, e.P(U1, -L - 22.5f), facingUp, "TrailMap", 2.4f, 1.5f, 1.1f);
            }
            else
            {
                // the unload: its sign facing the chairs coming in, the trail map, wands marking the flat's run side
                if (Ok(e, U0 - 0.5f, -L - 1.5f)) Board(e.root.transform, e.P(U0 - 0.5f, -L - 1.5f), Quaternion.LookRotation(e.dir + e.side * 0.3f, Vector3.up), e.lift.summit ? "LiftCamp" : "LiftUnload", 2.2f, 0.55f, 1.8f);
                if (Ok(e, U1, -L - 5f)) Board(e.root.transform, e.P(U1, -L - 5f), facingRun, "TrailMap", 2.4f, 1.5f, 1.1f);
                if (Ok(e, U1 + 0.5f, -L + 2f)) Board(e.root.transform, e.P(U1 + 0.5f, -L + 2f), facingRun, liftSign, 2.2f, 0.55f, 1.8f);
                // (not at the Summit top's pad, lifttop2: there the flat opens onto its two groomed ways on that side)
                if (!LiftTop.AtBench(e.lift))
                    for (float v = -L - 3f; v >= -L - 15f; v -= 3f)
                        if (Ok(e, U1 + 1.5f, v)) Post(e.root.transform, e.P(U1 + 1.5f, v), 1.4f, 0.025f, orange);
                if (e.toCamp.Count > 1) PathToCamp(e);
            }
        }

        // a post: a thin cylinder standing on the snow (not solid: a maze stanchion, a wand)
        void Post(Transform parent, Vector3 foot, float h, float r, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = "Post";
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(foot + Vector3.up * (h * 0.5f - 0.05f), Quaternion.identity);
            g.transform.localScale = new Vector3(2f * r, h * 0.5f, 2f * r);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // a rope between two post tops, sagging a little (not solid)
        void Rope(Transform parent, Vector3 a, Vector3 b)
        {
            const int n = 4;
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                Vector3 p0 = Vector3.Lerp(a, b, t0) + Vector3.down * 0.12f * 4f * t0 * (1f - t0);
                Vector3 p1 = Vector3.Lerp(a, b, t1) + Vector3.down * 0.12f * 4f * t1 * (1f - t1);
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.name = "Rope";
                g.transform.SetParent(parent, false);
                g.transform.SetPositionAndRotation((p0 + p1) * 0.5f, Quaternion.LookRotation(p1 - p0, Vector3.up));
                g.transform.localScale = new Vector3(0.035f, 0.035f, (p1 - p0).magnitude + 0.02f);
                var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = rope; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // something solid as drawn: a primitive with its own collider, an obstacle
        GameObject Solid(Transform parent, string name, PrimitiveType t, Vector3 at, Quaternion rot, Vector3 size, Material m, string label)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(at, rot);
            g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            g.AddComponent<Obstacle>().label = label;
            return g;
        }

        // drawn only (part of something whose collider is another piece)
        GameObject Part(Transform parent, PrimitiveType t, Vector3 at, Quaternion rot, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(at, rot);
            g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        /// <summary>The liftie's hut: a little red cabin on a plinth, a window looking at the load, the lift's name on it.</summary>
        void Hut(Transform parent, Vector3 foot, Quaternion face, string liftSign)
        {
            var t = new GameObject("LiftieHut").transform;
            t.SetParent(parent, false);
            // level on the snow's highest corner, its plinth down to the lowest
            float hi = foot.y, lo = foot.y;
            foreach (var c in new[] { new Vector3(-1.2f, 0f, -1.1f), new Vector3(1.2f, 0f, -1.1f), new Vector3(-1.2f, 0f, 1.1f), new Vector3(1.2f, 0f, 1.1f) })
            {
                var w = foot + face * c; float y = Slope.DrawnHeight(w.x, w.z); hi = Mathf.Max(hi, y); lo = Mathf.Min(lo, y);
            }
            var floor = new Vector3(foot.x, hi + 0.15f, foot.z);
            t.SetPositionAndRotation(floor, face);
            float plinth = hi - lo + 0.3f;
            Solid(t, "Plinth", PrimitiveType.Cube, floor + Vector3.down * (plinth * 0.5f - 0.05f), face, new Vector3(2.5f, plinth, 2.3f), dark, "Lift hut");
            Solid(t, "Walls", PrimitiveType.Cube, floor + Vector3.up * 1.2f, face, new Vector3(2.3f, 2.4f, 2.1f), red, "Lift hut");
            Part(t, PrimitiveType.Cube, floor + Vector3.up * 2.5f, face, new Vector3(2.7f, 0.22f, 2.5f), white);              // the roof
            Part(t, PrimitiveType.Cube, floor + face * new Vector3(0f, 1.45f, 1.06f), face, new Vector3(1.6f, 0.8f, 0.04f), glass);   // the window toward the run
            Part(t, PrimitiveType.Cube, floor + face * new Vector3(-1.16f, 1.45f, 0.2f), face, new Vector3(0.04f, 0.8f, 1.2f), glass); // and toward the load
            Part(t, PrimitiveType.Cube, floor + face * new Vector3(1.16f, 1.0f, 0f), face, new Vector3(0.05f, 2.0f, 0.9f), wood);    // the door
            var q = Part(t, PrimitiveType.Quad, floor + face * new Vector3(0f, 2.15f, 1.08f), face * Quaternion.Euler(0f, 180f, 0f), new Vector3(1.9f, 0.48f, 1f), Sign(liftSign));
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>A rack: two posts and a rail, skis and boards leaning on it.</summary>
        void Rack(Transform parent, Vector3 foot, Quaternion along, int seed)
        {
            var t = new GameObject("Rack").transform;
            t.SetParent(parent, false);
            const float len = 2.8f, h = 1.15f;
            Vector3 dirv = along * Vector3.forward, sidev = along * Vector3.right;
            for (int s = -1; s <= 1; s += 2)
            {
                var f = Snow(foot + dirv * s * len * 0.5f);
                Part(t, PrimitiveType.Cube, f + Vector3.up * (h * 0.5f - 0.1f), along, new Vector3(0.1f, h + 0.2f, 0.1f), wood);
            }
            var bar = Snow(foot) + Vector3.up * h;
            Part(t, PrimitiveType.Cube, bar, along, new Vector3(0.08f, 0.08f, len + 0.1f), wood);
            // what leans on it: from the people kit, the skis and boards the mountain's people ride
            int n = 5;
            for (int i = 0; i < n; i++)
            {
                float along_ = (i - (n - 1) * 0.5f) * (len / n);
                bool board = Slope.Hash01(seed, i, 311) < 0.5f;
                var basePt = Snow(foot + dirv * along_ + sidev * 0.45f);
                Quaternion lean = Quaternion.LookRotation(dirv, (Vector3.up - sidev * 0.35f).normalized);
                Mesh mesh = board ? PeopleKit.BoardMesh : PeopleKit.SkiMesh;
                Material[] mats = board ? PeopleKit.BoardSrcMats : PeopleKit.SkiSrcMats;
                if (mesh != null && mats != null && mats.Length > 0)
                {
                    var g = new GameObject(board ? "Board" : "Skis");
                    g.transform.SetParent(t, false);
                    // (the kit's piece lies along its own length: stood up on its tail, leaning on the rail)
                    var b = mesh.bounds;
                    float length = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    Vector3 axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? Vector3.right : b.size.y >= b.size.z ? Vector3.up : Vector3.forward;
                    g.transform.SetPositionAndRotation(basePt + lean * Vector3.up * (length * 0.5f), lean * Quaternion.FromToRotation(axis, Vector3.up) * Quaternion.Inverse(Quaternion.identity));
                    g.transform.position -= g.transform.rotation * b.center;
                    g.AddComponent<MeshFilter>().sharedMesh = mesh;
                    g.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }
                else
                    Part(t, PrimitiveType.Cube, basePt + lean * Vector3.up * 0.8f, lean, new Vector3(0.28f, 1.6f, 0.03f), board ? orange : white);
            }
            // solid: the rack and what leans on it, as one box
            var c = new GameObject("RackSolid");
            c.transform.SetParent(t, false);
            c.transform.SetPositionAndRotation(Snow(foot) + Vector3.up * 0.75f + sidev * 0.2f, along);
            c.AddComponent<BoxCollider>().size = new Vector3(0.75f, 1.5f, len + 0.2f);
            c.AddComponent<Obstacle>().label = "Ski rack";
        }

        /// <summary>A board on two posts, its picture facing `face`'s forward: a lift's name, the singles lane, the trail map.</summary>
        void Board(Transform parent, Vector3 foot, Quaternion face, string sign, float w, float hgt, float lift)
        {
            var t = new GameObject("Board " + sign).transform;
            t.SetParent(parent, false);
            Vector3 right = face * Vector3.right;
            for (int s = -1; s <= 1; s += 2)
            {
                var f = Snow(foot + right * s * (w * 0.5f - 0.1f));
                float top = foot.y + lift + hgt;
                Solid(t, "Leg", PrimitiveType.Cube, new Vector3(f.x, (f.y - 0.3f + top) * 0.5f, f.z), face, new Vector3(0.1f, top - f.y + 0.3f, 0.1f), wood, "Sign");
            }
            var mid = foot + Vector3.up * (lift + hgt * 0.5f);
            Solid(t, "Back", PrimitiveType.Cube, mid - face * Vector3.forward * 0.03f, face, new Vector3(w + 0.08f, hgt + 0.08f, 0.05f), dark, "Sign");
            var q = Part(t, PrimitiveType.Quad, mid + face * Vector3.forward * 0.005f, face * Quaternion.Euler(0f, 180f, 0f), new Vector3(w, hgt, 1f), Sign(sign));
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The Summit lift's path into the camp: packed snow a little darker than the powder, wands along it.</summary>
        void PathToCamp(End e)
        {
            bool bench = LiftTop.AtBench(e.lift);   // (its groomed way to the gate is ridden: no wands along it)
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i < e.toCamp.Count; i++)
            {
                var a = e.toCamp[Mathf.Max(0, i - 1)]; var b = e.toCamp[Mathf.Min(e.toCamp.Count - 1, i + 1)];
                var d = b - a; d.y = 0f; d.Normalize();
                var s = new Vector3(d.z, 0f, -d.x) * 1.1f;
                foreach (float k in new[] { -1f, 1f })
                {
                    var q = e.toCamp[i] + s * k; q.y = Slope.DrawnHeight(q.x, q.z) + 0.04f; v.Add(q - e.toCamp[0]);
                }
                if (i > 0) { int o = v.Count - 4; tri.Add(o); tri.Add(o + 2); tri.Add(o + 1); tri.Add(o + 1); tri.Add(o + 2); tri.Add(o + 3); }
                if (i % 3 == 1 && Mathf.Abs(e.toCamp[i].x - Slope.Centre(Slope.PadLip)) > Camp.HalfWidth + 1f && !(bench && i < e.walkEnd)) Post(e.root.transform, e.toCamp[i] + s * 1.6f, 1.4f, 0.025f, orange);   // (wands out on the open snow, not in the camp)
            }
            var m = new Mesh { name = "LiftEndsPath" };
            m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            meshes.Add(m);
            var g = new GameObject("PathToCamp");
            g.transform.SetParent(e.root.transform, false);
            g.transform.position = e.toCamp[0];
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = path; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- streaming them round the rider

        void Clear()
        {
            foreach (var e in ends)
            {
                foreach (var a in e.people) Park(a);
                e.people.Clear();
                if (e.root != null) { if (Application.isPlaying) Destroy(e.root); else DestroyImmediate(e.root); }
            }
            ends.Clear();
            seedFor = int.MinValue;
        }

        Vector3 Eye()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position : target != null ? target.position : Vector3.zero;
        }

        void Update()
        {
            if (!On) return;
            Step(Time.deltaTime, Eye());
        }

        /// <summary>For the batch tools: build and pose round this point, `seconds` on.</summary>
        public void Prewarm(Vector3 eye, float seconds = 0f)
        {
            Step(0f, eye);
            for (float t = 0f; t < seconds; t += 1f / 30f) Step(1f / 30f, eye);
        }

        public void Step(float dt, Vector3 eye)
        {
            Plan();
            clock += dt;
            Sight(eye);
            // the ends in range are built; those well out of it go
            foreach (var e in ends)
            {
                float d = Vector3.Distance(Flat(eye), Flat(e.st));
                if (e.root == null && d < BuildRange) Build(e);
                else if (e.root != null && d > DropRange) { foreach (var a in e.people) Park(a); e.people.Clear(); if (Application.isPlaying) Destroy(e.root); else DestroyImmediate(e.root); e.root = null; }
            }
            if (!PeopleKit.Ok) return;
            // people: the nearest ends in PeopleRange get theirs, up to the cap
            Staff(eye);
            int shown = 0;
            foreach (var e in ends)
            {
                if (e.people.Count == 0) continue;
                // a chair goes by: the bottom's front group loads and the rest move up; at the top its riders get off
                // (the same people: LiftRiders.Unloaded; without the chairs' people, someone new)
                float T = Interval(e.lift);
                int chair = Mathf.FloorToInt(clock / T);
                if (Mathf.FloorToInt(e.lastTick / T) != chair)
                {
                    e.lastTick = clock;
                    if (!e.top) Chair(e, Group(e.lift, chair));
                    else
                    {
                        // (the chairs not running here, the test tools' way: someone new gets off, as before)
                        int seen = LiftRiders.SeenCount(e.lift.id);
                        bool live = LiftRiders.On && e.seenAt >= 0 && seen != e.seenAt;
                        e.seenAt = seen;
                        if (!live) Arrive(e, Group(e.lift, chair));
                    }
                }
                if (e.top) { foreach (var o in e.arriving) Handover(e, o.look, o.seat, o.at, o.travel); e.arriving.Clear(); }
                work.Clear(); work.AddRange(e.people);
                foreach (var a in work)
                {
                    if (a.end != e) continue;
                    if (a.down != null) { Down(a, dt); continue; }   // (run down: thrown, lying there: rides2)
                    Move(a, dt);
                    if (a.end != e) continue;
                    if (a.job == Job.Camp) continue;   // (the camp walks them now: CampLife)
                    float d = Vector3.Distance(a.pos, eye);
                    int lod = !a.shown ? 2 : d < FullRange && shown < FullCap ? 0 : d < PeopleRange ? 1 : 2;
                    if (lod == 0) shown++;
                    a.p.gameObject.SetActive(lod < 2);
                    a.p.SetLod(lod);
                    if (a.hit != null) a.hit.enabled = lod < 2 && d < 70f;
                    if (lod < 2) { Pose(a, dt); a.p.Apply(dt); if (a.job == Job.Unload && a.handed && a.p.Look.gear != PersonGear.Skis) Skate(a); }
                }
            }
        }

        readonly List<Agent> work = new List<Agent>();

        // ---------------------------------------------------------------- run down (rides2)
        // The people here are as solid as the mountain's riders and go down the same way when run into (Rider.MeetPerson):
        // thrown on their own skeleton (Ragdoll, as MountainRiders.Knock), bleeding as the rider does (PeopleBlood), and
        // once they have lain still a moment, sat up in the snow where they landed, dazed, until they are next sent off.

        /// <summary>People here knocked over (for the tests).</summary>
        public static int Knocked;
        /// <summary>A knocked person's grunt, for the game to play (where, a woman's?).</summary>
        public System.Action<Vector3, bool> Grunted;
        readonly Dictionary<Person, Ragdoll> rags = new Dictionary<Person, Ragdoll>();
        global::Summit.Rider hero;

        Agent Of(Collider c) { if (c == null) return null; foreach (var a in pool) if (a.hit == c) return a; return null; }
        public int IdOf(Collider c) { var a = Of(c); return a != null ? a.p.GetInstanceID() : -1; }
        public Vector3 VelocityOf(Collider c)
        {
            var a = Of(c);
            return a == null || a.down != null ? Vector3.zero : Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward * a.speed;
        }

        public void Knock(Collider c, Vector3 throwVelocity, Vector3 at)
        {
            var a = Of(c);
            if (a == null || a.down != null || a.end == null || a.job == Job.Camp || a.p == null) return;
            var rag = RagFor(a.p);
            if (rag == null) return;
            Knocked++;
            Grunted?.Invoke(a.pos + Vector3.up, a.p.Look.female);
            if (a.partner != null) { a.partner.partner = null; a.partner = null; }
            // (posed as they stood, in the full model, then thrown from there)
            a.p.gameObject.SetActive(true); a.p.SetLod(0); Pose(a, 0f); a.p.Apply(0f);
            for (int i = 0; i < 2; i++) { if (a.p.Skis[i] != null) a.p.Skis[i].enabled = false; if (a.p.Poles[i] != null) a.p.Poles[i].enabled = false; }
            if (a.p.Board != null) a.p.Board.enabled = false;
            rag.Begin(throwVelocity);
            a.down = rag; a.downT = 0f; a.downStill = 0f; a.speed = 0f;
            if (a.hit != null) a.hit.enabled = false;
        }

        Ragdoll RagFor(Person p)
        {
            if (rags.TryGetValue(p, out var rag)) return rag;
            var rig = new RiderRig(p.Model);
            if (!rig.Ok) return null;
            if (hero == null) hero = FindFirstObjectByType<global::Summit.Rider>();
            var heroCol = hero != null ? hero.GetComponent<Collider>() : null;
            // (nothing in their hands flies off: a token, unseen)
            var g = new GameObject("KnockToken"); g.transform.SetParent(p.transform, false);
            rag = new Ragdoll(rig, g.transform, heroCol);
            rag.SetBoard(g.transform, new Vector3(0.08f, 0.02f, 0.2f), Vector3.zero, false);
            PeopleBlood.Hook(rag, rig);
            rags[p] = rag;
            return rag;
        }

        /// <summary>A thrown person, a frame: lying still a moment, laid to rest, then sat up where they lie.</summary>
        void Down(Agent a, float dt)
        {
            var rag = a.down;
            a.downT += dt;
            Vector3 c = rag.Centre;
            a.pos = Snow(c);
            if (rag.Speed < 0.7f) a.downStill += dt; else a.downStill = 0f;
            if (a.downStill > 1.2f) rag.Settle(dt, a.downStill > 2.4f);
            PeopleBlood.Body(rag, dt);
            if (a.downStill > 3.2f || a.downT > 25f)
            {
                var face = rag.Velocity; face.y = 0f;
                Up(a);
                a.job = Job.Sit; a.t = 0f; a.until = 0f; a.speed = 0f;
                if (face.sqrMagnitude > 0.01f) a.yaw = Yaw(face, Vector3.forward);
                if (a.hit != null) a.hit.enabled = true;
            }
        }

        void Up(Agent a)
        {
            if (a.down == null) return;
            a.down.End();
            a.down = null;
            if (a.p != null)
            {
                for (int i = 0; i < 2; i++) { if (a.p.Skis[i] != null) a.p.Skis[i].enabled = true; if (a.p.Poles[i] != null) a.p.Poles[i].enabled = true; }
                if (a.p.Board != null) a.p.Board.enabled = true;
            }
        }

        /// <summary>The thrown people's physics: a fixed step's worth (FixedUpdate; the tests call it).</summary>
        public void PhysicsStep() { foreach (var a in pool) if (a.down != null) a.down.PhysicsStep(); }
        void FixedUpdate() { PhysicsStep(); }
        void LateUpdate() { foreach (var a in pool) if (a.down != null) a.down.LateStep(); }

        static Vector3 Flat(Vector3 p) { p.y = 0f; return p; }

        // ---------------------------------------------------------------- the people

        readonly List<End> byDistance = new List<End>();

        void Staff(Vector3 eye)
        {
            byDistance.Clear();
            foreach (var e in ends) if (e.root != null) byDistance.Add(e);
            byDistance.Sort((a, b) => (Flat(a.st) - Flat(eye)).sqrMagnitude.CompareTo((Flat(b.st) - Flat(eye)).sqrMagnitude));
            int budget = PeopleCap;
            foreach (var e in byDistance)
            {
                bool want = Vector3.Distance(Flat(e.st), Flat(eye)) < PeopleRange + 20f && budget > 0;
                if (!want) { foreach (var a in e.people) Park(a); e.people.Clear(); continue; }
                if (e.people.Count == 0) Populate(e, budget);
                budget -= e.people.Count;
            }
        }

        Agent Take(End e, Job job, PersonLook? as_ = null)
        {
            Agent a = null;
            foreach (var q in pool) if (q.end == null) { a = q; break; }
            var look = as_ ?? NextLook();
            if (a == null)
            {
                var p = Person.Make(look, transform, "LiftPerson");
                if (p == null) return null;
                a = new Agent { p = p };
                var h = new GameObject("Hit");
                h.transform.SetParent(p.transform, false);
                a.hit = h.AddComponent<CapsuleCollider>();
                a.hit.radius = 0.3f; a.hit.height = 1.75f; a.hit.center = new Vector3(0f, 0.88f, 0f);
                var ob = h.AddComponent<Obstacle>(); ob.label = "Skier"; ob.person = this;   // (run into: knocked over, rides2)
                pool.Add(a);
            }
            else a.p.Dress(look);
            a.end = e; a.job = job; a.t = 0f; a.until = 0f; a.partner = null; a.shown = true; a.speed = 0f; a.handed = false; a.intoCamp = false;
            a.p.gameObject.SetActive(true);
            e.people.Add(a);
            return a;
        }

        void Park(Agent a)
        {
            if (a.down != null) Up(a);
            if (a.job == Job.Camp && a.p != null && CampLife.Current != null) CampLife.Current.Release(a.p);
            a.intoCamp = false; a.handed = false;
            a.end = null; a.shown = false; a.partner = null;
            if (a.p != null) { a.p.SetLod(2); a.p.gameObject.SetActive(false); }
            if (a.hit != null) a.hit.enabled = false;
        }

        PersonLook NextLook() => PersonLook.Random(70000 + spawned++ * 37 + (int)(Slope.Seed % 991), 0.5f);

        void Populate(End e, int budget)
        {
            if (!e.top)
            {
                int q = Mathf.Min(e.queue.Count, Mathf.Max(0, budget - 5));
                for (int i = 0; i < q; i++) { var a = Take(e, Job.Queue); if (a == null) return; a.slot = i; a.pos = a.goal = e.queue[i]; a.yaw = Yaw(e.queue[Mathf.Max(0, i - 1)] - e.queue[i], -e.dir); }
                if (e.hutDoor != Vector3.zero) { var lf = Take(e, Job.Liftie); if (lf != null) { lf.pos = lf.goal = e.hutDoor; lf.yaw = Yaw(e.load - e.hutDoor, e.side); } }
                if (e.chatA != Vector3.zero)
                {
                    var c1 = Take(e, Job.Chat); var c2 = Take(e, Job.Chat);
                    if (c1 != null && c2 != null) { c1.pos = e.chatA; c2.pos = e.chatB; c1.partner = c2; c2.partner = c1; c1.yaw = Yaw(c2.pos - c1.pos, e.dir); c2.yaw = Yaw(c1.pos - c2.pos, e.dir); }
                }
                if (e.clickIn != Vector3.zero) { var ck = Take(e, Job.ClickIn); if (ck != null) { ck.pos = e.clickIn; ck.yaw = Yaw(-e.dir, e.dir); ck.until = 6f; } }
            }
            else
            {
                Agent g = null, pt = null;
                if (e.goggles != Vector3.zero) { g = Take(e, Job.Goggles); if (g != null) { g.pos = e.goggles; g.yaw = Yaw(e.dir + e.side, e.dir); } }
                if (e.point != Vector3.zero) { pt = Take(e, Job.Point); if (pt != null) { pt.pos = e.point; pt.yaw = Yaw(e.dir + e.side * 0.6f, e.dir); } }
                if (e.sit != Vector3.zero) { var s = Take(e, Job.Sit); if (s != null) { s.pos = e.sit; s.yaw = Yaw(e.side + e.dir * 0.5f, e.dir); } }
                if (pt != null && g != null) { pt.partner = g; }
            }
            e.lastTick = clock;
        }

        static float Yaw(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 1e-4f) v = fallback;
            return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        }

        /// <summary>The time between chairs at a station, and the group each chair takes (pure: by the chair's number).</summary>
        static float Interval(LiftLine.Lift l) => l.spacing / LiftLine.Speed;
        static int Group(LiftLine.Lift l, int chair) { float h = Slope.Hash01(l.id, chair, 517); return h < 0.25f ? 0 : h < 0.45f ? 1 : h < 0.65f ? 2 : h < 0.82f ? 3 : 4; }

        void Move(Agent a, float dt)
        {
            var e = a.end;
            if (e == null) return;
            a.t += dt;
            switch (a.job)
            {
                case Job.Queue:
                    // (past the maze's last place: not here yet, coming down to it)
                    a.shown = a.slot < e.queue.Count;
                    if (!a.shown) break;
                    a.goal = e.queue[Mathf.Clamp(a.slot, 0, e.queue.Count - 1)];
                    Walk(a, a.goal, 1.1f, dt);
                    break;
                case Job.Load:
                {
                    Walk(a, e.load, 1.3f, dt);
                    // up the drift onto the deck's end
                    var flat = e.load - a.pos; flat.y = 0f;
                    a.pos.y = Mathf.Lerp(a.pos.y, e.st.y, Mathf.Clamp01(1f - flat.magnitude / 2.5f));
                    if (a.t > 2.4f)
                    {
                        // aboard (LiftRiders has them now); someone new joins the back of the queue
                        int last = -1;
                        foreach (var o in e.people) if (o != a && o.job == Job.Queue) last = Mathf.Max(last, o.slot);
                        a.job = Job.Queue; a.slot = last + 1; a.t = 0f;
                        a.pos = e.queue[Mathf.Clamp(a.slot, 0, e.queue.Count - 1)];
                        a.p.Dress(NextLook());
                    }
                    else if (a.t > 1.6f) a.shown = false;
                    break;
                }
                case Job.ClickIn:
                    if (a.t > a.until) { a.t = 0f; a.until = 5f + 3f * Slope.Hash01(a.slot, (int)clock, 7); }
                    break;
                case Job.Unload:
                    if (a.handed) { Glide(a, dt); break; }
                    Walk(a, a.goal, 1.6f, dt);
                    if ((a.pos - a.goal).sqrMagnitude < 0.04f) { a.job = Job.Strap; a.t = 0f; a.until = a.p.Look.gear == PersonGear.Skis ? 3.5f : 7.5f; }
                    break;
                case Job.Strap:
                    if (a.t > a.until) { a.job = Job.Push; a.t = 0f; a.speed = 1.5f; a.slot = 0; Pushed++; }
                    break;
                case Job.Doff:
                    // at the foot of the way in: gear off (a boarder's straps, a skier's bindings), then the camp walks
                    // them in, gear under an arm or on a shoulder, to the lodge's door and inside (CampLife)
                    if (a.t > a.until) ToCamp(a);
                    break;
                case Job.Camp:
                    // (the camp has them; given up if it lets go of them, or anything goes wrong)
                    if (a.until < 0f || CampLife.Current == null || (a.t > 90f && Hidden(a))) { a.p.gameObject.SetActive(false); e.people.Remove(a); Park(a); }
                    break;
                case Job.Push:
                {
                    // off down the run: across onto it and down the fall line, gathering speed, out of sight. (At the
                    // Summit top the run is the camp: they go round behind the station and away down the shoulder on its
                    // far side instead, beside the line: camplayout)
                    var to = (e.side * 0.8f + Vector3.forward * 0.6f).normalized;
                    var way = a.intoCamp && e.rideWay.Count > 1 ? e.rideWay : e.pushWay;
                    if (way.Count == 0 && a.t > 1f) { to = Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward; to = Vector3.RotateTowards(to, Vector3.forward, dt * 0.25f, 0f); }
                    if (way.Count > 0)
                    {
                        int w = Mathf.Min(a.slot, way.Count - 1);
                        to = way[w] - a.pos;
                        if (new Vector2(to.x, to.z).magnitude < 2.5f && a.slot < way.Count - 1) a.slot++;
                        // (turning as a rider does, not on the spot)
                        var was = Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward;
                        to.y = 0f; to.Normalize();
                        to = Vector3.RotateTowards(was, to, dt * 2.2f, 0f);
                    }
                    to.y = 0f; to.Normalize();
                    // (into the camp: easing to a stop at the way's foot; off the far side and down the run, gathering speed)
                    bool last = way.Count > 0 && a.slot >= way.Count - 1;
                    float top = a.intoCamp ? (last ? 1.2f : 5f) : 9f;
                    a.speed = Mathf.MoveTowards(a.speed, top, dt * (a.speed > top ? 3f : 2.2f));
                    a.pos = Snow(a.pos + to * a.speed * dt);
                    a.yaw = Yaw(to, to);
                    if (a.intoCamp)
                    {
                        // at the way's foot: stop, gear off, and in
                        if (last && (way[way.Count - 1] - a.pos).sqrMagnitude < 2.5f) { a.job = Job.Doff; a.t = 0f; a.until = a.p.Look.gear == PersonGear.Skis ? 1.6f : 2.4f; a.speed = 0f; }
                        else if (a.t > 60f && Hidden(a)) { e.people.Remove(a); Park(a); }
                        break;
                    }
                    // gone only once nobody can see them: past where people are drawn, out of the view, or behind the
                    // mountain (never on a timer; a very long ride down the run ends anyway, and then too only unseen)
                    if (a.t > 1.5f && Hidden(a)) { e.people.Remove(a); Park(a); }
                    break;
                }
                case Job.Walk:
                {
                    // the Summit lift: along the path into the camp, the board under an arm
                    int end = e.walkEnd > 0 ? Mathf.Min(e.walkEnd, e.toCamp.Count - 1) : e.toCamp.Count - 1;   // (to the lodge's steps, and in)
                    int i = Mathf.Clamp(a.slot, 0, end);
                    Walk(a, e.toCamp[i], 1.3f, dt);
                    if ((a.pos - e.toCamp[i]).sqrMagnitude < 0.25f) { a.slot++; if (a.slot > end) { e.people.Remove(a); Park(a); } }
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- off the chairs (lifttop)

        /// <summary>LiftRiders: someone getting off a chair at a top (queued for that end's next step).</summary>
        void OffChair(LiftLine.Lift l, PersonLook look, int seat, Vector3 at, Vector3 travel)
        {
            foreach (var e in ends)
                if (e.top && e.lift == l && e.root != null && e.people.Count > 0) { if (e.arriving.Count < 8) e.arriving.Add((look, seat, at, travel)); return; }
        }

        /// <summary>The most at a top at once off the chairs (gliding, strapping in, riding off); past it only those in sight are taken.</summary>
        public const int TopBusy = 9;

        /// <summary>
        /// The same person who sat on the chair stands up off it at the unload, gear on, and glides off the deck's
        /// uphill end to a place to strap in (a skier just sorts their poles). At the Summit's bench one in three (no
        /// more than three on their way in at once) then ride into the camp; the rest go off the far side.
        /// </summary>
        void Handover(End e, PersonLook look, int seat, Vector3 at, Vector3 travel)
        {
            if (e.strap.Count == 0) return;
            int busy = 0, inCamp = 0;
            foreach (var o in e.people)
            {
                if (o.job == Job.Unload || o.job == Job.Strap || o.job == Job.Push || o.job == Job.Walk || o.job == Job.Doff || o.job == Job.Camp) busy++;
                if (o.intoCamp) inCamp++;
            }
            var stand = new Vector3(at.x, e.st.y, at.z);
            if (busy >= TopBusy && !InSight(stand + Vector3.up * 1.2f)) return;   // (over the cap, and nobody would see them go: they don't)
            var a = Take(e, Job.Unload, look);
            if (a == null) return;
            a.handed = true;
            a.intoCamp = e.rideWay.Count > 1 && inCamp < LiftTop.IntoCampCap && Slope.Hash01(e.lift.id * 31 + seat, Mathf.FloorToInt(clock * 7f), 43) < LiftTop.IntoCampShare;
            a.pos = stand;
            var f = travel; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = -e.dir;
            a.yaw = Yaw(f, -e.dir);
            a.speed = 2.6f;
            a.slot = 0; a.seatSlot = seat;
            // where to: into the camp, straight on along the way in; otherwise a place to strap in (each seat its own)
            a.goal = a.intoCamp ? (e.toCamp.Count > 0 ? e.toCamp[0] : e.rideWay[0]) : e.strap[(seat + Mathf.FloorToInt(clock)) % e.strap.Count];
        }

        /// <summary>Off the deck with the chair's way, slowing, then round to the goal and stop.</summary>
        void Glide(Agent a, float dt)
        {
            var e = a.end;
            var d = a.goal - a.pos; d.y = 0f;
            float m = d.magnitude;
            var was = Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward;
            // (straight off the chair's way for the first metres, then turning toward where they're going)
            var to = a.t < 0.7f ? was : Vector3.RotateTowards(was, m > 1e-3f ? d / m : was, dt * 2.4f, 0f);
            float want = Mathf.Clamp(m * 0.8f, 0.4f, 3f);
            a.speed = Mathf.MoveTowards(a.speed, want, dt * 1.6f);
            var next = a.pos + to * a.speed * dt;
            // (on the deck while over it, the snow once off it)
            var local = next - e.st;
            float v = Vector3.Dot(local, e.dir), u = Vector3.Dot(local, e.side);
            bool deck = Mathf.Abs(v) < LiftLine.StationHalfLen && Mathf.Abs(u) < LiftLine.StationHalfWid;
            next.y = deck ? Mathf.Max(e.st.y, Slope.DrawnHeight(next.x, next.z)) : Slope.DrawnHeight(next.x, next.z);
            a.pos = next;
            a.yaw = Yaw(to, to);
            if (a.intoCamp && m < 2.5f) { a.job = Job.Push; a.t = 0f; a.slot = 0; Pushed++; return; }   // (already strapped in: on into the camp)
            if (m < 0.6f || a.t > 12f)
            {
                a.job = Job.Strap; a.t = 0f; a.speed = 0f;
                // a boarder sits to strap the back foot in; a skier sorts their poles and goggles
                a.until = a.p.Look.gear == PersonGear.Skis ? 1.8f : 5.5f;
            }
        }

        /// <summary>Gear off at the foot of the way in: the camp walks them to the lodge's door and in.</summary>
        void ToCamp(Agent a)
        {
            var e = a.end;
            var life = CampLife.Current;
            var door = life != null ? life.P("door") : null;
            if (life == null || door == null) { e.people.Remove(a); Park(a); return; }
            a.job = Job.Camp; a.t = 0f;
            if (a.hit != null) a.hit.enabled = false;   // (the camp's own pushing keeps the hero off them)
            var ag = life.Add(a.p);
            ag.gear = a.p.Look.gear != PersonGear.None;
            a.until = 0f;
            // through the door: inside, out of sight (let go of on the next step, not inside the camp's own)
            life.WalkTo(a.p, door.at, () => { if (a.job == Job.Camp) a.until = -1f; });
        }

        /// <summary>The foot a boarder's board stays on (as on the chair: LiftRiders).</summary>
        static int FrontFoot(PersonLook look) => Slope.Hash01((int)(look.height * 1000f), (int)(look.build * 1000f), 5) < 0.7f ? 0 : 1;

        /// <summary>
        /// Skating off the chair: the board on the front foot, the back foot pushing behind it, a stride every 0.8 s or
        /// so while they're moving (it planted beside the board's tail, swept back, lifted and brought forward again).
        /// </summary>
        void Skate(Agent a)
        {
            var p = a.p; var h = p.Rig;
            if (h == null || p.Lod != 0 && p.Lod != 1) return;
            int back = 1 - FrontFoot(p.Look);
            var rot = Quaternion.Euler(0f, a.yaw, 0f);
            float s = p.Scale, side = back == 0 ? -1f : 1f;
            bool moving = a.speed > 0.5f;
            float ph = moving ? Mathf.Repeat(a.t * 1.25f, 1f) : 0.2f;
            // on the snow for the push (0..0.6), then up and forward
            float z = ph < 0.6f ? Mathf.Lerp(0.05f, -0.45f, ph / 0.6f) : Mathf.Lerp(-0.45f, 0.05f, (ph - 0.6f) / 0.4f);
            float y = ph < 0.6f ? 0f : 0.1f * Mathf.Sin((ph - 0.6f) / 0.4f * Mathf.PI);
            var ankle = a.pos + rot * new Vector3(side * 0.2f * s, (0.08f + y) * s, z * s);
            ankle.y = Mathf.Max(ankle.y, Slope.DrawnHeight(ankle.x, ankle.z) + 0.08f * s);
            h.LegTo(back, ankle, h.Hips.position + rot * new Vector3(side * 0.15f, -0.3f, 0.5f));
        }

        // ---------------------------------------------------------------- who can see whom

        readonly Plane[] frustum = new Plane[6];
        bool haveFrustum;
        Vector3 sightFrom;
        float sightRange;

        void Sight(Vector3 eye)
        {
            var cam = Camera.main;
            haveFrustum = cam != null && cam.isActiveAndEnabled;
            if (haveFrustum) GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            sightFrom = haveFrustum ? cam.transform.position : eye;
            sightRange = PeopleRange + 8f;
        }

        /// <summary>Could the camera see a point: near enough to be drawn, in the view, and not behind the mountain.</summary>
        bool InSight(Vector3 p)
        {
            var d = p - sightFrom;
            float dist = d.magnitude;
            if (dist > sightRange) return false;
            if (RenderSettings.fog && RenderSettings.fogMode == FogMode.ExponentialSquared)
            {
                float f = RenderSettings.fogDensity * dist;
                if (Mathf.Exp(-f * f) < 0.04f) return false;   // (lost in the fog: snow, cloud)
            }
            if (haveFrustum && !GeometryUtility.TestPlanesAABB(frustum, new Bounds(p, new Vector3(1.4f, 2.2f, 1.4f)))) return false;
            // behind the snow: along the line from the eye, any point under the ground
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / 4f), 2, 40);
            for (int i = 1; i < n; i++)
            {
                var q = sightFrom + d * (i / (float)n);
                if (Slope.DrawnHeight(q.x, q.z) > q.y + 0.25f) return false;
            }
            return true;
        }

        /// <summary>Nobody can see this person now (head and feet both out of sight).</summary>
        bool Hidden(Agent a) => !InSight(a.pos + Vector3.up * 1.6f) && !InSight(a.pos + Vector3.up * 0.3f);

        /// <summary>A chair goes by the bottom: its group (the front of the queue) steps up to load; everyone behind moves up.</summary>
        void Chair(End e, int group)
        {
            if (group <= 0) return;
            int took = 0;
            foreach (var a in e.people)
                if (a.job == Job.Queue && a.slot < group) { a.job = Job.Load; a.t = 0f; a.group = group; took++; }
            if (took == 0) return;
            Loaded++;
            foreach (var a in e.people) if (a.job == Job.Queue) a.slot -= took;
        }

        /// <summary>A chair reaches the top: one or two get off, glide clear and find a place to strap in.</summary>
        void Arrive(End e, int group)
        {
            if (group <= 0 || e.strap.Count == 0) return;
            int busy = 0;
            foreach (var a in e.people) if (a.job == Job.Unload || a.job == Job.Strap || a.job == Job.Push || a.job == Job.Walk) busy++;
            for (int i = 0; i < Mathf.Min(group, 2) && busy < 5; i++, busy++)
            {
                bool bench = e.rideWay.Count > 1;
                var a = Take(e, !bench && e.lift.summit && Slope.Hash01(e.lift.id, Mathf.FloorToInt(clock), 41) < 0.4f ? Job.Walk : Job.Unload);
                if (a == null) return;
                // (the bench: a share ride into the camp, so many at a time; the rest off the far side)
                if (bench && Slope.Hash01(e.lift.id * 31 + i, Mathf.FloorToInt(clock * 7f), 43) < LiftTop.IntoCampShare)
                {
                    int inCamp = 0; foreach (var o in e.people) if (o.intoCamp && o != a) inCamp++;
                    a.intoCamp = inCamp < LiftTop.IntoCampCap;
                }
                a.pos = e.load + e.dir * 0.3f * i;
                a.yaw = Yaw(-e.dir, e.dir);
                a.slot = 0;
                a.goal = e.strap[(Mathf.FloorToInt(clock) + i) % e.strap.Count];
            }
        }

        void Walk(Agent a, Vector3 goal, float speed, float dt)
        {
            var d = goal - a.pos; d.y = 0f;
            float m = d.magnitude;
            if (m < 0.02f) { a.speed = 0f; return; }
            float step = Mathf.Min(m, speed * dt);
            a.pos = Snow(a.pos + d / m * step);
            a.speed = step / Mathf.Max(dt, 1e-4f);
            a.yaw = Mathf.MoveTowardsAngle(a.yaw, Yaw(d, d), 360f * dt);
            a.p.Advance(step);
        }

        void Pose(Agent a, float dt)
        {
            var p = a.p;
            var e = a.end;
            p.lookWeight = 0f; p.turnChest = 0f; p.pointW = 0f; p.handW[0] = p.handW[1] = 0f; p.phoneUp = 0f;
            p.crouch = 0f; p.skisOn = false; p.polesOn = false; p.skisOnShoulder = false; p.boardInHand = -1; p.held = Person.Held.None; p.boardFoot = -1;
            p.sitW = 0f; p.feetDown = false;
            var rot = Quaternion.Euler(0f, a.yaw, 0f);
            bool skis = p.Look.gear == PersonGear.Skis;
            float t = clock + a.slot * 1.7f + (a.p.GetInstanceID() & 15);
            float moving = Mathf.Clamp01(a.speed / 1.2f);
            if (a.hit != null) { a.hit.height = 1.75f; a.hit.center = new Vector3(0f, 0.88f, 0f); }
            switch (a.job)
            {
                case Job.Queue:
                case Job.Load:
                    // in the maze with their gear on: skis and poles, or the board on the front foot
                    if (skis)
                    {
                        p.mode = Person.Mode.Ski; p.pace = moving; p.skisOn = true; p.polesOn = true; p.polesInOneHand = false;
                        p.polesDir = (Vector3.down + rot * Vector3.back * 0.2f).normalized;
                        p.transform.SetPositionAndRotation(a.pos, rot);
                    }
                    else
                    {
                        p.mode = Person.Mode.Ride; p.pace = moving; p.held = Person.Held.Underfoot;
                        p.RideFrame(a.pos, rot * Vector3.forward, Vector3.up);
                    }
                    // a look about, at the chairs, at a friend
                    p.lookAt = Mathf.Repeat(t, 9f) < 4f ? e.load + Vector3.up * 3f : a.pos + rot * new Vector3(Mathf.Sin(t * 0.3f) * 6f, 1.4f, 8f); p.lookWeight = 0.5f;
                    break;
                case Job.Liftie:
                    p.mode = Person.Mode.Stand;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    p.lookAt = e.load + Vector3.up * 1.2f; p.lookWeight = 0.85f; p.turnChest = 0.3f;
                    // waving the next chair on, as each comes round
                    float w = Mathf.Repeat(clock, Interval(e.lift)) / Interval(e.lift);
                    if (w < 0.35f) { p.pointAt = e.load + Vector3.up * 2.5f - e.dir * 3f; p.pointW = Mathf.Sin(w / 0.35f * Mathf.PI); }
                    break;
                case Job.Chat:
                    p.mode = Person.Mode.Stand;
                    if (a.partner != null) { var to = a.partner.pos - a.pos; to.y = 0f; if (to.sqrMagnitude > 0.01f) a.yaw = Mathf.MoveTowardsAngle(a.yaw, Yaw(to, to), 120f * dt); }
                    p.transform.SetPositionAndRotation(a.pos, Quaternion.Euler(0f, a.yaw, 0f));
                    if (a.partner != null) { p.lookAt = a.partner.p.HeadPosition; p.lookWeight = 0.85f; }
                    if (Mathf.Repeat(t, 7f) < 3.5f)
                    {
                        p.handAt[1] = a.pos + rot * new Vector3(0.22f, 1.08f * p.Scale + 0.06f * Mathf.Sin(t * 3.3f), 0.3f);
                        p.handW[1] = 0.8f;
                    }
                    if (skis) p.skisOnShoulder = true; else { p.held = Person.Held.Placed; p.boardInHand = 1; }
                    break;
                case Job.ClickIn:
                {
                    // stepping into the bindings: down to them, a stamp, up again, and again a little later
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    float k = Mathf.Clamp01(a.t / 3.2f);
                    if (skis)
                    {
                        p.mode = Person.Mode.Ski; p.pace = 0f; p.skisOn = true; p.polesOn = true; p.polesInOneHand = false;
                        p.polesDir = (Vector3.down + rot * Vector3.back * 0.25f + rot * Vector3.right * 0.12f).normalized;
                        p.crouch = 0.35f * Mathf.Sin(k * Mathf.PI);
                    }
                    else
                    {
                        p.mode = Person.Mode.Ride; p.pace = 0f; p.held = Person.Held.Underfoot;
                        p.RideFrame(a.pos, rot * Vector3.forward, Vector3.up);
                        float down = Mathf.Sin(k * Mathf.PI);
                        p.crouch = 0.7f * down;
                        var tail = a.pos - rot * Vector3.forward * 0.25f + Vector3.up * 0.1f;
                        p.handAt[1] = tail; p.handW[1] = down;
                    }
                    p.lookAt = a.pos + rot * new Vector3(0f, 0f, 1f); p.lookWeight = 0.7f * Mathf.Sin(k * Mathf.PI);
                    break;
                }
                case Job.Unload when a.handed:
                {
                    // (lifttop2) off the chair: a skier pushes off on the poles, planted ahead and drawn back past the
                    // hips, a few times; a boarder skates, the board on the front foot, the back foot out of its binding
                    // pushing (Skate, after the pose), until they stop to strap in
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    if (skis)
                    {
                        p.mode = Person.Mode.Ski; p.pace = Mathf.Clamp01(a.speed / 3f); p.skisOn = true; p.polesOn = true; p.polesInOneHand = false;
                        float ph = Mathf.Repeat(a.t * 1.25f, 1f), push = ph < 0.55f ? ph / 0.55f : 1f - (ph - 0.55f) / 0.45f;
                        if (a.t < 3.4f && a.speed > 0.6f)
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                float sd = i == 0 ? -1f : 1f;
                                p.handAt[i] = a.pos + rot * new Vector3(sd * 0.25f, (1.0f - 0.12f * push) * p.Scale, (0.38f - 0.6f * push) * p.Scale);
                                p.handW[i] = 1f;
                            }
                            p.polesDir = (Vector3.down + rot * Vector3.back * (0.05f + 0.95f * push)).normalized;
                            p.crouch = 0.18f * push;
                        }
                        else p.polesDir = (Vector3.down + rot * Vector3.back * 0.3f).normalized;
                    }
                    else
                    {
                        p.mode = Person.Mode.Stand; p.held = Person.Held.Placed; p.boardFoot = FrontFoot(p.Look); p.boardPitch = 0f; p.boardTurn = 0f;
                    }
                    break;
                }
                case Job.Push:
                {
                    // gear on: gliding off the chair, or riding away
                    var v = Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward;
                    if (skis)
                    {
                        p.mode = Person.Mode.Ski; p.pace = Mathf.Clamp01(a.speed / 3f); p.skisOn = true; p.polesOn = true; p.polesInOneHand = false;
                        p.polesDir = (Vector3.down + rot * Vector3.back * (a.job == Job.Push ? 0.8f : 0.3f)).normalized;
                        p.transform.SetPositionAndRotation(a.pos, rot);
                    }
                    else
                    {
                        p.mode = Person.Mode.Ride; p.pace = Mathf.Clamp01(a.speed / 3f); p.held = Person.Held.Underfoot;
                        p.RideFrame(a.pos, v, Vector3.up);
                    }
                    break;
                }
                case Job.Doff:
                {
                    // bending to the bindings, then up with the gear
                    float k = Mathf.Clamp01(a.t / Mathf.Max(0.1f, a.until));
                    float down = Mathf.Sin(k * Mathf.PI);
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    if (skis) { p.mode = Person.Mode.Ski; p.pace = 0f; p.skisOn = k < 0.6f; p.skisOnShoulder = k >= 0.6f; p.polesOn = k < 0.6f; p.polesInOneHand = false; p.crouch = 0.4f * down; }
                    else if (k < 0.6f) { p.mode = Person.Mode.Ride; p.pace = 0f; p.held = Person.Held.Underfoot; p.RideFrame(a.pos, rot * Vector3.forward, Vector3.up); p.crouch = 0.7f * down; }
                    else { p.mode = Person.Mode.Stand; p.held = Person.Held.Placed; p.boardInHand = 1; p.crouch = 0.5f * down; }
                    break;
                }
                case Job.Unload:
                case Job.Walk:
                    p.mode = Person.Mode.Walk; p.pace = moving; p.run = 0f;
                    if (skis) p.skisOnShoulder = true; else { p.held = Person.Held.Placed; p.boardInHand = 1; }
                    p.polesDir = Vector3.down + rot * Vector3.forward * 0.15f;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    break;
                case Job.Strap:
                {
                    float k = Mathf.Clamp01(a.t / a.until);
                    if (skis)
                    {
                        p.mode = Person.Mode.Ski; p.pace = 0f; p.skisOn = true; p.polesOn = true; p.polesInOneHand = false;
                        p.polesDir = (Vector3.down + rot * Vector3.back * 0.25f).normalized;
                        p.crouch = 0.35f * Mathf.Sin(k * Mathf.PI);
                        p.transform.SetPositionAndRotation(a.pos, rot);
                    }
                    else
                    {
                        // sitting in the snow to do up the bindings, then up onto the board
                        bool sitting = k < 0.75f;
                        if (sitting)
                        {
                            p.mode = Person.Mode.Sit;
                            p.sitW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 6f));
                            p.feetDown = true; p.footY = a.pos.y; p.feetForward = 0.35f; p.lean = 14f; p.hipsForward = 0f; p.kneesApart = 0.45f;
                            p.transform.SetPositionAndRotation(Vector3.Lerp(a.pos, a.pos + Vector3.up * 0.12f, p.sitW), rot);
                            var feet = a.pos + rot * new Vector3(0f, 0.15f, 0.55f);
                            p.handAt[0] = feet + rot * Vector3.left * 0.12f; p.handAt[1] = feet + rot * Vector3.right * 0.12f;
                            p.handW[0] = p.handW[1] = 0.8f * p.sitW;
                            p.lookAt = feet; p.lookWeight = 0.8f;
                            if (a.hit != null) { a.hit.height = 1.0f; a.hit.center = new Vector3(0f, 0.5f, 0f); }
                        }
                        else
                        {
                            p.mode = Person.Mode.Ride; p.pace = 0f; p.held = Person.Held.Underfoot;
                            p.RideFrame(a.pos, rot * Vector3.forward, Vector3.up);
                        }
                    }
                    break;
                }
                case Job.Goggles:
                {
                    p.mode = Person.Mode.Stand;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    // both hands up to the goggles now and then, pulling them down over the eyes
                    float k = Mathf.Repeat(t, 8f);
                    float up = k < 2f ? Mathf.Sin(k / 2f * Mathf.PI) : 0f;
                    var head = p.HeadPosition + rot * Vector3.forward * 0.12f;
                    p.handAt[0] = head + rot * Vector3.left * 0.09f + Vector3.up * (0.06f - 0.05f * k / 2f); p.handAt[1] = head + rot * Vector3.right * 0.09f + Vector3.up * (0.06f - 0.05f * k / 2f);
                    p.handW[0] = p.handW[1] = up;
                    p.lookAt = a.pos + rot * new Vector3(0f, -2f, 20f); p.lookWeight = 0.5f;
                    if (skis) { p.mode = Person.Mode.Ski; p.skisOn = true; p.polesOn = true; p.polesInOneHand = true; }
                    else { p.mode = Person.Mode.Ride; p.held = Person.Held.Underfoot; p.RideFrame(a.pos, rot * Vector3.forward, Vector3.up); }
                    break;
                }
                case Job.Point:
                {
                    p.mode = Person.Mode.Stand;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    // pointing out a line down the run to a friend
                    var line = a.pos + e.side * 25f + Vector3.forward * 60f + Vector3.down * 30f;
                    float k = Mathf.Repeat(t, 10f);
                    p.lookAt = k < 6f ? line : (a.partner != null ? a.partner.p.HeadPosition : line); p.lookWeight = 0.8f; p.turnChest = 0.3f;
                    if (k < 4f) { p.pointAt = line; p.pointW = Mathf.Sin(k / 4f * Mathf.PI); }
                    if (skis) p.skisOnShoulder = true; else { p.held = Person.Held.Placed; p.boardInHand = 1; }
                    break;
                }
                case Job.Sit:
                {
                    // sitting in the snow, the board beside them, looking down the run
                    p.mode = Person.Mode.Sit;
                    p.sitW = 1f; p.feetDown = true; p.footY = a.pos.y; p.feetForward = 0.4f; p.lean = 12f; p.hipsForward = 0f; p.kneesApart = 0.5f;
                    p.transform.SetPositionAndRotation(a.pos + Vector3.up * 0.12f, rot);
                    p.lookAt = a.pos + rot * new Vector3(Mathf.Sin(t * 0.11f) * 15f, -6f, 30f); p.lookWeight = 0.6f;
                    var knees = a.pos + rot * new Vector3(0f, 0.45f, 0.3f);
                    p.handAt[0] = knees + rot * Vector3.left * 0.15f; p.handAt[1] = knees + rot * Vector3.right * 0.15f; p.handW[0] = p.handW[1] = 0.7f;
                    if (a.hit != null) { a.hit.height = 1.0f; a.hit.center = new Vector3(0f, 0.5f, 0f); }
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- for the tests

        /// <summary>Everything this has drawn and made solid, and the people now shown (LiftEndsTest).</summary>
        public IEnumerable<Agent> Shown() { foreach (var e in ends) foreach (var a in e.people) if (a.shown && a.p.gameObject.activeSelf) yield return a; }
    }
}
