using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// The wind's levels as the game plays them: each loop's RMS and peak as Unity imported it (AudioClip.GetData: after Force
/// To Mono and any Normalize), then the wind's mixed RMS (wind_low and wind_high together, from SoundMix.WindLevels) for
/// each weather, standing and at full speed, between gusts and in one, beside the ambience bed and the board's glide.
/// Batch: -executeMethod WindCheck.Run -logFile x.log
///
/// Trees: the wind in a mixed stand (TreeWind). Neighbouring trees of every kind and level of detail, sampled at the same
/// instants for two minutes through the wind's CPU sums (TreeWind.Sway, the shader's TreeSway; and the wind as it was
/// before it was made one, mirrored here as Before), in a calm clear day, a windy day and a storm. For each pair of
/// neighbours (within 10 m): how alike their sway is (the correlation of their motion down the wind: in step or not)
/// and how alike its direction (of their moving, across the ground); each kind's lean (its top's motion over its
/// height) and the jump between a tree and its far picture at the hand-over. Exits 1 if neighbours do not move together.
/// Batch (no graphics needed): -executeMethod WindCheck.Trees -logFile x.log   (WINDCHECK_OUT: a file for the report)
/// </summary>
public static class WindCheck
{
    static float Rms(string name, out float peakDb)
    {
        var c = Resources.Load<AudioClip>("Audio/" + name);
        peakDb = -99f;
        if (c == null) return -99f;
        var d = new float[c.samples * c.channels];
        if (!c.GetData(d, 0)) return -99f;
        double e = 0; float pk = 0f;
        foreach (var v in d) { e += v * v; pk = Mathf.Max(pk, Mathf.Abs(v)); }
        peakDb = 20f * Mathf.Log10(Mathf.Max(pk, 1e-9f));
        return 10f * Mathf.Log10((float)(e / d.Length) + 1e-20f);
    }

    static float Db(float v) => 20f * Mathf.Log10(Mathf.Max(v, 1e-6f));

    public static void Run()
    {
        foreach (var n in new[] { "wind_low", "wind_high", "ambience", "glide", "carve", "skid", "powder", "avalanche_near", "boost" })
        {
            float r = Rms(n, out float pk);
            Debug.Log($"WINDCHECK clip {n}: RMS {r:0.0} dBFS, peak {pk:0.0} dBFS (as imported)");
        }
        float low = Rms("wind_low", out _), high = Rms("wind_high", out _), amb = Rms("ambience", out _), glide = Rms("glide", out _);
        var weathers = new (string name, float howl)[] { ("CLEAR", 0f), ("WINDY", 0.45f), ("SNOW", 0f), ("STORM", 1f), ("FOG", 0f), ("CLOUD SEA", 0f) };
        Debug.Log($"WINDCHECK SoundMix.WindLow {SoundMix.WindLow}, WindHigh {SoundMix.WindHigh}");
        foreach (var (name, howl) in weathers)
            foreach (float s in new[] { 0f, 1f })
                foreach (float gust in new[] { 0f, 1f })
                {
                    var v = SoundMix.WindLevels(s, false, gust, howl);
                    float lowDb = low + Db(v.x), highDb = v.y > 0f ? high + Db(v.y) : -99f;
                    float mixed = 10f * Mathf.Log10(Mathf.Pow(10f, lowDb / 10f) + (v.y > 0f ? Mathf.Pow(10f, highDb / 10f) : 0f));
                    float ambDb = amb + Db(0.22f * (1f - 0.5f * s)), glideDb = s > 0f ? glide + Db(0.35f + 0.25f * s) : -99f;
                    Debug.Log($"WINDCHECK {name,-9} speed {s:0} gust {gust:0}: wind_low {v.x:0.000} ({lowDb:0.0} dBFS)  wind_high {v.y:0.000} ({(v.y > 0f ? highDb.ToString("0.0") : "off")})  wind mixed {mixed:0.0} dBFS  | ambience {ambDb:0.0}  glide {(s > 0f ? glideDb.ToString("0.0") : "-")}");
                }
        TestHarness.Exit(0);
    }

    // ---------------------------------------------------------------- the trees

    /// <summary>A kind as the stand has it: how tall, where its crown is watched (share of height, out from the trunk),
    /// boughs (1 a whole tree, 0 a far picture), its give now (TreeWind.Flex*) and before (0: it stood still).</summary>
    struct Kind { public string name; public float h, at, r, boughs, flex, oldSway, oldBoughs; }

    static readonly Kind[] Kinds =
    {
        new Kind { name = "fir",        h = 22f, at = 0.7f, r = 2.6f, boughs = 1f, flex = TreeWind.FlexFir,     oldSway = 1f, oldBoughs = 1f },
        new Kind { name = "pine",       h = 24f, at = 0.8f, r = 1.3f, boughs = 1f, flex = TreeWind.FlexPine,    oldSway = 1f, oldBoughs = 1f },
        new Kind { name = "snag",       h = 14f, at = 0.8f, r = 0.4f, boughs = 1f, flex = TreeWind.FlexPine,    oldSway = 1f, oldBoughs = 1f },
        new Kind { name = "young fir",  h = 8f,  at = 0.7f, r = 1.3f, boughs = 1f, flex = TreeWind.FlexFir,     oldSway = 1f, oldBoughs = 1f },
        new Kind { name = "giant fir",  h = 33f, at = 0.7f, r = 3.8f, boughs = 1f, flex = TreeWind.FlexFir,     oldSway = 1f, oldBoughs = 1f },
        new Kind { name = "sapling",    h = 3f,  at = 0.7f, r = 0.8f, boughs = 1f, flex = TreeWind.FlexFir,     oldSway = 1f, oldBoughs = 1f },
        new Kind { name = "fir picture",h = 22f, at = 0.7f, r = 2.6f, boughs = 0f, flex = TreeWind.FlexFir,     oldSway = 1f, oldBoughs = 0f },
        new Kind { name = "classic fir",h = 8f,  at = 0.7f, r = 1.8f, boughs = 1f, flex = TreeWind.FlexClassic, oldSway = 1f, oldBoughs = 1f },
        new Kind { name = "birch",      h = 11f, at = 0.7f, r = 1.5f, boughs = 1f, flex = TreeWind.FlexBirch,   oldSway = 0f, oldBoughs = 1f },
    };

    // ---- the wind as it was (TreeWind.hlsl at 51512c8): its own clock-only gusts, each tree its own phase, the boughs'
    // flutter at a rate set by the gust
    static float OHash(float n) { float s = Mathf.Sin(n * 12.9898f) * 43758.5453f; return s - Mathf.Floor(s); }
    static float ONoise(float x) { float i = Mathf.Floor(x), f = x - i; f = f * f * (3f - 2f * f); return Mathf.Lerp(OHash(i), OHash(i + 1f), f); }
    static float OGust(float x, float z, float t)
    {
        float u = t * 9f - (x * TreeWind.Dir.x + z * TreeWind.Dir.y);
        float n = ONoise(u / 85f) * 0.7f + ONoise(u / 31f + 7.3f) * 0.3f;
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n - 0.56f) / 0.3f));
    }
    static Vector3 Before(Vector3 pos, Vector3 origin, float boughs, float sway, float k, float t)
    {
        if (sway <= 0f) return pos;
        Vector3 rel = pos - origin;
        float h = Mathf.Max(rel.y, 0f), r = Mathf.Min(new Vector2(rel.x, rel.z).magnitude, 6f);
        var dir = new Vector3(TreeWind.Dir.x, 0f, TreeWind.Dir.y); var across = new Vector3(-dir.z, 0f, dir.x);
        float tp = (origin.x * 0.1031f + origin.z * 0.1131f); tp = (tp - Mathf.Floor(tp)) * 6.2832f;
        float bp = pos.x * 0.9f + pos.y * 0.6f + pos.z * 1.1f + tp;
        float reach = r * r * 0.035f * boughs;
        Vector3 off = Vector3.zero;
        if (k > 0f)
        {
            float gust = OGust(origin.x, origin.z, t);
            float g = (0.07f + gust) * k;
            float rock = g * (0.8f + 0.2f * Mathf.Sin(t * 1.6f + tp)) + gust * k * 0.2f * Mathf.Sin(t * 2.9f + tp);
            off += dir * (h * h * 0.0008f * rock);
            float flut = Mathf.Sin(t * (4f + 3.5f * gust) + bp);
            off += dir * (reach * g * 0.55f);
            off.y += reach * (0.04f * k + 0.45f * g) * flut;
            off += across * (reach * g * 0.22f * Mathf.Sin(t * 5.7f + bp * 1.3f));
        }
        return pos + off * sway;
    }

    static float Corr(float[] a, float[] b)
    {
        double ma = 0, mb = 0; for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
        ma /= a.Length; mb /= b.Length;
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { double x = a[i] - ma, y = b[i] - mb; sab += x * y; saa += x * x; sbb += y * y; }
        return saa < 1e-10 || sbb < 1e-10 ? 0f : (float)(sab / System.Math.Sqrt(saa * sbb));
    }

    static float Std(float[] a)
    {
        double m = 0; foreach (var v in a) m += v; m /= a.Length;
        double s = 0; foreach (var v in a) s += (v - m) * (v - m);
        return (float)System.Math.Sqrt(s / a.Length);
    }

    /// <summary>What is seen as sway: the motion less its two-second running mean (a gust's slow lean taken out).</summary>
    static float[] Fast(float[] a, int win)
    {
        var o = new float[a.Length];
        double run = 0; int lo = 0;
        var pre = new double[a.Length + 1];
        for (int i = 0; i < a.Length; i++) pre[i + 1] = pre[i] + a[i];
        for (int i = 0; i < a.Length; i++)
        {
            int i0 = Mathf.Max(0, i - win / 2), i1 = Mathf.Min(a.Length, i + win / 2 + 1);
            o[i] = a[i] - (float)((pre[i1] - pre[i0]) / (i1 - i0));
        }
        return o;
    }

    public static void Trees()
    {
        var log = new System.Text.StringBuilder();
        void Say(string l) { Debug.Log("WINDCHECK " + l); log.AppendLine(l); }
        // a mixed stand: 7 by 7 trees on the forest's cell (6.5 m), each a little off its cell's centre, the kinds mixed
        const int N = 7; const float Cell = 6.5f;
        var at = new List<Vector3>(); var kind = new List<int>();
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                float x = 260f + i * Cell + (Slope.Hash01(i, j, 2) - 0.5f) * Cell * 0.75f, z = 1400f + j * Cell + (Slope.Hash01(i, j, 3) - 0.5f) * Cell * 0.75f;
                at.Add(new Vector3(x, 0f, z)); kind.Add((i * 4 + j * 3 + (int)(Slope.Hash01(i, j, 7) * 3f)) % Kinds.Length);
            }
        const float Hz = 20f, T0 = 100f, Dur = 120f; int S = (int)(Dur * Hz);
        bool ok = true;
        float savedK = TreeWind.Strength;
        foreach (var (wname, wind, howl, oldK) in new[] { ("CLEAR", 0.2f, 0f, 0.6f + 1.6f * 0.2f), ("WINDY", 1f, 0.45f, 2.2f), ("STORM", 1f, 1f, 2.2f) })
        {
            float newK = TreeWind.ForWeather(wind, howl);
            Say($"---- {wname}: strength before {oldK:0.00}, now {newK:0.00}");
            float meanBefore = 0f, meanAfter = 0f;
            foreach (bool now in new[] { false, true })
            {
                // each tree's watched point (on its crown) through time: down the wind, across it
                var along = new float[at.Count][]; var acr = new float[at.Count][];
                for (int n = 0; n < at.Count; n++) { along[n] = new float[S]; acr[n] = new float[S]; }
                var dir = new Vector3(TreeWind.Dir.x, 0f, TreeWind.Dir.y); var side = new Vector3(-dir.z, 0f, dir.x);
                for (int s = 0; s < S; s++)
                {
                    float t = T0 + s / Hz;
                    if (now) TreeWind.Set(t, newK);
                    for (int n = 0; n < at.Count; n++)
                    {
                        var kd = Kinds[kind[n]];
                        // watched on the side facing across the wind (a bough's bob, not its reach, is what differs)
                        Vector3 p = at[n] + Vector3.up * (kd.h * kd.at) + side * kd.r;
                        Vector3 q = now ? TreeWind.Sway(p, at[n], kd.boughs, kd.flex) : Before(p, at[n], kd.oldBoughs, kd.oldSway, oldK, t);
                        Vector3 d = q - p;
                        along[n][s] = Vector3.Dot(d, dir); acr[n][s] = Vector3.Dot(d, side);
                    }
                }
                // neighbours: in step (correlation down the wind) and moving the same way (their motions' directions)
                var pairSum = new float[Kinds.Length, Kinds.Length]; var pairN = new int[Kinds.Length, Kinds.Length];
                float sum = 0f, dirSum = 0f, fastSum = 0f; int pairs = 0, low = 0;
                for (int a = 0; a < at.Count; a++)
                    for (int b = a + 1; b < at.Count; b++)
                    {
                        if (Vector3.Distance(at[a], at[b]) > 10f) continue;
                        float c = Corr(along[a], along[b]);
                        // the sway seen (the slow lean taken out), down the wind and across it together
                        var fa = Fast(along[a], (int)(2f * Hz)); var fb = Fast(along[b], (int)(2f * Hz));
                        var ga = Fast(acr[a], (int)(2f * Hz)); var gb = Fast(acr[b], (int)(2f * Hz));
                        double fab = 0, faa = 0, fbb = 0;
                        for (int s = 0; s < S; s++) { fab += fa[s] * fb[s] + ga[s] * gb[s]; faa += fa[s] * fa[s] + ga[s] * ga[s]; fbb += fb[s] * fb[s] + gb[s] * gb[s]; }
                        float fc = faa < 1e-12 || fbb < 1e-12 ? 0f : (float)(fab / System.Math.Sqrt(faa * fbb));
                        fastSum += fc;
                        double dot = 0, na = 0, nb = 0;
                        for (int s = 1; s < S; s++)
                        {
                            float ax = along[a][s] - along[a][s - 1], az = acr[a][s] - acr[a][s - 1];
                            float bx = along[b][s] - along[b][s - 1], bz = acr[b][s] - acr[b][s - 1];
                            dot += ax * bx + az * bz; na += ax * ax + az * az; nb += bx * bx + bz * bz;
                        }
                        float dc = na < 1e-12 || nb < 1e-12 ? 0f : (float)(dot / System.Math.Sqrt(na * nb));
                        sum += c; dirSum += dc; pairs++; if (fc < 0.6f) low++;
                        int ka = Mathf.Min(kind[a], kind[b]), kb = Mathf.Max(kind[a], kind[b]);
                        pairSum[ka, kb] += fc; pairN[ka, kb]++;
                    }
                float mean = fastSum / Mathf.Max(1, pairs);
                if (now) meanAfter = mean; else meanBefore = mean;
                Say($"{(now ? "NOW   " : "BEFORE")} neighbours (within 10 m): {pairs} pairs. Sway seen (gust's slow lean taken out) in step {mean:0.00}, pairs under 0.6: {low}; moving the same way {dirSum / Mathf.Max(1, pairs):0.00} (their motions' directions); whole motion down the wind in step {sum / Mathf.Max(1, pairs):0.00}");
                var sb = new System.Text.StringBuilder();
                for (int ka = 0; ka < Kinds.Length; ka++)
                    for (int kb = ka; kb < Kinds.Length; kb++)
                        if (pairN[ka, kb] > 0) sb.Append($"{Kinds[ka].name}/{Kinds[kb].name} {pairSum[ka, kb] / pairN[ka, kb]:0.00}; ");
                Say($"{(now ? "NOW   " : "BEFORE")}   sway seen in step, by kinds: {sb}");
                // each kind's lean: how far its watched point moves (std down the wind), as a share of its height
                var lean = new System.Text.StringBuilder();
                for (int kk = 0; kk < Kinds.Length; kk++)
                {
                    float st = 0f; int c = 0;
                    for (int n = 0; n < at.Count; n++) if (kind[n] == kk) { st += Std(along[n]) + Std(acr[n]); c++; }
                    if (c > 0) lean.Append($"{Kinds[kk].name} {100f * st / c:0.0} cm ({1000f * st / c / Kinds[kk].h:0.0}/1000 of h); ");
                }
                Say($"{(now ? "NOW   " : "BEFORE")}   sway: {lean}");
            }
            // a tree and its far picture at the hand-over: the same tree's crown, drawn both ways, at the same instants
            float jumpOld = 0f, jumpNew = 0f;
            var fir = Kinds[0];
            for (int s = 0; s < S; s += 4)
            {
                float t = T0 + s / Hz;
                Vector3 o = at[0], p = o + Vector3.up * (fir.h * fir.at) + new Vector3(-TreeWind.Dir.y, 0f, TreeWind.Dir.x) * fir.r;
                jumpOld = Mathf.Max(jumpOld, (Before(p, o, 1f, 1f, oldK, t) - Before(p, o, 0f, 1f, oldK, t)).magnitude);
                TreeWind.Set(t, newK);
                jumpNew = Mathf.Max(jumpNew, (TreeWind.Sway(p, o, 1f, fir.flex) - TreeWind.Sway(p, o, 0f, fir.flex)).magnitude);
            }
            Say($"a fir and its far picture, the most they differ out on the crown (the jump at the hand-over): before {jumpOld * 100f:0.0} cm, now {jumpNew * 100f:0.0} cm");
            if (meanAfter < 0.8f || meanAfter <= meanBefore) { ok = false; Say($"FAIL {wname}: neighbours' sway in step {meanAfter:0.00} (want 0.8 or more, and more than before's {meanBefore:0.00})"); }
        }
        TreeWind.Set(0f, savedK); TreeWind.Still();
        var outFile = System.Environment.GetEnvironmentVariable("WINDCHECK_OUT");
        if (!string.IsNullOrEmpty(outFile)) System.IO.File.WriteAllText(outFile, log.ToString());
        Say(ok ? "PASS" : "FAILED");
        TestHarness.Exit(ok ? 0 : 1);
    }
}
