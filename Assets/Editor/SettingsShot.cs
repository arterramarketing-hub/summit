using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The pause menu, every tab, drawn into the frame at an iPhone's portrait shape over a run in daylight; and checks
/// that it is the start page's settings sheet: the same rows (WEATHER among them, no LENS TYPE), taps on its tabs
/// and rows doing what they say, RIDE greyed mid-run, and an old saved ANAMORPHIC lens type forgotten.
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
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
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
            // the same rows as the start page's
            typeof(SummitGame).GetMethod("BuildStart", NP).Invoke(game, null);
            var start = (StartPage)typeof(SummitGame).GetField("start", NP).GetValue(game);
            var sheet = hud.PauseSettings;
            Debug.Log("SETTINGSSHOT pause rows: " + sheet.Labels());
            Check(sheet.Labels() == start.Settings.Labels(), "the pause menu's rows are the start page's");
            Check(sheet.RowIndex("LOOK", "WEATHER") >= 0, "WEATHER is in the pause menu");
            Check(!sheet.Labels().Contains("LENS TYPE") && !start.Settings.Labels().Contains("LENS TYPE"), "no LENS TYPE row anywhere");
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
            Check(start.Settings.RowText(wx) == was, "and the start page reads the same");
            // a tap on a hidden tab's row does nothing
            was = sheet.RowText(blood);
            hud.OnPauseSettings(sheet.RowPoint(blood, true));
            Check(sheet.RowText(blood) == was, "a tap where a hidden tab's row would be does nothing");
            Check(!hud.transform.Find("Safe/RestartButton").gameObject.activeInHierarchy, "the PAUSE button is hidden under the menu");
            for (int k = 0; k < Hud.TabName.Length; k++)
            {
                hud.ShowTab(k);
                string file = Path.Combine(outDir, $"pause_{k}_{Hud.TabName[k].ToLowerInvariant()}.png");
                Shoot(cam, file);
                Debug.Log("SETTINGSSHOT " + file);
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; Object.DestroyImmediate(uiCam.gameObject); uiCam = null;
            typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { false });
        }
        catch (System.Exception e) { Debug.LogError("SETTINGSSHOT exception " + e); bad++; }
        Debug.Log($"SETTINGSSHOT done: {bad} wrong");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(bad == 0 ? 0 : 1);
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
