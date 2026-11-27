using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Other people riding the mountain, doing their own thing: skiers and snowboarders in the people kit's outfits,
    /// carving down the run, traversing, stopping in groups at its edge, sitting in the snow, a small hop now and then,
    /// a fall and getting up again; beginners slow and wide near the lifts' bottoms. More of them near the lifts and the
    /// camp, few in the alpine; fewer at dusk, hardly anyone at night or in a storm.
    ///
    /// They ride the stretch of the run round the camera (Behind .. Ahead), on the drawn snow (Slope.DrawnHeight, the
    /// highest of the board's nose, middle and tail, so it never sinks into a bump), inside the groomed run and clear
    /// of everything that stands on it (Clear). A lap ends at a lift's bottom: they stop at the run's edge on its side
    /// and, once out of sight, go up: a new rider starts at a lift's top or further down the run, again out of sight.
    /// Nobody appears or goes while it could be seen (inside the view and DrawRange).
    ///
    /// Drawn: the nearest few as people (Person: the full model within FullRange, the simplified one to SimpleRange),
    /// the rest as baked poses drawn many at once (PeopleLod.Bake, the people's palette rows: PeopleKit.LodMaterial),
    /// mid detail to MidRange and far detail to DrawRange (or as far as the fog lets you see); past that, or out of the
    /// view, not at all. The nearest few drawn are solid (a capsule round the pose, an Obstacle the rider meets as he
    /// meets a tree: square on is a crash, a glancing touch slides by); nothing hidden is. Their tracks (SnowTrail, a
    /// short one each) and spray (SnowFX puffs) only near you. They keep out of your way: a rider in your line moves
    /// off it, and nobody falls, stops or hops in front of you.
    /// </summary>
    public class MountainRiders : MonoBehaviour, IPersonHit
    {
        public static MountainRiders Current { get; private set; }
        public static bool On = true;

        // ---- how many, and how far each is drawn (the iPhone's budget)
        public const int Population = 48;           // riding the stretch round you at most (a fair day; fewer at dusk, night, in weather)
        public const float FullRange = 15f, SimpleRange = 90f, MidRange = 220f, DrawRange = 600f;
        public const int FullCap = 2, NearCap = 8;    // full models; people (full and simplified) in all
        public const float ColliderRange = 45f; public const int ColliderCap = 6;
        public const float TrailRange = 110f; public const int TrailCap = 4, TrailChunks = 6;
        public const float SprayRange = 60f;
        public const float Behind = 250f, Ahead = 1150f;
        const int MaxBatch = 128;

        public enum Act { Ride, Stop, Fall, Wait }

        public class Rider
        {
            public int id, serial;
            public bool active, retire;
            public PersonLook look;
            public bool ski;
            public float skill;                      // 0 a beginner .. 1 an expert
            public Vector3 pos;                      // on the snow (y: the snow under it, plus a hop)
            public float heading, speed, yawRate;    // heading: radians from straight down the hill (+z), + toward +x
            public float board;                      // where the board's nose (or the skis) point (radians, as heading)
            public float lane, laneMid, amp, wave, phase, cruise, radius, maxAngle, avoid, plan, push, stuck, slowFor;
            public Act act; public float t, until;
            public Vector3 spot; public bool sitDown; public float sitW;   // a stop's place, and sitting down there
            public float air, airV, hopIn;           // a hop: height over the snow, its speed up; the next one in
            public float fallIn, stopIn;             // the next fall, the next stop on its own
            public float endZ;                       // its lap ends at a lift's bottom here (or above where the run is blocked)
            public bool endBlocked;
            public float nextLook;
            public int endSide;
            public Group group; public float side;   // in a group: which side of the leader
            public float crouch, carve, skid;
            public Vector3 up = Vector3.up, normal = Vector3.up; public int normalAt;
            // drawn
            public bool drawn, wasDrawn; public float d; public int tier = -1;
            public Person near; public int lod = -1;
            public CapsuleCollider solid;
            public SnowTrail trail;
            public float puff;
            public float ground, groundV;            // the snow's height under it (a bump under the board's ends lifts it), and how fast that changes
            public float snow;                       // the drawn snow right under its middle
            public bool wasNear, three;
            public float bornAt;                     // (when it last started riding: nobody can hit you on their first moments)
            public Ragdoll down; public float downT, downStill;   // knocked over by the rider (ridercrash): thrown, limp
        }
        public class Group { public Rider lead; public readonly List<Rider> members = new List<Rider>(); public float nextStop; }

        readonly Rider[] riders = new Rider[Population];
        public IReadOnlyList<Rider> All => riders;
        global::Summit.Rider player;
        SnowFX fx;
        Shader lit;
        float clock, stepDt; int frame, serial;
        int seed = int.MinValue;
        bool placedAll;
        Vector3 eye;
        readonly Plane[] planes = new Plane[6];
        float range = DrawRange;
        int want = Population;

        // drawn as people: a pool
        readonly List<Person> pool = new List<Person>();
        readonly List<Rider> byDistance = new List<Rider>();
        sealed class ByD : IComparer<Rider> { public static readonly ByD I = new ByD(); public int Compare(Rider a, Rider b) => a.d.CompareTo(b.d); }
        // solid: a pool of capsules
        readonly List<CapsuleCollider> solids = new List<CapsuleCollider>();
        // tracks
        readonly List<SnowTrail> trails = new List<SnowTrail>();
        readonly Dictionary<SnowTrail, Rider> trailOf = new Dictionary<SnowTrail, Rider>();

        // ---- the baked poses: [tier][pose * Heads + head]
        public enum Pose { BoardRide, BoardToe, BoardHeel, BoardStand, BoardSit, SkiRide, SkiStand, SkiSit }
        public const int Poses = 8, Heads = 2, Tiers = 2;
        static readonly Mesh[][] baked = { new Mesh[Poses * Heads], new Mesh[Poses * Heads] };
        static readonly float[] bakeCell = { 0.16f, 0.34f };
        const float BakeHeight = 0.97f;
        readonly List<Matrix4x4>[][] mats = { Lists(), Lists() };
        readonly float[][][] rowArr = { Rows(), Rows() };
        MaterialPropertyBlock[][] mpbs;   // (made in Create: not in a MonoBehaviour's field initialisers)
        static List<Matrix4x4>[] Lists() { var a = new List<Matrix4x4>[Poses * Heads]; for (int i = 0; i < a.Length; i++) a[i] = new List<Matrix4x4>(MaxBatch); return a; }
        static float[][] Rows() { var a = new float[Poses * Heads][]; for (int i = 0; i < a.Length; i++) a[i] = new float[MaxBatch]; return a; }
        static MaterialPropertyBlock[] Blocks() { var a = new MaterialPropertyBlock[Poses * Heads]; for (int i = 0; i < a.Length; i++) a[i] = new MaterialPropertyBlock(); return a; }

        // ---- for the tests and the perf readout
        public static int TestWant = -1;
        /// <summary>What they've done (for the tests and the readout): laps begun, ended at a lift, ended above something
        /// that blocks the run, stops (alone, with friends), sat down, falls, hops, and stops that found no place.</summary>
        public static int Laps, LapsAtLift, LapsBlocked, StopsAlone, StopsGroup, SatDown, Falls, Hops, NoPlace, NoStand, NoWay, WayClear, WayDrop;

        /// <summary>For the tests: nobody on the mountain but one rider, stopped and standing here.</summary>
        public Rider TestOnly(Vector3 at, bool ski)
        {
            foreach (var o in riders) { Release(o); o.active = false; o.group = null; }
            var r = riders[0];
            NewRider(r, at, 0f);
            r.look.gear = ski ? PersonGear.Skis : PersonGear.Snowboard; r.ski = ski;
            StopAt(r, at, 999f, false, true);
            r.bornAt = clock - 10f;   // (stood there a while: solid)
            placedAll = true; seed = Slope.Seed;
            return r;
        }
        /// <summary>For the tests: this one has only just started riding (not solid for its first moments).</summary>
        public void TestFresh(Rider r) { r.bornAt = clock; }
        public int Active { get; private set; }
        public int Shown { get; private set; }
        public int PeopleShown { get; private set; }
        public int Solid { get; private set; }
        public int DrawCalls { get; private set; }
        public long Triangles { get; private set; }
        public float LastMs { get; private set; }
        public string LastParts { get; private set; }
        public float Range => range;
        /// <summary>Riders that came into sight this frame inside the view (and DrawRange): should be none.</summary>
        public System.Action<Rider> Appeared, Went;
        static readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        public static MountainRiders Create(Transform parent, global::Summit.Rider rider, Shader lit)
        {
            var g = new GameObject("MountainRiders");
            if (parent != null) g.transform.SetParent(parent, false);
            var m = g.AddComponent<MountainRiders>();
            m.player = rider; m.lit = lit;
            m.mpbs = new[] { Blocks(), Blocks() };
            Current = m;
            for (int i = 0; i < Population; i++) m.riders[i] = new Rider { id = i };
            Slope.Changing += m.OnNewMountain;
            m.Prewarm();
            return m;
        }

        void OnDestroy() { Slope.Changing -= OnNewMountain; if (Current == this) Current = null; }
        void OnNewMountain() { placedAll = false; }

        /// <summary>The people and the baked poses made now (at load, not when the first rider comes into sight).</summary>
        public void Prewarm()
        {
            if (!On || !PeopleKit.Load()) return;
            Shapes();
            for (int t = 0; t < Tiers; t++) for (int v = 0; v < Poses * Heads; v++) Baked(t, v);
            while (pool.Count < NearCap) { var p = Person.Make(PersonLook.Random(777 + pool.Count), PeopleKit.Root, "MountainRider"); if (p == null) break; p.SetLod(2); p.gameObject.SetActive(false); pool.Add(p); }
            while (trails.Count < TrailCap) trails.Add(new SnowTrail(lit != null ? lit : PeopleKit.Lit) { MaxChunks = TrailChunks });
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Step(Time.deltaTime, cam);
        }

        /// <summary>One frame (public for the tests, which have no frame time).</summary>
        public void Step(float dt, Camera cam)
        {
            if (!On || cam == null || !PeopleKit.Load()) { Hide(); return; }
            sw.Restart();
            clock += dt; frame++; stepDt = dt;
            eye = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            range = Mathf.Min(DrawRange, FogSight(), cam.farClipPlane);
            if (frame % 30 == 1) want = Wanted();
            if (!placedAll || seed != Slope.Seed) { seed = Slope.Seed; PlaceAll(); placedAll = true; }
            // the riders
            Separate();
            int n = Count();
            foreach (var r in riders)
            {
                if (!r.active) { if (n < want && frame % 4 == r.id % 4 && Spawn(r, false)) n = Count(); continue; }
                if (r.down != null) { Down(r, dt); if (!r.active) { n = Count(); continue; } continue; }
                Move(r, dt);
                // a lap over, out of the stretch round you, or one too many: gone, when nobody can see it go
                bool outside = r.pos.z < eye.z - Behind || r.pos.z > eye.z + Ahead + 150f;
                bool done = r.act == Act.Wait && r.t > 2f;
                if ((outside || done || n > want) && !r.drawn && !Seen(r.pos, 8f)) { Recycle(r); n--; }
            }
            Active = n;
            double t1 = sw.Elapsed.TotalMilliseconds;
            Look(cam);
            Draw();
            double t2 = sw.Elapsed.TotalMilliseconds;
            Collide();
            Tracks(dt);
            LastMs = (float)sw.Elapsed.TotalMilliseconds;
            LastParts = $"riding {t1:0.00} ms, drawing {t2 - t1:0.00} ms, solids and tracks {LastMs - t2:0.00} ms";
        }

        // ---------------------------------------------------------------- how many

        /// <summary>How many ride now: a fair day's crowd, fewer at dusk, hardly anyone at night or in a storm.</summary>
        static int Wanted()
        {
            if (TestWant >= 0) return TestWant;
            float k = 1f;
            switch (TimeOfDay.Current)
            {
                case TimeOfDay.Phase.Morning: k = 0.75f; break;
                case TimeOfDay.Phase.Evening: k = 0.55f; break;
                case TimeOfDay.Phase.Night: k = 0.12f; break;
            }
            var w = Weather.Active;
            if (TimeOfDay.Current == TimeOfDay.Phase.Night && w != null && w.Base == Weather.Kind.Storm) return 0;   // (nobody out in a storm at night)
            if (w != null)
                switch (w.Base)
                {
                    case Weather.Kind.Snow: k *= 0.8f; break;
                    case Weather.Kind.Fog: k *= 0.6f; break;
                    case Weather.Kind.Storm: k *= 0.35f; break;
                }
            return Mathf.Clamp(Mathf.RoundToInt(Population * k), 0, Population);
        }

        /// <summary>How far the fog lets you see a person (as good as gone at 2% left).</summary>
        static float FogSight()
        {
            if (!RenderSettings.fog) return float.MaxValue;
            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear: return Mathf.Max(60f, RenderSettings.fogEndDistance);
                case FogMode.Exponential: return RenderSettings.fogDensity > 1e-5f ? Mathf.Max(60f, 3.9f / RenderSettings.fogDensity) : float.MaxValue;
                default: return RenderSettings.fogDensity > 1e-5f ? Mathf.Max(60f, 1.98f / RenderSettings.fogDensity) : float.MaxValue;
            }
        }

        /// <summary>Where riders gather: near the lifts' ends and below the camp, few in the alpine and on the glacier.</summary>
        public static float Weight(float z)
        {
            float w = 1f;
            var s = Course.At(z);
            if (s == Course.Section.Danger || s == Course.Section.Ridge || s == Course.Section.Band || s == Course.Section.Bowl) w *= 0.35f;
            if (Course.InGlacier(z)) w *= 0.4f;
            if (z < 450f) w *= 1.8f;
            foreach (var l in LiftLine.Lifts)
                if (Mathf.Abs(z - l.bottom.z) < 170f || Mathf.Abs(z - l.top.z) < 120f) { w *= 2.2f; break; }
            return w;
        }

        // ---------------------------------------------------------------- the run, and what's on it

        /// <summary>How far either way of the run's centre line a rider can be at z (m, along x), keeping margin from its edge.</summary>
        public static float LaneMax(float z, float margin) => Mathf.Max(1.5f, Slope.HalfAt(z) - margin);

        /// <summary>Somewhere a rider can be: on the groomed run, nothing standing there, no hole, no drop.</summary>
        public static int ClearCalls; public static double ClearMs;
        static readonly System.Diagnostics.Stopwatch clearSw = new System.Diagnostics.Stopwatch();
        public static bool Clear(float x, float z, float margin = 1.5f)
        {
            ClearCalls++; clearSw.Restart();
            bool ok = ClearCore(x, z, margin);
            ClearMs += clearSw.Elapsed.TotalMilliseconds;
            return ok;
        }
        public static readonly int[] Refused = new int[8];   // (why Clear said no: z, edge, kicker, park, crack, step, lights/cabins, things)
        static bool ClearCore(float x, float z, float margin)
        {
            if (z < 8f || z > Slope.FinishZ + 60f) { Refused[0]++; return false; }
            if (Mathf.Abs(x - Slope.Centre(z)) > LaneMax(z, margin)) { Refused[1]++; return false; }
            if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f || Lines.Keep(x, z, 1f)) { Refused[2]++; return false; }   // (and never on a feature line's lane: Lines)
            if (Course.NearFeature(x, z, 2f)) { Refused[3]++; return false; }
            if (Course.CrevasseNear(x, z, 1.5f)) { Refused[4]++; return false; }   // (no crack, nor a bridge over one: a field across the run ends the lap)
            if (Slope.StepNear(z, 8f, out var d) && d.step && Mathf.Abs(Slope.Across(x, z) - d.lane) > 1.8f) { Refused[5]++; return false; }
            if (NightLights.Taken(x, z) || Hamlets.Taken(x, z)) { Refused[6]++; return false; }
            if (ThingNear(x, z, 0.55f)) { Refused[7]++; return false; }
            return true;
        }

        /// <summary>Somewhere to stand still: clear, and well away from any crack (a bridge won't hold you standing).</summary>
        public static bool ClearToStand(float x, float z) => Clear(x, z, 1.6f) && !Course.CrevasseNear(x, z, 3f);

        // ---- what stands on the run or at its edge (the forest's trees and rocks, the lifts' towers and stations, the
        // piste's guns, huts and poles, the trail signs), by bands of z, made as the riders come to them
        struct Thing { public float x, z, r; }
        const float Band = 50f;
        static readonly Dictionary<int, List<Thing>> bands = new Dictionary<int, List<Thing>>();
        static int bandsFor = int.MinValue;

        static List<Thing> BandOf(int b)
        {
            if (bandsFor != Slope.Seed) { bands.Clear(); bandsFor = Slope.Seed; }
            if (bands.TryGetValue(b, out var list)) return list;
            list = new List<Thing>();
            float z0 = b * Band, z1 = z0 + Band, cell = Forest.Cell;
            for (int cz = Mathf.FloorToInt((z0 - cell) / cell); cz <= Mathf.FloorToInt((z1 + cell) / cell); cz++)
            {
                float zc = (cz + 0.5f) * cell, c = Slope.Centre(zc), h = Slope.HalfAt(zc) + 4f;
                for (int cx = Mathf.FloorToInt((c - h - cell) / cell); cx <= Mathf.FloorToInt((c + h + cell) / cell); cx++)
                {
                    var k = Forest.At(cx, cz, out var p, out float size, out _);
                    if (k == Forest.Kind.None || p.z < z0 || p.z >= z1) continue;
                    list.Add(new Thing { x = p.x, z = p.z, r = k == Forest.Kind.Rock ? 0.3f + size : 0.55f + 0.2f * size });
                }
            }
            foreach (var l in LiftLine.Lifts)
            {
                if (l.foot != null) foreach (var f in l.foot) if (f.z >= z0 && f.z < z1) list.Add(new Thing { x = f.x, z = f.z, r = 2.5f });
                foreach (var e in new[] { l.top, l.bottom }) if (e.z >= z0 && e.z < z1) list.Add(new Thing { x = e.x, z = e.z, r = 9f });
            }
            foreach (var it in PisteKit.Items)
                if (it.foot.z >= z0 && it.foot.z < z1) list.Add(new Thing { x = it.foot.x, z = it.foot.z, r = it.kind == PisteKit.Kind.Hut ? 5f : it.kind == PisteKit.Kind.Gun ? 2f : 1f });
            foreach (var sg in TrailSigns.Signs)
            {
                var f = TrailSigns.Foot(sg);
                if (f.z >= z0 && f.z < z1) list.Add(new Thing { x = f.x, z = f.z, r = 1.8f });
            }
            bands[b] = list;
            return list;
        }

        // where the whole run is blocked across (found by riders who met it): a lap ends above it, and none begins just above it
        static readonly List<float> blocked = new List<float>();
        static int blockedFor = int.MinValue;
        static List<float> Blocked() { if (blockedFor != Slope.Seed) { blocked.Clear(); blockedFor = Slope.Seed; } return blocked; }
        static void Block(float z) { var b = Blocked(); foreach (var o in b) if (Mathf.Abs(o - z) < 20f) return; b.Add(z); }
        /// <summary>The first place below z the run is known to be blocked across (or +infinity).</summary>
        static float BlockBelow(float z) { float best = float.PositiveInfinity; foreach (var o in Blocked()) if (o > z && o < best) best = o; return best; }

        static bool ThingNear(float x, float z, float m)
        {
            int b0 = Mathf.FloorToInt((z - 10f) / Band), b1 = Mathf.FloorToInt((z + 10f) / Band);
            for (int b = b0; b <= b1; b++)
                foreach (var t in BandOf(b))
                {
                    float r = t.r + m, dz = t.z - z;
                    if (dz > r || dz < -r) continue;
                    float dx = t.x - x;
                    if (dx * dx + dz * dz < r * r) return true;
                }
            return false;
        }

        // ---------------------------------------------------------------- who they are

        // ---- the shapes they come in (the head, the hair, a pack, the face, a man's or a woman's cut), made at load:
        // a new shape's head and far-off body take milliseconds to make, too long for a frame while you ride. Each
        // rider wears one of these in its own colours (which cost nothing)
        public const int ShapeCount = 24;
        static readonly List<PersonLook> shapes = new List<PersonLook>();
        static void Shapes()
        {
            if (shapes.Count > 0) return;
            var keys = new HashSet<int>();
            for (int seed = 9100; seed < 9100 + 4000 && shapes.Count < ShapeCount; seed += 37)
            {
                var l = PersonLook.Random(seed, 0.55f);
                if (!keys.Add(l.ShapeKey)) continue;
                PeopleKit.Gear(l); PeopleKit.Head(l); PeopleLod.Skinned(l);
                shapes.Add(l);
            }
        }

        /// <summary>A look in one of the shapes made at load (its colours, size and gear its own).</summary>
        static PersonLook LookFor(int s)
        {
            var look = PersonLook.Random(s, 0.55f);
            Shapes();
            if (shapes.Count == 0) return look;
            var t = shapes[Mathf.FloorToInt(Slope.Hash01(s, 9, 91) * shapes.Count) % shapes.Count];
            look.female = t.female; look.head = t.head; look.hasPompom = t.hasPompom; look.hairStyle = t.hairStyle; look.faceShows = t.faceShows;
            if (t.pack == null) look.pack = null; else if (look.pack == null) look.pack = t.pack;
            look.height = t.height * Mathf.Lerp(0.97f, 1.03f, Slope.Hash01(s, 10, 91)); look.build = t.build; look.shoulders = t.shoulders;
            return look;
        }

        void NewRider(Rider r, Vector3 at, float skillBias)
        {
            serial++;
            r.serial = serial; r.bornAt = clock;
            int s = Slope.Seed * 31 + serial * 7919 + 17;
            var look = LookFor(s);
            if (look.gear == PersonGear.None) look.gear = Slope.Hash01(s, 5, 91) < 0.55f ? PersonGear.Skis : PersonGear.Snowboard;
            look.name = (look.gear == PersonGear.Skis ? "Skier" : "Boarder") + serial;
            r.look = look; r.ski = look.gear == PersonGear.Skis;
            float h = Slope.Hash01(s, 1, 91);
            r.skill = Mathf.Clamp01(Mathf.Lerp(h, h * 0.25f, skillBias));
            r.pos = at; r.ground = at.y;
            r.speed = 0f; r.heading = 0f; r.board = r.ski ? 0f : -Mathf.PI / 2f; r.yawRate = 0f;
            r.lane = at.x - Slope.Centre(at.z); r.laneMid = Mathf.Clamp(r.lane * 0.5f, -LaneMax(at.z, 4f), LaneMax(at.z, 4f));
            bool beginner = r.skill < 0.25f;
            r.cruise = beginner ? Mathf.Lerp(3f, 5.5f, r.skill * 4f) : Mathf.Lerp(8f, 15f, (r.skill - 0.25f) / 0.75f);
            r.amp = beginner ? 0.75f : Mathf.Lerp(0.55f, 0.35f, r.skill);
            r.wave = beginner ? Mathf.Lerp(45f, 65f, Slope.Hash01(s, 2, 91)) : Mathf.Lerp(30f, 18f, r.skill) * Mathf.Lerp(0.85f, 1.2f, Slope.Hash01(s, 2, 91));
            r.radius = beginner ? 9f : Mathf.Lerp(8f, 5f, r.skill);
            r.maxAngle = (beginner ? 78f : 62f) * Mathf.Deg2Rad;
            r.phase = Slope.Hash01(s, 3, 91) * Mathf.PI * 2f;
            r.act = Act.Ride; r.t = 0f; r.until = 0f; r.sitW = 0f; r.sitDown = false; r.nextLook = 0f;
            r.air = 0f; r.airV = 0f;
            r.hopIn = Mathf.Lerp(12f, 40f, Slope.Hash01(s, 4, 91));
            r.fallIn = (beginner ? 70f : Mathf.Lerp(150f, 500f, r.skill)) * Mathf.Lerp(0.5f, 1.5f, Slope.Hash01(s, 6, 91));
            r.stopIn = Mathf.Lerp(25f, 80f, Slope.Hash01(s, 7, 91));
            r.avoid = 0f; r.plan = Slope.Hash01(s, 8, 91) * 0.5f; r.stuck = 0f; r.slowFor = 0f;
            r.group = null; r.side = 0f; r.retire = false;
            r.crouch = 0f; r.carve = 0f; r.skid = 0f;
            r.normalAt = -999; r.normal = Vector3.up; r.up = Vector3.up;
            // its lap ends at the next lift's bottom below it (or the finish)
            r.endZ = Slope.FinishZ + 30f; r.endSide = 1;
            foreach (var l in LiftLine.Lifts)
                if (l.bottom.z > at.z + 60f && l.bottom.z < r.endZ) { r.endZ = l.bottom.z; r.endSide = l.offBottom >= 0f ? 1 : -1; }
            float bz = BlockBelow(at.z);
            if (bz - 20f < r.endZ) { r.endZ = bz - 20f + 45f; r.endSide = at.x >= Slope.Centre(at.z) ? 1 : -1; r.endBlocked = true; } else r.endBlocked = false;
            Laps++;
            r.active = true; r.drawn = false; r.wasDrawn = false; r.tier = -1; r.wasNear = false; r.air = 0f; r.airV = 0f;
        }

        /// <summary>Somewhere on the run in the stretch round you: by a lift's top (come up on it), or anywhere, weighted.</summary>
        bool Pick(out Vector3 at, out float skillBias, bool anywhere)
        {
            float lo = Mathf.Max(12f, eye.z - Behind), hi = Mathf.Min(Slope.FinishZ, eye.z + Ahead);
            at = default; skillBias = 0f;
            if (hi <= lo) return false;
            float wMax = 1.8f * 2.2f;
            for (int k = 0; k < 10; k++)
            {
                float z, x;
                float u = Random.value;
                LiftLine.Lift top = null;
                if (u < 0.45f)
                {
                    foreach (var l in LiftLine.Lifts) if (l.top.z > lo && l.top.z < hi && Random.value < 0.6f) { top = l; break; }
                }
                if (top != null)
                {
                    z = top.top.z + Random.Range(4f, 20f);
                    x = Slope.Centre(z) + (top.offTop >= 0f ? 1f : -1f) * LaneMax(z, 3f) * Random.Range(0.6f, 1f);
                }
                else
                {
                    z = Random.Range(lo, hi);
                    if (Random.value * wMax > Weight(z)) continue;
                    x = Slope.Centre(z) + Random.Range(-1f, 1f) * LaneMax(z, 3f);
                }
                if (!ClearToStand(x, z) || !Clear(x, z, 2.5f)) continue;
                if (BlockBelow(z) - z < 150f) continue;   // (just above where the run is blocked: a lap of nothing)
                at = new Vector3(x, Slope.DrawnHeight(x, z), z);
                if (!anywhere && Seen(at, 15f)) continue;
                if (Crowded(at, 5f)) continue;
                // beginners mostly near a lift's bottom
                foreach (var l in LiftLine.Lifts) if (l.bottom.z > z && l.bottom.z - z < 300f) { skillBias = 0.6f; break; }
                return true;
            }
            return false;
        }

        bool Crowded(Vector3 at, float r)
        {
            foreach (var o in riders) if (o.active && (o.pos - at).sqrMagnitude < r * r) return true;
            return false;
        }

        bool Spawn(Rider r, bool anywhere)
        {
            if (!Pick(out var at, out float bias, anywhere)) return false;
            NewRider(r, at, bias);
            // some come in twos, threes and fours: friends who ride together and stop together
            if (Random.value < 0.35f)
            {
                var g = new Group { lead = r, nextStop = clock + Random.Range(20f, 60f) };
                g.members.Add(r); r.group = g;
                int n = Random.Range(1, 4);
                foreach (var o in riders)
                {
                    if (n == 0) break;
                    if (o.active || o == r) continue;
                    float side = (g.members.Count % 2 == 1 ? 1f : -1f) * (1.6f + g.members.Count * 0.9f);
                    float z = at.z - 3f * g.members.Count;
                    float x = Mathf.Clamp(at.x + side, Slope.Centre(z) - LaneMax(z, 2.5f), Slope.Centre(z) + LaneMax(z, 2.5f));
                    var p = new Vector3(x, Slope.DrawnHeight(x, z), z);
                    if (!ClearToStand(x, z) || (!anywhere && Seen(p, 15f)) || Crowded(p, 1.4f)) continue;
                    NewRider(o, p, bias);
                    o.skill = r.skill; o.cruise = r.cruise; o.amp = r.amp; o.wave = r.wave; o.radius = r.radius; o.maxAngle = r.maxAngle; o.endZ = r.endZ; o.endSide = r.endSide;
                    o.group = g; o.side = side; g.members.Add(o);
                    n--;
                }
                if (g.members.Count == 1) { r.group = null; }
            }
            return true;
        }

        void Recycle(Rider r)
        {
            Release(r);
            if (r.group != null)
            {
                var g = r.group;
                g.members.Remove(r);
                if (g.lead == r) g.lead = g.members.Count > 0 ? g.members[0] : null;
                if (g.members.Count <= 1) foreach (var m in g.members) m.group = null;
                r.group = null;
            }
            r.active = false;
        }

        /// <summary>A new mountain: everyone placed afresh along the stretch round you (the world is new: nothing to pop).</summary>
        void PlaceAll()
        {
            foreach (var r in riders) { Release(r); r.active = false; r.group = null; }
            foreach (var t in trails) t.Clear();
            trailOf.Clear();
            want = Wanted();
            for (int i = 0; i < riders.Length && Count() < want; i++) if (!riders[i].active) Spawn(riders[i], true);
            // some of them already stopped at the run's edge, or sitting there
            foreach (var r in riders)
                if (r.active && r.group == null && Random.value < 0.15f) StopAt(r, EdgeSpot(r, r.pos.z + 4f, r.lane >= 0f ? 1 : -1), Random.Range(10f, 40f), !r.ski && Random.value < 0.6f, true);
        }
        int Count() { int n = 0; foreach (var r in riders) if (r.active) n++; return n; }

        // ---------------------------------------------------------------- riding

        void Separate()
        {
            foreach (var r in riders)
            {
                if (!r.active) continue;
                float push = 0f;
                foreach (var o in riders)
                {
                    if (o == r || !o.active) continue;
                    float dz = o.pos.z - r.pos.z, dx = o.pos.x - r.pos.x;
                    if (dz < -2f || dz > 9f || Mathf.Abs(dx) > 4f) continue;
                    if (r.group != null && o.group == r.group && r.act != Act.Ride) continue;
                    push -= Mathf.Sign(dx == 0f ? (r.id - o.id) : dx) * (4f - Mathf.Abs(dx)) * (dz > 0f ? 1f : 0.4f);
                }
                r.push = Mathf.Clamp(push, -5f, 5f);
            }
        }

        /// <summary>The player bearing down on this one: which way to move off his line (0: he isn't).</summary>
        float Yield(Rider r)
        {
            if (player == null) return 0f;
            var v = player.Velocity; v.y = 0f;
            float sp = v.magnitude;
            if (sp < 3f) return 0f;
            Vector3 dir = v / sp, rel = r.pos - player.transform.position; rel.y = 0f;
            float ahead = Vector3.Dot(rel, dir);
            if (ahead < -2f || ahead > Mathf.Max(30f, sp * 2.5f)) return 0f;
            float lat = Vector3.Dot(rel, new Vector3(dir.z, 0f, -dir.x));
            if (Mathf.Abs(lat) > 6f) return 0f;
            // off the line on the side it's on (or the side with more room)
            float s = Mathf.Abs(lat) > 0.3f ? Mathf.Sign(lat) * Mathf.Sign(dir.z >= 0f ? 1f : -1f) : (r.lane > 0f ? -1f : 1f);
            return s * (6f - Mathf.Abs(lat)) / 6f;
        }

        bool PlayerNear(Rider r, float m)
        {
            if (player == null) return false;
            var d = r.pos - player.transform.position;
            return d.sqrMagnitude < m * m;
        }

        void Move(Rider r, float dt)
        {
            r.t += dt;
            if (dt <= 0f) { Ground(r); return; }
            switch (r.act)
            {
                case Act.Ride: Ride(r, dt); break;
                case Act.Stop: case Act.Wait: Stopping(r, dt); break;
                case Act.Fall: Falling(r, dt); break;
            }
            Ground(r);
        }

        void Ride(Rider r, float dt)
        {
            float z = r.pos.z;
            float half = LaneMax(z, 2f);
            r.phase += r.speed * dt / Mathf.Max(5f, r.wave) * Mathf.PI * 2f;
            float amp = r.avoid > 0f ? 0.12f : r.amp;
            float laneWant = r.laneMid + amp * half * Mathf.Sin(r.phase);
            // with friends: behind the leader, to one side
            float cruise = r.cruise;
            var g = r.group;
            if (g != null && g.lead != null && g.lead != r && g.lead.active && g.lead.act == Act.Ride)
            {
                laneWant = g.lead.lane + r.side;
                float gap = g.lead.pos.z - r.pos.z;
                cruise = Mathf.Clamp(g.lead.speed + (gap - 6f) * 0.4f, 0f, g.lead.cruise * 1.3f);
            }
            float y = Yield(r);
            if (y != 0f) laneWant = r.lane + y * 8f;
            laneWant = Mathf.Clamp(laneWant + r.push, -half, half);
            // toward the lane a little way ahead
            float look = Mathf.Max(5f, r.speed * 0.8f);
            float zA = z + look;
            float want = Mathf.Atan2(Slope.Centre(zA) + laneWant - r.pos.x, look);
            want = Mathf.Clamp(want, -r.maxAngle, r.maxAngle);
            float yawMax = r.speed < 0.5f ? 0f : Mathf.Max(0.6f, r.speed / r.radius);   // (standing still, it doesn't spin round)
            float dh = Mathf.Clamp(Mathf.DeltaAngle(r.heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad, -yawMax * dt, yawMax * dt);
            r.heading += dh; r.yawRate = dh / dt;
            if (r.speed >= 0.5f) r.board = Mathf.LerpAngle(r.board * Mathf.Rad2Deg, r.heading * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 8f)) * Mathf.Deg2Rad;
            float sp = cruise * Mathf.Lerp(0.5f, 1f, Mathf.Cos(r.heading));
            r.slowFor -= dt;
            if (r.slowFor > 0f) sp = Mathf.Min(sp, 6f);
            r.speed = Mathf.MoveTowards(r.speed, sp, (r.speed < sp ? 2.2f : r.slowFor > 0f ? 6f : 3.5f) * dt);
            if (r.speed < 0.5f && sp > 1f && r.stuck <= 0f) { r.speed = 0.6f; r.heading = Mathf.Clamp(want, -0.6f, 0.6f); }
            Advance(r, dt);
            r.carve = Mathf.Clamp(r.yawRate * r.speed / 6f, -1f, 1f);
            r.skid = Mathf.Clamp01(Mathf.Abs(r.carve) * 0.35f * (1f - r.skill));
            r.crouch = Mathf.MoveTowards(r.crouch, r.air > 0f ? 0f : r.hopIn < 0.35f ? 0.7f : 0.15f + 0.3f * Mathf.Abs(r.carve), dt * 4f);
            // looking ahead for what's in the way
            r.avoid -= dt; r.plan -= dt;
            if (r.plan <= 0f) { r.plan = r.drawn && r.d < MidRange ? 0.5f : 1.1f; Plan(r); }
            Events(r, dt);
        }

        /// <summary>Does the snow fall away between these two points more than a ridden pitch does (off a step, a cliff's edge)?</summary>
        public static bool Drop(float x0, float z0, float x1, float z1)
        {
            float d = new Vector2(x1 - x0, z1 - z0).magnitude;
            return Slope.Height(x1, z1) < Slope.Height(x0, z0) - d * 1.0f - 0.5f;   // (past 45 degrees: a step's face, a cliff's edge)
        }

        /// <summary>Can a rider be here, going at this pace (the cheap checks, every step: Clear's are every few)?</summary>
        static bool Step(float x, float z, float speed)
        {
            if (ThingNear(x, z, 0.45f)) return false;
            if (Course.CrevasseNear(x, z, 0.8f)) return false;
            if (NightLights.Taken(x, z) || Hamlets.Taken(x, z)) return false;
            return true;
        }

        void Advance(Rider r, float dt)
        {
            float go = r.speed * dt;
            // (followed closely, the drawn snow ahead: a cut in it, a wall's face, the smooth hill doesn't have)
            bool close = r.wasNear;
            bool Ok(float h)
            {
                float x = r.pos.x + Mathf.Sin(h) * go, z = r.pos.z + Mathf.Cos(h) * go;
                { float c0 = Slope.Centre(z), m0 = LaneMax(z, 1.2f); x = Mathf.Clamp(x, c0 - m0, c0 + m0); }   // (where it really goes: kept on the run)
                if (!Step(x, z, r.speed)) return false;
                float ax = x + Mathf.Sin(h) * 3f, az = z + Mathf.Cos(h) * 3f;
                if (close) return Slope.DrawnHeight(ax, az) >= r.ground - (3f + go) * 1.0f - 0.5f && (r.air > 0f || Slope.DrawnHeight(x, z) >= r.ground - go * 1.4f - 0.15f);
                return !Drop(r.pos.x, r.pos.z, ax, az);
            }
            if (go > 1e-4f && !Ok(r.heading))
            {
                // something just ahead the plan didn't see: the nearest way round it (else a stop, short of it)
                bool found = false;
                foreach (float turn in new[] { 0.35f, -0.35f, 0.7f, -0.7f, 1.1f, -1.1f })
                {
                    float h = r.heading + turn;
                    if (Mathf.Abs(h) > 1.45f || !Ok(h)) continue;
                    r.heading = h; found = true; break;
                }
                if (!found) { r.speed = 0f; r.avoid = 1f; r.stuck += dt; return; }
                r.laneMid = r.lane + Mathf.Sin(r.heading) * 4f; r.avoid = Mathf.Max(r.avoid, 1.5f);
            }
            r.pos.x += Mathf.Sin(r.heading) * r.speed * dt;
            r.pos.z += Mathf.Cos(r.heading) * r.speed * dt;
            // never off the groomed run
            float c = Slope.Centre(r.pos.z), m = LaneMax(r.pos.z, 1.2f);
            r.pos.x = Mathf.Clamp(r.pos.x, c - m, c + m);
            r.lane = r.pos.x - c;

        }

        /// <summary>What's ahead in its line (every 3 m of the weave it will ride): if something is, the nearest clear line.</summary>
        void Plan(Rider r)
        {
            // a roll ahead (the hill steepening sharply): slowed for, as a skier would (or it's air)
            {
                float hx = Mathf.Sin(r.heading), hz = Mathf.Cos(r.heading);
                float h0 = Slope.Height(r.pos.x, r.pos.z), h1 = Slope.Height(r.pos.x + hx * 6f, r.pos.z + hz * 6f), h2 = Slope.Height(r.pos.x + hx * 12f, r.pos.z + hz * 12f);
                if ((h1 - h2) / 6f - (h0 - h1) / 6f > 0.22f) r.slowFor = 2.5f;
            }
            float half = LaneMax(r.pos.z, 2f);
            float look = Mathf.Max(6f, r.speed * 0.9f), reach = look * 3f;
            float amp = r.avoid > 0f ? 0.12f : r.amp;
            bool blocked = false;
            float px = r.pos.x, pz = r.pos.z;
            for (float s = 4f; s <= reach && !blocked; s += 4f)
            {
                float zA = r.pos.z + s;
                float lane = Mathf.Lerp(r.lane, r.laneMid + amp * half * Mathf.Sin(r.phase + s / Mathf.Max(5f, r.wave) * Mathf.PI * 2f), Mathf.Clamp01(s / look));
                float xA = Slope.Centre(zA) + lane;
                if (!Clear(xA, zA, 1.5f) || Drop(px, pz, xA, zA)) blocked = true;
                px = xA; pz = zA;
            }
            if (!blocked) return;
            float zB = r.pos.z + look, zC = r.pos.z + look * 2f;
            int n = Mathf.Max(2, Mathf.CeilToInt(half / 1.5f));
            float best = float.MaxValue, bestLane = r.laneMid;
            for (int i = -n; i <= n; i++)
            {
                float lane = i / (float)n * half;
                float cost = Mathf.Abs(lane - r.lane);
                if (cost >= best) continue;
                float xB = Slope.Centre(zB) + lane, xC = Slope.Centre(zC) + lane;
                if (!Clear(xB, zB, 1.5f) || !Clear(xC, zC, 1.5f) || Drop(r.pos.x, r.pos.z, xB, zB) || Drop(xB, zB, xC, zC)) continue;
                best = cost; bestLane = lane;
            }
            if (best == float.MaxValue)
            {
                // nowhere clear: the run is blocked across ahead (a step's face, a drop). It's remembered, and this lap
                // ends here: over to the edge, to wait (and go, once out of sight)
                Block(zB);
                var spot = EdgeSpot(r, r.pos.z + 3f, r.lane >= 0f ? 1 : -1);
                if (float.IsNaN(spot.x)) spot = EdgeSpot(r, r.pos.z + 3f, r.lane >= 0f ? -1 : 1);
                if (float.IsNaN(spot.x)) spot = new Vector3(r.pos.x, 0f, r.pos.z);
                StopAt(r, spot, 999f, false, false);
                r.act = Act.Wait; LapsBlocked++;
                return;
            }
            r.stuck = 0f;
            r.laneMid = bestLane; r.avoid = 2.5f;
        }

        /// <summary>Is the way from here to there clear (every 3 m)?</summary>
        static bool Way(Vector3 a, Vector3 b)
        {
            float d = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(d / 3f));
            float px = a.x, pz = a.z;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, x = Mathf.Lerp(a.x, b.x, t), z = Mathf.Lerp(a.z, b.z, t);
                if (!Clear(x, z, 1.2f)) { WayClear++; return false; }
                if (Drop(px, pz, x, z)) { WayDrop++; return false; }
                px = x; pz = z;
            }
            return true;
        }

        void Events(Rider r, float dt)
        {
            bool youNear = PlayerNear(r, 50f);
            if (r.stuck > 3f) { r.act = Act.Wait; r.spot = r.pos; r.speed = 0f; r.t = 0f; r.stuck = 0f; Settle(r); return; }
            // the lap's end: off to the run's edge on the lift's side, to wait for it
            if (r.pos.z > r.endZ - 45f && r.t >= r.nextLook)
            {
                r.nextLook = r.t + 0.5f;
                var spot = EdgeSpot(r, Mathf.Min(r.endZ - 8f, r.pos.z + 25f), r.endSide);
                if (float.IsNaN(spot.x)) spot = EdgeSpot(r, r.pos.z + 12f, -r.endSide);
                StopAt(r, spot, 999f, false, false);
                if (r.act == Act.Stop) { r.act = Act.Wait; if (r.endBlocked) LapsBlocked++; else LapsAtLift++; return; }
                if (r.pos.z > r.endZ + 20f)
                {
                    // nowhere to wait for that lift: on to the next one down
                    float next = Slope.FinishZ + 30f;
                    foreach (var l in LiftLine.Lifts) if (l.bottom.z > r.pos.z + 60f && l.bottom.z < next) { next = l.bottom.z; r.endSide = l.offBottom >= 0f ? 1 : -1; }
                    r.endZ = Mathf.Min(next, BlockBelow(r.pos.z) - 20f + 45f);
                }
                return;
            }
            // a hop on a smooth straight stretch
            r.hopIn -= dt;
            if (r.hopIn <= 0f)
            {
                r.hopIn = Random.Range(12f, 40f);
                if (!youNear && r.skill > 0.3f && r.speed > 6f && Mathf.Abs(r.yawRate) < 0.8f && r.air <= 0f && r.avoid <= 0f) { r.airV = r.groundV + Random.Range(2.2f, 3.4f); r.air = 0.001f; Hops++; }
            }
            // a fall
            r.fallIn -= dt;
            if (r.fallIn <= 0f)
            {
                r.fallIn = (r.skill < 0.25f ? 70f : Mathf.Lerp(150f, 500f, r.skill)) * Random.Range(0.5f, 1.5f);
                if (!youNear && r.air <= 0f && r.group == null) { r.act = Act.Fall; r.t = 0f; r.until = Random.Range(2f, 4.5f); Falls++; return; }
            }
            // a stop at the edge, with friends or alone (sitting in the snow, for some of the boarders)
            var g = r.group;
            if (g != null && g.lead == r)
            {
                if (clock > g.nextStop && !youNear)
                {
                    g.nextStop = clock + Random.Range(45f, 110f);
                    int side = r.lane >= 0f ? 1 : -1;
                    var spot = EdgeSpot(r, r.pos.z + Mathf.Max(18f, r.speed * 2.5f), side);
                    float stay = Random.Range(15f, 40f);
                    if (!float.IsNaN(spot.x)) StopsGroup++; else { NoPlace++; g.nextStop = clock + 4f; }
                    for (int i = 0; i < g.members.Count; i++)
                    {
                        var m = g.members[i];
                        if (m.act != Act.Ride) continue;
                        float z = spot.z - 2.2f * i;
                        if (float.IsNaN(spot.x)) break;
                        float x = Mathf.Clamp(spot.x - side * 1.3f * (i % 2), Slope.Centre(z) - LaneMax(z, 1.6f), Slope.Centre(z) + LaneMax(z, 1.6f));
                        if (!ClearToStand(x, z) || !Way(m.pos, new Vector3(x, 0f, z))) continue;
                        StopAt(m, new Vector3(x, 0f, z), stay, !m.ski && Random.value < 0.5f, false);
                    }
                }
            }
            else if (g == null)
            {
                r.stopIn -= dt;
                if (r.stopIn <= 0f)
                {
                    r.stopIn = Random.Range(35f, 110f);
                    if (!youNear) { StopAt(r, EdgeSpot(r, r.pos.z + Mathf.Max(15f, r.speed * 2.5f), r.lane >= 0f ? 1 : -1), Random.Range(8f, 30f), !r.ski && Random.value < 0.6f, false); if (r.act == Act.Stop) StopsAlone++; else r.stopIn = 4f; }
                }
            }
        }

        /// <summary>A place to stop at the run's edge on one side, at about z, clear of everything (or its own lane).</summary>
        Vector3 EdgeSpot(Rider r, float z, int side)
        {
            for (int k = 0; k < 8; k++)
            {
                float zz = z + k * 4f;
                foreach (float f in new[] { 1f, 0.6f, 0.3f })
                {
                    float x = Slope.Centre(zz) + side * LaneMax(zz, 2.2f) * f;
                    var at = new Vector3(x, 0f, zz);
                    if (!ClearToStand(x, zz)) { NoStand++; continue; }
                    if (!Way(r.pos, at)) { NoWay++; continue; }
                    return at;
                }
            }
            return new Vector3(float.NaN, 0f, z);
        }

        void StopAt(Rider r, Vector3 spot, float stay, bool sit, bool already)
        {
            if (float.IsNaN(spot.x)) { NoPlace++; return; }   // (nowhere to stop just here: on it goes)
            if (sit) SatDown++;
            r.act = Act.Stop; r.t = 0f; r.until = stay; r.spot = spot; r.sitDown = sit;
            if (already)
            {
                r.pos = new Vector3(spot.x, r.pos.y, spot.z); r.lane = spot.x - Slope.Centre(spot.z);
                r.speed = 0f; Settle(r); r.sitW = sit ? 1f : 0f;
            }
        }

        /// <summary>Turned across the hill, standing (a boarder facing down it, on the heel edge; a skier side-on).</summary>
        void Settle(Rider r)
        {
            r.heading = 0f;
            r.board = r.ski ? (r.lane >= 0f ? Mathf.PI / 2f : -Mathf.PI / 2f) : -Mathf.PI / 2f;
        }

        void Stopping(Rider r, float dt)
        {
            Vector3 to = r.spot - r.pos; to.y = 0f;
            float dist = to.magnitude;
            if (r.speed > 0.05f || dist > 1.2f)
            {
                // to the spot, slowing down; the last few metres a skid, the board (or skis) turned across
                float want = Mathf.Atan2(to.x, Mathf.Max(0.5f, to.z));
                want = Mathf.Clamp(want, -1.35f, 1.35f);
                float dh = Mathf.Clamp(Mathf.DeltaAngle(r.heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad, -1.5f * dt, 1.5f * dt);
                r.heading += dh; r.yawRate = dh / dt;
                float sp = dist > 12f ? Mathf.Min(r.cruise, 8f) : Mathf.Lerp(0f, 5f, dist / 12f);
                if (to.z < -0.5f) sp = 0f;
                r.speed = Mathf.MoveTowards(r.speed, sp, (dist < 8f ? 4.5f : 3f) * dt);
                Advance(r, dt);
                float across = r.ski ? Mathf.Sign(r.lane == 0f ? 1f : r.lane) * Mathf.PI / 2f : -Mathf.PI / 2f;
                float k = Mathf.Clamp01(1f - dist / 6f);
                r.board = Mathf.LerpAngle(r.heading * Mathf.Rad2Deg, across * Mathf.Rad2Deg, k) * Mathf.Deg2Rad;
                r.skid = k * Mathf.Clamp01(r.speed / 3f);
                r.carve = k > 0f ? -0.6f * k : Mathf.Clamp(r.yawRate * r.speed / 6f, -1f, 1f);
                r.crouch = Mathf.MoveTowards(r.crouch, 0.2f + 0.4f * r.skid, dt * 3f);
                if (dist < 1.2f && r.speed < 0.6f) { r.speed = 0f; Settle(r); r.t = 0f; }
                return;
            }
            r.speed = 0f; r.skid = 0f; r.carve = 0f;
            r.crouch = Mathf.MoveTowards(r.crouch, 0f, dt * 2f);
            // sitting down, and up again before going on
            bool leaving = r.act == Act.Stop && r.t > r.until;
            r.sitW = Mathf.MoveTowards(r.sitW, r.sitDown && !leaving ? 1f : 0f, dt * 0.9f);
            if (r.act == Act.Wait) return;
            var g = r.group;
            bool friendsWaiting = g != null && g.lead != r && g.lead != null && g.lead.active && g.lead.act == Act.Stop;
            if (leaving && r.sitW <= 0f && !friendsWaiting && !PlayerNear(r, 25f)) { r.act = Act.Ride; r.t = 0f; r.heading = 0f; r.board = 0f; r.laneMid = r.lane * 0.6f; }
        }

        void Falling(Rider r, float dt)
        {
            // down: a slide to a stop, sat in the snow; a moment; up again
            r.speed = Mathf.MoveTowards(r.speed, 0f, 7f * dt);
            Advance(r, dt);
            r.skid = r.speed > 0.5f ? 1f : 0f;
            if (r.t < r.until) r.sitW = Mathf.MoveTowards(r.sitW, 1f, dt * 4f);
            else r.sitW = Mathf.MoveTowards(r.sitW, 0f, dt * 0.8f);
            if (r.t > 0.4f && r.t < 0.5f) r.board = r.ski ? r.heading + Mathf.PI / 2f : -Mathf.PI / 2f;
            if (r.t > r.until && r.sitW <= 0f) { r.act = Act.Ride; r.t = 0f; r.heading = 0f; r.board = 0f; r.speed = 0f; }
        }

        /// <summary>The snow under it: the highest of the board's nose, middle and tail (it never sinks into a bump).</summary>
        void Ground(Rider r, bool now = false)
        {
            // (on the snow a rider presses down over a roll as hard as 3 g before it leaves it)
            bool near = r.drawn || r.trail != null || r.solid != null || r.air > 0f;
            if (!near) r.wasNear = false;
            if (!near && !now && (frame + r.id) % 8 != 0) return;
            float x = r.pos.x, z = r.pos.z;
            float g = Slope.DrawnHeight(x, z);
            bool three = near && r.near != null;   // (the board's nose and tail too, for the people drawn near)
            if (three != r.three) { r.three = three; r.wasNear = false; }
            r.snow = g;
            if (three)
            {
                // the board lies along the slope (its up is the snow's): only a bump under its nose or tail lifts it
                float bx = Mathf.Sin(r.board) * 0.75f, bz = Mathf.Cos(r.board) * 0.75f;
                var n = r.normal; float ny = Mathf.Max(0.3f, n.y), along = (n.x * bx + n.z * bz) / ny;
                float lift = Mathf.Max(0f, Mathf.Max(Slope.DrawnHeight(x + bx, z + bz) - (g - along), Slope.DrawnHeight(x - bx, z - bz) - (g + along)));
                g += Mathf.Min(lift, 0.3f);
            }
            // in the air off a hop, or where the snow drops away faster than you fall (a roll, a step): flown, not
            // slid down it
            float dt = stepDt;
            // (not followed closely until now: its last snow is stale, so start it on this)
            bool fresh = !r.wasNear;
            r.wasNear = near && !now && dt > 0f;
            if (!near || now || dt <= 0f || fresh)
            {
                // (on the snow and moving with it: its rate is the hill's along the way it rides, not nothing. Started at
                // rest when drawn as a person near, on the mountain's 36 degrees the snow fell away from under it at 9 m/s
                // and it flew off the open hill, 2 to 3 m up: PeopleTest.Riders)
                float rate = 0f;
                if (near)
                {
                    float hx = Mathf.Sin(r.heading) * 0.75f, hz = Mathf.Cos(r.heading) * 0.75f;
                    rate = (Slope.DrawnHeight(x + hx, z + hz) - Slope.DrawnHeight(x - hx, z - hz)) / 1.5f * r.speed;
                }
                r.ground = g; r.pos.y = g; r.air = 0f; r.airV = rate; r.groundV = rate; return;
            }
            // (on the snow the body goes up and down with it: airV is the snow's own rate then)
            float pull = r.air > 0f ? 4.9f : 14.7f;
            float body = r.pos.y + r.airV * dt - pull * dt * dt;
            float lim = r.speed * 1.6f + 1f;   // (the snow's own rise and fall under it, no faster than the hill allows)
            // (smoothed: the drawn snow is flat pieces 1.5 m across, and every kink between them isn't a jump)
            float gv = Mathf.Lerp(r.groundV, Mathf.Clamp((g - r.ground) / dt, -lim, lim), 1f - Mathf.Exp(-dt / 0.15f));
            // (only those drawn as people near take off from the snow itself: far off a roll is ridden, a hop flown)
            bool flies = r.near != null || r.solid != null || r.air > 0f;
            if (!flies) body = Mathf.Min(body, g);
            if (body > g + 0.03f)
            {
                if (r.air <= 0f) body = r.pos.y + r.airV * dt - 4.9f * dt * dt;
                r.airV -= (r.air > 1.2f ? 25f : 9.81f) * dt;   // (more than a small air: brought down; a big one is a drop it shouldn't have met)
                r.pos.y = body; r.air = body - g;
            }
            else
            {
                if (r.air > 0.15f) r.crouch = 0.6f;   // (landed)
                r.pos.y = g; r.air = 0f; r.airV = gv;
            }
            r.ground = g; r.groundV = gv;
            if (near && frame - r.normalAt > 3) { r.normal = Slope.Normal(x, z); r.normalAt = frame; }
            // leaning into the turn
            var right = new Vector3(Mathf.Cos(r.heading), 0f, -Mathf.Sin(r.heading));
            float lean = Mathf.Clamp(r.speed * r.yawRate / 9.81f * 0.5f, -0.3f, 0.3f);
            var want = (r.normal + right * lean).normalized;
            r.up = Vector3.Slerp(r.up, want, 0.25f);
        }

        // ---------------------------------------------------------------- seen

        /// <summary>Could this place be seen: in the view (with a margin) and near enough to be drawn.</summary>
        public bool Seen(Vector3 at, float margin)
        {
            if ((at - eye).sqrMagnitude > (range + margin) * (range + margin)) return false;
            var b = new Bounds(at + Vector3.up * 0.9f, Vector3.one * (2f + margin * 2f));
            return GeometryUtility.TestPlanesAABB(planes, b);
        }

        void Look(Camera cam)
        {
            Shown = 0;
            foreach (var r in riders)
            {
                r.wasDrawn = r.drawn;
                if (!r.active) { r.drawn = false; continue; }
                r.d = (r.pos - eye).magnitude;
                r.drawn = r.d < range && GeometryUtility.TestPlanesAABB(planes, new Bounds(r.pos + r.up * 0.9f, new Vector3(2f, 2f, 2f)));
                if (r.drawn && !r.wasDrawn) Ground(r, true);
                if (r.drawn) Shown++;
                if (r.drawn && !r.wasDrawn) Appeared?.Invoke(r);
                if (!r.drawn && r.wasDrawn) Went?.Invoke(r);
            }
        }

        // ---------------------------------------------------------------- drawn

        static Quaternion Rot(Rider r) => RotFor(r);

        int PoseOf(Rider r)
        {
            bool down = r.sitW > 0.5f;
            if (r.ski) return (int)(down ? Pose.SkiSit : r.speed < 1.5f ? Pose.SkiStand : Pose.SkiRide);
            if (down) return (int)Pose.BoardSit;
            if (r.speed < 1.5f) return (int)Pose.BoardStand;
            return (int)(r.carve > 0.35f ? Pose.BoardToe : r.carve < -0.35f ? Pose.BoardHeel : Pose.BoardRide);
        }

        void Draw()
        {
            // the nearest drawn, as people
            byDistance.Clear();
            foreach (var r in riders) if (r.drawn && r.d < SimpleRange && r.down == null) byDistance.Add(r);
            byDistance.Sort(ByD.I);
            if (byDistance.Count > NearCap) byDistance.RemoveRange(NearCap, byDistance.Count - NearCap);
            foreach (var r in riders) if (r.near != null && r.down == null && !byDistance.Contains(r)) ReleasePerson(r);
            foreach (var r in riders) if (r.down != null) PeopleShown++;   // (thrown: their body is the ragdoll's, posed by nobody)
            PeopleShown = 0; DrawCalls = 0; Triangles = 0;
            PeopleLod.BuildPending();
            for (int i = 0; i < byDistance.Count; i++)
            {
                var r = byDistance[i];
                if (r.near == null && !TakePerson(r)) continue;
                int lod = i < FullCap && r.d < FullRange ? 0 : 1;
                r.near.SetLod(lod);
                if (lod != r.lod) { r.near.Shadows(lod == 0); r.lod = lod; }
                if (lod == 0 || (frame + r.id) % 2 == 0) PosePerson(r, lod == 0 ? Time.deltaTime : 2f * Time.deltaTime);
                else r.near.transform.SetPositionAndRotation(r.pos, Rot(r));
                PeopleShown++;
                DrawCalls += lod == 0 ? 5 : 2; Triangles += lod == 0 ? 15000 : 3000;
            }
            // the rest, baked, many at once
            for (int t = 0; t < Tiers; t++) foreach (var l in mats[t]) l.Clear();
            foreach (var r in riders)
            {
                if (!r.drawn || r.near != null || r.down != null) { r.tier = r.near != null ? -2 : -1; continue; }
                int tier = r.d < MidRange ? 0 : 1;
                int v = PoseOf(r) * Heads + (r.look.head == PersonHead.Beanie ? 1 : 0);
                var list = mats[tier][v];
                if (list.Count >= MaxBatch || Baked(tier, v) == null) { r.tier = -1; continue; }
                r.tier = tier;
                rowArr[tier][v][list.Count] = PeopleKit.Row(r.look);
                float k = Mathf.Clamp(r.look.height, 0.9f, 1.1f) / BakeHeight;
                list.Add(Matrix4x4.TRS(r.pos, Rot(r), new Vector3(k * Mathf.Clamp(r.look.build, 0.95f, 1.06f), k, k)));
            }
            PeopleKit.Flush();
            var mat = PeopleKit.LodMaterial;
            for (int t = 0; t < Tiers; t++)
                for (int v = 0; v < Poses * Heads; v++)
                {
                    var list = mats[t][v];
                    if (list.Count == 0) continue;
                    var mesh = baked[t][v];
                    var mpb = mpbs[t][v];
                    mpb.SetFloatArray("_Row", rowArr[t][v]);
                    var rp = new RenderParams(mat) { matProps = mpb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = Bounds(list) };
                    Graphics.RenderMeshInstanced(rp, mesh, 0, list);
                    DrawCalls++;
                    Triangles += (long)(mesh.GetIndexCount(0) / 3) * list.Count;
                }
        }

        static Bounds Bounds(List<Matrix4x4> list)
        {
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            foreach (var m in list) { Vector3 p = m.GetColumn(3); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            var b = new Bounds(); b.SetMinMax(lo - new Vector3(1.5f, 0.5f, 1.5f), hi + new Vector3(1.5f, 2.2f, 1.5f));
            return b;
        }

        bool TakePerson(Rider r)
        {
            Person p = null;
            foreach (var q in pool) if (!q.gameObject.activeSelf) { p = q; break; }
            if (p == null)
            {
                if (pool.Count >= NearCap) return false;
                p = Person.Make(r.look, PeopleKit.Root, "MountainRider");
                if (p == null) return false;
                pool.Add(p);
            }
            else { p.gameObject.SetActive(true); if (p.Look != r.look) p.Dress(r.look); }
            p.Shelter = 0f;
            r.near = p; r.lod = -1;
            PosePerson(r, 0f);
            return true;
        }

        void ReleasePerson(Rider r)
        {
            if (r.near == null) return;
            r.near.SetLod(2);
            r.near.gameObject.SetActive(false);
            r.near = null; r.lod = -1;
        }

        void Release(Rider r)
        {
            Up(r);
            ReleasePerson(r);
            if (r.solid != null) { r.solid.gameObject.SetActive(false); r.solid = null; }
            if (r.trail != null) { trailOf.Remove(r.trail); r.trail.Feed(r.pos, Vector3.up, 0f, false, 1f, 0f, 0f, 0f); r.trail = null; }
            r.drawn = false; r.tier = -1;
        }

        static void PosePerson(Rider r, float dt) => PoseFor(r, dt);

        void Hide()
        {
            foreach (var r in riders) if (r != null) Release(r);
        }

        // ---------------------------------------------------------------- knocked over (ridercrash)

        /// <summary>Riders the hero has knocked over (for the tests), and those back up again or gone unseen.</summary>
        public static int Knocked, GotUp, GoneDown;
        readonly Dictionary<Person, Ragdoll> rags = new Dictionary<Person, Ragdoll>();
        /// <summary>A knocked rider's grunt, for the game to play (where).</summary>
        public System.Action<Vector3, bool> Grunted;

        Rider Of(Collider c) { if (c == null) return null; foreach (var r in riders) if (r.solid == c) return r; return null; }

        public int IdOf(Collider c) { var r = Of(c); return r != null ? r.serial : -1; }

        public Vector3 VelocityOf(Collider c)
        {
            var r = Of(c);
            return r == null || r.act == Act.Fall ? Vector3.zero : new Vector3(Mathf.Sin(r.heading), 0f, Mathf.Cos(r.heading)) * r.speed;
        }

        /// <summary>
        /// Hit by the rider: thrown off their feet as the rider is in a crash (Ragdoll, on their own skeleton: its joints'
        /// limits, the powered start, laid still when it stops), from where they were. Once they've lain still a moment
        /// they get up (as from a fall of their own) where they lie; out of sight before that, they're gone.
        /// </summary>
        public void Knock(Collider c, Vector3 throwVelocity, Vector3 at)
        {
            var r = Of(c);
            if (r == null || r.down != null) return;
            Knocked++;
            if (r.group != null && r.group.lead == r) r.group.nextStop = clock + 30f;
            Grunted?.Invoke(r.pos + Vector3.up, r.look.female);
            var rag = r.near != null || TakePerson(r) ? RagFor(r) : null;
            if (rag == null)
            {
                // (no body to throw: down in the snow, as their own fall)
                r.act = Act.Fall; r.t = 0f; r.until = Random.Range(3f, 4.5f); r.speed = Mathf.Max(r.speed, 3f);
                return;
            }
            PoseFor(r, 0f); r.near.Apply(0f);
            r.near.SetLod(0); r.lod = 0;
            for (int i = 0; i < 2; i++) { if (r.near.Skis[i] != null) r.near.Skis[i].enabled = false; if (r.near.Poles[i] != null) r.near.Poles[i].enabled = false; }
            rag.Begin(throwVelocity);
            r.down = rag; r.downT = 0f; r.downStill = 0f;
            r.act = Act.Fall; r.t = 0f; r.until = 0f; r.speed = 0f; r.sitW = 0f;
            if (r.solid != null) { r.solid.gameObject.SetActive(false); r.solid = null; }
        }

        Ragdoll RagFor(Rider r)
        {
            var p = r.near;
            if (p == null) return null;
            if (rags.TryGetValue(p, out var rag)) return rag;
            var rig = new RiderRig(p.Model);
            if (!rig.Ok) return null;
            Transform board;
            if (p.Board != null && !r.ski) board = p.Board.transform;
            else
            {
                // (a skier: the skis stay on their feet, hidden while thrown; a token board flies off, unseen)
                var g = new GameObject("KnockToken"); g.transform.SetParent(p.transform, false); board = g.transform;
            }
            var heroCol = player != null ? player.GetComponent<Collider>() : null;
            rag = new Ragdoll(rig, board, heroCol);
            if (p.Board == null || r.ski) rag.SetBoard(board, new Vector3(0.08f, 0.02f, 0.2f), Vector3.zero, false);
            rags[p] = rag;
            return rag;
        }

        /// <summary>The thrown riders' physics: a fixed step's worth (FixedUpdate; the tests call it).</summary>
        public void PhysicsStep()
        {
            foreach (var r in riders) if (r != null && r.down != null) r.down.PhysicsStep();
        }
        void FixedUpdate() { PhysicsStep(); }
        void LateUpdate() { foreach (var r in riders) if (r != null && r.down != null) r.down.LateStep(); }

        /// <summary>A thrown rider, a frame: lying still a moment, laid to rest, then up again; out of sight, gone.</summary>
        void Down(Rider r, float dt)
        {
            var rag = r.down;
            r.downT += dt;
            Vector3 c = rag.Centre;
            r.pos = new Vector3(c.x, Slope.DrawnHeight(c.x, c.z), c.z); r.ground = r.pos.y;
            if (rag.Speed < 0.7f) r.downStill += dt; else r.downStill = 0f;
            if (r.downStill > 1.2f) rag.Settle(dt, r.downStill > 2.4f);
            bool seen = Seen(r.pos, 4f);
            if ((!seen && r.downT > 1.5f) || r.downT > 40f && !seen) { GoneDown++; Up(r); Recycle(r); return; }
            if (r.downStill > 3.2f || r.downT > 25f)
            {
                // up, where they lie: sat in the snow, then on their feet (their own fall's getting up)
                GotUp++; Up(r);
                r.act = Act.Fall; r.t = 0f; r.until = 0.6f; r.sitW = 1f; r.speed = 0f;
                r.heading = 0f; r.board = r.ski ? Mathf.PI / 2f : -Mathf.PI / 2f;
            }
        }

        /// <summary>Back from thrown to posed (the skeleton home, the skis back on).</summary>
        void Up(Rider r)
        {
            if (r.down == null) return;
            r.down.End();
            r.down = null;
            if (r.near != null) { for (int i = 0; i < 2; i++) { if (r.near.Skis[i] != null) r.near.Skis[i].enabled = true; if (r.near.Poles[i] != null) r.near.Poles[i].enabled = true; } PoseFor(r, 0f); }
        }

        // ---------------------------------------------------------------- solid

        /// <summary>The nearest few drawn are solid: a capsule round the pose (an Obstacle: square on, a crash; glancing, a slide by).</summary>
        void Collide()
        {
            Solid = 0;
            byDistance.Clear();
            // (not the thrown, their own body is solid; nor anyone in their first two seconds on the snow: nobody
            // appears into you)
            foreach (var r in riders) if (r.drawn && r.d < ColliderRange && r.down == null && clock - r.bornAt > 2f) byDistance.Add(r);
            byDistance.Sort(ByD.I);
            if (byDistance.Count > ColliderCap) byDistance.RemoveRange(ColliderCap, byDistance.Count - ColliderCap);
            foreach (var r in riders) if (r.solid != null && !byDistance.Contains(r)) { r.solid.gameObject.SetActive(false); r.solid = null; }
            bool moved = false;
            foreach (var r in byDistance)
            {
                if (r.solid == null)
                {
                    CapsuleCollider c = null;
                    foreach (var s in solids) if (!s.gameObject.activeSelf) { c = s; break; }
                    if (c == null)
                    {
                        var g = new GameObject("RiderSolid");
                        g.transform.SetParent(transform, false);
                        var rb = g.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                        c = g.AddComponent<CapsuleCollider>(); c.radius = 0.32f; c.direction = 1;
                        g.AddComponent<Obstacle>();
                        solids.Add(c);
                    }
                    c.gameObject.SetActive(true);
                    c.GetComponent<Obstacle>().label = r.ski ? "Skier!" : "Snowboarder!";
                    c.GetComponent<Obstacle>().person = this;
                    r.solid = c;
                }
                // as tall as the pose: riding (crouched lower), sat in the snow
                float hgt = Mathf.Lerp(Mathf.Lerp(1.62f, 1.3f, r.crouch), 0.9f, r.sitW) * Mathf.Clamp(r.look.height, 0.9f, 1.1f);
                r.solid.height = hgt;
                r.solid.center = Vector3.up * (hgt * 0.5f);
                r.solid.transform.SetPositionAndRotation(new Vector3(r.pos.x, r.ground + r.air, r.pos.z), Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, r.up, 0.6f)));
                moved = true; Solid++;
                // run into (or brushed past) by the player: down they go
                if (player != null && r.act != Act.Fall)
                {
                    var d = player.transform.position - r.solid.transform.position; d.y = 0f;
                    if ((d.sqrMagnitude < 0.66f * 0.66f && player.Velocity.sqrMagnitude > 4f) || (player.Crashed && d.sqrMagnitude < 1.3f * 1.3f)) { r.act = Act.Fall; r.t = 0f; r.until = Random.Range(2.5f, 4f); r.speed = Mathf.Max(r.speed, 3f); if (r.group != null && r.group.lead == r) r.group.nextStop = clock + 30f; }
                }
            }
            if (moved) Physics.SyncTransforms();
        }

        // ---------------------------------------------------------------- tracks and spray

        void Tracks(float dt)
        {
            if (fx == null) fx = FindFirstObjectByType<SnowFX>();
            // the nearest few riding get a track (a short one: TrailChunks)
            byDistance.Clear();
            foreach (var r in riders) if (r.active && r.d < TrailRange && r.act != Act.Wait) byDistance.Add(r);
            byDistance.Sort(ByD.I);
            if (byDistance.Count > TrailCap) byDistance.RemoveRange(TrailCap, byDistance.Count - TrailCap);
            foreach (var r in riders)
                if (r.trail != null && (!r.active || !byDistance.Contains(r)) && r.d > TrailRange + 30f) { trailOf.Remove(r.trail); r.trail.Feed(r.pos, Vector3.up, 0f, false, 1f, 0f, 0f, dt); r.trail = null; }
            foreach (var r in byDistance)
            {
                if (r.trail == null)
                {
                    SnowTrail t = null;
                    foreach (var s in trails) if (!trailOf.ContainsKey(s)) { t = s; break; }
                    if (t == null && trails.Count < TrailCap) { t = new SnowTrail(lit != null ? lit : PeopleKit.Lit) { MaxChunks = TrailChunks }; trails.Add(t); }
                    if (t == null) continue;
                    t.Clear();
                    trailOf[t] = r; r.trail = t;
                }
                bool on = r.air <= 0f && r.speed > 0.3f && r.sitW < 0.5f;
                r.trail.Mix = Surfaces.Surface(r.pos.x, r.pos.z);
                r.trail.Bite = 0.5f;
                r.trail.Feed(new Vector3(r.pos.x, r.snow, r.pos.z), r.normal, r.heading, on, r.ski ? 0.7f : 1f, r.carve, r.skid, dt);
                // spray off the edge in a hard turn or a stop
                if (fx != null && r.drawn && r.d < SprayRange && on)
                {
                    float hard = Mathf.Max(r.skid, Mathf.Abs(r.carve) - 0.5f);
                    r.puff -= dt;
                    if (hard > 0.3f && r.speed > 3f && r.puff <= 0f)
                    {
                        r.puff = 0.12f;
                        var right = new Vector3(Mathf.Cos(r.heading), 0f, -Mathf.Sin(r.heading));
                        fx.Puff(new Vector3(r.pos.x, r.ground + 0.1f, r.pos.z) - right * Mathf.Sign(r.carve) * 0.4f, r.speed * hard);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- the bakes

        static Person bakePerson;

        /// <summary>A pose frozen into one simplified mesh, in the rider's own frame (made the first time it's wanted).</summary>
        static Mesh Baked(int tier, int v)
        {
            var m = baked[tier][v];
            if (m != null) return m;
            if (!PeopleKit.Load()) return null;
            int pose = v / Heads, head = v % Heads;
            bool ski = pose >= (int)Pose.SkiRide;
            var look = PersonLook.Random(5151 + v);
            look.height = BakeHeight; look.build = 1f; look.shoulders = 1f;
            look.head = head == 1 ? PersonHead.Beanie : PersonHead.Helmet;
            look.hasPompom = head == 1;
            look.hairStyle = PersonHair.None; look.pack = null; look.faceShows = false;
            look.gear = ski ? PersonGear.Skis : PersonGear.Snowboard;
            if (bakePerson == null) { bakePerson = Person.Make(look, PeopleKit.Root, "MountainBake"); if (bakePerson == null) return null; bakePerson.Shadows(false); bakePerson.Windless = true; }
            else { bakePerson.gameObject.SetActive(true); bakePerson.Dress(look); }
            var p = bakePerson;
            var at = new Vector3(0f, -3200f, 0f);
            var r = new Rider { look = look, ski = ski, pos = at, ground = at.y, normal = Vector3.up, up = Vector3.up };
            switch ((Pose)pose)
            {
                case Pose.BoardRide: case Pose.SkiRide: r.speed = 10f; break;
                case Pose.BoardToe: r.speed = 10f; r.carve = 1f; break;
                case Pose.BoardHeel: r.speed = 10f; r.carve = -1f; break;
                case Pose.BoardStand: case Pose.SkiStand: r.speed = 0f; r.board = ski ? Mathf.PI / 2f : -Mathf.PI / 2f; break;
                case Pose.BoardSit: case Pose.SkiSit: r.speed = 0f; r.sitW = 1f; r.act = Act.Stop; r.board = ski ? Mathf.PI / 2f : -Mathf.PI / 2f; break;
            }
            p.gameObject.SetActive(true);
            p.SetLod(0);
            r.near = p;
            for (int i = 0; i < 4; i++) PoseFor(r, i == 0 ? 0f : 0.5f);
            var frame = Matrix4x4.TRS(at, RotFor(r), Vector3.one);
            m = PeopleLod.Bake(p, frame.inverse, bakeCell[tier], $"MountainRider{(tier == 0 ? "Mid" : "Far")}{(Pose)pose}{head}");
            baked[tier][v] = m;
            if (System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_LOG") == "1") Debug.Log($"MountainRiders: {(tier == 0 ? "mid" : "far")} bake {(Pose)pose}/{(head == 1 ? "beanie" : "helmet")}: {m.vertexCount} vertices, {m.GetIndexCount(0) / 3} triangles");
            p.SetLod(2);
            p.gameObject.SetActive(false);
            return m;
        }

        /// <summary>A person posed as the rider is (the near ones, and the bakes).</summary>
        static void PoseFor(Rider r, float dt)
        {
            var p = r.near;
            p.lookWeight = 0f; p.turnChest = 0f; p.pointW = 0f; p.handW[0] = p.handW[1] = 0f;
            p.skisOn = false; p.polesOn = false; p.skisOnShoulder = false; p.boardInHand = -1; p.boardFoot = -1;
            p.held = r.ski ? Person.Held.None : Person.Held.Underfoot;
            p.crouch = 0f;   // (Person's crouch is strapping in, reaching for a binding: the clips have the riding stance)
            var rot = RotFor(r);
            if (r.ski) { p.skisOn = true; p.polesOn = true; p.polesInOneHand = false; p.polesDir = (Vector3.down + rot * Vector3.back * 0.8f).normalized; }
            if (r.sitW > 0.01f)
            {
                p.mode = Person.Mode.Sit; p.sitW = r.sitW;
                p.feetDown = true; p.footY = r.ground; p.feetForward = r.ski ? 0.55f : 0.32f; p.lean = r.act == Act.Fall ? 22f : 10f; p.hipsForward = 0f;
                p.transform.SetPositionAndRotation(new Vector3(r.pos.x, r.ground + 0.02f, r.pos.z), rot);
            }
            else
            {
                p.mode = r.ski ? Person.Mode.Ski : Person.Mode.Ride;
                p.pace = Mathf.Clamp01(r.speed / 4f);
                p.carve = r.carve;
                p.transform.SetPositionAndRotation(r.pos, rot);
            }
            p.Apply(dt);
        }

        static Quaternion RotFor(Rider r)
        {
            if (r.sitW > 0.01f && (r.act == Act.Stop || r.act == Act.Wait || r.act == Act.Fall))
            {
                // sat in the snow: a boarder facing down the hill, the board across it; a skier across the hill,
                // toward the run's middle, the skis side by side
                var f = r.ski ? new Vector3(r.lane >= 0f ? -1f : 1f, 0f, 0.35f) : Vector3.forward;
                return Quaternion.LookRotation(Vector3.ProjectOnPlane(f, r.normal).normalized, r.normal);
            }
            var travel = new Vector3(Mathf.Sin(r.board), 0f, Mathf.Cos(r.board));
            travel = Vector3.ProjectOnPlane(travel, r.up).normalized;
            if (r.ski) return Quaternion.LookRotation(travel, r.up);
            return Quaternion.LookRotation(Vector3.Cross(-travel, r.up).normalized, r.up);
        }
    }
}
