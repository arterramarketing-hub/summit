using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The rock that breaks out of the cliffs: blocky, layered outcrops along a cliff's edge, buttresses standing
    /// out of its face, now and then a gendarme (a free-standing tower of stacked blocks), and talus (broken blocks
    /// fallen off it) heaped toward its foot. On the ridges' cliffs and their canyons' far walls, on the gorges' walls
    /// and rims, on the deadly walls, and at the ends of the jump cliffs' rock bands out in the rough. Built by
    /// CourseDressing with the rest of a cycle.
    ///
    /// Like real cliff rock it comes in bands along the cliff, with bare stretches between: where the rock is sound
    /// it stands proud, outcrop after outcrop; where it is not, the cliff runs on smooth. Every block is a stack of
    /// beds with level tops (the strata the cliffs' own granite shows), each bed a jointed polygon stepped back from
    /// the one under it, so its tops and ledges hold snow (the shared granite, RockFace.Material, whitens whatever
    /// faces up). Each grows out of the slope: its root runs deep into the ground, never balanced on top of it.
    ///
    /// The line stays fair: nothing on the groomed piste, a crest or a gorge's floor, nothing across a jump cliff's
    /// lip where the piste flies off it, and on a ridge nothing where a fall gets up (Ridge.Reach less Ridge.Clear).
    ///
    /// Cheap: two dozen shapes made here once and shared; every block in about 100 m of one cliff side baked into
    /// one mesh with one material (one draw). Each block has its own convex collider (its shape's hull, scaled)
    /// under one Obstacle "Rock" with a slick PhysicsMaterial: the board crashes into them (DoCrash("Rock") plays the
    /// rock hit), and the thrown body tumbles off them, which Ragdoll.LastHitObject sees, so the game screams and thumps.
    /// </summary>
    public static class CliffSpires
    {
        /// <summary>Off for before/after tests and renders: SPIRES_OFF=1 in the environment (never set on a phone).</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("SPIRES_OFF") != "1";
        const float GroupLen = 100f;   // metres of one cliff side baked into one mesh

        enum Kind { Outcrop, Buttress, Gendarme, Talus }
        // how many shapes of each kind, and the first of each in the shared arrays
        static readonly int[] count = { 8, 6, 3, 7 };
        static int First(Kind k) { int f = 0; for (int i = 0; i < (int)k; i++) f += count[i]; return f; }
        static int Total => count[0] + count[1] + count[2] + count[3];

        static Mesh[] hulls;           // each shape with shared corners, for its convex collider
        static Vector3[][] sv, sn;     // each shape drawn flat-shaded: its vertices and normals, kept for baking
        static int[][] st;
        static Material rockMat;
        static PhysicsMaterial slick;

        /// <summary>
        /// What cliff rock feels like to a thrown body: hard and slick with rime. It is hit, and bounced off, but
        /// nothing grips on a block halfway down a face.
        /// </summary>
        public static PhysicsMaterial Slick
        {
            get
            {
                if (slick == null)
                    slick = new PhysicsMaterial("CliffRock")
                    {
                        dynamicFriction = 0.04f, staticFriction = 0.04f, bounciness = 0.2f,
                        frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Average
                    };
                return slick;
            }
        }

        // ---------------------------------------------------------------- building a cycle's rock

        class Spot { public int v; public Vector3 pos; public Quaternion rot; public Vector3 scale; }

        class Groups
        {
            public readonly Dictionary<long, List<Spot>> map = new Dictionary<long, List<Spot>>();
            public void Add(int feature, float side, float z, Spot s)
            {
                if (s == null) return;
                long key = ((long)feature * 4 + (side < 0f ? 0 : 1)) * 100000 + Mathf.FloorToInt(z / GroupLen) + 50000;
                if (!map.TryGetValue(key, out var l)) map[key] = l = new List<Spot>();
                l.Add(s);
            }
        }

        public static void Build(Transform root, int cycle, Shader lit)
        {
            if (!On || cycle < 0) return;
            Init(lit);
            var g = new Groups();
            if (Ridge.Built(cycle)) RidgeRock(g, cycle);
            if (Cliffs.WallOf(cycle, out var w)) WallRock(g, w);
            for (int k = 0; k < 2; k++) if (Cliffs.JumpOf(cycle, k, out var j)) JumpRock(g, j);
            foreach (var kv in g.map) Bake(root, kv.Value);
        }

        static float H(int a, int b, int salt) => Slope.Hash01(a, b, salt);
        static float Pow(float x, float p) => Mathf.Pow(x, p);

        /// <summary>
        /// Is the cliff's rock sound here (0..1): it outcrops in bands along a cliff, tens of metres long, with
        /// stretches of plain face between. Above `from` there is rock; the further above, the more of it.
        /// </summary>
        static float Band(float z, float side, int salt, float from)
        {
            float n = 0.65f * Slope.Noise(z * 0.021f + side * 13.7f + salt * 3.1f, salt * 1.9f + 4.4f)
                    + 0.35f * Slope.Noise(z * 0.07f + side * 5.3f + salt * 7.7f, salt * 2.3f + 9.1f);
            return Mathf.Clamp01((n - from) / (1f - from));
        }

        static int Pick(Kind k, int pick, int salt) => First(k) + Mathf.Min(count[(int)k] - 1, (int)(H(pick, 6, salt) * count[(int)k]));

        /// <summary>How far a block reaches out from its middle across the cliff, however it is yawed (up to 25 degrees off the cliff's line).</summary>
        static float Across(float len, float depth) => 1.12f * (depth + 0.42f * len);

        /// <summary>
        /// One block at (x, z): `len` metres either way along the cliff, `depth` either way out from it, h tall, its
        /// beds level (strata), yawed a little off the cliff's line. It stands on the low side of its footprint and
        /// its root runs on down into the slope, so whichever way the ground falls under it, it grows out of it.
        /// </summary>
        static Spot Block(int v, float x, float z, float len, float depth, float h, int pick)
        {
            if (h < 0.3f) return null;
            float y = Slope.Height(x, z);
            // the ground under its footprint: the low side across the slope, and along it
            float lo = Mathf.Min(Mathf.Min(Slope.Height(x - depth, z), Slope.Height(x + depth, z)), Mathf.Min(Slope.Height(x, z - len), Slope.Height(x, z + len)));
            float py = Mathf.Lerp(y, lo, 0.55f) - 0.08f * h;
            // level beds, a few degrees of dip; yawed so its long side lies along the cliff
            float yaw = 90f + (H(pick, 4, 51) - 0.5f) * 50f + (H(pick, 5, 51) < 0.5f ? 0f : 180f);
            var rot = Quaternion.Euler((H(pick, 3, 51) - 0.5f) * 8f, yaw, (H(pick, 2, 51) - 0.5f) * 8f);
            return new Spot { v = v, pos = new Vector3(x, py, z), rot = rot, scale = new Vector3(len, h, depth) };
        }

        /// <summary>
        /// Would a block a across the run (reaching r out from its middle) stand where a fall gets up? A jump cliff or
        /// a deadly wall can stand where a ridge's canyons (or a gorge) still run on.
        /// </summary>
        static bool OnGetUp(float a, float z, float r)
        {
            if (Ridge.Depth(z) <= 0.05f) return false;
            float side = a < 0f ? -1f : 1f, d = Mathf.Abs(a);
            var k = Ridge.SideAt(z, side);
            if (k == Ridge.Side.Wall || k == Ridge.Side.Bank) return d - r < Ridge.HalfWidth(z) + 4f;
            float reach = Ridge.Reach(z, side);
            return d + r > reach - Ridge.Clear && d - r < reach + 8f;
        }

        // ---------------------------------------------------------------- ridges and gorges

        static void RidgeRock(Groups g, int cycle)
        {
            float zb = Course.First + cycle * Course.Cycle;
            for (int si = 0; si < 2; si++)
            {
                float s = si == 0 ? -1f : 1f;
                var side = Ridge.SideOf(cycle, s);
                if (side == Ridge.Side.Cliff) CliffSide(g, cycle, s, zb);
                else if (side == Ridge.Side.Wall) GorgeSide(g, cycle, s, zb);
                if (side == Ridge.Side.Cliff || side == Ridge.Side.Snow) FarWall(g, cycle, s, zb);
            }
        }

        /// <summary>A ridge's cliff (or a ledge's): outcrops along its edge, buttresses down its face, talus toward its foot.</summary>
        static void CliffSide(Groups g, int cycle, float s, float zb)
        {
            const int feature = 1;
            int si = s < 0f ? 0 : 1;
            float z0 = zb + Ridge.At + Ridge.RampIn * 0.3f, z1 = zb + Ridge.FadeEnd - 40f;
            int i = 0;
            // along the edge: outcrop after outcrop where the band is sound, the odd gendarme where it is soundest
            for (float z = z0; z < z1; i++)
            {
                z += 3.5f + 5f * H(cycle * 5 + si, i, 52);
                if (!Ridge.CliffSide(z, s) || Ridge.DepthHere(z, s) < 20f) continue;
                float band = Band(z, s, cycle * 3 + 1, 0.42f);
                if (band <= 0f) continue;
                int pick = cycle * 7919 + i * 31 + si * 3;
                float hz = Ridge.HalfWidth(z), limit = Ridge.Reach(z, s) - Ridge.Clear;
                bool tower = band > 0.6f && H(pick, 1, 55) < 0.12f;
                int v; float len, depth, h;
                if (tower) { v = Pick(Kind.Gendarme, pick, 55); len = 1.3f + 1f * H(pick, 7, 55); depth = len * (0.8f + 0.3f * H(pick, 8, 55)); h = 5f + 6f * H(pick, 9, 55); }
                else
                {
                    v = Pick(Kind.Outcrop, pick, 55);
                    float big = 0.4f + 0.6f * band;
                    len = (1.5f + 4f * Pow(H(pick, 7, 55), 1.3f)) * big + 0.6f;
                    depth = len * (0.55f + 0.35f * H(pick, 8, 55));
                    h = (1.2f + 3.2f * Pow(H(pick, 9, 55), 1.4f)) * (0.6f + 0.5f * band);
                }
                float r = Across(len, depth);
                // its inner side well clear of the rideable top: on the shoulder, where the edge breaks over
                float d = Mathf.Max(hz + Ridge.Shoulder * 0.7f + 2.2f * H(pick, 10, 55) + depth * 0.4f, hz + 1.5f + r);
                if (d + r > limit) continue;
                g.Add(feature, s, z, Block(v, Slope.Centre(z) + s * d, z, len, depth, h, pick));
                // a smaller block or two broken off beside it
                int n = (int)(H(pick, 11, 55) * 2.6f * band);
                for (int m = 0; m < n; m++)
                {
                    int pk = pick * 13 + m * 7 + 1;
                    float zz = z + (H(pk, 1, 56) - 0.5f) * (len * 2f + 3f);
                    float l2 = len * (0.35f + 0.3f * H(pk, 2, 56)), d2 = l2 * (0.6f + 0.3f * H(pk, 3, 56)), h2 = h * (0.4f + 0.35f * H(pk, 4, 56));
                    float r2 = Across(l2, d2);
                    if (!Ridge.CliffSide(zz, s)) continue;
                    float dd = Mathf.Max(d + (H(pk, 5, 56) - 0.2f) * 3f, Ridge.HalfWidth(zz) + 1.5f + r2);
                    if (dd + r2 > Ridge.Reach(zz, s) - Ridge.Clear) continue;
                    g.Add(feature, s, zz, Block(Pick(Kind.Outcrop, pk, 56), Slope.Centre(zz) + s * dd, zz, l2, d2, h2, pk));
                }
            }
            // down the face: buttresses in ribs, a few stacked one below another; the bands run differently down here
            i = 0;
            for (float z = z0 + 20f; z < z1 - 20f; i++)
            {
                z += 9f + 14f * H(cycle * 5 + si, i, 57);
                if (!Ridge.CliffSide(z, s)) continue;
                float depthHere = Ridge.DepthHere(z, s);
                if (depthHere < 30f) continue;
                float band = Band(z, s, cycle * 3 + 2, 0.38f);
                if (band <= 0f) continue;
                float half = Ridge.HalfWidth(z), reach = Ridge.Reach(z, s), limit = reach - Ridge.Clear;
                float face = reach - half - Ridge.Shoulder;   // how far the face runs out, edge to foot
                int n = 1 + (int)(H(cycle, i * 2 + si, 58) * 3.2f * band);
                float e = Ridge.Shoulder + 3f + face * 0.15f * H(cycle, i * 2 + si, 59);
                for (int m = 0; m < n; m++)
                {
                    int pick = cycle * 6271 + i * 37 + m * 11 + si * 5;
                    float zz = z + (H(pick, 1, 60) - 0.5f) * 6f;
                    float scale = 0.5f + depthHere / 160f;
                    float h = (10f + 22f * Pow(H(pick, 2, 60), 1.3f)) * scale * (0.6f + 0.5f * band);
                    float len = h * (0.3f + 0.25f * H(pick, 3, 60)), depth = len * (0.45f + 0.15f * H(pick, 4, 60));
                    float r = Across(len, depth);
                    float d = half + e + depth;
                    if (d - r < half + Ridge.Shoulder + 1.5f) d = half + Ridge.Shoulder + 1.5f + r;
                    // too big to fit above the foot (where a fall gets up): a smaller one
                    if (d + r > limit) { float k = (limit - d) / r; if (k < 0.35f) break; len *= k; depth *= k; h *= k; r *= k; }
                    // and it tops out below the crest: the cliff's edge stays the skyline
                    float x = Slope.Centre(zz) + s * d;
                    h = Mathf.Min(h, Ridge.Top(zz) - 2f - Slope.Height(x, zz));
                    if (h > 4f) g.Add(feature, s, zz, Block(Pick(Kind.Buttress, pick, 60), x, zz, len, depth, h, pick));
                    // the next one stands further down the face
                    e += h * (0.25f + 0.2f * H(pick, 5, 60)) / 2.6f + depth * 2f;
                }
            }
            // talus: broken blocks heaped on the lower face, short of where a fall gets up
            i = 0;
            for (float z = z0 + 30f; z < z1 - 20f; i++)
            {
                z += 10f + 16f * H(cycle * 5 + si, i, 61);
                if (!Ridge.CliffSide(z, s) || Ridge.DepthHere(z, s) < 30f) continue;
                if (Band(z, s, cycle * 3 + 2, 0.3f) <= 0f) continue;   // beneath the sound rock, where it falls from
                int n = 3 + (int)(H(cycle, i * 2 + si, 62) * 7f);
                for (int m = 0; m < n; m++)
                {
                    int pick = cycle * 3301 + i * 43 + m * 17 + si * 7;
                    float zz = z + (H(pick, 1, 63) - 0.5f) * 14f;
                    if (!Ridge.CliffSide(zz, s)) continue;
                    float sz = 0.5f + 2.4f * Pow(H(pick, 2, 63), 2f);
                    float r = Across(sz, sz * 0.8f);
                    float limit = Ridge.Reach(zz, s) - Ridge.Clear;
                    float d = limit - r - 9f * Pow(H(pick, 3, 63), 0.8f);
                    if (d - r < Ridge.HalfWidth(zz) + Ridge.Shoulder + 4f) continue;
                    g.Add(feature, s, zz, Block(Pick(Kind.Talus, pick, 63), Slope.Centre(zz) + s * d, zz, sz, sz * (0.7f + 0.3f * H(pick, 4, 63)), sz * (0.6f + 0.4f * H(pick, 5, 63)), pick));
                }
            }
        }

        /// <summary>
        /// A gorge's wall: blocks breaking over its rim (out of reach, up on the ground beside the gorge), buttresses
        /// in its face, and talus on the outer edge of the snow apron at its foot (never on the floor you ride).
        /// </summary>
        static void GorgeSide(Groups g, int cycle, float s, float zb)
        {
            const int feature = 6;
            int si = s < 0f ? 0 : 1;
            float z0 = zb + Ridge.At + 60f, z1 = zb + Ridge.FadeEnd - 40f;
            int i = 0;
            for (float z = z0; z < z1; i++)
            {
                z += 4f + 6f * H(cycle * 5 + si, i, 64);
                if (!Ridge.WallSide(z, s)) continue;
                float wall = Ridge.DepthHere(z, s);
                if (wall < 12f) continue;
                float half = Ridge.HalfWidth(z), reach = Ridge.Reach(z, s);
                float foot = half + Ridge.Shoulder + 8.5f;   // where the apron ends and the wall's rock begins
                // the rim, where the band is sound
                float band = Band(z, s, cycle * 3 + 4, 0.4f);
                if (band > 0f)
                {
                    int pick = cycle * 5381 + i * 29 + si;
                    float len = (2f + 4.5f * Pow(H(pick, 7, 65), 1.3f)) * (0.5f + 0.5f * band) + 0.5f;
                    float depth = len * (0.5f + 0.35f * H(pick, 8, 65));
                    float h = (1.5f + 4f * Pow(H(pick, 9, 65), 1.3f)) * (0.6f + 0.5f * band);
                    float d = reach - 4f + 6f * H(pick, 10, 65);
                    g.Add(feature, s, z, Block(Pick(Kind.Outcrop, pick, 65), Slope.Centre(z) + s * d, z, len, depth, h, pick));
                }
                // buttresses in the face, every so often
                float bb = Band(z, s, cycle * 3 + 5, 0.45f);
                if (bb > 0f && H(cycle, i * 2 + si, 66) < 0.45f)
                {
                    int pick = cycle * 4111 + i * 23 + si * 3;
                    float h = (0.35f + 0.4f * H(pick, 2, 67)) * wall * (0.7f + 0.4f * bb);
                    float len = h * (0.3f + 0.25f * H(pick, 3, 67)), depth = len * (0.45f + 0.15f * H(pick, 4, 67));
                    float r = Across(len, depth);
                    float d = Mathf.Max(foot + 2f + r * 0.5f, Mathf.Lerp(foot, reach, 0.25f + 0.45f * H(pick, 5, 67)));
                    float x = Slope.Centre(z) + s * d;
                    // topping out below the rim
                    h = Mathf.Min(h, Slope.Height(Slope.Centre(z) + s * (reach + 3f), z) - 1.5f - Slope.Height(x, z));
                    if (h > 4f) g.Add(feature, s, z, Block(Pick(Kind.Buttress, pick, 67), x, z, len, depth, h, pick));
                }
                // talus on the apron's outer edge, below the sound rock
                if (Band(z, s, cycle * 3 + 5, 0.3f) > 0f && H(cycle, i * 2 + si, 68) < 0.5f)
                {
                    int n = 2 + (int)(H(cycle, i * 2 + si, 69) * 4f);
                    for (int m = 0; m < n; m++)
                    {
                        int pick = cycle * 2203 + i * 41 + m * 13 + si * 5;
                        float zz = z + (H(pick, 1, 70) - 0.5f) * 8f;
                        if (!Ridge.WallSide(zz, s)) continue;
                        float sz = 0.4f + 1.7f * Pow(H(pick, 2, 70), 2f);
                        float r = Across(sz, sz * 0.8f);
                        float d = Mathf.Max(Ridge.HalfWidth(zz) + 5f + r, foot - 2f + 4f * H(pick, 3, 70));
                        g.Add(feature, s, zz, Block(Pick(Kind.Talus, pick, 70), Slope.Centre(zz) + s * d, zz, sz, sz * (0.7f + 0.3f * H(pick, 4, 70)), sz * (0.6f + 0.4f * H(pick, 5, 70)), pick));
                    }
                }
            }
        }

        /// <summary>A canyon's far wall, toward its top: bands of big outcrops and buttresses along the skyline you see from the crest.</summary>
        static void FarWall(Groups g, int cycle, float s, float zb)
        {
            const int feature = 2;
            int si = s < 0f ? 0 : 1;
            int i = 0;
            for (float z = zb + Ridge.At + Ridge.RampIn; z < zb + Ridge.FadeEnd - 80f; i++)
            {
                z += 7f + 9f * H(cycle * 5 + si, i, 71);
                if (Ridge.DepthHere(z, s) < 40f) continue;
                var k = Ridge.SideAt(z, s);
                if (k != Ridge.Side.Cliff && k != Ridge.Side.Snow) continue;
                float band = Band(z, s, cycle * 3 + 6, 0.4f);
                if (band <= 0f) continue;
                int pick = cycle * 4447 + i * 41 + si * 7;
                float big = Ridge.DepthHere(z, s) / Ridge.CliffDepth;
                bool buttress = H(pick, 1, 72) < 0.4f;
                float d = Ridge.FarIn + (Ridge.FarOut - Ridge.FarIn) * (buttress ? 0.3f + 0.35f * H(pick, 2, 72) : 0.6f + 0.3f * H(pick, 2, 72));
                float h = (buttress ? 14f + 24f * H(pick, 3, 72) : 5f + 10f * H(pick, 3, 72)) * (0.6f + 0.5f * big) * (0.6f + 0.5f * band);
                float len = h * (buttress ? 0.35f : 1.1f) * (0.8f + 0.4f * H(pick, 4, 72)), depth = len * (buttress ? 0.5f : 0.6f);
                float x = Slope.Centre(z) + s * d;
                if (buttress) h = Mathf.Min(h, Slope.Height(Slope.Centre(z) + s * (Ridge.FarOut + 4f), z) - 2f - Slope.Height(x, z));
                if (h > 3f) g.Add(feature, s, z, Block(Pick(buttress ? Kind.Buttress : Kind.Outcrop, pick, 72), x, z, len, depth, h, pick));
            }
        }

        // ---------------------------------------------------------------- deadly walls and jump cliffs

        static void WallRock(Groups g, Cliffs.Wall w)
        {
            const int feature = 3;
            int i = 0;
            // along the lip
            for (float z = w.z0 + 6f; z < w.z2 - 10f; i++)
            {
                z += 3.5f + 5f * H(w.cycle, i, 73);
                float hgt = Cliffs.WallHeight(w, z);
                if (hgt < 5f) continue;
                float band = Band(z, w.side, w.cycle * 3 + 7, 0.4f);
                if (band <= 0f) continue;
                int pick = w.cycle * 3571 + i * 29;
                bool tower = band > 0.6f && H(pick, 1, 74) < 0.1f;
                int v; float len, depth, h;
                if (tower) { v = Pick(Kind.Gendarme, pick, 74); len = 1.2f + 0.9f * H(pick, 7, 74); depth = len; h = 4f + 5f * H(pick, 9, 74); }
                else
                {
                    v = Pick(Kind.Outcrop, pick, 74);
                    len = (1.5f + 3.5f * Pow(H(pick, 7, 74), 1.3f)) * (0.5f + 0.5f * band) + 0.5f;
                    depth = len * (0.55f + 0.35f * H(pick, 8, 74));
                    h = (1.2f + 3f * Pow(H(pick, 9, 74), 1.4f)) * (0.6f + 0.5f * band);
                }
                float r = Across(len, depth);
                // on the lip and just over it, never back toward the poles and the run
                float a = w.edge + 0.4f + r + 2f * H(pick, 10, 74);
                if (OnGetUp(w.side * a, z, r)) continue;
                g.Add(feature, w.side, z, Block(v, Slope.Centre(z) + w.side * a, z, len, depth, h, pick));
            }
            // buttresses in the face, and talus on the terrace under it
            i = 0;
            for (float z = w.z0 + 14f; z < w.z2 - 20f; i++)
            {
                z += 7f + 10f * H(w.cycle, i, 75);
                float hgt = Cliffs.WallHeight(w, z);
                if (hgt < 12f) continue;
                float band = Band(z, w.side, w.cycle * 3 + 8, 0.35f);
                if (band <= 0f) continue;
                int pick = w.cycle * 2953 + i * 23;
                float h = hgt * (0.35f + 0.35f * H(pick, 2, 76)) * (0.7f + 0.4f * band);
                float len = h * (0.3f + 0.25f * H(pick, 3, 76)), depth = len * 0.5f;
                float r = Across(len, depth);
                float a = w.edge + 2f + r * 0.6f + hgt * 0.55f * 0.5f * H(pick, 4, 76);
                float bx = Slope.Centre(z) + w.side * a;
                h = Mathf.Min(h, Slope.Height(Slope.Centre(z) + w.side * w.edge, z) - 1.5f - Slope.Height(bx, z));
                if (h > 4f && !OnGetUp(w.side * a, z, r))
                    g.Add(feature, w.side, z, Block(Pick(Kind.Buttress, pick, 76), bx, z, len, depth, h, pick));
                float foot = w.edge + hgt * 0.55f;
                int n = (int)(H(pick, 5, 76) * 5f);
                for (int m = 0; m < n; m++)
                {
                    int pk = pick * 7 + m * 3 + 1;
                    float zz = z + (H(pk, 1, 77) - 0.5f) * 10f;
                    float sz = 0.5f + 2f * Pow(H(pk, 2, 77), 2f);
                    float aa = foot + 1f + 9f * H(pk, 3, 77);
                    if (OnGetUp(w.side * aa, zz, Across(sz, sz))) continue;
                    g.Add(feature, w.side, zz, Block(Pick(Kind.Talus, pk, 77), Slope.Centre(zz) + w.side * aa, zz, sz, sz * 0.85f, sz * 0.7f, pk));
                }
            }
        }

        static void JumpRock(Groups g, Cliffs.Jump j)
        {
            int feature = 4 + j.k;
            for (int si = 0; si < 2; si++)
            {
                float s = si == 0 ? -1f : 1f;
                int i = 0;
                // out in the rough either side, where the rock band runs on beyond the piste and fades away
                for (float a = Slope.PisteHalf + 6f; a < Slope.PisteHalf + 6f + Cliffs.JumpBand * 0.8f; i++)
                {
                    a += 3f + 4f * H(j.cycle * 3 + j.k, i * 2 + si, 78);
                    int pick = j.cycle * 2179 + j.k * 997 + i * 19 + si;
                    if (H(pick, 12, 79) < 0.3f) continue;   // gaps in the band
                    float fade = 1f - (a - Slope.PisteHalf - 6f) / Cliffs.JumpBand;
                    float len = (1.2f + 2.5f * H(pick, 7, 79)) * (0.6f + 0.5f * fade);
                    float depth = len * (0.6f + 0.3f * H(pick, 8, 79));
                    float h = (0.8f + j.drop * 0.55f * H(pick, 2, 79)) * (0.5f + 0.6f * fade);
                    float r = Across(len, depth);
                    float aa = Mathf.Max(a, Slope.PisteHalf + 5f + r);
                    // in the band itself, between its lip and its foot
                    float z = Cliffs.LipZ(j, s * aa) + (0.15f + 0.7f * H(pick, 3, 79)) * j.face;
                    if (OnGetUp(s * aa, z, r)) continue;
                    g.Add(feature, s, z, Block(Pick(Kind.Outcrop, pick, 79), Slope.Centre(j.z) + s * aa, z, len, depth, h, pick));
                }
            }
        }

        // ---------------------------------------------------------------- baking a group into one mesh

        static readonly List<Vector3> bv = new List<Vector3>(), bn = new List<Vector3>();
        static readonly List<int> bt = new List<int>();

        static void Bake(Transform root, List<Spot> spots)
        {
            if (spots.Count == 0) return;
            bv.Clear(); bn.Clear(); bt.Clear();
            var go = new GameObject("CliffSpires");
            go.transform.SetParent(root, false);
            go.AddComponent<Obstacle>().label = "Rock";
            foreach (var s in spots)
            {
                var m = Matrix4x4.TRS(s.pos, s.rot, s.scale);
                var mn = m.inverse.transpose;
                var v = sv[s.v]; var n = sn[s.v];
                int o = bv.Count;
                for (int k = 0; k < v.Length; k++) { bv.Add(m.MultiplyPoint3x4(v[k])); bn.Add(mn.MultiplyVector(n[k]).normalized); }
                foreach (int t in st[s.v]) bt.Add(o + t);
                // its own collider: the shape's hull, placed and scaled as drawn
                var c = new GameObject("SpireHit");
                c.transform.SetParent(go.transform, false);
                c.transform.SetPositionAndRotation(s.pos, s.rot);
                c.transform.localScale = s.scale;
                var mc = c.AddComponent<MeshCollider>();
                mc.convex = true;
                mc.sharedMesh = hulls[s.v];
                mc.sharedMaterial = Slick;
            }
            var mesh = new Mesh { name = "CliffSpires" };
            if (bv.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(bv); mesh.SetNormals(bn);
            mesh.SetTriangles(bt, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rockMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            go.AddComponent<OwnMesh>().mesh = mesh;
        }

        /// <summary>The group's baked mesh goes with it when the cycle is thrown away.</summary>
        class OwnMesh : MonoBehaviour
        {
            public Mesh mesh;
            void OnDestroy() { if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }
        }

        // ---------------------------------------------------------------- the shapes

        static void Init(Shader lit)
        {
            // the mountain's own granite (world-mapped, so the blocks' strata and snow run on from the cliff's)
            if (rockMat == null) rockMat = RockFace.Material(lit);
            if (hulls != null) return;
            int n = Total;
            hulls = new Mesh[n]; sv = new Vector3[n][]; sn = new Vector3[n][]; st = new int[n][];
            for (int v = 0; v < n; v++) MakeShape(v);
        }

        // a shape under construction: corners shared between faces (for the hull), triangles wound outward
        static readonly List<Vector3> cv = new List<Vector3>();
        static readonly List<int> ct = new List<int>();

        static void MakeShape(int v)
        {
            cv.Clear(); ct.Clear();
            var rnd = new System.Random(9157 + v * 104729);
            float R() => (float)rnd.NextDouble();
            Kind kind = v < First(Kind.Buttress) ? Kind.Outcrop : v < First(Kind.Gendarme) ? Kind.Buttress : v < First(Kind.Talus) ? Kind.Gendarme : Kind.Talus;
            // the block's plan: a jointed block, near enough square-cut, its corners knocked off here and there;
            // the same joints run up through every bed
            var plan = Plan(rnd);
            int corners = plan.Length;
            switch (kind)
            {
                case Kind.Outcrop:
                    // 3-5 beds, stepping back as they climb (a ledge at each), the top one broken off unevenly
                    Beds(rnd, plan, -0.8f, 1f, 3 + (int)(R() * 3f), 0.78f, 0.95f, new Vector2(R() - 0.5f, R() - 0.5f) * 0.12f, 0.08f);
                    break;
                case Kind.Buttress:
                    // a tall stack of beds, barely stepping back, its root deep in the face
                    Beds(rnd, plan, -1.4f, 1f, 5 + (int)(R() * 3f), 0.86f, 1.0f, new Vector2(R() - 0.5f, -0.3f - 0.3f * R()) * 0.08f, 0.06f);
                    break;
                case Kind.Gendarme:
                {
                    // a tower of a few big blocks set one on another, a little off true, with a smaller one atop
                    Beds(rnd, plan, -0.6f, 0.82f, 3 + (int)(R() * 2f), 0.84f, 1.02f, new Vector2(R() - 0.5f, R() - 0.5f) * 0.2f, 0.05f);
                    var cap = new Vector2[corners];
                    Vector2 off = new Vector2(R() - 0.5f, R() - 0.5f) * 0.3f;
                    for (int i = 0; i < corners; i++) cap[i] = plan[i] * (0.45f + 0.15f * R()) + off;
                    Bed(rnd, cap, 0.78f, 1f, 0.12f, false, true);
                    break;
                }
                default:
                    // a broken block: one bed, its top and bottom cut at a slant, corners knocked about
                    Bed(rnd, plan, -0.35f, 0.75f + 0.25f * R(), 0.3f, true, false);
                    break;
            }

            var hull = new Mesh { name = "CragHull" + v };
            hull.SetVertices(cv); hull.SetTriangles(ct, 0);
            hull.RecalculateBounds();
            hulls[v] = hull;
            Physics.BakeMesh(hull.GetInstanceID(), true);

            // drawn flat-shaded, each face its own corners
            var vv = new List<Vector3>(); var nn = new List<Vector3>(); var tt = new List<int>();
            for (int t = 0; t < ct.Count; t += 3)
            {
                Vector3 a = cv[ct[t]], b = cv[ct[t + 1]], c = cv[ct[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int o = vv.Count;
                vv.Add(a); vv.Add(b); vv.Add(c); nn.Add(n); nn.Add(n); nn.Add(n);
                tt.Add(o); tt.Add(o + 1); tt.Add(o + 2);
            }
            sv[v] = vv.ToArray(); sn[v] = nn.ToArray(); st[v] = tt.ToArray();
        }

        /// <summary>
        /// A block's plan, about 2 across: a rectangle (its sides 0.7 to 1 of each other), each corner either square or
        /// split off by a joint, every corner nudged a little off true. Never round: rock breaks along its joints.
        /// </summary>
        static Vector2[] Plan(System.Random rnd)
        {
            float R() => (float)rnd.NextDouble();
            float bx = 1f, bz = 0.7f + 0.3f * R();
            var pts = new List<Vector2>();
            var sq = new[] { new Vector2(bx, bz), new Vector2(-bx, bz), new Vector2(-bx, -bz), new Vector2(bx, -bz) };
            for (int i = 0; i < 4; i++)
            {
                Vector2 c = sq[i], prev = sq[(i + 3) % 4], next = sq[(i + 1) % 4];
                if (R() < 0.55f)
                {
                    // a joint cuts the corner off: two corners where there was one
                    float a = 0.15f + 0.35f * R(), b = 0.15f + 0.35f * R();
                    pts.Add(Vector2.Lerp(c, prev, a * 0.5f));
                    pts.Add(Vector2.Lerp(c, next, b * 0.5f));
                }
                else pts.Add(c);
            }
            for (int i = 0; i < pts.Count; i++) pts[i] += new Vector2(R() - 0.5f, R() - 0.5f) * 0.12f;
            return pts.ToArray();
        }

        /// <summary>
        /// A stack of beds from y0 (underground) to y1 on one plan: the lowest is the root, rising out of the ground;
        /// each one above a little smaller than the one under it (between shrinkLo and shrinkHi of it, and nudged by
        /// `drift`), so each leaves a ledge round the next; their tops level to within `rough`.
        /// </summary>
        static void Beds(System.Random rnd, Vector2[] plan, float y0, float y1, int beds, float shrinkLo, float shrinkHi, Vector2 drift, float rough)
        {
            float R() => (float)rnd.NextDouble();
            // uneven bed thicknesses, from the ground (0) to the top
            var th = new float[beds];
            float sum = 0f;
            for (int k = 0; k < beds; k++) { th[k] = 0.6f + R(); sum += th[k]; }
            float acc = 0f, scale = 1f, lo = y0;
            Vector2 at = Vector2.zero;
            var p = new Vector2[plan.Length];
            for (int k = 0; k < beds; k++)
            {
                acc += th[k];
                float hi = y1 * acc / sum;
                // the joints hold, but each bed's corners weather back their own way, and now and then a whole side
                // of a bed has broken away along a joint (an uneven step, not a tidy ring of ledge)
                Vector2 cutDir = Vector2.zero; float cut = 9f;
                if (k > 0 && R() < 0.45f) { float ang = R() * 6.283f; cutDir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)); cut = 0.35f + 0.4f * R(); }
                for (int i = 0; i < plan.Length; i++)
                {
                    Vector2 q = plan[i] * scale * (0.95f + 0.07f * R());
                    float over = Vector2.Dot(q, cutDir) - cut * scale;
                    if (over > 0f) q -= cutDir * over;
                    p[i] = q + at;
                }
                Bed(rnd, p, lo, hi, rough * (y1 - Mathf.Max(0f, lo)), false, k == beds - 1);
                lo = hi - 0.03f;   // the next bed sits on this one (a hair into it, so no light leaks between)
                scale *= Mathf.Lerp(shrinkLo, shrinkHi, R());
                at += drift + new Vector2(R() - 0.5f, R() - 0.5f) * 0.06f;
            }
        }

        /// <summary>
        /// One bed: a prism on plan p from y0 to y1, its sides split once across the middle (each face breaks into
        /// two facets), its top near level (within `rough`); snapped at a slant when `slant` (a loose block, closed
        /// underneath), or a little out of level when `broken` (the top of a stack).
        /// </summary>
        static void Bed(System.Random rnd, Vector2[] p, float y0, float y1, float rough, bool slant, bool broken)
        {
            float R() => (float)rnd.NextDouble();
            int n = p.Length, first = cv.Count;
            Vector2 c = Vector2.zero;
            foreach (var q in p) c += q;
            c /= n;
            Vector2 tilt = slant || broken ? new Vector2(R() - 0.5f, R() - 0.5f) * (slant ? 0.5f : 0.2f) : Vector2.zero;
            // a thick bed's faces break into two facets (a middle row bulging or sinking a little); a thin one's are one
            int rows = y1 - Mathf.Max(0f, y0) > 0.3f || slant ? 2 : 1;
            float mid = Mathf.Lerp(Mathf.Max(y0, y1 - 0.8f), y1, 0.4f + 0.2f * R());
            for (int i = 0; i < n; i++) cv.Add(new Vector3(p[i].x, y0 - (slant ? R() * 0.1f : 0f), p[i].y));
            if (rows == 2)
                for (int i = 0; i < n; i++)
                {
                    Vector2 q = c + (p[i] - c) * (0.94f + 0.12f * R());
                    cv.Add(new Vector3(q.x, mid + (R() - 0.5f) * 0.04f, q.y));
                }
            for (int i = 0; i < n; i++)
            {
                Vector2 q = c + (p[i] - c) * (0.9f + 0.08f * R());
                cv.Add(new Vector3(q.x, y1 + (R() - 0.5f) * rough + Vector2.Dot(q - c, tilt), q.y));
            }
            int top = cv.Count;
            cv.Add(new Vector3(c.x, y1 + (R() - 0.3f) * rough * 0.5f, c.y));
            Vector3 inside = new Vector3(c.x, (y0 + y1) * 0.5f, c.y);
            for (int row = 0; row < rows; row++)
                for (int i = 0; i < n; i++)
                {
                    int i1 = (i + 1) % n;
                    int a = first + row * n + i, b = first + row * n + i1, cc = first + (row + 1) * n + i, d = first + (row + 1) * n + i1;
                    Tri(a, cc, b, inside); Tri(b, cc, d, inside);
                }
            int last = first + rows * n;
            Vector3 below = new Vector3(c.x, Mathf.Min(y0, y1 - 1f), c.y);
            for (int i = 0; i < n; i++) Tri(last + i, top, last + (i + 1) % n, below);
            if (slant)
            {
                // a loose block: closed underneath too (it lies on the snow, its bottom can show)
                int bot = cv.Count;
                cv.Add(new Vector3(c.x, y0 - 0.05f, c.y));
                Vector3 above = new Vector3(c.x, y1 + 1f, c.y);
                for (int i = 0; i < n; i++) Tri(first + i, bot, first + (i + 1) % n, above);
            }
        }

        /// <summary>A triangle, wound so it faces away from `inside`.</summary>
        static void Tri(int a, int b, int c, Vector3 inside)
        {
            Vector3 pa = cv[a], pb = cv[b], pc = cv[c];
            Vector3 n = Vector3.Cross(pb - pa, pc - pa);
            if (Vector3.Dot(n, (pa + pb + pc) / 3f - inside) < 0f) { int t = b; b = c; c = t; }
            ct.Add(a); ct.Add(b); ct.Add(c);
        }
    }
}
