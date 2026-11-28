using UnityEngine;

namespace Summit
{
    /// <summary>
    /// After a fall (a crash on the mountain, off the camp's edge) the game waits, watching the body, until the player
    /// taps; nothing moves on by itself. This is that tap, as plain logic the test tools can drive: a finger put down
    /// after the fall began and lifted again quickly without moving much (a held, dragging finger is steering the
    /// tumble, not a tap), or the space bar. A finger already down when the fall began (steering, walking) is not one,
    /// even when it lifts.
    /// </summary>
    public class ResumeTap
    {
        /// <summary>A tap is lifted within this long (s), having moved less than this share of the screen's short side.</summary>
        public const float MaxTime = 0.3f, MaxTravel = 0.06f;
        bool down, counts, keyWas;
        float t; Vector2 start; float travel;

        /// <summary>The fall begins: whatever is down now doesn't count.</summary>
        public void Arm(bool fingerDown, bool keyDown)
        {
            down = fingerDown; counts = false; keyWas = keyDown; t = 0f; travel = 0f;
        }

        /// <summary>One frame: is a finger down, where, the frame's length and the screen's short side (px). True on the frame a tap ends.</summary>
        public bool Step(bool fingerDown, Vector2 pos, bool keyDown, float dt, float screen)
        {
            bool tapped = false;
            if (keyDown && !keyWas) tapped = true;
            keyWas = keyDown;
            if (fingerDown && !down) { counts = true; t = 0f; start = pos; travel = 0f; }
            if (fingerDown) { t += dt; travel = Mathf.Max(travel, (pos - start).magnitude); }
            if (!fingerDown && down && counts && t <= MaxTime && travel <= MaxTravel * Mathf.Max(1f, screen)) tapped = true;
            if (!fingerDown) counts = false;
            down = fingerDown;
            return tapped;
        }
    }
}
