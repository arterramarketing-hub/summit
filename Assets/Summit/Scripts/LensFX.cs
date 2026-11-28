using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Summit
{
    /// <summary>
    /// What the lens does besides its field of view, from the settings' LENS row. The lens is spherical (there was an
    /// anamorphic choice; it is gone). One full-screen pass after post-processing (Resources/LensEdge.shader): at most
    /// six texture reads toward the edges, a single read in the middle. The LENS row sets how strong it all is.
    ///
    /// The barrel warp (as the LENS row always had; drawn here, round on the screen, since URP's own one
    /// bowed the bottom of a tall frame far harder than its middle), plus colour fringing and softness that run
    /// outward from the centre and grow toward every edge of the screen. There is a little even at STANDARD,
    /// a clear fringe down both sides at the default WIDE, and the skate-video "death lens" at FISHEYE.
    ///
    /// HALATION (the LOOK tab): the film glow, red-orange, bleeding round bright highlights. Built at a quarter
    /// of the screen size in three tiny passes (bright parts, blur across, blur down) and screened on in the same
    /// full-screen pass, one more read per pixel. It works on the finished, graded picture, so it follows the
    /// colour filter; as it gets dark it catches dimmer lights and glows harder, so lamps and the headlamp
    /// bloom red at night and the low sun does in the evening.
    ///
    /// EDGE STREAKS (first person only, set each frame by ChaseCamera): speed smears the edges of the frame into
    /// streaks that run out from the point you are heading for, and leaves the middle sharp. Done in the same
    /// full-screen pass with five more reads, and only on the pixels that streak; nothing is read when `Blur` is 0.
    /// </summary>
    public class LensFX : MonoBehaviour
    {
        // per LENS level (STANDARD, WIDE, WIDER, FISHEYE), as a fraction of the screen width: at a side edge each
        // colour moves by half this, so the default 0.008 is about 4 px per colour at the iPhone's render size
        // (8 px between red and blue), 0.02 at FISHEYE about 10 px; the corners get half as much again
        static readonly float[] SphFringe = { 0.003f, 0.008f, 0.012f, 0.02f };
        static readonly float[] SphSmear = { 0.003f, 0.006f, 0.01f, 0.016f };   // kept small: two reads per colour blur up to a few px, beyond that they double

        /// <summary>The spherical barrel per unit of the LENS row's warp (WIDE -0.28, WIDER -0.42, FISHEYE -0.6).</summary>
        public static float Barrel = 0.4f;

        public static readonly string[] HalationName = { "OFF", "SUBTLE", "STRONG" };
        static readonly float[] HaloStrength = { 0f, 0.5f, 1.05f };
        static readonly float[] HaloSpread = { 1f, 1.1f, 1.7f };
        static readonly Color HaloTint = new Color(1f, 0.34f, 0.1f);   // the red layer of colour film catches it

        Camera cam;
        int halation;
        /// <summary>
        /// How much of the chosen lens to use (1 on the slope). The summit camp turns it down: its camera is close to
        /// the rider among straight walls, rails and timbers, where the same barrel warp and edge softness that the
        /// open slope hides bend every line, so it eases to about a third there and back to 1 when you drop in.
        /// </summary>
        public float Strength = 1f;
        /// <summary>Edge streaks: their length at the frame edge as a fraction of the screen (0: off, and no cost).</summary>
        public float Blur;
        /// <summary>The point the streaks run out from: where you are heading, in the view (0..1 across and up).</summary>
        public Vector2 BlurCentre = new Vector2(0.5f, 0.5f);
        /// <summary>
        /// Which way up the full-screen pass sees the picture: 1 if its v runs up the screen (as Unity's blits do),
        /// -1 if the streaks turn out to be centred too low and the rider's legs are smeared instead of the sky's edge.
        /// </summary>
        public static float BlurUp = 1f;
        LensDistortion lensRef;
        float warpBase, scaleBase = 1f, shown = -1f;
        DayNight dayNight; bool lookedForDayNight;
        Material mat;
        EdgePass pass;
        Vector2 edge;   // the fringe and the softness at the edge
        float barrel;
        static readonly int EdgeId = Shader.PropertyToID("_LensEdge"), BarrelId = Shader.PropertyToID("_LensBarrel"),
                            AspectId = Shader.PropertyToID("_LensAspect"), HaloId = Shader.PropertyToID("_Halo"),
                            HaloTexId = Shader.PropertyToID("_HaloTex"), HaloSrcId = Shader.PropertyToID("_HaloSrc"),
                            HaloBlurId = Shader.PropertyToID("_HaloBlur"), BlurId = Shader.PropertyToID("_LensBlur");

        public void Build(Camera camera)
        {
            cam = camera;
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

        /// <summary>Set the lens: `warp` is the LENS row's barrel distortion, `level` its index (0..3).</summary>
        public void Apply(LensDistortion lens, float warp, int level)
        {
            level = Mathf.Clamp(level, 0, 3);
            if (lens != null)
            {
                // URP's warp stays off: the shader draws the barrel (see Barrel)
                lensRef = lens;
                warpBase = 0f;
                scaleBase = 1f;
                shown = -1f;   // (set, at this strength, as the next frame is drawn)
                lens.xMultiplier.Override(1f);
                lens.yMultiplier.Override(1f);
                SetWarp();
            }
            edge = new Vector2(SphFringe[level], SphSmear[level]);
            barrel = Mathf.Max(0f, -warp) * Barrel;
        }

        void SetWarp()
        {
            if (lensRef == null || Mathf.Abs(shown - Strength) < 0.001f) return;
            shown = Strength;
            lensRef.intensity.Override(warpBase * Strength);
            lensRef.scale.Override(1f + (scaleBase - 1f) * Strength);
        }

        /// <summary>Halation: 0 OFF, 1 SUBTLE, 2 STRONG.</summary>
        public void SetHalation(int level) { halation = Mathf.Clamp(level, 0, HalationName.Length - 1); }

        /// <summary>
        /// Would the pass change the picture this frame? Not when the lens is turned all the way down, with no edge
        /// streaks and no halation: then it is not added at all, and neither is the intermediate texture it needs to
        /// read the frame from.
        /// </summary>
        bool Needed()
        {
            bool lens = Strength > 0.001f && (edge.x > 0f || edge.y > 0f || barrel > 0f);
            return lens || Blur > 0f || halation > 0;
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || pass == null) return;
            SetWarp();
            if (!Needed()) return;
            // the fringe, the softness and the barrel all scale with the strength
            mat.SetVector(EdgeId, new Vector4(edge.x * Strength, edge.y * Strength, 0f, 0f));
            mat.SetFloat(BarrelId, barrel * Strength);
            mat.SetFloat(AspectId, c.aspect);
            mat.SetVector(BlurId, new Vector4(Mathf.Max(0f, Blur), BlurCentre.x, BlurUp < 0f ? 1f - BlurCentre.y : BlurCentre.y, BlurUp < 0f ? -1f : 1f));
            float dark = 0f;
            if (!lookedForDayNight) { dayNight = FindFirstObjectByType<DayNight>(); lookedForDayNight = dayNight != null; }
            if (dayNight != null) dark = dayNight.Darkness;
            // at night the brightest things are far dimmer than sunlit snow: catch them lower, and glow a little harder
            float k = HaloStrength[halation] * (1f + 0.5f * dark);
            mat.SetVector(HaloId, new Vector4(HaloTint.r * k, HaloTint.g * k, HaloTint.b * k, 0f));
            pass.haloSource = new Vector2(Mathf.Lerp(0.72f, 0.42f, dark), Mathf.Lerp(0.98f, 0.8f, dark));
            pass.haloSpread = halation > 0 ? HaloSpread[halation] : 0f;
            var data = c.GetUniversalAdditionalCameraData();
            if (data != null && data.scriptableRenderer != null) data.scriptableRenderer.EnqueuePass(pass);
        }

        /// <summary>
        /// Copies the finished frame through the edge shader into a new colour target, which becomes the camera colour.
        /// (It must read the frame, so it asks for an intermediate texture; on the phone the 0.8 render scale has one
        /// anyway, for the final upscale, so that costs no extra copy there.)
        /// </summary>
        class EdgePass : ScriptableRenderPass
        {
            readonly Material mat;
            public Vector2 haloSource;   // the brightness where halation starts, and where it is full
            public float haloSpread;     // 0: no halation this frame

            class CompositeData { public TextureHandle src, halo; public Material mat; }

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

                // halation, at a quarter size: the bright parts, then blurred across and down
                var halo = TextureHandle.nullHandle;
                if (haloSpread > 0f)
                {
                    var q = desc;
                    q.sizeMode = TextureSizeMode.Explicit;
                    q.width = Mathf.Max(1, desc.width / 4); q.height = Mathf.Max(1, desc.height / 4);
                    q.msaaSamples = MSAASamples.None; q.filterMode = FilterMode.Bilinear;
                    q.name = "HaloA"; var a = rg.CreateTexture(q);
                    q.name = "HaloB"; var b = rg.CreateTexture(q);
                    mat.SetVector(HaloSrcId, new Vector4(1f / desc.width, 1f / desc.height, haloSource.x, haloSource.y));
                    mat.SetVector(HaloBlurId, new Vector4(haloSpread / q.width, haloSpread / q.height, 0f, 0f));
                    rg.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(src, a, mat, 1), "Halation Source");
                    rg.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(a, b, mat, 2), "Halation Blur X");
                    rg.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(b, a, mat, 3), "Halation Blur Y");
                    halo = a;
                }

                using (var builder = rg.AddRasterRenderPass<CompositeData>("Lens Edge", out var data))
                {
                    data.src = src; data.halo = halo; data.mat = mat;
                    builder.UseTexture(src);
                    if (halo.IsValid()) builder.UseTexture(halo);
                    builder.SetRenderAttachment(dst, 0);
                    builder.AllowGlobalStateModification(true);   // the halation texture goes in as a global
                    builder.SetRenderFunc((CompositeData d, RasterGraphContext ctx) =>
                    {
                        if (d.halo.IsValid()) ctx.cmd.SetGlobalTexture(HaloTexId, d.halo);
                        Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1f, 1f, 0f, 0f), d.mat, 0);
                    });
                }
                res.cameraColor = dst;
            }
        }
    }
}
