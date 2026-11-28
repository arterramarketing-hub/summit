using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// A rider's bones by part (the hero's Unity Humanoid names, or a Quaternius-style skeleton's), and its rest pose
    /// to go back to after a ragdoll: what the hero (RiderBodyHero), its ragdoll and the mountain's riders' ragdolls
    /// find their bones by. (It also posed the previous rider into a stance once; the hero has its own clips.)
    /// </summary>
    public class RiderRig
    {
        Transform model, body, hips, abdomen, torso, chest, neck, head;
        Transform uaL, laL, wrL, uaR, laR, wrR, ulL, llL, endL, ulR, llR, endR, ftL, ftR;
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        public bool Ok { get; private set; }

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
            if (F("UpperLeg.L") == null && F("LeftUpperLeg") != null)
            {
                // the hero (Tools/MakeHero.py): Unity Humanoid names, three spine bones, and the feet as the shins'
                // children, so a foot bone is also where its shin ends
                hips = F("Hips"); abdomen = F("Spine"); torso = F("Chest"); chest = F("UpperChest"); neck = F("Neck"); head = F("Head");
                uaL = F("LeftUpperArm"); laL = F("LeftLowerArm"); wrL = F("LeftHand"); uaR = F("RightUpperArm"); laR = F("RightLowerArm"); wrR = F("RightHand");
                ulL = F("LeftUpperLeg"); llL = F("LeftLowerLeg"); ftL = endL = F("LeftFoot"); ulR = F("RightUpperLeg"); llR = F("RightLowerLeg"); ftR = endR = F("RightFoot");
            }
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
        }
    }
}
