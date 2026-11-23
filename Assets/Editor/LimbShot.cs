using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Limbs torn off by the worst hits (RiderBodyLimbs). Run: Unity -batchmode -projectPath . -executeMethod
/// LimbShot.Run -logFile x.log (no -nographics: it renders). Writes LimbShot*.png into $SUMMIT_OUT (default
/// LimbRenders/). SUMMIT_OLDHERO=1 runs it on the previous rider (LimbShotOld*).
///
/// First a tally: natural crashes (into trees, tumbles on the snow, big falls) at a range of speeds, with the
/// hardest limb hits and whatever came off, to check it stays rare. Then the shots: an arm into a tree and a leg in
/// a big fall, gore on and off, at the moment it comes off and once the body has stopped; the end screen with
/// the bill; and the next run's rider, whole again, with the checks that nothing is left over.
///
/// Batchmode sends no collision messages, so (as BloodShot) this stands in for them every physics step. Where a
/// shot needs a particular limb and the crash didn't take it, it is torn off by hand at the first hard hit
/// (RiderBody.Sever, the same call the game makes).
/// </summary>
public static class LimbShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;

    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static RiderRig rig;
    static Camera cam; static ChaseCamera chase; static MethodInfo late, tUpd, fUpd; static Atmosphere atmo; static Blood blood; static SnowFX fx;
    static readonly Dictionary<Rigidbody, HashSet<Collider>> touching = new Dictionary<Rigidbody, HashSet<Collider>>();
    static readonly Dictionary<Rigidbody, Vector3> lastV = new Dictionary<Rigidbody, Vector3>();
    static Vector3 riderLastV; static bool riderHit; static float riderInto;
    static string outDir, tag;
    // what the last crash did
    static float hardObj, hardSnow; static string hardWhat; static int firstObjStrike; static Vector3 strikeAt, strikeN; static Transform strikeBone;
    static readonly List<string> lostNames = new List<string>(); static Vector3 lostAt;

    public static void Run()
    {
        bool old = System.Environment.GetEnvironmentVariable("SUMMIT_OLDHERO") == "1";
        RiderBody.UseHero = !old;
        tag = old ? "LimbShotOld" : "LimbShot";
        outDir = System.Environment.GetEnvironmentVariable("SUMMIT_OUT");
        if (string.IsNullOrEmpty(outDir)) outDir = "LimbRenders";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        blood = (Blood)typeof(SummitGame).GetField("blood", NP).GetValue(game);
        fx = Object.FindFirstObjectByType<SnowFX>();
        rig = (RiderRig)typeof(RiderBody).GetField("rig", NP).GetValue(body);
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        fUpd = typeof(Forest).GetMethod("Update", NP);
        body.LimbLost += (limb, at, whole) => { lostNames.Add(limb + (whole ? " (whole)" : "")); lostAt = at; Log($"sever {limb}: mesh split {RiderBody.LastSeverMs:0.00} ms"); };
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        try { Sequence(old); }
        catch (System.Exception e) { Debug.LogError("LIMBSHOT exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void Sequence(bool old)
    {
        Log($"rider: {(body.Hero != null ? "hero" : "previous")}, blood on={blood.On}");
        ToRiding();
        float z0 = Course.First + 30f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        Shoot("Warmup", true); Shoot("Warmup2", true);   // the first renders come out unlit (and the cloth shader half-compiled)

        if (!old && System.Environment.GetEnvironmentVariable("SUMMIT_TALLY") == "1") Tally();
        if (System.Environment.GetEnvironmentVariable("SUMMIT_MODE") == "rides") { RidesCheck(old); return; }
        if (System.Environment.GetEnvironmentVariable("SUMMIT_MODE") == "phantom") { PhantomCheck(old); return; }

        // the moment it comes off: an arm into a tree, a leg in a big fall
        blood.SetOn(true);
        Arm(true, rig.LowerR, false, "ArmGore", true);
        Leg(true, rig.ThighL, true, "LegGore");
        if (!old) Arm(false, rig.LowerR, false, "ArmClean", false);

        // and then getting up and riding on: without a forearm, without a lower leg, with both legs broken,
        // gore on and off; then a second crash with the limb already gone
        foreach (bool gore in new[] { true, false })
        {
            string g = gore ? "Gore" : "Clean";
            RideOn("RideForearm" + g, gore, rig.LowerL, new Transform[0], !gore);
            RideOn("RideShin" + g, gore, rig.ShinR, new Transform[0], false);
            RideOn("RideLegsBroken" + g, gore, null, new[] { rig.ShinL, rig.ShinR }, false);
        }
        blood.SetOn(true);
        ResetCheck();
    }

    /// <summary>
    /// An eye `dist` from `at`, as near the direction `prefer` as it can be with nothing between (trees, the body's
    /// own parts, the snow); `self` (and its bone) don't count.
    /// </summary>
    static Vector3 Clear(Vector3 at, Vector3 prefer, float dist, Transform self, bool bodyBlocks = true)
    {
        prefer = prefer.sqrMagnitude > 1e-4f ? prefer.normalized : Vector3.up;
        Vector3 best = at + prefer * dist; float bestScore = -1e9f;
        for (int e = 0; e < 4; e++)
            for (int a = 0; a < 16; a++)
            {
                float el = Mathf.Lerp(15f, 75f, e / 3f) * Mathf.Deg2Rad, az = a * Mathf.PI / 8f;
                var d = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
                Vector3 eye = at + d * dist;
                if (eye.y < Slope.DrawnHeight(eye.x, eye.z) + 0.3f) continue;
                bool blocked = false;
                // not inside a tree's branches (only the trunk has a collider)
                foreach (var o in Physics.OverlapSphere(eye, 2.2f, ~0, QueryTriggerInteraction.Ignore)) if (o.GetComponentInParent<Obstacle>() != null) { blocked = true; break; }
                foreach (var h in Physics.RaycastAll(at + d * 0.08f, d, dist, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (self != null && (h.transform == self || h.transform == self.parent)) continue;
                    var hb = h.collider.attachedRigidbody;
                    if (hb != null && hb.isKinematic && body.RagdollForTest.IsSnow(h.collider)) continue;   // the slabs lie under the snow
                    if (!bodyBlocks && hb != null && !hb.isKinematic) continue;                              // the body is the subject
                    blocked = true; break;
                }
                // the snow itself in the way (the slope has no collider)
                for (float k = 0.2f; k < 1f && !blocked; k += 0.2f) { var p = at + d * dist * k; if (p.y < Slope.DrawnHeight(p.x, p.z) - 0.05f) blocked = true; }
                float score = Vector3.Dot(d, prefer) - (blocked ? 10f : 0f);
                if (score > bestScore) { bestScore = score; best = eye; }
            }
        return best;
    }

    // ---- SUMMIT_MODE=rides: bones broken, get up and ride on, crash after crash, on each ride (the bandage glitch)

    static string PathOf(Transform t) { string p = t.name; for (var q = t.parent; q != null && q != body.transform; q = q.parent) p = q.name + "/" + p; return p; }

    /// <summary>The biggest thing drawn on the rider this frame (bandages, stumps, gear, the rides), by its world bounds.</summary>
    static float Biggest(out string what)
    {
        float best = 0f; what = "";
        foreach (var r in body.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.GetComponentInParent<SkinnedMeshRenderer>() == null && r.name.StartsWith("Trail")) continue;
            float sz = r.bounds.size.magnitude;
            if (sz > best) { best = sz; what = $"{PathOf(r.transform)} lossy {r.transform.lossyScale.x:0.###}"; }
        }
        return best;
    }

    static void ListBig(string name, float over)
    {
        foreach (var r in body.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            float sz = r.bounds.size.magnitude;
            if (sz > over) Log($"{name}:   BIG {PathOf(r.transform)} size {sz:0.00} m, lossy {r.transform.lossyScale}, local {r.transform.localScale}");
        }
    }

    static void RidesCheck(bool old)
    {
        string tagged = System.Environment.GetEnvironmentVariable("SUMMIT_TAG") ?? "";
        blood.SetOn(true);
        var brk = typeof(RiderBody).GetMethod("Break", NP);
        foreach (var kind in new[] { RideKind.Snowboard, RideKind.Skis, RideKind.Toboggan })
        {
            body.SetRide(kind); rider.SetRide(body.Ride);
            if (old && kind != RideKind.Snowboard) break;   // (the previous rider has only the board)
            string name = "Rides" + tagged + body.Ride;
            Log($"{name}: riding {body.Ride} ({(old ? "previous rider" : "hero")})");
            // bandages on and how big the wrapped bones are, from the start
            foreach (var t in new[] { rig.Head, rig.HandR, rig.HandL, rig.ShinL }) Log($"{name}: bone {t.name} lossy {t.lossyScale.x:0.####}");
            float worst = 0f; string worstWhat = "";
            for (int crash = 1; crash <= 3; crash++)
            {
                typeof(SummitGame).GetField("falls", NP).SetValue(game, 0);
                typeof(SummitGame).GetField("wipeoutTimer", NP).SetValue(game, 0f);
                Random.InitState(7 + crash);
                SnowRun(17f, 1.5f, Course.First + 200f + crash * 60f, 0.5f);
                if (crash == 1) foreach (var b in new[] { rig.ShinL, rig.ShinR, rig.LowerR, rig.Head }) brk.Invoke(body, new object[] { b, b.position });
                Settle();
                float up = GetUp();
                if (rider.Crashed) { Log($"{name}: crash {crash}: NEVER GOT UP"); break; }
                float most = 0f; string mostWhat = "";
                for (float t = 0f; t < 2f; t += Dt)
                {
                    rider.Steer = Mathf.Sin(t * 1.7f) * 0.6f; Step();
                    float b = Biggest(out var w); if (b > most) { most = b; mostWhat = w; }
                }
                Log($"{name}: crash {crash}: up after {up:0.0} s; riding at {rider.Speed * 3.6f:0} km/h; biggest thing on the rider {most:0.00} m ({mostWhat})");
                ListBig(name, 2.2f);
                if (most > worst) { worst = most; worstWhat = mostWhat; }
                if (crash == 1 || crash == 3)
                {
                    Vector3 mid = rig.Hips.position, f = rider.Velocity; f.y = 0f; f = f.sqrMagnitude > 0.1f ? f.normalized : Vector3.forward;
                    Vector3 right = Vector3.Cross(Vector3.up, f);
                    Look(mid + right * 3.6f + f * 1.0f + Vector3.up * 0.5f, mid, 50f); Shoot(name + "Crash" + crash + "Side", true);
                    // the game's own chase camera, as the player sees it
                    chase.enabled = true; late.Invoke(chase, null); Shoot(name + "Crash" + crash + "Chase", true);
                }
            }
            Log($"{name}: WORST {worst:0.00} m ({worstWhat})");
        }
        if (!old)
        {
            // first person, on skis, after the breaks: the head (and its bandage) has to stay out of the lens
            body.SetRide(RideKind.Skis); rider.SetRide(body.Ride);
            chase.pov = true;
            typeof(SummitGame).GetField("falls", NP).SetValue(game, 0);
            Random.InitState(3);
            SnowRun(17f, 1.5f, Course.First + 420f, 0.5f);
            foreach (var b in new[] { rig.ShinL, rig.Head }) brk.Invoke(body, new object[] { b, b.position });
            Settle();
            povFirst = true; GetUp();
            float most = 0f; string mostWhat = "";
            for (float t = 0f; t < 2f; t += Dt) { rider.Steer = Mathf.Sin(t * 1.7f) * 0.6f; Step(); float b = Biggest(out var w); if (b > most) { most = b; mostWhat = w; } }
            float headBand = 0f; foreach (Transform c in rig.Head) if (c.name.StartsWith("Bandage")) headBand = Mathf.Max(headBand, c.GetComponent<Renderer>().bounds.size.magnitude);
            Log($"RidesPov{tagged}: in first person: biggest {most:0.00} m ({mostWhat}); head lossy {rig.Head.lossyScale.x:0.####}, head bandage {headBand:0.00} m (want ~0: hidden with the head)");
            chase.enabled = true; late.Invoke(chase, null); Shoot("RidesPov" + tagged, true);
            povFirst = false; chase.pov = false; body.SetPovHead(false);
            headBand = 0f; foreach (Transform c in rig.Head) if (c.name.StartsWith("Bandage")) headBand = Mathf.Max(headBand, c.GetComponent<Renderer>().bounds.size.magnitude);
            Log($"RidesPov{tagged}: back in the chase view: head lossy {rig.Head.lossyScale.x:0.####}, head bandage {headBand:0.00} m (want ~0.3)");
            Look(rig.Head.position + rider.Velocity.normalized * 1.6f + Vector3.up * 0.4f, rig.Head.position, 40f); Shoot("RidesPovAfter" + tagged, true);
        }
        body.SetRide(RideKind.Snowboard); rider.SetRide(body.Ride);
    }

    /// <summary>
    /// SUMMIT_MODE=phantom: on every ride (board, skis, toboggan, tube), lose all four limbs over two falls in a row
    /// (a forearm and a lower leg, with the other shin and the head broken; then the other arm and the broken leg
    /// whole), getting up and riding on after each, then a third fall with nothing left to lose. After each get-up,
    /// what's on the rider: stumps, cut meshes, parts left asleep, hands let go, bandages (soaked ones) and any not
    /// on their bone; and shots from behind (the game's chase camera), the side, close, and in first person, by day
    /// and by night. SUMMIT_TAG labels the files (Before / After).
    /// </summary>
    static void PhantomCheck(bool old)
    {
        string tg = System.Environment.GetEnvironmentVariable("SUMMIT_TAG") ?? "";
        var dayNight = (DayNight)typeof(SummitGame).GetField("dayNight", NP).GetValue(game);
        var brk = typeof(RiderBody).GetMethod("Break", NP);
        void SetTime(bool night) { TimeOfDay.TestClock = night ? 3.4f : 1.4f; dayNight.ApplyNow(); }
        foreach (string rn in old ? new[] { "Snowboard" } : new[] { "Snowboard", "Skis", "Toboggan", "Tube" })
        {
            bool tube = rn == "Tube";
            body.SetRide(tube ? RideKind.Toboggan : (RideKind)System.Enum.Parse(typeof(RideKind), rn)); rider.SetRide(body.Ride);
            rider.Tube = tube; body.SetTube(tube);
            string name = "Phantom" + tg + rn;
            blood.SetOn(true); SetTime(false);
            for (int crash = 1; crash <= 3; crash++)
            {
                typeof(SummitGame).GetField("falls", NP).SetValue(game, 0);
                typeof(SummitGame).GetField("wipeoutTimer", NP).SetValue(game, 0f);
                Random.InitState(5 + crash);
                if (crash == 1) SnowRun(17f, 1.5f, Course.First + 200f, 0.3f);   // (fresh: everything back on)
                else { rider.DoCrash("test"); for (float t = 0f; t < 0.3f; t += Dt) Step(); }
                if (crash == 1)
                {
                    Log($"{name}: crash 1: left forearm {body.Sever(rig.LowerL, false, Vector3.up)}, right lower leg {body.Sever(rig.ShinR, false, Vector3.up)}");
                    foreach (var b in new[] { rig.ShinL, rig.Head }) brk.Invoke(body, new object[] { b, b.position });
                }
                else if (crash == 2) Log($"{name}: crash 2: right arm {body.Sever(rig.UpperR, true, Vector3.up)}, left leg (broken last time) {body.Sever(rig.ThighL, true, Vector3.up)}");
                else Log($"{name}: crash 3: left arm again {body.Sever(rig.UpperL, true, Vector3.up)} (want False: lost already)");
                Settle();
                bool pov = !old && crash == 2;
                if (pov) { chase.pov = true; povFirst = true; }
                float up = GetUp();
                if (rider.Crashed) { Log($"{name}: crash {crash}: NEVER GOT UP"); break; }
                float most = 0f; string mostWhat = "";
                for (float t = 0f; t < 2f; t += Dt) { rider.Steer = Mathf.Sin(t * 1.7f) * 0.6f; Step(); float b = Biggest(out var w); if (b > most) { most = b; mostWhat = w; } }
                Log($"{name}: crash {crash}: up after {up:0.0} s, riding at {rider.Speed * 3.6f:0} km/h; biggest {most:0.00} m ({mostWhat})");
                PhantomTally(name + $" crash {crash}");
                if (pov)
                {
                    // first person, as the player sees it, day and night
                    foreach (bool night in new[] { false, true }) { SetTime(night); chase.enabled = true; late.Invoke(chase, null); Shoot(name + "Pov" + (night ? "Night" : "Day"), true); }
                    SetTime(false);
                    povFirst = false; chase.pov = false; body.SetPovHead(false);
                    for (int i = 0; i < 3; i++) Step();
                }
                if (crash >= 2)
                {
                    string c = "Crash" + crash;
                    Vector3 mid = rig.Hips.position, f = rider.Velocity; f.y = 0f; f = f.sqrMagnitude > 0.1f ? f.normalized : Vector3.forward;
                    Vector3 right = Vector3.Cross(Vector3.up, f);
                    foreach (bool night in crash == 2 ? new[] { false, true } : new[] { false })
                    {
                        SetTime(night); string dn = night ? "Night" : "Day";
                        chase.enabled = true; late.Invoke(chase, null); Shoot(name + c + "Chase" + dn, true);
                        Look(mid + right * 3.6f + f * 1.0f + Vector3.up * 0.5f, mid, 50f); Shoot(name + c + "Side" + dn, true);
                        Look(mid - f * 2.6f + right * 0.8f + Vector3.up * 1.0f, mid, 50f); Shoot(name + c + "Back" + dn, true);
                    }
                    SetTime(false);
                    if (crash == 2) { Look(mid + right * 1.9f + f * 0.9f + Vector3.up * 0.3f, mid, 45f); Shoot(name + c + "Close", true); }
                    if (crash == 3 && rn == "Snowboard")
                    {
                        // gore off: the same bandages, clean
                        blood.SetOn(false); Step();
                        Look(mid + right * 1.9f + f * 0.9f + Vector3.up * 0.3f, mid, 45f); Shoot(name + c + "CloseClean", true);
                        blood.SetOn(true); Step();
                    }
                }
            }
            rider.Tube = false; body.SetTube(false);
        }
        TimeOfDay.TestClock = -1f;
        body.SetRide(RideKind.Snowboard); rider.SetRide(body.Ride);
    }

    /// <summary>What the rider rides on with: anything missing, cut, asleep, let go, or not where it's drawn.</summary>
    static void PhantomTally(string name)
    {
        int stumps = 0; foreach (var c in body.LimbCaps) if (c != null && c.transform.IsChildOf(body.transform)) stumps++;
        int cut = 0; foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.enabled && smr.sharedMesh != null && smr.sharedMesh.name.EndsWith("(cut)")) cut++;
        int asleep = 0; var rd = body.RagdollForTest;
        foreach (var b in new[] { rig.UpperL, rig.LowerL, rig.HandL, rig.UpperR, rig.LowerR, rig.HandR, rig.ThighL, rig.ShinL, rig.ThighR, rig.ShinR }) if (rd != null && rd.Gone(b)) asleep++;
        int joints = 0; if (rd != null) foreach (var rb in rd.Bodies) if (rb.GetComponent<CharacterJoint>() != null) joints++;
        int pieces = 0; foreach (var _ in body.LostPieces) pieces++;
        var per = new SortedDictionary<string, int>(); int heavy = 0, astray = 0; float far = 0f;
        foreach (var r in body.GetComponentsInChildren<Renderer>())
        {
            if (!r.name.StartsWith("Bandage")) continue;
            if (r.name.StartsWith("BandageHeavy")) { heavy++; string k = r.transform.parent.name; per[k] = per.TryGetValue(k, out var n) ? n + 1 : 1; }
            float d = (r.bounds.center - r.transform.parent.position).magnitude;
            far = Mathf.Max(far, d);
            if (d > 0.6f || !r.enabled || !r.gameObject.activeInHierarchy) astray++;
        }
        var soakedOn = new List<string>(); foreach (var kv in per) soakedOn.Add($"{kv.Key} {kv.Value}");
        Log($"{name}: lost {body.LimbsLost}, pieces on the snow {pieces}; on the rider: stumps {stumps}, cut meshes {cut}, parts asleep {asleep}, joints {joints} (want {CountParts() - 1}), hand gone L {body.HandGone(0)} R {body.HandGone(1)}; soaked wraps {heavy} [{string.Join(", ", soakedOn)}], bandages astray {astray} (farthest from its bone {far:0.00} m)");
    }

    static void ToRiding()
    {
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
    }

    // ---- how often it happens on its own

    static void Tally()
    {
        blood.SetOn(true);
        int n = 0, cut = 0;
        void Note(string what)
        {
            n++; if (lostNames.Count > 0) cut++;
            Log($"TALLY {what}: rider into obstacle {riderInto:0.0}, hardest limb hit obstacle {hardObj:0.0} snow {hardSnow:0.0} m/s ({hardWhat}), dead={body.Dead}; lost: {(lostNames.Count > 0 ? string.Join(", ", lostNames) : "nothing")}");
        }
        for (int seed = 1; seed <= 3; seed++)
            foreach (float v in new[] { 16f, 22f, 28f })
            {
                Random.InitState(seed * 31 + (int)v);
                if (TreeRun(v, Course.First + 160f + seed * 140f, 4f, null, null)) Note($"tree {v} m/s seed {seed}");
            }
        for (int seed = 1; seed <= 3; seed++)
            foreach (float v in new[] { 16f, 24f, 32f })
            {
                Random.InitState(seed * 53 + (int)v);
                SnowRun(v, 0f, Course.First + 300f + seed * 120f, 4f);
                Note($"tumble {v} m/s seed {seed}");
            }
        for (int seed = 1; seed <= 3; seed++)
            foreach (float up in new[] { 8f, 16f })
            {
                Random.InitState(seed * 71 + (int)up);
                SnowRun(24f, up, Course.First + 700f + seed * 60f, 4f);
                Note($"fall from {up} m at 24 m/s seed {seed}");
            }
        Log($"TALLY {cut} of {n} crashes lost a limb");
    }

    static void Clear()
    {
        hardObj = hardSnow = riderInto = 0f; hardWhat = ""; firstObjStrike = -1; strikeBone = null; lostNames.Clear();
        touching.Clear(); lastV.Clear();
    }

    /// <summary>Find a lone tree near z and ride flat out into it.</summary>
    static bool TreeRun(float speed, float near, float seconds, Transform force, System.Action<float> each)
    {
        rider.ResetAt(Slope.Centre(near), near); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm(); fUpd.Invoke(props, null);
        Obstacle best = null; float bestD = 1e9f;
        foreach (var ob in Object.FindObjectsByType<Obstacle>(FindObjectsSortMode.None))
        {
            if (!ob.gameObject.activeInHierarchy || ob.label != "Tree") continue;
            var col = ob.GetComponentInChildren<Collider>(); if (col == null || !col.enabled) continue;
            var p = col.bounds.center;
            if (p.z < near + 10f) continue;
            float d = Mathf.Abs(p.z - near - 25f) + Mathf.Abs(p.x - Slope.Centre(p.z)) * 0.5f + (Mathf.Abs(p.x - Slope.Centre(p.z)) > Slope.PisteHalf ? 200f : 0f);
            if (d >= bestD) continue;
            Vector3 st = p + Vector3.back * (col.bounds.extents.z + 1.4f); st.y = Slope.DrawnHeight(st.x, st.z) + 0.5f;
            bool clear = true;
            foreach (var o in Physics.OverlapSphere(st, 1.2f, ~0, QueryTriggerInteraction.Ignore)) if (o != col && o.GetComponentInParent<Obstacle>() != null) clear = false;
            if (!clear) continue;
            bestD = d; best = ob;
        }
        if (best == null) { Log($"no tree near z={near}"); return false; }
        var bcol = best.GetComponentInChildren<Collider>();
        var target = bcol.bounds.center;
        Vector3 start = target + Vector3.back * (bcol.bounds.extents.z + 1.4f);
        rider.ResetAt(start.x, start.z); body.ResetVisual(); rider.Begin();
        Vector3 to = target - rider.Body.position; to.y = 0f; to.Normalize();
        rider.Heading = Mathf.Atan2(to.x, to.z);
        rider.Body.linearVelocity = to * speed + Vector3.down * 2f;
        terrain.Prewarm(); props.Prewarm();
        chase.enabled = true; chase.Snap();
        blood.Clear(); Clear(); riderHit = false;
        float t0 = -1f;
        for (float t = 0f; t < seconds; t += Dt)
        {
            rider.Steer = 0f;
            Step();
            if (riderHit && t0 < 0f) t0 = t;
            if (force != null && body.LimbsLost == 0 && rider.Crashed && (firstObjStrike >= 0 || (t0 >= 0f && t - t0 > 0.25f)))
            {
                Vector3 n = strikeN.sqrMagnitude > 0.01f ? strikeN : -to;
                body.Sever(force, force == rig.UpperL || force == rig.UpperR || force == rig.ThighL || force == rig.ThighR, n);
            }
            each?.Invoke(t);
        }
        if (t0 < 0f) { Log("never reached the tree"); return false; }
        return true;
    }

    /// <summary>A crash on open snow at this speed, launched from `up` metres above it.</summary>
    static void SnowRun(float speed, float up, float z, float seconds, Transform force = null, System.Action<float> each = null)
    {
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        var p = rider.Body.position + Vector3.up * up;
        rider.Body.position = p; rider.transform.position = p;
        rider.Body.linearVelocity = new Vector3(0f, up > 0f ? -5f : -1f, speed);
        body.Apply(Dt);
        rider.DoCrash("test");
        blood.Clear(); Clear();
        chase.enabled = true; chase.Snap();
        bool bigHit = false;
        for (float t = 0f; t < seconds; t += Dt)
        {
            Step();
            if (hardSnow > 6f) bigHit = true;
            if (force != null && body.LimbsLost == 0 && (bigHit || t > 1.2f))
                body.Sever(force, force == rig.UpperL || force == rig.UpperR || force == rig.ThighL || force == rig.ThighR, Vector3.up);
            each?.Invoke(t);
        }
    }

    // ---- the shots

    static void Arm(bool gore, Transform cut, bool whole, string name, bool withScreens)
    {
        blood.SetOn(gore);
        Random.InitState(5);
        float lostT = -1f; bool impactShot = false; stumpShot = false;
        bool ok = false;
        if (withScreens) { typeof(SummitGame).GetField("medical", NP).SetValue(game, 0f); }
        for (int i = 0; i < 4 && !ok; i++)
        {
            float near = Course.First + 160f + i * 150f;
            lostT = -1f; impactShot = false; stumpShot = false;
            ok = TreeRun(21f, near, 6f, cut, t =>   // (below RiderBody.SeverRider: the shot takes the limb it asks for)
            {
                if (body.LimbsLost > 0 && lostT < 0f) lostT = t;
                if (lostT >= 0f && !impactShot && t - lostT >= 0.07f)
                {
                    impactShot = true;
                    ImpactShot(name, withScreens);
                }
                if (lostT >= 0f && !stumpShot && t - lostT >= 0.5f) { stumpShot = true; StumpShot(name); }
            });
            if (ok && lostT < 0f) { Log($"{name}: nothing came off"); ok = false; }
        }
        Log($"{name}: lost {string.Join(", ", lostNames)} at t={lostT:0.00}; hardest obstacle {hardObj:0.0} snow {hardSnow:0.0}");
        Settle(); Aftermath(name);
    }

    static void Leg(bool gore, Transform cut, bool whole, string name)
    {
        blood.SetOn(gore);
        Random.InitState(11);
        float lostT = -1f; bool impactShot = false; stumpShot = false;
        SnowRun(24f, 9f, Course.First + 250f, 6f, cut, t =>
        {
            if (body.LimbsLost > 0 && lostT < 0f) lostT = t;
            if (lostT >= 0f && !impactShot && t - lostT >= 0.07f) { impactShot = true; ImpactShot(name, false); }
            if (lostT >= 0f && !stumpShot && t - lostT >= 0.5f) { stumpShot = true; StumpShot(name); }
        });
        Log($"{name}: lost {string.Join(", ", lostNames)} at t={lostT:0.00}; hardest snow {hardSnow:0.0}");
        Settle(); Aftermath(name);
    }

    static bool stumpShot, povFirst;

    /// <summary>The stump and the piece half a second on, while they tumble apart.</summary>
    static void StumpShot(string name)
    {
        var caps = new List<GameObject>(body.LimbCaps);
        if (caps.Count == 0) return;
        Vector3 s = caps[0].transform.position;
        Look(Clear(s, caps[0].transform.up + Vector3.up * 0.4f, 1.6f, caps[0].transform), s, 45f);
        Shoot(name + "Tumble", true);
    }

    static void ImpactShot(string name, bool hudToo)
    {
        Vector3 c = lostAt;
        Vector3 v = body.RagdollForTest != null ? body.RagdollForTest.Velocity : Vector3.forward; v.y = 0f;
        Vector3 along = v.sqrMagnitude > 0.1f ? v.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, along);
        Look(Clear(c, side * 3.2f - along * 1.2f + Vector3.up * 1.2f, 3.4f, null, false), c, 45f);
        Shoot(name + "Impact", true, hudToo);
        Log($"{name}: impact shot, {blood.Shown} marks");
    }

    /// <summary>Run on until the body and the torn-off piece have stopped.</summary>
    static void Settle()
    {
        for (float t = 0f; t < 6f; t += Dt)
        {
            Step();
            if (t > 2.5f && (body.RagdollForTest == null || body.RagdollForTest.Speed < 0.3f)) break;
        }
    }

    static void Aftermath(string name)
    {
        var caps = new List<GameObject>(body.LimbCaps);
        Vector3 c = body.RagdollCentre;
        GameObject stump = caps.Count > 0 ? caps[0] : null, end = caps.Count > 1 ? caps[1] : null;
        Vector3 piece = end != null ? end.transform.position : c;
        Log($"{name}: body at {c}, piece at {piece} ({(piece - c).magnitude:0.0} m apart), caps {caps.Count}, marks {blood.Shown}");
        Vector3 mid = (c + piece) * 0.5f;
        float spread = Mathf.Max(2.5f, (piece - c).magnitude);
        // the whole scene: the body and the piece, from wherever nothing is in the way
        // from beyond the body, looking over it at the piece, so the two stack up the tall frame
        Vector3 line = piece - c; line.y = 0f; line = line.sqrMagnitude > 0.01f ? line.normalized : Vector3.forward;
        Look(Clear(mid, -line + Vector3.up * 1.1f, spread * 0.9f + 3f, null, false), mid, 60f);
        Shoot(name + "After", true);
        if (stump != null)
        {
            Vector3 s = stump.transform.position;
            Look(Clear(s, stump.transform.up + Vector3.up * 0.5f, 1.3f, stump.transform), s, 40f); Shoot(name + "Stump", true);
        }
        if (end != null)
        {
            Vector3 s = end.transform.position;
            Look(Clear(s, end.transform.up + Vector3.up * 0.5f, 1.2f, end.transform), s, 40f); Shoot(name + "Piece", true);
        }
    }

    /// <summary>
    /// Crash, lose this limb (or break these bones), get up and ride on: shots of the rider back on the board from
    /// the side and from behind, close on what's missing or bandaged, and the piece left on the snow. `again`: then
    /// crash once more, with the limb already gone, and get up from that too.
    /// </summary>
    static void RideOn(string name, bool gore, Transform cut, Transform[] breaks, bool again)
    {
        blood.SetOn(gore);
        Random.InitState(7);
        // a fresh count of falls: the game's limit (three) would keep this one down otherwise
        typeof(SummitGame).GetField("falls", NP).SetValue(game, 0);
        typeof(SummitGame).GetField("wipeoutTimer", NP).SetValue(game, 0f);
        float z = Course.First + 250f;
        SnowRun(17f, 1.5f, z, 0.5f);
        if (cut != null && !body.Sever(cut, false, Vector3.up)) Log($"{name}: couldn't take {cut.name}");
        var brk = typeof(RiderBody).GetMethod("Break", NP);
        foreach (var b in breaks) brk.Invoke(body, new object[] { b, b.position });
        Settle();
        float up = GetUp();
        if (rider.Crashed) { Log($"{name}: NEVER GOT UP (stay down {rider.StayDown}, hold {rider.HoldCrash})"); return; }
        int pieces = 0; foreach (var _ in body.LostPieces) pieces++;
        int wraps = 0; foreach (var t in body.GetComponentsInChildren<Transform>()) if (t.name.StartsWith("BandageHeavy") || t.name.StartsWith("BandageLight")) wraps++;
        int hidden = 0; foreach (var r in body.GetComponentsInChildren<Renderer>(true)) if (!r.gameObject.activeInHierarchy) hidden++;
        Log($"{name}: up after {up:0.0} s; lost {body.LimbsLost}, pieces on the snow {pieces}, hand gone L {body.HandGone(0)} R {body.HandGone(1)}, bandage wraps {wraps}, renderers put away {hidden}");
        // ride on, carving
        for (float t = 0f; t < 3f; t += Dt) { rider.Steer = Mathf.Sin(t * 1.7f) * 0.7f; Step(); }
        Log($"{name}: riding at {rider.Speed * 3.6f:0} km/h, crashed {rider.Crashed}");
        RidingShots(name, cut);
        // the piece where it was left
        foreach (var pc in body.LostPieces)
        {
            var rs = pc.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var bounds = rs[0].bounds;
            foreach (var r in rs) bounds.Encapsulate(r.bounds);
            Vector3 c = bounds.center;
            Look(Clear(c, Vector3.up + Vector3.back * 0.6f, 1.6f, null), c, 40f); Shoot(name + "Left", true);
            Log($"{name}: piece at {c} size {bounds.size}, {(rider.transform.position - c).magnitude:0} m behind");
            break;
        }
        if (!again) return;
        // crash again, with the forearm gone: its parts stay asleep, and you still get up
        rider.DoCrash("test");
        int awake = 0;
        for (float t = 0f; t < 1.5f; t += Dt) Step();
        foreach (var rb in body.RagdollForTest.Bodies) if (!rb.isKinematic) awake++;
        Settle();
        float up2 = GetUp();
        Log($"{name}: crashed again: {awake} parts thrown (of {CountParts()}), up again after {up2:0.0} s: {!rider.Crashed}, lost {body.LimbsLost}");
        for (float t = 0f; t < 2f; t += Dt) { rider.Steer = Mathf.Sin(t * 1.7f) * 0.7f; Step(); }
        RidingShots(name + "Again", cut);
    }

    /// <summary>Tap to get up as soon as the game lets you (up to 10 s): how long it took.</summary>
    static float GetUp()
    {
        for (float t = 0f; t < 10f; t += Dt)
        {
            if (!rider.Crashed) return t;
            if (rider.CanGetUp) rider.RequestUp = true;
            Step();
        }
        return -1f;
    }

    static void RidingShots(string name, Transform cut)
    {
        Vector3 p = rider.transform.position;
        Vector3 f = rider.Velocity; f.y = 0f; f = f.sqrMagnitude > 0.1f ? f.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, f);
        // the rider faces the toe edge, side-on to the way they're going: from in front of their chest, a little ahead
        Vector3 chest = right;   // (the board's toe edge: the side the rider faces)
        Vector3 mid = rig.Hips.position;   // (what you see of the rider, not its physics ball)
        Look(mid + chest * 3.6f + f * 1.0f + Vector3.up * 0.4f, mid, 50f); Shoot(name + "Ride", true);
        Look(mid - f * 4.0f + right * 1.0f + Vector3.up * 1.2f, mid, 50f); Shoot(name + "RideBack", true);
        // close on what's missing (the stump) or bandaged (the legs)
        Vector3 at = cut != null ? cut.position : (rig.ShinL.position + rig.ShinR.position) * 0.5f;
        Look(at + chest * 1.6f + Vector3.up * 0.4f + f * 0.5f, at, 45f); Shoot(name + "Close", true);
    }

    static void EndScreen(string name)
    {
        string title = (string)typeof(SummitGame).GetField("endTitle", NP).GetValue(game);
        float medical = (float)typeof(SummitGame).GetField("medical", NP).GetValue(game);
        Log($"{name}: end title {title}, medical ${medical:N0}, stay down {rider.StayDown}, can get up {rider.CanGetUp}");
        typeof(SummitGame).GetMethod("ToEnd", NP).Invoke(game, new object[] { title, false, false });
        var hud = Object.FindFirstObjectByType<Hud>();
        hud.SetAlpha(1f);
        var meta = Object.FindFirstObjectByType<MetaUi>();
        meta?.SetEndAlpha(1f);
        Look(body.RagdollCentre + new Vector3(3f, 3f, -5f), body.RagdollCentre, 50f);
        Shoot(name + "EndScreen", true, true);
        ToRiding();
    }

    static void ResetCheck()
    {
        // the run before this left a limb off (ShinGore / LegClean): the next run must have the rider whole
        int capsBefore = 0; foreach (var g in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (g.name == "Stump") capsBefore++;
        Log($"before reset: {body.LimbsLost} lost, {capsBefore} stump objects");
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { true });
        int caps = 0, slabs = 0; foreach (var g in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) { if (g.name == "Stump") caps++; if (g.name == "LimbGround" && g.gameObject.activeSelf) slabs++; }
        int joints = 0, live = 0;
        var rd = body.RagdollForTest;
        foreach (var rb in rd.Bodies) { if (rb.GetComponent<CharacterJoint>() != null) joints++; if (!rb.isKinematic) live++; }
        int cutMeshes = 0;
        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.sharedMesh != null && smr.sharedMesh.name.EndsWith("(cut)")) cutMeshes++;
        bool bonesHome = Vector3.Distance(rig.HandR.position, rig.LowerR.position) < 0.5f && Vector3.Distance(rig.FootL.position, rig.ThighL.position) < 1.2f;
        Log($"RESET lost={body.LimbsLost} stumps={caps} limbSlabs={slabs} joints={joints} (want {CountParts() - 1}) live bodies={live} cut meshes={cutMeshes} bones home={bonesHome} medical={(float)typeof(SummitGame).GetField("medical", NP).GetValue(game):0}");
        ToRiding();
        float z = Course.First + 30f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        terrain.Prewarm(); props.Prewarm();
        for (int i = 0; i < 20; i++) Step();
        Vector3 p = rider.transform.position;
        Look(p + new Vector3(2.6f, 1.2f, 1.6f), p + Vector3.up * 0.5f, 40f);
        Shoot("Reset", true);
        // and crash again: the ragdoll still works with the joints rebuilt
        Random.InitState(3);
        SnowRun(18f, 0f, Course.First + 200f, 3f);
        float spread = 0f; Vector3 c0 = body.RagdollCentre;
        foreach (var rb in rd.Bodies) spread = Mathf.Max(spread, (rb.position - c0).magnitude);
        Log($"RESET crash again: lost={body.LimbsLost} body spread {spread:0.00} m (want < 1.4)");
    }

    static int CountParts() { int n = 0; foreach (var _ in body.RagdollForTest.Bodies) n++; return n; }

    // ---- stepping, and the stand-in for collision messages (as BloodShot)

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
        fUpd.Invoke(props, null);
        if (povFirst && !rider.Crashed) body.SetPovHead(true);   // the game's camera hides the head before the body is posed
        body.Apply(Dt);
        late.Invoke(chase, null);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        foreach (var ps in blood.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(Dt, true, false, false);
        blood.Advance(Dt);   // the drops land, the marks soak in
        if (fx != null) foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(Dt, true, false, false);
    }

    static List<Rigidbody> Parts()
    {
        var list = new List<Rigidbody>();
        var rd = body.RagdollForTest;
        if (rd == null || !rd.Active) return list;
        list.AddRange(rd.Bodies);
        return list;
    }

    static bool IsLimb(Transform b) => b == rig.UpperL || b == rig.UpperR || b == rig.LowerL || b == rig.LowerR || b == rig.ThighL || b == rig.ThighR || b == rig.ShinL || b == rig.ShinR;

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
                rider.HitObstacle?.Invoke(ob, c.bounds.center, into);
                riderHit = true; riderInto = Mathf.Max(riderInto, into);
                if (into > 6f) rider.DoCrash(ob.label);
                break;
            }
        }
        var rd = body.RagdollForTest;
        foreach (var rb in Parts())
        {
            var mine = rb.GetComponent<Collider>();
            if (mine == null || !mine.enabled) continue;
            if (!touching.TryGetValue(rb, out var was)) touching[rb] = was = new HashSet<Collider>();
            var now = new HashSet<Collider>();
            foreach (var c in Physics.OverlapSphere(rb.worldCenterOfMass, 0.45f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == mine || c == rider.GetComponent<Collider>()) continue;
                if (c.attachedRigidbody != null && c.attachedRigidbody != rb && !c.attachedRigidbody.isKinematic) continue;
                if (c.attachedRigidbody != null && c.attachedRigidbody.isKinematic && !rd.IsSnow(c)) continue;
                if (Physics.GetIgnoreCollision(c, mine)) continue;   // (the body's slab, for a torn-off piece)
                Vector3 onIt = c.ClosestPoint(rb.worldCenterOfMass), onMe = mine.ClosestPoint(onIt);
                float gap = (onMe - onIt).magnitude;
                if (gap > 0.04f) continue;
                now.Add(c);
                if (was.Contains(c)) continue;
                Vector3 n = gap > 1e-4f ? (onMe - onIt) / gap : (rb.worldCenterOfMass - onIt).normalized;
                bool snow = rd.IsSnow(c);
                if (snow) n = c.transform.up;
                Vector3 v = lastV.TryGetValue(rb, out var lv) ? lv : rb.linearVelocity;
                float into = Mathf.Abs(Vector3.Dot(v, n));
                if (IsLimb(rb.transform) && rd.Attached(rb.transform))
                {
                    if (snow && into > hardSnow) { hardSnow = into; hardWhat = rb.name + " snow"; }
                    if (!snow && into > hardObj) { hardObj = into; hardWhat = rb.name + " " + c.name; }
                    if (!snow && into > 6f && firstObjStrike < 0) { firstObjStrike = 1; strikeAt = onIt; strikeN = n; strikeBone = rb.transform; }
                }
                rd.Struck?.Invoke(c, onIt, n, into, rb.transform);
            }
            touching[rb] = now;
        }
    }

    static void Look(Vector3 from, Vector3 to, float fov)
    {
        chase.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
    }

    static void Log(string s) => Debug.Log("LIMBSHOT " + s);

    static void Shoot(string name, bool bakeSkins, bool hud = false)
    {
        DynamicGI.UpdateEnvironment();
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
        if (bakeSkins)
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
        Canvas canvas = null; RenderMode was = RenderMode.ScreenSpaceOverlay;
        if (hud)
        {
            canvas = Object.FindFirstObjectByType<Hud>().GetComponent<Canvas>();
            was = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
        }
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, tag + name + ".png"), tex.EncodeToPNG());
        cam.targetTexture = null;
        if (canvas != null) canvas.renderMode = was;
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }
}
