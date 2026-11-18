using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Blood, only when a crash is really hard: the body slamming into a tree, rock or sign, or into the snow after
    /// a big fall. A quick spray of small dark-red droplets and a few drips at the hit, a splat on whatever was hit
    /// (lying on the snow, or flat on the tree or rock, facing the way it was struck), and for a moment after a hit
    /// on the snow a smear wiped along where the body slides. Stylised and brief, never gory; light tumbles stay
    /// clean. The marks are a pool of 40 quads reused oldest first, cleared on a restart; the whole thing can be
    /// switched off in the pause menu.
    /// </summary>
    public class Blood : MonoBehaviour
    {
        /// <summary>Off: no spray, no marks (and the marks already left are wiped).</summary>
        public bool On { get; private set; } = true;
        // impact speeds into the surface (m/s) that draw blood: a tree or rock hurts sooner than the snow does.
        // Above the bone-break thresholds (3.2, 9) so a break alone stays clean: blood is for the worst of them
        public static float ObstacleHard = 7f, GroundHard = 9.5f;
        public static float SmearLength = 6f;   // metres of slide one hit smears before it runs dry
        const int Max = 40;

        ParticleSystem drops, drips;
        Material splatMat, smearMat;
        Mark[] marks;
        int next;
        float bleed;                      // 0..1: how much a hit on the snow is still smearing
        float smeared;                    // metres of smear laid since that hit
        Vector3 lastSmear; bool smearing;
        Vector3 lastSplat; float lastSplatTime = -9f;   // a body bouncing in the same spot marks it once
        static readonly Color Red = new Color(0.46f, 0.03f, 0.04f);

        class Mark { public GameObject go; public Mesh mesh; public MeshRenderer r; }

        public int Shown { get { int n = 0; if (marks != null) foreach (var m in marks) if (m.go.activeSelf) n++; return n; } }

        public void Build(Material soft)
        {
            drops = System("BloodDrops", soft, 220);
            var dm = drops.main;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.08f);
            dm.startColor = new ParticleSystem.MinMaxGradient(Red, new Color(0.62f, 0.05f, 0.06f));
            dm.gravityModifier = 1.1f;
            Fade(drops);
            drips = System("BloodDrips", soft, 40);
            var pm = drips.main;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            pm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            pm.startColor = new ParticleSystem.MinMaxGradient(Red);
            pm.gravityModifier = 0.35f;   // slow, heavy drops running off
            Fade(drips);

            splatMat = Decal(Resources.Load<Texture2D>("Blood/Splats"), soft);
            smearMat = Decal(Resources.Load<Texture2D>("Blood/Smear"), soft);
            var root = new GameObject("BloodMarks").transform;
            root.SetParent(transform, false);
            marks = new Mark[Max];
            for (int i = 0; i < Max; i++)
            {
                var go = new GameObject("Mark", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root, false);
                var mesh = new Mesh { name = "BloodMark" };
                mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                mesh.uv = new Vector2[4];
                mesh.colors = new Color[4];
                mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
                go.SetActive(false);
                marks[i] = new Mark { go = go, mesh = mesh, r = r };
            }
        }

        public void SetOn(bool on)
        {
            On = on;
            if (!on) Clear();
        }

        /// <summary>The forest, which knows where its trees' trunks and rocks' faces really are.</summary>
        public Forest forest;

        /// <summary>
        /// A part of the thrown body (or the rider) hit something: where, the surface's normal there (pointing out
        /// of it, toward the body), how fast into it (m/s), whether it was the snow, how the body was moving, and
        /// what it hit.
        /// </summary>
        public void Hit(Vector3 at, Vector3 normal, float into, bool ground, Vector3 bodyVel, Collider other = null)
        {
            if (!On || marks == null) return;
            float hard = ground ? GroundHard : ObstacleHard;
            if (into < hard) return;
            if (!ground && other == null) { Burst(at, normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up, Mathf.Clamp01((into - hard) / 10f), bodyVel); return; }   // nothing to mark (an animal)
            float k = Mathf.Clamp01((into - hard) / 10f);
            normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            Burst(at, normal, k, bodyVel);
            if (Time.time - lastSplatTime < 1f && (at - lastSplat).sqrMagnitude < 0.64f) return;
            lastSplat = at; lastSplatTime = Time.time;
            if (ground)
            {
                var g = OnSnow(at);
                Splat(g, Slope.Normal(at.x, at.z), 0.55f + 0.5f * k, 0.95f);
                // then it smears as the body slides on
                bleed = Mathf.Max(bleed, 0.6f + 0.4f * k);
                smeared = 0f; smearing = false;
            }
            else
            {
                // on the trunk or the rock face as drawn, not on the rough collider round it
                if (forest != null && forest.SurfaceOf(other, at + normal * 0.4f, out var sAt, out var sN)) { at = sAt; normal = sN; }
                Splat(at + normal * 0.02f, normal, 0.38f + 0.35f * k, 0.95f);
                // and a few drops on the snow at its foot
                var below = OnSnow(at - normal * 0.1f + normal * Random.Range(0.25f, 0.6f));
                Splat(below, Slope.Normal(below.x, below.z), 0.22f + 0.15f * k, 0.8f);
            }
        }

        /// <summary>Each physics step while the body slides on the snow: where its middle is, and how fast it moves.</summary>
        public void Slide(Vector3 at, Vector3 vel, float dt)
        {
            if (!On || bleed <= 0f) return;
            bleed = Mathf.Max(0f, bleed - dt * 0.35f);
            var g = OnSnow(at);
            if (!smearing) { smearing = true; lastSmear = g; return; }
            Vector3 d = g - lastSmear;
            float len = d.magnitude;
            if (len < 0.5f) return;
            if (len > 1.5f) { lastSmear = g; return; }   // bounced off the snow and came down further on: start again here
            // a strip from the last point to here, drier the further it has run
            float t0 = smeared / SmearLength, t1 = Mathf.Min(1f, (smeared + len) / SmearLength);
            smeared += len;
            var n = Slope.Normal(g.x, g.z);
            var m = Take();
            m.r.sharedMaterial = smearMat;
            m.mesh.uv = new[] { new Vector2(0f, t0), new Vector2(1f, t0), new Vector2(0f, t1), new Vector2(1f, t1) };
            Tint(m, Mathf.Clamp01(0.35f + bleed));
            Vector3 mid = (g + lastSmear) * 0.5f + n * 0.032f;
            Vector3 along = Vector3.ProjectOnPlane(d, n).normalized;
            m.go.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(-n, along));
            m.go.transform.localScale = new Vector3(0.55f * (0.6f + 0.4f * bleed), len, 1f);   // butted end to end: overlapping strips would double up into dark bands
            m.go.SetActive(true);
            lastSmear = g;
            if (t1 >= 1f) bleed = 0f;
        }

        /// <summary>
        /// A limb torn off (see RiderBodyLimbs): a burst at the wound, both ends, and a splat on the snow under it;
        /// the body then smears as it slides, as after a hard hit on the snow.
        /// </summary>
        public void Sever(Vector3 at, Vector3 dir, Vector3 bodyVel)
        {
            if (!On || marks == null) return;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;
            Burst(at, dir, 1f, bodyVel);
            Burst(at, -dir, 0.7f, bodyVel);
            var g = OnSnow(at + Random.insideUnitSphere * 0.3f);
            Splat(g, Slope.Normal(g.x, g.z), 0.75f, 0.95f);
            lastSplat = g; lastSplatTime = Time.time;
            bleed = 1f; smeared = 0f; smearing = false;
        }

        /// <summary>A wound still pumping: `n` drops out along `dir` (and a drip running off), carried with `vel`.</summary>
        public void Gush(Vector3 at, Vector3 dir, int n, float speed, Vector3 vel)
        {
            if (!On || drops == null || n <= 0) return;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.02f;
                ep.velocity = (dir + Random.insideUnitSphere * 0.35f).normalized * speed * Random.Range(0.6f, 1.2f) + vel;
                drops.Emit(ep, 1);
            }
            if (Random.value < 0.3f) { ep.position = at; ep.velocity = vel * 0.5f + Vector3.down * 0.2f; drips.Emit(ep, 1); }
        }

        /// <summary>A small pool on the snow under a wound lying there (the caller spaces them out).</summary>
        public void Pool(Vector3 at, float size)
        {
            if (!On || marks == null) return;
            var g = OnSnow(at);
            Splat(g, Slope.Normal(g.x, g.z), size, 0.9f);
        }

        public void Clear()
        {
            bleed = 0f; smearing = false; lastSplatTime = -9f;
            if (marks != null) foreach (var m in marks) m.go.SetActive(false);
            if (drops != null) drops.Clear();
            if (drips != null) drips.Clear();
        }

        // ---- the pieces

        static Vector3 OnSnow(Vector3 p) => new Vector3(p.x, Slope.DrawnHeight(p.x, p.z) - Course.CrevasseCut(p.x, p.z), p.z);

        void Burst(Vector3 at, Vector3 normal, float k, Vector3 bodyVel)
        {
            int n = Mathf.RoundToInt(18f + 30f * k);
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = (normal * 0.9f + Random.insideUnitSphere * 0.8f).normalized;
                ep.position = at + Random.insideUnitSphere * 0.05f;
                ep.velocity = dir * Random.Range(1.5f, 4.5f) * (0.7f + 0.6f * k) + bodyVel * 0.15f;
                drops.Emit(ep, 1);
            }
            int d = 2 + Mathf.RoundToInt(3f * k);
            for (int i = 0; i < d; i++)
            {
                ep.position = at + normal * 0.03f + Random.insideUnitSphere * 0.08f;
                ep.velocity = normal * Random.Range(0.1f, 0.5f) + Vector3.down * Random.Range(0f, 0.3f);
                drips.Emit(ep, 1);
            }
        }

        /// <summary>A splat lying flat on a surface: one of the four in the sheet, turned any which way.</summary>
        void Splat(Vector3 at, Vector3 normal, float size, float alpha)
        {
            var m = Take();
            m.r.sharedMaterial = splatMat;
            int v = Random.Range(0, 4);
            float u0 = (v % 2) * 0.5f, v0 = (v / 2) * 0.5f;
            m.mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u0 + 0.5f, v0), new Vector2(u0, v0 + 0.5f), new Vector2(u0 + 0.5f, v0 + 0.5f) };
            Tint(m, alpha);
            Vector3 any = Vector3.Cross(normal, Random.onUnitSphere);
            if (any.sqrMagnitude < 1e-4f) any = Vector3.Cross(normal, Vector3.right);
            // a quad faces -Z: point -Z into the surface, so its face looks out of it
            m.go.transform.SetPositionAndRotation(at + normal * 0.03f, Quaternion.LookRotation(-normal, any.normalized));
            m.go.transform.localScale = new Vector3(size, size, 1f);
            m.go.SetActive(true);
        }

        Mark Take()
        {
            var m = marks[next];
            next = (next + 1) % marks.Length;
            return m;
        }

        static void Tint(Mark m, float a)
        {
            var c = new Color(1f, 1f, 1f, a);
            m.mesh.colors = new[] { c, c, c, c };
        }

        /// <summary>A lit, see-through material for the marks (the same see-through lit particle shader as the snow dents).</summary>
        static Material Decal(Texture2D tex, Material basis)
        {
            var baseMat = Resources.Load<Material>("ImprintBase");
            var sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
            var m = baseMat != null ? new Material(baseMat) : sh != null ? new Material(sh) : new Material(basis);
            if (tex != null) { tex.wrapMode = TextureWrapMode.Clamp; m.SetTexture("_BaseMap", tex); }
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_Smoothness", 0.25f);   // a little sheen
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 2610;   // over the snow dents and craters
            return m;
        }

        ParticleSystem System(string name, Material m, int max)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Fade(ParticleSystem ps)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.6f));
        }
    }
}
