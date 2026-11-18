// The realistic firs far away: a picture of the tree on two crossed quads (RealFir.BakeImpostors), lit like
// the near card trees (Fir.hlsl). The picture holds the tree's plain colour and, in _NormalMap, the normals
// the near tree has there, so sun, sky and moon fall on it the same way in every time of day. Near the hand-over
// band it dissolves into the card tree (Forest.cs), by the same screen pattern the cards use the other way round.
// The mesh's tangent is the quad's own left-to-right (the picture's x); its normal is the tree's up.
Shader "Summit/FirImpostor"
{
    Properties
    {
        [MainTexture] _BaseMap ("Colour picture", 2D) = "white" {}
        _NormalMap ("Normal picture", 2D) = "bump" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _SunShade ("Sun let through (the near tree shades itself)", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _SunShade;
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Fir.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 sideWS : TEXCOORD2;
                half3 upWS : TEXCOORD3;
                half4 fadeFogLift : TEXCOORD4;
                half3 vertexLight : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.sideWS = normalize(TransformObjectToWorldDir(i.tangentOS.xyz));
                o.upWS = normalize(TransformObjectToWorldDir(i.normalOS));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.fadeFogLift = half4(FirFade(origin), ComputeFogFactor(o.positionCS.z), FirLiftShare(origin), 0);
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                o.vertexLight = VertexLighting(o.positionWS, o.upWS);
            #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(c.a - _Cutoff);
                clip(i.fadeFogLift.x - FirDither(i.positionCS));
                // the picture was taken looking along `into`, with its x along `side`; seen from behind it shows
                // mirrored, which is the tree turned half round
                half3 side = normalize(i.sideWS), up = normalize(i.upWS);
                half3 into = cross(side, up);
                half flip = dot(into, i.positionWS - GetCameraPositionWS()) < 0 ? -1.0h : 1.0h;
                half3 s = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv).xyz * 2.0h - 1.0h;
                float3 n = normalize(side * (s.x * flip) + up * s.y + into * (s.z * flip));
                half3 col = FirLight(c.rgb, n, i.positionWS, i.positionCS, i.vertexLight, _SunShade, i.fadeFogLift.z);
                return half4(MixFog(col, i.fadeFogLift.y), 1);
            }
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
            #pragma multi_compile_instancing
            #include "Fir.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fade : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fade = FirFade(TransformObjectToWorld(float3(0, 0, 0)));
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                clip(i.fade - FirDither(i.positionCS));
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
