using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The snow underfoot as the terrain draws it (SnowSurface.shader), and the day's part of it.
    ///
    /// The shader reads what snow is where from one small map that follows the rider: 128 x 256 texels of 2.5 m
    /// (320 m across, 640 m down the hill, most of it ahead), each texel fixed to its place on the mountain and the
    /// map wrapping round, so riding on only ever paints the rows and columns that come into it. Step paints them,
    /// nearest first, a few rows a frame at most; when the weather turns or the mountain is a new one the whole map is
    /// painted again the same way, over about a quarter of a second.
    /// </summary>
    public static partial class Surfaces
    {
        const int MapW = 128, MapH = 256, Behind = 48;   // (Behind: the rows kept up the hill of the rider)
        const float Cell = 2.5f;
        /// <summary>The most texels painted in one frame (a phone paints these in well under a millisecond).</summary>
        const int Budget = 2600;

        static Texture2D map;
        static Color32[] px;
        static readonly int[] rowIz = new int[MapH], rowX0 = new int[MapH], rowEpoch = new int[MapH];
        static int[] order;
        static int epoch = 1, paintedStamp;
        static Weathering painted;
        static SnowMix? paintedForce;
        static float runShown;

        static Shader shader;
        static Material source;
        static readonly List<Material> made = new List<Material>();

        static readonly int MapId = Shader.PropertyToID("_SurfaceMap"), StId = Shader.PropertyToID("_SurfaceMapST"), WinId = Shader.PropertyToID("_SurfaceWin"),
                            DayId = Shader.PropertyToID("_SurfaceDay"), RunId = Shader.PropertyToID("_SurfaceRun"),
                            ColorId = Shader.PropertyToID("_BaseColor"), SmoothId = Shader.PropertyToID("_Smoothness"), BumpId = Shader.PropertyToID("_BumpScale");

        /// <summary>The terrain's snow material: the snow material drawn with the snow underfoot in it (with the far band
        /// or without). The snow material as it is, where the shader cannot be used.</summary>
        public static Material Terrain(Material snow, bool band)
        {
            if (snow == null) return snow;
            if (shader == null) shader = Resources.Load<Shader>("SnowSurface");
            if (!On || shader == null || !shader.isSupported) return band ? Farview.Snow(snow, true) : snow;
            var m = new Material(snow) { name = snow.name + (band ? " (surface, edge)" : " (surface)") };
            bool parallax = snow.IsKeywordEnabled("_PARALLAXMAP");
            int queue = snow.renderQueue;
            m.shader = shader;
            foreach (var k in m.shaderKeywords) m.DisableKeyword(k);
            if (parallax) m.EnableKeyword("_PARALLAXMAP");
            if (!band) m.EnableKeyword("FARVIEW_PLAIN");
            m.renderQueue = band ? (int)RenderQueue.AlphaTest : queue;
            m.enableInstancing = snow.enableInstancing;
            source = snow;
            made.Add(m);
            return m;
        }

        /// <summary>The weather's part of the day (Weather.Step): fresh snow lying, and the wind.</summary>
        public static void Weathered(float fresh, float wind) { Day.fresh = Mathf.Clamp01(fresh); Day.wind = Mathf.Clamp01(wind); }

        /// <summary>How warm the snow is by the clock (TimeOfDay.Clock, 0..4): from the early afternoon through the
        /// evening's warm light, cold again by dusk.</summary>
        public static float Warmth(float clock) => Mathf.Clamp01((clock - 1.2f) / 0.4f) * Mathf.Clamp01((2.8f - clock) / 0.3f);

        /// <summary>
        /// Once a frame: the day's warmth, and the map round `focus` (the rider, or the camera) kept painted for the
        /// terrain's shader. `run` is the run's own heading there (Rider.RunNow): the grooming's lines follow it.
        /// `all`: paint everything that is due now (the render tools; otherwise a few rows a frame).
        /// </summary>
        public static void Step(Vector3 focus, float run, float dt, bool all = false)
        {
            Day.warm = Warmth(TimeOfDay.Clock);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            Fresh();

            // the weather's own look of the snow (Weather changes the snow material itself): on the copies too
            if (source != null)
                for (int i = made.Count - 1; i >= 0; i--)
                {
                    var m = made[i];
                    if (m == null) { made.RemoveAt(i); continue; }
                    if (source.HasProperty(ColorId)) m.SetColor(ColorId, source.GetColor(ColorId));
                    if (source.HasProperty(SmoothId)) m.SetFloat(SmoothId, source.GetFloat(SmoothId));
                    if (source.HasProperty(BumpId)) m.SetFloat(BumpId, source.GetFloat(BumpId));
                }

            if (map == null)
            {
                map = new Texture2D(MapW, MapH, TextureFormat.RGBA32, false, true) { name = "SurfaceMap", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                px = new Color32[MapW * MapH];
                for (int i = 0; i < MapH; i++) rowEpoch[i] = 0;
                order = new int[MapH];
                // (rows in the order they are painted: from the rider's own outwards)
                for (int i = 0, lo = Behind, hi = Behind + 1; i < MapH;)
                {
                    if (lo >= 0) order[i++] = lo--;
                    if (hi < MapH && i < MapH) order[i++] = hi++;
                }
                map.SetPixels32(px); map.Apply(false);
            }

            // the weather turned, the mountain is a new one, or a test tool asked for one kind everywhere: all of it again
            bool forced = Force.HasValue != paintedForce.HasValue || (Force.HasValue && !Same(Force.Value, paintedForce.Value));
            if (paintedStamp != stamp || forced || Mathf.Abs(Day.fresh - painted.fresh) > 0.04f || Mathf.Abs(Day.wind - painted.wind) > 0.04f)
            {
                paintedStamp = stamp; painted = Day; paintedForce = Force; epoch++;
            }

            int x0 = Mathf.FloorToInt(focus.x / Cell) - MapW / 2, z0 = Mathf.FloorToInt(focus.z / Cell) - Behind;
            int left = all || Application.isBatchMode ? int.MaxValue : Budget;
            bool dirty = false;
            for (int k = 0; k < MapH && left > 0; k++)
            {
                int iz = z0 + order[k], t = ((iz % MapH) + MapH) % MapH;
                int from = x0, to = x0 + MapW;
                if (rowEpoch[t] == epoch && rowIz[t] == iz)
                {
                    int had = rowX0[t];
                    if (had == x0) continue;
                    // (the same row, the map moved across it: only the columns that came in)
                    if (x0 > had && x0 < had + MapW) from = had + MapW;
                    else if (x0 < had && x0 + MapW > had) to = had;
                }
                float z = (iz + 0.5f) * Cell;
                for (int ix = from; ix < to; ix++)
                {
                    var m = Place((ix + 0.5f) * Cell, z);
                    px[t * MapW + ((ix % MapW) + MapW) % MapW] = new Color32((byte)(m.powder * 255f + 0.5f), (byte)(m.ice * 255f + 0.5f), (byte)(m.crud * 255f + 0.5f), (byte)(m.crust * 255f + 0.5f));
                }
                left -= to - from;
                rowEpoch[t] = epoch; rowIz[t] = iz; rowX0[t] = x0;
                dirty = true;
            }
            if (dirty) { map.SetPixels32(px); map.Apply(false); }

            runShown = dt > 0f ? Mathf.Lerp(runShown, run, 1f - Mathf.Exp(-dt * 3f)) : run;
            Shader.SetGlobalTexture(MapId, map);
            Shader.SetGlobalVector(StId, new Vector4(1f / (Cell * MapW), 1f / (Cell * MapH), 0f, 0f));
            Shader.SetGlobalVector(WinId, new Vector4((x0 + MapW / 2) * Cell, (z0 + MapH / 2) * Cell, (MapW / 2 - 1) * Cell, (MapH / 2 - 1) * Cell));
            // (one kind everywhere, for the render tools: its slush everywhere too)
            Shader.SetGlobalVector(DayId, Force.HasValue ? new Vector4(-1e6f, 1f, Force.Value.slush, Day.fresh)
                                                         : new Vector4(Slope.FinishZ - SlushAbove, 1f / SlushRamp, SlushMost * Day.warm * (1f - Day.fresh), Day.fresh));
            Shader.SetGlobalVector(RunId, new Vector4(Mathf.Cos(runShown), -Mathf.Sin(runShown), On ? 1f : 0f, 0f));
        }

        static bool Same(in SnowMix a, in SnowMix b) => a.powder == b.powder && a.ice == b.ice && a.crud == b.crud && a.crust == b.crust && a.slush == b.slush;

        /// <summary>The share of the map painted for where it is now (the test tools).</summary>
        public static float Painted(Vector3 focus)
        {
            if (map == null) return 0f;
            int x0 = Mathf.FloorToInt(focus.x / Cell) - MapW / 2, z0 = Mathf.FloorToInt(focus.z / Cell) - Behind, n = 0;
            for (int r = 0; r < MapH; r++) { int iz = z0 + r, t = ((iz % MapH) + MapH) % MapH; if (rowEpoch[t] == epoch && rowIz[t] == iz && rowX0[t] == x0) n++; }
            return n / (float)MapH;
        }
    }
}
