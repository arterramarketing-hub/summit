using System.Reflection;
using UnityEngine;
using Summit;

/// <summary>
/// Starts the game the way the test tools need it: set up as on launch, then out of the evening camp and onto
/// the start pad in daylight, as "Drop In" does in the game. (The game now opens in the camp, at blue hour, and
/// the camp's own fade back to day runs in Update, which the batchmode tools never call.)
/// </summary>
public static class TestBoot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Switch to the test's own mountain the way the game does: the terrain, the forest and the start gate. (The
    /// gate's caution tape is tied to trees planted for the mountain it was built on; left from the mountain the game
    /// booted on, which Unity's random numbers pick, a tree could stand in the start line.)
    /// </summary>
    public static void NewMountain(SummitGame game, int seed, SlopeStreamer terrain, Forest props)
    {
        Slope.Init(seed);
        terrain.Rebuild(); props.Rebuild();
        typeof(SummitGame).GetMethod("RebuildGate", NP).Invoke(game, null);
    }

    public static void Boot(SummitGame game, bool toStart = true)
    {
        // every test rides a fresh player's settings, the ride included (TestPrefs; once a run, so a test's own
        // choices made after its first boot stand). One that read whatever another run had left saved rode a car
        // that takes no cable: LiftHillCheck, round 56
        TestPrefs.PinOnce();
        // a fresh player: no camp upgrades (the editor keeps PlayerPrefs between runs, so HubShot's purchases
        // would otherwise give every later test better grip, a fourth fall and a longer avalanche head start)
        foreach (Progress.Up u in System.Enum.GetValues(typeof(Progress.Up))) PlayerPrefs.DeleteKey("summit_up_" + u);
        PlayerPrefs.DeleteKey("summit_points"); PlayerPrefs.DeleteKey("summit_jacket"); PlayerPrefs.DeleteKey("summit_boardcol");
        // SUMMIT_TREES=classic|real picks the tree look for before/after shots (otherwise whatever is saved)
        var trees = System.Environment.GetEnvironmentVariable("SUMMIT_TREES");
        if (!string.IsNullOrEmpty(trees)) PlayerPrefs.SetInt(Forest.TreesPref, trees == "classic" ? 0 : 1);
        // midday, whatever the clock saved in the editor's PlayerPrefs says (DayNightShot picks its own times)
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
        if (!toStart) return;
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { true });
        var camp = Object.FindFirstObjectByType<Camp>();
        if (camp != null) camp.SetNight(0f);
    }
}
