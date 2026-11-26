using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders and measures the power-ups: the four pickups up close, a Super Jump (with an ordinary jump
/// beside it for comparison), and an invincible rider smashing through a tree and a rock, with the HUD.
/// Run: Unity -batchmode -projectPath . -executeMethod PowerShot.Run -logFile power.log
/// (no -nographics: it needs the GPU). Writes PowerShot*.png in the project folder; log lines start POWER.
/// </summary>
public static class PowerShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static Camera cam; static ChaseCamera chase; static MethodInfo late; static Atmosphere atmo;
    static Smasher smasher; static PowerAura aura; static Hud hud; static PowerUps power;
    static int step;

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        rider = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        smasher = F<Smasher>("smasher"); aura = F<PowerAura>("aura"); hud = F<Hud>("hud"); power = F<PowerUps>("power");
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);   // the game only reacts to smashes mid-run
        rider.input = null;
        ChaseCamera.TestDt = 4 * Dt;
        rider.Trick += (n, p) => Debug.Log($"POWER trick {n} +{p} air={rider.AirTime:0.00}");
        rider.RoughLanding += why => Debug.Log($"POWER rough {why}");
        rider.Landed += imp => Debug.Log($"POWER landed impact={imp:0.0} air={rider.AirTime:0.00} speed={rider.Speed * 3.6f:0} km/h");
        rider.Crash += why => Debug.Log($"POWER CRASH {why}");
        try
        {
            Pickups();
            Jump(false);
            Jump(true);
            Smash(Forest.Kind.Tree);
            Smash(Forest.Kind.Rock);
        }
        catch (System.Exception e) { Debug.LogError("POWER exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    // ---- the pickups, up close

    static void Pickups()
    {
        float z = Course.First + 110f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual();
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm();
        Vector3 basePt = new Vector3(Slope.Centre(z), 0f, z + 8f);
        basePt.y = Slope.DrawnHeight(basePt.x, basePt.z) + 1.25f;
        var make = typeof(PowerUps).GetMethod("Make", NP);
        var made = new List<GameObject>();
        var kinds = new[] { PowerUps.Kind.Soda, PowerUps.Kind.Shield, PowerUps.Kind.SuperJump, PowerUps.Kind.Invincible };
        for (int i = 0; i < kinds.Length; i++)
        {
            var go = (GameObject)make.Invoke(power, new object[] { kinds[i] });
            // a 2 x 2 block, so they fit a portrait frame
            float x = basePt.x + (i % 2 - 0.5f) * 1.5f;
            go.transform.position = new Vector3(x, basePt.y + (i < 2 ? 1.9f : 0f), basePt.z);
            made.Add(go);
        }
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name == "Halo") ps.Simulate(1.5f, false, true, false);
        chase.enabled = false;
        Vector3 can = made[0].transform.position;
        cam.fieldOfView = 32f;
        Look(can + new Vector3(0.25f, 0.35f, -2.1f), can);
        Shoot("PowerShotWarmup.png");   // the first render comes out unlit
        Shoot("PowerShotCan.png");
        cam.fieldOfView = 40f;
        Vector3 mid = basePt + Vector3.up * 0.95f;
        Look(mid + new Vector3(0f, 0.8f, -10f), mid);
        Shoot("PowerShotPickups.png");
        foreach (var g in made) Object.DestroyImmediate(g);
        chase.enabled = true;
    }

    // ---- an ordinary jump, then a super jump, from the same spot

    static void Jump(bool super)
    {
        float z = Course.First + 110f;
        string tag = super ? "Super" : "Plain";
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 18f;
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); chase.Snap();
        aura.Clear();
        step = 0;
        for (float t = 0f; t < 1f; t += Dt) { rider.Steer = Hold(); Step(); }
        if (super) rider.AddSuperJump(10f);
        rider.QueuePop(0.8f);
        float peak = 0f, t0 = -1f, abovePrev = 0f; bool shotPeak = false, shotLand = false, wasAir = false;
        for (float t = 0f; t < 6f; t += Dt)
        {
            rider.Steer = 0f;
            Step();
            if (rider.Airborne && t0 < 0f) { t0 = t; Debug.Log($"POWER {tag} takeoff v={rider.Velocity} trick={rider.TrickName} planned={rider.TrickTime:0.00}s"); }
            peak = Mathf.Max(peak, rider.AboveGround);
            float vy = rider.Velocity.y;
            // (the top of the flight above the snow, not of the flight itself: on a steep slope the rider is already
            // falling as they leave the snow, and the snow drops away faster still)
            if (super && rider.Airborne && !shotPeak && rider.AirTime > 0.3f && rider.AboveGround < abovePrev) { shotPeak = true; Frame("PowerShotJumpPeak.png"); Debug.Log($"POWER {tag} peak frame above={rider.AboveGround:0.0} m cam={cam.transform.position} rider={rider.transform.position}"); }
            if (super && rider.Airborne && shotPeak && !shotLand && vy < 0f && rider.AboveGround < 2.2f) { shotLand = true; Frame("PowerShotJumpLanding.png"); }
            abovePrev = rider.AboveGround;
            if (wasAir && !rider.Airborne) { Debug.Log($"POWER {tag} jump: air={t - t0:0.00}s peak={peak:0.0} m above the snow, crashed={rider.Crashed}"); break; }
            wasAir = rider.Airborne;
            if (rider.Crashed) break;
        }
        for (float t = 0f; t < 1.5f; t += Dt)
        {
            bool air = rider.Airborne;
            rider.Steer = Hold(); Step();
            if (rider.Airborne != air)
            {
                var p = rider.transform.position; var n = Slope.Normal(p.x, p.z);
                Debug.Log($"POWER {tag} after landing t={t:0.00} {(rider.Airborne ? "took off" : "landed")} n.y={n.y:0.00} v={rider.Velocity} trick={rider.TrickName} section={Course.At(p.z)} kicker={Slope.Kicker(p.x, p.z):0.00}");
            }
        }
        Debug.Log($"POWER {tag} after landing: speed={rider.Speed * 3.6f:0} km/h crashed={rider.Crashed} heading={rider.Heading:0.00}");
    }

    // ---- invincible: ride straight at a tree (or rock) on the piste

    static void Smash(Forest.Kind want)
    {
        Vector3 target = Vector3.zero; bool found = false;
        for (int cz = Mathf.FloorToInt((Course.First + 60f) / Forest.Cell); cz < 2400f / Forest.Cell && !found; cz++)
            for (int cx = -14; cx <= 14 && !found; cx++)
            {
                float wz = (cz + 0.5f) * Forest.Cell;
                int ccx = Mathf.FloorToInt(Slope.Centre(wz) / Forest.Cell) + cx;
                if (Forest.At(ccx, cz, out Vector3 p, out float size, out _) != want) continue;
                if (Mathf.Abs(p.x - Slope.Centre(p.z)) > Slope.PisteHalf - 3f || Course.At(p.z) == Course.Section.Pipe) continue;
                if (Course.CrevasseNear(p.x, p.z - 10f, 4f) || Mathf.Abs(Slope.Kicker(p.x, p.z - 10f)) > 0.01f) continue;
                // on its own, so the shots show it and not its neighbours
                bool alone = true;
                for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
                    if ((dx != 0 || dz != 0) && Forest.At(ccx + dx, cz + dz, out _, out _, out _) != Forest.Kind.None) alone = false;
                if (!alone) continue;
                target = p; found = true;
            }
        if (!found) { Debug.Log($"POWER no {want} found to smash"); return; }
        string tag = want == Forest.Kind.Tree ? "Tree" : "Rock";
        rider.ResetAt(target.x, target.z - 14f); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 15f;
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); chase.Snap(); aura.Clear(); smasher.Clear();
        rider.AddInvincible(8f);
        if (want == Forest.Kind.Tree) { rider.AddSuperJump(6.5f); rider.AddBoost(2f); }   // (so the HUD shot shows the timers stacking)
        float tHit = -1f, vIn = 0f;
        System.Action<Obstacle, Vector3, Vector3> onSmash = (ob, at, v) =>
        {
            if (tHit < 0f) { tHit = 0f; vIn = new Vector2(v.x, v.z).magnitude; }
            Debug.Log($"POWER smashed {ob.label} at {at} going {v.magnitude * 3.6f:0} km/h");
        };
        rider.Smashed += onSmash;
        step = 0;
        bool s0 = false, s1 = false;
        for (float t = 0f; t < 5f; t += Dt)
        {
            rider.Steer = Mathf.Clamp((target.x - rider.transform.position.x) * 0.8f - rider.Heading * 1.5f, -1f, 1f);
            Step();
            if (tHit >= 0f)
            {
                tHit += Dt;
                if (!s0 && tHit > 0.3f) { s0 = true; Frame($"PowerShotSmash{tag}0.png"); Debug.Log($"POWER {tag} speed in {vIn * 3.6f:0} km/h, 0.3 s later {rider.Speed * 3.6f:0} km/h crashed={rider.Crashed}"); }
                if (!s1 && tHit > 0.7f)
                {
                    s1 = true;
                    if (want == Forest.Kind.Tree) HudShot($"PowerShotSmash{tag}1.png"); else Frame($"PowerShotSmash{tag}1.png");
                }
                if (tHit > 1.4f)
                {
                    // and from the side, to see it go over / break up
                    // from the open piste beside it (the forest edge is the other way), looking at where it went over
                    float toPiste = Mathf.Sign(Slope.Centre(target.z) - target.x + 0.01f);
                    // (a tree: from above and behind where you hit it, so it reads lying across the snow below)
                    Vector3 look = target + (want == Forest.Kind.Tree ? new Vector3(0f, 0.5f, 4f) : Vector3.up * 0.6f);
                    Vector3 from = target + (want == Forest.Kind.Tree ? new Vector3(toPiste * 3f, 9f, -9f) : new Vector3(toPiste * 5f, 1.8f, -4f));
                    var fl = (System.Collections.IList)typeof(Smasher).GetField("fallen", NP).GetValue(smasher);
                    foreach (var f in fl) Debug.Log($"POWER fallen tree angle={(float)f.GetType().GetField("angle").GetValue(f) * Mathf.Rad2Deg:0} deg landed={f.GetType().GetField("landed").GetValue(f)}");
                    chase.enabled = false; cam.fieldOfView = 60f; Look(from, look);
                    DrawAll(); ShootBaked($"PowerShotSmash{tag}Side.png");
                    chase.enabled = true;
                    break;
                }
            }
            if (rider.Crashed) { Debug.Log($"POWER {tag}: CRASHED while invincible"); break; }
        }
        rider.Smashed -= onSmash;
        if (tHit < 0f) Debug.Log($"POWER {tag}: never reached it (rider at {rider.transform.position}, target {target})");
        Debug.Log($"POWER {tag} end: crashed={rider.Crashed} speed={rider.Speed * 3.6f:0} km/h invincible left={rider.InvincibleTime:0.0}s");
    }

    // ---- the stepping, the way the game runs it

    static float Hold() => Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);

    static void Step()
    {
        rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        smasher.Step(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
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

    // the same frame with the HUD drawn into it (it normally draws straight to the screen, which a render to a texture misses)
    static void HudShot(string file)
    {
        hud.SetPower(12.3f, rider.Shields, true);
        hud.SetTimers(true, rider.BoostTime, 3f, rider.SuperJumpTime, 10f, rider.InvincibleTime, 8f);
        hud.SetRun(rider.Speed * 3.6f, 1234, 1.5f, 80f, 160f, true);
        hud.Flash("TIMBER!", "+150");
        var canvas = hud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
        Frame(file);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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

    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        Canvas.ForceUpdateCanvases();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        Debug.Log("POWER wrote " + file);
    }
}
