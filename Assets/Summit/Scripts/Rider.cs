using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The rider's physics. A Unity Rigidbody carries the motion (interpolated between physics
    /// steps); this script supplies riding a board (or skis, or a toboggan: the same model with each
    /// ride's own numbers on it, see Ride.cs).
    ///
    /// How it moves on the snow is one model, in RiderMove.cs (slide): gravity along the snow sets the
    /// speed and pulls the way the board goes toward the fall line; the finger is a push sideways across
    /// the fall line, answered at once, against the snow's resistance to the slide; let go, the board
    /// eases back down the fall line. Nothing lets go or washes out, and nothing is aimed at. What it
    /// rides over and what it runs into is in RiderRideOver.cs; the air, landings, rails and falls are here.
    ///
    /// Heading 0 is the board pointing straight down the mountain (+Z); positive turns right (+X).
    /// </summary>
    public partial class Rider : MonoBehaviour
    {
        // ---- tuning (static so the test harness can sweep them)
        public static float G = 22f;                                 // gravity, stronger than real for punch
        // air drag per (m/s)^2 (lower: speed builds and holds). A tenth more than it was (0.0078, 0.0050): the old board also lost speed at
        // every facet of the snow and every tilt it was dragged across, which this one does not, and the pace of a run is kept
        public static float DragOpen = 0.0086f, DragTuck = 0.0055f;
        public static float Friction = 0.035f;                       // snow friction coefficient
        public static float MaxLean = 0.72f;                         // full-thumb edge angle (rad, ~41 degrees)
        /// <summary>
        /// The TURN setting (the pause menu's SETTINGS, saved by SteerPrefs): how hard the same drag (or tilt) pushes
        /// the board sideways, 0.5 to 2.5, 1 the stock feel: at 2 half the throw is the whole push, at 0.5 the whole
        /// throw is half of it. Applies at once, mid-run.
        /// </summary>
        public static float TurnScale { get; set; } = 1f;
        public static float TakeoffClearance = 0.22f;
        /// <summary>
        /// The one fall that kills: this far (m) below the mountain's own line from where you last stood on rideable
        /// snow, wherever you come down. Past it the body hits the snow like a bomb and that is the end of the run;
        /// short of it any fall is only a landing (or a crash) by the ordinary rules. 100 m is four and a half seconds of
        /// free fall: nothing ridden ever comes close (the biggest airs, Super Jumps off the start and the jump cliffs,
        /// are under 15 m), the 25-55 m walls in the danger stretch are a hard crash you can get up from, and the ridges'
        /// 165 m cliffs and the summit's 170-430 m one are the drops that end it.
        /// </summary>
        public static float DeathFall = 100f;
        /// <summary>Rock under the board: slower and scraping, never a fall by itself, and never off your line (see GroundStep).</summary>
        public static float RockFriction = 0.22f, RockSlam = 0.8f;
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
        /// <summary>How far below the snow's level, over a crevasse's opening, the board has fallen in (m).</summary>
        public const float FallInDepth = 0.6f;
        const float MaxHeading = 1.45f;           // about 83 degrees off the fall line

        public RiderInput input;

        // ---- state the rest of the game reads
        public float Heading, Speed, Steer, AirTime, StartZ;
        public bool Airborne, Crashed, Running;
        /// <summary>0..1, set by the avalanche while it flows under the board: the snow heaves, the edge cannot bite.</summary>
        public float Undercut;
        public float Lift;   // how high the debris under the board stands over the snow (the slide sets it)
        // the part of last step's vertical speed that was not riding: what put the board back on the snow's line (the
        // debris lifting it, a landing's leftover depth, the rise over a rock). Kept out of the next step's speed
        // along the snow, which is the board's own motion
        float fixVy;
        float churnT;
        public float Lean { get; private set; }          // edge angle, rad: + = leaning right (the finger's; with the body's chatter on it)
        /// <summary>The snow's own fall line under the board, smoothed (rad, as Heading): where gravity is pulling it.</summary>
        public float FallLine { get; private set; }
        public float YawRate { get; private set; }
        public float Tuck { get; private set; }
        public float Carve { get; private set; }         // 0..1 how hard the edge bites
        public float Skid { get; private set; }          // 0..1 how much the board is sliding sideways (the edge let go, or the board well across its way)
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

        // ---- tricks: the player's own, built in the air (RiderTricks.cs: TrickName, TrickSpin, TrickFlip, TrickGrab,
        //      TrickProgress, TrickDone, SpinNow, FlipNow and the rest)

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
        /// <summary>Riding over rock right now (the scraping, the grit, the wobble: see GroundStep).</summary>
        public bool OnRock { get; private set; }
        float rockScrapeT;
        public System.Action ShieldHit;
        public System.Action<Course.Feature> GrindStart;
        public System.Action<Obstacle, Vector3, float> HitObstacle;
        /// <summary>The collider of the last HitObstacle (one obstacle can have many: a crag's spires, a cabin's parts),
        /// and where the board met it.</summary>
        public Collider HitCollider { get; private set; }
        public Vector3 HitPoint { get; private set; }
        public System.Action<float> Bounced;                          // the tube bounced off something
        public System.Action<Obstacle, Vector3, Vector3> Smashed;     // invincible: what you went through, where, your velocity
        Vector3 grindOff;
        bool faceSlide; // in the air but against a face too steep to stand on, sliding down it (AirStep)
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
        /// <summary>Off balance (a knock, a hard landing, rock under the board), 0..1: the body's chatter, never the board's line.</summary>
        public float Wobble => Mathf.Clamp01(wobble / 0.35f);
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
        float groundVy, airLock, crashTimer, popHold, missClock;
        bool popPending;
        int lastMiss, lastGrind = -1;
        readonly System.Collections.Generic.List<Course.Feature> grindNear = new System.Collections.Generic.List<Course.Feature>();   // (TryGrind's, reused)
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
            FreeHitUsed = false; PeopleHit = 0;
            Body.isKinematic = true;
            rb.position = p;
            transform.position = p;
            StartZ = z;
            DropFrom = p;
            Heading = 0f; Speed = 0f; Steer = 0f; groundVy = 0f; fixVy = 0f;
            Airborne = false; Crashed = false; Running = false; Grinding = false; pipeAir = false;
            HasCrashFollow = false; rb.detectCollisions = true; StayDown = false; HoldCrash = false;
            AirTime = 0f; Tuck = 0f; Carve = 0f; Skid = 0f; Peak = 0f; AboveGround = 0f;
            Lean = 0f; edge = 0f; edgeVel = 0f; airTurn = 0f; YawRate = 0f; FallLine = 0f; PathSlope = 0f; groundKnown = false; faceSlide = false; yawSmooth = 0f; RunNow = 0f;
            Slip = 0f; Side = 0f; sideNow = 0f; easeFor = 99f; fallSeen = 0f; fallClock = 0;
            latSmooth = 0f; biteNow = 0f; deepIn = 0f; bodyLean = 0f; bodyLeanVel = 0f; crouchNow = 0f; crouchVel = 0f; Sink = 0f; SideEdge = 0f; SideSlope = 0f; Sliding = false; wasSliding = false;
            ClearTrick(); SwitchOff(); grindScoring = false; LandSpin = 0f; LandFlip = 0f; GrindYaw = 0f; GrindPress = 0f; TrickX = TrickY = 0f; GrabHeld = false;
            popPending = false; airLock = 0f; lastGrind = -1;
            Flow = 0f; Shields = 0; BoostTime = 0f; invulnerable = 0f; SuperJumpTime = 0f; InvincibleTime = 0f; superAir = false;
            Undercut = 0f; Lift = 0f; fixVy = 0f;
            wobble = 0f; slowFor = 0f; EndUnpin();
            Underfoot = default; snowFeel = Summit.SnowFeel.Firm;
            // (and the clocks behind the wobble and the churn: carried over, the same landing swung the board a
            // different way depending on every wobble before it, so a ride never came out the same twice)
            churnT = 0f; OnRock = false; rockScrapeT = 0f; popHold = 0f; missClock = 0f; lastMiss = 0;
            RoofReset(); RideOverReset();
        }

        public void Begin()
        {
            FreeHitUsed = false; PeopleHit = 0;   // (a free hit each run: ridercrash)
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
            // input is read per frame, physics runs on its own clock
            if (!Running || Time.timeScale <= 0f || input == null) return;   // (pause-menu taps are not jumps)
            // what the fingers' gestures mean here (RiderInput sorts them by context: the JUMP setting's double tap,
            // tap or flick, a second finger, Space): in an air long enough to trick a tap is the grab's
            // (RiderTricks); on a rail or a cable the pop off it, at once; anywhere else the jump
            bool trick = Airborne && trickAir && !Crashed;
            input.TrickMode = trick || (Grinding && !Crashed);
            input.OnRail = Grinding && !Crashed;
            if (input.RailPop && Grinding) { popPending = true; popHold = TapPop; }
            if (input.TrickTap && trick) tapQueue++;
            if (input.Jump && !trick && !Grinding)
            {
                popPending = true;
                popHold = TapPop;
            }
        }

        /// <summary>Forget a jump queued from the last release (e.g. a tap that closed the pause menu).</summary>
        public void CancelPop() { popPending = false; }

        void FixedUpdate() { if (Running) Tick(Time.fixedDeltaTime); }

        /// <summary>
        /// How strong a tapped jump is, on the old scale of "seconds the tuck was held" (0 a hop .. 0.8 the most):
        /// a tap has no wind-up, so every jump is the same good pop.
        /// </summary>
        public static float TapPop = 0.5f;

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
            TrickInput(dt);   // (the trick inputs, before the wobble and the churn get into the steering)
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
            }
            Vector3 p = rb.position, v = rb.linearVelocity;
            if (UnpinStep(dt, p, ref v)) { rb.linearVelocity = v; lastV = v; return; }   // pinned against something: eased free (RiderGetUp)
            if (slowFor > 0f && !Crashed) { slowFor -= dt; v *= Mathf.Exp(-slowRate * dt); }
            // knocked off balance (a sign, a hard landing, rock): the body chatters for a moment as you find your
            // feet (EdgeStep). The board keeps its line and the finger keeps the edge
            if (wobble > 0f && !Crashed) { wobble = Mathf.Max(0f, wobble - dt); churnT += dt; }
            if (InvincibleTime > 0f && !Crashed) v = SmashAhead(dt, p, v);
            // the trees, from what is drawn (TreeBreak): branches are brushed through and thin trunks (small trees,
            // treetops) snap, both ridden on through; a young tree or a dead snag hit fast enough snaps, and you go
            // down, but on through where it stood. Invincible, the trunks are smashed above and the branches still break
            if (!Crashed && TreeBreak.Instance != null) v = TreeBreak.Instance.RiderAhead(this, p, v, dt);
            // a sign or a flag where you are about to be, board to shoulders (see Breakables)
            if (!Crashed) Breakable.Probe(p, p + v * dt + Vector3.up * 1.2f, 0.45f, ownCol);
            // low things in the way are ridden over, not into (RiderRideOver)
            if (!Crashed) OverStep(p, v);
            // over a crevasse's opening there is nothing under the board: it carries on through the air on its own
            // line (fast enough across a narrow one, it comes down on the far lip). Only once it has dropped below the
            // snow's level, into the slot, has it fallen in. A narrow slot (a pinch) is skimmed at speed (Course.Solid)
            if (!Crashed && !Grinding && !Course.Solid(p.x, p.z, Speed))
            {
                if (p.y - R < Slope.DrawnHeight(p.x, p.z) - FallInDepth)
                {
                    Airborne = false;
                    DoCrash("Crevasse!");
                    v = new Vector3(v.x * 0.4f, Mathf.Min(v.y, -2f), v.z * 0.4f);
                }
                else if (!Airborne)
                {
                    // off the near lip: flying (no takeoff, no trick: you are not jumping, the snow just stopped)
                    Airborne = true; airLock = 0f; AirTime = 0f; Peak = 0f; Carve = 0f; Skid = 0f;
                    v -= Vector3.up * fixVy; fixVy = 0f;
                    ClearTrick();
                }
            }
            if (Crashed) v = CrashStep(dt, p, v);
            else if (Grinding) v = OnCable ? CableGrind.Step(this, dt, p, v) : GrindStep(dt, p, v);
            else
            {
                if (TryGrind(p, v)) v = GrindStep(dt, p, v);
                else if (CableGrind.TryBoard(this, p, v)) v = CableGrind.Step(this, dt, p, v);   // a lift's cable, or the ramp up to it
                else if (Airborne) v = AirStep(dt, p, v);
                else v = GroundStep(dt, p, v);
            }
            rb.linearVelocity = v;
            lastV = v;
            contactsAsked = false;
            if (!Crashed) CloseCalls(dt, p);
        }

        // ---------------------------------------------------------------- on the snow (GroundStep, the edge and the carve: RiderMove.cs)

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
                float gy = GroundAt(p.x + v.x * t, p.z + v.z * t, p.y) + R;
                if (i < N && by < gy - 0.02f) return false;
                if (i == N) return by - gy > TakeoffClearance;
            }
            return false;
        }

        Vector3 TakeOff(Vector3 p, Vector3 v, float lockTime, bool fromHere = true)
        {
            Airborne = true;
            if (fromHere) DropFrom = p;
            OnRock = false;
            // leaving a pipe wall or its deck by any route: the air arcs back over the pipe
            float off = Mathf.Abs(p.x - Slope.Centre(p.z));
            if (Course.PipeWeight(p.z) > 0.5f && off > Course.PipeFlat + 1f && off < Course.PipeHalfWidth + Course.PipeDeck) pipeAir = true;
            airLock = lockTime; faceSlide = false;
            fixVy = 0f;
            // (the board leaves the snow pointing as it was: whatever slip it had it flies with, until the finger turns it)
            airTurn = v.x * v.x + v.z * v.z > 1f && !Tube ? Mathf.Clamp(Mathf.DeltaAngle(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, Heading * Mathf.Rad2Deg) * Mathf.Deg2Rad, -MaxSlip, MaxSlip) : 0f;
            Sliding = false; wasSliding = false;   // (SideEdge and SideSlope stay this step's: what turned the board as it left)
            AirTime = 0f; Peak = 0f;
            Carve = 0f; Skid = 0f;
            superAir = false;
            // (off a face too steep to stand on you are falling, not jumping: no trick, or every touch on the way down a
            // rock face would plan one, and the next touch, a moment later, come down on it half way round)
            if (fromHere) PlanTrick(p, v); else ClearTrick();
            return v;
        }

        // ---------------------------------------------------------------- tricks

        // (ClearTrick, PlanTrick, TrickStep, BadLanding and LandTrick are in RiderTricks.cs: nothing is chosen for the
        // player any more; an air long enough is theirs to spin, flip and grab in)

        // ---------------------------------------------------------------- in the air

        Vector3 AirStep(float dt, Vector3 p, Vector3 v)
        {
            AirTime += dt;
            airLock -= dt;
            v -= Vector3.up * fixVy;   // (last step's correction back onto a face, see below: not the board's own motion)
            fixVy = 0f;
            TrickStep(dt, p, v);
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
            // the flight is mostly the flight's: gravity has it. The finger steers it a little (aircontrol): straight
            // from the finger, no edge to wait for, it swings the flight its way (AirDrift m/s^2 at a whole drag, a
            // fraction of what the edge gives on the snow) by no more than AirSideMost m/s of sideways speed over the
            // whole air, and it turns the board's nose ahead of the flight (AirTurn). So a landing can be lined up,
            // never flown somewhere else. While a spin is coming round, a drag its own way is the spin's; the other way
            // still steers. It comes down with that line and that slip, and the carving model takes it from there.
            // (Not in the pipe, which steers itself, nor sliding down a face too steep to ride: the rock has it)
            float flat = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            RunNow = RunHeading(p.z) * Mathf.Clamp01((flat - 5f) / 5f);
            EdgeStep(dt);
            bool sliding = faceSlide; faceSlide = false;
            if (AirTime <= dt * 1.5f) airSide = 0f;   // (a new air: the whole of its budget to steer with)
            if (flat > 2f)
            {
                float line = Mathf.Atan2(v.x, v.z);
                if (!pipeAir && !sliding && !Tube && Mathf.Abs(line) <= MaxHeading)
                {
                    float a = AirAsked();
                    if (trickAir && spinHalves != 0 && a * spinDir > 0f) a = 0f;   // (the spin's own way: the spin's)
                    float most = AirSideMost * feel.AirControl;
                    float dv = Mathf.Clamp(airSide + a * AirDrift * feel.AirControl * dt, -most, most) - airSide;
                    airSide += dv;
                    SideEdge = dv / dt; SideSlope = 0f;   // (the finger's drift: what turns the flight, for the test tools)
                    line = Mathf.Clamp(line + dv / flat, -MaxHeading, MaxHeading);
                    v = new Vector3(Mathf.Sin(line) * flat, v.y, Mathf.Cos(line) * flat);
                    airTurn = Mathf.MoveTowards(airTurn, a * AirTurnMost, AirTurnRate * dt);
                }
                else { airTurn = Mathf.MoveTowards(airTurn, 0f, AirTurnRate * dt); SideEdge = 0f; SideSlope = 0f; }
                Heading = Mathf.Clamp(line + airTurn, -MaxHeading, MaxHeading);
            }
            YawRate = 0f; yawSmooth = 0f; Slip = airTurn;
            if (Contact(dt, p, ref v)) return v;   // (something tall in the flight's way: RiderRideOver)
            Speed = new Vector2(v.x, v.z).magnitude;

            float nx = p.x + v.x * dt, nz = p.z + v.z * dt;
            // (come down on the avalanche's debris, if it is under you, or on a cabin's roof: RiderRoofs)
            float gy = GroundAt(nx, nz, p.y, out Vector3 fn, out var roofNext) + R + Lift;
            AboveGround = Mathf.Max(0f, p.y - (GroundAt(p.x, p.z, p.y) + R));
            // coming down on a roof's very edge, the step's end already past it: down on the edge, not on past it
            RoofEdge(p, ref gy, ref fn, ref roofNext);
            Peak = Mathf.Max(Peak, AboveGround);
            // (the lock after a takeoff keeps a lip's facets from landing you straight back on it, but never holds you
            // up while the snow comes up through the board)
            // (and nothing to come down on over a crevasse's opening: Tick sees you fall in)
            if (p.y + v.y * dt <= gy && Course.Solid(nx, nz, Speed))
            {
                // against a face too steep to stand on (rock or snow, a cliff or a crevasse's lip): no landing, the board
                // goes on falling down it, sliding on its surface, until it reaches ground it can stand on. Landing on it
                // threw the board straight back off again, a step or two later, over and over: hopping in the air on the
                // spot at a crawl, juddering down a cliff at speed
                if (fn.y < 0.62f && !Course.InPipe(nx, nz) && !pipeAir)
                {
                    float into = Vector3.Dot(v, fn);
                    if (into < 0f) v -= fn * into;
                    float fix = (gy - (p.y + v.y * dt)) / dt;   // back onto the face, kept apart (fixVy)
                    if (fix > 0f) { fixVy = fix; v.y += fix; }
                    OnRock = RockFace.IsRock(fn, nx, nz);
                    faceSlide = true;
                    return v;
                }
                if (airLock <= 0f || p.y + v.y * dt < gy - 0.1f) return Land(dt, p, v, gy, roofNext, fn);
            }
            return v;
        }

        Vector3 Land(float dt, Vector3 p, Vector3 v, float gyNext, Roofs.Roof roof = null, Vector3 roofN = default)
        {
            // (onto a roof: its pitch where the board comes down on it)
            Vector3 n = roof != null ? roofN : Slope.Normal(p.x, p.z);
            float topY = 0f; Vector3 topN = Vector3.up;
            bool onTop = roof == null && OverGround(p.x, p.z, Slope.DrawnHeight(p.x, p.z), out topY, out topN);   // (down on a rock: RiderRideOver)
            if (onTop) n = topN;
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
            // a fall past DeathFall, wherever it comes down: nobody rides away from that, and the body hitting the snow
            // ends it (RiderBody.FallStep). Anything shorter is a landing, on snow or on rock
            if (fell >= DeathFall) { DoCrash("Cliffed!"); return v; }
            bool rock = roof == null && (onTop || RockFace.IsRock(n, p.x, p.z));
            // a trick that had not come round: the board meets the snow upside down or across the way you are going
            string bad = BadLanding();
            if (bad != null) { DoCrash(bad); return v; }
            Airborne = false;
            AboveGround = 0f;
            pipeAir = false;
            // (moving away from that face, not into it, the board is only a few centimetres into the drawn snow where its
            // facets cut the corner of a lip: put back on it, it keeps its own line. Laid onto the face it was leaving, a
            // kicker's back side took two thirds of the speed)
            Vector3 vt = Vector3.Dot(v, n) < 0f ? Vector3.ProjectOnPlane(v, n) : v;
            if (v.z > 0f && vt.z < 0.2f * v.z) vt.z = 0.2f * v.z;   // landing on an up-facing slope never reverses you
            // the snow takes the part of the flight into it; it does not turn you: the board comes down going the way
            // it flew (laid onto a tilted face, the speed would be swung off across the hill)
            float flown = Mathf.Sqrt(v.x * v.x + v.z * v.z), laid = Mathf.Sqrt(vt.x * vt.x + vt.z * vt.z);
            if (flown > 1f && laid > 1e-3f && v.z > 0f) vt = new Vector3(v.x / flown * laid, vt.y, v.z / flown * laid);

            // a super jump comes down from four times the height: the power-up's legs soak most of it, so it
            // lands like an ordinary big air rather than a slam (the dust and sound still show a big one)
            if (superAir) impact = Mathf.Min(impact, 11f + 0.25f * Mathf.Max(0f, impact - 11f));
            superAir = false;
            // a slam is hard AND square on: landed down a slope at a glancing angle the legs soak it, however big the
            // air, and the board rides away (slowed, and wobbling as you find your feet)
            // (onto rock a little sooner: it gives nothing)
            if (impact > SlamImpact * (rock ? RockSlam : 1f) && LastLandAngle > MaxLandAngle - (rock ? 10f : 0f)) { DoCrash("Slammed!"); return v; }
            bool hard = impact > 18f;
            float keep = Mathf.Max(0.5f, 1f - Mathf.Clamp01((impact - 8f) * 0.012f));
            if (hard)
            {
                keep *= 0.9f;
                wobble = Mathf.Max(wobble, Mathf.Clamp((impact - 18f) / 14f, 0.25f, 0.9f));
            }
            vt *= keep;
            // (the board comes down pointing as the finger turned it in the air, a little off the way it flew at most:
            // the edge meets the snow with that slip, and the carving model brings the two together. Nothing is lined up)
            {
                float h = Mathf.Sqrt(vt.x * vt.x + vt.z * vt.z);
                if (h > 1f) Heading = Mathf.Clamp(Mathf.Atan2(vt.x, vt.z) + (Tube ? 0f : airTurn), -MaxHeading, MaxHeading);
            }
            wasSliding = true;   // (a landing's own slide is the landing's: it is not thrown across)
            easeFor = Mathf.Min(easeFor, 0f);   // (down across the fall line with no finger: it eases back from here, as if just let go)
            overLift = onTop ? Mathf.Max(0f, topY - Slope.DrawnHeight(p.x, p.z)) : 0f;   // (down on a rock: on its top, RiderRideOver)
            Landed?.Invoke(impact);
            // onto the snow's own line from here, and back up out of it if the board had come down into it (kept apart:
            // see fixVy)
            float hereY = roof != null ? gyNext : (onTop ? topY : Slope.DrawnHeight(p.x, p.z)) + R + Lift;
            float vy = (gyNext - p.y) / dt;
            groundVy = (gyNext - hereY) / dt; fixVy = vy - vt.y; groundKnown = true;
            LandTrick();   // the trick as it was actually landed, and the air itself (RiderTricks)
            if (hard) RoughLanding?.Invoke("Hard landing");
            ClearTrick();
            RoofStep(roof);   // (down on a roof: the points for getting up there, after the trick's: RiderRoofs)
            // a ride with no legs under it (the toboggan) comes down flat and skips back up off the snow, a
            // smaller hop each time until it settles
            if (feel.Bounce > 0f && impact > 4.5f && Undercut <= 0f)
            {
                Bounced?.Invoke(impact);
                return TakeOff(p, vt + n * (impact * feel.Bounce), 0.05f);
            }
            return new Vector3(vt.x, vy, vt.z);
        }

        // ---------------------------------------------------------------- boxes and rails

        bool TryGrind(Vector3 p, Vector3 v)
        {
            if (Tube) return false;
            // the park's boxes and rails, or a chalet's ridge (a box along the top of the roof: Roofs)
            bool park = Course.At(p.z) == Course.Section.Park || Course.At(p.z + 10f) == Course.Section.Park;
            if (!park && !Roofs.Near(p, 2f)) return false;
            if (park) Course.FeaturesNear(p.z, grindNear); else Roofs.RidgesNear(p, grindNear);
            foreach (var feat in grindNear)
            {
                if (feat.id == lastGrind) continue;
                if (feat.id >= Roofs.IdBase && RidgeSlam(v)) continue;   // (a ridge is landed on like the roof: RiderRoofs)
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
                    // a toboggan cannot balance on a rail: it clatters into the end of it and goes on by, slower
                    // and wobbling. Worth a few points for the nerve, and it counts
                    // as the rail for the day's missions
                    lastGrind = feat.id;
                    const float over = 0.2f;
                    slowFor = over; slowRate = -Mathf.Log(0.8f) / over;
                    wobble = Mathf.Max(wobble, 0.5f);
                    GrindStart?.Invoke(feat);
                    Bounced?.Invoke(8f);
                    Emit(feel.RailTrick, 60, LinkKind.Bonk);
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
                if (Airborne) LandTrick(true);   // a trick come round as the board met the rail still counts
                Airborne = false;
                ClearTrick();
                Lean = 0f; edge = 0f; edgeVel = 0f;
                GrindBegin(feat.kind == Course.Kind.Rail, false);
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
            GrindTick(dt, grindSpeed * dt);   // paid by the metre, and a flick is a switch-up (RiderTricks)
            Speed = grindSpeed;
            Heading = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z), -MaxHeading, MaxHeading);
            Carve = 0f; Skid = 0f; AboveGround = feat.height;
            bool pop = popPending;
            if (grindS >= len || pop)
            {
                // off the end with a little pop; a tap pops off anywhere along it, higher (and higher still with a Super Jump)
                Grinding = false;
                GrindEnd();
                bool super = pop && SuperJumpTime > 0f;
                if (pop) { popPending = false; Jumped?.Invoke(); }
                var leave = TakeOff(p, dir * grindSpeed + Vector3.up * (pop ? 5.5f * (super ? Mathf.Sqrt(SuperJumpLift) : 1f) : 3.5f), 0.1f);
                superAir = super;
                return leave;
            }
            grindOff *= Mathf.Exp(-dt * 12f);
            Vector3 target = Course.FeaturePoint(feat, grindS / len) + Vector3.up * R + grindOff;
            return (target - p) / dt;
        }

        // ---------------------------------------------------------------- lift cables (CableGrind.cs)
        // The same grind on a line the lifts supply: CableGrind decides when the board is on a cable or a launch
        // ramp and where it goes; these few doors are all it needs into the rider. Its stand-in features have
        // negative ids (the course's own count from 0).

        /// <summary>Grinding a lift's cable or riding one of its ramps, not a park feature.</summary>
        public bool OnCable => Grinding && GrindFeature.id < 0;
        /// <summary>Put the rider on a cable (announced, like boarding a rail) or a ramp (not).</summary>
        public void CableBoard(Course.Feature f, bool announce)
        {
            if (Airborne && announce) LandTrick(true);   // a trick come round as the board met the cable still counts
            Grinding = true; GrindFeature = f; Airborne = false; pipeAir = false; fixVy = 0f;
            ClearTrick(); Lean = 0f; edge = 0f; edgeVel = 0f;
            if (announce) { GrindBegin(true, true); GrindStart?.Invoke(f); }
            else grindScoring = false;   // (a launch ramp is only the way up)
        }
        /// <summary>Where the grind has got to this step: the line under the board, its speed, its height over the snow.</summary>
        public void CableRide(Course.Feature f, float speed, float above)
        {
            GrindFeature = f; Speed = speed; Carve = 0f; Skid = 0f; AboveGround = above;
            GrindTick(tickDt, speed * tickDt);   // paid by the metre, switch-ups and presses (RiderTricks)
            Vector3 d = f.Dir;
            Heading = Mathf.Clamp(Mathf.Atan2(d.x, d.z), -MaxHeading, MaxHeading);
        }
        /// <summary>Off the cable or the ramp's lip, into the air with this velocity.</summary>
        public Vector3 CableLeave(Vector3 p, Vector3 v) { Grinding = false; GrindEnd(); return TakeOff(p, v, 0.1f); }
        /// <summary>A pop asked for since the last step, taken (so it is not also a jump off the snow later).</summary>
        public bool TakePop(out bool super)
        {
            bool pop = popPending; popPending = false;
            super = pop && SuperJumpTime > 0f;
            if (pop) Jumped?.Invoke();
            return pop;
        }
        // (Score, for points from anywhere into the same feed as every trick, is in RiderTricks.cs)

        // ---------------------------------------------------------------- trees, rocks, animals

        void OnCollisionEnter(Collision c)
        {
            if (!Running || Crashed) return;
            var ob = c.collider.GetComponentInParent<Obstacle>();
            if (ob == null) return;
            // the face it met: the contacts' normals together (one facet's edge alone can point anywhere)
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < c.contactCount; i++) normal += c.GetContact(i).normal;
            if (normal.sqrMagnitude < 1e-8f) normal = -rb.linearVelocity;
            Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : Obstacle.Nearest(c.collider, rb.position);
            Hit(ob, c.collider, normal, at);   // (what the step's own look ahead did not see coming: RiderRideOver)
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
                if (ob != null && ob.Smashable && Smash(ob, near[i])) v *= SmashKeep;   // (the rest stays solid: see OnCollisionEnter)
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
                if (o == null || o.roof) continue;   // (a roof under the board is ridden, not missed)
                int id = o.GetInstanceID();
                if (id == lastMiss) continue;
                lastMiss = id;
                Emit("Close call", 50, LinkKind.CloseCall);
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
            crashTimer = 0.25f;   // a beat (a tap already under way isn't one), then the player taps to get up (RequestUp), even mid-tumble
            RequestUp = false;
            rb.detectCollisions = false;
            Carve = 0f; Skid = 0f; Lean = 0f; edge = 0f; edgeVel = 0f; Sliding = false; wasSliding = false;
            crashCause = why;
            ClearTrick(); SwitchOff(); grindScoring = false;   // (up again the normal way round)
            Crash?.Invoke(why);
        }

        Vector3 CrashStep(float dt, Vector3 p, Vector3 v)
        {
            crashTimer -= dt;
            var h = new Vector3(v.x, 0f, v.z) * Mathf.Exp(-dt * 3f);
            if (HasCrashFollow)
            {
                // stay with the thrown body, so the camera watches it and you get up where it lands: its own pace (from
                // where it was the step before, smoothed) and a pull back onto it, so a fast slide doesn't leave it behind
                if (followHave && dt > 1e-4f) { var fv = (CrashFollow - followPrev) / dt; fv.y = 0f; followVel = Vector3.Lerp(followVel, Vector3.ClampMagnitude(fv, 40f), 0.35f); }
                followPrev = CrashFollow; followHave = true;
                var d = CrashFollow - p; d.y = 0f;
                h = Vector3.ClampMagnitude(followVel + d * 8f, 45f);
            }
            else { followHave = false; followVel = Vector3.zero; }
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

        Vector3 followPrev, followVel; bool followHave;
        /// <summary>Where the thrown rider is, set by the ragdoll while a crash plays out.</summary>
        public Vector3 CrashFollow;
        public bool HasCrashFollow;
        /// <summary>Set while the fall isn't decided yet (still dropping off something high, or sinking into a
        /// crevasse): a tap now waits until it is, and is then a get-up or the end of the run.</summary>
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
            // step clear of whatever stopped you, toward the piste, onto snow you can ride away on: room to stand, clear
            // of anything solid, an open lane down the hill ahead, never up on a face too steep to stand on (where the
            // board only falls off again, hopping in the air on the spot) or over a crevasse's opening (RiderGetUp)
            p = GetUpSpot(p);
            p.y = Slope.DrawnHeight(p.x, p.z) + R;
            rb.position = p;
            DropFrom = p;
            groundVy = 0f; fixVy = 0f; groundKnown = false;
            return new Vector3(0f, 0f, 3f);
        }

        /// <summary>Somewhere to get up and ride away from: not too steep to stand on, and solid snow (no crevasse).</summary>
        static bool Rideable(float x, float z) => Slope.Normal(x, z).y > 0.75f && Course.CrevasseCut(x, z) <= 0f
            && !Course.CrevasseNear(x, z, 1.5f) && !Course.CrevasseNear(x, z + 5f, 1.5f) && !Course.CrevasseNear(x, z + 10f, 1.5f);   // (nor just above one's top)

    }
}
