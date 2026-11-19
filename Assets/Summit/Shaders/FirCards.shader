// The realistic trees as cards: branch cards cut out of an atlas (alpha test, both sides), lit by Fir.hlsl's
// sums so they match their far pictures (FirImpostor). The mesh's normals shade a crown as one rounded mass, and
// its vertex colour darkens the inside of the crown.
// A forest tree dissolves from one level of detail into the next (Forest.cs, Fir.hlsl): _FadeOut says which band it
// dissolves away over (1 the far one, into its picture; 2 the near one: the rich tree), _FadeIn that it appears
// over the near band (the plain tree). The camp's and the start tape's own trees leave both off.
// _Bake is for RealFir.BakeImpostors only: 1 writes the plain colour, 2 the normal, for the far pictures.
// Every pass moves the vertices with the wind (TreeWind.hlsl); _Sway 0 holds a felled piece still, and _Snow below 1
// shows boughs that have lost their snow (a snapped or smashed tree's top).
Shader "Summit/FirCards"
{
    Properties
    {
        [MainTexture] _BaseMap ("Atlas", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _FadeOut ("Dissolves away over (0 never, 1 the far band, 2 the near band)", Float) = 0
        _FadeIn ("Appears over the near band", Float) = 0
        _Bake ("Bake output (0 lit, 1 colour, 2 normal)", Float) = 0
        _NeedleTint ("Needles graded toward the dark blue-green of the Cascades' firs", Color) = (0.62, 0.78, 0.86, 1)
        _CutY ("Nothing drawn below this height (model space): a felled tree's buried part", Float) = -10000
        _Sway ("Moves in the wind (0: a felled piece lying still)", Float) = 1
        _Snow ("Snow left on the boughs (1 all of it; less: shaken off, bare and darker)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _FadeOut;
            half _FadeIn;
            half _Bake;
            float _CutY;
            half4 _NeedleTint;
            half _Sway;
            half _Snow;
        CBUFFER_END

        // the atlas's greens graded darker and bluer; the snow (pale, unsaturated) is left white
        half3 Needles(half3 c)
        {
            half sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
            return lerp(c, c * _NeedleTint.rgb, saturate(sat * 6.0h));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Fir.hlsl"
            #include "TreeWind.hlsl"

            // how much of a tree standing at `originWS` has dissolved away (x) and has appeared (y), each 0..1
            half2 Fades(float3 originWS)
            {
                half away = _FadeOut > 1.5h ? FirNearFade(originWS) : (_FadeOut > 0.5h ? FirFade(originWS) : 0.0h);
                half here = _FadeIn > 0.5h ? FirNearFade(originWS) : 1.0h;
                return half2(away, here);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;   // r: how much light reaches this part of the crown (white where a mesh has none)
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 fadeFogLift : TEXCOORD3;   // x dissolved away, y fog, z night lift share, w appeared
                half3 vertexLight : TEXCOORD4;
                float objY : TEXCOORD5;
                half shade : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.positionWS = TreeSway(TransformObjectToWorld(i.positionOS.xyz), origin, 1.0, _Sway);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                half2 fades = Fades(origin);
                o.fadeFogLift = half4(fades.x, ComputeFogFactor(o.positionCS.z), FirLiftShare(origin), fades.y);
                o.objY = i.positionOS.y;
                o.shade = i.color.r;
                if (fades.x >= 1.0h || fades.y <= 0.0h) o.positionCS = FirGone();   // all of it is the other level of detail's
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                o.vertexLight = VertexLighting(o.positionWS, o.normalWS);
            #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(c.a - _Cutoff);
                clip(i.objY - _CutY);
                // what is snow in the atlas: pale, and not needle green (white, or blue-white in shade); read before
                // the needles are graded
                half greener = c.g - max(c.r, c.b);
                half snowy = saturate((dot(c.rgb, half3(0.3h, 0.59h, 0.11h)) - 0.33h) * 5.0h) * saturate(1.0h - greener * 15.0h);
                c.rgb = Needles(c.rgb);
                // the snow has let go: there the bare bough under it shows, dark needles
                if (_Snow < 0.999h) c.rgb = lerp(c.rgb, half3(0.045h, 0.07h, 0.055h), snowy * (1.0h - _Snow));
                // a branch card lying flat, seen from underneath: its snow is on top, so here it is shaded needles
                // (what is pale in the picture - the snow - becomes needle green first, then all of it goes dark)
                float3 ng = normalize(cross(ddx(i.positionWS), ddy(i.positionWS)));
                if (abs(ng.y) > 0.5 && dot(ng * sign(ng.y), GetCameraPositionWS() - i.positionWS) < 0)
                {
                    half pale = saturate((dot(c.rgb, half3(0.3h, 0.59h, 0.11h)) - 0.18h) * 8.0h);
                    c.rgb = lerp(c.rgb, half3(0.04h, 0.06h, 0.04h), pale) * 0.28h;
                }
                c.rgb *= i.shade;
                float3 n = normalize(i.normalWS);
                if (_Bake > 0.5) return _Bake < 1.5 ? half4(c.rgb, 1) : half4(n * 0.5 + 0.5, 1);
                half dither = FirDither(i.positionCS);
                clip(dither - i.fadeFogLift.x);
                clip(i.fadeFogLift.w - dither);
                half3 col = FirLight(c.rgb, n, i.positionWS, i.positionCS, i.vertexLight, 1, i.fadeFogLift.z);
                return half4(MixFog(col, i.fadeFogLift.y), 1);
            }
            ENDHLSL
        }

        Pass
        {
            // the whole tree casts its shadow through the hand-overs (shadows have faded out by the far one's end;
            // up close the forest draws the plain tree for its shadow and the rich one without: Forest.cs)
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "TreeWind.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float objY : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                o.objY = i.positionOS.y;
                float3 pos = TreeSway(TransformObjectToWorld(i.positionOS.xyz), TransformObjectToWorld(float3(0, 0, 0)), 1.0, _Sway);
                float3 nrm = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 toLight = normalize(_LightPosition - pos);
            #else
                float3 toLight = _LightDirection;
            #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(pos, nrm, toLight)));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                clip(i.objY - _CutY);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Fir.hlsl"
            #include "TreeWind.hlsl"

            // how much of a tree standing at `originWS` has dissolved away (x) and has appeared (y), each 0..1
            half2 Fades(float3 originWS)
            {
                half away = _FadeOut > 1.5h ? FirNearFade(originWS) : (_FadeOut > 0.5h ? FirFade(originWS) : 0.0h);
                half here = _FadeIn > 0.5h ? FirNearFade(originWS) : 1.0h;
                return half2(away, here);
            }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half2 fades : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.positionCS = TransformWorldToHClip(TreeSway(TransformObjectToWorld(i.positionOS.xyz), origin, 1.0, _Sway));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fades = Fades(origin);
                if (o.fades.x >= 1.0h || o.fades.y <= 0.0h) o.positionCS = FirGone();
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                half dither = FirDither(i.positionCS);
                clip(dither - i.fades.x);
                clip(i.fades.y - dither);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
