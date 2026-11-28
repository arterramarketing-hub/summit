using UnityEditor;

/// <summary>
/// The camp's trampled snow (Resources/Camp/SnowPrints.png, Tools/MakeSnowPrints.py) imported as the data it is, not a
/// colour: linear, mipmapped, tiling, trilinear; ASTC 5x5 on the phone (its slopes and its prints' numbers keep).
/// </summary>
public class CampSnowImport : AssetPostprocessor
{
    const string Prints = "Assets/Summit/Resources/Camp/SnowPrints.png";

    void OnPreprocessTexture()
    {
        if (assetPath != Prints) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.mipmapEnabled = true;
        ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
        ti.filterMode = UnityEngine.FilterMode.Trilinear;
        ti.anisoLevel = 2;
        ti.maxTextureSize = 512;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 512, format = TextureImporterFormat.ASTC_5x5, compressionQuality = 50 });
    }
}
