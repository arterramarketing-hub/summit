using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders and measures the hamlets' fence giving way: the rider riding through a span (before, mid-break, after),
/// the thrown body tumbling through one, and an invincible pass; then checks every span is back after a reset.
/// Run: Unity -batchmode -projectPath . -executeMethod FenceShot.Run -logFile fence.log
/// (no -nographics: it needs the GPU). Writes FenceShot*.png in the project folder; log lines start FENCE.
/// </summary>
public static class FenceShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static Camera cam; static ChaseCamera chase; static MethodInfo late, hamUpdate; static Atmosphere atmo;
    static Smasher smasher; static PowerAura aura; static Breakables breaks; static Hamlets hamlets;
    static int step, broke;
    static string scene = "";

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static int Falls => (int)typeof(SummitGame).GetField("falls", NP).GetValue(game);

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
        hamlets = Object.FindFirstObjectByType<Hamlets>(); hamUpdate = typeof(Hamlets).GetMethod("Update", NP);
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        ChaseCamera.TestDt = 4 * Dt;
        rider.Crash += why => Debug.Log($"FENCE {scene}: CRASH {why} at {rider.Speed * 3.6f:0} km/h");
        breaks.Broke += (b, crashed, invincible, thrown) =>
        {
            broke++;
            Debug.Log($"FENCE {scene}: {b.kind} {b.name} down at {b.transform.position} rider {rider.Speed * 3.6f:0} km/h crashed={crashed} invincible={invincible} thrown={thrown}");
        };
        try
        {
            var spans = Spans();
            Debug.Log($"FENCE found {spans.Count} fence spans");
            if (spans.Count >= 9)
            {
                RideThrough(spans[spans.Count / 3], "Rider", false);
                Thrown(spans[spans.Count / 3]);
                RideThrough(spans[spans.Count / 3 + 4], "Invincible", true);
            }
            Breakable.RestoreAll();
            int down = 0, all = 0;
            foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None)) if (b.kind == Breakable.Kind.Fence) { all++; if (b.Down) down++; }
            var wood = GameObject.Find("FenceWood");
            Debug.Log($"FENCE after RestoreAll: {down} of {all} spans still down; wood mesh bounds {(wood != null ? wood.GetComponent<MeshFilter>().sharedMesh.bounds.size.ToString() : "none")}");
        }
        catch (System.Exception e) { Debug.LogError("FENCE exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    /// <summary>The first hamlet's fence, built (its pieces go down one a frame), its spans in order down the hill.</summary>
    static List<Breakable> Spans()
    {
        Hamlets.Hamlet h = null;
        for (int k = 1; k <= Hamlets.LastCycle && h == null; k++) h = Hamlets.Of(k);
        var l = new List<Breakable>();
        if (h == null) { Debug.Log("FENCE no hamlet on this mountain"); return l; }
        float z = (h.fenceZ0 + h.fenceZ1) * 0.5f;
        rider.ResetAt(Slope.Centre(z), z);
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm();
        for (int i = 0; i < 60; i++) hamUpdate.Invoke(hamlets, null);
        foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
            if (b.kind == Breakable.Kind.Fence) l.Add(b);
        l.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));
        Debug.Log($"FENCE hamlet in cycle {h.cycle}, side {h.side}, fence z {h.fenceZ0:0}..{h.fenceZ1:0}, gap at {h.gapZ:0}");
        return l;
    }

    static void Start(Vector3 at, Vector3 toward, float v)
    {
        Breakable.RestoreAll(); smasher.Clear(); aura.Clear();
        rider.ResetAt(at.x, at.z); body.ResetVisual(); rider.Begin();
        Vector3 d = toward - at; d.y = 0f; d.Normalize();
        rider.Heading = Mathf.Atan2(d.x, d.z);
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(d, Slope.Normal(at.x, at.z)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); hamUpdate.Invoke(hamlets, null); chase.Snap();
        step = 0; broke = 0;
    }

    static float Toward(Vector3 target)
    {
        Vector3 p = rider.transform.position;
        float want = Mathf.Atan2(target.x - p.x, target.z - p.z);
        return Mathf.Clamp((Mathf.DeltaAngle(rider.Heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad) * 3f - rider.YawRate * 0.2f, -1f, 1f);
    }

    // a point on the piste beside the span, to come at it from (and to look at it from)
    static Vector3 Approach(Breakable span, float back, float aside)
    {
        Vector3 c = span.transform.position;
        float side = Mathf.Sign(c.x - Slope.Centre(c.z));
        float x = c.x - side * aside, z = c.z - back;
        return new Vector3(x, Slope.DrawnHeight(x, z), z);
    }

    // ---- the rider through a span (and invincible)

    static void RideThrough(Breakable span, string tag, bool invincible)
    {
        scene = tag;
        Vector3 c = span.transform.position, from = Approach(span, 16f, 7f);
        Start(from, c, 15f);
        if (invincible) rider.AddInvincible(8f);
        int falls0 = Falls;
        if (!invincible) Still($"FenceShot{tag}Before.png", span);
        float tHit = -1f, vIn = 0f;
        bool s0 = false, s1 = false;
        for (float t = 0f; t < 4f; t += Dt)
        {
            float before = rider.Speed;
            rider.Steer = tHit < 0f ? Toward(c) : 0f;
            Step();
            if (tHit < 0f && span.Down) { tHit = 0f; vIn = before; }
            if (!s0 && tHit < 0f && (c - rider.transform.position).magnitude < 5f) { s0 = true; Frame($"FenceShot{tag}0.png"); }
            if (tHit >= 0f)
            {
                tHit += Dt;
                if (!s1 && tHit > 0.15f) { s1 = true; Frame($"FenceShot{tag}1.png"); }
                if (tHit > 0.6f) break;   // (not on into the cabins behind it)
            }
            if (rider.Crashed) break;
        }
        if (tHit < 0f) { Debug.Log($"FENCE {tag}: never reached the fence (rider at {rider.transform.position}, span at {c})"); return; }
        Debug.Log($"FENCE {tag}: in {vIn * 3.6f:0} km/h, 0.6 s later {rider.Speed * 3.6f:0} km/h ({rider.Speed / Mathf.Max(0.1f, vIn) * 100f:0}%), crashed={rider.Crashed}, falls {falls0} -> {Falls}");
        for (int i = 0; i < 300; i++) Breakable.Step(Dt);
        Debug.Log($"FENCE {tag}: {Breakable.MovingCount} pieces still moving 2.5 s on");
        if (!invincible) Still($"FenceShot{tag}After.png", span);
    }

    // ---- the thrown body through a span

    static void Thrown(Breakable span)
    {
        scene = "Thrown";
        Vector3 c = span.transform.position, from = Approach(span, 14f, 8f);
        Start(from, c, 16f);
        Still("FenceShotThrownBefore.png", span);
        float tCrash = -1f, tHit = -1f;
        for (float t = 0f; t < 6f; t += Dt)
        {
            rider.Steer = rider.Crashed ? 0f : Toward(c);
            Step();
            if (!rider.Crashed && (c - rider.transform.position).magnitude < 6f) { rider.DoCrash("test"); tCrash = 0f; }
            if (tCrash >= 0f) tCrash += Dt;
            if (tHit < 0f && span.Down) tHit = 0f;
            if (tHit >= 0f)
            {
                tHit += Dt;
                if (tHit > 0.3f && tHit < 0.3f + Dt * 1.5f) Frame("FenceShotThrown1.png");
            }
            if (tCrash > 4f) break;
        }
        Debug.Log($"FENCE Thrown: span down={span.Down}, the body came to rest at {body.RagdollCentre} (span at {c})");
        for (int i = 0; i < 300; i++) Breakable.Step(Dt);
        Still("FenceShotThrownAfter.png", span);
    }

    // ---- the stepping, the way the game runs it

    static void Step()
    {
        rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        smasher.Step(Dt);
        Breakable.Step(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); hamUpdate.Invoke(hamlets, null); }
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

    // the span from the piste beside it, a few metres up the hill: the same view before and after
    static void Still(string file, Breakable span)
    {
        Vector3 c = span.transform.position, from = Approach(span, 7f, 6f);
        from.y += 1.9f;
        chase.enabled = false; cam.fieldOfView = 55f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(c + Vector3.down * 0.2f - from));
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
        Debug.Log("FENCE wrote " + file);
    }
}
