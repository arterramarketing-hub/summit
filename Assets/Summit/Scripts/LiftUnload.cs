using UnityEngine;

namespace Summit
{
    /// <summary>
    /// A top station's unload wing (liftlook): the deck made much wider on one side, so a rider coming down the hill
    /// sees an open ramp beside the chairs to ride and jump off, not a lane a metre and a half wide between the chairs
    /// and a railing.
    ///
    /// The station's deck sits just over the highest snow under it, so its back end (up the hill) meets the snow and its
    /// front stands high over the falling mountain: off the front is a launch. Its chairs run through the middle, 0.6 m
    /// over the deck, and a chair reaches ChairReach out from the line (SkiLift.Chairs' hit box), so on the old deck the
    /// only way past them was the 1.3 m between that and the railing. The wing is level with the deck, WingWidth wide
    /// past the deck's side (no railing between them: unloaders glide straight out onto it), from where the snow comes
    /// up to it behind the station (BackMax at most) to WingFront past the station's front, ending in a lip of packed
    /// snow (LipLen long, LipRise high) that kicks the rider up a little: a rider coming down the hill rides straight
    /// onto it off the snow. From the chairs' reach to its outer edge it is WingWidth + 1.4 m clear, five chairs wide.
    ///
    /// Which side: where it fits (the snow not up through it beside the station, nothing standing there: another lift,
    /// a hamlet, the camp's pad, the park's features, a cliff's wall, a cable grind's hill or the lane to it), first one
    /// the snow comes up to behind (access), then one whose landing no other lift crosses (CrossedAt), then the run's
    /// side (where the unloaders go: LiftEnds moves their flat out past the wing) if their flat still fits between the
    /// wing and the run, else the far side. Where the snow is above the deck beside the station, the wing starts
    /// further forward (z0), the snow meeting it there. None at the Summit lift's top (the camp's pad: LiftTop) or where
    /// neither side will do.
    ///
    /// The lane (InUnload): the wing and the strip of ground the rider comes in on from behind it and flies over off its
    /// lip, LandLength down the line: cleared of trees and rocks (Taken, for the forest and everything placed beside the
    /// piste) and kept clear of the lift's own furniture (LiftEnds). A pure function of the lift, worked out once.
    /// LiftUnloadCheck measures the clear width and flies the jump line at every speed a rider has.
    /// </summary>
    public static partial class LiftLine
    {
        public const float WingWidth = 10f, WingFront = 8f, LipLen = 3f, LipRise = 0.5f;
        /// <summary>How deep the wing's deck is under its snow (its snow-clad sides hang this far, or bank down to the snow
        /// where it is nearer: Roofs.ApronSlope), and how far past its outer edge its lane reaches (the bank's foot).</summary>
        public const float WingDepth = 2.5f, LaneOut = 4.5f;
        /// <summary>How far out from the line a chair can reach a rider (SkiLift.Chairs: its box 1.4 m either side of
        /// the cable, and its slow roll).</summary>
        public const float ChairReach = Half + 1.5f;
        public const float ApproachLength = 30f, LandLength = 120f;
        /// <summary>How far back up the hill past the station's back end a wing may reach to meet the snow.</summary>
        public const float BackMax = 20f;
        /// <summary>How fast the cleared landing widens outward past the lip (tan 6 degrees: a rider turned that far off
        /// straight still lands in it).</summary>
        public const float LandFan = 0.105f;

        public class Wing
        {
            public float s;            // which side, as a sign along the lift's `across` (+1: the up-going chairs' side)
            public bool runSide;       // on the run's side
            public bool access;        // the snow comes up to its back end (a rider coming down rides onto it)
            public float crossedAt = float.MaxValue;   // how far out past the lip another lift's tower or chairs are first met (CrossedAt)
            public float z0, z1;       // along the line from the deck's middle: the wing's back end, and its lip's foot
            public float Lip => z1 + LipLen;   // the lip's end, where the rider takes off
            public float x0 => StationHalfWid;              // out from the line, on its side: its inner edge (the deck's)
            public float x1 => StationHalfWid + WingWidth;  // and its outer edge
            public float Clear => x1 - ChairReach;   // the clear width beside the chairs
            /// <summary>A point of the wing's frame: x out from the line on its side, z along the line, y over the deck.</summary>
            public Vector3 P(Lift l, float x, float y, float z) => l.top + l.across * (s * x) + l.dir * z + Vector3.up * y;
        }


        /// <summary>The unload wing at lift l's top (null: none).</summary>
        public static Wing UnloadWing(Lift l)
        {
            if (l.wingDone) return l.wing;
            l.wingDone = true;
            l.wing = null;
            if (l.summit) { l.wingWhy = "summit"; return null; }
            float a = l.top.x - Slope.Centre(l.top.z);
            float sRun = Vector3.Dot(l.across, Vector3.right) * (a > 0f ? -1f : 1f) >= 0f ? 1f : -1f;
            string why = "";
            // (the run's side first, where the unloaders go, if their flat still fits between the wing and the run:
            // LiftEnds moves it out past the wing; otherwise the far side first, and the flat stays where it was)
            bool room = FlatRoom(l, sRun);
            // (of the sides it fits: one the snow comes up to behind, so a rider coming down rides onto it, before one
            // it stands in the air behind; then one whose landing no other lift crosses, or crosses furthest out; then
            // the order above)
            Wing best = null; int bestScore = int.MinValue;
            foreach (float s in room ? new[] { sRun, -sRun } : new[] { -sRun, sRun })
            {
                var w = new Wing { s = s, runSide = s == sRun, z1 = StationHalfLen + WingFront - LipLen };
                if (!Fits(l, w, out string no)) { why += (why.Length > 0 ? "; " : "") + no; continue; }
                w.crossedAt = CrossedAt(l, w);
                int score = (w.access ? 1000 : 0) + (w.crossedAt < float.MaxValue ? Mathf.Clamp(Mathf.RoundToInt(w.crossedAt), 0, 500) : 900) + (best == null ? 1 : 0);
                if (w.crossedAt < float.MaxValue) why += (why.Length > 0 ? "; " : "") + $"{(w.runSide ? "run" : "far")} side crossed {w.crossedAt:0} m out";
                if (!w.access) why += (why.Length > 0 ? "; " : "") + $"{(w.runSide ? "run" : "far")} side in the air behind";
                if (score > bestScore) { best = w; bestScore = score; }
            }
            if (best != null) return Take(l, best, why);
            l.wingWhy = "none: " + why;
            return null;
        }

        static Wing Take(Lift l, Wing w, string why)
        {
            l.wing = w;
            l.wingWhy = (w.runSide ? "run side" : "far side") + $", from {w.z0:0.0}" + (w.access ? "" : " (in the air behind)") + (why.Length > 0 ? $" [{why}]" : "");
            return w;
        }

        /// <summary>
        /// Does another lift's tower stand in the wing's landing, or its line cross it low enough for its chairs to be met: anywhere along the
        /// strip off the lip (LandLength), where its cable is over the strip, a chair (ChairDrop under the cable) reaching
        /// down below the lip's height and its rider's head above the snow? (A rider in the air off the lip is between
        /// the snow and a metre over the lip.) How far past the lip the first such thing is (float.MaxValue: none); a
        /// side with none is taken first, then the one whose is furthest.
        /// </summary>
        static float CrossedAt(Lift l, Wing w)
        {
            float near = float.MaxValue;
            float lipY = l.top.y + LipRise + 1.2f;
            var all = Lifts;
            // (another lift's tower standing in the landing, its head and all)
            for (int i = 0; i < all.Length; i++)
            {
                var o = all[i];
                if (o == l) continue;
                for (int k = 0; k < o.foot.Length; k++)
                {
                    float dx = o.foot[k].x - l.top.x, dz = o.foot[k].z - l.top.z;
                    float along = dx * l.dir.x + dz * l.dir.z, side = (dx * l.across.x + dz * l.across.z) * w.s;
                    float reach = Half + 3.5f;   // (its head's crossarm and sheave trains)
                    if (along > w.Lip - reach && along < w.Lip + LandLength && side > w.x0 - reach && side < w.x1 + reach + (along - w.Lip) * LandFan) near = Mathf.Min(near, Mathf.Max(0f, along - reach - w.Lip));
                }
            }
            for (float z = w.Lip; z <= w.Lip + LandLength; z += 2f)
                for (float x = w.x0; x <= w.x1 + 0.01f; x += 2.5f)
                {
                    var p = w.P(l, x, 0f, z);
                    float snow = Slope.DrawnHeight(p.x, p.z);
                    for (int i = 0; i < all.Length; i++)
                    {
                        var o = all[i];
                        if (o == l) continue;
                        float along = (p.x - o.top.x) * o.dir.x + (p.z - o.top.z) * o.dir.z;
                        if (along < -StationHalfLen || along > o.plan + StationHalfLen) continue;
                        float side = Mathf.Abs((p.x - o.top.x) * o.across.x + (p.z - o.top.z) * o.across.z);
                        if (side > Half + 1.5f + 2.5f) continue;
                        float cable = o.top.y + StationCable;
                        if (along > o.nodeS[o.Spans]) cable = o.bottom.y + StationCable;
                        else if (along >= o.nodeS[0])
                        {
                            for (int k = 0; k < o.Spans; k++) if (along <= o.nodeS[k + 1]) { cable = o.CableY(k, along); break; }
                        }
                        if (cable - 4.5f - 0.75f < lipY && cable - 0.9f > snow) near = Mathf.Min(near, z - w.Lip);
                    }
                }
            return near;
        }

        /// <summary>Is there room on side s between a wing and the run for the unloaders' flat (LiftEnds: FlatOut past the
        /// wing's outer edge, behind the station)?</summary>
        static bool FlatRoom(Lift l, float s)
        {
            for (float x = StationHalfWid + WingWidth + 2f; x <= StationHalfWid + WingWidth + FlatOut + 9f; x += 2f)
                for (float z = -StationHalfLen - 16f; z <= -StationHalfLen - 2f; z += 3.5f)
                {
                    var p = l.top + l.across * (s * x) + l.dir * z;
                    if (Mathf.Abs(Slope.Across(p.x, p.z)) < Slope.Half(p.z) + 3f) return false;
                }
            return true;
        }

        /// <summary>How far out past a run-side wing's outer edge the unloaders' flat begins (LiftEnds).</summary>
        public const float FlatOut = 6.5f;

        /// <summary>Does the wing fit on its side, and where does its back end start (w.z0)?</summary>
        static bool Fits(Lift l, Wing w, out string no)
        {
            float deck = l.top.y;
            // its back end: back from the lip, level, until the snow comes up to it all the way across, near enough under it
            // for a bank of snow to ease a board up (Roofs.RampSlope over RampMax) or over it (up the hill behind the
            // station as far as BackMax; where it never does, the row it comes nearest, and it stands in the air behind
            // there). In front of that the snow may come up through it in places: there the snow is the ground (Roofs).
            float reach = Roofs.RampSlope * Roofs.RampMax - 0.1f;
            float z0 = -StationHalfLen, bestLow = float.MaxValue;
            w.access = false;
            for (float z = w.Lip; z >= -StationHalfLen - BackMax - 0.01f; z -= 0.5f)
            {
                float low = 0f;   // (how far the snow is under the deck at the lowest point across, more than the bank reaches)
                for (float x = w.x0; x <= w.x1 + 0.01f; x += 1f)
                {
                    var p = w.P(l, x, 0f, z);
                    low = Mathf.Max(low, deck - reach - Slope.DrawnHeight(p.x, p.z));
                }
                if (low <= 0f) { z0 = z; w.access = true; break; }
                if (low < bestLow - 0.01f && z <= -StationHalfLen) { bestLow = low; z0 = z; }
            }
            no = "snow";
            if (z0 > StationHalfLen) return false;   // (buried past the station's front: the snow beside it is the wide way, no wing)
            // (and not buried from front to back: the snow must leave most of it standing)
            int buried = 0, rows = 0;
            for (float z = z0; z <= w.z1; z += 1f)
            {
                var p = w.P(l, (w.x0 + w.x1) * 0.5f, 0f, z);
                rows++; if (Slope.DrawnHeight(p.x, p.z) > deck) buried++;
            }
            if (buried * 2 > rows) return false;
            w.z0 = z0;
            // and nothing standing where it would
            for (float z = w.z0; z <= w.Lip + 0.01f; z += 1f)
                for (float x = w.x0; x <= w.x1 + 0.01f; x += 1f)
                {
                    var p = w.P(l, x, 0f, z);
                    no = "lift"; if (TakenByOthers(l, p.x, p.z, 1f)) return false;
                    no = "pad"; if (Slope.OnPad(p.x, p.z) || LiftTop.Covers(p.x, p.z, 1f)) return false;
                    no = "kept"; if (Course.Keep(p.x, p.z, 1f) || Cliffs.WallCut(p.x, p.z) < -0.01f || Ridge.InCanyons(p.z)) return false;
                    no = "hamlet"; if (Hamlets.Taken(p.x, p.z)) return false;
                    no = "hill"; if (HillOrLane(p.x, p.z, 1f)) return false;
                    no = "run"; if (Mathf.Abs(Slope.Across(p.x, p.z)) < Slope.Half(p.z) + 1f) return false;
                }
            no = "";
            return true;
        }

        /// <summary>Under another lift's line or on its stations (TakenBy, leaving out l).</summary>
        static bool TakenByOthers(Lift l, float x, float z, float margin)
        {
            var all = Lifts;
            for (int i = 0; i < all.Length; i++)
            {
                var o = all[i];
                if (o == l) continue;
                float along = (x - o.top.x) * o.dir.x + (z - o.top.z) * o.dir.z;
                if (along < -StationHalfLen - 6f - margin || along > o.plan + StationHalfLen + 6f + margin) continue;
                float side = (x - o.top.x) * o.across.x + (z - o.top.z) * o.across.z;
                if (Mathf.Abs(side) < Swath + margin) return true;
            }
            return false;
        }

        /// <summary>
        /// How groomed the snow is here for a top's unload lane: 1 on it (the approach, the wing and the landing, from the
        /// chairs' reach to the bank's foot past its outer edge, widening with the landing's fan), fading out smoothly over
        /// GroomFade round it. The lane is packed as the run is (Surfaces), so the wing's snow, drawn with the terrain's
        /// own, carries the run's corduroy. Smooth everywhere (SurfaceTest: no share of the snow may change by more than
        /// 0.75 a metre): the fade is a smoothstep of the distance out of the lane, at most 1.5 / GroomFade a metre.
        /// </summary>
        public static float UnloadGroom(float x, float z)
        {
            var all = Lifts;
            float most = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                var l = all[i];
                if (l.summit) continue;
                float dz = z - l.top.z, dx = x - l.top.x;
                if (dx * dx + dz * dz > 170f * 170f) continue;
                var w = UnloadWing(l);
                if (w == null) continue;
                float along = dx * l.dir.x + dz * l.dir.z, side = (dx * l.across.x + dz * l.across.z) * w.s;
                float a0 = w.z0 - ApproachLength, a1 = w.Lip + LandLength;
                float outer = w.x1 + LaneOut + Mathf.Max(0f, along - w.Lip) * LandFan;
                float ox = Mathf.Max(0f, Mathf.Max(ChairReach - side, side - outer)), oz = Mathf.Max(0f, Mathf.Max(a0 - along, along - a1));
                float d = Mathf.Sqrt(ox * ox + oz * oz);
                if (d >= GroomFade) continue;
                most = Mathf.Max(most, 1f - Mathf.SmoothStep(0f, 1f, d / GroomFade));
            }
            return most;
        }
        public const float GroomFade = 8f;

        /// <summary>
        /// In a top's unload lane: the wing, the ground behind it the rider comes in on (ApproachLength), and the strip it
        /// flies over off the lip (LandLength), from the chairs' reach to past the wing's outer edge. x, z on the snow.
        /// </summary>
        public static bool InUnload(float x, float z, float margin)
        {
            var all = Lifts;
            for (int i = 0; i < all.Length; i++)
            {
                var l = all[i];
                if (l.summit) continue;
                float dz = z - l.top.z, dx = x - l.top.x;
                if (dx * dx + dz * dz > 130f * 130f) continue;
                var w = UnloadWing(l);
                if (w == null) continue;
                float along = dx * l.dir.x + dz * l.dir.z, side = (dx * l.across.x + dz * l.across.z) * w.s;
                if (along < w.z0 - ApproachLength - margin || along > w.Lip + LandLength + margin) continue;
                float fan = along > w.Lip ? (along - w.Lip) * LandFan : 0f;
                // (behind the station, where the wing reaches up the hill, the ground between it and the line too)
                float inner = along < -StationHalfLen ? -Swath : ChairReach;
                if (side > inner - margin && side < w.x1 + LaneOut + fan + margin) return true;
            }
            return false;
        }
    }
}
