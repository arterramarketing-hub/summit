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
        public static float LeanSpring = 13f;                        // how fast the body gets onto its edge (rad/s)
        public static float CarveK = 22f;                            // turn rate = CarveK * tan(lean) / speed
        public static float MinCarveSpeed = 7f, MaxYawRate = 1.9f;
        public static float GripLimit = 34f;                         // sideways acceleration the edge can hold (m/s^2)
        public static float Grip = 42f;                              // 1/s: how fast slip dies when the edge holds (high: the edge bites, no drifting)
        public static float TurnDrag = 0.12f;                        // speed lost per radian of turning
        public static float TakeoffClearance = 0.22f;
        public static float FallLinePull = 2.2f;                     // 1/s: how fast a flat board swings back down the hill

        /// <summary>
        /// Riding a snow tube instead of a board: no edges, so no carving. You lean to nudge it round, it slides
        /// wherever momentum takes it and always drifts back down the fall line; it bounces off things that would
        /// throw a snowboarder; no tricks, but airs still score.
        /// </summary>
        public bool Tube;

        public const float R = 0.3f;              // the collision sphere around the feet
        const float MaxHeading = 1.45f;           // about 83 degrees off the fall line

        public RiderInput input;

        // ---- state the rest of the game reads
        public float Heading, Speed, Steer, AirTime, StartZ;
        public bool Airborne, Crashed, Running;
        /// <summary>0..1, set by the avalanche while it flows under the board: the snow heaves, the edge cannot bite.</summary>
        public float Undercut;
        float churnT;
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
        public System.Action<Obstacle, Vector3, float> HitObstacle;
        public System.Action<float> Bounced;                          // the tube bounced off something
        public System.Action<Obstacle, Vector3, Vector3> Smashed;     // invincible: what you went through, where, your velocity
        Vector3 grindOff;
        bool pipeAir;   // flying off a halfpipe wall: drift back in over the pipe   // what, where (its middle), how hard

        // ---- power-ups
        public const int MaxShields = 3;
        public int Shields { get; private set; }
        public float BoostTime { get; private set; }
        public const float BoostAccel = 10f;
        float invulnerable;
        // Super Jump: a released tuck launches you this many times higher (the pop's speed scales by the root)
        public float SuperJumpTime { get; private set; }
        public float InvincibleTime { get; private set; }
        public const float SuperJumpLift = 4f;
        public const float SmashKeep = 0.93f;    // speed kept through each thing you smash
        bool superAir;                           // this flight came off a super jump
        Vector3 lastV;                           // velocity going into the last physics step, before any contact
        public void AddSuperJump(float seconds) { SuperJumpTime = Mathf.Max(SuperJumpTime, 0f) + seconds; }
        public void AddInvincible(float seconds) { InvincibleTime = Mathf.Max(InvincibleTime, 0f) + seconds; }
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
            Airborne = false; Crashed = false; Running = false; Grinding = false; pipeAir = false;
            HasCrashFollow = false; rb.detectCollisions = true; StayDown = false; HoldCrash = false;
            AirTime = 0f; Tuck = 0f; Carve = 0f; Skid = 0f; Peak = 0f; AboveGround = 0f;
            Lean = 0f; leanVel = 0f; YawRate = 0f;
            ClearTrick();
            popPending = false; airLock = 0f; lastGrind = -1;
            Shields = 0; BoostTime = 0f; invulnerable = 0f; SuperJumpTime = 0f; InvincibleTime = 0f; superAir = false;
            Undercut = 0f;
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

        /// <summary>A jump as if the thumb had just lifted after holding this long (for the test tools).</summary>
        public void QueuePop(float hold) { popPending = true; popHold = hold; }


        /// <summary>One physics step of riding (public so the test harness can drive it).</summary>
        public void Tick(float dt)
        {
            if (input != null)
            {
                Steer = input.Steer;
                Tuck = Mathf.MoveTowards(Tuck, input.Hold && !Airborne ? 1f : 0f, dt * 5f);
            }
            BoostTime = Mathf.Max(0f, BoostTime - dt);
            SuperJumpTime = Mathf.Max(0f, SuperJumpTime - dt);
            InvincibleTime = Mathf.Max(0f, InvincibleTime - dt);
            invulnerable -= dt;
            if (Undercut > 0f && !Crashed)
            {
                // the avalanche is flowing under the board: your steering barely registers and the heaving snow
                // swings the board about under you
                churnT += dt;
                Steer = Steer * (1f - 0.75f * Undercut) + Undercut * 0.9f * (Mathf.PerlinNoise(churnT * 2.3f, 3.7f) * 2f - 1f);
                Heading += Undercut * 1.4f * (Mathf.PerlinNoise(churnT * 1.7f, 9.1f) * 2f - 1f) * dt;
            }
            Vector3 p = rb.position, v = rb.linearVelocity;
            if (InvincibleTime > 0f && !Crashed) v = SmashAhead(dt, p, v);
            // over a crevasse with nothing under you (riding in, or coming down from the air): you fall in
            if (!Crashed && !Grinding && Course.CrevasseCut(p.x, p.z) > 1.2f && (!Airborne || p.y - Slope.DrawnHeight(p.x, p.z) < 0.8f))
            {
                Airborne = false;
                DoCrash("Crevasse!");
                v = new Vector3(v.x * 0.4f, Mathf.Min(v.y, -2f), v.z * 0.4f);
            }
            // gone over the edge of a ridge, onto rock too steep to ride: a tumble down the cliff, keeping all
            // your speed (a fall, not the end of the run)
            if (!Crashed && !Grinding && invulnerable <= 0f && Ridge.OverEdge(p, Airborne)) DoCrash("Off the ridge!");
            if (Crashed) v = CrashStep(dt, p, v);
            else if (Grinding) v = GrindStep(dt, p, v);
            else
            {
                if (TryGrind(p, v)) v = GrindStep(dt, p, v);
                else if (Airborne) v = AirStep(dt, p, v);
                else v = GroundStep(dt, p, v);
            }
            rb.linearVelocity = v;
            lastV = v;
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
            float yaw;
            if (Tube)
            {
                // a tube: leaning shifts your weight and nudges it round, slowly, and it always swings back downhill
                Lean = Mathf.Lerp(Lean, Steer * 0.35f, 1f - Mathf.Exp(-dt * 6f)); leanVel = 0f;
                yaw = Steer * 0.75f - Mathf.Sin(Heading) * 1.1f * Mathf.Clamp01(sp / 5f);
            }
            else
            {
                yaw = CarveTurn(dt, sp);
                // and with the board flat (no edge pressure) gravity swings its nose back down the fall line, the
                // way a real board drifts straight again the moment you stop holding a turn
                float flatness = 1f - Mathf.Clamp01(Mathf.Abs(Lean) / (0.35f * MaxLean));
                float fallBack = -Mathf.Sin(Heading) * FallLinePull * flatness * Mathf.Clamp01(sp / 6f) * Mathf.Clamp01((1f - n.y) * 4f);
                yaw += fallBack;
            }
            YawRate = yaw;
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
                float drag = (Tube ? DragOpen * 1.15f : Mathf.Lerp(DragOpen, DragTuck, Tuck)) * (BoostTime > 0f ? 0.6f : 1f) * sp * sp + (Tube ? Friction * 1.3f : Friction) * G * n.y;
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
                float beta2 = beta * Mathf.Exp(-(Tube ? 2.5f : Grip * hold * (1f - 0.85f * Undercut)) * dt);   // a tube has no edge: it slides
                float scrub = Mathf.Abs(beta - beta2);
                lost = scrub * sp;
                sp *= 1f - 0.18f * scrub;
                vt = (f * Mathf.Cos(beta2) + r * Mathf.Sin(beta2)) * sp;
                Skid = Mathf.Lerp(Skid, Mathf.Clamp01(Mathf.Abs(beta2) * 4f), 1f - Mathf.Exp(-dt * 10f));
            }
            // 5. carving costs speed; running straight builds it
            float brake = (Tube ? 0f : TurnDrag) * Mathf.Abs(yaw) * sp * dt;
            if (brake > 0f && sp > 3f) { float k = Mathf.Max(0f, sp - brake) / sp; vt *= k; sp *= k; }
            if (BoostTime > 0f) { vt += f * (BoostAccel * dt); sp = vt.magnitude; }
            if (sp < 3f) { vt += f * (6f * dt); sp = vt.magnitude; }
            Carve = Mathf.Lerp(Carve, Mathf.Clamp01(Mathf.Abs(Lean) / MaxLean * 0.8f + lost / dt / 20f) * Mathf.Clamp01(sp / 8f),
                               1f - Mathf.Exp(-dt * 12f));
            Speed = sp;

            // follow the snow: the vertical speed that puts the board on the mesh next step
            float nx = p.x + vt.x * dt, nz = p.z + vt.z * dt;
            float wantY = Slope.DrawnHeight(nx, nz) + R + Undercut * (0.3f + 0.08f * Mathf.Sin(churnT * 9f));   // lifted on the churning snow
            float vy = (wantY - p.y) / dt;
            AboveGround = 0f;

            // fell into a crevasse

            if (popPending)
            {
                popPending = false;
                Jumped?.Invoke();
                // pop up off the snow; on a face that tilts back up the hill, push straight up rather than
                // along its normal, which would send a slow rider backwards
                Vector3 popDir = n.z < 0f ? Vector3.Slerp(n, Vector3.up, 0.85f) : n;
                float pop = Mathf.Lerp(5.5f, 8f, Mathf.Clamp01(popHold / 0.8f));
                // Super Jump: SuperJumpLift times the height, straight up off the snow so the extra air is height,
                // not a launch down the hill (height goes with the square of the pop's speed)
                bool super = SuperJumpTime > 0f && !Tube;
                if (super) { pop *= Mathf.Sqrt(SuperJumpLift); popDir = Vector3.Slerp(popDir, Vector3.up, 0.5f); }
                var leave = TakeOff(p, vt + popDir * pop, 0.15f);
                superAir = super;
                return leave;
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
        Vector3 PipeAir(Vector3 p, Vector3 v)
        {
            float side = Mathf.Sign(p.x - Slope.Centre(p.z));
            // (height is held to a few metres by extra pull while rising in AirStep, not by clipping speed here)
            // keep a little of the outward speed (the wall is nearly vertical up there); AirStep then curves
            // the flight back over the pipe, so nothing changes in a single step
            pipeAir = true;   // the near-vertical lip has already turned most of the speed upward
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
            // leaving a pipe wall or its deck by any route: the air arcs back over the pipe
            float off = Mathf.Abs(p.x - Slope.Centre(p.z));
            if (Course.PipeWeight(p.z) > 0.5f && off > Course.PipeFlat + 1f && off < Course.PipeHalfWidth + Course.PipeDeck) pipeAir = true;
            airLock = lockTime;
            AirTime = 0f; Peak = 0f;
            Carve = 0f; Skid = 0f;
            superAir = false;
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
            if (T < 0.6f || Tube) return;   // tubes just fly (airs still score on landing)
            TrickTime = T;
            int pick = (int)(Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 7.13f + p.z)) * 1000f);
            string grab = grabs[pick % grabs.Length];
            int pts;
            bool pipe = Course.InPipe(p.x, p.z);
            if (T < 0.85f) { TrickGrab = 1 + pick % grabs.Length; TrickName = grab; pts = 150; }
            // whole turns only: a half turn (180, 540) would leave the board backwards and snap it at landing
            else if (T < 1.15f) { TrickSpin = Mathf.PI * 2f; TrickGrab = 1 + pick % grabs.Length; TrickName = "360 " + grab; pts = pipe ? 500 : 450; }
            else if (T < 1.5f)
            {
                if (pick % 2 == 0) { TrickSpin = Mathf.PI * 2f; TrickGrab = 1 + pick % grabs.Length; TrickName = "Stalled 360 " + grab; pts = 650; }
                else { TrickFlip = -Mathf.PI * 2f; TrickName = "Backflip"; pts = 800; }
            }
            else if (T >= 2.0f)
            {
                // super-jump air: room for the big ones (rotations stay under about 9 rad/s, so they still read)
                if (pick % 3 == 0) { TrickSpin = Mathf.PI * 6f; TrickGrab = 1 + pick % grabs.Length; TrickName = "1080 " + grab; pts = 2000; }
                else if (pick % 3 == 1) { TrickFlip = -Mathf.PI * 4f; TrickName = "Double Backflip"; pts = 2200; }
                else { TrickSpin = Mathf.PI * 4f; TrickFlip = -Mathf.PI * 4f; TrickGrab = 1 + pick % grabs.Length; TrickName = "Double Cork 1440 " + grab; pts = 2600; }
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
            if (pipeAir)
            {
                // back over the pipe: a steady pull toward the middle, ending with the board pointing back down
                float side = Mathf.Sign(p.x - Slope.Centre(p.z));
                v.x -= side * 28f * dt;
                if (v.y > 0f) v.y -= 0.8f * G * dt;   // pipe airs top out at a few metres, smoothly
            }
            // relax the edge; in the air the board swings round to line up with where you are going, so the
            // landing never has to snap it, and a little steering adds to that
            CarveTurn(dt, 30f);
            float flat = v.x * v.x + v.z * v.z;
            // (once a trick's rotation is done it lines up too: in a long super-jump flight, steering during the
            // trick would otherwise leave the board well off its line and snap it round at the landing)
            if (flat > 4f && (TrickName == null || AirTime > 0.8f * TrickTime))
            {
                float travel = Mathf.Clamp(Mathf.Atan2(v.x, v.z), -MaxHeading, MaxHeading);
                Heading = Mathf.MoveTowardsAngle(Heading * Mathf.Rad2Deg, travel * Mathf.Rad2Deg, 220f * dt) * Mathf.Deg2Rad;
            }
            Heading = Mathf.Clamp(Heading + Steer * 0.8f * dt, -MaxHeading, MaxHeading);
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
            pipeAir = false;
            Vector3 vt = Vector3.ProjectOnPlane(v, n);
            if (v.z > 0f && vt.z < 0.2f * v.z) vt.z = 0.2f * v.z;   // landing on an up-facing slope never reverses you
            // land facing where you are going: a trick always comes round clean
            if (vt.sqrMagnitude > 1f) Heading = Mathf.Clamp(Mathf.Atan2(vt.x, vt.z), -MaxHeading, MaxHeading);
            Lean = 0f; leanVel = 0f;

            // a super jump comes down from four times the height: the power-up's legs soak most of it, so it
            // lands like an ordinary big air rather than a slam (the dust and sound still show a big one)
            if (superAir) impact = Mathf.Min(impact, 11f + 0.25f * Mathf.Max(0f, impact - 11f));
            superAir = false;
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
                // a skip of a fraction of a second (bouncing off a steep face just after a big landing) was never a
                // jump you meant: no trick to bail
                else if (AirTime > 0.3f) RoughLanding?.Invoke("Bailed the " + TrickName);
            }
            else if (Peak > 0.8f && AirTime > 0.45f) Trick?.Invoke("Air " + AirTime.ToString("0.0") + "s", Mathf.RoundToInt(AirTime * 120f));
            if (hard) RoughLanding?.Invoke("Hard landing");
            ClearTrick();
            return new Vector3(vt.x, vy, vt.z);
        }

        // ---------------------------------------------------------------- boxes and rails

        bool TryGrind(Vector3 p, Vector3 v)
        {
            if (Tube) return false;
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
                // ease onto the line over a few frames instead of snapping onto it
                grindOff = p - (feat.a + dir * grindS + Vector3.up * R);
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
            grindOff *= Mathf.Exp(-dt * 12f);
            Vector3 target = feat.a + dir * grindS + Vector3.up * R + grindOff;
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
            if (InvincibleTime > 0f && Smash(ob, c.collider))
            {
                // the step ahead missed it (it came from the side, or the bear ran into you): carry on as if it
                // were never there, minus the smash's toll
                rb.linearVelocity = lastV * SmashKeep;
                return;
            }
            HitObstacle?.Invoke(ob, c.collider.bounds.center, into);
            if (invulnerable > 0f) return;
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

        // ---------------------------------------------------------------- invincible

        /// <summary>
        /// Invincible: anything about to be hit is smashed first (switched off and handed to the game to break
        /// up), so the board never touches it and is never stopped by it.
        /// </summary>
        Vector3 SmashAhead(float dt, Vector3 p, Vector3 v)
        {
            Vector3 c = p + v * dt + Vector3.up * 0.5f;
            int k = Physics.OverlapCapsuleNonAlloc(p, c, R + 0.35f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++)
            {
                if (near[i].attachedRigidbody == rb) continue;
                var ob = near[i].GetComponentInParent<Obstacle>();
                if (ob != null && Smash(ob, near[i])) v *= SmashKeep;
            }
            return v;
        }

        bool Smash(Obstacle ob, Collider col)
        {
            if (!col.enabled) return false;
            col.enabled = false;   // the game breaks it up and puts it back (or not) itself
            Smashed?.Invoke(ob, col.bounds.center, rb.linearVelocity.sqrMagnitude > lastV.sqrMagnitude ? rb.linearVelocity : lastV);
            return true;
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
            pipeAir = false;
            Airborne = false; Grinding = false;
            crashTimer = 0.8f;   // at least this long; then until the player taps to get up (RequestUp)
            RequestUp = false;
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
            // get up only once the thrown body has come to rest (or after a long slide, regardless)
            // back on your feet only when the player asks: watching where the body ends up is half the fun
            if (crashTimer <= 0f && RequestUp && !StayDown) return Recover(p);
            return new Vector3(h.x, (wantY - p.y) / dt, h.z);
        }

        /// <summary>Where the thrown rider is, set by the ragdoll while a crash plays out.</summary>
        public Vector3 CrashFollow;
        public bool HasCrashFollow;
        /// <summary>Set while the thrown body is still moving: stay down until it stops.</summary>
        public bool HoldCrash;
        /// <summary>Set by the game when this fall ends the run: no getting up.</summary>
        public bool StayDown;
        /// <summary>The player tapped to get back up.</summary>
        public bool RequestUp;
        /// <summary>Down long enough that a tap will get you back up.</summary>
        public bool CanGetUp => Crashed && crashTimer <= 0f && !StayDown && !HoldCrash;

        Vector3 Recover(Vector3 p)
        {
            Crashed = false;
            HasCrashFollow = false;
            RequestUp = false;
            rb.detectCollisions = true;
            invulnerable = Mathf.Max(invulnerable, 1.5f);   // a moment to get going before anything can knock you down again
            Heading = 0f;
            // climb out of a crevasse onto its nearer rim
            if (Course.CrevasseNear(p.x, p.z, 0.5f, out float cx, out float half))
                p.x = cx + Mathf.Sign(p.x - cx + 0.01f) * (half + 1.6f);
            // stopped on a ridge's cliff: get up on the snow below it, not on the rock
            p.x = Ridge.SafeX(p.x, p.z);
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
