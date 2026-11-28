using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The snow on the hamlets' roofs, ridden like the mountain's own. A rider who comes down onto a roof lands on it by
    /// the ordinary landing rules, rides across it on its own pitch (the chalet's and the lodge's ridges are a box to
    /// slide along) and leaves it off the eave or the gable's end as off any lip.
    ///
    /// Each roof is the drawn snow's top as Tools/MakeCabins.py lays it (the pitch, the ridge, the slab's thickness), in
    /// the building's own frame: x across, z out of the front. Under() answers for the board where its centre is: over
    /// a roof, and not underneath it (a board whose middle is already below the roof's top is beside or under the eaves:
    /// the roof is not its ground, the solid is). That is the fair line between the two: coming down onto the roof is
    /// a landing; coming at it from below, or into the gable, is a hit.
    ///
    /// The solid (Hamlets builds it from Solids()) stands Margin under the drawn snow and Inset in from its edges, so a
    /// board riding on the snow never touches it, and a board just past the edge, whose centre is not over the roof,
    /// falls by without catching it. The walls stop at the eaves; above them the gables and the roof's slab are solid
    /// up to just under the snow.
    ///
    /// A lift station's deck is ridden the same way (Deck, registered by SkiLift while the station is solid): level,
    /// open at both ends, a bank of snow eased up to whichever end meets the snow (rampFront, rampBack), and off the
    /// other end wherever the mountain has fallen away under it. SkiLift makes its parts solid as they are drawn.
    /// </summary>
    public static class Roofs
    {
        // a board whose bottom is this far under the roof's top (its middle level with the snow's top: on the eave's very
        // edge, or a step up from the snow beside it) is still put onto it; any lower, it is beside or under the eave
        public const float Tol = Rider.R;
        public const float Margin = 0.35f;  // the roof's solid stands this far under the drawn snow (below Tol: never touched from on top)
        public const float Inset = 0.25f;   // and this far in from the drawn snow's edge
        public const float Lip = 0.5f;      // the eave's lip: out to the roof's own edge (its boards or metal), this far under the snow's top (0.3 thick)
        // the ridden roof reaches this far past the roof's drawn edge: a board whose middle is any nearer would catch the
        // lip with its side, so it comes down on the edge instead
        public const float Reach = Rider.R + 0.1f;
        public const float Slab = 0.5f;     // the solid's thickness under the overhang
        /// <summary>Ridge features (a box along the top) are numbered from here, clear of the course's own and of the
        /// lifts' cables (negative).</summary>
        public const int IdBase = Course.RoofIdBase;

        /// <summary>One building's roof, in its own frame (from Tools/MakeCabins.py: keep in step with the models).</summary>
        public class Shape
        {
            public bool gable;                 // two pitches falling either way from a ridge along z at x = 0
            public float a, gx, gz, flat;      // the snow's top: a - gx * max(|x|, flat) (gable), or a + gx x + gz z
            public float x0, x1, z0, z1;       // the drawn snow's top, its extents
            public float px0, px1, pz0, pz1;   // the roof's own edge (the planks or the metal under the snow, out past it)
            public float wx, wz0, wz1, wallTop;   // the walls: |x| < wx, wz0 < z < wz1, up to wallTop
            public Vector4[] stacks;           // chimneys and flues through it: x, z, half width, top
            public float reach = Reach;        // how far past the roof's edge a board's middle still comes down on it (Reach)
            public string label = "Rooftop";   // the combo link for getting up there (none if points is 0)
            public int points = -1;            // its points (-1: Rider.RoofPoints)
            public bool banked;                // snow may be banked against its ends (a lift's unload wing, as its Deck: SkiLift.Drifts)
            // (liftlook) a lift's unload wing: snow banked against its sides too, down to the snow at ApronSlope, how far
            // out at each RampStep along it from z0 (null, or 0: none there; its edge is a drop)
            public float[] apronOut, apronIn;
            public float Top(float x, float z) => gable ? a - gx * Mathf.Max(Mathf.Abs(x), flat) : a + gx * x + gz * z;
            public Vector3 Normal(float x)
            {
                if (gable) return Mathf.Abs(x) <= flat ? Vector3.up : new Vector3(Mathf.Sign(x) * gx, 1f, 0f).normalized;
                return new Vector3(-gx, 1f, -gz).normalized;
            }
        }

        static float Tan(float deg) => Mathf.Tan(deg * Mathf.Deg2Rad);
        // the snow slab's top over the plane its corners sit on: th along the normal of a pitch at this slope
        static float Lift(float th, float slope) => th * Mathf.Sqrt(1f + slope * slope);

        static Dictionary<string, Shape> shapes;

        /// <summary>The roof of a kind of building, or null (the car deck, the lamp post).</summary>
        public static Shape Of(string kind)
        {
            if (shapes == null) Make();
            return shapes.TryGetValue(kind, out var s) ? s : null;
        }

        static void Make()
        {
            shapes = new Dictionary<string, Shape>();
            // the chalet: W 5.5, D 3.75, eave 5.3, pitch 22; the roof's underside RIDGE + 0.12 - |x| tan, a 0.2 plank deck,
            // 0.5 of snow on it; eaves 1.3 out at the sides, 1.9 in front, 1.1 behind; a stone chimney
            {
                float t = Tan(22f), ridge = 5.3f + 5.5f * t;
                shapes["Chalet"] = new Shape
                {
                    gable = true, a = ridge + 0.12f + 0.2f + Lift(0.5f, t), gx = t, flat = 0.3f,
                    x0 = -6.6f, x1 = 6.6f, z0 = -4.45f, z1 = 5.25f, px0 = -6.8f, px1 = 6.8f, pz0 = -4.85f, pz1 = 5.65f,
                    wx = 5.65f, wz0 = -3.9f, wz1 = 3.9f, wallTop = 5.3f,
                    stacks = new[] { new Vector4(2.65f, -1.15f, 0.48f, ridge + 1.25f) },
                };
            }
            // the lodge: W 4, D 3.25, eave 8.7, pitch 10; metal 0.12, snow 0.42; eaves 0.7, 1.7 in front, 0.5 behind; a flue
            {
                float t = Tan(10f), ridge = 8.7f + 4f * t;
                shapes["Lodge"] = new Shape
                {
                    gable = true, a = ridge + 0.05f + 0.12f + Lift(0.42f, t), gx = t, flat = 0.3f,
                    x0 = -4.45f, x1 = 4.45f, z0 = -3.4f, z1 = 4.6f, px0 = -4.7f, px1 = 4.7f, pz0 = -3.75f, pz1 = 4.95f,
                    wx = 4.0f, wz0 = -3.25f, wz1 = 3.25f, wallTop = 8.7f,
                    stacks = new[] { new Vector4(-2.2f, -1.2f, 0.25f, ridge + 1.3f) },
                };
            }
            // the cabins: a floor of 2.75 (H = floors x 2.75 + 0.15), the roof 0.3 over every wall (0.6 in front),
            // metal 0.14 and 0.55 of snow on it; flat, or a single pitch rising 0.2 to the metre
            shapes["CabinA"] = Cabin(2.3f, 2.3f, 1, 0f, 0f, 2.3f, null);
            shapes["CabinB"] = Cabin(2.3f, 2.5f, 2, 0f, 0.2f, 2.5f, new Vector4(-1.4f, -1.6f, 0.25f, 5.65f + 1.0f + 1.3f));
            shapes["CabinC"] = Cabin(2.9f, 2.1f, 1, 0.2f, 0f, 2.1f, new Vector4(-2.0f, -1.2f, 0.25f, 2.9f + 1.16f + 1.3f));
            shapes["CabinD"] = Cabin(2.1f, 2.1f, 2, 0f, 0f, 2.55f, null);
        }

        static Shape Cabin(float w, float d, int floors, float gx, float gz, float zf, Vector4? stack)
        {
            float H = floors * 2.75f + 0.15f, slope = Mathf.Max(gx, gz);
            // the underside: H at the low edge (x = -w, or z = -d), rising gx (gz) to the metre from there
            float a = H + gx * w + gz * d + 0.14f + Lift(0.55f, slope);
            return new Shape
            {
                gable = false, a = a, gx = gx, gz = gz,
                x0 = -w - 0.05f, x1 = w + 0.05f, z0 = -d - 0.05f, z1 = zf + 0.3f, px0 = -w - 0.3f, px1 = w + 0.3f, pz0 = -d - 0.3f, pz1 = zf + 0.6f,
                wx = w + 0.1f, wz0 = -d - 0.1f, wz1 = zf + 0.1f, wallTop = H,
                stacks = stack.HasValue ? new[] { stack.Value } : new Vector4[0],
            };
        }

        // ---------------------------------------------------------------- the roofs standing now

        /// <summary>A roof set down on the mountain: its shape, where, which way it faces, and its ridge (a box).</summary>
        public class Roof
        {
            public Shape s; public Vector3 o; public float c, sn, r2;
            public bool hasRidge; public Course.Feature ridge;
            public string name; public Transform t;
            // snow banked against a level deck's ends (a lift station's: SkiLift), ridden up onto it: how far out each
            // reaches past the end (z1, z0) at each RampStep across from x0; null where there is none
            public float[] rampFront, rampBack;
            public float RampLen(float[] ramp, float lx)
            {
                if (ramp == null) return 0f;
                float f = Mathf.Clamp((lx - s.x0) / RampStep, 0f, ramp.Length - 1.001f);
                int i = (int)f;
                // (no ramp at either sample: none between them)
                return ramp[i] <= 0f || ramp[i + 1] <= 0f ? 0f : Mathf.Lerp(ramp[i], ramp[i + 1], f - i);
            }
            public Vector2 Local(float x, float z) { float dx = x - o.x, dz = z - o.z; return new Vector2(dx * c - dz * sn, dx * sn + dz * c); }
            public Vector3 World(float lx, float y, float lz) => new Vector3(o.x + lx * c + lz * sn, o.y + y, o.z - lx * sn + lz * c);
            public Vector3 WorldDir(Vector3 l) => new Vector3(l.x * c + l.z * sn, l.y, -l.x * sn + l.z * c);
        }

        static readonly List<Roof> live = new List<Roof>();
        static int nextId;

        /// <summary>The roofs standing (for the tests).</summary>
        public static IReadOnlyList<Roof> Live => live;

        /// <summary>A building's roof is up: t is the building (its floor, its yaw).</summary>
        public static Roof Add(Shape s, Transform t, string name)
        {
            float yaw = t.eulerAngles.y * Mathf.Deg2Rad;
            var r = new Roof { s = s, o = t.position, c = Mathf.Cos(yaw), sn = Mathf.Sin(yaw), name = name, t = t };
            float ex = Mathf.Max(-s.px0, s.px1) + 1f + (s.banked ? 4.5f : 0f), ez = Mathf.Max(-s.pz0, s.pz1) + 1f + (s == Deck || s.banked ? RampMax : 0f);
            r.r2 = ex * ex + ez * ez;
            if (s.gable)
            {
                float zA = s.z0 + 0.3f, zB = s.z1 - 0.3f, y = s.Top(0f, 0f);
                r.hasRidge = true;
                r.ridge = new Course.Feature { kind = Course.Kind.Box, a = r.World(0f, y, zA), b = r.World(0f, y, zB), halfWidth = 0.35f, height = 0f, id = IdBase + (nextId++ & 0xFFFFF) };
            }
            live.Add(r);
            return r;
        }

        public static void Remove(Roof r) { live.Remove(r); }

        /// <summary>
        /// The roof under a board whose centre is at (x, refY, z): over a roof's snow (or within Reach of its edge) and not
        /// already more than Tol under its top. y is the snow's top there, n its normal.
        /// </summary>
        public static bool Under(float x, float z, float refY, out float y, out Vector3 n, out Roof roof)
        {
            y = 0f; n = Vector3.up; roof = null;
            if (live.Count == 0) return false;
            for (int i = 0; i < live.Count; i++)
            {
                var r = live[i];
                float dx = x - r.o.x, dz = z - r.o.z;
                if (dx * dx + dz * dz > r.r2) continue;
                var l = r.Local(x, z);
                var s = r.s;
                float top; Vector3 ln;
                if (l.x < s.px0 - s.reach || l.x > s.px1 + s.reach)
                {
                    // (a wing's side banked down to the snow: liftlook)
                    if (s.apronOut == null || l.y < s.z0 || l.y > s.z1) continue;
                    bool outer = l.x > s.x1;
                    float d = outer ? l.x - s.x1 : s.x0 - l.x, len = Apron(outer ? s.apronOut : s.apronIn, s, l.y);
                    if (len <= 0f || d > len) continue;
                    top = r.o.y + s.Top(outer ? s.x1 : s.x0, l.y) - ApronSlope * d;
                    ln = new Vector3(outer ? ApronSlope : -ApronSlope, 1f, 0f).normalized;
                }
                else if (l.y >= s.pz0 - s.reach && l.y <= s.pz1 + s.reach)
                {
                    top = r.o.y + s.Top(Mathf.Clamp(l.x, s.x0, s.x1), Mathf.Clamp(l.y, s.z0, s.z1));
                    ln = s.Normal(l.x);
                }
                else
                {
                    // on the snow banked against an end: down from the deck's level to the snow at RampSlope
                    bool front = l.y > s.z1;
                    float d = front ? l.y - s.z1 : s.z0 - l.y, len = r.RampLen(front ? r.rampFront : r.rampBack, l.x);
                    if (len <= 0f || d > len) continue;
                    top = r.o.y + s.Top(Mathf.Clamp(l.x, s.x0, s.x1), front ? s.z1 : s.z0) - RampSlope * d;
                    ln = new Vector3(0f, 1f, front ? RampSlope : -RampSlope).normalized;
                }
                if (refY - Rider.R < top - Tol) continue;
                if (s.banked && Slope.DrawnHeight(x, z) >= top) continue;   // (a lift's unload wing where the snow comes up over it: the snow is the ground)
                if (roof != null && top < y) continue;   // (two roofs: the higher one)
                y = top; n = r.WorldDir(ln); roof = r;
            }
            return roof != null;
        }

        public const float RampStep = 0.5f, RampSlope = 0.3f, RampMax = 4f;
        /// <summary>A lift's unload wing's sides, banked down to the snow (35 degrees: snow as it lies piled).</summary>
        public const float ApronSlope = 0.7f;

        /// <summary>How far out a wing's side bank reaches at z (along it, from its z0, RampStep apart).</summary>
        static float Apron(float[] a, Shape s, float z)
        {
            if (a == null || a.Length < 2) return 0f;
            float f = Mathf.Clamp((z - s.z0) / RampStep, 0f, a.Length - 1.001f);
            int i = (int)f;
            return a[i] <= 0f || a[i + 1] <= 0f ? 0f : Mathf.Lerp(a[i], a[i + 1], f - i);
        }

        /// <summary>A lift station's deck (SkiLift): level, its top at the transform, x across (railings along both
        /// sides), z along the line (both ends open). No points of its own: the air off it is what scores.</summary>
        public static readonly Shape Deck = new Shape
        {
            gable = false, a = 0f,
            x0 = -LiftLine.StationHalfWid, x1 = LiftLine.StationHalfWid, z0 = -LiftLine.StationHalfLen, z1 = LiftLine.StationHalfLen,
            px0 = -LiftLine.StationHalfWid, px1 = LiftLine.StationHalfWid, pz0 = -LiftLine.StationHalfLen, pz1 = LiftLine.StationHalfLen,
            wx = LiftLine.StationHalfWid, wz0 = -LiftLine.StationHalfLen, wz1 = LiftLine.StationHalfLen, wallTop = 0f,
            stacks = new Vector4[0], label = "Lift deck", points = 0,
        };

        /// <summary>The ridges near p, as boxes to slide along, into `into` (cleared first: no garbage each physics step).</summary>
        public static void RidgesNear(Vector3 p, List<Course.Feature> into)
        {
            into.Clear();
            for (int i = 0; i < live.Count; i++)
            {
                var r = live[i];
                if (!r.hasRidge) continue;
                float dx = p.x - r.o.x, dz = p.z - r.o.z;
                if (dx * dx + dz * dz > r.r2 + 9f) continue;
                into.Add(r.ridge);
            }
        }

        /// <summary>Is there a roof within d (m) of p, across the snow?</summary>
        public static bool Near(Vector3 p, float d)
        {
            for (int i = 0; i < live.Count; i++)
            {
                float dx = p.x - live[i].o.x, dz = p.z - live[i].o.z;
                float rr = Mathf.Sqrt(live[i].r2) + d;
                if (dx * dx + dz * dz < rr * rr) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- what is solid

        /// <summary>
        /// The solid of a roof, in the building's frame: convex blocks (eight corners each: bottom four, then top four,
        /// round the same way) for the gables over the walls and the slab under the snow out to the eaves, each standing
        /// Margin under the drawn snow and Inset in from its edge; and the chimneys, as boxes (centre, size).
        /// </summary>
        public static void Solids(Shape s, List<Vector3[]> blocks, List<Bounds> boxes)
        {
            float M = Margin;
            Vector3[] Block(float xa, float xb, float za, float zb, System.Func<float, float, float> bottom)
            {
                var v = new Vector3[8];
                float[] xs = { xa, xb, xb, xa }, zs = { za, za, zb, zb };
                for (int i = 0; i < 4; i++)
                {
                    float top = s.Top(xs[i], zs[i]) - M;
                    v[i] = new Vector3(xs[i], Mathf.Min(bottom(xs[i], zs[i]), top - 0.05f), zs[i]);
                    v[i + 4] = new Vector3(xs[i], top, zs[i]);
                }
                return v;
            }
            float sx0 = s.x0 + Inset, sx1 = s.x1 - Inset, sz0 = s.z0 + Inset, sz1 = s.z1 - Inset;
            // (the gables over the walls, but never out past the slab: a cabin's roof barely overhangs its walls, and a
            // board falling by just past its snow must not catch the top of the wall under it)
            float wx = Mathf.Min(s.wx, sx1), wz0 = Mathf.Max(s.wz0, sz0), wz1 = Mathf.Min(s.wz1, sz1);
            if (s.gable)
            {
                // each pitch on its own (a block with the ridge in it would not be convex)
                foreach (float side in new[] { -1f, 1f })
                {
                    float wa = side < 0f ? -wx : 0f, wb = side < 0f ? 0f : wx;
                    blocks.Add(Block(wa, wb, wz0, wz1, (x, z) => s.wallTop - 0.05f));
                    float ea = side < 0f ? sx0 : 0f, eb = side < 0f ? 0f : sx1;
                    blocks.Add(Block(ea, eb, sz0, sz1, (x, z) => s.Top(x, z) - M - Slab));
                    ea = side < 0f ? s.px0 : 0f; eb = side < 0f ? 0f : s.px1;
                    blocks.Add(LipBlock(ea, eb, s.pz0, s.pz1));
                }
            }
            else
            {
                blocks.Add(Block(-wx, wx, wz0, wz1, (x, z) => s.wallTop - 0.05f));
                blocks.Add(Block(sx0, sx1, sz0, sz1, (x, z) => s.Top(x, z) - M - Slab));
                blocks.Add(LipBlock(s.px0, s.px1, s.pz0, s.pz1));
            }
            // the eave's lip: under the snow out to its very edge, so a board coming up into the eave from below (or in
            // from the side, under the top) hits it, never slips up through it onto the roof
            Vector3[] LipBlock(float xa, float xb, float za, float zb)
            {
                var v = new Vector3[8];
                float[] xs = { xa, xb, xb, xa }, zs = { za, za, zb, zb };
                for (int i = 0; i < 4; i++)
                {
                    float top = s.Top(Mathf.Clamp(xs[i], s.x0, s.x1), Mathf.Clamp(zs[i], s.z0, s.z1)) - Lip;
                    v[i] = new Vector3(xs[i], top - 0.3f, zs[i]);
                    v[i + 4] = new Vector3(xs[i], top, zs[i]);
                }
                return v;
            }
            foreach (var st in s.stacks)
            {
                float lo = s.Top(st.x, st.y) - 1f;
                boxes.Add(new Bounds(new Vector3(st.x, (lo + st.w) * 0.5f, st.y), new Vector3(st.z * 2f, st.w - lo, st.z * 2f)));
            }
        }
    }
}
