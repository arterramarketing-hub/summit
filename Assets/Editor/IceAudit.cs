using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The icy stretches' look, for an audit: one mountain's stretch (ICEA_SEED, ICEA_PICK) from five views, each by clear
/// day, clear night and in a storm: the chase camera riding up to it and carving on it, close over the ice (the shine,
/// the normals, the streaks), from above its top edge (the blend into the snow, the patch shapes, the corduroy), and from
/// far up the hill (the shading at a distance, tiling). ICEA_OUT=dir, ICEA_TAG=a name for the files (before, after).
/// Batch: -executeMethod IceAudit.Shots (no -nographics: it renders).
/// </summary>
public static class IceAudit
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1280;
    const float Dt = 1f / 120f;
    static SummitGame game; static Camera cam; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase;
    static MethodInfo camStep, aLate;
    static RenderTexture rt; static Texture2D tex;

    static readonly (int choice, float clock, string name)[] Looks = { (1, 1.4f, "day"), (1, 3.4f, "night"), (4, 1.4f, "storm") };

    public static void Shots()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        string outDir = System.Environment.GetEnvironmentVariable("ICEA_OUT") ?? "ice-audit";
        string tagName = System.Environment.GetEnvironmentVariable("ICEA_TAG") ?? "shot";
        System.IO.Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        r = game.rider; body = game.body; terrain = game.terrain; props = game.props;
        dayNight = game.dayNight; weather = Get<Weather>("weather");
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        Physics.simulationMode = SimulationMode.Script;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("ICEA_SEED"), out int ss) ? ss : 1000;
        TestBoot.NewMountain(game, seed, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        if (chase == null) chase = Object.FindFirstObjectByType<ChaseCamera>();
        chase.enabled = false;
        rt = new RenderTexture(W, H, 24); tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        var secs = Surfaces.IceSections;
        if (secs.Length == 0) { Debug.Log($"ICEA seed{seed}: no icy stretch"); Leave(1); return; }
        var s = secs[Mathf.Clamp(int.TryParse(System.Environment.GetEnvironmentVariable("ICEA_PICK"), out int pk) ? pk : 0, 0, secs.Length - 1)];
        string tag = $"{outDir}/{tagName}_s{seed}_z{s.z0:0}";
        Debug.Log($"ICEA seed{seed} stretch z {s.z0:0}-{s.z1:0} steep {s.steep:0.00} wind {s.wind:0.00} shade {s.shade:0.00}");

        // ride in from 45 m above, down the middle, then carving across it; the chase camera's two views are kept
        float z0 = s.z0 - 45f, v0 = 16f;
        Show(1, 1.4f);
        r.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); r.Begin();
        r.Heading = 0f; r.Steer = 0f;
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z0), z0)).normalized * v0;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        chase.rider = r; chase.Snap();
        var views = new List<(string name, Vector3 p, Quaternion q, Vector3 focus)>();
        bool coming = false, carve = false;
        for (int n = 0; n < 120 * 14 && r.Z < s.z1; n++)
        {
            bool carving = r.Z > s.z0 + 4f;
            r.Steer = r.Crashed ? 0f : carving ? 0.5f * Mathf.Sin((r.Z - s.z0) / 9f) + Mathf.Clamp((Slope.Centre(r.Z + 10f) - r.X) * 0.05f, -0.3f, 0.3f)
                                               : Mathf.Clamp((Slope.Centre(r.Z + 10f) - r.X) * 0.08f, -0.4f, 0.4f);
            if (r.Speed > v0 && !r.Airborne && !r.Crashed) r.Body.linearVelocity *= v0 / r.Speed;
            TestHarness.Step(r, body, Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 2 == 0)
            {
                body.Apply(2 * Dt);
                ChaseCamera.TestDt = 2 * Dt;
                camStep.Invoke(chase, null);
                Surfaces.Step(r.Body.position, r.RunNow, 2 * Dt, true);
            }
            if (!coming && r.Z > s.z0 - 22f) { views.Add(("coming", cam.transform.position, cam.transform.rotation, r.Body.position)); coming = true; }
            if (!carve && r.Z > s.z0 + 26f && r.Underfoot.ice > 0.6f && Mathf.Abs(r.Lean) > 0.3f * Rider.MaxLean)
            {
                views.Add(("carve", cam.transform.position, cam.transform.rotation, r.Body.position)); carve = true;
                break;
            }
            if (r.Crashed) break;
        }
        Debug.Log($"ICEA ride: {(coming ? "coming" : "no coming")}, {(carve ? "carve" : "no carve")}, {(r.Crashed ? "CRASHED" : $"{r.Speed * 3.6f:0} km/h")}");

        // close over the ice: 1.7 m up, looking down the fall line (the shine and the normals)
        float zm = Mathf.Lerp(s.z0, s.z1, 0.45f);
        {
            Vector3 at = Ground(Slope.Centre(zm) + 2f, zm + 9f);
            Vector3 from = Ground(Slope.Centre(zm) - 1f, zm); from.y += 1.7f;
            views.Add(("close", from, Quaternion.LookRotation(at - from), at));
        }
        // over the top edge, 14 m up, looking down on it (the blend into the snow, the patches, the corduroy)
        {
            float ze = s.z0 - Surfaces.IceFade * 0.3f;
            Vector3 at = Ground(Slope.Centre(ze), ze + 6f);
            Vector3 from = Ground(Slope.Centre(ze) - 3f, ze - 8f); from.y += 14f;
            views.Add(("edge", from, Quaternion.LookRotation(at - from), at));
        }
        // close again, looking up the hill into the day's light (the shine: plastic or ice)
        {
            Vector3 at = Ground(Slope.Centre(zm) - 2f, zm - 10f);
            Vector3 from = Ground(Slope.Centre(zm), zm); from.y += 1.7f;
            views.Add(("shine", from, Quaternion.LookRotation(at - from), at));
        }
        // from well up the hill and high (over any roll between), the whole stretch below: the shading at a
        // distance, tiling, the patches' shapes
        {
            float zf = s.z0 - 40f, zc = Mathf.Lerp(s.z0, s.z1, 0.4f);
            Vector3 at = Ground(Slope.Centre(zc), zc);
            Vector3 from = Ground(Slope.Centre(zf) - 6f, zf); from.y = Mathf.Max(from.y, at.y) + 38f;
            views.Add(("far", from, Quaternion.LookRotation(at - from), at));
        }

        foreach (var (choice, clock, look) in Looks)
        {
            Show(choice, clock);
            foreach (var v in views)
            {
                Surfaces.Step(v.focus, r.RunNow, 0f, true);
                terrain.Prewarm(); props.Prewarm();
                cam.transform.SetPositionAndRotation(v.p, v.q);
                Air(0.5f, v.p, v.q);
                Grab($"{tag}_{v.name}_{look}.png");
            }
        }
        var sb = new System.Text.StringBuilder();
        for (float dz = -20f; dz <= 20f; dz += 4f) sb.Append($" {dz:+0;-0}:{Surfaces.Surface(Slope.Centre(s.z0 + dz), s.z0 + dz).ice:0.00}");
        Debug.Log($"ICEA ice down the middle, metres from the stretch's start:{sb}");
        Leave(0);
    }

    static Vector3 Ground(float x, float z) => new Vector3(x, Slope.Height(x, z), z);

    static void Show(int choice, float clock)
    {
        Weather.TestChoice = choice; TimeOfDay.TestClock = clock;
        weather.Step(0f); dayNight.ApplyNow();
        foreach (var ps in Systems()) ps.Clear();
        if (choice == 1) Surfaces.Day = default;   // (a calm, cold, clear day: no fresh snow over the ice)
    }

    /// <summary>Let the air settle over a held camera: weather, atmosphere, light, particles.</summary>
    static void Air(float seconds, Vector3 p, Quaternion q)
    {
        for (float t = 0f; t < seconds; t += 1f / 30f)
        {
            cam.transform.SetPositionAndRotation(p, q);
            TreeWind.Advance(1f / 30f, p);
            weather.Step(1f / 30f);
            aLate.Invoke(atmo, null);
            dayNight.ApplyNow();
            foreach (var ps in Systems()) ps.Simulate(1f / 30f, false, false, false);
        }
        cam.transform.SetPositionAndRotation(p, q);
    }

    static IEnumerable<ParticleSystem> Systems()
    {
        if (atmo != null) foreach (var ps in atmo.GetComponentsInChildren<ParticleSystem>()) yield return ps;
        foreach (var ps in weather.GetComponentsInChildren<ParticleSystem>(true)) yield return ps;
        foreach (var ps in cam.GetComponentsInChildren<ParticleSystem>()) yield return ps;
    }

    static void Grab(string file)
    {
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Debug.Log("ICEA-SHOT " + file);
    }

    static void Leave(int code)
    {
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        if (weather != null) { weather.Step(0f); dayNight.ApplyNow(); Object.DestroyImmediate(weather.gameObject); }
        TimeOfDay.TestClock = -1f; Weather.TestChoice = -1;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(code);
    }
}
