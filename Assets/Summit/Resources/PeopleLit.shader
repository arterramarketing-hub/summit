// The mountain's people seen from further off (PeopleLod): one simplified mesh a person, every vertex a slot of
// colour (skin, jacket, trousers, helmet, lens...) in uv.x. The colours come from a small palette picture, a row a
// person (PeopleKit.Row), so hundreds of differently dressed riders on the lifts draw in a handful of instanced
// batches: each instance only carries its row. The jacket and trousers carry their rest pose (uv2, uv3), and their
// row their pattern (its second colour, kind, scale and colour block's height), drawn as up close (ClothPattern.hlsl,
// the same code as SnowyCloth's): a camo jacket is camo at every distance. Lit like everything else: the sky, the sun
// or moon with its shadows, lamps nearby, a little sheen where the slot is glossy (a lens, a helmet), the fog.
Shader "Summit/PeopleLit"
{
    Properties
    {
        _Palette ("Palette (slots across, a person a row)", 2D) = "grey" {}
        _Rows ("Rows", Float) = 256
        _Row ("Row", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Palette_TexelSize;
            float _Rows;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(PeopleProps)
            UNITY_DEFINE_INSTANCED_PROP(float, _Row)
        UNITY_INSTANCING_BUFFER_END(PeopleProps)
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/Farview.hlsl"   // (far off, on a chair coming into reach, through the world's far band)
            #include "../Shaders/ClothPattern.hlsl"
            TEXTURE2D(_Palette); SAMPLER(sampler_Palette);
            // the row's slots the clothes use (PeopleKit: SJacket, SJacket2, SPants; each garment's second colour, then
            // its pattern: kind, repeats a metre, the colour block's height)
            #define SLOT_JACKET 1.0
            #define SLOT_JACKET2 2.0
            #define SLOT_PANTS 3.0
            #define SLOT_JACKET_B 16.0
            #define SLOT_PANTS_B 18.0

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0;
                float3 restPos : TEXCOORD2; float3 restNormal : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; half fog : TEXCOORD3; float3 restPos : TEXCOORD4; float3 restNormal : TEXCOORD5;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                float row = UNITY_ACCESS_INSTANCED_PROP(PeopleProps, _Row);
                o.uv = float2(i.uv.x, (row + 0.5) / max(_Rows, 1.0));
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.restPos = i.restPos; o.restNormal = i.restNormal;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                FarviewClip(i.positionWS, i.positionCS);
                half4 pal = SAMPLE_TEXTURE2D(_Palette, sampler_Palette, i.uv);
                // the clothes' pattern (the other slots plain: pattern 0). Fetched for every pixel, so the pattern's
                // footprint (fwidth) is taken the same on both sides of a garment's edge
                float slot = floor(i.uv.x * _Palette_TexelSize.z);
                bool jacket = slot == SLOT_JACKET || slot == SLOT_JACKET2, pants = slot == SLOT_PANTS;
                float g = pants ? SLOT_PANTS_B : SLOT_JACKET_B;
                half4 second = SAMPLE_TEXTURE2D(_Palette, sampler_Palette, float2((g + 0.5) * _Palette_TexelSize.x, i.uv.y));
                float4 kind = SAMPLE_TEXTURE2D(_Palette, sampler_Palette, float2((g + 1.5) * _Palette_TexelSize.x, i.uv.y));
                half3 albedo = ClothPattern(pal.rgb, second.rgb, jacket || pants ? kind.r : 0.0, kind.g, kind.b, i.restPos, i.restNormal, float3(0.0, 1.0, 0.0));
                half smooth = pal.a;
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                half3 light = SampleSH(n);
                half3 spec = 0;
                half shininess = exp2(10.0 * smooth + 1.0);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 sunL = sun.color * (sun.distanceAttenuation * sun.shadowAttenuation);
                light += sunL * saturate(dot(n, sun.direction));
                spec += sunL * pow(saturate(dot(n, normalize(sun.direction + v))), shininess) * smooth * smooth * 0.6;
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; k++)
                {
                    Light l = GetAdditionalLight(k, i.positionWS);
                    half3 c = l.color * (l.distanceAttenuation * l.shadowAttenuation);
                    light += c * saturate(dot(n, l.direction));
                    spec += c * pow(saturate(dot(n, normalize(l.direction + v))), shininess) * smooth * smooth * 0.6;
                }
                #endif
                half3 c = albedo * light + spec;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 p = TransformObjectToWorld(i.positionOS.xyz), n = TransformObjectToWorldNormal(i.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 l = normalize(_LightPosition - p);
                #else
                float3 l = _LightDirection;
                #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, n, l));
                #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(i); return TransformObjectToHClip(i.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
