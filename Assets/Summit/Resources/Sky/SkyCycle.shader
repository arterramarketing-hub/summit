// Summit's sky through the day (DayNight.cs): two photographed skies (Resources/Sky/Sky<Phase>.jpg, made by
// Tools/MakeSkies.swift from Poly Haven HDRIs and imported as cubemaps, so there is no seam round the back
// and no pinch overhead) blended as one phase turns into the next, with an exposure and a tint.
// The skies are stored squeezed, y = x / (1 + x), so a value above white (the sun, bright cloud edges) fits in
// 8 bits; this undoes it. Below the horizon it melts into the fog's colour, as the slope's distance does.
Shader "Summit/SkyCycle"
{
    Properties
    {
        [NoScaleOffset] _CubeA ("Sky A", Cube) = "grey" {}
        [NoScaleOffset] _CubeB ("Sky B", Cube) = "grey" {}
        _Blend ("A to B", Range(0, 1)) = 0
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Float) = 1
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
            TEXTURECUBE(_CubeA); SAMPLER(sampler_CubeA);
            TEXTURECUBE(_CubeB); SAMPLER(sampler_CubeB);
            CBUFFER_START(UnityPerMaterial)
                float _Blend, _Exposure;
                float4 _Tint;
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
            half3 Unsqueeze(half3 y) { y = min(y, 0.985); return y / (1.0 - y); }
            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 a = Unsqueeze(SAMPLE_TEXTURECUBE(_CubeA, sampler_CubeA, d).rgb);
                half3 c = a;
                if (_Blend > 0.001) c = lerp(a, Unsqueeze(SAMPLE_TEXTURECUBE(_CubeB, sampler_CubeB, d).rgb), _Blend);
                c *= _Tint.rgb * _Exposure;
                // below the horizon: the fog's colour, as the slope fades to it in the distance
                float e = asin(clamp(d.y, -1.0, 1.0));
                c = lerp(c, unity_FogColor.rgb, smoothstep(0.03, -0.01, e) * 0.95);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
