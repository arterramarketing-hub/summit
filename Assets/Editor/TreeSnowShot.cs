using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// A felled realistic tree lying on the snow with its snow on (left) and shaken off (right: FirCards' _Snow),
/// each drawn once, so nothing from another frame shows through (batchmode never flushes queued draws).
/// Run: Unity -batchmode -projectPath . -executeMethod TreeSnowShot.Run -logFile snow.log (no -nographics).
/// Writes TreeSnowShot_{kind}.png for a fir and a pine.
/// </summary>
public static class TreeSnowShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        PlayerPrefs.SetInt(Forest.TreesPref, 1);
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        RealFir.Load();
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        string kind = System.Environment.GetEnvironmentVariable("TREESNOW") ?? "0";
        int v = int.Parse(kind);
        float z = 300f, x = Slope.Centre(z);
        var mesh = RealFir.Meshes[v];
        float h = RealFir.Heights[v], k = 9f / h;
        // two lying side by side across the slope, crowns down the hill
        for (int i = 0; i < 2; i++)
        {
            float px = x + (i == 0 ? -4f : 4f);
            var foot = new Vector3(px, Slope.Surface(px, z) + 0.6f, z);
            var m = Matrix4x4.TRS(foot, Quaternion.Euler(80f, 0f, 0f), Vector3.one * k);
            var props = new MaterialPropertyBlock();
            props.SetFloat("_Sway", 0f); props.SetFloat("_Snow", i == 0 ? 1f : 0.12f);
            Graphics.RenderMesh(new RenderParams(RealFir.StandaloneOf(v)) { matProps = props, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On }, mesh, 0, m);
        }
        var eye = new Vector3(x, Slope.Surface(x, z - 9f) + 6f, z - 9f);
        cam.fieldOfView = 55f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(x, Slope.Surface(x, z + 4f), z + 4f) - eye));
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        int w = 720, hh = 540;
        var rt = new RenderTexture(w, hh, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)hh;
        cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, hh, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, hh), 0, 0); tex.Apply();
        File.WriteAllBytes($"TreeSnowShot_{v}.png", tex.EncodeToPNG());
        Debug.Log($"TREESNOW wrote TreeSnowShot_{v}.png (variant {v}, left snow on, right shaken off)");
        EditorApplication.Exit(0);
    }
}
