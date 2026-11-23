using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The run is a repeating cycle of designed sections, so it reads as a course rather than noise:
    ///   Cruise  - open piste with kickers through the trees
    ///   Park    - ride-on boxes and rails you grind automatically, and a jump line
    ///   Danger  - the deadly cliff beside the piste; a bear may come out of the trees (crevasses lie on the glacier
    ///             up the mountain, wherever it is: see Glacier)
    ///   Pipe    - a halfpipe cut into the slope (switched off: see HalfpipeOn)
    ///   Ridge   - the piste narrows onto a crest with cliffs falling away beside it (in the pipe's old stretch: see Ridge)
    /// Everything here is a pure function of position, like the rest of the mountain.
    /// </summary>
    public static class Course
    {
        public enum Section { Start, Cruise, Park, Danger, Pipe, Ridge, Band, Bowl }
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
            if (CliffBand.Has(z)) return Section.Band;
            if (Bowl.Has(z)) return Section.Bowl;
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

        // ---------------------------------------------------------------- crevasses: the glacier
        // The upper mountain is glacier (GlacierTop..GlacierBottom, the open snow of the piste and its margins), and
        // its crevasses lie the way real ones do (crevasses.md):
        //   transverse - fields of cracks across the slope where the run steepens over a convex break: a varied count,
        //                irregularly spaced, of varied length and width, gently bowed down the hill
        //   chevron    - short cracks along the piste's edges, slanting in from the margin and up the hill
        //   bergschrund- one long crack near the top, at the foot of the steep summit face
        //   splaying   - now and then, a few cracks down the fall line, fanning out, where the run flattens
        // Some cracks are partly spanned, or wholly hidden, by sagging snow bridges that hold a rider going at speed.
        // Every crack is a straight line in plan (bowed and wandering a little), with its own along (u) and across
        // (w) axes; Opening gives its half-width and the line of its middle at a point along it.

        public static void ResetCaches() { Slope.BeforeChange(); crevFor = int.MinValue; featCache.Clear(); Slope.ForgetDrops(); Cliffs.ForgetLedges(); CliffBand.Forget(); Bowl.Forget(); }

        public enum CrevasseKind { Transverse, Chevron, Bergschrund, Splay }

        public struct Crevasse
        {
            public CrevasseKind kind;
            public float ax, az, dx, dz;   // the start of its line, and the unit direction along it (plan)
            public float len, half, depth; // its length, its widest half-width, and how deep it is at its widest
            public float bow, phase;       // how far its middle is bowed off the straight line (m, toward +w), its wander
            public float b0, b1, c0, c1;   // up to two snow bridges, as spans along it (b1 <= b0: none)
            public float x0, x1, z0, z1;   // its plan bounds, lips and all
            public int id, field;          // its place in Crevasses, and the field it is part of (-1: none)
            /// <summary>How far along its line a point is.</summary>
            public float Along(float x, float z) => (x - ax) * dx + (z - az) * dz;
            /// <summary>How far to the side of its straight line a point is (+ to the left of its direction).</summary>
            public float Across(float x, float z) => -(x - ax) * dz + (z - az) * dx;
            /// <summary>The point u along it and w across (the inverse of Along and Across), as (x, z).</summary>
            public Vector2 Point(float u, float w) => new Vector2(ax + dx * u - dz * w, az + dz * u + dx * w);
            /// <summary>Is u along it under a snow bridge?</summary>
            public bool Bridged(float u) => (u > b0 && u < b1) || (u > c0 && u < c1);
            public bool HasBridge => b1 > b0 || c1 > c0;
        }

        /// <summary>
        /// The glacier: from the foot of the summit face (about 9,900 ft) down to about 6,500 ft, a third of the way down
        /// the run, over the piste and its margins (the forest starts beyond them).
        /// </summary>
        public const float GlacierTop = 40f, GlacierBottom = 1480f, GlacierHalf = Slope.PisteHalf + 14f;
        public const float CrevasseDepth = 28f;
        /// <summary>A snow bridge holds a rider going this fast (m/s); slower, it gives way under the board.</summary>
        public const float BridgeHolds = 7f;
        /// <summary>A crack no wider than twice this is carried across at a normal riding speed (the board flies on).</summary>
        public const float CarryHalf = 1.4f;

        public static bool InGlacier(float z) => z > GlacierTop - 10f && z < GlacierBottom + 10f;

        static Crevasse[] crevs = new Crevasse[0];
        static int[][] rows;                 // per 8 m of run: the cracks whose bounds reach into it
        const float RowLen = 8f;
        static int crevFor = int.MinValue;
        static bool laying;
        /// <summary>The crevasses are being laid out just now (Glacier.Build reads the snow: it reads it without the lifts' hills, LiftLine.HillHeight).</summary>
        public static bool Laying => laying;

        /// <summary>Every crevasse on today's mountain (laid out the first time it is asked for).</summary>
        public static Crevasse[] Crevasses { get { Lay(); return crevs; } }

        static void Lay()
        {
            if (crevFor == Slope.Seed || laying) return;
            laying = true;   // (while they are laid, nothing that asks about the ground sees half a glacier)
            try
            {
                var list = new List<Crevasse>();
                Glacier.Build(list);
                for (int i = 0; i < list.Count; i++) { var c = list[i]; c.id = i; list[i] = c; }
                crevs = list.ToArray();
                int n = Mathf.CeilToInt((GlacierBottom + 60f - GlacierTop + 60f) / RowLen);
                var rl = new List<int>[n];
                for (int i = 0; i < crevs.Length; i++)
                {
                    int r0 = Mathf.Max(0, RowOf(crevs[i].z0)), r1 = Mathf.Min(n - 1, RowOf(crevs[i].z1));
                    for (int r = r0; r <= r1; r++) (rl[r] ??= new List<int>()).Add(i);
                }
                rows = new int[n][];
                for (int r = 0; r < n; r++) rows[r] = rl[r] != null ? rl[r].ToArray() : System.Array.Empty<int>();
                crevFor = Slope.Seed;
            }
            finally { laying = false; }
        }

        static int RowOf(float z) => Mathf.FloorToInt((z - (GlacierTop - 60f)) / RowLen);

        /// <summary>The cracks whose bounds reach the 8 m of run at z (none outside the glacier).</summary>
        static int[] Near(float z)
        {
            if (!InGlacier(z)) return System.Array.Empty<int>();
            Lay();
            if (rows == null) return System.Array.Empty<int>();
            int r = RowOf(z);
            return r >= 0 && r < rows.Length ? rows[r] : System.Array.Empty<int>();
        }

        /// <summary>The crevasses of a course cycle (any part of them in it), for what is built cycle by cycle.</summary>
        public static Crevasse[] CrevassesOf(int cycle)
        {
            float a = First + cycle * Cycle, b = a + Cycle;
            if (cycle == 0) a = float.MinValue;   // (the first cycle owns everything above it, the bergschrund too)
            var all = Crevasses;
            var list = new List<Crevasse>();
            foreach (var c in all) { float zm = (c.z0 + c.z1) * 0.5f; if (zm >= a && zm < b) list.Add(c); }
            return list.ToArray();
        }

        /// <summary>
        /// Half the crack's width at u along it (it runs out to a point at both ends), and how far its middle line is
        /// to the side of its straight line there.
        /// </summary>
        public static float Opening(Crevasse c, float u, out float mid)
        {
            float s = Mathf.Clamp01(u / c.len);
            mid = c.bow * Mathf.Sin(Mathf.PI * s) + 0.3f * Mathf.Min(1f, c.half) * Mathf.Sin(u * 0.29f + c.phase);
            if (u <= 0f || u >= c.len) return 0f;
            // pointed at both ends, the way a crack runs out to nothing (a rounded end read as a pool from up the hill)
            float taper = Mathf.Min(9f, c.len * 0.38f);
            float t = Mathf.Clamp01(Mathf.Min(u, c.len - u) / taper);
            float o = c.half * t * (2f - t);
            return o * (0.82f + 0.18f * Mathf.Sin(u * 0.17f + c.phase * 1.3f));
        }

        /// <summary>How deep a crevasse cuts at (x, z): 0 almost everywhere. (Under a snow bridge too: the bridge is drawn over it.)</summary>
        public static float CrevasseCut(float x, float z)
        {
            var near = Near(z);
            float best = 0f;
            for (int i = 0; i < near.Length; i++)
            {
                ref var c = ref crevs[near[i]];
                if (x < c.x0 || x > c.x1 || z < c.z0 || z > c.z1) continue;
                float u = c.Along(x, z);
                float open = Opening(c, u, out float mid);
                if (open <= 0.01f) continue;
                float dx = Mathf.Abs(c.Across(x, z) - mid) / open;
                if (dx >= 1f) continue;
                // sheer walls: full depth right up to the lip
                float d2 = dx * dx, d4 = d2 * d2;
                best = Mathf.Max(best, c.depth * (1f - d4 * d4) * Mathf.Sqrt(open / c.half));
            }
            return best;
        }

        /// <summary>
        /// Within r of a crack's opening (its pointed ends too)? For the terrain's mesh, which leaves a hole for any quad
        /// that touches one: testing only its corners left the narrow ends covered, and the cracks looked cut off square.
        /// </summary>
        public static bool NearOpening(float x, float z, float r)
        {
            var near = Near(z);
            for (int i = 0; i < near.Length; i++)
            {
                ref var c = ref crevs[near[i]];
                if (x < c.x0 - r || x > c.x1 + r || z < c.z0 - r || z > c.z1 + r) continue;
                float u = c.Along(x, z);
                if (u < -r || u > c.len + r) continue;
                float uc = Mathf.Clamp(u, 0f, c.len);
                float open = Opening(c, uc, out float mid);
                float du = u - uc, dw = Mathf.Max(0f, Mathf.Abs(c.Across(x, z) - mid) - open);
                if (du * du + dw * dw < r * r) return true;
            }
            return false;
        }

        /// <summary>The crevasse at or near a point (within margin of its full width, bridged or not), if any.</summary>
        public static bool CrevasseNear(float x, float z, float margin, out Crevasse found)
        {
            found = default;
            if (laying || z < GlacierTop - 70f - margin || z > GlacierBottom + 70f + margin) return false;
            Lay();
            int r0 = Mathf.Max(0, RowOf(z - margin)), r1 = Mathf.Min(rows.Length - 1, RowOf(z + margin));
            for (int r = r0; r <= r1; r++)
            {
                var near = rows[r];
                for (int i = 0; i < near.Length; i++)
                {
                    ref var c = ref crevs[near[i]];
                    if (x < c.x0 - margin || x > c.x1 + margin || z < c.z0 - margin || z > c.z1 + margin) continue;
                    float u = c.Along(x, z);
                    if (u < -margin || u > c.len + margin) continue;
                    Opening(c, u, out float mid);
                    if (Mathf.Abs(c.Across(x, z) - mid) > c.half + margin) continue;
                    found = c;
                    return true;
                }
            }
            return false;
        }

        public static bool CrevasseNear(float x, float z, float margin) => CrevasseNear(x, z, margin, out _);

        /// <summary>Over a crack's opening where a snow bridge spans it.</summary>
        public static bool OnBridge(float x, float z)
        {
            var near = Near(z);
            for (int i = 0; i < near.Length; i++)
            {
                ref var c = ref crevs[near[i]];
                if (!c.HasBridge || x < c.x0 || x > c.x1 || z < c.z0 || z > c.z1) continue;
                float u = c.Along(x, z);
                if (!c.Bridged(u)) continue;
                float open = Opening(c, u, out float mid);
                if (open > 0.01f && Mathf.Abs(c.Across(x, z) - mid) < open + 0.3f) return true;
            }
            return false;
        }

        /// <summary>
        /// Is there snow to ride on at (x, z) at this speed? Everywhere but a crack's opening (one narrow enough for the
        /// speed is skimmed: SkimTime); over a snow bridge only going fast enough (a slow board breaks through).
        /// </summary>
        public static bool Solid(float x, float z, float speed) =>
            CrevasseCut(x, z) <= 0.3f || 2f * OpenAt(x, z) < speed * SkimTime || (speed >= BridgeHolds && OnBridge(x, z));

        /// <summary>
        /// A board crosses an opening it spans in less than this (s) without sinking into it more than a few
        /// centimetres, so it skims it at the snow's level. (Flown, it sank a hand's width below the far lip and was
        /// snapped back up onto it in one step: a jolt. Nothing it skims could have swallowed it: that takes 0.6 m.)
        /// </summary>
        public const float SkimTime = 0.14f;

        /// <summary>The half-width of the opening at (x, z): 0 off every crack.</summary>
        public static float OpenAt(float x, float z)
        {
            var near = Near(z);
            float best = 0f;
            for (int i = 0; i < near.Length; i++)
            {
                ref var c = ref crevs[near[i]];
                if (x < c.x0 || x > c.x1 || z < c.z0 || z > c.z1) continue;
                float u = c.Along(x, z);
                float open = Opening(c, u, out float mid);
                if (open > 0.01f && Mathf.Abs(c.Across(x, z) - mid) < open) best = Mathf.Max(best, open);
            }
            return best;
        }

        /// <summary>
        /// How a body that rode into a crevasse goes in: a drop, not a throw. It keeps a little of its speed along
        /// the crack, almost none across it, and none upward.
        /// </summary>
        public static Vector3 CrevasseDrop(Vector3 p, Vector3 v)
        {
            if (!CrevasseNear(p.x, p.z, 1.5f, out var c)) return v * 0.4f;
            var along = new Vector3(c.dx, 0f, c.dz);
            var flat = new Vector3(v.x, 0f, v.z);
            var a = along * Vector3.Dot(flat, along);
            return a * 0.4f + (flat - a) * 0.05f + Vector3.up * Mathf.Min(0f, v.y) * 0.4f;
        }

        /// <summary>
        /// For the test tools: the widest crack on the glacier (or of one kind), and a point on its middle line on the
        /// piste where no bridge spans it, as near its middle as there is one.
        /// </summary>
        public static bool WidestCrack(out Crevasse found, out Vector3 at, CrevasseKind? kind = null)
        {
            found = default; at = Vector3.zero;
            int best = -1;
            var all = Crevasses;
            for (int i = 0; i < all.Length; i++)
                if ((kind == null || all[i].kind == kind) && (best < 0 || all[i].half > all[best].half)) best = i;
            if (best < 0) return false;
            found = all[best];
            at = CrackSpot(found);
            return true;
        }

        /// <summary>A point on a crack's middle line, on the piste and not under a bridge, as near its middle as there is one.</summary>
        public static Vector3 CrackSpot(Crevasse c)
        {
            float bestScore = float.MaxValue; Vector3 at = Vector3.zero;
            for (float u = c.len * 0.15f; u <= c.len * 0.85f; u += 0.5f)
            {
                if (c.Bridged(u)) continue;
                Opening(c, u, out float mid);
                var q = c.Point(u, mid);
                float off = Mathf.Abs(q.x - Slope.Centre(q.y));
                float score = off + Mathf.Abs(u - c.len * 0.5f) * 0.2f;
                if (off < Slope.PisteHalf - 2f && score < bestScore) { bestScore = score; at = new Vector3(q.x, Slope.Surface(q.x, q.y), q.y); }
            }
            if (bestScore == float.MaxValue) { Opening(c, c.len * 0.5f, out float m); var q = c.Point(c.len * 0.5f, m); at = new Vector3(q.x, Slope.Surface(q.x, q.y), q.y); }
            return at;
        }

        /// <summary>The point on a crevasse's middle line level with p (its floor's deepest line), y unchanged.</summary>
        public static Vector3 CrevasseMiddle(Crevasse c, Vector3 p)
        {
            float u = Mathf.Clamp(c.Along(p.x, p.z), 0f, c.len);
            Opening(c, u, out float mid);
            var q = c.Point(u, mid);
            return new Vector3(q.x, p.y, q.y);
        }

        /// <summary>Out of a crevasse onto its nearer rim, `clear` metres back from the lip (y unchanged).</summary>
        public static Vector3 CrevasseRim(Crevasse c, Vector3 p, float clear)
        {
            float u = Mathf.Clamp(c.Along(p.x, p.z), 0f, c.len);
            Opening(c, u, out float mid);
            float s = Mathf.Sign(c.Across(p.x, p.z) - mid + 0.01f);
            var q = c.Point(u, mid + s * (c.half + clear));
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
        /// <summary>A roof's ridge (Roofs) is a feature numbered from here: clear of the course's own and the lifts' cables (negative).</summary>
        public const int RoofIdBase = 1 << 20;

        public static Vector3 FeaturePoint(Feature f, float t)
        {
            t = Mathf.Clamp01(t);
            float x = Mathf.Lerp(f.a.x, f.b.x, t), z = Mathf.Lerp(f.a.z, f.b.z, t);
            // (a roof's ridge is straight and stands where it was built: Roofs)
            if (f.id >= RoofIdBase) return new Vector3(x, Mathf.Lerp(f.a.y, f.b.y, t) + f.height, z);
            return new Vector3(x, BaseHeight(x, z) + f.height, z);
        }

        /// <summary>Which way a feature's top runs at t (down its line, following the snow).</summary>
        public static Vector3 FeatureDir(Feature f, float t)
        {
            float e = 0.6f / Mathf.Max(1f, f.Length);
            return (FeaturePoint(f, Mathf.Min(1f, t + e)) - FeaturePoint(f, Mathf.Max(0f, t - e))).normalized;
        }

        static float BaseHeight(float x, float z) => Slope.Height(x, z);

        /// <summary>
        /// The park features within 60 m of z, into `into` (cleared first). A list the caller keeps, not an iterator:
        /// the rider asks every physics step through the park, and an iterator was a new object each time.
        /// </summary>
        public static void FeaturesNear(float z, List<Feature> into)
        {
            into.Clear();
            int c = CycleOf(z);
            for (int k = c - 1; k <= c + 1; k++)
            {
                if (k < 0) continue;
                foreach (var f in FeaturesOf(k))
                    if (Near(f, z)) into.Add(f);
            }
        }

        static bool Near(Feature f, float z) => z > f.a.z - 60f && z < f.b.z + 60f;

        /// <summary>True where trees, rocks, pickups or kickers must stay away.</summary>
        public static bool Keep(float x, float z, float margin)
        {
            if (PipeWeight(z) > 0f && Mathf.Abs(x - Slope.Centre(z)) < PipeHalfWidth + PipeDeck + 6f + margin) return true;
            if (CrevasseNear(x, z, margin + 2f)) return true;
            if (Ridge.Keep(x, z, margin)) return true;
            if (Cliffs.Keep(x, z, margin)) return true;
            if (CliffBand.Keep(x, z, margin)) return true;
            if (Bowl.Keep(x, z, margin)) return true;
            return NearFeature(x, z, margin);
        }

        /// <summary>On or beside a park feature, or on the run-in to one.</summary>
        public static bool NearFeature(float x, float z, float margin)
        {
            if (At(z) != Section.Park && At(z + 20f) != Section.Park) return false;
            int c = CycleOf(z);
            for (int k = c - 1; k <= c + 1; k++)
            {
                if (k < 0) continue;
                foreach (var f in FeaturesOf(k))
                {
                    if (!Near(f, z)) continue;
                    if (z < f.a.z - 12f - margin || z > f.b.z + 6f + margin) continue;
                    float t = Mathf.Clamp01((z - f.a.z) / (f.b.z - f.a.z));
                    float fx = Mathf.Lerp(f.a.x, f.b.x, t);
                    if (Mathf.Abs(x - fx) < 2.5f + f.halfWidth + margin) return true;
                }
            }
            return false;
        }
    }
}
