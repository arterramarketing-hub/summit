using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The people riding the chairlifts: the mountain's people (Person, from the hero's own model) sitting on the
    /// chairs SkiLift moves, in place of the old block figures. SkiLift tells this, chair by chair, where each chair
    /// is this frame; who is aboard is a pure function of the lift, the chair and the lap (like the chairs
    /// themselves): about a quarter of the chairs going up are empty, the rest carry one to four people, men and
    /// women of every size and skin tone in the closet's clothes, on skis (both poles in one hand) or a board
    /// (hanging from the front foot), hands on their laps or on the bar. They get on just past the bottom bullwheel
    /// and off inside the top station; once in a while someone rides down without gear.
    ///
    /// Drawn by distance from the camera:
    ///   - within NearRange, the nearest NearCap are real people (pooled, re-dressed as they come and go), alive:
    ///     looking about, at the view, at each other while they talk, down at you as you ride under them, now and
    ///     then pointing you out, the hanging feet swinging; the full model inside FullRange, the simplified past it;
    ///   - out to MidRange, the same poses baked into simplified meshes and drawn instanced, a palette row each;
    ///   - out to FarRange, coarser bakes; past that, the coarsest, on every chair the lift draws however far off, so
    ///     no chair comes into view empty and fills up as you come nearer. The instanced shader's variants are kept in
    ///     a build by Resources/PeopleLit.mat (see PeopleAssets).
    /// The seats are the chair's (Tools/MakeLift.py): nothing here goes through the seat, the back, the bar or the
    /// footrest at any of the body sizes (PeopleTest checks), and a chair never comes over a jump station's lane, so
    /// neither do its riders.
    /// </summary>
    public static class LiftRiders
    {
        public static bool On = true;
        public const float NearRange = 15f, FullRange = 8f, MidRange = 75f, FarRange = 250f;   // (past FarRange: the distant bakes, as far as the chairs go)
        public const int NearCap = 6;

        // the chair, in its own frame: the grip on the cable at the origin, +z the way it travels (Tools/MakeLift.py)
        public const float SeatTop = -2.85f, SeatZ = -0.01f, BarY = -2.48f, BarZ = 0.5f, BarR = 0.028f;
        public const float FootrestY = -3.4f, FootrestZ = 0.64f, FootrestR = 0.028f;
        public static readonly float[] SeatX = { -0.80f, -0.272f, 0.272f, 0.80f };   // (the outer two a little in from the chair's ends, where its frame is)

        // the poses (the same live and baked): skis or board, hands on the lap or on the bar
        public const int Poses = 4, Heads = 2;
        public enum Pose { SkiLap, SkiBar, BoardLap, BoardBar }

        class Rider
        {
            public long key;
            public PersonLook look;
            public int row, pose, head, seat, count, lift, chair;
            public float seen, d2, phase;
            public Matrix4x4 chairM;
            public bool down;
            public Person near;
        }
        static readonly Dictionary<long, Rider> riders = new Dictionary<long, Rider>();
        static readonly List<Rider> now = new List<Rider>();
        static readonly List<Person> pool = new List<Person>();
        static readonly List<long> stale = new List<long>();
        static float clock;
        static Vector3 eye, player;
        static bool playerKnown;

        // baked meshes: [tier][pose * Heads + head]
        public const int Tiers = 3;   // mid, far, distant
        static readonly Mesh[][] baked = { new Mesh[Poses * Heads], new Mesh[Poses * Heads], new Mesh[Poses * Heads] };
        static readonly float[] bakeCell = { 0.075f, 0.16f, 0.34f };
        const float BakeHeight = 0.97f;
        static readonly List<Matrix4x4>[][] mats = { NewLists(), NewLists(), NewLists() };
        static readonly float[][][] rowArr = { NewRows(), NewRows(), NewRows() };
        static readonly MaterialPropertyBlock[][] mpbs = { NewBlocks(), NewBlocks(), NewBlocks() };
        const int MaxBatch = 500;
        static List<Matrix4x4>[] NewLists() { var a = new List<Matrix4x4>[Poses * Heads]; for (int i = 0; i < a.Length; i++) a[i] = new List<Matrix4x4>(); return a; }
        static float[][] NewRows() { var a = new float[Poses * Heads][]; for (int i = 0; i < a.Length; i++) a[i] = new float[MaxBatch]; return a; }
        static MaterialPropertyBlock[] NewBlocks() { var a = new MaterialPropertyBlock[Poses * Heads]; for (int i = 0; i < a.Length; i++) a[i] = new MaterialPropertyBlock(); return a; }

        // ---------------------------------------------------------------- SkiLift's calls

        /// <summary>A new frame: where the camera and the player are.</summary>
        public static void Begin(Vector3 camera, Vector3 rider, bool riderKnown)
        {
            eye = camera; player = rider; playerKnown = riderKnown;
            clock = Time.time;
            now.Clear();
        }

        /// <summary>
        /// For the before/after pictures only: draw as main did before liftpop (riders cut at 420 m), or as the phone did
        /// (TestPhoneBefore: no instanced riders at all, the build having dropped the shader's instanced variants).
        /// </summary>
        public static bool TestCutAt420, TestPhoneBefore;

        /// <summary>For the tests: every chair SkiLift hands over (lift, chair, lap, where, how many aboard, drawn with them).</summary>
        public static System.Action<int, int, int, Vector3, int, bool> Seen;

        /// <summary>
        /// One chair this frame: the lift, which chair, which lap of the loop, how far round the loop it is (m), its
        /// matrix (the grip on the cable, +z the way it travels).
        /// </summary>
        public static void Chair(LiftLine.Lift l, int c, int lap, float d, Matrix4x4 m)
        {
            if (!On || l.cum == null) return;
            int n = l.Spans;
            float straight = LiftLine.StationHalfLen + LiftLine.WheelBack;
            float h = Slope.Hash01(c + l.id * 977, lap, 151);
            int count = 0; bool down = false;
            // aboard from just past the bottom bullwheel to the unloading inside the top station
            if (d >= l.cum[0] + 0.8f && d < l.cum[n + 1] + straight * 0.45f)
                count = h < 0.27f ? 0 : h < 0.47f ? 1 : h < 0.70f ? 2 : h < 0.85f ? 3 : 4;
            // and now and then someone riding down: on at the top, off at the bottom
            else if (h > 0.965f && d >= l.cum[n + 3] + 0.8f && d < l.cum[2 * n + 4] + straight * 0.45f) { count = h > 0.99f ? 2 : 1; down = true; }
            Vector3 pos = m.GetColumn(3);
            float d2 = (pos - eye).sqrMagnitude;
            // (on every chair the lift draws, however far: SkiLift already keeps to its own range, and a chair seen
            // empty that fills up as you come nearer is the pop this avoids)
            bool shown = count > 0 && (!TestCutAt420 || d2 <= 420f * 420f);
            Seen?.Invoke(l.id, c, lap, pos, count, shown);
            if (!shown) return;
            float h2 = Slope.Hash01(c + l.id * 977, lap, 152);
            int seats = SeatSet(count, h2);
            for (int s = 0; s < 4; s++)
            {
                if ((seats & (1 << s)) == 0) continue;
                long key = ((long)l.id << 48) ^ ((long)c << 32) ^ ((long)(lap & 0xfffffff) << 3) ^ s;
                if (!riders.TryGetValue(key, out var r))
                {
                    int seed = l.id * 7919 + c * 104729 + lap * 1299709 + s * 15485863;
                    var look = PersonLook.Random(seed, 0.55f);
                    if (down) { look.gear = PersonGear.None; }
                    // four to a chair: no one broader than a quad's seat takes
                    look.build = Mathf.Min(look.build, 1.06f); look.shoulders = Mathf.Min(look.shoulders, (s == 0 || s == 3) && look.pack != null ? 0.98f : 1.03f);
                    r = new Rider { key = key, look = look, seat = s, lift = l.id, chair = c, down = down, phase = Slope.Hash01(seed, 1, 77) * 100f };
                    r.pose = down ? (int)Pose.SkiLap : look.gear == PersonGear.Skis ? (Slope.Hash01(seed, 2, 77) < 0.5f ? 0 : 1) : (Slope.Hash01(seed, 2, 77) < 0.5f ? 2 : 3);
                    r.head = look.head == PersonHead.Beanie ? 1 : 0;
                    r.row = -1;
                    riders[key] = r;
                }
                r.count = count; r.chairM = m; r.d2 = d2; r.seen = clock;
                now.Add(r);
            }
        }

        /// <summary>Which of the four seats count people take (a bit mask).</summary>
        static int SeatSet(int count, float h)
        {
            switch (count)
            {
                case 1: return 1 << Mathf.Min(3, (int)(h * 4f));
                case 2: { int[] p = { 0b0011, 0b0110, 0b1100, 0b1001, 0b0101, 0b1010, 0b0110 }; return p[Mathf.Min(p.Length - 1, (int)(h * p.Length))]; }
                case 3: return 0b1111 & ~(1 << Mathf.Min(3, (int)(h * 4f)));
                default: return 0b1111;
            }
        }

        /// <summary>Everyone forgotten (a new mountain).</summary>
        public static void Clear()
        {
            foreach (var r in riders.Values) if (r.near != null) Release(r);
            riders.Clear(); now.Clear();
        }

        // ---------------------------------------------------------------- each frame

        static readonly List<Rider> nearList = new List<Rider>();
        sealed class ByDistance : IComparer<Rider>
        {
            public static readonly ByDistance Instance = new ByDistance();
            public int Compare(Rider a, Rider b) => a.d2.CompareTo(b.d2);
        }
        public static int NearShown { get; private set; }
        public static int MidShown { get; private set; }
        public static int FarShown { get; private set; }
        public static int DistantShown { get; private set; }
        public static long Triangles { get; private set; }
        public static int DrawCalls { get; private set; }

        /// <summary>Pose the near riders and draw the rest (after SkiLift has called Chair for every chair).</summary>
        public static void Draw(float dt)
        {
            if (!On) return;
            if (!PeopleKit.Load()) return;
            PeopleLod.BuildPending();
            // who comes close enough to be a real person: the nearest few
            nearList.Clear();
            foreach (var r in now) if (r.d2 < NearRange * NearRange) nearList.Add(r);
            nearList.Sort(ByDistance.Instance);   // (a comparer kept, not one made from a lambda each frame)
            if (nearList.Count > NearCap) nearList.RemoveRange(NearCap, nearList.Count - NearCap);
            // let go of those who left (gone, or no longer among the nearest)
            foreach (var r in riders.Values) if (r.near != null && (r.seen != clock || !nearList.Contains(r))) Release(r);
            NearShown = 0; MidShown = 0; FarShown = 0; DistantShown = 0; Triangles = 0; DrawCalls = 0;
            foreach (var r in nearList)
            {
                if (r.near == null && !Take(r)) continue;
                Live(r, dt);
                NearShown++;
                Triangles += r.near.Lod == 0 ? 15000 : 3000;
                DrawCalls += r.near.Lod == 0 ? 5 : 3;
            }
            // the rest, instanced
            for (int t = 0; t < Tiers; t++) foreach (var l in mats[t]) l.Clear();
            foreach (var r in now)
            {
                if (r.near != null) continue;
                int tier = r.d2 < MidRange * MidRange ? 0 : r.d2 < FarRange * FarRange ? 1 : 2;
                int v = r.pose * Heads + r.head;
                var list = mats[tier][v];
                if (list.Count >= MaxBatch) continue;
                r.row = PeopleKit.Row(r.look);   // (asked for each frame it's drawn: a row not drawn lately can go to a new look)
                float k = Mathf.Clamp(r.look.height, 0.95f, 1.03f) / BakeHeight;
                rowArr[tier][v][list.Count] = r.row;
                list.Add(r.chairM * SeatLocal(r.seat) * Matrix4x4.Scale(new Vector3(k * Mathf.Clamp(r.look.build, 0.95f, 1.06f), k, k)));
                if (tier == 0) MidShown++; else if (tier == 1) FarShown++; else DistantShown++;
            }
            PeopleKit.Flush();
            var mat = PeopleKit.LodMaterial;
            for (int t = 0; t < Tiers; t++)
                for (int v = 0; v < Poses * Heads; v++)
                {
                    var list = mats[t][v];
                    if (list.Count == 0) continue;
                    var mesh = Baked(t, v);
                    if (mesh == null || TestPhoneBefore) continue;
                    var mpb = mpbs[t][v];
                    mpb.SetFloatArray("_Row", rowArr[t][v]);
                    var rp = new RenderParams(mat) { matProps = mpb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
                    Graphics.RenderMeshInstanced(rp, mesh, 0, list);
                    DrawCalls++;
                    Triangles += (long)(mesh.GetIndexCount(0) / 3) * list.Count;
                }
            // forget riders not seen for a while (their palette rows are recycled as new ones come)
            if (riders.Count > 600)
            {
                stale.Clear();
                foreach (var kv in riders) if (clock - kv.Value.seen > 3f && kv.Value.near == null) stale.Add(kv.Key);
                foreach (var k in stale) riders.Remove(k);
            }
        }

        /// <summary>A seat's frame in the chair's: the seat's surface under the hips, facing the way the chair goes.</summary>
        public static Matrix4x4 SeatLocal(int seat) => Matrix4x4.Translate(new Vector3(SeatX[seat], SeatTop, SeatZ));

        static bool Take(Rider r)
        {
            pool.RemoveAll(q => q == null);   // (gone with a scene)
            Person p = null;
            foreach (var q in pool) if (!q.gameObject.activeSelf) { p = q; break; }
            if (p == null)
            {
                if (pool.Count >= NearCap + 2) return false;
                p = Person.Make(r.look, PeopleKit.Root, "LiftRider");
                if (p == null) return false;
                p.Shadows(false);
                pool.Add(p);
            }
            else { p.gameObject.SetActive(true); p.Dress(r.look); }
            r.near = p;
            Place(p, r.look, r.pose, r.down);
            p.Apply(0f);
            return true;
        }

        static void Release(Rider r)
        {
            if (r.near != null) { r.near.SetLod(2); r.near.gameObject.SetActive(false); }
            r.near = null;
        }

        // ---------------------------------------------------------------- the poses

        /// <summary>Sit a person in a seat in one of the poses (what the bake and the live riders share).</summary>
        public static void Place(Person p, PersonLook look, int pose, bool noGear = false)
        {
            p.mode = Person.Mode.Sit;
            p.sitW = 1f;
            p.feetDown = false;
            p.kneesApart = look.female ? 0.2f : 0.45f;
            // a pack on the back, or a ponytail, keeps them off the back of the seat
            bool hair = look.hairStyle != PersonHair.None;
            p.hipsForward = (look.pack != null ? 0.2f : hair ? 0.05f : 0f) * look.height;
            bool bar = pose == (int)Pose.SkiBar || pose == (int)Pose.BoardBar;
            bool ski = pose == (int)Pose.SkiLap || pose == (int)Pose.SkiBar;
            p.lean = look.pack != null ? -8f : bar ? -1f : hair ? 1f : 7f;
            p.skisOn = ski && !noGear; p.polesOn = ski && !noGear; p.polesInOneHand = true;
            p.polesDir = PolesDir(bar);
            p.toesDown = ski ? 12f : 24f;
            p.held = !ski && !noGear ? Person.Held.Placed : Person.Held.None;
            p.boardFoot = !ski && !noGear ? (Slope.Hash01((int)(look.height * 1000f), (int)(look.build * 1000f), 5) < 0.7f ? 0 : 1) : -1;
            p.boardTurn = 0f;
            p.handW[0] = p.handW[1] = 1f;
            p.lookWeight = 0f; p.pointW = 0f; p.phoneUp = 0f; p.swing = 0f;
            p.SetLod(0);
        }

        /// <summary>The poles, from the right fist, in the chair's frame: down and ahead, in front of the bar and clear of the footrest.</summary>
        static Vector3 PolesDir(bool bar) => bar ? new Vector3(0f, -0.82f, 0.57f) : new Vector3(0f, -0.8f, 0.6f);

        /// <summary>Where the hands go in a pose (world), given the person's seat frame (and a hanging board turned off the footrest's post).</summary>
        static void Hands(Person p, int pose, Matrix4x4 chairToWorld, int seat)
        {
            p.boardTurn = seat == 0 || seat == 2 ? -10f : 10f;
            // knees together beside the footrest's posts (the outer seats), and when perched forward
            p.kneesApart = (p.Look.female ? 0.2f : 0.45f) * (seat == 0 || seat == 3 ? 0.4f : 1f) * (p.Look.pack != null ? 0.3f : 1f);
            p.kneesShift = seat == 0 ? -0.09f : seat == 3 ? 0.09f : 0f;
            bool bar = pose == (int)Pose.SkiBar || pose == (int)Pose.BoardBar;
            float s = p.Scale;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 local = bar
                    ? new Vector3(SeatX[seat] + side * 0.12f, BarY + 0.075f, BarZ - 0.08f)   // the wrists over and just behind the bar, the fingers over it
                    : new Vector3(SeatX[seat] + side * 0.1f, SeatTop + 0.22f * s, SeatZ + 0.27f * s + p.hipsForward);   // resting on the thighs
                p.handAt[i] = chairToWorld.MultiplyPoint3x4(local);
            }
        }

        /// <summary>For the tests: the hands put where a pose has them.</summary>
        public static void TestHands(Person p, int pose, Matrix4x4 chairToWorld, int seat) => Hands(p, pose, chairToWorld, seat);

        static void Live(Rider r, float dt)
        {
            var p = r.near;
            var seatM = r.chairM * SeatLocal(r.seat);
            p.transform.SetPositionAndRotation(seatM.GetColumn(3), seatM.rotation);
            float d = Mathf.Sqrt(r.d2);
            p.SetLod(d < FullRange ? 0 : 1);
            Hands(p, r.pose, r.chairM, r.seat);
            // poles: down and ahead in the chair's frame
            p.polesDir = r.chairM.MultiplyVector(PolesDir(r.pose == (int)Pose.SkiBar));
            // a slow swing of the hanging feet, each their own
            float t = clock + r.phase;
            p.swing = 0.07f * Mathf.Sin(t * 0.9f) * (r.look.gear == PersonGear.Snowboard ? 0.4f : 1f);
            // what they look at: a few seconds at a time, each their own choice
            int seg = Mathf.FloorToInt(t / 3.7f);
            float u = t / 3.7f - seg;
            float ease = Mathf.SmoothStep(0f, 1f, Mathf.Min(u / 0.25f, (1f - u) / 0.25f));
            float pick = Slope.Hash01((int)(r.key & 0x7fffffff), seg, 61);
            Vector3 head = p.HeadPosition;
            Vector3 fwd = r.chairM.MultiplyVector(Vector3.forward), right = r.chairM.MultiplyVector(Vector3.right);
            // a neighbour to talk to
            int nb = Neighbour(r);
            bool talking = nb >= 0 && pick < 0.45f;
            p.pointW = 0f;
            if (playerKnown && (player - head).sqrMagnitude < 40f * 40f && player.y < head.y + 2f)
            {
                // you, riding by: everyone looks; one of them points you out
                p.lookAt = player + Vector3.up; p.lookWeight = 0.9f; p.turnChest = 0.4f;
                if (Pointer(r))
                {
                    float k = Mathf.Clamp01(1f - ((player - head).magnitude - 6f) / 30f);
                    p.pointAt = player + Vector3.up * 0.5f; p.pointW = 0.85f * k; p.pointHand = 1;
                }
            }
            else if (talking)
            {
                // to the one beside: heads turned to each other
                Vector3 other = r.chairM.MultiplyPoint3x4(new Vector3(SeatX[nb], SeatTop + 0.75f * p.Scale, SeatZ + 0.05f));
                p.lookAt = other; p.lookWeight = 0.85f * ease; p.turnChest = 0.35f;
            }
            else if (pick < 0.75f)
            {
                // the view: off to one side, down the mountain
                float a = (Slope.Hash01((int)(r.key & 0x7fffffff), seg, 62) - 0.5f) * 2.2f;
                p.lookAt = head + (fwd * Mathf.Cos(a) + right * Mathf.Sin(a)) * 30f + Vector3.down * 9f;
                p.lookWeight = 0.75f * ease; p.turnChest = 0.2f;
            }
            else
            {
                // straight down at the snow going by under the skis
                p.lookAt = head + fwd * 6f + Vector3.down * 12f; p.lookWeight = 0.6f * ease; p.turnChest = 0f;
            }
            p.Apply(dt);
        }

        /// <summary>The seat of someone beside this rider on the same chair (-1: alone on that side).</summary>
        static int Neighbour(Rider r)
        {
            foreach (var o in now)
                if (o != r && o.lift == r.lift && o.chair == r.chair && Mathf.Abs(o.seat - r.seat) == 1) return o.seat;
            return -1;
        }

        /// <summary>The one on a chair who points: the first, by the hash, of those aboard.</summary>
        static bool Pointer(Rider r)
        {
            float mine = Slope.Hash01((int)(r.key & 0x7fffffff), 3, 63);
            if (mine > 0.6f) return false;
            foreach (var o in now)
                if (o != r && o.lift == r.lift && o.chair == r.chair && Slope.Hash01((int)(o.key & 0x7fffffff), 3, 63) < mine) return false;
            return true;
        }

        // ---------------------------------------------------------------- the bakes

        static Person bakePerson;

        /// <summary>A pose frozen into one simplified mesh, in the seat's frame (made the first time it's wanted).</summary>
        static Mesh Baked(int tier, int v)
        {
            var m = baked[tier][v];
            if (m != null) return m;
            int pose = v / Heads, head = v % Heads;
            var look = PersonLook.Random(4242 + v);
            look.height = BakeHeight; look.build = 1f; look.shoulders = 1f;
            look.head = head == 1 ? PersonHead.Beanie : PersonHead.Helmet;
            look.hasPompom = head == 1;
            look.hairStyle = PersonHair.None; look.pack = null; look.faceShows = false;
            look.gear = pose < 2 ? PersonGear.Skis : PersonGear.Snowboard;
            if (bakePerson == null) { bakePerson = Person.Make(look, PeopleKit.Root, "LiftBake"); if (bakePerson == null) return null; bakePerson.Shadows(false); bakePerson.Windless = true; }
            else { bakePerson.gameObject.SetActive(true); bakePerson.Dress(look); }
            var p = bakePerson;
            // on a chair standing still, out of sight
            var chairM = Matrix4x4.TRS(new Vector3(0f, -3000f, 0f), Quaternion.identity, Vector3.one);
            Place(p, look, pose);
            var seatM = chairM * SeatLocal(1);
            p.transform.SetPositionAndRotation(seatM.GetColumn(3), seatM.rotation);
            Hands(p, pose, chairM, 1);
            p.polesDir = PolesDir(pose == (int)Pose.SkiBar);
            for (int i = 0; i < 3; i++) p.Apply(0f);
            m = PeopleLod.Bake(p, seatM.inverse, bakeCell[tier], $"LiftRider{(tier == 0 ? "Mid" : tier == 1 ? "Far" : "Distant")}{v}");
            baked[tier][v] = m;
            if (System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_LOG") == "1") Debug.Log($"LiftRiders: {(tier == 0 ? "mid" : tier == 1 ? "far" : "distant")} bake {(Pose)pose}/{(head == 1 ? "beanie" : "helmet")}: {m.vertexCount} vertices, {m.GetIndexCount(0) / 3} triangles");
            p.SetLod(2);
            p.gameObject.SetActive(false);
            return m;
        }

        /// <summary>Make every baked pose now (at load, so the first rider seen far off doesn't cost a frame).</summary>
        public static void Prewarm()
        {
            if (!On || !PeopleKit.Load()) return;
            for (int t = 0; t < Tiers; t++) for (int v = 0; v < Poses * Heads; v++) Baked(t, v);
        }

        /// <summary>For the tests: the riders on chairs this frame (and the near ones' people).</summary>
        public static int Aboard => now.Count;
        public static IEnumerable<Person> NearPeople { get { foreach (var r in now) if (r.near != null) yield return r.near; } }
        public static void Riders(List<(Matrix4x4 chair, int seat, int pose, PersonLook look, Person near)> into)
        {
            into.Clear();
            foreach (var r in now) into.Add((r.chairM, r.seat, r.pose, r.look, r.near));
        }
    }
}
