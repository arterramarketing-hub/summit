using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Sits behind and above the rider, looking down the line of travel.
    /// Position tracks the rider exactly in the horizontal plane (no lag that
    /// grows with speed); only height and yaw are smoothed.
    ///
    /// It is also the first-person view (the pause menu's VIEW: FIRST PERSON, `pov`): a helmet camera at
    /// the rider's eyes, wide, tipped down so the board and the leading arm sit in the bottom of the frame,
    /// rolling a little with the lean. The chase view is always worked out first, exactly as before, and the
    /// first-person view is blended over it, so the two hand over to each other with a short glide:
    ///   - first person while riding, in the air, in spins and grabs, and through the avalanche's shake;
    ///   - the chase view at the start gate and the finish (the rider is standing: you see who you are),
    ///     during a flip (a head camera would whirl), from the moment of a crash until you are back up,
    ///     for the look down a crevasse and the shot of the burial. The camp has its own camera.
    /// The lens is stabilised like an action camera's: it follows a smoothed copy of where the head is over
    /// the board, never the bones themselves, and takes its direction from the line of travel.
    /// Every first-person number is in the block of Pov constants below.
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

        // ---- first person
        public const string PovPref = "summit_pov";   // PlayerPrefs: 1 = first person (off by default)
        public bool pov;                 // the player's choice (the pause menu); what is shown eases to it
        public RiderBody body;           // whose head bone the lens rides at, and whose head is hidden from it
        public LensFX lensFx;            // draws the streaking at the frame edges

        // Tuning, all blind (set without the game running): change them here.
        // Where the lens is.
        public static float PovEyeUp = 0.17f;        // m above the head bone (the base of the skull): eye height, a touch over
        public static float PovEyeForward = 0.13f;   // m ahead of it along the view: in front of the face and clear of the leading shoulder
        public static float PovHeadHeight = 1.0f;    // m above the rider's centre, for a rider with no head bone to follow
        public static float PovFollow = 9f;          // 1/s: how fast the lens follows the head over the board (lower: steadier, floatier)
        public static float PovGround = 0.45f;       // m: never closer to the snow than this
        public static float PovNear = 0.06f;         // m: near clip plane, so the arm and passing branches are not cut (0.2 in the chase view)
        // Where it looks.
        public static float PovPitch = 22f;          // deg down from the line of the slope ahead: more shows more board and knee, less shows more horizon
        public static float PovSlopeFollow = 0.8f;   // share of the slope's own steepness the view tips with (1: locked to the snow, 0: to the horizon)
        public static float PovAirPitch = 8f;        // deg further down when high in the air, toward the landing
        public static float PovPitchRate = 4f;       // 1/s: how fast the pitch settles
        public static float PovYawRate = 7f;         // 1/s: how fast the view swings onto the line of travel
        public static float PovLead = 0.12f;         // s: looks this far ahead into a turn...
        public static float PovLeadMax = 10f;        // ...by at most this many degrees
        public static float PovRoll = 0.3f;          // deg of roll per degree of the rider's lean (full lean is about 41)
        public static float PovRollMax = 14f;        // deg: the most the horizon tilts
        public static float PovRollRate = 5f;        // 1/s: how fast the roll follows
        public static float PovShake = 2f;           // deg the view trembles at full shake (the avalanche close behind, a hit)
        // The lens. The field of view is top to bottom: on a tall phone 108 is about 64 degrees across.
        public static float PovFov = 108f;           // deg at rest, with the LENS row on WIDE
        public static float PovFovSpeed = 6f;        // deg wider at full speed
        public static float PovFovBoost = 5f;        // deg wider on a Mountain Zoom
        public static float PovLensScale = 0.6f;     // share of the LENS row's widening used here (STANDARD -8, WIDER +5, FISHEYE +11)
        public static float PovFovMax = 124f;        // deg: never wider than this
        // Streaking at the frame edges (Resources/LensEdge.shader): the one strength value, and the speeds it grows between.
        public static float PovBlur = 0.07f;         // streak length at the frame edge at full speed, as a fraction of the screen (0: none)
        public static float PovBlurFrom = 8f, PovBlurFull = 32f;   // m/s: nothing below, all of it above
        // Handing over to the chase view and back.
        public static float PovBlendIn = 0.6f;       // s: chase view to first person
        public static float PovBlendOut = 0.35f;     // s: first person to chase view (a crash, a flip)
        public static float PovHideHead = 0.5f;      // the head stops drawing once the hand-over is this far in (0..1)
        public static bool PovFlipsInChase = true;   // watch flips from the chase view (false: stay in first person, level, while the body turns over)

        float povBlend, povYaw, povPitch, povRoll, povFov, nearChase = 0.2f;
        Vector3 povOff, povAhead = Vector3.forward, lastRider;
        bool povOn, haveLast;
        /// <summary>How much of the first-person view is on screen now: 0 the chase view, 1 first person.</summary>
        public float Pov => Mathf.SmoothStep(0f, 1f, povBlend);
        /// <summary>First person: the line of travel along the snow ahead (the lens itself is tipped down from it).</summary>
        public Vector3 PovAhead => povAhead;

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
                // from over the crevasse's middle line, backed off up it (from over a lip, its white top looked like a
                // pillar standing straight down the ice wall)
                Vector3 over = peekAt, back = f;
                if (Course.CrevasseNear(peekAt.x, peekAt.z, 4f, out var cv)) { over = Course.CrevasseMiddle(cv, peekAt); back = Vector3.forward; }
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
            Quaternion rot = Quaternion.LookRotation(look - pos, Vector3.up) * Quaternion.Euler(0f, 0f, roll);

            float wantFov = Mathf.Lerp(80f, 94f, s01 * s01) + extraFov + (rider.BoostTime > 0f ? 8f : 0f);   // a wide-angle lens
            fov = first ? wantFov : Mathf.Lerp(fov, wantFov, 1f - Mathf.Exp(-dt * 2f));
            float view = fov;
            // first person, over the chase view worked out above (which keeps running underneath, so handing back is seamless)
            if (pov || povBlend > 0f) StepPov(dt, s01, ref pos, ref rot, ref view);
            transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = view;
            lastRider = rider.transform.position; haveLast = true;
            first = false;
        }

        /// <summary>The first-person view for this frame, blended over the chase view's place, direction and lens.</summary>
        void StepPov(float dt, float s01, ref Vector3 pos, ref Quaternion rot, ref float view)
        {
            bool flipping = PovFlipsInChase && rider.Airborne && rider.TrickName != null && Mathf.Abs(rider.TrickFlip) > 0.01f;
            bool want = pov && rider.Running && !rider.Crashed && !flipping && peek <= 0.001f && hold <= 0.001f;
            // the hand-over runs on the real clock: it still glides through a crash's slow motion, and a change made
            // in the pause menu shows behind it
            float rdt = TestDt > 0f ? TestDt : Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool begin = povBlend <= 0f;   // coming in from the chase view: nothing of the last time is carried over
            povBlend = first ? (want ? 1f : 0f) : Mathf.MoveTowards(povBlend, want ? 1f : 0f, rdt / Mathf.Max(0.01f, want ? PovBlendIn : PovBlendOut));
            if (povBlend > 0f != povOn)
            {
                povOn = povBlend > 0f;
                if (povOn) { nearChase = cam.nearClipPlane; cam.nearClipPlane = PovNear; }
                else cam.nearClipPlane = nearChase;
            }
            if (body != null) body.SetPovHead(povBlend > PovHideHead && !rider.Crashed);
            if (!povOn) { if (lensFx != null) lensFx.Blur = 0f; return; }
            float k = Mathf.SmoothStep(0f, 1f, povBlend);

            // where the head is over the board: read from the body as it was drawn last frame (the body is posed
            // after this camera), and followed on a spring, never copied: the bones jiggle and the snow under the
            // board is made of facets. Held where it was through a fall or a flip, when the head goes its own way.
            Vector3 rp = rider.transform.position;
            Vector3 off = begin ? Vector3.up * PovHeadHeight : povOff;
            Transform head = body != null ? body.PovHead : null;
            if (head == null) off = Vector3.up * PovHeadHeight;
            else if (haveLast && !rider.Crashed && !flipping)
            {
                Vector3 o = head.position - lastRider;
                if (o.y > 0.1f && o.sqrMagnitude < 2.5f * 2.5f) off = o;   // (not a frame the rider was moved in)
            }
            povOff = begin ? off : Vector3.Lerp(povOff, off, 1f - Mathf.Exp(-dt * PovFollow));

            // along the line of travel, a little into the turn; never round to face uphill
            float lead = rider.Airborne ? 0f : Mathf.Clamp(rider.YawRate * PovLead, -PovLeadMax * Mathf.Deg2Rad, PovLeadMax * Mathf.Deg2Rad);
            float wantYaw = rider.Crashed ? povYaw : Mathf.Clamp(rider.TravelHeading + lead, -1.5f, 1.5f);
            povYaw = begin ? wantYaw : Mathf.Lerp(povYaw, wantYaw, 1f - Mathf.Exp(-dt * PovYawRate));
            // tipped down from the slope ahead (the chase view's own eased reading of it, over the next 12 m)
            float slope = Mathf.Clamp(Mathf.Atan2(-lookDrop, 12f) * Mathf.Rad2Deg, -10f, 45f);
            float air = rider.Airborne ? Mathf.Clamp01(rider.AboveGround / 4f) : 0f;
            float wantPitch = Mathf.Clamp(PovPitch + slope * PovSlopeFollow + PovAirPitch * air, 0f, 65f);
            povPitch = begin ? wantPitch : Mathf.Lerp(povPitch, wantPitch, 1f - Mathf.Exp(-dt * PovPitchRate));
            // rolling with the lean, as the head does (level in the air)
            float wantRoll = rider.Airborne || rider.Crashed ? 0f
                : Mathf.Clamp(-rider.Lean * Mathf.Rad2Deg * PovRoll, -PovRollMax, PovRollMax) * Mathf.Clamp01((rider.Speed - 3f) / 8f);
            povRoll = begin ? wantRoll : Mathf.Lerp(povRoll, wantRoll, 1f - Mathf.Exp(-dt * PovRollRate));

            float yawDeg = povYaw * Mathf.Rad2Deg;
            Quaternion eyeRot = Quaternion.Euler(povPitch, yawDeg, povRoll);
            povAhead = Quaternion.Euler(slope * PovSlopeFollow, yawDeg, 0f) * Vector3.forward;
            if (shake > 0f)
            {
                // a tremble of the view rather than of its place: the body is a hand's width from the lens
                float t = Time.time * 31f;
                eyeRot *= Quaternion.Euler((Mathf.PerlinNoise(t, 0.3f) - 0.5f) * shake * PovShake, (Mathf.PerlinNoise(0.7f, t) - 0.5f) * shake * PovShake, 0f);
            }
            // on the rider's own place (its height sprung, as the chase view's is), at the eyes, a little ahead of the face
            Vector3 flat = new Vector3(Mathf.Sin(povYaw), 0f, Mathf.Cos(povYaw));
            Vector3 eye = new Vector3(rp.x, focus.y - 1.1f, rp.z) + povOff + Vector3.up * PovEyeUp + flat * PovEyeForward;
            float ground = Slope.Height(eye.x, eye.z) + (floor != null ? Mathf.Max(0f, floor(eye.x, eye.z)) : 0f);
            eye.y = Mathf.Max(eye.y, ground + PovGround);

            float wantFov = Mathf.Min(PovFovMax, PovFov + PovFovSpeed * s01 * s01 + extraFov * PovLensScale + (rider.BoostTime > 0f ? PovFovBoost : 0f));
            povFov = begin ? wantFov : Mathf.Lerp(povFov, wantFov, 1f - Mathf.Exp(-dt * 2f));

            pos = Vector3.Lerp(pos, eye, k);
            rot = Quaternion.Slerp(rot, eyeRot, k);
            view = Mathf.Lerp(view, povFov, k);

            // the streaks run out from the point on screen you are travelling toward
            if (lensFx != null)
            {
                Vector2 centre = new Vector2(0.5f, 0.5f);
                Vector3 v = rider.Velocity;
                if (v.sqrMagnitude > 4f)
                {
                    Vector3 d = Quaternion.Inverse(rot) * v.normalized;
                    float th = Mathf.Tan(view * 0.5f * Mathf.Deg2Rad);
                    if (d.z > 0.2f)
                        centre = new Vector2(Mathf.Clamp(0.5f + 0.5f * d.x / (d.z * th * Mathf.Max(0.1f, cam.aspect)), 0.2f, 0.8f),
                                             Mathf.Clamp(0.5f + 0.5f * d.y / (d.z * th), 0.2f, 0.8f));
                }
                lensFx.BlurCentre = centre;
                lensFx.Blur = PovBlur * k * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PovBlurFrom, PovBlurFull, rider.Speed));
            }
        }

        // switched off (the camp takes the camera over): the chase view's clip plane, the head and a clear lens back
        void OnDisable()
        {
            if (povOn && cam != null) cam.nearClipPlane = nearChase;
            povOn = false; povBlend = 0f;
            if (body != null) body.SetPovHead(false);
            if (lensFx != null) lensFx.Blur = 0f;
        }
    }
}
