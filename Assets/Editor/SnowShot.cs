using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Debug = UnityEngine.Debug;

/// <summary>
/// What a wipeout leaves in the snow: the rider rides a few seconds (the board's trail), crashes at speed, tumbles
/// and slides to a stop, gets up and rides off; shots of the tumble, the body lying in the snow, and the furrow and
/// holes it left, from above and from low down. Collision callbacks do not fire in batchmode, so the body's hits and
/// slides are worked out here each step and handed to RiderBody as the game's physics would (both the old crater
/// path and the new one see the same events). The particle systems are stepped by hand too.
/// Run: Unity -batchmode -projectPath . -executeMethod SnowShot.Run -logFile x.log [-snowTag before]
/// (no -nographics). Writes SnowShot*_tag.png in the project folder. Compiles on code without SnowLayer too.
/// </summary>
public static class SnowShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;

    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camera cam; static ChaseCamera chase; static MethodInfo late, tUpd, fUpd, hitM, dragM; static Atmosphere atmo;
    static ParticleSystem[] fxSystems;
    static string tag = "after";
    static readonly Dictionary<Rigidbody, (bool touching, float vy, Vector3 lastDrag)> contact = new Dictionary<Rigidbody, (bool, float, Vector3)>();
    static int hits, drags;
    static float CrashSpeed = 11f;

    public static void Run()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-snowTag") tag = args[i + 1];
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        Random.InitState(4242);   // the same mountain every run, so before and after can be compared
        TestBoot.Boot(game);
        rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        fUpd = typeof(Forest).GetMethod("Update", NP);
        hitM = typeof(RiderBody).GetMethod("OnRagdollHit", NP);
        dragM = typeof(RiderBody).GetMethod("OnRagdollDrag", NP);
        var fx = Object.FindFirstObjectByType<SnowFX>();
        fxSystems = fx != null ? fx.GetComponentsInChildren<ParticleSystem>() : new ParticleSystem[0];
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        try { Sequence(); }
        catch (System.Exception e) { Debug.LogError("SNOWSHOT exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    static void Sequence()
    {
        Random.InitState(4242);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float z0 = Course.First + 30f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.forward * 12f;
        terrain.Prewarm(); props.Prewarm();
        chase.Snap();
        Shoot("Warmup", false);   // the first render comes out unlit
        Shoot("Warmup", false);

        // carve a few turns, for a board trail behind the crash
        for (float t = 0f; t < 2.2f; t += Dt) { rider.Steer = Mathf.Sin(t * 2.6f) * 0.8f; Step(); }
        // slow down to a crash a phone screen can take in (the slide stays inside the snow patch)
        rider.Body.linearVelocity = rider.Body.linearVelocity.normalized * CrashSpeed; body.Apply(0f);
        Vector3 crashAt = rider.transform.position;
        Vector3 crashVel = rider.Velocity;
        Log($"crash at {crashAt} speed {rider.Speed:0.0} m/s");
        rider.DoCrash("test");
        var watch = new Stopwatch();
        float[] at = { 0.3f, 0.7f, 1.2f, 2.0f };
        int shot = 0; float tt = 0f;
        while (shot < at.Length)
        {
            rider.Steer = 0.4f;
            watch.Start(); Step(); watch.Stop();
            tt += Dt;
            if (tt >= at[shot]) { Shoot($"Fall{shot}", true); shot++; }
        }
        var trail = new List<Vector3>();
        for (float t = 0f; t < 10f && !(rider.CanGetUp || (rider.StayDown && !body.RagdollMoving && t > 1.5f)); t += Dt) { rider.Steer = 0f; watch.Start(); Step(); watch.Stop(); tt += Dt; trail.Add(body.RagdollCentre); }
        Vector3 rest = body.RagdollCentre;
        Diag("at rest");
        Log($"rest at {rest}, {Vector3.Distance(new Vector3(crashAt.x, 0, crashAt.z), new Vector3(rest.x, 0, rest.z)):0.0} m from the crash after {tt:0.0} s; {hits} hits, {drags} slides; crash steps {watch.Elapsed.TotalMilliseconds / (tt / Dt):0.000} ms/frame");

        // the way it was sliding at the end (the path bends), for framing
        Vector3 back = trail.Count > 0 ? trail[0] : crashAt;
        for (int i = trail.Count - 1; i >= 0; i--) if (Vector3.Distance(trail[i], rest) > 4f) { back = trail[i]; break; }
        Vector3 along = rest - back; along.y = 0f;
        along = along.sqrMagnitude > 0.5f ? along.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
        // the body lying in the snow, close up from the side
        Look(Above(rest + side * 2.4f - along * 0.8f, 1.2f), rest + Vector3.down * 0.1f, 45f);
        Shoot("Rest", true);
        // straight down from 9 m: the last stretch of the furrow and the body at the end of it
        Look(rest + Vector3.up * 9f, rest, 60f);
        cam.transform.rotation = Quaternion.LookRotation(Vector3.down, along);
        Shoot("RestDown", true);
        // from up the furrow, low, looking down it to the body
        Look(Above(rest - along * 5.5f + side * 0.9f, 1.7f), rest, 50f);
        Shoot("FurrowLow", true);
        // from past the body, looking back up the furrow
        Look(Above(rest + along * 2.2f + side * 1.0f, 1.9f), Above(rest - along * 9f, 0f), 50f);
        Shoot("FurrowBack", true);
        // up again: what the chase camera shows as you set off
        rider.RequestUp = true;
        for (int i = 0; i < 30 && rider.Crashed; i++) Step();
        chase.Snap();
        for (float t = 0f; t < 0.5f; t += Dt) { rider.Steer = 0f; Step(); }
        Shoot("Chase", false);
        Bench();
    }

    static void Diag(string when)
    {
        var t = System.Type.GetType("Summit.SnowLayer, Assembly-CSharp");
        if (t == null) return;
        var layer = t.GetProperty("Instance").GetValue(null);
        var snow = (Vector4[])t.GetField("snow", NP).GetValue(layer);
        var ground = (Vector4[])t.GetField("ground", NP).GetValue(layer);
        var mr = (MeshRenderer)t.GetField("mr", NP).GetValue(layer);
        int dents = 0, piles = 0, none = 0; float lo = 0f, hi = 0f;
        for (int i = 0; i < snow.Length; i++)
        {
            if (snow[i].x < -0.01f) dents++; if (snow[i].x > 0.01f) piles++; if (ground[i].w < 0.5f) none++;
            lo = Mathf.Min(lo, snow[i].x); hi = Mathf.Max(hi, snow[i].x);
        }
        Log($"{when}: layer drawn={mr.enabled} dents={dents} piles={piles} deepest={lo:0.000} highest={hi:0.000} no-snow cells={none} origin={t.GetField("ox", NP).GetValue(layer)},{t.GetField("oz", NP).GetValue(layer)}");
    }

    /// <summary>Time the layer on its own (when there is one): a press of the whole body, and stepping the patch.</summary>
    static void Bench()
    {
        var t = System.Type.GetType("Summit.SnowLayer, Assembly-CSharp");
        if (t == null) return;
        var layer = t.GetProperty("Instance").GetValue(null);
        var focus = t.GetMethod("Focus");
        var press = t.GetMethod("Press");
        // a body's worth of presses (12 shapes) on fresh snow, sliding
        var w = new Stopwatch();
        Vector3 p = rider.transform.position;
        int n = 200;
        for (int k = 0; k < n; k++)
        {
            Vector3 c = p + new Vector3(0f, 0f, k * 0.05f);
            float g = Slope.DrawnHeight(c.x, c.z);
            w.Start();
            for (int s = 0; s < 12; s++)
            {
                Vector3 a = c + new Vector3((s % 4) * 0.2f, g - c.y + 0.07f, (s / 4) * 0.3f), b = a + new Vector3(0.1f, 0f, 0.3f);
                press.Invoke(layer, new object[] { a, b, 0.07f, 0.07f, new Vector3(0f, 0f, 6f), 0.05f, 0.5f });
            }
            w.Stop();
        }
        Log($"bench: 12 presses (one body step) {w.Elapsed.TotalMilliseconds / n:0.000} ms (editor, Mono)");
        // following the rider at 21 m/s, drifting sideways: with no marks about, then with marks always in view
        var clear = t.GetMethod("Clear");
        clear.Invoke(layer, null);
        w.Reset();
        for (int k = 0; k < 400; k++)
        {
            Vector3 c = p + new Vector3(Mathf.Sin(k * 0.05f) * 3f, 0f, k * 0.175f);   // 120 steps a second
            w.Start(); focus.Invoke(layer, new object[] { c }); w.Stop();
        }
        Log($"bench: following at 21 m/s, no marks: {w.Elapsed.TotalMilliseconds / 400:0.0000} ms per physics step");
        Vector3 q = p + new Vector3(Mathf.Sin(400 * 0.05f) * 3f, 0f, 400 * 0.175f);
        float gq = Slope.DrawnHeight(q.x, q.z);
        w.Reset(); w.Start();
        press.Invoke(layer, new object[] { new Vector3(q.x, gq + 0.05f, q.z), new Vector3(q.x, gq + 0.05f, q.z + 0.3f), 0.1f, 0.1f, Vector3.zero, 0.05f, 0.5f });
        w.Stop();
        Log($"bench: first press after riding clean (lays the whole patch): {w.Elapsed.TotalMilliseconds:0.00} ms");
        w.Reset();
        for (int k = 401; k < 801; k++)
        {
            Vector3 c = p + new Vector3(Mathf.Sin(k * 0.05f) * 3f, 0f, k * 0.175f);
            w.Start(); focus.Invoke(layer, new object[] { c }); w.Stop();
            if (k % 60 == 0) { float g = Slope.DrawnHeight(c.x, c.z); press.Invoke(layer, new object[] { new Vector3(c.x, g + 0.05f, c.z), new Vector3(c.x, g + 0.05f, c.z + 0.3f), 0.1f, 0.1f, Vector3.zero, 0.05f, 0.5f }); }
        }
        Log($"bench: following at 21 m/s with marks in view: {w.Elapsed.TotalMilliseconds / 400:0.000} ms per physics step (avg)");
        var flush = t.GetMethod("Flush");
        w.Reset(); w.Start(); flush.Invoke(layer, null); w.Stop();
        Log($"bench: flush {w.Elapsed.TotalMilliseconds:0.000} ms");
    }

    static void Step()
    {
        for (int k = 0; k < 2; k++)
        {
            rider.Tick(Dt / 2f);
            body.PhysicsStep();
            Physics.Simulate(Dt / 2f);
            Contacts();
        }
        tUpd.Invoke(terrain, null);
        fUpd.Invoke(props, null);
        body.Apply(Dt);
        foreach (var ps in fxSystems) ps.Simulate(Dt, false, false, false);
        chase.enabled = true;
        late.Invoke(chase, null);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
    }

    /// <summary>What the game's collision callbacks would report: a part landing on the snow, a part sliding along it.</summary>
    static void Contacts()
    {
        var rd = typeof(RiderBody).GetField("ragdoll", NP).GetValue(body);
        if (rd == null || !(bool)rd.GetType().GetProperty("Active").GetValue(rd)) { contact.Clear(); return; }
        var parts = (IList)rd.GetType().GetField("parts", NP).GetValue(rd);
        foreach (var part in parts)
        {
            var pt = part.GetType();
            var rb = (Rigidbody)pt.GetField("rb").GetValue(part);
            var col = (Collider)pt.GetField("col").GetValue(part);
            var bone = (Transform)pt.GetField("bone").GetValue(part);
            Vector3 c = rb.position;
            float g = Slope.DrawnHeight(c.x, c.z);
            bool touching = col.bounds.min.y < g + 0.04f;
            contact.TryGetValue(rb, out var was);
            Vector3 at = new Vector3(c.x, g, c.z);
            if (touching && !was.touching && was.vy < -1.5f) { hits++; hitM.Invoke(body, new object[] { -was.vy, false, at, bone }); }
            Vector3 lastDrag = was.lastDrag;
            float flat = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude;
            if (touching && flat > 1.2f && (at - lastDrag).sqrMagnitude > 0.36f) { drags++; lastDrag = at; dragM.Invoke(body, new object[] { at, flat }); }
            contact[rb] = (touching, rb.linearVelocity.y, lastDrag);
        }
    }

    static void Look(Vector3 from, Vector3 to, float fov)
    {
        chase.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
    }

    /// <summary>h metres above the snow at p.</summary>
    static Vector3 Above(Vector3 p, float h) => new Vector3(p.x, Slope.DrawnHeight(p.x, p.z) + h, p.z);

    static void Log(string s) => Debug.Log("SNOWSHOT " + s);

    static void Shoot(string name, bool bakeSkins)
    {
        DynamicGI.UpdateEnvironment();
        // batchmode renders do not re-skin meshes: bake them to see the real pose
        var baked = new List<GameObject>();
        var hidden = new List<SkinnedMeshRenderer>();
        if (bakeSkins)
            foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
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
        File.WriteAllBytes($"SnowShot{name}_{tag}.png", tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        rt.Release();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }
}
