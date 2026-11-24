using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The mountain's people, rendered through the game's own wiring (the phone's quality level):
///   lineup: a row of people off the same seeds, the full model and the simplified one, day and night;
///   lift:   riders on the chairs up close (from a seat on the chair behind), from the piste below, the far tiers;
///   camp:   camp life from a few places, day and night.
/// Run without -quit (one picture an editor frame: instanced draws stay queued for the frame in batchmode):
///   Unity -batchmode -projectPath . -executeMethod PeopleShot.Run -logFile x.log
///   pop:    lift riders from far off, the phone before liftpop and after (Pop);
///   chairs: the lifts' chairs and towers, the phone before liftchairs and after (ChairShots).
///   riders: the mountain's other riders (MountainRiders); campnpc: the camp's people near to far after the lifts
///   have run a while (PeopleShotAlive.cs).
///   SUMMIT_PEOPLE_SET  lineup|lift|camp|pop|chairs|riders|campnpc|all (all)   SUMMIT_PEOPLE_OUT  the folder (PeopleShots)
/// </summary>
public static partial class PeopleShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Camp camp; static RiderBody body; static Rider rider; static Camera cam;
    static SlopeStreamer terrain; static Forest props; static Atmosphere atmo; static DayNight dayNight; static MetaUi meta;
    static MethodInfo tUpd, fUpd, aLate;
    static string outDir;
    static readonly List<System.Action> steps = new List<System.Action>();
    static int at;
    const float dt = 1f / 60f;

    public static void Run()
    {
        outDir = System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_OUT") ?? "PeopleShots";
        string set = System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_SET") ?? "all";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        QualitySettings.SetQualityLevel(0, true);
        TestPrefs.Pin();
        game = Object.FindFirstObjectByType<SummitGame>();
        bool liftSet = set == "lift" || set == "pop" || set == "chairs" || set == "riders";
        TestBoot.Boot(game, liftSet);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        camp = Get<Camp>("camp"); body = Get<RiderBody>("body"); rider = Get<Rider>("rider"); meta = Get<MetaUi>("meta");
        terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props"); dayNight = Get<DayNight>("dayNight");
        cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        if (!liftSet) typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = dt;
        steps.Clear(); at = 0;
        if (set == "lineup" || set == "all") Lineup();
        if (set == "lift") Lifts();
        if (set == "camp") CampShots();
        if (set == "pop") Pop();
        if (set == "chairs") ChairShots();
        if (set == "riders") RiderShots();
        if (set == "campnpc") CampNpcShots();
        steps.Add(() =>
        {
            ChaseCamera.TestDt = 0f;
            TimeOfDay.TestClock = -1f;
            TestPrefs.Restore();
        });
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (at < steps.Count)
        {
            try { steps[at++](); }
            catch (System.Exception e) { Debug.LogError("PEOPLESHOT exception " + e); at = steps.Count; }
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    static void Log(string s) => Debug.Log("PEOPLESHOT " + s);

    // ---------------------------------------------------------------- the lineup

    static void Lineup()
    {
        var people = new List<Person>();
        float cx = Slope.Centre(Slope.PadLip);
        steps.Add(() =>
        {
            float t0 = Time.realtimeSinceStartup;
            PeopleKit.Load();
            Log($"kit loaded in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
            for (int i = 0; i < 12; i++)
            {
                var look = PersonLook.Random(1000 + i);
                float t1 = Time.realtimeSinceStartup;
                var p = Person.Make(look);
                Log($"person {i}: {(look.female ? "F" : "M")} h{look.height:0.00} b{look.build:0.00} s{look.shoulders:0.00} {look.head} {look.hairStyle} pack {look.pack != null} {look.gear} jacket {look.jacket.name} made in {(Time.realtimeSinceStartup - t1) * 1000f:0.0} ms");
                p.mode = Person.Mode.Stand;
                p.SetLod(2);
                people.Add(p);
            }
            camp.TeleportTo(new Vector3(cx + 30f, 0f, -90f), 0f);
        });
        // in pairs, a few metres off: the full model; then the whole crowd further off, full and simplified
        float gz = -62f;
        foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
        {
            for (int pair = 0; pair < 6; pair++)
            {
                int k = pair;
                foreach (bool back in new[] { false, true })
                {
                    if (back && k % 2 == 1) continue;
                    bool b = back;
                    steps.Add(() =>
                    {
                        TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                        foreach (var p in people) p.SetLod(2);
                        float g = Slope.DrawnHeight(cx, gz);
                        for (int j = 0; j < 2; j++)
                        {
                            var p = people[k * 2 + j];
                            p.SetLod(0);
                            float x = cx + (j == 0 ? -0.5f : 0.5f);
                            p.transform.SetPositionAndRotation(new Vector3(x, Slope.DrawnHeight(x, gz), gz), Quaternion.Euler(0f, (b ? 180f : 0f) + (j == 0 ? 12f : -12f), 0f));
                            p.Apply(0f); for (int f = 0; f < 20; f++) p.Apply(dt);
                        }
                        var eye = new Vector3(cx, g + 1.25f, gz + 3.6f);
                        Aim(eye, new Vector3(cx, g + 0.92f, gz), 38f);
                        Shoot($"pair{k}{(b ? "-back" : "")}-{when}.png");
                    });
                }
            }
            steps.Add(() =>
            {
                for (int i = 0; i < people.Count; i++)
                {
                    var p = people[i];
                    int row = i / 6, c = i % 6;
                    float x = cx - 3.75f + c * 1.5f, z = gz - row * 2.5f;
                    p.transform.SetPositionAndRotation(new Vector3(x, Slope.DrawnHeight(x, z), z), Quaternion.Euler(0f, 0f, 0f));
                    p.SetLod(row == 0 ? 0 : 1);
                    p.Apply(0f); for (int f = 0; f < 20; f++) p.Apply(dt);
                }
                float g = Slope.DrawnHeight(cx, gz);
                var eye = new Vector3(cx, g + 1.6f, gz + 9f);
                Aim(eye, new Vector3(cx, g + 0.9f, gz - 1.2f), 40f);
                Shoot($"crowd-{when}.png");
            });
        }
        steps.Add(() => { foreach (var p in people) Object.DestroyImmediate(p.gameObject); });
    }

    // ---------------------------------------------------------------- the lifts

    static SkiLift lift; static MethodInfo lUpd; static CourseDressing dress;

    static void Lifts()
    {
        lift = Object.FindFirstObjectByType<SkiLift>();
        dress = Object.FindFirstObjectByType<CourseDressing>();
        lUpd = typeof(SkiLift).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_SEED"), out int sd) ? sd : 4242;
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        // a first line, and a chair with three or four aboard half way up its third span from the bottom
        LiftLine.Lift F = null;
        foreach (var l in LiftLine.Lifts) if (!l.second && (F == null || l.k == 1)) F = l;
        int n = F.Spans;
        float d0 = F.cum[3] + (F.cum[4] - F.cum[3]) * 0.5f;
        double clock = 0; int chair = -1;
        for (int lap = 3; lap < 40 && chair < 0; lap++)
            for (int c = 0; c < F.chairs; c++)
            {
                float h = Slope.Hash01(c + F.id * 977, lap, 151);
                if (h < 0.72f) continue;
                clock = (d0 + lap * (double)F.loop - c * (double)F.spacing) / LiftLine.Speed;
                if (clock < 30.0) continue;
                chair = c; break;
            }
        F.Loop(d0, out Vector3 pos, out Vector3 travel, out bool up, out float since);
        var fwd = new Vector3(travel.x, 0f, travel.z).normalized; var right = Vector3.Cross(Vector3.up, fwd);
        Log($"lift {F.id} (cycle {F.k}), chair {chair} at {pos} going {(up ? "up" : "down")}, clock {clock:0.0}; {LiftLine.Lifts.Length} lifts");
        Vector3 seat = pos + Vector3.up * -2.5f;
        // the piste side: the way to the piste's middle from the line
        float px = Slope.Centre(pos.z);
        Vector3 toPiste = new Vector3(px - pos.x, 0f, 0f).normalized;
        void Ground(ref Vector3 v, float up2) { v.y = Slope.DrawnHeight(v.x, v.z) + up2; }
        var shots = new List<(string name, Vector3 rider, Vector3 eye, Vector3 look, float fov, double clock)>();
        // up close, from the front and a little to the side (as from a tower's catwalk), and from beside
        shots.Add(("close-front", pos + right * 25f, seat + fwd * 3.4f + right * 0.8f + Vector3.up * 0.25f, seat + Vector3.down * 0.15f, 52f, clock));
        shots.Add(("close-side", pos + right * 25f, seat + right * 3.6f + fwd * 1.6f + Vector3.up * 0.1f, seat + Vector3.down * 0.25f, 50f, clock));
        shots.Add(("close-back", pos + right * 25f, seat - fwd * 3.6f + right * 0.6f + Vector3.up * 0.6f, seat, 52f, clock));
        // from the piste under the line: the player stood looking up at them going over
        var under = pos + toPiste * 9f - fwd * 8f; Ground(ref under, 1.6f);
        shots.Add(("piste", under, under, seat + Vector3.down * 0.3f, 50f, clock));
        // further off, square to the line on its lower side: the chairs going up across the slope
        Vector3 sideA = pos + right * 38f - fwd * 6f, sideB = pos - right * 38f - fwd * 6f;
        Ground(ref sideA, 1.6f); Ground(ref sideB, 1.6f);
        var underFar = sideA.y < sideB.y ? sideA : sideB;
        shots.Add(("piste-far", underFar, underFar, seat + fwd * 6f, 50f, clock));
        // the player stood under the line as the chair comes over: they look down, one points
        var below = pos + fwd * 7f; Ground(ref below, 0f);
        var side = seat + right * 6.5f + fwd * 1.5f + Vector3.down * 1.2f;
        shots.Add(("pointing", below, side, seat + Vector3.down * 0.9f, 50f, clock));
        shots.Add(("pointing-up", below, below + Vector3.up * 1.6f, seat + Vector3.down * 0.2f, 55f, clock));
        foreach (var (when, tod) in new[] { ("day", 1.4f), ("night", 3.4f) })
            foreach (var sh in shots)
            {
                var shot = sh;
                steps.Add(() =>
                {
                    TimeOfDay.TestClock = tod; dayNight.ApplyNow();
                    rider.ResetAt(shot.rider.x, shot.rider.z); body.ResetVisual(); rider.Stop();
                    terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
                    SkiLift.TestClock = shot.clock;
                    var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
                    cam.fieldOfView = shot.fov;
                    cam.transform.SetPositionAndRotation(shot.eye, Quaternion.LookRotation(shot.look - shot.eye));
                    tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
                    typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
                    typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
                    for (int i = 0; i < 3; i++) lUpd.Invoke(lift, null);
                    lift.Stats(out int draws, out long tris, out int inst);
                    Shoot($"lift-{shot.name}-{when}.png");
                    Log($"lift-{shot.name}-{when}: aboard {LiftRiders.Aboard}, near {LiftRiders.NearShown}, mid {LiftRiders.MidShown}, far {LiftRiders.FarShown}; riders {LiftRiders.DrawCalls} draws {LiftRiders.Triangles} tris; lifts in all {draws} draws {tris} tris");
                });
            }
        // what the riders cost: the rider at every 30 m down the mountain, the camera behind as the chase camera has it
        steps.Add(() =>
        {
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
            int n = 0, near = 0, mid = 0, far = 0, nearMax = 0, aboardMax = 0, drawsMax = 0; long tris = 0, trisMax = 0; double ms = 0, msMax = 0;
            float zTop = float.MaxValue, zBot = float.MinValue;
            foreach (var l in LiftLine.Lifts) { zTop = Mathf.Min(zTop, l.ZMin); zBot = Mathf.Max(zBot, l.ZMax); }
            for (float z = Mathf.Max(0f, zTop); z < zBot; z += 30f)
            {
                float x = Slope.Centre(z);
                rider.ResetAt(x, z); body.ResetVisual(); rider.Stop();
                var at = new Vector3(x, Slope.DrawnHeight(x, z), z);
                cam.transform.SetPositionAndRotation(at + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.3f, 1f)));
                for (int k = 0; k < 6; k++)
                {
                    SkiLift.TestClock = 200.0 + z * 0.37 + k * 1.3;
                    typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    lUpd.Invoke(lift, null);
                    double t = sw.Elapsed.TotalMilliseconds;
                    n++; ms += t; msMax = System.Math.Max(msMax, t);
                    near += LiftRiders.NearShown; mid += LiftRiders.MidShown; far += LiftRiders.FarShown;
                    nearMax = Mathf.Max(nearMax, LiftRiders.NearShown); aboardMax = Mathf.Max(aboardMax, LiftRiders.Aboard);
                    tris += LiftRiders.Triangles; trisMax = System.Math.Max(trisMax, LiftRiders.Triangles); drawsMax = Mathf.Max(drawsMax, LiftRiders.DrawCalls);
                }
            }
            Log($"lift budget over {n} frames down the mountain: riders aboard in range at most {aboardMax}; drawn as people {near / (float)n:0.0} on average ({nearMax} at most), mid {mid / (float)n:0.0}, far {far / (float)n:0.0}; " +
                $"rider triangles {tris / n} on average, {trisMax} at most; rider draw calls at most {drawsMax}; SkiLift.Update with the riders {ms / n:0.00} ms on average, {msMax:0.00} at most (editor)");
        });
        steps.Add(() => { SkiLift.TestClock = -1.0; Physics.simulationMode = SimulationMode.FixedUpdate; });
    }

    // ---------------------------------------------------------------- lift riders from far off (liftpop)

    /// <summary>
    /// Riding down toward lifts, the chase camera's view: the riders on chairs ahead as main drew them (cut at 420 m), as
    /// the phone drew them (no instanced riders: the build had dropped the shader's instanced variants; reproduced here
    /// with LiftRiders.TestPhoneBefore, the editor keeps every variant), and as now. SUMMIT_PEOPLE_SET=pop.
    /// </summary>
    static void Pop()
    {
        PeopleAssets.Ensure();
        lift = Object.FindFirstObjectByType<SkiLift>();
        dress = Object.FindFirstObjectByType<CourseDressing>();
        lUpd = typeof(SkiLift).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_SEED"), out int sd) ? sd : 4242;
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        var views = LiftViews();
        foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
            foreach (var v in views)
                foreach (var mode in new[] { "phone-before", "main-before", "after" })
                {
                    var vw = v; var md = mode;
                    if (when == "night" && md == "main-before") continue;
                    steps.Add(() =>
                    {
                        TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                        LiftRiders.TestPhoneBefore = md == "phone-before"; LiftRiders.TestCutAt420 = md != "after";
                        float x = Slope.Centre(vw.riderZ);
                        rider.ResetAt(x, vw.riderZ); body.ResetVisual(); rider.Stop();
                        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
                        SkiLift.TestClock = 400.0;
                        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
                        cam.fieldOfView = vw.fov;
                        cam.transform.SetPositionAndRotation(vw.eye, Quaternion.LookRotation(vw.at - vw.eye));
                        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
                        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
                        typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
                        for (int i = 0; i < 3; i++) lUpd.Invoke(lift, null);
                        Shoot($"pop-{vw.name}-{when}-{md}.png");
                        Log($"pop-{vw.name}-{when}-{md}: {(vw.at - vw.eye).magnitude:0} m, lens {vw.fov:0.0} deg; aboard {LiftRiders.Aboard}, near {LiftRiders.NearShown}, mid {LiftRiders.MidShown}, far {LiftRiders.FarShown}, distant {LiftRiders.DistantShown}; riders {LiftRiders.DrawCalls} draws {LiftRiders.Triangles} tris");
                        LiftRiders.TestPhoneBefore = false; LiftRiders.TestCutAt420 = false;
                    });
                }
        // what the riders cost now, the rider at every 30 m down the mountain (as LiftShot's budget)
        steps.Add(() =>
        {
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
            cam.fieldOfView = 62f;
            int n = 0, mid = 0, far = 0, dist = 0, drawsMax = 0; long tris = 0, trisMax = 0; double ms = 0, msMax = 0;
            float zTop = float.MaxValue, zBot = float.MinValue;
            foreach (var l in LiftLine.Lifts) { zTop = Mathf.Min(zTop, l.ZMin); zBot = Mathf.Max(zBot, l.ZMax); }
            for (float z = Mathf.Max(0f, zTop); z < zBot; z += 30f)
            {
                float x = Slope.Centre(z);
                rider.ResetAt(x, z); body.ResetVisual(); rider.Stop();
                var at = new Vector3(x, Slope.DrawnHeight(x, z), z);
                cam.transform.SetPositionAndRotation(at + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.3f, 1f)));
                for (int i = 0; i < 4; i++)
                {
                    SkiLift.TestClock = 200.0 + z * 0.37 + i * 1.3;
                    typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    lUpd.Invoke(lift, null);
                    double t = sw.Elapsed.TotalMilliseconds;
                    n++; ms += t; msMax = System.Math.Max(msMax, t);
                    mid += LiftRiders.MidShown; far += LiftRiders.FarShown; dist += LiftRiders.DistantShown;
                    tris += LiftRiders.Triangles; trisMax = System.Math.Max(trisMax, LiftRiders.Triangles); drawsMax = Mathf.Max(drawsMax, LiftRiders.DrawCalls);
                }
            }
            Log($"pop budget over {n} frames: riders drawn mid {mid / (float)n:0.0}, far {far / (float)n:0.0}, distant {dist / (float)n:0.0} on average; rider triangles {tris / n} on average, {trisMax} at most; rider draw calls at most {drawsMax}; SkiLift.Update with the riders {ms / n:0.00} ms on average, {msMax:0.00} at most (editor)");
        });
        steps.Add(() => { SkiLift.TestClock = -1.0; Physics.simulationMode = SimulationMode.FixedUpdate; });
    }


    /// <summary>A lift's middle, its chairs going up, seen from the piste above it at 150, 350 and 600 m (the chase
    /// camera's place behind the rider, turned toward the lift, a lens 50 m across there: what the phone shows,
    /// enlarged), for the first three lifts; and where the rider is for each.</summary>
    static List<(string name, Vector3 eye, Vector3 at, float fov, float riderZ)> LiftViews()
    {
        // a lift's middle, its chairs going up, seen from the piste above it at 150, 350 and 600 m (the chase camera's
        // place behind the rider, turned toward the lift, a lens 50 m across there: what the phone shows, enlarged)
        var views = new List<(string name, Vector3 eye, Vector3 at, float fov, float riderZ)>();
        int k = 0;
        foreach (var l in LiftLine.Lifts)
        {
            if (l.second || k >= 3) continue;
            float s = l.plan * 0.5f;
            int span = LiftLine.SpanAt(l, s);
            var target = l.At(s, LiftLine.Half, l.CableY(span, s) - 2.8f);
            foreach (float dist in new[] { 150f, 350f, 600f })
            {
                // somewhere this far off with a clear line to them (the snow sampled along it), uphill of them first,
                // low before high: the chairs coming up toward you
                bool Clear(Vector3 a, Vector3 b) { for (int i = 1; i < 60; i++) { var q = Vector3.Lerp(a, b, i / 60f); if (q.y < Slope.DrawnHeight(q.x, q.z) + 2.5f) return false; } return true; }
                Vector3 eye = Vector3.zero; bool found = false;
                foreach (float lift in new[] { 0.06f, 0.12f, 0.2f, 0.3f, 0.45f })
                {
                    for (int a = 0; a < 18 && !found; a++)
                    {
                        float ang = (a % 2 == 0 ? 1 : -1) * ((a + 1) / 2) * 20f;   // 0, +-20, +-40 .. from straight up the line
                        var back = Quaternion.AngleAxis(ang, Vector3.up) * -l.dir;
                        var e = target + back * dist + Vector3.up * (dist * lift);
                        e.y = Mathf.Max(e.y, Slope.DrawnHeight(e.x, e.z) + 3f);
                        if (Clear(e, target)) { eye = e; found = true; }
                    }
                    if (found) break;
                }
                if (!found) continue;
                float z = Mathf.Min(eye.z, target.z - 60f);   // (the rider somewhere the lift is in range of)
                float d = (target - eye).magnitude;
                views.Add(($"lift{k}-{dist:0}m", eye, target, 2f * Mathf.Atan(25f / d) * Mathf.Rad2Deg, z));
            }
            k++;
        }
        return views;
    }

    // ---------------------------------------------------------------- the lifts' chairs on the phone (liftchairs)

    /// <summary>
    /// The lifts as the phone drew them from farview on (c831890) and as now. The chairs, towers, heads, sheave trains,
    /// pads and stations are instanced draws of one material, made at run time from Resources/FarLit.shader; the build
    /// kept none of that shader's instanced variants, so on the device all of them came out as nothing (the cables, the
    /// stations' near models and the riders still drawn). The editor keeps every variant: the "phone-before" pictures
    /// leave those instanced parts out (SkiLift's own lists, not drawn), which is what the device did. "after" is what
    /// the editor and, with Resources/FarLit.mat, the phone draw. The world's far band is on, as in the game.
    /// Chase-camera views riding toward three lifts, and each lift's middle from 150, 350 and 600 m.
    /// SUMMIT_PEOPLE_SET=chairs.
    /// </summary>
    static void ChairShots()
    {
        InstancedAssets.Ensure();
        lift = Object.FindFirstObjectByType<SkiLift>();
        dress = Object.FindFirstObjectByType<CourseDressing>();
        lUpd = typeof(SkiLift).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_SEED"), out int sd) ? sd : 4242;
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        Farview.TestOn = true;
        var views = LiftViews();
        // riding down a lift's cleared swath beside its line, the chase camera behind (its place and pitch, the
        // phone's lens): the chairs coming up toward you on one cable and going down on the other, into the distance
        int k = 0;
        foreach (var l in LiftLine.Lifts)
        {
            if (l.second || k >= 3) continue;
            foreach (float share in new[] { 0.1f, 0.45f })
            {
                var g = l.At(l.plan * share, 6.5f, 0f);
                var at = new Vector3(g.x, Slope.DrawnHeight(g.x, g.z), g.z);
                var eye = at - l.dir * 6.5f + Vector3.up * 3.2f;
                eye.y = Mathf.Max(eye.y, Slope.DrawnHeight(eye.x, eye.z) + 2.2f);   // (up the slope behind: above the snow, as the chase camera keeps)
                views.Add(($"lift{k}-ride{(share < 0.2f ? "A" : "B")}", eye, eye + (l.dir + Vector3.down * 0.28f) * 100f, 62f, at.z));
            }
            k++;
        }
        var partsF = typeof(SkiLift).GetField("parts", NP);
        var partT = typeof(SkiLift).GetNestedType("Part", BindingFlags.NonPublic);
        var meshF = partT.GetField("mesh");
        foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
            foreach (var v in views)
                foreach (var mode in new[] { "phone-before", "after" })
                {
                    var vw = v; var md = mode;
                    bool ride = vw.name.Contains("-ride");
                    if (when == "night" && !vw.name.EndsWith("rideA") && !vw.name.EndsWith("150m")) continue;
                    steps.Add(() =>
                    {
                        TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                        // (riding: the rider where the chase camera follows them, as in the game, the far band and what is
                        // built counted from them; the views from afar: the rider on the piste near the lift, as liftpop's)
                        if (ride) { var rl = vw.eye + Vector3.Scale(vw.at - vw.eye, new Vector3(1f, 0f, 1f)).normalized * 6.5f; rider.ResetAt(rl.x, rl.z); }
                        else rider.ResetAt(Slope.Centre(vw.riderZ), vw.riderZ);
                        body.ResetVisual(); rider.Stop();
                        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
                        SkiLift.TestClock = 400.0;
                        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
                        cam.fieldOfView = vw.fov;
                        cam.transform.SetPositionAndRotation(vw.eye, Quaternion.LookRotation(vw.at - vw.eye));
                        Farview.Instance?.Apply();
                        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
                        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
                        // the phone before: SkiLift's instanced parts drew nothing there
                        var parts = (System.Collections.IList)partsF.GetValue(lift);
                        var kept = new List<Mesh>();
                        foreach (var part in parts) { kept.Add((Mesh)meshF.GetValue(part)); if (md == "phone-before") meshF.SetValue(part, null); }
                        typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
                        for (int i = 0; i < 3; i++) lUpd.Invoke(lift, null);
                        lift.Stats(out int draws, out long tris, out int inst);
                        Farview.Instance?.Apply();
                        Shoot($"chairs-{vw.name}-{when}-{md}.png");
                        for (int i = 0; i < parts.Count; i++) meshF.SetValue(parts[i], kept[i]);
                        Log($"chairs-{vw.name}-{when}-{md}: {(vw.at - vw.eye).magnitude:0} m, lens {vw.fov:0.0} deg; lift draws {draws}, {inst} instances, {tris} tris; riders aboard {LiftRiders.Aboard}");
                    });
                }
        steps.Add(() => { SkiLift.TestClock = -1.0; Farview.TestOn = false; Physics.simulationMode = SimulationMode.FixedUpdate; });
    }

    // ---------------------------------------------------------------- camp life

    static CampLife life;
    static void Sim(float seconds, Vector2 stick = default)
    {
        for (float t = 0f; t < seconds; t += dt)
        {
            camp.Walk(stick, dt);
            body.Apply(dt);
            camp.CameraStep(dt);
            life.Step(dt, cam.transform.position);
            life.Tick(dt);
        }
    }

    static CampLife.Agent Find(System.Func<CampLife.Agent, bool> want, float maxSeconds)
    {
        for (float t = 0f; t < maxSeconds; t += 0.5f)
        {
            foreach (var a in life.Agents) if (a.p.gameObject.activeSelf && want(a)) return a;
            Sim(0.5f);
        }
        return null;
    }

    /// <summary>The camera a few metres in front of someone (and a little to the side), at eye height, looking at them.</summary>
    static void Face(CampLife.Agent a, float dist, float side, float up, float fov, float yawOff = 0f)
    {
        var rot = Quaternion.Euler(0f, a.yaw + yawOff, 0f);
        var target = a.p.transform.position + Vector3.up * (a.doing == CampLife.Doing.Sit ? 0.5f : 1.0f) * a.p.Scale;
        var eye = target + rot * new Vector3(side, 0f, dist) + Vector3.up * up;
        eye.y = Mathf.Max(eye.y, Slope.DrawnHeight(eye.x, eye.z) + 0.5f);
        cam.GetComponent<ChaseCamera>().enabled = false;
        Aim(eye, target, fov);
    }

    static void CampShots()
    {
        float cx = Slope.Centre(Slope.PadLip);
        steps.Add(() =>
        {
            life = camp.GetComponent<CampLife>();
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            camp.TeleportTo(new Vector3(cx - 16f, 0f, -45f), 90f);
            Sim(6f);
        });
        void Overview(string name, float clock, Vector3 eye, Vector3 look, float fov)
        {
            steps.Add(() =>
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow(); camp.SetNight(clock > 3f ? 1f : 0f);
                Sim(1f);
                cam.GetComponent<ChaseCamera>().enabled = false;
                Aim(eye, look, fov);
                Shoot($"camp-{name}.png");
                var sbx = new System.Text.StringBuilder();
                foreach (var a in life.Agents) sbx.Append($"{a.role}:{a.doing}@({a.pos.x - cx:0.0},{a.pos.z:0.0}) lod{a.p.Lod}  ");
                Log($"camp-{name}: {sbx}");
            });
        }
        var g = Slope.DrawnHeight(cx, -60f);
        Overview("overview-day", 1.4f, new Vector3(cx + 3f, g + 8f, -44f), new Vector3(cx - 2f, g, -68f), 62f);
        void Doing(string name, System.Func<CampLife.Agent, bool> want, float dist, float side, float up, float fov, float clock = 1.4f, float yawOff = 0f, float wait = 0f)
        {
            steps.Add(() =>
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow(); camp.SetNight(clock > 3f ? 1f : 0f);
                var a = Find(want, 300f);
                if (a == null) { Log($"camp-{name}: nobody doing that in 5 minutes"); return; }
                if (wait > 0f) Sim(wait);
                // the hero out of the picture
                camp.TeleportTo(a.pos + Quaternion.Euler(0f, a.yaw + 180f, 0f) * Vector3.forward * 9f, a.yaw, false);
                Sim(0.1f);
                Face(a, dist, side, up, fov, yawOff);
                Shoot($"camp-{name}.png");
                Log($"camp-{name}: role {a.role} {a.doing} at ({a.pos.x - cx:0.0}, {a.pos.z:0.0}) lod {a.p.Lod} look {a.p.Look.jacket.name} {(a.p.Look.female ? "F" : "M")}");
            });
        }
        Doing("photo", a => a.role == 0 && a.doing == CampLife.Doing.Look && a.p.phoneUp > 0.9f, 3.2f, 1.2f, 0.1f, 45f);
        Doing("chat", a => a.doing == CampLife.Doing.Chat && a.partner != null && a.partner.doing == CampLife.Doing.Chat && (a.pos - a.partner.pos).magnitude < 1.6f, 3.6f, 2.4f, 0.2f, 50f, 1.4f, 90f);
        Doing("bench", a => a.doing == CampLife.Doing.Sit && a.p.sitW > 0.95f, 3.4f, 0.8f, 0.3f, 50f);
        Doing("wax", a => a.doing == CampLife.Doing.Wax, 3.0f, -1.6f, 0.4f, 48f, 1.4f, 0f, 1.2f);
        Doing("strap", a => a.doing == CampLife.Doing.Strap && a.t > 1.5f, 3.4f, 1.6f, 0.5f, 48f);
        Doing("ride", a => a.doing == CampLife.Doing.Ride && a.pos.z > Slope.GateZ + 6f, 7f, -3.5f, 1.5f, 50f, 1.4f, 200f);
        Doing("walk", a => a.doing == CampLife.Doing.Walk && a.speed > 0.8f && a.p.Lod == 0, 4.5f, 2.5f, 0.3f, 48f);
        // the edge gives way under the hero: everyone looks
        steps.Add(() =>
        {
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); camp.SetNight(0f);
            var at = new Vector3(cx + 23.5f, Slope.DrawnHeight(cx + 23.5f, -66f), -66f);
            camp.TeleportTo(new Vector3(cx - 20f, 0f, -50f), 0f, false);
            typeof(CampLife).GetMethod("OnCollapse", NP).Invoke(life, new object[] { at, Vector3.right });
            Sim(6f);
            cam.GetComponent<ChaseCamera>().enabled = false;
            // over the people turned to the rail where it went
            Aim(new Vector3(cx + 7f, g + 3.2f, -57f), new Vector3(cx + 20f, g + 0.4f, -67f), 62f);
            Shoot("camp-fall-day.png");
            var sbx = new System.Text.StringBuilder();
            foreach (var a in life.Agents) sbx.Append($"{a.role}:{a.doing}@({a.pos.x - cx:0.0},{a.pos.z:0.0}) facing {Vector3.Angle(a.p.Facing, at - a.p.HeadPosition):0}deg  ");
            Log("camp-fall: " + sbx);
        });
        Overview("overview-night", 3.4f, new Vector3(cx + 3f, g + 8f, -44f), new Vector3(cx - 2f, g, -68f), 62f);
        Doing("chat-night", a => a.doing == CampLife.Doing.Chat && a.partner != null && a.partner.doing == CampLife.Doing.Chat && (a.pos - a.partner.pos).magnitude < 1.6f, 3.6f, 2.4f, 0.2f, 50f, 3.4f, 90f);
        Doing("photo-night", a => a.doing == CampLife.Doing.Look, 3.2f, 1.2f, 0.1f, 45f, 3.4f);
        Overview("lodge-day", 1.4f, camp.ChaletPoint(9f, 1.6f, 24f), camp.ChaletPoint(-2f, 1f, 10f), 55f);
    }

    // ---------------------------------------------------------------- the camera

    static void Aim(Vector3 eye, Vector3 look, float fov)
    {
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
        cam.fieldOfView = fov;
    }

    static void Shoot(string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); aLate.Invoke(atmo, null);
        // (skinned meshes don't skin in batchmode: bake each into a plain mesh for the picture)
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterials = sk.sharedMaterials; mr.shadowCastingMode = sk.shadowCastingMode;
            var mpb = new MaterialPropertyBlock(); sk.GetPropertyBlock(mpb); mr.SetPropertyBlock(mpb);
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; off.Add(sk); baked.Add(g);
        }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in off) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Log($"{file} cam={cam.transform.position}");
    }
}
