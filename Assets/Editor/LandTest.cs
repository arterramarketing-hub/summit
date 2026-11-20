using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Big landings: off the biggest jumps on the mountain (the start lip, the jump cliffs, the kickers, off a ridge's
/// snow side) as fast as they can be hit and with a Super Jump, every one must be ridden away from, however tall;
/// over a deadly cliff (a marked wall, a ridge's cliff side) must still kill and bury; and landing badly (upside
/// down, sideways, flat from a great height) must still crash.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod LandTest.Run -logFile land.log
/// </summary>
public static class LandTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static int fails, rides;
    static string lastCrash = "";
    static float lastKilled = -1f;
    static bool diedOnRock;
    static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        r.input = null;
        r.Crash += why => lastCrash = why;
        body.Killed += fell =>
        {
            lastKilled = fell;
            if (fell < Rider.DeathFall) Fail($"killed by a fall of only {fell:0} m (the rule is {Rider.DeathFall:0} m)");
            Vector3 c = body.RagdollCentre;
            diedOnRock = RockFace.IsRock(Slope.Normal(c.x, c.z), c.x, c.z);   // (killed on rock: nothing to bury it in, by design)
        };
        var dressing = Object.FindFirstObjectByType<CourseDressing>();
        if (dressing != null) { var tgt = dressing.target; dressing.target = null; dressing.Rebuild(); dressing.target = tgt; }
        float worstSurvived = 0f;
        try
        {
            for (int seed = 0; seed < 3; seed++)
            {
                Slope.Init(1000 + seed * 77);
                terrain.Rebuild(); props.Rebuild();
                // ---- the start lip, flat out and with a Super Jump
                foreach (float v0 in new[] { 12f, 25f, 40f })
                    foreach (bool super in new[] { false, true })
                        worstSurvived = Mathf.Max(worstSurvived, Survive($"seed{seed} startlip v{v0}{(super ? " super" : "")}", Slope.Centre(Slope.PadLip), Slope.PadLip - 25f, v0, super ? Slope.PadLip - 0.5f : float.NaN));
                // ---- every jump cliff of the first two cycles, crawling to flat out, and with a Super Jump off the lip
                for (int cyc = 0; cyc < 2; cyc++)
                    for (int k = 0; k < 2; k++)
                    {
                        if (!Cliffs.JumpOf(cyc, k, out var j)) continue;
                        foreach (float v0 in new[] { 12f, 25f, 38f, 50f })
                            worstSurvived = Mathf.Max(worstSurvived, Survive($"seed{seed} jumpcliff c{cyc}k{k} drop{j.drop:0.0} v{v0}", Slope.Centre(j.Start - 25f), j.Start - 25f, v0, float.NaN));
                        foreach (float v0 in new[] { 25f, 45f })
                            worstSurvived = Mathf.Max(worstSurvived, Survive($"seed{seed} jumpcliff c{cyc}k{k} v{v0} super", Slope.Centre(j.Start - 25f), j.Start - 25f, v0, j.z - 1.5f));
                    }
                // ---- kickers in the cruise (the first three) and the park's big pair, flat out with a popped Super Jump
                float zk = 100f; int found = 0;
                while (found < 3 && Slope.NextKicker(zk, out var at))
                {
                    zk = at.z + 5f; found++;
                    foreach (float v0 in new[] { 30f, 45f })
                    {
                        worstSurvived = Mathf.Max(worstSurvived, Survive($"seed{seed} kicker z{at.z:0} v{v0}", at.x, at.z - 30f, v0, at.z - 0.6f, false));
                        worstSurvived = Mathf.Max(worstSurvived, Survive($"seed{seed} kicker z{at.z:0} v{v0} super", at.x, at.z - 30f, v0, at.z - 0.6f));
                    }
                }
                foreach (float pk in Course.ParkKickers)
                {
                    float z0 = Course.First + Course.ParkAt + pk + Slope.KickRamp * 1.15f;
                    worstSurvived = Mathf.Max(worstSurvived, Survive($"seed{seed} park kicker {pk} v40 super", Slope.Centre(z0), z0 - 30f, 40f, z0 - 0.6f));
                }
                // ---- off a ridge: the snow side of a one-sided ridge (survived) and every cliff side (killed)
                RidgeSides(seed);
                // ---- over a deadly cliff (killed and buried)
                for (int cyc = 0; cyc < 2; cyc++)
                {
                    if (!Cliffs.WallOf(cyc, out var w)) continue;
                    foreach (float v0 in new[] { 18f, 34f })
                        Die($"seed{seed} wall c{cyc} h{w.height:0} v{v0}", w, v0);
                }
                // ---- bad landings: upside down, sideways, flat from a great height (crashed)
                if (Cliffs.JumpOf(0, 0, out var jb) || Cliffs.JumpOf(0, 1, out jb))
                {
                    Bad($"seed{seed} upside down", jb, 32f, flip: true);
                    Bad($"seed{seed} sideways", jb, 32f, flip: false);
                }
                if (Cliffs.JumpOf(0, 0, out var jf)) Flat($"seed{seed} flat 45m", 45f, jf);
            }
        }
        catch (System.Exception e) { Debug.LogError("LAND exception " + e); fails++; }
        Debug.Log($"LAND done: {rides} rides, {fails} failure(s); hardest landing ridden away {worstSurvived:0.0} m/s");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.Exit(0);
    }

    static void Fail(string what) { fails++; Debug.Log("LAND-FAIL " + what); }

    static void Step(int n)
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
    }

    static void Start(float x, float z, float v0)
    {
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
        lastCrash = "";
        terrain.Prewarm(); props.Prewarm();
    }

    static float Steer(float lineX) => Mathf.Clamp((lineX - r.transform.position.x) * 0.05f - r.Heading * 0.5f, -1f, 1f);

    /// <summary>
    /// Ride a line down the piste (popping at popZ if given, with a Super Jump unless super is false) through its
    /// big air (2 m up or more), and on until 2.5 s after it lands. Returns the hardest big landing ridden away from.
    /// A landing on rock (off the piste) or into a crevasse is the mountain, not the jump: noted, not failed.
    /// </summary>
    static float Survive(string name, float x, float z, float v0, float popZ, bool super = true)
    {
        rides++;
        Start(x, z, v0);
        float dx = x - Slope.Centre(z);
        bool pop = !float.IsNaN(popZ), popped = false, crashLanding = false;
        float peak = 0f, air = 0f, hardest = 0f, angle = 0f, sinceBig = -1f, fell = 0f, crashAng = 0f, crashImp = 0f; int bigLandings = 0;
        bool verbose = System.Environment.GetEnvironmentVariable("LAND_VERBOSE") == name;
        System.Action<float> landed = imp =>
        {
            if (verbose) { Vector3 q = r.transform.position; Debug.Log($"LAND-V landed {imp:0.0} raw {r.LastLandImpact:0.0} at {r.LastLandAngle:0} deg, peak {r.Peak:0.0} air {r.AirTime:0.00} z={q.z:0} x-c={q.x - Slope.Centre(q.z):0.0} n.y={Slope.Normal(q.x, q.z).y:0.00}"); }
            if (r.Peak < 2f) return;
            Vector3 p = r.transform.position;
            bigLandings++; sinceBig = 0f;
            peak = Mathf.Max(peak, r.Peak);
            if (r.LastLandImpact > hardest) { hardest = r.LastLandImpact; angle = r.LastLandAngle; fell = r.DropFrom.y - p.y - Slope.Fall * Mathf.Max(0f, p.z - r.DropFrom.z); }
        };
        r.Landed += landed;
        int n2 = 0;
        Vector3 crashAt = Vector3.zero;
        try
        {
            for (float t = 0f; t < 14f; t += Dt, n2++)
            {
                if (pop && !popped && !r.Airborne && r.Z > popZ)
                {
                    if (super) r.AddSuperJump(30f);
                    r.QueuePop(0.8f); popped = true;
                }
                r.Steer = Steer(Slope.Centre(r.Z + 12f) + dx);
                bool wasAir = r.Airborne; float pk = r.Peak;
                if (verbose && n2 % 12 == 0) { Vector3 q = r.transform.position; Debug.Log($"LAND-V t={t:0.00} z={q.z:0.0} x-c={q.x - Slope.Centre(q.z):0.0} above={r.AboveGround:0.0} v={r.Velocity} air={r.Airborne} super={r.SuperJumpTime:0.0} popped={popped}"); }
                Step(n2);
                if (r.Crashed)
                {
                    crashLanding = wasAir; crashAng = r.LastLandAngle; crashImp = r.LastLandImpact; crashAt = r.transform.position;
                    peak = Mathf.Max(peak, pk);
                    break;
                }
                if (r.Airborne) air = Mathf.Max(air, r.AirTime);
                if (sinceBig >= 0f && !r.Airborne) { sinceBig += Dt; if (sinceBig > 2.5f) break; }
            }
        }
        finally { r.Landed -= landed; }
        string crash = r.Crashed ? $"crashed '{lastCrash}' {(crashLanding ? $"landing {crashImp:0.0} m/s at {crashAng:0} deg" : "riding")} at z={crashAt.z:0} x-c={crashAt.x - Slope.Centre(crashAt.z):0.0} n.y={Slope.Normal(crashAt.x, crashAt.z).y:0.00} sec={Course.At(crashAt.z)} jumpface={Cliffs.OnJumpFace(crashAt.x, crashAt.z)} crevasse={Course.CrevasseCut(crashAt.x, crashAt.z):0.0}" : "rode away";
        Debug.Log($"LAND {name}: peak {peak:0.0} m, {air:0.00}s air, hardest big landing {hardest:0.0} m/s at {angle:0} deg to the slope, fell {fell:0.0} m beyond the line, " +
                  $"{bigLandings} big landing(s), {crash}, dead={body.Dead}, {r.Speed * 3.6f:0} km/h");
        if (r.Crashed)
        {
            // (a crevasse's walls are rock: a hop after the big landing can come down on one)
            // (and riding on after it, off the piste, the steering can take the rider over the deadly cliff beside it)
            bool offPiste = Mathf.Abs(crashAt.x - Slope.Centre(crashAt.z)) > Slope.PisteHalf;
            // (a flight that comes down on the next jump cliff's rock band lands on rock, which crashes by design)
            bool mountain = lastCrash == "Crevasse!" || (lastCrash == "Hit the rocks!" && (offPiste || Course.CrevasseNear(crashAt.x, crashAt.z, 3f) || Cliffs.OnJumpFace(crashAt.x, crashAt.z)))
                            || (lastCrash == "Cliffed!" && offPiste)
                            // (or, held to a line off the centre, off a ridge's narrow crest)
                            || (Ridge.Has(crashAt.z) && Mathf.Abs(crashAt.x - Slope.Centre(crashAt.z)) > Ridge.HalfWidth(crashAt.z))
                            // (or the landing was ridden away and the rider then went through a tree that snapped: the forest, not the landing)
                            || (lastCrash == "Tree snapped!" && !crashLanding);
            if (!mountain) Fail($"{name}: {crash} (peak {peak:0.0} m)");
            return 0f;
        }
        if (bigLandings == 0) Fail($"{name}: no big air (peak {peak:0.0} m)");
        else if (r.Speed < 3f)
        {
            Vector3 p = r.transform.position; string near = "";
            foreach (var c in Physics.OverlapSphere(p, 2.5f, ~0, QueryTriggerInteraction.Ignore)) if (c.attachedRigidbody != r.Body) near += c.name + " ";
            string where = $"({r.Speed:0.0} m/s at z={p.z:0} x-c={p.x - Slope.Centre(p.z):0.0} n.y={Slope.Normal(p.x, p.z).y:0.00} near: {near})";
            // (batchmode fires no collision callbacks: riding on into a tree or a boulder stops the board dead instead of crashing it)
            if (near.Contains("TreeHit") || near.Contains("RockHit")) Debug.Log($"LAND {name}: rode away, then into a tree or rock {where}");
            else Fail($"{name}: never rode away {where}");
        }
        return hardest;
    }

    /// <summary>
    /// Play a fall off a cliff out. One rule: a fall of Rider.DeathFall or more kills and buries ("Cliffed!" always does),
    /// anything shorter is lived through; either way the body comes to rest on the ground, never hung in the air.
    /// </summary>
    static void Expect(string name, int n, float midAir, bool mustDie)
    {
        string why = lastCrash;
        lastKilled = -1f;
        float tDead = -1f, rest = 0f;
        for (float t = 0f; t < 25f; t += Dt, n++)
        {
            Step(n);
            if (body.Dead && tDead < 0f) tDead = t;
            if (body.Dead && !body.RagdollMoving) { rest += Dt; if (rest > 1.5f) break; }
        }
        Vector3 c = body.RagdollCentre;
        Debug.Log($"LAND {name}: '{why}' {midAir:0.0} m off the ground, dead={body.Dead} after {tDead:0.0}s, lies {body.RagdollBelowSnow:0.00} m under the snow at x-c={c.x - Slope.Centre(c.z):0} z={c.z:0} rock={RockFace.IsRock(Slope.Normal(c.x, c.z), c.x, c.z)} below a deadly cliff={Deadly.Below(c.x, c.z)}");
        float up = c.y - Slope.DrawnHeight(c.x, c.z);
        bool onRock = RockFace.IsRock(Slope.Normal(c.x, c.z), c.x, c.z);
        if ((mustDie || why == "Cliffed!") && !body.Dead && why != "Crevasse!") Fail($"{name}: survived '{why}' off a cliff past the death height");   // (a glacier gap at the foot takes you too)
        else if (!mustDie && body.Dead && why != "Cliffed!") Fail($"{name}: killed ('{why}') by a fall short of the death height");
        else if (body.Dead && body.RagdollBelowSnow < 0.6f && !onRock && !diedOnRock) Fail($"{name}: dead but not buried ({body.RagdollBelowSnow:0.00} m)");
        if (up > 1.5f && !body.RagdollMoving) Fail($"{name}: the body is left hanging {up:0.0} m over the ground");
    }

    /// <summary>Over the edge of one of the walls in the danger stretch (25-55 m: short of Rider.DeathFall).</summary>
    static void Die(string name, Cliffs.Wall w, float v0)
    {
        rides++;
        float z = w.z1 + 15f - 25f, x0 = Slope.Centre(z) + w.side * (w.edge - 7f);
        Start(x0, z, v0);
        int n = 0; float midAir = 0f;
        for (float t = 0f; t < 8f && !r.Crashed; t += Dt, n++)
        {
            r.Steer = Mathf.Clamp(w.side * 0.9f - r.Heading * 0.3f, -1f, 1f);
            Step(n);
            if (r.Crashed) midAir = r.transform.position.y - Slope.DrawnHeight(r.transform.position.x, r.Z) - Rider.R;
        }
        float offW = (r.transform.position.x - Slope.Centre(r.Z)) * w.side;
        if (!r.Crashed)
        {
            if (offW < w.edge) Fail($"{name}: rode on without going over (x-c={r.transform.position.x - Slope.Centre(r.Z):0.0})");
            else Debug.Log($"LAND {name}: over the {w.height:0} m wall and rode it out, {r.Speed * 3.6f:0} km/h");
            return;
        }
        Expect(name, n, midAir, w.height >= Rider.DeathFall);
    }

    static float FollowCrest(float dx)
    {
        Vector3 p = r.transform.position;
        float look = Mathf.Max(7f, r.Speed * 0.5f);
        float want = Mathf.Atan2(Slope.Centre(r.Z + look) + dx - p.x, look);
        return Mathf.Clamp((want - r.Heading) * 4f - r.YawRate * 0.25f, -1f, 1f);
    }

    static void RidgeSides(int seed)
    {
        for (int cyc = 0; cyc < 4; cyc++)
        {
            if (!Ridge.Built(cyc)) continue;
            float z = Course.First + cyc * Course.Cycle + Ridge.At + Ridge.RampIn + 20f;
            for (int s = -1; s <= 1; s += 2)
            {
                bool cliff = Ridge.CliffSide(z, s);
                foreach (float v0 in cliff ? new[] { 24f } : new[] { 20f, 32f })
                {
                    string name = $"seed{seed} ridge c{cyc} {(s < 0 ? "left" : "right")} {(cliff ? "cliff" : "snow side")} v{v0}";
                    rides++;
                    Start(Slope.Centre(z), z, v0);
                    if (!cliff) r.AddSuperJump(30f);
                    int n = 0; float midAir = 0f, peak = 0f, hardest = 0f, sinceLand = 0f; bool popped = false, landed = false;
                    System.Action<float> onLand = imp => { hardest = Mathf.Max(hardest, imp); if (r.Peak > 3f) landed = true; sinceLand = 0f; };
                    r.Landed += onLand;
                    try
                    {
                        for (float t = 0f; t < 12f && !r.Crashed; t += Dt, n++)
                        {
                            float off = Mathf.Abs(r.transform.position.x - Slope.Centre(r.Z));
                            // pop off the crest's edge out over the drop: a big air down the snow side
                            if (!cliff && !popped && t > 1.5f && !r.Airborne && off > Ridge.HalfWidth(r.Z) - 1f) { r.QueuePop(0.8f); popped = true; }
                            r.Steer = t < 1.5f ? FollowCrest(0f) : cliff ? s * 0.8f : popped ? Mathf.Clamp((s * 0.6f - r.Heading) * 2f, -1f, 1f) : Mathf.Clamp((s * 0.9f - r.Heading) * 2f, -1f, 1f);
                            Step(n);
                            peak = Mathf.Max(peak, r.Peak);
                            if (!r.Airborne) sinceLand += Dt;
                            if (r.Crashed) midAir = r.transform.position.y - Slope.DrawnHeight(r.transform.position.x, r.Z) - Rider.R;
                            if (!cliff && landed && sinceLand > 3f) break;
                        }
                    }
                    finally { r.Landed -= onLand; }
                    if (cliff)
                    {
                        // (rock is ridden now: a ledge on the way down can break the fall, so only "Cliffed!" or a fall
                        // past the death height has to kill)
                        if (!r.Crashed) { Debug.Log($"LAND {name}: rode off the edge and down without a fall, {Ridge.Top(r.Z) - r.transform.position.y:0} m below the crest"); continue; }
                        Expect(name, n, midAir, false);
                        continue;
                    }
                    Vector3 at = r.transform.position;
                    Debug.Log($"LAND {name}: peak {peak:0.0} m, hardest landing {hardest:0.0} m/s, {Ridge.Top(at.z) - at.y:0} m below the crest, crashed={r.Crashed} {lastCrash}, {r.Speed * 3.6f:0} km/h");
                    Vector3 cp = r.transform.position;
                    // (a tree, a boulder or a crag on the way down is a crash by collision, not the landing's)
                    if (r.Crashed && lastCrash != "Hit the rocks!" && lastCrash != "Rock" && !lastCrash.StartsWith("Tree")) Fail($"{name}: crashed '{lastCrash}' landing on the snow side (peak {peak:0.0} m, {hardest:0.0} m/s; that landing {r.LastLandImpact:0.0} m/s at {r.LastLandAngle:0} deg, rock={RockFace.IsRock(Slope.Normal(cp.x, cp.z), cp.x, cp.z)} n.y={Slope.Normal(cp.x, cp.z).y:0.00} side={Ridge.SideAt(cp.z, s)})");
                    else if (!landed && !r.Crashed) Fail($"{name}: no big air off the snow side (peak {peak:0.0})");
                }
            }
        }
    }

    static void SetTrick(string trick, float spin, float flip, float time)
    {
        var tp = typeof(Rider);
        tp.GetProperty("TrickName").SetValue(r, trick);
        tp.GetProperty("TrickSpin").SetValue(r, spin);
        tp.GetProperty("TrickFlip").SetValue(r, flip);
        tp.GetProperty("TrickTime").SetValue(r, time);
        tp.GetField("trickPlanT", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(r, time);
    }

    /// <summary>Off a jump cliff with a trick that cannot come round in time: the board comes down upside down or sideways.</summary>
    static void Bad(string name, Cliffs.Jump j, float v0, bool flip)
    {
        float rate0 = Rider.TrickMaxRate;
        Rider.TrickMaxRate = 0f;   // (a trick that cannot hurry: it comes down part way round, as a misjudged one would)
        try { BadRide(name, j, v0, flip); } finally { Rider.TrickMaxRate = rate0; }
    }

    static void BadRide(string name, Cliffs.Jump j, float v0, bool flip)
    {
        rides++;
        float x = Slope.Centre(j.Start - 25f);
        Start(x, j.Start - 25f, v0);
        bool set = false; int n = 0; float air = 0f, progress = 0f;
        for (float t = 0f; t < 8f && !r.Crashed; t += Dt, n++)
        {
            r.Steer = Steer(Slope.Centre(r.Z + 12f));
            Step(n);
            if (r.Airborne && !set)
            {
                // a trick planned for a flight over twice as long as this one: half way round at the landing
                set = true;
                float T = Mathf.Max(r.TrickTime, 1f) * 2.2f;
                if (flip) SetTrick("Backflip", 0f, -Mathf.PI * 2f, T);
                else SetTrick("180 Indy", Mathf.PI, 0f, T);
            }
            if (r.Airborne) { air = r.AirTime; progress = r.TrickProgress; }
            if (set && !r.Airborne) break;
        }
        Debug.Log($"LAND {name}: {air:0.00}s air, trick {progress * 100f:0}% round at the landing, crashed={r.Crashed} '{lastCrash}' landing {r.LastLandImpact:0.0} m/s at {r.LastLandAngle:0} deg, dead={body.Dead}");
        if (!r.Crashed) Fail($"{name}: rode away from a landing {progress * 100f:0}% round");
        else if (lastCrash != (flip ? "Landed upside down!" : "Landed sideways!")) Fail($"{name}: crashed for '{lastCrash}'");
    }

    /// <summary>Dropped straight down onto flat snow (a jump cliff's bench) from a great height: a slam, not a landing.</summary>
    static void Flat(string name, float h, Cliffs.Jump j)
    {
        rides++;
        float z = j.z - 4f, x = Slope.Centre(z);
        Start(x, z, 0f);
        Vector3 p = r.Body.position + Vector3.up * h;
        r.Body.position = p; r.transform.position = p;
        r.Airborne = true; r.DropFrom = p;
        r.Body.linearVelocity = Vector3.zero;
        int n = 0;
        for (float t = 0f; t < 5f && (r.Airborne || t < 0.1f) && !r.Crashed; t += Dt, n++) Step(n);
        Debug.Log($"LAND {name}: crashed={r.Crashed} '{lastCrash}' landing {r.LastLandImpact:0.0} m/s at {r.LastLandAngle:0} deg");
        if (!r.Crashed) Fail($"{name}: rode away from a {h:0} m drop straight down onto the piste");
        else if (lastCrash != "Slammed!") Fail($"{name}: crashed for '{lastCrash}', not a slam");
    }
}
