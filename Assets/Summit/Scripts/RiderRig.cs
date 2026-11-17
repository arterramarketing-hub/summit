using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Poses a Quaternius-style skeleton (Hips/Abdomen/Torso/Chest/Neck/Head, UpperArm/LowerArm/Wrist,
    /// UpperLeg/LowerLeg with separate Foot bones) into a snowboard stance, every frame, from what the
    /// rider is doing. Bones are aimed in the rider's own frame, so it does not depend on how the
    /// file's bones are oriented. Feet are planted on the bindings and the legs solved with IK.
    /// </summary>
    public class RiderRig
    {
        Transform model, body, hips, abdomen, torso, chest, neck, head;
        Transform uaL, laL, wrL, uaR, laR, wrR, ulL, llL, endL, ulR, llR, endR, ftL, ftR;
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<Transform, Quaternion> footRel = new Dictionary<Transform, Quaternion>();
        Vector3 bodyBindPos;
        float thighLen, shinLen, hipHeight;
        public bool Ok { get; private set; }

        // smoothed pose values
        float sCrouch, sToe, sTuck, sArmL, sArmR, sTwist;

        public RiderRig(Transform modelRoot)
        {
            model = modelRoot;
            var all = new Dictionary<string, Transform>();
            foreach (var t in modelRoot.GetComponentsInChildren<Transform>(true)) if (!all.ContainsKey(t.name)) all[t.name] = t;
            Transform F(string n) => all.TryGetValue(n, out var t) ? t : null;
            body = F("Body"); hips = F("Hips"); abdomen = F("Abdomen"); torso = F("Torso"); chest = F("Chest"); neck = F("Neck"); head = F("Head");
            uaL = F("UpperArm.L"); laL = F("LowerArm.L"); wrL = F("Wrist.L"); uaR = F("UpperArm.R"); laR = F("LowerArm.R"); wrR = F("Wrist.R");
            ulL = F("UpperLeg.L"); llL = F("LowerLeg.L"); endL = F("LowerLeg.L_end"); ulR = F("UpperLeg.R"); llR = F("LowerLeg.R"); endR = F("LowerLeg.R_end");
            ftL = F("Foot.L"); ftR = F("Foot.R");
            Ok = hips != null && chest != null && head != null && uaL != null && laL != null && wrL != null && uaR != null && laR != null && wrR != null
                 && ulL != null && llL != null && endL != null && ulR != null && llR != null && endR != null && ftL != null && ftR != null;
        }

        public Transform Head => head;
        public Transform HandL => wrL;
        public Transform HandR => wrR;
        public Transform UpperL => uaL; public Transform LowerL => laL; public Transform UpperR => uaR; public Transform LowerR => laR;
        public Transform ThighL => ulL; public Transform ThighR => ulR;
        public Transform Hips => hips; public Transform BodyBone => body; public Transform Chest => chest; public Transform Neck => neck;
        public Transform ShinL => llL; public Transform ShinR => llR; public Transform EndL => endL; public Transform EndR => endR;
        public Transform FootL => ftL; public Transform FootR => ftR;
        public Transform Root => model;

        readonly Dictionary<Transform, Vector3> bindPos = new Dictionary<Transform, Vector3>();

        /// <summary>Put every bone back exactly where it was at rest (after a ragdoll has thrown them about).</summary>
        public void RestoreBind()
        {
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            foreach (var kv in bindPos) kv.Key.localPosition = kv.Value;
        }

        /// <summary>Call once the model is placed, scaled and turned side-on: records the rest pose.</summary>
        public void CaptureBind()
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) { bind[t] = t.localRotation; if (t != model) bindPos[t] = t.localPosition; }
            if (body != null) bodyBindPos = body.localPosition;
            thighLen = Vector3.Distance(ulL.position, llL.position);
            shinLen = Vector3.Distance(llL.position, endL.position);
            hipHeight = (ulL.position.y + ulR.position.y) * 0.5f - Mathf.Min(ftL.position.y, ftR.position.y);
            footRel[ftL] = Quaternion.Inverse(model.rotation) * ftL.rotation;
            footRel[ftR] = Quaternion.Inverse(model.rotation) * ftR.rotation;
        }

        static void Aim(Transform bone, Transform child, Vector3 dir)
        {
            Vector3 cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-8f || dir.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        static void Turn(Transform bone, float deg, Vector3 axis)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(deg, axis) * bone.rotation;
        }

        /// <summary>
        /// frame: the board's transform (its +Z is the nose, +X the toe edge, +Y up off the deck).
        /// bindL / bindR: world positions of the ankles on the bindings.
        /// </summary>
        public void Pose(Transform frame, Vector3 bindL, Vector3 bindR, float dt,
                         float crouch, float toe, float tuck, bool grab, bool flip, bool idle, bool crashed, float t)
        {
            if (!Ok) return;
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            if (body != null) body.localPosition = bodyBindPos;

            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * 11f);
            sCrouch = Mathf.Lerp(sCrouch, crouch, k);
            sToe = Mathf.Lerp(sToe, toe, k);
            sTuck = Mathf.Lerp(sTuck, tuck, k);

            Vector3 up = frame.up, fwd = frame.right, nose = frame.forward;   // the rider faces the toe edge; the left side leads
            Vector3 bendAxis = Vector3.Cross(up, fwd);                      // rotating about this tips the top toward fwd

            // hips drop as the knees bend; toe-side turns push them forward, heel-side sits them back
            float drop = hipHeight * Mathf.Lerp(0.06f, 0.36f, Mathf.Clamp01(sCrouch));
            Vector3 hipShift = -up * drop + fwd * (0.08f * sToe - 0.04f * Mathf.Max(0f, -sToe));
            if (body != null) body.position += hipShift; else hips.position += hipShift;

            // spine: fold forward with the crouch and tuck, lean into toe-side turns, shoulders open toward the nose
            float fold = Mathf.Lerp(8f, 38f, sTuck) + sCrouch * 12f + Mathf.Max(0f, sToe) * 18f - Mathf.Max(0f, -sToe) * 6f;
            Turn(abdomen, fold * 0.35f, bendAxis);
            Turn(torso, fold * 0.35f, bendAxis);
            Turn(chest, fold * 0.3f, bendAxis);
            float twist = 28f + sToe * 14f;   // turn the chest toward the nose (the left side)
            Turn(chest, -twist, up);
            if (neck != null) Turn(neck, -18f, up);
            Turn(head, idle ? -20f - 25f * Mathf.Sin(t * 0.5f) : -32f - sToe * 6f, up);
            Turn(head, -fold * 0.4f, bendAxis);   // keep the eyes up while the back folds

            // arms: lead arm out over the nose, rear arm low and relaxed; a hand reaches the board on a grab
            Vector3 down = -up;
            Vector3 leadUpper = (nose * 0.8f + down * 0.55f + fwd * 0.2f).normalized;
            Vector3 leadLower = (nose * 0.9f + down * 0.25f + fwd * 0.3f).normalized;
            Vector3 rearUpper = (-nose * 0.35f + down * 0.9f + fwd * 0.15f).normalized;
            Vector3 rearLower = (down * 0.7f + fwd * 0.5f - nose * 0.1f).normalized;
            if (sTuck > 0.01f)
            {
                leadUpper = Vector3.Slerp(leadUpper, (down * 0.8f + fwd * 0.6f).normalized, sTuck);
                leadLower = Vector3.Slerp(leadLower, (fwd * 0.9f - up * 0.2f + nose * 0.2f).normalized, sTuck);
                rearUpper = Vector3.Slerp(rearUpper, (down * 0.8f + fwd * 0.6f).normalized, sTuck);
                rearLower = Vector3.Slerp(rearLower, (fwd * 0.9f - up * 0.2f - nose * 0.2f).normalized, sTuck);
            }
            float sway = Mathf.Sin(t * 1.4f) * 0.06f;
            leadUpper = (leadUpper + up * sway).normalized;
            if (grab)
            {
                // rear hand down to the toe edge between the feet
                rearUpper = (down * 0.85f + fwd * 0.5f).normalized;
                rearLower = (down * 0.9f + fwd * 0.2f).normalized;
                leadUpper = (nose * 0.6f + up * 0.5f + fwd * 0.2f).normalized;
                leadLower = (nose * 0.7f + up * 0.6f).normalized;
            }
            if (crashed)
            {
                leadUpper = (up * Mathf.Sin(t * 9f) + nose * Mathf.Cos(t * 7f) + fwd * 0.3f).normalized;
                rearUpper = (up * Mathf.Sin(t * 8f + 1f) - nose * Mathf.Cos(t * 6f) + fwd * 0.3f).normalized;
                leadLower = leadUpper; rearLower = rearUpper;
            }
            // the left arm leads (left side is toward the nose)
            Aim(uaL, laL, leadUpper); Aim(laL, wrL, leadLower);
            Aim(uaR, laR, rearUpper); Aim(laR, wrR, rearLower);

            // feet on the bindings, legs solved to reach them, knees out over the toe edge
            if (flip || grab)
            {
                // knees tucked up: pull the bindings toward the hips
                Vector3 hipMid = (ulL.position + ulR.position) * 0.5f;
                bindL = Vector3.Lerp(bindL, hipMid, 0.25f); bindR = Vector3.Lerp(bindR, hipMid, 0.25f);
            }
            PlaceFoot(ftL, bindL, frame, 10f);
            PlaceFoot(ftR, bindR, frame, -10f);
            Leg(ulL, llL, endL, bindL, fwd + nose * 0.25f);
            Leg(ulR, llR, endR, bindR, fwd - nose * 0.25f);
        }

        /// <summary>The bind-pose height of the hips above the feet, in metres.</summary>
        public float HipHeight => hipHeight;

        /// <summary>
        /// Sitting in a snow tube: hips dropped into the ring, leaning back, legs out over the front of it,
        /// hands on the handles. frame: +X is where the rider faces, +Y up. lean: -1..1 weight shift.
        /// </summary>
        public void PoseSit(Transform frame, float seatDrop, Vector3 footL, Vector3 footR, Vector3 handL, Vector3 handR,
                            float lean, float dt, bool crashed, float t)
        {
            if (!Ok) return;
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            if (body != null) body.localPosition = bodyBindPos;
            Vector3 up = frame.up, fwd = frame.right;
            Vector3 bendAxis = Vector3.Cross(up, fwd);
            Vector3 shift = -up * seatDrop - fwd * 0.08f;
            if (body != null) body.position += shift; else hips.position += shift;
            // lean back against the tube, a weight shift to the side to steer, head up looking down the hill
            float back = -24f;
            Turn(abdomen, back * 0.4f, bendAxis); Turn(torso, back * 0.35f, bendAxis); Turn(chest, back * 0.25f, bendAxis);
            Turn(chest, lean * 16f, fwd);
            Turn(head, -back * 0.7f + 4f * Mathf.Sin(t * 1.7f), bendAxis);
            if (crashed)
            {
                Aim(uaL, laL, (up * Mathf.Sin(t * 9f) + fwd * 0.4f).normalized); Aim(laL, wrL, (up + fwd).normalized);
                Aim(uaR, laR, (up * Mathf.Sin(t * 8f + 1f) + fwd * 0.4f).normalized); Aim(laR, wrR, (up - fwd * 0.2f).normalized);
            }
            else
            {
                // arms out and down to the handles
                Aim(uaL, laL, (handL - uaL.position).normalized * 0.6f + (-up * 0.4f));
                Aim(laL, wrL, handL - laL.position);
                Aim(uaR, laR, (handR - uaR.position).normalized * 0.6f + (-up * 0.4f));
                Aim(laR, wrR, handR - laR.position);
            }
            PlaceFoot(ftL, footL, frame, 0f);
            PlaceFoot(ftR, footR, frame, 0f);
            Leg(ulL, llL, endL, footL, up + fwd * 0.4f);
            Leg(ulR, llR, endR, footR, up + fwd * 0.4f);
        }

        void PlaceFoot(Transform foot, Vector3 at, Transform frame, float duck)
        {
            foot.position = at;
            foot.rotation = Quaternion.AngleAxis(duck, frame.up) * model.rotation * footRel[foot];
        }

        void Leg(Transform thigh, Transform shin, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 h = thigh.position;
            float a = thighLen, b = shinLen;
            Vector3 d = target - h;
            float c = Mathf.Clamp(d.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            Vector3 dn = d.normalized;
            Vector3 p = Vector3.ProjectOnPlane(pole, dn).normalized;
            float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            Vector3 knee = h + dn * (cosA * a) + p * (Mathf.Sqrt(1f - cosA * cosA) * a);
            Aim(thigh, shin, knee - h);
            Aim(shin, end, h + dn * c - knee);
        }
    }
}
