using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Grinding the lift cables, and riding the launch ramps that reach them (LiftRamp.cs). Every tuning number is
    /// in CableTune.
    ///
    /// It is the game's own grind, on a line the lifts supply: while on a cable the rider is Grinding, and its
    /// GrindFeature is a stand-in rail a metre long, laid along the cable under the board and moved with it. So
    /// everything that already knows about rails does the right thing ten metres up without being told: the
    /// boardslide pose and the arms out, the rail's sound, no snow trail or spray, no crevasse underfoot, the
    /// cameras. A stand-in's id is negative, which is how Rider.OnCable tells it from a real rail.
    ///
    ///   1. Getting up there. A lift's ramp is boarded like a park box: ride into its foot, lined up, and it
    ///      carries you up its curve and throws you off the lip along the cable's own line.
    ///   2. Boarding. A rider in the air, going down the line within 45 degrees, whose board comes within the
    ///      catch distance of either cable, is put on it (not on the last span, which runs into the bottom
    ///      station, nor right beside a tower).
    ///   3. Sliding. Gravity along the cable does the work, less a little friction and the air: faster down into
    ///      the sag, slower up toward the tower. The cable dips under the rider and bounces when landed on.
    ///   4. Leaving. Just before the tower the rider is thrown up and a little outward, clear of the crossarm
    ///      and the sheaves. A pop hops off anywhere (higher with a Super Jump).
    ///   5. The chain. A span pays as it is slid (CableTune.SpanPoints on, PointsPerMetre after: Rider.GrindTick);
    ///      land on the next span, or the other cable, and the game's combo carries on (Combo.cs), with whatever
    ///      was spun or grabbed on the way between them.
    ///   6. Chairs. On the cable the board skims over each chair's grip: a tick and a few points, never a crash.
    ///      (SkiLift asks ChairsHit; chairs stay solid for anyone who is not grinding or fresh off a ramp.)
    ///
    /// Rides: the snowboard and skis grind. The toboggan cannot balance on a rope any more than on a rail: it
    /// clatters off it, slowed and wobbling, for a few points. The snow tube takes neither ramp nor cable, as it
    /// takes no rail. A cable counts as a rail for the day's missions and plays the rail's sound.
    /// </summary>
    public static class CableGrind
    {
        enum Mode { None, Ramp, Cable }
        static Mode mode;
        static LiftLine.Lift lift;
        static int span;                 // the span being ridden (or the ramp's)
        static float side;               // which cable, +1 or -1
        static float along;              // where the board is: m along the line (cable), or down the ramp
        static float speed, slid, landedAt;
        static Vector3 offset;           // from the line to where the board came on: eased away over a few frames
        static int chain;                // spans ridden in a row without touching snow
        static int lastKey = int.MinValue, bonkKey = int.MinValue, chairs;
        static float graceUntil;
        static bool offRamp;             // in the air off a ramp: chairs cannot hit until the snow (or a cable)
        static Rider hooked;

        /// <summary>The span in hand: for SkiLift, which bends that span's cable under the rider.</summary>
        public static bool Riding(out LiftLine.Lift l, out int i, out float s, out float atAlong, out float dip)
        {
            l = lift; i = span; s = side; atAlong = along; dip = 0f;
            if (mode != Mode.Cable || lift == null) return false;
            float u = Mathf.InverseLerp(lift.nodeS[span], lift.nodeS[span + 1], along), age = Time.time - landedAt;
            dip = CableTune.DipDepth * 4f * u * (1f - u) + CableTune.DipBounce * Mathf.Exp(-age * 3.5f) * Mathf.Cos(age * 11f) * Mathf.Clamp01(8f * u * (1f - u));
            return true;
        }

        /// <summary>May a chair knock this rider down just now? Not on a cable, just off one, or in the air off a ramp.</summary>
        public static bool ChairsHit => mode == Mode.None && !offRamp && Time.time > graceUntil;

        /// <summary>Follow this rider: touching snow, or a crash, ends a chain. (SkiLift calls it once.)</summary>
        public static void Hook(Rider r)
        {
            if (r == null || hooked == r) return;
            hooked = r;
            r.Landed += impact => { chain = 0; offRamp = false; lastKey = int.MinValue; };
            r.Crash += why => { chain = 0; offRamp = false; mode = Mode.None; lastKey = int.MinValue; };
            Reset();
        }

        /// <summary>A new run or a new mountain: nothing in hand.</summary>
        public static void Reset()
        {
            mode = Mode.None; lift = null; chain = 0; offRamp = false; graceUntil = 0f;
            lastKey = int.MinValue; bonkKey = int.MinValue;
        }

        static int Key(LiftLine.Lift l, int i, float s) => l.id * 1000 + i * 2 + (s > 0f ? 1 : 0);

        /// <summary>The stand-in rail under the board: a metre of the line, which is all the pose and the sounds ask of it.</summary>
        static Course.Feature Feature(Vector3 at, Vector3 dir, bool cable, float above) => new Course.Feature
        {
            kind = cable ? Course.Kind.Rail : Course.Kind.Box, a = at, b = at + dir, halfWidth = cable ? 0.04f : CableTune.RampHalfWidth,
            height = above, id = cable ? -1 : -2,
        };

        // ---------------------------------------------------------------- getting on

        /// <summary>
        /// Rider.Tick asks this each step it is neither crashed nor on a rail: is the board at a ramp's foot, or
        /// at a cable? If so the rider is put on it (and Step carries on from there).
        /// </summary>
        public static bool TryBoard(Rider r, Vector3 p, Vector3 v)
        {
            if (!LiftLine.On) return false;
            if (mode != Mode.None) mode = Mode.None;   // (left it some other way: a crash, a reset)
            var flat = new Vector2(v.x, v.z);
            float flatSpeed = flat.magnitude;
            foreach (var l in LiftLine.Lifts)
            {
                if (p.z < l.ZMin - 5f || p.z > l.ZMax + 5f) continue;
                float s = (p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z;
                float c = (p.x - l.top.x) * l.across.x + (p.z - l.top.z) * l.across.z;
                float down = (v.x * l.dir.x + v.z * l.dir.z);

                if (r.Tube || flatSpeed < CableTune.CatchMinSpeed) continue;
                if (down < CableTune.CatchAlign * flatSpeed) continue;   // across the line, or up it
                if (Mathf.Abs(c) > LiftLine.Half + CableTune.CatchSide + 0.5f) continue;

                // its ramp: ridden into at the foot, on the snow (or just off it), like a park box
                var ramp = l.ramp;
                if (ramp != null && (!r.Airborne || r.AboveGround < 0.6f) && down >= CableTune.RampMinSpeed
                    && s > ramp.s0 - 1.5f && s < ramp.s0 + ramp.length * 0.35f && Mathf.Abs(c - ramp.side * LiftLine.Half) < CableTune.RampHalfWidth + 0.5f)
                {
                    float t = Mathf.Max(0f, s - ramp.s0);
                    Vector3 on = LiftLine.RampPoint(l, ramp, t) + Vector3.up * Rider.R;
                    if (p.y < on.y + 1.2f)
                    {
                        mode = Mode.Ramp; lift = l; span = ramp.span; side = ramp.side; along = t;
                        speed = Mathf.Max(down, CableTune.RampMinSpeed); offset = p - on; slid = 0f;
                        r.CableBoard(Feature(on, LiftLine.RampDir(l, ramp, t), false, 0f), false);
                        return true;
                    }
                }

                // a cable: only from the air
                if (!r.Airborne) continue;
                for (int e = -1; e <= 1; e += 2)
                {
                    if (Mathf.Abs(c - e * LiftLine.Half) > CableTune.CatchSide) continue;
                    int i = LiftLine.SpanAt(l, s);
                    int key = Key(l, i, e);
                    if (key == lastKey) continue;   // (the span just left: no hopping straight back onto it)
                    if (!LiftLine.InCatch(l, i, s, p.y - Rider.R, down, v.y)) continue;
                    float slope = LiftLine.CableSlope(l, i, s);
                    Vector3 dir = (l.dir + Vector3.up * slope).normalized;
                    Vector3 on = l.At(s, e * LiftLine.Half, l.CableY(i, s)) + Vector3.up * Rider.R;
                    if (!r.Feel.Rails)
                    {
                        // a toboggan cannot balance on a rope: it clatters off it, once, and falls away
                        if (key == bonkKey) continue;
                        bonkKey = key;
                        r.Breakthrough(CableTune.BonkKeep, 0.5f, 0f, 99f, "");
                        r.Score("Cable Bonk", CableTune.BonkPoints, LinkKind.Bonk);
                        graceUntil = Time.time + CableTune.ChairGrace;
                        continue;
                    }
                    mode = Mode.Cable; lift = l; span = i; side = e; along = s;
                    speed = Mathf.Max(Vector3.Dot(v, dir), CableTune.CatchMinSpeed); offset = p - on; slid = 0f;
                    landedAt = Time.time; offRamp = false;
                    lastKey = key;
                    chairs = ChairsBehind();
                    r.CableBoard(Feature(on, dir, true, on.y - Slope.DrawnHeight(on.x, on.z)), true);
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- on it

        /// <summary>One physics step on a ramp or a cable: the velocity that puts the board where it should be next.</summary>
        public static Vector3 Step(Rider r, float dt, Vector3 p, Vector3 v)
        {
            if (lift == null || mode == Mode.None) return r.CableLeave(p, v);
            bool pop = r.TakePop(out bool super);
            float hop = CableTune.PopHop * (super ? Mathf.Sqrt(Rider.SuperJumpLift) : 1f);
            offset *= Mathf.Exp(-dt * 12f);

            if (mode == Mode.Ramp)
            {
                var ramp = lift.ramp;
                Vector3 d = LiftLine.RampDir(lift, ramp, along);
                speed = Mathf.Max(CableTune.MinSpeed, speed + LiftLine.SlideAccel(speed, d.y) * dt);
                along += speed * Mathf.Sqrt(Mathf.Max(0f, 1f - d.y * d.y)) * dt;
                if (along >= ramp.length || pop)
                {
                    // off the lip, along the cable's own line (a pop on the ramp adds its hop to that)
                    mode = Mode.None; offRamp = true;
                    d = LiftLine.RampDir(lift, ramp, Mathf.Min(along, ramp.length));
                    return r.CableLeave(p, d * speed + (pop ? Vector3.up * hop : Vector3.zero));
                }
                Vector3 at = LiftLine.RampPoint(lift, ramp, along) + Vector3.up * Rider.R;
                r.CableRide(Feature(at, d, false, 0f), speed, at.y - Slope.DrawnHeight(at.x, at.z));
                return (at + offset - p) / dt;
            }

            // a cable
            float slope = LiftLine.CableSlope(lift, span, along);
            Vector3 dir = (lift.dir + Vector3.up * slope).normalized;
            speed = Mathf.Max(CableTune.MinSpeed, speed + LiftLine.SlideAccel(speed, dir.y) * dt);
            float step = speed * dt;
            slid += step;
            along += step / Mathf.Sqrt(1f + slope * slope);
            // each chair's grip the board skims over: a tick and a few points
            int behind = ChairsBehind();
            if (behind != chairs) { chairs = behind; r.Score("Chair Hop", CableTune.ChairPoints, LinkKind.Chair); ChairTick?.Invoke(p); }
            float end = lift.nodeS[span + 1];
            bool tower = along >= end - speed * CableTune.HopLead / Mathf.Sqrt(1f + slope * slope);
            if (tower || pop)
            {
                // (the span is paid by the rider as it is slid, metre by metre, and each span in a row is another link in
                // the game's combo: Rider.GrindTick, Combo. The old "x2 .. x5" on the span's own points is gone)
                chain++;
                mode = Mode.None;
                graceUntil = Time.time + CableTune.ChairGrace;
                // thrown up over the tower's head, and a little outward: the next span has to be steered back onto
                Vector3 leave = dir * speed + Vector3.up * (tower ? CableTune.TowerHop : hop);
                // (but at the tower on a gorge's rim, straight on: the gorge span is there to be ridden, and it falls away
                // so steeply that a rider nudged off its line would have no chance to steer back on before the drop)
                if (tower && span + 1 != lift.gorge) leave += lift.across * (side * CableTune.TowerNudge);
                return r.CableLeave(p, leave);
            }
            Riding(out _, out _, out _, out _, out float dip);
            Vector3 on = lift.At(along, side * LiftLine.Half, lift.CableY(span, along) - dip) + Vector3.up * Rider.R;
            r.CableRide(Feature(on, dir, true, 0f), speed, on.y - Slope.DrawnHeight(on.x, on.z));
            return (on + offset - p) / dt;
        }

        /// <summary>A chair's grip has just gone under the board (SkiLift plays the clack there).</summary>
        public static System.Action<Vector3> ChairTick;

        /// <summary>
        /// How many chairs on this cable are behind the board (up the loop from it): the chairs sit every
        /// `spacing` round the loop and the loop moves at LiftLine.Speed, so the count steps by one each time a
        /// chair and the board pass, whichever way that chair is going.
        /// </summary>
        static int ChairsBehind()
        {
            int n = lift.Spans;
            float u = Mathf.InverseLerp(lift.nodeS[span], lift.nodeS[span + 1], along);
            // where the board is round the loop: the up-going side runs from each span's lower node to its upper
            int piece = side > 0f ? n - span : n + 4 + span;
            float len = lift.cum[piece + 1] - lift.cum[piece];
            double at = lift.cum[piece] + (side > 0f ? 1f - u : u) * len;
            return (int)System.Math.Floor((at - Time.timeAsDouble * LiftLine.Speed) / lift.spacing);
        }
    }
}
