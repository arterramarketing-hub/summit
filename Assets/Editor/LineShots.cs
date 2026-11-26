using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The feature lines (Lines), shot: every line on a mountain, ridden down its lane at the typical speed with the chase
/// camera (each hit coming up, in the air off it, and landed; each jib slid), and from overviews: from high above its
/// entry looking down it, and from out beside it, its top half and its bottom half.
/// LINE_SEED=seed, LINE_OUT=dir. Batch: -executeMethod LineShots.Shots (no -nographics: it renders).
/// </summary>
public static class LineShots
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1280, OW = 1600, OH = 900;
    const float Dt = 1f / 120f;
    static SummitGame game; static Camera cam; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase;
    static MethodInfo camStep, aLate;
    static RenderTexture rt, ort; static Texture2D tex, otex;
    static ParticleSystem[] bodyFx;

    public static void Shots()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        string outDir = System.Environment.GetEnvironmentVariable("LINE_OUT") ?? "line-shots";
        System.IO.Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        r = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight"); weather = Get<Weather>("weather");
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        Physics.simulationMode = SimulationMode.Script;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("LINE_SEED"), out int ss) ? ss : 1000;
        TestBoot.NewMountain(game, seed, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        if (chase == null) chase = Object.FindFirstObjectByType<ChaseCamera>();
        chase.enabled = false;
        rt = new RenderTexture(W, H, 24); tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        ort = new RenderTexture(OW, OH, 24); otex = new Texture2D(OW, OH, TextureFormat.RGB24, false);
        bodyFx = body.GetComponentsInChildren<ParticleSystem>(true);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        Show(1, 1.4f);
        var lines = Lines.All();
        Debug.Log($"LSHOT seed{seed}: {lines.Count} line(s)");
        foreach (var L in lines)
        {
            string id = $"{outDir}/s{seed}_line{L.id}_{L.style}";
            // the chase camera down its lane (the dressing built round the rider, as in a run)
            if (dress != null) { dress.target = r.transform; }
            terrain.target = r.transform; props.target = r.transform;
            Ride(L, id);
            // the overviews (the ground, the trees and the dressing built round each)
            var focus = new GameObject("LineShotFocus").transform;
            if (dress != null) dress.target = focus;
            terrain.target = focus; props.target = focus;
            float mid = (L.z0 + L.z1) * 0.5f, lane0 = L.hits[0].lane;
            // from out beside the run on the line's side, up the hill of its top half and its bottom half, looking across and down it
            foreach (var (name, za, zb) in new[] { ("top", L.z0 + 20f, mid + 10f), ("bottom", mid - 10f, L.z1) })
            {
                float zc = (za + zb) * 0.5f;
                Vector3 at = new Vector3(Slope.Centre(zc) + lane0, 0f, zc); at.y = Slope.DrawnHeight(at.x, at.z);
                Vector3 from = new Vector3(Slope.Centre(za) + L.side * 55f, 0f, za - 30f);
                from.y = Mathf.Max(Slope.DrawnHeight(from.x, from.z), Slope.DrawnHeight(Slope.Centre(za) + lane0, za)) + 26f;
                Overview(focus, dress, from, at, (from + at) * 0.5f, $"{id}_over_{name}.png");
            }
            // from above the entry, straight down the line
            {
                float zc = L.z0 + 150f;
                Vector3 at = new Vector3(Slope.Centre(zc) + lane0, 0f, zc); at.y = Slope.DrawnHeight(at.x, at.z);
                Vector3 from = new Vector3(Slope.Centre(L.z0 + 10f) + lane0 * 0.6f, 0f, L.z0 + 10f);
                from.y = Slope.DrawnHeight(from.x, from.z) + 20f;
                Overview(focus, dress, from, at, Vector3.Lerp(from, at, 0.4f), $"{id}_over_down.png");
            }
            Object.DestroyImmediate(focus.gameObject);
        }
        Leave(0);
    }

    static void Overview(Transform focus, CourseDressing dress, Vector3 from, Vector3 at, Vector3 round, string file)
    {
        focus.position = round;   // (the ground, the trees and the dressing streamed round the middle of the view)
        if (dress != null) dress.Prewarm();
        Surfaces.Step(round, 0f, 0f, true);
        terrain.Prewarm(); props.Prewarm();
        var q = Quaternion.LookRotation(at - from);
        cam.transform.SetPositionAndRotation(from, q);
        float fov = cam.fieldOfView; cam.fieldOfView = 55f;
        Air(0.5f, from, q);
        Grab(file, true);
        Grab(file, true);   // (the first frame drawn can come out without the snow's own look)
        cam.fieldOfView = fov;
    }

    static void Ride(Lines.Line L, string id)
    {
        var jibs = new List<Course.Feature>();
        Lines.JibsOf(L.cycle, jibs);
        float z0 = L.z0 - 30f, v = Lines.Speed;
        r.ResetAt(LineTest.LaneX(L, jibs, z0), z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(r.X, z0)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) dress.Prewarm();
        chase.rider = r; chase.Snap();
        var took = new HashSet<string>();
        bool warmed = false; float air = 0f, ground = 0f, grind = 0f; int lastHit = -1;
        for (int n = 0; n < 120 * 30 && r.Z < L.z1 + 10f; n++)
        {
            r.Steer = CarveBot.Onto(r, z => LineTest.LaneX(L, jibs, z), 2.4f, 12f, 1.5f);
            if (!r.Airborne && !r.Grinding) { var vel = r.Body.linearVelocity; if (vel.magnitude > 1f) r.Body.linearVelocity = vel.normalized * v; }
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm(); }
            if (n % 2 == 0)
            {
                body.Apply(2 * Dt);
                ChaseCamera.TestDt = 2 * Dt;
                camStep.Invoke(chase, null);
                Surfaces.Step(r.Body.position, r.RunNow, 2 * Dt, true);
                foreach (var ps in bodyFx) ps.Simulate(2 * Dt, false, false, false);
            }
            if (r.Crashed) { Debug.Log($"LSHOT {id}: crashed at z{r.Z:0}"); break; }
            if (r.Airborne) { air += Dt; ground = 0f; } else { if (air > 0f) ground += Dt; }
            if (r.Grinding) grind += Dt; else grind = 0f;
            if (!warmed && r.Z > L.z0) { Grab(id + "_warmup.png", false); System.IO.File.Delete(id + "_warmup.png"); warmed = true; }
            for (int i = 0; i < L.hits.Length; i++)
            {
                var h = L.hits[i];
                string hn = $"{id}_hit{i}_{h.kind}";
                float into = h.IsJib ? h.z0 : h.LipZ;
                if (!took.Contains(hn + "a") && r.Z > into - 26f && r.Z < into) { took.Add(hn + "a"); Grab(hn + "_1coming.png", false); lastHit = i; }
                if (lastHit == i && h.IsJib && grind > 0.15f && !took.Contains(hn + "g")) { took.Add(hn + "g"); Grab(hn + "_2on.png", false); }
                if (lastHit == i && !h.IsJib && air > 0.35f && !took.Contains(hn + "f")) { took.Add(hn + "f"); Grab(hn + "_2air.png", false); }
                if (lastHit == i && took.Contains(hn + "f") && ground > 0.25f && !took.Contains(hn + "l")) { took.Add(hn + "l"); Grab(hn + "_3landed.png", false); }
            }
            if (!r.Airborne && ground > 0.25f) air = 0f;
        }
        Debug.Log($"LSHOT {id}: rode to z{r.Z:0}, {r.Speed * 3.6f:0} km/h, chain {r.LineChain}");
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

    static void Grab(string file, bool wide)
    {
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null); CliffSpires.DrawNow();
        var t = wide ? ort : rt; var x = wide ? otex : tex;
        cam.targetTexture = t; cam.aspect = (float)t.width / t.height; cam.Render();
        RenderTexture.active = t; x.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); x.Apply();
        System.IO.File.WriteAllBytes(file, x.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Debug.Log("LSHOT-FILE " + file);
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
