using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Shows the hero in the game: bakes its clips (HeroBake), then renders it as the game builds it, from the game's
/// own camera and from a camera alongside: on the start pad, carving heel and toe, a jump with a grab, landing,
/// in the snow tube, crashing (the ragdoll), and walking and running in the camp. Frame sequences go to
/// HeroShots/seq_*/ for ffmpeg. Also times the hero's posing per frame.
/// Run: Unity -batchmode -projectPath . -executeMethod HeroShot.Run -logFile hero.log   (log lines start HEROSHOT)
/// HERO_OLD=1 renders the previous rider instead (the "before" pictures, file names start Old).
/// </summary>
public static class HeroShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camp camp; static Camera cam; static ChaseCamera chase; static MethodInfo late, aLate, tUpd, fUpd; static Atmosphere atmo;
    static string outDir, pre;
    static int step;
    static bool old;

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static void Log(string s) => Debug.Log("HEROSHOT " + s);

    public static void Run()
    {
        old = System.Environment.GetEnvironmentVariable("HERO_OLD") == "1";
        pre = old ? "Old" : "";
        outDir = System.Environment.GetEnvironmentVariable("HERO_OUT") ?? "HeroShots";
        Directory.CreateDirectory(outDir);
        if (!old) HeroBake.Bake();
        RiderBody.UseHero = !old;
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        try
        {
            TestBoot.Boot(game, false);
            rider = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
            camp = F<Camp>("camp");
            cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
            atmo = Object.FindFirstObjectByType<Atmosphere>(); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
            tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
            Stats();
            Camp_();
            typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { true });
            camp.SetNight(0f);
            Physics.simulationMode = SimulationMode.Script;
            ChaseCamera.TestDt = 4 * Dt;
            for (int i = 0; i < 30; i++) { body.Apply(1f / 30f); late.Invoke(chase, null); }
            Shoot("Pad", cam, 590, 1278);
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            Timing();
            Carve();
            Jump();
            Tube();
            Crash();
        }
        catch (System.Exception e) { Debug.LogError("HEROSHOT exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    // ---- what the hero is made of
    static void Stats()
    {
        int tris = 0, verts = 0, bones = 0, smrs = 0;
        foreach (var sk in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smrs++; tris += sk.sharedMesh.triangles.Length / 3; verts += sk.sharedMesh.vertexCount; bones = Mathf.Max(bones, sk.bones.Length);
            Log($"skinned {sk.name}: {sk.sharedMesh.triangles.Length / 3} tris, {sk.sharedMesh.vertexCount} verts, {sk.bones.Length} bones, {sk.sharedMaterials.Length} materials, blend shapes {sk.sharedMesh.blendShapeCount}");
        }
        var b = new Bounds(); bool first = true;
        foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        Log($"{(old ? "old rider" : "hero")}: {smrs} skinned meshes, {tris} tris, {verts} verts; height {b.size.y:0.00} m; materials: {body.MaterialNames()}");
        if (body.Hero != null) Log($"hero clips: {body.Hero.ClipCount}");
    }

    // ---- per frame cost of posing the body (editor, Mac: compare old vs new, not absolute iPhone numbers)
    static void Timing()
    {
        RideFor(1.0f, () => Mathf.Sin(step * Dt * 2f));
        var m = typeof(RiderBody).GetMethod(old ? "PoseRig" : "PoseHero", NP);
        var args = new object[] { 1f / 60f };
        for (int i = 0; i < 200; i++) m.Invoke(body, args);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int n = 3000;
        for (int i = 0; i < n; i++) { rider.Steer = Mathf.Sin(i * 0.01f); m.Invoke(body, args); }
        sw.Stop();
        Log($"posing cost ({(old ? "old RiderRig" : "hero clips + procedural layer")}): {sw.Elapsed.TotalMilliseconds * 1000.0 / n:0.0} us per frame (editor, reflection call included)");
    }

    // ---- riding: a slalom of heel- and toe-side carves, then a straight run into a tuck
    static void Carve()
    {
        Reset(Course.First + 40f, 16f);
        RideFor(1.2f, () => 0f);
        var seq = Seq("carve");
        int f = 0;
        for (float t = 0f; t < 5.0f; t += Dt)
        {
            rider.Steer = Mathf.Clamp(Mathf.Sin(t * 1.6f * Mathf.PI) * 1.4f, -1f, 1f);
            Step();
            if (step % 4 == 0) { Side(1); Shoot(seq + "/" + (f++).ToString("D3"), cam, 540, 720, false); late.Invoke(chase, null); }
            if (Mathf.Abs(t - 1.55f) < Dt * 0.5f) { late.Invoke(chase, null); Shoot("CarveToeGame", cam, 590, 1278); Side(1); Shoot("CarveToe", cam, 720, 900); late.Invoke(chase, null); }
            if (Mathf.Abs(t - 2.8f) < Dt * 0.5f) { late.Invoke(chase, null); Shoot("CarveHeelGame", cam, 590, 1278); Side(1); Shoot("CarveHeel", cam, 720, 900); late.Invoke(chase, null); }
        }
        // straight down in a tuck: the game tucks when you hold still at speed
        for (float t = 0f; t < 2.5f; t += Dt) { rider.Steer = Hold(); Step(); }
        Log($"tuck={rider.Tuck:0.00} speed={rider.Speed * 3.6f:0} km/h");
        Side(1); Shoot("Tuck", cam, 720, 900); late.Invoke(chase, null);
        Shoot("TuckGame", cam, 590, 1278);
        SilhouetteShot("SilhouetteGame");
    }

    // ---- a jump: pop, a grab held through the air, the landing
    static void Jump()
    {
        string[] grabs = { "Indy", "Melon", "Mute", "Method", "Stalefish", "TailGrab" };
        for (int gi = 0; gi < (old ? 1 : 6); gi++)
        {
            Reset(Course.First + 110f, 18f);
            RideFor(1f, Hold);
            rider.AddSuperJump(10f);
            rider.QueuePop(0.8f);
            string seq = gi == 0 ? Seq("jump") : null;
            int f = 0; bool wasAir = false, shotPeak = false; float landedAt = -1f;
            for (float t = 0f; t < 6f; t += Dt)
            {
                rider.Steer = 0f;
                Step();
                if (rider.Airborne && !wasAir)
                {
                    // a straight air with this grab (the game picks one at random)
                    var tp = typeof(Rider);
                    tp.GetProperty("TrickSpin").SetValue(rider, 0f); tp.GetProperty("TrickFlip").SetValue(rider, 0f);
                    tp.GetProperty("TrickGrab").SetValue(rider, gi + 1); tp.GetProperty("TrickName").SetValue(rider, grabs[gi]);
                    Log($"takeoff: {grabs[gi]} planned air {rider.TrickTime:0.00}s");
                }
                if (rider.Airborne && !shotPeak && rider.TrickProgress > 0.45f)
                {
                    shotPeak = true;
                    Side(gi % 2 == 0 ? 1 : -1, 4.2f); Shoot("Grab" + grabs[gi], cam, 720, 900);
                    late.Invoke(chase, null); if (gi == 0) Shoot("GrabGame", cam, 590, 1278);
                }
                if (wasAir && !rider.Airborne) landedAt = t;
                wasAir = rider.Airborne;
                if (seq != null && step % 4 == 0) { Side(1, 5f); Shoot(seq + "/" + (f++).ToString("D3"), cam, 540, 720, false); late.Invoke(chase, null); }
                if (landedAt > 0f && t > landedAt + 0.9f) break;
                if (rider.Crashed) { Log("crashed on the jump"); break; }
            }
        }
        if (old) return;
        // a spin and a flip, a frame each at the middle
        foreach (var (name, spin, flip) in new[] { ("Spin360", Mathf.PI * 2f, 0f), ("Backflip", 0f, -Mathf.PI * 2f) })
        {
            Reset(Course.First + 110f, 18f);
            RideFor(1f, Hold);
            rider.AddSuperJump(10f); rider.QueuePop(0.8f);
            bool wasAir = false, shot = false;
            for (float t = 0f; t < 6f && !shot; t += Dt)
            {
                rider.Steer = 0f; Step();
                if (rider.Airborne && !wasAir)
                {
                    var tp = typeof(Rider);
                    tp.GetProperty("TrickSpin").SetValue(rider, spin); tp.GetProperty("TrickFlip").SetValue(rider, flip);
                    tp.GetProperty("TrickGrab").SetValue(rider, 0); tp.GetProperty("TrickName").SetValue(rider, name);
                }
                wasAir = rider.Airborne;
                if (rider.Airborne && rider.TrickProgress > 0.06f && rider.TrickProgress < 0.08f) { Side(1, 4.5f); Shoot(name + "Early", cam, 720, 900); }
                if (rider.Airborne && rider.TrickProgress > 0.4f) { Side(1, 4.5f); Shoot(name, cam, 720, 900); shot = true; }
            }
        }
    }

    static void Tube()
    {
        Reset(Course.First + 60f, 14f);
        body.SetTube(true); rider.Tube = true;
        RideFor(1.5f, () => Mathf.Sin(step * Dt * 2f) * 0.8f);
        Side(1, 3.6f); Shoot("Tube", cam, 720, 900);
        body.SetTube(false); rider.Tube = false;
    }

    static void Crash()
    {
        Reset(Course.First + 60f, 16f);
        RideFor(1.0f, Hold);
        rider.DoCrash("test");
        float t = 0f; int shot = 0; float[] at = { 0.2f, 0.6f, 1.4f };
        while (shot < at.Length && t < 4f)
        {
            rider.Steer = 0f; Step(); t += Dt;
            if (step % 30 == 0) Log($"crash t={t:0.00} snow={(body.snowy != null ? body.snowy.Amount : -1f):0.000} ragdoll speed={(body.RagdollForTest != null ? body.RagdollForTest.Speed : 0f):0.0} below snow={body.RagdollBelowSnow:0.00} dead={body.Dead}");
            if (t >= at[shot])
            {
                Vector3 c = body.RagdollCentre;
                Look(c + new Vector3(3.2f, 1.4f, 2.4f), c, 40f);
                Shoot("Crash" + shot, cam, 720, 900); shot++;
            }
        }
        Log($"ragdoll active={body.RagdollForTest != null && body.RagdollForTest.Active} centre={body.RagdollCentre}");
        // up again: the bones go home and the hero rides on
        rider.RequestUp = true;
        for (float u = 0f; u < 3f && rider.Crashed; u += Dt) { rider.RequestUp = true; Step(); }
        RideFor(1.0f, Hold);
        Side(1); Shoot("AfterCrash", cam, 720, 900);
        var bands = (System.Collections.IList)typeof(RiderBody).GetField("bandages", NP).GetValue(body);
        Log($"after crash: crashed={rider.Crashed} bandage pieces={bands.Count} snow={(body.snowy != null ? body.snowy.Amount : -1f):0.00}");
        // the board rack's colours reach the jacket and the board
        body.SetColours(new Color(0.15f, 0.55f, 0.35f), new Color(0.95f, 0.8f, 0.1f));
        RideFor(0.3f, Hold);
        Side(-1); Shoot("Colours", cam, 720, 900);
    }

    // ---- the camp: standing, walking, running (the board in the right hand)
    static void Camp_()
    {
        var aCam = typeof(Camp).GetMethod("CameraStep", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        camp.SetNight(0f);
        camp.Teleport(-4f, -88f, 90f);
        for (int i = 0; i < 60; i++) CampStep(Vector2.zero);
        Shoot("CampStandGame", cam, 590, 1278);
        CampSide(); Shoot("CampStand", cam, 720, 900);
        var seq = Seq("camp");
        int f = 0;
        Vector3 prev = camp.WalkerPosition;
        for (float t = 0f; t < 6f; t += 1f / 60f)
        {
            float mag = t < 0.6f ? t / 0.6f * 0.8f : t < 3f ? 0.8f : 1f;
            CampStep(new Vector2(0f, mag));
            if (Mathf.RoundToInt(t * 60f) % 2 == 0) { CampSide(); Shoot(seq + "/" + (f++).ToString("D3"), cam, 540, 720, false); }
            if (Mathf.Abs(t - 2.5f) < 0.008f) { CampSide(); Shoot("CampWalk", cam, 720, 900); }
            if (Mathf.Abs(t - 5.5f) < 0.008f) { CampSide(); Shoot("CampRun", cam, 720, 900); }
        }
        Log($"camp: walked {(camp.WalkerPosition - prev).magnitude:0.0} m, footprints {camp.FootprintCount}");
        for (int i = 0; i < 30; i++) CampStep(new Vector2(0f, 1f));
        Shoot("CampRunGame", cam, 590, 1278);
    }

    static Vector3 campDir = Vector3.forward, lastWalker;
    static void CampStep(Vector2 stick)
    {
        camp.Walk(stick, 1f / 60f);
        body.PhysicsStep();
        body.Apply(1f / 60f);
        camp.CameraStep(1f / 60f);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        aLate.Invoke(atmo, null);
        var p = camp.WalkerPosition; var d = p - lastWalker; d.y = 0f;
        if (d.sqrMagnitude > 1e-6f) campDir = Vector3.Slerp(campDir, d.normalized, 0.1f);
        lastWalker = p;
    }

    static void CampSide()
    {
        var p = camp.WalkerPosition;
        var right = -Vector3.Cross(Vector3.up, campDir).normalized;   // the left side: the board hangs on the right
        Look(p + right * 3.4f + campDir * 1.2f + Vector3.up * 1.0f, p + Vector3.up * 0.85f, 34f);
    }

    // ---- riding helpers
    static void Reset(float z, float speed)
    {
        if (rider.Crashed) { rider.RequestUp = true; for (int i = 0; i < 600 && rider.Crashed; i++) { rider.RequestUp = true; Step(); } }
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * speed;
        terrain.Prewarm(); props.Prewarm(); chase.Snap();
    }

    static float Hold() => Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);

    static void RideFor(float secs, System.Func<float> steer) { for (float t = 0f; t < secs; t += Dt) { rider.Steer = steer(); Step(); } }

    static void Step()
    {
        rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (step % 4 == 0)
        {
            body.Apply(4 * Dt);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.name != "Snowfall") ps.Simulate(4 * Dt, false, false, false);
            late.Invoke(chase, null);
            aLate.Invoke(atmo, null);
        }
        step++;
    }

    static string Seq(string name) { var d = Path.Combine(outDir, pre + "seq_" + name); Directory.CreateDirectory(d); return pre + "seq_" + name; }

    // a camera alongside the rider on the toe side (side 1) or the heel side (-1), a little ahead
    static Vector3 sideDir; static bool sideInit;
    static void Side(int side, float dist = 4.0f)
    {
        Vector3 v = rider.Velocity; v.y = 0f;
        Vector3 fwd = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd) * side;
        sideDir = sideInit ? Vector3.Slerp(sideDir, right, 0.15f) : right; sideInit = true;
        Vector3 p = rider.transform.position + Vector3.up * 0.25f;
        Look(p + sideDir * dist + fwd * 1.6f + Vector3.up * 0.9f, p + fwd * 0.3f, 36f);
    }

    static void Look(Vector3 from, Vector3 at, float fov)
    {
        cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov;
        closeUp = true;
    }
    static bool closeUp;

    /// <summary>The game view with every model in flat grey: does the rider's shape read against the snow?</summary>
    static void SilhouetteShot(string name)
    {
        var grey = new Material(Shader.Find("Universal Render Pipeline/Unlit")); grey.SetColor("_BaseColor", new Color(0.25f, 0.27f, 0.3f));
        var rs = new List<(Renderer, Material[])>();
        foreach (var r in body.GetComponentsInChildren<Renderer>()) { if (r is ParticleSystemRenderer) continue; rs.Add((r, r.sharedMaterials)); var m = new Material[r.sharedMaterials.Length]; for (int i = 0; i < m.Length; i++) m[i] = grey; r.sharedMaterials = m; }
        late.Invoke(chase, null);
        Shoot(name, cam, 590, 1278);
        foreach (var (r, m) in rs) r.sharedMaterials = m;
    }

    static void Shoot(string file, Camera c, int w, int h, bool log = true)
    {
        // close-ups from beside the rider: the forest between would fill the frame, so it sits this one out
        bool hide = closeUp && props != null && props.gameObject.activeSelf;
        if (hide) props.gameObject.SetActive(false);
        closeUp = false;
        try { ShootNow(file, c, w, h, log); } finally { if (hide) props.gameObject.SetActive(true); }
    }

    static void ShootNow(string file, Camera c, int w, int h, bool log)
    {
        var rt = new RenderTexture(w, h, 24);
        c.targetTexture = rt; c.aspect = w / (float)h;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g);
        }
        c.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        string path = Path.Combine(outDir, (file.Contains("/") ? "" : pre) + file + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        c.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        if (log) Log($"{path} lean={rider.Lean:0.00} steer={rider.Steer:0.00} air={rider.Airborne} trick={rider.TrickName} grab={rider.TrickGrab} k={rider.TrickProgress:0.00}");
    }
}
