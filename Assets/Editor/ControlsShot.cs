using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The run's jump finger and the rail sparks. First the tap-or-tuck rule on its own: a quick tap of the second
/// finger is a jump, a press held is a tuck and letting go of it is not a jump. Then a grind along a rail, rendered
/// from beside it with the sparks coming off the board. Run: Unity -batchmode -projectPath . -executeMethod
/// ControlsShot.Run -logFile x.log (no -nographics). Writes ControlsShot*.png; lines start CONTROLS.
/// </summary>
public static class ControlsShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static int fails;
    static void Log(string s) => Debug.Log("CONTROLS " + s);
    static void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }

    public static void Run()
    {
        try { TapProof(); Grind(); }
        catch (System.Exception e) { Debug.LogError("CONTROLS exception " + e); fails++; }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Log($"done: {fails} failure(s)");
        EditorApplication.Exit(0);
    }

    // ---- the jump finger's rule: play a press of so many frames and see what came of it
    static void TapProof()
    {
        (int jumps, bool tucked, float tuckFor) Play(int downFrames, int frames)
        {
            float pt = 0f; int jumps = 0; bool tucked = false; float tuckFor = 0f;
            for (int f = 0; f < frames; f++)
            {
                bool hold = RiderInput.TapOrTuck(f >= 2 && f < 2 + downFrames, Dt, ref pt, out bool jump);
                if (jump) jumps++;
                if (hold) { tucked = true; tuckFor += Dt; }
            }
            return (jumps, tucked, tuckFor);
        }
        var r = Play(5, 30);     // 0.08 s
        Check($"a quick tap is one jump, no tuck (jumps={r.jumps} tucked={r.tucked})", r.jumps == 1 && !r.tucked);
        r = Play(1, 30);         // a single frame's touch
        Check($"the briefest touch is still a jump (jumps={r.jumps})", r.jumps == 1 && !r.tucked);
        r = Play(12, 40);        // 0.2 s: still a tap
        Check($"a 0.2 s press is still a tap (jumps={r.jumps} tucked={r.tucked})", r.jumps == 1 && !r.tucked);
        r = Play(60, 90);        // a second: a tuck
        Check($"a held press is a tuck, and letting go is not a jump (jumps={r.jumps} tucked for {r.tuckFor:0.00} s)", r.jumps == 0 && r.tucked && r.tuckFor > 0.7f);
        // two taps in a row: two jumps
        float pt2 = 0f; int j2 = 0;
        for (int f = 0; f < 60; f++) { RiderInput.TapOrTuck((f >= 2 && f < 6) || (f >= 20 && f < 25), Dt, ref pt2, out bool jump); if (jump) j2++; }
        Check($"two taps are two jumps (jumps={j2})", j2 == 2);
    }

    // ---- a rail, ridden along, with its sparks
    static void Grind()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body"); var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props");
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); var fUpd = typeof(Forest).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;

        void Step()
        {
            for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            body.Apply(Dt);
            chase.enabled = true; late.Invoke(chase, null);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name == "GrindSparks") ps.Simulate(Dt, true, false, false);
        }
        void Shoot(string file)
        {
            DynamicGI.UpdateEnvironment();
            var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
            foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
                var m = new Mesh(); sk.BakeMesh(m);
                var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
                g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
                g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                sk.enabled = false; hidden.Add(sk); baked.Add(g);
            }
            typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
            int w = 590, h = 1278;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            foreach (var g in baked) Object.DestroyImmediate(g);
            foreach (var sk in hidden) sk.enabled = true;
        }

        Course.Feature? rail = null, box = null;
        for (int c = 0; c < 3 && (rail == null || box == null); c++)
            foreach (var f in Course.FeaturesOf(c)) { if (f.kind == Course.Kind.Rail && rail == null) rail = f; if (f.kind == Course.Kind.Box && box == null) box = f; }
        int Ride(Course.Feature f, string file)
        {
            rider.ResetAt(f.a.x, f.a.z - 18f); body.ResetVisual(); rider.Begin();
            rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(f.a.x, f.a.z - 18f)).normalized * 12f;
            terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm(); chase.Snap();
            if (file != null) { Step(); Shoot("ControlsShotWarmup.png"); Shoot("ControlsShotWarmup.png"); }
            bool on = false, shot = false; int most = 0;
            for (float t = 0f; t < 10f && !rider.Crashed && rider.Z < f.b.z + 10f; t += Dt)
            {
                float tx = rider.Z < f.a.z ? f.a.x : Mathf.Lerp(f.a.x, f.b.x, Mathf.InverseLerp(f.a.z, f.b.z, rider.Z));
                rider.Steer = Mathf.Clamp((tx - rider.transform.position.x) * 0.4f - rider.Heading * 1.5f, -1f, 1f);
                Step();
                if (rider.Grinding) { on = true; most = Mathf.Max(most, body.SparkCount); }
                if (file != null && rider.Grinding && !shot && rider.Z > Mathf.Lerp(f.a.z, f.b.z, 0.4f))
                {
                    shot = true;
                    Log($"grinding at {rider.Speed * 3.6f:0} km/h, {body.SparkCount} sparks in the air");
                    Shoot(file + "Chase.png");
                    var rp = rider.transform.position;
                    Vector3 side = new Vector3(Mathf.Sign(Slope.Centre(rp.z) - rp.x), 0f, 0f);
                    chase.enabled = false; cam.fieldOfView = 45f;
                    Vector3 eye = rp + side * 3.2f + new Vector3(0f, 0.5f, 1.6f);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(rp + Vector3.down * 0.2f - eye));
                    Shoot(file + "Side.png");
                    eye = rp + side * 1.5f + new Vector3(0f, 0.1f, -1.9f);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(rp + Vector3.down * 0.3f - eye));
                    Shoot(file + "Close.png");
                    foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                        if (ps.name == "GrindSparks")
                        {
                            var arr = new ParticleSystem.Particle[8]; int n = ps.GetParticles(arr);
                            for (int q = 0; q < n; q++) Log($"  spark {(arr[q].position - rp).magnitude:0.00} m from the board, size {arr[q].GetCurrentSize(ps):0.000}, v {arr[q].velocity.magnitude:0.0}");
                            Log($"  material {ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.name}");
                        }
                }
            }
            Log($"{f.kind}: boarded={on}, most sparks at once {most}");
            return on ? most : -1;
        }
        if (rail != null) { int n = Ride(rail.Value, "ControlsShotRail"); Check($"a rail grind throws sparks, a few at a time ({n})", n >= 2 && n <= 24); }
        else Check("found a rail", false);
        if (box != null) { int n = Ride(box.Value, null); Check($"a box throws none ({n})", n == 0); }
    }
}
