// The painted mountain bands that ring the world (see Atmosphere.Parallax). Unlit, see-through where
// there is sky, and never fogged: the paint already carries its own haze.
// The time of day (DayNight.cs) grades it through globals: the bright snow and the dark forest each take
// their own tint (pink alpenglow on the snow while the forest goes blue-dark), the photo's own daytime sky
// is let through to the game's sky (by its mask) when the two would no longer match, and the haze it
// fades into at the bottom becomes the fog's colour, so it still melts into the slope below.
Shader "Summit/Backdrop"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SkyMask ("Sky mask (photo only)", 2D) = "black" {}
        _HazeBottom ("Where the photo ends and its haze begins (v)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_SkyMask); SAMPLER(sampler_SkyMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _HazeBottom;
            CBUFFER_END
            // set every frame by DayNight; all zero (before it runs) means plain daylight
            half3 _BackdropLo, _BackdropHi;   // tints for the dark (forest) and bright (snow, haze) parts
            half _BackdropKey;                // 0 keep the photo's sky, 1 show the game sky through it
            half _BackdropOn;                 // 1 once DayNight has set the tints
            half _BackdropSat;                // colour kept (1 day; less by moonlight, which shows little colour)
            half2 _BackdropGlow;              // the band of height (v) up which the snow comes into the low sun:
                                              // below it the snow is in the shade, as the lower slopes are at dusk
            // set by Weather.cs; all zero (the weather off) changes nothing
            half4 _BackdropVeil;              // x: the whole range in haze; y: how much of it is in cloud above v = z;
                                              // w: how much of it is under a sea of cloud below v = _BackdropSea.x
            half4 _BackdropSea;               // x: the sea's top (v); y: how much its top undulates
            half4 _BackdropCap;               // xy: the summit (uv); z: a cap cloud sitting on it; w: a plume of spindrift off it
            half4 _BackdropCloud;             // rgb: cloud lit by the sun (linear); a: the plume's clock
            TEXTURE2D(_WeatherCloudTex); SAMPLER(sampler_WeatherCloudTex);   // the weather's cloud noise (r masses, g detail)
            float4 _WeatherCloudScroll;
            float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                if (_BackdropOn < 0.5) return c;
                half lum = dot(c.rgb, half3(0.3, 0.59, 0.11));
                // bright snow takes the light's tint (sun, alpenglow, moon), the dark forest the shade's; at dusk only
                // the snow high enough to still see the sun does
                half lit = smoothstep(0.25, 0.8, lum) * smoothstep(_BackdropGlow.x, _BackdropGlow.y, i.uv.y);
                half3 tint = lerp(_BackdropLo, _BackdropHi, lit);
                half3 graded = lerp(lum.xxx, c.rgb, _BackdropSat) * tint;
                // below the photo it fades into the day's haze colour (Tools/MakePhotoBand.swift): put the
                // fog's colour there instead, by the same fade
                half f = saturate((_HazeBottom - i.uv.y) / max(_HazeBottom, 1e-3));
                half haze = 0.95 * smoothstep(0.02, 0.9, f) * step(1e-3, _HazeBottom);
                // (the haze written into the PNG as (0.80, 0.86, 0.94) is sampled linear, sRGB-decoded; graded like
                // the pixel it is in)
                half3 hz = half3(0.604, 0.708, 0.871);
                hz = lerp(dot(hz, half3(0.3, 0.59, 0.11)).xxx, hz, _BackdropSat) * tint;
                graded += haze * (unity_FogColor.rgb - hz);
                c.rgb = graded;
                c.a *= 1 - SAMPLE_TEXTURE2D(_SkyMask, sampler_SkyMask, i.uv).r * _BackdropKey;
                if (dot(_BackdropVeil, 1) + _BackdropCap.z + _BackdropCap.w > 0.001)
                {
                    float2 uv = i.uv;
                    // a sea of cloud filling the valleys: a flat top, gently heaving, sunlit, a little darker deeper in
                    if (_BackdropVeil.w > 0.001)
                    {
                        half top = _BackdropSea.x + _BackdropSea.y * (sin(uv.x * 70.0 + _BackdropCloud.a * 0.05) * 0.5 + Noise2(float2(uv.x * 40.0, 3.1)) - 0.5);
                        // the cloud tops, seen in perspective: the pixel's ray met with the sea's top, _BackdropSea.z
                        // metres below the eye (the band spans azimuth +-1.483 and elevation -0.95..0.21 rad); lit on
                        // their tops, blue shade in the hollows between them
                        float below = max(top - uv.y, 0.0);
                        float el = -0.95 + 1.16 * uv.y, az = (uv.x - 0.5) * 2.966;
                        float dist = min(_BackdropSea.z / max(tan(-el), 0.02), 9000.0);
                        float2 q = float2(sin(az), cos(az)) * dist * 0.0009 + _WeatherCloudScroll.xy * 0.5;
                        half n = SAMPLE_TEXTURE2D(_WeatherCloudTex, sampler_WeatherCloudTex, q).r * 0.65
                               + SAMPLE_TEXTURE2D(_WeatherCloudTex, sampler_WeatherCloudTex, q * 3.1 + 0.37).g * 0.35;
                        half sea = _BackdropVeil.w * smoothstep(top + 0.005, top - 0.012, uv.y + (n - 0.5) * 0.008);
                        half3 lit = _BackdropCloud.rgb, hollow = _BackdropCloud.rgb * half3(0.74, 0.79, 0.9);
                        half3 seaCol = lerp(hollow, lit, smoothstep(0.25, 0.75, n)) * lerp(1.0, 0.88, saturate(below * 4.0));
                        c.rgb = lerp(c.rgb, seaCol, sea);
                    }
                    // the upper mountain gone into the cloud deck, its edge ragged
                    if (_BackdropVeil.y > 0.001)
                    {
                        half rag = Noise2(uv * float2(60.0, 30.0)) * 0.05;
                        c.rgb = lerp(c.rgb, unity_FogColor.rgb, _BackdropVeil.y * smoothstep(_BackdropVeil.z - 0.08, _BackdropVeil.z + 0.03, uv.y + rag));
                    }
                    // on the summit: a smooth cap of cloud, and a plume of snow streaming off it downwind
                    half cloudA = 0.0;
                    float2 p = (uv - _BackdropCap.xy) / float2(0.034, 0.017);
                    if (_BackdropCap.z > 0.001 && dot(p, p) < 4.0)
                    {
                        half lens = length(float2(p.x, (p.y - 0.25) * 1.25)) + (Noise2(uv * 220.0) - 0.5) * 0.3;
                        cloudA = _BackdropCap.z * smoothstep(1.0, 0.55, lens) * 0.92;
                    }
                    float s = (uv.x - _BackdropCap.x) / 0.13;
                    if (_BackdropCap.w > 0.001 && s > 0.0 && s < 1.0)
                    {
                        float y = uv.y - (_BackdropCap.y - 0.006 - s * 0.035);
                        float wdt = 0.004 + 0.03 * s;
                        half n = Noise2(float2(uv.x * 90.0 - _BackdropCloud.a * 0.6, uv.y * 140.0)) * 0.6 + Noise2(float2(uv.x * 30.0 - _BackdropCloud.a * 0.25, uv.y * 50.0)) * 0.6;
                        half plume = exp(-(y * y) / (wdt * wdt)) * smoothstep(0.0, 0.06, s) * pow(1.0 - s, 1.5) * n;
                        cloudA = max(cloudA, saturate(_BackdropCap.w * plume * 1.1));
                    }
                    if (cloudA > 0.001)
                    {
                        half3 cc = _BackdropCloud.rgb * lerp(0.82, 1.05, saturate(p.y * 0.5 + 0.5));
                        c.rgb = lerp(c.rgb, cc, max(cloudA, 1.0 - c.a));
                        c.a = max(c.a, cloudA);
                    }
                    // the whole range in the weather's haze
                    c.rgb = lerp(c.rgb, unity_FogColor.rgb, _BackdropVeil.x);
                }
                return c;
            }
            ENDHLSL
        }
    }
}
