using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders the resort furniture (PisteKit.cs, TrailSigns.cs) at phone size, by day, at night under the floodlights
/// and in fog: riding between the piste poles; the rope line along a ridge's cliff side, along a deadly wall and
/// across a jump cliff's band; a snow gun and its hydrant; the patrol hut and its toboggan; each trail sign; a pole
/// knocked over and a rope ridden through. Logs the layout and what it costs (draw calls in view, triangles).
/// Batch: -executeMethod PisteShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes into $PISTE_OUT (or PisteShots/ in the project folder).
/// </summary>
public static class PisteShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float Dt = 1f / 30f;
    static SummitGame game; static Camera cam; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static ChaseCamera chase; static CourseDressing dress;
    static NightLights lights; static PisteKit kit; static Hamlets hamlets; static SkiLift lifts;
    static MethodInfo tUpd, fUpd, late, aLate;
    static string outDir;

    static readonly (string tag, int choice, float clock)[] DayNightFog = { ("day", 1, 1.4f), ("night", 1, 3.4f), ("fog", 5, 1.4f) };

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("PISTE_OUT") ?? "PisteShots";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight"); weather = Get<Weather>("weather"); dress = Get<CourseDressing>("dressing");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); atmo = Object.FindFirstObjectByType<Atmosphere>();
        lights = NightLights.Instance; kit = PisteKit.Instance;
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        if (kit == null || weather == null) { Debug.LogError("PISTESHOT: no PisteKit or Weather"); TestHarness.Exit(1); return; }
        var sw0 = System.Diagnostics.Stopwatch.StartNew();
        string stats = PisteKit.Stats();
        Debug.Log($"PISTESHOT layout: seed {Slope.Seed}, {stats}; trail signs {TrailSigns.Signs.Count}; laid out in {sw0.Elapsed.TotalMilliseconds:0} ms");
        var fails = new System.Text.StringBuilder();
        foreach (var kv in PisteKit.Fails) fails.Append($" {kv.Key}={kv.Value}");
        Debug.Log($"PISTESHOT not put up:{fails}");
        foreach (var s in TrailSigns.Signs) Debug.Log($"PISTESHOT sign {s.symbol} at z {s.z:0} side {s.side} ({Course.At(s.z + 30f)})");

        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        cam.aspect = W / (float)H;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = Dt;
        for (int i = 0; i < 6; i++) Shoot(null);

        var swb = System.Diagnostics.Stopwatch.StartNew();
        kit.Prewarm();
        Debug.Log($"PISTESHOT prewarm {swb.Elapsed.TotalMilliseconds:0} ms");

        // ---- riding down between the poles (a cruise stretch on the lower mountain, where the guns are)
        var items = PisteKit.Items;
        float rideZ = -1f;
        foreach (var it in items)
            if (it.kind == PisteKit.Kind.Gun && it.size > 0f && Course.At(it.foot.z - 60f) == Course.Section.Cruise && Course.At(it.foot.z + 20f) == Course.Section.Cruise && !Ridge.InCanyons(it.foot.z))
            { rideZ = it.foot.z - 60f; break; }
        if (rideZ < 0f) rideZ = Course.First + Course.Cycle + 40f;
        rider.ResetAt(Slope.Centre(rideZ) + 12f, rideZ); rider.Begin();
        Prewarm();
        Ride(2f);
        foreach (var (tag, choice, clock) in DayNightFog)
        {
            Show(choice, clock, 2f);
            Shoot($"ride_{tag}.png");
            if (tag == "day") Cost("ride_day");
            if (tag == "night") Cost("ride_night");
        }
        Sheet("sheet_ride.png", "ride_day.png", "ride_night.png", "ride_fog.png");
        // the same view with no kit, for comparison
        PisteKit.Disabled = true; kit.Prewarm(); Show(1, 1.4f, 1f); Shoot("ride_day_without.png"); PisteKit.Disabled = false; kit.Prewarm();

        // ---- the rope lines: along a ridge's (or a ledge's) cliff side, a deadly wall, across a jump cliff's band
        for (int c = 0; c < 6; c++)
        {
            if (!Ridge.Built(c)) continue;
            float zb = Course.First + c * Course.Cycle;
            float z = zb + Ridge.At + Ridge.RampIn;
            float s = Ridge.CliffSide(z, 1f) ? 1f : Ridge.CliffSide(z, -1f) ? -1f : 0f;
            if (s == 0f) continue;
            RopeView($"ridge", z - 20f, z + 80f, s, 4f);
            // and from off the edge, looking back up at the rope line against the sky
            Vector3 eye2 = Ground(Slope.Centre(z) + s * (Ridge.HalfWidth(z) + 6f), z + 20f, 1.6f);
            Vector3 at2 = Ground(Slope.Centre(z) + s * (Ridge.HalfWidth(z) + 1.3f), z, 0.9f);
            Spot("ridge_edge", new Vector3(Slope.Centre(z - 6f), 0f, z - 6f), eye2, at2, false);
            break;
        }
        for (int c = 0; c < 6; c++)
        {
            if (!Cliffs.WallOf(c, out var w)) continue;
            RopeView("wall", w.z0 - 20f, w.z0 + 80f, w.side, 5f);
            break;
        }
        if (Cliffs.JumpOf(0, 0, out var j) || Cliffs.JumpOf(1, 0, out j)) RopeView("jumpcliff", j.z - 10f, j.z + 4f, 1f, 7f);

        // ---- a snow gun and its hydrant
        foreach (var it in items)
            if (it.kind == PisteKit.Kind.Gun && it.size > 0f)
            {
                float s = Mathf.Sign(it.foot.x - Slope.Centre(it.foot.z));
                Vector3 eye = Ground(it.foot.x - s * 9f, it.foot.z - 12f, 1.7f);
                Spot("gun", new Vector3(Slope.Centre(it.foot.z - 20f), 0f, it.foot.z - 20f), eye, it.foot + Vector3.up * 1.8f);
                break;
            }

        // ---- the patrol hut and its toboggan
        foreach (var it in items)
            if (it.kind == PisteKit.Kind.Hut)
            {
                Vector3 front = it.aim;
                Vector3 eye = Ground(it.foot.x + front.x * 9f, it.foot.z + 7f, 1.7f);
                Spot("hut", new Vector3(Slope.Centre(it.foot.z - 30f), 0f, it.foot.z - 30f), eye, it.foot + Vector3.up * 1.0f);
                Debug.Log($"PISTESHOT hut at {it.foot}, front {it.aim}, plinth {it.size:0.00} m");
                break;
            }

        // ---- the trail signs, one of each
        var seen = new HashSet<TrailSigns.Symbol>();
        foreach (var s in TrailSigns.Signs)
        {
            if (!seen.Add(s.symbol)) continue;
            Vector3 f = TrailSigns.Foot(s);
            Vector3 eye = Ground(f.x - s.side * 3.5f, f.z - 6.5f, 1.6f);
            Spot($"sign_{s.symbol}", new Vector3(Slope.Centre(f.z - 30f), 0f, f.z - 30f), eye, f + Vector3.up * 1.7f, false);
        }
        // a sign of each symbol put up by the API (Place), side by side at the start, so all four show
        foreach (var (z, sym) in new[] { (100f, TrailSigns.Symbol.Green), (112f, TrailSigns.Symbol.Blue), (124f, TrailSigns.Symbol.Black), (136f, TrailSigns.Symbol.DoubleBlack) })
            TrailSigns.Place(z, sym, 1f);
        {
            Vector3 lo = Vector3.zero; int n = 0; float side = 1f;
            foreach (var s in TrailSigns.Signs) if (s.z > 95f && s.z < 141f) { lo += TrailSigns.Foot(s); n++; side = s.side; Debug.Log($"PISTESHOT api sign {s.symbol} at {TrailSigns.Foot(s)}"); }
            if (n > 0)
            {
                lo /= n;
                Vector3 eye = Ground(lo.x - side * 6f, lo.z - 24f, 1.8f);
                Spot("signs_api", new Vector3(Slope.Centre(70f), 0f, 70f), eye, lo + Vector3.up * 1.4f, false);
            }
        }

        // ---- a pole knocked over, and a rope ridden through
        Knock();

        Sheet("sheet_day.png", "ride_day.png", "ridge_day.png", "wall_day.png", "jumpcliff_day.png", "gun_day.png", "hut_day.png");
        Sheet("sheet_night.png", "ride_night.png", "ridge_night.png", "wall_night.png", "gun_night.png", "hut_night.png", "sign_DoubleBlack_night.png");
        Sheet("sheet_fog.png", "ride_fog.png", "ridge_fog.png", "wall_fog.png", "gun_fog.png", "hut_fog.png");
        Sheet("sheet_signs.png", "sign_Blue_day.png", "sign_Black_day.png", "sign_DoubleBlack_day.png", "signs_api_day.png", "knocked_pole_day.png");

        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; weather.Step(0f); dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static Vector3 Ground(float x, float z, float up) => new Vector3(x, Slope.DrawnHeight(x, z) + up, z);

    static void Prewarm()
    {
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm();
        // the hamlets (a piece a frame) and the lifts, as their Updates would (batchmode calls none)
        if (hamlets != null) { var hu = typeof(Hamlets).GetMethod("Update", NP); for (int i = 0; i < 120; i++) hu.Invoke(hamlets, null); }
        if (lifts != null) typeof(SkiLift).GetMethod("Update", NP).Invoke(lifts, null);
        lights?.Prewarm(); kit.Prewarm();
    }

    /// <summary>The rider stood at `riderAt` (so everything streams round there), the camera at eye looking at `at`, by day, at night, in fog.</summary>
    static void Spot(string name, Vector3 riderAt, Vector3 eye, Vector3 at, bool fog = true)
    {
        rider.ResetAt(riderAt.x, riderAt.z); rider.Begin();
        rider.Body.linearVelocity = Vector3.zero;
        cam.transform.position = eye;
        Prewarm();
        foreach (var (tag, choice, clock) in DayNightFog)
        {
            if (tag == "fog" && !fog) continue;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            Show(choice, clock, 1.5f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            Frame(0f); kit.Prewarm(); lights?.Prewarm();
            Shoot($"{name}_{tag}.png");
        }
        Debug.Log($"PISTESHOT {name}: eye {eye} at {at}");
    }

    /// <summary>A rope line's poles between z0 and z1 on side `side`: the camera inside its first, looking along it.</summary>
    static void RopeView(string name, float z0, float z1, float side, float inward)
    {
        var poles = new List<PisteKit.Item>();
        foreach (var it in PisteKit.Items)
            if ((it.kind == PisteKit.Kind.RopePole || it.kind == PisteKit.Kind.Closed) && it.foot.z > z0 && it.foot.z < z1 && Mathf.Sign(it.foot.x - Slope.Centre(it.foot.z)) == side)
                poles.Add(it);
        if (poles.Count < 2) { Debug.LogWarning($"PISTESHOT: no rope line for {name}"); return; }
        var a = poles[0]; var b = poles[Mathf.Min(poles.Count - 1, 4)];
        Vector3 along = b.foot - a.foot; along.y = 0f; along.Normalize();
        Vector3 inv = new Vector3(Slope.Centre(a.foot.z) - a.foot.x, 0f, 0f).normalized;
        Vector3 eye = a.foot - along * 6f + inv * inward + Vector3.back * (name == "jumpcliff" ? 9f : 0f); eye.y = Slope.DrawnHeight(eye.x, eye.z) + 1.7f;
        Spot(name, new Vector3(Slope.Centre(a.foot.z - 20f), 0f, a.foot.z - 20f), eye, b.foot + Vector3.up * 0.6f);
    }

    /// <summary>The nearest edge pole ahead knocked over by a rider riding through it, then the nearest rope ridden through.</summary>
    static void Knock()
    {
        var items = PisteKit.Items;
        int pole = -1;
        foreach (var it in items) if (it.kind == PisteKit.Kind.EdgePole && it.foot.z > 300f) { pole = it.id; break; }
        if (pole < 0) return;
        var p = items[pole];
        rider.ResetAt(p.foot.x, p.foot.z - 30f); rider.Begin();
        Prewarm();
        Breakable hit = null; float best = 9f;
        foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
        {
            float d = (b.transform.position - p.foot).magnitude;
            if (d < best) { best = d; hit = b; }
        }
        if (hit == null) { Debug.LogWarning("PISTESHOT: no holder found for the pole"); return; }
        hit.Break(p.foot + Vector3.up * 0.5f, new Vector3(0f, 0f, 9f));
        for (int i = 0; i < 60; i++) Breakable.Step(Dt);
        kit.Prewarm();
        float s = Mathf.Sign(p.foot.x - Slope.Centre(p.foot.z));
        Vector3 eye = Ground(p.foot.x - s * 5f, p.foot.z - 7f, 1.6f);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(p.foot + Vector3.up * 0.3f - eye));
        Show(1, 1.4f, 1f);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(p.foot + Vector3.up * 0.3f - eye));
        Frame(0f);
        Shoot("knocked_pole_day.png");
        Debug.Log($"PISTESHOT knocked pole {pole} at {p.foot}: down {hit.Down}");
        Breakable.RestoreAll();
        kit.Prewarm();
        Frame(0f);
        Shoot("knocked_pole_restored_day.png");
    }

    // ---------------------------------------------------------------- driving it (as LightsShot)

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
        Prewarm();
    }

    static void Frame(float dt)
    {
        TreeWind.Advance(dt, cam.transform.position);
        weather.Step(dt);
        aLate.Invoke(atmo, null);
        dayNight.ApplyNow();
    }

    static void Show(int choice, float clock, float warm)
    {
        Weather.TestChoice = choice; TimeOfDay.TestClock = clock;
        weather.Step(0f);
        var keep = (cam.transform.position, cam.transform.rotation);
        for (float t = 0f; t < warm; t += Dt)
        {
            cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
            Frame(Dt);
        }
        cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
        weather.Step(0f); dayNight.ApplyNow();
        lights?.Prewarm();
    }

    // ---------------------------------------------------------------- cost

    /// <summary>The kit's draw calls in this view (its renderers in the frustum, and those within the shadows' reach), and its triangles.</summary>
    static void Cost(string name)
    {
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        int draws = 0, shadows = 0; long tris = 0, allTris = 0; int meshes = 0;
        var mats = new HashSet<Material>();
        foreach (var r in kit.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            var mf = r.GetComponent<MeshFilter>();
            int t = mf != null && mf.sharedMesh != null ? (int)(mf.sharedMesh.GetIndexCount(0) / 3) : 0;
            allTris += t; meshes++;
            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
            draws++; tris += t; mats.Add(r.sharedMaterial);
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off && Vector3.Distance(r.bounds.ClosestPoint(cam.transform.position), cam.transform.position) < 80f) shadows++;
        }
        int colliders = 0, triggers = 0;
        foreach (var c in kit.GetComponentsInChildren<Collider>()) { if (c.isTrigger) triggers++; else colliders++; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var upd = typeof(PisteKit).GetMethod("Update", NP);
        for (int i = 0; i < 500; i++) upd.Invoke(kit, null);
        double cpu = sw.Elapsed.TotalMilliseconds / 500.0;
        Debug.Log($"PISTESHOT cost {name}: draws in view {draws} (+{shadows} into the shadow map), {mats.Count} materials, {tris} triangles in view; built {meshes} renderers, {allTris} triangles; colliders {colliders} solid, {triggers} trigger; PisteKit CPU {cpu:0.000} ms/frame (editor)");
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

    static void Sheet(string file, params string[] names)
    {
        int w = W / 2, h = H / 2;
        var sheet = new Texture2D(w * names.Length, h, TextureFormat.RGB24, false);
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
