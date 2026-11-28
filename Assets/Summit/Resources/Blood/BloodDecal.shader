// Blood on a rock that was hit (Blood.cs): the rock's own mesh drawn again where it stands, and a stain projected
// onto it from where the body struck, so it lies on the very surface you see (the rock's hit hull is a rough box).
// The same look as on the snow (BloodCommon.hlsl): a multiply over the rock as drawn, soaking in over a second,
// runs dripping down its face for a few seconds, drying darker.
//   _BloodProj: world -> the stain's own space (x across, y up the face, in stain radii; z out of it, in metres)
//   _BloodMark: born (blood clock), soak time, amount, seed      _BloodLamp: nearest floodlight lens, share
Shader "Summit/BloodDecal"
{
    Properties
    {
        _Noise ("Noise (BloodNoiseBake)", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-389" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "BloodDecal"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor SrcColor
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -8
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BloodCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
                float4x4 _BloodProj;
                float4 _BloodMark, _BloodLamp;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 proj : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.proj = mul(_BloodProj, float4(o.positionWS, 1.0)).xyz;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 q = i.proj;
                float3 out_ = normalize(_BloodProj[2].xyz);
                float face = dot(normalize(i.normalWS), out_);
                if (abs(q.z) > 0.7 || face < 0.3 || abs(q.x) > 2.5 || q.y > 1.5 || q.y < -4.0) return half4(0.5, 0.5, 0.5, 1.0);
                float age = max(_BloodClock - _BloodMark.x, 0.0);
                float wetEdge;
                float D = BloodStain(q.xy, _BloodMark.w, age, _BloodMark.y, 0.0, 0.0, 1.0, wetEdge) * _BloodMark.z;
                // (on the rock's faces, not up on the snow lying on its top)
                D *= smoothstep(0.3, 0.6, face) * (1.0 - smoothstep(0.45, 0.7, abs(q.z))) * (1.0 - smoothstep(1.0, 1.5, q.y))
                   * (1.0 - smoothstep(0.45, 0.7, normalize(i.normalWS).y));
                return BloodShade(D, wetEdge, age, _BloodSnowAcc + 1.0, i.positionWS, i.normalWS, _BloodMark.w, _BloodLamp, i.fog);
            }
            ENDHLSL
        }
    }
}
