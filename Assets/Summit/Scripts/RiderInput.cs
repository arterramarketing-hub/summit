using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Summit
{
    /// <summary>
    /// Tilt the phone to steer. Hold anywhere to tuck; let go to pop.
    /// In the editor: A/D or arrows steer, Space or the mouse button holds.
    /// Works with either the new Input System or the old Input Manager.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // read input before anything uses it this frame
    public class RiderInput : MonoBehaviour
    {
        public float Steer { get; private set; }     // -1 left .. +1 right
        public bool Hold { get; private set; }
        public bool Pressed { get; private set; }    // went down this frame
        public bool Released { get; private set; }   // came up this frame
        public float HoldTime { get; private set; }  // how long the current press has lasted
        public float LastHold { get; private set; }  // how long the press that just ended lasted
        public bool HasTilt { get; private set; }

        public float tiltRange = 0.36f;   // sin of the tilt that gives full lock (about 21 degrees)
        public float deadZone = 0.03f;
        public bool invertTilt;
        /// <summary>false = drag a finger left or right like a stick (default); true = steer by tilting the phone.</summary>
        public bool useTilt;

        float zero, twoTime;
        /// <summary>One-finger drag stick: where the finger first touched and where it is now (screen pixels).</summary>
        public bool StickActive;
        public Vector2 StickOrigin, StickPos;
        public float stickRadiusFrac = 0.09f;   // full lock at this fraction of the screen width
        bool stickPrev;
        bool wasDown, swallow;

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
            HoldTime = 0f;
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
                float d = on ? Mathf.Clamp((fingerPos.x - StickOrigin.x) / radius, -1f, 1f) : 0f;
                float a = Mathf.Abs(d);
                a = a < 0.05f ? 0f : (a - 0.05f) / 0.95f;
                target = Mathf.Sign(d) * a;
                // a second finger held down tucks; a quick overlap does not count
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
            // a short low-pass takes the jitter out of the finger or sensor without feeling laggy
            Steer = Mathf.Lerp(Steer, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * (useTilt ? 22f : 40f)));

            // hold / release
            // stick mode: a second finger (or Space) tucks; tilt mode: any press tucks
            bool down = useTilt ? PointerDown() : (twoTime > 0.22f || SpaceDown());
            if (swallow) { if (!down) swallow = false; down = false; wasDown = false; }
            Pressed = down && !wasDown;
            Released = !down && wasDown;
            if (Released) LastHold = HoldTime;
            HoldTime = down ? HoldTime + Time.unscaledDeltaTime : 0f;
            Hold = down;
            wasDown = down;
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
