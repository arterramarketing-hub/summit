using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders a frame of the game mid-run to a PNG without a device, for checking visuals.
/// Run: Unity -batchmode -projectPath . -executeMethod RenderShot.Run -logFile shot.log
/// (no -nographics: it needs the GPU). Writes RenderShot.png in the project folder.
/// </summary>
public static class RenderShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var terrain = (TerrainStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (PropField)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var input = rider.input;
        var steerProp = typeof(RiderInput).GetProperty("Steer");
        var step = typeof(Rider).GetMethod("Step", NP);
        var vis = typeof(Rider).GetMethod("UpdateVisual", NP);

        rider.Begin();
        float secs = 9f;
        for (int i = 0; i < 120 * secs; i++)
        {
            float t = i / 120f;
            steerProp.SetValue(input, Mathf.Clamp(Mathf.Sin(t * 1.3f) * 0.5f + (MountainField.DriftX(rider.Z) - rider.X) * 0.02f - rider.Heading * 0.5f, -1f, 1f));
            step.Invoke(rider, new object[] { 1f / 120f, false });
            if (i % 4 == 0) vis.Invoke(rider, new object[] { 1f / 30f });
        }
        terrain.Prewarm(); props.Prewarm();

        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        for (int i = 0; i < 3; i++) late.Invoke(chase, null);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        typeof(Atmosphere).GetField("speed01", NP).SetValue(atmo, 1f);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        typeof(Atmosphere).GetField("speed01", NP).SetValue(atmo, 1f);
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            var em = ps.emission;
            if (ps.name == "SpeedLines") em.rateOverTime = 110f;
            ps.Simulate(ps.name == "SpeedLines" ? 0.5f : 4f, true, true);
        }

        DynamicGI.UpdateEnvironment();   // batch mode skips the sky's ambient light otherwise
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes("RenderShot.png", tex.EncodeToPNG());
        Debug.Log($"SHOT written speed={rider.Speed * 3.6f:0} km/h");
        EditorApplication.Exit(0);
    }
}
