using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// The wind's levels as the game plays them: each loop's RMS and peak as Unity imported it (AudioClip.GetData: after Force
/// To Mono and any Normalize), then the wind's mixed RMS (wind_low and wind_high together, from SoundMix.WindLevels) for
/// each weather, standing and at full speed, between gusts and in one, beside the ambience bed and the board's glide.
/// Batch: -executeMethod WindCheck.Run -logFile x.log
/// </summary>
public static class WindCheck
{
    static float Rms(string name, out float peakDb)
    {
        var c = Resources.Load<AudioClip>("Audio/" + name);
        peakDb = -99f;
        if (c == null) return -99f;
        var d = new float[c.samples * c.channels];
        if (!c.GetData(d, 0)) return -99f;
        double e = 0; float pk = 0f;
        foreach (var v in d) { e += v * v; pk = Mathf.Max(pk, Mathf.Abs(v)); }
        peakDb = 20f * Mathf.Log10(Mathf.Max(pk, 1e-9f));
        return 10f * Mathf.Log10((float)(e / d.Length) + 1e-20f);
    }

    static float Db(float v) => 20f * Mathf.Log10(Mathf.Max(v, 1e-6f));

    public static void Run()
    {
        foreach (var n in new[] { "wind_low", "wind_high", "ambience", "glide", "carve", "skid", "powder", "avalanche_near", "boost" })
        {
            float r = Rms(n, out float pk);
            Debug.Log($"WINDCHECK clip {n}: RMS {r:0.0} dBFS, peak {pk:0.0} dBFS (as imported)");
        }
        float low = Rms("wind_low", out _), high = Rms("wind_high", out _), amb = Rms("ambience", out _), glide = Rms("glide", out _);
        var weathers = new (string name, float howl)[] { ("CLEAR", 0f), ("WINDY", 0.45f), ("SNOW", 0f), ("STORM", 1f), ("FOG", 0f), ("CLOUD SEA", 0f) };
        Debug.Log($"WINDCHECK SoundMix.WindLow {SoundMix.WindLow}, WindHigh {SoundMix.WindHigh}");
        foreach (var (name, howl) in weathers)
            foreach (float s in new[] { 0f, 1f })
                foreach (float gust in new[] { 0f, 1f })
                {
                    var v = SoundMix.WindLevels(s, false, gust, howl);
                    float lowDb = low + Db(v.x), highDb = v.y > 0f ? high + Db(v.y) : -99f;
                    float mixed = 10f * Mathf.Log10(Mathf.Pow(10f, lowDb / 10f) + (v.y > 0f ? Mathf.Pow(10f, highDb / 10f) : 0f));
                    float ambDb = amb + Db(0.22f * (1f - 0.5f * s)), glideDb = s > 0f ? glide + Db(0.35f + 0.25f * s) : -99f;
                    Debug.Log($"WINDCHECK {name,-9} speed {s:0} gust {gust:0}: wind_low {v.x:0.000} ({lowDb:0.0} dBFS)  wind_high {v.y:0.000} ({(v.y > 0f ? highDb.ToString("0.0") : "off")})  wind mixed {mixed:0.0} dBFS  | ambience {ambDb:0.0}  glide {(s > 0f ? glideDb.ToString("0.0") : "-")}");
                }
        EditorApplication.Exit(0);
    }
}
