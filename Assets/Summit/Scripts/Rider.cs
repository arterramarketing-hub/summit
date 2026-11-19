using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The rider's physics. A Unity Rigidbody carries the motion (interpolated between physics
    /// steps, colliding with trees, rocks and animals); this script supplies riding a board (or skis, or a
    /// toboggan: the same model with each ride's own numbers on it, see Ride.cs).
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
        public static float DragOpen = 0.0078f, DragTuck = 0.0050f; // air drag per (m/s)^2 (lower: speed builds and holds)
        public static float Friction = 0.035f;                       // snow friction coefficient
        public static float AirControl = 0.6f;                       // how fast steering bends the flight in the air (rad/s)
        public static float MaxLean = 0.72f;                         // full-thumb edge angle (rad, ~41 degrees)
        public static float LeanSpring = 17f;                        // how fast the body gets onto its edge (rad/s)
        public static float CarveK = 27f;                            // turn rate = CarveK * tan(lean) / speed
        public static float MinCarveSpeed = 7f, MaxYawRate = 2.4f;
        public static float GripLimit = 37f;                         // sideways acceleration the edge can hold (m/s^2)
        public static float Grip = 42f;                              // 1/s: how fast slip dies when the edge holds (high: the edge bites, no drifting)
        public static float TurnDrag = 0.12f;                        // speed lost per radian of turning
        public static float TakeoffClearance = 0.22f;
        public static float FallLinePull = 2.2f;                     // 1/s: how fast a flat board swings back down the hill
        public static float DeadlyDrop = 18f;                        // m fallen beyond the mountain's own line onto the foot of a deadly cliff (Deadly): fatal
        public static float SlamImpact = 28f;                        // m/s into the snow, square on: a landing nobody rides away from
        public static float MaxLandAngle = 65f;                      // deg between the flight and the snow: shallower than this (even onto the flat), any height is a landing
        public static float BadFlip = 70f, BadSpin = 55f;            // deg a bailed trick may be off upright / off the line and still be ridden out

        /// <summary>The last landing: how hard (m/s into the snow) and how steeply (deg between flight and snow) it came in.</summary>
        public float LastLandImpact { get; private set; }
        public float LastLandAngle { get; private set; }

        /// <summary>
        /// Riding a snow tube instead of a board: no edges, so no carving. You lean to nudge it round, it slides
        /// wherever momentum takes it and always drifts back down the fall line; it bounces off things that would
        /// throw a snowboarder; no tricks, but airs still score.
        /// </summary>
        public bool Tube;

        /// <summary>What is being ridden (the game sets it from Progress.Ride). Everything that needs to know asks here.</summary>
        public RideKind Ride { get; private set; }
        /// <summary>That ride's numbers (Ride.cs): all ones on the snowboard.</summary>
        public RideFeel Feel => feel;
        RideFeel feel = Rides.Of(RideKind.Snowboard);
        public void SetRide(RideKind k) { Ride = k; feel = Rides.Of(k); }
        /// <summary>The snowboard tricks a daily mission would take the trick in hand as (its grab's namesakes), or null.</summary>
        public string TrickAlias { get; private set; }

        public const float R = 0.3f;              // the collision sphere around the feet
        const float MaxHeading = 1.45f;           // about 83 degrees off the fall line

        public RiderInput input;

        // ---- state the rest of the game reads
        public float Heading, Speed, Steer, AirTime, StartZ;
        public bool Airborne, Crashed, Running;
        /// <summary>0..1, set by the avalanche while it flows under the board: the snow heaves, the edge cannot bite.</summary>
        public float Undercut;
        public float Lift;   // how high the debris under the board stands over the snow (the slide sets it)
        // the part of last step's vertical speed that only put the board onto the drawn snow, not riding: the mesh's
        // 1.5 m facets standing off the smooth slope the board steers on, the debris lifting it, a landing's leftover
        // depth. Kept out of the next step's speed along the snow, which is the board's own motion. Fed back in, the
        // facets bled speed on every curve (a kicker's ramp, a jump's bench: tens of m/s^2 of braking from nothing),
        // and a landing's few centimetres, divided by the step and laid onto a steep face, threw the board back up
        // the hill
        float fixVy;
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
        /// <summary>
        /// Momentum, 0..1: riding fast without falling lets you carry more speed (less drag), so a good run keeps
        /// getting faster and harder. A fall throws it all away; slowing right down lets it drain.
        /// </summary>
        public float Flow { get; private set; }
        const float FlowDrag = 0.55f;   // the drag left at full flow


        // ---- signs and flags ridden through (see Breakables)
        float wobble, slowFor, slowRate;
        Vector3 shove;
        /// <summary>Just back on your feet (or saved by a shield): nothing can knock you down for a moment.</summary>
        public bool Invulnerable => invulnerable > 0f;
        /// <summary>The share of your speed the thrown body carries on with in this crash (a snapped tree: TreeBreak).</summary>
        public float CrashCarry = 1f;
        /// <summary>
        /// Rode through a sign or a flag: keep this share of your speed (lost over a fraction of a second, as the
        /// board ploughs through) and wobble for this long. Hit square on harder than crashAbove (m/s), it knocks
        /// you down instead, unless a shield takes it; invincible, you go through clean. Returns true on a crash.
        /// </summary>
        public bool Breakthrough(float keep, float wobbleFor, float into, float crashAbove, string why)
        {
            if (!Running || Crashed) return false;
            if (InvincibleTime > 0f) { keep = Mathf.Max(keep, SmashKeep); wobbleFor = 0f; }
            else if (into > crashAbove && invulnerable <= 0f)
            {
                if (Shields <= 0) { DoCrash(why); return true; }
                Shields--;
                invulnerable = 0.9f;
                ShieldHit?.Invoke();
            }
            const float over = 0.2f;
            slowFor = over; slowRate = -Mathf.Log(Mathf.Clamp(keep, 0.05f, 1f)) / over;
            wobble = Mathf.Max(wobble, wobbleFor);
            return false;
        }
        public void AddBoost(float seconds)
        {
            BoostTime = Mathf.Max(BoostTime, 0f) + seconds;
            Flow = Mathf.Min(1f, Flow + 0.25f);   // a Mountain Zoom is speed you keep
            if (Running && !Crashed && rb != null) rb.linearVelocity += Velocity.normalized * 4f;
        }

        Rigidbody rb;
        Collider ownCol;
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
            ownCol = col;
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
            DropFrom = p;
            Heading = 0f; Speed = 0f; Steer = 0f; groundVy = 0f; fixVy = 0f;
            Airborne = false; Crashed = false; Running = false; Grinding = false; pipeAir = false;
            HasCrashFollow = false; rb.detectCollisions = true; StayDown = false; HoldCrash = false;
            AirTime = 0f; Tuck = 0f; Carve = 0f; Skid = 0f; Peak = 0f; AboveGround = 0f;
            Lean = 0f; leanVel = 0f; YawRate = 0f;
            ClearTrick();
            popPending = false; airLock = 0f; lastGrind = -1;
            Flow = 0f; Shields = 0; BoostTime = 0f; invulnerable = 0f; SuperJumpTime = 0f; InvincibleTime = 0f; superAir = false;
            Undercut = 0f; Lift = 0f; fixVy = 0f;
            wobble = 0f; slowFor = 0f; shove = Vector3.zero;
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
            if (Running && Time.timeScale > 0f && input != null && input.Released && input.LastHold > 0.08f)   // (pause-menu taps are not jumps)
            {
                popPending = true;
                popHold = input.LastHold;
            }
        }

        /// <summary>Forget a jump queued from the last release (e.g. a tap that closed the pause menu).</summary>
        public void CancelPop() { popPending = false; }

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
                Heading = Mathf.Lerp(Heading, Mathf.Clamp(Heading, -0.5f, 0.5f), Undercut);   // swung about, but carried on down the hill
            }
            Vector3 p = rb.position, v = rb.linearVelocity;
            if (shove.sqrMagnitude > 0f) { if (!Crashed) v += shove; shove = Vector3.zero; }   // knocked aside off a rail's end (TryGrind)
            if (slowFor > 0f && !Crashed) { slowFor -= dt; v *= Mathf.Exp(-slowRate * dt); }
            if (wobble > 0f && !Crashed)
            {
                // knocked off balance by a sign: the board swings about under you for a moment, and steering
                // only half registers, fading as you find your feet
                wobble = Mathf.Max(0f, wobble - dt);
                churnT += dt;
                float k = Mathf.Min(1f, wobble / 0.35f);
                Steer = Steer * (1f - 0.5f * k) + k * 0.7f * Mathf.Sin(churnT * 13f);
                Heading += k * 0.8f * Mathf.Sin(churnT * 8.5f + 1f) * dt;
            }
            if (InvincibleTime > 0f && !Crashed) v = SmashAhead(dt, p, v);
            // a young tree or a dead snag hit fast enough snaps: you go down, but on through where it stood (TreeBreak)
            else if (!Crashed && TreeBreak.Instance != null) TreeBreak.Instance.RiderAhead(this, p, v, dt);
            // a sign or a flag where you are about to be, board to shoulders (see Breakables)
            if (!Crashed) Breakable.Probe(p, p + v * dt + Vector3.up * 1.2f, 0.45f, ownCol);
            // over a crevasse with nothing under you (riding in, or coming down from the air): you fall in. Still
            // rising off the snow (an ollie at the lip), you are clearing it
            if (!Crashed && !Grinding && Course.CrevasseCut(p.x, p.z) > 1.2f
                && (!Airborne || (Vector3.Dot(v, Slope.Normal(p.x, p.z)) < 0f && p.y - Slope.DrawnHeight(p.x, p.z) < 0.8f)))
            {
                Airborne = false;
                DoCrash("Crevasse!");
                v = new Vector3(v.x * 0.4f, Mathf.Min(v.y, -2f), v.z * 0.4f);
            }
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
            float spring = LeanSpring * feel.TurnResponse;
            float acc = spring * spring * (target - Lean) - 2f * spring * leanVel;
            leanVel += acc * dt;
            Lean += leanVel * dt;
            float yaw = Mathf.Sign(Lean) * Mathf.Min(MaxYawRate * feel.TurnRate, CarveK * feel.TurnRate * Mathf.Tan(Mathf.Abs(Lean)) / Mathf.Max(sp, MinCarveSpeed));
            YawRate = yaw;
            return yaw;
        }

        Vector3 GroundStep(float dt, Vector3 p, Vector3 v)
        {
            Vector3 n = Slope.Normal(p.x, p.z);
            Vector3 vIn = v - Vector3.up * fixVy;   // the board's own motion, without last step's correction onto the snow
            Vector3 vt = Vector3.ProjectOnPlane(vIn, n);
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
                // momentum builds while you hold speed, and drains when you scrub it away
                if (sp > 20f) Flow = Mathf.Min(1f, Flow + dt / 14f);
                else if (sp < 12f) Flow = Mathf.Max(0f, Flow - dt / 3f);
                // (momentum carries you up to about 170 km/h; past that the air takes over again, so it never runs away)
                float flowK = Mathf.Lerp(1f, FlowDrag, Flow * Mathf.Clamp01((52f - sp) / 8f));
                float drag = (Tube ? DragOpen * 1.15f : Mathf.Lerp(DragOpen, DragTuck, Tuck)) * feel.Drag * (BoostTime > 0f ? 0.6f : 1f) * flowK * sp * sp + (Tube ? Friction * 1.3f : Friction) * G * n.y;
                vt -= vt / sp * Mathf.Min(sp, drag * dt);
            }
            // 4. the edge swings the velocity round to where the board points. It holds as long as the
            //    sideways pull is within its grip; beyond that the board skids
            float lost = 0f, demand = sp * Mathf.Abs(yaw);
            float hold = Mathf.Min(1f, GripLimit * feel.Grip / Mathf.Max(demand, 1e-3f));
            sp = vt.magnitude;
            if (sp > 0.05f)
            {
                float beta = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(vt, r), Vector3.Dot(vt, f)), -1.5f, 1.5f);
                float beta2 = beta * Mathf.Exp(-(Tube ? 2.5f : Grip * feel.Grip * hold * (1f - 0.85f * Undercut)) * dt);   // a tube has no edge: it slides
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

            // follow the snow: the vertical speed that puts the board on the mesh next step. Only vt is riding; the rest
            // of that vertical speed is the drawn snow's own facets (and any depth the board is off it) and is kept apart
            // (fixVy). The mesh's own rise or fall under the path (surfVy) is what tells a lip from a slope
            float nx = p.x + vt.x * dt, nz = p.z + vt.z * dt;
            float churn = Lift + Undercut * (0.3f + 0.08f * Mathf.Sin(churnT * 9f));   // lifted on the churning snow
            float hereY = Slope.DrawnHeight(p.x, p.z) + R + churn;
            float wantY = Slope.DrawnHeight(nx, nz) + R + churn;
            float surfVy = (wantY - hereY) / dt;
            float vy = (wantY - p.y) / dt;
            float fix = vy - vt.y;
            AboveGround = 0f;

            // fell into a crevasse

            // carried on the avalanche's debris: it holds the board down in it, no jumping off and no flying off
            // its lumps (or off the slope's features under it)
            if (Undercut > 0f) { popPending = false; groundVy = surfVy; fixVy = fix; return new Vector3(vt.x, vy, vt.z); }
            if (popPending)
            {
                popPending = false;
                Jumped?.Invoke();
                // pop up off the snow; on a face that tilts back up the hill, push straight up rather than
                // along its normal, which would send a slow rider backwards
                Vector3 popDir = n.z < 0f ? Vector3.Slerp(n, Vector3.up, 0.85f) : n;
                float pop = Mathf.Lerp(5.5f, 8f, Mathf.Clamp01(popHold / 0.8f)) * feel.Pop;
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
                // off the edge of a drop: the face falls away from the way you are going, so you leave it on your own
                // line (laid onto that face, the speed into the air would be taken away, or turned back up the hill)
                if (!inPipe && Vector3.Dot(vIn, n) >= 0f) return TakeOff(p, vIn, 0.05f);
                // running into a wall that rises in front must not turn the speed into a launch straight up, so the
                // climb is capped at the ground's own rate
                if (!inPipe) vt.y = Mathf.Min(vt.y, Mathf.Max(groundVy, 0f) + 1.5f);
                return TakeOff(p, inPipe ? PipeAir(p, vt) : vt, 0.05f);
            }
            // the snow falls away faster than gravity can follow (a kicker's lip, a crest, a pipe's coping): fly
            // (leave with the slope's own vertical speed, never the mesh-following one, which can spike on coarse facets)
            float vUp = Mathf.Clamp(groundVy, vt.y - 2f, vt.y + 2f);
            if (sp > 7f && surfVy < groundVy - G * dt * 1.2f && FallsAway(p, new Vector3(vt.x, vUp, vt.z)))
                return TakeOff(p, new Vector3(vt.x, vUp, vt.z), 0.08f);
            // riding up a pipe wall: leave it with the wall's own vertical speed
            if (inPipe && vt.y > 1f && vy < vt.y - G * dt * 2f)
                return TakeOff(p, PipeAir(p, vt), 0.08f);

            groundVy = surfVy; fixVy = fix;
            DropFrom = p;
            return new Vector3(vt.x, vy, vt.z);
        }

        /// <summary>Where the rider last left the snow (or where they are, while on it): how far a fall has come.</summary>
        public Vector3 DropFrom;

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

        /// <summary>
        /// The snow drops away from the line the board would fly: clear of it by TakeoffClearance 0.12 s on, and never
        /// under it on the way there. (At speed that look-ahead is several metres: from part way up a kicker's ramp it
        /// reaches past the lip, and a launch from there would fly into the rest of the ramp.)
        /// </summary>
        bool FallsAway(Vector3 p, Vector3 v)
        {
            const float T = 0.12f;
            const int N = 6;
            for (int i = 1; i <= N; i++)
            {
                float t = T * i / N;
                float by = p.y + v.y * t - 0.5f * G * t * t;
                float gy = Slope.DrawnHeight(p.x + v.x * t, p.z + v.z * t) + R;
                if (i < N && by < gy - 0.02f) return false;
                if (i == N) return by - gy > TakeoffClearance;
            }
            return false;
        }

        Vector3 TakeOff(Vector3 p, Vector3 v, float lockTime)
        {
            Airborne = true;
            DropFrom = p;
            // leaving a pipe wall or its deck by any route: the air arcs back over the pipe
            float off = Mathf.Abs(p.x - Slope.Centre(p.z));
            if (Course.PipeWeight(p.z) > 0.5f && off > Course.PipeFlat + 1f && off < Course.PipeHalfWidth + Course.PipeDeck) pipeAir = true;
            airLock = lockTime;
            fixVy = 0f;
            AirTime = 0f; Peak = 0f;
            Carve = 0f; Skid = 0f;
            superAir = false;
            PlanTrick(p, v);
            return v;
        }

        // ---------------------------------------------------------------- tricks

        void ClearTrick() { TrickAlias = null; TrickName = null; TrickSpin = 0f; TrickFlip = 0f; TrickGrab = 0; TrickTime = 0f; }

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
            var grabs = feel.Grabs;   // each ride has its own grabs (Ride.cs)
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
            if (TrickGrab > 0 && feel.GrabCountsAs != null && TrickGrab <= feel.GrabCountsAs.Length) TrickAlias = feel.GrabCountsAs[TrickGrab - 1];
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
            // air control: steering bends the line of flight a little (not in the pipe, which steers itself), so
            // you can pick your landing; never round to face uphill
            if (!pipeAir && flat > 4f && Mathf.Abs(Steer) > 0.02f)
            {
                float travelNow = Mathf.Atan2(v.x, v.z);
                float turn = Steer * AirControl * feel.AirControl * dt;
                if (Mathf.Abs(travelNow + turn) > MaxHeading) turn = 0f;
                float cs = Mathf.Cos(turn), sn = Mathf.Sin(turn);
                v = new Vector3(v.x * cs + v.z * sn, v.y, -v.x * sn + v.z * cs);
            }
            Speed = new Vector2(v.x, v.z).magnitude;

            float nx = p.x + v.x * dt, nz = p.z + v.z * dt;
            float gy = Slope.DrawnHeight(nx, nz) + R + Lift;   // (come down on the avalanche's debris, if it is under you)
            fixVy = 0f;
            AboveGround = Mathf.Max(0f, p.y - (Slope.DrawnHeight(p.x, p.z) + R));
            Peak = Mathf.Max(Peak, AboveGround);
            // (the lock after a takeoff keeps a lip's facets from landing you straight back on it, but never holds you
            // up while the snow comes up through the board)
            if (p.y + v.y * dt <= gy && (airLock <= 0f || p.y + v.y * dt < gy - 0.1f)) return Land(dt, p, v, gy);
            return v;
        }

        Vector3 Land(float dt, Vector3 p, Vector3 v, float gyNext)
        {
            Vector3 n = Slope.Normal(p.x, p.z);
            float impact = Mathf.Max(0f, -Vector3.Dot(v, n));
            // you never fall off a cliff in mid-air, only when you hit something: rock can't be ridden (a fair,
            // violent tumble), and the foot of a deadly cliff far below where you left it is the end (the thrown body
            // judges that: see RiderBody.FallStep). Up to then you are still on your board, and a landing is a landing.
            float fell = DropFrom.y - p.y - Slope.Fall * Mathf.Max(0f, p.z - DropFrom.z);
            // how steeply the board came into the snow: off a big air down a slope it comes in at a glancing angle,
            // however far it fell; dropped onto ground that does not fall away with it, it comes in square
            float along = Vector3.ProjectOnPlane(v, n).magnitude;
            LastLandAngle = Mathf.Atan2(impact, along) * Mathf.Rad2Deg;
            LastLandImpact = impact;
            if (RockFace.IsRock(n, p.x, p.z)) { DoCrash("Hit the rocks!"); return v; }
            // off one of the cliffs the mountain marks to keep away from, down to the ground below it: nobody rides
            // away from that (anywhere else, a long way down is only a big air: see the slam below)
            if (fell > DeadlyDrop && Deadly.Over(DropFrom, p)) { DoCrash("Cliffed!"); return v; }
            // a trick that had not come round: the board meets the snow upside down or across the way you are going
            string bad = BadLanding();
            if (bad != null) { DoCrash(bad); return v; }
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
            // a slam is hard AND square on: landed down a slope at a glancing angle the legs soak it, however big the
            // air, and the board rides away (slowed, and wobbling as you find your feet)
            if (impact > SlamImpact && LastLandAngle > MaxLandAngle) { DoCrash("Slammed!"); return v; }
            bool hard = impact > 18f;
            float keep = Mathf.Max(0.5f, 1f - Mathf.Clamp01((impact - 8f) * 0.012f));
            if (hard)
            {
                keep *= 0.9f;
                wobble = Mathf.Max(wobble, Mathf.Clamp((impact - 18f) / 14f, 0.25f, 0.9f));
            }
            vt *= keep;
            Landed?.Invoke(impact);
            // onto the snow's own line from here, and back up out of it if the board had come down into it (kept apart:
            // see fixVy)
            float hereY = Slope.DrawnHeight(p.x, p.z) + R + Lift;
            float vy = (gyNext - p.y) / dt;
            groundVy = (gyNext - hereY) / dt; fixVy = vy - vt.y;
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
            // a ride with no legs under it (the toboggan) comes down flat and skips back up off the snow, a
            // smaller hop each time until it settles
            if (feel.Bounce > 0f && impact > 4.5f && Undercut <= 0f)
            {
                Bounced?.Invoke(impact);
                return TakeOff(p, vt + n * (impact * feel.Bounce), 0.05f);
            }
            return new Vector3(vt.x, vy, vt.z);
        }

        /// <summary>
        /// Landing in the middle of a trick that had not finished turning: the board as it is drawn (see RiderBody)
        /// is upside down, or across the line of travel (backwards is fine: that is riding switch). Null if it is
        /// near enough upright and lined up to ride out (the ordinary "Bailed" rough landing).
        /// </summary>
        string BadLanding()
        {
            if (TrickName == null || AirTime >= 0.8f * TrickTime || AirTime <= 0.3f) return null;
            float k = TrickProgress;
            float flip = Mathf.Abs(Mathf.DeltaAngle(0f, TrickFlip * k * Mathf.Rad2Deg));        // off upright
            float spin = Mathf.Abs(Mathf.DeltaAngle(0f, TrickSpin * k * Mathf.Rad2Deg));
            spin = Mathf.Min(spin, 180f - spin);                                                  // off the line, either way round
            if (flip > BadFlip) return "Landed upside down!";
            if (spin > BadSpin) return "Landed sideways!";
            return null;
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
                Vector3 onLine = Course.FeaturePoint(feat, s / len);
                float lateral = new Vector2(p.x - onLine.x, p.z - onLine.z).magnitude;
                if (lateral > feat.halfWidth + 0.65f) continue;
                float bottom = p.y - R;
                if (bottom < onLine.y - feat.height - 0.3f || bottom > onLine.y + 1.0f) continue;
                float along = Vector3.Dot(v, dir);
                if (along < 4f) continue;
                // only lined up with it (within 45 degrees): boarded from across it, the grind keeps only the speed along
                // it, and a rider crossing a box at 90 km/h was stopped in his tracks onto a 30 km/h slide
                float flatSp = new Vector2(v.x, v.z).magnitude, flatAlong = Vector2.Dot(new Vector2(v.x, v.z), new Vector2(dir.x, dir.z).normalized);
                if (flatAlong < 0.707f * flatSp) continue;
                if (!feel.Rails && feat.kind == Course.Kind.Rail)
                {
                    // a toboggan cannot balance on a rail: it clatters into the end of it and is knocked off to
                    // the side it came from, slower and wobbling. Worth a few points for the nerve, and it counts
                    // as the rail for the day's missions
                    lastGrind = feat.id;
                    var off = new Vector3(p.x - onLine.x, 0f, p.z - onLine.z);
                    Vector3 aside = off.sqrMagnitude > 1e-4f ? off.normalized : Vector3.Cross(Vector3.up, dir).normalized;
                    shove = aside * 3.5f;
                    const float over = 0.2f;
                    slowFor = over; slowRate = -Mathf.Log(0.8f) / over;
                    wobble = Mathf.Max(wobble, 0.5f);
                    GrindStart?.Invoke(feat);
                    Bounced?.Invoke(8f);
                    Trick?.Invoke(feel.RailTrick, 60);
                    continue;
                }
                // on it: ride-on features are boarded automatically
                Grinding = true;
                GrindFeature = feat;
                lastGrind = feat.id;
                grindS = Mathf.Max(0f, s);
                grindSpeed = along;
                // ease onto the line over a few frames instead of snapping onto it
                grindOff = p - (onLine + Vector3.up * R);
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
            float len = feat.Length;
            // along the feature's top as it follows the snow
            Vector3 dir = Course.FeatureDir(feat, grindS / len);
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
                string name = feat.kind == Course.Kind.Box ? feel.BoxTrick : feel.RailTrick;
                Trick?.Invoke(name, 150 + Mathf.RoundToInt(len * 25f));
                return TakeOff(p, dir * grindSpeed + Vector3.up * 3.5f, 0.1f);
            }
            grindOff *= Mathf.Exp(-dt * 12f);
            Vector3 target = Course.FeaturePoint(feat, grindS / len) + Vector3.up * R + grindOff;
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
            Flow = 0f;
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
            Speed = h.magnitude;
            // get up only once the thrown body has come to rest (or after a long slide, regardless)
            // back on your feet only when the player asks: watching where the body ends up is half the fun
            if (crashTimer <= 0f && RequestUp && !StayDown) return Recover(p);
            // the board's sphere (nothing to hit while the body is thrown) goes along the snow under it. Put on the snow
            // directly, not by a speed that gets it there in one step: in a crash's hit-stop a step is a few
            // thousandths of a second, and a metre off the snow (thrown from the air) became thousands of m/s
            float hereY = Slope.DrawnHeight(p.x, p.z) + R;
            if (Mathf.Abs(p.y - hereY) > 1e-4f) { p.y = hereY; rb.position = p; }
            float wantY = Slope.DrawnHeight(p.x + h.x * dt, p.z + h.z * dt) + R;
            return new Vector3(h.x, (wantY - hereY) / dt, h.z);
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
            if (Course.CrevasseNear(p.x, p.z, 0.5f, out var cv)) p = Course.CrevasseRim(cv, p, 1.6f);
            // stopped on a ridge's cliff: get up on the snow below it, not on the rock
            p.x = Ridge.SafeX(p.x, p.z);
            // step clear of whatever stopped you, toward the piste
            float toward = Mathf.Sign(Slope.Centre(p.z) - p.x + 0.01f);
            for (int i = 0; i < 6 && Blocked(p); i++) p.x += toward * 1.5f;
            p.y = Slope.DrawnHeight(p.x, p.z) + R;
            rb.position = p;
            DropFrom = p;
            groundVy = 0f; fixVy = 0f;
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
