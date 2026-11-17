using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// What the slide looks like (the slide itself, its pace and what it does to the rider, is Avalanche):
    ///  - the powder cloud: a towering front of big, lit, churning puffs (Powder.shader) rolling up and over,
    ///    leaning out ahead at mid height and spilling forward down both flanks, so as it closes it looms over and
    ///    around the rider even from a camera that looks downhill; behind the front, a darker body of billows
    ///  - the surge: dense snow boiling along the ground at its toe, and plumes of spray thrown out ahead of it
    ///  - debris: blocks of snow tumbling at its base, and fine snow dust in the air ahead
    ///  - the burial: snow churning under the board, lumps over the carried body, then a pile of debris
    ///    building over it while the cloud rolls on and thins
    /// All the puffs are one mesh and one draw call, sorted back to front; a few large, textured, lit quads
    /// rather than thousands of particles, to keep overdraw down on the phone. Puffs fade before they reach the
    /// camera, so it never sits inside a wall of them.
    /// </summary>
    [DefaultExecutionOrder(150)]   // after the chase camera has moved: the puffs turn to face it
    public class AvalancheView : MonoBehaviour
    {
        Camera cam;
        Rider rider;
        RiderBody body;
        Avalanche slide;

        // ---------------------------------------------------------------- puffs
        enum Kind : byte { Head, Body, Surge, Plume, Churn }
        struct Puff
        {
            public Kind kind;
            public float u, phase, rate, spin, spinRate, scale, seed, hTop, reach, shade;
            public int cell;
            public Vector3 pos, vel; public float age, life; public bool live;   // plumes fly on their own
        }
        const int Heads = 70, Bodies = 34, Surges = 56, Plumes = 28, Churns = 18;
        const float Width = 36f;      // half-width of the slide across the hill
        const float ArmLead = 32f;    // how far its flanks spill ahead of the middle
        Puff[] puffs;
        Mesh mesh;
        MeshRenderer puffRenderer;
        Vector3[] verts, norms; Vector4[] tans, uv1; Color[] cols; Vector2[] uv0, uv2; int[] tris;
        float[] keys; int[] order;
        Vector3[] centre; float[] size, alpha, angle, fadeH, shadeOf, groundOf;

        // the powder cloud keeps rolling on (and thins away) after the debris under it has stopped
        float cloudZ, cloudSpeed, fadeHead = 1f, fadeBody = 1f, fadeSurge = 1f, t, plumeCarry;

        /// <summary>0..1: how white the screen should go (the camera is in the powder).</summary>
        public float Whiteout { get; private set; }

        // ---------------------------------------------------------------- debris, dust, the pile
        struct Block { public Vector3 p, v, w; public Quaternion q; public float r; public bool live, onPile; public Color c; }
        Block[] blocks = new Block[48];
        const int PileBlocks = 12;   // the first few are kept for the lumps that land on the pile over the body
        ParticleSystem blockPs, dust, lumps, haze;
        ParticleSystem.Particle[] blockParts;
        Mesh pile;
        MeshRenderer pileRenderer;
        Vector3[] pileVerts; Vector2[] pileUv;
        float pileCover = -1f;
        const int PileRings = 13, PileSegs = 32;
        float dustCarry, lumpCarry, hazeCarry, settleCarry;

        public void Build(Camera c, Rider r, RiderBody b, Avalanche s, Material snow, Material soft)
        {
            cam = c; rider = r; body = b; slide = s;

            // the cloud
            var mat = Resources.Load<Material>("Avalanche/Powder");
            if (mat == null)
            {
                var sh = Shader.Find("Summit/Powder");
                mat = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                mat.SetTexture("_MainTex", Resources.Load<Texture2D>("Avalanche/PuffAtlas"));
            }
            var pg = new GameObject("PowderCloud", typeof(MeshFilter), typeof(MeshRenderer));
            pg.transform.SetParent(transform, false);
            mesh = new Mesh { name = "PowderCloud" };
            mesh.MarkDynamic();
            pg.GetComponent<MeshFilter>().sharedMesh = mesh;
            puffRenderer = pg.GetComponent<MeshRenderer>();
            puffRenderer.sharedMaterial = mat;
            puffRenderer.shadowCastingMode = ShadowCastingMode.Off;
            puffRenderer.receiveShadows = false;
            puffRenderer.enabled = false;
            MakePuffs();

            // debris blocks: a lumpy chunk of snow, drawn as mesh particles in one call
            var vc = Resources.Load<Material>("VertexColor");
            var blockMat = vc != null ? new Material(vc) : new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit"));
            blockMat.SetColor("_BaseColor", Color.white);
            blockMat.enableInstancing = true;
            blockPs = MakeSystem("SnowBlocks", blockMat, blocks.Length, true);
            var br = blockPs.GetComponent<ParticleSystemRenderer>();
            br.renderMode = ParticleSystemRenderMode.Mesh;
            br.mesh = Chunk();
            br.alignment = ParticleSystemRenderSpace.World;
            br.shadowCastingMode = ShadowCastingMode.On;
            br.receiveShadows = true;
            var bm = blockPs.main; bm.startRotation3D = true; bm.startSize3D = true; bm.gravityModifier = 0f; bm.startLifetime = 1000f;
            blockParts = new ParticleSystem.Particle[blocks.Length];

            // fine snow dust in the air, lumps thrown up, and local haze
            dust = MakeSystem("SlideDust", soft, 900, false);
            var dm = dust.main;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.0f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            dm.startColor = new Color(1f, 1f, 1f, 0.9f);
            dm.gravityModifier = 0.04f;
            Fade(dust, 0.9f);

            lumps = MakeSystem("SlideLumps", soft, 500, false);
            var lm = lumps.main;
            lm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            lm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
            lm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.97f, 0.98f, 1f, 1f), new Color(0.80f, 0.85f, 0.94f, 1f));
            lm.gravityModifier = 1.2f;
            Fade(lumps, 1f);

            haze = MakeSystem("SlideHaze", soft, 260, false);
            var hm = haze.main;
            hm.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
            hm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            hm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.55f), new Color(0.86f, 0.9f, 0.97f, 0.55f));
            hm.gravityModifier = -0.03f;
            Fade(haze, 0.6f);
            var hsz = haze.sizeOverLifetime; hsz.enabled = true; hsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.9f));

            // the pile of debris over the body, in the mountain's own snow
            var pileGo = new GameObject("DebrisPile", typeof(MeshFilter), typeof(MeshRenderer));
            pileGo.transform.SetParent(transform, false);
            pile = new Mesh { name = "DebrisPile" };
            pile.MarkDynamic();
            pileGo.GetComponent<MeshFilter>().sharedMesh = pile;
            pileRenderer = pileGo.GetComponent<MeshRenderer>();
            pileRenderer.sharedMaterial = snow;
            pileRenderer.shadowCastingMode = ShadowCastingMode.On;
            pileRenderer.enabled = false;
            MakePile();
            Clear();
        }

        ParticleSystem MakeSystem(string name, Material m, int max, bool manual)
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
            if (manual) main.simulationSpeed = 0f;   // placed by hand every frame
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Fade(ParticleSystem ps, float a)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a, 0.12f), new GradientAlphaKey(a, 0.6f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }

        /// <summary>Back to a calm mountain (a new run).</summary>
        public void Clear()
        {
            fadeHead = fadeBody = fadeSurge = 1f;
            for (int i = 0; i < puffs.Length; i++) puffs[i].live = false;
            for (int i = 0; i < blocks.Length; i++) blocks[i].live = false;
            blockPs.Clear(); dust.Clear(); lumps.Clear(); haze.Clear();
            pileRenderer.enabled = false; pileCover = -1f;
            puffRenderer.enabled = false;
            Whiteout = 0f;
        }

        void LateUpdate() { if (Application.isPlaying) Tick(Time.deltaTime); }

        /// <summary>One frame (public so the test harness can drive it; it steps the particle systems itself).</summary>
        public void Tick(float dt)
        {
            if (cam == null || slide == null) return;
            t += dt;
            float gap = slide.Gap(rider);
            bool caught = slide.Caught;
            // the cloud: right on the slide while it chases, then rolling on, slowing and thinning once it has you
            if (!caught)
            {
                cloudZ = slide.Z; cloudSpeed = slide.Speed;
                fadeHead = fadeBody = fadeSurge = 1f;
            }
            else
            {
                cloudSpeed = Mathf.MoveTowards(cloudSpeed, slide.Phase == Avalanche.Stage.Undercut ? slide.Speed : 2.5f, 3f * dt);
                cloudZ = Mathf.Max(cloudZ + cloudSpeed * dt, slide.Z);
                bool after = slide.Phase == Avalanche.Stage.Carried || slide.Phase == Avalanche.Stage.Buried;
                if (after)
                {
                    fadeBody = Mathf.MoveTowards(fadeBody, 0f, dt / 2.5f);    // the camera is in this part: clear it first
                    fadeHead = Mathf.MoveTowards(fadeHead, 0.15f, dt / 7f);  // the head rolls on and thins away
                    if (slide.Speed < 3f) fadeSurge = Mathf.MoveTowards(fadeSurge, 0f, dt / 2.5f);
                }
            }
            bool show = caught || gap < 170f;
            puffRenderer.enabled = show;
            if (show) DrawPuffs(dt);

            Vector3 cp = cam.transform.position;
            float inCloud = Mathf.Clamp01((cloudZ - 7f - cp.z) / 14f) * Mathf.Max(fadeBody, fadeHead * 0.4f);
            Whiteout = inCloud * 0.32f;

            Blocks(dt, show && (caught || gap < 80f));
            Particles(dt, gap);
            Pile();
            if (!Application.isPlaying)
                foreach (var ps in new[] { dust, lumps, haze }) ps.Simulate(dt, true, false, false);
        }

        // ---------------------------------------------------------------- the cloud

        void MakePuffs()
        {
            int n = Heads + Bodies + Surges + Plumes + Churns;
            puffs = new Puff[n];
            var g = new System.Random(4242);
            float R() => (float)g.NextDouble();
            int k = 0;
            for (int i = 0; i < Heads; i++, k++)
            {
                // spread across the slope, denser in the middle where the camera looks
                float a = (i + R()) / Heads * 2f - 1f;
                puffs[k] = new Puff { kind = Kind.Head, u = Mathf.Sign(a) * Mathf.Pow(Mathf.Abs(a), 1.25f), phase = R(), rate = 1f / Mathf.Lerp(6f, 9f, R()),
                                      hTop = Mathf.Lerp(34f, 58f, R()), reach = Mathf.Lerp(14f, 26f, R()), scale = Mathf.Lerp(0.8f, 1.2f, R()) };
            }
            for (int i = 0; i < Bodies; i++, k++)
            {
                float a = (i + R()) / Bodies * 2f - 1f;
                puffs[k] = new Puff { kind = Kind.Body, u = a, phase = R(), rate = 1f / Mathf.Lerp(9f, 13f, R()),
                                      hTop = Mathf.Lerp(18f, 34f, R()), scale = Mathf.Lerp(0.85f, 1.2f, R()) };
            }
            for (int i = 0; i < Surges; i++, k++)
            {
                float a = (i + R()) / Surges * 2f - 1f;
                puffs[k] = new Puff { kind = Kind.Surge, u = Mathf.Sign(a) * Mathf.Pow(Mathf.Abs(a), 1.3f), phase = R(), rate = 1f / Mathf.Lerp(1.8f, 2.8f, R()),
                                      hTop = Mathf.Lerp(1.2f, 2.6f, R()), scale = Mathf.Lerp(0.8f, 1.25f, R()) };
            }
            for (int i = 0; i < Plumes; i++, k++) puffs[k] = new Puff { kind = Kind.Plume };
            for (int i = 0; i < Churns; i++, k++)
                puffs[k] = new Puff { kind = Kind.Churn, u = R() * 2f - 1f, phase = R(), rate = 1f / Mathf.Lerp(0.9f, 1.5f, R()), scale = Mathf.Lerp(0.8f, 1.2f, R()) };
            for (int i = 0; i < n; i++)
            {
                puffs[i].seed = R(); puffs[i].cell = Mathf.Min(3, (int)(R() * 4f));
                puffs[i].spin = R() * Mathf.PI * 2f; puffs[i].spinRate = (R() < 0.5f ? -1f : 1f) * Mathf.Lerp(0.08f, 0.3f, R());
                puffs[i].shade = Mathf.Lerp(0.82f, 1f, R());
            }

            verts = new Vector3[n * 4]; norms = new Vector3[n * 4]; tans = new Vector4[n * 4]; uv1 = new Vector4[n * 4];
            cols = new Color[n * 4]; uv0 = new Vector2[n * 4]; uv2 = new Vector2[n * 4]; tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 1; tris[i * 6 + 2] = i * 4 + 2;
                tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 2; tris[i * 6 + 5] = i * 4 + 3;
            }
            keys = new float[n]; order = new int[n];
            centre = new Vector3[n]; size = new float[n]; alpha = new float[n]; angle = new float[n]; fadeH = new float[n]; shadeOf = new float[n]; groundOf = new float[n];
            mesh.vertices = verts; mesh.normals = norms; mesh.tangents = tans; mesh.colors = cols;
            mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tris, 0, 0, 0, false);
        }

        /// <summary>0 below e0, 1 above e1, smooth between (Mathf.SmoothStep interpolates; this is the shader kind).</summary>
        static float Ease(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        /// <summary>Where the slide's leading edge is at this point across the hill (u: -1..1), as distance behind cloudZ.</summary>
        static float Flank(float u) => -ArmLead * Ease(0.3f, 1f, Mathf.Abs(u));

        void DrawPuffs(float dt)
        {
            Transform ct = cam.transform;
            Vector3 cp = ct.position, cf = ct.forward, cr = ct.right, cu = ct.up;
            float flow = 0.6f + 0.4f * Mathf.Clamp01(cloudSpeed / 15f);   // a fast slide churns faster
            float rx = rider.transform.position.x;
            int n = 0;

            // plumes of spray thrown out ahead of the toe, around where the camera is looking
            if (!slide.Caught || slide.Phase == Avalanche.Stage.Undercut)
            {
                plumeCarry += dt * 9f * Mathf.Clamp01(slide.Speed / 10f);
                for (int i = Heads + Bodies + Surges; i < Heads + Bodies + Surges + Plumes && plumeCarry >= 1f; i++)
                {
                    if (puffs[i].live) continue;
                    plumeCarry -= 1f;
                    float x = rx + Random.Range(-22f, 22f);
                    float u = (x - Slope.Centre(slide.Z)) / Width;
                    float z = slide.Z - Flank(Mathf.Clamp(u, -1f, 1f)) * 0.9f + Random.Range(-2f, 1f);
                    puffs[i].live = true; puffs[i].age = 0f; puffs[i].life = Random.Range(1.2f, 2f);
                    puffs[i].pos = new Vector3(x, Slope.DrawnHeight(x, z) + 0.8f, z);
                    puffs[i].vel = new Vector3(Random.Range(-2f, 2f), Random.Range(4f, 9f), slide.Speed + Random.Range(3f, 9f));
                    puffs[i].scale = Random.Range(0.8f, 1.3f);
                }
                plumeCarry = Mathf.Min(plumeCarry, 1f);
            }

            // the snow boiling under the board while the slide flows under it, then round the body it carries
            Vector3 churnAt = slide.Phase == Avalanche.Stage.Carried ? body.RagdollCentre : rider.transform.position;
            float churn = slide.Phase == Avalanche.Stage.Undercut ? slide.Churn
                        : slide.Phase == Avalanche.Stage.Carried ? Mathf.Clamp01(slide.Speed / 10f) : 0f;
            // with the camera out in front of the cloud, the head hides the body behind it: skip drawing it
            bool bodyHidden = cp.z > cloudZ + 2f;
            for (int i = 0; i < puffs.Length; i++)
            {
                ref Puff p = ref puffs[i];
                if (bodyHidden && p.kind == Kind.Body) { p.spin += p.spinRate * dt; continue; }
                float sz, a, h, s, x, z, fh, gy, shade = p.shade;
                Vector3 c;
                p.spin += p.spinRate * dt;
                if (p.kind == Kind.Churn)
                {
                    if (churn <= 0.01f) continue;
                    float ph = p.phase + t * p.rate;
                    ph -= Mathf.Floor(ph);
                    // rolling forward past the board from behind, heaving up and falling back
                    x = churnAt.x + p.u * 3.2f + Mathf.Sin(t * 1.3f + p.seed * 20f) * 0.6f;
                    z = churnAt.z - 3.5f + 8f * ph;
                    gy = Slope.Height(x, z);
                    h = 0.15f + 1.2f * Mathf.Sin(Mathf.PI * ph) * p.scale;
                    c = new Vector3(x, gy + h, z);
                    sz = Mathf.Lerp(1.2f, 3.4f, ph) * p.scale;
                    a = Mathf.SmoothStep(0f, 1f, ph / 0.15f) * (1f - Ease(0.7f, 1f, ph)) * churn * 0.75f;   // the rider stays in view through it
                    fh = 0.35f;
                }
                else if (p.kind == Kind.Plume)
                {
                    if (!p.live) continue;
                    p.age += dt;
                    if (p.age >= p.life) { p.live = false; continue; }
                    p.vel.y -= 5f * dt;
                    p.vel *= Mathf.Exp(-0.7f * dt);
                    p.pos += p.vel * dt;
                    gy = Slope.Height(p.pos.x, p.pos.z);
                    if (p.pos.y < gy + 0.4f) { p.pos.y = gy + 0.4f; p.vel.y = Mathf.Max(p.vel.y, 0f); }
                    float k = p.age / p.life;
                    c = p.pos; sz = Mathf.Lerp(1.4f, 5.5f, Mathf.Sqrt(k)) * p.scale;
                    a = Mathf.SmoothStep(0f, 1f, k / 0.15f) * (1f - Ease(0.45f, 1f, k)) * 0.85f;
                    fh = 0.5f;
                }
                else
                {
                    float ph = p.phase + t * p.rate * flow;
                    ph -= Mathf.Floor(ph);
                    float side = Mathf.Sin(t * 0.21f + p.seed * 40f) * 2.5f;
                    float arm = Ease(0.3f, 1f, Mathf.Abs(p.u));
                    switch (p.kind)
                    {
                        case Kind.Head:
                            // rising up the front and rolling over: low and back at birth, leaning out ahead at mid
                            // height, curling back at the top; lower and further ahead out on the flanks
                            h = 1.2f + Mathf.Pow(ph, 1.1f) * p.hTop * (1f - 0.45f * arm);
                            s = 6f - p.reach * Ease(0.05f, 0.55f, ph) * (1f - 0.6f * Ease(0.6f, 1f, ph)) + Flank(p.u);
                            sz = Mathf.Lerp(7f, 24f, Mathf.Pow(ph, 0.7f)) * p.scale * (1f - 0.25f * arm);
                            a = Mathf.SmoothStep(0f, 1f, ph / 0.1f) * (1f - Ease(0.8f, 1f, ph)) * fadeHead;
                            shade *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(h / 22f));   // the lower billows sit in the shade of those above
                            fh = 2f + sz * 0.15f;
                            break;
                        case Kind.Body:
                            // the mass behind the front, drifting back as the front runs on, sinking as it goes
                            s = 9f + 62f * ph + Flank(p.u) * 0.6f;
                            h = 3f + p.hTop * (1f - 0.55f * ph);
                            sz = Mathf.Lerp(14f, 26f, ph) * p.scale;
                            a = Mathf.SmoothStep(0f, 1f, ph / 0.15f) * (1f - Ease(0.75f, 1f, ph)) * fadeBody;
                            shade *= Mathf.Lerp(0.72f, 0.86f, p.seed);
                            fh = 3f;
                            break;
                        default:
                            // the surge: boiling forward along the ground, through the toe and dying out just ahead
                            s = 12f - 17f * ph + Flank(p.u) * 0.95f;
                            h = 0.4f + p.hTop * Mathf.Sin(Mathf.PI * Mathf.Min(1f, ph * 1.15f));
                            sz = Mathf.Lerp(2.5f, 7f, ph) * p.scale;
                            a = Mathf.SmoothStep(0f, 1f, ph / 0.12f) * (1f - Ease(0.72f, 1f, ph)) * fadeSurge;
                            shade *= Mathf.Lerp(0.93f, 1f, ph);
                            fh = 0.7f;
                            break;
                    }
                    float baseZ = p.kind == Kind.Surge ? slide.Z : cloudZ;
                    z = baseZ - s;
                    x = Slope.Centre(z) + p.u * Width + side;
                    gy = Slope.Height(x, z);
                    c = new Vector3(x, gy + h, z);
                }
                // near the camera: fade out before it gets there (no puff ever slices through the lens)
                float depth = Vector3.Dot(c - cp, cf);
                if (depth < -sz * 0.5f || depth > 320f) continue;
                a *= Mathf.Clamp01((depth - 0.8f - sz * 0.12f) / (sz * 0.5f));
                if (a < 0.01f) continue;
                centre[i] = c; size[i] = sz; alpha[i] = a; angle[i] = p.spin; fadeH[i] = fh; shadeOf[i] = shade; groundOf[i] = gy;
                keys[n] = -depth; order[n] = i; n++;
            }
            System.Array.Sort(keys, order, 0, n);   // far to near

            Vector3 nrm = -cf;
            // the snow's slope for the soft fade at each puff's foot: one sample is close enough for all of them
            Vector2 grad = Slope.Gradient(cp.x, cp.z);
            var g2 = new Vector2(grad.x, grad.y);
            for (int k = 0; k < n; k++)
            {
                int i = order[k];
                float shade = shadeOf[i];
                Vector3 c = centre[i];
                float half = size[i] * 0.5f, ca = Mathf.Cos(angle[i]), sa = Mathf.Sin(angle[i]);
                Vector3 rr = (cr * ca + cu * sa) * half, uu = (-cr * sa + cu * ca) * half;
                int v = k * 4;
                verts[v] = c - rr - uu; verts[v + 1] = c - rr + uu; verts[v + 2] = c + rr + uu; verts[v + 3] = c + rr - uu;
                int cell = puffs[i].cell;
                float u0 = (cell & 1) * 0.5f + 0.004f, v0 = (cell >> 1) * 0.5f + 0.004f, u1 = u0 + 0.492f, v1 = v0 + 0.492f;
                uv0[v] = new Vector2(u0, v0); uv0[v + 1] = new Vector2(u0, v1); uv0[v + 2] = new Vector2(u1, v1); uv0[v + 3] = new Vector2(u1, v0);
                var g1 = new Vector4(groundOf[i], c.x, c.z, fadeH[i]);
                var col = new Color(shade * 0.97f, shade * 0.985f, shade, alpha[i]);
                var tan = new Vector4(rr.x, rr.y, rr.z, 0f).normalized; tan.w = -1f;   // the texture's up runs along the quad's up
                for (int q = 0; q < 4; q++) { norms[v + q] = nrm; tans[v + q] = tan; cols[v + q] = col; uv1[v + q] = g1; uv2[v + q] = g2; }
            }
            mesh.vertices = verts; mesh.normals = norms; mesh.tangents = tans; mesh.colors = cols;
            mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tris, 0, n * 6, 0, false);
            mesh.bounds = new Bounds(cp, Vector3.one * 2000f);
        }

        // ---------------------------------------------------------------- blocks of snow at its base

        void Blocks(float dt, bool active)
        {
            if (!active)
            {
                if (blockPs.particleCount > 0) blockPs.Clear();
                for (int i = 0; i < blocks.Length; i++) blocks[i].live = false;
                return;
            }
            float rx = rider.transform.position.x;
            bool moving = slide.Speed > 2f;
            bool settling = slide.Phase == Avalanche.Stage.Carried && slide.Cover > 0.05f && slide.Cover < 0.85f;
            settleCarry += settling ? dt * 4f : 0f;
            int count = 0;
            for (int i = 0; i < blocks.Length; i++)
            {
                ref Block b = ref blocks[i];
                if (!b.live)
                {
                    if (moving && i >= PileBlocks && Random.value < dt * 12f)
                    {
                        // a new block breaks off the front, near where the camera is looking
                        float x = rx + Random.Range(-20f, 20f);
                        float u = Mathf.Clamp((x - Slope.Centre(slide.Z)) / Width, -1f, 1f);
                        float z = slide.Z - Flank(u) * 0.95f - Random.Range(0f, 5f);
                        b.r = Random.Range(0.14f, 0.42f) * (Random.value < 0.12f ? 1.8f : 1f);
                        b.p = new Vector3(x, Slope.DrawnHeight(x, z) + b.r, z);
                        b.v = new Vector3(Random.Range(-1f, 1f), Random.Range(0.5f, 2f), slide.Speed * Random.Range(0.95f, 1.2f));
                        b.w = Random.onUnitSphere * Random.Range(2f, 7f);
                        b.q = Random.rotation; b.live = true; b.onPile = false;
                        float g = Random.Range(0.82f, 1f);
                        b.c = new Color(g * 0.96f, g * 0.975f, g, 1f);
                    }
                    else if (i < PileBlocks && settleCarry >= 1f)
                    {
                        // lumps of the debris coming down on the pile over the body
                        settleCarry -= 1f;
                        Vector2 d = Random.insideUnitCircle * 2.2f;
                        b.r = Random.Range(0.15f, 0.4f);
                        b.p = slide.BuryAt + new Vector3(d.x, Random.Range(2.5f, 4.5f), d.y * 1.3f);
                        b.v = new Vector3(0f, -1f, slide.Speed * 0.5f);
                        b.w = Random.onUnitSphere * Random.Range(3f, 8f);
                        b.q = Random.rotation; b.live = true; b.onPile = true;
                        b.c = new Color(0.95f, 0.965f, 0.99f, 1f);
                    }
                    if (!b.live) continue;
                }
                float ground = Slope.DrawnHeight(b.p.x, b.p.z) + PileHeight(b.p.x, b.p.z);
                bool pushed = moving && !b.onPile && b.p.z < slide.Z + 1.5f;
                if (pushed) b.v.z += (slide.Speed * 1.08f - b.v.z) * Mathf.Min(1f, 2.5f * dt);
                b.v.y -= 22f * dt;
                b.p += b.v * dt;
                // tumbling along the snow, never sailing off a bump into the sky
                if (!b.onPile && b.p.y > ground + b.r + 1.2f) b.v.y = Mathf.Min(b.v.y, -2f);
                float floor = ground + b.r * 0.5f;
                bool onSnow = b.p.y <= floor + 0.02f;
                if (b.p.y < floor)
                {
                    b.p.y = floor;
                    if (b.v.y < 0f) b.v.y = pushed ? Random.Range(0.6f, 2.4f) * Mathf.Clamp01(slide.Speed / 18f) : -b.v.y * 0.15f;
                }
                if (onSnow && !pushed)
                {
                    // dragging and rolling to a stop in the snow
                    float k = Mathf.Exp(-2.2f * dt);
                    b.v.x *= k; b.v.z *= k; b.w *= k;
                }
                var flat = new Vector3(b.v.x, 0f, b.v.z);
                if (onSnow && flat.sqrMagnitude > 0.01f)
                    b.q = Quaternion.AngleAxis(flat.magnitude / b.r * dt * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, flat.normalized)) * b.q;
                else if (b.w.sqrMagnitude > 0.01f)
                    b.q = Quaternion.AngleAxis(b.w.magnitude * dt * Mathf.Rad2Deg, b.w.normalized) * b.q;
                // left behind by the front, or off to the side: break up (only while it is still running)
                if (moving && !b.onPile && (b.p.z < slide.Z - Mathf.Max(0f, -Flank(Mathf.Clamp((b.p.x - Slope.Centre(b.p.z)) / Width, -1f, 1f))) - 16f || Mathf.Abs(b.p.x - rx) > 26f))
                { b.live = false; continue; }
                // never right in the lens: a block passing the camera would fill the screen
                if ((b.p - cam.transform.position).sqrMagnitude < (2.5f + b.r * 4f) * (2.5f + b.r * 4f)) continue;
                var pt = new ParticleSystem.Particle
                {
                    position = b.p, rotation3D = b.q.eulerAngles, startSize3D = new Vector3(b.r * 2.1f, b.r * 1.5f, b.r * 1.8f),
                    startColor = b.c, startLifetime = 1000f, remainingLifetime = 1000f
                };
                blockParts[count++] = pt;
            }
            blockPs.SetParticles(blockParts, count);
        }

        /// <summary>A rough chunk of packed snow, about a unit across: a rounded box knocked about by noise.</summary>
        static Mesh Chunk()
        {
            const int n = 5;
            var vs = new System.Collections.Generic.List<Vector3>();
            var ts = new System.Collections.Generic.List<int>();
            var cs = new System.Collections.Generic.List<Color>();
            Vector3[] fn = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var f in fn)
            {
                Vector3 a = f.x != 0f ? Vector3.up : Vector3.right, b = Vector3.Cross(f, a);
                int start = vs.Count;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 p = f + a * (i / (float)n * 2f - 1f) + b * (j / (float)n * 2f - 1f);
                        p = Vector3.Lerp(p, p.normalized * 1.25f, 0.65f);
                        float k = Mathf.PerlinNoise(p.x * 2.1f + 3f, p.y * 2.1f + p.z * 1.7f) - 0.5f;
                        vs.Add(p * (0.5f + 0.22f * k));
                        cs.Add(Color.white);
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int q = start + j * (n + 1) + i;
                        ts.Add(q); ts.Add(q + n + 1); ts.Add(q + 1);
                        ts.Add(q + 1); ts.Add(q + n + 1); ts.Add(q + n + 2);
                    }
            }
            // soft within each face, a crease where the faces meet: a broken lump of packed snow
            var fv = vs.ToArray(); var ft = ts.ToArray();
            var m = new Mesh { name = "SnowChunk", vertices = fv, colors = cs.ToArray() };
            // make every triangle face out (whichever way the face's axes happened to wind)
            for (int i = 0; i < ft.Length; i += 3)
            {
                Vector3 a0 = fv[ft[i]], a1 = fv[ft[i + 1]], a2 = fv[ft[i + 2]];
                if (Vector3.Dot(Vector3.Cross(a1 - a0, a2 - a0), a0 + a1 + a2) < 0f) { int tmp = ft[i + 1]; ft[i + 1] = ft[i + 2]; ft[i + 2] = tmp; }
            }
            m.triangles = ft;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- dust, lumps and haze

        void Particles(float dt, float gap)
        {
            var ep = new ParticleSystem.EmitParams();
            Vector3 rp = rider.transform.position, cp = cam.transform.position;
            var phase = slide.Phase;
            // fine dust in the air ahead of the front, thickening as it closes
            float near = phase == Avalanche.Stage.Chasing ? Mathf.Clamp01(1f - gap / 45f) : phase == Avalanche.Stage.Undercut ? 1f : 0f;
            dustCarry += dt * 520f * near * near;
            int nd = (int)dustCarry; dustCarry -= nd;
            for (int i = 0; i < nd; i++)
            {
                float z = slide.Z + Random.Range(-3f, 24f), x = cp.x + Random.Range(-11f, 11f);
                ep.position = new Vector3(x, Slope.DrawnHeight(x, z) + Random.Range(0.2f, 7f), z);
                ep.velocity = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.4f, 1.6f), slide.Speed * Random.Range(0.6f, 1f));
                dust.Emit(ep, 1);
            }
            // after it has you: the cloud's snow sifting down all around
            if (phase == Avalanche.Stage.Carried || phase == Avalanche.Stage.Buried)
            {
                float settle = Mathf.Max(fadeBody, fadeHead * 0.6f);
                dustCarry += dt * 300f * settle;
                nd = (int)dustCarry; dustCarry -= nd;
                for (int i = 0; i < nd; i++)
                {
                    float z = cp.z + Random.Range(-4f, 26f), x = cp.x + Random.Range(-10f, 10f);
                    ep.position = new Vector3(x, Slope.DrawnHeight(x, z) + Random.Range(0.5f, 8f), z);
                    ep.velocity = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-1.2f, -0.3f), cloudSpeed * 0.3f);
                    dust.Emit(ep, 1);
                }
            }

            // the snow under the board churning: lumps thrown up round the board, powder boiling over it
            if (phase == Avalanche.Stage.Undercut)
            {
                Vector3 feet = rp + Vector3.down * Rider.R;
                lumpCarry += dt * 170f * slide.Churn;
                hazeCarry += dt * 40f * slide.Churn;
                Vector3 carry = new Vector3(0f, 0f, slide.Speed * 0.85f);
                EmitLumps(ref ep, feet, 1.4f, carry, 2f, 5.5f);
                EmitHaze(ref ep, feet, 1.8f, carry * 0.9f);
            }
            // the body tumbling in the debris, and the pile building over it
            else if (phase == Avalanche.Stage.Carried)
            {
                Vector3 c = body.RagdollCentre;
                float k = Mathf.Clamp01(slide.Speed / 8f);
                lumpCarry += dt * (55f * k + 30f * (slide.Cover < 1f ? 1f : 0f));
                hazeCarry += dt * (12f * k + 8f * (slide.Cover < 1f ? 1f : 0f));
                Vector3 at = Vector3.Lerp(slide.BuryAt + Vector3.up * 0.5f, c, k);
                Vector3 carry = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(c.x, c.z)) * slide.Speed * 0.8f;
                EmitLumps(ref ep, at, 1.8f, carry, k > 0.3f ? 1.5f : -0.5f, k > 0.3f ? 4f : 1f);
                EmitHaze(ref ep, at, 2.2f, carry * 0.6f);
            }
            else { lumpCarry = 0f; hazeCarry = 0f; }
        }

        void EmitLumps(ref ParticleSystem.EmitParams ep, Vector3 at, float spread, Vector3 carry, float up0, float up1)
        {
            int n = (int)lumpCarry; lumpCarry -= n;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = Random.insideUnitCircle * spread;
                ep.position = at + new Vector3(d.x, Random.Range(0f, 0.4f), d.y);
                ep.velocity = carry + new Vector3(d.x * 1.2f, Random.Range(up0, up1), d.y * 1.2f);
                ep.startSize = Random.Range(0.08f, 0.38f);
                lumps.Emit(ep, 1);
            }
        }

        void EmitHaze(ref ParticleSystem.EmitParams ep, Vector3 at, float spread, Vector3 carry)
        {
            int n = (int)hazeCarry; hazeCarry -= n;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = Random.insideUnitCircle * spread;
                ep.position = at + new Vector3(d.x, Random.Range(0.2f, 1.2f), d.y);
                ep.velocity = carry + new Vector3(d.x * 0.5f, Random.Range(0.3f, 1.4f), d.y * 0.5f);
                ep.startSize = Random.Range(0.9f, 2.4f);
                haze.Emit(ep, 1);
            }
        }

        // ---------------------------------------------------------------- the pile over the body

        /// <summary>How high the debris has piled over the body at this point (0 away from the pile).</summary>
        public float PileHeight(float x, float z)
        {
            float cover = slide.Cover;
            if (cover <= 0f) return 0f;
            Vector3 at = slide.BuryAt;
            float dx = x - at.x, dz = (z - at.z) / 1.35f;
            float spread = 0.55f + 0.45f * cover;
            float r = Mathf.Sqrt(dx * dx + dz * dz) / spread;
            if (r > 6.2f) return 0f;
            float th = Mathf.Atan2(dz, dx);
            float n1 = Mathf.PerlinNoise(Mathf.Cos(th) * 1.3f + 11f + at.x * 0.1f, Mathf.Sin(th) * 1.3f + r * 0.35f + at.z * 0.1f);
            float n2 = Mathf.PerlinNoise(x * 1.9f + 5f, z * 1.9f + 7f);
            float h = 1.15f * Mathf.Exp(-(r / 2.3f) * (r / 2.3f)) + 0.55f * Mathf.Exp(-(r / 5f) * (r / 5f));
            // a few heaps dumped round the main one, so it is a jumble of debris rather than one neat dome
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 1.7f + Mathf.PerlinNoise(at.x * 0.37f + k, at.z * 0.29f)) * Mathf.PI * 0.5f;
                float bx = dx / spread - Mathf.Cos(a) * (1.4f + 0.4f * k), bz = dz / spread - Mathf.Sin(a) * (1.6f + 0.3f * k);
                h += (0.55f - 0.08f * k) * Mathf.Exp(-(bx * bx + bz * bz) / 0.8f);
            }
            h *= 1f + 0.45f * (n1 - 0.5f);
            h += 0.3f * (n2 - 0.5f) * Mathf.Exp(-(r / 4f) * (r / 4f));   // lumpy: blocks and clods, not a smooth dome
            float edge = Mathf.Clamp01((6.2f - r) / 1.2f);
            return Mathf.Max(0f, h * cover * edge);
        }

        void MakePile()
        {
            int n = 1 + PileRings * PileSegs;
            pileVerts = new Vector3[n]; pileUv = new Vector2[n];
            var tri = new System.Collections.Generic.List<int>();
            for (int s = 0; s < PileSegs; s++)
            {
                int s1 = (s + 1) % PileSegs;
                tri.Add(0); tri.Add(1 + s1); tri.Add(1 + s);
                for (int r = 0; r < PileRings - 1; r++)
                {
                    int a = 1 + r * PileSegs + s, b = 1 + r * PileSegs + s1, c = a + PileSegs, d = b + PileSegs;
                    tri.Add(a); tri.Add(b); tri.Add(c);
                    tri.Add(b); tri.Add(d); tri.Add(c);
                }
            }
            pile.vertices = pileVerts; pile.uv = pileUv;
            pile.SetTriangles(tri, 0);
        }

        void Pile()
        {
            float cover = slide.Cover;
            if (cover <= 0f) { if (pileRenderer.enabled) pileRenderer.enabled = false; return; }
            pileRenderer.enabled = true;
            if (Mathf.Abs(cover - pileCover) < 0.002f && slide.Phase == Avalanche.Stage.Buried) return;
            pileCover = cover;
            Vector3 at = slide.BuryAt;
            float outer = 6.2f * (0.55f + 0.45f * cover);
            for (int r = -1; r < PileRings; r++)
            {
                float rr = r < 0 ? 0f : outer * Mathf.Pow((r + 1f) / PileRings, 1.3f);
                for (int s = 0; s < (r < 0 ? 1 : PileSegs); s++)
                {
                    float th = s / (float)PileSegs * Mathf.PI * 2f;
                    float x = at.x + Mathf.Cos(th) * rr, z = at.z + Mathf.Sin(th) * rr * 1.35f;
                    // the outer ring tucks just under the snow, so the pile grows out of the slope with no seam
                    float lift = r == PileRings - 1 ? -0.04f : PileHeight(x, z) + 0.015f;
                    int v = r < 0 ? 0 : 1 + r * PileSegs + s;
                    pileVerts[v] = new Vector3(x, Slope.DrawnHeight(x, z) + lift, z);
                    pileUv[v] = new Vector2(x / 9f, z / 9f);   // the same mapping as the slope, so the snow matches
                }
            }
            pile.vertices = pileVerts; pile.uv = pileUv;
            pile.RecalculateNormals();
            pile.RecalculateTangents();
            pile.RecalculateBounds();
        }
    }
}
