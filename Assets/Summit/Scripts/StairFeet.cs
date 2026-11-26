using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Feet on the camp's stairs (campstairs): the walk clips step on level ground, and the body goes up and down the
    /// stairs on a smoothed line (Camp's floor there), so each foot is lifted or lowered by how far the tread under it
    /// is above or below that line, the hips sinking as far as the lower foot needs to reach its tread, and the legs
    /// bent to suit (HeroRig.LegTo). A foot that's down stays where it came down (a clip's stride is never quite a
    /// whole number of treads: it would slip over a nosing). Off the stairs it eases away to nothing. The hero
    /// (RiderBodyHero) and the camp's people (Person, whose own feet lock already) both stand this way.
    /// </summary>
    public static class StairFeet
    {
        /// <summary>One body's feet on the stairs, kept between frames.</summary>
        public class State
        {
            public Vector2 off;                          // each foot's lift (left, right) over the body's floor
            public Vector2 tread = new Vector2(float.NaN, float.NaN);   // the tread under each foot (world height), eased
            public readonly bool[] down = new bool[2];   // held where it came down
            public readonly Vector3[] at = new Vector3[2];
        }

        /// <summary>A foot's reach behind and ahead of its ankle (m, at scale 1): heel and toes.</summary>
        public const float Heel = 0.07f, Toe = 0.15f;

        /// <summary>
        /// Set the rig's feet on the treads under them. ground: the floor the body stands on (the level its clips'
        /// feet are on); facing: the way it faces; hold: keep a foot that's down where it came down (the hero; the
        /// camp's people lock their own).
        /// </summary>
        public static void Plant(HeroRig h, float ground, Vector3 facing, State st, float dt, float scale = 1f, bool hold = true)
        {
            if (h == null || !h.Ok || h.Foot[0] == null || h.Foot[1] == null || st == null) return;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) return;
            facing.Normalize();
            Vector3 up = Vector3.up;
            var a = new[] { h.Foot[0].position, h.Foot[1].position };
            bool any = false;
            // (eased in the world, not against the body, which climbs a metre a second on them: a foot passing over a
            // nosing comes up to the next tread, not in one frame, and quicker up than down)
            float kUp = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * 70f), kDown = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * 50f);
            for (int s = 0; s < 2; s++)
            {
                bool on = !float.IsNaN(Camp.TreadUnder(a[s]));
                // down on the clip's floor: held there (unless the body has gone on without it: a turn on the spot)
                bool contact = a[s].y - ground < 0.11f * scale;
                if (hold && on && contact)
                {
                    if (!st.down[s]) { st.down[s] = true; st.at[s] = a[s]; }
                    else if (new Vector2(st.at[s].x - a[s].x, st.at[s].z - a[s].z).sqrMagnitude < 0.3f * 0.3f * scale * scale) { a[s].x = st.at[s].x; a[s].z = st.at[s].z; }
                    else st.at[s] = a[s];
                }
                else st.down[s] = false;
                // (a foot in the air looks further ahead for the next riser: it comes up over the nosing, not into it)
                float want = ground + Under(a[s], facing, ground, scale, contact ? 0f : 0.12f, out bool over);
                float was = s == 0 ? st.tread.x : st.tread.y;
                float now;
                if (!over)
                {
                    // off the treads: what lift is left eases away against the body's own floor. (Eased in the world, a
                    // step up onto a floor, the lookout's deck, left the tread behind under it: the hips and feet dropped
                    // through the boards for a frame or two, by as much as the step: campfix2)
                    float o = s == 0 ? st.off.x : st.off.y;
                    now = ground + (float.IsNaN(was) ? 0f : Mathf.Lerp(o, 0f, kDown));
                }
                else now = float.IsNaN(was) || Mathf.Abs(was - want) > 1f ? want : Mathf.Lerp(was, want, want > was ? kUp : kDown);
                if (s == 0) { st.tread.x = now; st.off.x = now - ground; } else { st.tread.y = now; st.off.y = now - ground; }
                any |= on;
            }
            bool moved = hold && (st.down[0] || st.down[1]);
            if (!any && Mathf.Abs(st.off.x) < 0.002f && Mathf.Abs(st.off.y) < 0.002f) { st.off = Vector2.zero; return; }
            if (!moved && Mathf.Abs(st.off.x) < 0.002f && Mathf.Abs(st.off.y) < 0.002f) return;
            Quaternion f0 = h.Foot[0].rotation, f1 = h.Foot[1].rotation;
            Vector3 k0 = h.LowerLeg[0].position, k1 = h.LowerLeg[1].position;
            // the hips down as far as the lower foot has to reach below the line
            float drop = Mathf.Max(0f, -Mathf.Min(st.off.x, st.off.y));
            if (drop > 0f) h.Hips.position -= up * drop;
            h.LegTo(0, a[0] + up * st.off.x, k0 + facing * 0.2f * scale + up * Mathf.Max(0f, st.off.x));
            h.LegTo(1, a[1] + up * st.off.y, k1 + facing * 0.2f * scale + up * Mathf.Max(0f, st.off.y));
            h.Foot[0].rotation = f0; h.Foot[1].rotation = f1;
        }

        /// <summary>How far the tread under a foot (its heel and its toes: the higher) is above the body's floor; 0 off the stairs.</summary>
        static float Under(Vector3 ankle, Vector3 facing, float ground, float scale, float ahead, out bool over)
        {
            float heel = Camp.TreadUnder(ankle - facing * (Heel * scale)), toe = Camp.TreadUnder(ankle + facing * ((Toe + ahead) * scale));
            over = !float.IsNaN(heel) || !float.IsNaN(toe);
            if (!over) return 0f;
            // (half off the stairs, that half stands on the body's own floor: coming off the plateau onto the top deck,
            // below the bank, the heel still on the bank kept the foot up there, not down at the deck in the snow: campfix2)
            float t = Mathf.Max(float.IsNaN(heel) ? ground : heel, float.IsNaN(toe) ? ground : toe);
            return Mathf.Clamp(t - ground, -0.5f, 0.5f);
        }
    }
}
