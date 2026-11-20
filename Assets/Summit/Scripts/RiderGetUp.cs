using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Where you get up after a fall, and never being left pinned against something.
    ///
    /// Getting up used to look only at the spot itself (nothing solid within 1.4 m of the board) and then set you off
    /// straight down the fall line. In a hamlet that line runs into the next cabin's wall, deck, stair or car a few
    /// metres below: riding into it slowly is only a scrape, the slope holds you against it, and at a standstill there
    /// is no speed to turn away with, so the rider sat against the wall at 1 km/h with no way off. (Its quick look
    /// also shared a 16-collider buffer with the thrown body lying right there, its parts, board, skis and slabs of
    /// snow, which could fill it before anything beyond was seen.)
    ///
    /// Now a spot to get up on must have room for the whole standing rider, GetUpMargin clear of anything solid, and an
    /// open lane LaneLength down the fall line to ride away along; the nearest such spot is found toward the piste
    /// first. And a rider who does end up pinned (slow, on the snow, touching something solid for PinnedAfter) is
    /// eased across to the nearest such spot, as if sliding off it.
    /// Kept apart from Rider's riding code, which only calls GetUpSpot (Recover), UnpinStep (Tick) and EndUnpin (reset).
    /// </summary>
    public partial class Rider
    {
        public static float GetUpMargin = 1.2f;   // m from the rider's middle to anything solid, head to toe
        public static float LaneLength = 12f;     // m of open snow down the fall line to ride away along (enough to get going and turn)
        public static float PinnedAfter = 0.8f;   // s slow and touching something solid before you are eased free

        /// <summary>Off only for the test tools' before-and-after numbers: the old look round, and no easing free.</summary>
        public static bool SafeGetUp = true;

        static readonly Collider[] standBuf = new Collider[64];   // (big: the thrown body and its gear lie right there)

        /// <summary>Something solid you could be stuck against (not the snow, the thrown body or its gear, nor yourself).</summary>
        bool SolidForRider(Collider c)
        {
            if (c == null || c.isTrigger || c == ownCol) return false;
            var a = c.attachedRigidbody;
            if (a != null && (a == rb || !a.isKinematic)) return false;
            if (Ragdoll.Of(c) != null || Ragdoll.IsSlab(c)) return false;
            return c.gameObject.layer != CourseDressing.IceLayer;   // (a crevasse's ice is under the snow's level)
        }

        /// <summary>Is there anything solid in an upright column round this point on the snow, up to head height?</summary>
        bool Crowded(float x, float z, float radius)
        {
            float g = Slope.DrawnHeight(x, z);
            int k = Physics.OverlapCapsuleNonAlloc(new Vector3(x, g + 0.3f, z), new Vector3(x, g + 1.6f, z), radius, standBuf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++) if (SolidForRider(standBuf[i])) return true;
            return false;
        }

        public static float HamletClear = 15f;    // m from a hamlet's buildings, cars and lamps that counts as out of it

        /// <summary>Within HamletClear of a hamlet's buildings (Hamlets)?</summary>
        bool AmongBuildings(float x, float z)
        {
            float g = Slope.DrawnHeight(x, z);
            int k = Physics.OverlapSphereNonAlloc(new Vector3(x, g + 1f, z), HamletClear, standBuf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++)
                if (!standBuf[i].isTrigger && standBuf[i].GetComponentInParent<Hamlets>() != null) return true;
            return false;
        }

        /// <summary>Room to stand up here and ride away down the fall line.</summary>
        bool RoomToRide(float x, float z)
        {
            if (!Rideable(x, z) || Crowded(x, z, GetUpMargin)) return false;
            for (float s = 2.5f; s <= LaneLength; s += 2.5f)
                if (Crowded(x, z + s, 0.9f)) return false;
            return true;
        }

        /// <summary>
        /// The nearest spot to `p` with room to get up and ride away, across the slope or a little further down it:
        /// toward the piste, back onto it if need be, and the other way only if there is room within a few metres (a
        /// rider down in a hamlet is better back on the run than further in among the cabins). Never up a cliff's face
        /// or over a crevasse (Rideable). Failing all that, the middle of the piste.
        /// </summary>
        Vector3 GetUpSpot(Vector3 p, bool notHere = false)
        {
            if (!SafeGetUp) return OldGetUpSpot(p);
            float toward = Mathf.Sign(Slope.Centre(p.z) - p.x + 0.01f);
            // down among a hamlet's cabins: back toward the piste, clear of them all, so the way on is open snow
            if (AmongBuildings(p.x, p.z))
                for (int i = 1; i <= 60; i++)
                    foreach (float dz in new[] { 0f, 3f, 6f })
                    {
                        float x = p.x + toward * 1.5f * i, z = p.z + dz;
                        if (!AmongBuildings(x, z) && RoomToRide(x, z)) return new Vector3(x, p.y, z);
                    }
            if (!notHere && RoomToRide(p.x, p.z)) return p;
            for (int i = 1; i <= 60; i++)
                foreach (float side in new[] { toward, -toward })
                {
                    if (side != toward && i > 4) continue;   // (away from the piste: only just beside where you fell)
                    foreach (float dz in new[] { 0f, 3f, 6f })
                    {
                        float x = p.x + side * 1.5f * i, z = p.z + dz;
                        if (RoomToRide(x, z)) return new Vector3(x, p.y, z);
                    }
                }
            for (float dz = 0f; dz <= 40f; dz += 5f)
            {
                float z = p.z + dz, x = Slope.Centre(z);
                if (RoomToRide(x, z)) return new Vector3(x, p.y, z);
            }
            return new Vector3(Slope.Centre(p.z), p.y, p.z);
        }

        /// <summary>How many times a pinned rider has been eased free (for the test tools).</summary>
        public static int Unpinned;
        /// <summary>The way it was (SafeGetUp off): nothing with an Obstacle within 1.4 m of the board, in a 16-collider look.</summary>
        Vector3 OldGetUpSpot(Vector3 p)
        {
            bool Blocked(Vector3 q)
            {
                int k = Physics.OverlapSphereNonAlloc(q, 1.4f, near, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < k; i++)
                    if (near[i].attachedRigidbody != rb && near[i].GetComponentInParent<Obstacle>() != null) return true;
                return false;
            }
            float toward = Mathf.Sign(Slope.Centre(p.z) - p.x + 0.01f);
            if (!Blocked(p) && Rideable(p.x, p.z)) return p;
            for (int i = 1; i <= 40; i++)
                foreach (float side in new[] { toward, -toward })
                    foreach (float dz in new[] { 0f, 3f, 6f })
                    {
                        var q = new Vector3(p.x + side * 1.5f * i, p.y, p.z + dz);
                        if (Rideable(q.x, q.z) && !Blocked(q)) return q;
                    }
            return p;
        }

        float pinnedFor, unpinLeft;
        Vector3 unpinTo;

        /// <summary>A new run, or a crash: no easing under way.</summary>
        void EndUnpin()
        {
            pinnedFor = 0f;
            if (unpinLeft > 0f) { unpinLeft = 0f; if (!Crashed) rb.detectCollisions = true; }
        }

        /// <summary>
        /// Each step on the snow: pinned against something, slow, for PinnedAfter? Then slide across to open snow over a
        /// moment (nothing in the way while you do), facing down the hill. True while easing: v is this step's velocity.
        /// </summary>
        bool UnpinStep(float dt, Vector3 p, ref Vector3 v)
        {
            if (!Running || Crashed) { EndUnpin(); return false; }
            if (unpinLeft > 0f)
            {
                unpinLeft -= dt;
                Vector3 to = unpinTo - p; to.y = 0f;
                Vector3 h = to / Mathf.Max(unpinLeft, dt);
                float hereY = Slope.DrawnHeight(p.x, p.z) + R;
                if (Mathf.Abs(p.y - hereY) > 1e-4f) { p.y = hereY; rb.position = p; }
                float wantY = Slope.DrawnHeight(p.x + h.x * dt, p.z + h.z * dt) + R;
                v = new Vector3(h.x, (wantY - hereY) / dt, h.z);
                Speed = h.magnitude;
                if (unpinLeft <= 0f)
                {
                    // free: on your way down the hill again
                    rb.detectCollisions = true;
                    Heading = 0f; Lean = 0f; leanVel = 0f; groundVy = 0f; fixVy = 0f;
                    v = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(p.x, p.z)).normalized * 3f;
                    invulnerable = Mathf.Max(invulnerable, 0.8f);
                }
                return true;
            }
            if (!SafeGetUp || Airborne || Grinding || Speed > 1f) { pinnedFor = 0f; return false; }
            // touching something solid, board to shoulders?
            int k = Physics.OverlapCapsuleNonAlloc(p, p + Vector3.up * 1.2f, R + 0.25f, standBuf, ~0, QueryTriggerInteraction.Ignore);
            bool touching = false;
            for (int i = 0; i < k && !touching; i++) touching = SolidForRider(standBuf[i]);
            if (!touching) { pinnedFor = 0f; return false; }
            pinnedFor += dt;
            if (pinnedFor < PinnedAfter) return false;
            pinnedFor = 0f;
            unpinTo = GetUpSpot(p, true);
            float far = new Vector2(unpinTo.x - p.x, unpinTo.z - p.z).magnitude;
            unpinLeft = Mathf.Clamp(far / 5f, 0.35f, 1.2f);   // a quick slide across, not a jump
            rb.detectCollisions = false;
            Unpinned++;
            return false;
        }
    }
}
