using UnityEngine;

namespace Summit
{
    /// <summary>Dents left in the snow by landings and crashes: a ring of reusable decals lying on the slope.</summary>
    public class SnowMarks
    {
        readonly Transform[] quads;
        int next;

        public SnowMarks(Transform parent, Material mat, int count = 24)
        {
            var root = new GameObject("SnowMarks").transform;
            root.SetParent(parent, false);
            quads = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                quads[i] = go.transform;
            }
        }

        /// <summary>Lay a dent at a point on the snow, facing up along the surface normal.</summary>
        public void Add(Vector3 at, Vector3 normal, float size, float heading)
        {
            var q = quads[next];
            next = (next + 1) % quads.Length;
            var along = Vector3.ProjectOnPlane(new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)), normal);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.forward;
            // a quad faces -Z: point -Z up out of the snow, long axis along the travel
            q.SetPositionAndRotation(at + normal * 0.035f, Quaternion.LookRotation(-normal, along));
            q.localScale = new Vector3(size * 0.75f, size * 1.25f, 1f);
            q.gameObject.SetActive(true);
        }

        public void Clear()
        {
            foreach (var q in quads) q.gameObject.SetActive(false);
        }
    }
}
