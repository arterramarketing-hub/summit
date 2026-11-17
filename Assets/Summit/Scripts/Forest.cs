using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Trees and rocks on a fixed grid of cells. Whether a cell holds something is a pure
    /// function of the cell, so the forest is the same for everyone on the same day.
    /// Uses the models in Resources/Trees and Resources/Rocks when there are any, each
    /// scaled to a sensible size whatever units it was made in; otherwise a simple fir.
    /// Every one carries a trunk collider, so hits come from the physics engine.
    /// </summary>
    public class Forest : MonoBehaviour
    {
        public const float Cell = 6.5f;
        public Transform target;
        public Material foliage, bark;
        public int sideCells = 12, aheadCells = 34, behindCells = 3;

        readonly List<GameObject> treeKinds = new List<GameObject>(), rockKinds = new List<GameObject>();
        readonly List<float> treeH = new List<float>(), rockH = new List<float>();
        readonly Dictionary<Vector2Int, GameObject> live = new Dictionary<Vector2Int, GameObject>();
        readonly Dictionary<int, Queue<GameObject>> spare = new Dictionary<int, Queue<GameObject>>();
        readonly List<Vector2Int> drop = new List<Vector2Int>();
        Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);
        bool loaded;

        public enum Kind { None, Tree, Rock }

        /// <summary>What stands in a cell, where, how big, and which model variant.</summary>
        public static Kind At(int cx, int cz, out Vector3 pos, out float size, out int variant)
        {
            pos = Vector3.zero; size = 1f; variant = 0;
            float wx = (cx + 0.5f) * Cell, wz = (cz + 0.5f) * Cell;
            float off = Mathf.Abs(wx - Slope.Centre(wz));
            float glade = Slope.Noise(wx * 0.016f + 9f, wz * 0.016f);   // stands of forest and open glades
            float density;
            if (off < Slope.PisteHalf - 3f)
                density = glade > 0.78f ? 0.10f : 0.012f;                 // the odd tree island on the piste
            else
                density = Mathf.Lerp(0.16f, 0.62f, Mathf.Clamp01((off - Slope.PisteHalf) / 30f)) * Mathf.Lerp(0.35f, 1.3f, glade);
            if (wz < 70f && off < 34f) density *= 0.1f;                  // a clean start
            if (Slope.Hash01(cx, cz, 1) >= density) return Kind.None;
            float x = wx + (Slope.Hash01(cx, cz, 2) - 0.5f) * Cell * 0.75f;
            float z = wz + (Slope.Hash01(cx, cz, 3) - 0.5f) * Cell * 0.75f;
            if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f || Mathf.Abs(Slope.Kicker(x, z - 14f)) > 0.01f) return Kind.None;   // keep kickers clear
            variant = (int)(Slope.Hash01(cx, cz, 7) * 64f);
            bool rock = Slope.Hash01(cx, cz, 8) < (off < Slope.PisteHalf ? 0.25f : 0.12f);
            pos = new Vector3(x, Slope.DrawnHeight(x, z), z);
            size = rock ? 0.8f + 1.4f * Slope.Hash01(cx, cz, 4) : 0.75f + 0.6f * Slope.Hash01(cx, cz, 4);
            return rock ? Kind.Rock : Kind.Tree;
        }

        void LoadKinds()
        {
            if (loaded) return;
            loaded = true;
            foreach (var g in Resources.LoadAll<GameObject>("Trees")) { treeKinds.Add(g); treeH.Add(ModelHeight(g)); }
            foreach (var g in Resources.LoadAll<GameObject>("Rocks")) { rockKinds.Add(g); rockH.Add(ModelHeight(g)); }
        }

        static float ModelHeight(GameObject prefab)
        {
            var rs = prefab.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return 1f;
            // prefab renderers report bounds even when not instantiated for MeshRenderers via their meshes
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                var m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float y = m.MultiplyPoint3x4(c).y;
                    top = Mathf.Max(top, y); bottom = Mathf.Min(bottom, y);
                }
            }
            return top > bottom ? top - bottom : 1f;
        }

        void Update() { Refresh(false); }
        public void Prewarm() { Refresh(true); }

        void Refresh(bool force)
        {
            if (target == null) return;
            LoadKinds();
            var c = new Vector2Int(Mathf.FloorToInt(target.position.x / Cell), Mathf.FloorToInt(target.position.z / Cell));
            if (!force && c == lastCell) return;
            lastCell = c;

            drop.Clear();
            foreach (var kv in live)
                if (kv.Key.x < c.x - sideCells || kv.Key.x > c.x + sideCells ||
                    kv.Key.y < c.y - behindCells || kv.Key.y > c.y + aheadCells)
                    drop.Add(kv.Key);
            foreach (var k in drop)
            {
                var go = live[k];
                live.Remove(k);
                if (go == null) continue;
                go.SetActive(false);
                int key = keyOf[go];
                if (!spare.TryGetValue(key, out var q)) spare[key] = q = new Queue<GameObject>();
                q.Enqueue(go);
            }

            for (int z = c.y - behindCells; z <= c.y + aheadCells; z++)
                for (int x = c.x - sideCells; x <= c.x + sideCells; x++)
                {
                    var k = new Vector2Int(x, z);
                    if (live.ContainsKey(k)) continue;
                    var kind = At(x, z, out Vector3 p, out float s, out int variant);
                    if (kind == Kind.None) { live[k] = null; continue; }
                    live[k] = Place(kind, variant, p, s, x, z);
                }
        }

        GameObject Place(Kind kind, int variant, Vector3 p, float s, int cx, int cz)
        {
            var kinds = kind == Kind.Tree ? treeKinds : rockKinds;
            int idx = kinds.Count > 0 ? variant % kinds.Count : -1;
            int key = (kind == Kind.Tree ? 1000 : 2000) + idx;
            GameObject go;
            if (spare.TryGetValue(key, out var q) && q.Count > 0) go = q.Dequeue();
            else go = Make(kind, idx, key);
            go.SetActive(true);
            float yaw = Slope.Hash01(cx, cz, 5) * 360f;
            // sink the base a little so it never floats on a slope
            go.transform.SetPositionAndRotation(p - Vector3.up * (kind == Kind.Rock ? 0.35f * s : 0.25f),
                Quaternion.Euler(0f, yaw, 0f) * (idx >= 0 ? kinds[idx].transform.localRotation : Quaternion.identity));
            float target = kind == Kind.Tree ? 8.5f * s : 1.3f * s;   // metres tall
            float h = idx >= 0 ? (kind == Kind.Tree ? treeH[idx] : rockH[idx]) : (kind == Kind.Tree ? 6.3f : 1f);
            float k = target / Mathf.Max(0.01f, h);
            go.transform.localScale = new Vector3(k, k * (0.92f + 0.2f * Slope.Hash01(cx, cz, 6)), k);
            return go;
        }

        readonly Dictionary<GameObject, int> keyOf = new Dictionary<GameObject, int>();

        GameObject Make(Kind kind, int idx, int key)
        {
            GameObject go;
            if (idx >= 0)
            {
                go = Instantiate(kind == Kind.Tree ? treeKinds[idx] : rockKinds[idx], transform);
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Kill(col);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
            }
            else
            {
                go = new GameObject(kind == Kind.Tree ? "Tree" : "Rock");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = kind == Kind.Tree ? FirMesh() : RockMesh();
                go.AddComponent<MeshRenderer>().sharedMaterials = kind == Kind.Tree ? new[] { foliage, bark } : new[] { bark };
            }
            keyOf[go] = key;
            go.AddComponent<Obstacle>();
            // the trunk (or the rock) is what you hit; branches are soft
            float h = idx >= 0 ? (kind == Kind.Tree ? treeH[idx] : rockH[idx]) : (kind == Kind.Tree ? 6.3f : 1f);
            if (kind == Kind.Tree)
            {
                var cap = go.AddComponent<CapsuleCollider>();
                cap.direction = 1;
                cap.height = h * 0.9f;
                cap.center = new Vector3(0f, h * 0.45f, 0f);
                cap.radius = h * 0.045f;   // about 0.4 m on a full-size tree
            }
            else
            {
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = h * 0.55f;
                sc.center = new Vector3(0f, h * 0.35f, 0f);
            }
            return go;
        }

        static void Kill(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        // ---- fallbacks, used only when there are no models ----

        static Mesh fir, rock;

        static Mesh FirMesh()
        {
            if (fir != null) return fir;
            var v = new List<Vector3>(); var leaf = new List<int>(); var wood = new List<int>();
            Cone(v, wood, 0.0f, 1.7f, 0.28f, 7);
            Cone(v, leaf, 1.1f, 3.3f, 2.0f, 9);
            Cone(v, leaf, 2.5f, 2.9f, 1.55f, 9);
            Cone(v, leaf, 3.8f, 2.5f, 1.05f, 8);
            fir = new Mesh { name = "Fir" };
            fir.SetVertices(v);
            fir.subMeshCount = 2;
            fir.SetTriangles(leaf, 0);
            fir.SetTriangles(wood, 1);
            fir.RecalculateNormals();
            return fir;
        }

        static Mesh RockMesh()
        {
            if (rock != null) return rock;
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock = Instantiate(g.GetComponent<MeshFilter>().sharedMesh);
            Kill(g);
            var v = rock.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = new Vector3(v[i].x, (v[i].y + 0.5f) * 0.7f, v[i].z) * (0.85f + 0.3f * Mathf.PerlinNoise(v[i].x * 3f + 5f, v[i].z * 3f));
            rock.vertices = v;
            rock.RecalculateNormals();
            rock.RecalculateBounds();
            return rock;
        }

        static void Cone(List<Vector3> v, List<int> t, float y0, float h, float r, int n)
        {
            Vector3 apex = new Vector3(0f, y0 + h, 0f), centre = new Vector3(0f, y0, 0f);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                var b0 = new Vector3(Mathf.Cos(a0) * r, y0, Mathf.Sin(a0) * r);
                var b1 = new Vector3(Mathf.Cos(a1) * r, y0, Mathf.Sin(a1) * r);
                int s = v.Count;
                v.Add(b0); v.Add(apex); v.Add(b1);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                s = v.Count;
                v.Add(centre); v.Add(b0); v.Add(b1);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
            }
        }
    }
}
