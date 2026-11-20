// The start gate's board (StartGate.cs): stained planks with MT. VERTIGO routed into them and painted, as a ski
// area's sign is made. SignArt.cs packs the lettering: r how much of the texel is cut and painted, g snow lying on
// the cuts' lower ledges, ba the slope of the cut. The wood is weathered: the stain faded unevenly and darker where
// water runs down from the top edge, the paint worn thin here and there to the timber. The cuts' walls take the light
// (the top wall in shade, the lower one lit) and, like the run's other signs (SignLit.shader), the paint sends a
// little of the rider's headlamp back. Lit by whatever reaches it: the sun or moon with its shadows, the sky, the
// gate's lamps, fog. In Resources so the phone build keeps it.
Shader "Summit/GateBoard"
{
    Properties
    {
        _MainTex ("Lettering (SignArt)", 2D) = "black" {}
        _Wood ("Timber", Color) = (0.30, 0.19, 0.11, 1)
        _WoodDark ("Timber grain", Color) = (0.13, 0.08, 0.05, 1)
        _Paint ("Paint", Color) = (0.93, 0.86, 0.68, 1)
        _SnowCol ("Snow", Color) = (0.95, 0.97, 1, 1)
        _Size ("The board, metres (xy)", Vector) = (7, 1.75, 0, 0)
        _Retro ("Retroreflection", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Wood, _WoodDark, _Paint, _SnowCol;
            float4 _Size;
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _LampPos, _LampDir; half4 _LampColor; float4 _LampCone;   // the headlamp (DayNight), as SignLit

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; float3 rightWS : TEXCOORD3; float3 upWS : TEXCOORD4; half fog : TEXCOORD5;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.rightWS = TransformObjectToWorldDir(float3(1, 0, 0));   // the picture's own across and up, for the cuts' walls
                o.upWS = TransformObjectToWorldDir(float3(0, 1, 0));
                o.uv = i.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }

            // every light on a surface facing n
            half3 Light3(float3 pos, float3 n)
            {
                half3 d = SampleSH(n);
                Light sun = GetMainLight(TransformWorldToShadowCoord(pos));
                d += sun.color * (sun.distanceAttenuation * sun.shadowAttenuation * saturate(dot(n, sun.direction)));
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; k++)
                {
                    Light l = GetAdditionalLight(k, pos);
                    d += l.color * (l.distanceAttenuation * l.shadowAttenuation * saturate(dot(n, l.direction)));
                }
                #endif
                return d;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float cut = t.r, sn = t.g;
                float3 N = normalize(i.normalWS), R = normalize(i.rightWS), U = normalize(i.upWS);
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);

                // the timber: three planks across, grain along them, knots, a joint between each
                float2 m = uv * _Size.xy;
                float rows = m.y / (_Size.y / 3.0);
                float pf = frac(rows), row = floor(rows);
                float joint = smoothstep(0.0, 0.025, pf) * smoothstep(1.0, 0.975, pf);
                float wob = vnoise(float2(m.x * 0.6 + row * 7.1, row * 3.3)) * 2.0;
                float grain = vnoise(float2(m.x * 1.1 + row * 13.7, (pf + wob) * 22.0)) * 0.65 + vnoise(float2(m.x * 6.0, pf * 70.0 + row)) * 0.35;
                float knot = smoothstep(0.83, 0.95, vnoise(m * float2(1.6, 4.0) + row * 5.0));
                half3 wood = lerp(_WoodDark.rgb, _Wood.rgb, saturate(grain * 0.9 + 0.15 - knot * 0.6));
                // weathering: the stain faded unevenly (paler where the sun gets it), darker streaks where meltwater
                // runs down from the top edge, the plank ends greyed
                float fade = vnoise(m * float2(0.7, 1.4) + 3.0);
                wood = lerp(wood, wood * half3(1.25, 1.18, 1.1) + 0.03, smoothstep(0.45, 0.9, fade) * 0.55);
                float streak = vnoise(float2(m.x * 9.0, 0.3)) * smoothstep(0.0, 0.9, m.y / _Size.y);
                wood *= 1.0 - 0.28 * smoothstep(0.55, 0.85, streak);
                float edge = min(min(m.x, _Size.x - m.x), min(m.y, _Size.y - m.y));
                wood = lerp(wood, half3(0.33, 0.31, 0.29), (1.0 - smoothstep(0.0, 0.1, edge)) * 0.4);
                wood *= 0.5 + 0.5 * joint;

                // the paint in the cuts, worn thin here and there to the wood
                float wear = vnoise(m * 14.0) * 0.6 + vnoise(m * 45.0) * 0.4;
                float paint = cut * (1.0 - 0.75 * smoothstep(0.66, 0.86, wear));
                half3 paintC = _Paint.rgb * (0.92 + 0.12 * vnoise(m * 30.0));

                // the cuts' walls: the slope out of SignArt, turned into a normal (the letters lie below the face)
                float2 g = (t.ba - 0.5) * 2.0;
                float3 n = normalize(N * 1.0 + (g.x * R + g.y * U) * 1.6);
                half3 light = Light3(i.positionWS, n);
                half3 col = lerp(wood, paintC, paint) * light;
                // the floor of the cut a little shaded (it's below the face, the edges overhang it)
                col *= 1.0 - 0.18 * cut * (1.0 - paint * 0.5);

                // snow in the cuts' lower ledges, broken up a little
                float snowK = smoothstep(0.25, 0.7, sn * (0.75 + 0.5 * vnoise(m * 60.0)));
                col = lerp(col, _SnowCol.rgb * Light3(i.positionWS, normalize(N + U * 0.8)), snowK);

                // the paint sends some of the rider's headlamp back (sign paint)
                if (_LampPos.w > 0.5)
                {
                    float3 toLamp = _LampPos.xyz - i.positionWS;
                    float d = length(toLamp); toLamp /= max(d, 1e-3);
                    half cone = smoothstep(_LampCone.x, _LampCone.y, dot(-toLamp, _LampDir.xyz));
                    half reach = saturate(1.0 - d / _LampCone.z); reach *= reach;
                    half facing = saturate(dot(N, toLamp));
                    half beside = smoothstep(0.85, 0.98, dot(toLamp, V));
                    col += paintC * paint * _LampColor.rgb * (cone * reach * sqrt(facing) * beside * _Retro);
                }
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
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
