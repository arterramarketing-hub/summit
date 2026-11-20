using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What each hit does to the thrown body, for the medical receipt (Receipt): every time a part strikes
    /// something, which part, how hard into it, what it was (snow, ice down a crevasse, a tree, rock, steel, a
    /// cabin's wall, the bear), whether that hit broke the bone (RiderBody's own rule, which also bandages it),
    /// whether the head went in face first, and which side of the chest or pelvis took it. Hooked onto the
    /// ragdoll's Struck after the blood and the limbs have had theirs, so a limb torn off by the hit is already
    /// gone (LimbLost bills that). Kept apart from RiderBody's riding code, which only calls HookInjuries.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>A hit the thrown body took (Receipt.Hit).</summary>
        public System.Action<Injury> Injured;
        int breaksTold;
        bool injuryHooks;

        void HookInjuries()
        {
            if (injuryHooks || ragdoll == null) return;
            injuryHooks = true;
            ragdoll.Struck += OnInjury;
        }

        void OnInjury(Collider other, Vector3 at, Vector3 normal, float into, Transform bone)
        {
            if (rig == null || ragdoll == null || !ragdoll.Active || rider == null || !rider.Crashed) return;
            if (other != null && Ragdoll.Of(other) == ragdoll) return;   // (its own board or a part of itself)
            // the bones broken since the last report: this hit's (RiderBody breaks them on the same collision, just
            // before this), or one broken some other way (below)
            bool broke = Told(bone);
            if (bone == null || !ragdoll.Attached(bone)) return;   // the board; or torn off by this very hit
            var part = PartOf(bone);
            if (!part.HasValue) return;
            var inj = new Injury { part = part.Value, speed = into, on = SurfaceOf(other, at), broke = broke };
            // which way the body faces: across the shoulders and up the spine
            Vector3 across = rig.UpperR.position - rig.UpperL.position, up = rig.Head.position - ragdoll.Centre;
            Vector3 front = Vector3.Cross(across, up);
            Vector3 into3 = -normal;   // from the part into what it hit
            if (part.Value == BodyPart.Head && front.sqrMagnitude > 1e-6f) inj.face = Vector3.Dot(front.normalized, into3) > 0.5f;
            if ((part.Value == BodyPart.Chest || part.Value == BodyPart.Pelvis) && across.sqrMagnitude > 1e-6f)
            {
                float s = Vector3.Dot(across.normalized, into3);
                inj.side = s > 0.5f ? 1 : s < -0.5f ? -1 : 0;
            }
            Injured?.Invoke(inj);
        }

        /// <summary>
        /// Report the breaks not reported yet, and say whether `bone` was one of them. RiderBody breaks a bone on a
        /// hit (reported with it, by OnInjury) or, if a wipeout has broken nothing after a moment, one of its own
        /// choosing ("the landing did it"), which no hit carries: the game calls ReportBreaks each frame of a crash
        /// for those, so every bandage is on the bill.
        /// </summary>
        bool Told(Transform bone)
        {
            bool found = false;
            if (brokenThisCrash.Count < breaksTold) breaksTold = 0;
            while (brokenThisCrash.Count > breaksTold)
            {
                var b = brokenThisCrash[breaksTold++];
                if (b == bone && bone != null) { found = true; continue; }
                var bp = PartOf(b);
                if (bp.HasValue && ragdoll != null && ragdoll.Attached(b)) Injured?.Invoke(new Injury { part = bp.Value, speed = 0f, on = Surface.Snow, broke = true });
            }
            return found;
        }

        /// <summary>Each frame of a crash (the game): any break no hit has reported (see Told).</summary>
        public void ReportBreaks()
        {
            if (rig == null || ragdoll == null || !ragdoll.Active || rider == null || !rider.Crashed) return;
            if (brokenThisCrash.Count != breaksTold) Told(null);
        }

        /// <summary>Which part of the body this ragdoll bone is.</summary>
        BodyPart? PartOf(Transform b)
        {
            if (b == null || rig == null) return null;
            if (b == rig.Head) return BodyPart.Head;
            if (b == rig.Chest) return BodyPart.Chest;
            if (b == rig.BodyBone || b == rig.Hips) return BodyPart.Pelvis;
            if (b == rig.UpperL) return BodyPart.ArmL;
            if (b == rig.UpperR) return BodyPart.ArmR;
            if (b == rig.LowerL) return BodyPart.ForearmL;
            if (b == rig.LowerR) return BodyPart.ForearmR;
            if (b == rig.ThighL) return BodyPart.ThighL;
            if (b == rig.ThighR) return BodyPart.ThighR;
            if (b == rig.ShinL) return BodyPart.ShinL;
            if (b == rig.ShinR) return BodyPart.ShinR;
            return null;
        }

        /// <summary>What a collider is, as far as a body hitting it goes.</summary>
        Surface SurfaceOf(Collider c, Vector3 at)
        {
            if (c == null) return Surface.Snow;
            if (ragdoll.IsSnow(c)) return RagdollBelowSnow > 1f ? Surface.Ice : Surface.Snow;   // (down a crevasse: its walls)
            var ob = c.GetComponentInParent<Obstacle>();
            if (ob != null)
            {
                switch (ob.label)
                {
                    case "Tree": return Surface.Tree;
                    case "Rock": return Surface.Rock;
                    case "Bear!": return Surface.Bear;
                    case "Cabin!": case "Lift station": return Surface.Wood;
                    default: return Surface.Metal;   // a lift tower, a lamp post, a car
                }
            }
            string n = c.name.ToLowerInvariant();
            if (n.Contains("rail") || n.Contains("box") || n.Contains("cable") || n.Contains("chair") || n.Contains("pole") || n.Contains("tower") || n.Contains("pipe"))
                return Surface.Metal;
            return Surface.Wood;   // fences, roofs, anything else built
        }

        /// <summary>For the test tools: the bone of a part (to drive hits through OnInjury as the ragdoll would).</summary>
        public Transform BoneOf(BodyPart p)
        {
            if (rig == null) return null;
            switch (p)
            {
                case BodyPart.Head: return rig.Head;
                case BodyPart.Chest: return rig.Chest;
                case BodyPart.Pelvis: return rig.BodyBone != null ? rig.BodyBone : rig.Hips;
                case BodyPart.ArmL: return rig.UpperL;
                case BodyPart.ArmR: return rig.UpperR;
                case BodyPart.ForearmL: return rig.LowerL;
                case BodyPart.ForearmR: return rig.LowerR;
                case BodyPart.ThighL: return rig.ThighL;
                case BodyPart.ThighR: return rig.ThighR;
                case BodyPart.ShinL: return rig.ShinL;
                default: return rig.ShinR;
            }
        }
    }
}
