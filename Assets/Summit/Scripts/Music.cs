using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The soundtrack, made by Tools/MakeMusic.swift (Resources/Audio/music_*.wav): a dusty boom bap loop
    /// while you walk round the summit camp, and a reggae dub riddim from the start gate down. It crossfades
    /// between the two as you leave or come back to the camp, sits under the sound effects, dips when something
    /// big happens (the avalanche roaring up behind you, a crash), and goes quiet and muffled under the snow
    /// on the BURIED screen. The pause menu's music volume (AudioPrefs) sets how loud it is. Pausing needs
    /// nothing here: AudioListener.pause stops it along with everything else.
    /// </summary>
    public class Music : MonoBehaviour
    {
        // the music's share of the mix at full volume. Both tracks are mastered to the same loudness
        // (-17 LUFS), so one level suits both; the board on the snow, the wind and the avalanche sit above it
        const float Level = 0.3f;
        const float Crossfade = 1.6f;   // seconds from the camp's beat to the run's riddim and back
        const float FirstFadeIn = 2.5f; // the very first start comes up gently rather than at full level

        AudioSource camp, run;
        AudioLowPassFilter muffler;
        float blend = -1f;   // 0 the camp, 1 the run (-1 until the first frame says where we are)
        float fadeIn, dip, dipHold, duck, muffle, endQuiet = 1f, volume;

        public void Build()
        {
            camp = Track("music_hub");
            run = Track("music_run");
            muffler = gameObject.AddComponent<AudioLowPassFilter>();
            muffler.cutoffFrequency = 22000f;
            volume = AudioPrefs.MusicVolume;
            AudioPrefs.Changed += OnPrefs;
        }

        void OnDestroy() { AudioPrefs.Changed -= OnPrefs; }

        void OnPrefs() { volume = AudioPrefs.MusicVolume; Apply(); }

        AudioSource Track(string name)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = Resources.Load<AudioClip>("Audio/" + name);
            s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
            s.priority = 0;   // never the voice that gets dropped when lots of sounds play at once
            return s;
        }

        /// <summary>A big moment the music should get out of the way of (from SoundMix.Play).</summary>
        public void OnSfx(Sfx kind, float amount)
        {
            float d = kind switch
            {
                Sfx.Crash => 0.55f * Mathf.Clamp01(amount),
                Sfx.HitTree or Sfx.HitRock or Sfx.Crevasse or Sfx.SlideHit => 0.55f,
                Sfx.BearRoar or Sfx.LandHard => 0.3f,
                _ => 0f,
            };
            if (d <= dip) return;
            dip = d; dipHold = 1.2f;
        }

        public void Set(RideSound r, float dt)
        {
            // paused (time stopped): leave everything as it is, so unpausing carries on where it was
            if (dt <= 0f || camp == null) return;
            float want = r.camp ? 0f : 1f;
            if (blend < 0f) blend = want;   // the game opens wherever it opens, without a fade across
            blend = Mathf.MoveTowards(blend, want, dt / Crossfade);
            fadeIn = Mathf.MoveTowards(fadeIn, 1f, dt / FirstFadeIn);

            // a track coming in from silence starts at its top; one that has faded right out stops
            Keep(camp, blend < 1f);
            Keep(run, blend > 0f);

            // dips: the avalanche closing in pushes the music down (most when it is right on you), and a
            // crash knocks it down for a moment before it comes back up
            if (dipHold > 0f) dipHold -= dt; else dip = Mathf.MoveTowards(dip, 0f, dt * 0.5f);
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, r.avalanche)) * 0.5f;
            duck = Mathf.Max(dip, slide);

            // the end screens: soft under the results, and nearly gone (and muffled by the snow) when buried
            float quiet = r.ended ? (r.muffle >= 0.99f ? 0.25f : 0.6f) : 1f;
            endQuiet = Mathf.MoveTowards(endQuiet, quiet, dt / 1.5f);
            muffle = Mathf.Lerp(muffle, r.ended ? r.muffle : r.muffle * 0.5f, 1f - Mathf.Exp(-dt * 4f));
            muffler.cutoffFrequency = Mathf.Lerp(22000f, 600f, Mathf.Sqrt(muffle));
            Apply();
        }

        void Apply()
        {
            if (camp == null || blend < 0f) return;
            // an equal-power crossfade, so the music doesn't sag in the middle of it
            float g = Level * volume * fadeIn * (1f - duck) * endQuiet;
            camp.volume = g * Mathf.Cos(blend * Mathf.PI * 0.5f);
            run.volume = g * Mathf.Sin(blend * Mathf.PI * 0.5f);
        }

        static void Keep(AudioSource s, bool on)
        {
            if (s.clip == null) return;
            if (on && !s.isPlaying) { s.time = 0f; s.Play(); }
            else if (!on && s.isPlaying) s.Stop();
        }
    }
}
