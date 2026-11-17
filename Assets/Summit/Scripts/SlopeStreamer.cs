using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Streams the mountain in square chunks around a target, a couple per
    /// frame so building one never costs a dropped frame. Chunks sit on a
    /// fixed world grid, so nothing re-triangulates as the rider moves.
    /// </summary>
    public class SlopeStreamer : MonoBehaviour
    {
        public Transform target;
        public Material snowMaterial;
        public float chunkSize = 48f;
        int quads;
        public int sideChunks = 5;      // either side of the rider (open mountain: you can see across it)
        public int aheadChunks = 12;    // down the hill (~580 m)
        public int behindChunks = 1;
        public int buildsPerFrame = 1;

        readonly Dictionary<Vector2Int, GameObject> live = new Dictionary<Vector2Int, GameObject>();
        readonly Queue<GameObject> spare = new Queue<GameObject>();
        readonly List<Vector2Int> wanted = new List<Vector2Int>();
        readonly List<Vector2Int> drop = new List<Vector2Int>();

        int lastCx = int.MinValue, lastCz = int.MinValue, sortCx, sortCz;
        bool pending;
        System.Comparison<Vector2Int> byDistance;

        void Awake() { Init(); }

        void Init()
        {
            quads = Mathf.RoundToInt(chunkSize / Slope.Quad);
            byDistance = (a, b) =>
                (Mathf.Abs(a.x - sortCx) + Mathf.Abs(a.y - sortCz)).CompareTo(Mathf.Abs(b.x - sortCx) + Mathf.Abs(b.y - sortCz));
        }
        void Start() { Refresh(true); }
        void Update() { Refresh(false); }

        /// <summary>Build everything around the target now (on a respawn or at start).</summary>
        public void Prewarm() { Refresh(true); }

        void Refresh(bool all)
        {
            if (target == null) return;
            if (byDistance == null) Init();
            int cx = Mathf.FloorToInt(target.position.x / chunkSize);
            int cz = Mathf.FloorToInt(target.position.z / chunkSize);
            // nothing to do until the rider crosses into a new cell, or while chunks are still queued
            if (!all && cx == lastCx && cz == lastCz && !pending) return;
            lastCx = cx; lastCz = cz;

            wanted.Clear();
            for (int dz = -behindChunks; dz <= aheadChunks; dz++)
                for (int dx = -sideChunks; dx <= sideChunks; dx++)
                    wanted.Add(new Vector2Int(cx + dx, cz + dz));
            // nearest first
            sortCx = cx; sortCz = cz;
            wanted.Sort(byDistance);

            drop.Clear();
            foreach (var kv in live)
                if (kv.Key.x < cx - sideChunks - 1 || kv.Key.x > cx + sideChunks + 1 ||
                    kv.Key.y < cz - behindChunks - 1 || kv.Key.y > cz + aheadChunks + 1)
                    drop.Add(kv.Key);
            foreach (var k in drop) { live[k].SetActive(false); spare.Enqueue(live[k]); live.Remove(k); }

            // a full rebuild (a new mountain) builds only what is round the rider at once; the far chunks
            // stream in over the next frames, so PLAY AGAIN does not freeze
            int budget = all ? 48 : buildsPerFrame;
            pending = false;
            foreach (var k in wanted)
            {
                if (budget <= 0) { pending = true; break; }
                if (live.ContainsKey(k)) continue;
                live[k] = Build(k);
                budget--;
            }
        }

        GameObject Build(Vector2Int key)
        {
            GameObject go;
            Mesh mesh;
            if (spare.Count > 0)
            {
                go = spare.Dequeue();
                go.SetActive(true);
                mesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
            else
            {
                go = new GameObject("Chunk");
                go.transform.SetParent(transform, false);
                mesh = new Mesh();
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = snowMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
            go.name = "Chunk " + key.x + "," + key.y;
            Fill(mesh, key);
            return go;
        }

        Vector3[] verts, norms;
        Vector2[] uvs;
        Color[] cols;
        int[] tris;
        float[] grid;   // heights with a one-cell border, reused between chunks
        bool[] holeMask;
        readonly List<int> holed = new List<int>();

        /// <summary>A new mountain: throw away every chunk and build again around the target.</summary>
        public void Rebuild()
        {
            foreach (var kv in live) { kv.Value.SetActive(false); spare.Enqueue(kv.Value); }
            live.Clear();
            lastCx = lastCz = int.MinValue;
            Refresh(true);
        }

        void Fill(Mesh mesh, Vector2Int key)
        {
            int n = quads + 1, g = n + 2;
            float q = Slope.Quad;
            float x0 = key.x * chunkSize, z0 = key.y * chunkSize;
            // sample once, with a border, and take normals and shading from the samples:
            // about a ninth of the height calls of sampling each one separately
            if (grid == null || grid.Length != g * g) grid = new float[g * g];
            for (int j = 0; j < g; j++)
                for (int i = 0; i < g; i++)
                    grid[j * g + i] = Slope.Height(x0 + (i - 1) * q, z0 + (j - 1) * q);

            if (verts == null || verts.Length != n * n)
            {
                verts = new Vector3[n * n]; norms = new Vector3[n * n];
                uvs = new Vector2[n * n]; cols = new Color[n * n];
            }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int c = (j + 1) * g + (i + 1);
                    float y = grid[c];
                    float hl = grid[c - 1], hr = grid[c + 1], hd = grid[c - g], hu = grid[c + g];
                    float x = x0 + i * q, z = z0 + j * q;
                    int v = j * n + i;
                    verts[v] = new Vector3(x, y, z);
                    norms[v] = new Vector3(-(hr - hl) / (2f * q), 1f, -(hu - hd) / (2f * q)).normalized;
                    uvs[v] = new Vector2(x / 9f, z / 9f);
                    // hollows go blue, crests catch the light
                    float lap = (hl + hr + hd + hu) * 0.25f - y;
                    float k = Mathf.Clamp(lap * 2.4f, -0.8f, 0.8f);
                    Color c0 = k > 0f ? Color.Lerp(Color.white, new Color(0.72f, 0.80f, 0.95f), k * 0.55f)
                                      : Color.Lerp(Color.white, new Color(1f, 0.98f, 0.94f), -k * 0.4f);
                    // the powder either side is a touch bluer than the groomed piste, so the line reads at speed
                    cols[v] = Color.Lerp(c0, c0 * new Color(0.90f, 0.94f, 1.0f), Slope.Rough(x, z));
                }
            if (tris == null)
            {
                tris = new int[quads * quads * 6];
                int t = 0;
                for (int j = 0; j < quads; j++)
                    for (int i = 0; i < quads; i++)
                    {
                        int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    }
            }
            // where a crevasse opens, leave a hole: its walls and lips are their own meshes (CourseDressing)
            int[] use = tris;
            if (Course.At(z0) == Course.Section.Danger || Course.At(z0 + chunkSize) == Course.Section.Danger)
            {
                if (holeMask == null || holeMask.Length != n * n) holeMask = new bool[n * n];
                bool any = false;
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        bool h = Course.CrevasseCut(x0 + i * q, z0 + j * q) > 0.25f;
                        holeMask[j * n + i] = h; any |= h;
                    }
                if (any)
                {
                    holed.Clear();
                    for (int j = 0; j < quads; j++)
                        for (int i = 0; i < quads; i++)
                        {
                            int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                            if (holeMask[a] || holeMask[b] || holeMask[c] || holeMask[d]) continue;
                            holed.Add(a); holed.Add(c); holed.Add(b); holed.Add(b); holed.Add(c); holed.Add(d);
                        }
                    use = holed.ToArray();
                }
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = use;
            mesh.RecalculateTangents();   // the snow's normal map needs them
            mesh.RecalculateBounds();
        }
    }
}
