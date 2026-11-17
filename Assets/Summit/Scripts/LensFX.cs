using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Summit
{
    /// <summary>
    /// What the lens does besides its field of view, from the pause menu's LENS and LENS TYPE rows.
    ///
    /// SPHERICAL: the barrel warp the LENS row always had, plus the faults of a cheap wide lens toward the
    /// edges, growing with the LENS setting: colour fringing and softness (Resources/LensEdge.shader). At
    /// STANDARD they are off; at FISHEYE they are the skate-video "death lens".
    ///
    /// ANAMORPHIC: a cinema lens. Glints off the snow throw long thin blue streaks sideways across the frame.
    /// Sunlit snow here is a broad even white with no point highlights, and the sun is always high behind the
    /// camera, so a screen-space streak filter (URP's Screen Space Lens Flare was tried) finds nothing to catch,
    /// or smears the whole slope. Instead the lens brings its own: glints that flash on the slope ahead, each
    /// with a thin blue streak drawn through it, sized by distance so every streak reaches across most of
    /// the screen. A few dozen particles, much cheaper than a streak filter. The barrel warp acts mostly
    /// sideways, so the frame feels squeezed and oval, and thin black bars top and bottom say "cinema"; a true
    /// 2.39:1 letterbox would leave a strip a third of the height of a tall phone, so they stay thin.
    ///
    /// The edge pass is one full-screen draw with six taps only near the edges (a single read in the middle),
    /// after post-processing; it is skipped entirely when there is nothing to draw.
    /// </summary>
    public class LensFX : MonoBehaviour
    {
        public static readonly string[] ModeName = { "SPHERICAL", "ANAMORPHIC" };

        // per LENS level (STANDARD, WIDE, WIDER, FISHEYE): how far the edges smear, and the share of that
        // smear that splits into colour, as a fraction of the distance from the centre
        static readonly float[] SphBlur = { 0f, 0.012f, 0.022f, 0.036f };
        static readonly float[] SphFringe = { 0f, 0.55f, 0.6f, 0.65f };
        static readonly float[] AnaBlur = { 0.004f, 0.008f, 0.012f, 0.018f };
        const float AnaFringe = 0.3f;

        Camera cam;
        Material mat;
        EdgePass pass;
        ParticleSystem glints, streaks;
        bool glintsOn;
        Vector4 edge;
        static readonly int EdgeId = Shader.PropertyToID("_LensEdge"), AspectId = Shader.PropertyToID("_LensAspect");

        public void Build(Camera camera, Material dot)
        {
            cam = camera;
            if (dot != null)
            {
                // the glint: a white flash brighter than the snow (HDR), so the bloom gives it a halo
                glints = MakeFlashes("SnowGlints", dot, new Color(8f, 8.3f, 9.5f, 1f));
                // the streak: the same flash stretched sideways, in the blue of coated anamorphic glass. Deep blue
                // with little red or green: sunlit snow is so bright that a paler blue tonemaps to plain white on it
                streaks = MakeFlashes("GlintStreaks", dot, new Color(0.15f, 0.55f, 5f, 1f));
                streaks.GetComponent<ParticleSystemRenderer>().sharedMaterial.SetTexture("_BaseMap", StreakTexture());
            }
            var sh = Resources.Load<Shader>("LensEdge");
            if (sh != null && sh.isSupported)
            {
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                pass = new EdgePass(mat);
            }
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (mat != null) Destroy(mat);
        }

        /// <summary>Set the lens: `warp` is the LENS row's barrel distortion, `level` its index (0..3), `mode` the LENS TYPE.</summary>
        public void Apply(LensDistortion lens, float warp, int level, int mode)
        {
            level = Mathf.Clamp(level, 0, 3);
            bool ana = mode == 1;
            if (lens != null)
            {
                // anamorphic: the warp mostly sideways, so the edges squeeze in horizontally, an oval frame
                lens.intensity.Override(ana ? Mathf.Min(warp, -0.12f) : warp);
                lens.xMultiplier.Override(1f);
                lens.yMultiplier.Override(ana ? 0.3f : 1f);
                lens.scale.Override(1f + 0.12f * (level / 3f));
            }
            if (glints != null && ana != glintsOn)
            {
                foreach (var ps in new[] { glints, streaks })
                    if (ana) ps.Play(); else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            glintsOn = ana && glints != null;
            edge = ana ? new Vector4(AnaBlur[level], AnaFringe, 0.045f, 1f)
                       : new Vector4(SphBlur[level], SphFringe[level], 0f, 0f);
        }

        // ---------------------------------------------------------------- snow glints (anamorphic)

        const int GlintCount = 9;

        ParticleSystem MakeFlashes(string name, Material dot, Color hdr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform.parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startSpeed = 0f;
            main.startSize3D = true;   // the streak is long and thin; sizes are set per flash in LateUpdate
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // each glint stays on its patch of snow
            main.maxParticles = GlintCount * 2;
            main.playOnAwake = false;
            var em = ps.emission; em.rateOverTime = 0f;   // placed by hand in LateUpdate, on the snow surface
            var shape = ps.shape; shape.enabled = false;
            // a twinkle: flashes up and dies away
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f)));
            var pr = go.GetComponent<ParticleSystemRenderer>();
            var m = new Material(dot) { name = name };
            m.SetColor("_BaseColor", hdr);
            pr.material = m;
            pr.renderMode = ParticleSystemRenderMode.Billboard;   // faces the lens, so the long axis lies across the screen
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            return ps;
        }

        // a streak: a sharp thin line, brightest in the middle and tapering away to each end
        static Texture2D StreakTexture()
        {
            const int W = 128, H = 16;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Streak" };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / W * 2f - 1f), v = ((y + 0.5f) / H * 2f - 1f) / 0.35f;
                    float a = Mathf.Exp(-v * v) * Mathf.Pow(1f - u, 2.2f);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            t.SetPixels32(px); t.Apply(false, true);
            return t;
        }

        void LateUpdate()
        {
            if (!glintsOn || cam == null) return;
            // keep about GlintCount alive, a few new ones a frame, scattered over the slope ahead
            int want = Mathf.Min(3, GlintCount - glints.particleCount);
            if (want <= 0) return;
            Vector3 cp = cam.transform.position, fwd = cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < want; i++)
            {
                float d = Mathf.Lerp(3f, 30f, Random.value * Random.value);   // more of them near the lens
                Vector3 p = cp + fwd * d + right * ((Random.value * 2f - 1f) * d * 0.6f);
                p.y = Slope.DrawnHeight(p.x, p.z) + 0.05f;
                // drawn half a metre toward the lens: same place on screen, but the long streak never cuts into the snow
                p += (cp - p).normalized * 0.5f;
                ep.position = p;
                ep.startLifetime = Random.Range(0.15f, 0.45f);
                // sizes grow with distance, so a glint is a few pixels and a streak most of the screen wide at any range
                float k = Random.Range(0.6f, 1.3f);
                ep.startSize3D = new Vector3(0.016f, 0.016f, 1f) * d * k;
                glints.Emit(ep, 1);
                ep.startSize3D = new Vector3(0.75f * d * k, 0.016f * d, 1f);
                streaks.Emit(ep, 1);
            }
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || pass == null) return;
            if (edge.x <= 0f && edge.z <= 0f) return;   // a clean STANDARD lens: no pass at all
            mat.SetVector(EdgeId, edge);
            mat.SetFloat(AspectId, c.aspect);
            var data = c.GetUniversalAdditionalCameraData();
            if (data != null && data.scriptableRenderer != null) data.scriptableRenderer.EnqueuePass(pass);
        }

        /// <summary>Copies the finished frame through the edge shader into a new colour target, which becomes the camera colour.</summary>
        class EdgePass : ScriptableRenderPass
        {
            readonly Material mat;

            public EdgePass(Material m)
            {
                mat = m;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                var src = res.activeColorTexture;
                var desc = rg.GetTextureDesc(src);
                desc.name = "LensEdge";
                desc.clearBuffer = false;
                var dst = rg.CreateTexture(desc);
                rg.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(src, dst, mat, 0), "Lens Edge");
                res.cameraColor = dst;
            }
        }
    }
}
