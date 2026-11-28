using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// How the crevasses read from a distance, on two seeds, day and night: the game's own chase camera riding at the
/// first field from 45 m and 25 m up the hill, and the field from above at close range (an 80 m frame). Uses only
/// Course.Crevasses' layout, so it shows any look of them (before and after a change).
/// Run: Unity -batchmode -projectPath . -executeMethod GlacierLook.Run -logFile x.log (no -nographics).
/// $LOOK_TAG names the shots (GlacierLook_{tag}_s{seed}_{day|night}_{view}.png, in the project folder).
/// </summary>
public static class GlacierLook
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly int[] Seeds = { 2000, 2131 };
    const float Dt = 1f / 60f;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        var game = TestHarness.Boot();
        var rider = game.rider;
        var body = game.body;
        var terrain = game.terrain;
        var props = game.props;
        var dayNight = typeof(SummitGame).GetField("dayNight", NP)?.GetValue(game) as DayNight;
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        string tag = System.Environment.GetEnvironmentVariable("LOOK_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "now";
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        try
        {
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            foreach (int seed in Seeds)
            {
                TestBoot.NewMountain(game, seed, terrain, props);
                var dress = Object.FindFirstObjectByType<CourseDressing>();
                // the first field of transverse cracks, and its top
                int field = -1; float top = float.MaxValue, bottom = float.MinValue;
                foreach (var c in Course.Crevasses) if (c.field >= 0) { field = c.field; break; }
                foreach (var c in Course.Crevasses) if (c.field == field) { top = Mathf.Min(top, c.z0); bottom = Mathf.Max(bottom, c.z1); }
                if (field < 0) { Debug.Log($"LOOK seed{seed}: no field"); continue; }
                float x = Slope.Centre(top);
                rider.ResetAt(x, top - 50f);
                if (dress != null) dress.Rebuild();
                foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
                {
                    TimeOfDay.TestClock = clock; dayNight?.ApplyNow();
                    foreach (float back in new[] { 45f, 25f })
                    {
                        rider.ResetAt(x, top - back - 8f); body.ResetVisual(); rider.Begin();
                        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, top - back)).normalized * 15f;
                        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
                        chase.enabled = true; chase.Snap();
                        for (int i = 0; i < 30; i++)
                        {
                            rider.Steer = Mathf.Clamp((x - rider.transform.position.x) * 0.15f - rider.Heading * 1.2f, -1f, 1f);
                            for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
                            typeof(SlopeStreamer).GetMethod("Update", NP)?.Invoke(terrain, null);
                            typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
                            body.Apply(Dt);
                            late.Invoke(chase, null);
                        }
                        Shoot(cam, atmo, $"GlacierLook_{tag}_s{seed}_{when}_warm.png");
                        Shoot(cam, atmo, $"GlacierLook_{tag}_s{seed}_{when}_approach{back:0}.png");
                    }
                    // the field from above, close: an 80 m frame
                    float zm = (top + bottom) * 0.5f, cx = Slope.Centre(zm);
                    rider.ResetAt(cx, zm); body.ResetVisual(); rider.Stop();
                    terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
                    typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
                    chase.enabled = false;
                    cam.orthographic = true; cam.orthographicSize = 40f; cam.nearClipPlane = 1f; cam.farClipPlane = 800f;
                    cam.transform.SetPositionAndRotation(new Vector3(cx, Slope.Height(cx, zm - 40f) + 80f, zm), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    Shoot(cam, atmo, $"GlacierLook_{tag}_s{seed}_{when}_plan.png");
                    if (System.Environment.GetEnvironmentVariable("LOOK_DEBUG") == "1" && when == "day")
                        foreach (var group in new[] { "CrevasseIce", "CrevasseSnow", "CrevasseLip", "Chunk", "SnowLayer" })
                        {
                            var off = new System.Collections.Generic.List<Renderer>();
                            foreach (var rr in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (rr.enabled && rr.name.StartsWith(group)) { rr.enabled = false; off.Add(rr); }
                            Shoot(cam, atmo, $"GlacierLook_{tag}_s{seed}_{when}_plan_no{group}.png");
                            foreach (var rr in off) rr.enabled = true;
                        }
                    cam.orthographic = false;
                }
                Debug.Log($"LOOK seed{seed}: field {field} z {top:0}..{bottom:0}");
            }
        }
        catch (System.Exception e) { Debug.LogError("LOOK exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    static void Shoot(Camera cam, Atmosphere atmo, string file)
    {
        DynamicGI.UpdateEnvironment();
        if (atmo != null) typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        // batchmode renders do not re-skin meshes: bake them to see the rider's real pose
        var baked = new System.Collections.Generic.List<GameObject>();
        var hidden = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; hidden.Add(sk); baked.Add(g);
        }
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }
}
