using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Every material the game makes at run time, against what a player build keeps (InstancedAssets).
///
/// Plays the game in the editor (play mode, as on the device): the camp at blue hour, a run at night in a storm with
/// a crash, the camp again, a run by day in snow, one at dusk in fog. Every half second it lists every material made at
/// run time: its shader, the shader_feature keywords switched on (those a build keeps only if a material asset needs
/// them), and whether it is instanced (GPU instancing on, and the shader declares it). Then, out of play mode, each is
/// looked up in a build's record of the variants that went in (SUMMIT_VARIANTS, Logs/shader-variants-full.txt, which
/// every player build writes): missing ones would be blank (instanced) or wrong (keywords) on the device.
/// SUMMIT_CENSUS_FIX=1 writes a keeper material (Resources/ShaderKeep) for each one missing.
///
/// On the way it also looks for anything drawn magenta (PurpleWatch.Scan every 2 s).
///
/// Batch, without -quit (it enters play mode): -executeMethod ShaderCensus.Run. Writes Logs/shader-census.txt. Exit 1
/// if anything is missing (before keepers are written) or was drawn magenta.
/// </summary>
[InitializeOnLoad]
public static class ShaderCensus
{
    const string Phase = "SummitCensusPhase", Store = "Logs/shader-census-seen.txt", PurpleStore = "Logs/shader-census-purple.txt";
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    static ShaderCensus()
    {
        string phase = SessionState.GetString(Phase, "");
        if (phase == "play") EditorApplication.update += Play;
    }

    public static void Run()
    {
        if (File.Exists(Store)) File.Delete(Store);
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        SessionState.SetString(Phase, "play");
        EditorApplication.EnterPlaymode();
    }

    // ---------------------------------------------------------------- in play mode

    class Seen { public string shader, keywords, example; public bool instanced; public int count; }
    static readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>();
    static readonly SortedSet<string> purple = new SortedSet<string>();
    static double t0 = -1, nextLook, nextPurple;
    static int step;
    static readonly HashSet<int> looked = new HashSet<int>();

    static void Play()
    {
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        if (t0 < 0) { t0 = now; nextLook = now; Debug.Log("CENSUS playing"); }
        double t = now - t0;
        if (now >= nextLook) { Look(); nextLook = now + 0.5; }
        if (now >= nextPurple) { foreach (var l in PurpleWatch.Scan(out _, out _)) purple.Add(l); nextPurple = now + 2.0; }
        var game = Object.FindFirstObjectByType<SummitGame>();
        if (game == null) return;
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        void Call(string name, params object[] args)
        {
            var m = typeof(SummitGame).GetMethods(NP).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
            try { m?.Invoke(game, args); } catch (System.Exception e) { Debug.LogWarning("CENSUS " + name + ": " + e.InnerException?.Message); }
        }
        // the camp at blue hour (as the game opens), then the runs
        (double at, System.Action what)[] plan =
        {
            (8, () => { Weather.TestChoice = 4; TimeOfDay.TestClock = 3.4f; Call("ToReady", true); }),
            (10, () => Call("ToRiding")),
            (24, () => rider?.Breakthrough(1f, 0f, 99f, 0f, "census")),
            (30, () => Call("ToHub", true)),
            (38, () => { Weather.TestChoice = 3; TimeOfDay.TestClock = 1.4f; Call("ToReady", true); }),
            (40, () => Call("ToRiding")),
            (56, () => { Weather.TestChoice = 5; TimeOfDay.TestClock = 2.6f; Call("ToReady", true); }),
            (58, () => Call("ToRiding")),
            (74, () => Call("ToHub", true)),
            (80, () =>
            {
                Look(); Save();
                // (leaving play mode reloads nothing: the report runs once back in edit mode)
                SessionState.EraseString(Phase);
                EditorApplication.update -= Play;
                EditorApplication.playModeStateChanged += Back;
                EditorApplication.ExitPlaymode();
            }),
        };
        while (step < plan.Length && t >= plan[step].at) { Debug.Log($"CENSUS step {step} at {t:0.0} s"); plan[step].what(); step++; }
    }

    static void Look()
    {
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (m == null || m.shader == null || EditorUtility.IsPersistent(m)) continue;
            var sh = m.shader;
            string path = AssetDatabase.GetAssetPath(sh);
            if (sh.name.StartsWith("Hidden/") || !(path.StartsWith("Assets/") || path.StartsWith("Packages/com.unity.render-pipelines"))) continue;
            var sf = InstancedAssets.ShaderFeatures(sh);
            string kw = string.Join(" ", m.shaderKeywords.Where(sf.Contains).OrderBy(k => k));
            bool inst = m.enableInstancing && InstancedAssets.DeclaresInstancing(sh);
            string key = sh.name + " | " + kw + " | " + inst;
            if (!seen.TryGetValue(key, out var s)) seen[key] = s = new Seen { shader = sh.name, keywords = kw, instanced = inst, example = m.name };
            if (looked.Add(m.GetInstanceID())) s.count++;
        }
    }

    static void Save()
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllLines(Store, seen.Values.Select(s => $"{s.shader}\t{s.keywords}\t{s.instanced}\t{s.count}\t{s.example}"));
        File.WriteAllLines(PurpleStore, purple);
        Debug.Log($"CENSUS {seen.Count} shader/keyword combinations seen, {purple.Count} things magenta");
    }

    // ---------------------------------------------------------------- the report

    static void Back(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= Back;
        EditorApplication.delayCall += Report;
    }

    /// <summary>The report again, from the last census's list (Logs/shader-census-seen.txt): -executeMethod ShaderCensus.ReportOnly.</summary>
    public static void ReportOnly() => Report();

    static void Report()
    {
        string varPath = System.Environment.GetEnvironmentVariable("SUMMIT_VARIANTS") ?? "Logs/shader-variants-full.txt";
        bool fix = System.Environment.GetEnvironmentVariable("SUMMIT_CENSUS_FIX") == "1";
        var sb = new StringBuilder();
        if (!File.Exists(Store)) { Debug.LogError("CENSUS nothing seen"); EditorApplication.Exit(2); return; }
        var record = File.Exists(varPath) ? InstancedAssets.ReadRecord(varPath) : null;
        sb.AppendLine($"CENSUS materials made at run time, by shader and the shader_feature keywords on; against {(record != null ? varPath : "no build record")}");
        int missing = 0;
        var toKeep = new List<(Shader sh, string[] kw, bool inst)>();
        foreach (var line in File.ReadAllLines(Store).OrderBy(l => l))
        {
            var f = line.Split('\t');
            var sh = Shader.Find(f[0]);
            string[] kw = f[1].Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            bool inst = f[2] == "True";
            string verdict = "kept";
            if (sh == null) verdict = "shader not found in the editor";
            else if (record != null && !InstancedAssets.Has(record, sh, kw, inst, out string why)) { verdict = "MISSING: " + why; missing++; toKeep.Add((sh, kw, inst)); }
            else if (record == null) verdict = "?";
            sb.AppendLine($"CENSUS {f[0]} [{(kw.Length > 0 ? string.Join(" ", kw) : "-")}]{(inst ? " instanced" : "")}: {f[3]} materials (e.g. {f[4]}): {verdict}");
        }
        sb.AppendLine($"CENSUS {missing} combinations missing from the build");
        // and anything that was drawn magenta on the way (PurpleWatch: an empty material slot, a missing or failed
        // shader, one this GPU cannot run, one with no pass URP draws)
        var magenta = File.Exists(PurpleStore) ? File.ReadAllLines(PurpleStore) : new string[0];
        foreach (var l in magenta) sb.AppendLine("CENSUS " + l);
        sb.AppendLine($"CENSUS {magenta.Length} things drawn magenta");
        if (fix)
        {
            // as few keepers as do it: one plain instanced material for a shader a build keeps no instanced variant of
            // (instanced variants then come with every keyword combination kept), and one material per keyword
            // combination missing
            var made = new HashSet<string>();
            foreach (var k in toKeep)
            {
                if (k.inst && !InstancedAssets.KeptInstanced(k.sh, out _) && made.Add(k.sh.name + "|inst"))
                    sb.AppendLine($"CENSUS keeper {AssetDatabase.GetAssetPath(InstancedAssets.MakeKeeper(k.sh, new string[0], true))}");
                if (k.kw.Length > 0 && made.Add(k.sh.name + "|" + string.Join(" ", k.kw)))
                    sb.AppendLine($"CENSUS keeper {AssetDatabase.GetAssetPath(InstancedAssets.MakeKeeper(k.sh, k.kw, false))}");
            }
        }
        Debug.Log(sb.ToString());
        File.WriteAllText("Logs/shader-census.txt", sb.ToString());
        EditorApplication.Exit((missing == 0 || fix) && magenta.Length == 0 ? 0 : 1);
    }

    /// <summary>
    /// Every shader in a build's record (SUMMIT_VARIANTS) with no instanced variant: whether it can be instanced at all
    /// (declares it), and whether the last census saw it on an instanced material. -executeMethod ShaderCensus.ZeroInstanced;
    /// writes Logs/shader-zero-instanced.txt.
    /// </summary>
    public static void ZeroInstanced()
    {
        string varPath = System.Environment.GetEnvironmentVariable("SUMMIT_VARIANTS") ?? "Logs/shader-variants-full.txt";
        var record = InstancedAssets.ReadRecord(varPath);
        var instancedSeen = new HashSet<string>();
        if (File.Exists(Store)) foreach (var line in File.ReadAllLines(Store)) { var f = line.Split('\t'); if (f.Length > 2 && f[2] == "True") instancedSeen.Add(f[0]); }
        var sb = new StringBuilder();
        int n = 0, declaring = 0;
        foreach (var kv in record.OrderBy(x => x.Key))
        {
            bool any = kv.Value.Values.Any(set => set.Any(v => (" " + v + " ").Contains(" INSTANCING_ON ")));
            if (any) continue;
            n++;
            var sh = Shader.Find(kv.Key);
            bool decl = sh != null && InstancedAssets.DeclaresInstancing(sh);
            if (decl) declaring++;
            sb.AppendLine($"ZERO {kv.Key}: {(decl ? "DECLARES INSTANCING" : "cannot be instanced")}{(instancedSeen.Contains(kv.Key) ? ", ON AN INSTANCED MATERIAL AT RUN TIME" : "")}");
        }
        sb.Insert(0, $"ZERO {varPath}: {record.Count} shaders, {n} with no instanced variant, of which {declaring} declare instancing\n");
        File.WriteAllText("Logs/shader-zero-instanced.txt", sb.ToString());
        Debug.Log(sb.ToString());
        EditorApplication.Exit(0);
    }
}
