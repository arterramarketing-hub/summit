using UnityEngine;

namespace Summit
{
    /// <summary>How the one finger jumps (the GAME tab's JUMP setting).</summary>
    public enum JumpStyle { DoubleTap, Tap, FlickUp }

    /// <summary>
    /// What one finger is doing, frame by frame: touching down, lifting, a tap (down and up quickly without
    /// moving), a double tap (a second touch close by, soon after a tap: it fires the moment the second finger
    /// lands, with nothing waited for), a quick flick up, a swipe (groundspin: put down, thrown sideways a good way
    /// and lifted, all quickly; it fires as it lifts). RiderInput feeds it the steering finger (or, with tilt
    /// steering, any finger) and turns those into the jump, the pop off a rail and the grab by context.
    /// No Unity input of its own: the test tools drive it the same way.
    /// </summary>
    public class FingerGesture
    {
        /// <summary>A press this short (s), that has hardly moved, is a tap.</summary>
        public const float TapMax = 0.22f;
        /// <summary>A double tap: the second touch lands within this long (s) of the first tap lifting.</summary>
        public const float DoubleGap = 0.28f;
        /// <summary>A flick: the finger travels up at least FlickMin of the screen's height within this long (s).</summary>
        public const float FlickWindow = 0.14f;
        // fractions of the screen: a tap moves less than TapSlop of its width; the second tap of a double lands
        // within PairSlop of the first; a flick climbs FlickMin of its height
        public const float TapSlop = 0.04f, PairSlop = 0.15f, FlickMin = 0.07f;
        /// <summary>A swipe (groundspin): down and lifted within SwipeMax s, SwipeMin of the screen's width across (past
        /// the steering's full lock, 0.22) and at least twice as far across as up or down. Steering is a finger held on;
        /// a swipe is let go of.</summary>
        public const float SwipeMax = 0.3f, SwipeMin = 0.25f;

        // ---- this frame
        public bool Began, Ended, Tap, DoubleTap, Flick;
        /// <summary>A swipe just lifted: -1 left, +1 right (0 with none); and how far across it went, in screen widths.</summary>
        public int Swipe;
        public float SwipeLength;
        /// <summary>This touch landed close to where the finger lifted, soon after (it carries on from where it was).</summary>
        public bool Resumed;
        public bool Down { get; private set; }
        /// <summary>How long the finger has been up (0 while it is down).</summary>
        public float SinceUp { get; private set; } = 99f;
        /// <summary>How long the finger has been down, and how far (pixels) it has moved since it landed.</summary>
        public float Held { get; private set; }
        public float Travel { get; private set; }
        /// <summary>This press already counted (a double tap's second touch, or swallowed): it is nothing else.</summary>
        public bool Consumed => consumed;

        float h, peakY; Vector2 downPos, upPos, tapPos, last;
        bool chain, flicked, consumed;
        const int N = 32;
        readonly float[] ht = new float[N];
        readonly Vector2[] hp = new Vector2[N];
        int hn, hi;

        /// <summary>Forget everything in progress (a pause, a new run).</summary>
        public void Reset()
        {
            chain = false; consumed = Down; flicked = true; SinceUp = 99f;
            Began = Ended = Tap = DoubleTap = Flick = Resumed = false; Swipe = 0;
        }

        /// <summary>The press in progress counts for nothing (the tap that closed a menu, that got the rider up).</summary>
        public void Swallow() { consumed = true; chain = false; flicked = true; }

        /// <summary>That tap was used (a grab in the air): it does not start a double tap.</summary>
        public void CancelChain() { chain = false; }

        public void Step(bool down, Vector2 pos, float dt, Vector2 screen)
        {
            Began = Ended = Tap = DoubleTap = Flick = Resumed = false; Swipe = 0;
            float w = Mathf.Max(screen.x, 320f), hgt = Mathf.Max(screen.y, 480f);
            h += dt;
            if (down && !Down)
            {
                Began = true; Held = 0f; Travel = 0f; downPos = pos; last = pos; flicked = false; consumed = false;
                hn = 0;
                Resumed = SinceUp <= DoubleGap && (pos - upPos).magnitude <= PairSlop * w;
                if (chain && SinceUp <= DoubleGap && (pos - tapPos).magnitude <= PairSlop * w) { DoubleTap = true; consumed = true; }
                chain = false;
                SinceUp = 0f;
            }
            else if (!down && Down)
            {
                Ended = true;
                if (!consumed && Held <= TapMax && Travel <= TapSlop * w) { Tap = true; tapPos = downPos; chain = true; }
                Vector2 thrown = last - downPos;
                if (!consumed && Held <= SwipeMax && Mathf.Abs(thrown.x) >= SwipeMin * w && Mathf.Abs(thrown.x) >= 2f * Mathf.Abs(thrown.y))
                { Swipe = thrown.x > 0f ? 1 : -1; SwipeLength = Mathf.Abs(thrown.x) / w; }
                upPos = last; SinceUp = 0f;
            }
            if (down)
            {
                Held += dt;
                Travel = Mathf.Max(Travel, (pos - downPos).magnitude);
                // a flick: up the screen fast, mostly straight up, once per climb
                hp[hi] = pos; ht[hi] = h; hi = (hi + 1) % N; hn = Mathf.Min(hn + 1, N);
                float lowY = pos.y; float lowX = pos.x;
                for (int k = 1; k <= hn; k++)
                {
                    int j = (hi - k + N) % N;
                    if (h - ht[j] > FlickWindow) break;
                    if (hp[j].y < lowY) { lowY = hp[j].y; lowX = hp[j].x; }
                }
                float rise = pos.y - lowY;
                if (!flicked && !consumed && rise >= FlickMin * hgt && rise > 2f * Mathf.Abs(pos.x - lowX)) { Flick = true; flicked = true; peakY = pos.y; }
                // (back down a way: another can come, counted from there)
                if (flicked) { peakY = Mathf.Max(peakY, pos.y); if (pos.y < peakY - FlickMin * hgt * 0.5f) { flicked = false; hn = 0; } }
                last = pos;
            }
            else
            {
                SinceUp += dt;
                if (SinceUp > DoubleGap) chain = false;
            }
            Down = down;
        }
    }
}
