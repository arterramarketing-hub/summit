// Summit's rock: every rock face on the mountain wears this (RockFace.Material) - the terrain's cliffs, gorge walls
// and steep banks, the ridges' outcrops, the spires. A scanned cliff face (Tools/MakeRockScan.py: its colour in
// RockAlbedo, its surface in RockRelief), mapped in world space from three sides at once, so a sheer wall is never
// stretched and nothing depends on a mesh's UVs.
//   - two scales of the same rock: the near one for its grain and cracks, a far one (eight times larger) tinting
//     it in broad patches, so it never tiles, near or a kilometre off
//   - relief: the scan's normals bend the light at both scales, so cracks and blocks catch a low sun, the moon
//     and the lamps. (A parallax-occlusion step near the camera used to sit on top behind a keyword; it was never
//     switched on in the game and doubled the variants, so it is gone.)
//     The silhouette and the depth buffer stay the flat face's: no depth is written from the fragment shader.
//   - strata: horizontal bands of slightly different rock, each with a dark bedding seam, wandering a little
//   - snow caught on whatever faces up enough to hold it (ledges, the tops of blocks), and in broken lines along
//     the strata's seams, which is what still reads from across a canyon when the face is in shade
// Lit like everything else (the sun or moon with its shadows, the sky's light, the headlamp and other lamps, fog),
// so it works at night too. Twelve texture samples a pixel.
// The terrain's own rock (RockFace.TerrainMaterial: the keyword SUMMIT_ROCK_TERRAIN) is drawn over the snow wherever
// any of it may show, and lets the snow through pixel by pixel: each corner says how bare the ground is there (its
// colour's alpha: RockFace.Rockiness), and the rock's own patches and cracks move the line about, so the snow line
// wanders through gullies and round blocks instead of following the mesh's triangles, with a fringe of patchy
// dusting and a drift's lip along it.
// Everything streamed comes in through the world's far band (Shaders/Farview.hlsl): the terrain's rock with its chunk,
// the crags and outcrops as the rider comes within reach of them. The crags' near and far looks cross-fade (their
// LODGroup, LOD_FADE_CROSSFADE).
Shader "Summit/Rock"
{
    Properties
    {
        [MainTexture] _BaseMap ("Rock", 2D) = "grey" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
        _ReliefMap ("Relief: normal in RG, height in B", 2D) = "bump" {}
        _Relief ("Relief: depth (m), full within (m), gone by (m), normal strength", Vector) = (0.2, 18, 50, 1)
        _Scales ("Metres per repeat: near, far", Vector) = (5, 41, 0, 0)
        _Strata ("Strata: band height (m), strength", Vector) = (6.5, 1, 0, 0)
        _Snow ("Snow on faces up from, to (normal.y)", Vector) = (0.48, 0.66, 0, 0)
        _SnowColor ("Snow", Color) = (0.86, 0.9, 0.96, 1)
        _Line ("Terrain's snow line: how far the rock moves it, fringe width", Vector) = (2.6, 0.16, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _ReliefMap_TexelSize;
            float4 _Relief;
            half4 _BaseColor;
            half _Smoothness;
            float4 _Scales;
            float4 _Strata;
            float4 _Snow;
            half4 _SnowColor;
            float4 _Line;
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
            // (multi_compile, not shader_feature: the terrain's material is made at run time, so no saved material
            // would keep the variant in a build)
            #pragma multi_compile_local _ SUMMIT_ROCK_TERRAIN
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif
            #include "../Shaders/Farview.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_ReliefMap); SAMPLER(sampler_ReliefMap);

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL;
                #if defined(SUMMIT_ROCK_TERRAIN)
                half4 color : COLOR;
                #endif
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half fog : TEXCOORD2;
                #if defined(SUMMIT_ROCK_TERRAIN)
                half2 bare : TEXCOORD3;   // how bare the ground is here, and the ground's own smooth normal.y
                #endif
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                #if defined(SUMMIT_ROCK_TERRAIN)
                o.bare = half2(i.color.a, i.color.r);
                #endif
                return o;
            }

            // the rock seen from three sides, blended by which way the surface faces (texture v is always up the face).
            // dx and dy are how far the face's own point moves from one pixel to the next, across and down the screen:
            // the mip level is taken from those and not from p, because p may be a parallaxed point, which jumps
            // from pixel to pixel at the edge of a block and would sparkle there
            half3 Tri(float3 p, float3 w, float metres, float3 dx, float3 dy)
            {
                float k = 1.0 / metres;
                dx *= k; dy *= k;
                half3 a = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, p.zy * k, dx.zy, dy.zy).rgb;
                half3 b = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, p.xz * k, dx.xz, dy.xz).rgb;
                half3 c = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, p.xy * k, dx.xy, dy.xy).rgb;
                return a * w.x + b * w.y + c * w.z;
            }

            // the relief seen from three sides. xyz: how far the scan's slopes tilt the face's normal, in world
            // space (each side's two texture axes are two world axes, and a slope along +u leans the normal along
            // that world axis whichever way the face looks, so nothing needs mirroring); w: the height, 0 to 1
            half4 TriRelief(float3 p, float3 w, float metres, float3 dx, float3 dy)
            {
                float k = 1.0 / metres;
                dx *= k; dy *= k;
                half3 a = SAMPLE_TEXTURE2D_GRAD(_ReliefMap, sampler_ReliefMap, p.zy * k, dx.zy, dy.zy).rgb;
                half3 b = SAMPLE_TEXTURE2D_GRAD(_ReliefMap, sampler_ReliefMap, p.xz * k, dx.xz, dy.xz).rgb;
                half3 c = SAMPLE_TEXTURE2D_GRAD(_ReliefMap, sampler_ReliefMap, p.xy * k, dx.xy, dy.xy).rgb;
                half3 tilt = 0.0;
                tilt.zy += (a.rg * 2.0 - 1.0) * w.x;
                tilt.xz += (b.rg * 2.0 - 1.0) * w.y;
                tilt.xy += (c.rg * 2.0 - 1.0) * w.z;
                return half4(tilt, a.b * w.x + b.b * w.y + c.b * w.z);
            }

            #define RELIEF_MEAN 0.567   // the height map's average (Tools/MakeRockScan.py prints it)

            #if defined(SUMMIT_ROCK_TERRAIN)
            // A noise that never repeats, for where the snow line wanders across the terrain's rock. The rock textures
            // repeat (every 5 and 41 m), and a line keyed to them comes back at the same spacing all the way across a
            // face: from afar, bands like contour lines. (A hash without sine, which loses precision far from the
            // origin on phone GPUs)
            float Hash3(float3 c)
            {
                c = frac(c * 0.1031);
                c += dot(c, c.zyx + 31.32);
                return frac((c.x + c.y) * c.z);
            }
            float ValueNoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x);
                float b = lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x);
                float c = lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x);
                float d = lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
            }
            #endif

            half4 frag(Varyings i) : SV_Target
            {
                FarviewClip(i.positionWS, i.positionCS);
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
                #endif
                float3 p = i.positionWS;
                float3 n = normalize(i.normalWS);
                float3 w = pow(abs(n), 4.0); w /= (w.x + w.y + w.z);
                float3 toCam = GetCameraPositionWS() - p;
                float dist = length(toCam);
                toCam /= max(dist, 1e-4);

                // where the near rock is looked up: on the face itself
                float3 q = p;
                float3 dpx = ddx(p), dpy = ddy(p);

                half3 nearRock = Tri(q, w, _Scales.x, dpx, dpy);
                half3 farRock = Tri(p + 17.3, w, _Scales.y, dpx, dpy);
                // the relief at both scales: the near one's blocks and cracks, and the far one's as broad swells
                // across a whole wall (gentler), which is what a low sun still picks out from across a canyon
                half4 nearRelief = TriRelief(q, w, _Scales.x, dpx, dpy);
                half4 farRelief = TriRelief(p + 17.3, w, _Scales.y, dpx, dpy);
                // past a hundred metres or so the near rock's 5 m repeat shows as a lattice of blobs and cracks across a
                // whole face (and every mark keyed to it, snow too, lines up with it): there it gives way to the far rock
                half nearFade = saturate((dist - 60.0) / 120.0);
                nearRock = lerp(nearRock, farRock, nearFade);
                nearRelief = lerp(nearRelief, half4(0, 0, 0, RELIEF_MEAN), nearFade);
                float3 nb = normalize(n + (nearRelief.xyz + farRelief.xyz * 0.6) * _Relief.w);
                // (the texture is sRGB, so its colours arrive linear: for the masks and tints below, its tones as
                // painted, about 0.36 on average)
                half3 nearT = sqrt(nearRock), farT = sqrt(farRock);
                // the far rock tints the near one in broad patches (its mean is about 0.38, so this keeps the brightness),
                // and across a whole face, slow swings between warm and cold rock, so even a kilometre off a cliff is
                // never one flat grey
                half3 albedo = nearRock * (0.3 + 1.95 * farT);
                half swing = sin(p.x * 0.0041 + p.z * 0.0029 + 1.7) * sin(p.y * 0.006 - p.z * 0.0023) * 0.5 + 0.5;
                albedo *= lerp(half3(1.2, 1.04, 0.84), half3(0.8, 0.94, 1.16), swing) * lerp(0.75, 1.2, farT.g * 2.0 - 0.25);
                // sky light reaches less far into the cracks than onto the blocks' faces (the scan's height averages
                // RELIEF_MEAN, so this leaves the rock's overall brightness where it was)
                albedo *= 1.0 + (nearRelief.w - RELIEF_MEAN) * 0.7;

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
                // (and by the relief: on the tops of blocks before their undersides)
                // (on the terrain, by the ground's smooth slope, not each facet's tilt, or the snow lies in triangles)
                #if defined(SUMMIT_ROCK_TERRAIN)
                half groundY = i.bare.y;
                #else
                half groundY = n.y;
                #endif
                #if defined(SUMMIT_ROCK_TERRAIN)
                // (on the terrain the broad patches come from a noise that never repeats, not the far rock: see ValueNoise)
                half broad = (half)(ValueNoise(p * (1.0 / 26.0) + 3.7) * 0.65 + ValueNoise(p * (1.0 / 11.0) - 8.1) * 0.35) - 0.5;
                #else
                half broad = (farT.b - 0.38) * 1.4;
                #endif
                half up = groundY + broad * 0.43 + (nearT.g - 0.36) * 0.5 + (nb.y - n.y) * 0.5;
                half snow = smoothstep(_Snow.x, _Snow.y, up);
                // and along the bedding seams, where the strata leave a little shelf: broken lines of snow across
                // even a sheer face (not under an overhang). In shade and haze, when the rock's own colours have all
                // but gone, these are what still say "cliff" from across a canyon
                half shelf = (1.0 - seam) * smoothstep(0.48, 0.66, farT.g * 1.7 + nearT.r * 0.7 - 0.3) * saturate(groundY * 2.0 + 0.9);
                shelf *= 1.0 - _Strata.z;   // (z: none at all, for tests)
                snow = max(snow, shelf * 0.85);

                #if defined(SUMMIT_ROCK_TERRAIN)
                // the snow line, pixel by pixel: how bare the ground is here, pushed about by the rock itself - its
                // broad patches (a noise that never repeats), its blocks standing proud and its cracks (the relief) - so snow lies
                // on in the hollows and cracks and the blocks and ribs come through first; and less where the strata
                // leave a shelf. Under the line the rock lets the snow below it show through
                // Only near the line, though: where the ground is plainly bare (a cliff), no crack in the rock lets snow
                // through, or the cracks' own repeat shows as a lattice of white ticks across the whole face
                half push = broad * 0.5 + (nearT.r - 0.36) * 0.3 + (nearRelief.w - RELIEF_MEAN) * 0.35 - shelf * 0.12;
                half bare = i.bare.x + _Line.x * push * (1.0 - smoothstep(0.75, 0.97, i.bare.x));
                clip(bare - 0.5);
                // just inside the line, the rock is still patchy with snow (thicker the nearer the line), and right
                // at it a drift's lip, smooth, facing up and catching the light, over a thin shadowed crease where it
                // leaves the rock
                half inside = saturate((bare - 0.5) / _Line.y);
                half dust = 1.0 - smoothstep(0.0, 1.0, inside);
                half lip = 1.0 - smoothstep(0.0, 0.3, inside);
                snow = max(snow, dust * dust * 0.95);
                half crease = smoothstep(0.2, 0.42, inside) * (1.0 - smoothstep(0.42, 0.7, inside));
                nb = normalize(lerp(nb, float3(0, 1, 0), lip * 0.5));
                #endif
                albedo = lerp(albedo, _SnowColor.rgb, snow);
                #if defined(SUMMIT_ROCK_TERRAIN)
                albedo *= 1.0 - crease * 0.35;
                #endif

                // the scene's light, as on any rough surface: sky light, the sun or moon, lamps (the headlamp at night).
                // From here on the normal is the relief's; snow lies smoother than the rock under it
                n = normalize(lerp(nb, n, snow * 0.8));
                half3 light = SampleSH(n);
                Light sun = GetMainLight(TransformWorldToShadowCoord(p));
                half sunLit = sun.distanceAttenuation * sun.shadowAttenuation;
                light += sun.color * (sunLit * saturate(dot(n, sun.direction)));
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
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif
            #include "../Shaders/Farview.hlsl"
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            V vert(float4 positionOS : POSITION)
            {
                V o;
                o.positionWS = TransformObjectToWorld(positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half frag(V i) : SV_Target
            {
                FarviewClip(i.positionWS, i.positionCS);
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
}
