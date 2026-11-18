// Summit's rock: every rock face on the mountain wears this (RockFace.Material) - the terrain's cliffs, gorge walls
// and steep banks, the ridges' outcrops, the spires. Dark fractured granite (Tools/MakeRock.swift), mapped in world
// space from three sides at once, so a sheer wall is never stretched and nothing depends on a mesh's UVs.
//   - two scales of the same rock: the near one for its grain and cracks, a far one (eight times larger) tinting
//     it in broad patches, so it never tiles, near or a kilometre off
//   - strata: horizontal bands of slightly different rock, each with a dark bedding seam, wandering a little
//   - snow caught on whatever faces up enough to hold it (ledges, the tops of blocks), and in broken lines along
//     the strata's seams, which is what still reads from across a canyon when the face is in shade
// Lit like everything else (the sun or moon with its shadows, the sky's light, the headlamp and other lamps, fog),
// so it works at night too. Cheap: six samples of one small texture and a few lines of arithmetic.
Shader "Summit/Rock"
{
    Properties
    {
        [MainTexture] _BaseMap ("Rock", 2D) = "grey" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
        _Scales ("Metres per repeat: near, far", Vector) = (5, 41, 0, 0)
        _Strata ("Strata: band height (m), strength", Vector) = (6.5, 1, 0, 0)
        _Snow ("Snow on faces up from, to (normal.y)", Vector) = (0.48, 0.66, 0, 0)
        _SnowColor ("Snow", Color) = (0.86, 0.9, 0.96, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Smoothness;
            float4 _Scales;
            float4 _Strata;
            float4 _Snow;
            half4 _SnowColor;
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
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half fog : TEXCOORD2;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // the rock seen from three sides, blended by which way the surface faces (texture v is always up the face)
            half3 Tri(float3 p, float3 w, float metres)
            {
                float k = 1.0 / metres;
                half3 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.zy * k).rgb;
                half3 b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xz * k).rgb;
                half3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xy * k).rgb;
                return a * w.x + b * w.y + c * w.z;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                float3 n = normalize(i.normalWS);
                float3 w = pow(abs(n), 4.0); w /= (w.x + w.y + w.z);
                half3 nearRock = Tri(p, w, _Scales.x);
                half3 farRock = Tri(p + 17.3, w, _Scales.y);
                // (the texture is sRGB, so its colours arrive linear: for the masks and tints below, its tones as
                // painted, about 0.36 on average)
                half3 nearT = sqrt(nearRock), farT = sqrt(farRock);
                // the far rock tints the near one in broad patches (its mean is about 0.38, so this keeps the brightness),
                // and across a whole face, slow swings between warm and cold rock, so even a kilometre off a cliff is
                // never one flat grey
                half3 albedo = nearRock * (0.3 + 1.95 * farT);
                half swing = sin(p.x * 0.0041 + p.z * 0.0029 + 1.7) * sin(p.y * 0.006 - p.z * 0.0023) * 0.5 + 0.5;
                albedo *= lerp(half3(1.2, 1.04, 0.84), half3(0.8, 0.94, 1.16), swing) * lerp(0.75, 1.2, farT.g * 2.0 - 0.25);
                float dist = length(GetCameraPositionWS() - p);

                // strata: level bands of rock, wandering a little, each its own shade and a touch browner or bluer,
                // with a dark bedding seam along its foot
                float warp = (farT.r - 0.38) * 3.0 + sin(p.x * 0.011 + p.z * 0.007) * 2.0;
                float s = (p.y + warp) / _Strata.x;
                float id = floor(s), f = frac(s);
                half h = frac(sin(id * 12.9898) * 43758.5453);
                half3 band = lerp(half3(1.08, 1.0, 0.9), half3(0.9, 0.98, 1.1), frac(h * 7.31)) * lerp(0.8, 1.3, h);
                // (the seam widens with distance, so the bands still read from across a canyon, not just up close)
                half sw = 0.06 + saturate(dist / 1500.0) * 0.14;
                half seam = smoothstep(0.0, sw, f) * smoothstep(1.0, 1.0 - sw * 1.4, f);
                albedo *= lerp(1.0, band * lerp(0.5, 1.0, seam), _Strata.y);
                // far off, the fine grain averages away and the haze takes half of what is left: push what remains
                // apart (bands, patches, warm and cold), so a distant face still reads as rock and never as a grey slab
                half far01 = saturate((dist - 60.0) / 500.0);
                albedo = max(0.0, 0.11 + (albedo - 0.11) * (1.0 + 1.3 * far01));
                albedo *= _BaseColor.rgb;

                // snow caught on whatever faces up enough to hold it, with a ragged edge
                half up = n.y + (farT.b - 0.38) * 0.6 + (nearT.g - 0.36) * 0.5;
                half snow = smoothstep(_Snow.x, _Snow.y, up);
                // and along the bedding seams, where the strata leave a little shelf: broken lines of snow across
                // even a sheer face (not under an overhang). In shade and haze, when the rock's own colours have all
                // but gone, these are what still say "cliff" from across a canyon
                half shelf = (1.0 - seam) * smoothstep(0.48, 0.66, farT.g * 1.7 + nearT.r * 0.7 - 0.3) * saturate(n.y * 2.0 + 0.9);
                snow = max(snow, shelf * 0.85);
                albedo = lerp(albedo, _SnowColor.rgb, snow);

                // the scene's light, as on any rough surface: sky light, the sun or moon, lamps (the headlamp at night)
                half3 light = SampleSH(n);
                Light sun = GetMainLight(TransformWorldToShadowCoord(p));
                half sunLit = sun.distanceAttenuation * sun.shadowAttenuation;
                light += sun.color * (sunLit * saturate(dot(n, sun.direction)));
                float3 toCam = normalize(GetCameraPositionWS() - p);
                half gloss = lerp(_Smoothness, 0.45, snow);
                half3 spec = sun.color * sunLit * pow(saturate(dot(n, normalize(sun.direction + toCam))), lerp(8.0, 64.0, gloss)) * gloss * 0.25;
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; k++)
                {
                    Light l = GetAdditionalLight(k, p);
                    light += l.color * (l.distanceAttenuation * l.shadowAttenuation * saturate(dot(n, l.direction)));
                }
                #endif
                half3 c = albedo * light + spec;
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
