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
    /// Gives the snow a surface: fine grain and wind-rippled relief (a tiling colour map and normal map),
    /// generated once and set on Materials/Snow.mat with normal mapping switched on.
    /// </summary>
    public static void EnsureSnowTextures()
    {
        const string dir = "Assets/Summit/Materials/";
        string albedoPath = dir + "SnowAlbedo.png", normalPath = dir + "SnowNormal.png";
        const int N = 512;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) == null)
        {
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    // tileable: sample noise on a torus of periods
                    float r = 0f;
                    r += 0.55f * Tile(u, v, 4, 1.7f) ;          // soft drifts
                    r += 0.30f * Ripple(u, v);                    // wind ripples
                    r += 0.15f * Tile(u, v, 32, 7.3f);           // grain
                    h[y * N + x] = r;
                }
            var alb = new Texture2D(N, N, TextureFormat.RGB24, true);
            var nrm = new Texture2D(N, N, TextureFormat.RGB24, true);
            var rng = new System.Random(5);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float c = h[y * N + x];
                    float dx = h[y * N + (x + 1) % N] - h[y * N + (x + N - 1) % N];
                    float dy = h[((y + 1) % N) * N + x] - h[((y + N - 1) % N) * N + x];
                    var n = new Vector3(-dx * 6f, -dy * 6f, 1f).normalized;
                    nrm.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f));
                    float sparkle = rng.NextDouble() < 0.004 ? 0.06f : 0f;
                    float g = 0.93f + (c - 0.5f) * 0.10f + sparkle;
                    alb.SetPixel(x, y, new Color(g * 0.98f, g * 0.99f, Mathf.Min(1f, g * 1.02f)));
                }
            System.IO.File.WriteAllBytes(albedoPath, alb.EncodeToPNG());
            System.IO.File.WriteAllBytes(normalPath, nrm.EncodeToPNG());
            AssetDatabase.ImportAsset(albedoPath);
            AssetDatabase.ImportAsset(normalPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(dir + "Snow.mat");
        if (mat == null) return;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        mat.SetFloat("_BumpScale", 0.7f);
        mat.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
        mat.SetColor("_BaseColor", new Color(0.94f, 0.96f, 1f));
        mat.SetFloat("_Smoothness", 0.35f);
        mat.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
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
        EnsureSnowTextures();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
