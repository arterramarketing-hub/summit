using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The game's plain materials, made in code: URP's Lit shader looked up once, and one recipe for a colour with
    /// its sheen (and, where asked, metal, a texture and a glow) that every builder's own little helper used to
    /// write out for itself.
    /// </summary>
    public static class Mats
    {
        static Shader lit;

        /// <summary>
        /// URP's Lit, found once. (No fallback: the built-in pipeline's "Standard" has no pass URP draws, and a
        /// player build leaves it out. Every scene's own materials keep it in the build.)
        /// </summary>
        public static Shader LitShader => lit != null ? lit : (lit = Shader.Find("Universal Render Pipeline/Lit"));

        /// <summary>
        /// A new material on shader (Lit when none): colour and smoothness; metallic only when given (otherwise the
        /// shader's own); a base texture, tiled when tile is given; emission turned on when emit is not black; GPU
        /// instancing when instanced. Each call makes a new material: the caller owns it.
        /// </summary>
        public static Material Lit(Color c, float smooth, Shader shader = null, float? metallic = null,
                                   Texture tex = null, Vector2 tile = default, Color emit = default, bool instanced = false)
        {
            var m = new Material(shader != null ? shader : LitShader);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            if (metallic.HasValue) m.SetFloat("_Metallic", metallic.Value);
            if (tex != null) { m.SetTexture("_BaseMap", tex); if (tile != default) m.SetTextureScale("_BaseMap", tile); }
            if (emit.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit); }
            if (instanced) m.enableInstancing = true;
            return m;
        }
    }
}
