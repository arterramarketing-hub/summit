using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Summit.EditorTools
{
    /// <summary>
    /// Summit ▸ Build Scene: makes the materials, a sky, a sun and a camera,
    /// saves Assets/Summit/Summit.unity and puts it first in the build.
    /// Safe to run again; it rebuilds the scene and reuses the materials.
    /// </summary>
    public static class SummitBuilder
    {
        const string Root = "Assets/Summit";
        const string MatDir = Root + "/Materials";
        const string ScenePath = Root + "/Summit.unity";

        [MenuItem("Summit/Build Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "Summit");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader particle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (lit == null)
            {
                EditorUtility.DisplayDialog("Summit",
                    "The Universal Render Pipeline was not found.\n\nCreate the project from the \"Universal 3D\" template, then run Summit ▸ Build Scene again.",
                    "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // sky
            var sky = LoadOrCreate(MatDir + "/Sky.mat", Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 0.75f);
            sky.SetColor("_SkyTint", new Color(0.42f, 0.56f, 0.82f));
            sky.SetColor("_GroundColor", new Color(0.78f, 0.84f, 0.92f));
            sky.SetFloat("_Exposure", 1.25f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0052f;
            RenderSettings.fogColor = new Color(0.80f, 0.87f, 0.95f);

            // sun
            var sun = new GameObject("Sun");
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.95f, 0.86f);
            l.intensity = 1.1f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            RenderSettings.sun = l;

            // camera
            var cg = new GameObject("Main Camera");
            cg.tag = "MainCamera";
            var cam = cg.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 900f;
            cg.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            Post();

            // game
            var game = new GameObject("Summit").AddComponent<SummitGame>();
            game.snow = Lit(lit, "Snow", new Color(0.86f, 0.89f, 0.95f), 0.28f);
            game.foliage = Lit(lit, "Foliage", new Color(0.12f, 0.25f, 0.21f), 0.05f);
            game.bark = Lit(lit, "Bark", new Color(0.30f, 0.22f, 0.16f), 0.05f);
            game.jacket = Lit(lit, "Jacket", new Color(0.95f, 0.36f, 0.14f), 0.3f);
            game.pants = Lit(lit, "Pants", new Color(0.13f, 0.15f, 0.22f), 0.2f);
            game.board = Lit(lit, "Board", new Color(0.10f, 0.45f, 0.90f), 0.6f);
            game.skin = Lit(lit, "Skin", new Color(0.88f, 0.70f, 0.58f), 0.3f);
            game.dark = Lit(lit, "Goggles", new Color(0.05f, 0.05f, 0.07f), 0.9f);
            game.trail = Unlit(particle, "Trail", Color.white, false);
            game.spray = Unlit(particle, "Spray", Color.white, true);

            TuneUrp();

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Summit: scene built at " + ScenePath + ". Press Play.");
        }

        static Material LoadOrCreate(string path, Shader sh)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        static Material Lit(Shader sh, string name, Color c, float smooth)
        {
            var m = LoadOrCreate(MatDir + "/" + name + ".mat", sh);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A soft, transparent, unlit material for the trail and the powder.</summary>
        static Material Unlit(Shader sh, string name, Color c, bool soft)
        {
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = LoadOrCreate(MatDir + "/" + name + ".mat", sh);
            m.SetColor("_BaseColor", c);
            // URP particle shader: Surface = Transparent, Blend = Alpha
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            if (soft) m.SetTexture("_BaseMap", SoftDot());
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A round, feathered dot so powder reads as puffs, not squares.</summary>
        static Texture2D SoftDot()
        {
            string path = MatDir + "/SoftDot.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Gentle grading: neutral tonemap, a little bloom off the snow, a soft vignette.</summary>
        static void Post()
        {
            string path = Root + "/Post.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            var tm = Get<Tonemapping>(profile);
            tm.mode.Override(TonemappingMode.Neutral);
            var bloom = Get<Bloom>(profile);
            bloom.intensity.Override(0.3f);
            bloom.threshold.Override(1.05f);
            var col = Get<ColorAdjustments>(profile);
            col.postExposure.Override(0.1f);
            col.contrast.Override(10f);
            col.saturation.Override(8f);
            var vig = Get<Vignette>(profile);
            vig.intensity.Override(0.2f);
            vig.smoothness.Override(0.5f);
            EditorUtility.SetDirty(profile);

            var vol = new GameObject("Post").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (p.TryGet(out T c)) return c;
            c = p.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        /// <summary>Longer shadows and 4x MSAA on every URP asset the project uses (each quality level has its own).</summary>
        static void TuneUrp()
        {
            var assets = new System.Collections.Generic.HashSet<RenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i);
                if (a != null) assets.Add(a);
            }
            foreach (var rp in assets)
            {
                var so = new SerializedObject(rp);
                var p = so.FindProperty("m_ShadowDistance");
                if (p != null) p.floatValue = 140f;
                p = so.FindProperty("m_MSAA");
                if (p != null) p.intValue = 4;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
