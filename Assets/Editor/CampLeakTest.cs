using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The camp rebuilt over and over (as every run start does),
/// with both looks of the trees: the materials, meshes and textures in memory must stay flat, so nothing a build makes
/// is left behind by the next. Logs what grew, by name. Batch: -executeMethod CampLeakTest.Run (exit 1 if anything
/// grew).
/// </summary>
public static class CampLeakTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        int grew = 0;
        try
        {
            TestBoot.Boot(game);
            var camp = (Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
            typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
            var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
            foreach (bool realistic in new[] { true, false })
            {
                // the trees' look: the camp's firs follow it (TREES: REALISTIC or ORIGINAL)
                if (props.IsRealistic != realistic) typeof(SummitGame).GetMethod("ToggleTrees", NP).Invoke(game, new object[] { });
                for (int i = 0; i < 3; i++) camp.Rebuild();   // (warm: whatever is made once, and cached, is made now)
                var before = Count();
                const int N = 14;
                for (int i = 0; i < N; i++) camp.Rebuild();
                var after = Count();
                foreach (var kind in after.Keys)
                {
                    int b = before[kind].Values.Sum(), a = after[kind].Values.Sum();
                    var names = after[kind].Where(kv => kv.Value > (before[kind].TryGetValue(kv.Key, out int c0) ? c0 : 0))
                                           .Select(kv => $"{kv.Key} +{kv.Value - (before[kind].TryGetValue(kv.Key, out int c1) ? c1 : 0)}").ToList();
                    bool bad = a > b;
                    if (bad) grew++;
                    string line = $"CAMPLEAK trees {(realistic ? "realistic" : "original")}, {kind}: {b} before {N} rebuilds, {a} after{(names.Count > 0 ? " — grew: " + string.Join(", ", names.Take(20)) : "")}";
                    if (bad) Debug.LogError(line); else Debug.Log(line);
                }
            }
        }
        finally { TestPrefs.Restore(); }
        Debug.Log($"CAMPLEAK done: {grew} kind(s) grew");
        TestHarness.Exit(grew == 0 ? 0 : 1);
    }

    static Dictionary<string, Dictionary<string, int>> Count()
    {
        var all = new Dictionary<string, Dictionary<string, int>>();
        void Add<T>(string kind) where T : Object
        {
            var d = new Dictionary<string, int>();
            foreach (var o in Resources.FindObjectsOfTypeAll<T>())
            {
                if (EditorUtility.IsPersistent(o)) continue;   // (assets on disk: loaded once, not made)
                string n = string.IsNullOrEmpty(o.name) ? "(unnamed)" : o.name;
                d[n] = (d.TryGetValue(n, out int c) ? c : 0) + 1;
            }
            all[kind] = d;
        }
        Add<Material>("materials"); Add<Mesh>("meshes"); Add<Texture>("textures");
        return all;
    }
}
