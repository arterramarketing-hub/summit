using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The wind in the trees, for every tree shader at once (Shaders/TreeWind.hlsl moves the vertices; nothing
    /// here touches a tree). Mostly calm, with gusts that blow through the forest every half a minute or so and
    /// are seen to cross a stand tree by tree. The same gusts push the falling snow sideways (Atmosphere) and
    /// lift the wind in the sound (SoundMix), so you hear and see a gust as the trees bend to it. On top: the
    /// air blast ahead of a released avalanche (SummitGame feeds it the front), and one tree shaken when the
    /// rider or a tumbling body hits it (SummitGame).
    ///
    /// It runs itself in play (a hidden object made at start-up, on scaled time, so a pause stills it). The test
    /// tools, outside play, leave the trees still unless they call Advance or Set.
    /// </summary>
    public class TreeWind : MonoBehaviour
    {
        /// <summary>The way it blows across the ground (x, z): down the mountain and a little across, as the snow drifts.</summary>
        public static readonly Vector2 Dir = new Vector2(0.45f, 0.89f).normalized;
        /// <summary>How hard it blows overall (1 the usual; 0 still).</summary>
        public static float Strength = 1f;
        /// <summary>The wind's clock, seconds.</summary>
        public static float Clock { get; private set; }
        /// <summary>The gust where the camera is, 0 calm .. 1 (for the snowfall and the sound).</summary>
        public static float Here { get; private set; }

        static float blastZ, blast, blastTarget;
        static Vector4 hit, hitDir;
        static readonly int WindId = Shader.PropertyToID("_TreeWind"), BlastId = Shader.PropertyToID("_TreeBlast"),
                            HitId = Shader.PropertyToID("_TreeHit"), HitDirId = Shader.PropertyToID("_TreeHitDir");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (FindFirstObjectByType<TreeWind>() != null) return;
            var g = new GameObject("TreeWind") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(g);
            g.AddComponent<TreeWind>();
        }

        void Update()
        {
            var cam = Camera.main;
            Advance(Time.deltaTime, cam != null ? cam.transform.position : Vector3.zero);
        }

        void OnDestroy() { Still(); }

        /// <summary>Run the wind on by dt (seconds) and put it on the trees; `listener` is where Here is measured.</summary>
        public static void Advance(float dt, Vector3 listener)
        {
            Clock += dt;
            blast = Mathf.MoveTowards(blast, blastTarget, dt * 1.5f);
            Here = Gust(listener.x, listener.z) * Strength;
            Apply();
        }

        /// <summary>Set the clock outright (the test tools: a gust at a chosen moment).</summary>
        public static void Set(float clock, float strength = 1f) { Clock = clock; Strength = strength; Apply(); }

        /// <summary>No wind at all (the test tools' default, and on the way out).</summary>
        public static void Still()
        {
            Shader.SetGlobalVector(WindId, Vector4.zero); Shader.SetGlobalVector(BlastId, Vector4.zero);
            Shader.SetGlobalVector(HitId, Vector4.zero);
        }

        static void Apply()
        {
            Shader.SetGlobalVector(WindId, new Vector4(Dir.x, Dir.y, Strength, Clock));
            Shader.SetGlobalVector(BlastId, new Vector4(blastZ, blast, 70f, 0f));
            Shader.SetGlobalVector(HitId, hit);
            Shader.SetGlobalVector(HitDirId, hitDir);
        }

        /// <summary>The avalanche each step: once it is loose, the air it drives ahead of it bends the trees in its way.</summary>
        public static void Avalanche(bool released, float frontZ, float speed)
        {
            blastZ = frontZ;
            blastTarget = released ? Mathf.Clamp01((speed - 4f) / 22f) : 0f;
        }

        /// <summary>A tree hit (and still standing): it shudders, dying away over a couple of seconds.</summary>
        /// <param name="foot">its foot (the collider's position)</param>
        /// <param name="dir">the way it was knocked</param>
        /// <param name="force">how hard (m/s into it)</param>
        public static void Shake(Vector3 foot, Vector3 dir, float force)
        {
            dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            hit = new Vector4(foot.x, foot.z, Clock, Mathf.Clamp01(force / 14f) * 0.8f + 0.2f);
            hitDir = new Vector4(dir.x, dir.z, 0f, 0f);
            Apply();
        }

        /// <summary>The gust at a place now, 0 calm .. 1 (Shaders/TreeWind.hlsl: TreeGust, the same sum).</summary>
        public static float Gust(float x, float z)
        {
            float u = Clock * 9f - (x * Dir.x + z * Dir.y);
            float n = Noise(u / 85f) * 0.7f + Noise(u / 31f + 7.3f) * 0.3f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n - 0.56f) / 0.3f));
        }

        // ---- the original trees: their models' materials, on the swaying shader

        static Shader sway;

        /// <summary>A copy of an original tree's material that moves in the wind (Resources/TreeSway.shader), keeping
        /// its colour, texture and finish; a plain copy where that shader is missing.</summary>
        public static Material Swaying(Material src)
        {
            if (sway == null) sway = Resources.Load<Shader>("TreeSway");
            var m = new Material(src) { enableInstancing = true };
            if (sway == null || src == null) return m;
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            Texture t = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
            float sm = src.HasProperty("_Smoothness") ? src.GetFloat("_Smoothness") : 0.2f;
            float me = src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0f;
            m.shader = sway;
            m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", t); m.SetFloat("_Smoothness", sm); m.SetFloat("_Metallic", me);
            return m;
        }

        /// <summary>An original tree placed on its own (the camp, the start tape): its materials swapped for swaying copies.</summary>
        /// <param name="cache">Optional: the swaying copies already made, by the material they copy (shared by every tree
        /// placed with it, so they draw instanced and none are made twice: the camp's, kept across its rebuilds).</param>
        public static void SwayAll(GameObject tree, System.Collections.Generic.Dictionary<Material, Material> cache = null)
        {
            foreach (var r in tree.GetComponentsInChildren<MeshRenderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i] == null) continue;
                    if (cache == null) { ms[i] = Swaying(ms[i]); continue; }
                    if (!cache.TryGetValue(ms[i], out var m) || m == null) cache[ms[i]] = m = Swaying(ms[i]);
                    ms[i] = m;
                }
                r.sharedMaterials = ms;
            }
        }

        static float Hash(float n) { float s = Mathf.Sin(n * 12.9898f) * 43758.5453f; return s - Mathf.Floor(s); }
        static float Noise(float x)
        {
            float i = Mathf.Floor(x), f = x - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash(i), Hash(i + 1f), f);
        }
    }
}
