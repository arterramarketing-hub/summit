using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders the blood on the hardest crashes: riding flat out into a tree and into a rock, and a big fall onto the
/// snow that smears as the body slides. Run: Unity -batchmode -projectPath . -executeMethod BloodShot.Run
/// -logFile x.log (no -nographics: it needs the GPU). Writes BloodShot*.png in the project folder.
///
/// Batchmode drives physics from an editor script, where Unity sends no collision messages, so this stands in for
/// them: every step it looks for the rider or a body part newly touching something and raises the same events
/// the game's collision handlers would (Rider.HitObstacle, Ragdoll.Struck), with the speed it was going into it.
/// </summary>
public static class BloodShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;

    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camera cam; static ChaseCamera chase; static MethodInfo late, tUpd, fUpd; static Atmosphere atmo; static Blood blood;
    static readonly Dictionary<Rigidbody, HashSet<Collider>> touching = new Dictionary<Rigidbody, HashSet<Collider>>();
    static readonly Dictionary<Rigidbody, Vector3> lastV = new Dictionary<Rigidbody, Vector3>();
    static Vector3 riderLastV; static bool riderHit;
    static Vector3 lastHitAt, lastHitN; static int struck; static float hardest;

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
        blood = (Blood)typeof(SummitGame).GetField("blood", NP).GetValue(game);
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        fUpd = typeof(Forest).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        try { Sequence(); }
        catch (System.Exception e) { Debug.LogError("BLOODSHOT exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void Sequence()
    {
        Log($"blood on={blood.On}");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float z0 = Course.First + 30f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        Shoot("BloodShotWarmup.png", false);   // the first render comes out unlit
        Shoot("BloodShotWarmup2.png", false);

        Into("Tree", Course.First + 160f, 22f, "BloodShotTree");
        foreach (float rz in new[] { 420f, 300f, 560f, 650f, 820f })
            if (Into("Rock", Course.First + rz, 22f, "BloodShotRock")) break;
        // the throw's spin is random: try a few until the body slides on after the hit and smears
        for (int seed = 1; seed <= 6; seed++)
        {
            Random.InitState(seed * 97);
            if (BigFall(Course.First + 700f, "BloodShotSmear")) break;
        }

        // a light tumble stays clean
        blood.Clear(); struck = 0; hardest = 0f;
        float z = Course.First + 900f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 9f;
        terrain.Prewarm(); props.Prewarm();
        for (float t = 0f; t < 0.3f; t += Dt) Step();
        rider.DoCrash("test");
        for (float t = 0f; t < 3f; t += Dt) Step();
        Log($"light tumble at 9 m/s: {blood.Shown} marks (want 0), {struck} contacts, hardest {hardest:0.0} m/s");

        // the pause menu with its new BLOOD row, drawn into the frame (it normally draws straight to the screen),
        // at an iPhone's portrait shape
        var hud = Object.FindFirstObjectByType<Hud>();
        typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { true });
        var canvas = hud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
        Look(rider.transform.position + new Vector3(0f, 3f, -6f), rider.transform.position, 60f);
        Shoot("BloodShotPause.png", false);
        blood.SetOn(false); hud.SetBlood(false);
        Shoot("BloodShotPauseOff.png", false);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        typeof(SummitGame).GetMethod("SetPaused", NP).Invoke(game, new object[] { false });
        blood.SetOn(true);
    }

    /// <summary>Ride flat out into the nearest tree or rock of this kind, and film it.</summary>
    static bool Into(string kind, float near, float speed, string file)
    {
        rider.ResetAt(Slope.Centre(near), near); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm(); fUpd.Invoke(props, null);
        Obstacle best = null; float bestD = 1e9f;
        foreach (var ob in Object.FindObjectsByType<Obstacle>(FindObjectsSortMode.None))
        {
            if (!ob.gameObject.activeInHierarchy || ob.label != kind) continue;
            var col = ob.GetComponentInChildren<Collider>(); if (col == null || !col.enabled) continue;
            var p = col.bounds.center;
            if (p.z < near + 10f) continue;
            float d = Mathf.Abs(p.z - near - 25f) + Mathf.Abs(p.x - Slope.Centre(p.z)) * 0.5f
                      + (Mathf.Abs(p.x - Slope.Centre(p.z)) > Slope.PisteHalf ? 200f : 0f);   // a lone tree on the piste, in the open
            if (d >= bestD) continue;
            // nothing else where the rider will start
            Vector3 st = p + Vector3.back * (col.bounds.extents.z + 1.4f); st.y = Slope.DrawnHeight(st.x, st.z) + 0.5f;
            bool clear = true;
            foreach (var o in Physics.OverlapSphere(st, 1.2f, ~0, QueryTriggerInteraction.Ignore)) if (o != col && o.GetComponentInParent<Obstacle>() != null) clear = false;
            if (!clear) continue;
            bestD = d; best = ob;
        }
        if (best == null) { Log($"no {kind} found near z={near}"); return false; }
        var bcol = best.GetComponentInChildren<Collider>();
        var target = bcol.bounds.center;
        // a metre or so up the hill from it, already going flat out straight at it
        Vector3 start = target + Vector3.back * (bcol.bounds.extents.z + 1.4f);
        rider.ResetAt(start.x, start.z); body.ResetVisual(); rider.Begin();
        Vector3 to = target - rider.Body.position; to.y = 0f; to.Normalize();
        rider.Heading = Mathf.Atan2(to.x, to.z);
        rider.Body.linearVelocity = to * speed + Vector3.down * 2f;
        terrain.Prewarm(); props.Prewarm();
        Log($"{kind} at {target} ({best.name}), from {rider.Body.position}");
        chase.enabled = true; chase.Snap();
        blood.Clear(); struck = 0; riderHit = false;
        float hitT = -1f;
        for (float t = 0f; t < 4.5f; t += Dt)
        {
            rider.Steer = 0f;
            Step();
            if (riderHit && hitT < 0f) hitT = t;
            if (hitT >= 0f && t - hitT >= 0.1f && t - hitT < 0.1f + Dt)
            {
                // from the side, a few metres off, looking at the hit
                Vector3 hn = lastHitN.sqrMagnitude > 0.01f ? lastHitN.normalized : Vector3.back; hn.y = 0f; hn.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, hn);
                Look(lastHitAt + hn * 4.5f + side * 3f + Vector3.up * 1.3f, lastHitAt + Vector3.up * 0.4f, 50f);
                Shoot(file + "Burst.png", true); Log($"{kind} burst shot, {blood.Shown} marks");
            }
        }
        if (hitT < 0f) { Log($"never reached the {kind}"); return false; }
        Log($"{kind}: hit at t={hitT:0.00}, {blood.Shown} marks, {struck} body contacts");
        {
            var rm = rider.transform.position + Vector3.up * 0.8f;
            bool ok = props.SurfaceOf(bcol, rm, out var sa, out var sn);
            Log($"  surface from {rm}: ok={ok} at {sa} n={sn}; collider {bcol.bounds.min}..{bcol.bounds.max}");
        }
        // the mark on it, close up, and the scene
        // the mark left on it: the one nearest the hit that is not lying on the snow
        bool mk = blood.ObstacleMark(lastHitAt, out var mAt, out var mN);
        Vector3 aim = mk ? mAt : lastHitAt, n = mk ? mN : (lastHitN.sqrMagnitude > 0.01f ? lastHitN.normalized : Vector3.back);
        Vector3 nh = new Vector3(n.x, 0f, n.z).normalized;
        Vector3 eye = aim + nh * 5f + Vector3.up * 1.0f + Vector3.Cross(Vector3.up, nh) * 1.5f;
        Look(eye, aim, 40f); Shoot(file + "Close.png", true);
        Vector3 c = body.RagdollCentre;
        Vector3 mid = (c + lastHitAt) * 0.5f;
        Look(mid + new Vector3(5f, 4.5f, -7f), mid, 50f); Shoot(file + "Wide.png", true);
        LogMarks();
        return true;
    }

    /// <summary>What marks there are, and the middle of them.</summary>
    static Vector3 LogMarks(bool smearOnly = false)
    {
        var c = blood.Counts;
        Log($"  marks: {c.stains} stains, {c.trail} trail pieces ({blood.TrailLaid:0.0} m), {c.drops} drops");
        return blood.MarksMiddle(body.RagdollCentre);
    }

    /// <summary>A crash in the air, a few metres up, onto the snow: it hits hard and smears as it slides.</summary>
    static bool BigFall(float z, string file)
    {
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        var p = rider.Body.position + Vector3.up * 10f;
        rider.Body.position = p; rider.transform.position = p;
        rider.Body.linearVelocity = new Vector3(0f, -5f, 26f);
        body.Apply(Dt);   // the body takes its last velocity from here
        rider.DoCrash("test");
        blood.Clear(); struck = 0;
        chase.enabled = true; chase.Snap();
        float firstGround = -1f;
        for (float t = 0f; t < 6f && !(rider.CanGetUp && t > 2f); t += Dt)
        {
            Step();
            if (firstGround < 0f && blood.Shown > 0) { firstGround = t; }
            if (firstGround >= 0f && t - firstGround >= 0.1f && t - firstGround < 0.1f + Dt) Shoot(file + "Burst.png", true);
        }
        Log($"big fall: {blood.Shown} marks, {struck} body contacts, first blood at t={firstGround:0.00}");
        if (blood.TrailLaid < 1.5f) { Log($"  only {blood.TrailLaid:0.0} m of trail: another throw"); return false; }
        Vector3 c = LogMarks(), sm = LogMarks(true);
        // the splat and the smear from above and to the side, and the smear close, from down the hill
        Look(c + new Vector3(3f, 6f, -3f), c, 50f); Shoot(file + ".png", true);
        Look(sm + new Vector3(1.0f, 1.8f, 3.6f), sm, 50f); Shoot(file + "Low.png", true);
        return true;
    }

    // ---- stepping, and the stand-in for collision messages

    static void Step()
    {
        for (int k = 0; k < 2; k++)
        {
            riderLastV = rider.Body.linearVelocity;
            foreach (var rb in Parts()) lastV[rb] = rb.linearVelocity;
            rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f);
            Contacts();
        }
        tUpd.Invoke(terrain, null);
        fUpd.Invoke(props, null);
        body.Apply(Dt);
        late.Invoke(chase, null);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        foreach (var ps in blood.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(Dt, true, false, false);
        blood.Advance(Dt);   // the drops land, the marks soak in
    }

    static List<Rigidbody> Parts()
    {
        var list = new List<Rigidbody>();
        var rd = body.RagdollForTest;
        if (rd == null || !rd.Active) return list;
        list.AddRange(rd.Bodies);
        return list;
    }

    static void Contacts()
    {
        // the rider into a tree or rock (as Rider.OnCollisionEnter)
        if (!rider.Crashed)
        {
            Vector3 rp = rider.Body.position;
            foreach (var c in Physics.OverlapSphere(rp, Rider.R + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            {
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob == null) continue;
                Vector3 cp = c.ClosestPoint(rp), n = (rp - cp).sqrMagnitude > 1e-6f ? (rp - cp).normalized : -riderLastV.normalized;
                float into = Mathf.Abs(Vector3.Dot(riderLastV, n));
                rider.HitObstacle?.Invoke(ob, c.bounds.center, into);
                riderHit = true; lastHitAt = cp; lastHitN = n;
                Log($"rider hit {ob.label} at {into:0.0} m/s");
                if (into > 6f) rider.DoCrash(ob.label);
                break;
            }
        }
        // the thrown body's parts into anything (as Ragdoll's Impact.OnCollisionEnter)
        var rd = body.RagdollForTest;
        foreach (var rb in Parts())
        {
            var mine = rb.GetComponent<Collider>();
            if (!touching.TryGetValue(rb, out var was)) touching[rb] = was = new HashSet<Collider>();
            var now = new HashSet<Collider>();
            foreach (var c in Physics.OverlapSphere(rb.worldCenterOfMass, 0.45f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == mine || c == rider.GetComponent<Collider>()) continue;
                if (c.attachedRigidbody != null && c.attachedRigidbody != rb && !c.attachedRigidbody.isKinematic) continue;   // its own parts, the board
                if (c.attachedRigidbody != null && c.attachedRigidbody.isKinematic && !rd.IsSnow(c)) continue;               // the board's own slab
                Vector3 onIt = c.ClosestPoint(rb.worldCenterOfMass), onMe = mine.ClosestPoint(onIt);
                float gap = (onMe - onIt).magnitude;
                if (gap > 0.04f) continue;
                now.Add(c);
                if (was.Contains(c)) continue;
                Vector3 n = gap > 1e-4f ? (onMe - onIt) / gap : (rb.worldCenterOfMass - onIt).normalized;
                if (rd.IsSnow(c)) n = c.transform.up;
                Vector3 v = lastV.TryGetValue(rb, out var lv) ? lv : rb.linearVelocity;
                float into = Mathf.Abs(Vector3.Dot(v, n));
                struck++; hardest = Mathf.Max(hardest, into);
                if (into > 6f) Log($"part {rb.name} struck {(rd.IsSnow(c) ? "snow" : c.name)} at {into:0.0} m/s");
                if (!rd.IsSnow(c) && into > 6f) { lastHitAt = onIt; lastHitN = n; }
                rd.Struck?.Invoke(c, onIt, n, into, rb.transform);
            }
            touching[rb] = now;
        }
    }

    static void Look(Vector3 from, Vector3 to, float fov)
    {
        chase.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
    }

    static void Log(string s) => Debug.Log("BLOODSHOT " + s);

    static void Shoot(string file, bool bakeSkins)
    {
        DynamicGI.UpdateEnvironment();
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
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
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
        blood.Draw();   // the blood on rocks
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
