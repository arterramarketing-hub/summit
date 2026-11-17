using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Checks and pictures of the ridges.
///   Probe: cross-sections, how steep the crest gets, at what speed its roll-overs would throw you, and that
///          nothing (kickers, crevasses, park features, trees, pickups) is built on a crest or its cliffs.
///          Unity -batchmode -nographics -projectPath . -executeMethod RidgeTest.Probe -logFile probe.log
///   Shots: renders riding a ridge, looking down a drop, the cliffs from below, a fall, and the sign.
///          Unity -batchmode -projectPath . -executeMethod RidgeTest.Shots -logFile shots.log
///          (no -nographics: it needs the GPU). Writes RidgeShot*.png to $RIDGE_SHOTS, or the project folder.
/// </summary>
public static class RidgeTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;

    public static void Probe()
    {
        int problems = 0;
        void Bad(string s) { problems++; Debug.Log("RIDGE BAD " + s); }
        for (int seed = 0; seed < 4; seed++)
        {
            Slope.Init(1000 + seed * 77);
            for (int cyc = 0; cyc < 5; cyc++)
            {
                if (!Ridge.Built(cyc)) { Debug.Log($"RIDGE seed{seed} cycle {cyc}: none"); continue; }
                float z0 = Course.First + cyc * Course.Cycle + Ridge.At, z1 = Course.First + cyc * Course.Cycle + Ridge.End;
                float zm = z0 + Ridge.RampIn + 30f;
                // a cross-section through the full crest
                var sb = new System.Text.StringBuilder();
                for (float a = -40f; a <= 40.01f; a += 4f)
                {
                    float x = Slope.Centre(zm) + a;
                    sb.Append($"{a:0}:{Slope.Height(x, zm) - Ridge.Top(zm):0.0}/{Slope.Normal(x, zm).y:0.00} ");
                }
                Debug.Log($"RIDGE seed{seed} cycle {cyc} sides={Ridge.Sides(cyc)} z={z0:0}-{z1:0} section at z={zm:0} (offset:height/normal.y): {sb}");
                // along the crest: the flattest it gets underfoot, and the tightest roll-over (the speed it would launch you at)
                float minNy = 1f, worstCurv = 0f, worstAt = 0f, maxWander = 0f;
                for (float z = z0; z < z1; z += 1f)
                {
                    float w = Ridge.Weight(z);
                    if (w < 0.3f) continue;
                    float half = Ridge.HalfWidth(z);
                    for (float a = -half; a <= half + 0.01f; a += half / 4f) minNy = Mathf.Min(minNy, Slope.Normal(Slope.Centre(z) + a, z).y);
                    // along the line you ride (the centre line), over 3 m: the surface curving away below a ballistic arc throws you
                    const float h = 3f;
                    float y0 = Slope.Height(Slope.Centre(z - h), z - h), y1 = Slope.Height(Slope.Centre(z), z), y2 = Slope.Height(Slope.Centre(z + h), z + h);
                    float curv = (y0 - 2f * y1 + y2) / (h * h);
                    if (curv < worstCurv) { worstCurv = curv; worstAt = z; }
                    maxWander = Mathf.Max(maxWander, Mathf.Abs(Slope.Centre(z + 1f) - Slope.Centre(z)));
                }
                float launch = worstCurv < 0f ? Mathf.Sqrt(Rider.G / -worstCurv) : 999f;
                Debug.Log($"RIDGE seed{seed} cycle {cyc}: crest min normal.y={minNy:0.00}, tightest roll-over at z={worstAt:0} launches above {launch * 3.6f:0} km/h, crest wanders up to {Mathf.Atan(maxWander) * Mathf.Rad2Deg:0} deg off the fall line");
                if (minNy < 0.7f) Bad($"seed{seed} cycle {cyc}: crest too steep underfoot (normal.y {minNy:0.00})");
                if (launch < 34f) Bad($"seed{seed} cycle {cyc}: crest roll-over at z={worstAt:0} launches at {launch * 3.6f:0} km/h");
                // nothing built on the ridge
                for (float z = z0; z < z1; z += 1f)
                {
                    if (Course.At(z) != Course.Section.Ridge) Bad($"seed{seed} z={z:0} is {Course.At(z)}, not Ridge");
                    for (float a = -30f; a <= 30f; a += 1f)
                    {
                        float x = Slope.Centre(z) + a;
                        if (Mathf.Abs(Slope.Kicker(x, z)) > 0.001f) { Bad($"seed{seed} kicker on the ridge at z={z:0} a={a:0}"); break; }
                        if (Course.CrevasseCut(x, z) > 0f) { Bad($"seed{seed} crevasse on the ridge at z={z:0} a={a:0}"); break; }
                    }
                    if (Mathf.Abs(Slope.Drops(z)) > 0.001f) Bad($"seed{seed} a drop on the ridge at z={z:0}");
                }
                foreach (var f in Course.FeaturesOf(cyc)) if (f.b.z > z0 && f.a.z < z1) Bad($"seed{seed} park feature on the ridge at z={f.a.z:0}");
                int trees = 0, rockCells = 0;
                for (int cz = Mathf.FloorToInt(z0 / Forest.Cell); cz <= Mathf.FloorToInt(z1 / Forest.Cell); cz++)
                    for (int cx = Mathf.FloorToInt((Slope.Centre(cz * Forest.Cell) - 45f) / Forest.Cell); cx <= Mathf.FloorToInt((Slope.Centre(cz * Forest.Cell) + 45f) / Forest.Cell); cx++)
                    {
                        var k = Forest.At(cx, cz, out Vector3 p, out _, out _);
                        if (k == Forest.Kind.None || Ridge.Weight(p.z) < 0.05f) continue;
                        float a = p.x - Slope.Centre(p.z);
                        if (Mathf.Abs(a) < Ridge.Reach(p.z, a < 0f ? -1f : 1f)) { if (k == Forest.Kind.Tree) trees++; else rockCells++; }
                    }
                if (trees + rockCells > 0) Bad($"seed{seed} cycle {cyc}: {trees} trees and {rockCells} rocks on the crest or its cliffs");
                for (int slot = 1; slot < 4000; slot++)
                {
                    if (!PowerUps.At(slot, out Vector3 pp, out _) || Ridge.Weight(pp.z) <= 0f) continue;
                    if (pp.z < z0 - 20f || pp.z > z1 + 20f) continue;
                    if (Mathf.Abs(pp.x - Slope.Centre(pp.z)) > Ridge.HalfWidth(pp.z)) Bad($"seed{seed} pickup off the crest at z={pp.z:0}");
                }
            }
        }
        Debug.Log($"RIDGE probe done: {problems} problem(s)");
        EditorApplication.Exit(0);
    }

    public static void Shots()
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
        var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var draw = typeof(Forest).GetMethod("Draw", NP);
        string outDir = System.Environment.GetEnvironmentVariable("RIDGE_SHOTS");
        if (string.IsNullOrEmpty(outDir)) outDir = ".";
        ChaseCamera.TestDt = 1f / 60f;

        // a mountain whose first ridge has cliffs on both sides, and one with a single cliff
        int both = -1, one = -1;
        for (int s = 1; s < 400 && (both < 0 || one < 0); s++)
        {
            Slope.Init(s);
            if (Ridge.Sides(0) == 0) { if (both < 0) both = s; }
            else if (one < 0) one = s;
        }
        foreach (int seed in new[] { both, one })
        {
            Slope.Init(seed);
            string tag = Ridge.Sides(0) == 0 ? "" : "OneSide";
            terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
            float zr = Course.First + Ridge.At;
            float zFull = zr + Ridge.RampIn + 10f;

            void Settle()
            {
                terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
                body.Apply(Dt);
                fx.Clear();
                typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
                draw.Invoke(props, null);
            }
            void Ride(float z, float v0, float secs, System.Func<float, float> steer)
            {
                rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
                rider.Body.linearVelocity = Vector3.forward * v0;
                terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
                int i = 0;
                for (float t = 0f; t < secs; t += Dt, i++)
                {
                    rider.Steer = steer(t);
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                    if (i % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                    if (i % 4 == 0) body.Apply(4 * Dt);
                    if (i % 2 == 0) late.Invoke(chase, null);
                }
            }
            float Follow(float dx)
            {
                Vector3 p = rider.transform.position;
                float look = Mathf.Max(7f, rider.Speed * 0.5f);
                float want = Mathf.Atan2(Slope.Centre(rider.Z + look) + dx - p.x, look);
                return Mathf.Clamp((want - rider.Heading) * 4f - rider.YawRate * 0.25f, -1f, 1f);
            }

            Debug.Log($"RIDGESHOT seed {seed} sides={Ridge.Sides(0)} ridge z={zr:0}-{Course.First + Ridge.End:0}");
            // the warm-up render comes out unlit; throw it away
            Ride(zr - 60f, 20f, 0.5f, t => 0f);
            Settle(); chase.Snap(); late.Invoke(chase, null);
            Shoot(cam, Path.Combine(outDir, "RidgeShotWarmup.png"));

            // 1. the sign, from the piste above it (it stands on the left, facing up the hill)
            {
                float zs = zr - 30f;
                Ride(zs - 45f, 18f, 0.1f, t => 0f);
                Settle();
                float sx = Slope.Centre(zs) - (Slope.PisteHalf - 1f), sy = Slope.Height(sx, zs);
                var eye = new Vector3(sx + 5f, Slope.Height(sx + 5f, zs - 16f) + 2.2f, zs - 16f);
                cam.fieldOfView = 55f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(sx + 2f, sy + 2.4f, zs) - eye));
                if (seed == both) Shoot(cam, Path.Combine(outDir, "RidgeShotSign.png"));
            }
            // 2. riding onto and along the crest, with the chase camera
            Ride(zr + 10f, 22f, 3.2f, t => Follow(0f));
            Settle(); chase.Snap(); late.Invoke(chase, null);
            Bake(() => Shoot(cam, Path.Combine(outDir, $"RidgeShotRide{tag}.png")));
            {
                // the same moment from higher up behind: the crest, and the drops either side of it
                Vector3 rp = rider.transform.position;
                var eye = new Vector3(Slope.Centre(rp.z - 16f), rp.y + 13f, rp.z - 16f);
                cam.fieldOfView = 60f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(rp + new Vector3(0f, -10f, 26f) - eye));
                Bake(() => Shoot(cam, Path.Combine(outDir, $"RidgeShotRideHigh{tag}.png")));
            }
            Debug.Log($"RIDGESHOT ride at z={rider.Z:0} off={rider.transform.position.x - Slope.Centre(rider.Z):0.0} speed={rider.Speed * 3.6f:0} km/h crashed={rider.Crashed} air={rider.Airborne}");
            // 3. looking down the drop from the crest's edge, the rider beside you
            {
                float z = zFull + 40f;
                int side = Ridge.Sides(0) == 0 ? 1 : Ridge.Sides(0);
                Ride(z - 20f, 12f, 1.2f, t => Follow(0f));
                Settle();
                float half = Ridge.HalfWidth(z);
                var edge = new Vector3(Slope.Centre(z) + side * (half - 0.5f), 0f, z);
                edge.y = Slope.Height(edge.x, edge.z);
                // leaning out over the edge, the way the rider sees it going over
                var eye = new Vector3(edge.x + side * 2.5f, Ridge.Top(z) + 2.2f, z - 3f);
                var look = new Vector3(edge.x + side * 11f, edge.y - 30f, z + 9f);
                cam.fieldOfView = 70f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                Bake(() => Shoot(cam, Path.Combine(outDir, $"RidgeShotDrop{tag}.png")));
            }
            // 4. the ridge from the gully below: the crest along the skyline, the cliffs and rock under it
            {
                float z = zFull + 30f;
                int side = Ridge.Sides(0) == 0 ? -1 : Ridge.Sides(0);
                float x = Slope.Centre(z) + side * (Ridge.Reach(z, side) + 25f);
                var eye = new Vector3(x, Slope.Height(x, z) + 3f, z - 25f);
                var at = new Vector3(Slope.Centre(z + 25f), Ridge.Top(z + 25f) - 10f, z + 25f);
                rider.ResetAt(x, z); Settle();
                cam.fieldOfView = 65f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                Shoot(cam, Path.Combine(outDir, $"RidgeShotCliffs{tag}.png"));
                // and from straight down the fall line, high above the crest, to see its shape
                var eye2 = new Vector3(Slope.Centre(zr + 20f), Ridge.Top(zr + 20f) + 26f, zr + 20f);
                var at2 = new Vector3(Slope.Centre(zFull + 60f), Ridge.Top(zFull + 60f) - 8f, zFull + 60f);
                rider.ResetAt(Slope.Centre(zFull), zFull); Settle();
                cam.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(at2 - eye2));
                Shoot(cam, Path.Combine(outDir, $"RidgeShotAbove{tag}.png"));
            }
            // 5. riding off the edge: the moment over, and the tumble down the rock
            {
                int side = Ridge.Sides(0) == 0 ? 1 : Ridge.Sides(0);
                Ride(zFull, 22f, 1.2f, t => Follow(0f));
                float tOff = 0f;
                for (int i = 0; i < 600 && !rider.Crashed; i++, tOff += Dt)
                {
                    rider.Steer = side * 0.8f;
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                    if (i % 4 == 0) body.Apply(4 * Dt);
                    if (i % 2 == 0) late.Invoke(chase, null);
                }
                Debug.Log($"RIDGESHOT went over after {tOff:0.00}s at off={rider.transform.position.x - Slope.Centre(rider.Z):0.0}, crashed={rider.Crashed}");
                for (int i = 0; i < 100; i++)
                {
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt);
                    if (i % 2 == 0) late.Invoke(chase, null);
                }
                Settle(); late.Invoke(chase, null);
                Bake(() => Shoot(cam, Path.Combine(outDir, $"RidgeShotFall{tag}.png")));
                Debug.Log($"RIDGESHOT fall: body at off={body.RagdollCentre.x - Slope.Centre(body.RagdollCentre.z):0.0}, {Ridge.Top(body.RagdollCentre.z) - body.RagdollCentre.y:0.0} m below the crest, cam={cam.transform.position}");
                for (int i = 0; i < 360; i++)
                {
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt);
                    if (i % 2 == 0) late.Invoke(chase, null);
                }
                Settle(); late.Invoke(chase, null);
                Bake(() => Shoot(cam, Path.Combine(outDir, $"RidgeShotFall2{tag}.png")));
                Debug.Log($"RIDGESHOT fall2: body at off={body.RagdollCentre.x - Slope.Centre(body.RagdollCentre.z):0.0}, {Ridge.Top(body.RagdollCentre.z) - body.RagdollCentre.y:0.0} m below the crest");
            }
        }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
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
        Debug.Log("RIDGESHOT wrote " + file);
    }
}
