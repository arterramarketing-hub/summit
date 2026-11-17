using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The time of day, game-wide: one clock running 0..4 through MORNING, DAY, EVENING and NIGHT, then round
    /// again. It moves slowly in real time while the game runs (not while paused) and is remembered between
    /// sessions. The pause menu can lock it to one phase instead. DayNight.cs turns the clock into light.
    /// </summary>
    public static class TimeOfDay
    {
        public enum Phase { Morning, Day, Evening, Night }
        public static readonly string[] PhaseName = { "MORNING", "DAY", "EVENING", "NIGHT" };
        /// <summary>The pause menu's choices: the running cycle, then each phase held.</summary>
        public static readonly string[] LockName = { "CYCLE", "MORNING", "DAY", "EVENING", "NIGHT" };

        /// <summary>Real seconds each phase lasts while it cycles: a whole day is about 22 minutes.</summary>
        public const float PhaseSeconds = 330f;
        /// <summary>Where in its phase a held phase sits (evening: the golden light already going pink).</summary>
        public const float HeldAt = 0.4f;

        const string ClockKey = "summit_clock", LockKey = "summit_timelock";
        static bool loaded;
        static float clock = 1f + HeldAt;   // a new player starts in the day
        static int lockLevel;               // 0 cycle, 1..4 a phase held
        static float saveT;

        /// <summary>The test tools set this to render one moment without touching the saved clock (-1: off).</summary>
        public static float TestClock = -1f;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            clock = Mathf.Repeat(PlayerPrefs.GetFloat(ClockKey, clock), 4f);
            lockLevel = Mathf.Clamp(PlayerPrefs.GetInt(LockKey, 0), 0, LockName.Length - 1);
            // device testing: SUMMIT_TIME=0..3 (morning, day, evening, night) shows that phase, saving nothing
            var env = System.Environment.GetEnvironmentVariable("SUMMIT_TIME");
            if (!string.IsNullOrEmpty(env) && float.TryParse(env, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t))
                TestClock = Mathf.Repeat(t, 4f) + HeldAt;
        }

        /// <summary>The clock now, 0..4 (a held phase sits a little way into its phase).</summary>
        public static float Clock
        {
            get
            {
                if (TestClock >= 0f) return TestClock;
                Load();
                return lockLevel > 0 ? lockLevel - 1 + HeldAt : clock;
            }
        }

        public static Phase Current => (Phase)Mathf.Clamp((int)Clock, 0, 3);
        public static int Lock { get { Load(); return lockLevel; } }
        public static string LockLabel => LockName[Lock];

        /// <summary>Step the pause menu's TIME setting: CYCLE, MORNING, DAY, EVENING, NIGHT.</summary>
        public static void StepLock(int dir)
        {
            Load();
            // leaving a held phase for the cycle: carry on from that phase, not from wherever the clock was
            if (lockLevel > 0) clock = lockLevel - 1 + HeldAt;
            lockLevel = (lockLevel + dir + LockName.Length) % LockName.Length;
            Save();
        }

        /// <summary>Run the clock on by real seconds (call with the game's scaled delta, so a pause stops it).</summary>
        public static void Advance(float seconds)
        {
            Load();
            if (lockLevel > 0 || TestClock >= 0f || seconds <= 0f) return;
            clock = Mathf.Repeat(clock + seconds / PhaseSeconds, 4f);
            saveT += seconds;
            if (saveT > 60f) Save();   // rarely: a PlayerPrefs write is a disk write (and pausing the app saves too)
        }

        public static void Save()
        {
            if (TestClock >= 0f) return;
            saveT = 0f;
            PlayerPrefs.SetFloat(ClockKey, clock);
            PlayerPrefs.SetInt(LockKey, lockLevel);
            PlayerPrefs.Save();
        }
    }
}
