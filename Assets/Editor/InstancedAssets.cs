using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// What a player build keeps of the shaders the game only reaches through materials it makes at run time.
///
/// A build compiles only the shader variants its material assets need. Two kinds of variants are therefore missing
/// on the device although the editor (which keeps every variant) draws them:
/// - instanced variants. GraphicsSettings strips unused instancing variants, so a shader drawn with
///   Graphics.RenderMeshInstanced only through materials made at run time loses them, and those draws come out as
///   nothing on the device. That is how the lift riders (liftpop), then the lifts' chairs and towers, the scanned
///   boulders and the swaying trees (liftchairs) went missing.
/// - `shader_feature` keywords that code switches on at run time (URP Lit's _EMISSION, _SURFACE_TYPE_TRANSPARENT,
///   _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A, _PARALLAXMAP...). The device picks the nearest variant it has: no glow,
///   or drawn opaque. (Keywords declared with `multi_compile`, like every keyword of this project's own shaders, are
///   always kept.)
///
/// The fix is a material asset that needs the variant: Assets/Summit/Resources/ShaderKeep/ (and Resources/PeopleLit.mat).
/// - Before every player build (and -executeMethod InstancedAssets.Ensure): every shader of the project that declares
///   instancing gets a keeper with GPU instancing on, unless a material in the build already keeps it. This covers
///   new shaders too.
/// - The rest come from ShaderCensus (the game played in the editor, every material made at run time listed, and
///   compared with what the last build kept), which writes keepers for the combinations missing.
/// - During every player build, the variants that really go in are recorded (after every other stripper). After it,
///   Logs/shader-variants.txt lists them per shader, and the build is checked: every keeper's variant, and an
///   instanced variant of every project shader that declares instancing, must be in it. If not, the problems are
///   logged as errors (SHADERKEEP) and CliBuild reports the build as failed.
///
/// The same check also fails a build that would draw things magenta (Unity's error shader) on the device (purplefix):
/// - a shader that failed to compile for the build's graphics API ("Shader error in ..." during the build, or errors
///   on the shader for Metal);
/// - a shader the build takes in (in a Resources folder, or used by a material it takes in) with no variant in it
///   (e.g. none of its SubShaders runs on Metal);
/// - a name the game's own scripts look a shader up by (Shader.Find("..."), Resources.Load&lt;Shader&gt;("...")) that is
///   not in the build: Shader.Find gives null in a player for a shader the build left out (the editor finds them all).
/// The game itself can look for magenta things while it runs: SUMMIT_PURPLE=1 (Summit.PurpleWatch).
/// </summary>
public class InstancedAssets : IPreprocessBuildWithReport, IPostprocessBuildWithReport, IPreprocessShaders
{
    public const string KeepDir = "Assets/Summit/Resources/ShaderKeep";

    public int callbackOrder => int.MaxValue;   // (as shaders go: after every other stripper, so what really goes in)

    public void OnPreprocessBuild(BuildReport report)
    {
        Record.Clear();
        lock (ShaderErrors) ShaderErrors.Clear();
        Application.logMessageReceivedThreaded -= Listen;
        Application.logMessageReceivedThreaded += Listen;
        target = report.summary.platform;
        Verified = false; Problems = "not checked";
        // (SUMMIT_KEEP_OFF=1: no keepers made, to see what a build without them lacks)
        if (System.Environment.GetEnvironmentVariable("SUMMIT_KEEP_OFF") != "1") Ensure();
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        Application.logMessageReceivedThreaded -= Listen;
        WriteReport("Logs/shader-variants.txt", "Logs/shader-variants-full.txt");
        Verified = Verify(out Problems);
        if (Verified) Debug.Log("SHADERKEEP every keeper's variant and every instanced variant is in the build, every shader the game looks up or takes in is in it, and none failed to compile");
        else Debug.LogError("SHADERKEEP the build lacks shaders or shader variants the game needs, or has shaders that failed:\n" + Problems);
    }

    /// <summary>Did the last build keep everything it should (Verify, after it)?</summary>
    public static bool Verified;
    public static string Problems = "not checked";

    // ---------------------------------------------------------------- keepers

    public static void Ensure()
    {
        PeopleAssets.Ensure();
        var kept = BuildMaterials();
        foreach (var path in ProjectShaders())
        {
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (sh == null || !DeclaresInstancing(sh)) continue;
            if (kept.Any(m => m.shader == sh && m.enableInstancing)) continue;
            MakeKeeper(sh, new string[0], true);
        }
    }

    /// <summary>A material asset in ShaderKeep that needs `sh` with exactly these keywords (and instanced, if asked).</summary>
    public static Material MakeKeeper(Shader sh, IList<string> keywords, bool instanced)
    {
        Directory.CreateDirectory(KeepDir);
        string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(sh));
        if (string.IsNullOrEmpty(name) || name == "unity_builtin_extra") name = sh.name.Replace('/', '_').Replace(' ', '_');
        if (keywords.Count > 0) name += "__" + string.Join("_", keywords.Select(k => k.Trim('_')));
        if (instanced && keywords.Count > 0) name += "__inst";
        string path = KeepDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool fresh = m == null;
        if (fresh) m = new Material(sh) { name = name };
        m.shader = sh;
        m.enableInstancing = instanced;
        foreach (var k in m.shaderKeywords) m.DisableKeyword(k);
        foreach (var k in keywords) m.EnableKeyword(k);
        // (what URP's own material editor would set alongside: so nothing it runs on import takes a keyword back)
        if (keywords.Contains("_EMISSION")) { if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
        if (keywords.Contains("_SURFACE_TYPE_TRANSPARENT") && m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        // (URP's "preserve specular" alpha blending is its premultiplied keyword: on only if that is wanted too)
        if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", keywords.Contains("_ALPHAPREMULTIPLY_ON") ? 1f : 0f);
        if (keywords.Contains("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A") && m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 1f);
        if (keywords.Contains("_ALPHATEST_ON") && m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
        foreach (var tex in new[] { ("_PARALLAXMAP", "_ParallaxMap"), ("_NORMALMAP", "_BumpMap"), ("_OCCLUSIONMAP", "_OcclusionMap"), ("_METALLICSPECGLOSSMAP", "_MetallicGlossMap") })
            if (keywords.Contains(tex.Item1) && m.HasProperty(tex.Item2)) m.SetTexture(tex.Item2, StandIn());
        foreach (var t in m.GetTexturePropertyNames()) { var tx = m.GetTexture(t); if (tx != null && !EditorUtility.IsPersistent(tx)) m.SetTexture(t, null); }
        if (fresh) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path);
        var back = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sf = ShaderFeatures(sh);
        var have = new HashSet<string>(back.shaderKeywords.Where(sf.Contains));
        var want = new HashSet<string>(keywords.Where(sf.Contains));
        if (!have.SetEquals(want)) Debug.LogWarning("SHADERKEEP " + path + " came out of its import with " + string.Join(" ", have) + " instead of " + string.Join(" ", want));
        Debug.Log("SHADERKEEP keeper " + path + " (" + sh.name + (keywords.Count > 0 ? ", " + string.Join(" ", keywords) : "") + (instanced ? ", instanced" : "") + ")");
        return back;
    }

    /// <summary>The smallest texture asset of the project: what a keeper points a map at, where URP wants one there to
    /// keep its keyword (the keeper is never drawn).</summary>
    static Texture2D StandIn()
    {
        if (standIn != null) return standIn;
        long least = long.MaxValue;
        foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Summit" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) continue;
            long size = (long)t.width * t.height;
            if (size < least) { least = size; standIn = t; }
        }
        return standIn;
    }
    static Texture2D standIn;

    /// <summary>Would a build keep `s`'s instanced variants? (GraphicsSettings' instancing variants on "Strip Unused":
    /// only if a material asset the build takes in has that shader and GPU instancing on.)</summary>
    public static bool KeptInstanced(Shader s, out string by)
    {
        by = null;
        if (s == null) return false;
        int strip = InstancingStripping();
        if (strip == 2) { by = "instancing variants kept for every shader (Keep All)"; return true; }
        if (strip == 1) return false;   // (Strip All)
        foreach (var m in BuildMaterials())
            if (m.shader == s && m.enableInstancing) { by = "e.g. " + AssetDatabase.GetAssetPath(m); return true; }
        return false;
    }

    static int InstancingStripping()
    {
        var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gs.Length == 0) return 0;
        var p = new SerializedObject(gs[0]).FindProperty("m_InstancingStripping");
        return p != null ? p.intValue : 0;
    }

    /// <summary>The material assets a build takes in: everything under a Resources folder and what it uses, and what the
    /// game's scene uses.</summary>
    public static List<Material> BuildMaterials()
    {
        var roots = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (path.Contains("/Resources/") && !AssetDatabase.IsValidFolder(path)) roots.Add(path);
        }
        foreach (var sc in EditorBuildSettings.scenes) if (sc.enabled) roots.Add(sc.path);
        if (!roots.Contains("Assets/Summit/Summit.unity")) roots.Add("Assets/Summit/Summit.unity");
        var list = new List<Material>();
        foreach (var d in AssetDatabase.GetDependencies(roots.ToArray(), true))
            if (d.EndsWith(".mat")) { var m = AssetDatabase.LoadAssetAtPath<Material>(d); if (m != null && m.shader != null) list.Add(m); }
        return list;
    }

    public static IEnumerable<string> ProjectShaders() =>
        AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".shader"));

    // ---------------------------------------------------------------- what a shader declares

    static readonly Dictionary<Shader, string> sources = new Dictionary<Shader, string>();

    /// <summary>The shader's text with the project's or the package's files it includes with pragmas.</summary>
    public static string Source(Shader s)
    {
        if (s == null) return "";
        if (sources.TryGetValue(s, out var have)) return have;
        string path = AssetDatabase.GetAssetPath(s), text = "";
        string full = Full(path);
        if (full != null && File.Exists(full))
        {
            text = File.ReadAllText(full);
            foreach (Match inc in Regex.Matches(text, "#include_with_pragmas\\s+\"([^\"]+)\""))
            {
                string ip = inc.Groups[1].Value;
                string f = ip.StartsWith("Packages/") || ip.StartsWith("Assets/") ? Full(ip) : Path.Combine(Path.GetDirectoryName(full), ip);
                if (f != null && File.Exists(f)) text += "\n" + File.ReadAllText(f);
            }
        }
        sources[s] = text;
        return text;
    }

    static string Full(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        if (assetPath.StartsWith("Assets/")) return Path.GetFullPath(assetPath);
        if (assetPath.StartsWith("Packages/"))
        {
            var pi = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (pi == null) return Path.GetFullPath(assetPath);
            return Path.Combine(pi.resolvedPath, assetPath.Substring(pi.assetPath.Length).TrimStart('/'));
        }
        return null;
    }

    public static bool DeclaresInstancing(Shader s) => Regex.IsMatch(Source(s), "#pragma\\s+(multi_compile_instancing|instancing_options)|INSTANCING_ON");

    /// <summary>Keywords the shader declares with shader_feature (kept only if a material in the build uses them).</summary>
    public static HashSet<string> ShaderFeatures(Shader s)
    {
        var set = new HashSet<string>();
        foreach (Match m in Regex.Matches(Source(s), "#pragma\\s+shader_feature\\w*\\s+([^\\r\\n]+)"))
            foreach (var k in m.Groups[1].Value.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries))
                if (!k.StartsWith("//") && k.Trim('_').Length > 0) set.Add(k);
        return set;
    }

    // ---------------------------------------------------------------- what the build keeps

    /// <summary>Per shader, per "pass | stage": the keyword sets of the variants that go into the build.</summary>
    public static readonly Dictionary<string, Dictionary<string, HashSet<string>>> Record = new Dictionary<string, Dictionary<string, HashSet<string>>>();

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (!Record.TryGetValue(shader.name, out var passes)) Record[shader.name] = passes = new Dictionary<string, HashSet<string>>();
        string k = snippet.passName + " | " + snippet.shaderType;
        if (!passes.TryGetValue(k, out var sets)) passes[k] = sets = new HashSet<string>();
        foreach (var d in data)
        {
            var names = d.shaderKeywordSet.GetShaderKeywords().Select(x => x.name).OrderBy(x => x);
            sets.Add(string.Join(" ", names));
        }
    }

    static void WriteReport(string summaryPath, string fullPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(summaryPath));
        var sb = new StringBuilder(); var full = new StringBuilder();
        foreach (var sh in Record.OrderBy(x => x.Key))
            foreach (var pass in sh.Value.OrderBy(x => x.Key))
            {
                int inst = pass.Value.Count(v => (" " + v + " ").Contains(" INSTANCING_ON "));
                sb.AppendLine($"{sh.Key} | {pass.Key} : {pass.Value.Count} variants, {inst} instanced");
                foreach (var v in pass.Value.OrderBy(x => x)) full.AppendLine($"{sh.Key} | {pass.Key} | {v}");
            }
        File.WriteAllText(summaryPath, sb.ToString());
        File.WriteAllText(fullPath, full.ToString());
    }

    /// <summary>A build record read back (Logs/shader-variants-full.txt), for the census.</summary>
    public static Dictionary<string, Dictionary<string, HashSet<string>>> ReadRecord(string fullPath)
    {
        var r = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        foreach (var line in File.ReadAllLines(fullPath))
        {
            var parts = line.Split(new[] { " | " }, System.StringSplitOptions.None);
            if (parts.Length < 4) continue;
            if (!r.TryGetValue(parts[0], out var passes)) r[parts[0]] = passes = new Dictionary<string, HashSet<string>>();
            string k = parts[1] + " | " + parts[2];
            if (!passes.TryGetValue(k, out var sets)) passes[k] = sets = new HashSet<string>();
            sets.Add(parts[3]);
        }
        return r;
    }

    /// <summary>
    /// Does a build record have a variant of `s` with exactly these shader_feature keywords (the others, multi_compile,
    /// are whatever they are) and, if asked, instanced? Looked for in the shader's main pass: the pass and stage whose
    /// variants use the most of its shader_feature keywords (URP Lit: ForwardLit's fragment).
    /// </summary>
    public static bool Has(Dictionary<string, Dictionary<string, HashSet<string>>> record, Shader s, ICollection<string> features, bool instanced, out string why)
    {
        why = null;
        if (!record.TryGetValue(s.name, out var passes)) { why = "not in the build at all"; return false; }
        var sf = ShaderFeatures(s);
        HashSet<string> main = null; int best = -1;
        foreach (var p in passes)
        {
            var used = new HashSet<string>();
            foreach (var v in p.Value) foreach (var k in v.Split(' ')) if (sf.Contains(k)) used.Add(k);
            int score = used.Count * 100000 + p.Value.Count;
            if (score > best) { best = score; main = p.Value; }
        }
        var want = new HashSet<string>(features.Where(sf.Contains));
        foreach (var v in main)
        {
            var ks = new HashSet<string>(v.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
            if (instanced && !ks.Contains("INSTANCING_ON")) continue;
            if (ks.Where(sf.Contains).Count() == want.Count && want.All(ks.Contains)) return true;
        }
        bool anyInst = main.Any(v => (" " + v + " ").Contains(" INSTANCING_ON "));
        why = instanced && !anyInst ? "no instanced variant at all" : want.Count > 0 ? "no variant with " + string.Join(" ", want) + (instanced ? ", instanced" : "") : "no instanced variant";
        return false;
    }

    /// <summary>After a build: is every keeper's variant in it, and an instanced variant of every project shader that
    /// declares instancing (and is in the build)? And nothing that would be magenta on the device (Magenta)?</summary>
    public static bool Verify(out string problems)
    {
        var sb = new StringBuilder();
        Magenta(Record, sb);
        var keepers = new List<Material>();
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { KeepDir })) { var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)); if (m != null) keepers.Add(m); }
        var people = AssetDatabase.LoadAssetAtPath<Material>("Assets/Summit/Resources/PeopleLit.mat"); if (people != null) keepers.Add(people);
        foreach (var m in keepers)
            if (!Has(Record, m.shader, m.shaderKeywords, m.enableInstancing, out string why))
                sb.AppendLine($"  {m.shader.name}: {why} (needed by {AssetDatabase.GetAssetPath(m)})");
        foreach (var path in ProjectShaders())
        {
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (sh == null || !DeclaresInstancing(sh) || !Record.ContainsKey(sh.name)) continue;
            if (!Record[sh.name].Values.Any(set => set.Any(v => (" " + v + " ").Contains(" INSTANCING_ON "))))
                sb.AppendLine($"  {sh.name}: declares instancing, no instanced variant in the build ({path})");
        }
        problems = sb.ToString();
        return problems.Length == 0;
    }

    // ---------------------------------------------------------------- what would be magenta on the device (purplefix)

    static BuildTarget target = BuildTarget.iOS;

    /// <summary>"Shader error in ..." and "... is not supported on this GPU" lines logged during the build (the compile for
    /// the build's graphics API).</summary>
    public static readonly List<string> ShaderErrors = new List<string>();

    static void Listen(string condition, string stack, LogType type)
    {
        if (condition == null) return;
        // (a compile error for the build's API, or a shader with no SubShader that runs on it: both magenta there)
        if (condition.StartsWith("Shader error in") || condition.Contains("shader is not supported on this GPU"))
            lock (ShaderErrors) ShaderErrors.Add(condition.Split('\n')[0]);
    }

    static ShaderCompilerPlatform[] Apis(BuildTarget t)
    {
        var list = new List<ShaderCompilerPlatform>();
        foreach (var g in PlayerSettings.GetGraphicsAPIs(t))
            switch (g)
            {
                case GraphicsDeviceType.Metal: list.Add(ShaderCompilerPlatform.Metal); break;
                case GraphicsDeviceType.Vulkan: list.Add(ShaderCompilerPlatform.Vulkan); break;
                case GraphicsDeviceType.OpenGLES3: list.Add(ShaderCompilerPlatform.GLES3x); break;
                case GraphicsDeviceType.Direct3D11: case GraphicsDeviceType.Direct3D12: list.Add(ShaderCompilerPlatform.D3D); break;
            }
        if (list.Count == 0) list.Add(ShaderCompilerPlatform.Metal);
        return list.ToArray();
    }

    /// <summary>The shaders a build takes in: those under a Resources folder, and those its materials and scenes use.</summary>
    public static List<Shader> BuildShaders()
    {
        var roots = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (path.Contains("/Resources/") && !AssetDatabase.IsValidFolder(path)) roots.Add(path);
        }
        foreach (var sc in EditorBuildSettings.scenes) if (sc.enabled) roots.Add(sc.path);
        if (!roots.Contains("Assets/Summit/Summit.unity")) roots.Add("Assets/Summit/Summit.unity");
        var list = new List<Shader>();
        foreach (var d in AssetDatabase.GetDependencies(roots.ToArray(), true))
            if (d.EndsWith(".shader")) { var sh = AssetDatabase.LoadAssetAtPath<Shader>(d); if (sh != null && !list.Contains(sh)) list.Add(sh); }
        foreach (var m in BuildMaterials()) if (!list.Contains(m.shader)) list.Add(m.shader);
        return list;
    }

    /// <summary>
    /// The names the game's own scripts (not Editor ones) look shaders up by: Shader.Find("name") gives the shader's
    /// name, Resources.Load&lt;Shader&gt;("path") its path in a Resources folder. Lookups by a name worked out in code
    /// cannot be checked (`loose`).
    /// </summary>
    public static List<(string file, int line, string name, bool byPath)> LookedUp(out List<string> loose)
    {
        var list = new List<(string, int, string, bool)>();
        loose = new List<string>();
        foreach (var f in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
        {
            string ff = f.Replace('\\', '/');
            if (ff.Contains("/Editor/")) continue;
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                int c = l.IndexOf("//"); if (c >= 0) l = l.Substring(0, c);
                foreach (Match m in Regex.Matches(l, "Shader\\.Find\\(\\s*(\"([^\"]*)\"|[^)]*)\\)"))
                    if (m.Groups[2].Success) list.Add((ff, i + 1, m.Groups[2].Value, false)); else loose.Add($"{ff}:{i + 1}: Shader.Find({m.Groups[1].Value})");
                foreach (Match m in Regex.Matches(l, "Resources\\.Load<Shader>\\(\\s*(\"([^\"]*)\"|[^)]*)\\)"))
                    if (m.Groups[2].Success) list.Add((ff, i + 1, m.Groups[2].Value, true)); else loose.Add($"{ff}:{i + 1}: Resources.Load<Shader>({m.Groups[1].Value})");
            }
        }
        return list;
    }

    /// <summary>A shader under a Resources folder at this Resources path (as Resources.Load takes it), or null.</summary>
    static Shader InResources(string resPath)
    {
        foreach (var g in AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            int r = p.LastIndexOf("/Resources/");
            if (r < 0) continue;
            string rel = p.Substring(r + "/Resources/".Length);
            if (Path.ChangeExtension(rel, null) == resPath) return AssetDatabase.LoadAssetAtPath<Shader>(p);
        }
        return null;
    }

    /// <summary>
    /// Everything a build record shows would be drawn magenta on the device: shaders that failed to compile for the
    /// build's graphics APIs, shaders the build takes in with no variant in it, and run-time lookups of shaders the build
    /// left out. Appends a line per problem to `sb`.
    /// </summary>
    public static void Magenta(Dictionary<string, Dictionary<string, HashSet<string>>> record, StringBuilder sb)
    {
        lock (ShaderErrors) foreach (var e in ShaderErrors.Distinct()) sb.AppendLine("  failed: " + e);
        var apis = Apis(target);
        var built = BuildShaders();
        foreach (var name in record.Keys.OrderBy(x => x))
        {
            var sh = Shader.Find(name);
            if (sh == null) continue;
            foreach (var api in apis)
                foreach (var msg in ShaderUtil.GetShaderMessages(sh, api))
                    if (msg.severity == ShaderCompilerMessageSeverity.Error)
                        sb.AppendLine($"  {name}: failed to compile for {api}: {msg.message} ({msg.file}:{msg.line})");
        }
        foreach (var sh in built)
        {
            if (sh == null || record.ContainsKey(sh.name)) continue;
            string path = AssetDatabase.GetAssetPath(sh);
            if (!path.StartsWith("Assets/")) continue;   // (URP's and Unity's own: theirs to strip)
            sb.AppendLine($"  {sh.name}: in the build ({path}) with no variant compiled for it: drawn magenta");
        }
        var lookups = LookedUp(out var loose);
        foreach (var (file, line, name, byPath) in lookups)
        {
            if (byPath)
            {
                var sh = InResources(name);
                if (sh == null) sb.AppendLine($"  Resources.Load<Shader>(\"{name}\") ({file}:{line}): no shader at that path in a Resources folder");
                else if (!record.ContainsKey(sh.name)) sb.AppendLine($"  Resources.Load<Shader>(\"{name}\") ({file}:{line}): {sh.name} has no variant in the build");
            }
            else if (!record.ContainsKey(name))
                sb.AppendLine($"  Shader.Find(\"{name}\") ({file}:{line}): not in the build (null in a player)");
        }
        foreach (var u in loose) Debug.LogWarning("SHADERKEEP a shader looked up by a name worked out in code, not checked: " + u);
    }

    /// <summary>
    /// The magenta checks against a build record already written (SUMMIT_VARIANTS, default
    /// Logs/shader-variants-full.txt), without building: -executeMethod InstancedAssets.CheckMagenta. Writes
    /// Logs/shader-magenta.txt; exit 1 if anything is found.
    /// </summary>
    public static void CheckMagenta()
    {
        string varPath = System.Environment.GetEnvironmentVariable("SUMMIT_VARIANTS") ?? "Logs/shader-variants-full.txt";
        var sb = new StringBuilder();
        Magenta(ReadRecord(varPath), sb);
        string text = $"MAGENTA against {varPath}: {(sb.Length == 0 ? "nothing" : "\n" + sb)}";
        File.WriteAllText("Logs/shader-magenta.txt", text + "\n");
        if (sb.Length == 0) Debug.Log(text); else Debug.LogError(text);
        EditorApplication.Exit(sb.Length == 0 ? 0 : 1);
    }
}
