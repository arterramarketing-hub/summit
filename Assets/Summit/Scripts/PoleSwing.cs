using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The hero's ski poles, after the clips have posed the arms (RiderBodyRide.PoseSkis, StartJump.Pose). The clips
    /// turn the hands so each pole points where Tools/MakeRides.py asked, but those directions were written in the
    /// rider's own frame and mostly near level (back and only a little down; level or above in the tuck, the flip and
    /// the grabs), so with the body leaning on into a jump the poles stuck out behind, up in the air, the whole time.
    ///
    /// Here each pole pivots in its fist toward where a skier's pole really goes, given as a direction in the
    /// skier's frame (x out from that hand's side, y up, z the way they face), and follows it on a soft spring:
    /// held in the world while the body turns under it, so a pole lags, swings past and settles. Trailing down
    /// and back on the snow, tips near it (RiderBodyTouch keeps them out of it); both planted forward at a
    /// take-off; out and hanging for balance in the air, swinging as the body turns through a spin or a flip.
    /// </summary>
    public sealed class PoleSwing
    {
        // aims, in the skier's frame (x out, y up, z forward): where the tip goes from the grip
        /// <summary>Trailing on the snow: down and back, about 55 degrees below level, a little out.</summary>
        public static readonly Vector3 Trail = new Vector3(0.16f, -0.80f, -0.56f);
        /// <summary>Clamped under the arms in a tuck: back along the body, tips just under level.</summary>
        public static readonly Vector3 Tuck = new Vector3(0.07f, -0.30f, -0.95f);
        /// <summary>Both planted at a take-off: forward and down, the tips by the ski tips.</summary>
        public static readonly Vector3 Plant = new Vector3(0.12f, -0.80f, 0.60f);
        /// <summary>In the air: out for balance and hanging down and back.</summary>
        public static readonly Vector3 Air = new Vector3(0.50f, -0.70f, -0.42f);
        /// <summary>The hand holding a ski in a grab: the pole hangs down behind, out of the way.</summary>
        public static readonly Vector3 Grab = new Vector3(0.20f, -0.85f, -0.45f);
        /// <summary>How much a pole hangs with the world's down on the snow and in the air (Apply's hang).</summary>
        public const float HangSnow = 0.75f, HangAir = 0.5f;
        /// <summary>Stood still: hanging nearly straight down, a little behind.</summary>
        public static readonly Vector3 Stand = new Vector3(0.10f, -0.97f, -0.18f);

        /// <summary>
        /// On the snow: trailing, the inside pole a little forward in a turn (turn + = right), clamped back in a
        /// tuck, and swung forward for a moment as the legs take a landing (landT: seconds since).
        /// </summary>
        public static void OnSnow(float tuck, float turn, float landT, out Vector3 aimL, out Vector3 aimR)
        {
            var inside = new Vector3(0f, -0.05f, 0.28f);
            var ready = Trail + new Vector3(0.10f, 0.05f, 0.55f);
            float land = 0.7f * (1f - Sm(0.05f, 0.45f, landT));
            aimL = Vector3.Lerp(Vector3.Lerp(Trail + inside * Mathf.Max(0f, -turn), Tuck, tuck), ready, land);
            aimR = Vector3.Lerp(Vector3.Lerp(Trail + inside * Mathf.Max(0f, turn), Tuck, tuck), ready, land);
        }

        /// <summary>
        /// In the air: both planted as the skis leave the lip (popT: seconds since) and let go; then out for
        /// balance, swinging (t: a clock), the two out of step; in with the ball in a flip (fl); the grabbing
        /// hand's pole (grabHand 0 left, 1 right, -1 none) hanging behind by grab; and coming forward, ready,
        /// as the landing nears (k: 0..1 through the air).
        /// </summary>
        public static void InAir(float popT, float t, float k, float fl, float grab, int grabHand, out Vector3 aimL, out Vector3 aimR)
        {
            float sw = Mathf.Sin(t * 5.2f), sw2 = Mathf.Sin(t * 3.1f + 1.1f);
            aimL = Air + new Vector3(0.10f * sw2, 0.12f * sw, 0.34f * sw);
            aimR = Air + new Vector3(-0.10f * sw2, -0.12f * sw, -0.34f * sw);
            aimL = Vector3.Lerp(aimL, Tuck, fl); aimR = Vector3.Lerp(aimR, Tuck, fl);
            if (grabHand == 0 || grabHand == 2) aimL = Vector3.Lerp(aimL, Grab, grab);
            if (grabHand == 1 || grabHand == 2) aimR = Vector3.Lerp(aimR, Grab, grab);
            var forward = Trail + new Vector3(0.15f, 0f, 0.30f);
            float ready = Sm(0.72f, 0.95f, k) * (1f - fl);
            aimL = Vector3.Lerp(aimL, forward, ready); aimR = Vector3.Lerp(aimR, forward, ready);
            float plant = 1f - Sm(0.02f, 0.28f, popT);
            aimL = Vector3.Lerp(aimL, Plant, plant); aimR = Vector3.Lerp(aimR, Plant, plant);
        }

        static float Sm(float a, float b, float x) { float u = Mathf.Clamp01((x - a) / (b - a)); return u * u * (3f - 2f * u); }

        /// <summary>Checks only (StartPageShot's before-and-after): the poles left where the clips put them.</summary>
        public static bool Off;

        const float K = 70f, C = 9f;      // the spring: a little under-damped, so a pole swings past and settles
        const float MaxLag = 75f;          // degrees a pole may trail its aim

        readonly Transform[] grip = new Transform[2];
        readonly Quaternion[] rest = new Quaternion[2];
        readonly Vector3[] dir = new Vector3[2], vel = new Vector3[2];
        bool primed;

        /// <summary>Takes each grip as it sits in its hand at rest (the clips turn the hands from there).</summary>
        public PoleSwing(Transform left, Transform right)
        {
            grip[0] = left; grip[1] = right;
            for (int s = 0; s < 2; s++) if (grip[s] != null) rest[s] = grip[s].localRotation;
        }

        /// <summary>For a copy of the rider (StartJump): its grips, with the rest the original took.</summary>
        public PoleSwing(Transform left, Transform right, Quaternion restL, Quaternion restR)
        {
            grip[0] = left; grip[1] = right; rest[0] = restL; rest[1] = restR;
        }

        /// <summary>A grip's place in its hand at rest (each frame starts from it: nothing builds up).</summary>
        public Quaternion Rest(int s) => rest[s];

        /// <summary>The next Apply snaps to its aims (a cut: a new loop, a respawn).</summary>
        public void Reset() => primed = false;

        /// <summary>The tip's direction now, from the grip (for checks).</summary>
        public Vector3 Tip(int s) => grip[s] != null ? grip[s].rotation * Vector3.down : Vector3.down;

        /// <summary>
        /// After the arms are posed: turn each pole about its grip toward its aim (skier's frame), keeping
        /// keepL / keepR of what the clips gave it (0..1: a pole plant the clip draws), on the spring. hang (0..1)
        /// is how much "down" is the world's rather than the skier's: a pole hangs, so on a slope or pitched up off a
        /// lip its tip still goes toward the snow; none in a flip, where the poles go round with the body.
        /// </summary>
        public void Apply(Vector3 forward, Vector3 up, Vector3 aimL, Vector3 aimR, float keepL, float keepR, float hang, float dt)
        {
            if (Off) { for (int s = 0; s < 2; s++) if (grip[s] != null) grip[s].localRotation = rest[s]; primed = false; return; }
            up = Vector3.Slerp(up.normalized, Vector3.up, Mathf.Clamp01(hang));
            forward = Vector3.ProjectOnPlane(forward, up).normalized;
            if (forward.sqrMagnitude < 1e-6f) return;
            Vector3 right = Vector3.Cross(up, forward);
            for (int s = 0; s < 2; s++)
            {
                var g = grip[s];
                if (g == null || g.parent == null) continue;
                Quaternion clipRot = g.parent.rotation * rest[s];
                Vector3 clipTip = clipRot * Vector3.down;
                Vector3 a = s == 0 ? aimL : aimR;
                Vector3 want = ((s == 0 ? -right : right) * a.x + up * a.y + forward * a.z).normalized;
                want = Vector3.Slerp(want, clipTip, Mathf.Clamp01(s == 0 ? keepL : keepR));
                if (!primed || dt <= 0f) { dir[s] = want; vel[s] = Vector3.zero; }
                else
                {
                    float h = Mathf.Min(dt, 1f / 30f);   // the spring stays steady on a long frame
                    vel[s] += ((want - dir[s]) * K - vel[s] * C) * h;
                    dir[s] = (dir[s] + vel[s] * h).normalized;
                    if (Vector3.Angle(dir[s], want) > MaxLag) dir[s] = Vector3.RotateTowards(want, dir[s], MaxLag * Mathf.Deg2Rad, 0f);
                }
                g.rotation = Quaternion.FromToRotation(clipTip, dir[s]) * clipRot;
            }
            primed = true;
        }
    }
}
