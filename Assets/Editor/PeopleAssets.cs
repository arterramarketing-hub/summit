using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets the mountain's people need as files: a GPU-instancing material of Resources/PeopleLit.shader. The project
/// strips unused instancing variants ("Strip Unused", ProjectSettings/GraphicsSettings), so a shader only ever used by
/// a material made at run time loses them in a player build, and every lift rider drawn instanced (all but the few
/// nearest) comes out as nothing on the phone. PeopleKit.LodMaterial starts from this asset.
/// Batch: -executeMethod PeopleAssets.Ensure (PeopleShot runs it too).
/// </summary>
public static class PeopleAssets
{
    const string LitPath = "Assets/Summit/Resources/PeopleLit.mat";

    public static void Ensure()
    {
        var have = AssetDatabase.LoadAssetAtPath<Material>(LitPath);
        if (have != null && have.enableInstancing) return;
        var sh = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Summit/Resources/PeopleLit.shader");
        if (sh == null) { Debug.LogWarning("PeopleAssets: Resources/PeopleLit.shader is missing"); return; }
        if (have != null) { have.enableInstancing = true; EditorUtility.SetDirty(have); AssetDatabase.SaveAssets(); return; }
        var m = new Material(sh) { name = "PeopleLit", enableInstancing = true };
        AssetDatabase.CreateAsset(m, LitPath);
        AssetDatabase.SaveAssets();
        Debug.Log("PeopleAssets: made " + LitPath + " (GPU instancing on)");
    }
}
