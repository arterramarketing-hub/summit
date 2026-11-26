using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// How far the start is from the chalet: from the foot of the chalet's front steps, walk (stick part way) and run
/// (stick right out) straight for the start gate's DROP IN, and log the distance and time until the prompt comes up;
/// then the lookout the same way, and where the stations inside stand in the chalet. Then the walk stick itself, through
/// the camp's own frame (Camp.Step, the real MetaUi): a thumb put on the fixed stick in the bottom left and pushed up
/// walks the hero away from the camera; a finger dragged anywhere else turns the camera and doesn't walk.
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
        // the fixed walk stick, through the camp's own frame
        {
            var meta = (MetaUi)typeof(SummitGame).GetField("meta", NP).GetValue(game);
            var fingers = new System.Collections.Generic.List<CampInput.Touch>();
            Camp.TestTouches = fingers;
            void Frame() { camp.Step(dt, meta); body.Apply(dt); camp.CameraStep(dt); }
            Vector3 P() => (Vector3)fPos.GetValue(camp);
            camp.TeleportTo(stairs, Mathf.Atan2(dropIn.x - stairs.x, dropIn.z - stairs.z) * Mathf.Rad2Deg);
            for (int i = 0; i < 30; i++) { fingers.Clear(); Frame(); }
            var home = camp.Controls.Home ?? Vector2.zero;
            float R = camp.Controls.Radius;
            var p0 = P(); float yaw0 = (float)fCamYaw.GetValue(camp);
            for (float t = 0f; t < 2.5f; t += dt) { fingers.Clear(); fingers.Add(new CampInput.Touch(1, home + Vector2.up * R * Mathf.Min(1f, t / 0.2f) * 0.8f)); Frame(); }
            var p1 = P();
            var fwd = new Vector3(Mathf.Sin(yaw0), 0f, Mathf.Cos(yaw0));
            float along = Vector3.Dot(p1 - p0, fwd), walked = Vector3.Distance(new Vector3(p1.x, 0f, p1.z), new Vector3(p0.x, 0f, p0.z));
            bool okWalk = camp.Controls.Home.HasValue && walked > 4f && along > walked * 0.8f;
            for (int i = 0; i < 30; i++) { fingers.Clear(); Frame(); }
            // a finger dragged across the middle of the screen: the camera turns, the hero stays
            var p2 = P(); float yaw2 = (float)fCamYaw.GetValue(camp);
            var mid = new Vector2(Camera.main.pixelWidth * 0.55f, Camera.main.pixelHeight * 0.45f);
            for (float t = 0f; t < 1.2f; t += dt) { fingers.Clear(); fingers.Add(new CampInput.Touch(2, mid + Vector2.right * R * Mathf.Min(1f, t / 0.4f))); Frame(); }
            float turned = Mathf.DeltaAngle(yaw2 * Mathf.Rad2Deg, (float)fCamYaw.GetValue(camp) * Mathf.Rad2Deg);
            float drift = Vector3.Distance(new Vector3(P().x, 0f, P().z), new Vector3(p2.x, 0f, p2.z));
            bool okLook = Mathf.Abs(turned) > 20f && drift < 0.3f;
            fingers.Clear(); Frame();
            Camp.TestTouches = null;
            if (!okWalk) fails++;
            if (!okLook) fails++;
            Debug.Log($"WALK the fixed stick at {home} (radius {R:0} px, screen {Camera.main.pixelWidth}x{Camera.main.pixelHeight}): pushed up 2.5 s, {walked:0.0} m, {along:0.0} m of it away from the camera{(okWalk ? "" : " << DIDN'T WALK")}");
            Debug.Log($"WALK a finger dragged off the stick: the camera turned {turned:0} deg, the hero moved {drift:0.00} m{(okLook ? "" : " << WRONG")}");
        }
        Debug.Log($"WALK done: {fails} fail(s)");
        ChaseCamera.TestDt = 0f;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }
}
