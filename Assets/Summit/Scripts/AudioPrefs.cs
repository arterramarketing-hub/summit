using UnityEngine;

namespace Summit
{
    /// <summary>The player's sound settings, kept between sessions (the pause menu sets them, the music reads them).</summary>
    public static class AudioPrefs
    {
        const string MusicKey = "summit_music";

        /// <summary>Music loudness, 0..1.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 0.7f);
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); Changed?.Invoke(); }
        }

        public static System.Action Changed;
    }

    /// <summary>
    /// The TURN setting, kept between sessions with the sound's (the pause menu's GAME tab and the start page set it):
    /// how sharp the carve is, Rider.TurnScale. Eleven stops, the stock feel (1.0) in the middle: fine steps below it
    /// (0.5 to 1.0 by 0.1) and bigger ones above (to 2.5 by 0.3). Set mid-run, it is felt the moment the run resumes.
    /// </summary>
    public static class SteerPrefs
    {
        public const string TurnKey = "summit_turn";
        public static readonly float[] Stops = { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.3f, 1.6f, 1.9f, 2.2f, 2.5f };
        public const int Middle = 5;

        /// <summary>Which stop (0..10) the saved setting is on.</summary>
        public static int Stop
        {
            get
            {
                float v = PlayerPrefs.GetFloat(TurnKey, 1f); int best = Middle;
                for (int i = 0; i < Stops.Length; i++) if (Mathf.Abs(Stops[i] - v) < Mathf.Abs(Stops[best] - v)) best = i;
                return best;
            }
            set
            {
                int k = Mathf.Clamp(value, 0, Stops.Length - 1);
                PlayerPrefs.SetFloat(TurnKey, Stops[k]); PlayerPrefs.Save();
                Rider.TurnScale = Stops[k];
            }
        }

        /// <summary>How far along the slider (0..1) the setting is.</summary>
        public static float Fraction => Stop / (float)(Stops.Length - 1);
        public static string Label => Stops[Stop].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Put the saved setting on the rider (at start-up).</summary>
        public static void Apply() => Rider.TurnScale = Stops[Stop];
    }
}
