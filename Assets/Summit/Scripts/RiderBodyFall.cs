using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Falls that kill. Once thrown, the body is watched on its way down: tumbling down a face or through the air
    /// it keeps count of how far it has fallen beyond the mountain's own line, and the moment it reaches ground it
    /// could lie on again (snow or rock, anything not too steep to stop on) that settles it. One rule, wherever it
    /// happens: Rider.DeathFall or more and it hits like a bomb, punches deep into the snow and stays there, half
    /// buried under what it threw up: the end of the run (the game hears Killed). Anything shorter is a fall like any
    /// other. A body that stops short of the ground, lodged on a spire or a boulder part way down a cliff
    /// (CliffSpires) after a drop that far, dies where it lies.
    /// Kept apart from RiderBody's riding code, which only calls FallBegin and FallStep.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>A fall that ends it: how far the body fell beyond the mountain's line (m).</summary>
        public System.Action<float> Killed;
        /// <summary>This crash killed the rider.</summary>
        public bool Dead { get; private set; }
        float fallTopX, fallTopY, fallTopZ, stuckOnRock;
        /// <summary>The body is dropping and has fallen far enough beyond the mountain's line that it may yet kill:
        /// whether it does isn't known until it lands (a tap waits for that).</summary>
        public bool FallPending { get; private set; }
        /// <summary>m: a drop this far beyond the mountain's line, still going, isn't decided until it lands.</summary>
        public const float PendingDrop = 3f;
        Vector3 fallVel;

        /// <summary>The body has just been thrown: count the fall from where the rider last left the snow.</summary>
        void FallBegin()
        {
            Dead = false; FallPending = false;
            Vector3 d = rider.DropFrom, p = rider.transform.position;
            fallTopX = d.x; fallTopY = Mathf.Max(d.y, p.y); fallTopZ = d.z;
            fallVel = lastVel;
            stuckOnRock = 0f;
        }

        /// <summary>Each physics step of a crash, once the body has moved for this step.</summary>
        void FallStep()
        {
            if (ragdoll == null || !ragdoll.Active || Dead) { FallPending = false; return; }
            Vector3 c = ragdoll.Centre, v = ragdoll.Velocity;
            // down a crevasse is the crevasse's own business (the game watches for that)
            if (Course.CrevasseNear(c.x, c.z, 3f)) { fallTopX = c.x; fallTopY = c.y; fallTopZ = c.z; fallVel = v; FallPending = false; return; }
            Vector3 n = Slope.Normal(c.x, c.z);
            // (only at the surface: a body somewhere under it is down a hole, not landing; and a face too steep to stop
            // on, snow or rock, is tumbled down, the fall still counting)
            bool touching = Mathf.Abs(c.y - Slope.DrawnHeight(c.x, c.z)) < 0.6f;
            if (touching && n.y >= 0.62f)
            {
                // the speed it came in at, before the ground stopped it
                float into = -Vector3.Dot(fallVel, n);
                float fell = fallTopY - c.y - Slope.Fall * Mathf.Max(0f, c.z - fallTopZ);
                if (fell >= Rider.DeathFall)
                {
                    float hit = Mathf.Max(into, Mathf.Sqrt(2f * Rider.G * fell));
                    if (RockFace.IsRock(n, c.x, c.z)) DieOnRock(c, n, fell); else Die(c, n, hit, fell);
                    return;
                }
                // on the snow: any fall from here starts from here
                fallTopX = c.x; fallTopY = c.y; fallTopZ = c.z;
                stuckOnRock = 0f;
                FallPending = false;
            }
            else
            {
                // stopped off the snow: caught on the rocks part way down a cliff, after a drop that kills
                stuckOnRock = ragdoll.Speed < 1f ? stuckOnRock + Time.fixedDeltaTime : 0f;
                float fell = fallTopY - c.y - Slope.Fall * Mathf.Max(0f, c.z - fallTopZ);
                if (stuckOnRock > 0.8f && fell >= Rider.DeathFall) { DieOnRock(c, n, fell); return; }
                FallPending = fell >= PendingDrop;
            }
            fallVel = v;
        }

        /// <summary>Killed by the fall, but caught on rock: it stays where it stopped, with nothing to bury it in.</summary>
        void DieOnRock(Vector3 at, Vector3 n, float fell)
        {
            Dead = true;
            rider.StayDown = true;
            float into = Mathf.Sqrt(2f * Rider.G * fell);
            fx?.Thud(at, 30f, Vector3.up * 4f);   // a giant impact: the dust and grit of it, and the hit-stop the game puts on Killed
            blood?.Hit(at, n, into, true, fallVel, null);
            Killed?.Invoke(fell);
        }

        void Die(Vector3 at, Vector3 n, float into, float fell)
        {
            Dead = true;
            rider.StayDown = true;
            // in as deep as it came in hard: a body-shaped hole a metre or more down, the snow it threw up heaped round it
            float depth = Mathf.Clamp(0.7f + into * 0.025f, 0.9f, 1.7f);
            ragdoll.Bury(depth, fallVel);
            SnowLayer.Instance?.Bury(ragdoll, depth);
            // a giant impact: snow bursting out all round and up, a second and third plume after it
            fx?.Thud(at, Mathf.Max(30f, into), fallVel * 0.3f);
            fx?.Thud(at + Random.insideUnitSphere * 0.8f, Mathf.Max(24f, into * 0.8f), Vector3.up * 8f);
            fx?.Thud(at + Random.insideUnitSphere * 1.5f, Mathf.Max(18f, into * 0.6f), Vector3.up * 12f);
            spray.Emit(180);
            blood?.Hit(at, n, into, true, fallVel, null);
            Killed?.Invoke(Mathf.Max(fell, 0f));
        }
    }
}
