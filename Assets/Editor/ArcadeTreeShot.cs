using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (arcadetrees) Before and after pictures of the lines through the trees: on three mountains, the densest stretch of the
/// lower forest that a line runs through on open ground. The rider rides down the line (the game's own physics and chase
/// camera, its tight-trees view and all) and the chase view is saved three times along it, then a view from high above
/// looking down the stretch; first with the lines switched off (Forest.LanesOn: the forest as it was), then on.
/// Phone quality, clear midday. Run: ARCADE_OUT=dir Unity -batchmode -projectPath . -executeMethod ArcadeTreeShot.Run
/// (graphics, no -quit). Writes {before|after}_s{seed}_{chase1..3|above}.png.
/// </summary>
public static class ArcadeTreeShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly int[] Seeds = { 1000, 1154, 7777 };
    const int W = 393, H = 852;
    const float Dt = 1f / 120f, Speed = 16f;
    static string outDir;
    static SummitGame game; static Summit.Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static DayNight dayNight;
    static Camera cam; static ChaseCamera chase; static Farview farview;
    static MethodInfo tUpd, fUpd, cLate, aLate; static Atmosphere atmo;
    static RenderTexture rt; static Texture2D tex;
    static int quality;
    static readonly Queue<System.Action> steps = new Queue<System.Action>();

    public static void Run()
    {
        outDir = System.Environment.GetEnvironmentVariable("ARCADE_OUT") ?? "arcadetrees-shots";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);
        PlayerPrefs.SetInt(Forest.TreesPref, 1);
        Farview.TestOn = true; Farview.TestClock = 0.0;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Summit.Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props"); dayNight = Get<DayNight>("dayNight");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); farview = Farview.Instance; atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        cLate = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        Physics.simulationMode = SimulationMode.Script;
        rt = new RenderTexture(W, H, 24) { antiAliasing = 1 };
        tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        foreach (int seed in Seeds)
        {
            int s = seed;
            steps.Enqueue(() => Mountain(s));
            foreach (bool on in new[] { false, true })
            {
                bool lanes = on;
                string tag = (on ? "after" : "before") + "_s" + seed;
                steps.Enqueue(() => { Forest.LanesOn = lanes; props.Rebuild(); });
                for (int i = 1; i <= 3; i++)
                {
                    int k = i;
                    steps.Enqueue(() => RideTo(k * 45f));
                    steps.Enqueue(() => { Updates(); Shoot(null); });
                    steps.Enqueue(() => { Updates(); Shoot($"{tag}_chase{k}.png"); });
                }
                steps.Enqueue(() => Above());
                steps.Enqueue(() => { Updates(); Shoot(null); });
                steps.Enqueue(() => { Updates(); Shoot($"{tag}_above.png"); });
            }
        }
        EditorApplication.update += Tick;
    }

    static int side, k; static float z0;

    static void Mountain(int seed)
    {
        Forest.LanesOn = true;
        TestBoot.NewMountain(game, seed, terrain, props);
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        // the densest stretch beside a line, on open ground, low on the mountain
        float best = -1f;
        for (float z = 2000f; z < Slope.FinishZ - 320f; z += 50f)
            for (int sd = -1; sd <= 1; sd += 2)
                for (int kk = 0; kk < 2; kk++)
                {
                    if (!Open(sd, kk, z, z + 200f)) continue;
                    float d = Beside(sd, kk, z, z + 200f);
                    if (d > best) { best = d; side = sd; k = kk; z0 = z; }
                }
        Debug.Log($"ARCADE seed {seed}: line {(side < 0 ? "left" : "right")} {(k == 0 ? "inner" : "outer")} from z {z0:0} (forest beside it {best * 100f:0}% of cells)");
    }

    static bool Open(int sd, int kk, float a, float b)
    {
        for (float z = a - 20f; z <= b; z += 3f)
        {
            float x = Forest.LaneX(sd, kk, z);
            if (Slope.Normal(x, z).y < 0.6f || Deadly.Below(x, z) || Course.CrevasseNear(x, z, 3f) || LiftLine.Taken(x, z, 4f) || Course.Keep(x, z, 2f) || Hamlets.Taken(x, z)) return false;
        }
        return true;
    }

    static float Beside(int sd, int kk, float a, float b)
    {
        int cells = 0, trees = 0;
        for (float z = a; z < b; z += Forest.Cell)
            foreach (float s in new[] { -1f, 1f })
                for (float o = Forest.LaneEdge; o < Forest.LaneEdge + 15f; o += Forest.Cell)
                {
                    float x = Forest.LaneX(sd, kk, z) + s * o / Slope.Squash(z);
                    cells++;
                    if (Forest.At(Mathf.FloorToInt(x / Forest.Cell), Mathf.FloorToInt(z / Forest.Cell), out _, out _, out _) == Forest.Kind.Tree) trees++;
                }
        return cells > 0 ? trees / (float)cells : 0f;
    }

    /// <summary>Ride down the line from 30 m above `at` (metres into the stretch) to it, the chase camera following.</summary>
    static void RideTo(float at)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float zs = z0 + at - 30f, xs = Forest.LaneX(side, k, zs);
        rider.ResetAt(xs, zs); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(xs, zs)).normalized * Speed;
        if (chase != null) chase.Snap();
        ChaseCamera.TestDt = 4f * Dt;
        int n = 0;
        while (rider.Body.position.z < z0 + at && n < 1200 && !rider.Crashed)
        {
            var p = rider.Body.position;
            float want = Mathf.Atan2(Forest.LaneX(side, k, p.z + 12f) - p.x, 12f);
            rider.Steer = Mathf.Clamp((want - rider.Heading) * 2.2f, -1f, 1f);
            var v = rider.Body.linearVelocity; if (v.magnitude > Speed) rider.Body.linearVelocity = v * (Speed / v.magnitude);
            rider.TestContacts(Dt);
            rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 2 == 1) body.Apply(2f * Dt);
            if (n % 4 == 3 && chase != null) cLate.Invoke(chase, null);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            n++;
        }
    }

    /// <summary>From high above and behind, looking down the stretch.</summary>
    static void Above()
    {
        float zc = z0 + 100f, xc = Forest.LaneX(side, k, zc);
        var look = new Vector3(xc, Slope.Height(xc, zc), zc);
        float ze = z0 - 10f, xe = Forest.LaneX(side, k, ze);
        var eye = new Vector3(xe, Slope.Height(xe, ze) + 70f, ze);
        rider.ResetAt(Forest.LaneX(side, k, z0 + 60f), z0 + 60f); body.ResetVisual();
        terrain.Prewarm(); props.Prewarm();
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        cam.fieldOfView = 60f;
    }

    static void Updates()
    {
        tUpd?.Invoke(terrain, null);
        fUpd?.Invoke(props, null);
        if (farview != null) { farview.Apply(); farview.Step(); if (farview.far != null) farview.far.Step(false); }
    }

    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        if (atmo != null) aLate.Invoke(atmo, null);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        if (file != null) { File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG()); Debug.Log("ARCADE shot " + file); }
        cam.targetTexture = null; RenderTexture.active = null;
    }

    static void Tick()
    {
        try
        {
            if (steps.Count > 0) { steps.Dequeue()(); return; }
        }
        catch (System.Exception e) { Debug.LogError("ARCADE exception " + e); }
        if (steps.Count > 0) return;
        EditorApplication.update -= Tick;
        Forest.LanesOn = true;
        ChaseCamera.TestDt = 0f; Farview.TestOn = false;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        QualitySettings.SetQualityLevel(quality, false);
        TestPrefs.Restore();
        Debug.Log("ARCADE done");
        TestHarness.Exit(0);
    }
}
