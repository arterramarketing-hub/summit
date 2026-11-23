using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The board and ski graphics (the closet's GRAPHICS, Wardrobe.Cat.Graphic): one texture a design, painted by
    /// Tools/MakeBoardArt.py into Resources/BoardArt, and the uvs that lay it on the hero's deck, skis and poles.
    ///
    /// The models were made for a flat colour (every vertex of the "Board" material at one swatch), so the uvs are laid
    /// here, once per mesh, when a rider is built: each face of the paint is put on the deck's top sheet, its base or the
    /// rail colour by which way it faces, and its place along and across the deck picks the texel. The game's own board
    /// material (URP Lit) then just takes the design as its _BaseMap: no shader of our own, no new variant to keep.
    ///
    /// Rows of a design's texture (512 x 256), from the bottom up, as MakeBoardArt.py paints them:
    /// 0-7 rail colour, 8-39 a ski's top sheet, 40-47 rail, 48-123 the base, 124-127 rail, 128-255 the deck's top.
    /// </summary>
    public static class BoardArt
    {
        public const string Folder = "BoardArt/";
        const float TexW = 512f, TexH = 256f;
        static readonly Vector2Int Top = new Vector2Int(128, 128), Base = new Vector2Int(48, 76), Ski = new Vector2Int(8, 32);
        /// <summary>Where the rail colour is (the sidewalls, the poles, the tube's trim: a mesh with no uvs lands here too).</summary>
        public static readonly Vector2 RailUv = new Vector2(0.5f, 4f / TexH);

        public enum Kind { Deck, Ski, Solid }

        static readonly Dictionary<(Mesh, Kind), Mesh> mapped = new Dictionary<(Mesh, Kind), Mesh>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static Texture2D thumbs;
        static readonly Dictionary<int, Sprite> thumbSprites = new Dictionary<int, Sprite>();

        /// <summary>A design's texture (Resources/BoardArt/key), or null.</summary>
        public static Texture2D Texture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (textures.TryGetValue(key, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(Folder + key);
            textures[key] = t;
            return t;
        }

        /// <summary>The closet's little board for a design (its place among the designs: GRAPHICS row - 1).</summary>
        public static Sprite Thumb(int n)
        {
            if (n < 0) return null;
            if (thumbSprites.TryGetValue(n, out var s) && s != null) return s;
            if (thumbs == null) thumbs = Resources.Load<Texture2D>(Folder + "BoardArtThumbs");
            if (thumbs == null) return null;
            // two to a row, 256 x 48 a cell, 51 px a row, the first row at the top of the picture
            int r = n / 2, c = n % 2;
            float k = thumbs.height / 512f;
            var rect = new Rect(c * 256f * k, thumbs.height - (r * 51 + 49) * k, 256f * k, 48f * k);
            if (rect.yMin < 0f) return null;
            s = Sprite.Create(thumbs, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = "BoardArtThumb" + n;
            thumbSprites[n] = s;
            return s;
        }

        /// <summary>
        /// Paint a board material with a design (or, for null or a design with no art, back to its plain colour).
        /// Returns true if a design is on it.
        /// </summary>
        public static bool Put(Material m, Wardrobe.Item design, Color plain)
        {
            if (m == null) return false;
            var tex = design != null ? Texture(design.art) : null;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (tex == null) { m.SetColor("_BaseColor", plain); return false; }
            m.SetColor("_BaseColor", Color.white);
            return true;
        }

        /// <summary>
        /// The mesh with its paint's uvs laid for the designs (made once per mesh and kept). `paint` says which of its
        /// submeshes are the board's paint; the others keep their own uvs (the bindings in the gear palette).
        /// </summary>
        public static Mesh Mapped(Mesh src, bool[] paint, Kind kind)
        {
            if (src == null || paint == null) return src;
            if (mapped.TryGetValue((src, kind), out var done) && done != null) return done;
            if (!src.isReadable) return src;
            int subs = src.subMeshCount;
            bool any = false;
            for (int s = 0; s < subs && s < paint.Length; s++) any |= paint[s];
            if (!any) return src;

            var v = src.vertices; var n = src.normals; var tg = src.tangents; var uv = src.uv; var col = src.colors;
            bool hasN = n != null && n.Length == v.Length, hasT = tg != null && tg.Length == v.Length, hasUv = uv != null && uv.Length == v.Length, hasC = col != null && col.Length == v.Length;
            var tris = new int[subs][];
            for (int s = 0; s < subs; s++) tris[s] = src.GetTriangles(s);

            // the paint's own frame: its long axis, which way is up (the bindings sit on top), and across
            var lo = Vector3.one * float.MaxValue; var hi = -lo;
            Vector3 pc = Vector3.zero; int pn = 0; Vector3 oc = Vector3.zero; int on = 0;
            var area = Vector3.zero; var signed = Vector3.zero;
            var inPaint = new bool[v.Length];
            for (int s = 0; s < subs; s++)
            {
                bool p = s < paint.Length && paint[s];
                var t = tris[s];
                for (int i = 0; i < t.Length; i += 3)
                {
                    if (!p) { for (int k = 0; k < 3; k++) { oc += v[t[i + k]]; on++; } continue; }
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    var fn = Vector3.Cross(b - a, c - a);
                    if (hasN) { var avg = n[t[i]] + n[t[i + 1]] + n[t[i + 2]]; if (Vector3.Dot(fn, avg) < 0f) fn = -fn; }
                    area += new Vector3(Mathf.Abs(fn.x), Mathf.Abs(fn.y), Mathf.Abs(fn.z));
                    signed += fn;
                    for (int k = 0; k < 3; k++)
                    {
                        var q = v[t[i + k]];
                        lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
                        if (!inPaint[t[i + k]]) { inPaint[t[i + k]] = true; pc += q; pn++; }
                    }
                }
            }
            if (pn == 0) return src;
            pc /= pn;
            var ext = hi - lo;
            int along = ext.x >= ext.y && ext.x >= ext.z ? 0 : ext.y >= ext.z ? 1 : 2;
            int up = -1;
            for (int a = 0; a < 3; a++) if (a != along && (up < 0 || area[a] > area[up])) up = a;
            int across = 3 - along - up;
            // which way is up: a paint that is one side only (a ski's top sheet) faces it; a closed deck (top, base and
            // rails) faces every way, so there it's the side its bindings stand on
            bool open = Mathf.Abs(signed[up]) > 0.3f * area[up];
            float upSign = open || on == 0 ? Mathf.Sign(signed[up]) : Mathf.Sign((oc / on)[up] - pc[up]);
            if (upSign == 0f) upSign = 1f;
            float len = Mathf.Max(1e-5f, ext[along]), mid = (lo[across] + hi[across]) * 0.5f, width = Mathf.Max(1e-5f, ext[across]);
            // which end is the nose (a ski's tip turns up higher than its tail; a twin deck: either)
            float hiEnd = 0f, loEnd = 0f; int nh = 0, nl = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (!inPaint[i]) continue;
                float f = (v[i][along] - lo[along]) / len;
                if (f > 0.93f) { hiEnd += v[i][up] * upSign; nh++; } else if (f < 0.07f) { loEnd += v[i][up] * upSign; nl++; }
            }
            bool noseAtLo = kind == Kind.Ski && nh > 0 && nl > 0 && loEnd / nl > hiEnd / nh;

            var nv = new List<Vector3>(v); var nn = hasN ? new List<Vector3>(n) : null; var nt = hasT ? new List<Vector4>(tg) : null;
            var nuv = new List<Vector2>(hasUv ? uv : new Vector2[v.Length]); var nc = hasC ? new List<Color>(col) : null;
            var ntris = new int[subs][];
            for (int s = 0; s < subs; s++)
            {
                if (!(s < paint.Length && paint[s])) { ntris[s] = tris[s]; continue; }
                var t = tris[s]; var o = new int[t.Length];
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    var fn = Vector3.Cross(b - a, c - a);
                    if (hasN) { var avg = n[t[i]] + n[t[i + 1]] + n[t[i + 2]]; if (Vector3.Dot(fn, avg) < 0f) fn = -fn; }
                    float mag = fn.magnitude;   // (by hand: the files' meshes are in small units, and Vector3.normalized gives up below 1e-5)
                    float facing = mag > 0f ? fn[up] / mag * upSign : 0f;
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = t[i + k];
                        var q = v[vi];
                        float u01 = Mathf.Clamp01((q[along] - lo[along]) / len); if (noseAtLo) u01 = 1f - u01;
                        float w01 = Mathf.Clamp01((q[across] - mid) / width + 0.5f);
                        Vector2 p;
                        if (kind == Kind.Solid) p = RailUv;
                        else if (kind == Kind.Ski ? facing > -0.2f : facing > 0.45f) p = Place(kind == Kind.Ski ? Ski : Top, u01, w01);   // (a ski's paint is all top sheet, its turned-up tip too)
                        else if (facing < -0.45f && kind == Kind.Deck) p = Place(Base, u01, 1f - w01);
                        else p = RailUv;
                        o[i + k] = nv.Count;
                        nv.Add(q); nuv.Add(p);
                        if (hasN) nn.Add(n[vi]);
                        if (hasT) nt.Add(tg[vi]);
                        if (hasC) nc.Add(col[vi]);
                    }
                }
                ntris[s] = o;
            }
            var m = new Mesh { name = src.name + " (art)" };
            if (nv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(nv);
            if (hasN) m.SetNormals(nn);
            if (hasT) m.SetTangents(nt);
            m.SetUVs(0, nuv);
            if (hasC) m.SetColors(nc);
            m.subMeshCount = subs;
            for (int s = 0; s < subs; s++) m.SetTriangles(ntris[s], s, false);
            m.RecalculateBounds();
            m.UploadMeshData(false);   // (kept readable: the closet's copies and the test tools read it)
            mapped[(src, kind)] = m;
            return m;
        }

        static Vector2 Place(Vector2Int region, float u01, float w01)
            => new Vector2((0.5f + u01 * (TexW - 1f)) / TexW, (region.x + 0.5f + w01 * (region.y - 1f)) / TexH);

        /// <summary>Lay the designs' uvs on a renderer's mesh (its paint: the submeshes whose material is named Board...).</summary>
        public static void Map(MeshRenderer mr, Kind kind)
        {
            if (mr == null) return;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var mats = mr.sharedMaterials;
            var paint = new bool[mf.sharedMesh.subMeshCount];
            for (int i = 0; i < paint.Length && i < mats.Length; i++) paint[i] = mats[i] != null && mats[i].name.StartsWith("Board");
            mf.sharedMesh = Mapped(mf.sharedMesh, paint, kind);
        }
    }
}
