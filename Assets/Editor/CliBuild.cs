using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CliBuild
{
    static Texture2D MakeIcon()
    {
        const int N = 1024;
        var t = new Texture2D(N, N, TextureFormat.RGB24, false);
        Color top = new Color(0.10f, 0.20f, 0.45f), bot = new Color(0.62f, 0.80f, 0.95f);
        Color snow = new Color(0.97f, 0.98f, 1f), shade = new Color(0.72f, 0.80f, 0.92f);
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float v = y / (float)N;
            Color c = Color.Lerp(bot, top, v);
            // sun
            float dx = x - 740, dy = y - 760;
            if (dx * dx + dy * dy < 90 * 90) c = new Color(1f, 0.92f, 0.65f);
            // main peak: apex (480,760), base y=200 spanning 120..840
            float peak = 760 - Mathf.Abs(x - 480) * 1.0f * (560f / 360f) * 0.64f * 1.0f;
            float ridge = 760 - Mathf.Abs(x - 480) * 1.04f;
            if (y < ridge && y > 150) c = (x < 480) ? snow : shade;
            // dark base slope
            if (y <= 330) c = Color.Lerp(snow, shade, 0.5f);
            // small side peak
            float ridge2 = 520 - Mathf.Abs(x - 800) * 1.1f;
            if (y < ridge2 && y > 150 && x > 560) c = shade;
            t.SetPixel(x, y, c);
        }
        t.Apply();
        return t;
    }

    public static void BuildIOS()
    {
        SummitImport.EnsureImprintMaterial();
        SummitImport.EnsureSnowTextures();
        PlayerSettings.productName = "Summit";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.paulartuso.summit");
        var icon = MakeIcon();
        Directory.CreateDirectory("Assets/Summit/Icon");
        File.WriteAllBytes("Assets/Summit/Icon/AppIcon.png", icon.EncodeToPNG());
        AssetDatabase.ImportAsset("Assets/Summit/Icon/AppIcon.png");
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Summit/Icon/AppIcon.png");
        var target = NamedBuildTarget.iOS;
        var sizes = PlayerSettings.GetIcons(target, IconKind.Application);
        var arr = new Texture2D[sizes.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = tex;
        PlayerSettings.SetIcons(target, arr, IconKind.Application);
        AssetDatabase.SaveAssets();

        if (Directory.Exists("iOSBuild")) Directory.Delete("iOSBuild", true);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Summit/Summit.unity" },
            locationPathName = "iOSBuild",
            target = BuildTarget.iOS,
            options = BuildOptions.None
        });
        Debug.Log("CLIBUILD result: " + report.summary.result + " icons=" + sizes.Length);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
