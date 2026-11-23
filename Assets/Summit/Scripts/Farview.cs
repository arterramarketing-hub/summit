using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The far view: how the streamed world meets the distance without anything popping in.
    ///
    /// The terrain, the forest, the hamlets, the lifts and the course are each built in a box round the rider. Each is
    /// let in through the same band near the box's edge (Shaders/Farview.hlsl): drawn whole inside it, not at all past
    /// it, and through the band pixel by pixel, more of them the nearer. The band lies inside what every system has
    /// always built by then (the terrain's 576 m down the hill and 240 m either side are the tightest), so nothing is
    /// ever seen being built: it rides in out of the haze. Past the band is the far field (FarField.cs): the same
    /// mountain, coarse and fogged, out to the backdrop.
    ///
    /// Things that hand over from one version to another (a terrain chunk rebuilt finer, a house's far model turned
    /// into its near one, a yard put out) do it over half a second (Fade, Swap): both versions drawn with the same
    /// screen pattern the opposite way round, so every pixel is exactly one or the other.
    ///
    /// Sets the shader globals every frame and the haze on the backdrop photograph (aerial perspective: the photo is
    /// as hazy as the air between it and the camera, so it is never crisper than the world in front of it).
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class Farview : MonoBehaviour
    {
        /// <summary>The band, in metres from the rider: across the hill wholly drawn within AcrossIn, gone by AcrossOut;
        /// down the hill within DownIn, gone by DownOut. (The terrain is built at least 240 m across and 576 m down.)</summary>
        public const float AcrossIn = 196f, AcrossOut = 236f, DownIn = 495f, DownOut = 562f;
        /// <summary>How long a hand-over between two versions of a thing takes (s).</summary>
        public const float SwapTime = 0.5f;

        public static Farview Instance { get; private set; }
        /// <summary>Is the band in use (false in a tool that never runs this, or with it switched off)?</summary>
        public static bool On => Instance != null && Instance.isActiveAndEnabled && Instance.target != null && !Disabled
                                 && (Application.isPlaying || TestOn);
        /// <summary>For before/after pictures: everything drawn as it was before the band.</summary>
        public static bool Disabled;
        /// <summary>The editor's tools run the game outside play mode and move things about without calling Apply: the
        /// band is used there only by a tool that asks for it (and calls Apply itself).</summary>
        public static bool TestOn;
        /// <summary>The clock the hand-overs run on: real time, or a test's own (&gt;= 0).</summary>
        public static double TestClock = -1.0;
        public static float Clock => TestClock >= 0.0 ? (float)TestClock : Time.realtimeSinceStartup;

        public Transform target;
        public FarField far;

        static readonly int CentreId = Shader.PropertyToID("_FarviewCentre"), BandId = Shader.PropertyToID("_FarviewBand"),
                            ClockId = Shader.PropertyToID("_FarviewClock"), SwapId = Shader.PropertyToID("_FarviewSwap"),
                            HazeId = Shader.PropertyToID("_FarviewHaze");

        public static Farview Create(Transform target, Material snow, Material foliage)
        {
            var go = new GameObject("Farview");
            var f = go.AddComponent<Farview>();
            f.target = target;
            f.far = FarField.Create(go.transform, target, snow, foliage);
            Instance = f;
            f.Apply();
            return f;
        }

        void OnEnable() { if (Instance == null) Instance = this; }
        void OnDisable() { if (Instance == this) Shader.SetGlobalVector(CentreId, Vector4.zero); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void LateUpdate() { Apply(); Step(); }

        /// <summary>The shader globals for this frame (the test tools call it themselves).</summary>
        public void Apply()
        {
            Instance = this;
            bool on = On;
            Vector3 p = target != null ? target.position : Vector3.zero;
            Shader.SetGlobalVector(CentreId, new Vector4(p.x, p.y, p.z, on ? 1f : 0f));
            Shader.SetGlobalVector(BandId, new Vector4(AcrossIn, AcrossOut, DownIn, DownOut));
            Shader.SetGlobalVector(ClockId, new Vector4(Clock, SwapTime, 0f, 0f));
            // aerial perspective. The fog is exp2 in its density, so it is light near and thick far; the far field
            // and the photo take more of it with distance (and the photo, at its 1.5 km, a fixed share) so the
            // country beyond the streamed world fades into the same air. Not in thick weather: there the fog
            // already hides it all, and the weather veils the photo itself (Backdrop.shader)
            float dens = RenderSettings.fogDensity;
            float clear = 1f - Mathf.Clamp01((dens - 0.004f) / 0.004f);
            float photo = on ? Mathf.Clamp01(dens / 0.0022f) * clear : 0f;
            Shader.SetGlobalVector(HazeId, new Vector4(photo, 700f, 1450f, on ? 0.55f * clear : 0f));
            if (far != null) far.gameObject.SetActive(on);
        }

        // ---------------------------------------------------------------- hand-overs over half a second

        struct Fading { public Renderer r; public float t0; public bool on, hadBlock; }
        readonly List<Fading> fading = new List<Fading>();
        static MaterialPropertyBlock block;

        /// <summary>
        /// Show or hide `r` over SwapTime instead of at once (its materials must be Farview's: Adopt). Calling it again
        /// before it is done turns it round. Without the band (On false) it is shown or hidden at once.
        /// </summary>
        public static void Fade(Renderer r, bool on)
        {
            if (r == null) return;
            var f = Instance;
            if (!On || f == null)
            {
                r.enabled = on;
                return;
            }
            int i = f.Find(r);
            if (i < 0 && r.enabled == on) return;
            float now = Clock;
            float t0 = now;
            if (i >= 0)
            {
                var g = f.fading[i];
                if (g.on == on) return;
                // turned round half-way: start from where it got to
                float k = Mathf.Clamp01((now - g.t0) / SwapTime);
                t0 = now - (1f - k) * SwapTime;
                g.on = on; g.t0 = t0; f.fading[i] = g;
            }
            else f.fading.Add(new Fading { r = r, t0 = t0, on = on, hadBlock = r.HasPropertyBlock() });
            r.enabled = true;
            Set(r, new Vector4(t0, on ? 1f : -1f, 0f, 0f));
        }

        /// <summary>Hand over from one set of renderers to another (either may be null).</summary>
        public static void Swap(Renderer[] from, Renderer[] to)
        {
            if (from != null) foreach (var r in from) Fade(r, false);
            if (to != null) foreach (var r in to) Fade(r, true);
        }

        /// <summary>Is anything still handing over?</summary>
        public static int FadingCount => Instance != null ? Instance.fading.Count : 0;

        int Find(Renderer r)
        {
            for (int i = 0; i < fading.Count; i++) if (fading[i].r == r) return i;
            return -1;
        }

        static void Set(Renderer r, Vector4 swap)
        {
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetVector(SwapId, swap);
            r.SetPropertyBlock(block);
        }

        /// <summary>Finish what has had its half second (the test tools call it themselves).</summary>
        public void Step()
        {
            float now = Clock;
            for (int i = fading.Count - 1; i >= 0; i--)
            {
                var g = fading[i];
                if (g.r == null) { fading.RemoveAt(i); continue; }
                if (now - g.t0 < SwapTime && On) continue;
                g.r.enabled = g.on;
                if (g.hadBlock) Set(g.r, Vector4.zero); else g.r.SetPropertyBlock(null);
                fading.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- materials

        static Shader farLit;
        static Shader FarLit
        {
            get
            {
                if (farLit == null) { farLit = Resources.Load<Shader>("FarLit"); if (farLit != null && !farLit.isSupported) farLit = null; }
                return farLit;
            }
        }

        static bool IsUrpLit(Shader s) => s != null && s.name == "Universal Render Pipeline/Lit";

        /// <summary>
        /// Make a plain opaque URP Lit material come in through the band (and hand over with Fade), in place: the same
        /// surface and lighting, drawn by Resources/FarLit.shader. Anything else (see-through, cut-out, another shader)
        /// is left as it is. Returns the material.
        /// </summary>
        public static Material Adopt(Material m)
        {
            if (m == null || FarLit == null) return m;
            if (m.shader == FarLit)
            {
                // (made from FarLit by someone else: it has the band already, unless it was made plain)
                if (m.IsKeywordEnabled("FARVIEW_PLAIN")) { m.DisableKeyword("FARVIEW_PLAIN"); m.renderQueue = Mathf.Max(m.renderQueue, (int)RenderQueue.AlphaTest); }
                return m;
            }
            if (!IsUrpLit(m.shader)) return m;
            if (m.GetFloat("_Surface") > 0.5f || m.IsKeywordEnabled("_ALPHATEST_ON")) return m;
            bool emit = m.IsKeywordEnabled("_EMISSION");
            bool snow = m.IsKeywordEnabled("_NORMALMAP") || m.IsKeywordEnabled("_DETAIL_MULX2");
            bool parallax = m.IsKeywordEnabled("_PARALLAXMAP");
            int queue = m.renderQueue;
            m.shader = FarLit;
            Keys(m, emit, snow, parallax, true);
            m.renderQueue = queue > (int)RenderQueue.AlphaTest ? queue : (int)RenderQueue.AlphaTest;
            return m;
        }

        /// <summary>Adopt every material of these renderers (shared materials, so each is changed once for all).</summary>
        public static void Adopt(IEnumerable<Renderer> rs)
        {
            foreach (var r in rs) if (r != null) foreach (var m in r.sharedMaterials) Adopt(m);
        }

        /// <summary>The terrain's snow as FarLit draws it: a copy of the snow material, with or without the band.</summary>
        public static Material Snow(Material snow, bool edge)
        {
            if (snow == null || FarLit == null || !IsUrpLit(snow.shader)) return snow;
            var m = new Material(snow) { name = snow.name + (edge ? " (edge)" : " (far)") };
            bool emit = snow.IsKeywordEnabled("_EMISSION"), parallax = snow.IsKeywordEnabled("_PARALLAXMAP");
            m.shader = FarLit;
            Keys(m, emit, true, parallax, edge);
            m.renderQueue = edge ? (int)RenderQueue.AlphaTest : snow.renderQueue;
            m.enableInstancing = snow.enableInstancing;
            return m;
        }

        static void Keys(Material m, bool emit, bool snow, bool parallax, bool edge)
        {
            foreach (var k in m.shaderKeywords) m.DisableKeyword(k);
            if (emit) m.EnableKeyword("_EMISSION");
            if (snow) m.EnableKeyword("FARVIEW_SNOW");
            if (parallax && snow) m.EnableKeyword("_PARALLAXMAP");
            if (!edge) m.EnableKeyword("FARVIEW_PLAIN");
        }

        /// <summary>How much of something at `p` the band lets in (0..1), as the shaders work it out.</summary>
        public static float Shown(Vector3 p)
        {
            if (!On) return 1f;
            Vector3 c = Instance.target.position;
            float across = Mathf.Clamp01((AcrossOut - Mathf.Abs(p.x - c.x)) / (AcrossOut - AcrossIn));
            float down = Mathf.Clamp01((DownOut - (p.z - c.z)) / (DownOut - DownIn));
            return across * down;
        }
    }
}
