// The terrain's snow: Summit/FarLit's snow (URP Lit with the snow material's own normal, detail and height maps,
// let in through the world's far band where a chunk needs it), with the snow underfoot drawn into it by place
// (Surfaces.cs, mountain audit E1) so the rider can read what is coming:
//   corduroy  straight grooming lines down the run, and the groomer's passes as faint lanes
//   powder    whiter and matte, the wind grooves softened
//   ice       grey-blue, polished (it catches the sun), almost no relief
//   crud      chopped: lumps in place of the grooves, mottled
//   crust     the wind grooves sharp, with a sheen
//   slush     darker, wet
// What snow is where comes from one small map that follows the rider (_SurfaceMap: powder, ice, crud and crust in
// its four channels, 2.5 m a texel, world-locked and wrapping; Surfaces.Step paints it), faded out at the edge of
// what it covers; slush is the same right across the hill, so it is worked out here from how far down the pixel is.
// Three more texture samples (the map, the snow's colour without its grooves, the grain again for the lumps) and a
// sine per pixel. Past the map, and with Surfaces off, this is the snow as it was.
//
// Variants (multi_compile, so always in the build): _PARALLAXMAP as the snow material has it, and FARVIEW_PLAIN for
// the chunks that discard nothing.
Shader "Summit/SnowSurface"
{
    Properties
    {
        _WorkflowMode("WorkflowMode", Float) = 1.0
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}
        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0
        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}
        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        HLSLINCLUDE
        #define _NORMALMAP 1
        #define _DETAIL_MULX2 1
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex FarVertex
            #pragma fragment FarFragment

            #pragma multi_compile_local _ _PARALLAXMAP
            #pragma multi_compile_local _ FARVIEW_PLAIN

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            // (the forward pass's own interpolators then carry the world position the band needs)
            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            #include "../Shaders/Farview.hlsl"

            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            float4 _SurfaceMapST;   // xy: 1 / (metres the map spans), across and down the hill
            float4 _SurfaceWin;     // xy: the middle of what the map covers (world x, z); zw: half its size (0: no map)
            float4 _SurfaceDay;     // x: where slush starts (world z); y: 1 / the metres it comes in over; z: how much of it
            float4 _SurfaceRun;     // xy: the way across the run under the rider (unit, in x and z); z: the lines' strength

            Varyings FarVertex(Attributes input) { return LitPassVertex(input); }

            // The snow underfoot, drawn into the lit surface (albedo, smoothness, the relief's strength and the
            // grooming's lines) before it is lit
            void SnowSurface(float3 positionWS, float2 uv, inout SurfaceData s)
            {
                float2 off = abs(positionWS.xz - _SurfaceWin.xy);
                half known = (half)(saturate((_SurfaceWin.z - off.x) * 0.05) * saturate((_SurfaceWin.w - off.y) * 0.025));
                half4 m = SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, positionWS.xz * _SurfaceMapST.xy) * known;   // powder, ice, crud, crust
                half slush = (half)saturate((positionWS.z - _SurfaceDay.x) * _SurfaceDay.y);
                slush = slush * slush * (3.0h - 2.0h * slush) * (half)_SurfaceDay.z * known;
                m *= 1.0h - slush;
                half cord = saturate(known - m.r - m.g - m.b - m.a - slush);

                // the relief (the wind grooves and the grain): how much of it each kind keeps. Groomed snow keeps almost
                // none: the groomer has flattened the wind's ripples, and its own lines are the only ones there (two
                // sets of lines read as moire and as motion where there is none)
                half relief = 1.0h + cord * -0.88h + m.r * -0.7h + m.g * -0.85h + m.b * -0.55h + m.a * 0.45h + slush * -0.45h;
                s.normalTS.xy *= relief;
                // (tracked snow is chopped, not combed: the snow's own grain again, four times the size and much
                // deeper, in lumps; a little of it in the crust)
                half3 chop = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uv * _DetailAlbedoMap_ST.xy * 0.27 + 0.19), 1.0h);
                s.normalTS.xy += chop.xy * (m.b * 2.6h + m.a * 0.5h);

                // corduroy: the grooming's lines, straight down the run, and the groomer's passes, each a lane a shade
                // off its neighbours. The lines fade out by how many pixels one spans: whole at 10 or more, gone by 4,
                // well before the pixel grid can alias them into crawling bands at speed (the same for the lanes'
                // edges, which are softened to a pixel or two and fade out once a lane is under 12 pixels)
                float across = dot(positionWS.xz, _SurfaceRun.xy);
                float phase = across * 39.27;   // (16 cm from line to line)
                float px = 6.2832 / max(fwidth(phase), 1e-4);   // (pixels from one line to the next)
                half fine = (half)saturate((px - 4.0) / 6.0);
                fine *= fine;
                half lines = cord * fine * (half)_SurfaceRun.z;
                s.normalTS.xy += (half2)_SurfaceRun.xy * ((half)cos(phase) * 0.5h * lines);
                float lanePhase = across * 0.2222;   // (4.5 m a pass)
                float laneW = max(fwidth(lanePhase), 1e-4);
                half lane = (half)(saturate((abs(frac(lanePhase + 0.5) - 0.5) - 0.25) / (laneW * 1.5)) * 2.0 - 1.0);
                lane *= (half)saturate((1.0 / laneW - 12.0) / 24.0);
                half groove = (half)sin(phase) * lines;

                // the colour
                // (the grooves drawn in the snow's own colour go with their relief: smooth snow is not striped)
                half3 a = s.albedo;
                half3 plain = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 7).rgb * _BaseColor.rgb;
                a = lerp(a, plain, saturate(cord * 0.9h + m.r * 0.85h + m.g * 0.9h + m.b * 0.5h + slush * 0.6h));
                a *= 1.0h + cord * 0.02h * lane - 0.09h * (0.5h - 0.5h * groove) * lines;
                a = lerp(a, min(a * 1.035h, half3(1.0h, 1.0h, 1.0h)), m.r);
                a = lerp(a, a * half3(0.66h, 0.78h, 0.93h), m.g);
                a *= 1.0h - m.b * (0.05h + 0.1h * saturate(-chop.y));
                a = lerp(a, a * half3(0.95h, 0.97h, 1.0h), m.a);
                a = lerp(a, a * half3(0.78h, 0.81h, 0.85h), slush);
                s.albedo = a;

                // the shine
                s.smoothness = saturate(s.smoothness + cord * 0.05h + m.r * -0.16h + m.g * 0.52h + m.b * -0.06h + m.a * 0.2h + slush * 0.3h);
            }

            void FarFragment(Varyings input, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if !defined(FARVIEW_PLAIN)
                FarviewClipWindow(FarviewAnd(FarviewAnd(half2(0.0h, FarviewShown(input.positionWS)), FarviewSwapWindow()),
                                             FarviewNearFarWindow(TransformObjectToWorld(float3(0, 0, 0)))), input.positionCS);
                #endif

                // (from here: URP's LitPassFragment, with the snow underfoot put into the surface before it is lit)
                #if defined(_PARALLAXMAP)
                #if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
                    half3 viewDirTS = input.viewDirTS;
                #else
                    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
                #endif
                    ApplyPerPixelDisplacement(viewDirTS, input.uv);
                #endif

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);
                SnowSurface(input.positionWS, input.uv, surfaceData);

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(input.positionCS);
                #endif

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

                #if defined(_DBUFFER)
                    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
                #endif

                InitializeBakedGIData(input, inputData);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));
                outColor = color;

                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
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
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // depth (and normals, for effects that want them), cut by the band as the colour is
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex FarDepthVertex
            #pragma fragment FarDepthFragment
            #pragma multi_compile_local _ FARVIEW_PLAIN
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "../Shaders/Farview.hlsl"
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V FarDepthVertex(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            half FarDepthFragment(V i) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if !defined(FARVIEW_PLAIN)
                FarviewClipWindow(FarviewAnd(FarviewAnd(half2(0.0h, FarviewShown(i.positionWS)), FarviewSwapWindow()), FarviewNearFarWindow(TransformObjectToWorld(float3(0, 0, 0)))), i.positionCS);
                #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex FarDepthVertex
            #pragma fragment FarNormalsFragment
            #pragma multi_compile_local _ FARVIEW_PLAIN
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/Farview.hlsl"
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V FarDepthVertex(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            void FarNormalsFragment(V i, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if !defined(FARVIEW_PLAIN)
                FarviewClipWindow(FarviewAnd(FarviewAnd(half2(0.0h, FarviewShown(i.positionWS)), FarviewSwapWindow()), FarviewNearFarWindow(TransformObjectToWorld(float3(0, 0, 0)))), i.positionCS);
                #endif
                float3 n = normalize((float3)i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                outNormalWS = half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
                #else
                outNormalWS = half4(n, 0.0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
