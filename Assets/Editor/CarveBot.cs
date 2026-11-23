using UnityEngine;
using Summit;

/// <summary>
/// The test tools' rider (slide: the finger is a push sideways across the fall line, and a held drag is a sideways
/// speed). It steers by the sideways speed it wants: toward the line at `rate` per second (no faster than `most` m/s),
/// with the line's own swing down the hill fed in, asked of the finger as the share of a whole drag that gives it.
/// </summary>
public static class CarveBot
{
    /// <summary>The finger that takes the board onto the line x = line(z) (looked at `ahead` m down the hill).</summary>
    public static float Onto(Rider r, System.Func<float, float> line, float rate = 1.5f, float most = 10f, float ahead = 0f)
    {
        Vector3 v = r.Velocity; float z = r.Z + ahead;
        float want = line(z), swing = (line(z + 1f) - want) * v.z;
        return ForSide(r, Mathf.Clamp((want - r.X) * rate, -most, most) + swing);
    }

    /// <summary>The finger for going sideways over the map at `side` m/s (+x): the way that is to be going, as a sideways
    /// speed across the fall line under the board, as a share of what a whole drag gives at this speed; with the little
    /// more it takes to hold that against gravity across the path. In the air: the drift toward it.</summary>
    public static float ForSide(Rider r, float side)
    {
        Vector3 v = r.Velocity; float flat = Mathf.Sqrt(v.x * v.x + v.z * v.z);
        float full = Mathf.Max(0.5f, r.SideFull(r.Speed));
        if (flat < 1f) return Mathf.Clamp(side / full, -1f, 1f);
        // (in the air the finger only drifts the flight, AirDrift at the whole drag: ask for the drift that closes on
        // the sideways speed wanted in about a second, no more)
        if (r.Airborne) return Mathf.Clamp((side - v.x) / Mathf.Max(1f, Rider.AirDrift) / Mathf.Clamp(Rider.TurnScale, 0.25f, 4f), -1f, 1f);
        float way = Mathf.Atan2(side, Mathf.Max(v.z, 1f));
        float across = flat * Mathf.Sin(way - r.FallLine) - (r.Airborne ? 0f : r.SideSlope * Rider.PushTime);
        return Mathf.Clamp(across / full / Mathf.Clamp(Rider.TurnScale, 0.25f, 4f), -1f, 1f);
    }
}
