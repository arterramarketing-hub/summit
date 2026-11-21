using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Where the lifts go, seen from the air, and the lift over the gorge (LiftGorge.cs) from the piste, from its cable
/// over the drop, and from the far side; by day and by night. Built against main too (the gorge line is found by
/// reflection), so the same overviews can be taken before and after. One picture each editor frame.
/// Run: Unity -batchmode -projectPath . -executeMethod LiftRoutesShot.Run -logFile x.log   (no -quit)
///   SUMMIT_ROUTES_SEEDS "1000,5000": overviews of these mountains, round their gorge line (or their second cycle)
///   SUMMIT_ROUTES_GORGE 5000: the gorge views on this mountain (none if 0, or if it has no gorge line)
///   SUMMIT_ROUTES_TAG, SUMMIT_ROUTES_OUT: a prefix for the pictures, and the folder they go in
/// </summary>
public static class LiftRoutesShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static CourseDressing dress;
    static Camera cam; static ChaseCamera chase; static Atmosphere atmo; static DayNight dayNight; static SkiLift lift;
    static MethodInfo tUpd, fUpd, lUpd;
    static string tag, outDir;
    static readonly List<System.Action> steps = new List<System.Action>();
    static int at;

    public static void Run()
    {
        tag = System.Environment.GetEnvironmentVariable("SUMMIT_ROUTES_TAG") ?? "after";
        outDir = System.Environment.GetEnvironmentVariable("SUMMIT_ROUTES_OUT") ?? ".";
        string seedList = System.Environment.GetEnvironmentVariable("SUMMIT_ROUTES_SEEDS") ?? "1000,5000";
        int gorgeSeed = int.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_ROUTES_GORGE"), out int gs) ? gs : 5000;
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight");
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        lift = Object.FindFirstObjectByType<SkiLift>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); lUpd = typeof(SkiLift).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        SkiLift.TestClock = 100.0;

        steps.Clear(); at = 0;
        foreach (var s in seedList.Split(','))
        {
            int seed = int.Parse(s);
            steps.Add(() => Mountain(seed));
            steps.Add(() => { TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); Overview(seed, true); });   // (warm-up: the first picture of a mountain is taken before all of it is in)
            steps.Add(() => { TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); Overview(seed, false); });
        }
        if (gorgeSeed != 0)
        {
            steps.Add(() => Mountain(gorgeSeed));
            foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
                foreach (var view in new[] { "piste", "cable", "far" })
                    steps.Add(() => { TimeOfDay.TestClock = clock; dayNight.ApplyNow(); Gorge(gorgeSeed, view, when); });
        }
        steps.Add(() =>
        {
            SkiLift.TestClock = -1.0;
            TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
            TimeOfDay.TestClock = -1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            QualitySettings.SetQualityLevel(quality, false);
        });
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (at < steps.Count)
        {
            try { steps[at++](); }
            catch (System.Exception e) { Debug.LogError("ROUTES exception " + e); at = steps.Count; }
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    static void Mountain(int seed)
    {
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        lift.Clear();
        // (batchmode refuses Destroy: the last mountain's lifts would stand on in this one)
        for (int i = lift.transform.childCount - 1; i >= 0; i--) if (lift.transform.GetChild(i).name.StartsWith("Lift ")) Object.DestroyImmediate(lift.transform.GetChild(i).gameObject);
        var sb = new System.Text.StringBuilder();
        foreach (var l in LiftLine.Lifts) sb.Append($" {l.k}{(l.second ? "b" : "")}: z {l.top.z:0}..{l.bottom.z:0} ({l.offTop:+0;-0}, {l.offBottom:+0;-0}){(Gorge(l) >= 0 ? " GORGE" : "")};");
        Log($"seed {seed}: {LiftLine.Lifts.Length} lifts:{sb}");
    }

    static readonly FieldInfo gorgeField = typeof(LiftLine.Lift).GetField("gorge");
    static int Gorge(LiftLine.Lift l) => gorgeField != null ? (int)gorgeField.GetValue(l) : -1;

    /// <summary>The stretch from 60 m above a gorge line's top station to 60 m below its bottom (or the mountain's second cycle), from the air.</summary>
    static void Overview(int seed, bool warm)
    {
        float z0 = Course.First + Course.Cycle + 300f - Course.Cycle, z1 = z0 + 960f;
        // (the same stretch before and after: round the gorge line if this mountain has one now, as told by the env)
        if (float.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_ROUTES_Z" + seed), out float zz)) { z0 = zz; z1 = zz + 960f; }
        else foreach (var l in LiftLine.Lifts) if (Gorge(l) >= 0) { z0 = l.top.z - 60f; z1 = Mathf.Max(l.bottom.z + 60f, z0 + 700f); break; }
        float zm = (z0 + z1) * 0.5f, xm = Slope.Centre(zm);
        // the terrain and the forest drawn out over the whole of it
        int side = terrain.sideChunks, ahead = terrain.aheadChunks, behind = terrain.behindChunks, fSide = props.farSide, fAhead = props.farAhead;
        terrain.sideChunks = 9; terrain.aheadChunks = Mathf.CeilToInt((z1 - z0) / terrain.chunkSize) + 3; terrain.behindChunks = 3;
        props.farSide = 70; props.farAhead = 170;
        float behindLift = lift.behind; lift.behind = 400f;
        Stand(new Vector3(xm, 0f, z0 + 20f), 100.0);
        for (int k = 0; k < 16; k++) terrain.Prewarm();   // (48 chunks a time)
        // straight down from high over its middle, downhill at the top of the picture, no haze
        Vector3 mid = new Vector3(xm, Slope.Height(xm, zm), zm);
        Vector3 eye = mid + Vector3.up * 1150f;
        cam.fieldOfView = 52f;
        float far = cam.farClipPlane; cam.farClipPlane = 4000f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.down, Vector3.forward));
        bool fog = RenderSettings.fog; RenderSettings.fog = false;
        var marks = Marks();
        // (the warm-up picture is thrown away: it is what gets the overlay's shader and the far terrain ready)
        Shoot(Path.Combine(outDir, warm ? "LiftRoutes_warmup.png" : $"{tag}-overview-{seed}.png"));
        if (!warm) Log($"{tag} overview seed {seed}: z {z0:0}..{z1:0}");
        RenderSettings.fog = fog; cam.farClipPlane = far;
        if (marks != null) Object.DestroyImmediate(marks);
        terrain.sideChunks = side; terrain.aheadChunks = ahead; terrain.behindChunks = behind; props.farSide = fSide; props.farAhead = fAhead; lift.behind = behindLift;
    }

    /// <summary>
    /// For the overview only: each line drawn over the ground as a strip, yellow for a cycle's first line, cyan for its
    /// second, red for a span over a gorge; each station a white square.
    /// </summary>
    static GameObject Marks()
    {
        var root = new GameObject("RouteMarks");
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        Material M(Color c) { var m = new Material(sh); m.SetColor("_BaseColor", c); return m; }
        var first = M(new Color(1f, 0.82f, 0.1f)); var second = M(new Color(0.1f, 0.85f, 1f)); var gorge = M(new Color(1f, 0.15f, 0.1f)); var station = M(Color.white);
        foreach (var l in LiftLine.Lifts)
        {
            int g = Gorge(l);
            for (int i = 0; i < l.Spans; i++)
            {
                Vector3 a = l.At(l.nodeS[i], 0f, 0f), b = l.At(l.nodeS[i + 1], 0f, 0f);
                a.y = l.CableY(i, l.nodeS[i]) + 8f; b.y = l.CableY(i, l.nodeS[i + 1]) + 8f;
                var q = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(root.transform, false);
                q.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(b - a));
                q.transform.localScale = new Vector3(5f, 1f, (b - a).magnitude);
                q.GetComponent<MeshRenderer>().sharedMaterial = i == g ? gorge : l.second ? second : first;
            }
            foreach (var st in new[] { l.top, l.bottom })
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(c.GetComponent<Collider>());
                c.transform.SetParent(root.transform, false);
                c.transform.SetPositionAndRotation(st + Vector3.up * 12f, Quaternion.LookRotation(l.dir));
                c.transform.localScale = new Vector3(16f, 2f, 16f);
                c.GetComponent<MeshRenderer>().sharedMaterial = station;
            }
        }
        return root;
    }

    static void Gorge(int seed, string view, string when)
    {
        LiftLine.Lift l = null;
        foreach (var c in LiftLine.Lifts) if (Gorge(c) >= 0) { l = c; break; }
        if (l == null) { Log($"seed {seed}: no gorge line"); return; }
        int i = Gorge(l);
        float sa = l.nodeS[i], sb = l.nodeS[i + 1];
        Vector3 rim = l.head[i - 1], far = l.head[i];
        float cs = Mathf.Sign(rim.x - Slope.Centre(rim.z));
        Vector3 up = Vector3.up;
        if (view == "piste")
        {
            // riding the piste a little above the rim tower, looking down the run with the gorge opening to the side
            // and the line going out over it (as in the screenshot)
            // (on the crest's way in, past the danger stretch and its crevasses, the rim tower a little ahead)
            float z = Mathf.Max(rim.z - 35f, Course.First + Course.CycleOf(rim.z) * Course.Cycle + Course.PipeAt + 4f);
            Vector3 r = new Vector3(Slope.Centre(z) - cs * 2f, 0f, z);
            Stand(r, 100.0);
            Vector3 at = rider.transform.position;
            Vector3 fwd = new Vector3(Slope.Centre(z + 30f) - Slope.Centre(z), 0f, 30f).normalized;
            Vector3 eye = at - fwd * 8f + up * 3.4f - new Vector3(cs * 3f, 0f, 0f);
            Vector3 look = at + fwd * 60f + new Vector3(cs * 32f, -17f, 0f);
            cam.fieldOfView = 76f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        }
        else if (view == "cable")
        {
            // grinding the cable over the deepest of the drop: the rider on it, looking past them and down
            float s = sa + (sb - sa) * 0.42f;
            Vector3 on = l.At(s, LiftLine.Half, l.CableY(i, s));
            Stand(new Vector3(on.x, 0f, on.z), 100.0);
            Grind(l, i, s);
            Vector3 at = rider.transform.position;
            Vector3 eye = at - l.dir * 6.5f + up * 2.3f;
            Vector3 look = at + l.dir * 26f + l.across * (cs * 6f) - up * 12f;
            cam.fieldOfView = 80f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            Log($"{tag} gorge cable: the rider {rider.AboveGround:0} m over the ground, grinding {rider.Grinding}");
        }
        else
        {
            // from the far side: on the line's cleared swath below the floor tower, looking back up the span to the rim
            Vector3 eyeG = l.At(sb + 45f, -cs * 6f, 0f);
            eyeG.y = Slope.Height(eyeG.x, eyeG.z) + 2.2f;
            Stand(new Vector3(eyeG.x, 0f, eyeG.z), 100.0);
            Vector3 look = Vector3.Lerp(far, rim, 0.6f) - up * 20f;
            cam.fieldOfView = 72f;
            cam.transform.SetPositionAndRotation(eyeG, Quaternion.LookRotation(look - eyeG));
        }
        Shoot(Path.Combine(outDir, $"{tag}-gorge-{view}-{when}.png"));
        lift.Stats(out int draws, out long tris, out int inst);
        Log($"{tag} gorge {view} {when}: {draws} draw calls, {tris} triangles, {inst} instances; span {sb - sa:0} m");
    }

    /// <summary>Put the rider on the cable at s along span i, as CableGrind boards it from the air, and let it run a moment.</summary>
    static void Grind(LiftLine.Lift l, int i, float s)
    {
        const float dt = 1f / 120f;
        Vector3 on = l.At(s, LiftLine.Half, l.CableY(i, s));
        float slope = LiftLine.CableSlope(l, i, s);
        Vector3 dir = (l.dir + Vector3.up * slope).normalized;
        CableGrind.Reset();
        rider.ResetAt(on.x, on.z); body.ResetVisual(); rider.Begin();
        rider.Body.position = on + Vector3.up * (Rider.R + 0.4f);
        rider.transform.position = rider.Body.position;
        rider.Airborne = true;
        rider.Heading = Mathf.Atan2(l.dir.x, l.dir.z);
        rider.Body.linearVelocity = dir * 26f - Vector3.up * 1f;
        for (int n = 0; n < 60; n++)
        {
            rider.Steer = 0f;
            rider.Tick(dt); body.PhysicsStep(); Physics.Simulate(dt);
            if (n % 4 == 0) body.Apply(4 * dt);
        }
        body.Apply(dt);
        rider.Stop();
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear(true);
    }

    static void Stand(Vector3 p, double clock)
    {
        rider.ResetAt(p.x, p.z); body.ResetVisual(); rider.Stop();
        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
        SkiLift.TestClock = clock;
        chase.enabled = false;
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
        typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
        for (int i = 0; i < 3; i++) lUpd.Invoke(lift, null);
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
        int w = 886, h = 1920;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }

    static void Log(string s) => Debug.Log("ROUTES " + s);
}
