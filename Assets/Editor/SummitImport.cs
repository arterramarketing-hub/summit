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
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
