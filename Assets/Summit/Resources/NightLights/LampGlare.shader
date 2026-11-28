// A floodlight's lens seen from the hill (NightLights.Glare): a quad at the lens turned to face the camera here, drawn
// additively. A hot white core with a soft bloom round it and faint cross spikes (a phone camera's), small and dim
// from behind or the side, blazing when the lamp looks at you; swollen by falling snow and fog (_NLHaze).
//   POSITION: the lens      uv0: corner (-1..1), size (m)      uv1: the aim (xyz)
Shader "Summit/NightLights/LampGlare"
{
    Properties
    {
        _Strength ("Strength", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glare"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Strength;
            CBUFFER_END
            float _NLOn, _NLHaze, _NLGain;
            float4 _NLColor;

            struct Attributes { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 corner : TEXCOORD0; half3 k : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 c = TransformObjectToWorld(i.positionOS.xyz);
                float3 toEye = GetCameraPositionWS() - c;
                float dist = length(toEye);
                float face = saturate(dot(i.uv1.xyz, toEye / dist));
                float glare = 0.1 + 0.35 * face + 1.4 * pow(face, 5.0);
                float haze = saturate(_NLHaze);
                // never smaller on screen than a few pixels (a far lamp is still a point of light)
                float pixel = dist * 2.0 / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                float size = max(i.uv0.z * (0.5 + 0.5 * pow(face, 2.0)) * (1.0 + 0.4 * haze), 7.0 * pixel);
                float3 vs = TransformWorldToView(c) + float3(i.uv0.xy * size, 0);
                // (pulled a little toward the eye so its own housing doesn't cut it)
                vs.z += min(0.8, dist * 0.5);
                o.positionCS = TransformWViewToHClip(vs);
                o.corner = i.uv0.xy;
                o.k = half3(glare * _NLOn * _Strength * (1.0 + (max(_NLGain, 1.0) - 1.0) * 1.35), ComputeFogFactor(o.positionCS.z), haze);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.corner;
                float r2 = dot(c, c);
                half core = exp(-r2 * 70.0) * 6.0;
                half bloom = exp(-r2 * 7.0) * (0.35 + 0.4 * i.k.z);
                half spikes = (exp(-abs(c.y) * 70.0) + exp(-abs(c.x) * 70.0)) * exp(-r2 * 2.5) * 0.25;
                half3 col = _NLColor.rgb * (core + bloom + spikes) * i.k.x;
                return half4(MixFogColor(col, half3(0, 0, 0), i.k.y * 0.6), 1);
            }
            ENDHLSL
        }
    }
}
