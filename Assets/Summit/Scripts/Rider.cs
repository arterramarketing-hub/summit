using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The rider's physics. A Unity Rigidbody carries the motion (so it is interpolated
    /// smoothly between physics steps and collides with trees and rocks), and this
    /// script supplies the forces of riding a board: gravity along the snow, air drag,
    /// snow friction, and an edge that grips hard so the board goes where it points.
    /// Heading 0 is straight down the fall line (+Z); positive turns right (+X).
    /// The rider stands on exactly the drawn terrain mesh, so it never sinks or floats.
    /// </summary>
    public class Rider : MonoBehaviour
    {
        // tuning (static so the test harness can sweep them)
        public static float G = 22f;                                 // gravity, a little stronger than real for punch
        public static float DragOpen = 0.0105f, DragTuck = 0.0068f; // air drag per (m/s)^2
        public static float Friction = 0.035f;                       // snow friction coefficient
        public static float Grip = 22f, GripTuck = 16f;              // 1/s: how fast sideways slip dies (higher = tighter)
        public static float TurnSlow = 2.9f, TurnFast = 1.7f;        // rad/s of board yaw at full stick, slow and fast
        public static float TurnBuild = 8f;                          // 1/s: how quickly a turn builds to the stick
        public static float TakeoffClearance = 0.22f;

        public const float R = 0.3f;              // the collision sphere around the feet
        const float MaxHeading = 1.45f;           // about 83 degrees off the fall line
        const float Tau = Mathf.PI * 2f;

        public RiderInput input;

        // state the rest of the game reads
        public float Heading, Speed, Steer, AirTime, Spin, StartZ;
        public bool Airborne, Crashed, Running;
        public float Tuck { get; private set; }
        public float Carve { get; private set; }
        public float AboveGround { get; private set; }
        public float Peak { get; private set; }
        public float SpinVisual { get; set; }
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        public float X => transform.position.x;
        public float Z => rb != null ? rb.position.z : transform.position.z;
        public float Vz => rb != null ? rb.linearVelocity.z : 0f;
        public float Distance => Mathf.Max(0f, Z - StartZ);
        public float TravelHeading
        {
            get { var v = Velocity; return v.x * v.x + v.z * v.z > 0.25f ? Mathf.Atan2(v.x, v.z) : Heading; }
        }

        public System.Action<string, int> Trick;     // name, points
        public System.Action<string> Crash;          // reason
        public System.Action<float> Landed;          // impact speed into the snow (m/s)
        public System.Action<string> RoughLanding;
        public System.Action Jumped;
        public System.Action<float> Scraped;         // glanced off something: how hard
        public System.Action ShieldHit;              // a shield took a hit that would have been a crash

        // power-ups
        public const int MaxShields = 3;
        public int Shields { get; private set; }
        public float BoostTime { get; private set; }
        public const float BoostAccel = 10f;          // extra push along the board while boosting (m/s^2)
        float invulnerable;
        public void AddShield() { Shields = Mathf.Min(MaxShields, Shields + 1); }
        public void AddBoost(float seconds)
        {
            BoostTime = Mathf.Max(BoostTime, 0f) + seconds;
            if (Running && !Crashed && rb != null) rb.linearVelocity += Velocity.normalized * 4f;   // an instant kick
        }

        Rigidbody rb;
        float groundVy, airLock, crashTimer, popHold, missClock, yawRate;
        bool popPending;
        int lastMiss;
        readonly Collider[] near = new Collider[16];

        public Rigidbody Body
        {
            get
            {
                if (rb == null) Setup();
                return rb;
            }
        }

        void Awake() { if (rb == null) Setup(); }

        void Setup()
        {
            rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 75f;
            rb.useGravity = false;               // gravity is applied along the snow below
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            var col = gameObject.GetComponent<SphereCollider>();
            if (col == null) col = gameObject.AddComponent<SphereCollider>();
            col.radius = R;
            col.sharedMaterial = new PhysicsMaterial("Board")
            {
                dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        // ---------------------------------------------------------------- lifecycle

        public void ResetAt(float x, float z)
        {
            var p = new Vector3(x, Slope.DrawnHeight(x, z) + R, z);
            Body.isKinematic = true;
            rb.position = p;
            transform.position = p;
            StartZ = z;
            Heading = 0f; Speed = 0f; Steer = 0f; groundVy = 0f;
            Airborne = false; Crashed = false; Running = false;
            AirTime = 0f; Spin = 0f; SpinVisual = 0f; Tuck = 0f; Carve = 0f; Peak = 0f; AboveGround = 0f;
            popPending = false; airLock = 0f; yawRate = 0f;
            Shields = 0; BoostTime = 0f; invulnerable = 0f;
        }

        public void Begin()
        {
            Running = true;
            Body.isKinematic = false;
            rb.linearVelocity = new Vector3(Mathf.Sin(Heading), 0f, Mathf.Cos(Heading)) * 5f;
        }

        public void Stop()
        {
            Running = false;
            if (rb != null && !rb.isKinematic) rb.linearVelocity = Vector3.zero;
        }

        void Update()
        {
            // a jump is a released tuck; input is read per frame, physics runs on its own clock
            if (Running && input != null && input.Released && input.LastHold > 0.08f)
            {
                popPending = true;
                popHold = input.LastHold;
            }
        }

        void FixedUpdate()
        {
            if (Running) Tick(Time.fixedDeltaTime);
        }

        /// <summary>One physics step of riding (public so the test harness can drive it).</summary>
        public void Tick(float dt)
        {
            if (input != null)
            {
                Steer = input.Steer;
                Tuck = Mathf.MoveTowards(Tuck, input.Hold && !Airborne ? 1f : 0f, dt * 5f);
            }
            BoostTime = Mathf.Max(0f, BoostTime - dt);
            invulnerable -= dt;
            Vector3 p = rb.position, v = rb.linearVelocity;
            if (Crashed) v = CrashStep(dt, p, v);
            else if (Airborne) v = AirStep(dt, p, v);
            else v = GroundStep(dt, p, v);
            rb.linearVelocity = v;
            if (!Crashed) CloseCalls(dt, p);
        }

        // ---------------------------------------------------------------- on the snow

        Vector3 GroundStep(float dt, Vector3 p, Vector3 v)
        {
            Vector3 n = Slope.Normal(p.x, p.z);
            var dir = new Vector3(Mathf.Sin(Heading), 0f, Mathf.Cos(Heading));
            Vector3 f = Vector3.ProjectOnPlane(dir, n).normalized;
            Vector3 r = Vector3.Cross(n, f);

            // work in the plane of the snow
            Vector3 vt = Vector3.ProjectOnPlane(v, n);
            // 1. gravity along the slope
            vt += Vector3.ProjectOnPlane(Vector3.down * G, n) * dt;
            // 2. air drag (less when tucked) and snow friction
            float sp = vt.magnitude;
            if (sp > 1e-3f)
            {
                float drag = Mathf.Lerp(DragOpen, DragTuck, Tuck) * (BoostTime > 0f ? 0.6f : 1f) * sp * sp + Friction * G * n.y;
                vt -= vt / sp * Mathf.Min(sp, drag * dt);
            }
            // 3. the edge: the velocity swings round to where the board points. The slip angle
            //    dies fast and the speed is kept (a clean carve), less a little scrubbed off;
            //    it can never gain speed, however you wiggle
            float lost = 0f;
            sp = vt.magnitude;
            if (sp > 0.05f)
            {
                float beta = Mathf.Atan2(Vector3.Dot(vt, r), Vector3.Dot(vt, f));   // slip angle
                beta = Mathf.Clamp(beta, -1.5f, 1.5f);
                float beta2 = beta * Mathf.Exp(-Mathf.Lerp(Grip, GripTuck, Tuck) * dt);
                float scrub = Mathf.Abs(beta - beta2);
                lost = scrub * sp;
                sp *= 1f - 0.08f * scrub;
                vt = (f * Mathf.Cos(beta2) + r * Mathf.Sin(beta2)) * sp;
            }
            if (BoostTime > 0f) { vt += f * (BoostAccel * dt); sp = vt.magnitude; }
            if (sp < 3f) { vt += f * (6f * dt); sp = vt.magnitude; }   // never stuck: skate along the board when slow
            Carve = Mathf.Lerp(Carve, Mathf.Clamp01(lost / dt / 16f + Mathf.Abs(Steer) * 0.25f) * Mathf.Clamp01(sp / 8f),
                               1f - Mathf.Exp(-dt * 12f));

            // 4. steer: the board turns, the velocity follows through the grip next step
            // the turn builds up over a tenth of a second or so instead of snapping: gradual to the eye,
            // but it starts at once, so small corrections still land precisely
            float turn = Mathf.Lerp(TurnSlow, TurnFast, Mathf.Clamp01(sp / 40f)) * Mathf.Lerp(1f, 0.75f, Tuck);
            yawRate = Mathf.Lerp(yawRate, Steer * turn, 1f - Mathf.Exp(-dt * TurnBuild));
            Heading += yawRate * dt;
            if (sp < 3f) Heading = Mathf.MoveTowards(Heading, 0f, dt * 1.4f * (1f - sp / 3f));   // stalled: swing back downhill
            Heading = Mathf.Clamp(Heading, -MaxHeading, MaxHeading);
            Speed = sp;

            // follow the snow: choose the vertical speed that puts the board on the mesh next step
            float nx = p.x + vt.x * dt, nz = p.z + vt.z * dt;
            float wantY = Slope.DrawnHeight(nx, nz) + R;
            float vy = (wantY - p.y) / dt;
            AboveGround = 0f;

            // jump: a released tuck pops you off the snow
            if (popPending)
            {
                popPending = false;
                Jumped?.Invoke();
                return TakeOff(vt + n * Mathf.Lerp(5.5f, 8f, Mathf.Clamp01(popHold / 0.8f)), 0.15f);
            }
            // a face too steep to ride (the drop below the start, a cliff): you are falling, not riding
            if (n.y < 0.62f) return TakeOff(new Vector3(vt.x, Mathf.Min(groundVy, 0f), vt.z), 0.05f);
            // the snow falls away faster than gravity can follow (a kicker's lip, a crest): fly
            if (sp > 7f && vy < groundVy - G * dt * 1.2f && FallsAway(p, new Vector3(vt.x, groundVy, vt.z)))
                return TakeOff(new Vector3(vt.x, groundVy, vt.z), 0.08f);

            groundVy = vy;
            return new Vector3(vt.x, vy, vt.z);
        }

        bool FallsAway(Vector3 p, Vector3 v)
        {
            const float T = 0.12f;
            float by = p.y + v.y * T - 0.5f * G * T * T;
            float gy = Slope.DrawnHeight(p.x + v.x * T, p.z + v.z * T) + R;
            return by - gy > TakeoffClearance;
        }

        Vector3 TakeOff(Vector3 v, float lockTime)
        {
            Airborne = true;
            airLock = lockTime;
            AirTime = 0f; Spin = 0f; Peak = 0f;
            Carve = 0f;
            return v;
        }

        // ---------------------------------------------------------------- in the air

        Vector3 AirStep(float dt, Vector3 p, Vector3 v)
        {
            AirTime += dt;
            airLock -= dt;
            v.y -= G * dt;
            float sp = v.magnitude;
            v -= v * Mathf.Min(1f, 0.0025f * sp * dt);
            // a gentle steer lines the board up for the landing; holding hard over spins
            if (AirTime > 0.18f && Mathf.Abs(Steer) > 0.6f) Spin += Steer * 9f * dt;
            else Heading = Mathf.Clamp(Heading + Steer * 1.5f * dt, -MaxHeading, MaxHeading);
            Speed = new Vector2(v.x, v.z).magnitude;

            float nx = p.x + v.x * dt, nz = p.z + v.z * dt;
            float gy = Slope.DrawnHeight(nx, nz) + R;
            AboveGround = Mathf.Max(0f, p.y - (Slope.DrawnHeight(p.x, p.z) + R));
            Peak = Mathf.Max(Peak, AboveGround);
            if (airLock <= 0f && p.y + v.y * dt <= gy) return Land(dt, p, v, gy);
            return v;
        }

        Vector3 Land(float dt, Vector3 p, Vector3 v, float gyNext)
        {
            Vector3 n = Slope.Normal(p.x, p.z);
            float impact = Mathf.Max(0f, -Vector3.Dot(v, n));   // speed into the snow
            Airborne = false;
            AboveGround = 0f;
            Vector3 vt = Vector3.ProjectOnPlane(v, n);

            float err = Spin - Tau * Mathf.Round(Spin / Tau);
            int turns = Mathf.RoundToInt(Mathf.Abs(Spin) / Tau);
            bool real = Peak > 0.8f;
            bool sloppy = real && Mathf.Abs(err) > 0.75f;
            bool hard = impact > 17f;
            SpinVisual = sloppy ? 0f : (real ? err : Spin);   // fold the leftover into the picture
            Spin = 0f;
            // a landing absorbs some speed: more if it is hard or crooked
            float keep = 1f - Mathf.Clamp01((impact - 8f) * 0.012f);
            if (sloppy) keep *= 0.85f;
            if (hard) keep *= 0.9f;
            vt *= keep;
            Landed?.Invoke(impact);
            float vy = (gyNext - p.y) / dt;
            groundVy = vy;
            if (sloppy || hard) RoughLanding?.Invoke(sloppy ? "Sloppy landing" : "Hard landing");
            else if (real)
            {
                if (turns > 0) Trick?.Invoke((turns * 360) + (turns > 2 ? "!" : ""), 400 * turns * turns);
                if (Peak > 5f) Trick?.Invoke("Big air", 300);
                else if (AirTime > 0.5f) Trick?.Invoke("Air " + AirTime.ToString("0.0") + "s", Mathf.RoundToInt(AirTime * 120f));
            }
            return new Vector3(vt.x, vy, vt.z);
        }

        // ---------------------------------------------------------------- trees and rocks

        void OnCollisionEnter(Collision c)
        {
            if (!Running || Crashed || c.collider.GetComponentInParent<Obstacle>() == null) return;
            Vector3 normal = c.contactCount > 0 ? c.GetContact(0).normal : -rb.linearVelocity.normalized;
            float into = Mathf.Abs(Vector3.Dot(c.relativeVelocity, normal));
            if (invulnerable > 0f) return;
            if (into > 6f && Shields > 0)
            {
                // the shield takes it: bounce off and keep riding
                Shields--;
                invulnerable = 0.7f;
                Vector3 v = rb.linearVelocity;
                Vector3 nh = new Vector3(normal.x, 0f, normal.z).normalized;
                if (Vector3.Dot(v, nh) < 0f) v -= nh * Vector3.Dot(v, nh) * 1.6f;
                rb.linearVelocity = v * 0.8f;
                ShieldHit?.Invoke();
            }
            else if (into > 6f) DoCrash(c.collider.GetComponent<SphereCollider>() != null ? "Rock" : "Tree");
            else
            {
                // a glancing blow: bounce off and lose a little speed, but keep riding
                Scraped?.Invoke(into);
                rb.linearVelocity *= 0.85f;
            }
        }

        void CloseCalls(float dt, Vector3 p)
        {
            missClock -= dt;
            if (missClock > 0f || Airborne || Speed < 14f) return;
            missClock = 0.1f;
            int k = Physics.OverlapSphereNonAlloc(p, 1.9f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++)
            {
                if (near[i].attachedRigidbody == rb) continue;
                var o = near[i].GetComponentInParent<Obstacle>();
                if (o == null) continue;
                int id = o.GetInstanceID();
                if (id == lastMiss) continue;
                lastMiss = id;
                Trick?.Invoke("Close call", 50);
                return;
            }
        }

        void DoCrash(string why)
        {
            Crashed = true;
            Airborne = false;
            crashTimer = 1.4f;
            Carve = 0f;
            Crash?.Invoke(why);
        }

        Vector3 CrashStep(float dt, Vector3 p, Vector3 v)
        {
            crashTimer -= dt;
            var h = new Vector3(v.x, 0f, v.z) * Mathf.Exp(-dt * 3f);
            float wantY = Slope.DrawnHeight(p.x + h.x * dt, p.z + h.z * dt) + R;
            Speed = h.magnitude;
            if (crashTimer <= 0f) return Recover(p);
            return new Vector3(h.x, (wantY - p.y) / dt, h.z);
        }

        Vector3 Recover(Vector3 p)
        {
            Crashed = false;
            Heading = 0f;
            Spin = 0f; SpinVisual = 0f;
            // step clear of whatever stopped you, toward the piste
            float toward = Mathf.Sign(Slope.Centre(p.z) - p.x + 0.01f);
            for (int i = 0; i < 6 && Physics.CheckSphere(p, 1.4f, ~0, QueryTriggerInteraction.Ignore) && Blocked(p); i++)
                p.x += toward * 1.5f;
            p.y = Slope.DrawnHeight(p.x, p.z) + R;
            rb.position = p;
            groundVy = 0f;
            return new Vector3(0f, 0f, 3f);
        }

        bool Blocked(Vector3 p)
        {
            int k = Physics.OverlapSphereNonAlloc(p, 1.4f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++)
                if (near[i].attachedRigidbody != rb && near[i].GetComponentInParent<Obstacle>() != null) return true;
            return false;
        }
    }
}
