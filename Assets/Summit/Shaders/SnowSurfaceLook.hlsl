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
float4 _SurfaceRun;     // z: the lines' strength
// (cordflicker) the run's own line down the mountain, for the grooming's lines and the ice's streaks: a texel a step of z,
// R how far the traverses have swung the run sideways there (world x), G and B the cos and sin of its heading, A how far
// along it (m). Read with Load (no sampler: the terrain's are all taken), between two texels by hand.
TEXTURE2D(_SurfaceGroom);
float4 _SurfaceGroomST; // x: the first texel's z, y: 1 / the step, z: the last texel (0: none: the lines straight down z)
float4 _SurfaceGlacier; // x: the glacier's top (world z), y: its bottom, z: the metres the grooming fades over at each (0: none)
// (the camp's two maps borrow the snow's own sampler, sampler_BaseMap, a repeating one: Metal's pass has 16 and the
// terrain's is full. The camp map is held off its edge by half a texel below, so it reads as if clamped.)
TEXTURE2D(_CampMap);
TEXTURE2D(_CampPrints);
float4 _CampRect;       // xy: the camp map's corner (world x, z); zw: 1 / the metres it spans (0: no camp)

// a value noise for the ice's patches (a hash without sines: the same on every GPU, in float)
float IceHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}
float IceNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(IceHash(i), IceHash(i + float2(1.0, 0.0)), f.x), lerp(IceHash(i + float2(0.0, 1.0)), IceHash(i + 1.0), f.x), f.y);
}

// The run's own axes at a place on the snow: across it (m, from its swung line) and along it (m), and the way across
// (in x and z). Fixed to the snow: where a cliff band's traverse turns the run, the lines turn with it, curving with the
// turn in, rather than all of them being swung round the map's middle by the run's heading under the rider (from
// kilometres away, a degree's turn swept every line across the snow in a frame: they crawled and flickered)
void GroomAxes(float2 wp, out float across, out float along, out float2 dir)
{
    across = wp.x; along = wp.y; dir = float2(1.0, 0.0);
    UNITY_BRANCH
    if (_SurfaceGroomST.z > 0.0)
    {
        float u = clamp((wp.y - _SurfaceGroomST.x) * _SurfaceGroomST.y, 0.0, _SurfaceGroomST.z);
        float i = min(floor(u), _SurfaceGroomST.z - 1.0);
        float4 g = lerp(LOAD_TEXTURE2D(_SurfaceGroom, int2((int)i, 0)), LOAD_TEXTURE2D(_SurfaceGroom, int2((int)i + 1, 0)), u - i);
        float2 cs = g.yz * rsqrt(max(dot(g.yz, g.yz), 1e-6));
        float dx = wp.x - g.x;
        across = dx * cs.x;
        along = g.w + dx * cs.y;
        dir = float2(cs.x, -cs.y);
    }
}

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

    // the snow's own grain again, four times the size (the lumps of tracked snow, below; and the ice's edge)
    half3 chop = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uv * _DetailAlbedoMap_ST.xy * 0.27 + 0.19), 1.0h);

    // the run's own axes there (the grooming's lines and the ice's streaks follow them)
    float across, along; float2 runAcross;
    GroomAxes(positionWS.xz, across, along, runAcross);

    // ice, by height (as a height-blended terrain does it): where the map says some ice, it lies in the low spots
    // first and the snow stays on the high ones, so a patch's edge is ragged, not a linear fade. The height is not the
    // snow's wind-groove map (its ripples run across the hill and made the ice a field of dune-like stripes): it is
    // the shape of scoured glaze, a noise stretched down the fall line (patches a couple of metres across and
    // several long, their edges wandering; no texture, so no tile to repeat, and only worked out where there is
    // ice), with the grain at its larger scale on its edges; and a macro field over tens of metres moves the edge in
    // and out, so no two patches look stamped. Far off, where a patch is under a few pixels, the noise fades to
    // its middle (no crawl). The look only: the ride reads the map's share (Surfaces)
    float2 wp = positionWS.xz;
    half macro = (half)(0.5 + 0.5 * sin(wp.x * 0.071 + sin(wp.y * 0.043) * 1.6) * sin(wp.y * 0.057 + sin(wp.x * 0.031 + 1.3) * 1.4));
    float2 glaze = float2(across * 0.55 + sin(along * 0.19 + across * 0.3) * 0.7, along * 0.13);   // (1.8 m across, 7.7 m along)
    half glazeSeen = (half)saturate((1.0 / max(fwidth(glaze.x), 1e-4) - 3.0) / 6.0);   // (pixels a cell spans; taken out of the branch)
    half iceMap = m.g;   // (the map's own share: the stretch's, before the patches)
    // the scoured streaks in the ice: polished where the wind cut it, frosted between, long down the fall line and
    // a metre or so across, wandering and broken (the same noise, finer across and longer along; not a sine, whose
    // even ribs read as a pattern), faded to their middle once a streak is under 8 pixels, so they never crawl
    float2 streak = float2(across * 1.1 + sin(along * 0.11) * 0.9, along * 0.06 + 31.7);
    half scourSeen = (half)saturate((1.0 / max(fwidth(streak.x), 1e-4) - 8.0) / 10.0);
    half scour = 0.5h;
    UNITY_BRANCH
    if (m.g > 0.004h)
    {
        scour = lerp(0.5h, (half)smoothstep(0.25, 0.75, IceNoise(streak)), scourSeen);
        half n = (half)(IceNoise(glaze) * 0.68 + IceNoise(glaze * 2.7 + 17.3) * 0.32);
        half h = lerp(0.5h, n, glazeSeen) * 0.75h + saturate(0.5h + 0.6h * (chop.x + chop.y) * 0.5h) * 0.25h;
        half e = m.g + (0.5h - h) * 0.75h + (macro - 0.5h) * 0.2h;
        m.g = min(saturate((e - 0.5h) * 4.0h + 0.5h), saturate(m.g * 4.0h));   // (and none where the map has none)
    }

    // snow a body has churned up (SnowLayer: its mask): chopped like crud, its grooming gone for good
    m.b = max(m.b, SnowChurn(positionWS) * (1.0h - m.g));
    half cord = saturate(known - m.r - m.g - m.b - m.a - slush);
    // no grooming lines on the glacier: wind-packed snow round the crevasses (the snow underfoot is still
    // the groomed run's to ride: only the look)
    half glacier = _SurfaceGlacier.z > 0.0 ? (half)(saturate((positionWS.z - _SurfaceGlacier.x + _SurfaceGlacier.z) / _SurfaceGlacier.z)
                                                  * saturate((_SurfaceGlacier.y + _SurfaceGlacier.z - positionWS.z) / _SurfaceGlacier.z)) : 0.0h;
    // (nor in an icy stretch, between its patches: the wind that scoured the ice has packed the snow there flat; the
    // stretch's own share, so the lines do not show through the ragged edge of every patch)
    half combed = cord * (1.0h - glacier) * (1.0h - saturate(iceMap * 2.5h));

    // the relief (the wind grooves and the grain): how much of it each kind keeps. Groomed snow keeps almost
    // none: the groomer has flattened the wind's ripples, and its own lines are the only ones there (two
    // sets of lines read as moire and as motion where there is none)
    half relief = 1.0h + combed * -0.88h + (cord - combed) * -0.35h + m.r * -0.7h + m.g * -0.85h + m.b * -0.55h + m.a * 0.45h + slush * -0.45h;
    // (the camp's: untouched snow keeps a soft third of the grain, trodden and shovelled snow hardly any)
    // (an icy stretch is flat, ice and wind-packed snow alike: the wind's grooves gone from it as from groomed snow.
    // Partly icy snow keeping them made dune-like stripes and hot glints across the ice)
    relief = lerp(relief, 0.12h, saturate(iceMap * 2.5h));
    relief = lerp(relief, max(0.06h, 0.32h - camp.r * 0.18h - camp.b * 0.24h - camp.g * 0.1h), inCamp * (1.0h - m.b));
    s.normalTS.xy *= relief;
    s.normalTS.xy += printN * 1.1h;
    // (tracked snow is chopped, not combed: the snow's own grain again, four times the size and much
    // deeper, in lumps; a little of it in the crust. Sampled above)
    s.normalTS.xy += chop.xy * (m.b * 2.6h + m.a * 0.5h);

    // corduroy: the grooming's ridges, straight down the run, as a tiller leaves them: fine ridges 2.6 cm
    // apart, in soft groups of three, each wandering a few millimetres along its length, softly shaded (the
    // light raking across them, a touch of shade in the furrows, never black and white bars); and the
    // groomer's passes, each a lane 4.5 m wide a shade off its neighbours. Each set of lines fades out by how
    // many pixels one spans (whole at 10 or more, gone by 4), well before the pixel grid can alias it into
    // crawling bands at speed: the fine ridges close to, the groups a little further off (the same for
    // the lanes' edges, which are softened to a pixel or two and fade out once a lane is under 12 pixels)
    float wander = (sin(along * 1.7 + across * 0.9) + sin(along * 0.43 - across * 2.1)) * 0.002;
    float phase = (across + wander) * 241.66;   // (2.6 cm from ridge to ridge)
    float phase3 = phase / 3.0;                 // (the groups: 7.8 cm)
    float px = 6.2832 / max(fwidth(phase), 1e-4), px3 = 6.2832 / max(fwidth(phase3), 1e-4);   // (pixels from one line to the next)
    half fine = (half)saturate((px - 4.0) / 6.0); fine *= fine;
    half fine3 = (half)saturate((px3 - 4.0) / 8.0); fine3 *= fine3;
    half lines = combed * (half)_SurfaceRun.z;
    half ridge = (half)cos(phase) * fine * 0.55h + (half)cos(phase3) * fine3 * 0.45h;
    s.normalTS.xy += (half2)runAcross * (ridge * 0.28h * lines);
    float lanePhase = across * 0.2222;   // (4.5 m a pass)
    float laneW = max(fwidth(lanePhase), 1e-4);
    half lane = (half)(saturate((abs(frac(lanePhase + 0.5) - 0.5) - 0.25) / (laneW * 1.5)) * 2.0 - 1.0);
    lane *= (half)saturate((1.0 / laneW - 12.0) / 24.0);
    half groove = ((half)sin(phase) * fine * 0.5h + (half)sin(phase3) * fine3 * 0.5h) * lines;
    half shade = max(fine, fine3) * lines;

    // ice: wind-scoured, in streaks down the fall line (scour, above), polished between frosted ones. Read from up
    // the hill as a greyer, glossy sheet with the grooming gone (SurfacesIce)
    // (not a flat sheet: the glaze is frozen grains, so the light breaks up on it as it does on real hard-pack ice)
    s.normalTS.xy += chop.xy * (m.g * 0.3h);

    // the colour
    // (the grooves drawn in the snow's own colour go with their relief: smooth snow is not striped)
    half3 a = s.albedo;
    half3 plain = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 7).rgb * _BaseColor.rgb;
    a = lerp(a, plain, saturate(cord * 0.9h + m.r * 0.85h + m.g * 0.55h + m.b * 0.5h + slush * 0.6h));
    a *= 1.0h + combed * 0.02h * lane - 0.03h * (0.5h - 0.5h * groove) * shade;
    a = lerp(a, min(a * 1.035h, half3(1.0h, 1.0h, 1.0h)), m.r);
    // (the polished streaks darker, the frosted ones between them a shade lighter)
    // (and over tens of metres, bluer and more polished or greyer and more frosted: macro)
    // (grey with a little blue, as hard-pack ice is: not a saturated blue film; frosted grains a shade lighter)
    half3 iceTint = lerp(half3(0.71h, 0.77h, 0.85h), half3(0.62h, 0.72h, 0.86h), macro);   // (dark enough to be seen coming)
    half rime = saturate(0.5h + (chop.x - chop.y) * 0.6h);
    a = lerp(a, a * iceTint * (0.92h + 0.16h * (1.0h - scour)) * (0.94h + 0.1h * rime), m.g);
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
    s.smoothness = saturate(s.smoothness + cord * 0.05h + m.r * -0.16h + m.g * (0.24h + 0.34h * scour + 0.1h * macro - 0.12h * rime) + m.b * -0.06h + m.a * 0.2h + slush * 0.3h
                            + camp.r * -0.08h + camp.b * 0.14h + camp.g * 0.4h);
}


#endif
