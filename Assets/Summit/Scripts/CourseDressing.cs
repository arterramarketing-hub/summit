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
        Material metal, boxSide, railMat, post, ice, poleOrange, poleBlack, signPark, signDanger, signPipe, signBack, snow, signRidge;
        readonly Dictionary<int, GameObject> built = new Dictionary<int, GameObject>();
        readonly List<int> drop = new List<int>();

        public void Build(Shader lit, Material snowSurface)
        {
            snow = snowSurface;
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
            ice = IceMaterial(lit);
            poleOrange = M(new Color(1f, 0.42f, 0.05f), 0.4f);
            poleBlack = M(new Color(0.08f, 0.08f, 0.09f), 0.3f);
            signBack = M(new Color(0.35f, 0.36f, 0.4f), 0.3f);
            signPark = Sign(lit, "Signs/SignPark");
            signDanger = Sign(lit, "Signs/SignDanger");
            signPipe = Sign(lit, "Signs/SignPipe");
            signRidge = Sign(lit, "Signs/SignRidge");
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
            foreach (int k in drop) { FreeMeshes(built[k]); Destroy(built[k]); built.Remove(k); }
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

            // danger: the crevasses themselves (snow lips, ice walls into the dark) and marker poles
            foreach (var cv in Course.CrevassesOf(k))
            {
                BuildCrevasse(root.transform, cv);
                float zm = (cv.z0 + cv.z1) * 0.5f;
                for (float z = cv.z0; z <= cv.z1; z += 7f)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float cx = cv.x + (Slope.Centre(z) - Slope.Centre(zm)) + s * (cv.half + 3.0f);
                        float y = Slope.Height(cx, z);
                        Block(PrimitiveType.Cylinder, root.transform, new Vector3(cx, y + 0.8f, z), Quaternion.identity, new Vector3(0.06f, 0.8f, 0.06f), poleOrange);
                        Block(PrimitiveType.Cylinder, root.transform, new Vector3(cx, y + 1.45f, z), Quaternion.identity, new Vector3(0.065f, 0.12f, 0.065f), poleBlack);
                    }
            }

            // a sign at the start of each section, at the side of the piste, facing up the hill
            if (zb + Course.ParkAt + 180f < Course.ClearFrom) SignAt(root.transform, zb + Course.ParkAt - 30f, signPark, 1f);
            if (Course.CrevassesOf(k).Length > 0) SignAt(root.transform, zb + Course.DangerAt - 30f, signDanger, -1f);
            if (Course.PipeWeight(zb + (Course.PipeAt + Course.PipeEnd) * 0.5f) > 0f) SignAt(root.transform, zb + Course.PipeAt - 30f, signPipe, 1f);
            if (Ridge.Built(k)) { SignAt(root.transform, zb + Ridge.At - 30f, signRidge, -1f); RidgeDressing.Build(root.transform, k, poleOrange, poleBlack); }
            return root;
        }

        /// <summary>The crevasse meshes are made here, so they are freed here (primitives share Unity's).</summary>
        static void FreeMeshes(GameObject g)
        {
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.StartsWith("Crevasse") && mf.sharedMesh != null) { if (Application.isPlaying) Destroy(mf.sharedMesh); else DestroyImmediate(mf.sharedMesh); }
        }

        /// <summary>A rebuilt mountain: drop everything built for the old one.</summary>
        public void Rebuild()
        {
            foreach (var kv in built) if (kv.Value != null) { FreeMeshes(kv.Value); if (Application.isPlaying) Destroy(kv.Value); else DestroyImmediate(kv.Value); }
            built.Clear();
            Update();
        }

        // ---------------------------------------------------------------- crevasses

        /// <summary>Ice from the lip down: snow white, pale blue, deep blue, then black.</summary>
        static Material IceMaterial(Shader lit)
        {
            var t = new Texture2D(2, 128, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 128; y++)
            {
                float v = y / 127f;   // v = 0 at the lip, 1 at the bottom
                Color c = v < 0.07f ? new Color(0.95f, 0.97f, 1f)
                        : v < 0.2f ? Color.Lerp(new Color(0.78f, 0.9f, 0.97f), new Color(0.45f, 0.74f, 0.9f), (v - 0.07f) / 0.13f)
                        : v < 0.55f ? Color.Lerp(new Color(0.45f, 0.74f, 0.9f), new Color(0.10f, 0.32f, 0.55f), (v - 0.2f) / 0.35f)
                        : Color.Lerp(new Color(0.10f, 0.32f, 0.55f), new Color(0.01f, 0.03f, 0.08f), Mathf.Pow((v - 0.55f) / 0.45f, 0.7f));
                t.SetPixel(0, y, c); t.SetPixel(1, y, c);
            }
            t.Apply();
            var m = new Material(lit);
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.8f);
            m.EnableKeyword("_EMISSION");                       // light through the ice: a faint blue glow
            m.SetTexture("_EmissionMap", t);
            m.SetColor("_EmissionColor", new Color(0.18f, 0.30f, 0.38f));
            m.SetFloat("_Cull", 0f);                            // seen from above and from inside
            return m;
        }

        static float Jag(float z, int side, float open) =>
            open <= 0f ? 0f : 0.3f * (Slope.Noise(z * 1.3f + side * 17.1f, 77.7f) - 0.5f) * 2f * Mathf.Min(1f, open);

        void BuildCrevasse(Transform root, Course.Crevasse cv)
        {
            const float step = 0.4f, lipDrop = 0.75f;
            float[] apron = { 0f, 0.25f, 0.7f, 1.4f, 2.7f };
            const int Wall = 8;
            float za = cv.z0 - 2.6f, zb = cv.z1 + 2.6f;
            int rows = Mathf.CeilToInt((zb - za) / step) + 1;
            var sv = new List<Vector3>(); var suv = new List<Vector2>(); var st = new List<int>();
            var iv = new List<Vector3>(); var iuv = new List<Vector2>(); var it = new List<int>();
            for (int side = -1; side <= 1; side += 2)
            {
                int sBase = sv.Count, iBase = iv.Count;
                for (int r = 0; r < rows; r++)
                {
                    float z = Mathf.Min(za + r * step, zb);
                    float open = Course.Opening(cv, z, out float cx);
                    float e = Mathf.Max(0f, open + Jag(z, side, open));
                    float depth = Course.CrevasseDepth * Mathf.Sqrt(Mathf.Max(0f, open) / cv.half) - 1.5f;
                    // the snow either side, laid over the terrain's ragged hole; the lip rounds down a little
                    for (int c = 0; c < apron.Length; c++)
                    {
                        float x = cx + side * (e + apron[c]);
                        float y = Slope.Surface(x, z) + (c == 0 ? -0.06f : c == 1 ? 0.0f : 0.025f);
                        sv.Add(new Vector3(x, y, z)); suv.Add(new Vector2(x / 9f, z / 9f));
                    }
                    // the snow face under the lip (overhanging a touch), then the ice down into the dark
                    float x0 = cx + side * e, y0 = Slope.Surface(x0, z) - 0.06f;
                    iv.Add(new Vector3(x0, y0, z)); iuv.Add(new Vector2(0.5f, 0f));
                    iv.Add(new Vector3(x0 + side * 0.22f, y0 - lipDrop, z)); iuv.Add(new Vector2(0.5f, 0.06f));
                    for (int w = 1; w <= Wall; w++)
                    {
                        float f = w / (float)Wall;
                        float wob = (Slope.Noise(z * 0.8f + side * 5f, w * 1.7f + 3f) - 0.5f) * 0.7f * Mathf.Min(1f, open);
                        float x = cx + side * (Mathf.Lerp(e + 0.22f, open * 0.12f, Mathf.Pow(f, 0.75f)) + wob);
                        float y = y0 - lipDrop - Mathf.Max(0f, depth - lipDrop) * f;
                        iv.Add(new Vector3(x, y, z)); iuv.Add(new Vector2(0.5f, Mathf.Lerp(0.07f, 1f, f)));
                    }
                }
                int sc = apron.Length, ic = Wall + 2;
                for (int r = 0; r < rows - 1; r++)
                {
                    for (int c = 0; c < sc - 1; c++)
                    {
                        int a = sBase + r * sc + c, b = a + 1, d = a + sc, e2 = d + 1;
                        if (side > 0) { st.Add(a); st.Add(d); st.Add(b); st.Add(b); st.Add(d); st.Add(e2); }
                        else { st.Add(a); st.Add(b); st.Add(d); st.Add(b); st.Add(e2); st.Add(d); }
                    }
                    for (int c = 0; c < ic - 1; c++)
                    {
                        int a = iBase + r * ic + c, b = a + 1, d = a + ic, e2 = d + 1;
                        // facing into the crevasse, so the walls light from the inside
                        if (side > 0) { it.Add(a); it.Add(b); it.Add(d); it.Add(b); it.Add(e2); it.Add(d); }
                        else { it.Add(a); it.Add(d); it.Add(b); it.Add(b); it.Add(d); it.Add(e2); }
                    }
                }
            }
            MeshObject(root, "CrevasseSnow", sv, suv, st, snow, true);
            MeshObject(root, "CrevasseIce", iv, iuv, it, ice, false);
        }

        static void MeshObject(Transform root, string name, List<Vector3> v, List<Vector2> uv, List<int> t, Material m, bool shadows)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root, false);
            var mesh = new Mesh();
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
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
