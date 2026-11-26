using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The pause menu's GAME tab with its TURN slider, drawn into the frame at an iPhone's portrait shape: at the stock
/// setting, at the gentlest and at the sharpest. Also checks that the slider's stops reach the rider (Rider.TurnScale)
/// and are saved, and puts the player's own setting back after.
/// Batch: -executeMethod TurnMenuShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes into $TURNSHOT_OUT (or turn-shots/ in the project folder).
/// </summary>
public static class TurnMenuShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("TURNSHOT_OUT") ?? "turn-shots";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();   // (the player's own settings, kept; the stock ones for the run)
        TestBoot.Boot(game);
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var cam = Camera.main;
        Physics.simulationMode = SimulationMode.Script;
        int ok = 0, bad = 0;
        try
        {
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            float z0 = Course.First + 30f;
            rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
            terrain.Prewarm(); props.Prewarm();
            var hud = Object.FindFirstObjectByType<Hud>();
            typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { true });
            hud.ShowTab(2);
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
            cam.transform.position = rider.transform.position + new Vector3(0f, 3f, -6f);
            cam.transform.LookAt(rider.transform.position); cam.fieldOfView = 60f;
            var set = typeof(SummitGame).GetMethod("SetTurnStop", NP);
            Shoot(cam, Path.Combine(outDir, "TurnWarmup.png"));
            foreach (var (stop, name) in new[] { (SteerPrefs.Middle, "TurnPause_1.0.png"), (0, "TurnPause_0.5.png"), (SteerPrefs.Stops.Length - 1, "TurnPause_2.5.png"), (7, "TurnPause_1.6.png") })
            {
                set.Invoke(game, new object[] { stop });
                bool reached = Mathf.Approximately(Rider.TurnScale, SteerPrefs.Stops[stop]) && Mathf.Approximately(PlayerPrefs.GetFloat(SteerPrefs.TurnKey), SteerPrefs.Stops[stop]) && SteerPrefs.Stop == stop;
                if (reached) ok++; else bad++;
                Debug.Log($"TURNSHOT stop {stop}: label {SteerPrefs.Label} scale {Rider.TurnScale:0.0} saved {PlayerPrefs.GetFloat(SteerPrefs.TurnKey):0.0} {(reached ? "ok" : "WRONG")}");
                Shoot(cam, Path.Combine(outDir, name));
            }
            // the - and + ends stop at the ends
            set.Invoke(game, new object[] { 0 }); typeof(SummitGame).GetMethod("StepTurn", NP).Invoke(game, new object[] { -1 });
            if (SteerPrefs.Stop == 0) ok++; else bad++;
            set.Invoke(game, new object[] { SteerPrefs.Stops.Length - 1 }); typeof(SummitGame).GetMethod("StepTurn", NP).Invoke(game, new object[] { 1 });
            if (SteerPrefs.Stop == SteerPrefs.Stops.Length - 1) ok++; else bad++;
            set.Invoke(game, new object[] { SteerPrefs.Middle });
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { false });
        }
        catch (System.Exception e) { Debug.LogError("TURNSHOT exception " + e); bad++; }
        Debug.Log($"TURNSHOT done: {ok} ok, {bad} wrong");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(bad == 0 ? 0 : 1);
    }

    static void Shoot(Camera cam, string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
    }
}
