using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// treeline: the chase camera in tight trees, before and after (needs the GPU: never with -nographics).
///   Unity -batchmode -projectPath . -executeMethod TreeCamShots.Run -logFile cam.log      (TREECAM_OUT=dir)
/// A bot rides the valley's forest at the foot of the mountain (seed 1000) along the run's edge, in and out of the trees. The ride is found once, the
/// moments where the trees are tightest picked, and the same ride (it is the same to the step: the camera does not
/// move the rider) is shot from the chase camera at those moments with the tight-trees view off (before) and on
/// (after): TreeCam_before.png, TreeCam_after.png, and TreeCam_compare.png (before on top, after below).
/// TREECAM_MODE=see: the tight view on in both, and the view through the trees (ChaseCamera.SeeOn) off before, on after.
/// </summary>
public static class TreeCamShots
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    const int W = 360, H = 780, Shots = 3;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static ChaseCamera chase; static MethodInfo camStep; static Camera cam;
    static readonly bool seeMode = System.Environment.GetEnvironmentVariable("TREECAM_MODE") == "see";

    public static void Run()
    {
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        r = game.rider;
        body = game.body;
        terrain = game.terrain;
        props = game.props;
        chase = Object.FindFirstObjectByType<ChaseCamera>();
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        string outDir = System.Environment.GetEnvironmentVariable("TREECAM_OUT") ?? "treecam-shots";
        System.IO.Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        TestBoot.NewMountain(game, 1000, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        cam = Camera.main;
        chase.enabled = false;
        try
        {
            // 1. the ride, once, with the tight view on: where are the trees tightest?
            var tights = new List<(int frame, float tight)>();
            Ride(false, f => tights.Add((f, chase.Tight)), null);
            tights.Sort((a, b) => b.tight.CompareTo(a.tight));
            var picks = new List<int>();
            foreach (var t in tights)
            {
                if (picks.Count >= Shots) break;
                bool apart = true; foreach (int p in picks) if (Mathf.Abs(p - t.frame) < 90) apart = false;   // (1.5 s apart)
                if (apart && t.frame > 120) picks.Add(t.frame);
            }
            picks.Sort();
            string seen = ""; foreach (int p in picks) seen += $" frame {p} ({p * 2 * Dt:0.0} s) tight {tights.Find(x => x.frame == p).tight:0.00};";
            Debug.Log($"TREECAM the tightest moments:{seen}");
            // 2. the same ride shot at those moments, before and after
            var before = Shoot(false, picks); var after = Shoot(true, picks);
            ChaseCamera.SeeOn = true;
            Save(before, $"{outDir}/TreeCam_before.png"); Save(after, $"{outDir}/TreeCam_after.png");
            var both = new Texture2D(W * Shots, H * 2, TextureFormat.RGB24, false);
            both.SetPixels(0, H, W * Shots, H, before.GetPixels()); both.SetPixels(0, 0, W * Shots, H, after.GetPixels()); both.Apply();
            Save(both, $"{outDir}/TreeCam_compare.png");
        }
        catch (System.Exception e) { Debug.LogError("TREECAM exception " + e); }
        ChaseCamera.TightOn = true;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Save(Texture2D t, string file) { System.IO.File.WriteAllBytes(file, t.EncodeToPNG()); Debug.Log($"TREECAM wrote {file}"); }

    static Texture2D Shoot(bool on, List<int> frames)
    {
        var strip = new Texture2D(W * Shots, H, TextureFormat.RGB24, false);
        var rt = new RenderTexture(W, H, 24); var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        int taken = 0;
        ChaseCamera.SeeOn = !seeMode || on;
        Ride(seeMode ? false : !on, null, f =>
        {
            if (!frames.Contains(f)) return;
            // (the batch run has no frame loop: the sky, the forest and what has broken are drawn by hand)
            var atmo = Object.FindFirstObjectByType<Atmosphere>();
            if (atmo != null) typeof(Atmosphere).GetMethod("LateUpdate", NP)?.Invoke(atmo, null);
            typeof(Forest).GetMethod("Draw", NP | BindingFlags.Public).Invoke(props, null);
            typeof(TreeBreak).GetMethod("Draw", NP | BindingFlags.Public)?.Invoke(TreeBreak.Instance, null);
            cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            strip.SetPixels(frames.IndexOf(f) * W, 0, W, H, tex.GetPixels());
            Vector3 c = chase.transform.position, p = r.transform.position;
            Debug.Log($"TREECAM {(on ? "after" : "before")} frame {f}: see-through {(ChaseCamera.SeeOn ? "on" : "off")}, tight {chase.Tight:0.00}, the lens {new Vector2(c.x - p.x, c.z - p.z).magnitude:0.0} m behind and {c.y - p.y:0.0} m up, field of view {cam.fieldOfView:0}, {r.Speed * 3.6f:0} km/h");
            taken++;
        });
        cam.targetTexture = null; RenderTexture.active = null;
        strip.Apply();
        return strip;
    }

    /// <summary>The ride: 14 s from the top of the lower forest, weaving 2-7 m off the run's right edge. Each camera
    /// frame (every second step): `look` (the frame number), `shoot` (render now). `off`: the tight view off.</summary>
    static void Ride(bool off, System.Action<int> look, System.Action<int> shoot)
    {
        ChaseCamera.TightOn = !off;
        // (the same mountain each time, undamaged: what the last ride broke would change this one)
        TestBoot.NewMountain(game, 1000, terrain, props);
        float z0 = Course.First;
        // (the valley's forest at the foot of the mountain, the densest and tallest trees: where the playtest was)
        for (float z = Course.First; z < Slope.FinishZ; z += 20f) if (Forest.Low(z) > Forest.Valley) { z0 = z; break; }
        z0 = Mathf.Min(z0, Slope.FinishZ - 420f);
        float x0 = Slope.Centre(z0) + Slope.Half(z0) - 1f;
        r.ResetAt(x0, z0); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x0, z0)).normalized * 14f;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        chase.rider = r; chase.Snap();
        Random.InitState(1234);
        int frame = 0;
        for (int n = 0; n < 120 * 14; n++)
        {
            // (along the run's right edge, in and out of the trees' reach: a metre inside it to two out)
            r.Steer = r.Crashed ? 0f : CarveBot.Onto(r, zz => Slope.Centre(zz) + Slope.Half(zz) + 0.5f + 1.5f * Mathf.Sin(zz / 17f), 1.2f, 8f, 4f);
            if (r.Speed > 20f && !r.Airborne) r.Body.linearVelocity *= 0.995f;
            r.TestContacts(Dt);
            TestHarness.Step(r, body, Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (r.Crashed && r.CanGetUp) r.RequestUp = true;
            if (n % 2 != 1) continue;
            body.Apply(2 * Dt);
            ChaseCamera.TestDt = 2 * Dt; camStep.Invoke(chase, null);
            Surfaces.Step(r.Body.position, r.RunNow, 2 * Dt, true);
            frame++;
            look?.Invoke(frame);
            shoot?.Invoke(frame);
        }
        ChaseCamera.TestDt = 0f;
    }
}
