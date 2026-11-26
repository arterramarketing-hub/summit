using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders every weather (Weather.cs) from the rider's view at speed, in the summit camp and on the start page, by day
/// and by night; a storm rolling in during a run; riding down into a cloud sea; and logs what each weather costs
/// (particles alive, the screen their quads cover, draw calls, CPU per frame).
/// Batch: -executeMethod WeatherShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes WeatherShots/*.png in the project folder (or into $WEATHER_OUT).
/// </summary>
public static class WeatherShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float Dt = 1f / 30f;
    static SummitGame game; static Camera cam; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Camp camp; static Atmosphere atmo; static Weather weather; static ChaseCamera chase; static SnowFX fx;
    static MethodInfo tUpd, fUpd, late, aLate;
    static string outDir;

    static readonly (int choice, string name)[] Kinds =
        { (1, "clear"), (2, "windy"), (3, "snow"), (4, "storm"), (5, "fog"), (6, "cloudsea") };
    static readonly (string name, float clock)[] Times = { ("day", 1.4f), ("night", 3.4f) };

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("WEATHER_OUT") ?? "WeatherShots";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;   // (no placeholder pink in the first shots)
        Weather.TestChoice = 1;   // (asked for: the weather is built even in batchmode)
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight"); camp = Get<Camp>("camp"); fx = Get<SnowFX>("fx"); weather = Get<Weather>("weather");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); atmo = Object.FindFirstObjectByType<Atmosphere>();
        Debug.Log($"WEATHERSHOT built: weather {(weather != null ? weather.Base.ToString() : "NONE")}, seed {Slope.Seed}");
        if (weather == null) { TestHarness.Exit(1); return; }
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        cam.aspect = W / (float)H;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = Dt;
        for (int i = 0; i < 6; i++) Shoot(null);   // (placeholder materials, then the real ones)

        // ---- from the rider at speed, every weather by day and by night
        Ride(10f);
        Show(1, 1.4f, 0.5f); Shoot(null); Shoot(null);
        Debug.Log($"WEATHERSHOT ride view: rider {rider.transform.position} speed {rider.Speed * 3.6f:0} km/h cam {cam.transform.position}");
        foreach (var (choice, name) in Kinds)
            foreach (var (when, clock) in Times)
            {
                Show(choice, clock, 3f);
                Shoot($"ride_{name}_{when}.png");
                Debug.Log($"WEATHERSHOT light {name} {when}: fog {RenderSettings.fogColor} x{RenderSettings.fogDensity:0.0000} amb sky {RenderSettings.ambientSkyColor} eq {RenderSettings.ambientEquatorColor} sun {RenderSettings.sun.color} x{RenderSettings.sun.intensity:0.00} shadow {RenderSettings.sun.shadowStrength:0.00}");
                if (when == "day") Cost(name);
            }
        foreach (var (when, _) in Times) Sheet($"sheet_ride_{when}.png", System.Array.ConvertAll(Kinds, k => $"ride_{k.name}_{when}.png"));
        // morning: diamond dust in the clear, the cloud sea at its likeliest
        Show(1, 0.4f, 3f); Shoot("ride_clear_morning.png");
        Show(6, 0.4f, 3f); Shoot("ride_cloudsea_morning.png");
        // the same moment looking back up the hill: the storm's own sky, and where a front comes from
        Show(4, 1.4f, 2f); LookBack(); Shoot("ride_storm_day_back.png");
        Ride(0.2f);

        // ---- a storm rolling in during a run (FRONT: clear, the front coming in from 8 s)
        Show(7, 1.4f, 2f);
        var fronts = new List<string>();
        foreach (float t in new[] { 0f, 12f, 22f, 32f, 42f, 56f })
        {
            Weather.TestRunTime = t;
            Show(7, 1.4f, 1.5f, false);
            string f = $"front_{t:00}s.png"; Shoot(f); fronts.Add(f);
            LookBack(); string b = $"front_{t:00}s_back.png"; Shoot(b);
            Debug.Log($"WEATHERSHOT front {t:0}s: progress {weather.FrontProgress:0.00} cloud {weather.Now.cloud:0.00} snow {weather.Now.snow:0.00} wind {weather.Now.wind:0.00} halo {weather.Now.halo:0.00}");
            Ride(0.05f);
        }
        Weather.TestRunTime = -1f;
        Sheet("sheet_front.png", fronts.ToArray());
        Sheet("sheet_front_back.png", System.Array.ConvertAll(fronts.ToArray(), s => s.Replace(".png", "_back.png")));
        // the halo foreshadowing it, on the sun (morning: the sun ahead of the rider, low)
        Weather.TestRunTime = 0f; Show(7, 0.4f, 1f, false); LookAtSun(); Shoot("front_halo_morning.png"); Weather.TestRunTime = -1f;
        Ride(0.05f);

        // ---- riding down into the cloud sea
        Show(6, 1.4f, 0.5f);
        var sea = new List<string>();
        foreach (float h in new[] { 700f, 180f, 30f, -40f, -weather.SeaThick - 90f })
        {
            float z = ZAt(weather.SeaTop + h + 3f);
            rider.ResetAt(Slope.Centre(z), z); rider.Begin();
            rider.Body.linearVelocity = new Vector3(0f, -0.58f, 0.81f) * 22f;
            terrain.Prewarm(); props.Prewarm(); chase.Snap();
            Ride(2.5f);
            Show(6, 1.4f, 2f, false);
            string f = $"cloudsea_{(h >= 0 ? "+" : "")}{h:0}m.png"; Shoot(f); sea.Add(f);
            Debug.Log($"WEATHERSHOT cloudsea fx: {weather.FxState}");
            Debug.Log($"WEATHERSHOT cloudsea cam {cam.transform.position.y - weather.SeaTop:0} m from the top (sea top {weather.SeaTop:0}, {weather.SeaThick:0} thick): fog {RenderSettings.fogDensity:0.0000} deck {weather.Now.deck:0.00} seaV {weather.Now.seaV:0.00}");
        }
        Sheet("sheet_cloudsea_descent.png", sea.ToArray());
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;

        // ---- the camp
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = Dt;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(3f, true, true);
        var from = camp.ChaletPoint(9f, 0f, 22f); var to = camp.ChaletPoint(0f, 0f, 0f);
        camp.TeleportTo(from, Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg);
        SettleCamp();
        foreach (var (choice, name) in Kinds)
            foreach (var (when, clock) in Times) { Show(choice, clock, 3f, true, true); Shoot($"camp_{name}_{when}.png"); }
        foreach (var (when, _) in Times) Sheet($"sheet_camp_{when}.png", System.Array.ConvertAll(Kinds, k => $"camp_{k.name}_{when}.png"));
        ChaseCamera.TestDt = 0f;

        // ---- the start page's fly-around (the FLIGHT view: the weather is the far mountain's)
        int homeWas = StartPage.Home; StartPage.Home = 1;
        typeof(SummitGame).GetMethod("ToTitle", NP).Invoke(game, null);
        StartPage.Home = homeWas;
        var start = Get<StartPage>("start");
        foreach (var (choice, name) in Kinds)
            foreach (var (when, clock) in Times)
            {
                TimeOfDay.TestClock = clock;
                start.Clock = StartPage.OrbitSeconds * 0.05f; start.Preview(0f);
                Show(choice, clock, 4f, true, true);
                start.Far.Shade(-RenderSettings.sun.transform.forward, 0f, -1f);
                dayNight.ApplyNow();
                Shoot($"start_{name}_{when}.png");
            }
        foreach (var (when, _) in Times) Sheet($"sheet_start_{when}.png", System.Array.ConvertAll(Kinds, k => $"start_{k.name}_{when}.png"));
        StartCost(start);
        // back to day and clear before leaving: the Spray, Trail and Snow materials are project assets
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; weather.Step(0f); dayNight.ApplyNow();
        Object.DestroyImmediate(weather.gameObject);
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    // ---------------------------------------------------------------- driving it

    /// <summary>Ride on for `seconds` with the world following (terrain, forest, camera, atmosphere, weather, particles).</summary>
    static void Ride(float seconds)
    {
        for (float t = 0f; t < seconds; t += Dt)
        {
            for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            body.Apply(Dt);
            late.Invoke(chase, null);
            Frame(Dt);
        }
    }

    /// <summary>One frame of the air: wind, weather, atmosphere, light, and every particle system moved on.</summary>
    static void Frame(float dt)
    {
        TreeWind.Advance(dt, cam.transform.position);
        weather.Step(dt);
        aLate.Invoke(atmo, null);
        dayNight.ApplyNow();
        foreach (var ps in Systems()) ps.Simulate(dt, false, false, false);
    }

    static IEnumerable<ParticleSystem> Systems()
    {
        foreach (var ps in atmo.GetComponentsInChildren<ParticleSystem>()) yield return ps;
        foreach (var ps in weather.GetComponentsInChildren<ParticleSystem>(true)) yield return ps;
        foreach (var ps in cam.GetComponentsInChildren<ParticleSystem>()) yield return ps;
    }

    /// <summary>Put a weather on at a time of day and let its air fill (`warm` seconds), the camera held where it is.</summary>
    static void Show(int choice, float clock, float warm, bool fresh = true, bool still = false)
    {
        Weather.TestChoice = choice; TimeOfDay.TestClock = clock;
        weather.Step(0f);
        if (fresh) foreach (var ps in Systems()) ps.Clear();
        // a moment with a gust over the camera, so the windy weathers show it
        if (fresh) GustHere();
        var keep = (cam.transform.position, cam.transform.rotation);
        for (float t = 0f; t < warm; t += Dt)
        {
            if (still) camp.CameraStep(0f);
            cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
            Frame(Dt);
        }
        cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
        weather.Step(0f); dayNight.ApplyNow();
        fx.Sprinkle(Mathf.RoundToInt(400 * Mathf.Clamp01(fx.glintRate)));
    }

    static void GustHere()
    {
        Vector3 p = cam.transform.position;
        float best = 0f, at = TreeWind.Clock;
        for (float c = 0f; c < 900f; c += 1.5f) { TreeWind.Set(c, TreeWind.Strength); float g = TreeWind.Gust(p.x, p.z); if (g > best) { best = g; at = c; } if (g > 0.85f) break; }
        TreeWind.Set(at - 2f, TreeWind.Strength);
    }

    static void LookBack()
    {
        var f = cam.transform.forward; f.y = 0f;
        cam.transform.rotation = Quaternion.LookRotation(-f.normalized + Vector3.up * 0.25f);
    }

    static void LookAtSun()
    {
        var s = -RenderSettings.sun.transform.forward;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(s.x, Mathf.Max(s.y, 0.12f), s.z));
        cam.fieldOfView = 75f;
    }

    static void SettleCamp()
    {
        for (int i = 0; i < 40; i++) { camp.Walk(Vector2.zero, Dt); body.Apply(Dt); camp.CameraStep(Dt); tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); aLate.Invoke(atmo, null); }
    }

    /// <summary>The distance down the run where the fall line is at height y.</summary>
    static float ZAt(float y)
    {
        float lo = 0f, hi = Slope.FinishZ;
        for (int i = 0; i < 40; i++) { float m = (lo + hi) * 0.5f; if (Slope.FallLine(m) > y) lo = m; else hi = m; }
        return (lo + hi) * 0.5f;
    }

    // ---------------------------------------------------------------- what it costs

    /// <summary>Particles alive per system, and the share of the screen their quads cover (overdraw, in screens).</summary>
    static void Cost(string name)
    {
        float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var sb = new System.Text.StringBuilder();
        float total = 0f; int alive = 0;
        var buf = new ParticleSystem.Particle[16000];
        foreach (var ps in Systems())
        {
            int n = ps.GetParticles(buf);
            if (n == 0) continue;
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            float cover = 0f; int seen = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = local ? ps.transform.TransformPoint(buf[i].position) : buf[i].position;
                Vector3 v = cam.transform.InverseTransformPoint(p);
                if (v.z < cam.nearClipPlane) continue;
                // (the renderer clamps a particle to maxParticleSize of the view)
                float px = Mathf.Min(buf[i].GetCurrentSize(ps) / (2f * v.z * tan) * H, pr.maxParticleSize * W);
                if (Mathf.Abs(v.x) > v.z * tan * cam.aspect + buf[i].GetCurrentSize(ps) || Mathf.Abs(v.y) > v.z * tan + buf[i].GetCurrentSize(ps)) continue;
                float len = px;
                if (pr.renderMode == ParticleSystemRenderMode.Stretch)
                    len = (buf[i].GetCurrentSize(ps) * pr.lengthScale + buf[i].velocity.magnitude * pr.velocityScale) / (2f * v.z * tan) * H;
                cover += Mathf.Min(px * len, W * H); seen++;
            }
            cover /= W * H;
            total += cover; alive += n;
            sb.Append($" {ps.name}={n}({seen} in view, {cover:0.00} scr)");
        }
        // the CPU: the weather's own step and the light pass after it, then the particles' simulation, per frame
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 120; i++) { weather.Step(1f / 60f); }
        double stepMs = sw.Elapsed.TotalMilliseconds / 120.0;
        // the weather's own light pass (run after DayNight's each frame), alone
        var onDn = typeof(Weather).GetMethod("OnDayNight", NP);
        var args = new object[] { dayNight };
        sw.Restart();
        for (int i = 0; i < 500; i++) onDn.Invoke(weather, args);
        double lightMs = sw.Elapsed.TotalMilliseconds / 500.0;
        dayNight.ApplyNow();
        int renderers = 0;
        foreach (var r in weather.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy && (!(r is ParticleSystemRenderer) || r.GetComponent<ParticleSystem>().particleCount > 0)) renderers++;
        foreach (var r in cam.GetComponentsInChildren<ParticleSystemRenderer>()) if (r.GetComponent<ParticleSystem>().particleCount > 0) renderers++;
        sw.Restart();
        int frames = 30;
        for (int i = 0; i < frames; i++) foreach (var ps in Systems()) ps.Simulate(1f / 60f, false, false, false);
        double simMs = sw.Elapsed.TotalMilliseconds / frames;
        Debug.Log($"WEATHERSHOT cost {name}: particles {alive}, quads cover {total:0.00} screens; weather step {stepMs:0.000} ms, its light pass {lightMs:0.000} ms, particle sim {simMs:0.00} ms (editor CPU); weather draws {renderers};" + sb);
    }

    static void StartCost(StartPage start)
    {
        foreach (var (choice, name) in Kinds)
        {
            Show(choice, 1.4f, 4f, true, true);
            int n = 0; foreach (var ps in weather.GetComponentsInChildren<ParticleSystem>(true)) if (ps.gameObject.layer == FarMountain.Layer) n += ps.particleCount;
            Debug.Log($"WEATHERSHOT start fx {name}: {weather.FxState}");
            Debug.Log($"WEATHERSHOT start {name}: on the start page {weather.OnStartPage}, far snow particles {n}, fog {RenderSettings.fogColor} sky overcast {weather.Now.cloud:0.00} sea {weather.Now.sea:0.00}");
        }
    }

    // ---------------------------------------------------------------- pictures

    static void Shoot(string file)
    {
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g);
        }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        if (file != null)
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
    }

    /// <summary>Pictures side by side at half size.</summary>
    static void Sheet(string file, params string[] names)
    {
        int w = W / 2, h = H / 2;
        var sheet = new Texture2D(w * names.Length, h, TextureFormat.RGB24, false);
        for (int k = 0; k < names.Length; k++)
        {
            var t = new Texture2D(2, 2);
            if (!t.LoadImage(File.ReadAllBytes(Path.Combine(outDir, names[k])))) continue;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    sheet.SetPixel(k * w + x, y, t.GetPixel(x * 2, y * 2));
            Object.DestroyImmediate(t);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }
}
