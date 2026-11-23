using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Development check for things drawn magenta (Unity's error shader). With the environment variable
    /// SUMMIT_PURPLE=1 (on the phone: Xcode, the scheme's Run > Arguments > Environment Variables) it looks over the
    /// game a second after start-up and every 30 s after, and prints one line per thing that would come out magenta:
    ///   PURPLE &lt;scene path&gt; &lt;shader&gt; (&lt;why&gt;)
    /// and a summary line "PURPLE scan ...". What it looks for:
    ///  - a renderer's material slot left empty (a renderer draws an empty slot with the error shader);
    ///  - a particle system drawing particles or trails with no material;
    ///  - a material whose shader is missing or Unity's error shader, or that this device cannot run (isSupported false:
    ///    a shader with no SubShader that compiled for this GPU is drawn with the error shader);
    ///  - a shader with no pass URP draws (built-in pipeline shaders: magenta in the editor and development builds,
    ///    nothing in a release build);
    ///  - every material loaded (instanced draws through Graphics.RenderMesh*), the terrains' and the sky's.
    /// Without the variable it does nothing (no object is made). The editor's checks use Scan directly (ShaderCensus).
    /// </summary>
    public class PurpleWatch : MonoBehaviour
    {
        public const string Env = "SUMMIT_PURPLE";
        const float First = 1f, Every = 30f;
        float next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Environment.GetEnvironmentVariable(Env) != "1") return;
            var go = new GameObject("PurpleWatch");
            DontDestroyOnLoad(go);
            go.AddComponent<PurpleWatch>().next = Time.realtimeSinceStartup + First;
            Debug.Log($"PURPLE watch on: a look a second after start-up and every {Every:0} s");
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + Every;
            var found = Scan(out int renderers, out int materials);
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var f in found) { if (shown++ >= 200) { sb.AppendLine($"PURPLE ... and {found.Count - 200} more"); break; } sb.AppendLine(f); }
            sb.Append($"PURPLE scan at {Time.realtimeSinceStartup:0} s: {renderers} renderers and {materials} materials looked at, {found.Count} magenta");
            Debug.Log(sb.ToString());
        }

        static readonly ShaderTagId LightMode = new ShaderTagId("LightMode");
        static readonly HashSet<string> urpPasses = new HashSet<string>
        { "", "SRPDefaultUnlit", "UniversalForward", "UniversalForwardOnly", "Universal2D", "UniversalGBuffer" };

        /// <summary>Why a material would be drawn magenta here, or null if it would not.</summary>
        public static string Why(Material m)
        {
            if (m == null) return "no material";
            var sh = m.shader;
            if (sh == null) return "no shader";
            if (sh.name == "Hidden/InternalErrorShader") return "the error shader (its shader is missing or failed)";
            if (!sh.isSupported) return "not supported on this GPU";
            bool drawn = false;
            for (int p = 0; p < sh.passCount && !drawn; p++) drawn = urpPasses.Contains(sh.FindPassTagValue(p, LightMode).name);
            if (!drawn) return "no pass URP draws";
            return null;
        }

        /// <summary>Every renderer, terrain, the sky and every loaded material that would come out magenta.</summary>
        public static List<string> Scan(out int renderers, out int materials)
        {
            var found = new List<string>();
            var seen = new HashSet<string>();
            void Note(string where, Material m, string why)
            {
                string line = $"PURPLE {where} {(m != null && m.shader != null ? m.shader.name : "-")} ({why})";
                if (seen.Add(line)) found.Add(line);
            }
            renderers = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                renderers++;
                var mats = r.sharedMaterials;
                if (r is ParticleSystemRenderer pr)
                {
                    var ps = r.GetComponent<ParticleSystem>();
                    if (pr.renderMode != ParticleSystemRenderMode.None && pr.sharedMaterial == null) Note(PathOf(r.transform), null, "particles with no material");
                    if (ps != null && ps.trails.enabled && pr.trailMaterial == null) Note(PathOf(r.transform), null, "particle trails with no material");
                }
                for (int i = 0; i < mats.Length; i++)
                {
                    if (r is ParticleSystemRenderer && i > 0) break;   // (slot 1 is the trails', above)
                    string why = Why(mats[i]);
                    if (why != null) Note(PathOf(r.transform) + (mats.Length > 1 ? $"[{i}]" : ""), mats[i], why);
                }
            }
            foreach (var t in Terrain.activeTerrains)
                if (t != null && t.materialTemplate != null && Why(t.materialTemplate) is string tw) Note(PathOf(t.transform) + " (terrain)", t.materialTemplate, tw);
            if (RenderSettings.skybox != null && Why(RenderSettings.skybox) is string sw) Note("(sky)", RenderSettings.skybox, sw);
            materials = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.shader == null) continue;
                // (the engine's own error material, which it draws magenta things with: unnamed, hidden, never saved)
                if (m.hideFlags == HideFlags.HideAndDontSave && string.IsNullOrEmpty(m.name)) continue;
                materials++;
                // only what cannot be drawn at all (a material left lying about with a built-in shader draws nothing)
                var sh = m.shader;
                if (sh.name == "Hidden/InternalErrorShader" || !sh.isSupported) Note("material:" + m.name, m, Why(m));
            }
            return found;
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString().Replace(' ', '_');
        }
    }
}
