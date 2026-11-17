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
}
