// The closet's cloth patterns (stripes, camo, a colour block, plaid, Nordic knit), laid out on the garment's rest pose
// in metres so they stay put on the cloth however the limbs move. One copy, shared by Summit/SnowyCloth (the hero, and
// people up close) and Summit/PeopleLit (people further off: the simplified bodies and the baked poses), so a jacket
// is the same jacket at every distance.
#ifndef SUMMIT_CLOTH_PATTERN
#define SUMMIT_CLOTH_PATTERN

// value noise in 3D, 0..1
float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float Noise(float3 x)
{
    float3 i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// bands: x in repeats, w how many repeats a pixel spans (its fwidth)
half StripeBandW(float x, float w)
{
    float tri = abs(frac(x) - 0.5) * 2.0;           // 0 in the middle of one band, 1 in the middle of the next
    float aa = max(w * 2.0, 1e-4);
    return smoothstep(0.5 - aa, 0.5 + aa, tri);
}

// Nordic knit (Brianna's fleece): bands round the body, each a row of one motif in the second colour: open diamonds
// with a small one inside, a zigzag, a row of dots. x runs round the body, y up it, both in repeats; px how many
// repeats a pixel spans
half KnitW(float2 p, float px)
{
    float aa = min(px, 0.03);
    float y = frac(p.y);
    // diamonds, two to a repeat: an outline and its middle
    float2 d = float2(abs(frac(p.x * 2.0) - 0.5) * 0.5, abs(y - 0.26));
    float dm = d.x + d.y;
    half k = smoothstep(0.215 + aa, 0.215 - aa, dm) * smoothstep(0.15 - aa, 0.15 + aa, dm) + smoothstep(0.065 + aa, 0.065 - aa, dm);
    // the zigzag, four points to a repeat
    float zz = y - 0.63 - (abs(frac(p.x * 4.0) - 0.5) - 0.25) * 0.22;
    k += smoothstep(0.024 + aa, 0.024 - aa, abs(zz));
    // dots, set between the zigzag's points
    float2 o = float2((frac(p.x * 4.0 + 0.5) - 0.5) * 0.25, y - 0.87);
    k += smoothstep(0.032 + aa, 0.032 - aa, length(o));
    // (far off, where a motif is a pixel or two, it settles to the even mix the knit averages to)
    return lerp(saturate(k), 0.18, saturate((px - 0.03) / 0.07));
}

// A garment's colour at a point of its rest pose: a its main colour, b the pattern's second; pattern 0 plain,
// 1 stripes, 2 camo, 3 colour block, 4 plaid, 5 Nordic knit; scale its repeats a metre; at a colour block's height
// (rest frame, m; negative: below that height); up the rest frame's up. The pixel's footprint is measured before any
// branch: PeopleLit picks each pixel's garment (and so its pattern) from the slot it is drawn in.
half3 ClothPattern(half3 a, half3 b, half pattern, float scale, float at, float3 restPos, float3 restNormal, float3 up)
{
    float3 side = normalize(cross(up, float3(0.0, 0.0, 1.0)) + float3(1e-4, 0.0, 0.0));
    float3 fwd = cross(side, up);
    float hs = dot(restPos, up) * scale, ss = dot(restPos, side) * scale, fs = dot(restPos, fwd) * scale;
    float dh = fwidth(hs), ds = fwidth(ss), df = fwidth(fs);
    if (pattern < 0.5) return a;
    if (pattern < 1.5) return lerp(a, b, StripeBandW(hs, dh));
    if (pattern < 2.5)
    {
        float n = Noise(restPos * scale) * 0.65 + Noise(restPos * scale * 2.6 + 3.1) * 0.35;
        // (up close the blots' edges are as drawn; far off, where a blot is a pixel or two, they soften instead of
        // sparkling)
        float w = max(0.0, max(dh, max(ds, df)) * 1.5 - 0.15);
        half3 c = lerp(a, b, smoothstep(0.53 - w, 0.56 + w, n));
        return lerp(c, a * 0.5, smoothstep(0.37 + w, 0.34 - w, n));
    }
    if (pattern < 3.5)
    {
        float h = dot(restPos, up);
        float k = at >= 0 ? smoothstep(at - 0.006, at + 0.006, h) : smoothstep(-at + 0.006, -at - 0.006, h);
        return lerp(a, b, k);
    }
    if (pattern > 4.5)
    {
        // laid on from the front and back, or from the sides where the cloth faces that way, so the motifs keep their
        // shape round the body (tone on tone, the join doesn't show)
        bool sideways = abs(dot(restNormal, side)) > abs(dot(restNormal, fwd));
        float x = sideways ? fs : ss, dx = sideways ? df : ds;
        return lerp(a, b, KnitW(float2(x, hs), max(dx + dh, 1e-4)));
    }
    // plaid: bands round the body and down it, crossed, with a thin dark line through each
    half across = StripeBandW(hs, dh), down = StripeBandW(ss, ds);
    half3 c = lerp(a, b, (across + down) * 0.5);
    half grid = StripeBandW(hs * 4.0 + 0.5, dh * 4.0) * StripeBandW(ss * 4.0 + 0.5, ds * 4.0);
    return c * (1.0 - 0.25 * grid);
}

#endif
