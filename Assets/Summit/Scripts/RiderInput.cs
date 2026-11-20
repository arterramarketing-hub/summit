using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Summit
{
    /// <summary>
    /// Drag one finger left and right to steer (or tilt the phone). With that finger still down, a quick tap of a
    /// second finger jumps; pressing and holding the second finger tucks low for speed (letting go of a tuck is
    /// not a jump). In tilt mode the one finger does both. In the editor: A/D or arrows steer, Space taps or
    /// holds. A controller: left stick steers, the bottom face button taps or holds.
    /// Works with either the new Input System or the old Input Manager.
    ///
    /// In the air and on a rail (TrickMode, which the rider sets) the same fingers do the tricks: the steering
    /// finger's left/right is the spin and its up/down travel (Flip: counted from where it was as the board left
    /// the snow; W/S; a controller's stick) is the flip; the jump finger landing is a TrickTap (the grab, or the
    /// pop off a rail) and staying down is GrabHold. With tilt steering the one finger does it: a tap is the
    /// TrickTap (on lifting, or once it has been down past a tap) and a swipe up or down is the flip.
    /// A press that was part of a trick is never also a jump when it lifts.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // read input before anything uses it this frame
    public class RiderInput : MonoBehaviour
    {
        public float Steer { get; private set; }     // -1 left .. +1 right
        public bool Hold { get; private set; }       // tucking: the jump finger has been held past a tap
        public bool Pressed { get; private set; }    // the tuck began this frame
        public bool Released { get; private set; }   // the tuck ended this frame
        public float HoldTime { get; private set; }  // how long the current tuck has lasted
        public float LastHold { get; private set; }  // how long the tuck that just ended lasted
        /// <summary>A quick tap of the jump finger just lifted: jump (true for the one frame).</summary>
        public bool Jump { get; private set; }
        /// <summary>A press shorter than this (seconds) is a tap, a jump; held longer it is a tuck.</summary>
        public const float TapTime = 0.22f;
        float pressTime;
        public bool HasTilt { get; private set; }

        /// <summary>Up/down for tricks, -1 (down: a backflip, a tail press) .. +1 (up: a frontflip, a nose press).</summary>
        public float Flip { get; private set; }
        /// <summary>The trick finger just landed (true for the one frame): a grab in the air, the pop off a rail.</summary>
        public bool TrickTap { get; private set; }
        /// <summary>The trick finger is down: the grab is being held.</summary>
        public bool GrabHold { get; private set; }
        /// <summary>Set by the rider: in an air long enough to trick, or on a rail. Out of it the fingers only steer, jump and tuck.</summary>
        public bool TrickMode;
        float flipOrigin;
        bool fingerPrev, pressPrev, pressUsed, swiped;

        public float tiltRange = 0.36f;   // sin of the tilt that gives full lock (about 21 degrees)
        public float deadZone = 0.03f;
        public bool invertTilt;
        /// <summary>false = drag a finger left or right like a stick (default); true = steer by tilting the phone.</summary>
        public bool useTilt;

        float zero, twoTime;
        /// <summary>One-finger drag stick: where the finger first touched and where it is now (screen pixels).</summary>
        public bool StickActive;
        public Vector2 StickOrigin, StickPos;
        public float stickRadiusFrac = 0.22f;   // full lock at this fraction of the screen width
        bool stickPrev;
        bool wasDown, swallow;

        /// <summary>Thumb throw (0..1, past the dead zone) to edge: fine near the middle, full lock at the end.</summary>
        public static float Curve(float a) => a * (0.55f + 0.45f * a);   // the whole throw does something

        void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (GravitySensor.current != null) InputSystem.EnableDevice(GravitySensor.current);
            if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
#endif
            invertTilt = PlayerPrefs.GetInt("summit_invert", 0) == 1;
            useTilt = PlayerPrefs.GetInt("summit_tilt", 0) == 1;
        }

        /// <summary>Treat however the phone is held right now as straight.</summary>
        public void Calibrate()
        {
            float t = RawTilt();
            zero = float.IsNaN(t) ? 0f : Mathf.Clamp(t, -0.5f, 0.5f);
        }

        /// <summary>Ignore the press in progress, so the tap that starts a run is not also a jump.</summary>
        public void SwallowCurrentPress()
        {
            swallow = PointerDown();
            Hold = false;
            Released = false;
            Jump = false; TrickTap = false; GrabHold = false; pressUsed = false; swiped = false;
            HoldTime = 0f; pressTime = 0f;
        }

        void Update()
        {
            // steering
            float t = RawTilt();
            HasTilt = !float.IsNaN(t);
            float target = 0f;
            Pointers(out int fingers, out Vector2 fingerPos);
            if (!useTilt)
            {
                // one finger is a stick: the spot you first touch is neutral, drag sideways to steer
                bool on = fingers >= 1;
                if (on && !stickPrev) StickOrigin = fingerPos;
                stickPrev = on;
                StickActive = on;
                StickPos = fingerPos;
                float radius = Mathf.Max(60f, Screen.width * stickRadiusFrac);
                // the start point trails the thumb once it passes full lock, so reversing is instant
                // and you never run out of screen
                if (on)
                {
                    float off = fingerPos.x - StickOrigin.x;
                    if (off > radius) StickOrigin.x = fingerPos.x - radius;
                    else if (off < -radius) StickOrigin.x = fingerPos.x + radius;
                }
                float d = on ? Mathf.Clamp((fingerPos.x - StickOrigin.x) / radius, -1f, 1f) : 0f;
                float a = Mathf.Abs(d);
                a = a < 0.025f ? 0f : (a - 0.025f) / 0.975f;
                // expo response: the first half of the throw is gentle fine carving (a fifth of the edge at
                // half throw); only the outer part opens to a full edge
                target = Mathf.Sign(d) * Curve(a);
                twoTime = fingers >= 2 ? twoTime + Time.unscaledDeltaTime : 0f;
            }
            else if (HasTilt)
            {
                StickActive = false; stickPrev = false;
                float d = t - zero;
                if (invertTilt) d = -d;
                float a = Mathf.Abs(d);
                a = a < deadZone ? 0f : (a - deadZone) / (tiltRange - deadZone);
                target = Mathf.Clamp(Mathf.Sign(d) * a, -1f, 1f);
            }
            float key = KeyAxis();
            if (key != 0f) target = key;
            float pad = PadSteer();
            if (pad != 0f) target = pad;

            // up/down, for tricks: how far the first finger has moved up or down the screen since the board left the
            // snow (out of a trick its zero follows the finger, so the drift of a thumb that has been steering for a
            // minute is never a flip), on the same throw as the steering; or W/S, or a controller's stick
            float flip = 0f;
            {
                bool finger = fingers >= 1;
                float radius = Mathf.Max(60f, Screen.width * stickRadiusFrac);
                if (finger && (!fingerPrev || !TrickMode)) flipOrigin = fingerPos.y;
                if (finger)
                {
                    float off = fingerPos.y - flipOrigin;
                    if (off > radius) flipOrigin = fingerPos.y - radius; else if (off < -radius) flipOrigin = fingerPos.y + radius;
                    float d = Mathf.Clamp((fingerPos.y - flipOrigin) / radius, -1f, 1f), a = Mathf.Abs(d);
                    flip = a < 0.1f ? 0f : Mathf.Sign(d) * (a - 0.1f) / 0.9f;
                }
                fingerPrev = finger;
                float kv = KeyVertical(); if (kv != 0f) flip = kv;
                float pv = PadVertical(); if (pv != 0f) flip = pv;
            }
            Flip = TrickMode ? Mathf.Lerp(Flip, flip, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 60f)) : 0f;
            // a short low-pass takes the jitter out of the finger or sensor without feeling laggy
            Steer = Mathf.Lerp(Steer, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * (useTilt ? 16f : 60f)));

            // jump / tuck
            // the jump finger: in stick mode a second finger while the first steers; in tilt mode any finger; or
            // Space, or a controller's bottom face button. A quick tap of it is a jump, on lifting; held past a
            // tap it is a tuck, and letting go of a tuck is just standing back up
            bool press = (useTilt ? PointerDown() : (twoTime > 0f || SpaceDown())) || PadButton();
            if (swallow) { if (!press) swallow = false; press = false; pressTime = 0f; }
            float heldBefore = pressTime;
            bool down = TapOrTuck(press, Time.unscaledDeltaTime, ref pressTime, out bool jump);
            // the same finger, for tricks. Drag: the second finger landing is the tap. Tilt: the one finger is tap and
            // swipe both, so it is a tap when it lifts unmoved (or has stayed down past a tap), and a swipe is the flip
            bool began = press && !pressPrev;
            if (began) { pressUsed = false; swiped = false; }
            bool oneFinger = useTilt && !SpaceDown() && !PadButton();
            if (oneFinger && press && Mathf.Abs(flip) >= 0.5f) swiped = true;
            bool tap = oneFinger ? !swiped && (jump || (press && pressTime >= TapTime && heldBefore < TapTime)) : began;
            TrickTap = TrickMode && tap;
            GrabHold = TrickMode && press && !swiped && (!oneFinger || pressTime >= TapTime);
            if (TrickMode && (press || tap)) pressUsed = true;
            if (pressUsed || swiped) jump = false;   // a press that was a trick's is not also a jump when it lifts
            pressPrev = press;
            Jump = jump;
            Pressed = down && !wasDown;
            Released = !down && wasDown;
            if (Released) LastHold = HoldTime;
            HoldTime = down ? HoldTime + Time.unscaledDeltaTime : 0f;
            Hold = down;
            wasDown = down;
        }

        /// <summary>
        /// The jump finger's rule on its own (the test tools drive it too): given whether it is down this frame and
        /// how long it has been, is it a tuck (returned), and did a tap just lift (jump)?
        /// </summary>
        public static bool TapOrTuck(bool press, float dt, ref float pressTime, out bool jump)
        {
            jump = !press && pressTime > 0f && pressTime < TapTime;
            pressTime = press ? pressTime + dt : 0f;
            return press && pressTime >= TapTime;
        }

        /// <summary>Any new touch or click this frame, even one being swallowed.</summary>
        public bool AnyPress()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
            return false;
#else
            return Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) ||
                   (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
#endif
        }

        /// <summary>Where the latest touch or click is, in screen pixels.</summary>
        public static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                return Touchscreen.current.primaryTouch.position.ReadValue();
            if (Mouse.current != null) return Mouse.current.position.ReadValue();
            return Vector2.zero;
#else
            if (Input.touchCount > 0) return Input.GetTouch(0).position;
            return Input.mousePosition;
#endif
        }

        static bool SpaceDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
#else
            return Input.GetKey(KeyCode.Space);
#endif
        }

        /// <summary>How many fingers are down, and where the first one is.</summary>
        static void Pointers(out int fingers, out Vector2 pos)
        {
            fingers = 0; pos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                foreach (var tc in Touchscreen.current.touches)
                {
                    if (!tc.press.isPressed) continue;
                    if (fingers == 0) pos = tc.position.ReadValue();
                    fingers++;
                }
            }
            if (fingers == 0 && Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                fingers = 1;
                pos = Mouse.current.position.ReadValue();
            }
#else
            for (int i = 0; i < Input.touchCount; i++)
            {
                var tc = Input.GetTouch(i);
                if (tc.phase == TouchPhase.Ended || tc.phase == TouchPhase.Canceled) continue;
                if (fingers == 0) pos = tc.position;
                fingers++;
            }
            if (fingers == 0 && Input.GetMouseButton(0))
            {
                fingers = 1;
                pos = Input.mousePosition;
            }
#endif
        }

        public void SetTilt(bool on)
        {
            useTilt = on;
            PlayerPrefs.SetInt("summit_tilt", on ? 1 : 0);
            PlayerPrefs.Save();
            Calibrate();
        }

        static bool PointerDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return true;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) return true;
            return false;
#else
            return Input.touchCount > 0 || Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);
#endif
        }

        /// <summary>A controller's left stick, sideways (0 with none plugged in or inside its dead zone).</summary>
        static float PadSteer()
        {
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            if (g == null) return 0f;
            float x = g.leftStick.ReadValue().x, a = Mathf.Abs(x);
            return a < 0.12f ? 0f : Mathf.Sign(x) * Curve((a - 0.12f) / 0.88f);
#else
            return 0f;
#endif
        }

        static bool PadButton()
        {
#if ENABLE_INPUT_SYSTEM
            return Gamepad.current != null && Gamepad.current.buttonSouth.isPressed;
#else
            return false;
#endif
        }

        static float KeyAxis()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return 0f;
            float v = 0f;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) v -= 1f;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) v += 1f;
            return v;
#else
            return Input.GetAxisRaw("Horizontal");
#endif
        }

        /// <summary>W/S or the up/down arrows: up is +1.</summary>
        static float KeyVertical()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return 0f;
            float v = 0f;
            if (k.wKey.isPressed || k.upArrowKey.isPressed) v += 1f;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) v -= 1f;
            return v;
#else
            return Input.GetAxisRaw("Vertical");
#endif
        }

        /// <summary>A controller's left stick, up and down (0 with none plugged in or inside its dead zone).</summary>
        static float PadVertical()
        {
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            if (g == null) return 0f;
            float y = g.leftStick.ReadValue().y, a = Mathf.Abs(y);
            return a < 0.25f ? 0f : Mathf.Sign(y) * (a - 0.25f) / 0.75f;
#else
            return 0f;
#endif
        }

        /// <summary>Sideways component of gravity in the phone's frame, or NaN with no sensor.</summary>
        static float RawTilt()
        {
            Vector3 g = Vector3.zero;
#if ENABLE_INPUT_SYSTEM
            if (GravitySensor.current != null && GravitySensor.current.enabled)
                g = GravitySensor.current.gravity.ReadValue();
            if (g.sqrMagnitude < 0.01f && Accelerometer.current != null && Accelerometer.current.enabled)
                g = Accelerometer.current.acceleration.ReadValue();
#else
            if (SystemInfo.supportsAccelerometer) g = Input.acceleration;
#endif
            if (g.sqrMagnitude < 0.01f) return float.NaN;
            return g.normalized.x;
        }
    }
}
