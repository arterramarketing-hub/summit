using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// The saved settings, held still for a test run. PlayerPrefs live in one file per Mac
/// (~/Library/Preferences/unity.DefaultCompany.Summit.plist), shared by every project folder and every harness: the
/// player's choices in the game, and whatever another session's harness last left in them (StartPageShot steps every
/// setting, HubShot buys upgrades). A test that reads them gives a different answer on a different day.
/// Pin() remembers every key the game reads, clears them all (a fresh player: the game's own defaults), applies
/// SUMMIT_PREFS overrides ("summit_trees=0;summit_ride=1", for flipping one on purpose), and puts the player's own
/// values back when the run ends (Restore(), also hooked to the editor quitting), so a test run leaves them as it
/// found them.
/// </summary>
public static class TestPrefs
{
    enum Kind { Int, Float, String }

    static readonly Dictionary<string, Kind> keys = new Dictionary<string, Kind>();
    static Dictionary<string, object> saved;

    static void Keys()
    {
        if (keys.Count > 0) return;
        foreach (var k in new[] {
            "summit_best", "summit_camdist", "summit_angle", "summit_lens", "summit_lenstype", "summit_filter",
            "summit_filterstrength", "summit_halation", "summit_blood", ChaseCamera.PovPref, "summit_tube",
            "summit_voicelines", "summit_flashlight", "summit_timelock", "summit_points", "summit_points_life",
            "summit_ride", Forest.TreesPref, "summit_invert", "summit_tilt",
            "summit_jacket", "summit_jackets", "summit_boardcol", "summit_boards", "summit_weather",
            // (no longer read on main, still written by other branches; summit_lodge2: the lodge's layout, gone with campsnow)
            "summit_rider", "summit_body", "summit_outfit", "summit_lodge", "summit_lodge2", "summit_oldhero" })
            keys[k] = Kind.Int;
        foreach (Summit.Progress.Up u in System.Enum.GetValues(typeof(Summit.Progress.Up))) keys["summit_up_" + u] = Kind.Int;
        foreach (Wardrobe.Cat c in System.Enum.GetValues(typeof(Wardrobe.Cat))) { keys["summit_wear_" + c] = Kind.Int; keys["summit_own_" + c] = Kind.Int; }
        foreach (var k in Summit.Progress.SetKeys()) keys[k] = Kind.Int;   // (each set of clothes' own colours)
        foreach (var k in Summit.Progress.RiderKeys()) keys[k] = Kind.Int;   // (each rider's own board graphic)
        keys["summit_clock"] = Kind.Float;
        keys["summit_music"] = Kind.Float;
        keys[SteerPrefs.TurnKey] = Kind.Float;
        foreach (var k in Tuning.Keys()) keys[k] = Kind.Float;   // (steerB: the pause menu's TUNE sliders)
        keys["summit_missions"] = Kind.String;
    }

    /// <summary>Remember the player's settings, then run as a fresh player (plus any SUMMIT_PREFS overrides).</summary>
    public static void Pin()
    {
        Keys();
        if (saved == null)
        {
            saved = new Dictionary<string, object>();
            foreach (var kv in keys)
            {
                if (!PlayerPrefs.HasKey(kv.Key)) continue;
                saved[kv.Key] = kv.Value == Kind.Int ? PlayerPrefs.GetInt(kv.Key) : kv.Value == Kind.Float ? PlayerPrefs.GetFloat(kv.Key) : (object)PlayerPrefs.GetString(kv.Key);
            }
            EditorApplication.quitting += Restore;
        }
        foreach (var k in keys.Keys) PlayerPrefs.DeleteKey(k);
        var over = System.Environment.GetEnvironmentVariable("SUMMIT_PREFS");
        if (!string.IsNullOrEmpty(over))
            foreach (var pair in over.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string k = pair.Substring(0, eq).Trim(), val = pair.Substring(eq + 1).Trim();
                if (int.TryParse(val, out int i)) PlayerPrefs.SetInt(k, i);
                else if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) PlayerPrefs.SetFloat(k, f);
                else PlayerPrefs.SetString(k, val);
                Debug.Log($"TESTPREFS {k} = {val}");
            }
        // anything the game draws from Unity's shared random numbers starts from the same place every run
        // (SUMMIT_RANDOM=<n> starts it somewhere else, SUMMIT_RANDOM=off leaves it where the editor left it)
        var rs = System.Environment.GetEnvironmentVariable("SUMMIT_RANDOM");
        int seed = int.TryParse(rs, out int n) ? n : 20261120;
        if (rs != "off") Random.InitState(seed);
        Debug.Log($"TESTPREFS random {(rs == "off" ? "left as the editor started it" : "seeded " + seed)}, next draw {Random.state.GetHashCode()}");
        Debug.Log($"TESTPREFS pinned {keys.Count} keys to a fresh player ({saved.Count} of the player's own kept to put back)");
    }

    /// <summary>Put the player's own settings back as they were before Pin.</summary>
    public static void Restore()
    {
        if (saved == null) return;
        EditorApplication.quitting -= Restore;
        foreach (var k in keys.Keys) PlayerPrefs.DeleteKey(k);
        foreach (var kv in saved)
        {
            if (kv.Value is int i) PlayerPrefs.SetInt(kv.Key, i);
            else if (kv.Value is float f) PlayerPrefs.SetFloat(kv.Key, f);
            else PlayerPrefs.SetString(kv.Key, (string)kv.Value);
        }
        PlayerPrefs.Save();
        Debug.Log($"TESTPREFS restored {saved.Count} of the player's settings");
        saved = null;
    }
}
