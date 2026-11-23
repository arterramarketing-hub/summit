using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The one wind (Shaders/TreeWind.hlsl moves the vertices of every tree; nothing here touches a tree): the only
    /// owner of the wind's shader globals, set once a frame. A field of gusts and eddies frozen into the air and
    /// carried down the mountain at the wind's speed, so a gust is seen to cross a stand tree by tree and trees side
    /// by side bend together, whatever their kind or level of detail. Its strength is the weather's (Weather sets it
    /// through ForWeather: calm on a clear day, more in snow, strong on a windy day, strongest in a storm), and the
    /// same gusts push the falling snow sideways (Atmosphere), drive the ground drift and the snow off the summit
    /// (Weather, WeatherFX) and lift the wind in the sound (SoundMix), so you hear and see a gust as the trees bend to
    /// it. On top: the air blast ahead of a released avalanche (SummitGame feeds it the front), and one tree shaken
    /// when the rider or a tumbling body hits it (SummitGame).
    ///
    /// It runs itself in play (a hidden object made at start-up, on scaled time, so a pause stills it). The test
    /// tools, outside play, leave the trees still unless they call Advance or Set. Gust and Sway are the shader's sums
    /// on the CPU (WindCheck compares trees with them).
    /// </summary>
    public class TreeWind : MonoBehaviour
    {
        /// <summary>The way it blows across the ground (x, z): down the mountain and a little across, as the snow drifts.
        /// (The mountain's prevailing wind, the same in every weather: turning it would swing the whole moving field.)</summary>
        public static readonly Vector2 Dir = new Vector2(0.45f, 0.89f).normalized;
        /// <summary>How hard it blows overall (0 still; about 0.5 a calm clear day, 2.3 a storm: ForWeather).</summary>
        public static float Strength = 1f;
        /// <summary>The wind's clock, seconds.</summary>
        public static float Clock { get; private set; }
        /// <summary>How far the air has travelled down the wind (m): the gusts' place.</summary>
        public static float Air { get; private set; }
        /// <summary>The gust where the camera is, 0 calm .. 1 (for the snowfall and the sound; not scaled by Strength).</summary>
        public static float Here { get; private set; }

        /// <summary>Each kind's give (the shaders' _WindFlex): how far it bends in the same air. A fir is stiff, a pine a
        /// little less (and the snags among them), the original low-poly firs as the firs, a bare birch loose.</summary>
        public const float FlexFir = 1f, FlexPine = 1.15f, FlexClassic = 1f, FlexBirch = 1.6f;

        /// <summary>The wind's strength for the weather's wind (0..1) and its storm (Weather.Howl, 0..1).</summary>
        public static float ForWeather(float wind, float howl) => 0.2f + 1.6f * wind + 0.5f * howl;

        /// <summary>How fast the air (and a gust in it) moves down the mountain at a strength, m/s.</summary>
        public static float Speed(float strength) => 6f + 3f * strength;

        static float blastZ, blast, blastTarget;
        static Vector4 hit, hitDir;
        static readonly int WindId = Shader.PropertyToID("_TreeWind"), AirId = Shader.PropertyToID("_TreeAir"), BlastId = Shader.PropertyToID("_TreeBlast"),
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
            Air += dt * Speed(Strength);   // (carried on, not worked out from the clock: a change of strength never jumps it)
            blast = Mathf.MoveTowards(blast, blastTarget, dt * 1.5f);
            Here = Gust(listener.x, listener.z);
            Apply();
        }

        /// <summary>Set the clock outright (the test tools: a gust at a chosen moment), the air as if it had always blown so.</summary>
        public static void Set(float clock, float strength = 1f) { Clock = clock; Strength = strength; Air = clock * Speed(strength); Apply(); }

        /// <summary>No wind at all (the test tools' default, and on the way out).</summary>
        public static void Still()
        {
            Shader.SetGlobalVector(WindId, Vector4.zero); Shader.SetGlobalVector(AirId, Vector4.zero);
            Shader.SetGlobalVector(BlastId, Vector4.zero); Shader.SetGlobalVector(HitId, Vector4.zero);
        }

        static void Apply()
        {
            Shader.SetGlobalVector(WindId, new Vector4(Dir.x, Dir.y, Strength, Clock));
            Shader.SetGlobalVector(AirId, new Vector4(Air, 0f, 0f, 0f));
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

        // ---- the field, as the shader has it (Shaders/TreeWind.hlsl: the same sums)

        /// <summary>Where in the moving air a place is (m along the wind): TreeWindS.</summary>
        public static float S(float x, float z)
        {
            float across = -x * Dir.y + z * Dir.x;
            return Air - (x * Dir.x + z * Dir.y) + 12f * Noise(across / 140f + 3.7f);
        }

        static float GustAt(float s)
        {
            float n = Noise(s / 85f) * 0.7f + Noise(s / 31f + 7.3f) * 0.3f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n - 0.56f) / 0.3f));
        }

        /// <summary>The gust at a place now, 0 calm .. 1 (TreeGust).</summary>
        public static float Gust(float x, float z) => GustAt(S(x, z));

        /// <summary>How far a point h metres up a tree is carried for one unit of lean (TreeBend).</summary>
        public static float Bend(float h) => h * h / (h + 10f) * 0.02f;

        /// <summary>
        /// Where the wind moves a point of a tree standing at `origin` (TreeSway's wind, without an avalanche's blast or
        /// a hit): `boughs` 1 for a whole tree, 0 for a far picture; `flex` the kind's give (FlexFir...).
        /// </summary>
        public static Vector3 Sway(Vector3 pos, Vector3 origin, float boughs, float flex)
        {
            float k = Strength;
            if (flex <= 0f || k <= 0f) return pos;
            Vector3 rel = pos - origin;
            float h = Mathf.Max(rel.y, 0f), r = Mathf.Min(new Vector2(rel.x, rel.z).magnitude, 6f), t = Clock;
            var dir = new Vector3(Dir.x, 0f, Dir.y); var across = new Vector3(-dir.z, 0f, dir.x);
            float jit = Hash(origin.x * 0.1031f + origin.z * 0.1131f) - 0.5f;
            float bp = pos.x * 0.9f + pos.y * 0.6f + pos.z * 1.1f;
            float reach = r * r * 0.035f * boughs;
            float s = S(origin.x, origin.z) + jit * 1.5f;
            float gust = GustAt(s);
            float turb = Noise(s / 45f + 3.1f) - 0.5f, side = Noise(s / 50f + 11.7f) - 0.5f;
            float rock = Mathf.Sin(t * 1.9f - (origin.x * Dir.x + origin.z * Dir.y) * 0.06f + jit * 0.5f);
            float lean = k * (0.15f + gust + turb * (0.2f + 0.45f * gust) + (0.08f + 0.25f * gust) * rock);
            var off = (dir * lean + across * (side * k * (0.15f + 0.35f * gust))) * Bend(h);
            off.y -= 0.5f * (off.x * off.x + off.z * off.z) / Mathf.Max(h, 1f);
            off.y += reach * 0.25f * k * (0.08f + 0.5f * gust) * Mathf.Sin(t * 4.6f + bp);
            return pos + off * flex;
        }

        // ---- the original trees: their models' materials, on the swaying shader

        static Shader sway;

        /// <summary>A copy of a tree's material that moves in the wind (Resources/TreeSway.shader), keeping its colour,
        /// texture and finish, giving as much as `flex` says; a plain copy where that shader is missing. Bends from the
        /// renderer's own origin: a tree's parts need it at the tree's foot (one mesh a tree, as the models have it).</summary>
        public static Material Swaying(Material src, float flex = FlexClassic)
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
            m.SetFloat("_WindFlex", flex);
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
