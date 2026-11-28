using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Nothing of the rider goes through the snow (snowfx), worked out after the clips and the rig's own layer have
    /// posed the body for the frame, against the snow's drawn surface (the highest of five samples a hand's width
    /// apart, so a bump between two of the terrain's triangles is not poked through):
    ///  - the hips: lifted back out, the legs solved again so the feet stay on the board;
    ///  - each knee: swung up about the line from hip to ankle (the foot does not move) until it is out;
    ///  - each hand: held on the surface where it would have gone in, keeping its own turn. A hard lean takes the
    ///    trailing hand down to the snow this way: it drags over the top of it (HandOnSnow, HandDragAt), with its own
    ///    spray and a groove in the track (RiderBody);
    ///  - on skis, each pole's tip: the pole swung back about the grip so the tip rests on the snow and drags.
    /// The board's own lean is RiderBody's (it rocks onto its edge rather than about its middle: EdgeLift).
    /// </summary>
    public partial class RiderBody
    {
        const float HipRoom = 0.13f, KneeRoom = 0.07f, HandRoom = 0.045f, PoleTip = 1.08f;
        readonly float[] handTouch = new float[2], poleTouch = new float[2];
        readonly Vector3[] handDragAt = new Vector3[2], poleDragAt = new Vector3[2];

        /// <summary>Where the hand is dragging on the snow (the one most on it), or the pole tip on skis.</summary>
        public Vector3 HandDragAt { get; private set; }
        /// <summary>How much the most-touching pole tip is on the snow (skis).</summary>
        public float PoleOnSnow { get; private set; }
        public Vector3 PoleDragAt { get; private set; }

        /// <summary>
        /// The board rocks over onto its edge, not about its middle: the deck's middle stays on the snow only when it
        /// is flat; banked by `rollDeg` (deg), the edge in the snow is the pivot, and the middle rises by the half
        /// width times its sine (less the edge's own bite, 1.5 cm). Only the look: the board's contact is Rider's.
        /// </summary>
        public static float EdgeLift(float rollDeg) => Mathf.Max(0f, 0.13f * Mathf.Abs(Mathf.Sin(rollDeg * Mathf.Deg2Rad)) - 0.015f);

        /// <summary>The snow's top under p (the drawn surface, the highest of five samples 8 cm apart).</summary>
        static float SnowTop(Vector3 p)
        {
            const float e = 0.08f;
            float y = Slope.DrawnHeight(p.x, p.z);
            y = Mathf.Max(y, Slope.DrawnHeight(p.x + e, p.z)); y = Mathf.Max(y, Slope.DrawnHeight(p.x - e, p.z));
            y = Mathf.Max(y, Slope.DrawnHeight(p.x, p.z + e)); y = Mathf.Max(y, Slope.DrawnHeight(p.x, p.z - e));
            return y;
        }

        /// <summary>After the pose: hips, knees and hands out of the snow (see the class). `poles`: the skis' poles too.</summary>
        void KeepOffSnow(float dt, bool poles)
        {
            var h = hero;
            HandOnSnow = 0f; PoleOnSnow = 0f;
            if (h == null || !h.Ok || Walking || rider.Crashed || ragdoll != null && ragdoll.Active) { handTouch[0] = handTouch[1] = 0f; return; }
            Vector3 up = Vector3.up;

            // the hips (the seat of the trousers, a hand's width under the bone)
            float gap = SnowTop(h.Hips.position) + HipRoom - h.Hips.position.y;
            if (gap > 0f)
            {
                Vector3 a0 = h.Foot[0].position, a1 = h.Foot[1].position, k0 = h.LowerLeg[0].position, k1 = h.LowerLeg[1].position;
                Quaternion f0 = h.Foot[0].rotation, f1 = h.Foot[1].rotation;
                h.Hips.position += up * gap;
                h.LegTo(0, a0, k0 + up * gap); h.Foot[0].rotation = f0;
                h.LegTo(1, a1, k1 + up * gap); h.Foot[1].rotation = f1;
            }
            // each knee, swung up about the line from the hip to the ankle
            for (int s = 0; s < 2; s++)
            {
                Vector3 knee = h.LowerLeg[s].position;
                float kg = SnowTop(knee) + KneeRoom - knee.y;
                if (kg <= 0f) continue;
                Vector3 ankle = h.Foot[s].position; Quaternion fr = h.Foot[s].rotation;
                h.LegTo(s, ankle, knee + up * (kg + 0.35f));
                h.Foot[s].rotation = fr;
            }
            // each hand: on the surface, never through it
            for (int s = 0; s < 2; s++)
            {
                if (HandGone(s) || h.Hand[s] == null) { handTouch[s] = 0f; continue; }
                Vector3 w = h.Hand[s].position;
                float top = SnowTop(w);
                float hg = top + HandRoom - w.y;
                if (hg > 0f)
                {
                    Quaternion hr = h.Hand[s].rotation;
                    h.ArmTo(s, w + up * hg, h.LowerArm[s].position + up * 0.25f);
                    h.Hand[s].rotation = hr;
                }
                // on it: pressed down to it, or within a couple of centimetres over it
                float on = Mathf.Clamp01((hg + 0.02f) / 0.04f);
                handTouch[s] = dt <= 0f ? on : Mathf.MoveTowards(handTouch[s], on, dt * 12f);
                handDragAt[s] = new Vector3(w.x, top, w.z);
            }
            int most = handTouch[1] > handTouch[0] ? 1 : 0;
            HandOnSnow = handTouch[most];
            HandDragAt = handDragAt[most];

            if (!poles || pole[0] == null) return;
            for (int s = 0; s < 2; s++)
            {
                poleTouch[s] = 0f;
                var p = pole[s];
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                Vector3 grip = p.position, tip = p.TransformPoint(new Vector3(0f, -PoleTip, 0f));
                float top = SnowTop(tip);
                if (tip.y < top + 0.02f)
                {
                    // swung back about the grip: the same length, the tip on the snow
                    Vector3 d = tip - grip;
                    float len = d.magnitude, want = top + 0.02f - grip.y;   // (how far down the tip may reach)
                    Vector3 flat = new Vector3(d.x, 0f, d.z);
                    if (flat.sqrMagnitude < 1e-6f) flat = -rider.Velocity; flat.y = 0f;
                    if (flat.sqrMagnitude < 1e-6f) flat = Vector3.back;
                    float y = Mathf.Clamp(want, -len, len), r = Mathf.Sqrt(Mathf.Max(0f, len * len - y * y));
                    Vector3 to = flat.normalized * r + up * y;
                    p.rotation = Quaternion.FromToRotation(d, to) * p.rotation;
                    tip = grip + to;
                    poleTouch[s] = 1f;
                }
                poleDragAt[s] = new Vector3(tip.x, top, tip.z);
            }
            int pm = poleTouch[1] > poleTouch[0] ? 1 : 0;
            PoleOnSnow = poleTouch[pm];
            PoleDragAt = poleDragAt[pm];
        }
    }
}
