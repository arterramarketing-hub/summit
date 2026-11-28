// Snow a body has churned up (SnowLayer.cs's churn mask): read by the terrain's snow (SnowSurface.shader), which drops
// its grooming there and chops the surface up, and by the deformable layer itself (SnowLayer.shader), so the two
// agree. The mask is one byte a 12.5 cm cell in the layer's own wrapped layout; here it is softened (five taps over
// two and a half cells) and its edge broken up by noise, so a churned patch meets the corduroy along a soft, uneven,
// organic line and never along the grid's steps. It fades out over the patch's last metre and a half, as the
// layer's marks do. Off (no taps at all) when nothing is marked, and outside the patch.
#ifndef SUMMIT_SNOWCHURN_INCLUDED
#define SUMMIT_SNOWCHURN_INCLUDED

#ifndef SUMMIT_SNOWGRID_DECLARED
float4 _SnowGrid;     // x/y: the patch's first cell (x, z), z: cells per side in the buffers, w: cells shown per side
float4 _SnowParams;   // x: cell size (m), y: lift, z: fade-out band (cells)
#endif
TEXTURE2D(_SnowChurn); SAMPLER(sampler_SnowChurn);
float _SnowChurnOn;

float SnowChurnHash(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
float SnowChurnNoise(float2 p)
{
    float2 c = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(SnowChurnHash(c), SnowChurnHash(c + float2(1, 0)), f.x), lerp(SnowChurnHash(c + float2(0, 1)), SnowChurnHash(c + float2(1, 1)), f.x), f.y);
}

// 0..1: how churned the snow at positionWS is
half SnowChurn(float3 positionWS)
{
    if (_SnowChurnOn < 0.5 || _SnowParams.x <= 0.0) return 0.0h;
    float2 c = positionWS.xz / _SnowParams.x;
    float2 rel = c - _SnowGrid.xy;
    float m = _SnowGrid.w;
    float e = min(min(rel.x, m - rel.x), min(rel.y, m - rel.y));
    if (e <= 0.0) return 0.0h;
    float2 uv = (c + 0.5) / _SnowGrid.z;
    float2 d = 1.2 / _SnowGrid.z;
    half v = SAMPLE_TEXTURE2D_LOD(_SnowChurn, sampler_SnowChurn, uv, 0).r * 0.4h
           + (SAMPLE_TEXTURE2D_LOD(_SnowChurn, sampler_SnowChurn, uv + d, 0).r + SAMPLE_TEXTURE2D_LOD(_SnowChurn, sampler_SnowChurn, uv - d, 0).r
            + SAMPLE_TEXTURE2D_LOD(_SnowChurn, sampler_SnowChurn, uv + float2(d.x, -d.y), 0).r + SAMPLE_TEXTURE2D_LOD(_SnowChurn, sampler_SnowChurn, uv + float2(-d.x, d.y), 0).r) * 0.15h;
    if (v < 0.01h) return 0.0h;
    // the edge broken up: lumps and tongues of churned snow a hand to a forearm across
    half n = (half)(SnowChurnNoise(positionWS.xz * 2.3) * 0.6 + SnowChurnNoise(positionWS.xz * 6.7 + 17.0) * 0.4);
    half ch = smoothstep(0.16h, 0.5h, v + (n - 0.5h) * 0.32h);
    return ch * (half)smoothstep(0.0, _SnowParams.z, e);
}

#endif
