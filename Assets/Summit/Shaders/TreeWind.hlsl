// Wind in the trees, in the vertex stage (no scripts on the trees; the forest stays one instanced draw a kind).
// Used by every tree shader: the realistic cards (firs, pines, saplings, the camp's and the start tape's trees),
// their far pictures (the trunk's lean only) and the original trees (TreeSway.shader).
//
// Gusts: a slowly varying field that blows down the wind's direction at about 9 m/s, so a gust is seen to come
// through a stand tree by tree; mostly calm, with a gust every half a minute or so (TreeWind.cs holds the same
// sum for the snowfall and the sound). The trunk barely leans (more toward its top); the boughs give more the
// further out they reach, bobbing and fluttering each a little out of step with the next, snow and all.
// On top: the air blast running ahead of an avalanche, and one tree shaken by a hit (the rider or a body).
// All of it is 0 until TreeWind.cs sets it (the test tools render still trees unless they ask).
#ifndef SUMMIT_TREEWIND_INCLUDED
#define SUMMIT_TREEWIND_INCLUDED

float4 _TreeWind;       // xy: the way it blows (x, z), z: strength (0 still, 1 the usual), w: time (s)
float4 _TreeBlast;      // x: the avalanche front's z, y: strength (0 none), z: how far ahead it reaches (m)
float4 _TreeHit;        // xy: the shaken tree's foot (x, z), z: when it was hit (wind time), w: how hard (0..1)
float4 _TreeHitDir;     // xy: the way it was knocked (x, z)

float TreeWindHash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }
float TreeWindNoise(float x)
{
    float i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(TreeWindHash(i), TreeWindHash(i + 1.0), f);
}

// how hard the wind gusts at a place now: 0 calm .. 1 the height of a gust (TreeWind.Gust: the same sum)
float TreeGust(float2 xz)
{
    float u = _TreeWind.w * 9.0 - dot(xz, _TreeWind.xy);
    float n = TreeWindNoise(u * (1.0 / 85.0)) * 0.7 + TreeWindNoise(u * (1.0 / 31.0) + 7.3) * 0.3;
    return smoothstep(0.56, 0.86, n);
}

// where a vertex of a tree standing at originWS is moved to by the wind; boughs: 1 for a whole tree, 0 for the
// trunk's lean alone (a far picture), still: 0 for pieces lying on the snow (a felled top)
float3 TreeSway(float3 posWS, float3 originWS, float boughs, float sway)
{
    if (sway <= 0.0) return posWS;
    float3 rel = posWS - originWS;
    float h = max(rel.y, 0.0);
    float r = min(length(rel.xz), 6.0);
    float t = _TreeWind.w;
    float3 dir = float3(_TreeWind.x, 0.0, _TreeWind.y);
    float3 across = float3(-dir.z, 0.0, dir.x);
    float tp = frac(dot(originWS.xz, float2(0.1031, 0.1131))) * 6.2832;   // each tree its own phase
    float bp = dot(posWS, float3(0.9, 0.6, 1.1)) + tp;                      // each bough its own
    float reach = r * r * 0.035 * boughs;                                   // the tips give most
    float3 off = 0.0;
    float k = _TreeWind.z;
    if (k > 0.0)
    {
        float gust = TreeGust(originWS.xz);
        float g = (0.07 + gust) * k;
        // the trunk leans downwind, and rocks as the gust works on it
        float rock = g * (0.8 + 0.2 * sin(t * 1.6 + tp)) + gust * k * 0.2 * sin(t * 2.9 + tp);
        off += dir * (h * h * 0.0008 * rock);
        // the boughs: pushed downwind, bobbing and fluttering out of step
        float flut = sin(t * (4.0 + 3.5 * gust) + bp);
        off += dir * (reach * g * 0.55);
        off.y += reach * (0.04 * k + 0.45 * g) * flut;
        off += across * (reach * g * 0.22 * sin(t * 5.7 + bp * 1.3));
    }
    if (_TreeBlast.y > 0.0)
    {
        // the air pushed ahead of an avalanche: hardest just in front of the snow, everything bent down the hill
        float ahead = originWS.z - _TreeBlast.x;
        float b = _TreeBlast.y * saturate(1.0 - ahead / _TreeBlast.z) * step(-20.0, ahead);
        float shake = sin(t * 9.0 + bp) * 0.35 + 0.65;
        off += float3(0.0, 0.0, 1.0) * (h * h * 0.003 * b + reach * 1.4 * b * shake);
        off.y -= reach * 0.4 * b;
    }
    if (_TreeHit.w > 0.0)
    {
        // a tree just hit shudders, the shake dying away
        float age = t - _TreeHit.z;
        float d = length(originWS.xz - _TreeHit.xy);
        if (age >= 0.0 && age < 3.0 && d < 1.5)
        {
            float a = _TreeHit.w * exp(-age * 2.2) * sin(age * 15.0);
            float3 hd = float3(_TreeHitDir.x, 0.0, _TreeHitDir.y);
            off += hd * (h * 0.035 * a + reach * 0.6 * a);
            off.y += reach * 0.3 * a * sin(age * 23.0 + bp);
        }
    }
    return posWS + off * sway;
}

#endif
