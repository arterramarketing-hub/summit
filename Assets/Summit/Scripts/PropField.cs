using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Trees on a fixed grid of cells. Whether a cell holds a tree is a pure
    /// function of the cell, so collision never depends on what is drawn, and
    /// the forest is the same for everyone on the same day.
    /// </summary>
    public class PropField : MonoBehaviour
    {
        public const float Cell = 9f;
        public Transform target;
        public Material foliage, bark;
        public int sideCells = 10, aheadCells = 30, behindCells = 3;

        Mesh treeMesh;
        readonly Dictionary<Vector2Int, GameObject> live = new Dictionary<Vector2Int, GameObject>();
        readonly Queue<GameObject> spare = new Queue<GameObject>();
        readonly List<Vector2Int> drop = new List<Vector2Int>();
        Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);

        public static bool TreeAt(int cx, int cz, out Vector3 pos, out float scale)
        {
            pos = Vector3.zero;
            scale = 1f;
            float wx = (cx + 0.5f) * Cell, wz = (cz + 0.5f) * Cell;
            float off = Mathf.Abs(wx - MountainField.DriftX(wz));
            // sparse on the good line, a forest off it, with clearings
            float density = Mathf.Clamp01((off - 14f) / 45f) * 0.75f + 0.07f;
            density *= Mathf.Lerp(0.35f, 1.25f, MountainField.Noise(wx * 0.012f, wz * 0.012f));
            if (wz < 60f && off < 30f) density *= 0.15f;   // a clean start
            if (MountainField.Hash01(cx, cz, 1) >= density) return false;
            float x = wx + (MountainField.Hash01(cx, cz, 2) - 0.5f) * Cell * 0.7f;
            float z = wz + (MountainField.Hash01(cx, cz, 3) - 0.5f) * Cell * 0.7f;
            pos = new Vector3(x, MountainField.DrawnHeight(x, z) - 0.15f, z);
            scale = 0.8f + 0.65f * MountainField.Hash01(cx, cz, 4);
            return true;
        }

        public static int Id(int cx, int cz) { return cx * 73856093 ^ cz * 19349663; }

        /// <summary>Distance from (x, z) to the nearest trunk, in multiples of that tree's size.</summary>
        public static float Nearest(float x, float z, out int id, out float scale)
        {
            int cx = Mathf.FloorToInt(x / Cell), cz = Mathf.FloorToInt(z / Cell);
            float best = float.MaxValue;
            id = 0;
            scale = 1f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!TreeAt(cx + dx, cz + dz, out Vector3 p, out float s)) continue;
                    float d = new Vector2(p.x - x, p.z - z).magnitude;
                    if (d < best) { best = d; id = Id(cx + dx, cz + dz); scale = s; }
                }
            return best;
        }

        void Awake() { treeMesh = BuildTreeMesh(); }
        void Update() { Refresh(false); }
        public void Prewarm() { Refresh(true); }

        void Refresh(bool force)
        {
            if (target == null) return;
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
                spare.Enqueue(go);
            }

            for (int z = c.y - behindCells; z <= c.y + aheadCells; z++)
                for (int x = c.x - sideCells; x <= c.x + sideCells; x++)
                {
                    var k = new Vector2Int(x, z);
                    if (live.ContainsKey(k)) continue;
                    if (!TreeAt(x, z, out Vector3 p, out float s)) { live[k] = null; continue; }
                    var go = spare.Count > 0 ? spare.Dequeue() : NewTree();
                    go.SetActive(true);
                    go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, MountainField.Hash01(x, z, 5) * 360f, 0f));
                    go.transform.localScale = new Vector3(s, s * (0.9f + 0.25f * MountainField.Hash01(x, z, 6)), s);
                    live[k] = go;
                }
        }

        GameObject NewTree()
        {
            var go = new GameObject("Tree");
            go.transform.SetParent(transform, false);
            if (treeMesh == null) treeMesh = BuildTreeMesh();
            go.AddComponent<MeshFilter>().sharedMesh = treeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { foliage, bark };
            return go;
        }

        // ---- a low-poly fir: three stacked cones on a trunk, flat shaded ----

        static Mesh BuildTreeMesh()
        {
            var v = new List<Vector3>();
            var leaf = new List<int>();
            var wood = new List<int>();
            Cone(v, wood, 0.0f, 1.7f, 0.28f, 7);
            Cone(v, leaf, 1.1f, 3.3f, 2.0f, 9);
            Cone(v, leaf, 2.5f, 2.9f, 1.55f, 9);
            Cone(v, leaf, 3.8f, 2.5f, 1.05f, 8);
            var m = new Mesh { name = "Fir" };
            m.SetVertices(v);
            m.subMeshCount = 2;
            m.SetTriangles(leaf, 0);
            m.SetTriangles(wood, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void Cone(List<Vector3> v, List<int> t, float y0, float h, float r, int n)
        {
            Vector3 apex = new Vector3(0f, y0 + h, 0f), centre = new Vector3(0f, y0, 0f);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                var b0 = new Vector3(Mathf.Cos(a0) * r, y0, Mathf.Sin(a0) * r);
                var b1 = new Vector3(Mathf.Cos(a1) * r, y0, Mathf.Sin(a1) * r);
                // side, wound clockwise seen from outside
                int s = v.Count;
                v.Add(b0); v.Add(apex); v.Add(b1);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                // underside
                s = v.Count;
                v.Add(centre); v.Add(b0); v.Add(b1);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
            }
        }
    }
}
