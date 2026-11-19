using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Summit
{
    /// <summary>
    /// What the lens does besides its field of view, from the pause menu's LENS and LENS TYPE rows.
    /// Both lens types draw one full-screen pass after post-processing (Resources/LensEdge.shader): at most six
    /// texture reads toward the edges, a single read in the middle. The LENS row sets how strong it all is.
    ///
    /// SPHERICAL: URP's barrel warp (as the LENS row always had), plus colour fringing and softness that run
    /// outward from the centre and grow toward every edge of the screen. There is a little even at STANDARD,
    /// a clear fringe down both sides at the default WIDE, and the skate-video "death lens" at FISHEYE.
    ///
    /// ANAMORPHIC: the look of a desqueezed anamorphic picture rather than its flares. The middle is stretched
    /// wide and the sides squeezed (the "mumps"), verticals near the sides bow, the softness and the colour
    /// fringe run sideways only, a wide oval vignette closes in from top and bottom, and thin black bars give
    /// a widescreen frame. URP's own barrel warp is off here: the shader does the anamorphic warp instead.
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
        public static readonly string[] ModeName = { "SPHERICAL", "ANAMORPHIC" };

        // per LENS level (STANDARD, WIDE, WIDER, FISHEYE), as a fraction of the screen width: at a side edge each
        // colour moves by half this, so the default 0.008 is about 4 px per colour at the iPhone's render size
        // (8 px between red and blue), 0.02 at FISHEYE about 10 px; the corners get half as much again
        static readonly float[] SphFringe = { 0.003f, 0.008f, 0.012f, 0.02f };
        static readonly float[] SphSmear = { 0.003f, 0.006f, 0.01f, 0.016f };   // kept small: two reads per colour blur up to a few px, beyond that they double
        // anamorphic: the same scale, but sideways only
        static readonly float[] AnaFringe = { 0.003f, 0.007f, 0.01f, 0.016f };
        static readonly float[] AnaSmear = { 0.004f, 0.008f, 0.012f, 0.018f };
        // squeeze of the sides (and stretch of the middle); the verticals bow by half as much
        static readonly float[] AnaSqueeze = { 0.06f, 0.1f, 0.14f, 0.2f };

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
        Vector4 edge, ana;
        static readonly int EdgeId = Shader.PropertyToID("_LensEdge"), AnaId = Shader.PropertyToID("_LensAna"),
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

        /// <summary>Set the lens: `warp` is the LENS row's barrel distortion, `level` its index (0..3), `mode` the LENS TYPE.</summary>
        public void Apply(LensDistortion lens, float warp, int level, int mode)
        {
            level = Mathf.Clamp(level, 0, 3);
            bool anamorphic = mode == 1;
            if (lens != null)
            {
                lensRef = lens;
                warpBase = anamorphic ? 0f : warp;
                scaleBase = anamorphic ? 1f : 1f + 0.12f * (level / 3f);
                shown = -1f;   // (set, at this strength, as the next frame is drawn)
                lens.xMultiplier.Override(1f);
                lens.yMultiplier.Override(1f);
                SetWarp();
            }
            if (anamorphic)
            {
                edge = new Vector4(AnaFringe[level], AnaSmear[level], 0.035f, 1f);
                ana = new Vector4(AnaSqueeze[level], AnaSqueeze[level] * 0.5f, 0.45f, 0f);
            }
            else
            {
                edge = new Vector4(SphFringe[level], SphSmear[level], 0f, 0f);
                ana = Vector4.zero;
            }
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

        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || pass == null) return;
            SetWarp();
            // the fringe and softness, and the anamorphic squeeze, scale with the strength; its vignette and bars don't
            mat.SetVector(EdgeId, new Vector4(edge.x * Strength, edge.y * Strength, edge.z, edge.w));
            mat.SetVector(AnaId, new Vector4(ana.x * Strength, ana.y * Strength, ana.z, ana.w));
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

        /// <summary>Copies the finished frame through the edge shader into a new colour target, which becomes the camera colour.</summary>
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
