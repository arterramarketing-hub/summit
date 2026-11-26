using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The board's track and spray, and the hand on the snow (snowfx), at phone size, from the chase camera: a groomed
/// carve, deep powder, ice and a hard lean held to the hand-drag, by day and at night under a floodlight. Each case:
/// a strip of six frames 1/15 s apart, and a look back up the hill at the track it left. The hard lean also logs how
/// far the hands, knees, hips and the board's edges are above the snow (negative: through it).
/// Batch: -executeMethod SnowFxShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes into $SNOWFX_OUT (or SnowFxShots/), names prefixed by $SNOWFX_TAG (before/after).
/// </summary>
public static class SnowFxShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float Dt = 1f / 60f;
    static SummitGame game; static Camera cam; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase; static NightLights lights;
    static MethodInfo camStep, aLate;
    static string outDir, tag;
    /// <summary>The most particles alive in the rider's own systems over the last case (the cost log).</summary>
    static int peakParticles;
    static string handWhere = "";

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("SNOWFX_OUT") ?? "SnowFxShots";
        tag = System.Environment.GetEnvironmentVariable("SNOWFX_TAG") ?? "shot";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight"); weather = Get<Weather>("weather");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); atmo = Object.FindFirstObjectByType<Atmosphere>();
        lights = NightLights.Instance;
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        cam.aspect = W / (float)H;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;

        float dayZ = Stretch(Course.First + 500f, -1f), nightZ = dayZ;
        foreach (var l in NightLights.Lamps)
            if (l.kind == NightLights.Kind.Piste && l.head.z > dayZ) { float z = Stretch(l.head.z - 60f, l.head.z - 25f); if (z > 0f) { nightZ = z; break; } }
        Debug.Log($"SNOWFX stretches: day z {dayZ:0}, night z {nightZ:0}");

        var cases = new (string name, Snow snow, int mode)[] { ("carve", Snow.Corduroy, 0), ("powder", Snow.Powder, 0), ("ice", Snow.Ice, 0), ("drag", Snow.Corduroy, 1), ("dragpowder", Snow.Powder, 1) };
        foreach (var (light, z, clock) in new[] { ("day", dayZ, 1.4f), ("night", nightZ, 3.4f) })
        {
            Light(clock);
            var strips = new List<string>();
            foreach (var (name, snow, mode) in cases)
            {
                if (light == "night" && name == "dragpowder") continue;
                string only = System.Environment.GetEnvironmentVariable("SNOWFX_ONLY");
                if (!string.IsNullOrEmpty(only) && !name.StartsWith(only)) continue;
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SNOWFX_DAYONLY")) && light == "night") continue;
                Surfaces.Force = SnowMix.Only(snow);
                Case($"{light}_{name}", z, mode);
                strips.Add($"{tag}_{light}_{name}_back.png");
            }
            Sheet($"{tag}_{light}_tracks.png", strips.ToArray());
        }
        // the other rides, by day on the groomed run, leaned hard: nothing through the snow (and the poles' tips on it)
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SNOWFX_ONLY")) || System.Environment.GetEnvironmentVariable("SNOWFX_ONLY") == "rides")
        {
            Light(1.4f);
            Surfaces.Force = SnowMix.Only(Snow.Corduroy);
            foreach (var (name, kind, tube) in new[] { ("skis", RideKind.Skis, false), ("sled", RideKind.Toboggan, false), ("tube", RideKind.Snowboard, true) })
            {
                body.SetRide(kind); rider.Tube = tube; body.SetTube(tube);
                Case($"day_{name}", dayZ, 1);
            }
            body.SetRide(RideKind.Snowboard); rider.Tube = false; body.SetTube(false);
        }
        Surfaces.Force = null;

        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static float Stretch(float from, float near)
    {
        float lo = near > 0f ? near - 60f : from, hi = near > 0f ? near + 60f : Slope.FinishZ - 400f;
        for (float z = lo; z < hi; z += 10f)
        {
            bool ok = true;
            for (float d = -40f; d <= 160f && ok; d += 10f)
            {
                if (Course.At(z + d) != Course.Section.Cruise || Ridge.InCanyons(z + d) || Cliffs.JumpNear(z + d, 10f, out _)) ok = false;
                else if (Slope.Normal(Slope.Centre(z + d), z + d).y < 0.74f) ok = false;
                for (float a = -12f; a <= 12f && ok; a += 6f) if (Mathf.Abs(Slope.Kicker(Slope.Centre(z + d) + a, z + d)) > 0.01f) ok = false;
            }
            if (ok) return z;
        }
        return near > 0f ? -1f : Course.First + 500f;
    }

    static void Light(float clock)
    {
        Weather.TestChoice = 1; TimeOfDay.TestClock = clock;
        weather.Step(0f);
        for (int i = 0; i < 30; i++) Frame(1f / 30f);
        dayNight.ApplyNow();
        lights?.Prewarm();
    }

    static void Frame(float dt)
    {
        TreeWind.Advance(dt, cam.transform.position);
        weather.Step(dt);
        aLate.Invoke(atmo, null);
        dayNight.ApplyNow();
    }

    /// <summary>
    /// mode 0: carving S-turns at about 60 km/h; mode 1: a hard lean held one way, then the other (the hand-drag).
    /// Six chase frames 1/15 s apart once the turn is on, then a look back up the hill at the track.
    /// </summary>
    static void Case(string name, float z, int mode)
    {
        float V = mode == 1 ? 15f : 17f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        rider.Heading = 0f; rider.Steer = 0f;
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z), z)).normalized * V;
        terrain.Prewarm(); props.Prewarm(); lights?.Prewarm(); Physics.SyncTransforms();
        chase.rider = rider; chase.Snap();
        foreach (var ps in body.GetComponentsInChildren<ParticleSystem>(true)) ps.Clear(true);
        var files = new List<string>();
        int shots = 0;
        peakParticles = 0;
        float minHand = 9f, minKnee = 9f, minHip = 9f, minBoard = 9f, dragT = 0f;
        bool touched = false; double frags = 0;
        int total = mode == 1 ? 60 * 5 : 60 * 6;
        for (int n = 0; n < total; n++)
        {
            float t = n * Dt;
            float steer;
            if (mode == 1) steer = t < 0.6f ? 0f : t < 2.6f ? 1f : -1f;
            else steer = 0.8f * Mathf.Sin(t * 2.2f);
            // keep it on the run
            steer = Mathf.Clamp(steer + (Slope.Centre(rider.Z + 15f) - rider.X) * 0.03f, -1f, 1f);
            rider.Steer = rider.Crashed ? 0f : steer;
            if (rider.Speed > V * 1.15f && !rider.Airborne) rider.Body.linearVelocity *= V * 1.15f / rider.Speed;
            rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 30 == 0) { terrain.Prewarm(); props.Prewarm(); lights?.Prewarm(); }
            if (n % 2 == 0)
            {
                body.Apply(2 * Dt);
                ChaseCamera.TestDt = 2 * Dt;
                camStep.Invoke(chase, null);
                Surfaces.Step(rider.Body.position, rider.RunNow, 2 * Dt, true);
                foreach (var ps in body.GetComponentsInChildren<ParticleSystem>(true)) { ps.Simulate(2 * Dt, false, false, false); }
                Frame(2 * Dt);
                int alive = 0; foreach (var ps in body.GetComponentsInChildren<ParticleSystem>(true)) alive += ps.particleCount;
                peakParticles = Mathf.Max(peakParticles, alive);
                if (t > 0.5f) Clearance(ref minHand, ref minKnee, ref minHip, ref minBoard);   // (the bones are where the last case left them until the pose has run)
                if (body.HandOnSnow > 0.5f || body.PoleOnSnow > 0.5f) dragT += 2 * Dt;
                // the hand on the snow, from beside it (the first time it is well down on it, a moment in)
                if (!touched && body.HandOnSnow > 0.9f && t > 1.2f) { touched = true; Side($"{tag}_{name}_touch.png", body.HandDragAt); }
                // the spray's overdraw at its height (powder, a hard push): every fragment it draws, counted
                if (name.EndsWith("powder") && n == 300) frags = Overdraw();
                // once the turn is on: every fourth frame (1/15 s)
                bool on = mode == 1 ? (t > 1.6f && t < 2.6f) : t > 3.0f;
                if (on && n % 8 == 0 && shots < 6) { string f = $"{tag}_{name}_ride{shots}.png"; Shoot(f); files.Add(f); shots++; }
                // the hard lean, from beside it: low, on the inside of the turn, 3 m off
                if (mode == 1 && (n == 140 || n == 260)) Side($"{tag}_{name}_side{(n == 140 ? "R" : "L")}.png");
            }
        }
        Sheet($"{tag}_{name}_strip.png", files.ToArray());
        // the look back: 9 m on down the run and 2.5 m up, looking back up at where the board came down
        Vector3 r = rider.transform.position;
        Vector3 eye = new Vector3(r.x + 2f, 0f, r.z + 9f); eye.y = Slope.DrawnHeight(eye.x, eye.z) + 2.5f;
        Vector3 at = new Vector3(r.x, 0f, r.z - 8f); at.y = Slope.DrawnHeight(at.x, at.z);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
        Frame(0f);
        Shoot($"{tag}_{name}_back.png");
        Debug.Log($"SNOWFX {name}: z {rider.Z:0}, {rider.Speed * 3.6f:0} km/h, crashed {rider.Crashed}; peak particles {peakParticles}; " +
                  (frags > 0 ? $"spray overdraw {frags:0.00} screens; " : "") + $"lowest over the snow: hand {minHand:+0.00;-0.00} m, knee {minKnee:+0.00;-0.00}, hips {minHip:+0.00;-0.00}, board edge {minBoard:+0.00;-0.00}; hand on the snow {dragT:0.0} s; lowest hand: {handWhere}");
    }

    static void Side(string file) => Side(file, Vector3.zero);

    static void Side(string file, Vector3 look)
    {
        var keep = (cam.transform.position, cam.transform.rotation);
        Vector3 r = rider.transform.position;
        Vector3 v = rider.Velocity; v.y = 0f; v = v.sqrMagnitude > 0.01f ? v.normalized : Vector3.forward;
        Vector3 inside = Vector3.Cross(Vector3.up, v) * -Mathf.Sign(rider.Lean);   // (leaning right: the inside is to the right)
        Vector3 eye = r + inside * 3.2f + v * 0.6f; eye.y = Slope.DrawnHeight(eye.x, eye.z) + 0.45f;
        Vector3 at = look != Vector3.zero ? Vector3.Lerp(look, r, 0.35f) : r + Vector3.up * 0.2f;
        if (look != Vector3.zero) { eye = look + (eye - r) * 0.9f - v * 1.2f; eye.y = Slope.DrawnHeight(eye.x, eye.z) + 0.6f; }
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
        Shoot(file);
        cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
        var br = (Transform)typeof(RiderBody).GetField("boardRoot", NP).GetValue(body);
        var hr = body.Hero;
        if (hr != null) for (int k = 0; k < 2; k++) Debug.Log($"SNOWFX side bone {k}: hand {hr.Hand[k].position - r} (over snow {hr.Hand[k].position.y - Slope.DrawnHeight(hr.Hand[k].position.x, hr.Hand[k].position.z):0.00}), knee {hr.LowerLeg[k].position - r}, active {hr.Hand[k].gameObject.activeInHierarchy}, model {hr.Model.name} {hr.Model.gameObject.activeInHierarchy}");
        Debug.Log($"SNOWFX side {file}: lean {rider.Lean * Mathf.Rad2Deg:0} deg, crouch {rider.Crouch:0.00}, body lean {rider.BodyLean:0}, board centre {br.position.y - Slope.DrawnHeight(br.position.x, br.position.z):+0.00;-0.00} m over the snow, rider {r.y - Slope.DrawnHeight(r.x, r.z):+0.00;-0.00}");
    }

    /// <summary>The rider's particles alone, each fragment counted (no depth test: an upper bound), in screens.</summary>
    static double Overdraw()
    {
        var count = new Material(Shader.Find("Hidden/NightLightsCount"));
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf);
        var px = new Texture2D(W, H, TextureFormat.RGBAHalf, false);
        var rs = new List<ParticleSystemRenderer>(body.GetComponentsInChildren<ParticleSystemRenderer>(true));
        var layers = new List<int>(); var mats = new List<Material[]>();
        foreach (var r in rs) { layers.Add(r.gameObject.layer); mats.Add(r.sharedMaterials); r.gameObject.layer = 31; r.sharedMaterial = count; }
        int mask = cam.cullingMask; var clear = cam.clearFlags; var bg = cam.backgroundColor;
        cam.cullingMask = 1 << 31; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear;
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
        RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, W, H), 0, 0); px.Apply(); RenderTexture.active = null;
        double sum = 0; foreach (var c in px.GetPixels()) sum += c.r;
        for (int i = 0; i < rs.Count; i++) { rs[i].gameObject.layer = layers[i]; rs[i].sharedMaterials = mats[i]; }
        cam.cullingMask = mask; cam.clearFlags = clear; cam.backgroundColor = bg;
        rt.Release(); Object.DestroyImmediate(px); Object.DestroyImmediate(count);
        int alive = 0; foreach (var ps in body.GetComponentsInChildren<ParticleSystem>(true)) alive += ps.particleCount;
        Debug.Log($"SNOWFX overdraw: {alive} particles alive, {sum / (W * H):0.00} screens of fragments");
        return sum / (W * H);
    }

    /// <summary>How far above the drawn snow the hands, knees, hips and the board's four corners are (the least of each so far).</summary>
    static void Clearance(ref float hand, ref float knee, ref float hip, ref float board)
    {
        if (!rider.Running || rider.Crashed || rider.Airborne) return;
        float Above(Vector3 p) => p.y - Slope.DrawnHeight(p.x, p.z);
        var h = body.Hero;
        if (h != null && h.Ok)
        {
            for (int s = 0; s < 2; s++)
            {
                if (h.Hand[s] != null && Above(h.Hand[s].position) < hand) { hand = Above(h.Hand[s].position); handWhere = $"hand {s} at {h.Hand[s].position - rider.transform.position} lean {rider.Lean * Mathf.Rad2Deg:0} steer {rider.Steer:0.0} drawn {Slope.DrawnHeight(h.Hand[s].position.x, h.Hand[s].position.z):0.00} height {Slope.Height(h.Hand[s].position.x, h.Hand[s].position.z):0.00} y {h.Hand[s].position.y:0.00}"; }
                if (h.LowerLeg[s] != null) knee = Mathf.Min(knee, Above(h.LowerLeg[s].position));
            }
            hip = Mathf.Min(hip, Above(h.Hips.position));
        }
        var br = (Transform)typeof(RiderBody).GetField("boardRoot", NP).GetValue(body);
        if (br != null)
            for (int i = -1; i <= 1; i += 2)
                for (int k = -1; k <= 1; k += 2)
                    board = Mathf.Min(board, Above(br.TransformPoint(new Vector3(i * 0.145f, 0f, k * 0.45f))));
    }

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
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
    }

    static void Sheet(string file, params string[] names)
    {
        int w = W / 2, h = H / 2;
        var sheet = new Texture2D(w * Mathf.Max(1, names.Length), h, TextureFormat.RGB24, false);
        for (int k = 0; k < names.Length; k++)
        {
            var p = Path.Combine(outDir, names[k]);
            if (!File.Exists(p)) continue;
            var t = new Texture2D(2, 2);
            if (!t.LoadImage(File.ReadAllBytes(p))) continue;
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
