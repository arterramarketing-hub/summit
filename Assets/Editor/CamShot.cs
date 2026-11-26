using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The riding camera at speed, at every angle and lens: a straight run and a hard carve, by day and by night.
/// Every variant is drawn from the same moment of the same ride (the camera snapped to where it settles for that
/// setting), so two versions of the game can be compared frame for frame. It also logs how high the camera sits
/// over the snow under it, and whether it is riding its "never inside the hill" floor.
/// Batch: -executeMethod CamShot.Run   (CAMSHOT_TAG=before|after names the folder: CamShots/&lt;tag&gt;/)
/// </summary>
public static class CamShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static Camera cam;
    static string dir;

    public static void Run()
    {
        TestPrefs.Pin();
        try { Shoot(); } finally { TestPrefs.Restore(); }
        TestHarness.Exit(0);
    }

    static void Shoot()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        Random.InitState(7);
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        void Set(string f, object v) => typeof(SummitGame).GetField(f, NP).SetValue(game, v);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props");
        var dayNight = Get<DayNight>("dayNight"); var lensFx = Get<LensFX>("lensFx");
        var lens = Get<UnityEngine.Rendering.Universal.LensDistortion>("lens");
        cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        var applyCam = typeof(SummitGame).GetMethod("ApplyCamera", NP);
        var edgeField = typeof(LensFX).GetField("edge", NP);
        dir = "CamShots/" + (System.Environment.GetEnvironmentVariable("CAMSHOT_TAG") ?? "now");
        Directory.CreateDirectory(dir);

        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        TestBoot.NewMountain(game, 1154, terrain, props);
        ChaseCamera.TestDt = Dt;
        Set("pov", false); chase.pov = false;

        // one ride: straight down the fall line at speed, then a hard carve
        float z0 = 420f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z0), z0)).normalized * 34f;
        terrain.Prewarm(); props.Prewarm();
        chase.Snap();
        bool warmed = false;
        // a hard carve between 2.0 and 2.5 s, otherwise held on the middle of the run
        (string name, float at)[] moments = { ("straight", 2.0f), ("carve", 2.45f), ("straight2", 5.0f) };
        int m = 0, n = 0;
        for (float t = 0f; m < moments.Length; t += Dt, n++)
        {
            float steer = t >= 2.0f && t < 2.5f ? 1f : Mathf.Clamp((Slope.Centre(rider.Z + 15f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.6f, -1f, 1f);
            rider.Steer = steer;
            rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 2 == 0) { body.Apply(2 * Dt); late.Invoke(chase, null); aLate.Invoke(atmo, null); }
            if (t < moments[m].at) continue;
            if (!warmed) { for (int i = 0; i < 3; i++) Render("warmup"); warmed = true; }
            string mn = moments[m].name;
            Debug.Log($"CAMSHOT {mn}: {rider.Speed * 3.6f:0} km/h, lean {rider.Lean:0.00}, crashed={rider.Crashed}, air={rider.Airborne}");

            void Variant(string label, int angle, int lensLv)
            {
                Set("angleLevel", angle); Set("lensLevel", lensLv);
                applyCam.Invoke(game, null);
                chase.Snap(); late.Invoke(chase, null); aLate.Invoke(atmo, null);
                Vector3 c = cam.transform.position;
                float over = c.y - Slope.Height(c.x, c.z);
                Debug.Log($"CAMSHOT {mn} {label}: camera {over:0.00} m over the snow under it{(over < 1.32f ? " (ON THE FLOOR)" : "")}, " +
                          $"{c.y - rider.transform.position.y:0.00} m over the rider, {Vector3.Distance(new Vector3(c.x, 0, c.z), new Vector3(rider.transform.position.x, 0, rider.transform.position.z)):0.0} m back, " +
                          $"fov {cam.fieldOfView:0}, pitch {cam.transform.eulerAngles.x:0.0}, roll {Mathf.DeltaAngle(0f, cam.transform.eulerAngles.z):0.0}");
                Render($"{mn}_{label}");
            }

            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            // every angle on the default lens (WIDE)
            string[] an = { "ground", "low", "normal", "high", "overhead" };
            for (int a = 0; a < 5; a++) Variant($"day_angle{a}-{an[a]}_wide", a, 1);
            // every lens on the default angle (LOW)
            string[] ln = { "standard", "wide", "wider", "fisheye" };
            for (int l = 0; l < 4; l++) Variant($"day_angle1-low_lens{l}-{ln[l]}", 1, l);
            Variant("day_angle0-ground_fisheye", 0, 3);
            // the default view with the parts of the lens taken away one at a time: what bends the bottom?
            Set("angleLevel", 1); Set("lensLevel", 1); applyCam.Invoke(game, null);
            chase.Snap(); late.Invoke(chase, null);
            float li = lens.intensity.value, ls = lens.scale.value;
            var barrelField = typeof(LensFX).GetField("barrel", NP); var b0 = (float)barrelField.GetValue(lensFx);
            lens.intensity.Override(0f); lens.scale.Override(1f); barrelField.SetValue(lensFx, 0f);
            Render($"{mn}_diag_low-wide_no-barrel");
            lens.intensity.Override(li); lens.scale.Override(ls); barrelField.SetValue(lensFx, b0);
            var e = (Vector2)edgeField.GetValue(lensFx); edgeField.SetValue(lensFx, Vector2.zero); Render($"{mn}_diag_low-wide_no-fringe");
            edgeField.SetValue(lensFx, e);
            // first person (the chase view still runs underneath it)
            Set("pov", true); chase.pov = true; applyCam.Invoke(game, null);
            chase.Snap(); late.Invoke(chase, null);
            Debug.Log($"CAMSHOT {mn} day_pov: fov {cam.fieldOfView:0}, {cam.transform.position.y - Slope.Height(cam.transform.position.x, cam.transform.position.z):0.00} m over the snow");
            Render($"{mn}_day_pov_low-wide");
            Set("pov", false); chase.pov = false; applyCam.Invoke(game, null); chase.Snap(); late.Invoke(chase, null);
            // night
            TimeOfDay.TestClock = 3.4f; dayNight.ApplyNow();
            Variant("night_angle1-low_wide", 1, 1);
            Variant("night_angle0-ground_fisheye", 0, 3);
            Variant("night_angle2-normal_wide", 2, 1);
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            // back to the default for the rest of the ride
            Set("angleLevel", 1); Set("lensLevel", 1); applyCam.Invoke(game, null);
            m++;
        }
        ChaseCamera.TestDt = 0f;
        TimeOfDay.TestClock = -1f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
    }

    static void Render(string name)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new System.Collections.Generic.List<GameObject>();
        foreach (var sk in skins) { if (!sk.enabled) continue; var mm = new Mesh(); sk.BakeMesh(mm); var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.GetComponent<MeshFilter>().sharedMesh = mm; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; baked.Add(g); }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes($"{dir}/{name}.png", tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
