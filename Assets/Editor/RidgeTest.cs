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
///   Shots: renders riding a ridge, looking down a drop, the cliffs from below and out over a canyon, a fall from
///          the moment it meets the rock to the body buried at the bottom, and the sign; then a jump cliff (flying
///          off it, and from the side) and a deadly cliff (its sign, the wall from below, going over, the burial).
///          Unity -batchmode -projectPath . -executeMethod RidgeTest.Shots -logFile shots.log
///          (no -nographics: it needs the GPU). Writes RidgeShot*.png to $RIDGE_SHOTS, or the project folder.
///   Fit:   that what is drawn fits what is ridden: rays down onto the built terrain chunks across the crest's
///          rounded edge (how far the rider would hover over or sink into the drawn snow, and any cracks), the
///          marker poles' feet, and that wherever a fall on a cliff ends, the rider gets up clear of every rock.
///          Unity -batchmode -nographics -projectPath . -executeMethod RidgeTest.Fit -logFile fit.log
/// </summary>
public static class RidgeTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static string lastWhy = "";

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
                float z0 = Course.First + cyc * Course.Cycle + Ridge.At, z1 = Course.First + cyc * Course.Cycle + Ridge.EndOf(cyc);
                float zm = z0 + Ridge.RampIn + 30f;
                // a cross-section through the full crest
                var sb = new System.Text.StringBuilder();
                for (float a = -40f; a <= 40.01f; a += 4f)
                {
                    float x = Slope.Centre(zm) + a;
                    sb.Append($"{a:0}:{Slope.Height(x, zm) - Ridge.Top(zm):0.0}/{Slope.Normal(x, zm).y:0.00} ");
                }
                Debug.Log($"RIDGE seed{seed} cycle {cyc} form={Ridge.FormOf(cyc)} z={z0:0}-{z1:0} section at z={zm:0} (offset:height/normal.y): {sb}");
                // along the crest: the flattest it gets underfoot, and the tightest roll-over (the speed it would launch you at)
                float minNy = 1f, worstCurv = 0f, worstAt = 0f, maxWander = 0f;
                for (float z = z0; z < z1; z += 1f)
                {
                    float w = Ridge.Weight(z);
                    if (w < 0.3f) continue;
                    float half = Ridge.HalfWidth(z);
                    // (inside the edge: past it is a ridge's shoulder or a valley wall's foot, both meant to tilt)
                    for (float a = -0.85f * half; a <= 0.85f * half + 0.01f; a += 0.85f * half / 4f) minNy = Mathf.Min(minNy, Slope.Normal(Slope.Centre(z) + a, z).y);
                    // along the line you ride (the centre line), over 3 m: the surface curving away below a ballistic arc throws you
                    const float h = 3f;
                    float y0 = Slope.Height(Slope.Centre(z - h), z - h), y1 = Slope.Height(Slope.Centre(z), z), y2 = Slope.Height(Slope.Centre(z + h), z + h);
                    float curv = (y0 - 2f * y1 + y2) / (h * h);
                    if (curv < worstCurv) { worstCurv = curv; worstAt = z; }
                    maxWander = Mathf.Max(maxWander, Mathf.Abs(Slope.Centre(z + 1f) - Slope.Centre(z)));
                }
                float launch = worstCurv < 0f ? Mathf.Sqrt(Rider.G / -worstCurv) : 999f;
                Debug.Log($"RIDGE seed{seed} cycle {cyc}: crest min normal.y={minNy:0.00}, tightest roll-over at z={worstAt:0} launches above {launch * 3.6f:0} km/h, crest wanders up to {Mathf.Atan(maxWander) * Mathf.Rad2Deg:0} deg off the fall line");
                // (a narrow crest must stay gentle underfoot; a gorge's wide floor may plunge in more steeply, so long as
                // the board stays on it: it takes off from anything steeper than normal.y 0.62)
                bool gorge = Ridge.SideOf(cyc, -1f) == Ridge.Side.Wall || Ridge.SideOf(cyc, 1f) == Ridge.Side.Wall;
                if (minNy < (gorge ? 0.64f : 0.7f)) Bad($"seed{seed} cycle {cyc}: crest too steep underfoot (normal.y {minNy:0.00})");
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
        rider.Crash += why => lastWhy = why;

        // a mountain whose first stretch is each form ($RIDGE_FORMS picks some, by name; all of them otherwise)
        var forms = new System.Collections.Generic.List<Ridge.Form>();
        string want = System.Environment.GetEnvironmentVariable("RIDGE_FORMS");
        foreach (Ridge.Form f in System.Enum.GetValues(typeof(Ridge.Form)))
            if (f != Ridge.Form.Plain && (string.IsNullOrEmpty(want) || want.Contains(f.ToString()))) forms.Add(f);
        var seeds = new System.Collections.Generic.List<int>();
        foreach (var f in forms)
            for (int s = 1; s < 2000; s++) { Slope.Init(s); if (Ridge.Built(0) && Ridge.FormOf(0) == f) { seeds.Add(s); break; } }
        int both = seeds.Count > 0 && forms[0] == Ridge.Form.Ridge ? seeds[0] : -1;
        foreach (int seed in seeds)
        {
            Slope.Init(seed);
            var form = Ridge.FormOf(0);
            string tag = form == Ridge.Form.Ridge ? "" : form == Ridge.Form.RidgeOneSided ? "OneSide" : form.ToString();
            // the side the falls and views go to: a cliff if there is one, else a wall
            int mainSide = Ridge.SideOf(0, 1f) == Ridge.Side.Cliff ? 1 : Ridge.SideOf(0, -1f) == Ridge.Side.Cliff ? -1 : Ridge.SideOf(0, 1f) == Ridge.Side.Wall ? 1 : -1;
            bool wall = Ridge.SideOf(0, mainSide) == Ridge.Side.Wall;
            terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
            float zr = Course.First + Ridge.At;
            float zFull = zr + Ridge.RampIn + 10f;
            if (Ridge.Cut(zr + 250f) > 0f) zFull = zr + 210f;   // a gorge is deepest further in

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
            // a crash played out to the end: the body still, or dead and settled in its hole
            void PlayOut()
            {
                float rest = 0f;
                for (int i = 0; i < 120 * 30 && rest < 2f; i++)
                {
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt);
                    if (i % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                    if (i % 2 == 0) late.Invoke(chase, null);
                    if (rider.Crashed && !body.RagdollMoving) rest += Dt; else rest = 0f;
                }
                Debug.Log($"RIDGESHOT played out: dead={body.Dead}, {body.RagdollBelowSnow:0.00} m under the snow");
            }
            // close on what is left: a body-shaped hole, the snow thrown up round it
            void Buried(string file)
            {
                Settle();
                Vector3 c = body.RagdollCentre;
                // from the open side, away from the wall it came down
                float away = Mathf.Sign(c.x - Slope.Centre(c.z) + 0.01f);
                var eye = c + new Vector3(away * 5f, 4.5f, -5f);
                eye.y = Mathf.Max(eye.y, Slope.Height(eye.x, eye.z) + 2.5f);
                cam.fieldOfView = 55f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c - eye));
                Bake(() => Shoot(cam, Path.Combine(outDir, file)));
            }
            float Follow(float dx)
            {
                Vector3 p = rider.transform.position;
                float look = Mathf.Max(7f, rider.Speed * 0.5f);
                float want = Mathf.Atan2(Slope.Centre(rider.Z + look) + dx - p.x, look);
                return Mathf.Clamp((want - rider.Heading) * 4f - rider.YawRate * 0.25f, -1f, 1f);
            }

            Debug.Log($"RIDGESHOT seed {seed} form={form} left={Ridge.SideOf(0, -1f)} right={Ridge.SideOf(0, 1f)} z={zr:0}-{Course.First + Ridge.EndOf(0):0} depth={Ridge.DepthHere(zFull + 40f, mainSide):0}");
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
                int side = mainSide;
                Ride(z - 20f, 12f, 1.2f, t => Follow(0f));
                Settle();
                float half = Ridge.HalfWidth(z);
                var edge = new Vector3(Slope.Centre(z) + side * (half - 0.5f), 0f, z);
                edge.y = Slope.Height(edge.x, edge.z);
                // leaning out over the edge, the way the rider sees it going over
                var eye = new Vector3(edge.x + side * 2.5f, Ridge.Top(z) + 2.2f, z - 3f);
                var look = new Vector3(edge.x + side * 11f, edge.y + (wall ? 45f : -30f), z + 9f);   // down the drop, or up the wall
                cam.fieldOfView = 70f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                Bake(() => Shoot(cam, Path.Combine(outDir, $"RidgeShotDrop{tag}.png")));
            }
            // 4. the ridge from the gully below: the crest along the skyline, the cliffs and rock under it
            {
                float z = zFull + 30f;
                int side = mainSide;
                // from the canyon floor up at the crest; in a valley, from its floor up at the wall's top
                float x = wall ? Slope.Centre(z) - side * (Ridge.HalfWidth(z) - 2f) : Slope.Centre(z) + side * (Ridge.Reach(z, side) + 25f);
                var eye = new Vector3(x, Slope.Height(x, z) + (wall ? 1.8f : 3f), z - 25f);
                var at = wall ? new Vector3(Slope.Centre(z + 25f) + side * Ridge.Reach(z + 25f, side), Ridge.Top(z + 25f) - Ridge.Cut(z + 25f) * 0.15f, z + 25f)
                              : new Vector3(Slope.Centre(z + 25f), Ridge.Top(z + 25f) - 10f, z + 25f);
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
                int side = mainSide;
                Ride(zFull, 22f, 1.2f, t => Follow(0f));
                float tOff = 0f;
                for (int i = 0; i < 600 && !rider.Crashed; i++, tOff += Dt)
                {
                    rider.Steer = side * 0.8f;
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                    if (i % 4 == 0) body.Apply(4 * Dt);
                    if (i % 2 == 0) late.Invoke(chase, null);
                }
                Debug.Log($"RIDGESHOT went over after {tOff:0.00}s at off={rider.transform.position.x - Slope.Centre(rider.Z):0.0}, crashed={rider.Crashed} ({lastWhy})");
                // the moment it meets the rock: still on the board until then
                {
                    for (int i = 0; i < 3; i++) { rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); }
                    Settle();
                    Vector3 rp = body.RagdollCentre;
                    var eye = rp + new Vector3(-side * 7f, 5f, -9f);
                    cam.fieldOfView = 60f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(rp + Vector3.down * 3f - eye));
                    Bake(() => Shoot(cam, Path.Combine(outDir, $"RidgeShotRockHit{tag}.png")));
                }
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
                // all the way down, and what is left at the bottom
                PlayOut(); 
                Buried($"RidgeShotBuried{tag}.png");
            }
            // 6. the whole thing from out over a canyon: the crest, the walls, the floor far below
            {
                float z = zFull + 50f;
                // out over a canyon, or from up on a valley wall's rim, looking along the run
                int side = Ridge.SideOf(0, -mainSide) == Ridge.Side.Plain ? mainSide : -mainSide;
                float x = Slope.Centre(z) + side * 120f;
                rider.ResetAt(Slope.Centre(z), z); Settle();
                var eye = new Vector3(x, Mathf.Max(Ridge.Top(z) + 35f, Slope.Height(x, z - 70f) + 25f), z - 70f);
                var at = new Vector3(Slope.Centre(z + 60f), Ridge.Top(z + 60f) - (Ridge.SideOf(0, side) == Ridge.Side.Wall || Ridge.SideOf(0, side) == Ridge.Side.Bank ? Ridge.Cut(z + 60f) : 70f), z + 60f);
                cam.fieldOfView = 68f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                Shoot(cam, Path.Combine(outDir, $"RidgeShotCanyon{tag}.png"));
                // and high over it all, off to one side: how the stretch sits in the mountain round it
                float zo = zr - 40f, xo = Slope.Centre(zo) + side * 150f;
                var eyeO = new Vector3(xo, Ridge.Top(zo) + 150f, zo);
                var atO = new Vector3(Slope.Centre(zFull + 120f), Ridge.Top(zFull + 120f) - 40f, zFull + 120f);
                rider.ResetAt(Slope.Centre(zFull + 60f), zFull + 60f);
                for (int k = 0; k < 4; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
                Settle();
                cam.fieldOfView = 70f;
                cam.transform.SetPositionAndRotation(eyeO, Quaternion.LookRotation(atO - eyeO));
                Shoot(cam, Path.Combine(outDir, $"RidgeShotOverview{tag}.png"));
            }
            if (seed != both) continue;
            // 7. a jump cliff: the lip and its rock band, and flying off it
            if (Cliffs.JumpOf(0, 1, out var jc) || Cliffs.JumpOf(1, 0, out jc))
            {
                rider.ResetAt(Slope.Centre(jc.Start - 30f), jc.Start - 30f); body.ResetVisual(); rider.Begin();
                rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(rider.X, rider.Z)).normalized * 22f;
                terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
                for (int i = 0; i < 1200 && !(rider.Airborne && rider.AirTime > 0.45f && rider.Z > jc.z); i++)
                {
                    rider.Steer = Follow(0f);
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                    if (i % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                    if (i % 4 == 0) body.Apply(4 * Dt);
                    if (i % 2 == 0) late.Invoke(chase, null);
                }
                Debug.Log($"RIDGESHOT jump cliff drop {jc.drop:0.0} m at z={jc.z:0}: rider at z={rider.Z:0} air={rider.Airborne} {rider.AirTime:0.00}s {rider.AboveGround:0.0} m up, {rider.Speed * 3.6f:0} km/h");
                Settle(); late.Invoke(chase, null);
                Bake(() => Shoot(cam, Path.Combine(outDir, "CliffShotJump.png")));
                Vector3 rp = rider.transform.position;
                // from off to the side and a little behind the lip, above the bench: the lip, its rock band, the flight
                float ex = Slope.Centre(jc.z) + 34f, ez = jc.z - 8f;
                var eye = new Vector3(ex, Mathf.Max(Slope.Height(ex, ez), Slope.Height(Slope.Centre(jc.z), jc.z - 1f)) + 7f, ez);
                cam.fieldOfView = 62f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(rp + Vector3.down * 4f - eye));
                Bake(() => Shoot(cam, Path.Combine(outDir, "CliffShotJumpSide.png")));
            }
            // 8. a deadly cliff: its sign, the wall from its foot, going over it, and what is left
            if (Cliffs.WallOf(0, out var wl) || Cliffs.WallOf(1, out wl))
            {
                float zs = wl.z0 - 28f;
                rider.ResetAt(Slope.Centre(zs - 40f), zs - 40f); body.ResetVisual(); Settle();
                var eye = new Vector3(Slope.Centre(zs - 16f) - wl.side * 4f, Slope.Height(Slope.Centre(zs - 16f), zs - 16f) + 2f, zs - 16f);
                var at = new Vector3(Slope.Centre(zs) + wl.side * 16f, Slope.Height(Slope.Centre(zs), zs) + 2.5f, zs + 6f);
                cam.fieldOfView = 62f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                Shoot(cam, Path.Combine(outDir, "CliffShotWallSign.png"));
                float zb = wl.z1 + 30f, xb = Slope.Centre(zb) + wl.side * (wl.edge + wl.height * 0.55f + 28f);
                rider.ResetAt(xb, zb); Settle();
                eye = new Vector3(xb, Slope.Height(xb, zb) + 2f, zb + 10f);
                at = new Vector3(Slope.Centre(zb - 20f) + wl.side * wl.edge, Slope.Height(Slope.Centre(zb - 20f) + wl.side * (wl.edge - 2f), zb - 20f), zb - 20f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                Shoot(cam, Path.Combine(outDir, "CliffShotWallBelow.png"));
                // over the edge
                float zo = wl.z1 + 10f;
                rider.ResetAt(Slope.Centre(zo), zo); body.ResetVisual(); rider.Begin();
                rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(rider.X, rider.Z)).normalized * 20f;
                terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
                bool shot = false;
                for (int i = 0; i < 1200 && !rider.Crashed; i++)
                {
                    rider.Steer = Mathf.Clamp(wl.side * 0.9f - rider.Heading * 0.3f, -1f, 1f);
                    rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
                    if (i % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
                    if (i % 4 == 0) body.Apply(4 * Dt);
                    if (i % 2 == 0) late.Invoke(chase, null);
                    if (!shot && rider.Airborne && rider.AboveGround > 8f)
                    {
                        shot = true;
                        Settle(); late.Invoke(chase, null);
                        Bake(() => Shoot(cam, Path.Combine(outDir, "CliffShotWallOver.png")));
                    }
                }
                Debug.Log($"RIDGESHOT wall {wl.height:0} m: over and down ({lastWhy}) at z={rider.Z:0}");
                PlayOut();
                Buried("CliffShotWallBuried.png");
            }
        }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// The rock, near and far, by day and by night: a gorge wall, a ridge's cliff, and the spires along a cliff's edge.
    /// Unity -batchmode -projectPath . -executeMethod RidgeTest.RockShots (writes Rock*.png to $RIDGE_SHOTS)
    /// </summary>
    public static void RockShots()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        var rockSh = Resources.Load<Shader>("RockLit");
        if (rockSh != null)
        {
            Debug.Log($"ROCKSHOT RockLit supported {rockSh.isSupported} errors {ShaderUtil.ShaderHasError(rockSh)}");
            foreach (var m in ShaderUtil.GetShaderMessages(rockSh)) Debug.Log($"ROCKSHOT RockLit: {m.severity} {m.message} line {m.line}");
        }
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        Debug.Log($"ROCKSHOT rock material shader: {RockFace.Material(Shader.Find("Universal Render Pipeline/Lit")).shader.name}");
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = Get<Rider>("rider"); var body = Get<RiderBody>("body");
        var terrain = Get<SlopeStreamer>("terrain"); var props = Get<Forest>("props"); var dayNight = Get<DayNight>("dayNight");
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var draw = typeof(Forest).GetMethod("Draw", NP);
        rider.input = null;
        var cam = Camera.main;
        cam.GetComponent<ChaseCamera>().enabled = false;
        string outDir = System.Environment.GetEnvironmentVariable("RIDGE_SHOTS");
        if (string.IsNullOrEmpty(outDir)) outDir = ".";
        int SeedOf(Ridge.Form f) { for (int s = 1; s < 2000; s++) { Slope.Init(s); if (Ridge.Built(0) && Ridge.FormOf(0) == f) return s; } return 1; }
        // a view: the rider stands by the camera (so the headlamp lights what it looks at, at night) and the world is built round it
        void View(string file, Vector3 eye, Vector3 at, float fov, bool night)
        {
            TimeOfDay.TestClock = night ? 3.4f : 1.4f; dayNight.ApplyNow();
            Vector3 flat = at - eye; flat.y = 0f;
            // (just behind the camera: the headlamp lights the view, and the rider is never in it)
            float rz = eye.z - flat.normalized.z * 2.5f, rx = eye.x - flat.normalized.x * 2.5f;
            rider.ResetAt(rx, rz); body.ResetVisual();
            rider.Heading = Mathf.Clamp(Mathf.Atan2(flat.x, flat.z), -1.4f, 1.4f);
            for (int k = 0; k < 5; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
            body.Apply(Dt);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            draw.Invoke(props, null);
            dayNight.ApplyNow();
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            Shoot(cam, Path.Combine(outDir, file));
        }
        foreach (bool night in new[] { false, true })
        {
            string when = night ? "Night" : "Day";
            // a gorge: along its wall from the floor, and from far off and high, how it sits in the mountain
            Slope.Init(SeedOf(Ridge.Form.Valley));
            terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
            float zg = Course.First + Ridge.At + 200f;
            float half = Ridge.HalfWidth(zg);
            View($"RockGorgeNear{when}.png", new Vector3(Slope.Centre(zg) - half * 0.4f, Slope.Height(Slope.Centre(zg), zg) + 1.8f, zg),
                 new Vector3(Slope.Centre(zg + 30f) + half + 6f, Slope.Height(Slope.Centre(zg + 30f), zg + 30f) + 14f, zg + 30f), 62f, night);
            View($"RockGorgeFar{when}.png", new Vector3(Slope.Centre(zg - 260f) + 160f, Ridge.Top(zg - 260f) + 120f, zg - 260f),
                 new Vector3(Slope.Centre(zg + 40f), Ridge.Top(zg + 40f) - 30f, zg + 40f), 55f, night);
            // a ridge: its cliff from the canyon floor, from far off across the canyon, and the spires along its edge
            Slope.Init(SeedOf(Ridge.Form.Ridge));
            terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
            float zr = Course.First + Ridge.At + 150f;
            float x0 = Slope.Centre(zr) + Ridge.Reach(zr, 1f) + 14f;
            View($"RockCliffNear{when}.png", new Vector3(x0, Slope.Height(x0, zr) + 12f, zr - 30f),
                 new Vector3(Slope.Centre(zr + 20f) + Ridge.HalfWidth(zr) + 10f, Ridge.Top(zr + 20f) - 60f, zr + 20f), 66f, night);
            View($"RockCliffFar{when}.png", new Vector3(Slope.Centre(zr) + 230f, Ridge.Top(zr) + 20f, zr - 120f),
                 new Vector3(Slope.Centre(zr + 60f), Ridge.Top(zr + 60f) - 70f, zr + 60f), 55f, night);
            View($"RockSpires{when}.png", new Vector3(Slope.Centre(zr) + 1.5f, Ridge.Top(zr) + 2.2f, zr - 12f),
                 new Vector3(Slope.Centre(zr + 30f) + 12f, Ridge.Top(zr + 30f) - 25f, zr + 30f), 70f, night);
        }
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Where the snow meets the rock, and the shapes of the big faces, by day and by night: the summit's shoulders
    /// seen from below them (left, behind, and beside the start, where the first lift tops out), the camp's cliff
    /// from the valley under it, a deadly cliff from its foot, and the whole top of the mountain from down the run.
    /// Unity -batchmode -projectPath . -executeMethod RidgeTest.SnowLineShots (writes SnowLine*.png to $RIDGE_SHOTS)
    /// </summary>
    public static void SnowLineShots()
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
        string outDir = System.Environment.GetEnvironmentVariable("RIDGE_SHOTS");
        if (string.IsNullOrEmpty(outDir)) outDir = ".";
        void View(string file, Vector3 eye, Vector3 at, float fov, bool night)
        {
            TimeOfDay.TestClock = night ? 3.4f : 1.4f; dayNight.ApplyNow();
            Vector3 flat = at - eye; flat.y = 0f;
            float rz = eye.z - flat.normalized.z * 2.5f, rx = eye.x - flat.normalized.x * 2.5f;
            rider.ResetAt(rx, rz); body.ResetVisual();
            rider.Heading = Mathf.Clamp(Mathf.Atan2(flat.x, flat.z), -1.4f, 1.4f);
            for (int k = 0; k < 5; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
            body.Apply(Dt);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
            draw.Invoke(props, null);
            dayNight.ApplyNow();
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            Shoot(cam, Path.Combine(outDir, file));
        }
        int seed = 1;
        for (int sd = 1; sd < 400; sd++) { Slope.Init(sd); if (Cliffs.WallOf(0, out _)) { seed = sd; break; } }
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
        float cx = Slope.Centre(Slope.PadLip);
        float top = Slope.Height(cx, -100f);
        Vector3 G(float a, float z, float up) { float x = cx + a; return new Vector3(x, Slope.Height(x, z) + up, z); }
        Cliffs.WallOf(0, out var w);
        float zw = w.z1 + 18f, hw = Cliffs.WallHeight(w, zw);
        float cw = Slope.Centre(zw);
        if (System.Environment.GetEnvironmentVariable("SNOWLINE_DEBUG") == "2")
        {
            // the left shoulder's snow bands: as drawn, without the strata's shelf snow, without the strata at all,
            // without the rock's own up-facing snow, and without the snow line's push
            var trm = RockFace.TerrainMaterial(Shader.Find("Universal Render Pipeline/Lit"));
            Vector3 eye = new Vector3(cx - 190f, top - 20f, -40f), at = new Vector3(cx - 45f, top - 30f, -110f);
            Vector4 st = trm.GetVector("_Strata");
            View("DbgL0Base.png", eye, at, 60f, false);
            trm.SetVector("_Strata", new Vector4(st.x, st.y, 1f, 0f)); View("DbgL1NoShelf.png", eye, at, 60f, false);
            trm.SetVector("_Strata", new Vector4(st.x, 0f, 1f, 0f)); View("DbgL2NoStrata.png", eye, at, 60f, false);
            trm.SetVector("_Snow", new Vector4(5f, 6f, 0f, 0f)); View("DbgL3NoUpSnow.png", eye, at, 60f, false);
            trm.SetVector("_Line", new Vector4(0f, 0.16f, 0f, 0f)); View("DbgL4NoPush.png", eye, at, 60f, false);
            EditorApplication.Exit(0);
            return;
        }
        if (System.Environment.GetEnvironmentVariable("SNOWLINE_DEBUG") == "1")
        {
            // what the far rock's white flecks are: the camp cliff from 330 m and from 110 m, as drawn, then with the
            // rock's snow turned off piece by piece, then (from 110 m) with both terrain materials flat colours: holes
            // show the sky, and blue is snow no rock covers
            var trm = RockFace.TerrainMaterial(Shader.Find("Universal Render Pipeline/Lit"));
            Vector3 farEye = new Vector3(cx + 330f, top - 140f, -260f), at = new Vector3(cx + 40f, top - 150f, -130f);
            Vector3 nearEye = at + (farEye - at).normalized * 110f;
            void Both(string tag) { View($"Dbg{tag}Far.png", farEye, at, 60f, false); View($"Dbg{tag}Near.png", nearEye, at, 60f, false); }
            Both("0Base");
            Vector4 sn = trm.GetVector("_Snow"), ln = trm.GetVector("_Line");
            trm.SetVector("_Snow", new Vector4(5f, 6f, 0f, 0f)); Both("1NoUpSnow");
            trm.SetVector("_Line", new Vector4(0f, ln.y, 0f, 0f)); Both("2NoLinePush");
            trm.SetVector("_Snow", sn); trm.SetVector("_Line", ln);
            // (both terrain materials as flat colours, each chunk given its own copies: the snow's is a project asset)
            var flatSnow = new Material(Shader.Find("Unlit/Color")) { color = Color.blue };
            var flatRock = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
            foreach (var mr in terrain.GetComponentsInChildren<MeshRenderer>()) mr.sharedMaterials = new[] { flatSnow, flatRock };
            View("Dbg3FlatNear.png", nearEye, at, 60f, false);
            EditorApplication.Exit(0);
            return;
        }
        foreach (bool night in new[] { false, true })
        {
            string when = night ? "Night" : "Day";
            // (the shoulders' views are from the air: the slopes under them are forest)
            View($"SnowLineLeft{when}.png", new Vector3(cx - 190f, top - 20f, -40f), new Vector3(cx - 45f, top - 30f, -110f), 60f, night);
            View($"SnowLineStart{when}.png", G(-95f, -10f, 1.7f), new Vector3(cx - 35f, top - 4f, -45f), 62f, night);
            View($"SnowLineBack{when}.png", new Vector3(cx - 120f, top + 10f, -300f), new Vector3(cx, top - 50f, -170f), 60f, night);
            View($"SnowLineCampCliff{when}.png", new Vector3(cx + 330f, top - 140f, -260f), new Vector3(cx + 40f, top - 150f, -130f), 60f, night);
            float fx = cw + w.side * (w.edge + hw * 0.7f + 22f);
            View($"SnowLineWallFoot{when}.png", new Vector3(fx, Slope.Height(fx, zw - 30f) + 2.5f, zw - 30f),
                 new Vector3(Slope.Centre(zw + 25f) + w.side * (w.edge + hw * 0.3f), Slope.Height(Slope.Centre(zw + 25f), zw + 25f) - hw * 0.45f, zw + 25f), 66f, night);
            View($"SnowLineFar{when}.png", new Vector3(cx + 420f, top + 60f, 160f), new Vector3(cx, top - 60f, -100f), 45f, night);
            // from the piste part way down, across at a valley's wall (or a ridge's cliff, if the mountain has no wall)
            float mz = -1f, ms = 1f, md = 0f;
            foreach (var kind in new[] { Ridge.Side.Wall, Ridge.Side.Cliff })
            {
                for (float z = 300f; z < 4000f && mz < 0f; z += 4f)
                    foreach (float sd in new[] { -1f, 1f })
                        if (Ridge.SideAt(z, sd) == kind && Ridge.DepthHere(z, sd) > 40f) { mz = z + 60f; ms = sd; md = Ridge.DepthHere(z + 60f, sd); break; }
                if (mz > 0f) break;
            }
            if (mz > 0f)
            {
                float mc = Slope.Centre(mz), ac = Slope.Centre(mz + 20f), hwM = Ridge.HalfWidth(mz + 20f);
                float floorY = Slope.Height(mc - ms * 2f, mz - 20f);
                View($"SnowLineMidRun{when}.png", new Vector3(mc - ms * 2f, floorY + 1.7f, mz - 20f),
                     new Vector3(ac + ms * (hwM + 30f), floorY + md * 0.35f, mz + 20f), 64f, night);
                if (!night) Debug.Log($"SNOWLINE mid-run view at z {mz} side {ms} depth {md} ({Ridge.SideAt(mz, ms)})");
            }
        }
        var tm = RockFace.TerrainMaterial(Shader.Find("Universal Render Pipeline/Lit"));
        Debug.Log($"SNOWLINE seed {seed} wall z {zw} height {hw} side {w.side}; terrain rock {tm.shader.name} keyword {tm.IsKeywordEnabled("SUMMIT_ROCK_TERRAIN")} queue {tm.renderQueue}");
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    /// <summary>The summit's left shoulder, down its fall line at a few places along it: where the ground holds snow
    /// (s), is part bare (/) or rock (R), every 2 m out from the shoulder's edge (seed 1).</summary>
    public static void Shoulder()
    {
        Slope.Init(1);
        float cx = Slope.Centre(Slope.PadLip);
        for (float z = -20f; z >= -150f; z -= 10f)
        {
            var sb = new System.Text.StringBuilder();
            for (float e = 0f; e <= 160f; e += 2f)
            {
                float x = cx + Slope.SummitLeft - e;
                float ny = Slope.Normal(x, z).y;
                float r = RockFace.Rockiness(ny, x, z);
                sb.Append(r < 0.3f ? 's' : r < 0.7f ? '/' : 'R');
            }
            Debug.Log($"SHOULDER z{z,5:0} {sb}");
        }
        // and the back, down from its edge, at a few places across it
        for (float a = -80f; a <= 40f; a += 15f)
        {
            var sb = new System.Text.StringBuilder();
            for (float e = 0f; e <= 160f; e += 2f)
            {
                float x = cx + a, z = Slope.SummitBack - e;
                float r = RockFace.Rockiness(Slope.Normal(x, z).y, x, z);
                sb.Append(r < 0.3f ? 's' : r < 0.7f ? '/' : 'R');
            }
            Debug.Log($"BACK a{a,5:0} {sb}");
        }
        EditorApplication.Exit(0);
    }

    /// <summary>The ground along the piste's centre line from $LINE_Z0 to $LINE_Z1 on mountain $LINE_SEED, term by term.</summary>
    public static void Line()
    {
        int seed = int.Parse(System.Environment.GetEnvironmentVariable("LINE_SEED") ?? "1154");
        float z0 = float.Parse(System.Environment.GetEnvironmentVariable("LINE_Z0") ?? "1000"), z1 = float.Parse(System.Environment.GetEnvironmentVariable("LINE_Z1") ?? "1100");
        Slope.Init(seed);
        if (System.Environment.GetEnvironmentVariable("LINE_ACROSS") == "1")
        {
            // across the run instead: every few metres down, height and slope out to 30 m either side
            for (float z = z0; z <= z1; z += 10f)
            {
                var sb = new System.Text.StringBuilder();
                for (float a = -2f; a <= 30f; a += 2f)
                {
                    float x = Slope.Centre(z) + a;
                    var n = Slope.Normal(x, z);
                    sb.Append($"{a:0}:{Slope.Height(x, z) - Ridge.Top(z):0.0}/{n.y:0.00}{(RockFace.IsRock(n, x, z) ? "R" : "")} ");
                }
                Debug.Log($"LINE z={z:0} form={Ridge.FormOf(Course.CycleOf(z))} w={Ridge.Weight(z):0.00} half={Ridge.HalfWidth(z):0.0} right={Ridge.SideAt(z, 1f)} wall={Cliffs.WallCut(Slope.Centre(z) + 12f, z):0.0} {sb}");
            }
            EditorApplication.Exit(0);
            return;
        }
        for (float z = z0; z <= z1; z += 1f)
        {
            float x = Slope.Centre(z);
            Debug.Log($"LINE z={z:0} h-line={Slope.Height(x, z) - Slope.FallLine(z):0.00} jump={Cliffs.JumpHeight(x, z):0.00} kick={Slope.Kicker(x, z):0.00} drops={Slope.Drops(z):0.00} canyon={Ridge.Depth(z):0.00} wall={Cliffs.WallCut(x, z):0.00} sec={Course.At(z)} n.y={Slope.Normal(x, z).y:0.00}");
        }
        EditorApplication.Exit(0);
    }

    public static void Fit()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        rider.input = null;
        const int RockLayer = 31, GroundLayer = 30;
        int problems = 0;
        void Bad(string m) { problems++; if (problems < 40) Debug.Log("RIDGEFIT BAD " + m); }

        // mountains with both kinds of ridge
        var seeds = new System.Collections.Generic.List<int>();
        int both = 0, one = 0;
        for (int s = 1; s < 400 && seeds.Count < 4; s++)
        {
            Slope.Init(s);
            if (!Ridge.Built(0)) continue;
            // two with a ridge's cliffs, two with a valley's walls
            bool cliffs = Ridge.SideOf(0, -1f) == Ridge.Side.Cliff || Ridge.SideOf(0, 1f) == Ridge.Side.Cliff;
            if (cliffs ? both++ < 2 : one++ < 2) seeds.Add(s);
        }
        foreach (int seed in seeds)
        {
            Slope.Init(seed);
            terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
            float zr = Course.First + Ridge.At, ze = Course.First + Ridge.EndOf(0);
            rider.ResetAt(Slope.Centre(zr - 20f), zr - 20f);
            for (int k = 0; k < 6; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
            foreach (var mf in terrain.GetComponentsInChildren<MeshFilter>())
            {
                var mc = mf.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = null; mc.sharedMesh = mf.sharedMesh;
                mf.gameObject.layer = GroundLayer;   // so the rays below see only the terrain
            }
            var rocks = new System.Collections.Generic.List<GameObject>();
            foreach (var t in dress.GetComponentsInChildren<Transform>())
                if (t.name == "RidgeRock") rocks.Add(t.gameObject);
            foreach (var g in rocks)
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
                {
                    mf.gameObject.layer = RockLayer;
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
            Physics.SyncTransforms();
            bool Ground(float x, float z, out float y)
            {
                y = 0f;
                if (!Physics.Raycast(new Vector3(x, Slope.Height(x, z) + 60f, z), Vector3.down, out var hit, 200f, 1 << GroundLayer)) return false;
                y = hit.point.y; return true;
            }
            // the coarse facet the mesh used to draw everywhere, for comparison
            float Coarse(float x, float z)
            {
                float q = Slope.Quad, gx = x / q, gz = z / q;
                int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
                float u = gx - i, v = gz - j, x0 = i * q, z0 = j * q;
                float hb = Slope.Height(x0 + q, z0), hc = Slope.Height(x0, z0 + q);
                if (u + v <= 1f) { float ha = Slope.Height(x0, z0); return ha + (hb - ha) * u + (hc - ha) * v; }
                float hd = Slope.Height(x0 + q, z0 + q);
                return hd + (hc - hd) * (1f - u) + (hb - hd) * (1f - v);
            }

            // 1. the crest's rounded edge: the rider (on DrawnHeight) against the drawn mesh
            float hover = 0f, sink = 0f, was = 0f, hoverAt = 0f, off = 0f, cracks = 0, samples = 0;
            for (float z = zr + 1f; z < ze - 1f; z += 0.37f)
            {
                float w = Ridge.Weight(z);
                if (w < 0.05f) continue;
                float half = Ridge.HalfWidth(z);
                for (int sd = -1; sd <= 1; sd += 2)
                    for (float d = half - 1.5f; d < half + Ridge.Shoulder + 2.5f; d += 0.11f)
                    {
                        float x = Slope.Centre(z) + sd * d;
                        samples++;
                        if (!Ground(x, z, out float my)) { cracks++; if (cracks < 5) Bad($"seed{seed} no terrain under x={x:0.00} z={z:0.00}"); continue; }
                        float gap = Slope.DrawnHeight(x, z) - my;
                        if (gap > hover) { hover = gap; hoverAt = z; off = sd * d; }
                        sink = Mathf.Min(sink, gap);
                        was = Mathf.Max(was, Slope.Height(x, z) - Coarse(x, z));
                    }
            }
            Debug.Log($"RIDGEFIT seed{seed} form={Ridge.FormOf(0)} edge: rider over drawn snow at most {hover * 100f:0.0} cm (z={hoverAt:0} off={off:0.0}), into it at most {-sink * 100f:0.0} cm, " +
                      $"{cracks} cracks in {samples} rays (whole 1.5 m facets would have left a {was * 100f:0} cm gap)");
            if (hover > 0.005f) Bad($"seed{seed} rider hovers {hover * 100f:0.0} cm over the crest edge at z={hoverAt:0} off={off:0.0}");
            if (sink < -0.005f) Bad($"seed{seed} rider sinks {-sink * 100f:0.0} cm into the crest edge");
            // 2. everywhere else round the ridge, the rider stands on exactly the drawn mesh, and it has no holes
            float worst = 0f; int holes = 0, n2 = 0;
            for (float z = zr - 40f; z < ze + 40f; z += 0.53f)
                for (float a = -60f; a <= 60f; a += 0.71f)
                {
                    float x = Slope.Centre(z) + a + 0.013f * (z % 7f);
                    n2++;
                    // a crevasse's hole (the terrain leaves out any quad with a corner in it: its walls are their own mesh)
                    float q = Slope.Quad, cx0 = Mathf.Floor(x / q) * q, cz0 = Mathf.Floor(z / q) * q;
                    if (Mathf.Max(Mathf.Max(Course.CrevasseCut(cx0, cz0), Course.CrevasseCut(cx0 + q, cz0)),
                                  Mathf.Max(Course.CrevasseCut(cx0, cz0 + q), Course.CrevasseCut(cx0 + q, cz0 + q))) > 0.25f) continue;
                    if (!Ground(x, z, out float my)) { holes++; if (holes < 5) Bad($"seed{seed} hole in the terrain at x={x:0.00} z={z:0.00}"); continue; }
                    worst = Mathf.Max(worst, Mathf.Abs(Slope.DrawnHeight(x, z) - my));
                }
            Debug.Log($"RIDGEFIT seed{seed} around the ridge: {holes} holes in {n2} rays, the mesh is ridden to within {worst * 1000f:0.0} mm");
            if (worst > 0.005f) Bad($"seed{seed} rider and mesh disagree by {worst * 100f:0.0} cm round the ridge");
            // 3. the marker poles stand in the snow, not over it
            float poleHigh = -9f, poleLow = 9f; int poles = 0;
            foreach (var t in dress.GetComponentsInChildren<Transform>())
            {
                if (t.name != "RidgePole" || t.localScale.y < 0.5f) continue;
                if (!Ground(t.position.x, t.position.z, out float my)) continue;
                float foot = t.position.y - t.localScale.y - my;
                poleHigh = Mathf.Max(poleHigh, foot); poleLow = Mathf.Min(poleLow, foot); poles++;
            }
            Debug.Log($"RIDGEFIT seed{seed}: {poles} poles, feet from {poleLow * 100f:0.0} to {poleHigh * 100f:0.0} cm off the drawn snow");
            if (poles > 0 && (poleHigh > 0f || poleLow < -0.2f)) Bad($"seed{seed} pole feet {poleLow * 100f:0.0}..{poleHigh * 100f:0.0} cm off the snow");
            // 4. a fall that ends anywhere on or below a cliff gets up clear of the rocks
            int inRock = 0, ups = 0;
            for (float z = zr; z < ze; z += 1.3f)
            {
                if (Ridge.Weight(z) < 0.4f) continue;
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    if (!Ridge.CliffSide(z, sd)) continue;
                    float half = Ridge.HalfWidth(z), reach = Ridge.Reach(z, sd);
                    for (float d = half + 0.5f; d < reach + 14f; d += 0.9f)
                    {
                        float x = Ridge.SafeX(Slope.Centre(z) + sd * d, z);
                        var p = new Vector3(x, Slope.DrawnHeight(x, z), z);
                        ups++;
                        if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.4f, 1 << RockLayer))
                        {
                            inRock++;
                            if (inRock < 6) Bad($"seed{seed} a fall {d:0.0} m out at z={z:0} gets up inside a rock at off={x - Slope.Centre(z):0.0} (reach {reach:0.0})");
                        }
                    }
                }
            }
            Debug.Log($"RIDGEFIT seed{seed}: {rocks.Count} rocks; {inRock} of {ups} get-ups inside one");
            foreach (var g in rocks) foreach (var mc in g.GetComponentsInChildren<MeshCollider>()) Object.DestroyImmediate(mc);
        }
        Debug.Log($"RIDGEFIT done: {problems} problem(s)");
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
