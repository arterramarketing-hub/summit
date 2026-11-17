using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for Summit's models and sounds, applied automatically:
/// the rider becomes a Humanoid (so it can be posed in muscle space), scenery imports
/// as plain static meshes, and the long loops decompress once at load so they never stutter.
/// </summary>
public class SummitImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.Contains("Summit/Resources")) return;
        var m = (ModelImporter)assetImporter;
        m.importCameras = false;
        m.importLights = false;
        m.bakeAxisConversion = true;   // these were made in Blender (Z up): stand them upright on import
        if (assetPath.Contains("/Animals/"))
        {
            // blend-shape gallop: legacy animation plays it with no controller; normals are missing in the file
            m.animationType = ModelImporterAnimationType.Legacy;
            m.importAnimation = true;
            m.importNormals = ModelImporterNormals.Calculate;
            m.normalSmoothingAngle = 0f;
            m.importBlendShapes = true;
            m.importBlendShapeNormals = ModelImporterNormals.None;
            var clips = m.defaultClipAnimations;
            foreach (var c in clips) { c.loopTime = true; c.wrapMode = WrapMode.Loop; }
            if (clips.Length > 0) m.clipAnimations = clips;
            return;
        }
        if (assetPath.Contains("/Rider/"))
        {
            m.animationType = ModelImporterAnimationType.Human;
            m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            m.importAnimation = false;
            m.isReadable = true;   // the game loosens the clothes and adds wind shapes to the meshes at start
        }
        else
        {
            m.animationType = ModelImporterAnimationType.None;
            m.importAnimation = false;
            m.importBlendShapes = false;
        }
    }

    void OnPreprocessTexture()
    {
        if (assetPath.Contains("Summit/Resources/Avalanche/"))
        {
            // the powder puffs hold a surface shape (normals and thickness), not colours: keep the numbers as they are
            var a = (TextureImporter)assetImporter;
            a.sRGBTexture = false;
            a.alphaSource = TextureImporterAlphaSource.FromInput;
            a.alphaIsTransparency = true;
            a.mipmapEnabled = true;
            a.wrapMode = TextureWrapMode.Clamp;
            a.maxTextureSize = 1024;
            var ios = a.GetPlatformTextureSettings("iPhone");
            ios.overridden = true; ios.maxTextureSize = 1024; ios.format = TextureImporterFormat.ASTC_5x5;
            a.SetPlatformTextureSettings(ios);
            return;
        }
        if (assetPath.Contains("Summit/Resources/RealTrees"))
        {
            // alpha-tested branch cards: keep the needles from thinning out to nothing in the smaller mips
            var r = (TextureImporter)assetImporter;
            r.alphaIsTransparency = true; r.alphaSource = TextureImporterAlphaSource.FromInput;
            r.mipmapEnabled = true; r.mipMapsPreserveCoverage = true; r.alphaTestReferenceValue = 0.5f;
            r.wrapMode = TextureWrapMode.Clamp; r.maxTextureSize = 1024;
            var ios = r.GetPlatformTextureSettings("iPhone");
            ios.overridden = true; ios.maxTextureSize = 1024; ios.format = TextureImporterFormat.ASTC_5x5;
            r.SetPlatformTextureSettings(ios);
            return;
        }
        if (!assetPath.Contains("Summit/Resources/Rider")) return;
        var t = (TextureImporter)assetImporter;
        t.filterMode = FilterMode.Point;    // a flat-colour palette: keep the colours crisp
        t.mipmapEnabled = false;
    }

    /// <summary>An opaque lit material that shows a model's vertex colours (the bear is coloured that way).</summary>
    public static void EnsureVertexColorMaterial()
    {
        const string path = "Assets/Summit/Resources/VertexColor.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Particles/Lit");
        if (sh == null) return;
        var m = new Material(sh);
        m.SetFloat("_Surface", 0f);
        m.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// The realistic fir materials (see RealFir): the branch-card atlas on Simple Lit, alpha-tested and two-sided,
    /// and an unlit alpha-tested one for the far-away impostor pictures. Kept in Resources so they ship.
    /// </summary>
    public static void EnsureRealTrees()
    {
        const string dir = "Assets/Summit/Resources/RealTrees";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/FirCards.png");
        if (tex == null) return;
        Material Make(string name, string shader, bool lit)
        {
            string path = dir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (sh == null) return m;
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);   // both sides
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.enableInstancing = true;
            if (lit)
            {
                m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_SpecularHighlights", 0f); m.DisableKeyword("_SPECULAR_COLOR");
                m.SetFloat("_ReceiveShadows", 1f);
            }
            else m.SetColor("_BaseColor", new Color(0.92f, 0.95f, 0.98f));
            EditorUtility.SetDirty(m);
            return m;
        }
        Make("FirCards", "Universal Render Pipeline/Simple Lit", true);
        Make("FirImpostor", "Universal Render Pipeline/Unlit", false);
        AssetDatabase.SaveAssets();
    }

    /// <summary>A lit see-through particle material kept in Resources, so its shader ships in the build.</summary>
    public static void EnsureImprintMaterial()
    {
        const string path = "Assets/Summit/Resources/ImprintBase.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
        if (sh == null) return;
        AssetDatabase.CreateAsset(new Material(sh), path);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Gives the snow a surface without visible repeats: a large map (one tile spans ~36 m of slope)
    /// for broad drifts and wind-scoured patches, plus a fine detail layer (~2 m) for grain and sparkle,
    /// on separate tilings so neither pattern lines up with the other.
    /// </summary>
    public static void EnsureSnowTextures()
    {
        const string dir = "Assets/Summit/Materials/";
        string macroA = dir + "SnowGroovesAlbedo.png", macroN = dir + "SnowGroovesNormal.png", detA = dir + "SnowGrain.png", detN = dir + "SnowGrainNormal.png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(detN) == null)
        {
            foreach (var old in new[] { "SnowMacro.png", "SnowMacroNormal.png", "SnowDetail.png", "SnowDetailNormal.png", "SnowAlbedo.png", "SnowNormal.png" })
                AssetDatabase.DeleteAsset(dir + old);
            // one 2048 tile spans ~36 m: wind grooves ~22 cm apart, bent by warps, swinging between three
            // directions and fading out in patches, over broad drifts, so no two spots in a tile look alike
            MakePair(2048, macroA, macroN, Grooves, 0.9f, 0.035f, false);
            // a fine grain layer (~2.7 m) on a tiling that never lines up with the grooves
            MakePair(512, detA, detN, (u, v) => 0.55f * Tile(u, v, 24, 4.4f) + 0.3f * Tile(u, v, 64, 9.9f) + 0.15f * Tile(u, v, 128, 2.7f), 0.5f, 0.06f, true);
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(dir + "Snow.mat");
        if (mat == null) return;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(macroA));
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(macroN));
        mat.SetFloat("_BumpScale", 1f);
        mat.SetTextureScale("_BaseMap", new Vector2(0.25f, 0.25f));        // uv is metres / 9, so ~36 m per tile
        mat.SetTexture("_DetailAlbedoMap", AssetDatabase.LoadAssetAtPath<Texture2D>(detA));
        mat.SetTexture("_DetailNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(detN));
        mat.SetTextureScale("_DetailAlbedoMap", new Vector2(3.37f, 3.37f));  // ~2.7 m, an odd ratio to the grooves
        mat.SetTextureOffset("_DetailAlbedoMap", new Vector2(0.37f, 0.71f));
        mat.SetFloat("_DetailAlbedoMapScale", 1f);
        mat.SetFloat("_DetailNormalMapScale", 0.5f);
        mat.SetColor("_BaseColor", new Color(0.88f, 0.905f, 0.95f));   // just under white, so glints and highlights can stand out
        mat.SetFloat("_Smoothness", 0.32f);
        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_DETAIL_MULX2");
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
    }

    /// <summary>The mountain bands (Resources/Backdrop/*.png from Tools/MakeSky.swift) and their materials.</summary>
    public static void EnsureBackdrop()
    {
        var sh = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Summit/Shaders/Backdrop.shader");
        if (sh == null) return;
        foreach (var n in new[] { "Far", "Mid", "Near", "Photo" })
        {
            string tex = "Assets/Summit/Resources/Backdrop/Backdrop" + n + ".png", mp = "Assets/Summit/Resources/Backdrop/" + n + ".mat";
            var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
            if (ti == null) continue;
            if (ti.mipmapEnabled || !ti.alphaIsTransparency || ti.wrapModeU != TextureWrapMode.Repeat)
            {
                ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp; ti.maxTextureSize = 4096;
                var ios = ti.GetPlatformTextureSettings("iPhone");
                ios.overridden = true; ios.maxTextureSize = 4096; ios.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(ios);
                ti.SaveAndReimport();
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, mp); }
            m.shader = sh;
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
            m.renderQueue = 2940 + (n == "Far" ? 0 : n == "Mid" ? 1 : n == "Near" ? 2 : 3);   // far, then mid, then near
            if (n == "Photo")
            {
                // where its sky is (Tools/MakePhotoSky.swift), for the time of day to show the game's sky through it,
                // and where the picture ends and its haze begins (Tools/MakePhotoBand.swift: 0.323 of 1.06 rad)
                const string mask = "Assets/Summit/Resources/Backdrop/BackdropPhotoSky.png";
                var mi = (TextureImporter)AssetImporter.GetAtPath(mask);
                if (mi != null && (mi.mipmapEnabled || mi.sRGBTexture || mi.textureType != TextureImporterType.SingleChannel))
                {
                    mi.textureType = TextureImporterType.SingleChannel; mi.sRGBTexture = false; mi.mipmapEnabled = false;
                    mi.wrapModeU = TextureWrapMode.Repeat; mi.wrapModeV = TextureWrapMode.Clamp; mi.maxTextureSize = 4096;
                    var ms = new TextureImporterSettings(); mi.ReadTextureSettings(ms); ms.singleChannelComponent = TextureImporterSingleChannelComponent.Red; mi.SetTextureSettings(ms);
                    var ios = mi.GetPlatformTextureSettings("iPhone");
                    ios.overridden = true; ios.maxTextureSize = 2048; ios.format = TextureImporterFormat.R8;
                    mi.SetPlatformTextureSettings(ios);
                    mi.SaveAndReimport();
                }
                m.SetTexture("_SkyMask", AssetDatabase.LoadAssetAtPath<Texture2D>(mask));
                m.SetFloat("_HazeBottom", 0.323f / 1.06f);
            }
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>The avalanche's powder cloud material (Resources/Avalanche/Powder.mat), kept in Resources so its shader ships.</summary>
    public static void EnsureAvalanche()
    {
        const string path = "Assets/Summit/Resources/Avalanche/Powder.mat";
        var sh = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Summit/Shaders/Powder.shader");
        if (sh == null) return;
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Summit/Resources/Avalanche/PuffAtlas.png"));
        m.SetFloat("_Sun", 0.8f);
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
    }

    /// <summary>The scene's own sky (Materials/SkyPanorama.png on Skybox/Panoramic, a fallback: DayNight replaces it with the
    /// photographed time-of-day skies in Resources/Sky), and those skies' cubemap import.</summary>
    public static void EnsureSky()
    {
        const string tex = "Assets/Summit/Materials/SkyPanorama.png", matPath = "Assets/Summit/Materials/Sky.mat";
        ImportPanorama(tex);
        // the photographed time-of-day skies (Resources/Sky, Tools/MakeSkies.swift), as cubemaps
        foreach (var n in new[] { "Morning", "Day", "Evening", "Night" }) ImportSkyCube("Assets/Summit/Resources/Sky/Sky" + n + ".jpg");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        var sh = Shader.Find("Skybox/Panoramic");
        if (mat == null || sh == null) return;
        mat.shader = sh;
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
        mat.SetFloat("_Mapping", 1f);       // latitude-longitude
        mat.SetFloat("_ImageType", 0f);     // 360 degrees
        mat.SetFloat("_Exposure", 1f);
        mat.SetFloat("_Rotation", 0f);
        mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// A lat-long sky picture as a cubemap: no seam where the picture's ends meet and no pinch overhead, as a
    /// panorama has; 1024 a face, which is as sharp as the 4096-wide photos are; with mips, for the snow's
    /// reflections of it (DayNight uses them as the reflection cubemap).
    /// </summary>
    static void ImportSkyCube(string tex)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
        if (ti == null) return;
        var ios = ti.GetPlatformTextureSettings("iPhone");
        if (ti.textureShape == TextureImporterShape.TextureCube && ti.mipmapEnabled && ios.overridden && ios.format == TextureImporterFormat.ASTC_4x4) return;
        ti.textureShape = TextureImporterShape.TextureCube;
        var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
        st.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
        st.cubemapConvolution = TextureImporterCubemapConvolution.None;
        st.seamlessCubemap = true;
        ti.SetTextureSettings(st);
        ti.mipmapEnabled = true; ti.sRGBTexture = true;
        ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear;
        ti.maxTextureSize = 1024;
        // 4x4 blocks: the skies are stored squeezed (y/(1+y)) and unsqueezing magnifies block error near the sun
        ios.overridden = true; ios.maxTextureSize = 1024; ios.format = TextureImporterFormat.ASTC_4x4;
        ti.SetPlatformTextureSettings(ios);
        ti.SaveAndReimport();
    }

    static void ImportPanorama(string tex)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
        if (ti == null) return;
        bool dirty = ti.mipmapEnabled || ti.maxTextureSize != 4096 || ti.wrapModeU != TextureWrapMode.Repeat;
        if (dirty)
        {
            ti.mipmapEnabled = false;                 // mips put a seam down the back of a panorama
            ti.maxTextureSize = 4096;
            ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.sRGBTexture = true;
            var ios = ti.GetPlatformTextureSettings("iPhone");
            ios.overridden = true; ios.maxTextureSize = 4096; ios.format = TextureImporterFormat.ASTC_5x5;
            ti.SetPlatformTextureSettings(ios);
            ti.SaveAndReimport();
        }
    }

    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    // a wind ripple: a gentle windward face and a short steep lee side
    static float Ridge(float phase)
    {
        float s = phase - Mathf.Floor(phase);
        float r = s < 0.72f ? s / 0.72f : (1f - s) / 0.28f;
        return r * r * (3f - 2f * r);
    }

    static float Grooves(float u, float v)
    {
        // one groove field, its phase pushed around by big, slow warps: the grooves swing up to ~35 degrees
        // either way, bunch up and spread out, and bend, with no cross-hatching (integer frequencies keep
        // the tile seamless)
        float w = 26f * Tile(u, v, 2, 1.3f) + 9f * Tile(u, v, 5, 6.6f) + 2.2f * Tile(u, v, 13, 3.9f) + 0.4f * Tile(u, v, 41, 2.2f);
        float ripple = Ridge(164f * v + w);
        // the wind has scoured some places smooth and left deep grooves in others
        float amount = Smooth(0.42f, 0.62f, Tile(u, v, 4, 4.8f) * 0.65f + Tile(u, v, 9, 8.1f) * 0.35f);
        amount = 0.05f + 0.95f * amount * (0.55f + 0.45f * Tile(u, v, 13, 5.2f));
        float drift = 0.6f * Tile(u, v, 5, 1.7f) + 0.3f * Tile(u, v, 11, 3.3f) + 0.1f * Tile(u, v, 23, 8.8f);
        return drift * 0.55f + ripple * amount * 0.035f + Tile(u, v, 256, 5.5f) * 0.004f;
    }

    static void MakePair(int N, string albedoPath, string normalPath, System.Func<float, float, float> height, float bump, float contrast, bool detail)
    {
        var h = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++) h[y * N + x] = height(x / (float)N, y / (float)N);
        var alb = new Texture2D(N, N, TextureFormat.RGB24, true);
        var nrm = new Texture2D(N, N, TextureFormat.RGB24, true);
        var rng = new System.Random(N);
        // slope per metre-ish: scale the per-pixel difference by the resolution
        float k = bump * N / 64f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float c = h[y * N + x];
                float dx = h[y * N + (x + 1) % N] - h[y * N + (x + N - 1) % N];
                float dy = h[((y + 1) % N) * N + x] - h[((y + N - 1) % N) * N + x];
                var n = new Vector3(-dx * k, -dy * k, 1f).normalized;
                nrm.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f));
                // detail maps multiply around mid-grey (x2); the large map is the snow colour itself,
                // a touch darker in the groove troughs and on lee faces
                float g = detail ? 0.5f + (c - 0.5f) * contrast * 2f + (rng.NextDouble() < 0.003 ? 0.05f : 0f)
                                 : 0.95f + (c - 0.5f) * contrast * 2f - Mathf.Max(0f, -n.y) * 0.05f;
                alb.SetPixel(x, y, new Color(g * 0.99f, g, Mathf.Min(1f, g * 1.015f)));
            }
        System.IO.File.WriteAllBytes(albedoPath, alb.EncodeToPNG());
        System.IO.File.WriteAllBytes(normalPath, nrm.EncodeToPNG());
        AssetDatabase.ImportAsset(albedoPath);
        AssetDatabase.ImportAsset(normalPath);
        foreach (var path in new[] { albedoPath, normalPath })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            bool isNormal = path == normalPath;
            if (isNormal) ti.textureType = TextureImporterType.NormalMap;
            else ti.sRGBTexture = !detail;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 8;                       // stays sharp at the low angle you see the slope from
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Repeat;
            var ios = ti.GetPlatformTextureSettings("iPhone");
            ios.overridden = true;
            ios.maxTextureSize = 2048;
            ios.format = isNormal ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_5x5;
            ti.SetPlatformTextureSettings(ios);
            ti.SaveAndReimport();
        }
    }

    // value noise that wraps every `period` cells
    static float Tile(float u, float v, int period, float seed)
    {
        float x = u * period, y = v * period;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float H(int i, int j) { i = ((i % period) + period) % period; j = ((j % period) + period) % period; return Mathf.Repeat(Mathf.Sin(i * 127.1f + j * 311.7f + seed * 74.7f) * 43758.5453f, 1f); }
        return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx), fy);
    }

    // long, slightly wavy wind ripples
    static float Ripple(float u, float v)
    {
        float w = Tile(u, v, 3, 9.1f);
        return 0.5f + 0.5f * Mathf.Sin((v * 14f + w * 1.2f) * Mathf.PI * 2f);
    }

    // the long, continuously playing layers: compressed; everything short stays sharp (ADPCM)
    static readonly string[] LoopClips = { "wind_low", "wind_high", "glide", "carve", "skid", "powder", "grind_rail", "grind_box",
                                           "avalanche", "boost", "ambience", "bear_growl", "bear_steps", "avalanche_near" };

    void OnPreprocessAudio()
    {
        if (!assetPath.Contains("Summit/Resources/Audio")) return;
        var a = (AudioImporter)assetImporter;
        var s = a.defaultSampleSettings;
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        if (file.StartsWith("music_"))
        {
            // the soundtrack (Tools/MakeMusic.swift): kept in stereo, and streamed from disk as it plays
            // rather than decoded into memory (two minutes of stereo would take ~20 MB of RAM each)
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            a.defaultSampleSettings = s;
            a.forceToMono = false;
            a.loadInBackground = true;
            return;
        }
        bool loop = System.Array.IndexOf(LoopClips, file) >= 0;
        s.loadType = AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = loop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
        s.quality = 0.7f;
        a.defaultSampleSettings = s;
        a.forceToMono = true;
        a.loadInBackground = false;
    }

    /// <summary>Re-import Summit's models and sounds with these settings (batch: -executeMethod SummitImport.Reimport).</summary>
    [MenuItem("Summit/Reimport Assets")]
    public static void Reimport()
    {
        AssetDatabase.ImportAsset("Assets/Summit/Resources", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        EnsureImprintMaterial();
        EnsureVertexColorMaterial();
        EnsureSnowTextures();
        EnsureSky();
        EnsureBackdrop();
        EnsureAvalanche();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
