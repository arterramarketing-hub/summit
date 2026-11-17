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

        Camera cam;
        Material mat;
        EdgePass pass;
        Vector4 edge, ana;
        static readonly int EdgeId = Shader.PropertyToID("_LensEdge"), AnaId = Shader.PropertyToID("_LensAna"),
                            AspectId = Shader.PropertyToID("_LensAspect");

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
                lens.intensity.Override(anamorphic ? 0f : warp);
                lens.xMultiplier.Override(1f);
                lens.yMultiplier.Override(1f);
                lens.scale.Override(anamorphic ? 1f : 1f + 0.12f * (level / 3f));
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

        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || pass == null) return;
            mat.SetVector(EdgeId, edge);
            mat.SetVector(AnaId, ana);
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
