using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The slide behind you. It creeps: it cruises a little slower than you have been riding, so a clean run
    /// slowly pulls away from it, but it never drops far behind, and it keeps coming at its own pace while you
    /// are slow or down in the snow. So falls and slow sections are what let it catch up.
    ///
    /// It does not kill on contact. When it reaches you it first flows under the board (the snow heaves, the
    /// edge stops biting, you are carried and thrown about), then knocks you off, carries the body on in the
    /// debris, and buries it as the slide runs out and settles. The game ends the run once that is over (Done).
    /// The look of all this is AvalancheView.
    /// </summary>
    public class Avalanche
    {
        public const string Cause = "Avalanche!";
        public enum Stage { Chasing, Undercut, Carried, Buried }

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
        public bool Caught => Phase != Stage.Chasing;

        // how long it flows under the board before it knocks you off, and how long the camera holds on the grave
        public static float UndercutTime = 1.7f, HoldTime = 2.6f;
        // how it creeps: the most it ever closes on you (m/s), early on and after four minutes
        public static float CreepEasy = 6f, CreepHard = 8.5f;

        float ema, sinceDown = 99f;
        bool buryLocked;

        public void Reset(float riderZ)
        {
            Z = riderZ - 120f - Progress.HeadStart;   // the early-warning upgrade buys a longer lead
            Speed = 8f;
            ema = 0f; sinceDown = 99f;
            Elapsed = 0f;
            Phase = Stage.Chasing; PhaseTime = 0f;
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
        public void ForceGap(Rider r, float gap, float speed) { Z = r.Z - gap; Speed = speed; ema = Mathf.Max(ema, speed); Elapsed = Mathf.Max(Elapsed, 10f); }

        void Enter(Stage s) { Phase = s; PhaseTime = 0f; }

        public void Step(float dt, Rider r, RiderBody body)
        {
            if (dt <= 0f) return;
            Elapsed += dt; PhaseTime += dt;
            switch (Phase)
            {
                case Stage.Chasing:
                    Chase(dt, r);
                    float gap = Gap(r);
                    Shake = Mathf.Pow(Mathf.Clamp01(1f - gap / 55f), 2f) * 0.9f;
                    if (gap <= 0f)
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
                    Shake = 1f;
                    if (!r.Crashed && r.Running && !r.Body.isKinematic)
                    {
                        // carried: the moving snow takes the board along with it, whatever you do
                        var v = r.Body.linearVelocity;
                        float want = Speed * 0.92f;
                        if (v.z < want) v.z += (want - v.z) * Mathf.Min(1f, 2.2f * dt);
                        r.Body.linearVelocity = v;
                    }
                    if (r.Crashed) StartCarry(r, body);
                    else if (PhaseTime >= UndercutTime)
                    {
                        // the cloud is on you now: the board goes out from under you
                        r.Undercut = 0f;
                        r.DoCrash(Cause);
                        StartCarry(r, body);
                    }
                    break;

                case Stage.Carried:
                    // the slide runs out: it slows to a stop over a few seconds, with the body tumbling in it
                    Speed = Mathf.MoveTowards(Speed, 0f, Mathf.Lerp(3.5f, 7f, Mathf.Clamp01(PhaseTime / 3f)) * dt);
                    Z += Speed * dt;
                    Churn = Mathf.MoveTowards(Churn, 0f, dt);
                    Shake = 0.9f * Mathf.Clamp01(Speed / Mathf.Max(CaughtSpeed, 1f)) + 0.1f;
                    Vector3 c = body != null ? body.RagdollCentre : r.transform.position;
                    if (body != null && Speed > 0.3f)
                    {
                        Vector3 n = Slope.Normal(c.x, c.z);
                        Vector3 down = Vector3.ProjectOnPlane(Vector3.forward, n).normalized;
                        // carried along in the debris, kept up near its surface and rolled over and over
                        body.Carry(down * Speed * 0.9f + n * Mathf.Clamp01(Speed / 8f) * 1.2f, 2.6f, dt);
                    }
                    // the pile follows the body until it stops, then stays put
                    if (!buryLocked)
                    {
                        var at = new Vector3(c.x, Slope.DrawnHeight(c.x, c.z), c.z);
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
            r.Undercut = 0f;
            r.StayDown = true;   // nobody gets up from this
            Churn = 1f;
            var c = body != null ? body.RagdollCentre : r.transform.position;
            BuryAt = new Vector3(c.x, Slope.DrawnHeight(c.x, c.z), c.z);
            Enter(Stage.Carried);
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
