using UnityEngine;

namespace Summit
{
    /// <summary>
    /// (groundspin) A flat spin on the snow: swipe left or right (a quick throw of the finger, let go: FingerGesture)
    /// and the rider and the board go round on the snow that way, a 360 (a short swipe on a board or skis is a 180, a
    /// long one a 540; another swipe the same way while it turns adds half a turn more, up to the 540). A board or skis
    /// come out of a 180 or a 540 riding switch; a toboggan, a tube and a kayak have no way round to ride, so theirs go
    /// by whole turns (a long swipe, or another, a 720).
    ///
    /// It is only the look and the yaw: the board goes on where it was going, as fast. The swipe's own moment of drag
    /// (it is the steering finger) is taken back: the way the board was going as the finger came down is put back as
    /// the spin begins. Spinning never puts the rider down by itself; turned across the line mid-spin, though, there
    /// is no glancing off anything: what would only knock a rider aside (a tree clipped, a rock's face) takes them down
    /// (RiderRideOver.Meet). Off a jump mid-spin, the spin goes on into the air as the air's own trick.
    ///
    /// Scored as it comes round (Butter / Flat / Spin / Kayak Flatspin, and the degrees) into the combo as a trick,
    /// though not toward the day's trick missions: a 360 on the snow is not the air mission's 360.
    /// </summary>
    public partial class Rider
    {
        // ---- tuning
        /// <summary>On the snow it turns this share of the air's SpinRate (and half as much slower as the ride spins in the air).</summary>
        public static float GroundSpinShare = 0.75f;
        /// <summary>Swipes this long (screen widths) and over are a 360, and a 540; shorter, a 180 (board and skis).</summary>
        public static float SwipeMid = 0.35f, SwipeLong = 0.6f;
        /// <summary>Not much slower than this (m/s): a spin needs speed to spin on.</summary>
        public static float GroundSpinMinSpeed = 3f;
        /// <summary>Into something faster than this (m/s) turned across the line mid-spin is a fall.</summary>
        public static float SpinHitInto = 2f;
        /// <summary>The swipe's own drag is undone if the finger came down no longer ago than this (s).</summary>
        const float SnapKeep = 0.45f;

        // ---- the spin in hand
        int gsHalves;            // committed half turns (signed: + right)
        float gsNow;             // rad turned so far
        bool gsSwitch;           // begun riding switch
        bool gsRestore;          // put back the board's line from the touch before stepping
        float gsClock;           // s, the physics' own (the touch's snapshot is timed by it)
        // where the board was going as the finger came down
        Vector3 snapV; float snapHeading, snapEase, snapAt = -99f;

        /// <summary>A flat spin on the snow is under way.</summary>
        public bool GroundSpinning => gsHalves != 0;
        /// <summary>How far round it is (rad, signed): what the body is drawn with.</summary>
        public float GroundSpinNow => gsNow;
        /// <summary>The spin committed (half turns, signed).</summary>
        public int GroundSpinHalves => gsHalves;
        /// <summary>Turned across the line (more than CleanSpin off it, a backwards board being switch on a board or skis).</summary>
        public bool MidSpin
        {
            get
            {
                if (gsHalves == 0) return false;
                float span = CanSwitch ? 180f : 360f, s = Mathf.Repeat(Mathf.Abs(gsNow) * Mathf.Rad2Deg, span);
                return Mathf.Min(s, span - s) > CleanSpin;
            }
        }

        /// <summary>Can this ride spin on the snow?</summary>
        bool CanGroundSpin => Tube || feel.GroundSpin != null;

        /// <summary>The finger touched down: what the board was doing then, in case this is a swipe.</summary>
        void SwipeSnap()
        {
            if (!Running || Crashed || Airborne || Grinding || rb == null) { snapAt = -99f; return; }
            snapV = rb.linearVelocity; snapHeading = Heading; snapEase = easeFor; snapAt = gsClock;
        }

        /// <summary>A swipe on the snow (dir -1 left, +1 right; length in screen widths): a spin, or half a turn more on the one going.</summary>
        public void GroundSwipe(int dir, float length)
        {
            if (dir == 0 || !Running || Crashed || Airborne || Grinding || !CanGroundSpin || Speed < GroundSpinMinSpeed) return;
            int step = CanSwitch ? 1 : 2, most = CanSwitch ? 3 : 4;
            if (gsHalves == 0)
            {
                int halves = length >= SwipeLong ? 3 : length >= SwipeMid ? 2 : 1;
                if (!CanSwitch) halves = halves >= 3 ? 4 : 2;
                gsHalves = dir * halves; gsNow = 0f; gsSwitch = Switch;
                // (the swipe was the steering finger: what it steered in its moment on the glass is taken back)
                gsRestore = input == null || !input.useTilt;
            }
            else if (dir == (gsHalves > 0 ? 1 : -1) && Mathf.Abs(gsHalves) < most)
                gsHalves = dir * Mathf.Min(most, Mathf.Abs(gsHalves) + step);
        }

        /// <summary>Each physics step: the line put back once, the spin turned, and paid as it comes round.</summary>
        Vector3 GroundSpinStep(float dt, Vector3 v)
        {
            gsClock += dt;
            if (gsHalves == 0) { gsRestore = false; return v; }
            if (Crashed || Grinding) { GroundSpinClear(); return v; }
            if (gsRestore)
            {
                gsRestore = false;
                if (gsClock - snapAt <= SnapKeep && !Airborne)
                {
                    // the way it was going, as fast as it goes now
                    Vector3 was = new Vector3(snapV.x, 0f, snapV.z), now = new Vector3(v.x, 0f, v.z);
                    if (was.sqrMagnitude > 0.25f && now.sqrMagnitude > 0.25f) { now = was.normalized * now.magnitude; v = new Vector3(now.x, v.y, now.z); }
                    Heading = snapHeading; Slip = 0f;
                    easeFor = snapEase + (gsClock - snapAt); pushing = false;   // (let go, as it was before the touch)
                }
            }
            float target = gsHalves * Mathf.PI, left = Mathf.Abs(target - gsNow);
            float rate = SpinRate * GroundSpinShare * (Tube ? 1f : Mathf.Lerp(1f, feel.Spin, 0.5f));   // (the slow spinners half as much slower: a toboggan's 720 in 2 s, not 2.5)
            float w = Mathf.Min(rate, Mathf.Max(rate * 0.4f, left * 12f));   // (eased into the stop)
            gsNow = Mathf.MoveTowards(gsNow, target, w * dt);
            if (Mathf.Abs(target - gsNow) < 1e-3f) GroundSpinDone();
            return v;
        }

        /// <summary>Come round: switch after an odd number of half turns (a board, skis), and the points.</summary>
        void GroundSpinDone()
        {
            int halves = gsHalves;
            bool wasSwitch = gsSwitch;
            if ((halves & 1) != 0 && CanSwitch) Switch = !Switch;
            gsHalves = 0; gsNow = 0f;
            Emit(GroundSpinName(halves, wasSwitch), GroundSpinPoints(halves), LinkKind.Trick, false, false);
        }

        /// <summary>Off the snow mid-spin into an air long enough to trick: the spin goes on as that air's own (RiderTricks).</summary>
        void GroundSpinIntoAir()
        {
            if (gsHalves == 0) return;
            spinNow = gsNow; spinHalves = gsHalves; spinDir = gsHalves > 0 ? 1 : -1;
            gsHalves = 0; gsNow = 0f; gsRestore = false;
        }

        void GroundSpinClear() { gsHalves = 0; gsNow = 0f; gsRestore = false; snapAt = -99f; }

        /// <summary>What a flat spin on the snow is worth: three fifths of the same spin in the air.</summary>
        public static int GroundSpinPoints(int halves) => Mathf.RoundToInt(0.6f * SpinPoints(halves) / 5f) * 5;

        /// <summary>Its name: the ride's (Butter, Flat, Spin, Kayak Flatspin) and the degrees; begun switch, a switch one.</summary>
        public string GroundSpinName(int halves, bool fromSwitch)
        {
            string kind = Tube ? "Spin" : feel.GroundSpin ?? "Spin";
            string name = kind + " " + Mathf.Abs(halves) * 180;
            return fromSwitch && CanSwitch ? "Switch " + name : name;
        }
    }
}
