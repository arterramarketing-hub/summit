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
///  - The mountain bike (mtb): a rock or a tree hit square on and hard sends the rider over the bars; a glance does not;
///    hard turning never does (above).
///  - Run-overs: the car, the bear and the bike run down the mountain's riders and the people at a lift's station, every time,
///    and never go down for it: the person is thrown (their ragdoll) and bleeds (Blood: marks laid). On the board the
///    first rider hit is still the warning (the free hit).
///  - The bear is drawn its own size (about 1.5 m at the back: a skin with no bones is drawn with its root's x100).
///  - (tube) The snow tube bounces off a tree, a rock, a person and a fence, and stays on: each bounce is told to the
///    game (Rider.TubeHit: the ring's look and its sound go by it), the ring flattens against it and springs back, spins,
///    tips, hops and the rider is thrown in the seat, all of it bigger for a harder hit, and all of it settles again.
/// Lines start RIDECRASH; exits 1 on any failure. With $RIDECRASH_SHOTS (and graphics) it also writes frames of the
/// tube's hard bounce off a tree there.
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
    static int dents, ranOver, tubeHits;
    static float tubeHitInto;
    static float hardest;
    static bool contacts;   // (asked for once, the look ahead stays on: the hard turning is ridden before)
    const int Mountain = 1000;

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
        rider = game.rider; body = game.body; terrain = game.terrain; props = game.props;
        blood = game.blood;
        // (one mountain, always: the boot's is whatever Unity's random numbers pick, and a steeper pitch or a tree in the
        // line changes what every hit here meets)
        TestBoot.NewMountain(game, Mountain, terrain, props);
        mr = MountainRiders.Current; cam = Camera.main;
        RiderInput.TestDrive = true; RiderInput.TestFingers = 0;
        MountainRiders.TestWant = 0;
        bool bloodWas = blood != null && blood.On;
        if (blood != null && !blood.On) blood.SetOn(true);
        rider.Crash += why => crashes.Add($"{why} ({rider.Ride}{(rider.Tube ? " tube" : "")}, {rider.Speed:0.0} m/s, tip {rider.Tip:0.00}, skid {rider.Skid:0.00}, air {rider.Airborne})");
        rider.CarDented += (n, at) => dents++;
        rider.RanOver += (at, into, person) => ranOver++;
        rider.TubeHit += (n, into) => { tubeHits++; tubeHitInto = Mathf.Max(tubeHitInto, into); };
        rider.Landed += impact => hardest = Mathf.Max(hardest, impact);
        try
        {
            // ---- hard turning on open snow (nothing met: the look ahead not asked for yet)
            foreach (var k in new[] { RideKind.Snowboard, RideKind.Skis, RideKind.Toboggan, RideKind.Kayak, RideKind.Car, RideKind.Bear, RideKind.Bike }) Turning(k, false);
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
            TubeBounces();
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
        var all = new List<string>(); var landed = new List<string>();
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
            foreach (var c in crashes) (Landing(c) ? landed : all).Add(c);
        }
        Check($"{n}: hard turning at speed never throws the rider off", all.Count == 0, (all.Count == 0 ? $"(up to {fastest * 3.6f:0} km/h)" : string.Join("; ", all)) + (landed.Count > 0 ? $"; besides, off the mountain's own drops: {string.Join("; ", landed)}" : ""));
    }

    /// <summary>A fall that is the mountain's (a landing off its drops and kickers, a cliff, a crevasse), not the turning's.</summary>
    static bool Landing(string c) => c.StartsWith("Slammed") || c.StartsWith("Landed") || c.StartsWith("Cliffed") || c.StartsWith("Crevasse");

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
        // (something to see in the pictures: no collider of its own)
        var col = g.GetComponent<Collider>();
        var look = GameObject.CreatePrimitive(col is SphereCollider ? PrimitiveType.Sphere : col is BoxCollider ? PrimitiveType.Cube : PrimitiveType.Cylinder);
        Object.DestroyImmediate(look.GetComponent<Collider>());
        look.transform.SetParent(g.transform, false);
        if (col is SphereCollider sc) { look.transform.localPosition = sc.center; look.transform.localScale = Vector3.one * sc.radius * 2f; }
        else if (col is BoxCollider bc) { look.transform.localPosition = bc.center; look.transform.localScale = bc.size; }
        else if (col is CapsuleCollider cc) { look.transform.localPosition = cc.center; look.transform.localScale = new Vector3(cc.radius * 2f, cc.height * 0.5f, cc.radius * 2f); }
        made.Add(g);
        Physics.SyncTransforms();
        return g;
    }

    static void Gone(GameObject g) { if (g != null) { made.Remove(g); Object.DestroyImmediate(g); Physics.SyncTransforms(); } }

    /// <summary>Into what was put ahead, for up to `most` s (or until down); the speed going in. Aimed at it (steered at its
    /// middle all the way, as a rider means to hit it square on), or with aim false straight on as it was put.</summary>
    static float Into(float most = 2f, GameObject at = null)
    {
        float before = rider.Speed;
        if (at != null)
        {
            for (float t = 0f; t < most && !rider.Crashed; t += Dt)
            {
                var d = at.transform.position - rider.transform.position;
                Step(Mathf.Clamp(Mathf.DeltaAngle(rider.TravelHeading * Mathf.Rad2Deg, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg) * Mathf.Deg2Rad * 3f, -1f, 1f));
            }
            return before;
        }

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
        var t1 = Put("Tree", 12f); Into(1.5f, t1);
        Check("CAR: the first tree hit head on dents it, the driver stays in", !rider.Crashed && rider.CarDents == 1 && dents == 1, $"{s1:0.0} m/s in, {rider.Speed:0.0} after, dents {rider.CarDents}, crashed {rider.Crashed} {string.Join("; ", crashes)}");
        Gone(t1);
        for (int i = 0; i < 60; i++) Step(Pilot());
        UpTo(20f);
        float s2 = rider.Speed;
        var t2 = Put("Tree", 12f); Into(1.5f, t2);
        Check("CAR: the second tree dents it too, still in", !rider.Crashed && rider.CarDents == 2, $"{s2:0.0} m/s in, dents {rider.CarDents}, crashed {rider.Crashed} {string.Join("; ", crashes)}");
        Gone(t2);
        for (int i = 0; i < 60; i++) Step(Pilot());
        UpTo(20f);
        float s3 = rider.Speed;
        var t3 = Put("Tree", 12f); Into(1.5f, t3);
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
        var r = Put("Rock", 12f); float sr = Into(1.5f, r);
        Check("CAR: a rock hit head on throws the driver out", rider.Crashed && crashes.Count > 0 && crashes[0].StartsWith("Rock"), $"{sr:0.0} m/s in, {string.Join("; ", crashes)}");
        Gone(r);
        // anything else built: a bump
        NewRun(RideKind.Car); UpTo(18f);
        dents = 0;
        var cab = Put("Cabin!", 12f); Into(1.5f, cab);
        Check("CAR: a cabin is a bump, not out through the windscreen (and no tree's dent)", !rider.Crashed && dents == 1 && rider.CarDents == 0, $"bumps {dents}, dents {rider.CarDents}, {string.Join("; ", crashes)}");
        Gone(cab);
        // an animal: run down
        NewRun(RideKind.Car); UpTo(18f);
        int ro = ranOver;
        var deer = Put("Deer!", 12f, 0f, true); Into(1.5f, deer);
        Check("CAR: an animal is run down, the car goes on", !rider.Crashed && ranOver == ro + 1, $"run down {ranOver - ro}, {string.Join("; ", crashes)}");
        Gone(deer);
        // (mtb) the mountain bike: over the bars into a rock, or a tree hit square on and hard; a glance is a glance
        NewRun(RideKind.Bike); UpTo(16f);
        var br = Put("Rock", 12f); float sbr = Into(1.5f, br);
        Check("BIKE: a rock hit head on sends the rider over the bars", rider.Crashed && crashes.Count > 0 && crashes[0].StartsWith("Rock"), $"{sbr:0.0} m/s in, {string.Join("; ", crashes)}");
        Gone(br);
        NewRun(RideKind.Bike); UpTo(16f);
        var bt = Put("Tree", 12f); float sbt = Into(1.5f, bt);
        Check("BIKE: a tree hit square on and hard does too", rider.Crashed && crashes.Count > 0 && crashes[0].StartsWith("Tree"), $"{sbt:0.0} m/s in, {string.Join("; ", crashes)}");
        Gone(bt);
        NewRun(RideKind.Bike); UpTo(16f);
        var bg = Put("Tree", 12f, 0.55f); Into(1.5f);
        Check("BIKE: a tree clipped off-centre is a glance, still riding", !rider.Crashed, string.Join("; ", crashes));
        Gone(bg);
        // the board, for comparison: a tree head on and fast still knocks you down
        NewRun(RideKind.Snowboard); UpTo(18f);
        var tb = Put("Tree", 12f); float sb = Into(1.5f, tb);
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
        foreach (var k in new[] { RideKind.Car, RideKind.Bear, RideKind.Bike })
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

    // ---------------------------------------------------------------- the snow tube's bounce

    struct Bounce { public int hits; public float into, squash, hop, tip, jolt, spin; public bool down; public string crash; }

    /// <summary>The tube into what was put ahead: how it was met and how it showed it (the most of each over 1.5 s).</summary>
    static Bounce TubeInto(GameObject what, float most = 2f, string shots = null)
    {
        var b = new Bounce();
        int shot = 0; Vector3 eye = default, look = default;
        int h0 = tubeHits; tubeHitInto = 0f;
        float spin0 = TubeSpin();
        for (float t = 0f, after = -1f; t < most && !rider.Crashed; t += Dt)
        {
            Step(0f);
            if (tubeHits > h0 && after < 0f)
            {
                after = t;
                // (the camera stood off to the side of the hit, where it stays)
                var p = rider.transform.position; var fwd = rider.Body.linearVelocity; fwd.y = 0f; fwd.Normalize();
                var side = Vector3.Cross(Vector3.up, fwd);
                look = p + fwd * 1.2f + Vector3.up * 0.4f; eye = p - fwd * 4.2f + side * 2.6f + Vector3.up * 2.4f;
            }
            if (shots != null && after < 0f && shot == 0 && (what == null || Vector3.Distance(rider.transform.position, what.transform.position) < 4f))
            {
                // (the warm-up renders, and one just before the hit, from the same place)
                var p = rider.transform.position; var fwd = rider.Body.linearVelocity; fwd.y = 0f; fwd.Normalize();
                var side = Vector3.Cross(Vector3.up, fwd);
                look = p + fwd * 3f + Vector3.up * 0.4f; eye = p - fwd * 2.5f + side * 2.6f + Vector3.up * 2.4f;
                for (int w = 0; w < 3; w++) Shoot(shots, "tube_bounce_before.png", eye, look);
                shot = -1;
            }
            if (shots != null && after >= 0f && shot >= -1 && shot < 8 && t >= after + Mathf.Max(0, shot) * 0.07f) { if (shot < 0) shot = 0; Shoot(shots, $"tube_bounce_{shot}.png", eye, look); shot++; }
            if (after >= 0f)
            {
                b.squash = Mathf.Max(b.squash, -body.TubeSquashNow);
                b.hop = Mathf.Max(b.hop, body.TubeHopNow);
                b.tip = Mathf.Max(b.tip, Mathf.Abs(body.TubeTipNow));
                b.jolt = Mathf.Max(b.jolt, body.TubeJoltNow.magnitude);
                b.spin = Mathf.Max(b.spin, Mathf.Abs(TubeSpin() - spin0));
                if (t > after + 1.5f) break;
            }
        }
        b.hits = tubeHits - h0; b.into = tubeHitInto; b.down = rider.Crashed; b.crash = string.Join("; ", crashes);
        if (what != null) Gone(what);
        return b;
    }

    static float TubeSpin() => (float)typeof(RiderBody).GetField("tubeSpin", NP).GetValue(body);
    static string Show(Bounce b) => $"{b.hits} bounce(s) at {b.into:0.0} m/s: flattened {b.squash * 100f:0}%, hopped {b.hop * 100f:0} cm, tipped {b.tip:0} deg, rider thrown {b.jolt * 100f:0} cm, spun {b.spin * Mathf.Rad2Deg:0} deg{(b.down ? ", DOWN " + b.crash : "")}";

    static void TubeBounces()
    {
        // its sound: the rubber ball's bounces, loaded
        var fx = typeof(SummitGame).GetField("audioFx", NP)?.GetValue(game);
        var clips = fx != null ? fx.GetType().GetField("tubeBounces", NP)?.GetValue(fx) as AudioClip[] : null;
        Check("TUBE: its bounce has its own recordings (tube_bounce_1..5)", clips != null && clips.Length == 5, $"{(clips != null ? clips.Length : -1)} clips");
        // into a tree, hard (but slower than throws you)
        NewRun(RideKind.Snowboard, true); UpTo(12f);
        var shots = System.Environment.GetEnvironmentVariable("RIDECRASH_SHOTS");
        if (!string.IsNullOrEmpty(shots)) System.IO.Directory.CreateDirectory(shots); else shots = null;
        var hard = TubeInto(Put("Tree", 12f), 2f, shots);
        Check("TUBE: bounces off a tree, and stays on", hard.hits >= 1 && !hard.down, Show(hard));
        Check("TUBE: and shows it: the ring flattens, hops, tips, spins away, the rider is thrown in the seat",
              hard.squash > 0.15f && hard.hop > 0.15f && hard.tip > 10f && hard.spin > 0.5f && hard.jolt > 0.05f, Show(hard));
        // settled again a moment later
        for (int i = 0; i < 150 && !rider.Crashed; i++) Step(Pilot());
        Check("TUBE: and settles again (the ring round, on the snow, upright, the rider in the seat)",
              Mathf.Abs(body.TubeSquashNow) < 0.02f && body.TubeHopNow < 0.005f && Mathf.Abs(body.TubeTipNow) < 1f && body.TubeJoltNow.magnitude < 0.01f,
              $"give {body.TubeSquashNow:0.000}, hop {body.TubeHopNow:0.000} m, tip {body.TubeTipNow:0.0} deg, jolt {body.TubeJoltNow.magnitude:0.000} m");
        float hardK = body.TubeBounceK;
        // a gentle bump: square on, slow
        NewRun(RideKind.Snowboard, true, 320f, 3f);
        var soft = TubeInto(Put("Rock", 1.6f));
        Check("TUBE: a slow bump is a small bounce (less of everything than a hard one)",
              soft.hits >= 1 && !soft.down && soft.squash < hard.squash && soft.tip < hard.tip && body.TubeBounceK < hardK, $"slow: {Show(soft)}; hard k {hardK:0.00}, slow k {body.TubeBounceK:0.00}");
        // a rock at speed
        NewRun(RideKind.Snowboard, true); UpTo(8f);
        var rock = TubeInto(Put("Rock", 7f));
        Check("TUBE: bounces off a rock, and stays on", rock.hits >= 1 && !rock.down && rock.squash > 0.05f, Show(rock));
        // a person: they go down, the tube bounces off them (no warning used, no fall)
        if (mr != null)
        {
            // (slow enough that a rubber ring takes it: TestOnly leaves them the only one on the mountain)
            NewRun(RideKind.Snowboard, true, 320f, 4f); UpTo(9f);
            for (int i = 0; i < 30 && rider.Airborne; i++) Step(Pilot());   // (on the snow: a tube in the air rides over anyone)
            int k0 = MountainRiders.Knocked;
            float sp = rider.Speed;
            Person(5f, false);
            var person = TubeInto(null);
            Check("TUBE: bounces off a person (they go down), and stays on", person.hits >= 1 && !person.down && MountainRiders.Knocked == k0 + 1 && !rider.FreeHitUsed && person.squash > 0.05f, $"{Show(person)}, {sp:0.0} m/s going in, knocked {MountainRiders.Knocked - k0}, free hit used {rider.FreeHitUsed}");
        }
        // a fence (a breakable: ridden through, a bump)
        NewRun(RideKind.Snowboard, true); UpTo(9f);
        int f0 = tubeHits;
        bool fell = rider.Breakthrough(0.85f, 0.3f, 7f, 99f, "Fence");
        for (int i = 0; i < 20; i++) Step(0f);
        Check("TUBE: bumps through a fence, and shows it", tubeHits > f0 && !fell && !rider.Crashed && Mathf.Abs(body.TubeTipNow) + body.TubeHopNow > 0f, $"bounces {tubeHits - f0}, give {body.TubeSquashNow:0.000}");
    }

    static void Shoot(string dir, string file, Vector3 eye, Vector3 look)
    {
        terrain.Prewarm(); props.Prewarm();
        int w = 900, h = 700;
        var rt = new RenderTexture(w, h, 24);
        var keep = cam.transform.position; var keepR = cam.transform.rotation;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr2 = g.GetComponent<MeshRenderer>(); mr2.sharedMaterials = sk.sharedMaterials;
            var mpb = new MaterialPropertyBlock(); sk.GetPropertyBlock(mpb); mr2.SetPropertyBlock(mpb);
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            if (sk.bones.Length == 0) g.transform.localScale = sk.transform.lossyScale;
            sk.enabled = false; off.Add(sk); baked.Add(g);
        }
        cam.Render();
        foreach (var g in baked) { Object.DestroyImmediate(g.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(g); }
        foreach (var sk in off) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        cam.transform.SetPositionAndRotation(keep, keepR);
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
