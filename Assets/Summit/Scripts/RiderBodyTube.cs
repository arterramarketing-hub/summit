using UnityEngine;

namespace Summit
{
    /// <summary>
    /// (tube) The snow tube meeting something (Rider.TubeHit: a tree, a rock, a person, a fence, a sign): a rubber ring
    /// bounced off it. It squashes flat against what it hit and springs back with a few wobbles of its rubber, spins away
    /// from it, tips away, hops up off the snow and drops back (a smaller bounce off the snow if it came down hard), and the
    /// rider sat in it is thrown toward the hit and back in the seat, hands on the handles. All of it by how hard it was
    /// met, from a nudge to as hard as the tube takes before it throws you; the look only (the physics' bounce is Rider's:
    /// RiderRideOver.Meet). The ring, its stripe and its handles sit under two frames: one turned to the hit and scaled
    /// along it, one turned back, so the ring flattens along the hit and nowhere else.
    /// </summary>
    public partial class RiderBody
    {
        Transform tubeSquash, tubeShape;
        float squash, squashV;                  // the ring's give along the hit (- flattened against it), and how fast
        Vector3 squashAxis = Vector3.forward;   // the hit's way, in the tube's own frame
        float tubeHop, tubeHopV;                // m up off the snow (the look), and how fast
        float tubeTip, tubeTipV;                // deg tipped away from what it hit, and how fast
        Vector3 tubeTipAxis = Vector3.right;    // (in the pivot's frame)
        Vector3 jolt, joltV;                    // the rider thrown in the seat (in the tube's frame), and how fast

        /// <summary>The ring's spring: how fast its rubber wobbles (Hz) and how soon it stops (share of critical).</summary>
        public static float SquashHz = 3.4f, SquashDamp = 0.2f;
        /// <summary>The most it flattens (share of its width), tips (deg) and hops (m), met as hard as it takes.</summary>
        public static float SquashMost = 0.32f, TipMost = 24f, HopMost = 0.34f;

        /// <summary>The test tools: the last bounce's strength (0..1), and the tube's give, hop, tip and the rider's jolt now.</summary>
        public float TubeBounceK { get; private set; }
        public float TubeSquashNow => squash;
        public float TubeHopNow => tubeHop;
        public float TubeTipNow => tubeTip;
        public Vector3 TubeJoltNow => jolt;
        public Transform TubeShape => tubeShape;

        /// <summary>The ring, its stripe and handles go under these (BuildTube).</summary>
        Transform TubeFrames(Transform root)
        {
            tubeSquash = new GameObject("Squash").transform; tubeSquash.SetParent(root, false);
            tubeShape = new GameObject("Shape").transform; tubeShape.SetParent(tubeSquash, false);
            return tubeShape;
        }

        /// <summary>
        /// The tube bounced off something: `normal` points from it back toward the tube, `into` is how fast it was met (m/s).
        /// </summary>
        public void TubeBounce(Vector3 normal, float into)
        {
            if (!Tube || tubeRoot == null || pivot == null || rider == null) return;
            float k = Mathf.Clamp01((into - 1f) / 13f);   // (1 m/s a nudge; 14 m/s and over, as hard as it takes)
            TubeBounceK = k;
            Vector3 n = new Vector3(normal.x, 0f, normal.z);
            if (n.sqrMagnitude < 1e-4f) { n = -rider.Velocity; n.y = 0f; }
            if (n.sqrMagnitude < 1e-4f) n = Vector3.back;
            n.Normalize();
            // spun away from it: met on one side, the tube is turned off it (head on, one way or the other)
            Vector3 way = rider.Velocity; way.y = 0f;
            float off = way.sqrMagnitude > 0.01f ? Vector3.Cross(way.normalized, n).y : 0f;
            float turn = Mathf.Abs(off) > 0.15f ? Mathf.Sign(off) : (Random.value < 0.5f ? -1f : 1f);
            tubeSpinRate += turn * Mathf.Lerp(1.2f, 7f, k);
            // flattened against it (the ring's own frame: the give turns with it)
            Vector3 a = tubeRoot.InverseTransformDirection(n); a.y = 0f;
            squashAxis = a.sqrMagnitude > 1e-4f ? a.normalized : Vector3.forward;
            squashV -= Mathf.Lerp(1.2f, 6.5f, k);
            // tipped away from it (its top swung along the normal, away from what it hit)
            Vector3 np = pivot.InverseTransformDirection(n); np.y = 0f;
            if (np.sqrMagnitude > 1e-4f) tubeTipAxis = Vector3.Cross(Vector3.up, np.normalized);
            tubeTipV += Mathf.Lerp(70f, 360f, k);
            // up off the snow (a nudge only rocks it)
            if (k > 0.12f && tubeHop < 0.05f) tubeHopV = Mathf.Max(tubeHopV, Mathf.Sqrt(2f * 9.8f * HopMost) * Mathf.Lerp(0.35f, 1f, k));
            // the rider in it thrown on toward what it hit, and down into the seat as the ring gives
            Vector3 nr = roll != null ? roll.InverseTransformDirection(n) : n;
            joltV += -nr * Mathf.Lerp(0.5f, 2.6f, k) + Vector3.down * Mathf.Lerp(0.15f, 0.9f, k);
        }

        /// <summary>Every frame on the tube: the springs on (Apply, before the hero is posed).</summary>
        void TubeBounceStep(float dt)
        {
            if (tubeSquash == null) return;
            if (!Tube || rider.Crashed || dt <= 0f)
            {
                if (rider.Crashed || !Tube) TubeBounceClear();
                return;
            }
            // the ring's rubber: a damped spring along the hit
            float w = 2f * Mathf.PI * SquashHz;
            squashV += (-w * w * squash - 2f * SquashDamp * w * squashV) * dt;
            squash = Mathf.Clamp(squash + squashV * dt, -SquashMost, SquashMost * 0.8f);
            // the hop: up, and down under gravity; come down hard, it bounces once more, smaller
            if (tubeHop > 0f || tubeHopV > 0f)
            {
                tubeHopV -= 9.8f * dt;
                tubeHop += tubeHopV * dt;
                if (tubeHop <= 0f)
                {
                    tubeHop = 0f;
                    if (tubeHopV < -1.4f) { squashV += tubeHopV * 0.5f; joltV.y += tubeHopV * 0.35f; tubeHopV = -tubeHopV * 0.32f; }
                    else tubeHopV = 0f;
                }
            }
            // tipped away and back
            float wt = 2f * Mathf.PI * 2.2f;
            tubeTipV += (-wt * wt * tubeTip - 2f * 0.3f * wt * tubeTipV) * dt;
            tubeTip = Mathf.Clamp(tubeTip + tubeTipV * dt, -TipMost, TipMost);
            // the rider in the seat: thrown and back, a little behind the ring
            float wj = 2f * Mathf.PI * 2.6f;
            joltV += (-wj * wj * jolt - 2f * 0.35f * wj * joltV) * dt;
            jolt = Vector3.ClampMagnitude(jolt + joltV * dt, 0.18f);
            TubeBounceShow();
        }

        /// <summary>The ring flattened along the hit (and bulged the other ways), on its frames.</summary>
        void TubeBounceShow()
        {
            if (tubeSquash == null) return;
            var turn = Quaternion.LookRotation(squashAxis, Vector3.up);
            tubeSquash.localRotation = turn;
            tubeSquash.localScale = new Vector3(1f - 0.5f * squash, 1f - 0.4f * squash, 1f + squash);
            tubeShape.localRotation = Quaternion.Inverse(turn);
        }

        /// <summary>The tube's tip and hop on the roll (Apply's, after the lean).</summary>
        void TubeBounceRoll()
        {
            if (!Tube || roll == null) return;
            if (tubeTip != 0f) roll.localRotation = Quaternion.AngleAxis(tubeTip, tubeTipAxis) * roll.localRotation;
            roll.localPosition = Vector3.up * tubeHop;
        }

        /// <summary>The rider thrown in the seat (PoseHero's, in the tube): the body moves in it, leans and rolls with it, sinks.</summary>
        void TubeJolt(ref HeroRig.Drive d)
        {
            if (jolt.sqrMagnitude < 1e-8f) return;
            // (the rider faces the tube's +X: forward is the toe's way, the chest rolls about it)
            d.rootInBoard += new Vector3(jolt.x * 0.6f, Mathf.Min(0f, jolt.y) * 0.6f, jolt.z * 0.6f);
            d.lean += jolt.x / 0.18f * 16f;
            d.side += -jolt.z / 0.18f * 14f;
            d.drop += Mathf.Max(0f, -jolt.y) * 0.8f;
        }

        void TubeBounceClear()
        {
            squash = squashV = tubeHop = tubeHopV = tubeTip = tubeTipV = 0f;
            jolt = joltV = Vector3.zero;
            if (tubeSquash != null) { tubeSquash.localRotation = Quaternion.identity; tubeSquash.localScale = Vector3.one; tubeShape.localRotation = Quaternion.identity; }
        }
    }
}
