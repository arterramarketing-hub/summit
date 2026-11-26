using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The fun rides' own physics (rides), on top of the one carving model (RiderMove.cs) with each ride's numbers
    /// (Ride.cs). Kept to a few doors into it: an extra sideways pull (a paddle stroke), an extra push along (the
    /// paddle, the engine, the bear's legs) with any extra drag (a kayak spun across its way), and a check after each
    /// step (the car rolling over). Everything here is also what the body shows and the sound plays.
    ///
    /// The kayak: it slides on its hull and the paddle does the rest. Turning, a forward stroke on the outside of the
    /// turn every StrokeEvery s (a forward stroke on the left swings the bow right): during its pull it pushes the hull
    /// round (StrokeTurn) and on (StrokePush, less the faster it goes); slow, it strokes left and right to get going.
    /// Thrown hard from one way to the other at speed, or slid well across, it spins out: the hull goes round on its
    /// own middle (SpinOut, the look) while it carries on down its line, slowed while it is across, and comes back
    /// round to face the way it goes (or slides backwards a moment first).
    ///
    /// The car: an engine that keeps it going where the hill gives up (CruiseSpeed) and pulls harder held (the tuck is
    /// the throttle), through five gears for the sound. Its springs tip it over in a hard turn and on a side slope
    /// (Tip: the pull across the way it goes, in g, with the snow's own tilt that way): past RollTip for RollHold s it
    /// rolls over, a crash like any other (Feel.Wreck).
    ///
    /// The bear: it runs by itself where the hill flattens (BearRun), galloping (Gait, the cycle the body plays) a
    /// stride a cycle, its paws coming down twice a cycle (Pawfall, for the sound).
    /// </summary>
    public partial class Rider
    {
        // ---- the kayak
        public static float StrokeEvery = 0.78f, StrokeLength = 0.73f, StrokeTurn = 16f, StrokePush = 3.2f, StrokeTopSpeed = 24f, StrokeSlow = 12f;
        /// <summary>The stroke under way: +1 the left blade, -1 the right, 0 none; and how far through it (0..1).</summary>
        public int PaddleSide { get; private set; }
        public float PaddleT { get; private set; }
        /// <summary>A stroke began (its side).</summary>
        public System.Action<int> Stroke;
        float strokeClock;
        int lastStrokeSide = -1;
        /// <summary>How far the kayak's hull is spun round off the way it goes (rad, the look), and how fast.</summary>
        public float SpinOut { get; private set; }
        float spinRate;
        /// <summary>How fast the hull is spinning round (rad/s): the test tools.</summary>
        public float HullSpinRate => spinRate;
        public static float SpinKick = 9f, SpinFriction = 1.6f, SpinDrag = 7f;
        public System.Action<float> SpunOut;

        // ---- the car
        public static float CruiseSpeed = 14f, CruisePull = 5f, ThrottlePull = 4.5f, ThrottleTop = 34f;
        static readonly float[] GearTop = { 8f, 15f, 23f, 32f, 99f };
        /// <summary>The engine: how far up its gear's range (0..1), which gear, how hard it is pulling (0..1).</summary>
        public float Rpm { get; private set; }
        public int Gear { get; private set; }
        public float Throttle { get; private set; }
        /// <summary>How far over the car is on its springs: its tip (the pull across the way it goes in g, and the
        /// snow's tilt), + to the right. Past RollTip for RollHold s it goes over.</summary>
        public float Tip { get; private set; }
        public static float RollTip = 2.1f, RollHold = 0.22f, RollSpeed = 16f;
        float tipOver;
        public System.Action<int> GearChange;

        // ---- the bear
        public static float BearRun = 11f, BearRunPull = 6f, BearStride = 4.2f;
        /// <summary>The gallop: where it is in its cycle (0..1), and the cycles a second.</summary>
        public float Gait { get; private set; }
        public float GaitRate { get; private set; }
        public System.Action Pawfall;

        void VehicleReset()
        {
            PaddleSide = 0; PaddleT = 0f; strokeClock = 0f; lastStrokeSide = -1; SpinOut = 0f; spinRate = 0f;
            Rpm = 0f; Gear = 0; Throttle = 0f; Tip = 0f; tipOver = 0f; Gait = 0f; GaitRate = 0f;
        }

        /// <summary>The sideways pull a paddle stroke adds (m/s^2 across the fall line, + right): GroundStep's.</summary>
        float VehiclePull(float dt, float asked, float sp)
        {
            if (Ride != RideKind.Kayak) return 0f;
            // the stroke under way, and the next one: on the outside of the turn, or left and right to get going
            strokeClock += dt;
            if (PaddleSide != 0)
            {
                PaddleT += dt / StrokeLength;
                if (PaddleT >= 1f) { PaddleSide = 0; PaddleT = 0f; }
            }
            bool turning = Mathf.Abs(asked) > 0.15f, slow = sp < StrokeSlow;
            if (PaddleSide == 0 && strokeClock >= StrokeEvery && (turning || slow) && !Airborne)
            {
                PaddleSide = turning ? (asked > 0f ? 1 : -1) : -lastStrokeSide;
                lastStrokeSide = PaddleSide; PaddleT = 0f; strokeClock = 0f;
                Stroke?.Invoke(PaddleSide);
            }
            // the pull: from the catch to the exit (0.18..0.66 of the clip), the bow swung away from the blade
            float power = PaddleSide != 0 ? Mathf.Clamp01(Mathf.Min((PaddleT - 0.15f) / 0.1f, (0.7f - PaddleT) / 0.1f)) : 0f;
            return turning ? PaddleSide * StrokeTurn * power : PaddleSide * StrokeTurn * 0.25f * power;
        }

        /// <summary>The ride's own push along and its extra drag (GroundStep's, after the hill's and the air's): the speed it leaves.</summary>
        float VehicleDrive(float dt, float sp, float sinP, float slopeS)
        {
            switch (Ride)
            {
                case RideKind.Kayak:
                {
                    float power = PaddleSide != 0 ? Mathf.Clamp01(Mathf.Min((PaddleT - 0.15f) / 0.1f, (0.7f - PaddleT) / 0.1f)) : 0f;
                    sp += StrokePush * power * Mathf.Clamp01(1f - sp / StrokeTopSpeed + 0.15f) * dt;
                    // spun out: thrown across or slid well across at speed, the hull goes round; slowed while it is
                    // across its way (a hull side on ploughs), and it swings back to face the way it goes
                    if (Mathf.Abs(SpinOut) < 0.05f && Mathf.Abs(spinRate) < 0.1f && sp > 13f && (Sliding || Skid > 0.62f))
                    {
                        spinRate = Mathf.Sign(Side == 0f ? Steer : Side) * SpinKick * Mathf.Clamp01(sp / 25f + 0.3f);
                        SpunOut?.Invoke(sp);
                    }
                    if (SpinStep(dt)) sp -= Mathf.Min(sp, SpinDrag * Mathf.Abs(Mathf.Sin(SpinOut)) * dt);
                    break;
                }
                case RideKind.Car:
                {
                    // the engine: cruising where the hill gives up, harder with the throttle (the tuck) held
                    bool held = Tuck > 0.5f;
                    Throttle = Mathf.MoveTowards(Throttle, held ? 1f : sp < CruiseSpeed ? 0.55f : 0.18f, dt * 4f);
                    if (sp < CruiseSpeed) sp += CruisePull * (1f - sp / CruiseSpeed) * dt;
                    if (held && sp < ThrottleTop) sp += ThrottlePull * (1f - sp / ThrottleTop) * dt;
                    Revs(sp, dt);
                    break;
                }
                case RideKind.Bear:
                {
                    if (sp < BearRun) sp += BearRunPull * (1f - sp / BearRun) * dt;
                    GallopStep(sp, dt);
                    break;
                }
            }
            return sp;
        }

        /// <summary>
        /// The kayak's spin-out going on (on the snow and through the air alike): round at its rate, slowing, kept within
        /// half a turn either way (past half way round it is coming round the other side, so it carries on round to face
        /// the way it goes; short of half way it swings back). True while it is spun.
        /// </summary>
        bool SpinStep(float dt)
        {
            if (Mathf.Abs(spinRate) <= 0.01f && Mathf.Abs(SpinOut) <= 0.01f) return false;
            SpinOut += spinRate * dt;
            if (SpinOut > Mathf.PI) SpinOut -= 2f * Mathf.PI; else if (SpinOut < -Mathf.PI) SpinOut += 2f * Mathf.PI;
            spinRate = Mathf.MoveTowards(spinRate, 0f, SpinFriction * 3f * dt);
            if (Mathf.Abs(spinRate) < 2.5f) SpinOut = Mathf.Lerp(SpinOut, 0f, 1f - Mathf.Exp(-dt * 2.2f));
            if (Mathf.Abs(SpinOut) < 0.02f && Mathf.Abs(spinRate) < 0.2f) { SpinOut = 0f; spinRate = 0f; }
            return true;
        }

        /// <summary>The engine's revs through its gears (the sound's): up its gear's range with the speed, a shift at the top.</summary>
        void Revs(float sp, float dt)
        {
            int g = 0;
            while (g < GearTop.Length - 1 && sp > GearTop[g]) g++;
            if (g != Gear) { Gear = g; GearChange?.Invoke(g); }
            float lo = g == 0 ? 0f : GearTop[g - 1], hi = g == GearTop.Length - 1 ? 45f : GearTop[g];
            float want = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01((sp - lo) / (hi - lo))) * Mathf.Lerp(0.85f, 1f, Throttle);
            Rpm = Mathf.Lerp(Rpm, want, 1f - Mathf.Exp(-dt * 8f));
        }

        void GallopStep(float sp, float dt)
        {
            GaitRate = Mathf.Clamp(sp / BearStride, 0.6f, 2.6f);
            float was = Gait;
            Gait = Mathf.Repeat(Gait + GaitRate * dt, 1f);
            if ((was < 0.1f && Gait >= 0.1f) || (was < 0.6f && Gait >= 0.6f)) Pawfall?.Invoke();
        }

        /// <summary>After the step: what the ride does by itself (the car's springs, the gallop in the air, the engine's revs).</summary>
        void VehicleAfter(float dt, Vector3 p)
        {
            if (Ride == RideKind.Car)
            {
                if (Airborne)
                {
                    // off the snow the wheels spin up (held) or the revs drop back
                    Rpm = Mathf.Lerp(Rpm, Throttle > 0.5f ? 1f : 0.55f, 1f - Mathf.Exp(-dt * 3f));
                    Tip = Mathf.Lerp(Tip, 0f, 1f - Mathf.Exp(-dt * 3f)); tipOver = 0f;
                    return;
                }
                if (Grinding) { Tip = 0f; tipOver = 0f; return; }
                // over on its springs: the pull across the way it goes (in g) and the snow's tilt across it, the outside
                // of the turn the way it leans. Past RollTip at speed for long enough, over it goes
                float line = TravelHeading;
                var right = new Vector3(Mathf.Cos(line), 0f, -Mathf.Sin(line));
                Vector3 n = Slope.Normal(p.x, p.z);
                float tilt = Vector3.Dot(n, right);    // (+: the snow falls away to the right, and tips it that way)
                float pull = -SideEdge / G;            // (the push left (SideEdge < 0) throws the car's weight right)
                float tip = pull + 1.6f * tilt;
                Tip = Mathf.Lerp(Tip, tip, 1f - Mathf.Exp(-dt * 10f));
                tipOver = Mathf.Abs(Tip) > RollTip && Speed > RollSpeed && invulnerable <= 0f ? tipOver + dt : Mathf.Max(0f, tipOver - 2f * dt);
                if (tipOver > RollHold && Undercut <= 0f)
                {
                    tipOver = 0f;
                    if (Shields > 0) { Shields--; invulnerable = 0.9f; ShieldHit?.Invoke(); }
                    else { RollDir = Mathf.Sign(Tip); DoCrash(feel.Wreck ?? "Rolled it!"); }
                }
            }
            else if (Ride == RideKind.Bear && Airborne) GaitRate = Mathf.Lerp(GaitRate, 0.9f, 1f - Mathf.Exp(-dt * 2f));
            else if (Ride == RideKind.Kayak && Airborne) SpinStep(dt);   // (a spin-out goes on round through the air)
        }

        /// <summary>Which way the car went over (+1 to the right): the thrown car is set rolling that way.</summary>
        public float RollDir { get; private set; }
    }
}
