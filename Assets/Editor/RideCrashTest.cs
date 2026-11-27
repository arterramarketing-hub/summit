using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// (rides2) What throws the rider off each ride, and what each ride runs down, as the game plays it (the rider's own
/// step, its look ahead for what it meets, the mountain's people's own step):
///  - Hard turning on open snow never throws anyone off, on any ride (the board, skis, the toboggan, the snow tube,
///    the kayak, the car, the bear): full lock from one way to the other, held full lock, quick flicks, at speed.
///  - The car: a tree hit head on and fast only dents it (it bounces off, the driver stays in), twice; the third tree
///    throws the driver out. A rock throws the driver out at once. Anything else built (a cabin) is a bump. An animal
///    is run down. A landing however hard (one that would slam a board rider) is ridden out on its springs.
///  - The board, for comparison: a tree head on and fast, and a slam, still knock you down.
///  - Run-overs: the car and the bear run down the mountain's riders and the people at a lift's station, every time,
///    and never go down for it: the person is thrown (their ragdoll) and bleeds (Blood: marks laid). On the board the
///    first rider hit is still the warning (the free hit).
///  - The bear is drawn its own size (about 1.5 m at the back: a skin with no bones is drawn with its root's x100).
/// Lines start RIDECRASH; exits 1 on any failure.
/// Batch: -batchmode -nographics -quit -projectPath . -executeMethod RideCrashTest.Run
/// </summary>
public static class RideCrashTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static int fails, checks;
    static SummitGame game; static Summit.Rider rider; static RiderBody body; static MountainRiders mr; static SlopeStreamer terrain; static Forest props; static Camera cam; static Blood blood;
    static readonly List<string> crashes = new List<string>();
    static readonly List<GameObject> made = new List<GameObject>();
    static int dents, ranOver;
    static float hardest;
    static bool contacts;   // (asked for once, the look ahead stays on: the hard turning is ridden before)

    static void Check(string what, bool ok, string got = "")
    {
        checks++; if (!ok) fails++;
        Debug.Log($"RIDECRASH {(ok ? "PASS" : "FAIL")} {what} {got}");
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        int rideWas = (int)Progress.Ride;
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Summit.Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        blood = Get<Blood>("blood");
        mr = MountainRiders.Current; cam = Camera.main;
        RiderInput.TestDrive = true; RiderInput.TestFingers = 0;
        MountainRiders.TestWant = 0;
        bool bloodWas = blood != null && blood.On;
        if (blood != null && !blood.On) blood.SetOn(true);
        rider.Crash += why => crashes.Add($"{why} ({rider.Ride}{(rider.Tube ? " tube" : "")}, {rider.Speed:0.0} m/s, tip {rider.Tip:0.00}, skid {rider.Skid:0.00}, air {rider.Airborne})");
        rider.CarDented += (n, at) => dents++;
        rider.RanOver += (at, into, person) => ranOver++;
        rider.Landed += impact => hardest = Mathf.Max(hardest, impact);
        try
        {
            // ---- hard turning on open snow (nothing met: the look ahead not asked for yet)
            foreach (var k in new[] { RideKind.Snowboard, RideKind.Skis, RideKind.Toboggan, RideKind.Kayak, RideKind.Car, RideKind.Bear }) Turning(k, false);
            Turning(RideKind.Snowboard, true);
            // ---- the bear, its own size
            NewRun(RideKind.Bear);
            for (int i = 0; i < 10; i++) Step(0f);
            var smr = body.BearGear != null ? body.BearGear.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            // (its mesh's box as drawn: by the renderer's whole scale, as a phone draws a skin with no bones)
            var drawn = smr != null ? Vector3.Scale(smr.localBounds.size, smr.transform.lossyScale) : Vector3.zero;
            float longest = Mathf.Max(drawn.x, Mathf.Max(drawn.y, drawn.z));
            Check("BEAR: drawn its own size (as a phone draws a skin with no bones)", longest > 1.5f && longest < 4.5f, $"{drawn.x:0.00} x {drawn.y:0.00} x {drawn.z:0.00} m (scale {(smr != null ? smr.transform.lossyScale.x : 0f):0.000})");
            // ---- from here on, what the rider meets
            contacts = true;
            CarHits();
            Landings();
            RunOvers();
        }
        catch (System.Exception e) { Debug.LogError("RIDECRASH exception " + e); fails++; }
        foreach (var g in made) if (g != null) Object.DestroyImmediate(g);
        RiderInput.TestDrive = false; RiderInput.TestFingers = 0; MountainRiders.TestWant = -1;
        rider.Tube = false; body.SetTube(false);
        Progress.PickRide((RideKind)rideWas);
        if (blood != null && !bloodWas) blood.SetOn(false);
        Debug.Log(fails == 0 ? $"RIDECRASH PASS ({checks} checks)" : $"RIDECRASH FAIL ({fails} of {checks})");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    static string Name(RideKind k, bool tube) => tube ? "SNOW TUBE" : Rides.Of(k).Name;

    /// <summary>A new run on this ride, riding straight down the run at z.</summary>
    static void NewRun(RideKind k, bool tube = false, float z = 320f, float speed = 11f)
    {
        Progress.PickRide(k);
        rider.Tube = tube; body.SetTube(tube);
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.forward * speed;
        terrain.Prewarm(); props.Prewarm();
        crashes.Clear();
    }

    /// <summary>One frame as the game steps it: two physics steps (each with the look ahead, once asked for), the bodies, the people.</summary>
    static void Step(float steer)
    {
        rider.Steer = steer;
        for (int k = 0; k < 2; k++)
        {
            if (contacts) rider.TestContacts(Dt / 2f);
            rider.Tick(Dt / 2f); body.PhysicsStep(); mr?.PhysicsStep(); LiftEnds.Instance?.PhysicsStep(); Physics.Simulate(Dt / 2f);
        }
        body.Apply(Dt);
        var p = rider.transform.position;
        cam.transform.SetPositionAndRotation(p + new Vector3(0f, 3f, -7f), Quaternion.LookRotation(new Vector3(0f, -2.5f, 7f)));
        mr?.Step(Dt, cam);
        game.CrashTapStep(Dt);
        typeof(SummitGame).GetField("falls", NP).SetValue(game, 0);
    }

    /// <summary>Down the run's middle 30 m ahead.</summary>
    static float Pilot()
    {
        float aim = Slope.Centre(rider.Z + 30f);
        return Mathf.Clamp((Mathf.Atan2(aim - rider.X, 30f) - rider.TravelHeading) * 2.5f, -1f, 1f);
    }

    static void UpTo(float speed, float most = 15f)
    {
        for (float t = 0f; t < most && rider.Speed < speed && !rider.Crashed; t += Dt) Step(Pilot());
    }

    // ---------------------------------------------------------------- hard turning

    static void Turning(RideKind k, bool tube)
    {
        string n = Name(k, tube);
        var all = new List<string>();
        float fastest = 0f;
        // full lock from one way to the other, held full lock each way, quick flicks: each from a fresh run at speed
        for (int phase = 0; phase < 4; phase++)
        {
            NewRun(k, tube);
            UpTo(24f);
            fastest = Mathf.Max(fastest, rider.Speed);
            for (float t = 0f; t < 6f && !rider.Crashed; t += Dt)
            {
                float s = phase == 0 ? (Mathf.Repeat(t, 0.9f) < 0.45f ? 1f : -1f)
                        : phase == 1 ? (t < 3f ? 1f : -1f)
                        : phase == 2 ? (Mathf.Repeat(t, 0.3f) < 0.15f ? 1f : -1f)
                        : (Mathf.Repeat(t, 1.6f) < 0.8f ? 1f : -1f);
                Step(s);
            }
            all.AddRange(crashes);
        }
        Check($"{n}: hard turning at speed never throws the rider off", all.Count == 0, all.Count == 0 ? $"(up to {fastest * 3.6f:0} km/h)" : string.Join("; ", all));
    }

    // ---------------------------------------------------------------- the car and what it hits

    /// <summary>Something solid `ahead` m on the rider's line (x across it), standing on the snow.</summary>
    static GameObject Put(string label, float ahead, float x = 0f, bool always = false)
    {
        var p = rider.Body.position; var v = rider.Body.linearVelocity; v.y = 0f;
        var dir = v.sqrMagnitude > 0.5f ? v.normalized : Vector3.forward;
        var at = p + dir * ahead + Vector3.Cross(Vector3.up, dir) * x; at.y = Slope.DrawnHeight(at.x, at.z);
        var g = new GameObject("RideCrash" + label);
        // (stood square to the snow and to the way the rider comes: a wall stood upright on a steep slope is met
        // glancing, the rider's way pitched down the slope under it)
        g.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(dir, Slope.Normal(at.x, at.z)), Slope.Normal(at.x, at.z)));
        switch (label)
        {
            case "Tree": { g.transform.rotation = Quaternion.identity; var c = g.AddComponent<CapsuleCollider>(); c.radius = 0.35f; c.height = 10f; c.center = Vector3.up * 5f; break; }
            case "Rock": { var c = g.AddComponent<SphereCollider>(); c.radius = 1.4f; c.center = Vector3.up * 0.9f; break; }
            case "Cabin!": { var c = g.AddComponent<BoxCollider>(); c.size = new Vector3(6f, 4f, 4f); c.center = Vector3.up * 2f; break; }
            default: { var c = g.AddComponent<CapsuleCollider>(); c.radius = 0.5f; c.height = 1.6f; c.center = Vector3.up * 0.8f; break; }
        }
        var ob = g.AddComponent<Obstacle>(); ob.label = label; ob.always = always;
        made.Add(g);
        Physics.SyncTransforms();
        return g;
    }

    static void Gone(GameObject g) { if (g != null) { made.Remove(g); Object.DestroyImmediate(g); Physics.SyncTransforms(); } }

    /// <summary>Straight on into what was put ahead, for up to `most` s (or until down); the speed going in.</summary>
    static float Into(float most = 2f)
    {
        float before = rider.Speed;
        for (float t = 0f; t < most && !rider.Crashed; t += Dt) Step(0f);
        return before;
    }

    static void CarHits()
    {
        // three trees, one after another in the same run
        NewRun(RideKind.Car);
        UpTo(20f);
        dents = 0;
        float s1 = rider.Speed;
        var t1 = Put("Tree", 12f); Into(1.5f);
        Check("CAR: the first tree hit head on dents it, the driver stays in", !rider.Crashed && rider.CarDents == 1 && dents == 1, $"{s1:0.0} m/s in, {rider.Speed:0.0} after, dents {rider.CarDents}, crashed {rider.Crashed} {string.Join("; ", crashes)}");
        Gone(t1);
        for (int i = 0; i < 60; i++) Step(Pilot());
        UpTo(20f);
        float s2 = rider.Speed;
        var t2 = Put("Tree", 12f); Into(1.5f);
        Check("CAR: the second tree dents it too, still in", !rider.Crashed && rider.CarDents == 2, $"{s2:0.0} m/s in, dents {rider.CarDents}, crashed {rider.Crashed} {string.Join("; ", crashes)}");
        Gone(t2);
        for (int i = 0; i < 60; i++) Step(Pilot());
        UpTo(20f);
        float s3 = rider.Speed;
        var t3 = Put("Tree", 12f); Into(1.5f);
        Check("CAR: the third tree throws the driver out (through the windscreen)", rider.Crashed && crashes.Count > 0 && crashes[0].StartsWith("Tree"), $"{s3:0.0} m/s in, {string.Join("; ", crashes)}");
        Check("CAR: and the count starts again", rider.CarDents == 0, $"dents {rider.CarDents}");
        Gone(t3);
        // a glancing tree is no dent at all
        NewRun(RideKind.Car); UpTo(20f);
        var tg = Put("Tree", 12f, 0.55f); Into(1.5f);
        Check("CAR: a tree clipped off-centre is a glance (no dent, still in)", !rider.Crashed && rider.CarDents == 0, $"dents {rider.CarDents}, {string.Join("; ", crashes)}");
        Gone(tg);
        // a rock: out at once
        NewRun(RideKind.Car); UpTo(18f);
        var r = Put("Rock", 12f); float sr = Into(1.5f);
        Check("CAR: a rock hit head on throws the driver out", rider.Crashed && crashes.Count > 0 && crashes[0].StartsWith("Rock"), $"{sr:0.0} m/s in, {string.Join("; ", crashes)}");
        Gone(r);
        // anything else built: a bump
        NewRun(RideKind.Car); UpTo(18f);
        dents = 0;
        var cab = Put("Cabin!", 12f); Into(1.5f);
        Check("CAR: a cabin is a bump, not out through the windscreen (and no tree's dent)", !rider.Crashed && dents == 1 && rider.CarDents == 0, $"bumps {dents}, dents {rider.CarDents}, {string.Join("; ", crashes)}");
        Gone(cab);
        // an animal: run down
        NewRun(RideKind.Car); UpTo(18f);
        int ro = ranOver;
        var deer = Put("Deer!", 12f, 0f, true); Into(1.5f);
        Check("CAR: an animal is run down, the car goes on", !rider.Crashed && ranOver == ro + 1, $"run down {ranOver - ro}, {string.Join("; ", crashes)}");
        Gone(deer);
        // the board, for comparison: a tree head on and fast still knocks you down
        NewRun(RideKind.Snowboard); UpTo(18f);
        var tb = Put("Tree", 12f); float sb = Into(1.5f);
        Check("SNOWBOARD: a tree head on and fast still knocks you down", rider.Crashed && crashes.Count > 0 && crashes[0].StartsWith("Tree"), $"{sb:0.0} m/s in, {string.Join("; ", crashes)}");
        Gone(tb);
    }

    // ---------------------------------------------------------------- landings

    static void Landings()
    {
        foreach (var k in new[] { RideKind.Car, RideKind.Snowboard })
        {
            NewRun(k); UpTo(10f);
            // dropped onto the snow square on, hard (as off a big drop onto the flat: straight into it)
            var p = rider.Body.position + Vector3.up * 9f;
            rider.Body.position = p; rider.transform.position = p;
            hardest = 0f;
            var vOff = (Vector3)typeof(Summit.Rider).GetMethod("TakeOff", NP).Invoke(rider, new object[] { p, -Slope.Normal(p.x, p.z) * 33f, 0.1f, true });
            rider.Body.linearVelocity = vOff;
            for (float t = 0f; t < 2f && (rider.Airborne || t < 0.2f) && !rider.Crashed; t += Dt) Step(0f);
            for (int i = 0; i < 30 && !rider.Crashed; i++) Step(0f);
            float impact = Mathf.Max(hardest, rider.LastLandImpact);   // (the car skips on its springs: its first landing is the hard one)
            if (k == RideKind.Car) Check("CAR: a landing that would slam a board rider is ridden out on its springs", !rider.Crashed && impact > Summit.Rider.SlamImpact, $"impact {impact:0.0} m/s, {string.Join("; ", crashes)}");
            else Check("SNOWBOARD: the same landing still slams you", rider.Crashed && crashes.Count > 0 && crashes[0].StartsWith("Slammed"), $"impact {impact:0.0} m/s, {string.Join("; ", crashes)}");
        }
    }

    // ---------------------------------------------------------------- run-overs

    static MountainRiders.Rider Person(float ahead, bool ski)
    {
        var p = rider.Body.position; var v = rider.Body.linearVelocity; v.y = 0f;
        var dir = v.sqrMagnitude > 0.5f ? v.normalized : Vector3.forward;
        var at = p + dir * ahead; at.y = Slope.DrawnHeight(at.x, at.z);
        var r = mr.TestOnly(at, ski);
        mr.Step(Dt, cam);
        return r;
    }

    /// <summary>Let the thrown fly and land (the rider held where they are), for the blood to be laid.</summary>
    static void Settle(float seconds, Vector3 look)
    {
        var eye = look + new Vector3(0f, 4f, -9f);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        var at = rider.Body.position; var v = rider.Body.linearVelocity; bool kin = rider.Body.isKinematic;
        rider.Body.isKinematic = true;
        for (float t = 0f; t < seconds; t += Dt)
        {
            mr.PhysicsStep(); LiftEnds.Instance?.PhysicsStep(); Physics.Simulate(Dt);
            mr.Step(Dt, cam); LiftEnds.Instance?.Step(Dt, cam.transform.position);
            blood?.Advance(Dt);
        }
        rider.Body.isKinematic = kin; rider.Body.position = at; rider.transform.position = at; rider.Body.linearVelocity = v;
    }

    static void RunOvers()
    {
        if (mr == null) { Check("the mountain's riders are there", false); return; }
        foreach (var k in new[] { RideKind.Car, RideKind.Bear })
        {
            string n = Rides.Of(k).Name;
            if (blood != null) blood.Clear();
            int k0 = MountainRiders.Knocked, ro = ranOver, thrown = 0, warned = 0, downs = 0;
            MountainRiders.Rider last = null;
            var log = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                // (each from a start of its own, straight down the run: the one before is left lying behind)
                NewRun(k, false, 320f + 90f * i); UpTo(16f);
                int before = MountainRiders.Knocked;
                var r = Person(12f, i == 1);
                float sp = rider.Speed;
                for (float t = 0f; t < 2.5f && MountainRiders.Knocked == before && !rider.Crashed; t += Dt) Step(0f);
                bool hit = MountainRiders.Knocked > before;
                if (hit && r.down != null && r.down.Active) thrown++;
                if (rider.FreeHitUsed) warned++;
                if (rider.Crashed) downs++;
                for (int f = 0; f < 30 && !rider.Crashed; f++) Step(0f);
                if (rider.Crashed) downs++;
                log.Add($"#{i + 1} {(hit ? "hit" : "missed")} at {sp:0.0} m/s");
                last = r;
            }
            int knocked = MountainRiders.Knocked - k0;
            Check($"{n}: runs down three of the mountain's riders, and never goes down for it", knocked == 3 && downs == 0 && warned == 0 && ranOver - ro == 3, $"knocked {knocked} ({string.Join(", ", log)}), run down {ranOver - ro}, down {downs} {string.Join("; ", crashes)}, warned {warned}");
            Check($"{n}: each one run down is thrown (their ragdoll)", thrown == knocked && knocked > 0, $"{thrown} of {knocked}");
            Settle(2.5f, last != null ? last.pos : rider.transform.position);
            int marks = blood != null ? blood.Shown : 0;
            Check($"{n}: and they bleed (marks on the snow)", blood == null || marks > 0, $"{marks} marks");
        }
        // the board, for comparison: the first rider hit is the warning (they go down, you stay up)
        NewRun(RideKind.Snowboard); UpTo(15f);
        int b0 = MountainRiders.Knocked;
        Person(12f, false);
        for (float t = 0f; t < 2.5f && MountainRiders.Knocked == b0 && !rider.Crashed; t += Dt) Step(0f);
        Check("SNOWBOARD: the first rider hit is still the warning (the free hit)", MountainRiders.Knocked == b0 + 1 && !rider.Crashed && rider.FreeHitUsed, $"knocked {MountainRiders.Knocked - b0}, crashed {rider.Crashed}");

        // the people at a lift's station: run down by the car as well
        LiftStation();
    }

    static void LiftStation()
    {
        var le = LiftEnds.Instance;
        if (le == null) { Check("CAR: a lift's people (no lifts on this mountain)", false); return; }
        var ends = (System.Collections.IList)typeof(LiftEnds).GetField("ends", NP).GetValue(le);
        LiftEnds.End bottom = null;
        // (the ends are laid out once the lifts are: round the start, then the nearest bottom station)
        le.Prewarm(rider.transform.position, 0f);
        float best = float.MaxValue;
        foreach (LiftEnds.End e in ends) { if (e.top) continue; float d = Mathf.Abs(e.st.z - 320f); if (d < best) { best = d; bottom = e; } }
        if (bottom == null) { Check("CAR: a lift's people (no bottom station found)", false); return; }
        le.Prewarm(bottom.st, 2f);
        // someone with the open snow straight up the hill of them
        CapsuleCollider target = null; Vector3 from = default;
        foreach (var c in Object.FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None))
        {
            var ob = c.GetComponent<Obstacle>();
            if (ob == null || ob.person != (IPersonHit)le || !c.enabled) continue;
            Vector3 at = c.transform.position, start = at - Vector3.forward * 14f;
            start.y = Slope.DrawnHeight(start.x, start.z) + Summit.Rider.R;
            if (Physics.SphereCast(start, Summit.Rider.R, (at + Vector3.up * 0.5f - start).normalized, out var hit, 16f, ~0, QueryTriggerInteraction.Ignore))
            {
                var hob = hit.collider.GetComponent<Obstacle>();
                if (hob != null && hob.person == (IPersonHit)le) { target = (CapsuleCollider)hit.collider; from = start; break; }
                Debug.Log($"RIDECRASH a lift's person at {at}: the way down to them meets {hit.collider.name} ({(hob != null ? hob.label : "-")}) first");
            }
        }
        if (target == null) { Debug.Log("RIDECRASH a lift's people: nobody with clear snow up the hill of them; knocked directly instead"); }
        int k0 = LiftEnds.Knocked;
        if (target != null)
        {
            NewRun(RideKind.Car, false, from.z, 16f);
            rider.ResetAt(from.x, from.z); body.ResetVisual(); rider.Begin();
            var dir = target.transform.position - from; dir.y = 0f;
            rider.Body.linearVelocity = dir.normalized * 16f;
            for (float t = 0f; t < 2.5f && LiftEnds.Knocked == k0 && !rider.Crashed; t += Dt) { Step(0f); le.Step(Dt, rider.transform.position); }
            Check("CAR: runs down someone at a lift's station (thrown, and on it goes)", LiftEnds.Knocked == k0 + 1 && !rider.Crashed, $"knocked {LiftEnds.Knocked - k0}, crashed {rider.Crashed} {string.Join("; ", crashes)}");
        }
        else
        {
            foreach (var c in Object.FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None))
            {
                var ob = c.GetComponent<Obstacle>();
                if (ob == null || ob.person != (IPersonHit)le || !c.enabled) continue;
                le.Knock(c, Vector3.forward * 12f + Vector3.up * 3f, c.transform.position + Vector3.up);
                break;
            }
            Check("a lift's people can be knocked over (thrown)", LiftEnds.Knocked == k0 + 1, $"knocked {LiftEnds.Knocked - k0}");
        }
        // and they lie there a while, then sit up where they landed
        Settle(8f, rider.transform.position);
        Debug.Log($"RIDECRASH a lift's people knocked {LiftEnds.Knocked - k0}");
    }
}
