using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Clothes in the air, for everyone who wears the hero's cut of jacket and trousers: the rider (RiderBody), the camp's
    /// couple (CampCouple), Alyssa at the fire and the people of the camp, the lodge and the lifts (Person, PeopleKit).
    ///
    /// Add gives a jacket's or a pair of trousers' mesh six shapes the air can blow into, named (a mesh may carry shapes
    /// of its own first, a collar's or a hood's): Billow, WindNose, WindToe and WindHeel push the cloth four ways fixed to
    /// the body (for the rider: to the board's tail, its nose, its toe edge and its heel edge; for anyone else on foot:
    /// behind them, in front, to their right and their left), the side away from the air ballooning and the side it hits
    /// staying put; FlapA and FlapB ripple it outward. The trousers are pinned where the boots grip them and at the
    /// waistband, free down the shins, knees and thighs, and freest at the hems.
    ///
    /// Air turns the air on someone (world m/s) into those shapes' weights: what it pushes down their back and across
    /// them, eased, and a flutter that runs faster the harder it blows. WeatherAt is the weather's wind where someone
    /// stands (TreeWind: its way and its gusts at that moment, as the trees round them have it).
    /// </summary>
    public static class ClothWind
    {
        /// <summary>A mesh's wind shapes, by index (-1: none).</summary>
        public struct Shapes
        {
            public int billow, nose, toe, heel, flapA, flapB;
            public bool Ok => billow >= 0 && flapB >= 0;
            public static Shapes Of(Mesh m) => m == null ? None : new Shapes
            {
                billow = m.GetBlendShapeIndex("Billow"), nose = m.GetBlendShapeIndex("WindNose"), toe = m.GetBlendShapeIndex("WindToe"),
                heel = m.GetBlendShapeIndex("WindHeel"), flapA = m.GetBlendShapeIndex("FlapA"), flapB = m.GetBlendShapeIndex("FlapB"),
            };
            public static readonly Shapes None = new Shapes { billow = -1, nose = -1, toe = -1, heel = -1, flapA = -1, flapB = -1 };

            /// <summary>No wind in it: the cloth at rest (the trousers of someone sitting, lying on the seat).</summary>
            public void Still(SkinnedMeshRenderer smr)
            {
                if (smr == null || !Ok) return;
                smr.SetBlendShapeWeight(billow, 0f); smr.SetBlendShapeWeight(nose, 0f); smr.SetBlendShapeWeight(toe, 0f);
                smr.SetBlendShapeWeight(heel, 0f); smr.SetBlendShapeWeight(flapA, 0f); smr.SetBlendShapeWeight(flapB, 0f);
            }
        }

        /// <summary>The air on someone standing in the weather's wind, m/s: a breeze between gusts, a storm's gust about two
        /// thirds of what riding flat out feels like.</summary>
        public static float WeatherAir(float strength, float gust) => strength * (1.5f + 7f * gust);

        /// <summary>The weather's wind where someone stands (world m/s), times how far they are out in it (0 indoors).</summary>
        public static Vector3 WeatherAt(Vector3 at, float outside = 1f)
        {
            if (outside <= 0f || TreeWind.Strength <= 0f) return Vector3.zero;
            return new Vector3(TreeWind.Dir.x, 0f, TreeWind.Dir.y) * (WeatherAir(TreeWind.Strength, TreeWind.Gust(at.x, at.z)) * outside);
        }

        /// <summary>Where a skinned mesh's own units and directions are drawn in the world, now: through the bone most of it
        /// hangs from and that bone's bind pose (its renderer's transform is not what places it).</summary>
        public static Matrix4x4 MeshToWorld(SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh; var bones = smr.bones; var binds = mesh != null ? mesh.bindposes : null;
            if (mesh == null || bones == null || binds == null || binds.Length == 0 || bones.Length != binds.Length) return smr.transform.localToWorldMatrix;
            var weight = new float[bones.Length];
            foreach (var bw in mesh.GetAllBoneWeights()) if (bw.boneIndex >= 0 && bw.boneIndex < weight.Length) weight[bw.boneIndex] += bw.weight;
            int best = -1; float most = 0f;
            for (int i = 0; i < weight.Length; i++) if (bones[i] != null && weight[i] > most) { most = weight[i]; best = i; }
            return best >= 0 ? bones[best].localToWorldMatrix * binds[best] : smr.transform.localToWorldMatrix;
        }

        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));

        /// <summary>
        /// Give `mesh` (the renderer's own copy, its vertices final) the wind's shapes, unless it has them. `kind` says
        /// what each vertex is: 0 not cloth (left alone), 1 jacket, 2 trousers; or null with `pants` for the whole mesh.
        /// `toWorld`: the mesh as drawn now (MeshToWorld); `tailW`, `toeW`, `upW`: the body's back (where riding's air
        /// streams to), its toe side and its up, in the world now.
        /// </summary>
        public static Shapes Add(Mesh mesh, Matrix4x4 toWorld, Vector3 tailW, Vector3 toeW, Vector3 upW, bool pants, byte[] kind = null)
        {
            if (mesh == null) return Shapes.None;
            if (mesh.GetBlendShapeIndex("Billow") >= 0) return Shapes.Of(mesh);
            float unit = (toWorld.GetColumn(0).magnitude + toWorld.GetColumn(1).magnitude + toWorld.GetColumn(2).magnitude) / 3f;
            float mu = 1f / Mathf.Max(1e-6f, unit);   // metres to mesh units
            var toMesh = toWorld.inverse;
            Vector3 Dir(Vector3 w) => toMesh.MultiplyVector(w).normalized;
            Vector3 tail = Dir(tailW), toe = Dir(toeW), up = Dir(upW);
            var ways = new[] { tail, -tail, toe, -toe };
            var v = mesh.vertices; var n = mesh.normals;
            if (n == null || n.Length != v.Length) { mesh.RecalculateNormals(); n = mesh.normals; }
            var wn = Normals(mesh, v, n);
            // how far up each piece of cloth a vertex is: 0 at its foot (the trousers' hems over the boots), 1 at its top
            float[] lo = { float.MaxValue, float.MaxValue }, hi = { float.MinValue, float.MinValue };
            int K(int i) => kind != null ? kind[i] : pants ? 2 : 1;
            for (int i = 0; i < v.Length; i++)
            {
                int k = K(i); if (k == 0) continue;
                float y = Vector3.Dot(v[i], up); lo[k - 1] = Mathf.Min(lo[k - 1], y); hi[k - 1] = Mathf.Max(hi[k - 1], y);
            }
            var shapes = new Vector3[4][]; for (int w = 0; w < 4; w++) shapes[w] = new Vector3[v.Length];
            var flapA = new Vector3[v.Length]; var flapB = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                int k = K(i); if (k == 0) continue;
                bool legs = k == 2;
                Vector3 pm = v[i] * unit;   // metres, for noise
                float loose = 0.55f + 0.45f * Mathf.PerlinNoise(pm.x * 4f + pm.y * 3f + 11f, pm.z * 4f - pm.y * 2f + 5f);
                float t = hi[k - 1] > lo[k - 1] ? Mathf.Clamp01((Vector3.Dot(v[i], up) - lo[k - 1]) / (hi[k - 1] - lo[k - 1])) : 0.5f;
                // where the cloth is free: a jacket all over; trousers pinned at the boots and the waistband, the hems freest
                float stream = 1f, flap = 1f;
                if (legs)
                {
                    float free = Sm(0.02f, 0.12f, t) * (1f - Sm(0.62f, 0.97f, t));
                    float hem = Sm(0.03f, 0.08f, t) * (1f - Sm(0.2f, 0.4f, t));
                    stream = 0.2f + 0.8f * free + 0.3f * hem;
                    flap = 0.3f + 0.7f * free + 1.1f * hem;
                }
                // (the trousers bulge and ripple more: what the air from riding does to them most is stream them toward
                // the tail, which the camera behind a side-on rider looks straight along)
                float bulge = legs ? 0.075f : 0.035f, trail = 0.05f;
                for (int w = 0; w < 4; w++)
                {
                    // the side away from the air balloons out and streams with it; the side it hits stays where it is
                    // (pushed in, it would go into the leg, or let the boot's top through)
                    float lee = Mathf.Max(0f, Vector3.Dot(wn[i], ways[w]));
                    shapes[w][i] = (wn[i] * bulge * lee + ways[w] * trail * (0.12f + 0.88f * lee)) * loose * stream * mu;
                }
                // the ripples, the lee side most (riding, the air comes from the nose): outward from the cloth only
                float leeTail = Mathf.Max(0f, Vector3.Dot(wn[i], tail));
                float ra = Mathf.PerlinNoise(pm.x * 7f + pm.z * 3f, pm.y * 7f + 2f);
                float rb = Mathf.PerlinNoise(pm.y * 6f + 31f, pm.z * 6f + pm.x * 2f + 17f);
                float flutter = (0.3f + 0.7f * leeTail) * (legs ? 0.07f : 0.03f) * loose * flap * mu;
                flapA[i] = wn[i] * ra * 2f * flutter + tail * (ra - 0.5f) * flutter * 0.5f;
                flapB[i] = wn[i] * rb * 2f * flutter - tail * (rb - 0.5f) * flutter * 0.5f;
            }
            mesh.AddBlendShapeFrame("Billow", 100f, shapes[0], null, null);
            mesh.AddBlendShapeFrame("WindNose", 100f, shapes[1], null, null);
            mesh.AddBlendShapeFrame("WindToe", 100f, shapes[2], null, null);
            mesh.AddBlendShapeFrame("WindHeel", 100f, shapes[3], null, null);
            mesh.AddBlendShapeFrame("FlapA", 100f, flapA, null, null);
            mesh.AddBlendShapeFrame("FlapB", 100f, flapB, null, null);
            return Shapes.Of(mesh);
        }

        /// <summary>The wind's normals: welded by position, then smoothed across the cloth along its triangles' edges (six
        /// rings), so pieces lying side by side (a puffer's quilted panels, a pocket) move as one, no slit opens between
        /// them, and the bulges are broad and round.</summary>
        static Vector3[] Normals(Mesh mesh, Vector3[] v, Vector3[] n)
        {
            var id = new int[v.Length]; var ids = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); if (!ids.TryGetValue(k, out int w)) { w = ids.Count; ids[k] = w; } id[i] = w; }
            int count = ids.Count;
            var nb = new HashSet<int>[count];
            for (int i = 0; i < count; i++) nb[i] = new HashSet<int>();
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                if (mesh.GetTopology(sm) != MeshTopology.Triangles) continue;
                var t = mesh.GetTriangles(sm);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = id[t[i]], b = id[t[i + 1]], c = id[t[i + 2]];
                    nb[a].Add(b); nb[a].Add(c); nb[b].Add(a); nb[b].Add(c); nb[c].Add(a); nb[c].Add(b);
                }
            }
            var cur = new Vector3[count];
            for (int i = 0; i < v.Length; i++) cur[id[i]] += n[i];
            for (int i = 0; i < count; i++) cur[i] = cur[i].normalized;
            var next = new Vector3[count];
            for (int pass = 0; pass < 6; pass++)
            {
                for (int i = 0; i < count; i++) { var acc = cur[i]; foreach (int j in nb[i]) acc += cur[j]; next[i] = acc.sqrMagnitude > 1e-12f ? acc.normalized : cur[i]; }
                var swap = cur; cur = next; next = swap;
            }
            var o = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) o[i] = cur[id[i]];
            return o;
        }

        /// <summary>Which vertices of a mesh are jacket (1) or trousers (2), by their submeshes' materials' names
        /// (Jacket..., Fleece..., Hoodie..., Knit...; Pants...): for a model whose clothes share a mesh with the rest.</summary>
        public static byte[] KindsByMaterial(Mesh mesh, Material[] mats)
        {
            var kind = new byte[mesh.vertexCount];
            bool any = false;
            for (int sm = 0; sm < mesh.subMeshCount && sm < mats.Length; sm++)
            {
                string n = mats[sm] != null ? mats[sm].name : "";
                byte k = n.StartsWith("Pants") ? (byte)2 : n.StartsWith("Jacket") || n.StartsWith("Fleece") || n.StartsWith("Hoodie") || n.StartsWith("Knit") ? (byte)1 : (byte)0;
                if (k == 0) continue;
                any = true;
                foreach (int i in mesh.GetTriangles(sm)) kind[i] = k;
            }
            return any ? kind : null;
        }

        /// <summary>
        /// The air on someone's clothes, eased: down their back (+, to the tail; - toward the nose), across them (+ to the
        /// toe side), and its flutter. Step it with the air (world m/s) and the body's back and toe side in the world now;
        /// Apply puts it on a cloth mesh's shapes.
        /// </summary>
        public struct Air
        {
            public float billow, toward, flapPhase, flapAmp;

            /// <param name="extra">a push down the back on top (the rider in the air)</param>
            public void Step(float dt, Vector3 air, Vector3 tailW, Vector3 toeW, float extra = 0f)
            {
                float b = Vector3.Dot(air, tailW) / 30f + extra, c = Vector3.Dot(air, toeW) / 30f;
                float total = Mathf.Clamp01(new Vector2(b, c).magnitude);
                float ease = 1f - Mathf.Exp(-dt * 3f);
                billow = Mathf.Lerp(billow, Mathf.Clamp(b, -1f, 1f), ease);
                toward = Mathf.Lerp(toward, Mathf.Clamp(c, -1f, 1f), ease);
                flapAmp = Mathf.Lerp(flapAmp, total, ease);
                flapPhase += dt * Mathf.PI * 2f * (3f + 9f * flapAmp);
                flapPhase = Mathf.Repeat(flapPhase, Mathf.PI * 2f * 100f);   // (100 turns: 137 of the second ripple's, so neither jumps)
            }

            public void Apply(SkinnedMeshRenderer smr, in Shapes s)
            {
                if (smr == null || !s.Ok) return;
                float amp = 100f * flapAmp;
                smr.SetBlendShapeWeight(s.billow, 100f * Mathf.Max(0f, billow));
                smr.SetBlendShapeWeight(s.nose, 100f * Mathf.Max(0f, -billow));
                smr.SetBlendShapeWeight(s.toe, 100f * Mathf.Max(0f, toward));
                smr.SetBlendShapeWeight(s.heel, 100f * Mathf.Max(0f, -toward));
                smr.SetBlendShapeWeight(s.flapA, amp * (0.5f + 0.5f * Mathf.Sin(flapPhase)));
                smr.SetBlendShapeWeight(s.flapB, amp * (0.5f + 0.5f * Mathf.Sin(flapPhase * 1.37f + 1.3f)));
            }
        }
    }
}
