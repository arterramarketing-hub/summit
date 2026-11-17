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
                half3 tint = lerp(_BackdropLo, _BackdropHi, smoothstep(0.25, 0.8, lum));
                half3 graded = c.rgb * tint;
                // below the photo it fades into the day's haze colour (Tools/MakePhotoBand.swift): put the
                // fog's colour there instead, by the same fade
                half f = saturate((_HazeBottom - i.uv.y) / max(_HazeBottom, 1e-3));
                half haze = 0.95 * smoothstep(0.02, 0.9, f) * step(1e-3, _HazeBottom);
                graded += haze * (unity_FogColor.rgb - half3(0.80, 0.86, 0.94) * _BackdropHi);
                c.rgb = graded;
                c.a *= 1 - SAMPLE_TEXTURE2D(_SkyMask, sampler_SkyMask, i.uv).r * _BackdropKey;
                return c;
            }
            ENDHLSL
        }
    }
}
