using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The blood on the snow, crash by crash, at the phone's quality: riding into a tree and into a rock, a long tumble
/// after a big fall onto the snow, and a fall off a cliff that kills, by day, at night under a floodlight (and in
/// the headlamp) and in a storm. Each crash is filmed from one fixed camera at moments after the first blood (a
/// strip of frames: the spray landing, the soak spreading, the trail, the pool) and once from the chase camera, as
/// the player sees it. Run: Unity -batchmode -projectPath . -executeMethod BloodSnowShot.Run -logFile x.log (no
/// -nographics: it needs the GPU). BLOODSNOW_OUT = the folder (default BloodSnowShots), BLOODSNOW_ONLY = a
/// comma-separated list of scene names.
///
/// Batchmode drives physics from an editor script, where Unity sends no collision messages, so (as BloodShot) this
/// stands in for them every step.
/// </summary>
public static class BloodSnowShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    const int W = 590, H = 1278;

    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camera cam; static ChaseCamera chase; static MethodInfo late, tUpd, fUpd, aLate, advance; static Atmosphere atmo; static Blood blood;
    static DayNight dayNight; static Weather weather; static SnowFX fx; static NightLights lights;
    static readonly Dictionary<Rigidbody, HashSet<Collider>> touching = new Dictionary<Rigidbody, HashSet<Collider>>();
    static readonly Dictionary<Rigidbody, Vector3> lastV = new Dictionary<Rigidbody, Vector3>();
    static Vector3 riderLastV; static bool riderHit;
    static Vector3 lastHitAt, lastHitN; static int struck;
    static string outDir; static HashSet<string> only;
    static readonly float[] Moments = { 0.1f, 0.4f, 0.9f, 1.8f, 3.5f, 7f };
    static readonly System.Diagnostics.Stopwatch bloodTime = new System.Diagnostics.Stopwatch();
    static int bloodCalls; static bool holdForest;

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("BLOODSNOW_OUT") ?? "BloodSnowShots";
        Directory.CreateDirectory(outDir);
        var o = System.Environment.GetEnvironmentVariable("BLOODSNOW_ONLY");
        if (!string.IsNullOrEmpty(o)) only = new HashSet<string>(o.Split(','));
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        QualitySettings.SetQualityLevel(0, true);   // the phone's
        Weather.TestChoice = 1;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = game.rider; body = game.body; terrain = game.terrain; props = game.props;
        blood = game.blood; dayNight = game.dayNight; weather = Get<Weather>("weather"); fx = Get<SnowFX>("fx");
        lights = NightLights.Instance;
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        fUpd = typeof(Forest).GetMethod("Update", NP);
        advance = typeof(Blood).GetMethod("Advance", BindingFlags.Public | BindingFlags.Instance);
        Log($"blood on={blood.On}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, animated={(advance != null)}");
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        try { Sequence(); }
        catch (System.Exception e) { Debug.LogError("BLOODSNOW exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static bool Want(string n) => only == null || only.Contains(n);

    static void Sequence()
    {
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        blood.SetOn(true);
        float z0 = Course.First + 30f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        Sky(1, 1.4f);
        for (int i = 0; i < 4; i++) Shoot(null);   // the first renders come out unlit

        if (Want("tree_day")) { Sky(1, 1.4f); foreach (float tz in new[] { 160f, 420f, 700f, 1000f, 1400f, 1900f }) if (Into("Tree", Course.First + tz, 22f, "tree_day")) break; }
        if (Want("rock_day")) { Sky(1, 1.4f); foreach (float rz in new[] { 420f, 300f, 560f, 650f, 820f }) if (Into("Rock", Course.First + rz, 22f, "rock_day")) break; }
        if (Want("tumble_day")) { Sky(1, 1.4f); Tumbles(Course.First + 700f, "tumble_day"); }
        if (Want("cliff_day")) { Sky(1, 1.4f); Cliff(Course.First + 1100f, "cliff_day"); }
        if (Want("tumble_storm")) { Sky(4, 1.4f); Tumbles(Course.First + 700f, "tumble_storm"); }
        if (Want("tumble_night") || Want("tree_night"))
        {
            // a piste floodlight: the big fall lands in its pool
            NightLights.Lamp best = default; float bd = 1e9f; bool found = false;
            foreach (var l in NightLights.Lamps)
            {
                if (l.kind != NightLights.Kind.Piste) continue;
                Vector3 pool = PoolOf(l);
                float d = Mathf.Abs(pool.z - (Course.First + 650f));
                if (d < bd) { bd = d; best = l; found = true; }
            }
            if (found && Want("tumble_night"))
            {
                Vector3 pool = PoolOf(best);
                Log($"night: lamp {best.id} head {best.head} pool {pool}");
                Sky(1, 3.4f);
                Tumbles(pool.z - 35f, "tumble_night", pool.x);
            }
            if (Want("tree_night")) { Sky(1, 3.4f); foreach (float tz in new[] { 160f, 420f, 700f, 1000f, 1400f, 1900f }) if (Into("Tree", Course.First + tz, 22f, "tree_night")) break; }
        }
        if (Want("tumble_nightstorm"))
        {
            Sky(4, 3.4f);
            Tumbles(Course.First + 700f, "tumble_nightstorm");
        }
        HitCost();
        if (bloodCalls > 0) Log($"blood CPU: {bloodTime.Elapsed.TotalMilliseconds / bloodCalls:0.000} ms a frame over {bloodCalls} frames (editor)");
        Sky(1, 1.4f);
    }

    static Vector3 PoolOf(NightLights.Lamp l)
    {
        Vector3 p = l.head;
        for (int i = 0; i < 400; i++) { p += l.dir * 0.25f; if (p.y <= Slope.DrawnHeight(p.x, p.z)) break; }
        return p;
    }

    static void Sky(int choice, float clock)
    {
        Weather.TestChoice = choice; TimeOfDay.TestClock = clock;
        weather.Step(0f);
        for (int i = 0; i < 30; i++) Frame(Dt);
        weather.Step(0f); dayNight.ApplyNow();
        if (lights != null) lights.Prewarm();
    }

    // ---------------------------------------------------------------- the crashes

    /// <summary>Ride flat out into the nearest tree or rock of this kind.</summary>
    static bool Into(string kind, float near, float speed, string name)
    {
        rider.ResetAt(Slope.Centre(near), near); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm(); fUpd.Invoke(props, null);
        Obstacle best = null; float bestD = 1e9f;
        foreach (var ob in Object.FindObjectsByType<Obstacle>(FindObjectsSortMode.None))
        {
            if (!ob.gameObject.activeInHierarchy || ob.label != kind) continue;
            var col = ob.GetComponentInChildren<Collider>(); if (col == null || !col.enabled) continue;
            var p = col.bounds.center;
            if (p.z < near + 10f) continue;
            if (kind == "Tree" && (props.Snappable(col.gameObject, out _))) continue;   // (a young one snaps: TreeBreak)
            float d = Mathf.Abs(p.z - near - 25f) + Mathf.Abs(p.x - Slope.Centre(p.z)) * 0.5f
                      + (Mathf.Abs(p.x - Slope.Centre(p.z)) > Slope.PisteHalf ? 200f : 0f);
            if (d >= bestD) continue;
            Vector3 st = p + Vector3.back * (col.bounds.extents.z + 1.4f); st.y = Slope.DrawnHeight(st.x, st.z) + 0.5f;
            bool clear = true;
            foreach (var oc in Physics.OverlapSphere(st, 1.2f, ~0, QueryTriggerInteraction.Ignore)) if (oc != col && oc.GetComponentInParent<Obstacle>() != null) clear = false;
            if (!clear) continue;
            bestD = d; best = ob;
        }
        if (best == null) { Log($"{name}: no {kind} near z={near}"); return false; }
        var bcol = best.GetComponentInChildren<Collider>();
        var target = bcol.bounds.center;
        // a rock is ridden into; a tree's thin trunk would snap under the board (TreeBreak), so the rider comes off
        // a few metres short and the thrown body goes into it
        bool thrown = kind == "Tree";
        Vector3 start = target + Vector3.back * (bcol.bounds.extents.z + (thrown ? 3.2f : 1.4f));
        rider.ResetAt(start.x, start.z); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm(); fUpd.Invoke(props, null);
        // the forest's hit shapes are pooled: find the one standing there now, and aim at it
        Physics.SyncTransforms();
        float bd = 4f;
        foreach (var c in Physics.OverlapSphere(target, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            var o = c.GetComponentInParent<Obstacle>();
            if (o == null || o.label != kind) continue;
            Vector3 cc = c.bounds.center; cc.y = target.y;
            float dd = (cc - target).magnitude;
            if (dd < bd) { bd = dd; bcol = c; }
        }
        target = bcol.bounds.center;
        Log($"{name}: aiming at {bcol.name} {bcol.bounds.min}..{bcol.bounds.max} from {rider.Body.position}");
        Vector3 to = target - rider.Body.position; to.y = 0f; to.Normalize();
        rider.Heading = Mathf.Atan2(to.x, to.z);
        rider.Body.linearVelocity = to * speed + Vector3.down * 2f;
        chase.enabled = true; chase.Snap();
        blood.Clear(); fx.Clear(); struck = 0; riderHit = false; lastHitAt = Vector3.zero;
        if (thrown)
        {
            var lift = rider.Body.position + Vector3.up * 0.15f;
            rider.Body.position = lift; rider.transform.position = lift;
            rider.Body.linearVelocity = (target + Vector3.down * 0.3f - lift).normalized * speed;   // (down the slope's drop, into the post)
            body.Apply(Dt);
            rider.DoCrash("test");
        }
        float hitT = -1f;
        Vector3 aimAt = target, fwd = to;
        // (the forest's hit shapes stay where they are for the run-up: re-laid round the camera, the one in front
        // of the rider went elsewhere before it got there)
        holdForest = true;
        // (and stands as a big old trunk would: this mountain's trees near the piste are thin enough to snap)
        var tb = TreeBreak.Instance;
        var tbProp = typeof(TreeBreak).GetProperty("Instance");
        if (thrown) tbProp.SetValue(null, null);
        for (float t = 0f; t < 1.5f && hitT < 0f; t += Dt) { rider.Steer = 0f; Step(); if (riderHit || (thrown && blood.Shown > 0)) hitT = t; }
        if (thrown && hitT >= 0f && lastHitAt == Vector3.zero) lastHitAt = target;
        holdForest = false;
        tbProp.SetValue(null, tb);
        if (hitT < 0f) { Log($"{name}: never reached the {kind}: rider at {rider.Body.position} crashed={rider.Crashed}, target {target}, collider {bcol.GetType().Name} {bcol.bounds.min}..{bcol.bounds.max}"); return false; }
        aimAt = lastHitAt;
        Log($"{name}: {kind} {best.name} hit at {lastHitAt}");
        Film(name, aimAt, fwd, 3.5f, 0f);
        // the face it hit, close
        var om = typeof(Blood).GetMethod("ObstacleMark", BindingFlags.Public | BindingFlags.Instance);
        var oargs = new object[] { lastHitAt, null, null };
        if (om != null && (bool)om.Invoke(blood, oargs))
        {
            Vector3 mAt = (Vector3)oargs[1], mN = (Vector3)oargs[2];
            Vector3 nh = new Vector3(mN.x, 0f, mN.z).normalized;
            Look(mAt + nh * 2.4f + Vector3.up * 0.7f + Vector3.Cross(Vector3.up, nh) * 0.8f, mAt + Vector3.down * 0.15f, 45f);
            Shoot($"{name}_face.png");
        }
        return true;
    }

    /// <summary>A crash in the air a few metres up onto the snow: it hits hard and tumbles and slides on.</summary>
    static void Tumbles(float z, string name, float x = float.NaN)
    {
        // a dry run first: the throw whose body slides on and bleeds a trail (the throw's spin is random)
        int pick = 1; float most = -1f;
        var laid = typeof(Blood).GetProperty("TrailLaid");
        for (int seed = 1; seed <= 6 && laid != null; seed++)
        {
            Throw(seed, z, x);
            for (float t = 0f; t < 3.5f; t += Dt) Step();
            float m = (float)laid.GetValue(blood);
            if (m > most) { most = m; pick = seed; }
            if (m > 3f) break;
        }
        // (the replay is not always the same throw: if it draws no blood, the others in turn)
        foreach (int seed in new[] { pick, 1, 2, 3, 4, 5, 6 })
        {
            Throw(seed, z, x);
            int marks0 = blood.Shown;
            bool bled = false;
            for (float t = 0f; t < 3f && !bled; t += Dt) { Step(); bled = blood.Shown > marks0 || BloodyDrops(); }
            if (!bled) { Log($"{name}: throw {seed} drew no blood"); continue; }
            Vector3 c = body.RagdollCentre;
            Vector3 g = new Vector3(c.x, Slope.DrawnHeight(c.x, c.z), c.z);
            Log($"{name}: first blood at {g} (throw {seed})");
            Film(name, g, Vector3.forward, 6f, 0f);
            return;
        }
    }

    static void Throw(int seed, float z, float x)
    {
        Random.InitState(seed * 97);
        float cx = float.IsNaN(x) ? Slope.Centre(z) : x;
        rider.ResetAt(cx, z); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        var p = rider.Body.position + Vector3.up * 8f;
        rider.Body.position = p; rider.transform.position = p;
        rider.Body.linearVelocity = new Vector3(0f, -4f, 22f);
        body.Apply(Dt);
        rider.DoCrash("test");
        blood.Clear(); fx.Clear(); struck = 0;
        SnowLayer.Instance?.Clear();
        chase.enabled = true; chase.Snap();
    }

    /// <summary>Off a cliff: dropped far past Rider.DeathFall onto the snow, the run's end.</summary>
    static void Cliff(float z, string name)
    {
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        var p = rider.Body.position + Vector3.up * (Rider.DeathFall + 15f);
        rider.Body.position = p; rider.transform.position = p; rider.DropFrom = p;
        rider.Body.linearVelocity = new Vector3(0f, -2f, 6f);
        body.Apply(Dt);
        rider.DoCrash("test");
        blood.Clear(); fx.Clear(); struck = 0;
        chase.enabled = true; chase.Snap();
        int marks0 = blood.Shown;
        float t = 0f;
        for (; t < 9f && blood.Shown == marks0 && !body.Dead; t += Dt) Step();
        Vector3 c = body.RagdollCentre;
        Vector3 g = new Vector3(c.x, Slope.DrawnHeight(c.x, c.z), c.z);
        Log($"{name}: down at t={t:0.00} dead={body.Dead} at {g}");
        Film(name, g, Vector3.forward, 2.5f, 1.5f);
    }

    static bool BloodyDrops()
    {
        foreach (var ps in blood.GetComponentsInChildren<ParticleSystem>()) if (ps.particleCount > 0) return true;
        return false;
    }

    /// <summary>
    /// From the moment of first blood: a fixed camera to the side of and above where it is going, frames at the
    /// Moments after; the chase camera's view at 1 s; then a close look at the marks at the end, and a strip.
    /// </summary>
    static void Film(string name, Vector3 at, Vector3 fwd, float ahead, float lift)
    {
        fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, fwd);
        Vector3 aim = at + fwd * ahead * 0.5f;
        aim.y = Slope.DrawnHeight(aim.x, aim.z);
        // up the hill and to the side, high, looking down along where it goes
        Vector3 eye = at - fwd * 6f + side * 2.8f;
        eye.y = Mathf.Max(at.y + 3.5f, Slope.DrawnHeight(eye.x, eye.z) + 3.2f) + lift;
        float t = 0f; int k = 0; bool chaseShot = false;
        var frames = new List<string>();
        float end = Moments[Moments.Length - 1];
        while (t <= end + 1e-3f)
        {
            if (!chaseShot && t >= 1f)
            {
                chase.enabled = true; late.Invoke(chase, null);
                Shoot($"{name}_chase.png"); chaseShot = true;
                Cost(name + " chase");
            }
            if (k < Moments.Length && t >= Moments[k] - 1e-3f)
            {
                Look(eye, aim, 48f);
                string f = $"{name}_t{Moments[k]:0.0}.png";
                Shoot(f); frames.Add(f);
                k++;
                chase.enabled = true;
            }
            Step(); t += Dt;
        }
        // a close look, low and down the hill, at the middle of where it bled
        Vector3 mid = MarksMiddle(at);
        Vector3 ceye = mid + fwd * 2.6f + side * 1.2f + Vector3.up * 1.5f;
        Look(ceye, mid, 45f); Shoot($"{name}_close.png");
        Look(mid + Vector3.up * 5.5f + side * 0.6f - fwd * 0.6f, mid, 45f); Shoot($"{name}_top.png");
        Sheet($"{name}_strip.png", frames.ToArray());
        Log($"{name}: {blood.Shown} marks after {end}s, {struck} body contacts");
    }

    static Vector3 MarksMiddle(Vector3 fallback)
    {
        var mid = typeof(Blood).GetMethod("MarksMiddle", BindingFlags.Public | BindingFlags.Instance);
        if (mid != null) return (Vector3)mid.Invoke(blood, new object[] { fallback });
        Vector3 sum = Vector3.zero; int n = 0;
        var root = blood.transform.Find("BloodMarks");
        if (root != null) foreach (Transform m in root) if (m.gameObject.activeSelf && (m.position - fallback).sqrMagnitude < 100f) { sum += m.position; n++; }
        return n > 0 ? sum / n : fallback;
    }

    // ---------------------------------------------------------------- stepping, and the stand-in for collision messages

    static void Step()
    {
        for (int k = 0; k < 2; k++)
        {
            riderLastV = rider.Body.linearVelocity;
            foreach (var rb in Parts()) lastV[rb] = rb.linearVelocity;
            rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f);
            Contacts();
        }
        tUpd.Invoke(terrain, null);
        if (!holdForest) fUpd.Invoke(props, null);
        body.Apply(Dt);
        late.Invoke(chase, null);
        Frame(Dt);
        foreach (var ps in blood.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(Dt, true, false, false);
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(Dt, true, false, false);
        if (advance != null)
        {
            bloodTime.Start(); advance.Invoke(blood, new object[] { Dt }); bloodTime.Stop(); bloodCalls++;
        }
    }

    static void Frame(float dt)
    {
        TreeWind.Advance(dt, cam.transform.position);
        weather.Step(dt);
        aLate.Invoke(atmo, null);
        dayNight.ApplyNow();
        foreach (var ps in atmo.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(dt, false, false, false);
        foreach (var ps in weather.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(dt, false, false, false);
        foreach (var ps in cam.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(dt, false, false, false);
        if (lights != null) foreach (var ps in lights.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(dt, false, false, false);
    }

    static List<Rigidbody> Parts()
    {
        var list = new List<Rigidbody>();
        var rd = body.RagdollForTest;
        if (rd == null || !rd.Active) return list;
        list.AddRange(rd.Bodies);
        return list;
    }

    static void Contacts()
    {
        if (!rider.Crashed)
        {
            Vector3 rp = rider.Body.position;
            foreach (var c in Physics.OverlapSphere(rp, Rider.R + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            {
                var ob = c.GetComponentInParent<Obstacle>();
                if (ob == null) continue;
                Vector3 cp = c.ClosestPoint(rp), n = (rp - cp).sqrMagnitude > 1e-6f ? (rp - cp).normalized : -riderLastV.normalized;
                float into = Mathf.Abs(Vector3.Dot(riderLastV, n));
                typeof(Rider).GetProperty("HitCollider").SetValue(rider, c);
                typeof(Rider).GetProperty("HitPoint").SetValue(rider, cp);
                rider.HitObstacle?.Invoke(ob, c.bounds.center, into);
                riderHit = true; lastHitAt = cp; lastHitN = n;
                if (into > 6f) rider.DoCrash(ob.label);
                break;
            }
        }
        var rd = body.RagdollForTest;
        foreach (var rb in Parts())
        {
            var mine = rb.GetComponent<Collider>();
            if (!touching.TryGetValue(rb, out var was)) touching[rb] = was = new HashSet<Collider>();
            var now = new HashSet<Collider>();
            foreach (var c in Physics.OverlapSphere(rb.worldCenterOfMass, 0.45f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == mine || c == rider.GetComponent<Collider>()) continue;
                if (c.attachedRigidbody != null && c.attachedRigidbody != rb && !c.attachedRigidbody.isKinematic) continue;
                if (c.attachedRigidbody != null && c.attachedRigidbody.isKinematic && !rd.IsSnow(c)) continue;
                Vector3 onIt = c.ClosestPoint(rb.worldCenterOfMass), onMe = mine.ClosestPoint(onIt);
                float gap = (onMe - onIt).magnitude;
                if (gap > 0.04f) continue;
                now.Add(c);
                if (was.Contains(c)) continue;
                Vector3 n = gap > 1e-4f ? (onMe - onIt) / gap : (rb.worldCenterOfMass - onIt).normalized;
                if (rd.IsSnow(c)) n = c.transform.up;
                Vector3 v = lastV.TryGetValue(rb, out var lv) ? lv : rb.linearVelocity;
                float into = Mathf.Abs(Vector3.Dot(v, n));
                struck++;
                if (!rd.IsSnow(c) && into > 6f) { lastHitAt = onIt; lastHitN = n; Log($"part {rb.name} struck {c.name} at {into:0.0} m/s"); }
                rd.Struck?.Invoke(c, onIt, n, into, rb.transform);
            }
            touching[rb] = now;
        }
    }

    // ---------------------------------------------------------------- pictures

    static void Look(Vector3 from, Vector3 to, float fov)
    {
        chase.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
    }

    static void Log(string s) => Debug.Log("BLOODSNOW " + s);

    /// <summary>
    /// What the marks cost in this view: fragments drawn by the marks' mesh (each counted once, depth test off: an
    /// upper bound), in screens of the phone's view; and the draw calls (the marks' one mesh, and a rock's pieces).
    /// </summary>
    static void Cost(string name)
    {
        var r = blood.transform.Find("BloodMarks")?.GetComponent<MeshRenderer>();
        var sh = Shader.Find("Hidden/NightLightsCount");
        if (r == null || sh == null || !(r.sharedMaterial != null && r.sharedMaterial.shader.name == "Summit/BloodSnow")) return;
        var count = new Material(sh);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf);
        var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        bool post = data != null && data.renderPostProcessing;
        int oldMask = cam.cullingMask; var oldClear = cam.clearFlags; var oldBg = cam.backgroundColor;
        int layer = r.gameObject.layer; var mat = r.sharedMaterial; bool was = r.enabled;
        r.gameObject.layer = 31; r.sharedMaterial = count; r.enabled = true;
        if (data != null) data.renderPostProcessing = false;
        cam.cullingMask = 1 << 31; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear;
        cam.targetTexture = rt; cam.aspect = W / (float)H; cam.Render(); cam.targetTexture = null;
        var px = new Texture2D(W, H, TextureFormat.RGBAHalf, false);
        RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, W, H), 0, 0); px.Apply(); RenderTexture.active = null;
        double frags = 0; foreach (var c in px.GetPixels()) frags += c.r;
        r.gameObject.layer = layer; r.sharedMaterial = mat; r.enabled = was;
        cam.cullingMask = oldMask; cam.clearFlags = oldClear; cam.backgroundColor = oldBg;
        if (data != null) data.renderPostProcessing = post;
        Object.DestroyImmediate(px); Object.DestroyImmediate(count); rt.Release();
        int rocks = 0;
        var rf = typeof(Blood).GetField("rocks", BindingFlags.NonPublic | BindingFlags.Instance);
        if (rf != null) foreach (var o in (System.Array)rf.GetValue(blood)) { var lv = o.GetType().GetField("live"); var pcs = (System.Collections.ICollection)o.GetType().GetField("pieces").GetValue(o); if ((bool)lv.GetValue(o)) rocks += pcs.Count; }
        Log($"cost {name}: marks {frags / (W * H):0.000} screens of fragments; draws: 1 marks + {rocks} rock pieces");
    }

    /// <summary>The CPU a hard hit on the snow costs (the spray's arcs worked out, a stain laid): ms a hit, in the editor.</summary>
    static void HitCost()
    {
        Vector3 at = rider.transform.position; at.y = Slope.DrawnHeight(at.x, at.z);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 40; i++) blood.Hit(at + Vector3.forward * (i * 2f), Vector3.up, 22f, true, new Vector3(0f, -6f, 20f), null);
        sw.Stop();
        Log($"hit CPU: {sw.Elapsed.TotalMilliseconds / 40.0:0.000} ms for a hard hit on the snow (editor)");
        blood.Clear();
    }

    static void Shoot(string file)
    {
        DynamicGI.UpdateEnvironment();
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; hidden.Add(sk); baked.Add(g);
        }
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
        typeof(Blood).GetMethod("Draw", BindingFlags.Public | BindingFlags.Instance)?.Invoke(blood, null);   // the blood on rocks
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H; cam.Render();
        if (file != null)
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }

    static void Sheet(string file, params string[] names)
    {
        int w = W / 2, h = H / 2;
        var sheet = new Texture2D(w * names.Length, h, TextureFormat.RGB24, false);
        for (int k = 0; k < names.Length; k++)
        {
            var p = Path.Combine(outDir, names[k]);
            if (!File.Exists(p)) continue;
            var t = new Texture2D(2, 2);
            if (!t.LoadImage(File.ReadAllBytes(p))) continue;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    sheet.SetPixel(k * w + x, y, t.GetPixel(x * 2, y * 2));
            Object.DestroyImmediate(t);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }
}
