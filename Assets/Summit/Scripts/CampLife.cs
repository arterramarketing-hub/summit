using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The people living in the summit camp (Person, the hero's own body): a handful about the place, never a
    /// crowd. Someone at the lookout taking pictures of the view, someone else just looking; two friends talking;
    /// someone on the bench by the drop; someone waxing a board on the stand beside the lodge; someone walking from
    /// the lodge to the cliff rail and back in; and now and then someone coming out of the lodge with their board or
    /// skis, strapping in beside the gate and riding off down the start, out of sight. When the hero runs past they
    /// look; when the edge gives way under the hero, everyone turns, and the nearest go to the rail to look over.
    ///
    /// They walk on a grid laid over the camp's own solid things (Camp.PushOutOfThings: the lodge, the fire pit and
    /// its benches, posts, trees, the rail), inside the camp, never within reach of the cliff's edge, round the fire
    /// pit's seats (kept for the fire's own guests) and not standing in the gate's DROP IN circle; the walk cycle runs
    /// with the ground covered, so the feet don't slide. They step round each other and the hero, and the hero can't
    /// walk through them (Camp.PushWalker). Footsteps crunch nearby; friends talking murmur. Nobody says anything on
    /// screen.
    ///
    /// Everywhere they go is a named point (Points), so the lodge and the camp's other builders can move or add them:
    ///   door, lookout.1, lookout.2, rail.1, rail.2, chat.1..3, bench.1, bench.2, wax, gate.L, gate.R, snowman.
    ///
    /// For named characters made elsewhere (Valentino and Brianna, Alyssa), CampLife.Current offers:
    ///   SetPoint(name, at, yaw)       move or add a named point (the lodge's door, say)
    ///   Keep(name, centre, radius)    keep the camp's own people out of somewhere (the fire's seats, a corner)
    ///   Add(person, home)             a person of yours, walked about by the camp like its own (home: a point they return to)
    ///   Hold(person)                  a person you pose yourself: the camp's people step round them, the hero can't walk through
    ///   Release(person)               the camp lets go of them
    ///   WalkTo(person, at, arrived)   walk one of yours somewhere on the camp's grid, feet on the ground; arrived is called there
    /// </summary>
    public class CampLife : MonoBehaviour
    {
        public static bool On = true;
        public static CampLife Current { get; private set; }
        public const int Crowd = 8;                  // the camp's own people at once
        public const float FullRange = 14f, SimpleRange = 110f;
        public const int FullCap = 3;

        Camp camp;
        Transform props, crowd;
        static float Cx => Slope.Centre(Slope.PadLip);
        static Vector3 Ground(float dx, float z) { float x = Cx + dx; return new Vector3(x, Slope.DrawnHeight(x, z), z); }

        // ---------------------------------------------------------------- named points

        public class Point
        {
            public string name;
            public Vector3 at;
            public float yaw;                      // the way someone there faces (degrees)
            public Agent holder;                   // who is using it
        }
        readonly Dictionary<string, Point> points = new Dictionary<string, Point>();
        readonly HashSet<string> setByOthers = new HashSet<string>();
        public IEnumerable<Point> Points => points.Values;
        public Point P(string name) => points.TryGetValue(name, out var p) ? p : null;

        /// <summary>Move or add a named point (kept across camp rebuilds).</summary>
        public void SetPoint(string name, Vector3 at, float yaw)
        {
            setByOthers.Add(name);
            Put(name, at, yaw);
            gridDirty = true;
        }
        void Put(string name, Vector3 at, float yaw)
        {
            if (!points.TryGetValue(name, out var p)) points[name] = p = new Point { name = name };
            p.at = at; p.yaw = yaw;
        }

        // ---------------------------------------------------------------- places kept clear

        struct KeepOut { public string name; public Vector3 c; public float r; public bool walk; }
        readonly List<KeepOut> keep = new List<KeepOut>();

        /// <summary>Keep the camp's own people out of a circle (they walk round it).</summary>
        public void Keep(string name, Vector3 centre, float radius)
        {
            keep.RemoveAll(k => k.name == name);
            keep.Add(new KeepOut { name = name, c = centre, r = radius, walk = false });
            gridDirty = true;
        }

        // ---------------------------------------------------------------- the people

        public enum Doing { Idle, Walk, Stand, Sit, Chat, Wax, Inside, Strap, Ride, Look, Held }
        public class Agent
        {
            public Person p;
            public bool own;                       // one of the camp's (re-dressed as they come and go)
            public bool held;                      // posed by someone else
            public string home;
            public Doing doing;
            public float t, until;                 // time in what they're doing, and how long it lasts
            public Vector3 pos; public float yaw, speed, want;
            public readonly List<Vector3> path = new List<Vector3>();
            public int pathAt;
            public System.Action arrived;
            public Point spot;                     // the point they're using
            public Agent partner;
            public int role;
            public float progress, progressT;      // for noticing they're stuck
            public float lookT; public Vector3 look;   // a glance at something (the hero going by)
            public float stepPh;
            public bool gear;                      // carrying their board or skis (droppers)
            public Vector3 rideVel; public float rideX;
            public int stuck;
            public float sitW;
            public float turn;                     // degrees the body turns off its way, to look at something (the fall)
            public bool goer;                      // hurrying to the rail after the fall
        }
        readonly List<Agent> agents = new List<Agent>();
        public IReadOnlyList<Agent> Agents => agents;
        int spawned;
        float clock;

        /// <summary>Hooked to its camp (Camp.Build adds it: in the editor's tools nothing gets an Awake).</summary>
        public void Setup(Camp c)
        {
            if (camp == c) return;
            Current = this;
            camp = c;
            camp.Built += OnBuilt;
            camp.Collapsed += OnCollapse;
            camp.PushWalker = PushHero;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        // ---------------------------------------------------------------- building

        void OnBuilt(Transform root)
        {
            if (!On) return;
            props = new GameObject("CampLife").transform;
            props.SetParent(root, false);
            DefaultPoints();
            BuildProps();
            gridDirty = true;
            if (crowd == null)
            {
                crowd = new GameObject("CampPeople").transform;
                crowd.SetParent(transform, false);
            }
            if (!PeopleKit.Load()) return;
            // the camp's own: made once, set going again on each new camp
            for (int i = 0; i < Crowd; i++)
            {
                Agent a = i < agents.Count && agents[i].own ? agents[i] : null;
                if (a == null)
                {
                    var p = Person.Make(Look(), crowd, "CampPerson");
                    if (p == null) return;
                    a = new Agent { p = p, own = true, role = i };
                    agents.Add(a);
                }
                Begin(a, true);
            }
            BuildGrid();
            foreach (var a in agents) { PeopleLod.Skinned(a.p.Look); Pose(a, 0f); a.p.Apply(0f); }   // (their simplified bodies now, with the camp)
        }

        PersonLook Look() => PersonLook.Random(9000 + spawned++ * 31 + (int)(Slope.Seed % 997), 0.5f);

        /// <summary>Where everything is, from the camp's layout (the lodge's door from its own frame).</summary>
        void DefaultPoints()
        {
            void D(string n, Vector3 at, float yaw) { if (!setByOthers.Contains(n)) Put(n, at, yaw); }
            float cy = camp.ChaletYaw;
            // (beside the foot of the steps, off Val and Breanna's way up them: CampCouple's ChaletSteps)
            var door = camp.ChaletPoint(2.4f, 0f, 12.2f); door.y = Slope.DrawnHeight(door.x, door.z);
            D("door", door, cy);
            // the lookout's deck at the cliff's edge (Camp.LookoutX, LookoutZ, facing out over the drop, +x): two places
            // at its front rail (on its boards, at the height Camp.Lookout sets them; the snow under its front is lower,
            // and standing over that was standing in the deck)
            float deck = camp.LookoutDeckY;
            D("lookout.1", new Vector3(Ground(Camp.LookoutX + 1.4f, 0f).x, deck, Camp.LookoutZ + 1.4f), 90f);
            D("lookout.2", new Vector3(Ground(Camp.LookoutX + 1.4f, 0f).x, deck, Camp.LookoutZ - 0.3f), 98f);
            // along the cliff's rail between the flags (either side of the deck), looking out over the edge
            D("rail.1", Ground(21.2f, -75.2f), 90f);
            D("rail.2", Ground(21.2f, -61.0f), 85f);
            // open snow for standing about
            D("chat.1", Ground(9.0f, -71.0f), 0f);   // (clear of the shovelled path to the lookout, and the couple's Stroll)
            // (campstairs: these two stood out on the bank's steep snow, 40 to 50 per cent; now at its top either side
            // of the stairs, on the level, looking down them to the gate)
            D("chat.2", Ground(-6.6f, -63.8f), 10f);
            D("chat.3", Ground(2.2f, -63.6f), -10f);
            // the bench by the drop, looking down the start (the points are where you stand to sit down, just in front)
            D("bench.1", Ground(-9.4f, -47.1f), 0f);
            D("bench.2", Ground(-8.6f, -47.1f), 0f);
            // the waxing stand in the start corral, on the right before the gate (camplayout: it stood by the lodge's steps)
            D("wax", Ground(7.2f, Slope.GateZ - 10.4f), 0f);
            // strapping in, either side of the gate, out of the DROP IN circle
            D("gate.L", Ground(-5.4f, Slope.GateZ - 4.2f), 0f);
            D("gate.R", Ground(5.4f, Slope.GateZ - 4.2f), 0f);
            // in front of the snowman (it looks out over the plaza), off the shovelled path
            {
                var sf = Quaternion.Euler(0f, Camp.SnowmanYaw, 0f) * Vector3.forward;
                var sp = Ground(Camp.SnowmanX + sf.x * 1.9f, Camp.SnowmanZ + sf.z * 1.9f);
                D("snowman", sp, Camp.SnowmanYaw - 180f);
            }
            // the fire pit's seats are for the fire's own guests; the DROP IN circle is for the hero. (Both laid again
            // each build: the camp sits on each mountain's own line, and a circle kept from the first one was metres off)
            keep.RemoveAll(k => k.name == "fire");
            keep.Add(new KeepOut { name = "fire", c = Ground(0f, -71f), r = 4.1f });
            keep.RemoveAll(k => k.name == "dropin");
            keep.Add(new KeepOut { name = "dropin", c = Ground(0f, Slope.GateZ - 2.6f), r = 4.5f, walk = true });
        }

        Material wood, logs, steel, standBoard, standGear;   // (made once: the camp is rebuilt for every mountain)

        /// <summary>The camp's people's own things: a log bench by the drop, a waxing stand beside the lodge.</summary>
        void BuildProps()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Material M(Color c, float sm, string tex = null, Vector2 tile = default)
            {
                var m = new Material(lit);
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", sm);
                if (tex != null) { var t = Resources.Load<Texture2D>(tex); if (t != null) { m.SetTexture("_BaseMap", t); if (tile != default) m.SetTextureScale("_BaseMap", tile); } }
                m.enableInstancing = true;
                return m;
            }
            if (wood == null) { wood = M(new Color(0.95f, 0.88f, 0.8f), 0.18f, "Camp/Wood"); logs = M(new Color(1f, 0.92f, 0.85f), 0.12f, "Camp/Logs", new Vector2(1f, 2f)); steel = M(new Color(0.12f, 0.12f, 0.13f), 0.5f); steel.SetFloat("_Metallic", 0.5f); }
            Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material m)
            {
                var go = GameObject.CreatePrimitive(type);
                DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(euler); go.transform.localScale = scale;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                return go.transform;
            }
            // the bench: a split log on two stumps, between bench.1 and bench.2, facing down the start
            var b1 = P("bench.1"); var b2 = P("bench.2");
            if (b1 != null && b2 != null)
            {
                var t = new GameObject("Bench").transform; t.SetParent(props, false);
                Vector3 mid = (b1.at + b2.at) * 0.5f - Quaternion.Euler(0f, b1.yaw, 0f) * Vector3.forward * BenchBack;
                mid.y = Slope.DrawnHeight(mid.x, mid.z);
                t.SetPositionAndRotation(mid, Quaternion.Euler(0f, b1.yaw, 0f));
                // the seat's top at knee height (0.45 m), level, sunk into the snow either end
                foreach (var sx in new[] { -0.75f, 0.75f }) Part(PrimitiveType.Cylinder, t, new Vector3(sx, 0.17f, 0f), new Vector3(0.36f, 0.25f, 0.36f), Vector3.zero, logs);
                Part(PrimitiveType.Cylinder, t, new Vector3(0f, 0.385f, 0f), new Vector3(0.36f, 1.05f, 0.36f), new Vector3(0f, 0f, 90f), logs);
                Part(PrimitiveType.Cube, t, new Vector3(0f, 0.445f, 0f), new Vector3(2.0f, 0.03f, 0.3f), Vector3.zero, wood);
                camp.AddBlock(mid, new Vector2(1.05f, 0.22f), b1.yaw);
                benchTop = mid.y + 0.46f;
            }
            // the waxing stand: two trestles and a board on them, base up
            var wx = P("wax");
            if (wx != null)
            {
                var t = new GameObject("WaxStand").transform; t.SetParent(props, false);
                // the stand in front of whoever waxes (they face it): 0.55 m ahead
                var f = Quaternion.Euler(0f, wx.yaw, 0f) * Vector3.forward;
                waxAt = wx.at + f * 0.55f;
                t.SetPositionAndRotation(waxAt, Quaternion.Euler(0f, wx.yaw, 0f));   // (its length across in front of them)
                foreach (var sx in new[] { -0.5f, 0.5f })
                {
                    foreach (var sz in new[] { -0.15f, 0.15f }) Part(PrimitiveType.Cube, t, new Vector3(sx, 0.37f, sz), new Vector3(0.05f, 0.76f, 0.05f), new Vector3(sz > 0f ? -10f : 10f, 0f, 0f), wood);
                    Part(PrimitiveType.Cube, t, new Vector3(sx, 0.74f, 0f), new Vector3(0.08f, 0.06f, 0.32f), Vector3.zero, wood);
                }
                if (PeopleKit.Load() && PeopleKit.BoardMesh != null)
                {
                    var g = new GameObject("WaxBoard", typeof(MeshFilter), typeof(MeshRenderer));
                    g.transform.SetParent(t, false);
                    // the deck turned base up, along the stand
                    var m = Matrix4x4.TRS(new Vector3(0f, 0.79f, 0f), Quaternion.Euler(0f, 0f, 180f), Vector3.one) * PeopleKit.BoardLocal;
                    g.transform.localPosition = m.GetColumn(3); g.transform.localRotation = m.rotation; g.transform.localScale = PeopleKit.BoardLocal.lossyScale;
                    g.GetComponent<MeshFilter>().sharedMesh = PeopleKit.BoardMesh;
                    if (standBoard == null) { standBoard = M(new Color(0.55f, 0.92f, 0.78f), 0.7f); standGear = M(new Color(0.08f, 0.08f, 0.09f), 0.4f); }
                    var bm = standBoard; var gm = standGear;
                    var mats = new Material[PeopleKit.BoardSrcMats.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = PeopleKit.BoardSrcMats[i] != null && PeopleKit.BoardSrcMats[i].name.StartsWith("Board") ? bm : gm;
                    g.GetComponent<MeshRenderer>().sharedMaterials = mats;
                }
                camp.AddBlock(waxAt, new Vector2(0.8f, 0.25f), wx.yaw);
            }
        }
        float benchTop; Vector3 waxAt;
        const float BenchBack = 0.5f;   // the bench's middle behind where you stand to sit on it

        // ---------------------------------------------------------------- the grid they walk on

        const float CellSize = 0.5f;
        float gx0, gz0; int gw, gh;
        bool[] open; float[] floorY;
        // a named point's own cell, opened for it though something solid stands in part of it (the bench behind its
        // seats): walked only where a body stands clear of it, and not a way through to anywhere else
        bool[] tight;
        // the cells whose floor is the snow, not the camp's stairs (CampStairs): up or down the bank's steep snow costs
        // more than the stairs beside it
        bool[] bank;
        const float BodyR = 0.24f;
        bool gridDirty = true;
        public bool GridReady => open != null && !gridDirty;

        void BuildGrid()
        {
            gridDirty = false;
            gx0 = Cx - 22f; gz0 = -100f;
            gw = Mathf.CeilToInt(49f / CellSize); gh = Mathf.CeilToInt((-30f - gz0) / CellSize);   // (out to the lookout's deck past the cliff's rail: camplayout)
            open = new bool[gw * gh]; floorY = new float[gw * gh]; tight = new bool[gw * gh]; bank = new bool[gw * gh];
            for (int j = 0; j < gh; j++)
                for (int i = 0; i < gw; i++)
                {
                    var w = CellCentre(i, j);
                    float snow = Slope.DrawnHeight(w.x, w.z);
                    float y = camp.FloorHeight(new Vector3(w.x, snow, w.z), snow);
                    floorY[j * gw + i] = y;
                    bank[j * gw + i] = y - snow < 0.05f && !Camp.OnStairs(w, 0.3f);
                    // (the cliff's rail is at +24, the left edge's mounds at -22; and the lookout's deck out over the lip)
                    bool ok = w.x - Cx < 21.5f && w.x - Cx > -21.5f || Camp.OnLookoutDeck(w.x - Cx, w.z);
                    if (ok)
                    {
                        var q = camp.PushOutOfThings(new Vector2(w.x, w.z), 0.42f, y);
                        ok = (q - new Vector2(w.x, w.z)).sqrMagnitude < 1e-6f;
                        if (!ok)
                        {
                            // (a deck standing up off the snow, the lookout's: solid under it, open on its boards)
                            float up = camp.FloorHeight(new Vector3(w.x, snow + 1.2f, w.z), snow + 1.2f);
                            if (up > y + 0.3f && (camp.PushOutOfThings(new Vector2(w.x, w.z), 0.42f, up) - new Vector2(w.x, w.z)).sqrMagnitude < 1e-6f)
                            { ok = true; y = up; floorY[j * gw + i] = up; }
                        }
                    }
                    if (ok) foreach (var k in keep) if (!k.walk && (new Vector2(w.x - k.c.x, w.z - k.c.z)).sqrMagnitude < k.r * k.r) { ok = false; break; }
                    // no edges: a drop of more than half a metre within a stride is a place they don't go
                    if (ok)
                        for (int a = 0; a < 8 && ok; a++)
                        {
                            float an = a * Mathf.PI * 0.25f;
                            float x2 = w.x + Mathf.Sin(an) * 0.9f, z2 = w.z + Mathf.Cos(an) * 0.9f;
                            if (y - Slope.DrawnHeight(x2, z2) > 0.6f && y - camp.FloorHeight(new Vector3(x2, y, z2), y) > 0.6f) ok = false;
                        }
                    open[j * gw + i] = ok;
                }
            // the lodge's door and the deck are reached from open snow: make sure each named point's cell is open
            foreach (var p in points.Values)
            {
                if (!Cell(p.at, out int ci, out int cj)) continue;
                int c = cj * gw + ci;
                if (!open[c] && BodyClear(p.at, floorY[c])) tight[c] = true;   // (the point itself is clear: only that much of the cell)
                open[c] = true;
            }
        }

        Vector3 CellCentre(int i, int j) => new Vector3(gx0 + (i + 0.5f) * CellSize, 0f, gz0 + (j + 0.5f) * CellSize);
        bool Cell(Vector3 w, out int i, out int j)
        {
            i = Mathf.FloorToInt((w.x - gx0) / CellSize); j = Mathf.FloorToInt((w.z - gz0) / CellSize);
            return i >= 0 && j >= 0 && i < gw && j < gh;
        }
        public bool Walkable(Vector3 w)
        {
            if (open == null || !Cell(w, out int i, out int j)) return false;
            int c = j * gw + i;
            return open[c] && (!tight[c] || BodyClear(w, floorY[c]));
        }
        bool BodyClear(Vector3 w, float feet) => (camp.PushOutOfThings(new Vector2(w.x, w.z), BodyR, feet) - new Vector2(w.x, w.z)).sqrMagnitude < 1e-6f;
        float FloorAt(Vector3 w) => Cell(w, out int i, out int j) ? camp.FloorHeight(w, w.y) : Slope.DrawnHeight(w.x, w.z);

        // A* over the grid, then the path pulled straight where the way is clear
        readonly Dictionary<int, int> came = new Dictionary<int, int>();
        readonly Dictionary<int, float> cost = new Dictionary<int, float>();
        readonly List<(float f, int c)> heap = new List<(float, int)>();

        bool roundPeople;
        int pathStart = -1, pathGoal = -1;
        bool OpenFor(int c) => open[c] && (!tight[c] || c == pathStart || c == pathGoal) && (!roundPeople || !people.Contains(c));
        readonly HashSet<int> people = new HashSet<int>();

        /// <summary>A path again, this time round everyone standing still (they don't move out of the way).</summary>
        bool FindPathRound(Agent me, Vector3 from, Vector3 to, List<Vector3> path)
        {
            people.Clear();
            foreach (var o in agents)
            {
                if (o == me || o.doing == Doing.Walk || o.doing == Doing.Ride || o.doing == Doing.Inside || !o.p.gameObject.activeSelf) continue;
                if (!Cell(o.pos, out int ci, out int cj)) continue;
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { int ni = ci + di, nj = cj + dj; if (ni >= 0 && nj >= 0 && ni < gw && nj < gh) people.Add(nj * gw + ni); }
            }
            foreach (var o in others)
            {
                if (!Cell(o, out int ci, out int cj)) continue;
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { int ni = ci + di, nj = cj + dj; if (ni >= 0 && nj >= 0 && ni < gw && nj < gh) people.Add(nj * gw + ni); }
            }
            if (Cell(from, out int fi, out int fj)) people.Remove(fj * gw + fi);
            if (Cell(to, out int ti, out int tj)) people.Remove(tj * gw + ti);
            roundPeople = true;
            bool ok = FindPath(from, to, path);
            roundPeople = false;
            return ok;
        }

        bool FindPath(Vector3 from, Vector3 to, List<Vector3> path)
        {
            path.Clear();
            if (open == null) return false;
            if (!Cell(from, out int si, out int sj) || !Cell(to, out int ti, out int tj)) return false;
            int start = Nearest(si, sj), goal = Nearest(ti, tj);
            if (start < 0 || goal < 0) return false;
            pathStart = start; pathGoal = goal;
            came.Clear(); cost.Clear(); heap.Clear();
            cost[start] = 0f; Push(0f, start);
            int gi = goal % gw, gj = goal / gw;
            bool found = false;
            int guard = 0;
            while (heap.Count > 0 && guard++ < 20000)
            {
                int c = Pop();
                if (c == goal) { found = true; break; }
                int ci = c % gw, cj = c / gw;
                float cc = cost[c];
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int ni = ci + di, nj = cj + dj;
                        if (ni < 0 || nj < 0 || ni >= gw || nj >= gh) continue;
                        int n = nj * gw + ni;
                        if (!OpenFor(n)) continue;
                        if (di != 0 && dj != 0 && (!OpenFor(cj * gw + ni) || !OpenFor(nj * gw + ci))) continue;   // no cutting corners
                        float flat = di != 0 && dj != 0 ? 1.414f : 1f, rise = Mathf.Abs(floorY[n] - floorY[c]);
                        float step = flat + rise * 2f;
                        // (up or down the bank's steep snow costs more: a 50 per cent slope about four times its length, so
                        // they go round by the stairs from anywhere along it: campstairs)
                        if (bank[n] && bank[c]) step += Mathf.Max(0f, rise / (flat * CellSize) - 0.2f) * flat * 18f;
                        float nc = cc + step;
                        if (cost.TryGetValue(n, out float old) && old <= nc) continue;
                        cost[n] = nc; came[n] = c;
                        int dx = Mathf.Abs(ni - gi), dz = Mathf.Abs(nj - gj);
                        Push(nc + Mathf.Max(dx, dz) + 0.414f * Mathf.Min(dx, dz), n);
                    }
            }
            if (!found) return false;
            var cells = new List<int>();
            for (int c = goal; ; c = came[c]) { cells.Add(c); if (c == start) break; }
            cells.Reverse();
            // pull it straight: keep a corner only where the straight line would leave the open grid
            var pts = new List<Vector3>();
            foreach (var c in cells) pts.Add(CellCentre(c % gw, c / gw));
            pts[pts.Count - 1] = new Vector3(to.x, 0f, to.z);
            int at = 0;
            path.Add(pts[0]);
            while (at < pts.Count - 1)
            {
                int far = at + 1;
                for (int k = pts.Count - 1; k > at + 1; k--) if (Clear(pts[at], pts[k]) && !Steep(pts[at], pts[k])) { far = k; break; }
                path.Add(pts[far]); at = far;
            }
            return true;
        }

        /// <summary>A straight line between two points goes up or down steep snow off the stairs (more than 30 per cent).</summary>
        bool Steep(Vector3 a, Vector3 b)
        {
            float d = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            int n = Mathf.CeilToInt(d / CellSize);
            if (n < 1 || !Cell(a, out int i0, out int j0)) return false;
            int c0 = j0 * gw + i0;
            for (int k = 1; k <= n; k++)
            {
                if (!Cell(Vector3.Lerp(a, b, k / (float)n), out int i, out int j)) return false;
                int c = j * gw + i;
                if (bank[c] && bank[c0] && Mathf.Abs(floorY[c] - floorY[c0]) > 0.3f * d / n) return true;
                c0 = c;
            }
            return false;
        }

        // the open list: a binary heap on f
        void Push(float f, int c)
        {
            heap.Add((f, c));
            int k = heap.Count - 1;
            while (k > 0) { int up = (k - 1) >> 1; if (heap[up].f <= heap[k].f) break; (heap[up], heap[k]) = (heap[k], heap[up]); k = up; }
        }
        int Pop()
        {
            int c = heap[0].c;
            heap[0] = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
            int k = 0;
            while (true)
            {
                int l = 2 * k + 1, r = l + 1, m = k;
                if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                if (m == k) break;
                (heap[m], heap[k]) = (heap[k], heap[m]); k = m;
            }
            return c;
        }

        int Nearest(int i, int j)
        {
            for (int r = 0; r < 8; r++)
                for (int dj = -r; dj <= r; dj++)
                    for (int di = -r; di <= r; di++)
                    {
                        if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != r) continue;
                        int ni = i + di, nj = j + dj;
                        if (ni < 0 || nj < 0 || ni >= gw || nj >= gh) continue;
                        if (open[nj * gw + ni]) return nj * gw + ni;
                    }
            return -1;
        }

        /// <summary>Nothing solid of the camp's within a body's reach of here (for the last step to a point beside something).</summary>
        bool Clear(Vector3 p) => (camp.PushOutOfThings(new Vector2(p.x, p.z), 0.26f, p.y) - new Vector2(p.x, p.z)).sqrMagnitude < 1e-6f;

        bool Clear(Vector3 a, Vector3 b)
        {
            float len = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            int n = Mathf.CeilToInt(len / (CellSize * 0.5f));
            for (int k = 1; k < n; k++)
            {
                var p = Vector3.Lerp(a, b, k / (float)n);
                if (!Walkable(p)) return false;
                if (roundPeople && Cell(p, out int ci, out int cj) && people.Contains(cj * gw + ci)) return false;
                // and a body's width either side
                var side = new Vector3(b.z - a.z, 0f, a.x - b.x).normalized * 0.3f;
                if (!Walkable(p + side) || !Walkable(p - side)) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- what they do

        /// <summary>Set one of the camp's own going: a fresh look, a place to start, a first thing to do.</summary>
        void Begin(Agent a, bool scatter)
        {
            a.doing = Doing.Idle; a.t = 0f; a.partner = null; a.gear = false;
            if (a.spot != null && a.spot.holder == a) a.spot.holder = null;
            a.spot = null; a.path.Clear();
            var p = a.p;
            p.gameObject.SetActive(true);
            p.mode = Person.Mode.Stand; p.held = Person.Held.None; p.skisOn = p.polesOn = p.skisOnShoulder = false; p.boardInHand = -1; p.boardFoot = -1;
            p.phoneUp = 0f; p.pointW = 0f; p.handW[0] = p.handW[1] = 0f; p.lookWeight = 0f; p.crouch = 0f; p.sitW = 0f;
            // where they start: somewhere their first errand makes sense (scattered when the camp is new)
            var door = P("door");
            Vector3 at = door != null ? OutOfDoor(a, door) : Ground(0f, -70f);
            float yaw = door != null ? door.yaw : 0f;
            if (scatter)
            {
                string[] starts = { "lookout.1", "lookout.2", "chat.1", "chat.1", "bench.1", "wax", "rail.1", "door" };
                var sp = P(starts[a.role % starts.Length]);
                if (sp != null) { at = sp.at; yaw = sp.yaw; }
                if (a.role == 3 && sp != null) { at += Quaternion.Euler(0f, sp.yaw, 0f) * Vector3.forward * 1.25f; yaw = sp.yaw + 180f; }
            }
            a.pos = at; a.yaw = yaw;
            Next(a);
        }

        /// <summary>What next, by role: the camp's eight each have their own round.</summary>
        void Next(Agent a)
        {
            if (!a.own)
            {
                // someone else's, wandering: home (if they have one) and somewhere about the camp, by turns
                bool atHome = a.home != null && a.spot != null && a.spot.name == a.home;
                if (a.home != null && !atHome) Go(a, a.home, Doing.Look, Random.Range(20f, 40f));
                else Go(a, Free("rail.2", "chat.3", "snowman", "lookout.2", "chat.2"), Doing.Look, Random.Range(12f, 25f));
                return;
            }
            float r = Hash(a, 1);
            switch (a.role)
            {
                case 0:   // the photographer: the lookout, the rail, back in
                    if (a.doing == Doing.Idle || a.doing == Doing.Inside) Go(a, Free("lookout.1", "lookout.2"), Doing.Look, Random.Range(18f, 30f));
                    else if (a.spot != null && a.spot.name.StartsWith("lookout")) Go(a, Free("rail.1", "rail.2"), Doing.Look, Random.Range(10f, 16f));
                    else Go(a, "door", Doing.Inside, Random.Range(15f, 30f));
                    break;
                case 1:   // someone who likes the view
                    if (a.doing == Doing.Look && r < 0.5f) Go(a, Free("snowman", "chat.3"), Doing.Stand, Random.Range(6f, 10f));
                    else Go(a, Free("lookout.2", "lookout.1", "rail.2"), Doing.Look, Random.Range(20f, 35f));
                    break;
                case 2:   // two friends talking, and walking on together
                case 3:
                    Pair(a);
                    break;
                case 4:   // the bench
                    if (a.doing == Doing.Sit) Go(a, Free("snowman", "rail.2"), Doing.Look, Random.Range(8f, 14f));
                    else { string b = Free("bench.1", "bench.2"); Go(a, b, b.StartsWith("bench") ? Doing.Sit : Doing.Look, Random.Range(30f, 55f)); }
                    break;
                case 5:   // the waxer: waxes, goes in, comes out to ride
                    if (a.doing == Doing.Wax) Go(a, "door", Doing.Inside, Random.Range(10f, 20f));
                    else if (a.doing == Doing.Inside) { Redress(a); if (r < 0.5f) Dropper(a); else Go(a, "wax", Doing.Wax, Random.Range(35f, 70f)); }
                    else if (a.doing == Doing.Ride) { a.p.gameObject.SetActive(false); a.gear = false; a.doing = Doing.Inside; a.t = 0f; a.until = Random.Range(15f, 30f); a.pos = P("door").at; }
                    else if (a.doing == Doing.Strap) Ride(a);
                    else if (a.doing == Doing.Walk && a.gear) Strap(a);
                    else Go(a, "wax", Doing.Wax, Random.Range(35f, 70f));
                    break;
                case 6:   // to the rail and back in
                    if (a.doing == Doing.Idle || a.doing == Doing.Inside) { if (a.doing == Doing.Inside) Redress(a); Go(a, Free("rail.1", "rail.2", "chat.2"), Doing.Look, Random.Range(10f, 20f)); }
                    else if (r < 0.5f && a.doing != Doing.Stand) Go(a, Free("chat.2", "snowman"), Doing.Stand, Random.Range(8f, 15f));
                    else Go(a, "door", Doing.Inside, Random.Range(20f, 40f));
                    break;
                default:  // out of the lodge with gear, strap in, ride off; then someone else
                    if (a.doing == Doing.Strap) Ride(a);
                    else if (a.doing == Doing.Walk && a.gear) Strap(a);
                    else if (a.doing == Doing.Ride) { a.p.gameObject.SetActive(false); a.gear = false; a.doing = Doing.Inside; a.t = 0f; a.until = Random.Range(20f, 45f); a.pos = P("door").at; }
                    else { if (a.doing == Doing.Inside) Redress(a); Dropper(a); }
                    break;
            }
        }

        float Hash(Agent a, int k) => Slope.Hash01(a.role * 131 + spawned, Mathf.FloorToInt(clock * 0.37f) + k, 711);

        /// <summary>The first of these points nobody is using (or the first, shared, if all are taken).</summary>
        string Free(params string[] names)
        {
            foreach (var n in names) { var p = P(n); if (p != null && p.holder == null && !NearOther(p.at, 1.4f)) return n; }
            // all taken: somewhere else to stand and look
            foreach (var n in new[] { "snowman", "chat.3", "chat.2", "rail.2", "rail.1", "chat.1" }) { var p = P(n); if (p != null && p.holder == null && !NearOther(p.at, 1.4f)) return n; }
            return names[0];
        }

        void Go(Agent a, string point, Doing then, float stay)
        {
            var sp = P(point);
            // (one of the camp's characters standing there: the nearest free point of the same kind instead)
            if (sp != null && then != Doing.Inside && NearOther(sp.at, 1.4f))
            {
                string kind = point.Split('.')[0];
                foreach (var q in points.Values) if (q.name.StartsWith(kind) && q.holder == null && !NearOther(q.at, 1.4f)) { sp = q; break; }
            }
            if (sp == null) { a.doing = Doing.Stand; a.t = 0f; a.until = stay; return; }
            if (a.spot != null && a.spot.holder == a) a.spot.holder = null;
            a.spot = sp; if (sp.holder == null) sp.holder = a;
            Vector3 target = sp.at;
            if (sp.holder != a && then != Doing.Inside)
            {
                // taken: stand beside it
                target += Quaternion.Euler(0f, sp.yaw + 90f, 0f) * Vector3.forward * 1.1f;
            }
            WalkTo(a, target, then, stay);
        }

        void WalkTo(Agent a, Vector3 target, Doing then, float stay)
        {
            if (!FindPath(a.pos, target, a.path)) { a.path.Clear(); a.path.Add(a.pos); a.path.Add(target); }
            a.pathAt = 1;
            a.doing = Doing.Walk; a.t = 0f;
            a.arrived = () => { a.doing = then; a.t = 0f; a.until = stay; Arrive(a); };
            a.progress = float.MaxValue; a.progressT = 0f;
        }

        void Arrive(Agent a)
        {
            if (a.spot != null && (a.spot.holder == a || a.doing == Doing.Inside)) a.yaw = a.spot.yaw;
            if (a.doing == Doing.Inside) { a.p.gameObject.SetActive(false); if (a.spot != null && a.spot.holder == a) a.spot.holder = null; }
            if (a.doing == Doing.Sit) a.sitW = 0f;
        }

        void Pair(Agent a)
        {
            // the first of the two leads: picks where to go; the other comes along to face them (or sit beside them)
            var lead = agents.Find(o => o.own && o.role == 2);
            var other = agents.Find(o => o.own && o.role == 3);
            if (lead == null || other == null) { Go(a, "chat.1", Doing.Stand, 20f); return; }
            if (a != lead) { a.partner = lead; a.doing = Doing.Stand; a.t = 0f; a.until = 3f; return; }   // (waits for the lead)
            a.partner = other;
            var spots = new List<string> { "chat.1", "chat.3", "chat.2" };
            spots.RemoveAll(n => P(n) == null || NearOther(P(n).at, 2.2f));   // (not where the camp's characters are standing)
            if (spots.Count == 0) spots.Add("chat.3");
            var b1 = P("bench.1"); var b2 = P("bench.2");
            if (b1 != null && b2 != null && b1.holder == null && b2.holder == null) spots.Add("bench.1");
            string to = spots[Mathf.FloorToInt(Hash(a, 3) * spots.Count) % spots.Count];
            if (a.spot != null && a.spot.name == to) to = spots[(spots.IndexOf(to) + 1) % spots.Count];
            bool bench = to == "bench.1";
            float stay = Random.Range(25f, 45f);
            Go(a, to, bench ? Doing.Sit : Doing.Chat, stay);
            var sp = P(to);
            if (sp == null) return;
            if (other.spot != null && other.spot.holder == other) other.spot.holder = null;
            if (bench) { other.spot = b2; b2.holder = other; WalkTo(other, b2.at, Doing.Sit, stay); }
            else { other.spot = null; WalkTo(other, sp.at + Quaternion.Euler(0f, sp.yaw, 0f) * Vector3.forward * 1.25f, Doing.Chat, stay + 30f); }
            other.partner = lead;
        }

        /// <summary>Someone new (back out of the lodge as somebody else).</summary>
        void Redress(Agent a)
        {
            a.p.Dress(Look());
            a.p.gameObject.SetActive(true);
            var door = P("door");
            if (door != null) { a.pos = OutOfDoor(a, door); a.yaw = door.yaw; }
        }

        /// <summary>Where someone comes out of the door: on its step, beside anyone already standing there.</summary>
        Vector3 OutOfDoor(Agent a, Point door)
        {
            var side = Quaternion.Euler(0f, door.yaw + 90f, 0f) * Vector3.forward;
            foreach (float off in new[] { 0f, 0.75f, -0.75f, 1.5f, -1.5f })
            {
                var at = door.at + side * off;
                bool free = true;
                foreach (var o in agents) if (o != a && o.p.gameObject.activeSelf && o.doing != Doing.Inside && (o.pos - at).sqrMagnitude < 0.7f * 0.7f) { free = false; break; }
                if (free) { at.y = Slope.DrawnHeight(at.x, at.z); return at; }
            }
            return door.at;
        }

        void Dropper(Agent a)
        {
            a.gear = true;
            var look = a.p.Look;
            if (look.gear == PersonGear.None) look.gear = PersonGear.Snowboard;
            a.p.gameObject.SetActive(true);
            Go(a, Free("gate.L", "gate.R"), Doing.Walk, 0f);
            // (arriving at the gate: Next sees them walking with gear and straps in)
            a.arrived = () => { a.doing = Doing.Walk; Next(a); };
        }

        void Strap(Agent a)
        {
            a.doing = Doing.Strap; a.t = 0f; a.until = a.p.Look.gear == PersonGear.Skis ? 3.2f : 6.5f;
            // facing down the start
            a.yaw = 0f;
        }

        void Ride(Agent a)
        {
            a.doing = Doing.Ride; a.t = 0f; a.until = 30f;
            a.rideVel = Vector3.forward * 0.6f;
            a.rideX = a.pos.x;
            if (a.spot != null && a.spot.holder == a) a.spot.holder = null;
            a.spot = null;
        }

        // ---------------------------------------------------------------- each frame

        void Update()
        {
            if (!On || camp == null || open == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            // only while the camp is about (the title's view of it, the hub)
            if ((cam.transform.position - Ground(0f, -70f)).sqrMagnitude > 400f * 400f) return;
            Step(Time.deltaTime, cam.transform.position);
        }

        Vector3 hero; float heroSpeed; bool heroHere;
        readonly List<Agent> byDistance = new List<Agent>();
        public float LastStepMs { get; private set; }

        /// <summary>One frame of camp life (public for the test tools, which have no frame time).</summary>
        static readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        sealed class ByDistance : IComparer<Agent> { public Vector3 eye; public int Compare(Agent x, Agent y) => (x.pos - eye).sqrMagnitude.CompareTo((y.pos - eye).sqrMagnitude); }
        readonly ByDistance byEye = new ByDistance();

        public void Step(float dt, Vector3 eye)
        {
            sw.Restart();
            if (gridDirty) BuildGrid();
            PeopleLod.BuildPending();
            clock += dt;
            heroHere = camp.Active && !camp.Falling;
            hero = camp.WalkerPosition; heroSpeed = camp.WalkerSpeed;
            Others();
            if (!camp.Active)
                foreach (var a in agents) if (a.own && a.doing == Doing.Ride) { a.p.gameObject.SetActive(false); a.gear = false; a.doing = Doing.Inside; a.t = 0f; a.until = 20f; a.pos = P("door").at; }
            foreach (var a in agents)
            {
                if (a.held) continue;
                a.t += dt;
                if (a.doing == Doing.Walk) Walk(a, dt);
                else if (a.doing == Doing.Ride) RideStep(a, dt);
                else if (a.doing == Doing.Idle || (a.doing != Doing.Chat || a.partner == null || a.partner.doing != Doing.Walk) && a.t > a.until && a.doing != Doing.Held) Next(a);
                // a glance: the hero going by at a run, close
                if (heroHere && (hero - a.pos).sqrMagnitude < 7f * 7f && heroSpeed > Camp.MaxSpeed * 0.9f) { a.lookT = 1.6f; a.look = hero + Vector3.up * 1.3f; }
                a.lookT -= dt;
            }
            // nobody stands in anybody: walkers give way to those standing still, and to each other
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                if (a.held || a.doing == Doing.Ride || a.doing == Doing.Inside || !a.p.gameObject.activeSelf) continue;
                for (int j = 0; j < agents.Count; j++)
                {
                    var o = agents[j];
                    if (o == a || o.doing == Doing.Ride || o.doing == Doing.Inside || !o.p.gameObject.activeSelf) continue;
                    if (Mathf.Abs(a.pos.y - o.pos.y) > OtherFloor) continue;   // (one over the other: the lodge's deck over its café)
                    Vector3 d = a.pos - o.pos; d.y = 0f;
                    float m = d.magnitude;
                    const float gap = 0.56f;
                    if (m >= gap) continue;
                    bool aMoves = a.doing == Doing.Walk, oMoves = o.doing == Doing.Walk && !o.held;
                    if (!aMoves) continue;   // (the one walking steps aside)
                    Vector3 dir = m > 1e-4f ? d / m : new Vector3(Mathf.Cos(i), 0f, Mathf.Sin(i));
                    float push = (gap - m) * (oMoves ? 0.5f : 1f);
                    // straight away from them, or else aside (a doorway, a bench's end), or else where they are already off the grid
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    Vector3 np = a.pos + dir * push;
                    if (!Walkable(np)) np = a.pos + (dir + side).normalized * push;
                    if (!Walkable(np)) np = a.pos + (dir - side).normalized * push;
                    if (Walkable(np) || (!Walkable(a.pos) && Clear(np))) { np.y = camp.FloorHeight(np, a.pos.y); a.pos = np; }
                }
            }
            foreach (var a in agents)
            {
                if (a.held || a.doing == Doing.Ride || a.doing == Doing.Inside || a.doing == Doing.Sit || a.doing == Doing.Strap || !a.p.gameObject.activeSelf) continue;
                foreach (var o in others)
                {
                    if (Mathf.Abs(a.pos.y - o.y) > OtherFloor) continue;
                    Vector3 d = a.pos - o; d.y = 0f;
                    float m = d.magnitude;
                    const float gap = 0.62f;
                    if (m >= gap) continue;
                    Vector3 dir = m > 1e-4f ? d / m : Vector3.right;
                    var np = a.pos + dir * (gap - m);
                    if (Walkable(np) || (!Walkable(a.pos) && Clear(np))) { np.y = camp.FloorHeight(np, a.pos.y); a.pos = np; }
                }
            }
            // levels of detail: the full model for the nearest few
            byDistance.Clear();
            foreach (var a in agents) if (a.p != null && a.p.gameObject.activeSelf) byDistance.Add(a);
            byEye.eye = eye;
            byDistance.Sort(byEye);
            for (int i = 0; i < byDistance.Count; i++)
            {
                var a = byDistance[i];
                float d = (a.pos - eye).magnitude;
                a.p.SetLod(d < FullRange && i < FullCap ? 0 : d < SimpleRange ? 1 : 2);
                if (a.held) continue;   // (posed by whoever holds them)
                Pose(a, dt);
                a.p.Apply(dt);
                if (a.p.Lod < 2) Steps(a, d, dt);
            }
            Murmur(eye, dt);
            LastStepMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        bool Reacting(Agent a) => reactT > 0f && !a.goer && (a.pos - reactAt).sqrMagnitude < 50f * 50f;

        // the camp's named characters, who live by their own rules (CampCouple: Val, Breanna and Paul; Alyssa at the
        // fire): the camp's people walk round them, give them their spots, and step aside when one comes through
        readonly List<Vector3> others = new List<Vector3>(), othersGoing = new List<Vector3>();
        public IReadOnlyList<Vector3> OthersNow => others;
        void Others()
        {
            others.Clear(); othersGoing.Clear();
            var cpl = camp.Couple;
            if (cpl != null)
                for (int who = 1; who <= 3; who++)
                {
                    var t = cpl.Of(who); if (t != null && t.gameObject.activeInHierarchy) others.Add(t.position);
                    var g = cpl.GoingTo(who); if (g.HasValue) othersGoing.Add(g.Value);
                }
            var al = camp.Alyssa;
            if (al != null && al.isActiveAndEnabled) others.Add(al.Seat);
        }
        /// <summary>One of the camp's characters is there, or on the way there.</summary>
        bool NearOther(Vector3 p, float r)
        {
            foreach (var o in others) { float dx = o.x - p.x, dz = o.z - p.z; if (dx * dx + dz * dz < r * r) return true; }
            foreach (var o in othersGoing) { float dx = o.x - p.x, dz = o.z - p.z; if (dx * dx + dz * dz < r * r) return true; }
            return false;
        }
        static bool Near(Vector3 a, Vector3 b, float r) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz < r * r; }

        // ---- one way for everyone in the camp to keep out of each other: the camp's people, CampCouple's Val, Breanna
        // and Paul (who: 1, 2, 3), and Alyssa at the fire (a fixed place)

        /// <summary>
        /// A step one of CampCouple's characters wants to take (who: 1 Val, 2 Breanna, 3 Paul), moved out of everyone
        /// else's way: the camp's people, Alyssa, and Paul and the couple out of each other's (Val and Breanna walk hand
        /// in hand, so not out of each other's).
        /// </summary>
        public Vector3 Around(int who, Vector3 from, Vector3 want)
        {
            const float gap = 0.62f;
            Vector3 Push(Vector3 w, Vector3 o) { var d = w - o; d.y = 0f; float m = d.magnitude; if (m < gap && m > 1e-4f) w += d / m * (gap - m); return w; }
            foreach (var a in agents) if (a.p != null && a.p.gameObject.activeSelf && a.doing != Doing.Inside && a.doing != Doing.Ride) want = Push(want, a.pos);
            var al = camp.Alyssa; if (al != null && al.isActiveAndEnabled) want = Push(want, al.Seat);
            var c = camp.Couple;
            if (c != null)
                for (int k = 1; k <= 3; k++)
                {
                    if (k == who || (who != 3 && k != 3)) continue;   // (Paul round the couple, the couple round Paul)
                    var t = c.Of(k); if (t != null) want = Push(want, t.position);
                }
            return want;
        }

        /// <summary>
        /// Is a spot taken (who: CampCouple's 1 Val, 2 Breanna, 3 Paul)? By any of the camp's people standing there or on
        /// their way there, and for Paul by where the couple is going (and for the couple by where Paul is).
        /// </summary>
        public bool Taken(Vector3 at, int who, float r = 1.6f)
        {
            foreach (var a in agents)
            {
                if (a.p == null || !a.p.gameObject.activeSelf || a.doing == Doing.Inside || a.doing == Doing.Ride) continue;
                if (Near(a.pos, at, r) || (a.spot != null && a.spot.holder == a && Near(a.spot.at, at, r))) return true;
            }
            var c = camp.Couple;
            if (c != null)
                for (int k = 1; k <= 3; k++)
                {
                    if (k == who || (who != 3 && k != 3)) continue;
                    var g = c.GoingTo(k); if (g.HasValue && Near(g.Value, at, r)) return true;
                }
            return false;
        }

        void Walk(Agent a, float dt)
        {
            if (Reacting(a)) { a.speed = Mathf.MoveTowards(a.speed, 0f, dt * 4f); return; }   // (stopped dead, looking)
            if (a.pathAt >= a.path.Count) { a.speed = 0f; a.arrived?.Invoke(); return; }
            Vector3 goal = a.path[a.pathAt]; goal.y = a.pos.y;
            Vector3 to = goal - a.pos; to.y = 0f;
            float dist = to.magnitude;
            bool last = a.pathAt == a.path.Count - 1;
            if (dist < (last ? 0.15f : 0.5f))
            {
                a.pathAt++;
                a.progress = float.MaxValue; a.progressT = 0f;   // (a new leg: progress is measured along it afresh)
                if (a.pathAt >= a.path.Count) { a.speed = 0f; a.arrived?.Invoke(); }
                return;
            }
            Vector3 dir = to / dist;
            // step round the others and the hero
            Vector3 push = Vector3.zero;
            foreach (var o in agents)
            {
                if (o == a || o.p == null || !o.p.gameObject.activeSelf || o.doing == Doing.Ride) continue;
                Vector3 d = a.pos - o.pos; d.y = 0f;
                float m = d.magnitude;
                if (m < 1.3f && m > 1e-3f) push += d / m * (1.3f - m) * (o.doing == Doing.Walk ? 1.2f : 2f);
            }
            foreach (var o in others)
            {
                Vector3 d = a.pos - o; d.y = 0f;
                float m = d.magnitude;
                if (m < 1.4f && m > 1e-3f) push += d / m * (1.4f - m) * 2f;
            }
            float heroGap = 99f;
            if (heroHere)
            {
                Vector3 d = a.pos - hero; d.y = 0f;
                heroGap = d.magnitude;
                if (heroGap < 2.2f && heroGap > 1e-3f) push += d / heroGap * (2.2f - heroGap) * 2.5f;
                // the hero coming at them: step to their right
                if (heroGap < 3f && Vector3.Dot(-d / heroGap, dir) > 0.6f) push += new Vector3(dir.z, 0f, -dir.x) * 0.8f;
            }
            Vector3 want = (dir + push).normalized;
            // turn, slowing for a sharp one; slow down close to the end
            float wantYaw = Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg;
            float turn = Mathf.Abs(Mathf.DeltaAngle(a.yaw, wantYaw));
            a.yaw = Mathf.MoveTowardsAngle(a.yaw, wantYaw, 240f * dt);
            float natural = a.p.NaturalSpeed;
            float target = natural * Mathf.Lerp(1f, 0.25f, Mathf.Clamp01((turn - 30f) / 90f)) * (last ? Mathf.Clamp01(dist / 1.2f) * 0.6f + 0.4f : 1f);
            if (heroGap < 0.9f) target = 0f;
            a.speed = Mathf.MoveTowards(a.speed, target, dt * 2.2f);
            Vector3 f = Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward;
            Vector3 next = a.pos + f * a.speed * dt;
            if (!Walkable(next) && !(last && dist < 1.2f && Clear(next)))
            {
                // slide along whatever is in the way
                var nx = new Vector3(next.x, a.pos.y, a.pos.z); var nz = new Vector3(a.pos.x, a.pos.y, next.z);
                next = Walkable(nx) ? nx : Walkable(nz) ? nz : (Walkable(a.pos) || !Clear(next) ? a.pos : next);
            }
            float moved = new Vector2(next.x - a.pos.x, next.z - a.pos.z).magnitude;
            next.y = camp.FloorHeight(next, a.pos.y);
            a.pos = next;
            a.p.pace = Mathf.Clamp01(a.speed / Mathf.Max(0.3f, natural));
            // on the stairs: a whole number of treads a step (two, mostly), each foot down on a tread's middle
            int per = Mathf.Clamp(Mathf.RoundToInt(a.p.NaturalStep / Camp.StairGoing), 1, 2);
            a.p.StairStep = Camp.OnStairs(a.pos, 0.1f) ? per * Camp.StairGoing : 0f;
            // (the walk blended in as far as makes that its stride: their own clip's step at their size)
            if (a.p.StairStep > 0f) a.p.pace = Mathf.Min(a.p.pace, PeopleLod.BlendForStep(a.p.StairStep / a.p.Scale));
            a.p.Advance(moved);
            if (a.p.StairStep > 0f && moved > 1e-4f)
            {
                float ph = Camp.StairPhase(a.pos, f, per, Camp.StairLeadWalk * Mathf.Min(1f, a.p.StairStep / a.p.NaturalStep) * a.p.Scale);
                if (!float.IsNaN(ph)) a.p.LockPhase(ph / (2f * Mathf.PI), dt);
            }
            // stuck? (no nearer the goal in a few seconds): find the way again, or give up and do something else
            float left = dist;
            if (left < a.progress - 0.3f) { a.progress = left; a.progressT = 0f; }
            else if ((a.progressT += dt) > 4f)
            {
                a.stuck++; a.progressT = 0f; a.progress = float.MaxValue;
                StuckAt.Add(new Vector2(a.pos.x - Cx, a.pos.z));
                {
                    float no = 99f; foreach (var o in others) no = Mathf.Min(no, new Vector2(o.x - a.pos.x, o.z - a.pos.z).magnitude);
                    float na = 99f; foreach (var o in agents) if (o != a && o.p.gameObject.activeSelf && o.doing != Doing.Inside) na = Mathf.Min(na, new Vector2(o.pos.x - a.pos.x, o.pos.z - a.pos.z).magnitude);
                    float nh = heroHere ? new Vector2(hero.x - a.pos.x, hero.z - a.pos.z).magnitude : 99f;
                    var nxt = a.path[Mathf.Min(a.pathAt, a.path.Count - 1)];
                    StuckWhy.Add($"{a.role}:{(a.spot != null ? a.spot.name : "-")} at ({a.pos.x - Cx:0.0},{a.pos.z:0.0}) next ({nxt.x - Cx:0.0},{nxt.z:0.0}) walkable {Walkable(nxt)} pathAt {a.pathAt}/{a.path.Count} other {no:0.0} person {na:0.0} hero {nh:0.0} speed {a.speed:0.00}");
                }
                LastStuck = $"{a.role}:{(a.spot != null ? a.spot.name : "-")} at ({a.pos.x - Cx:0.0}, {a.pos.z:0.0}) heading for ({a.path[a.path.Count - 1].x - Cx:0.0}, {a.path[a.path.Count - 1].z:0.0})";
                var end = a.path[a.path.Count - 1];
                // held up by one of the camp's characters (in the way, or standing where they were going): something
                // else to do, unless it's the door they want (then they wait their turn)
                bool byOther = NearOther(a.pos, 1.8f) || NearOther(end, 2f);
                bool door = a.spot != null && a.spot.name == "door";
                if (byOther && !door) { if (a.spot != null && a.spot.holder == a) a.spot.holder = null; a.spot = null; a.doing = Doing.Stand; a.t = 0f; a.until = 0.6f; a.gear = a.gear && a.role >= 5; }
                else if (a.stuck % 3 == 0) { a.doing = Doing.Stand; a.t = 0f; a.until = 1f; }
                else if (!FindPathRound(a, a.pos, end, a.path) && !FindPath(a.pos, end, a.path)) { a.path.Clear(); a.path.Add(a.pos); a.path.Add(end); }
                a.pathAt = 1;
                StuckCount++;
            }
        }
        public int StuckCount { get; private set; }
        public string LastStuck { get; private set; }
        public readonly List<Vector2> StuckAt = new List<Vector2>();
        public readonly List<string> StuckWhy = new List<string>();   // (for the tests: where people were held up, from the run's centre)

        void RideStep(Agent a, float dt)
        {
            // down the start: through the gate, faster down the pad, over the lip and out of sight
            float gz = Slope.GateZ;
            float target = Cx + Mathf.Clamp(a.rideX - Cx, -2.5f, 2.5f) * (a.pos.z < gz ? 1f : 0.6f);
            float speed = a.rideVel.magnitude;
            speed = Mathf.MoveTowards(speed, a.pos.z < gz ? 2.5f : a.pos.z < 0f ? 9f : 14f, dt * (a.pos.z < gz ? 1.2f : 2.4f));
            float x = a.pos.x, z = a.pos.z;
            // a gentle S down the pad; and wide of the hero if they're in the way
            float carve = a.pos.z > gz ? Mathf.Sin((a.pos.z - gz) * 0.12f) * 2.2f : 0f;
            if (heroHere && Mathf.Abs(hero.z - z - 4f) < 5f && Mathf.Abs(hero.x - x) < 2.5f) target += hero.x > x ? -3f : 3f;
            float vx = Mathf.Clamp((target + carve - x) * 0.8f, -2.5f, 2.5f);
            var v = new Vector3(vx, 0f, Mathf.Sqrt(Mathf.Max(0.01f, speed * speed - vx * vx)));
            a.rideVel = v;
            a.pos += v * dt;
            a.pos.y = Slope.DrawnHeight(a.pos.x, a.pos.z);
            a.yaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            a.p.carve = Mathf.Clamp(-vx * 0.4f, -1f, 1f);
            if (a.pos.z > 45f || a.t > a.until) Next(a);
        }

        // ---------------------------------------------------------------- posing them

        void Pose(Agent a, float dt)
        {
            var p = a.p;
            p.lookWeight = 0f; p.turnChest = 0f; p.pointW = 0f;
            p.handW[0] = p.handW[1] = 0f;
            float phoneWant = 0f;
            p.crouch = 0f; p.skisOn = false; p.polesOn = false; p.skisOnShoulder = false; p.boardInHand = -1; p.held = Person.Held.None;
            p.boardFoot = -1;
            // turning to look at the fall (whole body, if they're on their feet), and back again after
            bool turnable = a.doing == Doing.Walk || a.doing == Doing.Stand || a.doing == Doing.Look || a.doing == Doing.Chat || a.doing == Doing.Wax;
            float turnWant = 0f;
            if (turnable && Reacting(a))
            {
                Vector3 to = reactAt - a.pos; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) turnWant = Mathf.DeltaAngle(a.yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            }
            a.turn = Mathf.MoveTowardsAngle(a.turn, turnWant, (turnWant != 0f ? 220f : 90f) * dt);
            var rot = Quaternion.Euler(0f, a.yaw + a.turn, 0f);
            var lookAhead = a.pos + rot * Vector3.forward * 20f + Vector3.up * 1.2f;
            float t = clock + a.role * 7.3f;
            if (a.gear && a.doing == Doing.Walk)
            {
                // carrying the board under the arm, or skis on the shoulder
                if (p.Look.gear == PersonGear.Skis) p.skisOnShoulder = true;
                else { p.held = Person.Held.Placed; p.boardInHand = 1; }
                p.polesDir = Vector3.down + rot * Vector3.forward * 0.15f;
            }
            switch (a.doing)
            {
                case Doing.Walk:
                    p.mode = Person.Mode.Walk;
                    p.pace = Mathf.Clamp01(a.speed / Mathf.Max(0.3f, p.NaturalSpeed));
                    p.run = 0f;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    break;
                case Doing.Sit:
                {
                    // onto the bench: the seat's edge under the hips, facing out
                    p.mode = Person.Mode.Sit;
                    a.sitW = Mathf.MoveTowards(a.sitW, a.t < a.until - 1.2f ? 1f : 0f, dt * 1.3f);
                    p.sitW = Mathf.SmoothStep(0f, 1f, a.sitW);
                    p.feetDown = true; p.footY = a.pos.y; p.feetForward = 0.02f; p.lean = 4f; p.hipsForward = 0f; p.kneesApart = p.Look.female ? 0.25f : 0.6f;
                    var seat = new Vector3(a.pos.x, benchTop > 0f ? benchTop : a.pos.y + 0.45f, a.pos.z) - rot * Vector3.forward * (BenchBack - 0.04f);
                    p.transform.SetPositionAndRotation(Vector3.Lerp(a.pos, seat, p.sitW), rot);
                    // hands on the knees; a look at the friend beside, or down the start
                    for (int i = 0; i < 2; i++) { p.handAt[i] = seat + rot * new Vector3(i == 0 ? -0.13f : 0.13f, 0.2f, 0.33f); p.handW[i] = p.sitW; }
                    if (a.partner != null && a.partner.doing == Doing.Sit && Mathf.Repeat(t, 9f) < 5f) { p.lookAt = a.partner.p.HeadPosition; p.lookWeight = 0.8f; p.turnChest = 0.3f; }
                    else { p.lookAt = a.pos + rot * new Vector3(Mathf.Sin(t * 0.13f) * 12f, -3f, 25f); p.lookWeight = 0.6f; }
                    if (a.t > a.until) { a.sitW = 0f; p.sitW = 0f; }
                    break;
                }
                case Doing.Chat:
                {
                    p.mode = Person.Mode.Stand;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    if (a.partner != null && a.partner.p.gameObject.activeSelf && a.partner.doing == Doing.Chat)
                    {
                        // face each other; the one talking moves a hand; nods
                        Vector3 to = a.partner.pos - a.pos; to.y = 0f;
                        if (to.sqrMagnitude > 0.01f) a.yaw = Mathf.MoveTowardsAngle(a.yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 120f * dt);
                        p.lookAt = a.partner.p.HeadPosition + Vector3.down * 0.05f * Mathf.Sin(t * 2.1f); p.lookWeight = 0.85f;
                        bool talking = Mathf.Repeat(t + (a.role == 3 ? 3.5f : 0f), 7f) < 3.5f;
                        if (talking)
                        {
                            float g = Mathf.Sin(t * 2.7f) * 0.5f + 0.5f;
                            p.handAt[1] = a.pos + rot * new Vector3(0.2f + 0.05f * g, 1.08f * p.Scale + 0.06f * Mathf.Sin(t * 3.3f), 0.3f);
                            p.handW[1] = 0.85f * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, Mathf.Repeat(t + (a.role == 3 ? 3.5f : 0f), 7f) / 0.6f));
                        }
                    }
                    else { p.lookAt = lookAhead; p.lookWeight = 0.3f; }
                    break;
                }
                case Doing.Look:
                {
                    // at the rail: the view; the photographer holds the phone up now and then
                    p.mode = Person.Mode.Stand;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    var view = a.pos + rot * new Vector3(Mathf.Sin(t * 0.21f) * 30f, -12f, 60f);
                    p.lookAt = view; p.lookWeight = 0.7f; p.turnChest = 0.25f;
                    if (a.role == 0 && Mathf.Repeat(t, 9f) > 4.5f) phoneWant = 1f;
                    if (a.role == 1 && Mathf.Repeat(t, 14f) > 11.5f) { p.pointAt = view + Vector3.up * 8f; p.pointW = Mathf.Sin((Mathf.Repeat(t, 14f) - 11.5f) / 2.5f * Mathf.PI); }
                    break;
                }
                case Doing.Wax:
                {
                    // at the stand: leaning over the board, both hands working along it
                    p.mode = Person.Mode.Stand;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    float u = Mathf.Sin(t * 1.4f);
                    Vector3 side = rot * Vector3.right;
                    Vector3 top = waxAt + Vector3.up * 0.83f;
                    p.handAt[1] = top + side * (0.32f * u) - rot * Vector3.forward * 0.05f; p.handW[1] = 1f;
                    p.handAt[0] = top + side * (0.32f * u - 0.22f) + rot * Vector3.forward * 0.02f; p.handW[0] = 0.9f;
                    p.lookAt = top + side * (0.3f * u); p.lookWeight = 0.9f;
                    break;
                }
                case Doing.Strap:
                {
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    float k = Mathf.Clamp01(a.t / a.until);
                    if (p.Look.gear == PersonGear.Skis)
                    {
                        // step into the skis, the poles to hand, a stamp, ready
                        p.mode = Person.Mode.Ski; p.pace = 0f;
                        p.skisOn = true; p.polesOn = true; p.polesInOneHand = false;
                        p.polesDir = (Vector3.down + rot * Vector3.back * 0.25f + rot * Vector3.right * 0.12f).normalized;
                        p.crouch = 0.35f * Mathf.Sin(k * Mathf.PI);
                        p.lookAt = a.pos + rot * new Vector3(0f, 0f, 1.2f); p.lookWeight = 0.7f * (1f - k);
                    }
                    else
                    {
                        // on the board, down to do up the back binding, then up again
                        p.mode = Person.Mode.Ride; p.pace = 0f; p.held = Person.Held.Underfoot;
                        p.RideFrame(a.pos, rot * Vector3.forward, Vector3.up);
                        float down = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 4f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.8f) * 5f)));
                        p.crouch = 0.75f * down;
                        // the back foot's binding: toward the tail (behind the travel)
                        var tail = a.pos - rot * Vector3.forward * 0.25f + Vector3.up * 0.1f;
                        p.handAt[1] = tail; p.handW[1] = down; p.handAt[0] = tail + rot * Vector3.right * 0.1f; p.handW[0] = down * 0.8f;
                        p.lookAt = tail; p.lookWeight = 0.8f * down;
                    }
                    break;
                }
                case Doing.Ride:
                {
                    var v = a.rideVel; v.y = 0f;
                    var up = Vector3.up;
                    if (p.Look.gear == PersonGear.Skis)
                    {
                        p.mode = Person.Mode.Ski; p.pace = Mathf.Clamp01(v.magnitude / 3f); p.skisOn = true; p.polesOn = true; p.polesInOneHand = false;
                        p.polesDir = (Vector3.down + rot * Vector3.back * 0.8f).normalized;
                        p.transform.SetPositionAndRotation(a.pos, Quaternion.LookRotation(v.sqrMagnitude > 0.01f ? v.normalized : Vector3.forward, up));
                    }
                    else
                    {
                        p.mode = Person.Mode.Ride; p.pace = Mathf.Clamp01(v.magnitude / 3f); p.held = Person.Held.Underfoot;
                        p.RideFrame(a.pos, v.sqrMagnitude > 0.01f ? v.normalized : Vector3.forward, up);
                    }
                    break;
                }
                case Doing.Inside:
                    break;
                default:
                    p.mode = Person.Mode.Stand;
                    p.transform.SetPositionAndRotation(a.pos, rot);
                    p.lookAt = lookAhead + rot * new Vector3(Mathf.Sin(t * 0.17f) * 15f, 0f, 0f); p.lookWeight = 0.4f;
                    break;
            }
            p.phoneUp = Mathf.MoveTowards(p.phoneUp, phoneWant, dt * 1.6f);
            // a glance at the hero, or the fall
            if (reactT > 0f && (a.pos - reactAt).sqrMagnitude < 55f * 55f && a.doing != Doing.Ride && a.doing != Doing.Inside)
            {
                p.lookAt = reactAt; p.lookWeight = 1f; p.turnChest = 0.7f;
                if (a == pointer) { p.pointAt = reactAt; p.pointW = Mathf.Clamp01(reactT / 2f); }
                p.phoneUp = 0f;
            }
            else if (a.lookT > 0f) { p.lookAt = a.look; p.lookWeight = Mathf.Clamp01(a.lookT * 2f); p.turnChest = 0.4f; }
        }

        // ---------------------------------------------------------------- the edge giving way

        float reactT; Vector3 reactAt; Agent pointer;

        void OnCollapse(Vector3 at, Vector3 dir)
        {
            reactAt = at + dir * 3f + Vector3.down * 4f;
            reactT = 7f;
            // the nearest two hurry to the rail to look over (well back from the edge), one of them pointing
            pointer = null;
            foreach (var a in agents) a.goer = false;
            var near = new List<Agent>(agents);
            near.RemoveAll(a => !a.own || a.held || a.doing == Doing.Ride || a.doing == Doing.Inside || a.doing == Doing.Strap || !a.p.gameObject.activeSelf);
            near.Sort((x, y) => (x.pos - at).sqrMagnitude.CompareTo((y.pos - at).sqrMagnitude));
            for (int i = 0; i < Mathf.Min(2, near.Count); i++)
            {
                var a = near[i];
                if ((a.pos - at).sqrMagnitude > 30f * 30f) break;
                var to = new Vector3(Mathf.Min(at.x, Cx + 21f) - 1.2f * i, 0f, at.z + (i == 0 ? -1.5f : 1.5f));
                to.y = FloorAt(to);
                if (!Walkable(to)) continue;
                if (a.spot != null && a.spot.holder == a) a.spot.holder = null;
                a.spot = null; a.partner = null;
                WalkTo(a, to, Doing.Stand, 6f);
                a.goer = true;
                if (pointer == null) pointer = a;
            }
        }

        void LateUpdate() { reactT -= Time.deltaTime; }
        /// <summary>For the test tools: time passes for the reactions.</summary>
        public void Tick(float dt) { reactT -= dt; }

        // ---------------------------------------------------------------- the hero among them

        /// <summary>Further apart in height than this, two people are on different floors (the lodge's deck over its café,
        /// its loft over the snug): neither is in the other's way. (A seat is lower; a floor is 3.8 m.)</summary>
        public const float OtherFloor = 1.5f;

        /// <summary>The hero, pushed out of anyone in the way (Camp.PushWalker).</summary>
        Vector2 PushHero(Vector2 p)
        {
            float feet = camp.WalkerPosition.y;
            var from = new Vector2(camp.WalkerPosition.x, camp.WalkerPosition.z);
            foreach (var a in agents)
            {
                if (a.p == null || !a.p.gameObject.activeSelf || a.doing == Doing.Ride || a.doing == Doing.Inside) continue;
                if (Mathf.Abs(a.pos.y - feet) > OtherFloor) continue;   // (on another floor: up on the lodge's deck, down in its café)
                var c = new Vector2(a.pos.x, a.pos.z);
                var d = p - c;
                float r = a.doing == Doing.Sit ? 0.55f : 0.62f, m = d.magnitude;
                if (m >= r) continue;
                var q = c + (m > 1e-4f ? d / m : Vector2.right) * r;
                // walking straight at them (within 25 degrees): round them, not stopped dead against them (pushed
                // straight back, the walk makes no way at all: someone standing in a doorway). Along their edge by
                // the step, to the walker's right, or on round the side they're already passing
                var move = p - from; var rel = from - c;
                float len = move.magnitude, rm = rel.magnitude;
                if (len > 1e-4f && rm > 1e-4f && Vector2.Dot(move / len, -rel / rm) > 0.9f)
                {
                    var right = new Vector2(move.y, -move.x) / len;
                    float ang = (Vector2.Dot(-rel, right) > 1e-3f ? -1f : 1f) * len / r;
                    var u = rel / rm;
                    float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    q = c + new Vector2(u.x * cs - u.y * sn, u.x * sn + u.y * cs) * r;
                }
                p = q;
            }
            return p;
        }

        // ---------------------------------------------------------------- others' people

        /// <summary>A person of yours, walked about by the camp like its own (home: a named point they come back to).</summary>
        public Agent Add(Person p, string home = null)
        {
            var a = agents.Find(x => x.p == p);
            if (a == null) { a = new Agent { p = p, own = false, role = -1 }; agents.Add(a); }
            a.held = false; a.home = home;
            a.pos = p.transform.position; a.yaw = p.transform.eulerAngles.y;
            a.doing = Doing.Idle;
            return a;
        }

        /// <summary>A person you pose yourself: the camp's people step round them and the hero can't walk through them.</summary>
        public Agent Hold(Person p)
        {
            var a = Add(p);
            a.held = true; a.doing = Doing.Held;
            return a;
        }

        /// <summary>The camp lets go of someone of yours.</summary>
        public void Release(Person p) => agents.RemoveAll(a => a.p == p && !a.own);

        /// <summary>Walk someone (yours, held or added) somewhere on the camp's grid; arrived is called when they're there.</summary>
        public void WalkTo(Person p, Vector3 at, System.Action arrived = null)
        {
            var a = agents.Find(x => x.p == p) ?? Add(p);
            a.held = false;
            WalkTo(a, at, Doing.Stand, 9999f);
            var then = a.arrived;
            a.arrived = () => { then(); a.held = true; a.doing = Doing.Held; arrived?.Invoke(); };
        }

        /// <summary>Keep held people's places current (call after moving them yourself, before the camp's Step).</summary>
        public void Moved(Person p) { var a = agents.Find(x => x.p == p); if (a != null) { a.pos = p.transform.position; a.yaw = p.transform.eulerAngles.y; } }

        // ---------------------------------------------------------------- sound

        AudioClip[] stepClips;
        readonly AudioSource[] stepPool = new AudioSource[4];
        int stepNext;
        AudioSource[] murmurs;
        static AudioClip[] babble;

        void Steps(Agent a, float d, float dt)
        {
            if (a.doing != Doing.Walk || d > 12f || !Application.isPlaying) { a.stepPh = a.p.phase; return; }
            float ph = a.p.phase;
            // a heel strikes at a quarter and three quarters of the cycle
            bool strike = (a.stepPh < 0.25f && ph >= 0.25f) || (a.stepPh < 0.75f && ph >= 0.75f) || (ph < a.stepPh && (a.stepPh < 0.75f || ph >= 0.25f));
            a.stepPh = ph;
            if (!strike) return;
            if (stepClips == null) stepClips = Resources.LoadAll<AudioClip>("Audio/Steps");
            if (stepClips.Length == 0) return;
            var src = stepPool[stepNext];
            if (src == null)
            {
                var g = new GameObject("CampStep"); g.transform.SetParent(transform, false);
                src = stepPool[stepNext] = g.AddComponent<AudioSource>();
                src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 1.5f; src.maxDistance = 14f; src.playOnAwake = false;
                src.dopplerLevel = 0f;
            }
            stepNext = (stepNext + 1) % stepPool.Length;
            src.transform.position = a.pos;
            src.pitch = Random.Range(0.92f, 1.08f) * Mathf.Lerp(1.08f, 0.94f, Mathf.InverseLerp(0.9f, 1.07f, a.p.Scale));
            src.PlayOneShot(stepClips[Random.Range(0, stepClips.Length)], 0.28f);
        }

        /// <summary>Two friends talking: a low murmur of voices, heard only close by (no words).</summary>
        void Murmur(Vector3 eye, float dt)
        {
            if (!Application.isPlaying) return;
            if (murmurs == null)
            {
                if (babble == null) babble = new[] { Babble(1), Babble(2) };
                murmurs = new AudioSource[2];
                for (int i = 0; i < 2; i++)
                {
                    var g = new GameObject("CampMurmur"); g.transform.SetParent(transform, false);
                    var s = murmurs[i] = g.AddComponent<AudioSource>();
                    s.clip = babble[i]; s.loop = true; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 2f; s.maxDistance = 16f; s.volume = 0f; s.playOnAwake = false; s.dopplerLevel = 0f;
                    s.Play();
                }
            }
            int k = 0;
            foreach (var a in agents)
            {
                if (k >= murmurs.Length) break;
                if (a.doing != Doing.Chat && !(a.doing == Doing.Sit && a.partner != null)) continue;
                if (a.role == 3) continue;
                var s = murmurs[k++];
                s.transform.position = a.pos + Vector3.up * 1.5f;
                s.volume = Mathf.MoveTowards(s.volume, 0.16f, dt * 0.3f);
            }
            for (; k < murmurs.Length; k++) murmurs[k].volume = Mathf.MoveTowards(murmurs[k].volume, 0f, dt * 0.3f);
        }

        /// <summary>
        /// Voices too far off to make out: a band of noise shaped like speech (syllables at four or five a second in
        /// phrases with pauses between, the pitch of the voice wandering), two voices taking turns.
        /// </summary>
        static AudioClip Babble(int seed)
        {
            const int rate = 22050; float secs = 9f;
            int n = (int)(rate * secs);
            var data = new float[n];
            var rng = new System.Random(seed * 7919);
            float b0 = 0f, b1 = 0f, lp = 0f, env = 0f, ph = 0f;
            float syl = 0f, sylLen = 0.2f, phrase = 0f, phraseLen = 1.6f; bool talking = true; int voice = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                syl += 1f / rate; phrase += 1f / rate;
                if (syl > sylLen) { syl = 0f; sylLen = 0.16f + (float)rng.NextDouble() * 0.12f; }
                if (phrase > phraseLen)
                {
                    phrase = 0f; talking = !talking;
                    phraseLen = talking ? 1.2f + (float)rng.NextDouble() * 1.8f : 0.3f + (float)rng.NextDouble() * 0.6f;
                    if (talking) voice = 1 - voice;
                }
                float wantEnv = talking ? Mathf.Sin(Mathf.PI * syl / sylLen) * (0.6f + 0.4f * Mathf.PerlinNoise(t * 1.3f, seed)) : 0f;
                env += (wantEnv - env) * 0.004f;
                // a voiced buzz (the voice's pitch wandering) through a muffled band: speech heard through snow and distance
                float f0 = (voice == 0 ? 115f : 190f) * (1f + 0.12f * Mathf.Sin(t * 2.1f + seed));
                ph += f0 / rate; if (ph > 1f) ph -= 1f;
                float buzz = (ph < 0.5f ? 1f : -1f) * 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.6f;
                b0 += (buzz - b0) * 0.18f; b1 += (b0 - b1) * 0.18f;    // a soft low-pass
                float band = b0 - lp; lp += (b0 - lp) * 0.02f;          // and the lowest rumble taken out
                data[i] = band * env * 0.5f;
            }
            // fade the loop's ends together
            int fade = rate / 4;
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; data[i] *= k; data[n - 1 - i] *= k; }
            var clip = AudioClip.Create("CampMurmur" + seed, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
