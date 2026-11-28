// The start page's jump (StartJump): the edges of the frame go soft, as through a fast lens wide open, and the rider
// in the middle stays sharp. One screen-filling quad, drawn after everything else in the scene, reading URP's copy of
// the opaque picture (StartJump asks the camera for it while the page shows): nothing where the middle is sharp (the
// pixel is discarded), a ten-tap disc where it is soft, its radius growing out to the edges, each pixel's disc turned
// by a noise so the few taps read as a smooth blur. A single pass, no render targets of its own.
// In Resources so the phone build keeps it; no keywords, so nothing to strip.
Shader "Summit/JumpEdgeBlur"
{
    Properties
    {
        _Centre ("Sharp middle (viewport)", Vector) = (0.5, 0.5, 0, 0)
        _Mask ("Soft from, fully soft at (screen heights from the middle)", Vector) = (0.13, 0.52, 0, 0)
        _Radius ("Blur radius at the edge (screen heights)", Float) = 0.02
        _Glow ("A little brighter where soft", Float) = 0.04
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+500" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Centre, _Mask;
                float _Radius, _Glow;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                // the quad's corners are the screen's: straight into clip space, whatever the camera
                o.positionCS = float4(i.positionOS.xy, 0.5, 1.0);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float d = length((uv - _Centre.xy) * float2(aspect, 1.0));
                float m = smoothstep(_Mask.x, _Mask.y, d);
                clip(m - 0.004);
                float r = _Radius * m;
                // interleaved gradient noise: each pixel's disc turned its own way
                float ang = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715)))) * 6.2831853;
                half3 acc = SampleSceneColor(uv);
                const int N = 10;
                [unroll] for (int k = 0; k < N; k++)
                {
                    float rr = sqrt((k + 0.5) / N) * r;
                    float a = k * 2.3999632 + ang;   // the golden angle: the taps spread evenly over the disc
                    float2 o = float2(cos(a) / aspect, sin(a)) * rr;
                    acc += SampleSceneColor(clamp(uv + o, 0.0005, 0.9995));
                }
                half3 c = acc / (N + 1);
                c = lerp(c, half3(1, 1, 1), m * _Glow);
                return half4(c, saturate(m * 1.6));
            }
            ENDHLSL
        }
    }
}
