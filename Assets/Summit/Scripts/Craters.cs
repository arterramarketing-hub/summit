using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Holes punched in the snow by a body or a hard landing, as real geometry: a packed floor, steep inner
    /// walls and a lumpy rim of thrown-up snow. Never a circle: the outline wobbles, it is stretched along
    /// the line the body was travelling and the rim is piled highest on the side it was heading.
    /// The newest few dozen are kept; the oldest is reused.
    /// </summary>
    public class Craters
    {
        const int Seg = 24, Rings = 6, Max = 64;
        static readonly float[] Frac = { 0f, 0.45f, 0.78f, 0.95f, 1.12f, 1.55f };   // outline fractions, centre to foot
        static readonly float[] U = { 0.5f, 0.45f, 0.36f, 0.26f, 0.2f, 0f };
        readonly Material mat;
        readonly Transform root;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<GameObject> objs = new List<GameObject>();
        int next;
        readonly Vector3[] v = new Vector3[Seg * Rings + 1];
        readonly Vector2[] uv = new Vector2[Seg * Rings + 1];
        readonly int[] tri;
        readonly float[] wob = new float[Seg], lump = new float[Seg], lumpS = new float[Seg];

        public Craters(Shader lit)
        {
            mat = new Material(lit);
            mat.SetTexture("_BaseMap", Strip());
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.12f);
            root = new GameObject("Craters").transform;
            var t = new List<int>();
            // centre fan, then rings
            for (int s = 0; s < Seg; s++) { int a = 1 + s, b = 1 + (s + 1) % Seg; t.Add(0); t.Add(a); t.Add(b); }   // clockwise from above: facing up
            for (int r = 0; r < Rings - 1; r++)
                for (int s = 0; s < Seg; s++)
                {
                    int a = 1 + r * Seg + s, b = 1 + r * Seg + (s + 1) % Seg, c = a + Seg, d = b + Seg;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            tri = t.ToArray();
        }

        /// <summary>Across a crater: snow, bright thrown rim, then a shaded inner wall down to a packed blue-grey floor.</summary>
        static Texture2D Strip()
        {
            const int n = 128;
            var t = new Texture2D(n, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, e = Mathf.Abs(u - 0.5f) * 2f;
                Color floor = new Color(0.58f, 0.65f, 0.80f), wall = new Color(0.74f, 0.80f, 0.91f), rim = new Color(0.97f, 0.98f, 1f), snow = new Color(0.90f, 0.92f, 0.97f);
                Color c = e < 0.2f ? Color.Lerp(floor, wall, e / 0.2f) : e < 0.48f ? Color.Lerp(wall, rim, (e - 0.2f) / 0.28f) : Color.Lerp(rim, snow, (e - 0.48f) / 0.52f);
                t.SetPixel(x, 0, c); t.SetPixel(x, 1, c);
            }
            t.Apply();
            return t;
        }

        public void Clear()
        {
            foreach (var g in objs) g.SetActive(false);
        }

        static float Hash(int i, int s) { uint h = (uint)(i * 374761393 + s * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xffff) / 65535f; }

        /// <summary>
        /// at: on the snow. travel: the way it was moving (zero for none). size: radius in metres.
        /// depth: how high the rim is thrown (the hole reads as that deep).
        /// </summary>
        public void Add(Vector3 at, Vector3 travel, float size, float depth)
        {
            if (Course.CrevasseCut(at.x, at.z) > 0.3f) return;   // nothing to dent over a crevasse
            Mesh mesh; GameObject go;
            if (objs.Count < Max)
            {
                go = new GameObject("Crater");
                go.transform.SetParent(root, false);
                mesh = new Mesh(); mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                objs.Add(go); meshes.Add(mesh);
            }
            else { go = objs[next]; mesh = meshes[next]; next = (next + 1) % Max; }
            go.SetActive(true);

            Vector3 n = Slope.Normal(at.x, at.z);
            Vector3 fwd = Vector3.ProjectOnPlane(travel, n);
            float spd = fwd.magnitude;
            fwd = spd > 0.3f ? fwd / spd : Vector3.ProjectOnPlane(Random.onUnitSphere, n).normalized;
            Vector3 side = Vector3.Cross(n, fwd);
            float stretch = 1f + Mathf.Clamp01(spd / 10f) * 0.8f;     // a skid of a crater when it was moving fast
            int seed = Random.Range(0, 100000);
            // a wobbly outline: a few lobes plus finer bumps
            var wob = this.wob; var lump = this.lump;
            float ph1 = Hash(seed, 1) * 6.28f, ph2 = Hash(seed, 2) * 6.28f;
            for (int s = 0; s < Seg; s++)
            {
                float a = s / (float)Seg * Mathf.PI * 2f;
                wob[s] = 1f + 0.16f * Mathf.Sin(a * 2f + ph1) + 0.1f * Mathf.Sin(a * 3f + ph2) + 0.12f * (Hash(seed + s, 3) - 0.5f);
                lump[s] = 0.6f + 0.8f * Hash(seed + s, 4);
            }
            // smooth the lumps a little so the rim is clods, not spikes
            var lumpS = this.lumpS;
            for (int s = 0; s < Seg; s++) lumpS[s] = (lump[(s + Seg - 1) % Seg] + 2f * lump[s] + lump[(s + 1) % Seg]) * 0.25f;

            v[0] = Lift(at, n, 0.012f);
            uv[0] = new Vector2(U[0], 0.25f);
            for (int r = 0; r < Rings; r++)
                for (int s = 0; s < Seg; s++)
                {
                    float a = s / (float)Seg * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    // stretched along travel, the hole's centre a touch behind (snow shoved forward)
                    Vector3 dir = fwd * (ca * stretch) + side * sa;
                    float rad = size * wob[s] * Frac[r];
                    Vector3 p = at + dir * rad - fwd * size * 0.15f * (1f - Frac[r] / 1.55f);
                    float ahead = Mathf.Max(0f, ca);                          // the front piles up
                    float h = r switch
                    {
                        0 => 0.015f,
                        1 => depth * 0.35f,
                        2 => depth * 0.8f * lumpS[s],
                        3 => depth * (0.95f + 0.55f * ahead) * lumpS[s],
                        4 => depth * (0.45f + 0.3f * ahead) * lumpS[(s + 5) % Seg],
                        _ => 0.006f,
                    };
                    int i = 1 + r * Seg + s;
                    v[i] = Lift(p, n, h);
                    uv[i] = new Vector2(U[r] + (r >= 3 ? 0.03f * (lumpS[s] - 1f) : 0f), 0.25f);
                }
            mesh.Clear();
            mesh.vertices = v; mesh.uv = uv; mesh.triangles = tri;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }

        static Vector3 Lift(Vector3 p, Vector3 n, float h)
        {
            p.y = Slope.DrawnHeight(p.x, p.z) + 0.01f;
            return p + n * h;
        }
    }
}
