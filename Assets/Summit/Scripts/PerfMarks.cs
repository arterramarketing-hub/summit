using System.Diagnostics;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Markers for the device checks: a line in the log each time a periodic job runs (the reflections redrawn, a
    /// terrain chunk or a course cycle built, the weather's cloud deck moved...), with what it took, the frame and the
    /// time, so a dip in the phone's fps samples can be tied to its cause. Lines start "SUMMIT PERF". On only in device
    /// runs (SUMMIT_AUTOPLAY=1 or SUMMIT_PERF=1) and when a test tool switches it on; otherwise nothing is measured or
    /// written. No stack trace, and nothing allocated unless it is on.
    /// </summary>
    public static class PerfMarks
    {
        /// <summary>Whether markers are written (the test tools may switch it on).</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("SUMMIT_AUTOPLAY") == "1" ||
                                System.Environment.GetEnvironmentVariable("SUMMIT_PERF") == "1";

        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>A start time to hand to Mark (0 while off).</summary>
        public static long Now => On ? Stopwatch.GetTimestamp() : 0L;

        /// <summary>The milliseconds since `since` (a Now).</summary>
        public static double Since(long since) => (Stopwatch.GetTimestamp() - since) * TickMs;

        /// <summary>The job just done, and how long it took since `since` (a Now).</summary>
        public static void Mark(string job, long since, string detail = null)
        {
            if (!On) return;
            double ms = Since(since);
            Last = ms;
            UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "SUMMIT PERF {0} {1:0.00} ms frame {2} t={3:0.00}{4}",
                                        job, ms, Time.frameCount, Time.realtimeSinceStartup, detail == null ? "" : "  " + detail);
        }

        /// <summary>Mark the job only if it took longer than `ms` (for jobs that run every frame and only now and then
        /// have real work to do).</summary>
        public static void MarkIfOver(string job, long since, double ms = 1.0)
        {
            if (On && Since(since) > ms) Mark(job, since);
        }

        /// <summary>A moment worth knowing about that costs nothing itself (a weather rolled, a front arriving).</summary>
        public static void Note(string what)
        {
            if (!On) return;
            UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "SUMMIT PERF {0} frame {1} t={2:0.00}", what, Time.frameCount, Time.realtimeSinceStartup);
        }

        /// <summary>The last marked job's milliseconds (the test tools read it).</summary>
        public static double Last { get; private set; }
    }
}
