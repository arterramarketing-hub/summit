using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Jagged spires and shards of dark granite poking up through the snow along the cliffs' edges and out of their
    /// faces: the ridges' crest edges and canyon walls (the spine below the crest too), the deadly walls' lips and
    /// faces, and the ends of the jump cliffs' rock bands out in the rough. From knee-high shards to spires taller
    /// than a house, in clusters, never evenly spaced. Built by CourseDressing with the rest of a cycle.
    ///
    /// The line stays fair: nothing on the groomed piste or the crest, nothing across a jump cliff's lip where the
    /// piste flies off it, and on a ridge nothing past where a fall gets up (Ridge.Reach less Ridge.Clear).
    ///
    /// Cheap: a dozen fractured prism shapes made here once, shared; every spire in about 100 m of one cliff side
    /// is baked into one mesh (two materials: the rock, and snow on its ledges and caps), so a whole ridge is a
    /// handful of draws. Each spire has its own convex collider (the shape's own hull, scaled), all under one
    /// Obstacle "Rock": the board crashes into them (DoCrash("Rock") plays the rock hit), and the thrown body
    /// tumbles off them, which Ragdoll.LastHitObject sees, so the game screams and thumps.
    /// </summary>
    public static class CliffSpires
    {
        /// <summary>Off for before/after tests and renders: SPIRES_OFF=1 in the environment (never set on a phone).</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("SPIRES_OFF") != "1";
        const int Variants = 12;
        const float GroupLen = 100f;   // metres of one cliff side baked into one mesh
        const float UvTile = 7f;       // metres per repeat of the rock texture on a spire's faces

        static Mesh[] shapes;          // render shape (flat-shaded) per variant: submesh 0 rock, 1 snow
        static Mesh[] hulls;           // the same shape with shared corners, for its convex collider
        static float[] baseR;          // the shape's radius at the ground, per metre of height
        static Vector3[][] sv, sn;     // the render shape's vertices and normals, kept for baking
        static int[][] sRock, sSnow;
        static Material rockMat, snowMat;
        static PhysicsMaterial slick;

        /// <summary>
        /// What cliff rock feels like to a thrown body: hard and slick with rime. It is hit, and bounced off, but
        /// nothing grips on a spire or a boulder halfway down a 160 m face: the body slides off and goes on down
        /// (with ordinary friction it would perch there, never reaching the foot of the cliff).
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

        // ---------------------------------------------------------------- building a cycle's spires

        class Spot { public int v; public Vector3 pos; public Quaternion rot; public Vector3 scale; }

        class Groups
        {
            public readonly Dictionary<long, List<Spot>> map = new Dictionary<long, List<Spot>>();
            public void Add(int feature, float side, float z, Spot s)
            {
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
            if (Ridge.Built(cycle)) RidgeSpires(g, cycle);
            if (Cliffs.WallOf(cycle, out var w)) WallSpires(g, w);
            for (int k = 0; k < 2; k++) if (Cliffs.JumpOf(cycle, k, out var j)) JumpSpires(g, j);
            foreach (var kv in g.map) Bake(root, kv.Value);
        }

        static float H(int a, int b, int salt) => Slope.Hash01(a, b, salt);

        /// <summary>
        /// One spire at (x, z): h metres tall, `wide` times its shape's own girth, its axis leaned `tilt` of the way
        /// toward the face's normal and a little further out along `outward` (sideways, away from the run).
        /// Its base sits down at the lowest ground under its footprint, so no side of it floats off a steep face.
        /// </summary>
        static Spot Make(int v, float x, float z, float h, float wide, float tilt, float outward, float lean, int pick)
        {
            float r = baseR[v] * h * wide;
            float y = Slope.Height(x, z);
            float lo = Mathf.Min(Mathf.Min(Slope.Height(x - r, z), Slope.Height(x + r, z)), Mathf.Min(Slope.Height(x, z - r), Slope.Height(x, z + r)));
            Vector3 n = Slope.Normal(x, z);
            Vector3 axis = Vector3.Slerp(Vector3.up, n, tilt);
            axis = (axis + new Vector3(outward * lean, 0f, (H(pick, 3, 51) - 0.5f) * 0.25f)).normalized;
            var rot = Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.Euler(0f, H(pick, 4, 51) * 360f, 0f);
            // most of the way down to the low side, and sunk a little: its buried root hides the rest
            float py = Mathf.Lerp(y, lo, 0.75f) - 0.1f * h;
            return new Spot { v = v, pos = new Vector3(x, py, z), rot = rot, scale = new Vector3(h * wide, h, h * wide * (0.8f + 0.4f * H(pick, 5, 51))) };
        }

        /// <summary>
        /// Would a spire a across the run (r its base radius) reach where a fall off a ridge's cliff gets up? A jump
        /// cliff or a deadly wall can stand where a ridge's canyons still run on (the spine below a crest).
        /// </summary>
        static bool OnGetUp(float a, float z, float r)
        {
            if (Ridge.Depth(z) <= 0.05f) return false;
            float side = a < 0f ? -1f : 1f, d = Mathf.Abs(a), reach = Ridge.Reach(z, side);
            return d + r * 1.5f > reach - Ridge.Clear && d - r * 1.5f < reach + 8f;
        }

        /// <summary>A variant: kinds take turns (spire, blade, crag, twin), so a cluster mixes them.</summary>
        static int Pick(int a, int b, int salt) => Mathf.Min(Variants - 1, (int)(H(a, b, salt) * Variants));

        static void RidgeSpires(Groups g, int cycle)
        {
            float zb = Course.First + cycle * Course.Cycle;
            int feature = 1;
            for (int si = 0; si < 2; si++)
            {
                float s = si == 0 ? -1f : 1f;
                // along the edge (the crest's, then the spine's), in clusters
                int i = 0;
                for (float z = zb + Ridge.At + Ridge.RampIn * 0.3f; z < zb + Ridge.FadeEnd - 40f; i++)
                {
                    z += 4f + 9f * H(cycle * 5 + si, i, 52);
                    if (!Ridge.CliffSide(z, s) || Ridge.DepthHere(z, s) < 22f) continue;
                    float half = Ridge.HalfWidth(z), limit = Ridge.Reach(z, s) - Ridge.Clear;
                    int n = 1 + (int)(H(cycle, i * 2 + si, 53) * 5f);
                    float e0 = Ridge.Shoulder * 0.7f + 2.2f * H(cycle, i * 2 + si, 54);
                    for (int m = 0; m < n; m++)
                    {
                        int pick = cycle * 7919 + i * 31 + m * 7 + si * 3;
                        float zz = z + (H(pick, 1, 55) - 0.5f) * 7f;
                        if (!Ridge.CliffSide(zz, s)) continue;
                        // the first of a cluster stands tall; the rest are shards round its foot
                        float h = m == 0 ? 2.2f + 7f * Mathf.Pow(H(pick, 2, 55), 1.3f) : 0.5f + 3f * Mathf.Pow(H(pick, 2, 55), 1.8f);
                        int v = Pick(pick, 6, 55);
                        float wide = 0.85f + 0.4f * H(pick, 7, 55);
                        float r = baseR[v] * h * wide;
                        float hz = Ridge.HalfWidth(zz);
                        // its inner side a metre clear of the rideable top at least
                        float d = Mathf.Max(hz + e0 + 2.6f * H(pick, 8, 55), hz + 1.5f + r * 1.4f);
                        if (d + r * 1.5f > limit) continue;
                        float x = Slope.Centre(zz) + s * d;
                        g.Add(feature, s, zz, Make(v, x, zz, h, wide, 0.2f, s, 0.12f + 0.15f * H(pick, 9, 55), pick));
                    }
                }
                // down the face: bigger, jutting out of it
                i = 0;
                for (float z = zb + Ridge.At + Ridge.RampIn * 0.5f; z < zb + Ridge.FadeEnd - 60f; i++)
                {
                    z += 5f + 10f * H(cycle * 5 + si, i, 56);
                    if (!Ridge.CliffSide(z, s)) continue;
                    float depth = Ridge.DepthHere(z, s);
                    if (depth < 30f) continue;
                    float half = Ridge.HalfWidth(z), reach = Ridge.Reach(z, s), limit = reach - Ridge.Clear;
                    float big = 1f + depth / 110f;
                    int n = 1 + (int)(H(cycle, i * 2 + si, 57) * 3f);
                    // the upper face mostly, where the big ones fit
                    float e = Ridge.Shoulder + 4f + Mathf.Pow(H(cycle, i * 2 + si, 58), 1.6f) * Mathf.Max(1f, reach - half - Ridge.Shoulder - 14f);
                    for (int m = 0; m < n; m++)
                    {
                        int pick = cycle * 6271 + i * 37 + m * 11 + si * 5;
                        float zz = z + (H(pick, 1, 59) - 0.5f) * 9f;
                        float h = (5f + 16f * Mathf.Pow(H(pick, 2, 59), 1.4f)) * big * (m == 0 ? 1f : 0.5f);
                        int v = Pick(pick, 6, 59);
                        float wide = 0.9f + 0.4f * H(pick, 7, 59);
                        float r = baseR[v] * h * wide;
                        float d = half + e + (H(pick, 8, 59) - 0.5f) * 8f;
                        if (d - r * 1.4f < half + Ridge.Shoulder + 2f) d = half + Ridge.Shoulder + 2f + r * 1.4f;
                        // too big to fit above the foot (where a fall gets up): a smaller one
                        float room = (limit - d) / (baseR[v] * wide * 1.5f + 0.15f);
                        if (room < h) { h = room; r = baseR[v] * h * wide; }
                        if (h < 3f) continue;
                        float x = Slope.Centre(zz) + s * d;
                        // near upright: a pinnacle standing proud of the face, not lying in it
                        g.Add(feature, s, zz, Make(v, x, zz, h, wide, 0.12f, s, 0.08f, pick));
                    }
                }
                // the canyon's far wall, toward its top: big crags along the skyline you see from the crest
                i = 0;
                for (float z = zb + Ridge.At + Ridge.RampIn; z < zb + Ridge.FadeEnd - 80f; i++)
                {
                    z += 9f + 16f * H(cycle * 5 + si, i, 60);
                    if (Ridge.DepthHere(z, s) < 40f || Ridge.SideAt(z, s) == Ridge.Side.Wall || Ridge.SideAt(z, s) == Ridge.Side.Bank) continue;   // (a gorge has no far wall)
                    float big = Ridge.DepthHere(z, s) / Ridge.CliffDepth;
                    int n = 1 + (int)(H(cycle, i * 2 + si, 61) * 3f);
                    for (int m = 0; m < n; m++)
                    {
                        int pick = cycle * 4447 + i * 41 + m * 13 + si * 7;
                        float zz = z + (H(pick, 1, 62) - 0.5f) * 10f;
                        float d = Ridge.FarIn + (Ridge.FarOut - Ridge.FarIn) * (0.45f + 0.45f * H(pick, 2, 62));
                        float h = (8f + 22f * Mathf.Pow(H(pick, 3, 62), 1.4f)) * (0.6f + 0.6f * big) * (m == 0 ? 1f : 0.6f);
                        int v = Pick(pick, 6, 62);
                        float x = Slope.Centre(zz) + s * d;
                        // leaning out over the canyon, toward the crest
                        g.Add(feature + 1, s, zz, Make(v, x, zz, h, 0.9f + 0.4f * H(pick, 7, 62), 0.12f, -s, 0.08f, pick));
                    }
                }
            }
        }

        static void WallSpires(Groups g, Cliffs.Wall w)
        {
            int feature = 3, i = 0;
            // along the lip, in clusters
            for (float z = w.z0 + 6f; z < w.z2 - 10f; i++)
            {
                z += 5f + 10f * H(w.cycle, i, 63);
                float hgt = Cliffs.WallHeight(w, z);
                if (hgt < 5f) continue;
                int n = 1 + (int)(H(w.cycle, i, 64) * 4.5f);
                for (int m = 0; m < n; m++)
                {
                    int pick = w.cycle * 3571 + i * 29 + m * 5;
                    float zz = z + (H(pick, 1, 65) - 0.5f) * 6f;
                    float h = m == 0 ? 2f + 6f * Mathf.Pow(H(pick, 2, 65), 1.3f) : 0.5f + 2.6f * Mathf.Pow(H(pick, 2, 65), 1.8f);
                    int v = Pick(pick, 6, 65);
                    float wide = 0.85f + 0.4f * H(pick, 7, 65);
                    float r = baseR[v] * h * wide;
                    // on the lip and just over it, never back toward the poles and the run
                    float a = w.edge + 0.4f + r * 1.4f + 2.4f * H(pick, 8, 65);
                    if (OnGetUp(w.side * a, zz, r)) continue;
                    float x = Slope.Centre(zz) + w.side * a;
                    g.Add(feature, w.side, zz, Make(v, x, zz, h, wide, 0.2f, w.side, 0.12f + 0.15f * H(pick, 9, 65), pick));
                }
            }
            // out of the face
            i = 0;
            for (float z = w.z0 + 14f; z < w.z2 - 20f; i++)
            {
                z += 6f + 11f * H(w.cycle, i, 66);
                float hgt = Cliffs.WallHeight(w, z);
                if (hgt < 12f) continue;
                int n = 1 + (int)(H(w.cycle, i, 67) * 2.5f);
                for (int m = 0; m < n; m++)
                {
                    int pick = w.cycle * 2953 + i * 23 + m * 3;
                    float zz = z + (H(pick, 1, 68) - 0.5f) * 8f;
                    float hz = Cliffs.WallHeight(w, zz);
                    float h = (4f + 10f * Mathf.Pow(H(pick, 2, 68), 1.4f)) * (0.6f + hz / 50f) * (m == 0 ? 1f : 0.6f);
                    int v = Pick(pick, 6, 68);
                    float r = baseR[v] * h * 1.3f;
                    float a = w.edge + hz * 0.55f * (0.2f + 0.65f * H(pick, 3, 68));
                    if (OnGetUp(w.side * a, zz, r)) continue;
                    float x = Slope.Centre(zz) + w.side * a;
                    g.Add(feature, w.side, zz, Make(v, x, zz, h, 0.9f + 0.4f * H(pick, 7, 68), 0.12f, w.side, 0.08f, pick));
                }
            }
        }

        static void JumpSpires(Groups g, Cliffs.Jump j)
        {
            int feature = 4 + j.k;
            for (int si = 0; si < 2; si++)
            {
                float s = si == 0 ? -1f : 1f;
                int i = 0;
                // out in the rough either side, where the rock band runs on beyond the piste and fades away
                for (float a = Slope.PisteHalf + 6f; a < Slope.PisteHalf + 6f + Cliffs.JumpBand * 0.75f; i++)
                {
                    a += 2.5f + 5f * H(j.cycle * 3 + j.k, i * 2 + si, 69);
                    int n = 1 + (int)(H(j.cycle, i * 2 + si + j.k * 50, 70) * 3.5f);
                    // shorter as the band dies away
                    float fade = 1f - (a - Slope.PisteHalf - 6f) / Cliffs.JumpBand;
                    for (int m = 0; m < n; m++)
                    {
                        int pick = j.cycle * 2179 + j.k * 997 + i * 19 + m * 3 + si;
                        float aa = Mathf.Max(Slope.PisteHalf + 6f, a + (H(pick, 1, 71) - 0.5f) * 3f);
                        float h = (m == 0 ? 1.2f + (1f + j.drop * 0.6f) * H(pick, 2, 71) : 0.5f + 1.6f * H(pick, 2, 71)) * (0.5f + 0.6f * fade);
                        int v = Pick(pick, 6, 71);
                        float wide = 0.85f + 0.4f * H(pick, 7, 71);
                        float r = baseR[v] * h * wide;
                        aa = Mathf.Max(aa, Slope.PisteHalf + 5f + r);
                        float x = Slope.Centre(j.z) + s * aa;
                        // poking up at the lip and out of the band below it
                        float z = Cliffs.LipZ(j, s * aa) + (H(pick, 3, 71) - 0.25f) * j.face;
                        if (OnGetUp(s * aa, z, r)) continue;
                        g.Add(feature, s, z, Make(v, x, z, h, wide, 0.3f, 0f, 0f, pick));
                    }
                }
            }
        }

        // ---------------------------------------------------------------- baking a group into one mesh

        static readonly List<Vector3> bv = new List<Vector3>(), bn = new List<Vector3>();
        static readonly List<Vector2> bu = new List<Vector2>();
        static readonly List<int> bRock = new List<int>(), bSnow = new List<int>();

        static void Bake(Transform root, List<Spot> spots)
        {
            if (spots.Count == 0) return;
            bv.Clear(); bn.Clear(); bu.Clear(); bRock.Clear(); bSnow.Clear();
            var go = new GameObject("CliffSpires");
            go.transform.SetParent(root, false);
            go.AddComponent<Obstacle>().label = "Rock";
            foreach (var s in spots)
            {
                var m = Matrix4x4.TRS(s.pos, s.rot, s.scale);
                var mn = m.inverse.transpose;
                var v = sv[s.v]; var n = sn[s.v];
                int o = bv.Count;
                for (int k = 0; k < v.Length; k++)
                {
                    Vector3 p = m.MultiplyPoint3x4(v[k]);
                    Vector3 nw = mn.MultiplyVector(n[k]).normalized;
                    bv.Add(p); bn.Add(nw);
                    // mapped flat onto each face from the axis it faces most, at one size whatever the spire's size
                    float ax = Mathf.Abs(nw.x), ay = Mathf.Abs(nw.y), az = Mathf.Abs(nw.z);
                    bu.Add((ay > ax && ay > az ? new Vector2(p.x, p.z) : ax > az ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y)) / UvTile);
                }
                foreach (int t in sRock[s.v]) bRock.Add(o + t);
                foreach (int t in sSnow[s.v]) bSnow.Add(o + t);
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
            mesh.SetVertices(bv); mesh.SetNormals(bn); mesh.SetUVs(0, bu);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(bRock, 0); mesh.SetTriangles(bSnow, 1);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { rockMat, snowMat };
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
            if (rockMat == null)
            {
                // the cliffs' own granite, darker: fresh, frost-split rock that has not weathered pale
                rockMat = new Material(RockFace.Material(lit)) { name = "SpireRock" };
                rockMat.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.64f));
                rockMat.SetFloat("_Smoothness", 0.18f);
                snowMat = new Material(lit) { name = "SpireSnow" };
                snowMat.SetColor("_BaseColor", new Color(0.9f, 0.93f, 0.98f));
                snowMat.SetFloat("_Smoothness", 0.22f);
                snowMat.SetFloat("_Metallic", 0f);
            }
            if (shapes != null) return;
            shapes = new Mesh[Variants]; hulls = new Mesh[Variants]; baseR = new float[Variants];
            sv = new Vector3[Variants][]; sn = new Vector3[Variants][]; sRock = new int[Variants][]; sSnow = new int[Variants][];
            for (int v = 0; v < Variants; v++) MakeShape(v);
        }

        // a shape under construction: corners shared between faces (for the hull), triangles wound outward
        static readonly List<Vector3> cv = new List<Vector3>();
        static readonly List<int> ct = new List<int>();

        static void MakeShape(int v)
        {
            cv.Clear(); ct.Clear();
            var rnd = new System.Random(7717 + v * 104729);
            float R() => (float)rnd.NextDouble();
            int kind = v % 4;
            float r0;
            switch (kind)
            {
                case 0:   // a spire: tall, slender, kinked, with a sharp broken tip
                    r0 = 0.15f + 0.07f * R();
                    Prism(rnd, Vector2.zero, 1f, r0, 1f, new Vector2((R() - 0.5f) * 0.12f, 0.08f + 0.1f * R()), 5 + (int)(R() * 2f), 5, true);
                    break;
                case 1:   // a blade: a flat shard of split slab, leaning, its top snapped off at a slant
                    r0 = 0.24f + 0.08f * R();
                    Prism(rnd, Vector2.zero, 1f, r0, 0.32f + 0.1f * R(), new Vector2(0f, 0.15f + 0.1f * R()), 4 + (int)(R() * 2f), 3, R() < 0.5f);
                    break;
                case 2:   // a crag: a blocky fractured tooth, with a smaller shard split off beside it
                    r0 = 0.36f + 0.1f * R();
                    Prism(rnd, Vector2.zero, 1f, r0, 0.75f + 0.2f * R(), new Vector2((R() - 0.5f) * 0.1f, 0.06f), 6, 3, false);
                    Prism(rnd, new Vector2(r0 * 0.95f, (R() - 0.5f) * r0), 0.5f + 0.15f * R(), r0 * 0.45f, 0.7f, new Vector2(0.12f, 0.05f), 5, 2, true);
                    break;
                default:  // twin spires off one root
                    r0 = 0.17f + 0.05f * R();
                    Prism(rnd, new Vector2(-r0 * 0.5f, 0f), 1f, r0, 0.9f, new Vector2(-0.06f, 0.1f), 5, 4, true);
                    Prism(rnd, new Vector2(r0 * 0.75f, (R() - 0.5f) * r0), 0.55f + 0.2f * R(), r0 * 0.75f, 0.9f, new Vector2(0.15f, 0.08f), 5, 3, true);
                    break;
            }
            baseR[v] = r0;

            // the collider's hull: the corners themselves
            var hull = new Mesh { name = "SpireHull" + v };
            hull.SetVertices(cv); hull.SetTriangles(ct, 0);
            hull.RecalculateBounds();
            hulls[v] = hull;
            Physics.BakeMesh(hull.GetInstanceID(), true);

            // drawn flat-shaded, each face its own corners; faces looking up wear snow
            var vv = new List<Vector3>(); var nn = new List<Vector3>(); var rock = new List<int>(); var snow = new List<int>();
            for (int t = 0; t < ct.Count; t += 3)
            {
                Vector3 a = cv[ct[t]], b = cv[ct[t + 1]], c = cv[ct[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int o = vv.Count;
                vv.Add(a); vv.Add(b); vv.Add(c); nn.Add(n); nn.Add(n); nn.Add(n);
                var list = n.y > 0.62f ? snow : rock;
                list.Add(o); list.Add(o + 1); list.Add(o + 2);
            }
            sv[v] = vv.ToArray(); sn[v] = nn.ToArray(); sRock[v] = rock.ToArray(); sSnow[v] = snow.ToArray();
            var m = new Mesh { name = "Spire" + v };
            m.SetVertices(vv); m.SetNormals(nn);
            m.subMeshCount = 2; m.SetTriangles(rock, 0); m.SetTriangles(snow, 1);
            shapes[v] = m;
        }

        /// <summary>
        /// A fractured prism rising from well below the ground (y = -0.6) to h: `sides` corners round, each ring of
        /// them jittered, kinked and twisted, narrowing as it climbs and leaning by `lean` (x, z) at the top; `flat`
        /// squashes it front to back into a blade. Topped by a sharp point, or snapped off at a slant.
        /// </summary>
        static void Prism(System.Random rnd, Vector2 c, float h, float r, float flat, Vector2 lean, int sides, int rings, bool sharp)
        {
            float R() => (float)rnd.NextDouble();
            const float root = -0.6f;
            int first = cv.Count;
            float twist = R() * 6.283f, top = sharp ? h * (0.62f + 0.12f * R()) : h;
            // the snapped-off top's slant: a plane through the top, falling away one way
            Vector2 slantDir = new Vector2(R() - 0.5f, R() - 0.5f).normalized;
            float slant = sharp ? 0f : (0.25f + 0.5f * R()) * r * 2f;
            var centres = new Vector3[rings + 1];
            float step = 1f;   // frost-split ledges: now and then the column steps in, leaving a shelf that holds snow
            for (int k = 0; k <= rings; k++)
            {
                if (k > 0 && k < rings && R() < 0.45f) step *= 0.72f + 0.12f * R();
                float t = k / (float)rings;
                float y = Mathf.Lerp(root, top, k == 0 || k == rings ? t : t + (R() - 0.5f) * 0.5f / rings);
                float up = Mathf.Max(0f, y) / h;
                float taper = step * Mathf.Lerp(1.12f, sharp ? 0.55f : 0.75f, Mathf.Pow(Mathf.Clamp01((y - root) / (top - root)), 1.1f));
                Vector2 kink = new Vector2(R() - 0.5f, R() - 0.5f) * r * 0.5f * (k == 0 ? 0f : 1f);
                float squash = flat * (0.8f + 0.4f * R());
                Vector2 cc = c + lean * up * up * h + kink;
                centres[k] = new Vector3(cc.x, y, cc.y);
                float tw = twist + (R() - 0.5f) * 0.35f;
                for (int i = 0; i < sides; i++)
                {
                    float ang = tw + (i + (R() - 0.5f) * 0.45f) * 6.2832f / sides;
                    float rr = r * taper * (0.62f + 0.65f * R());
                    float px = Mathf.Cos(ang) * rr, pz = Mathf.Sin(ang) * rr * squash;
                    float py = y;
                    if (k == rings && !sharp) py = y - (px * slantDir.x + pz * slantDir.y) / Mathf.Max(0.01f, r) * slant - R() * 0.06f * h;
                    else if (k > 0) py = y + (R() - 0.5f) * 0.14f * h / rings;   // ragged, never a level ring
                    cv.Add(new Vector3(cc.x + px, py, cc.y + pz));
                }
            }
            // the sides
            for (int k = 0; k < rings; k++)
                for (int i = 0; i < sides; i++)
                {
                    int i1 = (i + 1) % sides;
                    int a = first + k * sides + i, b = first + k * sides + i1, cc = first + (k + 1) * sides + i, d = first + (k + 1) * sides + i1;
                    Vector3 mid = (centres[k] + centres[k + 1]) * 0.5f;
                    Tri(a, cc, b, mid); Tri(b, cc, d, mid);
                }
            int last = first + rings * sides;
            if (sharp)
            {
                // the tip: off-centre, leaning on with the rest of it
                Vector3 ce = centres[rings];
                Vector2 tipOff = lean * 0.35f + new Vector2(R() - 0.5f, R() - 0.5f) * r * 0.5f;
                int tip = cv.Count;
                cv.Add(new Vector3(ce.x + tipOff.x, h, ce.z + tipOff.y));
                for (int i = 0; i < sides; i++) Tri(last + i, tip, last + (i + 1) % sides, ce);
            }
            else
            {
                // the snapped top: a fan from a corner, so it stays one ragged face
                Vector3 ce = centres[rings] + Vector3.down * h;
                for (int i = 1; i < sides - 1; i++) Tri(last, last + i + 1, last + i, ce);
            }
            // (the root, far underground, is left open)
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
