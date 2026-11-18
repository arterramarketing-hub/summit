using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The two kinds of crevasse, through the game's own wiring: a down-slope slot and a glacier gap across the run
/// (its dark lip, its flags, a snow bridge), whether a gap can be ollied at normal speeds, and a body fallen into
/// each, lying against the ice inside. Run: Unity -batchmode -projectPath . -executeMethod GlacierShot.Run
/// -logFile x.log (no -nographics: it needs the GPU). Writes GlacierShot*.png in the project folder.
/// </summary>
public static class GlacierShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;

    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static CourseDressing dress;
    static Camera cam; static ChaseCamera chase; static MethodInfo late, tUpd, fUpd; static Atmosphere atmo;
    static string lastCrash = "";
    static int bad;

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
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        fUpd = typeof(Forest).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        rider.Crash += why => lastCrash = why;
        try { Sequence(); }
        catch (System.Exception e) { Debug.LogError("GLACIERSHOT exception " + e); }
        Log($"done, {bad} problems");
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void Sequence()
    {
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        // the mix down the run
        int along = 0, across = 0, bridges = 0;
        for (int c = 0; c < 12; c++)
            foreach (var cv in Course.CrevassesOf(c))
            {
                if (cv.across) { across++; if (cv.bridgeHalf > 0f) bridges++; Log($"c{c} gap z={cv.zc:0} x {cv.x0 - cv.x:0}..{cv.x1 - cv.x:+0} off centre, {2f * cv.half:0.0} m wide, bridge={(cv.bridgeHalf > 0f ? $"{cv.bridge - cv.x:0} ({2f * cv.bridgeHalf:0.0} m)" : "none")}"); }
                else { along++; Log($"c{c} slot z={cv.z0:0}..{cv.z1:0} x={cv.x - Slope.Centre(cv.z0):0.0} off centre, {2f * cv.half:0.0} m wide"); }
            }
        Log($"first 12 cycles: {along} down the slope, {across} across ({bridges} with a bridge)");

        Course.Crevasse slot = default, gap = default, bridged = default;
        bool haveBridge = false;
        foreach (var cv in Course.CrevassesOf(0)) { if (cv.across) gap = cv; else slot = cv; }
        for (int c = 0; c < 12 && !haveBridge; c++)
            foreach (var cv in Course.CrevassesOf(c)) if (cv.across && cv.bridgeHalf > 0f) { bridged = cv; haveBridge = true; break; }

        // ---- warm up, then the gap from up the hill (rider's eye, a little high) and from its side
        Place(gap.x, gap.zc - 40f);
        Shoot("GlacierShotWarmup.png", false);
        Course.Opening(gap, gap.x, out float gz);
        // what the player sees riding at it: the game's own camera, 30 m and 15 m out at 15 m/s
        foreach (float d in new[] { 30f, 15f })
        {
            rider.ResetAt(OffBridge(gap, gap.x + 4f), gz - d - 3f); body.ResetVisual(); rider.Begin();
            rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(gap.x, gz - d)).normalized * 15f;
            terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
            chase.Snap();
            for (int i = 0; i < 12; i++) { rider.Steer = 0f; Step(); }
            Shoot($"GlacierShotGapApproach{d:0}.png", true);
        }
        var profile = "";
        for (float zz = gz - 40f; zz <= gz + 20f; zz += 5f) profile += $" {zz - gz:0}:{Slope.Surface(Slope.Centre(zz), zz) - Slope.Surface(Slope.Centre(gz), gz):0.0}";
        Log("ground along the piste's middle round the gap (m from it: height):" + profile);
        Vector3 eye;
        Place(gap.x, gap.zc - 40f);
        eye = new Vector3(gap.x + 9f, Slope.Surface(gap.x + 9f, gz - 7f) + 4.5f, gz - 7f);
        Look(eye, new Vector3(gap.x - 4f, Slope.Surface(gap.x - 4f, gz) - 7f, gz), 60f);
        Shoot("GlacierShotGapSide.png", false);
        eye = new Vector3(gap.x + 1f, Slope.Surface(gap.x + 1f, gz - 6f) + 3f, gz - 6f);
        Look(eye, new Vector3(gap.x, Slope.Surface(gap.x, gz - 2f), gz - 1f), 60f);
        Shoot("GlacierShotGapLip.png", false);
        eye = new Vector3(gap.x, Slope.Surface(gap.x, gz - 60f) + 22f, gz - 60f);
        Look(eye, new Vector3(gap.x, Slope.Surface(gap.x, gz), gz + 10f), 70f);
        Shoot("GlacierShotGapWide.png", false);

        // ---- the down-slope slot, from beside its rim and from above its top end
        {
            float zm = (slot.z0 + slot.z1) * 0.5f;
            float open = Course.Opening(slot, zm, out float cx);
            Place(cx + 10f, zm - 6f);
            eye = new Vector3(cx + open + 3.5f, Slope.Surface(cx + open + 3.5f, zm - 7f) + 3.2f, zm - 7f);
            Look(eye, new Vector3(cx, Slope.Surface(cx, zm) - 5f, zm + 2f), 60f);
            Shoot("GlacierShotSlot.png", false);
            eye = new Vector3(cx + 1f, Slope.Surface(cx, slot.z0 - 8f) + 7f, slot.z0 - 8f);
            Look(eye, new Vector3(cx, Slope.Surface(cx, zm) - 8f, zm), 60f);
            Shoot("GlacierShotSlotAbove.png", false);
        }

        // ---- a snow bridge, with its wands
        if (haveBridge)
        {
            Course.Opening(bridged, bridged.bridge, out float bz);
            Place(bridged.bridge, bz - 30f);
            eye = new Vector3(bridged.bridge - 4f, Slope.Surface(bridged.bridge - 4f, bz - 20f) + 3.5f, bz - 20f);
            Look(eye, new Vector3(bridged.bridge, Slope.Surface(bridged.bridge, bz), bz + 3f), 55f);
            Shoot("GlacierShotBridge.png", false);
            // ridden straight over it: no fall
            bool fell = RideAt(bridged, bridged.bridge, bz, 16f, -1f, 3f);
            Log($"over the bridge at 16 m/s: {(fell ? "FELL IN (" + lastCrash + ")" : "across")}");
            if (fell) bad++;
        }

        // ---- can a gap be cleared? Speeds from slow to fast, the jump at a few distances before the lip
        foreach (float v in new[] { 10f, 14f, 18f, 24f, 30f })
        {
            string s = "";
            foreach (float before in new[] { 1f, 3f, 6f })
            {
                bool fell = RideAt(gap, OffBridge(gap, gap.x + 2f), gz, v, before, 3f);
                s += $" pop {before:0}m before: {(fell ? "fell" : "cleared")};";
            }
            bool plain = RideAt(gap, OffBridge(gap, gap.x + 2f), gz, v, -1f, 3f);
            Log($"gap at {v:0} m/s:{s} no pop: {(plain ? "fell" : "RODE OVER")}");
            if (!plain) bad++;
        }

        // ---- falls: into the gap, and into the slot. The body must come to rest inside, on the ice
        Fall("Gap", gap, OffBridge(gap, gap.x + 2f), gz, 12f);
        Fall("GapFast", gap, OffBridge(gap, gap.x - 3f), gz, 24f);
        {
            float zm = (slot.z0 + slot.z1) * 0.5f;
            Course.Opening(slot, zm, out float cx);
            Fall("Slot", slot, cx, zm, 8f);
        }
    }

    /// <summary>Ride straight down at (x, across z) at speed v, popping `before` metres before the lip (-1: no pop). True if it fell in.</summary>
    static bool RideAt(Course.Crevasse cv, float x, float zc, float v, float before, float pastLip)
    {
        float z0 = zc - 25f;
        rider.ResetAt(x, z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z0)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        lastCrash = "";
        bool popped = false;
        for (float t = 0f; t < 6f && !rider.Crashed && rider.Z < zc + pastLip + 6f; t += Dt)
        {
            Course.Opening(cv, rider.transform.position.x, out float cz);
            float lip = cz - cv.half;
            if (!popped && before >= 0f && rider.Z > lip - before) { rider.QueuePop(0.5f); popped = true; }
            rider.Steer = Mathf.Clamp(-rider.Heading * 0.8f, -1f, 1f);
            for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
        }
        bool fell = rider.Crashed && lastCrash.StartsWith("Crevasse");
        if (rider.Crashed && !fell) Log($"  (crashed: {lastCrash})");
        return fell;
    }

    static void Fall(string name, Course.Crevasse cv, float x, float zc, float v)
    {
        float z0 = cv.across ? zc - 12f : zc - 4f;
        rider.ResetAt(x, z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z0)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        lastCrash = "";
        float minY = 1e9f, t = 0f;
        Vector3 first = Vector3.zero; bool shotFalling = false;
        for (; t < 12f; t += Dt)
        {
            rider.Steer = 0f; Step();
            var c = body.RagdollCentre;
            if (rider.Crashed) minY = Mathf.Min(minY, c.y);
            if (rider.Crashed && first == Vector3.zero) first = rider.transform.position;
            if (!shotFalling && body.RagdollBelowSnow > 6f)
            {
                shotFalling = true;
                chase.enabled = true; chase.peek = 1f; chase.peekAt = c; late.Invoke(chase, null);
                Shoot($"GlacierShot{name}Falling.png", true);
            }
            if (rider.Crashed && t > 3f && !body.RagdollMoving) break;
        }
        var rc = body.RagdollCentre;
        float along = cv.Along(rc.x, rc.z);
        float open = Course.Opening(cv, along, out float ctr);
        float off = cv.Across(rc.x, rc.z) - ctr;
        float surface = Slope.Surface(rc.x, rc.z), floor = surface - Course.CrevasseCut(rc.x, rc.z);
        float depth = Course.CrevasseDepth * Mathf.Sqrt(Mathf.Max(0f, open) / cv.half) - 1.5f;
        Log($"{name}: crash '{lastCrash}' at {first}; after {t:0.0}s the body lies {surface - rc.y:0.0} m down (crevasse {depth:0.0} m deep here, lowest {surface - minY:0.0}), " +
            $"{off:0.00} m off its middle (open {open:0.00}), moving={body.RagdollMoving}, belowSnow={body.RagdollBelowSnow:0.0}");
        if (!lastCrash.StartsWith("Crevasse")) { Log($"{name}: PROBLEM did not fall in"); bad++; }
        if (surface - rc.y > depth + 1.2f) { Log($"{name}: PROBLEM fell through the floor"); bad++; }
        if (Mathf.Abs(off) > open + 0.3f) { Log($"{name}: PROBLEM lies outside the walls"); bad++; }
        if (body.RagdollMoving) { Log($"{name}: PROBLEM still moving"); bad++; }
        // the game's own look down after it, then a look along the inside at the body
        chase.enabled = true; chase.peek = 1f; chase.peekAt = rc; late.Invoke(chase, null);
        Shoot($"GlacierShot{name}Peek.png", true);
        // inside, on its middle line a little along from the body, part way down, looking down at it
        Look(InsideEye(cv, rc, 4f, 0.5f), rc, 60f);
        Shoot($"GlacierShot{name}Inside.png", true);
        Look(InsideEye(cv, rc, -3f, 0.72f), rc, 60f);
        Shoot($"GlacierShot{name}Inside2.png", true);
        chase.peek = 0f;
    }

    /// <summary>A point on the crevasse's middle line `along` metres on from p, `frac` of p's depth below the snow there.</summary>
    static Vector3 InsideEye(Course.Crevasse cv, Vector3 p, float along, float frac)
    {
        float below = Slope.Surface(p.x, p.z) - p.y;
        float u = cv.Along(p.x, p.z) + along;
        Course.Opening(cv, u, out float ctr);
        var q = cv.Point(u, ctr);
        return new Vector3(q.x, Slope.Surface(q.x, q.y) - below * frac, q.y);
    }

    /// <summary>x, or the nearest spot well clear of the gap's snow bridge.</summary>
    static float OffBridge(Course.Crevasse cv, float x)
    {
        if (cv.bridgeHalf <= 0f || Mathf.Abs(x - cv.bridge) > cv.bridgeHalf + 4f) return x;
        return cv.bridge + Mathf.Sign(x - cv.bridge + 0.01f) * (cv.bridgeHalf + 5f);
    }

    static void Place(float x, float z)
    {
        rider.ResetAt(x, z); body.ResetVisual(); rider.Stop();
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
    }

    static void Step()
    {
        for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
        tUpd.Invoke(terrain, null);
        fUpd.Invoke(props, null);
        body.Apply(Dt);
        chase.enabled = true;
        late.Invoke(chase, null);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
    }

    static void Look(Vector3 from, Vector3 to, float fov)
    {
        chase.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
    }

    static void Log(string s) => Debug.Log("GLACIERSHOT " + s);

    static void Shoot(string file, bool bakeSkins)
    {
        DynamicGI.UpdateEnvironment();
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
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
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }
}
