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
    // the lodge's glass glows from inside at dusk: transparent with emission, its own variant (CampLodge.LodgeGlass)
    const string GlowPath = "Assets/Summit/Resources/Camp/GlassGlow.mat";

    public static void Ensure()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(GlowPath) == null)
        {
            var g = Transparent();
            g.EnableKeyword("_EMISSION");
            g.SetColor("_EmissionColor", new Color(0.15f, 0.09f, 0.05f));
            g.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(g, GlowPath);
            AssetDatabase.SaveAssets();
        }
        if (AssetDatabase.LoadAssetAtPath<Material>(GlassPath) != null) return;
        var m = Transparent();
        AssetDatabase.CreateAsset(m, GlassPath);
        AssetDatabase.SaveAssets();
    }

    static Material Transparent()
    {
        var m = new Material(Summit.Mats.LitShader);
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
        return m;
    }
}
