using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Bare rock for faces too steep to hold snow: ridge cliffs, and the odd steep bank in the open mountain.
    /// The terrain chunks draw those triangles with this material instead of the snow (see SlopeStreamer).
    /// The rock is a scanned cliff face (Resources/RockTex, shaded by Resources/RockLit.shader): its colour, and
    /// its relief, which bends the light and, near the camera, gives the blocks real depth (parallax).
    /// <see cref="Parallax"/> is the one quality switch for that depth. Only where that shader cannot run is the
    /// texture made here, procedurally: grey granite with dark cracks and pale bands of rime in them.
    /// </summary>
    public static class RockFace
    {
        /// <summary>Faces whose normal points less upward than this are rock (about 46 degrees and steeper).</summary>
        public const float SteepY = 0.69f;
        /// <summary>Size of one repeat of the rock texture on the face, in metres.</summary>
        public const float Tile = 11f;

        static Material shared;
        static bool parallax = false;

        /// <summary>
        /// The rock's depth near the camera (parallax occlusion mapping in RockLit.shader): off unless switched on,
        /// because it is the costly part and adds little to a still view beside the normal-mapped relief.
        /// Off, rock keeps that relief but costs a handful of texture samples less per pixel close up.
        /// Takes effect at once, on every rock face (one global shader keyword).
        /// </summary>
        public static bool Parallax
        {
            get { return parallax; }
            set
            {
                parallax = value;
                if (value) Shader.DisableKeyword(NoParallax); else Shader.EnableKeyword(NoParallax);
            }
        }
        const string NoParallax = "SUMMIT_ROCK_NO_PARALLAX";

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
            Parallax = parallax;   // the keyword is global and outlives a domain reload in the editor: make it match
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            shared = m;
            return m;
        }

        /// <summary>Is the triangle with this (unnormalised) face normal rock? The jitter frays the snow line.</summary>
        public static bool IsRock(Vector3 faceNormal, float x, float z)
        {
            float len = faceNormal.magnitude;
            if (len < 1e-6f) return false;
            float ny = faceNormal.y / len;
            // the groomed piste is always snow, even down the steep face of a roll-over; on a ridge, only the crest is
            if (Cliffs.OnJumpLanding(x, z) || Ridge.SnowAt(x, z)) return false;   // a jump cliff's landing, a ridge's snow side, a wall's apron
            if (Cliffs.OnJumpFace(x, z) || Cliffs.OnWallFace(x, z)) { }   // a cliff's rock band shows, piste or not
            // beside a ridge (and the spine below it), only its top: the canyon walls are rock right up to the edge
            else if (Ridge.Depth(z) > 0.05f) { if (Mathf.Abs(x - Slope.Centre(z)) < Ridge.HalfWidth(z) + Ridge.Shoulder * 0.5f) return false; }
            else if (Slope.Rough(x, z) < 0.5f) return false;
            return ny < SteepY + (Slope.Noise(x * 0.21f + 7f, z * 0.21f) - 0.5f) * 0.12f;
        }

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
