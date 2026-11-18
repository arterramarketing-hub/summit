// The realistic firs up close: branch cards cut out of the atlas (alpha test, both sides), lit by Fir.hlsl's
// sums so they match their far pictures (FirImpostor). With _FadeOn, a forest tree dissolves into its picture
// over the hand-over band (Forest.cs); the camp's and the start tape's own trees leave it off.
// _Bake is for RealFir.BakeImpostors only: 1 writes the plain colour, 2 the normal, for the far pictures.
Shader "Summit/FirCards"
{
    Properties
    {
        [MainTexture] _BaseMap ("Atlas", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _FadeOn ("Hands over to the far picture", Float) = 1
        _Bake ("Bake output (0 lit, 1 colour, 2 normal)", Float) = 0
        _NeedleTint ("Needles graded toward the dark blue-green of the Cascades' firs", Color) = (0.62, 0.78, 0.86, 1)
        _CutY ("Nothing drawn below this height (model space): a felled tree's buried part", Float) = -10000
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _FadeOn;
            half _Bake;
            float _CutY;
            half4 _NeedleTint;
        CBUFFER_END

        // the atlas's greens graded darker and bluer; the snow (pale, unsaturated) is left white
        half3 Needles(half3 c)
        {
            half sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
            return lerp(c, c * _NeedleTint.rgb, saturate(sat * 6.0h));
        }
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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 fadeFogLift : TEXCOORD3;   // x hand-over, y fog, z night lift share, w model-space height
                half3 vertexLight : TEXCOORD4;
                float objY : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.fadeFogLift = half4(FirFade(origin) * _FadeOn, ComputeFogFactor(o.positionCS.z), FirLiftShare(origin), 0);
                o.objY = i.positionOS.y;
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                o.vertexLight = VertexLighting(o.positionWS, o.normalWS);
            #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(c.a - _Cutoff);
                clip(i.objY - _CutY);
                c.rgb = Needles(c.rgb);
                // a branch card lying flat, seen from underneath: its snow is on top, so here it is shaded needles
                float3 ng = normalize(cross(ddx(i.positionWS), ddy(i.positionWS)));
                if (abs(ng.y) > 0.5 && dot(ng * sign(ng.y), GetCameraPositionWS() - i.positionWS) < 0) c.rgb *= 0.28h;
                float3 n = normalize(i.normalWS);
                if (_Bake > 0.5) return _Bake < 1.5 ? half4(c.rgb, 1) : half4(n * 0.5 + 0.5, 1);
                clip(FirDither(i.positionCS) - i.fadeFogLift.x);
                half3 col = FirLight(c.rgb, n, i.positionWS, i.positionCS, i.vertexLight, 1, i.fadeFogLift.z);
                return half4(MixFog(col, i.fadeFogLift.y), 1);
            }
            ENDHLSL
        }

        Pass
        {
            // the whole tree casts its shadow through the hand-over (shadows have faded out by its far end)
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float objY : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                o.objY = i.positionOS.y;
                float3 pos = TransformObjectToWorld(i.positionOS.xyz);
                float3 nrm = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 toLight = normalize(_LightPosition - pos);
            #else
                float3 toLight = _LightDirection;
            #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(pos, nrm, toLight)));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                clip(i.objY - _CutY);
                return 0;
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
                o.fade = FirFade(TransformObjectToWorld(float3(0, 0, 0))) * _FadeOn;
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                clip(FirDither(i.positionCS) - i.fade);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
