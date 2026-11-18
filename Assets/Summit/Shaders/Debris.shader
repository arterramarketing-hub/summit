// The avalanche's debris (AvalancheView): churned, broken snow, metres deep. One deforming mesh laid over the
// slope; this makes it read as tonnes of lumps and clods rather than more smooth slope snow:
//  - a texture of packed clods (Tools/MakeAvalanche.swift): their normals in RG, the cracks between them in B,
//    two scales of it over each other, scrolled with the surface as it runs on (the mesh's uv does that)
//  - the mesh's colour: r is how open each spot is to the sky (dark down in the troughs between the big lumps)
//  - lit by the sun with its shadows and by the sky's own light, so it follows the time of day
// Cooler and greyer in the shade than the slope's fresh snow, and the creases go a deep blue.
Shader "Summit/Debris"
{
    Properties
    {
        _MainTex ("Clods (normal xy, cracks)", 2D) = "bump" {}
        _BaseColor ("Lit colour", Color) = (0.95, 0.96, 0.99, 1)
        _Crease ("Crease colour", Color) = (0.42, 0.5, 0.66, 1)
        _Bump ("Lumpiness", Float) = 1.1
        _Tile ("Clods per metre", Float) = 0.42
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _BaseColor, _Crease;
            half _Bump, _Tile;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; half3 n : TEXCOORD1; half3 t : TEXCOORD2; half3 b : TEXCOORD3;
                float2 uv : TEXCOORD4; half open : TEXCOORD5; half fog : TEXCOORD6;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.n = TransformObjectToWorldNormal(i.normalOS);
                o.t = TransformObjectToWorldDir(i.tangentOS.xyz);
                o.b = cross(o.n, o.t) * i.tangentOS.w;
                o.uv = i.uv;
                o.open = i.color.r;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                // big clods and small ones over them
                half4 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _Tile);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _Tile * 2.7 + 0.37);
                half2 nxy = ((a.rg * 2 - 1) + (c.rg * 2 - 1) * 0.6) * _Bump;
                half3 nt = normalize(half3(nxy, 1));
                half3 n = normalize(i.t * nt.x + i.b * nt.y + normalize(i.n) * nt.z);
                half cracks = a.b * lerp(0.75, 1, c.b);
                half open = saturate(i.open) * cracks;
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                half ndl = saturate(dot(n, sun.direction) * 0.85 + 0.15);
                half3 amb = max(SampleSH(n), 0);
                half3 albedo = lerp(_Crease.rgb, _BaseColor.rgb, open);
                half3 col = albedo * (amb * (0.35 + 0.65 * open) + sun.color * ndl * sun.shadowAttenuation * (0.55 + 0.45 * open));
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attributes i) : SV_POSITION
            {
                float3 pos = TransformObjectToWorld(i.positionOS.xyz);
                float3 nrm = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 toLight = normalize(_LightPosition - pos);
            #else
                float3 toLight = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(pos, nrm, toLight)));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert(Attributes i) : SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
