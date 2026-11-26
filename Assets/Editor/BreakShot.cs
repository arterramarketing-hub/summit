using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders and measures signs and flags being ridden into: a ride down the line of flags along the start lip,
/// a ride into a start warning sign, a section sign at speed, a harder hit (which should knock you down), the
/// same while invincible (which should not), and a thrown body tumbling through the flags.
/// Run: Unity -batchmode -projectPath . -executeMethod BreakShot.Run -logFile break.log
/// (no -nographics: it needs the GPU). Writes BreakShot*.png in the project folder; log lines start BREAK.
/// </summary>
public static class BreakShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static Camera cam; static ChaseCamera chase; static MethodInfo late; static Atmosphere atmo;
    static Smasher smasher; static PowerAura aura; static Breakables breaks;
    static int step, broke;
    static string scene = "";

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        rider = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        smasher = F<Smasher>("smasher"); aura = F<PowerAura>("aura"); breaks = F<Breakables>("breaks");
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 4 * Dt;
        rider.Crash += why => Debug.Log($"BREAK {scene}: CRASH {why} at {rider.Speed * 3.6f:0} km/h");
        rider.ShieldHit += () => Debug.Log($"BREAK {scene}: shield took it");
        breaks.Broke += (b, crashed, invincible, thrown) =>
        {
            broke++;
            Debug.Log($"BREAK {scene}: {b.kind} {b.name} down at z={b.transform.position.z:0.0} rider {rider.Speed * 3.6f:0} km/h crashed={crashed} invincible={invincible} thrown={thrown}");
        };
        try
        {
            Flags();
            WarningSign();
            var signs = SectionSigns();
            Debug.Log($"BREAK found {signs.Count} section signs");
            if (signs.Count > 0) SectionSign(signs[0], 17f, "Section", true);
            if (signs.Count > 0) SectionSign(signs[0], 30f, "Hard", false);
            if (signs.Count > 0) SectionSign(signs[0], 30f, "Invincible", false, true);   // (the first again: it is put back for each ride)
            Thrown();
            // and everything back as it was for a new run
            int down = 0, all = 0;
            Breakable.RestoreAll();
            foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None)) { all++; if (b.Down) down++; }
            Debug.Log($"BREAK after RestoreAll: {down} of {all} still down");
        }
        catch (System.Exception e) { Debug.LogError("BREAK exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    static void Start(float x, float z, float v)
    {
        Breakable.RestoreAll(); smasher.Clear(); aura.Clear();
        rider.ResetAt(x, z); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * v;
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); chase.Snap();
        step = 0; broke = 0;
    }

    static float Toward(float x) => Mathf.Clamp((x - rider.transform.position.x) * 0.8f - rider.Heading * 1.5f, -1f, 1f);

    // ---- down the line of flags along the start lip

    static List<Breakable> LipFlags(float side)
    {
        var l = new List<Breakable>();
        float cx = Slope.Centre(Slope.PadLip);
        foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
            if (b.name == "LipFlag" && Mathf.Sign(b.transform.position.x - cx) == side) l.Add(b);
        return l;
    }

    static void Flags()
    {
        scene = "Flags";
        float cx = Slope.Centre(Slope.PadLip), x = cx + Slope.PadHalf - 1f;
        Start(x, Slope.PadLip - 42f, 12f);
        var line = LipFlags(1f);
        float vIn = rider.Speed;
        bool s0 = false, s1 = false;
        for (float t = 0f; t < 6f && rider.Z < Slope.PadLip - 1f; t += Dt)
        {
            rider.Steer = Toward(x);
            Step();
            if (!s0 && broke >= 3) { s0 = true; Settle(0.2f, x); Frame("BreakShotFlags0.png"); }
            if (!s1 && broke >= 5) { s1 = true; Settle(0.3f, x); Frame("BreakShotFlags1.png"); }
            if (rider.Crashed) break;
        }
        int down = 0; foreach (var b in line) if (b.Down) down++;
        Debug.Log($"BREAK Flags: {down} of {line.Count} knocked down; speed {vIn * 3.6f:0} -> {rider.Speed * 3.6f:0} km/h crashed={rider.Crashed}");
        for (int i = 0; i < 240; i++) Breakable.Step(Dt);
        // from inside the pad, looking across at the line of flags lying in the snow
        float y = Slope.DrawnHeight(cx, Slope.PadLip - 20f);
        chase.enabled = false; cam.fieldOfView = 55f;
        // (from up the line, a little inside it, clear of the warning signs)
        Look(new Vector3(x - 3.5f, Slope.DrawnHeight(x - 3.5f, Slope.PadLip - 40f) + 2.2f, Slope.PadLip - 40f), new Vector3(x, y + 0.2f, Slope.PadLip - 16f));
        DrawAll(); ShootBaked("BreakShotFlagsSide.png");
        chase.enabled = true;
    }

    // keep riding a moment (so the frame shows them on their way down)
    static void Settle(float secs, float x) { for (float t = 0f; t < secs; t += Dt) { rider.Steer = Toward(x); Step(); } }

    // ---- into a start warning sign

    static void WarningSign()
    {
        scene = "Warning";
        float cx = Slope.Centre(Slope.PadLip);
        Breakable sign = null;
        foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
            if (b.name == "WarningSign" && b.transform.position.x < cx && b.transform.position.z < Slope.PadLip - 16f) sign = b;
        if (sign == null) { Debug.Log("BREAK no warning sign found"); return; }
        Vector3 post = sign.transform.TransformPoint(new Vector3(1.0f, 0f, 0f));   // just inside the inner post
        Ride(sign, post.x, sign.transform.position.z - 20f, 13f, "BreakShotSign", true, false);
    }

    // ---- section signs on the course

    static List<Breakable> SectionSigns()
    {
        var l = new List<Breakable>();
        // (they are built a cycle ahead of the rider: stand at the top of the course to see the first ones)
        rider.ResetAt(Slope.Centre(Course.First), Course.First); dress?.Prewarm();
        foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
            if (b.name == "SectionSign") l.Add(b);
        l.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));
        return l;
    }

    static void SectionSign(Breakable sign, float v, string tag, bool shots, bool invincible = false)
    {
        scene = tag;
        Vector3 c = sign.transform.position;
        float side = Mathf.Sign(c.x - Slope.Centre(c.z));
        float x = c.x - side * 2.6f;   // the inner post
        Ride(sign, x, c.z - (v > 20f ? 8f : 16f), v, shots ? "BreakShot" + tag : null, false, invincible);   // (fast: start close, or a little drift misses the post)
    }

    static void Ride(Breakable sign, float x, float z, float v, string shots, bool side, bool invincible)
    {
        Start(x, z, v);
        if (invincible) rider.AddInvincible(8f);
        float tHit = -1f, vIn = 0f, into = 0f;
        bool s0 = false, s1 = false, s2 = false;
        for (float t = 0f; t < 5f; t += Dt)
        {
            rider.Steer = Toward(x);
            float before = rider.Speed;
            var vel = rider.Velocity;
            Step();
            if (tHit < 0f && sign.Down) { tHit = 0f; vIn = before; Vector3 sp = sign.transform.position; into = Mathf.Abs(Vector3.Dot(vel, Vector3.ProjectOnPlane(sign.transform.forward, Slope.Normal(sp.x, sp.z)).normalized)); }
            if (shots != null && !s0 && tHit < 0f && sign.transform.position.z - rider.Z < 5f) { s0 = true; Frame(shots + "0.png"); }
            if (tHit >= 0f)
            {
                tHit += Dt;
                if (shots != null && !s1 && tHit > 0.12f) { s1 = true; Frame(shots + "1.png"); }
                if (shots != null && !s2 && tHit > 0.45f) { s2 = true; Frame(shots + "2.png"); }
                if (tHit > 0.6f && vIn > 0f) { Debug.Log($"BREAK {scene}: in {vIn * 3.6f:0} km/h ({into:0.0} m/s into its face), 0.6 s later {rider.Speed * 3.6f:0} km/h, crashed={rider.Crashed}"); vIn = -1f; }
                if (tHit > 2.5f) break;
            }
        }
        if (tHit < 0f) { Debug.Log($"BREAK {scene}: never hit it (rider at {rider.transform.position}, sign at {sign.transform.position})"); return; }
        for (int i = 0; i < 360; i++) Breakable.Step(Dt);
        int moving = Breakable.MovingCount;
        Debug.Log($"BREAK {scene}: {moving} pieces still moving 3 s on");
        if (shots == null) return;
        // from the side of the piste, looking at where it broke and where the pieces went
        Vector3 c = sign.transform.position;
        float toPiste = Mathf.Sign(Slope.Centre(c.z) - c.x + 0.01f);
        chase.enabled = false; cam.fieldOfView = 55f;
        // from the piste below it, looking back up at where it stood and where its pieces went (the pieces fly
        // down the hill; a camera above it on a steep pitch would be inside the snow)
        Vector3 from = c + new Vector3(toPiste * 6f, 0f, 9f);
        from.y = Slope.DrawnHeight(from.x, from.z) + 2.2f;
        Look(from, c + new Vector3(0f, 0.6f, 3f));
        DrawAll(); ShootBaked(shots + "Side.png");
        chase.enabled = true;
    }

    // ---- a thrown body tumbling through the flags

    static void Thrown()
    {
        scene = "Thrown";
        float cx = Slope.Centre(Slope.PadLip), x = cx - (Slope.PadHalf - 1f);
        Start(x, Slope.PadLip - 40f, 14f);
        var line = LipFlags(-1f);
        bool shot = false; float tCrash = -1f;
        for (float t = 0f; t < 7f; t += Dt)
        {
            rider.Steer = rider.Crashed ? 0f : Toward(x);
            Step();
            if (!rider.Crashed && rider.Z > Slope.PadLip - 33f) { rider.DoCrash("test"); tCrash = 0f; }
            if (tCrash >= 0f) tCrash += Dt;
            if (!shot && tCrash > 0.7f) { shot = true; Frame("BreakShotThrown.png"); }
        }
        int down = 0; foreach (var b in line) if (b.Down) down++;
        Debug.Log($"BREAK Thrown: the body knocked down {down} of {line.Count} flags, came to rest at {body.RagdollCentre}");
    }

    // ---- the stepping, the way the game runs it

    static void Step()
    {
        rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        smasher.Step(Dt);
        Breakable.Step(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); }
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

    static void Look(Vector3 from, Vector3 at) => cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));

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
        Debug.Log("BREAK wrote " + file);
    }
}
