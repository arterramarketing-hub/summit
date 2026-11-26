using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// People for distance, and what the walk clips measure:
    ///   - Skinned(look): the whole person (jacket, trousers, gear in the look's shape) as one simplified skinned
    ///     mesh at rest, on the hero's skeleton, in the slot-coloured PeopleLit material: one draw, one skin. The
    ///     clothes keep their rest pose (uv2, uv3), so their patterns are drawn on them as on the full model.
    ///   - Bake(person, frame, cell): a posed person (a rider on a chair) frozen into one simplified static mesh in a
    ///     frame of its own, for drawing many at once with GPU instancing (each instance's colours from its palette row).
    ///   - WalkCycle, RunCycle: how far the body travels in one cycle of the Walk and Run clips (two steps), measured
    ///     from the planted foot, so a walker's feet can be kept from sliding at any speed.
    /// The cell sizes trade triangles for shape; the counts are logged the first time each is made.
    /// </summary>
    public static class PeopleLod
    {
        public const float SkinnedCell = 0.06f;   // the camp's people past the full model's range
        static readonly Dictionary<int, Mesh> skinned = new Dictionary<int, Mesh>();
        static float walkCycle = -1f, runCycle = -1f;

        public static float WalkCycle { get { if (walkCycle < 0f) Measure(); return walkCycle; } }
        // the stride, as a share of the full walk's, with the walk blended in at 0.1, 0.2 .. 1 over standing
        static readonly float[] Blend = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };
        /// <summary>How long a step is with the walk blended in at this weight over standing (a share of the full stride).</summary>
        public static float StrideAt(float w)
        {
            if (walkCycle < 0f) Measure();
            float f = Mathf.Clamp(w, 0f, 1f) * Blend.Length - 1f;
            if (f <= 0f) return Mathf.Max(0.05f, Blend[0] * Mathf.Clamp01(w * Blend.Length));
            int i = Mathf.Min(Blend.Length - 2, Mathf.FloorToInt(f));
            return Mathf.Lerp(Blend[i], Blend[i + 1], f - i);
        }
        public static float RunCycle { get { if (runCycle < 0f) Measure(); return runCycle; } }

        /// <summary>The planted foot's travel through each clip: the ground a cycle covers.</summary>
        static void Measure()
        {
            walkCycle = 1.16f; runCycle = 2.2f;   // (the clips as made, if they can't be measured)
            if (!PeopleKit.Load()) return;
            var t = PeopleKit.Template.transform;
            var rig = new HeroRig(t, PeopleKit.Clips);
            if (!rig.Ok) return;
            int stand = rig.Id("Stand");
            float Cycle(string clip, float w = 1f)
            {
                int id = rig.Id(clip);
                if (id < 0) return -1f;
                const int N = 120;
                var feet = new Vector3[N, 2];
                for (int k = 0; k < N; k++)
                {
                    rig.Begin(); rig.Want(id, w); if (w < 1f) { rig.Want(stand, 1f - w); rig.At(stand, 0f); } rig.At(id, k / (float)N); rig.Snap(); rig.Evaluate(0f);
                    for (int s = 0; s < 2; s++) feet[k, s] = t.InverseTransformPoint(rig.Foot[s].position);
                }
                // over each step, the lower foot carries the body: its slide backward is the ground covered
                float travel = 0f;
                for (int k = 0; k < N; k++)
                {
                    int k1 = (k + 1) % N;
                    int s = feet[k, 0].y <= feet[k, 1].y ? 0 : 1;
                    if (feet[k, s].y > 0.13f) continue;   // (a flight: both feet off, running)
                    travel += feet[k1, s].z - feet[k, s].z;   // the body faces -z: a planted foot moves +z under it
                }
                return travel;
            }
            float w = Cycle("Walk"), r = Cycle("Run");
            if (w > 0.3f) walkCycle = w;
            if (r > 0.3f) runCycle = Mathf.Max(r, walkCycle);
            // part way between standing and walking the stride isn't simply in proportion: measure it
            for (int k = 0; k < Blend.Length; k++)
            {
                float c = Cycle("Walk", (k + 1f) / Blend.Length);
                Blend[k] = c > 0.02f ? c / walkCycle : (k + 1f) / Blend.Length;
            }
            // back to rest
            rig.Begin(); rig.Want(rig.Id("Stand"), 1f); rig.Snap(); rig.Evaluate(0f);
            if (System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_LOG") == "1") Debug.Log($"People: stride by blend {string.Join(" ", System.Array.ConvertAll(Blend, b => b.ToString("0.00")))}");
            if (System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_LOG") == "1") Debug.Log($"People: a walk cycle covers {walkCycle:0.00} m ({walkCycle / Mathf.Max(0.01f, rig.Length(rig.Id("Walk"))):0.00} m/s at the clip's pace), a run cycle {runCycle:0.00} m");
        }

        // ---------------------------------------------------------------- the simplified skinned body

        static readonly Queue<PersonLook> pending = new Queue<PersonLook>();
        static readonly HashSet<int> queued = new HashSet<int>();

        /// <summary>Is this look's simplified body made yet?</summary>
        public static bool Has(PersonLook l) => skinned.TryGetValue(l.ShapeKey, out var m) && m != null;

        /// <summary>Ask for a look's simplified body to be made soon (one a frame: BuildPending).</summary>
        public static void Want(PersonLook l) { if (!Has(l) && queued.Add(l.ShapeKey)) pending.Enqueue(l); }

        /// <summary>Make one waiting simplified body (call once a frame).</summary>
        public static void BuildPending()
        {
            if (pending.Count == 0) return;
            var l = pending.Dequeue();
            queued.Remove(l.ShapeKey);
            Skinned(l);
        }

        public static Mesh Skinned(PersonLook l)
        {
            int key = l.ShapeKey;
            if (skinned.TryGetValue(key, out var have) && have != null) return have;
            var pos = new List<Vector3>(); var slot = new List<int>(); var tris = new List<int>(); var w = new List<BoneWeight>();
            var restPos = new List<Vector3>(); var restNrm = new List<Vector3>();   // (the clothes' rest pose: their patterns)
            var cut = PeopleKit.CutOf(l);
            var gearBones = cut.gear.bones;
            var index = new Dictionary<string, int>();
            for (int i = 0; i < gearBones.Length; i++) if (gearBones[i] != null) index[gearBones[i].name] = i;
            void Add(Mesh m, Transform[] bones, System.Func<int, Vector2, int> slotOf, bool cloth = false)
            {
                if (m == null) return;
                var toModel = cut.meshToModel;
                var v = m.vertices; var uv = m.uv; var bw = m.boneWeights; var t = m.triangles;
                var rp = new List<Vector3>(); var rn = new List<Vector3>();
                if (cloth) { m.GetUVs(2, rp); m.GetUVs(3, rn); }
                bool rest = rp.Count == v.Length && rn.Count == v.Length;
                // the source's bone indices, as the gear's
                var remap = new int[bones.Length];
                for (int i = 0; i < bones.Length; i++) remap[i] = bones[i] != null && index.TryGetValue(bones[i].name, out int g) ? g : 0;
                int o = pos.Count;
                for (int i = 0; i < v.Length; i++)
                {
                    pos.Add(toModel.MultiplyPoint3x4(v[i]));
                    slot.Add(slotOf(i, uv != null && i < uv.Length ? uv[i] : Vector2.zero));
                    restPos.Add(rest ? rp[i] : Vector3.zero); restNrm.Add(rest ? rn[i] : Vector3.zero);
                    var b = i < bw.Length ? bw[i] : new BoneWeight { weight0 = 1f };
                    b.boneIndex0 = remap[b.boneIndex0]; b.boneIndex1 = remap[b.boneIndex1]; b.boneIndex2 = remap[b.boneIndex2]; b.boneIndex3 = remap[b.boneIndex3];
                    w.Add(b);
                }
                foreach (var k in t) tris.Add(o + k);
            }
            Add(cut.Body, cut.body.bones, (i, uv) => PeopleKit.SJacket, true);
            Add(cut.Legs, cut.legs.bones, (i, uv) => PeopleKit.SPants, true);
            var gear = PeopleKit.Gear(l);
            Add(gear, gearBones, (i, uv) => PeopleKit.SlotOfCell(PeopleKit.CellAt(uv)));
            if (cut.head != null) Add(PeopleKit.Head(l), cut.head.bones, (i, uv) => PeopleKit.SlotOfCell(PeopleKit.CellAt(uv)));
            var mesh = PeopleKit.Simplify(pos, slot, tris, SkinnedCell, "PersonSimple" + key, w, cut.modelToMesh, restPos, restNrm);
            mesh.bindposes = gear.bindposes;
            if (System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_LOG") == "1") Debug.Log($"People: simplified body {key}: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles");
            skinned[key] = mesh;
            return mesh;
        }

        // ---------------------------------------------------------------- baked poses

        /// <summary>
        /// A posed person (everything it shows: clothes, gear, board, skis, poles) frozen into one simplified mesh,
        /// in the frame worldToFrame takes the world to. Cell: the grid the vertices merge on (bigger: fewer triangles).
        /// </summary>
        public static Mesh Bake(Person p, Matrix4x4 worldToFrame, float cell, string name)
        {
            var pos = new List<Vector3>(); var slot = new List<int>(); var tris = new List<int>();
            var restPos = new List<Vector3>(); var restNrm = new List<Vector3>();
            PeopleKit.Gather(p, worldToFrame, pos, slot, tris, restPos, restNrm);
            var mesh = PeopleKit.Simplify(pos, slot, tris, cell, name, null, null, restPos, restNrm);
            return mesh;
        }
    }
}
