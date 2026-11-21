using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Pictures of the jagged rock spires on the cliffs (CliffSpires), from fixed cameras so a run before and after a
/// change can be compared frame for frame: along a ridge's cliff edge, a ridge's canyon wall from below, along the
/// top of a deadly wall, the deadly wall from its terrace, and a jump cliff from the side.
///   Unity -batchmode -projectPath . -executeMethod SpireShot.Run -logFile spires.log
///   (no -nographics: it needs the GPU). Writes SpireShot*.png to $SPIRE_SHOTS, or the project folder.
/// Also logs how many spires, colliders and triangles each cycle carries, and checks that none of them stands on
/// the piste, the crest, or where a fall off a ridge gets up (Ridge.SafeX).
/// </summary>
public static class SpireShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures();
        SummitImport.EnsureSky();
        SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var fx = Object.FindFirstObjectByType<SnowFX>();
        rider.input = null;
        var cam = Camera.main;
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var draw = typeof(Forest).GetMethod("Draw", NP);
        string outDir = System.Environment.GetEnvironmentVariable("SPIRE_SHOTS");
        if (string.IsNullOrEmpty(outDir)) outDir = ".";
        Directory.CreateDirectory(outDir);

        // a mountain whose first ridge has cliffs on both sides, and whose first cycle has a deadly wall and a jump
        int seed = -1;
        for (int s = 1; s < 400 && seed < 0; s++)
        {
            Slope.Init(s);
            if (Ridge.Sides(0) == 0 && Cliffs.WallOf(0, out _) && Cliffs.JumpOf(0, 0, out _)) seed = s;
        }
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild(); dress.Rebuild();

        void Place(float x, float z)
        {
            rider.ResetAt(x, z); body.ResetVisual();
            terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
            terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
            body.Apply(1f / 60f);
            fx.Clear();
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            draw.Invoke(props, null);
        }
        void Shot(string name, Vector3 eye, Vector3 at, float fov)
        {
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            Bake(() => Shoot(cam, Path.Combine(outDir, name)));
        }

        float zr = Course.First + Ridge.At, zFull = zr + Ridge.RampIn + 10f;
        Debug.Log($"SPIRESHOT seed {seed} ridge z={zr:0}");
        // the warm-up render comes out unlit; throw it away
        Place(Slope.Centre(zr - 60f), zr - 60f);
        Shoot(cam, Path.Combine(outDir, "SpireShotWarmup.png"));

        // 1. along a ridge's cliff edge, from just off the crest, looking down the run
        {
            float z = zFull + 30f;
            float half = Ridge.HalfWidth(z);
            float ex = Slope.Centre(z) + (half + 0.5f);
            Place(Slope.Centre(z - 10f), z - 10f);
            var eye = new Vector3(Slope.Centre(z) + half - 1f, Ridge.Top(z) + 2.4f, z);
            var at = new Vector3(Slope.Centre(z + 40f) + half + 6f, Ridge.Top(z + 40f) - 9f, z + 40f);
            Shot("SpireShotRidgeEdge.png", eye, at, 62f);
            // the same edge from out over the canyon, looking back at the wall and the crest along its top
            float x = Slope.Centre(z + 30f) + Ridge.Reach(z + 30f, 1f) + 20f;
            var eye2 = new Vector3(x, Ridge.Top(z + 30f) - 25f, z - 30f);
            var at2 = new Vector3(Slope.Centre(z + 40f) + half + 25f, Ridge.Top(z + 40f) - 45f, z + 40f);
            Shot("SpireShotRidgeFace.png", eye2, at2, 62f);
        }
        // 2. the ridge's canyon wall from the gully floor
        {
            float z = zFull + 70f;
            float x = Slope.Centre(z) - (Ridge.Reach(z, -1f) + 45f);
            Place(x, z);
            var eye = new Vector3(x, Slope.Height(x, z) + 45f, z - 45f);
            var at = new Vector3(Slope.Centre(z + 25f), Ridge.Top(z + 25f) - 25f, z + 25f);
            Shot("SpireShotRidgeWall.png", eye, at, 65f);
        }
        // 3. a deadly wall: along its top from the piste's edge, and from the terrace below
        if (Cliffs.WallOf(0, out var w))
        {
            float z = w.z1 + 10f;
            float ex = Slope.Centre(z) + w.side * w.edge;
            Place(Slope.Centre(z - 20f), z - 20f);
            var eye = new Vector3(ex - w.side * 3f, Slope.Height(ex - w.side * 3f, z - 20f) + 2.5f, z - 20f);
            var at = new Vector3(Slope.Centre(z + 25f) + w.side * (w.edge + 4f), Slope.Height(ex, z + 25f) - 3f, z + 25f);
            Shot("SpireShotWallTop.png", eye, at, 62f);
            float zt = w.z1 + 30f;
            float tx = Slope.Centre(zt) + w.side * (w.edge + w.height * 0.55f + 45f);
            Place(tx, zt);
            var eye2 = new Vector3(tx, Slope.Height(tx, zt) + 30f, zt - 45f);
            var at2 = new Vector3(Slope.Centre(zt + 15f) + w.side * w.edge, Slope.Height(Slope.Centre(zt) + w.side * w.edge, zt + 15f) - w.height * 0.4f, zt + 15f);
            Shot("SpireShotWallBelow.png", eye2, at2, 65f);
            Debug.Log($"SPIRESHOT wall side={w.side} edge={w.edge:0.0} height={w.height:0}");
        }
        // 4. a jump cliff from the side, out in the rough
        if (Cliffs.JumpOf(0, 0, out var j))
        {
            // from the landing, looking back up at the lip and its rock band running on into the rough
            float z = j.z + j.face + 22f;
            float x = Slope.Centre(z) - Slope.PisteHalf * 0.6f;
            Place(Slope.Centre(j.z - 15f), j.z - 15f);
            var eye = new Vector3(x, Slope.Height(x, z) + 3f, z);
            float lx = Slope.Centre(j.z) - Slope.PisteHalf - 12f;
            var at = new Vector3(lx, Slope.Height(lx, j.z) - 1f, j.z);
            Shot("SpireShotJump.png", eye, at, 60f);
        }

        // 5. a fall off the ridge's edge into the spires, and where the body ends up
        {
            const float Dt = 1f / 120f;
            float z0 = zFull + 108f, side = -1f;
            rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
            rider.Body.linearVelocity = Vector3.forward * 18f;
            terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
            Physics.SyncTransforms();
            for (int i = 0; i < 120 * 12; i++)
            {
                rider.Steer = rider.Crashed ? 0f : side * 0.9f;
                rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                if (i % 4 == 0) body.Apply(4 * Dt);
                if (i % 60 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (rider.Crashed && body.Dead && !body.RagdollMoving && i > 600) break;
            }
            body.Apply(1f / 60f);
            Vector3 c = body.RagdollCentre;
            Debug.Log($"SPIRESHOT fall ended {Ridge.Top(c.z) - c.y:0} m below the crest, dead={body.Dead}");
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            draw.Invoke(props, null);
            // from out over the canyon, level with it
            var eye = c + new Vector3(side * 22f, 4f, -14f);
            Shot("SpireShotFall.png", eye, c + Vector3.up * 1.5f, 45f);
        }

        Stats();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// The cliff rock (CliffSpires) by day and by night from four places: along a ridge's cliff edge from its crest,
    /// a ridge's cliff from the canyon floor, a gorge's wall and rim from its floor, and a ridge from far off.
    ///   Unity -batchmode -projectPath . -executeMethod SpireShot.Crags -logFile crags.log
    ///   Writes Crag*{Day,Night}.png to $SPIRE_SHOTS, or the project folder.
    /// </summary>
    public static void Crags()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props"); var dayNight = Get<DayNight>("dayNight");
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var draw = typeof(Forest).GetMethod("Draw", NP);
        rider.input = null;
        var cam = Camera.main;
        cam.GetComponent<ChaseCamera>().enabled = false;
        string outDir = System.Environment.GetEnvironmentVariable("SPIRE_SHOTS");
        if (string.IsNullOrEmpty(outDir)) outDir = ".";
        Directory.CreateDirectory(outDir);
        int SeedOf(Ridge.Form f) { for (int s = 1; s < 2000; s++) { Slope.Init(s); if (Ridge.Built(0) && Ridge.FormOf(0) == f) return s; } return 1; }
        bool warm = false;
        void View(string file, Vector3 eye, Vector3 at, float fov, bool night)
        {
            TimeOfDay.TestClock = night ? 3.4f : 1.4f; dayNight.ApplyNow();
            Vector3 flat = at - eye; flat.y = 0f;
            float rz = eye.z - flat.normalized.z * 2.5f, rx = eye.x - flat.normalized.x * 2.5f;
            rider.ResetAt(rx, rz); body.ResetVisual();
            rider.Heading = Mathf.Clamp(Mathf.Atan2(flat.x, flat.z), -1.4f, 1.4f);
            for (int k = 0; k < 5; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
            body.Apply(1f / 120f);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            draw.Invoke(props, null);
            dayNight.ApplyNow();
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            if (!warm) { Shoot(cam, Path.Combine(outDir, "CragWarmup.png")); warm = true; }   // the first render comes out unlit
            Bake(() => Shoot(cam, Path.Combine(outDir, file)));
        }
        int ridgeSeed = SeedOf(Ridge.Form.Ridge), gorgeSeed = SeedOf(Ridge.Form.Valley);
        foreach (bool night in new[] { false, true })
        {
            string when = night ? "Night" : "Day";
            Slope.Init(ridgeSeed);
            terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
            float zr = Course.First + Ridge.At + 150f;
            float half = Ridge.HalfWidth(zr);
            // 1. along the cliff's edge, from the crest just in from it
            View($"CragEdge{when}.png", new Vector3(Slope.Centre(zr) + half - 1.5f, Ridge.Top(zr) + 2.4f, zr - 14f),
                 new Vector3(Slope.Centre(zr + 30f) + half + 9f, Ridge.Top(zr + 30f) - 14f, zr + 30f), 66f, night);
            // 2. the ridge's cliff from the canyon floor, looking up it
            float x0 = Slope.Centre(zr) + Ridge.Reach(zr, 1f) + 40f;
            View($"CragWall{when}.png", new Vector3(x0, Slope.Height(x0, zr) + 45f, zr - 50f),
                 new Vector3(Slope.Centre(zr + 20f) + half + 12f, Ridge.Top(zr + 20f) - 60f, zr + 20f), 66f, night);
            // 4. the ridge from far off across the canyon
            View($"CragFar{when}.png", new Vector3(Slope.Centre(zr) + 230f, Ridge.Top(zr) + 20f, zr - 120f),
                 new Vector3(Slope.Centre(zr + 60f), Ridge.Top(zr + 60f) - 70f, zr + 60f), 55f, night);
            // 3. a gorge: its wall and rim from the floor
            Slope.Init(gorgeSeed);
            terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
            float zg = Course.First + Ridge.At + 220f;
            float hg = Ridge.HalfWidth(zg);
            View($"CragGorge{when}.png", new Vector3(Slope.Centre(zg) - hg * 0.3f, Slope.Height(Slope.Centre(zg), zg) + 1.8f, zg),
                 new Vector3(Slope.Centre(zg + 35f) + hg + 10f, Slope.Height(Slope.Centre(zg + 35f), zg + 35f) + 22f, zg + 35f), 64f, night);
        }
        Stats();
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// The crag beside the run up close, as the player saw it from a one-sided gorge's floor (its wall on one side,
    /// a treed snow bank on the other): from the floor a few metres off, looking down the run past it, and a close
    /// pass of the same block. By day and by night.
    ///   Unity -batchmode -projectPath . -executeMethod SpireShot.Detail -logFile detail.log
    ///   Writes Detail*{Day,Night}.png to $SPIRE_SHOTS.
    /// </summary>
    public static void Detail()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props"); var dayNight = Get<DayNight>("dayNight");
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var draw = typeof(Forest).GetMethod("Draw", NP);
        rider.input = null;
        var cam = Camera.main;
        cam.GetComponent<ChaseCamera>().enabled = false;
        string outDir = System.Environment.GetEnvironmentVariable("SPIRE_SHOTS");
        if (string.IsNullOrEmpty(outDir)) outDir = ".";
        Directory.CreateDirectory(outDir);
        // a mountain whose first deadly wall is built: from its terrace the wall runs on down the slope beside you,
        // the run's forest on the other side, as in the player's screenshot
        int seed = 1;
        for (int s = 1; s < 2000; s++) { Slope.Init(s); if (Cliffs.WallOf(0, out var w0) && w0.height > 35f) { seed = s; break; } }
        Slope.Init(seed);
        Cliffs.WallOf(0, out var w);
        bool warm = false;
        void View(string file, Vector3 eye, Vector3 at, float fov, bool night)
        {
            TimeOfDay.TestClock = night ? 3.4f : 1.4f; dayNight.ApplyNow();
            Vector3 flat = at - eye; flat.y = 0f;
            float rz = eye.z - flat.normalized.z * 2.5f, rx = eye.x - flat.normalized.x * 2.5f;
            rider.ResetAt(rx, rz); body.ResetVisual();
            rider.Heading = Mathf.Clamp(Mathf.Atan2(flat.x, flat.z), -1.4f, 1.4f);
            for (int k = 0; k < 5; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
            body.Apply(1f / 120f);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            draw.Invoke(props, null);
            CliffSpires.DrawNow();
            dayNight.ApplyNow();
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            if (!warm) { Shoot(cam, Path.Combine(outDir, "DetailWarmup.png")); warm = true; }
            Bake(() => Shoot(cam, Path.Combine(outDir, file)));
        }
        terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
        float zv = w.z1 + 20f;
        float foot = w.edge + w.height * 0.55f;
        rider.ResetAt(Slope.Centre(zv) + w.side * (foot + 5f), zv);
        for (int k = 0; k < 4; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
        Physics.SyncTransforms();
        // the biggest block standing low on the wall, near where the terrace begins
        Collider best = null; float bestSize = 0f;
        foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
        {
            if (c.name != "SpireHit") continue;
            var bb = c.bounds;
            if (bb.center.z < w.z1 || bb.center.z > w.z1 + 70f) continue;
            float a = (bb.center.x - Slope.Centre(bb.center.z)) * w.side;
            if (a < w.edge + 3f || a > foot + 6f) continue;
            float size = Mathf.Min(bb.size.y, 18f) * Mathf.Min(bb.size.x, bb.size.z) - (foot - a) * 0.5f;
            if (size > bestSize) { bestSize = size; best = c; }
        }
        Vector3 rc = best != null ? best.bounds.center : new Vector3(Slope.Centre(zv) + w.side * (foot - 4f), 0f, zv);
        if (best == null) rc.y = Slope.Height(rc.x, rc.z) + 4f;
        Debug.Log($"SPIRESHOT detail seed {seed}, wall side {w.side} edge {w.edge:0.0} height {w.height:0}, block at {rc} ({(best != null ? best.bounds.size.ToString() : "none")})");
        foreach (bool night in new[] { false, true })
        {
            string when = night ? "Night" : "Day";
            // standing on the terrace by the wall's foot, looking down the run along it (the player's view)
            float ze = zv - 14f;
            float xe = Slope.Centre(ze) + w.side * (foot + 7f);
            var eye = new Vector3(xe, Slope.DrawnHeight(xe, ze) + 1.7f, ze);
            float za = zv + 26f, xa = Slope.Centre(za) + w.side * (foot + 1f);
            var at = new Vector3(xa, Slope.DrawnHeight(xa, za) + 5f, za);
            View($"DetailRun{when}.png", eye, at, 64f, night);
            // a close pass: further down the same wall, a few metres off its foot
            float zc = zv + 2f;
            float xc = Slope.Centre(zc) + w.side * (foot + 6f);
            var eye2 = new Vector3(xc, Slope.DrawnHeight(xc, zc) + 2.2f, zc);
            float zt = zc + 14f, xt = Slope.Centre(zt) + w.side * (foot - 2f);
            rc = new Vector3(xt, Slope.DrawnHeight(xt, zt) + 4f, zt);
            View($"DetailClose{when}.png", eye2, rc, 64f, night);
        }
        Stats();
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Do thrown bodies hit the spires? Rides off a ridge's cliff edge at a dozen places along it, and watches each
    /// tumble: which of the body's parts come into contact with a spire's collider (batchmode fires no collision
    /// callbacks, so contact is measured: ComputePenetration against the spires near each part), how deep any
    /// part ever sinks into one (it should be bounced off, not pass through), and how the fall ends.
    ///   Unity -batchmode -nographics -projectPath . -executeMethod SpireShot.Hit -logFile hit.log
    /// </summary>
    public static void Hit()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        rider.input = null;
        const float Dt = 1f / 120f;
        string why = "";
        rider.Crash += w => why = w;
        int seed = -1;
        for (int s = 1; s < 400 && seed < 0; s++) { Slope.Init(s); if (Ridge.Sides(0) == 0) seed = s; }
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
        float zr = Course.First + Ridge.At, zFull = zr + Ridge.RampIn + 10f;
        {
            // what building one cycle's spires costs (the shapes are already made by the rebuild above)
            var tmp = new GameObject("SpireTiming").transform;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CliffSpires.Build(tmp, 0, Shader.Find("Universal Render Pipeline/Lit"));
            sw.Stop();
            Debug.Log($"SPIREHIT building cycle 0's spires took {sw.Elapsed.TotalMilliseconds:0.0} ms ({tmp.GetComponentsInChildren<Collider>().Length} spires, {tmp.childCount} groups)");
            Object.DestroyImmediate(tmp.gameObject);
        }
        var near = new Collider[32];
        int falls = 0, touched = 0, deep = 0;
        float worst = 0f;
        for (int n = 0; n < 12; n++)
        {
            float z0 = zFull + 12f * n;
            float side = n % 2 == 0 ? 1f : -1f;
            rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
            rider.Body.linearVelocity = Vector3.forward * 18f;
            for (int k = 0; k < 3; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
            if (System.Environment.GetEnvironmentVariable("NOBOX") == "1")
                foreach (var t in dress.GetComponentsInChildren<BoxCollider>()) if (t.name == "RidgeRock") t.enabled = false;
            Physics.SyncTransforms();
            why = "";
            int contacts = 0, boxSteps = 0; float maxPen = 0f; string what = "";
            int i = 0;
            for (; i < 120 * 12; i++)
            {
                rider.Steer = rider.Crashed ? 0f : side * 0.9f;
                rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                if (i % 4 == 0) body.Apply(4 * Dt);
                if (i % 60 == 0) { terrain.Prewarm(); props.Prewarm(); }
                if (!rider.Crashed) continue;
                foreach (var rb in body.RagdollForTest.Bodies)
                {
                    var own = rb.GetComponent<Collider>();
                    if (own == null) continue;
                    int k = Physics.OverlapSphereNonAlloc(rb.worldCenterOfMass, 1.2f, near, ~0, QueryTriggerInteraction.Ignore);
                    for (int c = 0; c < k; c++)
                    {
                        if (near[c].name == "RidgeRock") { if (Physics.ComputePenetration(own, own.transform.position, own.transform.rotation, near[c], near[c].transform.position, near[c].transform.rotation, out _, out _)) boxSteps++; continue; }
                        if (near[c].name != "SpireHit") continue;
                        if (Physics.ComputePenetration(own, own.transform.position, own.transform.rotation, near[c], near[c].transform.position, near[c].transform.rotation, out _, out float dist))
                        { contacts++; maxPen = Mathf.Max(maxPen, dist); what = near[c].name; }
                        else if (near[c].ClosestPoint(rb.worldCenterOfMass) == rb.worldCenterOfMass) { contacts++; maxPen = Mathf.Max(maxPen, 1f); }
                    }
                }
                if (i > 120 * 3 && !body.RagdollMoving && body.Dead) break;
            }
            if (!rider.Crashed) { Debug.Log($"SPIREHIT ride {n}: never went over"); continue; }
            falls++;
            if (contacts > 0) touched++;
            if (maxPen > 0.25f) deep++;
            worst = Mathf.Max(worst, maxPen);
            Vector3 c0 = body.RagdollCentre;
            Debug.Log($"SPIREHIT ride {n} side {side}: crash '{why}', spire contact steps {contacts}, outcrop contact steps {boxSteps}, deepest {maxPen:0.00} m, ended {Ridge.Top(c0.z) - c0.y:0} m below the crest, off={c0.x - Slope.Centre(c0.z):0.0}, dead={body.Dead}, {i * Dt:0.0} s");
        }
        Debug.Log($"SPIREHIT summary: {falls} falls, {touched} hit spires, {deep} sank over 25 cm into one (deepest {worst:0.00} m)");
        EditorApplication.Exit(0);
    }

    /// <summary>What the spires cost, and that none of them is somewhere it must not be.</summary>
    static void Stats()
    {
        Physics.SyncTransforms();
        int cols = 0, groups = 0, nearDraws = 0, farDraws = 0, boulders = 0; long nearTris = 0, farTris = 0;
        float zMin = float.MaxValue, zMax = float.MinValue;
        foreach (var g in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (g.name != "CliffSpires") continue;
            groups++;
            if (g.GetComponent<Obstacle>() == null || g.GetComponent<Obstacle>().label != "Rock") Debug.Log("SPIRESHOT BAD group without a Rock obstacle");
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
            {
                long t = 0;
                for (int sm = 0; sm < mf.sharedMesh.subMeshCount; sm++) t += mf.sharedMesh.GetIndexCount(sm) / 3;
                if (mf.name == "Near") { nearTris += t; nearDraws++; } else { farTris += t; farDraws++; }
            }
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) { cols++; if (c is MeshCollider hc && hc.sharedMesh != null && hc.sharedMesh.name.StartsWith("TalusHull")) boulders++; zMin = Mathf.Min(zMin, c.bounds.min.z); zMax = Mathf.Max(zMax, c.bounds.max.z); }
        }
        Debug.Log($"SPIRESHOT stats: {groups} groups, {cols} blocks with colliders ({boulders} scanned boulders, instanced), near look {nearDraws} meshes {nearTris} tris, far look {farDraws} meshes {farTris} tris, z {zMin:0}-{zMax:0}");
        var hits = new Collider[16];
        bool Rock(Vector3 p, float r)
        {
            int n = Physics.OverlapSphereNonAlloc(p, r, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (hits[i].name == "SpireHit" || hits[i].name == "RidgeRock") return true;
            return false;
        }
        // nothing on the groomed piste or a crest: a rider-sized sphere swept over every half metre of it
        int line = 0, scanned = 0;
        for (float z = zMin; z < zMax; z += 0.5f)
        {
            float clear = Ridge.Depth(z) > 0.05f ? Ridge.HalfWidth(z) + 0.5f : Slope.PisteHalf + 1f;
            for (float a = -clear; a <= clear; a += 0.5f)
            {
                float x = Slope.Centre(z) + a, y = Slope.Height(x, z);
                scanned++;
                if (Rock(new Vector3(x, y + 0.5f, z), 0.45f) || Rock(new Vector3(x, y + 1.4f, z), 0.45f))
                { line++; if (line < 10) Debug.Log($"SPIRESHOT BAD rock on the line at x={a:0.0} z={z:0}"); }
            }
        }
        // wherever a fall on a ridge cliff ends, the rider gets up clear of every rock (Ridge.SafeX)
        int getUp = 0, falls = 0;
        for (float z = zMin; z < zMax; z += 2f)
        {
            if (Ridge.Depth(z) <= 0.05f) continue;
            for (int si = -1; si <= 1; si += 2)
            {
                if (!Ridge.CliffSide(z, si)) continue;
                for (float d = Ridge.HalfWidth(z); d < Ridge.Reach(z, si); d += 1.5f)
                {
                    float sx = Ridge.SafeX(Slope.Centre(z) + si * d, z);
                    falls++;
                    if (Rock(new Vector3(sx, Slope.Height(sx, z) + 0.9f, z), 0.6f))
                    { getUp++; if (getUp < 10) Debug.Log($"SPIRESHOT BAD get-up inside a rock at x={sx - Slope.Centre(z):0.0} z={z:0}"); }
                }
            }
        }
        Debug.Log($"SPIRESHOT fair: {line} rocks on the line ({scanned} spots), {getUp} get-ups inside rocks ({falls} falls)");
    }

    /// <summary>Batchmode renders do not re-skin meshes: bake them for the shot, then put them back.</summary>
    static void Bake(System.Action shoot)
    {
        var baked = new System.Collections.Generic.List<GameObject>();
        var skins = new System.Collections.Generic.List<SkinnedMeshRenderer>();
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
        shoot();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
    }

    static void Shoot(Camera cam, string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        Debug.Log("SPIRESHOT wrote " + file);
    }
}
