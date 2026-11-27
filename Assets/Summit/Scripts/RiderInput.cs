using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Summit
{
    /// <summary>
    /// Everything with one finger. Drag it left and right to steer (or tilt the phone). The jump is the JUMP
    /// setting's gesture of that same finger (FingerGesture): a double tap (the default; it fires as the second
    /// tap lands), a tap, or a flick up. A single tap on the snow does nothing with DOUBLE TAP, so a nervous
    /// lift-and-retouch never jumps; lifted and put straight back down, the finger steers on from the same middle.
    /// On a rail, a box or a cable the same double tap (a tap, with TAP) is the pop off it. In the air (TrickMode,
    /// which the rider sets) the finger's left/right is the spin and its up/down travel (Flip: counted from where it
    /// was as the board left the snow; W/S; a controller's stick) is the flip, and each tap is a grab, a bigger
    /// one each time; a tap's moment off the glass lets go of neither the spin nor the flip. With tilt steering
    /// the finger can do all of it anywhere on the screen. One gesture is only ever one thing: a tap that grabbed
    /// starts no double tap, a double tap is never a grab or a switch-up.
    ///
    /// The old two-handed way still works alongside: a second finger's quick tap jumps, held it tucks; in the air
    /// it lands a grab and keeping it down holds the grab; on a rail it pops. In the editor: A/D or arrows steer,
    /// W/S flip, Space taps or holds. A controller: left stick, bottom face button.
    /// Works with either the new Input System or the old Input Manager.
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
        /// <summary>Set by the rider: on a rail, a box or a cable (TrickMode too).</summary>
        public bool OnRail;
        /// <summary>The pop off a rail, a box or a cable (true for the one frame).</summary>
        public bool RailPop { get; private set; }
        float flipOrigin, heldSteer, heldFlip;
        bool fingerPrev, pressPrev, pressUsed, swallowFinger;

        /// <summary>How the one finger jumps: the GAME tab's JUMP setting (PlayerPrefs "summit_jump").</summary>
        public JumpStyle jumpStyle;
        public static readonly string[] JumpNames = { "DOUBLE TAP", "TAP", "FLICK UP" };
        readonly FingerGesture gesture = new FingerGesture();
        public void SetJumpStyle(JumpStyle s) { jumpStyle = s; PlayerPrefs.SetInt("summit_jump", (int)s); PlayerPrefs.Save(); }

        // ---- the test tools drive it without a screen: fingers and where the first is, and the step's length
        public static bool TestDrive; public static int TestFingers; public static Vector2 TestPos;
        float testDt = -1f;
        /// <summary>One frame of input, `dt` long, from TestFingers / TestPos (TestDrive on).</summary>
        public void TestStep(float dt) { testDt = dt; Read(dt); testDt = -1f; }

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

        void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (GravitySensor.current != null) InputSystem.EnableDevice(GravitySensor.current);
            if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
#endif
            invertTilt = PlayerPrefs.GetInt("summit_invert", 0) == 1;
            useTilt = PlayerPrefs.GetInt("summit_tilt", 0) == 1;
            jumpStyle = (JumpStyle)Mathf.Clamp(PlayerPrefs.GetInt("summit_jump", 0), 0, 2);
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
            swallow = SpaceDown() || PadButton() || twoTime > 0f;
            swallowFinger = PointerDown();
            gesture.Reset();
            if (swallowFinger) gesture.Swallow();
            Hold = false;
            Released = false;
            Jump = false; TrickTap = false; GrabHold = false; RailPop = false; pressUsed = false;
            HoldTime = 0f; pressTime = 0f;
        }

        void Update() => Read(testDt >= 0f ? testDt : Time.unscaledDeltaTime);

        void Read(float dt)
        {
            Pointers(out int fingers, out Vector2 fingerPos);
            bool finger = fingers >= 1;
            Vector2 screen = new Vector2(Screen.width, Screen.height);
            // the one finger: drag steering's own finger, or with tilt any finger. Its taps, double taps and flicks
            // are read first: whether it has come straight back down decides where the stick's middle is
            if (Time.timeScale <= 0f && testDt < 0f) gesture.Reset();   // (the pause menu's taps are nobody's)
            gesture.Step(finger, fingerPos, dt, screen);
            if (swallowFinger) { if (!finger) swallowFinger = false; else if (gesture.Began) gesture.Swallow(); }

            // steering
            float t = RawTilt();
            HasTilt = !float.IsNaN(t);
            float target = 0f;
            bool holding = false;
            if (!useTilt)
            {
                // one finger is a stick: the spot you first touch is neutral, drag sideways to steer. Lifted and put
                // straight back down near the same spot (a tap, a double tap) it carries on from the same middle;
                // in the air or on a rail it even holds what it was doing while it is up that moment, so a tap for a
                // grab never lets a spin go, and a double tap to pop is never a switch-up
                bool on = finger;
                if (on && !stickPrev && !gesture.Resumed) StickOrigin = fingerPos;
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
                // how far you drag is how fast you go sideways: in proportion here, shaped once by Rider.Shape (half
                // the throw asks about 45% of a whole drag)
                target = Mathf.Sign(d) * a;   // (in proportion; the one curve is Rider.Shape's)
                holding = !on && TrickMode && gesture.SinceUp <= FingerGesture.DoubleGap;
                if (holding) target = heldSteer; else heldSteer = target;
                twoTime = fingers >= 2 ? twoTime + dt : 0f;
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
                float radius = Mathf.Max(60f, Screen.width * stickRadiusFrac);
                if (finger && ((!fingerPrev && !gesture.Resumed) || !TrickMode)) flipOrigin = fingerPos.y;
                if (finger)
                {
                    float off = fingerPos.y - flipOrigin;
                    if (off > radius) flipOrigin = fingerPos.y - radius; else if (off < -radius) flipOrigin = fingerPos.y + radius;
                    float d = Mathf.Clamp((fingerPos.y - flipOrigin) / radius, -1f, 1f), a = Mathf.Abs(d);
                    flip = a < 0.1f ? 0f : Mathf.Sign(d) * (a - 0.1f) / 0.9f;
                }
                else if (holding) flip = heldFlip;
                if (finger) heldFlip = flip;
                fingerPrev = finger;
                float kv = KeyVertical(); if (kv != 0f) flip = kv;
                float pv = PadVertical(); if (pv != 0f) flip = pv;
            }
            Flip = TrickMode ? Mathf.Lerp(Flip, flip, 1f - Mathf.Exp(-dt * 60f)) : 0f;
            // a short low-pass takes the jitter out of the finger or sensor without feeling laggy
            Steer = Mathf.Lerp(Steer, target, 1f - Mathf.Exp(-dt * (useTilt ? 16f : 60f)));

            bool air = TrickMode && !OnRail, rail = TrickMode && OnRail, snow = !TrickMode;

            // ---- the buttons: a second finger while the first steers, Space, a controller's bottom button. As they
            //      always were: a quick tap is a jump (on lifting), held past a tap it is a tuck; in the air its
            //      landing is the grab and keeping it down holds the grab; on a rail its landing is the pop
            bool button = (!useTilt && twoTime > 0f) || SpaceDown() || PadButton();
            if (swallow) { if (!button) swallow = false; button = false; pressTime = 0f; }
            bool downB = TapOrTuck(button, dt, ref pressTime, out bool jumpB);
            bool beganB = button && !pressPrev;
            if (beganB) pressUsed = false;
            if (TrickMode && button) pressUsed = true;
            if (pressUsed) jumpB = false;   // a press that was a trick's is not also a jump when it lifts
            pressPrev = button;

            // ---- the one finger, by the JUMP setting: on the snow a double tap (or a tap, or a flick up) is the
            //      jump; on a rail a double tap (a tap, with TAP) is the pop off it; in the air a tap is the grab. One
            //      gesture is only ever one of them: a tap that grabbed starts no double tap, and the double tap's
            //      own second touch is never a tap
            bool oneJump = false, onePop = false, oneGrab = false;
            if (air)
            {
                if (gesture.Tap) { oneGrab = true; gesture.CancelChain(); }
                // with tilt the finger held still past a tap is a grab too, held (it always was)
                if (useTilt && finger && !gesture.Consumed && gesture.Held >= TapTime && gesture.Held - dt < TapTime && gesture.Travel <= FingerGesture.TapSlop * Mathf.Max(screen.x, 320f)) oneGrab = true;
            }
            else if (rail) onePop = jumpStyle == JumpStyle.Tap ? gesture.Tap : gesture.DoubleTap;
            else if (snow) oneJump = jumpStyle == JumpStyle.DoubleTap ? gesture.DoubleTap : jumpStyle == JumpStyle.Tap ? gesture.Tap : gesture.Flick;
            if (!snow && gesture.Tap && jumpStyle == JumpStyle.Tap) gesture.CancelChain();

            TrickTap = air && (beganB || oneGrab);
            GrabHold = air && (button || (useTilt && finger && !gesture.Consumed && gesture.Held >= TapTime && gesture.Travel <= FingerGesture.TapSlop * Mathf.Max(screen.x, 320f)));
            RailPop = rail && (beganB || onePop);
            Jump = snow && (jumpB || oneJump);
            // the tuck: a button held; with tilt the finger held still (as ever)
            bool down = downB || (useTilt && finger && snow && gesture.Held >= TapTime && gesture.Travel <= FingerGesture.TapSlop * Mathf.Max(screen.x, 320f));
            Pressed = down && !wasDown;
            Released = !down && wasDown;
            if (Released) LastHold = HoldTime;
            HoldTime = down ? HoldTime + dt : 0f;
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

        /// <summary>The space bar is down (a tap for the keyboard).</summary>
        public static bool SpaceHeld() => !TestDrive && SpaceDown();

        /// <summary>Where the latest touch or click is, in screen pixels.</summary>
        public static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (TestDrive) return TestPos;
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
            if (TestDrive) { fingers = TestFingers; pos = TestPos; return; }
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

        /// <summary>A finger (or the mouse button) is down right now.</summary>
        public static bool PointerHeld()
        {
            if (TestDrive) return TestFingers > 0;
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return true;
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.touchCount > 0 || Input.GetMouseButton(0);
#endif
        }

        static bool PointerDown()
        {
            if (TestDrive) return TestFingers > 0;
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
            return a < 0.12f ? 0f : Mathf.Sign(x) * ((a - 0.12f) / 0.88f);
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
