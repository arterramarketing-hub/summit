// The avalanche's powder cloud: big camera-facing puffs whose texture holds the billow's shape (a normal and a
// thickness, from Tools/MakeAvalanche.swift) instead of a picture, so the sun lights each one as a lumpy, rounded
// mass, with its creases in shadow (the texture's blue channel: how open each spot is to the sky). That shading is
// what makes a white cloud read against white snow, and a few dozen large quads read as a churning volume, at the
// cost of one texture read.
//  - colour   rgb: shade (puffs deep in the cloud are given less light), a: opacity
//  - normal / tangent: the quad's facing and its (spun) right-hand direction
//  - uv1      (ground height under the puff's centre, centre x, centre z, fade height) and
//    uv2      (ground slope across, slope down): the snow under the puff as a plane, so the puff's bottom
//             fades softly into the snow instead of cutting a hard line across it
Shader "Summit/Powder"
{
    Properties
    {
        _MainTex ("Puff atlas", 2D) = "white" {}
        _SkyLight ("Light from the sky", Color) = (0.62, 0.70, 0.86, 1)
        _GroundLight ("Light bounced off the snow", Color) = (0.80, 0.84, 0.92, 1)
        _Sun ("Sun strength", Float) = 0.8
        [HideInInspector] _Debug ("Show normals (test tools)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _SkyLight, _GroundLight;
                half _Sun, _Debug;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION; half4 color : COLOR; float3 normalOS : NORMAL; float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0; float4 ground : TEXCOORD1; float2 slope : TEXCOORD2;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0;
                float3 posWS : TEXCOORD1; half3 n : TEXCOORD2; half3 t : TEXCOORD3; half3 b : TEXCOORD4;
                float4 ground : TEXCOORD5; float2 slope : TEXCOORD6; half fog : TEXCOORD7;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.color = i.color;
                o.uv = i.uv;
                o.n = TransformObjectToWorldNormal(i.normalOS);
                o.t = TransformObjectToWorldDir(i.tangentOS.xyz);
                o.b = cross(o.n, o.t) * i.tangentOS.w;
                o.ground = i.ground; o.slope = i.slope;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // the billow's surface, turned from the quad's frame into the world
                half2 nxy = tex.rg * 2 - 1;
                half3 nt = half3(nxy, sqrt(saturate(1 - dot(nxy, nxy))));
                half3 n = normalize(i.t * nt.x + i.b * nt.y + i.n * nt.z);
                Light sun = GetMainLight();
                half ndl = dot(n, sun.direction);
                // powder lets some light through: a wrapped diffuse, but steep enough that billows turned from the
                // sun fall into a cool shade
                half diffuse = pow(saturate(ndl * 0.5 + 0.5), 1.6);
                half open = tex.b;
                half3 sky = lerp(_GroundLight.rgb, _SkyLight.rgb, n.y * 0.5 + 0.5) * (0.4 + 0.6 * open);
                // looking toward the sun, the thin, torn edges glow
                half3 v = normalize(_WorldSpaceCameraPos - i.posWS);
                half glow = pow(saturate(dot(-v, sun.direction)), 8) * (1 - tex.a) * 0.5;
                half3 col = i.color.rgb * (sky * 0.6 + sun.color * _Sun * (diffuse * (0.6 + 0.4 * open) + glow));
                // fade into the snow underneath
                float gy = i.ground.x + i.slope.x * (i.posWS.x - i.ground.y) + i.slope.y * (i.posWS.z - i.ground.z);
                half a = tex.a * i.color.a * saturate((i.posWS.y - gy) / max(i.ground.w, 0.01));
                col = MixFog(col, i.fog);
                if (_Debug > 0.5) return half4(n * 0.5 + 0.5, a);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
