// Summit's signs, banners, tape and camp notes (CourseDressing.Sign): plain painted boards lit like
// everything else (the sun or moon with its shadows, the sky light, lamps nearby, fog), plus what a real
// retroreflective sign does: it sends the light that reaches it straight back where it came from. The only
// light near the camera is the rider's headlamp, so the return is the headlamp's own, given by DayNight in
// globals: strong only inside its cone, falling off with distance and as the sign turns away from it, and
// nothing at all when it is off. No glow of their own: in the dark, unlit, they are dark.
Shader "Summit/SignLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Picture", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Retro ("Retroreflection", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Retro;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/Farview.hlsl"   // (it comes in through the world's far band, pixel by pixel)
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            // the headlamp (DayNight.Headlamp): position, aim, colour times strength, and its cone and reach
            float4 _LampPos;      // xyz; w 1 when on
            float4 _LampDir;      // xyz, the way it points
            half4 _LampColor;     // rgb: colour times intensity
            float4 _LampCone;     // x cos of the outer half-angle, y cos of the inner, z how far the return reaches

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; half fog : TEXCOORD3;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                FarviewClip(i.positionWS, i.positionCS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 n = normalize(i.normalWS);
                // the scene's light, as on any matte surface: sky light, the sun or moon, lamps
                half3 light = SampleSH(n);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                light += sun.color * (sun.distanceAttenuation * sun.shadowAttenuation * saturate(dot(n, sun.direction)));
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; k++)
                {
                    Light l = GetAdditionalLight(k, i.positionWS);
                    light += l.color * (l.distanceAttenuation * l.shadowAttenuation * saturate(dot(n, l.direction)));
                }
                #endif
                half3 c = albedo * light;
                // the headlamp's light sent back to the camera: inside its cone, by distance and by how square on
                // the sign is to it (a sign seen edge-on returns little), and only toward a camera near the lamp
                if (_LampPos.w > 0.5)
                {
                    float3 toLamp = _LampPos.xyz - i.positionWS;
                    float d = length(toLamp); toLamp /= max(d, 1e-3);
                    half cone = smoothstep(_LampCone.x, _LampCone.y, dot(-toLamp, _LampDir.xyz));
                    half reach = saturate(1.0 - d / _LampCone.z); reach *= reach;
                    half facing = saturate(dot(n, toLamp));
                    float3 toCam = normalize(GetCameraPositionWS() - i.positionWS);
                    half beside = smoothstep(0.85, 0.98, dot(toLamp, toCam));   // the camera is just behind the lamp
                    c += albedo * _LampColor.rgb * (cone * reach * sqrt(facing) * beside * _Retro);
                }
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        // shadows and depth as any plain surface
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attributes i) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(i.positionOS.xyz), n = TransformObjectToWorldNormal(i.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
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
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
