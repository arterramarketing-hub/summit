// LightsShot's overdraw counter: every fragment drawn adds one (into a half-float target), so the sum over the screen
// is how many fragments a renderer costs. Editor only.
Shader "Hidden/NightLightsCount"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; };
            float4 vert(A i) : SV_POSITION { return TransformObjectToHClip(i.p.xyz); }
            half4 frag() : SV_Target { return half4(1, 1, 1, 1); }
            ENDHLSL
        }
    }
}
