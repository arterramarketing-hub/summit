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
        struct Part { public Transform bone; public Rigidbody rb; public Collider col; public Joint joint; }

        readonly RiderRig rig;
        readonly Transform board, boardHome;
        readonly Collider riderCollider;
        readonly List<Part> parts = new List<Part>();
        readonly PhysicsMaterial snowFeel;
        Rigidbody boardRb; BoxCollider boardCol;
        Rigidbody groundBody, boardGroundBody;
        Transform modelHome;
        Vector3 modelPos, boardPos; Quaternion modelRot, boardRot; Vector3 modelScale;
        Quaternion footRelL, footRelR;
        public bool Active { get; private set; }
        /// <summary>Something hit something: impact speed (m/s), the board?, where, and which bone (null for the board).</summary>
        public System.Action<float, bool, Vector3, Transform> Hit;
        /// <summary>A part sliding along the snow, every half metre or so: where, how fast.</summary>
        public System.Action<Vector3, float> Drag;

        /// <summary>Reports a ragdoll part's or the board's impacts, and its slides along the snow.</summary>
        class Impact : MonoBehaviour
        {
            public Ragdoll owner; public bool board;
            Vector3 lastMark; float lastTime;
            void OnCollisionEnter(Collision c)
            {
                Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
                lastMark = at; lastTime = Time.time;
                owner?.Hit?.Invoke(c.relativeVelocity.magnitude, board, at, board ? null : transform);
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

        public Ragdoll(RiderRig rig, Transform board, Collider riderCollider)
        {
            this.rig = rig;
            this.board = board;
            boardHome = board.parent;
            this.riderCollider = riderCollider;
            snowFeel = new PhysicsMaterial("RagdollSnow") { dynamicFriction = 0.85f, staticFriction = 1f, bounciness = 0.1f,
                                                            frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Average };
            groundBody = Slab("RagdollGround");
            boardGroundBody = Slab("BoardGround");
        }

        Rigidbody Slab(string name)
        {
            var g = new GameObject(name);
            var c = g.AddComponent<BoxCollider>();
            c.size = new Vector3(14f, 2f, 14f);
            c.center = new Vector3(0f, -1f, 0f);
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
            var model = rig.Root;
            modelHome = model.parent;
            modelPos = model.localPosition; modelRot = model.localRotation; modelScale = model.localScale;
            boardPos = board.localPosition; boardRot = board.localRotation;
            footRelL = Quaternion.Inverse(rig.ShinL.rotation) * rig.FootL.rotation;
            footRelR = Quaternion.Inverse(rig.ShinR.rotation) * rig.FootR.rotation;
            model.SetParent(null, true);
            board.SetParent(null, true);

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

            for (int i = 0; i < parts.Count; i++)
            {
                if (riderCollider != null) Physics.IgnoreCollision(parts[i].col, riderCollider);
                for (int j = i + 1; j < parts.Count; j++) Physics.IgnoreCollision(parts[i].col, parts[j].col);
            }
            // thrown forward and up, tumbling
            var spin = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f)) * 6f;
            foreach (var p in parts)
            {
                p.rb.linearVelocity = velocity * 0.85f + Vector3.up * 2.5f;
                p.rb.angularVelocity = spin;
            }

            // the board flies off on its own
            boardRb = board.gameObject.AddComponent<Rigidbody>();
            boardRb.mass = 4f;
            boardRb.interpolation = RigidbodyInterpolation.Interpolate;
            boardRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            boardCol = board.gameObject.AddComponent<BoxCollider>();
            boardCol.center = new Vector3(0f, 0.04f, 0f);
            boardCol.size = new Vector3(0.29f, 0.07f, 1.55f);
            boardCol.sharedMaterial = snowFeel;
            var bimp = board.gameObject.AddComponent<Impact>(); bimp.owner = this; bimp.board = true;
            if (riderCollider != null) Physics.IgnoreCollision(boardCol, riderCollider);
            foreach (var p in parts) Physics.IgnoreCollision(boardCol, p.col);
            boardRb.linearVelocity = velocity * 0.75f + Vector3.up * 3.5f + Random.insideUnitSphere * 2f;
            boardRb.angularVelocity = new Vector3(Random.Range(-9f, 9f), Random.Range(-4f, 4f), Random.Range(-9f, 9f));

            groundBody.gameObject.SetActive(true);
            boardGroundBody.gameObject.SetActive(true);
            PlaceSlab(groundBody, Centre, true);
            PlaceSlab(boardGroundBody, board.position, true);
        }

        enum Shape { Ball, Capsule, Head }

        void Add(Transform bone, Transform child, float mass, Shape shape, float radius, float twist, float swing1, float swing2, float stretch = 1f)
        {
            if (bone == null) return;
            float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.25f; rb.angularDamping = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 12;
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

            Joint joint = null;
            Rigidbody parentRb = null;
            for (var t = bone.parent; t != null && parentRb == null; t = t.parent) parentRb = t.GetComponent<Rigidbody>();
            if (parentRb != null)
            {
                var j = bone.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parentRb;
                j.autoConfigureConnectedAnchor = true;
                j.anchor = Vector3.zero;
                j.axis = axisLocal;
                j.swingAxis = Vector3.Cross(axisLocal, Mathf.Abs(axisLocal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                j.lowTwistLimit = new SoftJointLimit { limit = -twist };
                j.highTwistLimit = new SoftJointLimit { limit = twist };
                j.swing1Limit = new SoftJointLimit { limit = swing1 };
                j.swing2Limit = new SoftJointLimit { limit = swing2 };
                j.enableProjection = true;
                j.projectionDistance = 0.02f;
                j.projectionAngle = 20f;
                j.enablePreprocessing = false;
                joint = j;
            }
            parts.Add(new Part { bone = bone, rb = rb, col = col, joint = joint });
        }

        /// <summary>Each physics step: keep a slab of snow under the body and under the board.</summary>
        public void PhysicsStep()
        {
            if (!Active) return;
            PlaceSlab(groundBody, Centre, false);
            if (boardRb != null) PlaceSlab(boardGroundBody, boardRb.position, false);
        }

        static void PlaceSlab(Rigidbody slab, Vector3 at, bool snap)
        {
            Vector3 n = Slope.Normal(at.x, at.z);
            var pos = new Vector3(at.x, Slope.DrawnHeight(at.x, at.z), at.z);
            var rot = Quaternion.FromToRotation(Vector3.up, n);
            if (snap) { slab.position = pos; slab.rotation = rot; slab.transform.SetPositionAndRotation(pos, rot); }
            else { slab.MovePosition(pos); slab.MoveRotation(rot); }
        }

        /// <summary>Every frame while thrown: the feet are separate bones, so carry them on the shins.</summary>
        public void LateStep()
        {
            if (!Active) return;
            rig.FootL.SetPositionAndRotation(rig.EndL.position, rig.ShinL.rotation * footRelL);
            rig.FootR.SetPositionAndRotation(rig.EndR.position, rig.ShinR.rotation * footRelR);
        }

        static void Kill(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        /// <summary>Back on the board: physics off, bones home, model and board back on the rider.</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            foreach (var p in parts) { p.rb.isKinematic = true; p.col.enabled = false; }
            foreach (var p in parts) { if (p.joint != null) Kill(p.joint); }
            foreach (var p in parts) { Kill(p.col); Kill(p.rb); var im = p.bone.GetComponent<Impact>(); if (im != null) Kill(im); }
            var bi = board.GetComponent<Impact>(); if (bi != null) Kill(bi);
            parts.Clear();
            if (boardRb != null) { boardRb.isKinematic = true; boardCol.enabled = false; Kill(boardCol); Kill(boardRb); boardRb = null; }
            groundBody.gameObject.SetActive(false);
            boardGroundBody.gameObject.SetActive(false);
            var model = rig.Root;
            model.SetParent(modelHome, false);
            model.localPosition = modelPos; model.localRotation = modelRot; model.localScale = modelScale;
            board.SetParent(boardHome, false);
            board.localPosition = boardPos; board.localRotation = boardRot;
            rig.RestoreBind();
        }
    }
}
