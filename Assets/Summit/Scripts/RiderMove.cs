using UnityEngine;

namespace Summit
{
    /// <summary>
    /// How the rider moves on the snow: the one movement model, all of it here. It is a carving model: forces on a
    /// board, nothing aimed at.
    ///
    /// The board has a velocity (the way it is going, and how fast), a heading (the way it points: Heading) and an
    /// edge angle (Lean). They are three things, and the angle between the first two is the slip.
    ///   - The finger sets the edge angle, and only that: how far the thumb is dragged (or the phone tilted) is how
    ///     far the board is tipped onto its edge, and it gets there in a tenth of a second (EdgeTime). The TURN
    ///     setting is how much edge the same drag gives.
    ///   - An edged board carves: its heading turns along an arc whose radius follows from the edge angle (more edge,
    ///     a tighter arc: CarveRadius at the least, the sidecut's) and from the speed (the faster, the wider the arc
    ///     the snow can hold: v^2 / r is the sideways pull the edge has to find, and it has only EdgeGrip gravities
    ///     of it). The first three quarters of the throw (EdgeKnee) are arcs the snow holds; the last quarter asks
    ///     for more than it can (SkidOver): that is a skid.
    ///   - The edge is what turns the velocity: it pushes across the board against any sideslip (stiffly: BiteRate),
    ///     up to its grip. Inside the grip the velocity follows the heading a few degrees behind and nothing is lost:
    ///     a carve. Past it the edge lets go (it holds less sliding than biting: SlideHold), the board swings on
    ///     across the way it is going, and its push now both turns the velocity and takes speed off it: a skid, the
    ///     way to scrub speed and to get round something a carve cannot.
    ///   - Gravity is real along the snow, both ways: down the path it is the speed (as ever), and across the path it
    ///     pulls the velocity round toward the snow's own fall line. A flat board has nothing to hold it against
    ///     that (FlatHold) and its nose swings to the way it slides (FlatAlign), so hands off it drifts, and turns,
    ///     down the path of least resistance: slowly (a second or three), never a snap. To hold a line across a
    ///     slope, hold an edge into the hill.
    ///   - The snow underfoot (Surfaces) sets the grip and how stiffly the edge bites: corduroy holds best, powder is
    ///     soft and washes out, ice lets go soonest.
    /// There is no line to hold and nothing is aimed at: no target heading, no target sideways speed, and the run's
    /// own heading (RunHeading) is the camera's to look along, never the board's.
    ///
    /// The snow it rides is the drawn snow averaged over the board's own length (SnowUnder), so the mesh's facets and
    /// the seams between its pieces are not felt, and anything low enough to ride over is a smooth rise in that
    /// surface (RiderRideOver), not a jump. A real lip, crest or drop still throws the board into the air: when the
    /// snow falls away faster than gravity can follow.
    ///
    /// In the air the flight is the flight's: gravity, and a little drift the finger's way (AirDrift). The finger
    /// still tips the board onto its edge and turns its nose a little ahead of the flight (AirTurn); it comes down
    /// with that edge and that slip, and the same model takes it from there: nothing is lined up for it.
    /// </summary>
    public partial class Rider
    {
        // ---- the edge: the finger's
        /// <summary>s for the edge to be nine tenths of the way to the angle asked for (a tenth of a second from the finger itself, with the input's own smoothing).</summary>
        public static float EdgeTime = 0.08f;
        /// <summary>m: the tightest arc a held edge carves (at EdgeKnee of the throw, slow enough for the snow to hold it): the sidecut's.</summary>
        public static float CarveRadius = 4.5f;
        /// <summary>The sideways pull a full edge holds on corduroy, in gravities (of G, on the level). The SHARP EDGES upgrade and each ride's Grip scale it.</summary>
        public static float EdgeGrip = 3.2f;
        /// <summary>The arc asked for at EdgeKnee, as a share of what the snow holds at that speed (under 1: the last of the carve is held on any pitch).</summary>
        public static float CarveShare = 0.85f;
        /// <summary>The share of the throw that is carve; past it the arc asked for climbs to SkidOver times what the snow holds.</summary>
        public static float EdgeKnee = 0.75f, SkidOver = 2.8f;
        /// <summary>The share of a full edge by which the edge is fully in the snow (under it, its hold and its bite fade to a flat board's).</summary>
        public static float EdgeBite = 0.25f;
        /// <summary>1/s: how stiffly a biting edge pushes back against sideslip; and a flat board's (it has almost none), and the share of the grip a flat board has.</summary>
        public static float BiteRate = 30f, FlatBite = 1.2f, FlatHold = 0.05f;
        /// <summary>A sliding edge holds this share of what a biting one does, having let go over SlideFall m/s of slip.</summary>
        public static float SlideHold = 0.8f, SlideFall = 2.5f;
        /// <summary>How fast the nose swings to the way the board is going (1/s): flat, edged, and (a number, on grip / speed) in a skid.</summary>
        public static float FlatAlign = 5f, EdgeAlign = 1f, SkidAlign = 1f;
        /// <summary>At a crawl the rider can step the board round (rad/s at full edge, fading out by PivotBelow m/s).</summary>
        public static float PivotRate = 1.2f, PivotBelow = 10f;
        /// <summary>The fastest the board turns (rad/s), the most it can be across the way it is going (rad), and the speed under which the arc stops tightening (m/s).</summary>
        public static float MaxYawRate = 6f, MaxSlip = 1.2f, MinCarveSpeed = 4f;
        /// <summary>
        /// THE PACE KNOB: gravity's pull along the snow, as a share of G (1: as it has always been). It is what sets how
        /// fast a run is: the speed the board runs out to goes with its square root (0.64 is four fifths of the
        /// speed, 0.36 three fifths). It does not touch the air: jumps fly and land as they do.
        /// </summary>
        public static float Pace = 1f;
        /// <summary>m/s^2 a full edge drifts the flight sideways in the air; how far ahead of the flight the nose is turned (rad) and how fast (rad/s).</summary>
        public static float AirDrift = 5f, AirTurnMost = 0.35f, AirTurnRate = 1.6f;

        // the snow's own fall line under the board is followed this slowly (s), and is never taken as more than
        // FallLineMax off the mountain's (a face falling back up the hill is no guide)
        public static float FallLineMax = 1.2f, FallLineLag = 0.35f;
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

        // ---- the snow underfoot (Surfaces): what it is under the board, and what that does to the edge's grip and
        //      bite and the snow's drag. Kept through an air
        /// <summary>The snow under the board (the last it rode, while it is in the air).</summary>
        public SnowMix Underfoot { get; private set; }
        /// <summary>How that snow rides (Surfaces.Feel).</summary>
        public SnowFeel SnowFeel => snowFeel;
        SnowFeel snowFeel = Summit.SnowFeel.Firm;

        float edge, edgeVel;   // the edge angle (rad, + right) and its rate: the physics' own (Lean is this, and the body's chatter on top)
        float airTurn;         // in the air: how far the nose is turned off the flight (rad)
        bool wasSliding;

        /// <summary>The slip: how far the board points off the way it is going (rad, + to the right).</summary>
        public float Slip { get; private set; }
        /// <summary>This step's pull across the way the board is going (m/s^2, + right): the edge's, and gravity's (the snow's own tilt). The test tools.</summary>
        public float SideEdge { get; private set; }
        public float SideSlope { get; private set; }
        /// <summary>The edge has let go: the board is skidding.</summary>
        public bool Sliding { get; private set; }
        /// <summary>The edge let go (how fast the board was going, m/s): the spray's burst.</summary>
        public System.Action<float> Skidded;

        /// <summary>The edge angle the finger asks for now (rad).</summary>
        float EdgeAsked() => Mathf.Clamp(Steer * Mathf.Clamp(TurnScale, 0.25f, 4f), -1f, 1f) * MaxLean * Mathf.Lerp(1f, 0.7f, Tuck);

        /// <summary>
        /// One step of the edge going to the finger's angle (a critically damped spring: it starts at once, is nine
        /// tenths there in EdgeTime and never overshoots), on the snow or in the air; and the body's chatter laid on
        /// it for the look (a knock, a hard landing, rock under the board), which the physics never see.
        /// </summary>
        void EdgeStep(float dt)
        {
            float spring = 3.9f / Mathf.Max(0.02f, EdgeTime) * feel.TurnResponse;
            float acc = spring * spring * (EdgeAsked() - edge) - 2f * spring * edgeVel;
            edgeVel += acc * dt;
            edge = Mathf.Clamp(edge + edgeVel * dt, -MaxLean, MaxLean);
            if (Tube && !Airborne) { Lean = Mathf.Lerp(Lean, Steer * 0.35f, 1f - Mathf.Exp(-dt * 6f)); return; }
            Lean = edge + (wobble > 0f ? MaxLean * 0.3f * Mathf.Min(1f, wobble / 0.35f) * Mathf.Sin(churnT * 13f) : 0f);
        }

        /// <summary>The arc asked for at this share of a full edge, in units of the arc the snow holds: up to 1 by EdgeKnee (as the tangent of the edge angle: a tilted sidecut), then on up to SkidOver.</summary>
        public static float ArcAsked(float share)
        {
            if (share <= EdgeKnee) return Mathf.Tan(share / EdgeKnee * MaxLean) / Mathf.Tan(MaxLean);
            return 1f + (SkidOver - 1f) * (share - EdgeKnee) / Mathf.Max(1e-3f, 1f - EdgeKnee);
        }

        /// <summary>The arc a ride's edge is asked for at `share` of full edge going at `sp`, on snow as steep as it is under the board now (1/m): the test tools, and the bots.</summary>
        public float ArcAt(float share, float sp)
        {
            float hold0 = EdgeGrip * G * Mathf.Max(0.05f, feel.Grip), vv = Mathf.Max(sp, MinCarveSpeed);
            float ny = rb != null ? Slope.Normal(rb.position.x, rb.position.z).y : 1f;
            return ArcAsked(Mathf.Clamp01(share)) * Mathf.Min(feel.TurnRate / CarveRadius, CarveShare * hold0 * ny / (vv * vv));
        }

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

            // ---- the snow underfoot here (a roof is its own thing; rock gives an edge little to bite)
            if (roofHere == null && !OnRock) { Underfoot = Surfaces.Surface(p.x, p.z); snowFeel = Surfaces.Feel(Underfoot, sp); }
            else { Underfoot = default; snowFeel = OnRock ? Summit.SnowFeel.Rock : Summit.SnowFeel.Firm; }

            // ---- 1. the edge: the finger's
            EdgeStep(dt);

            // the snow's own fall line here (smoothed): where a stalled board is turned, where a tube's nose swings, and
            // for the test tools. And the run's heading, for the camera
            {
                float fallNow = slopeS > 0.02f && downS.z > -0.5f * slopeS ? Mathf.Clamp(Mathf.Atan2(downS.x, downS.z), -FallLineMax, FallLineMax) : FallLine;
                FallLine = Mathf.Lerp(FallLine, fallNow, 1f - Mathf.Exp(-dt / FallLineLag));
                RunNow = RunHeading(p.z) * Mathf.Clamp01((sp - 5f) / 5f);
            }

            // ---- 2. the forces across the way it is going: gravity's (the snow's tilt across the path, as it lies on
            //      the map) and the edge's
            float sinL = Mathf.Sin(line), cosL = Mathf.Cos(line);
            float sideSlope = G * Pace * n.y * (n.x * cosL - n.z * sinL);
            float sideEdge = 0f, brake = 0f, yaw, slide = 0f;
            float across = Mathf.Max(flat, 2f);
            if (Tube)
            {
                // a tube has no edge: gravity has it, leaning nudges its nose round, slowly, the nose swings back
                // down the snow's own fall line, and the way it slides follows the nose a moment behind
                float nose = Heading;
                yaw = Steer * 0.75f - Mathf.Sin(nose - FallLine) * 1.1f * Mathf.Clamp01(sp / 5f);
                nose = Mathf.Clamp(nose + yaw * dt, -MaxHeading, MaxHeading);
                line = Mathf.Clamp(line + sideSlope / across * dt, -MaxHeading, MaxHeading);
                line = Mathf.Lerp(line, nose, 1f - Mathf.Exp(-2.5f * dt));
                Heading = nose;
                Slip = 0f;
            }
            else
            {
                float beta = Mathf.Clamp(Mathf.DeltaAngle(line * Mathf.Rad2Deg, Heading * Mathf.Rad2Deg) * Mathf.Deg2Rad, -MaxSlip, MaxSlip);
                float e = edge / MaxLean, share = Mathf.Min(1f, Mathf.Abs(e)), bite = Mathf.Clamp01(share / EdgeBite);
                // the arc the edge is bent to: by its angle, and no tighter than the snow could hold at this speed
                // (times what the throw asks for past the knee: more than it can)
                float hold0 = EdgeGrip * G * Mathf.Max(0.05f, feel.Grip);
                float vv = Mathf.Max(sp, MinCarveSpeed);
                float arc = Mathf.Sign(e) * ArcAsked(share) * Mathf.Min(feel.TurnRate / CarveRadius, CarveShare * hold0 * n.y / (vv * vv));
                // what the edge has to give here: by the snow, how hard the board is pressed on it, how far it is tipped
                float most = hold0 * n.y * snowFeel.grip * Mathf.Lerp(FlatHold, 1f, bite) * (1f - 0.6f * Mathf.Clamp01(Undercut));
                float stiff = Mathf.Min(Mathf.Lerp(FlatBite, BiteRate * snowFeel.bite, bite), 0.5f / dt);
                float sinB = Mathf.Sin(beta), cosB = Mathf.Cos(beta);
                float slip = Mathf.Abs(sp * sinB), peak = most / Mathf.Max(stiff, 1e-3f), push;
                if (slip <= peak) push = stiff * slip;
                else
                {
                    push = most * (SlideHold + (1f - SlideHold) * Mathf.Exp(-(slip - peak) / SlideFall));
                    slide = Mathf.Clamp01((slip - peak) / 3f);
                }
                // (it pushes square to the board, against the slip: part of it turns the velocity, part of it brakes)
                sideEdge = push * cosB * Mathf.Sign(sinB);
                brake = push * Mathf.Abs(sinB);
                // the board's own turn: along its arc, stepped round at a crawl, and its nose swung to the way it goes
                float align = Mathf.Lerp(FlatAlign, EdgeAlign, bite) + SkidAlign * hold0 * n.y / vv * slide;
                yaw = sp * cosB * arc + e * PivotRate * Mathf.Clamp01(1f - sp / PivotBelow) - align * sinB;
                yaw = Mathf.Clamp(yaw, -MaxYawRate, MaxYawRate);

                line = Mathf.Clamp(line + (sideEdge + sideSlope) / across * dt, -MaxHeading, MaxHeading);
                float nose = Mathf.Clamp(Heading + yaw * dt, -MaxHeading - MaxSlip, MaxHeading + MaxSlip);
                Heading = Mathf.Clamp(line + Mathf.Clamp(nose - line, -MaxSlip, MaxSlip), -MaxHeading, MaxHeading);
                Slip = Heading - line;
            }
            // stalled: swing back downhill, down this ground's own fall line (on a valley's wall or a ridge's flank that
            // is across the mountain, not down it), as near as the board's nose may point
            if (sp < 3f)
            {
                float fallHeading = slopeS > 0.07f ? Mathf.Clamp(Mathf.Atan2(downS.x, downS.z), -MaxHeading, MaxHeading) : 0f;
                float k = 1f - sp / 3f;
                line = Mathf.MoveTowards(line, fallHeading, dt * 1.4f * k);
                if (!Tube) { Heading = Mathf.MoveTowards(Heading, line, dt * 2.5f * k); Slip = Heading - line; }
            }
            SideEdge = sideEdge; SideSlope = sideSlope;
            Sliding = slide > 0f;
            // (announced once a slide, when it is plainly one: the first step past the grip is not yet a skid)
            if (!Sliding) wasSliding = false;
            else if (!wasSliding && slide > 0.15f) { wasSliding = true; if (sp > 6f) Skidded?.Invoke(sp); }
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

            // ---- 4. the speed: gravity down the path, the air, the snow, and what a skidding edge takes
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
                           + (Tube ? Friction * 1.3f * snowFeel.friction : Friction * snowFeel.friction + (OnRock ? RockFriction : 0f)) * G * n.y;
                sp -= Mathf.Min(sp, (drag + brake) * dt);
            }
            if (BoostTime > 0f) sp += BoostAccel * dt;
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
                        d = new Vector2(Mathf.Sin(line), Mathf.Cos(line));
                        Heading = Mathf.Clamp(Tube ? line : Heading + turned, -MaxHeading, MaxHeading);
                    }
                    vt = new Vector3(d.x * cosP, sinP, d.y * cosP) * sp;
                }
            }
            Speed = sp;

            // ---- the look of it (from the motion, never into it): how much the board is sliding sideways, and how hard its edge bites
            float sliding = Tube ? Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(line * Mathf.Rad2Deg, Heading * Mathf.Rad2Deg)) * Mathf.Deg2Rad * 4f)
                                 : Mathf.Max(slide, Mathf.Clamp01((Mathf.Abs(Slip) - 0.12f) / 0.5f)) * Mathf.Clamp01(sp / 6f);
            Skid = Mathf.Lerp(Skid, sliding, 1f - Mathf.Exp(-dt * 10f));
            float biting = Tube ? 0f : Mathf.Clamp01(Mathf.Abs(sideEdge) / (0.6f * EdgeGrip * G)) * (1f - 0.5f * slide);
            Carve = Mathf.Lerp(Carve, Mathf.Clamp01(Mathf.Abs(edge) / MaxLean * 0.5f + biting) * Mathf.Clamp01(sp / 8f), 1f - Mathf.Exp(-dt * 12f));

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
