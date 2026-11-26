using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// A crash as a strip of pictures: the same throws as GlitchTest / RagdollAudit (mountain 1000, z 300, the same
/// random spin), at 18 m/s and at 30 m/s, each seen side-on from a camera that keeps the pelvis in the middle, at
/// 0.15, 0.35, 0.6, 0.9, 1.25, 1.7, 2.3 and 3 s after the hit. One row per crash, written as one PNG.
/// RAGSHOT_OUT: the file (default RagdollShot.png). Phone quality.
/// Run: Unity -batchmode -projectPath . -executeMethod RagdollShot.Run -logFile shot.log (no -nographics).
/// </summary>
public static class RagdollShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    const int Cell = 360;
    static readonly float[] At = { 0.15f, 0.35f, 0.6f, 0.9f, 1.25f, 1.7f, 2.3f, 3f };

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        string file = System.Environment.GetEnvironmentVariable("RAGSHOT_OUT") ?? "RagdollShot.png";
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        QualitySettings.SetQualityLevel(0, true);
        Weather.TestChoice = 1;
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        rider.input = null;
        var dressing = Object.FindFirstObjectByType<CourseDressing>();
        if (dressing != null) { var tgt = dressing.target; dressing.target = null; dressing.Rebuild(); dressing.target = tgt; }
        float[] speeds = { 18f, 30f };
        var strip = new Texture2D(Cell * At.Length, Cell * speeds.Length, TextureFormat.RGB24, false);
        try
        {
            TestBoot.NewMountain(game, 1000, terrain, props);
            chase.enabled = false;
            for (int row = 0; row < speeds.Length; row++)
            {
                float v0 = speeds[row], z = 300f;
                rider.Steer = 0f;
                Random.InitState(4242);
                rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
                rider.Body.linearVelocity = Vector3.forward * v0;
                terrain.Prewarm(); props.Prewarm();
                for (int i = 0; i < 30; i++) { rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); }
                rider.DoCrash("shot");
                var pelvis = new List<Rigidbody>(body.RagdollForTest.Bodies)[0];
                int k = 0; float t = 0f;
                for (; k < At.Length; t += Dt)
                {
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt);
                    if (t + Dt * 0.5f < At[k]) continue;
                    Vector3 c = pelvis.position;
                    Vector3 from = c + new Vector3(5.5f, 1.4f, 0.6f);
                    cam.fieldOfView = 42f;
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(c + Vector3.up * 0.2f - from));
                    var px = Shoot(cam);
                    strip.SetPixels(k * Cell, (speeds.Length - 1 - row) * Cell, Cell, Cell, px);
                    k++;
                }
                if (rider.Crashed) { rider.RequestUp = true; for (int i = 0; i < 600 && rider.Crashed; i++) { rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); if (rider.CanGetUp) rider.RequestUp = true; } }
            }
            strip.Apply();
            File.WriteAllBytes(file, strip.EncodeToPNG());
            Debug.Log("RAGSHOT wrote " + file);
        }
        catch (System.Exception e) { Debug.LogError("RAGSHOT exception " + e); }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static Color[] Shoot(Camera cam)
    {
        // batchmode renders don't re-skin meshes: bake them for the pose
        var baked = new List<GameObject>(); var skins = new List<SkinnedMeshRenderer>();
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
        DynamicGI.UpdateEnvironment();
        var rt = new RenderTexture(Cell, Cell, 24);
        cam.targetTexture = rt; cam.aspect = 1f;
        cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0); tex.Apply();
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        var px = tex.GetPixels(); Object.DestroyImmediate(tex);
        return px;
    }
}
