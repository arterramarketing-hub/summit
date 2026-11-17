using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// A flag planted in the snow wherever you came to rest after a fall: a thin pole in a little heap of
    /// snow, with a pennant numbered by the fall (1, 2, 3) streaming and flapping in the wind. They stay
    /// for the rest of the run, so the mountain keeps a record of where it got you, and go on a restart.
    /// </summary>
    public class FallFlags : MonoBehaviour
    {
        class Flag { public int number; public Transform root; public Mesh mesh; public float phase; }

        readonly List<Flag> flags = new List<Flag>();
        readonly Dictionary<int, Material> pennants = new Dictionary<int, Material>();
        Material poleMat, moundMat;
        Shader lit;
        float clock;

        const int Cols = 9;                       // columns of vertices along the pennant
        const float PoleH = 1.35f, Len = 0.5f, Hgt = 0.3f;
        // fall 1, 2, 3: hotter colours as the run gets closer to its end
        static readonly Color[] Colours = { new Color(1f, 0.80f, 0.10f), new Color(1f, 0.45f, 0.08f), new Color(0.92f, 0.12f, 0.16f), new Color(0.65f, 0.25f, 0.85f) };

        public void Build(Shader litShader)
        {
            lit = litShader != null ? litShader : Shader.Find("Universal Render Pipeline/Lit");
            poleMat = new Material(lit); poleMat.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.2f)); poleMat.SetFloat("_Smoothness", 0.55f); poleMat.SetFloat("_Metallic", 0.6f);
            moundMat = new Material(lit); moundMat.SetColor("_BaseColor", new Color(0.93f, 0.95f, 0.99f)); moundMat.SetFloat("_Smoothness", 0.1f);
        }

        public int Count => flags.Count;

        /// <summary>Plant fall number n's flag where the body came to rest (once per fall).</summary>
        public void Plant(Vector3 at, int n)
        {
            if (lit == null || n <= 0) return;
            foreach (var f in flags) if (f.number == n) return;
            // on the snow, or on the bottom of the crevasse the body fell into
            var ground = new Vector3(at.x, Slope.DrawnHeight(at.x, at.z) - Course.CrevasseCut(at.x, at.z), at.z);
            var root = new GameObject("Fall flag " + n).transform;
            root.SetParent(transform, false);
            root.position = ground;

            var pole = Prim(PrimitiveType.Cylinder, root, poleMat);
            pole.localPosition = new Vector3(0f, PoleH * 0.5f - 0.1f, 0f);
            pole.localScale = new Vector3(0.025f, PoleH * 0.5f, 0.025f);
            var knob = Prim(PrimitiveType.Sphere, root, poleMat);
            knob.localPosition = new Vector3(0f, PoleH - 0.08f, 0f);
            knob.localScale = Vector3.one * 0.05f;
            // the snow heaped round the foot of the pole where it was jammed in
            var mound = Prim(PrimitiveType.Sphere, root, moundMat);
            mound.localPosition = Vector3.down * 0.025f;
            mound.localRotation = Quaternion.FromToRotation(Vector3.up, Slope.Normal(at.x, at.z));
            mound.localScale = new Vector3(0.3f, 0.1f, 0.26f);

            var cloth = new GameObject("Pennant", typeof(MeshFilter), typeof(MeshRenderer));
            cloth.transform.SetParent(root, false);
            cloth.transform.localPosition = new Vector3(0f, PoleH - 0.12f, 0f);
            var mesh = new Mesh { name = "Pennant" };
            mesh.MarkDynamic();
            cloth.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = cloth.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Pennant(n);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var f2 = new Flag { number = n, root = root, mesh = mesh, phase = Random.value * 10f };
            BuildPennant(f2);
            flags.Add(f2);
            Shape(f2, 0f);
        }

        public void Clear()
        {
            foreach (var f in flags)
                if (f.root != null)
                {
                    if (Application.isPlaying) { Destroy(f.root.gameObject); Destroy(f.mesh); }
                    else { DestroyImmediate(f.root.gameObject); DestroyImmediate(f.mesh); }
                }
            flags.Clear();
        }

        void Update() { Step(Time.deltaTime); }

        /// <summary>Flap the pennants (public so the test harness can drive it).</summary>
        public void Step(float dt)
        {
            clock += dt;
            foreach (var f in flags) Shape(f, clock);
        }

        Transform Prim(PrimitiveType type, Transform parent, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            DestroyImmediate(g.GetComponent<Collider>());   // a marker, not something to crash into
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        // ---- the pennant: a triangle of cloth, both sides, rippling down its length

        Vector3[] verts = new Vector3[Cols * 4];

        void BuildPennant(Flag f)
        {
            // front: top and bottom edge at each column; back: the same, wound the other way
            var uv = new Vector2[Cols * 4];
            var tris = new List<int>();
            for (int i = 0; i < Cols; i++)
            {
                float t = i / (Cols - 1f);
                // the front reads the left half of the texture with the pole on its left; the back reads the
                // right half, mirrored, so the number is the right way round from either side
                // (the cloth tapers, and the texture with it, so the number is not squashed)
                float top = 0.5f + 0.5f * (1f - t), bot = 0.5f - 0.5f * (1f - t);
                uv[i * 2] = new Vector2(0.5f * t, top); uv[i * 2 + 1] = new Vector2(0.5f * t, bot);
                uv[Cols * 2 + i * 2] = new Vector2(1f - 0.5f * t, top); uv[Cols * 2 + i * 2 + 1] = new Vector2(1f - 0.5f * t, bot);
                if (i == Cols - 1) continue;
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
                int o = Cols * 2;
                tris.Add(o + a); tris.Add(o + b); tris.Add(o + c); tris.Add(o + b); tris.Add(o + d); tris.Add(o + c);
            }
            f.mesh.vertices = verts;
            f.mesh.uv = uv;
            f.mesh.SetTriangles(tris, 0);
        }

        void Shape(Flag f, float time)
        {
            // the wind swings slowly about a steady direction across the hill, and gusts
            float yaw = 65f + 25f * Mathf.Sin(time * 0.23f + f.phase) + 10f * Mathf.Sin(time * 0.71f + f.phase * 2f);
            float gust = 0.75f + 0.25f * Mathf.Sin(time * 0.9f + f.phase * 3f);
            f.root.rotation = Quaternion.Euler(0f, yaw, 0f);
            float w = time * (9f + 4f * gust) + f.phase;
            for (int i = 0; i < Cols; i++)
            {
                float t = i / (Cols - 1f);
                float x = Len * t * (0.96f + 0.04f * gust);
                // a ripple running out to the tip, growing along the free end; the tip droops in a lull
                float z = (0.03f + 0.06f * t) * t * Mathf.Sin(w - t * 7f) + 0.015f * t * Mathf.Sin(w * 1.9f - t * 11f);
                float droop = (1f - gust) * 0.12f * t * t;
                float half = Hgt * 0.5f * (1f - t);   // tapers to a point
                verts[i * 2] = new Vector3(x, -Hgt * 0.5f + half - droop, z);
                verts[i * 2 + 1] = new Vector3(x, -Hgt * 0.5f - half - droop, z);
                verts[Cols * 2 + i * 2] = verts[i * 2];
                verts[Cols * 2 + i * 2 + 1] = verts[i * 2 + 1];
            }
            f.mesh.vertices = verts;
            f.mesh.RecalculateNormals();
            f.mesh.RecalculateBounds();
        }

        // ---- the number, painted on in a blocky 3x5 font

        static readonly string[] Glyphs =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111",
        };

        Material Pennant(int n)
        {
            if (pennants.TryGetValue(n, out var m)) return m;
            const int W = 256, H = 80, S = 9;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Pennant" + n };
            Color bg = Colours[Mathf.Clamp(n - 1, 0, Colours.Length - 1)], ink = bg.grayscale < 0.4f ? new Color(0.98f, 0.97f, 0.94f) : new Color(0.08f, 0.08f, 0.12f);   // white on the dark ones
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = bg;
            string g = Glyphs[n % 10];
            void Digit(int left)
            {
                int bottom = (H - 5 * S) / 2;
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 3; gx++)
                        if (g[gy * 3 + gx] == '1')
                            for (int yy = 0; yy < S; yy++)
                                for (int xx = 0; xx < S; xx++)
                                    px[(bottom + (4 - gy) * S + yy) * W + left + gx * S + xx] = ink;
            }
            Digit(12);              // front, near the pole
            Digit(W - 12 - 3 * S);  // back, near the pole as seen from behind
            tex.SetPixels(px);
            tex.Apply(true);
            m = new Material(lit);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.3f);
            pennants[n] = m;
            return m;
        }
    }
}
