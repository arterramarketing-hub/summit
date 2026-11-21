using System.Diagnostics;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Markers for the device checks: a record each time a periodic job runs (the reflections redrawn, a terrain chunk or
    /// a course cycle built, the weather's cloud deck moved...), with what it took, the frame and the time, so a dip in the
    /// phone's fps samples can be tied to its cause. On only in device runs (SUMMIT_AUTOPLAY=1 or SUMMIT_PERF=1) and when
    /// a test tool switches it on; otherwise nothing is measured, kept or written.
    ///
    /// So that the measuring is not itself a cost in the frames it measures, a mark is only kept in memory (no text made,
    /// nothing allocated); the lines ("SUMMIT PERF <job> <ms> ms frame <n> t=<seconds>") are written later, all together,
    /// when the game says it is a quiet moment (Tick: not in the first 16 s of a device run's ride, where the frame rate
    /// is logged) or when the store is nearly full, and when the app goes to the background (Flush).
    /// </summary>
    public static class PerfMarks
    {
        /// <summary>Whether markers are kept (the test tools may switch it on).</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("SUMMIT_AUTOPLAY") == "1" ||
                                System.Environment.GetEnvironmentVariable("SUMMIT_PERF") == "1";

        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        struct Entry { public string job, detail; public float ms, t; public int frame; }
        static readonly Entry[] kept = new Entry[1024];
        static int count;
        static float nextFlush;

        /// <summary>A start time to hand to Mark (0 while off).</summary>
        public static long Now => On ? Stopwatch.GetTimestamp() : 0L;

        /// <summary>The milliseconds since `since` (a Now).</summary>
        public static double Since(long since) => (Stopwatch.GetTimestamp() - since) * TickMs;

        /// <summary>The job just done, and how long it took since `since` (a Now). `detail`: a fixed string, or null.</summary>
        public static void Mark(string job, long since, string detail = null)
        {
            if (!On) return;
            double ms = Since(since);
            Last = ms;
            Keep(job, detail, (float)ms);
        }

        /// <summary>Mark the job only if it took longer than `ms` (for jobs that run often and only now and then take long).</summary>
        public static void MarkIfOver(string job, long since, double ms = 1.0, string detail = null)
        {
            if (!On) return;
            double took = Since(since);
            if (took > ms) { Last = took; Keep(job, detail, (float)took); }
        }

        /// <summary>A moment worth knowing about that costs nothing itself (a weather rolled, a front arriving).</summary>
        public static void Note(string what)
        {
            if (On) Keep(what, null, -1f);
        }

        static void Keep(string job, string detail, float ms)
        {
            if (count >= kept.Length) Flush();
            kept[count++] = new Entry { job = job, detail = detail, ms = ms, t = Time.realtimeSinceStartup, frame = Time.frameCount };
        }

        /// <summary>
        /// Once a frame (SummitGame): write what is kept, at most every few seconds and only at a `quiet` moment, unless
        /// the store is nearly full.
        /// </summary>
        public static void Tick(bool quiet)
        {
            if (!On || count == 0) return;
            if ((quiet && Time.realtimeSinceStartup >= nextFlush) || count > kept.Length - 64) Flush();
        }

        /// <summary>Write every kept mark to the log now (no stack traces).</summary>
        public static void Flush()
        {
            for (int i = 0; i < count; i++)
            {
                var e = kept[i];
                if (e.ms < 0f)
                    UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "SUMMIT PERF {0} frame {1} t={2:0.00}", e.job, e.frame, e.t);
                else
                    UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "SUMMIT PERF {0} {1:0.00} ms frame {2} t={3:0.00}{4}",
                                                e.job, e.ms, e.frame, e.t, e.detail == null ? "" : "  " + e.detail);
                kept[i] = default;
            }
            count = 0;
            nextFlush = Time.realtimeSinceStartup + 5f;
        }

        /// <summary>The last marked job's milliseconds (the test tools read it).</summary>
        public static double Last { get; private set; }
    }
}
