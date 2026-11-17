using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Builds what you see of each course section near the rider: park boxes and rails,
    /// crevasse ice with marker poles, and a printed sign at the start of each section.
    /// Rebuilt one cycle ahead; older cycles are thrown away.
    /// </summary>
    public class CourseDressing : MonoBehaviour
    {
        public Transform target;
        Material metal, boxSide, railMat, post, ice, poleOrange, poleBlack, signPark, signDanger, signPipe, signBack;
        readonly Dictionary<int, GameObject> built = new Dictionary<int, GameObject>();
        readonly List<int> drop = new List<int>();

        public void Build(Shader lit)
        {
            Material M(Color c, float smooth, float metallic = 0f, Color emit = default)
            {
                var m = new Material(lit);
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metallic);
                if (emit.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit); }
                return m;
            }
            metal = M(new Color(0.78f, 0.80f, 0.84f), 0.75f, 0.8f);
            boxSide = M(new Color(0.10f, 0.42f, 0.85f), 0.4f);
            railMat = M(new Color(0.85f, 0.86f, 0.9f), 0.85f, 0.9f);
            post = M(new Color(0.15f, 0.15f, 0.17f), 0.3f);
            ice = M(new Color(0.06f, 0.20f, 0.36f), 0.85f, 0f, new Color(0.0f, 0.04f, 0.09f));
            poleOrange = M(new Color(1f, 0.42f, 0.05f), 0.4f);
            poleBlack = M(new Color(0.08f, 0.08f, 0.09f), 0.3f);
            signBack = M(new Color(0.35f, 0.36f, 0.4f), 0.3f);
            signPark = Sign(lit, "Signs/SignPark");
            signDanger = Sign(lit, "Signs/SignDanger");
            signPipe = Sign(lit, "Signs/SignPipe");
        }

        public static Material Sign(Shader lit, string path)
        {
            var m = new Material(lit);
            var t = Resources.Load<Texture2D>(path);
            if (t != null) m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.25f);
            return m;
        }

        void Update()
        {
            if (target == null || metal == null) return;
            int c = Course.CycleOf(target.position.z);
            for (int k = Mathf.Max(0, c); k <= c + 1; k++)
                if (!built.ContainsKey(k)) built[k] = BuildCycle(k);
            drop.Clear();
            foreach (var kv in built) if (kv.Key < c - 0 && target.position.z > Course.First + (kv.Key + 1) * Course.Cycle + 80f) drop.Add(kv.Key);
            foreach (int k in drop) { Destroy(built[k]); built.Remove(k); }
        }

        public void Prewarm() { Update(); }

        GameObject BuildCycle(int k)
        {
            var root = new GameObject("Course " + k);
            root.transform.SetParent(transform, false);
            float zb = Course.First + k * Course.Cycle;

            // park: boxes and rails
            foreach (var f in Course.FeaturesOf(k))
            {
                Vector3 mid = (f.a + f.b) * 0.5f, dir = f.Dir;
                float len = f.Length;
                var rot = Quaternion.LookRotation(dir, Vector3.up);
                if (f.kind == Course.Kind.Box)
                {
                    Block(PrimitiveType.Cube, root.transform, mid - Vector3.up * 0.04f, rot, new Vector3(0.64f, 0.08f, len), metal);
                    Block(PrimitiveType.Cube, root.transform, mid - Vector3.up * (f.height * 0.5f + 0.05f), rot, new Vector3(0.58f, f.height + 0.1f, len - 0.1f), boxSide);
                }
                else
                {
                    Block(PrimitiveType.Cylinder, root.transform, mid, Quaternion.FromToRotation(Vector3.up, dir), new Vector3(0.09f, len * 0.5f, 0.09f), railMat);
                    for (int i = 0; i <= 2; i++)
                    {
                        Vector3 top = Vector3.Lerp(f.a, f.b, 0.08f + 0.42f * i);
                        float ground = Slope.Height(top.x, top.z);
                        float hgt = top.y - ground;
                        Block(PrimitiveType.Cylinder, root.transform, new Vector3(top.x, ground + hgt * 0.5f - 0.05f, top.z), Quaternion.identity, new Vector3(0.07f, hgt * 0.5f, 0.07f), post);
                    }
                }
            }

            // danger: crevasse ice and marker poles along the rims
            foreach (var cv in Course.CrevassesOf(k))
            {
                float zm = (cv.z0 + cv.z1) * 0.5f;
                for (float z = cv.z0 + 1f; z < cv.z1 - 1f; z += 3f)
                {
                    float cx = cv.x + (Slope.Centre(z) - Slope.Centre(zm));
                    float rim = Slope.Height(cx + cv.half + 1.5f, z);
                    Block(PrimitiveType.Cube, root.transform, new Vector3(cx, rim - 1.2f - 4.5f, z), Quaternion.identity, new Vector3(cv.half * 1.6f, 9f, 3.2f), ice);
                }
                for (float z = cv.z0; z <= cv.z1; z += 7f)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float cx = cv.x + (Slope.Centre(z) - Slope.Centre(zm)) + s * (cv.half + 0.9f);
                        float y = Slope.Height(cx, z);
                        Block(PrimitiveType.Cylinder, root.transform, new Vector3(cx, y + 0.8f, z), Quaternion.identity, new Vector3(0.06f, 0.8f, 0.06f), poleOrange);
                        Block(PrimitiveType.Cylinder, root.transform, new Vector3(cx, y + 1.45f, z), Quaternion.identity, new Vector3(0.065f, 0.12f, 0.065f), poleBlack);
                    }
            }

            // a sign at the start of each section, at the side of the piste, facing up the hill
            SignAt(root.transform, zb + Course.ParkAt - 30f, signPark, 1f);
            SignAt(root.transform, zb + Course.DangerAt - 30f, signDanger, -1f);
            SignAt(root.transform, zb + Course.PipeAt - 30f, signPipe, 1f);
            return root;
        }

        void SignAt(Transform root, float z, Material face, float side)
        {
            float x = Slope.Centre(z) + side * (Slope.PisteHalf - 1f);
            float y = Slope.Height(x, z);
            const float W = 6f, H = 1.5f, lift = 2.2f;
            for (int s = -1; s <= 1; s += 2)
                Block(PrimitiveType.Cylinder, root, new Vector3(x + s * W * 0.45f, y + lift * 0.5f + 0.4f, z), Quaternion.identity, new Vector3(0.12f, (lift + 0.8f) * 0.5f, 0.12f), post);
            Block(PrimitiveType.Cube, root, new Vector3(x, y + lift + H * 0.5f, z + 0.05f), Quaternion.identity, new Vector3(W + 0.15f, H + 0.15f, 0.08f), signBack);
            // the printed face looks up the slope at the rider (a quad shows its face to -Z)
            var q = Block(PrimitiveType.Quad, root, new Vector3(x, y + lift + H * 0.5f, z - 0.01f), Quaternion.identity, new Vector3(W, H, 1f), face);
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static GameObject Block(PrimitiveType t, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }
    }
}
