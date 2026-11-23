// What blood in snow (or on a trunk or rock) looks like, shared by BloodSnow.shader (the marks on the snow and
// trunks) and BloodDecal.shader (drawn over a rock's own mesh). Each works out how much blood there is at a pixel
// (D) from the mark's own coordinates, and Shade turns that into the multiply over what is drawn there already
// (Blend DstColor SrcColor: out = 2 * src * dst, 0.5 leaving it as it was). See BloodSnow.shader.
#ifndef SUMMIT_BLOOD_COMMON
#define SUMMIT_BLOOD_COMMON

TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
float _BloodClock, _BloodSnowAcc, _NLOn;

float3 BloodHash32(float2 p) { float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973)); p3 += dot(p3, p3.yxz + 33.33); return frac((p3.xxy + p3.yzz) * p3.zyx); }
half4 BloodN(float2 uv) { return SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv); }

// a scatter of little drops round a mark: one per cell at most, some cells empty
float BloodDrops(float2 q, float seed, float cell, float keep)
{
    float2 c = q / cell + seed * 7.13;
    float2 ci = floor(c), cf = frac(c);
    float3 h = BloodHash32(ci);
    float r = 0.05 + 0.13 * h.z * h.z;
    float2 o = cf - (0.25 + 0.5 * h.xy);
    float d = length(o) * (0.85 + 0.3 * BloodN(o * 0.35 + h.xy).r);   // not quite round
    return (h.z * 0.6 + h.x * 0.4 < keep) ? 1.0 - smoothstep(r * 0.55, r, d) : 0.0;
}

// how soaked in a mark is, from its age (0 at the hit, ~0.95 at its soak time)
float BloodSoak(float age, float soakTime) { return 1.0 - exp(-3.0 * age / max(soakTime, 0.05)); }

// A stain at p (its own coordinates, in radii; +y the way it was thrown, or up a trunk or rock's face): an impact,
// a pool (rest: still filling), or on a face (runs dripping down -y). Returns the blood in it; wetEdge the thin rim.
float BloodStain(float2 p, float seed, float age, float soakTime, float thrown, float rest, float face, out float wetEdge)
{
    float soak = BloodSoak(age, soakTime);
    float creep = 0.05 * log2(1.0 + age * 0.25);   // the rim keeps wicking out, slower and slower
    float2 q = float2(p.x, p.y > 0.0 ? p.y / (1.0 + thrown) : p.y * (1.0 - 0.15 * thrown));
    float r = length(q);
    half4 lo = BloodN(p * 0.13 + seed);
    half4 hi = BloodN(p * 0.41 + seed * 1.7 + 0.37);
    float R = lerp(0.4, 1.0, soak) + creep + rest * 0.35 * saturate(age / 14.0);
    // a lobed edge, and fine fingers wicking out from it through the snow crystals (capillary)
    float edge = R * (0.74 + 0.5 * lo.r);
    float ang = atan2(q.y, q.x) * 0.159155;
    half4 fing = BloodN(float2(ang, r * 0.09 + seed * 0.31));
    float finger = pow(saturate(fing.g * 1.25 - 0.1), 4.0) * (1.0 - 0.6 * face);
    float x = r / (edge + R * 0.55 * finger * soak);
    float core = 1.0 - smoothstep(0.05, 0.8, x + (hi.r - 0.5) * 0.35 - 0.15 * rest);
    float body = 1.0 - smoothstep(0.82, 1.0, x);
    wetEdge = body * (1.0 - core);
    float D = body * (0.16 + 0.85 * core * lerp(0.8, 1.1, hi.g));
    // spatter round it, thicker ahead where it was thrown, beyond the stain
    float ahead = saturate(0.5 + 0.5 * q.y / max(r, 1e-3));
    float keep = (0.28 + 0.35 * thrown * ahead) * smoothstep(edge * 1.9, edge * 1.05, r) * step(edge * 0.95, r);
    D = max(D, 0.75 * BloodDrops(q, seed, 0.28, keep) * saturate(soak * 3.0));
    if (face > 0.5)
    {
        // on a trunk or a rock: runs down from it (-y is down), lengthening for a few seconds
        // a few thin runs, each wandering a little, thinning as it goes, a bead at its end
        float wob = (BloodN(float2(p.y * 0.12, seed * 0.37)).r - 0.5) * 0.35;
        float cx = (p.x + wob) * 2.4 + seed * 3.1;
        float3 h = BloodHash32(float2(floor(cx), seed));
        float len = (0.35 + 1.1 * h.x) * saturate(age / (1.2 + 2.5 * h.z)) * step(0.5, h.z);
        float y0 = -R * (0.55 + 0.3 * h.y);
        float along = saturate((y0 - p.y) / max(len, 1e-3));
        float half_ = (0.035 + 0.06 * h.y) * lerp(1.0, 0.45, along);
        float w = abs(frac(cx) - 0.5) / half_;
        float run = (1.0 - smoothstep(0.5, 1.0, w)) * step(p.y, y0) * step(y0 - len, p.y);
        float tip = 1.0 - smoothstep(0.03, 0.06, length(float2((frac(cx) - 0.5) / 2.4, p.y - (y0 - len))));
        D = max(D, (run * lerp(0.6, 0.35, along) + tip * 0.6 * step(0.05, len)) * soak);
    }
    // (never out to the mesh's edge: its grid spans +-2.5 radii, further ahead where thrown)
    float lim = max(abs(p.x), p.y > 0.0 ? p.y / (1.0 + thrown) : -p.y * (face > 0.5 ? 0.55 : 1.0));
    return min(D, 1.1) * (1.0 - smoothstep(2.05, 2.45, lim));
}

// the share of the light here that is this light, times its highlight on wet blood (normalised Blinn-Phong)
float BloodGlint(float3 n, float3 v, float3 l, float power)
{
    float3 h = normalize(l + v);
    float nh = saturate(dot(n, h));
    const float P = 160.0;
    return power * (P + 8.0) * 0.0398 * pow(nh, P) * saturate(dot(n, l));
}

// D (blood in it), wetEdge, age (s) of a mark at this pixel -> what to multiply the snow (or bark, or rock) by, x0.5.
// grainScale: how fine the surface's own grain is (snow: crystals; bark and rock: less)
half4 BloodShade(float D, float wetEdge, float age, float powderAt, float3 posWS, float3 normalWS, float seed, float4 lamp, float fog)
{
    // the snow's own grain: crystals take it up unevenly
    half4 g = BloodN(posWS.xz * 0.61);
    D *= lerp(0.75, 1.15, g.a);
    // dusted over: powder thrown over it at the hit, and snow falling since
    float cover = saturate(_BloodSnowAcc - powderAt);
    float dust = smoothstep(cover - 0.1, cover + 0.1, 1.0 - BloodN(posWS.xz * 1.37 + 0.5).b);
    D *= 1.0 - dust * (0.6 + 0.4 * cover);
    if (D < 0.004) return half4(0.5, 0.5, 0.5, 1.0);

    // Beer-Lambert in the snow: thin is pink, thick a deep red; drying, it darkens toward a brown-red
    float dry = saturate(age / 45.0);
    float3 absorb = float3(0.55 + 0.5 * dry, 4.4 + 1.0 * dry, 4.1 + 0.3 * dry);
    float3 tint = exp(-absorb * D * 1.25);

    // the sheen of fresh blood: the sun's or moon's highlight, a floodlight's, the headlamp's and the lit lamps
    // near the rider's, each as its share of the light here; and a wet sky reflection at a glancing angle. Bumped
    // where it soaked in unevenly, dulling as it soaks in and freezes
    float wet = saturate(D * 1.4) * lerp(1.0, 0.35, saturate(age / 60.0)) * (0.55 + 0.45 * saturate(wetEdge * 2.0 + D - 0.4));
    float3 v = normalize(GetWorldSpaceViewDir(posWS));
    half4 b = BloodN(posWS.xz * 2.3 + seed);
    float3 n = normalize(normalWS + float3(b.r - 0.5, 0.0, b.g - 0.5) * 0.35);
    float4 shadowCoord = TransformWorldToShadowCoord(posWS);
    Light mainL = GetMainLight(shadowCoord);
    float3 amb = SampleSH(n);
    float eMain = Luminance(mainL.color) * mainL.shadowAttenuation;
    float total = Luminance(amb) + eMain * saturate(dot(n, mainL.direction)) + 1e-3;
    float spec = BloodGlint(n, v, mainL.direction, eMain);
    float lampE = lamp.w * _NLOn * 1.3;
    if (lampE > 0.0)
    {
        float3 lv = normalize(lamp.xyz - posWS);
        spec += BloodGlint(n, v, lv, lampE);
        total += lampE * saturate(dot(n, lv));
    }
    #if defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
    for (uint li = 0u; li < count; ++li)
    {
        Light al = GetAdditionalLight(li, posWS);
        float e = Luminance(al.color) * al.distanceAttenuation * al.shadowAttenuation;
        spec += BloodGlint(n, v, al.direction, e);
        total += e * saturate(dot(n, al.direction));
    }
    #endif
    float fres = 0.03 + 0.97 * pow(1.0 - saturate(dot(n, v)), 5.0);
    float sheen = wet * (fres * spec / total + fres * 0.6);

    float3 col = tint + sheen * float3(1.0, 0.93, 0.9);
    col = MixFogColor(col, float3(1.0, 1.0, 1.0), fog);
    return half4(col * 0.5, 1.0);
}

#endif
