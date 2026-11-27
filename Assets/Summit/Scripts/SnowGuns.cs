using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The snow guns down the lower mountain (PisteKit) making snow in the morning, as resorts do before and as the
    /// lifts open: from the last of the night (TimeOfDay's clock from 3.8) through the morning, easing off as the day
    /// comes on; off in the day and the evening.
    ///
    /// Only the guns nearest the camera run a plume (a pool of three, handed from gun to gun as you ride down): each
    /// a cone of mist and spray blown from the nozzle that slows in the air and drifts away downwind (TreeWind's
    /// direction and strength), swelling and thinning as it goes, and a fall of fine flakes out of it that settles
    /// onto the snow round the gun. Both are drawn with Resources/SnowSpray.shader (soft round puffs, lit by sky, sun
    /// and floodlights, backlit glow: no new shader, no new variants), size-capped so a plume in front of the lens
    /// never fills the screen.
    ///
    /// The sound: the roar and hiss of a running gun (Resources/Audio/snow_gun, a loop), on two 3D sources that follow
    /// the two nearest running guns, rising as you pass. Without the clip, the guns are silent.
    /// </summary>
    public class SnowGuns : MonoBehaviour
    {
        public const int Plumes = 3, Voices = 2;
        public const float Reach = 320f;              // m from the camera: guns further than this don't run a plume
        public const int JetMax = 60, MistMax = 150, FlakeMax = 280;   // (3 plumes: 1,470 particles at most)

        class Plume
        {
            public int gun = -1;                      // the item it is on (-1: none)
            public Transform root;
            public ParticleSystem jet, mist, flakes;
            public ParticleSystem.EmissionModule jetEm, mistEm, flakeEm;
        }

        readonly Plume[] plumes = new Plume[Plumes];
        readonly AudioSource[] voices = new AudioSource[Voices];
        readonly List<int> near = new List<int>();
        Material mistMat, flakeMat;
        Camera cam;
        bool soundTried;
        AudioClip clip;
        /// <summary>How hard the guns run now, 0..1 (the test tools read it).</summary>
        public float Level { get; private set; }
        public static SnowGuns Instance { get; private set; }
        /// <summary>The test tools force it on (1) or off (0) whatever the clock says (-1: the clock).</summary>
        public static float TestOn = -1f;

        public static SnowGuns Build(Transform parent)
        {
            var go = new GameObject("SnowGuns");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<SnowGuns>();
            g.Setup();
            Instance = g;
            return g;
        }

        /// <summary>
        /// How hard the guns run at this time of day (TimeOfDay.Clock, 0..4: morning, day, evening, night): full from
        /// before dawn (3.85) through the morning, easing off from 0.8 to gone by 1.05.
        /// </summary>
        public static float MorningLevel(float clock)
        {
            float c = Mathf.Repeat(clock, 4f);
            if (c >= 3.6f) return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.6f, 3.85f, c));
            if (c < 0.8f) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1.05f, c));
        }

        static Material SprayMaterial(string name, Color c, float core, float sparkle, float grain)
        {
            var sh = Resources.Load<Shader>("SnowSpray");
            if (sh == null || !sh.isSupported) return null;
            var m = new Material(sh) { name = name, renderQueue = 3000 };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Core", core);
            m.SetFloat("_Sparkle", sparkle);
            m.SetFloat("_Grain", grain);
            return m;
        }

        void Setup()
        {
            // the mist: soft and only a shade off white; the flakes: fine, a little sparkle
            mistMat = SprayMaterial("SnowGunMist", new Color(0.86f, 0.88f, 0.92f, 1f), 0.8f, 0.05f, 0.35f);
            flakeMat = SprayMaterial("SnowGunFlakes", new Color(0.9f, 0.92f, 0.96f, 1f), 2.2f, 0.7f, 0.6f);
            for (int i = 0; i < Plumes; i++) plumes[i] = MakePlume(i);
            for (int i = 0; i < Voices; i++)
            {
                var g = new GameObject("SnowGunSound" + i);
                g.transform.SetParent(transform, false);
                var s = g.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = true; s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 7f; s.maxDistance = 170f;
                s.dopplerLevel = 0.3f; s.volume = 0f;
                voices[i] = s;
            }
        }

        Plume MakePlume(int i)
        {
            var p = new Plume { root = new GameObject("SnowGunPlume" + i).transform };
            p.root.SetParent(transform, false);

            // the jet: a tight, bright cone straight out of the nozzle, a few metres long, that the plume grows out of
            p.jet = Part(p.root, "Jet", mistMat, JetMax);
            var j = p.jet.main;
            j.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            j.startSpeed = new ParticleSystem.MinMaxCurve(19f, 24f);
            j.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.42f);
            j.startColor = new Color(1f, 1f, 1f, 0.95f);
            var jsh = p.jet.shape; jsh.shapeType = ParticleSystemShapeType.Cone; jsh.angle = 3.5f; jsh.radius = 0.22f;
            var jsz = p.jet.sizeOverLifetime; jsz.enabled = true; jsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 3.2f));
            var jcol = p.jet.colorOverLifetime; jcol.enabled = true;
            var jg = new Gradient();
            jg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0.45f, 1f) });
            jcol.color = jg;
            var jpr = p.jet.GetComponent<ParticleSystemRenderer>();
            jpr.renderMode = ParticleSystemRenderMode.Stretch; jpr.velocityScale = 0.06f; jpr.lengthScale = 1.6f;
            p.jetEm = p.jet.emission;

            // the mist: out of the end of the jet, slowing and spreading into streaks that drift and wander downwind
            // (stretched along the way each goes, and turned this way and that by turbulence), heavy with water, so it
            // comes down onto the snow downwind of the gun rather than hanging as a ball
            p.mist = Part(p.root, "Mist", mistMat, MistMax);
            var m = p.mist.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 4.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(12f, 19f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.75f), new Color(0.94f, 0.95f, 0.98f, 0.5f));
            m.gravityModifier = 0.22f;
            var sh = p.mist.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.25f;
            var lv = p.mist.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 0.55f; lv.multiplyDragByParticleSize = false; lv.limit = 40f;
            var sz = p.mist.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.2f, 1.3f), new Keyframe(1f, 2.6f)));
            var col = p.mist.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.6f, 0.25f), new GradientAlphaKey(0.28f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var nz = p.mist.noise; nz.enabled = true; nz.strength = 0.9f; nz.frequency = 0.3f; nz.scrollSpeed = 0.4f; nz.octaveCount = 1; nz.quality = ParticleSystemNoiseQuality.Low; nz.damping = true;
            var mpr = p.mist.GetComponent<ParticleSystemRenderer>();
            mpr.renderMode = ParticleSystemRenderMode.Stretch; mpr.velocityScale = 0.05f; mpr.lengthScale = 1.35f;
            p.mistEm = p.mist.emission;

            // the flakes: thrown out with the mist, then falling out of it onto the snow round the gun
            p.flakes = Part(p.root, "Flakes", flakeMat, FlakeMax);
            var f = p.flakes.main;
            f.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
            f.startSpeed = new ParticleSystem.MinMaxCurve(9f, 16f);
            f.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            f.startColor = new Color(1f, 1f, 1f, 0.85f);
            f.gravityModifier = 0.3f;   // (out of the plume and down onto the snow downwind)
            var fsh = p.flakes.shape; fsh.shapeType = ParticleSystemShapeType.Cone; fsh.angle = 12f; fsh.radius = 0.3f;
            var flv = p.flakes.limitVelocityOverLifetime; flv.enabled = true; flv.drag = 1.3f; flv.multiplyDragByParticleSize = false; flv.limit = 40f;
            var fcol = p.flakes.colorOverLifetime; fcol.enabled = true;
            var fg = new Gradient();
            fg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fcol.color = fg;
            p.flakeEm = p.flakes.emission;
            return p;
        }

        static ParticleSystem Part(Transform parent, string name, Material mat, int max)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = max;
            m.playOnAwake = false;
            var em = ps.emission; em.rateOverTime = 0f;
            // the wind carries it all away downhill-ish (set each frame)
            var fo = ps.forceOverLifetime; fo.enabled = true; fo.space = ParticleSystemSimulationSpace.World;
            var pr = g.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            if (mat != null) pr.SetActiveVertexStreams(RiderBody.Streams);
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.sortMode = ParticleSystemSortMode.None;
            pr.maxParticleSize = 0.35f;   // (a plume right at the lens never fills the screen)
            ps.Play();
            return ps;
        }

        void Update() => Step(Time.deltaTime);

        /// <summary>A frame: which guns run a plume, how hard, where the wind takes it, and the sound (the test tools call it themselves).</summary>
        public void Step(float dt)
        {
            if (cam == null) cam = Camera.main;
            Level = TestOn >= 0f ? TestOn : MorningLevel(TimeOfDay.Clock);
            var items = PisteKit.Items;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;

            // the nearest running guns to the camera
            near.Clear();
            if (Level > 0.01f)
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].kind != PisteKit.Kind.Gun || items[i].size <= 0f) continue;
                    float d = (items[i].foot - eye).sqrMagnitude;
                    if (d > Reach * Reach) continue;
                    int at = near.Count;
                    while (at > 0 && (items[near[at - 1]].foot - eye).sqrMagnitude > d) at--;
                    near.Insert(at, i);
                    if (near.Count > Plumes) near.RemoveAt(near.Count - 1);
                }

            // a plume keeps its gun while that gun is still among the nearest; the free ones take the rest
            for (int k = 0; k < Plumes; k++) if (plumes[k].gun >= 0 && !near.Contains(plumes[k].gun)) plumes[k].gun = -1;
            foreach (int gun in near)
            {
                bool held = false;
                for (int k = 0; k < Plumes; k++) if (plumes[k].gun == gun) held = true;
                if (held) continue;
                for (int k = 0; k < Plumes; k++) if (plumes[k].gun < 0) { plumes[k].gun = gun; break; }
            }

            // the wind: TreeWind's direction, harder in the weather's wind
            Vector3 wind = new Vector3(TreeWind.Dir.x, 0f, TreeWind.Dir.y) * (0.8f + 1.4f * TreeWind.Strength);
            for (int k = 0; k < Plumes; k++)
            {
                var p = plumes[k];
                if (p.gun < 0) { p.jetEm.rateOverTime = 0f; p.mistEm.rateOverTime = 0f; p.flakeEm.rateOverTime = 0f; continue; }
                var it = items[p.gun];
                PisteKit.Nozzle(it, out Vector3 at, out Vector3 axis);
                p.root.SetPositionAndRotation(at, Quaternion.LookRotation(axis, Vector3.up));
                // the nearest runs hardest (a third gun far off, a little less)
                float share = k == 0 ? 1f : 0.85f;
                p.jetEm.rateOverTime = 75f * Level * share;
                p.mistEm.rateOverTime = 42f * Level * share;
                p.flakeEm.rateOverTime = 60f * Level * share;
                var fo = p.mist.forceOverLifetime; fo.x = wind.x; fo.y = 0f; fo.z = wind.z;
                var ff = p.flakes.forceOverLifetime; ff.x = wind.x * 0.8f; ff.y = 0f; ff.z = wind.z * 0.8f;
            }

            Sound(items, eye, dt);
        }

        void Sound(PisteKit.Item[] items, Vector3 eye, float dt)
        {
            if (!soundTried) { soundTried = true; clip = Resources.Load<AudioClip>("Audio/snow_gun"); if (clip != null) foreach (var v in voices) { v.clip = clip; v.time = clip.length * Random.value * 0.9f; v.Play(); } }
            if (clip == null) return;
            for (int i = 0; i < Voices; i++)
            {
                var v = voices[i];
                bool on = i < near.Count && Level > 0.01f;
                if (on)
                {
                    PisteKit.Nozzle(items[near[i]], out Vector3 at, out _);
                    v.transform.position = at;
                    v.pitch = i == 0 ? 1f : 0.96f;   // (two guns never quite in step)
                }
                float want = on ? 0.6f * Level : 0f;
                v.volume = dt <= 0f ? want : Mathf.MoveTowards(v.volume, want, dt * 0.5f);
            }
        }

        /// <summary>The gun's sound: its clip's length (0: none), and how many of the voices are playing and how loud (the test tools log it).</summary>
        public string SoundState { get { int n = 0; float v = 0f; foreach (var s in voices) if (s.isPlaying) { n++; v = Mathf.Max(v, s.volume); } return $"clip {(clip != null ? clip.name + " " + clip.length.ToString("0.0") + " s, " + clip.channels + " ch" : "none")}, {n} voices playing, loudest at {v:0.00}"; } }

        public void ClearPlumes() { foreach (var p in plumes) { p.jet.Clear(); p.mist.Clear(); p.flakes.Clear(); p.gun = -1; } }

        /// <summary>Particles alive in all the plumes (the cost log).</summary>
        public int Alive { get { int n = 0; foreach (var p in plumes) n += p.jet.particleCount + p.mist.particleCount + p.flakes.particleCount; return n; } }
    }
}
