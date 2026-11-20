using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Throws the rider off the board in a crash: the skeleton becomes a chain of physics bodies
    /// (pelvis, chest, head, upper and lower arms and legs) joined at the bones, launched with the
    /// speed you were carrying, and the board tumbles away on its own. The snow under them is a
    /// slab that follows the slope beneath each one, since the slope itself has no collider.
    /// When the crash is over everything is put back on the board.
    /// </summary>
    public class Ragdoll
    {
        class Part
        {
            public Transform bone; public Rigidbody rb; public Collider col; public Joint joint;
            public Rigidbody parentRb; public Vector3 axis; public float twist, swing1, swing2;   // to rebuild the joint after a sever
            public bool free;          // torn off: its own piece (or a part hanging off one), no longer held to the body
            public bool gone;          // lost in an earlier crash this run: the rider rides on without it, so it stays asleep
                                       // (no collider, no joint) in every later crash until the run is reset (Reattach)
            public Rigidbody slab;     // the snow under a torn-off piece (on its root part only)
        }

        readonly RiderRig rig;
        Transform board, boardHome;
        readonly Collider riderCollider;
        readonly List<Part> parts = new List<Part>();
        readonly PhysicsMaterial snowFeel;
        Rigidbody boardRb; BoxCollider boardCol;
        Rigidbody groundBody, boardGroundBody;
        Transform modelHome;
        Vector3 modelPos, boardPos, boardScl = Vector3.one; Quaternion modelRot, boardRot; Vector3 modelScale;
        Quaternion footRelL, footRelR;
        public bool Active { get; private set; }
        public static float Plough = 2.2f;   // 1/s: how hard snow drags a body sliding through it
        public static float Sink = 0.05f, SinkPowder = 0.07f;   // m: how far the body settles into the snow, groomed and in powder
        /// <summary>Something hit something: impact speed (m/s), the board?, where, and which bone (null for the board).</summary>
        public System.Action<float, bool, Vector3, Transform> Hit;
        /// <summary>A part sliding along the snow, every half metre or so: where, how fast.</summary>
        public System.Action<Vector3, float> Drag;
        /// <summary>A part (or the board: bone null) struck something: what, where, the surface's normal there (out of it, toward the part), and how fast into it (m/s).</summary>
        public System.Action<Collider, Vector3, Vector3, float, Transform> Struck;
        /// <summary>Is this collider the snow under the body (the slab), rather than a tree, rock or sign?</summary>
        public bool IsSnow(Collider c) => c != null && groundBody != null && (c.attachedRigidbody == groundBody || (c.attachedRigidbody != null && limbSlabs.Contains(c.attachedRigidbody)));   // (or a torn-off limb's)
        /// <summary>The thrown body this collider is part of (or the board of), if any.</summary>
        public static Ragdoll Of(Collider c) { var i = c != null ? c.GetComponent<Impact>() : null; return i != null ? i.owner : null; }
        /// <summary>Something it went through (a sign) took some of its speed: every part keeps this share.</summary>
        public void Slow(float keep)
        {
            foreach (var p in parts) if (!p.rb.isKinematic) p.rb.linearVelocity *= keep;
            if (boardRb != null && !boardRb.isKinematic) boardRb.linearVelocity *= keep;
            foreach (var x in extras) if (x.rb != null) x.rb.linearVelocity *= keep;
        }
        /// <summary>The body's parts (for the test tools).</summary>
        public IEnumerable<Rigidbody> Bodies { get { foreach (var p in parts) yield return p.rb; } }
        /// <summary>Every physics step, each part ploughing through the snow: which bone, the way down into the snow, how hard (m/s).</summary>
        public System.Action<Transform, Vector3, float> Ploughed;
        public static float LimbMaxRel = 14f;   // m/s: the fastest a limb can swing past the pelvis (about its spin cap times its reach)

        /// <summary>Reports a ragdoll part's or the board's impacts, and its slides along the snow.</summary>
        /// <summary>Whether the latest hit (read during Hit) was against an object rather than the snow.</summary>
        public bool LastHitObject;

        class Impact : MonoBehaviour
        {
            public Ragdoll owner; public bool board;
            Vector3 lastMark; float lastTime;
            void OnCollisionEnter(Collision c)
            {
                Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
                lastMark = at; lastTime = Time.time;
                if (owner != null) owner.LastHitObject = c.collider.GetComponentInParent<Obstacle>() != null;   // a tree, rock, sign... not snow
                owner?.Hit?.Invoke(c.relativeVelocity.magnitude, board, at, board ? null : transform);
                if (owner?.Struck != null && c.contactCount > 0)
                {
                    Vector3 n = c.GetContact(0).normal;
                    owner.Struck(c.collider, at, n, Mathf.Abs(Vector3.Dot(c.relativeVelocity, n)), board ? null : transform);
                }
            }
            void OnCollisionStay(Collision c)
            {
                if (c.contactCount == 0) return;
                Vector3 at = c.GetContact(0).point;
                if ((at - lastMark).sqrMagnitude < 0.36f || Time.time - lastTime < 0.05f) return;
                lastMark = at; lastTime = Time.time;
                owner?.Drag?.Invoke(at, c.relativeVelocity.magnitude);
            }
        }
        public Vector3 Centre => Active && parts.Count > 0 ? parts[0].rb.position : Vector3.zero;
        /// <summary>Where the board is while thrown (the body's middle if it has none).</summary>
        public Vector3 BoardCentre => boardRb != null ? boardRb.position : Centre;
        /// <summary>How fast the body (and the board) are still moving: the faster of the two.</summary>
        public float Speed => !Active || parts.Count == 0 || Frozen ? 0f : Mathf.Max(parts[0].rb.linearVelocity.magnitude, boardRb != null ? boardRb.linearVelocity.magnitude * 0.5f : 0f);
        public Vector3 Velocity => Active && parts.Count > 0 ? parts[0].rb.linearVelocity : Vector3.zero;

        /// <summary>How many solid shapes the thrown body has, its parts and then the board (see Capsule).</summary>
        public int ShapeCount => Active ? parts.Count + (boardRb != null ? 1 : 0) + extras.Count : 0;

        /// <summary>
        /// One of the thrown body's shapes as a capsule, for pressing it into the snow: the ends of its core line,
        /// how far it reaches out round that line, how far its underside reaches below it (the board is flat), and
        /// its velocity and mass. A ball has both ends at its centre.
        /// </summary>
        public bool Capsule(int i, out Vector3 a, out Vector3 b, out float r, out float rDown, out Vector3 v, out float mass)
        {
            a = b = v = Vector3.zero; r = rDown = mass = 0f;
            Collider col; Rigidbody rb;
            if (!Active) return false;
            if (i < parts.Count) { if (parts[i].gone) return false; col = parts[i].col; rb = parts[i].rb; }
            else if (i == parts.Count && boardRb != null && boardCol != null) { col = boardCol; rb = boardRb; }
            else
            {
                // the rest of the loose gear (after the board, or in its place if there is none)
                int k = i - parts.Count - (boardRb != null ? 1 : 0);
                if (k < 0 || k >= extras.Count || extras[k].rb == null || extras[k].col == null) return false;
                col = extras[k].col; rb = extras[k].rb;
            }
            var t = col.transform;
            float s = Mathf.Abs(t.lossyScale.x);
            v = rb.linearVelocity; mass = rb.mass;
            if (col is CapsuleCollider cc)
            {
                Vector3 axis = cc.direction == 0 ? Vector3.right : cc.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 c = t.TransformPoint(cc.center), d = t.TransformVector(axis * Mathf.Max(0f, cc.height * 0.5f - cc.radius));
                a = c - d; b = c + d; r = rDown = cc.radius * s;
            }
            else if (col is SphereCollider sc) { a = b = t.TransformPoint(sc.center); r = rDown = sc.radius * s; }
            else if (col is BoxCollider bc)
            {
                // the board: a flat capsule down its length, as wide as the board, its underside half its thickness down
                Vector3 c = t.TransformPoint(bc.center), d = t.TransformVector(Vector3.forward * Mathf.Max(0f, (bc.size.z - bc.size.x) * 0.5f));
                a = c - d; b = c + d; r = bc.size.x * 0.5f * s;
                Vector3 up = t.TransformVector(Vector3.up * bc.size.y * 0.5f), side = t.TransformVector(Vector3.right * bc.size.x * 0.5f);
                rDown = Mathf.Max(Mathf.Abs(up.y), Mathf.Abs(side.y));   // on its edge, it cuts in as deep as it is wide
            }
            else return false;
            return true;
        }

        Vector3 boardSize = new Vector3(0.29f, 0.07f, 1.55f), boardCenter = new Vector3(0f, 0.04f, 0f);

        // ---- more loose gear than the one board: the second ski and the two poles (RiderBodyRide). Each flies off
        // on its own body like the board does. They are light and there can be several, so instead of a slab of
        // snow each they are simply kept on top of the snow (LooseStep): they skid, tumble and come to rest on it.

        /// <summary>A piece of gear that comes off in a crash: its transform (z down its length), and its box.</summary>
        public class Loose
        {
            public Transform t; public Vector3 size, center; public float mass = 1.5f;
            internal Transform home; internal Vector3 pos, scl; internal Quaternion rot; internal Rigidbody rb; internal BoxCollider col;
        }
        readonly List<Loose> extras = new List<Loose>();

        /// <summary>The gear, beyond the board itself, that flies off with the rider (none on a snowboard).</summary>
        public void SetExtras(IEnumerable<Loose> gear)
        {
            if (Active) End();
            extras.Clear();
            if (gear != null) extras.AddRange(gear);
        }

        void ThrowExtras(Vector3 velocity, Collider gCol, Collider bgCol)
        {
            foreach (var x in extras)
            {
                x.rb = null; x.col = null;
                if (x.t == null || !x.t.gameObject.activeInHierarchy) continue;   // (a pole in a hand that is gone, say)
                x.home = x.t.parent; x.pos = x.t.localPosition; x.rot = x.t.localRotation; x.scl = x.t.localScale;
                x.t.SetParent(null, true);
                x.rb = x.t.gameObject.AddComponent<Rigidbody>();
                x.rb.mass = x.mass;
                x.rb.maxAngularVelocity = 25f;
                x.rb.maxDepenetrationVelocity = 2.5f;
                x.rb.useGravity = true;
                x.rb.interpolation = RigidbodyInterpolation.Interpolate;
                x.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                x.col = x.t.gameObject.AddComponent<BoxCollider>();
                x.col.center = x.center; x.col.size = x.size;
                x.col.sharedMaterial = snowFeel;
                var imp = x.t.gameObject.AddComponent<Impact>(); imp.owner = this; imp.board = true;
                if (riderCollider != null) Physics.IgnoreCollision(x.col, riderCollider);
                foreach (var p in parts) if (!p.gone) Physics.IgnoreCollision(x.col, p.col);
                if (boardCol != null) Physics.IgnoreCollision(x.col, boardCol);
                Physics.IgnoreCollision(x.col, gCol); Physics.IgnoreCollision(x.col, bgCol);
                foreach (var y in extras) if (y != x && y.col != null) Physics.IgnoreCollision(x.col, y.col);
                x.rb.linearVelocity = velocity * Random.Range(0.6f, 0.85f) + Vector3.up * Random.Range(2.5f, 4.5f) + Random.insideUnitSphere * 2.5f;
                x.rb.angularVelocity = new Vector3(Random.Range(-10f, 10f), Random.Range(-5f, 5f), Random.Range(-10f, 10f));
            }
        }

        /// <summary>Each physics step: the loose gear stays on top of the snow, skidding and losing its spin there.</summary>
        void LooseStep()
        {
            foreach (var x in extras)
            {
                if (x.rb == null) continue;
                Vector3 c = x.rb.worldCenterOfMass;
                float floor = Slope.DrawnHeight(c.x, c.z) - Course.CrevasseCut(c.x, c.z) + 0.03f;
                if (c.y >= floor) continue;
                Vector3 v = x.rb.linearVelocity;
                x.rb.position += Vector3.up * (floor - c.y);
                if (v.y < 0f) v.y = -v.y * 0.25f;
                float k = Mathf.Exp(-Time.fixedDeltaTime * 2.5f);
                x.rb.linearVelocity = new Vector3(v.x * k, v.y, v.z * k);
                x.rb.angularVelocity *= Mathf.Exp(-Time.fixedDeltaTime * 4f);
            }
        }

        void EndExtras()
        {
            foreach (var x in extras)
            {
                if (x.rb == null || x.t == null) { x.rb = null; x.col = null; continue; }
                Kill(x.t.GetComponent<Impact>());
                x.col.enabled = false; Kill(x.col); Kill(x.rb); x.rb = null; x.col = null;
                // back exactly as it was, scale and all: loose in the world it took on its parent's scale (a pole's
                // parent is the hand bone, which the hero's file scales by 100), and SetParent(home, false) keeps
                // that, so without this a pole came back 100 times bigger after every crash
                x.t.SetParent(x.home, false);
                x.t.localPosition = x.pos; x.t.localRotation = x.rot; x.t.localScale = x.scl;
            }
        }

        /// <summary>The thing that flies off with the rider: a board by default; a tube is a big flat ring.</summary>
        public Ragdoll(RiderRig rig, Transform board, Collider riderCollider, Vector3 size, Vector3 center) : this(rig, board, riderCollider)
        { boardSize = size; boardCenter = center; }

        public Ragdoll(RiderRig rig, Transform board, Collider riderCollider)
        {
            this.rig = rig;
            this.board = board;
            boardHome = board.parent;
            this.riderCollider = riderCollider;
            // snow grabs a body: high friction (it tumbles rather than skates), a little give
            snowFeel = new PhysicsMaterial("RagdollSnow") { dynamicFriction = 1.1f, staticFriction = 1.3f, bounciness = 0.12f,
                                                            frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Average };
            groundBody = Slab("RagdollGround");
            boardGroundBody = Slab("BoardGround");
        }

        Rigidbody Slab(string name)
        {
            var g = new GameObject(name);
            var c = g.AddComponent<BoxCollider>();
            // deep, so whatever is inside it is always pushed out of its top: the slab is teleported under the body every
            // step, and on a steep face (the surface under it moving a metre a step) a part could end up more than half way
            // into a 2 m slab, be pushed out of its bottom, and fall on through the mountain for good
            c.size = new Vector3(14f, 20f, 14f);
            c.center = new Vector3(0f, -10f, 0f);
            c.sharedMaterial = snowFeel;
            var rb = g.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            g.SetActive(false);
            return rb;
        }

        /// <summary>Launch: velocity is what the rider was carrying just before the hit.</summary>
        public void Begin(Vector3 velocity)
        {
            if (Active || rig == null || !rig.Ok) return;
            Active = true;
            Buried = 0f;
            var model = rig.Root;
            modelHome = model.parent;
            modelPos = model.localPosition; modelRot = model.localRotation; modelScale = model.localScale;
            boardPos = board.localPosition; boardRot = board.localRotation; boardScl = board.localScale;
            footRelL = Quaternion.Inverse(rig.ShinL.rotation) * rig.FootL.rotation;
            footRelR = Quaternion.Inverse(rig.ShinR.rotation) * rig.FootR.rotation;
            model.SetParent(null, true);
            board.SetParent(null, true);

            if (parts.Count == 0) Prebuild();
            // switch the body over from posed to physical. Each joint first re-captures the pose the body is
            // in right now as its rest, so a crouched knee or a lowered arm is never "outside its limits"
            foreach (var p in parts) if (p.joint != null) { var cb = p.joint.connectedBody; p.joint.connectedBody = null; p.joint.connectedBody = cb; }
            foreach (var p in parts)
            {
                if (p.gone) continue;   // (a limb lost earlier in the run: nothing there to throw)
                p.col.enabled = true;
                p.rb.isKinematic = false;
                p.rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].gone) continue;
                if (riderCollider != null) Physics.IgnoreCollision(parts[i].col, riderCollider);
                for (int j = i + 1; j < parts.Count; j++) if (!parts[j].gone) Physics.IgnoreCollision(parts[i].col, parts[j].col);
            }
            // thrown forward and up, tumbling head over heels about the axis across the line of travel
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 roll = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            float spinRate = Mathf.Clamp(flat.magnitude * 0.35f, 3f, 9f);
            var spin = roll * spinRate + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * 2f;
            // as one rigid body: each part moves with the pelvis plus the spin about it, so no joint starts
            // out torn
            Vector3 pivot = parts[0].rb.worldCenterOfMass, lin = velocity * 0.85f + Vector3.up * 2.5f;
            foreach (var p in parts)
            {
                if (p.gone) continue;
                p.rb.linearVelocity = lin + Vector3.Cross(spin, p.rb.worldCenterOfMass - pivot);
                p.rb.angularVelocity = spin;
            }

            // the board flies off on its own
            boardRb = board.gameObject.AddComponent<Rigidbody>();
            boardRb.mass = 4f;
            boardRb.maxAngularVelocity = 25f;
            boardRb.maxDepenetrationVelocity = 2.5f;
            boardRb.interpolation = RigidbodyInterpolation.Interpolate;
            boardRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            boardCol = board.gameObject.AddComponent<BoxCollider>();
            boardCol.center = boardCenter;
            boardCol.size = boardSize;
            boardCol.sharedMaterial = snowFeel;
            var bimp = board.gameObject.AddComponent<Impact>(); bimp.owner = this; bimp.board = true;
            if (riderCollider != null) Physics.IgnoreCollision(boardCol, riderCollider);
            foreach (var p in parts) if (!p.gone) Physics.IgnoreCollision(boardCol, p.col);
            // each slab of snow is only for its own body: the body's for the body, the board's for the board
            // (and neither for the rider's own sphere, which turns collisions back on when it gets up).
            // Unity only accepts IgnoreCollision between active colliders, so the slabs go live first
            groundBody.gameObject.SetActive(true);
            boardGroundBody.gameObject.SetActive(true);
            PlaceSlab(groundBody, Centre, true);
            PlaceSlab(boardGroundBody, board.position, true);
            var gCol = groundBody.GetComponent<Collider>(); var bgCol = boardGroundBody.GetComponent<Collider>();
            Physics.IgnoreCollision(gCol, boardCol);
            foreach (var p in parts) if (!p.gone) Physics.IgnoreCollision(bgCol, p.col);
            if (riderCollider != null) { Physics.IgnoreCollision(gCol, riderCollider); Physics.IgnoreCollision(bgCol, riderCollider); }
            boardRb.linearVelocity = velocity * 0.75f + Vector3.up * 3.5f + Random.insideUnitSphere * 2f;
            boardRb.angularVelocity = new Vector3(Random.Range(-9f, 9f), Random.Range(-4f, 4f), Random.Range(-9f, 9f));
            ThrowExtras(velocity, gCol, bgCol);   // the other ski and the poles, on skis
            // never thrown from inside a corner: the board hit something, and the body above the board may already be in
            // it. PhysX eases a part out of one thing by itself (a trunk, a post), so the body is only moved when it is
            // in two at once (a chalet's wall and its landing) or deep in one
            Physics.SyncTransforms();
            Vector3 back = -flat;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            if (Cornered(false)) UnwedgeOne(back.normalized, true, false);
            if (Cornered(true)) UnwedgeOne(back.normalized, true, true);   // (the board on its own: it is its own body)
        }

        /// <summary>
        /// Build the physical body once, on the skeleton at rest, like Unity's Ragdoll Wizard: every part a
        /// rigidbody with a collider and a joint to the part above it, kept kinematic and switched off until a
        /// crash switches it on. Nothing is added or destroyed during play.
        /// </summary>
        public void Prebuild()
        {
            if (parts.Count > 0 || rig == null || !rig.Ok) return;
            float s = 1f;   // metres
            // pelvis first: every later part hangs off the nearest part above it
            // the pelvis body goes on the top bone of the skeleton that moves (vertices are skinned to it
            // too: anything above it would stay behind and stretch the mesh back to where you crashed)
            Add(rig.BodyBone != null ? rig.BodyBone : rig.Hips, null, 12f, Shape.Ball, 0.15f * s, 0f, 0f, 0f);
            Add(rig.Chest, rig.Neck != null ? rig.Neck : rig.Head, 12f, Shape.Capsule, 0.15f * s, 30f, 35f, 25f, 1.6f);
            Add(rig.Head, null, 5f, Shape.Head, 0.13f * s, 30f, 40f, 30f);
            Add(rig.UpperL, rig.LowerL, 2.2f, Shape.Capsule, 0.055f * s, 25f, 80f, 60f);
            Add(rig.LowerL, rig.HandL, 1.6f, Shape.Capsule, 0.05f * s, 10f, 80f, 10f, 1.35f);
            Add(rig.UpperR, rig.LowerR, 2.2f, Shape.Capsule, 0.055f * s, 25f, 80f, 60f);
            Add(rig.LowerR, rig.HandR, 1.6f, Shape.Capsule, 0.05f * s, 10f, 80f, 10f, 1.35f);
            Add(rig.ThighL, rig.ShinL, 7f, Shape.Capsule, 0.08f * s, 15f, 60f, 35f);
            Add(rig.ShinL, rig.EndL, 4f, Shape.Capsule, 0.065f * s, 5f, 75f, 5f, 1.1f);
            Add(rig.ThighR, rig.ShinR, 7f, Shape.Capsule, 0.08f * s, 15f, 60f, 35f);
            Add(rig.ShinR, rig.EndR, 4f, Shape.Capsule, 0.065f * s, 5f, 75f, 5f, 1.1f);
            foreach (var p in parts) Sleep(p);
        }

        static void Sleep(Part p)
        {
            p.rb.interpolation = RigidbodyInterpolation.None;
            if (!p.rb.isKinematic) { p.rb.linearVelocity = Vector3.zero; p.rb.angularVelocity = Vector3.zero; }
            p.rb.isKinematic = true;
            p.col.enabled = false;
        }

        /// <summary>Switch what flies off with the rider: the board, or the tube.</summary>
        public void SetBoard(Transform b, Vector3 size, Vector3 center)
        {
            if (Active) End();
            board = b; boardHome = b.parent; boardSize = size; boardCenter = center;
        }

        enum Shape { Ball, Capsule, Head }

        void Add(Transform bone, Transform child, float mass, Shape shape, float radius, float twist, float swing1, float swing2, float stretch = 1f)
        {
            if (bone == null) return;
            float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.02f; rb.angularDamping = 0.45f;   // the snow does the slowing (see PhysicsStep)
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 14;
            rb.solverVelocityIterations = 6;
            // a human body cannot spin a limb a hundred times a second, nor be shot out of the snow
            rb.maxAngularVelocity = 18f;
            rb.maxDepenetrationVelocity = 2.5f;
            Collider col;
            Vector3 axisLocal = Vector3.up;
            if (shape == Shape.Capsule && child != null)
            {
                Vector3 local = bone.InverseTransformPoint(child.position) * stretch;
                var cc = bone.gameObject.AddComponent<CapsuleCollider>();
                axisLocal = local.normalized;
                Vector3 a = new Vector3(Mathf.Abs(axisLocal.x), Mathf.Abs(axisLocal.y), Mathf.Abs(axisLocal.z));
                cc.direction = a.x > a.y && a.x > a.z ? 0 : a.y > a.z ? 1 : 2;
                cc.center = local * 0.5f;
                cc.radius = radius / scale;
                cc.height = local.magnitude + 2f * radius / scale;
                col = cc;
            }
            else
            {
                var sc = bone.gameObject.AddComponent<SphereCollider>();
                sc.radius = radius / scale;
                // the head's ball sits up from the neck joint
                if (shape == Shape.Head) sc.center = bone.InverseTransformDirection(Vector3.up) * (0.09f / scale);
                col = sc;
            }
            col.sharedMaterial = snowFeel;
            var imp = bone.gameObject.AddComponent<Impact>(); imp.owner = this;

            Rigidbody parentRb = null;
            // only ever another part of this ragdoll: the bones sit under the rider's own body, and a joint to
            // that (the pelvis would find it) pins the rider to a frozen pelvis and drags it through the snow
            for (var t = bone.parent; t != null && parentRb == null; t = t.parent)
            {
                var prb = t.GetComponent<Rigidbody>();
                if (prb != null && parts.Exists(q => q.rb == prb)) parentRb = prb;
            }
            var part = new Part { bone = bone, rb = rb, col = col, parentRb = parentRb, axis = axisLocal, twist = twist, swing1 = swing1, swing2 = swing2 };
            MakeJoint(part);
            // thin limbs have almost no inertia about their length and the solver spins them wildly; give
            // every part at least the inertia of a solid ball its size
            float floorI = 0.4f * mass * 0.06f * 0.06f * 2.5f;
            var it = rb.inertiaTensor;
            rb.inertiaTensor = new Vector3(Mathf.Max(it.x, floorI), Mathf.Max(it.y, floorI), Mathf.Max(it.z, floorI));
            parts.Add(part);
        }

        static void MakeJoint(Part p)
        {
            if (p.parentRb == null || p.joint != null) return;
            var j = p.bone.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = p.parentRb;
            j.autoConfigureConnectedAnchor = true;
            j.anchor = Vector3.zero;
            j.axis = p.axis;
            j.swingAxis = Vector3.Cross(p.axis, Mathf.Abs(p.axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            j.lowTwistLimit = new SoftJointLimit { limit = -p.twist };
            j.highTwistLimit = new SoftJointLimit { limit = p.twist };
            j.swing1Limit = new SoftJointLimit { limit = p.swing1 };
            j.swing2Limit = new SoftJointLimit { limit = p.swing2 };
            j.enableProjection = true;
            j.projectionDistance = 0.02f;
            j.projectionAngle = 20f;
            j.enablePreprocessing = false;
            p.joint = j;
        }

        // ---- limbs torn off (see RiderBodyLimbs): the joint to the body goes, the piece tumbles away on its own
        // with its own slab of snow under it, and it all goes back together in End

        readonly List<Rigidbody> limbSlabs = new List<Rigidbody>();

        /// <summary>Is this bone a part of the body that is still held to it (not torn off, nor hanging off a torn-off piece)?</summary>
        public bool Attached(Transform bone) { var p = PartOf(bone); return p != null && !p.free && !p.gone; }
        /// <summary>Is this bone (or a bone above it) a part lost earlier in the run?</summary>
        public bool Gone(Transform bone) { for (var t = bone; t != null; t = t.parent) { var p = PartOf(t); if (p != null && p.gone) return true; } return false; }

        Part PartOf(Transform bone) { foreach (var p in parts) if (p.bone == bone) return p; return null; }

        /// <summary>The torn-off piece this part belongs to (its root part).</summary>
        Rigidbody SlabOf(Part p)
        {
            for (var t = p.bone; t != null; t = t.parent) { var q = PartOf(t); if (q != null && q.slab != null) return q.slab; }
            return null;
        }

        /// <summary>
        /// Tear this part (and every part below it) off the body: its joint goes, it is thrown out along `kick` (m/s,
        /// on top of what it carries) and from now on it has its own snow under it. False if it can't be (not a
        /// part, the pelvis, or already off).
        /// </summary>
        public bool Sever(Transform bone, Vector3 kick)
        {
            if (!Active || Frozen) return false;
            var p = PartOf(bone);
            if (p == null || p.free || p.parentRb == null) return false;
            if (p.joint != null) Kill(p.joint);
            p.joint = null;
            var piece = new List<Part>();
            foreach (var q in parts) if ((q.bone == bone || q.bone.IsChildOf(bone)) && !q.gone) { q.free = true; piece.Add(q); }
            // its own slab of snow, for it alone
            Rigidbody slab = null;
            foreach (var s in limbSlabs) if (!s.gameObject.activeSelf) { slab = s; break; }
            if (slab == null) { slab = Slab("LimbGround"); limbSlabs.Add(slab); }
            slab.gameObject.SetActive(true);
            PlaceSlab(slab, p.rb.position, true);
            p.slab = slab;
            var sCol = slab.GetComponent<Collider>(); var gCol = groundBody.GetComponent<Collider>();
            // (a slab used before still remembers what it ignored then: IgnoreCollision outlives switching it off)
            foreach (var q in parts) if (!q.gone) Physics.IgnoreCollision(sCol, q.col, !piece.Contains(q));
            if (boardCol != null) Physics.IgnoreCollision(sCol, boardCol);
            if (riderCollider != null) Physics.IgnoreCollision(sCol, riderCollider);
            foreach (var q in piece) Physics.IgnoreCollision(q.col, gCol);
            foreach (var other in limbSlabs) if (other != slab && other.gameObject.activeSelf)
                foreach (var q in piece) Physics.IgnoreCollision(q.col, other.GetComponent<Collider>());
            foreach (var q in piece)
            {
                q.rb.linearVelocity += kick;
                q.rb.angularVelocity += Random.insideUnitSphere * 8f;
                q.rb.maxAngularVelocity = 30f;   // a loose arm spins faster than one on a body
            }
            return true;
        }

        /// <summary>Put every torn-off piece back on (called by End, with the bones home).</summary>
        void Rejoin()
        {
            var gCol = groundBody.GetComponent<Collider>();
            foreach (var p in parts)
            {
                if (!p.free) continue;
                p.free = false; p.gone = true;   // the rider gets up and rides on without it
                p.rb.maxAngularVelocity = 18f;
                if (p.col.enabled && gCol.enabled && gCol.gameObject.activeInHierarchy) Physics.IgnoreCollision(p.col, gCol, false);
                p.slab = null;
            }
            foreach (var s in limbSlabs) s.gameObject.SetActive(false);
            foreach (var p in parts) if (!p.gone) MakeJoint(p);
        }

        /// <summary>Every lost limb back on, its joint rebuilt at rest (a new run: call after End).</summary>
        public void Reattach()
        {
            if (Active) return;
            foreach (var p in parts) { p.gone = false; p.free = false; p.slab = null; MakeJoint(p); }
        }

        /// <summary>
        /// Carried off in an avalanche: every part (and the board) is pulled toward the flow of the debris, and the
        /// body is rolled over and over in it.
        /// </summary>
        public void Carry(Vector3 flow, float rate, float dt)
        {
            if (!Active) return;
            CarriedFor = 0.5f;   // (an avalanche moves it: never settled, see Settle)
            if (Frozen) Thaw();
            float k = 1f - Mathf.Exp(-rate * dt);
            foreach (var p in parts)
            {
                if (p.gone) continue;
                p.rb.linearVelocity = Vector3.Lerp(p.rb.linearVelocity, flow, k);
                p.rb.angularVelocity += Random.insideUnitSphere * (6f * k);
            }
            if (boardRb != null) boardRb.linearVelocity = Vector3.Lerp(boardRb.linearVelocity, flow, k * 0.7f);
            foreach (var x in extras) if (x.rb != null) x.rb.linearVelocity = Vector3.Lerp(x.rb.linearVelocity, flow, k * 0.7f);
        }

        /// <summary>Each physics step: keep a slab of snow under the body and under the board.</summary>
        public void PhysicsStep()
        {
            if (!Active) return;
            CarriedFor -= Time.fixedDeltaTime;
            if (Frozen) { LooseStep(); return; }   // laid to rest where it lay (Settle); the loose gear still keeps to the snow
            // tumbling into a sign or a flag breaks it / knocks it down (see Breakables)
            if (parts.Count > 0) Breakable.Probe(parts[0].rb.position, parts[0].rb.position, 0.9f, parts[0].col);
            // tumbling into a young tree or a snag fast enough snaps it, and the body carries on through (TreeBreak)
            if (parts.Count > 0 && TreeBreak.Instance != null && TreeBreak.Instance.BodyAhead(parts[0].rb.position, parts[0].rb.linearVelocity)) Slow(TreeBreak.BodyKeep);
            // the mountain's own gravity (Rider.G), not PhysX's 9.81: thrown at the board's speed off a lip or a roller, a
            // body under 9.81 flew more than twice as long and as high as the board ever does (seven seconds and 60 m
            // up off a kicker at 200 km/h): launched, and never coming down
            Vector3 extra = Vector3.down * (Rider.G + Physics.gravity.y);
            if (extra.y < 0f)
            {
                foreach (var p in parts) if (!p.gone && !p.rb.isKinematic) p.rb.AddForce(extra, ForceMode.Acceleration);
                if (boardRb != null) boardRb.AddForce(extra, ForceMode.Acceleration);
            }
            // ploughing through snow: any part down in it is dragged back in proportion to its speed
            // (snow has to be pushed aside), on top of the contact friction
            Vector3 gp = groundBody.position, gn = groundBody.rotation * Vector3.up;
            foreach (var p in parts)
            {
                if (p.gone) continue;
                var s = p.free ? SlabOf(p) : null;
                Vector3 sp = s != null ? s.position : gp, sn = s != null ? s.rotation * Vector3.up : gn;
                float h = Vector3.Dot(p.rb.position - sp, sn);
                if (h > 0.28f) continue;
                Vector3 v = Vector3.ProjectOnPlane(p.rb.linearVelocity, sn);
                float deep = Mathf.Clamp01(1.2f - h / 0.28f);
                p.rb.AddForce(-v * (Plough * p.rb.mass * deep), ForceMode.Force);
                if (Ploughed != null && v.sqrMagnitude > 0.25f && !p.free) Ploughed(p.bone, -sn, v.magnitude * deep);
            }
            // now and then the solver kicks a limb out at two or three times the body's speed for a step (with
            // nothing near it): no arm or leg can swing past the pelvis faster than its spin cap allows, so clip it
            Vector3 pv = parts[0].rb.linearVelocity;
            for (int i = 1; i < parts.Count; i++)
            {
                if (parts[i].free || parts[i].gone) continue;   // a torn-off limb goes where it was thrown
                Vector3 rel = parts[i].rb.linearVelocity - pv;
                if (rel.sqrMagnitude > LimbMaxRel * LimbMaxRel) parts[i].rb.linearVelocity = pv + rel * (LimbMaxRel / rel.magnitude);
            }
            Guide(gp, gn);
            PlaceSlab(groundBody, FloorPoint(Centre), false);
            if (Buried > 0f)
            {
                // punched down into the snow: its floor is the bottom of the hole it made
                Vector3 lowered = groundBody.position - groundBody.rotation * Vector3.up * Buried;
                groundBody.position = lowered; groundBody.transform.position = lowered;
            }
            if (boardRb != null) PlaceSlab(boardGroundBody, FloorPoint(boardRb.position), false);
            LooseStep();
            foreach (var p in parts) if (p.slab != null) PlaceSlab(p.slab, FloorPoint(p.rb.position), false);
        }

        /// <summary>The player's thumb while thrown: -1..1, the same drag that steers the board.</summary>
        public float Steer;
        public static float GuideSlide = 4.5f, GuideAir = 1.8f;   // m/s^2 sideways, for the whole body
        public static float GuideMaxSide = 5f;                     // sideways speed (m/s) past which the thumb stops pushing

        /// <summary>
        /// A controlled fall: the thumb nudges the tumbling body sideways, across the way it is sliding, so
        /// you can steer a wipeout away from a tree or into the soft stuff. The sliding pelvis and chest decide
        /// which way is sideways and when you are on the snow, but every part gets the same push for its
        /// weight: pushing the pelvis and chest alone dragged the limbs after them, whipping a forearm out at
        /// 28 m/s and stretching the joints. An even push is like tilting the hill under you, so the joints
        /// and spin caps never feel it. It fades as the sideways speed builds, so it guides the fall rather
        /// than flinging it, and grips better sliding through the snow than in the air.
        /// </summary>
        void Guide(Vector3 gp, Vector3 gn)
        {
            if (Mathf.Abs(Steer) < 0.05f || parts.Count < 2) return;
            var pelvis = parts[0].rb; var chest = parts[1].rb;
            // sideways is across the slide (or across the hill once it has stopped), in the plane of the snow
            Vector3 v = Vector3.ProjectOnPlane(pelvis.linearVelocity, gn);
            Vector3 along = v.sqrMagnitude > 1f ? v.normalized : Vector3.ProjectOnPlane(Vector3.forward, gn).normalized;
            Vector3 side = Vector3.Cross(gn, along).normalized;
            // nothing to steer once the body has all but stopped: it would crawl along the snow
            float moving = Mathf.Clamp01((v.magnitude - 0.5f) / 2.5f);
            bool onSnow = Vector3.Dot(pelvis.position - gp, gn) < 0.35f || Vector3.Dot(chest.position - gp, gn) < 0.35f;
            float a = (onSnow ? GuideSlide : GuideAir) * Steer * moving;
            float sideSpeed = Vector3.Dot(v, side) * Mathf.Sign(Steer);
            a *= Mathf.Clamp01(1f - sideSpeed / GuideMaxSide);
            if (a == 0f) return;
            foreach (var p in parts) if (!p.free && !p.gone) p.rb.AddForce(side * a, ForceMode.Acceleration);
        }

        /// <summary>
        /// Where to put a slab of snow: under the point, except once it has dropped into a crevasse, where the slab
        /// goes down onto the middle of the crevasse's bottom and stays out of the way, below the ice. The ice walls
        /// and floor have colliders of their own (CourseDressing), so the body tumbles off them and comes to rest on
        /// the floor or wedged between the walls; a slab under a body by the wall would sit up at the lip's height.
        /// </summary>
        static Vector3 FloorPoint(Vector3 c)
        {
            float surface = Slope.DrawnHeight(c.x, c.z);
            return c.y < surface - 1f && Course.CrevasseNear(c.x, c.z, 4f, out var cv) ? Course.CrevasseMiddle(cv, c) : c;
        }

        static void PlaceSlab(Rigidbody slab, Vector3 at, bool snap)
        {
            Vector3 n = Slope.Normal(at.x, at.z);
            var pos = new Vector3(at.x, Slope.DrawnHeight(at.x, at.z) - Course.CrevasseCut(at.x, at.z), at.z);   // the bottom, in a crevasse
            pos.y -= Sink + SinkPowder * Slope.Rough(at.x, at.z);   // the body settles into the snow (SnowLayer draws the hole)
            var rot = Quaternion.FromToRotation(Vector3.up, n);
            // teleport, never MovePosition: a slab moved along with the body would carry it like a
            // conveyor and its friction would have nothing to grip
            slab.position = pos; slab.rotation = rot; slab.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>How far down into the snow a fatal fall has driven the body (its floor is this far below the surface).</summary>
        public float Buried { get; private set; }

        /// <summary>
        /// A fatal fall: the snow gives way and the body carries on down into it, `depth` metres, keeping a little of
        /// the way it was going (vel) and none of its bounce.
        /// </summary>
        public void Bury(float depth, Vector3 vel)
        {
            if (!Active) return;
            if (Frozen) Thaw();
            Buried = depth;
            Vector3 v = new Vector3(vel.x * 0.12f, -Mathf.Clamp(depth * 6f, 5f, 9f), vel.z * 0.12f);
            foreach (var p in parts) if (!p.gone) { p.rb.linearVelocity = v; p.rb.angularVelocity *= 0.3f; }
            if (boardRb != null) boardRb.linearVelocity = boardRb.linearVelocity * 0.3f + Vector3.up * 4f;   // the board flies on
        }

        /// <summary>Every frame while thrown: the feet are separate bones, so carry them on the shins.</summary>
        public void LateStep()
        {
            if (!Active) return;
            rig.FootL.SetPositionAndRotation(rig.EndL.position, rig.ShinL.rotation * footRelL);
            rig.FootR.SetPositionAndRotation(rig.EndR.position, rig.ShinR.rotation * footRelR);
        }

        /// <summary>Done with for good (the rider's model is being swapped for another): the snow slabs go too.</summary>
        public void Dispose()
        {
            if (Active) End();
            if (groundBody != null) Kill(groundBody.gameObject);
            if (boardGroundBody != null) Kill(boardGroundBody.gameObject);
        }

        // immediately, never deferred: a new crash can start in the very next physics step, and a deferred
        // Rigidbody would still be there (AddComponent would fail and the ragdoll would be half built)
        static void Kill(Object o) { if (o != null) Object.DestroyImmediate(o); }

        /// <summary>Back on the board: physics off, bones home, model and board back on the rider.</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            Frozen = false; CarriedFor = 0f;
            var gc = groundBody.GetComponent<Collider>();
            foreach (var p in parts) if (p.free && p.col.enabled) Physics.IgnoreCollision(p.col, gc, false);   // (only while both are live)
            foreach (var p in parts) Sleep(p);   // back to posed: kept, not destroyed
            var bi = board.GetComponent<Impact>(); if (bi != null) Kill(bi);
            if (boardRb != null) { boardRb.isKinematic = true; boardCol.enabled = false; Kill(boardCol); Kill(boardRb); boardRb = null; }
            groundBody.gameObject.SetActive(false);
            boardGroundBody.gameObject.SetActive(false);
            EndExtras();
            var model = rig.Root;
            model.SetParent(modelHome, false);
            model.localPosition = modelPos; model.localRotation = modelRot; model.localScale = modelScale;
            board.SetParent(boardHome, false);
            board.localPosition = boardPos; board.localRotation = boardRot; board.localScale = boardScl;   // (as the loose gear: see EndExtras)
            rig.RestoreBind();
            Rejoin();   // any limb torn off is back on, its joint rebuilt at rest
        }

        // ---- never stuck. The body is thrown from where the board hit something, so the posed body above the board
        // can start out partly inside it: the torso over a chalet's landing as the board hits its step, say. PhysX
        // pushes a part out of each solid shape on its own, and where two of them overlap (a chalet's wall and its
        // landing, deck, stair or terrace boxes) it pushed the part out of one into the other: the body locked there
        // inside the wall, twitching at a metre or two a second, never asleep, and the fall never ended (the game
        // waits for the body to read as still). So the body never starts inside anything (Begin), one found pinned
        // later is lifted clear (Unwedge), and one that will not come to rest is laid down where it is (Settle).

        static readonly Collider[] nearBuf = new Collider[48];

        /// <summary>A collider the body can be stuck in: solid, standing still, and not the body, its gear or its snow.</summary>
        bool Solid(Collider c)
        {
            if (c == null || c.isTrigger || c == riderCollider) return false;
            var rb = c.attachedRigidbody;
            if (rb != null && (!rb.isKinematic || rb == groundBody || rb == boardGroundBody || limbSlabs.Contains(rb))) return false;
            return Of(c) == null;
        }

        /// <summary>How deep a collider would be inside solid things, moved by offset (and the way out of them, summed).</summary>
        float Inside(Collider col, Vector3 pos, Quaternion rot, Vector3 offset, ref Vector3 outSum, HashSet<Collider> what = null)
        {
            var b = col.bounds;
            int n = Physics.OverlapBoxNonAlloc(b.center + offset, b.extents + Vector3.one * 0.02f, nearBuf, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            float deepest = 0f;
            for (int i = 0; i < n; i++)
            {
                var o = nearBuf[i];
                if (!Solid(o)) continue;
                if (Physics.ComputePenetration(col, pos + offset, rot, o, o.transform.position, o.transform.rotation, out var dir, out float d))
                {
                    deepest = Mathf.Max(deepest, d);
                    outSum += dir * d;
                    if (what != null && d > 0.05f) what.Add(o);
                }
            }
            return deepest;
        }

        /// <summary>How deep the deepest part (or the board) is inside something solid right now, and the way out.</summary>
        public float Embedded(out Vector3 way)
        {
            way = Vector3.zero;
            if (!Active) return 0f;
            float deepest = 0f;
            foreach (var p in parts)
                if (!p.gone && !p.free && p.col.enabled) deepest = Mathf.Max(deepest, Inside(p.col, p.rb.position, p.rb.rotation, Vector3.zero, ref way));
            if (boardRb != null) deepest = Mathf.Max(deepest, Inside(boardCol, boardRb.position, boardRb.rotation, Vector3.zero, ref way));
            return deepest;
        }

        static readonly HashSet<Collider> touched = new HashSet<Collider>();

        /// <summary>Just thrown, and in two things at once or deep in one (the body, or the board)?</summary>
        bool Cornered(bool theBoard)
        {
            touched.Clear();
            return InsideAt(Vector3.zero, true, theBoard, touched) > 0.25f || touched.Count >= 2;
        }

        float InsideAt(Vector3 offset, bool posed, bool theBoard, HashSet<Collider> what = null)
        {
            Vector3 ignore = Vector3.zero;
            float deepest = 0f;
            foreach (var p in parts)
            {
                if (theBoard) break;
                if (p.gone || p.free || !p.col.enabled) continue;
                var t = p.rb.transform;
                deepest = Mathf.Max(deepest, Inside(p.col, posed ? t.position : p.rb.position, posed ? t.rotation : p.rb.rotation, offset, ref ignore, what));
            }
            if (theBoard && boardRb != null)
                deepest = Mathf.Max(deepest, Inside(boardCol, posed ? board.position : boardRb.position, posed ? board.rotation : boardRb.rotation, offset, ref ignore, what));
            return deepest;
        }

        /// <summary>
        /// Lift the body clear of anything solid it is inside, as one, and the board on its own: the nearest spot back
        /// along `away` (and a little up, or to either side) where it is at most a touch into one thing. posed: the body has just been switched
        /// on and its bones still say where it is. False if it was clear already, or nowhere near was.
        /// </summary>
        public bool Unwedge(Vector3 away, bool posed)
        {
            if (!Active || !Unstick) return false;
            bool body = UnwedgeOne(away, posed, false);
            return (boardRb != null && UnwedgeOne(away, posed, true)) || body;
        }

        /// <summary>The body, or the board on its own: moved the least that clears it.</summary>
        bool UnwedgeOne(Vector3 away, bool posed, bool theBoard)
        {
            touched.Clear();
            if (!Active || !Unstick || (InsideAt(Vector3.zero, posed, theBoard, touched) < 0.1f && touched.Count < 2)) return false;
            if (tries.Count == 0)
            {
                // every move to try, the smallest first (back the way it came before sideways, at the same length)
                foreach (float up in new[] { 0f, 0.3f, 0.6f, 1f })
                    foreach (float b in new[] { 0f, 0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f, 1.3f, 1.6f, 2f })
                        for (int d = 0; d < 5; d++)
                            if (up > 0f || b > 0f) tries.Add(new Vector3(d, b, up));
                tries.Sort((x, y) => (x.y * x.y + x.z * x.z + x.x * 0.001f).CompareTo(y.y * y.y + y.z * y.z + y.x * 0.001f));
            }
            Vector3 side = Vector3.Cross(Vector3.up, away).normalized;
            Vector3[] dirs = { away, (away + side).normalized, (away - side).normalized, side, -side };
            foreach (var t in tries)
            {
                Vector3 o = dirs[(int)t.x] * t.y + Vector3.up * t.z;
                // clear enough: at most a touch into one thing, which PhysX eases it out of by itself (the least move)
                touched.Clear();
                if (InsideAt(o, posed, theBoard, touched) >= 0.1f || touched.Count >= 2) continue;
                Shift(o, posed, theBoard);
                Unwedged++; LastUnwedge = o.magnitude;
                return true;
            }
            return false;
        }
        static readonly List<Vector3> tries = new List<Vector3>();   // (which way, how far back, how far up)

        void Shift(Vector3 o, bool posed, bool theBoard)
        {
            if (posed)
            {
                // the bones are the truth until the next step: move the model (every bone with it), or the board
                if (theBoard) board.position += o; else rig.Root.position += o;
                Physics.SyncTransforms();
                return;
            }
            if (theBoard) boardRb.position += o;
            else foreach (var p in parts) if (!p.gone && !p.free) p.rb.position += o;
        }

        /// <summary>Off only for the test tools' before-and-after numbers: no lifting clear, no settling.</summary>
        public static bool Unstick = true;
        /// <summary>How many times a body has been lifted clear of something it was inside (for the test tools).</summary>
        public static int Unwedged;
        /// <summary>How far the last one was moved (m).</summary>
        public static float LastUnwedge;
        /// <summary>Moved by an avalanche lately (s left): never settled while it is.</summary>
        public float CarriedFor { get; private set; }
        /// <summary>Laid to rest where it lay: no more physics until it is put away or the snow moves it.</summary>
        public bool Frozen { get; private set; }

        /// <summary>
        /// A body that will not come to rest by itself: slow every part (and the board) hard this step, and once
        /// `freeze` lay it down where it is, every part still. Nothing an avalanche is carrying is touched.
        /// </summary>
        public void Settle(float dt, bool freeze)
        {
            if (!Active || Frozen || CarriedFor > 0f) return;
            float k = Mathf.Exp(-dt * 8f);
            foreach (var p in parts)
            {
                if (p.gone || p.rb.isKinematic) continue;
                p.rb.linearVelocity *= k; p.rb.angularVelocity *= k;
                if (freeze) { p.rb.linearVelocity = Vector3.zero; p.rb.angularVelocity = Vector3.zero; p.rb.isKinematic = true; }
            }
            if (boardRb != null && !boardRb.isKinematic)
            {
                boardRb.linearVelocity *= k; boardRb.angularVelocity *= k;
                if (freeze) { boardRb.linearVelocity = Vector3.zero; boardRb.angularVelocity = Vector3.zero; boardRb.isKinematic = true; }
            }
            Frozen = freeze;
        }

        void Thaw()
        {
            Frozen = false;
            foreach (var p in parts) if (!p.gone) p.rb.isKinematic = false;
            if (boardRb != null) boardRb.isKinematic = false;
        }
    }
}
