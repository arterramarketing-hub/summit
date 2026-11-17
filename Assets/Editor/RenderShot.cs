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

        rider.Begin();
        float[] shots = { 0.6f, 9f, 14f };
        float t = 0f; int shot = 0;
        for (int i = 0; shot < shots.Length; i++, t += Dt)
        {
            rider.Steer = Mathf.Clamp(0.6f * Mathf.Sin(t * 1.2f) + (Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);
            rider.Tick(Dt);
            Physics.Simulate(Dt);
            if (i % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (i % 4 == 0) { body.Apply(4 * Dt); }
            if (t >= shots[shot])
            {
                terrain.Prewarm(); props.Prewarm();
                body.Apply(Dt);
                chase.Snap();
                late.Invoke(chase, null);
                typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.name == "Snowfall") ps.Simulate(4f, true, true);
                Shoot(cam, "RenderShot" + shot + ".png");
                Debug.Log($"SHOT {shot} speed={rider.Speed * 3.6f:0} km/h air={rider.Airborne} tuck={rider.Tuck:0.0}");
                shot++;
            }
        }
        // close-ups of the rider standing still, from the side and from behind
        rider.Stop();
        rider.Heading = 0f; rider.Spin = 0f; rider.SpinVisual = 0f;
        chase.enabled = false;
        body.Apply(0.5f);
        Vector3 c = rider.transform.position + Vector3.up * 0.6f;
        cam.fieldOfView = 40f;
        cam.transform.SetPositionAndRotation(c + new Vector3(4.5f, 0.6f, 0f), Quaternion.LookRotation(-Vector3.right + Vector3.down * 0.1f));
        Shoot(cam, "RenderShotSide.png");
        cam.transform.SetPositionAndRotation(c + new Vector3(0f, 0.8f, -4.5f), Quaternion.LookRotation(Vector3.forward + Vector3.down * 0.15f));
        Shoot(cam, "RenderShotBack.png");
        var an = rider.GetComponentInChildren<Animator>();
        if (an != null)
        {
            var roll = rider.transform.Find("Pivot/Roll");
            Debug.Log("FEET L=" + roll.InverseTransformPoint(an.GetBoneTransform(HumanBodyBones.LeftFoot).position) +
                      " R=" + roll.InverseTransformPoint(an.GetBoneTransform(HumanBodyBones.RightFoot).position));
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;   // do not leave the project on manual physics
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
