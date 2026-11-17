using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Summit
{
    /// <summary>
    /// The colour filters on the pause menu. Each one is a full grade (white balance, split tone,
    /// lift/gamma/gain, curves, grain, vignette, bloom) driven through the scene's Volume, and a strength
    /// that blends it against NATURAL, the game's own grade. NATURAL at any strength, or any filter at 0%,
    /// is the look the game had before filters existed.
    ///
    /// Rules every filter keeps, from how snow is shot and graded:
    ///  - sunlit snow stays near-neutral white and shade goes blue; a filter tints shadows and highlights
    ///    apart rather than pushing the whole frame, or the slope turns cyan (or orange);
    ///  - the brightest snow is desaturated by the luminance-vs-saturation curve and rolled off by the master
    ///    curve, so a warm or vivid filter never clips the snow to a flat colour;
    ///  - the tonemapper stays Neutral for every filter, so the grade carries the look and strengths blend smoothly.
    /// All of this is baked into URP's grading LUT, so the filters cost nothing per pixel; only grain and bloom do.
    /// </summary>
    public static class LookGrade
    {
        public static readonly string[] Names = { "NATURAL", "CINEMATIC", "BLUEBIRD", "GOLDEN HOUR", "FILM", "NOIR" };

        struct Grade
        {
            public float exposure, contrast, saturation, temperature, tint;
            public Vector4 shadows, midtones, highlights;   // split tone: colour multipliers per tonal range
            public Vector4 lift, gamma, gain;
            public float fade;      // blacks lifted by the master curve, like faded print stock
            public float rolloff;   // the top of the master curve pulled down: soft highlights on snow
            public float hiDesat;   // how much the brightest tones lose their colour
            public float grain;
            public FilmGrainLookup grainType;
            public float vignette;
            public float bloom, bloomThreshold;
            public Color bloomTint;
        }

        static readonly Vector4 One = new Vector4(1f, 1f, 1f, 0f);

        // The game's own grade (what SummitGame.SetUpWorld set before filters): the 0% end of every blend.
        static readonly Grade Natural = new Grade
        {
            exposure = 0.1f, contrast = 14f, saturation = 10f,
            shadows = One, midtones = One, highlights = One, lift = One, gamma = One, gain = One,
            grainType = FilmGrainLookup.Thin1, vignette = 0.2f, bloom = 0.6f, bloomThreshold = 1f, bloomTint = Color.white,
        };

        static readonly Grade[] Presets =
        {
            Natural,
            // CINEMATIC: the film-trailer look done safely on snow. Cool blue-violet shade and warm amber
            // light, kept apart; more contrast, a little less colour, highlights rolled off and drained so the
            // snow stays white instead of going orange. A touch of grain and a darker frame edge.
            new Grade
            {
                exposure = 0.1f, contrast = 22f, saturation = 2f, temperature = 4f, tint = -2f,
                shadows = new Vector4(0.92f, 0.98f, 1.10f, 0f), midtones = new Vector4(1.02f, 1f, 0.98f, 0f), highlights = new Vector4(1.07f, 1.0f, 0.90f, 0f),
                lift = new Vector4(0.95f, 0.99f, 1.07f, -0.01f), gamma = One, gain = new Vector4(1.02f, 1f, 0.97f, -0.03f),
                rolloff = 0.06f, hiDesat = 0.4f, grain = 0.14f, grainType = FilmGrainLookup.Thin2,
                vignette = 0.3f, bloom = 0.75f, bloomThreshold = 0.95f, bloomTint = new Color(1f, 0.94f, 0.86f),
            },
            // BLUEBIRD: a cloudless powder day. Rich sky, jackets and trees, a slightly cool balance and crisp
            // contrast, but the brightest snow drained of colour so the extra saturation never tints it.
            new Grade
            {
                exposure = 0.08f, contrast = 20f, saturation = 34f, temperature = -3f, tint = 0f,
                shadows = new Vector4(0.96f, 0.99f, 1.06f, 0f), midtones = One, highlights = One,
                lift = One, gamma = One, gain = new Vector4(0.99f, 1f, 1.02f, 0f),
                rolloff = 0.03f, hiDesat = 0.65f, grainType = FilmGrainLookup.Thin1,
                vignette = 0.12f, bloom = 0.55f, bloomThreshold = 1f, bloomTint = Color.white,
            },
            // GOLDEN HOUR: low sun and alpenglow. Strongly warm balance, gold highlights over magenta-violet
            // shade (the complementary pair that suits snow better than teal-orange), softer contrast,
            // a warm, wider bloom.
            new Grade
            {
                exposure = 0.06f, contrast = 8f, saturation = 16f, temperature = 24f, tint = 4f,
                shadows = new Vector4(1.0f, 0.93f, 1.07f, 0f), midtones = new Vector4(1.03f, 0.99f, 0.96f, 0f), highlights = new Vector4(1.10f, 1.0f, 0.82f, 0f),
                lift = new Vector4(1.03f, 0.96f, 1.06f, 0f), gamma = One, gain = new Vector4(1.04f, 1f, 0.92f, -0.01f),
                rolloff = 0.06f, hiDesat = 0.25f, grain = 0.06f, grainType = FilmGrainLookup.Thin2,
                vignette = 0.3f, bloom = 1.0f, bloomThreshold = 0.88f, bloomTint = new Color(1f, 0.84f, 0.64f),
            },
            // FILM: an old ski movie on print stock. Lifted, slightly green-teal blacks, cream highlights, low
            // contrast, muted colour, soft highlights and heavy grain.
            new Grade
            {
                exposure = 0.06f, contrast = 0f, saturation = -14f, temperature = 10f, tint = 4f,
                shadows = new Vector4(0.92f, 1.03f, 1.02f, 0f), midtones = One, highlights = new Vector4(1.06f, 1.02f, 0.90f, 0f),
                lift = new Vector4(0.97f, 1.02f, 1.01f, 0.01f), gamma = One, gain = One,
                fade = 0.07f, rolloff = 0.08f, hiDesat = 0.2f, grain = 0.38f, grainType = FilmGrainLookup.Medium2,
                vignette = 0.34f, bloom = 0.8f, bloomThreshold = 0.92f, bloomTint = new Color(1f, 0.95f, 0.85f),
            },
            // NOIR: a cold, near-monochrome storm day. Most colour gone, steel-blue shade, hard contrast,
            // grain and a heavy frame edge; the rider's jacket is the only warmth left.
            new Grade
            {
                exposure = 0.08f, contrast = 32f, saturation = -72f, temperature = -18f, tint = -2f,
                shadows = new Vector4(0.86f, 0.95f, 1.16f, -0.02f), midtones = One, highlights = new Vector4(0.97f, 0.99f, 1.03f, 0f),
                lift = new Vector4(0.95f, 0.98f, 1.08f, -0.01f), gamma = One, gain = One,
                rolloff = 0.02f, grain = 0.26f, grainType = FilmGrainLookup.Medium1,
                vignette = 0.42f, bloom = 0.35f, bloomThreshold = 1.05f, bloomTint = new Color(0.9f, 0.95f, 1f),
            },
        };

        /// <summary>
        /// Grade the profile with filter `index` at strength `s` (0..1). Returns the base vignette, which the
        /// caller hands to Atmosphere (it widens the vignette with speed on top of this).
        /// </summary>
        public static float Apply(VolumeProfile p, int index, float s)
        {
            if (p == null) return Natural.vignette;
            Grade a = Natural, b = Presets[Mathf.Clamp(index, 0, Presets.Length - 1)];
            s = Mathf.Clamp01(s);
            float L(float x, float y) => Mathf.Lerp(x, y, s);
            Vector4 V(Vector4 x, Vector4 y) => Vector4.Lerp(x, y, s);

            var ca = Get<ColorAdjustments>(p);
            ca.postExposure.Override(L(a.exposure, b.exposure));
            ca.contrast.Override(L(a.contrast, b.contrast));
            ca.saturation.Override(L(a.saturation, b.saturation));

            var wb = Get<WhiteBalance>(p);
            wb.temperature.Override(L(a.temperature, b.temperature));
            wb.tint.Override(L(a.tint, b.tint));

            var smh = Get<ShadowsMidtonesHighlights>(p);
            smh.shadows.Override(V(a.shadows, b.shadows));
            smh.midtones.Override(V(a.midtones, b.midtones));
            smh.highlights.Override(V(a.highlights, b.highlights));

            var lgg = Get<LiftGammaGain>(p);
            lgg.lift.Override(V(a.lift, b.lift));
            lgg.gamma.Override(V(a.gamma, b.gamma));
            lgg.gain.Override(V(a.gain, b.gain));

            var cc = Get<ColorCurves>(p);
            cc.master.value?.Release(); cc.lumVsSat.value?.Release();   // each curve owns a small texture; drop the old ones
            cc.master.Override(Master(L(a.fade, b.fade), L(a.rolloff, b.rolloff)));
            cc.lumVsSat.Override(HighlightDesat(L(a.hiDesat, b.hiDesat)));

            var fg = Get<FilmGrain>(p);
            fg.type.Override(b.grain > 0f ? b.grainType : a.grainType);
            fg.intensity.Override(L(a.grain, b.grain));
            fg.response.Override(0.85f);   // grain mostly in the shade, not crawling over bright snow

            var bl = Get<Bloom>(p);
            bl.intensity.Override(L(a.bloom, b.bloom));
            bl.threshold.Override(L(a.bloomThreshold, b.bloomThreshold));
            bl.tint.Override(Color.Lerp(a.bloomTint, b.bloomTint, s));

            var vg = Get<Vignette>(p);
            float v = L(a.vignette, b.vignette);
            vg.intensity.Override(v);
            return v;
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet(out T c)) c = p.Add<T>(false);
            c.active = true;
            return c;
        }

        // the master curve: blacks lifted by `fade`, a straight middle, and a shoulder that brings white down by `roll`
        static TextureCurve Master(float fade, float roll)
        {
            float mx = 0.65f, my = fade + mx * (1f - fade), top = 1f - roll;
            float slope = 1f - fade, shoulder = Mathf.Max(0f, (top - my) / (1f - mx));
            var keys = new[]
            {
                new Keyframe(0f, fade, slope, slope),
                new Keyframe(mx, my, slope, slope),
                new Keyframe(1f, top, shoulder * 0.6f, shoulder * 0.6f),
            };
            return new TextureCurve(keys, 0f, false, new Vector2(0f, 1f));
        }

        // luminance-vs-saturation: 0.5 leaves colour alone; above mid-grey the brightest tones lose `amount` of it
        static TextureCurve HighlightDesat(float amount)
        {
            if (amount <= 0.001f) return new TextureCurve(new Keyframe[] { }, 0.5f, false, new Vector2(0f, 1f));
            var keys = new[]
            {
                new Keyframe(0f, 0.5f, 0f, 0f),
                new Keyframe(0.55f, 0.5f, 0f, 0f),
                new Keyframe(1f, 0.5f * (1f - amount), 0f, 0f),
            };
            return new TextureCurve(keys, 0.5f, false, new Vector2(0f, 1f));
        }
    }
}
