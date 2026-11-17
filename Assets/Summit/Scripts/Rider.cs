using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The rider's physics. A Unity Rigidbody carries the motion (interpolated between physics
    /// steps, colliding with trees, rocks and animals); this script supplies riding a board.
    ///
    /// Steering is built like real carving. Your thumb sets an edge (lean) angle; the body
    /// moves to it on a damped spring, so turns start at once but build smoothly. A carve's
    /// turn rate comes from that lean and your speed (a leaned board at speed draws a wide
    /// arc, at low speed a tight one). If you ask for more than the edge can hold, the board
    /// skids and scrubs speed. Every turn costs some speed; running straight builds it.
    ///
    /// Heading 0 is straight down the fall line (+Z); positive turns right (+X).
    /// </summary>
    public class Rider : MonoBehaviour
    {
        // ---- tuning (static so the test harness can sweep them)
        public static float G = 22f;                                 // gravity, stronger than real for punch
        public static float DragOpen = 0.0105f, DragTuck = 0.0068f; // air drag per (m/s)^2
        public static float Friction = 0.035f;                       // snow friction coefficient
        public static float MaxLean = 0.72f;                         // full-thumb edge angle (rad, ~41 degrees)
        public static float LeanSpring = 10f;                        // how fast the body gets onto its edge (rad/s)
        public static float CarveK = 22f;                            // turn rate = CarveK * tan(lean) / speed
        public static float MinCarveSpeed = 7f, MaxYawRate = 1.9f;
        public static float GripLimit = 34f;                         // sideways acceleration the edge can hold (m/s^2)
        public static float Grip = 22f;                              // 1/s: how fast slip dies when the edge holds
        public static float TurnDrag = 0.12f;                        // speed lost per radian of turning
        public static float TakeoffClearance = 0.22f;

        public const float R = 0.3f;              // the collision sphere around the feet
        const float MaxHeading = 1.45f;           // about 83 degrees off the fall line

        public RiderInput input;

        // ---- state the rest of the game reads
        public float Heading, Speed, Steer, AirTime, StartZ;
        public bool Airborne, Crashed, Running;
        public float Lean { get; private set; }          // edge angle, rad: + = leaning right
        public float YawRate { get; private set; }
        public float Tuck { get; private set; }
        public float Carve { get; private set; }         // 0..1 how hard the edge bites
        public float Skid { get; private set; }          // 0..1 how much the board is sliding sideways
        public float AboveGround { get; private set; }
        public float Peak { get; private set; }
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        public float X => transform.position.x;
        public float Z => rb != null ? rb.position.z : transform.position.z;
        public float Vz => rb != null ? rb.linearVelocity.z : 0f;
        public float Distance => Mathf.Max(0f, Z - StartZ);
        public float TravelHeading
        {
            get { var v = Velocity; return v.x * v.x + v.z * v.z > 0.25f ? Mathf.Atan2(v.x, v.z) : Heading; }
        }

        // ---- tricks (chosen automatically when there is enough air)
        public string TrickName { get; private set; }
        public float TrickSpin { get; private set; }     // total yaw to turn (rad)
        public float TrickFlip { get; private set; }     // total flip (rad, about the board's long axis)
        public int TrickGrab { get; private set; }       // 0 none, 1.. which grab
        public float TrickTime { get; private set; }     // predicted airtime
        /// <summary>0..1 through the trick's rotation, eased so it finishes before the landing.</summary>
        public float TrickProgress
        {
            get
            {
                if (TrickTime <= 0f) return 0f;
                float t = Mathf.Clamp01((AirTime - 0.08f * TrickTime) / (0.72f * TrickTime));
                return t * t * (3f - 2f * t);
            }
        }

        // ---- grinding
        public bool Grinding { get; private set; }
        public Course.Feature GrindFeature { get; private set; }
        float grindS, grindSpeed;

        public System.Action<string, int> Trick;     // name, points
        public System.Action<string> Crash;          // reason
        public System.Action<float> Landed;          // impact speed into the snow (m/s)
        public System.Action<string> RoughLanding;
        public System.Action Jumped;
        public System.Action<float> Scraped;
        public System.Action ShieldHit;
        public System.Action<Course.Feature> GrindStart;

        // ---- power-ups
        public const int MaxShields = 3;
        public int Shields { get; private set; }
        public float BoostTime { get; private set; }
        public const float BoostAccel = 10f;
        float invulnerable;
        public void AddShield() { Shields = Mathf.Min(MaxShields, Shields + 1); }
        public void AddBoost(float seconds)
        {
            BoostTime = Mathf.Max(BoostTime, 0f) + seconds;
            if (Running && !Crashed && rb != null) rb.linearVelocity += Velocity.normalized * 4f;
        }

        Rigidbody rb;
        float groundVy, airLock, crashTimer, popHold, missClock, leanVel;
        bool popPending;
        int lastMiss, lastGrind = -1;
        string crashCause = "";
        readonly Collider[] near = new Collider[16];

        public Rigidbody Body { get { if (rb == null) Setup(); return rb; } }

        void Awake() { if (rb == null) Setup(); }

        void Setup()
        {
            rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 75f;
            rb.useGravity = false;
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
            Airborne = false; Crashed = false; Running = false; Grinding = false;
            HasCrashFollow = false; rb.detectCollisions = true;
            AirTime = 0f; Tuck = 0f; Carve = 0f; Skid = 0f; Peak = 0f; AboveGround = 0f;
            Lean = 0f; leanVel = 0f; YawRate = 0f;
            ClearTrick();
            popPending = false; airLock = 0f; lastGrind = -1;
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

        void FixedUpdate() { if (Running) Tick(Time.fixedDeltaTime); }

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
            else if (Grinding) v = GrindStep(dt, p, v);
            else
            {
                if (TryGrind(p, v)) v = GrindStep(dt, p, v);
                else if (Airborne) v = AirStep(dt, p, v);
                else v = GroundStep(dt, p, v);
            }
            rb.linearVelocity = v;
            if (!Crashed) CloseCalls(dt, p);
        }

        // ---------------------------------------------------------------- the edge

        /// <summary>Moves the body onto the edge the thumb asks for, and returns the carve's turn rate.</summary>
        float CarveTurn(float dt, float sp)
        {
            float target = Steer * MaxLean * Mathf.Lerp(1f, 0.65f, Tuck);
            // critically damped spring: starts at once, settles in about 0.4 s, never overshoots
            float acc = LeanSpring * LeanSpring * (target - Lean) - 2f * LeanSpring * leanVel;
            leanVel += acc * dt;
            Lean += leanVel * dt;
            float yaw = Mathf.Sign(Lean) * Mathf.Min(MaxYawRate, CarveK * Mathf.Tan(Mathf.Abs(Lean)) / Mathf.Max(sp, MinCarveSpeed));
            YawRate = yaw;
            return yaw;
        }

        Vector3 GroundStep(float dt, Vector3 p, Vector3 v)
        {
            Vector3 n = Slope.Normal(p.x, p.z);
            Vector3 vt = Vector3.ProjectOnPlane(v, n);
            float sp = vt.magnitude;

            // riding backwards (coming back down a pipe wall, or after a crash): switch to face the way you go
            var dir0 = new Vector3(Mathf.Sin(Heading), 0f, Mathf.Cos(Heading));
            if (sp > 2f && Vector3.Dot(Vector3.ProjectOnPlane(dir0, n), vt) < -0.2f * sp)
                Heading = Mathf.Clamp(Mathf.Atan2(vt.x, vt.z), -MaxHeading, MaxHeading);

            // 1. steer: lean onto the edge; the board yaws at the carve's rate
            float yaw = CarveTurn(dt, sp);
            Heading += yaw * dt;
            if (sp < 3f) Heading = Mathf.MoveTowards(Heading, 0f, dt * 1.4f * (1f - sp / 3f));   // stalled: swing back downhill
            Heading = Mathf.Clamp(Heading, -MaxHeading, MaxHeading);

            var dir = new Vector3(Mathf.Sin(Heading), 0f, Mathf.Cos(Heading));
            Vector3 f = Vector3.ProjectOnPlane(dir, n).normalized;
            Vector3 r = Vector3.Cross(n, f);

            // 2. gravity along the slope
            vt += Vector3.ProjectOnPlane(Vector3.down * G, n) * dt;
            // 3. air drag (less when tucked or boosting) and snow friction
            sp = vt.magnitude;
            if (sp > 1e-3f)
            {
                float drag = Mathf.Lerp(DragOpen, DragTuck, Tuck) * (BoostTime > 0f ? 0.6f : 1f) * sp * sp + Friction * G * n.y;
                vt -= vt / sp * Mathf.Min(sp, drag * dt);
            }
            // 4. the edge swings the velocity round to where the board points. It holds as long as the
            //    sideways pull is within its grip; beyond that the board skids
            float lost = 0f, demand = sp * Mathf.Abs(yaw);
            float hold = Mathf.Min(1f, GripLimit / Mathf.Max(demand, 1e-3f));
            sp = vt.magnitude;
            if (sp > 0.05f)
            {
                float beta = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(vt, r), Vector3.Dot(vt, f)), -1.5f, 1.5f);
                float beta2 = beta * Mathf.Exp(-Grip * hold * dt);
                float scrub = Mathf.Abs(beta - beta2);
                lost = scrub * sp;
                sp *= 1f - 0.18f * scrub;
                vt = (f * Mathf.Cos(beta2) + r * Mathf.Sin(beta2)) * sp;
                Skid = Mathf.Lerp(Skid, Mathf.Clamp01(Mathf.Abs(beta2) * 4f), 1f - Mathf.Exp(-dt * 10f));
            }
            // 5. carving costs speed; running straight builds it
            float brake = TurnDrag * Mathf.Abs(yaw) * sp * dt;
            if (brake > 0f && sp > 3f) { float k = Mathf.Max(0f, sp - brake) / sp; vt *= k; sp *= k; }
            if (BoostTime > 0f) { vt += f * (BoostAccel * dt); sp = vt.magnitude; }
            if (sp < 3f) { vt += f * (6f * dt); sp = vt.magnitude; }
            Carve = Mathf.Lerp(Carve, Mathf.Clamp01(Mathf.Abs(Lean) / MaxLean * 0.8f + lost / dt / 20f) * Mathf.Clamp01(sp / 8f),
                               1f - Mathf.Exp(-dt * 12f));
            Speed = sp;

            // follow the snow: the vertical speed that puts the board on the mesh next step
            float nx = p.x + vt.x * dt, nz = p.z + vt.z * dt;
            float wantY = Slope.DrawnHeight(nx, nz) + R;
            float vy = (wantY - p.y) / dt;
            AboveGround = 0f;

            // fell into a crevasse
            if (Course.CrevasseCut(p.x, p.z) > 1.2f) { DoCrash("Crevasse!"); return new Vector3(vt.x * 0.2f, vy, vt.z * 0.2f); }

            if (popPending)
            {
                popPending = false;
                Jumped?.Invoke();
                return TakeOff(p, vt + n * Mathf.Lerp(5.5f, 8f, Mathf.Clamp01(popHold / 0.8f)), 0.15f);
            }
            // a face too steep to ride: you are falling, not riding (pipe walls are ridden almost to vertical)
            bool inPipe = Course.InPipe(p.x, p.z);
            float steep = inPipe ? 0.22f : 0.62f;
            if (n.y < steep)
            {
                // off the edge of a drop. Running into a wall that rises in front must not turn the
                // speed into a launch straight up, so the climb is capped at the ground's own rate
                if (!inPipe) vt.y = Mathf.Min(vt.y, Mathf.Max(groundVy, 0f) + 1.5f);
                return TakeOff(p, inPipe ? PipeAir(p, vt) : vt, 0.05f);
            }
            // the snow falls away faster than gravity can follow (a kicker's lip, a crest, a pipe's coping): fly
            // (leave with the slope's own vertical speed, never the mesh-following one, which can spike on coarse facets)
            float vUp = Mathf.Clamp(groundVy, vt.y - 2f, vt.y + 2f);
            if (sp > 7f && vy < groundVy - G * dt * 1.2f && FallsAway(p, new Vector3(vt.x, vUp, vt.z)))
                return TakeOff(p, new Vector3(vt.x, vUp, vt.z), 0.08f);
            // riding up a pipe wall: leave it with the wall's own vertical speed
            if (inPipe && vt.y > 1f && vy < vt.y - G * dt * 2f)
                return TakeOff(p, PipeAir(p, vt), 0.08f);

            groundVy = vy;
            return new Vector3(vt.x, vy, vt.z);
        }

        /// <summary>
        /// Leaving a pipe wall: the air goes straight up off the coping and drops back in, never out over
        /// the deck, and tops out around 5 m however hard you hit the wall.
        /// </summary>
        static Vector3 PipeAir(Vector3 p, Vector3 v)
        {
            float side = Mathf.Sign(p.x - Slope.Centre(p.z));
            float vMax = Mathf.Sqrt(2f * G * 5f);
            v.y = Mathf.Min(v.y, vMax);
            v.x = -side * 1.2f;   // a little drift back toward the middle
            return v;
        }

        bool FallsAway(Vector3 p, Vector3 v)
        {
            const float T = 0.12f;
            float by = p.y + v.y * T - 0.5f * G * T * T;
            float gy = Slope.DrawnHeight(p.x + v.x * T, p.z + v.z * T) + R;
            return by - gy > TakeoffClearance;
        }

        Vector3 TakeOff(Vector3 p, Vector3 v, float lockTime)
        {
            Airborne = true;
            airLock = lockTime;
            AirTime = 0f; Peak = 0f;
            Carve = 0f; Skid = 0f;
            PlanTrick(p, v);
            return v;
        }

        // ---------------------------------------------------------------- tricks

        static readonly string[] grabs = { "Indy", "Melon", "Mute", "Method", "Stalefish", "Tail Grab" };

        void ClearTrick() { TrickName = null; TrickSpin = 0f; TrickFlip = 0f; TrickGrab = 0; TrickTime = 0f; }

        /// <summary>Predict the airtime, and pick a trick that can be finished before the landing.</summary>
        void PlanTrick(Vector3 p, Vector3 v)
        {
            ClearTrick();
            float T = 0f; const float h = 1f / 30f;
            Vector3 q = p, u = v;
            for (int i = 0; i < 150; i++)
            {
                u.y -= G * h; q += u * h; T += h;
                if (i > 2 && q.y <= Slope.DrawnHeight(q.x, q.z) + R) break;
            }
            if (T < 0.6f) return;
            TrickTime = T;
            int pick = (int)(Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 7.13f + p.z)) * 1000f);
            string grab = grabs[pick % grabs.Length];
            int pts;
            bool pipe = Course.InPipe(p.x, p.z);
            if (T < 0.85f) { TrickGrab = 1 + pick % grabs.Length; TrickName = grab; pts = 150; }
            else if (T < 1.15f) { TrickSpin = Mathf.PI * (pipe ? 1f : 2f); TrickGrab = 1 + pick % grabs.Length; TrickName = (pipe ? "180 " : "360 ") + grab; pts = pipe ? 300 : 450; }
            else if (T < 1.5f)
            {
                if (pick % 2 == 0) { TrickSpin = Mathf.PI * 3f; TrickGrab = 1 + pick % grabs.Length; TrickName = "540 " + grab; pts = 700; }
                else { TrickFlip = -Mathf.PI * 2f; TrickName = "Backflip"; pts = 800; }
            }
            else
            {
                if (pick % 3 == 0) { TrickSpin = Mathf.PI * 2f; TrickFlip = -Mathf.PI * 2f; TrickGrab = 1 + pick % grabs.Length; TrickName = "Cork 720 " + grab; pts = 1400; }
                else if (pick % 3 == 1) { TrickSpin = Mathf.PI * 4f; TrickGrab = 1 + pick % grabs.Length; TrickName = "720 " + grab; pts = 1000; }
                else { TrickFlip = Mathf.PI * 2f; TrickGrab = 1 + pick % grabs.Length; TrickName = "Frontflip " + grab; pts = 900; }
            }
            if (Random.value < 0.5f) TrickSpin = -TrickSpin;
            trickPoints = pts;
        }
        int trickPoints;

        // ---------------------------------------------------------------- in the air

        Vector3 AirStep(float dt, Vector3 p, Vector3 v)
        {
            AirTime += dt;
            airLock -= dt;
            v.y -= G * dt;
            float sp = v.magnitude;
            v -= v * Mathf.Min(1f, 0.0025f * sp * dt);
            // relax the edge; a little steering lines the board up for the landing
            Steer *= 1f;
            CarveTurn(dt, 30f);
            Heading = Mathf.Clamp(Heading + Steer * 1.2f * dt, -MaxHeading, MaxHeading);
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
            float impact = Mathf.Max(0f, -Vector3.Dot(v, n));
            Airborne = false;
            AboveGround = 0f;
            Vector3 vt = Vector3.ProjectOnPlane(v, n);
            // land facing where you are going: a trick always comes round clean
            if (vt.sqrMagnitude > 1f) Heading = Mathf.Clamp(Mathf.Atan2(vt.x, vt.z), -MaxHeading, MaxHeading);
            Lean = 0f; leanVel = 0f;

            bool hard = impact > 18f;
            float keep = 1f - Mathf.Clamp01((impact - 8f) * 0.012f);
            if (hard) keep *= 0.9f;
            vt *= keep;
            Landed?.Invoke(impact);
            float vy = (gyNext - p.y) / dt;
            groundVy = vy;
            if (TrickName != null)
            {
                if (AirTime >= 0.8f * TrickTime) Trick?.Invoke(TrickName, trickPoints);
                else RoughLanding?.Invoke("Bailed the " + TrickName);
            }
            else if (Peak > 0.8f && AirTime > 0.45f) Trick?.Invoke("Air " + AirTime.ToString("0.0") + "s", Mathf.RoundToInt(AirTime * 120f));
            if (hard) RoughLanding?.Invoke("Hard landing");
            ClearTrick();
            return new Vector3(vt.x, vy, vt.z);
        }

        // ---------------------------------------------------------------- boxes and rails

        bool TryGrind(Vector3 p, Vector3 v)
        {
            if (Course.At(p.z) != Course.Section.Park && Course.At(p.z + 10f) != Course.Section.Park) return false;
            foreach (var feat in Course.FeaturesNear(p.z))
            {
                if (feat.id == lastGrind) continue;
                Vector3 d = feat.b - feat.a;
                float len = d.magnitude;
                Vector3 dir = d / len;
                float s = Vector3.Dot(p - feat.a, dir);
                if (s < -1.5f || s > len * 0.8f) continue;
                Vector3 onLine = feat.a + dir * Mathf.Clamp(s, 0f, len);
                float lateral = new Vector2(p.x - onLine.x, p.z - onLine.z).magnitude;
                if (lateral > feat.halfWidth + 0.65f) continue;
                float bottom = p.y - R;
                if (bottom < onLine.y - feat.height - 0.3f || bottom > onLine.y + 1.0f) continue;
                float along = Vector3.Dot(v, dir);
                if (along < 4f) continue;
                // on it: ride-on features are boarded automatically
                Grinding = true;
                GrindFeature = feat;
                lastGrind = feat.id;
                grindS = Mathf.Max(0f, s);
                grindSpeed = along;
                Airborne = false;
                ClearTrick();
                Lean = 0f; leanVel = 0f;
                GrindStart?.Invoke(feat);
                return true;
            }
            return false;
        }

        Vector3 GrindStep(float dt, Vector3 p, Vector3 v)
        {
            var feat = GrindFeature;
            Vector3 d = feat.b - feat.a;
            float len = d.magnitude;
            Vector3 dir = d / len;
            grindSpeed += (G * -dir.y - 0.06f * G) * dt;
            grindSpeed = Mathf.Max(grindSpeed, 3f);
            grindS += grindSpeed * dt;
            Speed = grindSpeed;
            Heading = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z), -MaxHeading, MaxHeading);
            Carve = 0f; Skid = 0f; AboveGround = feat.height;
            if (grindS >= len)
            {
                // off the end with a little pop
                Grinding = false;
                string name = feat.kind == Course.Kind.Box ? "50-50 Box" : "Boardslide";
                Trick?.Invoke(name, 150 + Mathf.RoundToInt(len * 25f));
                return TakeOff(p, dir * grindSpeed + Vector3.up * 3.5f, 0.1f);
            }
            Vector3 target = feat.a + dir * grindS + Vector3.up * R;
            return (target - p) / dt;
        }

        // ---------------------------------------------------------------- trees, rocks, animals

        void OnCollisionEnter(Collision c)
        {
            if (!Running || Crashed) return;
            var ob = c.collider.GetComponentInParent<Obstacle>();
            if (ob == null) return;
            Vector3 normal = c.contactCount > 0 ? c.GetContact(0).normal : -rb.linearVelocity.normalized;
            float into = Mathf.Abs(Vector3.Dot(c.relativeVelocity, normal));
            if (ob.always) into = Mathf.Max(into, 10f);   // animals always knock you down
            if (invulnerable > 0f) return;
            if (into > 6f && Shields > 0)
            {
                Shields--;
                invulnerable = 0.9f;
                Vector3 v = rb.linearVelocity;
                Vector3 nh = new Vector3(normal.x, 0f, normal.z).normalized;
                if (Vector3.Dot(v, nh) < 0f) v -= nh * Vector3.Dot(v, nh) * 1.6f;
                rb.linearVelocity = v * 0.8f;
                ShieldHit?.Invoke();
            }
            else if (into > 6f) DoCrash(ob.label);
            else
            {
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

        public void DoCrash(string why)
        {
            if (Crashed) return;
            Crashed = true;
            Airborne = false; Grinding = false;
            crashTimer = 2.6f;   // long enough to tumble and come to rest
            rb.detectCollisions = false;
            Carve = 0f; Skid = 0f; Lean = 0f; leanVel = 0f;
            crashCause = why;
            ClearTrick();
            Crash?.Invoke(why);
        }

        Vector3 CrashStep(float dt, Vector3 p, Vector3 v)
        {
            crashTimer -= dt;
            var h = new Vector3(v.x, 0f, v.z) * Mathf.Exp(-dt * 3f);
            if (HasCrashFollow)
            {
                // stay with the thrown body, so the camera watches it and you get up where it lands
                var d = CrashFollow - p; d.y = 0f;
                h = Vector3.ClampMagnitude(d * 8f, 40f);
            }
            float wantY = Slope.DrawnHeight(p.x + h.x * dt, p.z + h.z * dt) + R;
            Speed = h.magnitude;
            if (crashTimer <= 0f) return Recover(p);
            return new Vector3(h.x, (wantY - p.y) / dt, h.z);
        }

        /// <summary>Where the thrown rider is, set by the ragdoll while a crash plays out.</summary>
        public Vector3 CrashFollow;
        public bool HasCrashFollow;

        Vector3 Recover(Vector3 p)
        {
            Crashed = false;
            HasCrashFollow = false;
            rb.detectCollisions = true;
            Heading = 0f;
            // climb out of a crevasse onto its nearer rim
            if (Course.CrevasseNear(p.x, p.z, 0.5f, out float cx, out float half))
                p.x = cx + Mathf.Sign(p.x - cx + 0.01f) * (half + 1.6f);
            // step clear of whatever stopped you, toward the piste
            float toward = Mathf.Sign(Slope.Centre(p.z) - p.x + 0.01f);
            for (int i = 0; i < 6 && Blocked(p); i++) p.x += toward * 1.5f;
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
