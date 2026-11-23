using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// A sign or a flag that can be ridden into, by the rider or by the thrown body.
    ///  - A flag goes down: a knock bends it over into the snow the way you were going; a hard one snaps the pole
    ///    just above the snow and throws the top.
    ///  - A sign breaks: its posts snap above the snow, its board splits into slabs, and it all tumbles away.
    ///  - A SLOW banner tears where it was hit: the cloth comes off its stakes in two pieces that flap away, and a
    ///    stake you went right by is knocked over.
    /// Its colliders are trigger boxes, not solid ones, so it never stops the board dead the way a tree does. The
    /// rider and the thrown body look for them each physics step (Probe) rather than waiting for trigger messages,
    /// which also makes them work in the test tools; what a hit costs the rider is decided by Breakables and
    /// Rider.Breakthrough. Broken things lie where they land for the rest of the run; RestoreAll puts everything
    /// back for the next one.
    /// </summary>
    public class Breakable : MonoBehaviour
    {
        public enum Kind { Flag, Sign, Fence, Banner }
        public Kind kind;
        public bool Down { get; private set; }
        /// <summary>How tall a flag stands above its base (its holder's origin, on the snow).</summary>
        public float height = 1.5f;

        /// <summary>Something touched one: the breakable, and the collider that touched it.</summary>
        public static System.Action<Breakable, Collider> Touched;

        // what a sign is made of: posts that snap, and boards (a backing slab with a printed face) that split
        readonly List<Transform> posts = new List<Transform>();
        readonly List<Transform> backs = new List<Transform>(), faces = new List<Transform>();
        // everything as built, to put back; and the stumps and slices made when it broke, to throw away
        struct Saved { public Transform t, parent; public Vector3 p, s; public Quaternion r; public bool on; }
        readonly List<Saved> saved = new List<Saved>();
        readonly List<GameObject> made = new List<GameObject>();
        readonly List<BoxCollider> triggers = new List<BoxCollider>();

        static readonly List<Breakable> all = new List<Breakable>();

        // ---------------------------------------------------------------- building

        /// <summary>Make the holder (its origin on the snow at the foot of the pole) a flag that can be knocked down.</summary>
        public static Breakable Flag(Transform holder, float height)
        {
            var b = holder.gameObject.AddComponent<Breakable>();
            b.kind = Kind.Flag; b.height = height;
            // a little wider than the pole, so brushing past it with the board takes it down
            b.Box(new Vector3(0f, height * 0.5f + 0.1f, 0f), new Vector3(0.45f, height - 0.2f, 0.45f));
            b.Arm();
            return b;
        }

        /// <summary>
        /// A flag drawn as part of a mesh shared with others (PisteKit) until it is hit: reveal (called as it is hit,
        /// before it goes down) hides it in the shared mesh and gives the holder pieces of its own to topple; conceal
        /// (a new run, once it stands again) takes them away and shows it in the shared mesh.
        /// </summary>
        public static Breakable Flag(Transform holder, float height, System.Action reveal, System.Action conceal)
        {
            var b = Flag(holder, height);
            b.reveal = reveal; b.putBack = conceal;
            return b;
        }
        System.Action reveal;

        /// <summary>A piece of wood to throw when a fence span breaks: where it was, its size, what it is painted with.</summary>
        public struct Plank { public Vector3 at; public Quaternion rot; public Vector3 size; public Material mat; }

        // a fence span drawn as part of a mesh shared with its neighbours (Hamlets): taken out of it when it breaks,
        // handing back the wood to throw, and put back on a new run
        System.Func<List<Plank>> takeOut;
        System.Action putBack;

        /// <summary>
        /// Make the holder (in the middle of the span, turned along it) a fence span that breaks at the lightest
        /// touch. size: its box. takeOut hides the span in the shared mesh and returns its planks; putBack shows it.
        /// </summary>
        public static Breakable Span(Transform holder, Vector3 size, System.Func<List<Plank>> takeOut, System.Action putBack)
        {
            var b = holder.gameObject.AddComponent<Breakable>();
            b.kind = Kind.Fence; b.takeOut = takeOut; b.putBack = putBack;
            b.Box(Vector3.zero, size);
            b.Arm();
            return b;
        }

        // a SLOW banner: its cloth (the printed side, facing up the hill, and the side seen from below), and its stakes
        Transform clothFront, clothBack;
        float clothW, stakeLen;
        readonly List<Transform> stakes = new List<Transform>();

        /// <summary>
        /// Make the holder (on the snow under the middle of the banner, facing down the hill) a SLOW banner. front and
        /// back: the cloth's two sides (quads of w by h, the front facing up the hill); stakes: each pole, from a
        /// transform at its foot, stakeLen tall; hem: how high the cloth's bottom edge hangs.
        /// </summary>
        public static Breakable Banner(Transform holder, Transform front, Transform back, Transform[] stakes, float w, float h, float hem, float stakeLen)
        {
            var b = holder.gameObject.AddComponent<Breakable>();
            b.kind = Kind.Banner; b.clothFront = front; b.clothBack = back; b.clothW = w; b.height = h; b.stakeLen = stakeLen;
            b.stakes.AddRange(stakes);
            // stake to stake, from a hand above the snow to the top hem; as deep as a rider leaning into it
            float lo = 0.25f, hi = hem + h + 0.1f;
            b.Box(new Vector3(0f, (lo + hi) * 0.5f, 0f), new Vector3(w + 0.4f, hi - lo, 0.6f));
            b.Arm();
            return b;
        }

        /// <summary>
        /// What a rider with their feet at `feet`, going at vel, struck, before it breaks: on a sign, a post ("Shoulder"), else the
        /// board, by where it is on them ("Head", "Chest" or "Legs"); on a banner, a stake ("Shin"), else null (the
        /// cloth). from: the post or stake, or the middle of the board, to be knocked away from.
        /// </summary>
        public string HitPart(Vector3 feet, Vector3 vel, out Vector3 from)
        {
            from = transform.position;
            float best = float.MaxValue; Transform near = null;
            Vector3 dir = new Vector3(vel.x, 0f, vel.z);
            foreach (var p in kind == Kind.Banner ? stakes : posts)
            {
                // how far it is from the line you are on (the touch comes a little before you reach it)
                Vector3 d = p.position - feet; d.y = 0f;
                if (dir.sqrMagnitude > 0.01f) d = Vector3.ProjectOnPlane(d, dir.normalized);
                if (d.magnitude < best) { best = d.magnitude; near = p; }
            }
            // (a post's box and the rider's own reach: brushed by its edge counts)
            if (near != null && best < (kind == Kind.Banner ? 0.5f : 0.9f)) { from = near.position; return kind == Kind.Banner ? "Shin" : "Shoulder"; }
            if (kind == Kind.Banner) return null;
            if (backs.Count == 0) return "Chest";
            var r = backs[0].GetComponent<Renderer>();
            from = r.bounds.center;
            float rel = r.bounds.min.y - feet.y;
            return rel > 1.2f ? "Head" : rel > 0.5f ? "Chest" : "Legs";
        }

        /// <summary>Make the holder a sign; then name its posts and boards with Post and Board, and call Arm.</summary>
        public static Breakable Sign(Transform holder)
        {
            var b = holder.gameObject.AddComponent<Breakable>();
            b.kind = Kind.Sign;
            return b;
        }

        /// <summary>A vertical post (a cylinder or cube primitive) that snaps above the snow.</summary>
        public void Post(Transform post)
        {
            posts.Add(post);
            // the post from just above the snow to its top
            var r = post.GetComponent<Renderer>();
            float ground = Slope.DrawnHeight(post.position.x, post.position.z);
            float bottom = Mathf.Max(r.bounds.min.y, ground + 0.1f), top = r.bounds.max.y;
            if (top - bottom < 0.2f) return;
            Vector3 c = transform.InverseTransformPoint(new Vector3(post.position.x, (bottom + top) * 0.5f, post.position.z));
            float w = Mathf.Max(0.4f, Mathf.Max(post.lossyScale.x, post.lossyScale.z) + 0.25f);
            Box(c, new Vector3(w, top - bottom, w));
        }

        /// <summary>A board: its backing slab (a cube primitive) and the printed face on it (a quad, or null).</summary>
        public void Board(Transform back, Transform face)
        {
            backs.Add(back); faces.Add(face);
            Vector3 c = transform.InverseTransformPoint(back.position);
            Vector3 size = back.lossyScale;
            // as thick as a rider leaning into it, so a jump into the board itself counts
            Box(c, new Vector3(size.x, size.y, Mathf.Max(size.z, 0.5f)));
        }

        void Box(Vector3 centre, Vector3 size)
        {
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.isTrigger = true; bc.center = centre; bc.size = size;
            triggers.Add(bc);
        }

        /// <summary>Ready to be hit: remember how it was built, to put it back.</summary>
        public void Arm()
        {
            saved.Clear();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                saved.Add(new Saved { t = t, parent = t.parent, p = t.localPosition, r = t.localRotation, s = t.localScale, on = t.gameObject.activeSelf });
            if (!all.Contains(this)) all.Add(this);
        }

        static readonly Collider[] near = new Collider[32];

        /// <summary>Anything breakable within r of the line a..b is touched by `by` (the rider's or a thrown body's collider).</summary>
        public static void Probe(Vector3 a, Vector3 b, float r, Collider by)
        {
            if (all.Count == 0 || Touched == null || by == null) return;
            int n = Physics.OverlapCapsuleNonAlloc(a, b, r, near, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                if (!near[i].isTrigger) continue;
                var bk = near[i].GetComponent<Breakable>();
                if (bk != null && !bk.Down) Touched(bk, by);
            }
        }

        void OnDestroy()
        {
            all.Remove(this);
            moving.RemoveAll(m => m.t == null || m.owner == this);
            foreach (var g in made) if (g != null) Kill(g);
            made.Clear();
        }

        // ---------------------------------------------------------------- breaking

        /// <summary>
        /// Knock it down / break it. at: where it was hit; vel: the velocity of what hit it.
        /// Returns the middle of what broke (for the snow and splinters).
        /// </summary>
        public Vector3 Break(Vector3 at, Vector3 vel)
        {
            if (Down) return transform.position;
            Down = true;
            foreach (var bc in triggers) bc.enabled = false;
            reveal?.Invoke();
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            Vector3 dir = flat.sqrMagnitude > 0.04f ? flat.normalized : transform.forward;
            float speed = flat.magnitude;
            if (kind == Kind.Flag) return speed > SnapSpeed ? SnapFlag(dir, speed) : Bend(dir, speed);
            if (kind == Kind.Fence) return Splinter(at, vel, dir);
            if (kind == Kind.Banner) return Tear(at, vel, dir, speed);
            return Shatter(at, vel, dir);
        }

        /// <summary>Faster than this (m/s), a flag's pole snaps instead of bending over.</summary>
        public const float SnapSpeed = 14f;

        Vector3 Bend(Vector3 dir, float speed)
        {
            Fell(transform, height, dir, speed);
            return transform.position + Vector3.up * 0.4f;
        }

        // whacked over at the foot (t: at the foot of a pole `length` tall): it swings down into the snow the way you
        // were going, its foot dragged a little
        void Fell(Transform t, float length, Vector3 dir, float speed)
        {
            moving.Add(new Moving
            {
                owner = this, t = t, topple = true, pivot = t.position, origin = t.position, rot0 = t.rotation,
                axis = Vector3.Cross(Vector3.up, dir).normalized, tip0 = t.position + Vector3.up * length,
                spin = Mathf.Clamp(speed * 0.3f, 2.5f, 8f), slide = dir * Mathf.Min(2f, speed * 0.08f), length = length
            });
        }

        Vector3 SnapFlag(Vector3 dir, float speed)
        {
            // the pole breaks a hand's width above the snow; the stump stays, the rest is thrown ahead of you
            Vector3 foot = transform.position;
            float cut = Random.Range(0.18f, 0.32f);
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (IsPole(r) && r.bounds.min.y < foot.y + cut) SnapPost(r.transform, foot.y + cut, transform.parent);
            var top = new GameObject(name + " top").transform;
            top.SetParent(transform.parent, false);
            top.position = foot + Vector3.up * (cut + (height - cut) * 0.5f);
            top.rotation = transform.rotation;
            made.Add(top.gameObject);
            // (what lies below the break, like the heap of snow round the foot, stays)
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i); var cr = c.GetComponent<Renderer>();
                if (cr == null || cr.bounds.max.y > foot.y + cut + 0.02f) c.SetParent(top, true);
            }
            Throw(top, new Vector3(0.05f, (height - cut) * 0.5f, 0.05f), dir * speed * Random.Range(0.35f, 0.5f) + Vector3.up * Random.Range(2f, 4f),
                  Vector3.Cross(Vector3.up, dir) * Random.Range(6f, 11f) + Random.insideUnitSphere * 2f);
            return foot + Vector3.up * cut;
        }

        // a pole is the tall thin part, as opposed to the pennant, cap or snow mound
        static bool IsPole(Renderer r) { var e = r.bounds.extents; return e.y > 0.3f && e.x < 0.2f && e.z < 0.2f; }

        Vector3 Shatter(Vector3 at, Vector3 vel, Vector3 dir)
        {
            var b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            // the posts snap just above the snow, and their tops fly
            foreach (var p in posts)
            {
                float ground = Slope.DrawnHeight(p.position.x, p.position.z);
                var upper = SnapPost(p, ground + Random.Range(0.25f, 0.5f), transform);
                if (upper == null) continue;
                var e = upper.lossyScale; bool cyl = IsCylinder(upper);
                Fling(upper, new Vector3(e.x * 0.5f, e.y * (cyl ? 1f : 0.5f), e.z * 0.5f), at, vel, dir);
            }
            // the boards split into slabs, each its piece of the backing with its piece of the print
            for (int i = 0; i < backs.Count; i++)
            {
                var back = backs[i]; var face = faces[i];
                float w = back.lossyScale.x;
                int n = Mathf.Clamp(Mathf.RoundToInt(w / 1.4f), 2, 6);
                // uneven cuts: it splits where it splits
                var cuts = new float[n + 1];
                cuts[0] = 0f; cuts[n] = 1f;
                for (int k = 1; k < n; k++) cuts[k] = (k + Random.Range(-0.3f, 0.3f)) / n;
                for (int k = 0; k < n; k++) Slab(back, face, cuts[k], cuts[k + 1], at, vel, dir);
                back.gameObject.SetActive(false);
                if (face != null) face.gameObject.SetActive(false);
            }
            return b.size.sqrMagnitude > 0f ? Vector3.Lerp(b.center, at, 0.5f) : at;
        }

        static bool IsCylinder(Transform t) { var mf = t.GetComponent<MeshFilter>(); return mf != null && mf.sharedMesh != null && mf.sharedMesh.bounds.size.y > 1.5f; }

        /// <summary>
        /// Cut a vertical post at height y: a stump (a copy, cut down, under stumpParent) stays; the post itself is
        /// trimmed to the part above, which is returned (null if the cut is above its top).
        /// </summary>
        Transform SnapPost(Transform post, float y, Transform stumpParent)
        {
            var r = post.GetComponent<Renderer>();
            float bottom = r.bounds.min.y, top = r.bounds.max.y;
            if (y >= top - 0.1f) return null;
            float unit = IsCylinder(post) ? 2f : 1f;   // the primitive's own height
            if (y > bottom + 0.05f)
            {
                var stump = Instantiate(post.gameObject, stumpParent, true);
                stump.name = post.name + " stump";
                stump.transform.position = new Vector3(post.position.x, (bottom + y) * 0.5f, post.position.z);
                stump.transform.localScale = Rescale(stump.transform, (y - bottom) / unit);
                made.Add(stump);
            }
            else y = bottom;
            post.position = new Vector3(post.position.x, (y + top) * 0.5f, post.position.z);
            post.localScale = Rescale(post, (top - y) / unit);
            return post;
        }

        static Vector3 Rescale(Transform t, float worldY)
        {
            float py = t.parent != null ? Mathf.Max(1e-4f, t.parent.lossyScale.y) : 1f;
            var s = t.localScale; s.y = worldY / py; return s;
        }

        /// <summary>The piece of a board between fractions u0 and u1 of its width, thrown.</summary>
        void Slab(Transform back, Transform face, float u0, float u1, Vector3 at, Vector3 vel, Vector3 dir)
        {
            float w = back.lossyScale.x;
            var g = new GameObject("Slab").transform;
            g.SetParent(transform, false);
            g.SetPositionAndRotation(back.TransformPoint(new Vector3((u0 + u1) * 0.5f - 0.5f, 0f, 0f)), back.rotation);
            made.Add(g.gameObject);
            // the backing
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(slab.GetComponent<Collider>());
            slab.transform.SetParent(g, false);
            slab.transform.localScale = new Vector3(w * (u1 - u0), back.lossyScale.y, back.lossyScale.z);
            var br = back.GetComponent<MeshRenderer>();
            var sr = slab.GetComponent<MeshRenderer>();
            sr.sharedMaterial = br.sharedMaterial; sr.shadowCastingMode = br.shadowCastingMode;
            // the print: the same strip of the face, as a quad of its own
            if (face != null)
            {
                float fw = face.lossyScale.x;
                // where this strip's edges fall across the face (it is a little narrower than the backing)
                float a = Vector3.Dot(back.TransformPoint(new Vector3(u0 - 0.5f, 0f, 0f)) - face.position, face.right) / fw + 0.5f;
                float c = Vector3.Dot(back.TransformPoint(new Vector3(u1 - 0.5f, 0f, 0f)) - face.position, face.right) / fw + 0.5f;
                a = Mathf.Clamp01(a); c = Mathf.Clamp01(c);
                if (c - a > 0.01f)
                {
                    var q = new GameObject("Print", typeof(MeshFilter), typeof(MeshRenderer));
                    q.transform.SetParent(g, false);
                    q.transform.SetPositionAndRotation(face.position + face.right * ((a + c) * 0.5f - 0.5f) * fw, face.rotation);
                    q.transform.localScale = new Vector3(fw * (c - a), face.lossyScale.y, 1f);
                    var mesh = Strip(a, c);
                    q.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var fr = face.GetComponent<MeshRenderer>();
                    var qr = q.GetComponent<MeshRenderer>();
                    qr.sharedMaterial = fr.sharedMaterial; qr.shadowCastingMode = fr.shadowCastingMode;
                }
            }
            Fling(g, new Vector3(w * (u1 - u0) * 0.5f, back.lossyScale.y * 0.5f, Mathf.Max(0.03f, back.lossyScale.z * 0.5f)), at, vel, dir);
        }

        /// <summary>A unit quad facing -Z, like Unity's own, showing only the strip u0..u1 of its texture.</summary>
        static Mesh Strip(float u0, float u1)
        {
            var m = new Mesh { name = "PrintStrip" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 3, 1, 3, 0, 2 };
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// A fence span: its rails snap in two and fly, with any post left with no rail either side; the span is
        /// taken out of the fence's shared mesh (no object per span while it stands).
        /// </summary>
        Vector3 Splinter(Vector3 at, Vector3 vel, Vector3 dir)
        {
            var planks = takeOut != null ? takeOut() : null;
            if (planks == null) return transform.position;
            foreach (var pk in planks)
            {
                bool rail = pk.size.z > pk.size.y;
                // a rail breaks where it was hit (near enough its middle); a post goes whole
                int parts = rail ? 2 : 1;
                for (int i = 0; i < parts; i++)
                {
                    float f = parts == 1 ? 0f : (i - 0.5f) * 0.5f;
                    var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    DestroyImmediate(g.GetComponent<Collider>());
                    g.name = rail ? "Rail" : "Post";
                    g.transform.SetParent(transform, false);
                    g.transform.SetPositionAndRotation(pk.at + pk.rot * new Vector3(0f, 0f, f * pk.size.z), pk.rot);
                    var s = pk.size; if (rail) s.z *= 0.5f * Random.Range(0.85f, 1f);
                    g.transform.localScale = s;
                    var r = g.GetComponent<MeshRenderer>();
                    r.sharedMaterial = pk.mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    made.Add(g);
                    Fling(g.transform, s * 0.5f, at, vel, dir);
                }
            }
            return transform.position;
        }

        /// <summary>
        /// A banner: the cloth rips where it was hit and both pieces are torn off their stakes and blown away,
        /// flapping; a stake the rider went right by goes over, the other stays standing.
        /// </summary>
        Vector3 Tear(Vector3 at, Vector3 vel, Vector3 dir, float speed)
        {
            Vector3 mid = clothFront.position;
            float u = Mathf.Clamp(clothFront.InverseTransformPoint(at).x + 0.5f, 0.2f, 0.8f);   // (the quad is a unit wide in its own space)
            Cloth(0f, u, at, vel, dir);
            Cloth(u, 1f, at, vel, dir);
            clothFront.gameObject.SetActive(false);
            if (clothBack != null) clothBack.gameObject.SetActive(false);
            foreach (var s in stakes)
            {
                Vector3 d = s.position - at; d.y = 0f;
                if (Vector3.ProjectOnPlane(d, dir).magnitude < 1.1f) Fell(s, stakeLen, dir, speed);
            }
            return mid;
        }

        /// <summary>The piece of the banner between fractions u0 and u1 of its width (both sides of it), thrown flapping.</summary>
        void Cloth(float u0, float u1, Vector3 at, Vector3 vel, Vector3 dir)
        {
            float w = clothW * (u1 - u0), h = height;
            var g = new GameObject("Cloth").transform;
            g.SetParent(transform, false);
            g.SetPositionAndRotation(clothFront.position + clothFront.right * ((u0 + u1) * 0.5f - 0.5f) * clothW, clothFront.rotation);
            made.Add(g.gameObject);
            for (int side = 0; side < 2; side++)
            {
                var src = side == 0 ? clothFront : clothBack;
                if (src == null) continue;
                var q = new GameObject("Print", typeof(MeshFilter), typeof(MeshRenderer));
                q.transform.SetParent(g, false);
                // the back is the same print seen through the cloth: turned round, so its strip runs the other way
                q.transform.localRotation = side == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                q.transform.localPosition = new Vector3(0f, 0f, side == 0 ? 0f : 0.004f);
                q.transform.localScale = new Vector3(w, h, 1f);
                q.GetComponent<MeshFilter>().sharedMesh = side == 0 ? Strip(u0, u1) : Strip(1f - u1, 1f - u0);
                var sr = src.GetComponent<MeshRenderer>(); var qr = q.GetComponent<MeshRenderer>();
                qr.sharedMaterial = sr.sharedMaterial; qr.shadowCastingMode = sr.shadowCastingMode;
            }
            // off the way the rider was going, slower than them (cloth catches the air), and away from the rip
            Vector3 away = clothFront.right * ((u0 + u1) * 0.5f < 0.5f ? -1f : 1f);
            Vector3 v = vel * Random.Range(0.3f, 0.5f) + Vector3.up * Random.Range(1.5f, 3f) + away * Random.Range(1f, 2.5f);
            Vector3 spin = Vector3.Cross(Vector3.up, dir) * Random.Range(2f, 5f) + Random.insideUnitSphere * 4f;
            moving.Add(new Moving { owner = this, t = g, ext = new Vector3(w * 0.5f, h * 0.5f, 0.02f), vel = v, angVel = spin, cloth = true });
        }

        /// <summary>Throw a broken piece: mostly the way the hit was going, harder the nearer it was to the hit.</summary>
        void Fling(Transform piece, Vector3 ext, Vector3 at, Vector3 vel, Vector3 dir)
        {
            Vector3 away = piece.position - at; away.y = 0f;
            float near = Mathf.Clamp01(1f - away.magnitude / 6f);
            float k = 0.25f + 0.75f * near;
            // (only a share of the hit's speed: a fast rider on a steep pitch would otherwise send it a hundred metres)
            Vector3 v = vel * Random.Range(0.18f, 0.38f) * k + Vector3.up * Random.Range(1.5f, 4.5f) * (0.4f + 0.6f * k)
                      + (away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere) * Random.Range(0.5f, 2.5f);
            Vector3 w = Vector3.Cross(Vector3.up, dir) * Random.Range(3f, 10f) * k + Random.insideUnitSphere * 3f;
            Throw(piece, ext, v, w);
        }

        void Throw(Transform piece, Vector3 ext, Vector3 v, Vector3 w)
        {
            moving.Add(new Moving { owner = this, t = piece, ext = ext, vel = v, angVel = w });
        }

        // ---------------------------------------------------------------- putting it back

        /// <summary>Everything standing again, as built (a new run on the same mountain).</summary>
        public static void RestoreAll()
        {
            moving.Clear();
            foreach (var b in all) if (b != null) b.Restore();
            Restored?.Invoke();
        }

        /// <summary>Everything was put back for a new run (PisteKit ties its rope lines up again).</summary>
        public static event System.Action Restored;

        void Restore()
        {
            if (!Down) return;
            foreach (var s in saved)
            {
                if (s.t == null) continue;
                if (s.t != transform) s.t.SetParent(s.parent, false);
                s.t.localPosition = s.p; s.t.localRotation = s.r; s.t.localScale = s.s;
                s.t.gameObject.SetActive(s.on);
            }
            foreach (var g in made) if (g != null) Kill(g);
            made.Clear();
            putBack?.Invoke();
            foreach (var bc in triggers) bc.enabled = true;
            Down = false;
        }

        static void Kill(GameObject g)
        {
            // the slabs' print strips are meshes of their own
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.name == "PrintStrip") { if (Application.isPlaying) Destroy(mf.sharedMesh); else DestroyImmediate(mf.sharedMesh); }
            if (Application.isPlaying) Destroy(g); else DestroyImmediate(g);
        }

        // ---------------------------------------------------------------- the pieces in motion

        class Moving
        {
            public Breakable owner; public Transform t;
            // toppling about its foot
            public bool topple; public Vector3 pivot, origin, axis, tip0, slide; public Quaternion rot0; public float angle, spin, length;
            // thrown free
            public Vector3 ext, vel, angVel; public bool grounded;
            public bool cloth;   // a piece of a banner: it falls slowly, flapping, and the air slows it
            public float age;
        }
        static readonly List<Moving> moving = new List<Moving>();
        /// <summary>Snow thrown up where a piece lands (set by the game: where, how hard).</summary>
        public static System.Action<Vector3, float> Landed;
        public static int MovingCount => moving.Count;

        /// <summary>Moves the falling and flying pieces on (from Breakables.Update, or a test tool).</summary>
        public static void Step(float dt)
        {
            for (int i = moving.Count - 1; i >= 0; i--)
            {
                var m = moving[i];
                if (m.t == null) { moving.RemoveAt(i); continue; }
                m.age += dt;
                bool done = m.topple ? Topple(m, dt) : Tumble(m, dt);
                if (done || m.age > 8f) moving.RemoveAt(i);
            }
        }

        static bool Topple(Moving m, float dt)
        {
            // a pole falling over: gravity's pull grows as it leans
            m.spin += 1.5f * Rider.G / Mathf.Max(0.5f, m.length) * Mathf.Sin(Mathf.Max(m.angle, 0.05f)) * dt;
            m.angle += m.spin * dt;
            m.pivot += m.slide * dt;
            m.slide *= Mathf.Exp(-dt * 4f);
            m.pivot.y = Slope.DrawnHeight(m.pivot.x, m.pivot.z);
            var q = Quaternion.AngleAxis(m.angle * Mathf.Rad2Deg, m.axis);
            Vector3 tip = m.pivot + q * (m.tip0 - m.origin);
            bool down = tip.y <= Slope.DrawnHeight(tip.x, tip.z) + 0.04f || m.angle > 3f;
            if (down)
            {
                // lying in the snow: back off to just touching it
                for (int k = 0; k < 12 && tip.y < Slope.DrawnHeight(tip.x, tip.z) + 0.04f; k++)
                {
                    m.angle -= 0.02f;
                    q = Quaternion.AngleAxis(m.angle * Mathf.Rad2Deg, m.axis);
                    tip = m.pivot + q * (m.tip0 - m.origin);
                }
                Landed?.Invoke(tip, 3f + m.spin);
            }
            m.t.SetPositionAndRotation(m.pivot, q * m.rot0);
            return down;
        }

        static bool Tumble(Moving m, float dt)
        {
            m.vel.y -= Rider.G * (m.cloth ? 0.35f : 1f) * dt;
            if (m.cloth && !m.grounded)
            {
                // cloth in the air: dragged back to drifting, and turned this way and that as it flaps
                m.vel *= Mathf.Exp(-dt * 1.6f);
                m.angVel += Random.insideUnitSphere * (24f * dt);
            }
            Vector3 p = m.t.position + m.vel * dt;
            Quaternion r = m.t.rotation;
            float w = m.angVel.magnitude;
            if (w > 1e-3f) r = Quaternion.AngleAxis(w * dt * Mathf.Rad2Deg, m.angVel / w) * r;
            // how far its lowest corner reaches below its middle, the way it is turned now
            float reach = Mathf.Abs((r * Vector3.right).y) * m.ext.x + Mathf.Abs((r * Vector3.up).y) * m.ext.y + Mathf.Abs((r * Vector3.forward).y) * m.ext.z;
            float ground = Slope.DrawnHeight(p.x, p.z);
            bool settled = false;
            if (p.y - reach < ground)
            {
                p.y = ground + reach;
                if (!m.grounded && m.vel.y < -3f) Landed?.Invoke(p, -m.vel.y);
                m.grounded = true;
                if (m.vel.y < 0f) m.vel.y = -m.vel.y * 0.2f;
                // the snow grabs it: it slides to a stop and rolls over onto its flattest side
                m.vel.x *= Mathf.Exp(-dt * 3.5f); m.vel.z *= Mathf.Exp(-dt * 3.5f);
                m.angVel *= Mathf.Exp(-dt * 5f);
                Vector3 n = Slope.Normal(p.x, p.z);
                Vector3 flat = FlatAxis(r, m.ext, n, out float half);
                var lie = Quaternion.FromToRotation(flat, Vector3.Dot(flat, n) >= 0f ? n : -n) * r;
                r = Quaternion.Slerp(r, lie, 1f - Mathf.Exp(-dt * 4f));
                if (m.vel.sqrMagnitude < 0.3f && m.angVel.sqrMagnitude < 0.5f)
                {
                    r = lie;
                    p.y = ground + half / Mathf.Max(0.5f, n.y);
                    settled = true;
                }
            }
            m.t.SetPositionAndRotation(p, r);
            return settled;
        }

        /// <summary>The axis of a piece's thinnest side most nearly facing n (a pole has two to choose from), and its half thickness.</summary>
        static Vector3 FlatAxis(Quaternion r, Vector3 ext, Vector3 n, out float half)
        {
            float min = Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z));
            Vector3 best = r * Vector3.up; float bestHalf = ext.y, bestDot = -1f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 local = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                float e = ext[i];
                if (e > min * 1.5f) continue;
                Vector3 a = r * local;
                float d = Mathf.Abs(Vector3.Dot(a, n));
                if (d > bestDot) { bestDot = d; best = a; bestHalf = e; }
            }
            half = bestHalf;
            return best;
        }
    }
}
