using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Life on the mountain: flocks of birds crossing the sky, crows on the snow that take off
    /// as you come past, and in the danger zones the chance of a bear charging out of the trees.
    /// </summary>
    public class Wildlife : MonoBehaviour
    {
        public Rider rider;
        public System.Action BearSpotted;
        public System.Action<Vector3> CrowsTookOff;

        /// <summary>The nearest flock in the air within `range`, for birdsong.</summary>
        public bool NearestFlock(Vector3 from, float range, out Vector3 at)
        {
            at = Vector3.zero; float best = range * range; bool any = false;
            foreach (var f in flocks)
            {
                if (f.perched && f.lift <= 0f) continue;
                float d = (f.pos - from).sqrMagnitude;
                if (d < best) { best = d; at = f.pos; any = true; }
            }
            return any;
        }

        // ---------------------------------------------------------------- birds

        class Bird { public Transform t, wl, wr; public Vector3 off; public float phase, rate; }
        class Flock { public Vector3 pos, vel; public List<Bird> birds = new List<Bird>(); public bool perched; public float lift; }

        readonly List<Flock> flocks = new List<Flock>();
        Mesh body, wing;
        Material birdMat;
        float nextPerch;

        // ---------------------------------------------------------------- bear

        GameObject bear;
        Rigidbody bearRb;
        Animation bearAnim;
        float bearSpeed, bearTimer, bearYaw;
        int bearCycle = -1;
        bool bearActive;
        float bearFwdYaw;   // the model's own facing, so it runs head first
        public static float BearYawFix = 180f;   // the file's head points down -Z

        public void Build(Shader lit)
        {
            birdMat = new Material(lit);
            birdMat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.14f));
            birdMat.SetFloat("_Smoothness", 0.1f);
            body = Diamond();
            wing = WingMesh();
            for (int i = 0; i < 4; i++) flocks.Add(NewFlock(false, 7 + i * 2));
            for (int i = 0; i < 2; i++) flocks.Add(NewFlock(true, 4));
            BuildBear();
        }

        public void ResetRun()
        {
            bearCycle = -1;
            if (bear != null) { bear.SetActive(false); bearActive = false; }
            foreach (var f in flocks) Respawn(f, true);
            nextPerch = 0f;
        }

        Flock NewFlock(bool perched, int count)
        {
            var f = new Flock { perched = perched };
            var root = new GameObject(perched ? "Crows" : "Flock").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < count; i++)
            {
                var b = new Bird();
                b.t = new GameObject("Bird").transform; b.t.SetParent(root, false);
                Part(b.t, body, Vector3.zero);
                b.wl = new GameObject("WingL").transform; b.wl.SetParent(b.t, false); b.wl.localPosition = new Vector3(-0.04f, 0.02f, 0f);
                Part(b.wl, wing, Vector3.zero).localScale = new Vector3(-1f, 1f, 1f);
                b.wr = new GameObject("WingR").transform; b.wr.SetParent(b.t, false); b.wr.localPosition = new Vector3(0.04f, 0.02f, 0f);
                Part(b.wr, wing, Vector3.zero);
                // a loose V for flying flocks, a scatter on the ground for crows
                b.off = perched ? new Vector3(Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f))
                                : new Vector3((i % 2 == 0 ? 1 : -1) * (i + 1) * 0.9f, Random.Range(-0.4f, 0.4f), -(i + 1) * 1.1f);
                b.phase = Random.value * 6f; b.rate = Random.Range(7f, 10f);
                float sc = perched ? 0.55f : Random.Range(0.8f, 1.1f);
                b.t.localScale = Vector3.one * sc;
                f.birds.Add(b);
            }
            return f;
        }

        Transform Part(Transform parent, Mesh m, Vector3 pos)
        {
            var g = new GameObject("Mesh");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = birdMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        void Respawn(Flock f, bool initial)
        {
            Vector3 rp = rider != null ? rider.transform.position : Vector3.zero;
            if (f.perched)
            {
                // crows sit on the snow at the edge of the piste ahead
                float z = rp.z + Random.Range(90f, 220f);
                float x = Slope.Centre(z) + (Random.value < 0.5f ? -1f : 1f) * Random.Range(10f, 18f);
                f.pos = new Vector3(x, Slope.Height(x, z), z);
                f.vel = Vector3.zero; f.lift = 0f;
            }
            else
            {
                float z = rp.z + Random.Range(initial ? 40f : 120f, 260f);
                float side = Random.value < 0.5f ? -1f : 1f;
                float x = Slope.Centre(z) - side * Random.Range(60f, 120f);
                f.pos = new Vector3(x, Slope.Height(Slope.Centre(z), z) + Random.Range(25f, 55f), z);
                f.vel = new Vector3(side * Random.Range(8f, 12f), Random.Range(-0.5f, 0.8f), Random.Range(-4f, 6f));
            }
        }

        void Update()
        {
            if (rider == null || body == null) return;
            float dt = Time.deltaTime, t = Time.time;
            Vector3 rp = rider.transform.position;
            foreach (var f in flocks)
            {
                if (f.perched)
                {
                    float d = Vector3.Distance(rp, f.pos);
                    if (f.lift <= 0f && d < 24f && rider.Running)
                        { f.lift = 0.001f; f.vel = new Vector3(Mathf.Sign(f.pos.x - rp.x) * 6f, 5f, 4f); CrowsTookOff?.Invoke(f.pos); }
                    if (f.lift > 0f) { f.lift += dt; f.vel.y += dt * 1.5f; f.pos += f.vel * dt; }
                    if (f.pos.z < rp.z - 40f || f.lift > 12f) Respawn(f, false);
                }
                else
                {
                    f.pos += f.vel * dt;
                    if (f.pos.z < rp.z - 60f || Mathf.Abs(f.pos.x - rp.x) > 220f) Respawn(f, false);
                }
                Vector3 fwd = f.vel.sqrMagnitude > 0.01f ? f.vel.normalized : Vector3.forward;
                Quaternion face = Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z).sqrMagnitude > 1e-4f ? new Vector3(fwd.x, fwd.y * 0.3f, fwd.z) : Vector3.forward);
                bool flying = !f.perched || f.lift > 0f;
                foreach (var b in f.birds)
                {
                    Vector3 off = face * b.off;
                    if (f.perched && f.lift > 0f) off *= 1f + f.lift * 0.6f;
                    Vector3 pos = f.pos + off;
                    if (f.perched && f.lift <= 0f) pos.y = Slope.Height(pos.x, pos.z) + 0.05f;
                    b.t.SetPositionAndRotation(pos + Vector3.up * (flying ? 0.15f * Mathf.Sin(t * 1.3f + b.phase) : 0f), face);
                    // flap (with the odd glide); folded on the ground with a peck now and then
                    float flap = flying ? (Mathf.Sin(t * 0.7f + b.phase) > 0.6f ? 10f : Mathf.Sin(t * b.rate + b.phase) * 50f) : 80f;
                    b.wl.localRotation = Quaternion.Euler(0f, 0f, -flap);
                    b.wr.localRotation = Quaternion.Euler(0f, 0f, flap);
                }
            }
            UpdateBear(dt);
        }

        static Mesh Diamond()
        {
            var m = new Mesh { name = "BirdBody" };
            var v = new[] { new Vector3(0, 0, 0.32f), new Vector3(0.07f, 0, 0), new Vector3(0, 0.06f, 0), new Vector3(-0.07f, 0, 0), new Vector3(0, -0.05f, 0), new Vector3(0, 0.01f, -0.3f) };
            m.vertices = v;
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 5, 2, 1, 5, 3, 2, 5, 4, 3, 5, 1, 4 };
            m.RecalculateNormals();
            return m;
        }

        static Mesh WingMesh()
        {
            var m = new Mesh { name = "Wing" };
            m.vertices = new[] { new Vector3(0, 0, 0.1f), new Vector3(0, 0, -0.1f), new Vector3(0.45f, 0, -0.12f), new Vector3(0.3f, 0, 0.04f) };
            m.triangles = new[] { 0, 3, 2, 0, 2, 1, 0, 2, 3, 0, 1, 2 };
            m.RecalculateNormals();
            return m;
        }

        // ---------------------------------------------------------------- the bear

        void BuildBear()
        {
            var prefab = Resources.Load<GameObject>("Animals/Bear");
            if (prefab == null) return;
            bear = new GameObject("Bear");
            // the file's clip keys the model root's own position, rotation and scale (x100), so all
            // sizing and placing goes on a wrapper above it, measured with the clip applied
            var wrap = new GameObject("BearWrap").transform;
            wrap.SetParent(bear.transform, false);
            var model = Instantiate(prefab, wrap, false);
            bearAnim = model.GetComponentInChildren<Animation>();
            if (bearAnim != null)
            {
                bearAnim.wrapMode = WrapMode.Loop;
                foreach (AnimationState s in bearAnim) { s.wrapMode = WrapMode.Loop; bearAnim.Play(s.name); s.time = 0f; }
                bearAnim.Sample();
            }
            var vmat = Resources.Load<Material>("VertexColor");
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (vmat != null)
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = vmat;
                    r.sharedMaterials = ms;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            foreach (var c in model.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            // size it: about 1.4 m at the shoulder, and find which way its long axis runs
            var b = new Bounds(model.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any && b.size.y > 0.001f)
            {
                float k = 1.5f / b.size.y;
                wrap.localScale = Vector3.one * k;
                wrap.localPosition = new Vector3(-b.center.x * k, -b.min.y * k, -b.center.z * k);
                bearFwdYaw = (b.size.x > b.size.z ? 90f : 0f) + BearYawFix;
            }
            bearRb = bear.AddComponent<Rigidbody>();
            bearRb.isKinematic = true;
            bearRb.interpolation = RigidbodyInterpolation.Interpolate;
            var col = bear.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.6f; col.height = 2.2f; col.center = new Vector3(0f, 0.75f, 0f);
            var ob = bear.AddComponent<Obstacle>();
            ob.label = "Bear!"; ob.always = true;
            bear.SetActive(false);
        }

        public bool BearActive => bearActive;
        public float BearSpeed => bearActive ? bearSpeed : 0f;
        public Vector3 BearPosition => bear != null ? bear.transform.position : Vector3.zero;

        void UpdateBear(float dt)
        {
            if (bear == null || !rider.Running) return;
            Vector3 rp = rider.transform.position;
            // once per danger zone, maybe: a bear breaks out of the trees ahead and to the side
            int cyc = Course.CycleOf(rp.z);
            if (!bearActive && cyc != bearCycle && Course.At(rp.z + 60f) == Course.Section.Danger && Course.At(rp.z) != Course.Section.Danger)
            {
                bearCycle = cyc;
                if (Slope.Hash01(cyc, 5, 77) < 0.7f || cyc == 0)
                {
                    float side = Slope.Hash01(cyc, 6, 77) < 0.5f ? -1f : 1f;
                    float z = rp.z + 75f;
                    float x = Slope.Centre(z) + side * 30f;
                    var p = new Vector3(x, Slope.DrawnHeight(x, z), z);
                    bear.SetActive(true);
                    bear.transform.position = p;
                    bearRb.position = p;
                    bearYaw = side > 0f ? -90f : 90f;
                    bearSpeed = 4f; bearTimer = 0f; bearActive = true;
                    if (bearAnim != null) bearAnim.Play();
                    BearSpotted?.Invoke();
                }
            }
            if (!bearActive) return;
            bearTimer += dt;
            Vector3 bp = bearRb.position;
            // charge at where the rider is about to be; a bear tops out near 50 km/h, so speed is your escape
            Vector3 aim = rp + rider.Velocity * 0.7f;
            Vector3 to = aim - bp; to.y = 0f;
            bool chasing = bearTimer < 14f && rp.z - bp.z < 28f;
            float want = chasing ? 14f : 0f;
            bearSpeed = Mathf.MoveTowards(bearSpeed, want, dt * (chasing ? 7f : 10f));
            if (to.sqrMagnitude > 1f && chasing)
            {
                float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                bearYaw = Mathf.MoveTowardsAngle(bearYaw, targetYaw, 140f * dt);
            }
            Vector3 fwd = Quaternion.Euler(0f, bearYaw, 0f) * Vector3.forward;
            Vector3 np = bp + fwd * bearSpeed * dt;
            np.y = Slope.DrawnHeight(np.x, np.z);
            bearRb.MovePosition(np);
            bearRb.MoveRotation(Quaternion.Euler(0f, bearYaw + bearFwdYaw, 0f));
            if (bearAnim != null) foreach (AnimationState s in bearAnim) s.speed = Mathf.Clamp(bearSpeed / 12f, 0f, 1.3f);
            // gone once it has given up and is well behind
            if (!chasing && (bearSpeed < 0.1f && rp.z - bp.z > 60f || bearTimer > 30f)) { bear.SetActive(false); bearActive = false; }
        }
    }
}
