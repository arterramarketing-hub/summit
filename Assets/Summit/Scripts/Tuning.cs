using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// steerB: the feel's own numbers, live, for playtesting: the pause menu's TUNE tab draws one slider per knob (SettingsSheet
    /// AddSlider), each setting the very static the physics or the camera reads, so a value dragged mid-run is felt the
    /// moment the run resumes. Kept in PlayerPrefs ("summit_tune_" + key); RESET TO DEFAULTS puts back the numbers the
    /// game ships with (taken from the statics before anything saved is applied).
    /// </summary>
    public static class Tuning
    {
        public class Knob
        {
            public string label, key, unit;
            public float min, max, def, step;
            public System.Func<float> get;
            public System.Action<float> set;
            /// <summary>The value as the slider shows it.</summary>
            public string Text => Format(get());
            public string Format(float v) => (step >= 1f ? v.ToString("0") : step >= 0.1f ? v.ToString("0.0") : v.ToString("0.00")) + unit;
            /// <summary>Where it sits along its slider, 0..1.</summary>
            public float T { get => Mathf.InverseLerp(min, max, get()); set => Set(Mathf.Lerp(min, max, Mathf.Clamp01(value))); }
            public void Set(float v)
            {
                v = Mathf.Clamp(v, min, max);
                if (step > 0f) v = Mathf.Round(v / step) * step;
                set(v);
            }
        }

        /// <summary>
        /// The one switch for all of it: the TUNE tab in the pause menu, and the saved numbers put over the game's own at
        /// start. On, in release builds too, while the owner playtests release builds on the phone; off, the game rides
        /// on the numbers it ships with and the tab is not there (anything saved stays saved, unused).
        /// </summary>
        public static readonly bool Enabled = true;

        public const string Prefix = "summit_tune_";
        public static readonly List<Knob> Knobs = new List<Knob>();
        static bool loaded;

        static void K(string label, string key, float min, float max, float step, string unit, System.Func<float> get, System.Action<float> set)
            => Knobs.Add(new Knob { label = label, key = key, min = min, max = max, step = step, unit = unit, get = get, set = set, def = get() });

        static Tuning()
        {
            // (the defaults are read here, from the statics as compiled, before Load applies anything saved)
            K("STEER STRENGTH", "side", 0.3f, 1.2f, 0.01f, "", () => Rider.SideShare, v => Rider.SideShare = v);
            K("STEER AT SPEED", "sidefast", 0f, 0.4f, 0.01f, "", () => Rider.SideShareFast, v => Rider.SideShareFast = v);
            K("INPUT CURVE", "expo", 0.6f, 2.5f, 0.05f, "", () => Rider.InputExpo, v => Rider.InputExpo = v);
            K("EDGE RAMP", "ramp", 0.05f, 0.8f, 0.01f, " s", () => Rider.RampTime, v => Rider.RampTime = v);
            K("COAST TIME", "coast", 0f, 1.5f, 0.05f, " s", () => Rider.CoastTime, v => Rider.CoastTime = v);
            K("RETURN TIME", "return", 0.5f, 6f, 0.1f, " s", () => Rider.ReturnTime, v => Rider.ReturnTime = v);
            K("BOARD FOLLOW", "yawshare", 0.3f, 1f, 0.01f, "", () => Rider.YawShare, v => Rider.YawShare = v);
            K("BOARD MAX TURN", "yawcap", 15f, 85f, 1f, "°", () => Rider.YawCap * Mathf.Rad2Deg, v => Rider.YawCap = v * Mathf.Deg2Rad);
            K("CAMERA FOLLOW", "camshare", 0.3f, 1f, 0.01f, "", () => ChaseCamera.TravelShare, v => ChaseCamera.TravelShare = v);
            K("CAMERA EASE", "camease", 0.02f, 0.5f, 0.01f, " s", () => ChaseCamera.YawEase, v => ChaseCamera.YawEase = v);
            K("AIR CONTROL", "airdrift", 0f, 25f, 0.5f, "", () => Rider.AirDrift, v => Rider.AirDrift = v);
            K("AIR BUDGET", "airmost", 0f, 10f, 0.1f, " m/s", () => Rider.AirSideMost, v => Rider.AirSideMost = v);
            K("PACE", "pace", 0.6f, 1.6f, 0.01f, "", () => Rider.Pace, v => Rider.Pace = v);
            K("FALL LINE LAG", "falllag", 0.05f, 1.2f, 0.01f, " s", () => Rider.FallLineLag, v => Rider.FallLineLag = v);
        }

        /// <summary>The PlayerPrefs keys (the test tools pin them).</summary>
        public static IEnumerable<string> Keys() { foreach (var k in Knobs) yield return Prefix + k.key; }

        /// <summary>Apply what was saved (once, at start).</summary>
        public static void Load()
        {
            loaded = true;
            if (!Enabled) return;
            foreach (var k in Knobs) if (PlayerPrefs.HasKey(Prefix + k.key)) k.Set(PlayerPrefs.GetFloat(Prefix + k.key));
        }

        /// <summary>Keep one knob's value (as a drag on its slider ends).</summary>
        public static void Save(Knob k) { PlayerPrefs.SetFloat(Prefix + k.key, k.get()); PlayerPrefs.Save(); }

        /// <summary>Every knob back to the numbers the game ships with, and nothing saved.</summary>
        public static void Reset()
        {
            foreach (var k in Knobs) { k.set(k.def); PlayerPrefs.DeleteKey(Prefix + k.key); }
            PlayerPrefs.Save();
        }

        /// <summary>Anything moved off its default.</summary>
        public static bool Changed { get { foreach (var k in Knobs) if (Mathf.Abs(k.get() - k.def) > k.step * 0.5f + 1e-5f) return true; return false; } }
        public static bool Loaded => loaded;
    }
}
