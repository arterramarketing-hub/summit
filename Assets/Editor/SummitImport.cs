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
        string macroA = dir + "SnowMacro.png", macroN = dir + "SnowMacroNormal.png", detA = dir + "SnowDetail.png", detN = dir + "SnowDetailNormal.png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(detN) == null)
        {
            // large: domain-warped drifts, no periodic stripes
            MakePair(512, macroA, macroN, (u, v) =>
            {
                float wu = u + 0.08f * Tile(u, v, 3, 2.2f), wv = v + 0.08f * Tile(u + 0.3f, v, 3, 5.1f);
                return 0.55f * Tile(wu, wv, 5, 1.7f) + 0.30f * Tile(wu, wv, 11, 3.3f) + 0.15f * Tile(wu, wv, 23, 8.8f);
            }, 3f, 0.05f);
            // fine: grain and tiny wind ridges
            MakePair(256, detA, detN, (u, v) =>
            {
                float wu = u + 0.05f * Tile(u, v, 4, 6.2f);
                return 0.6f * Tile(wu, v, 16, 4.4f) + 0.4f * Tile(wu, v, 48, 9.9f);
            }, 2.2f, 0.10f);
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(dir + "Snow.mat");
        if (mat == null) return;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(macroA));
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(macroN));
        mat.SetFloat("_BumpScale", 0.6f);
        mat.SetTextureScale("_BaseMap", new Vector2(0.25f, 0.25f));     // uv is metres / 9, so ~36 m per tile
        mat.SetTexture("_DetailAlbedoMap", AssetDatabase.LoadAssetAtPath<Texture2D>(detA));
        mat.SetTexture("_DetailNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(detN));
        mat.SetTextureScale("_DetailAlbedoMap", new Vector2(4.37f, 4.37f)); // ~2 m, an odd ratio to the large map
        mat.SetFloat("_DetailAlbedoMapScale", 1f);
        mat.SetFloat("_DetailNormalMapScale", 0.8f);
        mat.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f));
        mat.SetFloat("_Smoothness", 0.32f);
        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_DETAIL_MULX2");
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
    }

    static void MakePair(int N, string albedoPath, string normalPath, System.Func<float, float, float> height, float bump, float contrast)
    {
        var h = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++) h[y * N + x] = height(x / (float)N, y / (float)N);
        var alb = new Texture2D(N, N, TextureFormat.RGB24, true);
        var nrm = new Texture2D(N, N, TextureFormat.RGB24, true);
        var rng = new System.Random(N);
        bool detail = N < 512;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float c = h[y * N + x];
                float dx = h[y * N + (x + 1) % N] - h[y * N + (x + N - 1) % N];
                float dy = h[((y + 1) % N) * N + x] - h[((y + N - 1) % N) * N + x];
                var n = new Vector3(-dx * bump * N / 64f, -dy * bump * N / 64f, 1f).normalized;
                nrm.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f));
                // detail maps multiply around mid-grey (x2); the large map is the snow colour itself
                float g = detail ? 0.5f + (c - 0.5f) * contrast * 2f + (rng.NextDouble() < 0.003 ? 0.05f : 0f)
                                 : 0.94f + (c - 0.5f) * contrast * 2f;
                alb.SetPixel(x, y, new Color(g * 0.99f, g, Mathf.Min(1f, g * 1.015f)));
            }
        System.IO.File.WriteAllBytes(albedoPath, alb.EncodeToPNG());
        System.IO.File.WriteAllBytes(normalPath, nrm.EncodeToPNG());
        AssetDatabase.ImportAsset(albedoPath);
        AssetDatabase.ImportAsset(normalPath);
        var ti = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        ti.textureType = TextureImporterType.NormalMap;
        ti.SaveAndReimport();
        var ta = (TextureImporter)AssetImporter.GetAtPath(albedoPath);
        ta.sRGBTexture = !detail;
        ta.SaveAndReimport();
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

    void OnPreprocessAudio()
    {
        if (!assetPath.Contains("Summit/Resources/Audio")) return;
        var a = (AudioImporter)assetImporter;
        var s = a.defaultSampleSettings;
        bool loop = assetPath.EndsWith("wind.ogg") || assetPath.EndsWith("carve.wav");
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
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
