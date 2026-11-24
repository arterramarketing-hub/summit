// The snow underfoot drawn into the lit surface (mountain audit E1; Surfaces.cs paints the map it reads): shared by
// the terrain's snow (Resources/SnowSurface.shader) and the deformable layer the thrown body presses into
// (SnowLayer.shader), so the two are the same snow wherever they meet: the same corduroy, ice, powder, crud and
// slush, and the same churned snow where a body has been (SnowChurn.hlsl). Needs URP's LitInput (the snow
// material's maps) included first.
//
// In the camp (CampSnow.cs) none of the mountain's kinds: no grooming and hardly any of the wind's grooves, but the
// camp's own snow by place from a map of it (_CampMap, 0.5 m a texel over the camp: R trodden, G dirty and wet, B
// shovelled, A how much is the camp's), boot prints pressed into the trodden snow (_CampPrints, a 2 m tile, two turned
// samples, each print shown where the snow is trodden enough for it), the untouched snow left soft and smooth. A body's
// churned snow there is still churned. Outside the camp's square that is one comparison; in it, three samples more.
#ifndef SUMMIT_SNOWSURFACELOOK_INCLUDED
#define SUMMIT_SNOWSURFACELOOK_INCLUDED
#include "SnowChurn.hlsl"

TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
float4 _SurfaceMapST;   // xy: 1 / (metres the map spans), across and down the hill
float4 _SurfaceWin;     // xy: the middle of what the map covers (world x, z); zw: half its size (0: no map)
float4 _SurfaceDay;     // x: where slush starts (world z); y: 1 / the metres it comes in over; z: how much of it
float4 _SurfaceRun;     // xy: the way across the run under the rider (unit, in x and z); z: the lines' strength
float4 _SurfaceGlacier; // x: the glacier's top (world z), y: its bottom, z: the metres the grooming fades over at each (0: none)
// (the camp's two maps borrow the snow's own sampler, sampler_BaseMap, a repeating one: Metal's pass has 16 and the
// terrain's is full. The camp map is held off its edge by half a texel below, so it reads as if clamped.)
TEXTURE2D(_CampMap);
TEXTURE2D(_CampPrints);
float4 _CampRect;       // xy: the camp map's corner (world x, z); zw: 1 / the metres it spans (0: no camp)


// The snow underfoot, drawn into the lit surface (albedo, smoothness, the relief's strength and the
// grooming's lines) before it is lit
void SnowSurface(float3 positionWS, float2 uv, inout SurfaceData s)
{
    float2 off = abs(positionWS.xz - _SurfaceWin.xy);
    half known = (half)(saturate((_SurfaceWin.z - off.x) * 0.05) * saturate((_SurfaceWin.w - off.y) * 0.025));
    half4 m = SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, positionWS.xz * _SurfaceMapST.xy) * known;   // powder, ice, crud, crust
    half slush = (half)saturate((positionWS.z - _SurfaceDay.x) * _SurfaceDay.y);
    slush = slush * slush * (3.0h - 2.0h * slush) * (half)_SurfaceDay.z * known;
    m *= 1.0h - slush;

    // the camp's snow (CampSnow.cs): trodden (r), dirty and wet (g), shovelled (b), how much is the camp's (a)
    float2 cuv = (positionWS.xz - _CampRect.xy) * _CampRect.zw;
    float2 puv = positionWS.xz * 0.5;   // (the prints: a 2 m tile)
    float2 pdx = ddx(puv), pdy = ddy(puv);
    half4 camp = half4(0.0h, 0.0h, 0.0h, 0.0h);
    UNITY_BRANCH
    if (_CampRect.z > 0.0 && cuv.x > 0.0 && cuv.y > 0.0 && cuv.x < 1.0 && cuv.y < 1.0)
        camp = SAMPLE_TEXTURE2D_LOD(_CampMap, sampler_BaseMap, clamp(cuv, _CampRect.zw * 0.25, 1.0 - _CampRect.zw * 0.25), 0);   // (0.5 m texels)
    half inCamp = camp.a;
    // (none of the mountain's kinds in camp, and no grooming: below)
    m *= 1.0h - inCamp; slush *= 1.0h - inCamp; known *= 1.0h - inCamp;
    half4 pr = half4(0.5h, 0.5h, 1.0h, 0.0h), pr2 = pr;
    UNITY_BRANCH
    if (camp.r > 0.01h)
    {
        pr = SAMPLE_TEXTURE2D_GRAD(_CampPrints, sampler_BaseMap, puv, pdx, pdy);
        // (and again, turned a quarter and a little smaller, so the tile doesn't show)
        float2 puv2 = float2(puv.y, -puv.x) * 0.83 + 0.41;
        pr2 = SAMPLE_TEXTURE2D_GRAD(_CampPrints, sampler_BaseMap, puv2, float2(pdx.y, -pdx.x) * 0.83, float2(pdy.y, -pdy.x) * 0.83);
    }
    // each print shown where the snow is trodden enough for it: its own number under how trodden it is
    half show1 = saturate((camp.r * 1.15h - pr.a) * 10.0h) * step(0.02h, pr.a);
    half show2 = saturate((camp.r * 0.95h - pr2.a) * 10.0h) * step(0.02h, pr2.a) * (1.0h - show1);
    half2 n2 = pr2.rg * 2.0h - 1.0h;
    half2 printN = (pr.rg * 2.0h - 1.0h) * show1 + half2(-n2.y, n2.x) * show2;
    half pressed = lerp(1.0h, pr.b, show1) * lerp(1.0h, pr2.b, show2);

    // snow a body has churned up (SnowLayer: its mask): chopped like crud, its grooming gone for good
    m.b = max(m.b, SnowChurn(positionWS) * (1.0h - m.g));
    half cord = saturate(known - m.r - m.g - m.b - m.a - slush);
    // no grooming lines on the glacier: wind-packed snow round the crevasses (the snow underfoot is still
    // the groomed run's to ride: only the look)
    half glacier = _SurfaceGlacier.z > 0.0 ? (half)(saturate((positionWS.z - _SurfaceGlacier.x + _SurfaceGlacier.z) / _SurfaceGlacier.z)
                                                  * saturate((_SurfaceGlacier.y + _SurfaceGlacier.z - positionWS.z) / _SurfaceGlacier.z)) : 0.0h;
    half combed = cord * (1.0h - glacier);

    // the relief (the wind grooves and the grain): how much of it each kind keeps. Groomed snow keeps almost
    // none: the groomer has flattened the wind's ripples, and its own lines are the only ones there (two
    // sets of lines read as moire and as motion where there is none)
    half relief = 1.0h + combed * -0.88h + (cord - combed) * -0.35h + m.r * -0.7h + m.g * -0.85h + m.b * -0.55h + m.a * 0.45h + slush * -0.45h;
    // (the camp's: untouched snow keeps a soft third of the grain, trodden and shovelled snow hardly any)
    relief = lerp(relief, max(0.06h, 0.32h - camp.r * 0.18h - camp.b * 0.24h - camp.g * 0.1h), inCamp * (1.0h - m.b));
    s.normalTS.xy *= relief;
    s.normalTS.xy += printN * 1.1h;
    // (tracked snow is chopped, not combed: the snow's own grain again, four times the size and much
    // deeper, in lumps; a little of it in the crust)
    half3 chop = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uv * _DetailAlbedoMap_ST.xy * 0.27 + 0.19), 1.0h);
    s.normalTS.xy += chop.xy * (m.b * 2.6h + m.a * 0.5h);

    // corduroy: the grooming's ridges, straight down the run, as a tiller leaves them: fine ridges 2.6 cm
    // apart, in soft groups of three, each wandering a few millimetres along its length, softly shaded (the
    // light raking across them, a touch of shade in the furrows, never black and white bars); and the
    // groomer's passes, each a lane 4.5 m wide a shade off its neighbours. Each set of lines fades out by how
    // many pixels one spans (whole at 10 or more, gone by 4), well before the pixel grid can alias it into
    // crawling bands at speed: the fine ridges close to, the groups a little further off (the same for
    // the lanes' edges, which are softened to a pixel or two and fade out once a lane is under 12 pixels)
    float across = dot(positionWS.xz, _SurfaceRun.xy);
    float along = dot(positionWS.xz, float2(-_SurfaceRun.y, _SurfaceRun.x));
    float wander = (sin(along * 1.7 + across * 0.9) + sin(along * 0.43 - across * 2.1)) * 0.002;
    float phase = (across + wander) * 241.66;   // (2.6 cm from ridge to ridge)
    float phase3 = phase / 3.0;                 // (the groups: 7.8 cm)
    float px = 6.2832 / max(fwidth(phase), 1e-4), px3 = 6.2832 / max(fwidth(phase3), 1e-4);   // (pixels from one line to the next)
    half fine = (half)saturate((px - 4.0) / 6.0); fine *= fine;
    half fine3 = (half)saturate((px3 - 4.0) / 8.0); fine3 *= fine3;
    half lines = combed * (half)_SurfaceRun.z;
    half ridge = (half)cos(phase) * fine * 0.55h + (half)cos(phase3) * fine3 * 0.45h;
    s.normalTS.xy += (half2)_SurfaceRun.xy * (ridge * 0.28h * lines);
    float lanePhase = across * 0.2222;   // (4.5 m a pass)
    float laneW = max(fwidth(lanePhase), 1e-4);
    half lane = (half)(saturate((abs(frac(lanePhase + 0.5) - 0.5) - 0.25) / (laneW * 1.5)) * 2.0 - 1.0);
    lane *= (half)saturate((1.0 / laneW - 12.0) / 24.0);
    half groove = ((half)sin(phase) * fine * 0.5h + (half)sin(phase3) * fine3 * 0.5h) * lines;
    half shade = max(fine, fine3) * lines;

    // the colour
    // (the grooves drawn in the snow's own colour go with their relief: smooth snow is not striped)
    half3 a = s.albedo;
    half3 plain = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 7).rgb * _BaseColor.rgb;
    a = lerp(a, plain, saturate(cord * 0.9h + m.r * 0.85h + m.g * 0.9h + m.b * 0.5h + slush * 0.6h));
    a *= 1.0h + combed * 0.02h * lane - 0.03h * (0.5h - 0.5h * groove) * shade;
    a = lerp(a, min(a * 1.035h, half3(1.0h, 1.0h, 1.0h)), m.r);
    a = lerp(a, a * half3(0.66h, 0.78h, 0.93h), m.g);
    a *= 1.0h - m.b * (0.05h + 0.1h * saturate(-chop.y));
    a = lerp(a, a * half3(0.95h, 0.97h, 1.0h), m.a);
    a = lerp(a, a * half3(0.78h, 0.81h, 0.85h), slush);
    // the camp's: untouched snow smooth and bright; trodden snow packed a shade greyer, the prints' hollows in a blue
    // shadow; shovelled snow compacted, a cold blue-grey; by the doors and the fire, grey-brown and wet
    a = lerp(a, plain * 1.02h, inCamp * 0.55h * (1.0h - m.b));
    a *= 1.0h - camp.r * 0.12h;
    a *= lerp(half3(0.84h, 0.87h, 0.93h), half3(1.0h, 1.0h, 1.0h), pressed);
    a = lerp(a, a * half3(0.84h, 0.88h, 0.95h), camp.b);
    a = lerp(a, a * half3(0.54h, 0.47h, 0.39h), camp.g * 0.9h);
    s.albedo = a;

    // the shine
    s.smoothness = saturate(s.smoothness + cord * 0.05h + m.r * -0.16h + m.g * 0.52h + m.b * -0.06h + m.a * 0.2h + slush * 0.3h
                            + camp.r * -0.08h + camp.b * 0.14h + camp.g * 0.4h);
}


#endif
