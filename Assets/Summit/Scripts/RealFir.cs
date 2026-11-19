using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The realistic forest's trees: snowy firs and hemlocks, pines, dead snags, young trees, old giants and
    /// saplings, all made of photo-scanned branch cards. The meshes are built ahead of time by Tools/MakeTrees.py
    /// (so they can be looked at in Blender) and read here from Resources/RealTrees/Trees.bytes. Two atlases: the
    /// firs' (FirCards.png, Tools/MakeFirCards.swift, Poly Haven's CC0 "Fir Tree 01") and the pines' and snags'
    /// (PineCards.png, Tools/MakeTrees.py, Poly Haven's CC0 "Pine Tree 01").
    ///
    /// Every kind comes at three levels of detail (Forest.cs draws them and dissolves one into the next):
    ///  - rich, right beside the rider: bent branches with side fronds and a full inner crown, a trunk with a root
    ///    flare, a pine's pads of needles built up from several cards (1,000 to 3,000 triangles);
    ///  - plain, in the middle distance: two cards a branch (250 to 550 triangles), and the tree's shadow;
    ///  - far away an impostor: a picture of the plain tree, taken once at start-up, on two crossed quads. The
    ///    picture keeps the tree's plain colour and its normals, and all three are lit by the same sums
    ///    (Shaders/Fir.hlsl), so they match in every time of day.
    /// Normals shade a crown (or a pine's pad) as one rounded mass, and the vertex colour darkens its inside.
    ///
    /// Kinds 0-7 are the firs and hemlocks the forest has always had, with the same measurements for their hit
    /// capsules (Kinds below: the numbers are also in Tools/MakeTrees.py - change both or neither). They are the only
    /// kinds that grow on and beside the piste (Forest.cs), so the obstacles there are what they always were.
    /// </summary>
    public static class RealFir
    {
        /// <summary>How many kinds of tree there are (saplings included).</summary>
        public static int Variants { get; private set; }
        public static Mesh[] Meshes;         // rich: up close, and trees planted on their own
        public static Mesh[] Plain;          // the middle distance
        public static Mesh[] Impostors;      // far away (null for saplings: they just fade out)
        public static float[] Heights;       // each kind's height as built, metres
        public static Material[] ImpostorMats;
        static int[] atlas;                  // 0 the firs' atlas, 1 the pines'
        static int[] flags;
        static float[] size;                 // how tall it grows beside a full-grown tree of its stand
        static float[][] trunkTable, crownTable;
        static Material[] plainMats, richMats, decoMats, standaloneMats;
        static RenderTexture[] shots, normalShots;

        const int FlagSnag = 1, FlagYoung = 2, FlagSapling = 4, FlagPine = 8, FlagGiant = 16;   // as Tools/MakeTrees.py writes them
        const int Table = 17;                // measurements at 17 heights, foot to top

        public static bool Ready => Meshes != null && standaloneMats != null;

        /// <summary>The firs' material for things drawn on their own (TreeBreak's stumps wear its bark).</summary>
        public static Material Standalone => standaloneMats != null ? standaloneMats[0] : null;
        /// <summary>The material a tree of this kind wears when it stands on its own (the camp, a felled tree): never fades.</summary>
        public static Material StandaloneOf(int variant) => standaloneMats[atlas[variant]];
        /// <summary>The forest's materials for this kind: up close (dissolves away past the near band) and in the
        /// middle distance (dissolves in over the near band, and out into the far picture).</summary>
        public static Material RichOf(int variant) => richMats[atlas[variant]];
        public static Material PlainOf(int variant) => IsSapling(variant) ? decoMats[atlas[variant]] : plainMats[atlas[variant]];

        /// <summary>A dead snag: dry and brittle, it snaps easily (TreeBreak). Kind 6 is the fir whose top has gone.</summary>
        public static bool IsSnag(int variant) => variant == Snag || (flags[variant] & FlagSnag) != 0;
        public const int Snag = 6;
        public static bool IsYoung(int variant) => (flags[variant] & FlagYoung) != 0;
        public static bool IsPine(int variant) => (flags[variant] & FlagPine) != 0;
        /// <summary>A sapling poking out of the snow: decoration, with nothing to hit.</summary>
        public static bool IsSapling(int variant) => (flags[variant] & FlagSapling) != 0;
        /// <summary>How tall this kind grows as a share of a full-grown tree of its stand (young 0.4, giants 1.4).</summary>
        public static float SizeOf(int variant) => size[variant];

        /// <summary>Reads the trees and takes their impostor pictures (once).</summary>
        public static bool Load()
        {
            if (Ready) return true;
            var data = Resources.Load<TextAsset>("RealTrees/Trees");
            var fir = Resources.Load<Material>("RealTrees/FirCards");
            var pine = Resources.Load<Material>("RealTrees/PineCards");
            var imp = Resources.Load<Material>("RealTrees/FirImpostor");
            if (data == null || fir == null || pine == null || imp == null) return false;
            if (!Read(data.bytes)) { Meshes = null; return false; }
            Material Make(Material from, float fadeIn, float fadeOut)
            {
                var m = new Material(from) { enableInstancing = true };
                m.SetFloat("_FadeIn", fadeIn); m.SetFloat("_FadeOut", fadeOut);
                return m;
            }
            var src = new[] { fir, pine };
            plainMats = new Material[2]; richMats = new Material[2]; decoMats = new Material[2]; var alone = new Material[2];
            for (int a = 0; a < 2; a++)
            {
                plainMats[a] = Make(src[a], 1f, 1f);   // appears over the near band, hands over to the far picture
                richMats[a] = Make(src[a], 0f, 2f);    // dissolves away over the near band
                decoMats[a] = Make(src[a], 0f, 1f);    // saplings: there from close up, gone by the far band's end
                alone[a] = Make(src[a], 0f, 0f);
            }
            standaloneMats = alone;
            BakeImpostors(imp);
            return true;
        }

        /// <summary>
        /// A realistic tree standing on its own, outside the forest (the camp, the trees the start tape is tied to):
        /// `height` metres tall from `at`, turned by `yaw` degrees. Null if the trees could not be loaded.
        /// For one grown and buried like the forest's, see Forest.PlantLike.
        /// </summary>
        public static GameObject Plant(Transform parent, Vector3 at, float height, float yaw, int variant, string name = "RealFir")
        {
            if (!Load()) return null;
            int v = ((variant % Variants) + Variants) % Variants;
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            g.transform.localScale = Vector3.one * (height / Heights[v]);
            g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            g.GetComponent<MeshFilter>().sharedMesh = Meshes[v];
            g.GetComponent<MeshRenderer>().sharedMaterial = StandaloneOf(v);
            return g;
        }

        // ---- reading Trees.bytes (Tools/MakeTrees.py: write_trees)

        static bool Read(byte[] bytes)
        {
            try
            {
                using (var r = new BinaryReader(new MemoryStream(bytes)))
                {
                    if (r.ReadByte() != 'S' || r.ReadByte() != 'M' || r.ReadByte() != 'T' || r.ReadByte() != 'R') return false;
                    if (r.ReadUInt32() != 1) return false;
                    int n = (int)r.ReadUInt32();
                    if (n < Legacy || n > 200) return false;
                    Meshes = new Mesh[n]; Plain = new Mesh[n]; Heights = new float[n];
                    atlas = new int[n]; flags = new int[n]; size = new float[n];
                    trunkTable = new float[n][]; crownTable = new float[n][];
                    for (int i = 0; i < n; i++)
                    {
                        string name = System.Text.Encoding.ASCII.GetString(r.ReadBytes(r.ReadByte()));
                        Heights[i] = r.ReadSingle();
                        atlas[i] = Mathf.Clamp(r.ReadByte(), 0, 1);
                        flags[i] = r.ReadByte();
                        size[i] = r.ReadSingle();
                        trunkTable[i] = new float[Table]; crownTable[i] = new float[Table];
                        for (int k = 0; k < Table; k++) trunkTable[i][k] = r.ReadSingle();
                        for (int k = 0; k < Table; k++) crownTable[i][k] = r.ReadSingle();
                        Meshes[i] = ReadMesh(r, name);
                        Plain[i] = ReadMesh(r, name + "Plain");
                    }
                    Variants = n;
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("RealFir: could not read RealTrees/Trees.bytes: " + e.Message);
                return false;
            }
        }

        static Mesh ReadMesh(BinaryReader r, string name)
        {
            int vn = (int)r.ReadUInt32(), tn = (int)r.ReadUInt32();
            var v = new Vector3[vn]; var nrm = new Vector3[vn]; var col = new Color32[vn]; var uv = new Vector2[vn];
            for (int i = 0; i < vn; i++)
            {
                v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                float nx = r.ReadSByte() / 127f, ny = r.ReadSByte() / 127f, nz = r.ReadSByte() / 127f;
                nrm[i] = new Vector3(nx, ny, nz);
                byte shade = r.ReadByte();   // how much light reaches this part of the crown (FirCards multiplies it in)
                col[i] = new Color32(shade, shade, shade, 255);
                uv[i] = new Vector2(r.ReadUInt16() / 65535f, r.ReadUInt16() / 65535f);
            }
            var t = new int[tn];
            for (int i = 0; i < tn; i++) t[i] = r.ReadUInt16();
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(nrm); m.SetColors(col); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m;
        }

        // ---- the firs and hemlocks the forest has always had (kinds 0-7): what their hit capsules are sized from

        /// <summary>One of the original kinds, at its real height.</summary>
        struct Species
        {
            public float H;            // height, metres
            public float reach;        // longest branch (at the crown's foot) as a share of the height
            public float crownBase;    // where the branches start, share of the height
            public float broken;       // 0: a whole crown; else where the top snapped off (share of the height), above it a bare spike
            public float shape;        // how the branches shorten up the crown: 1 a straight cone, less a rounder, stubbier crown
        }

        const int Legacy = 8;
        static readonly Species[] Kinds =
        {
            // subalpine firs: tall, narrow spires, branches nearly level, snow along them
            new Species { H = 24f, reach = 0.12f, crownBase = 0.12f },
            new Species { H = 27f, reach = 0.10f, crownBase = 0.16f },
            // the same firs plastered: high up, where the storms load every branch
            new Species { H = 20f, reach = 0.17f, crownBase = 0.08f, shape = 0.55f },
            new Species { H = 18f, reach = 0.19f, crownBase = 0.06f, shape = 0.5f },
            // mountain hemlocks: drooping branches and a nodding top, ragged, lopsided
            new Species { H = 24f, reach = 0.15f, crownBase = 0.14f },
            new Species { H = 29f, reach = 0.13f, crownBase = 0.18f },
            // a fir whose top has snapped off: a bare spike above the last branches
            new Species { H = 25f, reach = 0.12f, crownBase = 0.15f, broken = 0.74f },
            // a thin, ragged hemlock with gaps in its crown
            new Species { H = 22f, reach = 0.17f, crownBase = 0.2f },
        };

        /// <summary>Which of the original kinds grows here: plastered firs high up; lower down the spires, hemlocks and the odd snag.</summary>
        /// <param name="low">0 at the summit .. 1 at the foot of the mountain</param>
        /// <param name="pick">0..1, the tree's own</param>
        public static int Pick(float low, float pick)
        {
            // weights for each kind, by height on the mountain
            float plastered = Mathf.Lerp(1f, 0.05f, Mathf.Clamp01(low * 1.6f));
            float fir = Mathf.Lerp(0.3f, 0.9f, Mathf.Clamp01(low * 1.5f)), hemlock = Mathf.Lerp(0.15f, 1f, low), snag = Mathf.Lerp(0.02f, 0.12f, low);
            float[] w = { fir, fir, plastered, plastered, hemlock, hemlock, snag, hemlock * 0.5f };
            float sum = 0f; foreach (var x in w) sum += x;
            float r = pick * sum;
            for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0f) return i; }
            return 0;
        }

        /// <summary>
        /// Which kind grows at a spot away from the piste, where the forest stands in patches: `pines` is how much of
        /// a pine stand this is (0 firs and hemlocks .. 1 pines), `young` and `old` the shares of young trees and old
        /// giants here, `dead` the share of snags. r1..r4 (0..1) are the tree's own.
        /// </summary>
        public static int PickWild(float low, float pines, float young, float old, float dead, float r1, float r2, float r3, float r4)
        {
            bool pine = r2 < pines;
            if (r3 < dead) { int snag = First(FlagSnag, 0, r1); if (snag >= 0) return snag; }
            int age = r4 < young ? FlagYoung : r4 > 1f - old ? FlagGiant : 0;
            if (!pine && age == 0) return Pick(low, r1);
            if (pine && age == 0)
            {
                // the mature pines: the heavily snowed one more often high on the mountain
                int a = First(FlagPine, FlagYoung | FlagGiant | FlagSapling, 0f), b = First(FlagPine, FlagYoung | FlagGiant | FlagSapling, 0.999f);
                if (a < 0) return Pick(low, r1);
                return r1 < Mathf.Lerp(0.7f, 0.25f, low) ? b : a;
            }
            int found = First(age | (pine ? FlagPine : 0), FlagSapling | (pine ? 0 : FlagPine), r1);
            return found >= Legacy ? found : Pick(low, r1);
        }

        /// <summary>A sapling for a clearing: a pine's or a fir's.</summary>
        public static int PickSapling(bool pine, float r) => First(FlagSapling | (pine ? FlagPine : 0), pine ? 0 : FlagPine, r);

        /// <summary>One of the new kinds (8 on) that has all of `with` and none of `without`, chosen by r (0..1); -1 if there is none.</summary>
        static int First(int with, int without, float r)
        {
            int count = 0;
            for (int i = Legacy; i < Variants; i++) if ((flags[i] & with) == with && (flags[i] & without) == 0) count++;
            if (count == 0) return -1;
            int want = Mathf.Min(count - 1, (int)(Mathf.Clamp01(r) * count));
            for (int i = Legacy; i < Variants; i++)
                if ((flags[i] & with) == with && (flags[i] & without) == 0 && want-- == 0) return i;
            return -1;
        }

        static float Lookup(float[] table, float at)
        {
            float f = Mathf.Clamp01(at) * (Table - 1);
            int i = Mathf.Min(Table - 2, (int)f);
            return Mathf.Lerp(table[i], table[i + 1], f - i);
        }

        /// <summary>The trunk's radius at this height (metres, model space: the tree at its kind's own height).</summary>
        public static float TrunkRadius(int variant, float y)
        {
            int v = variant % Variants;
            if (v >= Legacy) return Lookup(trunkTable[v], y / Heights[v]);
            var sp = Kinds[v];
            float H = sp.H, y0 = -0.3f, y1 = H * (sp.broken > 0f ? sp.broken + 0.1f : 0.8f);
            float r0 = H * 0.02f, r1 = sp.broken > 0f ? H * 0.006f : H * 0.004f;
            return Mathf.Lerp(r0, r1, Mathf.InverseLerp(y0, y1, y));
        }

        /// <summary>A tree of this variant's crown radius at a share of its height (for hit capsules).</summary>
        public static float CrownRadius(int variant, float at)
        {
            int v = variant % Variants;
            if (v >= Legacy) return Lookup(crownTable[v], at);
            var k = Kinds[v];
            if (at < k.crownBase || at > (k.broken > 0f ? k.broken : 0.95f)) return 0f;
            float f = Mathf.InverseLerp(k.crownBase, 0.92f, at);
            return k.reach * Mathf.Pow(1f - f, k.shape > 0f ? k.shape : 0.95f) + 0.35f / k.H;   // as a share of the height
        }

        /// <summary>
        /// The radius of the hit capsule of a tree grown `full` metres tall with `sink` metres of it under the snow.
        /// The original kinds: the trunk, or most of the crown where the snow line crosses it, as it always was.
        /// The new kinds: the trunk where it leaves the snow, or what their branches reach within a rider's height
        /// of the snow - most of a fir's crown, but only the inner third of a pine's open one (its limbs and the
        /// middle of its pads: the outer twigs brush past).
        /// </summary>
        public static float HitRadius(int variant, float full, float sink)
        {
            int v = variant % Variants;
            float at = sink / full;
            if (v < Legacy) return Mathf.Max(0.02f * full, 0.7f * CrownRadius(v, at) * full);
            float crown = 0f, top = Mathf.Min(1f, at + 2.5f / full);
            for (int i = 0; i <= 4; i++) crown = Mathf.Max(crown, CrownRadius(v, Mathf.Lerp(at, top, i / 4f)));
            float trunk = TrunkRadius(v, at * Heights[v]) * full / Heights[v];
            return Mathf.Max(Mathf.Max(0.25f, trunk), (IsPine(v) ? 0.3f : 0.7f) * crown * full);
        }

        // ---- impostors: a picture of each tree, side on, on two crossed quads

        const int ShotW = 256, ShotH = 512;

        static void BakeImpostors(Material impostorTemplate)
        {
            Impostors = new Mesh[Variants]; ImpostorMats = new Material[Variants];
            shots = new RenderTexture[Variants]; normalShots = new RenderTexture[Variants];
            // unlit: the plain colour, then the normals (FirCards' _Bake), lit later as they are drawn. Drawn straight
            // into the pictures with a command buffer (no camera, nothing queued that could show up in the next one)
            var colour = new Material[2]; var normal = new Material[2];
            for (int a = 0; a < 2; a++)
            {
                colour[a] = new Material(standaloneMats[a]); colour[a].SetFloat("_Bake", 1f);
                normal[a] = new Material(standaloneMats[a]); normal[a].SetFloat("_Bake", 2f);
            }
            int pass = standaloneMats[0].FindPass("ForwardLit"); if (pass < 0) pass = 0;
            var cmd = new UnityEngine.Rendering.CommandBuffer { name = "FirImpostorBake" };
            for (int i = 0; i < Variants; i++)
            {
                if (IsSapling(i)) continue;   // too small to see from where the pictures take over
                // the small and the thin (young trees, snags) get by with a quarter of the picture
                int sw = (flags[i] & (FlagYoung | FlagSnag)) != 0 ? ShotW / 2 : ShotW, sh = sw * ShotH / ShotW;
                var bnd = Plain[i].bounds;
                float half = Mathf.Max(bnd.extents.y, Mathf.Max(bnd.extents.x, bnd.extents.z) * 2f) * 1.02f;   // the cell is twice as tall as wide
                float w = half * ShotW / ShotH;
                // looking along +z at the tree from 60 m, centred on it
                var eye = new Vector3(0f, bnd.center.y, -60f);
                var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye, Quaternion.identity, Vector3.one).inverse;
                var proj = Matrix4x4.Ortho(-w, w, -half, half, 1f, 200f);
                RenderTexture Shot(Material m, Color clear, bool linear)
                {
                    var rt = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB)
                        { useMipMap = true, autoGenerateMips = true, name = "FirImpostor" + i };
                    rt.Create();
                    cmd.Clear();
                    cmd.SetRenderTarget(rt);
                    cmd.ClearRenderTarget(true, true, clear);   // clear: what the edges fade into down the mips
                    cmd.SetViewProjectionMatrices(view, proj);
                    cmd.DrawMesh(Plain[i], Matrix4x4.identity, m, 0, pass);
                    Graphics.ExecuteCommandBuffer(cmd);
                    return rt;
                }
                bool wood = (flags[i] & FlagSnag) != 0;
                shots[i] = Shot(colour[atlas[i]], wood ? new Color(0.36f, 0.35f, 0.33f, 0f) : new Color(0.10f, 0.14f, 0.14f, 0f), false);   // dead wood, or needle green
                normalShots[i] = Shot(normal[atlas[i]], new Color(0.5f, 1f, 0.5f, 0f), true);    // straight up
                var mat = new Material(impostorTemplate) { enableInstancing = true };
                mat.SetTexture("_BaseMap", shots[i]); mat.mainTexture = shots[i];
                mat.SetTexture("_NormalMap", normalShots[i]);
                ImpostorMats[i] = mat;
                Impostors[i] = Cross(bnd.center.y, half);
            }
            cmd.Release();
            for (int a = 0; a < 2; a++)
            {
                if (Application.isPlaying) { Object.Destroy(colour[a]); Object.Destroy(normal[a]); }
                else { Object.DestroyImmediate(colour[a]); Object.DestroyImmediate(normal[a]); }
            }
        }

        static Mesh Cross(float cy, float half)
        {
            float w = half * ShotW / ShotH;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var t = new List<int>();
            var tan = new List<Vector4>();   // the quad's left-to-right, which the picture's x runs along (FirImpostor)
            for (int k = 0; k < 2; k++)
            {
                var side = k == 0 ? Vector3.right : Vector3.forward;
                int s = v.Count;
                v.Add(-side * w + Vector3.up * (cy - half)); v.Add(side * w + Vector3.up * (cy - half));
                v.Add(-side * w + Vector3.up * (cy + half)); v.Add(side * w + Vector3.up * (cy + half));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                for (int q = 0; q < 4; q++) { n.Add(Vector3.up); tan.Add(new Vector4(side.x, side.y, side.z, 1f)); }
                t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s + 1); t.Add(s + 2); t.Add(s + 3);
            }
            var m = new Mesh { name = "FirImpostor" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTangents(tan); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
