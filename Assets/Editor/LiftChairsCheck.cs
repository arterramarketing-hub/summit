using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Are the lifts' chairs (and towers) all there, near and far, on the phone too?
///
/// Rides down seed SUMMIT_LIFT_SEED (4242) past its lifts at 22 m/s, the camera behind as the chase camera has it, the
/// lifts built a few pieces a frame as in the game and the world's far band on (Farview, where the build has it).
/// Every frame, for every lift being drawn: each chair its haul rope carries (every `spacing` metres round the loop,
/// from the clock, as SkiLift places them) that is in view and inside what the band lets in, against what SkiLift
/// asks the GPU to draw: in its near or far list, with the band's and the near/far hand-over's dither leaving any of
/// it (and all of it once through the hand-over). Runs of chairs missing one after another on a rope are gaps.
/// The towers the same way.
///
/// "On the phone" adds what a build keeps: the chairs and towers are instanced draws of one material made at run
/// time; with GraphicsSettings' instancing variants on "Strip Unused", a build keeps a shader's instanced variants only
/// if some material asset it includes uses that shader with GPU instancing on. If none does, every one of those
/// instanced draws comes out as nothing on the device (the editor keeps every variant, so it never shows there).
///
/// -executeMethod LiftChairsCheck.Ride (-nographics is fine). Exit 1 if any chair or tower in view is missing on
/// the phone, or any rope shows a gap. Writes Logs/liftchairs.txt.
/// </summary>
public static class LiftChairsCheck
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags SP = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    class PerLift { public int expected, drawn, phone, partly, worstGap, towersExpected, towersDrawn, towersPhone; public float worstGapM; }

    public static void Ride()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var lift = Object.FindFirstObjectByType<SkiLift>();
        var lUpd = typeof(SkiLift).GetMethod("Update", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_LIFT_SEED"), out int sd) ? sd : 4242;
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild();
        SkiLift.SpreadBuild = true;
        var cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        cam.fieldOfView = 62f;

        // SkiLift's insides (by reflection, so this runs on every commit being bisected)
        object Field(string n) => typeof(SkiLift).GetField(n, NP).GetValue(lift);
        var partT = typeof(SkiLift).GetNestedType("Part", BindingFlags.NonPublic);
        var listF = partT.GetField("list"); var meshF = partT.GetField("mesh");
        List<Matrix4x4> L(object part) => part != null ? (List<Matrix4x4>)listF.GetValue(part) : null;
        bool HasMesh(object part) => part != null && meshF.GetValue(part) as Mesh != null;
        object chair = Field("chair"), chairLod = Field("chairLod"), mast = Field("mast"), mastLod = Field("mastLod");
        var paint = (Material)Field("paint");
        var live = (System.Collections.IList)Field("live");
        var liveT = typeof(SkiLift).GetNestedType("Live", BindingFlags.NonPublic);
        var liftF = liveT.GetField("lift"); var spanF = liveT.GetField("span");
        float behind = lift.behind;
        float farRange = (float)typeof(SkiLift).GetField("FarRange", SP).GetValue(null);
        float chairNear = (float)typeof(SkiLift).GetField("ChairNear", SP).GetValue(null);
        float nearRange = (float)typeof(SkiLift).GetField("NearRange", SP).GetValue(null);
        var handF = typeof(SkiLift).GetField("Hand", SP);
        float hand = handF != null ? (float)handF.GetValue(null) : 0f;

        // the far band (Farview: from farview on)
        var fvT = System.Type.GetType("Summit.Farview, Assembly-CSharp");
        object fv = fvT?.GetProperty("Instance", SP)?.GetValue(null);
        var fvApply = fvT?.GetMethod("Apply"); var fvStep = fvT?.GetMethod("Step");
        var fvShown = fvT?.GetMethod("Shown", SP);
        if (fvT != null) { fvT.GetField("TestOn", SP).SetValue(null, true); fvT.GetField("TestClock", SP)?.SetValue(null, 0.0); }
        bool bandOn() => fvT != null && (bool)fvT.GetProperty("On", SP).GetValue(null);
        float Shown(Vector3 at) => fvShown != null && bandOn() ? (float)fvShown.Invoke(null, new object[] { at }) : 1f;

        // what a build keeps
        var sb = new StringBuilder();
        bool paintKept = KeptInstanced(paint.shader, out string paintBy);
        var people = PeopleKit.LodMaterial;
        bool peopleKept = people != null && KeptInstanced(people.shader, out _);
        sb.AppendLine($"LIFTCHAIRS chairs/towers drawn instanced with '{paint.name}' (shader {paint.shader.name}, GPU instancing {paint.enableInstancing}): " +
                      (paintKept ? $"a build keeps its instanced variants ({paintBy})" :
                       "a build KEEPS NONE of its instanced variants (no material asset in the build uses that shader with GPU instancing; GraphicsSettings strips unused instancing variants): on the phone every chair, tower, head, sheave train, pad and station drawn this way is nothing"));
        sb.AppendLine($"LIFTCHAIRS riders (people) drawn instanced with {(people != null ? people.shader.name : "none")}: {(peopleKept ? "kept by a build" : "NOT kept by a build")}");
        sb.AppendLine($"LIFTCHAIRS far band (Farview): {(fvT == null ? "not in this build" : fv == null ? "class present, no instance" : "on")}; near/far hand-over {(hand > 0f ? hand + " m either side" : "none (at once)")}");

        float zTop = float.MaxValue, zBot = float.MinValue;
        foreach (var l in LiftLine.Lifts) { zTop = Mathf.Min(zTop, l.ZMin); zBot = Mathf.Max(zBot, l.ZMax); }
        float z0 = Mathf.Max(0f, zTop - 100f), z1 = zBot + 50f;
        const float dt = 1f / 30f, speed = 22f;
        var planes = new Plane[6];
        var per = new Dictionary<int, PerLift>();
        int frames = 0, expectedAll = 0, drawnAll = 0, phoneAll = 0, partlyAll = 0, listedMissing = 0, extras = 0, worstGap = 0;
        int towersExpected = 0, towersDrawn = 0, towersPhone = 0;
        float worstGapM = 0f, nearestMissing = float.MaxValue;
        int ridersWith = 0, ridersMissing = 0;
        Vector3 eye = Vector3.zero;
        LiftRiders.Seen = (id, c, lap, pos, count, shown) =>
        {
            var b = new Bounds(pos + Vector3.down * 3.0f, new Vector3(2.6f, 2.4f, 2.2f));
            if (!GeometryUtility.TestPlanesAABB(planes, b) || (pos - eye).magnitude > cam.farClipPlane || Shown(pos) <= 0f) return;
            if (count > 0) { ridersWith++; if (!shown || !peopleKept) ridersMissing++; }
        };
        var missingRun = new List<bool>();
        var listed = new Dictionary<(float, float), int>(); var found = new HashSet<(float, float)>();
        for (float z = z0; z < z1; z += speed * dt)
        {
            float x = Slope.Centre(z);
            var at = new Vector3(x, Slope.DrawnHeight(x, z), z);
            rider.ResetAt(x, z); body.ResetVisual(); rider.Stop();
            cam.transform.SetPositionAndRotation(at + new Vector3(0f, 3.2f, -6.5f), Quaternion.LookRotation(new Vector3(0f, -0.28f, 1f)));
            eye = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            double clock = 300.0 + frames * dt;
            SkiLift.TestClock = clock;
            if (fvT != null) { fvT.GetField("TestClock", SP)?.SetValue(null, clock); fv = fvT.GetProperty("Instance", SP).GetValue(null); if (fv != null) fvApply.Invoke(fv, null); }
            lUpd.Invoke(lift, null);
            if (fv != null) fvStep?.Invoke(fv, null);
            frames++;
            var tgt = (Transform)Field("target");
            Vector3 p = tgt != null ? tgt.position : at;

            // what SkiLift listed this frame
            listed.Clear();
            var nearL = L(chair); var farL = L(chairLod);
            bool nearMesh = HasMesh(chair), farMesh = HasMesh(chairLod);
            foreach (var m in nearL) { var q = (Vector3)m.GetColumn(3); var k = (q.x, q.z); listed.TryGetValue(k, out int f); listed[k] = f | 1; }
            foreach (var m in farL) { var q = (Vector3)m.GetColumn(3); var k = (q.x, q.z); listed.TryGetValue(k, out int f); listed[k] = f | 2; }
            found.Clear();

            foreach (var lvo in live)
            {
                var l = (LiftLine.Lift)liftF.GetValue(lvo);
                if (!per.TryGetValue(l.id, out var pl)) per[l.id] = pl = new PerLift();
                double moved = clock * LiftLine.Speed;
                missingRun.Clear();
                for (int c = 0; c < l.chairs; c++)
                {
                    double along = c * (double)l.spacing + moved;
                    int lap = (int)System.Math.Floor(along / l.loop);
                    l.Loop((float)(along - lap * (double)l.loop), out Vector3 pos, out Vector3 travel, out bool up, out float since);
                    if (pos.z < p.z - behind || pos.z > p.z + farRange) { missingRun.Add(false); continue; }
                    if (new Vector3(travel.x, 0f, travel.z).sqrMagnitude < 1e-6f) { missingRun.Add(false); continue; }
                    // in SkiLift's lists? (the near one, the far one, or both through the hand-over)
                    // (the same sums as SkiLift's, so the very same numbers)
                    listed.TryGetValue((pos.x, pos.z), out int fl);
                    if (fl != 0) found.Add((pos.x, pos.z));
                    bool inNear = (fl & 1) != 0, inFar = (fl & 2) != 0;
                    if (!inNear && !inFar) listedMissing++;
                    // in view, and let in by the band?
                    var b = new Bounds(pos + Vector3.down * 2.3f, new Vector3(2.8f, 4.6f, 2.4f));
                    float shown = Shown(pos);
                    if (!GeometryUtility.TestPlanesAABB(planes, b) || (pos - eye).magnitude > cam.farClipPlane || shown <= 0f) { missingRun.Add(false); continue; }
                    // how much of it the near/far dither leaves (1: all of it)
                    float d = Mathf.Sqrt((pos.x - p.x) * (pos.x - p.x) + (pos.z - p.z) * (pos.z - p.z));
                    float cover;
                    if (hand > 0f && bandOn())
                    {
                        float k = Mathf.Clamp01((d - (chairNear - hand)) / (2f * hand));
                        cover = (inNear && nearMesh ? 1f - k : 0f) + (inFar && farMesh ? k : 0f);
                    }
                    else cover = (inNear && nearMesh) || (inFar && farMesh) ? 1f : 0f;
                    bool drawn = cover > 0.001f, whole = cover > 0.999f;
                    pl.expected++; expectedAll++;
                    if (drawn) { pl.drawn++; drawnAll++; if (paintKept) { pl.phone++; phoneAll++; } }
                    else nearestMissing = Mathf.Min(nearestMissing, (pos - eye).magnitude);
                    if (drawn && !whole) { pl.partly++; partlyAll++; }
                    missingRun.Add(!(drawn && paintKept));
                }
                // gaps: chairs missing one after another along the rope (round the loop)
                int run = 0, best = 0, n = missingRun.Count;
                for (int i = 0; i < 2 * n; i++) { if (missingRun[i % n]) { run++; best = Mathf.Max(best, Mathf.Min(run, n)); } else run = 0; }
                if (best > pl.worstGap) { pl.worstGap = best; pl.worstGapM = best * l.spacing; }
                if (best > worstGap) { worstGap = best; worstGapM = best * l.spacing; }

                // the towers
                var mastN = L(mast); var mastF = L(mastLod);
                for (int i = 0; i < l.s.Length; i++)
                {
                    Vector3 f = l.foot[i];
                    // (SkiLift lists its towers at each refresh, every 12 m ridden: one at the very ends of its range
                    // may be a refresh late, as it always was. Counted from 13 m inside them.)
                    if (f.z < p.z - behind + 13f || f.z > p.z + farRange - 13f) continue;
                    var tb = new Bounds(f + Vector3.up * (l.h[i] * 0.5f), new Vector3(3f, l.h[i] + 2f, 3f));
                    if (!GeometryUtility.TestPlanesAABB(planes, tb) || (f - eye).magnitude > cam.farClipPlane || Shown(f) <= 0f) continue;
                    bool got = false;
                    foreach (var m in mastN) if (((Vector3)m.GetColumn(3) - f).sqrMagnitude < 4f * 4f) got = true;
                    foreach (var m in mastF) if (((Vector3)m.GetColumn(3) - f).sqrMagnitude < 4f * 4f) got = true;
                    pl.towersExpected++; towersExpected++;
                    if (got) { pl.towersDrawn++; towersDrawn++; if (paintKept) { pl.towersPhone++; towersPhone++; } }
                }
            }
            extras += listed.Count - found.Count;
        }
        LiftRiders.Seen = null;
        if (fvT != null) { fvT.GetField("TestOn", SP).SetValue(null, false); fvT.GetField("TestClock", SP)?.SetValue(null, -1.0); }

        float Pct(int a, int b) => b > 0 ? a * 100f / b : 100f;
        sb.AppendLine($"LIFTCHAIRS seed {seed}: {LiftLine.Lifts.Length} lifts, ridden from z {z0:0} to {z1:0} at {speed} m/s, {frames} frames");
        sb.AppendLine($"LIFTCHAIRS chair-frames in view on a drawn rope: {expectedAll}; drawn (editor) {drawnAll} ({Pct(drawnAll, expectedAll):0.0}%), on the phone {phoneAll} ({Pct(phoneAll, expectedAll):0.0}%); partly (through a hand-over's or the band's dither) {partlyAll}");
        sb.AppendLine($"LIFTCHAIRS worst gap on a rope: {worstGap} chairs one after another ({worstGapM:0} m); nearest chair missing in the editor: {(nearestMissing < float.MaxValue ? nearestMissing.ToString("0") + " m" : "none")}");
        sb.AppendLine($"LIFTCHAIRS chairs in range not listed by SkiLift: {listedMissing}; listed with no chair there (doubles, wrong spacing): {extras}");
        sb.AppendLine($"LIFTCHAIRS tower-frames in view: {towersExpected}; drawn (editor) {towersDrawn} ({Pct(towersDrawn, towersExpected):0.0}%), on the phone {towersPhone} ({Pct(towersPhone, towersExpected):0.0}%)");
        sb.AppendLine($"LIFTCHAIRS chair-frames in view with people aboard: {ridersWith}; drawn without them (phone): {ridersMissing}");
        foreach (var kv in per)
        {
            var pl = kv.Value;
            if (pl.expected == 0 && pl.towersExpected == 0) continue;
            sb.AppendLine($"LIFTCHAIRS   lift {kv.Key}: chairs {pl.expected} in view, editor {pl.drawn}, phone {pl.phone}, partly {pl.partly}, worst gap {pl.worstGap} ({pl.worstGapM:0} m); towers {pl.towersExpected}, editor {pl.towersDrawn}, phone {pl.towersPhone}");
        }
        Debug.Log(sb.ToString());
        System.IO.Directory.CreateDirectory("Logs");
        System.IO.File.WriteAllText("Logs/liftchairs.txt", sb.ToString());
        SkiLift.TestClock = -1.0;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        bool ok = phoneAll == expectedAll && towersPhone == towersExpected && worstGap == 0 && listedMissing == 0 && extras == 0 && ridersMissing == 0;
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool KeptInstanced(Shader s, out string by) => InstancedAssets.KeptInstanced(s, out by);
}
