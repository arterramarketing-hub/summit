// The whole mountain seen from far away (FarMountain.cs: the start page's helicopter view), in the character of the
// game's backdrop photograph of Mount Rainier: heavy snow and blue-white glacier ice broken by ribs and bands of warm
// grey rock, crevasses across the ice where it steepens, a ragged snow line into dark forest. One cheap pass:
// snow, ice, rock and forest are decided per pixel from the height, the slope and one small noise texture, so the
// snow line and the tree line wander across the coarse triangles instead of following their edges. Lit by the
// sun or moon and the sky's ambient like everything else (so it follows the time of day), with the mountain's
// own shadow baked per vertex (colour alpha), and its own haze: a thin one everywhere and a thick one lying in
// the valleys, both in the fog's colour, so the far ground melts into the bottom of the sky.
Shader "Summit/FarMountain"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Noise (r value, gb slope, a second value)", 2D) = "grey" {}
        _FloorY ("Valley floor (world y)", Float) = -3100
        _TopY ("Summit (world y)", Float) = 20
        _TreeLine ("Tree line (0 floor .. 1 summit)", Float) = 0.4
        _Phase ("Piste phase (Slope.ph)", Float) = 0
        _FinishZ ("Finish (world z)", Float) = 4233
        _Haze ("Haze: 1 / the distance at which the view is two thirds gone", Float) = 0.0000125
        _Mist ("Valley mist per metre at the floor", Float) = 0.00018
        _MistH ("Valley mist height", Float) = 320
        _Lights ("Valley lights (night)", Float) = 0
        _ShadeBlend ("Shadow before (0) to the new one (1)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _FloorY, _TopY, _TreeLine, _Phase, _FinishZ, _Haze, _Mist, _MistH, _Lights, _ShadeBlend;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 P = i.positionWS;
                half3 N0 = normalize(i.normalWS);
                half4 n1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz / 760.0);
                half4 n2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.zx / 3300.0 + 0.37);
                // (the finest one turned, so its repeat never lines up with the others' or with the view)
                half4 n3 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(P.x * 0.79 + P.z * 0.61, P.z * 0.79 - P.x * 0.61) / 211.0 + 0.11);
                half h01 = saturate((P.y - _FloorY) / (_TopY - _FloorY));
                half hollow = i.color.b;   // low in gullies and bowls, high on ribs and crests

                // the ground's lie as the snow sees it: the mesh's, leaned a little by the broad noise only, so snow,
                // rock and forest come in whole fields and not in speckles
                half3 Nd = normalize(N0 + half3(n2.b - 0.5, 0, n2.g - 0.5) * 0.45 + half3(n1.g - 0.5, 0, n1.b - 0.5) * 0.3);

                // forest: below the tree line, where the ground can hold it, with clearings; thinning toward the line
                // (the line lower in the glaciers' beds: tongues of ice reach down between the forested ribs)
                half bed = saturate(0.86 - hollow);
                half line0 = _TreeLine + (n2.r - 0.5) * 0.22 + (n1.a - 0.5) * 0.1 + (n3.r - 0.5) * 0.04 - bed * 0.5;
                half below = line0 - h01;
                half forest = smoothstep(-0.01, 0.03, below) * smoothstep(0.5, 0.62, Nd.y);
                forest *= smoothstep(0.3, 0.44, n1.r + below * 6.0);
                // the piste: a white ribbon winding down through the trees (Slope.Centre, as the game rides it)
                float cx = 26.0 * sin(P.z * 0.0047 + _Phase) + 11.0 * sin(P.z * 0.0113 + 1.3 + _Phase * 1.7) + 5.0 * sin(P.z * 0.0240 + 0.4 + _Phase * 2.3);
                half piste = (1.0 - smoothstep(20.0, 34.0, abs(P.x - cx))) * step(0.0, P.z) * step(P.z, _FinishZ + 60.0);
                forest *= 1.0 - piste;

                // rock where it is too steep for snow: sooner on ribs, later in gullies, which hold their snow
                // (heavily snowed: only the cleavers' crests, the cliff bands and the steepest faces are bare)
                half rockLine = 0.8 + (n2.r - 0.5) * 0.42 + (n1.r - 0.5) * 0.12 + (hollow - 0.82) * 1.0 - h01 * 0.04;
                half rock = smoothstep(rockLine + 0.035, rockLine - 0.035, Nd.y) * (1.0 - piste);

                // relief the triangles are too coarse for: the noise's own slope bends the light's normal (most on rock)
                half3 N = N0;
                half bump = lerp(0.4, 1.0, rock) * (1.0 - 0.75 * forest);
                N.xz += ((n1.gb - 0.5) * 1.3 + (n2.bg - 0.5) * 0.7 + (n3.gb - 0.5) * 0.8) * bump;
                N = normalize(N);

                // rock: grey-brown in level bands, darker where it is wet, snow lying on its ledges
                half strata = 0.5 + 0.5 * sin((P.y + n2.r * 380.0 + n1.a * 50.0) * 0.05);
                half3 rockCol = lerp(half3(0.16, 0.14, 0.14), half3(0.47, 0.44, 0.44), saturate(n1.a * 0.55 + n2.a * 0.35 + strata * 0.22 - 0.05) * smoothstep(0.25, 0.7, Nd.y + 0.15));
                half ledge = smoothstep(0.5, 0.74, n3.a * 0.4 + n1.r * 0.6) * smoothstep(0.3, 0.62, N.y) * 0.5;
                half3 snowCol = lerp(half3(0.80, 0.86, 0.97), half3(0.95, 0.96, 0.985), saturate(hollow * 1.15));   // hollows a little blue
                // glacier ice in the beds: bluer, and crevassed across the flow where it steepens (lines along the
                // contours, wandering; faded out where they would be finer than a pixel)
                half ice = smoothstep(0.02, 0.2, bed) * (1.0 - smoothstep(-0.02, 0.05, below));
                snowCol = lerp(snowCol, half3(0.74, 0.86, 0.98), ice * 0.5);
                float cy = (P.y + (n1.r - 0.5) * 300.0 + (n3.r - 0.5) * 50.0) / 38.0;
                half crev = smoothstep(0.1, 0.03, abs(frac(cy) - 0.5)) * saturate(1.0 - fwidth(cy) * 2.2);
                crev *= smoothstep(0.05, 0.22, bed + (n2.a - 0.5) * 0.2) * smoothstep(0.97, 0.86, N0.y) * smoothstep(0.4, 0.6, n1.a + n3.a * 0.3);
                snowCol = lerp(snowCol, half3(0.34, 0.48, 0.66), crev * 0.6);
                rockCol = lerp(rockCol, snowCol * 0.9, ledge);
                // forest: dark and close-grained, snow on its crowns toward the tree line
                half3 forestCol = lerp(half3(0.010, 0.020, 0.016), half3(0.024, 0.040, 0.025), saturate(n3.r * 0.3 + n1.a * 0.4 + n2.a * 0.3));
                forestCol = lerp(forestCol, half3(0.5, 0.56, 0.64), (0.03 + 0.6 * smoothstep(0.12, 0.0, below)) * smoothstep(0.4, 0.85, n3.a * 0.6 + n1.r * 0.4));
                half3 albedo = lerp(snowCol, rockCol, rock);
                albedo = lerp(albedo, forestCol, forest);

                Light sun = GetMainLight();
                half ndl = dot(N, sun.direction);
                // snow scatters: a soft wrap; trees and rock take it straight
                half snowy = (1.0 - rock) * (1.0 - forest);
                half lit = lerp(saturate(ndl), saturate((ndl + 0.22) / 1.22), snowy);
                half shade = lerp(i.color.r, i.color.a, _ShadeBlend);   // (fading from the shadow before to the new one: FarMountain.Shade)
                half3 ambient = SampleSH(N) * lerp(0.72, 1.0, saturate(hollow * 1.2));
                half3 c = albedo * (sun.color * (lit * shade) + ambient);
                // the low sun skimming the snow: a little sheen toward the light
                float3 ray = P - _WorldSpaceCameraPos;
                float dist = length(ray);
                float3 V = -ray / max(dist, 1.0);
                half sheen = pow(saturate(dot(N, normalize(sun.direction + V))), 24.0) * snowy * shade;
                c += sun.color * sheen * 0.1;

                // lights down in the valleys at night: clustered, warm, tiny
                half lamp = step(0.9, n3.a) * step(0.62, n2.r) * step(0.55, n1.a) * smoothstep(0.13, 0.02, h01) * step(0.82, N0.y);
                c += half3(1.0, 0.7, 0.34) * lamp * _Lights;

                // haze: thin near, closing over the far country (so it melts into the bottom of the sky), and mist
                // lying in the valleys: thick at the floor, gone by _MistH up
                float dy = ray.y / max(dist, 1.0);
                float camH = (_WorldSpaceCameraPos.y - _FloorY) / _MistH;
                float k = dy * dist / _MistH;
                float through = abs(k) < 0.001 ? exp(-camH) : exp(-camH) * (1.0 - exp(-k)) / k;
                float far = dist * _Haze;
                float fog = 1.0 - exp(-(far * far + dist * _Mist * through));
                c = lerp(c, unity_FogColor.rgb, saturate(fog));
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
