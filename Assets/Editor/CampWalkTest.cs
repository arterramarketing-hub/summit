using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// How far the start is from the chalet: from the foot of the chalet's front steps, walk (stick part way) and run
/// (stick right out) straight for the start gate's DROP IN, and log the distance and time until the prompt comes up;
/// then the lookout the same way, and where the stations inside stand in the chalet.
/// Batch: -executeMethod CampWalkTest.Run
/// </summary>
public static class CampWalkTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        var camp = (Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = 1f / 60f;
        const float dt = 1f / 60f;
        var fPos = typeof(Camp).GetField("pos", NP); var fCamYaw = typeof(Camp).GetField("camYaw", NP);
        var stairs = (Vector3)typeof(Camp).GetField("stairsFoot", NP).GetValue(camp);
        var spots = (System.Collections.IList)typeof(Camp).GetField("spots", NP).GetValue(camp);
        float cx = Slope.Centre(Slope.PadLip);
        var dropIn = new Vector3(cx, 0f, Slope.GateZ - 2.6f);
        int fails = 0;
        Debug.Log($"WALK chalet at {camp.ChaletPoint(0f, 0f, 0f)} (z from the gate {camp.ChaletPoint(0f, 0f, 0f).z - Slope.GateZ:0.0} m), steps' foot {stairs}, drop-in {dropIn}; straight line {Vector2.Distance(new Vector2(stairs.x, stairs.z), new Vector2(dropIn.x, dropIn.z)):0.0} m");

        float Go(string how, Vector3 from, Vector3 to, float mag, Camp.Station want, bool lookout = false)
        {
            camp.TeleportTo(from, Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg);
            for (int i = 0; i < 20; i++) { camp.Walk(Vector2.zero, dt); camp.CameraStep(dt); }
            float t = 0f, path = 0f;
            Vector3 last = (Vector3)fPos.GetValue(camp);
            for (; t < 60f; t += dt)
            {
                var p = (Vector3)fPos.GetValue(camp);
                if (want != Camp.Station.None ? camp.Near == want : (lookout && camp.OnLookout)) break;
                // the stick: toward the target, in the camera's frame (up the screen is the way it looks)
                float head = Mathf.Atan2(to.x - p.x, to.z - p.z) - (float)fCamYaw.GetValue(camp);
                camp.Walk(new Vector2(Mathf.Sin(head), Mathf.Cos(head)) * mag, dt);
                body.Apply(dt); camp.CameraStep(dt);
                var q = (Vector3)fPos.GetValue(camp);
                path += Vector3.Distance(new Vector3(q.x, 0f, q.z), new Vector3(last.x, 0f, last.z)); last = q;
            }
            bool ok = t < 60f;
            if (!ok) fails++;
            Debug.Log($"WALK {how}: {path:0.0} m in {t:0.0} s{(ok ? "" : " << NEVER GOT THERE, at " + fPos.GetValue(camp))}");
            return t;
        }

        // the chalet's steps to the gate: walking, then running
        Go("chalet steps to DROP IN, walking", stairs, dropIn, 0.85f, Camp.Station.DropIn);
        Go("chalet steps to DROP IN, running", stairs, dropIn, 1f, Camp.Station.DropIn);
        // and every other spot from the steps: still there, still answering
        foreach (var s in spots)
        {
            var st = (Camp.Station)s.GetType().GetField("st").GetValue(s);
            var at = (Vector3)s.GetType().GetField("at").GetValue(s);
            if (st == Camp.Station.DropIn) continue;
            if (st == Camp.Station.Lookout) { Go($"chalet steps to the lookout at {at}", stairs, at, 0.85f, Camp.Station.None, true); continue; }
            // (the stations inside: where they stand in the chalet's own frame, which moves with it; HubShot walks to them)
            var chalet = camp.ChaletPoint(0f, 0f, 0f);
            Debug.Log($"WALK {st} inside the chalet at {Quaternion.Euler(0f, -camp.ChaletYaw, 0f) * (at - chalet)} from its centre");
        }
        Debug.Log($"WALK done: {fails} fail(s)");
        ChaseCamera.TestDt = 0f;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
