// The lens's own faults, drawn after URP's post-processing (see LensFX.cs). Six texture reads at most
// (eleven where the first-person view's edges streak).
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
//  - Halation (film): a warm red-orange glow bleeding around bright highlights, from light scattering back
//    through the film base. Passes 1-3 build it at a quarter of the screen size (bright parts kept, then a
//    blur across and down); pass 0 reads it once more and screens it over the picture, so it shows most
//    where a highlight meets something dark: snow against trees, lamps at night, the rider against the snow.
//  - Edge streaks (first person, at speed): the edges of the frame are smeared along lines running out from
//    the point you are heading for, longer the further from it and the nearer the edge; the middle stays
//    sharp, and so does the bottom middle, where the rider's own legs and board are (they travel with the
//    lens). Five more reads back along the streak, each pixel starting at a different point along it, so the
//    steps between reads show as fine grain instead of ghost copies. Skipped wherever the streak is shorter
//    than a pixel, and altogether when it is off.
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
            float4 _Halo;        // rgb: tint times strength (0 = off)
            float4 _LensBlur;    // x: streak length at the frame edge, as a fraction of the screen (0 = off);
                                 // yz: the point the streaks run out from (uv); w: 1 if v runs up the screen, -1 if down
            TEXTURE2D(_HaloTex);

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
                // pinned to nothing at the very border: the offset reads would otherwise run off the picture and smear
                // the clamped edge pixel into a coloured strip along the screen's edge
                o *= saturate(min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y)) * 40.0);

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

                // edge streaks
                UNITY_BRANCH
                if (_LensBlur.x > 0.0)
                {
                    float up = p.y * _LensBlur.w;
                    float w = smoothstep(0.25, 1.0, r2);
                    w *= 1.0 - 0.8 * smoothstep(0.3, 0.8, -up) * (1.0 - smoothstep(0.45, 0.95, abs(p.x)));
                    float2 streak = (uv - _LensBlur.yz) * (2.0 * _LensBlur.x * w);
                    UNITY_BRANCH
                    if (dot(streak, streak) > 2.5e-7)
                    {
                        float start = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
                        half3 sum = col;
                        for (int i = 0; i < 5; i++) sum += Read(uv - streak * ((i + start) * 0.2));
                        col = sum / 6.0;
                    }
                }

                // halation, screened on: it lifts the dark side of a bright edge and barely touches the bright side
                UNITY_BRANCH
                if (_Halo.r > 0.0)
                {
                    half3 h = SAMPLE_TEXTURE2D_LOD(_HaloTex, sampler_LinearClamp, uv, 0).rgb;
                    half glow = dot(h, half3(0.3, 0.5, 0.2));
                    col = 1.0 - (1.0 - col) * (1.0 - saturate(glow * _Halo.rgb));
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

        // 1: halation source, at a quarter size: four reads (a 4x4 block), only what is bright enough kept
        Pass
        {
            Name "HaloPrefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _HaloSrc;   // xy: one texel of the full-size picture; z: brightness where it starts, w: where it is full
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord, t = _HaloSrc.xy;
                half3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x, -t.y), 0).rgb
                        + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2( t.x, -t.y), 0).rgb
                        + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x,  t.y), 0).rgb
                        + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2( t.x,  t.y), 0).rgb;
                c *= 0.25;
                // the picture here is in linear light; the thresholds are as brightness looks on screen
                half l = sqrt(max(c.r, max(c.g, c.b)));
                return half4(c * smoothstep(_HaloSrc.z, _HaloSrc.w, l), 1);
            }
            ENDHLSL
        }

        // 2 and 3: a nine-wide gaussian in five reads (bilinear pairs), across then down
        Pass
        {
            Name "HaloBlurX"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _HaloBlur;   // xy: one texel of the quarter-size picture times the spread
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord, d = float2(_HaloBlur.x, 0);
                half3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb * 0.2270
                        + (SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + d * 1.3846, 0).rgb
                         + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - d * 1.3846, 0).rgb) * 0.3162
                        + (SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + d * 3.2308, 0).rgb
                         + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - d * 3.2308, 0).rgb) * 0.0703;
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "HaloBlurY"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _HaloBlur;
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord, d = float2(0, _HaloBlur.y);
                half3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb * 0.2270
                        + (SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + d * 1.3846, 0).rgb
                         + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - d * 1.3846, 0).rgb) * 0.3162
                        + (SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + d * 3.2308, 0).rgb
                         + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - d * 3.2308, 0).rgb) * 0.0703;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
