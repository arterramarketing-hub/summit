using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders the avalanche catching a rider: the approach, the undercut, the knock-off, the carry and the burial.
/// It rides the start of a run as the game plays it, then puts the slide a little way behind and lets it come.
/// Run: Unity -batchmode -projectPath . -executeMethod AvalancheShot.Run -logFile shot.log
/// (no -nographics: it needs the GPU). Writes AvalancheShots/*.png (or into $SUMMIT_SHOTS).
/// </summary>
public static class AvalancheShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        try { Shoot(); }
        catch (System.Exception e) { Debug.LogError("AVSHOT failed: " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    static void Shoot()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureAvalanche();
        string dir = System.Environment.GetEnvironmentVariable("SUMMIT_SHOTS");
        if (string.IsNullOrEmpty(dir)) dir = "AvalancheShots";
        Directory.CreateDirectory(dir);

        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        Random.InitState(1234);   // the same mountain every time
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = game.rider;
        var body = game.body;
        var terrain = game.terrain;
        var props = game.props;
        var slide = Get<Avalanche>("slide");
        var view = Get<AvalancheView>("slideView");
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var fUpd = typeof(Forest).GetMethod("Update", NP);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var dUpd = dress != null ? typeof(CourseDressing).GetMethod("Update", NP) : null;
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 1f / 60f;
        const float dt = 1f / 60f;

        // (label, when): the moment of each picture
        const Avalanche.Stage Ch = Avalanche.Stage.Chasing, Un = Avalanche.Stage.Undercut, Ca = Avalanche.Stage.Carried, Bu = Avalanche.Stage.Buried;
        var shots = new (string name, Avalanche.Stage stage, System.Func<bool> when)[]
        {
            ("1_approach_28m", Ch, () => slide.Gap(rider) <= 28f),
            ("2_approach_14m", Ch, () => slide.Gap(rider) <= 14f),
            ("3_approach_5m", Ch, () => slide.Gap(rider) <= 5f),
            ("4_undercut_a", Un, () => slide.Phase == Un && slide.PhaseTime >= 0.5f),
            ("5_undercut_b", Un, () => slide.Phase == Un && slide.PhaseTime >= 1.35f),
            ("6_knockoff", Ca, () => slide.Phase == Ca && slide.PhaseTime >= 0.5f),
            ("7_carried", Ca, () => slide.Phase == Ca && slide.PhaseTime >= 1.3f),
            ("8_burying", Ca, () => slide.Phase == Ca && slide.Cover >= 0.55f),
            ("9_buried", Bu, () => slide.Phase == Bu && slide.PhaseTime >= 0.8f),
            ("10_buried_hold", Bu, () => slide.Phase == Bu && slide.PhaseTime >= 2.4f),
        };
        var side = new GameObject("SideCam").AddComponent<Camera>();
        side.CopyFrom(cam); side.fieldOfView = 60f; side.enabled = false; side.farClipPlane = 2300f;
        var viewCam = typeof(AvalancheView).GetField("cam", NP);
        int next = 0;
        bool forced = false, warmed = false, early = false, released = false;
        var sw = new System.Diagnostics.Stopwatch(); double viewMs = 0; int viewFrames = 0;
        for (float t = 0f; next < shots.Length && t < 70f; t += dt)
        {
            if (!rider.Crashed)
            {
                // ride down the middle with a gentle weave, as a player would
                var rp = rider.transform.position;
                rider.Steer = Mathf.Clamp(0.4f * Mathf.Sin(t * 1.1f) + (Slope.Centre(rider.Z + 12f) - rp.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);
            }
            for (int k = 0; k < 2; k++)
            {
                // once the slide is set behind, the rider makes a mistake and scrubs off speed (skidding, a bad
                // line) until it arrives. A clean, steady rider pulls away from it by design (it cruises a little
                // under your pace), so without this the catch depends on the course slowing the rider down
                if (forced && !slide.Caught && !rider.Crashed && !rider.Airborne && rider.Body.linearVelocity.magnitude > 11f)
                    rider.Body.linearVelocity = rider.Body.linearVelocity.normalized * 11f;
                rider.Tick(dt / 2f); body.PhysicsStep(); Physics.Simulate(dt / 2f);
            }
            if (!released && slide.Released) { released = true; Debug.Log($"AVSHOT released at t={t:0.00} riderZ={rider.Z:0.0} slideZ={slide.Z:0.0}"); }
            if (!early && t > 1.5f)
            {
                // dropping in: the slope above has not let go yet, so nothing of it may be drawn
                early = true;
                Render(cam, Path.Combine(dir, "Avalanche0_dropin.png"));
                int drawn = 0; foreach (var rr in view.GetComponentsInChildren<Renderer>()) if (rr.enabled && (!(rr is ParticleSystemRenderer) || rr.GetComponent<ParticleSystem>().particleCount > 0)) drawn++;
                Debug.Log($"AVSHOT dropin t={t:0.00} released={slide.Released} riderZ={rider.Z:0.0} drawn={drawn}");
            }
            if (!forced && rider.Z > 75f && !rider.Crashed)
            {
                // the slide, put not far behind and closing at its creep
                forced = true;
                slide.ForceGap(rider, 32f, Mathf.Max(0f, rider.Vz) + 6f);
                Debug.Log($"AVSHOT forced at t={t:0.00} riderSpeed={rider.Speed:0.0}");
            }
            slide.Step(dt, rider, body);
            if (slide.Phase == Avalanche.Stage.Undercut && Mathf.Repeat(slide.PhaseTime, 0.25f) < dt)
                Debug.Log($"AVSHOT undercut pt={slide.PhaseTime:0.00} rider={rider.transform.position} v={rider.Body.linearVelocity} lift={rider.Lift:0.00} depth={slide.Depth(rider.transform.position.x, rider.transform.position.z):0.00} air={rider.Airborne}");
            tUpd.Invoke(terrain, null);
            fUpd.Invoke(props, null);
            if (dUpd != null) dUpd.Invoke(dress, null);
            body.Apply(dt);
            // what SummitGame.Update does for the camera
            chase.shake = slide.Shake;
            chase.hold = Mathf.MoveTowards(chase.hold, slide.Caught && slide.Cover > 0.2f ? 1f : 0f, dt * 0.55f);
            chase.holdAt = slide.BuryAt;
            late.Invoke(chase, null);
            sw.Restart(); view.Tick(dt); sw.Stop();
            if (forced) { viewMs += sw.Elapsed.TotalMilliseconds; viewFrames++; }
            aLate.Invoke(atmo, null);
            if (forced && !warmed) { warmed = true; Render(cam, Path.Combine(dir, "_warmup.png")); }   // the first render has placeholder materials
            // a shot whose moment never comes (a rider already down skips the undercut) is skipped
            while (next < shots.Length && slide.Phase > shots[next].stage) { Debug.Log("AVSHOT skipped " + shots[next].name); next++; }
            if (next < shots.Length && shots[next].when())
            {
                var p = rider.transform.position;
                Render(cam, Path.Combine(dir, "Avalanche" + shots[next].name + ".png"));
                // and the whole slide from up high, downhill and off to one side, for its shape
                if (next <= 3 || next == 6)
                {
                    float fz = slide.Z + 70f, fx = p.x + 35f;
                    side.transform.position = new Vector3(fx, Slope.Height(fx, fz) + 30f, fz);
                    side.transform.LookAt(new Vector3(Slope.Centre(slide.Z), Slope.Height(p.x, slide.Z) + 14f, slide.Z - 8f));
                    viewCam.SetValue(view, side); view.Tick(0f);
                    Render(side, Path.Combine(dir, "Avalanche" + shots[next].name + "_over.png"));
                    if (next == 2)
                    {
                        // the shader's view of the billows: their normals as colours
                        var pm = view.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                        pm.SetFloat("_Debug", 1f); Render(side, Path.Combine(dir, "Avalanche" + shots[next].name + "_normals.png")); pm.SetFloat("_Debug", 0f);
                    }
                    viewCam.SetValue(view, cam); view.Tick(0f);
                }
                var mesh = view.GetComponentInChildren<MeshFilter>().sharedMesh;
                Debug.Log($"AVSHOT {shots[next].name} t={t:0.00} phase={slide.Phase} pt={slide.PhaseTime:0.00} gap={slide.Gap(rider):0.0} " +
                          $"slideSpeed={slide.Speed:0.0} riderSpeed={rider.Speed:0.0} crashed={rider.Crashed} cover={slide.Cover:0.00} " +
                          $"puffTris={mesh.GetIndexCount(0) / 3} whiteout={view.Whiteout:0.00} rider={p} body={body.RagdollCentre} bury={slide.BuryAt} cam={cam.transform.position}");
                next++;
            }
        }
        Debug.Log($"AVSHOT done: {next}/{shots.Length} shots, view tick avg {(viewFrames > 0 ? viewMs / viewFrames : 0):0.000} ms (editor CPU)");
        File.Delete(Path.Combine(dir, "_warmup.png"));
    }

    static void Render(Camera cam, string file)
    {
        // skinned meshes are not drawn by a manual render in batchmode: bake them for the shot
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new System.Collections.Generic.List<GameObject>();
        var hidden = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g); hidden.Add(sk);
        }
        int w = 590, h = 1278;
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
