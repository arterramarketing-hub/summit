using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The realistic forest down the whole mountain: near the summit (trees buried to their tops), half way, at the
/// bottom (whole tall trees on their trunks), and a dense stand seen from beside the piste, as in a photo.
/// Run: TREEVAR=Top:Chase Unity -batchmode -projectPath . -executeMethod TreeVarietyShot.Run -logFile v.log
/// (no -nographics). TREEVAR is where (Top, Upper, Mid, Low, Bottom) and how (Chase: the game's camera; Side:
/// standing at the piste's edge looking into the trees; Up: across from a stand, looking up at it). One shot a run
/// (batchmode never flushes queued instanced draws). Writes TreeVar_{where}_{how}.png and a TREES line.
/// </summary>
public static class TreeVarietyShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        PlayerPrefs.SetInt(Forest.TreesPref, 1);
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props"); var fx = Get<SnowFX>("fx");
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        rider.input = null;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear();
        Slope.Init(424242);

        var want = (System.Environment.GetEnvironmentVariable("TREEVAR") ?? "Mid:Chase").Split(':');
        float F = Slope.FinishZ;
        float z = want[0] switch { "Top" => 160f, "Upper" => F * 0.25f, "Mid" => F * 0.5f, "Low" => F * 0.75f, _ => F - 260f };
        rider.ResetAt(Slope.Centre(z), z);
        body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.forward * 14f;
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        for (int i = 0; i < 30; i++) { rider.Tick(1f / 60f); body.PhysicsStep(); Physics.Simulate(1f / 60f); }
        body.Apply(1f / 60f);
        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
        cam.fieldOfView = 60f;
        chase.enabled = true; chase.Snap(); late.Invoke(chase, null);
        if (want[1] != "Chase")
        {
            rider.gameObject.SetActive(false);
            float rz = rider.transform.position.z;
            float ex = Slope.Centre(rz) + Slope.PisteHalf - (want[1] == "Up" ? 16f : 10f), ez = rz;
            var eye = new Vector3(ex, Slope.Surface(ex, ez) + 1.7f, ez);
            Vector3 look;
            if (want[1] == "Up")
            {
                // a few metres from the trees, looking up into their crowns against the sky, as in a photo
                float tx = ex + 40f, tz = ez + 14f;
                look = new Vector3(tx, Slope.Surface(tx, tz) + 22f, tz);
                cam.fieldOfView = 62f;
            }
            else
            {
                float tx = ex + 40f, tz = ez + 45f;
                look = new Vector3(tx, Slope.Surface(tx, tz) + 4f, tz);
            }
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        }
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        if (fx != null) fx.Clear();
        props.Stats(out int draws, out long tris, out int sDraws, out long sTris, out int inst);
        string file = $"TreeVar_{want[0]}_{want[1]}.png";
        Shoot(cam, props, file);
        Debug.Log($"TREES {want[0]} {want[1]} z={z:0} alt={Slope.AltitudeFeet(z):0}ft low={Forest.Low(z):0.00} trees+rocks={inst} draws={draws} tris={tris} shadowDraws={sDraws} shadowTris={sTris}");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static readonly MethodInfo draw = typeof(Forest).GetMethod("Draw", NP);

    static void Shoot(Camera cam, Forest props, string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        for (int k = 0; k < 2; k++) { draw.Invoke(props, null); cam.Render(); }
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
    }
}
