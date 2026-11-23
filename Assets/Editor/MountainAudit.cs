using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The mountain as a whole, measured (an audit tool: it changes nothing in the game).
///
/// MountainAudit.Measure: along the piste's centre line of several mountains, every 2 m from the start to the finish,
/// the pitch, the camber, the turn of the line, the open width (snow you can ride either side before trees, a cliff,
/// a crack or a deadly drop) and every feature on the way, as CSV and MAUDIT summary lines; and a map of each whole
/// run from straight above, coloured by steepness like a resort's trail map (the bot's ride drawn on it when Ride has
/// left one). Runs with or without graphics.
///
/// MountainAudit.Run: rides whole runs on a few mountains with a steering bot (the game's own physics, chase camera
/// and streaming, phone quality, clear midday), logging speed and air along the way, and saves what the game camera
/// sees every few seconds and as each feature comes up, with a view from high above it; then Measure.
///
/// Run: AUDIT_OUT=dir Unity -batchmode -projectPath . -executeMethod MountainAudit.Run -logFile x.log (graphics, no -quit)
///      AUDIT_OUT=dir Unity -batchmode -nographics -quit -projectPath . -executeMethod MountainAudit.Measure -logFile x.log
///      AUDIT_SEEDS=1000,1154 picks the mountains to ride (Measure always measures MeasureSeeds).
/// </summary>
public static class MountainAudit
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly int[] MeasureSeeds = { 1000, 1077, 1154, 1231, 1308, 2131 };
    static int[] rideSeeds = { 1000, 1154, 2131 };
    const float Step2 = 2f;
    static string outDir;

    static void Log(string s) => Debug.Log("MAUDIT " + s);

    // ================================================================ the profile

    struct Ev { public float z; public string kind; public float size; }

    class Profile
    {
        public int seed;
        public List<float> z = new List<float>(), cx = new List<float>(), h = new List<float>(), pitch = new List<float>(),
            camber = new List<float>(), radius = new List<float>(), heading = new List<float>(), openL = new List<float>(),
            openR = new List<float>(), cover = new List<float>();
        public List<string> sec = new List<string>(), stop = new List<string>();
        public List<Ev> ev = new List<Ev>();
    }

    static bool Blocked(float x, float z, out string why)
    {
        why = null;
        bool piste = Mathf.Abs(x - Slope.Centre(z)) < Slope.PisteHalf;
        if (Deadly.Below(x, z)) { why = "deadly"; return true; }
        if (Course.CrevasseCut(x, z) > 0.05f) { why = "crack"; return true; }
        if (!piste && Slope.Normal(x, z).y < 0.743f) { why = "steep"; return true; }   // steeper than 42 degrees, off the groomed run
        if (Forest.Cover(x, z) > 0.55f) { why = "trees"; return true; }
        return false;
    }

    static Profile Build(int seed)
    {
        Slope.Init(seed);
        var p = new Profile { seed = seed };
        float zEnd = Slope.FinishZ + 40f;
        for (float z = 0f; z <= zEnd; z += Step2)
        {
            float c = Slope.Centre(z);
            float c0 = Slope.Centre(z - 5f), c1 = Slope.Centre(z + 5f);
            float ds = Mathf.Sqrt(100f + (c1 - c0) * (c1 - c0));
            float h0 = Slope.Height(c0, z - 5f), h1 = Slope.Height(c1, z + 5f);
            p.z.Add(z); p.cx.Add(c); p.h.Add(Slope.Height(c, z));
            p.pitch.Add(Mathf.Atan2(h0 - h1, ds) * Mathf.Rad2Deg);
            var g = Slope.Gradient(c, z);
            p.camber.Add(Mathf.Atan(g.x) * Mathf.Rad2Deg);
            float d1 = (c1 - c0) / 10f, d2 = (c1 - 2f * c + c0) / 25f;
            p.heading.Add(Mathf.Atan(d1) * Mathf.Rad2Deg);
            p.radius.Add(Mathf.Abs(d2) < 1e-6f ? 99999f : Mathf.Pow(1f + d1 * d1, 1.5f) / Mathf.Abs(d2));
            float l = 0f, r = 0f; string wl = "far", wr = "far";
            for (float a = 1f; a <= 150f; a += 1f) { if (Blocked(c - a, z, out wl)) break; l = a; }
            for (float a = 1f; a <= 150f; a += 1f) { if (Blocked(c + a, z, out wr)) break; r = a; }
            p.openL.Add(l); p.openR.Add(r); p.stop.Add((wl ?? "far") + "/" + (wr ?? "far"));
            p.cover.Add(Forest.Cover(c - 35f, z) + Forest.Cover(c + 35f, z));
            p.sec.Add(Course.At(z).ToString());
        }
        // the features along the way
        int cycles = Course.CycleOf(Slope.FinishZ) + 1;
        for (int cy = 0; cy <= cycles; cy++)
        {
            for (int k = 0; k < 2; k++) if (Cliffs.JumpOf(cy, k, out var j)) p.ev.Add(new Ev { z = j.z, kind = "jumpcliff", size = j.drop });
            if (Cliffs.WallOf(cy, out var w)) p.ev.Add(new Ev { z = w.z1, kind = "wallcliff", size = w.height });
            foreach (var f in Course.FeaturesOf(cy)) p.ev.Add(new Ev { z = f.a.z, kind = f.kind == Course.Kind.Box ? "box" : "rail", size = f.Length });
            float ps = Course.First + cy * Course.Cycle + Course.ParkAt;
            if (ps < Course.ClearFrom - 180f) foreach (float at in Course.ParkKickers) p.ev.Add(new Ev { z = ps + at, kind = "parkkicker", size = Slope.KickHeight * 1.15f });
            foreach (var sz in SlowZones.Of(cy)) p.ev.Add(new Ev { z = sz.z, kind = "slow:" + sz.why, size = 0f });
            if (Ridge.Built(cy)) p.ev.Add(new Ev { z = Course.First + cy * Course.Cycle + Course.PipeAt, kind = "ridge:" + Ridge.FormOf(cy), size = 0f });
        }
        for (int cell = 0; cell < 60; cell++)
        {
            float z0 = 60f + cell * Slope.KickPeriod + 30f;
            float side = (Slope.Hash01(cell, 4, 9) - 0.5f) * 18f;
            float x = Slope.Centre(z0) + side;
            if (Slope.Kicker(x, z0 + Slope.KickRamp * 0.9f) > 0.3f && Course.At(z0) == Course.Section.Cruise) p.ev.Add(new Ev { z = z0 + Slope.KickRamp, kind = "kicker", size = Slope.KickHeight });
        }
        // roll-overs: the pitch over the next 15 m steeper than over the last 15 m by 6 degrees or more
        for (int i = 8; i < p.z.Count - 8; i++)
        {
            float before = 0f, after = 0f;
            for (int k = 1; k <= 7; k++) { before += p.pitch[i - k]; after += p.pitch[i + k]; }
            before /= 7f; after /= 7f;
            if (after - before > 6f && (p.ev.Count == 0 || !(p.ev[p.ev.Count - 1].kind == "rollover" && p.z[i] - p.ev[p.ev.Count - 1].z < 40f)))
                p.ev.Add(new Ev { z = p.z[i], kind = "rollover", size = after - before });
        }
        // cracks across the piste, lift lines over it, hamlets beside it
        float lastCrack = -999f, lastLift = -999f, lastHam = -999f;
        for (int i = 0; i < p.z.Count; i++)
        {
            float z = p.z[i], c = p.cx[i];
            bool crack = false, lift = false, ham = false;
            for (float a = -Slope.PisteHalf; a <= Slope.PisteHalf; a += 4f)
            {
                if (!crack && Course.CrevasseNear(c + a, z, 0f)) crack = true;
                if (!lift && LiftLine.Taken(c + a, z, 0f)) lift = true;
            }
            for (float a = -70f; a <= 70f; a += 10f) if (!ham && Hamlets.Taken(c + a, z)) ham = true;
            if (crack && z - lastCrack > 20f) p.ev.Add(new Ev { z = z, kind = "crack", size = 0f });
            if (lift && z - lastLift > 40f) p.ev.Add(new Ev { z = z, kind = "lift", size = 0f });
            if (ham && z - lastHam > 80f) p.ev.Add(new Ev { z = z, kind = "hamlet", size = 0f });
            if (crack) lastCrack = z; if (lift) lastLift = z; if (ham) lastHam = z;
        }
        p.ev.Sort((a, b) => a.z.CompareTo(b.z));
        return p;
    }

    static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    static void Summarise(Profile p, List<Vector4> trace)
    {
        int n = p.z.Count;
        var bins = new[] { 0f, 10f, 20f, 25f, 30f, 35f, 40f, 45f, 90f };
        var cnt = new int[bins.Length - 1];
        float sum = 0f, sq = 0f, mx = -99f, mn = 99f;
        for (int i = 0; i < n; i++)
        {
            float a = p.pitch[i]; sum += a; sq += a * a; mx = Mathf.Max(mx, a); mn = Mathf.Min(mn, a);
            for (int b = 0; b < cnt.Length; b++) if (a >= bins[b] && a < bins[b + 1]) { cnt[b]++; break; }
        }
        float mean = sum / n, sd = Mathf.Sqrt(Mathf.Max(0f, sq / n - mean * mean));
        var hist = new StringBuilder();
        for (int b = 0; b < cnt.Length; b++) hist.Append($" {bins[b]:0}-{bins[b + 1]:0}:{100f * cnt[b] / n:0.0}%");
        // 100 m averages: how the run's pitch changes over a skier's "pitch" (a stretch of one character)
        var seg = new StringBuilder(); float smax = -99f, smin = 99f; int changes = 0; float prev = float.NaN;
        for (int i = 0; i + 50 <= n; i += 50)
        {
            float s = 0f; for (int k = 0; k < 50; k++) s += p.pitch[i + k]; s /= 50f;
            seg.Append(' ').Append(s.ToString("0"));
            smax = Mathf.Max(smax, s); smin = Mathf.Min(smin, s);
            if (!float.IsNaN(prev) && Mathf.Abs(s - prev) >= 5f) changes++;
            prev = s;
        }
        // pitch breaks (the 10 m pitch changing by 10 degrees or more within 30 m), flats, steeps
        int breaks = 0; float lastBreak = -999f;
        for (int i = 0; i + 15 < n; i++)
            if (Mathf.Abs(p.pitch[i + 15] - p.pitch[i]) >= 10f && p.z[i] - lastBreak > 30f) { breaks++; lastBreak = p.z[i]; }
        float flat = 0f, steep = 0f, longFlat = 0f, run = 0f, longSteep = 0f, srun = 0f;
        for (int i = 0; i < n; i++)
        {
            if (p.pitch[i] < 25f) { flat += Step2; run += Step2; longFlat = Mathf.Max(longFlat, run); } else run = 0f;
            if (p.pitch[i] > 40f) { steep += Step2; srun += Step2; longSteep = Mathf.Max(longSteep, srun); } else srun = 0f;
        }
        Log($"seed {p.seed} pitch: mean {mean:0.0} sd {sd:0.0} min {mn:0.0} max {mx:0.0} |{hist}");
        Log($"seed {p.seed} pitch per 100 m:{seg} (range {smin:0}-{smax:0}, {changes} changes of 5+ deg between neighbours)");
        Log($"seed {p.seed} pitch breaks (10+ deg within 30 m): {breaks} ({breaks / (p.z[n - 1] / 1000f):0.0}/km); under 25 deg: {flat:0} m (longest {longFlat:0}); over 40 deg: {steep:0} m (longest {longSteep:0})");
        // camber, curvature
        float cmax = 0f, csum = 0f, hmax = 0f, rmin = 99999f; int flips = 0, tight = 0; float lastH = 0f;
        for (int i = 0; i < n; i++)
        {
            cmax = Mathf.Max(cmax, Mathf.Abs(p.camber[i])); csum += Mathf.Abs(p.camber[i]);
            hmax = Mathf.Max(hmax, Mathf.Abs(p.heading[i])); rmin = Mathf.Min(rmin, p.radius[i]);
            if (p.radius[i] < 150f) tight++;
            if (i > 0 && Mathf.Sign(p.heading[i]) != Mathf.Sign(lastH) && Mathf.Abs(p.heading[i]) > 0.5f) flips++;
            if (Mathf.Abs(p.heading[i]) > 0.5f) lastH = p.heading[i];
        }
        Log($"seed {p.seed} line: heading off the fall line max {hmax:0.0} deg, tightest radius {rmin:0} m, radius under 150 m on {100f * tight / n:0.0}% of the run, {flips} changes of direction; camber mean {csum / n:0.0} max {cmax:0.0} deg");
        // open width
        var wbins = new[] { 0f, 20f, 30f, 40f, 60f, 100f, 400f }; var wc = new int[wbins.Length - 1]; float wsum = 0f, wmin = 999f;
        var stops = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            float w = p.openL[i] + p.openR[i]; wsum += w; wmin = Mathf.Min(wmin, w);
            for (int b = 0; b < wc.Length; b++) if (w >= wbins[b] && w < wbins[b + 1]) { wc[b]++; break; }
            foreach (var s in p.stop[i].Split('/')) { stops.TryGetValue(s, out int c); stops[s] = c + 1; }
        }
        var wh = new StringBuilder(); for (int b = 0; b < wc.Length; b++) wh.Append($" {wbins[b]:0}-{wbins[b + 1]:0}:{100f * wc[b] / n:0.0}%");
        var st = new StringBuilder(); foreach (var kv in stops) st.Append($" {kv.Key}:{100f * kv.Value / (2f * n):0}%");
        Log($"seed {p.seed} open width: mean {wsum / n:0} min {wmin:0} m |{wh} | what closes it:{st}");
        // features
        var kinds = new SortedDictionary<string, int>();
        var gaps = new List<float>(); float lastZ = Slope.StartZ;
        foreach (var e in p.ev)
        {
            string k = e.kind.Split(':')[0]; kinds.TryGetValue(k, out int c); kinds[k] = c + 1;
            if (e.z > lastZ) { gaps.Add(e.z - lastZ); lastZ = e.z; }
        }
        gaps.Add(Slope.FinishZ - lastZ);
        gaps.Sort();
        var ks = new StringBuilder(); foreach (var kv in kinds) ks.Append($" {kv.Key}:{kv.Value}");
        Log($"seed {p.seed} features:{ks} | gaps between any two: median {gaps[gaps.Count / 2]:0} m, 90th {gaps[(int)(gaps.Count * 0.9f)]:0} m, longest {gaps[gaps.Count - 1]:0} m");
        var list = new StringBuilder(); foreach (var e in p.ev) list.Append($" {e.z:0}:{e.kind}{(e.size > 0f ? "(" + F(e.size) + ")" : "")}");
        Log($"seed {p.seed} feature list:{list}");
        // the ride
        if (trace != null && trace.Count > 10)
        {
            // trace: x = z, y = speed, z = airborne (1/0), w = t
            float dt = trace[1].w - trace[0].w, air = 0f, total = trace[trace.Count - 1].w, vmax = 0f, vsum = 0f, fast = 0f, idle = 0f, longIdle = 0f, irun = 0f;
            int airs = 0; bool was = false;
            var vb = new float[8];
            int ei = 0;
            for (int i = 0; i < trace.Count; i++)
            {
                var q = trace[i];
                vmax = Mathf.Max(vmax, q.y); vsum += q.y; if (q.y > 25f) fast += dt;
                bool a = q.z > 0.5f; if (a) air += dt; if (a && !was) airs++; was = a;
                while (ei < p.ev.Count && p.ev[ei].z < q.x - 40f) ei++;
                bool near = ei < p.ev.Count && Mathf.Abs(p.ev[ei].z - q.x) < 40f;
                if (!near && !a) { idle += dt; irun += dt; longIdle = Mathf.Max(longIdle, irun); } else irun = 0f;
                int vi = Mathf.Clamp((int)(q.y / 5f), 0, 7); vb[vi] += dt;
            }
            var vh = new StringBuilder(); for (int b = 0; b < 8; b++) vh.Append($" {b * 5}-{(b == 7 ? "+" : ((b + 1) * 5).ToString())}:{100f * vb[b] / total:0}%");
            Log($"seed {p.seed} ride: {total:0.0} s, speed mean {vsum / trace.Count * 3.6f:0} km/h max {vmax * 3.6f:0} km/h, over 90 km/h {100f * fast / total:0}% | m/s{vh}");
            Log($"seed {p.seed} ride: {airs} airs, {air:0.0} s in the air ({100f * air / total:0}%); {100f * idle / total:0}% of the time 40 m or more from any feature, longest such stretch {longIdle:0.0} s");
        }
    }

    static void WriteCsv(Profile p)
    {
        var sb = new StringBuilder("z,centre,height,pitch,camber,heading,radius,openL,openR,stops,cover35,section\n");
        for (int i = 0; i < p.z.Count; i++)
            sb.Append($"{F(p.z[i])},{F(p.cx[i])},{F(p.h[i])},{F(p.pitch[i])},{F(p.camber[i])},{F(p.heading[i])},{F(Mathf.Min(p.radius[i], 99999f))},{F(p.openL[i])},{F(p.openR[i])},{p.stop[i]},{F(p.cover[i])},{p.sec[i]}\n");
        File.WriteAllText(Path.Combine(outDir, $"profile_s{p.seed}.csv"), sb.ToString());
        var ev = new StringBuilder("z,kind,size\n");
        foreach (var e in p.ev) ev.Append($"{F(e.z)},{e.kind},{F(e.size)}\n");
        File.WriteAllText(Path.Combine(outDir, $"features_s{p.seed}.csv"), ev.ToString());
    }

    // ================================================================ the map

    static Color Grade(float deg)
    {
        // a trail map's colours by steepness: green, blue, black, double black, rock
        if (deg < 15f) return new Color(0.62f, 0.85f, 0.55f);
        if (deg < 25f) return new Color(0.35f, 0.72f, 0.38f);
        if (deg < 32f) return new Color(0.36f, 0.58f, 0.92f);
        if (deg < 40f) return new Color(0.30f, 0.30f, 0.36f);
        if (deg < 48f) return new Color(0.18f, 0.12f, 0.24f);
        return new Color(0.55f, 0.27f, 0.18f);
    }

    /// <summary>The whole run from straight above, in panels side by side, downhill down the page, 1 m a pixel.</summary>
    static void Map(Profile p, List<Vector4> trace, List<Vector2> path)
    {
        Slope.Init(p.seed);
        const int Half = 200, PanelH = 1100, Gap = 12;
        float zEnd = Slope.FinishZ + 40f;
        int panels = Mathf.CeilToInt(zEnd / PanelH);
        int W = panels * (2 * Half + Gap), H = PanelH;
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(24, 24, 28, 255);
        var hrow = new float[2 * Half + 3];
        var hprev = new float[2 * Half + 3];
        for (int pi = 0; pi < panels; pi++)
        {
            int ox = pi * (2 * Half + Gap);
            for (int r = -1; r < PanelH; r++)
            {
                float z = pi * PanelH + r;
                for (int c = -1; c <= 2 * Half + 1; c++) hrow[c + 1] = Slope.Height(c - Half, z);
                if (r >= 0 && z <= zEnd)
                {
                    float cz = Slope.Centre(z);
                    for (int c = 0; c < 2 * Half; c++)
                    {
                        float x = c - Half;
                        float gx = (hrow[c + 2] - hrow[c]) * 0.5f, gz = hrow[c + 1] - hprev[c + 1];
                        var nrm = new Vector3(-gx, 1f, -gz).normalized;
                        float deg = Mathf.Acos(nrm.y) * Mathf.Rad2Deg;
                        Color col = Grade(deg);
                        float shade = Mathf.Clamp01(Vector3.Dot(nrm, new Vector3(-0.5f, 0.75f, -0.45f).normalized));
                        col *= 0.55f + 0.6f * shade;
                        float a = Mathf.Abs(x - cz);
                        if (a < Slope.PisteHalf) col = Color.Lerp(col, Color.white, 0.28f);   // groomed
                        float cov = Forest.Cover(x, z);
                        if (cov > 0.05f && Slope.Hash01(c, (int)z, 77) < cov * 0.55f) col = Color.Lerp(col, new Color(0.05f, 0.22f, 0.1f), 0.85f);
                        if (Course.CrevasseCut(x, z) > 0.05f) col = new Color(0.02f, 0.05f, 0.12f);
                        if (Deadly.Below(x, z) && ((c + (int)z) & 7) < 2) col = Color.Lerp(col, new Color(0.9f, 0.1f, 0.1f), 0.7f);   // a fatal drop: hatched
                        if (Slope.Kicker(x, z) > 0.1f) col = new Color(1f, 0.85f, 0.1f);
                        if (Cliffs.OnJumpFace(x, z)) col = new Color(1f, 0.5f, 0.05f);
                        if (LiftLine.Taken(x, z, 0f) && ((int)z + c) % 3 == 0) col = new Color(0.85f, 0.85f, 0.9f);
                        if (Hamlets.Taken(x, z) && ((int)z + c) % 2 == 0) col = new Color(0.6f, 0.35f, 0.2f);
                        if (Mathf.Abs(a - Slope.PisteHalf) < 0.6f) col = Color.Lerp(col, Color.white, 0.7f);
                        px[(H - 1 - r) * W + ox + c] = col;
                    }
                    // a tick every 100 m, longer every 500 m, on the panel's left edge
                    int zi = Mathf.RoundToInt(z);
                    if (zi % 100 == 0) for (int c = 0; c < (zi % 500 == 0 ? 18 : 8); c++) px[(H - 1 - r) * W + ox + c] = new Color32(255, 255, 255, 255);
                }
                var t = hprev; hprev = hrow; hrow = t;
            }
        }
        // park features, then the ride
        int cycles = Course.CycleOf(Slope.FinishZ) + 1;
        for (int cy = 0; cy <= cycles; cy++)
            foreach (var f in Course.FeaturesOf(cy))
                for (float u = 0f; u <= 1f; u += 0.01f) Dot(px, W, H, Half, PanelH, Gap, Vector3.Lerp(f.a, f.b, u), new Color32(240, 60, 220, 255), 1);
        if (path != null)
            for (int i = 0; i < path.Count; i++)
            {
                float v = i < trace.Count ? trace[i].y : 20f;
                var col = Color.Lerp(new Color(0f, 0.9f, 1f), new Color(1f, 0.1f, 0.6f), Mathf.Clamp01((v - 10f) / 30f));
                if (i < trace.Count && trace[i].z > 0.5f) col = Color.yellow;
                Dot(px, W, H, Half, PanelH, Gap, new Vector3(path[i].x, 0f, path[i].y), col, 1);
            }
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, $"map_s{p.seed}.png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static void Dot(Color32[] px, int W, int H, int Half, int PanelH, int Gap, Vector3 at, Color32 col, int r)
    {
        int pi = Mathf.FloorToInt(at.z / PanelH); if (pi < 0) return;
        int row = Mathf.RoundToInt(at.z - pi * PanelH), c = Mathf.RoundToInt(at.x) + Half + pi * (2 * Half + Gap);
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int y = H - 1 - (row + dy), x = c + dx;
                if (y >= 0 && y < H && x >= pi * (2 * Half + Gap) && x < pi * (2 * Half + Gap) + 2 * Half) px[y * W + x] = col;
            }
    }

    /// <summary>Pitch down the run as a chart: distance across, 0..60 degrees up, on a trail map's colour bands; features as ticks.</summary>
    static void Chart(Profile p, List<Vector4> trace)
    {
        const int Wc = 1800, Hc = 420, Base = 60;
        var px = new Color32[Wc * Hc];
        float zEnd = p.z[p.z.Count - 1];
        for (int y = 0; y < Hc; y++)
            for (int x = 0; x < Wc; x++)
            {
                Color c;
                if (y < Base) c = new Color(0.1f, 0.1f, 0.12f);
                else { float deg = (y - Base) / (float)(Hc - Base) * 60f; c = Grade(deg) * 0.55f; if (Mathf.Abs(deg % 10f) < 0.17f) c = c * 1.5f; }
                if (x % Mathf.RoundToInt(Wc * 500f / zEnd) == 0 && y >= Base) c = c * 1.4f;
                px[y * Wc + x] = c;
            }
        for (int i = 1; i < p.z.Count; i++)
        {
            int x0 = Mathf.RoundToInt(p.z[i - 1] / zEnd * (Wc - 1)), x1 = Mathf.RoundToInt(p.z[i] / zEnd * (Wc - 1));
            int ya = Base + Mathf.RoundToInt(Mathf.Clamp(p.pitch[i - 1], 0f, 60f) / 60f * (Hc - Base - 1));
            int yb = Base + Mathf.RoundToInt(Mathf.Clamp(p.pitch[i], 0f, 60f) / 60f * (Hc - Base - 1));
            for (int x = x0; x <= x1; x++)
                for (int y = Mathf.Min(ya, yb) - 1; y <= Mathf.Max(ya, yb) + 1; y++)
                    if (y >= Base && y < Hc) px[y * Wc + x] = new Color32(255, 255, 255, 255);
        }
        if (trace != null)
            foreach (var q in trace)
            {
                int x = Mathf.RoundToInt(q.x / zEnd * (Wc - 1)); if (x < 0 || x >= Wc) continue;
                int y = Base + Mathf.RoundToInt(Mathf.Clamp(q.y * 3.6f / 200f * 60f, 0f, 60f) / 60f * (Hc - Base - 1));   // speed: 0..200 km/h on the same height
                if (y >= Base && y < Hc) px[y * Wc + x] = q.z > 0.5f ? new Color32(255, 230, 0, 255) : new Color32(255, 60, 160, 255);
            }
        foreach (var e in p.ev)
        {
            int x = Mathf.RoundToInt(e.z / zEnd * (Wc - 1)); if (x < 0 || x >= Wc) continue;
            string k = e.kind.Split(':')[0];
            Color32 c = k == "jumpcliff" ? new Color32(255, 128, 0, 255) : k == "wallcliff" ? new Color32(230, 30, 30, 255) : k == "ridge" ? new Color32(160, 90, 255, 255)
                : k == "kicker" || k == "parkkicker" ? new Color32(255, 220, 0, 255) : k == "box" || k == "rail" ? new Color32(240, 60, 220, 255)
                : k == "rollover" ? new Color32(80, 200, 255, 255) : k == "crack" ? new Color32(20, 60, 160, 255) : k == "lift" ? new Color32(220, 220, 230, 255)
                : k == "hamlet" ? new Color32(160, 100, 50, 255) : new Color32(120, 120, 120, 255);
            int row = k == "rollover" ? 0 : k == "jumpcliff" || k == "wallcliff" || k == "ridge" ? 1 : k == "kicker" || k == "parkkicker" || k == "box" || k == "rail" ? 2 : 3;
            for (int y = row * 15 + 2; y < row * 15 + 13; y++) for (int d = -1; d <= 1; d++) if (x + d >= 0 && x + d < Wc) px[y * Wc + x + d] = c;
        }
        var tex = new Texture2D(Wc, Hc, TextureFormat.RGB24, false);
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, $"pitch_s{p.seed}.png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // ================================================================ Measure

    public static void Measure()
    {
        outDir = System.Environment.GetEnvironmentVariable("AUDIT_OUT") ?? "MountainAudit";
        Directory.CreateDirectory(outDir);
        try { MeasureAll(); }
        catch (System.Exception e) { Debug.LogError("MAUDIT exception " + e); }
        if (Application.isBatchMode && !rideRunning) EditorApplication.Exit(0);
    }

    static bool rideRunning;

    static void MeasureAll()
    {
        foreach (int seed in MeasureSeeds)
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var p = Build(seed);
            traces.TryGetValue(seed, out var tr); paths.TryGetValue(seed, out var pa);
            if (tr == null) LoadTrace(seed, out tr, out pa);
            Summarise(p, tr);
            WriteCsv(p);
            Map(p, tr, pa);
            Chart(p, tr);
            Log($"seed {seed} measured in {t0.ElapsedMilliseconds / 1000f:0.0} s");
        }
    }

    static void LoadTrace(int seed, out List<Vector4> tr, out List<Vector2> pa)
    {
        tr = null; pa = null;
        string f = Path.Combine(outDir, $"ride_s{seed}.csv");
        if (!File.Exists(f)) return;
        tr = new List<Vector4>(); pa = new List<Vector2>();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var line in File.ReadAllLines(f))
        {
            var c = line.Split(',');
            if (c.Length < 6 || c[0] == "t") continue;
            float t = float.Parse(c[0], ci), x = float.Parse(c[1], ci), z = float.Parse(c[3], ci), v = float.Parse(c[4], ci), a = float.Parse(c[5], ci);
            tr.Add(new Vector4(z, v, a, t)); pa.Add(new Vector2(x, z));
        }
    }

    // ================================================================ Run: the rides

    static readonly Dictionary<int, List<Vector4>> traces = new Dictionary<int, List<Vector4>>();
    static readonly Dictionary<int, List<Vector2>> paths = new Dictionary<int, List<Vector2>>();
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props; static DayNight dayNight;
    static CourseDressing dress; static Hamlets hamlets; static SkiLift lift; static Atmosphere atmo; static Farview farview; static Camera cam; static ChaseCamera chase;
    static MethodInfo tUpd, fUpd, dUpd, hUpd, lUpd, aLate, cLate;
    static RenderTexture rt; static Texture2D tex;
    const int W = 393, H = 852;
    const float Dt = 1f / 120f, Frame = 1f / 30f, Every = 3f;
    static int quality, seedIx, n, phase;
    static float t, nextShot, downFor, lastTrace, maxZ, nextPop;
    static int evIx, shotPending; static string pendingName; static bool pendingAir;
    static Profile prof; static List<Vector4> tr; static List<Vector2> pa; static StringBuilder csv;
    static int crashes, airsLogged; static bool killed, finished;
    static readonly List<Course.Feature> near = new List<Course.Feature>();

    public static void Run()
    {
        outDir = System.Environment.GetEnvironmentVariable("AUDIT_OUT") ?? "MountainAudit";
        Directory.CreateDirectory(outDir);
        var s = System.Environment.GetEnvironmentVariable("AUDIT_SEEDS");
        if (!string.IsNullOrEmpty(s)) rideSeeds = System.Array.ConvertAll(s.Split(','), int.Parse);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's
        PlayerPrefs.SetInt(Forest.TreesPref, 1);
        Farview.TestOn = true; Farview.TestClock = 0.0;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        r = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props"); dayNight = Get<DayNight>("dayNight");
        dress = Object.FindFirstObjectByType<CourseDressing>(); hamlets = Object.FindFirstObjectByType<Hamlets>();
        lift = Object.FindFirstObjectByType<SkiLift>(); atmo = Object.FindFirstObjectByType<Atmosphere>();
        farview = Farview.Instance;
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        dUpd = typeof(CourseDressing).GetMethod("Update", NP); hUpd = typeof(Hamlets).GetMethod("Update", NP);
        lUpd = typeof(SkiLift).GetMethod("Update", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        cLate = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        Physics.simulationMode = SimulationMode.Script;
        rt = new RenderTexture(W, H, 24) { antiAliasing = 1 };
        tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        rideRunning = true;
        seedIx = -1; NextSeed();
        EditorApplication.update += Tick;
    }

    static void NextSeed()
    {
        seedIx++;
        if (seedIx >= rideSeeds.Length) { Finish(); return; }
        int seed = rideSeeds[seedIx];
        prof = Build(seed);
        TestBoot.NewMountain(game, seed, terrain, props);
        if (dress != null) { var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt; }
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        terrain.Prewarm(); props.Prewarm();
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear();
        ScoreBot.Reset();
        t = 0f; n = 0; nextShot = 2f; downFor = 0f; lastTrace = -1f; maxZ = r.Z; nextPop = 0f; evIx = 0; shotPending = 0;
        crashes = 0; killed = false; finished = false; airsLogged = 0;
        tr = new List<Vector4>(); pa = new List<Vector2>();
        csv = new StringBuilder("t,x,y,z,speed,air,crashed,pitchUnder,lean\n");
        traces[seed] = tr; paths[seed] = pa;
        r.Crash -= OnCrash; r.Crash += OnCrash;
        body.Killed -= OnKill; body.Killed += OnKill;
        if (chase != null) chase.Snap();
        ChaseCamera.TestDt = Frame;
        phase = 0;
        Log($"ride seed {seed} start at z {r.Z:0}");
    }

    static void OnCrash(string why) { crashes++; Log($"ride seed {rideSeeds[seedIx]} crash at z {r.Z:0} t {t:0.0}: {why}"); }
    static void OnKill(float fell) { killed = true; }

    static void Tick()
    {
        try { Advance(); }
        catch (System.Exception e) { Debug.LogError("MAUDIT exception " + e); Finish(); }
    }

    static void Updates()
    {
        tUpd?.Invoke(terrain, null);
        fUpd?.Invoke(props, null);
        if (dress != null) dUpd?.Invoke(dress, null);
        if (hamlets != null) hUpd?.Invoke(hamlets, null);
        if (lift != null) lUpd?.Invoke(lift, null);
        if (farview != null) { Farview.TestClock = t; farview.Apply(); farview.Step(); if (farview.far != null) farview.far.Step(false); }
    }

    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        if (atmo != null) aLate.Invoke(atmo, null);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        if (file != null) File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
    }

    static void Advance()
    {
        if (seedIx >= rideSeeds.Length) return;
        int seed = rideSeeds[seedIx];
        // a shot asked for: stream, render once to flush the queued draws, then the frame itself (and from above)
        if (shotPending > 0)
        {
            if (shotPending == 3) { Updates(); Shoot(null); }
            else if (shotPending == 2) { Updates(); Shoot(pendingName + ".png"); if (pendingName.EndsWith("_every")) Tally(); }
            else if (shotPending == 1 && pendingAir)
            {
                var keep = cam.transform.position; var keepR = cam.transform.rotation; float fov = cam.fieldOfView;
                var p0 = r.transform.position;
                var ahead = new Vector3(Slope.Centre(p0.z + 45f), 0f, p0.z + 45f); ahead.y = Slope.Height(ahead.x, ahead.z);
                var eye = p0 + new Vector3(-28f, 55f, -45f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(ahead - eye)); cam.fieldOfView = 70f;
                Updates(); Shoot(null); Shoot(pendingName + "_above.png");
                cam.transform.SetPositionAndRotation(keep, keepR); cam.fieldOfView = fov;
            }
            shotPending--;
            return;
        }
        if (phase == 1) { NextSeed(); return; }
        // one game frame: four physics steps with the bot steering, then the chase camera
        for (int k = 0; k < 4; k++, n++)
        {
            Vector3 p = r.Body.position;
            r.TrickX = 0f; r.TrickY = 0f; r.GrabHeld = false;
            float aimX = Slope.Centre(r.Z + 12f);
            if (Slope.NextKicker(r.Z, out Vector3 kk) && kk.z - r.Z < 70f) aimX = kk.x;
            Course.FeaturesNear(r.Z, near);
            foreach (var f in near)
                if (f.a.z > r.Z - 2f && f.a.z - r.Z < 60f && Mathf.Abs(f.a.x - p.x) < 12f) { aimX = f.a.x; break; }
            r.Steer = ScoreBot.Steer(r, aimX, t);
            if (!r.Airborne && !r.Crashed && !r.Grinding)
            {
                bool lip = Slope.NextKicker(r.Z - 2f, out Vector3 k2) && k2.z - r.Z < 0.8f && k2.z - r.Z > -0.2f && Mathf.Abs(k2.x - p.x) < 4f;
                if (lip) { r.QueuePop(0.5f); nextPop = t + 5f; }
            }
            if (!r.Crashed && r.Running) r.TestContacts(Dt);
            if (r.Crashed && r.CanGetUp) r.RequestUp = true;
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            game.TestPoints(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 2 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 2f * Dt);
            t += Dt;
            maxZ = Mathf.Max(maxZ, r.Z);
            downFor = r.Crashed ? downFor + Dt : 0f;
        }
        if (chase != null) cLate.Invoke(chase, null);
        if (t - lastTrace >= 0.1f)
        {
            lastTrace = t;
            var q = r.Body.position;
            float v = r.Speed;
            var g = Slope.Gradient(q.x, q.z);
            float under = Mathf.Atan(Mathf.Sqrt(g.x * g.x + g.y * g.y)) * Mathf.Rad2Deg;
            tr.Add(new Vector4(q.z, v, r.Airborne ? 1f : 0f, t)); pa.Add(new Vector2(q.x, q.z));
            csv.Append($"{F(t)},{F(q.x)},{F(q.y)},{F(q.z)},{F(v)},{(r.Airborne ? 1 : 0)},{(r.Crashed ? 1 : 0)},{F(under)},{F(r.Lean)}\n");
        }
        // the shots: every few seconds, and each feature as it comes up 30 m ahead
        while (evIx < prof.ev.Count && prof.ev[evIx].z < r.Z + 30f)
        {
            var e = prof.ev[evIx++];
            if (e.z < r.Z) continue;
            string kind = e.kind.Replace(':', '-');
            if (kind == "box" || kind == "rail" || kind.StartsWith("slow")) continue;
            pendingName = $"s{seed}_t{t:000.0}_z{e.z:0000}_{kind}"; pendingAir = true; shotPending = 3;
            return;
        }
        if (t >= nextShot)
        {
            nextShot = t + Every;
            pendingName = $"s{seed}_t{t:000.0}_z{r.Z:0000}_every"; pendingAir = false; shotPending = 3;
            return;
        }
        bool over = killed || downFor > 12f || t > 420f || (r.Crashed && body.RagdollBelowSnow > 3f) || (r.Crashed && r.StayDown && !body.RagdollMoving);
        if (!r.Crashed && !r.Airborne && r.Z > Slope.FinishZ + 30f) { finished = true; over = true; }
        if (over)
        {
            File.WriteAllText(Path.Combine(outDir, $"ride_s{seed}.csv"), csv.ToString());
            Log($"ride seed {seed} over: t {t:0.0} s, reached z {maxZ:0} of {Slope.FinishZ:0}, finished {finished}, crashes {crashes}, killed {killed}");
            phase = 1;
        }
    }

    // ---- what the view holds, at each routine shot: trees in the camera's view (placed in the streamed box round the
    // rider), the forest's listed instances and draws, and the main view's draws and triangles; split at the tree
    // line's height (Low 0.33: above it, and below it)
    static readonly double[,] sums = new double[2, 6];
    static readonly int[] shots = new int[2];

    static void Tally()
    {
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        var rp = r.transform.position;
        int trees = 0, rocks = 0;
        int c0x = Mathf.FloorToInt((rp.x - 240f) / Forest.Cell), c1x = Mathf.FloorToInt((rp.x + 240f) / Forest.Cell);
        int c0z = Mathf.FloorToInt((rp.z - 60f) / Forest.Cell), c1z = Mathf.FloorToInt((rp.z + 560f) / Forest.Cell);
        for (int cz = c0z; cz <= c1z; cz++)
            for (int cx = c0x; cx <= c1x; cx++)
            {
                var k = Forest.At(cx, cz, out Vector3 p, out _, out _);
                if (k == Forest.Kind.None) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(p + Vector3.up * 3f, new Vector3(4f, 6f, 4f)))) continue;
                if (k == Forest.Kind.Tree) trees++; else rocks++;
            }
        props.Stats(out int fd, out long ft, out int fsd, out _, out int fi);
        int draws = 0; long tris = 0;
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!mr.enabled || !mr.gameObject.activeInHierarchy || (cam.cullingMask & (1 << mr.gameObject.layer)) == 0) continue;
            if (lift != null && mr.transform.IsChildOf(lift.transform)) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, mr.bounds)) continue;
            var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) { draws++; tris += mf.sharedMesh.GetIndexCount(i) / 3; }
        }
        draws += fd; tris += ft;
        if (lift != null) { lift.Stats(out int ld, out long lt, out _); draws += ld; tris += lt; }
        int b = Forest.Low(rp.z) < 0.33f ? 0 : 1;
        shots[b]++;
        sums[b, 0] += trees; sums[b, 1] += rocks; sums[b, 2] += fi; sums[b, 3] += fd; sums[b, 4] += draws; sums[b, 5] += tris;
    }

    static void ViewReport(string label)
    {
        for (int b = 0; b < 2; b++)
        {
            int n = Mathf.Max(1, shots[b]);
            Log($"view {label} {(b == 0 ? "above the tree line" : "below it")} ({shots[b]} shots): trees in view {sums[b, 0] / n:0}, rocks in view {sums[b, 1] / n:0}, forest instances listed {sums[b, 2] / n:0}, forest draws {sums[b, 3] / n:0.0}, main-view draws {sums[b, 4] / n:0}, triangles {sums[b, 5] / n / 1000:0}k");
        }
        int all = Mathf.Max(1, shots[0] + shots[1]);
        Log($"view {label} whole run ({shots[0] + shots[1]} shots): trees in view {(sums[0, 0] + sums[1, 0]) / all:0}, forest instances listed {(sums[0, 2] + sums[1, 2]) / all:0}, forest draws {(sums[0, 3] + sums[1, 3]) / all:0.0}, main-view draws {(sums[0, 4] + sums[1, 4]) / all:0}, triangles {(sums[0, 5] + sums[1, 5]) / all / 1000:0}k");
    }

    static void Finish()
    {
        ViewReport("all rides");
        EditorApplication.update -= Tick;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        ChaseCamera.TestDt = 0f;
        Farview.TestOn = false;
        QualitySettings.SetQualityLevel(quality, false);
        TestPrefs.Restore();
        try { MeasureAll(); } catch (System.Exception e) { Debug.LogError("MAUDIT exception " + e); }
        Log("done");
        rideRunning = false;
        EditorApplication.Exit(0);
    }

    /// <summary>ScoreTest's rider who looks where they are going: down the piste, round whatever is in the way.</summary>
    static class ScoreBot
    {
        static float shift, nextLook;
        public static void Reset() { shift = 0f; nextLook = 0f; }

        static bool Clear(Vector3 p, Vector3 q, Rigidbody own)
        {
            Vector3 d = q - p; float len = d.magnitude;
            for (int i = 1; i <= 6; i++)
            {
                Vector3 s = Vector3.Lerp(p, q, i / 6f);
                if (Course.CrevasseNear(s.x, s.z, 2f) || Deadly.Below(s.x, s.z)) return false;
                if (Slope.Normal(s.x, s.z).y < 0.7f && !Course.InPipe(s.x, s.z)) return false;
            }
            var from = p + Vector3.up * 0.8f;
            foreach (var h in Physics.SphereCastAll(from, 1.3f, d / len, len, ~0, QueryTriggerInteraction.Ignore))
                if (h.rigidbody != own && h.collider.GetComponentInParent<Obstacle>() != null) return false;
            return true;
        }

        public static float Steer(Rider r, float aimX, float t)
        {
            Vector3 p = r.Body.position;
            float ahead = Mathf.Clamp(r.Speed * 1.1f, 14f, 45f);
            if (t >= nextLook && !r.Airborne)
            {
                nextLook = t + 0.1f;
                float best = 0f; bool found = false;
                foreach (float dx in new[] { 0f, 3f, -3f, 6f, -6f, 10f, -10f, 15f, -15f, 22f, -22f })
                {
                    var q = new Vector3(aimX + dx, 0f, p.z + ahead); q.y = Slope.DrawnHeight(q.x, q.z);
                    var mid = new Vector3(Mathf.Lerp(p.x, q.x, 0.5f), 0f, p.z + ahead * 0.5f); mid.y = Slope.DrawnHeight(mid.x, mid.z);
                    if (Clear(p, mid, r.Body) && Clear(mid, q, r.Body)) { best = dx; found = true; break; }
                }
                shift = found ? best : shift;
            }
            float wantX = aimX + shift;
            float want = Mathf.Atan2(wantX - p.x, ahead);
            return Mathf.Clamp((want - r.Heading) * 2.2f + (shift == 0f ? 0.25f * Mathf.Sin(t * 1.1f) : 0f), -1f, 1f);
        }
    }
}
