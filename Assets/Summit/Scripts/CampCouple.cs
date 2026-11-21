using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Val and Breanna at home in the camp. Whoever of them isn't the one being played lives here: walks about,
    /// stops at the lookout, warms up by the fire, goes into the chalet to look out of the French doors, watches the
    /// gate, admires the snowman. They're a couple: when both are free they go about together, holding hands (the
    /// joined hands are reached by each arm's own IK from the walk clip, so the arms follow the bodies), at the same
    /// pace, and at a view they stop side by side and she leans in to him for a moment. When one of them is played,
    /// the other carries on alone and now and then waves at the hero (who is, after all, their other half).
    ///
    /// They are the very models the hero rides as (Resources/Rider/Buddy and Breanna, from Tools/MakeHero.py), on the
    /// same clips (Rider/HeroAnim), dressed the way RiderBodyHero dresses them: their own pyjamas, in whatever colours
    /// the closet last left that set of clothes in. Near the camera they are posed every frame with shadows; further
    /// off every second or fourth frame, with lighter skinning and no shadows; far off not drawn at all.
    ///
    /// Kept to this file on purpose: the camp only makes one (Camp.Build), tells it when it was rebuilt, and lends it
    /// its walking (Camp.StepTo, which goes round the same trees, walls and floors as the hero). A shared way of making
    /// camp people from the hero's model can take over Person.Make later without the rest changing.
    /// </summary>
    public class CampCouple : MonoBehaviour
    {
        /// <summary>Test tools: no frames of its own; the tool calls Step.</summary>
        public static bool Manual;

        Camp camp;
        Camera cam;
        Person val, bri, paul;   // (Paul lives here too, on his own: he isn't part of their couple)
        int seed = 20261120;
        System.Random rnd;
        float clock;
        bool placed;

        public void Setup(Camp c) { camp = c; }

        /// <summary>The camp was rebuilt (another mountain): its spots moved, so they set off again from new ones.</summary>
        public void Rebuilt() { spotsMade = false; placed = false; }

        /// <summary>Test tools: the people, by name ("Val", "Breanna"), for where they are and how they stand.</summary>
        public Transform Of(int who) { var p = Who(who); return p != null && p.shown ? p.root : null; }
        public HeroRig RigOf(int who) { var p = Who(who); return p != null ? p.rig : null; }
        Person Who(int who) => who == 1 ? val : who == 2 ? bri : who == 3 ? paul : null;
        /// <summary>Test tools: holding hands now (0..1), and what each of them is doing.</summary>
        public float Holding => holdW;
        /// <summary>Test tools: where they are in the walk cycle (0..1), and how much they're walking (0 standing, 1 striding).</summary>
        public float PhaseOf(int who) { var p = Who(who); return p != null ? p.phase : 0f; }
        public float WalkOf(int who) { var p = Who(who); return p != null && p.shown ? p.walk : 0f; }
        public string State => (val != null && val.shown ? "Val " + val.doing + " " : "") + (bri != null && bri.shown ? "Breanna " + bri.doing : "") + (together ? " (together)" : "")
                             + (paul != null && paul.shown ? "; Paul " + paul.doing : "");
        /// <summary>Test tools: go to this spot next (by name: Lookout, Fire, Chalet, Gate, Snowman, Stroll).</summary>
        public void Send(string spot) { forced = spot; foreach (var p in new[] { val, bri }) if (p != null) { p.stay = 0f; p.path.Clear(); } }
        /// <summary>Test tools: Paul goes to this spot next.</summary>
        public void SendPaul(string spot) { if (paul == null) return; paulForced = spot; paul.stay = 0f; paul.path.Clear(); }
        string paulForced;
        string forced;
        /// <summary>Test tools: she leans in now (at a stop); the one alone waves now.</summary>
        public void LeanNow() { leanT = 0f; }
        public void WaveNow() { waveCool = 0f; }

        void Update()
        {
            // (only while the hero is in the camp: on a run, at the gate or on the start page they wait where they are)
            if (Manual || camp == null || !camp.Active) return;
            Step(Time.deltaTime);
        }

        // ---------------------------------------------------------------------------------------------- who's free

        /// <summary>Which of them the player is: 1 Val, 2 Breanna, 3 Paul, 0 none (the summit rider).</summary>
        static int Played()
        {
            int r = Progress.Worn(Wardrobe.Cat.Rider);
            return r == 2 ? 1 : r == 3 ? 2 : r == Wardrobe.PaulRow ? 3 : 0;
        }

        bool together;      // walking as a couple (both free, side by side)
        float holdW;        // how firmly the hands are joined (eases in and out)
        float leanT = 99f;  // seconds into her lean on him (at a stop)

        /// <summary>A frame of their lives (public for the test tools, which have no frame time).</summary>
        public void Step(float dt)
        {
            if (camp == null || dt <= 0f) return;
            if (!spotsMade) MakeSpots();
            if (cam == null) cam = Camera.main;
            rnd ??= new System.Random(seed);
            clock += dt;
            int played = Played();
            tick++;
            // a person is made the first time they're free (the one being played isn't loaded until then)
            if (played != 1 && val == null) { val = Person.Make(this, 1); if (val != null && placed) ComeOut(val); }
            if (played != 2 && bri == null) { bri = Person.Make(this, 2); if (bri != null && placed) ComeOut(bri); }
            if (played != 3 && paul == null) { paul = Person.Make(this, 3); if (paul != null && placed) ComeOut(paul); }
            all[0] = val; all[1] = bri; all[2] = paul;
            if (!placed) { Place(played); placed = true; }
            // the one being played isn't here (they're the hero); the one who has just stopped being played comes
            // out of the chalet
            Show(val, played != 1);
            Show(bri, played != 2);
            Show(paul, played != 3);
            bool both = val != null && bri != null && val.shown && bri.shown;
            if (!both) { together = false; joiner = null; }
            else if (!together)
            {
                joiner ??= val;
                // the one who just came out walks over to the other; together again once at their side
                if (Flat(val.pos - Slot(val, bri, 1)).magnitude < 0.35f && Flat(bri.pos - Slot(val, bri, 2)).magnitude < 0.5f)
                {
                    // her plan is the couple's: she takes on the plan of whoever was waiting
                    var lead = joiner == bri ? val : bri;
                    if (lead != bri) { bri.path.Clear(); bri.path.AddRange(lead.path); bri.at = lead.at; bri.stay = lead.stay; }
                    together = true; joiner = null; midSet = false;
                }
            }
            if (together) Couple(dt);
            else
            {
                if (val != null && val.shown) Alone(val, dt, joiner == val ? bri : null, joiner != null && joiner != val);
                if (bri != null && bri.shown) Alone(bri, dt, joiner == bri ? val : null, joiner != null && joiner != bri);
            }
            // together, they're posed on the same frames (by the nearer one's distance), so their joined hands agree
            int every = 0;
            if (together && cam != null) every = Every(Mathf.Min(Vector3.Distance(cam.transform.position, val.pos), Vector3.Distance(cam.transform.position, bri.pos)));
            if (paul != null && paul.shown) Alone(paul, dt, null, false);
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].shown) Pose(all[i], dt, all[i] == paul ? 0 : every);
            if (together && val.posed && bri.posed) Hands(dt); else if (!together) holdW = 0f;
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].shown && all[i].posed) Extras(all[i], dt);
            if (waver != null && !waver.shown) { waver = null; wave = 0f; }
            waveCool -= dt;
        }
        int tick;
        Person joiner;   // the one walking over to join the other
        readonly Person[] all = new Person[3];   // Val, Breanna, Paul (kept, not made afresh each frame)

        static int Every(float dist) => dist < 30f ? 1 : dist < 70f ? 2 : 4;

        void Show(Person p, bool on)
        {
            if (p == null || p.shown == on) return;
            p.shown = on;
            p.root.gameObject.SetActive(on);
            if (on && placed) ComeOut(p);
        }

        /// <summary>Back from being the hero: out through the chalet's door, then over to the other if they're about.</summary>
        void ComeOut(Person p)
        {
            if (p == paul)
            {
                var c = spots["Chalet"];
                p.pos = c.at; p.y = c.at.y; p.yaw = p.yawNow = p.lastYaw = c.yaw + Mathf.PI; p.speed = p.vel = 0f;
                p.path.Clear(); p.path.AddRange(Exit("Chalet")); p.at = null; p.stay = 0f; p.doing = "out of the chalet";
                return;
            }
            var s = spots["Chalet"];
            p.pos = s.at; p.y = s.at.y; p.yaw = p.yawNow = p.lastYaw = s.yaw + Mathf.PI; p.speed = p.vel = 0f;
            p.path.Clear(); p.path.AddRange(Exit("Chalet")); p.at = null; p.stay = 0f; p.doing = "out of the chalet";
            var other = p == val ? bri : val;
            if (other != null && other.shown) joiner = p;
            together = false;
        }

        void Place(int played)
        {
            // they start the day at the lookout together, or apart: one at the lookout, one by the fire
            var s = spots["Lookout"];
            if (val != null) { val.pos = s.at - Right(s.yaw) * Gap; val.yaw = s.yaw; val.at = "Lookout"; val.stay = 6f; }
            if (bri != null) { bri.pos = s.at + Right(s.yaw) * Gap; bri.yaw = s.yaw; bri.at = "Lookout"; bri.stay = 6f; }
            together = played != 1 && played != 2 && val != null && bri != null;
            // Paul starts his day by the gate, watching who drops in
            if (paul != null) { var g = spots["Gate"]; paul.pos = g.at + Right(g.yaw) * 1.6f; paul.yaw = g.yaw; paul.at = "Gate"; paul.stay = 4f; }
            foreach (var p in new[] { val, bri, paul }) if (p != null) { p.pos.y = camp.StepTo(p.pos + Vector3.up * 0.5f, p.pos, 0.3f).y; p.y = p.pos.y; p.yawNow = p.lastYaw = p.yaw; }
        }

        // ---------------------------------------------------------------------------------------------- the camp's spots

        // The places they go are built from a short list of named points, so a change to the camp's buildings is a
        // change to this list (or a call to Repoint from whoever builds the new one), not to the rest of this file.
        // Each point is in the camp's frame (dx from the run's centre, z; downhill is +z) or in the chalet's own
        // (x, z, its front door on +z). The chalet's floor heights and the lookout's deck come from the camp's floors.
        enum In { Camp, Chalet }
        struct Pt { public string name; public In frame; public float x, z; public Pt(string n, In f, float x, float z) { name = n; frame = f; this.x = x; this.z = z; } }
        static readonly Pt[] Points =
        {
            new Pt("LookoutDeck", In.Camp, 16f, -54.4f),       // on the lookout's deck, the rail in front
            new Pt("LookoutBack", In.Camp, 16f, -58.4f),       // the deck's open back, where you step up
            new Pt("LookoutWay", In.Camp, 11.5f, -59.5f),      // round to the back from the camp
            new Pt("LookoutView", In.Camp, 16f, 6f),           // down the mountain
            new Pt("Fire", In.Camp, 0f, -71f),
            new Pt("FireStand", In.Camp, -2.5f, -68.1f),       // the fire's open front corner (the log seats stay free)
            new Pt("FireWay", In.Camp, -3.4f, -65.8f),
            new Pt("ChaletFront", In.Chalet, 0f, 13.7f),       // out in front of the steps
            new Pt("ChaletSteps", In.Chalet, 0f, 11.2f),       // the foot of the steps
            new Pt("ChaletTerrace", In.Chalet, 0f, 7.3f),
            new Pt("ChaletDoor", In.Chalet, 0f, 5.2f),         // just inside the open French doors
            new Pt("ChaletWindow", In.Chalet, -3.4f, 3.9f),    // at the next doors along, looking out (the kitchen and the stove's corner stay free)
            new Pt("ChaletView", In.Chalet, -3.4f, 34f),
            new Pt("Gate", In.Camp, 0f, Slope.GateZ),
            new Pt("GateWatch", In.Camp, -7f, Slope.GateZ - 9f),   // to one side behind the gate, out of the hero's way
            new Pt("Snowman", In.Camp, 11.5f, -61f),
            new Pt("SnowmanStand", In.Camp, 11.2f, -63.6f),    // in front of it (it looks back toward the chalet)
            new Pt("SnowmanWay", In.Camp, 8f, -64.5f),
            new Pt("Stroll", In.Camp, 6f, -66f),               // a turn round the open snow between the fire and the lookout
            new Pt("StrollA", In.Camp, -6f, -58f),
            new Pt("StrollB", In.Camp, 2f, -53f),
            new Pt("StrollView", In.Camp, 30f, -30f),
        };

        /// <summary>
        /// A visit: where to stand (the middle of the couple), what to face, the way in (walked first, so they come up
        /// the lookout's open back and the chalet's steps) and the way out, how long they stay, and what they do there.
        /// </summary>
        class Visit { public string stand, face; public float faceUp; public string[] via, exit; public float min, max; public bool view, warm; }
        static readonly Dictionary<string, Visit> Visits = new Dictionary<string, Visit>
        {
            ["Lookout"] = new Visit { stand = "LookoutDeck", face = "LookoutView", via = new[] { "LookoutWay", "LookoutBack" }, exit = new[] { "LookoutBack" }, min = 14f, max = 22f, view = true },
            ["Fire"] = new Visit { stand = "FireStand", face = "Fire", faceUp = 0.5f, via = new[] { "FireWay" }, min = 12f, max = 18f, warm = true },
            ["Chalet"] = new Visit { stand = "ChaletWindow", face = "ChaletView", faceUp = 1.2f, via = new[] { "ChaletFront", "ChaletSteps", "ChaletTerrace", "ChaletDoor" },
                                     exit = new[] { "ChaletDoor", "ChaletTerrace", "ChaletSteps", "ChaletFront" }, min = 14f, max = 22f, view = true },
            ["Gate"] = new Visit { stand = "GateWatch", face = "Gate", faceUp = 2.5f, min = 10f, max = 15f, view = true },
            ["Snowman"] = new Visit { stand = "SnowmanStand", face = "Snowman", faceUp = 1.6f, via = new[] { "SnowmanWay" }, min = 6f, max = 10f, view = true },
            ["Stroll"] = new Visit { stand = "Stroll", face = "StrollView", via = new[] { "StrollA", "StrollB" }, min = 3f, max = 6f },
        };
        static readonly string[] Places = { "Lookout", "Fire", "Chalet", "Gate", "Snowman", "Stroll" };

        /// <summary>
        /// Put a named point somewhere else in the world (a new building's terrace or room, say): it wins over the
        /// list from then on (call it again after the camp is rebuilt, as the building is). Names: see Points.
        /// </summary>
        public void Repoint(string name, Vector3 world) { moved[name] = world; spotsMade = false; }

        /// <summary>Where one of them is going (or staying), if anywhere: 1 Val, 2 Breanna, 3 Paul (for CampLife's spots).</summary>
        public Vector3? GoingTo(int who) { var p = Who(who); return p != null && p.shown && p.at != null && spots.TryGetValue(p.at, out var s) ? s.at : (Vector3?)null; }
        readonly Dictionary<string, Vector3> moved = new Dictionary<string, Vector3>();

        Vector3 Point(string name)
        {
            if (moved.TryGetValue(name, out var w)) return w;
            foreach (var p in Points)
                if (p.name == name) return p.frame == In.Chalet ? camp.ChaletPoint(p.x, 0f, p.z) : Camp.GroundAt(p.x, p.z);
            Debug.LogWarning("CampCouple: no point called " + name);
            return Camp.GroundAt(0f, -60f);
        }

        /// <summary>A visit made concrete: where its points are on this mountain.</summary>
        class Spot { public Vector3 at; public float yaw; public Vector3[] via, exit; public Vector3 look; public float min, max; public bool view, warm; }
        readonly Dictionary<string, Spot> spots = new Dictionary<string, Spot>();
        bool spotsMade;

        void MakeSpots()
        {
            spotsMade = true;
            spots.Clear();
            Vector3[] Pts(string[] ns) { var r = new Vector3[ns != null ? ns.Length : 0]; for (int i = 0; i < r.Length; i++) r[i] = Point(ns[i]); return r; }
            foreach (var kv in Visits)
            {
                var v = kv.Value;
                Vector3 at = Point(v.stand), look = Point(v.face) + Vector3.up * v.faceUp;
                spots[kv.Key] = new Spot { at = at, yaw = Mathf.Atan2(look.x - at.x, look.z - at.z), via = Pts(v.via), exit = Pts(v.exit), look = look, min = v.min, max = v.max, view = v.view, warm = v.warm };
            }
        }

        /// <summary>The way back out of where they are before going anywhere else (the chalet's door and steps; the lookout's back).</summary>
        IEnumerable<Vector3> Exit(string at) => at != null && spots.TryGetValue(at, out var s) ? s.exit : new Vector3[0];

        string Pick(string now, bool useForced = true)
        {
            if (useForced && forced != null) { var f = forced; forced = null; return f; }
            // by night the fire and the chalet's warm room win; by day the views
            float n = camp.Night;
            float[] w = { 3f - 2f * n, 2f + 3f * n, 1.5f + 2.5f * n, 2f - n, 1.5f - n, 1.5f };
            float sum = 0f;
            for (int i = 0; i < w.Length; i++) if (Places[i] != now) sum += Mathf.Max(0.05f, w[i]);
            float r = (float)rnd.NextDouble() * sum;
            for (int i = 0; i < w.Length; i++)
            {
                if (Places[i] == now) continue;
                r -= Mathf.Max(0.05f, w[i]);
                if (r <= 0f) return Places[i];
            }
            return "Stroll";
        }

        // ---------------------------------------------------------------------------------------------- going about

        const float Gap = 0.31f;           // half the distance between them, side by side (m)
        const float Pace = 1.05f, PaceAlone = 1.15f;   // a stroll together; a little brisker alone (m/s)

        static Vector3 Fwd(float yaw) => new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        static Vector3 Right(float yaw) => new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Where each of them belongs beside the other (Val on her left, so his right hand holds her left).</summary>
        Vector3 Slot(Person v, Person b, int who)
        {
            var mid = (v.pos + b.pos) * 0.5f; float yaw = b.yaw;
            return who == 1 ? mid - Right(yaw) * Gap : mid + Right(yaw) * Gap;
        }

        // the couple's middle, and the way it's facing
        Vector3 mid; float midYaw; float midSpeed; bool midSet;

        void Couple(float dt)
        {
            if (!midSet || Flat(mid - (val.pos + bri.pos) * 0.5f).magnitude > 1f) { mid = (val.pos + bri.pos) * 0.5f; midYaw = bri.yaw; midSet = true; }
            // the plan is hers (she keeps it whoever she's with); he walks the same way at her side
            bool walking = Plan(bri, dt);
            val.doing = bri.doing; val.at = bri.at;
            float want = walking ? Pace : 0f;
            Vector3 aim = walking ? bri.path[0] : spots[bri.at].at;
            Vector3 to = Flat(aim - mid);
            float turnTo = walking && to.magnitude > 0.05f ? Mathf.Atan2(to.x, to.z) : spots[bri.at].yaw;
            // a couple turns gently (a sharp turn would have the inside one walking backwards)
            if (walking) turnTo = RoundHero(mid, midYaw, turnTo, 0.6f);
            midYaw = Mathf.MoveTowardsAngle(midYaw * Mathf.Rad2Deg, turnTo * Mathf.Rad2Deg, dt * (walking ? 70f : 55f)) * Mathf.Deg2Rad;
            // slow for a corner, and to a stop on the spot
            float facing = Mathf.Abs(Mathf.DeltaAngle(midYaw * Mathf.Rad2Deg, turnTo * Mathf.Rad2Deg));
            // (never quite to a standstill for a turn: a couple walks round in an arc rather than pivoting on the spot)
            if (walking) want *= Mathf.Clamp(1.2f - facing / 70f, 0.4f, 1f) * Mathf.Clamp01(to.magnitude / 0.8f + 0.25f);
            midSpeed = Mathf.MoveTowards(midSpeed, want, dt * 1.4f);
            if (walking) mid += Fwd(midYaw) * midSpeed * dt;
            else mid = Vector3.MoveTowards(mid, new Vector3(aim.x, mid.y, aim.z), dt * 0.5f);
            if (walking && Flat(aim - mid).magnitude < 0.45f) { bri.path.RemoveAt(0); if (bri.path.Count == 0) Arrive(bri); }
            Move(val, mid - Right(midYaw) * Gap, midYaw, dt);
            Move(bri, mid + Right(midYaw) * Gap, midYaw, dt);
            // the middle stays between them: if something held them back (a post, a wall), it waits for them
            var real = (val.pos + bri.pos) * 0.5f;
            var off = Flat(mid - real);
            if (off.magnitude > 0.2f) mid = real + new Vector3(off.x, 0f, off.z) * (0.2f / off.magnitude);
            mid.y = real.y;
            // at a view, now and then she rests against him for a few seconds
            if (!walking && spots[bri.at].view)
            {
                if (leanT > 30f && bri.stay < spots[bri.at].max - 3f && bri.stay > 5f && rnd.NextDouble() < dt * 0.5f) leanT = 0f;
            }
            leanT += dt;
        }

        /// <summary>Alone (or on the way back to the other): the same plans, at their own pace.</summary>
        void Alone(Person p, float dt, Person other, bool waiting)
        {
            if (other != null && p.path.Count == 0)
            {
                // joining the other (once out of where they were): to their side, wherever they are, a little quicker
                var slot = Slot(p.who == 1 ? p : other, p.who == 1 ? other : p, p.who);
                var d = Flat(slot - p.pos);
                float yaw = d.magnitude > 0.3f ? Mathf.Atan2(d.x, d.z) : other.yaw;
                p.speed = Mathf.MoveTowards(p.speed, d.magnitude > 0.3f ? PaceAlone * 1.25f * Mathf.Clamp01(d.magnitude / 1.2f + 0.2f) : 0f, dt * 1.6f);
                Move(p, p.pos + Fwd(p.yawNow) * p.speed * dt + (d.magnitude < 0.3f ? d * Mathf.Min(1f, dt * 3f) : Vector3.zero), yaw, dt);
                p.doing = "joining";
                return;
            }
            if (other != null)
            {
                // still on the way out (the chalet's door)
                var to0 = Flat(p.path[0] - p.pos);
                p.speed = Mathf.MoveTowards(p.speed, PaceAlone, dt * 1.6f);
                Move(p, p.pos + Fwd(p.yawNow) * p.speed * dt, Mathf.Atan2(to0.x, to0.z), dt);
                if (to0.magnitude < 0.4f) p.path.RemoveAt(0);
                return;
            }
            bool walking = Plan(p, dt);
            Vector3 aim = walking ? p.path[0] : spots[p.at].at;
            var to = Flat(aim - p.pos);
            float turnTo = walking && to.magnitude > 0.05f ? Mathf.Atan2(to.x, to.z) : spots[p.at].yaw;
            if (walking) turnTo = RoundHero(p.pos, p.yawNow, turnTo);
            if (wave > 0f && waver == p) turnTo = Mathf.Atan2(hero.x - p.pos.x, hero.z - p.pos.z);   // (turned to the hero to wave)
            float facing = Mathf.Abs(Mathf.DeltaAngle(p.yawNow * Mathf.Rad2Deg, turnTo * Mathf.Rad2Deg));
            float want = walking ? PaceAlone * Mathf.Clamp01(1.2f - facing / 80f) * Mathf.Clamp01(to.magnitude / 0.8f + 0.25f) : 0f;
            if (waiting) want *= 0.5f;   // (the other is on the way over: an easy pace, to be caught up)
            p.speed = Mathf.MoveTowards(p.speed, want, dt * 1.6f);
            Vector3 next = walking ? p.pos + Fwd(p.yawNow) * p.speed * dt : Vector3.MoveTowards(p.pos, new Vector3(aim.x, p.pos.y, aim.z), dt * 0.5f);
            Move(p, next, turnTo, dt);
            if (walking && to.magnitude < 0.4f) { p.path.RemoveAt(0); if (p.path.Count == 0) Arrive(p); }
        }

        /// <summary>
        /// The hero standing in the way, a couple of metres ahead: bear off to pass beside them (rather than walk into
        /// them and be pushed round, feet slipping).
        /// </summary>
        float RoundHero(Vector3 at, float facing, float want, float wide = 0f)
        {
            if (!camp.Active) return want;
            var h = Flat(camp.WalkerPosition - at);
            float d = h.magnitude;
            if (d > 2.8f + wide || d < 0.05f) return want;
            var f = Fwd(want);
            float ahead = Vector3.Dot(h, f), side = Vector3.Dot(h, Right(want));
            if (ahead < 0f || Mathf.Abs(side) > 0.95f + wide) return want;
            // round whichever side is already the nearer way past
            return want + (side > 0f ? -1f : 1f) * Mathf.Deg2Rad * 55f * Mathf.Clamp01((2.8f + wide - d) / 1.5f + 0.3f);
        }

        /// <summary>Their plan: staying put (false) until it's time to go, then walking the way there (true).</summary>
        bool Plan(Person p, float dt)
        {
            if (p.path.Count > 0) return true;
            p.stay -= dt;
            if (p.stay > 0f && p.at != null) { p.doing = p.at; return false; }
            // off somewhere else: out of where they are, then the way in to the next (Paul, somewhere the couple isn't)
            string next = p == paul ? (paulForced ?? Pick(p.at, false)) : Pick(p.at);
            if (p == paul) { if (paulForced != null) paulForced = null; else if (bri != null && bri.shown && next == bri.at) next = Pick(next, false); }
            // (somewhere free: not where the camp's people are, nor where Paul or the couple is going; CampLife)
            if (CampLife.Current != null && spots.TryGetValue(next, out var sp) && CampLife.Current.Taken(sp.at, p.who)) next = Pick(next, false);
            p.path.Clear();
            if (p.at != null) p.path.AddRange(Exit(p.at));
            p.path.AddRange(spots[next].via);
            p.path.Add(spots[next].at);
            p.at = next; p.doing = "to " + next;
            return true;
        }

        void Arrive(Person p)
        {
            if (p.at == null) { p.stay = 0f; return; }   // (out of the chalet with nowhere yet in mind: off somewhere next)
            var s = spots[p.at];
            p.stay = Mathf.Lerp(s.min, s.max, (float)rnd.NextDouble());
            p.doing = p.at;
        }

        /// <summary>
        /// A step toward where they should be, round whatever is in the way (the camp's own walking), never into the
        /// hero; the walk cycle runs on by the distance actually covered, so the feet never slide.
        /// </summary>
        void Move(Person p, Vector3 want, float yaw, float dt)
        {
            want.y = p.pos.y;
            // out of the hero's way: they step round the walker as round a post
            if (camp.Active)
            {
                var h = Flat(want - camp.WalkerPosition); float m = h.magnitude;
                if (m < 0.75f && m > 1e-4f) want += h / m * (0.75f - m);
            }
            // and round the camp's people, Alyssa, and each other (Paul and the couple; CampLife)
            if (CampLife.Current != null) want = CampLife.Current.Around(p.who, p.pos, want);
            var was = p.pos;
            var to = camp.StepTo(p.pos, want, 0.3f);
            p.pos = new Vector3(to.x, p.pos.y, to.z);
            // up a step or onto the deck over a moment (the camp lifts the walker in one go)
            p.y = Mathf.MoveTowards(p.y, to.y, dt * 2.2f);
            p.pos.y = to.y;
            p.yawNow = Mathf.MoveTowardsAngle(p.yawNow * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, dt * 140f) * Mathf.Deg2Rad;
            var d = Flat(p.pos - was);
            // pushed off their line (along a wall, round a post): they turn the way they're actually going, so the
            // feet step that way instead of sliding sideways under a forward stride (side by side, they keep facing together)
            if ((!together || p == paul) && d.magnitude > dt * 0.25f && Vector3.Angle(d, Fwd(p.yawNow)) > 35f)
                p.yawNow = Mathf.MoveTowardsAngle(p.yawNow * Mathf.Rad2Deg, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, dt * 220f) * Mathf.Deg2Rad;
            // (stepping round on the spot or sideways counts as walking too: feet that move with the body don't slide)
            p.moved = d.magnitude;
            p.vel = Mathf.Lerp(p.vel, dt > 0f ? d.magnitude / dt : 0f, 1f - Mathf.Exp(-dt * 8f));
            p.yaw = p.yawNow;
            // stuck against something (a rail, a wall): try the way round after a moment
            p.stuck = p.vel < 0.1f && p.path.Count > 0 ? p.stuck + dt : 0f;
            if (p.stuck > 2.5f && p.path.Count > 0)
            {
                p.stuck = 0f;
                var side = Right(p.yawNow) * ((rnd.NextDouble() < 0.5) ? -1.6f : 1.6f);
                p.path.Insert(0, p.pos + side - Fwd(p.yawNow) * 0.6f);
                Stuck++;
            }
        }
        /// <summary>Test tools: how many times someone had to find a way round.</summary>
        public int Stuck;

        // ---------------------------------------------------------------------------------------------- posing

        void Pose(Person p, float dt, int every)
        {
            p.posed = false;
            // how often (and how well) they're posed: every frame near the camera, less often further off
            float dist = cam != null ? Vector3.Distance(cam.transform.position, p.pos) : 0f;
            bool far = dist > 180f;
            if (p.drawn != !far) { p.drawn = !far; foreach (var r in p.skins) r.enabled = !far; }
            p.root.SetPositionAndRotation(new Vector3(p.pos.x, p.y, p.pos.z), Quaternion.Euler(0f, p.yawNow * Mathf.Rad2Deg, 0f));
            // the walk cycle, by distance walked (a cycle is two steps)
            p.phase = Mathf.Repeat(p.phase + p.moved / p.stride + Mathf.Abs(Mathf.DeltaAngle(p.lastYaw * Mathf.Rad2Deg, p.yawNow * Mathf.Rad2Deg)) * 0.0015f, 1f);
            p.lastYaw = p.yawNow;
            float walkW = Mathf.Clamp01(p.vel / 0.75f);
            p.walk = Mathf.MoveTowards(p.walk, walkW, dt * 3f);
            if (far) return;
            if (every <= 0) every = Every(dist);
            p.acc += dt;
            if (tick % every != 0 && !Manual) return;
            float pdt = p.acc; p.acc = 0f;
            p.posed = true;
            var q = dist < 25f ? SkinQuality.Bone4 : SkinQuality.Bone2;
            var sh = dist < 45f ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (q != p.quality || sh != p.shadows) { p.quality = q; p.shadows = sh; foreach (var r in p.skins) { r.quality = q; r.shadowCastingMode = sh; } }
            var h = p.rig;
            h.Begin();
            // standing about: Val alone has his own way (hands in his fleece pocket, rocking on his heels)
            int stand = p.who == 1 && !together && p.idStandVal >= 0 ? p.idStandVal : p.idStand;   // (Paul: the plain stand)
            h.Want(stand, 1f - p.walk);
            h.Want(p.idWalk, p.walk);
            h.At(p.idWalk, p.phase);
            h.Evaluate(pdt);
            // a blink every few seconds (both models have the shape)
            if (p.face != null && p.blink >= 0)
            {
                if (clock >= p.nextBlink) { p.blinkT = 0f; p.nextBlink = clock + 2.2f + 3.3f * (float)rnd.NextDouble(); }
                p.blinkT += pdt;
                p.face.SetBlendShapeWeight(p.blink, 100f * Mathf.Clamp01(1f - Mathf.Abs(p.blinkT / 0.085f - 1f)));
            }
        }

        // where the hero is, and the wave (one at a time, alone, now and then)
        Vector3 hero; float wave, waveCool = 8f; Person waver;

        /// <summary>After the clips: the lean, the look, the warm hands and the wave.</summary>
        void Extras(Person p, float dt)
        {
            var h = p.rig;
            if (!p.drawn) return;
            hero = camp.Active ? camp.WalkerPosition : new Vector3(0f, -9999f, 0f);
            var fw = Fwd(p.yawNow); var rt = Right(p.yawNow);
            bool stopped = p.path.Count == 0 && p.at != null && p.walk < 0.3f && p.doing == p.at;
            var s = p.at != null ? spots[p.at] : null;
            // her lean against him at a view: her body tips toward him and her head rests over (his tilts to hers)
            bool inCouple = together && p != paul;
            if (inCouple && stopped)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(leanT / 0.8f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((leanT - 4.5f) / 1f)));
                if (k > 0f)
                {
                    float sgn = p.who == 2 ? 1f : -1f;   // she leans left (to him); he tilts right (to her)
                    float body = p.who == 2 ? 5f : 1.5f, head = p.who == 2 ? 14f : 7f;
                    Turn(h.Spine, fw, sgn * body * 0.4f * k); Turn(h.Chest, fw, sgn * body * 0.6f * k);
                    Turn(h.Head, fw, sgn * head * k);
                }
            }
            // where they look: the view (or the fire) at a stop, the hero when they pass close or wave, else the clip's own
            float d = Flat(hero - p.pos).magnitude;
            Vector3 lookAt = Vector3.zero; float lw = 0f;
            if (wave > 0f && waver == p) { lookAt = hero + Vector3.up * 1.5f; lw = 1f; }
            else if (d < 4f) { lookAt = hero + Vector3.up * 1.5f; lw = 0.8f * Mathf.Clamp01((4f - d) / 1.5f); }
            else if (stopped && s != null && (s.view || s.warm)) { lookAt = s.look; lw = 0.75f; }
            p.lookW = Mathf.MoveTowards(p.lookW, lw, dt * 1.5f);
            if (lw > 0f) p.lookAt = lookAt;
            if (p.lookW > 0f) Look(p, p.lookAt, p.lookW);
            // hands held out to the fire (the one not holding the other's; both, alone)
            p.warm = Mathf.MoveTowards(p.warm, stopped && s != null && s.warm ? 1f : 0f, dt * 1.2f);
            if (p.warm > 0f)
            {
                for (int side = 0; side < 2; side++)
                {
                    if (inCouple && side == (p.who == 1 ? 1 : 0)) continue;   // that hand is in the other's
                    if (wave > 0f && waver == p && side == p.waveSide) continue;
                    float sx = side == 0 ? -1f : 1f;
                    var chest = h.UpperArm[side].position;
                    // (forearms out to the warmth, the elbows down at the sides: not arms held straight out)
                    var at = chest + fw * 0.30f - Vector3.up * 0.30f - rt * sx * 0.12f + Vector3.up * 0.015f * Mathf.Sin(clock * 3.1f + side);
                    Reach(h, side, at, h.UpperArm[side].position + rt * sx * 0.12f - Vector3.up * 0.5f - fw * 0.1f, p.warm);
                }
            }
            // a wave at the hero, now and then, from the one alone (the hero is their other half)
            if ((!inCouple) && waver == null && waveCool <= 0f && camp.Active && d < 14f && d > 2.5f && InView(p.pos))
            {
                waver = p; wave = 2.2f; waveCool = 18f + 20f * (float)rnd.NextDouble();
                p.waveSide = Vector3.Dot(hero - p.pos, rt) > 0f ? 1 : 0;
            }
            if (waver == p)
            {
                wave -= dt;
                float k = Mathf.Clamp01(Mathf.Min((2.2f - wave) / 0.35f, wave / 0.35f));
                int sd = p.waveSide; float sx = sd == 0 ? -1f : 1f;
                var sh = h.UpperArm[sd].position;
                var at = sh + Vector3.up * 0.30f + rt * sx * (0.22f + 0.08f * Mathf.Sin(clock * 10f)) + fw * 0.10f;
                Reach(h, sd, at, sh + rt * sx * 0.45f - Vector3.up * 0.15f, k);
                if (wave <= 0f) { waver = null; wave = 0f; }
            }
        }

        bool InView(Vector3 at)
        {
            if (cam == null) return true;
            var v = cam.WorldToViewportPoint(at + Vector3.up);
            return v.z > 0f && v.x > 0.08f && v.x < 0.92f && v.y > 0.05f && v.y < 0.95f;
        }

        /// <summary>His right hand in her left: both reach for a point between them, swinging a little as they walk.</summary>
        void Hands(float dt)
        {
            float apart = Flat(val.pos - bri.pos).magnitude;
            // let go if something pushed them apart (round a post), take hands again after
            float want = apart < 0.95f ? 1f : 0f;
            holdW = Mathf.MoveTowards(holdW, want, dt * (want > holdW ? 1.6f : 4f));
            if (holdW <= 0f || !val.drawn) return;
            var a = val.rig.Hand[1].position; var b = bri.rig.Hand[0].position;
            var j = (a + b) * 0.5f;
            var fw = Fwd(midYaw);
            var across = Flat(bri.pos - val.pos).normalized;
            // the joined hands hang between their hips, a little forward, swinging gently with the steps
            float hipY = (val.rig.Hips.position.y + bri.rig.Hips.position.y) * 0.5f;
            j.y = Mathf.Lerp(j.y, hipY - 0.12f, 0.6f);
            j += fw * (0.05f + 0.05f * Mathf.Sin(2f * Mathf.PI * bri.phase) * bri.walk);
            Reach(val.rig, 1, j - across * 0.025f, val.rig.UpperArm[1].position - fw * 0.25f - Vector3.up * 0.45f - across * 0.05f, holdW);
            Reach(bri.rig, 0, j + across * 0.025f, bri.rig.UpperArm[0].position - fw * 0.25f - Vector3.up * 0.45f + across * 0.05f, holdW);
        }

        static void Turn(Transform t, Vector3 axis, float deg) { if (t != null) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation; }

        /// <summary>Turn the head (and a little of the neck) toward a point, no further round than 70 degrees.</summary>
        static void Look(Person p, Vector3 target, float w)
        {
            var h = p.rig;
            if (h.Head == null) return;
            Vector3 face = h.Head.rotation * p.faceLocal, want = target - h.Head.position;
            if (want.sqrMagnitude < 1e-4f) return;
            var all = Quaternion.FromToRotation(face, want.normalized);
            all.ToAngleAxis(out float ang, out var axis);
            if (ang > 180f) ang -= 360f;
            all = Quaternion.AngleAxis(Mathf.Clamp(ang, -70f, 70f) * w, axis);
            if (h.Neck != null) h.Neck.rotation = Quaternion.Slerp(Quaternion.identity, all, 0.35f) * h.Neck.rotation;
            h.Head.rotation = Quaternion.Slerp(Quaternion.identity, all, 0.65f) * h.Head.rotation;
        }

        /// <summary>An arm reaching for a point: two-bone IK, the elbow bent toward the hint, blended in by w.</summary>
        static void Reach(HeroRig h, int s, Vector3 wrist, Vector3 elbowHint, float w)
        {
            Transform upper = h.UpperArm[s], lower = h.LowerArm[s], end = h.Hand[s];
            if (upper == null || lower == null || end == null || w <= 0f) return;
            Vector3 target = Vector3.Lerp(end.position, wrist, w);
            Vector3 sh = upper.position, el = lower.position, wr = end.position;
            float a = (el - sh).magnitude, b = (wr - el).magnitude;
            Vector3 dd = target - sh;
            float c = Mathf.Clamp(dd.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-4f);
            Vector3 dn = dd.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(elbowHint - sh, dn);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(el - sh, dn);
            pole.Normalize();
            float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            Vector3 knee = sh + dn * (cosA * a) + pole * (Mathf.Sqrt(1f - cosA * cosA) * a);
            upper.rotation = Quaternion.FromToRotation(el - sh, knee - sh) * upper.rotation;
            el = lower.position; wr = end.position;
            lower.rotation = Quaternion.FromToRotation(wr - el, sh + dn * c - el) * lower.rotation;
        }

        // ---------------------------------------------------------------------------------------------- the people

        class Person
        {
            public int who;                    // 1 Val, 2 Breanna, 3 Paul
            public Transform root, model;      // root: on the snow, turned the way they face; the model under it
            public HeroRig rig;
            public SkinnedMeshRenderer[] skins;
            public SkinnedMeshRenderer face; public int blink = -1; public float nextBlink, blinkT = 1f;
            public Vector3 faceLocal;          // the way the face looks, in the head bone's frame
            public int idStand, idStandVal, idWalk;
            public float stride;               // metres a walk cycle covers (two steps), measured from the clip
            public bool shown, drawn = true;
            public SkinQuality quality; public UnityEngine.Rendering.ShadowCastingMode shadows;
            public float acc; public bool posed;
            // where they are and what they're doing
            public Vector3 pos; public float y, yaw, yawNow, lastYaw, speed, vel, moved, phase, walk, stuck;
            public readonly List<Vector3> path = new List<Vector3>();
            public string at, doing = "";
            public float stay;
            public Vector3 lookAt; public float lookW, warm; public int waveSide;

            /// <summary>One of them, built from their own model, dressed as the hero is.</summary>
            public static Person Make(CampCouple home, int who)
            {
                var prefab = Resources.Load<GameObject>(who == 1 ? "Rider/Buddy" : who == 2 ? "Rider/Breanna" : "Rider/Paul");
                var table = Resources.Load<TextAsset>("Rider/HeroAnim");
                if (prefab == null || table == null) return null;
                var p = new Person { who = who };
                p.root = new GameObject(who == 1 ? "Val" : who == 2 ? "Breanna" : "Paul").transform;
                p.root.SetParent(home.transform, false);
                var go = Object.Instantiate(prefab, p.root, false);
                go.name = "Model";
                p.model = go.transform;
                foreach (var a in go.GetComponentsInChildren<Animator>()) a.enabled = false;   // posed by HeroRig
                foreach (var c in go.GetComponentsInChildren<Collider>()) Kill(c);
                // (every rider's file comes with their snowboard and its bindings: on foot in the camp it stays at the rack)
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Snowboard") t.gameObject.SetActive(false);
                p.rig = new HeroRig(p.model, table);
                if (!p.rig.Ok) { Kill(p.root.gameObject); return null; }
                var h = p.rig;
                p.idStand = h.Id("Stand"); p.idStandVal = h.Id("Stand_Val"); p.idWalk = h.Id("Walk");
                // the face, in the bind pose (looking straight ahead)
                p.faceLocal = Quaternion.Inverse(h.Head.rotation) * p.model.forward;
                // standing, turned to face along the root's +z, the Root bone (the clips' ground point) on the snow
                h.Begin(); h.Want(p.idStand, 1f); h.Snap(); h.Evaluate(0f);
                Vector3 left = p.root.InverseTransformDirection(h.UpperLeg[0].position - h.UpperLeg[1].position); left.y = 0f;
                var fwd = Vector3.Cross(Vector3.up, left);
                if (fwd.sqrMagnitude > 1e-8f) p.model.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up), 0f) * p.model.localRotation;
                p.faceLocal = Quaternion.Inverse(h.Head.rotation) * p.root.forward;   // (again, now that the model is turned)
                p.model.localPosition -= p.root.InverseTransformPoint(h.Root.position);
                // how far a walk cycle goes: the time a foot is planted (the clip's stance, 60%), and how far it travels
                p.stride = Stride(h, p.idWalk, p.root);
                Dress(p);
                p.skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var r in p.skins)
                {
                    r.updateWhenOffscreen = false;
                    r.quality = SkinQuality.Bone4; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    // in the camp their faces show: goggles up on the beanie, the balaclava down (RiderBodyHero.CampLook)
                    foreach (var nm in CampShapes)
                    {
                        int cs = r.sharedMesh != null ? r.sharedMesh.GetBlendShapeIndex(nm) : -1;
                        if (cs >= 0) r.SetBlendShapeWeight(cs, 100f);
                    }
                    int bs = r.sharedMesh != null ? r.sharedMesh.GetBlendShapeIndex("Blink") : -1;
                    if (bs >= 0) { p.face = r; p.blink = bs; }
                }
                p.quality = SkinQuality.Bone4; p.shadows = UnityEngine.Rendering.ShadowCastingMode.On;
                p.shown = true;
                return p;
            }

            static readonly string[] CampShapes = { "GogglesUp", "MaskDown" };

            static float Stride(HeroRig h, int walk, Transform root)
            {
                if (walk < 0) return 1.2f;
                h.Begin(); h.Want(walk, 1f); h.Snap();
                // the left heel strikes at a quarter; it's planted for 60% of the cycle
                h.At(walk, 0.27f); h.Evaluate(0f); float z0 = root.InverseTransformPoint(h.Foot[0].position).z;
                h.At(walk, 0.81f); h.Evaluate(0f); float z1 = root.InverseTransformPoint(h.Foot[0].position).z;
                float s = (z0 - z1) / 0.54f;
                return s > 0.3f && s < 3f ? s : 1.2f;
            }

            /// <summary>
            /// Their own pyjamas, as RiderBodyHero puts them on the hero: snowy fleece with a pile (hers with the
            /// Nordic knit), the printed bottoms, the painted face, the gear palette; and in the colours the closet
            /// last left that set of clothes in (the first row of each is the pyjamas as made).
            /// </summary>
            static void Dress(Person p)
            {
                int set = p.who == 1 ? 1 : p.who == 2 ? 5 : 3;   // their own clothes (Wardrobe.OwnOutfit): Val's pyjamas, her fleece, his puffer
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                var snowy = new SnowyClothes();
                if (!snowy.Ok) snowy = null;
                Material Cloth(Color c, float smooth, float holds)
                {
                    if (snowy != null) return snowy.Make(c, smooth, holds);
                    var m = new Material(lit); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); return m;
                }
                Material Tex(string path, float smooth)
                {
                    var m = new Material(lit);
                    var t = Resources.Load<Texture2D>(path);
                    if (t != null) { m.SetTexture("_BaseMap", t); m.mainTexture = t; }
                    m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", smooth);
                    if (path.EndsWith("Face")) RiderBody.FaceGlow(m, t);   // (as the hero's: readable in the shade)
                    return m;
                }
                int jacket = Progress.SetWorn(set, Wardrobe.Cat.Jacket), pants = Progress.SetWorn(set, Wardrobe.Cat.Pants), boots = Progress.SetWorn(set, Wardrobe.Cat.Boots);
                Material blue = null, white = null, print = null, plain = null, face = null, jacketM = null, knit = null, hoodie = null;
                Material Fleece(Color c, float holds) { var m = Cloth(c, 0.12f, holds); m.SetFloat("_Fuzz", 1f); return m; }
                // the gear palette (shoes, chain, glasses, hair): the shared picture, or a copy with the shoes dyed
                var gear = new Material(lit);
                var pal = Resources.Load<Texture2D>("Rider/HeroPalette");
                if (pal != null)
                {
                    var tex = pal;
                    if (boots != 0)
                    {
                        try
                        {
                            var px = pal.GetPixels();
                            int cell = set == 1 ? 27 : set == 3 || set == 4 ? 47 : 41, w = pal.width, hh = pal.height;   // sneakers, ski boots, slipper boots (MakeHero.py's PAL)
                            int x = Mathf.FloorToInt((cell % 8 + 0.5f) / 8f * w), y = Mathf.FloorToInt((cell / 8 + 0.5f) / 8f * hh);
                            var c = Wardrobe.Get(Wardrobe.Cat.Boots, boots).a;
                            px[y * w + x] = new Color(c.r, c.g, c.b, px[y * w + x].a);
                            tex = new Texture2D(w, hh, TextureFormat.RGBA32, false) { filterMode = pal.filterMode, wrapMode = TextureWrapMode.Clamp, name = "HeroPalette (camp)" };
                            tex.SetPixels(px); tex.Apply(false);
                        }
                        catch (UnityException) { tex = pal; }
                    }
                    gear.SetTexture("_BaseMap", tex); gear.mainTexture = tex;
                }
                gear.SetColor("_BaseColor", Color.white);
                gear.SetFloat("_Smoothness", 1f);
                gear.SetFloat("_SmoothnessTextureChannel", 1f);
                gear.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                foreach (var smr in p.model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.name.StartsWith("HeroHands")) smr.gameObject.SetActive(true);   // pyjamas: no gloves
                    var mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string n = mats[i] != null ? mats[i].name : "";
                        mats[i] = n.StartsWith("FleeceBlue") ? (blue ??= Fleece(new Color(0.08f, 0.24f, 0.72f), 1.3f))
                                : n.StartsWith("FleeceWhite") ? (white ??= Fleece(new Color(0.93f, 0.90f, 0.82f), 1.5f))
                                : n.StartsWith("PantsPrint") ? (pants != 0 ? (plain ??= Cloth(Color.white, 0.2f, 0.5f)) : (print ??= Tex(set == 2 ? "Rider/BreannaPants" : "Rider/BuddyPants", 0.12f)))
                                : n.StartsWith("FaceSkin") ? (face ??= Tex(p.who == 2 ? "Rider/BreannaFace" : p.who == 3 ? "Rider/PaulFace" : "Rider/BuddyFace", 0.3f))
                                // (Paul's puffer and trousers: snowy cloth the closet's rows dye; his turtleneck, his hoodie)
                                : n.StartsWith("Jacket") ? (jacketM ??= Cloth(Color.black, 0.35f, 0.85f))
                                : n.StartsWith("Pants") ? (plain ??= Cloth(Color.white, 0.2f, 0.5f))
                                : n.StartsWith("Knit") ? (knit ??= Cloth(new Color(0.92f, 0.91f, 0.88f), 0.05f, 1.2f))
                                : n.StartsWith("Hoodie") ? (hoodie ??= Cloth(new Color(0.50f, 0.51f, 0.53f), 0.05f, 1.3f))
                                : gear;
                    }
                    smr.sharedMaterials = mats;
                }
                // the fleece the jacket rows dye (Val's blue half, all of Breanna's), and dyed trousers
                var main = set == 1 ? blue : set == 3 || set == 4 ? jacketM : white;
                if (main != null) Paint(main, Wardrobe.Shown(Wardrobe.Cat.Jacket, jacket, set));
                if (plain != null) Paint(plain, Wardrobe.Shown(Wardrobe.Cat.Pants, pants, set));
                // the snowy cloth's rest frame, and the clothes a little looser, as on the hero
                if (snowy != null)
                {
                    snowy.SetFrame(p.model, p.rig.Hips);
                    foreach (var smr in p.model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (smr.name.Contains("Body") || smr.name.Contains("Legs")) Loosen(smr, snowy, 0.35f);
                }
            }

            static void Paint(Material m, Wardrobe.Item it)
            {
                m.SetColor("_BaseColor", it.a);
                m.SetColor("_Color2", it.b.a > 0f ? it.b : it.a);
                m.SetFloat("_Pattern", (float)it.pattern);
                m.SetFloat("_PatternScale", it.scale);
                m.SetFloat("_PatternAt", it.at);
                if (it.smooth >= 0f && m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", it.smooth);
            }

            /// <summary>The hero's clothes are puffed out a little (RiderBody.Loosen); these are too, so they match.</summary>
            static void Loosen(SkinnedMeshRenderer smr, SnowyClothes snowy, float puff)
            {
                var src = smr.sharedMesh;
                if (src == null || !src.isReadable) return;
                var mesh = Object.Instantiate(src);
                mesh.name = src.name + " (camp)";
                float m = 1f / Mathf.Max(1e-5f, smr.transform.lossyScale.x);
                var v = mesh.vertices; var n = mesh.normals;
                if (n == null || n.Length != v.Length) { mesh.RecalculateNormals(); n = mesh.normals; }
                var sum = new Dictionary<Vector3Int, Vector3>();
                Vector3Int Key(Vector3 q) => new Vector3Int(Mathf.RoundToInt(q.x * 1e5f), Mathf.RoundToInt(q.y * 1e5f), Mathf.RoundToInt(q.z * 1e5f));
                for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); sum[k] = (sum.TryGetValue(k, out var acc) ? acc : Vector3.zero) + n[i]; }
                float baggy = (smr.name.Contains("Body") ? 0.022f : 0.018f) * puff;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 pm = v[i] / m, sn = sum[Key(v[i])].normalized;
                    float loose = 0.55f + 0.45f * Mathf.PerlinNoise(pm.x * 4f + pm.y * 3f + 11f, pm.z * 4f - pm.y * 2f + 5f);
                    v[i] += sn * (baggy * loose * m);
                }
                mesh.vertices = v;
                mesh.RecalculateBounds();
                snowy.Bake(smr, mesh);
                smr.sharedMesh = mesh;
            }

            static void Kill(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
        }

        /// <summary>Keep the walker out of them (Camp.Walk asks): they're solid, as a post would be.</summary>
        public Vector2 PushOut(Vector2 p, float r)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var q = all[i];
                if (q == null || !q.shown) continue;
                var d = p - new Vector2(q.pos.x, q.pos.z);
                float m = d.magnitude, min = r + 0.25f;
                if (m < min && m > 1e-4f) p += d / m * (min - m);
            }
            return p;
        }
    }
}
