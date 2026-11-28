// The slope's rocks: photo-scanned boulders (Resources/ScanRocks, built by Tools/MakeScanRocks.py), all sharing one
// colour atlas and one normal map, drawn in instanced batches by Forest.cs.
//   - the scan's own colour (graded to the mountain's grey granite by the tool) and its fine relief from the
//     normal map, which is where the 900-triangle rocks get their cracks and grain back
//   - snow caught on whatever faces up enough to hold it, with a ragged edge - the same rule, thresholds and snow
//     colour as the cliffs (RockLit.shader), worked out in the world, so it stays on top however a rock is turned
//   - snow banked against the foot: Forest.cs sinks every rock about a quarter into the slope, and a drift fading
//     up its sloping sides softens the line where rock meets snow (never on upright faces or under overhangs)
// Lit like the cliffs and the firs (the sun or moon with its shadows, the sky's light, the headlamp and other lamps,
// fog), so it works at night too. Two texture samples and a few lines of arithmetic.
// Each rock comes in through the world's far band (Shaders/Farview.hlsl), as a whole, by where it stands; far off it
// is drawn as a cheap stand-in of the same shape (Forest.cs), the two handing over across _NearFar by distance with
// the same screen pattern the opposite way round.
Shader "Summit/ScanRock"
{
    Properties
    {
        [MainTexture] _BaseMap ("Rock atlas", 2D) = "grey" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal atlas", 2D) = "bump" {}
        _BumpScale ("Relief", Range(0, 2)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
        _Snow ("Snow on faces up from, to (normal.y)", Vector) = (0.48, 0.66, 0, 0)
        _SnowColor ("Snow", Color) = (0.86, 0.9, 0.96, 1)
        _Drift ("Snow banked at the foot: all below, none above (m up the model)", Vector) = (0.34, 0.62, 0, 0)
        _NearFar ("Hand-over by distance: from, to (m), +1 the near version, -1 the far one, 0 none", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Smoothness;
            float4 _Snow;
            half4 _SnowColor;
            float4 _Drift;
            float4 _NearFar;
        CBUFFER_END
        ENDHLSL
        HLSLINCLUDE
        #include "../Shaders/Farview.hlsl"
        // which of the dither's values the rock standing at `foot` is drawn with: the band, and its hand-over between
        // near and far versions (Farview.hlsl: the two exactly complementary)
        half2 RockWindow(float3 foot)
        {
            half2 w = half2(0.0h, FarviewShown(foot));
            if (_NearFar.z != 0.0 && _FarviewCentre.w > 0.5)
            {
                float d = length(foot.xz - _FarviewCentre.xz);
                half f = (half)saturate((d - _NearFar.x) / max(_NearFar.y - _NearFar.x, 1e-3));
                w = FarviewAnd(w, _NearFar.z > 0.0 ? half2(f, 1.0h) : half2(0.0h, f));
            }
            return w;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
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
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;      // w: which way the bitangent runs
                half4 fogUp : TEXCOORD4;          // x fog, y metres up the model from its foot, zw the dither window drawn
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
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.tangentWS = half4(TransformObjectToWorldDir(i.tangentOS.xyz), i.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                // how far up the rock this is, in the model's own metres (the models are all 1.3 m tall and stand on
                // their pivot), whatever size and way round Forest.cs has drawn it: measured in the world, straight up
                // from the pivot, and divided by the size it was drawn at
                float3 foot = TransformObjectToWorld(float3(0, 0, 0));
                float drawn = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                half2 win = RockWindow(foot);
                o.fogUp = half4(ComputeFogFactor(o.positionCS.z), (o.positionWS.y - foot.y) / max(drawn, 1e-4), win);
                if (!FarviewAny(win)) o.positionCS = FarviewGone();
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                o.vertexLight = VertexLighting(o.positionWS, o.normalWS);
            #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                FarviewClipWindow(i.fogUp.zw, i.positionCS);
                float3 p = i.positionWS;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                // (the atlas is sRGB, so its colours arrive linear: for the snow's ragged edge, its tone as painted,
                // about 0.39 on average)
                half tone = sqrt(albedo.g) - 0.39;
                albedo *= _BaseColor.rgb;

                // the rock's shape (the mesh) and its fine relief on top (the normal map)
                float3 ng = normalize(i.normalWS);
                half3 nt = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 tg = normalize(i.tangentWS.xyz - ng * dot(i.tangentWS.xyz, ng));
                float3 n = normalize(nt.x * tg + nt.y * (cross(ng, tg) * i.tangentWS.w) + nt.z * ng);

                // snow caught on whatever faces up enough to hold it, with a ragged edge: on the rock's broad shape
                // mostly, a little on its relief, so ledges and the tops of blocks hold it and cracks stay dark
                half up = lerp(ng.y, n.y, 0.5) + tone * 0.7;
                half snow = smoothstep(_Snow.x, _Snow.y, up);
                // and banked against the foot, thinning out gradually up the rock - only on faces snow can lie against:
                // less as a face steepens, none on one that is upright or leans out over the snow (an overhang's underside)
                half bank = smoothstep(_Drift.y, _Drift.x, i.fogUp.y + tone * 0.6);
                bank *= bank * smoothstep(0.0, 0.2, ng.y);
                snow = max(snow, bank);
                albedo = lerp(albedo, _SnowColor.rgb, snow);
                n = normalize(lerp(n, ng, snow * 0.8));      // snow fills the relief in

                // the scene's light, as on any rough surface: sky light, the sun or moon, lamps (the headlamp at night)
                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 light = SampleSH(n);
                Light sun = GetMainLight(TransformWorldToShadowCoord(p));
                half sunLit = sun.distanceAttenuation * sun.shadowAttenuation;
                light += sun.color * (sunLit * saturate(dot(n, sun.direction)));
                float3 toCam = normalize(GetCameraPositionWS() - p);
                half gloss = lerp(_Smoothness, 0.45, snow);
                half3 spec = sun.color * sunLit * pow(saturate(dot(n, normalize(sun.direction + toCam))), lerp(8.0, 64.0, gloss)) * gloss * 0.25;
            #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, p, half4(1, 1, 1, 1));
                    light += l.color * (saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation);
                LIGHT_LOOP_END
            #endif
                light += i.vertexLight;
                half3 c = albedo * light + spec;
                c = MixFog(c, i.fogUp.x);
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
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
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
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half2 win : TEXCOORD0; };
            V vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o;
                o.win = RockWindow(TransformObjectToWorld(float3(0, 0, 0)));
                o.positionCS = FarviewAny(o.win) ? TransformObjectToHClip(i.positionOS.xyz) : FarviewGone();
                return o;
            }
            half frag(V i) : SV_Target { FarviewClipWindow(i.win, i.positionCS); return 0; }
            ENDHLSL
        }
    }
}
