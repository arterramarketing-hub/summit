using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Sits behind and above the rider, looking down the line of travel.
    /// Position tracks the rider exactly in the horizontal plane (no lag that
    /// grows with speed); only height and yaw are smoothed.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ChaseCamera : MonoBehaviour
    {
        public Rider rider;
        public float shake;           // 0..1, set by the game (the slide closing in)
        public float distScale = 1f;  // the player's camera distance setting
        public float heightScale = 1f;  // the player's camera angle setting (low .. high)
        public float extraFov;          // the lens setting: wider angle
        public Vector3 peekAt;        // look down into a crevasse at this point...
        public float peek;            // ...this much (0..1)
        public Vector3 holdAt;        // the avalanche has buried the rider here: hold on a shot of the grave...
        public float hold;            // ...this much (0..1)
        public System.Func<float, float, float> floor;   // anything standing over the snow to keep above (the avalanche's debris)

        Camera cam;
        float speedCam = 5f, yaw, height, fov = 62f, focusVy, camDist = 6f, lookDrop, roll;
        Vector3 focus;
        bool first = true;

        void Awake() { cam = GetComponent<Camera>(); }

        public void Snap() { first = true; }
        /// <summary>Frame time for the batchmode test tools, where Time.deltaTime is always zero.</summary>
        public static float TestDt;

        void LateUpdate()
        {
            if (rider == null) return;
            if (cam == null) cam = GetComponent<Camera>();
            float dt = TestDt > 0f ? TestDt : Time.deltaTime;
            float s01 = Mathf.Clamp01(rider.Speed / 40f);
            Vector3 raw = rider.transform.position + Vector3.up * 1.1f;
            // follow sideways and forwards exactly, but spring the height: the snow is made of flat
            // facets, and following every kink is what made the whole view shudder
            if (first) { focus = raw; focusVy = 0f; }
            focus.x = raw.x; focus.z = raw.z;
            focus.y = Mathf.SmoothDamp(focus.y, raw.y, ref focusVy, 0.11f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));

            // look where the rider is going, not just where the board points
            // follow the line you are travelling, part way and a little behind: you see the rider carve
            // across the frame instead of the whole world swinging round with the board
            float wantYaw = rider.Crashed ? yaw : Mathf.Clamp(rider.TravelHeading, -1.4f, 1.4f) * 0.7f;   // never round to face uphill
            yaw = first ? wantYaw : Mathf.Lerp(yaw, wantYaw, 1f - Mathf.Exp(-dt * 4f));
            var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

            // low and close when slow; the faster you go the further back it pulls, to show what is coming
            // (eased both ways, so slowing down or a crash glides it back in rather than snapping)
            float speedDist = Mathf.Lerp(4.4f, 10.5f, Mathf.SmoothStep(0f, 1f, s01)) * distScale;
            speedCam = first ? speedDist : Mathf.Lerp(speedCam, speedDist, 1f - Mathf.Exp(-dt * 1.8f));
            float dist = speedCam;
            // never behind a tree trunk: slide in front of it (quickly), and ease back out after
            if (Physics.SphereCast(focus, 0.35f, -f, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<Obstacle>() != null)
                dist = Mathf.Max(2.5f, hit.distance);
            camDist = first ? dist : Mathf.Lerp(camDist, dist, 1f - Mathf.Exp(-dt * (dist < camDist ? 20f : 3f)));
            dist = camDist;
            Vector3 pos = focus - f * dist;
            float wantH = focus.y + Mathf.Lerp(1.15f, 2.3f, Mathf.SmoothStep(0f, 1f, s01)) * heightScale * Mathf.Lerp(1f, distScale, 0.7f);
            // never inside the hill behind you
            wantH = Mathf.Max(wantH, Slope.Height(pos.x, pos.z) + 1.3f);
            // ...or inside the avalanche's debris: ride up over it
            if (floor != null) wantH = Mathf.Max(wantH, Slope.Height(pos.x, pos.z) + floor(pos.x, pos.z) + 2.4f);
            height = first ? wantH : Mathf.Lerp(height, wantH, 1f - Mathf.Exp(-dt * 7f));
            pos.y = height;

            if (shake > 0f)
            {
                float t = Time.time * 31f;
                pos += new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * shake * 0.5f;
            }

            // aim along the slope ahead, but only part way down it, so the horizon and the far ranges stay in view
            Vector3 ahead = focus + f * 12f;
            float drop = Slope.Height(ahead.x, ahead.z) - Slope.Height(focus.x, focus.z);
            lookDrop = first ? drop : Mathf.Lerp(lookDrop, drop, 1f - Mathf.Exp(-dt * 3f));
            Vector3 look = ahead + Vector3.up * (lookDrop * 0.55f - 0.6f);
            if (peek > 0.001f)
            {
                // the rider went into a crevasse: lean out over the edge and look straight down after them
                float k = Mathf.SmoothStep(0f, 1f, peek);
                // from over the crevasse's middle, backed off along its length (not along the way you were going: across
                // a glacier gap that hung the camera over the near lip, its white top looking straight down the ice wall)
                Vector3 over = peekAt, back = f;
                if (Course.CrevasseNear(peekAt.x, peekAt.z, 4f, out var cv))
                {
                    over = Course.CrevasseMiddle(cv, peekAt);
                    if (cv.across) back = new Vector3(f.x < 0f ? -1f : 1f, 0f, 0f);
                }
                Vector3 rim = new Vector3(over.x, Mathf.Max(focus.y, Slope.Height(over.x, over.z) + 1f), over.z);
                pos = Vector3.Lerp(pos, rim - back * 1.8f + Vector3.up * 3.2f, k);
                look = Vector3.Lerp(look, peekAt, k);
            }
            if (hold > 0.001f)
            {
                // buried: rise and stand off to one side, uphill of the pile, looking down on it with the
                // settling cloud rolling on beyond
                float k = Mathf.SmoothStep(0f, 1f, hold);
                Vector3 stand = holdAt + new Vector3(4.5f, 0f, -8.5f);
                stand.y = Mathf.Max(holdAt.y, Slope.Height(stand.x, stand.z) + (floor != null ? floor(stand.x, stand.z) : 0f)) + 5.5f;
                pos = Vector3.Lerp(pos, stand, k);
                look = Vector3.Lerp(look, holdAt + new Vector3(0f, 0.4f, 3f), k);
            }
            // bank with a hard carve: nothing on gentle turns, up to about 9 degrees on a full-edge turn at speed
            float hard = Mathf.Clamp01((Mathf.Abs(rider.Lean) - 0.3f) / 0.42f) * Mathf.Clamp01((rider.Speed - 8f) / 17f);
            float wantRoll = rider.Crashed || rider.Airborne || peek > 0.001f || hold > 0.001f ? 0f : -Mathf.Sign(rider.Lean) * 9f * hard * hard;
            roll = first ? wantRoll : Mathf.Lerp(roll, wantRoll, 1f - Mathf.Exp(-dt * 3f));
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up) * Quaternion.Euler(0f, 0f, roll));

            float wantFov = Mathf.Lerp(80f, 94f, s01 * s01) + extraFov + (rider.BoostTime > 0f ? 8f : 0f);   // a wide-angle lens
            fov = first ? wantFov : Mathf.Lerp(fov, wantFov, 1f - Mathf.Exp(-dt * 2f));
            cam.fieldOfView = fov;
            first = false;
        }
    }
}
