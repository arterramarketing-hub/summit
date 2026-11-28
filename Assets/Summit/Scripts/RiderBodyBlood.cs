using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Where the rider's crashes feed the blood (see Blood): the rider slamming into a tree or rock, the thrown
    /// body striking anything, the body sliding on after a hard hit on the snow (the drag trail), and the body in
    /// the air or come to rest (drips, a pool). Kept apart from RiderBody's
    /// riding code, which only hooks these up.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>The blood effects (set by the game; null leaves every crash clean).</summary>
        public Blood blood;

        /// <summary>The ragdoll as the test tools see it.</summary>
        public Ragdoll RagdollForTest => ragdoll;

        // the rider's last obstacle hit, held until we know it knocked them down (a shield, a tube's bounce or a
        // moment's invulnerability can save them, and then nothing bleeds)
        Obstacle pendingOb; float pendingInto; int pendingFrame = -1;

        void OnRiderHitObstacle(Obstacle ob, Vector3 centre, float into)
        {
            pendingOb = ob; pendingInto = into; pendingFrame = Time.frameCount;
        }

        void OnCrashBleed(string why)
        {
            if (pendingFrame != Time.frameCount || pendingOb == null) return;
            var ob = pendingOb; float into = pendingInto; pendingOb = null;
            if (why == null || !why.StartsWith(ob.label)) return;   // this fall wasn't that hit (a crevasse or ridge in the same frame)
            if (blood == null) return;
            var col = ob.HitBy(rider);   // (the part hit: a crag's or a cabin's first collider can be far off)
            if (col == null) return;
            // the rider's middle against the obstacle's surface: that is where the body met it
            Vector3 me = rider.transform.position + Vector3.up * 0.8f;
            Vector3 at = Obstacle.Nearest(col, me);
            Vector3 n = me - at; n.y *= 0.3f;
            if (n.sqrMagnitude < 1e-4f) n = -rider.Velocity;
            blood.Hit(at, n, into, false, rider.Velocity, ob.always ? null : col);   // (no marks on an animal)
        }

        void OnRagdollStruck(Collider other, Vector3 at, Vector3 normal, float into, Transform bone)
        {
            if (blood == null || bone == null || ragdoll == null) return;   // the board does not bleed
            var ob = other != null ? other.GetComponentInParent<Obstacle>() : null;
            blood.Hit(at, normal, into, ragdoll.IsSnow(other), ragdoll.Velocity, ob != null && ob.always ? null : other);   // (no marks on an animal)
        }

        void BleedAlong(Transform bone, Vector3 down, float press)
        {
            // the trail follows the middle of the body, as wide as it presses in
            if (blood == null || rig == null || (bone != rig.BodyBone && bone != rig.Hips)) return;
            blood.Slide(bone.position, ragdoll.Velocity, press);
        }

        /// <summary>Every frame: the body down after a hard crash drips between its bounces and pools where it stops.</summary>
        void BloodStep(float dt)
        {
            if (blood == null || ragdoll == null || !ragdoll.Active || !rider.Crashed || dt <= 0f) return;
            blood.Body(ragdoll.Centre, ragdoll.Velocity, dt);
        }
    }
}
