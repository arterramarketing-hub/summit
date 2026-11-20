using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The glacier's crevasses, through the game's own wiring: the layout, and from the rider's view, day and night, a
/// field coming up, a snow bridge crossed, the widest crack jumped and a fall into it (the body lying inside). Run: Unity
/// -batchmode -projectPath . -executeMethod GlacierShot.Run -logFile x.log (no -nographics: it needs the GPU).
/// Writes GlacierShot*.png in the project folder. GlacierShot.Snow: head-first falls and the snow at the lip;
/// GlacierShot.Park: the park's rails and boxes and a grind on each.
/// </summary>
public static class GlacierShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;

    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props; static CourseDressing dress;
    static Camera cam; static ChaseCamera chase; static MethodInfo late, tUpd, fUpd; static Atmosphere atmo;
    static string lastCrash = "";
    static int bad;

    public static void Run() => Boot(Sequence);

    /// <summary>
    /// Head-first falls into a crevasse, watching the snow layer round the lip (it must not heap up or
    /// be carved down into the hole), and a fall on open snow as the control (that must still leave its hole and
    /// rims). Run: -executeMethod GlacierShot.Snow. Writes CrevSnow*.png.
    /// </summary>
    public static void Snow() => Boot(SnowSequence);

    static void Boot(System.Action seq)
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>();
        late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        fUpd = typeof(Forest).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = Dt;
        rider.Crash += why => lastCrash = why;
        try { seq(); }
        catch (System.Exception e) { Debug.LogError("GLACIERSHOT exception " + e); }
        Log($"done, {bad} problems");
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void SnowSequence()
    {
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        Course.WidestCrack(out var crack, out var spot);
        Place(spot.x, spot.z - 40f);
        Shoot("CrevSnowWarmup.png", false);
        // head first, slowly enough to go in: the widest crack, and the bergschrund
        HeadFirst("Crack", crack, spot.x, spot.z - 8f, 6f, 0f);
        if (Course.WidestCrack(out var berg, out var bspot, Course.CrevasseKind.Bergschrund))
            HeadFirst("Bergschrund", berg, bspot.x, bspot.z - 8f, 5f, 0f);
        // the control: thrown on open snow (below the glacier), the body still leaves its furrow and rims
        float z0 = Course.GlacierBottom + 80f;
        rider.ResetAt(Slope.Centre(z0), z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(Slope.Centre(z0), z0)).normalized * 18f;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        for (int i = 0; i < 40; i++) { rider.Steer = 0f; Step(); }
        rider.DoCrash("test");
        for (float t = 0f; t < 4f; t += Dt) { rider.Steer = 0f; Step(); }
        SnowStats(body.RagdollCentre, 10f, out float hi, out float lo);
        Log($"Open snow: heaps up to {hi:0.00} m, dug down to {-lo:0.00} m round where the body went");
        if (hi < 0.02f || lo > -0.05f) { Log("Open snow: PROBLEM no furrow or rims on open snow"); bad++; }
        var rc = body.RagdollCentre;
        Look(rc + new Vector3(2.5f, 2f, -3.5f), rc, 50f);
        Shoot("CrevSnowOpen.png", true);
    }

    /// <summary>Ridden into a crevasse at speed v from (x, z0), steering `steer`: it goes in head first.</summary>
    static void HeadFirst(string name, Course.Crevasse cv, float x, float z0, float v, float steer)
    {
        rider.ResetAt(x, z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z0)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        lastCrash = "";
        Vector3 at = Vector3.zero; float tCrash = -1f, worstHi = 0f, worstLo = 0f;
        int shot = 0; float[] when = { 0.25f, 0.7f, 1.5f };
        for (float t = 0f; t < 9f; t += Dt)
        {
            rider.Steer = steer; Step();
            if (rider.Crashed && tCrash < 0f) { tCrash = t; at = rider.transform.position; }
            if (tCrash < 0f) continue;
            if (Mathf.Repeat(t, 0.1f) < Dt) { SnowStats(at, 7f, out float hi, out float lo); worstHi = Mathf.Max(worstHi, hi); worstLo = Mathf.Min(worstLo, lo); }
            if (shot < when.Length && t - tCrash >= when[shot]) { LipShots(name, $"T{shot}", at); shot++; }
            if (t - tCrash > 3f && !body.RagdollMoving) break;
        }
        SnowStats(at, 7f, out float h1, out float l1);
        Log($"{name}: '{lastCrash}' at {at}; snow round the lip during the fall: up {worstHi:0.00} m, down {-worstLo:0.00} m; after: up {h1:0.00} m, down {-l1:0.00} m; body {body.RagdollBelowSnow:0.0} m down");
        if (!lastCrash.StartsWith("Crevasse")) { Log($"{name}: PROBLEM did not fall in"); bad++; }
        if (worstHi > 0.03f || worstLo < -0.05f) { Log($"{name}: PROBLEM the snow by the crevasse moved"); bad++; }
        LipShots(name, "After", at);
    }

    static void LipShots(string name, string when, Vector3 at)
    {
        // from beside and up the hill of where it went in, looking at the lip; and the game's own look down after it
        Vector3 eye = at + new Vector3(3.5f, 0f, -5f);
        eye.y = Slope.Surface(eye.x, eye.z) + 2.6f;
        Look(eye, new Vector3(at.x, Slope.Surface(at.x, at.z) - 1.5f, at.z + 1f), 55f);
        Shoot($"CrevSnow{name}{when}.png", true);
        chase.enabled = true; chase.peek = 1f; chase.peekAt = body.RagdollCentre; late.Invoke(chase, null);
        Shoot($"CrevSnow{name}{when}Peek.png", true);
        chase.peek = 0f;
    }

    /// <summary>How far the snow layer has been raised (hi) and dug (lo) round p, off any crevasse's hole.</summary>
    static void SnowStats(Vector3 p, float radius, out float hi, out float lo)
    {
        hi = 0f; lo = 0f;
        var sl = SnowLayer.Instance;
        if (sl == null) return;
        for (float dz = -radius; dz <= radius; dz += 0.125f)
            for (float dx = -radius; dx <= radius; dx += 0.125f)
            {
                float x = p.x + dx, z = p.z + dz;
                if (Course.CrevasseCut(x, z) > 0.25f) continue;   // over the hole: the layer is not drawn there
                float d = sl.SurfaceAt(x, z) - Slope.DrawnHeight(x, z) - 0.006f;
                hi = Mathf.Max(hi, d); lo = Mathf.Min(lo, d);
            }
    }

    static void Sequence()
    {
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        // the glacier as laid out today
        var all = Course.Crevasses;
        var byKind = new Dictionary<Course.CrevasseKind, int>();
        int bridged = 0, hidden = 0, wide = 0; var fieldsSeen = new HashSet<int>();
        foreach (var c in all)
        {
            byKind.TryGetValue(c.kind, out int n); byKind[c.kind] = n + 1;
            if (c.HasBridge) bridged++;
            if (c.b1 - c.b0 > c.len - 4f) hidden++;
            if (c.half > Course.CarryHalf) wide++;
            if (c.field >= 0) fieldsSeen.Add(c.field);
        }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in byKind) sb.Append($"{kv.Value} {kv.Key} ");
        Log($"glacier z {Course.GlacierTop:0}..{Course.GlacierBottom:0} ({Slope.AltitudeFeet(Course.GlacierTop):0} to {Slope.AltitudeFeet(Course.GlacierBottom):0} ft): {all.Length} cracks: {sb}in {fieldsSeen.Count} fields; " +
            $"{wide} too wide to carry, {bridged} with snow bridges ({hidden} hidden under one)");
        foreach (var c in all)
            if (c.kind != Course.CrevasseKind.Chevron)
                Log($"  #{c.id} {c.kind}{(c.field >= 0 ? $" field {c.field}" : "")} z~{(c.z0 + c.z1) * 0.5f:0} {c.len:0} m long, {2f * c.half:0.0} m wide, {c.depth:0} m deep, at {Mathf.Atan2(c.dz, c.dx) * Mathf.Rad2Deg:0} deg" +
                    $"{(c.b1 > c.b0 ? $", bridge {c.b1 - c.b0:0} m" : "")}{(c.c1 > c.c0 ? $" + {c.c1 - c.c0:0} m" : "")}");
        if (all.Length == 0) { Log("PROBLEM no crevasses"); bad++; return; }

        var dayNight = typeof(SummitGame).GetField("dayNight", NP)?.GetValue(game) as DayNight;
        Course.WidestCrack(out var widest, out var spot);
        Place(spot.x, spot.z - 30f);
        Shoot("GlacierShotWarmup.png", false);
        Shoot("GlacierShotWarmup2.png", true);   // (the first render with the rider in it comes out in placeholder colours)
        foreach (var (tag, clock) in new[] { ("Day", 1.4f), ("Night", 3.4f) })
        {
            TimeOfDay.TestClock = clock; dayNight?.ApplyNow();
            Approach(tag, all);
            Bridge(tag, all);
            Jump(tag, widest, spot);
            FallIn(tag, widest, spot);
        }
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight?.ApplyNow();
    }

    /// <summary>Ride straight down at (x, z0) at speed v, steering for x; the step callback sees each step.</summary>
    static void Ride(float x, float z0, float v, float secs, System.Func<float, bool> each)
    {
        rider.ResetAt(x, z0); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z0)).normalized * v;
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); chase.Snap();
        lastCrash = "";
        for (float t = 0f; t < secs; t += Dt)
        {
            if (!rider.Crashed) rider.Steer = Mathf.Clamp((x - rider.transform.position.x) * 0.15f - rider.Heading * 1.2f, -1f, 1f);
            Step();
            if (each != null && !each(t)) break;
        }
    }

    /// <summary>Riding down at the first field, from 40 m up the hill: what the player sees coming.</summary>
    static void Approach(string tag, Course.Crevasse[] all)
    {
        float top = float.MaxValue; int field = -1;
        foreach (var c in all) if (c.field >= 0 && (field < 0 || c.field == 0)) { field = c.field; }
        foreach (var c in all) if (c.field == field) top = Mathf.Min(top, c.z0);
        if (field < 0) { Log("no field to approach"); return; }
        float x = Slope.Centre(top);
        bool shot = false;
        Ride(x, top - 45f, 15f, 4f, t => { if (!shot && rider.Z > top - 26f) { shot = true; Shoot($"GlacierShot{tag}Approach.png", true); } return !shot; });
    }

    /// <summary>Over a snow bridge you can see (wands at its ends), at a normal speed.</summary>
    static void Bridge(string tag, Course.Crevasse[] all)
    {
        foreach (var c in all)
        {
            if (c.b1 <= c.b0 || c.b1 - c.b0 > c.len - 4f) continue;
            float u = (c.b0 + c.b1) * 0.5f;
            Course.Opening(c, u, out float mid);
            var q = c.Point(u, mid);
            if (Mathf.Abs(q.x - Slope.Centre(q.y)) > Slope.PisteHalf - 3f || Mathf.Abs(c.dx) < 0.6f) continue;
            bool shot = false, over = false;
            Ride(q.x, q.y - 22f, 16f, 4f, t =>
            {
                var p = rider.transform.position;
                if (Course.OnBridge(p.x, p.z)) over = true;
                if (!shot && over) { shot = true; Shoot($"GlacierShot{tag}Bridge.png", true); }
                return !rider.Crashed && rider.Z < q.y + 8f;
            });
            Log($"{tag} bridge #{c.id} ({c.b1 - c.b0:0} m) at 16 m/s: {(rider.Crashed ? "CRASHED " + lastCrash : over ? "rode across" : "missed it")}");
            if (rider.Crashed) bad++;
            // and at a crawl, from its lip, it gives way
            float open = Course.Opening(c, u, out _);
            Ride(q.x, q.y - open - 0.4f, 1.5f, 5f, t => !rider.Crashed);
            Log($"{tag} bridge #{c.id} from the lip at 1.5 m/s: {(rider.Crashed ? lastCrash : "held")}");
            if (!rider.Crashed) { Log($"PROBLEM {tag}: a slow board on a bridge did not break through"); bad++; }
            return;
        }
        Log("no visible bridge on the piste to ride");
    }

    /// <summary>The widest crack, jumped: an ollie at its lip at a normal speed.</summary>
    static void Jump(string tag, Course.Crevasse cv, Vector3 spot)
    {
        bool popped = false, shot = false;
        Ride(spot.x, spot.z - 25f, 16f, 4f, t =>
        {
            var p = rider.transform.position;
            if (!popped && !rider.Airborne && Course.CrevasseNear(p.x, p.z, 1.2f)) { rider.QueuePop(0.8f); popped = true; }
            if (!shot && rider.Airborne && Course.CrevasseCut(p.x, p.z) > 0.3f) { shot = true; Shoot($"GlacierShot{tag}Jump.png", true); }
            return !rider.Crashed && rider.Z < spot.z + 12f;
        });
        Log($"{tag} jump over #{cv.id} ({2f * cv.half:0.0} m wide) at 16 m/s: {(rider.Crashed ? "CRASHED " + lastCrash : "cleared")}");
        if (rider.Crashed) bad++;
    }

    /// <summary>The widest crack, ridden into slowly: the fall, the look down after the body, and the body inside.</summary>
    static void FallIn(string tag, Course.Crevasse cv, Vector3 spot)
    {
        bool shotFalling = false;
        float minY = 1e9f;
        Ride(spot.x, spot.z - cv.half - 3f, 4f, 12f, t =>
        {
            var c = body.RagdollCentre;
            if (rider.Crashed) minY = Mathf.Min(minY, c.y);
            if (!shotFalling && body.RagdollBelowSnow > 6f)
            {
                shotFalling = true;
                chase.enabled = true; chase.peek = 1f; chase.peekAt = c; late.Invoke(chase, null);
                Shoot($"GlacierShot{tag}Falling.png", true);
            }
            return !(rider.Crashed && t > 3f && !body.RagdollMoving);
        });
        var rc = body.RagdollCentre;
        float u = cv.Along(rc.x, rc.z);
        float open = Course.Opening(cv, u, out float mid);
        float off = cv.Across(rc.x, rc.z) - mid;
        float surface = Slope.Surface(rc.x, rc.z);
        float depth = cv.depth * Mathf.Sqrt(Mathf.Max(0f, open) / cv.half) - 1.5f;
        Log($"{tag} fall into #{cv.id}: crash '{lastCrash}'; the body lies {surface - rc.y:0.0} m down (crack {depth:0.0} m deep there), {off:0.00} m off its middle (open {open:0.00}), moving={body.RagdollMoving}");
        if (!lastCrash.StartsWith("Crevasse")) { Log($"PROBLEM {tag}: did not fall in"); bad++; }
        if (surface - rc.y > depth + 1.2f) { Log($"PROBLEM {tag}: fell through the floor"); bad++; }
        if (Mathf.Abs(off) > open + 0.3f) { Log($"PROBLEM {tag}: lies outside the walls"); bad++; }
        chase.enabled = true; chase.peek = 1f; chase.peekAt = rc; late.Invoke(chase, null);
        Shoot($"GlacierShot{tag}Peek.png", true);
        Look(InsideEye(cv, rc, 4f, 0.5f), rc, 60f);
        Shoot($"GlacierShot{tag}Inside.png", true);
        chase.peek = 0f;
    }

    /// <summary>A point on the crack's middle line `along` metres on from p, `frac` of p's depth below the snow there.</summary>
    static Vector3 InsideEye(Course.Crevasse cv, Vector3 p, float along, float frac)
    {
        float below = Slope.Surface(p.x, p.z) - p.y;
        float u = Mathf.Clamp(cv.Along(p.x, p.z) + along, 1f, cv.len - 1f);
        Course.Opening(cv, u, out float mid);
        var q = cv.Point(u, mid);
        return new Vector3(q.x, Slope.Surface(q.x, q.y) - below * frac, q.y);
    }

    // ---------------------------------------------------------------- the park

    /// <summary>
    /// The park's big rails and boxes: renders of each, a grind down each from the snow beside its start, and checks
    /// that they clear the snow, stay apart and keep trees and pickups off them. Run: -executeMethod GlacierShot.Park.
    /// Writes ParkShot*.png.
    /// </summary>
    public static void Park() => Boot(ParkSequence);

    static void ParkSequence()
    {
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        var fs = Course.FeaturesOf(0);
        // the layout: lengths, heights over the snow, no two features or a feature and a kicker side by side
        for (int c = 0; c < 4; c++)
            foreach (var f in Course.FeaturesOf(c))
            {
                float lo = 1e9f, hi = 0f;
                for (int i = 0; i <= 40; i++) { var q = Course.FeaturePoint(f, i / 40f); float cl = q.y - Slope.Height(q.x, q.z); lo = Mathf.Min(lo, cl); hi = Mathf.Max(hi, cl); }
                if (c == 0) Log($"c{c} {f.kind} {f.Length:0} m long at x={f.a.x - Slope.Centre(f.a.z):0.0}, z {f.a.z - Course.CycleBase(f.a.z) - Course.ParkAt:0}..{f.b.z - Course.CycleBase(f.a.z) - Course.ParkAt:0} of the park; {lo:0.00}..{hi:0.00} m over the snow");
                if (lo < f.height - 0.05f) { Log($"PROBLEM c{c} {f.kind} only {lo:0.00} m over the snow"); bad++; }
                if (hi > f.height + 1.6f) { Log($"PROBLEM c{c} {f.kind} stands {hi:0.00} m over the snow"); bad++; }
                if (f.b.z > Course.CycleBase(f.a.z) + Course.DangerAt) { Log($"PROBLEM c{c} {f.kind} runs out of the park"); bad++; }
                foreach (var g in Course.FeaturesOf(c))
                    if (g.id != f.id && g.a.z < f.b.z + 6f && f.a.z < g.b.z + 6f && Mathf.Abs(g.a.x - f.a.x) < 4f) { Log($"PROBLEM c{c} features {f.id} and {g.id} overlap"); bad++; }
                foreach (float pk in Course.ParkKickers)
                {
                    float kz = Course.CycleBase(f.a.z) + Course.ParkAt + pk;
                    if (kz < f.b.z + 4f && kz + 22f > f.a.z - 4f) { Log($"PROBLEM c{c} {f.kind} beside a kicker"); bad++; }
                }
                bool blocked = false;
                for (int i = 0; i <= 30 && !blocked; i++)
                {
                    var q = Course.FeaturePoint(f, i / 30f);
                    for (int dx = -1; dx <= 1 && !blocked; dx++)
                        for (int dz = -1; dz <= 1 && !blocked; dz++)
                        {
                            int cx = Mathf.FloorToInt(q.x / Forest.Cell) + dx, cz = Mathf.FloorToInt(q.z / Forest.Cell) + dz;
                            if (Forest.At(cx, cz, out var tp, out _, out _) != Forest.Kind.None && new Vector2(tp.x - q.x, tp.z - q.z).magnitude < 2f) blocked = true;
                        }
                }
                if (blocked) { Log($"PROBLEM c{c} a tree or rock on the {f.kind}"); bad++; }
            }

        foreach (var f in fs)
        {
            string tag = f.kind == Course.Kind.Rail ? "Rail" : "Box";
            if (System.IO.File.Exists($"ParkShot{tag}.png")) continue;   // one of each
            Place(f.a.x, f.a.z - 20f);
            Shoot("ParkShotWarmup.png", false);
            Vector3 mid = Course.FeaturePoint(f, 0.5f);
            Vector3 side = new Vector3(Mathf.Sign(Slope.Centre(mid.z) - mid.x), 0f, 0f);   // from the middle of the piste
            var eye = mid + side * 7f + new Vector3(0f, 0f, -f.Length * 0.55f); eye.y = Slope.Surface(eye.x, eye.z) + 2.2f;
            Look(eye, mid, 60f);
            Shoot($"ParkShot{tag}.png", false);
            eye = f.a + side * 3f + new Vector3(0f, 0f, -14f); eye.y = Slope.Surface(eye.x, eye.z) + 1.8f;
            Look(eye, Course.FeaturePoint(f, 0.25f), 55f);
            Shoot($"ParkShot{tag}Start.png", false);
            // a grind: ride at it from 18 m up the hill in line with it, boarded from the snow
            rider.ResetAt(f.a.x, f.a.z - 18f); body.ResetVisual(); rider.Begin();
            rider.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(f.a.x, f.a.z - 18f)).normalized * 11f;
            terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); chase.Snap();
            lastCrash = "";
            bool on = false, shot = false; float covered = 0f; string trick = "";
            System.Action<string, int> tk = (nm, pts) => trick = $"{nm} {pts}";
            rider.Trick += tk;
            for (float t = 0f; t < 10f && !rider.Crashed && rider.Z < f.b.z + 15f; t += Dt)
            {
                float tx = rider.Z < f.a.z ? f.a.x : Mathf.Lerp(f.a.x, f.b.x, Mathf.InverseLerp(f.a.z, f.b.z, rider.Z));
                rider.Steer = Mathf.Clamp((tx - rider.transform.position.x) * 0.4f - rider.Heading * 1.5f, -1f, 1f);
                Step();
                if (rider.Grinding) { on = true; covered = Mathf.Max(covered, rider.Z - f.a.z); }
                if (rider.Grinding && !shot && rider.Z > Mathf.Lerp(f.a.z, f.b.z, 0.45f))
                {
                    shot = true;
                    Shoot($"ParkShot{tag}Grind.png", true);
                    var rp = rider.transform.position;
                    Look(rp + side * 4f + new Vector3(0f, 1.2f, 2.5f), rp + Vector3.up * 0.4f, 50f);
                    Shoot($"ParkShot{tag}GrindSide.png", true);
                    chase.enabled = true;
                }
            }
            rider.Trick -= tk;
            Log($"{tag} grind: boarded={on}, {covered:0} of {f.b.z - f.a.z:0} m down the hill, trick '{trick}', crashed={rider.Crashed} {lastCrash}");
            if (!on || covered < (f.b.z - f.a.z) * 0.85f) { Log($"PROBLEM {tag}: did not grind it end to end"); bad++; }
            if (rider.Crashed) { Log($"PROBLEM {tag}: crashed on it ({lastCrash})"); bad++; }
        }
    }

    static void Place(float x, float z)
    {
        rider.ResetAt(x, z); body.ResetVisual(); rider.Stop();
        terrain.Prewarm(); props.Prewarm(); dress.Prewarm();
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        typeof(Forest).GetMethod("Draw", NP)?.Invoke(props, null);
    }

    static void Step()
    {
        for (int k = 0; k < 2; k++) { rider.Tick(Dt / 2f); body.PhysicsStep(); Physics.Simulate(Dt / 2f); }
        tUpd.Invoke(terrain, null);
        fUpd.Invoke(props, null);
        body.Apply(Dt);
        chase.enabled = true;
        late.Invoke(chase, null);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
    }

    static void Look(Vector3 from, Vector3 to, float fov)
    {
        chase.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
    }

    static void Log(string s) => Debug.Log("GLACIERSHOT " + s);

    static void Shoot(string file, bool bakeSkins)
    {
        DynamicGI.UpdateEnvironment();
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        // batchmode renders do not re-skin meshes: bake them to see the real pose
        var baked = new List<GameObject>();
        var hidden = new List<SkinnedMeshRenderer>();
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
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
    }
}
