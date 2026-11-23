// A floodlight's pool of light on the snow (NightLights.Pool): a mesh lying on the terrain's own triangles a few
// centimetres up, drawn additively after the opaque world. Each corner carries its lamp, so the light is worked out
// per pixel exactly as URP lights a spot (1/d^2 with the range's smooth falloff, the cone's soft edge squared, the
// snow's facing) on the snow's albedo: the same light the real spot gives where NightLights makes it real, which
// is why a pool whose lamp is real (_NLReal: id, weight) leaves its share out. On top, the lit snow sparkles: each
// little cell of it has a crystal facing its own way, and the few that mirror the lamp into the eye flash.
//   uv0: the lens (xyz) and power      uv1: the aim (xyz) and cos(outer half angle)
//   uv2: cos(inner half angle), 1 / range^2, the lamp's id
Shader "Summit/NightLights/LightPool"
{
    Properties
    {
        _Albedo ("Snow albedo", Float) = 0.83
        _Glitter ("Glitter", Float) = 5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+501" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Pool"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -4
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Albedo, _Glitter;
            CBUFFER_END
            float _NLOn, _NLSpark, _NLGain;
            float4 _NLColor, _NLReal;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 lamp : TEXCOORD2;     // lens, power (already less the real light's share)
                float4 aim : TEXCOORD3;      // aim, cos outer
                float4 cone : TEXCOORD4;     // spot scale, offset, 1/range^2, fog
                float glit : TEXCOORD5;      // the lamp's whole power (the sparkle keeps it all: the real light has none)
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                float id = i.uv2.z;
                float real = (abs(id - _NLReal.x) < 0.5 ? _NLReal.y : 0.0) + (abs(id - _NLReal.z) < 0.5 ? _NLReal.w : 0.0);
                o.lamp = float4(i.uv0.xyz, i.uv0.w * _NLOn * max(_NLGain, 1.0));
                o.glit = o.lamp.w;
                o.lamp.w *= 1.0 - saturate(real);
                float a = 1.0 / max(i.uv2.x - i.uv1.w, 0.001);
                o.aim = i.uv1;
                o.cone = float4(a, -i.uv1.w * a, i.uv2.y, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            float hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float3 hash32(float2 p) { float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973)); p3 += dot(p3, p3.yxz + 33.33); return frac((p3.xxy + p3.yzz) * p3.zyx); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 L = i.lamp.xyz - i.positionWS;
                float d2 = max(dot(L, L), 1e-4);
                float3 l = L * rsqrt(d2);
                // URP's spot: 1/d^2, smoothed to nothing at its range; the cone's edge squared
                float f = d2 * i.cone.z;
                float sm = saturate(1.0 - f * f); sm *= sm;
                float ang = saturate(dot(-l, i.aim.xyz) * i.cone.x + i.cone.y); ang *= ang;
                float3 n = normalize(i.normalWS);
                float ndl = saturate(dot(n, l));
                float e = sm * ang / d2;   // the light reaching here, per unit power
                // the faintest edge dropped smoothly to nothing (the mesh stops where it is this faint)
                float diffuse = max(0.0, i.lamp.w * e * ndl * _Albedo - 0.006 * _NLOn);

                // the sparkle: one crystal per cell (finer near, coarser far, so it never shimmers to grey)
                float3 v = GetCameraPositionWS() - i.positionWS;
                float dist = length(v); v /= dist;
                float cell = 0.03 * exp2(floor(log2(max(1.0, dist / 7.0))));
                float2 uvc = i.positionWS.xz / cell;
                float2 c = floor(uvc);
                float3 r = hash32(c) - 0.5;
                float3 crystal = normalize(n + r * 1.7);
                float3 h = normalize(l + v);
                // a round point of light somewhere in its cell, not the whole square
                float2 at = frac(uvc) - (0.3 + 0.4 * r.xz + 0.2);
                float dot2 = saturate(1.0 - dot(at, at) * 22.0);
                float glint = smoothstep(0.9965, 0.9995, dot(crystal, h)) * step(0.35, hash12(c + 17.0)) * dot2 * saturate(1.6 - dist / 60.0);
                float spark = glint * e * i.glit * _NLSpark * _Glitter;
                half3 col = _NLColor.rgb * (diffuse + spark);
                return half4(MixFogColor(col, half3(0, 0, 0), i.cone.w), 1);
            }
            ENDHLSL
        }
    }
}
