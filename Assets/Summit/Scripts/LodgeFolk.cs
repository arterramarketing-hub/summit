using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The lodge's own people, so it looks lived in: in the covered section at the low end (under the roof's tail)
    /// people at the picnic tables, someone choosing at the racks with a board under the arm, two talking between the
    /// racks with their skis and board; in the café in the wing two at the counter with their hot chocolate and two
    /// standing talking over mugs by where it's made. Each sits or stands at one of the lodge's people's places
    /// (Camp.LodgeFolkAt, from Camp.LodgePeopleSpots) and stays there: talking, looking about, sipping, reaching to the
    /// rack. They are the camp's people's kind (Person, the hero's body) and held by the camp's life (CampLife.Hold): the
    /// camp's people and Val and Breanna walk round them, the hero can't walk through them, and CampLife's levels of
    /// detail decide who has the full model (the nearest three of everyone in the camp) and who the simplified one.
    ///
    /// Cost: made once (kept across the camp's rebuilds, as CampLife's); posed every frame in the full model, every third
    /// frame in the simplified one (staggered), not at all past CampLife's range; no shadows (they're under the roof or
    /// inside).
    /// </summary>
    public class LodgeFolk : MonoBehaviour
    {
        public static bool On = true;
        public static LodgeFolk Current { get; private set; }

        public class Folk
        {
            public Person p;
            public string pose;            // table, stool (sitting); rack, chat, mugchat (standing)
            public Vector3 seat;           // world: the seat's surface under the hips, or the floor where they stand
            public float yaw;              // world, degrees
            public float floorY;           // world: the floor under them
            public float topY;             // world: the table's (or counter's) top in front of them, if they sit at one
            public readonly List<Folk> with = new List<Folk>();   // at the same table, or the one they're talking to
            public Transform mug;
            public int k;
            public float acc;              // time not yet posed (the simplified ones are posed every third frame)
            public float lift = float.NaN; // the seated: how far their seat point is raised so their own body rests on the
                                           // seat's top (measured once, from their mesh: none sunk into it, none floating)
        }
        readonly List<Folk> folk = new List<Folk>();
        /// <summary>Test tools: the lodge's people as they are now.</summary>
        public IReadOnlyList<Folk> All => folk;
        /// <summary>Test tools: what the last frame of posing cost (ms) and how many were posed.</summary>
        public float LastStepMs { get; private set; }
        public int LastPosed { get; private set; }

        Camp camp;
        Transform crowd;
        Material mugMat, cocoaMat;
        float clock;
        int frame;
        static readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        /// <summary>Hooked to its camp (Camp.Build adds it after CampLife).</summary>
        public void Setup(Camp c)
        {
            if (camp == c) return;
            Current = this;
            camp = c;
            camp.Built += OnBuilt;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        // ---------------------------------------------------------------- building

        void OnBuilt(Transform root)
        {
            if (!On || !Camp.UseLodge)
            {
                foreach (var f in folk) if (f.p != null) { CampLife.Current?.Release(f.p); f.p.gameObject.SetActive(false); if (f.mug != null) f.mug.gameObject.SetActive(false); }
                return;
            }
            if (!PeopleKit.Load()) return;
            if (crowd == null) { crowd = new GameObject("LodgeFolk").transform; crowd.SetParent(transform, false); }
            if (mugMat == null) { mugMat = Marshmallow.Plain(new Color(0.93f, 0.90f, 0.82f), 0.55f); cocoaMat = Marshmallow.Plain(new Color(0.30f, 0.16f, 0.09f), 0.6f); }
            var life = CampLife.Current;
            var places = Camp.LodgeFolkAt;
            float cy = camp.ChaletYaw;
            for (int i = 0; i < places.Length; i++)
            {
                var (pose, at, yaw, gear) = places[i];
                Folk f = i < folk.Count ? folk[i] : null;
                if (f == null)
                {
                    var p = Person.Make(Look(i, pose, gear), crowd, "LodgeFolk");
                    if (p == null) return;
                    p.Shadows(false);
                    f = new Folk { p = p, k = i };
                    folk.Add(f);
                    if (pose == "stool" || pose == "mugchat") f.mug = Mug(p.transform);
                }
                f.pose = pose;
                var floor = camp.ChaletPoint(at.x, 0f, at.z);
                f.floorY = floor.y;
                f.seat = floor + Vector3.up * at.y;
                f.yaw = cy + yaw;
                f.p.gameObject.SetActive(true);
                f.p.transform.SetPositionAndRotation(f.seat, Quaternion.Euler(0f, f.yaw, 0f));
                f.acc = 0f;
            }
            // who they're with: the others at their table or the counter (within reach), the one they're talking to
            foreach (var f in folk)
            {
                f.with.Clear();
                foreach (var o in folk)
                    if (o != f && (o.seat - f.seat).sqrMagnitude < 1.7f * 1.7f && Sits(o) == Sits(f) && o.pose != "rack" && f.pose != "rack") f.with.Add(o);
            }
            foreach (var f in folk)
            {
                if (Sits(f))
                {
                    if (float.IsNaN(f.lift)) f.lift = Settle(f);
                    f.seat += Vector3.up * f.lift;
                }
            }
            foreach (var f in folk)
            {
                PeopleLod.Skinned(f.p.Look);   // (their simplified bodies now, with the camp: not one a frame while you watch)
                if (life != null) { life.Hold(f.p); life.Moved(f.p); }
                f.p.SetLod(life != null ? 1 : 0);
                Pose(f);
                for (int k = 0; k < 2; k++) f.p.Apply(0f);
                PlaceMug(f);
            }
        }

        public static bool Sits(Folk f) => f.pose == "table" || f.pose == "stool";

        static readonly List<Vector3> gPos = new List<Vector3>();
        static readonly List<int> gSlot = new List<int>(), gTris = new List<int>();

        /// <summary>
        /// How far to raise a seated person's seat point so their own body (their size, their clothes) rests on the
        /// seat's top: posed there in the full model, the lowest of them over the seat (the bench's depth, the stool's
        /// round top) put 4 mm over it. Once, when they're made.
        /// </summary>
        float Settle(Folk f)
        {
            var p = f.p;
            int lod = p.Lod;
            p.SetLod(0);
            Pose(f);
            for (int k = 0; k < 2; k++) p.Apply(0f);
            gPos.Clear(); gSlot.Clear(); gTris.Clear();
            PeopleKit.Gather(p, p.transform.worldToLocalMatrix, gPos, gSlot, gTris);
            float low = float.MaxValue;
            foreach (var v in gPos) if (OverSeat(f, v) && v.y < low) low = v.y;
            if (lod >= 0) p.SetLod(lod);
            return low == float.MaxValue ? 0f : Mathf.Clamp(0.004f - low, -0.05f, 0.12f);
        }

        /// <summary>
        /// Is a point (in the sitter's own frame: x across, z forward) over the seat's top? MakeLodge.py's bench (0.3 deep,
        /// its middle 0.11 behind the hips: Camp.PicnicHips; perched toward its front edge, the thighs fall clear of it),
        /// the stool's round top (r 0.19, its middle 6 cm ahead of the hips).
        /// </summary>
        public static bool OverSeat(Folk f, Vector3 v) =>
            f.pose == "table" ? Mathf.Abs(v.x) < 0.3f && v.z > -0.26f && v.z < 0.04f : new Vector2(v.x, v.z - 0.06f).magnitude < 0.19f;

        /// <summary>A pole's length (the poles a skier carries, planted beside them standing).</summary>
        static float PoleLength()
        {
            if (poleLen > 0f) return poleLen;
            var m = PeopleKit.PoleMesh;
            if (m == null) return poleLen = 1.2f;
            var sz = Vector3.Scale(m.bounds.size, PeopleKit.PoleLocal.lossyScale);
            return poleLen = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
        }
        static float poleLen = -1f;

        /// <summary>Someone for this place: dressed for the snow; inside (and at the tables) beanies, faces showing, no packs.</summary>
        static PersonLook Look(int i, string pose, PersonGear gear)
        {
            var l = PersonLook.Random(6100 + i * 41, 0.5f);
            l.pack = null;   // (the seats have backs, the benches each other; and a pack in the café: off)
            if (pose != "rack" && pose != "chat") { l.head = PersonHead.Beanie; l.faceShows = true; }
            if (gear != PersonGear.None) l.gear = gear;
            l.name = "LodgeFolk" + i;
            return l;
        }

        /// <summary>A mug of hot chocolate (the café's, as CampCocoa's): carried with the right hand.</summary>
        Transform Mug(Transform parent)
        {
            var m = new GameObject("Mug").transform;
            m.SetParent(crowd, false);
            Marshmallow.Prim(PrimitiveType.Cylinder, m, new Vector3(0f, 0.055f, 0f), new Vector3(0.09f, 0.055f, 0.09f), mugMat);
            Marshmallow.Prim(PrimitiveType.Cylinder, m, new Vector3(0f, 0.1f, 0f), new Vector3(0.078f, 0.003f, 0.078f), cocoaMat);
            Marshmallow.Prim(PrimitiveType.Cube, m, new Vector3(0.055f, 0.055f, 0f), new Vector3(0.02f, 0.06f, 0.015f), mugMat);
            return m;
        }

        // ---------------------------------------------------------------- each frame

        void Update()
        {
            if (!On || camp == null || folk.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            if ((cam.transform.position - camp.ChaletPoint(0f, 0f, 0f)).sqrMagnitude > 400f * 400f) return;   // (only while the camp is about)
            Step(Time.deltaTime);
        }

        /// <summary>One frame (public for the test tools). CampLife's step sets their levels of detail.</summary>
        public void Step(float dt)
        {
            sw.Restart();
            clock += dt; frame++;
            int posed = 0;
            foreach (var f in folk)
            {
                if (f.p == null || !f.p.gameObject.activeSelf) continue;
                if (CampLife.Current == null) f.p.SetLod(0);
                f.acc += dt;
                if (f.p.Lod >= 2) { f.acc = 0f; if (f.mug != null && f.mug.gameObject.activeSelf) f.mug.gameObject.SetActive(false); continue; }
                // the simplified ones every third frame, staggered (they barely move: breathing, a look, a hand)
                if (f.p.Lod == 1 && (frame + f.k) % 3 != 0) continue;
                Pose(f);
                f.p.Apply(f.acc);
                f.acc = 0f;
                PlaceMug(f);
                posed++;
            }
            LastPosed = posed;
            LastStepMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        float T(Folk f) => clock + f.k * 5.3f;

        /// <summary>The one they're looking at now (turns about at a table of three), or null.</summary>
        Folk Toward(Folk f)
        {
            if (f.with.Count == 0) return null;
            return f.with[Mathf.FloorToInt(T(f) / 6.5f) % f.with.Count];
        }

        /// <summary>Talking now (by turns with the one they're looking at)?</summary>
        bool Talking(Folk f) => Mathf.Repeat(T(f), 7f) < 3.2f;

        void Pose(Folk f)
        {
            var p = f.p;
            float t = T(f);
            p.lookWeight = 0f; p.turnChest = 0f; p.pointW = 0f; p.phoneUp = 0f;
            p.handW[0] = p.handW[1] = 0f;
            p.crouch = 0f; p.skisOn = false; p.polesOn = false; p.skisOnShoulder = false; p.boardInHand = -1; p.held = Person.Held.None; p.boardFoot = -1;
            var rot = Quaternion.Euler(0f, f.yaw, 0f);
            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right, up = Vector3.up;
            var to = Toward(f);
            p.transform.SetPositionAndRotation(f.seat, rot);
            switch (f.pose)
            {
                case "table":
                case "stool":
                {
                    // sitting: the hips on the seat, the feet on the floor (hanging from the counter's stool), facing
                    // the table or the counter
                    p.mode = Person.Mode.Sit; p.sitW = 1f;
                    p.feetDown = f.pose == "table"; p.footY = f.floorY; p.swing = 0f; p.toesDown = 20f;
                    p.feetForward = 0.03f; p.kneesShift = 0f;
                    p.lean = f.pose == "table" ? 5f : 2f; p.hipsForward = 0f;
                    p.kneesApart = p.Look.female ? 0.25f : 0.5f;
                    f.topY = f.floorY + (f.pose == "table" ? 0.78f : 1.07f);
                    bool talking = to != null && Talking(f);
                    float s = p.Scale;
                    if (f.pose == "stool")
                    {
                        // at the counter (chest high on the stool): the forearms on it, the mug by the right hand
                        // (the wrist this far over the top: the glove and the sleeve rest on it, not in it)
                        Vector3 onTop = new Vector3(f.seat.x, f.topY + 0.15f, f.seat.z) + fwd * 0.47f;
                        p.handAt[0] = onTop - right * 0.16f; p.handW[0] = 1f;
                        p.handAt[1] = onTop + right * 0.14f; p.handW[1] = 1f;
                    }
                    else
                    {
                        // at a picnic table: the hands resting on the thighs under it (a hand up over it would put the arm
                        // through its edge: they talk with their heads, nodding, turning to each other)
                        Vector3 lap = f.seat + up * (0.17f * s) + fwd * 0.22f;
                        p.handAt[0] = lap - right * 0.1f; p.handW[0] = 1f;
                        p.handAt[1] = lap + right * 0.1f; p.handW[1] = 1f;
                    }
                    Sip(f, t, fwd, right, up);
                    if (to != null && Mathf.Repeat(t, 11f) < 8.5f) { p.lookAt = to.p.HeadPosition + Vector3.down * (talking ? 0.05f * Mathf.Sin(t * 2.3f) : 0f); p.lookWeight = 0.85f; p.turnChest = 0.25f; }
                    else { p.lookAt = f.seat + rot * new Vector3(Mathf.Sin(t * 0.17f) * 10f, 0.8f, 18f); p.lookWeight = 0.5f; }
                    break;
                }
                case "mugchat":
                {
                    // standing with a mug, talking: facing each other, the mug held in front, a sip now and then; the
                    // other hand moves as they talk
                    p.mode = Person.Mode.Stand;
                    float s = p.Scale;
                    p.handAt[1] = f.seat + rot * new Vector3(0.12f * s, 1.08f * s, 0.26f * s); p.handW[1] = 1f;
                    if (to != null)
                    {
                        p.lookAt = to.p.HeadPosition + Vector3.down * 0.05f * Mathf.Sin(t * 2.1f); p.lookWeight = 0.85f;
                        if (Talking(f))
                        {
                            float g = Mathf.Sin(t * 2.7f) * 0.5f + 0.5f;
                            p.handAt[0] = f.seat + rot * new Vector3(-0.22f - 0.05f * g, 1.08f * s + 0.06f * Mathf.Sin(t * 3.3f), 0.3f);
                            p.handW[0] = 0.85f * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, Mathf.Repeat(t, 7f) / 0.6f));
                        }
                    }
                    f.topY = f.floorY;
                    Sip(f, t, fwd, right, up);
                    break;
                }
                case "rack":
                {
                    // choosing at the rack: their own board under the arm, looking along the boards, now and then
                    // reaching to one
                    p.mode = Person.Mode.Stand;
                    Gear(p, rot);
                    float u = Mathf.Sin(t * 0.21f);
                    var boards = f.seat + fwd * 0.75f + up * 1.15f + right * (0.7f * u);
                    p.lookAt = boards; p.lookWeight = 0.9f; p.turnChest = 0.3f;
                    float reachT = Mathf.Repeat(t, 9f);
                    float r = reachT > 5.5f && reachT < 8.5f ? Mathf.Sin((reachT - 5.5f) / 3f * Mathf.PI) : 0f;
                    int free = p.boardInHand == 1 ? 0 : p.skisOnShoulder ? -1 : 1;
                    if (free >= 0 && r > 0f) { p.handAt[free] = f.seat + fwd * 0.55f + up * 1.12f + right * (0.7f * u); p.handW[free] = 0.85f * r; }
                    break;
                }
                case "chat":
                {
                    // two talking between the racks, their skis and board with them: facing each other, by turns
                    // one talks (a hand moving, if it's free), the other nods
                    p.mode = Person.Mode.Stand;
                    Gear(p, rot);
                    if (to != null)
                    {
                        p.lookAt = to.p.HeadPosition + Vector3.down * 0.05f * Mathf.Sin(t * 2.1f); p.lookWeight = 0.85f;
                        int free = p.boardInHand == 1 ? 0 : p.skisOnShoulder ? -1 : 1;
                        if (free >= 0 && Talking(f))
                        {
                            float g = Mathf.Sin(t * 2.7f) * 0.5f + 0.5f;
                            p.handAt[free] = f.seat + rot * new Vector3((free == 1 ? 0.2f : -0.2f) + (free == 1 ? 0.05f : -0.05f) * g, 1.08f * p.Scale + 0.06f * Mathf.Sin(t * 3.3f), 0.3f);
                            p.handW[free] = 0.85f * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, Mathf.Repeat(t, 7f) / 0.6f));
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>A sip every ten seconds or so: the mug (in the right hand) to the lips and down again.</summary>
        static void Sip(Folk f, float t, Vector3 fwd, Vector3 right, Vector3 up)
        {
            if (f.mug == null) return;
            var p = f.p;
            float sip = Mathf.Repeat(t, 10f);
            float lift = sip < 2.2f ? Mathf.Sin(sip / 2.2f * Mathf.PI) : 0f;
            var lips = p.HeadPosition + fwd * 0.16f - up * 0.07f;
            p.handAt[1] = Vector3.Lerp(p.handAt[1], lips, Mathf.SmoothStep(0f, 1f, lift));
            p.handW[1] = 1f;
        }

        /// <summary>Their own gear with them: a board under the arm, or skis on the shoulder (as the camp's people carry theirs).</summary>
        static void Gear(Person p, Quaternion rot)
        {
            if (p.Look.gear == PersonGear.Skis)
            {
                // skis on the shoulder; the poles in the left hand, planted on the floor beside them (their tips at the
                // floor: the grip the pole's length up, the hand there)
                p.skisOnShoulder = true;
                Vector3 dir = (Vector3.down + rot * Vector3.left * 0.1f + rot * Vector3.forward * 0.12f).normalized;
                p.polesDir = dir;
                float L = PoleLength();
                p.handAt[0] = p.transform.position + rot * new Vector3(-0.3f * p.Scale, 0f, 0.08f) - dir * (L + 0.01f);
                p.handW[0] = 1f;
            }
            else if (p.Look.gear == PersonGear.Snowboard) { p.held = Person.Held.Placed; p.boardInHand = 1; }
        }

        void PlaceMug(Folk f)
        {
            if (f.mug == null) return;
            bool on = f.p.Lod < 2;
            if (f.mug.gameObject.activeSelf != on) f.mug.gameObject.SetActive(on);
            if (!on) return;
            // in the right hand's grip, upright, its handle out to the side
            var hand = f.p.Rig.Hand[1];
            var fwd = Quaternion.Euler(0f, f.yaw, 0f) * Vector3.forward;
            var at = hand.position + fwd * 0.045f + Vector3.down * 0.06f;
            at.y = Mathf.Max(at.y, f.topY + 0.002f);   // (set down on the table, never into it)
            f.mug.SetPositionAndRotation(at, Quaternion.Euler(0f, f.yaw - 90f, 0f));
        }
    }
}
