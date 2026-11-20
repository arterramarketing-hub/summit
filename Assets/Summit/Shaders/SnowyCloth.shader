// The rider's jacket and trousers, which get caked in snow when you fall (see SnowyClothes.cs).
// Lit like URP Lit (same PBR lighting, so it matches the rest of the rider), plus one number, _Snow:
// how much snow is stuck on. Where the snow sits comes from a 3D noise in the body's own rest-pose
// coordinates (baked into the mesh, so the clumps stay put on the cloth however the limbs move),
// biased towards surfaces that face up and the side that hit the snow. Clumps are pushed out a little
// and bumped from screen-space derivatives: no textures, two noise lookups a pixel, cheap on a phone.
Shader "Summit/SnowyCloth"
{
    Properties
    {
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _Snow ("Snow amount", Range(0, 1)) = 0
        _SnowScale ("How readily this garment holds snow", Range(0, 2)) = 1
        _Wet ("Damp", Range(0, 1)) = 0
        _Fuzz ("Fleece pile (0 = plain cloth)", Range(0, 1)) = 0
        _SnowUp ("Up (rest frame)", Vector) = (0, 1, 0, 0)
        _SnowDir ("Impact side (rest frame), w = strength", Vector) = (0, 0, 1, 0)
        _Color2 ("Second colour (the pattern's)", Color) = (1, 1, 1, 1)
        _Pattern ("Pattern: 0 plain, 1 stripes, 2 camo, 3 colour block, 4 plaid, 5 Nordic knit", Float) = 0
        _PatternScale ("Pattern repeats a metre", Float) = 7
        _PatternAt ("Colour block: the height (rest frame, m) it changes at; negative: below that height", Float) = 1.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Smoothness;
            half _Snow;
            half _SnowScale;
            half _Wet;
            half _Fuzz;
            float4 _SnowUp;
            float4 _SnowDir;
            half4 _Color2;
            half _Pattern;
            half _PatternScale;
            half _PatternAt;
        CBUFFER_END

        // value noise in 3D, 0..1
        float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float Noise(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }
        // the cloth's pattern, laid out on the rest pose in metres (so it stays put on the garment however the limbs
        // move): how much of the second colour shows, and a darkening for camo's third tone. One uniform branch;
        // plain cloth pays for the test only
        half StripeBand(float x)
        {
            float tri = abs(frac(x) - 0.5) * 2.0;           // 0 in the middle of one band, 1 in the middle of the next
            float aa = max(fwidth(x) * 2.0, 1e-4);
            return smoothstep(0.5 - aa, 0.5 + aa, tri);
        }
        // Nordic knit (Brianna's fleece): bands round the body, each a row of one motif in the second colour: open
        // diamonds with a small one inside, a zigzag, a row of dots. x runs round the body, y up it, both in repeats
        half Knit(float2 p)
        {
            float px = max(fwidth(p.x) + fwidth(p.y), 1e-4), aa = min(px, 0.03);
            float y = frac(p.y);
            // diamonds, two to a repeat: an outline and its middle
            float2 d = float2(abs(frac(p.x * 2.0) - 0.5) * 0.5, abs(y - 0.26));
            float dm = d.x + d.y;
            half k = smoothstep(0.215 + aa, 0.215 - aa, dm) * smoothstep(0.15 - aa, 0.15 + aa, dm) + smoothstep(0.065 + aa, 0.065 - aa, dm);
            // the zigzag, four points to a repeat
            float zz = y - 0.63 - (abs(frac(p.x * 4.0) - 0.5) - 0.25) * 0.22;
            k += smoothstep(0.024 + aa, 0.024 - aa, abs(zz));
            // dots, set between the zigzag's points
            float2 o = float2((frac(p.x * 4.0 + 0.5) - 0.5) * 0.25, y - 0.87);
            k += smoothstep(0.032 + aa, 0.032 - aa, length(o));
            // (far off, where a motif is a pixel or two, it settles to the even mix the knit averages to)
            return lerp(saturate(k), 0.18, saturate((px - 0.03) / 0.07));
        }
        half3 PatternColour(half3 a, float3 restPos, float3 restNormal)
        {
            if (_Pattern < 0.5) return a;
            float h = dot(restPos, _SnowUp.xyz);
            if (_Pattern < 1.5) return lerp(a, _Color2.rgb, StripeBand(h * _PatternScale));
            if (_Pattern < 2.5)
            {
                float n = Noise(restPos * _PatternScale) * 0.65 + Noise(restPos * _PatternScale * 2.6 + 3.1) * 0.35;
                half3 c = lerp(a, _Color2.rgb, smoothstep(0.53, 0.56, n));
                return lerp(c, a * 0.5, smoothstep(0.37, 0.34, n));
            }
            if (_Pattern < 3.5)
            {
                float k = _PatternAt >= 0 ? smoothstep(_PatternAt - 0.006, _PatternAt + 0.006, h) : smoothstep(-_PatternAt + 0.006, -_PatternAt - 0.006, h);
                return lerp(a, _Color2.rgb, k);
            }
            float3 side = normalize(cross(_SnowUp.xyz, float3(0.0, 0.0, 1.0)) + float3(1e-4, 0.0, 0.0));
            if (_Pattern > 4.5)
            {
                // laid on from the front and back, or from the sides where the cloth faces that way, so the motifs
                // keep their shape round the body (tone on tone, the join doesn't show)
                float3 fwd = cross(side, _SnowUp.xyz);
                float x = abs(dot(restNormal, side)) > abs(dot(restNormal, fwd)) ? dot(restPos, fwd) : dot(restPos, side);
                return lerp(a, _Color2.rgb, Knit(float2(x, h) * _PatternScale));
            }
            // plaid: bands round the body and down it, crossed, with a thin dark line through each
            half across = StripeBand(h * _PatternScale), down = StripeBand(dot(restPos, side) * _PatternScale);
            half3 c = lerp(a, _Color2.rgb, (across + down) * 0.5);
            half grid = StripeBand(h * _PatternScale * 4.0 + 0.5) * StripeBand(dot(restPos, side) * _PatternScale * 4.0 + 0.5);
            return c * (1.0 - 0.25 * grid);
        }

        // big clumps with a finer crust on them
        float Clumps(float3 restPos) { return Noise(restPos * 11.0) * 0.68 + Noise(restPos * 37.0 + 7.3) * 0.32; }

        // how strongly this bit of cloth holds snow: some everywhere, more facing up and on the side that hit
        float Bias(float3 restNormal)
        {
            float up = saturate(dot(restNormal, _SnowUp.xyz));
            float hit = saturate(dot(restNormal, _SnowDir.xyz)) * _SnowDir.w;
            return 0.45 + 0.45 * up + 0.75 * hit;
        }
        // coverage before clamping: below 0 bare cloth, above 1 solid snow. The square root lets a light fall
        // show plainly, while even a full caking leaves cloth showing on the sheltered, downward faces
        float Cover(float clumps, float bias) { return (sqrt(saturate(_Snow * _SnowScale)) * bias * 1.6 + clumps - 1.05) * 5.0; }

        // the snow sits proud of the cloth, more where it is thick: up to a centimetre, pushed out in world
        // space (a skinned mesh's own units say nothing about metres)
        // (along the smoothed normal baked into the tangents, so the flat-shaded faces stay joined)
        float3 SwellWS(float3 positionOS, float3 smoothOS, float3 restPos, float3 restNormal)
        {
            float c = saturate(Cover(Clumps(restPos), Bias(restNormal)));
            return TransformObjectToWorld(positionOS) + TransformObjectToWorldNormal(smoothOS) * (c * 0.01);
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
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;      // the smoothed normal (baked by SnowyClothes)
                float3 restPos : TEXCOORD2;      // rest-pose position, metres
                float3 restNormal : TEXCOORD3;   // rest-pose normal
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 restPos : TEXCOORD2;
                float3 restNormal : TEXCOORD3;
                half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = SwellWS(i.positionOS.xyz, i.tangentOS.xyz, i.restPos, i.restNormal);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.restPos = i.restPos;
                o.restNormal = i.restNormal;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float clumps = Clumps(i.restPos);
                float raw = Cover(clumps, Bias(normalize(i.restNormal + 1e-5)));
                float cover = saturate(raw);
                // lumps: bump the normal by the snow's height (metres: a few millimetres of crust), from how it
                // changes across the screen
                float h = cover * (0.6 + clumps) * 0.002;
                float3 dpdx = ddx(i.positionWS), dpdy = ddy(i.positionWS);
                float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
                float det = dot(dpdx, r1);
                float3 tilt = sign(det) * (ddx(h) * r1 + ddy(h) * r2) / max(abs(det), 1e-12);
                tilt *= min(1.0, 0.45 / max(length(tilt), 1e-6));   // never more than about 25 degrees
                n = normalize(n - tilt);

                // the cloth darkens where the snow is melting into it
                half damp = saturate(_Wet * (0.5 + 0.5 * saturate(1.0 - abs(raw + 0.6))));
                half3 cloth = PatternColour(_BaseColor.rgb, i.restPos, i.restNormal) * (1.0 - 0.35 * damp);
                // fleece (_Fuzz): a soft pile. Its colour is faintly mottled, and it blooms paler where the cloth turns
                // away from the eye, as the fibres catch the light there. Nothing changes for plain cloth (_Fuzz 0).
                half3 toEye = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half bloom = pow(1.0 - saturate(dot(n, toEye)), 3.0);
                cloth *= 1.0 + _Fuzz * 0.14 * (Noise(i.restPos * 90.0) - 0.5);
                cloth = lerp(cloth, saturate(cloth * 1.5 + 0.07), _Fuzz * 0.75 * bloom);
                half3 snow = half3(0.92, 0.93, 0.95) * (0.84 + 0.18 * clumps);

                SurfaceData s = (SurfaceData)0;
                s.albedo = lerp(cloth, snow, cover);
                s.metallic = 0;
                s.specular = 0;
                s.smoothness = lerp(saturate(_Smoothness + 0.25 * damp), 0.12, cover);
                s.occlusion = 1;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(n);
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float3 restPos : TEXCOORD2; float3 restNormal : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 pos = SwellWS(i.positionOS.xyz, i.tangentOS.xyz, i.restPos, i.restNormal);
                float3 nrm = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 toLight = normalize(_LightPosition - pos);
            #else
                float3 toLight = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pos, nrm, toLight));
                cs = ApplyShadowClamping(cs);
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float3 restPos : TEXCOORD2; float3 restNormal : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                return TransformWorldToHClip(SwellWS(i.positionOS.xyz, i.tangentOS.xyz, i.restPos, i.restNormal));
            }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
