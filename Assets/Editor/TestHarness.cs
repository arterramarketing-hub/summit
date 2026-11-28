using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// What every batch test and shot tool shares when it ends: the project left as it was found, and a real exit code.
///
/// The game's own start-up (SummitGame.Awake) and the tools change settings that the editor keeps in the project:
/// the physics step and Time's limits (ProjectSettings/TimeManager.asset), vSync and shadows (QualitySettings.asset),
/// Physics.simulationMode (Script while a tool steps the world itself), and the materials the game tunes as it
/// builds the world (Spray, FirCards and the rest of Assets/Summit/Materials and Resources). Left changed, Unity
/// writes them back when it quits and the checkout shows them as edits.
///
/// In batch mode this takes a note of all of it as the editor loads, before any tool runs, and puts it back when the
/// editor quits, however it quits: a tool's own Exit, an exception out of -executeMethod, a timeout's kill aside.
/// Materials are put back by clearing their unsaved changes, so nothing the run did to them is written to disk.
///
/// Tools end with TestHarness.Exit(code) (0 pass, 1 fail): it puts everything back, puts the player's saved settings
/// back (TestPrefs), waits for the Burst compiler to go idle (it crashed the editor at exit, H1 in codeaudit.md),
/// then exits. A tool that wants the restore at a point of its own can use `using (TestHarness.Begin()) { ... }`.
///
/// Burst: run test rounds with `--burst-disable-compilation` on the editor's command line (or the environment
/// variable UNITY_BURST_DISABLE_COMPILATION=1). Burst then compiles nothing in that editor, jobs run as plain C#, and
/// the crashes in its compiler at exit go away. Never pass it to CliBuild: it also turns Burst off for the player
/// build, so CliBuild refuses to build when it is set (see BurstOff).
/// </summary>
[InitializeOnLoad]
public static class TestHarness
{
    struct Settings
    {
        public SimulationMode sim; public float fixedDt, maxDt, maxParticleDt, timeScale; public int capture;
        public int quality, vSync; public float shadowDistance, lodBias;
    }

    static Settings start;
    static readonly string[] SettingsFiles = { "ProjectSettings/TimeManager.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/DynamicsManager.asset" };
    static bool noted, restoring;

    static TestHarness()
    {
        if (!Application.isBatchMode) return;
        start = Take(); noted = true;
        EditorApplication.quitting += Restore;
    }

    static Settings Take() => new Settings
    {
        sim = Physics.simulationMode,
        fixedDt = Time.fixedDeltaTime, maxDt = Time.maximumDeltaTime, maxParticleDt = Time.maximumParticleDeltaTime,
        timeScale = Time.timeScale, capture = Time.captureFramerate,
        quality = QualitySettings.GetQualityLevel(), vSync = QualitySettings.vSyncCount,
        shadowDistance = QualitySettings.shadowDistance, lodBias = QualitySettings.lodBias,
    };

    /// <summary>
    /// Everything back as the editor loaded it: Physics and Time, quality, and the materials' unsaved changes
    /// dropped. Safe to call more than once; quitting calls it again in case a tool changed things after.
    /// </summary>
    public static void Restore()
    {
        if (!noted || restoring) return;
        restoring = true;
        try
        {
            var s = start;
            Physics.simulationMode = s.sim;
            Time.timeScale = s.timeScale; Time.captureFramerate = s.capture;
            Time.fixedDeltaTime = s.fixedDt; Time.maximumDeltaTime = s.maxDt; Time.maximumParticleDeltaTime = s.maxParticleDt;
            if (QualitySettings.GetQualityLevel() != s.quality) QualitySettings.SetQualityLevel(s.quality, false);
            QualitySettings.vSyncCount = s.vSync;
            QualitySettings.shadowDistance = s.shadowDistance; QualitySettings.lodBias = s.lodBias;
            int dropped = DropMaterialChanges();
            // (put back is not enough for these: Unity writes a changed settings file in its newer layout, and the
            // fixed step as a ratio that comes back as 0.019999986, not 0.02; not written at all, they stay as they are)
            foreach (var f in SettingsFiles)
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(f)) if (o != null) EditorUtility.ClearDirty(o);
            Debug.Log($"TESTHARNESS restored Physics, Time and Quality settings; dropped unsaved changes to {dropped} material(s)");
        }
        catch (Exception e) { Debug.LogWarning("TESTHARNESS restore: " + e.Message); }
        finally { restoring = false; }
    }

    /// <summary>Materials saved in the project that the run changed: their changes are not written back.</summary>
    static int DropMaterialChanges()
    {
        int n = 0;
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (m == null || !EditorUtility.IsDirty(m)) continue;
            string path = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
            EditorUtility.ClearDirty(m);
            n++;
        }
        return n;
    }

    sealed class Scope : IDisposable { public void Dispose() => Restore(); }

    /// <summary>`using (TestHarness.Begin()) { ... }`: everything put back when the block ends, exception or not.</summary>
    public static IDisposable Begin() => new Scope();

    /// <summary>
    /// End a batch run: settings and the player's prefs put back, Burst given time to finish, then the editor quits
    /// with code (0 pass, 1 fail, anything else for the tool's own meaning).
    /// </summary>
    public static void Exit(int code)
    {
        Restore();
        TestPrefs.Restore();
        WaitForBurst(30f);
        Debug.Log($"TESTHARNESS exit {code}");
        EditorApplication.Exit(code);
    }

    /// <summary>
    /// The game as a test wants it: the scene opened, the player's saved settings pinned to a fresh player's
    /// (TestPrefs: the ride a snowboard, no upgrades, the default camera), the game booted (TestBoot), and, unless
    /// toStart is false, on the start pad in daylight. Its parts are SummitGame's internal fields (game.rider,
    /// game.body, game.terrain, game.props...).
    /// </summary>
    public static Summit.SummitGame Boot(bool toStart = true)
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = UnityEngine.Object.FindFirstObjectByType<Summit.SummitGame>();
        TestBoot.Boot(game, toStart);
        return game;
    }

    /// <summary>One fixed step of the world as the game takes it: the rider, then the body, then physics.</summary>
    public static void Step(Summit.Rider r, Summit.RiderBody body, float dt)
    {
        r.Tick(dt); body.PhysicsStep(); Physics.Simulate(dt);
    }

    /// <summary>Pass (0) when there were no failures, fail (1) otherwise.</summary>
    public static void Exit(bool pass) => Exit(pass ? 0 : 1);

    /// <summary>True when Burst compilation is turned off for this editor (the command-line flag or the variable).</summary>
    public static bool BurstOff
    {
        get
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (a == "--burst-disable-compilation") return true;
            var v = Environment.GetEnvironmentVariable("UNITY_BURST_DISABLE_COMPILATION");
            return !string.IsNullOrEmpty(v) && v != "0";
        }
    }

    /// <summary>
    /// Wait (up to seconds) for the Burst compiler's background work to finish, so the editor does not quit with
    /// it half way through a compile. Nothing to wait for when Burst is off. (BurstCompiler.IsCurrentCompilationDone
    /// is internal to the Burst package, so it is reached by reflection; if it is not there, nothing waits.)
    /// </summary>
    static void WaitForBurst(float seconds)
    {
        if (BurstOff) return;
        MethodInfo done = null;
        try { done = Type.GetType("Unity.Burst.BurstCompiler, Unity.Burst")?.GetMethod("IsCurrentCompilationDone", BindingFlags.NonPublic | BindingFlags.Static); }
        catch { }
        if (done == null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (!(bool)done.Invoke(null, null))
            {
                if (sw.Elapsed.TotalSeconds > seconds) { Debug.LogWarning($"TESTHARNESS Burst still compiling after {seconds:0} s; exiting anyway"); return; }
                System.Threading.Thread.Sleep(100);
            }
        }
        catch (Exception e) { Debug.LogWarning("TESTHARNESS Burst wait: " + e.Message); return; }
        if (sw.Elapsed.TotalSeconds > 0.2) Debug.Log($"TESTHARNESS waited {sw.Elapsed.TotalSeconds:0.0} s for Burst to finish");
    }
}
