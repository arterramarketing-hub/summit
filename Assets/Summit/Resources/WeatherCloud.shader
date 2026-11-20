// Cloud for the weather (WeatherFX.cs): the deck of a sea of cloud filling the valleys below the run, and the cap and
// plume on the start page's summit. Unlit, see-through, one or two reads of the weather's cloud noise. Its own thin
// haze rather than the scene's fog, so a sea of cloud a couple of kilometres off still shows.
// _WorldUV 1: the noise is laid across the world (the deck), its vertex alpha thinning it to nothing at the shore and
// far out; 0: the mesh's own uv (a billboard), faded round its edge.
Shader "Summit/WeatherCloud"
{
    Properties
    {
        [NoScaleOffset] _CloudTex ("Cloud noise (r: masses, g: detail)", 2D) = "grey" {}
        _Color ("Lit colour (linear), alpha", Color) = (1, 1, 1, 1)
        _Shade ("Colour in its own shade (linear)", Color) = (0.7, 0.74, 0.82, 1)
        _WorldUV ("Noise across the world (1) or the mesh's uv (0)", Float) = 1
        _UVScale ("World metres to noise", Float) = 0.0015
        _Scroll ("Drift: xy the masses, zw the detail", Vector) = (0, 0, 0, 0)
        _Haze ("Its own haze: 1 / the distance it is two thirds gone", Float) = 0.0004
        _Fade ("Faded out (0) .. in (1)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-40" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Shade, _Scroll;
                float _WorldUV, _UVScale, _Haze, _Fade;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 ws : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.uv = i.uv; o.color = i.color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = _WorldUV > 0.5 ? i.ws.xz * _UVScale + _Scroll.xy : i.uv * 0.55 + _Scroll.xy;
                half n = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv).r * 0.7
                       + SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv * 3.3 + _Scroll.zw).g * 0.3;
                half a;
                if (_WorldUV > 0.5) a = saturate((n - (1.0 - i.color.a) * 0.8) * 3.0) * i.color.a;
                else
                {
                    half r = length(i.uv * 2.0 - 1.0);
                    a = saturate((n * 0.9 + 0.35) * smoothstep(1.0, 0.45, r + (n - 0.5) * 0.4)) * i.color.a;
                }
                // tops in the light, hollows in their own shade
                half3 c = lerp(_Shade.rgb, _Color.rgb, saturate(n * 1.4 - 0.15));
                float d = length(i.ws - _WorldSpaceCameraPos) * _Haze;
                c = lerp(c, unity_FogColor.rgb, 1.0 - exp(-d * d));
                return half4(c, a * _Color.a * _Fade);
            }
            ENDHLSL
        }
    }
}
