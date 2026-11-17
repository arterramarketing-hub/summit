using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Checks the soundtrack without listening: how the two loops import (stereo, streamed, Vorbis), and how the
/// music's volume moves as the game goes from the camp to the run, the avalanche closes in, the rider crashes
/// and gets buried. Batch: -executeMethod MusicTest.Run
/// </summary>
public static class MusicTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        foreach (var name in new[] { "music_hub", "music_run" })
        {
            string path = "Assets/Summit/Resources/Audio/" + name + ".wav";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var s = imp.defaultSampleSettings;
            var comp = typeof(AudioImporter).GetProperty("compSize", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(imp);
            Debug.Log($"MUSIC {name}: {clip.channels} ch {clip.frequency} Hz {clip.length:0.00} s ({clip.samples} samples), " +
                      $"load {s.loadType}, {s.compressionFormat} q{s.quality:0.00}, mono={imp.forceToMono}, compressed {comp ?? "?"} bytes");
        }

        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game, false);
        var fx = (SoundMix)typeof(SummitGame).GetField("audioFx", NP).GetValue(game);
        var music = (Music)typeof(SoundMix).GetField("music", NP).GetValue(fx);
        var srcs = music.GetComponents<AudioSource>();
        var lp = music.GetComponent<AudioLowPassFilter>();
        float t = 0f;
        void Step(string what, float sec, RideSound r)
        {
            for (float e = 0f; e < sec - 1e-4f; e += 0.1f) { music.Set(r, 0.1f); t += 0.1f; }
            Debug.Log($"MUSIC t={t:0.0} {what}: camp {srcs[0].volume:0.000} run {srcs[1].volume:0.000} low-pass {lp.cutoffFrequency:0} Hz");
        }
        AudioPrefs.MusicVolume = 1f;
        Step("camp, fading in", 1f, new RideSound { camp = true });
        Step("camp", 2f, new RideSound { camp = true });
        Step("ready: crossfade 0.8 s in", 0.8f, new RideSound());
        Step("crossfade done", 1.2f, new RideSound());
        Step("riding", 2f, new RideSound { riding = true });
        Step("avalanche 0.7", 1f, new RideSound { riding = true, avalanche = 0.7f });
        Step("avalanche right behind", 1f, new RideSound { riding = true, avalanche = 1f });
        Step("away again", 1f, new RideSound { riding = true });
        fx.Play(Sfx.HitTree);
        Step("hit a tree, 0.5 s on", 0.5f, new RideSound { riding = true, muffle = 0.3f });
        Step("2.5 s on", 2f, new RideSound { riding = true });
        AudioPrefs.MusicVolume = 0.5f;
        Step("music volume set to half", 0.1f, new RideSound { riding = true });
        AudioPrefs.MusicVolume = 1f;
        Step("BURIED screen", 2f, new RideSound { ended = true, muffle = 1f });
        Step("results screen (not buried)", 2f, new RideSound { ended = true, muffle = 0.45f });
        Step("back to camp", 2f, new RideSound { camp = true });
        Step("paused (dt 0): nothing moves", 0f, new RideSound());
        music.Set(new RideSound(), 0f);
        Debug.Log($"MUSIC after a paused frame asking for the run: camp {srcs[0].volume:0.000} run {srcs[1].volume:0.000}");
        PlayerPrefs.DeleteKey("summit_music");   // leave the editor's saved music volume at its default
        EditorApplication.Exit(0);
    }
}
