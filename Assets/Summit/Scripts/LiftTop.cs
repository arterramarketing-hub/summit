using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The Summit lift's top (lifttop, the user's choice of three: "B, the summit bench"). The station stands behind
    /// the lodge on the mountain's real high point, a broad bench of snow built up about 5.5 m over the camp's plateau
    /// and flush with its deck all round, so a rider coming off the chair has room to ride, or jump, off it. From the
    /// bench two groomed ways go down:
    ///   - into the camp: a wide funnel down past the lodge's wing to the terrace's steps (50 m, falling about 9 m);
    ///   - off the far side, for those not stopping (most of the lift's riders): a blue run down the shoulder behind
    ///     the station, which the mountain builds out for it. Where the shoulder fell away in tiers of rock into a
    ///     hollow, its ground is a broad snow shoulder now, falling gently (about 16 per cent) along the lift line's
    ///     left side to the front ridge (farTop); there it turns right and traverses, cut into the start's left bank,
    ///     into the start's in-run above the lip (farMerge), so it joins the downhill run where every run does: off the
    ///     lip. Trees stand in clumps either side of it, a few rock outcrops break its outer slope, and it is poled
    ///     along both edges with a rope line where an edge is open (PisteKit). It rides as groomed snow (Surfaces).
    /// Ground in camp terms: a, metres from the run's line at the lip (Slope.Centre(PadLip)), + toward the cliff; z down
    /// the run. Heights are above the plateau's own level there (Slope.PlateauAt), or follow the mountain as it lies.
    ///
    /// The shaping is worked out once a mountain onto a grid (Slope.Quad apart, on the terrain's own vertices): for each
    /// point of it, the final height as A * (the mountain's own) + C, how groomed it is, how far it is off the ways
    /// (Covers) and how thickly trees grow there (Grove). Slope asks Shape for every height, from any thread; the first
    /// to ask lays it out (under a lock) while the others wait.
    /// </summary>
    public static class LiftTop
    {
        /// <summary>Off: the top as it was before lifttop (the station beside the lodge's wing, no bench, no far side): for the before pictures.</summary>
        public static bool On = true;

        // ---------------------------------------------------------------- the design

        class Pad { public Rect r; public float corner, rise, refZ; public float H; }
        class Way
        {
            public string name;
            public Vector2[] key;          // its line, a few points (a, z), smoothed through
            public float[] halfW;          // its half width at each key point (the groomed width)
            public float bankK = 1.8f, bankMin = 2.5f, bankMax = 20f;   // its banks: how far out they reach for each metre built up or cut
            public bool follow;            // on the mountain as it lies (smoothed), not a designed fall
            public int smooth = 1;         // (a follow way: how many points either side its ground is averaged over)
            public float ease = 14f;       // (a follow way: over how many metres it eases from its start's height to the ground's)
            public bool far;               // part of the far side (trees beside it, poles, the riders' way off)
            public List<Vector2> pts = new List<Vector2>(); public List<float> w = new List<float>(), h = new List<float>(), s = new List<float>();
            public float len;
            public Rect box;
        }

        /// <summary>Where the station may stand (z, and metres off the camp's line), the first that will go first: the bench.</summary>
        static readonly float[] StationZ = { -113f, -112f, -114f, -111f, -115f };
        static readonly float[] StationOff = { -46f, -45f, -47f, -44f, -48f };

        static readonly Pad pad = new Pad { r = Rect.MinMaxRect(-57f, -128f, -30f, -101f), corner = 9f, rise = 5.5f, refZ = -112f };
        static readonly Way rideIn = new Way
        {
            name = "ride in",
            key = new[] { new Vector2(-37f, -103f), new Vector2(-35.5f, -94f), new Vector2(-33.5f, -86f), new Vector2(-29.5f, -78.5f), new Vector2(-23f, -73.5f), new Vector2(-15f, -70f), new Vector2(-8f, -68f) },
            halfW = new[] { 9f, 7.5f, 6f, 5f, 4.5f, 4.3f, 4.5f },
        };
        static readonly Vector2[] onTo = { new Vector2(-5.2f, -65.5f) };   // (on from the ride in's end, walking, to the paved path)
        // the far side: round behind the station off the bench's left edge, down the built-out shoulder beside the lift
        // line to the front ridge (a designed fall), then under the line and down the face to the run (as it lies)
        static readonly Way farTop = new Way
        {
            name = "far side, shoulder", far = true,
            key = new[] { new Vector2(-52f, -124f), new Vector2(-60f, -117f), new Vector2(-66f, -103f), new Vector2(-69f, -86f), new Vector2(-70f, -66f), new Vector2(-70f, -46f), new Vector2(-68f, -28f), new Vector2(-63f, -17f) },
            halfW = new[] { 4.5f, 5f, 5.5f, 6f, 6f, 6f, 6f, 6f },
            bankK = 2.3f, bankMin = 6f, bankMax = 62f,
        };
        static readonly Way farFace = new Way
        {
            name = "far side, into the in-run", far = true,
            key = new[] { new Vector2(-63f, -17f), new Vector2(-55f, -11.5f), new Vector2(-44f, -8f), new Vector2(-33f, -6f), new Vector2(-24f, -4.5f), new Vector2(-17f, -3.5f) },
            halfW = new[] { 6f, 6f, 6f, 5.5f, 5f, 5f },
            bankK = 1.8f, bankMin = 4f, bankMax = 22f,
        };
        static readonly Way[] Ways = { farFace, rideIn, farTop };

        /// <summary>Of those who come off the lift at its top: the share who ride into the camp, and how many may be on their way in at once (the rest ride off the far side).</summary>
        public const float IntoCampShare = 0.34f;
        public const int IntoCampCap = 3;

        // ---------------------------------------------------------------- laid out for today's mountain

        static readonly object gate = new object();
        static volatile bool ready;
        static volatile int preparedSeed = int.MinValue; static float preparedCx = float.NaN;
        [System.ThreadStatic] static bool raw;   // (this thread is laying it out: the mountain without it)
        static float gx0, gz0; static int gw, gh;
        static float[] gA, gC, gG, gD, gV;
        public static float PreparedMs { get; private set; }

        static bool Ready()
        {
            if (!On || raw) return false;
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
        public static void Warm() { if (On) Ready(); }

        static void Prepare(float cx)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            pad.H = Slope.PlateauAt(pad.refZ) + pad.rise;
            Lay(farTop, cx); Lay(rideIn, cx); Lay(farFace, cx);   // (the traverse starts where the shoulder ends)
            // the grid: over every way and the bench, with their banks
            float a0 = pad.r.xMin, a1 = pad.r.xMax, z0 = pad.r.yMin, z1 = pad.r.yMax;
            foreach (var w in Ways) { a0 = Mathf.Min(a0, w.box.xMin); a1 = Mathf.Max(a1, w.box.xMax); z0 = Mathf.Min(z0, w.box.yMin); z1 = Mathf.Max(z1, w.box.yMax); }
            float q = Slope.Quad;
            // (never as far down as the run's first section: the ground there asks where the lifts are, Cliffs' ledges,
            // and they are laid on this)
            z1 = Mathf.Min(z1 + 6f, Course.First - 6f);
            gx0 = Mathf.Floor((cx + a0 - 6f) / q) * q; gz0 = Mathf.Floor((z0 - 6f) / q) * q;
            gw = Mathf.CeilToInt((cx + a1 + 6f - gx0) / q) + 1; gh = Mathf.CeilToInt((z1 - gz0) / q) + 1;
            int n = gw * gh;
            gA = new float[n]; gC = new float[n]; gG = new float[n]; gD = new float[n]; gV = new float[n];
            for (int j = 0; j < gh; j++)
                for (int i = 0; i < gw; i++)
                {
                    int k = j * gw + i;
                    float x = gx0 + i * q, z = gz0 + j * q, a = x - cx;
                    float A = 1f, C = 0f, groomed = 0f, dist = 99f, nearFar = 99f;
                    float y = Slope.Height(x, z), y0 = y;
                    // the traverse into the in-run, the bench, then the bench's own ways
                    Apply(farFace, a, z, ref y, ref A, ref C, ref groomed, ref dist, ref nearFar);
                    float sd = RoundRect(a, z, pad.r, pad.corner);
                    dist = Mathf.Min(dist, sd);
                    float wp = 0f;
                    if (sd <= 0f) wp = 1f;
                    else
                    {
                        // (built up, or cut, at no more than about 30 degrees: snow banks, not a dome of rock)
                        float B = Mathf.Clamp(Mathf.Abs(pad.H - y) * 2.8f, 3f, 45f);
                        if (sd < B) wp = 1f - Mathf.SmoothStep(0f, 1f, sd / B);
                    }
                    if (wp > 0f) { y = Mathf.Lerp(y, pad.H, wp); C = C * (1f - wp) + pad.H * wp; A *= 1f - wp; groomed = Mathf.Max(groomed, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sd / 3f))); }
                    Apply(rideIn, a, z, ref y, ref A, ref C, ref groomed, ref dist, ref nearFar);
                    Apply(farTop, a, z, ref y, ref A, ref C, ref groomed, ref dist, ref nearFar);
                    // (never the start: its in-run and its banks; nor anything the run keeps for itself, a crevasse, a
                    // jump cliff, a deadly wall)
                    float keep = Keep(x, z, a);
                    if (keep < 1f) { A = Mathf.Lerp(1f, A, keep); C *= keep; groomed *= keep; }
                    gA[k] = A; gC[k] = C; gG[k] = groomed; gD[k] = dist;
                    gV[k] = GroveAt(x, z, nearFar, A * y0 + C, y0);
                }
            PreparedMs = (float)watch.Elapsed.TotalMilliseconds;
        }

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
            if (w == farFace) h0 = farTop.h[farTop.h.Count - 1];
            var e = w.pts[w.pts.Count - 1];
            float h1 = Slope.Height(cx + e.x, e.y);
            if (w == farTop) h1 = Mathf.Min(h1, InRunAt(-3.5f) + 2.2f);   // (the shoulder's foot: a little over the in-run where the traverse joins it)
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
                    // steady, easing in off the bench and out at its foot
                    float t = w.s[k] / Mathf.Max(0.01f, w.len);
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

        /// <summary>The start's in-run's own height at z (its middle, without its lip): where the far side's traverse comes down to.</summary>
        static float InRunAt(float z) => Slope.Height(Slope.Centre(Slope.PadLip), Mathf.Min(z, Slope.PadLip - Slope.LipLen));

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
            if (off > 34f || off < 4f) return 0f;
            float clump = Slope.Noise(x * 0.06f + 31f, z * 0.06f + 7f);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.7f, clump)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 34f, off)));
            return k * 0.7f;
        }

        /// <summary>For the forest: trees beside the far side (a density to plant at, 0 for none of its own), and in a few cells a rock outcrop (size over 0).</summary>
        public static float Grove(float x, float z, out float outcrop)
        {
            outcrop = 0f;
            if (!Ready()) return 0f;
            float v = Sample(gV, x, z, 0f);
            // outcrops: where the shoulder was rock before, on the far side's outer slope, a few big blocks breaking through
            float d = Sample(gD, x, z, 99f);
            if (d > 10f && d < 40f && v <= 0.02f)
            {
                float n = Slope.Noise(x * 0.09f + 3f, z * 0.09f + 17f);
                if (n > 0.72f) outcrop = 2.2f + 4f * Mathf.InverseLerp(0.72f, 0.95f, n);
            }
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

        /// <summary>The ground with the top's bench and ways on it (Slope's last step; x, z world).</summary>
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

        /// <summary>On the top's bench or one of its ways (with a margin): no trees, mounds or other things there.</summary>
        public static bool Covers(float x, float z, float margin)
        {
            if (!Ready()) return false;
            return Sample(gD, x, z, 99f) < margin;
        }

        /// <summary>1 on the groomed ways off the top (and the bench), 0 a few metres off them: they ride, and look, groomed (Surfaces).</summary>
        public static float Groomed(float x, float z)
        {
            if (!Ready()) return 0f;
            return Sample(gG, x, z, 0f);
        }

        /// <summary>Where the Summit lift's top station may stand (camp terms), the first that will go first.</summary>
        public static bool Station(out float[] zs, out float[] offs)
        {
            zs = offs = null;
            if (!On) return false;
            zs = StationZ; offs = StationOff;
            return true;
        }

        /// <summary>Is this lift's top on the bench (it is, unless the bench would not take it on this mountain)?</summary>
        public static bool AtBench(LiftLine.Lift l)
        {
            if (!On || l == null || !l.summit) return false;
            float a = l.top.x - Slope.Centre(Slope.PadLip);
            return a > StationOff[StationOff.Length - 1] - 1.5f && a < StationOff[StationOff.Length - 2] + 1.5f && l.top.z > -116.5f && l.top.z < -109.5f;
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

        /// <summary>The far side's line, from behind the station down to the run (world points on the snow); empty if none.</summary>
        public static List<Vector3> FarSide()
        {
            var pts = new List<Vector3>();
            if (!Ready()) return pts;
            foreach (var p in farTop.pts) pts.Add(Snow(p));
            for (int i = 1; i < farFace.pts.Count; i++) pts.Add(Snow(farFace.pts[i]));
            return pts;
        }

        /// <summary>The far side's edges, for its poles: (point on one edge, x z world), the side (-1 left going down, +1 right), and whether that side is open (falls away: roped).</summary>
        public static List<(Vector2 at, int side, bool open)> FarEdges(float every)
        {
            var r = new List<(Vector2, int, bool)>();
            if (!Ready()) return r;
            float cx = preparedCx;
            foreach (var w in new[] { farTop, farFace })
            {
                float next = w == farTop ? 16f : every * 0.5f;   // (from clear of the bench)
                for (int i = 1; i < w.pts.Count; i++)
                {
                    if (w.s[i] < next) continue;
                    next = w.s[i] + every;
                    var d = (w.pts[Mathf.Min(w.pts.Count - 1, i + 1)] - w.pts[i - 1]).normalized;
                    var right = new Vector2(d.y, -d.x);   // (to the right going down: +a, the run's way)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var p = w.pts[i] + right * s * (w.w[i] + 1.2f);
                        // open: the ground falls away beyond the edge more than a metre in 6 m
                        var o = p + right * s * 6f;
                        float fall = Slope.Height(cx + p.x, p.y) - Slope.Height(cx + o.x, o.y);
                        r.Add((new Vector2(cx + p.x, p.y), s, fall > 1.2f));
                    }
                }
            }
            return r;
        }

        static Vector3 Snow(Vector2 p) { float x = preparedCx + p.x; return new Vector3(x, Slope.DrawnHeight(x, p.y), p.y); }

        /// <summary>For the plan's lines: the bench's outline and each way's line (world points), and which is which (0 bench, 1 ride in, 2 far side).</summary>
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
            var ri = new List<Vector3>(); foreach (var q in rideIn.pts) ri.Add(Snow(q));
            r.Add((1, ri));
            r.Add((2, FarSide()));
            return r;
        }

        /// <summary>For the notes: the bench's height above the plateau, each way's length and fall, and its steepest stretch.</summary>
        public static string Describe()
        {
            if (!Ready()) return "off";
            var sb = new System.Text.StringBuilder();
            sb.Append($"bench {pad.r.width:0}x{pad.r.height:0} m at {pad.rise:+0.0} m over the plateau; ");
            foreach (var w in new[] { rideIn, farTop, farFace })
            {
                float fall = w.h[0] - w.h[w.h.Count - 1], steep = 0f;
                for (int i = 4; i < w.h.Count; i += 4) steep = Mathf.Max(steep, (w.h[i - 4] - w.h[i]) / Mathf.Max(0.1f, w.s[i] - w.s[i - 4]));
                sb.Append($"{w.name} {w.len:0} m, falls {fall:0.0} m ({fall / Mathf.Max(1f, w.len) * 100f:0}% on average, {steep * 100f:0}% at its steepest); ");
            }
            sb.Append($"laid out in {PreparedMs:0} ms on a {gw}x{gh} grid");
            return sb.ToString();
        }

        /// <summary>The far side's ground along its line: (metres along, height over the lip's level, the mountain's own there), for the notes.</summary>
        public static List<(float s, float h, float natural)> FarProfile()
        {
            var r = new List<(float, float, float)>();
            if (!Ready()) return r;
            float cx = preparedCx, y0 = Slope.DrawnHeight(cx, Slope.PadLip), s0 = 0f;
            foreach (var w in new[] { farTop, farFace })
            {
                for (int i = 0; i < w.pts.Count; i += 6)
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
