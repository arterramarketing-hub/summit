using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Poses for the camp's fire and kitchen that the hero's clips don't have, made on top of the standing idle by
    /// moving the whole model and placing the limbs (HeroRig.ArmTo / LegTo): sitting on a stump, holding a roasting
    /// stick, holding a mug. Shared by the hero (Camp, through RiderBody.CampPose) and by Alyssa.
    /// </summary>
    public static class CampPoses
    {
        /// <summary>Turn the model to face `face` and set its hips just above the seat (a stump's top).</summary>
        public static void Seat(HeroRig h, Transform model, Vector3 seatTop, Vector3 face)
        {
            Vector3 f = h.HipsForward();
            if (f.sqrMagnitude > 1e-4f) model.rotation = Quaternion.FromToRotation(f, face) * model.rotation;
            model.position += seatTop + Vector3.up * 0.09f - face * 0.04f - h.Hips.position;
        }

        /// <summary>Stand the model at `at` on the floor (`floorY`), facing `face`.</summary>
        public static void Stand(HeroRig h, Transform model, Vector3 at, Vector3 face)
        {
            Vector3 f = h.HipsForward();
            if (f.sqrMagnitude > 1e-4f) model.rotation = Quaternion.FromToRotation(f, face) * model.rotation;
            Vector3 root = h.Root.position;
            model.position += new Vector3(at.x - root.x, at.y - root.y, at.z - root.z);
        }

        /// <summary>Legs folded in front of the seat, feet flat on the ground at `groundY`.</summary>
        public static void FoldLegs(HeroRig h, Vector3 face, float groundY, float scale)
        {
            Vector3 right = Vector3.Cross(Vector3.up, face), hips = h.Hips.position;
            for (int sd = 0; sd < 2; sd++)
            {
                float side = sd == 0 ? -1f : 1f;
                Vector3 a = hips + face * 0.42f * scale + right * side * 0.16f * scale; a.y = groundY + 0.09f * scale;
                h.LegTo(sd, a, hips + face * 0.9f + Vector3.up * 0.5f + right * side * 0.1f);
            }
        }

        /// <summary>
        /// Where the right hand holds the roasting stick: low by the right knee with the forearm resting on it while
        /// it's out over the fire (lift 0), up in front of the chest to look at it or blow on it (lift 1).
        /// </summary>
        public static Vector3 StickHand(HeroRig h, float lift, float scale)
        {
            Vector3 face = h.HipsForward(), right = Vector3.Cross(Vector3.up, face);
            Vector3 knee = h.Hips.position + face * 0.4f * scale + right * 0.13f * scale + Vector3.up * 0.3f * scale;
            Vector3 chest = h.Chest.position + face * 0.28f * scale + right * 0.1f * scale - Vector3.up * 0.1f * scale;
            return Vector3.Lerp(knee, chest, lift);
        }

        /// <summary>
        /// The way the stick points: from the knee down at a spot over the fire; tipped up nearly upright to look at the
        /// marshmallow at eye height (lift); or brought round toward the mouth to blow on it (blow, with lift). A stick
        /// is far longer than an arm, so this is how it's done: the end comes up, not in.
        /// </summary>
        public static Vector3 StickAim(HeroRig h, Vector3 overFire, float lift, float blow, float scale)
        {
            Vector3 face = h.HipsForward(), right = Vector3.Cross(Vector3.up, face);
            Vector3 low = (overFire - StickHand(h, 0f, scale)).normalized;
            Vector3 up = (face * 0.55f + Vector3.up * 0.83f - right * 0.1f).normalized;
            Vector3 toMouth = (face * 0.75f + Vector3.up * 0.62f - right * 0.28f).normalized;
            return Vector3.Slerp(low, Vector3.Slerp(up, toMouth, blow), lift);
        }

        /// <summary>The stick upright beside you, held loosely (between roasts, or sitting down).</summary>
        public static Vector3 StickRest(HeroRig h)
        {
            Vector3 face = h.HipsForward(), right = Vector3.Cross(Vector3.up, face);
            return (face * 0.35f + Vector3.up * 0.9f + right * 0.15f).normalized;
        }

        /// <summary>The stick in the right hand, pointing along `aim`, turning slowly as it roasts. Returns where the marshmallow is.</summary>
        public static Vector3 HoldStick(HeroRig h, Marshmallow mm, Vector3 aim, float turn)
        {
            var hand = h.Hand[1].position;
            mm.Stick.position = hand;
            if (aim.sqrMagnitude > 1e-6f) mm.Stick.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);
            mm.Tip.localRotation = Quaternion.Euler(0f, 0f, turn);
            return mm.Tip.position;
        }

        /// <summary>A point by the mouth (the neck warmer comes up to the goggles: just in front of it).</summary>
        public static Vector3 Mouth(HeroRig h, Vector3 face, float scale) => h.Head.position + face * 0.13f * scale - Vector3.up * 0.05f * scale;
    }
}
