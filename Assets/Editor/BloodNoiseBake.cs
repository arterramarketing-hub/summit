using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the blood marks' noise (Blood.MakeNoise: four tileable layers, made from nothing but numbers) into
/// Assets/Summit/Resources/Blood/BloodNoise.png, linear, repeating, mipmapped, so the game loads it rather than
/// working it out at boot. Run: Unity -batchmode -nographics -projectPath . -executeMethod BloodNoiseBake.Run
/// </summary>
public static class BloodNoiseBake
{
    const string Path_ = "Assets/Summit/Resources/Blood/BloodNoise.png";

    [MenuItem("Summit/Bake Blood Noise")]
    public static void Run()
    {
        var t = Summit.Blood.MakeNoise();
        File.WriteAllBytes(Path_, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(Path_, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(Path_);
        imp.sRGBTexture = false;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.filterMode = FilterMode.Bilinear;
        imp.anisoLevel = 2;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.alphaIsTransparency = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        Debug.Log("BLOODNOISE baked " + Path_);
        if (Application.isBatchMode) TestHarness.Exit(0);
    }
}
