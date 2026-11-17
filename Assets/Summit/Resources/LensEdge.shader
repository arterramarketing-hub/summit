// The lens's own faults at the edge of the frame, drawn after URP's post-processing (see LensFX.cs).
//  - Soft edges with colour fringes: a short zoom blur toward the centre that grows toward the corners.
//    Its taps are weighted red at the outer end and blue at the inner end, so the same few taps give the
//    lateral chromatic aberration of a cheap wide lens as well as the softness: one loop, six taps.
//    The middle of the frame takes the early exit and costs a single read.
//  - Anamorphic: thin black bars top and bottom, a hint of a wide frame without letterboxing a tall phone.
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

            float4 _LensEdge;    // x: blur spread, y: share of it that is colour fringe (0..1), z: bar height, w: bar darkness
            float _LensAspect;   // width / height, so the falloff is round on screen

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 d = uv - 0.5;
                // 0 in the middle, 1 in the corners; nothing at all inside the inner 40%
                float r = length(float2(d.x * _LensAspect, d.y)) / length(float2(0.5 * _LensAspect, 0.5));
                float e = saturate((r - 0.4) / 0.6);
                float spread = _LensEdge.x * e * e;

                half3 col;
                UNITY_BRANCH
                if (spread < 0.0004)
                {
                    col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                }
                else
                {
                    half3 acc = 0, wsum = 0;
                    UNITY_UNROLL
                    for (int k = 0; k < 6; k++)
                    {
                        float t = k / 5.0;
                        // red at t=0 (sampled furthest out), green in the middle, blue at t=1 (furthest in)
                        half3 spec = half3(saturate(1.0 - 2.0 * t), saturate(1.0 - abs(t - 0.5) * 2.0), saturate(2.0 * t - 1.0));
                        half3 w = lerp(half3(1, 1, 1), spec + 0.02, (half)_LensEdge.y);
                        float s = 1.0 + spread * (0.5 - t);
                        acc += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, 0.5 + d * s, 0).rgb * w;
                        wsum += w;
                    }
                    col = acc / wsum;
                }

                // thin bars top and bottom (anamorphic), with an anti-aliased edge
                float band = _LensEdge.z;
                if (band > 0.0)
                {
                    float y = min(uv.y, 1.0 - uv.y);
                    col *= 1.0 - _LensEdge.w * (1.0 - smoothstep(band - 0.0015, band, y));
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
