using UnityEngine;

namespace Summit
{
    // SummitGame, part: every setting (the pause menu's and the start page's sheets share these rows)
    public partial class SummitGame
    {
        // pause menu and the camera distance setting
        bool paused;
        static readonly float[] CamDist = { 0.65f, 0.82f, 1f, 1.22f, 1.5f };
        static readonly string[] CamName = { "CLOSEST", "CLOSE", "NORMAL", "FAR", "FARTHEST" };
        int camLevel = 2;
        // camera angle (height) and lens (field of view and barrel distortion), from the pause menu
        static readonly float[] AngleHeight = { 0.62f, 0.9f, 1.1f, 1.55f, 2.2f };   // times ChaseCamera's Lift/Clear heights
        static readonly string[] AngleName = { "GROUND", "LOW", "NORMAL", "HIGH", "OVERHEAD" };
        static readonly float[] LensFov = { -14f, 0f, 8f, 18f };
        static readonly float[] LensWarp = { 0f, -0.28f, -0.42f, -0.6f };
        static readonly string[] LensName = { "STANDARD", "WIDE", "WIDER", "FISHEYE" };
        int angleLevel = 1, lensLevel = 1;
        bool pov;   // first person instead of the chase camera (the VIEW button; ChaseCamera does the rest)
        UnityEngine.Rendering.Universal.LensDistortion lens;
        // colour filter (LookGrade) and its strength in tenths, from the settings
        int filterLevel, filterStrength = 10;
        int halation;   // the film glow round highlights (LensFX): OFF, SUBTLE, STRONG
        LensFX lensFx;
        internal DayNight dayNight;   // the time of day's light (TimeOfDay, DayNight.cs), and the pause menu's TIME row
        Weather weather;     // rolled for each new mountain (Weather.cs); null in the test tools unless they ask for one
        UnityEngine.Rendering.VolumeProfile post;

        void ApplyCamera()
        {
            chase.heightScale = AngleHeight[angleLevel];
            chase.extraFov = LensFov[lensLevel];
            lensFx.Apply(lens, LensWarp[lensLevel], lensLevel);
            hud.RefreshPause();
            PlayerPrefs.SetInt("summit_angle", angleLevel); PlayerPrefs.SetInt("summit_lens", lensLevel); PlayerPrefs.Save();
        }

        void SetPov(bool on)
        {
            pov = on; chase.pov = on;
            hud.RefreshPause();
            PlayerPrefs.SetInt(ChaseCamera.PovPref, on ? 1 : 0); PlayerPrefs.Save();
        }

        void ApplyLook()
        {
            atmo.BaseVignette = LookGrade.Apply(post, filterLevel, filterStrength / 10f);
            lensFx.SetHalation(halation);
            hud.RefreshPause();
            PlayerPrefs.SetInt("summit_filter", filterLevel); PlayerPrefs.SetInt("summit_filterstrength", filterStrength); PlayerPrefs.SetInt("summit_halation", halation); PlayerPrefs.Save();
        }

        // the SOUND tab's music volume, in steps of 10%
        void StepMusic(int dir)
        {
            int v = Mathf.Clamp(Mathf.RoundToInt(AudioPrefs.MusicVolume * 10f) + dir, 0, 10);
            AudioPrefs.MusicVolume = v / 10f;
            hud.RefreshPause();
        }

        // the GAME tab's TURN: how sharp the carve is (SteerPrefs, Rider.TurnScale), felt the moment the run resumes
        void StepTurn(int dir) => SetTurnStop(SteerPrefs.Stop + dir);
        void SetTurnStop(int k)
        {
            SteerPrefs.Stop = k;
            hud.RefreshPause();
        }

        // ---- every setting has one method here, which the pause menu's and the start page's settings both call
        //      (FillSettings hands them to both sheets as rows), so the two can never disagree and a change made in
        //      one shows in the other
        void StepCam(int dir) => SetCamLevel(camLevel + dir);
        void StepAngle(int dir) { angleLevel = Mathf.Clamp(angleLevel + dir, 0, AngleHeight.Length - 1); ApplyCamera(); }
        void StepLens(int dir) { lensLevel = Mathf.Clamp(lensLevel + dir, 0, LensFov.Length - 1); ApplyCamera(); }
        void StepFilter(int dir) { filterLevel = (filterLevel + dir + LookGrade.Names.Length) % LookGrade.Names.Length; ApplyLook(); }
        void StepStrength(int dir) { filterStrength = Mathf.Clamp(filterStrength + dir, 0, 10); ApplyLook(); }
        void StepHalation(int dir) { halation = Mathf.Clamp(halation + dir, 0, LensFX.HalationName.Length - 1); ApplyLook(); }
        void StepTime(int dir) { TimeOfDay.StepLock(dir); hud.RefreshPause(); }
        void ToggleSteer() { input.SetTilt(!input.useTilt); hud.SetMode(input.useTilt); hud.RefreshPause(); }
        // how the one finger jumps: a double tap (the default), a tap, or a flick up (RiderInput, FingerGesture)
        void StepJump(int dir) { input.SetJumpStyle((JumpStyle)(((int)input.jumpStyle + dir + 3) % 3)); hud.RefreshPause(); if (state == State.Ready) ShowReadyHint(); }
        string JumpName => RiderInput.JumpNames[(int)input.jumpStyle];
        /// <summary>The jump, in words, for the hints: as the JUMP setting has it.</summary>
        string JumpWords => input.jumpStyle == JumpStyle.Tap ? "tap" : input.jumpStyle == JumpStyle.FlickUp ? "flick up" : "double-tap";
        // (tilt steering the other way round: the game has always read "summit_invert", but nothing could set it)
        void ToggleInvert() { input.invertTilt = !input.invertTilt; PlayerPrefs.SetInt("summit_invert", input.invertTilt ? 1 : 0); PlayerPrefs.Save(); }
        void ToggleTrees() { props.SetRealistic(!props.IsRealistic); hud.RefreshPause(); }
        void ToggleVoice() { SoundMix.VoiceLines = !SoundMix.VoiceLines; hud.RefreshPause(); PlayerPrefs.SetInt("summit_voicelines", SoundMix.VoiceLines ? 1 : 0); PlayerPrefs.Save(); }
        void ToggleFlashlight() { DayNight.FlashlightOn = !DayNight.FlashlightOn; hud.RefreshPause(); PlayerPrefs.SetInt("summit_flashlight", DayNight.FlashlightOn ? 1 : 0); PlayerPrefs.Save(); dayNight.ApplyNow(); }
        void ToggleBlood() { blood.SetOn(!blood.On); hud.RefreshPause(); PlayerPrefs.SetInt("summit_blood", blood.On ? 1 : 0); PlayerPrefs.Save(); }

        /// <summary>
        /// Every setting there is, a row each under four headings, onto a settings sheet: the start page's and the pause
        /// menu's get the very same rows (one list, here), so the two read and behave alike.
        /// </summary>
        void FillSettings(SettingsSheet s)
        {
            string OnOff(bool on) => on ? "ON" : "OFF";
            int view = s.AddTab(Hud.TabName[0]), look = s.AddTab(Hud.TabName[1]), game = s.AddTab(Hud.TabName[2]), sound = s.AddTab(Hud.TabName[3]);
            s.AddRow(view, "VIEW", () => pov ? "FIRST PERSON" : "CHASE CAMERA", d => SetPov(!pov), true);
            s.AddRow(view, "CAMERA DISTANCE", () => pov ? "N/A" : CamName[camLevel], StepCam, false, () => !pov);
            s.AddRow(view, "CAMERA ANGLE", () => pov ? "N/A" : AngleName[angleLevel], StepAngle, false, () => !pov);
            s.AddRow(view, "LENS", () => LensName[lensLevel], StepLens);
            s.AddRow(view, "HOME SCREEN", () => StartPage.HomeName, d => { StartPage.StepHome(d); hud.RefreshPause(); }, true);
            s.AddRow(look, "TIME OF DAY", () => TimeOfDay.LockLabel, StepTime);
            s.AddRow(look, "WEATHER", () => Weather.ChoiceLabel, Weather.StepChoice);
            s.AddRow(look, "FILTER", () => LookGrade.Names[filterLevel], StepFilter);
            s.AddRow(look, "FILTER STRENGTH", () => filterStrength * 10 + "%", StepStrength);
            s.AddRow(look, "HALATION", () => LensFX.HalationName[halation], StepHalation);
            s.AddRow(game, "TURN", () => SteerPrefs.Label, StepTurn);
            s.AddRow(game, "STEERING", () => input.useTilt ? "TILT" : "DRAG", d => ToggleSteer(), true);
            s.AddRow(game, "JUMP", () => JumpName, StepJump);
            s.AddRow(game, "INVERT TILT", () => OnOff(input.invertTilt), d => ToggleInvert(), true, () => input.useTilt);
            // (not mid-run: what you ride is changed on the start pad, or before you go)
            s.AddRow(game, "RIDE", () => RideName, StepRide, false, () => state != State.Riding);
            s.AddRow(game, "TREES", () => props.IsRealistic ? "REALISTIC" : "ORIGINAL", d => ToggleTrees(), true);
            s.AddRow(game, "FLASHLIGHT", () => OnOff(DayNight.FlashlightOn), d => ToggleFlashlight(), true);
            s.AddRow(game, "BLOOD", () => OnOff(blood.On), d => ToggleBlood(), true);
            s.AddRow(sound, "MUSIC", () => Mathf.RoundToInt(AudioPrefs.MusicVolume * 10f) * 10 + "%", StepMusic);
            s.AddRow(sound, "VOICE LINES", () => OnOff(SoundMix.VoiceLines), d => ToggleVoice(), true);
            // (steerB) the feel's own numbers, live: drag one mid-run, resume, and feel it (Tuning; kept in PlayerPrefs)
            if (Tuning.Enabled)
            {
                int tune = s.AddTab(Hud.TabName[4]);
                foreach (var k in Tuning.Knobs)
                {
                    var kk = k;
                    s.AddSlider(tune, kk.label, () => kk.Text, () => kk.T, t => kk.T = t, () => Tuning.Save(kk));
                }
                s.AddButton(tune, "RESET TO DEFAULTS", "RESET", () => { Tuning.Reset(); hud.RefreshPause(); });
            }
        }
    }
}
