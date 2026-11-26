using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The tricks the player performs, and what they are worth (the rest of the rider is Rider.cs).
    ///
    /// In the air: left/right spins (half a turn at a time), up/down flips (a whole one at a time), taps step a
    /// grab up, and the rider has to come out of it before the snow. Holding a direction only ever starts a turn
    /// that will come round in time; pushing it again commits one more, fit or not. With no input an air is just
    /// an air. On a box, a rail or a lift cable: points by the metre for as long as it is held, a flick to change
    /// stance (a switch-up, a press), a tap to pop off.
    ///
    /// Everything that scores leaves through Emit: the Link event (the game's combo) and the older Trick event
    /// (the day's missions, the test tools).
    /// </summary>
    public partial class Rider
    {
        // ---- tuning (static so the test harness can sweep them)
        public static float SpinRate = 9.5f;       // rad/s: half a turn in a third of a second
        public static float FlipRate = 7.2f;       // rad/s: a flip in 0.87 s
        public static float TrickHurry = 1.35f;    // how much faster a turn may go to come round before a landing that is coming sooner
        public static float FlickAt = 0.6f, FlickOff = 0.35f;   // the stick's throw that counts as a direction given, and where it lets go again
        public static float MinTrickAir = 0.55f;   // s of air predicted at takeoff before trick input counts (a bump is not a jump)
        public static float CleanSpin = 25f, CleanFlip = 30f;   // deg off the line / off upright a landing may be and still be clean
        public static float GrabMin = 0.38f;       // s a tapped grab is held for
        public const int MaxGrabLevel = 6;
        public const float RailPerMetre = 25f;
        public const int BoxOn = 100, RailOn = 150, SwitchUp = 100;
        static readonly int[] GrabPoints = { 0, 100, 220, 400, 550, 750, 900 };
        const float GrabPerSecond = 120f, GrabHeldMax = 2f;
        static readonly string[] TubeGrabs = { "Starfish", "Hands Up" };
        static readonly int[] TubeGrabBonus = { 0, 50 };
        /// <summary>s a grab is held before it is tweaked, and again before it goes into the next grab (a grab to grab).</summary>
        public static float GrabStepHold = 0.7f;

        // ---- the trick inputs (set from RiderInput each step; the test tools set them themselves)
        /// <summary>Left/right in the air or on a rail, -1..1 (the steering stick).</summary>
        public float TrickX;
        /// <summary>Up/down, -1..1: up (forward) is a frontflip or a nose press, down a backflip or a tail press.</summary>
        public float TrickY;
        /// <summary>The grab finger is down.</summary>
        public bool GrabHeld;
        int tapQueue;
        /// <summary>A tap of the grab finger (queued for the next physics step).</summary>
        public void TrickTap() { tapQueue++; }

        // ---- the air trick in hand
        bool trickAir;                     // this air is long enough to trick
        float spinNow, flipNow;            // rad turned so far (signed: + right / frontflip)
        int spinHalves, flipTurns, spinDir;   // committed: half turns and whole flips (signed), and the way the spin goes
        bool xOn, yOn;
        int grabLevel, grabFirst;
        float grabClock, grabPose, grabTweak, grabHeldFor, grabStepClock;
        float landIn, trickLook;
        string liveName, wholeName; int liveSig = -1, wholeSig = -1, livePoints;

        /// <summary>Something has been asked for this air (a spin, a flip, a grab).</summary>
        public bool HasTrick => trickAir && (spinHalves != 0 || flipTurns != 0 || grabLevel > 0);
        /// <summary>The trick in hand, as committed (null with none): what it will be called if it is landed clean.</summary>
        public string TrickName
        {
            get
            {
                if (!HasTrick) return null;
                int sig = Sig(spinHalves, flipTurns, grabLevel);
                if (sig != wholeSig) { wholeSig = sig; wholeName = NameFor(spinHalves, flipTurns, grabLevel) ?? ""; }
                return wholeName;
            }
        }
        /// <summary>The spin and flip committed (rad).</summary>
        public float TrickSpin => spinHalves * Mathf.PI;
        public float TrickFlip => flipTurns * Mathf.PI * 2f;
        /// <summary>Which grab is in hand (1.. into the ride's grabs), 0 with none.</summary>
        public int TrickGrab => grabLevel <= 0 ? 0 : 1 + (grabFirst + (grabLevel - 1) / 2) % Grabs.Length;
        /// <summary>The air time expected (s), looked at again as the flight goes.</summary>
        public float TrickTime { get; private set; }
        /// <summary>How far round the committed rotation is, 0..1 (1 with none).</summary>
        public float TrickProgress
        {
            get
            {
                float all = Mathf.Abs(TrickSpin) + Mathf.Abs(TrickFlip);
                return all < 0.01f ? 1f : Mathf.Clamp01((Mathf.Abs(spinNow) + Mathf.Abs(flipNow)) / all);
            }
        }
        /// <summary>Everything committed has come round.</summary>
        public bool TrickDone => Mathf.Abs(TrickSpin - spinNow) < 0.02f && Mathf.Abs(TrickFlip - flipNow) < 0.02f;
        /// <summary>The rotation as it stands (rad): what the body is drawn with.</summary>
        public float SpinNow => spinNow;
        public float FlipNow => flipNow;
        /// <summary>0..1: the hand is on the board; and how far the grab is tweaked.</summary>
        public float GrabPose => grabPose;
        public float GrabTweak => grabTweak;
        /// <summary>
        /// What a landing left the board short of its line (rad), swinging out over the next few tenths: a sketchy
        /// landing comes round late. (A half turn more or less is not left over: that is riding switch.)
        /// </summary>
        public float LandSpin { get; private set; }

        /// <summary>
        /// Riding switch: the board (or the skis) going tail first, the rider turned round on it, since a landing that
        /// came round an odd number of half turns (a 180, a 540, one landed half way round). It is only how the rider
        /// stands: the board goes where it goes as ever, and steering is the same on the screen. The next air that
        /// comes round an odd number of half turns brings them back; so does slowing to a stop (a revert on the snow),
        /// a fall, or a new run. Snowboard and skis: a tube and a toboggan have no way round to ride.
        /// </summary>
        public bool Switch { get; private set; }
        bool takeoffSwitch;   // (this air began switch: its trick is a switch one)
        bool CanSwitch => !Tube && (Ride == RideKind.Snowboard || Ride == RideKind.Skis);

        /// <summary>Back to riding the normal way (a fall, a new run): no swing, it is just so.</summary>
        void SwitchOff() { Switch = false; takeoffSwitch = false; }

        /// <summary>
        /// The air's turn as it ended: the whole half turns it came round change which way the rider stands (an odd
        /// number of them flips it), and what is left over swings out on the snow.
        /// </summary>
        float LandStance()
        {
            float rem = Mathf.DeltaAngle(0f, spinNow * Mathf.Rad2Deg);
            if (!CanSwitch) return rem * Mathf.Deg2Rad;
            if (Mathf.Abs(rem) > 90f) { Switch = !Switch; rem -= Mathf.Sign(rem) * 180f; }
            return rem * Mathf.Deg2Rad;
        }
        public float LandFlip { get; private set; }

        string[] Grabs => Tube ? TubeGrabs : feel.Grabs;
        int MaxGrab => Mathf.Min(MaxGrabLevel, 2 * Grabs.Length);

        // ---- what scored
        /// <summary>A link for the combo: every trick, grind, close call and air (name, points, kind).</summary>
        public System.Action<TrickLink> Link;

        void Emit(string name, int points, LinkKind kind, bool sketchy = false, bool counts = true)
        {
            Link?.Invoke(new TrickLink { name = name, points = points, kind = kind, sketchy = sketchy });
            if (counts) Trick?.Invoke(name, points);
        }

        /// <summary>
        /// Points for something done, from anywhere, into the same feed as every trick: a chair hopped on a cable,
        /// a treetop brushed (LinkKind.Brush), whatever comes next.
        /// </summary>
        public void Score(string name, int points, LinkKind kind = LinkKind.Bonus) => Emit(name, points, kind);

        /// <summary>The link in progress, for the HUD: the stance being held on a rail, the trick as built so far in the air.</summary>
        public string LiveName => Crashed ? null : GrindLive ? segName : Airborne && trickAir ? liveName : null;
        public int LivePoints => GrindLive ? Mathf.RoundToInt(segPoints) : livePoints;
        public LinkKind LiveKind => GrindLive ? LinkKind.Grind : LinkKind.Trick;

        // ---------------------------------------------------------------- each step

        float tickDt = 1f / 120f;

        /// <summary>The trick inputs for this step, and the landing's leftover swinging out.</summary>
        void TrickInput(float dt)
        {
            tickDt = dt;
            if (input != null) { TrickX = input.Steer; TrickY = input.Flip; GrabHeld = input.GrabHold; }
            if (LandSpin != 0f) { LandSpin *= Mathf.Exp(-dt * 9f); if (Mathf.Abs(LandSpin) < 0.01f) LandSpin = 0f; }
            // a revert: riding switch and come almost to a stop, the board swings round under you to the normal way
            if (Switch && Running && !Airborne && !Grinding && !Crashed && Speed < 1.5f) { Switch = false; LandSpin += Mathf.PI; }
            if (LandFlip != 0f) { LandFlip *= Mathf.Exp(-dt * 14f); if (Mathf.Abs(LandFlip) < 0.01f) LandFlip = 0f; }
            if (!Airborne && !Grinding) tapQueue = 0;
        }

        void ClearTrick()
        {
            TrickAlias = null; TrickTime = 0f; trickAir = false;
            spinNow = flipNow = 0f; spinHalves = flipTurns = spinDir = 0; xOn = yOn = false;
            grabLevel = 0; grabClock = grabPose = grabTweak = grabHeldFor = grabStepClock = 0f;
            landIn = 0f; trickLook = 0f; liveName = null; liveSig = -1; livePoints = 0; tapQueue = 0;
            takeoffSwitch = false;
        }

        /// <summary>How long until a flight from p at v comes down on the snow (up to max seconds).</summary>
        static float AirLeft(Vector3 p, Vector3 v, float max)
        {
            float T = 0f; const float h = 1f / 30f;
            Vector3 q = p, u = v;
            for (int i = 0; i < 180 && T < max; i++)
            {
                u.y -= G * h; q += u * h; T += h;
                if (q.y <= Slope.DrawnHeight(q.x, q.z) + R && Course.Solid(q.x, q.z, new Vector2(u.x, u.z).magnitude)) break;
            }
            return T;
        }

        /// <summary>At takeoff: is this an air to trick in? (Nothing is chosen for the player any more.)</summary>
        void PlanTrick(Vector3 p, Vector3 v)
        {
            ClearTrick();
            float T = 0f; const float h = 1f / 30f;
            Vector3 q = p, u = v;
            for (int i = 0; i < 180; i++)
            {
                u.y -= G * h; q += u * h; T += h;
                if (i > 2 && q.y <= Slope.DrawnHeight(q.x, q.z) + R) break;
            }
            if (T < MinTrickAir) return;
            trickAir = true; TrickTime = T; landIn = T;
            takeoffSwitch = Switch;
            // which grab comes first: from where you took off (on this mountain), so the same jump gives the same grab
            int pick = (int)(Slope.Hash01(Mathf.RoundToInt(p.x * 8f), Mathf.RoundToInt(p.z * 8f), 97) * 4000f);
            grabFirst = pick % Grabs.Length;
            // a direction already held as you leave the snow is a hold (it spins only as far as will fit), not a flick
            xOn = Mathf.Abs(TrickX) >= FlickAt; yOn = Mathf.Abs(TrickY) >= FlickAt;
        }

        bool Fits(float rad, float rate) => rad / rate + 0.13f <= landIn;

        float Turn(float now, float target, float rate, float dt)
        {
            float left = Mathf.Abs(target - now);
            if (left < 1e-4f) return target;
            // the snow is coming sooner than the turn will be done: hurry (as far as TrickHurry allows)
            float need = left / Mathf.Max(landIn - 0.03f, 0.02f);
            float w = need > rate ? Mathf.Min(need, rate * TrickHurry) : Mathf.Min(rate, Mathf.Max(rate * 0.4f, left * 14f));   // (else ease into the stop)
            return Mathf.MoveTowards(now, target, w * dt);
        }

        /// <summary>One step of the air trick: the inputs taken, the rotation turned.</summary>
        void TrickStep(float dt, Vector3 p, Vector3 v)
        {
            if (!trickAir) { tapQueue = 0; return; }
            trickLook -= dt; landIn = Mathf.Max(0f, landIn - dt);
            if (trickLook <= 0f)
            {
                trickLook = 0.05f;
                landIn = AirLeft(p, v, 6f);
                TrickTime = AirTime + landIn;
            }

            // ---- taps: the grab, stepped up
            while (tapQueue > 0)
            {
                tapQueue--;
                if (grabLevel < MaxGrab) grabLevel++;
                grabClock = GrabMin; grabStepClock = 0f;
            }
            // held on: the grab is tweaked, then goes on into the next one (the same steps a tap takes)
            if (grabLevel > 0 && GrabHeld && grabPose > 0.6f)
            {
                grabStepClock += dt;
                if (grabStepClock >= GrabStepHold && grabLevel < MaxGrab) { grabLevel++; grabStepClock = 0f; }
            }
            else grabStepClock = 0f;
            grabClock -= dt;
            bool grabbing = grabLevel > 0 && (GrabHeld || grabClock > 0f);
            grabPose = Mathf.MoveTowards(grabPose, grabbing ? 1f : 0f, dt * 8f);
            grabTweak = Mathf.MoveTowards(grabTweak, grabbing && grabLevel % 2 == 0 ? 1f : 0f, dt * 7f);
            if (grabPose > 0.6f) grabHeldFor += dt;

            // ---- left / right: the spin
            float spinRate = SpinRate * (Tube ? 1f : feel.Spin), flipRate = FlipRate * feel.Flip;
            float ax = Mathf.Abs(TrickX);
            bool on = xOn ? ax > FlickOff : ax >= FlickAt;
            int dir = TrickX > 0f ? 1 : -1;
            bool mine = on && (spinDir == 0 || dir == spinDir);   // (once spinning, the other way is only steering)
            if (mine)
            {
                float left = Mathf.Abs(spinHalves * Mathf.PI - spinNow);
                if (spinHalves == 0)
                {
                    // the first half turn: only if it can come round (in a short air this is just steering)
                    if (AirTime > 0.06f && Fits(Mathf.PI, spinRate * TrickHurry)) { spinDir = dir; spinHalves = dir; }
                }
                else if (!xOn) spinHalves += spinDir;                                         // pushed again: one more, come what may
                else if (left < 0.7f && Fits(left + Mathf.PI, spinRate)) spinHalves += spinDir;   // held: another if it fits
            }
            xOn = on;

            // ---- up / down: flips (on a tube, a roll over with it: the Bail Roll)
            if (Tube) flipRate = FlipRate * 0.8f;
            {
                float ay = Mathf.Abs(TrickY);
                bool yon = yOn ? ay > FlickOff : ay >= FlickAt;
                int ydir = TrickY > 0f ? 1 : -1;
                bool ymine = yon && (flipTurns == 0 || ydir == (flipTurns > 0 ? 1 : -1));
                if (ymine)
                {
                    float left = Mathf.Abs(flipTurns * Mathf.PI * 2f - flipNow);
                    if (!yOn) flipTurns += ydir;                                                               // pushed: a whole flip, fit or not
                    else if (flipTurns == 0) { if (AirTime > 0.06f && Fits(Mathf.PI * 2f, flipRate)) flipTurns = ydir; }   // held from the snow
                    else if (left < 0.7f && Fits(left + Mathf.PI * 2f, flipRate)) flipTurns += ydir;           // held: another if it fits
                }
                yOn = yon;
            }

            spinNow = Turn(spinNow, spinHalves * Mathf.PI, spinRate, dt);
            flipNow = Turn(flipNow, flipTurns * Mathf.PI * 2f, flipRate, dt);

            // ---- the trick as built so far (what the HUD ticks up)
            int doneHalves = Mathf.FloorToInt(Mathf.Abs(spinNow) / Mathf.PI + 0.08f) * (spinNow < 0f ? -1 : 1);
            int doneFlips = Mathf.FloorToInt(Mathf.Abs(flipNow) / (Mathf.PI * 2f) + 0.06f) * (flipNow < 0f ? -1 : 1);
            int sig = Sig(doneHalves, doneFlips, grabLevel);
            if (sig != liveSig) { liveSig = sig; liveName = NameFor(doneHalves, doneFlips, grabLevel); }
            livePoints = liveName == null ? 0 : TrickPoints(doneHalves, doneFlips, grabLevel, grabHeldFor, GrabBonusNow());
        }

        static int Sig(int halves, int flips, int level) => (halves + 64) | ((flips + 16) << 8) | (level << 14);

        /// <summary>For the test tools: commit this much rotation (half turns, whole flips; signed) as flicks would, come what may.</summary>
        public void ForceTrick(int halves, int flips)
        {
            if (!trickAir) { trickAir = true; landIn = 1f; }
            spinHalves = halves; spinDir = halves > 0 ? 1 : halves < 0 ? -1 : 0; flipTurns = flips;
        }

        /// <summary>For the test tools: this grab (0.. into the ride's own), as a tap would take it; hold it with GrabHeld.</summary>
        public void ForceGrab(int index)
        {
            if (!trickAir) { trickAir = true; landIn = 1f; }
            grabFirst = Mathf.Clamp(index, 0, Grabs.Length - 1); grabLevel = 1; grabClock = GrabMin;
        }

        // ---------------------------------------------------------------- names and points

        public static int SpinPoints(int halves) { int n = Mathf.Abs(halves); return 150 * n + 25 * n * (n - 1); }
        public static int FlipPoints(int flips) { int m = Mathf.Abs(flips); return m == 0 ? 0 : 700 * m + 250 * m * (m - 1) + (flips > 0 ? 100 * m : 0); }

        /// <summary>What a trick is worth, landed clean: its spin, its flips, a quarter more for both at once, the grab and how long it was held.</summary>
        public static int TrickPoints(int halves, int flips, int level, float held, int bonus = 0)
        {
            int s = SpinPoints(halves), f = FlipPoints(flips);
            float pts = s + f + (s > 0 && f > 0 ? 0.25f * (s + f) : 0f);
            level = Mathf.Clamp(level, 0, MaxGrabLevel);
            if (level > 0) pts += GrabPoints[level] + GrabPerSecond * Mathf.Min(held, GrabHeldMax) + bonus;
            return Mathf.RoundToInt(pts / 5f) * 5;
        }

        /// <summary>What an air is worth on its own (s of hang time, m of height), and what it is called.</summary>
        public static int AirPoints(float seconds, float peak, out string name)
        {
            // (by hang time; the height is over the snow under the board, which on a 36 degree hill falls away fast, so it only
            // counts when it is far more than the time alone would give)
            name = seconds >= 3.8f || peak >= 24f ? "Monster Air" : seconds >= 2.6f || peak >= 16f ? "Huge Air" : seconds >= 1.6f || peak >= 9f ? "Big Air" : "Air";
            return Mathf.Max(5, Mathf.RoundToInt(100f * seconds * seconds / 5f) * 5);
        }

        /// <summary>
        /// A trick's name from its parts (null with none): flips, spin, grab. A flat spin on a snowboard is frontside or
        /// backside (a regular rider, the toe edge to the right: frontside turns left, the chest opening down the hill;
        /// switch, the other way); a backflip with a spin is a cork (whole turns) or a rodeo (an odd half turn), a
        /// frontflip with one a misty; skiers count their spins plain; a tube twirls, and its flip is a bail roll.
        /// </summary>
        public string NameFor(int halves, int flips, int level)
        {
            int m = Mathf.Abs(flips), deg = Mathf.Abs(halves) * 180;
            string count = m <= 1 ? "" : m == 2 ? "Double " : m == 3 ? "Triple " : m == 4 ? "Quad " : m + "x ";
            string rot;
            if (Tube)
                rot = m > 0 && deg > 0 ? count + "Bail Roll " + deg : m > 0 ? count + "Bail Roll" : deg > 0 ? deg + " Twirl" : null;
            else
            {
                bool board = Ride == RideKind.Snowboard;
                bool front = takeoffSwitch ? halves > 0 : halves < 0;
                string side = board && halves != 0 ? (front ? "Frontside " : "Backside ") : "";
                rot = m > 0 && deg > 0 ? (flips > 0 ? count + "Misty " + deg : (halves & 1) != 0 ? count + "Rodeo " + deg : side + count + "Cork " + deg)
                    : m > 0 ? count + (flips > 0 ? "Frontflip" : "Backflip")
                    : deg > 0 ? side + deg : null;
            }
            string grab = GrabName(level);
            string name = rot == null ? grab : grab == null ? rot : rot + " " + grab;
            // (taken off riding switch: a switch trick)
            return name != null && takeoffSwitch ? "Switch " + name : name;
        }

        string GrabName(int level)
        {
            if (level <= 0) return null;
            var g = Grabs;
            string a = g[grabFirst % g.Length], b = g[(grabFirst + 1) % g.Length], tw = Tube ? "Big" : feel.Tweak;
            switch (Mathf.Min(level, MaxGrab))
            {
                case 1: return a;
                case 2: return tw + " " + a;
                case 3: return a + " to " + b;
                case 4: return a + " to " + tw + " " + b;
                case 5: return "Triple Grab";
                default: return tw + " Triple Grab";
            }
        }

        /// <summary>What the grabs in hand add for their shapes (the ride's GrabBonus): the first, and the second once it is gone into.</summary>
        int GrabBonusNow()
        {
            if (grabLevel <= 0) return 0;
            int[] b = Tube ? TubeGrabBonus : feel.GrabBonus;
            if (b == null || b.Length == 0) return 0;
            int n = Grabs.Length, v = b[grabFirst % n % b.Length];
            if (grabLevel >= 3) v += b[(grabFirst + 1) % n % b.Length];
            return v;
        }

        /// <summary>The snowboard grabs a mission would take this air's grabs as (each ride's own count as their namesakes: Ride.cs).</summary>
        string GrabAlias()
        {
            if (grabLevel <= 0 || Tube || feel.GrabCountsAs == null) return null;
            string s = "";
            int n = Mathf.Min((grabLevel + 1) / 2, feel.GrabCountsAs.Length);
            for (int i = 0; i < n; i++) s += feel.GrabCountsAs[(grabFirst + i) % feel.GrabCountsAs.Length] + " ";
            return s;
        }

        // ---------------------------------------------------------------- the landing

        /// <summary>How far off the rotation is as the board meets the snow (deg): the spin from the nearest half turn, the flip from upright.</summary>
        void LandErrors(out float spinErr, out float flipErr)
        {
            float s = Mathf.Repeat(Mathf.Abs(spinNow) * Mathf.Rad2Deg, 180f), f = Mathf.Repeat(Mathf.Abs(flipNow) * Mathf.Rad2Deg, 360f);
            spinErr = Tube ? 0f : Mathf.Min(s, 180f - s);   // (backwards is fine: that is riding switch; a tube has no way round)
            flipErr = Mathf.Min(f, 360f - f);
        }

        /// <summary>
        /// Landing with the rotation not come round: the board upside down, or across the line of travel. Null if it
        /// is near enough upright and lined up to ride out (clean, or sketchy: see LandTrick).
        /// </summary>
        string BadLanding()
        {
            if (!HasTrick || AirTime <= 0.3f) return null;
            LandErrors(out float spinErr, out float flipErr);
            if (flipErr > BadFlip) return "Landed upside down!";
            if (spinErr > BadSpin) return "Landed sideways!";
            return null;
        }

        /// <summary>
        /// The air is over and it was ridden out (or it ended on a rail or a cable: onto): pay the trick as it was
        /// actually landed, and the air itself.
        /// </summary>
        void LandTrick(bool onto = false)
        {
            string name = null; int pts = 0; bool sketchy = false;
            if (HasTrick && AirTime > 0.3f)
            {
                LandErrors(out float spinErr, out float flipErr);
                if (onto && (spinErr > BadSpin || flipErr > BadFlip)) return;   // caught the rail half way round: no trick, no fall
                int halves = Mathf.RoundToInt(spinNow / Mathf.PI), flips = Mathf.RoundToInt(flipNow / (Mathf.PI * 2f));
                sketchy = spinErr > CleanSpin || flipErr > CleanFlip || (grabPose > 0.6f && !onto);
                name = NameFor(halves, flips, grabLevel);
                if (name != null)
                {
                    pts = TrickPoints(halves, flips, grabLevel, grabHeldFor, GrabBonusNow());
                    if (sketchy) pts = Mathf.RoundToInt(pts * 0.5f / 5f) * 5;
                    TrickAlias = GrabAlias();
                }
                if (!onto)
                {
                    // what is left of the turn swings out on the snow; a half turn more is riding switch (LandStance)
                    LandSpin = LandStance();
                    LandFlip = Mathf.DeltaAngle(0f, flipNow * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    if (sketchy) wobble = Mathf.Max(wobble, 0.45f);
                }
                else LandStance();   // (onto a rail half way round: on it switch)
            }
            // the air itself first, then the trick done in it (the trick is the headline: the newest link)
            if (!onto && Peak > 0.8f && AirTime > 0.45f)
            {
                int airPts = AirPoints(AirTime, Peak, out string air);
                if (name == null || air != "Air") Emit(air, airPts, LinkKind.Air);   // (a small air with a trick in it is the trick)
            }
            if (name != null) Emit(sketchy ? "Sketchy " + name : name, pts, LinkKind.Trick, sketchy);
            if (sketchy && !onto) RoughLanding?.Invoke("Sketchy landing");
        }

        // ---------------------------------------------------------------- boxes, rails and cables: the stance held

        int grindQuarter, grindPress, stanceMask;
        float grindYawTo, segPoints;
        string segName;
        bool segNew, gxOn, gyOn, grindScoring, grindCable, grindRail;

        /// <summary>On a box, a rail or a cable and being paid for it (not a lift's launch ramp).</summary>
        public bool GrindLive => Grinding && grindScoring;
        /// <summary>How the board sits on the line (rad off it): 0 along it, a quarter turn across it. Eased, for the body.</summary>
        public float GrindYaw { get; private set; }
        /// <summary>-1 a tail press .. +1 a nose press, eased.</summary>
        public float GrindPress { get; private set; }

        void GrindBegin(bool rail, bool cable)
        {
            grindScoring = true; grindRail = rail; grindCable = cable;
            grindQuarter = rail ? 1 : 0; grindPress = 0; stanceMask = 0;   // along a box, across a rail
            grindYawTo = GrindYaw = grindQuarter * Mathf.PI * 0.5f; GrindPress = 0f;
            gxOn = Mathf.Abs(TrickX) >= FlickAt; gyOn = Mathf.Abs(TrickY) >= FlickAt;   // (steering as you came on is not a switch-up)
            StartSegment(cable ? CableTune.SpanPoints : rail ? RailOn : BoxOn);
        }

        void StartSegment(int forNew)
        {
            int id = grindQuarter + 4 * (grindPress + 1);
            segNew = (stanceMask & (1 << id)) == 0;
            stanceMask |= 1 << id;
            segPoints = segNew ? forNew : 0f;
            segName = Rides.GrindName(Ride, grindRail, grindCable, grindQuarter, grindPress);
        }

        void EndSegment()
        {
            int pts = Mathf.RoundToInt(segPoints);
            segPoints = 0f;
            if (grindScoring && pts > 0) Emit(segName, pts, LinkKind.Grind, false, segNew);   // (a stance gone back to is paid, but is not another trick for the day's missions)
        }

        /// <summary>One step of a grind: the metres paid, and a flick taken as a switch-up or a press.</summary>
        void GrindTick(float dt, float metres)
        {
            if (!grindScoring) { GrindYaw = 0f; GrindPress = 0f; return; }
            segPoints += metres * (grindCable ? CableTune.PointsPerMetre : RailPerMetre);
            float ax = Mathf.Abs(TrickX), ay = Mathf.Abs(TrickY);
            bool on = gxOn ? ax > FlickOff : ax >= FlickAt;
            if (on && !gxOn)
            {
                int d = (TrickX > 0f ? 1 : -1) * feel.SwitchStep;
                EndSegment();
                grindQuarter = ((grindQuarter + d) % 4 + 4) % 4; grindPress = 0;
                grindYawTo += d * Mathf.PI * 0.5f;
                StartSegment(SwitchUp);
            }
            gxOn = on;
            bool yon = gyOn ? ay > FlickOff : ay >= FlickAt;
            if (yon && !gyOn && feel.Presses)
            {
                int d = TrickY > 0f ? 1 : -1;
                EndSegment();
                grindPress = grindPress == d ? 0 : d;   // (the same again lets it down)
                StartSegment(SwitchUp);
            }
            gyOn = yon;
            GrindYaw = Mathf.MoveTowards(GrindYaw, grindYawTo, dt * 14f);
            GrindPress = Mathf.MoveTowards(GrindPress, grindPress, dt * 8f);
        }

        void GrindEnd() { EndSegment(); grindScoring = false; }
    }
}
