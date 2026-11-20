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
            half _BackdropHaze;               // 0 in the game; the start page's far view sees the range through haze
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
                // (from the helicopter the range stands beyond tens of kilometres of air: more of it toward its foot)
                c.rgb = lerp(graded, unity_FogColor.rgb, saturate(_BackdropHaze * (1.0 + 1.3 * smoothstep(0.93, 0.8, i.uv.y))));
                half sky = SAMPLE_TEXTURE2D(_SkyMask, sampler_SkyMask, i.uv).r;
                c.a *= 1 - sky * _BackdropKey;
                c.a *= 1 - saturate(sky * 4.0) * step(0.001, _BackdropHaze);   // (hazed, any of the photo's own sky left in would show as a veil)
                return c;
            }
            ENDHLSL
        }
    }
}
