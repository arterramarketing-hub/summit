using UnityEngine;

namespace Summit
{
    /// <summary>
    /// How the rider moves on the snow: the one movement model, all of it here (slide).
    ///
    /// The board has a velocity: how fast it is going, and which way. Both are read from it each step (nothing else is
    /// remembered), and three things act on it:
    ///   - Gravity, along the snow: down the path it is the speed (with the air's drag and the snow's), and across
    ///     the path it pulls the way the board goes round toward the snow's own fall line. The shape of the slope is
    ///     what decides how fast a run is; the finger never adds speed.
    ///   - The finger: a push sideways, across the fall line (feel). How far the thumb is dragged (or the phone
    ///     tilted), shaped (a small dead zone, then an expo curve: small drags ask for little), is the sideways speed
    ///     the rider asks the board for (a whole drag: SideShare of the speed down the hill). The edge builds it no
    ///     faster than its grip lets it (a whole drag's in RampTime on corduroy), so the board ramps across with
    ///     weight rather than jumping, and sets itself against an old slide harder (a reversal, ReverseBite). While
    ///     the finger is on, the edge also holds the board against gravity's pull across the path: a held drag is
    ///     never pulled back downhill. The TURN setting is how hard the same drag asks. The push turns the way the
    ///     board goes; it does not change how fast (sliding sideways costs a little speed, smoothly: SideDrag).
    ///   - Letting go: the board carries its slide (CoastTime, the edge's friction bleeding it a little), then eases
    ///     back to the fall line over a second and a half or so (ReturnIn, ReturnTime), the edge's hold against
    ///     gravity fading as it goes flat. Never a snap. Long after (EaseFor) the board is gravity's alone: a board
    ///     nobody is steering is turned by the hill only as gravity turns it, slowly.
    /// "The fall line" is the hill's own where the board is (FallAt): the way the rideable snow falls across the
    /// width of a run, not the spot under the board (a kicker, a roller, a ridge's flank or a band's face beside it
    /// do not turn it), followed a moment behind (FallLineLag). It is never the course's path: there is no line to
    /// hold and nothing is aimed at. Rider.RunHeading is the camera's.
    /// There is no grip to lose: nothing here lets go, washes out or takes the speed away in a lump.
    ///
    /// The board and the body stay mostly facing down the fall line: Heading (what is drawn) follows the way the
    /// board goes only part of the way and never more than YawCap off the fall line, and the lean carries the rest:
    /// the rider leans into the slide. Heading, Lean, Skid and Carve are worked out from the motion and never feed
    /// back into it.
    ///
    /// The snow it rides is the drawn snow averaged over the board's own length (SnowUnder), so the mesh's facets and
    /// the seams between its pieces are not felt, and anything low enough to ride over is a smooth rise in that
    /// surface (RiderRideOver), not a jump. A real lip, crest or drop still throws the board into the air: when the
    /// snow falls away faster than gravity can follow.
    ///
    /// In the air the flight is mostly the flight's: gravity, and a limited steer the finger's way, straight from the
    /// finger (AirDrift, never more than AirSideMost m/s over the air), to line the landing up. The board is tipped and
    /// its nose turned a little by the finger.
    /// </summary>
    public partial class Rider
    {
        // ---- the finger's push
        /// <summary>A whole drag's sideways speed, as a share of the speed the board is going (so the faster, the more
        /// there is to dodge with), never under SideLeast nor over SideCap m/s, and never further round than SideAngle
        /// off the fall line (rad).</summary>
        public static float SideShare = 0.6f, SideLeast = 9f, SideCap = 28f, SideAngle = 1f;
        /// <summary>(steerB) Speed-aware: a whole drag asks SideShareFast more of the speed at FastAt m/s and over (eased in
        /// from SlowAt), so the hardest turn the finger can ask for stays about 45 deg off the fall line at 108 km/h
        /// rather than closing to 37.</summary>
        public static float SideShareFast = 0.11f, SlowAt = 12f, FastAt = 30f;
        /// <summary>
        /// The finger's throw, shaped (feel): no push at all inside DeadZone of the middle, then an expo curve (the
        /// throw past the dead zone to the power InputExpo), so small drags ask for little and the whole drag still
        /// asks for the whole push.
        /// </summary>
        public static float DeadZone = 0.04f, InputExpo = 1.05f;   // (steerB: the one curve, half the thumb's throw asks 45%)
        /// <summary>s: how long the edge takes to build a whole drag's sideways speed from none (on corduroy, for a
        /// snowboard): the sideways acceleration it can give is that speed over this long, so the board ramps across
        /// rather than jumping. SettleTime: the last of it eases in, not with a step.</summary>
        public static float RampTime = 0.3f, SettleTime = 0.08f;
        /// <summary>m/s^2: the least sideways acceleration the edge gives on corduroy, however slow the board (dodging a tree at a tree-weaving pace).</summary>
        public static float EdgeLeast = 40f;
        /// <summary>How much harder the edge bites set against a slide the other way (a reversal) than building one.</summary>
        public static float ReverseBite = 1.6f;
        /// <summary>Asked for less with the finger still on: the edge bleeds the slide this much more gently than it builds it.</summary>
        public static float CheckBite = 0.18f;
        /// <summary>
        /// Let go: the board carries its slide (CoastTime s, only the edge's friction bleeding it: CoastBleed m/s^2 on
        /// corduroy), then the return to the fall line comes on over ReturnIn s and dies away with the time constant
        /// ReturnTime, but only as a light nudge (feel2): the board comes round mostly by gravity on the slope as the
        /// edge goes flat, three or four seconds to be going down an average pitch again, and on flat ground hardly at
        /// all. (CoastBleed is per second: the share of the slide the edge's friction takes while it coasts.) All the while the edge holds
        /// the board against gravity's pull across the path, less and less (HoldFade s after the coast). EaseFor s
        /// after the finger came off the return fades out (over EaseFade s): a board left to itself longer than that is
        /// gravity's alone, and the hill turns it only as gravity does.
        /// </summary>
        public static float CoastTime = 0.3f, CoastBleed = 0.1f, ReturnIn = 0.8f, ReturnTime = 3f, HoldFade = 1.2f, EaseFor = 6f, EaseFade = 1f;
        /// <summary>m/s^2: the most the push and the snow's resistance together ever pull the board sideways.</summary>
        public static float SideAccel = 600f;
        /// <summary>What sliding sideways costs: m/s^2 off the speed for each m/s of sideways speed. Small and smooth.</summary>
        public static float SideDrag = 0.03f;
        /// <summary>The SHARP EDGES upgrade: how much quicker the board answers the push (1: stock).</summary>
        public static float SideGrip = 1f;
        /// <summary>The look: how much of the way the board goes its nose follows, the most it is ever off the fall line (rad), and how fast it gets there (1/s).</summary>
        public static float YawShare = 0.9f, YawCap = 0.87f, YawQuick = 12f;   // (steerB: 0.9 of it, up to 50 deg)
        public static float LeanSpring = 30f;                        // how fast the board gets onto its edge (rad/s)
        /// <summary>The body's lean into the slide: degrees per degree of the pull's own lean (the angle that holds a rider up
        /// against it), degrees at a whole drag's slide, the most, and its spring (rad/s: settles in about a quarter second).</summary>
        public static float BodyLeanPull = 0.9f, BodyLeanSlide = 14f, BodyLeanMost = 34f, BodySpring = 16f;
        public static float MaxSlip = 1.2f;                          // the most the nose is ever off the way the board goes (rad: the look, and through an air)
        /// <summary>
        /// THE PACE KNOB: gravity's pull along the snow, as a share of G (1: as it always was; 1.15 since slide). It is what sets how
        /// fast a run is: the speed the board runs out to goes with its square root (0.64 is four fifths of the
        /// speed, 0.36 three fifths). It does not touch the air: jumps fly and land as they do.
        /// </summary>
        public static float Pace = 1.15f;
        /// <summary>In the air (aircontrol): m/s^2 a whole drag swings the flight sideways, straight from the finger (about a
        /// quarter of what the edge gives on the snow); the most sideways speed (m/s) the finger may add or take over one
        /// air, so the landing can be lined up, not moved somewhere else; how far ahead of the flight the nose is turned
        /// (rad) and how fast (rad/s).</summary>
        public static float AirDrift = 10f, AirSideMost = 4f, AirTurnMost = 0.35f, AirTurnRate = 4f;

        // the hill's own fall line where the board is: taken at five places across, out to FallReach m either side,
        // each over FallBase m, ground steeper than FallGentle (rise over run) counting for little; followed this
        // slowly (s), and never taken as more than FallLineMax off the mountain's
        public static float FallReach = 12f, FallBase = 3f, FallGentle = 0.5f, FallLineMax = 1.2f, FallLineLag = 0.35f;
        /// <summary>
        /// Where the run itself swings across the hill (a traverse along a cliff band), its heading off the fall line at
        /// z (rad, + to the right; 0 everywhere else). The terrain fills this in (Slope.Init). The camera looks along it
        /// and the grooming's lines follow it; the board's physics never read it.
        /// </summary>
        public static System.Func<float, float> RunHeading = z => 0f;
        /// <summary>The run's own heading under the board, as much of it as counts at this speed (0 off a traverse): for the camera.</summary>
        public float RunNow { get; private set; }

        // what a turn shows goes by the swing of the board over this long (s), not step by step
        public static float YawEase = 0.06f;
        float yawSmooth;
        // ---- the snow under the board
        public static float BoardLength = 1.5f;                      // m of snow the board bridges: the surface it rides is averaged over it
        public static float SnowSink = 0.03f;                        // m: the most that averaging may put the board under the drawn snow (the crest of a lip)

        // ---- the snow underfoot (Surfaces): what it is under the board, and what that does to how soon the board
        //      answers the push, how long it slides on, and the snow's drag. Kept through an air
        /// <summary>The snow under the board (the last it rode, while it is in the air).</summary>
        public SnowMix Underfoot { get; private set; }
        /// <summary>How that snow rides (Surfaces.Feel).</summary>
        public SnowFeel SnowFeel => snowFeel;
        SnowFeel snowFeel = Summit.SnowFeel.Firm;

        float edge, edgeVel;   // the lean (rad, + right) and its rate: the look's own (Lean is this, and the body's chatter on top)
        float airTurn;         // in the air: how far the nose is turned off the flight (rad)
        float airSide;         // in the air: the sideways speed the finger has given the flight so far (m/s, + right; AirSideMost)
        float sideNow;         // the sideways speed over what a whole drag would give, -1..1: what the lean shows
        float sideFrom;        // (steerB) the sideways speed the board had when the finger started this push
        float pushPeak;        // the most of a whole drag this push has asked for yet
        bool pushing;          // the finger is pushing (out of the dead zone)
        float easeFor;         // s since the finger came off: the coast, then the return
        float deepIn;          // 0..1: how far the board has sunk into deep powder
        float latSmooth, biteNow, bodyLean, bodyLeanVel, crouchNow, crouchVel;   // the look's own: lateral pull, bite, the body's lean and crouch
        bool wasSliding;

        /// <summary>How far the board's nose is off the way it is going (rad, + to the right): the look.</summary>
        public float Slip { get; private set; }
        /// <summary>The sideways speed across the fall line (m/s, + right).</summary>
        public float Side { get; private set; }
        /// <summary>This step's pull across the way the board is going (m/s^2, + right): the finger's push with the snow's
        /// resistance to the slide (SideEdge), and gravity's (the snow's own tilt: SideSlope). The test tools.</summary>
        public float SideEdge { get; private set; }
        public float SideSlope { get; private set; }
        /// <summary>The board is being thrown across from one way to the other (the look: the spray's burst).</summary>
        public bool Sliding { get; private set; }
        public System.Action<float> Skidded;

        /// <summary>The sideways speed a whole drag gives at this speed (m/s), for the ride it is.</summary>
        public float SideFull(float sp)
        {
            float share = SideShare + SideShareFast * Mathf.SmoothStep(0f, 1f, (sp - SlowAt) / Mathf.Max(1f, FastAt - SlowAt));
            float full = Mathf.Clamp(share * sp, SideLeast, SideCap) * feel.TurnRate;
            return Mathf.Min(full, Mathf.Sin(SideAngle) * Mathf.Max(sp, 0f));
        }

        /// <summary>The share of a whole drag the finger asks for now (-1..1): the TURN setting is how much the same drag
        /// asks, the throw shaped (Shape).</summary>
        float Asked() => Shape(Steer * Mathf.Clamp(TurnScale, 0.25f, 4f)) * Mathf.Lerp(1f, 0.7f, Tuck);

        /// <summary>
        /// In the air (aircontrol): the share of the whole air steer the finger asks for, -1..1. In proportion to the throw
        /// (no expo: the air is short, what is asked is had at once). In an air long enough to trick, a throw past FlickAt
        /// is a spin (RiderTricks), so there the whole air steer comes just short of it (AirReach of FlickAt): a drag
        /// steers, a flick spins.
        /// </summary>
        float AirAsked()
        {
            float th = Steer * Mathf.Clamp(TurnScale, 0.25f, 4f), a = Mathf.Abs(th);
            if (a <= DeadZone) return 0f;
            float reach = trickAir ? FlickAt * AirReach : 1f;
            return Mathf.Sign(th) * Mathf.Clamp01((a - DeadZone) / Mathf.Max(0.05f, reach - DeadZone));
        }
        /// <summary>In an air long enough to trick: the whole air steer at this share of the flick's throw (FlickAt).</summary>
        public static float AirReach = 0.9f;

        float pushSign;
        /// <summary>(steerB) The sideways speed across the fall line a push asking `asked` of a whole drag (`full`) wants:
        /// its own share of the whole, and, pushed the way the board already slid when the push began, that slide kept
        /// under it (all of it at the most the push has asked, fading as the throw is eased back).</summary>
        float WantFor(float asked, float full)
        {
            float w = asked * full;
            if (asked * sideFrom > 0f && pushPeak > 1e-4f)
                w += (1f - Mathf.Abs(asked)) * sideFrom * Mathf.Clamp01(Mathf.Abs(asked) / pushPeak)
                     * Mathf.SmoothStep(0f, 1f, Mathf.Abs(asked) / Mathf.Max(0.01f, HoldReach));
            return w;
        }
        /// <summary>(bandfix2) The push's hold comes in with the throw: none just out of the dead zone, all of it from this
        /// share of a whole drag. A thumb resting a hair off the middle asks for almost nothing, as it always did, rather
        /// than locking the board onto the slide it had (a traverse held along a cliff band's edge); a real push the way
        /// the board slides still keeps that slide under it.</summary>
        public static float HoldReach = 0.25f;
        /// <summary>For the test tools' bots: the sideways speed across the fall line a throw of `steer` would ask for
        /// now (as if the finger went there this step), the input's shaping and the push's hold included.</summary>
        public float SideWant(float steer)
        {
            float a = Shape(steer * Mathf.Clamp(TurnScale, 0.25f, 4f)) * Mathf.Lerp(1f, 0.7f, Tuck), full = SideFull(Speed);
            if (Mathf.Abs(a) < 0.005f) return 0f;
            float f = sideFrom, pk = pushPeak;
            bool fresh = !pushing || a * pushSign < 0f;
            float was = sideFrom, wasPk = pushPeak;
            sideFrom = fresh ? (pushing ? 0f : Side) : f; pushPeak = Mathf.Max(fresh ? 0f : pk, Mathf.Abs(a));
            float w = WantFor(a, full);
            sideFrom = was; pushPeak = wasPk;
            return w;
        }

        /// <summary>A throw (-1..1) to the share of a whole push it asks for: the dead zone, then the expo curve.</summary>
        public static float Shape(float throw_)
        {
            float a = Mathf.Abs(Mathf.Clamp(throw_, -1f, 1f));
            if (a <= DeadZone) return 0f;
            return Mathf.Sign(throw_) * Mathf.Pow((a - DeadZone) / (1f - DeadZone), InputExpo);
        }
        /// <summary>Shape undone: the throw that asks for this share of a whole push (the test tools' bots).</summary>
        public static float Unshape(float share)
        {
            float a = Mathf.Abs(Mathf.Clamp(share, -1f, 1f));
            if (a <= 1e-5f) return 0f;
            return Mathf.Sign(share) * (DeadZone + (1f - DeadZone) * Mathf.Pow(a, 1f / InputExpo));
        }

        /// <summary>The sideways pull across the way the board goes, smoothed (m/s^2, + right): what the body leans
        /// into, the camera kicks with and the spray and the edge's sound build on.</summary>
        public float LateralAccel => latSmooth;
        /// <summary>0..1: how hard the edge is biting now (the sideways pull against what the edge can give).</summary>
        public float Bite => biteNow;
        /// <summary>deg: how far the body leans into the slide (+ to the right), on a spring.</summary>
        public float BodyLean => bodyLean;
        /// <summary>0..1: how far the knees bend into a hard push, on a spring.</summary>
        public float Crouch => crouchNow;
        /// <summary>m: the most the board sinks into deep powder (the look only).</summary>
        public static float DeepSink = 0.12f;
        /// <summary>m: how far the board sinks into deep snow (the look).</summary>
        public float Sink { get; private set; }

        /// <summary>
        /// The look of the ride: the body over onto its edge the way the finger pushes and the way the board is
        /// sliding (a critically damped spring: it starts at once, settles in a fifth of a second, never overshoots),
        /// with the chatter of a knock, a hard landing or rock under the board laid on it. Nothing here goes back
        /// into the motion.
        /// </summary>
        void EdgeStep(float dt)
        {
            float target = MaxLean * Mathf.Clamp(0.55f * Asked() + (Airborne ? 0f : 0.45f * sideNow), -1f, 1f);
            float spring = LeanSpring * feel.TurnResponse;
            float acc = spring * spring * (target - edge) - 2f * spring * edgeVel;
            edgeVel += acc * dt;
            edge = Mathf.Clamp(edge + edgeVel * dt, -MaxLean, MaxLean);
            // the body: into the slide by its pull (the lean that holds a rider up against it) and by the slide
            // itself, on a softer spring than the board's edge so it rolls in rather than snapping; the knees bend
            // with how hard the edge is pushing
            float g = Mathf.Atan2(latSmooth, 9.81f) * Mathf.Rad2Deg;
            float leanTo = Airborne || Crashed ? 0f : Mathf.Clamp(BodyLeanPull * g + BodyLeanSlide * sideNow, -BodyLeanMost, BodyLeanMost);
            float bs = BodySpring;
            bodyLeanVel += (bs * bs * (leanTo - bodyLean) - 2f * bs * bodyLeanVel) * dt;
            bodyLean += bodyLeanVel * dt;
            float crouchTo = Airborne || Crashed ? 0f : Mathf.Clamp01(0.75f * biteNow + 0.35f * Mathf.Abs(sideNow));
            crouchVel += (bs * bs * (crouchTo - crouchNow) - 2f * bs * crouchVel) * dt;
            crouchNow = Mathf.Clamp01(crouchNow + crouchVel * dt);
            if (Tube && !Airborne) { Lean = Mathf.Lerp(Lean, Steer * 0.35f, 1f - Mathf.Exp(-dt * 6f)); return; }
            Lean = edge + (wobble > 0f ? MaxLean * 0.3f * Mathf.Min(1f, wobble / 0.35f) * Mathf.Sin(churnT * 13f) : 0f);
        }

        /// <summary>
        /// The hill's own fall line at (x, z), as a heading: which way the ground falls at five places across the hill
        /// (FallReach either side), each over FallBase m, the gentler ground counting for far more. So a kicker or a
        /// roller under the board does not turn it, and beside something steep (a ridge's flank, a cliff band's face,
        /// a bank) it is still the way the rideable snow runs: down a ridge's crest, along a band's bench. `was`
        /// where it is as good as level or falls back up the hill.
        /// </summary>
        static float FallAt(float x, float z, float was)
        {
            float b = FallBase, sx = 0f, sz = 0f;
            for (int i = -2; i <= 2; i++)
            {
                float px = x + i * 0.5f * FallReach;
                float gx = Slope.Height(px - b, z) - Slope.Height(px + b, z), gz = Slope.Height(px, z - b) - Slope.Height(px, z + b);   // (the drop each way)
                float drop = Mathf.Sqrt(gx * gx + gz * gz);
                if (drop < 0.06f * b) continue;
                float steep = drop / (2f * b) / FallGentle;
                float w = (i == 0 ? 2f : 1f) / (1f + steep * steep * steep * steep);
                sx += w * gx / drop; sz += w * gz / drop;
            }
            float m = Mathf.Sqrt(sx * sx + sz * sz);
            if (m < 1e-3f || sz < -0.5f * m) return was;
            return Mathf.Clamp(Mathf.Atan2(sx, sz), -FallLineMax, FallLineMax);
        }
        float fallSeen; int fallClock;   // the hill's fall line as last looked at (every fourth step: it is followed slowly anyway)

        bool groundKnown;   // groundVy is last step's: the snow's own vertical speed under the board then
        /// <summary>The snow's surface slope along the path this step: its rise per metre gone (the test tools).</summary>
        public float PathSlope { get; private set; }

        /// <summary>
        /// The snow the board rides at (x, z), going along `d`: the drawn snow averaged over the board's length along
        /// its line (a 1.5 m board bridges the mesh's facets and seams; it does not drop into each crease), never more
        /// than SnowSink under the drawn snow itself. A roof is ridden as it is.
        /// </summary>
        float SnowUnder(float x, float z, float refY, Vector2 d, out Roofs.Roof roof)
        {
            if (Roofs.Under(x, z, refY, out float y, out _, out roof)) return y;
            float a = BoardLength * 0.25f, b = BoardLength * 0.5f;
            float ax = d.x * a, az = d.y * a, bx = d.x * b, bz = d.y * b;
            float here = Slope.DrawnHeight(x, z);
            float mean = 0.25f * (here + Slope.DrawnHeight(x - ax, z - az) + Slope.DrawnHeight(x + ax, z + az))
                       + 0.125f * (Slope.DrawnHeight(x - bx, z - bz) + Slope.DrawnHeight(x + bx, z + bz));
            return Mathf.Max(mean, here - SnowSink);
        }

        Vector3 GroundStep(float dt, Vector3 p, Vector3 v)
        {
            // ---- where it is: which way the ground under the board faces (a roof's pitch, or the snow's)
            if (!Roofs.Under(p.x, p.z, p.y, out _, out Vector3 n, out var roofHere)) n = Slope.Normal(p.x, p.z);
            OnRock = !Tube && roofHere == null && RockFace.IsRock(n, p.x, p.z);
            Vector3 downS = Vector3.ProjectOnPlane(Vector3.down, n);
            float slopeS = downS.magnitude;   // (the sine of the ground's slope)

            // ---- what it is doing: its speed and the way it is going, read from its velocity (without last step's
            //      nudge onto the snow, fixVy, which is not riding)
            Vector3 vIn = v - Vector3.up * fixVy;
            float sp = vIn.magnitude;
            float flat = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            float line = flat > 0.5f ? Mathf.Clamp(Mathf.Atan2(v.x, v.z), -MaxHeading, MaxHeading) : Heading;

            // ---- the snow underfoot here (a roof is its own thing; rock answers the push late)
            if (roofHere == null && !OnRock) { Underfoot = Surfaces.Surface(p.x, p.z); snowFeel = Surfaces.Feel(Underfoot, sp); }
            else { Underfoot = default; snowFeel = OnRock ? Summit.SnowFeel.Rock : Summit.SnowFeel.Firm; }
            // (deep snow: the board sinks into it over a moment, DeepSinkIn, and comes up out of it quicker: its drag
            // goes with how far it has sunk. And it sinks a little in the look too)
            {
                float deepHere = Underfoot.Deep;
                deepIn = Mathf.Lerp(deepIn, deepHere, 1f - Mathf.Exp(-dt / (deepHere > deepIn ? Surfaces.DeepSinkIn : Surfaces.DeepSinkOut)));
            }
            Sink = Tube ? 0f : DeepSink * deepIn;

            // ---- 1. the snow's own fall line here (the hill's, a moment behind): what "sideways" is across, where a
            //      board left to itself eases back to, where a tube's nose swings. And the run's heading, for the camera
            {
                if (roofHere != null) fallSeen = slopeS > 0.02f && downS.z > -0.5f * slopeS ? Mathf.Clamp(Mathf.Atan2(downS.x, downS.z), -FallLineMax, FallLineMax) : FallLine;
                else if (fallClock++ % 4 == 0) fallSeen = FallAt(p.x, p.z, FallLine);
                FallLine = Mathf.Lerp(FallLine, fallSeen, 1f - Mathf.Exp(-dt / FallLineLag));
                RunNow = RunHeading(p.z) * Mathf.Clamp01((sp - 5f) / 5f);
            }

            // ---- 2. what turns the way it is going: gravity across the path (the snow's tilt, as it lies on the map),
            //      and the finger's push across the fall line against the snow's resistance to the slide
            float sinL = Mathf.Sin(line), cosL = Mathf.Cos(line);
            float sideSlope = G * Pace * n.y * (n.x * cosL - n.z * sinL);
            float sideEdge = 0f, brake = 0f, yaw, thrown = 0f;
            float across = Mathf.Max(flat, 2f);
            float headWas = Heading;
            if (Tube)
            {
                line = Mathf.Clamp(line + sideSlope / across * dt, -MaxHeading, MaxHeading);
                // a tube has no edge: gravity has it, leaning nudges its nose round, slowly, the nose swings back
                // down the snow's own fall line, and the way it slides follows the nose a moment behind
                float nose = Heading;
                yaw = Steer * 0.75f - Mathf.Sin(nose - FallLine) * 1.1f * Mathf.Clamp01(sp / 5f);
                nose = Mathf.Clamp(nose + yaw * dt, -MaxHeading, MaxHeading);
                line = Mathf.Lerp(line, nose, 1f - Mathf.Exp(-2.5f * dt));
                Heading = nose;
                Slip = 0f; Side = flat * Mathf.Sin(line - FallLine); sideNow = 0f;
            }
            else
            {
                float off = line - FallLine, cosO = Mathf.Cos(off);
                float side = flat * Mathf.Sin(off);                       // the sideways speed it has, across the fall line
                float full = SideFull(sp), asked = Asked();
                bool released = Mathf.Abs(asked) < 0.005f;
                // (steerB) the push counts from where the board was when it began: a push the way the board already
                // slides (riding the run's line across a side-hill, a carve tightened) asks for that slide and more,
                // never less, so a drag never turns the board against itself. Pushed the other way it asks for its
                // own sideways speed, as before, so a reversal is as sharp
                // (eased back from the most this push asked, it sheds that slide as it always did: the hold fades with
                // the throw, to none at the middle; and a push barely out of the dead zone holds next to none: HoldReach)
                if (!released && (!pushing || asked * pushSign < 0f)) { sideFrom = pushing ? 0f : side; pushPeak = 0f; }   // (a new push; flipped straight across, a reversal)
                pushing = !released;
                if (pushing) { pushSign = Mathf.Sign(asked); pushPeak = Mathf.Max(pushPeak, Mathf.Abs(asked)); }
                float want = WantFor(asked, full);   // and the one the finger pushes for
                // the edge builds the slide no faster than its grip allows (a whole drag in RampTime on corduroy: softer
                // snow and ice a little slower), sets itself against an old slide the other way harder (ReverseBite),
                // and with the finger still on bleeds a slide it no longer asks for a little more gently (CheckBite).
                // Let go, the board carries its slide: only the edge's friction bleeds it (CoastBleed) for CoastTime,
                // then the return to the fall line comes on (ReturnIn) and dies away (ReturnTime). Nothing lets go
                float grip = Mathf.Max(0.2f, snowFeel.grip), bite = Mathf.Max(0.2f, snowFeel.bite);
                float rideGrip = Mathf.Max(0.2f, feel.Grip);
                float firm = Mathf.Sqrt(grip * bite) * feel.TurnResponse * Mathf.Max(0.5f, SideGrip) / (1f + 2f * Mathf.Clamp01(Undercut));
                float edgeMost = Mathf.Max(Mathf.Max(full, SideLeast) / RampTime, EdgeLeast) * firm;
                float pull, hold;
                if (!released)
                {
                    easeFor = 0f;
                    // (bleeding a slide it no longer asks for: by the board's own ramp, never the slow-speed floor)
                    float limit = want * side < 0f ? edgeMost * ReverseBite : Mathf.Abs(want) >= Mathf.Abs(side) ? edgeMost : Mathf.Max(full, SideLeast) / RampTime * firm * CheckBite;
                    pull = Mathf.Clamp((want - side) / SettleTime, -limit, limit);
                    hold = 1f;
                }
                else
                {
                    easeFor += dt;
                    float bleed = Mathf.Max(0.6f, CoastBleed * Mathf.Abs(side)) * Mathf.Sqrt(grip * rideGrip);
                    float coast = -Mathf.Sign(side) * Mathf.Min(bleed, Mathf.Abs(side) / dt);
                    float k = Mathf.SmoothStep(0f, 1f, (easeFor - CoastTime) / ReturnIn);
                    float back = -side / (ReturnTime / Mathf.Sqrt(grip * rideGrip)) * k;
                    pull = Mathf.Abs(back) > Mathf.Abs(coast) ? back : coast;
                    // (and long let go: the return fades and the board is gravity's alone)
                    pull *= 1f - Mathf.SmoothStep(0f, 1f, (easeFor - EaseFor) / Mathf.Max(EaseFade, dt));
                    hold = 1f - Mathf.SmoothStep(0f, 1f, (easeFor - CoastTime) / HoldFade);
                }
                if (feel.Vehicle) pull += VehiclePull(dt, asked, sp);   // (a paddle stroke: RiderVehicle)
                pull = Mathf.Clamp(pull, -SideAccel, SideAccel);
                // (the edge holds the board against gravity's pull across the path as long as it is on its edge: the
                // finger on it, or the slide it carries; flat, gravity turns it)
                float gravity = sideSlope * (1f - hold);
                sideSlope = gravity;
                line = Mathf.Clamp(line + gravity / across * dt, -MaxHeading, MaxHeading);
                off = line - FallLine; cosO = Mathf.Cos(off);
                // (it turns the way the board goes and leaves how fast alone: across the fall line by `pull`, which is
                // across the path by pull over the cosine of how far round it already is)
                float turned = pull * dt / (across * Mathf.Max(cosO, 0.35f));
                // (never pushed further round than SideAngle off the fall line; a board already past it only eases back)
                float most = SideAngle, to = off + turned;
                if (Mathf.Abs(to) > most) to = Mathf.Abs(off) > most ? (Mathf.Abs(to) < Mathf.Abs(off) ? to : off) : Mathf.Sign(to) * most;
                float lineWas = line;
                line = Mathf.Clamp(FallLine + to, -MaxHeading, MaxHeading);
                sideEdge = (line - lineWas) * across / dt;
                Side = flat * Mathf.Sin(line - FallLine);
                sideNow = full > 0.5f ? Mathf.Clamp(Side / full, -1f, 1f) : 0f;
                brake = SideDrag * Mathf.Abs(Side);
                thrown = !released && want * Side < 0f && full > 0.5f ? Mathf.Abs(want - Side) / full : 0f;   // (set against a slide the other way: the look)
                // (the look: the pull across the path, and how hard that is for the edge, built fast and let go slower)
                latSmooth = Mathf.Lerp(latSmooth, sideEdge, 1f - Mathf.Exp(-dt / 0.05f));
                float biteTo = Mathf.Clamp01(Mathf.Abs(latSmooth) / Mathf.Max(1f, edgeMost));
                biteNow = Mathf.Lerp(biteNow, biteTo, 1f - Mathf.Exp(-dt / (biteTo > biteNow ? 0.06f : 0.25f)));
                // the look: the nose follows the way it goes part of the way, never far off the fall line
                float noseTo = Mathf.Clamp(FallLine + Mathf.Clamp(YawShare * (line - FallLine), -YawCap, YawCap), -MaxHeading, MaxHeading);
                Heading = Mathf.Lerp(Heading, noseTo, 1f - Mathf.Exp(-dt * YawQuick));
                Heading = Mathf.Clamp(line + Mathf.Clamp(Heading - line, -MaxSlip, MaxSlip), -MaxHeading, MaxHeading);
                Slip = Heading - line;
            }
            // the body onto its edge (the look)
            EdgeStep(dt);
            // stalled: swing back downhill, down this ground's own fall line (on a valley's wall or a ridge's flank that
            // is across the mountain, not down it), as near as the board's nose may point
            if (sp < 3f)
            {
                float fallHeading = slopeS > 0.07f ? Mathf.Clamp(Mathf.Atan2(downS.x, downS.z), -MaxHeading, MaxHeading) : 0f;
                float k = 1f - sp / 3f;
                line = Mathf.MoveTowards(line, fallHeading, dt * 1.4f * k);
                if (!Tube) { Heading = Mathf.MoveTowards(Heading, line, dt * 2.5f * k); Slip = Heading - line; }
            }
            yaw = (Heading - headWas) / dt;
            SideEdge = sideEdge; SideSlope = sideSlope;
            // (thrown across from one way to the other: a burst of spray, once a throw. Only the look)
            Sliding = thrown > 0.9f && sp > 6f;
            if (!Sliding) wasSliding = false;
            else if (!wasSliding) { wasSliding = true; Skidded?.Invoke(sp); }
            yawSmooth = Mathf.Lerp(yawSmooth, yaw, 1f - Mathf.Exp(-dt / YawEase));
            YawRate = yawSmooth;
            var d = new Vector2(Mathf.Sin(line), Mathf.Cos(line));

            // ---- 3. the snow along the way it is going: the slope of the path, measured on the surface the board rides
            float g0 = -(n.x * d.x + n.z * d.y) / Mathf.Max(n.y, 0.05f);            // (the ground's own slope that way: where to look)
            float look = Mathf.Max(0.02f, sp * dt / Mathf.Sqrt(1f + g0 * g0));
            float snowHere = SnowUnder(p.x, p.z, p.y, d, out roofHere);
            float snowAhead = SnowUnder(p.x + d.x * look, p.z + d.y * look, p.y, d, out var roofNext);
            // (onto or off a roof the surface steps: that is an edge to fly off, below, not a slope to ride)
            bool roofEdge = roofHere != roofNext;
            float g = roofEdge ? g0 : (snowAhead - snowHere) / look;
            PathSlope = g;
            float cosP = 1f / Mathf.Sqrt(1f + g * g), sinP = g * cosP;

            // ---- the snow falls away faster than gravity can follow (a kicker's lip, a crest, the edge of a drop): fly.
            //      Decided here, before the board is laid onto the new slope: it leaves as it came, with the speed over
            //      the ground it had and the vertical speed the snow had just before it fell away. (Laid onto the
            //      falling snow first, it was turned down the drop and left a quarter of its speed on the lip)
            // (set down this step, by a new run, a getting up or the test tools: there is no "before" to fall away from)
            if (!groundKnown) { groundVy = sp * sinP; groundKnown = true; }
            bool lip = sp * sinP < groundVy - G * dt * 1.2f;
            // (never more than 2 m/s off the ground's own line here: where the snow tips over a long crest rather than
            // breaks at a lip, the board has tipped with it, and it only leaves if the snow still falls away from that)
            float tipped = sp * g0 / Mathf.Sqrt(1f + g0 * g0);
            Vector3 asCame = new Vector3(d.x * flat, Mathf.Clamp(groundVy, tipped - 2f, tipped + 2f), d.y * flat);
            if (lip && sp > 7f && !roofEdge && Undercut <= 0f && !popPending && n.y >= (Course.InPipe(p.x, p.z) ? 0.22f : 0.62f) && FallsAway(p, asCame))
            {
                Speed = sp;
                return TakeOff(p, asCame, 0.08f);
            }

            // ---- 4. the speed: gravity down the path, the air, the snow, and the little a sideways slide costs
            sp -= G * Pace * sinP * dt;
            if (sp < 0f) sp = 0f;   // (run out up a rise: it stops, and the stalled board's rules turn it downhill)
            if (sp > 1e-3f)
            {
                // momentum builds while you hold speed, and drains when you scrub it away
                if (sp > 20f) Flow = Mathf.Min(1f, Flow + dt / 14f);
                else if (sp < 12f) Flow = Mathf.Max(0f, Flow - dt / 3f);
                // (momentum carries you up to about 170 km/h; past that the air takes over again, so it never runs away)
                float flowK = Mathf.Lerp(1f, FlowDrag, Flow * Mathf.Clamp01((52f - sp) / 8f));
                // (the snow underfoot: its own friction, and what the board has to plough through)
                float drag = (Tube ? DragOpen * 1.15f : Mathf.Lerp(DragOpen, DragTuck, Tuck)) * feel.Drag * (BoostTime > 0f ? 0.6f : 1f) * flowK * sp * sp
                           + snowFeel.drag * sp * sp
                           + Mathf.Min(snowFeel.deep * sp * sp, Surfaces.DeepMost * G) * deepIn
                           + (Tube ? Friction * 1.3f * snowFeel.friction : Friction * snowFeel.friction + (OnRock ? RockFriction : 0f)) * G * n.y;
                sp -= Mathf.Min(sp, (drag + brake) * dt);
            }
            if (BoostTime > 0f) sp += BoostAccel * dt;
            if (feel.Vehicle) sp = VehicleDrive(dt, sp, sinP, slopeS);   // (the paddle, the engine, the bear's legs: RiderVehicle)
            if (sp < 3f)
            {
                // a stalled board is eased on along its line, never up the ground's own slope (on anything steeper
                // than a degree: the push is three times gravity's pull on a 4-degree slope)
                bool uphill = slopeS > 0.02f && sinP / slopeS > 0.05f;
                if (!uphill) sp += 6f * dt;
            }
            // (tracked snow, crust and ice chatter under the board: the look and the sound only)
            if (snowFeel.chatter > 0.05f && sp > 6f && !Tube) wobble = Mathf.Max(wobble, 0.13f * snowFeel.chatter);
            if (OnRock && sp > 3f)
            {
                wobble = Mathf.Max(wobble, 0.3f);   // (the body chatters: the look only)
                rockScrapeT -= dt;
                if (rockScrapeT <= 0f) { rockScrapeT = 0.18f; Scraped?.Invoke(Mathf.Clamp(sp * 0.25f, 1f, 8f)); }
            }

            // ---- 5. what stands in the way: something tall met square on is a crash, anything else is slid along
            Vector3 vt = new Vector3(d.x * cosP, sinP, d.y * cosP) * sp;
            if (sp > 0.2f)
            {
                Vector3 was = vt;
                if (Contact(dt, p, ref vt)) return vt;
                if (vt != was)
                {
                    float h = Mathf.Sqrt(vt.x * vt.x + vt.z * vt.z), h0 = Mathf.Sqrt(was.x * was.x + was.z * was.z);
                    sp *= h0 > 1e-4f ? Mathf.Min(1f, h / h0) : 0f;
                    if (h > 0.05f)
                    {
                        // (slid along its face: the board is turned with the way it goes, by as much, so the face
                        // leaves no slip behind for the edge to kick against)
                        float turned = Mathf.Clamp(Mathf.Atan2(vt.x, vt.z), -MaxHeading, MaxHeading) - line;
                        line += turned;
                        if (Mathf.Abs(turned) > 0.02f) easeFor = Mathf.Min(easeFor, 0f);   // (turned off its way: the board eases back from here, as if just let go)
                        d = new Vector2(Mathf.Sin(line), Mathf.Cos(line));
                        Heading = Mathf.Clamp(Tube ? line : Heading + turned, -MaxHeading, MaxHeading);
                    }
                    vt = new Vector3(d.x * cosP, sinP, d.y * cosP) * sp;
                }
            }
            Speed = sp;

            // ---- the look of it (from the motion, never into it): how much the board is being thrown sideways, and how hard it is over on its edge
            float sliding = Tube ? Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(line * Mathf.Rad2Deg, Heading * Mathf.Rad2Deg)) * Mathf.Deg2Rad * 4f)
                                 : Mathf.Clamp01((thrown - 0.5f) / 0.9f) * Mathf.Clamp01(sp / 6f);
            Skid = Mathf.Lerp(Skid, sliding, 1f - Mathf.Exp(-dt * 10f));
            Carve = Mathf.Lerp(Carve, Tube ? 0f : Mathf.Clamp01(Mathf.Abs(edge) / MaxLean) * Mathf.Clamp01(sp / 8f), 1f - Mathf.Exp(-dt * 12f));

            // ---- 6. follow the snow: where the board is next, and the vertical speed that puts it there. Anything
            //      low in the way lifts it, smoothly (overLift: the legs take it up and over; it is not a slope and
            //      not a jump). What is not riding in that vertical speed is kept apart (fixVy)
            float hd = sp * cosP * dt;
            float churn = Lift + Undercut * (0.3f + 0.08f * Mathf.Sin(churnT * 9f));   // lifted on the churning snow
            float liftWas = overLift;
            OverLiftStep(dt, p.x + d.x * hd, p.z + d.y * hd, d, sp, roofNext != null);
            float hereY = snowHere + liftWas + R + churn;
            float wantY = (roofEdge ? snowAhead : snowHere + g * hd) + overLift + R + churn;
            float surfVy = roofEdge ? (snowAhead - snowHere) / dt : vt.y;   // (the snow's own rise or fall under the path: what tells a lip from a slope)
            float vy = (wantY - p.y) / dt;
            float fix = vy - vt.y;
            AboveGround = 0f;

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
                var leave = TakeOff(p, (lip && sp > 7f ? asCame : vt) + popDir * pop, 0.15f);   // (off a lip: as it came, not laid down the drop)
                superAir = super;
                return leave;
            }
            // off a roof's edge (the eave, the gable's end), or onto a roof lower down: into the air, as off any lip
            if ((roofHere != null || roofNext != null) && wantY < hereY - 0.4f) { RoofStep(null); return TakeOff(p, vt, 0.08f); }
            // (or already past the edge: down on its very edge, the step carried the board off it)
            if (roofHere == null && roofUnder != null && p.y > hereY + 0.4f) { RoofStep(null); return TakeOff(p, vIn, 0.08f); }
            // a face too steep to ride: you are falling, not riding (pipe walls are ridden almost to vertical)
            bool inPipe = Course.InPipe(p.x, p.z);
            float steep = inPipe ? 0.22f : 0.62f;
            if (n.y < steep)
            {
                // off the edge of a drop: the face falls away from the way you are going, so you leave it on your own
                // line (laid onto that face, the speed into the air would be taken away, or turned back up the hill)
                // (the fall counts from where you last stood on rideable snow, not from each touch on the way down)
                if (!inPipe && Vector3.Dot(vIn, n) >= 0f) return TakeOff(p, vIn, 0.05f, false);
                // running into a wall that rises in front must not turn the speed into a launch straight up, so the
                // climb is capped at the ground's own rate
                if (!inPipe) vt.y = Mathf.Min(vt.y, Mathf.Max(groundVy, 0f) + 1.5f);
                return TakeOff(p, inPipe ? PipeAir(p, vt) : vt, 0.05f, inPipe);
            }
            // riding up a pipe wall: leave it with the wall's own vertical speed
            if (inPipe && vt.y > 1f && surfVy < groundVy - G * dt * 2f)
                return TakeOff(p, PipeAir(p, vt), 0.08f);

            groundVy = surfVy; fixVy = fix;
            DropFrom = p;
            RoofStep(roofHere);
            return new Vector3(vt.x, vy, vt.z);
        }
    }
}
