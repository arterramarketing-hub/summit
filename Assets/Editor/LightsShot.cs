using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders the night-skiing floodlights (NightLights.cs) at phone size: riding by day, at dusk, at night clear, in
/// falling snow and in a storm; a rider in the air over a kicker in a beam (the reference shot); the gate from the
/// camp; a lift station; a hamlet; the hill from above; and logs what they cost (draw calls, triangles, the GPU
/// time of a frame with and without them, their CPU per frame).
/// Batch: -executeMethod LightsShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes into $LIGHTS_OUT (or LightsShots/ in the project folder).
/// </summary>
public static class LightsShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float Dt = 1f / 30f;
    static SummitGame game; static Camera cam; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Camp camp; static Atmosphere atmo; static Weather weather; static ChaseCamera chase; static SnowFX fx;
    static NightLights lights;
    static MethodInfo tUpd, fUpd, late, aLate;
    static string outDir;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("LIGHTS_OUT") ?? "LightsShots";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = game.rider; body = game.body; terrain = game.terrain; props = game.props;
        dayNight = game.dayNight; camp = game.camp; fx = Get<SnowFX>("fx"); weather = Get<Weather>("weather");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); atmo = Object.FindFirstObjectByType<Atmosphere>();
        lights = NightLights.Instance;
        if (lights == null || weather == null) { Debug.LogError("LIGHTSSHOT: no NightLights or Weather"); TestHarness.Exit(1); return; }
        var lamps = NightLights.Lamps;
        var byKind = new Dictionary<NightLights.Kind, int>();
        foreach (var l in lamps) { byKind.TryGetValue(l.kind, out int n); byKind[l.kind] = n + 1; }
        var kinds = new System.Text.StringBuilder();
        foreach (var kv in byKind) kinds.Append($" {kv.Key}={kv.Value}");
        var fails = new System.Text.StringBuilder();
        foreach (var kv in NightLights.Fails) fails.Append($" {kv.Key}={kv.Value}");
        Debug.Log($"LIGHTSSHOT layout: seed {Slope.Seed}, {lamps.Length} lamps:{kinds}; poles not put up:{fails}");
        var sw0 = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 2000; i++) NightLights.Taken(Slope.Centre(i * 2f) + NightLights.PoleOff, i * 2f);
        Debug.Log($"LIGHTSSHOT Taken: {sw0.Elapsed.TotalMilliseconds / 2000.0 * 1000.0:0.00} us a call");

        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        cam.aspect = W / (float)H;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = Dt;
        for (int i = 0; i < 6; i++) Shoot(null);

        // the bins' build time on this machine (the worker's share: one bin, all its lamps)
        var swb = System.Diagnostics.Stopwatch.StartNew();
        lights.Prewarm();
        Debug.Log($"LIGHTSSHOT prewarm {swb.Elapsed.TotalMilliseconds:0} ms: {lights.Stats()}");

        // ---- riding: day, dusk, night clear, night snow, night storm
        Ride(8f);
        if (System.Environment.GetEnvironmentVariable("LIGHTS_ONLY") == "evening") { Evening(); return; }
        var rides = new (string name, int choice, float clock)[]
            { ("day", 1, 1.4f), ("dusk", 1, 2.72f), ("night_clear", 1, 3.4f), ("night_snow", 3, 3.4f), ("night_storm", 4, 3.4f) };
        foreach (var (name, choice, clock) in rides)
        {
            Show(choice, clock, 3f);
            Shoot($"ride_{name}.png");
            Debug.Log($"LIGHTSSHOT ride {name}: rider {rider.transform.position} {lights.Stats()}");
        }
        Sheet("sheet_ride.png", System.Array.ConvertAll(rides, r => $"ride_{r.name}.png"));

        // ---- what it costs: the night storm with the lights and without
        Show(4, 3.4f, 2f);
        Cost("night_storm");
        Show(1, 3.4f, 2f);
        Cost("night_clear");
        Show(1, 1.4f, 2f);
        Cost("day");

        // ---- the hill from above at night: islands of light
        foreach (var (name, choice) in new[] { ("clear", 1), ("storm", 4) })
        {
            Show(choice, 3.4f, 2f);
            var keep = (cam.transform.position, cam.transform.rotation);
            Vector3 r = rider.transform.position;
            cam.transform.position = new Vector3(Slope.Centre(r.z - 40f), Slope.Height(Slope.Centre(r.z - 40f), r.z - 40f) + 45f, r.z - 40f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.42f, 1f));
            Frame(0f); lights.Prewarm();
            Shoot($"hill_night_{name}.png");
            cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
        }

        // ---- the reference: a rider in the air over a kicker, in a floodlight's beam
        foreach (var (name, choice) in new[] { ("snow", 3), ("storm", 4), ("clear", 1) })
            if (!JumpShot(name, choice)) Debug.LogWarning("LIGHTSSHOT: no lit kicker found for the jump shot");

        // ---- a lift station and a hamlet, at night
        Place("piste", NightLights.Kind.Piste, 3, 30f, 5f, 2);
        Place("station", NightLights.Kind.Station, 3, 24f, 4f);
        Place("tower", NightLights.Kind.Tower, 1, 26f, 4f);
        Place("hamlet", NightLights.Kind.Hamlet, 1, 30f, 5f);
        Place("park", NightLights.Kind.Feature, 4, 26f, 4f);
        Sheet("sheet_places.png", "place_piste.png", "place_station.png", "place_tower.png", "place_hamlet.png", "place_park.png");
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;

        // ---- the camp and the gate at dusk and night
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = Dt;
        var from = camp.ChaletPoint(9f, 0f, 22f); var to = camp.ChaletPoint(0f, 0f, 0f);
        camp.TeleportTo(from, Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg);
        SettleCamp();
        foreach (var (name, clock) in new[] { ("dusk", 2.72f), ("night", 3.4f) })
        {
            Show(1, clock, 2f, true, true);
            lights.Prewarm();
            Shoot($"camp_{name}.png");
            // turned round toward the gate and the run
            var keep = (cam.transform.position, cam.transform.rotation);
            // from behind the gate, looking down the pad and the start between its two floods
            float gx = Slope.Centre(Slope.GateZ);
            Vector3 eye = new Vector3(gx + 2f, 0f, Slope.GateZ - 14f);
            eye.y = Slope.Height(eye.x, eye.z) + 2.2f;
            Vector3 at = new Vector3(gx, 0f, Slope.GateZ + 26f);
            at.y = Slope.Height(at.x, at.z) + 2f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            Frame(0f);
            Shoot($"gate_{name}.png");
            cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
        }
        ChaseCamera.TestDt = 0f;

        Weather.TestChoice = 1; TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; weather.Step(0f); dayNight.ApplyNow();
        Object.DestroyImmediate(weather.gameObject);
        Object.DestroyImmediate(lights.gameObject);
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    // ---------------------------------------------------------------- shots

    /// <summary>
    /// The evening only (LIGHTS_ONLY=evening): riding at SUMMIT_TIME=2's moment (golden), at dusk (pink) and at night,
    /// clear and in falling snow; the camp and the gate at the same times. For before/after of the lamps' evening.
    /// </summary>
    static void Evening()
    {
        var times = new (string name, float clock)[] { ("evening", 2f + TimeOfDay.HeldAt), ("dusk", 2.72f), ("night", 3.4f) };
        foreach (var (name, clock) in times)
            foreach (var (wn, choice) in new[] { ("clear", 1), ("snow", 3) })
            {
                Show(choice, clock, 3f);
                Shoot($"ride_{name}_{wn}.png");
                Debug.Log($"LIGHTSSHOT evening ride {name} {wn}: darkness {dayNight.Darkness:0.00} {lights.Stats()}");
            }
        Sheet("sheet_ride_clear.png", "ride_evening_clear.png", "ride_dusk_clear.png", "ride_night_clear.png");
        Sheet("sheet_ride_snow.png", "ride_evening_snow.png", "ride_dusk_snow.png", "ride_night_snow.png");
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = Dt;
        var from = camp.ChaletPoint(9f, 0f, 22f); var to = camp.ChaletPoint(0f, 0f, 0f);
        camp.TeleportTo(from, Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg);
        SettleCamp();
        foreach (var (name, clock) in times)
        {
            Show(1, clock, 2f, true, true);
            lights.Prewarm();
            Shoot($"camp_{name}.png");
            var keep = (cam.transform.position, cam.transform.rotation);
            float gx = Slope.Centre(Slope.GateZ);
            Vector3 eye = new Vector3(gx + 2f, 0f, Slope.GateZ - 14f);
            eye.y = Slope.Height(eye.x, eye.z) + 2.2f;
            Vector3 at = new Vector3(gx, 0f, Slope.GateZ + 26f);
            at.y = Slope.Height(at.x, at.z) + 2f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            Frame(0f);
            Shoot($"gate_{name}.png");
            Debug.Log($"LIGHTSSHOT evening camp {name}: darkness {dayNight.Darkness:0.00} {lights.Stats()}");
            cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
        }
        Sheet("sheet_camp.png", "camp_evening.png", "camp_dusk.png", "camp_night.png");
        Sheet("sheet_gate.png", "gate_evening.png", "gate_dusk.png", "gate_night.png");
        ChaseCamera.TestDt = 0f;
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; weather.Step(0f); dayNight.ApplyNow();
        Object.DestroyImmediate(weather.gameObject);
        Object.DestroyImmediate(lights.gameObject);
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>The nearest lit kicker ahead: the rider set at its in-run at speed, ridden until in the air past the lip.</summary>
    static bool JumpShot(string name, int choice)
    {
        NightLights.Lamp jump = default; bool found = false;
        float z0 = rider.transform.position.z + 60f;
        foreach (var l in NightLights.Lamps)
            if (l.kind == NightLights.Kind.Jump && l.foot.z > z0) { jump = l; found = true; break; }
        if (!found) return false;
        float lipZ = jump.foot.z - 15f;
        float cx = Slope.Centre(lipZ);
        // where along the piste the kicker is (its lip is the top of Slope.Kicker)
        float best = 0f, bx = cx, bz = lipZ;
        for (float z = lipZ - 22f; z < lipZ + 6f; z += 0.5f)
            for (float x = cx - 18f; x <= cx + 18f; x += 1f)
            {
                float k = Slope.Kicker(x, z);
                if (k > best + 0.01f) { best = k; bx = x; bz = z; }
            }
        Debug.Log($"LIGHTSSHOT jump {name}: lamp {jump.id} at {jump.head}, kicker lip {best:0.0} m at ({bx:0.0}, {bz:0.0})");
        Show(choice, 3.4f, 0.5f);
        float startZ = bz - 40f;
        rider.ResetAt(bx, startZ); rider.Begin();
        var down = new Vector3(0f, -0.58f, 0.81f).normalized;
        rider.Body.linearVelocity = down * 19f;
        terrain.Prewarm(); props.Prewarm(); chase.Snap();
        lights.Prewarm();
        bool shot = false; float peak = 0f;
        for (float t = 0f; t < 6f; t += Dt)
        {
            Ride(Dt);
            float h = rider.transform.position.y - Slope.Height(rider.transform.position.x, rider.transform.position.z);
            if (rider.Airborne && rider.transform.position.z > bz + 1f) peak = Mathf.Max(peak, h);
            if (rider.Airborne && rider.transform.position.z > bz + 3f && h > 2.2f)
            {
                for (int i = 0; i < 6; i++) Frame(0f);   // (the air filled at this moment, the rider held)
                lights.Prewarm();
                Shoot($"jump_{name}.png");
                // framed like the reference: low beside the in-run across from the lamp, looking up at the rider in the beam
                var keep = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
                float side = Mathf.Sign(jump.foot.x - bx);
                Vector3 rp = rider.transform.position;
                Vector3 eye = new Vector3(bx - side * 9f, 0f, bz - 6f);
                eye.y = Slope.Height(eye.x, eye.z) + 1.4f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(rp + Vector3.up * 1.5f - eye));
                cam.fieldOfView = 62f;
                for (int i = 0; i < 4; i++) Frame(0f);
                Shoot($"jump_{name}_side.png");
                cam.transform.SetPositionAndRotation(keep.position, keep.rotation); cam.fieldOfView = keep.fieldOfView;
                Debug.Log($"LIGHTSSHOT jump {name} shot: rider {rider.transform.position} {h:0.0} m up, speed {rider.Speed * 3.6f:0} km/h, {lights.Stats()}");
                shot = true;
                break;
            }
        }
        if (!shot) Debug.LogWarning($"LIGHTSSHOT jump {name}: never got {2.2f} m up (peak {peak:0.0} m)");
        return shot;
    }

    /// <summary>Where a lamp's beam meets the snow.</summary>
    static Vector3 Hit(NightLights.Lamp l)
    {
        for (float d = 1f; d < l.range; d += 0.5f)
        {
            Vector3 p = l.head + l.dir * d;
            if (p.y < Slope.Height(p.x, p.z)) return p;
        }
        return l.head + l.dir * l.range * 0.5f;
    }

    /// <summary>A camera across a lamp's pool from it, looking back at the lamp: the pole, the pool and the beam.</summary>
    static void Place(string name, NightLights.Kind kind, int choice, float back, float up, int nth = 0)
    {
        NightLights.Lamp lamp = default; bool found = false;
        foreach (var l in NightLights.Lamps)
            if (l.kind == kind && l.head.z > 20f && nth-- <= 0) { lamp = l; found = true; break; }
        if (!found) { Debug.LogWarning($"LIGHTSSHOT: no {kind} lamp"); return; }
        Vector3 hit = Hit(lamp);
        Vector3 away = hit - lamp.foot; away.y = 0f;
        if (away.sqrMagnitude < 1f) away = Vector3.back;
        away = away.normalized;
        // a rider stood in the pool, the camera beyond them
        Vector3 stand = hit - away * 2f;
        rider.ResetAt(stand.x, stand.z); rider.Begin();
        rider.Body.linearVelocity = Vector3.zero;
        Vector3 eye = hit + away * back;
        eye.y = Slope.Height(eye.x, eye.z) + up;
        cam.transform.position = eye;
        terrain.Prewarm(); props.Prewarm();
        Show(choice, 3.4f, 1.5f);
        Vector3 look = Vector3.Lerp(hit, lamp.head, 0.45f);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        Frame(0f); lights.Prewarm(); Frame(0f);
        Shoot($"place_{name}.png");
        Debug.Log($"LIGHTSSHOT place {name}: lamp {lamp.id} {kind} at {lamp.head} aim hits {hit}, power {lamp.power:0} range {lamp.range:0}; {lights.Stats()}");
    }

    // ---------------------------------------------------------------- driving it (as WeatherShot)

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
        foreach (var ps in lights.GetComponentsInChildren<ParticleSystem>()) yield return ps;
    }

    static void Show(int choice, float clock, float warm, bool fresh = true, bool still = false)
    {
        Weather.TestChoice = choice; TimeOfDay.TestClock = clock;
        weather.Step(0f);
        if (fresh) foreach (var ps in Systems()) ps.Clear();
        var keep = (cam.transform.position, cam.transform.rotation);
        for (float t = 0f; t < warm; t += Dt)
        {
            if (still) camp.CameraStep(0f);
            cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
            Frame(Dt);
        }
        cam.transform.SetPositionAndRotation(keep.position, keep.rotation);
        weather.Step(0f); dayNight.ApplyNow();
        lights.Prewarm();
        fx.Sprinkle(Mathf.RoundToInt(400 * Mathf.Clamp01(fx.glintRate)));
    }

    static void SettleCamp()
    {
        for (int i = 0; i < 40; i++) { camp.Walk(Vector2.zero, Dt); body.Apply(Dt); camp.CameraStep(Dt); tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); aLate.Invoke(atmo, null); }
    }

    // ---------------------------------------------------------------- cost

    /// <summary>
    /// What the lights cost in this view: draw calls added (their renderers in view, and the shadow pass for the poles),
    /// the fragments each layer of them draws (counted: every renderer of a kind drawn alone into a half-float target
    /// with an additive counter; no depth test, so an upper bound), the glare (worked out: its quads are made in the
    /// shader), and their CPU per frame.
    /// </summary>
    static void Cost(string name)
    {
        var count = new Material(Shader.Find("Hidden/NightLightsCount"));
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf);
        var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        bool post = data != null && data.renderPostProcessing;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        var sb = new System.Text.StringBuilder();
        int draws = 0, shadowDraws = 0;
        var groups = new Dictionary<string, List<Renderer>>();
        foreach (var r in lights.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer && r.GetComponent<ParticleSystem>().particleCount == 0) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
            draws++;
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off && Vector3.Distance(r.bounds.ClosestPoint(cam.transform.position), cam.transform.position) < 80f) shadowDraws++;
            string g = r.name;
            if (!groups.TryGetValue(g, out var list)) groups[g] = list = new List<Renderer>();
            list.Add(r);
        }
        // the lit snowfall (the game's own snow, its shader swapped): its fragments, for scale
        foreach (var ps in atmo.GetComponentsInChildren<ParticleSystemRenderer>()) if (ps.name == "Snowfall") { if (!groups.ContainsKey("Snowfall (game's)")) groups["Snowfall (game's)"] = new List<Renderer>(); groups["Snowfall (game's)"].Add(ps); }
        var px = new Texture2D(W, H, TextureFormat.RGBAHalf, false);
        int oldMask = cam.cullingMask; var oldClear = cam.clearFlags; var oldBg = cam.backgroundColor;
        if (data != null) data.renderPostProcessing = false;
        foreach (var kv in groups)
        {
            if (kv.Key == "Glare") continue;
            var keepLayer = new List<int>(); var keepMat = new List<Material[]>();
            foreach (var r in kv.Value) { keepLayer.Add(r.gameObject.layer); keepMat.Add(r.sharedMaterials); r.gameObject.layer = 31; r.sharedMaterial = count; }
            cam.cullingMask = 1 << 31; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, W, H), 0, 0); px.Apply(); RenderTexture.active = null;
            double frags = 0; int covered = 0;
            var c = px.GetPixels();
            foreach (var p in c) { frags += p.r; if (p.r > 0.5f) covered++; }
            for (int i = 0; i < kv.Value.Count; i++) { kv.Value[i].gameObject.layer = keepLayer[i]; kv.Value[i].sharedMaterials = keepMat[i]; }
            sb.Append($" {kv.Key}: {frags / (W * H):0.00} screens of fragments ({covered * 100.0 / (W * H):0}% of the screen);");
        }
        cam.cullingMask = oldMask; cam.clearFlags = oldClear; cam.backgroundColor = oldBg;
        if (data != null) data.renderPostProcessing = post;
        // the glare's quads, worked out as the shader makes them
        double glare = 0;
        float tanH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        foreach (var l in NightLights.Lamps)
        {
            Vector3 c = l.head + l.dir * 0.3f, v = cam.transform.InverseTransformPoint(c);
            if (v.z < 0.3f || Mathf.Abs(v.x) > v.z * tanH * cam.aspect + 4f || Mathf.Abs(v.y) > v.z * tanH + 4f) continue;
            float face = Mathf.Clamp01(Vector3.Dot(l.dir, (cam.transform.position - c).normalized));
            float size = (l.kind == NightLights.Kind.Jump ? 2.2f : l.kind == NightLights.Kind.Tower ? 1.4f : 1.8f) * (0.5f + 0.5f * face * face) * (1f + 0.4f * Mathf.Clamp01(lights.Haze));
            float pxs = Mathf.Max(size / (v.z * tanH) * H * 0.5f, 7f);
            glare += Mathf.Min(4f * pxs * pxs, W * H);
        }
        sb.Append($" Glare: {glare / (W * H):0.00} screens (worked out);");
        var onApplied = typeof(NightLights).GetMethod("OnApplied", NP);
        var args = new object[] { dayNight };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 500; i++) onApplied.Invoke(lights, args);
        double cpu = sw.Elapsed.TotalMilliseconds / 500.0;
        int realOn = 0; foreach (var l in lights.GetComponentsInChildren<Light>()) if (l.enabled) realOn++;
        Debug.Log($"LIGHTSSHOT cost {name}: draws added {draws} (+{shadowDraws} into the shadow map), real spot lights {realOn};{sb} NightLights CPU {cpu:0.000} ms/frame (editor); {lights.Stats()}");
        rt.Release(); Object.DestroyImmediate(px); Object.DestroyImmediate(count);
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
