using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Snow caked on the rider's jacket and trousers after a fall. The tumble packs it on: every hit and
    /// every metre slid through the snow adds some, and it gathers on the side that hit. Back on the board
    /// it melts and blows off over ten to twenty seconds, quicker the faster you ride. The look is the
    /// Summit/SnowyCloth shader; this keeps the numbers it reads.
    /// </summary>
    public class SnowyClothes
    {
        readonly List<Material> mats = new List<Material>();
        readonly Material template;
        Transform frame, pelvis;
        Quaternion frameRest = Quaternion.identity, pelvisRest = Quaternion.identity;   // rest rotations, relative to frame
        Vector3 side;          // which side of the body took the snow, in the rest frame (summed, weighted)
        float sideWeight;
        float wet, peak;
        Vector3 upRest = Vector3.up;   // up, in the rest frame
        bool wasDown;
        public float Amount { get; private set; }   // 0 clean .. 1 caked
        public static float MeltSecs = 16f;          // standing still; about 10 s at full speed

        public SnowyClothes()
        {
            // the material in Resources keeps the shader in the build
            template = Resources.Load<Material>("SnowyCloth");
        }

        public bool Ok => template != null;

        /// <summary>A snowy copy of a clothing material, in the same colour and sheen.</summary>
        public Material Make(Color colour, float smoothness)
        {
            if (template == null) return null;
            var m = new Material(template);
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", smoothness);
            m.SetVector("_SnowUp", upRest);
            mats.Add(m);
            return m;
        }

        /// <summary>
        /// Call once with the model standing at rest. Snow is placed in this frame (the model's), measured
        /// in metres, so the clumps stay put on the cloth however the limbs are thrown about.
        /// </summary>
        public void SetFrame(Transform model, Transform pelvisBone)
        {
            frame = model; pelvis = pelvisBone;
            frameRest = model.rotation;
            if (pelvisBone != null) pelvisRest = Quaternion.Inverse(frameRest) * pelvisBone.rotation;
            upRest = Quaternion.Inverse(frameRest) * Vector3.up;
            foreach (var m in mats) m.SetVector("_SnowUp", upRest);
        }

        /// <summary>
        /// Write each vertex's rest position and normal into the mesh (uv2, uv3) for the shader, and a smoothed
        /// normal into the tangents (which are skinned with the mesh) for the snow to swell along. The model is
        /// flat-shaded, so its corners are split vertices with different normals: pushed out along those, the
        /// faces would part and open white cracks along every edge.
        /// </summary>
        public void Bake(SkinnedMeshRenderer smr, Mesh mesh)
        {
            if (frame == null) return;
            var v = mesh.vertices; var n = mesh.normals;
            if (n == null || n.Length != v.Length) return;
            var sum = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
            for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); sum[k] = (sum.TryGetValue(k, out var acc) ? acc : Vector3.zero) + n[i]; }
            var inv = Quaternion.Inverse(frameRest);
            var pos = new List<Vector3>(v.Length); var nrm = new List<Vector3>(v.Length); var tan = new Vector4[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 smooth = sum[Key(v[i])].normalized;
                pos.Add(inv * (smr.transform.TransformPoint(v[i]) - frame.position));
                nrm.Add((inv * smr.transform.TransformDirection(smooth)).normalized);
                tan[i] = new Vector4(smooth.x, smooth.y, smooth.z, 1f);
            }
            mesh.SetUVs(2, pos);
            mesh.SetUVs(3, nrm);
            mesh.tangents = tan;
        }

        /// <summary>
        /// The same for plain meshes fixed to the bones (the jacket cuffs) that wear a snowy material: each gets
        /// its own copy of its mesh with the rest frame baked in.
        /// </summary>
        public void BakeRigid(Transform root)
        {
            if (frame == null) return;
            var inv = Quaternion.Inverse(frameRest);
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mats.Contains(mr.sharedMaterial)) continue;
                var mesh = Object.Instantiate(mf.sharedMesh);
                var v = mesh.vertices; var n = mesh.normals;
                var pos = new List<Vector3>(v.Length); var nrm = new List<Vector3>(v.Length); var tan = new Vector4[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    pos.Add(inv * (mr.transform.TransformPoint(v[i]) - frame.position));
                    nrm.Add((inv * mr.transform.TransformDirection(n[i])).normalized);
                    tan[i] = new Vector4(n[i].x, n[i].y, n[i].z, 1f);
                }
                mesh.SetUVs(2, pos); mesh.SetUVs(3, nrm); mesh.tangents = tan;
                mf.sharedMesh = mesh;
            }
        }

        /// <summary>A world direction, as the body sees it, in the rest frame: where on the body it points.</summary>
        Vector3 OnBody(Vector3 world)
        {
            if (pelvis == null) return Vector3.zero;
            return pelvisRest * (Quaternion.Inverse(pelvis.rotation) * world);
        }

        /// <summary>A part of the body hit the snow at this speed (m/s); the side that hit gets the most.</summary>
        public void Hit(Vector3 at, Vector3 centre, float speed)
        {
            if (speed < 1.5f) return;
            Add(Mathf.Min(speed, 12f) * 0.03f, at - centre, speed);
        }

        /// <summary>
        /// Each physics step, a part ploughing through the snow (down = the way into the snow): the faster and
        /// longer the slide, the more gets packed on, on the side pressed into it.
        /// </summary>
        public void Plough(Vector3 down, float speed, float dt)
        {
            if (speed < 0.5f) return;
            Add(Mathf.Min(speed, 8f) * 0.014f * dt, down, speed * dt);
        }

        void Add(float amount, Vector3 toward, float weight)
        {
            Amount = Mathf.Min(1f, Amount + amount);
            var d = OnBody(toward);
            if (d.sqrMagnitude > 1e-6f) { side += d.normalized * weight; sideWeight += weight; }
            wet = 0f;
        }

        /// <summary>Every frame: down in the snow nothing melts; riding, it melts and blows off.</summary>
        public void Step(float dt, bool down, float speed)
        {
            // however much stuck, it takes about as long to go: a dusting fades, a caking breaks up and drops off
            if (wasDown && !down) peak = Mathf.Max(Amount, 0.05f);
            wasDown = down;
            if (!down && Amount > 0f)
            {
                float rate = peak * (0.65f + 0.85f * Mathf.Clamp01(speed / 25f)) / MeltSecs;
                Amount = Mathf.Max(0f, Amount - rate * dt);
                // the melt soaks in, then dries off once the snow has gone
                wet = Mathf.MoveTowards(wet, Amount > 0.02f ? 1f : 0f, dt * (Amount > 0.02f ? 0.5f : 0.15f));
            }
            else if (!down) wet = Mathf.MoveTowards(wet, 0f, dt * 0.15f);
            if (Amount <= 0f && wet <= 0f && sideWeight > 0f) { side = Vector3.zero; sideWeight = 0f; }
            Push();
        }

        public void Clear() { Amount = 0f; wet = 0f; peak = 0f; side = Vector3.zero; sideWeight = 0f; Push(); }

        /// <summary>For the test harness: set the snow directly.</summary>
        public void Set(float amount, float damp) { Amount = Mathf.Clamp01(amount); peak = Mathf.Max(Amount, 0.05f); wet = Mathf.Clamp01(damp); Push(); }

        float shownAmount = -1f, shownWet = -1f; Vector4 shownSide;
        void Push()
        {
            float s = sideWeight > 0f ? Mathf.Clamp01(side.magnitude / sideWeight * 1.4f) : 0f;
            Vector4 dir = side.sqrMagnitude > 1e-8f ? (Vector4)side.normalized : new Vector4(0f, 0f, 1f, 0f);
            dir.w = s;
            if (Mathf.Abs(shownAmount - Amount) < 0.002f && Mathf.Abs(shownWet - wet) < 0.01f && (dir - shownSide).sqrMagnitude < 1e-4f) return;
            shownAmount = Amount; shownWet = wet; shownSide = dir;
            foreach (var m in mats)
            {
                m.SetFloat("_Snow", Amount);
                m.SetFloat("_Wet", wet);
                m.SetVector("_SnowDir", dir);
            }
        }
    }
}
