// The far field (FarField.cs): the mountain beyond the streamed world, coarse, lit by URP's own PBR sums as the
// terrain's snow is (so the two match where one hands over to the other), with bare rock and the forest's stands
// laid on from the vertex colours (r rock, g forest cover, b a hollow's shade), then the fog, and past 700 m a
// little more haze (aerial perspective, Farview.cs). Never drawn inside the band's inner edge, where the streamed
// terrain is whole; dissolves into the backdrop photograph towards its far rim.
Shader "Summit/FarField"
{
    Properties
    {
        [MainTexture] _BaseMap ("Snow (its average colour is used)", 2D) = "white" {}
        [MainColor] _BaseColor ("Snow tint", Color) = (1, 1, 1, 1)
        _Smoothness ("Snow smoothness", Range(0, 1)) = 0.32
        _RockColor ("Rock", Color) = (0.24, 0.23, 0.22, 1)
        _ForestColor ("Forest, from afar", Color) = (0.11, 0.15, 0.15, 1)
        _ForestCover ("How much of the ground a stand hides, from afar", Range(0, 1)) = 0.8
        _Rim ("Dissolved into the photo between (m from the camera)", Vector) = (1250, 1460, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/Farview.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness;
                half4 _RockColor;
                half4 _ForestColor;
                half _ForestCover;
                float4 _Rim;
            CBUFFER_END
            // x: the photo's haze (Backdrop.shader); y, z: the far field's extra haze from, to (m); w: how much
            float4 _FarviewHaze;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2; half fog : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // the streamed terrain is whole in here: nothing of this may show through it
                half shown = FarviewShown(i.positionWS);
                if (shown > 0.999h) discard;
                float3 toCam = GetCameraPositionWS() - i.positionWS;
                float dist = length(toCam);
                // and out at the rim, into the photo (the same pattern as the band's)
                half rim = (half)saturate((_Rim.y - dist) / max(_Rim.y - _Rim.x, 1.0));
                FarviewClipShown(rim, i.positionCS);

                // the snow's own colour as the terrain shows it from a long way off: its texture's average
                half3 snow = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, float2(0.5, 0.5), 12).rgb * _BaseColor.rgb;
                snow *= lerp(half3(0.78, 0.85, 0.97), half3(1, 1, 1), saturate(i.color.b * 2.0));   // hollows blue
                half rock = smoothstep(0.35, 0.65, i.color.r);
                half forest = i.color.g * _ForestCover;
                half3 albedo = lerp(snow, _RockColor.rgb, rock);
                albedo = lerp(albedo, _ForestColor.rgb, forest);
                half smooth = lerp(lerp(_Smoothness, 0.12, rock), 0.05, forest);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.normalWS = normalize((float3)i.normalWS);
                d.viewDirectionWS = toCam / max(dist, 1e-3);
                d.shadowCoord = float4(0, 0, 0, 0);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = 0; s.specular = 0; s.smoothness = smooth; s.occlusion = 1;
                s.alpha = 1; s.normalTS = half3(0, 0, 1);
                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fog);
                // aerial perspective, past the streamed world
                half air = (half)smoothstep(_FarviewHaze.y, _FarviewHaze.z, dist) * (half)_FarviewHaze.w;
                c.rgb = lerp(c.rgb, unity_FogColor.rgb, air);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
    }
}
