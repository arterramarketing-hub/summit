using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The lift ends (LiftEnds.cs) on four mountains, every station:
///   - built: the bottom has its maze (posts), gates, hut, rack, boards; the top its boards and wands (and the Summit
///     lift's top its path into the camp, ending at the camp's edge);
///   - solid as drawn: every collider under an end is an Obstacle on a drawn primitive (or the rack's one box over
///     the rack and what leans on it); the maze's posts and ropes have none; nothing solid on the groomed run, on a
///     hill or the lane to it, or inside another lift's station;
///   - people: the bottom's queue (at least four in it at a station with room, counting the group stepping up to load), its liftie, the chatting pair, someone
///     clicking in; the top's people standing, sitting, pointing; all of them on the snow (or the deck), never inside
///     a solid; after 40 s, groups have loaded at the bottoms and people have got off, strapped in and pushed off at
///     the tops.
/// -executeMethod LiftEndsTest.Run (ENDS_SEEDS=...). Log lines: ENDS, ENDS-FAIL, ENDS done.
/// Shots (with the GPU, without -quit: one picture an editor frame): -executeMethod LiftEndsTest.Shots
///   (ENDS_OUT=dir, ENDS_SEED=one seed): the Summit lift arriving at the camp, its unload, a bottom station's maze from
///   the chase camera and from above, a park lift, and the layout from high above. Log lines: ENDS-SHOT.
/// </summary>
public static class LiftEndsTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly int[] Seeds = System.Array.ConvertAll((System.Environment.GetEnvironmentVariable("ENDS_SEEDS") ?? "1000,1077,1154,1231").Split(','), int.Parse);
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props; static LiftEnds ends;
    static int fails;
    static void Fail(string s) { fails++; Debug.Log("ENDS-FAIL " + s); }

    static void Setup()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        ends = LiftEnds.Instance;
        r.input = null;
    }

    public static void Run()
    {
        Setup();
        if (ends == null) { Fail("no LiftEnds"); Finish(); return; }
        int stations = 0, people = 0, loaded = 0, pushed = 0, solids = 0;
        try
        {
            foreach (int seed in Seeds)
            {
                TestBoot.NewMountain(game, seed, terrain, props);
                foreach (var e in ends.Ends)
                {
                    stations++;
                    Vector3 eye = e.st + e.side * 14f + Vector3.up * 3f;
                    r.ResetAt(e.st.x, e.st.z);
                    ends.Prewarm(eye, 1f);
                    string tag = $"seed{seed} lift {e.lift.id} ({(e.lift.summit ? "Summit" : e.lift.park ? "park" : e.lift.second ? "across" : "first")}) {(e.top ? "top" : "bottom")}";
                    if (e.root == null) { Fail($"{tag}: not built"); continue; }
                    Physics.SyncTransforms();
                    if (System.Environment.GetEnvironmentVariable("ENDS_GRID") == "1") Debug.Log($"ENDS-GRID {tag}: " + ends.Grid(e));
                    int posts = 0, boards = 0, huts = 0, racks = 0, gates = 0, solidHere = 0;
                    foreach (var c in e.root.GetComponentsInChildren<Collider>())
                    {
                        solidHere++;
                        var ob = c.GetComponent<Obstacle>();
                        if (ob == null) Fail($"{tag}: a collider ({c.name}) that is no obstacle");
                        bool drawn = c.GetComponent<MeshRenderer>() != null || c.name == "RackSolid";
                        if (!drawn) Fail($"{tag}: {c.name}'s collider is not drawn");
                        var p = c.transform.position;
                        if (p.z > Slope.GateZ && Mathf.Abs(Slope.Across(p.x, p.z)) < Slope.Half(p.z)) Fail($"{tag}: {c.name} on the groomed run");   // (behind the gate there is no run: the Summit top beside the lodge, camplayout)
                        if (LiftLine.HillOrLane(p.x, p.z, 0f)) Fail($"{tag}: {c.name} on a hill or the lane to it");
                        foreach (var o in LiftLine.Lifts)
                            if (o != e.lift)
                                foreach (var st in new[] { o.top, o.bottom })
                                    if ((new Vector2(st.x - p.x, st.z - p.z)).magnitude < LiftLine.StationHalfLen + 1f) Fail($"{tag}: {c.name} in lift {o.id}'s station");
                    }
                    foreach (Transform t in e.root.GetComponentsInChildren<Transform>())
                    {
                        if (t.name == "Post") { posts++; if (t.GetComponent<Collider>() != null) Fail($"{tag}: a solid post"); }
                        else if (t.name.StartsWith("Board ")) boards++;
                        else if (t.name == "LiftieHut") huts++;
                        else if (t.name == "Rack") racks++;
                        else if (t.name == "Gate") gates++;
                    }
                    solids += solidHere;
                    int shown = 0, queue = 0, loading = 0, offSnow = 0; string what = "";
                    foreach (var a in ends.Shown())
                    {
                        if (a.end != e) continue;
                        shown++;
                        if (a.job == LiftEnds.Job.Queue) queue++;
                        else if (a.job == LiftEnds.Job.Load) loading++;   // (the queue's front, stepping up to the chair: back in the queue after)
                        what += a.job + " ";
                        float y = a.p.transform.position.y, snow = Slope.DrawnHeight(a.pos.x, a.pos.z);
                        bool onDeck = a.job == LiftEnds.Job.Load && Mathf.Abs(y - e.st.y) < 0.6f;
                        if (!onDeck && (y < snow - 0.25f || y > snow + 0.6f)) offSnow++;
                        // (never standing inside one of the end's solids)
                        foreach (var c in e.root.GetComponentsInChildren<Collider>())
                            if (c.bounds.Contains(a.pos + Vector3.up * 0.8f)) Fail($"{tag}: a {a.job} person inside {c.name}");
                    }
                    people += shown;
                    if (offSnow > 0) Fail($"{tag}: {offSnow} people off the snow");
                    Debug.Log($"ENDS {tag}: z {e.st.z:0}, {posts} posts, {gates} gates, {huts} hut(s), {racks} rack(s), {boards} boards, {solidHere} solid pieces; {shown} people ({what.Trim()}), {queue} queueing; {e.queue.Count} places in the queue, {e.strap.Count} to strap in{(e.toCamp.Count > 0 ? $", path to the camp {e.toCamp.Count} points" : "")}");
                    if (!e.top && e.queue.Count >= 6 && queue + loading < 4) Fail($"{tag}: only {queue} queueing ({loading} loading)");
                    if (!e.top && e.queue.Count >= 6 && posts < 6) Fail($"{tag}: no maze ({posts} posts)");
                    if (e.lift.summit && e.top)
                    {
                        if (e.toCamp.Count < 2) Fail($"{tag}: no path to the camp");
                        else
                        {
                            var last = e.toCamp[e.toCamp.Count - 1];
                            float edge = Mathf.Abs(last.x - Slope.Centre(Slope.PadLip)) - Camp.HalfWidth;
                            if (edge > 3f) Fail($"{tag}: the path ends {edge:0} m short of the camp");
                            Debug.Log($"ENDS {tag}: the path into the camp from z {e.toCamp[0].z:0} to z {last.z:0}, {Vector3.Distance(e.toCamp[0], last):0} m; ends {edge:+0.0;-0.0} m from the camp's edge");
                        }
                    }
                    // a while on: chairs go by
                    int before = Loads(e), pushBefore = Pushes(e);
                    ends.Prewarm(eye, 40f);
                    loaded += Loads(e) - before; pushed += Pushes(e) - pushBefore;
                }
            }
        }
        catch (System.Exception ex) { Fail("exception " + ex); }
        Debug.Log($"ENDS done: {stations} lift ends on {Seeds.Length} mountains, {solids} solid pieces, {people} people shown; over 40 s at each: {loaded} groups loaded, {pushed} pushed off; {fails} failure(s)");
        if (loaded == 0) Fail("nobody loaded anywhere");
        if (pushed == 0) Fail("nobody pushed off anywhere");
        Finish();
    }

    static int Loads(LiftEnds.End e) => LiftEnds.Instance.Loaded;
    static int Pushes(LiftEnds.End e) => LiftEnds.Instance.Pushed;

    static void Finish()
    {
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- pictures

    static Camera cam; static SkiLift lift; static Atmosphere atmo; static DayNight dayNight; static CourseDressing dress;
    static MethodInfo tUpd, fUpd, lUpd;
    static readonly List<System.Action> steps = new List<System.Action>();
    static int at;
    static string outDir;

    public static void Shots()
    {
        outDir = System.Environment.GetEnvironmentVariable("ENDS_OUT") ?? "ends-shots";
        Directory.CreateDirectory(outDir);
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("ENDS_SEED"), out int sd) ? sd : 1000;
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        Setup();
        dayNight = (DayNight)typeof(SummitGame).GetField("dayNight", NP).GetValue(game);
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        lift = Object.FindFirstObjectByType<SkiLift>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); lUpd = typeof(SkiLift).GetMethod("Update", NP);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        TestBoot.NewMountain(game, seed, terrain, props);
        if (dress != null) dress.Rebuild();
        var camp = Object.FindFirstObjectByType<Camp>(); if (camp != null) camp.Rebuild();   // (the camp of this mountain, on its snow)
        SkiLift.TestClock = 100.0;
        Debug.Log($"ENDS-SHOT seed {seed}: {LiftLine.Lifts.Length} lifts\n" + LiftLine.Report());
        // the views: from what each end is, a chase-camera view (behind and above a rider coming in) and one from above
        var views = new List<(string name, Vector3 eye, Vector3 look, Vector3 rider, float fov)>();
        LiftEnds.End summitTop = null, bottom = null, park = null, parkTop = null;
        foreach (var e in ends.Ends)
        {
            if (e.lift.summit && e.top) summitTop = e;
            if (!e.lift.second && !e.lift.summit && !e.top && e.queue.Count >= 8 && bottom == null) bottom = e;
            if (e.lift.park && !e.top && park == null) park = e;
            if (e.lift.park && e.top && parkTop == null) parkTop = e;
        }
        float cx = Slope.Centre(Slope.PadLip);
        if (summitTop != null)
        {
            var s = summitTop;
            // coming off the chair: the unload flat, the path and the camp beyond it
            var stand = s.P(9f, -LiftLine.StationHalfLen - 2f);
            // (from up the path, back toward the station: the chairs coming in, the unload, the people on it)
            var pathEye = s.toCamp.Count > 3 ? s.toCamp[s.toCamp.Count - 3] : stand + s.side * 10f;
            views.Add(("summit-unload-chase", pathEye + Vector3.up * 2.6f - s.side * 1f, s.P(5f, -LiftLine.StationHalfLen) + Vector3.up * 1.5f, pathEye, 66f));
            // the camp's side: the lift arriving beside it, its cable climbing the face
            var campEye = new Vector3(cx + 8f, Camp.GroundAt(8f, -50f).y + 2.4f, -50f);
            views.Add(("summit-from-camp", campEye, s.st + Vector3.up * 2f + s.dir * 25f, new Vector3(cx + 8f, 0f, -50f), 64f));
            views.Add(("summit-overhead", new Vector3(cx - 25f, s.st.y + 95f, s.st.z + 30f), new Vector3(cx - 22f, s.st.y, s.st.z - 12f), stand, 55f));
        }
        if (bottom != null)
        {
            var b = bottom;
            var near = b.P(10f, -LiftLine.StationHalfLen - 16f);
            // a rider coming down to it: from up the line beside the maze's head, looking down the queue to the station
            var down = b.P(17f, -LiftLine.StationHalfLen - 30f);
            views.Add(("bottom-chase", down + Vector3.up * 4f, b.P(8f, -LiftLine.StationHalfLen - 4f) + Vector3.up * 1f, down, 60f));
            views.Add(("bottom-queue", b.P(15f, -LiftLine.StationHalfLen - 7f) + Vector3.up * 2.2f, b.P(7.5f, -LiftLine.StationHalfLen - 5f) + Vector3.up * 1.2f, b.P(15f, -LiftLine.StationHalfLen - 7f), 58f));
            views.Add(("bottom-overhead", b.st + b.side * 9f + Vector3.up * 55f - b.dir * 4f, b.st + b.side * 9f - b.dir * 8f, near, 55f));
        }
        if (park != null)
            views.Add(("park-bottom", park.P(16f, -LiftLine.StationHalfLen - 12f) + Vector3.up * 3f, park.st + Vector3.up * 2f, park.P(16f, -LiftLine.StationHalfLen - 12f), 60f));
        if (parkTop != null)
            views.Add(("park-top", parkTop.P(16f, -LiftLine.StationHalfLen - 14f) + Vector3.up * 2.5f, parkTop.P(8f, -LiftLine.StationHalfLen - 5f) + Vector3.up * 1f, parkTop.P(16f, -LiftLine.StationHalfLen - 14f), 60f));
        // the layout: the camp, the start, the Summit lift and cycle 0's lines, from high above and off to the side
        views.Add(("layout-cycle0", new Vector3(cx - 260f, Slope.Height(cx, 300f) + 420f, 380f), new Vector3(cx, Slope.Height(cx, 250f), 250f), new Vector3(cx, 0f, 200f), 60f));

        steps.Clear(); at = 0;
        steps.Add(() => { TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); });
        foreach (var v in views)
        {
            var view = v;
            // (in one editor frame: what SkiLift draws instanced is gone when the frame ends)
            steps.Add(() => { Stand(view.rider, view.eye, view.look, view.fov); Shoot(Path.Combine(outDir, $"LiftEnds_s{seed}_{view.name}.png")); });
        }
        steps.Add(() =>
        {
            SkiLift.TestClock = -1.0;
            TimeOfDay.TestClock = -1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            TestPrefs.Restore();
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        });
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (at >= steps.Count) return;
        try { steps[at++](); }
        catch (System.Exception e) { Debug.LogError("ENDS-SHOT exception " + e); EditorApplication.update -= Tick; EditorApplication.Exit(1); }
    }

    static void Stand(Vector3 rider, Vector3 eye, Vector3 look, float fov)
    {
        r.ResetAt(rider.x, rider.z); body.ResetVisual(); r.Stop();
        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
        typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
        for (int i = 0; i < 3; i++) lUpd.Invoke(lift, null);
        ends.Prewarm(eye, 6f);
    }

    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
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
        const int w = 1100, h = 1400;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        foreach (var g in baked) { var mf = g.GetComponent<MeshFilter>(); if (mf != null) Object.DestroyImmediate(mf.sharedMesh); Object.DestroyImmediate(g); }
        foreach (var sk in hidden) sk.enabled = true;
        Debug.Log("ENDS-SHOT " + file);
    }
}
