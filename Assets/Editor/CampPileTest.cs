using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The camp's snow piles and everything else drawn there, against the camp's walker (CampSnow.cs, Camp.Walk):
///   1. every pile of snow the walker can reach (camp.Piles: the lodge's drifts against its terrace, the mounds round the
///      camp, the banks along the paved and the shovelled paths) walked into from eight ways round with the stick, to
///      its highest point: the walker's feet are never under the snow there (sunk into it), and its body (knee height
///      up) never in the pile's drawn mesh. A gentle pile is walked up; a steep one stops the walker at its foot.
///   2. a grid over the whole camp (0.4 m), outside the lodge (LodgeWalkTest audits that): wherever the walker can stand,
///      nothing drawn is in its body (a bench, a rack, a table, the fire's ring, a sign, a lamp post, a pile...):
///      nothing to walk through. Listed by what it is.
/// Batch: -executeMethod CampPileTest.Run (-nographics is fine; exit 1 on any of them).
/// </summary>
public static class CampPileTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float dt = 1f / 60f;
    const int TestLayer = 27;
    static Camp camp; static RiderBody body; static FieldInfo fPos, fCamYaw;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        int bad = 0;
        try
        {
            TestBoot.Boot(game);
            camp = (Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
            body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
            typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
            ChaseCamera.TestDt = dt;
            fPos = typeof(Camp).GetField("pos", NP); fCamYaw = typeof(Camp).GetField("camYaw", NP);
            camp.Rebuild();
            Physics.SyncTransforms();
            bad += Piles();
            bad += WalkThrough();
        }
        finally
        {
            ChaseCamera.TestDt = 0f;
            TestPrefs.Restore();
        }
        Debug.Log($"CAMPPILES done: {bad} problem(s)");
        TestHarness.Exit(bad == 0 ? 0 : 1);
    }

    static Vector3 Pos => (Vector3)fPos.GetValue(camp);

    /// <summary>The camp's drawn meshes, colliders of their own for the test (on a layer of their own).</summary>
    static List<GameObject> Colliders(System.Func<MeshRenderer, bool> which)
    {
        var added = new List<GameObject>();
        foreach (var r in camp.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !which(r)) continue;
            var g = new GameObject("test_" + r.name) { layer = TestLayer };
            g.transform.SetParent(r.transform, false);
            g.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            added.Add(g);
        }
        Physics.SyncTransforms();
        return added;
    }

    static bool IsPile(MeshRenderer r) => r.name.StartsWith("SnowPile") || r.name == "PathBanks" || r.name == "DugBanks" || (r.transform.parent != null && r.transform.parent.name == "Mounds");

    // ---------------------------------------------------------------- 1: into each pile

    static int Piles()
    {
        var added = Colliders(IsPile);
        int mask = 1 << TestLayer, bad = 0, walks = 0, up = 0, stopped = 0;
        float worstSink = 0f;
        Debug.Log($"CAMPPILES {camp.Piles.Count} piles of snow the walker meets");
        foreach (var p in camp.Piles)
        {
            // its footprint's reach from its highest point (as far as the grid has snow above the ground)
            float reach = 1f;
            for (int j = 0; j < p.nz; j++)
                for (int i = 0; i < p.nx; i++)
                {
                    var w = p.World(i, j);
                    if (float.IsNaN(w.y)) continue;
                    if (w.y - Slope.DrawnHeight(w.x, w.z) > 0.02f) reach = Mathf.Max(reach, new Vector2(w.x - p.middle.x, w.z - p.middle.z).magnitude);
                }
            reach = Mathf.Min(reach, 6f);   // (a long bank: walked into across it, near its highest point)
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var from = p.middle + dir * (reach + 1.6f);
                from.y = Slope.DrawnHeight(from.x, from.z);
                // (a start the walker can stand at: not inside something else, not off the camp)
                if (camp.WalkBlocked(from, 0.4f, from.y + 0.3f - camp.ChaletPoint(0f, 0f, 0f).y) || !Camp.InCamp(from.x, from.z) || camp.InsideAt(from + Vector3.up)) continue;
                walks++;
                camp.TeleportTo(from, Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg);
                for (int i = 0; i < 10; i++) { camp.Walk(Vector2.zero, dt); camp.CameraStep(dt); }
                float sink = 0f; bool inside = false, reached = false; Vector3 at = Pos;
                for (float t = 0f; t < 6f; t += dt)
                {
                    var q = Pos;
                    var to = p.middle;
                    if (new Vector2(to.x - q.x, to.z - q.z).magnitude < 0.35f) { reached = true; break; }
                    float head = Mathf.Atan2(to.x - q.x, to.z - q.z) - (float)fCamYaw.GetValue(camp);
                    camp.Walk(new Vector2(Mathf.Sin(head), Mathf.Cos(head)) * 0.85f, dt);
                    body.Apply(dt); camp.CameraStep(dt);
                    q = Pos;
                    // the feet on the snow, never under it
                    float snow = Mathf.Max(Slope.DrawnHeight(q.x, q.z), camp.PileTop(q.x, q.z));
                    sink = Mathf.Max(sink, snow - q.y);
                    // the body, knee height up, never in the pile's drawn snow
                    if (Physics.CheckCapsule(q + Vector3.up * 0.45f, q + Vector3.up * 1.5f, 0.2f, mask, QueryTriggerInteraction.Ignore)) { inside = true; at = q; }
                }
                worstSink = Mathf.Max(worstSink, sink);
                if (reached) up++; else stopped++;
                if (sink > 0.04f || inside)
                {
                    bad++;
                    Debug.LogError($"CAMPPILES into {p.name} (stands {p.tallest:0.00} m) from the {k * 45}° side: {(sink > 0.04f ? $"sank {sink:0.00} m into it" : "")}{(inside ? $" its body in the pile's snow at {at}" : "")}");
                }
            }
            Debug.Log($"CAMPPILES {p.name} at {p.middle}: stands {p.tallest:0.00} m over the snow round it, reaches {reach:0.0} m");
        }
        foreach (var g in added) Object.DestroyImmediate(g);
        Debug.Log($"CAMPPILES walked into the piles {walks} times: up and onto the top {up}, stopped at the foot {stopped}; deepest under the snow {worstSink * 100f:0.0} cm; {bad} problem(s)");
        return bad;
    }

    // ---------------------------------------------------------------- 2: nothing drawn to walk through

    static int WalkThrough()
    {
        // what counts: the camp's solid things, not its flat ones (paths, footprints, signs' faces), its lights' glow,
        // its people (skinned), the lodge (LodgeWalkTest's) or the trees' needles (the walker keeps off their trunks)
        bool Solid(MeshRenderer r)
        {
            for (var t = r.transform; t != null; t = t.parent)
                if (t.name == "Lodge" || t.name.Contains("Fir") || t.name.Contains("Tree") || t.name.Contains("Mist")) return false;
            if (r.GetComponentInParent<Person>() != null) return false;   // (the camp's people and their gear: they push the walker away themselves)
            var b = r.bounds;
            if (b.size.y < 0.12f) return false;   // (lying flat on the snow)
            var n = r.name;
            return !(n == "Path" || n == "Footpath" || n.StartsWith("Print") || n.Contains("Glow") || n.Contains("Flame") || n.Contains("Wire") || n.Contains("Bulb"));
        }
        var added = Colliders(Solid);
        int mask = 1 << TestLayer;
        float cx = Slope.Centre(Slope.PadLip);
        int stand = 0, through = 0;
        var what = new Dictionary<string, (int n, Vector3 at)>();
        for (float x = cx - 34f; x <= cx + 23.5f; x += 0.4f)
            for (float z = -112f; z <= -40f; z += 0.4f)
            {
                var w = new Vector3(x, 0f, z);
                if (camp.InLodgeOutline(w, 0.5f)) continue;   // (the lodge and its terrace: LodgeWalkTest)
                w.y = Slope.DrawnHeight(x, z);
                // (from the snow there: what it stands on and what stops it, both from that height)
                float feet = camp.FloorHeight(new Vector3(x, w.y + 0.3f, z), out _);
                if (camp.WalkBlocked(w, 0.38f, w.y + 0.3f - camp.ChaletPoint(0f, 0f, 0f).y)) continue;
                if (camp.OffTreesCheck(w)) continue;   // (by a tree's trunk: the walker is kept off it)
                // (where a pile's side is too steep to walk up, nobody stands)
                var p = new Vector3(x, feet, z);
                if (camp.PileRise(x, z).magnitude > Camp.MaxPileSlope && camp.PileTop(x, z) > Slope.DrawnHeight(x, z) + 0.1f) continue;
                stand++;
                var hits = Physics.OverlapCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.6f, 0.2f, mask, QueryTriggerInteraction.Ignore);
                if (hits.Length == 0) continue;
                through++;
                foreach (var h in hits)
                {
                    string k = h.name.Replace("test_", "") + " (" + (h.transform.parent != null && h.transform.parent.parent != null ? h.transform.parent.parent.name : "") + ")";
                    what[k] = what.TryGetValue(k, out var v) ? (v.n + 1, v.at) : (1, p);
                }
            }
        foreach (var kv in what.OrderByDescending(kv => kv.Value.n))
            Debug.LogError($"CAMPPILES walk-through: {kv.Key} at {kv.Value.n} places where the walker can stand, e.g. {kv.Value.at} ({kv.Value.at.x - cx:0.0}, {kv.Value.at.z:0.0} from the run's centre)");
        foreach (var g in added) Object.DestroyImmediate(g);
        Debug.Log($"CAMPPILES standing places checked across the camp: {stand}; with something drawn through the walker's body: {through} ({what.Count} things)");
        return what.Count;
    }
}
