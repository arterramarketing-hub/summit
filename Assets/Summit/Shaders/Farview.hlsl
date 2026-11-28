// The streamed world's far edge, shared by every shader that draws something streamed (Farview.cs).
//
// The mountain is only ever built in a box round the rider: the terrain out to 576 m down the hill and 240 m either
// side, the forest about as far, the hamlets, lifts and course pieces as they come within reach. Instead of each
// thing appearing at once where it is built, everything is let in through the same band, counted from the rider
// across the ground: wholly drawn inside its inner edge, not at all past its outer edge, and in between a fixed
// screen pattern decides which pixels show, more of them the nearer it is (only alpha test: no sorting, nothing
// see-through). Built further out than the band's outer edge, nothing is ever seen to appear: it comes in out of the
// haze as the rider rides towards it. What lies beyond is the far field (FarField.cs: a coarse, fogged copy of the
// same mountain out to the backdrop), which nothing in the band ever stands in front of without its own pixels.
//
// A second, short fade by the clock (_FarviewSwap) hands one version of a thing over to another (a terrain chunk
// rebuilt finer, a far house turned into its near model): the two use the same pattern the opposite way round, so
// for that half second every pixel is exactly one or the other.
//
// All zero (before Farview.cs runs, or in a tool that never runs it) means everything is drawn as it always was.
#ifndef SUMMIT_FARVIEW_INCLUDED
#define SUMMIT_FARVIEW_INCLUDED

// x, z: the rider; w: 1 while the band is in use
float4 _FarviewCentre;
// x, y: across the hill, wholly drawn within x metres of the rider and gone by y; z, w: the same down the hill
float4 _FarviewBand;
// x: the clock the hand-overs run on (s); y: how long one takes (s)
float4 _FarviewClock;
// per renderer or per instanced draw (a MaterialPropertyBlock, never a material's): x the time a hand-over began,
// y +1 for the version coming in, -1 for the one going out (0: none)
float4 _FarviewSwap;
// per renderer or per instanced draw too: a hand-over by distance, from the rider to the object's origin.
// x, y: the band (m); z +1 for the near version (drawn inside it), -1 for the far one (0: none)
float4 _FarviewNearFar;

// how much of something at positionWS is let in (0..1)
half FarviewShown(float3 positionWS)
{
    if (_FarviewCentre.w < 0.5) return 1.0h;
    float2 d = positionWS.xz - _FarviewCentre.xz;
    half across = (half)saturate((_FarviewBand.y - abs(d.x)) / max(_FarviewBand.y - _FarviewBand.x, 1e-3));
    half down = (half)saturate((_FarviewBand.w - d.y) / max(_FarviewBand.w - _FarviewBand.z, 1e-3));
    return across * down;
}

// Each fade is a window of the dither's values a pixel must fall in to be drawn: [0, shown) to come in (the band,
// a version handing in), [shown, 1) for the version handing out, so that the two of a hand-over are exactly
// complementary: every pixel is one or the other, never both, never neither. Windows combine by intersection.

// a hand-over by the clock: the version coming in, or the one going out ((0, 1) when none is under way)
half2 FarviewSwapWindow()
{
    if (_FarviewSwap.y == 0.0) return half2(0.0h, 1.0h);
    half k = (half)saturate((_FarviewClock.x - _FarviewSwap.x) / max(_FarviewClock.y, 1e-3));
    return _FarviewSwap.y > 0.0 ? half2(0.0h, k) : half2(k, 1.0h);
}

// a hand-over by distance to the object's origin: the far version comes in as the near one goes ((0, 1) when none)
half2 FarviewNearFarWindow(float3 originWS)
{
    if (_FarviewNearFar.z == 0.0 || _FarviewCentre.w < 0.5) return half2(0.0h, 1.0h);
    float d = length(originWS.xz - _FarviewCentre.xz);
    half k = (half)saturate((d - _FarviewNearFar.x) / max(_FarviewNearFar.y - _FarviewNearFar.x, 1e-3));
    return _FarviewNearFar.z > 0.0 ? half2(k, 1.0h) : half2(0.0h, k);
}

half2 FarviewAnd(half2 a, half2 b) { return half2(max(a.x, b.x), min(a.y, b.y)); }

// is anything of a window left to draw?
bool FarviewAny(half2 w) { return w.y > w.x; }

// a 4x4 ordered dither, 1/32 .. 31/32 (the forest's own: Fir.hlsl)
half FarviewDither(float4 positionCS)
{
    uint2 p = (uint2)positionCS.xy & 3;
    const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[p.y * 4 + p.x] + 0.5h) / 16.0h;
}

// keep this pixel only if its dither value falls in the window
void FarviewClipWindow(half2 w, float4 positionCS)
{
    if (w.x > 0.001h || w.y < 0.999h)
    {
        half d = FarviewDither(positionCS);
        clip(d >= w.x && d < w.y ? 1.0h : -1.0h);
    }
}

// keep this pixel only if it is let in (`shown` already worked out)
void FarviewClipShown(half shown, float4 positionCS) { FarviewClipWindow(half2(0.0h, shown), positionCS); }

// the band and any hand-over by the clock, for a pixel at positionWS
void FarviewClip(float3 positionWS, float4 positionCS)
{
    FarviewClipWindow(FarviewAnd(half2(0.0h, FarviewShown(positionWS)), FarviewSwapWindow()), positionCS);
}

// where something with nothing left to show sends its vertices: one point outside the view, so no triangle has area
float4 FarviewGone() { return float4(2.0, 2.0, 2.0, 1.0); }

#endif
