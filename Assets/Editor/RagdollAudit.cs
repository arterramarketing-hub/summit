using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// How a thrown body moves, as anatomy: the same crashes GlitchTest throws (four mountains; 8, 18 and 30 m/s; left
/// alone, steered hard one way and wagged), each played out for 6 s, measuring every step:
/// - knees and elbows: how far past straight they bend (hyperextension), and how far out of their hinge's plane
///   (bent sideways), from the hinge each had when the crash began (in the thigh's / upper arm's own frame);
/// - hips: how far the thigh swings out sideways from the pelvis;
/// - spin: the fastest any part turns, and the share of part-steps turning faster than 40 rad/s (a shaking limb);
/// - flung: any part faster than the speed thrown + 6 m/s (GlitchTest's spike), or a limb swinging past the pelvis
///   faster than 14 m/s;
/// - the board: how far its middle is from the middle of the two ankles (bound on: stays ~ the stance apart).
/// Lines start RAGAUDIT; the last is the totals. Exit 0 always (it measures; the thresholds are in the report).
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod RagdollAudit.Run -logFile audit.log
/// </summary>
public static class RagdollAudit
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;

    class Limb { public Transform a, b, c; public Vector3 hingeLocal; public string name; }

    public static void Run()
    {
        var game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        var rider = game.rider;
        var body = game.body;
        var terrain = game.terrain;
        var props = game.props;
        var rig = (RiderRig)typeof(RiderBody).GetField("rig", NP).GetValue(body);
        rider.input = null;
        var dressing = Object.FindFirstObjectByType<CourseDressing>();
        if (dressing != null) { var tgt = dressing.target; dressing.target = null; dressing.Rebuild(); dressing.target = tgt; }
        var tot = new Stats();
        int n = 0;
        try
        {
            for (int seed = 0; seed < 4; seed++)
            {
                TestBoot.NewMountain(game, 1000 + seed * 77, terrain, props);
                foreach (float v0 in new[] { 8f, 18f, 30f })
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        System.Func<float, float> steer = mode == 0 ? null : mode == 1 ? (t => 1f) : (System.Func<float, float>)(t => Mathf.Sign(Mathf.Sin(t * 4f)));
                        var s = Crash(rider, body, rig, terrain, props, 300f + seed * 40f, v0, steer);
                        Debug.Log($"RAGAUDIT seed{seed} v{v0} {(mode == 0 ? "alone" : mode == 1 ? "steer+1" : "wag")}: {s}");
                        tot.Add(s); n++;
                    }
            }
        }
        catch (System.Exception e) { Debug.LogError("RAGAUDIT exception " + e); }
        Debug.Log($"RAGAUDIT TOTAL {n} crashes: {tot}");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>One crash traced (GlitchTest's seed 3, 30 m/s, wagged, getting up once at rest): where the body is every quarter second.</summary>
    public static void Trace()
    {
        var game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        var r = game.rider;
        var body = game.body;
        var terrain = game.terrain;
        var props = game.props;
        r.input = null;
        var dressing = Object.FindFirstObjectByType<CourseDressing>();
        if (dressing != null) { var tgt = dressing.target; dressing.target = null; dressing.Rebuild(); dressing.target = tgt; }
        TestBoot.NewMountain(game, 1000 + 3 * 77, terrain, props);
        float z = 300f + 3 * 40f, v0 = 30f;
        Random.InitState(4242);
        r.ResetAt(Slope.Centre(z), z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.forward * v0;
        terrain.Prewarm(); props.Prewarm();
        for (int i = 0; i < 30; i++) { TestHarness.Step(r, body, Dt); body.Apply(Dt); }
        r.DoCrash("test");
        int n = 0;
        for (float t = 0f; t < 8f; t += Dt, n++)
        {
            r.Steer = Mathf.Sign(Mathf.Sin(t * 4f));
            if (r.CanGetUp && !body.RagdollMoving) r.RequestUp = true;
            TestHarness.Step(r, body, Dt); body.Apply(Dt);
            Vector3 c = body.RagdollCentre;
            if (n % 30 == 0 || (c.y < Slope.DrawnHeight(c.x, c.z) - 1f && n % 6 == 0))
                Debug.Log($"RAGTRACE t={t:0.00} c={c} snow={Slope.DrawnHeight(c.x, c.z):0.00} x-c={c.x - Slope.Centre(c.z):0.0} crevasse={Course.CrevasseNear(c.x, c.z, 4f)} cut={Course.CrevasseCut(c.x, c.z):0.0} v={body.RagdollSpeed:0.0} crashed={r.Crashed} moving={body.RagdollMoving} hold={r.HoldCrash} pending={body.FallPending}");
            if (!r.Crashed) { Debug.Log($"RAGTRACE up at t={t:0.00}"); break; }
        }
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    class Stats
    {
        public float kneeHyper, kneeSide, elbowHyper, elbowSide, hipOut, maxSpin, boardFar, limbRel, partFast;
        public int shakeSteps, steps, spikes, kneeBadSteps, elbowBadSteps, legSteps;
        public string firstSpike = "", spinNote = "";
        public void Add(Stats o)
        {
            kneeHyper = Mathf.Max(kneeHyper, o.kneeHyper); kneeSide = Mathf.Max(kneeSide, o.kneeSide);
            elbowHyper = Mathf.Max(elbowHyper, o.elbowHyper); elbowSide = Mathf.Max(elbowSide, o.elbowSide);
            hipOut = Mathf.Max(hipOut, o.hipOut); maxSpin = Mathf.Max(maxSpin, o.maxSpin); boardFar = Mathf.Max(boardFar, o.boardFar);
            limbRel = Mathf.Max(limbRel, o.limbRel); partFast = Mathf.Max(partFast, o.partFast);
            shakeSteps += o.shakeSteps; steps += o.steps; spikes += o.spikes; kneeBadSteps += o.kneeBadSteps; elbowBadSteps += o.elbowBadSteps; legSteps += o.legSteps;
        }
        public override string ToString() =>
            $"knee past straight {kneeHyper:0}deg, knee sideways {kneeSide:0}deg, knees out of range {100f * kneeBadSteps / Mathf.Max(1, legSteps):0.0}% of steps; " +
            $"elbow past straight {elbowHyper:0}deg, elbow sideways {elbowSide:0}deg, elbows out of range {100f * elbowBadSteps / Mathf.Max(1, legSteps):0.0}%; " +
            $"thigh out sideways {hipOut:0}deg;  spin max {maxSpin:0} rad/s ({spinNote}), shaking (>40 rad/s) {100f * shakeSteps / Mathf.Max(1, steps):0.00}% of part-steps; " +
            $"spikes {spikes}{(firstSpike != "" ? " (first: " + firstSpike + ")" : "")}, fastest part over thrown {partFast:0.0} m/s, limb past pelvis {limbRel:0.0} m/s; board from feet {boardFar:0.00} m";
    }

    static Stats Crash(Rider r, RiderBody body, RiderRig rig, SlopeStreamer terrain, Forest props, float z, float v0, System.Func<float, float> steer)
    {
        var st = new Stats();
        r.Steer = 0f;
        Random.InitState(4242);
        r.ResetAt(Slope.Centre(z), z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.forward * v0;
        terrain.Prewarm(); props.Prewarm();
        for (int i = 0; i < 30; i++) { TestHarness.Step(r, body, Dt); body.Apply(Dt); }
        // the body's frame and each hinge, from the pose it is thrown in
        Vector3 right = (rig.ThighR.position - rig.ThighL.position).normalized;
        Vector3 up = Vector3.ProjectOnPlane(rig.Chest.position - rig.Hips.position, right).normalized;
        Vector3 fwd = Vector3.Cross(right, up);
        Limb Make(string nm, Transform a, Transform b, Transform c, Vector3 bendFallback)
        {
            Vector3 u = b.position - a.position, w = c.position - b.position;
            Vector3 h = Vector3.Angle(u, w) > 8f ? Vector3.Cross(u, w) : Vector3.Cross(u, bendFallback);
            return new Limb { a = a, b = b, c = c, name = nm, hingeLocal = a.InverseTransformDirection(h.normalized) };
        }
        var knees = new[] { Make("kneeL", rig.ThighL, rig.ShinL, rig.EndL, -fwd), Make("kneeR", rig.ThighR, rig.ShinR, rig.EndR, -fwd) };
        var elbows = new[] { Make("elbowL", rig.UpperL, rig.LowerL, rig.HandL, fwd), Make("elbowR", rig.UpperR, rig.LowerR, rig.HandR, fwd) };
        // the hips: the thigh's angle out from the pelvis's side-to-side plane, at rest
        r.DoCrash("audit");
        var rd = body.RagdollForTest;
        var rbs = new List<Rigidbody>(rd.Bodies);
        var all = new List<Rigidbody>();
        foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (!rb.isKinematic && rb != r.Body) all.Add(rb);
        Rigidbody board = null;
        for (float t = 0f; t < 6f && r.Crashed; t += Dt)
        {
            r.Steer = steer != null ? steer(t) : 0f;
            TestHarness.Step(r, body, Dt); body.Apply(Dt);
            if (!rd.Active) break;
            if (board == null) { foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (rb.name == "Board" && !rb.isKinematic) board = rb; }
            Vector3 pv = rbs[0].linearVelocity;
            foreach (var rb in rbs)
            {
                if (rb == null || rb.isKinematic) continue;
                float w = rb.angularVelocity.magnitude;
                if (w > st.maxSpin) st.spinNote = $"{rb.name} t={t:0.00} {rb.position.y - Slope.DrawnHeight(rb.position.x, rb.position.z):0.00} m over the snow";
                st.maxSpin = Mathf.Max(st.maxSpin, w);
                if (w > 40f) st.shakeSteps++;
                st.steps++;
                float over = rb.linearVelocity.magnitude - v0;
                if (over > 6f) { st.spikes++; if (st.firstSpike == "") st.firstSpike = $"{rb.name} {rb.linearVelocity.magnitude:0.0} m/s at t={t:0.00}, pelvis {pv.magnitude:0.0}, {rb.position.y - Slope.DrawnHeight(rb.position.x, rb.position.z):0.00} m over the snow"; }
                st.partFast = Mathf.Max(st.partFast, over);
                if (rb != rbs[0]) st.limbRel = Mathf.Max(st.limbRel, (rb.linearVelocity - pv).magnitude);
            }
            foreach (var k in knees) { Bend(k, out float flex, out float side); if (-flex > st.kneeHyper + 20f && -flex > 40f) Debug.Log($"RAGAUDIT   {k.name} {-flex:0} past straight at t={t:0.00}"); st.kneeHyper = Mathf.Max(st.kneeHyper, -flex); st.kneeSide = Mathf.Max(st.kneeSide, side); if (flex < -10f || side > 20f) st.kneeBadSteps++; }
            foreach (var e in elbows) { Bend(e, out float flex, out float side); st.elbowHyper = Mathf.Max(st.elbowHyper, -flex); st.elbowSide = Mathf.Max(st.elbowSide, side); if (flex < -10f || side > 25f) st.elbowBadSteps++; }
            st.legSteps += 2;
            // the thigh out sideways: its angle to the pelvis's mid plane, beyond 60 degrees is a split no hip makes
            Vector3 pr = (rig.ThighR.position - rig.ThighL.position).normalized;
            foreach (var (th, sh, sgn) in new[] { (rig.ThighL, rig.ShinL, -1f), (rig.ThighR, rig.ShinR, 1f) })
            {
                Vector3 td = (sh.position - th.position).normalized;
                st.hipOut = Mathf.Max(st.hipOut, Mathf.Asin(Mathf.Clamp(Vector3.Dot(td, pr) * sgn, -1f, 1f)) * Mathf.Rad2Deg);
            }
            if (board != null && board.gameObject.activeInHierarchy && !board.isKinematic)
                st.boardFar = Mathf.Max(st.boardFar, (board.worldCenterOfMass - (rig.EndL.position + rig.EndR.position) * 0.5f).magnitude);
        }
        r.Steer = 0f;
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 240 && r.Crashed; i++) { TestHarness.Step(r, body, Dt); body.Apply(Dt); if (r.CanGetUp) r.RequestUp = true; } }
        return st;
    }

    /// <summary>A hinge's bend now: flexion (+ bent the right way, - past straight) and how far out of its plane (degrees).</summary>
    static void Bend(Limb l, out float flex, out float side)
    {
        Vector3 u = l.b.position - l.a.position, w = l.c.position - l.b.position;
        Vector3 h = l.a.TransformDirection(l.hingeLocal);
        side = Mathf.Abs(90f - Vector3.Angle(w, h));
        Vector3 wp = Vector3.ProjectOnPlane(w, h);
        flex = wp.sqrMagnitude < 1e-6f ? 0f : Vector3.SignedAngle(u, wp, h);
    }
}
