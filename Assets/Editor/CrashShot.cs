using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders the playful side of crashing, through the game's own wiring: a wipeout steered with the thumb
/// while tumbling, the snow caked on the clothes and melting off as you ride on, and the numbered flags
/// left where each fall came to rest. Run: Unity -batchmode -projectPath . -executeMethod CrashShot.Run
/// -logFile x.log (no -nographics: it needs the GPU). Writes CrashShot*.png in the project folder.
/// </summary>
public static class CrashShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;

    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camera cam; static ChaseCamera chase; static MethodInfo late, tUpd, fUpd; static Atmosphere atmo; static FallFlags flags;
    static float clock;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        flags = (FallFlags)typeof(SummitGame).GetField("flags", NP).GetValue(game);
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        fUpd = typeof(Forest).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        try { Sequence(); }
        catch (System.Exception e) { Debug.LogError("CRASHSHOT exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    static void Sequence()
    {
        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var restUv = new List<Vector3>(); smr.sharedMesh.GetUVs(2, restUv);
            if (restUv.Count > 0) { var b = new Bounds(restUv[0], Vector3.zero); foreach (var q in restUv) b.Encapsulate(q); Log($"rest frame {smr.name}: {b.min} .. {b.max} (metres) scale={smr.transform.lossyScale.x}"); }
            foreach (var m in smr.sharedMaterials)
                Debug.Log($"CRASHSHOT material {smr.name}: {(m != null ? m.name + " / " + m.shader.name : "-")} uv2={smr.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2)}");
        }

        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float z0 = Course.First + 30f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.forward * 18f;
        terrain.Prewarm(); props.Prewarm();
        chase.Snap();
        Shoot("CrashShotWarmup.png", false);   // the first render comes out unlit
        // the shader on its own: fully caked, no fall needed (the impact side set to the back)
        if (body.snowy != null)
        {
            for (int i = 0; i < 3; i++) body.snowy.Plough(-rider.transform.right, 6f, 0.4f);
            body.snowy.Set(1f, 0f);
            Step();
            var p = BodyMid() + Vector3.down * 0.15f;
            Look(p + new Vector3(3.4f, 0.6f, 1.6f), p, 40f); Shoot("CrashShotSnowFullA.png", true);
            Shoot("CrashShotSnowFullA2.png", true);   // the same again: the first use of a shader can render half-compiled
            body.snowy.Set(0.6f, 0f); Shoot("CrashShotSnowHalfA.png", true); body.snowy.Set(1f, 0f);
            Look(p + new Vector3(-3.4f, 0.6f, 1.6f), p, 40f); Shoot("CrashShotSnowFullB.png", true);
            body.snowy.Clear();
            chase.Snap();
        }

        // ---- fall 1: thrown at 18 m/s, steered hard right while tumbling
        RideFor(0.8f, Centre);
        Vector3 before = rider.transform.position;
        rider.DoCrash("test");
        float t = 0f; int shot = 0;
        float[] at = { 0.15f, 0.45f, 0.9f, 1.5f, 2.3f };
        while (shot < at.Length)
        {
            rider.Steer = 1f;
            Step(); t += Dt;
            if (t >= at[shot]) { Shoot($"CrashShotFall{shot}.png", true); Log($"fall t={t:0.00}"); shot++; }
        }
        Vector3 rest = WaitForRest(1f);
        Log($"fall 1 rest: {rest.x - before.x:0.0} m right of where it started (steer +1), {rest.z - before.z:0.0} m on");
        // the body lying caked in snow, close up
        Vector3 c = body.RagdollCentre;
        Look(c + new Vector3(2.2f, 1.2f, 2.8f), c, 45f);   // from below: up the hill the snow is in the way
        Shoot("CrashShotDown.png", true);
        GetUp();
        Log($"flags after fall 1: {flags.Count}");
        chase.Snap(); Step();
        Shoot("CrashShotFlag1Chase.png", true);

        // ---- riding on: the snow melts off. Close-ups from both sides as it goes
        float[] melt = { 0f, 4f, 8f, 12f, 16f };
        float ridden = 0f;
        for (int i = 0; i < melt.Length; i++)
        {
            RideFor(melt[i] - ridden, Centre); ridden = melt[i];
            var p = BodyMid() + Vector3.down * 0.15f;
            Log($"melt t={melt[i]:0} speed={rider.Speed * 3.6f:0} km/h snow={body.snowy?.Amount:0.00}");
            Look(p + new Vector3(3.4f, 0.6f, 1.6f), p, 40f); Shoot($"CrashShotSnowA{i}.png", true);
            Look(p + new Vector3(-3.4f, 0.6f, 1.6f), p, 40f); Shoot($"CrashShotSnowB{i}.png", true);
            chase.Snap();
        }

        // ---- falls 2 and 3: the last one ends the run, and still gets its flag
        rider.DoCrash("test");
        for (float k = 0f; k < 0.6f; k += Dt) { rider.Steer = -1f; Step(); }
        WaitForRest(-1f);
        GetUp();
        RideFor(3f, Centre);
        rider.DoCrash("test");
        WaitForRest(0f);
        typeof(SummitGame).GetMethod("ToEnd", NP).Invoke(game, new object[] { "WIPED OUT", false, false });
        Log($"flags after the run: {flags.Count}");

        // ---- the flags, close up, front and back, at two moments of the flutter
        int n = 0;
        foreach (Transform f in flags.transform)
        {
            n++;
            var fp = f.position;
            rider.ResetAt(fp.x, fp.z - 2f);   // the world streams round the rider
            terrain.Prewarm(); props.Prewarm(); fUpd.Invoke(props, null);
            Vector3 aim = fp + Vector3.up * 0.9f;
            Look(fp + new Vector3(-1.2f, 1.3f, -2.6f), aim, 45f); flags.Step(0.13f); Shoot($"CrashShotFlag{n}.png", false);
            Look(fp + new Vector3(1.2f, 1.3f, 2.6f), aim, 45f); flags.Step(0.21f); Shoot($"CrashShotFlag{n}Back.png", false);
            Log($"flag {f.name} at {fp}");
        }
        // all three in one view, from up the hill
        if (flags.transform.childCount > 0)
        {
            var f0 = flags.transform.GetChild(0).position;
            rider.ResetAt(f0.x, f0.z); terrain.Prewarm(); props.Prewarm(); fUpd.Invoke(props, null);
            Look(f0 + new Vector3(0f, 4f, -9f), f0 + new Vector3(0f, 0.5f, 12f), 55f);
            Shoot("CrashShotFlagsView.png", false);
        }
    }

    static float Centre(float tt) => Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);

    static void Step()
    {
        for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
        clock += Dt;
        tUpd.Invoke(terrain, null);
        fUpd.Invoke(props, null);
        body.Apply(Dt);
        flags.Step(Dt);
        chase.enabled = true;
        late.Invoke(chase, null);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
    }

    static void RideFor(float secs, System.Func<float, float> steer)
    {
        for (float t = 0f; t < secs; t += Dt) { rider.Steer = steer(t); Step(); }
    }

    /// <summary>Let the body come to rest (steering it as it goes); returns where it stopped.</summary>
    static Vector3 WaitForRest(float steer)
    {
        for (float t = 0f; t < 10f && !(rider.CanGetUp || (rider.StayDown && !body.RagdollMoving && t > 1.5f)); t += Dt) { rider.Steer = steer; Step(); }
        rider.Steer = 0f;
        Log($"at rest: snow={body.snowy?.Amount:0.00}");
        return body.RagdollCentre;
    }

    static void GetUp()
    {
        rider.RequestUp = true;
        for (int i = 0; i < 30 && rider.Crashed; i++) Step();
        Step();
    }

    static void Look(Vector3 from, Vector3 to, float fov)
    {
        chase.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
    }

    static void Log(string s) => Debug.Log("CRASHSHOT " + s);

    /// <summary>The middle of the rider's body as drawn (the jacket's bounds).</summary>
    static Vector3 BodyMid()
    {
        foreach (var sk in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (sk.name.Contains("Body")) return sk.bounds.center;
        return rider.transform.position + Vector3.up * 0.75f;
    }

    static void Shoot(string file, bool bakeSkins)
    {
        DynamicGI.UpdateEnvironment();
        // batchmode renders do not re-skin meshes: bake them to see the real pose
        var baked = new List<GameObject>();
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var hidden = new List<SkinnedMeshRenderer>();
        if (bakeSkins)
            foreach (var sk in skins)
            {
                if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
                var m = new Mesh(); sk.BakeMesh(m);
                var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
                g.GetComponent<MeshFilter>().sharedMesh = m;
                g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
                g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                sk.enabled = false; hidden.Add(sk); baked.Add(g);
            }
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
}
