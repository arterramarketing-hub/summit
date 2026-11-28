// The original (low-poly) trees, lit as URP Lit lights them, moved by the one wind like the realistic ones
// (Shaders/TreeWind.hlsl); and the camp's birches and its firs' shadow stand-ins (Camp.SwayFromFoot, FirShadow). The
// forest swaps this in for the models' own material (Forest.Add, TreeWind.Swaying), keeping their colour, texture,
// finish and culling; _WindFlex is the kind's give (TreeWind.Flex*). _Sway 0 holds a felled piece still; _Snow below 1 drops the snow
// (the models' pale snow parts) from a snapped or smashed tree's top. In Resources so it ships (loaded by name).
Shader "Summit/TreeSway"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Sway ("Moves in the wind (0: a felled piece lying still)", Float) = 1
        _WindFlex ("Give in the wind (1 a fir; more: looser)", Float) = 1
        _Snow ("Snow left on it", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (kept from the material copied)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../Shaders/TreeWind.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Smoothness;
            half _Metallic;
            half _Sway;
            half _WindFlex;
            half _Snow;
        CBUFFER_END

        float3 SwayWS(float3 positionOS) { return TreeSway(TransformObjectToWorld(positionOS), TransformObjectToWorld(float3(0, 0, 0)), 1.0, _Sway * _WindFlex); }

        // the snow parts of a model (pale and colourless) go when the snow has fallen off
        void ClipSnow(half3 c)
        {
            if (_Snow > 0.999h) return;
            half sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
            half snowy = step(0.62h, dot(c, half3(0.3h, 0.59h, 0.11h))) * step(sat, 0.12h);
            clip(0.5h - snowy * (1.0h - _Snow));
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = SwayWS(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                ClipSnow(albedo);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = _Metallic; s.smoothness = _Smoothness; s.occlusion = 1; s.alpha = 1;
                s.normalTS = half3(0, 0, 1);
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = normalize(i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fog);
                c.a = 1;
                return c;
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                float3 pos = SwayWS(i.positionOS.xyz);
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
                ClipSnow(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb);
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                o.positionCS = TransformWorldToHClip(SwayWS(i.positionOS.xyz));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                ClipSnow(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
