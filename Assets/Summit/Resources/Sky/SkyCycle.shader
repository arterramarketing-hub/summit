// Summit's sky through the day (DayNight.cs): two latitude-longitude panoramas blended (the phase that is
// ending and the one coming), times a tint and exposure, with the stars and the moon drawn on top at night.
// The panoramas use Tools/MakeSky.swift's layout (azimuth 0 straight down the mountain, +Z; a quarter turn
// is +X), which is how DayNight places each phase's sun, so the glow in the sky and the light agree.
// Below the horizon it melts into the fog's colour, as the slope's distance does, whatever the time.
Shader "Summit/SkyCycle"
{
    Properties
    {
        _TexA ("Panorama A", 2D) = "grey" {}
        _TexB ("Panorama B", 2D) = "grey" {}
        _Blend ("A to B", Range(0, 1)) = 0
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Float) = 1
        _Stars ("Stars", Range(0, 1)) = 0
        _Moon ("Moon", Range(0, 1)) = 0
        _MoonDir ("Moon direction", Vector) = (0.4, 0.25, 0.88, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_TexA); SAMPLER(sampler_TexA);
            TEXTURE2D(_TexB); SAMPLER(sampler_TexB);
            CBUFFER_START(UnityPerMaterial)
                float _Blend, _Exposure, _Stars, _Moon;
                float4 _Tint, _MoonDir;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }
            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float e = asin(clamp(d.y, -1.0, 1.0));
                float2 uv = float2(frac(atan2(d.x, d.z) * (0.5 / PI) + 1.0), 0.5 + e / PI);
                // the panoramas have no mips (a seam down the back otherwise), so the plain sample is fine
                half3 a = SAMPLE_TEXTURE2D_LOD(_TexA, sampler_TexA, uv, 0).rgb;
                half3 b = SAMPLE_TEXTURE2D_LOD(_TexB, sampler_TexB, uv, 0).rgb;
                half3 c = lerp(a, b, _Blend) * _Tint.rgb * _Exposure;
                if (_Stars > 0.001)
                {
                    // a star in some cells of a fine grid on the sky: a pinpoint each, twinkling, gone toward
                    // the horizon's haze and behind the brighter parts of the sky (cloud, moon glow)
                    float3 p = d * 260.0;
                    float3 cell = floor(p);
                    float h = Hash(cell);
                    if (h > 0.972)
                    {
                        float3 centre = cell + 0.3 + 0.4 * float3(Hash(cell + 7.1), Hash(cell + 3.7), Hash(cell + 1.3));
                        float r = length(p - centre);
                        float bright = (h - 0.972) / 0.028;
                        bright = bright * bright * 2.2 + 0.25;
                        float tw = 0.75 + 0.25 * sin(_Time.y * (2.0 + 3.0 * Hash(cell + 2.9)) + h * 90.0);
                        half sky = dot(c, half3(0.3, 0.59, 0.11));
                        half s = smoothstep(0.28, 0.0, r) * bright * tw * smoothstep(0.03, 0.22, d.y) * saturate(1.0 - sky * 5.0) * _Stars;
                        c += s * lerp(half3(0.8, 0.88, 1.0), half3(1.0, 0.92, 0.8), Hash(cell + 5.5));
                    }
                }
                if (_Moon > 0.001)
                {
                    // the moon: a bright disc, its faint grey seas, and a soft halo (bloom adds the rest)
                    float cosA = dot(d, normalize(_MoonDir.xyz));
                    float ang = acos(clamp(cosA, -1.0, 1.0));
                    const float R = 0.022;
                    float disc = smoothstep(R, R * 0.9, ang);
                    float3 side = normalize(cross(_MoonDir.xyz, float3(0, 1, 0)));
                    float2 q = float2(dot(d, side), d.y - _MoonDir.y) / R;
                    half seas = 0.86 + 0.14 * sin(q.x * 3.1 + 1.3) * sin(q.y * 2.6 + 0.4) + 0.05 * sin(q.x * 7.0 - q.y * 5.0);
                    c = lerp(c, half3(1.35, 1.38, 1.45) * seas, disc * _Moon);
                    c += half3(0.18, 0.22, 0.32) * exp(-ang * 14.0) * _Moon * (1.0 - disc);
                }
                // below the horizon: the fog's colour, as the slope fades to it in the distance
                c = lerp(c, unity_FogColor.rgb, smoothstep(0.045, -0.005, e) * 0.92);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
