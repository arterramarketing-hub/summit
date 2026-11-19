using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Trees and rocks on a fixed grid of cells. Whether a cell holds something is a pure
    /// function of the cell, so the forest is the same for everyone on the same day.
    ///
    /// Every tree is drawn from its real model, in instanced batches, from right beside you
    /// to where the fog swallows it - so nothing ever swaps model, height or type as you
    /// approach. Only the near ones cast shadows. Invisible colliders are placed near the
    /// rider for hits.
    ///
    /// Two looks, switched from the pause menu (PlayerPrefs "summit_trees"): the original low-poly firs, or
    /// realistic snowy firs built from photo-scanned branch cards (see RealFir), which dissolve into flat
    /// impostor pictures between FadeStart and FadeEnd.
    ///
    /// The rocks are photo-scanned boulders (Resources/ScanRocks, built by Tools/MakeScanRocks.py) in either look,
    /// with their snow laid on by Resources/ScanRockLit.shader.
    /// </summary>
    public class Forest : MonoBehaviour
    {
        public const float Cell = 6.5f;
        public Transform target;
        public Material foliage, bark;
        public int sideCells = 12, aheadCells = 18, behindCells = 3;   // colliders, and shadow-casting trees
        public int farSide = 44, farAhead = 88;                         // drawn out to here (~570 m ahead, ~290 m aside)
        const float LodRange = 300f;   // beyond this, trees are drawn as simple low-poly firs
        readonly List<Matrix4x4> distL = new List<Matrix4x4>();
        Mesh lodMesh; Material lodFoliage, lodBark;

        // ---- which trees: the original low-poly ones, or realistic card trees
        public const string TreesPref = "summit_trees";
        public static bool Realistic => PlayerPrefs.GetInt(TreesPref, 1) == 1;
        bool real;                         // the look this forest was built with
        // realistic trees: card trees out to FadeStart, their pictures beyond FadeEnd, and in between both, each
        // pixel one or the other (Shaders/Fir.hlsl), the pictures' share growing with distance. The band ends where
        // the shadows have faded out (the render asset's 140 m), so the cards hand over shadowless, like the pictures.
        public float FadeStart = 100f, FadeEnd = 140f;
        // and right beside the rider the rich trees (RealFir), which dissolve into the plain card trees the same way
        // between RichStart and RichEnd. The plain tree is still drawn in there, for its shadow: the shader folds
        // away whichever of the two has no pixels to show.
        public float RichStart = 30f, RichEnd = 44f;
        readonly Dictionary<int, List<Matrix4x4>> richL = new Dictionary<int, List<Matrix4x4>>();
        const float FadeSlack = 9f;   // lists are made from cell centres when the rider changes cell; the shader
                                      // fades by the exact distance, so each list reaches a little past the band
        List<Matrix4x4>[] impL;

        /// <summary>Switch between the two looks; the trees are rebuilt where they stand.</summary>
        public void SetRealistic(bool on)
        {
            PlayerPrefs.SetInt(TreesPref, on ? 1 : 0); PlayerPrefs.Save();
            loaded = false;
            treeParts.Clear(); treeH.Clear();   // rocks stay as they are
            nearL.Clear(); farL.Clear(); richL.Clear();
            Rebuild();
        }

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
            if (Slope.OnPad(wx, wz)) return Kind.None;
            if (Slope.Hash01(cx, cz, 1) >= density) return Kind.None;
            if (Course.Keep(wx, wz, 1f)) return Kind.None;   // pipe, crevasses, park features
            if (LiftLine.Taken(wx, wz, 3.5f)) return Kind.None;   // ski lifts (LiftLine.cs): the cleared swath under a line, and its stations
            float x = wx + (Slope.Hash01(cx, cz, 2) - 0.5f) * Cell * 0.75f;
            float z = wz + (Slope.Hash01(cx, cz, 3) - 0.5f) * Cell * 0.75f;
            if (Course.Keep(x, z, 0.5f)) return Kind.None;   // (where it really stands, too: the long park features)
            if (Hamlets.Taken(x, z)) return Kind.None;       // hamlets (Hamlets.cs): nothing grows in a house, its fence or its clearing
            if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f || Mathf.Abs(Slope.Kicker(x, z - 14f)) > 0.01f) return Kind.None;   // keep kickers clear
            variant = (int)(Slope.Hash01(cx, cz, 7) * 64f);
            bool rock = Slope.Hash01(cx, cz, 8) < (off < Slope.PisteHalf ? 0.25f : 0.12f);
            pos = new Vector3(x, Slope.DrawnHeight(x, z), z);
            size = rock ? 0.8f + 1.4f * Slope.Hash01(cx, cz, 4) : 0.75f + 0.6f * Slope.Hash01(cx, cz, 4);
            return rock ? Kind.Rock : Kind.Tree;
        }

        // ---- what the models are made of

        class Part { public Mesh mesh; public int sub; public Material mat; public Matrix4x4 local; }
        readonly List<List<Part>> treeParts = new List<List<Part>>(), rockParts = new List<List<Part>>();
        readonly List<float> treeH = new List<float>(), rockH = new List<float>();
        bool loaded;

        void LoadKinds()
        {
            if (loaded) return;
            loaded = true;
            real = Realistic && RealFir.Load();
            if (real)
            {
                // every kind twice: first the plain trees of the middle distance, then (from RealFir.Variants on) the rich ones
                for (int i = 0; i < RealFir.Variants * 2; i++)
                {
                    int v = i % RealFir.Variants; bool rich = i >= RealFir.Variants;
                    treeParts.Add(new List<Part> { new Part { mesh = rich ? RealFir.Meshes[v] : RealFir.Plain[v], sub = 0, mat = rich ? RealFir.RichOf(v) : RealFir.PlainOf(v), local = Matrix4x4.identity } });
                    treeH.Add(RealFir.Heights[v]);
                }
                impL = new List<Matrix4x4>[RealFir.Variants];
                for (int i = 0; i < impL.Length; i++) impL[i] = new List<Matrix4x4>();
            }
            else foreach (var g in Resources.LoadAll<GameObject>("Trees")) Add(g, treeParts, treeH);
            if (rockParts.Count == 0)
            {
                // photo-scanned boulders (Tools/MakeScanRocks.py), all wearing the one snowy rock material
                foreach (var g in Resources.LoadAll<GameObject>("ScanRocks")) Add(g, rockParts, rockH);
                var scan = ScanRockMaterial();
                if (scan != null) foreach (var parts in rockParts) foreach (var part in parts) part.mat = scan;
            }
            if (treeParts.Count == 0) { treeParts.Add(Fallback(FirMesh(), new[] { foliage, bark })); treeH.Add(6.3f); }
            if (lodMesh == null && foliage != null && bark != null)
            {
                lodMesh = FirMesh();
                lodFoliage = new Material(foliage) { enableInstancing = true };
                lodBark = new Material(bark) { enableInstancing = true };
            }
            if (rockParts.Count == 0) { rockParts.Add(Fallback(RockMesh(), new[] { bark })); rockH.Add(1f); }
        }

        void Add(GameObject prefab, List<List<Part>> into, List<float> heights)
        {
            var parts = new List<Part>();
            float top = float.MinValue, bottom = float.MaxValue;
            var rootInv = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                // relative to the root, but keeping the root's own rotation (the Blender axis fix)
                var local = Matrix4x4.Rotate(prefab.transform.localRotation) * rootInv * mf.transform.localToWorldMatrix;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    var src = mr.sharedMaterials[Mathf.Min(s, mr.sharedMaterials.Length - 1)];
                    // the trees move in the wind (TreeWind); rocks keep their own material
                    var m = into == treeParts ? TreeWind.Swaying(src) : new Material(src) { enableInstancing = true };
                    parts.Add(new Part { mesh = mf.sharedMesh, sub = s, mat = m, local = local });
                }
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float y = local.MultiplyPoint3x4(c).y;
                    top = Mathf.Max(top, y); bottom = Mathf.Min(bottom, y);
                }
            }
            if (parts.Count == 0) return;
            into.Add(parts);
            heights.Add(top > bottom ? top - bottom : 1f);
        }

        /// <summary>
        /// The rocks' material: Resources/ScanRockLit.shader over the scans' shared colour and normal atlases
        /// (snow on what faces up and banked at the foot). Null if any of it is missing: the rocks then keep the
        /// plain material their models came with.
        /// </summary>
        static Material ScanRockMaterial()
        {
            var sh = Resources.Load<Shader>("ScanRockLit");
            var albedo = Resources.Load<Texture2D>("ScanRocks/ScanRocksAlbedo");
            var normal = Resources.Load<Texture2D>("ScanRocks/ScanRocksNormal");
            if (sh == null || !sh.isSupported || albedo == null || normal == null) return null;
            var m = new Material(sh) { name = "ScanRock", enableInstancing = true };
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_BumpMap", normal);
            return m;
        }

        static List<Part> Fallback(Mesh mesh, Material[] mats)
        {
            var parts = new List<Part>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var m = new Material(mats[Mathf.Min(s, mats.Length - 1)]) { enableInstancing = true };
                parts.Add(new Part { mesh = mesh, sub = s, mat = m, local = Matrix4x4.identity });
            }
            return parts;
        }

        // ---- placement, cached per cell

        // foot: where it meets the snow; vis: how much of it shows above the snow (m); rad: its hit capsule;
        // cut: the snow line in the model's own height (what a felled tree leaves behind in the snow)
        // girth: the trunk's radius where it comes out of the snow (m); ys: the model's vertical scale (TreeBreak)
        // deco: a sapling in an otherwise empty cell (kind None): drawn, but nothing to hit
        struct Placed { public Kind kind; public int idx; public Matrix4x4 m; public float size; public Vector3 foot; public float vis, rad, cut, girth, ys; public bool deco; }

        // ---- realistic trees: how tall they grow, and how deep the snow buries them

        /// <summary>0 at the summit .. 1 at the foot of the mountain.</summary>
        public static float Low(float z) => Mathf.Clamp01(1f - Slope.AltitudeFeet(z) / Slope.VerticalFeet);

        /// <summary>
        /// A realistic tree at (x, z): how tall it has grown (`full`, metres) and how much of that is under the snow
        /// (`sink`). Full-grown trees are 20-35 m at the foot of the mountain and smaller near the summit, in stands
        /// that run taller and shorter along the run; and the higher up, the deeper the snow: near the summit only the
        /// top 40% of a tree shows, and going down more and more of it does, until at the bottom the whole tree
        /// stands on its trunk. `pick` (0..1) is the tree's own share of the variety.
        /// </summary>
        public static void Growth(float x, float z, float pick, out float full, out float sink)
        {
            float low = Low(z);
            float stand = Mathf.Lerp(0.8f, 1.22f, Slope.Noise(x * 0.011f + 31f, z * 0.0065f + 7f));
            full = Mathf.Lerp(15f, 27f, low) * stand * Mathf.Lerp(0.85f, 1.1f, pick);
            float shown = Mathf.Lerp(0.38f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(low * 1.15f)));
            sink = full * (1f - shown);
        }

        /// <summary>
        /// Plant a realistic tree on its own (the camp, the start tape) the way the forest grows one at `ground`:
        /// its kind, height and burial for that spot. Returns how much of it shows and its hit radius.
        /// </summary>
        public static GameObject PlantLike(Transform parent, Vector3 ground, float pick, float yaw, string name, out float vis, out float rad)
        {
            Growth(ground.x, ground.z, pick, out float full, out float sink);
            int v = RealFir.Pick(Low(ground.z), Mathf.Repeat(pick * 7.31f, 1f));
            vis = full - sink;
            rad = Mathf.Max(0.02f * full, 0.7f * RealFir.CrownRadius(v, sink / full) * full);
            return RealFir.Plant(parent, ground + Vector3.down * (0.25f + sink), full, yaw, v, name);
        }

        // ---- realistic trees away from the piste: stands of one species, of one age

        /// <summary>
        /// Beyond this far outside the groomed piste the forest grows in patches of species and ages (Stand). On the
        /// piste and up to here beside it, every tree is one of the original firs and hemlocks at the size it always
        /// was, so what there is to hit along the rideable line has not changed.
        /// </summary>
        public const float WildMargin = 6f;

        /// <summary>
        /// What grows around (x, z), away from the piste. Pure functions of the place, in patches a hundred metres or
        /// two across: `pines` is the chance a tree here is a pine (stands of them on the open, thinly wooded and
        /// rocky ground; the dense groves are fir and hemlock), `young` and `old` the shares of young trees and old
        /// giants (young at the edges of clearings and in stands of their own, giants in patches of old growth),
        /// `dead` the share of dead snags (a few everywhere, more among the pines, many in the odd burnt patch) and
        /// `edge` how much this is the fringe of a clearing (where saplings come up).
        /// </summary>
        public static void Stand(int cx, int cz, float x, float z, out float pines, out float young, out float old, out float dead, out float edge)
        {
            float glade = Slope.Noise(x * 0.016f + 9f, z * 0.016f);   // (At: the stands of forest and the open glades)
            // rocky ground: how many of the cells around are likely to hold a rock (At gives a cell a rock on hash 8)
            int rocks = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dz != 0) && Slope.Hash01(cx + dx, cz + dz, 8) < 0.12f && Slope.Hash01(cx + dx, cz + dz, 1) < 0.45f) rocks++;
            float p = Slope.Noise(x * 0.0075f + 53f, z * 0.0055f + 17f) + 0.35f * (0.5f - glade) + 0.07f * rocks;
            pines = Mathf.Lerp(0.03f, 0.92f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 0.80f, p)));
            edge = Mathf.Clamp01(1f - Mathf.Abs(glade - 0.42f) / 0.14f);
            float age = Slope.Noise(x * 0.012f + 91f, z * 0.010f + 37f);
            young = Mathf.Lerp(0.5f, 0.07f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.45f, age))) + 0.22f * edge;
            old = 0.02f + 0.3f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.82f, age));
            bool burnt = Slope.Noise(x * 0.03f + 141f, z * 0.03f + 5f) > 0.8f;
            dead = burnt ? 0.4f : 0.035f + 0.04f * pines;
        }

        /// <summary>Is the cell centred on (wx, wz) far enough off the piste for the patchwork forest?</summary>
        static bool Wild(float wx, float wz) => Mathf.Abs(wx - Slope.Centre(wz)) > Slope.PisteHalf + WildMargin;

        /// <summary>
        /// A sapling for an empty cell, or false: the tops of young trees poking out of the snow at the edges of
        /// clearings and among young stands, well away from the piste and clear of everything the course keeps clear.
        /// Decoration only: there is nothing to hit, and the rider goes straight through.
        /// </summary>
        static bool Sapling(int cx, int cz, out Placed pl)
        {
            pl = default;
            float wx = (cx + 0.5f) * Cell, wz = (cz + 0.5f) * Cell;
            if (Mathf.Abs(wx - Slope.Centre(wz)) < Slope.PisteHalf + 8f) return false;
            if (wz < 70f) return false;                                   // a clean start
            float r = Slope.Hash01(cx, cz, 13);
            if (r >= 0.4f) return false;                                  // (the most a cell's chance can be: skip the sums)
            Stand(cx, cz, wx, wz, out float pines, out float young, out _, out _, out float edge);
            if (r >= 0.04f + 0.26f * edge + 0.2f * Mathf.Max(0f, young - 0.25f)) return false;
            if (Slope.OnPad(wx, wz) || Course.Keep(wx, wz, 1f)) return false;
            if (LiftLine.Taken(wx, wz, 3.5f)) return false;   // not in a ski lift's cleared swath, like the trees
            float x = wx + (Slope.Hash01(cx, cz, 17) - 0.5f) * Cell * 0.75f;
            float z = wz + (Slope.Hash01(cx, cz, 18) - 0.5f) * Cell * 0.75f;
            if (Course.Keep(x, z, 0.5f)) return false;
            if (Hamlets.Taken(x, z)) return false;            // nor in a hamlet's houses, fence or clearing
            if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f || Mathf.Abs(Slope.Kicker(x, z - 14f)) > 0.01f) return false;
            int idx = RealFir.PickSapling(Slope.Hash01(cx, cz, 15) < pines, Slope.Hash01(cx, cz, 9));
            if (idx < 0) return false;
            // 1.5 to 3.5 m tall, up to a third of it under the snow
            float k = Mathf.Lerp(0.7f, 1.5f, Slope.Hash01(cx, cz, 4));
            float sink = RealFir.Heights[idx] * k * 0.35f * Slope.Hash01(cx, cz, 6);
            var p = new Vector3(x, Slope.DrawnHeight(x, z), z);
            pl.deco = true; pl.idx = idx; pl.foot = p;
            pl.m = Matrix4x4.TRS(p - Vector3.up * (0.25f * k + sink), Quaternion.Euler(0f, Slope.Hash01(cx, cz, 5) * 360f, 0f), Vector3.one * k);
            return true;
        }
        readonly Dictionary<Vector2Int, Placed> cache = new Dictionary<Vector2Int, Placed>();

        Placed Get(int x, int z)
        {
            var key = new Vector2Int(x, z);
            if (felled.Contains(key)) return default;   // smashed this run: gone until the next
            if (cache.TryGetValue(key, out var pl)) return pl;
            var kind = At(x, z, out Vector3 p, out float s, out int variant);
            pl = new Placed { kind = kind, size = s };
            if (kind == Kind.None && real) Sapling(x, z, out pl);
            else if (kind == Kind.Tree && real && Wild((x + 0.5f) * Cell, (z + 0.5f) * Cell))
            {
                // away from the piste: the kind for this stand (species and age in patches), grown to its age,
                // standing in the same depth of snow as its neighbours; pines and snags lean more
                Stand(x, z, p.x, p.z, out float pines, out float young, out float old, out float dead, out _);
                pl.idx = RealFir.PickWild(Low(p.z), pines, young, old, dead, Slope.Hash01(x, z, 9), Slope.Hash01(x, z, 14), Slope.Hash01(x, z, 15), Slope.Hash01(x, z, 16));
                Growth(p.x, p.z, Slope.Hash01(x, z, 4), out float full, out float sink);
                if (pl.idx >= 8)
                {
                    float grown = full * RealFir.SizeOf(pl.idx);
                    // the snow is as deep as round a full-grown tree, but a small tree still shows its top third
                    sink = Mathf.Min(sink, grown * 0.66f);
                    full = grown;
                }
                float k = full / RealFir.Heights[pl.idx], yk = 0.94f + 0.12f * Slope.Hash01(x, z, 6);
                bool loose = RealFir.IsPine(pl.idx) || RealFir.IsSnag(pl.idx);
                float lean = Mathf.Pow(Slope.Hash01(x, z, 10), loose ? 2f : 3f) * (loose ? 11f : 7f);
                var rot = Quaternion.AngleAxis(lean, Quaternion.Euler(0f, Slope.Hash01(x, z, 11) * 360f, 0f) * Vector3.right)
                        * Quaternion.Euler(0f, Slope.Hash01(x, z, 5) * 360f, 0f);
                pl.m = Matrix4x4.TRS(p - Vector3.up * (0.25f + sink), rot, new Vector3(k, k * yk, k));
                pl.foot = p;
                pl.vis = full * yk - sink;
                pl.cut = (0.25f + sink) / (k * yk);
                pl.rad = RealFir.HitRadius(pl.idx, full, sink);
                pl.ys = k * yk;
                pl.girth = RealFir.TrunkRadius(pl.idx, pl.cut) * k;
            }
            else if (kind == Kind.Tree && real)
            {
                // a realistic tree: the kind for this height on the mountain, grown and buried for it, a few leaning
                pl.idx = RealFir.Pick(Low(p.z), Slope.Hash01(x, z, 9));
                Growth(p.x, p.z, Slope.Hash01(x, z, 4), out float full, out float sink);
                float k = full / RealFir.Heights[pl.idx], yk = 0.94f + 0.12f * Slope.Hash01(x, z, 6);
                float lean = Mathf.Pow(Slope.Hash01(x, z, 10), 3f) * 7f;
                var rot = Quaternion.AngleAxis(lean, Quaternion.Euler(0f, Slope.Hash01(x, z, 11) * 360f, 0f) * Vector3.right)
                        * Quaternion.Euler(0f, Slope.Hash01(x, z, 5) * 360f, 0f);
                pl.m = Matrix4x4.TRS(p - Vector3.up * (0.25f + sink), rot, new Vector3(k, k * yk, k));
                pl.foot = p;
                pl.vis = full * yk - sink;
                pl.cut = (0.25f + sink) / (k * yk);
                pl.rad = Mathf.Max(0.02f * full, 0.7f * RealFir.CrownRadius(pl.idx, sink / full) * full);
                pl.ys = k * yk;
                pl.girth = RealFir.TrunkRadius(pl.idx, pl.cut) * k;
            }
            else if (kind != Kind.None)
            {
                bool tree = kind == Kind.Tree;
                pl.idx = variant % (tree ? treeParts.Count : rockParts.Count);
                float h = tree ? treeH[pl.idx] : rockH[pl.idx];
                float want = tree ? 8.5f * s : 1.3f * s;
                float k = want / Mathf.Max(0.01f, h);
                pl.m = Matrix4x4.TRS(p - Vector3.up * (tree ? 0.25f : 0.35f * s), Quaternion.Euler(0f, Slope.Hash01(x, z, 5) * 360f, 0f),
                                     new Vector3(k, k * (0.92f + 0.2f * Slope.Hash01(x, z, 6)), k));
                pl.foot = pl.m.GetColumn(3);
                pl.vis = tree ? 8.5f * s : 1.3f * s; pl.rad = 0.38f * s;
                pl.ys = k; pl.girth = 0.25f * s;
            }
            cache[key] = pl;
            return pl;
        }

        // ---- drawing lists, rebuilt when the rider crosses into a new cell

        readonly Dictionary<int, List<Matrix4x4>> nearL = new Dictionary<int, List<Matrix4x4>>(), farL = new Dictionary<int, List<Matrix4x4>>();
        Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);

        static int Key(Kind k, int idx, int part) => (k == Kind.Tree ? 0 : 4096) + idx * 16 + part;
        List<Part> PartsOf(int key) => key >= 4096 ? rockParts[(key - 4096) / 16] : treeParts[key / 16];

        void Update()
        {
            Refresh(false);
            Draw();
        }

        public void Prewarm() { Refresh(true); }

        /// <summary>What the forest asks the GPU for this frame: instanced draw calls, triangles, and the share of
        /// each that is drawn again into the shadow map (the near set).</summary>
        public void Stats(out int draws, out long tris, out int shadowDraws, out long shadowTris, out int instances)
        {
            draws = 0; tris = 0; shadowDraws = 0; shadowTris = 0; instances = 0;
            foreach (var set in new[] { nearL, farL })
                foreach (var kv in set)
                {
                    int n = kv.Value.Count; if (n == 0) continue;
                    var part = PartsOf(kv.Key)[kv.Key & 15];
                    long t = (long)part.mesh.GetIndexCount(part.sub) / 3 * n;
                    int d = (n + 999) / 1000;
                    draws += d; tris += t; instances += n;
                    if (set == nearL) { shadowDraws += d; shadowTris += t; }
                }
            foreach (var kv in richL)   // the rich trees: no shadow pass, and each also counted above as its plain tree
            {
                int n = kv.Value.Count; if (n == 0) continue;
                var part = PartsOf(kv.Key)[kv.Key & 15];
                draws += (n + 999) / 1000; tris += (long)part.mesh.GetIndexCount(part.sub) / 3 * n;
            }
            if (real && impL != null)
                for (int v = 0; v < impL.Length; v++)
                {
                    int n = impL[v].Count; if (n == 0) continue;
                    draws += (n + 999) / 1000; tris += 4L * n; instances += n;
                }
            if (!real && lodMesh != null && distL.Count > 0)
            {
                int d = (distL.Count + 999) / 1000;
                draws += 2 * d; instances += distL.Count;
                tris += (long)(lodMesh.GetIndexCount(0) + lodMesh.GetIndexCount(1)) / 3 * distL.Count;
            }
        }
        public bool IsRealistic => real;

        /// <summary>A new mountain: forget every placement and collider, and place again.</summary>
        public void Rebuild()
        {
            cache.Clear();
            felled.Clear();
            stumps.Clear(); stumpL.Clear();
            foreach (var kv in live)
            {
                var g = kv.Value; g.SetActive(false);
                (g.GetComponent<CapsuleCollider>() != null ? spareTree : spareRock).Enqueue(g);
            }
            live.Clear();
            lastCell = new Vector2Int(int.MinValue, int.MinValue);
            Refresh(true);
        }

        const float ShadowRange = 150f;   // a little past the render asset's 140 m shadow distance

        void Refresh(bool force)
        {
            if (target == null) return;
            LoadKinds();
            var c = new Vector2Int(Mathf.FloorToInt(target.position.x / Cell), Mathf.FloorToInt(target.position.z / Cell));
            if (!force && c == lastCell) return;
            lastCell = c;
            foreach (var l in nearL.Values) l.Clear();
            foreach (var l in farL.Values) l.Clear();
            foreach (var l in richL.Values) l.Clear();
            distL.Clear();
            if (impL != null) foreach (var l in impL) l.Clear();
            for (int z = c.y - behindCells; z <= c.y + farAhead; z++)
                for (int x = c.x - farSide; x <= c.x + farSide; x++)
                {
                    var pl = Get(x, z);
                    if (pl.kind == Kind.None && !pl.deco) continue;
                    // shadows for every tree within the shadow range, so none pops a shadow on as it nears
                    // (the render asset fades shadows out over the last part of that range)
                    float dxm = (x - c.x) * Cell, dzm = (z - c.y) * Cell;
                    float d2 = dxm * dxm + dzm * dzm;
                    if (pl.deco)
                    {
                        // a sapling: its cards as far as the trees' reach (it fades out with them; no picture beyond)
                        if (d2 <= (FadeEnd + FadeSlack) * (FadeEnd + FadeSlack)) ListFor(nearL, Key(Kind.Tree, pl.idx, 0)).Add(pl.m);
                        continue;
                    }
                    if (real && pl.kind == Kind.Tree)
                    {
                        // far realistic tree: its picture on two crossed quads; through the band, the cards too
                        if (d2 > (FadeStart - FadeSlack) * (FadeStart - FadeSlack)) impL[pl.idx].Add(pl.m);
                        if (d2 > (FadeEnd + FadeSlack) * (FadeEnd + FadeSlack)) continue;
                        // close by, the rich tree as well (the plain one below still casts the shadow for both)
                        if (d2 < (RichEnd + FadeSlack) * (RichEnd + FadeSlack)) ListFor(richL, Key(Kind.Tree, RealFir.Variants + pl.idx, 0)).Add(pl.m);
                    }
                    if (d2 > LodRange * LodRange)
                    {
                        // far away: one cheap fir the same height instead of the full model (rocks are left out)
                        if (pl.kind == Kind.Tree && lodMesh != null) distL.Add(pl.m * Matrix4x4.Scale(Vector3.one * (treeH[pl.idx] / 6.3f)));
                        continue;
                    }
                    bool isNear = d2 < ShadowRange * ShadowRange;
                    var parts = pl.kind == Kind.Tree ? treeParts[pl.idx] : rockParts[pl.idx];
                    var dict = isNear ? nearL : farL;
                    for (int i = 0; i < parts.Count; i++)
                    {
                        int key = Key(pl.kind, pl.idx, i);
                        if (!dict.TryGetValue(key, out var list)) dict[key] = list = new List<Matrix4x4>();
                        list.Add(pl.m * parts[i].local);
                    }
                }
            PlaceColliders(c);
            stumpL.Clear();
            foreach (var kv in stumps)
            {
                float dxm = (kv.Key.x - c.x) * Cell, dzm = (kv.Key.y - c.y) * Cell;
                if (dxm * dxm + dzm * dzm < ShadowRange * ShadowRange) stumpL.Add(kv.Value);
            }
            // forget cells well behind
            if (cache.Count > 30000)
            {
                var drop = new List<Vector2Int>();
                foreach (var k in cache.Keys) if (k.y < c.y - behindCells - 4) drop.Add(k);
                foreach (var k in drop) cache.Remove(k);
            }
        }

        static List<Matrix4x4> ListFor(Dictionary<int, List<Matrix4x4>> set, int key)
        {
            if (!set.TryGetValue(key, out var list)) set[key] = list = new List<Matrix4x4>();
            return list;
        }

        static readonly int FirFadeId = Shader.PropertyToID("_FirFade"), FirCentreId = Shader.PropertyToID("_FirCentre");

        void Draw()
        {
            if (real && target != null)
            {
                Shader.SetGlobalVector(FirFadeId, new Vector4(FadeStart, FadeEnd, RichStart, RichEnd));
                Shader.SetGlobalVector(FirCentreId, target.position);
            }
            DrawSet(nearL, UnityEngine.Rendering.ShadowCastingMode.On);
            DrawSet(farL, UnityEngine.Rendering.ShadowCastingMode.Off);
            DrawSet(richL, UnityEngine.Rendering.ShadowCastingMode.Off);
            if (stumpL.Count > 0) TreeBreak.DrawStumps(stumpL, real, bark);
            if (real && impL != null)
            {
                for (int v = 0; v < impL.Length; v++)
                {
                    var list = impL[v];
                    if (list.Count == 0) continue;
                    var rp = new RenderParams(RealFir.ImpostorMats[v]) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                    for (int i = 0; i < list.Count; i += 1000)
                        Graphics.RenderMeshInstanced(rp, RealFir.Impostors[v], 0, list, Mathf.Min(1000, list.Count - i), i);
                }
            }
            if (distL.Count > 0 && lodMesh != null)
            {
                var rf = new RenderParams(lodFoliage) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                var rb = new RenderParams(lodBark) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                for (int i = 0; i < distL.Count; i += 1000)
                {
                    int n = Mathf.Min(1000, distL.Count - i);
                    Graphics.RenderMeshInstanced(rf, lodMesh, 0, distL, n, i);
                    Graphics.RenderMeshInstanced(rb, lodMesh, 1, distL, n, i);
                }
            }
        }

        void DrawSet(Dictionary<int, List<Matrix4x4>> set, UnityEngine.Rendering.ShadowCastingMode shadows)
        {
            foreach (var kv in set)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;
                var part = PartsOf(kv.Key)[kv.Key & 15];
                var rp = new RenderParams(part.mat) { shadowCastingMode = shadows, receiveShadows = true };
                for (int i = 0; i < list.Count; i += 1000)
                    Graphics.RenderMeshInstanced(rp, part.mesh, part.sub, list, Mathf.Min(1000, list.Count - i), i);
            }
        }

        // ---- invisible colliders for the near trees and rocks

        readonly Dictionary<Vector2Int, GameObject> live = new Dictionary<Vector2Int, GameObject>();
        readonly Queue<GameObject> spareTree = new Queue<GameObject>(), spareRock = new Queue<GameObject>();
        readonly List<Vector2Int> dropList = new List<Vector2Int>();

        void PlaceColliders(Vector2Int c)
        {
            dropList.Clear();
            foreach (var kv in live)
                if (kv.Key.x < c.x - sideCells || kv.Key.x > c.x + sideCells || kv.Key.y < c.y - behindCells || kv.Key.y > c.y + aheadCells)
                    dropList.Add(kv.Key);
            foreach (var k in dropList)
            {
                var g = live[k]; live.Remove(k);
                g.SetActive(false);
                (g.GetComponent<CapsuleCollider>() != null ? spareTree : spareRock).Enqueue(g);
            }
            for (int z = c.y - behindCells; z <= c.y + aheadCells; z++)
                for (int x = c.x - sideCells; x <= c.x + sideCells; x++)
                {
                    var key = new Vector2Int(x, z);
                    if (live.ContainsKey(key)) continue;
                    var pl = Get(x, z);
                    if (pl.kind == Kind.None) continue;
                    bool tree = pl.kind == Kind.Tree;
                    var q = tree ? spareTree : spareRock;
                    var g = q.Count > 0 ? q.Dequeue() : NewCollider(tree);
                    g.SetActive(true);
                    g.GetComponent<Collider>().enabled = true;   // (an invincible rider switches off what it smashes)
                    g.transform.position = pl.foot;
                    if (tree)
                    {
                        // from the snow up, as much of the tree as shows (a buried tree's crown, a whole one's trunk)
                        // where the trunk comes out of the snow, leaning as the tree does. (A tree leans from its foot
                        // under the snow: buried metres deep, a lean of a few degrees puts its trunk a metre or two
                        // from the spot straight above that foot, where an upright capsule stood round nothing)
                        Vector3 axis = ((Vector3)pl.m.GetColumn(1)).normalized, pivot = pl.m.GetColumn(3);
                        Vector3 at = axis.y > 0.2f ? pivot + axis * ((pl.foot.y - pivot.y) / axis.y) : pl.foot;
                        g.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, axis));
                        var cap = g.GetComponent<CapsuleCollider>();
                        float h = pl.vis;
                        cap.radius = pl.rad; cap.height = Mathf.Max(h * 0.9f, pl.rad * 2f); cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
                    }
                    else RockBox(g, pl);
                    live[key] = g;
                }
        }

        /// <summary>
        /// A rock's hit shape: the boulder as drawn, a tapered eight-sided hull inside its box (CliffSpires.Frustum),
        /// placed, turned and scaled as the rock is. (It was a sphere 0.55 of the rock's height across its middle,
        /// which stood a metre clear of a long, low boulder's sides at the snow: a hit on nothing.)
        /// </summary>
        void RockBox(GameObject g, Placed pl)
        {
            var mc = g.GetComponent<MeshCollider>();
            mc.sharedMesh = RockHull(pl.idx);
            Vector3 s = pl.m.lossyScale;
            g.transform.SetPositionAndRotation(pl.m.GetColumn(3), pl.m.rotation);
            g.transform.localScale = s;
        }

        readonly Dictionary<int, Mesh> rockHulls = new Dictionary<int, Mesh>();

        /// <summary>A rock model's hit hull, in the model's own space (its parts' boxes together).</summary>
        Mesh RockHull(int idx)
        {
            if (rockHulls.TryGetValue(idx, out var m) && m != null) return m;
            Bounds b = default; bool any = false;
            foreach (var part in rockParts[idx])
            {
                var mb = part.mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = part.local.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (any) b.Encapsulate(c); else { b = new Bounds(c, Vector3.zero); any = true; }
                }
            }
            if (!any) b = new Bounds(Vector3.up * 0.5f, Vector3.one);
            m = CliffSpires.Frustum(b, 0.8f, "RockHull" + idx);
            rockHulls[idx] = m;
            return m;
        }

        // ---- smashed by an invincible rider

        /// <summary>One piece of a tree or rock as drawn: mesh, sub-mesh, material, and where in the world.</summary>
        /// <summary>A piece of a felled tree or rock; `cut` (model height) hides what was under the snow (0: nothing).</summary>
        public struct Piece { public Mesh mesh; public int sub; public Material mat; public Matrix4x4 m; public float cut; }
        readonly HashSet<Vector2Int> felled = new HashSet<Vector2Int>();

        /// <summary>
        /// Takes the tree or rock behind this collider out of the forest for the rest of the run and hands back
        /// the pieces it was drawn with, so the smash can knock them over or break them up. False if it is not ours.
        /// </summary>
        public bool Fell(GameObject hit, List<Piece> into, out Vector3 basePos, out float size)
        {
            basePos = Vector3.zero; size = 1f;
            foreach (var kv in live)
            {
                if (kv.Value != hit) continue;
                var pl = Get(kv.Key.x, kv.Key.y);
                felled.Add(kv.Key);
                live.Remove(kv.Key);
                hit.SetActive(false);
                (hit.GetComponent<CapsuleCollider>() != null ? spareTree : spareRock).Enqueue(hit);
                if (pl.kind == Kind.None) return false;
                // a tree goes over from where it meets the snow, as tall as it shows (Smasher: 8.5 m a size)
                basePos = pl.foot; size = pl.kind == Kind.Tree ? pl.vis / 8.5f : pl.size;
                if (real && pl.kind == Kind.Tree) into.Add(Whole(pl, pl.cut));
                else foreach (var part in pl.kind == Kind.Tree ? treeParts[pl.idx] : rockParts[pl.idx])
                    into.Add(new Piece { mesh = part.mesh, sub = part.sub, mat = part.mat, m = pl.m * part.local, cut = pl.kind == Kind.Tree ? pl.cut : 0f });
                // redraw without it
                lastCell = new Vector2Int(int.MinValue, int.MinValue);
                Refresh(false);
                return true;
            }
            return false;
        }

        /// <summary>A realistic tree taken out of the forest to be knocked down: its rich mesh, in the material that
        /// never dissolves (it is seen from close by, and lies there as the rider leaves).</summary>
        static Piece Whole(Placed pl, float cut) =>
            new Piece { mesh = RealFir.Meshes[pl.idx], sub = 0, mat = RealFir.StandaloneOf(pl.idx), m = pl.m, cut = cut };

        // ---- snapped by a fast hit (TreeBreak): young, thin trees and dead snags; big old trunks stand

        /// <summary>The thickest trunk (radius where it leaves the snow, m) a fast hit snaps; a dead snag's dry wood goes up to SnagGirth.</summary>
        public const float SnapGirth = 0.22f, SnagGirth = 0.4f;
        readonly Dictionary<Vector2Int, Matrix4x4> stumps = new Dictionary<Vector2Int, Matrix4x4>();
        readonly List<Matrix4x4> stumpL = new List<Matrix4x4>();

        /// <summary>Would a fast enough hit snap the tree in this cell (and is it a dead snag)?</summary>
        public bool Snappable(int x, int z, out bool snag)
        {
            var pl = Get(x, z);
            if (pl.kind != Kind.Tree) { snag = false; return false; }
            snag = real && RealFir.IsSnag(pl.idx);
            if (snag) return pl.girth < SnagGirth;
            if (real && RealFir.IsYoung(pl.idx)) return pl.girth < SnapGirth;   // a young tree, away from the piste
            // the young ones: the smaller trees of their stand (their own share of the height, Growth's `pick`),
            // and of those only the thin ones - so high up, where the deep snow leaves only slim tops showing,
            // more go; down where the trees stand on their whole trunks, hardly any
            return Slope.Hash01(x, z, 4) < YoungShare && pl.girth < SnapGirth;
        }
        /// <summary>The share of the trees in a stand that count as young (the smallest).</summary>
        public const float YoungShare = 0.4f;

        /// <summary>The same, for the tree behind this collider (false if it is not one of ours).</summary>
        public bool Snappable(GameObject hit, out bool snag)
        {
            snag = false;
            foreach (var kv in live) if (kv.Value == hit) return Snappable(kv.Key.x, kv.Key.y, out snag);
            return false;
        }

        /// <summary>A tree snapped `at` metres above the snow, for TreeBreak to throw down.</summary>
        public struct Snapped { public Vector3 foot, up; public float vis, girth, at; }

        /// <summary>
        /// Snaps the tree behind this collider `at` metres above the snow: it is gone from the forest for the run and
        /// a jagged stump stands in its place. Hands back the pieces it was drawn with, cut off below the break
        /// (realistic trees; the classic ones go over whole), for the top to fall.
        /// </summary>
        public bool Snap(GameObject hit, float at, List<Piece> into, out Snapped info)
        {
            info = default;
            foreach (var kv in live)
            {
                if (kv.Value != hit) continue;
                var pl = Get(kv.Key.x, kv.Key.y);
                if (pl.kind != Kind.Tree) return false;
                felled.Add(kv.Key);
                live.Remove(kv.Key);
                hit.SetActive(false);
                spareTree.Enqueue(hit);
                if (!real) at = 0.25f;   // the classic firs' trunks are short stubs: they go at the foot
                Vector3 up = ((Vector3)pl.m.GetColumn(1)).normalized;
                info = new Snapped { foot = pl.foot, up = up, vis = pl.vis, girth = pl.girth, at = at };
                if (real) into.Add(Whole(pl, pl.cut + at / pl.ys));
                else foreach (var part in treeParts[pl.idx])
                    into.Add(new Piece { mesh = part.mesh, sub = part.sub, mat = part.mat, m = pl.m * part.local, cut = 0f });
                // the stump: from a little under the snow up to the break, as thick as the trunk there
                float h = at + 0.35f;
                stumps[kv.Key] = Matrix4x4.TRS(pl.foot - up * 0.35f, Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, Slope.Hash01(kv.Key.x, kv.Key.y, 12) * 360f, 0f),
                                               new Vector3(pl.girth * 1.05f, h / TreeBreak.StumpTop, pl.girth * 1.05f));
                lastCell = new Vector2Int(int.MinValue, int.MinValue);
                Refresh(false);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Where a blow coming from a point meets the tree or rock behind this collider as it is drawn (the
        /// colliders are rough shapes round them): a fir's branches, a rock's face. Worked out from the models'
        /// bounds alone (their meshes are not kept readable), so marks left there sit on what you see rather
        /// than floating on an invisible capsule. False if the collider is not one of ours.
        /// </summary>
        public bool SurfaceOf(Collider hit, Vector3 from, out Vector3 at, out Vector3 normal)
        {
            at = normal = Vector3.zero;
            if (hit == null) return false;
            foreach (var kv in live)
            {
                if (kv.Value != hit.gameObject) continue;
                var pl = Get(kv.Key.x, kv.Key.y);
                if (pl.kind == Kind.None) return false;
                var part = (pl.kind == Kind.Tree ? treeParts : rockParts)[pl.idx][0];
                var M = pl.m * part.local; var inv = M.inverse;
                var b = part.mesh.bounds;
                if (pl.kind == Kind.Tree)
                {
                    // the model's parts, boxed in the world (the meshes are not authored Y-up, so this is the safe way
                    // round): the narrowest is the trunk, the widest the branches. Below where the branches start a
                    // blow lands on the trunk; above, on a cone fitted inside the branches
                    Bounds trunk = default, crown = default; float tr = float.MaxValue, cr = 0f;
                    foreach (var tp in treeParts[pl.idx])
                    {
                        if (tp.sub >= tp.mesh.subMeshCount) continue;
                        var wb = WorldBox(pl.m * tp.local, tp.mesh.GetSubMesh(tp.sub).bounds);
                        float r = Mathf.Min(wb.extents.x, wb.extents.z);
                        if (r <= 1e-4f) continue;
                        if (r < tr) { tr = r; trunk = wb; }
                        if (r > cr) { cr = r; crown = wb; }
                    }
                    if (cr <= 0f) return false;
                    // only what shows above the snow: a buried tree is met on its crown, narrower where it comes out
                    if (crown.min.y < pl.foot.y)
                    {
                        float shown = Mathf.Clamp01((crown.max.y - pl.foot.y) / Mathf.Max(0.01f, crown.size.y));
                        cr *= shown;
                        var mn = crown.min; mn.y = pl.foot.y; crown.min = mn;
                        if (trunk.min.y < pl.foot.y) { mn = trunk.min; mn.y = pl.foot.y; trunk.min = mn; }
                    }
                    Vector3 d = from - crown.center; d.y = 0f;
                    d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.back;
                    if (from.y < crown.min.y + 0.15f && tr < cr * 0.6f)
                    {
                        float y = Mathf.Clamp(from.y, trunk.min.y + 0.3f, crown.min.y);
                        at = new Vector3(trunk.center.x, y, trunk.center.z) + d * (tr * 0.9f);
                        normal = d;
                    }
                    else
                    {
                        float h = crown.size.y, r0 = cr * 0.75f;
                        float y = Mathf.Clamp(from.y, crown.min.y + 0.15f, crown.min.y + 0.35f * h);
                        at = new Vector3(crown.center.x, y, crown.center.z) + d * (r0 * (1f - (y - crown.min.y) / h));
                        normal = (d * h + Vector3.up * r0).normalized;
                    }
                    return true;
                }
                // a rock: an egg fitted just inside its model's bounds, met on the line from the blow to its middle
                Vector3 ext = b.extents * 0.85f;
                Vector3 o = inv.MultiplyPoint3x4(from), c = b.center;
                Vector3 os = new Vector3((o.x - c.x) / ext.x, (o.y - c.y) / ext.y, (o.z - c.z) / ext.z);
                if (os.sqrMagnitude <= 1f) os = os.sqrMagnitude > 1e-6f ? os.normalized : Vector3.up;   // started inside: straight out
                Vector3 ps = os.normalized;   // aiming at the middle, the ray meets the unit sphere here
                Vector3 pLocal = c + Vector3.Scale(ps, ext);
                Vector3 nLocal = new Vector3(ps.x / ext.x, ps.y / ext.y, ps.z / ext.z);
                at = M.MultiplyPoint3x4(pLocal);
                normal = inv.transpose.MultiplyVector(nLocal).normalized;
                return true;
            }
            return false;
        }

        /// <summary>A box in a mesh's own space, as an upright box round it in the world.</summary>
        static Bounds WorldBox(Matrix4x4 m, Bounds b)
        {
            var w = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                w.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return w;
        }

        GameObject NewCollider(bool tree)
        {
            var g = new GameObject(tree ? "TreeHit" : "RockHit");
            g.transform.SetParent(transform, false);
            if (tree) { var cap = g.AddComponent<CapsuleCollider>(); cap.direction = 1; }
            else g.AddComponent<MeshCollider>().convex = true;
            var ob = g.AddComponent<Obstacle>();
            ob.label = tree ? "Tree" : "Rock";
            return g;
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
