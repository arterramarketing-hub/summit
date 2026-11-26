using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The forest's drawing lists, followed down a set path: every list's instances (as a count and an order-free hash of
/// their matrices), the damaged trees, the stumps and the cells with hit shapes, logged every few metres. The path
/// weaves, fells a tree and a rock, snaps a tree, tears one's branches and clips another's top, jumps 400 m sideways
/// and back, turns back up the hill, and takes the camp's longer view behind. Run in both looks.
///
/// The FORESTLIST lines are the same for any way of building the lists that draws the same trees (run it on main
/// and on a branch and compare); at the end the lists are also built again from nothing and must match. The time
/// each re-listing took is logged apart (FORESTLIST-TIME), as it differs run to run.
///
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod ForestListTest.Run -logFile forestlist.log
/// </summary>
public static class ForestListTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static Forest props;
    static int fails;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        // the shadow band as the desktop asset has it, so it compares with a build that had it fixed at 150 m
        var urp = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
        float keptShadow = urp != null ? urp.shadowDistance : -1f;
        if (urp != null) urp.shadowDistance = 140f;
        var keptTarget = props.target;
        int side = props.sideCells, ahead = props.aheadCells, behind = props.behindCells;
        try
        {
            props.sideCells = 5; props.aheadCells = 8; props.behindCells = 3;   // (the same band whichever build)
            var t = new GameObject("ForestListTest target").transform;
            props.target = t;
            foreach (bool real in new[] { true, false })
            {
                TestBoot.NewMountain(game, 1000, terrain, props);
                if (Forest.Realistic != real) props.SetRealistic(real);
                Debug.Log($"FORESTLIST ==== look {(real ? "realistic" : "classic")} (built {(props.IsRealistic ? "realistic" : "classic")})");
                Ride(t);
            }
            // the time it takes, with this build's own band of hit shapes, the realistic look and nothing happening:
            // each cell crossing's re-listing, and the frames in between (where a build may place the rows ahead)
            props.sideCells = side; props.aheadCells = ahead;
            TestBoot.NewMountain(game, 1000, terrain, props);
            if (!Forest.Realistic) props.SetRealistic(true);
            TimeIt(t);
            Object.DestroyImmediate(t.gameObject);
        }
        catch (System.Exception e) { fails++; Debug.LogError("FORESTLIST exception " + e); }
        props.target = keptTarget; props.sideCells = side; props.aheadCells = ahead; props.behindCells = behind;
        if (urp != null) urp.shadowDistance = keptShadow;
        Debug.Log(fails == 0 ? "FORESTLIST PASS" : $"FORESTLIST FAIL ({fails})");
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Ride(Transform t)
    {
        var refresh = typeof(Forest).GetMethod("Refresh", NP);
        var pieces = new List<Forest.Piece>();
        // (two cycles down: below the tree line, where the forest grows, so there are trees to fell, snap and clip;
        // above it there are only rocks, Forest.Zone)
        float z = Course.First + 2f * Course.Cycle + 110f, xOff = 0f;
        long ticks = 0, worst = 0; int relists = 0;
        var sw = new System.Diagnostics.Stopwatch();
        Vector2Int lastC = new Vector2Int(int.MinValue, 0);
        bool rock = false;
        for (int step = 0; step <= 600; step++)
        {
            // down the hill 2.2 m a step, weaving 25 m either side; a while 400 m off to the side; then back up a way
            if (step >= 400 && step < 420) z -= 6.5f; else z += 2.2f;
            xOff = 25f * Mathf.Sin(step * 0.05f) + (step >= 300 && step < 310 ? 400f : 0f);
            if (step == 450) props.behindCells = 16;
            if (step == 500) props.behindCells = 3;
            t.position = new Vector3(Slope.Centre(z) + xOff, 0f, z);
            var c = new Vector2Int(Mathf.FloorToInt(t.position.x / Forest.Cell), Mathf.FloorToInt(t.position.z / Forest.Cell));
            sw.Restart();
            refresh.Invoke(props, new object[] { false });
            sw.Stop();
            if (c != lastC && step > 0) { relists++; ticks += sw.ElapsedTicks; worst = System.Math.Max(worst, sw.ElapsedTicks); }
            lastC = c;
            // things happening to the trees by the rider
            if (step == 100) Fell("TreeHit", c, pieces);
            if (step >= 150 && step < 200 && !rock && Nearest("RockHit", c, out _)) { Fell("RockHit", c, pieces); rock = true; }
            if (step == 200 && !rock) Log("fell RockHit: none near");
            if (step == 200 && Nearest("TreeHit", c, out var sc)) Log($"snap {sc}: {props.Snap(sc, 1f, pieces, out _)}");
            if (step == 250 && Nearest("TreeHit", c, out var tc) && props.TreeIn(tc.x, tc.y, out var tree))
            {
                props.TearBranches(tree, tree.at + tree.up * 2f, 1.2f);
                Log($"tear {tc}: damaged {props.Damaged}");
            }
            if (step == 260 && Nearest("TreeHit", c, out var cc)) Log($"clip {cc}: {props.ClipTop(cc, 3f, pieces, out _)}");
            if (step % 25 == 0 || (step >= 100 && step <= 110) || (step >= 150 && step <= 205) || (step >= 250 && step <= 265))
                Log($"step {step} cell {c}: {Digest()}");
        }
        // the same spot, every list built again from nothing: what the incremental lists must be
        string had = Digest();
        var dropAll = typeof(Forest).GetMethod("DropAllStrips", NP);
        if (dropAll != null) dropAll.Invoke(props, null);
        refresh.Invoke(props, new object[] { true });
        string fresh = Digest();
        Log($"rebuilt from nothing: {fresh}");
        if (had != fresh) { fails++; Log($"!! the lists differ from those built from nothing:\n   had   {had}\n   fresh {fresh}"); }
        double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Debug.Log($"FORESTLIST-TIME {relists} re-listings, mean {ticks * ms / Mathf.Max(1, relists):0.000} ms, worst {worst * ms:0.000} ms");
    }

    static void TimeIt(Transform t)
    {
        var refresh = typeof(Forest).GetMethod("Refresh", NP);
        var ahead = typeof(Forest).GetMethod("PlaceAhead", NP);
        var cross = new List<double>(); var quiet = new List<double>();
        double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var sw = new System.Diagnostics.Stopwatch();
        Vector2Int lastC = new Vector2Int(int.MinValue, 0);
        float z = Course.First + 2f * Course.Cycle + 110f;   // (through the forest, as Ride)
        for (int step = 0; step <= 900; step++, z += 2.2f)
        {
            t.position = new Vector3(Slope.Centre(z) + 25f * Mathf.Sin(step * 0.05f), 0f, z);
            var c = new Vector2Int(Mathf.FloorToInt(t.position.x / Forest.Cell), Mathf.FloorToInt(t.position.z / Forest.Cell));
            sw.Restart();
            refresh.Invoke(props, new object[] { false });
            if (c == lastC && ahead != null) ahead.Invoke(props, null);
            sw.Stop();
            if (step > 0) (c != lastC ? cross : quiet).Add(sw.ElapsedTicks * ms);
            lastC = c;
        }
        Debug.Log($"FORESTLIST-TIME own band ({props.sideCells} side, {props.aheadCells} ahead): {Stats(cross)} per cell crossed; {Stats(quiet)} on the frames between");
    }

    static string Stats(List<double> l)
    {
        if (l.Count == 0) return "none";
        double sum = 0; foreach (var v in l) sum += v;
        l.Sort();
        return $"{l.Count} x mean {sum / l.Count:0.000} ms, median {l[l.Count / 2]:0.000}, 95% {l[(int)(l.Count * 0.95)]:0.000}, worst {l[l.Count - 1]:0.000}";
    }

    static void Log(string s) => Debug.Log("FORESTLIST " + s);

    static Dictionary<Vector2Int, GameObject> Live() => (Dictionary<Vector2Int, GameObject>)typeof(Forest).GetField("live", NP).GetValue(props);

    /// <summary>The hit shape of this kind nearest the rider's cell (ties: the lowest x, then z).</summary>
    static bool Nearest(string name, Vector2Int c, out Vector2Int cell)
    {
        cell = default; int best = int.MaxValue;
        foreach (var kv in Live())
        {
            if (kv.Value.name != name) continue;
            int d = (kv.Key - c).sqrMagnitude;
            if (d < best || (d == best && (kv.Key.x < cell.x || (kv.Key.x == cell.x && kv.Key.y < cell.y)))) { best = d; cell = kv.Key; }
        }
        return best < int.MaxValue;
    }

    static void Fell(string name, Vector2Int c, List<Forest.Piece> pieces)
    {
        if (!Nearest(name, c, out var cell)) { Log($"fell {name}: none near"); return; }
        bool ok = props.Fell(Live()[cell], pieces, out var at, out float size);
        Log($"fell {name} {cell}: {ok} at {at.x:0.00},{at.z:0.00} size {size:0.000}");
    }

    // ---- an order-free hash of each list: what is drawn, not in which order

    static string Digest()
    {
        var near = (Dictionary<int, List<Matrix4x4>>)typeof(Forest).GetField("nearL", NP).GetValue(props);
        var far = (Dictionary<int, List<Matrix4x4>>)typeof(Forest).GetField("farL", NP).GetValue(props);
        var rich = (Dictionary<int, List<Matrix4x4>>)typeof(Forest).GetField("richL", NP).GetValue(props);
        var imp = (List<Matrix4x4>[])typeof(Forest).GetField("impL", NP).GetValue(props);
        var dist = (List<Matrix4x4>)typeof(Forest).GetField("distL", NP).GetValue(props);
        var dmg = (List<Vector2Int>)typeof(Forest).GetField("damagedL", NP).GetValue(props);
        var stumps = (List<Matrix4x4>)typeof(Forest).GetField("stumpL", NP).GetValue(props);
        int ni = 0; ulong ih = 0;
        if (imp != null) for (int v = 0; v < imp.Length; v++) { ni += imp[v].Count; ih += (ulong)(v + 1) * 0x9E3779B97F4A7C15UL ^ Sum(imp[v]); }
        int nl = 0; ulong lh = 0;
        foreach (var kv in Live()) { nl++; lh += Cell(kv.Key) * (kv.Value.name == "TreeHit" ? 3UL : 5UL); }
        ulong dh = 0; foreach (var k in dmg) dh += Cell(k);
        return $"near {Dict(near)} far {Dict(far)} rich {Dict(rich)} imp {ni}/{ih:x16} dist {dist.Count}/{Sum(dist):x16} " +
               $"damaged {dmg.Count}/{dh:x} stumps {stumps.Count}/{Sum(stumps):x16} hit {nl}/{lh:x}";
    }

    static string Dict(Dictionary<int, List<Matrix4x4>> d)
    {
        int n = 0; ulong h = 0;
        foreach (var kv in d) { if (kv.Value.Count == 0) continue; n += kv.Value.Count; h += ((ulong)kv.Key + 1) * 0x9E3779B97F4A7C15UL ^ Sum(kv.Value); }
        return $"{n}/{h:x16}";
    }

    static ulong Sum(List<Matrix4x4> l) { ulong s = 0; foreach (var m in l) s += Hash(m); return s; }

    static ulong Hash(Matrix4x4 m)
    {
        ulong h = 1469598103934665603UL;
        for (int i = 0; i < 16; i++) { h ^= (uint)System.BitConverter.SingleToInt32Bits(m[i]); h *= 1099511628211UL; }
        return h;
    }

    static ulong Cell(Vector2Int k) => (ulong)(uint)(k.x * 73856093) * 0x100000001B3UL ^ (ulong)(uint)(k.y * 19349663);
}
