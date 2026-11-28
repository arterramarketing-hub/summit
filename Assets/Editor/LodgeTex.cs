using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The lodge's PBR materials (Resources/Camp/LodgeTex, made by Tools/MakeLodgeTex.py from the CC0 sets in CREDITS.txt):
/// sets each texture's import for the phone (ASTC on iPhone: 6x6 for the colour and the normal map, 8x8 for the packed
/// mask; their own size, 1K for the big surfaces and 512 for the small ones; mipmaps, repeat, a little anisotropy) and
/// makes the lodge's materials as assets (URP Lit with a normal map and the packed metallic-gloss / occlusion mask, the
/// tint and tiling each surface wants), so their shader variants go into the build (materials made in code with these
/// keywords could have them stripped). CampLodge.LodgeMat uses them by name.
/// Batch: -executeMethod LodgeTex.Make (no graphics needed).
/// </summary>
public static class LodgeTex
{
    const string Dir = "Assets/Summit/Resources/Camp/LodgeTex";

    /// <summary>A lodge material: its texture set, tint, smoothness (times the mask's), normal strength, occlusion, tiling.</summary>
    struct Def
    {
        public string name, set; public Color tint; public float smooth, bump, occlusion; public Vector2 tiling;
        public Def(string n, string s, Color t, float sm = 1f, float b = 1f, float o = 0.8f, float tx = 1f, float ty = 1f)
        { name = n; set = s; tint = t; smooth = sm; bump = b; occlusion = o; tiling = new Vector2(tx, ty); }
    }

    // (the tints over textures graded to a neutral mean: Tools/MakeLodgeTex.py)
    static readonly Def[] Defs =
    {
        new Def("Glulam", "Oak", new Color(1f, 0.86f, 0.66f), 0.75f),            // warm glued-laminated beams, rafters, columns
        new Def("Oak", "Oak", new Color(1f, 0.95f, 0.86f), 0.85f),               // tables, shelves, treads, the board shop's counter
        new Def("CounterTop", "Oak", new Color(0.42f, 0.28f, 0.19f), 1f),        // the café's counter: walnut-stained oak
        new Def("Lining", "Planks", new Color(1f, 0.94f, 0.86f), 0.7f, 0.8f, 0.7f),       // the pale boards inside
        new Def("Soffit", "Planks", new Color(1f, 0.86f, 0.68f), 0.7f, 0.8f, 0.7f),       // the ceiling's boards (and under the eaves)
        new Def("FloorBoards", "Planks", new Color(0.86f, 0.72f, 0.56f), 1f, 0.8f, 0.7f), // the upper floors
        new Def("Cladding", "BlackPlanks", new Color(0.78f, 0.63f, 0.52f), 1f),  // the dark-stained boards outside (warm, as the photo's)
        new Def("Floor", "Concrete", new Color(0.84f, 0.82f, 0.79f), 1f, 0.6f, 0.7f),     // polished concrete inside
        new Def("Concrete", "Concrete", new Color(1.0f, 0.99f, 0.96f), 0.45f, 0.8f, 0.8f), // the terrace, the base, the steps
        new Def("Hearth", "Stone", new Color(0.92f, 0.9f, 0.86f), 1f, 1.2f, 1f),           // the fire's stacked-stone hearth
        new Def("Steel", "Steel", new Color(0.5f, 0.5f, 0.53f), 1f, 0.6f, 1f),             // black powder-coated frames, rails, stringers
        new Def("Upholstery", "Tweed", new Color(0.36f, 0.37f, 0.4f), 0.5f, 1f, 1f),       // the sofas
        new Def("Cushion", "Tweed", new Color(0.72f, 0.69f, 0.64f), 0.5f, 1f, 1f),         // their cushions, the armchairs' seats
        new Def("Runner", "Tweed", new Color(0.22f, 0.23f, 0.26f), 0.4f, 1f, 1f),          // the runner, the door mat
        new Def("Rug", "Check", new Color(0.95f, 0.88f, 0.76f), 0.4f, 1f, 1f, 1f, 0.81f),   // the wool rugs (its weave 24% taller than wide)
        new Def("Hide", "Leather", Color.white, 1f, 1f, 0.6f),                             // the leather armchairs and sofas
        new Def("Decking", "Planks", new Color(0.66f, 0.56f, 0.47f), 0.55f, 1f, 0.8f),     // lodge5: the lookout's deck boards outside (weathered)
        new Def("Throw", "Check", new Color(0.74f, 0.2f, 0.16f), 0.3f, 1f, 1f),            // lodge5: the wool throws over the sofas (red check)
    };

    public static void Make()
    {
        // the textures: each set's colour, normal map and mask
        var sets = new HashSet<string>();
        foreach (var d in Defs) sets.Add(d.set);
        foreach (var s in sets)
        {
            Import($"{Dir}/{s}_Albedo.jpg", TextureImporterType.Default, true, TextureImporterFormat.ASTC_6x6);
            Import($"{Dir}/{s}_Normal.jpg", TextureImporterType.NormalMap, false, TextureImporterFormat.ASTC_6x6);
            Import($"{Dir}/{s}_Mask.png", TextureImporterType.Default, false, TextureImporterFormat.ASTC_8x8);
        }
        // the art on the lodge's walls (Tools/MakeLodgeSigns.swift: the trail map, the posters, the LOOKOUT sign): one
        // 1K sheet, ASTC 6x6 on the phone, clamped (its pieces run to its edges)
        {
            const string artPath = "Assets/Summit/Resources/Camp/LodgeArt.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(artPath);
            if (ti == null) Debug.LogError("LODGETEX missing " + artPath);
            else
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 2;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.textureCompression = TextureImporterCompression.Compressed; ti.maxTextureSize = 1024;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50 });
                ti.SaveAndReimport();
            }
        }
        // the materials
        var lit = Summit.Mats.LitShader;
        System.IO.Directory.CreateDirectory(Dir + "/Materials");
        foreach (var d in Defs)
        {
            string path = $"{Dir}/Materials/{d.name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = m == null;
            if (fresh) m = new Material(lit);
            m.shader = lit;
            m.name = d.name;
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{d.set}_Albedo.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{d.set}_Normal.jpg");
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{d.set}_Mask.png");
            m.SetFloat("_WorkflowMode", 1f);   // metallic
            m.SetFloat("_Surface", 0f);
            m.SetTexture("_BaseMap", albedo); m.SetColor("_BaseColor", d.tint);
            m.SetTextureScale("_BaseMap", d.tiling);
            m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", d.bump);
            m.SetTexture("_MetallicGlossMap", mask); m.SetFloat("_Smoothness", d.smooth); m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);   // (the mask's alpha)
            m.SetTexture("_OcclusionMap", mask); m.SetFloat("_OcclusionStrength", d.occlusion);
            m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP");
            m.enableInstancing = true;
            if (fresh) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            Debug.Log($"LODGETEX material {d.name}: {d.set}, tint {d.tint}, tiling {d.tiling}");
        }
        AssetDatabase.SaveAssets();
        // what they cost: each texture's size as built for the phone (ASTC), and in all
        long total = 0;
        foreach (var s in sets)
            foreach (var suffix in new[] { "_Albedo.jpg", "_Normal.jpg", "_Mask.png" })
            {
                string p = $"{Dir}/{s}{suffix}";
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                var ps = ti.GetPlatformTextureSettings("iPhone");
                int w = t.width, h = t.height, mips = t.mipmapCount;
                long bytes = AstcBytes(w, h, mips, ps.format);
                total += bytes;
                Debug.Log($"LODGETEX {p}: {w}x{h}, {mips} mips, iPhone {ps.format} max {ps.maxTextureSize}: {bytes / 1024} KB");
            }
        Debug.Log($"LODGETEX all the lodge's textures on the phone: {total / 1024} KB ({total / (1024f * 1024f):0.00} MB)");
    }

    /// <summary>
    /// What the lodge's textures and materials add to the iOS build: builds the Xcode project (iOSBuild/, nothing
    /// installed) and sums the build report's packed sizes for them, with the build's total. Only when the project is
    /// already on iOS (switching would reimport everything for it). Batch: -executeMethod LodgeTex.BuildSize
    /// </summary>
    public static void BuildSize()
    {
        Debug.Log("LODGETEX active build target " + EditorUserBuildSettings.activeBuildTarget);
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
        {
            Debug.Log("LODGETEX not building: the project isn't on iOS here");
            TestHarness.Exit(0);
            return;
        }
        var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Summit/Summit.unity" }, locationPathName = "iOSBuild", target = BuildTarget.iOS, options = BuildOptions.None
        });
        long tex = 0, mats = 0, all = 0;
        foreach (var pa in report.packedAssets)
            foreach (var c in pa.contents)
            {
                all += (long)c.packedSize;
                if (!c.sourceAssetPath.Contains("/LodgeTex/")) continue;
                if (c.sourceAssetPath.EndsWith(".mat")) mats += (long)c.packedSize; else tex += (long)c.packedSize;
            }
        Debug.Log($"LODGETEX build {report.summary.result}: the lodge's textures {tex / 1024} KB, its materials {mats / 1024} KB; all packed assets {all / 1024} KB; build total {report.summary.totalSize / 1024} KB");
        TestHarness.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    static void Import(string path, TextureImporterType type, bool srgb, TextureImporterFormat phone)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) { Debug.LogError("LODGETEX missing " + path); return; }
        ti.textureType = type;
        ti.sRGBTexture = srgb;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = 2;
        ti.alphaSource = path.EndsWith("_Mask.png") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.GetSourceTextureWidthAndHeight(out int w, out int h);
        int size = Mathf.Max(w, h);
        ti.maxTextureSize = size;
        ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = size, format = phone, compressionQuality = 50 });
        ti.SaveAndReimport();
    }

    /// <summary>ASTC's size: 16 bytes a block, every mip level.</summary>
    static long AstcBytes(int w, int h, int mips, TextureImporterFormat f)
    {
        int b = f == TextureImporterFormat.ASTC_4x4 ? 4 : f == TextureImporterFormat.ASTC_5x5 ? 5 : f == TextureImporterFormat.ASTC_6x6 ? 6 : f == TextureImporterFormat.ASTC_8x8 ? 8 : f == TextureImporterFormat.ASTC_10x10 ? 10 : 12;
        long sum = 0;
        for (int i = 0; i < mips; i++)
        {
            int mw = Mathf.Max(1, w >> i), mh = Mathf.Max(1, h >> i);
            sum += (long)((mw + b - 1) / b) * ((mh + b - 1) / b) * 16;
        }
        return sum;
    }
}
