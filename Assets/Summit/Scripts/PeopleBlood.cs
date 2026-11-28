using UnityEngine;

namespace Summit
{
    /// <summary>
    /// (rides2) The people the rider knocks over (MountainRiders, LiftEnds) bleed as the rider does in a crash (Blood,
    /// RiderBodyBlood): where they are hit, where their thrown body strikes the snow or a tree, the drag trail as it
    /// slides on, the drips and the pool where it stops. The same thresholds as the rider's own, so a slow knock stays
    /// clean and a car or a bear at speed does not; nothing at all with the pause menu's BLOOD off.
    /// </summary>
    public static class PeopleBlood
    {
        /// <summary>The game's blood (null: every knock stays clean).</summary>
        public static Blood blood;

        /// <summary>A thrown body's hits and its slide feed the blood (once, when its ragdoll is made).</summary>
        public static void Hook(Ragdoll rag, RiderRig rig)
        {
            if (rag == null) return;
            rag.Struck += (other, at, normal, into, bone) =>
            {
                if (blood == null || bone == null) return;   // (the token in their hands does not bleed)
                var ob = other != null ? other.GetComponentInParent<Obstacle>() : null;
                blood.Hit(at, normal, into, rag.IsSnow(other), rag.Velocity, ob != null && (ob.always || ob.person != null) ? null : other);
            };
            rag.Ploughed += (bone, down, press) =>
            {
                if (blood == null || rig == null || (bone != rig.BodyBone && bone != rig.Hips)) return;
                blood.Slide(bone.position, rag.Velocity, press);
            };
        }

        /// <summary>Run down: the spray where they were hit, thrown on the way they go (nothing marked on the car or the bear).</summary>
        public static void Hit(Vector3 at, Vector3 way, float into)
        {
            if (blood == null) return;
            way.y = 0f;
            Vector3 n = way.sqrMagnitude > 1e-4f ? way.normalized + Vector3.up * 0.4f : Vector3.up;
            blood.Hit(at, n, into, false, way, null);
        }

        /// <summary>Every frame a thrown body is down: drips between its bounces, a pool where it comes to rest.</summary>
        public static void Body(Ragdoll rag, float dt)
        {
            if (blood == null || rag == null || !rag.Active || dt <= 0f) return;
            blood.Body(rag.Centre, rag.Velocity, dt);
        }
    }
}
