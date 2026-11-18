// Shared by the realistic firs' two shaders (FirCards: the near card trees; FirImpostor: their far pictures),
// so the two are lit by exactly the same sums and a tree looks the same colour either side of the hand-over.
//
// Lighting: what URP's Simple Lit does with no specular (the sky's ambient on the normal, the sun or moon with
// its shadow, and any other lights such as the headlamp), then the fog. On top, at dusk and night, a faint
// moonlit lift that grows with distance, so the far forest reads as shapes instead of going black.
//
// The hand-over (Forest.cs): between _FirFade.x and _FirFade.y metres from the rider (across the ground, to the
// tree's foot) a tree is drawn both ways, and a fixed screen pattern decides which of the two each pixel
// shows; the share of picture pixels grows with distance. The two use the same pattern the opposite way
// round, so every pixel is exactly one or the other: no doubled tree, no gap, no line, and only alpha test.
#ifndef SUMMIT_FIR_INCLUDED
#define SUMMIT_FIR_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// set by Forest every frame (x: fade starts, y: fade ends, metres) and the point distance is counted from
float4 _FirFade;
float4 _FirCentre;
// set by DayNight: rgb = the extra light on the far forest at dusk and night (0 by day)
half4 _FirLift;

// how far along the hand-over a tree standing at `originWS` is: 0 cards, 1 picture
half FirFade(float3 originWS)
{
    float d = length(originWS.xz - _FirCentre.xz);
    return (half)saturate((d - _FirFade.x) / max(_FirFade.y - _FirFade.x, 1e-3));
}

// the night lift's share for a tree that far away: none close up (the headlamp has those), all of it far off
half FirLiftShare(float3 originWS)
{
    float d = length(originWS.xz - _FirCentre.xz);
    return (half)smoothstep(40.0, 220.0, d);
}

// a 4x4 ordered dither, 1/32 .. 31/32
half FirDither(float4 positionCS)
{
    uint2 p = (uint2)positionCS.xy & 3;
    const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[p.y * 4 + p.x] + 0.5h) / 16.0h;
}

half3 FirLight(half3 albedo, float3 n, float3 positionWS, float4 positionCS, half3 vertexLight, half sunShade, half lift)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = n;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);

    half3 light = SampleSH(n) + _FirLift.rgb * lift;
    Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS));
    light += sun.color * (saturate(dot(n, sun.direction)) * sun.distanceAttenuation * sun.shadowAttenuation * sunShade);
#if defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light l = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        light += l.color * (saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation);
    LIGHT_LOOP_END
#endif
    light += vertexLight;
    return albedo * light;
}

#endif
