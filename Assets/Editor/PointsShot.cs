using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The points on screen, rendered: a small trick, a big air trick as it is built and as it lands, a combo on a
/// rail and on a lift cable with the multiplier up, the bank, a dropped combo, and the medical bill mid-fall, on
/// its way to the corner and on the end screen. Each by day and by night, with the HUD as the phone draws it.
/// Run: Unity -batchmode -quit -projectPath . -executeMethod PointsShot.Run -logFile points.log   (no -nographics)
/// Writes points-shots/*.png; log lines start POINTS.
/// (Batchmode sends no collision messages, so the ragdoll's hits never reach the bill here: the shots put a
/// fall's costs on it by hand, the amounts the game charges: a bruise, a leg, an arm, a limb torn off.)
/// </summary>
public static class PointsShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    const string Dir = "points-shots";
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static CourseDressing dress; static Camera cam; static ChaseCamera chase; static MethodInfo late; static Atmosphere atmo;
    static Hud hud; static MetaUi meta; static DayNight dayNight;
    static int step; static float clock; static string tag = "day";
    static int landings; static float bankedAt = -1f;

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static void Call(string name, params object[] args)
    {
        foreach (var m in typeof(SummitGame).GetMethods(NP))
            if (m.Name == name && m.GetParameters().Length == args.Length) { m.Invoke(game, args); return; }
        Debug.LogError("POINTS no method " + name);
    }

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        hud = F<Hud>("hud"); meta = F<MetaUi>("meta"); dayNight = F<DayNight>("dayNight");
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        Directory.CreateDirectory(Dir);
        // overlay canvases don't draw into a render texture: hand them the camera (and the barrel lens would bend a
        // camera-space canvas, which the real overlay never is)
        meta.UseCamera(cam);
        var hc = hud.GetComponent<Canvas>(); hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
        var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
        if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
        ChaseCamera.TestDt = 4 * Dt;
        r.Landed += impact => landings++;
        game.Points.BankedNow += (total, pot, mult) => { bankedAt = clock; Debug.Log($"POINTS banked {total} = {pot} x{mult}"); };
        game.Points.Dropped += total => Debug.Log($"POINTS dropped {total}");
        r.Link += l => Debug.Log($"POINTS link {l.name} +{l.points}{(l.sketchy ? " sketchy" : "")}");
        try
        {
            TestBoot.NewMountain(game, 1000, terrain, props);
            if (dress != null) { var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt; }
            foreach (var (name, at) in new[] { ("day", 1.4f), ("night", 3.4f) })
            {
                tag = name;
                TimeOfDay.TestClock = at; dayNight.ApplyNow();
                if (name == "day") { Begin(Slope.Centre(Course.First + 110f), Course.First + 110f, 18f); Run(0.3f, null); for (int i = 0; i < 3; i++) Shoot(Dir + "/warmup.png"); }
                Ready(); Small(); BigAir(); Rail(); Cable(); Dropped(); Fall(); End();
            }
            File.Delete(Dir + "/warmup.png");
        }
        catch (System.Exception e) { Debug.LogError("POINTS exception " + e); }
        TimeOfDay.TestClock = -1f;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    // ---------------------------------------------------------------- the stepping, as the game runs it

    static void Begin(float x, float z, float v0, Vector3? along = null)
    {
        Call("ToReady", false);
        Call("ToRiding");
        r.input = null;
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        Vector3 dir = along ?? Vector3.forward;
        r.Heading = Mathf.Atan2(dir.x, dir.z);
        r.Body.linearVelocity = Vector3.ProjectOnPlane(dir, Slope.Normal(x, z)).normalized * v0;
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); chase.Snap();
        hud.Clear();
        step = 0; clock = 0f; landings = 0; bankedAt = -1f;
    }

    static void Step()
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        HudFrame();
        game.TestPoints(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (step % 4 == 0)
        {
            body.Apply(4 * Dt);
            // (only the rail's sparks: the snow puffs are stepped by their own Update, which batchmode never runs, and
            // simulated from here they hang in front of the camera as blobs)
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.name == "GrindSparks") ps.Simulate(4 * Dt, true, false, false);
            late.Invoke(chase, null);
        }
        step++; clock += Dt;
    }

    /// <summary>What SummitGame.Update tells the HUD every frame of a run.</summary>
    static void HudFrame()
    {
        bool riding = F<object>("state").ToString() == "Riding";
        int score = (int)typeof(SummitGame).GetProperty("Score", NP).GetValue(game);
        int falls = F<int>("falls");
        hud.SetRun(r.Speed * 3.6f, score, 1f, 9999f, 160f, riding);
        hud.SetPower(clock + 21.4f, r.Shields, riding);
        hud.SetTimers(riding, r.BoostTime, 3f, r.SuperJumpTime, 10f, r.InvincibleTime, 8f);
        hud.SetAltitude(Slope.AltitudeFeet(r.Z), riding);
        hud.SetMedical(F<float>("medical"), riding);
        hud.SetFalls(3 - falls, 3, riding);
    }

    delegate void Drive(float t);

    /// <summary>Ride on for a while (steering down the piste on the snow), with whatever the scene wants done each step.</summary>
    static void Run(float seconds, Drive drive, float dx = 0f)
    {
        for (float t = 0f; t < seconds; t += Dt)
        {
            r.TrickX = r.TrickY = 0f; r.GrabHeld = false;
            if (!r.Airborne && !r.Grinding) r.Steer = Mathf.Clamp((Slope.Centre(r.Z + 12f) + dx - r.transform.position.x) * 0.06f - r.Heading * 0.8f, -1f, 1f);
            else r.Steer = 0f;
            drive?.Invoke(t);
            Step();
        }
    }

    static void Shot(string name)
    {
        body.Apply(Dt);
        late.Invoke(chase, null);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
        string file = $"{Dir}/{name}-{tag}.png";
        Shoot(file);
        Debug.Log($"POINTS wrote {file}: combo {game.Points.Total} (x{game.Points.Mult}, {game.Points.Lines.Count} lines) live '{game.Points.LiveName}'");
    }

    static void Shoot(string file)
    {
        // batchmode renders do not re-skin meshes, so bake the skinned meshes to see the real pose
        var baked = new List<GameObject>(); var skins = new List<SkinnedMeshRenderer>();
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
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
    }

    static bool At(float t, float when) => t >= when && t - Dt < when;

    // ---------------------------------------------------------------- the scenes

    /// <summary>The start pad: the hint that says how the tricks are done.</summary>
    static void Ready()
    {
        Call("ToReady", false);
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); chase.Snap();
        step = 0; clock = 0f;
        HudFrame(); game.TestPoints(Dt);
        Shot("00-ready-hint");
    }

    /// <summary>A tapped jump on the piste with the stick held left: a 180. Small on screen.</summary>
    static void Small()
    {
        float z = Course.First + 110f;
        Begin(Slope.Centre(z), z, 18f);
        Run(1f, null);
        r.QueuePop(0.5f);
        landings = 0;
        for (float t = 0f; t < 3f && landings == 0; t += Dt) Run(Dt, _ => { if (r.Airborne) r.TrickX = r.AirTime > 0.08f ? -1f : 0f; });
        Run(0.3f, null);
        Shot("01-small-trick");
    }

    /// <summary>A kicker at speed: right and down held, the grab stepped up three times. As it is built, and as it lands.</summary>
    static void BigAir()
    {
        Slope.NextKicker(100f, out Vector3 at);
        Begin(at.x, at.z - 30f, 30f);
        float dx = at.x - Slope.Centre(at.z - 30f);
        bool shotAir = false; landings = 0; float last = 0f;
        for (float t = 0f; t < 8f; t += Dt)
        {
            Run(Dt, _ =>
            {
                if (!r.Airborne || r.Z < at.z - 5f) return;
                float a = r.AirTime;
                r.TrickX = a > 0.08f ? 1f : 0f; r.TrickY = a > 0.08f ? -1f : 0f;
                if ((last < 0.2f && a >= 0.2f) || (last < 0.5f && a >= 0.5f) || (last < 0.8f && a >= 0.8f)) r.TrickTap();
                r.GrabHeld = a > 0.2f && a < 1.5f;
                last = a;
            }, dx);
            if (!shotAir && r.Airborne && r.Z > at.z && r.AirTime > 1.25f) { shotAir = true; Shot("02-big-air-building"); }
            if (shotAir && landings > 0) break;
        }
        Run(0.3f, null, dx);
        Shot("03-big-air-landed");
    }

    static Course.Feature Feature(Course.Kind kind)
    {
        for (int c = 0; c < 3; c++)
            foreach (var f in Course.FeaturesOf(c)) if (f.kind == kind) return f;
        return default;
    }

    static void OntoRail(Course.Feature f, float until, bool flicks)
    {
        Begin(f.a.x, f.a.z - 18f, 14f);
        float on = 0f;
        for (float t = 0f; t < 10f && on < until; t += Dt)
        {
            float tx = r.Z < f.a.z ? f.a.x : Mathf.Lerp(f.a.x, f.b.x, Mathf.InverseLerp(f.a.z, f.b.z, r.Z));
            r.TrickX = r.TrickY = 0f;
            r.Steer = Mathf.Clamp((tx - r.transform.position.x) * 0.4f - r.Heading * 1.5f, -1f, 1f);
            if (r.Grinding)
            {
                on += Dt;
                if (flicks) { r.TrickX = on > 0.3f && on < 0.4f ? 1f : on > 1.0f && on < 1.1f ? -1f : 0f; r.TrickY = on > 0.65f && on < 0.75f ? 1f : on > 1.3f && on < 1.4f ? -1f : 0f; }
            }
            Step();
        }
    }

    /// <summary>A rail with two switch-ups and two presses: the combo live with the multiplier up; then off the end and the bank.</summary>
    static void Rail()
    {
        var f = Feature(Course.Kind.Rail);
        OntoRail(f, 1.55f, true);
        Shot("04-rail-combo");
        if (Missions.Today.Count > 0) { meta.ToastNow(Missions.Today[0]); Shot("04b-rail-combo-with-a-mission-toast"); typeof(MetaUi).GetField("toastT", NP).SetValue(meta, 99f); typeof(MetaUi).GetMethod("Update", NP).Invoke(meta, null); }
        // off the end, landed, and the window runs out: the bank
        landings = 0; bankedAt = -1f;
        for (float t = 0f; t < 8f && bankedAt < 0f; t += Dt) Run(Dt, null, f.b.x - Slope.Centre(f.b.z));
        Run(0.2f, null); Shot("06-bank-flying");
        Run(0.4f, null); Shot("07-bank-counted");
    }

    /// <summary>Up a lift's ramp and along its cable, switching up as it goes.</summary>
    static void Cable()
    {
        foreach (var l in LiftLine.Lifts)
        {
            var rp = l.ramp;
            if (rp == null) continue;
            Vector3 foot = LiftLine.RampPoint(l, rp, 0f), d = LiftLine.RampDir(l, rp, 0f);
            Vector3 flat = new Vector3(d.x, 0f, d.z).normalized, from = foot - flat * 10f;
            Begin(from.x, from.z, rp.needSpeed + 4f, flat);
            float on = 0f; bool shot = false;
            for (float t = 0f; t < 30f && !r.Crashed; t += Dt)
            {
                Vector3 p = r.transform.position;
                float ahead = Vector3.Dot(foot - p, flat);
                Vector3 aim = ahead > 2f ? foot + flat * 2f : p + flat * 10f;
                r.Steer = r.Grinding ? 0f : Mathf.Clamp((Mathf.Atan2(aim.x - p.x, aim.z - p.z) - r.Heading) * 3f - r.YawRate * 0.2f, -1f, 1f);
                r.TrickX = r.TrickY = 0f;
                if (r.OnCable)
                {
                    on += Dt;
                    float ph = Mathf.Repeat(on, 1.2f);
                    r.TrickX = ph > 0.5f && ph < 0.6f ? 1f : 0f; r.TrickY = ph > 1f && ph < 1.1f ? 1f : 0f;
                }
                Step();
                if (!shot && on > 4.4f && r.OnCable) { shot = true; Shot("05-cable-combo"); break; }
            }
            if (!shot) Debug.Log($"POINTS cable: never got {on:0.0} s on it (crashed={r.Crashed})");
            return;
        }
        Debug.Log("POINTS no lift with a ramp on this mountain");
    }

    /// <summary>A rail combo, then a fall: the pot goes red, shakes and falls away (and the fall's bill comes in after it).</summary>
    static void Dropped()
    {
        var f = Feature(Course.Kind.Rail);
        OntoRail(f, 1.2f, true);
        r.DoCrash("Tree");
        Run(0.16f, null); Shot("08-dropped-shake");
        Run(0.34f, null); Shot("09-dropped-falling");
    }

    static void Bill(float cost) => typeof(SummitGame).GetMethod("Bill", NP).Invoke(game, new object[] { cost });

    /// <summary>A fall at speed on the piste: the bill jumping with each hit, then into the corner as the rider gets up.</summary>
    static void Fall()
    {
        float z = Course.First + 110f;
        Begin(Slope.Centre(z), z, 24f);
        Run(1.2f, null);
        r.DoCrash("Tree");                      // (the clinic trip goes on at once: 850)
        Run(0.3f, null); Bill(320f);            // a bruise
        Run(0.1f, null); Shot("10-bill-first-hits");
        Run(0.2f, null); Bill(9400f);           // a leg
        Run(0.07f, null); Shot("11-bill-big-hit");
        Run(0.5f, null); Bill(540f); Run(0.3f, null); Bill(4600f);   // another bruise, an arm
        Run(0.6f, null); Shot("12-bill-settled");
        // back on your feet: the fall's figure flies up into the total
        for (float t = 0f; t < 10f && r.Crashed; t += Dt) { if (r.CanGetUp) r.RequestUp = true; Run(Dt, null); }
        Run(0.2f, null); Shot("13-bill-to-corner");
        Run(0.9f, null); Shot("14-bill-in-corner");
        // and a worst case for size: a limb torn off
        r.DoCrash("Tree");
        Run(0.3f, null); Bill(9400f); Run(0.25f, null); Bill(124000f);
        Run(0.08f, null); Shot("15-bill-limb");
    }

    /// <summary>The run's last fall: the bill mid-fall, then carried into the end screen, counting up to the total.</summary>
    static void End()
    {
        float z = Course.First + 110f;
        Begin(Slope.Centre(z), z, 24f);
        typeof(SummitGame).GetField("medical", NP).SetValue(game, 15710f);   // (two earlier falls' worth)
        typeof(SummitGame).GetField("falls", NP).SetValue(game, 2);
        Run(1.2f, null);
        r.DoCrash("Tree");
        Run(0.3f, null); Bill(410f); Run(0.3f, null); Bill(12500f); Run(0.4f, null); Bill(7800f);
        Run(0.9f, null); Shot("16-last-fall");
        Call("ToEnd", "WIPED OUT", false, false);
        // the end screen fades in over the last of it (SummitGame.Update's State.Buried, by hand)
        for (float t = 0f; t < 2.6f; t += Dt)
        {
            float a = Mathf.Clamp01(t / 1.4f);
            hud.SetAlpha(a); meta.SetEndAlpha(a);
            Run(Dt, null);
            if (At(t, 0.55f)) Shot("17-end-screen-counting");
        }
        Shot("18-end-screen");
        hud.SetAlpha(1f);
    }
}
