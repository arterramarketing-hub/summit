using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The game's title, VERTIGO, drawn here rather than set in a font: our own letterforms, built from
    /// straight cuts (chamfered where a stock face would curve, so they read as carved ice and rock), with a peaked
    /// crossbar on the T and peaks on the M for the summit, and snow loaded on every upward face of every letter,
    /// heaped above it in cornices. Nothing is shipped: it is made at start-up, on a worker thread, into one small
    /// texture that Resources/TitleLetters.shader turns into the lettering:
    ///   r  signed distance to the letters' edge (0.5 on it, more inside), so the edge stays sharp at any size;
    ///   g  how much snow lies there;
    ///   ba the slope of a bevel running in from the edge, so the letters catch the sun or moon from where it is.
    /// One word, alone: the game is VERTIGO (the mountain in it is Mt. Vertigo, as its signs say).
    /// </summary>
    public static class TitleArt
    {
        public const int Width = 1280, Height = 320;
        public const float Spread = 20f;   // pixels of distance the r channel holds either side of the edge
        const int SS = 2;                  // drawn at twice the size, for a smooth edge

        /// <summary>One way of drawing the title: the letters' build and the material the shader gives them.</summary>
        public class Style
        {
            public string name;
            public float slant, stem, bar, chamfer, gap, snow, mound, bevel;
            public Color top, bottom, snowCol, shadowCol;
            public float spec, edge, frost, shadow, drift;
        }

        public static readonly Style[] Styles =
        {
            // 1. CORNICE: heavy slanted ice with snow loaded on it, spindrift blowing off the tops
            new Style
            {
                name = "CORNICE", slant = 0.2f, stem = 0.24f, bar = 0.2f, chamfer = 0.2f, gap = 0.1f, snow = 0.06f, mound = 0.075f, bevel = 0.085f,
                top = new Color(0.66f, 0.8f, 0.96f), bottom = new Color(0.26f, 0.41f, 0.66f), snowCol = new Color(0.97f, 0.985f, 1f), shadowCol = new Color(0.02f, 0.04f, 0.1f),
                spec = 0.55f, edge = 0.8f, frost = 0.45f, shadow = 0.45f, drift = 1f,
            },
            // 2. GRANITE: upright, monumental, dark rock with frost on its edges and a little snow on the tops
            new Style
            {
                name = "GRANITE", slant = 0f, stem = 0.27f, bar = 0.22f, chamfer = 0.14f, gap = 0.13f, snow = 0.045f, mound = 0.045f, bevel = 0.06f,
                top = new Color(0.4f, 0.385f, 0.38f), bottom = new Color(0.2f, 0.195f, 0.205f), snowCol = new Color(0.95f, 0.97f, 1f), shadowCol = new Color(0.02f, 0.03f, 0.06f),
                spec = 0.25f, edge = 1.2f, frost = 0.08f, shadow = 0.3f, drift = 0.35f,
            },
            // 3. FROST: light, widely spaced letters of clear ice, a thin line of snow on their tops: the quiet one
            new Style
            {
                name = "FROST", slant = 0.12f, stem = 0.135f, bar = 0.115f, chamfer = 0.12f, gap = 0.19f, snow = 0.05f, mound = 0.04f, bevel = 0.05f,
                top = new Color(0.9f, 0.95f, 1f), bottom = new Color(0.56f, 0.69f, 0.87f), snowCol = new Color(0.98f, 0.99f, 1f), shadowCol = new Color(0.02f, 0.04f, 0.1f),
                spec = 0.45f, edge = 0.6f, frost = 0.35f, shadow = 0.45f, drift = 0.6f,
            },
        };

        /// <summary>The one shown unless something chooses another.</summary>
        public const int DefaultStyle = 0;

        /// <summary>Where VERTIGO lies in the texture (v from its foot to its cap height): the shader grades the ice down it.</summary>
        public const float WordV0 = BaseV / Height, WordV1 = (BaseV + CapV) / Height;
        const float CapV = 200f, BaseV = 66f;   // (room below for the shadow, above for the snow heaped on the tops)

        // ---------------------------------------------------------------- the letters

        struct Op { public Vector2[] p; public bool add; }
        class Glyph { public float w; public readonly List<Op> ops = new List<Op>(); }

        static Vector2[] P(params float[] xy) { var v = new Vector2[xy.Length / 2]; for (int i = 0; i < v.Length; i++) v[i] = new Vector2(xy[2 * i], xy[2 * i + 1]); return v; }
        static Vector2[] Box(float x0, float y0, float x1, float y1) => P(x0, y0, x1, y0, x1, y1, x0, y1);
        static Vector2[] Oct(float x0, float y0, float x1, float y1, float c) => P(x0 + c, y0, x1 - c, y0, x1, y0 + c, x1, y1 - c, x1 - c, y1, x0 + c, y1, x0, y1 - c, x0, y0 + c);

        /// <summary>A V: two strokes meeting at `foot`, their inner edges parallel to the outer.</summary>
        static Vector2[] Vee(float w, float s, float foot)
        {
            float s1 = s * 1.12f, e = s * 0.48f;
            float yi = 1f - (w * 0.5f - s1) / (w * 0.5f - e) * (1f - foot);
            return P(0, 1, s1, 1, w * 0.5f, yi, w - s1, 1, w, 1, w * 0.5f + e, foot, w * 0.5f - e, foot);
        }

        /// <summary>One letter, cap height 1, in its own space (y up). Shapes are drawn in order, later over earlier.</summary>
        static Glyph Make(char c, float s, float b, float ch)
        {
            var g = new Glyph();
            void A(Vector2[] p) => g.ops.Add(new Op { p = p, add = true });
            void X(Vector2[] p) => g.ops.Add(new Op { p = p, add = false });
            float w;
            switch (c)
            {
                case 'V': w = 0.84f + s * 0.3f; A(Vee(w, s, 0f)); break;
                case 'M':
                    w = 0.98f + s * 0.3f;
                    A(Vee(w, s * 0.92f, 0.3f)); A(Box(0, 0, s, 1)); A(Box(w - s, 0, w, 1));
                    break;
                case 'N':
                    w = 0.8f + s * 0.2f;
                    A(Box(0, 0, s, 1)); A(Box(w - s, 0, w, 1)); A(P(0, 1, s * 1.15f, 1, w, 0, w - s * 1.15f, 0));
                    break;
                case 'E':
                    w = 0.6f + s * 0.25f;
                    A(Box(0, 0, s, 1));
                    A(P(0, 1 - b, w - 0.04f, 1 - b, w, 1, 0, 1));                      // the top bar's end cut like a blade
                    A(Box(0, 0.5f - b * 0.45f, w * 0.84f, 0.5f + b * 0.55f));
                    A(Box(0, 0, w, b));
                    break;
                case 'R':
                {
                    w = 0.7f + s * 0.25f;
                    float mid = 0.42f;
                    A(Box(0, 0, s, 1));
                    A(P(s * 0.5f, 1, w - ch, 1, w, 1 - ch, w, mid + ch * 0.6f, w - ch * 0.6f, mid, s * 0.5f, mid));
                    X(P(s, 1 - b, w - s - ch * 0.5f, 1 - b, w - s, 1 - b - ch * 0.5f, w - s, mid + b + ch * 0.3f, w - s - ch * 0.3f, mid + b, s, mid + b));
                    A(P(w * 0.3f, mid + 0.05f, w * 0.3f + s * 1.1f, mid + 0.05f, w + 0.02f, 0, w + 0.02f - s * 1.2f, 0));   // the leg, kicked out
                    break;
                }
                case 'T':
                {
                    w = 0.74f + s * 0.2f;
                    const float p = 0.085f;   // the crossbar peaks in the middle: a summit
                    A(P(0, 1 - p - b, w * 0.5f, 1 - b, w, 1 - p - b, w, 1 - p, w * 0.5f, 1, 0, 1 - p));
                    A(Box(w * 0.5f - s * 0.5f, 0, w * 0.5f + s * 0.5f, 1 - b * 0.5f));
                    break;
                }
                case 'I': w = s; A(P(0, 0, s, 0, s, 1, 0, 1 - 0.05f)); break;
                case 'G':
                    w = 0.8f + s * 0.2f;
                    A(Oct(0, 0, w, 1, ch)); X(Oct(s, b, w - s, 1 - b, ch * 0.55f));
                    X(Box(w - s - 0.01f, 0.47f, w + 0.01f, 1 - b - ch * 0.2f));
                    A(Box(w * 0.46f, 0.47f - b, w, 0.47f));
                    break;
                case 'O': w = 0.8f + s * 0.2f; A(Oct(0, 0, w, 1, ch)); X(Oct(s, b, w - s, 1 - b, ch * 0.55f)); break;
                case 'U':
                    w = 0.74f + s * 0.2f;
                    A(P(0, 1, 0, ch, ch, 0, w - ch, 0, w, ch, w, 1, w - s, 1, w - s, b + ch * 0.5f, w - s - ch * 0.5f, b, s + ch * 0.5f, b, s, b + ch * 0.5f, s, 1));
                    break;
                default: w = 0.5f; break;
            }
            g.w = w;
            return g;
        }

        static bool Inside(Vector2[] p, float x, float y)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > y) != (p[j].y > y) && x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x) c = !c;
            return c;
        }

        /// <summary>Draw a word into the mask, centred, its foot at `baseY`, its caps `cap` high (pixels at the drawn size).</summary>
        static void Word(bool[] mask, int w, int h, string text, float cap, float baseY, float s, float b, float ch, float gap, float slant)
        {
            var glyphs = new Glyph[text.Length];
            float total = 0f;
            for (int i = 0; i < text.Length; i++) { glyphs[i] = Make(text[i], s, b, ch); total += glyphs[i].w + (i > 0 ? gap : 0f); }
            float x0 = (w - (total + slant) * cap) * 0.5f, at = 0f;
            for (int i = 0; i < glyphs.Length; i++)
            {
                var g = glyphs[i];
                int px0 = Mathf.Max(0, Mathf.FloorToInt(x0 + (at - 0.1f) * cap)), px1 = Mathf.Min(w, Mathf.CeilToInt(x0 + (at + g.w + slant + 0.1f) * cap));
                int py0 = Mathf.Max(0, Mathf.FloorToInt(baseY - 2)), py1 = Mathf.Min(h, Mathf.CeilToInt(baseY + cap * 1.05f + 2));
                for (int py = py0; py < py1; py++)
                {
                    float yu = (py + 0.5f - baseY) / cap;
                    for (int px = px0; px < px1; px++)
                    {
                        float xu = (px + 0.5f - x0) / cap - slant * yu - at;
                        bool on = false;
                        foreach (var op in g.ops) if (Inside(op.p, xu, yu)) on = op.add;
                        if (on) mask[py * w + px] = true;
                    }
                }
                at += g.w + gap;
            }
        }

        // ---------------------------------------------------------------- noise

        static float Hash(int x, int salt)
        {
            unchecked { int h = x * 374761393 + salt * 668265263; h = (h ^ (int)((uint)h >> 13)) * 1274126177; return ((uint)(h ^ (int)((uint)h >> 16))) / 4294967296f; }
        }
        static float Noise1(float x, int salt = 0)
        {
            int i = Mathf.FloorToInt(x); float f = x - i; f = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash(i, salt), Hash(i + 1, salt), f);
        }

        // ---------------------------------------------------------------- snow

        /// <summary>
        /// Snow on every face that looks up: a layer on the letter's top, as deep as the face is level, and above it a
        /// cornice heaped up along each run of level top, rounding off at its ends. Columns `x` in turn, top to bottom,
        /// within the band `y0..y1` (one word, its caps `cap` high).
        /// </summary>
        static void Snow(bool[] mask, float[] snow, int w, int h, int y0, int y1, float cap, float depth, float mound, int salt)
        {
            var surf = new List<int>[w];
            for (int x = 0; x < w; x++)
            {
                surf[x] = new List<int>();
                for (int y = Mathf.Min(y1, h - 2); y >= y0; y--)
                    if (mask[y * w + x] && !mask[(y + 1) * w + x]) surf[x].Add(y);
            }
            int Near(int x, int y, int tol)
            {
                if (x < 0 || x >= w) return int.MinValue;
                foreach (int s in surf[x]) if (Mathf.Abs(s - y) <= tol) return s;
                return int.MinValue;
            }
            int step = Mathf.Max(2, Mathf.RoundToInt(cap * 0.02f));
            // runs of level surface, for the cornices
            var runs = new List<List<Vector2Int>>();
            var runOf = new Dictionary<long, int>();
            for (int x = 0; x < w; x++)
                foreach (int ys in surf[x])
                {
                    int l = Near(x - step, ys, step * 3), r = Near(x + step, ys, step * 3);
                    float slope = l != int.MinValue && r != int.MinValue ? (r - l) / (2f * step) : l != int.MinValue ? (ys - l) / (float)step : r != int.MinValue ? (r - ys) / (float)step : 9f;
                    float flat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.9f, 1f / Mathf.Sqrt(1f + slope * slope)));
                    // the layer on the face
                    float d = depth * flat * (0.8f + 0.4f * Noise1(x / (cap * 0.25f), salt));
                    for (int k = 0; k < d + 2f && ys - k >= 0 && mask[(ys - k) * w + x]; k++)
                        snow[(ys - k) * w + x] = Mathf.Max(snow[(ys - k) * w + x], Mathf.Clamp01(d - k + 0.5f));
                    if (flat < 0.5f) continue;
                    // which run: the one this face continues from the column before
                    int id = -1;
                    for (int dy = -2; dy <= 2 && id < 0; dy++) if (runOf.TryGetValue(((long)(x - 1) << 20) | (uint)(ys + dy), out int rid)) id = rid;
                    if (id < 0) { id = runs.Count; runs.Add(new List<Vector2Int>()); }
                    runs[id].Add(new Vector2Int(x, ys));
                    runOf[((long)x << 20) | (uint)ys] = id;
                }
            foreach (var run in runs)
            {
                int n = run.Count;
                if (n < 4) continue;
                for (int i = 0; i < n; i++)
                {
                    var p = run[i];
                    float prof = Mathf.Pow(Mathf.Sin(Mathf.PI * (i + 0.5f) / n), 0.45f);
                    float m = Mathf.Min(mound * prof * (0.8f + 0.4f * Noise1(p.x / (cap * 0.3f), salt + 7)), n * 0.16f);
                    for (int k = 1; k <= m + 1f && p.y + k < h; k++)
                    {
                        float a = Mathf.Clamp01(m - k + 1f);
                        if (a < 0.5f) break;
                        mask[(p.y + k) * w + p.x] = true;
                        snow[(p.y + k) * w + p.x] = 1f;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- distance

        const float Inf = 1e20f;

        /// <summary>Felzenszwalb and Huttenlocher's squared distance transform of one line.</summary>
        static void Edt1(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0; v[0] = 0; z[0] = -Inf; z[1] = Inf;
            for (int q = 1; q < n; q++)
            {
                float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                while (s <= z[k]) { k--; s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]); }
                k++; v[k] = q; z[k] = s; z[k + 1] = Inf;
            }
            k = 0;
            for (int q = 0; q < n; q++) { while (z[k + 1] < q) k++; d[q] = (q - v[k]) * (q - v[k]) + f[v[k]]; }
        }

        /// <summary>Squared distance from every pixel to the nearest pixel where `to` is set.</summary>
        static float[] Edt(bool[] mask, bool to, int w, int h)
        {
            var g = new float[w * h];
            for (int i = 0; i < g.Length; i++) g[i] = mask[i] == to ? 0f : Inf;
            int n = Mathf.Max(w, h);
            var f = new float[n]; var d = new float[n]; var v = new int[n]; var z = new float[n + 1];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) f[y] = g[y * w + x];
                Edt1(f, h, d, v, z);
                for (int y = 0; y < h; y++) g[y * w + x] = d[y];
            }
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) f[x] = g[y * w + x];
                Edt1(f, w, d, v, z);
                for (int x = 0; x < w; x++) g[y * w + x] = d[x];
            }
            return g;
        }

        // ---------------------------------------------------------------- all of it

        /// <summary>The title's texture for a style (Width x Height, RGBA). Pure arithmetic: safe off the main thread.</summary>
        public static Color32[] Build(Style st)
        {
            int w = Width * SS, h = Height * SS;
            var mask = new bool[w * h];
            var snow = new float[w * h];
            float cap = CapV * SS;
            Word(mask, w, h, "VERTIGO", cap, BaseV * SS, st.stem, st.bar, st.chamfer, st.gap, st.slant);
            Snow(mask, snow, w, h, (int)(BaseV * SS) - 4, (int)((BaseV + CapV * 1.06f) * SS), cap, st.snow * cap, st.mound * cap, 11);

            var toIn = Edt(mask, true, w, h); var toOut = Edt(mask, false, w, h);
            // the signed distance, at the texture's own size (half the drawn), each pixel the mean of its four
            var sd = new float[Width * Height]; var sn = new float[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float acc = 0f, sa = 0f;
                    for (int j = 0; j < SS; j++)
                        for (int i = 0; i < SS; i++)
                        {
                            int q = (y * SS + j) * w + x * SS + i;
                            acc += mask[q] ? -(Mathf.Sqrt(toOut[q]) - 0.5f) : Mathf.Sqrt(toIn[q]) - 0.5f;
                            sa += snow[q];
                        }
                    sd[y * Width + x] = acc / (SS * SS * SS);
                    sn[y * Width + x] = sa / (SS * SS);
                }
            // the bevel: rising from the edge over `bevel` cap heights (more steeply at first, like a rounded edge)
            float bw = Mathf.Max(2f, st.bevel * CapV);
            var hgt = new float[Width * Height];
            for (int i = 0; i < hgt.Length; i++) { float t = Mathf.Clamp01(-sd[i] / bw); hgt[i] = 1f - (1f - t) * (1f - t); }
            var px = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    float gx = (hgt[y * Width + Mathf.Min(x + 1, Width - 1)] - hgt[y * Width + Mathf.Max(x - 1, 0)]) * 0.5f * bw;
                    float gy = (hgt[Mathf.Min(y + 1, Height - 1) * Width + x] - hgt[Mathf.Max(y - 1, 0) * Width + x]) * 0.5f * bw;
                    px[i] = new Color32((byte)(255f * Mathf.Clamp01(0.5f - sd[i] / (2f * Spread))), (byte)(255f * Mathf.Clamp01(sn[i])),
                                        (byte)(255f * Mathf.Clamp01(0.5f + gx * 0.5f)), (byte)(255f * Mathf.Clamp01(0.5f + gy * 0.5f)));
                }
            return px;
        }

        /// <summary>The texture for those pixels (main thread only).</summary>
        public static Texture2D ToTexture(Color32[] px, string name)
        {
            var t = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
