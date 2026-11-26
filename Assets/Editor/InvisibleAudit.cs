using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Hunts for things that knock the rider down with nothing to see there.
///
/// 1. Colliders: streams several mountains end to end and, along the whole rideable width and the off-piste margins,
///    compares every solid collider with what is drawn at that spot this frame (enabled renderers in their LOD, and the
///    instanced forest, talus and lift parts). For each collider it samples its surface at board and body heights and
///    measures to the nearest drawn triangle: a collider whose surface stands well clear of anything drawn is a hit on
///    nothing. Log lines: AUDIT-COL.
/// 2. Crashes: long seeded rides in lanes across the width at two speeds, weaving, restarting past each fall. Every
///    crash is logged with its cause, where, the collider (if any) and how far the nearest drawn thing is; one with
///    nothing drawn within 1.5 m is flagged. Log lines: AUDIT-CRASH.
/// The obstacle contact is found as StressTest does (batchmode sends no collision messages).
///
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod InvisibleAudit.Run -logFile audit.log
/// ($AUDIT_SEEDS mountains, default 3; $AUDIT_RIDES=0 skips the rides; $AUDIT_CLASSIC=1 also audits the original trees;
/// $AUDIT_SHOTS=n, run with the GPU, renders the first n flagged colliders of each kind, in red: AuditCol_*.png.)
/// Batchmode quirks it works round: Destroy is refused outside play mode (streamed-out cycles, hamlets and lifts are
/// swept up by hand), moved colliders only reach the physics scene on its next step (synced before each query), and
/// OnEnable never runs (the talus draws are found by type).
/// </summary>
public static class InvisibleAudit
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags NS = BindingFlags.NonPublic | BindingFlags.Static;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static Hamlets hamlets; static SkiLift lifts; static Camera cam;
    static MethodInfo hamUpd, liftUpd;
    static NightLights lights;
    static string lastCrash = "";
    static int n;

    // what is drawn: a mesh's triangles where they are this frame
    struct Drawn { public Mesh mesh; public int sub; public Matrix4x4 m; public Bounds wb; public string what, shader; public bool instanced; }
    static readonly List<Drawn> drawn = new List<Drawn>();
    static readonly Dictionary<long, Vector3[]> worldTris = new Dictionary<long, Vector3[]>();
    static readonly Dictionary<(Mesh, int), (Vector3[] v, int[] t)> meshCache = new Dictionary<(Mesh, int), (Vector3[], int[])>();

    // results
    class Finding { public string kind, label, path, detail; public float gap; public Vector3 at; public float xc; public int seed; }
    static readonly List<Finding> colFindings = new List<Finding>();
    static readonly Dictionary<string, int> colCount = new Dictionary<string, int>(), colChecked = new Dictionary<string, int>();
    static readonly Dictionary<string, int> strictCount = new Dictionary<string, int>();
    static readonly Dictionary<string, int> crashCount = new Dictionary<string, int>(), invisibleCount = new Dictionary<string, int>();
    static int crashes, invisible;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();   // (Boot clears the saved points and upgrades: the player's own are put back at the end)
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        slide = F<Avalanche>("slide");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.Crash += why => lastCrash = why;
        cam = Camera.main;
        riderParts = new HashSet<Renderer>(r.GetComponentsInChildren<Renderer>(true));   // (the board and body: thrown off, they are still the rider)
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP); liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("AUDIT_SEEDS"), out int sv) ? sv : 3;
        // AUDIT_NIGHT=1: everything at night (the headlamp's hour); AUDIT_SLIDE=1: every ride with the avalanche loose
        // and close behind (Avalanche.Step each physics step, as SummitGame does)
        night = System.Environment.GetEnvironmentVariable("AUDIT_NIGHT") == "1";
        withSlide = System.Environment.GetEnvironmentVariable("AUDIT_SLIDE") == "1";
        if (night) { TimeOfDay.TestClock = 3.4f; F<DayNight>("dayNight").ApplyNow(); }
        // AUDIT_STORM=1: a storm (Weather's STORM held), the trees bent by its wind. Nothing solid moves with the wind:
        // the trunks' posts stand still while the drawn trunks sway (Shaders/TreeWind.hlsl), so how far a trunk is carried
        // at board and body height, the storm's worst over a minute, is the gap that opens between them
        storm = System.Environment.GetEnvironmentVariable("AUDIT_STORM") == "1";
        if (storm)
        {
            Weather.TestChoice = 4;
            var w = Object.FindFirstObjectByType<Weather>(); if (w != null) w.Step(0f);
            float k = TreeWind.ForWeather(1f, 1f), worst06 = 0f, worst18 = 0f;
            for (float t = 0f; t < 60f; t += 0.1f)
            {
                TreeWind.Set(t, k);
                for (int i = 0; i < 40; i++)
                {
                    Vector3 o = new Vector3(i * 37.3f, 0f, i * 91.7f);
                    worst06 = Mathf.Max(worst06, (TreeWind.Sway(o + Vector3.up * 0.6f, o, 1f, TreeWind.FlexBirch) - (o + Vector3.up * 0.6f)).magnitude);
                    worst18 = Mathf.Max(worst18, (TreeWind.Sway(o + Vector3.up * 1.8f, o, 1f, TreeWind.FlexBirch) - (o + Vector3.up * 1.8f)).magnitude);
                }
            }
            Debug.Log($"AUDIT storm: wind strength {k:0.00}; a trunk (the loosest kind) carried at most {worst06 * 100f:0.0} cm at 0.6 m up, {worst18 * 100f:0.0} cm at 1.8 m, against its still post");
        }
        // AUDIT_MOBILE=1: the phone's quality level (its LOD bias picks the crags' and lifts' LODs)
        int quality = QualitySettings.GetQualityLevel();
        if (System.Environment.GetEnvironmentVariable("AUDIT_MOBILE") == "1") QualitySettings.SetQualityLevel(0, true);
        Debug.Log($"AUDIT quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} lodBias {QualitySettings.lodBias}");
        Debug.Log($"AUDIT setup: night={night} slide={withSlide} storm={System.Environment.GetEnvironmentVariable("AUDIT_STORM") == "1"}");
        bool rides = System.Environment.GetEnvironmentVariable("AUDIT_RIDES") != "0";
        bool classic = System.Environment.GetEnvironmentVariable("AUDIT_CLASSIC") == "1";
        try
        {
            for (int seed = 0; seed < seeds; seed++)
            {
                NewMountain(seed);
                Colliders(seed, "real");
                if (classic && seed == 0)
                {
                    props.SetRealistic(false); NewMountain(seed);
                    Colliders(seed, "classic");
                    props.SetRealistic(true); NewMountain(seed);
                }
                if (rides) Rides(seed);
            }
        }
        catch (System.Exception e) { Debug.LogError("AUDIT exception " + e); }
        Report();
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TimeOfDay.TestClock = -1f; Weather.TestChoice = -1;
        QualitySettings.SetQualityLevel(quality, false);   // (the editor saves the current level into ProjectSettings)
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }
    static bool night, withSlide, storm;
    static Avalanche slide;
    static int caught;

    /// <summary>A picture of the first few lift towers (or $AUDIT_SHOT_TARGET: LiftStation, ...) from beside them, the
    /// collider in see-through red (needs the GPU: no -nographics). Writes AuditLift*.png.</summary>
    public static void Shot()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        r = F<Rider>("rider"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props"); body = F<RiderBody>("body");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        cam = Camera.main; cam.GetComponent<ChaseCamera>().enabled = false;
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP); liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        NewMountain(0);
        int shots = 0;
        for (float z = Course.First; z < Course.First + 2f * Course.Cycle && shots < 3; z += 15f)
        {
            r.ResetAt(Slope.Centre(z), z); Stream(false);
            foreach (var c in Physics.OverlapBox(new Vector3(Slope.Centre(z), Slope.Height(Slope.Centre(z), z), z), new Vector3(80f, 400f, 8f)))
            {
                string want = System.Environment.GetEnvironmentVariable("AUDIT_SHOT_TARGET") ?? "LiftTower";
                if (!c.name.StartsWith(want)) continue;
                Vector3 at = c.bounds.center; at.y = Slope.DrawnHeight(at.x, at.z);
                Vector3 from = at + new Vector3(Mathf.Sign(Slope.Centre(at.z) - at.x) * 14f, 0f, -16f);
                // the collider too, in see-through red
                ShotGhost(c);
                from.y = Slope.DrawnHeight(from.x, from.z) + 2.5f;
                cam.fieldOfView = 60f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at + Vector3.up * 3f - from));
                liftUpd.Invoke(lifts, null);
                typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
                var rt = new RenderTexture(590, 1278, 24);
                cam.targetTexture = rt; cam.aspect = 590f / 1278f; cam.Render(); liftUpd.Invoke(lifts, null); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(590, 1278, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 590, 1278), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes($"AuditLift{shots}.png", tex.EncodeToPNG());
                foreach (var g in ghostsLeft) Object.DestroyImmediate(g); ghostsLeft.Clear();
                cam.targetTexture = null;
                Debug.Log($"AUDIT shot {c.name} at {c.bounds.center} size {c.bounds.size}");
                shots++;
                break;
            }
        }
        TestHarness.Exit(0);
    }

    /// <summary>
    /// The crags' hit shapes where the rides hit nothing drawn (the Ridge section of mountains 0 and 1), in
    /// see-through red over the scene, seen from the piste at about a rider's height; each spot also without them.
    /// Needs the GPU (no -nographics). CRAG_TAG names the set: writes AuditCrag_{tag}_{spot}_{plain|hit}.png.
    /// </summary>
    public static void CragShot()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        r = F<Rider>("rider"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props"); body = F<RiderBody>("body");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        cam = Camera.main; cam.GetComponent<ChaseCamera>().enabled = false;
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP); liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        string tag = System.Environment.GetEnvironmentVariable("CRAG_TAG") ?? "now";
        var spots = new (int seed, float z, float xc)[] { (0, 763f, -26f), (1, 703f, -19f), (1, 757f, 26f) };
        int lastSeed = -1, k = 0;
        foreach (var (seed, z, xc) in spots)
        {
            if (seed != lastSeed) { NewMountain(seed); lastSeed = seed; }
            float x = Slope.Centre(z) + xc;
            r.ResetAt(Slope.Centre(z), z - 20f); Stream(false);
            Vector3 at = new Vector3(x, Slope.DrawnHeight(x, z) + 0.8f, z);
            Vector3 from = new Vector3(Slope.Centre(z) + xc * 0.3f, 0f, z - 11f); from.y = Slope.DrawnHeight(from.x, from.z) + 1.7f;
            cam.fieldOfView = 62f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            int n = 0;
            foreach (var c in Physics.OverlapSphere(at, 14f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.name != "SpireHit" || !(c is MeshCollider mc) || mc.sharedMesh == null) continue;
                var g = new GameObject("CragHitGhost", typeof(MeshFilter), typeof(MeshRenderer));
                g.GetComponent<MeshFilter>().sharedMesh = mc.sharedMesh;
                g.GetComponent<MeshRenderer>().sharedMaterial = RedMat();
                g.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation); g.transform.localScale = c.transform.lossyScale;
                g.SetActive(false); ghostsLeft.Add(g); n++;
            }
            Render($"AuditCrag_{tag}_{k}_plain.png");
            foreach (var g in ghostsLeft) g.SetActive(true);
            Render($"AuditCrag_{tag}_{k}_hit.png");
            foreach (var g in ghostsLeft) Object.DestroyImmediate(g); ghostsLeft.Clear();
            Debug.Log($"AUDIT crag shot {tag} {k}: seed{seed} z={z} x-c={xc}, {n} crag hit shapes within 14 m");
            k++;
        }
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Render(string file)
    {
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null); CliffSpires.DrawNow(); if (lifts != null) liftUpd.Invoke(lifts, null);
        var rt = new RenderTexture(590, 1278, 24);
        cam.targetTexture = rt; cam.aspect = 590f / 1278f; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(590, 1278, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 590, 1278), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt);
    }

    static Material RedMat()
    {
        if (redMat != null) return redMat;
        redMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        redMat.SetFloat("_Surface", 1f); redMat.SetFloat("_Blend", 0f);
        redMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); redMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        redMat.SetFloat("_ZWrite", 0f); redMat.renderQueue = 3100; redMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        redMat.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f, 0.35f));
        return redMat;
    }

    static readonly List<GameObject> ghostsLeft = new List<GameObject>();
    static void ShotGhost(Collider c)
    {
        if (redMat == null) { var tmp = new GameObject(); var bc0 = tmp.AddComponent<BoxCollider>(); ShotOf(bc0, Vector3.zero, "/dev/null"); Object.DestroyImmediate(tmp); }
        foreach (var bc in c.GetComponents<BoxCollider>())
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<MeshRenderer>().sharedMaterial = redMat;
            g.transform.SetPositionAndRotation(bc.transform.TransformPoint(bc.center), bc.transform.rotation); g.transform.localScale = bc.size;
            ghostsLeft.Add(g);
        }
    }

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);

    static void NewMountain(int seed)
    {
        Slope.Init(5000 + seed * 977);
        r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ);
        terrain.Rebuild(); props.Rebuild();
        dress = Object.FindFirstObjectByType<CourseDressing>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (dress != null) { dress.Rebuild(); dress.Prewarm(); }
        Debug.Log($"AUDIT-TIME seed{seed} course dressing (two cycles, crags and their colliders) built in {sw.Elapsed.TotalMilliseconds:0} ms; spire colliders {Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Length} mesh colliders");
        Stream(true);
    }

    /// <summary>Everything that streams round the rider, as its Update would (batchmode calls none).</summary>
    static void Stream(bool full)
    {
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm();
        // hamlets set down a piece a frame: let them finish
        if (hamlets != null) for (int i = 0; i < (full ? 120 : 6); i++) hamUpd.Invoke(hamlets, null);
        if (lifts != null) liftUpd.Invoke(lifts, null);
        typeof(Forest).GetMethod("Update", NP).Invoke(props, null);
        if (lights == null) lights = Object.FindFirstObjectByType<NightLights>();
        if (lights != null) lights.Prewarm();   // (the floodlights' poles: solid, and streamed with the rider)
        if (PisteKit.Instance != null) PisteKit.Instance.Prewarm();   // (the snow guns, hydrants and patrol hut: solid)
        Sweep();
        Physics.SyncTransforms();   // (moved colliders reach the physics scene on its next step; the queries here come first)
    }

    /// <summary>
    /// What the game streams out it Destroys, which edit mode refuses: old cycles, hamlets, lifts and stations would
    /// linger here (never on the phone). Put them away for real, so they are not taken for things left behind.
    /// </summary>
    static void Sweep()
    {
        var keep = new HashSet<GameObject>();
        if (lifts != null)
        {
            foreach (var lv in (System.Collections.IEnumerable)typeof(SkiLift).GetField("live", NP).GetValue(lifts))
                foreach (var f in lv.GetType().GetFields())
                {
                    var v = f.GetValue(lv);
                    if (v is GameObject g && g != null) keep.Add(g);
                    else if (v is GameObject[] ga) foreach (var x in ga) if (x != null) keep.Add(x);
                }
            foreach (Transform t in Children(lifts.transform)) if (t.name.StartsWith("Lift ") && !keep.Contains(t.gameObject)) Object.DestroyImmediate(t.gameObject);
            foreach (var st in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
                if (st != null && st.name == "LiftStation" && st.transform.IsChildOf(lifts.transform) && !keep.Contains(st.gameObject)) Object.DestroyImmediate(st.gameObject);
            foreach (var mr in lifts.GetComponentsInChildren<Collider>(true))
                if (mr != null && (mr.name == "LiftRampSignPost" || mr.name == "LiftRampSign")) Object.DestroyImmediate(mr);
        }
        if (hamlets != null)
        {
            keep.Clear();
            foreach (var b in ((System.Collections.IDictionary)typeof(Hamlets).GetField("built", NP).GetValue(hamlets)).Values)
                keep.Add((GameObject)b.GetType().GetField("root").GetValue(b));
            foreach (Transform t in Children(hamlets.transform)) if (t.name.StartsWith("Hamlet ") && !keep.Contains(t.gameObject)) Object.DestroyImmediate(t.gameObject);
        }
        if (dress != null)
        {
            keep.Clear();
            foreach (GameObject g in ((System.Collections.IDictionary)typeof(CourseDressing).GetField("built", NP).GetValue(dress)).Values) keep.Add(g);
            // (and the cycle being built a piece a frame, not in `built` until it is finished)
            var ahead = typeof(CourseDressing).GetField("ahead", NP)?.GetValue(dress);
            if (ahead != null && ahead.GetType().GetField("root")?.GetValue(ahead) is GameObject ar && ar != null) keep.Add(ar);
            foreach (Transform t in Children(dress.transform)) if (t.name.StartsWith("Course ") && !keep.Contains(t.gameObject)) Object.DestroyImmediate(t.gameObject);
        }
    }

    static List<Transform> Children(Transform t) { var l = new List<Transform>(); foreach (Transform c in t) l.Add(c); return l; }

    // ================================================================ pictures of hit shapes against what is drawn

    /// <summary>
    /// Pictures (needs the GPU: no -nographics) of the hit shapes of what the audit flags and what was fitted with them:
    /// the hamlets' landings, terraces, decks and the lodge's stair, talus blocks, forest rocks and floodlight poles. Each
    /// from beside at board height, plain and with every solid surface near it dotted in red (rays onto each collider from
    /// all round and along its axes: where the board would meet it). $SHOT_TAG names the files (AuditFit_{tag}_{kind}_{n}
    /// _plain/_hit.png), $AUDIT_NIGHT=1 at night (the poles' light), $SHOT_PER kind (default 2).
    /// </summary>
    public static void ColliderShot()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        r = F<Rider>("rider"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props"); body = F<RiderBody>("body");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        cam = Camera.main; cam.GetComponent<ChaseCamera>().enabled = false;
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP); liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        string tag = System.Environment.GetEnvironmentVariable("SHOT_TAG") ?? "now";
        int per = int.TryParse(System.Environment.GetEnvironmentVariable("SHOT_PER"), out int pv) ? pv : 2;
        if (System.Environment.GetEnvironmentVariable("AUDIT_NIGHT") == "1") { TimeOfDay.TestClock = 3.4f; F<DayNight>("dayNight").ApplyNow(); }
        string[] kinds = { "landing", "terrace", "stair", "deck", "talus", "rock", "pole" };
        var done = new Dictionary<string, int>();
        var shot = new HashSet<Collider>();
        try
        {
            for (int seed = 0; seed < 3; seed++)
            {
                NewMountain(seed);
                float zEnd = Course.First + 2f * Course.Cycle;
                for (float z = Course.First - 40f; z < zEnd; z += 15f)
                {
                    float cx = Slope.Centre(z);
                    r.ResetAt(cx, z); Stream(false);
                    foreach (var c in Physics.OverlapBox(new Vector3(cx, Slope.Height(cx, z + 7.5f), z + 7.5f), new Vector3(Slope.PisteHalf + 45f, 600f, 7.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (!Relevant(c) || shot.Contains(c)) continue;
                        string k = FitKind(c);
                        if (k == null) continue;
                        done.TryGetValue(k, out int nk);
                        if (nk >= per) continue;
                        done[k] = nk + 1; shot.Add(c);
                        FitShot(c, $"AuditFit_{tag}_{k}_{nk}");
                    }
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("AUDIT fit shot exception " + e); }
        foreach (var k in kinds) { done.TryGetValue(k, out int nk); Debug.Log($"AUDIT fit shot {tag}: {k} x{nk}"); }
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static string FitKind(Collider c)
    {
        string n = c.name;
        if (n.StartsWith("COL_landing")) return "landing";
        if (n.StartsWith("COL_terrace")) return "terrace";
        if (n.StartsWith("COL_stair")) return "stair";
        if (n.StartsWith("COL_deck") && c.GetComponentInParent<Obstacle>()?.label == "Cabin!") return "deck";
        if (n == "SpireHit" && c is MeshCollider m && m.sharedMesh != null && m.sharedMesh.name.StartsWith("Talus")) return "talus";
        if (n == "RockHit") return "rock";
        if (n == "LightPole") return "pole";
        return null;
    }

    static Material dotMat;

    static void FitShot(Collider target, string file)
    {
        var b = target.bounds;
        // the thing and its own pieces (a landing's rail, a car's top: the same building or rock), not a whole crag behind it
        var group = new List<Collider>();
        var near = b; near.Expand(3f);
        var owner = target.GetComponentInParent<Obstacle>();
        foreach (var c in Physics.OverlapBox(near.center, near.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!Relevant(c) || c.GetComponentInParent<Obstacle>() == null) continue;
            if (c != target && c.transform.parent != target.transform.parent && c.GetComponentInParent<Obstacle>() != owner) continue;
            if (c != target && c.bounds.size.magnitude > 3f * b.size.magnitude + 4f) continue;
            if (c != target && c.name.StartsWith("COL_body")) continue;   // (the house itself: not what is being shown)
            group.Add(c);
        }
        var all = b; foreach (var c in group) all.Encapsulate(c.bounds);
        float snow = Slope.DrawnHeight(all.center.x, all.center.z);
        Vector3 look = new Vector3(all.center.x, Mathf.Max(snow + 0.7f, Mathf.Min(all.center.y, snow + 1.5f)), all.center.z);
        float dist = Mathf.Clamp(Mathf.Max(all.extents.x, all.extents.z) * 2.2f + 3.5f, 6f, 18f);
        // from whichever side has a clear view (the piste's side first), at a skier's eye height over the snow there
        Vector3 from = look; bool clear = false;
        float side = Mathf.Sign(Slope.Centre(all.center.z) - all.center.x);
        for (int i = 0; i < 16 && !clear; i++)
        {
            float a = Mathf.Atan2(-0.6f, side * 0.8f) + (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 0.5f;
            from = look + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist * (i < 8 ? 1f : 1.6f);
            from.y = Mathf.Max(Slope.DrawnHeight(from.x, from.z) + 1.7f, look.y + 0.6f);
            clear = !Physics.Linecast(from, look + (from - look).normalized * (Mathf.Max(all.extents.x, all.extents.z) + 0.3f), ~0, QueryTriggerInteraction.Ignore)
                && !Physics.CheckSphere(from, 0.5f, ~0, QueryTriggerInteraction.Ignore)
                && !Physics.Raycast(from, Vector3.down, from.y - Slope.DrawnHeight(from.x, from.z), ~0, QueryTriggerInteraction.Ignore);   // (not on a building)
        }
        cam.fieldOfView = 50f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
        Render2(file + "_plain.png");
        if (dotMat == null) { dotMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); dotMat.SetColor("_BaseColor", new Color(1f, 0.08f, 0.08f, 1f)); }
        var ghosts = new List<GameObject>();
        foreach (var c in group)
        {
            var pts = SurfaceDots(c);
            if (pts.Count == 0) continue;
            var g = new GameObject("FitGhost", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = DotMesh(pts, 0.022f);
            g.GetComponent<MeshRenderer>().sharedMaterial = dotMat;
            ghosts.Add(g);
        }
        Render2(file + "_hit.png");
        foreach (var g in ghosts) { Object.DestroyImmediate(g.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(g); }
        Debug.Log($"AUDIT fit shot {file}: {target.name} {PathOf(target.transform)} at z={b.center.z:0.0} x-c={b.center.x - Slope.Centre(b.center.z):0.0}, size {b.size}; {group.Count} solids dotted; view clear={clear}");
    }

    /// <summary>A landscape picture (Render's is the phone's portrait).</summary>
    static void Render2(string file)
    {
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null); if (lifts != null) liftUpd.Invoke(lifts, null);
        // the talus (OnEnable, which lists its draws for DrawNow, never runs here: found by type, as CollectDrawn does)
        var tdType = typeof(CliffSpires).GetNestedType("TalusDraw", BindingFlags.NonPublic);
        foreach (var td in Object.FindObjectsByType(tdType, FindObjectsSortMode.None)) tdType.GetMethod("Draw").Invoke(td, null);
        const int W = 1100, H = 760;
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }

    /// <summary>Points on a collider's surface: rays onto it from a sphere round it, and along each axis on a grid.</summary>
    static List<Vector3> SurfaceDots(Collider c)
    {
        var pts = new List<Vector3>();
        var b = c.bounds;
        float reach = b.extents.magnitude + 1f;
        const int Dirs = 700;
        for (int i = 0; i < Dirs; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / Dirs, rr = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
            var d = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
            if (c.Raycast(new Ray(b.center + d * reach, -d), out var hit, reach * 2f)) pts.Add(hit.point);
        }
        const int G = 22;
        for (int axis = 0; axis < 3; axis++)
            for (int sgn = -1; sgn <= 1; sgn += 2)
                for (int i = 0; i < G; i++)
                    for (int j = 0; j < G; j++)
                    {
                        int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                        Vector3 o = Vector3.zero, d = Vector3.zero;
                        o[axis] = sgn > 0 ? b.max[axis] + 0.5f : b.min[axis] - 0.5f;
                        o[a1] = Mathf.Lerp(b.min[a1], b.max[a1], (i + 0.5f) / G);
                        o[a2] = Mathf.Lerp(b.min[a2], b.max[a2], (j + 0.5f) / G);
                        d[axis] = -sgn;
                        if (c.Raycast(new Ray(o, d), out var hit, b.size[axis] + 1f)) pts.Add(hit.point);
                    }
        // (only what a board or a body could meet: above the snow, below head height over it)
        pts.RemoveAll(q => { float s = Slope.DrawnHeight(q.x, q.z); return q.y < s - 0.05f || q.y > s + 2.2f; });
        return pts;
    }

    static Mesh DotMesh(List<Vector3> pts, float s)
    {
        var v = new List<Vector3>(pts.Count * 8); var t = new List<int>(pts.Count * 36);
        int[] cube = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
        var corner = new[] { new Vector3(-s, -s, -s), new Vector3(s, -s, -s), new Vector3(s, -s, s), new Vector3(-s, -s, s),
                             new Vector3(-s, s, -s), new Vector3(s, s, -s), new Vector3(s, s, s), new Vector3(-s, s, s) };
        foreach (var p in pts)
        {
            int o = v.Count;
            foreach (var k in corner) v.Add(p + k);
            foreach (int i in cube) t.Add(o + i);
        }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetTriangles(t, 0, true); m.RecalculateNormals();
        return m;
    }

    // ================================================================ 1. colliders against what is drawn

    static void Colliders(int seed, string look)
    {
        var seen = new HashSet<long>();
        float zEnd = Course.First + 2f * Course.Cycle;
        for (float z = Course.First - 40f; z < zEnd; z += 15f)
        {
            float cx = Slope.Centre(z);
            r.ResetAt(cx, z);
            Stream(false);
            if (lifts != null) { liftUpd.Invoke(lifts, null); }
            CollectDrawn();
            var cols = Physics.OverlapBox(new Vector3(cx, Slope.Height(cx, z + 7.5f), z + 7.5f), new Vector3(Slope.PisteHalf + 45f, 600f, 7.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
            {
                if (!Relevant(c)) continue;
                Vector3 bc = c.bounds.center;
                if (bc.z < z || bc.z >= z + 15f) continue;   // each once: by the step its middle is in
                long key = ((long)c.GetInstanceID() << 32) ^ ((long)Mathf.RoundToInt(bc.x * 10f) * 73856093L) ^ ((long)Mathf.RoundToInt(bc.z * 10f) * 19349663L);
                if (!seen.Add(key)) continue;
                Check(c, seed, look);
            }
        }
    }

    static bool Relevant(Collider c)
    {
        if (c == null || !c.enabled || c.isTrigger) return false;
        if (c.attachedRigidbody != null && c.attachedRigidbody == r.Body) return false;
        if (Ragdoll.Of(c) != null) return false;
        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;   // loose debris, the thrown body
        string nm = c.gameObject.name;
        if (nm.StartsWith("RagdollGround") || nm.StartsWith("BoardGround") || nm.Contains("Slab")) return false;
        return true;
    }

    static string Label(Collider c)
    {
        var ob = c.GetComponentInParent<Obstacle>();
        return ob != null ? ob.label : "(no Obstacle)";
    }

    static string PathOf(Transform t)
    {
        string s = t.name;
        for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    static readonly float[] Heights = { 0.15f, 0.35f, 0.6f, 1.1f };

    static void Check(Collider c, int seed, string look)
    {
        string label = Label(c), path = PathOf(c.transform);
        string kind = label + " :: " + Group(c);
        Bump(colChecked, kind);
        votes.Clear();
        var b = c.bounds;
        float snowAt = Slope.DrawnHeight(b.center.x, b.center.z);
        float worst = 0f, worstLow = 0f; Vector3 worstAt = b.center; int far = 0, samples = 0;
        float reach = b.extents.magnitude + 3f;
        for (int d = 0; d < 16; d++)
        {
            float a = d * Mathf.PI * 2f / 16f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            foreach (float h in Heights)
            {
                Vector3 from = b.center + dir * reach;
                from.y = Slope.DrawnHeight(from.x, from.z) + h;
                if (!SurfaceToward(c, from, out Vector3 q)) continue;   // (inside, or nothing of it at this height this way)
                float snow = Slope.DrawnHeight(q.x, q.z);
                if (q.y < snow + 0.02f || q.y > snow + 1.8f) continue;   // under the snow, or above anyone's reach
                samples++;
                float gap = Nearest(q, 3f);
                if (lastNearest >= 0) { string k = DrawnKey(lastNearest); votes.TryGetValue(k, out int nv); votes[k] = nv + 1; }
                if (gap > 0.6f) far++;
                if (gap > worst) { worst = gap; worstAt = q; }
                if (h <= 0.6f && gap > worstLow) worstLow = gap;
            }
        }
        NoteDrawnWith(label + " :: " + Group(c));
        if (samples == 0) return;
        // (and the stricter line: anything more than 0.3 m clear of what is drawn where a board meets it)
        if (worstLow > 0.3f)
        {
            string sk = label + " :: " + Group(c);
            Bump(strictCount, sk);
            if (strictCount[sk] <= 3) Debug.Log($"AUDIT-COL03 seed{seed} {sk}: {worstLow:0.00} m clear of anything drawn at board height, {path}{(c is MeshCollider mm && mm.sharedMesh != null ? " mesh " + mm.sharedMesh.name : "")} size={b.size} centre {b.center.y - snowAt:0.0} m up at z={b.center.z:0.0} x-c={b.center.x - Slope.Centre(b.center.z):0.0}; drawn nearby: {NearbyDrawn(b.center, 6f)}");
        }
        // a hit on nothing: its surface well clear of anything drawn where a board meets it (up to 0.6 m above the
        // snow), or (counted apart) higher, where only a board in the air does
        if (worstLow > 0.9f) kind += " [board height]";
        else if (worst > 0.9f) kind += " [above 0.6 m]";
        if (worst > 0.9f)
        {
            Bump(colChecked, kind);
            Bump(colCount, kind);
            var f = new Finding
            {
                kind = kind, label = label, path = path, gap = worst, at = worstAt, xc = worstAt.x - Slope.Centre(worstAt.z), seed = seed,
                detail = $"{look} {c.GetType().Name} size={b.size} {far}/{samples} samples >0.6 m from anything drawn; snow at {snowAt:0.0}, centre {b.center.y - snowAt:0.0} m up"
            };
            colFindings.Add(f);
            if (colCount[kind] <= shotsPerKind && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) ShotOf(c, worstAt, $"AuditCol_{kind.Split(' ')[0]}_{Group(c).Split('/')[1].Split(' ')[0]}_{colCount[kind]}.png");
            if (colCount[kind] <= 6)
                Debug.Log($"AUDIT-COL seed{seed} {kind}: nothing drawn within {(worst >= 3f ? ">3" : worst.ToString("0.00"))} m of its surface at z={worstAt.z:0.0} x-c={f.xc:0.0} ({worstAt.y - Slope.DrawnHeight(worstAt.x, worstAt.z):0.00} m up) {path}; {f.detail}; drawn nearby: {NearbyDrawn(b.center, 8f)}");
        }
    }

    /// <summary>The collider's surface seen from `from`: its nearest point (convex), or for a solid that is not convex
    /// (ClosestPoint cannot answer for one) where a level line from `from` toward its middle meets it.</summary>
    static bool SurfaceToward(Collider c, Vector3 from, out Vector3 q)
    {
        if (!(c is MeshCollider m) || m.convex) { q = c.ClosestPoint(from); return (q - from).sqrMagnitude >= 1e-6f; }
        var b = c.bounds;
        Vector3 to = new Vector3(b.center.x, from.y, b.center.z) - from;
        q = from;
        if (to.sqrMagnitude < 1e-6f || !c.Raycast(new Ray(from, to.normalized), out var hit, to.magnitude + b.extents.magnitude)) return false;
        q = hit.point;
        return true;
    }

    static readonly int shotsPerKind = int.TryParse(System.Environment.GetEnvironmentVariable("AUDIT_SHOTS"), out int sp) ? sp : 0;
    static Material redMat;

    /// <summary>The flagged collider, drawn in see-through red over the scene, seen from 7 m off at board height.</summary>
    static void ShotOf(Collider c, Vector3 worst, string file)
    {
        if (redMat == null)
        {
            redMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            redMat.SetFloat("_Surface", 1f); redMat.SetFloat("_Blend", 0f);
            redMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); redMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            redMat.SetFloat("_ZWrite", 0f); redMat.renderQueue = 3100; redMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            redMat.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f, 0.35f));
        }
        var ghosts = new List<GameObject>();
        GameObject G(PrimitiveType t) { var g = GameObject.CreatePrimitive(t); Object.DestroyImmediate(g.GetComponent<Collider>()); g.GetComponent<MeshRenderer>().sharedMaterial = redMat; ghosts.Add(g); return g; }
        var tr = c.transform;
        if (c is BoxCollider bx) { var g = G(PrimitiveType.Cube); g.transform.SetPositionAndRotation(tr.TransformPoint(bx.center), tr.rotation); g.transform.localScale = Vector3.Scale(bx.size, tr.lossyScale); }
        else if (c is SphereCollider sc) { var g = G(PrimitiveType.Sphere); g.transform.position = tr.TransformPoint(sc.center); g.transform.localScale = Vector3.one * 2f * sc.radius * tr.lossyScale.x; }
        else if (c is CapsuleCollider cc) { var g = G(PrimitiveType.Cylinder); g.transform.position = tr.TransformPoint(cc.center); g.transform.rotation = tr.rotation; g.transform.localScale = new Vector3(2f * cc.radius, cc.height * 0.5f, 2f * cc.radius); }
        else { var g = G(PrimitiveType.Cube); g.transform.position = c.bounds.center; g.transform.localScale = c.bounds.size; }
        var dot = G(PrimitiveType.Sphere); dot.transform.position = worst; dot.transform.localScale = Vector3.one * 0.2f;
        dot.GetComponent<MeshRenderer>().sharedMaterial = new Material(redMat) { color = Color.red };
        Vector3 outward = worst - c.bounds.center; outward.y = 0f; outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.back;
        Vector3 from = worst + outward * 7f; from.y = Slope.DrawnHeight(from.x, from.z) + 1.6f;
        cam.GetComponent<ChaseCamera>().enabled = false; cam.fieldOfView = 55f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(worst + Vector3.up * 0.3f - from));
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
        CliffSpires.DrawNow(); if (lifts != null) liftUpd.Invoke(lifts, null);
        var rt = new RenderTexture(590, 1278, 24);
        cam.targetTexture = rt; cam.aspect = 590f / 1278f; cam.Render();
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null); CliffSpires.DrawNow(); if (lifts != null) liftUpd.Invoke(lifts, null);
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(590, 1278, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 590, 1278), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt);
        foreach (var g in ghosts) Object.DestroyImmediate(g);
    }

    /// <summary>A collider's family, for counting: the kind of thing it belongs to.</summary>
    static string Group(Collider c)
    {
        var t = c.transform;
        string root = t.name;
        while (t.parent != null && t.parent.parent != null) { t = t.parent; }
        string top = t.name;
        string nm = c.gameObject.name;
        int sp = nm.IndexOf(' '); if (sp > 0) nm = nm.Substring(0, sp);
        int par = nm.IndexOf('('); if (par > 0) nm = nm.Substring(0, par);
        return $"{top.Split(' ')[0]}/{nm} ({c.GetType().Name})";
    }

    static void Bump(Dictionary<string, int> d, string k) { d.TryGetValue(k, out int v); d[k] = v + 1; }

    // ---------------------------------------------------------------- what is drawn

    static void CollectDrawn()
    {
        drawn.Clear(); worldTris.Clear();
        Vector3 eye = r.transform.position + new Vector3(0f, 2.6f, -6f);
        float fov = cam != null ? cam.fieldOfView : 60f;
        // renderers, each in the LOD its group shows from the chase camera
        var lodOff = new HashSet<Renderer>();
        foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            if (!g.enabled || !g.gameObject.activeInHierarchy) continue;
            var lods = g.GetLODs();
            Vector3 wc = g.transform.TransformPoint(g.localReferencePoint);
            float size = g.size * Mathf.Max(Mathf.Abs(g.transform.lossyScale.x), Mathf.Max(Mathf.Abs(g.transform.lossyScale.y), Mathf.Abs(g.transform.lossyScale.z)));
            float dist = Vector3.Distance(eye, wc);
            float rel = size / (2f * Mathf.Max(0.01f, dist) * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
            int pick = -1;
            for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) { pick = i; break; }
            for (int i = 0; i < lods.Length; i++) if (i != pick) foreach (var rr in lods[i].renderers) if (rr != null) lodOff.Add(rr);
        }
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!mr.enabled || !mr.gameObject.activeInHierarchy || mr.forceRenderingOff || lodOff.Contains(mr) || riderParts.Contains(mr)) continue;
            if (mr.GetComponentInParent<SlopeStreamer>() != null) continue;   // the snow itself
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            if (mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_BaseColor") && mr.sharedMaterial.GetColor("_BaseColor").a < 0.05f && mr.sharedMaterial.renderQueue > 2500) continue;   // see-through
            for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
            {
                var mat = s < mr.sharedMaterials.Length ? mr.sharedMaterials[s] : mr.sharedMaterial;
                drawn.Add(new Drawn { mesh = mf.sharedMesh, sub = s, m = mr.localToWorldMatrix, wb = mr.bounds, what = mr.name, shader = ShaderOf(mat), instanced = mat != null && mat.enableInstancing });
            }
        }
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy || sk.sharedMesh == null) continue;
            if (sk.transform.IsChildOf(body.transform) || riderParts.Contains(sk)) continue;
            drawn.Add(new Drawn { mesh = null, wb = sk.bounds, what = "skin " + sk.name });   // (its box only)
        }
        // the forest's instanced trees and rocks
        var near = (Dictionary<int, List<Matrix4x4>>)typeof(Forest).GetField("nearL", NP).GetValue(props);
        var farL = (Dictionary<int, List<Matrix4x4>>)typeof(Forest).GetField("farL", NP).GetValue(props);
        var partsOf = typeof(Forest).GetMethod("PartsOf", NP);
        foreach (var set in new[] { near, farL })
            foreach (var kv in set)
            {
                var parts = (System.Collections.IList)partsOf.Invoke(props, new object[] { kv.Key });
                var part = parts[kv.Key & 15];
                var pt = part.GetType();
                var mesh = (Mesh)pt.GetField("mesh").GetValue(part); int sub = (int)pt.GetField("sub").GetValue(part);
                var fmat = (Material)pt.GetField("mat").GetValue(part);
                foreach (var m in kv.Value) AddInstanced(mesh, sub, m, "forest", fmat);
            }
        // trees with damage (branches torn, a top clipped), drawn one by one out of the instanced batches (Forest.DrawDamaged)
        var damaged = (List<Vector2Int>)typeof(Forest).GetField("damagedL", NP)?.GetValue(props);
        if (damaged != null)
            foreach (var cell in damaged)
            {
                var pl = typeof(Forest).GetMethod("Get", NP).Invoke(props, new object[] { cell.x, cell.y });
                int idx = (int)pl.GetType().GetField("idx").GetValue(pl);
                var m = (Matrix4x4)pl.GetType().GetField("m").GetValue(pl);
                var parts = (System.Collections.IList)partsOf.Invoke(props, new object[] { idx * 16 });
                var part = parts[0]; var pt = part.GetType();
                AddInstanced((Mesh)pt.GetField("mesh").GetValue(part), (int)pt.GetField("sub").GetValue(part), m, "forest (damaged)", (Material)pt.GetField("mat").GetValue(part));
            }
        // the stumps of snapped trees
        var stumps = (List<Matrix4x4>)typeof(Forest).GetField("stumpL", NP)?.GetValue(props);
        var stumpMesh = (Mesh)typeof(TreeBreak).GetField("StumpMesh", NS)?.GetValue(null);
        if (stumps != null && stumpMesh != null) foreach (var m in stumps) AddInstanced(stumpMesh, 0, m, "stump");
        // the crags' scanned boulders
        var tdType = typeof(CliffSpires).GetNestedType("TalusDraw", BindingFlags.NonPublic);
        var scans = (System.Array)typeof(CliffSpires).GetField("scans", NS).GetValue(null);
        if (tdType != null && scans != null)
        {
            // (found by type: its own list fills in OnEnable, which batchmode never calls)
            foreach (var td in Object.FindObjectsByType(tdType, FindObjectsSortMode.None))
            {
                var lists = (List<Matrix4x4>[])tdType.GetField("lists").GetValue(td);
                if (lists == null || !((MonoBehaviour)td).isActiveAndEnabled) continue;
                for (int v = 0; v < lists.Length; v++)
                    foreach (var part in (System.Collections.IEnumerable)scans.GetValue(v))
                    {
                        var pt = part.GetType();
                        var mesh = (Mesh)pt.GetField("mesh").GetValue(part); int sub = (int)pt.GetField("sub").GetValue(part); var local = (Matrix4x4)pt.GetField("local").GetValue(part);
                        foreach (var m in lists[v]) AddInstanced(mesh, sub, m * local, "talus", (Material)typeof(CliffSpires).GetField("scanMat", NS).GetValue(null));
                    }
            }
        }
        // the lifts' instanced towers, chairs and stations
        if (lifts != null)
        {
            var lparts = (System.Collections.IList)typeof(SkiLift).GetField("parts", NP).GetValue(lifts);
            foreach (var part in lparts)
            {
                var pt = part.GetType();
                var mesh = (Mesh)pt.GetField("mesh").GetValue(part);
                var list = (List<Matrix4x4>)pt.GetField("list").GetValue(part);
                if (mesh == null) continue;
                foreach (var m in list) AddInstanced(mesh, 0, m, "lift", (Material)typeof(SkiLift).GetField("paint", NP).GetValue(lifts));
            }
        }
    }

    static void AddInstanced(Mesh mesh, int sub, Matrix4x4 m, string what, Material mat = null)
    {
        if (mesh == null) return;
        Vector3 p = m.GetColumn(3);
        if (Mathf.Abs(p.z - r.transform.position.z) > 160f) return;
        drawn.Add(new Drawn { mesh = mesh, sub = sub, m = m, wb = WorldBox(m, mesh.bounds), what = what, shader = ShaderOf(mat), instanced = true });
    }

    static string ShaderOf(Material m) => m == null || m.shader == null ? "?" : m.shader.name;

    // ---- what each solid is drawn with, against the shaders the iOS build has been stripping the instanced variants of
    // (Summit/ScanRock, Summit/TreeSway, Summit/FarLit: the lift session's finding). On the phone, a solid whose nearest
    // drawn surface is one of those, drawn instanced, would be blank where it still knocks you down. (Summit/FirCards,
    // the realistic forest, also sways in the wind and is drawn instanced: listed apart, to confirm.)
    static readonly string[] Stripped = { "Summit/ScanRock", "Summit/TreeSway", "Summit/FarLit" };
    static readonly Dictionary<string, Dictionary<string, int>> drawnWith = new Dictionary<string, Dictionary<string, int>>();
    static readonly Dictionary<string, int> votes = new Dictionary<string, int>();

    static string DrawnKey(int i)
    {
        var d = drawn[i];
        string sh = d.shader ?? "?";
        bool hit = System.Array.IndexOf(Stripped, sh) >= 0;
        return (hit && d.instanced ? "STRIPPED " : "") + sh + (d.instanced ? " (instanced)" : "") + " [" + d.what.Split(' ')[0] + "]";
    }

    /// <summary>A kind's collider: what its samples' nearest drawn surfaces were drawn with (the most of them).</summary>
    static void NoteDrawnWith(string kind)
    {
        if (votes.Count == 0) return;
        string top = null; int most = -1;
        foreach (var kv in votes) if (kv.Value > most) { most = kv.Value; top = kv.Key; }
        votes.Clear();
        if (!drawnWith.TryGetValue(kind, out var d)) drawnWith[kind] = d = new Dictionary<string, int>();
        d.TryGetValue(top, out int n); d[top] = n + 1;
    }

    static Bounds WorldBox(Matrix4x4 m, Bounds b)
    {
        var w = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            w.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return w;
    }

    /// <summary>How far the nearest drawn triangle is from this point (up to `limit`; `limit` if none is nearer).</summary>
    static int lastNearest = -1;   // (the drawn thing whose surface Nearest found)

    static float Nearest(Vector3 q, float limit)
    {
        float best = limit;
        lastNearest = -1;
        for (int i = 0; i < drawn.Count; i++)
        {
            var d = drawn[i];
            if (d.wb.SqrDistance(q) > best * best) continue;
            if (d.mesh == null) { float bd = Mathf.Sqrt(d.wb.SqrDistance(q)); if (bd < best) { best = bd; lastNearest = i; } continue; }
            var tris = Tris(i, d);
            if (tris == null) { float bd = Mathf.Sqrt(d.wb.SqrDistance(q)); if (bd < best) { best = bd; lastNearest = i; } continue; }
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = tris[t], b = tris[t + 1], c = tris[t + 2];
                // cheap reject on the triangle's box
                float mnx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - best, mxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + best;
                if (q.x < mnx || q.x > mxx) continue;
                float mny = Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - best, mxy = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) + best;
                if (q.y < mny || q.y > mxy) continue;
                float mnz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - best, mxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + best;
                if (q.z < mnz || q.z > mxz) continue;
                float dd = (ClosestOnTri(q, a, b, c) - q).magnitude;
                if (dd < best) { best = dd; lastNearest = i; }
            }
        }
        return best;
    }

    static string NearbyDrawn(Vector3 at, float within)
    {
        var l = new List<(float, string)>();
        for (int i = 0; i < drawn.Count; i++)
        {
            float d = Mathf.Sqrt(drawn[i].wb.SqrDistance(at));
            if (d < within) l.Add((d, $"{drawn[i].what}[{(drawn[i].mesh != null ? drawn[i].mesh.name : "-")} box {drawn[i].wb.center} {drawn[i].wb.size} readable={(Tris(i, drawn[i]) != null)}]"));
        }
        l.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Mathf.Min(3, l.Count); i++) sb.Append($"{l[i].Item1:0.0}m {l[i].Item2}; ");
        return l.Count == 0 ? "nothing" : sb.ToString();
    }

    static Vector3[] Tris(int i, Drawn d)
    {
        long key = i;
        if (worldTris.TryGetValue(key, out var w)) return w;
        if (!meshCache.TryGetValue((d.mesh, d.sub), out var lt))
        {
            try
            {
                var v = new List<Vector3>(); d.mesh.GetVertices(v);
                lt = (v.ToArray(), d.mesh.GetTriangles(d.sub));
            }
            catch { lt = (null, null); }
            meshCache[(d.mesh, d.sub)] = lt;
        }
        if (lt.v == null || lt.t == null || lt.v.Length == 0) { worldTris[key] = null; return null; }
        w = new Vector3[lt.t.Length];
        for (int t = 0; t < lt.t.Length; t++) w[t] = d.m.MultiplyPoint3x4(lt.v[lt.t[t]]);
        worldTris[key] = w;
        return w;
    }

    static Vector3 ClosestOnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float den = 1f / (va + vb + vc);
        return a + ab * (vb * den) + ac * (vc * den);
    }

    // ================================================================ 2. crashes

    static Collider hitBy;
    static readonly MethodInfo testContacts = typeof(Rider).GetMethod("TestContacts");
    static HashSet<Renderer> riderParts = new HashSet<Renderer>();

    static void Step()
    {
        float dt = Time.fixedDeltaTime;
        // as the game's FixedUpdate: the rider's own step first (in it TreeBreak snaps a thin trunk before the board
        // meets its post), then what the physics step's move runs into (Rider.OnCollisionEnter, never called in batchmode)
        var was = hitBy;
        if (withSlide && slide != null) slide.Step(dt, r, body);
        r.Tick(dt);
        hitBy = r.Crashed ? was : null;
        // (ridden over, glanced off or crashed into, as on the phone: Rider.Hit; on code from before that, any touch of
        // an obstacle, as the rider then took it: so the same audit measures both)
        if (testContacts != null) { if ((bool)testContacts.Invoke(r, new object[] { dt })) hitBy = (Collider)typeof(Rider).GetProperty("HitCollider").GetValue(r); }
        else if (!r.Crashed && r.Running)
        {
            Vector3 p = r.Body.position, v = r.Velocity;
            foreach (var c in Physics.OverlapCapsule(p, p + v * dt, Rider.R + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.attachedRigidbody == r.Body) continue;
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob != null && r.InvincibleTime <= 0f) { hitBy = c; r.DoCrash(ob.label); break; }
            }
        }
        Physics.Simulate(dt);
        if (n % 24 == 0) Stream(false);
        n++;
    }

    static void Rides(int seed)
    {
        var rng = new System.Random(seed * 7 + 3);
        float zEnd = Course.First + 2f * Course.Cycle;
        float[] lanes = { -(Slope.PisteHalf + 18f), -(Slope.PisteHalf + 6f), -12f, -4f, 4f, 12f, Slope.PisteHalf + 6f, Slope.PisteHalf + 18f };
        foreach (float v0 in new[] { 18f, 30f })
            foreach (float lane in lanes)
            {
                float z = Course.First - 20f;
                int guard = 0;
                while (z < zEnd && guard++ < 200)
                {
                    float x = Slope.Centre(z) + lane;
                    r.ResetAt(x, z); r.Begin();
                    r.Heading = 0f;
                    r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
                    lastCrash = "";
                    if (withSlide) { slide.Reset(z - 200f); slide.ForceGap(r, 45f, v0); }
                    Stream(false);
                    float phase = (float)rng.NextDouble() * 10f, amp = 0.3f + 0.5f * (float)rng.NextDouble();
                    float t = 0f;
                    while (!r.Crashed && t < 60f && r.Z < zEnd)
                    {
                        float want = Slope.Centre(r.Z + 14f) + lane + 6f * Mathf.Sin(t * 0.6f + phase);
                        r.Steer = Mathf.Clamp(amp * Mathf.Sin(t * 1.3f + phase) * 0.5f + (want - r.X) * 0.06f - r.Heading * 0.6f, -1f, 1f);
                        // keep the speed roughly where it was set: a ride is about where you go, not how fast
                        Step(); t += Time.fixedDeltaTime;
                        if (r.Speed > v0 * 1.3f) r.Body.linearVelocity *= 0.98f;
                    }
                    if (withSlide && slide.Caught)
                    {
                        caught++;
                        Vector3 cp = r.transform.position;
                        Debug.Log($"AUDIT-CAUGHT seed{seed} lane{lane:0} v{v0:0}: by the slide at z={cp.z:0.0} x-c={cp.x - Slope.Centre(cp.z):0.0}, its depth here {slide.Depth(cp.x, cp.z):0.0} m, front {slide.FrontZ(cp.x) - cp.z:0.0} m from the board");
                    }
                    else if (r.Crashed) Crash(seed, lane, v0);
                    z = r.Z + 25f;
                    if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 4; i++) r.Tick(Time.fixedDeltaTime); }
                }
            }
    }

    static void Crash(int seed, float lane, float v0)
    {
        crashes++;
        Vector3 p = r.transform.position;
        CollectDrawn();
        string why = lastCrash;
        Bump(crashCount, why);
        // where it touched: the collider's surface nearest the board, or the board itself
        Vector3 at = hitBy != null ? Obstacle.Nearest(hitBy, p) : p;
        float gap = Nearest(at, 3f);
        string what = hitBy != null ? $"{Label(hitBy)} {PathOf(hitBy.transform)} {Group(hitBy)}" : "-";
        string ctx = $"rock={RockFace.IsRock(Slope.Normal(p.x, p.z), p.x, p.z)} cut={Course.CrevasseCut(p.x, p.z):0.00} n.y={Slope.Normal(p.x, p.z).y:0.00} land={r.LastLandAngle:0} deg {r.LastLandImpact:0.0} m/s sec={Course.At(p.z)}";
        bool none = hitBy != null && gap > 1.5f;
        if (why == "Hit the rocks!")
        {
            // the rule asks the slope's own normal at the board; the snow mesh paints rock by each drawn triangle's
            string under = DrawnSurface(p);
            ctx += $" drawn={under}";
            if (under.StartsWith("snow")) { none = true; Bump(invisibleCount, "Hit the rocks! on snow as drawn"); }
        }
        if (none) { invisible++; if (hitBy != null) Bump(invisibleCount, why + " :: " + Group(hitBy)); }
        Debug.Log($"AUDIT-CRASH{(none ? "-INVISIBLE" : "")} seed{seed} lane{lane:0} v{v0:0}: '{why}' at z={p.z:0.0} x-c={p.x - Slope.Centre(p.z):0.0} {r.Speed * 3.6f:0} km/h; hit {what}; nearest drawn {(gap >= 3f ? ">3" : gap.ToString("0.00"))} m; {ctx}");
    }

    /// <summary>What the snow mesh draws under this point: the triangle it is over, and whether it is in the rock part.</summary>
    static string DrawnSurface(Vector3 p)
    {
        foreach (var mf in terrain.GetComponentsInChildren<MeshFilter>())
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled || mf.sharedMesh == null) continue;
            var wb = mr.bounds;
            if (p.x < wb.min.x || p.x > wb.max.x || p.z < wb.min.z || p.z > wb.max.z) continue;
            var mesh = mf.sharedMesh; var v = new List<Vector3>(); mesh.GetVertices(v);
            var M = mf.transform.localToWorldMatrix;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                var t = mesh.GetTriangles(sm);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = M.MultiplyPoint3x4(v[t[i]]), b = M.MultiplyPoint3x4(v[t[i + 1]]), c = M.MultiplyPoint3x4(v[t[i + 2]]);
                    if (!InTri(p, a, b, c)) continue;
                    Vector3 n = Vector3.Cross(b - a, c - a).normalized; if (n.y < 0f) n = -n;
                    string mat = sm < mr.sharedMaterials.Length && mr.sharedMaterials[sm] != null ? mr.sharedMaterials[sm].name : "?";
                    bool rockMat = mat.ToLower().Contains("rock");
                    return $"{(rockMat ? "rock" : "snow")}({mat} face n.y={n.y:0.00})";
                }
            }
        }
        return "?";
    }

    static bool InTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        float d1 = (p.x - b.x) * (a.z - b.z) - (a.x - b.x) * (p.z - b.z);
        float d2 = (p.x - c.x) * (b.z - c.z) - (b.x - c.x) * (p.z - c.z);
        float d3 = (p.x - a.x) * (c.z - a.z) - (c.x - a.x) * (p.z - a.z);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }

    // ================================================================ the summary

    static void Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"AUDIT summary: {colFindings.Count} colliders with nothing drawn near part of their surface; by kind (found/checked):\n");
        var kinds = new List<string>(colChecked.Keys);
        kinds.Sort((a, b) => (colCount.TryGetValue(b, out int x) ? x : 0).CompareTo(colCount.TryGetValue(a, out int y) ? y : 0));
        foreach (var k in kinds) { colCount.TryGetValue(k, out int f); if (f > 0) sb.Append($"  {f}/{colChecked[k]}  {k}\n"); }
        sb.Append("  clean kinds: ");
        foreach (var k in kinds) { colCount.TryGetValue(k, out int f); if (f == 0) sb.Append($"{k} ({colChecked[k]}); "); }
        sb.Append("\n  more than 0.3 m clear at board height: ");
        int strict = 0; foreach (var kv in strictCount) { strict += kv.Value; sb.Append($"{kv.Key}={kv.Value}; "); }
        sb.Append($"({strict} in all)");
        sb.Append("\nAUDIT drawn-with (per solid kind: what its nearest drawn surface is drawn with, on how many of them; STRIPPED = drawn instanced with a shader whose instanced variants the iOS build has been stripping):");
        foreach (var kv in drawnWith)
        {
            int all = 0, hit = 0; foreach (var e in kv.Value) { all += e.Value; if (e.Key.StartsWith("STRIPPED")) hit += e.Value; }
            sb.Append($"\n  {(hit > 0 ? $"AFFECTED {hit}/{all} " : "")}{kv.Key}: ");
            foreach (var e in kv.Value) sb.Append($"{e.Key} x{e.Value}; ");
        }
        sb.Append($"\nAUDIT crashes: {crashes}, with nothing drawn within 1.5 m of the contact: {invisible} (night={night} slide={withSlide} storm={storm}, caught by the slide {caught})\n  by cause: ");
        foreach (var kv in crashCount) sb.Append($"{kv.Key}={kv.Value} ");
        sb.Append("\n  invisible: ");
        foreach (var kv in invisibleCount) sb.Append($"{kv.Key}={kv.Value}; ");
        Debug.Log(sb.ToString());
    }
}
