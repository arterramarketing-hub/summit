using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What every person is made from, loaded once and shared: the hero's summit kit as cut for a man and for a
    /// woman (Resources/Rider/Hero.fbx, HeroF.fbx: one skeleton, so either cut's meshes go on any person's bones) and
    /// its clips, the skis and poles (Rides.fbx), and the meshes made from them:
    ///   - the jacket and trousers with their rest pose baked in (the closet's patterns are drawn on it, SnowyCloth);
    ///   - the gear (boots, gloves) with or without the pack, and the head in each shape a look needs: a helmet or a
    ///     beanie (the helmet's own shell drawn in snug round the head, a pompom on top), the neck warmer up or down,
    ///     hair showing under it (a ponytail, two pigtails, or long hair over the collar). The palette is MakeHero.py's
    ///     8 x 8; the people's hair and pompoms take two swatches at its end nobody else uses;
    ///   - cheap copies for distance (Simplify): the whole person in one mesh, every part a slot of colour, merged
    ///     by vertex clustering. Skinned for the camp, or baked into a pose for drawing many at once (the lifts),
    ///     both drawn with Resources/PeopleLit.shader, which colours each slot from a row of the PeoplePalette.
    /// </summary>
    public static class PeopleKit
    {
        public static bool Ok { get; private set; }
        static bool tried;
        public static GameObject Prefab;              // the summit kit's man's cut: every person is built on it (both cuts share one skeleton)
        public static TextAsset Clips;
        static Transform root;
        public static Transform Root => root;

        /// <summary>
        /// The summit kit, as cut for a man (Rider/Hero) or a woman (Rider/HeroF): the same skeleton and clips, its own
        /// jacket, trousers, gear (boots, gloves, pack) and head (face, helmet, goggles, neck warmer), and its bare hands
        /// (unseen: the kit has gloves). Kept at rest out of sight, for its meshes' frames, and a body to pose for baking.
        /// </summary>
        public class Cut
        {
            public GameObject template;
            public SkinnedMeshRenderer body, legs, gear, head;
            public Matrix4x4 meshToModel, modelToMesh;
            public Mesh Body, Legs;                    // with the rest pose in uv2/uv3 (and smoothed normals in the tangents)
            public ClothWind.Shapes BodyWind, LegsWind;   // their shapes for the air to blow into (ClothWind)
            public Mesh gearSrc, headSrc;
            public int[] gearCell, headCell;           // each vertex's palette swatch (MakeHero.py's PAL index)
            public readonly Dictionary<int, Mesh> gears = new Dictionary<int, Mesh>(), heads = new Dictionary<int, Mesh>();
        }
        static readonly Cut[] cuts = new Cut[2];
        /// <summary>The cut a look wears (a woman's, when there is one).</summary>
        public static Cut CutOf(PersonLook l) => l != null && l.female && cuts[1] != null ? cuts[1] : cuts[0];
        public static GameObject Template => cuts[0] != null ? cuts[0].template : null;
        public static Mesh BoardMesh, SkiMesh, PoleMesh;
        public static Material[] BoardSrcMats, SkiSrcMats, PoleSrcMats;
        public static Matrix4x4 BoardLocal, SkiLocal, PoleLocal;   // each piece's mesh frame in the model's (as the files lay them out)
        public static Material ClothTemplate;
        public static Shader Lit;
        public static Texture2D PaletteSrc;
        public static Color[] PaletteBase;

        // The hero's measurements (rest pose, model space: y up, facing -z, the left side +x)
        public static readonly Vector3 HeadCentre = new Vector3(0f, 1.612f, -0.004f);
        public const float ThighLen = 0.40f, ShinLen = 0.39f, AnkleUp = 0.095f, HipUp = 0.93f;

        // the palette's swatches (Tools/MakeHero.py's PAL: 8 across, 8 rows), and two free ones at its end the people use
        public const int CSkin = 0, CHelmet = 1, CFrame = 2, CLens = 3, CGlove = 4, CCuff = 5, CBoot = 6, CSole = 7, CStrap = 8,
                         CBinding = 9, CBStrap = 10, CVent = 11, CNose = 12, CDeckEdge = 13, CBase = 14, CSpare = 15,
                         CPack = 16, CPackSide = 17, CPackAccent = 18, CSeam = 19, CZip = 20, CGaiter = 21, CGaiterBand = 22, CChinstrap = 23,
                         CHair = 60, CPompom = 61, CLip = 62;
        const int PalCols = 8;
        static int PalRows = 8;   // (from the palette itself: MakeHero.py now always writes 8 x 8)
        public static Vector2 CellUv(int c) => new Vector2((c % PalCols + 0.5f) / PalCols, (c / PalCols + 0.5f) / PalRows);
        static int CellOf(Vector2 uv) => Mathf.Clamp(Mathf.FloorToInt(uv.x * PalCols), 0, PalCols - 1) + PalCols * Mathf.Clamp(Mathf.FloorToInt(uv.y * PalRows), 0, PalRows - 1);

        /// <summary>Where a swatch is in the palette texture (found where its uv lands).</summary>
        public static int PalettePixel(int cell, int w, int h)
        {
            int x = Mathf.FloorToInt((cell % PalCols + 0.5f) / PalCols * w), y = Mathf.FloorToInt((cell / PalCols + 0.5f) / PalRows * h);
            return x < w && y < h ? y * w + x : -1;
        }

        public static bool Load()
        {
            // (the scene that held the templates was unloaded: start again)
            if (tried && Ok && (root == null || cuts[0] == null || cuts[0].template == null || cuts[0].Body == null)) { tried = false; Ok = false; }
            if (tried) return Ok;
            tried = true;
            Prefab = Resources.Load<GameObject>("Rider/Hero");
            Clips = Resources.Load<TextAsset>("Rider/HeroAnim");
            ClothTemplate = Resources.Load<Material>("SnowyCloth");
            Lit = Shader.Find("Universal Render Pipeline/Lit");
            PaletteSrc = Resources.Load<Texture2D>("Rider/HeroPalette");
            if (Prefab == null || Clips == null || PaletteSrc == null) { Debug.LogWarning("People: the hero's model, clips or palette is missing"); return false; }
            try { PaletteBase = PaletteSrc.GetPixels(); } catch (UnityException) { Debug.LogWarning("People: the hero's palette can't be read"); return false; }
            PalRows = PaletteSrc.height == 4 ? 3 : Mathf.Max(1, PaletteSrc.height);   // (the first palette was 3 rows, imported as 4)
            var rg = new GameObject("People");
            rg.hideFlags = HideFlags.DontSave;
            if (Application.isPlaying) Object.DontDestroyOnLoad(rg);
            root = rg.transform;
            cuts[0] = MakeCut(Prefab, "PersonTemplate");
            if (cuts[0] == null) return false;
            var fem = Resources.Load<GameObject>("Rider/HeroF");
            cuts[1] = fem != null ? MakeCut(fem, "PersonTemplateF") : null;
            var template = cuts[0].template;
            foreach (var mr in template.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.enabled = false;
                if (mr.name != "Snowboard") continue;
                BoardMesh = mr.GetComponent<MeshFilter>().sharedMesh; BoardSrcMats = mr.sharedMaterials;
                BoardLocal = template.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;
            }
            // the skis and poles, laid out at the Root bone as the hero's are
            var rides = Resources.Load<GameObject>("Rider/Rides");
            if (rides != null)
            {
                var r = Object.Instantiate(rides, template.transform, false);
                r.transform.localPosition = Vector3.zero; r.transform.localRotation = Quaternion.identity; r.transform.localScale = Vector3.one;
                foreach (var mr in r.GetComponentsInChildren<MeshRenderer>(true))
                {
                    mr.enabled = false;
                    var m = template.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;
                    if (mr.name == "Ski") { SkiMesh = mr.GetComponent<MeshFilter>().sharedMesh; SkiSrcMats = mr.sharedMaterials; SkiLocal = m; }
                    else if (mr.name == "Pole") { PoleMesh = mr.GetComponent<MeshFilter>().sharedMesh; PoleSrcMats = mr.sharedMaterials; PoleLocal = m; }
                }
                foreach (var c in r.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            Ok = true;
            return true;
        }

        static Cut MakeCut(GameObject prefab, string name)
        {
            var c = new Cut();
            var t = c.template = Object.Instantiate(prefab, root, false);
            t.name = name;
            t.transform.position = new Vector3(0f, -2000f, 0f);
            foreach (var a in t.GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (var col in t.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            foreach (var smr in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.enabled = false;
                string n = smr.name;
                if (n.StartsWith("HeroBody")) c.body = smr; else if (n.StartsWith("HeroLegs")) c.legs = smr;
                else if (n.StartsWith("HeroGear")) c.gear = smr; else if (n.StartsWith("HeroHead")) c.head = smr;
            }
            if (c.body == null || c.legs == null || c.gear == null) { Debug.LogWarning("People: " + prefab.name + " has no body, legs or gear mesh"); Object.DestroyImmediate(t); return null; }
            c.meshToModel = t.transform.worldToLocalMatrix * c.gear.transform.localToWorldMatrix;
            c.modelToMesh = c.meshToModel.inverse;
            // the jacket and trousers: the rest pose for the patterns (as SnowyClothes bakes the hero's)
            c.Body = RestBaked(t, c.body); c.Legs = RestBaked(t, c.legs);
            // the wind's shapes, once for everyone in this cut (weights are each person's own: Person.Apply). Its model
            // faces -z: the back is +z, the right side -x
            Vector3 back = t.transform.TransformDirection(Vector3.forward), right = t.transform.TransformDirection(Vector3.left), up = t.transform.up;
            c.BodyWind = ClothWind.Add(c.Body, ClothWind.MeshToWorld(c.body), back, right, up, false);
            c.LegsWind = ClothWind.Add(c.Legs, ClothWind.MeshToWorld(c.legs), back, right, up, true);
            c.gearSrc = c.gear.sharedMesh;
            c.headSrc = c.head != null ? c.head.sharedMesh : null;
            if (!c.gearSrc.isReadable || (c.headSrc != null && !c.headSrc.isReadable)) { Debug.LogWarning("People: the hero's gear can't be read"); Object.DestroyImmediate(t); return null; }
            c.gearCell = Cells(c.gearSrc);
            c.headCell = c.headSrc != null ? Cells(c.headSrc) : new int[0];
            return c;
        }

        static int[] Cells(Mesh m)
        {
            var uv = m.uv; var cell = new int[uv.Length];
            for (int i = 0; i < uv.Length; i++) cell[i] = CellOf(uv[i]);
            return cell;
        }

        static Mesh RestBaked(GameObject template, SkinnedMeshRenderer smr)
        {
            var mesh = Object.Instantiate(smr.sharedMesh);
            mesh.name = smr.sharedMesh.name + " (people)";
            var v = mesh.vertices; var n = mesh.normals;
            if (n == null || n.Length != v.Length) { mesh.RecalculateNormals(); n = mesh.normals; }
            var sum = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
            for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); sum[k] = (sum.TryGetValue(k, out var acc) ? acc : Vector3.zero) + n[i]; }
            var pos = new List<Vector3>(v.Length); var nrm = new List<Vector3>(v.Length); var tan = new Vector4[v.Length];
            var m = template.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 s = sum[Key(v[i])].normalized;
                pos.Add(m.MultiplyPoint3x4(v[i]));
                nrm.Add(m.MultiplyVector(s).normalized);
                tan[i] = new Vector4(s.x, s.y, s.z, 1f);
            }
            mesh.SetUVs(2, pos); mesh.SetUVs(3, nrm); mesh.tangents = tan;
            return mesh;
        }

        // ---------------------------------------------------------------- the gear and the head, in every shape a look needs

        /// <summary>The gear (boots, gloves, the pack if the look has one) for a look's cut.</summary>
        public static Mesh Gear(PersonLook l)
        {
            var c = CutOf(l);
            int key = l.pack != null ? 1 : 0;
            if (c.gears.TryGetValue(key, out var have) && have != null) return have;
            var src = c.gearSrc; var cell = c.gearCell;
            var tris = src.triangles;
            var keep = new List<int>(tris.Length);
            for (int t = 0; t < tris.Length; t += 3)
            {
                bool drop = false;
                for (int k = 0; k < 3 && !drop; k++) { int cc = cell[tris[t + k]]; if (l.pack == null && (cc == CPack || cc == CPackSide || cc == CPackAccent)) drop = true; }
                if (!drop) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
            }
            var mesh = Object.Instantiate(src);
            mesh.name = "PersonGear" + key;
            mesh.SetTriangles(keep, 0);
            mesh.RecalculateBounds();
            c.gears[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// The head for a look: the helmet, or a beanie (the helmet's own shell drawn in snug round the head, a pompom
        /// on top), the neck warmer up or down, and hair showing under it.
        /// </summary>
        public static Mesh Head(PersonLook l)
        {
            var c = CutOf(l);
            if (c.headSrc == null) return null;
            int key = l.ShapeKey;
            if (c.heads.TryGetValue(key, out var have) && have != null) return have;
            var src = c.headSrc; var cell = c.headCell;
            var meshToModel = c.meshToModel; var modelToMesh = c.modelToMesh;
            var v = new List<Vector3>(src.vertices); var n = new List<Vector3>(src.normals); var uv = new List<Vector2>(src.uv);
            var w = new List<BoneWeight>(src.boneWeights);
            var tris = src.triangles;
            bool beanie = l.head == PersonHead.Beanie;
            // the helmet's rim is made of frame swatch faces joined to the shell: find them by the faces they share
            var shell = new bool[v.Count];
            for (int i = 0; i < v.Count; i++) { int cc = cell[i]; shell[i] = cc == CHelmet || cc == CVent; }
            var rim = new bool[v.Count];
            for (int t = 0; t < tris.Length; t += 3)
            {
                bool any = shell[tris[t]] || shell[tris[t + 1]] || shell[tris[t + 2]];
                if (!any) continue;
                for (int k = 0; k < 3; k++) if (cell[tris[t + k]] == CFrame) rim[tris[t + k]] = true;
            }
            var keep = new List<int>(tris.Length);
            for (int t = 0; t < tris.Length; t += 3)
            {
                bool drop = false;
                for (int k = 0; k < 3 && !drop; k++) if (beanie && cell[tris[t + k]] == CChinstrap) drop = true;
                if (!drop) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
            }
            if (beanie)
            {
                // the helmet's shell drawn in snug: a knitted beanie over the ears (the vents and rim go with it, the
                // goggles' strap comes in round it); it takes the helmet's swatch, repainted in a knit's colour
                Vector3 hc = HeadCentre;
                for (int i = 0; i < v.Count; i++)
                {
                    int cc = cell[i];
                    bool strap = cc == CStrap && (meshToModel.MultiplyPoint3x4(v[i]) - hc).sqrMagnitude < 0.22f * 0.22f;
                    if (!shell[i] && !rim[i] && !strap) continue;
                    Vector3 p = meshToModel.MultiplyPoint3x4(v[i]) - hc;
                    Vector3 q = strap ? new Vector3(p.x * 0.91f, p.y, p.z * 0.9f) : new Vector3(p.x * 0.91f, p.y * (p.y > 0f ? 0.95f : 1f), p.z * 0.9f);
                    if (cc == CVent) q *= 0.985f;   // (the vents sink into the knit)
                    v[i] = modelToMesh.MultiplyPoint3x4(hc + q);
                    // the vents knitted over (the helmet's swatch); the rim turned up into a cuff (the vents' swatch,
                    // free now, so the goggles' frame keeps its own colour)
                    if (cc == CVent) uv[i] = CellUv(CHelmet);
                    else if (rim[i]) uv[i] = CellUv(CVent);
                }
            }
            if (l.faceShows)
            {
                // the neck warmer pulled down round the neck, into the collar: the chin and cheeks show
                for (int i = 0; i < v.Count; i++)
                {
                    int cc = cell[i];
                    if (cc != CGaiter && cc != CGaiterBand) continue;
                    Vector3 p = meshToModel.MultiplyPoint3x4(v[i]);
                    float k = Mathf.InverseLerp(1.40f, 1.60f, p.y);
                    Vector3 axis = new Vector3(0f, 0f, 0.01f);
                    Vector3 r = new Vector3(p.x - axis.x, 0f, p.z - axis.z) * Mathf.Lerp(1.0f, 1.1f, k);
                    v[i] = modelToMesh.MultiplyPoint3x4(new Vector3(axis.x + r.x, Mathf.Lerp(1.43f, 1.495f, k), axis.z + r.z));
                }
            }
            var add = new Builder(v, n, uv, w, keep, c.head.bones, modelToMesh);
            if (l.faceShows) Face(add, v, cell, keep, meshToModel, modelToMesh, l.female);
            if (beanie && l.hasPompom) add.Ball(HeadCentre + new Vector3(0f, 0.178f, 0.012f), 0.045f, CPompom, "Head");
            // hair from under the helmet's back edge, over the bunched hood (it stands out to 22 cm behind the neck)
            bool pack = l.pack != null;
            switch (l.hairStyle)
            {
                case PersonHair.Ponytail:
                {
                    var path = new List<Vector3> { new Vector3(0f, 1.545f, 0.135f), new Vector3(0f, 1.50f, 0.205f), new Vector3(0f, 1.44f, 0.258f), new Vector3(0f, 1.37f, 0.266f), new Vector3(0f, 1.30f, 0.25f), new Vector3(0f, 1.245f, 0.226f) };
                    var rad = new List<float> { 0.026f, 0.032f, 0.033f, 0.028f, 0.02f, 0.005f };
                    if (pack) { path.RemoveRange(4, 2); rad.RemoveRange(4, 2); path.Add(new Vector3(0f, 1.33f, 0.265f)); rad.Add(0.006f); }
                    add.Tube(path.ToArray(), rad.ToArray(), 8, CHair);
                    break;
                }
                case PersonHair.Pigtails:
                    for (int sd = -1; sd <= 1; sd += 2)
                        add.Tube(new[] { new Vector3(0.075f * sd, 1.545f, 0.12f), new Vector3(0.12f * sd, 1.49f, 0.18f), new Vector3(0.15f * sd, 1.42f, 0.21f), new Vector3(0.162f * sd, 1.35f, 0.2f), new Vector3(0.165f * sd, 1.29f, 0.183f) },
                                 new[] { 0.022f, 0.024f, 0.021f, 0.016f, 0.005f }, 6, CHair);
                    break;
                case PersonHair.Long:
                    add.Drape(CHair, pack);
                    break;
            }
            var mesh = new Mesh { name = "PersonHead" + key };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
            mesh.boneWeights = w.ToArray();
            mesh.bindposes = src.bindposes;
            mesh.SetTriangles(keep, 0);
            mesh.RecalculateBounds();
            c.heads[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// The face under the goggles, with the neck warmer pulled down: the hero's head is a plain skin shape there (his
        /// warmer comes up to the goggles), which read as a blank stump. A nose from under the goggles' bridge, and lips
        /// (their own swatch, a shade of the person's skin: CLip), each laid on the skin where it is (found by rays from
        /// the front); the helmet's chin straps drawn in against the cheeks instead of standing off where the warmer was.
        /// </summary>
        static void Face(Builder add, List<Vector3> v, int[] cell, List<int> tris, Matrix4x4 meshToModel, Matrix4x4 modelToMesh, bool female)
        {
            // the skin's triangles, in the model's frame (it faces -z)
            var skin = new List<Vector3>();
            float gogLow = float.MaxValue;
            for (int i = 0; i < v.Count; i++)
            {
                int cc = cell[i];
                if (cc != CFrame && cc != CLens) continue;
                Vector3 p = meshToModel.MultiplyPoint3x4(v[i]);
                if (Mathf.Abs(p.x) < 0.02f && p.z < HeadCentre.z - 0.05f) gogLow = Mathf.Min(gogLow, p.y);
            }
            for (int t = 0; t < tris.Count; t += 3)
            {
                if (cell[tris[t]] != CSkin || cell[tris[t + 1]] != CSkin || cell[tris[t + 2]] != CSkin) continue;
                for (int j = 0; j < 3; j++) skin.Add(meshToModel.MultiplyPoint3x4(v[tris[t + j]]));
            }
            if (skin.Count == 0 || gogLow == float.MaxValue) return;
            // where the skin is, straight in from the front at (x, y): its z, or NaN (a ray down a seam between two
            // triangles can slip through: then a hair to either side)
            float Front(float x, float y)
            {
                float z = Hit(x, y);
                if (float.IsNaN(z)) z = Hit(x + 0.0007f, y);
                if (float.IsNaN(z)) z = Hit(x - 0.0007f, y);
                return z;
            }
            float Hit(float x, float y)
            {
                var o = new Vector3(x, y, HeadCentre.z - 0.5f); var d = Vector3.forward; float best = float.MaxValue;
                for (int i = 0; i < skin.Count; i += 3)
                {
                    Vector3 e1 = skin[i + 1] - skin[i], e2 = skin[i + 2] - skin[i], pv = Vector3.Cross(d, e2);
                    float det = Vector3.Dot(e1, pv); if (Mathf.Abs(det) < 1e-9f) continue;
                    Vector3 tv = o - skin[i]; float u = Vector3.Dot(tv, pv) / det; if (u < 0f || u > 1f) continue;
                    Vector3 qv = Vector3.Cross(tv, e1); float w2 = Vector3.Dot(d, qv) / det; if (w2 < 0f || u + w2 > 1f) continue;
                    float tt = Vector3.Dot(e2, qv) / det; if (tt > 0f && tt < best) best = tt;
                }
                return best == float.MaxValue ? float.NaN : o.z + best;
            }
            float k = female ? 0.9f : 1f;
            // the nose: flush under the goggles' bridge, standing out most at the tip, tucked in at its base
            var nose = new List<(Vector3 c, float rx, float rz)>();
            foreach (var (dy, proud, rx) in new[] { (0.004f, 0.002f, 0.010f), (-0.010f, 0.009f, 0.012f), (-0.022f, 0.014f, 0.016f), (-0.030f, 0.011f, 0.018f), (-0.036f, 0.002f, 0.014f) })
            {
                float y = gogLow + dy * k, z = Front(0f, y);
                if (float.IsNaN(z)) return;
                float rz = 0.016f;
                nose.Add((new Vector3(0f, y, z + rz - proud * k), rx * k, rz));
            }
            add.Lump(nose, CSkin);
            // the lips: a soft band, the corners up a touch (a half smile)
            float my = gogLow - 0.058f * k, mw = 0.021f * k;
            var lip = new List<(Vector3 c, float rx, float rz)>();
            for (int i = 0; i <= 6; i++)
            {
                float u = -1f + 2f * i / 6f, x = u * mw, y = my + 0.003f * u * u, z = Front(x, y);
                if (float.IsNaN(z)) return;
                float th = 0.0042f * k * Mathf.Sqrt(Mathf.Max(0.15f, 1f - u * u));
                lip.Add((new Vector3(x, y, z + 0.003f - 0.0022f * (1f - u * u)), th, 0.003f));
            }
            add.Band(lip, CLip);
            // the chin straps, in against the cheeks
            for (int i = 0; i < cell.Length; i++)   // (the head's own vertices: the nose and lips come after them)
            {
                if (cell[i] != CChinstrap) continue;
                Vector3 p = meshToModel.MultiplyPoint3x4(v[i]);
                if (p.y > gogLow) continue;
                Vector3 d = new Vector3(p.x - HeadCentre.x, 0f, p.z - HeadCentre.z);
                v[i] = modelToMesh.MultiplyPoint3x4(new Vector3(HeadCentre.x, p.y, HeadCentre.z) + d * 0.88f);
            }
        }

        /// <summary>Extra skinned pieces added to the gear: hair and pompoms, made in the model's frame at rest.</summary>
        class Builder
        {
            readonly List<Vector3> v, n; readonly List<Vector2> uv; readonly List<BoneWeight> w; readonly List<int> tris;
            readonly int head, neck, chest;
            readonly Matrix4x4 modelToMesh;
            public Builder(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<BoneWeight> w, List<int> tris, Transform[] bones, Matrix4x4 modelToMesh)
            {
                this.v = v; this.n = n; this.uv = uv; this.w = w; this.tris = tris; this.modelToMesh = modelToMesh;
                int Find(string s) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == s) return i; return 0; }
                head = Find("Head"); neck = Find("Neck"); chest = Find("UpperChest");
            }
            /// <summary>The bones a point of hair hangs from: the head above the helmet's back edge, then the neck, then the back.</summary>
            BoneWeight Weigh(Vector3 p)
            {
                float y = p.y;
                if (y > 1.5f) return new BoneWeight { boneIndex0 = head, weight0 = 1f };
                if (y > 1.42f) { float k = (y - 1.42f) / 0.08f; return new BoneWeight { boneIndex0 = head, weight0 = k, boneIndex1 = neck, weight1 = 1f - k }; }
                float j = Mathf.Clamp01((y - 1.3f) / 0.12f);
                return new BoneWeight { boneIndex0 = neck, weight0 = 0.3f + 0.4f * j, boneIndex1 = chest, weight1 = 0.7f - 0.4f * j };
            }
            int Add(Vector3 p, Vector3 nrm, int cell, BoneWeight bw)
            {
                v.Add(modelToMesh.MultiplyPoint3x4(p)); n.Add(modelToMesh.MultiplyVector(nrm).normalized); uv.Add(CellUv(cell)); w.Add(bw);
                return v.Count - 1;
            }
            public void Ball(Vector3 c, float r, int cell, string bone)
            {
                var bw = new BoneWeight { boneIndex0 = head, weight0 = 1f };
                const int rings = 5, segs = 8;
                int top = Add(c + Vector3.up * r, Vector3.up, cell, bw);
                var idx = new int[rings - 1, segs];
                for (int i = 1; i < rings; i++)
                    for (int s = 0; s < segs; s++)
                    {
                        float a = Mathf.PI * i / rings, b = 2f * Mathf.PI * s / segs;
                        // a little shaggy: knitted yarn, not a ball bearing
                        float rr = r * (1f + 0.12f * Mathf.Sin(s * 2.7f + i * 1.9f));
                        var d = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                        idx[i - 1, s] = Add(c + d * rr, d, cell, bw);
                    }
                int bot = Add(c + Vector3.down * r, Vector3.down, cell, bw);
                for (int s = 0; s < segs; s++)
                {
                    int s1 = (s + 1) % segs;
                    tris.Add(top); tris.Add(idx[0, s1]); tris.Add(idx[0, s]);
                    for (int i = 0; i < rings - 2; i++) { Quad(idx[i, s], idx[i, s1], idx[i + 1, s1], idx[i + 1, s]); }
                    tris.Add(bot); tris.Add(idx[rings - 2, s]); tris.Add(idx[rings - 2, s1]);
                }
            }
            void Quad(int a, int b, int c, int d) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d); }
            /// <summary>A shape down the face (a nose): rings across it (x wide, z deep) at each point, ends closed; on the head.</summary>
            public void Lump(List<(Vector3 c, float rx, float rz)> rows, int cell) => Run(rows, cell, true);
            /// <summary>A shape across the face (lips): rings in the y-z plane (y tall, z deep) at each point, ends closed; on the head.</summary>
            public void Band(List<(Vector3 c, float rx, float rz)> rows, int cell) => Run(rows, cell, false);
            void Run(List<(Vector3 c, float rx, float rz)> rows, int cell, bool down)
            {
                const int segs = 8;
                var bw = new BoneWeight { boneIndex0 = head, weight0 = 1f };
                var ring = new int[rows.Count, segs];
                for (int i = 0; i < rows.Count; i++)
                    for (int s = 0; s < segs; s++)
                    {
                        float a = 2f * Mathf.PI * s / segs;
                        var (c, r1, r2) = rows[i];
                        Vector3 d = down ? new Vector3(Mathf.Cos(a) * r1, 0f, -Mathf.Sin(a) * r2) : new Vector3(0f, Mathf.Cos(a) * r1, -Mathf.Sin(a) * r2);
                        Vector3 nn = down ? new Vector3(Mathf.Cos(a) / r1, 0f, -Mathf.Sin(a) / r2) : new Vector3(0f, Mathf.Cos(a) / r1, -Mathf.Sin(a) / r2);
                        ring[i, s] = Add(c + d, nn.normalized, cell, bw);
                    }
                int e = rows.Count - 1;
                Vector3 along0 = (rows[0].c - rows[1].c).normalized, along1 = (rows[e].c - rows[e - 1].c).normalized;
                int c0 = Add(rows[0].c, along0, cell, bw), c1 = Add(rows[e].c, along1, cell, bw);
                for (int i = 0; i < e; i++)
                    for (int s = 0; s < segs; s++)
                    {
                        int s1 = (s + 1) % segs;
                        Quad(ring[i, s], ring[i + 1, s], ring[i + 1, s1], ring[i, s1]);
                    }
                for (int s = 0; s < segs; s++) { int s1 = (s + 1) % segs; tris.Add(c0); tris.Add(ring[0, s1]); tris.Add(ring[0, s]); tris.Add(c1); tris.Add(ring[e, s]); tris.Add(ring[e, s1]); }
            }
            /// <summary>A tapering tube of hair along the points (a ponytail, a pigtail).</summary>
            public void Tube(Vector3[] path, float[] radius, int segs, int cell)
            {
                var ring = new int[path.Length, segs];
                for (int i = 0; i < path.Length; i++)
                {
                    Vector3 t = (path[Mathf.Min(i + 1, path.Length - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                    Vector3 a = Vector3.Cross(t, Vector3.right).normalized, b = Vector3.Cross(t, a);
                    var bw = Weigh(path[i]);
                    for (int s = 0; s < segs; s++)
                    {
                        float ang = 2f * Mathf.PI * s / segs;
                        // flattened a touch across the back, the way a ponytail lies
                        var d = a * Mathf.Cos(ang) * 0.8f + b * Mathf.Sin(ang) * 1.15f;
                        ring[i, s] = Add(path[i] + d * radius[i], d.normalized, cell, bw);
                    }
                }
                for (int i = 0; i < path.Length - 1; i++)
                    for (int s = 0; s < segs; s++)
                    {
                        int s1 = (s + 1) % segs;
                        Quad(ring[i, s], ring[i + 1, s], ring[i + 1, s1], ring[i, s1]);
                    }
                // the ends closed
                int e = path.Length - 1;
                var c0 = Add(path[0], -(path[1] - path[0]).normalized, cell, Weigh(path[0]));
                var c1 = Add(path[e], (path[e] - path[e - 1]).normalized, cell, Weigh(path[e]));
                for (int s = 0; s < segs; s++) { int s1 = (s + 1) % segs; tris.Add(c0); tris.Add(ring[0, s]); tris.Add(ring[0, s1]); tris.Add(c1); tris.Add(ring[e, s1]); tris.Add(ring[e, s]); }
            }
            /// <summary>
            /// Long hair: a curtain from under the helmet's back edge, lying over the hood and down the back to the
            /// shoulder blades, round to behind the ears; the ends uneven; both faces drawn.
            /// </summary>
            public void Drape(int cell, bool pack)
            {
                // each row: height, half-width, how far back it lies (on the neck's axis, z 0.016)
                var rows = new List<Vector3> { new Vector3(1.548f, 0.112f, 0.148f), new Vector3(1.50f, 0.132f, 0.215f), new Vector3(1.44f, 0.152f, 0.258f), new Vector3(1.38f, 0.162f, 0.266f) };
                if (!pack) { rows.Add(new Vector3(1.32f, 0.168f, 0.245f)); rows.Add(new Vector3(1.265f, 0.168f, 0.215f)); }
                const int cols = 11;
                const float z0 = 0.016f;
                var outer = new int[rows.Count, cols]; var inner = new int[rows.Count, cols];
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        float a = Mathf.Lerp(-1.25f, 1.25f, c / (cols - 1f));
                        var row = rows[r];
                        float y = row.x;
                        if (r == rows.Count - 1) y += 0.035f * Mathf.Abs(Mathf.Sin(c * 2.3f)) + 0.02f * Mathf.Abs(Mathf.Sin(a));   // the ends uneven, shorter at the sides
                        var p = new Vector3(Mathf.Sin(a) * row.y, y, z0 + Mathf.Cos(a) * (row.z - z0));
                        var d = new Vector3(Mathf.Sin(a) * (row.z - z0), 0f, Mathf.Cos(a) * row.y).normalized;
                        var bw = Weigh(p);
                        outer[r, c] = Add(p, d, cell, bw);
                        inner[r, c] = Add(p - d * 0.012f, -d, cell, bw);
                    }
                for (int r = 0; r < rows.Count - 1; r++)
                    for (int c = 0; c < cols - 1; c++)
                    {
                        Quad(outer[r, c], outer[r + 1, c], outer[r + 1, c + 1], outer[r, c + 1]);
                        Quad(inner[r, c + 1], inner[r + 1, c + 1], inner[r + 1, c], inner[r, c]);
                    }
            }
        }

        // ---------------------------------------------------------------- the colours

        /// <summary>The gear palette for a look: the hero's own, repainted with the look's gear and skin.</summary>
        public static void PaintPalette(PersonLook l, Color[] px, int w, int h)
        {
            System.Array.Copy(PaletteBase, px, Mathf.Min(px.Length, PaletteBase.Length));
            void Set(int cell, Color c, float smooth = -1f)
            {
                int i = PalettePixel(cell, w, h);
                if (i < 0 || i >= px.Length || c.a <= 0f) return;
                px[i] = new Color(c.r, c.g, c.b, smooth >= 0f ? smooth : px[i].a);
            }
            void Item(Wardrobe.Item it, int a, int b = -1, int c = -1)
            {
                if (it == null) return;
                Set(a, it.a, it.smooth); if (b >= 0) Set(b, it.b); if (c >= 0) Set(c, it.c);
            }
            Set(CSkin, l.skin);
            Set(CLip, new Color(l.skin.r * 0.80f, l.skin.g * 0.56f, l.skin.b * 0.56f), 0.35f);   // (the lips: a shade of the skin)
            if (l.head == PersonHead.Beanie) { Set(CHelmet, l.beanie, 0.05f); Set(CVent, l.beanie * 0.8f, 0.05f); }   // (the vents' swatch is the beanie's folded cuff)
            else Item(l.helmet, CHelmet, CVent);
            Item(l.goggles, CLens, CFrame);
            Item(l.gloves, CGlove, CCuff);
            Item(l.boots, CBoot);
            if (l.pack != null) Item(l.pack, CPack, CPackSide, CPackAccent);
            Set(CHair, l.hair, 0.3f);
            Set(CPompom, l.pompom, 0.02f);
            // the neck warmer, its band a little darker
            Set(CGaiter, l.gaiter, 0.1f);
            Set(CGaiterBand, l.gaiter * 0.7f, 0.1f);
        }

        // ---------------------------------------------------------------- the people material for distance

        static Material lodMat;
        public static Texture2D Atlas { get; private set; }
        public const int Slots = 16, Rows = 1024;   // (a row a person: the lifts draw a new look each lap, so rows are reused oldest first)
        public const int SSkin = 0, SJacket = 1, SJacket2 = 2, SPants = 3, SHead = 4, SLens = 5, SFrame = 6, SGlove = 7, SBoot = 8,
                         SBoard = 9, SDark = 10, SPack = 11, SHair = 12, SGaiter = 13, SMetal = 14, SPompom = 15;
        static readonly Color[] atlasPx = new Color[Slots * Rows];
        static readonly Color[] rowBuf = new Color[Slots];
        static readonly Dictionary<PersonLook, int> rowOf = new Dictionary<PersonLook, int>();
        static readonly PersonLook[] rowLook = new PersonLook[Rows];
        // who still needs each row: how many people wear its look (Hold), and when it was last asked for, counted in
        // rows handed out since (RowsMade): a row is only given to a new look when nobody wears its own and nothing has
        // drawn it for RowKeep new rows. (It used to go round the rows oldest first: the lifts' riders bring a new look
        // every lap, 64 a minute at the camp, and after a quarter of an hour the camp's people were drawn far off in
        // rows that now held lift riders' colours: their outfits flickered as they went near and far.)
        static readonly int[] rowHeld = new int[Rows], rowAsked = new int[Rows];
        public const int RowKeep = 256;
        static int nextRow, atlasDirty;
        static bool toldFull;
        public static int RowsInUse => rowOf.Count;
        public static int RowsMade { get; private set; }
        public static int RowsHeld { get { int n = 0; foreach (var h in rowHeld) if (h > 0) n++; return n; } }

        /// <summary>The instanced, slot-coloured material every distant person shares (Resources/PeopleLit.shader).</summary>
        public static Material LodMaterial
        {
            get
            {
                if (lodMat != null) return lodMat;
                // (from the instancing material in Resources: a build keeps the shader's instanced variants only because
                // that asset uses them; a material made here from the bare shader would draw the lifts' riders as nothing)
                var asset = Resources.Load<Material>("PeopleLit");
                var sh = asset != null ? asset.shader : Resources.Load<Shader>("PeopleLit");
                if (sh == null) { Debug.LogWarning("People: Resources/PeopleLit.shader is missing"); sh = Lit; }
                if (asset == null) Debug.LogWarning("People: Resources/PeopleLit.mat is missing: a player build drops the riders' instanced shader (PeopleAssets.Ensure makes it)");
                Atlas = new Texture2D(Slots, Rows, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "PeoplePalette" };
                for (int i = 0; i < atlasPx.Length; i++) atlasPx[i] = Color.grey;
                Atlas.SetPixels(atlasPx); Atlas.Apply(false);
                lodMat = asset != null ? new Material(asset) : new Material(sh);
                lodMat.name = "PeopleLit"; lodMat.enableInstancing = true;
                lodMat.SetTexture("_Palette", Atlas);
                lodMat.SetFloat("_Rows", Rows);
                return lodMat;
            }
        }

        /// <summary>
        /// A row of the palette for this look (each slot's colour, sheen in alpha): the instance's colours far off. Ask
        /// for it each time it's drawn (that keeps it): one nobody has asked for in a while may go to a new look, and
        /// then asking again gives the look a row of its own again.
        /// </summary>
        public static int Row(PersonLook l)
        {
            if (rowOf.TryGetValue(l, out int r)) { rowAsked[r] = RowsMade; return r; }
            _ = LodMaterial;
            r = FreeRow();
            if (rowLook[r] != null) rowOf.Remove(rowLook[r]);
            rowLook[r] = l; rowOf[l] = r; rowHeld[r] = 0;
            RowsMade++; rowAsked[r] = RowsMade;
            WriteRow(l, r);
            // just this row into the picture (sent with the rest at the frame's Flush)
            System.Array.Copy(atlasPx, r * Slots, rowBuf, 0, Slots);
            Atlas.SetPixels(0, r, Slots, 1, rowBuf);
            atlasDirty++;
            return r;
        }

        /// <summary>A row for a new look: one nobody wears and nothing has drawn lately, looking on from the last one given.</summary>
        static int FreeRow()
        {
            int oldest = -1, oldestAt = int.MaxValue;
            for (int k = 0; k < Rows; k++)
            {
                int c = (nextRow + k) % Rows;
                if (rowHeld[c] > 0) continue;
                if (rowLook[c] == null || RowsMade - rowAsked[c] >= RowKeep) { nextRow = (c + 1) % Rows; return c; }
                if (rowAsked[c] < oldestAt) { oldestAt = rowAsked[c]; oldest = c; }
            }
            // every row worn or drawn lately: the one drawn longest ago (never one somebody wears)
            if (!toldFull) { toldFull = true; Debug.LogWarning($"People: all {Rows} palette rows in use ({RowsHeld} worn): the one drawn longest ago goes to a new look"); }
            if (oldest < 0) oldest = nextRow;
            nextRow = (oldest + 1) % Rows;
            return oldest;
        }

        /// <summary>Keep a look's row for as long as someone wears it (Person: from Dress until dressed again or gone).</summary>
        public static void Hold(PersonLook l) { if (l != null) rowHeld[Row(l)]++; }
        /// <summary>No longer worn by this one (Hold's other half).</summary>
        public static void Let(PersonLook l) { if (l != null && rowOf.TryGetValue(l, out int r) && rowHeld[r] > 0) rowHeld[r]--; }

        /// <summary>The colours a look's row holds (Slots of them), for the checks.</summary>
        public static void RowColours(PersonLook l, Color[] into) => Fill(l, into, 0);

        /// <summary>Send the palette's changed rows to the GPU (once a frame at most).</summary>
        public static void Flush()
        {
            if (atlasDirty == 0 || Atlas == null) return;
            Atlas.Apply(false);
            atlasDirty = 0;
        }

        static void WriteRow(PersonLook l, int r) => Fill(l, atlasPx, r * Slots);

        static void Fill(PersonLook l, Color[] atlasPx, int o)
        {
            Color S(Color c, float smooth) => new Color(c.r, c.g, c.b, smooth);
            Color Avg(Wardrobe.Item it) => it == null ? Color.grey : it.Patterned && it.pattern != Wardrobe.Pattern.Block ? Color.Lerp(it.a, it.b, it.pattern == Wardrobe.Pattern.Stripes ? 0.4f : 0.35f) : it.a;
            atlasPx[o + SSkin] = S(l.skin, 0.25f);
            atlasPx[o + SJacket] = S(Avg(l.jacket), l.jacket != null && l.jacket.smooth >= 0f ? l.jacket.smooth : 0.35f);
            atlasPx[o + SJacket2] = l.jacket != null && l.jacket.pattern == Wardrobe.Pattern.Block ? S(l.jacket.b, 0.35f) : atlasPx[o + SJacket];
            atlasPx[o + SPants] = S(Avg(l.pants), 0.2f);
            atlasPx[o + SHead] = l.head == PersonHead.Beanie ? S(l.beanie, 0.05f) : S(l.helmet != null ? l.helmet.a : Color.white, l.helmet != null && l.helmet.smooth >= 0f ? l.helmet.smooth : 0.55f);
            atlasPx[o + SLens] = S(l.goggles != null ? l.goggles.a : new Color(1f, 0.6f, 0.2f), 0.92f);
            atlasPx[o + SFrame] = S(l.goggles != null && l.goggles.b.a > 0f ? l.goggles.b : new Color(0.06f, 0.06f, 0.07f), 0.45f);
            atlasPx[o + SGlove] = S(l.gloves != null ? l.gloves.a : new Color(0.12f, 0.12f, 0.14f), 0.2f);
            atlasPx[o + SBoot] = S(l.boots != null ? l.boots.a : new Color(0.2f, 0.18f, 0.16f), 0.3f);
            atlasPx[o + SBoard] = S(l.board != null ? l.board.a : Color.blue, l.board != null && l.board.smooth >= 0f ? l.board.smooth : 0.6f);
            atlasPx[o + SDark] = S(new Color(0.07f, 0.07f, 0.08f), 0.35f);
            atlasPx[o + SPack] = S(l.pack != null ? l.pack.a : new Color(0.13f, 0.14f, 0.16f), 0.25f);
            atlasPx[o + SHair] = S(l.hair, 0.3f);
            atlasPx[o + SGaiter] = S(l.gaiter, 0.1f);
            atlasPx[o + SMetal] = S(new Color(0.72f, 0.73f, 0.76f), 0.7f);
            atlasPx[o + SPompom] = S(l.pompom, 0.02f);
        }

        /// <summary>The distance slot of a gear swatch (-1: too fine to keep far off).</summary>
        public static int SlotOfCell(int cell)
        {
            switch (cell)
            {
                case CSkin: return SSkin;
                case CHelmet: case CVent: return SHead;
                case CFrame: case CStrap: return SFrame;
                case CLens: return SLens;
                case CGlove: case CCuff: return SGlove;
                case CBoot: return SBoot;
                case CSole: case CBinding: case CBStrap: case CDeckEdge: case CBase: return SDark;
                case CPack: case CPackSide: case CPackAccent: return SPack;
                case CHair: return SHair;
                case CPompom: return SPompom;
                case CGaiter: case CGaiterBand: return SGaiter;
                // (the swatches the other riders' clothes and faces added: Val, Breanna, Paul)
                case 24: case 35: case 44: case 32: case 33: case 34: case 37: case 38: return SSkin;   // skins, eyes, lips
                case 25: case 36: case 45: case 26: case 51: case 40: return SHair;                       // hair, moustaches, a scrunchie
                case 27: case 28: case 41: case 42: case 47: return SBoot;                                // sneakers, slippers, ski boots
                case 29: case 48: return SDark;                                                           // snaps, buckles
                case 30: return SMetal;                                                                   // gold
                case 39: return SFrame;                                                                   // clear glasses' frames
                case 46: case 49: return SHead;                                                           // knitted beanies
                case 50: return SGaiter;                                                                  // a balaclava
                default: return -1;   // seams, zips, the chin strap, stitching, logos
            }
        }

        public static int CellAt(Vector2 uv) => CellOf(uv);

        // ---------------------------------------------------------------- one mesh for far off

        /// <summary>
        /// A soup of triangles with a slot on each vertex, merged by vertex clustering: every vertex in the same
        /// cube of the grid (and of the same slot, so colours never bleed) becomes one, at their average place;
        /// faces that collapse go. Normals are smoothed over what is left. With weights, each merged vertex keeps
        /// those of the original nearest to it (good for a skinned body).
        /// </summary>
        public static Mesh Simplify(List<Vector3> pos, List<int> slot, List<int> tris, float cell, string name, List<BoneWeight> weights = null, Matrix4x4? toMesh = null)
        {
            var map = new Dictionary<long, int>();
            var vid = new int[pos.Count];
            // only what is drawn: vertices no face uses (a pack taken off, a helmet's strap) are left out
            var used = new bool[pos.Count];
            foreach (var k in tris) if (k >= 0 && k < used.Length) used[k] = true;
            var sum = new List<Vector3>(); var count = new List<int>(); var vslot = new List<int>();
            float inv = 1f / cell;
            for (int i = 0; i < pos.Count; i++)
            {
                if (slot[i] < 0 || !used[i]) { vid[i] = -1; continue; }
                var p = pos[i];
                long k = ((long)(Mathf.FloorToInt(p.x * inv) + 4096) << 40) | ((long)(Mathf.FloorToInt(p.y * inv) + 4096) << 26) | ((long)(Mathf.FloorToInt(p.z * inv) + 4096) << 12) | (long)(slot[i] & 0xfff);
                if (!map.TryGetValue(k, out int id)) { id = sum.Count; map[k] = id; sum.Add(Vector3.zero); count.Add(0); vslot.Add(slot[i]); }
                vid[i] = id; sum[id] += p; count[id]++;
            }
            var outPos = new Vector3[sum.Count];
            for (int i = 0; i < outPos.Length; i++) outPos[i] = sum[i] / count[i];
            BoneWeight[] outW = null;
            if (weights != null)
            {
                outW = new BoneWeight[outPos.Length];
                var best = new float[outPos.Length];
                for (int i = 0; i < best.Length; i++) best[i] = float.MaxValue;
                for (int i = 0; i < pos.Count; i++)
                {
                    int id = vid[i]; if (id < 0) continue;
                    float d = (pos[i] - outPos[id]).sqrMagnitude;
                    if (d < best[id]) { best[id] = d; outW[id] = weights[i]; }
                }
            }
            var outTris = new List<int>(tris.Count / 3);
            var seen = new HashSet<long>();
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = vid[tris[t]], b = vid[tris[t + 1]], c = vid[tris[t + 2]];
                if (a < 0 || b < 0 || c < 0 || a == b || b == c || a == c) continue;
                // the same face twice (both sides of a thin shell merged): once is enough
                int lo = Mathf.Min(a, Mathf.Min(b, c)), hi = Mathf.Max(a, Mathf.Max(b, c)), mid = a + b + c - lo - hi;
                long key = ((long)lo << 42) | ((long)mid << 21) | (long)hi;
                // (keep the winding: one of each orientation can stay, a double-sided sheet)
                long wk = key * 2 + (((a == lo && b == mid) || (b == lo && c == mid) || (c == lo && a == mid)) ? 1 : 0);
                if (!seen.Add(wk)) continue;
                outTris.Add(a); outTris.Add(b); outTris.Add(c);
            }
            var nrm = new Vector3[outPos.Length];
            for (int t = 0; t < outTris.Count; t += 3)
            {
                Vector3 a = outPos[outTris[t]], b = outPos[outTris[t + 1]], c = outPos[outTris[t + 2]];
                var f = Vector3.Cross(b - a, c - a);
                nrm[outTris[t]] += f; nrm[outTris[t + 1]] += f; nrm[outTris[t + 2]] += f;
            }
            var uvs = new Vector2[outPos.Length];
            for (int i = 0; i < outPos.Length; i++) { nrm[i] = nrm[i].sqrMagnitude > 1e-12f ? nrm[i].normalized : Vector3.up; uvs[i] = new Vector2((vslot[i] + 0.5f) / Slots, 0f); }
            if (toMesh.HasValue)
            {
                var m = toMesh.Value;
                for (int i = 0; i < outPos.Length; i++) { outPos[i] = m.MultiplyPoint3x4(outPos[i]); nrm[i] = m.MultiplyVector(nrm[i]).normalized; }
            }
            var mesh = new Mesh { name = name };
            if (outPos.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = outPos; mesh.normals = nrm; mesh.uv = uvs;
            if (outW != null) mesh.boneWeights = outW;
            mesh.SetTriangles(outTris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Everything a posed person shows, as one soup in the given frame: the skinned meshes as they are posed now,
        /// and the board, skis and poles where they hang. Each vertex gets its distance slot.
        /// </summary>
        public static void Gather(Person p, Matrix4x4 worldToFrame, List<Vector3> pos, List<int> slot, List<int> tris)
        {
            var baked = new Mesh();
            foreach (var smr in p.Skins)
            {
                if (smr == null || !smr.gameObject.activeInHierarchy) continue;
                smr.BakeMesh(baked);
                var m = worldToFrame * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                var v = baked.vertices; var uv = baked.uv; var t = baked.triangles;
                int o = pos.Count;
                bool gear = smr == p.GearSkin || smr == p.HeadSkin, legs = smr == p.LegsSkin;
                for (int i = 0; i < v.Length; i++)
                {
                    pos.Add(m.MultiplyPoint3x4(v[i]));
                    if (gear) slot.Add(SlotOfCell(CellOf(uv[i])));
                    else if (legs) slot.Add(SPants);
                    else
                    {
                        // the jacket: below a colour block's line, its second colour (rest height, from uv2)
                        slot.Add(SJacket);
                    }
                }
                if (!gear && !legs)
                {
                    var rest = new List<Vector3>(); smr.sharedMesh.GetUVs(2, rest);
                    if (rest.Count == v.Length) for (int i = 0; i < v.Length; i++) if (rest[i].y < 1.3f) slot[o + i] = SJacket2;
                }
                foreach (var k in t) tris.Add(o + k);
            }
            Object.DestroyImmediate(baked);
            foreach (var mr in p.Rigid)
            {
                if (mr == null || !mr.gameObject.activeInHierarchy || !mr.enabled) continue;
                var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                var m = worldToFrame * mr.transform.localToWorldMatrix;
                var v = mesh.vertices; var uv = mesh.uv;
                int o = pos.Count;
                bool isPole = mesh == PoleMesh;
                var mats = mr.sharedMaterials;
                for (int i = 0; i < v.Length; i++) { pos.Add(m.MultiplyPoint3x4(v[i])); slot.Add(SDark); }
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    string mn = s < mats.Length && mats[s] != null ? mats[s].name : "";
                    bool paint = mn.StartsWith("PersonBoard") || mn.StartsWith("Board");
                    var t = mesh.GetTriangles(s);
                    foreach (var k in t)
                    {
                        tris.Add(o + k);
                        if (isPole) slot[o + k] = SMetal;
                        else if (paint) slot[o + k] = SBoard;
                        else if (uv != null && k < uv.Length) { int sl = SlotOfCell(CellOf(uv[k])); slot[o + k] = sl >= 0 ? sl : SDark; }
                    }
                }
            }
        }
    }
}
