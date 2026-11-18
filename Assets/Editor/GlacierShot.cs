using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The crevasses (long slots down the slope), through the game's own wiring: their layout and the lanes past them,
/// renders, a ride down the middle lane, and a body fallen into one lying against the ice inside. Run: Unity
/// -batchmode -projectPath . -executeMethod GlacierShot.Run -logFile x.log (no -nographics: it needs the GPU).
/// Writes GlacierShot*.png in the project folder. GlacierShot.Snow: head-first falls and the snow at the lip;
/// GlacierShot.Park: the park's rails and boxes and a grind on each.
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
    /// Head-first falls into a crevasse, watching the snow layer round the lip (it must not heap up or
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
        var slot = Course.CrevassesOf(0)[0];
        float zm = (slot.z0 + slot.z1) * 0.5f;
        Course.Opening(slot, zm, out float sx);
        Place(sx, zm - 40f);
        Shoot("CrevSnowWarmup.png", false);
        // head first into its top end, and in from the side, steered across it
        HeadFirst("SlotEnd", slot, sx, slot.z0 - 5f, 14f, 0f);
        HeadFirst("SlotSide", slot, sx - Mathf.Sign(sx - Slope.Centre(zm)) * 7f, zm - 12f, 16f, Mathf.Sign(sx - Slope.Centre(zm)));
        // the control: thrown on open snow, the body still leaves its furrow and rims
        float z0 = Course.First + 30f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z0), z0)).normalized * 18f;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        for (int i = 0; i < 40; i++) { rider.Steer = 0f; Step(); }
        rider.DoCrash("test");
        for (float t = 0f; t < 4f; t += Dt) { rider.Steer = 0f; Step(); }
        SnowStats(body.RagdollCentre, 10f, out float hi, out float lo);
        Log($"Open snow: heaps up to {hi:0.00} m, dug down to {-lo:0.00} m round where the body went");
        if (hi < 0.02f || lo > -0.05f) { Log("Open snow: PROBLEM no furrow or rims on open snow"); bad++; }
        var rc = body.RagdollCentre;
        Look(rc + new Vector3(2.5f, 2f, -3.5f), rc, 50f);
        Shoot("CrevSnowOpen.png", true);
    }

    /// <summary>Ridden into a crevasse at speed v from (x, z0), steering `steer`: it goes in head first.</summary>
    static void HeadFirst(string name, Course.Crevasse cv, float x, float z0, float v, float steer)
    {
        rider.ResetAt(x, z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z0)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        lastCrash = "";
        Vector3 at = Vector3.zero; float tCrash = -1f, worstHi = 0f, worstLo = 0f;
        int shot = 0; float[] when = { 0.25f, 0.7f, 1.5f };
        for (float t = 0f; t < 9f; t += Dt)
        {
            rider.Steer = steer; Step();
            if (rider.Crashed && tCrash < 0f) { tCrash = t; at = rider.transform.position; }
            if (tCrash < 0f) continue;
            if (Mathf.Repeat(t, 0.1f) < Dt) { SnowStats(at, 7f, out float hi, out float lo); worstHi = Mathf.Max(worstHi, hi); worstLo = Mathf.Min(worstLo, lo); }
            if (shot < when.Length && t - tCrash >= when[shot]) { LipShots(name, $"T{shot}", at); shot++; }
            if (t - tCrash > 3f && !body.RagdollMoving) break;
        }
        SnowStats(at, 7f, out float h1, out float l1);
        Log($"{name}: '{lastCrash}' at {at}; snow round the lip during the fall: up {worstHi:0.00} m, down {-worstLo:0.00} m; after: up {h1:0.00} m, down {-l1:0.00} m; body {body.RagdollBelowSnow:0.0} m down");
        if (!lastCrash.StartsWith("Crevasse")) { Log($"{name}: PROBLEM did not fall in"); bad++; }
        if (worstHi > 0.03f || worstLo < -0.05f) { Log($"{name}: PROBLEM the snow by the crevasse moved"); bad++; }
        LipShots(name, "After", at);
    }

    static void LipShots(string name, string when, Vector3 at)
    {
        // from beside and up the hill of where it went in, looking at the lip; and the game's own look down after it
        Vector3 eye = at + new Vector3(3.5f, 0f, -5f);
        eye.y = Slope.Surface(eye.x, eye.z) + 2.6f;
        Look(eye, new Vector3(at.x, Slope.Surface(at.x, at.z) - 1.5f, at.z + 1f), 55f);
        Shoot($"CrevSnow{name}{when}.png", true);
        chase.enabled = true; chase.peek = 1f; chase.peekAt = body.RagdollCentre; late.Invoke(chase, null);
        Shoot($"CrevSnow{name}{when}Peek.png", true);
        chase.peek = 0f;
    }

    /// <summary>How far the snow layer has been raised (hi) and dug (lo) round p, off any crevasse's hole.</summary>
    static void SnowStats(Vector3 p, float radius, out float hi, out float lo)
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
        // the layout down the run: every crevasse a long slot down the slope, a lane down the middle and each edge
        float minLen = 1e9f, maxLen = 0f, minLane = 1e9f, minEdge = 1e9f;
        int n = 0;
        for (int c = 0; c < 12; c++)
        {
            var cs = Course.CrevassesOf(c);
            foreach (var cv in cs)
            {
                n++;
                minLen = Mathf.Min(minLen, cv.z1 - cv.z0); maxLen = Mathf.Max(maxLen, cv.z1 - cv.z0);
                for (float z = cv.z0; z < cv.z1; z += 2f)
                {
                    Course.Opening(cv, z, out float cx);
                    float off = Mathf.Abs(cx - Slope.Centre(z));
                    minEdge = Mathf.Min(minEdge, Slope.PisteHalf - off - cv.half);
                    minLane = Mathf.Min(minLane, off - cv.half);   // to the middle of the piste
                }
                Log($"c{c} slot z={cv.z0:0}..{cv.z1:0} ({cv.z1 - cv.z0:0} m) x={cv.x - Slope.Centre((cv.z0 + cv.z1) * 0.5f):0.0} off centre, {2f * cv.half:0.0} m wide");
            }
        }
        Log($"{n} slots, {minLen:0}..{maxLen:0} m long; at least {minLane:0.0} m from the middle of the piste and {minEdge:0.0} m from its edge");
        if (minLen < 45f) { Log("PROBLEM a slot shorter than 45 m"); bad++; }
        if (minLane < 2.5f || minEdge < 2.5f) { Log("PROBLEM no clear lane past a slot"); bad++; }

        var slot = Course.CrevassesOf(0)[0];
        float zm = (slot.z0 + slot.z1) * 0.5f;
        float open = Course.Opening(slot, zm, out float sx);
        Place(sx, slot.z0 - 30f);
        Shoot("GlacierShotWarmup.png", false);
        // from beside the rim, from above its top end, and the whole stretch from high up the hill
        Place(sx + 10f, zm - 6f);
        var eye = new Vector3(sx + open + 3.5f, Slope.Surface(sx + open + 3.5f, zm - 7f) + 3.2f, zm - 7f);
        Look(eye, new Vector3(sx, Slope.Surface(sx, zm) - 5f, zm + 2f), 60f);
        Shoot("GlacierShotSlot.png", false);
        eye = new Vector3(sx + 1f, Slope.Surface(sx, slot.z0 - 8f) + 7f, slot.z0 - 8f);
        Look(eye, new Vector3(sx, Slope.Surface(sx, zm) - 8f, zm), 60f);
        Shoot("GlacierShotSlotAbove.png", false);
        float c0 = Slope.Centre(slot.z0 - 30f);
        Place(c0, slot.z0 - 30f);
        eye = new Vector3(c0, Slope.Surface(c0, slot.z0 - 40f) + 28f, slot.z0 - 40f);
        Look(eye, new Vector3(Slope.Centre(zm + 20f), Slope.Surface(Slope.Centre(zm + 20f), zm + 20f), zm + 20f), 70f);
        Shoot("GlacierShotSlotsWide.png", false);
        // the rider's own view riding down the middle lane between them
        rider.ResetAt(c0, slot.z0 - 25f); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(c0, slot.z0 - 25f)).normalized * 15f;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); chase.Snap();
        lastCrash = "";
        for (int i = 0; i < 60; i++) { rider.Steer = Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f); Step(); }
        Shoot("GlacierShotSlotsRide.png", true);
        for (float t = 0f; t < 8f && rider.Z < slot.z1 + 20f; t += Dt) { rider.Steer = Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f); Step(); }
        Log($"down the middle lane past the slots: {(rider.Crashed ? "CRASHED (" + lastCrash + ")" : "clear")} to z={rider.Z:0}");
        if (rider.Crashed) bad++;

        // a fall into a slot from its top end: the body must come to rest inside, on the ice
        Fall("Slot", slot, sx, slot.z0 - 4f, 8f);
    }

    static void Fall(string name, Course.Crevasse cv, float x, float z0, float v)
    {
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
        float open = Course.Opening(cv, rc.z, out float cx);
        float off = rc.x - cx;
        float surface = Slope.Surface(rc.x, rc.z);
        float depth = Course.CrevasseDepth * Mathf.Sqrt(Mathf.Max(0f, open) / cv.half) - 1.5f;
        Log($"{name}: crash '{lastCrash}' at {first}; after {t:0.0}s the body lies {surface - rc.y:0.0} m down (crevasse {depth:0.0} m deep here, lowest {surface - minY:0.0}), " +
            $"{off:0.00} m off its middle (open {open:0.00}), moving={body.RagdollMoving}, belowSnow={body.RagdollBelowSnow:0.0}");
        if (!lastCrash.StartsWith("Crevasse")) { Log($"{name}: PROBLEM did not fall in"); bad++; }
        if (surface - rc.y > depth + 1.2f) { Log($"{name}: PROBLEM fell through the floor"); bad++; }
        if (Mathf.Abs(off) > open + 0.3f) { Log($"{name}: PROBLEM lies outside the walls"); bad++; }
        if (body.RagdollMoving) { Log($"{name}: PROBLEM still moving"); bad++; }
        chase.enabled = true; chase.peek = 1f; chase.peekAt = rc; late.Invoke(chase, null);
        Shoot($"GlacierShot{name}Peek.png", true);
        Look(InsideEye(cv, rc, 4f, 0.5f), rc, 60f);
        Shoot($"GlacierShot{name}Inside.png", true);
        chase.peek = 0f;
    }

    /// <summary>A point on the crevasse's middle line `along` metres down from p, `frac` of p's depth below the snow there.</summary>
    static Vector3 InsideEye(Course.Crevasse cv, Vector3 p, float along, float frac)
    {
        float below = Slope.Surface(p.x, p.z) - p.y;
        float z = p.z + along;
        Course.Opening(cv, z, out float cx);
        return new Vector3(cx, Slope.Surface(cx, z) - below * frac, z);
    }

    // ---------------------------------------------------------------- the park

    /// <summary>
    /// The park's big rails and boxes: renders of each, a grind down each from the snow beside its start, and checks
    /// that they clear the snow, stay apart and keep trees and pickups off them. Run: -executeMethod GlacierShot.Park.
    /// Writes ParkShot*.png.
    /// </summary>
    public static void Park() => Boot(ParkSequence);

    static void ParkSequence()
    {
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        var fs = Course.FeaturesOf(0);
        // the layout: lengths, heights over the snow, no two features or a feature and a kicker side by side
        for (int c = 0; c < 4; c++)
            foreach (var f in Course.FeaturesOf(c))
            {
                float lo = 1e9f, hi = 0f;
                for (int i = 0; i <= 40; i++) { var q = Course.FeaturePoint(f, i / 40f); float cl = q.y - Slope.Height(q.x, q.z); lo = Mathf.Min(lo, cl); hi = Mathf.Max(hi, cl); }
                if (c == 0) Log($"c{c} {f.kind} {f.Length:0} m long at x={f.a.x - Slope.Centre(f.a.z):0.0}, z {f.a.z - Course.CycleBase(f.a.z) - Course.ParkAt:0}..{f.b.z - Course.CycleBase(f.a.z) - Course.ParkAt:0} of the park; {lo:0.00}..{hi:0.00} m over the snow");
                if (lo < f.height - 0.05f) { Log($"PROBLEM c{c} {f.kind} only {lo:0.00} m over the snow"); bad++; }
                if (hi > f.height + 1.6f) { Log($"PROBLEM c{c} {f.kind} stands {hi:0.00} m over the snow"); bad++; }
                if (f.b.z > Course.CycleBase(f.a.z) + Course.DangerAt) { Log($"PROBLEM c{c} {f.kind} runs out of the park"); bad++; }
                foreach (var g in Course.FeaturesOf(c))
                    if (g.id != f.id && g.a.z < f.b.z + 6f && f.a.z < g.b.z + 6f && Mathf.Abs(g.a.x - f.a.x) < 4f) { Log($"PROBLEM c{c} features {f.id} and {g.id} overlap"); bad++; }
                foreach (float pk in Course.ParkKickers)
                {
                    float kz = Course.CycleBase(f.a.z) + Course.ParkAt + pk;
                    if (kz < f.b.z + 4f && kz + 22f > f.a.z - 4f) { Log($"PROBLEM c{c} {f.kind} beside a kicker"); bad++; }
                }
                bool blocked = false;
                for (int i = 0; i <= 30 && !blocked; i++)
                {
                    var q = Course.FeaturePoint(f, i / 30f);
                    for (int dx = -1; dx <= 1 && !blocked; dx++)
                        for (int dz = -1; dz <= 1 && !blocked; dz++)
                        {
                            int cx = Mathf.FloorToInt(q.x / Forest.Cell) + dx, cz = Mathf.FloorToInt(q.z / Forest.Cell) + dz;
                            if (Forest.At(cx, cz, out var tp, out _, out _) != Forest.Kind.None && new Vector2(tp.x - q.x, tp.z - q.z).magnitude < 2f) blocked = true;
                        }
                }
                if (blocked) { Log($"PROBLEM c{c} a tree or rock on the {f.kind}"); bad++; }
            }

        foreach (var f in fs)
        {
            string tag = f.kind == Course.Kind.Rail ? "Rail" : "Box";
            if (System.IO.File.Exists($"ParkShot{tag}.png")) continue;   // one of each
            Place(f.a.x, f.a.z - 20f);
            Shoot("ParkShotWarmup.png", false);
            Vector3 mid = Course.FeaturePoint(f, 0.5f);
            Vector3 side = new Vector3(Mathf.Sign(Slope.Centre(mid.z) - mid.x), 0f, 0f);   // from the middle of the piste
            var eye = mid + side * 7f + new Vector3(0f, 0f, -f.Length * 0.55f); eye.y = Slope.Surface(eye.x, eye.z) + 2.2f;
            Look(eye, mid, 60f);
            Shoot($"ParkShot{tag}.png", false);
            eye = f.a + side * 3f + new Vector3(0f, 0f, -14f); eye.y = Slope.Surface(eye.x, eye.z) + 1.8f;
            Look(eye, Course.FeaturePoint(f, 0.25f), 55f);
            Shoot($"ParkShot{tag}Start.png", false);
            // a grind: ride at it from 18 m up the hill in line with it, boarded from the snow
            rider.ResetAt(f.a.x, f.a.z - 18f); body.ResetVisual(); rider.Begin();
            rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(f.a.x, f.a.z - 18f)).normalized * 11f;
            terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); chase.Snap();
            lastCrash = "";
            bool on = false, shot = false; float covered = 0f; string trick = "";
            System.Action<string, int> tk = (nm, pts) => trick = $"{nm} {pts}";
            rider.Trick += tk;
            for (float t = 0f; t < 10f && !rider.Crashed && rider.Z < f.b.z + 15f; t += Dt)
            {
                float tx = rider.Z < f.a.z ? f.a.x : Mathf.Lerp(f.a.x, f.b.x, Mathf.InverseLerp(f.a.z, f.b.z, rider.Z));
                rider.Steer = Mathf.Clamp((tx - rider.transform.position.x) * 0.4f - rider.Heading * 1.5f, -1f, 1f);
                Step();
                if (rider.Grinding) { on = true; covered = Mathf.Max(covered, rider.Z - f.a.z); }
                if (rider.Grinding && !shot && rider.Z > Mathf.Lerp(f.a.z, f.b.z, 0.45f))
                {
                    shot = true;
                    Shoot($"ParkShot{tag}Grind.png", true);
                    var rp = rider.transform.position;
                    Look(rp + side * 4f + new Vector3(0f, 1.2f, 2.5f), rp + Vector3.up * 0.4f, 50f);
                    Shoot($"ParkShot{tag}GrindSide.png", true);
                    chase.enabled = true;
                }
            }
            rider.Trick -= tk;
            Log($"{tag} grind: boarded={on}, {covered:0} of {f.b.z - f.a.z:0} m down the hill, trick '{trick}', crashed={rider.Crashed} {lastCrash}");
            if (!on || covered < (f.b.z - f.a.z) * 0.85f) { Log($"PROBLEM {tag}: did not grind it end to end"); bad++; }
            if (rider.Crashed) { Log($"PROBLEM {tag}: crashed on it ({lastCrash})"); bad++; }
        }
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
