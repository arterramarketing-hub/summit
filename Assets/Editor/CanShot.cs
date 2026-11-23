using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The Mountain Zoom can on the slope, at the phone's quality and resolution: up close from two sides (as you ride
/// up to it), and in the rider's own chase view with the can 12 m, 30 m and 55 m ahead (does it read from far off).
/// CANSHOT_LABEL: a label PNG to use instead of the game's (the before); CANSHOT_OUT: the folder; CANSHOT_TAG: a
/// prefix for the files.
/// Run: Unity -batchmode -projectPath . -executeMethod CanShot.Run -logFile can.log (no -nographics: it renders).
/// </summary>
public static class CanShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camera cam; static ChaseCamera chase; static MethodInfo late; static PowerUps power;
    static string outDir, tag;
    static int step;

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("CANSHOT_OUT") ?? "CanShots";
        tag = System.Environment.GetEnvironmentVariable("CANSHOT_TAG") ?? "";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        QualitySettings.SetQualityLevel(0, true);   // the phone's
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        rider = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        power = F<PowerUps>("power");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 4 * Dt;
        try
        {
            var lab = System.Environment.GetEnvironmentVariable("CANSHOT_LABEL");
            if (!string.IsNullOrEmpty(lab))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                t.LoadImage(File.ReadAllBytes(lab));
                var m = (Material)typeof(PowerUps).GetField("canLabel", NP).GetValue(power);
                m.SetTexture("_BaseMap", t); m.SetTexture("_EmissionMap", t);
                Debug.Log("CANSHOT label from " + lab);
            }
            Shots();
        }
        catch (System.Exception e) { Debug.LogError("CANSHOT exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static void Shots()
    {
        float z = Course.First + 110f;
        var make = typeof(PowerUps).GetMethod("Make", NP);
        var can = (GameObject)make.Invoke(power, new object[] { PowerUps.Kind.Soda });
        power.enabled = false;   // (this can is placed by hand; the pickups' own update would take it)

        // ridden up to: the rider coming down the fall line, the can ahead
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 16f;
        terrain.Prewarm(); props.Prewarm(); chase.Snap();
        step = 0;
        for (float t = 0f; t < 1.2f; t += Dt) Step();
        foreach (float ahead in new[] { 12f, 30f, 55f })
        {
            var rp = rider.transform.position;
            float cz = rp.z + ahead, cx = Slope.Centre(cz) + 1.5f;
            can.transform.SetPositionAndRotation(new Vector3(cx, Slope.DrawnHeight(cx, cz) + 1.25f, cz), Quaternion.Euler(0f, 200f, 0f));
            foreach (var ps in can.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(1.5f, true, true, false);
            Shoot($"{tag}can_chase_{ahead:0}m.png", 1179, 2556);
        }

        // up close, two sides of it, the snow round it
        chase.enabled = false;
        {
            float cz = z + 40f, cx = Slope.Centre(cz);
            var at = new Vector3(cx, Slope.DrawnHeight(cx, cz) + 1.25f, cz);
            can.transform.position = at;
            cam.fieldOfView = 40f;
            foreach (float yaw in new[] { 200f, 290f })
            {
                can.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                var from = at + new Vector3(0.35f, 0.75f, -2.6f);
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                Shoot($"{tag}can_close_{yaw:0}.png", 1179, 2556);
            }
            // from above, as you come over it: the lid, the rim and the tab
            can.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var top = at + new Vector3(0f, 1.9f, -1.5f);
            cam.transform.SetPositionAndRotation(top, Quaternion.LookRotation(at - top));
            Shoot($"{tag}can_top.png", 1179, 2556);
        }
        chase.enabled = true;
        Object.DestroyImmediate(can);
    }

    static void Step()
    {
        rider.Steer = Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);
        rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (step % 4 == 0) { body.Apply(4 * Dt); late.Invoke(chase, null); }
        step++;
    }

    static void Shoot(string file, int w, int h)
    {
        DynamicGI.UpdateEnvironment();
        // batchmode renders do not re-skin meshes: bake them for the rider's pose
        var baked = new System.Collections.Generic.List<GameObject>();
        var skins = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            skins.Add(sk);
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g);
        }
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        cam.Render(); cam.Render();   // (the first render of a new target can come out unlit)
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        Debug.Log("CANSHOT wrote " + file);
    }
}
