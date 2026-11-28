using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The start of a run, timed: from GO to the lip (the first jump, off the start pad into the face) and on to the first
/// kicker below it, on several mountains, with three riders: hands off, steering for the kicker, and carving side to side
/// all the way (the slowest way down). Logs where the run starts, the speed at each take-off, whether the landing was
/// ridden away, and how far behind the avalanche lets go, so the start can be moved and the jumps still checked.
/// </summary>
public static class GateTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static readonly string[] BotName = { "hands off", "steers for the kicker", "carves side to side" };

    public static void Run()
    {
        var game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        var r = game.rider;
        var body = game.body;
        var terrain = game.terrain;
        var props = game.props;
        var slide = (Avalanche)typeof(SummitGame).GetField("slide", NP).GetValue(game);
        string crash = null;
        r.Crash += why => crash = why;
        int fails = 0;
        float sumLip = 0f, sumKick = 0f; int nLip = 0, nKick = 0;
        foreach (int seed in new[] { 1000, 1077, 1154, 1231, 1308 })
            for (int bot = 0; bot < 3; bot++)
            {
                TestBoot.NewMountain(game, seed, terrain, props);
                typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
                typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
                r.input = null; crash = null;
                float z0 = r.Z;
                // the first kicker below the lip (on the line a rider coming off the lip meets first)
                // (NextKicker names the cell's spot; the course leaves some out, so check one is built there)
                bool hasKick = Slope.NextKicker(Slope.PadLip + 20f, out Vector3 kick) && Slope.Kicker(kick.x, kick.z - 1f) > 0.5f;
                float t = 0f, lipT = -1f, lipV = 0f, kickT = -1f, kickV = 0f, releaseGap = -1f, releaseT = -1f;
                bool wasAir = false, lipLanded = false, kickLanded = false; int jumps = 0;
                for (; t < 30f; t += Dt)
                {
                    Vector3 p = r.Body.position;
                    if (bot == 0) r.Steer = 0f;
                    else
                    {
                        float aimX = bot == 1 && hasKick && kick.z > r.Z - 4f ? kick.x : Slope.Centre(r.Z + 12f) + (bot == 2 ? 9f * Mathf.Sin(t * 1.3f) : 0f);
                        float want = Mathf.Atan2(aimX - p.x, 14f);
                        r.Steer = Mathf.Clamp((want - r.Heading) * 2.2f, -1f, 1f);
                    }
                    TestHarness.Step(r, body, Dt);
                    slide.Step(Dt, r, body);
                    if (slide.JustReleased) { releaseGap = slide.Gap(r); releaseT = t; }
                    bool air = r.Airborne;
                    if (air && !wasAir)
                    {
                        jumps++;
                        if (lipT < 0f && r.Z > Slope.PadLip - 3f && r.Z < Slope.PadLip + 6f) { lipT = t; lipV = r.Speed; }
                        else if (hasKick && kickT < 0f && Mathf.Abs(r.Z - (kick.z)) < 4f) { kickT = t; kickV = r.Speed; }
                    }
                    if (!air && wasAir && !r.Crashed)
                    {
                        if (lipT >= 0f && !lipLanded && kickT < 0f) lipLanded = true;
                        if (kickT >= 0f) kickLanded = true;
                    }
                    wasAir = air;
                    // the start is done once the slide is loose and you are well down the face (or a kicker ridden)
                    if (r.Crashed || (releaseT >= 0f && r.Z > Slope.PadLip + 70f && (kickT < 0f || kickLanded))) break;
                }
                // passed: the lip jumped and ridden away, no fall in the start, and a first kicker cleared where there is one
                bool ok = lipT >= 0f && lipLanded && !r.Crashed && (kickT < 0f || kickLanded);
                if (!ok) fails++;
                if (lipT >= 0f) { sumLip += lipT; nLip++; }
                if (kickT >= 0f) { sumKick += kickT; nKick++; }
                Debug.Log($"GATE seed{seed} {BotName[bot]}: start z={z0:0.0} ({Slope.PadLip - z0:0.0} m to the lip) | lip at {lipT:0.00} s {lipV * 3.6f:0} km/h {(lipLanded ? "landed" : "NOT LANDED")}"
                        + $" | first kicker {(hasKick ? $"z={kick.z:0} ({kick.z - z0:0} m from the start)" : "none on this mountain")} {(kickT >= 0f ? $"at {kickT:0.00} s {kickV * 3.6f:0} km/h {(kickLanded ? "landed" : "NOT LANDED")}" : "not jumped")}"
                        + $" | slide let go at {releaseT:0.0} s, {releaseGap:0} m behind | {(r.Crashed ? "CRASHED: " + crash : "riding")} at z={r.Z:0} {(ok ? "" : " << FAIL")}");
            }
        Debug.Log($"GATE summary: start z={Slope.StartZ:0.0}, gate z={Slope.GateZ:0.0}, lip z={Slope.PadLip:0.0}; mean GO-to-lip {sumLip / Mathf.Max(1, nLip):0.00} s, mean GO-to-first-kicker {sumKick / Mathf.Max(1, nKick):0.00} s; {fails} fails");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }
}
