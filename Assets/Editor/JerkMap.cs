using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// Where GlitchTest's jerks happen, against the crevasses: reads a GlitchTest log ($JERK_LOG), lays out each seed's
/// mountain the way GlitchTest does (1000 + seed * 77) and counts the jerks within 3 m of a crack's opening, and of
/// those how many came with the board in the air. Run: -executeMethod JerkMap.Run (no scene needed).
/// </summary>
public static class JerkMap
{
    public static void Run()
    {
        string path = System.Environment.GetEnvironmentVariable("JERK_LOG");
        var rx = new Regex(@"^GLITCH seed(\d+) (.+?): jerk (\d+) m/s\^2 at t=([\d.]+) z=(-?\d+) x-c=(-?[\d.]+) air=(\w+)");
        var bySeed = new Dictionary<int, List<Match>>();
        foreach (var line in File.ReadLines(path))
        {
            var m = rx.Match(line);
            if (!m.Success) continue;
            int s = int.Parse(m.Groups[1].Value);
            if (!bySeed.TryGetValue(s, out var l)) bySeed[s] = l = new List<Match>();
            l.Add(m);
        }
        int all = 0, near = 0, nearAir = 0;
        var where = new Dictionary<string, int>();
        foreach (var kv in bySeed)
        {
            Slope.Init(1000 + kv.Key * 77);
            Course.ResetCaches();
            foreach (var m in kv.Value)
            {
                all++;
                float z = float.Parse(m.Groups[5].Value), xc = float.Parse(m.Groups[6].Value), x = Slope.Centre(z) + xc;
                if (!Course.NearOpening(x, z, 3f)) continue;
                near++;
                if (m.Groups[7].Value == "True") nearAir++;
                Course.CrevasseNear(x, z, 3f, out var c);
                string k = $"{c.kind}";
                where.TryGetValue(k, out int n); where[k] = n + 1;
                if (near <= 40) Debug.Log($"JERK near #{c.id} {c.kind} ({2f * c.half:0.0} m wide): seed{m.Groups[1].Value} {m.Groups[2].Value} jerk {m.Groups[3].Value} t={m.Groups[4].Value} z={z:0} x-c={xc:0.0} air={m.Groups[7].Value}");
            }
        }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in where) sb.Append($"{kv.Key}={kv.Value} ");
        Debug.Log($"JERK summary: {all} jerks, {near} within 3 m of a crack's opening ({nearAir} in the air): {sb}");
        TestHarness.Exit(0);
    }
}
