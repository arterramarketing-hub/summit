using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Trees snapping on a fast hit (TreeBreak): which share of the forest can snap, down the mountain; a ride at
/// about 86 km/h into a young tree (before, the moment it snaps, the top going over, and after: the top lying in
/// the snow and the jagged stump), the same into a dead snag, and into a big old trunk that stands, to compare how
/// far the thrown body carries on. Run: Unity -batchmode -projectPath . -executeMethod TreeBreakShot.Run
/// -logFile treebreak.log (no -nographics: it needs the GPU). Writes TreeBreakShot*.png in the project folder;
/// log lines start TREEBREAK.
/// </summary>
public static class TreeBreakShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camera cam; static ChaseCamera chase; static MethodInfo late; static Atmosphere atmo;
    static Smasher smasher; static TreeBreak breaker;
    static int step;
    static float snapT = -1f, snapInto;
    static string scene = "";

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static void Log(string s) => Debug.Log("TREEBREAK " + s);

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        rider = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        smasher = F<Smasher>("smasher"); breaker = F<TreeBreak>("treeBreak");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 4 * Dt;
        rider.Crash += why => Log($"{scene}: CRASH {why} at {rider.Speed * 3.6f:0} km/h");
        breaker.Snapped += (at, into, byRider) => { if (snapT < 0f) { snapT = 0f; snapInto = into; } Log($"{scene}: SNAPPED at {at} {into * 3.6f:0} km/h into it, byRider={byRider}"); };
        try
        {
            Log($"forest realistic={props.IsRealistic}");
            Share();
            // (the mountain is new each run: a tree may turn out to be behind rocks or over a roller; try the next)
            int used = 0;
            for (int i = 0; i < 6; i++)
            {
                var young = Find(false, false, i);
                if (!young.HasValue) { Log("no young tree found"); break; }
                used = i + 1;
                if (Ride("Young", young.Value, 25f, true, false)) break;
            }
            for (int i = 0; i < 6; i++)
            {
                var snag = Find(true, false, i);
                if (!snag.HasValue) { Log("no snag found"); break; }
                if (Ride("Snag", snag.Value, 21.5f, true, false)) break;
            }
            var big = Find(false, true, 0);
            var young2 = Find(false, false, used);
            if (big.HasValue) Ride("Big", big.Value, 24f, false, true);
            else Log("no big tree found");
            if (young2.HasValue) Ride("Slow", young2.Value, 9f, false, false, true);   // below the speed: an ordinary tree crash
        }
        catch (System.Exception e) { Debug.LogError("TREEBREAK exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    // ---- which trees can snap, top to bottom of the mountain

    static void Share()
    {
        rider.ResetAt(Slope.Centre(Course.First + 30f), Course.First + 30f); props.Prewarm();
        int[] trees = new int[4], snap = new int[4], snags = new int[4];
        int z0 = Mathf.FloorToInt(Course.First / Forest.Cell), z1 = Mathf.FloorToInt((Course.First + 2400f) / Forest.Cell);
        for (int cz = z0; cz < z1; cz++)
        {
            float wz = (cz + 0.5f) * Forest.Cell;
            int band = Mathf.Min(3, (int)(Forest.Low(wz) * 4f));
            int cxc = Mathf.FloorToInt(Slope.Centre(wz) / Forest.Cell);
            for (int cx = cxc - 14; cx <= cxc + 14; cx++)
            {
                if (Forest.At(cx, cz, out _, out _, out _) != Forest.Kind.Tree) continue;
                trees[band]++;
                if (props.Snappable(cx, cz, out bool sn)) { snap[band]++; if (sn) snags[band]++; }
            }
        }
        int T = 0, S = 0;
        for (int b = 0; b < 4; b++)
        {
            T += trees[b]; S += snap[b];
            Log($"band {b} (low {b * 0.25f:0.00}-{(b + 1) * 0.25f:0.00}): {trees[b]} trees, {snap[b]} snap ({100f * snap[b] / Mathf.Max(1, trees[b]):0}%), of them {snags[b]} snags");
        }
        Log($"all: {T} trees, {S} snap ({100f * S / Mathf.Max(1, T):0}%)");
    }

    /// <summary>A tree near the piste with a clear run at it from straight up the hill.</summary>
    static Vector3? Find(bool wantSnag, bool wantBig, int skip)
    {
        rider.ResetAt(Slope.Centre(Course.First + 30f), Course.First + 30f);
        props.Rebuild();   // every tree standing again (a snapped one is out of the forest, and would read as one that can't snap)
        int z0 = Mathf.FloorToInt((Course.First + 150f) / Forest.Cell), z1 = Mathf.FloorToInt((Course.First + 2200f) / Forest.Cell);
        for (int cz = z0; cz < z1; cz++)
        {
            float wz = (cz + 0.5f) * Forest.Cell;
            int cxc = Mathf.FloorToInt(Slope.Centre(wz) / Forest.Cell);
            for (int cx = cxc - 12; cx <= cxc + 12; cx++)
            {
                if (Forest.At(cx, cz, out Vector3 p, out _, out _) != Forest.Kind.Tree) continue;
                bool can = props.Snappable(cx, cz, out bool sn);
                if (wantBig ? can : (!can || sn != wantSnag)) continue;
                if (Mathf.Abs(p.x - Slope.Centre(p.z)) > Slope.PisteHalf + (wantSnag ? 30f : 4f)) continue;   // reachable from the piste
                bool smooth = true;   // no crevasse or rock on the way in
                for (float dz = -28f; dz <= 4f && smooth; dz += 2f) if (Course.CrevasseCut(p.x, p.z + dz) > 0.01f || Mathf.Abs(Slope.Kicker(p.x, p.z + dz)) > 0.01f) smooth = false;
                if (!smooth) continue;
                if (Mathf.Abs(Course.CrevasseCut(p.x, p.z)) > 0.01f) continue;
                // nothing else in the way for 40 m up the hill, nor just past it
                bool clear = true;
                for (int dz = -7; dz <= 2 && clear; dz++)
                    for (int dx = -2; dx <= 2 && clear; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        if (Forest.At(cx + dx, cz + dz, out Vector3 q, out _, out _) == Forest.Kind.None) continue;
                        if (Mathf.Abs(q.x - p.x) < 5f && q.z < p.z + 12f) clear = false;
                    }
                if (!clear) continue;
                if (skip-- > 0) continue;
                Log($"found {(wantBig ? "big" : wantSnag ? "snag" : "young")} tree at {p} (low {Forest.Low(p.z):0.00})");
                return p;
            }
        }
        return null;
    }

    static void Start(float x, float z, float v)
    {
        smasher.Clear(); breaker.Clear();
        if (rider.Crashed) { rider.RequestUp = true; for (int i = 0; i < 60 && rider.Crashed; i++) Step(); }
        rider.ResetAt(x, z); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Slope.Normal(x, z) == Vector3.zero ? Vector3.forward * v : Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); chase.Snap();
        step = 0; snapT = -1f;
    }

    static float Toward(float x) => Mathf.Clamp((x - rider.transform.position.x) * 0.8f - rider.Heading * 1.5f, -1f, 1f);

    static bool Ride(string tag, Vector3 tree, float v, bool expectSnap, bool big, bool slow = false)
    {
        scene = tag;
        Start(tree.x, tree.z - (slow ? 12f : tag == "Snag" ? 16f : 26f), v);
        string file = "TreeBreakShot" + tag;
        bool shots = !slow;
        bool s0 = false, s1 = false, s2 = false; float tCrash = -1f; Vector3 crashAt = Vector3.zero; float vIn = 0f;
        var trail = new List<Vector3>(); float closest = 1e9f; string closeNote = "";
        for (float t = 0f; t < 8f; t += Dt)
        {
            if (!rider.Crashed) { rider.Steer = Toward(tree.x); vIn = rider.Speed; }
            else rider.Steer = 0f;
            float flatD = new Vector2(rider.transform.position.x - tree.x, rider.transform.position.z - tree.z).magnitude;
            if (shots && !s0 && !rider.Crashed && tree.z - rider.Z < 9f) { s0 = true; Frame(file + "0_before.png"); }
            // a tree that does not snap: the game's collision would knock you down at the branches (batchmode has no collision callbacks)
            if ((big || slow) && !rider.Crashed && flatD < 2.4f) { vIn = rider.Speed; rider.DoCrash("Tree"); }
            if (!rider.Crashed && flatD < closest) { closest = flatD; closeNote = $"{flatD:0.0} m at {rider.Speed * 3.6f:0} km/h airborne={rider.Airborne} {rider.transform.position.y - Slope.DrawnHeight(rider.transform.position.x, rider.Z):0.0} m up, colliders by the trunk={Physics.OverlapSphere(tree + Vector3.up, 1f).Length}"; }
            Step();
            if (rider.Crashed && tCrash < 0f) { tCrash = 0f; crashAt = rider.transform.position; Log($"{scene}: down at {vIn * 3.6f:0} km/h, {flatD:0.0} m from the trunk"); }
            if (tCrash >= 0f)
            {
                tCrash += Dt;
                if (step % 12 == 0) trail.Add(body.RagdollCentre);
                if (shots && !s1 && tCrash > 0.12f) { s1 = true; Frame(file + "1_impact.png"); Side(tree, file + "1_impactSide.png"); }
                if (shots && !s2 && tCrash > 0.7f) { s2 = true; Frame(file + "2_falling.png"); Side(tree, file + "2_fallingSide.png"); }
                if (tCrash > 4f) break;
            }
        }
        Log($"{scene}: closest approach {closeNote}");
        if (expectSnap && snapT < 0f) Log($"{scene}: NEVER SNAPPED (rider at {rider.transform.position}, tree at {tree})");
        if (tCrash < 0f) { Log($"{scene}: never crashed (rider at {rider.transform.position}, tree at {tree})"); return false; }
        if (expectSnap && snapT < 0f) return false;
        // how far on the body went, and how fast in the first half-second after the hit
        Vector3 rest = body.RagdollCentre;
        float carried = Vector3.Dot(rest - crashAt, Vector3.forward);
        float early = trail.Count > 6 ? (trail[6] - trail[1]).magnitude / (5 * 12 * Dt) : 0f;
        Log($"{scene}: in at {vIn * 3.6f:0} km/h; thrown body {early * 3.6f:0} km/h 0.1-0.6 s after; came to {carried:0.0} m down the hill from where you went down (past the trunk: {rest.z - tree.z:0.0} m)");
        if (snapT >= 0f) Log($"{scene}: tops lying/falling={breaker.Count} still moving={breaker.Moving}");
        if (breaker.TopEnds(out Vector3 butt, out Vector3 tip))
            Log($"{scene}: top lies butt {butt} ({butt.y - Slope.DrawnHeight(butt.x, butt.z):0.00} m up), tip {tip} ({tip.y - Slope.DrawnHeight(tip.x, tip.z):0.00} m up), tilt {Vector3.Angle(tip - butt, Vector3.up):0} deg");
        if (!shots) return true;
        // after: from the side, looking at the stump, the fallen top and the body
        Vector3 c = tree;
        float toPiste = Mathf.Sign(Slope.Centre(c.z) - c.x + 0.01f);
        chase.enabled = false; cam.fieldOfView = 55f;
        Vector3 mid = breaker.TopEnds(out butt, out tip) ? Vector3.Lerp(butt, tip, 0.35f) : c;
        Vector3 from = c + new Vector3(toPiste * 9f, 0f, 13f);
        from.y = Slope.DrawnHeight(from.x, from.z) + 3.2f;
        Look(from, Vector3.Lerp(c, mid, 0.5f) + Vector3.up * 0.6f);
        DrawAll(); ShootBaked(file + "3_after.png");
        // and the stump, close, from up the hill on the side away from where the top went
        float away = breaker.TopEnds(out butt, out tip) ? -Mathf.Sign(tip.x - c.x + 0.01f) : toPiste;
        Vector3 f2 = c + new Vector3(away * 1.8f, 0f, -2.4f);
        f2.y = Slope.DrawnHeight(f2.x, f2.z) + 1.4f;
        cam.fieldOfView = 45f;
        Look(f2, c + Vector3.up * 0.5f);
        DrawAll(); ShootBaked(file + "4_stump.png");
        chase.enabled = true;
        return true;
    }

    /// <summary>From the side of the line, a little up the hill: the trunk going.</summary>
    static void Side(Vector3 tree, string file)
    {
        float toPiste = Mathf.Sign(Slope.Centre(tree.z) - tree.x + 0.01f);
        Vector3 from = tree + new Vector3(toPiste * 14f, 0f, -6f);
        from.y = Slope.DrawnHeight(from.x, from.z) + 2.5f;
        var keep = cam.transform; Vector3 kp = keep.position; Quaternion kr = keep.rotation; float fov = cam.fieldOfView;
        chase.enabled = false; cam.fieldOfView = 60f;
        Look(from, tree + new Vector3(0f, 2.5f, 4f));
        DrawAll(); ShootBaked(file);
        cam.transform.SetPositionAndRotation(kp, kr); cam.fieldOfView = fov; chase.enabled = true;
    }

    static void Step()
    {
        rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        smasher.Step(Dt); breaker.Step(Dt);
        if (snapT >= 0f) snapT += Dt;
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (step % 4 == 0)
        {
            body.Apply(4 * Dt);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.name != "Snowfall") ps.Simulate(4 * Dt, false, false, false);
            late.Invoke(chase, null);
        }
        step++;
    }

    static void DrawAll()
    {
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
        smasher.Draw(); breaker.Draw();
    }

    static void Frame(string file)
    {
        body.Apply(Dt);
        late.Invoke(chase, null);
        DrawAll();
        ShootBaked(file);
    }

    static void Look(Vector3 from, Vector3 at) => cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));

    static void ShootBaked(string file)
    {
        // batchmode renders do not re-skin meshes, so bake the skinned meshes to see the real pose
        var baked = new List<GameObject>();
        var skins = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            skins.Add(sk);
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g);
        }
        Shoot(file);
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
    }

    static bool warm;
    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        Canvas.ForceUpdateCanvases();
        if (!warm) { warm = true; cam.Render(); }   // the first render comes out unlit
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        Log("wrote " + file);
    }
}
