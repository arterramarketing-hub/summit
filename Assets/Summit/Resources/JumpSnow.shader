// The start page's jump (StartJump): the kicker, the landing and the snow round them, bright and clean as the snow
// in a ski film's still. Unlit and never fogged (the scene keeps its own day whatever the game's time): shaded by the
// slope against the scene's sun, a fine grain, the line the board has carved, a soft shadow under the rider (a blob:
// the scene's lights cast none) and, far off, the snow going into the haze of the sky behind it.
// In Resources so the phone build keeps it; no keywords, so nothing to strip.
Shader "Summit/JumpSnow"
{
    Properties
    {
        _Grain ("Grain", 2D) = "gray" {}
        _Lit ("Lit", Color) = (0.97, 0.975, 0.985, 1)
        _Shade ("Shade", Color) = (0.70, 0.77, 0.88, 1)
        _Haze ("Haze", Color) = (0.90, 0.93, 0.97, 1)
        _SunDir ("Sun (object space)", Vector) = (0.3, 0.8, -0.5, 0)
        _Blob ("Shadow: xyz where, w radius", Vector) = (0, 0, 0, 1)
        _BlobA ("Shadow strength", Float) = 0.3
        _Track ("Track: x from, y to (along x), z half width", Vector) = (-80, 0, 0.12, 0)
        _Cam ("Camera (object space)", Vector) = (0, 0, 0, 0)
        _HazeFrom ("Haze from, to (m)", Vector) = (60, 260, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Grain); SAMPLER(sampler_Grain);
            CBUFFER_START(UnityPerMaterial)
                float4 _Grain_ST;
                half4 _Lit, _Shade, _Haze;
                float4 _SunDir, _Blob, _Track, _Cam, _HazeFrom;
                float _BlobA;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 p : TEXCOORD0; float3 n : TEXCOORD1; half ao : TEXCOORD2; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.p = i.positionOS.xyz;
                o.n = i.normalOS;
                o.ao = i.color.r;   // the dips' occlusion, baked per vertex from the hill's shape (StartJump.HillMesh)
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.n);
                float dist = length(i.p.xz - _Cam.xz);
                float near = 1.0 - smoothstep(25.0, 90.0, dist);          // the surface detail fades with distance
                float groomed = smoothstep(2.6, 5.5, abs(i.p.z));           // the kicker's line is shaped smooth
                // wind ripples and low sastrugi: long crests across the wind, sharp on the lee side, bent by a slow warp
                float2 wind = normalize(float2(0.42, 0.91));
                float warp = SAMPLE_TEXTURE2D(_Grain, sampler_Grain, i.p.xz * 0.006 + 0.11).r;
                float ph = dot(i.p.xz, wind) * 2.2 + warp * 9.0 + sin(dot(i.p.xz, float2(-wind.y, wind.x)) * 0.35) * 1.4;
                float s1 = sin(ph), s2 = sin(ph * 2.7 + 1.3);
                float2 dh = wind * (2.2 * cos(ph) * (0.6 + 0.4 * s1) + 0.35 * 2.7 * cos(ph * 2.7 + 1.3));
                float rip = 0.15 * groomed * near;
                // the fine grain as a normal: wind-packed crystals catching the light
                float2 gu = i.p.xz * 0.9;
                float g0 = SAMPLE_TEXTURE2D(_Grain, sampler_Grain, gu).r;
                float gx = SAMPLE_TEXTURE2D(_Grain, sampler_Grain, gu + float2(0.012, 0)).r;
                float gz = SAMPLE_TEXTURE2D(_Grain, sampler_Grain, gu + float2(0, 0.012)).r;
                float2 dg = float2(gx - g0, gz - g0) * 1.5 * (1.0 - smoothstep(6.0, 30.0, dist));
                n = normalize(n + float3(-(dh.x * rip + dg.x), 0, -(dh.y * rip + dg.y)));
                // the slope against the sun: a soft wrap with some form to it; the shade side a cool blue-grey
                float l = saturate(dot(n, normalize(_SunDir.xyz)) * 0.86 + 0.18);
                half3 c = lerp(_Shade.rgb, _Lit.rgb, l);
                // soft occlusion in the dips and hollows, bluer than the shade
                half ao = lerp(1.0h, i.ao, 0.9h);
                c *= lerp(half3(0.68, 0.77, 0.91), half3(1, 1, 1), ao);
                // the grain: wind-packed snow, finer near, gone into the haze far off
                float g = g0 + SAMPLE_TEXTURE2D(_Grain, sampler_Grain, i.p.xz * 0.043 + 0.37).r * 0.6;
                c *= 1.0 + (g - 0.8) * 0.04;
                // faint old tracks across the slope, filled in by the wind
                float t1 = abs(i.p.z - (-14.0 + 4.0 * sin(i.p.x * 0.09))) , t2 = abs(i.p.z - (11.0 + 5.0 * sin(i.p.x * 0.07 + 2.0)));
                float t3 = abs(i.p.z - (-23.0 + 6.0 * sin(i.p.x * 0.05 + 0.7)));
                float old = max(max(1.0 - smoothstep(0.05, 0.22, t1), 1.0 - smoothstep(0.05, 0.25, t2)), 1.0 - smoothstep(0.06, 0.3, t3));
                c *= 1.0 - 0.05 * old * near;
                // the line the board carved in, up to the lip and on from the landing
                float tr = (step(i.p.x, _Track.x) + step(_Track.y, i.p.x)) * (1.0 - smoothstep(_Track.z * 0.4, _Track.z, abs(i.p.z)));
                c *= 1.0 - 0.09 * saturate(tr);
                // the rider's shadow: softer and paler the higher they are
                float d = length((i.p - _Blob.xyz).xz) / max(_Blob.w, 0.05);
                c *= 1.0 - _BlobA * exp(-d * d * 2.2);
                // far off, into the sky's haze
                float far = smoothstep(_HazeFrom.x, _HazeFrom.y, length(i.p.xz - _Cam.xz));
                c = lerp(c, _Haze.rgb, far);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
