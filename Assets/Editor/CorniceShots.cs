using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The cornices' look, before and after (CORNICE_OFF=1 for before): one mountain's first jump cliff and first ledge line
/// that the cornice rule picks (whether cornices are on or not), each from the chase camera riding up to the lip, leaving
/// it and landing, and from fixed views: beside the lip (its profile), and from the landing looking back up under it.
/// CORN_SEED=seed, CORN_OUT=dir, CORN_TAG=a name for the files, CORN_LOOKS=day (or all: day, night, storm).
/// Batch: -executeMethod CorniceShots.Shots (no -nographics: it renders).
/// </summary>
public static class CorniceShots
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1280;
    const float Dt = 1f / 120f;
    static SummitGame game; static Camera cam; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase;
    static MethodInfo camStep, aLate;
    static RenderTexture rt; static Texture2D tex;
    static string prefix;   // where the chase camera's shots go (taken as the ride passes: the rider in them, the spray as it is)
    static ParticleSystem[] bodyFx;

    public static void Shots()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        string outDir = System.Environment.GetEnvironmentVariable("CORN_OUT") ?? "cornice-shots";
        string tagName = System.Environment.GetEnvironmentVariable("CORN_TAG") ?? "shot";
        var looks = System.Environment.GetEnvironmentVariable("CORN_LOOKS") == "all"
            ? new[] { (1, 1.4f, "day"), (1, 3.4f, "night"), (4, 1.4f, "storm") } : new[] { (1, 1.4f, "day") };
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
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("CORN_SEED"), out int ss) ? ss : 1000;
        TestBoot.NewMountain(game, seed, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        if (chase == null) chase = Object.FindFirstObjectByType<ChaseCamera>();
        chase.enabled = false;
        rt = new RenderTexture(W, H, 24); tex = new Texture2D(W, H, TextureFormat.RGB24, false);

        prefix = $"{outDir}/{tagName}_s{seed}_";
        bodyFx = body.GetComponentsInChildren<ParticleSystem>(true);
        var views = new List<(string name, Vector3 p, Quaternion q, Vector3 focus)>();
        // the first jump cliff the rule makes a cornice
        bool found = false;
        for (int c = 1; c < 6 && !found; c++)
            for (int k = 0; k < 2 && !found; k++)
                if (Cliffs.JumpOf(c, k, out var j) && Cliffs.CorniceRule(j)) { found = true; JumpViews(j, views, seed); }
        if (!found) Debug.Log($"CORN seed{seed}: no cornice jump cliff in the first cycles");
        // the first ledge line the rule makes cornices
        found = false;
        for (int c = 0; c < 8 && !found; c++)
        {
            bool has = Cliffs.LedgeOf(c, out var L);
            Debug.Log($"CORN seed{seed} cycle{c}: ledge line {(has ? $"{L.count} ledges, cornice {Cliffs.CorniceRule(L)}" : "none")}");
            if (has && Cliffs.CorniceRule(L)) { found = true; LedgeViews(L, views, seed); }
        }
        if (!found) Debug.Log($"CORN seed{seed}: no cornice ledge line in the first cycles");

        // the course's dressing (signs, wands, crags, cornices) built round each view, not round the rider
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var focus = new GameObject("CorniceShotFocus").transform;
        if (dress != null) dress.target = focus;
        terrain.target = focus; props.target = focus;   // (and the ground and the trees streamed round it)
        foreach (var (choice, clock, look) in looks)
        {
            Show(choice, clock);
            foreach (var v in views)
            {
                foreach (var ps in bodyFx) ps.Clear();
                focus.position = v.focus;
                if (dress != null) dress.Prewarm();
                Surfaces.Step(v.focus, r.RunNow, 0f, true);
                terrain.Prewarm(); props.Prewarm();
                cam.transform.SetPositionAndRotation(v.p, v.q);
                Air(0.5f, v.p, v.q);
                Grab($"{outDir}/{tagName}_s{seed}_{v.name}_{look}.png");
            }
        }
        Leave(0);
    }

    /// <summary>The snow down the middle of the seed's first cornice jump cliff, metre by metre (a check: CORN_SEED).</summary>
    public static void Probe()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        var g = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(g);
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("CORN_SEED"), out int ss) ? ss : 1000;
        TestBoot.NewMountain(g, seed, g.terrain, g.props);
        Surfaces.Day = default;
        for (int c = 1; c < 6; c++)
            for (int k = 0; k < 2; k++)
                if (Cliffs.JumpOf(c, k, out var j) && Cliffs.CorniceRule(j))
                {
                    float lip = Cliffs.LipZ(j, 0f);
                    for (float u = -40f; u <= 14f; u += 4f)
                    {
                        float x = Slope.Centre(lip + u), z = lip + u;
                        var m = Surfaces.Surface(x, z);
                        Debug.Log($"PROBE c{c}k{k} u{u:0} pow {m.powder:0.00} ice {m.ice:0.00} crud {m.crud:0.00} crust {m.crust:0.00} face {Cliffs.CorniceFace(x, z):0.00} rough {Slope.Rough(x, z):0.00}");
                    }
                    TestPrefs.Restore(); TestHarness.Exit(0); return;
                }
        TestPrefs.Restore(); TestHarness.Exit(0);
    }

    static void JumpViews(Cliffs.Jump j, List<(string, Vector3, Quaternion, Vector3)> views, int seed)
    {
        string id = $"jump_c{j.cycle}k{j.k}";
        Debug.Log($"CORN seed{seed} {id}: lip z {j.z:0} drop {j.drop:0.0} face {j.face:0.0}");
        // ride in down the middle from 40 m up the in-run, as a rider would, and keep the chase camera's views
        float z0 = j.Start - 40f, v0 = 15f;
        Show(1, 1.4f);
        r.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); r.Begin();
        r.Heading = 0f; r.Steer = 0f;
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z0), z0)).normalized * v0;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        chase.rider = r; chase.Snap();
        float lip = Cliffs.LipZ(j, 0f);
        bool approach = false, nearLip = false, launch = false, landing = false; float air = 0f, ground = 0f; bool flew = false;
        for (int n = 0; n < 120 * 14; n++)
        {
            r.Steer = r.Crashed ? 0f : Mathf.Clamp((Slope.Centre(r.Z + 10f) - r.X) * 0.08f, -0.4f, 0.4f);
            if (r.Z < lip - 30f && r.Speed > v0 && !r.Airborne) r.Body.linearVelocity *= v0 / r.Speed;
            TestHarness.Step(r, body, Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 2 == 0)
            {
                body.Apply(2 * Dt);
                ChaseCamera.TestDt = 2 * Dt;
                camStep.Invoke(chase, null);
                Surfaces.Step(r.Body.position, r.RunNow, 2 * Dt, true);
                foreach (var ps in bodyFx) ps.Simulate(2 * Dt, false, false, false);
            }
            if (r.Airborne) { air += Dt; flew = true; } else if (flew) ground += Dt;
            if (!approach && r.Z > lip - 24f)
            {
                // (the first frame drawn can come out without the snow's own look: drawn once to warm up, and again)
                Grab(prefix + "warmup.png"); System.IO.File.Delete(prefix + "warmup.png");
                Grab(prefix + id + "_approach_day.png"); approach = true;
            }
            if (!nearLip && r.Z > lip - 6f) { Grab(prefix + id + "_lip_day.png"); nearLip = true; }
            if (!launch && air > 0.3f) { Grab(prefix + id + "_launch_day.png"); launch = true; }
            if (!landing && ground > 0.5f) { Grab(prefix + id + "_landing_day.png"); landing = true; break; }
            if (r.Crashed) break;
        }
        Debug.Log($"CORN {id} ride: {(r.Crashed ? "CRASHED" : $"{r.Speed * 3.6f:0} km/h")}, air {air:0.00} s");
        float c = Slope.Centre(lip);
        float h0 = Slope.DrawnHeight(c, lip);
        // its profile: from out beside the run, level with a point under the lip, looking along it
        {
            Vector3 at = new Vector3(c + 4f, h0 - 1.2f, lip + 1.5f);
            Vector3 from = new Vector3(c + Slope.PisteHalf + 6f, 0f, lip + 3.5f);
            from.y = Mathf.Max(Slope.DrawnHeight(from.x, from.z) + 1.7f, h0 - 0.4f);
            views.Add((id + "_side", from, Quaternion.LookRotation(at - from), at));
        }
        // from the landing, looking back up under the lip (the undercut)
        {
            float zb = lip + j.face + 9f;
            Vector3 from = new Vector3(c + 7f, Slope.DrawnHeight(c + 7f, zb) + 1.7f, zb);
            Vector3 at = new Vector3(c - 3f, h0 - 1f, lip);
            views.Add((id + "_below", from, Quaternion.LookRotation(at - from), at));
        }
    }

    static void LedgeViews(Cliffs.LedgeLine L, List<(string, Vector3, Quaternion, Vector3)> views, int seed)
    {
        string id = $"ledge_c{L.cycle}";
        var e = L[0];
        float lip = Cliffs.LedgeLipZ(L, 0, 0f);
        float x = Slope.Centre(lip) + L.side * L.off;
        float h0 = Slope.DrawnHeight(x, lip);
        Debug.Log($"CORN seed{seed} {id}: {L.count} ledges, first lip z {lip:0} drop {e.drop:0.0}");
        // riding up to the first lip: 16 m up its in-run, a rider's height and a little more, looking down the line
        {
            float zf = lip - 16f;
            Vector3 from = new Vector3(Slope.Centre(zf) + L.side * L.off, 0f, zf); from.y = Slope.DrawnHeight(from.x, from.z) + 2.6f;
            Vector3 at = new Vector3(x, h0 - 3f, lip + 10f);
            views.Add((id + "_approach", from, Quaternion.LookRotation(at - from), at));
        }
        // its profile, from out at the strip's side
        {
            float zs = lip + 3f;
            float xs = Slope.Centre(zs) + L.side * (L.off + L.half + 9f);
            Vector3 from = new Vector3(xs, 0f, zs); from.y = Mathf.Max(Slope.DrawnHeight(xs, zs) + 1.7f, h0 - 0.5f);
            Vector3 at = new Vector3(x, h0 - 1.3f, lip + 1.5f);
            views.Add((id + "_side", from, Quaternion.LookRotation(at - from), at));
        }
        // from its landing, looking back up under the lip
        {
            float zb = lip + e.face + 8f;
            float xb = Slope.Centre(zb) + L.side * (L.off + 4f);
            Vector3 from = new Vector3(xb, Slope.DrawnHeight(xb, zb) + 1.7f, zb);
            Vector3 at = new Vector3(x - L.side * 2f, h0 - 1f, lip);
            views.Add((id + "_below", from, Quaternion.LookRotation(at - from), at));
        }
    }

    static void Show(int choice, float clock)
    {
        Weather.TestChoice = choice; TimeOfDay.TestClock = clock;
        weather.Step(0f); dayNight.ApplyNow();
        foreach (var ps in Systems()) ps.Clear();
        if (choice == 1) Surfaces.Day = default;
    }

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
        Debug.Log("CORN-SHOT " + file);
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
