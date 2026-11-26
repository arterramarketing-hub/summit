using System.IO;
using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// treesound: the wood-breaking layers (TreeSounds: recorded takes since treesfx) written out as WAV files to listen to and measure, and how long
/// loading them takes.   Unity -batchmode -nographics -projectPath . -executeMethod TreeSoundDump.Run   (TREESOUND_OUT=dir)
/// </summary>
public static class TreeSoundDump
{
    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("TREESOUND_OUT") ?? "treesound-wavs";
        Directory.CreateDirectory(dir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sets = new[] { ("twig", TreeSounds.Twigs()), ("limb", TreeSounds.Limbs()), ("trunk", TreeSounds.Trunks()), ("thud", TreeSounds.Thuds()), ("snow", TreeSounds.Snow()) };
        double ms = sw.Elapsed.TotalMilliseconds;
        long samples = 0;
        foreach (var (name, clips) in sets)
            for (int k = 0; k < clips.Length; k++)
            {
                var c = clips[k]; samples += c.samples;
                var d = new float[c.samples]; c.GetData(d, 0);
                Wav(Path.Combine(dir, $"{name}_{k}.wav"), d, c.frequency);
            }
        Debug.Log($"TREESOUND loaded {sets.Length} layers ({sets[0].Item2.Length}/{sets[1].Item2.Length}/{sets[2].Item2.Length}/{sets[3].Item2.Length}/{sets[4].Item2.Length} takes), {samples} samples ({samples * 4 / 1048576f:0.0} MB as float) in {ms:0} ms (editor)");
        EditorApplication.Exit(0);
    }

    static void Wav(string file, float[] d, int rate)
    {
        using var w = new BinaryWriter(File.Create(file));
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + d.Length * 2);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(d.Length * 2);
        foreach (var x in d) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(x * 32767f), -32768, 32767));
    }
}
