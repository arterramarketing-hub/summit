// The start gate's board (StartGate.cs): MT. VERTIGO in the title's own lettering (TitleArt.BuildLine packs it: r the
// distance to the letters' edge, g snow, ba the bevel's slope) on a board of dark planks with a routed border.
// The letters are ice, raised off the wood: graded pale at the top to deep blue at the foot, their bevel lit by
// whatever light reaches them (the sun or moon with its shadows, the sky, the gate's lamps, fog), snow loaded on
// their tops, a shadow cut under them into the wood, and, like the run's other signs (SignLit.shader), painted to
// send the rider's headlamp back. In Resources so the phone build keeps it.
Shader "Summit/GateBoard"
{
    Properties
    {
        _MainTex ("Lettering (TitleArt.BuildLine)", 2D) = "black" {}
        _Wood ("Timber", Color) = (0.36, 0.23, 0.14, 1)
        _WoodDark ("Timber grain", Color) = (0.16, 0.095, 0.055, 1)
        _Top ("Ice at the top", Color) = (0.70, 0.84, 0.98, 1)
        _Bottom ("Ice at the foot", Color) = (0.22, 0.38, 0.66, 1)
        _SnowCol ("Snow", Color) = (0.97, 0.985, 1, 1)
        _Word ("The letters' foot and cap height (v)", Vector) = (0.27, 0.73, 0, 0)
        _Size ("The board, metres (xy)", Vector) = (8, 2, 0, 0)
        _Spread ("Distance held in r, texels", Float) = 20
        _Retro ("Retroreflection", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Wood, _WoodDark, _Top, _Bottom, _SnowCol;
            float4 _Word, _Size;
            float _Spread;
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
                // the picture's own across and up, for the letters' bevel
                o.rightWS = TransformObjectToWorldDir(float3(1, 0, 0));
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
            float Dist(float4 t) { return (0.5 - t.r) * 2 * _Spread; }   // texels from the letters' edge, + outside

            // every light on a surface facing n: diffuse into d, a glint off ice into s (gloss: its sharpness)
            void Light3(float3 pos, float3 n, float3 v, float gloss, out half3 d, out half3 s)
            {
                d = SampleSH(n); s = 0;
                Light sun = GetMainLight(TransformWorldToShadowCoord(pos));
                half a = sun.distanceAttenuation * sun.shadowAttenuation;
                d += sun.color * (a * saturate(dot(n, sun.direction)));
                s += sun.color * (a * pow(saturate(dot(n, normalize(sun.direction + v))), gloss));
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; k++)
                {
                    Light l = GetAdditionalLight(k, pos);
                    half la = l.distanceAttenuation * l.shadowAttenuation;
                    d += l.color * (la * saturate(dot(n, l.direction)));
                    s += l.color * (la * pow(saturate(dot(n, normalize(l.direction + v))), gloss));
                }
                #endif
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float sd = Dist(t);
                float aa = max(fwidth(sd), 1e-4) * 0.7;
                float fill = saturate(0.5 - sd / aa);
                float3 N = normalize(i.normalWS), R = normalize(i.rightWS), U = normalize(i.upWS);
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);

                // the timber: three planks across, grain running along them, a routed line round the edge
                float2 m = uv * _Size.xy;
                float rows = m.y / (_Size.y / 3.0);
                float pf = frac(rows), row = floor(rows);
                float joint = smoothstep(0.0, 0.03, pf) * smoothstep(1.0, 0.97, pf);
                float wob = vnoise(float2(m.x * 0.6 + row * 7.1, row * 3.3)) * 2.0;
                float grain = vnoise(float2(m.x * 1.1 + row * 13.7, (pf + wob) * 22.0)) * 0.65 + vnoise(float2(m.x * 6.0, pf * 70.0 + row)) * 0.35;
                float knot = smoothstep(0.82, 0.95, vnoise(m * float2(1.6, 4.0) + row * 5.0));
                half3 wood = lerp(_WoodDark.rgb, _Wood.rgb, saturate(grain * 0.9 + 0.2 - knot * 0.6));
                float edge = min(min(m.x, _Size.x - m.x), min(m.y, _Size.y - m.y));
                float routed = 1.0 - smoothstep(0.012, 0.03, abs(edge - 0.13));
                wood *= (0.5 + 0.5 * joint) * (1.0 - 0.55 * routed) * (1.0 + 0.25 * (1.0 - smoothstep(0.0, 0.035, edge)));
                // the letters stand off the board: a shadow under them, below and a little to the right
                float sds = Dist(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-0.0025, 0.022)));
                wood *= 1.0 - 0.6 * saturate(0.5 - (sds - 1.0) / 7.0);

                // the ice: graded down the letters, streaked and frosted
                float wv = saturate((uv.y - _Word.x) / (_Word.y - _Word.x));
                half3 ice = lerp(_Bottom.rgb, _Top.rgb, smoothstep(0, 1, wv));
                float streak = vnoise(float2(uv.x * 190 + uv.y * 60, uv.y * 9));
                float frost = vnoise(uv * float2(260, 70)) * 0.6 + vnoise(uv * float2(700, 180)) * 0.4;
                ice *= 1 + (streak - 0.5) * 0.25;
                ice = lerp(ice, half3(0.9, 0.95, 1), smoothstep(0.55, 0.85, frost) * 0.3 * (1 - wv * 0.5));

                // the bevel: the letters' faces turned toward the light or away from it
                float2 g = (t.ba - 0.5) * 2;
                float3 nL = normalize(-g.x * R - g.y * U + 0.55 * N);
                half3 dW, sW, dL, sL;
                Light3(i.positionWS, N, V, 8, dW, sW);
                Light3(i.positionWS, nL, V, 48, dL, sL);
                half3 col = lerp(wood * dW + sW * 0.03, ice * dL + sL * 0.6, fill);

                // snow on their tops, soft-lit, a little blue where it thins
                float sn = t.g;
                half3 snowC = _SnowCol.rgb * (dL * 0.85 + SampleSH(N) * 0.25);
                snowC = lerp(snowC * half3(0.82, 0.9, 1.05), snowC, smoothstep(0.2, 0.8, sn));
                col = lerp(col, snowC, smoothstep(0.15, 0.6, sn) * fill);

                // the paint sends the rider's headlamp back, as the run's signs do
                if (_LampPos.w > 0.5)
                {
                    float3 toLamp = _LampPos.xyz - i.positionWS;
                    float d = length(toLamp); toLamp /= max(d, 1e-3);
                    half cone = smoothstep(_LampCone.x, _LampCone.y, dot(-toLamp, _LampDir.xyz));
                    half reach = saturate(1.0 - d / _LampCone.z); reach *= reach;
                    half facing = saturate(dot(N, toLamp));
                    half beside = smoothstep(0.85, 0.98, dot(toLamp, V));
                    col += lerp(ice, _SnowCol.rgb, smoothstep(0.15, 0.6, sn)) * fill * _LampColor.rgb * (cone * reach * sqrt(facing) * beside * _Retro);
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
