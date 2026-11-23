using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What happens when a sign or a flag (Breakable) is ridden into:
    ///  - a flag goes down with a little snow; it barely slows you
    ///  - a sign breaks in a burst of splinters and snow, and you stay on your feet: it costs real speed, staggers
    ///    you and knocks you off your line, all by how hard you hit it, and it goes on the medical bill (RiderHit).
    ///    Nothing about a sign knocks you down any more: that is what the player asked for
    ///  - a SLOW banner tears and flaps away: it barely checks you; catching one of its stakes hard is a bruise
    ///  - a fence span (the hamlets' post-and-rail fence) gives way at a touch: it costs a tenth of your speed and a
    ///    light wobble, never a fall
    ///  - invincible, you go through any of them clean
    /// The thrown body breaks them too, and is slowed by what it goes through.
    /// </summary>
    public class Breakables : MonoBehaviour
    {
        public const float FlagKeep = 0.98f;      // speed kept riding through
        // a sign, from a gentle knock (Easy, m/s into it) to a hard one (Hard): the speed kept, the seconds of being
        // knocked about, and how hard (m/s) you are knocked aside (over a fifth of a second: Rider.Knock)
        public const float SignEasy = 6f, SignHard = 30f;
        public const float SignKeepEasy = 0.85f, SignKeepHard = 0.6f, SignWobbleEasy = 0.5f, SignWobbleHard = 1.2f;
        public const float BannerKeep = 0.97f, BannerWobble = 0.12f;   // a strip of cloth: you hardly feel it
        public const float StakeHurts = 15f;      // m/s: catching a banner's stake faster than this is a bruise
        public const float FenceKeep = 0.9f, FenceWobble = 0.3f;   // a fence span: flimsy, it hardly checks you

        Rider rider;
        SnowFX fx;
        Smasher smasher;
        /// <summary>One went down: which, did the rider crash on it, was the rider invincible, was it the thrown body.</summary>
        public System.Action<Breakable, bool, bool, bool> Broke;
        /// <summary>
        /// The rider rode into one, stayed up, and it hurt: what it was, their speed along the snow when they hit it
        /// (m/s), where it happened, the part of them that took it ("Head", "Chest", "Legs", "Shoulder", "Shin"), and
        /// what it costs (dollars, for the medical bill). Not raised for the thrown body or while invincible.
        /// </summary>
        public System.Action<Breakable.Kind, float, Vector3, string, float> RiderHit;

        /// <summary>A sign hit `hard` m/s (the harder of into its face, and half your speed): a few hundred dollars, to a few thousand.</summary>
        public static float SignCost(float hard) => Mathf.Round((150f + 4f * hard * hard) / 10f) * 10f;
        /// <summary>A banner's stake caught at `speed` m/s (above StakeHurts): a bruise.</summary>
        public static float StakeCost(float speed) => Mathf.Round((60f + 2f * (speed - 10f) * (speed - 10f)) / 10f) * 10f;

        public void Build(Rider r, SnowFX snow, Smasher splinters)
        {
            rider = r; fx = snow; smasher = splinters;
            Breakable.Touched = OnTouched;
            Breakable.Landed = (at, v) => fx?.Puff(at, Mathf.Clamp(v, 2f, 9f));
        }

        void OnDestroy()
        {
            if (Breakable.Touched == (System.Action<Breakable, Collider>)OnTouched) Breakable.Touched = null;
        }

        void Update() { Breakable.Step(Time.deltaTime); }

        void OnTouched(Breakable b, Collider other)
        {
            if (rider == null || b.Down) return;
            var rb = other.attachedRigidbody;
            var thrown = Ragdoll.Of(other);
            bool body = thrown != null;
            Vector3 v, at;
            if (rb == rider.Body)
            {
                // the rider on the board (getting up beside one, or idling on the start pad, does not count)
                if (!rider.Running || rider.Crashed) return;
                v = rider.Velocity; at = rider.transform.position;
                if (v.magnitude < 4f) return;
                // just up again after a fall, beside the flag planted where you came to rest: leave it standing
                if (rider.Invulnerable && v.magnitude < 8f) return;
            }
            else if (body && rb != null)
            {
                v = rb.linearVelocity; at = rb.position;
                if (v.magnitude < 2.5f) return;   // (a body at rest beside one, or a fall flag planted where it lies)
                // what it goes through takes some of its speed
                thrown.Slow(b.kind == Breakable.Kind.Sign ? 0.7f : b.kind == Breakable.Kind.Fence ? 0.9f : b.kind == Breakable.Kind.Banner ? 0.97f : 0.92f);
            }
            else return;

            bool invincible = !body && rider.InvincibleTime > 0f;
            bool crashed = false;
            // (what was struck, before it breaks and its pieces move)
            Vector3 from = b.transform.position;
            string part = body ? null : b.HitPart(at, v, out from);
            Vector3 mid = b.Break(at, v);
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            if (b.kind == Breakable.Kind.Sign)
            {
                fx?.Impact(mid, 8f, flat * 0.2f);
                smasher?.Splinters(mid, flat.sqrMagnitude > 0.01f ? flat.normalized : b.transform.forward, invincible ? 90 : 60, flat.magnitude);
                if (!body)
                {
                    // how hard into its face (signs face up the hill, at whoever comes down), measured along the
                    // snow: on a steep pitch most of your speed is downward, and all of it goes into the sign
                    Vector3 p = b.transform.position, n = Slope.Normal(p.x, p.z);
                    Vector3 face = Vector3.ProjectOnPlane(b.transform.forward, n).normalized;
                    float into = Mathf.Abs(Vector3.Dot(v, face)), along = Vector3.ProjectOnPlane(v, n).magnitude;
                    // (a post taken at a glance still hurts: half your speed at the least)
                    float hard = Mathf.Max(into, along * 0.5f), k = Mathf.Clamp01((hard - SignEasy) / (SignHard - SignEasy));
                    if (invincible) rider.Breakthrough(SignKeepHard, 0f, 0f, float.MaxValue, "");   // (Rider: straight through at SmashKeep)
                    else
                    {
                        // you stay up: slowed and staggered, on the line you were on (nothing shoves the board aside)
                        rider.Breakthrough(Mathf.Lerp(SignKeepEasy, SignKeepHard, k), Mathf.Lerp(SignWobbleEasy, SignWobbleHard, k), 0f, float.MaxValue, "");
                        RiderHit?.Invoke(b.kind, along, mid, part, SignCost(hard));
                    }
                }
            }
            else if (b.kind == Breakable.Kind.Fence)
            {
                // the snow on its rails comes off in a puff, and the wood cracks into splinters
                fx?.Puff(mid, 4f + flat.magnitude * 0.15f);
                smasher?.Splinters(mid, flat.sqrMagnitude > 0.01f ? flat.normalized : b.transform.right, 35, flat.magnitude);
                // (invincible: not even the tenth of your speed)
                if (!body) rider.Breakthrough(invincible ? 1f : FenceKeep, invincible ? 0f : FenceWobble, 0f, float.MaxValue, "");
            }
            else if (b.kind == Breakable.Kind.Banner)
            {
                // the snow it shakes off as it rips; you hardly feel it, unless you took a stake with you
                fx?.Puff(mid, 3f + flat.magnitude * 0.12f);
                if (!body)
                {
                    rider.Breakthrough(invincible ? 1f : BannerKeep, invincible ? 0f : BannerWobble, 0f, float.MaxValue, "");
                    float speed = flat.magnitude;
                    if (!invincible && part != null && speed > StakeHurts) RiderHit?.Invoke(b.kind, speed, from, part, StakeCost(speed));
                }
            }
            else
            {
                fx?.Puff(b.transform.position + Vector3.up * 0.2f, 3f + flat.magnitude * 0.15f);
                if (!body) rider.Breakthrough(FlagKeep, 0f, 0f, float.MaxValue, "");
            }
            Broke?.Invoke(b, crashed, invincible, body);
        }
    }
}
