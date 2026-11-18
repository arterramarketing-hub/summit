using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The slide behind you. It lies quiet while you are in camp and on the start pad: the slope above releases
    /// only once you have dropped in (Asleep until then), with a crack and a rumble, and then it gives chase.
    /// It creeps: it cruises a little slower than you have been riding, so a clean run
    /// slowly pulls away from it, but it never drops far behind, and it keeps coming at its own pace while you
    /// are slow or down in the snow. So falls and slow sections are what let it catch up.
    ///
    /// It does not kill on contact. When it reaches you it first flows under the board (the snow heaves, the
    /// edge stops biting, you are carried and thrown about), then knocks you off, carries the body on in the
    /// debris, and buries it as the slide runs out and settles. The game ends the run once that is over (Done).
    ///
    /// It is a river of heavy debris, metres deep (Depth), whose flanks run on ahead of its middle and wrap round
    /// you as it closes. The shape lives here, not in the view, because the camera, the carried body and the
    /// burial all have to sit on the same surface the view draws. The look of all this is AvalancheView.
    /// </summary>
    public class Avalanche
    {
        public const string Cause = "Avalanche!";
        public enum Stage { Asleep, Chasing, Undercut, Carried, Buried }

        public Stage Phase { get; private set; }
        public float PhaseTime { get; private set; }
        /// <summary>The toe: the leading edge of the snow surging along the ground.</summary>
        public float Z { get; private set; }
        /// <summary>How fast the slide is moving down the hill (m/s).</summary>
        public float Speed { get; private set; }
        public float Elapsed { get; private set; }
        /// <summary>Where the debris is piling over the body.</summary>
        public Vector3 BuryAt { get; private set; }
        /// <summary>0..1: how far the debris has piled up over the body.</summary>
        public float Cover { get; private set; }
        /// <summary>0..1: the snow under the board churning and lifting (while it flows under you).</summary>
        public float Churn { get; private set; }
        /// <summary>0..1: how hard the ground shakes for the camera.</summary>
        public float Shake { get; private set; }
        /// <summary>The speed it had when it reached you (the run-out slows from this).</summary>
        public float CaughtSpeed { get; private set; }
        /// <summary>The run is over: the body is under the snow and the camera has held on it.</summary>
        public bool Done => Phase == Stage.Buried && PhaseTime > HoldTime;
        /// <summary>The slide has caught the rider (from the first touch under the board).</summary>
        public bool Caught => Phase >= Stage.Undercut;
        /// <summary>The slope has let go (it is asleep, and nothing of it is drawn or heard, until you drop in).</summary>
        public bool Released => Phase != Stage.Asleep;
        /// <summary>True for the one step in which the slope let go (the game plays the crack).</summary>
        public bool JustReleased { get; private set; }
        /// <summary>The middle of the flow across the hill: it follows you, slowly, so its arms come round you.</summary>
        public float FlowX { get; private set; }

        // how long it flows under the board before it knocks you off, and how long the camera holds on the grave
        public static float UndercutTime = 1.7f, HoldTime = 2.6f;
        // how it creeps: the most it ever closes on you (m/s), early on and after four minutes
        public static float CreepEasy = 6f, CreepHard = 8.5f;
        // when it lets go: this far past the lip, or this long into the run, whichever comes first; and how far
        // up the hill behind you the fracture is
        public static float ReleasePast = 30f, ReleaseTime = 9f, ReleaseGap = 130f;

        // the debris flow: how deep its body runs, how far its arms run on ahead of its middle, its half-width,
        // its length behind the front, and how long its snout is (where it climbs from the toe to its full depth)
        public const float DepthMax = 4.2f, ArmLead = 22f, HalfWidth = 46f, Length = 120f, Snout = 12f;
        /// <summary>How far the surface snow has run on past the front (the view scrolls its texture by this).</summary>
        public float SurfaceRun => flowOff;
        Vector3 dipAt; float dipLift, dip;   // the hollow the board rides in while it is carried on the debris
        float flowOff, boil, closed;   // closed: 0..1, its middle surging on over you once it has you   // how far the surface snow has run on past the front (it overtakes the toe), and its churn

        float ema, sinceDown = 99f;
        bool buryLocked;

        public void Reset(float riderZ)
        {
            Z = riderZ - 1000f;   // asleep: nowhere (see Release)
            Speed = 0f;
            FlowX = 0f; flowOff = 0f; boil = 0f; closed = 0f; dip = 0f; JustReleased = false;
            ema = 0f; sinceDown = 99f;
            Elapsed = 0f;
            Phase = Stage.Asleep; PhaseTime = 0f;
            Cover = 0f; Churn = 0f; Shake = 0f;
            buryLocked = false;
        }

        public float Gap(Rider r) { return r.Z - Z; }

        /// <summary>0 far away .. 1 on top of you: drives the roar.</summary>
        public float Closeness(Rider r)
        {
            if (Phase == Stage.Chasing) return Mathf.Clamp01(1f - Gap(r) / 90f);
            if (Phase == Stage.Undercut) return 1f;
            return Mathf.Clamp01(0.25f + Speed / Mathf.Max(CaughtSpeed, 1f));   // dying away as it settles
        }

        /// <summary>Put the slide this far behind the rider, moving this fast (for the test tools).</summary>
        public void ForceGap(Rider r, float gap, float speed) { if (Phase == Stage.Asleep) Release(r); Z = r.Z - gap; Speed = speed; ema = Mathf.Max(ema, speed); Elapsed = Mathf.Max(Elapsed, 10f); }

        void Enter(Stage s) { Phase = s; PhaseTime = 0f; }

        public void Step(float dt, Rider r, RiderBody body)
        {
            JustReleased = false;
            if (dt <= 0f) return;
            Elapsed += dt; PhaseTime += dt;
            // the surface of the debris runs a little faster than its front, and boils as it goes
            flowOff += Speed * 0.12f * dt; boil += dt * (0.15f + 0.02f * Speed);
            if (Caught) closed = Mathf.MoveTowards(closed, 1f, dt / 1.4f);
            switch (Phase)
            {
                case Stage.Asleep:
                    // the slope holds until you have dropped in and are on your way
                    Shake = 0f;
                    if (r.Z > Slope.PadLip + ReleasePast || Elapsed > ReleaseTime) Release(r);
                    break;

                case Stage.Chasing:
                    float gap0 = Gap(r);
                    Chase(dt, r);
                    // its arms come round you wherever you go, closely once it is near
                    FlowX = Mathf.Lerp(FlowX, r.transform.position.x, 1f - Mathf.Exp(-dt / Mathf.Lerp(0.25f, 1.5f, Mathf.Clamp01((gap0 - 30f) / 60f))));
                    float gap = Gap(r);
                    Shake = Mathf.Pow(Mathf.Clamp01(1f - gap / 55f), 2f) * 0.9f;
                    if (Elapsed < 3f) Shake = Mathf.Max(Shake, 0.4f * (1f - Elapsed / 3f));   // the ground trembling as it lets go
                    // caught by its middle reaching you, or by steering into one of its arms (a rider in the air is
                    // caught when they come down into it)
                    Vector3 at0 = r.transform.position;
                    if ((gap <= 0f || Depth(at0.x, at0.z) > 1.2f) && (!r.Airborne || r.Crashed))
                    {
                        CaughtSpeed = Mathf.Max(Speed, 12f);
                        if (r.Crashed) StartCarry(r, body);   // already down: straight into the debris
                        else Enter(Stage.Undercut);
                    }
                    break;

                case Stage.Undercut:
                    // it flows on under you, faster than you are going
                    Speed = Mathf.MoveTowards(Speed, Mathf.Max(CaughtSpeed, r.Speed + 3f), 6f * dt);
                    Z += Speed * dt;
                    Churn = Mathf.SmoothStep(0f, 1f, PhaseTime / 0.6f);
                    r.Undercut = Churn;
                    FlowX = Mathf.Lerp(FlowX, r.transform.position.x, 1f - Mathf.Exp(-dt / 0.25f));
                    // up onto the debris as it runs in under the board: carried on it, boots in the snow
                    Vector3 rp = r.transform.position;
                    // (smoothed: a lumpy surface dropping away under the board must not throw it into the air)
                    r.Lift = Mathf.MoveTowards(r.Lift, Mathf.Max(0f, Depth(rp.x, rp.z, false) - 0.5f) * Churn, 7f * dt);
                    // and the churning snow round the board stands about knee deep, so you stay in view in it
                    dipAt = rp; dipLift = r.Lift; dip = 1f;
                    Shake = 1f;
                    if (!r.Crashed && r.Running && !r.Body.isKinematic)
                    {
                        // carried: the moving snow takes the board along with it, whatever you do
                        var v = r.Body.linearVelocity;
                        float want = Speed * 0.95f;
                        if (v.z < want) v.z += (want - v.z) * Mathf.Min(1f, 4f * dt);
                        v.x *= Mathf.Exp(-1.5f * Churn * dt);   // taken straight on down with it
                        r.Body.linearVelocity = v;
                    }
                    if (r.Crashed) StartCarry(r, body);
                    else if (PhaseTime >= UndercutTime)
                    {
                        // the cloud is on you now: the board goes out from under you
                        r.Undercut = 0f; r.Lift = 0f;
                        r.DoCrash(Cause);
                        StartCarry(r, body);
                    }
                    break;

                case Stage.Carried:
                    dip = Mathf.MoveTowards(dip, 0f, dt / 0.8f);   // and then it closes over                    // the slide runs out: it slows to a stop over a few seconds, with the body tumbling in it
                    Speed = Mathf.MoveTowards(Speed, 0f, Mathf.Lerp(3.5f, 7f, Mathf.Clamp01(PhaseTime / 3f)) * dt);
                    Z += Speed * dt;
                    Churn = Mathf.MoveTowards(Churn, 0f, dt);
                    Shake = 0.9f * Mathf.Clamp01(Speed / Mathf.Max(CaughtSpeed, 1f)) + 0.1f;
                    Vector3 c = body != null ? body.RagdollCentre : r.transform.position;
                    if (body != null && Speed > 0.3f)
                    {
                        Vector3 n = Slope.Normal(c.x, c.z);
                        Vector3 down = Vector3.ProjectOnPlane(Vector3.forward, n).normalized;
                        // carried along in the debris, rolled over and over: tumbling near its surface at first,
                        // then dragged under (a body caught high in the air falls into it first)
                        float above = c.y - Slope.DrawnHeight(c.x, c.z);
                        float keep = Depth(c.x, c.z) * Mathf.Lerp(0.7f, 0f, Mathf.Clamp01(PhaseTime / 1.8f));
                        float lift = above < keep + 0.3f ? Mathf.Clamp((keep - above) * 4f, -2f, 6f) * Mathf.Clamp01(Speed / 6f) : 0f;
                        body.Carry(down * Speed * 0.9f + n * lift, 2.6f, dt);
                        FlowX = Mathf.Lerp(FlowX, c.x, 1f - Mathf.Exp(-dt / 3f));
                    }
                    // the pile follows the body until it stops, then stays put
                    if (!buryLocked)
                    {
                        var at = new Vector3(c.x, Slope.DrawnHeight(c.x, c.z) + Depth(c.x, c.z), c.z);
                        BuryAt = Vector3.Lerp(BuryAt, at, 1f - Mathf.Exp(-dt * 6f));
                        if (Speed < 1.5f && PhaseTime > 1.5f) buryLocked = true;
                    }
                    // debris settles over it as it slows
                    if (PhaseTime > 0.8f) Cover = Mathf.MoveTowards(Cover, 1f, dt / 2.8f);
                    if (Cover >= 1f && Speed <= 0.01f) Enter(Stage.Buried);
                    break;

                case Stage.Buried:
                    Speed = 0f; Shake = 0f;
                    break;
            }
        }

        void StartCarry(Rider r, RiderBody body)
        {
            r.Undercut = 0f; r.Lift = 0f;
            r.StayDown = true;   // nobody gets up from this
            Churn = 1f;
            var c = body != null ? body.RagdollCentre : r.transform.position;
            BuryAt = new Vector3(c.x, Slope.DrawnHeight(c.x, c.z) + Depth(c.x, c.z), c.z);
            Enter(Stage.Carried);
        }

        /// <summary>The slope lets go, well up the hill behind you, and starts down from rest.</summary>
        void Release(Rider r)
        {
            Enter(Stage.Chasing);
            Elapsed = 0f;   // the grace and the four-minute ramp count from here
            Z = r.Z - ReleaseGap - Progress.HeadStart;   // the early-warning upgrade buys a longer lead
            Speed = 4f;
            ema = 0f; sinceDown = 99f;
            FlowX = r.transform.position.x;
            JustReleased = true;
        }

        static float Ease(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        /// <summary>How far the front runs on ahead of Z at this point across the hill: the arms lead the middle.</summary>
        public float FrontZ(float x)
        {
            float a = Mathf.Abs(x - FlowX);
            float lead = ArmLead * Mathf.Lerp(Ease(1.2f, 6.5f, a), 1f, closed * closed * (3f - 2f * closed)) * (1f - 0.35f * Ease(30f, HalfWidth, a));
            // a ragged, shifting front, but true in the middle, where it catches you
            return Z + lead + 4.4f * (Mathf.PerlinNoise(x * 0.06f + 3.1f, Z * 0.012f) - 0.5f) * Ease(3f, 15f, a);
        }

        /// <summary>
        /// How high the debris stands over the snow at this point (0 outside it): a steep, bulging snout climbing
        /// from the toe, a body four metres deep, its surface heaped in lumps that run on toward the front and boil
        /// as they go, thinning out to the sides and far back up the hill. Once the slide stops it settles a little
        /// and stays, the debris the body is buried in.
        /// </summary>
        public float Depth(float x, float z) => Depth(x, z, true);

        float Depth(float x, float z, bool hollow)
        {
            if (Phase == Stage.Asleep) return 0f;
            float a = Mathf.Abs(x - FlowX);
            if (a >= HalfWidth) return 0f;
            float s = FrontZ(x) - z;
            if (s <= 0f || s >= Length) return 0f;
            float f = 1f - Mathf.Min(1f, s / Snout);
            float d = DepthMax * (1f - f * f) + 0.9f * Mathf.Exp(-(s - 7f) * (s - 7f) / 16f);   // the wall, and the head bulging over it
            d *= (1f - Ease(Length - 45f, Length, s)) * (1f - Ease(HalfWidth - 16f, HalfWidth, a)) * (1f - 0.2f * Ease(15f, 35f, a));
            if (Phase >= Stage.Carried) d *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(Speed / Mathf.Max(CaughtSpeed, 1f)));   // spreading as it stops
            // lumps and slabs on the surface, carried on toward the front
            float ls = s + flowOff;
            float n1 = Mathf.PerlinNoise(x * 0.11f + 7.3f, ls * 0.11f + boil * 0.3f), n2 = Mathf.PerlinNoise(x * 0.27f + 1.1f + boil, ls * 0.27f);
            // big rounded heaps, and blocky ridges between them (folded noise)
            float bumps = (2.2f * (n1 - 0.5f) + 0.9f * (0.5f - Mathf.Abs(n2 - 0.5f) * 2f)) * (1f + 0.5f * Mathf.Exp(-(s - 6f) * (s - 6f) / 20f));
            d = Mathf.Max(0f, d + bumps * Mathf.Min(1f, d / 1.5f));
            if (hollow && dip > 0f)
            {
                float dx = x - dipAt.x, dz = z - dipAt.z, r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r < 5f) d = Mathf.Lerp(d, Mathf.Min(d, dipLift + 0.45f), dip * (1f - Ease(1.8f, 5f, r)));
            }
            return d;
        }

        /// <summary>
        /// The creep. It cruises at a little under the speed you have been holding (an average over several
        /// seconds, so a slow patch costs you before it shows), is pulled back into view if you get far ahead,
        /// and never closes faster than a set creep, however slow you are. It has momentum: it speeds up and
        /// slows down gradually, so it never jumps at you.
        /// </summary>
        void Chase(float dt, Rider r)
        {
            float d = Mathf.Clamp01(Elapsed / 240f);                     // harder over four minutes
            // the pace you have been holding; a fall, and getting going again after it, do not count, so the
            // slide does not ease off just because you went down
            sinceDown = r.Crashed ? 0f : sinceDown + dt;
            if (sinceDown > 6f) ema = Mathf.Lerp(ema, Mathf.Max(0f, r.Vz), 1f - Mathf.Exp(-dt / 8f));
            float cruise = Mathf.Max(ema * Mathf.Lerp(0.93f, 0.985f, d), Mathf.Lerp(9f, 17f, d));
            float comfort = Mathf.Lerp(110f, 80f, d);                    // where it hangs back when you ride well
            float gap = Gap(r);
            float want = cruise + Mathf.Clamp((gap - comfort) * 0.12f, 0f, 14f);
            want = Mathf.Min(want, Mathf.Max(r.Vz, 0f) + Mathf.Lerp(CreepEasy, CreepHard, d));
            if (Elapsed < 8f) want = 8f;                                 // grace while you get going
            Speed = Mathf.MoveTowards(Speed, want, (want > Speed ? 5f : 9f) * dt);
            Z += Speed * dt;
        }
    }
}
