using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The start gate (StartGate.cs) by day, at golden hour and at night: the board close up, the start as a run sees it
/// (waiting under it, then riding through), and from the camp above.
/// Batch: -executeMethod GateShot.Run  (writes Gate*.png in the project folder)
/// </summary>
public static class GateShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly (string name, float clock)[] Times = { ("day", 1.4f), ("golden", 2.15f), ("night", 3.4f) };
    static Camera cam;
    static DayNight dayNight;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var sh = Resources.Load<Shader>("GateBoard");
        Debug.Log($"GATESHOT GateBoard found {sh != null} supported {(sh != null && sh.isSupported)} errors {(sh != null && ShaderUtil.ShaderHasError(sh))}");
        if (sh != null) foreach (var m in ShaderUtil.GetShaderMessages(sh)) Debug.Log($"GATESHOT GateBoard: {m.severity} {m.message} line {m.line}");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight"); var camp = Get<Camp>("camp");
        cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var fUpd = typeof(Forest).GetMethod("Update", NP);
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        Debug.Log($"GATESHOT lettering ready {StartGate.Letters(true)}");
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = 1f / 60f;
        const float dt = 1f / 60f;
        void Frame() { tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); body.Apply(dt); late.Invoke(chase, null); aLate.Invoke(atmo, null); }

        // waiting at the start, as the game shows it
        for (int i = 0; i < 30; i++) Frame();
        for (int i = 0; i < 3; i++) Shoot("Gate_warmup.png");
        float cx = Slope.Centre(Slope.PadLip), gz = Slope.GateZ, gy = Slope.DrawnHeight(cx, gz);
        Debug.Log($"GATESHOT rider ready at {rider.transform.position} (gate z {gz}, start z {Slope.StartZ}), camera {cam.transform.position}");
        Times3("Gate2_start");

        // the board close up, from the in-run a little below and to one side
        var keep = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
        chase.enabled = false;
        cam.transform.position = new Vector3(cx - 1.5f, gy + 2.2f, gz - 9f);
        cam.transform.LookAt(new Vector3(cx, gy + 4.4f, gz));
        Times3("Gate1_board");
        cam.transform.position = new Vector3(cx + 7.5f, gy + 1.6f, gz - 7f);
        cam.transform.LookAt(new Vector3(cx, gy + 3.6f, gz));
        Times3("Gate1b_side");
        chase.enabled = true;
        (cam.transform.position, cam.transform.rotation, cam.fieldOfView) = keep;

        // riding through it: just before the board passes over, and just after
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        bool under = false, past = false;
        for (float t = 0f; t < 6f && !past; t += dt)
        {
            for (int k = 0; k < 2; k++) { rider.Tick(dt / 2f); body.PhysicsStep(); Physics.Simulate(dt / 2f); }
            Frame();
            if (!under && rider.Z > gz - 1.2f) { under = true; Times3("Gate3_through"); Debug.Log($"GATESHOT under the gate t={t:0.00} v={rider.Speed * 3.6f:0} km/h"); }
            if (!past && rider.Z > gz + 9f) { past = true; Times3("Gate4_past"); }
        }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;

        // from the camp: walk out to the front of it and look down the in-run at the gate
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = dt;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.transform.IsChildOf(camp.transform)) ps.Simulate(3f, true, true);
        cx = Slope.Centre(Slope.PadLip);   // (a fresh mountain: its own centre line)
        void Settle() { for (int i = 0; i < 40; i++) { camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt); tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); aLate.Invoke(atmo, null); } }
        foreach (var (file, dx, z) in new[] { ("Gate5_camp", -2f, -56f), ("Gate6_camp_far", -4f, -66f) })
        {
            var from = new Vector3(cx + dx, Slope.DrawnHeight(cx + dx, z), z);
            camp.TeleportTo(from, Mathf.Atan2(cx - from.x, gz - from.z) * Mathf.Rad2Deg);
            Settle();
            var sg = GameObject.Find("StartGate"); var gt = sg != null ? sg.transform.Find("Gate") : null;
            Debug.Log($"GATESHOT {file}: avatar {rider.transform.position} cam {cam.transform.position} fwd {cam.transform.forward} startgate {(sg != null ? sg.activeInHierarchy + " children " + sg.transform.childCount : "none")} gate {(gt != null ? gt.position + " " + gt.gameObject.activeInHierarchy : "none")} cull {cam.cullingMask} far {cam.farClipPlane}");
            Times3(file);
        }
        // and the drop-in prompt there
        var dropIn = new Vector3(cx, Slope.DrawnHeight(cx, gz - 2.6f), gz - 2.6f);
        camp.TeleportTo(dropIn, 0f);
        Settle();
        Debug.Log($"GATESHOT at the drop-in spot: near={camp.Near}");
        ChaseCamera.TestDt = 0f;
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static void Times3(string file)
    {
        foreach (var (name, clock) in Times)
        {
            TimeOfDay.TestClock = clock; dayNight.ApplyNow();
            foreach (var g in Object.FindObjectsByType<GateLamps>(FindObjectsSortMode.None)) g.Apply();
            Shoot($"{file}_{name}.png");
        }
        TimeOfDay.TestClock = Times[0].clock; dayNight.ApplyNow();
    }

    static void Shoot(string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new System.Collections.Generic.List<GameObject>();
        var off = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g); off.Add(sk);
        }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in off) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null; rt.Release();
    }
}
