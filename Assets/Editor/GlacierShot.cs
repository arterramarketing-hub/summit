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

    public static void Run() => Boot(Sequence);

    /// <summary>
    /// Head-first falls into both kinds of crevasse, watching the snow layer round the lip (it must not heap up or
    /// be carved down into the hole), and a fall on open snow as the control (that must still leave its hole and
    /// rims). Run: -executeMethod GlacierShot.Snow. Writes CrevSnow*.png.
    /// </summary>
    public static void Snow() => Boot(SnowSequence);

    static void Boot(System.Action seq)
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
        try { seq(); }
        catch (System.Exception e) { Debug.LogError("GLACIERSHOT exception " + e); }
        Log($"done, {bad} problems");
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void SnowSequence()
    {
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        Course.Crevasse slot = default, gap = default;
        foreach (var cv in Course.CrevassesOf(0)) { if (cv.across) gap = cv; else slot = cv; }
        Course.Opening(gap, gap.x, out float gz);
        float zm = (slot.z0 + slot.z1) * 0.5f;
        Course.Opening(slot, zm, out float sx);
        Place(gap.x, gz - 40f);
        Shoot("CrevSnowWarmup.png", false);
        int deepHits = 0; float deepHitMax = 0f;
        body.RagdollHit += (v, board) => { if (body.RagdollBelowSnow > 1f) { deepHits++; deepHitMax = Mathf.Max(deepHitMax, v); } };
        foreach (var run in new[] { ("Gap", gap, OffBridge(gap, gap.x + 2f), gz - 12f, 16f), ("GapFast", gap, OffBridge(gap, gap.x - 6f), gz - 14f, 26f),
                                    ("SlotEnd", slot, sx, slot.z0 - 5f, 14f) })
        {
            deepHits = 0; deepHitMax = 0f;
            HeadFirst(run.Item1, run.Item2, run.Item3, run.Item4, run.Item5);
            Log($"{run.Item1}: {deepHits} hits on the ice down the crevasse (hardest {deepHitMax:0.0} m/s)");
            if (run.Item1 == "Gap")
            {
                // a mark in the snow layer's patch switches the layer on: the crevasse must still show as a hole
                var c = body.RagdollCentre; Course.Opening(gap, c.x, out float czc);
                var m = new Vector3(c.x + 1.5f, 0f, czc - gap.half - 4f); m.y = Slope.DrawnHeight(m.x, m.z);
                SnowLayer.Instance?.Press(m - Vector3.right * 0.5f, m + Vector3.right * 0.5f, 0.3f, 0.3f, Vector3.forward, 0.3f, 1f);
                SnowLayer.Instance?.Flush();
                LipShots("GapMarked", "", gap, new Vector3(c.x, 0f, czc - gap.half));
            }
        }
        // the control: thrown on open snow, the body still leaves its furrow and rims
        float z0 = Course.First + 30f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z0), z0)).normalized * 18f;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        for (int i = 0; i < 40; i++) { rider.Steer = 0f; Step(); }
        var before = rider.transform.position;
        rider.DoCrash("test");
        for (float t = 0f; t < 4f; t += Dt) { rider.Steer = 0f; Step(); }
        SnowStats(body.RagdollCentre, 10f, null, out float hi, out float lo);
        Log($"Open snow: heaps up to {hi:0.00} m, dug down to {-lo:0.00} m round where the body went");
        if (hi < 0.02f || lo > -0.05f) { Log("Open snow: PROBLEM no furrow or rims on open snow"); bad++; }
        var rc = body.RagdollCentre;
        Look(rc + new Vector3(2.5f, 2f, -3.5f), rc, 50f);
        Shoot("CrevSnowOpen.png", true);
    }

    /// <summary>Ridden into a crevasse at speed v from (x, z0), straight down the hill: it goes in head first.</summary>
    static void HeadFirst(string name, Course.Crevasse cv, float x, float z0, float v)
    {
        rider.ResetAt(x, z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z0)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        lastCrash = "";
        Vector3 at = Vector3.zero; float tCrash = -1f, worstHi = 0f, worstLo = 0f;
        int shot = 0; float[] when = { 0.25f, 0.7f, 1.5f };
        for (float t = 0f; t < 9f; t += Dt)
        {
            rider.Steer = 0f; Step();
            if (rider.Crashed && tCrash < 0f) { tCrash = t; at = rider.transform.position; }
            if (tCrash < 0f) continue;
            if (Mathf.Repeat(t, 0.1f) < Dt) { SnowStats(at, 7f, cv, out float hi, out float lo); worstHi = Mathf.Max(worstHi, hi); worstLo = Mathf.Min(worstLo, lo); }
            if (shot < when.Length && t - tCrash >= when[shot]) { LipShots(name, $"T{shot}", cv, at); shot++; }
            if (t - tCrash > 3f && !body.RagdollMoving) break;
        }
        SnowStats(at, 7f, cv, out float h1, out float l1);
        Log($"{name}: '{lastCrash}' at {at}; snow round the lip during the fall: up {worstHi:0.00} m, down {-worstLo:0.00} m; after: up {h1:0.00} m, down {-l1:0.00} m; body {body.RagdollBelowSnow:0.0} m down");
        if (worstHi > 0.03f || worstLo < -0.05f) { Log($"{name}: PROBLEM the snow by the crevasse moved"); bad++; }
        LipShots(name, "After", cv, at);
    }

    static void LipShots(string name, string when, Course.Crevasse cv, Vector3 at)
    {
        // from beside and up the hill of where it went in, looking at the lip; and the game's own look down after it
        Vector3 side = cv.across ? Vector3.right : Vector3.right;
        Vector3 eye = at + side * 3.5f + new Vector3(0f, 0f, -5f);
        eye.y = Slope.Surface(eye.x, eye.z) + 2.6f;
        Look(eye, new Vector3(at.x, Slope.Surface(at.x, at.z) - 1.5f, at.z + 1f), 55f);
        Shoot($"CrevSnow{name}{when}.png", true);
        chase.enabled = true; chase.peek = 1f; chase.peekAt = body.RagdollCentre; late.Invoke(chase, null);
        Shoot($"CrevSnow{name}{when}Peek.png", true);
        chase.peek = 0f;
    }

    /// <summary>How far the snow layer has been raised (hi) and dug (lo) round p, off the crevasse's hole if one is given.</summary>
    static void SnowStats(Vector3 p, float radius, Course.Crevasse? cv, out float hi, out float lo)
    {
        hi = 0f; lo = 0f;
        var sl = SnowLayer.Instance;
        if (sl == null) return;
        for (float dz = -radius; dz <= radius; dz += 0.125f)
            for (float dx = -radius; dx <= radius; dx += 0.125f)
            {
                float x = p.x + dx, z = p.z + dz;
                if (Course.CrevasseCut(x, z) > 0.25f) continue;   // over the hole: the layer is not drawn there
                float d = sl.SurfaceAt(x, z) - Slope.DrawnHeight(x, z) - 0.006f;
                hi = Mathf.Max(hi, d); lo = Mathf.Min(lo, d);
            }
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
        // whichever side of it is nearer the middle of the piste (the other may be off among the rocks)
        float a = cv.bridge - (cv.bridgeHalf + 5f), b = cv.bridge + (cv.bridgeHalf + 5f);
        return Mathf.Abs(a - cv.x) < Mathf.Abs(b - cv.x) ? a : b;
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
