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
        // the band of cells with hit shapes round the rider. 8 ahead is at least 45 m: more than the rider covers in a
        // second at top speed (45 m/s), and than the buddy's warning looks ahead (0.9 s: SummitGame); what a rider meets
        // in a tree (TreeBreak) is only ever in the cells next to the board or the thrown body, which the rider follows
        public int sideCells = 5, aheadCells = 8, behindCells = 3;
        public int farSide = 44, farAhead = 88;                         // drawn out to here (~570 m ahead, ~290 m aside)
        const float LodRange = 300f;   // beyond this, trees are drawn as simple low-poly firs
        readonly List<Matrix4x4> distL = new List<Matrix4x4>();
        Mesh lodMesh; Material lodFoliage, lodBark;
        // the rocks far off (with the world's far band on: Farview): each as a cheap stand-in of its own size (FarRock),
        // out to the forest's edge, handing over from the scanned boulder across RockNear..RockFar by distance (both
        // drawn through the band, the shader picking each pixel's: ScanRockLit's _NearFar). Without the band they end
        // at LodRange, as they always did
        public const float RockNear = 240f, RockFar = 285f;
        readonly List<Matrix4x4> rockFarL = new List<Matrix4x4>();
        Mesh farRock; Material farRockMat;
        bool farRocks;   // (the lists were filled with them)

        // ---- which trees: the original low-poly ones, or realistic card trees
        public const string TreesPref = "summit_trees";
        public static bool Realistic => PlayerPrefs.GetInt(TreesPref, 1) == 1;
        bool real;                         // the look this forest was built with
        // realistic trees: card trees out to FadeStart, their pictures beyond FadeEnd, and in between both, each
        // pixel one or the other (Shaders/Fir.hlsl), the pictures' share growing with distance. The band ends where
        // the shadows have faded out (the desktop render asset's 140 m; the phone's stop at 80 m, sooner), so the cards
        // hand over shadowless, like the pictures.
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
            nearL.Clear(); farL.Clear(); richL.Clear(); shadeL.Clear();
            Rebuild();   // (and the rows placed for the old look go: DropAllStrips)
        }

        public enum Kind { None, Tree, Rock }

        /// <summary>What stands in a cell, where, how big, and which model variant.</summary>
        public static Kind At(int cx, int cz, out Vector3 pos, out float size, out int variant)
        {
            pos = Vector3.zero; size = 1f; variant = 0;
            float wx = (cx + 0.5f) * Cell, wz = (cz + 0.5f) * Cell;
            float off = Mathf.Abs(Slope.Across(wx, wz));   // (square to the run: a cliff band's traverse crosses x slantwise)
            float glade = Slope.Noise(wx * 0.016f + 9f, wz * 0.016f);   // stands of forest and open glades
            float density;
            float half = Slope.Half(wz);   // (the groomed run's own half-width here: B4)
            if (off < half - 3f)
                density = glade > 0.78f ? 0.10f : 0.012f;                 // the odd tree island on the piste
            else
                density = Mathf.Lerp(0.16f, 0.62f, Mathf.Clamp01((off - half) / 30f)) * Mathf.Lerp(0.35f, 1.3f, glade);
            // (lifttop: clumps of firs either side of the Summit top's far side, and a few rock outcrops on its open slope)
            float grove = LiftTop.Grove(wx, wz, out float outcrop);
            density = Mathf.Max(density, grove);
            if (wz < 70f && off < 34f) density *= 0.1f;                  // a clean start
            if (Slope.OnPad(wx, wz) || LiftTop.Covers(wx, wz, 3f)) return Kind.None;
            // how many trees grow at this height (Zone): rocks stand where they always did; above the tree line the
            // cells that would have held a tree are open snow, a few of them a boulder (none on the piste)
            bool rock = outcrop > 0f || Slope.Hash01(cx, cz, 8) < (off < Slope.PisteHalf ? 0.25f : 0.12f);
            float grow = rock ? 1f : Mathf.Max(Zone(wx, wz), grove > 0.05f ? 1f : 0f);
            if (outcrop > 0f) density = 1f;
            if (off < Slope.PisteHalf - 3f) grow = Mathf.Min(grow, 1f);   // (never more to hit on the piste than there was)
            if (!rock && grow < 1f && off > Slope.PisteHalf + WildMargin && Slope.Hash01(cx, cz, 19) < Boulders * Mathf.Clamp01(1f - grow / 0.25f)) { rock = true; grow = 1f; }
            float x = wx + (Slope.Hash01(cx, cz, 2) - 0.5f) * Cell * 0.75f;
            float z = wz + (Slope.Hash01(cx, cz, 3) - 0.5f) * Cell * 0.75f;
            // arcadetrees: the open lines through the forest and its glade pockets, where it really stands (rocks too:
            // nothing to hit on a line), and the forest a little thicker away from them
            float lane = outcrop <= 0f && grove <= 0.05f && off > half - 3f ? LaneKeep(x, z) : 1f;   // (from the piste's edge out)
            if (Slope.Hash01(cx, cz, 1) >= density * grow * lane) return Kind.None;
            if (Course.Keep(wx, wz, 1f)) return Kind.None;   // pipe, crevasses, park features
            if (LiftLine.Taken(wx, wz, 3.5f)) return Kind.None;   // ski lifts (LiftLine.cs): the cleared swath under a line, and its stations
            if (Course.Keep(x, z, 0.5f)) return Kind.None;   // (where it really stands, too: the long park features)
            if (Hamlets.Taken(x, z)) return Kind.None;       // hamlets (Hamlets.cs): nothing grows in a house, its fence or its clearing
            if (NightLights.Taken(x, z)) return Kind.None;   // nor at the foot of a floodlight's pole (NightLights.cs)
            if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f || Mathf.Abs(Slope.Kicker(x, z - 14f)) > 0.01f) return Kind.None;   // keep kickers clear
            variant = (int)(Slope.Hash01(cx, cz, 7) * 64f);
            pos = new Vector3(x, Slope.DrawnHeight(x, z), z);
            size = rock ? (outcrop > 0f ? outcrop : 0.8f + 1.4f * Slope.Hash01(cx, cz, 4)) : (0.75f + 0.6f * Slope.Hash01(cx, cz, 4)) * Mathf.Max(Stature(z), grove > 0.05f ? 0.7f : 0f);
            return rock ? Kind.Rock : Kind.Tree;
        }

        // ---- arcadetrees: lines through the trees
        // Either side of the run, two lines run down through the forest the way a rider goes (along the run, so down
        // the fall line, and across the hill with it where a cliff band's traverse swings it): an inner one in the
        // thinner trees beside the piste and an outer one deep in the full forest. Each is a smooth curve, wandering a
        // few metres from side to side over a couple of hundred, never a zig-zag. On a line's core nothing stands (a
        // tree's hit capsule is its crown at the snow, up to a few metres round, so the core is wide enough that the
        // capsules either side still leave a clear way several metres across); beside it the trees thin out and come
        // back to the full forest by its edge, so from inside the forest reads as full and the line as a gap through it.
        // And glade pockets: open stretches tens of metres long along the run, where most of the trees give way.
        // Pure functions of the place, like everything here, so every mountain has them and any thread may ask.

        /// <summary>The lines and glade pockets are cut (off: the forest as it was, for the test tools' before).</summary>
        public static bool LanesOn = true;

        /// <summary>Half the clear core of a line (m, square to the run), where the thinning ends (full forest), and how much is kept just outside the core.</summary>
        public const float LaneCore = 5f, LaneEdge = 8.5f, LaneThin = 0.4f;
        /// <summary>Away from the lines and pockets the forest grows this much thicker, so it still reads as full (and as many trees stand as before, near enough).</summary>
        public const float LaneFill = 1.2f;
        /// <summary>The lines' offsets beyond the groomed run's edge: inner, outer (m), and how far each wanders either way.</summary>
        public static readonly float[] LaneOut = { 17f, 47f }, LaneWander = { 6f, 10f };

        /// <summary>
        /// The middle of line k (0 inner, 1 outer) on side `side` (+1 toward +x, -1 toward -x) at z: how far it is from the
        /// run's middle along x. Measured along x, not square to the run, so where a cliff band's traverse swings the run
        /// across the hill the lines swing with it, the same curve shifted, as smooth as the run is (square to a swinging
        /// run, a point 60 m out lies against a different stretch of it, and the line jumped).
        /// </summary>
        public static float LaneOffset(int side, int k, float z) => LaneOffset(side, k, z, LaneHalf(z));
        static float LaneOffset(int side, int k, float z, float halfX)
        {
            float w = (Slope.Noise(z * 0.0045f + 17.3f * (k + 1), side > 0 ? 61.7f : 83.1f) - 0.5f) * 2f * LaneWander[k];
            return side * (halfX + LaneOut[k] + w);
        }

        /// <summary>
        /// The groomed run's half-width along x as the lines follow it: smoothed over 160 m, so where the run narrows into a
        /// cliff band's gap or opens out below it (B4) or into the bowl, the lines ease out and in over the stretch rather
        /// than jump sideways with it (a hard zig-zag through the trees).
        /// </summary>
        public static float LaneHalf(float z)
        {
            float sum = 0f, w = 0f;
            for (int i = -4; i <= 4; i++) { float k = 5f - Mathf.Abs(i); sum += Slope.HalfAt(z + i * 20f) * k; w += k; }
            return sum / w;
        }

        /// <summary>The middle of line k on side `side` at z, as a point on the map (x, z).</summary>
        public static float LaneX(int side, int k, float z) => Slope.Centre(z) + LaneOffset(side, k, z);

        /// <summary>How far (x, z) is from the middle of the nearest line, square to it (m).</summary>
        public static float LaneDistance(float x, float z)
        {
            float a = x - Slope.Centre(z), half = LaneHalf(z), best = float.MaxValue;
            int side = a < 0f ? -1 : 1;   // (the other side's lines are the whole run's width away)
            for (int k = 0; k < 2; k++) best = Mathf.Min(best, Mathf.Abs(a - LaneOffset(side, k, z, half)));
            return best * Slope.Squash(z);
        }

        /// <summary>The share of the forest kept at (x, z): 0 on a line's core, thinning back to all of it by its edge; less again in a glade pocket.</summary>
        public static float LaneKeep(float x, float z)
        {
            if (!LanesOn) return 1f;
            float d = LaneDistance(x, z);
            float keep = d < LaneCore ? 0f : Mathf.Lerp(LaneThin, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LaneCore, LaneEdge, d)));
            // glade pockets: long along the run, a few tens of metres across
            float pocket = Slope.Noise(Slope.Across(x, z) * 0.03f + 5.5f, z * 0.0085f + 2.1f);
            float glade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.74f, 0.82f, pocket));
            keep *= Mathf.Lerp(1f, 0.22f, glade);
            // and the rest of the forest a little thicker (only where it is no line and no pocket)
            if (d >= LaneEdge && glade <= 0f) keep *= LaneFill;
            return keep;
        }

        /// <summary>
        /// How thickly trees stand round (x, z), 0..1, as At plants them (its density, without the odd cell kept clear
        /// for a feature, a lift or a house): the far field's forest tint (FarField). Pure, so any thread may ask.
        /// </summary>
        public static float Cover(float x, float z)
        {
            float off = Mathf.Abs(Slope.Across(x, z));
            if (off < Slope.PisteHalf - 3f) return 0f;
            float glade = Slope.Noise(x * 0.016f + 9f, z * 0.016f);
            float density = Mathf.Lerp(0.16f, 0.62f, Mathf.Clamp01((off - Slope.PisteHalf) / 30f)) * Mathf.Lerp(0.35f, 1.3f, glade);
            if (z < 70f && off < 34f) density *= 0.1f;
            if (Slope.OnPad(x, z) || LiftTop.Covers(x, z, 3f)) return 0f;
            // (about one in eight of what stands off the piste is a rock; and only so many trees grow at this height,
            // in clumps too fine for the far field to show one by one)
            return Mathf.Clamp01(density * 0.88f / 0.62f * Mathf.Min(1.15f, ZoneCoarse(z)));
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
                if (scan != null)
                {
                    foreach (var parts in rockParts) foreach (var part in parts) part.mat = scan;
                    scan.SetVector("_NearFar", new Vector4(RockNear, RockFar, 1f, 0f));
                    farRockMat = new Material(scan) { name = "ScanRockFar", enableInstancing = true };
                    farRockMat.SetVector("_NearFar", new Vector4(RockNear, RockFar, -1f, 0f));
                    // (far off, the atlas's grain is far below a pixel: its average grey, and the snow on top, are what show)
                    farRockMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    farRockMat.SetTexture("_BumpMap", null);
                    farRockMat.SetColor("_BaseColor", new Color(0.36f, 0.35f, 0.34f));
                    farRock = FarRock();
                }
            }
            if (treeParts.Count == 0) { treeParts.Add(Fallback(FirMesh(), new[] { foliage, bark })); treeH.Add(6.3f); }
            if (lodMesh == null && foliage != null && bark != null)
            {
                lodMesh = FirMesh();
                lodFoliage = new Material(foliage) { enableInstancing = true };
                lodBark = new Material(bark) { enableInstancing = true };
            }
            if (rockParts.Count == 0) { rockParts.Add(Fallback(RockMesh(), new[] { bark })); rockH.Add(1f); }
            // (the classic look's far firs are plain lit: they come in through the band as the rest do)
            if (lodFoliage != null) { Farview.Adopt(lodFoliage); Farview.Adopt(lodBark); }
        }

        /// <summary>
        /// A far rock: an 80-triangle rounded block filling the unit cube round its middle (scaled to each scanned
        /// boulder's own box when it is placed), a little flat-bottomed, as the boulders sit sunk into the snow.
        /// </summary>
        static Mesh FarRock()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                    3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int i)) return i;
                v.Add((v[a] + v[b]) * 0.5f); mid[key] = v.Count - 1; return v.Count - 1;
            }
            var g = new List<int>();
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                g.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            var n = new Vector3[v.Count]; var uv = new Vector2[v.Count]; var tg = new Vector4[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                var d = v[i].normalized;
                // lumpy, and flatter underneath
                float lump = 1f + 0.12f * (Slope.Hash01(i, 3, 41) - 0.5f);
                var p = d * 0.5f * lump;
                if (p.y < -0.2f) p.y = -0.2f + (p.y + 0.2f) * 0.4f;
                v[i] = p; n[i] = d; uv[i] = new Vector2(0.5f, 0.5f);
                var side = Vector3.Cross(Vector3.up, d); if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                tg[i] = new Vector4(side.normalized.x, side.normalized.y, side.normalized.z, 1f);
            }
            var m = new Mesh { name = "FarRock" };
            m.SetVertices(v); m.normals = n; m.uv = uv; m.tangents = tg; m.SetTriangles(g, 0);
            m.RecalculateBounds();
            return m;
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
        // full: how tall the whole tree is, buried part and all (m); k: the model's scale across
        // solid: how far up from the snow its trunk stays thicker than ThinGirth (the solid part a rider crashes into)
        // deco: a sapling in an otherwise empty cell (kind None): drawn, but nothing to hit
        struct Placed { public Kind kind; public int idx; public Matrix4x4 m; public float size; public Vector3 foot; public float vis, rad, cut, girth, ys, full, k, solid; public bool deco; }

        // ---- realistic trees: how tall they grow, and how deep the snow buries them

        /// <summary>0 at the summit .. 1 at the foot of the mountain.</summary>
        public static float Low(float z) => Mathf.Clamp01(1f - Slope.AltitudeFeet(z) / Slope.VerticalFeet);

        // ---- the mountain's zones, by height (Low): from the summit down,
        //   alpine     above the tree line (the glacier's third of the mountain): open snow and rock, no trees
        //   tree line  a band of scattered, stunted firs, crouched in clumps out of the wind
        //   subalpine  glades: clumps of trees with open snow between, the clumps growing and closing up going down
        //   forest     the full forest, as it always was
        //   valley     the densest and tallest trees, in the valley bottom
        // Only the trees change: rocks stand where they always did, and what is kept clear (the start, the course,
        // lifts, hamlets, the camp) is kept clear as before.

        /// <summary>Low() at the tree line (the glacier's foot, about two-thirds of the way up), and where each band below it ends.</summary>
        public const float TreeLine = 0.33f, LineBand = 0.43f, Subalpine = 0.62f, Valley = 0.86f;
        /// <summary>The height of the tree line (world y): the far views draw their forest up to it (FarMountain).</summary>
        public static float TreeLineY => Slope.FallLine(Slope.FinishZ) + (1f - TreeLine) * Slope.Vertical;
        /// <summary>Above the tree line, the share of the would-be tree cells that hold a boulder instead (off the piste; fading out through the tree-line band).</summary>
        public const float Boulders = 0.08f;

        /// <summary>How many of the trees the forest would plant at this height grow here, before clumping (0 .. 1.15).</summary>
        public static float ZoneCoarse(float z)
        {
            float low = Low(z);
            if (low < TreeLine) return 0f;
            if (low < LineBand) return Mathf.Lerp(0.05f, 0.24f, Mathf.InverseLerp(TreeLine, LineBand, low));
            if (low < Subalpine) return Mathf.Lerp(0.24f, 0.9f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LineBand, Subalpine, low)));
            return Mathf.Lerp(0.9f, 1.15f, Mathf.InverseLerp(Subalpine, 0.95f, low));
        }

        /// <summary>
        /// How many of the trees the forest would plant at (x, z) grow there (0 above the tree line, up to about 2 in a
        /// clump high up, 1.15 in the valley): ZoneCoarse, gathered into clumps twenty-odd metres across at the tree line
        /// and in the subalpine glades (open snow between them), evening out into the forest below.
        /// </summary>
        public static float Zone(float x, float z)
        {
            float k = ZoneCoarse(z);
            if (k <= 0f) return 0f;
            float clumpy = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LineBand + 0.04f, Subalpine + 0.06f, Low(z)));
            if (clumpy <= 0f) return k;
            float clump = Slope.Noise(x * 0.045f + 71f, z * 0.045f + 13f);
            return k * Mathf.Lerp(1f, 2.3f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.66f, clump)), clumpy);
        }

        /// <summary>How grown the trees are at this height: stunted at the tree line, full in the forest, tallest in the valley.</summary>
        public static float Stature(float z)
        {
            float low = Low(z);
            if (low < LineBand) return Mathf.Lerp(0.3f, 0.5f, Mathf.InverseLerp(TreeLine, LineBand, low));
            if (low < Subalpine) return Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LineBand, Subalpine, low)));
            return Mathf.Lerp(1f, 1.12f, Mathf.InverseLerp(Valley, 1f, low));
        }

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
        static bool Wild(float wx, float wz) => Mathf.Abs(Slope.Across(wx, wz)) > Slope.PisteHalf + WildMargin;

        /// <summary>
        /// A sapling for an empty cell, or false: the tops of young trees poking out of the snow at the edges of
        /// clearings and among young stands, well away from the piste and clear of everything the course keeps clear.
        /// Decoration only: there is nothing to hit, and the rider goes straight through.
        /// </summary>
        static bool Sapling(int cx, int cz, out Placed pl)
        {
            pl = default;
            float wx = (cx + 0.5f) * Cell, wz = (cz + 0.5f) * Cell;
            if (Mathf.Abs(Slope.Across(wx, wz)) < Slope.PisteHalf + 8f) return false;
            if (wz < 70f) return false;                                   // a clean start
            float r = Slope.Hash01(cx, cz, 13);
            if (r >= 0.4f) return false;                                  // (the most a cell's chance can be: skip the sums)
            Stand(cx, cz, wx, wz, out float pines, out float young, out _, out _, out float edge);
            if (r >= (0.04f + 0.26f * edge + 0.2f * Mathf.Max(0f, young - 0.25f)) * Mathf.Min(1f, Zone(wx, wz))) return false;   // (none above the tree line)
            if (Slope.OnPad(wx, wz) || Course.Keep(wx, wz, 1f) || LiftTop.Covers(wx, wz, 3f)) return false;
            if (LiftLine.Taken(wx, wz, 3.5f)) return false;   // not in a ski lift's cleared swath, like the trees
            float x = wx + (Slope.Hash01(cx, cz, 17) - 0.5f) * Cell * 0.75f;
            float z = wz + (Slope.Hash01(cx, cz, 18) - 0.5f) * Cell * 0.75f;
            if (Course.Keep(x, z, 0.5f)) return false;
            if (Hamlets.Taken(x, z)) return false;            // nor in a hamlet's houses, fence or clearing
            if (LaneKeep(x, z) < 0.5f) return false;          // nor on a line through the trees (arcadetrees)
            if (NightLights.Taken(x, z)) return false;
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
                float st = Stature(p.z); full *= st; sink *= st;
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
                pl.full = full; pl.k = k;
            }
            else if (kind == Kind.Tree && real)
            {
                // a realistic tree: the kind for this height on the mountain, grown and buried for it, a few leaning
                pl.idx = RealFir.Pick(Low(p.z), Slope.Hash01(x, z, 9));
                Growth(p.x, p.z, Slope.Hash01(x, z, 4), out float full, out float sink);
                float st = Stature(p.z); full *= st; sink *= st;
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
                pl.full = full; pl.k = k;
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
                pl.full = pl.vis; pl.k = k;
            }
            if (kind == Kind.Tree) pl.solid = SolidHeight(pl);
            cache[key] = pl;
            return pl;
        }

        // ---- drawing lists, rebuilt when the rider crosses into a new cell

        readonly Dictionary<int, List<Matrix4x4>> nearL = new Dictionary<int, List<Matrix4x4>>(), farL = new Dictionary<int, List<Matrix4x4>>();
        /// <summary>(treeshadow) Trees drawn into the shadow map only: too far off to be drawn as cards (their pictures are),
        /// but the sun low enough that their shadows reach the snow near the rider (CastsFar).</summary>
        readonly Dictionary<int, List<Matrix4x4>> shadeL = new Dictionary<int, List<Matrix4x4>>();
        Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);

        static int Key(Kind k, int idx, int part) => (k == Kind.Tree ? 0 : 4096) + idx * 16 + part;
        List<Part> PartsOf(int key) => key >= 4096 ? rockParts[(key - 4096) / 16] : treeParts[key / 16];

        void Update()
        {
            long t0 = PerfMarks.Now;
            if (!Refresh(false)) PlaceAhead();
            PerfMarks.MarkIfOver("forest cells", t0, 0.5);
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
            foreach (var kv in shadeL)   // trees drawn into the shadow map only (CastsFar)
            {
                int n = kv.Value.Count; if (n == 0) continue;
                var part = PartsOf(kv.Key)[kv.Key & 15];
                int d = (n + 999) / 1000; long t = (long)part.mesh.GetIndexCount(part.sub) / 3 * n;
                draws += d; shadowDraws += d; shadowTris += t; instances += n;
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
            if (farRock != null && rockFarL.Count > 0 && farRocks)
            {
                draws += (rockFarL.Count + 999) / 1000; instances += rockFarL.Count;
                tris += (long)farRock.GetIndexCount(0) / 3 * rockFarL.Count;
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
            DropAllStrips();
            felled.Clear();
            stumps.Clear(); stumpL.Clear();
            damage.Clear(); damagedL.Clear();
            foreach (var kv in live)
            {
                var g = kv.Value; g.SetActive(false);
                (IsTreeHit(g) ? spareTree : spareRock).Enqueue(g);
            }
            live.Clear();
            lastCell = new Vector2Int(int.MinValue, int.MinValue);
            Refresh(true);
        }

        /// <summary>
        /// The trees that cast shadows: those inside the render pipeline's shadow distance and a little past it, so none
        /// pops a shadow on as it nears (the asset fades shadows out over the last part of it). The phone's asset draws
        /// them to 80 m, the desktop's to 140 m (and the camp pulls it in to 80 m while you're there: Camp).
        /// </summary>
        static float ShadowRange
        {
            get
            {
                var urp = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
                return (urp != null ? urp.shadowDistance : 140f) + 10f;
            }
        }
        /// <summary>
        /// (treeshadow) The way the trees' shadows fall over the ground (unit, x and z: away from the sun) and how far a
        /// shadow reaches per metre of tree, with some over for the ground falling away from the sun. 0 with the sun
        /// high (short shadows: the shadow range's margin covers them) or down.
        /// </summary>
        static void SunReach(out Vector2 away, out float perMetre)
        {
            away = Vector2.zero; perMetre = 0f;
            var sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled || sun.shadows == LightShadows.None) return;
            Vector3 to = -sun.transform.forward;   // toward the sun
            if (to.y <= 0.01f) return;
            var flat = new Vector2(-to.x, -to.z);
            if (flat.sqrMagnitude < 1e-6f) return;
            away = flat.normalized;
            perMetre = 1.3f * flat.magnitude / to.y;   // (1 / tan of its height; and 30% over for a slope falling away)
            if (perMetre < 0.4f) perMetre = 0f;        // (under 0.4 m a metre: within the range's 10 m for any tree here)
        }

        /// <summary>The tallest a tree here stands (m), and the furthest a shadow is followed (m): the rows behind that are
        /// looked through for shadows falling on past the rider.</summary>
        const float TallTree = 36f, ShadowReachMax = 260f;

        /// <summary>How far behind the forest's middle (the rider) a shadow still counts: the camera is a few metres back,
        /// looking on down the hill, and no shadow further back is seen.</summary>
        const float ShadowSeenBehind = 14f;

        /// <summary>A tree (dx, dz from the forest's middle, `h` m tall) whose shadow falls on the ground that is seen and
        /// shadowed: within the shadow range of the middle, and not behind the camera.</summary>
        static bool CastsFar(float dx, float dz, float h, Vector2 away, float perMetre, float range2)
        {
            if (perMetre <= 0f) return false;
            float len = Mathf.Min(h * perMetre, ShadowReachMax);
            // the shadow's line, from the tree's foot `len` away from the sun, cut to the ground ahead of -ShadowSeenBehind
            float u0 = 0f, u1 = len, zMin = -ShadowSeenBehind;
            if (Mathf.Abs(away.y) < 1e-4f) { if (dz < zMin) return false; }
            else if (away.y > 0f) u0 = Mathf.Max(u0, (zMin - dz) / away.y);
            else u1 = Mathf.Min(u1, (zMin - dz) / away.y);
            if (u0 > u1) return false;
            // and its nearest point to the middle
            float u = Mathf.Clamp(-(dx * away.x + dz * away.y), u0, u1);
            float px = dx + away.x * u, pz = dz + away.y * u;
            return px * px + pz * pz < range2;
        }

        /// <summary>How far a stump, or a tree's damage (DrawDamaged), is drawn.</summary>
        const float DetailRange = 150f;

        // ---- each row of cells is placed once, as it comes into reach (a Strip): what stands in each cell, its matrices
        // worked out and the lists they go in found. When the rider changes cell the lists are filled from the strips,
        // with nothing looked up again; and on a frame without a new cell the next row ahead is placed (PlaceAhead)

        /// <summary>A tree, rock or sapling in a strip: its cell's x, and where its matrices are in the strip's `mats`:
        /// at mat0 its own (pl.m), then each part's (pl.m times the part's own), then (the classic look's trees) its far
        /// stand-in fir's. `one` and `two`: the lists a sapling's or a realistic tree's own matrix goes in (a sapling's
        /// cards; a realistic tree's rich model and its picture). `h`: how tall it stands (its shadow's reach: CastsFar).</summary>
        struct Stood { public int x, idx, mat0, parts; public Kind kind; public bool deco, far; public float h; public List<Matrix4x4> one, two; }

        /// <summary>A row of cells as placed, x0..x1: what stands in it (the empty cells left out). For each matrix in
        /// `mats`, the list it is drawn from near (casting a shadow) and far.</summary>
        class Strip
        {
            public int x0, x1, n, nm;
            public Stood[] stood = new Stood[48];
            public Matrix4x4[] mats = new Matrix4x4[96];
            public List<Matrix4x4>[] near = new List<Matrix4x4>[96], far = new List<Matrix4x4>[96], shade = new List<Matrix4x4>[96];

            public void Clear() { System.Array.Clear(near, 0, nm); System.Array.Clear(far, 0, nm); System.Array.Clear(shade, 0, nm); n = 0; nm = 0; }

            public int Mat(Matrix4x4 m, List<Matrix4x4> nearList, List<Matrix4x4> farList, List<Matrix4x4> shadeList = null)
            {
                if (nm == mats.Length)
                {
                    System.Array.Resize(ref mats, nm * 2); System.Array.Resize(ref near, nm * 2); System.Array.Resize(ref far, nm * 2); System.Array.Resize(ref shade, nm * 2);
                }
                mats[nm] = m; near[nm] = nearList; far[nm] = farList; shade[nm] = shadeList;
                return nm++;
            }

            public void Add(in Stood e)
            {
                if (n == stood.Length) System.Array.Resize(ref stood, n * 2);
                stood[n++] = e;
            }
        }
        readonly Dictionary<int, Strip> strips = new Dictionary<int, Strip>();
        readonly Stack<Strip> spareStrips = new Stack<Strip>();
        readonly List<int> stripDrop = new List<int>();
        /// <summary>Cells placed past the drawn width each side, so weaving down the hill rarely widens a strip.</summary>
        const int StripSlack = 8;

        /// <summary>The strip for row z, holding at least the cells from..to.</summary>
        Strip StripAt(int z, int from, int to)
        {
            if (!strips.TryGetValue(z, out var s))
            {
                s = spareStrips.Count > 0 ? spareStrips.Pop() : new Strip();
                strips[z] = s;
                PlaceStrip(s, z, from - StripSlack, to + StripSlack);
            }
            else if (from < s.x0 || to > s.x1)
            {
                if (to < s.x0 - 1 || from > s.x1 + 1 || Mathf.Max(to, s.x1) - Mathf.Min(from, s.x0) > 4 * (to - from + 1))
                    PlaceStrip(s, z, from - StripSlack, to + StripSlack);   // somewhere else entirely (a new start): again
                else
                {
                    if (from < s.x0) { int was = s.x0; s.x0 = from - StripSlack; PlaceCells(s, z, s.x0, was - 1); }
                    if (to > s.x1) { int was = s.x1; s.x1 = to + StripSlack; PlaceCells(s, z, was + 1, s.x1); }
                }
            }
            return s;
        }

        void PlaceStrip(Strip s, int z, int x0, int x1)
        {
            s.Clear();
            s.x0 = x0; s.x1 = x1;
            PlaceCells(s, z, x0, x1);
        }

        void PlaceCells(Strip s, int z, int x0, int x1)
        {
            for (int x = x0; x <= x1; x++)
            {
                var pl = Get(x, z);
                if (pl.kind == Kind.None && !pl.deco) continue;
                var e = new Stood { x = x, kind = pl.kind, idx = pl.idx, deco = pl.deco, h = pl.vis, mat0 = s.Mat(pl.m, null, null) };
                if (pl.deco) e.one = ListFor(nearL, Key(Kind.Tree, pl.idx, 0));
                else
                {
                    if (real && pl.kind == Kind.Tree) { e.one = ListFor(richL, Key(Kind.Tree, RealFir.Variants + pl.idx, 0)); e.two = impL[pl.idx]; }
                    var parts = pl.kind == Kind.Tree ? treeParts[pl.idx] : rockParts[pl.idx];
                    for (int i = 0; i < parts.Count; i++)
                    {
                        int key = Key(pl.kind, pl.idx, i);
                        s.Mat(pl.m * parts[i].local, ListFor(nearL, key), ListFor(farL, key), pl.kind == Kind.Tree ? ListFor(shadeL, key) : null);
                    }
                    e.parts = parts.Count;
                    // (the classic look's far firs)
                    if (pl.kind == Kind.Tree && !real && lodMesh != null) { s.Mat(pl.m * Matrix4x4.Scale(Vector3.one * (treeH[pl.idx] / 6.3f)), null, null); e.far = true; }
                    // (a rock's far stand-in, filling its boulder's box)
                    if (pl.kind == Kind.Rock && farRock != null)
                    {
                        var box = parts[0].mesh.bounds;
                        s.Mat(pl.m * parts[0].local * Matrix4x4.TRS(box.center, Quaternion.identity, box.size), null, null);
                        e.far = true;
                    }
                }
                s.Add(e);
            }
        }

        /// <summary>A frame without a new cell: place the next row ahead not placed yet (one a frame), so a crossing
        /// has only the lists to fill.</summary>
        void PlaceAhead()
        {
            if (target == null || lastCell.x == int.MinValue) return;
            int xFrom = lastCell.x - farSide, xTo = lastCell.x + farSide;
            for (int z = lastCell.y + farAhead + 1; z <= lastCell.y + farAhead + 2; z++)
                if (!strips.ContainsKey(z)) { StripAt(z, xFrom, xTo); return; }
        }

        /// <summary>Rows out of reach: their strips back to the spares, and their cells' placements forgotten.</summary>
        void DropStrips(int z0, int z1)
        {
            stripDrop.Clear();
            foreach (var kv in strips) if (kv.Key < z0 || kv.Key > z1) stripDrop.Add(kv.Key);
            foreach (int z in stripDrop)
            {
                var s = strips[z]; strips.Remove(z);
                for (int x = s.x0; x <= s.x1; x++) cache.Remove(new Vector2Int(x, z));
                spareStrips.Push(s);
            }
        }

        void DropAllStrips()
        {
            foreach (var s in strips.Values) spareStrips.Push(s);
            strips.Clear();
        }

        /// <summary>Fill the drawing lists and place the hit shapes for the rider's cell, if it has changed (or `force`).
        /// True if it did.</summary>
        bool Refresh(bool force)
        {
            if (target == null) return false;
            LoadKinds();
            var c = new Vector2Int(Mathf.FloorToInt(target.position.x / Cell), Mathf.FloorToInt(target.position.z / Cell));
            if (!force && c == lastCell) return false;
            lastCell = c;
            foreach (var l in nearL.Values) l.Clear();
            foreach (var l in farL.Values) l.Clear();
            foreach (var l in richL.Values) l.Clear();
            foreach (var l in shadeL.Values) l.Clear();
            distL.Clear();
            rockFarL.Clear();
            damagedL.Clear();
            if (impL != null) foreach (var l in impL) l.Clear();
            // shadows for every tree within the shadow range, so none pops a shadow on as it nears; and (treeshadow) for
            // every tree whose shadow reaches into it: with the sun low (7-8 degrees up, mornings and evenings) a fir's
            // shadow runs 150 m and more, and a tree well out of the range laid one across the run, all at once, as it
            // came into it
            float shadow2 = ShadowRange * ShadowRange, detail2 = DetailRange * DetailRange;
            SunReach(out Vector2 away, out float perMetre);
            float band0 = (FadeStart - FadeSlack) * (FadeStart - FadeSlack), band1 = (FadeEnd + FadeSlack) * (FadeEnd + FadeSlack);
            float rich = (RichEnd + FadeSlack) * (RichEnd + FadeSlack), lod = LodRange * LodRange;
            farRocks = farRock != null && Farview.On;
            float rockFar0 = (RockNear - FadeSlack) * (RockNear - FadeSlack), rockNear1 = (RockFar + FadeSlack) * (RockFar + FadeSlack);
            int zFrom = c.y - behindCells, zTo = c.y + farAhead, xFrom = c.x - farSide, xTo = c.x + farSide;
            // (and with the sun behind, the rows of trees already passed whose shadows fall on down the hill past the
            // rider: into the shadow map only. Before, a passed tree's shadow went from in front of you all at once)
            int shadeFrom = zFrom;
            if (perMetre > 0f && away.y > 0.05f)
                shadeFrom = c.y - Mathf.CeilToInt(Mathf.Min(ShadowReachMax, TallTree * perMetre) * away.y / Cell) - 1;
            for (int z = Mathf.Min(zFrom, shadeFrom); z <= zTo; z++)
            {
                var s = StripAt(z, xFrom, xTo);
                float dzm = (z - c.y) * Cell;
                var stood = s.stood; var mats = s.mats;
                if (z < zFrom)
                {
                    for (int j = 0; j < s.n; j++)
                    {
                        ref var e = ref stood[j];
                        if (e.deco || e.kind != Kind.Tree || e.x < xFrom || e.x > xTo) continue;
                        if (CastsFar((e.x - c.x) * Cell, dzm, e.h, away, perMetre, shadow2))
                            for (int i = e.mat0 + 1; i <= e.mat0 + e.parts; i++) s.shade[i]?.Add(mats[i]);
                    }
                    continue;
                }
                for (int j = 0; j < s.n; j++)
                {
                    ref var e = ref stood[j];
                    if (e.x < xFrom || e.x > xTo) continue;
                    float dxm = (e.x - c.x) * Cell;
                    float d2 = dxm * dxm + dzm * dzm;
                    if (e.deco)
                    {
                        // a sapling: its cards as far as the trees' reach (it fades out with them; no picture beyond)
                        if (d2 <= band1) e.one.Add(mats[e.mat0]);
                        continue;
                    }
                    if (real && e.kind == Kind.Tree && damage.Count > 0 && d2 < detail2 && damage.ContainsKey(new Vector2Int(e.x, z)))
                    {
                        damagedL.Add(new Vector2Int(e.x, z));   // drawn on its own, with its damage (DrawDamaged)
                        continue;
                    }
                    bool casts = d2 < shadow2 || (e.kind == Kind.Tree && CastsFar(dxm, dzm, e.h, away, perMetre, shadow2));
                    if (real && e.kind == Kind.Tree)
                    {
                        // far realistic tree: its picture on two crossed quads; through the band, the cards too
                        if (d2 > band0) e.two.Add(mats[e.mat0]);
                        if (d2 > band1)
                        {
                            // (its picture casts no shadow: the plain tree into the shadow map alone, if its shadow reaches)
                            if (casts) for (int i = e.mat0 + 1; i <= e.mat0 + e.parts; i++) s.shade[i]?.Add(mats[i]);
                            continue;
                        }
                        // close by, the rich tree as well (the plain one below still casts the shadow for both)
                        if (d2 < rich) e.one.Add(mats[e.mat0]);
                    }
                    if (e.kind == Kind.Rock && farRocks)
                    {
                        // a rock with the band on: its stand-in from RockNear out, the boulder in to RockFar
                        if (d2 > rockFar0 && e.far) rockFarL.Add(mats[e.mat0 + 1 + e.parts]);
                        if (d2 > rockNear1) continue;
                    }
                    else if (d2 > lod)
                    {
                        // far away: one cheap fir the same height instead of the full model (rocks are left out)
                        if (e.far && e.kind == Kind.Tree) distL.Add(mats[e.mat0 + 1 + e.parts]);
                        continue;
                    }
                    var lists = casts ? s.near : s.far;
                    for (int i = e.mat0 + 1; i <= e.mat0 + e.parts; i++) lists[i].Add(mats[i]);
                }
            }
            // rows out of reach go (a few kept either side, for a turn back up the hill)
            DropStrips(Mathf.Min(zFrom, shadeFrom) - 4, zTo + 4);
            PlaceColliders(c);
            stumpL.Clear();
            foreach (var kv in stumps)
            {
                float dxm = (kv.Key.x - c.x) * Cell, dzm = (kv.Key.y - c.y) * Cell;
                if (dxm * dxm + dzm * dzm < detail2) stumpL.Add(kv.Value);
            }
            // forget cells well behind (the strips take theirs with them as they go: this is for any looked up on their own)
            if (cache.Count > 30000)
            {
                dropList.Clear();
                foreach (var k in cache.Keys) if (k.y < zFrom - 4 || k.y > zTo + 4) dropList.Add(k);
                foreach (var k in dropList) cache.Remove(k);
            }
            return true;
        }

        /// <summary>
        /// The tree or rock in this cell, felled or snapped: out of its strip and out of whichever drawing lists hold it,
        /// without filling them all again (thousands of cells). Its matrices are found in the lists of its kind.
        /// </summary>
        void Unplace(Vector2Int cell)
        {
            damagedL.Remove(cell);
            if (!strips.TryGetValue(cell.y, out var s)) return;   // (a row out of reach: in no list)
            for (int j = 0; j < s.n; j++)
            {
                var e = s.stood[j];
                if (e.x != cell.x) continue;
                s.stood[j] = s.stood[--s.n];
                if (e.deco) return;
                for (int i = e.mat0 + 1; i <= e.mat0 + e.parts; i++) { DropFrom(s.near[i], s.mats[i]); DropFrom(s.far[i], s.mats[i]); if (s.shade[i] != null) DropFrom(s.shade[i], s.mats[i]); }
                if (e.one != null) DropFrom(e.one, s.mats[e.mat0]);
                if (e.two != null) DropFrom(e.two, s.mats[e.mat0]);
                if (e.far) { DropFrom(distL, s.mats[e.mat0 + 1 + e.parts]); DropFrom(rockFarL, s.mats[e.mat0 + 1 + e.parts]); }
                return;
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
            DrawSet(shadeL, UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly);
            DrawSet(richL, UnityEngine.Rendering.ShadowCastingMode.Off);
            if (stumpL.Count > 0) TreeBreak.DrawStumps(stumpL, real, bark);
            if (damagedL.Count > 0) DrawDamaged();
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
            if (rockFarL.Count > 0 && farRock != null && farRocks)
            {
                var rr = new RenderParams(farRockMat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                for (int i = 0; i < rockFarL.Count; i += 1000)
                    Graphics.RenderMeshInstanced(rr, farRock, 0, rockFarL, Mathf.Min(1000, rockFarL.Count - i), i);
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
                (IsTreeHit(g) ? spareTree : spareRock).Enqueue(g);
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
                    if (tree) TrunkBox(g, pl);
                    else RockBox(g, pl);
                    live[key] = g;
                }
        }

        /// <summary>
        /// A tree's hit shape: its trunk as drawn, and only the trunk. A tapered eight-sided post from where the trunk
        /// comes out of the snow (leaning as the tree does: it leans from its foot under the snow, so buried metres
        /// deep the trunk comes out a metre or two from the spot straight above that foot) up to where it thins to
        /// ThinGirth. The branches round it are no wall: riding into them is a brush (TreeBreak, from CrownAt), and
        /// the thin top above the post, or a small tree's thin trunk, snaps when you hit it (TreeBreak). (It was a
        /// capsule as wide as the crown at the snow, all the way up: riding into a fir's branches was riding into a
        /// tree trunk.)
        /// </summary>
        void TrunkBox(GameObject g, Placed pl)
        {
            var mc = g.GetComponent<MeshCollider>();
            Vector3 axis = ((Vector3)pl.m.GetColumn(1)).normalized, pivot = pl.m.GetColumn(3);
            Vector3 at = axis.y > 0.2f ? pivot + axis * ((pl.foot.y - pivot.y) / axis.y) : pl.foot;
            float h = Mathf.Max(0.6f, pl.solid);
            float r0 = Mathf.Max(0.06f, pl.girth), r1 = Mathf.Clamp(Trunk(pl, h), 0.04f, r0);
            mc.sharedMesh = TrunkHull(r1 / r0);
            g.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, axis));
            g.transform.localScale = new Vector3(r0, h, r0);
        }

        /// <summary>The tree hit shapes (the rest are rocks'), told apart without reading a name (a new string each time).</summary>
        readonly HashSet<GameObject> treeHits = new HashSet<GameObject>();
        bool IsTreeHit(GameObject g) => treeHits.Contains(g);

        static readonly Mesh[] trunkHulls = new Mesh[11];

        /// <summary>A trunk's post, radius 1 at the bottom (y 0) tapering to `taper` at the top (y 1), eight-sided,
        /// with the corners just outside the round (so the flats sit on the drawn bark). One per tenth of taper.</summary>
        static Mesh TrunkHull(float taper)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(taper * 10f), 1, 10);
            if (trunkHulls[i] != null) return trunkHulls[i];
            float t = i / 10f, c = 1f / Mathf.Cos(Mathf.PI / 8f);
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int k = 0; k < 8; k++)
            {
                float a = (k + 0.5f) * Mathf.PI / 4f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * c;
                v.Add(d); v.Add(d * t + Vector3.up);
            }
            for (int k = 0; k < 8; k++)
            {
                int a0 = 2 * k, a1 = 2 * ((k + 1) % 8);
                tri.Add(a0); tri.Add(a0 + 1); tri.Add(a1); tri.Add(a1); tri.Add(a0 + 1); tri.Add(a1 + 1);
                if (k >= 1 && k < 7) { tri.Add(0); tri.Add(2 * k + 2); tri.Add(2 * k); tri.Add(1); tri.Add(2 * k + 1); tri.Add(2 * k + 3); }   // the ends
            }
            var m = new Mesh { name = "TrunkHull" + i };
            m.SetVertices(v); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            trunkHulls[i] = m;
            return m;
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
            var parts = new List<(Mesh, Matrix4x4)>();
            foreach (var part in rockParts[idx]) if (part.sub == 0) parts.Add((part.mesh, part.local));
            m = any ? CliffSpires.ScanHull(parts, b, 0.8f, "RockHull" + idx) : CliffSpires.Frustum(b, 0.8f, "RockHull" + idx);
            rockHulls[idx] = m;
            return m;
        }

        // ---- smashed by an invincible rider

        /// <summary>One piece of a tree or rock as drawn: mesh, sub-mesh, material, and where in the world.</summary>
        /// <summary>A piece of a felled tree or rock; `cut` (model height) hides what was under the snow (0: nothing).</summary>
        /// <summary>... and `top` (model height, 0: none) hides what was above it (a tree whose top had already gone).</summary>
        public struct Piece { public Mesh mesh; public int sub; public Material mat; public Matrix4x4 m; public float cut, top; }
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
                (IsTreeHit(hit) ? spareTree : spareRock).Enqueue(hit);
                if (pl.kind == Kind.None) return false;
                // a tree goes over from where it meets the snow, as tall as it shows (Smasher: 8.5 m a size)
                basePos = pl.foot; size = pl.kind == Kind.Tree ? pl.vis / 8.5f : pl.size;
                if (real && pl.kind == Kind.Tree) into.Add(Topped(Whole(pl, pl.cut), pl, kv.Key));
                else foreach (var part in pl.kind == Kind.Tree ? treeParts[pl.idx] : rockParts[pl.idx])
                    into.Add(new Piece { mesh = part.mesh, sub = part.sub, mat = part.mat, m = pl.m * part.local, cut = pl.kind == Kind.Tree ? pl.cut : 0f });
                damage.Remove(kv.Key);
                Unplace(kv.Key);   // drawn without it from now on
                return true;
            }
            return false;
        }

        /// <summary>
        /// The pieces the rock behind this collider is drawn with (left standing, unlike Fell), so blood can be drawn
        /// over its very surface. False if it is not one of our rocks.
        /// </summary>
        public bool RockPieces(Collider hit, List<Piece> into)
        {
            if (hit == null) return false;
            foreach (var kv in live)
            {
                if (kv.Value != hit.gameObject) continue;
                var pl = Get(kv.Key.x, kv.Key.y);
                if (pl.kind != Kind.Rock) return false;
                foreach (var part in rockParts[pl.idx]) into.Add(new Piece { mesh = part.mesh, sub = part.sub, mat = part.mat, m = pl.m * part.local });
                return true;
            }
            return false;
        }

        /// <summary>A realistic tree taken out of the forest to be knocked down: its rich mesh, in the material that
        /// never dissolves (it is seen from close by, and lies there as the rider leaves).</summary>
        /// <summary>The piece without the top this tree has already lost.</summary>
        Piece Topped(Piece p, in Placed pl, Vector2Int cell)
        {
            if (damage.TryGetValue(cell, out var dm) && dm.top < pl.vis) p.top = pl.cut + dm.top / pl.ys;
            return p;
        }

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
            foreach (var kv in live) if (kv.Value == hit) return Snap(kv.Key, at, into, out info);
            return false;
        }

        /// <summary>The same, for the tree in this cell.</summary>
        public bool Snap(Vector2Int cell, float at, List<Piece> into, out Snapped info)
        {
            info = default;
            var pl = Get(cell.x, cell.y);
            if (pl.kind != Kind.Tree) return false;
            felled.Add(cell);
            var whole = Topped(Whole(pl, pl.cut + at / pl.ys), pl, cell);
            float stood = damage.TryGetValue(cell, out var was) ? was.top : float.MaxValue;
            damage.Remove(cell);
            if (live.TryGetValue(cell, out var hit))
            {
                live.Remove(cell);
                hit.SetActive(false);
                spareTree.Enqueue(hit);
            }
            if (!real) at = 0.25f;   // the classic firs' trunks are short stubs: they go at the foot
            Vector3 up = ((Vector3)pl.m.GetColumn(1)).normalized;
            float girth = real ? Mathf.Max(0.03f, Trunk(pl, at)) : pl.girth;
            info = new Snapped { foot = pl.foot, up = up, vis = Mathf.Min(pl.vis, stood), girth = girth, at = at };
            if (real) into.Add(whole);
            else foreach (var part in treeParts[pl.idx])
                into.Add(new Piece { mesh = part.mesh, sub = part.sub, mat = part.mat, m = pl.m * part.local, cut = 0f });
            // the stump: from a little under the snow up to the break, as thick as the trunk there
            float h = at + 0.35f;
            stumps[cell] = Matrix4x4.TRS(pl.foot - up * 0.35f, Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, Slope.Hash01(cell.x, cell.y, 12) * 360f, 0f),
                                         new Vector3(Mathf.Max(pl.girth, girth) * 1.05f, h / TreeBreak.StumpTop, Mathf.Max(pl.girth, girth) * 1.05f));
            Unplace(cell);   // drawn without it from now on, and its stump with
            stumpL.Add(stumps[cell]);
            return true;
        }

        // ---- the shape of a tree as drawn, for what a rider meets in it (TreeBreak): the trunk is solid, the
        // branches round it are not, and a trunk thinner than ThinGirth snaps

        /// <summary>
        /// The thickest trunk (radius where you meet it, m) that snaps and lets you ride on, with no crash: a small
        /// tree's trunk at the snow, or the thin top of a tall one. Chosen from the forest's spread (BranchTest):
        /// about the thinnest third of the trunks a rider meets at the snow. Thicker is a trunk you crash into
        /// (unless a fast hit snaps a young one or a snag: SnapGirth).
        /// </summary>
        public const float ThinGirth = 0.15f;

        /// <summary>A tree as drawn, for hits: where its trunk leaves the snow and the way it leans, how much of it
        /// shows, and how much of that still stands (less once its top has been clipped off).</summary>
        public struct Tree
        {
            public Vector2Int cell; public int idx; public bool realistic, snag;
            public Vector3 at, up;
            /// <summary>The model's own x and z as they lie in the world (unit): which way round the tree is turned.</summary>
            public Vector3 xAxis, zAxis;
            public float vis, top, girth, solid;
            internal float cut, ys, k, full;
        }

        Tree View(Vector2Int cell, in Placed pl)
        {
            Vector3 axis = ((Vector3)pl.m.GetColumn(1)).normalized, pivot = pl.m.GetColumn(3);
            Vector3 at = axis.y > 0.2f ? pivot + axis * ((pl.foot.y - pivot.y) / axis.y) : pl.foot;
            float top = damage.TryGetValue(cell, out var dm) ? Mathf.Min(pl.vis, dm.top) : pl.vis;
            return new Tree
            {
                cell = cell, idx = pl.idx, realistic = real, snag = real && RealFir.IsSnag(pl.idx), at = at, up = axis,
                xAxis = ((Vector3)pl.m.GetColumn(0)).normalized, zAxis = ((Vector3)pl.m.GetColumn(2)).normalized,
                vis = pl.vis, top = top, girth = pl.girth, solid = Mathf.Max(0.6f, pl.solid), cut = pl.cut, ys = pl.ys, k = pl.k, full = pl.full
            };
        }

        /// <summary>The trees with hit shapes in the cells round p (those near enough to the rider to have one).</summary>
        public void TreesNear(Vector3 p, List<Tree> into)
        {
            into.Clear();
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
            for (int z = cz - 1; z <= cz + 1; z++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!live.ContainsKey(key)) continue;
                    var pl = Get(x, z);
                    if (pl.kind == Kind.Tree) into.Add(View(key, pl));
                }
        }

        /// <summary>The tree in this cell as drawn (false if there is none standing).</summary>
        public bool TreeIn(int x, int z, out Tree t)
        {
            var pl = Get(x, z);
            t = pl.kind == Kind.Tree ? View(new Vector2Int(x, z), pl) : default;
            return pl.kind == Kind.Tree;
        }

        /// <summary>The tree behind this hit shape (false if it is not one of ours).</summary>
        public bool TreeOf(Collider c, out Tree t)
        {
            t = default;
            if (c == null) return false;
            foreach (var kv in live) if (kv.Value == c.gameObject) return TreeIn(kv.Key.x, kv.Key.y, out t);
            return false;
        }

        /// <summary>A point of a realistic tree's own mesh (model space) where it is drawn in the world (the test tools).</summary>
        public static Vector3 ModelToWorld(in Tree t, Vector3 m) => t.at + t.up * ((m.y - t.cut) * t.ys) + t.xAxis * (m.x * t.k) + t.zAxis * (m.z * t.k);
        /// <summary>The model height of what still stands of a realistic tree (the test tools).</summary>
        public static float ModelTop(in Tree t) => t.cut + t.top / t.ys;

        /// <summary>The trunk's radius (m) h metres up from where it leaves the snow.</summary>
        public static float TrunkAt(in Tree t, float h) => t.realistic ? RealFir.TrunkRadius(t.idx, t.cut + Mathf.Max(0f, h) / t.ys) * t.k : t.girth;

        /// <summary>How far its branches reach from the trunk (m) h metres up from the snow: 0 below its lowest branches
        /// and above what still stands.</summary>
        public static float CrownAt(in Tree t, float h)
        {
            if (h < 0f || h > t.top) return 0f;
            if (!t.realistic)
            {
                // the classic low-poly firs: a cone of boughs from a sixth of the way up (Resources/Trees, FirMesh)
                float f = h / Mathf.Max(0.5f, t.vis);
                return f < 0.16f ? 0f : 0.3f * t.vis * (1f - f) / 0.84f;
            }
            float share = (t.cut + h / t.ys) / RealFir.Heights[t.idx];
            return RealFir.CrownRadius(t.idx, share) * t.full;
        }

        float Trunk(in Placed pl, float h) => real ? RealFir.TrunkRadius(pl.idx, pl.cut + Mathf.Max(0f, h) / pl.ys) * pl.k : pl.girth;

        /// <summary>How far up from the snow the trunk stays thicker than ThinGirth (to most of what shows, for a trunk
        /// that never thins that much; for a small tree, thin from the snow up, as far as a rider stands: it is still
        /// solid to a slow push).</summary>
        float SolidHeight(in Placed pl)
        {
            if (!real) return pl.vis * 0.45f;   // the classic firs: a stub of trunk under a cone of boughs
            float cap = pl.vis * 0.9f;
            if (Trunk(pl, 0f) < ThinGirth) return Mathf.Min(cap, 1.8f);
            float h = 0f;
            while (h < cap && Trunk(pl, h + 0.5f) >= ThinGirth) h += 0.5f;
            return Mathf.Min(cap, h);
        }

        // ---- damage a tree keeps for the rest of the run: a gap torn in its branches, its top clipped off

        class Damage
        {
            public float top = float.MaxValue, snow = 1f;
            public readonly Vector4[] gaps = new Vector4[2];   // model space: centre, radius (0: none)
            public int next;
            public readonly MaterialPropertyBlock props = new MaterialPropertyBlock();
        }
        readonly Dictionary<Vector2Int, Damage> damage = new Dictionary<Vector2Int, Damage>();
        readonly List<Vector2Int> damagedL = new List<Vector2Int>();
        static readonly int CutYId = Shader.PropertyToID("_CutY"), CutTopId = Shader.PropertyToID("_CutTop"),
                            Gap0Id = Shader.PropertyToID("_Gap0"), Gap1Id = Shader.PropertyToID("_Gap1"), SnowId = Shader.PropertyToID("_Snow");

        Damage DamageOf(Vector2Int cell)
        {
            if (!damage.TryGetValue(cell, out var dm)) damage[cell] = dm = new Damage();
            return dm;
        }

        /// <summary>How many trees carry damage this run (for the test tools).</summary>
        public int Damaged => damage.Count;

        /// <summary>
        /// Branches torn out where something went through them at `world` (a gap `radius` m across, its edges bared of
        /// their snow): drawn so from now on (realistic trees; the classic ones only shed their snow). The two latest
        /// gaps a tree has are kept.
        /// </summary>
        public void TearBranches(in Tree t, Vector3 world, float radius)
        {
            if (!real) return;
            var pl = Get(t.cell.x, t.cell.y);
            if (pl.kind != Kind.Tree) return;
            var dm = DamageOf(t.cell);
            Vector3 local = pl.m.inverse.MultiplyPoint3x4(world);
            dm.gaps[dm.next] = new Vector4(local.x, local.y, local.z, radius / Mathf.Max(0.05f, pl.k));
            dm.next = (dm.next + 1) % dm.gaps.Length;
            dm.snow = Mathf.Max(0.55f, dm.snow - 0.1f);
            Unlist(t.cell, pl);
        }

        /// <summary>
        /// A tree just damaged: out of the instanced batches and onto the list drawn one by one (DrawDamaged), without
        /// rebuilding every list (thousands of cells: Refresh) as a felled tree does. Its matrices are found in the
        /// lists of its kind and swapped out.
        /// </summary>
        void Unlist(Vector2Int cell, in Placed pl, bool damaged = true)
        {
            if (damagedL.Remove(cell) && damaged) { damagedL.Add(cell); return; }
            Drop(nearL, Key(Kind.Tree, pl.idx, 0), pl.m); Drop(farL, Key(Kind.Tree, pl.idx, 0), pl.m);
            Drop(richL, Key(Kind.Tree, RealFir.Variants + pl.idx, 0), pl.m);
            if (impL != null && pl.idx < impL.Length) DropFrom(impL[pl.idx], pl.m);
            if (damaged) damagedL.Add(cell);
        }

        static void Drop(Dictionary<int, List<Matrix4x4>> set, int key, Matrix4x4 m) { if (set.TryGetValue(key, out var list)) DropFrom(list, m); }
        static void DropFrom(List<Matrix4x4> list, Matrix4x4 m)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == m) { list[i] = list[list.Count - 1]; list.RemoveAt(list.Count - 1); return; }
        }

        /// <summary>
        /// The top of the tree in this cell comes away `at` metres above the snow; the rest stands, broken off there,
        /// for the rest of the run. Hands back its pieces cut off below the break, for the top to fall (realistic trees;
        /// a classic fir has no top to lose: it goes over whole from a stump, as Snap). False if there is none.
        /// </summary>
        public bool ClipTop(Vector2Int cell, float at, List<Piece> into, out Snapped info)
        {
            info = default;
            if (!real) return Snap(cell, 0.25f, into, out info);
            var pl = Get(cell.x, cell.y);
            if (pl.kind != Kind.Tree) return false;
            var dm = DamageOf(cell);
            if (at >= dm.top - 0.5f) return false;   // nothing left up there to clip
            dm.snow = Mathf.Min(dm.snow, 0.6f);   // the whole tree shook
            Vector3 up = ((Vector3)pl.m.GetColumn(1)).normalized;
            info = new Snapped { foot = pl.foot, up = up, vis = Mathf.Min(pl.vis, dm.top), girth = Mathf.Max(0.03f, Trunk(pl, at)), at = at };
            into.Add(Topped(Whole(pl, pl.cut + at / pl.ys), pl, cell));
            dm.top = at;
            Unlist(cell, pl);
            return true;
        }

        /// <summary>The damaged trees near enough to see it, each drawn on its own (its rich mesh, in the material
        /// that never dissolves) with its gaps and missing top.</summary>
        void DrawDamaged()
        {
            foreach (var cell in damagedL)
            {
                if (!damage.TryGetValue(cell, out var dm)) continue;
                var pl = Get(cell.x, cell.y);
                if (pl.kind != Kind.Tree) continue;
                var p = dm.props;
                p.SetFloat(CutYId, pl.cut);
                p.SetFloat(CutTopId, dm.top < pl.vis ? pl.cut + dm.top / pl.ys : 1e6f);
                p.SetVector(Gap0Id, dm.gaps[0]); p.SetVector(Gap1Id, dm.gaps[1]);
                p.SetFloat(SnowId, dm.snow);
                var rp = new RenderParams(RealFir.StandaloneOf(pl.idx)) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true, matProps = p };
                Graphics.RenderMesh(rp, RealFir.Meshes[pl.idx], 0, pl.m);
                // (no collar of torn wood on the broken top: it would stand still while the tree sways and shudders round
                // it. The cut leader and the shaken, bare boughs under it read as broken)
            }
        }

        /// <summary>
        /// Where a blow coming from a point meets the tree or rock behind this collider as it is drawn (the
        /// colliders are rough shapes round them): a fir's branches, a rock's face. Worked out from the models'
        /// bounds alone (their meshes are not kept readable), so marks left there sit on what you see rather
        /// than floating on an invisible capsule. False if the collider is not one of ours.
        /// </summary>
        public bool SurfaceOf(Collider hit, Vector3 from, out Vector3 at, out Vector3 normal) => SurfaceOf(hit, from, out at, out normal, out _);

        /// <summary>As SurfaceOf, and how round the surface is there (m): a trunk's radius, the branches' cone's, a rock's.</summary>
        public bool SurfaceOf(Collider hit, Vector3 from, out Vector3 at, out Vector3 normal, out float round)
        {
            at = normal = Vector3.zero; round = 1f;
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
                        normal = d; round = tr * 0.9f;
                    }
                    else
                    {
                        float h = crown.size.y, r0 = cr * 0.75f;
                        float y = Mathf.Clamp(from.y, crown.min.y + 0.15f, crown.min.y + 0.35f * h);
                        at = new Vector3(crown.center.x, y, crown.center.z) + d * (r0 * (1f - (y - crown.min.y) / h));
                        normal = (d * h + Vector3.up * r0).normalized; round = r0 * (1f - (y - crown.min.y) / h);
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
                Vector3 we = WorldBox(M, new Bounds(c, ext * 2f)).extents;
                round = Mathf.Min(we.x, Mathf.Min(we.y, we.z));
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
            if (tree) treeHits.Add(g);
            g.transform.SetParent(transform, false);
            g.AddComponent<MeshCollider>().convex = true;   // a tree's trunk post, a rock's hull
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
