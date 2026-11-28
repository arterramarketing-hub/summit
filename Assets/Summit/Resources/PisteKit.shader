// The resort's furniture down the run (PisteKit.cs, TrailSigns.cs): bamboo piste poles and their discs, rope lines
// and their crossed "closed" poles, snow guns and hydrants, the patrol hut and its toboggan, the trail signs.
//
// Painted things, lit as URP Lit lights an opaque dielectric (the sun and its shadows, the sky's ambient, the real
// lights near the rider, the rider's headlamp, the fog), plus the floodlights (NightLights): the lamps nearest the
// camera (_NLPos/_NLDir/_NLCone, up to eight) are worked out per vertex as the snow's pools work them out, so a pole
// standing in a pool is lit as the snow round it is; a lamp that is a real light just now (_NLReal) leaves its share
// to URP, which lights this too. With the lamps off (_NLCount 0) it is plain lit paint.
//
// The colour is the vertex colour (the run's furniture is drawn in a few big meshes, one draw per stretch) times
// _BaseMap times _BaseColor; _VertexColor 0 ignores the vertex colour (Unity's primitives, the signs' pieces).
// Vertex alpha is the paint's smoothness (times _Smoothness).
//
// Let in through the world's far band like the rest of the course (Shaders/Farview.hlsl).
Shader "Summit/PisteKit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _VertexColor ("Use vertex colour", Float) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 1
        _NLGainK ("Floodlight gain", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _VertexColor, _Smoothness, _NLGainK;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/Farview.hlsl"

            int _NLCount;
            float4 _NLPos[8], _NLDir[8], _NLCone[8];
            float4 _NLColor, _NLReal;
            float _NLGain;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half4 lamps : TEXCOORD4;     // rgb: the floodlights' light here; a: fog
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half3 vertexLight : TEXCOORD5;
                #endif
            };

            // the floodlights' light falling on a surface at p facing n (URP's spot falloff, as the pools work it out)
            half3 Floodlit(float3 p, float3 n)
            {
                float lit = 0.0;
                for (int k = 0; k < _NLCount; k++)
                {
                    float3 d = p - _NLPos[k].xyz;
                    float d2 = max(dot(d, d), 0.25);
                    float3 ray = d * rsqrt(d2);
                    float f = d2 * _NLDir[k].w;
                    float sm = saturate(1.0 - f * f); sm *= sm;
                    float ang = saturate(dot(ray, _NLDir[k].xyz) * _NLCone[k].x + _NLCone[k].y); ang *= ang;
                    float id = _NLCone[k].z;
                    float real = (abs(id - _NLReal.x) < 0.5 ? _NLReal.y : 0.0) + (abs(id - _NLReal.z) < 0.5 ? _NLReal.w : 0.0);
                    lit += _NLPos[k].w * (1.0 - saturate(real)) * sm * ang * saturate(dot(n, -ray)) / d2;
                }
                return (half3)(_NLColor.rgb * lit * max(_NLGain, 1.0) * _NLGainK);
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = (half3)TransformObjectToWorldNormal(i.normalOS);
                o.color = lerp(half4(1, 1, 1, 1), i.color, _VertexColor) * _BaseColor;
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.lamps = half4(Floodlit(o.positionWS, normalize((float3)o.normalWS)), (half)ComputeFogFactor(o.positionCS.z));
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                o.vertexLight = VertexLighting(o.positionWS, o.normalWS);
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                FarviewClipWindow(FarviewAnd(FarviewAnd(half2(0.0h, FarviewShown(i.positionWS)), FarviewSwapWindow()),
                                             FarviewNearFarWindow(TransformObjectToWorld(float3(0, 0, 0)))), i.positionCS);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * i.color.rgb;

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = NormalizeNormalPerPixel(i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.lamps.a;
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                d.vertexLighting = i.vertexLight;
                #endif
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = 0.0h;
                s.specular = half3(0, 0, 0);
                s.smoothness = saturate(i.color.a * _Smoothness);
                s.occlusion = 1.0h;
                s.alpha = 1.0h;
                s.normalTS = half3(0, 0, 1);

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb += albedo * i.lamps.rgb;
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return half4(c.rgb, 1.0h);
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
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(A i) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 l = normalize(_LightPosition - p);
                #else
                float3 l = _LightDirection;
                #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, n, l));
                #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 frag() : SV_TARGET { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "../Shaders/Farview.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            V vert(A i)
            {
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half frag(V i) : SV_TARGET
            {
                FarviewClipWindow(FarviewAnd(FarviewAnd(half2(0.0h, FarviewShown(i.positionWS)), FarviewSwapWindow()), FarviewNearFarWindow(TransformObjectToWorld(float3(0, 0, 0)))), i.positionCS);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
