// The snow thrown by the board, the hand and the pole (RiderBodySpray.cs): soft round puffs, never a hard-edged
// quad (a smooth falloff to nothing at the rim, sharper for fine crystals, softer for powder and clumps: _Core),
// alpha blended, nothing opaque.
// Lit per particle at its centre, as snow in the air is lit:
//  - the sky's light (the ambient probe, from above) and the sun's, thrown forward: a puff between you and the sun
//    glows (backlit), one with the sun behind you is plain white;
//  - the floodlights (NightLights: the lamps nearest the camera, as the falling snow is lit), the same way;
//  - fine crystals catch the light now and then as they turn (a twinkle per particle, brightest backlit).
// So the time of day, the night and the floodlights light it without anyone tinting the material. Fogged.
// Vertex streams (RiderBodySpray.Streams): Position, Color, UV (uv0.xy), Center (uv0.zw, uv1.x), StableRandom.x (uv1.y)
Shader "Summit/SnowSpray"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.86, 0.88, 0.92, 1)
        _Core ("Falloff (1 soft .. 3 crisp)", Float) = 1.5
        _Sparkle ("Sparkle", Float) = 0.6
        _Gain ("Floodlight gain", Float) = 1.1
        _Grain ("Grain (0 smooth .. 1 broken)", Float) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Spray"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Core, _Sparkle, _Gain, _Grain;
            CBUFFER_END
            int _NLCount;
            float4 _NLPos[8], _NLDir[8], _NLCone[8];
            float4 _NLColor;
            float _NLGain;

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float4 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float3 uv : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float3 centre = TransformObjectToWorld(float3(i.uv0.zw, i.uv1.x));
                float rnd = i.uv1.y;
                float3 view = normalize(centre - GetCameraPositionWS());   // from the eye through the puff

                // the sky's light from above, and the sun's thrown forward (a backlit puff glows)
                half3 sky = SampleSH(half3(0, 1, 0));
                Light sun = GetMainLight();
                float fwd = saturate(dot(view, sun.direction));
                float phase = 0.55 + 0.45 * fwd + 2.2 * pow(fwd, 8.0);
                half3 lit = sky + sun.color * (half)phase;

                // the floodlights near the camera, the same way
                float lamps = 0.0;
                for (int k = 0; k < _NLCount; k++)
                {
                    float3 d = centre - _NLPos[k].xyz;
                    float d2 = max(dot(d, d), 0.25);
                    float3 ray = d * rsqrt(d2);
                    float f = d2 * _NLDir[k].w;
                    float sm = saturate(1.0 - f * f); sm *= sm;
                    float ang = saturate(dot(ray, _NLDir[k].xyz) * _NLCone[k].x + _NLCone[k].y); ang *= ang;
                    float lf = saturate(dot(view, -ray));
                    lamps += _NLPos[k].w * sm * ang * (0.45 + 0.55 * lf + 2.4 * pow(lf, 6.0)) / d2;
                }
                lit += (half3)(_NLColor.rgb * min(lamps * _Gain * max(_NLGain, 1.0), 6.0));

                // a crystal turning catches the light now and then, most of all against the light
                float tw = pow(0.5 + 0.5 * sin(_Time.y * (5.0 + 9.0 * rnd) + rnd * 71.0), 12.0);
                float glint = _Sparkle * tw * (0.3 + 1.7 * pow(fwd, 3.0));
                lit *= 1.0 + glint;

                o.positionCS = TransformWorldToHClip(p);
                o.uv = float3(i.uv0.xy, rnd);
                half4 c = i.color * _BaseColor;
                c.rgb *= lit;
                o.color = c;
                o.fog = (half)ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float noise(float2 p)
            {
                float2 c = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(c), hash(c + float2(1, 0)), f.x), lerp(hash(c + float2(0, 1)), hash(c + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // a soft gaussian puff (no rim at all), broken up into grains: each particle its own pattern
                float2 d = i.uv.xy * 2.0 - 1.0;
                float k = 3.0 * _Core;
                half a = (half)saturate((exp(-dot(d, d) * k) - exp(-k)) / (1.0 - exp(-k)));
                half grain = (half)noise(i.uv.xy * 3.3 + i.uv.z * 37.0) * 0.6h + (half)noise(i.uv.xy * 7.1 + i.uv.z * 11.0) * 0.4h;
                a *= lerp(1.0h, saturate(grain * 1.8h - 0.25h), (half)_Grain);
                half4 c = i.color;
                c.a *= a;
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }
    }
}
