using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Before/after for the two tree looks: renders the same views with the original low-poly firs and with the
/// realistic card firs, and logs what the forest asks of the GPU in each (instanced draw calls, triangles).
/// Run: TREESHOT=Real:Close Unity -batchmode -projectPath . -executeMethod TreeShot.Run -logFile trees.log
/// (no -nographics: it needs the GPU). TREESHOT picks the look (Classic or Real) and the view (Start, Run, Edge,
/// Close); one shot per run, because in batchmode the instanced draws queued for one render are never flushed
/// and would show up in every later shot. Writes TreeShot_{look}_{view}.png and a TREES line to the log.
/// </summary>
public static class TreeShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures();
        SummitImport.EnsureSky();
        SummitImport.EnsureBackdrop();
        SummitImport.EnsureRealTrees();
        var game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        var rider = game.rider;
        var body = game.body;
        var terrain = game.terrain;
        var props = game.props;
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var fx = Object.FindFirstObjectByType<SnowFX>();
        rider.input = null;
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear();
        // the game starts on a random mountain; use the same one for every shot so the two looks compare fairly
        Slope.Init(424242);
        rider.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ);
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        Random.InitState(7);

        // (name, rider x offset from the piste centre, z, camera: 0 = the game's chase camera, 1 = beside the trees)
        var views = new (string name, float dx, float z, int camMode)[] {
            ("Start", 0f, Slope.StartZ, 0),
            ("Run", 0f, Course.First + 110f, 0),
            ("Edge", Slope.PisteHalf - 4f, Course.First + 260f, 0),
            ("Close", Slope.PisteHalf + 2f, Course.First + 400f, 1),
            ("Pause", 0f, Course.First + 110f, 2),   // the pause menu, with its TREES switch
        };
        var want = (System.Environment.GetEnvironmentVariable("TREESHOT") ?? "Real:Close").Split(':');
        {
            bool realistic = want[0] == "Real";
            props.SetRealistic(realistic);
            string look = realistic ? "Real" : "Classic";
            foreach (var v in views)
            {
                if (v.name != want[1]) continue;
                rider.ResetAt(Slope.Centre(v.z) + v.dx, v.z);
                body.ResetVisual();
                rider.Begin();
                rider.Body.linearVelocity = Vector3.forward * 14f;
                for (int i = 0; i < 30; i++) { rider.Tick(1f / 60f); body.PhysicsStep(); Physics.Simulate(1f / 60f); }
                body.Apply(1f / 60f);
                terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
                cam.fieldOfView = 60f;
                chase.enabled = true; chase.Snap(); late.Invoke(chase, null);
                if (v.camMode == 1)
                {
                    // standing on the piste near its edge, looking across and down into the trees
                    float ex = Slope.Centre(v.z) + Slope.PisteHalf - 14f, ez = v.z;
                    var eye = new Vector3(ex, Slope.Surface(ex, ez) + 1.8f, ez);
                    float tx = ex + 22f, tz = ez + 30f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(tx, Slope.Surface(tx, tz) + 3f, tz) - eye));
                }
                typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
                if (fx != null) fx.Clear();
                props.Stats(out int draws, out long tris, out int sDraws, out long sTris, out int inst);
                Canvas canvas = null;
                if (v.camMode == 2)
                {
                    typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { true });
                    // the HUD draws straight to the screen, which a render to a texture misses: draw it with the camera
                    canvas = (game.hud).GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
                }
                Shoot(cam, props, $"TreeShot_{look}_{v.name}.png");
                if (canvas != null) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Debug.Log($"TREES {look} {v.name} trees+rocks={inst} draws={draws} tris={tris} shadowDraws={sDraws} shadowTris={sTris}");
            }
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    static readonly MethodInfo draw = typeof(Forest).GetMethod("Draw", NP);

    /// <summary>Renders the frame to a PNG. (Not timed: in batchmode the instanced draws queued for one manual
    /// render pile up into the next, so timings here mean nothing - frame time is measured on the device.)</summary>
    static void Shoot(Camera cam, Forest props, string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        // twice: the first render after a change comes out unlit in batchmode
        for (int k = 0; k < 2; k++) { draw.Invoke(props, null); cam.Render(); }
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());

        RenderTexture.active = null;
        cam.targetTexture = null;
    }
}
