// The chairlifts' haul rope (SkiLift.Cable). Up close it is what a haul rope is: six fat strands of galvanised
// wire laid in a helix round a core, bright on their crowns and black in the grooves between, with the grease
// and wear of the line passing along it at the chairs' speed. Far off it is a thin dark line across the sky.
//
// The mesh carries the rope's centre line in POSITION and the way out from it in NORMAL, so the rope's thickness
// is set here: its real radius, but never thinner on screen than _MinPixels. Without that a 6 cm rope 300 m away
// is a quarter of a pixel wide and breaks up into a crawling dotted line. A rope fattened to stay visible is
// drawn fainter by as much (alpha: the far material blends; the near one is opaque and writes depth), so a far
// line stays as light as the real thing, a hair against the sky.
//   uv.x  round the rope, 0..1        uv.y  metres along it        uv.z  +1 the side going up, -1 the side coming down
Shader "Summit/LiftRope"
{
    Properties
    {
        _Color ("Steel", Color) = (0.5, 0.51, 0.52, 1)
        _Groove ("Grooves", Color) = (0.07, 0.07, 0.075, 1)
        _FarColor ("Far off", Color) = (0.07, 0.07, 0.08, 1)
        _Radius ("Radius (m)", Float) = 0.03
        _MinPixels ("Least width (pixels)", Float) = 1.5
        _MinAlpha ("Least alpha", Float) = 0.5
        _Strands ("Strands", Float) = 6
        _Lay ("Lay length (m)", Float) = 0.42
        _Bump ("Strand relief", Float) = 0.9
        _Speed ("Line speed (m/s)", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/Farview.hlsl"   // (a span comes in through the world's far band, pixel by pixel)
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _Groove, _FarColor;
                float _Radius, _MinPixels, _MinAlpha, _Strands, _Lay, _Bump, _Speed;
                float _SrcBlend, _DstBlend, _ZWrite;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 uv : TEXCOORD0; float4 tangentOS : TANGENT; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; float3 alongWS : TEXCOORD3; half3 cover : TEXCOORD4;   // x: how much of its drawn width is rope; y: fog
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 centre = TransformObjectToWorld(i.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldDir(i.normalOS));
                // what one pixel spans at this distance, and so the least radius that still covers _MinPixels of them
                float dist = distance(centre, GetCameraPositionWS());
                float pixel = dist * 2.0 / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                float r = max(_Radius, 0.5 * _MinPixels * pixel);
                o.positionWS = centre + n * r;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = n;
                o.alongWS = normalize(TransformObjectToWorldDir(i.tangentOS.xyz));
                o.uv = i.uv;
                o.cover = half3(_Radius / r, ComputeFogFactor(o.positionCS.z), saturate(pixel / (_Radius * 0.35)));
                return o;
            }

            float hash1(float n) { return frac(sin(n * 127.1) * 43758.5453); }
            float noise1(float x) { float i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f); return lerp(hash1(i), hash1(i + 1.0), f); }

            half4 frag(Varyings i) : SV_Target
            {
                FarviewClip(i.positionWS, i.positionCS);
                float3 n = normalize(i.normalWS);
                float3 t = normalize(i.alongWS);
                float3 c = cross(t, n);                                   // round the rope
                // the strands: a helix, _Strands of them round, one turn every _Lay metres
                float phase = _Strands * (i.uv.x + i.uv.y / _Lay);
                float w = fwidth(phase);
                half detail = saturate(1.6 - 2.2 * w);                    // gone before the strands are narrower than two pixels
                float s = frac(phase) * 2.0 - 1.0;                        // -1..1 across a strand
                half groove = smoothstep(0.55, 1.0, abs(s));
                // each strand is itself round: its normal leans out toward its edges, across the helix
                float3 acrossStrand = normalize(c + t * (6.2832 * _Radius / _Lay));
                float3 ns = normalize(n + acrossStrand * (s * _Bump * detail));
                // the wires in a strand, faintly, and the grease and wear along the line, moving with it
                half wires = 1.0 - 0.16 * detail * (0.5 + 0.5 * cos(phase * 6.2832 * 5.0)) * saturate(1.6 - 9.0 * w);
                half wear = 0.82 + 0.3 * noise1((i.uv.y + i.uv.z * _Speed * _Time.y) * 0.55 + i.uv.z * 17.0);
                half3 albedo = lerp(_Color.rgb * wires * wear, _Groove.rgb, groove * detail);
                albedo = lerp(albedo, _Color.rgb * 0.62, 1.0 - detail);   // too far for strands: their average
                // light: the sky, the sun or moon, lamps; a soft metal glint along the crowns
                half3 light = SampleSH(ns);
                Light sun = GetMainLight();
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                half nl = saturate(dot(ns, sun.direction));
                half3 spec = sun.color * (pow(saturate(dot(ns, normalize(sun.direction + v))), 24.0) * 0.35 * nl * detail);
                light += sun.color * nl;
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; k++)
                {
                    Light l = GetAdditionalLight(k, i.positionWS);
                    light += l.color * (l.distanceAttenuation * saturate(dot(ns, l.direction)));
                }
                #endif
                half3 col = albedo * light + spec;
                // far off: a dark line, whatever the light
                col = lerp(col, min(col, _FarColor.rgb * (0.4 + light)), i.cover.z);
                col = MixFog(col, i.cover.y);
                return half4(col, max(_MinAlpha, saturate(i.cover.x)));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
