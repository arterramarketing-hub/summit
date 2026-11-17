using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>Renders the start of a run as the game plays it (ToReady, ToRiding, streaming each frame).</summary>
public static class StartShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var fUpd = typeof(Forest).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float[] shots = { 6.5f, 6.8f, 6.9f, 7.0f, 7.1f, 7.4f };
        ChaseCamera.TestDt = 1f / 60f;
        int next = 0;
        const float dt = 1f / 60f;
        for (float t = 0f; next < shots.Length; t += dt)
        {
            for (int k = 0; k < 2; k++) { rider.Tick(dt / 2f); body.PhysicsStep(); Physics.Simulate(dt / 2f); }
            tUpd.Invoke(terrain, null);
            fUpd.Invoke(props, null);
            body.Apply(dt);
            late.Invoke(chase, null);
            foreach (var tp in Object.FindObjectsByType<CautionTape>(FindObjectsSortMode.None)) typeof(CautionTape).GetMethod("LateUpdate", NP).Invoke(tp, null);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            if (t >= shots[next])
            {
                var p = rider.transform.position;
                var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
                var baked = new System.Collections.Generic.List<GameObject>();
                foreach (var sk in skins) { var m = new Mesh(); sk.BakeMesh(m); var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; baked.Add(g); }
                Shoot(cam, $"StartShot{next}.png");
                foreach (var g in baked) Object.DestroyImmediate(g);
                foreach (var sk in skins) sk.enabled = true;
                var hips = rider.transform.Find("Pivot");
                var tape0 = Object.FindFirstObjectByType<CautionTape>();
                Debug.Log($"STARTSHOT {next} tape={(tape0 != null && tape0.Broken)} t={t:0.00} rider={p} ground={Slope.DrawnHeight(p.x, p.z):0.00} cam={cam.transform.position} chunks={terrain.transform.childCount} model={(skins.Length > 0 ? skins[0].bounds.center.ToString() : "-")}");
                next++;
            }
        }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
    }
}
