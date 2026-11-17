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
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
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
        // (name, z to start from, seconds to ride before the shot)
        var spots = new (string name, float z, float secs)[] {
            ("Start", Slope.StartZ, 0.6f), ("Run", Slope.StartZ, 9f),
            ("Park", Course.First + Course.ParkAt + 8f, 1.2f), ("Danger", Course.First + Course.DangerAt + 2f, 1.5f),
            ("Pipe", Course.First + Course.PipeAt + 50f, 1.5f) };
        foreach (var sp in spots)
        {
            rider.ResetAt((sp.name == "Start" || sp.name == "Run" ? Slope.Centre(Slope.PadLip) : Slope.Centre(sp.z)) + (sp.name == "Park" ? -6f : 0f), sp.z);
            rider.Begin();
            if (sp.name != "Start" && sp.name != "Run") rider.Body.linearVelocity = Vector3.forward * 18f;
            float t = 0f;
            for (int i = 0; t < sp.secs; i++, t += Dt)
            {
                rider.Steer = sp.name == "Run" ? Mathf.Clamp(0.6f * Mathf.Sin(t * 1.2f) + (Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f) : 0f;
                rider.Tick(Dt);
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
            // the forest draws itself in Update; do it by hand for the shot
            typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
            if (sp.name == "Start") Shoot(cam, "RenderShotWarmup.png");   // the first render comes out unlit
            Shoot(cam, "RenderShot" + sp.name + ".png");
            Debug.Log($"SHOT {sp.name} speed={rider.Speed * 3.6f:0} km/h air={rider.Airborne} grind={rider.Grinding} heading={rider.Heading:0.00} pos={rider.transform.position} cam={cam.transform.position} camfwd={cam.transform.forward}");
        }

        // close-ups of the rider standing still, from the side and from behind
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
        Physics.simulationMode = SimulationMode.FixedUpdate;   // do not leave the project on manual physics
        EditorApplication.Exit(0);
    }

    /// <summary>Probe the bear: log its clip bindings and render a few frames of its run from the side.</summary>
    public static void Bear()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
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
