using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders frames of the game mid-run to PNGs without a device, for checking visuals.
/// Run: Unity -batchmode -projectPath . -executeMethod RenderShot.Run -logFile shot.log
/// (no -nographics: it needs the GPU). Writes RenderShot*.png in the project folder.
/// </summary>
public static class RenderShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures();
        SummitImport.EnsureSky();
        SummitImport.EnsureBackdrop();
        SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        rider.input = null;
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();

        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var fx = Object.FindFirstObjectByType<SnowFX>();
        // (name, z to start from, seconds to ride before the shot)
        var spots = new (string name, float z, float secs)[] {
            ("Start", Slope.StartZ, 0.6f), ("Run", Slope.StartZ, 9f),
            ("Park", Course.First + Course.ParkAt + 8f, 1.2f), ("Danger", Course.First + Course.DangerAt + 2f, 1.5f),
            ("Pipe", Course.First + Course.PipeAt + 50f, 1.5f), ("Carve", Course.First + 110f, 3.2f),
            ("Crash", Course.First + 30f, 1.35f), ("Crash2", Course.First + 30f, 2.4f) };
        Vector3 trackAt = Vector3.zero, trackDir = Vector3.forward;
        foreach (var sp in spots)
        {
            rider.ResetAt((sp.name == "Start" || sp.name == "Run" ? Slope.Centre(Slope.PadLip) : Slope.Centre(sp.z)) + (sp.name == "Park" ? -6f : 0f), sp.z);
            rider.Begin();
            if (sp.name != "Start" && sp.name != "Run") rider.Body.linearVelocity = Vector3.forward * 18f;
            float t = 0f;
            for (int i = 0; t < sp.secs; i++, t += Dt)
            {
                rider.Steer = sp.name == "Run" ? Mathf.Clamp(0.6f * Mathf.Sin(t * 1.2f) + (Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f)
                            : sp.name == "Carve" ? Mathf.Sin(t * 1.9f) * 0.9f : 0f;
                if (sp.name.StartsWith("Crash") && !rider.Crashed && t >= 0.8f) rider.DoCrash("test");
                rider.Tick(Dt);
                body.PhysicsStep();
                Physics.Simulate(Dt);
                if (i % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (i % 4 == 0) body.Apply(4 * Dt);
            }
            terrain.Prewarm(); props.Prewarm();
            if (dress != null) dress.Prewarm();
            body.Apply(Dt);
            chase.Snap();
            late.Invoke(chase, null);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.name == "Snowfall") ps.Simulate(4f, true, true);
                else if (ps.name == "Glints" && sp.name == "Run") { fx.Sprinkle(0); }
                else if (ps.name == "Bow" || ps.name == "Spray" || ps.name == "Wake") { ps.Simulate(0.5f, true, true); Debug.Log($"SHOT ps {ps.name} n={ps.particleCount} rate={ps.emission.rateOverTime.constant:0}"); }
            if (sp.name != "Crash") fx.Clear();
            var gl = fx.transform.Find("Glints").GetComponent<ParticleSystem>(); gl.Clear();
            fx.Sprinkle(260); gl.Simulate(0.1f, true, false, false);
            { var arr = new ParticleSystem.Particle[3]; int got = gl.GetParticles(arr); for (int q = 0; q < got; q++) Debug.Log($"SHOT glint p={arr[q].position} size={arr[q].GetCurrentSize(gl)} col={arr[q].GetCurrentColor(gl)} life={arr[q].remainingLifetime} cam={cam.transform.position}"); }
            Debug.Log($"SHOT glints n={gl.particleCount} playing={gl.isPlaying} mat={gl.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.name} rq={gl.GetComponent<ParticleSystemRenderer>().sharedMaterial.renderQueue}");
            if (sp.name == "Run")
            {
                fx.Landing(rider.transform.position + Vector3.down * Rider.R, Slope.Normal(rider.transform.position.x, rider.transform.position.z), 12f, rider.Velocity);
                foreach (var nm in new[] { "ImpactPuffs", "Chunks" }) fx.transform.Find(nm).GetComponent<ParticleSystem>().Simulate(0.25f, true, false, false);
            }
            if (sp.name == "Crash")
            {
                foreach (var nm in new[] { "ImpactPuffs", "Chunks", "TreeMist" }) fx.transform.Find(nm).GetComponent<ParticleSystem>().Simulate(0.2f, true, false, false);
            }
            if (sp.name == "Carve")
            {
                // a tree shaken by a hit, a few metres ahead to the side
                Vector3 tp = rider.transform.position + new Vector3(4f, 0f, 9f);
                fx.TreeShake(tp, 12f);
                foreach (var nm in new[] { "TreeSnow", "TreeMist" }) { var ps2 = fx.transform.Find(nm).GetComponent<ParticleSystem>(); ps2.Simulate(0.5f, true, false, false); Debug.Log($"SHOT {nm} n={ps2.particleCount}"); }
            }
            // the forest draws itself in Update; do it by hand for the shot
            typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
            if (sp.name == "Start") Shoot(cam, "RenderShotWarmup.png");   // the first render comes out unlit
            // batchmode renders do not re-skin meshes, so bake the skinned meshes to see the real pose
            var baked = new System.Collections.Generic.List<GameObject>();
            var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            foreach (var sk in skins)
            {
                var m = new Mesh(); sk.BakeMesh(m);
                var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
                g.GetComponent<MeshFilter>().sharedMesh = m;
                g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
                g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                g.transform.localScale = Vector3.one;   // BakeMesh already applies the scale
                sk.enabled = false; baked.Add(g);
            }
            Shoot(cam, "RenderShot" + sp.name + ".png");
            foreach (var g in baked) Object.DestroyImmediate(g);
            foreach (var sk in skins) sk.enabled = true;
            if (sp.name.StartsWith("Crash"))
                foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                    Debug.Log($"SHOT skin {sk.name} bounds={sk.bounds.center} size={sk.bounds.size} root={(sk.rootBone ? sk.rootBone.name + sk.rootBone.position : "-")} model={sk.transform.root.name} {sk.transform.root.position} scale={sk.transform.lossyScale}");
            if (sp.name.StartsWith("Crash")) foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (!rb.isKinematic) Debug.Log($"SHOT body {rb.name} pos={rb.position} v={rb.linearVelocity.magnitude:0.0}");
            if (sp.name == "Carve") { var bp = rider.transform.position; trackDir = new Vector3(rider.Velocity.x, 0f, rider.Velocity.z).normalized; trackAt = bp - trackDir * 7f; trackAt.y = Slope.DrawnHeight(trackAt.x, trackAt.z); }
            Debug.Log($"SHOT {sp.name} speed={rider.Speed * 3.6f:0} km/h air={rider.Airborne} grind={rider.Grinding} heading={rider.Heading:0.00} pos={rider.transform.position} cam={cam.transform.position} camfwd={cam.transform.forward}");
        }

        // the snow tube, riding down the open mountain
        {
            rider.Tube = true; body.SetTube(true);
            rider.ResetAt(Slope.Centre(400f), 400f); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 14f;
            terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
            for (int i = 0; i < 120 * 2; i++) { rider.Steer = 0.4f * Mathf.Sin(i * Dt); rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); if (i % 4 == 0) body.Apply(4 * Dt); }
            body.Apply(Dt);
            var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            var baked = new System.Collections.Generic.List<GameObject>();
            foreach (var sk in skins) { var m = new Mesh(); sk.BakeMesh(m); var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer)); g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; baked.Add(g); }
            chase.enabled = true; chase.Snap(); late.Invoke(chase, null);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
            Shoot(cam, "RenderShotTube.png");
            var c0 = rider.transform.position + Vector3.up * 0.6f;
            cam.transform.SetPositionAndRotation(c0 + new Vector3(2.6f, 0.9f, 2.2f), Quaternion.LookRotation(c0 - (c0 + new Vector3(2.6f, 0.9f, 2.2f))));
            Shoot(cam, "RenderShotTubeClose.png");
            foreach (var g in baked) Object.DestroyImmediate(g);
            foreach (var sk in skins) sk.enabled = true;
            rider.Tube = false; body.SetTube(false);
        }
        // a fall into a crevasse: ride straight into one, let the body drop, then the camera looks down after it
        {
            Course.WidestCrack(out var cv, out var spot);
            rider.ResetAt(spot.x, spot.z - cv.half - 1f); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 3f;
            terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
            Debug.Log($"SHOT crevstart {cv.kind} #{cv.id} half={cv.half:0.0} at={rider.transform.position}");
            for (int i = 0; i < 120 * 2.5f && body.RagdollBelowSnow < 9f; i++)
            {
                rider.Steer = 0f; rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); if (i % 4 == 0) body.Apply(4 * Dt);
                if (i % 30 == 0) Debug.Log($"SHOT crevstep t={i * Dt:0.00} crashed={rider.Crashed} below={body.RagdollBelowSnow:0.0} pos={rider.transform.position} rag={body.RagdollCentre}");
            }
            Debug.Log($"SHOT crevfall crashed={rider.Crashed} below={body.RagdollBelowSnow:0.0} centre={body.RagdollCentre}");
            chase.enabled = true; chase.Snap(); late.Invoke(chase, null);
            chase.peek = 1f; chase.peekAt = body.RagdollCentre;
            late.Invoke(chase, null);
            typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
            Shoot(cam, "RenderShotCrevFall.png");
            chase.peek = 0f;
        }
        // back up after the crash (bandages on), then close-ups standing still, from the side and from behind
        rider.ResetAt(Slope.Centre(Slope.StartZ), Slope.StartZ);
        body.Apply(Dt);
        rider.Stop();
        rider.Heading = 0f;
        chase.enabled = false;
        body.Apply(0.5f);
        Vector3 c = rider.transform.position + Vector3.up * 0.6f;
        cam.fieldOfView = 40f;
        cam.transform.SetPositionAndRotation(c + new Vector3(4.5f, 0.6f, 0f), Quaternion.LookRotation(-Vector3.right + Vector3.down * 0.1f));
        Shoot(cam, "RenderShotSide.png");
        cam.transform.SetPositionAndRotation(c + new Vector3(0f, 0.8f, -4.5f), Quaternion.LookRotation(Vector3.forward + Vector3.down * 0.15f));
        Shoot(cam, "RenderShotBack.png");
        // a crevasse, from beside its rim
        {
            Course.WidestCrack(out var cv, out var spot);
            float ccx = spot.x, zm = spot.z;
            rider.ResetAt(ccx + 12f, zm - 6f); terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
            typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
            var eye = new Vector3(ccx + 3.5f, Slope.Surface(ccx + 3.5f, zm - cv.half - 4f) + 3.2f, zm - cv.half - 4f);
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(ccx, Slope.Surface(ccx, zm) - 5f, zm) - eye));
            Shoot(cam, "RenderShotCrevasse.png");
            eye = new Vector3(ccx + 1f, Slope.Surface(ccx, zm - 20f) + 6f, zm - 20f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(ccx, Slope.Surface(ccx, zm) - 8f, zm) - eye));
            Shoot(cam, "RenderShotCrevasse2.png");
        }
        // the track left by the carve run, close up from behind and above
        cam.fieldOfView = 50f;
        var cpos = trackAt - trackDir * 2.5f; cpos.y = Slope.DrawnHeight(cpos.x, cpos.z) + 1.5f;
        cam.transform.SetPositionAndRotation(cpos, Quaternion.LookRotation((trackAt + trackDir * 3f) - cpos));
        Shoot(cam, "RenderShotTrack.png");
        Physics.simulationMode = SimulationMode.FixedUpdate;   // do not leave the project on manual physics
        EditorApplication.Exit(0);
    }

    /// <summary>Probe the bear: log its clip bindings and render a few frames of its run from the side.</summary>
    public static void Bear()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        var wild = Object.FindFirstObjectByType<Wildlife>();
        var bear = (GameObject)typeof(Wildlife).GetField("bear", NP).GetValue(wild);
        var anim = (Animation)typeof(Wildlife).GetField("bearAnim", NP).GetValue(wild);
        Debug.Log($"BEAR go={bear != null} anim={anim != null} fwdYaw={typeof(Wildlife).GetField("bearFwdYaw", NP).GetValue(wild)}");
        foreach (var smr in bear.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Debug.Log($"BEAR smr {smr.name} shapes={smr.sharedMesh.blendShapeCount} bones={smr.bones.Length} verts={smr.sharedMesh.vertexCount} bounds={smr.bounds}");
        foreach (var t in bear.GetComponentsInChildren<Transform>(true)) Debug.Log($"BEAR node {t.name} lpos={t.localPosition} lrot={t.localEulerAngles} lscale={t.localScale}");
        if (anim != null)
            foreach (AnimationState s in anim)
            {
                Debug.Log($"BEAR clip {s.name} len={s.length} legacy={s.clip.legacy}");
                foreach (var b in AnimationUtility.GetCurveBindings(s.clip))
                {
                    var c = AnimationUtility.GetEditorCurve(s.clip, b);
                    Debug.Log($"BEAR  curve {b.path}/{b.propertyName} keys={c.length} first={(c.length > 0 ? c[0].value : 0)} last={(c.length > 0 ? c[c.length - 1].value : 0)}");
                }
            }
        {
            var sm0 = bear.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mesh = sm0.sharedMesh;
            var dv = new Vector3[mesh.vertexCount];
            for (int j = 0; j < mesh.blendShapeCount; j++)
            {
                mesh.GetBlendShapeFrameVertices(j, mesh.GetBlendShapeFrameCount(j) - 1, dv, null, null);
                float mx = 0f; foreach (var d in dv) mx = Mathf.Max(mx, d.magnitude);
                Debug.Log($"BEAR shape {j} {mesh.GetBlendShapeName(j)} frames={mesh.GetBlendShapeFrameCount(j)} w={mesh.GetBlendShapeFrameWeight(j, 0)} maxDelta={mx}");
            }
        }
        bear.SetActive(true);
        bear.transform.SetPositionAndRotation(new Vector3(0f, 500f, 0f), Quaternion.identity);
        var cam = Camera.main;
        cam.GetComponent<ChaseCamera>().enabled = false;
        cam.fieldOfView = 40f;
        for (int i = 0; i < 4; i++)
        {
            if (anim != null)
                foreach (AnimationState s in anim) { anim.Play(s.name); s.time = s.length * i / 4f; anim.Sample(); }
            var sm = bear.GetComponentInChildren<SkinnedMeshRenderer>();
            string w = ""; for (int j = 0; j < sm.sharedMesh.blendShapeCount; j++) w += sm.GetBlendShapeWeight(j).ToString("0") + " ";
            Debug.Log($"BEAR weights t{i}: {w}");
            // batchmode renders skip the skinning update, so bake this frame into a plain mesh to look at it
            var baked = new Mesh(); sm.BakeMesh(baked);
            var show = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            show.GetComponent<MeshFilter>().sharedMesh = baked;
            show.GetComponent<MeshRenderer>().sharedMaterial = sm.sharedMaterial;
            show.transform.SetPositionAndRotation(sm.transform.position, sm.transform.rotation);
            show.transform.localScale = sm.transform.lossyScale;
            sm.enabled = false;
            cam.transform.SetPositionAndRotation(new Vector3(6f, 500.9f, 0f), Quaternion.LookRotation(-Vector3.right));
            Shoot(cam, $"BearSide{i}.png");
            Object.DestroyImmediate(show); sm.enabled = true;
            continue;
            cam.transform.SetPositionAndRotation(new Vector3(6f, 500.9f, 0f), Quaternion.LookRotation(-Vector3.right));
            Shoot(cam, $"BearSide{i}.png");
        }
        cam.transform.SetPositionAndRotation(new Vector3(0f, 501f, 7f), Quaternion.LookRotation(-Vector3.forward));
        Shoot(cam, "BearFront.png");
        // up on its hind legs
        var rear = (Transform)typeof(Wildlife).GetField("bearRear", NP).GetValue(wild);
        rear.localRotation = Quaternion.Euler(70f, 0f, 0f);
        bear.transform.rotation = Quaternion.Euler(0f, (float)typeof(Wildlife).GetField("bearFwdYaw", NP).GetValue(wild) + 90f, 0f);
        cam.transform.SetPositionAndRotation(new Vector3(0f, 501.2f, -7f), Quaternion.LookRotation(Vector3.forward));
        Shoot(cam, "BearStand.png");
        EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
    }
}
