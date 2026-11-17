// The deformable snow layer round the rider (see SnowLayer.cs): one flat grid mesh, lifted onto the terrain in the
// vertex shader from two buffers the CPU fills: the terrain under each cell (height and smooth normal, written once
// as the cell scrolls into the patch) and what has been done to it (how far the snow was pushed down or piled up,
// how packed it is, and a stain). The cells wrap round in the buffers, so moving the patch only rewrites the strip
// that came into view, and the mesh itself never changes.
//
// Shaded exactly like the terrain (URP Lit's own surface code fed the snow material's maps), so where the snow is
// untouched the layer is tucked out of sight under the terrain and the two can never be told apart. Where it is
// pressed in, the snow at the bottom is packed: a touch darker, cooler and smoother. Where it is thrown up, it is
// fresh, bright and matte.
//
// Holes go deeper than the terrain surface. The terrain would hide them, so their depth is written as if they sat
// just above it (their colour still comes from where they really are): nothing that belongs above the snow is ever
// hidden by this, and anything below the snow was hidden by the terrain anyway.
Shader "Summit/SnowLayer"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.32
        _Metallic ("Metallic", Range(0, 1)) = 0
        _BumpScale ("Normal scale", Float) = 1
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _DetailMask ("Detail mask", 2D) = "white" {}
        _DetailAlbedoMapScale ("Detail albedo scale", Range(0, 2)) = 1
        _DetailAlbedoMap ("Detail albedo", 2D) = "linearGrey" {}
        _DetailNormalMapScale ("Detail normal scale", Range(0, 2)) = 1
        [Normal] _DetailNormalMap ("Detail normal", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        // always the snow material's look: normal map and doubled detail, no variants to strip out of a build
        #define _NORMALMAP 1
        #define _DETAIL_MULX2 1
        #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

        StructuredBuffer<float4> _SnowBase;   // x: terrain height, y/z: terrain normal x/z, w: 1 snow, 0 rock or a hole
        StructuredBuffer<float4> _SnowDyn;    // x: height change (m), y: packed 0..1, z: stain 0..1
        float4 _SnowGrid;     // x/y: the patch's first cell (x, z), z: cells per side in the buffers, w: cells shown per side
        float4 _SnowParams;   // x: cell size (m), y: lift of disturbed snow over the terrain, z: fade-out band (cells)
        half4 _SnowStain;     // what a stain looks like; a: how strongly

        uint SlotOf(int i, int j)
        {
            // the cells wrap round in the buffers; shifted well positive first, so the wrap is an unsigned one
            uint n = (uint)_SnowGrid.z, big = n * 8192u;
            uint sx = (uint)((int)_SnowGrid.x + i + (int)big) % n;
            uint sz = (uint)((int)_SnowGrid.y + j + (int)big) % n;
            return sz * n + sx;
        }
        // marks flatten out over the last metre or so of the patch, so nothing ever ends in a cliff at its edge
        float Fade(int i, int j)
        {
            float m = _SnowGrid.w;
            float e = min(min((float)i, m - i), min((float)j, m - j));
            return smoothstep(0.0, _SnowParams.z, e);
        }
        float HeightAt(int i, int j)
        {
            uint s = SlotOf(i, j);
            return _SnowDyn[s].x * Fade(i, j) * _SnowBase[s].w;
        }

        struct Snow
        {
            float3 positionWS;   // where the snow really is
            float3 depthWS;      // where it counts as being for depth (never below the terrain) and shadows
            float3 normalWS;
            float4 mark;         // x: height change, y: packed, z: stain
        };

        Snow Displace(float2 cell, float3 positionOS)
        {
            Snow o;
            int i = (int)round(cell.x), j = (int)round(cell.y);
            uint s = SlotOf(i, j);
            float4 b = _SnowBase[s], d = _SnowDyn[s];
            float f = Fade(i, j) * b.w;
            float h = d.x * f, k = d.y * f, st = d.z * f;
            // untouched snow is the terrain's job: tuck this layer out of sight under it
            bool untouched = abs(h) < 0.0015 && k < 0.01 && st < 0.01;
            float3 p = TransformObjectToWorld(positionOS);
            // disturbed snow sits a few millimetres proud of the terrain so the two never fight, but the barely
            // touched edge of a mark comes down to meet it: no step, nothing to show where the layer starts
            float lift = _SnowParams.y * saturate(abs(h) / 0.01);
            p.y = untouched ? b.x - 0.05 : b.x + lift + h;
            o.positionWS = p;
            o.depthWS = p;
            if (!untouched) o.depthWS.y = max(p.y, b.x + 0.004);
            // the terrain's own smooth normal, tilted by the slope of the dents and piles
            float c2 = 2.0 * _SnowParams.x;
            float hr = HeightAt(i + 1, j), hl = HeightAt(i - 1, j), hu = HeightAt(i, j + 1), hd = HeightAt(i, j - 1);
            float hx = (hr - hl) / c2;
            float hz = (hu - hd) / c2;
            float3 nb = float3(b.y, sqrt(saturate(1.0 - b.y * b.y - b.z * b.z)), b.z);
            o.normalWS = normalize(nb / max(nb.y, 0.05) - float3(hx, 0, hz));
            // how hollow (+) or crested (-) the snow is here: the floor and foot of a dent get less light from the
            // sky, the crest of a rim catches it
            o.mark = float4(h, k, st, (hr + hl + hu + hd) * 0.25 - h);
            return o;
        }

        // clip position from where the snow is, depth from where it counts as being
        float4 ClipOf(Snow s)
        {
            float4 cs = TransformWorldToHClip(s.positionWS);
            float4 cd = TransformWorldToHClip(s.depthWS);
            cs.z = cd.z / cd.w * cs.w;
            return cs;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 cell : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 shadowWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 mark : TEXCOORD4;
                half fog : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Snow s = Displace(v.cell, v.positionOS.xyz);
                Varyings o;
                o.positionCS = ClipOf(s);
                o.positionWS = s.positionWS;
                o.shadowWS = s.depthWS;   // a hole below the terrain must not sit in the terrain's own shadow
                o.normalWS = s.normalWS;
                // the terrain's mapping: metres / 9, then the material's tiling
                float2 metres9 = s.positionWS.xz / 9.0;
                o.uv = TRANSFORM_TEX(metres9, _BaseMap);
                o.mark = s.mark;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                SurfaceData s;
                InitializeStandardLitSurfaceData(i.uv, s);
                float h = i.mark.x;
                half k = saturate(i.mark.y), st = saturate(i.mark.z);
                half fresh = saturate(h / 0.05);
                half deep = saturate(-h / 0.3);
                half hollow = saturate(i.mark.w * 45.0), crest = saturate(-i.mark.w * 45.0);
                // packed snow at the bottom of a dent: a touch darker and cooler, smoother, its grain pressed flat
                s.albedo *= lerp(half3(1, 1, 1), half3(0.82, 0.86, 0.94), k);
                s.smoothness = lerp(s.smoothness, 0.55, k * 0.8);
                s.normalTS = normalize(lerp(s.normalTS, half3(0, 0, 1), k * 0.75));
                // snow thrown up on the rims: fresh, bright and matte
                s.albedo = lerp(s.albedo, min(s.albedo * 1.14, 1.0), max(fresh, crest * 0.5 * saturate(h / 0.02)));
                s.smoothness = lerp(s.smoothness, 0.16, fresh);
                // a stain soaked into the snow (blood, say), wet and a little glossy
                half sa = st * _SnowStain.a;
                s.albedo = lerp(s.albedo, _SnowStain.rgb, sa);
                s.smoothness = lerp(s.smoothness, 0.6, sa * 0.6);
                // the bottom of a deep hole sees less sky
                s.occlusion = (1.0 - 0.45 * deep) * (1.0 - 0.45 * hollow);
                s.albedo *= 1.0 - 0.12 * hollow;

                float3 n = normalize(i.normalWS);
                // the terrain's tangents follow its mapping: across (x) and down (z) the hill
                float3 t = normalize(float3(1, 0, 0) - n * n.x);
                float3 bt = cross(t, n);
                float3 nw = normalize(TransformTangentToWorld(s.normalTS, half3x3(t, bt, n)));

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = nw;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.shadowWS);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(nw);
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
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 cell : TEXCOORD0; };
            float4 vert(Attributes v) : SV_POSITION { return ClipOf(Displace(v.cell, v.positionOS.xyz)); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct Attributes { float4 positionOS : POSITION; float2 cell : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Snow s = Displace(v.cell, v.positionOS.xyz);
                Varyings o; o.positionCS = ClipOf(s); o.normalWS = s.normalWS; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                return half4(PackFloat2To888(oct), 0);
            #else
                return half4(n, 0);
            #endif
            }
            ENDHLSL
        }
    }
}
