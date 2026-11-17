// The lens's own faults, drawn after URP's post-processing (see LensFX.cs). Six texture reads at most.
//
//  - Colour fringing (lateral chromatic aberration): red is read a little further out from the centre and
//    blue a little further in, by an amount that grows with the square of the distance from the centre.
//    The distance is measured in screen shape (1 on every edge, 1.5 in the corners), so a tall phone gets
//    fringes down both sides, where you look while riding, not only in the corners.
//  - Edge softness: each colour is read twice, spread along the same direction, so the smear and the
//    fringe share the six reads.
//  - Spherical: both run outward from the centre. Anamorphic: both run sideways only, and the frame is
//    warped like a desqueezed anamorphic picture: the middle stretched wide, the sides squeezed in (the
//    "mumps"), verticals near the sides bowing, all pinned at the frame edge so nothing is read from
//    outside the picture. A wide oval vignette and thin bars give the widescreen character.
//  - The middle of the frame takes an early exit with a single read.
Shader "Hidden/Summit/LensEdge"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "LensEdge"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _LensEdge;    // x: fringe, y: smear, z: bar height (0 = none), w: 1 = anamorphic
            float4 _LensAna;     // x: squeeze of the sides, y: bow of the verticals, z: oval vignette
            float _LensAspect;   // width / height: keeps spherical fringes the same length in x and y on screen

            half3 Read(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb; }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = input.texcoord * 2.0 - 1.0;   // -1..1 across the screen
                bool ana = _LensEdge.w > 0.5;

                float2 dir;   // the direction the faults run, in uv
                float r2;     // 0 in the middle, 1 at the side edges
                if (ana)
                {
                    // desqueeze: magnified middle, squeezed sides, bowed verticals; x stays inside -1..1
                    p.x *= 1.0 - _LensAna.x * (1.0 - p.x * p.x) - _LensAna.y * (1.0 - p.y * p.y);
                    r2 = p.x * p.x;
                    dir = float2(p.x * 0.5, 0.0);
                }
                else
                {
                    r2 = dot(p, p) * 0.5 + 0.5 * max(p.x * p.x, p.y * p.y);
                    dir = float2(p.x, p.y * _LensAspect) * 0.5;
                }
                float2 uv = p * 0.5 + 0.5;
                float2 o = dir * r2;

                half3 col;
                UNITY_BRANCH
                if (r2 < 0.03)
                {
                    col = Read(uv);
                }
                else
                {
                    float ca = _LensEdge.x, s = _LensEdge.y * 0.5;
                    col.r = 0.5 * (Read(uv + o * (ca + s)).r + Read(uv + o * (ca - s)).r);
                    col.g = 0.5 * (Read(uv + o * s).g + Read(uv - o * s).g);
                    col.b = 0.5 * (Read(uv - o * (ca - s)).b + Read(uv - o * (ca + s)).b);
                }

                if (ana)
                {
                    // a wide oval: the top and bottom fall off first, the sides stay open
                    float e = length(float2(p.x * 0.55, p.y));
                    col *= 1.0 - _LensAna.z * smoothstep(0.62, 1.2, e);
                    // thin bars, with an anti-aliased edge
                    float y = 1.0 - abs(p.y), bar = _LensEdge.z * 2.0;
                    col *= smoothstep(bar - 0.003, bar, y);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
