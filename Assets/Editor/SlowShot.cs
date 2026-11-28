using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The SLOW banners (SlowZones) and the signs you now stay up through (Breakables):
///  - lists every slow zone on the mountain and where its banners went
///  - renders a zone from the rider's view, a banner torn through (mid-tear and after), and a sign hit with the
///    rider staying up, by day and by night
///  - rides into banners and signs at several speeds and angles, and logs each one: hit or not, speed in and
///    out, crashed (there should be none), falls, and what the hit put on the medical bill
/// Run: Unity -batchmode -projectPath . -executeMethod SlowShot.Run -logFile slow.log
/// (no -nographics: it needs the GPU). Writes SlowShot*.png in the project folder; log lines start SLOW, and a
/// summary line SLOW-SUMMARY with the count of crashes.
/// </summary>
public static class SlowShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static Camera cam; static ChaseCamera chase; static MethodInfo late; static Atmosphere atmo;
    static Smasher smasher; static PowerAura aura; static Breakables breaks; static DayNight dayNight;
    static int step, crashes, rides;
    static bool inv;   // the next ride is invincible
    static float billed; static string hitWhat = "";
    static string scene = "";

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static int Falls => game.falls;
    static float Medical => game.medical;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        rider = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        smasher = F<Smasher>("smasher"); aura = F<PowerAura>("aura"); breaks = F<Breakables>("breaks"); dayNight = F<DayNight>("dayNight");
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        int seed = 3;
        var env = System.Environment.GetEnvironmentVariable("SUMMIT_SEED");
        if (!string.IsNullOrEmpty(env) && int.TryParse(env, out int es)) seed = es;
        TestBoot.NewMountain(game, seed, terrain, props);
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 4 * Dt;
        rider.Crash += why => { crashes++; Debug.Log($"SLOW {scene}: CRASH {why} at {rider.Speed * 3.6f:0} km/h"); };
        breaks.RiderHit += (kind, speed, at, part, cost) =>
        {
            billed += cost; hitWhat = part;
            Debug.Log($"SLOW {scene}: RiderHit({kind}, {speed:0.0} m/s = {speed * 3.6f:0} km/h, at {at}, \"{part}\", ${cost:0})");
        };
        try
        {
            Zones();
            var banner = FirstBanner(out var zoneZ);
            if (banner != null)
            {
                Day();
                ZoneView(banner, zoneZ);
                Tear(banner, "Tear", 15f, 0f, 0f, true);
                foreach (float v in new[] { 8f, 15f, 22f, 30f })
                    foreach (float ang in new[] { 0f, 25f, -25f })
                        Tear(banner, $"Banner v{v} a{ang}", v, ang, 0f, false);
                // straight at a stake (a bruise, above StakeHurts) and invincible
                Tear(banner, "Banner stake v22", 22f, 0f, SlowZones.W * 0.5f, false);
                Tear(banner, "Banner stake v12", 12f, 0f, SlowZones.W * 0.5f, false);
                inv = true; Tear(banner, "Banner invincible v22", 22f, 0f, 0f, false); inv = false;
            }
            var sign = FirstSign();
            if (sign != null)
            {
                Day(); SignHit(sign, "SignDay", 20f, 0f, true);
                Night(); SignHit(sign, "SignNight", 20f, 0f, true);
                Day();
                foreach (float v in new[] { 10f, 17f, 24f, 30f, 36f })
                    foreach (float ang in new[] { 0f, 20f, -20f })
                        SignHit(sign, $"Sign v{v} a{ang}", v, ang, false);
                inv = true; SignHit(sign, "Sign invincible v30", 30f, 0f, false); inv = false;
                // the board itself, jumped into: start a little in the air, at the board's height
                SignBoard(sign, 24f);
            }
            // (the signs' rides built the course round them again: find the banner afresh)
            banner = FirstBanner(out zoneZ);
            if (banner != null) { Night(); ZoneView(banner, zoneZ, "Night"); Tear(banner, "TearNight", 15f, 0f, 0f, true); Day(); }
            Breakable.RestoreAll();
            int down = 0, all = 0;
            foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None)) if (b.kind == Breakable.Kind.Banner) { all++; if (b.Down) down++; }
            Debug.Log($"SLOW after RestoreAll: {down} of {all} banners built still down");
            Debug.Log($"SLOW-SUMMARY {rides} rides, {crashes} crash(es), ${billed:0} billed in all");
        }
        catch (System.Exception e) { Debug.LogError("SLOW exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Day() { TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow(); }
    static void Night() { TimeOfDay.TestClock = 3.4f; dayNight.ApplyNow(); }

    // ---- where they are

    static void Zones()
    {
        int n = 0;
        for (int k = 0; k <= Course.CycleOf(Slope.FinishZ); k++)
            foreach (var z in SlowZones.Of(k))
            {
                n++;
                Debug.Log($"SLOW zone cycle {k} z={z.z:0} ({z.why}): left {(SlowZones.Room(z.z, -1f) ? "built" : "no room")}, right {(SlowZones.Room(z.z, 1f) ? "built" : "no room")}");
            }
        Debug.Log($"SLOW {n} slow zones on this mountain (finish at z={Slope.FinishZ:0})");
    }

    // a banner of the first zone below the start (a hamlet's or a lift's, if there is one: they are out on the mountain)
    static Breakable FirstBanner(out float zoneZ)
    {
        zoneZ = 0f;
        float want = -1f;
        for (int k = 1; k <= Course.CycleOf(Slope.FinishZ) && want < 0f; k++)
            foreach (var z in SlowZones.Of(k)) if (z.why != "finish") { want = z.z; break; }
        if (want < 0f) foreach (var z in SlowZones.Of(0)) { want = z.z; break; }
        if (want < 0f) { Debug.Log("SLOW no slow zone on this mountain"); return null; }
        zoneZ = want;
        rider.ResetAt(Slope.Centre(want), want - 30f);
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        Breakable best = null;
        foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
            if (b.kind == Breakable.Kind.Banner && Mathf.Abs(b.transform.position.z - want) < 10f && (best == null || b.transform.position.x > best.transform.position.x)) best = b;
        Debug.Log(best != null ? $"SLOW riding into {best.name} at {best.transform.position}" : $"SLOW no banner built at z={want:0}");
        return best;
    }

    static Breakable FirstSign()
    {
        rider.ResetAt(Slope.Centre(Course.First), Course.First);
        dress.Rebuild(); terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        var signs = new List<Breakable>();
        foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None)) if (b.name == "SectionSign") signs.Add(b);
        signs.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));
        if (signs.Count == 0) { Debug.Log("SLOW no section sign"); return null; }
        Debug.Log($"SLOW riding into the section sign at {signs[0].transform.position}");
        return signs[0];
    }

    // ---- the rides

    static void Start(Vector3 at, Vector3 toward, float v)
    {
        Breakable.RestoreAll(); smasher.Clear(); aura.Clear();
        rider.ResetAt(at.x, at.z); body.ResetVisual(); rider.Begin();
        if (inv) rider.AddInvincible(30f);
        Vector3 d = toward - at; d.y = 0f; d.Normalize();
        rider.Heading = Mathf.Atan2(d.x, d.z);
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(d, Slope.Normal(at.x, at.z)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); chase.Snap();
        step = 0; hitWhat = "";
    }

    static float Toward(Vector3 target)
    {
        Vector3 p = rider.transform.position;
        float want = Mathf.Atan2(target.x - p.x, target.z - p.z);
        return Mathf.Clamp((Mathf.DeltaAngle(rider.Heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad) * 3f - rider.YawRate * 0.2f, -1f, 1f);
    }

    // from `back` metres up the hill of target, coming in at `angle` degrees off straight down the run
    static Vector3 From(Vector3 target, float back, float angle)
    {
        Vector3 d = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        Vector3 p = target - d * back;
        p.y = Slope.DrawnHeight(p.x, p.z);
        return p;
    }

    /// <summary>Ride into a banner (aim: metres along it from its middle), log it, and optionally render it.</summary>
    static void Tear(Breakable banner, string tag, float v, float angle, float aim, bool shots)
    {
        scene = tag; rides++;
        Vector3 c = banner.transform.position + banner.transform.right * aim + Vector3.up * 0.2f;
        Start(From(c, Mathf.Max(10f, v * 0.9f), angle), c, v);
        int falls0 = Falls; float bill0 = billed, med0 = Medical;
        float tHit = -1f, vIn = 0f;
        bool s1 = false, s2 = false;
        for (float t = 0f; t < 4f; t += Dt)
        {
            float before = rider.Speed;
            rider.Steer = tHit < 0f ? Toward(c) : 0f;
            Step();
            if (tHit < 0f && banner.Down) { tHit = 0f; vIn = before; }
            if (shots && tHit < 0f && !s1 && (c - rider.transform.position).magnitude < 3.5f) { s1 = true; Frame($"SlowShot{tag}0.png"); }
            if (tHit >= 0f)
            {
                tHit += Dt;
                if (shots && !s2 && tHit > 0.12f) { s2 = true; Frame($"SlowShot{tag}1.png"); }
                if (shots && tHit > 0.45f && tHit < 0.45f + Dt * 1.5f) Frame($"SlowShot{tag}2.png");
                if (tHit > 0.6f) break;
            }
            if (rider.Crashed) break;
        }
        if (tHit < 0f) { Debug.Log($"SLOW {tag}: missed it (rider at {rider.transform.position}, aim {c}), crashed={rider.Crashed}"); return; }
        Debug.Log($"SLOW {tag}: hit at {vIn * 3.6f:0} km/h, 0.6 s later {rider.Speed * 3.6f:0} km/h ({rider.Speed / Mathf.Max(0.1f, vIn) * 100f:0}%), crashed={rider.Crashed}, falls {falls0} -> {Falls}, billed ${billed - bill0:0} {(hitWhat != "" ? "(" + hitWhat + ")" : "")} (medical {med0:0} -> {Medical:0})");
        for (int i = 0; i < 360; i++) Breakable.Step(Dt);
        if (shots) Still($"SlowShot{tag}After.png", banner);
    }

    static void SignHit(Breakable sign, string tag, float v, float angle, bool shots)
    {
        scene = tag; rides++;
        Vector3 s = sign.transform.position;
        float x = s.x - Mathf.Sign(s.x - Slope.Centre(s.z)) * 2.7f;   // its inner post
        Vector3 c = new Vector3(x, Slope.DrawnHeight(x, s.z), s.z);
        Start(From(c, Mathf.Max(9f, v * 0.5f), angle), c, v);
        int falls0 = Falls; float bill0 = billed, med0 = Medical;
        float tHit = -1f, vIn = 0f; Vector3 pIn = Vector3.zero;
        bool s0 = false, s1 = false;
        for (float t = 0f; t < 4f; t += Dt)
        {
            float before = rider.Speed;
            rider.Steer = tHit < 0f ? Toward(c) : 0f;
            Step();
            if (tHit < 0f && sign.Down) { tHit = 0f; vIn = before; pIn = rider.transform.position; }
            if (shots && !s0 && tHit < 0f && (c - rider.transform.position).magnitude < 4f) { s0 = true; Frame($"SlowShot{tag}0.png"); }
            if (tHit >= 0f)
            {
                tHit += Dt;
                if (shots && !s1 && tHit > 0.15f) { s1 = true; Frame($"SlowShot{tag}1.png"); }
                if (shots && tHit > 0.9f && tHit < 0.9f + Dt * 1.5f) Frame($"SlowShot{tag}2.png");
                if (tHit > 1.0f) break;
            }
            if (rider.Crashed) break;
        }
        if (tHit < 0f) { Debug.Log($"SLOW {tag}: missed it (rider at {rider.transform.position}, aim {c}), crashed={rider.Crashed}"); return; }
        Vector3 off = rider.transform.position - pIn; off.y = 0f;
        Debug.Log($"SLOW {tag}: hit at {vIn * 3.6f:0} km/h, 1 s later {rider.Speed * 3.6f:0} km/h ({rider.Speed / Mathf.Max(0.1f, vIn) * 100f:0}%), crashed={rider.Crashed}, falls {falls0} -> {Falls}, billed ${billed - bill0:0} {(hitWhat != "" ? "(" + hitWhat + ")" : "")} (medical {med0:0} -> {Medical:0}), {off.magnitude:0.0} m on");
        for (int i = 0; i < 240; i++) Breakable.Step(Dt);
        if (shots) Still($"SlowShot{tag}After.png", sign);
    }

    // into the board itself: launched at its height, as off a bump in front of it
    static void SignBoard(Breakable sign, float v)
    {
        scene = "Sign board"; rides++;
        Vector3 s = sign.transform.position;
        Vector3 c = new Vector3(s.x, s.y, s.z);
        Start(From(c, 6f, 0f), c, v);
        rider.Body.position += Vector3.up * 1.4f;
        rider.Body.linearVelocity += Vector3.up * 3f;
        int falls0 = Falls; float bill0 = billed;
        for (float t = 0f; t < 2f; t += Dt)
        {
            rider.Steer = 0f;
            Step();
            if (sign.Down && t > 0.6f) break;
            if (rider.Crashed) break;
        }
        Debug.Log($"SLOW Sign board: down={sign.Down}, crashed={rider.Crashed}, falls {falls0} -> {Falls}, billed ${billed - bill0:0} ({hitWhat})");
    }

    static void ZoneView(Breakable banner, float zoneZ, string tag = "")
    {
        scene = "View" + tag;
        Vector3 c = new Vector3(Slope.Centre(zoneZ), 0f, zoneZ);
        Start(From(c, 26f, 0f), c, 9f);
        for (float t = 0f; t < 3f; t += Dt)
        {
            rider.Steer = Toward(c + Vector3.forward * 20f);
            Step();
            if (rider.Z > zoneZ - 11f) break;
        }
        Frame($"SlowShotZone{tag}.png");
    }

    // ---- the stepping, the way the game runs it

    static void Step()
    {
        TestHarness.Step(rider, body, Dt);
        smasher.Step(Dt);
        Breakable.Step(Dt);
        game.TestPoints(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
        if (step % 4 == 0)
        {
            body.Apply(4 * Dt);
            aura.Step(step * Dt);
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
        smasher.Draw();
    }

    static void Frame(string file)
    {
        body.Apply(Dt);
        late.Invoke(chase, null);
        DrawAll();
        ShootBaked(file);
    }

    // what broke, from the piste a few metres up the hill and to the side: where the rider is now, and the pieces
    static void Still(string file, Breakable b)
    {
        Vector3 c = b.transform.position;
        float side = Mathf.Sign(c.x - Slope.Centre(c.z));
        Vector3 from = new Vector3(c.x - side * 5f, 0f, c.z - 8f);
        from.y = Slope.DrawnHeight(from.x, from.z) + 2.2f;
        Vector3 look = Vector3.Lerp(c, rider.transform.position, 0.35f) + Vector3.up * 0.5f;
        chase.enabled = false; cam.fieldOfView = 60f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
        body.Apply(Dt);
        DrawAll(); ShootBaked(file);
        chase.enabled = true;
    }

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
        Debug.Log("SLOW wrote " + file);
    }
}
