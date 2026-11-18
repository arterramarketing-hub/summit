using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The run is a repeating cycle of designed sections, so it reads as a course rather than noise:
    ///   Cruise  - open piste with kickers through the trees
    ///   Park    - ride-on boxes and rails you grind automatically, and a jump line
    ///   Danger  - long crevasses down the slope to steer between, and glacier gaps across it to jump; a bear may come out of the trees
    ///   Pipe    - a halfpipe cut into the slope (switched off: see HalfpipeOn)
    ///   Ridge   - the piste narrows onto a crest with cliffs falling away beside it (in the pipe's old stretch: see Ridge)
    /// Everything here is a pure function of position, like the rest of the mountain.
    /// </summary>
    public static class Course
    {
        public enum Section { Start, Cruise, Park, Danger, Pipe, Ridge }
        public const float First = 60f, Cycle = 900f;
        /// <summary>The halfpipe is switched off: its stretch of each cycle is open cruising piste instead.</summary>
        public const bool HalfpipeOn = false;
        // where each section begins within a cycle
        public const float ParkAt = 200f, DangerAt = 380f, PipeAt = 520f, PipeEnd = 760f;

        static float Local(float z) => Mathf.Repeat(z - First, Cycle);
        public static int CycleOf(float z) => Mathf.FloorToInt((z - First) / Cycle);
        public static float CycleBase(float z) => First + CycleOf(z) * Cycle;

        public static Section At(float z)
        {
            if (z < First) return Section.Start;
            if (z > Slope.FinishZ - 220f) return Section.Cruise;   // the last stretch to the finish is clear
            float u = Local(z);
            if (u < ParkAt) return Section.Cruise;
            if (u < DangerAt) return Section.Park;
            if (u < PipeAt) return Section.Danger;
            if (Ridge.Has(z)) return Section.Ridge;
            if (u < PipeEnd) return HalfpipeOn ? Section.Pipe : Section.Cruise;
            return Section.Cruise;
        }

        /// <summary>Distance to the start of the next section of a kind (for signs).</summary>
        public static float NextStart(float z, Section s)
        {
            float at = s == Section.Park ? ParkAt : s == Section.Danger ? DangerAt : PipeAt;
            float c = Mathf.Floor((z - First) / Cycle);
            float z0 = First + c * Cycle + at;
            if (z0 < z) z0 += Cycle;
            return z0;
        }

        // ---------------------------------------------------------------- halfpipe

        public const float PipeFlat = 6f, PipeR = 6f, PipeCap = 0.93f, PipeDeck = 5f, PipeRamp = 30f;
        public static float PipeWallTop => PipeR - Mathf.Sqrt(PipeR * PipeR - (PipeCap * PipeR) * (PipeCap * PipeR));
        public static float PipeHalfWidth => PipeFlat + PipeCap * PipeR;

        /// <summary>0 outside the pipe, 1 inside, easing in and out at the ends.</summary>
        /// <summary>Nothing is built past here: the last stretch to the finish is a clean run.</summary>
        public static float ClearFrom => Slope.FinishZ - 260f;

        public static float PipeWeight(float z)
        {
            if (!HalfpipeOn || z < First) return 0f;
            float pipeStart = z - Local(z) + PipeAt;
            if (pipeStart + (PipeEnd - PipeAt) > ClearFrom) return 0f;   // a pipe that would run into the finish is not built
            float u = Local(z);
            if (u < PipeAt || u > PipeEnd) return 0f;
            float w = Mathf.Min((u - PipeAt) / PipeRamp, (PipeEnd - u) / PipeRamp);
            w = Mathf.Clamp01(w);
            return w * w * (3f - 2f * w);
        }

        /// <summary>Height of the pipe's cross-section above its floor, at distance a from the centre line.</summary>
        public static float PipeProfile(float a)
        {
            float d = Mathf.Abs(a) - PipeFlat;
            if (d <= 0f) return 0f;
            float cap = PipeCap * PipeR;
            if (d < cap) return PipeR - Mathf.Sqrt(PipeR * PipeR - d * d);
            return PipeWallTop;   // the deck
        }

        public static bool InPipe(float x, float z) => PipeWeight(z) > 0.15f && Mathf.Abs(x - Slope.Centre(z)) < PipeHalfWidth + 0.5f;

        // ---------------------------------------------------------------- crevasses
        // Two kinds, a mix in each danger stretch:
        //   down the slope - long narrow slots running down the fall line, to steer between
        //   across the slope - glacier gaps: a long crack across most of the run (sometimes all of it), to jump
        //                      or ollie, or cross on a snow bridge where it pinches shut
        // Each has an along axis (z for a down-slope one, x for a gap) and opens across it: Opening gives its
        // half-width and centre line at a point along it.

        public static void ResetCaches() { crevCache.Clear(); featCache.Clear(); }

        public struct Crevasse
        {
            public float x, z0, z1, half;   // down-slope: centre x at its middle, top and bottom z, half its width.
                                            // across: z0/z1 bound the z it can reach (for quick rejects)
            public bool across;             // a glacier gap across the slope (perpendicular to the fall line)
            public float zc, x0, x1;        // across: its line's z at the middle, and its ends in x
            public float tilt, phase;       // across: a slight slant (dz/dx) and the wander of its line
            public float bridge, bridgeHalf;// across: a snow bridge's x and half span (bridgeHalf 0: none)
            /// <summary>The ends along its length: z for a down-slope crevasse, x for a gap.</summary>
            public float Lo => across ? x0 : z0;
            public float Hi => across ? x1 : z1;
            /// <summary>Where along it a point is.</summary>
            public float Along(float px, float pz) => across ? px : pz;
            /// <summary>Where across it a point is (x for a down-slope crevasse, z for a gap).</summary>
            public float Across(float px, float pz) => across ? pz : px;
            /// <summary>A point given how far along and across it (the inverse of Along/Across).</summary>
            public Vector2 Point(float along, float acrossAt) => across ? new Vector2(along, acrossAt) : new Vector2(acrossAt, along);
        }
        public const float CrevasseDepth = 28f;
        static readonly Dictionary<int, Crevasse[]> crevCache = new Dictionary<int, Crevasse[]>();

        public static Crevasse[] CrevassesOf(int cycle)
        {
            if (crevCache.TryGetValue(cycle, out var list)) return list;
            // rare: one or two per danger zone, well apart
            float zs = First + cycle * Cycle + DangerAt + 18f;
            if (zs + 140f > ClearFrom) { list = new Crevasse[0]; crevCache[cycle] = list; return list; }
            // the very first danger stretch shows one of each kind; after that a mix
            int n = cycle == 0 || Slope.Hash01(cycle, 9, 31) >= 0.45f ? 2 : 1;
            list = new Crevasse[n];
            float kindRoll = Slope.Hash01(cycle, 9, 34);   // which one (if any) is a gap across the slope
            int gapAt = cycle == 0 ? 1 : n == 1 ? (kindRoll < 0.6f ? 0 : -1) : kindRoll < 0.2f ? -1 : kindRoll < 0.6f ? 1 : 0;
            for (int k = 0; k < list.Length; k++)
            {
                float h1 = Slope.Hash01(cycle, k, 31), h2 = Slope.Hash01(cycle, k, 32), h3 = Slope.Hash01(cycle, k, 33);
                float z0 = zs + k * 60f + h1 * 20f;
                if (k == gapAt) { list[k] = Gap(cycle, k, GapSpot(z0 + 4f, z0 + 24f)); continue; }
                float len = 20f + h2 * 14f;
                // alternate sides so there is always a line between them
                float side = (k % 2 == 0 ? -1f : 1f) * (3f + h3 * 9f);
                float cx = Slope.Centre(z0 + len * 0.5f) + side;
                list[k] = new Crevasse { x = cx, z0 = z0, z1 = z0 + len, half = 2.3f + h3 * 1.2f };
            }
            if (crevCache.Count > 64) crevCache.Clear();
            crevCache[cycle] = list;
            return list;
        }

        /// <summary>
        /// Where in a stretch of the run a gap is fair: not on a brow, where the slope steepens past it and hides it
        /// until you are on it, but where the run ahead flattens or holds its pitch, so you see the far lip coming.
        /// </summary>
        static float GapSpot(float za, float zb)
        {
            float best = za, bestScore = float.MaxValue;
            for (float z = za; z <= zb; z += 2f)
            {
                float c = Slope.Centre(z);
                float h = Slope.Surface(c, z);
                float before = (Slope.Surface(Slope.Centre(z - 14f), z - 14f) - h) / 14f;   // the drop per metre into it
                float after = (h - Slope.Surface(Slope.Centre(z + 10f), z + 10f)) / 10f;     // and past it
                float score = after - before + 0.004f * Mathf.Abs(z - (za + zb) * 0.5f);   // (a little toward the middle)
                if (score < bestScore) { bestScore = score; best = z; }
            }
            return best;
        }

        /// <summary>
        /// A glacier gap across the run at zc: from the cliff's edge (or past the piste) on one side to well past the
        /// piste on the other, narrow enough to clear with an ollie at normal speed (3.4 to 4.4 m), sometimes with a
        /// snow bridge where it pinches shut.
        /// </summary>
        static Crevasse Gap(int cycle, int k, float zc)
        {
            float h4 = Slope.Hash01(cycle, k, 35), h5 = Slope.Hash01(cycle, k, 36), h6 = Slope.Hash01(cycle, k, 37), h7 = Slope.Hash01(cycle, k, 38);
            float c = Slope.Centre(zc);
            // it reaches the deadly cliff's lip on that side (stopping just short of it), and runs out into the open
            // mountain on the other
            float left = Slope.PisteHalf + 9f + h4 * 10f, right = Slope.PisteHalf + 9f + h5 * 10f;
            if (Cliffs.WallNear(zc, 4f, out var w))
            {
                if (w.side < 0f) left = w.edge - 1.5f; else right = w.edge - 1.5f;
            }
            var g = new Crevasse
            {
                across = true, zc = zc, x0 = c - left, x1 = c + right,
                half = 1.7f + h6 * 0.5f,
                tilt = (h7 - 0.5f) * 0.12f, phase = h4 * 40f,
            };
            g.x = c;
            // half of them have a snow bridge, somewhere on the piste: a 4-6 m crossing marked with wands
            if (Slope.Hash01(cycle, k, 39) < 0.5f)
            {
                g.bridge = c + (Slope.Hash01(cycle, k, 40) - 0.5f) * 2f * (Slope.PisteHalf - 6f);
                g.bridgeHalf = 2f + Slope.Hash01(cycle, k, 41) * 1f;
            }
            float reach = Mathf.Abs(g.tilt) * Mathf.Max(left, right) + 1.2f + g.half + 0.5f;
            g.z0 = zc - reach; g.z1 = zc + reach;
            return g;
        }

        /// <summary>
        /// Half the crevasse's width at `along` (it pinches shut at both ends, and over a snow bridge), and the line
        /// of its middle there (x for a down-slope crevasse, z for a gap).
        /// </summary>
        public static float Opening(Crevasse c, float along, out float centre)
        {
            if (!c.across)
            {
                // the crevasse wanders a little with the piste
                centre = c.x + (Slope.Centre(along) - Slope.Centre((c.z0 + c.z1) * 0.5f));
                if (along <= c.z0 || along >= c.z1) return 0f;
                return c.half * Mathf.Sqrt(Mathf.Clamp01(Mathf.Min(along - c.z0, c.z1 - along) / 5f));
            }
            // a gap: slanting a touch, wandering a little, its width breathing along it
            float u = along - c.x;
            centre = c.zc + c.tilt * u + 0.45f * Mathf.Sin(u * 0.19f + c.phase) + 0.25f * Mathf.Sin(u * 0.47f + c.phase * 1.7f);
            if (along <= c.x0 || along >= c.x1) return 0f;
            float o = c.half * (0.9f + 0.1f * Mathf.Sin(u * 0.31f + c.phase * 0.6f));
            o *= Mathf.Sqrt(Mathf.Clamp01(Mathf.Min(along - c.x0, c.x1 - along) / 5f));
            if (c.bridgeHalf > 0f) o *= Mathf.Sqrt(Mathf.Clamp01((Mathf.Abs(along - c.bridge) - c.bridgeHalf) / 3f));
            return o;
        }

        /// <summary>How deep a crevasse cuts at (x, z): 0 almost everywhere.</summary>
        public static float CrevasseCut(float x, float z)
        {
            if (At(z) != Section.Danger) return 0f;
            foreach (var c in CrevassesOf(CycleOf(z)))
            {
                if (z < c.z0 || z > c.z1) continue;
                if (c.across && (x <= c.x0 || x >= c.x1)) continue;
                float open = Opening(c, c.Along(x, z), out float ctr);
                if (open <= 0.01f) continue;
                float dx = Mathf.Abs(c.Across(x, z) - ctr) / open;
                if (dx >= 1f) continue;
                // sheer walls: full depth right up to the lip
                float d2 = dx * dx, d4 = d2 * d2;
                return CrevasseDepth * (1f - d4 * d4) * Mathf.Sqrt(open / c.half);
            }
            return 0f;
        }

        /// <summary>The crevasse at or near a point (within margin of its full width), if any.</summary>
        public static bool CrevasseNear(float x, float z, float margin, out Crevasse found)
        {
            found = default;
            if (At(z) != Section.Danger && At(z - margin) != Section.Danger && At(z + margin) != Section.Danger) return false;
            int cyc = CycleOf(z);
            foreach (var c in CrevassesOf(cyc))
            {
                if (z < c.z0 - margin || z > c.z1 + margin) continue;
                if (c.across)
                {
                    if (x < c.x0 - margin || x > c.x1 + margin) continue;
                    // over a snow bridge's middle is not near it
                    if (c.bridgeHalf > 0f && Mathf.Abs(x - c.bridge) < c.bridgeHalf - margin) continue;
                }
                Opening(c, c.Along(x, z), out float ctr);
                if (Mathf.Abs(c.Across(x, z) - ctr) > c.half + margin) continue;
                found = c;
                return true;
            }
            return false;
        }

        public static bool CrevasseNear(float x, float z, float margin) => CrevasseNear(x, z, margin, out _);

        /// <summary>
        /// A line check: the next glacier gap straight down the run from (x, z) within `within` metres, and the z of
        /// its uphill lip at x. Over a snow bridge (or past a gap's end) there is none to jump.
        /// </summary>
        public static bool GapAhead(float x, float z, float within, out Crevasse gap, out float lipZ)
        {
            gap = default; lipZ = 0f;
            int c1 = CycleOf(z + within);
            for (int c = Mathf.Max(0, CycleOf(z)); c <= c1; c++)
                foreach (var cv in CrevassesOf(c))
                {
                    if (!cv.across || x <= cv.x0 || x >= cv.x1) continue;
                    float open = Opening(cv, x, out float ctr);
                    if (open < 0.4f) continue;
                    float lip = ctr - open;
                    if (lip < z - 0.5f || lip > z + within) continue;
                    gap = cv; lipZ = lip;
                    return true;
                }
            return false;
        }

        /// <summary>
        /// How a body that rode into a crevasse goes in: a drop, not a throw. It keeps a little of its speed along
        /// the crevasse, almost none across it (or it would sail over a gap to the far lip), and none upward.
        /// </summary>
        public static Vector3 CrevasseDrop(Vector3 p, Vector3 v)
        {
            if (!CrevasseNear(p.x, p.z, 1.5f, out var c)) return v * 0.4f;
            Vector3 ax = c.across ? Vector3.right : Vector3.forward;
            Vector3 along = Vector3.Project(new Vector3(v.x, 0f, v.z), ax);
            Vector3 across = new Vector3(v.x, 0f, v.z) - along;
            return along * 0.4f + across * 0.05f + Vector3.up * Mathf.Min(0f, v.y) * 0.4f;
        }

        /// <summary>The point on a crevasse's middle line level with (x, z) (its floor's deepest line).</summary>
        public static Vector3 CrevasseMiddle(Crevasse c, Vector3 p)
        {
            float along = c.Along(p.x, p.z);
            Opening(c, along, out float ctr);
            var q = c.Point(along, ctr);
            return new Vector3(q.x, p.y, q.y);
        }

        /// <summary>Out of a crevasse onto its nearer rim, `clear` metres back from the lip (y unchanged).</summary>
        public static Vector3 CrevasseRim(Crevasse c, Vector3 p, float clear)
        {
            float along = c.Along(p.x, p.z);
            Opening(c, along, out float ctr);
            float w = c.Across(p.x, p.z);
            // out of a gap you climb back up the hill side you came from unless you are already past the middle
            float s = Mathf.Sign(w - ctr + 0.01f);
            var q = c.Point(along, ctr + s * (c.half + clear));
            return new Vector3(q.x, p.y, q.y);
        }

        // ---------------------------------------------------------------- park features

        public enum Kind { Box, Rail }
        public struct Feature
        {
            public Kind kind;
            public Vector3 a, b;     // ends of the top line (the board rides along it)
            public float halfWidth, height;
            public int id;
            public Vector3 Dir => (b - a).normalized;
            public float Length => Vector3.Distance(a, b);
        }

        // spots along the park (metres from its start): boxes and rails, with two kickers between
        static readonly float[] spotAt = { 18f, 48f, 108f, 138f };
        static readonly Kind[] spotKind = { Kind.Box, Kind.Rail, Kind.Box, Kind.Rail };
        static readonly float[] spotSide = { -6f, 5f, 4f, -5f };
        public static readonly float[] ParkKickers = { 78f, 166f };
        static readonly Dictionary<int, Feature[]> featCache = new Dictionary<int, Feature[]>();

        public static Feature[] FeaturesOf(int cycle)
        {
            if (featCache.TryGetValue(cycle, out var list)) return list;
            float zs = First + cycle * Cycle + ParkAt;
            if (zs + 180f > ClearFrom) { list = new Feature[0]; featCache[cycle] = list; return list; }
            list = new Feature[spotAt.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var k = spotKind[i];
                float len = k == Kind.Box ? 13f : 15f;
                float h = k == Kind.Box ? 0.45f : 0.65f;
                float z0 = zs + spotAt[i], z1 = z0 + len;
                float x0 = Slope.Centre(z0) + spotSide[i], x1 = Slope.Centre(z1) + spotSide[i];
                list[i] = new Feature
                {
                    kind = k, height = h, halfWidth = k == Kind.Box ? 0.32f : 0.05f, id = cycle * 16 + i,
                    a = new Vector3(x0, BaseHeight(x0, z0) + h, z0),
                    b = new Vector3(x1, BaseHeight(x1, z1) + h, z1),
                };
            }
            if (featCache.Count > 64) featCache.Clear();
            featCache[cycle] = list;
            return list;
        }

        static float BaseHeight(float x, float z) => Slope.Height(x, z);

        public static IEnumerable<Feature> FeaturesNear(float z)
        {
            int c = CycleOf(z);
            for (int k = c - 1; k <= c + 1; k++)
            {
                if (k < 0) continue;
                foreach (var f in FeaturesOf(k))
                    if (z > f.a.z - 60f && z < f.b.z + 60f) yield return f;
            }
        }

        /// <summary>True where trees, rocks, pickups or kickers must stay away.</summary>
        public static bool Keep(float x, float z, float margin)
        {
            if (PipeWeight(z) > 0f && Mathf.Abs(x - Slope.Centre(z)) < PipeHalfWidth + PipeDeck + 6f + margin) return true;
            if (CrevasseNear(x, z, margin + 2f)) return true;
            if (Ridge.Keep(x, z, margin)) return true;
            if (Cliffs.Keep(x, z, margin)) return true;
            if (At(z) == Section.Park || At(z + 20f) == Section.Park)
                foreach (var f in FeaturesNear(z))
                {
                    if (z < f.a.z - 12f - margin || z > f.b.z + 6f + margin) continue;
                    float t = Mathf.Clamp01((z - f.a.z) / (f.b.z - f.a.z));
                    float fx = Mathf.Lerp(f.a.x, f.b.x, t);
                    if (Mathf.Abs(x - fx) < 2.5f + margin) return true;
                }
            return false;
        }
    }
}
