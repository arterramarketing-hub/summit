using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Every fall ends. The game waits for the thrown body to read as still (RagdollMoving) before it offers the tap to
    /// get up or, on the last fall, ends the run; a body pinned inside something, or twitching on the spot against it,
    /// never did, and the player was left watching it. This watches each fall and steps in:
    /// - pinned more than PinnedDeep inside something solid for a moment: lifted clear (Ragdoll.Unwedge);
    /// - reading as moving while going nowhere for RestlessFor: slowed hard, then laid down where it is (Ragdoll.Settle);
    /// - still creeping along (under CreepMax) SettleAfter seconds after the crash, on something: the same.
    /// An honest tumble, slide or fall is never touched, and nothing an avalanche is carrying.
    /// Kept apart from RiderBody's riding code, which only calls ResetSettle and SettleStep.
    /// </summary>
    public partial class RiderBody
    {
        public static float PinnedDeep = 0.12f;    // m: deeper than this inside a wall, a rock or a car is stuck in it, not lying against it
        public static float RestlessFor = 0.6f;    // s reading as moving (RagdollMoving's 2.5 m/s) while getting nowhere
        public static float SettleAfter = 8f;      // s after the crash: anything still moving by then is settled (a long slide has stopped well before)
        public static float CreepMax = 8f;         // m/s: faster than this, long after the crash, is a real tumble down a face
        const float Moving = 2.5f;                 // m/s: what RagdollMoving calls moving (the game waits below it)

        readonly Vector3[] settleTrail = new Vector3[120], boardTrail = new Vector3[120];   // the pelvis and the board, the last second of steps
        int trailAt, trailCount, settleTick;
        float pinnedFor, restless, settling;

        /// <summary>A new fall: nothing watched yet.</summary>
        void ResetSettle() { trailAt = 0; trailCount = 0; settleTick = 0; pinnedFor = 0f; restless = 0f; settling = 0f; }

        /// <summary>Each physics step of a crash, once the body has moved for this step.</summary>
        void SettleStep()
        {
            if (ragdoll == null || !ragdoll.Active || !rider.Crashed || ragdoll.Frozen || !Ragdoll.Unstick) return;
            float dt = Time.fixedDeltaTime;
            Vector3 c = ragdoll.Centre, bc = ragdoll.BoardCentre;
            // how far the body, or the board, has got in the last second (either one sliding along is getting somewhere)
            bool full = trailCount == settleTrail.Length;
            float got = full ? Mathf.Max((c - settleTrail[trailAt]).magnitude, (bc - boardTrail[trailAt]).magnitude) : float.MaxValue;
            settleTrail[trailAt] = c; boardTrail[trailAt] = bc;
            trailAt = (trailAt + 1) % settleTrail.Length; trailCount = Mathf.Min(trailCount + 1, settleTrail.Length);

            // pinned inside something: lift it clear (looked at a few times a second: it is a handful of queries)
            if (++settleTick % 6 == 0)
            {
                float deep = ragdoll.Embedded(out var way);
                pinnedFor = deep > PinnedDeep ? pinnedFor + 6f * dt : 0f;
                if (pinnedFor > 0.3f)
                {
                    pinnedFor = 0f;
                    var away = new Vector3(way.x, 0f, way.z);
                    if (away.sqrMagnitude < 1e-4f) away = new Vector3(-lastVel.x, 0f, -lastVel.z);
                    if (away.sqrMagnitude < 1e-4f) away = Vector3.back;
                    ragdoll.Unwedge(away.normalized, false);
                }
            }

            // twitching on the spot: fast enough to hold the ending back, getting nowhere
            bool fast = ragdoll.Speed > Moving;
            restless = fast && got < 0.6f ? restless + dt : 0f;
            // still creeping along long after any slide would have stopped, on something (never stopped in mid-air, nor
            // tumbling down a face: that is a fall, and it ends where it lands)
            bool aloft = c.y - Slope.DrawnHeight(c.x, c.z) > 1.5f;
            bool overdue = fast && ragdoll.Speed < CreepMax && crashClock > SettleAfter && !aloft;
            if (settling > 0f || restless > RestlessFor || overdue)
            {
                settling += dt;
                ragdoll.Settle(dt, settling > 1f);   // a second of slowing, then still
            }
        }
    }
}
