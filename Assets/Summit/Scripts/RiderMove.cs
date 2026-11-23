using UnityEngine;

namespace Summit
{
    /// <summary>
    /// How the rider moves on the snow: the one movement model, all of it here.
    ///
    /// The board has two things, and both are read from its velocity each step (nothing else is remembered):
    ///   - its speed along the snow, which is gravity's (down the slope of the path it is on), the air's and the
    ///     snow's (drag, friction, what turning costs), never the finger's;
    ///   - its line across the hill (the way it travels, an angle off straight down the mountain), which is the
    ///     finger's alone: how far the thumb is dragged is how fast you go sideways, the line swings there in a few
    ///     hundredths of a second, and with the thumb lifted it is straight down the hill (or, where the run itself
    ///     swings across the hill, along the run: RunHeading).
    /// The board always points along its line: there is no slip, no grip to lose and no momentum carrying it the old
    /// way. Nothing but the finger turns it: the snow's tilt, bumps, rocks under the board and landings change the
    /// speed and the height, not the line. (The one exception is a board at a crawl, under FallLineSlow: "straight"
    /// is then the snow's own fall line and a stalled board is eased on, so it can never sit stuck in a hollow.)
    ///
    /// The snow it rides is the drawn snow averaged over the board's own length (SnowUnder), so the mesh's facets and
    /// the seams between its pieces are not felt, and anything low enough to ride over is a smooth rise in that
    /// surface (RiderRideOver), not a jump. A real lip, crest or drop still throws the board into the air: when the
    /// snow falls away faster than gravity can follow.
    ///
    /// In the air the same finger asks for the same sideways speed: in a hop too short to trick in the flight follows
    /// it as the board does on the snow (a bump never takes the steering away); in a real air it bends toward it at
    /// AirControl, as before. The body's lean, the edge, the trail and the spray are worked out from the motion
    /// afterwards (LookStep) and never feed back into it.
    /// </summary>
    public partial class Rider
    {
        // ---- the finger's line
        // A full drag is SideSpeed (m/s) across the hill, as the angle off straight that gives that at the speed you
        // are going (never more than SideAngle off it: slow, a full drag is a hard traverse). The line swings there
        // with the time constant SideResponse, by no more sideways pull than SideAccel (m/s^2) and no faster than
        // MaxYawRate: drag left and you go left, flip right and you go right, let go and you go straight down the hill
        public static float SideSpeed = 15f, SideAngle = 1.1f, SideResponse = 0.03f, SideAccel = 260f;
        public static float MinCarveSpeed = 7f, MaxYawRate = 12f;    // (slower than MinCarveSpeed the pull cap stops climbing; MaxYawRate caps the swing)
        // crawling (under FallLineSlow m/s, fading out by twice that), straight is the snow's own fall line, at most
        // FallLineMax off the mountain's, followed this slowly (s)
        public static float FallLineSlow = 5f, FallLineMax = 1.2f, FallLineLag = 0.35f;
        /// <summary>
        /// Where the run itself swings across the hill (a traverse along a cliff band), its heading off the fall line at
        /// z (rad, + to the right; 0 everywhere else). The terrain fills this in (one line where the mountain is set
        /// up: Rider.RunHeading = Slope.RunHeading). At speed that heading is "straight": a lifted thumb holds it, and
        /// the sideways speed a drag asks for is measured off it. (Crawling, straight is still the snow's own fall line.)
        /// </summary>
        public static System.Func<float, float> RunHeading = z => 0f;
        /// <summary>The run's heading is read this far ahead of the board (s of travel): as far as the line lags the
        /// finger, so on the way into a traverse the board swings with the run and not a moment behind it.</summary>
        public static float RunLead = 0.035f;
        /// <summary>What straight is for the steering now (rad off the mountain's fall line): the snow's own fall line at
        /// a crawl, the run's heading at speed.</summary>
        public float Straight { get; private set; }
        /// <summary>The run's own heading under the board, as much of it as counts at this speed (0 off a traverse): the
        /// camera looks along it.</summary>
        public float RunNow { get; private set; }

        /// <summary>Straight, for this step; returns how far the run's own heading swung since the last one.</summary>
        float SetStraight(float z, float vz, float sp)
        {
            float run = RunHeading(z + vz * RunLead) * Mathf.Clamp01((sp - FallLineSlow) / FallLineSlow), swung = run - RunNow;
            RunNow = run;
            Straight = FallLine + RunNow;
            return swung;
        }

        // the edge (the SHARP EDGES upgrade and each ride's Grip scale these): the most sideways pull it gives the
        // line (m/s^2), and how soon it bites (1/s: half of one over it is added to the swing's time constant)
        public static float GripLimit = 280f, Grip = 150f;
        public static float TurnDrag = 0.24f;                        // speed lost per radian the line is turned
        // what a turn costs, and how the body shows it, go by the swing of the line over this long (s), not step by
        // step: a thumb that trembles (or a bot that chatters) is not turning, and must not brake the board for it
        public static float YawEase = 0.06f;
        float yawSmooth;
        public static float AirResponse = 0.25f;                     // s: how soon a real air's flight bends toward the finger's line (never faster than AirControl)
        // ---- the snow under the board
        public static float BoardLength = 1.5f;                      // m of snow the board bridges: the surface it rides is averaged over it
        public static float SnowSink = 0.03f;                        // m: the most that averaging may put the board under the drawn snow (the crest of a lip)
        // ---- the look (never the motion)
        public static float LeanSpring = 30f;                        // how fast the body gets onto its edge (rad/s)
        public static float LeanAccel = 60f;                         // m/s^2 of sideways pull that puts the body fully over (the rest of the lean is the drag itself)

        // ---- the snow underfoot (Surfaces): what it is under the board, and what that does to the board's own numbers.
        //      It scales the snow's drag, and how soon the sideways speed the finger asks for is the board's to swing
        //      onto (SideStep); it never moves the line, and the line holds what it has as tightly on ice as on
        //      corduroy. Kept through an air (a hop steers as on the snow it left)
        /// <summary>The snow under the board (the last it rode, while it is in the air).</summary>
        public SnowMix Underfoot { get; private set; }
        /// <summary>How that snow rides (Surfaces.Feel).</summary>
        public SnowFeel SnowFeel => snowFeel;
        SnowFeel snowFeel = Summit.SnowFeel.Firm;
        float sideWant;   // the sideways speed the line is swinging onto now (m/s): the finger's, a moment behind it on snow with less grip

        /// <summary>The sideways speed the finger asks for now (m/s, + to the right of straight).</summary>
        float SideAsked() => Steer * SideSpeed * feel.TurnRate * Mathf.Clamp(TurnScale, 0.25f, 4f) * Mathf.Lerp(1f, 0.7f, Tuck);

        /// <summary>
        /// One step of the sideways speed the board is given to swing onto. On corduroy it is the finger's own, at once
        /// (the board's own quickness is all there is). Snow with less grip gives it later and no faster than its edge
        /// can pull: ice comes round in a quarter of a second, not a twelfth. That is all "less grip" is: what the board
        /// has already been given it holds exactly as on corduroy, whatever the snow's tilt or the run does under it.
        /// </summary>
        void SideStep(float dt, bool free)
        {
            float ask = SideAsked();
            if (free || (snowFeel.response <= 1.001f && snowFeel.pull >= 0.999f)) { sideWant = ask; return; }
            float scale = Mathf.Clamp(TurnScale, 0.25f, 4f), root = Mathf.Sqrt(scale);
            float edge = Mathf.Max(0.05f, feel.Grip) * Mathf.Max(1f, scale);
            float most = Mathf.Min(SideAccel * root * feel.TurnResponse, GripLimit * edge) * snowFeel.pull;
            float rate = (ask - sideWant) / Mathf.Max(SwingTime(1f) * (snowFeel.response - 1f), dt);
            sideWant += Mathf.Clamp(rate, -most, most) * dt;
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

        /// <summary>The line the finger asks for (rad off straight down the mountain: Straight, and the drag off it), going at `flat` over the ground
        /// (the speed across the map, not along the snow: the sideways speed a drag gives is the same on any pitch, and
        /// in the air).</summary>
        float WantLine(float sp)
        {
            float side = sideWant;
            float maxSin = Mathf.Sin(SideAngle);
            return Mathf.Clamp(Straight + Mathf.Asin(Mathf.Clamp(side / Mathf.Max(sp, 1f), -maxSin, maxSin)), -MaxHeading, MaxHeading);
        }

        /// <summary>The time constant of the line's swing onto the finger's (s), on snow that answers `response` times as slowly as corduroy.</summary>
        float SwingTime(float response)
        {
            float scale = Mathf.Clamp(TurnScale, 0.25f, 4f), root = Mathf.Sqrt(scale);
            float edge = Mathf.Max(0.05f, feel.Grip) * Mathf.Max(1f, scale);
            return (SideResponse / (feel.TurnResponse * root) + 0.5f / (Grip * edge)) * response;
        }

        /// <summary>One step of the line swinging onto the finger's, on the snow (or in a hop off it): the new line, and the rate it swung at.</summary>
        float SteerSnow(float dt, float line, float sp, float flat, out float yaw)
        {
            float scale = Mathf.Clamp(TurnScale, 0.25f, 4f), root = Mathf.Sqrt(scale);
            float edge = Mathf.Max(0.05f, feel.Grip) * Mathf.Max(1f, scale);
            float tau = SwingTime(1f);
            float pull = Mathf.Min(SideAccel * root * feel.TurnResponse, GripLimit * edge);
            float cap = Mathf.Min(MaxYawRate * root, pull / Mathf.Max(sp, MinCarveSpeed));
            yaw = Mathf.Clamp((WantLine(flat) - line) / Mathf.Max(tau, dt), -cap, cap);
            return Mathf.Clamp(line + yaw * dt, -MaxHeading, MaxHeading);
        }

        /// <summary>One step of a real air's flight bending toward the finger's line: no faster than AirControl.</summary>
        float SteerAir(float dt, float line, float sp)
        {
            float cap = AirControl * feel.AirControl;
            float yaw = Mathf.Clamp((WantLine(sp) - line) / Mathf.Max(AirResponse, dt), -cap, cap);
            return Mathf.Clamp(line + yaw * dt, -MaxHeading, MaxHeading);
        }

        Vector3 GroundStep(float dt, Vector3 p, Vector3 v)
        {
            // ---- where it is: which way the ground under the board faces (a roof's pitch, or the snow's)
            if (!Roofs.Under(p.x, p.z, p.y, out _, out Vector3 n, out var roofHere)) n = Slope.Normal(p.x, p.z);
            OnRock = !Tube && roofHere == null && RockFace.IsRock(n, p.x, p.z);
            Vector3 downS = Vector3.ProjectOnPlane(Vector3.down, n);
            float slopeS = downS.magnitude;   // (the sine of the ground's slope)

            // ---- what it is doing: its speed and its line, read from its velocity (without last step's nudge onto the
            //      snow, fixVy, which is not riding)
            Vector3 vIn = v - Vector3.up * fixVy;
            float sp = vIn.magnitude;
            float flat = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            float line = flat > 0.5f ? Mathf.Clamp(Mathf.Atan2(v.x, v.z), -MaxHeading, MaxHeading) : Heading;

            // ---- the snow underfoot here (a roof and bare rock are their own thing)
            if (roofHere == null && !OnRock) { Underfoot = Surfaces.Surface(p.x, p.z); snowFeel = Surfaces.Feel(Underfoot, sp); }
            else { Underfoot = default; snowFeel = Summit.SnowFeel.Firm; }
            SideStep(dt, false);

            // ---- 1. the line: the finger's. Straight is straight down the mountain; crawling (out of a landing in a
            //      hollow, on a valley's wall) it is the snow's own fall line under the board instead (smoothed; a
            //      face falling well back up the hill is no guide to it), so a board left to itself there runs
            //      downhill, not across the face. Never at speed
            {
                float local = 1f - Mathf.Clamp01((sp - FallLineSlow) / FallLineSlow);
                float fallNow = slopeS > 0.02f && downS.z > -0.5f * slopeS ? Mathf.Clamp(Mathf.Atan2(downS.x, downS.z), -FallLineMax, FallLineMax) : FallLine;
                FallLine = Mathf.Lerp(FallLine, fallNow * local, 1f - Mathf.Exp(-dt / FallLineLag));
                SetStraight(p.z, v.z, sp);
            }
            float yaw;
            if (Tube)
            {
                // a tube has no edge: leaning nudges its nose round, slowly, it always swings back downhill, and the
                // way it slides follows the nose a moment behind
                float nose = Heading;
                yaw = Steer * 0.75f - Mathf.Sin(nose - Straight) * 1.1f * Mathf.Clamp01(sp / 5f);
                nose = Mathf.Clamp(nose + yaw * dt, -MaxHeading, MaxHeading);
                line = Mathf.Lerp(line, nose, 1f - Mathf.Exp(-2.5f * dt));
                Heading = nose;
            }
            else
            {
                // (its speed over the ground on the pitch it is on now, not the one it came off: over a lip the two
                // differ, and the sideways speed a held drag gives must not)
                float gLine = -(n.x * Mathf.Sin(line) + n.z * Mathf.Cos(line)) / Mathf.Max(n.y, 0.05f);
                line = SteerSnow(dt, line, sp, sp / Mathf.Sqrt(1f + gLine * gLine), out yaw);
            }
            // stalled: swing back downhill, down this ground's own fall line (on a valley's wall or a ridge's flank that
            // is across the mountain, not down it), as near as the board's nose may point
            if (sp < 3f)
            {
                float fallHeading = slopeS > 0.07f ? Mathf.Clamp(Mathf.Atan2(downS.x, downS.z), -MaxHeading, MaxHeading) : 0f;
                line = Mathf.MoveTowards(line, fallHeading, dt * 1.4f * (1f - sp / 3f));
            }
            if (!Tube) Heading = line;
            yawSmooth = Mathf.Lerp(yawSmooth, yaw, 1f - Mathf.Exp(-dt / YawEase));
            yaw = yawSmooth;   // (from here on: the turn as it is felt, seen and paid for)
            YawRate = yaw;
            var d = new Vector2(Mathf.Sin(line), Mathf.Cos(line));

            // ---- 2. the snow along that line: the slope of the path, measured on the surface the board rides
            float g0 = -(n.x * d.x + n.z * d.y) / Mathf.Max(n.y, 0.05f);            // (the ground's own slope that way: where to look)
            float look = Mathf.Max(0.02f, sp * dt / Mathf.Sqrt(1f + g0 * g0));
            float snowHere = SnowUnder(p.x, p.z, p.y, d, out roofHere);
            float snowAhead = SnowUnder(p.x + d.x * look, p.z + d.y * look, p.y, d, out var roofNext);
            // (onto or off a roof the surface steps: that is an edge to fly off, below, not a slope to ride)
            bool edge = roofHere != roofNext;
            float g = edge ? g0 : (snowAhead - snowHere) / look;
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
            if (lip && sp > 7f && !edge && Undercut <= 0f && !popPending && n.y >= (Course.InPipe(p.x, p.z) ? 0.22f : 0.62f) && FallsAway(p, asCame))
            {
                Speed = sp;
                LookStep(dt, sp, yaw, true);
                return TakeOff(p, asCame, 0.08f);
            }

            // ---- 3. the speed: gravity down the path, the air, the snow, and what the turn cost
            sp -= G * sinP * dt;
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
                sp -= Mathf.Min(sp, drag * dt);
            }
            float turned = Mathf.Abs(yaw) * dt;
            if (!Tube && sp > 3f) sp = Mathf.Max(0f, sp - TurnDrag * turned * sp);
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

            // ---- 4. what stands in the way: something tall met square on is a crash, anything else is slid along
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
                        line = Mathf.Clamp(Mathf.Atan2(vt.x, vt.z), -MaxHeading, MaxHeading);
                        d = new Vector2(Mathf.Sin(line), Mathf.Cos(line));
                        if (!Tube) Heading = line;
                    }
                    vt = new Vector3(d.x * cosP, sinP, d.y * cosP) * sp;
                }
            }
            Speed = sp;

            // ---- the look of it (from the motion, never into it)
            LookStep(dt, sp, yaw, true);
            Skid = Mathf.Lerp(Skid, Tube ? Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(line * Mathf.Rad2Deg, Heading * Mathf.Rad2Deg)) * Mathf.Deg2Rad * 4f)
                                         : Mathf.Clamp01(Mathf.Abs(yaw) / Grip * 4f), 1f - Mathf.Exp(-dt * 10f));
            Carve = Mathf.Lerp(Carve, Mathf.Clamp01(Mathf.Abs(Lean) / MaxLean * 0.8f + Mathf.Abs(yaw) * sp / 20f) * Mathf.Clamp01(sp / 8f), 1f - Mathf.Exp(-dt * 12f));

            // ---- 5. follow the snow: where the board is next, and the vertical speed that puts it there. Anything
            //      low in the way lifts it, smoothly (overLift: the legs take it up and over; it is not a slope and
            //      not a jump). What is not riding in that vertical speed is kept apart (fixVy)
            float hd = sp * cosP * dt;
            float churn = Lift + Undercut * (0.3f + 0.08f * Mathf.Sin(churnT * 9f));   // lifted on the churning snow
            float liftWas = overLift;
            OverLiftStep(dt, p.x + d.x * hd, p.z + d.y * hd, d, sp, roofNext != null);
            float hereY = snowHere + liftWas + R + churn;
            float wantY = (edge ? snowAhead : snowHere + g * hd) + overLift + R + churn;
            float surfVy = edge ? (snowAhead - snowHere) / dt : vt.y;   // (the snow's own rise or fall under the path: what tells a lip from a slope)
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

        /// <summary>
        /// The look of the ride, from the motion: the body over onto its edge the way the finger drags (most of the
        /// lean is the drag itself, the rest the pull of the swing, so a quick move throws you over and a held one
        /// leaves you on the edge), and the chatter of a knock, a hard landing or rock under the board. Read by the
        /// body, the trail, the spray and the camera; nothing here goes back into the motion.
        /// </summary>
        void LookStep(float dt, float sp, float yaw, bool ground)
        {
            if (Tube && ground) { Lean = Mathf.Lerp(Lean, Steer * 0.35f, 1f - Mathf.Exp(-dt * 6f)); leanVel = 0f; return; }
            float root = Mathf.Sqrt(Mathf.Clamp(TurnScale, 0.25f, 4f));
            float pull = ground ? sp * yaw / LeanAccel : 0f;
            float target = MaxLean * Mathf.Clamp(0.6f * Steer + 0.4f * pull, -1f, 1f) * Mathf.Lerp(1f, 0.65f, Tuck);
            if (wobble > 0f) target += MaxLean * 0.3f * Mathf.Min(1f, wobble / 0.35f) * Mathf.Sin(churnT * 13f);
            // critically damped spring: starts at once, settles in a fifth of a second, never overshoots
            float spring = LeanSpring * feel.TurnResponse * root;
            float acc = spring * spring * (target - Lean) - 2f * spring * leanVel;
            leanVel += acc * dt;
            Lean += leanVel * dt;
        }
    }
}
