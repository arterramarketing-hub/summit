using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Falls that kill. Once thrown, the body is watched on its way down: tumbling down rock or through the air
    /// it keeps count of how far it has fallen beyond the mountain's own line, and the moment it reaches snow
    /// again that settles it. A few metres is a fall like any other. Off a cliff (Rider.DeadlyDrop and more), or
    /// into the snow harder than anyone survives, it punches deep into the snow and stays there, half buried
    /// under what it threw up: the end of the run (the game hears Killed).
    /// Kept apart from RiderBody's riding code, which only calls FallBegin and FallStep.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>A fall that ends it: how far the body fell beyond the mountain's line (m).</summary>
        public System.Action<float> Killed;
        /// <summary>This crash killed the rider.</summary>
        public bool Dead { get; private set; }
        /// <summary>Into the snow faster than this (m/s) is fatal however short the fall.</summary>
        public static float FatalHit = 26f;

        float fallTopY, fallTopZ;
        Vector3 fallVel;

        /// <summary>The body has just been thrown: count the fall from where the rider last left the snow.</summary>
        void FallBegin()
        {
            Dead = false;
            Vector3 d = rider.DropFrom, p = rider.transform.position;
            fallTopY = Mathf.Max(d.y, p.y); fallTopZ = d.z;
            fallVel = lastVel;
        }

        /// <summary>Each physics step of a crash, once the body has moved for this step.</summary>
        void FallStep()
        {
            if (ragdoll == null || !ragdoll.Active || Dead) return;
            Vector3 c = ragdoll.Centre, v = ragdoll.Velocity;
            // down a crevasse is the crevasse's own business (the game watches for that)
            if (Course.CrevasseNear(c.x, c.z, 3f, out _, out _)) { fallTopY = c.y; fallTopZ = c.z; fallVel = v; return; }
            Vector3 n = Slope.Normal(c.x, c.z);
            // (only at the surface: a body somewhere under it is down a hole, not landing)
            bool touching = Mathf.Abs(c.y - Slope.DrawnHeight(c.x, c.z)) < 0.6f;
            if (touching && !RockFace.IsRock(n, c.x, c.z))
            {
                // the speed it came in at, before the snow stopped it
                float into = -Vector3.Dot(fallVel, n);
                float fell = fallTopY - c.y - Slope.Fall * Mathf.Max(0f, c.z - fallTopZ);
                if (fell > Rider.DeadlyDrop || into > FatalHit) { Die(c, n, Mathf.Max(into, Mathf.Sqrt(2f * Rider.G * Mathf.Max(0f, fell))), fell); return; }
                // on the snow: any fall from here starts from here
                fallTopY = c.y; fallTopZ = c.z;
            }
            fallVel = v;
        }

        void Die(Vector3 at, Vector3 n, float into, float fell)
        {
            Dead = true;
            rider.StayDown = true;
            // in as deep as it came in hard: a body-shaped hole a metre or more down, the snow it threw up heaped round it
            float depth = Mathf.Clamp(0.7f + into * 0.025f, 0.9f, 1.7f);
            ragdoll.Bury(depth, fallVel);
            SnowLayer.Instance?.Bury(ragdoll, depth);
            fx?.Thud(at, Mathf.Max(18f, into), fallVel * 0.3f);
            fx?.Thud(at + Random.insideUnitSphere * 0.8f, Mathf.Max(14f, into * 0.8f), Vector3.up * 6f);
            spray.Emit(90);
            blood?.Hit(at, n, into, true, fallVel, null);
            Killed?.Invoke(Mathf.Max(fell, 0f));
        }
    }
}
