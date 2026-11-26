using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Riding switch (Rider.Switch), drawn: off a jump cliff with a 180, on the board and on skis: half way round in the
/// air, just landed backwards, riding away switch, and carving switch; from behind (as the chase camera sees it) and
/// from the side, day and night. Run with the GPU (no -nographics, no -quit):
/// Unity -batchmode -projectPath . -executeMethod SwitchShot.Run -logFile shot.log   ($SWITCH_OUT: the folder)
/// </summary>
public static class SwitchShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camera cam; static DayNight dayNight; static Atmosphere atmo;
    static readonly List<System.Action> steps = new List<System.Action>();
    static int at;
    static Cliffs.Jump jump;

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("SWITCH_OUT") ?? "SwitchShots";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);
        TestPrefs.Pin();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        dayNight = F<DayNight>("dayNight"); atmo = Object.FindFirstObjectByType<Atmosphere>();
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        cam = Camera.main; cam.GetComponent<ChaseCamera>().enabled = false;
        TestBoot.NewMountain(game, 1000, terrain, props);
        var dressing = Object.FindFirstObjectByType<CourseDressing>();
        if (dressing != null) dressing.Rebuild();
        if (!Cliffs.JumpOf(0, 0, out jump) && !Cliffs.JumpOf(0, 1, out jump)) { Debug.LogError("SWITCH no jump cliff"); TestHarness.Exit(1); return; }
        steps.Clear(); at = 0;
        steps.Add(() => { TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); Ride(RideKind.Snowboard, 40); Shoot(Path.Combine(outDir, "switch-warmup.png")); });
        foreach (var kind in new[] { RideKind.Snowboard, RideKind.Skis })
        {
            // the moments: found by riding it once
            var moments = Moments(kind);
            foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
                foreach (var m in moments)
                {
                    if (when == "night" && m.name.StartsWith("1")) continue;
                    steps.Add(() =>
                    {
                        TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                        Ride(kind, m.step);
                        string k = kind == RideKind.Snowboard ? "board" : "skis";
                        Debug.Log($"SWITCH shot {k} {m.name} {when}: riding {(r.Switch ? "switch" : "forward")}, air={r.Airborne}, speed {r.Speed:0.0}, lean {r.Lean * Mathf.Rad2Deg:0} deg");
                        Vector3 rp = r.transform.position, v = r.Velocity; v.y = 0f;
                        Vector3 fwd = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward, side = new Vector3(fwd.z, 0f, -fwd.x);
                        Aim(rp - fwd * 6.5f + Vector3.up * 2.6f, rp + fwd * 3f + Vector3.down * 0.4f, 60f);
                        Shoot(Path.Combine(outDir, $"switch-{k}-{m.name}-{when}-behind.png"));
                        Aim(rp + side * 5.5f + fwd * 1.5f + Vector3.up * 1.2f, rp + Vector3.up * 0.6f, 55f);
                        Shoot(Path.Combine(outDir, $"switch-{k}-{m.name}-{when}-side.png"));
                    });
                }
        }
        steps.Add(() =>
        {
            body.SetRide(RideKind.Snowboard); r.SetRide(body.Ride);
            TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow(); TimeOfDay.TestClock = -1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            QualitySettings.SetQualityLevel(quality, false);
            TestPrefs.Restore();
        });
        EditorApplication.update += Tick;
    }

    static List<(string name, int step)> Moments(RideKind kind)
    {
        int tAir = -1, tLand = -1, total = Ride(kind, 2400, (i) =>
        {
            if (tAir < 0 && r.Airborne && r.TrickName != null && Mathf.Abs(r.SpinNow) > Mathf.PI * 0.45f) tAir = i;
            if (tAir >= 0 && tLand < 0 && !r.Airborne) tLand = i;
            return tLand >= 0 && i > tLand + 330;
        });
        Debug.Log($"SWITCH {kind}: half way round at step {tAir}, landed at {tLand}, riding {(r.Switch ? "switch" : "forward")}, crashed={r.Crashed}");
        return new List<(string, int)> { ("1-half-way-round", tAir), ("2-landed-backwards", tLand + 6), ("3-riding-switch", tLand + 120), ("4-carving-switch", tLand + 300) };
    }

    /// <summary>The ride from the top of the jump cliff's in-run with a 180 off it, to its `stepsTo`th step; a long
    /// carve to one side after landing, so a turn is seen. Returns the steps taken.</summary>
    static int Ride(RideKind kind, int stepsTo, System.Func<int, bool> stop = null)
    {
        body.SetRide(kind); r.SetRide(body.Ride);
        float z0 = jump.Start - 25f, x0 = Slope.Centre(z0);
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * 30f;
        terrain.Prewarm(); props.Prewarm();
        bool set = false; int landedAt = -1, i = 0;
        for (; i < stepsTo; i++)
        {
            float steer = Mathf.Clamp((Slope.Centre(r.Z + 12f) - r.transform.position.x) * 0.05f - r.Heading * 0.5f, -1f, 1f);
            if (landedAt >= 0 && i > landedAt + 200) steer = 0.8f;   // a carve, riding switch
            r.Steer = steer;
            if (r.Airborne && !set && r.Z > jump.Start) { set = true; r.ForceTrick(1, 0); }
            if (set && landedAt < 0 && !r.Airborne) landedAt = i;
            r.Tick(Time.fixedDeltaTime); body.PhysicsStep(); Physics.Simulate(Time.fixedDeltaTime);
            body.Apply(Time.fixedDeltaTime);
            if (i % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (stop != null && stop(i)) break;
        }
        return i;
    }

    static void Aim(Vector3 eye, Vector3 look, float fov)
    {
        eye.y = Mathf.Max(eye.y, Slope.DrawnHeight(eye.x, eye.z) + 1f);
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        typeof(SlopeStreamer).GetMethod("Update", NP)?.Invoke(terrain, null);
        typeof(Forest).GetMethod("Update", NP)?.Invoke(props, null);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear(true);
    }

    static void Tick()
    {
        if (at < steps.Count)
        {
            try { steps[at++](); }
            catch (System.Exception e) { Debug.LogError("SWITCH shots exception " + e); at = steps.Count; }
            return;
        }
        EditorApplication.update -= Tick;
        TestHarness.Exit(0);
    }

    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        typeof(Atmosphere).GetMethod("LateUpdate", NP)?.Invoke(atmo, null);
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
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
        int w = 886, h = 1920;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }
}
