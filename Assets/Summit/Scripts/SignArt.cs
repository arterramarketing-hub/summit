using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The start gate's board as a resort would have it made: MT. VERTIGO routed into the planks in a sturdy slab serif
    /// (the face of national-park and ski-area signs: square slab feet, thick and thin strokes) and painted cream, a
    /// smaller ELEV. 10,000 FT under it between two little routed mountains, and a routed line round the edge. Our own
    /// letterforms, built from rectangles, polygons and ellipses (no font in the build), rasterised once at start-up
    /// on a worker thread into one texture that Resources/GateBoard.shader paints the board from:
    ///   r  paint: how much of the texel is routed and painted (antialiased);
    ///   g  snow: lying on the lower ledge of each cut, where it settles in real carved letters;
    ///   ba the slope of the cut (from a blur of the paint), so its walls catch the light and the shade.
    /// The board is 4:1 (StartGate's 7 x 1.75 m); everything here is laid out in metres on it.
    /// </summary>
    public static class SignArt
    {
        public const int Width = 2048, Height = 512;
        public const float BoardW = 7f, BoardH = 1.75f;
        const int SS = 2;   // drawn at twice the size, for a smooth edge

        // ---------------------------------------------------------------- the letters

        enum K { Rect, Poly, Ellipse }
        struct Op { public K k; public bool add; public float x0, y0, x1, y1; public Vector2[] p; }
        class Glyph { public float w; public readonly List<Op> ops = new List<Op>(); }

        // the slab serif's proportions, in cap heights: thick stem, thin stroke, the slabs' height and overhang
        const float S = 0.17f, T = 0.085f, SH = 0.075f, O = 0.07f;

        static Glyph Make(char c)
        {
            var g = new Glyph();
            void R(float x0, float y0, float x1, float y1, bool add = true) => g.ops.Add(new Op { k = K.Rect, add = add, x0 = x0, y0 = y0, x1 = x1, y1 = y1 });
            void P(bool add, params float[] xy)
            {
                var v = new Vector2[xy.Length / 2];
                for (int i = 0; i < v.Length; i++) v[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
                g.ops.Add(new Op { k = K.Poly, add = add, p = v });
            }
            void E(float cx, float cy, float rx, float ry, bool add = true) => g.ops.Add(new Op { k = K.Ellipse, add = add, x0 = cx, y0 = cy, x1 = rx, y1 = ry });
            // a ring: thick on the sides, thin top and bottom, as a pen would make it
            void Ring(float cx, float cy, float rx, float ry) { E(cx, cy, rx, ry); E(cx, cy, rx - S, ry - T, false); }
            void Foot(float xc, float half) => R(xc - half, 0, xc + half, SH);
            void Head(float xc, float half) => R(xc - half, 1 - SH, xc + half, 1);
            float w;
            switch (c)
            {
                case 'M':
                    w = 1.04f;
                    R(O, 0, O + T, 1);                                         // the thin left stem
                    R(w - O - S, 0, w - O, 1);                                 // the thick right one
                    P(true, O, 1, O + S * 1.1f, 1, w * 0.5f + S * 0.35f, 0.02f, w * 0.5f - S * 0.55f, 0.02f);   // thick diagonal down
                    P(true, w - O - S - T * 0.6f, 1, w - O - S + T * 0.5f, 1, w * 0.5f + T * 0.4f, 0.02f, w * 0.5f - T * 0.6f, 0.02f);   // thin one up
                    Foot(O + T * 0.5f, 0.14f); Foot(w - O - S * 0.5f, 0.17f);
                    R(0, 1 - SH, O + S * 1.1f, 1); R(w - O - S - T * 0.6f, 1 - SH, w, 1);
                    break;
                case 'T':
                    w = 0.86f;
                    R(0, 1 - T * 1.25f, w, 1);
                    R(w * 0.5f - S * 0.5f, 0, w * 0.5f + S * 0.5f, 1);
                    Foot(w * 0.5f, 0.18f);
                    R(0, 1 - 0.27f, T * 0.95f, 1); R(w - T * 0.95f, 1 - 0.27f, w, 1);   // the bar's ends turned down
                    break;
                case 'V':
                    w = 0.96f;
                    P(true, O, 1, O + S * 1.15f, 1, w * 0.5f + S * 0.32f, 0, w * 0.5f - S * 0.42f, 0);
                    P(true, w - O - T * 1.15f, 1, w - O, 1, w * 0.5f + T * 0.55f, 0, w * 0.5f - T * 0.45f, 0);
                    R(0, 1 - SH, O * 2f + S * 1.15f, 1); R(w - O * 2f - T * 1.15f, 1 - SH, w, 1);
                    break;
                case 'E':
                    w = 0.78f;
                    R(O, 0, O + S, 1);
                    R(O, 1 - T, w, 1); R(O, 0.5f - T * 0.55f, w - 0.13f, 0.5f + T * 0.45f); R(O, 0, w, T);
                    R(0, 0, O * 2f + S, SH); R(0, 1 - SH, O * 2f + S, 1);
                    R(w - T, 1 - 0.25f, w, 1); R(w - T, 0, w, 0.25f);                // serifs at the bars' ends
                    R(w - 0.13f - T * 0.8f, 0.5f - 0.11f, w - 0.13f, 0.5f + 0.11f);
                    break;
                case 'L':
                    w = 0.72f;
                    R(O, 0, O + S, 1); R(O, 0, w, T);
                    R(0, 0, O * 2f + S, SH); R(0, 1 - SH, O * 2f + S, 1);
                    R(w - T, 0, w, 0.25f);
                    break;
                case 'F':
                    w = 0.74f;
                    R(O, 0, O + S, 1);
                    R(O, 1 - T, w, 1); R(O, 0.5f - T * 0.55f, w - 0.12f, 0.5f + T * 0.45f);
                    Foot(O + S * 0.5f, 0.2f); R(0, 1 - SH, O * 2f + S, 1);
                    R(w - T, 1 - 0.25f, w, 1);
                    R(w - 0.12f - T * 0.8f, 0.5f - 0.11f, w - 0.12f, 0.5f + 0.11f);
                    break;
                case 'R':
                {
                    w = 0.86f;
                    float bx = 0.46f, cy = 0.745f, rx = 0.27f, ry = 0.255f;
                    Ring(bx, cy, rx, ry);
                    R(-1f, 0, bx, 1, false);                                    // only the bowl's right half
                    R(O, 0, O + S, 1);
                    R(O, 1 - T, bx, 1); R(O, cy - ry, bx, cy - ry + T * 1.05f);
                    P(true, 0.36f, cy - ry + 0.02f, 0.36f + S * 0.95f, cy - ry + 0.02f, w - 0.07f, 0, w - 0.07f - S * 1.05f, 0);   // the leg
                    R(0, 0, O * 2f + S, SH); R(0, 1 - SH, O * 2f + S, 1);
                    R(w - 0.07f - S * 1.05f - 0.05f, 0, w + 0.02f, SH);
                    break;
                }
                case 'I':
                    w = 0.46f;
                    R(w * 0.5f - S * 0.5f, 0, w * 0.5f + S * 0.5f, 1);
                    R(0.02f, 0, w - 0.02f, SH); R(0.02f, 1 - SH, w - 0.02f, 1);
                    break;
                case 'G':
                {
                    w = 0.92f;
                    float cx = w * 0.5f, rx = w * 0.5f - 0.01f;
                    Ring(cx, 0.5f, rx, 0.5f);
                    R(cx + 0.02f, 0.44f, w + 0.1f, 0.78f, false);                  // the mouth
                    R(cx + 0.04f, 0.44f - T, w - 0.01f, 0.44f);                    // the bar in
                    R(w - 0.01f - S * 0.85f, 0.1f, w - 0.01f, 0.44f);              // and the spur down
                    R(w - 0.01f - T * 1.1f, 0.74f, w - 0.01f, 0.9f);               // a serif on the upper terminal
                    break;
                }
                case 'O': w = 0.92f; Ring(w * 0.5f, 0.5f, w * 0.5f - 0.01f, 0.5f); break;
                case '0': w = 0.64f; Ring(w * 0.5f, 0.5f, w * 0.5f - 0.01f, 0.5f); break;
                case '1':
                    w = 0.56f;
                    R(w * 0.5f - S * 0.5f, 0, w * 0.5f + S * 0.5f, 1);
                    P(true, w * 0.5f - S * 0.5f, 1, w * 0.5f + S * 0.5f, 1, w * 0.5f - S * 0.5f, 0.78f, 0.03f, 0.7f, 0.03f, 0.78f);   // the flag
                    Foot(w * 0.5f, 0.22f);
                    break;
                case '.': w = 0.3f; R(0.05f, 0, 0.05f + S * 1.05f, S * 1.05f); break;
                case ',': w = 0.3f; R(0.05f, 0.02f, 0.05f + S * 1.05f, S * 1.05f + 0.02f); P(true, 0.05f + S * 1.05f, 0.04f, 0.05f + S * 0.2f, -0.2f, 0.05f, -0.2f, 0.05f + S * 0.5f, 0.04f); break;
                case ' ': w = 0.34f; break;
                default: w = 0.5f; break;
            }
            g.w = w;
            return g;
        }

        // ---------------------------------------------------------------- drawing it

        static bool Inside(Vector2[] p, float x, float y)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > y) != (p[j].y > y) && x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x) c = !c;
            return c;
        }

        static bool Hit(Op op, float x, float y)
        {
            switch (op.k)
            {
                case K.Rect: return x >= op.x0 && x < op.x1 && y >= op.y0 && y < op.y1;
                case K.Ellipse: { float dx = (x - op.x0) / op.x1, dy = (y - op.y0) / op.y1; return dx * dx + dy * dy <= 1f; }
                default: return Inside(op.p, x, y);
            }
        }

        /// <summary>A shape drawn in metres on the board: its ops, the box it fills (for speed).</summary>
        static void Shape(bool[] mask, int w, int h, Glyph g, float x0m, float baseM, float capM)
        {
            float pxPerM = w / BoardW;
            int px0 = Mathf.Max(0, Mathf.FloorToInt((x0m - 0.05f * capM) * pxPerM)), px1 = Mathf.Min(w, Mathf.CeilToInt((x0m + (g.w + 0.1f) * capM) * pxPerM));
            int py0 = Mathf.Max(0, Mathf.FloorToInt((baseM - 0.25f * capM) * pxPerM)), py1 = Mathf.Min(h, Mathf.CeilToInt((baseM + 1.05f * capM) * pxPerM));
            for (int py = py0; py < py1; py++)
            {
                float y = ((py + 0.5f) / pxPerM - baseM) / capM;
                for (int px = px0; px < px1; px++)
                {
                    float x = ((px + 0.5f) / pxPerM - x0m) / capM;
                    bool on = false, any = false;
                    foreach (var op in g.ops) if (Hit(op, x, y)) { on = op.add; any = true; }
                    if (any && on) mask[py * w + px] = true;   // (a cut only shapes its own glyph: it never clears another's)
                }
            }
        }

        /// <summary>A line of text centred at `cxM`, its foot at `baseM`, caps `capM` high, `track` cap heights between letters.</summary>
        static float Line(bool[] mask, int w, int h, string text, float cxM, float baseM, float capM, float track, bool draw = true)
        {
            var gs = new Glyph[text.Length];
            float total = 0f;
            for (int i = 0; i < text.Length; i++) { gs[i] = Make(text[i]); total += gs[i].w + (i > 0 ? track : 0f); }
            if (!draw) return total * capM;
            float x = cxM - total * capM * 0.5f;
            foreach (var g in gs) { Shape(mask, w, h, g, x, baseM, capM); x += (g.w + track) * capM; }
            return total * capM;
        }

        /// <summary>A little mountain: two peaks, the larger snowcapped (a routed line between snow and rock).</summary>
        static Glyph Mountain()
        {
            var g = new Glyph { w = 1.6f };
            void P(bool add, params float[] xy)
            {
                var v = new Vector2[xy.Length / 2];
                for (int i = 0; i < v.Length; i++) v[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
                g.ops.Add(new Op { k = K.Poly, add = add, p = v });
            }
            P(true, 0.55f, 0.55f, 1.05f, 0.95f, 1.6f, 0f, 0.5f, 0f);          // the small peak behind, to the right
            P(true, 0f, 0f, 0.62f, 1f, 1.22f, 0f);                            // the big one
            // the snow line across it: a zigzag cut, rock below, snow above
            P(false, 0.33f, 0.55f, 0.47f, 0.63f, 0.6f, 0.52f, 0.74f, 0.64f, 0.92f, 0.5f, 0.92f, 0.44f, 0.74f, 0.57f, 0.6f, 0.45f, 0.47f, 0.56f, 0.37f, 0.48f);
            return g;
        }

        /// <summary>The routed line round the board, a little in from its edge.</summary>
        static void Border(bool[] mask, int w, int h, float inset, float width)
        {
            float pxPerM = w / BoardW;
            int a = Mathf.RoundToInt(inset * pxPerM), b = Mathf.RoundToInt((inset + width) * pxPerM);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Min(x, w - 1 - x), dy = Mathf.Min(y, h - 1 - y), d = Mathf.Min(dx, dy);
                    if (d >= a && d < b) mask[y * w + x] = true;
                }
        }

        // ---------------------------------------------------------------- all of it

        /// <summary>The board's texture (Width x Height, RGBA). Pure arithmetic: safe off the main thread.</summary>
        public static Color32[] Build()
        {
            int w = Width * SS, h = Height * SS;
            var mask = new bool[w * h];
            // MT. VERTIGO, big; under it the elevation, small and widely spaced, between two mountains
            Line(mask, w, h, "MT. VERTIGO", BoardW * 0.5f, 0.68f, 0.64f, 0.07f);
            float lineW = Line(mask, w, h, "ELEV. 10,000 FT", BoardW * 0.5f, 0.27f, 0.155f, 0.14f);
            var mt = Mountain();
            float mh = 0.2f, gap = 0.18f;
            Shape(mask, w, h, mt, BoardW * 0.5f - lineW * 0.5f - gap - mt.w * mh, 0.25f, mh);
            Shape(mask, w, h, mt, BoardW * 0.5f + lineW * 0.5f + gap, 0.25f, mh);
            // a fine routed rule between the two lines, and the line round the edge
            {
                float pxPerM = w / BoardW;
                int y0 = Mathf.RoundToInt(0.55f * pxPerM), y1 = Mathf.RoundToInt(0.565f * pxPerM);
                int x0 = Mathf.RoundToInt((BoardW * 0.5f - 1.6f) * pxPerM), x1 = Mathf.RoundToInt((BoardW * 0.5f + 1.6f) * pxPerM);
                for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) mask[y * w + x] = true;
            }
            Border(mask, w, h, 0.09f, 0.022f);

            // down to the texture's size: coverage
            var cov = new float[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int n = 0;
                    for (int j = 0; j < SS; j++) for (int i = 0; i < SS; i++) if (mask[(y * SS + j) * w + x * SS + i]) n++;
                    cov[y * Width + x] = n / (float)(SS * SS);
                }
            // the cut's slope: a soft blur of the paint, as a depth (the routed letters are lower than the board)
            var depth = Blur(cov, Width, Height, 3);
            depth = Blur(depth, Width, Height, 2);
            var px = new Color32[Width * Height];
            const int ledge = 4;   // texels of snow on each cut's lower ledge (about 1.4 cm)
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    float gx = (depth[y * Width + Mathf.Min(x + 1, Width - 1)] - depth[y * Width + Mathf.Max(x - 1, 0)]) * 2.5f;
                    float gy = (depth[Mathf.Min(y + 1, Height - 1) * Width + x] - depth[Mathf.Max(y - 1, 0) * Width + x]) * 2.5f;
                    // snow: in the cut, where the board below it isn't cut (the lower ledge), thinning up from it
                    float snow = 0f;
                    if (cov[i] > 0.3f)
                        for (int k = 1; k <= ledge; k++)
                        {
                            int yy = y - k;
                            if (yy < 0 || cov[yy * Width + x] < 0.5f) { snow = 1f - (k - 1) / (float)ledge; break; }
                        }
                    px[i] = new Color32((byte)(255f * cov[i]), (byte)(255f * Mathf.Clamp01(snow * cov[i])),
                                        (byte)(255f * Mathf.Clamp01(0.5f + gx)), (byte)(255f * Mathf.Clamp01(0.5f + gy)));
                }
            return px;
        }

        /// <summary>A separable box blur, `r` texels each way.</summary>
        static float[] Blur(float[] src, int w, int h, int r)
        {
            var tmp = new float[src.Length]; var dst = new float[src.Length];
            for (int y = 0; y < h; y++)
            {
                float acc = 0f; int n = 0;
                for (int x = -r; x <= r; x++) if (x >= 0 && x < w) { acc += src[y * w + x]; n++; }
                for (int x = 0; x < w; x++)
                {
                    tmp[y * w + x] = acc / n;
                    int xo = x - r, xi = x + r + 1;
                    if (xo >= 0) { acc -= src[y * w + xo]; n--; }
                    if (xi < w) { acc += src[y * w + xi]; n++; }
                }
            }
            for (int x = 0; x < w; x++)
            {
                float acc = 0f; int n = 0;
                for (int y = -r; y <= r; y++) if (y >= 0 && y < h) { acc += tmp[y * w + x]; n++; }
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = acc / n;
                    int yo = y - r, yi = y + r + 1;
                    if (yo >= 0) { acc -= tmp[yo * w + x]; n--; }
                    if (yi < h) { acc += tmp[yi * w + x]; n++; }
                }
            }
            return dst;
        }

        /// <summary>The texture for those pixels (main thread only), mipmapped: it's read from far up the camp too.</summary>
        public static Texture2D ToTexture(Color32[] px, string name)
        {
            var t = new Texture2D(Width, Height, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }
    }
}
