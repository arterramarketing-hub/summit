using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The Summit lift's top. (lifttop2, the user: "the lift moved further down to the same level as the main VERTIGO
    /// sign... a connecting slope of snow from the platform cus people would just fall off".) The station stands at the
    /// camp's front left, beside the gate, its deck flush with a pad of snow laid at the gate's own level under MT.
    /// VERTIGO's board (LiftLine: DeckStep; no deck edge, frame or legs show). The pad's edges are banks of snow out to
    /// the mountain as it lies, roped (PisteKit) where they fall away; under the line, past the deck's downhill end, the
    /// snow falls away from the deck's edge so the chairs coming in stay clear. Off the deck's uphill end two groomed
    /// ways leave the pad's camp side: one across the start's left bank (cut down to it) to the gate and the foot of the
    /// stairs up into the camp (LiftEnds' riders into the camp; the hero, LiftArrive); the other down past the station's
    /// camp side, cut into the bank, into the in-run above the lip (the far side's way down to the run), poled.
    /// Ground in camp terms: a, metres from the run's line at the lip (Slope.Centre(PadLip)), + toward the cliff; z down
    /// the run.
    ///
    /// The shaping is worked out once a mountain onto a grid (Slope.Quad apart, on the terrain's own vertices): for each
    /// point of it, the final height as A * (the mountain's own) + C, how groomed it is, how far it is off the pad and
    /// ways (Covers), how far off the way down (towers) and how thickly trees grow there (Grove). Slope asks Shape for
    /// every height, from any thread; the first to ask lays it out (under a lock) while the others wait.
    /// </summary>
    public static class LiftTop
    {
        // ---------------------------------------------------------------- the design

        class Pad { public Rect r; public float corner; public float H; }
        class Way
        {
            public string name;
            public Vector2[] key;          // its line, a few points (a, z), smoothed through
            public float[] halfW;          // its half width at each key point (the groomed width)
            public float bankK = 1.8f, bankMin = 2.5f, bankMax = 20f;   // its banks: how far out they reach for each metre built up or cut
            public bool follow;            // on the mountain as it lies (smoothed), not a designed fall
            public int smooth = 1;
            public float ease = 14f;
            public bool far;
            public float flatFor;          // (a designed fall: metres it keeps its start's height before falling)
            public List<Vector2> pts = new List<Vector2>(); public List<float> w = new List<float>(), h = new List<float>(), s = new List<float>();
            public float len;
            public Rect box;
        }

        // (lifttop2, the user: "moved further down to the same level as the main VERTIGO sign") the station stands at
        // the camp's front left, beside the gate, on a pad of snow at the gate's own level (the start's in-run there,
        // under MT. VERTIGO's board), its deck flush with it (LiftLine: DeckStep). Off the deck's uphill end two
        // groomed ways leave the pad's camp side: one across the start's left bank (cut down to it) to the gate and the
        // foot of the stairs up into the camp; the other down past the station's camp side, cut into the bank, into the
        // in-run above the lip (the far side's way down to the run). Under the line, past the deck's downhill end, the
        // snow falls away from the deck's edge so the chairs coming in stay clear of it.
        static readonly float[] StationZ = { -34f, -33f, -35f, -37f };
        static readonly float[] StationOff = { -50f, -49f, -51f, -53f };
        /// <summary>The deck's top over the pad's snow: a few centimetres, flush (the deck's frame and legs under the snow).</summary>
        public const float DeckStep = 0.05f;

        static readonly Pad pad = new Pad { r = Rect.MinMaxRect(-61f, -57f, -36f, -25.5f), corner = 6f };
        static readonly Way rideIn = new Way
        {
            name = "the way to the gate",
            key = new[] { new Vector2(-41f, -45f), new Vector2(-34f, -43.5f), new Vector2(-27f, -41.5f), new Vector2(-20f, -40f) },
            halfW = new[] { 7f, 6.5f, 6f, 6f },
            bankK = 1.8f, bankMin = 3f, bankMax = 18f,
        };
        static readonly Way farDown = new Way
        {
            name = "the way down to the run", far = true,
            key = new[] { new Vector2(-39f, -44f), new Vector2(-35f, -36f), new Vector2(-31f, -27f), new Vector2(-26f, -17f), new Vector2(-20f, -9f), new Vector2(-15.5f, -4f) },
            halfW = new[] { 6f, 5.5f, 5.5f, 5.5f, 5f, 5f },
            bankK = 1.8f, bankMin = 3.5f, bankMax = 18f, flatFor = 5f,
        };
        static readonly Vector2[] onTo = { new Vector2(-12f, -43f), new Vector2(Camp.StairX, Camp.StairBottomZ + 1.2f) };   // (on to the stairs' foot, walking)
        static readonly Vector2[] downTo = { new Vector2(-10f, -1f), new Vector2(-6f, 3f) };   // (on into the in-run and over the lip)
        static readonly Way[] Ways = { rideIn, farDown };

        /// <summary>Of those who come off the lift at its top: the share who go up into the camp, and how many may be on their way in at once (the rest ride down the in-run).</summary>
        public const float IntoCampShare = 0.34f;
        public const int IntoCampCap = 3;

        // ---------------------------------------------------------------- laid out for today's mountain

        static readonly object gate = new object();
        static volatile bool ready;
        static volatile int preparedSeed = int.MinValue; static float preparedCx = float.NaN;
        [System.ThreadStatic] static bool raw;   // (this thread is laying it out: the mountain without it)
        static float gx0, gz0; static int gw, gh;
        static float[] gA, gC, gG, gD, gV, gF;
        public static float PreparedMs { get; private set; }

        static bool Ready()
        {
            if (raw) return false;
            if (ready && preparedSeed == Slope.Seed) return true;
            lock (gate)
            {
                if (ready && preparedSeed == Slope.Seed) return true;
                ready = false;
                float cx = Slope.Centre(Slope.PadLip);
                raw = true;
                try { Prepare(cx); }
                finally { raw = false; }
                preparedSeed = Slope.Seed; preparedCx = cx;
                ready = true;
            }
            return true;
        }

        /// <summary>Lay it out now (the main thread, before the terrain's workers ask: LiftLine does, before laying the lifts).</summary>
        public static void Warm() { Ready(); }

        static void Prepare(float cx)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            pad.H = GateLevel(cx);
            Lay(rideIn, cx); Lay(farDown, cx);
            // the grid: over the pad and its ways, with their banks
            float a0 = pad.r.xMin, a1 = pad.r.xMax, z0 = pad.r.yMin, z1 = pad.r.yMax;
            foreach (var w in Ways) { a0 = Mathf.Min(a0, w.box.xMin); a1 = Mathf.Max(a1, w.box.xMax); z0 = Mathf.Min(z0, w.box.yMin); z1 = Mathf.Max(z1, w.box.yMax); }
            // (and as far as the pad's own banks can reach: cut off by the grid's edge, they'd stand as a wall)
            a0 = Mathf.Min(a0, pad.r.xMin - PadBank - 1f); a1 = Mathf.Max(a1, pad.r.xMax + PadBank + 1f);
            z0 = Mathf.Min(z0, pad.r.yMin - PadBank - 1f); z1 = Mathf.Max(z1, pad.r.yMax + PadBank + 1f);
            float q = Slope.Quad;
            // (never as far down as the run's first section: the ground there asks where the lifts are, Cliffs' ledges,
            // and they are laid on this)
            z1 = Mathf.Min(z1 + 6f, Course.First - 6f);
            gx0 = Mathf.Floor((cx + a0 - 6f) / q) * q; gz0 = Mathf.Floor((z0 - 6f) / q) * q;
            gw = Mathf.CeilToInt((cx + a1 + 6f - gx0) / q) + 1; gh = Mathf.CeilToInt((z1 - gz0) / q) + 1;
            int n = gw * gh;
            gA = new float[n]; gC = new float[n]; gG = new float[n]; gD = new float[n]; gV = new float[n]; gF = new float[n];
            float lineA = StationOff[0];
            // (where any of the station's places would put its deck, and a metre round it: held at the pad's level, so
            // the deck sits flush whichever place the line takes)
            float W = LiftLine.StationHalfWid, L = LiftLine.StationHalfLen;
            var deckZone = Rect.MinMaxRect(Mathf.Min(StationOff) - W - 1f, Mathf.Min(StationZ) - L - 1f, Mathf.Max(StationOff) + W + 1f, Mathf.Max(StationZ) + L + 1f);
            float deckEnd = Mathf.Max(pad.r.yMax, deckZone.yMax);
            for (int j = 0; j < gh; j++)
                for (int i = 0; i < gw; i++)
                {
                    int k = j * gw + i;
                    float x = gx0 + i * q, z = gz0 + j * q, a = x - cx;
                    float A = 1f, C = 0f, groomed = 0f, dist = 99f, nearFar = 99f;
                    float y = Slope.Height(x, z), y0 = y;
                    // the pad, then the ways off it
                    float sd = RoundRect(a, z, pad.r, pad.corner);
                    dist = Mathf.Min(dist, sd);
                    float wp = 0f;
                    if (sd <= 0f) wp = 1f;
                    else
                    {
                        // (built up, or cut, at no more than about 30 degrees: snow banks, not a dome of rock)
                        float B = Mathf.Clamp(Mathf.Abs(pad.H - y) * 2.8f, 3f, PadBank);
                        if (sd < B) wp = 1f - Mathf.SmoothStep(0f, 1f, sd / B);
                    }
                    if (wp > 0f) { y = Mathf.Lerp(y, pad.H, wp); C = C * (1f - wp) + pad.H * wp; A *= 1f - wp; groomed = Mathf.Max(groomed, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sd / 3f))); }
                    // under the line past the deck's downhill end: the snow falls away from the pad's edge (never a bank
                    // up into the chairs coming in), at most RampFall a metre
                    if (z > deckEnd)
                    {
                        float dz = z - deckEnd;
                        float band = (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(a - lineA) - LiftLine.StationHalfWid - 3f) / 4f)))
                            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dz - CutReach) / 8f)));
                        // (the pad's bank built up there cut back; a knoll of the mountain's own cut into, CutDeep at most)
                        float cap = Mathf.Max(pad.H - dz * RampFall, y0 - CutDeep);
                        if (band > 0f && y > cap) { float t = band; y = Mathf.Lerp(y, cap, t); A *= 1f - t; C = C * (1f - t) + cap * t; }
                    }
                    Apply(rideIn, a, z, ref y, ref A, ref C, ref groomed, ref dist, ref nearFar);
                    Apply(farDown, a, z, ref y, ref A, ref C, ref groomed, ref dist, ref nearFar);
                    float wz = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(RoundRect(a, z, deckZone, 0.5f) / 1.5f));
                    if (wz > 0f) { y = Mathf.Lerp(y, pad.H, wz); A *= 1f - wz; C = C * (1f - wz) + pad.H * wz; groomed = Mathf.Max(groomed, wz); }
                    // (never the start: its in-run and its banks; nor anything the run keeps for itself, a crevasse, a
                    // jump cliff, a deadly wall)
                    float keep = Keep(x, z, a);
                    if (keep < 1f) { A = Mathf.Lerp(1f, A, keep); C *= keep; groomed *= keep; }
                    gA[k] = A; gC[k] = C; gG[k] = groomed; gD[k] = dist; gF[k] = nearFar;
                    gV[k] = GroveAt(x, z, nearFar, A * y0 + C, y0);
                }
            PreparedMs = (float)watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>How fast the snow falls away under the line past the deck's downhill end (m a metre).</summary>
        const float RampFall = 0.45f;
        /// <summary>How far past the deck's end the snow under the line is cut (then eased back over 8 m), and at most how deep into the mountain.</summary>
        const float CutReach = 12f, CutDeep = 2.5f;
        /// <summary>The furthest the pad's banks reach out from its edge (m).</summary>
        const float PadBank = 45f;

        /// <summary>
        /// How much of the shaping a point keeps (0: none): never the start's in-run (the traverse ends at its edge,
        /// where the in-run's own bank lies as low as it), nor the run below the lip. (Only shapes and the start's own
        /// numbers here: nothing that asks where the lifts are, which are laid on this ground.)
        /// </summary>
        static float Keep(float x, float z, float a)
        {
            float k = 1f;
            if (z > -74f && z < 2f) k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(a) - 15f) / 5f));
            // (nor the lodge's own level pad and a few metres round it: its floor, its steps and its footings are set
            // from that ground, and its walls stand in it)
            float lp = Camp.LodgePad(a, z, out _);
            if (lp > 0f) k = Mathf.Min(k, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lp * 2f)));
            if (z >= 2f) k = Mathf.Min(k, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(Slope.Across(x, z)) - Slope.Half(z) - 4f) / 8f)));
            return k;
        }

        /// <summary>A way's line, widths and heights for today's mountain.</summary>
        static void Lay(Way w, float cx)
        {
            Smooth(w);
            w.h.Clear();
            float h0 = PadAt(w.pts[0], out float ph) ? ph : float.NaN;
            var e = w.pts[w.pts.Count - 1];
            float h1 = Slope.Height(cx + e.x, e.y);
            if (w == rideIn) h1 = Mathf.Min(h1, pad.H + 0.4f);   // (level with the gate: the start's bank cut down to it)
            if (w.follow)
            {
                // the ground along it, smoothed (groomed, level across, on the face as it lies), from its start's height
                var g = new float[w.pts.Count];
                for (int k = 0; k < g.Length; k++) g[k] = Slope.Height(cx + w.pts[k].x, w.pts[k].y);
                for (int k = 0; k < g.Length; k++)
                {
                    float sum = 0f, wsum = 0f;
                    for (int j = Mathf.Max(0, k - w.smooth); j <= Mathf.Min(g.Length - 1, k + w.smooth); j++) { float wt = 1f + w.smooth - Mathf.Abs(j - k); sum += g[j] * wt; wsum += wt; }
                    float y = sum / wsum;
                    if (!float.IsNaN(h0)) y = Mathf.Lerp(h0, y, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(w.s[k] / w.ease)));
                    w.h.Add(y);
                }
            }
            else
            {
                if (float.IsNaN(h0)) h0 = Slope.Height(cx + w.pts[0].x, w.pts[0].y);
                for (int k = 0; k < w.pts.Count; k++)
                {
                    // steady, easing in off the pad and out at its end
                    float t = Mathf.Clamp01((w.s[k] - w.flatFor) / Mathf.Max(0.01f, w.len - w.flatFor));
                    w.h.Add(Mathf.Lerp(h0, h1, Mathf.SmoothStep(0f, 1f, t) * 0.25f + t * 0.75f));
                }
            }
            // its box, out to as far as its banks could reach
            float a0 = float.MaxValue, a1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int k = 0; k < w.pts.Count; k++)
            {
                float r = w.w[k] + w.bankMax;
                a0 = Mathf.Min(a0, w.pts[k].x - r); a1 = Mathf.Max(a1, w.pts[k].x + r); z0 = Mathf.Min(z0, w.pts[k].y - r); z1 = Mathf.Max(z1, w.pts[k].y + r);
            }
            w.box = Rect.MinMaxRect(a0, z0, a1, z1);
        }

        /// <summary>The gate's own level: the start's in-run under MT. VERTIGO's board (the pad and its way are laid at it).</summary>
        static float GateLevel(float cx) => Slope.Height(cx, Slope.GateZ);

        static void Apply(Way w, float a, float z, ref float y, ref float A, ref float C, ref float groomed, ref float dist, ref float nearFar)
        {
            if (!w.box.Contains(new Vector2(a, z))) return;
            float d = Nearest(w, a, z, out int i, out float t);
            float half = Mathf.Lerp(w.w[i], w.w[i + 1], t);
            float T = Mathf.Lerp(w.h[i], w.h[i + 1], t);
            dist = Mathf.Min(dist, d - half);
            if (w.far) nearFar = Mathf.Min(nearFar, d - half);
            groomed = Mathf.Max(groomed, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - half) / 3f)));
            // (its banks: a slope of snow out to the mountain as it lies; at its ends beyond its line it fades)
            float B = Mathf.Clamp(Mathf.Abs(T - y) * w.bankK, w.bankMin, w.bankMax);
            float wt = d <= half ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - half) / B);
            if (wt <= 0f) return;
            y = Mathf.Lerp(y, T, wt);
            A *= 1f - wt; C = C * (1f - wt) + T * wt;
        }

        static bool PadAt(Vector2 p, out float h)
        {
            h = 0f;
            if (RoundRect(p.x, p.y, pad.r, pad.corner) <= 0.5f) { h = pad.H; return true; }
            return false;
        }

        /// <summary>A way's line through its key points (Catmull-Rom), a point every 1.2 m, with its width and length along.</summary>
        static void Smooth(Way w)
        {
            w.pts.Clear(); w.w.Clear(); w.s.Clear();
            var k = w.key;
            for (int i = 0; i < k.Length - 1; i++)
            {
                Vector2 p0 = k[Mathf.Max(0, i - 1)], p1 = k[i], p2 = k[i + 1], p3 = k[Mathf.Min(k.Length - 1, i + 2)];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 1.2f));
                for (int j = 0; j < n; j++)
                {
                    float t = j / (float)n, t2 = t * t, t3 = t2 * t;
                    w.pts.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                    w.w.Add(Mathf.Lerp(w.halfW[i], w.halfW[i + 1], t));
                }
            }
            w.pts.Add(k[k.Length - 1]); w.w.Add(w.halfW[k.Length - 1]);
            float s = 0f;
            for (int i = 0; i < w.pts.Count; i++) { if (i > 0) s += Vector2.Distance(w.pts[i - 1], w.pts[i]); w.s.Add(s); }
            w.len = s;
        }

        static float RoundRect(float a, float z, Rect r, float c)
        {
            float qx = Mathf.Abs(a - r.center.x) - (r.width * 0.5f - c), qz = Mathf.Abs(z - r.center.y) - (r.height * 0.5f - c);
            float ox = Mathf.Max(qx, 0f), oz = Mathf.Max(qz, 0f);
            return Mathf.Sqrt(ox * ox + oz * oz) + Mathf.Min(Mathf.Max(qx, qz), 0f) - c;
        }

        /// <summary>The nearest point of a way's line: how far off it (m), which segment and how far along it.</summary>
        static float Nearest(Way w, float a, float z, out int seg, out float t)
        {
            float best = float.MaxValue; seg = 0; t = 0f;
            var q = new Vector2(a, z);
            for (int i = 0; i + 1 < w.pts.Count; i++)
            {
                var p = w.pts[i]; var d = w.pts[i + 1] - p;
                float u = Mathf.Clamp01(Vector2.Dot(q - p, d) / Mathf.Max(1e-6f, d.sqrMagnitude));
                float m = (p + d * u - q).sqrMagnitude;
                if (m < best) { best = m; seg = i; t = u; }
            }
            return Mathf.Sqrt(best);
        }

        // ---------------------------------------------------------------- trees and rock beside the far side

        /// <summary>
        /// How thickly trees stand at a point beside the far side (0: as the forest has it): clumps of firs from a few
        /// metres off its edges out to 34 m, thinning out; none on its banks where they're steep, nor within a chair's
        /// swing of the line (the forest keeps the line's swath clear itself).
        /// </summary>
        static float GroveAt(float x, float z, float off, float y, float y0)
        {
            return 0f;   // (lifttop2: no far side of its own; the forest as it was)
#pragma warning disable CS0162
            if (off > 34f || off < 4f) return 0f;
            float clump = Slope.Noise(x * 0.06f + 31f, z * 0.06f + 7f);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.7f, clump)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 34f, off)));
            return k * 0.7f;
#pragma warning restore CS0162
        }

        /// <summary>For the forest: trees beside the far side (a density to plant at, 0 for none of its own), and in a few cells a rock outcrop (size over 0).</summary>
        public static float Grove(float x, float z, out float outcrop)
        {
            outcrop = 0f;
            if (!Ready()) return 0f;
            float v = Sample(gV, x, z, 0f);
            return v;
        }

        // ---------------------------------------------------------------- what others ask

        static float Sample(float[] g, float x, float z, float outside)
        {
            float q = Slope.Quad;
            float fx = (x - gx0) / q, fz = (z - gz0) / q;
            if (fx < 0f || fz < 0f || fx >= gw - 1 || fz >= gh - 1) return outside;
            int i = (int)fx, j = (int)fz; float u = fx - i, v = fz - j;
            int k = j * gw + i;
            return Mathf.Lerp(Mathf.Lerp(g[k], g[k + 1], u), Mathf.Lerp(g[k + gw], g[k + gw + 1], u), v);
        }

        /// <summary>The ground with the top's pad and its way on it (Slope's last step; x, z world).</summary>
        public static float Shape(float x, float z, float y)
        {
            if (!Ready()) return y;
            float q = Slope.Quad;
            float fx = (x - gx0) / q, fz = (z - gz0) / q;
            if (fx < 0f || fz < 0f || fx >= gw - 1 || fz >= gh - 1) return y;
            int i = (int)fx, j = (int)fz; float u = fx - i, v = fz - j;
            int k = j * gw + i;
            float A = Mathf.Lerp(Mathf.Lerp(gA[k], gA[k + 1], u), Mathf.Lerp(gA[k + gw], gA[k + gw + 1], u), v);
            if (A >= 0.9999f) return y;
            float C = Mathf.Lerp(Mathf.Lerp(gC[k], gC[k + 1], u), Mathf.Lerp(gC[k + gw], gC[k + gw + 1], u), v);
            return A * y + C;
        }

        /// <summary>On the top's pad or its way (with a margin): no trees, mounds or other things there.</summary>
        public static bool Covers(float x, float z, float margin)
        {
            if (!Ready()) return false;
            return Sample(gD, x, z, 99f) < margin;
        }

        /// <summary>1 on the pad and the groomed way off it, 0 a few metres off them: they ride, and look, groomed (Surfaces).</summary>
        public static float Groomed(float x, float z)
        {
            if (!Ready()) return 0f;
            return Sample(gG, x, z, 0f);
        }

        /// <summary>Where the Summit lift's top station may stand (camp terms), the first that will go first.</summary>
        public static bool Station(out float[] zs, out float[] offs)
        {
            zs = StationZ; offs = StationOff;
            return true;
        }

        /// <summary>Is this lift's top on the pad (it is, unless the pad would not take it on this mountain)?</summary>
        public static bool AtBench(LiftLine.Lift l)
        {
            if (l == null || !l.summit) return false;
            float a = l.top.x - Slope.Centre(Slope.PadLip);
            float o0 = Mathf.Min(StationOff), o1 = Mathf.Max(StationOff), z0 = Mathf.Min(StationZ), z1 = Mathf.Max(StationZ);
            return a > o0 - 1.5f && a < o1 + 1.5f && l.top.z > z0 - 1.5f && l.top.z < z1 + 1.5f;
        }

        /// <summary>The line people ride in along (world points on the snow), then on to the paved path; walkEnd: where the ride in ends.</summary>
        public static List<Vector3> RideIn(out int walkEnd)
        {
            var pts = new List<Vector3>(); walkEnd = 0;
            if (!Ready()) return pts;
            foreach (var p in rideIn.pts) pts.Add(Snow(p));
            walkEnd = pts.Count;
            var last = rideIn.pts[rideIn.pts.Count - 1];
            foreach (var p in onTo)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(last, p) / 1.5f));
                for (int k = 1; k <= n; k++) pts.Add(Snow(Vector2.Lerp(last, p, k / (float)n)));
                last = p;
            }
            return pts;
        }

        /// <summary>The way down for those not stopping: off the pad down the way past the station's camp side, into the in-run above the lip and over it (world points on the snow).</summary>
        public static List<Vector3> FarSide()
        {
            var pts = new List<Vector3>();
            if (!Ready()) return pts;
            foreach (var p in farDown.pts) pts.Add(Snow(p));
            var last = farDown.pts[farDown.pts.Count - 1];
            foreach (var p in downTo)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(last, p) / 1.5f));
                for (int k = 1; k <= n; k++) pts.Add(Snow(Vector2.Lerp(last, p, k / (float)n)));
                last = p;
            }
            return pts;
        }

        /// <summary>
        /// Where poles and rope lines go (PisteKit): along both edges of the way down (side -1 its left going down, +1
        /// its right), and round the pad's rim (side 2) where the ground falls away past it and it isn't a way off it
        /// nor the chairs' way in under the line. (Point x z world; open: the ground falls away there, roped.)
        /// </summary>
        public static List<(Vector2 at, int side, bool open)> FarEdges(float every)
        {
            var r = new List<(Vector2, int, bool)>();
            if (!Ready()) return r;
            float cx = preparedCx;
            var w = farDown;
            float next = 12f;   // (from clear of the pad)
            for (int i = 1; i < w.pts.Count; i++)
            {
                if (w.s[i] < next) continue;
                next = w.s[i] + every;
                var d = (w.pts[Mathf.Min(w.pts.Count - 1, i + 1)] - w.pts[i - 1]).normalized;
                var right = new Vector2(d.y, -d.x);   // (to the right going down: +a, toward the in-run)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    var p = w.pts[i] + right * sd * (w.w[i] + 1.2f);
                    var o = p + right * sd * 6f;
                    float fall = Slope.Height(cx + p.x, p.y) - Slope.Height(cx + o.x, o.y);
                    r.Add((new Vector2(cx + p.x, p.y), sd, fall > 1.2f));
                }
            }
            // round the pad's rim, a point every `every` metres, in order round it
            float per = 2f * (pad.r.width + pad.r.height);
            int n = Mathf.Max(8, Mathf.CeilToInt(per / every));
            for (int k = 0; k < n; k++)
            {
                float ang = k / (float)n * Mathf.PI * 2f;
                var dirv = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float lo = 0f, hi = 40f;
                for (int it = 0; it < 20; it++) { float m = (lo + hi) * 0.5f; var q = pad.r.center + dirv * m; if (RoundRect(q.x, q.y, pad.r, pad.corner) < 1.2f) lo = m; else hi = m; }
                var p = pad.r.center + dirv * lo;
                bool skip = Nearest(rideIn, p.x, p.y, out _, out _) < 10f || Nearest(farDown, p.x, p.y, out _, out _) < 9f   // (the ways off it)
                    || (Mathf.Abs(p.x - StationOff[0]) < LiftLine.StationHalfWid + 3f && p.y > StationZ[0]);   // (the chairs' way in, under the line)
                var o = p + dirv * 6f;
                float fall = Slope.Height(cx + p.x, p.y) - Slope.Height(cx + o.x, o.y);
                r.Add((new Vector2(cx + p.x, p.y), 2, !skip && fall > 1.2f));
            }
            return r;
        }

        /// <summary>On the pad's flat (x, z world), a margin in from its edge: a station's deck here sits flush with it (LiftLine).</summary>
        public static bool OnPad(float x, float z, float margin)
        {
            if (!Ready()) return false;
            return RoundRect(x - preparedCx, z, pad.r, pad.corner) < -margin;
        }

        /// <summary>On or near the way down (x, z world): no lift tower's foot there.</summary>
        public static bool OnWayDown(float x, float z, float margin)
        {
            if (!Ready()) return false;
            return Sample(gF, x, z, 99f) < margin;
        }

        /// <summary>For the pictures' notes: the mountain's own height here, without the top's shaping.</summary>
        public static float RawHeight(float x, float z) { bool was = raw; raw = true; try { return Slope.Height(x, z); } finally { raw = was; } }

        static Vector3 Snow(Vector2 p) { float x = preparedCx + p.x; return new Vector3(x, Slope.DrawnHeight(x, p.y), p.y); }

        /// <summary>For the plan's lines: the pad's outline, the way to the camp's stairs and the way down (world points), and which is which (0 pad, 1 to the camp, 2 down).</summary>
        public static List<(int kind, List<Vector3> pts)> Lines()
        {
            var r = new List<(int, List<Vector3>)>();
            if (!Ready()) return r;
            var o = new List<Vector3>();
            for (int k = 0; k <= 48; k++)
            {
                float ang = k / 48f * Mathf.PI * 2f;
                var dirv = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float lo = 0f, hi = 60f;
                for (int it = 0; it < 24; it++) { float m = (lo + hi) * 0.5f; var q = pad.r.center + dirv * m; if (RoundRect(q.x, q.y, pad.r, pad.corner) < 0f) lo = m; else hi = m; }
                o.Add(Snow(pad.r.center + dirv * lo));
            }
            r.Add((0, o));
            var ri = RideIn(out _);
            r.Add((1, ri));
            r.Add((2, FarSide()));
            return r;
        }

        /// <summary>For the notes: the pad's level, its way's length and fall, and its steepest stretch.</summary>
        public static string Describe()
        {
            if (!Ready()) return "off";
            var sb = new System.Text.StringBuilder();
            sb.Append($"pad {pad.r.width:0}x{pad.r.height:0} m at the gate's level ({pad.H - Slope.PlateauAt(-70f):+0.0;-0.0} m against the camp's plateau); ");
            foreach (var w in Ways)
            {
                float fall = w.h[0] - w.h[w.h.Count - 1], steep = 0f;
                for (int i = 4; i < w.h.Count; i += 4) steep = Mathf.Max(steep, (w.h[i - 4] - w.h[i]) / Mathf.Max(0.1f, w.s[i] - w.s[i - 4]));
                sb.Append($"{w.name} {w.len:0} m, falls {fall:0.0} m ({fall / Mathf.Max(1f, w.len) * 100f:0}% on average, {steep * 100f:0}% at its steepest); ");
            }
            sb.Append($"laid out in {PreparedMs:0} ms on a {gw}x{gh} grid");
            return sb.ToString();
        }

        /// <summary>The way down's ground along its line: (metres along, height over the lip's level, the mountain's own there), for the notes.</summary>
        public static List<(float s, float h, float natural)> FarProfile()
        {
            var r = new List<(float, float, float)>();
            if (!Ready()) return r;
            float cx = preparedCx, y0 = Slope.DrawnHeight(cx, Slope.PadLip), s0 = 0f;
            foreach (var w in new[] { farDown })
            {
                for (int i = 0; i < w.pts.Count; i += 2)
                {
                    float x = cx + w.pts[i].x, z = w.pts[i].y;
                    float nat; raw = true; try { nat = Slope.Height(x, z); } finally { raw = false; }
                    r.Add((s0 + w.s[i], Slope.Height(x, z) - y0, nat - y0));
                }
                s0 += w.len;
            }
            return r;
        }
    }
}
