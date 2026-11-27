using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Bare rock for faces too steep to hold snow: ridge cliffs, and the odd steep bank in the open mountain.
    /// The terrain chunks draw those triangles with this material instead of the snow (see SlopeStreamer).
    /// The rock is a scanned cliff face (Resources/RockTex, shaded by Resources/RockLit.shader): its colour, and
    /// its relief, which bends the light.
    /// Only where that shader cannot run is the
    /// texture made here, procedurally: grey granite with dark cracks and pale bands of rime in them.
    /// </summary>
    public static class RockFace
    {
        /// <summary>Faces whose normal points less upward than this are rock (about 46 degrees and steeper).</summary>
        public const float SteepY = 0.69f;
        /// <summary>Size of one repeat of the rock texture on the face, in metres.</summary>
        public const float Tile = 11f;

        static Material shared;

        public static Material Material(Shader lit)
        {
            if (shared != null) return shared;
            // one rock for every rock face on the mountain: Tools/MakeRockScan.py's scanned cliff, mapped in world space
            // with relief, strata and caught snow by Resources/RockLit.shader (the plain granite below only where that
            // cannot run)
            var sh = Resources.Load<Shader>("RockLit");
            var tex = Resources.Load<Texture2D>("RockTex/RockAlbedo");
            var relief = Resources.Load<Texture2D>("RockTex/RockRelief");
            bool full = sh != null && sh.isSupported && tex != null;
            var m = new Material(full ? sh : lit) { name = "Rock" };
            m.SetTexture("_BaseMap", full ? tex : Texture());
            if (full && relief != null) m.SetTexture("_ReliefMap", relief);   // (without it the shader's default is a flat face)
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            shared = m;
            return m;
        }

        /// <summary>Is the triangle with this (unnormalised) face normal rock? (The physics' coarse view of what the
        /// terrain draws: the drawn line wanders across triangles, pixel by pixel, round the same 0.5 of Rockiness.)</summary>
        public static bool IsRock(Vector3 faceNormal, float x, float z)
        {
            float len = faceNormal.magnitude;
            if (len < 1e-6f) return false;
            return Rockiness(faceNormal.y / len, x, z) > 0.5f;
        }

        // how wide the snow line is, in normal.y: from all snow to all rock over about twenty degrees of slope, so it
        // changes slowly across the terrain's triangles and the rock's own patches decide where it really falls
        const float Band = 0.28f;

        /// <summary>
        /// How bare the ground is, 0 (snow) to 1 (rock), for a surface whose normal points ny up, at (x, z). Snow holds
        /// on anything gentler than about 46 degrees and thins over a few degrees either side of that, wandering with
        /// the ground; it lies on longer in hollows and gullies and blows off ribs and crests sooner (`hollow`: how
        /// far the ground here sits below its neighbours, in metres). The terrain draws the line pixel by pixel from
        /// this, per corner, and the rock's own cracks and ledges (RockLit.shader, SUMMIT_ROCK_TERRAIN).
        /// Never rock on the groomed piste or a jump cliff's landing, a ridge's crest or snow side, or a gorge's apron
        /// (each faded in over a few metres, so no line is ever ruled straight); always able to be on a cliff's band.
        /// </summary>
        public static float Rockiness(float ny, float x, float z, float hollow = 0f)
        {
            if (Cliffs.OnJumpLanding(x, z) || Ridge.SnowAt(x, z) || Cliffs.OnLedgeSnow(x, z) || CliffBand.OnBench(x, z)) return 0f;   // a jump cliff's landing, a ridge's snow side, a wall's apron
            if (Cliffs.OnCorniceFace(x, z)) return 0f;   // a cornice's face: wind-packed snow, not a rock band (Cliffs)
            float allowed = 1f;
            if (Cliffs.OnJumpFace(x, z) || Cliffs.OnWallFace(x, z) || Cliffs.OnLedgeFace(x, z) || Slope.OnStepFace(x, z) || CliffBand.OnFace(x, z)) { }   // a cliff's rock band shows, piste or not
            // beside a ridge (and the spine below it), only its top: the canyon walls are rock right up to the edge
            else if (Ridge.Depth(z) > 0.05f)
                allowed = Mathf.Clamp01((Mathf.Abs(x - Slope.Centre(z)) - Ridge.HalfWidth(z) - Ridge.Shoulder * 0.25f) / (Ridge.Shoulder * 0.5f));
            // the groomed piste is always snow, even down the steep face of a roll-over: faded in across its edge
            else allowed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, Slope.Rough(x, z)));
            if (allowed <= 0f) return 0f;
            float line = SteepY + (Slope.Noise(x * 0.09f + 7f, z * 0.09f) - 0.5f) * 0.14f - Mathf.Clamp(hollow, -1.5f, 1.5f) * 0.04f;
            float bare = Mathf.Clamp01((line - ny) / Band + 0.5f) * allowed;
            // a deadly wall's face is rock all the way down, even where its waves ease off (only dusted with snow there):
            // a patch of snow part way down would catch a falling body and start its fall again from there, and it lives
            if (Cliffs.OnWallFace(x, z)) bare = Mathf.Max(bare, 0.8f);
            return bare;
        }

        /// <summary>
        /// The terrain's own rock: the same rock, drawn over the snow (one step later in the queue) wherever any of it
        /// may show, and kept or let go pixel by pixel (SUMMIT_ROCK_TERRAIN in RockLit.shader; the terrain puts each
        /// corner's Rockiness in its colour's alpha).
        /// </summary>
        public static Material TerrainMaterial(Shader lit)
        {
            if (terrain != null) return terrain;
            terrain = new Material(Material(lit)) { name = "TerrainRock" };
            terrain.EnableKeyword("SUMMIT_ROCK_TERRAIN");
            terrain.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
            return terrain;
        }
        static Material terrain;

        static Texture2D Texture()
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 2, name = "RockFace" };
            var px = new Color[N * N];
            Color dark = new Color(0.21f, 0.21f, 0.23f), light = new Color(0.47f, 0.45f, 0.43f), rime = new Color(0.82f, 0.86f, 0.92f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;   // v runs up the face
                    // blotchy granite: a few octaves of noise that wrap at the edges
                    float tone = 0.55f * Fbm(u, v, 4, 4, 1) + 0.25f * Fbm(u, v, 16, 3, 2) + 0.2f * (Hash(x, y) * 0.6f + 0.2f);
                    // dark streaks where meltwater runs down the face: long in v, narrow in u
                    float streak = Fbm(u, v, 1, 1, 3) * 0.3f + Fbm(u, v, 9, 2, 4) * 0.7f;
                    float wet = Mathf.Clamp01((streak - 0.62f) * 5f) * Mathf.Clamp01((Fbm(u, v, 3, 3, 5) - 0.35f) * 3f);
                    Color c = Color.Lerp(dark, light, Mathf.SmoothStep(0.2f, 0.8f, tone));
                    c = Color.Lerp(c, dark * 0.75f, wet * 0.5f);
                    // rime and old snow caught in the rough patches
                    float snow = Fbm(u, v, 8, 6, 6);
                    if (snow > 0.7f) c = Color.Lerp(c, rime, Mathf.Clamp01((snow - 0.7f) * 6f) * 0.8f);
                    px[y * N + x] = c;
                }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        /// <summary>Smooth value noise in [0, 1] that wraps every 1 in u and v: fx by fy cells, then octaves at twice the count.</summary>
        static float Fbm(float u, float v, int fx, int fy, int salt)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < 3; o++)
            {
                sum += amp * Lattice(u * fx, v * fy, fx, fy, salt * 31 + o);
                norm += amp; amp *= 0.5f; fx *= 2; fy *= 2;
            }
            return sum / norm;
        }

        static float Lattice(float x, float y, int px, int py, int salt)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % px) + px) % px, xb = (xa + 1) % px, ya = ((y0 % py) + py) % py, yb = (ya + 1) % py;
            float a = Hash(xa + salt * 977, ya), b = Hash(xb + salt * 977, ya), c = Hash(xa + salt * 977, yb), d = Hash(xb + salt * 977, yb);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return ((uint)(h ^ (int)((uint)h >> 16))) / 4294967296f;
            }
        }
    }
}
