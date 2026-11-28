using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (tube) The snow tube close up, as the game draws it: on the start pad with the rider sat in it (from the side, from
/// the front three-quarters, from above, low from behind), and riding down the snow from the chase camera.
/// Batch (with graphics): -batchmode -quit -projectPath . -executeMethod TubeShot.Run, with $TUBE_SHOTS the folder.
/// </summary>
public static class TubeShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static SummitGame game; static Summit.Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static Camera cam;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("TUBE_SHOTS");
        if (string.IsNullOrEmpty(dir)) dir = "TubeShots";
        Directory.CreateDirectory(dir);
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        rider = game.rider; body = game.body; terrain = game.terrain; props = game.props;
        cam = Camera.main;
        try
        {
            rider.Tube = true; body.SetTube(true);
            typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
            for (int i = 0; i < 30; i++) body.Apply(Dt);
            terrain.Prewarm(); props.Prewarm();
            var p = rider.transform.position;
            var f = body.transform.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            var r = Vector3.Cross(Vector3.up, f);
            for (int w = 0; w < 3; w++) Shoot(dir, "tube_warmup.png", p + r * 2.6f + Vector3.up * 1.1f, p + Vector3.up * 0.4f);
            Shoot(dir, "tube_1_side.png", p + r * 2.6f + Vector3.up * 1.0f, p + Vector3.up * 0.4f);
            Shoot(dir, "tube_2_front.png", p + f * 2.3f + r * 1.3f + Vector3.up * 1.3f, p + Vector3.up * 0.35f);
            Shoot(dir, "tube_3_above.png", p + Vector3.up * 3.2f + f * 0.8f + r * 0.4f, p);
            Shoot(dir, "tube_4_back_low.png", p - f * 2.4f - r * 1.2f + Vector3.up * 0.45f, p + Vector3.up * 0.3f);
            // riding down the snow
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            RiderInput.TestDrive = true; RiderInput.TestFingers = 0;
            for (int i = 0; i < 240; i++) { rider.Steer = 0.3f * Mathf.Sin(i * 0.03f); for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); } body.Apply(Dt); }
            terrain.Prewarm(); props.Prewarm();
            p = rider.transform.position; var v = rider.Velocity; v.y = 0f; v.Normalize(); r = Vector3.Cross(Vector3.up, v);
            Shoot(dir, "tube_5_riding.png", p - v * 3.6f + r * 1.4f + Vector3.up * 1.8f, p + v * 1.5f + Vector3.up * 0.3f);
            Shoot(dir, "tube_6_riding_side.png", p + r * 3.2f + v * 0.6f + Vector3.up * 0.9f, p + Vector3.up * 0.35f);
        }
        catch (System.Exception e) { Debug.LogError("TUBESHOT " + e); }
        RiderInput.TestDrive = false;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Shoot(string dir, string file, Vector3 eye, Vector3 look)
    {
        int w = 900, h = 700;
        var rt = new RenderTexture(w, h, 24);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterials = sk.sharedMaterials;
            var mpb = new MaterialPropertyBlock(); sk.GetPropertyBlock(mpb); mr.SetPropertyBlock(mpb);
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            if (sk.bones.Length == 0) g.transform.localScale = sk.transform.lossyScale;
            sk.enabled = false; off.Add(sk); baked.Add(g);
        }
        cam.Render();
        foreach (var g in baked) { Object.DestroyImmediate(g.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(g); }
        foreach (var sk in off) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("TUBESHOT " + file);
    }
}
