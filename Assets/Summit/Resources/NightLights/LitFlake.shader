// The falling snow at night under the floodlights (NightLights.Flakes): the soft particle the game always drew
// (URP Particles/Unlit, alpha blended: its texture times the particle's colour times _BaseColor, the time of day's
// tint, and fogged), plus the light of the lamps nearest the camera (_NLPos/_NLDir/_NLCone, up to eight), worked out
// per flake at its centre. A flake in a beam lights up white, a little bigger, and twinkles as it turns; snow
// throws light forward, so looking back into a beam it blazes; a flake outside every beam stays as dark as the
// night. With the lamps off (_NLCount 0) it is the plain particle exactly.
// Vertex streams (NightLights.FlakeStreams): Position, Color, UV (uv0.xy), Center (uv0.zw, uv1.x), StableRandom.x (uv1.y)
Shader "Summit/NightLights/LitFlake"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Gain ("Light gain", Float) = 1.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Flake"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Gain;
            CBUFFER_END
            int _NLCount;
            float4 _NLPos[8], _NLDir[8], _NLCone[8];
            float4 _NLColor;
            float _NLOn;

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float4 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float3 centre = TransformObjectToWorld(float3(i.uv0.zw, i.uv1.x));
                float rnd = i.uv1.y;
                float3 toEye = normalize(GetCameraPositionWS() - centre);
                float lit = 0.0;
                for (int k = 0; k < _NLCount; k++)
                {
                    float3 d = centre - _NLPos[k].xyz;
                    float d2 = max(dot(d, d), 0.25);
                    float3 ray = d * rsqrt(d2);
                    float f = d2 * _NLDir[k].w;
                    float sm = saturate(1.0 - f * f); sm *= sm;
                    float ang = saturate(dot(ray, _NLDir[k].xyz) * _NLCone[k].x + _NLCone[k].y); ang *= ang;
                    // forward throw: a flake between you and the lamp glows hardest
                    float fwd = saturate(dot(ray, toEye));
                    float phase = 0.45 + 0.55 * fwd + 2.4 * pow(fwd, 6.0);
                    lit += _NLPos[k].w * sm * ang * phase / d2;
                }
                // a crystal turning catches the light now and then
                float tw = 0.55 + 1.6 * pow(0.5 + 0.5 * sin(_Time.y * (3.0 + 7.0 * rnd) + rnd * 61.0), 8.0);
                lit = min(lit * tw * _Gain, 7.0);
                // lit flakes read a little bigger (their glow)
                p = centre + (p - centre) * (1.0 + 0.45 * saturate(lit * 0.6));
                o.positionCS = TransformWorldToHClip(p);
                o.uv = TRANSFORM_TEX(i.uv0.xy, _BaseMap);
                half4 c = i.color * _BaseColor;
                c.rgb += (half3)(_NLColor.rgb * lit);
                o.color = c;
                o.fog = (half)ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half4 c = t * i.color;
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }
    }
}
