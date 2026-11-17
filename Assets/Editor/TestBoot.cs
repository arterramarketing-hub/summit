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

    public static void Boot(SummitGame game, bool toStart = true)
    {
        // a fresh player: no camp upgrades (the editor keeps PlayerPrefs between runs, so HubShot's purchases
        // would otherwise give every later test better grip, a fourth fall and a longer avalanche head start)
        foreach (Progress.Up u in System.Enum.GetValues(typeof(Progress.Up))) PlayerPrefs.DeleteKey("summit_up_" + u);
        PlayerPrefs.DeleteKey("summit_points"); PlayerPrefs.DeleteKey("summit_jacket"); PlayerPrefs.DeleteKey("summit_boardcol");
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
        if (!toStart) return;
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { true });
        var camp = Object.FindFirstObjectByType<Camp>();
        if (camp != null) camp.SetNight(0f);
    }
}
