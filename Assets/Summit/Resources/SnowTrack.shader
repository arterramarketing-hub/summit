// The board's track (SnowTrail.cs): the trench and its walls drawn in the snow's own texture, sampled where the
// terrain samples it (the macro map at x, z / 36 m, the grain at its own tiling), so the walls are the snow round
// them and only the cut itself differs: a compressed, darker, glossier floor in a carve, a shadowed interior in
// powder, a faint polished scrape on ice (the vertex colour multiplies the snow, its alpha is the smoothness).
//
// Lit as URP Lit lights the snow (sun and shadows, sky, the real lights near the rider, fog), plus the floodlights
// (NightLights: the lamps nearest the camera, per vertex, as PisteKit.shader), so a track through a pool catches
// its light. It fades as it ages and as it falls behind (uv0.x: when the row was made on _TrackClock; uv0.y: how
// much of it shows at most) through the far band's own screen pattern, pixel by pixel: nothing pops in or out.
Shader "Summit/SnowTrack"
{
    Properties
    {
        [MainTexture] _BaseMap ("Snow", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _DetailAlbedoMap ("Grain x2", 2D) = "linearGrey" {}
        _TrackHold ("Seconds before it fades", Float) = 8
        _TrackFade ("Seconds it fades over", Float) = 10
        _TrackNear ("Fully drawn within (m)", Float) = 110
        _TrackFar ("Gone by (m)", Float) = 190
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/Farview.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _DetailAlbedoMap_ST;
                half4 _BaseColor;
                float _TrackHold, _TrackFade, _TrackNear, _TrackFar;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DetailAlbedoMap); SAMPLER(sampler_DetailAlbedoMap);
            float _TrackClock;
            int _NLCount;
            float4 _NLPos[8], _NLDir[8], _NLCone[8];
            float4 _NLColor, _NLReal;
            float _NLGain;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half4 lamps : TEXCOORD3;     // rgb: the floodlights here; a: fog
                half life : TEXCOORD4;
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half3 vertexLight : TEXCOORD5;
                #endif
            };

            half3 Floodlit(float3 p, float3 n)
            {
                float lit = 0.0;
                for (int k = 0; k < _NLCount; k++)
                {
                    float3 d = p - _NLPos[k].xyz;
                    float d2 = max(dot(d, d), 0.25);
                    float3 ray = d * rsqrt(d2);
                    float f = d2 * _NLDir[k].w;
                    float sm = saturate(1.0 - f * f); sm *= sm;
                    float ang = saturate(dot(ray, _NLDir[k].xyz) * _NLCone[k].x + _NLCone[k].y); ang *= ang;
                    float id = _NLCone[k].z;
                    float real = (abs(id - _NLReal.x) < 0.5 ? _NLReal.y : 0.0) + (abs(id - _NLReal.z) < 0.5 ? _NLReal.w : 0.0);
                    lit += _NLPos[k].w * (1.0 - saturate(real)) * sm * ang * saturate(dot(n, -ray)) / d2;
                }
                return (half3)(_NLColor.rgb * lit * max(_NLGain, 1.0));
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = (half3)TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                o.lamps = half4(Floodlit(o.positionWS, normalize((float3)o.normalWS)), (half)ComputeFogFactor(o.positionCS.z));
                float age = _TrackClock - i.uv.x;
                float dist = distance(o.positionWS, GetCameraPositionWS());
                o.life = (half)(i.uv.y * saturate(1.0 - (age - _TrackHold) / max(_TrackFade, 1e-3)) * saturate((_TrackFar - dist) / max(_TrackFar - _TrackNear, 1e-3)));
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                o.vertexLight = VertexLighting(o.positionWS, o.normalWS);
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // fading (with age, with distance) and the far band, by the same screen pattern
                FarviewClipWindow(FarviewAnd(half2(0.0h, i.life), half2(0.0h, FarviewShown(i.positionWS))), i.positionCS);
                float2 metres9 = i.positionWS.xz / 9.0;
                half3 snow = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, metres9 * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb;
                half3 grain = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, metres9 * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw).rgb * 2.0h;
                half3 albedo = snow * lerp(half3(1, 1, 1), grain, 0.5h) * _BaseColor.rgb * i.color.rgb;

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = NormalizeNormalPerPixel(i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.lamps.a;
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                d.vertexLighting = i.vertexLight;
                #endif
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.specular = half3(0, 0, 0);
                s.smoothness = i.color.a;
                s.occlusion = 1.0h;
                s.alpha = 1.0h;
                s.normalTS = half3(0, 0, 1);

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb += albedo * i.lamps.rgb;
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return half4(c.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Shaders/Farview.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _DetailAlbedoMap_ST;
                half4 _BaseColor;
                float _TrackHold, _TrackFade, _TrackNear, _TrackFar;
            CBUFFER_END
            float _TrackClock;
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half life : TEXCOORD1; };
            V vert(A i)
            {
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                float age = _TrackClock - i.uv.x;
                float dist = distance(o.positionWS, GetCameraPositionWS());
                o.life = (half)(i.uv.y * saturate(1.0 - (age - _TrackHold) / max(_TrackFade, 1e-3)) * saturate((_TrackFar - dist) / max(_TrackFar - _TrackNear, 1e-3)));
                return o;
            }
            half frag(V i) : SV_TARGET
            {
                FarviewClipWindow(FarviewAnd(half2(0.0h, i.life), half2(0.0h, FarviewShown(i.positionWS))), i.positionCS);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
