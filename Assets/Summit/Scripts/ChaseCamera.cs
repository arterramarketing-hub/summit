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

        Camera cam;
        float yaw, height, fov = 62f, focusVy;
        Vector3 focus;
        bool first = true;

        void Awake() { cam = GetComponent<Camera>(); }

        public void Snap() { first = true; }

        void LateUpdate()
        {
            if (rider == null) return;
            if (cam == null) cam = GetComponent<Camera>();
            float dt = Time.deltaTime;
            float s01 = Mathf.Clamp01(rider.Speed / 40f);
            Vector3 raw = rider.transform.position + Vector3.up * 1.1f;
            // follow sideways and forwards exactly, but spring the height: the snow is made of flat
            // facets, and following every kink is what made the whole view shudder
            if (first) { focus = raw; focusVy = 0f; }
            focus.x = raw.x; focus.z = raw.z;
            focus.y = Mathf.SmoothDamp(focus.y, raw.y, ref focusVy, 0.11f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));

            // look where the rider is going, not just where the board points
            float wantYaw = rider.Crashed ? yaw
                : Mathf.Lerp(rider.Heading, rider.TravelHeading, 0.7f) * 0.85f;
            yaw = first ? wantYaw : Mathf.Lerp(yaw, wantYaw, 1f - Mathf.Exp(-dt * 6.5f));
            var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

            float dist = Mathf.Lerp(5.8f, 8.2f, s01);
            Vector3 pos = focus - f * dist;
            float wantH = focus.y + Mathf.Lerp(2.2f, 2.9f, s01);
            // never inside the hill behind you
            wantH = Mathf.Max(wantH, MountainField.Height(pos.x, pos.z) + 1.3f);
            height = first ? wantH : Mathf.Lerp(height, wantH, 1f - Mathf.Exp(-dt * 7f));
            pos.y = height;

            if (shake > 0f)
            {
                float t = Time.time * 31f;
                pos += new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * shake * 0.5f;
            }

            Vector3 look = focus + f * 5f - Vector3.up * 0.4f;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));

            float wantFov = Mathf.Lerp(60f, 74f, s01 * s01);
            fov = first ? wantFov : Mathf.Lerp(fov, wantFov, 1f - Mathf.Exp(-dt * 2f));
            cam.fieldOfView = fov;
            first = false;
        }
    }
}
