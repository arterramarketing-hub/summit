using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets the summit camp needs that must exist as files: a transparent Lit material for the chalet's glass
/// (made at runtime it would be missing its shader variant in a player build).
/// Batch: -executeMethod CampAssets.Ensure (HubShot runs it too).
/// </summary>
public static class CampAssets
{
    const string GlassPath = "Assets/Summit/Resources/Camp/GlassBase.mat";

    public static void Ensure()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(GlassPath) != null) return;
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_BaseColor", new Color(0.62f, 0.74f, 0.82f, 0.22f));
        m.SetFloat("_Smoothness", 0.95f);
        AssetDatabase.CreateAsset(m, GlassPath);
        AssetDatabase.SaveAssets();
    }
}
