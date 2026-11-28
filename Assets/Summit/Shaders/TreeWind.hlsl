// The one wind, in the vertex stage, for every tree (no scripts on the trees; the forest stays one instanced draw a
// kind). Every tree shader moves its vertices with TreeSway: the realistic cards (rich and plain firs, pines, snags,
// young trees, giants, saplings, the camp's and the start tape's), their far pictures (FirImpostor), the original
// low-poly trees, the camp's birches and the camp firs' shadow stand-ins (TreeSway.shader).
//
// The air (TreeWind.cs sets it; TreeWind.Gust and TreeWind.Sway are the same sums on the CPU): a field of gusts and
// turbulence frozen into the air and carried down the wind's direction at the wind's speed, so a gust is seen to come
// through a stand tree by tree and the trees standing side by side bend together. Each tree's motion is read from the
// field where it stands, with only a metre or so of its own (life, not a phase of its own), and bends it from its
// foot: nothing moves at the foot, more and more toward the top (by the height above the foot alone, so a tree and
// its far picture, which have the same heights, lean exactly alike). Each kind's give (_WindFlex: a fir stiff, a pine
// a little less, a birch loose) only scales how far it goes, never when. On a whole tree (boughs 1) the boughs also
// bob a little, at one rate, each a touch out of step with the next; that alone is missing from the far pictures.
// On top: the air blast running ahead of an avalanche, and one tree shaken by a hit (the rider or a body).
// All of it is 0 until TreeWind.cs sets it (the test tools render still trees unless they ask).
#ifndef SUMMIT_TREEWIND_INCLUDED
#define SUMMIT_TREEWIND_INCLUDED

float4 _TreeWind;       // xy: the way it blows (x, z), z: strength (0 still, ~0.5 a calm day .. ~2.3 a storm), w: time (s)
float4 _TreeAir;        // x: how far the air has travelled (m: the field's offset down the wind)
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

// where in the moving air a place is (m along the wind): the field is a function of this alone. The fronts are bowed
// a little across the slope, over a hundred metres and more, so a gust is not a ruled line
float TreeWindS(float2 xz)
{
    float across = dot(xz, float2(-_TreeWind.y, _TreeWind.x));
    return _TreeAir.x - dot(xz, _TreeWind.xy) + 12.0 * TreeWindNoise(across * (1.0 / 140.0) + 3.7);
}

// the gust at a point of the air: 0 calm .. 1 the height of a gust (mostly calm, a gust every half a minute or so)
float TreeGustAt(float s)
{
    float n = TreeWindNoise(s * (1.0 / 85.0)) * 0.7 + TreeWindNoise(s * (1.0 / 31.0) + 7.3) * 0.3;
    return smoothstep(0.56, 0.86, n);
}

// how hard the wind gusts at a place now (TreeWind.Gust: the same sum, for the snowfall and the sound)
float TreeGust(float2 xz) { return TreeGustAt(TreeWindS(xz)); }

// how far a point `h` metres up a tree is carried for one unit of lean: nothing at the foot, the trunk curving
// over toward the top (a young tree leans nearly as steeply as a giant, not just a little of the way)
float TreeBend(float h) { return h * h / (h + 10.0) * 0.02; }

// where a vertex of a tree standing at originWS is moved to by the wind; boughs: 1 for a whole tree, 0 for its far
// picture (the lean alone); sway: the kind's give (_WindFlex), times 0 for a piece lying on the snow (a felled top)
float3 TreeSway(float3 posWS, float3 originWS, float boughs, float sway)
{
    if (sway <= 0.0) return posWS;
    float3 rel = posWS - originWS;
    float h = max(rel.y, 0.0);
    float r = min(length(rel.xz), 6.0);
    float t = _TreeWind.w;
    float3 dir = float3(_TreeWind.x, 0.0, _TreeWind.y);
    float3 across = float3(-dir.z, 0.0, dir.x);
    float jit = TreeWindHash(dot(originWS.xz, float2(0.1031, 0.1131))) - 0.5;   // each tree's own touch: -0.5..0.5
    float bp = dot(posWS, float3(0.9, 0.6, 1.1));                                // each bough's
    float reach = r * r * 0.035 * boughs;                                        // the tips give most
    float3 off = 0.0;
    float k = _TreeWind.z;
    if (k > 0.0)
    {
        float s = TreeWindS(originWS.xz) + jit * 1.5;
        float gust = TreeGustAt(s);
        float turb = TreeWindNoise(s * (1.0 / 45.0) + 3.1) - 0.5;   // the air's eddies, carried with it (wider than a stand)
        float side = TreeWindNoise(s * (1.0 / 50.0) + 11.7) - 0.5;
        // and the trees rocking on their springs, in step across a stand: a long wave (100 m) running down the wind
        float rock = sin(t * 1.9 - dot(originWS.xz, _TreeWind.xy) * 0.06 + jit * 0.5);
        float lean = k * (0.15 + gust + turb * (0.2 + 0.45 * gust) + (0.08 + 0.25 * gust) * rock);
        float bend = TreeBend(h);
        off = (dir * lean + across * (side * k * (0.15 + 0.35 * gust))) * bend;
        off.y -= 0.5 * dot(off.xz, off.xz) / max(h, 1.0);           // the top swings on an arc, not up a slope
        // the boughs bob with the gust, at one rate, each a little out of step with the next
        off.y += reach * 0.25 * k * (0.08 + 0.5 * gust) * sin(t * 4.6 + bp);
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
