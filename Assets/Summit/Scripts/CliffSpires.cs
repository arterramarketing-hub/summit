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
        const float GroupLen = 60f;   // metres of one cliff side baked into one mesh

        enum Kind { Outcrop, Buttress, Gendarme, Talus }
        // how many shapes of each kind, and the first of each in the shared arrays
        static readonly int[] count = { 8, 6, 3, 7 };
        static int First(Kind k) { int f = 0; for (int i = 0; i < (int)k; i++) f += count[i]; return f; }
        static int Total => count[0] + count[1] + count[2] + count[3];

        static Mesh[] hulls;           // each shape with shared corners, for its convex collider
        // each shape's collider as it is drawn up close: the near look's own surface (null for a talus block, whose
        // plain look is its hull). A convex hull round the near look stood up to a few metres clear of it at the
        // snow (its ledge lips out over feet tucked in, its joints cut in): a hit on nothing beside a crag
        static Mesh[] rockHits;
        static Vector3[][] sv, sn;     // each shape drawn flat-shaded: its vertices and normals, kept for baking
        static int[][] st;
        static Material rockMat, cragMat, scanMat, driftMat;
        // each shape up close: broken edges, chipped corners, joints and ledges (flat-shaded shapes above are the far look)
        static Vector3[][] dv, dn;
        static int[][] dt;
        // the photo-scanned boulders (Resources/ScanRocks) for talus: their parts, and each one's box in its own space
        class ScanPart { public Mesh mesh; public int sub; public Matrix4x4 local; }
        static List<ScanPart>[] scans;
        static Bounds[] scanBox;
        const float NearLod = 0.8f;   // the near look while a group stands this tall on screen (its own size over the view's height)
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

        class Spot { public int v; public Vector3 pos; public Quaternion rot; public Vector3 scale; public bool scan, drift; }

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
            foreach (var _ in Steps(root, cycle, lit)) { }
        }

        /// <summary>Build, a piece at a time (each cliff's rock laid out, each group of it baked), for a cycle built over several frames.</summary>
        public static IEnumerable<bool> Steps(Transform root, int cycle, Shader lit)
        {
            if (!On || cycle < 0) yield break;
            Init(lit);
            // drifts wear the ground's own snow (same grooves and sparkle, same mapping), so they run on into it
            if (driftMat == null) { var t = Object.FindFirstObjectByType<SlopeStreamer>(); if (t != null) driftMat = t.snowMaterial; }
            var g = new Groups();
            if (Ridge.Built(cycle)) { RidgeRock(g, cycle); yield return true; }
            if (Cliffs.WallOf(cycle, out var w)) { WallRock(g, w); yield return true; }
            for (int k = 0; k < 2; k++) if (Cliffs.JumpOf(cycle, k, out var j)) { JumpRock(g, j); yield return true; }
            foreach (var kv in g.map) { Bake(root, kv.Value); yield return true; }
        }

        /// <summary>Queue this frame's instanced talus now (the test renders call it before a camera renders by hand).</summary>
        public static void DrawNow() { foreach (var t in TalusDraw.live) t.Draw(); }

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
            if (v >= First(Kind.Talus) && scans != null && scans.Length > 0) return Boulder(x, z, len, depth, h, pick);
            float y = Slope.Height(x, z);
            // the ground under its footprint: the low side across the slope, and along it
            float lo = Mathf.Min(Mathf.Min(Slope.Height(x - depth, z), Slope.Height(x + depth, z)), Mathf.Min(Slope.Height(x, z - len), Slope.Height(x, z + len)));
            float py = Mathf.Lerp(y, lo, 0.55f) - 0.08f * h;
            // level beds, a few degrees of dip; yawed so its long side lies along the cliff
            float yaw = 90f + (H(pick, 4, 51) - 0.5f) * 50f + (H(pick, 5, 51) < 0.5f ? 0f : 180f);
            var rot = Quaternion.Euler((H(pick, 3, 51) - 0.5f) * 8f, yaw, (H(pick, 2, 51) - 0.5f) * 8f);
            // on ground that holds snow, the snow drifts up against it (Bake)
            bool drift = Slope.Normal(x, z).y > 0.6f && h < 25f;
            return new Spot { v = v, pos = new Vector3(x, py, z), rot = rot, scale = new Vector3(len, h, depth), drift = drift };
        }

        /// <summary>
        /// A fallen block for the talus: one of the photo-scanned boulders, about len by h by depth (scaled evenly, so
        /// it keeps its scanned shape), a third of it sunk in the snow and settled to the slope.
        /// </summary>
        static Spot Boulder(float x, float z, float len, float depth, float h, int pick)
        {
            int v = Mathf.Min(scans.Length - 1, (int)(H(pick, 9, 53) * scans.Length));
            var b = scanBox[v];
            float k = ((2f * len) / Mathf.Max(0.05f, b.size.x) + h / Mathf.Max(0.05f, b.size.y) + (2f * depth) / Mathf.Max(0.05f, b.size.z)) / 3f;
            Vector3 n = Slope.Normal(x, z);
            var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, n, 0.6f)) * Quaternion.Euler(0f, H(pick, 8, 53) * 360f, 0f);
            float y = Slope.DrawnHeight(x, z) - b.min.y * k - 0.32f * b.size.y * k;
            return new Spot { v = v, scan = true, pos = new Vector3(x, y, z), rot = rot, scale = Vector3.one * k };
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
        static Mesh[] talusHulls;

        /// <summary>
        /// A scanned boulder's hit shape: its box (0.8 of the model's) with the corners cut off and the top drawn in,
        /// an eight-sided frustum. The plain box's corners stood up to a metre clear of a big rounded boulder, at
        /// the height a board passes: a hit on nothing.
        /// </summary>
        static Mesh TalusHull(int v)
        {
            if (talusHulls == null || talusHulls.Length != scanBox.Length) talusHulls = new Mesh[scanBox.Length];
            if (talusHulls[v] != null) return talusHulls[v];
            talusHulls[v] = Frustum(scanBox[v], 0.75f, "TalusHull" + v);
            return talusHulls[v];
        }

        /// <summary>
        /// A rounded rock's hit shape inside its box `b` (shrunk by `k`): an eight-sided frustum, corners cut and the
        /// top drawn in, as a convex mesh (PhysX hulls the points).
        /// </summary>
        public static Mesh Frustum(Bounds b, float shrink, string name)
        {
            Vector3 c = b.center, e = b.extents * shrink;
            var pts = new List<Vector3>();
            const float cut = 0.38f;   // how much of each half-side the corners lose
            foreach (var (y, k) in new[] { (-e.y, 1f), (e.y, 0.55f) })
            {
                float x = e.x * k, z = e.z * k;
                pts.Add(c + new Vector3(x, y, z * (1f - cut))); pts.Add(c + new Vector3(x * (1f - cut), y, z));
                pts.Add(c + new Vector3(-x * (1f - cut), y, z)); pts.Add(c + new Vector3(-x, y, z * (1f - cut)));
                pts.Add(c + new Vector3(-x, y, -z * (1f - cut))); pts.Add(c + new Vector3(-x * (1f - cut), y, -z));
                pts.Add(c + new Vector3(x * (1f - cut), y, -z)); pts.Add(c + new Vector3(x, y, -z * (1f - cut)));
            }
            // (PhysX builds the convex hull from the points: the triangles only have to hold them)
            var tri = new List<int>();
            for (int i = 1; i < 7; i++) { tri.Add(0); tri.Add(i + 1); tri.Add(i); tri.Add(8); tri.Add(8 + i); tri.Add(8 + i + 1); }
            for (int i = 0; i < 8; i++) { int j = (i + 1) % 8; tri.Add(i); tri.Add(j); tri.Add(8 + j); tri.Add(i); tri.Add(8 + j); tri.Add(8 + i); }
            var m = new Mesh { name = name };
            m.SetVertices(pts); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        static readonly List<int> bt = new List<int>();

        static void Bake(Transform root, List<Spot> spots)
        {
            if (spots.Count == 0) return;
            var go = new GameObject("CliffSpires");
            go.transform.SetParent(root, false);
            go.AddComponent<Obstacle>().label = "Rock";
            var own = go.AddComponent<OwnMesh>();
            var nearV = new List<Vector3>(); var nearN = new List<Vector3>(); var nearT = new List<int>(); var driftT = new List<int>();
            var farV = new List<Vector3>(); var farN = new List<Vector3>(); var farT = new List<int>();
            List<Matrix4x4>[] talus = null;
            var tb = new Bounds();
            bool anyTalus = false;
            foreach (var s in spots)
            {
                var m = Matrix4x4.TRS(s.pos, s.rot, s.scale);
                var c = new GameObject("SpireHit");
                c.transform.SetParent(go.transform, false);
                c.transform.SetPositionAndRotation(s.pos, s.rot);
                c.transform.localScale = s.scale;
                if (s.scan)
                {
                    // a scanned boulder: drawn instanced (TalusDraw), hit as a box a little inside it
                    if (talus == null) { talus = new List<Matrix4x4>[scans.Length]; for (int i = 0; i < talus.Length; i++) talus[i] = new List<Matrix4x4>(); }
                    talus[s.v].Add(m);
                    var wb = new Bounds(m.MultiplyPoint3x4(scanBox[s.v].center), Vector3.zero);
                    for (int i = 0; i < 8; i++)
                        wb.Encapsulate(m.MultiplyPoint3x4(scanBox[s.v].center + Vector3.Scale(scanBox[s.v].extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    if (anyTalus) tb.Encapsulate(wb); else { tb = wb; anyTalus = true; }
                    var hull = c.AddComponent<MeshCollider>();
                    hull.convex = true;
                    hull.sharedMesh = TalusHull(s.v);
                    hull.sharedMaterial = Slick;
                    continue;
                }
                Append(farV, farN, farT, sv[s.v], sn[s.v], st[s.v], m);
                Append(nearV, nearN, nearT, dv[s.v], dn[s.v], dt[s.v], m);
                if (s.drift) Drift(nearV, nearN, driftT, s);
                // its own collider: placed and scaled as drawn, the broken rock it is up close (a talus block: its hull)
                var mc = c.AddComponent<MeshCollider>();
                mc.convex = rockHits[s.v] == null;
                mc.sharedMesh = rockHits[s.v] != null ? rockHits[s.v] : hulls[s.v];
                mc.sharedMaterial = Slick;
            }
            if (farT.Count > 0)
            {
                // two looks: near, the broken, jointed rock with its drifts; further off, the plain blocks
                var near = Part(go.transform, "Near", nearV, nearN, nearT, driftT, own);
                var far = Part(go.transform, "Far", farV, farN, farT, null, own);
                var lods = go.AddComponent<LODGroup>();
                lods.SetLODs(new[] { new LOD(NearLod, new Renderer[] { near }), new LOD(0f, new Renderer[] { far }) });
                lods.RecalculateBounds();
            }
            if (talus != null) { var td = go.AddComponent<TalusDraw>(); td.lists = talus; td.bounds = tb; }
        }

        static void Append(List<Vector3> V, List<Vector3> N, List<int> T, Vector3[] v, Vector3[] n, int[] t, Matrix4x4 m)
        {
            var mn = m.inverse.transpose;
            int o = V.Count;
            for (int k = 0; k < v.Length; k++) { V.Add(m.MultiplyPoint3x4(v[k])); N.Add(mn.MultiplyVector(n[k]).normalized); }
            foreach (int i in t) T.Add(o + i);
        }

        static readonly List<Vector2> uvs = new List<Vector2>();
        static readonly Dictionary<int, int> copyOf = new Dictionary<int, int>();   // (Part's: a corner's copy mapped across a steep face)

        static MeshRenderer Part(Transform parent, string name, List<Vector3> V, List<Vector3> N, List<int> T, List<int> drift, OwnMesh own)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "Crags" + name };
            bool drifts = drift != null && drift.Count > 0;
            if (drifts)
            {
                // the ground's mapping (SlopeStreamer: x/9, z/9), so a drift's grain runs on from the snow round it
                uvs.Clear();
                foreach (var p in V) uvs.Add(new Vector2(p.x / 9f, p.z / 9f));
                // except where a drift lies against a steep face (a block on a gorge's wall): there, as on the terrain,
                // mapped across the face, from corners shared with the drift's other steep triangles mapped the same way;
                // from above it smeared into long white streaks down the wall
                copyOf.Clear();
                for (int t = 0; t < drift.Count; t += 3)
                {
                    Vector3 a = V[drift[t]], b = V[drift[t + 1]], c = V[drift[t + 2]];
                    Vector3 fn = Vector3.Cross(b - a, c - a);
                    float len = fn.magnitude;
                    if (len < 1e-12f || fn.y >= SlopeStreamer.SteepSnow * len) continue;
                    int axis = Mathf.Abs(fn.x) >= Mathf.Abs(fn.z) ? 0 : 1;
                    for (int k = 0; k < 3; k++)
                    {
                        int i = drift[t + k], key = i * 2 + axis;
                        if (!copyOf.TryGetValue(key, out int at))
                        {
                            at = V.Count; Vector3 p = V[i];
                            V.Add(p); N.Add(N[i]);
                            uvs.Add(axis == 0 ? new Vector2(p.z / 9f, p.y / 9f) : new Vector2(p.x / 9f, p.y / 9f));
                            copyOf[key] = at;
                        }
                        drift[t + k] = at;
                    }
                }
            }
            if (V.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(V); mesh.SetNormals(N);
            if (drifts)
            {
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(drift, 1);
            }
            mesh.SetTriangles(T, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            if (drifts) mr.sharedMaterials = new[] { cragMat, driftMat != null ? driftMat : cragMat };
            else mr.sharedMaterial = cragMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            own.meshes.Add(mesh);
            return mr;
        }

        /// <summary>
        /// Snow drifted against a block's foot: a soft, rounded mound from a little way up the rock (most on its
        /// uphill side, where the wind and the slope pile it) out and down until it meets the ground's own snow
        /// almost flat, so it runs on into it. Drawn with the ground's snow material (the second submesh).
        /// </summary>
        const float DriftFall = 0.84f;   // the most a drift's ground may fall away from its rock to its edge (40 degrees)

        static void Drift(List<Vector3> V, List<Vector3> N, List<int> T, Spot s)
        {
            var plan = foot[s.v];
            int n = plan.Length * 2;
            Vector3 n0 = Slope.Normal(s.pos.x, s.pos.z);
            Vector2 uphill = -new Vector2(n0.x, n0.z);
            uphill = uphill.sqrMagnitude > 1e-6f ? uphill.normalized : Vector2.zero;
            var ring = new Vector2[n];
            for (int i = 0; i < plan.Length; i++)
            {
                ring[i * 2] = plan[i];
                ring[i * 2 + 1] = (plan[i] + plan[(i + 1) % plan.Length]) * 0.5f;
            }
            // snow drifts against a block only where it can lie all round it: a block on the edge of a ledge, part of
            // its footprint over the wall below, drew its drift down the wall in long white strips (the spot's own
            // test is at its middle only)
            for (int i = 0; i < n; i++)
            {
                Vector3 w = s.pos + s.rot * new Vector3(ring[i].x * s.scale.x, 0f, ring[i].y * s.scale.z);
                if (Slope.Normal(w.x, w.z).y < 0.6f) return;
            }
            Vector2 c = new Vector2(s.pos.x, s.pos.z);
            // across the mound, out from the rock: how far out (of its reach) and how high (of its height) each ring
            // stands, a rounded profile that leaves the ground at a shallow angle
            float[] outF = { -0.08f, 0.22f, 0.5f, 0.78f, 1f }, upF = { 1f, 0.9f, 0.58f, 0.2f, -0.04f };
            int rings = outF.Length;
            int o = V.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 w = s.pos + s.rot * new Vector3(ring[i].x * s.scale.x, 0f, ring[i].y * s.scale.z);
                Vector2 p = new Vector2(w.x, w.z);
                Vector2 d = p - c;
                float len = d.magnitude;
                Vector2 dir = len > 1e-4f ? d / len : Vector2.right;
                float up = Mathf.Max(0f, Vector2.Dot(dir, uphill));
                float hgt = 0.25f + 0.55f * up + 0.2f * Noise3(w.x * 0.7f, 0f, w.z * 0.7f, 81);
                float reach = 1.2f + 2.2f * up + 0.8f * Noise3(w.x * 0.5f, 1f, w.z * 0.5f, 82);
                // never out over a drop: where the ground beyond falls away (a block on a ledge of a gorge's wall) the
                // drift is drawn in, or its outer edge followed the wall down and hung a white sheet down the rock
                float baseY = Slope.DrawnHeight(p.x, p.y);
                for (int k = 0; k < 6 && reach > 0.3f; k++)
                {
                    Vector2 q = p + dir * reach;
                    if (Slope.DrawnHeight(q.x, q.y) >= baseY - reach * DriftFall) break;
                    reach *= 0.7f;
                }
                for (int r = 0; r < rings; r++)
                {
                    Vector2 q = p + dir * reach * outF[r];
                    float y = Slope.DrawnHeight(q.x, q.y) + hgt * upF[r];
                    V.Add(new Vector3(q.x, y, q.y)); N.Add(Vector3.zero);
                }
            }
            int t0 = T.Count;
            for (int i = 0; i < n; i++)
            {
                int i1 = (i + 1) % n;
                for (int r = 0; r < rings - 1; r++)
                {
                    int a = o + i * rings + r, b = o + i1 * rings + r, cc = o + i * rings + r + 1, dd = o + i1 * rings + r + 1;
                    TriUp(V, T, a, b, cc); TriUp(V, T, b, dd, cc);
                }
            }
            // rounded: each corner's normal the average of the faces round it, and its outer edge the ground's
            for (int t = t0; t < T.Count; t += 3)
            {
                Vector3 fn = Vector3.Cross(V[T[t + 1]] - V[T[t]], V[T[t + 2]] - V[T[t]]);
                for (int k = 0; k < 3; k++) N[T[t + k]] += fn;
            }
            for (int i = 0; i < n; i++)
                for (int r = 0; r < rings; r++)
                {
                    int k = o + i * rings + r;
                    Vector3 nn = N[k].sqrMagnitude > 1e-10f ? N[k].normalized : Vector3.up;
                    if (r == rings - 1) nn = Slope.Normal(V[k].x, V[k].z);
                    N[k] = nn;
                }
        }

        /// <summary>A triangle of the drift, wound to face up.</summary>
        static void TriUp(List<Vector3> V, List<int> T, int a, int b, int c)
        {
            if (Vector3.Cross(V[b] - V[a], V[c] - V[a]).y < 0f) { int t = b; b = c; c = t; }
            T.Add(a); T.Add(b); T.Add(c);
        }

        /// <summary>The group's baked meshes go with it when the cycle is thrown away.</summary>
        class OwnMesh : MonoBehaviour
        {
            public readonly List<Mesh> meshes = new List<Mesh>();
            void OnDestroy() { foreach (var m in meshes) if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); } }
        }

        /// <summary>A group's scanned boulders, drawn instanced each frame (the meshes cannot be baked in: they are not CPU-readable).</summary>
        class TalusDraw : MonoBehaviour
        {
            public static readonly List<TalusDraw> live = new List<TalusDraw>();
            public List<Matrix4x4>[] lists;
            public Bounds bounds;
            void OnEnable() { live.Add(this); }
            void OnDisable() { live.Remove(this); }
            void LateUpdate() { Draw(); }
            public void Draw()
            {
                if (lists == null || scanMat == null) return;
                var rp = new RenderParams(scanMat)
                {
                    worldBounds = bounds, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true
                };
                for (int v = 0; v < lists.Length; v++)
                {
                    if (lists[v].Count == 0) continue;
                    foreach (var part in scans[v])
                    {
                        if (part.local == Matrix4x4.identity) Graphics.RenderMeshInstanced(rp, part.mesh, part.sub, lists[v]);
                        else
                        {
                            var l = new List<Matrix4x4>(lists[v].Count);
                            foreach (var m in lists[v]) l.Add(m * part.local);
                            Graphics.RenderMeshInstanced(rp, part.mesh, part.sub, l);
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- the shapes

        static void Init(Shader lit)
        {
            // the mountain's own granite (world-mapped, so the blocks' strata and snow run on from the cliff's)
            if (rockMat == null)
            {
                rockMat = RockFace.Material(lit);
                // the crags' copy: the same scanned rock, its grain finer and its relief stronger (a block is seen from
                // metres away, a cliff from tens)
                cragMat = new Material(rockMat) { name = "CragRock" };
                if (cragMat.HasProperty("_Scales")) { var sc = cragMat.GetVector("_Scales"); sc.x *= 0.6f; cragMat.SetVector("_Scales", sc); }
                if (cragMat.HasProperty("_Relief")) { var rl = cragMat.GetVector("_Relief"); rl.w *= 1.35f; cragMat.SetVector("_Relief", rl); }
                // snow only where it lies flat enough to stay, its edge soft and pushed about by the rock's own
                // patches and relief (per pixel, in RockLit): on a block's top it thins out toward the rounded lip
                // instead of filling the face corner to corner; on a steep or overhanging face there is none
                if (cragMat.HasProperty("_Snow")) cragMat.SetVector("_Snow", new Vector4(0.66f, 0.92f, 0f, 0f));
                LoadScans();
            }
            if (hulls != null) return;
            int n = Total;
            hulls = new Mesh[n]; rockHits = new Mesh[n]; sv = new Vector3[n][]; sn = new Vector3[n][]; st = new int[n][];
            dv = new Vector3[n][]; dn = new Vector3[n][]; dt = new int[n][]; foot = new Vector2[n][];
            for (int v = 0; v < n; v++) MakeShape(v);
        }

        /// <summary>The photo-scanned boulders and their snowy material, as the forest's rocks wear them (Forest).</summary>
        static void LoadScans()
        {
            var sh = Resources.Load<Shader>("ScanRockLit");
            var albedo = Resources.Load<Texture2D>("ScanRocks/ScanRocksAlbedo");
            var normal = Resources.Load<Texture2D>("ScanRocks/ScanRocksNormal");
            var prefabs = Resources.LoadAll<GameObject>("ScanRocks");
            if (sh == null || !sh.isSupported || albedo == null || prefabs.Length == 0) { scans = new List<ScanPart>[0]; return; }
            scanMat = new Material(sh) { name = "TalusRock", enableInstancing = true };
            scanMat.SetTexture("_BaseMap", albedo);
            if (normal != null) scanMat.SetTexture("_BumpMap", normal);
            var list = new List<List<ScanPart>>(); var boxes = new List<Bounds>();
            foreach (var g in prefabs)
            {
                var parts = new List<ScanPart>();
                var inv = g.transform.worldToLocalMatrix;
                bool any = false; var box = new Bounds();
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var local = inv * mf.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++) parts.Add(new ScanPart { mesh = mf.sharedMesh, sub = sub, local = local });
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var pnt = local.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        if (any) box.Encapsulate(pnt); else { box = new Bounds(pnt, Vector3.zero); any = true; }
                    }
                }
                if (parts.Count == 0) continue;
                list.Add(parts); boxes.Add(box);
            }
            scans = list.ToArray(); scanBox = boxes.ToArray();
        }

        // a shape under construction: corners shared between faces (for the hull), triangles wound outward
        static readonly List<Vector3> cv = new List<Vector3>();
        static readonly List<int> ct = new List<int>();

        static Vector2[][] foot;       // each shape's plan where it meets the ground (for its drift)

        /// <summary>A bed as the plain shape made it, for the near look to break up.</summary>
        class BedDesc { public Vector2[] p; public float y0, y1, rough; public Vector2 tilt; public bool slant; }
        static List<BedDesc> beds;

        static void MakeShape(int v)
        {
            cv.Clear(); ct.Clear();
            beds = new List<BedDesc>();
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

            // drawn faceted, each face its own corners, but each corner's normal leaning toward its neighbours' (so a
            // block's top fades into its sides in the light and the snow, rather than ending at a hard white edge)
            var smooth = new Vector3[cv.Count];
            for (int t = 0; t < ct.Count; t += 3)
            {
                Vector3 fn = Vector3.Cross(cv[ct[t + 1]] - cv[ct[t]], cv[ct[t + 2]] - cv[ct[t]]).normalized;
                for (int k = 0; k < 3; k++) smooth[ct[t + k]] += fn;
            }
            var vv = new List<Vector3>(); var nn = new List<Vector3>(); var tt = new List<int>();
            for (int t = 0; t < ct.Count; t += 3)
            {
                Vector3 a = cv[ct[t]], b = cv[ct[t + 1]], c = cv[ct[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int o = vv.Count;
                vv.Add(a); vv.Add(b); vv.Add(c);
                for (int k = 0; k < 3; k++) nn.Add(Vector3.Lerp(n, smooth[ct[t + k]].normalized, 0.6f).normalized);
                tt.Add(o); tt.Add(o + 1); tt.Add(o + 2);
            }
            sv[v] = vv.ToArray(); sn[v] = nn.ToArray(); st[v] = tt.ToArray();
            foot[v] = beds.Count > 0 ? beds[0].p : new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(-1, -1), new Vector2(1, -1) };
            // up close: the same beds, broken up (a loose talus block keeps its plain look: the scans stand in for it)
            if (kind == Kind.Talus) { dv[v] = sv[v]; dn[v] = sn[v]; dt[v] = st[v]; }
            else
            {
                MakeDetail(v);
                // what it is hit as: that same broken rock, its own surface (a static solid need not be convex),
                // cooked here once for every spire of the shape
                var hit = new Mesh { name = "CragHit" + v };
                if (dv[v].Length > 65535) hit.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                hit.SetVertices(dv[v]); hit.SetTriangles(dt[v], 0);
                hit.RecalculateBounds();
                Physics.BakeMesh(hit.GetInstanceID(), false);
                rockHits[v] = hit;
            }
            beds = null;
        }

        /// <summary>
        /// The near look of a shape: every bed again, its plan's corners chipped off and its sides cut every 40 cm or
        /// so of the plan, rows up its height; each point pushed in or out by two scales of noise, joints cut in as
        /// vertical cracks, the foot of each bed tucked in under the ledge above it and its top lip pushed out over it
        /// (overhangs and ledges of varied depth), the top edge broken here and there, and the top itself rough.
        /// Smooth across a bed's sides, sharp at its top edge, so it reads as weathered rock, not cut stone.
        /// </summary>
        static void MakeDetail(int v)
        {
            var rnd = new System.Random(5501 + v * 7919);
            var V = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<int>();
            for (int b = 0; b < beds.Count; b++) DetailBed(rnd, beds[b], b, v, V, N, T);
            dv[v] = V.ToArray(); dn[v] = N.ToArray(); dt[v] = T.ToArray();
        }

        static void DetailBed(System.Random rnd, BedDesc bd, int bi, int v, List<Vector3> V, List<Vector3> N, List<int> T)
        {
            float R() => (float)rnd.NextDouble();
            int n = bd.p.Length;
            Vector2 c = Vector2.zero;
            foreach (var q0 in bd.p) c += q0;
            c /= n;
            // the plan's outline: corners chipped, sides cut into short lengths, some of the cuts joints
            var q = new List<Vector2>(); var qn = new List<Vector2>(); var joint = new List<float>();
            var chip = new float[n];
            for (int i = 0; i < n; i++) chip[i] = 0.12f + 0.22f * R();
            for (int i = 0; i < n; i++)
            {
                Vector2 a = bd.p[i], b = bd.p[(i + 1) % n];
                float L = (b - a).magnitude;
                if (L < 1e-4f) continue;
                Vector2 dir = (b - a) / L, nrm = new Vector2(dir.y, -dir.x);
                if (Vector2.Dot(nrm, (a + b) * 0.5f - c) < 0f) nrm = -nrm;
                float s0 = Mathf.Min(chip[i], 0.4f * L), s1 = Mathf.Min(chip[(i + 1) % n], 0.4f * L);
                int m = Mathf.Clamp(Mathf.RoundToInt((L - s0 - s1) / 0.4f), 1, 5);
                int j1 = m > 1 && R() < 0.75f ? 1 + (int)(R() * (m - 1)) : -1;
                int j2 = m > 2 && R() < 0.35f ? 1 + (int)(R() * (m - 1)) : -1;
                for (int k = 0; k <= m; k++)
                {
                    q.Add(a + dir * Mathf.Lerp(s0, L - s1, k / (float)m));
                    qn.Add(nrm);
                    joint.Add(k == j1 || k == j2 ? 0.12f + 0.16f * R() : 0f);
                }
            }
            int P = q.Count;
            // rows up the bed: its root, deep in the ground, is one plain ring
            float yb = Mathf.Max(bd.y0, -0.12f);
            int rows = Mathf.Clamp(Mathf.RoundToInt((bd.y1 - yb) / 0.3f), 1, 3);
            bool root = bd.y0 < yb - 0.01f;
            int rings = rows + 1 + (root ? 1 : 0);
            float lip = R() < 0.5f ? 0.04f + 0.07f * R() : 0f;   // its top pushed out over its foot: an overhang
            int seed = v * 131 + bi * 17;
            int o = V.Count;
            var grid = new Vector3[rings, P];
            for (int r = 0; r < rings; r++)
            {
                int rr = root ? r - 1 : r;
                float y = rr < 0 ? bd.y0 : Mathf.Lerp(yb, bd.y1, rr / (float)rows);
                for (int i = 0; i < P; i++)
                {
                    Vector2 pp = q[i];
                    float push = 0f;
                    if (rr >= 0)
                    {
                        push = (Noise3(pp.x * 1.7f, y * 1.7f, pp.y * 1.7f, seed) - 0.5f) * 0.3f
                             + (Noise3(pp.x * 4.3f, y * 4.3f, pp.y * 4.3f, seed + 7) - 0.5f) * 0.1f
                             - joint[i] * (0.7f + 0.3f * Noise3(y * 5f, i, 0f, seed + 3));
                        if (rr == 0 && bi > 0) push -= 0.07f + 0.05f * Noise3(pp.x * 2f, 0f, pp.y * 2f, seed + 9);          // tucked in under the ledge of the bed below's top
                        if (rr == rows) push += lip;                     // the top lip out over it
                    }
                    float yy = y;
                    if (rr == rows)
                    {
                        // the top edge: near level, with the bed's slant, and broken off here and there
                        yy += (Noise3(pp.x * 2.5f, 0f, pp.y * 2.5f, seed + 11) - 0.5f) * (bd.rough * 2f + 0.06f) + Vector2.Dot(pp - c, bd.tilt);
                        if (Noise3(pp.x * 3f, 1f, pp.y * 3f, seed + 13) > 0.62f) yy -= 0.06f + 0.12f * Noise3(pp.x * 6f, 2f, pp.y * 6f, seed);
                    }
                    else if (rr > 0) yy += (Noise3(pp.x * 5f, y * 2f, pp.y * 5f, seed + 5) - 0.5f) * 0.05f;
                    Vector2 w = pp + qn[i] * push;
                    grid[r, i] = new Vector3(w.x, yy, w.y);
                }
            }
            // the sides: shared corners, smooth across
            for (int r = 0; r < rings; r++) for (int i = 0; i < P; i++) { V.Add(grid[r, i]); N.Add(Vector3.zero); }
            int t0 = T.Count;
            for (int r = 0; r < rings - 1; r++)
                for (int i = 0; i < P; i++)
                {
                    int i1 = (i + 1) % P;
                    int a = o + r * P + i, b = o + r * P + i1, cc = o + (r + 1) * P + i, d = o + (r + 1) * P + i1;
                    Vector3 inside = new Vector3(c.x, (grid[r, i].y + grid[r + 1, i].y) * 0.5f, c.y);
                    TriOut(V, T, a, cc, b, inside); TriOut(V, T, b, cc, d, inside);
                }
            // the top: its own corners (a sharp edge round it), an inner ring and a middle, both a little rough
            int ot = V.Count;
            Vector3 topC = Vector3.zero;
            for (int i = 0; i < P; i++) topC += grid[rings - 1, i];
            topC /= P;
            for (int i = 0; i < P; i++) { V.Add(grid[rings - 1, i]); N.Add(Vector3.zero); }
            for (int i = 0; i < P; i++)
            {
                Vector3 e = grid[rings - 1, i];
                Vector3 inner = Vector3.Lerp(e, topC, 0.45f);
                inner.y = Mathf.Lerp(e.y, topC.y, 0.45f) + (Noise3(inner.x * 3f, 3f, inner.z * 3f, seed + 17) - 0.45f) * (bd.rough * 2.5f + 0.08f);
                V.Add(inner); N.Add(Vector3.zero);
            }
            V.Add(topC + Vector3.up * (Noise3(topC.x, 4f, topC.z, seed) - 0.4f) * (bd.rough * 2f + 0.08f)); N.Add(Vector3.zero);
            int mid = V.Count - 1;
            Vector3 below = new Vector3(c.x, topC.y - 2f, c.y);
            for (int i = 0; i < P; i++)
            {
                int i1 = (i + 1) % P;
                TriOut(V, T, ot + i, ot + i1, ot + P + i, below); TriOut(V, T, ot + i1, ot + P + i1, ot + P + i, below);
                TriOut(V, T, ot + P + i, ot + P + i1, mid, below);
            }
            for (int t = t0; t < T.Count; t += 3)
            {
                Vector3 fn = Vector3.Cross(V[T[t + 1]] - V[T[t]], V[T[t + 2]] - V[T[t]]);
                for (int k = 0; k < 3; k++) N[T[t + k]] += fn;
            }
            for (int i = o; i < V.Count; i++) N[i] = N[i].sqrMagnitude > 1e-10f ? N[i].normalized : Vector3.up;
            // the lip rounded over: the side's top row and the top's edge share a normal half way between, so the
            // light and the snow on the top (per pixel, by the normal) both fall away smoothly over the edge
            for (int i = 0; i < P; i++)
            {
                int side = o + (rings - 1) * P + i, edge = ot + i;
                Vector3 r = (N[side] * 0.8f + N[edge]).normalized;
                N[side] = r; N[edge] = r;
            }
        }

        static void TriOut(List<Vector3> V, List<int> T, int a, int b, int c, Vector3 inside)
        {
            Vector3 n = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (Vector3.Dot(n, (V[a] + V[b] + V[c]) / 3f - inside) < 0f) { int t = b; b = c; c = t; }
            T.Add(a); T.Add(b); T.Add(c);
        }

        /// <summary>Smooth value noise in [0, 1].</summary>
        static float Noise3(float x, float y, float z, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float fx = x - xi, fy = y - yi, fz = z - zi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float L(int a, int b, int cc) => Hash3(xi + a, yi + b, zi + cc, seed);
            float x00 = Mathf.Lerp(L(0, 0, 0), L(1, 0, 0), fx), x10 = Mathf.Lerp(L(0, 1, 0), L(1, 1, 0), fx);
            float x01 = Mathf.Lerp(L(0, 0, 1), L(1, 0, 1), fx), x11 = Mathf.Lerp(L(0, 1, 1), L(1, 1, 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        static float Hash3(int x, int y, int z, int s)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177 + s * 1442695041;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return ((uint)(h ^ (int)((uint)h >> 16))) / 4294967296f;
            }
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
            beds?.Add(new BedDesc { p = (Vector2[])p.Clone(), y0 = y0, y1 = y1, rough = rough, tilt = tilt, slant = slant });
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
