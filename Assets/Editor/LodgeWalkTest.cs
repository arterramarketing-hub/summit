using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Walks the lodge (CampLodge.cs) the way a player does, with the stick: from where you arrive in camp, up the steps,
/// in at the door, to each station inside (the wardrobe, the missions board, the tune-up bench), into the wing, out
/// the side door to the racks, round the terrace and down the wing's steps, and from the wardrobe out and down to
/// DROP IN at the gate. A leg fails if the walker doesn't reach its mark in time (blocked, or stuck on something).
/// Then it checks the lodge's walking against what is drawn there, with the drawn meshes given colliders for the test:
///   - every solid box the walker is pushed out of has drawn geometry along its sides (no invisible walls);
///   - nowhere the walker can stand has drawn geometry through its body (nothing to walk through);
///   - every floor the walker stands on has a drawn surface just under it.
/// Writes LodgeWalk_map_A/B/C.png (top down: blocks red, floors green, the walks blue, any problem magenta) with the GPU.
/// Each of the lodge's layouts in turn (LODGE_LAYOUTS=A,B,C,B1,B2,B3,B2R, or some of them).
/// Batch: -executeMethod LodgeWalkTest.Run (exit 1 on a failed leg or a problem); $LODGE_SHOTS for the map's folder.
/// </summary>
public static class LodgeWalkTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float dt = 1f / 60f;
    const int AuditLayer = 27;
    static Camp camp; static RiderBody body;
    static FieldInfo fPos, fCamYaw;
    static readonly List<Vector3> trail = new List<Vector3>();
    static readonly List<(Vector3 at, string what)> problems = new List<(Vector3, string)>();
    static int fails;
    static int layout, problemsFrom;
    // each layout's floors above the hall's: C's mezzanine, B1's gallery, B2's loft, B3's landing and balcony, B2R's loft
    static readonly float[][] Levels = { new[] { 0f }, new[] { 0f }, new[] { 0f, 3.8f }, new[] { 0f, 3.8f }, new[] { 0f, 3.8f }, new[] { 0f, 1.9f, 3.8f }, new[] { 0f, 3.8f } };
    static string Key => Camp.LodgeLayoutKeys[layout];

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        try
        {
            TestBoot.Boot(game);
            camp = (Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
            body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
            typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
            ChaseCamera.TestDt = dt;
            fPos = typeof(Camp).GetField("pos", NP); fCamYaw = typeof(Camp).GetField("camYaw", NP);
            // each of the lodge's layouts in turn (LODGE_LAYOUTS, comma-separated, default all: "A,B,C,B1,B2,B3")
            var which = System.Environment.GetEnvironmentVariable("LODGE_LAYOUTS");
            if (string.IsNullOrEmpty(which)) which = string.Join(",", Camp.LodgeLayoutKeys);
            foreach (var k in which.Split(','))
            {
                layout = System.Array.IndexOf(Camp.LodgeLayoutKeys, k.Trim());
                if (layout < 0) continue;
                PlayerPrefs.SetInt(Camp.LodgePref, layout);
                camp.Rebuild();
                Physics.SyncTransforms();
                camp.TeleportTo(L(4.5f, 16f), camp.ChaletYaw + 180f);
                int f0 = fails, p0 = problems.Count;
                trail.Clear(); problemsFrom = p0;
                Debug.Log($"LODGEWALK ===== layout {Camp.LodgeLayoutName}");
                Walks();
                People();
                Audit();
                Map();
                Debug.Log($"LODGEWALK layout {Key}: {fails - f0} failed leg(s), {problems.Count - p0} problem(s)");
            }
            Switch((MetaUi)typeof(SummitGame).GetField("meta", NP).GetValue(game));
        }
        finally
        {
            ChaseCamera.TestDt = 0f;
            TestPrefs.Restore();
        }
        Debug.Log($"LODGEWALK done: {fails} failed leg(s), {problems.Count} problem(s)");
        EditorApplication.Exit(fails == 0 && problems.Count == 0 ? 0 : 1);
    }

    static Vector3 L(float x, float z) => camp.ChaletPoint(x, 0f, z);
    /// <summary>A point upstairs (the floors at +3.8).</summary>
    static Vector3 U(float x, float z) => camp.ChaletPoint(x, 3.8f, z);

    /// <summary>Walk through the marks in turn (stick 0.85, or 1 to run); true if each is reached (and the station wanted comes up at the end).</summary>
    static float Leg(string name, Vector3 from, Vector3[] marks, Camp.Station want = Camp.Station.None, float stick = 0.85f, bool teleport = true)
    {
        if (teleport)
        {
            var first = marks[0];
            camp.TeleportTo(from, Mathf.Atan2(first.x - from.x, first.z - from.z) * Mathf.Rad2Deg);
            for (int i = 0; i < 10; i++) { camp.Walk(Vector2.zero, dt); camp.CameraStep(dt); }
        }
        float t = 0f, path = 0f, since = 0f;
        Vector3 last = (Vector3)fPos.GetValue(camp);
        int k = 0;
        bool ok = true;
        while (k < marks.Length)
        {
            var p = (Vector3)fPos.GetValue(camp);
            var to = marks[k];
            float d = new Vector2(to.x - p.x, to.z - p.z).magnitude;
            bool lastMark = k == marks.Length - 1;
            if (lastMark && want != Camp.Station.None ? camp.Near == want : d < 0.6f) { k++; since = 0f; continue; }
            if (since > 12f) { ok = false; break; }
            float head = Mathf.Atan2(to.x - p.x, to.z - p.z) - (float)fCamYaw.GetValue(camp);
            camp.Walk(new Vector2(Mathf.Sin(head), Mathf.Cos(head)) * stick, dt);
            body.Apply(dt); camp.CameraStep(dt);
            var q = (Vector3)fPos.GetValue(camp);
            float step = new Vector2(q.x - last.x, q.z - last.z).magnitude;
            path += step; last = q;
            if (((int)(t / dt)) % 3 == 0) trail.Add(q);
            t += dt; since += dt;
            if (camp.Falling) { ok = false; break; }
        }
        var end = (Vector3)fPos.GetValue(camp);
        if (!ok)
        {
            fails++;
            problems.Add((end, "stuck on " + name));
            Debug.LogError($"LODGEWALK {name}: NEVER REACHED mark {k} at {marks[k]}; stuck at {end} after {t:0.0} s ({camp.DebugHere()})");
        }
        else Debug.Log($"LODGEWALK {name}: {path:0.0} m in {t:0.0} s{(want != Camp.Station.None ? ", " + want + " came up" : "")}; feet {end.y - camp.ChaletPoint(0, 0, 0).y:0.00} from the lodge's floor");
        return t;
    }

    static void Walks()
    {
        float cx = Slope.Centre(Slope.PadLip);
        var arrive = new Vector3(cx - 1.5f, 0f, -64f);
        var dropIn = new Vector3(cx, 0f, Slope.GateZ - 2.6f);
        var foot = camp.StairsFoot;
        var style = camp.StationPoint(Camp.Station.Style);
        var missions = camp.StationPoint(Camp.Station.Missions);
        var ups = camp.StationPoints(Camp.Station.Upgrades);
        Vector3 counter = ups[0], bench = ups[ups.Count - 1];
        foreach (var u in ups) { var l = Quaternion.Euler(0f, -camp.ChaletYaw, 0f) * (u - camp.ChaletPoint(0, 0, 0)); if (l.z > 2f) bench = u; else counter = u; }
        var door = L(4.5f, 5.5f);
        Debug.Log($"LODGEWALK lodge at {camp.ChaletPoint(0, 0, 0)}, steps' foot {foot} ({camp.LodgeStepsShowing} steps showing, the floor {camp.LodgeFrontDrop:0.00} m above the snow there); door {door}; DROP IN {dropIn}; straight line steps-to-DROP IN {Vector2.Distance(new Vector2(foot.x, foot.z), new Vector2(dropIn.x, dropIn.z)):0.0} m");
        Leg("arrival to the steps, up them and in at the door", arrive, new[] { foot, L(0f, 12f), L(2.5f, 8.2f), door });
        Vector3[] toDropIn;
        if (layout == 0)
        {
            Leg("the door to the wardrobe (the outfitters)", door, new[] { L(0f, 1.0f), L(-4.2f, -1.0f), L(-4.2f, -4.2f), style }, Camp.Station.Style);
            Leg("the wardrobe to the missions board", style, new[] { L(-4.2f, -4.2f), L(0f, -4.5f), missions }, Camp.Station.Missions);
            Leg("the missions board to the board shop's counter", missions, new[] { L(4.0f, -3.0f), L(4.5f, -0.4f), counter }, Camp.Station.Upgrades);
            Leg("the counter to the tuning bench", counter, new[] { L(7.2f, 2.5f), bench }, Camp.Station.Upgrades);
            Leg("the tuning bench along the spine into the café", bench, new[] { L(6f, 1.0f), L(-6f, 0.9f), L(-10f, 0.8f), L(-13.5f, 2.2f) });
            Leg("the café back to the lounge by the fire", L(-13.5f, 2.2f), new[] { L(-10f, 0.8f), L(-2.6f, 1.2f), L(-3.0f, 3.4f) });
            Leg("the lounge out the side door to the racks", L(-3.0f, 3.4f), new[] { L(-2.6f, 1.2f), L(0f, 1.0f), L(8f, 3.9f), L(10.4f, 3.95f), L(11.6f, 3.95f), L(14.75f, 3.95f), L(14.75f, -2.1f), L(14.75f, -4.6f) });
            toDropIn = new[] { L(-4.2f, -4.2f), L(-4.2f, -1.0f), L(0f, 1.0f), door, L(2f, 9f), foot, dropIn };
        }
        else if (layout == 1)
        {
            Leg("the door to the wardrobe (the boutique's back)", door, new[] { L(2.5f, 1.2f), L(-7.1f, 0.9f), L(-7.1f, -3.6f), style }, Camp.Station.Style);
            Leg("the wardrobe round behind the fire to the missions board", style, new[] { L(-7.1f, -3.6f), L(-4.4f, -4.3f), L(0.9f, -4.3f), L(3.0f, -4.6f), missions }, Camp.Station.Missions);
            Leg("the missions board to the board shop's counter", missions, new[] { L(4.6f, -3.0f), L(4.6f, -0.4f), counter }, Camp.Station.Upgrades);
            Leg("the counter to the tuning bench", counter, new[] { L(7.2f, 2.5f), bench }, Camp.Station.Upgrades);
            Leg("the tuning bench through the boutique's way into the café", bench, new[] { L(6f, 1.0f), L(-6f, 1.0f), L(-10f, 0.8f), L(-13.5f, 2.2f) });
            Leg("the café into the ring round the fire", L(-13.5f, 2.2f), new[] { L(-10f, 0.8f), L(-4.6f, 0.3f), L(-4.5f, -2.05f), L(-3.0f, -2.05f) });
            Leg("all the way round the fire", L(-3.0f, -2.05f), new[] { L(-0.1f, -2.05f), L(0.6f, -3.2f), L(-0.1f, -4.35f), L(-3.5f, -4.35f), L(-4.3f, -3.2f), L(-3.5f, -2.05f) });
            Leg("the fire out the side door to the racks", L(-3.5f, -2.05f), new[] { L(-4.5f, -2.05f), L(-4.6f, 0.3f), L(0f, 1.0f), L(8f, 3.9f), L(10.4f, 3.95f), L(11.6f, 3.95f), L(14.75f, 3.95f), L(14.75f, -2.1f), L(14.75f, -4.6f) });
            toDropIn = new[] { L(-7.1f, -3.6f), L(-7.1f, 0.9f), L(2.5f, 1.2f), door, L(2f, 9f), foot, dropIn };
        }
        else if (layout == 3)
        {
            // B1: up the feature stair by the door onto the gallery, along it to the corner over the boutique
            var up = new[] { L(3.9f, 4.0f), L(3.9f, 2.4f), L(3.9f, -4.0f), L(3.9f, -5.5f), L(-5.2f, -5.6f), L(-8.6f, -5.4f), L(-8.4f, -3.7f) };
            Leg("the door up the feature stair, along the gallery to its corner over the boutique", door, up);
            Leg("the gallery's corner back down the stair to the missions board", U(-8.4f, -3.7f), new[] { L(-8.6f, -5.4f), L(-5.2f, -5.6f), L(3.9f, -5.5f), L(3.9f, -4.0f), L(3.9f, 2.4f), L(5.2f, 1.6f), L(5.2f, -3.0f), L(5.1f, -5.3f), missions }, Camp.Station.Missions);
            Leg("the door to the wardrobe (the boutique's back, under the gallery)", door, new[] { L(2.5f, 1.2f), L(-7.1f, 0.9f), L(-7.1f, -3.6f), style }, Camp.Station.Style);
            Leg("the missions board to the board shop's counter", missions, new[] { L(5.2f, -3.0f), L(5.2f, -0.4f), counter }, Camp.Station.Upgrades);
            Leg("the counter to the tuning bench", counter, new[] { L(7.2f, 2.5f), bench }, Camp.Station.Upgrades);
            Leg("the tuning bench round the stair's foot into the café", bench, new[] { L(6f, 2.4f), L(2.5f, 2.4f), L(-6f, 1.0f), L(-10f, 0.8f), L(-13.5f, 2.2f) });
            Leg("the café into the ring round the fire", L(-13.5f, 2.2f), new[] { L(-10f, 0.8f), L(-4.6f, 0.3f), L(-4.5f, -2.05f), L(-3.0f, -2.05f) });
            Leg("all the way round the fire (under the gallery behind it)", L(-3.0f, -2.05f), new[] { L(-0.1f, -2.05f), L(0.6f, -3.2f), L(-0.1f, -4.35f), L(-3.5f, -4.35f), L(-4.3f, -3.2f), L(-3.5f, -2.05f) });
            Leg("the fire out the side door to the racks", L(-3.5f, -2.05f), new[] { L(-4.5f, -2.05f), L(-4.6f, 0.3f), L(0f, 1.0f), L(2.5f, 2.4f), L(8f, 3.9f), L(10.4f, 3.95f), L(11.6f, 3.95f), L(14.75f, 3.95f), L(14.75f, -2.1f), L(14.75f, -4.6f) });
            toDropIn = new[] { L(-7.1f, -3.6f), L(-7.1f, 0.9f), L(2.5f, 1.2f), door, L(2f, 9f), foot, dropIn };
        }
        else if (layout == 4)
        {
            // B2: up the stair along the front glass into the loft, round to the wardrobe up there
            var up = new[] { L(1.6f, 5.3f), L(0.4f, 5.35f), L(-5.0f, 5.35f), L(-6.4f, 5.0f), L(-6.6f, 3.0f), L(-7.2f, 1.5f), L(-7.2f, -3.5f), L(-8.6f, -4.0f) };
            var down = new[] { L(-7.2f, -3.5f), L(-7.2f, 1.5f), L(-6.6f, 3.0f), L(-6.4f, 5.0f), L(-5.0f, 5.35f), L(0.4f, 5.35f), L(1.6f, 4.6f) };
            var toStyle = new List<Vector3>(up) { style };
            Leg("the door up the stair along the glass to the wardrobe in the loft", door, toStyle.ToArray(), Camp.Station.Style);
            var toMissions = new List<Vector3>(down) { L(2.4f, 2.4f), L(4.6f, -3.0f), missions };
            Leg("the loft back down the stair to the missions board", style, toMissions.ToArray(), Camp.Station.Missions);
            Leg("the missions board to the board shop's counter", missions, new[] { L(4.6f, -3.0f), L(4.6f, -0.4f), counter }, Camp.Station.Upgrades);
            Leg("the counter to the tuning bench", counter, new[] { L(7.2f, 2.5f), bench }, Camp.Station.Upgrades);
            Leg("the tuning bench through the snug's way into the café", bench, new[] { L(6f, 1.0f), L(-6f, 1.0f), L(-10f, 0.8f), L(-13.5f, 2.2f) });
            Leg("the café to the snug under the loft", L(-13.5f, 2.2f), new[] { L(-10f, 0.8f), L(-8.6f, 0.6f), L(-8.6f, -1.6f) });
            Leg("the snug into the ring round the fire", L(-8.6f, -1.6f), new[] { L(-6.8f, -1.6f), L(-4.6f, 0.3f), L(-4.5f, -2.05f), L(-3.0f, -2.05f) });
            Leg("all the way round the fire", L(-3.0f, -2.05f), new[] { L(-0.1f, -2.05f), L(0.6f, -3.2f), L(-0.1f, -4.35f), L(-3.5f, -4.35f), L(-4.3f, -3.2f), L(-3.5f, -2.05f) });
            Leg("the fire out the side door to the racks", L(-3.5f, -2.05f), new[] { L(-4.5f, -2.05f), L(-4.6f, 0.3f), L(0f, 1.0f), L(8f, 3.9f), L(10.4f, 3.95f), L(11.6f, 3.95f), L(14.75f, 3.95f), L(14.75f, -2.1f), L(14.75f, -4.6f) });
            var tdi = new List<Vector3>(down) { door, L(2f, 9f), foot, dropIn };
            toDropIn = tdi.ToArray();
        }
        else if (layout == 6)
        {
            // B2R: in past the lounge to the back wall, up the stair along it into the loft by the gear wall, to the wardrobe
            var up = new[] { L(3.6f, 0.8f), L(3.0f, -4.4f), L(1.6f, -5.9f), L(0.4f, -5.9f), L(-5.0f, -5.9f), L(-6.4f, -5.6f), L(-7.2f, -4.4f), L(-8.6f, -4.0f) };
            var down = new[] { L(-7.2f, -4.4f), L(-6.4f, -5.6f), L(-5.0f, -5.9f), L(0.4f, -5.9f), L(1.6f, -5.9f), L(3.0f, -4.4f) };
            var toStyle = new List<Vector3>(up) { style };
            Leg("the door up the stair along the back wall to the wardrobe in the loft", door, toStyle.ToArray(), Camp.Station.Style);
            var toMissions = new List<Vector3>(down) { missions };
            Leg("the loft back down the stair to the missions board", style, toMissions.ToArray(), Camp.Station.Missions);
            Leg("the missions board to the board shop's counter", missions, new[] { L(4.6f, -3.0f), L(4.6f, -0.4f), counter }, Camp.Station.Upgrades);
            Leg("the counter to the tuning bench", counter, new[] { L(7.2f, 2.5f), bench }, Camp.Station.Upgrades);
            Leg("the tuning bench through the snug's way into the café", bench, new[] { L(6f, 1.0f), L(-6f, 1.0f), L(-10f, 0.8f), L(-13.5f, 2.2f) });
            Leg("the café to the snug under the loft", L(-13.5f, 2.2f), new[] { L(-10f, 0.8f), L(-8.6f, 0.6f), L(-8.6f, -1.6f) });
            Leg("the snug into the ring round the fire", L(-8.6f, -1.6f), new[] { L(-6.8f, -1.6f), L(-4.6f, 0.3f), L(-4.5f, -1.72f), L(-3.0f, -1.72f) });
            Leg("all the way round the fire (the stair behind the back sofa)", L(-3.0f, -1.72f), new[] { L(-0.1f, -1.72f), L(0.45f, -2.71f), L(-0.1f, -3.7f), L(-3.5f, -3.7f), L(-4.1f, -2.71f), L(-3.5f, -1.72f) });
            Leg("the fire out the side door to the racks", L(-3.5f, -1.72f), new[] { L(-4.5f, -1.72f), L(-4.6f, 0.3f), L(0f, 1.0f), L(8f, 3.9f), L(10.4f, 3.95f), L(11.6f, 3.95f), L(14.75f, 3.95f), L(14.75f, -2.1f), L(14.75f, -4.6f) });
            Leg("the stair's foot round the lounge to the couple's glass", L(1.6f, -5.9f), new[] { L(3.0f, -4.4f), L(3.6f, 0.8f), L(-1.0f, 5.0f) });
            var tdi = new List<Vector3>(down) { L(3.6f, 0.8f), door, L(2f, 9f), foot, dropIn };
            toDropIn = tdi.ToArray();
        }
        else if (layout == 5)
        {
            // B3: round the dog-leg, up it (a landing half way) onto the balcony along the glass
            var up = new[] { L(2.4f, 2.6f), L(-4.0f, 2.6f), L(-4.0f, 5.2f), L(-3.4f, 5.2f), L(-0.2f, 5.2f), L(0.7f, 5.0f), L(0.7f, 3.8f), L(-0.2f, 3.8f), L(-3.4f, 3.8f), L(-5.0f, 3.0f), L(-8.0f, 3.0f) };
            Leg("the door round the dog-leg, up it onto the balcony along the glass", door, up);
            Leg("the balcony back down the dog-leg to the missions board", U(-8.0f, 3.0f), new[] { L(-5.0f, 3.0f), L(-3.4f, 3.8f), L(-0.2f, 3.8f), L(0.7f, 3.8f), L(0.7f, 5.0f), L(-0.2f, 5.2f), L(-3.4f, 5.2f), L(-4.0f, 5.2f), L(-4.0f, 2.6f), L(2.4f, 2.4f), L(4.6f, -3.0f), missions }, Camp.Station.Missions);
            Leg("the door to the wardrobe (the boutique's back)", door, new[] { L(2.5f, 1.2f), L(-7.1f, 0.9f), L(-7.1f, -3.6f), style }, Camp.Station.Style);
            Leg("the missions board to the board shop's counter", missions, new[] { L(4.6f, -3.0f), L(4.6f, -0.4f), counter }, Camp.Station.Upgrades);
            Leg("the counter to the tuning bench", counter, new[] { L(7.2f, 2.5f), bench }, Camp.Station.Upgrades);
            Leg("the tuning bench under the balcony into the café", bench, new[] { L(6f, 1.0f), L(-6f, 1.0f), L(-10f, 0.8f), L(-13.5f, 2.2f) });
            Leg("the café into the ring round the fire", L(-13.5f, 2.2f), new[] { L(-10f, 0.8f), L(-4.6f, 0.3f), L(-4.5f, -2.05f), L(-3.0f, -2.05f) });
            Leg("all the way round the fire", L(-3.0f, -2.05f), new[] { L(-0.1f, -2.05f), L(0.6f, -3.2f), L(-0.1f, -4.35f), L(-3.5f, -4.35f), L(-4.3f, -3.2f), L(-3.5f, -2.05f) });
            Leg("the fire out the side door to the racks", L(-3.5f, -2.05f), new[] { L(-4.5f, -2.05f), L(-4.6f, 0.3f), L(0f, 1.0f), L(2.4f, 2.4f), L(8f, 3.9f), L(10.4f, 3.95f), L(11.6f, 3.95f), L(14.75f, 3.95f), L(14.75f, -2.1f), L(14.75f, -4.6f) });
            toDropIn = new[] { L(-7.1f, -3.6f), L(-7.1f, 0.9f), L(2.5f, 1.2f), door, L(2f, 9f), foot, dropIn };
        }
        else
        {
            var up = new[] { L(2.9f, 5.0f), L(1.1f, 5.3f), L(1.1f, 3.9f), L(1.1f, -1.3f), L(1.1f, -2.4f), L(-2.0f, -3.1f), L(-5.6f, -3.1f), L(-5.9f, -4.5f) };
            var down = new[] { L(-5.6f, -3.1f), L(-2.0f, -3.1f), L(1.1f, -2.4f), L(1.1f, -1.3f), L(1.1f, 3.9f), L(1.1f, 5.3f) };
            var toStyle = new List<Vector3>(up) { style };
            Leg("the door up the stair to the wardrobe on the mezzanine", door, toStyle.ToArray(), Camp.Station.Style);
            var toMissions = new List<Vector3>(down) { L(2.9f, 5.0f), L(3.8f, 1.0f), L(3.8f, -4.4f), missions };
            Leg("the wardrobe down the stair to the missions board", style, toMissions.ToArray(), Camp.Station.Missions);
            Leg("the missions board to the board shop's counter", missions, new[] { L(4.6f, -3.0f), L(4.6f, -0.4f), counter }, Camp.Station.Upgrades);
            Leg("the counter to the tuning bench", counter, new[] { L(7.2f, 2.5f), bench }, Camp.Station.Upgrades);
            Leg("the tuning bench under the mezzanine into the café", bench, new[] { L(6f, 1.0f), L(3.8f, 1.0f), L(3.8f, -2.8f), L(-0.5f, -2.8f), L(-9.5f, -2.8f), L(-10f, -1.0f), L(-13.5f, 2.2f) });
            Leg("the café to the lounge by the fire", L(-13.5f, 2.2f), new[] { L(-10f, 0.5f), L(-8.3f, 0.5f), L(-8.4f, 2.9f) });
            Leg("the lounge under the mezzanine and out the side door to the racks", L(-8.4f, 2.9f), new[] { L(-7.6f, 0.6f), L(-7.4f, -2.8f), L(3.8f, -2.8f), L(3.8f, 1.0f), L(8f, 3.9f), L(10.4f, 3.95f), L(11.6f, 3.95f), L(14.75f, 3.95f), L(14.75f, -2.1f), L(14.75f, -4.6f) });
            Leg("the foyer past the stair's foot to the lounge", door, new[] { L(2.9f, 5.0f), L(1.1f, 5.3f), L(-1.2f, 5.3f), L(-1.0f, 3.0f) });
            var tdi = new List<Vector3>(down) { L(2.9f, 5.0f), L(3.9f, 4.9f), door, L(2f, 9f), foot, dropIn };
            toDropIn = tdi.ToArray();
        }
        Leg("the covered section from the racks round the front to the wing's steps and down", L(14.75f, -4.6f), new[] { L(14.75f, 3.95f), L(14.75f, 8.15f), L(6f, 8.15f), L(-10.5f, 8.2f), L(-11.9f, 9.2f), L(-11.9f, 11.6f), L(-13.0f, 12.3f), L(-13.0f, 17.5f), L(-13.0f, 20f) });
        Leg("into the wing, past the two talking by it, to make hot chocolate", L(-11.6f, -3.0f), new[] { L(-12.6f, -3.3f), L(-13.5f, -3.6f) }, Camp.Station.Cocoa);
        Leg("the covered section: along the racks, out to its edge and back to the side door", L(14.75f, -4.6f), new[] { L(14.75f, -2.1f), L(17.2f, -2.1f), L(14.75f, -2.1f), L(14.75f, 3.95f), L(17.9f, 3.95f), L(11.6f, 4.2f) });
        Leg("the snow below the wing's steps back up and along to the door", L(-13.0f, 20f), new[] { L(-13.0f, 13.0f), L(-11.9f, 11.6f), L(-11.9f, 9.0f), L(-8f, 8.2f), L(3f, 8.2f), door });
        Leg("the door to the fire table and the chalkboard", door, new[] { L(4.5f, 8.2f), L(-5.5f, 8.3f), L(-5.5f, 9.0f), L(-4.4f, 10.4f) });
        float walk = Leg("the wardrobe out of the door, down the steps to DROP IN (walking)", style, toDropIn, Camp.Station.DropIn);
        float run = Leg("the wardrobe out of the door, down the steps to DROP IN (running)", style, toDropIn, Camp.Station.DropIn, 1f);
        Leg("the steps' foot to DROP IN (walking, as CampWalkTest)", foot, new[] { dropIn }, Camp.Station.DropIn);
        Leg("the steps' foot to the lookout", foot, new[] { camp.StationPointLookout() });
        Debug.Log($"LODGEWALK wardrobe to DROP IN: {walk:0.0} s walking, {run:0.0} s running");
        // the cliff-fall rescue: back on your feet at the foot of the lodge's steps, clear of everything, and in from there
        typeof(Camp).GetMethod("Rescue", NP).Invoke(camp, null);
        var at = (Vector3)fPos.GetValue(camp);
        bool clear = !camp.WalkBlocked(at);
        if (!clear) { fails++; problems.Add((at, "rescued into something")); }
        Debug.Log($"LODGEWALK rescued at {at} ({Vector3.Distance(new Vector3(at.x, 0f, at.z), new Vector3(foot.x, 0f, foot.z)):0.0} m from the steps' foot), {(clear ? "standing clear" : "INSIDE SOMETHING")}");
        Leg("from the rescue up the steps and in at the door", at, new[] { L(0f, 12f), L(2.5f, 8.2f), door }, Camp.Station.None, 0.85f, false);
    }

    /// <summary>
    /// The LODGE chip in the camp, as a player taps it standing in the hall: the screen dips to black, the next layout
    /// comes in behind it, the hero is where they stood, and it fades back; three taps go A, B, C and round to A.
    /// </summary>
    static void Switch(MetaUi meta)
    {
        PlayerPrefs.SetInt(Camp.LodgePref, 0);
        camp.Rebuild();
        Physics.SyncTransforms();
        int count = Camp.LodgeLayoutKeys.Length;
        for (int n = 1; n <= count; n++)
        {
            camp.TeleportTo(L(-2.0f, 2.0f), camp.ChaletYaw + 30f);
            camp.Step(dt, meta);
            var before = (Vector3)fPos.GetValue(camp);
            meta.LodgeAsked = true;
            float t = 0f, black = -1f; bool saw = false;
            for (; t < 3f; t += dt)
            {
                int was = Camp.LodgeLayout;
                camp.Step(dt, meta); body.Apply(dt); camp.CameraStep(dt);
                if (Camp.LodgeLayout != was) black = t;
                if (camp.LodgeSwitching) saw = true; else if (saw) break;
            }
            var after = (Vector3)fPos.GetValue(camp);
            float moved = Vector2.Distance(new Vector2(before.x, before.z), new Vector2(after.x, after.z));
            int want = n % count;
            bool ok = saw && !camp.LodgeSwitching && Camp.LodgeLayout == want && moved < 0.05f && Mathf.Abs(after.y - before.y) < 0.05f;
            if (!ok) { fails++; problems.Add((after, "the LODGE chip's switch")); }
            Debug.Log($"LODGEWALK the LODGE chip, tap {n}: {(ok ? "" : "FAILED: ")}now {Camp.LodgeLayoutName} (wanted {Camp.LodgeLayoutKeys[want]}), swapped {black:0.00} s in, back in view at {t:0.00} s, the hero moved {moved:0.000} m");
        }
    }

    /// <summary>
    /// The places this layout gives the camp's people: each one they stand at (browsing, queueing, staff, standing) on
    /// its floor and clear of everything solid; and the couple's place at the big glass reached from the door.
    /// </summary>
    static void People()
    {
        float floor = camp.ChaletPoint(0, 0, 0).y;
        int n = 0, bad = 0;
        foreach (var (what, at, yaw) in Camp.LodgePeopleSpots)
        {
            if (what == "sit") continue;   // (on a seat: inside its block by design)
            n++;
            var w = camp.ChaletPoint(at.x, 0f, at.z);
            float feet = camp.FloorHeight(new Vector3(w.x, floor + at.y, w.z), out _) - floor;
            bool blocked = camp.WalkBlocked(w, 0.25f, at.y);
            if (Mathf.Abs(feet - at.y) > 0.1f || blocked)
            {
                bad++;
                problems.Add((new Vector3(w.x, floor + feet, w.z), $"people's {what} spot"));
                Debug.LogError($"LODGEWALK people's {what} spot at local ({at.x}, {at.y}, {at.z}): feet {feet:0.00}{(blocked ? ", inside something" : "")}");
            }
        }
        Debug.Log($"LODGEWALK people's standing spots checked: {n}, {bad} not clear");
        // the lodge's own people (LodgeFolk) each at one of those places
        int off = 0;
        foreach (var (pose, at, yaw, gear) in Camp.LodgeFolkAt)
        {
            bool found = false;
            foreach (var (what, sat, syaw) in Camp.LodgePeopleSpots)
                if ((sat - at).magnitude < 0.1f && Mathf.Abs(Mathf.DeltaAngle(syaw, yaw)) < 1f && (what == "sit") == (pose == "table" || pose == "stool")) { found = true; break; }
            if (!found) { off++; Debug.LogError($"LODGEWALK the lodge's {pose} person at local ({at.x}, {at.y}, {at.z}) isn't at one of the people's places"); }
        }
        Debug.Log($"LODGEWALK the lodge's own people: {Camp.LodgeFolkAt.Length}, {off} not at a people's place; {(LodgeFolk.Current != null ? LodgeFolk.Current.All.Count : 0)} in the camp");
        fails += off;
        var window = ((Vector2[])typeof(Camp).GetField("LodgeWindow", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))[layout];
        // (round B3's dog-leg: it stands between the door and the glass there)
        var toWindow = layout == 5 ? new[] { L(4.5f, 4.6f), L(2.4f, 2.6f), L(-4.0f, 2.6f), L(window.x, window.y) } : new[] { L(4.5f, 4.6f), L(window.x, window.y) };
        Leg("the door to the couple's place at the big glass", L(4.5f, 5.5f), toWindow);
        if (camp.WalkBlocked(L(window.x, window.y), 0.45f)) { fails++; problems.Add((L(window.x, window.y), "the couple's place")); Debug.LogError("LODGEWALK the couple's place at the glass: no room for two"); }
    }

    /// <summary>The lodge's walking (blocks and floors) against what is drawn there.</summary>
    static void Audit()
    {
        // give the lodge's drawn meshes colliders on a layer of their own, for the test
        Transform lodge = null;
        foreach (var t in camp.GetComponentsInChildren<Transform>(true)) if (t.name == "Lodge") { lodge = t; break; }
        var added = new List<GameObject>();
        foreach (var r in lodge.GetComponentsInChildren<MeshRenderer>())
        {
            var g = new GameObject("audit_" + r.name);
            g.layer = AuditLayer;
            g.transform.SetParent(r.transform, false);
            g.AddComponent<MeshCollider>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            added.Add(g);
        }
        foreach (var t in camp.GetComponentsInChildren<Transform>(true))
            if (t.name == "LampPosts")
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
                {
                    var g = new GameObject("audit_" + r.name); g.layer = AuditLayer; g.transform.SetParent(r.transform, false);
                    g.AddComponent<MeshCollider>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh; added.Add(g);
                }
        Physics.SyncTransforms();
        int mask = 1 << AuditLayer;
        float floor = camp.ChaletPoint(0, 0, 0).y;

        // 1: each solid box's sides: something drawn within reach of most of them, at body height
        int boxes = 0, bad = 0;
        foreach (var b in camp.SolidBoxes())
        {
            var lc = lodge.InverseTransformPoint(new Vector3(b.c.x, floor, b.c.y));
            if (lc.x < -19f || lc.x > 16f || lc.z < -10f || lc.z > 17.8f) continue;   // (the lodge's: not the camp's benches and lamp posts)
            if (b.lo < floor - 500f) continue;   // (nor what the camp's people put down, their waxing stand: CampLife's)
            boxes++;
            // at a walker's body height beside it: on the floor there, or the snow (beside the steps' buried end)
            float g = Mathf.Min(camp.FloorHeight(new Vector3(b.c.x, floor, b.c.y), out _), Slope.DrawnHeight(b.c.x, b.c.y));
            if (b.lo > floor - 0.5f) g = floor;
            float y0 = Mathf.Max(b.lo, g + 0.15f), y1 = Mathf.Min(b.hi, g + 1.6f);
            if (y1 <= y0) continue;   // (nothing of it at body height: it stops no one)
            int n = 0, hit = 0;
            var rot = Quaternion.Euler(0f, b.yaw, 0f);
            for (int s = 0; s < 4; s++)
                for (int i = 0; i < 6; i++)
                {
                    float u = (i + 0.5f) / 6f * 2f - 1f;
                    var l = s == 0 ? new Vector3(u * b.h.x, 0f, b.h.y) : s == 1 ? new Vector3(u * b.h.x, 0f, -b.h.y) : s == 2 ? new Vector3(b.h.x, 0f, u * b.h.y) : new Vector3(-b.h.x, 0f, u * b.h.y);
                    n++;
                    // something drawn within 0.25 m of the side, at the bottom, middle or top of what it blocks
                    for (int k = 0; k < 3; k++)
                    {
                        var w = new Vector3(b.c.x, Mathf.Lerp(y0, y1, k * 0.5f), b.c.y) + rot * l;
                        if (Physics.CheckSphere(w, 0.25f, mask, QueryTriggerInteraction.Ignore)) { hit++; break; }
                    }
                }
            float cover = hit / (float)n;
            if (cover < 0.6f)
            {
                bad++;
                problems.Add((new Vector3(b.c.x, y0, b.c.y), $"block with little drawn round it ({cover:P0})"));
                Debug.LogError($"LODGEWALK invisible? block at local {lc} half {b.h} heights {b.lo - floor:0.0}..{b.hi - floor:0.0}: drawn along {cover:P0} of its sides");
            }
        }
        Debug.Log($"LODGEWALK blocks checked: {boxes}, {bad} with little drawn along them");

        // 2 and 3: a grid over the lodge and its terrace and steps: where the walker can stand, its body is clear of
        // drawn things, and (on a floor) there is a drawn surface just under its feet
        int stand = 0, through = 0, floating = 0;
        var seen = new HashSet<Vector2Int>();
        foreach (float above in Levels[layout])
        for (float x = -17.4f; x <= 14.2f; x += 0.25f)
            for (float z = -8f; z <= 18.5f; z += 0.25f)
            {
                var w = lodge.TransformPoint(new Vector3(x, 0f, z));
                if (camp.WalkBlocked(w, 0.38f, above)) continue;
                float feet = camp.FloorHeight(new Vector3(w.x, floor + above, w.z), out bool snow);
                if (above > 0f && feet < floor + above - 0.8f) continue;   // (the upper pass: only where that floor is)
                if (snow && (z > 12f || x > 14f || x < -17.2f || z < -7f)) continue;   // the open snow round about: not the lodge's
                stand++;
                var p = new Vector3(w.x, feet, w.z);
                if (Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.55f, 0.16f, mask, QueryTriggerInteraction.Ignore))
                {
                    var cell = new Vector2Int(Mathf.RoundToInt(x * 2f), Mathf.RoundToInt(z * 2f));
                    through++;
                    if (seen.Add(cell)) { problems.Add((p, "drawn but walkable")); Debug.LogError($"LODGEWALK walk-through at local ({x:0.00}, {z:0.00}) feet {feet - floor:0.00}: {Hit(p, mask)}"); }
                }
                if (!snow && !Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 0.55f, mask, QueryTriggerInteraction.Ignore))
                {
                    floating++;
                    problems.Add((p, "floor with nothing drawn under it"));
                    Debug.LogError($"LODGEWALK floating floor at local ({x:0.00}, {z:0.00}) feet {feet - floor:0.00}");
                }
            }
        Debug.Log($"LODGEWALK standing places checked: {stand}; walk-through {through}, floating {floating}");
        foreach (var g in added) Object.DestroyImmediate(g);
    }

    static string Hit(Vector3 p, int mask)
    {
        var cs = Physics.OverlapCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.55f, 0.16f, mask, QueryTriggerInteraction.Ignore);
        var s = "";
        foreach (var c in cs) s += c.name + " ";
        return s;
    }

    /// <summary>Top down over the lodge: the render, with its blocks, floors, the walks and the problems drawn on.</summary>
    static void Map()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var dir = System.Environment.GetEnvironmentVariable("LODGE_SHOTS");
        if (string.IsNullOrEmpty(dir)) dir = ".";
        var cam = Camera.main;
        var centre = camp.ChaletPoint(-1.5f, 0f, 4.5f);
        int W = 1600, H = 1100;
        var fwd = Quaternion.Euler(0f, camp.ChaletYaw, 0f) * Vector3.forward;
        cam.orthographic = true; cam.orthographicSize = 15.5f; cam.aspect = W / (float)H;
        cam.transform.SetPositionAndRotation(centre + Vector3.up * 40f, Quaternion.LookRotation(Vector3.down, -fwd));
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 120f;
        // the roofs off, to see in
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt;
        float floor = camp.ChaletPoint(0, 0, 0).y;
        // a cut through the lodge at 2.2 m: everything above it hidden (the far plane can't do it at an angle; clip with the near plane)
        cam.transform.position = new Vector3(centre.x, floor + 2.2f, centre.z) + Vector3.up * 0.0f;
        cam.nearClipPlane = 0.01f; cam.farClipPlane = 40f;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        RenderTexture.active = null;
        void Dot(Vector3 w, Color c, int r)
        {
            var s = cam.WorldToScreenPoint(w);
            int x0 = Mathf.RoundToInt(s.x), y0 = Mathf.RoundToInt(s.y);
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) { int x = x0 + dx, y = y0 + dy; if (x >= 0 && y >= 0 && x < W && y < H) tex.SetPixel(x, y, c); }
        }
        void Line(Vector3 a, Vector3 b, Color c, int r)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.04f));
            for (int i = 0; i <= n; i++) Dot(Vector3.Lerp(a, b, i / (float)n), c, r);
        }
        void Rect4(Vector2 c, Vector2 h, float yaw, Color col, int r)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var pts = new[] { new Vector3(-h.x, 0, -h.y), new Vector3(h.x, 0, -h.y), new Vector3(h.x, 0, h.y), new Vector3(-h.x, 0, h.y) };
            for (int i = 0; i < 4; i++) Line(new Vector3(c.x, floor, c.y) + rot * pts[i], new Vector3(c.x, floor, c.y) + rot * pts[(i + 1) % 4], col, r);
        }
        foreach (var b in camp.FloorBoxes()) Rect4(b.c, b.h, b.yaw, new Color(0.2f, 0.85f, 0.3f), 0);
        foreach (var b in camp.SolidBoxes()) Rect4(b.c, b.h, b.yaw, new Color(0.95f, 0.15f, 0.1f), 1);
        foreach (var p in trail) Dot(p, new Color(0.15f, 0.45f, 1f), 1);
        for (int i = problemsFrom; i < problems.Count; i++) Dot(problems[i].at, Color.magenta, 6);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(dir, $"LodgeWalk_map_{Key}.png"), tex.EncodeToPNG());
        cam.targetTexture = null; cam.orthographic = false;
        Debug.Log("LODGEWALK map written");
    }
}
