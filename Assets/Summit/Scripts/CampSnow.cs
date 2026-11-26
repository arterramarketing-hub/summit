using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The camp's snow (CampSnow.cs, part of Camp): what the snow underfoot looks like by place, and the piles of it
    /// you walk on, not through.
    ///
    /// The ground: the terrain's snow shader (Resources/SnowSurface.shader) draws the camp from a map of it, painted
    /// here each build: 0.5 m a texel over the camp, R how trodden the snow is (the paths' edges, round the fire, the
    /// lodge's steps, the gate, the lookout, the snowman, the waxing stand, and the ways people walk between them,
    /// boot prints in it), G how dirty and wet (the fire's melt and ash, the steps' foot, the gate), B shovelled (a
    /// path dug from the fire past the snowman to the lookout, banked either side), A how much of it is the camp's (all
    /// of it inside, none past the gate or into the forest). The rest is untouched snow, soft and smooth. No grooming
    /// anywhere in it. The boot prints are Resources/Camp/SnowPrints.png (Tools/MakeSnowPrints.py).
    ///
    /// The piles: every heap of snow the walker can reach (the lodge's drifts against its terrace walls, its
    /// "SnowPile" mesh; the camp's mounds round its edges; the banks along the paved path and the shovelled one) is
    /// baked each build into a height grid of 0.2 m from its own drawn mesh. The walker stands on it (FloorAt: up a
    /// gentle one and over it) and is stopped where it is steeper than MaxPileSlope or stands up off the snow (OffPiles:
    /// sliding along it, as against a wall), so nobody walks into or through a pile of snow.
    /// </summary>
    public partial class Camp
    {
        /// <summary>The steepest snow you walk up (rise over run: 0.75 is about 37 degrees); steeper stops you.</summary>
        public const float MaxPileSlope = 0.75f;
        const float PileCell = 0.2f;

        /// <summary>
        /// A pile of snow's surface, as a grid of heights (NaN off it) in a frame of its own: the world's, or the lodge's
        /// (its drifts: in its own frame, heights above its floor).
        /// </summary>
        public class Pile
        {
            public string name;
            public Transform frame;   // (null: the world's)
            public float x0, z0;      // the grid's corner, in the frame
            public int nx, nz;
            public float[] top;
            public float tallest;    // the most it stands above the snow round it
            public Vector3 middle;   // its highest point (world)
            /// <summary>Cell (i, j) in the world: its place and the snow's top there (y NaN off the pile).</summary>
            public Vector3 World(int i, int j)
            {
                var l = new Vector3(x0 + i * PileCell, 0f, z0 + j * PileCell);
                var w = frame != null ? frame.TransformPoint(l) : l;
                float y = top[j * nx + i];
                w.y = float.IsNaN(y) ? float.NaN : (frame != null ? frame.position.y + y : y);
                return w;
            }
        }
        readonly List<Pile> piles = new List<Pile>();
        /// <summary>Test tools: the piles of snow the walker meets.</summary>
        public IReadOnlyList<Pile> Piles => piles;

        static Texture2D campMap, campPrints;
        static Material dugMat;
        static Color32[] campPx;
        const float MapCell = 0.5f;
        static readonly int CampMapId = Shader.PropertyToID("_CampMap"), CampPrintsId = Shader.PropertyToID("_CampPrints"), CampRectId = Shader.PropertyToID("_CampRect");

        /// <summary>The camp's snow, at the end of a build: the shovelled path, the piles baked, the ground's map painted.</summary>
        void CampSnow()
        {
            piles.Clear();
            var dug = ShovelledPath();
            BakePiles();
            PaintGround(dug);
        }

        // ---------------------------------------------------------------- the piles

        void BakePiles()
        {
            // the lodge's drifts against its terrace walls: their grids, cast onto the drawn drifts by Tools/MakeLodge.py
            // (Resources/Camp/LodgePiles.txt; the lodge's meshes themselves can't be read on the phone)
            if (UseLodge && chalet != null)
            {
                var txt = Resources.Load<TextAsset>("Camp/LodgePiles");
                if (txt != null) LodgePiles(txt.text);
            }
            // the mounds round the camp (Mounds: spheres, squashed and sunk): their shape exactly
            var mounds = root.Find("Mounds");
            if (mounds != null) foreach (Transform m in mounds) Mound(m);
            // the banks along the paved path and the shovelled one (meshes made here: their triangles)
            foreach (Transform t in root)
                if (t.name == "PathBanks" || t.name == "PathBanksLower" || t.name == "DugBanks")
                {
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) Banks(t.name, mf.sharedMesh, t);
                }
        }

        void LodgePiles(string text)
        {
            var lines = text.Split('\n');
            int k = 0, n = 0;
            while (k < lines.Length)
            {
                var head = lines[k++].Trim();
                if (!head.StartsWith("pile ")) continue;
                var f = head.Split(' ');
                var p = new Pile { name = "SnowPile" + (n++), frame = chalet, x0 = Num(f[1]), z0 = Num(f[2]), nx = int.Parse(f[3]), nz = int.Parse(f[4]) };
                p.top = new float[p.nx * p.nz];
                for (int j = 0; j < p.nz && k < lines.Length; j++)
                {
                    var row = lines[k++].Trim().Split(' ');
                    for (int i = 0; i < p.nx; i++) p.top[j * p.nx + i] = i < row.Length && row[i] != "-" ? Num(row[i]) : float.NaN;
                }
                Finish(p);
            }
        }

        static float Num(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A mound (a sphere primitive, scaled flat and turned about the up axis, its middle sunk into the snow).</summary>
        void Mound(Transform m)
        {
            var c = m.position;
            Vector3 r = m.lossyScale * 0.5f;
            float reach = Mathf.Max(r.x, r.z);
            var p = new Pile { name = "Mound" + piles.Count, x0 = c.x - reach - PileCell, z0 = c.z - reach - PileCell };
            p.nx = p.nz = Mathf.CeilToInt((2f * reach + 2f * PileCell) / PileCell) + 1;
            p.top = new float[p.nx * p.nz];
            var inv = Quaternion.Inverse(m.rotation);
            for (int j = 0; j < p.nz; j++)
                for (int i = 0; i < p.nx; i++)
                {
                    var l = inv * new Vector3(p.x0 + i * PileCell - c.x, 0f, p.z0 + j * PileCell - c.z);
                    float q = 1f - (l.x * l.x) / (r.x * r.x) - (l.z * l.z) / (r.z * r.z);
                    p.top[j * p.nx + i] = q > 0f ? c.y + r.y * Mathf.Sqrt(q) : float.NaN;
                }
            Finish(p);
        }

        /// <summary>A bank's mesh (its own triangles, in the world), laid onto a grid: the highest of it over each point.</summary>
        void Banks(string name, Mesh mesh, Transform t)
        {
            var v = mesh.vertices; var tri = mesh.triangles;
            for (int k = 0; k < v.Length; k++) v[k] = t.TransformPoint(v[k]);
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var q in v) b.Encapsulate(q);
            var p = new Pile { name = name, x0 = b.min.x - PileCell, z0 = b.min.z - PileCell };
            p.nx = Mathf.CeilToInt((b.size.x + 2f * PileCell) / PileCell) + 1;
            p.nz = Mathf.CeilToInt((b.size.z + 2f * PileCell) / PileCell) + 1;
            p.top = new float[p.nx * p.nz];
            for (int k = 0; k < p.top.Length; k++) p.top[k] = float.NaN;
            for (int k = 0; k + 2 < tri.Length; k += 3)
            {
                Vector3 A = v[tri[k]], B = v[tri[k + 1]], C = v[tri[k + 2]];
                int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(A.x, Mathf.Min(B.x, C.x)) - p.x0) / PileCell));
                int i1 = Mathf.Min(p.nx - 1, Mathf.CeilToInt((Mathf.Max(A.x, Mathf.Max(B.x, C.x)) - p.x0) / PileCell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(A.z, Mathf.Min(B.z, C.z)) - p.z0) / PileCell));
                int j1 = Mathf.Min(p.nz - 1, Mathf.CeilToInt((Mathf.Max(A.z, Mathf.Max(B.z, C.z)) - p.z0) / PileCell));
                float den = (B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z);
                if (Mathf.Abs(den) < 1e-8f) continue;
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = p.x0 + i * PileCell, z = p.z0 + j * PileCell;
                        float wa = ((B.z - C.z) * (x - C.x) + (C.x - B.x) * (z - C.z)) / den;
                        float wb = ((C.z - A.z) * (x - C.x) + (A.x - C.x) * (z - C.z)) / den;
                        float wc = 1f - wa - wb;
                        if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                        float y = wa * A.y + wb * B.y + wc * C.y;
                        int at = j * p.nx + i;
                        if (float.IsNaN(p.top[at]) || y > p.top[at]) p.top[at] = y;
                    }
            }
            Finish(p);
        }

        /// <summary>A pile's height over the snow round it and its highest point, and in the list if any of it is snow.</summary>
        void Finish(Pile p)
        {
            float best = float.MinValue;
            for (int j = 0; j < p.nz; j++)
                for (int i = 0; i < p.nx; i++)
                {
                    var w = p.World(i, j);
                    if (float.IsNaN(w.y)) continue;
                    float over = w.y - Slope.DrawnHeight(w.x, w.z);
                    if (over > p.tallest) p.tallest = over;
                    if (w.y > best) { best = w.y; p.middle = w; }
                }
            if (best > float.MinValue) piles.Add(p);
        }

        /// <summary>The top of the piles of snow at (x, z) (world y), or -infinity where there are none.</summary>
        public float PileTop(float x, float z)
        {
            float best = float.NegativeInfinity;
            for (int k = 0; k < piles.Count; k++)
            {
                var p = piles[k];
                float lx = x, lz = z, lift = 0f;
                if (p.frame != null) { var l = p.frame.InverseTransformPoint(new Vector3(x, p.frame.position.y, z)); lx = l.x; lz = l.z; lift = p.frame.position.y; }
                float u = (lx - p.x0) / PileCell, v = (lz - p.z0) / PileCell;
                if (u < 0f || v < 0f || u >= p.nx - 1 || v >= p.nz - 1) continue;
                int i = (int)u, j = (int)v;
                float fu = u - i, fv = v - j;
                float a = p.top[j * p.nx + i], b = p.top[j * p.nx + i + 1], c = p.top[(j + 1) * p.nx + i], d = p.top[(j + 1) * p.nx + i + 1];
                if (float.IsNaN(a) && float.IsNaN(b) && float.IsNaN(c) && float.IsNaN(d)) continue;
                // (off its snow at a corner: the ground under it there, so its edge comes down to the snow over a cell)
                if (float.IsNaN(a)) a = Ground0(p, i, j) - lift;
                if (float.IsNaN(b)) b = Ground0(p, i + 1, j) - lift;
                if (float.IsNaN(c)) c = Ground0(p, i, j + 1) - lift;
                if (float.IsNaN(d)) d = Ground0(p, i + 1, j + 1) - lift;
                float y = Mathf.Lerp(Mathf.Lerp(a, b, fu), Mathf.Lerp(c, d, fu), fv) + lift;
                if (y > best) best = y;
            }
            return best;
        }

        static float Ground0(Pile p, int i, int j)
        {
            var l = new Vector3(p.x0 + i * PileCell, 0f, p.z0 + j * PileCell);
            var w = p.frame != null ? p.frame.TransformPoint(l) : l;
            return Slope.DrawnHeight(w.x, w.z);
        }

        /// <summary>The snow's own height at (x, z): the piles where there are any, the ground elsewhere.</summary>
        float SnowTop(float x, float z) => Mathf.Max(Slope.DrawnHeight(x, z), PileTop(x, z));

        /// <summary>How steep the piles' snow is at (x, z) (rise over run), and which way it rises.</summary>
        public Vector2 PileRise(float x, float z)
        {
            const float e = 0.15f;
            return new Vector2(SnowTop(x + e, z) - SnowTop(x - e, z), SnowTop(x, z + e) - SnowTop(x, z - e)) / (2f * e);
        }

        /// <summary>
        /// The walker's next step (from pos, its feet at `feet`) kept off what it can't walk up: the steep sides of a
        /// pile, and a pile's edge standing up off the snow. Up a gentle pile it goes; against a steep one it slides
        /// along it.
        /// </summary>
        Vector2 OffPiles(Vector2 next, float feet)
        {
            if (piles.Count == 0) return next;
            if (Climbable(next, feet)) return next;
            var from = new Vector2(pos.x, pos.z);
            var move = next - from;
            var rise = PileRise(next.x, next.y);
            if (rise.sqrMagnitude > 1e-6f)
            {
                var up = rise.normalized;
                float into = Vector2.Dot(move, up);
                if (into > 0f) move -= up * into;   // (along it, not into it)
            }
            var slid = from + move;
            return Climbable(slid, feet) ? slid : from;
        }

        bool Climbable(Vector2 at, float feet)
        {
            float top = PileTop(at.x, at.y);
            if (float.IsNegativeInfinity(top) || top <= feet + 0.06f) return true;   // (no pile, or no higher than the feet)
            return top <= feet + 0.3f && PileRise(at.x, at.y).magnitude <= MaxPileSlope;
        }

        /// <summary>Test tools: is this point (world) on the lodge or its terrace (within margin of its outline)?</summary>
        public bool InLodgeOutline(Vector3 w, float margin) => UseLodge && chalet != null && !LodgeClear(w, margin);
        /// <summary>Test tools: would the walker be kept off this point (world) by a tree's trunk?</summary>
        public bool OffTreesCheck(Vector3 w) => (OffTrees(w) - w).sqrMagnitude > 1e-4f;

        // ---------------------------------------------------------------- the shovelled path

        /// <summary>
        /// A path shovelled through the snow from the fire past the snowman to the lookout: the snow packed flat in it,
        /// thrown up in low banks either side (a pile you step over). Returns its middle line (for the ground's map).
        /// </summary>
        List<Vector3> ShovelledPath()
        {
            // (camplayout: from the fire east to the lookout's deck at the cliff's edge, past the snowman on its left)
            var line = Spline(new List<Vector3> { Ground(1.6f, -69.4f), Ground(6f, -67.6f), Ground(11f, -66.8f), Ground(16f, -66.5f), Ground(LookoutX - 2.4f, LookoutZ) }, 8);
            const float half = 0.7f;
            var bv = new List<Vector3>(); var btri = new List<int>(); var buv = new List<Vector2>();
            for (int i = 0; i < line.Count; i++)
            {
                var a = line[Mathf.Max(0, i - 1)]; var b = line[Mathf.Min(line.Count - 1, i + 1)];
                var dir = b - a; dir.y = 0f; dir.Normalize();
                var side = new Vector3(dir.z, 0f, -dir.x);
                // (the banks die away at the path's ends, where it opens out by the fire and the lookout)
                float t = i / (float)(line.Count - 1), ends = Mathf.Clamp01(Mathf.Min(t, 1f - t) / 0.12f);
                foreach (var s in new[] { -1f, 1f })
                    foreach (var (off, h) in new[] { (half - 0.05f, -0.03f), (half + 0.1f, 0.24f), (half + 0.32f, 0.32f), (half + 0.66f, -0.03f) })
                    {
                        var q = line[i] + side * s * off;
                        q.y = Slope.DrawnHeight(q.x, q.z) + Mathf.Lerp(-0.03f, h, ends) + 0.07f * Slope.Noise(q.x * 2.3f, q.z * 2.3f) * (h > 0f ? 1f : 0f);
                        bv.Add(q); buv.Add(new Vector2(q.x, q.z));
                    }
                if (i > 0)
                    for (int s = 0; s < 2; s++)
                        for (int k = 0; k < 3; k++)
                        {
                            int p0 = (i - 1) * 8 + s * 4 + k, p1 = i * 8 + s * 4 + k;
                            if (s == 0) btri.AddRange(new[] { p0, p0 + 1, p1, p0 + 1, p1 + 1, p1 });
                            else btri.AddRange(new[] { p0, p1, p0 + 1, p0 + 1, p1, p1 + 1 });
                        }
            }
            // (in the camp's own snow, as the ground is drawn: its colour through the day and the night, no grooves in it;
            // normal-mapped, so with tangents)
            var mesh = Made(new Mesh { name = "DugBanks" });
            mesh.SetVertices(bv); mesh.SetUVs(0, buv); mesh.SetTriangles(btri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject("DugBanks", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            if (dugMat == null && Surfaces.Snow != null) dugMat = Surfaces.Terrain(Surfaces.Snow, false);   // (made once, kept: SurfaceLook keeps it in step with the weather)
            mr.sharedMaterial = dugMat != null ? dugMat : snowCap;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        // ---------------------------------------------------------------- the ground's map

        void PaintGround(List<Vector3> dug)
        {
            float cx = Cx;
            float x0 = cx - 40f, z0 = -118f, w = 66f, h = 81f;
            int nx = Mathf.RoundToInt(w / MapCell), nz = Mathf.RoundToInt(h / MapCell);
            if (campMap == null || campMap.width != nx || campMap.height != nz)
            {
                campMap = new Texture2D(nx, nz, TextureFormat.RGBA32, false, true) { name = "CampSnowMap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                campPx = new Color32[nx * nz];
            }
            if (campPrints == null) campPrints = Resources.Load<Texture2D>("Camp/SnowPrints");

            // where people walk and stand: (a, b) a line between two places (or a point, a == b), its width either side
            // fully trodden, and how far out the treading thins to nothing
            var trod = new List<(Vector2 a, Vector2 b, float full, float fade, float how)>();
            var dirt = new List<(Vector2 a, Vector2 b, float full, float fade, float how)>();
            Vector2 G(float dx, float z) => new Vector2(cx + dx, z);
            Vector2 F(Vector3 v) => new Vector2(v.x, v.z);
            Vector2 L(float lx, float lz) { var v = chalet != null ? chalet.TransformPoint(new Vector3(lx, 0f, lz)) : new Vector3(cx + lx, 0f, -92f + lz); return new Vector2(v.x, v.z); }
            var fire = G(0f, -71f);
            // round the fire (inside its benches and out round them) and its melt
            trod.Add((fire, fire, 5.2f, 3.5f, 1f));
            dirt.Add((fire, fire, 1.9f, 1.1f, 0.85f));
            if (UseLodge && chalet != null)
            {
                var foot = F(stairsFoot);
                trod.Add((foot, foot, 3.6f, 3f, 1f)); dirt.Add((foot, foot, 1.3f, 1.2f, 0.55f));
                var wingFoot = L(-13.2f, 18.2f);
                trod.Add((wingFoot, wingFoot, 2.4f, 2.2f, 0.95f)); dirt.Add((wingFoot, wingFoot, 0.9f, 0.9f, 0.45f));
                trod.Add((wingFoot, fire, 0.7f, 1.6f, 0.75f));     // the wing's steps to the fire
                // along the paved path, either side of its banks: people step off it
                // (campstairs: in two runs, to the top of the stairs down the bank and from their foot to the gate)
                for (int part = 0; part < 2; part++)
                {
                    var path = LodgePathPoints(part == 0).ConvertAll(F);
                    for (int i = 0; i + 1 < path.Count; i++) trod.Add((path[i], path[i + 1], 2.4f, 2.6f, 0.85f));
                }
                // and round the stairs' ends, where people step off them and wait to go up
                var sTop = G(StairX, StairTopZ - StairTopDeck - 0.6f); var sFoot = G(StairX, StairBottomZ + 0.8f);
                trod.Add((sTop, sTop, 2.2f, 2f, 0.9f)); trod.Add((sFoot, sFoot, 2.2f, 2f, 0.9f));
            }
            var gate = G(0f, Slope.GateZ - 2.6f);
            trod.Add((gate, gate, 4.5f, 3f, 1f)); dirt.Add((gate, gate, 1.6f, 1.6f, 0.35f));
            trod.Add((G(-4f, -44f), G(4f, -44f), 1.4f, 2.5f, 0.85f));
            // the start corral (Camp.StartCorral, CampLife's waxing stand): round the rack, the trail map and the stand
            var wax = G(7.2f, Slope.GateZ - 10.4f);
            trod.Add((wax, wax, 1.6f, 1.6f, 0.8f));
            trod.Add((wax, gate, 0.8f, 1.4f, 0.7f));
            trod.Add((G(-8.6f, Slope.GateZ - 8.9f), G(BoardX + 0.8f, BoardZ + 1.6f), 1.6f, 1.8f, 0.85f));   // (the rack to the missions board: CampBoard)
            var snowman = G(SnowmanX, SnowmanZ);
            trod.Add((snowman, snowman, 2.0f, 1.8f, 0.9f));
            var look = G(LookoutX - 2.6f, LookoutZ);
            trod.Add((look, look, 3.0f, 2.2f, 1f));
            trod.Add((fire, gate, 0.8f, 2.2f, 0.7f));            // the fire to the gate, by the path's side
            trod.Add((look, gate, 0.6f, 1.8f, 0.6f));            // the lookout to the gate
            trod.Add((G(2.2f, -61f), snowman, 0.5f, 1.4f, 0.5f));

            float camp0 = x0 + 2f, camp1 = x0 + w - 2f;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    var p = new Vector2(x0 + (i + 0.5f) * MapCell, z0 + (j + 0.5f) * MapCell);
                    // the camp's own snow: all of it inside, none past the gate (the run's) or out into the forest
                    float a = Smooth01((p.x - camp0) / 3f) * Smooth01((camp1 - p.x) / 3f) * Smooth01((p.y - (z0 + 2f)) / 3f) * Smooth01((-39.5f - p.y) / 3f);
                    float n = Slope.Noise(p.x * 0.31f + 4.1f, p.y * 0.29f + 7.3f), n2 = Slope.Noise(p.x * 0.9f + 1.3f, p.y * 0.83f + 5.5f);
                    float r = 0f, g = 0f, b = 0f;
                    foreach (var t in trod) r = Mathf.Max(r, Within(p, t) * t.how);
                    foreach (var t in dirt) g = Mathf.Max(g, Within(p, t) * t.how);
                    // (treading in patches, thinning raggedly at its edges; the dirt in smudges)
                    r = Mathf.Clamp01(r * (0.78f + 0.4f * n) - 0.08f * n2);
                    g = Mathf.Clamp01(g * (0.6f + 0.6f * n2));
                    // the shovelled path: packed flat in it, its edges a hand's width soft
                    float dd = DistToLine(p, dug);
                    b = 1f - Smooth01((dd - 0.6f) / 0.25f);
                    r = Mathf.Max(r * (1f - b), 0.35f * b * (0.6f + 0.4f * n2));   // (a few prints along it)
                    campPx[j * nx + i] = new Color32((byte)(r * a * 255f + 0.5f), (byte)(g * a * 255f + 0.5f), (byte)(b * a * 255f + 0.5f), (byte)(a * 255f + 0.5f));
                }
            campMap.SetPixels32(campPx);
            campMap.Apply(false);
            Shader.SetGlobalTexture(CampMapId, campMap);
            if (campPrints != null) Shader.SetGlobalTexture(CampPrintsId, campPrints);
            Shader.SetGlobalVector(CampRectId, new Vector4(x0, z0, 1f / w, 1f / h));
        }

        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        static float Within(Vector2 p, (Vector2 a, Vector2 b, float full, float fade, float how) t)
        {
            var ab = t.b - t.a;
            float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - t.a, ab) / ab.sqrMagnitude) : 0f;
            float d = (p - (t.a + ab * u)).magnitude;
            return 1f - Smooth01((d - t.full) / Mathf.Max(0.01f, t.fade));
        }

        static float DistToLine(Vector2 p, List<Vector3> line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 a = new Vector2(line[i].x, line[i].z), b = new Vector2(line[i + 1].x, line[i + 1].z), ab = b - a;
                float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (p - (a + ab * u)).magnitude);
            }
            return best;
        }
    }
}
