using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The run is a repeating cycle of designed sections, so it reads as a course rather than noise:
    ///   Cruise  - open piste with kickers through the trees
    ///   Park    - ride-on boxes and rails you grind automatically, and a jump line
    ///   Danger  - long crevasses down the slope to steer between; a bear may come out of the trees
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
        // Long narrow slots running down the fall line, to steer between: two to a danger stretch, one on each side
        // of the piste and overlapping down most of it, so there is always a clear lane down the middle and one
        // down each edge.

        public static void ResetCaches() { crevCache.Clear(); featCache.Clear(); }

        public struct Crevasse { public float x, z0, z1, half; }   // centre x at its middle, top and bottom z, half its width
        public const float CrevasseDepth = 28f;
        static readonly Dictionary<int, Crevasse[]> crevCache = new Dictionary<int, Crevasse[]>();

        public static Crevasse[] CrevassesOf(int cycle)
        {
            if (crevCache.TryGetValue(cycle, out var list)) return list;
            float zs = First + cycle * Cycle + DangerAt + 14f;
            if (zs + 140f > ClearFrom) { list = new Crevasse[0]; crevCache[cycle] = list; return list; }
            // the danger stretch has 120 m of room before the next section: 50 to 85 m long each (2-3 times what
            // they were), the second starting part way down the first, so they overlap
            const float room = 120f;
            list = new Crevasse[Slope.Hash01(cycle, 9, 31) < 0.25f ? 1 : 2];
            float first = Slope.Hash01(cycle, 9, 35) < 0.5f ? -1f : 1f;   // which side the first is on
            for (int k = 0; k < list.Length; k++)
            {
                float h1 = Slope.Hash01(cycle, k, 31), h2 = Slope.Hash01(cycle, k, 32), h3 = Slope.Hash01(cycle, k, 33);
                float z0 = zs + (k == 0 ? h1 * 8f : 22f + h1 * 14f);
                float len = Mathf.Min(50f + h2 * 35f, zs + room - z0);
                float half = 2.3f + h3 * 1.2f;
                // well off the middle (7 to 11 m): at least 7 m of lane between the two, and 3.5 m or more to the
                // piste's edge outside each
                float side = (k == 0 ? first : -first) * (7f + Slope.Hash01(cycle, k, 34) * 4f);
                float cx = Slope.Centre(z0 + len * 0.5f) + side;
                list[k] = new Crevasse { x = cx, z0 = z0, z1 = z0 + len, half = half };
            }
            if (crevCache.Count > 64) crevCache.Clear();
            crevCache[cycle] = list;
            return list;
        }

        /// <summary>Half the crevasse's width at z (it pinches shut at both ends), and its centre line there.</summary>
        public static float Opening(Crevasse c, float z, out float cx)
        {
            // the crevasse wanders a little with the piste
            cx = c.x + (Slope.Centre(z) - Slope.Centre((c.z0 + c.z1) * 0.5f));
            if (z <= c.z0 || z >= c.z1) return 0f;
            return c.half * Mathf.Sqrt(Mathf.Clamp01(Mathf.Min(z - c.z0, c.z1 - z) / 5f));
        }

        /// <summary>How deep a crevasse cuts at (x, z): 0 almost everywhere.</summary>
        public static float CrevasseCut(float x, float z)
        {
            if (At(z) != Section.Danger) return 0f;
            foreach (var c in CrevassesOf(CycleOf(z)))
            {
                if (z < c.z0 || z > c.z1) continue;
                float open = Opening(c, z, out float cx);
                if (open <= 0.01f) continue;
                float dx = Mathf.Abs(x - cx) / open;
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
            foreach (var c in CrevassesOf(CycleOf(z)))
            {
                if (z < c.z0 - margin || z > c.z1 + margin) continue;
                Opening(c, z, out float cx);
                if (Mathf.Abs(x - cx) > c.half + margin) continue;
                found = c;
                return true;
            }
            return false;
        }

        public static bool CrevasseNear(float x, float z, float margin) => CrevasseNear(x, z, margin, out _);

        /// <summary>
        /// How a body that rode into a crevasse goes in: a drop, not a throw. It keeps a little of its speed down
        /// the crevasse, almost none across it, and none upward.
        /// </summary>
        public static Vector3 CrevasseDrop(Vector3 p, Vector3 v)
        {
            if (!CrevasseNear(p.x, p.z, 1.5f)) return v * 0.4f;
            return new Vector3(v.x * 0.05f, Mathf.Min(0f, v.y) * 0.4f, v.z * 0.4f);
        }

        /// <summary>The point on a crevasse's middle line level with p (its floor's deepest line).</summary>
        public static Vector3 CrevasseMiddle(Crevasse c, Vector3 p)
        {
            Opening(c, p.z, out float cx);
            return new Vector3(cx, p.y, p.z);
        }

        /// <summary>Out of a crevasse onto its nearer rim, `clear` metres back from the lip (y unchanged).</summary>
        public static Vector3 CrevasseRim(Crevasse c, Vector3 p, float clear)
        {
            Opening(c, p.z, out float cx);
            return new Vector3(cx + Mathf.Sign(p.x - cx + 0.01f) * (c.half + clear), p.y, p.z);
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

        // The park (180 m): two lanes either side of the jump line down the middle. Down the left a box, then later a
        // rail; down the right a rail, then later a box; the two big kickers in the middle between them. Rails are 45 m
        // long (three times what they were), a metre off the snow and thicker; boxes 26 m (twice), 0.7 m high and a
        // metre wide, so both read as big features from up the hill. Each follows the snow (FeaturePoint).
        public const float RailLength = 45f, BoxLength = 26f, RailHeight = 1.0f, BoxHeight = 0.7f;
        public const float RailHalfWidth = 0.08f, BoxHalfWidth = 0.55f;
        static readonly float[] spotAt = { 10f, 20f, 104f, 120f };
        static readonly Kind[] spotKind = { Kind.Box, Kind.Rail, Kind.Rail, Kind.Box };
        static readonly float[] spotSide = { -7f, 7f, -7f, 7f };
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
                float len = k == Kind.Box ? BoxLength : RailLength;
                float h = k == Kind.Box ? BoxHeight : RailHeight;
                float z0 = zs + spotAt[i], z1 = z0 + len;
                float x0 = Slope.Centre(z0) + spotSide[i], x1 = Slope.Centre(z1) + spotSide[i];
                var a = new Vector3(x0, BaseHeight(x0, z0) + h, z0);
                var b = new Vector3(x1, BaseHeight(x1, z1) + h, z1);
                list[i] = new Feature
                {
                    kind = k, height = h, halfWidth = k == Kind.Box ? BoxHalfWidth : RailHalfWidth, id = cycle * 16 + i,
                    a = a, b = b,
                };
            }
            if (featCache.Count > 64) featCache.Clear();
            featCache[cycle] = list;
            return list;
        }

        /// <summary>
        /// The top of a feature, t of the way along it (0..1). Straight in plan, it follows the snow at its height
        /// over it: a straight top 45 m long stood up to 5 m off a rolling slope, or buried itself in a bulge.
        /// </summary>
        public static Vector3 FeaturePoint(Feature f, float t)
        {
            t = Mathf.Clamp01(t);
            float x = Mathf.Lerp(f.a.x, f.b.x, t), z = Mathf.Lerp(f.a.z, f.b.z, t);
            return new Vector3(x, BaseHeight(x, z) + f.height, z);
        }

        /// <summary>Which way a feature's top runs at t (down its line, following the snow).</summary>
        public static Vector3 FeatureDir(Feature f, float t)
        {
            float e = 0.6f / Mathf.Max(1f, f.Length);
            return (FeaturePoint(f, Mathf.Min(1f, t + e)) - FeaturePoint(f, Mathf.Max(0f, t - e))).normalized;
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
            return NearFeature(x, z, margin);
        }

        /// <summary>On or beside a park feature, or on the run-in to one.</summary>
        public static bool NearFeature(float x, float z, float margin)
        {
            if (At(z) != Section.Park && At(z + 20f) != Section.Park) return false;
            foreach (var f in FeaturesNear(z))
            {
                if (z < f.a.z - 12f - margin || z > f.b.z + 6f + margin) continue;
                float t = Mathf.Clamp01((z - f.a.z) / (f.b.z - f.a.z));
                float fx = Mathf.Lerp(f.a.x, f.b.x, t);
                if (Mathf.Abs(x - fx) < 2.5f + f.halfWidth + margin) return true;
            }
            return false;
        }
    }
}
