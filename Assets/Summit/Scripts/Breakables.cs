using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What happens when a sign or a flag (Breakable) is ridden into:
    ///  - a flag goes down with a little snow; it barely slows you
    ///  - a sign breaks in a burst of splinters and snow; it costs real speed and knocks you off balance, and a
    ///    hit square on at more than CrashAbove knocks you down the way a tree would (a shield takes that for you)
    ///  - invincible, you go through either clean
    /// The thrown body breaks them too, and is slowed by what it goes through.
    /// </summary>
    public class Breakables : MonoBehaviour
    {
        public const float CrashAbove = 24f;      // m/s into a sign's face (about 86 km/h): that hurts
        public const float SignKeep = 0.72f, FlagKeep = 0.98f;   // speed kept riding through
        public const float SignWobble = 0.7f;     // s of being knocked about after a sign

        Rider rider;
        SnowFX fx;
        Smasher smasher;
        /// <summary>One went down: which, did the rider crash on it, was the rider invincible, was it the thrown body.</summary>
        public System.Action<Breakable, bool, bool, bool> Broke;

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
                thrown.Slow(b.kind == Breakable.Kind.Sign ? 0.7f : 0.92f);
            }
            else return;

            bool invincible = !body && rider.InvincibleTime > 0f;
            bool crashed = false;
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
                    Vector3 p = b.transform.position;
                    Vector3 face = Vector3.ProjectOnPlane(b.transform.forward, Slope.Normal(p.x, p.z)).normalized;
                    float into = Mathf.Abs(Vector3.Dot(v, face));
                    crashed = rider.Breakthrough(SignKeep, SignWobble, into, CrashAbove, "Signpost!");
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
