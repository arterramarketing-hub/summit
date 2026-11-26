using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Debug = UnityEngine.Debug;

/// <summary>
/// What the periodic jobs of a run cost on the CPU (editor, so a guide to the phone, not its numbers): the reflections
/// redrawn, the cloud sea's deck moved, a weather rolled, a course cycle and a terrain chunk built; then a ride through a
/// cloud sea and through a storm front, frame by frame, logging each job's worst frame and how many frames went over 2 ms.
/// Batch: -executeMethod WeatherPerf.Run -logFile x.log   (no -nographics: the reflections are drawn)
/// </summary>
public static class WeatherPerf
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 6;
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props");
        var dayNight = Get<DayNight>("dayNight"); var weather = Get<Weather>("weather");
        var dressing = Get<CourseDressing>("dressing");
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); var fUpd = typeof(Forest).GetMethod("Update", NP);
        var dUpd = typeof(CourseDressing).GetMethod("Update", NP);
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP); var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        var dnLate = typeof(DayNight).GetMethod("LateUpdate", NP);

        // ---- each job alone
        dayNight.ApplyNow();
        var reflCam = (Camera)typeof(DayNight).GetField("reflCam", NP).GetValue(dayNight);
        var reflRT = (RenderTexture)typeof(DayNight).GetField("reflRT", NP).GetValue(dayNight);
        if (reflCam != null && reflRT != null)
        {
            for (int i = 0; i < 3; i++) reflCam.RenderToCubemap(reflRT);   // (warm)
            Time6("reflections, all six faces at once", 10, () => reflCam.RenderToCubemap(reflRT));
            Time6("reflections, one face", 30, () => reflCam.RenderToCubemap(reflRT, 1 << 2));
            if (!reflRT.autoGenerateMips) Time6("reflections, mips", 30, () => reflRT.GenerateMips());
        }
        float seaTop = weather.SeaTop;
        Time6("cloud deck: 120 slope heights (one frame's rows of a deck move now)", 20, () =>
        {
            float s = 0f;
            for (int j = 0; j < 3; j++) for (int i = 0; i < 40; i++) s += Slope.Height(i * 75f - 1460f, 900f + j * 75f - 1460f);
            if (s == 12345f) Debug.Log("");
        });
        Time6("cloud deck: 1600 slope heights (the old deck move, in one frame)", 5, () =>
        {
            float s = 0f;
            for (int j = 0; j < 40; j++) for (int i = 0; i < 40; i++) s += Slope.Height(i * 75f - 1460f, 900f + j * 75f - 1460f);
            if (s == 12345f) Debug.Log("");
        });
        int choice = 1;
        Time6("weather rolled", 6, () => { Weather.TestChoice = 1 + (choice++ % 6); weather.Step(0f); });
        var build = typeof(CourseDressing).GetMethod("BuildCycle", NP);
        int kc = 6;
        Time6("course cycle built", 4, () => { var go = (GameObject)build.Invoke(dressing, new object[] { kc++ }); Object.DestroyImmediate(go); });
        var chunk = typeof(SlopeStreamer).GetMethod("Build", NP);
        int kz = 40;
        Time6("terrain chunk built", 8, () => { var go = (GameObject)chunk.Invoke(terrain, new object[] { new Vector2Int(0, kz++) }); go.SetActive(false); });

        // ---- riding: each job's worst frame
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = Dt;
        foreach (var (choiceRide, name, startZ) in new[] { (6, "cloud sea", -1f), (7, "storm front", 0f) })
        {
            Weather.TestChoice = choiceRide; weather.Step(0f);
            if (startZ < 0f)
            {
                // start a little above the cloud sea's top, so the ride goes down into it
                float lo = 0f, hi = Slope.FinishZ;
                for (int i = 0; i < 40; i++) { float m = (lo + hi) * 0.5f; if (Slope.FallLine(m) > weather.SeaTop + 260f) lo = m; else hi = m; }
                float z = lo;
                rider.ResetAt(Slope.Centre(z), z); rider.Begin(); rider.Body.linearVelocity = new Vector3(0f, -0.58f, 0.81f) * 25f;
                terrain.Prewarm(); props.Prewarm(); dressing.Prewarm(); chase.Snap();
            }
            var worst = new Dictionary<string, double>(); var over = new Dictionary<string, int>(); var total = new Dictionary<string, double>();
            var bytes = new Dictionary<string, long>();
            void Job(string job, System.Action a)
            {
                long b0 = System.GC.GetAllocatedBytesForCurrentThread();
                long t0 = Stopwatch.GetTimestamp(); a(); double ms = (Stopwatch.GetTimestamp() - t0) * TickMs;
                bytes[job] = (bytes.TryGetValue(job, out var bb) ? bb : 0L) + System.GC.GetAllocatedBytesForCurrentThread() - b0;
                worst[job] = Mathf.Max((float)(worst.TryGetValue(job, out var w) ? w : 0.0), (float)ms);
                total[job] = (total.TryGetValue(job, out var s) ? s : 0.0) + ms;
                if (ms > 2.0) over[job] = (over.TryGetValue(job, out var o) ? o : 0) + 1;
            }
            int frames = 0;
            for (float t = 0f; t < 40f; t += Dt, frames++)
            {
                if (choiceRide == 7) Weather.TestRunTime = t + 4f;
                for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
                Job("terrain", () => tUpd.Invoke(terrain, null));
                Job("forest", () => fUpd.Invoke(props, null));
                Job("course", () => dUpd.Invoke(dressing, null));
                body.Apply(Dt);
                late.Invoke(chase, null);
                TreeWind.Advance(Dt, cam.transform.position);
                Job("weather", () => weather.Step(Dt));
                aLate.Invoke(atmo, null);
                Job("daynight", () => dnLate.Invoke(dayNight, null));
                if (rider.Crashed) { rider.ResetAt(rider.transform.position.x, rider.Z + 5f); rider.Begin(); rider.Body.linearVelocity = new Vector3(0f, -0.58f, 0.81f) * 25f; }
            }
            Weather.TestRunTime = -1f;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in worst) sb.Append($"  {kv.Key}: worst {kv.Value:0.00} ms, mean {total[kv.Key] / frames:0.000} ms, frames over 2 ms {(over.TryGetValue(kv.Key, out var o) ? o : 0)}, {bytes[kv.Key] / frames} B/frame");
            Debug.Log($"WEATHERPERF ride {name} ({frames} frames, rider z {rider.Z:0}, cam {cam.transform.position.y - weather.SeaTop:0} m from the sea's top):" + sb);
        }
        // ---- the audit's measures: a night storm at speed (the snowfall's particles, what simulating them costs), and a
        // whole storm front (how many reflection faces it draws)
        {
            TimeOfDay.TestClock = 3.4f; Weather.TestChoice = 4; weather.Step(0f); dayNight.ApplyNow();
            var systems = new List<ParticleSystem>(atmo.GetComponentsInChildren<ParticleSystem>());
            systems.AddRange(weather.GetComponentsInChildren<ParticleSystem>(true));
            systems.AddRange(cam.GetComponentsInChildren<ParticleSystem>());
            var snowfall = atmo.transform.Find("Snowfall").GetComponent<ParticleSystem>();
            double simWorst = 0, simSum = 0; long snowSum = 0; int snowMax = 0, all = 0, frames = 0;
            for (float t = 0f; t < 20f; t += Dt, frames++)
            {
                for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
                tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); dUpd.Invoke(dressing, null);
                body.Apply(Dt); late.Invoke(chase, null); TreeWind.Advance(Dt, cam.transform.position);
                weather.Step(Dt); aLate.Invoke(atmo, null); dnLate.Invoke(dayNight, null);
                long t0 = Stopwatch.GetTimestamp();
                foreach (var ps in systems) ps.Simulate(Dt, false, false, false);
                double ms = (Stopwatch.GetTimestamp() - t0) * TickMs;
                if (t > 6f) { simSum += ms; simWorst = System.Math.Max(simWorst, ms); snowSum += snowfall.particleCount; snowMax = Mathf.Max(snowMax, snowfall.particleCount); }
                if (rider.Crashed) { rider.ResetAt(rider.transform.position.x, rider.Z + 5f); rider.Begin(); rider.Body.linearVelocity = new Vector3(0f, -0.58f, 0.81f) * 25f; }
            }
            foreach (var ps in systems) all += ps.particleCount;
            int measured = frames - (int)(6f / Dt);
            var mr = atmo.transform.Find("Mist").GetComponent<ParticleSystemRenderer>();
            Debug.Log($"WEATHERPERF storm night at {rider.Speed * 3.6f:0} km/h: snowfall alive mean {snowSum / measured} max {snowMax} (cap {snowfall.main.maxParticles}, lifetime {snowfall.main.startLifetime.constantMin:0.0}-{snowfall.main.startLifetime.constantMax:0.0} s, noise {snowfall.noise.enabled}/{snowfall.noise.quality}); all particles at the end {all}; simulating them: mean {simSum / measured:0.00} ms, worst {simWorst:0.00} ms a frame (editor); mist maxParticleSize {mr.maxParticleSize}");
            TimeOfDay.TestClock = 1.4f;
            // a whole front: count the faces drawn into the reflections
            var faces = typeof(DayNight).GetField("FacesDrawn", BindingFlags.Public | BindingFlags.Static);
            int f0 = faces != null ? (int)faces.GetValue(null) : 0;
            marks.Clear(); PerfMarks.On = faces == null;
            if (PerfMarks.On) Application.logMessageReceived += Count;
            Weather.TestChoice = 7; weather.Step(0f); dayNight.ApplyNow();
            float r0 = Time.realtimeSinceStartup;
            for (float t = 0f; t < 60f; t += Dt)
            {
                Weather.TestRunTime = t;
                // (the reflections' own timer runs on real time: the front is played at its real pace)
                while (Time.realtimeSinceStartup - r0 < t) { }
                weather.Step(Dt); dnLate.Invoke(dayNight, null);
            }
            if (PerfMarks.On) Application.logMessageReceived -= Count;
            PerfMarks.On = false;
            int drawn = faces != null ? (int)faces.GetValue(null) - f0 : (marks.TryGetValue("reflections face", out var n) ? n : 0);
            Debug.Log($"WEATHERPERF a storm front (60 s, at its real pace): {drawn} reflection faces drawn ({drawn / 6f:0.0} captures)");
            Weather.TestRunTime = -1f;
        }

        // the markers a device run writes, over ten seconds of the storm front coming in (counted from the log)
        PerfMarks.On = true;
        Weather.TestChoice = 7; weather.Step(0f);
        Application.logMessageReceived += Count;
        for (float t = 0f; t < 10f; t += Dt)
        {
            Weather.TestRunTime = 10f + t * 2f;
            for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); dUpd.Invoke(dressing, null);
            body.Apply(Dt); late.Invoke(chase, null); TreeWind.Advance(Dt, cam.transform.position);
            typeof(Weather).GetMethod("LateUpdate", NP).Invoke(weather, null); aLate.Invoke(atmo, null); dnLate.Invoke(dayNight, null);
            if (rider.Crashed) { rider.ResetAt(rider.transform.position.x, rider.Z + 5f); rider.Begin(); }
        }
        PerfMarks.Flush();   // (the marks are kept in memory until a quiet moment)
        Application.logMessageReceived -= Count;
        Weather.TestRunTime = -1f;
        var mk = new System.Text.StringBuilder();
        foreach (var kv in marks) mk.Append($"  {kv.Key} x{kv.Value}");
        Debug.Log("WEATHERPERF markers in 10 s of a front coming in:" + mk);
        PerfMarks.On = false;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Weather.TestChoice = 1; weather.Step(0f); dayNight.ApplyNow();
        Object.DestroyImmediate(weather.gameObject);
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static readonly Dictionary<string, int> marks = new Dictionary<string, int>();
    static void Count(string line, string trace, LogType type)
    {
        if (!line.StartsWith("SUMMIT PERF ")) return;
        string rest = line.Substring(12);
        int cut = rest.IndexOfAny("0123456789".ToCharArray());
        string job = (cut > 0 ? rest.Substring(0, cut) : rest).Trim();
        if (job.StartsWith("weather rolled")) job = "weather rolled";
        marks[job] = (marks.TryGetValue(job, out var n) ? n : 0) + 1;
    }

    static void Time6(string what, int n, System.Action a)
    {
        double worst = 0, sum = 0;
        for (int i = 0; i < n; i++)
        {
            long t0 = Stopwatch.GetTimestamp(); a(); double ms = (Stopwatch.GetTimestamp() - t0) * TickMs;
            sum += ms; worst = System.Math.Max(worst, ms);
        }
        Debug.Log($"WEATHERPERF {what}: mean {sum / n:0.00} ms, worst {worst:0.00} ms ({n} runs)");
    }
}
