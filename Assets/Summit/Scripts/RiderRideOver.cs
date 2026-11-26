using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What the board meets that is not snow: anything low enough is ridden like terrain, and the rest only knocks you
    /// down when it is hit square on.
    ///  - Low (its top no more than StepUp above the board's bottom: a boulder, a talus block, a stump, a crag's foot;
    ///    or anything under a board in the air): no collision at all. It is part of the surface the board rides: the
    ///    board rises smoothly before it, glides over its top and settles back onto the snow behind it (OverLiftStep).
    ///    It never leaves the snow's line for it, is never thrown by it, and the finger steers all the way over.
    ///  - Taller: a crash only when hit within DeadOnAngle of square on (and faster than CrashInto into it). Anything
    ///    glancing is slid along: the part of the speed into its face is taken away (and a little more, GlanceKeep),
    ///    nothing is added, and the board goes on along the face until it is past. Animals still always knock you
    ///    down; invincible, a shield, the tube's bounce and a moment's invulnerability work as before.
    /// The board finds what it is about to meet itself, each step, before it moves (Contact): it never sinks into a
    /// thing and is pushed back out by the physics engine, which is what a sideways kick was.
    /// </summary>
    public partial class Rider
    {
        /// <summary>How far above the board's bottom an obstacle's top may be for the board to ride up onto it or over it.</summary>
        public static float StepUp = 0.6f;
        /// <summary>Hit within this of square on (degrees between the way you go and straight into its face): a crash.</summary>
        public const float DeadOnAngle = 28f;
        public static readonly float DeadOnCos = Mathf.Cos(DeadOnAngle * Mathf.Deg2Rad);
        /// <summary>Faster than this into a face (m/s), square on, it knocks you down (as before: slower is a scrape).</summary>
        public const float CrashInto = 6f;
        /// <summary>A glancing blow: this share of the speed kept, the part into the face taken away.</summary>
        public const float GlanceKeep = 0.88f;

        /// <summary>The last solid met: how far off square on the board's travel was to its face (degrees, 0 dead on).</summary>
        public float LastHitAngle { get; private set; }
        /// <summary>A glancing blow off something solid: how fast into it (m/s).</summary>
        public System.Action<float> Glanced;
        /// <summary>Riding over something low: how high it lifts the board over the snow.</summary>
        public System.Action<float> Hopped;

        // the low obstacles near the board, which it does not collide with (Physics.IgnoreCollision) and rides on instead
        List<Collider> over = new List<Collider>(8), overNow = new List<Collider>(8);
        readonly Collider[] overNear = new Collider[48];

        /// <summary>Ridden over, not hit: the board's sphere ignores it and its top is ground.</summary>
        public bool RidesOver(Collider c) => c != null && over.Contains(c);
        /// <summary>The test tools: the low things the board is riding over or about to.</summary>
        public IReadOnlyList<Collider> RidingOver => over;

        /// <summary>
        /// Low enough to ride up onto or over: a solid Obstacle (not an animal, nor a roof's solid, which Roofs rides)
        /// that stands no more than StepUp out of the snow (StandsOut), plus however high the board is over its own
        /// snow now (in the air, or up on another one). (Not against the board's own height: on a steep slope a
        /// boulder down the hill is far below the board, however tall it is.)
        /// </summary>
        public static bool Low(Collider c, float above)
        {
            if (c == null || !c.enabled || c.isTrigger) return false;
            var ob = c.GetComponentInParent<Obstacle>();
            if (ob == null || ob.always || ob.roof) return false;
            return StandsOut(c) <= StepUp + Mathf.Max(0f, above);
        }

        // how far each collider stands out of the snow, worked out once for where it is (Forest moves its pooled
        // colliders from tree to tree and rock to rock: a new box, worked out again)
        static readonly Dictionary<Collider, (Bounds b, float h)> standing = new Dictionary<Collider, (Bounds, float)>();

        /// <summary>
        /// How far a collider stands out of the snow at its highest, square to the snow (straight up, on a steep slope,
        /// it would count the slope's own fall across it): its surface over the snow under it, sampled over its
        /// footprint (a quick answer first from its box: a sunk, tilted rock's box reaches well past its top).
        /// </summary>
        public static float StandsOut(Collider c)
        {
            var b = c.bounds;
            if (standing.TryGetValue(c, out var known) && known.b == b) return known.h;
            if (standing.Count > 4096) standing.Clear();
            float lowSnow = float.PositiveInfinity, highSnow = float.NegativeInfinity;
            for (int i = 0; i < 5; i++)
            {
                float x = i == 4 ? b.center.x : (i & 1) == 0 ? b.min.x : b.max.x, z = i == 4 ? b.center.z : (i & 2) == 0 ? b.min.z : b.max.z;
                float y = Slope.DrawnHeight(x, z);
                lowSnow = Mathf.Min(lowSnow, y); highSnow = Mathf.Max(highSnow, y);
            }
            float h;
            if (b.max.y - lowSnow <= 0.15f) h = b.max.y - lowSnow;                 // low even over the lowest snow by it
            else if (b.max.y - highSnow > 6f) h = b.max.y - highSnow;              // tall even over the highest: no need to look
            else
            {
                h = float.NegativeInfinity;
                const int N = 5;
                for (int i = 0; i < N; i++)
                    for (int j = 0; j < N; j++)
                    {
                        float x = Mathf.Lerp(b.min.x, b.max.x, (i + 0.5f) / N), z = Mathf.Lerp(b.min.z, b.max.z, (j + 0.5f) / N);
                        if (!c.Raycast(new Ray(new Vector3(x, b.max.y + 0.1f, z), Vector3.down), out var hit, b.size.y + 0.2f)) continue;
                        h = Mathf.Max(h, (hit.point.y - Slope.DrawnHeight(x, z)) * Slope.Normal(x, z).y);   // (square to the snow, as it stands out of it)
                    }
                if (float.IsNegativeInfinity(h)) h = b.max.y - highSnow;
            }
            standing[c] = (b, h);
            return h;
        }

        /// <summary>
        /// Each step, before the board moves: which low things are near enough to meet. The board does not collide
        /// with them (they are part of the ground it rides: OverLiftStep) and collides with the rest as ever.
        /// </summary>
        void OverStep(Vector3 p, Vector3 v)
        {
            float bottom = p.y - R - Lift;
            float above = bottom - Slope.DrawnHeight(p.x, p.z);   // (over the snow: in the air, or up on a rock)
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float sp = flat.magnitude;
            float reach = R + 2f + sp * 0.35f;
            int k = Physics.OverlapSphereNonAlloc(p + flat * 0.15f, reach, overNear, ~0, QueryTriggerInteraction.Ignore);
            overNow.Clear();
            for (int i = 0; i < k; i++)
            {
                var c = overNear[i];
                if (c.attachedRigidbody != null && c.attachedRigidbody == rb) continue;
                if (Low(c, above)) overNow.Add(c);
            }
            for (int i = 0; i < over.Count; i++)
                if (over[i] != null && !overNow.Contains(over[i])) Physics.IgnoreCollision(ownCol, over[i], false);
            for (int i = 0; i < overNow.Count; i++)
                if (!over.Contains(overNow[i])) Physics.IgnoreCollision(ownCol, overNow[i], true);
            (over, overNow) = (overNow, over);
        }

        // how far the low things under and ahead of the board lift it over the snow now (m), and whether it is on its
        // way over one (for Hopped, once a thing)
        float overLift, overLiftVel; bool overRiding;
        /// <summary>How long before a low thing the board starts to rise for it, and after it to come down (s of
        /// travel; never less than OverLeadMin m nor more than OverLeadMax).</summary>
        public static float OverLead = 0.22f, OverLeadMin = 1.5f, OverLeadMax = 6f;
        /// <summary>The fastest the lift may change (m/s): only a limit on what the rise itself never reaches (a rock
        /// come down beside, a thing that moves).</summary>
        public static float OverLiftRate = 7f, OverLiftEase = 0.04f;
        /// <summary>How high the board is carried over the snow by what it is riding over (m): the test tools.</summary>
        public float OverLift => overLift;

        /// <summary>
        /// Riding over low things, smoothly: how far the board is lifted over the snow at (x, z), going along d at sp.
        /// Each thing's top, as far as it stands out of the snow, is spread along the board's line into a rise that
        /// begins OverLead before it, is level over it and falls away as gently after: the board glides up and over
        /// (the rider's legs take it), it does not leave the snow's line, is not thrown, and steers all the way.
        /// </summary>
        void OverLiftStep(float dt, float x, float z, Vector2 d, float sp, bool onRoof)
        {
            float target = 0f;
            if (over.Count > 0 && !onRoof)
            {
                float lead = Mathf.Clamp(sp * OverLead, OverLeadMin, OverLeadMax);
                float sx = d.y * 0.22f, sz = -d.x * 0.22f;   // (the board's width: a rock clipped by an edge is ridden over too)
                // (looked at on marks fixed to the ground along the line, not to the board: sampled at points that moved
                // with it, each step read the same rock a little differently, and the lift shook)
                const float Mark = 0.2f;
                float along = x * d.x + z * d.y;
                for (float s = Mathf.Ceil((along - lead) / Mark) * Mark - along; s <= lead; s += Mark)
                {
                    float c = Mathf.Cos(0.5f * Mathf.PI * Mathf.Abs(s) / lead), w = c * c;
                    if (w * (StepUp + 0.6f) <= target) continue;   // (nothing there could stand higher than what is found already)
                    float px = x + d.x * s, pz = z + d.y * s;
                    float top = Mathf.Max(OutOfSnow(px, pz), Mathf.Max(OutOfSnow(px + sx, pz + sz), OutOfSnow(px - sx, pz - sz)));
                    if (top * w > target) target = top * w;
                }
            }
            if (target > 0.08f && !overRiding) { overRiding = true; Hopped?.Invoke(target); }
            // (eased: where one thing's rise hands over to the next's the rise has a corner, and the board must not feel it)
            overLift = Mathf.SmoothDamp(overLift, target, ref overLiftVel, OverLiftEase, OverLiftRate, dt);
            if (overRiding && target <= 0.01f && overLift <= 0.01f) overRiding = false;
        }

        /// <summary>How far the low things at (x, z) stand out of the drawn snow there (0: none).</summary>
        float OutOfSnow(float x, float z)
        {
            if (!OverTop(x, z, out float y, out _)) return 0f;
            return Mathf.Max(0f, y - Slope.DrawnHeight(x, z));
        }

        /// <summary>The top of the low things under (x, z), as a board would ride it (-infinity where there are none).</summary>
        float OverHeight(float x, float z) => OverTop(x, z, out float y, out _) ? y : float.NegativeInfinity;

        /// <summary>
        /// The highest low thing under (x, z) and which way its top faces there (a side or rim too steep to ride taken
        /// as level, as the board steps over its edge, so it is never too steep to stand on and never a ramp).
        /// </summary>
        bool OverTop(float x, float z, out float y, out Vector3 n)
        {
            y = float.NegativeInfinity; n = Vector3.up;
            bool any = false;
            for (int i = 0; i < over.Count; i++)
            {
                var c = over[i];
                if (c == null || !c.enabled) continue;
                var b = c.bounds;
                if (x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z) continue;
                float from = b.max.y + 0.1f;
                if (!c.Raycast(new Ray(new Vector3(x, from, z), Vector3.down), out var hit, b.size.y + 0.2f)) continue;
                if (hit.point.y <= y) continue;
                y = hit.point.y; n = hit.normal; any = true;
            }
            if (!any) return false;
            // (a side or a rim too steep to ride, met where the board clips it: level ground, stepped up onto. Leant back
            // part way, it was a ramp's face, and a board clipping a stump's rim at speed was thrown metres into the air)
            if (n.y < 0.8f) n = Vector3.up;
            return true;
        }

        /// <summary>The ground under the board: the snow's, or a low thing's top over it (GroundAt).</summary>
        bool OverGround(float x, float z, float snow, out float y, out Vector3 n)
        {
            n = Vector3.up; y = snow;
            if (over.Count == 0 || !OverTop(x, z, out float oy, out n) || oy <= snow) return false;
            y = oy;
            return true;
        }

        void RideOverReset()
        {
            if (ownCol != null) for (int i = 0; i < over.Count; i++) if (over[i] != null) Physics.IgnoreCollision(ownCol, over[i], false);
            over.Clear(); overNow.Clear(); touched.Clear(); overLift = 0f; overLiftVel = 0f; overRiding = false;
        }

        // the tall things the board is in touch with, and when it last was (a touch is one meeting: its first step
        // decides crash or glance; after that it is only slid along)
        readonly Dictionary<Collider, float> touched = new Dictionary<Collider, float>();
        readonly RaycastHit[] castBuf = new RaycastHit[24];
        float contactClock;
        const float TouchGap = 0.3f;      // s out of touch before meeting the same thing is a new meeting
        const float ContactSkin = 0.04f;  // m the look ahead reaches past the step's own move

        /// <summary>
        /// What this step's move runs into: the board's sphere swept along v for the step. Something tall in the way is
        /// met (Meet): v comes back as the board goes on (slid along its face, bounced off it, or as it was into a
        /// crash). True if it knocked the rider down. The same on the phone and in the test tools.
        /// </summary>
        bool Contact(float dt, Vector3 p, ref Vector3 v)
        {
            contactClock += dt;
            if (!Running || Crashed || rb == null || !rb.detectCollisions) return false;
            // (the test tools' rides of the bare mountain: as ever, only a tool that asks for what the board meets,
            // by TestContacts before the step, has anything in its way)
            if (Application.isBatchMode && !contactsAsked) return false;
            for (int pass = 0; pass < 2; pass++)
            {
                float sp = v.magnitude;
                if (sp < 0.2f) return false;
                Vector3 dir = v / sp;
                int m = Physics.SphereCastNonAlloc(p, R, dir, castBuf, sp * dt + ContactSkin, ~0, QueryTriggerInteraction.Ignore);
                Collider best = null; Obstacle bestOb = null; float bestD = float.PositiveInfinity; Vector3 q = default, nrm = default;
                for (int i = 0; i < m; i++)
                {
                    var c = castBuf[i].collider;
                    if (c == null || c == ownCol || (c.attachedRigidbody != null && c.attachedRigidbody == rb)) continue;
                    if (Physics.GetIgnoreLayerCollision(gameObject.layer, c.gameObject.layer) || (ownCol != null && (ownCol.excludeLayers.value & (1 << c.gameObject.layer)) != 0)) continue;
                    if (RidesOver(c) || castBuf[i].distance >= bestD) continue;
                    var ob = c.GetComponentInParent<Obstacle>();
                    if (ob == null) continue;
                    Vector3 hq = castBuf[i].point, hn = castBuf[i].normal;
                    if (castBuf[i].distance <= 0f)
                    {
                        // already in touch: the nearest point of its surface, and the way out from it
                        hq = Obstacle.Nearest(c, p); hn = p - hq;
                        if (hn.sqrMagnitude < 1e-6f) hn = -dir;
                        if (Vector3.Dot(hn, dir) >= 0f) continue;   // (moving away from it)
                    }
                    best = c; bestOb = ob; bestD = castBuf[i].distance; q = hq; nrm = hn;
                }
                if (best == null) return false;
                Vector3 was = v;
                v = Meet(bestOb, best, nrm, q, v);
                if (Crashed) return true;
                if (v == was) return false;
            }
            return false;
        }

        /// <summary>
        /// The board meets something solid and tall, going at v: `normal` is its face's, out toward the board; `at`
        /// where they meet. Returns the velocity it goes on with.
        /// </summary>
        Vector3 Meet(Obstacle ob, Collider col, Vector3 normal, Vector3 at, Vector3 v)
        {
            if (normal.sqrMagnitude < 1e-8f) normal = -v;
            normal.Normalize();
            if (Vector3.Dot(normal, rb.position - at) < 0f && (rb.position - at).sqrMagnitude > 1e-6f) normal = -normal;
            bool first = !touched.TryGetValue(col, out float last) || contactClock - last > TouchGap;
            touched[col] = contactClock;
            if (touched.Count > 64) touched.Clear();
            float sp = v.magnitude;
            float into = Mathf.Max(0f, -Vector3.Dot(v, normal));
            bool square = sp < 1e-3f || into >= sp * DeadOnCos;
            if (first) LastHitAngle = sp > 1e-3f ? Mathf.Acos(Mathf.Clamp(into / sp, 0f, 1f)) * Mathf.Rad2Deg : 0f;
            if (ob.person != null) return MeetPerson(ob, col, normal, at, v, first);   // (ridercrash: another rider)
            if (ob.always) { into = Mathf.Max(into, 10f); square = true; }   // animals always knock you down
            // invincible: smashed and ridden on through, minus the smash's toll
            if (InvincibleTime > 0f && ob.Smashable && Smash(ob, col)) return v * SmashKeep;
            if (InvincibleTime > 0f && !ob.Smashable)
            {
                // a building, a car, a fence, a lift tower: invincible doesn't go through it. Thrown off it with a
                // good part of the speed gone, still on the board
                Vector3 nb = new Vector3(normal.x, 0f, normal.z);
                if (nb.sqrMagnitude < 1e-4f) nb = -new Vector3(v.x, 0f, v.z);
                nb.Normalize();
                if (Vector3.Dot(v, nb) < 0f) v -= nb * Vector3.Dot(v, nb) * 1.6f;
                invulnerable = Mathf.Max(invulnerable, 0.4f);
                if (first) Scraped?.Invoke(into);
                return v * (first ? 0.6f : 1f);
            }
            if (first) { HitCollider = col; HitPoint = at; HitObstacle?.Invoke(ob, col.bounds.center, into); }
            // glancing (or still in touch after the first meeting, or just back on your feet): slid along its face,
            // never down. (Met from above, on its top, it is a touch, as before: a scrape, or a crash come down on hard)
            bool glancing = !square && normal.y < 0.7f;
            if (!first || invulnerable > 0f || glancing)
            {
                v = Slide(v, normal);
                if (first && glancing)
                {
                    v = new Vector3(v.x * GlanceKeep, v.y, v.z * GlanceKeep);
                    Scraped?.Invoke(Mathf.Max(into, 1f));
                    Glanced?.Invoke(into);
                }
                return v;
            }
            if (Tube && into > 6f && into < 16f && !ob.always)
            {
                // the tube is a big rubber ring: it bounces off, spinning, and you keep going
                invulnerable = 0.5f;
                Vector3 nb = new Vector3(normal.x, 0f, normal.z).normalized;
                if (Vector3.Dot(v, nb) < 0f) v -= nb * Vector3.Dot(v, nb) * 1.7f;
                v *= 0.75f;
                Heading = Mathf.Clamp(Mathf.Atan2(v.x, v.z), -MaxHeading, MaxHeading);
                Bounced?.Invoke(into);
                return v;
            }
            if (into > CrashInto && Shields > 0)
            {
                Shields--;
                invulnerable = 0.9f;
                Vector3 nb = new Vector3(normal.x, 0f, normal.z).normalized;
                if (Vector3.Dot(v, nb) < 0f) v -= nb * Vector3.Dot(v, nb) * 1.6f;
                ShieldHit?.Invoke();
                return v * 0.8f;
            }
            if (into > CrashInto) { DoCrash(ob.label); return v; }
            Scraped?.Invoke(into);
            return Slide(v, normal) * 0.85f;
        }

        /// <summary>Closing on another rider at least this fast (m/s, the two speeds together) is a real hit; slower, a brush you slide past.</summary>
        public const float PersonHitMin = 1.8f;
        /// <summary>The free hit of this run is used (the first rider you hit goes down and you stay up: the warning). Reset each run.</summary>
        public bool FreeHitUsed;
        /// <summary>The free hit was just used: who was hit, where.</summary>
        public System.Action<Obstacle, Vector3> PersonWarned;
        /// <summary>Riders knocked down this run (for the tests).</summary>
        public int PeopleHit;
        int lastPersonId = -1; float lastPersonAt = -9f;

        /// <summary>
        /// (ridercrash) Into another rider. A real hit (closing at PersonHitMin or more, the first touch) knocks them down,
        /// thrown by how fast you met; you: the first time in a run, you stay up, staggered, a third of your speed gone
        /// (the warning); after that, a shield takes it if you have one, or you go down too, as into anything else.
        /// Invincible, you go through. A brush, or still touching after the hit, slides past.
        /// </summary>
        Vector3 MeetPerson(Obstacle ob, Collider col, Vector3 normal, Vector3 at, Vector3 v, bool first)
        {
            var theirs = ob.person.VelocityOf(col);
            var rel = v - theirs;
            float closing = Mathf.Max(0f, -Vector3.Dot(rel, normal));
            // (the first touch of this person: by who they are, not the capsule, which passes from one rider to the next)
            int who = ob.person.IdOf(col);
            first = who != lastPersonId || contactClock - lastPersonAt > 0.6f;
            if (first && closing >= PersonHitMin) { lastPersonId = who; lastPersonAt = contactClock; }
            else if (who == lastPersonId) lastPersonAt = contactClock;
            if (!first || closing < PersonHitMin) return Slide(v, normal);
            // they go down: thrown along the way you met, with some of the speed you had over theirs
            var push = new Vector3(rel.x, 0f, rel.z) * 0.6f - new Vector3(normal.x, 0f, normal.z) * closing * 0.4f;
            ob.person.Knock(col, theirs + push, at);
            PeopleHit++;
            if (InvincibleTime > 0f) return v * 0.9f;
            if (!FreeHitUsed)
            {
                FreeHitUsed = true;
                const float over = 0.25f;
                slowFor = over; slowRate = -Mathf.Log(0.8f) / over;
                wobble = Mathf.Max(wobble, 0.7f);
                invulnerable = Mathf.Max(invulnerable, 0.6f);
                PersonWarned?.Invoke(ob, at);
                // (knocked aside, not stopped: half of the speed into them taken off)
                Vector3 nh = new Vector3(normal.x, 0f, normal.z);
                if (nh.sqrMagnitude > 1e-4f) { nh.Normalize(); float vn = Vector3.Dot(v, nh); if (vn < 0f) v -= nh * vn * 0.5f; }
                return v;
            }
            if (Shields > 0)
            {
                Shields--;
                invulnerable = 0.9f;
                ShieldHit?.Invoke();
                return Slide(v, normal) * 0.8f;
            }
            HitCollider = col; HitPoint = at; HitObstacle?.Invoke(ob, col.bounds.center, closing);
            DoCrash(ob.label);
            return v;
        }

        /// <summary>Along a face: the part of v into it taken away (on the snow, sideways only: the snow has the rest).</summary>
        Vector3 Slide(Vector3 v, Vector3 normal)
        {
            Vector3 nh = new Vector3(normal.x, Airborne ? normal.y : 0f, normal.z);
            if (nh.sqrMagnitude < 1e-4f) nh = normal;
            nh.Normalize();
            float vn = Vector3.Dot(v, nh);
            return vn < 0f ? v - nh * vn : v;
        }

        /// <summary>
        /// Something solid ran into the board (the physics engine's own report: an animal, a chair, anything the step's
        /// look ahead did not see coming). Met by the same rules.
        /// </summary>
        public void Hit(Obstacle ob, Collider col, Vector3 normal, Vector3 at)
        {
            if (!Running || Crashed || ob == null || RidesOver(col)) return;
            if (touched.TryGetValue(col, out float last) && contactClock - last <= TouchGap) return;   // (met already: Contact)
            Vector3 v = lastV.sqrMagnitude > 1e-4f ? lastV : rb.linearVelocity;
            v = Meet(ob, col, normal, at, v);
            if (!Crashed) { rb.linearVelocity = v; lastV = v; }
        }

        /// <summary>The test tools call this before a step for the board to meet what stands in its way on that step
        /// (batchmode never reported collisions, and the tools that ride the bare mountain never asked): the board's own
        /// look ahead (Contact), which on the phone runs every step. True while the rider is down.</summary>
        public bool TestContacts(float dt) { contactsAsked = true; return Crashed; }
        bool contactsAsked;
    }
}
