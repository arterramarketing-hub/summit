using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The run is a repeating cycle of designed sections, so it reads as a course rather than noise:
    ///   Cruise  - open piste with kickers through the trees
    ///   Park    - ride-on boxes and rails you grind automatically, and a jump line
    ///   Danger  - long crevasses down the slope to steer between; a bear may come out of the trees
    ///   Pipe    - a halfpipe cut into the slope
    /// Everything here is a pure function of position, like the rest of the mountain.
    /// </summary>
    public static class Course
    {
        public enum Section { Start, Cruise, Park, Danger, Pipe }
        public const float First = 60f, Cycle = 900f;
        // where each section begins within a cycle
        public const float ParkAt = 200f, DangerAt = 380f, PipeAt = 520f, PipeEnd = 760f;

        static float Local(float z) => Mathf.Repeat(z - First, Cycle);
        public static int CycleOf(float z) => Mathf.FloorToInt((z - First) / Cycle);
        public static float CycleBase(float z) => First + CycleOf(z) * Cycle;

        public static Section At(float z)
        {
            if (z < First) return Section.Start;
            float u = Local(z);
            if (u < ParkAt) return Section.Cruise;
            if (u < DangerAt) return Section.Park;
            if (u < PipeAt) return Section.Danger;
            if (u < PipeEnd) return Section.Pipe;
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
        public static float PipeWeight(float z)
        {
            if (z < First) return 0f;
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

        public static bool InPipe(float x, float z) => PipeWeight(z) > 0.5f && Mathf.Abs(x - Slope.Centre(z)) < PipeHalfWidth + 0.5f;

        // ---------------------------------------------------------------- crevasses

        public struct Crevasse { public float x, z0, z1, half; }
        public const float CrevasseDepth = 10f;
        static readonly Dictionary<int, Crevasse[]> crevCache = new Dictionary<int, Crevasse[]>();

        public static Crevasse[] CrevassesOf(int cycle)
        {
            if (crevCache.TryGetValue(cycle, out var list)) return list;
            list = new Crevasse[5];
            float zs = First + cycle * Cycle + DangerAt + 12f;
            for (int k = 0; k < list.Length; k++)
            {
                float h1 = Slope.Hash01(cycle, k, 31), h2 = Slope.Hash01(cycle, k, 32), h3 = Slope.Hash01(cycle, k, 33);
                float z0 = zs + k * 24f + h1 * 6f;
                float len = 20f + h2 * 14f;
                // alternate sides so there is always a line between them
                float side = (k % 2 == 0 ? -1f : 1f) * (3f + h3 * 9f);
                float cx = Slope.Centre(z0 + len * 0.5f) + side;
                list[k] = new Crevasse { x = cx, z0 = z0, z1 = z0 + len, half = 2.1f + h3 * 0.8f };
            }
            if (crevCache.Count > 64) crevCache.Clear();
            crevCache[cycle] = list;
            return list;
        }

        /// <summary>How deep a crevasse cuts at (x, z): 0 almost everywhere.</summary>
        public static float CrevasseCut(float x, float z)
        {
            if (At(z) != Section.Danger) return 0f;
            foreach (var c in CrevassesOf(CycleOf(z)))
            {
                if (z < c.z0 || z > c.z1) continue;
                // the crevasse wanders a little with the piste
                float cx = c.x + (Slope.Centre(z) - Slope.Centre((c.z0 + c.z1) * 0.5f));
                float dx = Mathf.Abs(x - cx) / c.half;
                if (dx >= 1f) continue;
                float along = Mathf.Min(z - c.z0, c.z1 - z) / 3f;
                float end = Mathf.Clamp01(along);
                float across = 1f - dx * dx * dx * dx;
                return CrevasseDepth * across * end * end * (3f - 2f * end);
            }
            return 0f;
        }

        /// <summary>The crevasse at a point, if any, with its centre line x there.</summary>
        public static bool CrevasseNear(float x, float z, float margin, out float cx, out float half)
        {
            cx = 0f; half = 0f;
            if (At(z) != Section.Danger) return false;
            foreach (var c in CrevassesOf(CycleOf(z)))
            {
                if (z < c.z0 - margin || z > c.z1 + margin) continue;
                float x0 = c.x + (Slope.Centre(z) - Slope.Centre((c.z0 + c.z1) * 0.5f));
                if (Mathf.Abs(x - x0) > c.half + margin) continue;
                cx = x0; half = c.half;
                return true;
            }
            return false;
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
            list = new Feature[spotAt.Length];
            float zs = First + cycle * Cycle + ParkAt;
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
            if (CrevasseNear(x, z, margin + 2f, out _, out _)) return true;
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
