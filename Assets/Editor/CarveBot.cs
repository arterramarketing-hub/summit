using UnityEngine;
using Summit;

/// <summary>
/// The test tools' rider, carving (carve: the finger is an edge angle, and an edge is a pull across the way the board
/// goes; a bot that steers by how far it is off its line alone swings wider each time). This one steers by the
/// sideways speed it wants: toward the line at `rate` per second (no faster than `most` m/s), with the line's own
/// swing down the hill fed in; it asks its edge for the pull that closes on that speed at `quick` per second, and
/// for the pull that holds the board against gravity across its path (an edge into the hill).
/// </summary>
public static class CarveBot
{
    /// <summary>The finger that carves the board onto the line x = line(z) (looked at `ahead` m down the hill).</summary>
    public static float Onto(Rider r, System.Func<float, float> line, float rate = 1.5f, float most = 10f, float ahead = 0f, float quick = 6f)
    {
        Vector3 v = r.Velocity; float z = r.Z + ahead;
        float want = line(z), swing = (line(z + 1f) - want) * v.z;
        float side = Mathf.Clamp((want - r.X) * rate, -most, most) + swing;
        return ForPull(r, quick * (side - v.x) - (r.Airborne ? 0f : r.SideSlope));
    }

    /// <summary>The finger that asks the edge for this pull across the way the board is going (m/s^2, + right), on
    /// corduroy at the speed it is going: the arc a carve of that pull is, as a share of the throw (Rider.ArcAt, inverted).</summary>
    public static float ForPull(Rider r, float pull)
    {
        float sp = Mathf.Max(4f, r.Speed);
        float knee = r.ArcAt(Rider.EdgeKnee, sp);   // (the arc at the knee of the throw: all the snow holds)
        float k = Mathf.Min(Mathf.Abs(pull) / (sp * sp) / Mathf.Max(knee, 1e-5f), Rider.SkidOver);
        float share = k <= 1f ? Mathf.Atan(k * Mathf.Tan(Rider.MaxLean)) / Rider.MaxLean * Rider.EdgeKnee
                              : Rider.EdgeKnee + (k - 1f) / (Rider.SkidOver - 1f) * (1f - Rider.EdgeKnee);
        return Mathf.Clamp(Mathf.Sign(pull) * share / Mathf.Clamp(Rider.TurnScale, 0.25f, 4f), -1f, 1f);
    }
}
