using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The chairlifts through the game's own wiring, at the phone's quality level: from a seat on a chair going up,
/// looking back down the line at the tower behind (the Whistler photo's viewpoint); from the piste looking up at
/// a line crossing over it; and from high above, the whole network; each by day and by night. Then the same seat
/// a second and a half apart (the chairs move), and what the lifts cost to draw with the rider at every 30 m of
/// the mountain. Run: Unity -batchmode -projectPath . -executeMethod LiftShot.Run -logFile x.log   (no -quit: it takes
/// a picture each editor frame and quits by itself)
///   SUMMIT_LIFT_TAG   a prefix for the pictures (before, after)        SUMMIT_LIFT_OUT   the folder they go in
///   SUMMIT_LIFT_SEED  the mountain (4242)                              SUMMIT_LIFT_POSES a file of camera places
/// The places the camera stands are written to LiftShot_poses.txt in the output folder; given back as
/// SUMMIT_LIFT_POSES they are used as they are, so a before and an after are taken from the same spots.
/// </summary>
public static class LiftShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static CourseDressing dress;
    static Camera cam; static ChaseCamera chase; static Atmosphere atmo; static DayNight dayNight; static SkiLift lift;
    static MethodInfo tUpd, fUpd, lUpd;
    static string tag, outDir;

    class Pose { public string name; public Vector3 rider, eye, look; public float fov; public double clock; public System.Action<Pose> then; }

    public static void Run()
    {
        tag = System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_TAG") ?? "lift";
        outDir = System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_OUT") ?? ".";
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_SEED"), out int sd) ? sd : 4242;
        string posesIn = System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_POSES");
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Rider>("rider"); body = Get<RiderBody>("body"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        dayNight = Get<DayNight>("dayNight");
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        lift = Object.FindFirstObjectByType<SkiLift>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); lUpd = typeof(SkiLift).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        SkiLift.TestClock = 100.0;
        Log($"seed {seed}: {LiftLine.Lifts.Length} lifts\n" + LiftLine.Report() + LiftLine.RampReport());
        // SUMMIT_LIFT_SET=jump: the bottom station you ski through (LiftJump.cs), not the lines
        bool jumpSet = System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_SET") == "jump";
        if (jumpSet) Log(LiftLine.JumpReport());
        var poses = jumpSet ? FindJump() : posesIn != null && File.Exists(posesIn) ? Read(posesIn) : Find();
        if (!jumpSet) Write(Path.Combine(outDir, "LiftShot_poses.txt"), poses);
        // One picture to an editor frame: what SkiLift asks the GPU to draw (Graphics.RenderMeshInstanced) stays
        // queued until the frame ends, so two pictures taken in one frame would show every chair twice.
        steps.Clear(); at = 0;
        steps.Add(() => { TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); Stand(poses[0]); for (int i = 0; i < 3; i++) Shoot(Path.Combine(outDir, "LiftShot_warmup.png")); });
        foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
            foreach (var p in poses)
            {
                if (when == "night" && p.name.StartsWith("moving")) continue;
                steps.Add(() =>
                {
                    TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                    Stand(p);
                    p.then?.Invoke(p);
                    Shoot(Path.Combine(outDir, $"{tag}-{p.name}-{when}.png"));
                    lift.Stats(out int draws, out long tris, out int inst);
                    Log($"{tag} {p.name} {when}: {draws} draw calls, {tris} triangles, {inst} instances (rider at {p.rider})");
                });
            }
        steps.Add(() => { TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); Budget(); });
        steps.Add(() =>
        {
            SkiLift.TestClock = -1.0;
            TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
            TimeOfDay.TestClock = -1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            QualitySettings.SetQualityLevel(quality, false);
        });
        EditorApplication.update += Tick;
    }

    static readonly List<System.Action> steps = new List<System.Action>();
    static int at;
    static void Tick()
    {
        if (at < steps.Count)
        {
            try { steps[at++](); }
            catch (System.Exception e) { Debug.LogError("LIFTSHOT exception " + e); at = steps.Count; }
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    // ---------------------------------------------------------------- where the camera stands

    static List<Pose> Find()
    {
        var poses = new List<Pose>();
        var lifts = LiftLine.Lifts;
        // a second line that is carried over its cycle's first line (the second cycle's, if it has one)
        LiftLine.Lift S = null, F = null;
        foreach (var c in lifts)
            if (c.second && c.under.Length > 0 && (S == null || (c.k == 1 && S.k != 1))) { S = c; F = c.under[0]; }
        if (S == null) { F = lifts[Mathf.Min(1, lifts.Length - 1)]; }
        // where the two meet, along the first line (or its middle, if there is no second line here)
        float tAt = F.plan * 0.5f;
        if (S != null)
        {
            float det = S.dir.x * F.dir.z - S.dir.z * F.dir.x, dx = F.top.x - S.top.x, dz = F.top.z - S.top.z;
            tAt = (dx * S.dir.z - dz * S.dir.x) / det;
        }
        // the seat: on the first line's cable going up, 55 m above the crossing, looking back down the line
        float s = Mathf.Clamp(tAt - 55f, F.nodeS[0] + 10f, F.nodeS[F.Spans] - 40f);
        int span = LiftLine.SpanAt(F, s);
        Vector3 seat = F.At(s, LiftLine.Half + 0.5f, F.CableY(span, s) - 1.35f);
        Vector3 ahead = F.At(s + 60f, LiftLine.Half - 2f, seat.y - 9f);
        // (between two chairs: the nearest is half a spacing away)
        int n = F.Spans;
        float d = F.cum[n - span] + (F.nodeS[span + 1] - s) / (F.nodeS[span + 1] - F.nodeS[span]) * (F.cum[n - span + 1] - F.cum[n - span]);
        double clock = (d + F.spacing * 0.5 + F.spacing * System.Math.Ceiling(100.0 * LiftLine.Speed / F.spacing)) / LiftLine.Speed;
        Vector3 under = new Vector3(seat.x, 0f, seat.z);
        poses.Add(new Pose { name = "seat", rider = under, eye = seat, look = ahead, fov = 74f, clock = clock });
        for (int k = 0; k < 6; k++)
            poses.Add(new Pose { name = "moving" + k, rider = under, eye = seat, look = ahead, fov = 74f, clock = clock + 0.6 * k });
        // the crossing: standing on the piste 70 m above where a cable passes over its centre line (the second line's, if there is one)
        Vector3 over = Vector3.zero; bool any = false;
        foreach (var c in lifts)
        {
            if (S != null && c != S) continue;
            for (float a = c.nodeS[0]; a < c.nodeS[c.Spans] && !any; a += 1f)
            {
                Vector3 q = c.At(a, 0f, 0f);
                if (Mathf.Abs(q.x - Slope.Centre(q.z)) > 1f || q.z < 300f) continue;
                over = c.At(a, 0f, c.CableY(LiftLine.SpanAt(c, a), a)); any = true;
            }
        }
        if (!any) { var c = lifts[lifts.Length / 2]; over = c.At(c.plan * 0.5f, 0f, c.CableY(LiftLine.SpanAt(c, c.plan * 0.5f), c.plan * 0.5f)); }
        float zc = over.z - 70f, xc = Slope.Centre(zc);
        poses.Add(new Pose { name = "crossing", rider = new Vector3(xc, 0f, zc), eye = new Vector3(xc + 3f, Slope.DrawnHeight(xc + 3f, zc) + 1.7f, zc), look = over + Vector3.down * 9f, fov = 74f, clock = 100.0 });
        // far: from 20 m over the piste's edge, 170 m above the crossing, both lines and the next ones down the mountain
        float zf = over.z - 170f, xf = Slope.Centre(zf) + (S != null ? -Mathf.Sign(S.offTop) : 1f) * 16f;
        Vector3 eye = new Vector3(xf, Slope.DrawnHeight(xf, zf) + 20f, zf);
        poses.Add(new Pose { name = "far", rider = new Vector3(xf, 0f, zf), eye = eye, look = over + new Vector3(0f, -14f, 50f), fov = 62f, clock = 100.0 });
        return poses;
    }

    // ---------------------------------------------------------------- the station you ski through

    /// <summary>
    /// The jump station of the mountain's second cycle (or the first it has): from up the hill as the rider comes to
    /// it, at its mouth, from the piste's side, from below its lip, with the rider in the air off it and with the
    /// rider on its deck coming to the lip (real rides through the lane at 26 m/s, stopped for the picture).
    /// </summary>
    static List<Pose> FindJump()
    {
        var poses = new List<Pose>();
        LiftLine.Lift l = null;
        int want = int.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_CYCLE"), out int wc) ? wc : 1;
        foreach (var c in LiftLine.Lifts) if (c.jump != null && (l == null || (c.k == want && l.k != want))) l = c;
        if (l == null) { Log("no jump station on this mountain"); return poses; }
        var j = l.jump;
        Vector3 up = Vector3.up, toPiste = l.across * j.side;
        System.Func<float, float, Vector3> G = (t, c) => { Vector3 q = l.At(l.plan + t, j.side * c, 0f); q.y = Slope.DrawnHeight(q.x, q.z); return q; };
        float mid = LiftLine.JumpLaneMid;
        // from up the hill: the rider 24 m above where the lane's top leaves the snow, the camera behind them as in the game
        Vector3 r0 = G(j.start - 24f, mid);
        poses.Add(new Pose { name = "jump-uphill", rider = r0, eye = G(j.start - 32f, mid) + up * 3.4f, look = l.At(l.plan + 2f, j.side * mid, j.y0 + 1f), fov = 74f, clock = 100.0 });
        // nearer, at the mouth: what the lane looks like as you come onto it
        Vector3 r1 = G(j.start - 5f, mid + 1f);
        poses.Add(new Pose { name = "jump-mouth", rider = r1, eye = G(j.start - 12f, mid + 1.5f) + up * 3.0f, look = l.At(l.plan + j.end, j.side * mid, j.y0 + 1.5f), fov = 74f, clock = 100.0 });
        // from the side: off the piste's side of it, level with its deck, all of it
        Vector3 side = l.At(l.plan + 1f, j.side * (mid + 34f), 0f);
        poses.Add(new Pose { name = "jump-side", rider = G(j.start - 60f, mid), eye = new Vector3(side.x, Mathf.Max(Slope.DrawnHeight(side.x, side.z) + 2f, j.y0 + 2.5f), side.z) + l.dir * 8f, look = l.At(l.plan + 1f, 0f, j.y0 - 1f), fov = 70f, clock = 100.0 });
        // from below: under the lip, looking back up at it
        Vector3 below = G(j.end + 30f, mid + 10f);
        poses.Add(new Pose { name = "jump-below", rider = G(j.start - 60f, mid), eye = below + up * 2f, look = l.At(l.plan + j.end - 4f, j.side * (mid - 4f), j.y0 + 1f), fov = 70f, clock = 100.0 });
        // in the air off it
        poses.Add(new Pose
        {
            name = "jump-air", rider = G(j.start - 10f, mid), fov = 74f, clock = 100.0,
            eye = Vector3.zero, look = Vector3.zero,
            then = p =>
            {
                Fly(l, 26f, 1.05f);
                Vector3 at = rider.Body.position;
                // (from ahead of the rider and below, toward the piste: the rider against the sky, the station behind)
                Vector3 eye = at + l.dir * 13f + toPiste * 9f - up * 5.5f;
                eye.y = Mathf.Max(eye.y, Slope.DrawnHeight(eye.x, eye.z) + 1.5f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at + up * 0.6f - l.dir * 3f - eye));
                Log($"{tag} jump-air: the rider {rider.AboveGround:0.0} m over the snow, {rider.AirTime:0.00} s off the lip, trick '{rider.TrickName}'");
            },
        });
        // and as the game shows it, from behind the rider: on the deck, coming to the lip
        poses.Add(new Pose
        {
            name = "jump-lip", rider = G(j.start - 10f, mid), fov = 74f, clock = 100.0, eye = Vector3.zero, look = Vector3.zero,
            then = p =>
            {
                Fly(l, 26f, 0f, j.end - 9f);
                Vector3 at = rider.Body.position;
                Vector3 eye = at - l.dir * 7.5f + up * 3.1f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at + l.dir * 16f - up * 1.5f - eye));
            },
        });
        return poses;
    }

    /// <summary>A real ride through the lane from where the rider stands, at v0, until `air` seconds after the lip.</summary>
    static void Fly(LiftLine.Lift l, float v0, float air, float stopOnDeckAt = float.MaxValue)
    {
        const float dt = 1f / 120f;
        Vector3 from = rider.Body.position;
        rider.Begin();
        rider.Heading = Mathf.Atan2(l.dir.x, l.dir.z);
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(l.dir, Slope.Normal(from.x, from.z)).normalized * v0;
        // (the lift itself is left as Stand drew it: asked to draw again in this frame, its chairs would show twice)
        bool on = false; float off = 0f;
        for (int n = 0; n < 1200; n++)
        {
            rider.Steer = 0f;
            rider.Tick(dt); body.PhysicsStep(); Physics.Simulate(dt);
            if (n % 12 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (n % 4 == 0) body.Apply(4 * dt);
            bool deck = rider.Grinding && rider.GrindFeature.id == -3;
            if (deck) on = true;
            Vector3 q = rider.Body.position;
            if (deck && (q.x - l.top.x) * l.dir.x + (q.z - l.top.z) * l.dir.z - l.plan >= stopOnDeckAt) break;
            if (on && !deck) { off += dt; if (off >= air || !rider.Airborne) break; }
        }
        body.Apply(dt);
        rider.Stop();
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        // (a still has no use for the takeoff's puff of snow: batchmode leaves its particles hanging where they were born)
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear(true);
        var fx = Object.FindFirstObjectByType<SnowFX>();
        if (fx != null) fx.Clear();
    }

    static string V(Vector3 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R}", v.x, v.y, v.z);
    static void Write(string file, List<Pose> poses)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in poses) sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4:R} {5:R}", p.name, V(p.rider), V(p.eye), V(p.look), p.fov, p.clock));
        File.WriteAllText(file, sb.ToString());
    }
    static List<Pose> Read(string file)
    {
        var poses = new List<Pose>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var line in File.ReadAllLines(file))
        {
            var w = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (w.Length < 12) continue;
            float F(int i) => float.Parse(w[i], inv);
            poses.Add(new Pose { name = w[0], rider = new Vector3(F(1), F(2), F(3)), eye = new Vector3(F(4), F(5), F(6)), look = new Vector3(F(7), F(8), F(9)), fov = F(10), clock = double.Parse(w[11], inv) });
        }
        return poses;
    }

    // ---------------------------------------------------------------- taking them

    static void Stand(Pose p)
    {
        rider.ResetAt(p.rider.x, p.rider.z); body.ResetVisual(); rider.Stop();
        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
        SkiLift.TestClock = p.clock;
        chase.enabled = false;
        cam.fieldOfView = p.fov;
        cam.transform.SetPositionAndRotation(p.eye, Quaternion.LookRotation(p.look - p.eye));
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
        typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
        for (int i = 0; i < 3; i++) lUpd.Invoke(lift, null);   // (the lamp fades out, moves and fades in)
    }

    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; hidden.Add(sk); baked.Add(g);
        }
        int w = 886, h = 1920;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }

    // ---------------------------------------------------------------- what they cost

    /// <summary>The lifts' draw calls and triangles with the rider at every 30 m of the piste, top to bottom.</summary>
    static void Budget()
    {
        var probe = new GameObject("LiftProbe").transform;
        var tf = typeof(SkiLift).GetField("target", NP);
        var was = tf.GetValue(lift);
        tf.SetValue(lift, probe);
        long sumT = 0, maxT = 0; int sumD = 0, maxD = 0, n = 0, maxI = 0; float atT = 0f, atD = 0f;
        for (float z = 0f; z < Slope.FinishZ; z += 30f)
        {
            probe.position = new Vector3(Slope.Centre(z), Slope.Height(Slope.Centre(z), z), z);
            typeof(SkiLift).GetField("refreshClock", NP).SetValue(lift, 0f);
            lUpd.Invoke(lift, null);
            lift.Stats(out int draws, out long tris, out int inst);
            sumT += tris; sumD += draws; n++;
            if (tris > maxT) { maxT = tris; atT = z; }
            if (draws > maxD) { maxD = draws; atD = z; }
            maxI = Mathf.Max(maxI, inst);
        }
        tf.SetValue(lift, was);
        Object.DestroyImmediate(probe.gameObject);
        Log($"{tag} BUDGET over {n} places down the piste: draw calls mean {sumD / (float)n:0.0}, most {maxD} (z={atD:0}); triangles mean {sumT / n}, most {maxT} (z={atT:0}); most instances {maxI}; lifts {LiftLine.Lifts.Length}");
    }

    static void Log(string s) => Debug.Log("LIFTSHOT " + s);
}
