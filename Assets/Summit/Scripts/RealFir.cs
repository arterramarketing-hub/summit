using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Realistic snowy firs, made of photo-scanned branch cards (Resources/RealTrees/FirCards.png, built by
    /// Tools/MakeFirCards.swift from Poly Haven's CC0 "Fir Tree 01" twigs).
    ///
    /// Up close, each tree is a trunk and tiers of drooping branches; every branch is two cards - one upright
    /// with a cap of snow along its top (what you see from beside the tree) and one lying flat, loaded with
    /// snow (what you see from above, riding down towards it). About 300 triangles and one material a tree.
    ///
    /// Far away, each tree is an impostor: a picture of the same tree, taken once at start-up, on two crossed
    /// quads (4 triangles). The picture keeps the tree's plain colour and its normals, and both are lit by the same
    /// sums (Shaders/Fir.hlsl), so near and far match in every time of day; the forest dissolves one into the
    /// other over a band of distance (Forest.cs).
    /// </summary>
    public static class RealFir
    {
        public const int Variants = 8;
        public static Mesh[] Meshes, Impostors;
        public static float[] Heights;
        public static Material Cards;        // the forest's trees: hand over to their far pictures
        public static Material Standalone;   // trees planted on their own (camp, start tape): always cards
        public static Material[] ImpostorMats;
        static RenderTexture[] shots, normalShots;

        public static bool Ready => Meshes != null && Cards != null;

        /// <summary>Builds the tree meshes and takes their impostor pictures (once).</summary>
        public static bool Load()
        {
            if (Ready) return true;
            var cards = Resources.Load<Material>("RealTrees/FirCards");
            var imp = Resources.Load<Material>("RealTrees/FirImpostor");
            if (cards == null || imp == null) return false;
            Cards = new Material(cards) { enableInstancing = true };
            Cards.SetFloat("_FadeOn", 1f);
            Standalone = new Material(cards) { enableInstancing = true };
            Standalone.SetFloat("_FadeOn", 0f);
            Meshes = new Mesh[Variants]; Heights = new float[Variants];
            for (int i = 0; i < Variants; i++)
            {
                Meshes[i] = Build(i, 1000 + i * 77);
                Heights[i] = Kinds[i].H;
            }
            BakeImpostors(imp);
            return true;
        }

        /// <summary>
        /// A realistic fir standing on its own, outside the forest (the camp, the trees the start tape is tied to):
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
            g.GetComponent<MeshRenderer>().sharedMaterial = Standalone;
            return g;
        }

        // ---- the atlas: 2 columns x 4 rows of 512x256 cells

        // row 0: upright with a snow cap; row 1: flat, a dusting; row 2: flat, heavy snow; row 3: bark (whole width)
        static Rect Cell(int col, int row)
        {
            const float px = 1f / 1024f;
            float u0 = col * 0.5f + px, u1 = col * 0.5f + 0.5f - px;
            float vTop = 1f - row * 0.25f - px, vBot = 1f - (row + 1) * 0.25f + px;
            return Rect.MinMaxRect(u0, vBot, u1, vTop);
        }
        const float StemUpright = 0.44f;   // where the stem runs across an upright card (0 bottom .. 1 top)
        const float StemStart = 12f / 512f, StemEnd = 498f / 512f;   // where the frond's stem starts and ends along a card

        // ---- building one tree

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            /// <summary>A card: `along` runs from the trunk end of the cell to its tip, `across` from the cell's bottom edge to its top.</summary>
            public void Card(Vector3 origin, Vector3 along, Vector3 across, Rect cell, float stemV)
            {
                int s = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float a = (k & 1) == 0 ? 0f : 1f, b = (k & 2) == 0 ? 0f : 1f;
                    var p = origin + along * a + across * (b - stemV);
                    v.Add(p);
                    // soft, rounded lighting: the normal leans out from the trunk and up, whatever way the card faces
                    var radial = new Vector3(p.x, 0f, p.z);
                    radial = radial.sqrMagnitude > 1e-4f ? radial.normalized : Vector3.Cross(across, along).normalized;
                    n.Add((radial * 0.65f + Vector3.up * 0.6f).normalized);
                    uv.Add(new Vector2(Mathf.Lerp(cell.xMin, cell.xMax, a), Mathf.Lerp(cell.yMin, cell.yMax, b)));
                }
                t.Add(s); t.Add(s + 2); t.Add(s + 1);
                t.Add(s + 1); t.Add(s + 2); t.Add(s + 3);
            }
        }

        // ---- the kinds of tree: Pacific Northwest subalpine firs and mountain hemlocks

        /// <summary>One kind of tree, built at its real height.</summary>
        struct Species
        {
            public float H;            // height, metres
            public float reach;        // longest branch (at the crown's foot) as a share of the height
            public float crownBase;    // where the branches start, share of the height
            public float droopLo, droopHi;   // branch droop (degrees) at the foot and the top of the crown
            public float flat, heavy;  // chance of a flat snow-loaded card under each branch, and of it being heavily loaded
            public float ragged;       // branches missing and of uneven length
            public float lopside;      // one side of the crown fuller than the other
            public bool nod;           // the leader droops over (hemlocks)
            public float broken;       // 0: a whole crown; else where the top snapped off (share of the height), above it a bare spike
            public float shape;        // how the branches shorten up the crown: 1 a straight cone, less a rounder, stubbier crown
        }

        static readonly Species[] Kinds =
        {
            // subalpine firs: tall, narrow spires, branches nearly level, snow along them
            new Species { H = 24f, reach = 0.12f, crownBase = 0.12f, droopLo = 22f, droopHi = 8f, flat = 0.85f, heavy = 0.7f, ragged = 0.15f, lopside = 0.1f },
            new Species { H = 27f, reach = 0.10f, crownBase = 0.16f, droopLo = 18f, droopHi = 6f, flat = 0.8f, heavy = 0.6f, ragged = 0.25f, lopside = 0.2f },
            // the same firs plastered: high up, where the storms load every branch
            new Species { H = 20f, reach = 0.17f, crownBase = 0.08f, droopLo = 30f, droopHi = 14f, flat = 1f, heavy = 0.97f, ragged = 0.2f, lopside = 0.15f, shape = 0.55f },
            new Species { H = 18f, reach = 0.19f, crownBase = 0.06f, droopLo = 34f, droopHi = 16f, flat = 1f, heavy = 0.95f, ragged = 0.3f, lopside = 0.25f, shape = 0.5f },
            // mountain hemlocks: drooping branches and a nodding top, ragged, lopsided
            new Species { H = 24f, reach = 0.15f, crownBase = 0.14f, droopLo = 42f, droopHi = 24f, flat = 0.7f, heavy = 0.65f, ragged = 0.35f, lopside = 0.25f, nod = true },
            new Species { H = 29f, reach = 0.13f, crownBase = 0.18f, droopLo = 46f, droopHi = 26f, flat = 0.65f, heavy = 0.6f, ragged = 0.45f, lopside = 0.35f, nod = true },
            // a fir whose top has snapped off: a bare spike above the last branches
            new Species { H = 25f, reach = 0.12f, crownBase = 0.15f, droopLo = 24f, droopHi = 10f, flat = 0.8f, heavy = 0.7f, ragged = 0.35f, lopside = 0.2f, broken = 0.74f },
            // a thin, ragged hemlock with gaps in its crown
            new Species { H = 22f, reach = 0.17f, crownBase = 0.2f, droopLo = 44f, droopHi = 22f, flat = 0.6f, heavy = 0.55f, ragged = 0.6f, lopside = 0.4f, nod = true },
        };

        /// <summary>Which kind of tree grows here: plastered firs high up; lower down the spires, hemlocks and the odd snag.</summary>
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

        /// <summary>The kind that is a dead snag (its top long gone): dry and brittle, it snaps easily (TreeBreak).</summary>
        public const int Snag = 6;

        /// <summary>The trunk's radius at this height (metres, model space: the tree at its kind's own height).</summary>
        public static float TrunkRadius(int variant, float y)
        {
            var sp = Kinds[variant % Variants];
            float H = sp.H, y0 = -0.3f, y1 = H * (sp.broken > 0f ? sp.broken + 0.1f : 0.8f);
            float r0 = H * 0.02f, r1 = sp.broken > 0f ? H * 0.006f : H * 0.004f;
            return Mathf.Lerp(r0, r1, Mathf.InverseLerp(y0, y1, y));
        }

        /// <summary>A tree of this variant's crown radius at a share of its height (for hit capsules).</summary>
        public static float CrownRadius(int variant, float at)
        {
            var k = Kinds[variant % Variants];
            if (at < k.crownBase || at > (k.broken > 0f ? k.broken : 0.95f)) return 0f;
            float f = Mathf.InverseLerp(k.crownBase, 0.92f, at);
            return k.reach * Mathf.Pow(1f - f, k.shape > 0f ? k.shape : 0.95f) + 0.35f / k.H;   // as a share of the height
        }

        static Mesh Build(int variant, int seed)
        {
            var rs = Random.state;
            Random.InitState(seed);
            var sp = Kinds[variant];
            var b = new Builder();
            float H = sp.H;
            float top = sp.broken > 0f ? sp.broken : 1f;   // where the crown ends

            // the trunk: six sides, tapering, bark from the bottom strip of the atlas (up through a snapped top)
            {
                var bark = Cell(0, 3); bark.xMax = 958f / 1024f;
                const int sides = 6; float y0 = -0.3f, y1 = H * (sp.broken > 0f ? sp.broken + 0.1f : 0.8f), r0 = H * 0.02f;
                float r1 = sp.broken > 0f ? H * 0.006f : H * 0.004f;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    int s = b.v.Count;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    b.v.Add(d0 * r0 + Vector3.up * y0); b.v.Add(d1 * r0 + Vector3.up * y0); b.v.Add(d0 * r1 + Vector3.up * y1); b.v.Add(d1 * r1 + Vector3.up * y1);
                    b.n.Add(d0); b.n.Add(d1); b.n.Add(d0); b.n.Add(d1);
                    float va = Mathf.Lerp(bark.yMin, bark.yMax, i / (float)sides), vb = Mathf.Lerp(bark.yMin, bark.yMax, (i + 1) / (float)sides);
                    b.uv.Add(new Vector2(bark.xMin, va)); b.uv.Add(new Vector2(bark.xMin, vb)); b.uv.Add(new Vector2(bark.xMax, va)); b.uv.Add(new Vector2(bark.xMax, vb));
                    b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 1); b.t.Add(s + 1); b.t.Add(s + 2); b.t.Add(s + 3);
                }
            }

            // the core: a dark cone inside the crown, so you never see straight through the tree
            {
                var core = Rect.MinMaxRect(0.06f, 0.59f, 0.42f, 0.66f);   // the dense middle of a frond: needles up close
                const int sides = 7; float y0 = H * (sp.crownBase + 0.04f), y1 = H * top * 0.86f, r0 = H * sp.reach * 0.58f;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    int s = b.v.Count;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    b.v.Add(d0 * r0 + Vector3.up * y0); b.v.Add(d1 * r0 + Vector3.up * y0); b.v.Add(Vector3.up * y1);
                    b.n.Add((d0 + Vector3.up * 0.5f).normalized); b.n.Add((d1 + Vector3.up * 0.5f).normalized); b.n.Add(Vector3.up);
                    b.uv.Add(new Vector2(core.xMin, core.yMin)); b.uv.Add(new Vector2(core.xMin, core.yMax)); b.uv.Add(new Vector2(core.xMax, core.center.y));
                    b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 1);
                }
            }

            // tiers of branches, broad at the foot of the crown and narrowing to a spire; about one a metre
            int tiers = Mathf.RoundToInt(H * (0.92f - sp.crownBase) * 0.95f) + Random.Range(0, 3);
            float spacing = H * (0.92f - sp.crownBase) / tiers;   // between tiers: the upright cards fill it, so the crown reads solid
            float twist = Random.value * 360f, lopAz = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < tiers; i++)
            {
                float f = i / (tiers - 1f);
                float y = H * (sp.crownBase + (0.92f - sp.crownBase) * Mathf.Pow(f, 0.92f)) + Random.Range(-0.2f, 0.2f);
                if (y > H * top) break;   // a snapped top: nothing above the break
                float reach = H * sp.reach * Mathf.Pow(1f - f, sp.shape > 0f ? sp.shape : 0.95f) + 0.35f;
                int count = Mathf.RoundToInt(Mathf.Lerp(8f, 5f, f));
                twist += 137.5f;
                for (int j = 0; j < count; j++)
                {
                    if (Random.value < sp.ragged * 0.3f) continue;   // a missing branch: ragged gaps
                    float az = (twist + j * 360f / count + Random.Range(-18f, 18f)) * Mathf.Deg2Rad;
                    float by = y + Random.Range(-0.5f, 0.5f) * spacing;   // not quite level: no shelves
                    var outH = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                    float droop = Mathf.Lerp(sp.droopLo, sp.droopHi, f) + Random.Range(-10f, 10f);
                    var dir = Quaternion.AngleAxis(droop, Vector3.Cross(outH, Vector3.up)) * outH;   // tip down
                    float uneven = 1f + sp.ragged * Random.Range(-0.45f, 0.35f) + sp.lopside * Mathf.Cos(az - lopAz);
                    float len = reach * Mathf.Max(0.35f, uneven) * Random.Range(0.85f, 1.12f) / Mathf.Cos(droop * Mathf.Deg2Rad);
                    float cardLen = len / (StemEnd - StemStart);
                    var origin = Vector3.up * by - dir * (cardLen * StemStart);
                    var side = Vector3.Cross(Vector3.up, dir).normalized;
                    var up = Vector3.Cross(dir, side).normalized;
                    float wid = cardLen * 0.5f;
                    int col = Random.Range(0, 2);
                    // upright card, snow cap on top
                    b.Card(origin, dir * cardLen, up * Mathf.Max(wid * 0.85f, spacing * 1.6f), Cell(col, 0), StemUpright);
                    // flat card, rolled a little, loaded with snow
                    if (Random.value < Mathf.Lerp(sp.flat, sp.flat * 0.55f, f))
                    {
                        var roll = Quaternion.AngleAxis(Random.Range(-15f, 15f), dir);
                        int row = Random.value < Mathf.Lerp(sp.heavy, sp.heavy * 0.45f, f) ? 2 : 1;
                        b.Card(origin, dir * cardLen, roll * side * wid * 1.15f, Cell(1 - col, row), 0.5f);
                    }
                    // plastered trees: a second load of snow on the branch, rolled the other way, so it shows from the side
                    if (sp.heavy > 0.9f && Random.value < 0.7f)
                    {
                        var roll = Quaternion.AngleAxis(Random.Range(28f, 42f) * (Random.value < 0.5f ? -1f : 1f), dir);
                        b.Card(origin + Vector3.up * 0.08f, dir * cardLen * 0.85f, roll * side * wid, Cell(col, 2), 0.5f);
                    }
                }
            }
            // the leader: two crossed fronds pointing up (a hemlock's nods over; a snapped tree has none)
            if (sp.broken <= 0f)
            {
                float y = H * 0.86f, len = H * 0.15f;
                var lead = sp.nod ? (Vector3.up + new Vector3(Mathf.Cos(lopAz), 0f, Mathf.Sin(lopAz)) * 0.55f).normalized : Vector3.up;
                for (int k = 0; k < 2; k++)
                {
                    var side = Vector3.Cross(lead, k == 0 ? Vector3.right : Vector3.forward).normalized;
                    if (side.sqrMagnitude < 0.5f) side = Vector3.right;
                    b.Card(Vector3.up * y - lead * (len * StemStart / (StemEnd - StemStart)), lead * len / (StemEnd - StemStart), side * len * 0.45f, Cell(k, 1), 0.5f);
                }
            }

            var m = new Mesh { name = "RealFir" + variant };
            m.SetVertices(b.v); m.SetNormals(b.n); m.SetUVs(0, b.uv); m.SetTriangles(b.t, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            Random.state = rs;
            return m;
        }

        // ---- impostors: a picture of each tree, side on, on two crossed quads

        const int ShotW = 256, ShotH = 512;

        static void BakeImpostors(Material impostorTemplate)
        {
            Impostors = new Mesh[Variants]; ImpostorMats = new Material[Variants];
            shots = new RenderTexture[Variants]; normalShots = new RenderTexture[Variants];
            // unlit: the plain colour, then the normals (FirCards' _Bake), lit later as they are drawn. Drawn straight
            // into the pictures with a command buffer (no camera, nothing queued that could show up in the next one)
            var colour = new Material(Standalone); colour.SetFloat("_Bake", 1f);
            var normal = new Material(Standalone); normal.SetFloat("_Bake", 2f);
            int pass = Standalone.FindPass("ForwardLit"); if (pass < 0) pass = 0;
            var cmd = new UnityEngine.Rendering.CommandBuffer { name = "FirImpostorBake" };
            for (int i = 0; i < Variants; i++)
            {
                var bnd = Meshes[i].bounds;
                float half = Mathf.Max(bnd.extents.y, Mathf.Max(bnd.extents.x, bnd.extents.z) * 2f) * 1.02f;   // the cell is twice as tall as wide
                float w = half * ShotW / ShotH;
                // looking along +z at the tree from 60 m, centred on it
                var eye = new Vector3(0f, bnd.center.y, -60f);
                var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye, Quaternion.identity, Vector3.one).inverse;
                var proj = Matrix4x4.Ortho(-w, w, -half, half, 1f, 200f);
                RenderTexture Shot(Material m, Color clear, bool linear)
                {
                    var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB)
                        { useMipMap = true, autoGenerateMips = true, name = "FirImpostor" + i };
                    rt.Create();
                    cmd.Clear();
                    cmd.SetRenderTarget(rt);
                    cmd.ClearRenderTarget(true, true, clear);   // clear: what the edges fade into down the mips
                    cmd.SetViewProjectionMatrices(view, proj);
                    cmd.DrawMesh(Meshes[i], Matrix4x4.identity, m, 0, pass);
                    Graphics.ExecuteCommandBuffer(cmd);
                    return rt;
                }
                shots[i] = Shot(colour, new Color(0.10f, 0.14f, 0.14f, 0f), false);   // needle green
                normalShots[i] = Shot(normal, new Color(0.5f, 1f, 0.5f, 0f), true);    // straight up
                var mat = new Material(impostorTemplate) { enableInstancing = true };
                mat.SetTexture("_BaseMap", shots[i]); mat.mainTexture = shots[i];
                mat.SetTexture("_NormalMap", normalShots[i]);
                ImpostorMats[i] = mat;
                Impostors[i] = Cross(bnd.center.y, half);
            }
            cmd.Release();
            if (Application.isPlaying) { Object.Destroy(colour); Object.Destroy(normal); }
            else { Object.DestroyImmediate(colour); Object.DestroyImmediate(normal); }
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
