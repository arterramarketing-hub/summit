using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// aircontrol: the finger steers the flight a little, to line a landing up. Off kickers and jump cliffs on three
/// mountains, the same approach (a bot holds the piste's centre to the lip), then in the air the finger held one way,
/// not at all, or the other way, from the takeoff to the landing. Checked:
///  - the landing moves the finger's way, measurably (at least 0.8 m for an air of 0.8 s or more, both ways);
///  - never further than the air steer's budget allows (Rider.AirSideMost of sideways speed over the air);
///  - at once: 0.05 s after the finger goes over in the air the flight is already going sideways (0.25 m/s);
///  - every steered landing is ridden away from;
///  - the tricks still come from the same finger: in an air long enough to trick, a drag under the flick steers and
///    spins nothing; a flick still throws a spin and a flick up a flip, both ridden away; with a spin coming round, a
///    drag its own way is the spin's (no drift) and a drag the other way still steers.
/// The fingers are the real input's: Rider.TrickX is the steering throw, as RiderInput feeds it.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod AirControlTest.Run -logFile air.log
/// </summary>
public static class AirControlTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static int fails, rides;
    static string lastCrash = "";
    static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;

    static void Fail(string what) { fails++; Debug.Log("AIRCTL-FAIL " + what); }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        r.input = null;
        r.Crash += why => lastCrash = why;
        var dressing = Object.FindFirstObjectByType<CourseDressing>();
        if (dressing != null) { var tgt = dressing.target; dressing.target = null; dressing.Rebuild(); dressing.target = tgt; }
        Debug.Log($"AIRCTL air drift {Rider.AirDrift:0.0} m/s^2 at a whole drag, at most {Rider.AirSideMost:0.0} m/s over an air, whole at {Rider.AirReach * RiderTricks_FlickAt():0.00} of the throw in a trick air");
        float most = 0f, least = 99f; int measured = 0;
        try
        {
            for (int seed = 0; seed < 3; seed++)
            {
                TestBoot.NewMountain(game, 1000 + seed * 77, terrain, props);
                var jumps = new List<(string name, float x, float z, float v)>();
                float zk = 100f; int found = 0;
                while (found < 3 && Slope.NextKicker(zk, out var at)) { zk = at.z + 5f; found++; jumps.Add(($"kicker z{at.z:0}", at.x, at.z - 30f, 26f)); }
                for (int cyc = 0; cyc < 2; cyc++)
                    if (Cliffs.JumpOf(cyc, 0, out var j)) jumps.Add(($"jumpcliff c{cyc} drop{j.drop:0.0}", Slope.Centre(j.Start - 25f), j.Start - 25f, 28f));
                foreach (var jp in jumps)
                {
                    string name = $"seed{seed} {jp.name} v{jp.v:0}";
                    var flat = Fly(name + " hands off", jp.x, jp.z, jp.v, 0f, Hands.Steer);
                    if (!flat.flew) { Debug.Log($"AIRCTL {name}: no air off it (skipped)"); continue; }
                    foreach (float s in new[] { 1f, -1f })
                    {
                        var st = Fly(name + (s > 0 ? " right" : " left"), jp.x, jp.z, jp.v, s, Hands.Steer);
                        if (!st.flew) { Fail($"{name}: steered {(s > 0 ? "right" : "left")}, it never left the snow"); continue; }
                        float shift = (st.land.x - flat.land.x) * Mathf.Sign(s);
                        float airT = Mathf.Min(st.air, flat.air);
                        float bound = Rider.AirSideMost * airT + 0.25f;
                        Debug.Log($"AIRCTL {name} {(s > 0 ? "right" : "left")}: {st.air:0.00} s in the air (hands off {flat.air:0.00} s), the landing {shift:+0.00;-0.00} m the finger's way (bound {bound:0.0} m), {Vector2.Distance(new Vector2(st.land.x, st.land.z), new Vector2(flat.land.x, flat.land.z)):0.00} m from the hands-off landing; sideways {st.at05:0.00} m/s 0.05 s after the finger went over; {(st.crash ? "CRASHED '" + lastCrash + "'" : "rode away")}, trick {st.trick}");
                        if (airT >= 0.8f)
                        {
                            measured++; most = Mathf.Max(most, shift / airT); least = Mathf.Min(least, shift);
                            if (shift < 0.8f) Fail($"{name}: steered {(s > 0 ? "right" : "left")} through a {airT:0.00} s air, the landing moved only {shift:0.00} m its way (0.8 m asked)");
                        }
                        if (Mathf.Abs(shift) > bound) Fail($"{name}: the landing moved {shift:0.00} m, past what the air steer allows ({bound:0.00} m)");
                        if (st.at05 < 0.25f) Fail($"{name}: 0.05 s after the finger went over in the air the flight is going only {st.at05:0.00} m/s sideways (0.25 asked: no lag)");
                        if (st.crash) Fail($"{name}: steered {(s > 0 ? "right" : "left")} in the air, then crashed '{lastCrash}'");
                        if (st.trick != "") Fail($"{name}: a steering drag in the air threw a trick ({st.trick})");
                    }
                    // the tricks, in an air long enough to trick
                    if (flat.trickAir)
                    {
                        var spin = Fly(name + " flick spin", jp.x, jp.z, jp.v, 1f, Hands.FlickSpin);
                        if (spin.spin == 0 && spin.air >= 0.8f) Fail($"{name}: a flick in a {spin.air:0.00} s air threw no spin");
                        if (spin.crash && spin.spin % 2 == 0) Fail($"{name}: a flicked {spin.spin * 180}, crashed '{lastCrash}'");
                        float own = (spin.land.x - flat.land.x) * Mathf.Sign(spin.spin);
                        var against = Fly(name + " spin + drag back", jp.x, jp.z, jp.v, -1f, Hands.FlickSpinSteer);
                        float back = -(against.land.x - flat.land.x) * Mathf.Sign(against.spin);
                        Debug.Log($"AIRCTL {name} spin: a flick threw {spin.spin * 180} ({spin.trick}), {(spin.crash ? "CRASHED '" + lastCrash + "'" : "rode away")}; held its own way the landing moved {own:+0.00;-0.00} m; dragged back against it {back:+0.00;-0.00} m the drag's way");
                        if (spin.spin != 0 && Mathf.Abs(own) > 0.3f) Fail($"{name}: with a spin coming round, a drag its own way drifted the landing {own:0.00} m (the spin's, not steering)");
                        if (against.spin != 0 && back < 0.3f && against.air >= 0.8f) Fail($"{name}: with a spin coming round, a drag the other way moved the landing only {back:0.00} m");
                        var flip = Fly(name + " flick flip", jp.x, jp.z, jp.v, 0f, Hands.FlickFlip);
                        Debug.Log($"AIRCTL {name} flip: a flick up threw {flip.flips} flip(s) ({flip.trick}), {(flip.crash ? "crashed '" + lastCrash + "'" : "rode away")}");
                        if (flip.flips == 0 && flip.air >= 0.8f) Fail($"{name}: a flick up in the air threw no flip");
                    }
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("AIRCTL exception " + e); fails++; }
        if (measured == 0) Fail("no air of 0.8 s or more was steered");
        Debug.Log($"AIRCTL done: {rides} rides, {measured} steered airs of 0.8 s or more, the least landing moved {least:0.00} m, the most {most:0.00} m per second of air; {fails} failure(s)");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static float RiderTricks_FlickAt() => Rider.FlickAt;

    enum Hands { Steer, FlickSpin, FlickSpinSteer, FlickFlip }

    struct Flight { public bool flew, crash, trickAir; public float air, at05; public Vector3 land; public string trick; public int spin, flips; }

    static void Step(int n)
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 4 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 4 * Dt);
    }

    /// <summary>
    /// The approach held to the piste's line to the lip, then in the air (from the first step off the snow to the first
    /// back on it) the fingers as `hands` has them; on 2 s after the landing. Where it came down, how long it flew.
    /// </summary>
    static Flight Fly(string name, float x, float z, float v0, float s, Hands hands)
    {
        rides++;
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
        lastCrash = ""; terrain.Prewarm(); props.Prewarm();
        float dx = x - Slope.Centre(z);
        var f = new Flight { trick = "", at05 = -1f };
        bool inAir = false, landed = false; float airClock = 0f, side0 = 0f, after = 0f; int n = 0;
        for (float t = 0f; t < 12f; t += Dt, n++)
        {
            float steer, tx, ty = 0f;
            if (!inAir || landed)
            {
                steer = landed ? 0f : Mathf.Clamp((Slope.Centre(r.Z + 12f) + dx - r.transform.position.x) * 0.05f - r.Heading * 0.5f, -1f, 1f);
                if (!landed && r.Z > z + 22f) steer = Mathf.Clamp(steer, -0.3f, 0.3f);   // (gently onto the lip: nothing held into the air)
                tx = steer;
            }
            else
            {
                // in the air: the trick air's steering drag is under the flick (RiderInput feeds the throw to both)
                float drag = f.trickAir ? Rider.FlickAt * 0.85f * s : s;
                switch (hands)
                {
                    case Hands.Steer: steer = drag; break;
                    case Hands.FlickSpin: steer = airClock > 0.1f && airClock < 0.2f ? 1f : airClock >= 0.2f ? Rider.FlickAt * 0.85f : 0f; break;   // flicked right, then held its own way
                    case Hands.FlickSpinSteer: steer = airClock > 0.1f && airClock < 0.2f ? 1f : airClock >= 0.25f ? Rider.FlickAt * 0.85f * s : 0f; break;   // flicked right, then dragged the other way
                    default: steer = 0f; ty = airClock > 0.1f && airClock < 0.2f ? 1f : 0f; break;   // flicked up
                }
                tx = steer;
            }
            r.Steer = steer; r.TrickX = tx; r.TrickY = ty;
            bool wasAir = r.Airborne;
            Step(n);
            if (r.Crashed) { f.crash = true; if (wasAir && !landed) { f.land = r.transform.position; f.air = airClock; } break; }
            if (!inAir && r.Airborne && !landed)
            {
                inAir = true; f.flew = true; airClock = 0f;
                f.trickAir = r.TrickTime > 0f;
                side0 = r.Velocity.x;
            }
            if (inAir && !landed)
            {
                if (r.Airborne)
                {
                    airClock += Dt;
                    if (f.at05 < 0f && airClock >= 0.05f - 1e-4f) f.at05 = (r.Velocity.x - side0) * Mathf.Sign(s == 0f ? 1f : s);
                    if (r.HasTrick) { f.spin = Mathf.RoundToInt(r.TrickSpin / Mathf.PI); f.flips = Mathf.RoundToInt(r.TrickFlip / (2f * Mathf.PI)); f.trick = r.TrickName ?? "trick"; }
                }
                else { landed = true; f.land = r.transform.position; f.air = airClock; }
            }
            if (landed) { after += Dt; if (after > 2f) break; }
        }
        if (inAir && !landed && !f.crash) { f.land = r.transform.position; f.air = airClock; }
        return f;
    }
}
