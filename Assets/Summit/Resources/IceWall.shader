// Summit's glacier ice: the walls and floor of every crevasse wear this (CourseDressing.IceMaterial).
// A photo-scanned fractured ice surface (Resources/Ice, made by Tools/MakeIce.py) laid flat on the walls by their
// length and height, so nothing depends on the walls' own stretched mapping.
//   - colour by depth: snow white at the lip with a ragged lower edge, pale blue, a luminous blue, deep blue, and
//     near black at the very bottom, as light runs out in real ice
//   - the scan's fractures on top: its white fracture planes stay white in the blue, its normal map tilts each
//     block so they catch the light one by one, and a second, larger copy tints it in broad patches so it never tiles
//   - annual layers: bands that follow the lip down the hill, some bluer, some whiter and bubbly, with a thin
//     dirty line where each summer's surface was
//   - a short parallax step on the scan's height map (in the albedo's alpha), so close up - the fall camera - the
//     blocks sit at different depths in the wall. _Parallax = 0 turns it off and saves two texture samples
//   - glossy: a sharp highlight from the sun or moon and from the headlamp and other lamps
//   - a blue glow of its own (light through the ice), so the walls still read in shade, at dusk and at night
// Lit like everything else (the sun or moon with its shadows, the sky's light, lamps, fog).
// Cheap: three texture samples, five with the parallax step.
// The mesh gives: uv0 = (metres below the lip, 0 at the lip to 1 at the bottom), uv1 = (metres along the
// crevasse, metres up), both small numbers so the mapping stays sharp far down the mountain.
Shader "Summit/IceWall"
{
    Properties
    {
        [MainTexture] [NoScaleOffset] _BaseMap ("Ice (rgb), height (a)", 2D) = "grey" {}
        [NoScaleOffset] [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _BumpScale ("Normal strength", Range(0, 3)) = 1.2
        _Smoothness ("Smoothness", Range(0, 1)) = 0.8
        _Tile ("Metres per repeat: near, far", Vector) = (4.5, 23, 0, 0)
        _Parallax ("Parallax depth in metres (0 = off)", Range(0, 0.2)) = 0.07
        _Layers ("Annual layers: thickness (m), strength", Vector) = (0.9, 1, 0, 0)
        _Glow ("Glow (light through the ice)", Color) = (0.30, 0.46, 0.58, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _BumpScale;
            half _Smoothness;
            float4 _Tile;
            float _Parallax;
            float4 _Layers;
            half4 _Glow;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off   // seen from above and from inside
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float4 uv : TEXCOORD2; half fog : TEXCOORD3;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = float4(i.uv0, i.uv1);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // a sharp highlight off the ice from one light
            half Shine(float3 n, float3 toLight, float3 toCam, half gloss)
            {
                half ndh = saturate(dot(n, normalize(toLight + toCam)));
                return pow(ndh, lerp(6.0, 140.0, gloss)) * gloss * (0.25 + 1.6 * gloss) * saturate(dot(n, toLight) * 4.0);
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 p = i.positionWS;
                float3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 toCam = GetCameraPositionWS() - p;
                float dist = length(toCam);
                toCam /= max(dist, 0.001);
                float metresDown = i.uv.x;        // below the lip
                half t = saturate(i.uv.y);        // 0 at the lip, 1 at the bottom
                // the scan is laid on the walls (which face across the slope, along x); the narrow floor, which
                // faces up, takes it stretched and without relief - it is all but black down there anyway
                half wall = 1.0 - smoothstep(0.6, 0.9, abs(n.y));

                // a second, larger copy of the scan, for broad patches of lighter and darker ice
                half3 broad = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.zw / _Tile.y + 0.37).rgb;
                half broadT = sqrt(broad.g);
                // annual layers, following the lip down the hill and wandering a little
                float layer = (metresDown + (broadT - 0.52) * 0.35 + sin(i.uv.z * 0.23) * 0.3 + sin(i.uv.z * 0.71 + 1.3) * 0.1) / max(_Layers.x, 0.05);
                float layerId = floor(layer), layerF = frac(layer);
                half rnd = frac(sin(layerId * 12.9898) * 43758.5453);

                // (each layer takes its own part of the scan, so its blocks never line up into a repeat down the wall;
                // the jump hides in the dirty line between layers)
                float2 uv = i.uv.zw / _Tile.x;
                float2 dx = ddx(uv), dy = ddy(uv);
                uv += float2(rnd * 7.31, rnd * 3.17);
                // parallax: step the mapping along the view by the scan's height, twice, so the blocks sit at
                // different depths in the wall (only near, where it shows: it fades out by 30 m)
                UNITY_BRANCH if (_Parallax > 0.0)
                {
                    float3 viewT = float3(toCam.z, toCam.y, max(abs(toCam.x), 0.35));   // along, up, out of the wall
                    float2 reach = viewT.xy / viewT.z * (_Parallax / _Tile.x) * wall * saturate(1.5 - dist / 20.0);
                    half h1 = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv, dx, dy).a;
                    half h2 = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv + reach * (h1 - 1.0), dx, dy).a;
                    uv += reach * ((h1 + h2) * 0.5 - 1.0);
                }
                half4 ice = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv, dx, dy);
                // (the texture is sRGB, so its colours arrive linear: for the masks below, its tones as scanned,
                // about 0.52 on average)
                half iceT = sqrt(ice.g);

                // snow at the lip, its lower edge ragged; below it the ice
                half snowy = 1.0 - smoothstep(0.55, 1.5, metresDown + (iceT - 0.52) * 0.9 + (broadT - 0.52) * 0.6);

                // the ice's own colour by depth: pale blue, a luminous blue, deep blue, near black at the bottom
                half3 blue = lerp(half3(0.50, 0.78, 0.93), half3(0.21, 0.60, 0.87), saturate((t - 0.05) / 0.15));
                blue = lerp(blue, half3(0.04, 0.23, 0.60), saturate((t - 0.2) / 0.35));
                blue = lerp(blue, half3(0.009, 0.064, 0.26), saturate((t - 0.55) / 0.3));
                blue = lerp(blue, half3(0.002, 0.009, 0.04), saturate((t - 0.85) / 0.15));

                // the layers: each its own shade, some whiter and bubbly, with a thin dirty line where the summer's
                // surface was
                half bubbly = smoothstep(0.6, 0.95, rnd) * 0.22 * _Layers.y;
                half dirt = (1.0 - smoothstep(0.0, 0.05, layerF) * (1.0 - smoothstep(0.93, 1.0, layerF))) * 0.45 * _Layers.y;
                half shade = lerp(1.0, lerp(0.7, 1.25, frac(rnd * 5.77)), _Layers.y);

                // the scan over it: darker and lighter blocks, and its bright fracture planes white in the blue
                half vein = smoothstep(0.62, 0.95, iceT);
                half3 pale = half3(0.62, 0.80, 0.92);
                half3 albedo = blue * (0.6 + 0.8 * iceT) * (0.45 + 1.1 * broadT) * shade;
                albedo = lerp(albedo, pale * (0.08 + 0.92 * (1.0 - t) * (1.0 - t)), saturate(vein * 0.7 + bubbly));
                albedo *= 1.0 - dirt;
                albedo = lerp(albedo, half3(0.89, 0.93, 1.0) * (0.9 + 0.2 * iceT), snowy);
                albedo *= _BaseColor.rgb;

                // the scan's normal, laid on the wall as the colour is (x along the crevasse, y up)
                half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BumpMap, uv, dx, dy), _BumpScale * wall * (1.0 - 0.6 * snowy));
                float3 nb = normalize(float3(abs(tn.z) * n.x, tn.y + n.y, tn.x + n.z));

                // the scene's light: sky light (less of it the deeper you go, and in the scan's hollows), the sun
                // or moon, lamps (the headlamp at night), each with its highlight off the ice
                half gloss = lerp(_Smoothness, 0.3, snowy);
                half3 light = SampleSH(nb) * lerp(1.0, 0.3, t) * (0.7 + 0.3 * ice.a);
                Light sun = GetMainLight(TransformWorldToShadowCoord(p));
                half3 sunLit = sun.color * (sun.distanceAttenuation * sun.shadowAttenuation);
                light += sunLit * saturate(dot(nb, sun.direction));
                half3 spec = sunLit * Shine(nb, sun.direction, toCam, gloss);
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; k++)
                {
                    Light l = GetAdditionalLight(k, p);
                    half3 lampLit = l.color * (l.distanceAttenuation * l.shadowAttenuation);
                    light += lampLit * saturate(dot(nb, l.direction));
                    spec += lampLit * Shine(nb, l.direction, toCam, gloss);
                }
                #endif
                // light through the ice: its own blue, a little stronger where you look straight into the wall
                half3 glow = _Glow.rgb * blue * (0.55 + 0.9 * iceT) * (0.75 + 0.45 * saturate(dot(nb, toCam))) * (1.0 - 0.5 * snowy);

                half3 c = albedo * light + spec + glow;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        // depth as any plain surface (the walls cast no shadows: CourseDressing turns that off)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
