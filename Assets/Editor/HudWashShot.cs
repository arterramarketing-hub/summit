using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Summit;

/// <summary>
/// The HUD's washes and the avalanche's bar, which sit on canvases of their own inside the HUD's (they change every
/// frame; Hud.Build): rendered mid-run with the slide on its way, the whiteout part way up and a red flash, then with
/// all of them clear. Logs where each sits and whether the clear washes are culled.
/// Run: Unity -batchmode -quit -projectPath . -executeMethod HudWashShot.Run -logFile hudwash.log  (no -nographics)
/// Writes HudWash_*.png; log lines start HUDWASH.
/// </summary>
public static class HudWashShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var hud = Get<Hud>("hud");
        var cam = Camera.main;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        var hc = hud.GetComponent<Canvas>(); hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
        foreach (var name in new[] { "whiteout", "hurt", "slideFill" })
        {
            var img = (Image)typeof(Hud).GetField(name, NP).GetValue(hud);
            var nested = img.canvas;
            Debug.Log($"HUDWASH {name}: on canvas '{nested.name}' (nested {!nested.isRootCanvas}, under '{nested.rootCanvas.name}') cull clear {img.canvasRenderer.cullTransparentMesh}");
        }
        // the slide 70 m back of 160, closing in: the bar, its "SLIDE 70 m", some white, and a flash
        hud.SetRun(64f, 1200, 1f, 70f, 160f, true);
        hud.SetWhiteout(0.3f); hud.Hurt(0.45f);
        typeof(Hud).GetMethod("Update", NP).Invoke(hud, null);
        Canvas.ForceUpdateCanvases();
        Shoot(cam, "HudWash_on.png");
        hud.SetRun(64f, 1200, 1f, 9999f, 160f, true);
        hud.SetWhiteout(0f);
        typeof(Hud).GetField("hurtA", NP).SetValue(hud, 0.001f);
        typeof(Hud).GetMethod("Update", NP).Invoke(hud, null);   // (fades the last of the flash to clear)
        Canvas.ForceUpdateCanvases();
        Shoot(cam, "HudWash_off.png");
        hc.renderMode = RenderMode.ScreenSpaceOverlay;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Shoot(Camera cam, string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null; rt.Release();
        Debug.Log("HUDWASH wrote " + file);
    }
}
