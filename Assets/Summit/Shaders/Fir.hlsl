// Shared by the realistic trees' two shaders (FirCards: the card trees, rich and plain; FirImpostor: their far
// pictures), so all are lit by exactly the same sums and a tree looks the same colour either side of a hand-over.
//
// Lighting: what URP's Simple Lit does with no specular (the sky's ambient on the normal, the sun or moon with
// its shadow, and any other lights such as the headlamp), then the fog. On top, at dusk and night, a faint
// moonlit lift that grows with distance, so the far forest reads as shapes instead of going black.
//
// The hand-overs (Forest.cs), counted in metres from the rider across the ground to the tree's foot:
//  - far, between _FirFade.x and _FirFade.y: the plain card tree gives way to its picture;
//  - near, between _FirFade.z and _FirFade.w: the rich tree gives way to the plain one.
// Through a band a tree is drawn both ways, and a fixed screen pattern decides which of the two each pixel
// shows; the farther one's share grows with distance. The two use the same pattern the opposite way round, so
// every pixel is exactly one or the other: no doubled tree, no gap, no line, and only alpha test. A tree that
// has no pixels left to show (FirGone) is not drawn at all: its vertices are folded away.
#ifndef SUMMIT_FIR_INCLUDED
#define SUMMIT_FIR_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// set by Forest every frame (x, y: the far band; z, w: the near band; metres) and the point distance is counted from
float4 _FirFade;
float4 _FirCentre;
// set by DayNight: rgb = the extra light on the far forest at dusk and night (0 by day)
half4 _FirLift;
// set by DayNight: how cold the snow on the boughs shows. 0 (by day, and when nothing sets it): white in the sun,
// only a hint of the sky's blue where it is shaded. 1 (night): the cool blue of moonlight, as the trees always had
half _FirSnowCool;

// how much of a colour is the snow on the boughs: pale and not needle green (the atlas's snow is blue-white, with its
// own shade baked in). Read on the plain colour, so the near cards and the far pictures (baked from it) agree
half FirSnowy(half3 c)
{
    half greener = c.g - max(c.r, c.b);
    return saturate((dot(c, half3(0.3h, 0.59h, 0.11h)) - 0.33h) * 5.0h) * saturate(1.0h - greener * 15.0h);
}
// the dark blue-green grade the needles get (FirCards' _NeedleTint, at the share the snow used to take of it): at
// night the snow is graded so again, so the night forest looks as it did
#define FIR_SNOW_NIGHT half3(0.77h, 0.87h, 0.92h)

// how far along the far hand-over a tree standing at `originWS` is: 0 cards, 1 picture
half FirFade(float3 originWS)
{
    float d = length(originWS.xz - _FirCentre.xz);
    return (half)saturate((d - _FirFade.x) / max(_FirFade.y - _FirFade.x, 1e-3));
}

// how far along the near hand-over: 0 the rich tree, 1 the plain one
half FirNearFade(float3 originWS)
{
    float d = length(originWS.xz - _FirCentre.xz);
    return (half)saturate((d - _FirFade.z) / max(_FirFade.w - _FirFade.z, 1e-3));
}

// where a tree with nothing left to show sends its vertices: one point outside the view, so no triangle has any area
float4 FirGone() { return float4(2.0, 2.0, 2.0, 1.0); }

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

// The view through the trees (ChaseCamera): the foliage between the lens and the rider is thinned, a fixed screen
// pattern dropping part of its pixels, so the rider and the way ahead show through while the tree is still plainly
// there. Only what stands nearer the lens than the rider is thinned (the trees ahead never are), within a cone from
// the lens round the rider and the line the camera looks along, and anything right at the lens. Shadows are untouched.
float4 _FirSeeEye;     // xyz the lens; w how much (0 off: the first-person view, a menu .. 1)
float4 _FirSeeAt;      // xyz the rider (chest high); w the cone's half-angle (radians)
float4 _FirSeeAhead;   // xyz where the camera looks
#define FIR_SEE_MOST 0.7h   // the most of a crown's pixels dropped: it thins, it never goes

half FirSeeThrough(float3 p)
{
    if (_FirSeeEye.w <= 0.001) return 0.0h;
    float3 e = _FirSeeEye.xyz;
    float3 v = p - e;
    float dist = length(v);
    float3 vr = _FirSeeAt.xyz - e;
    float lr = max(length(vr), 0.5);
    float3 toRider = vr / lr;
    float3 f = normalize(_FirSeeAhead.xyz - e);
    // nearer the lens than the rider (by depth along the view)
    float dr = dot(vr, f);
    half nearer = (half)(1.0 - smoothstep(dr - 1.2, dr - 0.5, dot(v, f)));
    // in the cone round the rider, the view's middle and the line between them
    float3 dir = v / max(dist, 1e-3);
    float c = max(max(dot(dir, toRider), dot(dir, f)), dot(dir, normalize(toRider + f)));
    float ang = acos(clamp(c, -1.0, 1.0));
    half inCone = (half)(1.0 - smoothstep(_FirSeeAt.w * 0.6, _FirSeeAt.w, ang));
    // a frond brushing the lens
    half atLens = (half)(1.0 - smoothstep(1.0, 2.0, dist));
    return max(inCone * nearer, atLens) * (half)_FirSeeEye.w * FIR_SEE_MOST;
}

half3 FirLight(half3 albedo, float3 n, float3 positionWS, float4 positionCS, half3 vertexLight, half sunShade, half lift)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = n;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);

    // The snow by day reads white: its own blue-white goes neutral, and the sky's blue light on it is mostly let go
    // of where the sun is on it, less so in shade (deep shade keeps a cool cast). At night it is as it always was:
    // the needles' blue grade, and the sky's (moonlit) light as it is.
    half snow = FirSnowy(albedo);
    half day = 1.0h - saturate(_FirSnowCool);
    half lum = dot(albedo, half3(0.3h, 0.59h, 0.11h));
    albedo = lerp(albedo, lerp(albedo * FIR_SNOW_NIGHT, half3(lum, lum, lum) * 1.04h, day), snow);
    Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS));
    half sunlit = saturate(dot(n, sun.direction)) * sun.distanceAttenuation * sun.shadowAttenuation * sunShade;
    half3 sky = SampleSH(n);
    half skyLum = dot(sky, half3(0.3h, 0.59h, 0.11h));
    sky = lerp(sky, half3(skyLum, skyLum, skyLum), snow * day * lerp(0.55h, 0.9h, sunlit));
    half3 light = sky + _FirLift.rgb * lift;
    light += sun.color * sunlit;
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
