using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Summit;

/// <summary>
/// The pause menu, every tab, drawn into the frame at an iPhone's portrait shape over a run in daylight; and checks
/// that it is the one settings sheet: its rows (WEATHER and HOME SCREEN among them, no LENS TYPE), taps on its tabs
/// and rows doing what they say, RIDE greyed mid-run, and an old saved ANAMORPHIC lens type forgotten. Then the same
/// sheet from the start pad and the camp (the sliders in the corner): SETTINGS and DONE, over the camp's menus.
/// Batch: -executeMethod SettingsShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes into $SETTINGSSHOT_OUT (or settings-shots/ in the project folder).
/// </summary>
public static class SettingsShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("SETTINGSSHOT_OUT") ?? "settings-shots";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        PlayerPrefs.SetInt("summit_lenstype", 1);   // an old save: ANAMORPHIC
        TestBoot.Boot(game);
        var rider = game.rider;
        var body = game.body;
        var terrain = game.terrain;
        var props = game.props;
        var cam = Camera.main;
        Physics.simulationMode = SimulationMode.Script;
        int bad = 0;
        void Check(bool ok, string what) { Debug.Log($"SETTINGSSHOT {(ok ? "ok" : "WRONG")}: {what}"); if (!ok) bad++; }
        try
        {
            Check(!PlayerPrefs.HasKey("summit_lenstype"), "the old lens type is forgotten");
            Check(typeof(SummitGame).GetField("lensMode", NP) == null && typeof(LensFX).GetField("ModeName") == null, "no lens type left in the game");
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            float z0 = Course.First + 30f;
            rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
            terrain.Prewarm(); props.Prewarm();
            var hud = Object.FindFirstObjectByType<Hud>();
            typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { true });
            // the HUD is an overlay on the phone, drawn after the picture's post-processing: here a second camera draws
            // it, flat, and it is laid over the picture (as StartPageShot does the start page)
            var ug = new GameObject("SettingsShot UI Camera");
            uiCam = ug.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.SolidColor; uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f); uiCam.cullingMask = 1 << 5; uiCam.nearClipPlane = 0.1f; uiCam.farClipPlane = 10f;
            uiCam.enabled = false; uiCam.aspect = W / (float)H; uiCam.fieldOfView = 40f;
            var ud = ug.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            ud.renderPostProcessing = false; ud.renderShadows = false;
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCam; canvas.planeDistance = 1f;
            foreach (var tr in hud.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 5;
            cam.cullingMask &= ~(1 << 5);
            cam.transform.position = rider.transform.position + new Vector3(0f, 3f, -6f);
            cam.transform.LookAt(rider.transform.position); cam.fieldOfView = 60f;
            Shoot(cam, null);
            // the one settings sheet (the start page has none now: it opens from the camp, the start pad and here)
            typeof(SummitGame).GetMethod("BuildStart", NP).Invoke(game, null);
            var start = (StartPage)typeof(SummitGame).GetField("start", NP).GetValue(game);
            var sheet = hud.PauseSettings;
            var meta = game.meta;
            Debug.Log("SETTINGSSHOT pause rows: " + sheet.Labels());
            Check(typeof(StartPage).GetProperty("Settings") == null, "no settings on the start page");
            Check(sheet.RowIndex("LOOK", "WEATHER") >= 0, "WEATHER is in the pause menu");
            Check(sheet.RowIndex("VIEW", "HOME SCREEN") >= 0, "HOME SCREEN is in the VIEW tab");
            Check(!sheet.Labels().Contains("LENS TYPE"), "no LENS TYPE row anywhere");
            // taps, at the points a finger would land (the canvas is drawn by the camera here, as on the phone it is the screen)
            Canvas.ForceUpdateCanvases();
            sheet.Layout();
            hud.ShowTab(0);
            hud.OnPauseSettings(sheet.TabPoint(2));
            Check(hud.PauseTab == 2, "a tap on GAME shows the GAME tab");
            int turn = sheet.RowIndex("GAME", "TURN"), stop = SteerPrefs.Stop;
            hud.OnPauseSettings(sheet.RowPoint(turn, true));
            Check(SteerPrefs.Stop == stop + 1, $"TURN's right half steps it up ({stop} -> {SteerPrefs.Stop})");
            hud.OnPauseSettings(sheet.RowPoint(turn, false));
            Check(SteerPrefs.Stop == stop, "and its left half back down");
            int blood = sheet.RowIndex("GAME", "BLOOD"); string was = sheet.RowText(blood);
            hud.OnPauseSettings(sheet.RowPoint(blood, true)); string now = sheet.RowText(blood);
            Check(now != was, $"BLOOD toggles ({was} -> {now})");
            hud.OnPauseSettings(sheet.RowPoint(blood, false));
            Check(sheet.RowText(blood) == was, "and back");
            int ride = sheet.RowIndex("GAME", "RIDE"); was = sheet.RowText(ride);
            hud.OnPauseSettings(sheet.RowPoint(ride, true));
            Check(sheet.RowText(ride) == was && was.Contains("(greyed)"), $"RIDE is greyed mid-run ({was})");
            hud.ShowTab(1);
            int wx = sheet.RowIndex("LOOK", "WEATHER"); was = sheet.RowText(wx);
            hud.OnPauseSettings(sheet.RowPoint(wx, true)); now = sheet.RowText(wx);
            hud.OnPauseSettings(sheet.RowPoint(wx, false));
            Check(now != was && sheet.RowText(wx) == was, $"WEATHER steps ({was} -> {now}) and back");
            int home = sheet.RowIndex("VIEW", "HOME SCREEN"); int homeWas = StartPage.Home; was = sheet.RowText(home);
            sheet.RowStep(home, 1); now = sheet.RowText(home);
            Check(now != was && StartPage.Home != homeWas, $"HOME SCREEN switches the start page's view ({was} -> {now})");
            sheet.RowStep(home, 1);
            Check(StartPage.Home == homeWas, "and back");
            // a tap on a hidden tab's row does nothing
            was = sheet.RowText(blood);
            hud.OnPauseSettings(sheet.RowPoint(blood, true));
            Check(sheet.RowText(blood) == was, "a tap where a hidden tab's row would be does nothing");
            Check(!hud.transform.Find("Safe/RestartButton").gameObject.activeInHierarchy, "the PAUSE button is hidden under the menu");
            // (steerB) the TUNE tab: live sliders, dragged; kept; RESET TO DEFAULTS
            hud.OnPauseSettings(sheet.TabPoint(4));
            Check(hud.PauseTab == 4 && Hud.TabName[4] == "TUNE", "a tap on TUNE shows the TUNE tab");
            {
                int sr = sheet.RowIndex("TUNE", "STEER STRENGTH"), rs = sheet.RowIndex("TUNE", "RESET TO DEFAULTS");
                Check(sr >= 0 && sheet.RowSlider(sr) && rs >= 0, "TUNE has its sliders and RESET TO DEFAULTS");
                float def = Rider.SideShare;
                hud.OnPauseSettings(sheet.SliderPoint(sr, 0.9f)); float hi = Rider.SideShare;
                Check(hi > def + 0.05f && hud.PauseDrag(sheet.SliderPoint(sr, 0.9f), true), $"a finger on STEER STRENGTH's rail sets it ({def:0.00} -> {hi:0.00}) and holds it");
                hud.PauseDrag(sheet.SliderPoint(sr, 0.1f), true); float lo = Rider.SideShare;
                Check(lo < def - 0.05f, $"dragged along, it follows ({lo:0.00})");
                Check(sheet.RowText(sr).Contains(lo.ToString("0.00")), $"the value shows beside it ({sheet.RowText(sr)})");
                hud.PauseDrag(sheet.SliderPoint(sr, 0.1f), false);
                Check(!hud.PauseDrag(sheet.SliderPoint(sr, 0.5f), true) && Mathf.Approximately(Rider.SideShare, lo), "let go, it stays where it was left");
                Check(PlayerPrefs.HasKey(Tuning.Prefix + "side") && Mathf.Abs(PlayerPrefs.GetFloat(Tuning.Prefix + "side") - lo) < 1e-4f, "and it is kept (PlayerPrefs)");
                int cam_ = sheet.RowIndex("TUNE", "CAMERA EASE");
                hud.OnPauseSettings(sheet.SliderPoint(cam_, 1f)); hud.PauseDrag(Vector2.zero, false);
                Check(ChaseCamera.YawEase > 0.45f, $"CAMERA EASE moves the camera's own number ({ChaseCamera.YawEase:0.00} s)");
                Shoot(cam, Path.Combine(outDir, "pause_tune_moved.png"));
                hud.OnPauseSettings(sheet.RowPoint(rs, true));
                Check(Mathf.Approximately(Rider.SideShare, def) && !PlayerPrefs.HasKey(Tuning.Prefix + "side") && !Tuning.Changed, $"RESET TO DEFAULTS puts every knob back ({Rider.SideShare:0.00}, camera ease {ChaseCamera.YawEase:0.00} s)");
                Debug.Log("SETTINGSSHOT tune rows: " + string.Join(" | ", System.Linq.Enumerable.Select(Tuning.Knobs, k => k.label + " " + k.Text)));
            }
            for (int k = 0; k < Hud.TabName.Length; k++)
            {
                hud.ShowTab(k);
                string file = Path.Combine(outDir, $"pause_{k}_{Hud.TabName[k].ToLowerInvariant()}.png");
                Shoot(cam, file);
                Debug.Log("SETTINGSSHOT " + file);
            }
            typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { false });
            // (steerB) the pause button: top right, inside the safe area, clear of the right-hand column under it
            {
                var pb = (RectTransform)hud.transform.Find("Safe/RestartButton");
                Check(pb.gameObject.activeInHierarchy && pb.anchorMin == Vector2.one && pb.pivot == Vector2.one, "on a run the pause button is up, in the top right corner");
                Rect R(RectTransform t) { var c = new Vector3[4]; t.GetWorldCorners(c); return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y); }
                Rect pr = R(pb), safeR = R((RectTransform)hud.transform.Find("Safe"));
                Check(safeR.Contains(pr.min) && safeR.Contains(pr.max), "inside the safe area");
                foreach (var nm in new[] { "Safe/FallsLabel", "Safe/Fall0", "Safe/Fall1", "Safe/Fall2", "Safe/Medical", "Safe/Score", "Safe/Slide/Slide", "Safe/Slide/SlideBar" })
                {
                    var t = hud.transform.Find(nm) as RectTransform;
                    if (t == null) { Debug.Log("SETTINGSSHOT (no " + nm + ")"); continue; }
                    var tr = R(t);
                    var tx = t.GetComponent<Text>();
                    if (tx != null)
                    {
                        // (a HUD text's rect is 900 wide: what counts is the words, as long as they get. Filled for the check)
                        string keep = tx.text;
                        if (string.IsNullOrEmpty(keep)) tx.text = nm.EndsWith("Score") ? "1,234,567" : nm.EndsWith("Medical") ? "$12,345 MEDICAL" : nm.EndsWith("FallsLabel") ? "FALLS LEFT" : "AVALANCHE 120 m";
                        float w = tx.preferredWidth * t.lossyScale.x, x0 = Mathf.Lerp(tr.xMin, tr.xMax, t.pivot.x) - w * t.pivot.x;
                        tr = Rect.MinMaxRect(x0, tr.yMin, x0 + w, tr.yMax);
                        tx.text = keep;
                    }
                    // (a right-aligned text grows leftward from its anchor: its top is what matters against the button)
                    bool clear = !tr.Overlaps(pr) || tr.yMax <= pr.yMin;
                    Check(clear, $"the pause button is clear of {nm}");
                }
                var mb = meta.transform.Find("Safe/SettingsButton");
                Check(mb == null || !mb.gameObject.activeInHierarchy, "the camp's settings button (same corner) is not up on a run");
                Shoot(cam, Path.Combine(outDir, "run_hud_pause.png"));
            }

            // ---- the same sheet from the start pad and the camp: the sliders in the corner, SETTINGS and DONE
            var sliders = meta.transform.Find("Safe/SettingsButton");
            var open = typeof(SummitGame).GetMethod("OpenSettings", NP); var close = typeof(SummitGame).GetMethod("CloseSettings", NP);
            var step = typeof(SummitGame).GetMethod("SettingsStep", NP);
            Text Title() => hud.transform.Find("Pause/PauseTop/PausedTitle").GetComponent<Text>();
            bool On(string path) { var t = hud.transform.Find(path); return t != null && t.gameObject.activeInHierarchy; }
            foreach (var (where, call, arg) in new (string, string, object)[] { ("ready", "ToReady", false), ("camp", "ToHub", false) })
            {
                typeof(SummitGame).GetMethod(call, NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { arg });
                typeof(MetaUi).GetMethod("Update", NP).Invoke(meta, null);
                Check(sliders != null && sliders.gameObject.activeInHierarchy, $"{where}: the sliders show");
                Check(meta.Hit(RectTransformUtility.WorldToScreenPoint(null, sliders.position)) == "settings", $"{where}: a tap on them is the settings");
                open.Invoke(game, null);
                Check(hud.Paused && Title().text == "SETTINGS" && On("Pause/PauseTop/Done") && !On("Pause/PauseTop/Resume") && !On("Pause/PauseTop/RestartRun"),
                      $"{where}: SETTINGS with DONE (no RESUME or RESTART RUN)");
                Check(canvas.sortingOrder > meta.GetComponent<Canvas>().sortingOrder, $"{where}: the sheet is over the camp's menus");
                Check((bool)step.Invoke(game, null), $"{where}: while it's up a tap is the sheet's");
                hud.ShowTab(0);
                if (where == "ready") { cam.transform.position = rider.transform.position + new Vector3(0f, 3f, -6f); cam.transform.LookAt(rider.transform.position); }
                Shoot(cam, Path.Combine(outDir, $"settings_{where}.png"));
                close.Invoke(game, null);
                Check(!hud.Paused && !(bool)step.Invoke(game, null), $"{where}: DONE puts it away");
            }
            typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { true });
            Check(Title().text == "PAUSED" && On("Pause/PauseTop/Resume") && !On("Pause/PauseTop/Done"), "on a run the pause menu is PAUSED with RESUME");
            typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { false });
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; Object.DestroyImmediate(uiCam.gameObject); uiCam = null;
        }
        catch (System.Exception e) { Debug.LogError("SETTINGSSHOT exception " + e); bad++; }
        Debug.Log($"SETTINGSSHOT done: {bad} wrong");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(bad == 0 ? 0 : 1);
    }

    static Camera uiCam;

    static void Shoot(Camera cam, string file)
    {
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        Canvas.ForceUpdateCanvases();
        cam.Render();
        if (file != null)
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null;
            if (uiCam != null) Over(tex);
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        cam.targetTexture = null; RenderTexture.active = null;
        rt.Release(); Object.DestroyImmediate(rt);
    }

    /// <summary>The HUD laid over the picture as the phone's overlay canvas is (see StartPageShot.Over).</summary>
    static void Over(Texture2D picture)
    {
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        uiCam.targetTexture = rt; Canvas.ForceUpdateCanvases(); uiCam.Render(); uiCam.targetTexture = null;
        RenderTexture.active = rt;
        var page = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
        page.ReadPixels(new Rect(0, 0, W, H), 0, 0); page.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        var a = picture.GetPixels(); var b = page.GetPixels();
        for (int i = 0; i < a.Length; i++)
        {
            float cover = Mathf.Sqrt(b[i].a);
            Color under = a[i].linear, over = b[i];
            a[i] = new Color(over.r + under.r * (1f - cover), over.g + under.g * (1f - cover), over.b + under.b * (1f - cover), 1f).gamma;
        }
        picture.SetPixels(a); picture.Apply();
        Object.DestroyImmediate(page);
    }
}
