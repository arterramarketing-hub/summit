// Summit's sky through the day (DayNight.cs): two photographed skies (Resources/Sky/Sky<Phase>.jpg, made by
// Tools/MakeSkies.swift from Poly Haven HDRIs and imported as cubemaps, so there is no seam round the back
// and no pinch overhead) blended as one phase turns into the next, with an exposure and a tint.
// The skies are stored squeezed, y = x / (1 + x), so a value above white (the sun, bright cloud edges) fits in
// 8 bits; this undoes it. Below the horizon it melts into the fog's colour, as the slope's distance does.
// The weather (Weather.cs) lays its own sky over the photo: a thin veil of cirrus with a 22-degree halo and sundogs
// round the sun, and a deck of overcast that can sweep in from one side of the sky (a front). All of it is off (and
// costs one branch) while those values are zero.
Shader "Summit/SkyCycle"
{
    Properties
    {
        [NoScaleOffset] _CubeA ("Sky A", Cube) = "grey" {}
        [NoScaleOffset] _CubeB ("Sky B", Cube) = "grey" {}
        _Blend ("A to B", Range(0, 1)) = 0
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Float) = 1
        _Rotation ("Turned round the vertical, degrees (the start page's time-lapse)", Float) = 0
        // ---- the weather's (Weather.cs)
        _Overcast ("Overcast: how much of the sky the cloud deck covers", Range(0, 1)) = 0
        _OvercastZenith ("Overcast colour overhead (linear)", Color) = (0.6, 0.62, 0.66, 1)
        _OvercastHorizon ("Overcast colour at the horizon (linear)", Color) = (0.8, 0.82, 0.86, 1)
        [NoScaleOffset] _CloudTex ("Cloud noise (r: masses, g: detail)", 2D) = "grey" {}
        _CloudAmp ("How much the cloud masses show light and dark", Range(0, 2)) = 0.5
        _CloudScroll ("Cloud drift: xy offset, z scale", Vector) = (0, 0, 0.12, 0)
        _Front ("The way a front comes from (xyz), and how far it sweeps (w: 0 the whole sky at once)", Vector) = (0, 0, 1, 0)
        _Cirrus ("A thin veil of high cloud", Range(0, 1)) = 0
        _Halo ("The 22-degree halo and sundogs round the sun", Range(0, 1)) = 0
        _SunW ("Toward the sun or moon (world)", Vector) = (0, 0.5, 0.8, 0)
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
            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            CBUFFER_START(UnityPerMaterial)
                float _Blend, _Exposure, _Rotation;
                float4 _Tint;
                float _Overcast, _CloudAmp, _Cirrus, _Halo;
                float4 _OvercastZenith, _OvercastHorizon, _CloudScroll, _Front, _SunW;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; float3 world : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                // (turned by _Rotation: what was at direction s shows at s turned; so look up the view turned back)
                float r = -_Rotation * 0.0174533, cs = cos(r), sn = sin(r);
                float3 d = i.positionOS.xyz;
                o.dir = float3(d.x * cs + d.z * sn, d.y, -d.x * sn + d.z * cs);
                o.world = d;
                return o;
            }
            half3 Unsqueeze(half3 y) { y = min(y, 0.985); return y / (1.0 - y); }
            half Lum(half3 c) { return dot(c, half3(0.3, 0.59, 0.11)); }
            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 a = Unsqueeze(SAMPLE_TEXTURECUBE(_CubeA, sampler_CubeA, d).rgb);
                half3 c = a;
                if (_Blend > 0.001) c = lerp(a, Unsqueeze(SAMPLE_TEXTURECUBE(_CubeB, sampler_CubeB, d).rgb), _Blend);
                c *= _Tint.rgb * _Exposure;

                float3 w = normalize(i.world);
                if (_Cirrus + _Halo > 0.001)
                {
                    // cirrostratus: the blue goes milky, most toward the horizon
                    half cir = _Cirrus * saturate(1.15 - w.y * 0.8);
                    c = lerp(c, Lum(c).xxx * 1.08 + c * 0.12, cir * 0.45);
                    // the 22-degree halo: a thin ring, reddish on its inside edge, and two sundogs at the sun's height
                    float cosA = dot(w, normalize(_SunW.xyz));
                    float ang = acos(clamp(cosA, -1.0, 1.0));
                    float x = (ang - 0.384) / 0.016;
                    half ring = exp(-x * x) * 0.55 + exp(-(x + 1.3) * (x + 1.3)) * 0.18;
                    half3 rc = half3(ring * 1.15, ring, ring * 0.92) - half3(0.0, 0.05, 0.09) * exp(-(x + 1.6) * (x + 1.6));
                    // sundogs: the sun turned 22 degrees either way round the vertical (a little more as it climbs)
                    float3 s = normalize(_SunW.xyz);
                    float el = asin(clamp(s.y, -1.0, 1.0));
                    float dAz = 0.384 / max(cos(el), 0.3);
                    float az = atan2(s.x, s.z);
                    float3 sd1 = float3(cos(el) * sin(az + dAz), s.y, cos(el) * cos(az + dAz));
                    float3 sd2 = float3(cos(el) * sin(az - dAz), s.y, cos(el) * cos(az - dAz));
                    float g1 = saturate(1.0 - dot(w, sd1)), g2 = saturate(1.0 - dot(w, sd2));
                    half dogs = exp(-g1 * 4000.0) + exp(-g2 * 4000.0);
                    half3 dc = dogs * half3(1.3, 1.05, 0.8);
                    half sunUp = saturate(s.y * 8.0 + 0.5) * _Halo;
                    // (the sky just inside the ring is darker than outside it: that step is what the eye sees, more
                    // than the ring's own light, which a bright sky and the tonemapper would flatten)
                    half inside = smoothstep(0.2, 0.36, ang) * (1.0 - smoothstep(0.37, 0.39, ang));
                    c *= 1.0 - 0.25 * inside * sunUp;
                    c *= 1.0 + (rc + dc * 1.5) * 1.15 * sunUp;
                }
                if (_Overcast > 0.001)
                {
                    // the deck: a cloud layer overhead seen in perspective, drifting with the wind
                    float2 uv = w.xz / (abs(w.y) + 0.14) * _CloudScroll.z + _CloudScroll.xy;
                    half n = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv).r * 0.7
                           + SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv * 3.1 + _CloudScroll.yx * 0.7).g * 0.3;
                    // a front fills the sky from the side it comes from (0 there .. 1 across the sky), its edge ragged
                    half lag = 0.5 - 0.5 * dot(w, normalize(_Front.xyz));
                    half edge = _Overcast * (1.0 + _Front.w) - lag * _Front.w + (n - 0.5) * 0.35 * _Front.w;
                    half cover = smoothstep(0.0, 1.0, saturate(edge * (1.0 + 2.0 * _Front.w)));
                    half3 oc = lerp(_OvercastHorizon.rgb, _OvercastZenith.rgb, saturate(w.y * 1.8));
                    // the masses: lighter and darker, fading out toward the horizon where the layer is seen edge-on
                    oc *= 1.0 + _CloudAmp * (n - 0.5) * 2.0 * saturate(w.y * 3.0 + 0.15);
                    // the sun or moon still shows as a glow where the deck is thin
                    half glow = pow(saturate(dot(w, normalize(_SunW.xyz))), 16.0) * saturate(1.3 - n) * (1.0 - _Overcast * 0.75);
                    oc += Lum(c) * glow * 0.6;
                    c = lerp(c, oc, cover);
                }
                // below the horizon: the fog's colour, as the slope fades to it in the distance
                float e = asin(clamp(d.y, -1.0, 1.0));
                c = lerp(c, unity_FogColor.rgb, smoothstep(0.03, -0.01, e) * 0.95);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
