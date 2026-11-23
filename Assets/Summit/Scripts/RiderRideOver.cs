using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What the board meets that is not snow: anything low enough is ridden like terrain, and the rest only knocks you
    /// down when it is hit square on.
    ///  - Low (its top no more than StepUp above the board's bottom: a boulder, a talus block, a stump, a crag's foot;
    ///    or anything under a board in the air): no collision at all. Its top is ground (GroundAt): the board rides up
    ///    a gentle side and off its far edge as off any lip; a side too steep to ride up is popped over, the board
    ///    lifted just in time to clear it (or to land on its top), as an ollie would.
    ///  - Taller: a crash only when hit within DeadOnAngle of square on (and faster than CrashInto into it). Anything
    ///    glancing turns the board off along it, a little slower and wobbling (Glance). Animals still always knock you
    ///    down; invincible, a shield, the tube's bounce and a moment's invulnerability work as before.
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
        /// <summary>A side steeper than this (rise over run) is popped over, not ridden up.</summary>
        const float HopSlope = 0.75f;
        /// <summary>Clearance over the top of what is popped over.</summary>
        const float HopClear = 0.12f;

        /// <summary>A glancing blow off something solid: how fast into it (m/s).</summary>
        public System.Action<float> Glanced;
        /// <summary>Popped over something low: how high its top stood over the board.</summary>
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
        /// Each step, before the board moves: which low things are near enough to meet (collisions with them off, the
        /// rest back on), and on the snow, a pop over the next one whose side is too steep to ride up.
        /// </summary>
        Vector3 RideOverStep(float dt, Vector3 p, Vector3 v)
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
            if (over.Count == 0 || Airborne || Grinding || Crashed || sp < 3f) return v;

            // the next steep side on the way: popped over at the moment that puts the top of the jump over its face.
            // (Heights over the snow under each point: the board in the air keeps falling with the slope, so what it
            // has to clear is how far the thing stands out of the snow, less how high it already is)
            Vector3 d = flat / sp;
            float hereTop = OverHeight(p.x, p.z);   // (already up on one: its own top is the ground, not a side)
            float face = -1f, top = 0f;
            float far = sp * 0.4f + R;
            for (float s = R; s <= far; s += 0.2f)
            {
                float x = p.x + d.x * s, z = p.z + d.z * s;
                float y = OverHeight(x, z);
                if (float.IsNegativeInfinity(y)) { if (face >= 0f) break; continue; }
                float snow = Slope.DrawnHeight(x, z);
                float under = float.IsNegativeInfinity(hereTop) ? snow : Mathf.Max(snow, hereTop - (p.y - R - Lift) + snow);
                float rise = y - under;
                if (face < 0f)
                {
                    if (rise < 0.08f) continue;
                    // how steeply it rises out of the snow: from a little before, where it was lower (or not there)
                    float run = Mathf.Clamp(rise / HopSlope, 0.05f, 0.2f);
                    float xb = x - d.x * run, zb = z - d.z * run;
                    float before = OverHeight(xb, zb);
                    float riseBefore = float.IsNegativeInfinity(before) ? 0f : Mathf.Max(0f, before - Slope.DrawnHeight(xb, zb) - (under - snow));
                    if ((rise - riseBefore) / run < HopSlope) return v;   // a side to ride up: the ground does it
                    face = s;
                }
                top = Mathf.Max(top, y - snow - above);
            }
            if (face < 0f) return v;
            float need = top + HopClear;
            float vHop = Mathf.Sqrt(2f * G * need);
            float tTop = vHop / G;   // (the moment it is highest over the snow)
            if (face - R > sp * tTop + 0.05f) return v;   // not yet: popped later, the top of the jump comes at its face
            // (left late, it takes more to clear it in the time there is)
            float t = Mathf.Max(0.02f, (face - R) / sp);
            if (t < tTop) vHop = Mathf.Max(vHop, (need + 0.5f * G * t * t) / t);
            vHop = Mathf.Min(vHop, Mathf.Sqrt(2f * G * (StepUp + HopClear)) * 1.6f);
            Hopped?.Invoke(top);
            Vector3 vt = new Vector3(v.x, v.y - fixVy + vHop, v.z);
            return TakeOff(p, vt, 0.1f);
        }

        /// <summary>The top of the low things under (x, z), as a board would ride it (-infinity where there are none).</summary>
        float OverHeight(float x, float z) => OverTop(x, z, out float y, out _) ? y : float.NegativeInfinity;

        /// <summary>
        /// The highest low thing under (x, z) and which way its top faces there (a steep side leant back toward up, as
        /// the board rides over its edge, so it is never too steep to stand on).
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
            if (n.y < 0.8f) n = Vector3.Slerp(n.y > 0f ? n : Vector3.up, Vector3.up, Mathf.Clamp01((0.8f - n.y) / 0.8f + 0.25f)).normalized;
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
            over.Clear(); overNow.Clear(); testWas.Clear();
        }

        /// <summary>
        /// The board met a solid obstacle (OnCollisionEnter; the test tools: TestContacts). `normal` is the face's,
        /// out toward the board; `at` where they met.
        /// </summary>
        public void Hit(Obstacle ob, Collider col, Vector3 normal, Vector3 at)
        {
            if (!Running || Crashed || ob == null) return;
            if (RidesOver(col)) return;   // (ridden over, not into: see RideOverStep)
            Vector3 vIn = lastV.sqrMagnitude > 1e-4f ? lastV : rb.linearVelocity;
            if (normal.sqrMagnitude < 1e-8f) normal = -vIn;
            normal.Normalize();
            if (Vector3.Dot(normal, rb.position - at) < 0f && (rb.position - at).sqrMagnitude > 1e-6f) normal = -normal;
            float sp = vIn.magnitude;
            float into = Mathf.Max(0f, -Vector3.Dot(vIn, normal));
            bool square = sp < 1e-3f || into >= sp * DeadOnCos;
            if (ob.always) { into = Mathf.Max(into, 10f); square = true; }   // animals always knock you down
            if (InvincibleTime > 0f && ob.Smashable && Smash(ob, col))
            {
                // the step ahead missed it (it came from the side, or the bear ran into you): carry on as if it
                // were never there, minus the smash's toll
                rb.linearVelocity = lastV * SmashKeep;
                return;
            }
            if (InvincibleTime > 0f && !ob.Smashable)
            {
                // a building, a car, a fence, a lift tower: invincible doesn't go through it. Thrown off it with a
                // good part of the speed gone, still on the board
                Vector3 v = lastV.sqrMagnitude > rb.linearVelocity.sqrMagnitude ? lastV : rb.linearVelocity;
                Vector3 nh = new Vector3(normal.x, 0f, normal.z);
                if (nh.sqrMagnitude < 1e-4f) nh = -new Vector3(v.x, 0f, v.z);
                nh.Normalize();
                if (Vector3.Dot(v, nh) < 0f) v -= nh * Vector3.Dot(v, nh) * 1.6f;
                rb.linearVelocity = v * 0.6f;
                Heading = Mathf.Clamp(Mathf.Atan2(v.x, v.z), -MaxHeading, MaxHeading);
                invulnerable = Mathf.Max(invulnerable, 0.4f);
                Scraped?.Invoke(into);
                return;
            }
            HitCollider = col; HitPoint = at;
            HitObstacle?.Invoke(ob, col.bounds.center, into);
            if (invulnerable > 0f) return;
            // glancing: turned off along it, never down (met from above, on its top, it is a touch, as before: a scrape)
            if (!square && normal.y < 0.7f) { Glance(normal, into); return; }
            if (Tube && into > 6f && into < 16f && !ob.always)
            {
                // the tube is a big rubber ring: it bounces off, spinning, and you keep going
                invulnerable = 0.5f;
                Vector3 v = rb.linearVelocity;
                Vector3 nh = new Vector3(normal.x, 0f, normal.z).normalized;
                if (Vector3.Dot(v, nh) < 0f) v -= nh * Vector3.Dot(v, nh) * 1.7f;
                rb.linearVelocity = v * 0.75f;
                Heading = Mathf.Clamp(Mathf.Atan2(v.x, v.z), -MaxHeading, MaxHeading);
                Bounced?.Invoke(into);
                return;
            }
            if (into > CrashInto && Shields > 0)
            {
                Shields--;
                invulnerable = 0.9f;
                Vector3 v = rb.linearVelocity;
                Vector3 nh = new Vector3(normal.x, 0f, normal.z).normalized;
                if (Vector3.Dot(v, nh) < 0f) v -= nh * Vector3.Dot(v, nh) * 1.6f;
                rb.linearVelocity = v * 0.8f;
                ShieldHit?.Invoke();
            }
            else if (into > CrashInto) DoCrash(ob.label);
            else
            {
                Scraped?.Invoke(into);
                rb.linearVelocity *= 0.85f;
            }
        }

        /// <summary>A glancing blow: the part of the speed into the face taken away (and a touch more, off it), a little
        /// of the rest lost, the board turned to its new line and wobbling as you find your feet.</summary>
        void Glance(Vector3 normal, float into)
        {
            Vector3 v = lastV.sqrMagnitude > 1e-4f ? lastV : rb.linearVelocity;
            Vector3 nh = new Vector3(normal.x, Airborne ? normal.y : 0f, normal.z);
            if (nh.sqrMagnitude < 1e-4f) nh = normal;
            nh.Normalize();
            float vn = Vector3.Dot(v, nh);
            if (vn < 0f) v -= nh * vn * 1.15f;
            v = new Vector3(v.x * GlanceKeep, v.y, v.z * GlanceKeep);
            rb.linearVelocity = v; lastV = v;
            if (v.x * v.x + v.z * v.z > 1f) Heading = Mathf.Clamp(Mathf.Atan2(v.x, v.z), -MaxHeading, MaxHeading);
            wobble = Mathf.Max(wobble, Mathf.Clamp(into / 14f, 0.2f, 0.6f));
            Scraped?.Invoke(Mathf.Max(into, 1f));
            Glanced?.Invoke(into);
        }

        // the test tools' stand-in for OnCollisionEnter (batchmode sends no collision messages): what the board's
        // step overlaps, each thing once while it stays in touch
        readonly HashSet<Collider> testWas = new HashSet<Collider>(), testNowSet = new HashSet<Collider>();
        readonly Collider[] testNear = new Collider[32];
        readonly RaycastHit[] testCast = new RaycastHit[32];

        /// <summary>
        /// The test tools: what the physics step's move runs into, handed to Hit as OnCollisionEnter would on the phone
        /// (the face where the board meets it, once a touch). True if it knocked the rider down.
        /// </summary>
        public bool TestContacts(float dt)
        {
            if (!Running || Crashed) { testWas.Clear(); return false; }
            Vector3 p = rb.position, v = rb.linearVelocity;
            int k = Physics.OverlapCapsuleNonAlloc(p, p + v * dt, R + 0.05f, testNear, ~0, QueryTriggerInteraction.Ignore);
            testNowSet.Clear();
            bool down = false;
            for (int i = 0; i < k && !down; i++)
            {
                var c = testNear[i];
                if (c.attachedRigidbody != null && c.attachedRigidbody == rb) continue;
                if ((ownCol != null && Physics.GetIgnoreLayerCollision(gameObject.layer, c.gameObject.layer)) || (ownCol != null && (ownCol.excludeLayers.value & (1 << c.gameObject.layer)) != 0)) continue;
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob == null || RidesOver(c)) continue;
                testNowSet.Add(c);
                if (testWas.Contains(c)) continue;
                if (InvincibleTime > 0f) continue;   // (the step ahead smashes it: SmashAhead)
                // where the board's sphere first touches it on this step's move (the face PhysX would report), or, already
                // in touch, the nearest point of its surface
                Vector3 q = Obstacle.Nearest(c, p), nrm = p - q;
                float len = v.magnitude * dt + 0.05f;
                if (len > 0.06f)
                {
                    int m = Physics.SphereCastNonAlloc(p, R, v.normalized, testCast, len, ~0, QueryTriggerInteraction.Ignore);
                    for (int j = 0; j < m; j++)
                        if (testCast[j].collider == c && testCast[j].distance > 0f) { q = testCast[j].point; nrm = testCast[j].normal; break; }
                }
                if (nrm.sqrMagnitude < 1e-6f) nrm = -v;
                Hit(ob, c, nrm, q);
                down = Crashed;
            }
            testWas.Clear();
            foreach (var c in testNowSet) testWas.Add(c);
            return down;
        }
    }
}
