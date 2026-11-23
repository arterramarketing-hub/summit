using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Trail difficulty signs (mountain audit F2): a white board on two posts at the side of the piste, facing up the
    /// hill, with the run's symbol on it and nothing else (no lettering): green circle, blue square, black diamond,
    /// double black diamond. One at the start of each section that changes the riding: the park (blue), the deadly
    /// walls (double black), a ridge or a ledge (double black), a valley's gorge (black). Others can put one up
    /// anywhere with Place (a cliff band's traverse, a fork), for the mountain they are built for.
    /// Streamed with PisteKit (built near the rider, thrown away behind), and they break like the section signs
    /// (Breakable.Sign). The symbols are drawn at start-up into small textures; the board is lit like the rest of the
    /// kit, floodlights and all (Resources/PisteKit.shader).
    /// </summary>
    public static class TrailSigns
    {
        public enum Symbol { Green, Blue, Black, DoubleBlack }

        public struct Sign { public float z, side; public Symbol symbol; public int seed; }

        static readonly List<Sign> asked = new List<Sign>();
        static readonly List<Sign> laid = new List<Sign>();
        static int laidFor = int.MinValue;

        /// <summary>
        /// Put up a sign at z on today's mountain: side -1 left of the run, +1 right, 0 whichever is clear.
        /// (Asked again for the same place and symbol, it is the same sign.)
        /// </summary>
        public static void Place(float z, Symbol symbol, float side = 0f)
        {
            foreach (var s in asked) if (s.seed == Slope.Seed && Mathf.Abs(s.z - z) < 1f && s.symbol == symbol) return;
            asked.Add(new Sign { z = z, side = side, symbol = symbol, seed = Slope.Seed });
            laidFor = int.MinValue;
        }

        /// <summary>Every sign on today's mountain, by z.</summary>
        public static List<Sign> Signs { get { Lay(); return laid; } }

        /// <summary>True within r of a sign's foot (PisteKit keeps its poles off them).</summary>
        public static bool Near(float x, float z, float r)
        {
            foreach (var s in Signs)
            {
                Vector3 f = Foot(s);
                if ((f.x - x) * (f.x - x) + (f.z - z) * (f.z - z) < r * r) return true;
            }
            return false;
        }

        static void Lay()
        {
            if (laidFor == Slope.Seed) return;
            laidFor = Slope.Seed;
            laid.Clear();
            for (int c = 0; c <= Course.CycleOf(Course.ClearFrom); c++)
            {
                float zb = Course.First + c * Course.Cycle;
                // (the section signs, CourseDressing's, stand on the other side at the same place)
                if (zb + Course.ParkAt + 180f < Course.ClearFrom) Add(zb + Course.ParkAt - 30f, Symbol.Blue, -1f);
                if (Cliffs.WallOf(c, out var w)) Add(w.z0 - 28f, Symbol.DoubleBlack, -w.side);
                // a cliff band's traverse (or an angled ledge): double black; a ledge line beside the run: black. (Across the
                // run from the section sign, CliffDressing's)
                if (CliffBand.Of(c, out var band)) Add(band.z0 - 30f, Symbol.DoubleBlack, band.side);
                if (Cliffs.LedgeOf(c, out var L)) Add(L.Start - 22f, Symbol.Black, -L.side);
                if (Bowl.Of(c, out var bowl)) Add(bowl.zA - 25f, Symbol.DoubleBlack, 1f);   // (the alpine bowl: its roll-over is blind, its floor 40 degrees)
                if (Ridge.Built(c))
                {
                    var f = Ridge.FormOf(c);
                    Add(zb + Ridge.At - 30f, f == Ridge.Form.Valley || f == Ridge.Form.ValleyOneSided ? Symbol.Black : Symbol.DoubleBlack, 1f);
                }
            }
            foreach (var s in asked) if (s.seed == Slope.Seed) Add(s.z, s.symbol, s.side);
            laid.Sort((a, b) => a.z.CompareTo(b.z));
        }

        static void Add(float z, Symbol symbol, float side)
        {
            if (z < Course.First || z > Slope.FinishZ - 10f) return;
            // nudged along the run (and to the other side, for either) until its spot is clear
            foreach (float dz in new[] { 0f, -5f, 5f, -10f, 10f })
                foreach (float s in side == 0f ? new[] { -1f, 1f } : new[] { side, -side })
                {
                    var sg = new Sign { z = z + dz, side = s, symbol = symbol, seed = Slope.Seed };
                    Vector3 f = Foot(sg);
                    if (Clear(f.x, f.z)) { laid.Add(sg); return; }
                }
        }

        static bool Clear(float x, float z)
        {
            float w = 0.9f;
            for (int i = -1; i <= 1; i += 2)
            {
                float px = x + i * w;
                if (Slope.OnPad(px, z) || LiftLine.Taken(px, z, 1.5f) || Hamlets.Taken(px, z) || NightLights.Taken(px, z)) return false;
                if (Course.CrevasseNear(px, z, 2f) || Cliffs.Keep(px, z, 1f) || Mathf.Abs(Slope.Kicker(px, z)) > 0.01f || Course.NearFeature(px, z, 2f)) return false;
                if (PisteKit.TreeNear(px, z, 0.8f)) return false;
            }
            return true;
        }

        /// <summary>Where a sign stands: just off the groomed run (or the crest), on its side.</summary>
        public static Vector3 Foot(Sign s)
        {
            float half = Ridge.Depth(s.z) > 0.05f ? Ridge.HalfWidth(s.z) : Slope.HalfAt(s.z);   // (the run's own edge: wide below a band, slantwise on one)
            float x = Slope.Centre(s.z) + s.side * (half + 2.2f / Slope.Squash(s.z));
            return new Vector3(x, Slope.DrawnHeight(x, s.z), s.z);
        }

        // ---------------------------------------------------------------- built round the rider

        static readonly Dictionary<long, GameObject> built = new Dictionary<long, GameObject>();
        static readonly List<long> drop = new List<long>();
        static Material post, back;
        static readonly Material[] faces = new Material[4];

        static long Key(Sign s) => ((long)Mathf.RoundToInt(s.z * 10f) << 4) | (long)((int)s.symbol << 1) | (s.side > 0f ? 1L : 0L);

        /// <summary>The signs between z0 and z1 built under `parent`, and those outside it thrown away (PisteKit).</summary>
        public static void Stream(Transform parent, float z0, float z1)
        {
            var all = Signs;
            drop.Clear();
            foreach (var kv in built)
            {
                float z = (kv.Key >> 4) / 10f;
                if (kv.Value == null || z < z0 - 20f || z > z1 + 20f) drop.Add(kv.Key);
            }
            foreach (long k in drop) { Kill(built[k]); built.Remove(k); }
            foreach (var s in all)
            {
                if (s.z < z0 || s.z > z1) continue;
                long k = Key(s);
                if (built.ContainsKey(k)) continue;
                built[k] = Build(parent, s);
            }
        }

        /// <summary>A new mountain (or the kit switched off): every sign taken down.</summary>
        public static void Clear()
        {
            foreach (var g in built.Values) Kill(g);
            built.Clear();
        }

        static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        public static Vector2 BoardSize(Symbol s) => s == Symbol.DoubleBlack ? new Vector2(1.3f, 0.8f) : new Vector2(0.85f, 0.8f);

        static GameObject Build(Transform parent, Sign s)
        {
            if (post == null)
            {
                post = PisteKit.KitMaterial("TrailSignPost", new Color(0.36f, 0.30f, 0.24f), 0.2f, false);
                back = PisteKit.KitMaterial("TrailSignBack", new Color(0.16f, 0.17f, 0.19f), 0.3f, false);
            }
            int si = (int)s.symbol;
            if (faces[si] == null)
            {
                faces[si] = PisteKit.KitMaterial("TrailSign" + s.symbol, Color.white, 0.25f, false);
                faces[si].SetTexture("_BaseMap", Art(s.symbol));
            }
            Vector3 f = Foot(s);
            Vector2 size = BoardSize(s.symbol);
            const float lift = 1.35f;   // the board's bottom over the snow
            var holder = new GameObject("TrailSign").transform;
            holder.SetParent(parent, false);
            holder.position = f;
            var brk = Breakable.Sign(holder);
            for (int i = -1; i <= 1; i += 2)
            {
                float top = lift + size.y, bottom = -0.4f;
                brk.Post(Block(PrimitiveType.Cylinder, holder, f + new Vector3(i * (size.x * 0.5f - 0.1f), (top + bottom) * 0.5f, 0.06f), new Vector3(0.09f, (top - bottom) * 0.5f, 0.09f), post));
            }
            var bk = Block(PrimitiveType.Cube, holder, f + new Vector3(0f, lift + size.y * 0.5f, 0.0f), new Vector3(size.x + 0.08f, size.y + 0.08f, 0.05f), back);
            // the printed face looks up the slope at the rider (a quad shows its face to -Z)
            var q = Block(PrimitiveType.Quad, holder, f + new Vector3(0f, lift + size.y * 0.5f, -0.027f), new Vector3(size.x, size.y, 1f), faces[si]);
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            brk.Board(bk, q);
            brk.Arm();
            return holder.gameObject;
        }

        static Transform Block(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        // ---------------------------------------------------------------- the art

        /// <summary>The board's face for a symbol: an off-white board, a thin dark routed line round it, the symbol.</summary>
        public static Texture2D Art(Symbol s)
        {
            Vector2 size = BoardSize(s);
            const int H = 128;
            int W = Mathf.RoundToInt(H * size.x / size.y);
            var px = new Color32[W * H];
            Color board = new Color(0.94f, 0.94f, 0.91f), line = new Color(0.12f, 0.13f, 0.15f);
            Color ink = s == Symbol.Green ? new Color(0.05f, 0.55f, 0.22f) : s == Symbol.Blue ? new Color(0.05f, 0.30f, 0.75f) : new Color(0.05f, 0.05f, 0.06f);
            const int SS = 4;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float sym = 0f, edge = 0f;
                    for (int j = 0; j < SS; j++)
                        for (int i = 0; i < SS; i++)
                        {
                            // in metres on the board, from its middle
                            float u = ((x + (i + 0.5f) / SS) / W - 0.5f) * size.x, v = ((y + (j + 0.5f) / SS) / H - 0.5f) * size.y;
                            if (Inside(s, u, v)) sym += 1f;
                            float bx = size.x * 0.5f - Mathf.Abs(u), by = size.y * 0.5f - Mathf.Abs(v);
                            float bd = Mathf.Min(bx, by);
                            if (bd > 0.035f && bd < 0.05f) edge += 1f;
                        }
                    sym /= SS * SS; edge /= SS * SS;
                    Color c = Color.Lerp(Color.Lerp(board, line, edge), ink, sym);
                    px[y * W + x] = c;
                }
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "TrailSign" + s, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        // the symbol, in metres from the board's middle
        static bool Inside(Symbol s, float u, float v)
        {
            switch (s)
            {
                case Symbol.Green: return u * u + v * v < 0.25f * 0.25f;
                case Symbol.Blue: return Mathf.Abs(u) < 0.215f && Mathf.Abs(v) < 0.215f;
                case Symbol.Black: return Mathf.Abs(u) + Mathf.Abs(v) < 0.29f;
                default: return Mathf.Abs(u - 0.27f) + Mathf.Abs(v) < 0.25f || Mathf.Abs(u + 0.27f) + Mathf.Abs(v) < 0.25f;
            }
        }
    }
}
