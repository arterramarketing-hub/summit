using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The closet's swaps (RiderBody.SetCharacter) leave no ride gear behind. 30 swaps through the riders and their sets of
/// clothes, in turn on the board, on skis, on the toboggan, and on foot with skis or the sled waiting at the gate; then
/// one more in a crash on skis (the ragdoll has thrown them). After the first build and after every swap:
///   - exactly one Skis and one Toboggan under the rider (RiderBody.Drawn), the ones the new body rides (SkiGear,
///     SledGear), and in the whole scene no more Skis, Toboggan, SkiL/R or PoleL/R than after the first build;
///   - only what is ridden shows (nothing on foot);
///   - the poles are in the new body's hands;
///   - the rider's Landed and Jumped have as many listeners as after the first build.
/// Batch (no GPU needed):
///   Unity -batchmode -nographics -quit -projectPath . -executeMethod GearLeakTest.Run -logFile gearleak.log
/// Lines start GEARLEAK; the last is GEARLEAK PASS or GEARLEAK FAIL (exit 1).
/// </summary>
public static class GearLeakTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    const int Swaps = 30;
    static SummitGame game; static Rider rider; static RiderBody body;
    static int fails, landed0, jumped0;
    static Dictionary<string, int> scene0;
    static readonly string[] GearNames = { "Skis", "Toboggan", "SkiL", "SkiR", "Sled", "PoleL", "PoleR", "Rides" };

    static void Fail(string what) { fails++; if (fails < 60) Debug.Log("GEARLEAK FAIL " + what); }
    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static int Landed => rider.Landed?.GetInvocationList().Length ?? 0;
    static int Jumped => rider.Jumped?.GetInvocationList().Length ?? 0;

    // what the rider is doing when the swap comes: 0 board, 1 skis, 2 toboggan, 3 on foot (skis at the gate), 4 on foot (the sled)
    static readonly string[] ModeName = { "board", "skis", "toboggan", "on foot, skis", "on foot, sled" };
    static RideKind KindOf(int mode) => mode == 1 || mode == 3 ? RideKind.Skis : mode == 2 || mode == 4 ? RideKind.Toboggan : RideKind.Snowboard;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        var simWas = Physics.simulationMode;
        int swaps = 0;
        try
        {
            TestBoot.Boot(game);
            rider = F<Rider>("rider"); body = F<RiderBody>("body");
            TestBoot.NewMountain(game, 1000, F<SlopeStreamer>("terrain"), F<Forest>("props"));
            Physics.simulationMode = SimulationMode.Script;
            Begin();
            if (body.Hero == null) throw new System.Exception("no hero built: nothing to swap");
            landed0 = Landed; jumped0 = Jumped; scene0 = InScene();
            Debug.Log($"GEARLEAK first build (#{body.Built}): Landed {landed0}, Jumped {jumped0} listeners; under the rider: {Under()}; in the scene: {Names(scene0)}");
            Check("first build", 0);

            // every rider in every set of clothes, round and round, until 30 of them were a new body
            var looks = new List<int[]>();
            int riders = Wardrobe.Items(Wardrobe.Cat.Rider).Length;
            for (int set = 0; set < Wardrobe.OutfitCount; set++)
                for (int r = 0; r < riders; r++)
                {
                    var o = (int[])Wardrobe.Worn().Clone();
                    o[(int)Wardrobe.Cat.Rider] = r; o[(int)Wardrobe.Cat.Outfit] = set;
                    looks.Add(o);
                }
            for (int i = 0; swaps < Swaps && i < looks.Count * 3; i++)
            {
                int mode = swaps % ModeName.Length;
                Mode(mode);
                Frames(8);
                int built = body.Built;
                body.Wear(looks[i % looks.Count]);
                if (body.Built == built) continue;   // (the same look as now: no new body)
                swaps++;
                Frames(8);
                Check($"swap {swaps} ({ModeName[mode]}, rider {looks[i % looks.Count][(int)Wardrobe.Cat.Rider]} outfit {looks[i % looks.Count][(int)Wardrobe.Cat.Outfit]})", mode);
                Frames(8);
            }
            if (swaps < Swaps) Fail($"only {swaps} swaps made a new body");
            Debug.Log($"GEARLEAK after {swaps} swaps (#{body.Built}): Landed {Landed}, Jumped {Jumped} listeners; under the rider: {Under()}; in the scene: {Names(InScene())}");

            CrashSwap(looks);
            Debug.Log(fails == 0 ? $"GEARLEAK PASS: {swaps} swaps and a swap in a crash on skis; one Skis and one Toboggan, Landed {Landed} and Jumped {Jumped} listeners throughout; 0 failures"
                                 : $"GEARLEAK FAIL: {fails} failure(s) over {swaps} swaps");
        }
        catch (System.Exception e) { Debug.LogError("GEARLEAK exception " + e); fails++; Debug.Log("GEARLEAK FAIL: exception"); }
        Physics.simulationMode = simWas;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static void Begin()
    {
        float z = Course.First + 110f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
    }

    static void Mode(int mode)
    {
        body.SetWalking(false);
        rider.Tube = false; body.SetTube(false);
        body.SetRide(KindOf(mode)); rider.SetRide(body.Ride);
        if (body.Ride != KindOf(mode)) Fail($"{ModeName[mode]}: SetRide gave {body.Ride}");
        if (mode >= 3) body.SetWalking(true);
    }

    /// <summary>A few frames of the rider being drawn (posed on the gear there is now), and a jump and a landing heard.</summary>
    static void Frames(int n)
    {
        for (int i = 0; i < n; i++) body.Apply(Dt);
        rider.Jumped?.Invoke();
        rider.Landed?.Invoke(3f);
        body.Apply(Dt);
    }

    static void Check(string tag, int mode)
    {
        int skis = 0, sleds = 0, shown = 0;
        Transform skiAt = null, sledAt = null;
        foreach (Transform c in body.Drawn)
        {
            if (c.name == "Skis") { skis++; skiAt = c; }
            else if (c.name == "Toboggan") { sleds++; sledAt = c; }
            else continue;
            if (c.gameObject.activeInHierarchy) shown++;
        }
        if (skis != 1 || sleds != 1) Fail($"{tag}: {skis} Skis and {sleds} Toboggan under the rider (want 1 and 1): {Under()}");
        if (skiAt != body.SkiGear) Fail($"{tag}: the Skis under the rider are not the body's own (SkiGear)");
        if (sledAt != body.SledGear) Fail($"{tag}: the Toboggan under the rider is not the body's own (SledGear)");
        var now = InScene();
        foreach (var n in GearNames)
        {
            int a = now.TryGetValue(n, out int x) ? x : 0, b = scene0.TryGetValue(n, out int y) ? y : 0;
            if (a != b) Fail($"{tag}: {a} {n} in the scene (after the first build: {b})");
        }
        // only what is ridden shows
        bool foot = mode >= 3;
        if (body.SkiGear != null && body.SkiGear.gameObject.activeSelf != (!foot && KindOf(mode) == RideKind.Skis)) Fail($"{tag}: the skis {(body.SkiGear.gameObject.activeSelf ? "show" : "are hidden")}");
        if (body.SledGear != null && body.SledGear.gameObject.activeSelf != (!foot && KindOf(mode) == RideKind.Toboggan)) Fail($"{tag}: the toboggan {(body.SledGear.gameObject.activeSelf ? "shows" : "is hidden")}");
        if (shown > 1) Fail($"{tag}: {shown} sets of ride gear show");
        // the poles in the new body's hands
        var pole = (Transform[])typeof(RiderBody).GetField("pole", NP).GetValue(body);
        for (int s = 0; s < 2; s++)
            if (pole[s] == null || body.Hero == null || pole[s].parent != body.Hero.Hand[s]) Fail($"{tag}: pole {s} is not in the new body's hand");
        if (Landed != landed0 || Jumped != jumped0) Fail($"{tag}: Landed {Landed}, Jumped {Jumped} listeners (after the first build {landed0}, {jumped0})");
    }

    /// <summary>A crash on skis (they and the poles are thrown loose), and a swap before getting up; then a new run.</summary>
    static void CrashSwap(List<int[]> looks)
    {
        Begin();
        Mode(1);
        Frames(8);
        rider.DoCrash("GEARLEAK test");
        var ski = (Transform[])typeof(RiderBody).GetField("ski", NP).GetValue(body);
        for (int i = 0; i < 30; i++) { body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); }
        bool thrown = ski[0] != null && ski[0].parent != body.SkiGear;
        int built = body.Built;
        for (int i = 0; i < looks.Count && body.Built == built; i++) body.Wear(looks[(i + 7) % looks.Count]);
        if (body.Built == built) { Fail("crash: no swap made a new body"); return; }
        Frames(8);
        Check($"swap in a crash on skis ({(thrown ? "the skis were thrown" : "the skis were not thrown")})", 1);
        Begin();
        Mode(1);
        Frames(8);
        Check("a new run on skis after it", 1);
        Debug.Log($"GEARLEAK crash: skis {(thrown ? "thrown" : "not thrown")} by the ragdoll, swapped (#{body.Built}); under the rider: {Under()}; in the scene: {Names(InScene())}");
    }

    static string Under()
    {
        var names = new Dictionary<string, int>();
        foreach (Transform c in body.Drawn) { names.TryGetValue(c.name, out int n); names[c.name] = n + 1; }
        return Names(names);
    }

    static Dictionary<string, int> InScene()
    {
        var names = new Dictionary<string, int>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (System.Array.IndexOf(GearNames, t.name) >= 0) { names.TryGetValue(t.name, out int n); names[t.name] = n + 1; }
        return names;
    }

    static string Names(Dictionary<string, int> d)
    {
        var list = new List<string>();
        foreach (var kv in d) list.Add($"{kv.Key} x{kv.Value}");
        list.Sort();
        return string.Join(", ", list);
    }
}
