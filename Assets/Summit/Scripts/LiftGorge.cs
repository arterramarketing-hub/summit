using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The lift over the gorge: where a cycle's first line used to stop short of the canyon beside the ridge above
    /// it (its top station was below the canyons' foot), it now carries on up across it.
    ///
    /// A ridge stretch drops away on its cliff side into a canyon 124 to 198 m deep, a couple of hundred metres
    /// long and filling back in over 380 m more. No tower can stand on that cliff, nor on the canyon floor where it
    /// is deep. So the line goes over it on one long span (up to GorgeSpanMax: an aerial lift's, not a chairlift's
    /// usual 70 m), between a tall tower on the rim above the ridge stretch and one standing up from the canyon
    /// floor where the canyon has nearly filled in again (no more than GorgeFloor deep there; towers up to
    /// HMaxGorge). Its top station is above the ridge stretch, beside the previous cycle's danger stretch, on the
    /// cliff side and away from that stretch's deadly wall. Its bottom station is the cycle's own, or one at the
    /// bottom of the danger stretch, or one above it (HighAt). All of it is laid by LiftLine.Try with the same rules as any first line, these apart:
    ///   - its towers may stand in the canyons where the ground is not the cliff, its far wall, or deep floor;
    ///   - one span, the one that crosses the deep canyon, may be up to GorgeSpanMax long;
    ///   - along the crest, and where the canyons are still deep past its end, it keeps to the cliff side, at least
    ///     GorgeOffCrest out past the crest's edge;
    ///   - somewhere under that span the chairs are at least GorgeDrop over the canyon floor: a fall off the cable
    ///     there is a fall past Rider.DeathFall, judged like any other drop.
    /// It is laid after the lines above it and keeps clear of them (LiftNetwork.Crowds, Meets): no other line
    /// beside it, no station near another's. Its launch ramp, where it gets one, is on
    /// the span before the gorge, so the grind leads over the rim tower onto the gorge span (CableGrind throws the
    /// rider straight on over that tower, not out to the side). Where no such line will go the cycle's first line is
    /// laid as before. Tools/LiftHarness checks every gorge line, and rides its cable across.
    /// </summary>
    public static partial class LiftLine
    {
        public static bool Gorges = true;   // (static: the tools switch it off to compare)
        public const float GorgeSpanMax = 560.5f;     // the span over the canyon (a chairlift's are 40 to 140 m; aerial lifts span 500 m and more)
        public const float HMaxGorge = 60f;           // a tower beside the gorge (aerial lifts' run to 60 and more)
        public const float GorgeFloor = 55f;          // the deepest canyon floor a tower may stand up from
        public const float GorgeDrop = 110f;          // the least drop from the chairs to the ground somewhere on the gorge span
        public const float GorgeOffCrest = 8f;
        // a bottom station above the danger stretch, metres into its cycle (or this much higher, where the ground there will not take one)
        const float HighAt = 365f;
        static readonly float[] HighUp = { 0f, 30f, 60f };
        public const float GorgeDeep = 0.5f;          // (of a canyon's full depth: where, past the crest's end, the line must still be on the cliff side)       // through the ridge stretch, at least this far out past the crest's edge
        // where the top station goes, metres into the cycle above (beside its danger stretch, which begins at 380)
        static readonly float[] GorgeTops = { 445f, 470f, 425f, 495f };
        static readonly float[] GorgeOffsets = { 54f, 62f, 72f, 84f, 96f, 46f, 110f };
        const int GorgeTryCap = 30, GorgeTryCapAny = 90;   // (the second pass, on a mountain with no gorge line yet, tries harder)
        static bool gorgeAny;   // (the second pass: Lifts)

        /// <summary>
        /// Lay cycle k's first line over the gorge of the ridge above it, if one will go; null if not. `plain` is the
        /// line Lay found for it the ordinary way (or null): a gorge line ends at the same bottom station where it can,
        /// and the lines nearest to it in direction are tried first.
        /// </summary>
        static Lift LayGorge(int k, IList<Lift> laid, Lift plain)
        {
            if (!Gorges || k < 1 || !Range(k, out _, out float zb0)) return null;
            int g = k - 1;
            if (!Ridge.Built(g)) return null;
            float b = Course.First + g * Course.Cycle, bk = Course.First + k * Course.Cycle;
            float wall = Cliffs.WallOf(g, out var w) ? w.side : 0f;
            // through cycle k's own danger stretch, the side away from its deadly cliff (as LayTo)
            float sb = k < LastCycle && Cliffs.WallOf(k, out var wk) ? -wk.side : Slope.Hash01(k, 1, 171) < 0.5f ? -1f : 1f;
            var cands = new List<(float zt, float ot, float zb, float ob, float cs, float key)>();
            for (int e = 0; e < 2; e++)
            {
                float cs = (Slope.Hash01(g, 12, 171) < 0.5f) == (e == 0) ? -1f : 1f;
                if (!Ridge.CliffSide(b + Ridge.At + 100f, cs) || cs == wall) continue;
                // the bottoms: the ordinary line's own; one at the bottom of the danger stretch, on the side away from its
                // wall; or one above the danger stretch, either side (there it can be on the cliff's side, which keeps
                // the line there over the gorge)
                var bottoms = new List<(float z, float o, float pref)>();
                if (plain != null) bottoms.Add((plain.bottom.z, plain.offBottom, 0f));
                foreach (float ob in Offsets) bottoms.Add((zb0, sb * ob, gorgeAny ? -1f : 3f));
                if (k < LastCycle)
                    foreach (float up in HighUp)
                        foreach (float side in new[] { cs, -cs })
                            foreach (float ob in Offsets) bottoms.Add((bk + HighAt - up, side * ob, gorgeAny ? -0.5f + up * 0.01f : 1f + up * 0.01f));
                foreach (var bt in bottoms)
                    foreach (float u in GorgeTops)
                        foreach (float ot in GorgeOffsets)
                        {
                            // (a straight line between the two stations must be on the cliff's side over the deep canyon,
                            // whichever way the piste wanders meanwhile: looked at before the line is tried)
                            if (!SideFits(b + u, cs * ot, bt.z, bt.o, cs)) continue;
                            // nearest the ordinary line's direction first
                            float xt = Slope.Centre(b + u) + cs * ot, xb = Slope.Centre(bt.z) + bt.o;
                            float turn = plain != null ? Mathf.Abs(Mathf.Atan2(xb - xt, bt.z - b - u) - Mathf.Atan2(plain.dir.x, plain.dir.z)) : 0f;
                            cands.Add((b + u, cs * ot, bt.z, bt.o, cs, bt.pref + turn * 4f));
                        }
            }
            cands.Sort((p, q) => p.key.CompareTo(q.key));
            Lift fallback = null;
            int tries = 0;
            foreach (var c in cands)
            {
                // (nor is it worth trying where the ground under it has no room for a tower for longer than a span)
                if (!Bridgeable(c.zt, c.ot, c.zb, c.ob, c.cs)) continue;
                if (++tries > (gorgeAny ? GorgeTryCapAny : GorgeTryCap)) break;
                var l = Try(k, c.zt, c.zb, c.ot, c.ob, laid, c.cs);
                if (l == null) continue;
                l.tries = tries;
                PlaceRamp(l, laid);
                // the grind must lead onto the gorge: a line with its ramp there is the one; one without is kept in case
                // none has
                if (l.ramp != null && l.ramp.span < l.gorge) return l;
                if (fallback == null) { PlaceRamp(l, laid, true); fallback = l; }
            }
            return fallback;
        }

        /// <summary>
        /// Ground a gorge line's tower can stand on: anywhere a tower can (GroundOk), and in a ridge stretch's
        /// canyons on its cliff side, the canyon floor where it is no deeper than GorgeFloor, off the cliff and
        /// the far wall.
        /// </summary>
        static bool GorgeGround(float x, float z, float off, float cs)
        {
            if (!Ridge.InCanyons(z)) return GroundOk(x, z, off);
            float a = x - Slope.Centre(z);
            if (Mathf.Abs(a) < off || a * cs < 0f) return false;
            if (Course.Keep(x, z, 3f) || Course.Keep(x, z, 0.5f) || Cliffs.WallCut(x, z) < -0.01f || Slope.OnPad(x, z)) return false;
            if (Ridge.DepthHere(z, cs) > GorgeFloor) return false;
            float gx = (Slope.Height(x + 3f, z) - Slope.Height(x - 3f, z)) / 6f;
            float gz = (Slope.Height(x, z + 3f) - Slope.Height(x, z - 3f)) / 6f;
            return Mathf.Abs(gx) <= 0.55f && gz <= 0.3f && gz >= -1.25f;
        }

        /// <summary>Is the canyon deep somewhere between sa and sb along l (a span there may be a gorge span)?</summary>
        static bool OverGorge(Lift l, float sa, float sb)
        {
            for (float s = sa; s <= sb; s += 10f)
            {
                Vector3 p = l.At(s, 0f, 0f);
                if (Ridge.Depth(p.z) > 0.3f) return true;
            }
            return false;
        }

        /// <summary>Along the straight line between two stations, nowhere longer than GorgeSpanMax without ground for a tower.</summary>
        static bool Bridgeable(float zt, float ot, float zb, float ob, float cs)
        {
            float xt = Slope.Centre(zt) + ot, xb = Slope.Centre(zb) + ob;
            float len = Mathf.Sqrt((xb - xt) * (xb - xt) + (zb - zt) * (zb - zt)), last = 0f;
            for (float d = 8f; d < len; d += 8f)
            {
                float t = d / len;
                if (GorgeGround(Mathf.Lerp(xt, xb, t), Mathf.Lerp(zt, zb, t), TowerOff, cs)) last = d;
                else if (d - last > GorgeSpanMax - 30f) return false;
            }
            return true;
        }

        static bool DeepHere(float z) => Ridge.Has(z) || Ridge.Depth(z) >= GorgeDeep;

        /// <summary>GorgeSide for the straight line between two stations ot and ob off the piste at zt and zb, before it is laid.</summary>
        static bool SideFits(float zt, float ot, float zb, float ob, float cs)
        {
            float xt = Slope.Centre(zt) + ot, xb = Slope.Centre(zb) + ob;
            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                float z = Mathf.Lerp(zt, zb, t);
                if (!DeepHere(z)) continue;
                float a = (Mathf.Lerp(xt, xb, t) - Slope.Centre(z)) * cs;
                if (a < Ridge.HalfWidth(z) + Ridge.Shoulder + GorgeOffCrest) return false;
            }
            return true;
        }

        /// <summary>Through the ridge stretch the line keeps to the cliff side, out past the crest's edge.</summary>
        static bool GorgeSide(Lift l, float cs)
        {
            for (float s = 0f; s <= l.plan; s += 5f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 q = l.At(s, side * Swath, 0f);
                    if (Cliffs.WallCut(q.x, q.z) < -3f) return false;   // (nor over a deadly wall's terrace)
                }
                // (where the crest is, or the canyons are deep; past the crest's end, as they fill in, it may come in
                // over the piste)
                Vector3 p = l.At(s, 0f, 0f);
                if (!DeepHere(p.z)) continue;
                float a = (p.x - Slope.Centre(p.z)) * cs;
                if (a < Ridge.HalfWidth(p.z) + Ridge.Shoulder + GorgeOffCrest) return false;
            }
            return true;
        }

        /// <summary>After the towers are up: which span is the gorge's, and is the drop under it what it should be?</summary>
        static bool FindGorge(Lift l)
        {
            l.gorge = -1;
            float best = 0f;
            for (int i = 0; i < l.Spans; i++)
            {
                float drop = 0f;
                for (float s = l.nodeS[i]; s <= l.nodeS[i + 1]; s += 3f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 p = l.At(s, side * Half, l.CableY(i, s) - ChairDrop);
                        drop = Mathf.Max(drop, p.y - Slope.Height(p.x, p.z));
                    }
                if (drop > best) { best = drop; l.gorge = i; }
            }
            l.gorgeDrop = best;
            return l.gorge >= 0 && best >= GorgeDrop;
        }

        /// <summary>A line of text for each gorge line (SkiLift logs it with the lifts when SUMMIT_LIFTCHECK=1).</summary>
        public static string GorgeReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in Lifts)
            {
                if (l.gorge < 0) continue;
                int i = l.gorge;
                float len = l.nodeS[i + 1] - l.nodeS[i];
                Vector3 a = l.At(l.nodeS[i], 0f, 0f), b = l.At(l.nodeS[i + 1], 0f, 0f);
                float tall = 0f; foreach (float h in l.h) tall = Mathf.Max(tall, h);
                sb.AppendLine($"lift {l.k}: over the gorge of the ridge above it, top station z={l.top.z:0} ({l.offTop:+0;-0} m off the piste); gorge span {i} from z={a.z:0} to {b.z:0}, {len:0} m, sag {Sag(len):0.0} m, chairs up to {l.gorgeDrop:0} m over the ground; towers either end {(i > 0 ? l.h[i - 1] : 0f):0.0} and {(i < l.h.Length ? l.h[i] : 0f):0.0} m, tallest {tall:0.0} m; {(l.ramp != null ? $"launch ramp on span {l.ramp.span}, reached at {l.ramp.needSpeed:0} m/s" : "no launch ramp")}");
            }
            return sb.ToString();
        }
    }
}
