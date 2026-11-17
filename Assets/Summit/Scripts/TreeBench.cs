using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Device testing only (added when the app is launched with SUMMIT_TREEBENCH=1): while the run plays itself
    /// (SUMMIT_AUTOPLAY=1), switches between the original and the realistic trees every few seconds and records
    /// frame times for each - CPU from the frame clock, GPU from FrameTimingManager - then writes them to
    /// Documents/treebench.txt (and the log). Alternating A/B/A/B evens out what is on screen at the time.
    /// Starts 16 s after launch: past the camp, the drop-in and autoplay's look at the pause menu.
    /// </summary>
    public class TreeBench : MonoBehaviour
    {
        public Forest forest;
        const float StartAfter = 16f, Settle = 1.5f, Window = 5f;
        const int Rounds = 4;   // per look
        float t;
        int step = -1;   // even = original, odd = realistic
        bool settling;
        readonly List<float>[] cpu = { new List<float>(), new List<float>() }, gpu = { new List<float>(), new List<float>() };
        readonly List<int>[] tris = { new List<int>(), new List<int>() };
        readonly float[] switchHitch = new float[2];   // worst frame while settling after a switch (rebuilding the forest)
        int paused;                                     // frames over a second: the app was in the background
        readonly FrameTiming[] ft = new FrameTiming[1];
        bool done;

        void Update()
        {
            if (done || forest == null) return;
            t += Time.unscaledDeltaTime;
            FrameTimingManager.CaptureFrameTimings();
            if (step < 0)
            {
                if (t < StartAfter) return;
                Next();
                return;
            }
            int look = step & 1;
            if (Time.unscaledDeltaTime > 1f) { paused++; return; }
            if (settling) { switchHitch[look] = Mathf.Max(switchHitch[look], Time.unscaledDeltaTime * 1000f); if (t >= Settle) { settling = false; t = 0f; } return; }
            cpu[look].Add(Time.unscaledDeltaTime * 1000f);
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].gpuFrameTime > 0) gpu[look].Add((float)ft[0].gpuFrameTime);
            if (Time.frameCount % 30 == 0) { forest.Stats(out _, out long tr, out _, out _, out _); tris[look].Add((int)tr); }
            if (t >= Window) Next();
        }

        void Next()
        {
            step++;
            if (step >= Rounds * 2) { Finish(); return; }
            forest.SetRealistic((step & 1) == 1);
            settling = true; t = 0f;
        }

        static string Sum(List<float> v)
        {
            if (v.Count == 0) return "n/a";
            var s = new List<float>(v); s.Sort();
            float mean = 0f; foreach (var x in s) mean += x; mean /= s.Count;
            return $"mean {mean:0.00} ms  median {s[s.Count / 2]:0.00}  p95 {s[(int)(s.Count * 0.95f)]:0.00}  max {s[s.Count - 1]:0.00}  (n={s.Count})";
        }

        void Finish()
        {
            done = true;
            forest.SetRealistic(true);
            var sb = new StringBuilder();
            sb.AppendLine($"Summit tree bench  {SystemInfo.deviceModel}  {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  target {Application.targetFrameRate} fps");
            for (int look = 0; look < 2; look++)
            {
                float tr = 0f; foreach (var x in tris[look]) tr += x; if (tris[look].Count > 0) tr /= tris[look].Count;
                sb.AppendLine(look == 0 ? "ORIGINAL trees" : "REALISTIC trees");
                sb.AppendLine("  CPU frame  " + Sum(cpu[look]));
                sb.AppendLine("  GPU frame  " + Sum(gpu[look]));
                sb.AppendLine($"  forest triangles/frame  {tr:0}");
                sb.AppendLine($"  worst frame while switching to it  {switchHitch[look]:0.0} ms");
            }
            sb.AppendLine($"frames skipped (app in background, over 1 s): {paused}");
            var text = sb.ToString();
            Debug.Log("SUMMIT TREEBENCH\n" + text);
            try { File.WriteAllText(Path.Combine(Application.persistentDataPath, "treebench.txt"), text); } catch (System.Exception e) { Debug.Log("SUMMIT TREEBENCH write failed " + e.Message); }
        }
    }
}
