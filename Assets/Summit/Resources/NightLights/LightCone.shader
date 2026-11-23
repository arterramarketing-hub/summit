// A floodlight's beam seen in the air (NightLights.Cone): an open cone out of the lamp, drawn additively from both
// sides. Its corners carry the light the air there scatters (color.a: thinning with distance, nothing below the snow,
// so it fades into the ground). Per pixel: a soft edge (the cone's sides seen edge-on hold little air: |N.V|), the
// light thrown forward by falling snow (brightest looking back into the beam), and a drifting grain of snow falling
// through it. Faint in clear air, a glowing wall in a snowstorm (_NLHaze, from Weather); off by day; faint in the evening (_NLBeam).
//   uv0: the lens (xyz) and the lamp's id      uv1: the aim (xyz)
Shader "Summit/NightLights/LightCone"
{
    Properties
    {
        _Noise ("Snow grain", 2D) = "gray" {}
        _Strength ("Strength", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+40" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Haze"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
                float _Strength;
            CBUFFER_END
            float _NLBeam, _NLHaze;
            float4 _NLColor;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 grain : TEXCOORD2;     // two scrolling coordinates into the grain
                half4 light : TEXCOORD3;      // x: the air's light here, y: forward throw toward the eye, z: fog
                float4 ground : TEXCOORD4;    // the snow under the beam as a plane (normal, offset)
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                float3 cam = GetCameraPositionWS();
                float3 toEye = cam - o.positionWS;
                float dist = length(toEye);
                // the light's way here from the lens, against the way to the eye: snow throws it forward
                float3 ray = normalize(o.positionWS - i.uv0.xyz);
                float fwd = saturate(dot(ray, toEye / dist));
                float phase = 0.35 + 0.65 * fwd + 2.6 * pow(fwd, 6.0);
                // nothing in the last metres before the eye (inside the beam the cone's walls would sit on the lens)
                float nearFade = saturate((dist - 1.5) / 7.0);
                o.light = half4(i.color.a * nearFade, phase, ComputeFogFactor(o.positionCS.z), 0);
                o.ground = i.uv2;
                // falling snow: the grain drifts down and a little across, seen on two planes
                float t = _Time.y;
                o.grain = float4(o.positionWS.x * 0.11 + o.positionWS.z * 0.07, o.positionWS.y * 0.05 + t * 0.21,
                                 o.positionWS.z * 0.13 - o.positionWS.x * 0.05 + 0.37, o.positionWS.y * 0.09 + t * 0.34);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                half edge = abs(dot(normalize(i.normalWS), v));
                edge = edge * edge * (3.0 - 2.0 * edge);
                half g = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, i.grain.xy).r * SAMPLE_TEXTURE2D(_Noise, sampler_Noise, i.grain.zw).r;
                half grain = 0.55 + 1.3 * g;
                // into the snow softly: nothing at the ground, all of it 2.5 m up
                half above = saturate((dot(i.positionWS, i.ground.xyz) - i.ground.w) / 2.5);
                grain *= above * above * (3.0 - 2.0 * above);
                half k = i.light.x * i.light.y * edge * grain * _Strength * _NLHaze * _NLBeam;
                // (fog dims the beam far off; a beam in thick weather stays visible a little further)
                half3 col = _NLColor.rgb * k;
                return half4(MixFogColor(col, half3(0, 0, 0), i.light.z * 0.75), 1);
            }
            ENDHLSL
        }
    }
}
