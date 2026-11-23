using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Blood, only when a crash is really hard: the body slamming into a tree, rock or sign, or into the snow after
    /// a big fall. Never gory, light tumbles stay clean, and the whole thing can be switched off in the pause menu.
    ///
    /// What a hard crash leaves, all driven by how the body really moved:
    /// - at the hit, a spray of drops thrown on with the body's own way of going: each one flies (the particles) and
    ///   lands where its arc meets the snow as a small spot, round where it fell steeply, drawn out with a tail where
    ///   it came in low. So a fast hit fans its spots out ahead down the hill; a hit on a tree throws them back off it.
    /// - a stain where it struck: the snow wicks it up, so it spreads over a second or two from a small wet core to
    ///   a lobed stain with a pink, feathered rim and fine fingers, dusted with the powder the hit threw over it.
    /// - while the body slides on, a drag trail along its real path: wider where it presses in hard, striated by the
    ///   clothes, running dry and breaking up the further it goes and the slower it gets; drips between the bounces.
    /// - where it comes to rest, a pool that keeps slowly spreading, downhill a little.
    /// - on a trunk or rock that was hit, a stain wrapped round it with runs dripping down.
    /// All of it soaks in, darkens as it dries, and is dusted over by falling snow (see BloodSnow.shader, which
    /// multiplies it over the snow already drawn, so it takes whatever light is there: sun, shadow, a floodlight's
    /// pool, the headlamp, fog). Every mark lies on the snow's own surface, including the furrows and holes the body
    /// pressed into the soft layer (SnowLayer), so nothing floats or fights.
    ///
    /// Cheap for the phone: every mark is a slot in one mesh (one draw call), its vertices rewritten only when a
    /// mark is laid; the soaking and drying happen on the GPU from one clock. Slots are reused oldest first,
    /// preferring the ones well behind the rider, and any left far up the hill are taken away. Cleared on a restart.
    /// </summary>
    public class Blood : MonoBehaviour
    {
        /// <summary>Off: no spray, no marks (and the marks already left are wiped).</summary>
        public bool On { get; private set; } = true;
        // impact speeds into the surface (m/s) that draw blood: a tree or rock hurts sooner than the snow does.
        // Above the bone-break thresholds (3.2, 9) so a break alone stays clean: blood is for the worst of them
        public static float ObstacleHard = 7f, GroundHard = 9.5f;
        public static float SmearLength = 9f;   // metres of drag trail one hard hit has in it before it runs dry

        // ---- the mark slots: stains (a grid), trail pieces (a ribbon), drops (a quad)
        const int StainCount = 36, StainSide = 11;
        const int TrailCount = 44, TrailRows = 14, TrailCols = 7;
        const int DropCount = 360;
        const float Lift = 0.012f;           // over the snow (the shader adds a depth offset too)
        const float Behind = 90f;            // marks this far up the hill of the camera are taken away
        static readonly float[] TrailX = { -2.5f, -1.5f, -0.75f, 0f, 0.75f, 1.5f, 2.5f };   // the ribbon's columns, in half widths

        enum Kind { Stain = 0, Trail = 1, Drop = 2, OnObstacle = 3 }

        [StructLayout(LayoutKind.Sequential)]
        struct V { public Vector3 pos, nrm; public Vector4 uv0, uv1, uv2, uv3; }

        class Slot
        {
            public int v0, n;          // its vertices
            public bool live;
            public float born;         // blood clock
            public Vector3 at;
            public Collider on;        // a stain on a tree or rock: gone if that is
        }

        ParticleSystem drops, drips;
        Mesh mesh; MeshRenderer mr; Material mat, rockMat;

        // blood on a rock: its own mesh drawn again with the stain projected onto it (BloodDecal.shader)
        const int RockCount = 6;
        class RockMark { public bool live; public float born; public Vector3 at; public Collider on; public MaterialPropertyBlock mpb = new MaterialPropertyBlock(); public readonly List<Forest.Piece> pieces = new List<Forest.Piece>(); }
        readonly RockMark[] rocks = new RockMark[RockCount];
        NativeArray<V> verts;
        int dirty0 = int.MaxValue, dirty1 = -1;
        Slot[] stains, trails, spots;
        Bounds bounds; bool anyBounds;
        float clock, snowAcc, sweep;
        Camera cam;
        MeshRenderer layerR;   // the soft snow layer's renderer: on while it is drawn

        // the drops in the air that will land: when, where, and how
        struct Falling { public float at; public Vector3 p, dir; public float size, stretch; }
        readonly List<Falling> falling = new List<Falling>();

        // the crash going on: how much blood the body still smears, how badly it bleeds, the trail being laid
        float ink, wound;
        bool trailing; Slot seg; int segRow; Vector3 head, rowAt, rowDir; float along, rowAlong, rowHalf, rowAmount, trailSeed, lastSlide = -9f;
        Vector3 lastSplat; float lastSplatTime = -9f;
        Vector3 poolAt; int pools; float stillFor; Vector3 lastBody;

        static readonly Color Red = new Color(0.42f, 0.02f, 0.03f);
        static readonly int ClockId = Shader.PropertyToID("_BloodClock"), SnowId = Shader.PropertyToID("_BloodSnowAcc"),
            LayerId = Shader.PropertyToID("_BloodOnLayer");

        /// <summary>Marks showing (stains, trail pieces and drops).</summary>
        public int Shown { get { int n = 0; if (stains != null) { foreach (var s in stains) if (s.live) n++; foreach (var s in trails) if (s.live) n++; foreach (var s in spots) if (s.live) n++; foreach (var r in rocks) if (r != null && r.live) n++; } return n; } }
        /// <summary>Metres of drag trail laid since the last clear (for the tests).</summary>
        public float TrailLaid { get; private set; }
        /// <summary>Stains, trail pieces, drops showing (for the tests).</summary>
        public (int stains, int trail, int drops) Counts
        {
            get { int a = 0, b = 0, c = 0; if (stains != null) { foreach (var s in stains) if (s.live) a++; foreach (var s in trails) if (s.live) b++; foreach (var s in spots) if (s.live) c++; } return (a, b, c); }
        }

        public void Build(Material soft)
        {
            drops = System("BloodDrops", soft, 260);
            var dm = drops.main;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            dm.startColor = new ParticleSystem.MinMaxGradient(Red, new Color(0.55f, 0.04f, 0.05f));
            dm.gravityModifier = 1.1f;
            Fade(drops);
            // drawn out along their flight, so a fast throw reads as streaks rather than dots
            var dr = drops.GetComponent<ParticleSystemRenderer>();
            dr.renderMode = ParticleSystemRenderMode.Stretch; dr.velocityScale = 0.025f; dr.lengthScale = 1.2f;
            drips = System("BloodDrips", soft, 60);
            var pm = drips.main;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            pm.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
            pm.startColor = new ParticleSystem.MinMaxGradient(Red);
            pm.gravityModifier = 1.1f;
            Fade(drips);

            // the marks: one mesh, every slot laid out once, all collapsed (drawing nothing) until used
            stains = new Slot[StainCount]; trails = new Slot[TrailCount]; spots = new Slot[DropCount];
            int nv = 0, ni = 0;
            for (int i = 0; i < StainCount; i++) { stains[i] = new Slot { v0 = nv, n = StainSide * StainSide }; nv += stains[i].n; ni += (StainSide - 1) * (StainSide - 1) * 6; }
            for (int i = 0; i < TrailCount; i++) { trails[i] = new Slot { v0 = nv, n = TrailRows * TrailCols }; nv += trails[i].n; ni += (TrailRows - 1) * (TrailCols - 1) * 6; }
            for (int i = 0; i < DropCount; i++) { spots[i] = new Slot { v0 = nv, n = 4 }; nv += 4; ni += 6; }
            var idx = new ushort[ni]; int t = 0;
            void Grid(Slot s, int cols, int rows)
            {
                for (int r = 0; r < rows - 1; r++)
                    for (int c = 0; c < cols - 1; c++)
                    {
                        int a = s.v0 + r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                        idx[t++] = (ushort)a; idx[t++] = (ushort)d; idx[t++] = (ushort)b;
                        idx[t++] = (ushort)b; idx[t++] = (ushort)d; idx[t++] = (ushort)e;
                    }
            }
            foreach (var s in stains) Grid(s, StainSide, StainSide);
            foreach (var s in trails) Grid(s, TrailCols, TrailRows);
            foreach (var s in spots) Grid(s, 2, 2);
            verts = new NativeArray<V>(nv, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            mesh = new Mesh { name = "BloodMarks" };
            mesh.SetVertexBufferParams(nv,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4));
            mesh.SetVertexBufferData(verts, 0, 0, nv);
            mesh.SetIndexBufferParams(ni, IndexFormat.UInt16);
            mesh.SetIndexBufferData(idx, 0, 0, ni);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni), MeshUpdateFlags.DontRecalculateBounds);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            mesh.MarkDynamic();

            var sh = Resources.Load<Shader>("Blood/BloodSnow");
            mat = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit")) { name = "BloodSnow" };
            mat.SetTexture("_Noise", Noise());
            var dsh = Resources.Load<Shader>("Blood/BloodDecal");
            if (dsh != null) { rockMat = new Material(dsh) { name = "BloodDecal" }; rockMat.SetTexture("_Noise", Noise()); }
            for (int i = 0; i < RockCount; i++) rocks[i] = new RockMark();
            var go = new GameObject("BloodMarks", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.enabled = false;
            Shader.SetGlobalFloat(ClockId, 0f);
            Shader.SetGlobalFloat(SnowId, 0f);
        }

        void OnDestroy() { if (verts.IsCreated) verts.Dispose(); }

        public void SetOn(bool on)
        {
            On = on;
            if (!on) Clear();
        }

        /// <summary>The forest, which knows where its trees' trunks and rocks' faces really are.</summary>
        public Forest forest;

        void Update() { if (Application.isPlaying) { Advance(Time.deltaTime); Draw(); } }

        /// <summary>The blood on rocks, drawn over them this frame (the game's Update; a test harness before it renders).</summary>
        public void Draw()
        {
            if (rockMat == null) return;
            foreach (var r in rocks)
            {
                if (!r.live) continue;
                var rp = new RenderParams(rockMat) { matProps = r.mpb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
                foreach (var pc in r.pieces) Graphics.RenderMesh(rp, pc.mesh, pc.sub, pc.m);
            }
        }

        /// <summary>
        /// Each frame (the game's Update, or a test harness): the blood clock the marks soak and dry by, the snow
        /// falling on them, drops landing, old marks far behind taken away, and what changed sent to the GPU.
        /// </summary>
        public void Advance(float dt)
        {
            if (stains == null) return;
            clock += dt;
            var w = Weather.Active;
            if (w != null) snowAcc += dt * (0.012f * w.Now.snow + 0.006f * w.Now.drift);
            Shader.SetGlobalFloat(ClockId, clock);
            Shader.SetGlobalFloat(SnowId, snowAcc);
            if (layerR == null && SnowLayer.Instance != null) layerR = SnowLayer.Instance.GetComponent<MeshRenderer>();
            Shader.SetGlobalFloat(LayerId, layerR != null && layerR.enabled ? 1f : 0f);
            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var f = falling[i];
                if (f.at > clock) continue;
                falling.RemoveAt(i);
                if (On) Spot(f.p, f.dir, f.size, f.stretch);
            }
            if (ink > 0f && trailing && clock - lastSlide > 0.25f) EndTrail();
            sweep -= dt;
            if (sweep <= 0f) { sweep = 0.5f; Sweep(); }
            Flush();
        }

        // ---------------------------------------------------------------- what the crash tells it

        /// <summary>
        /// A part of the thrown body (or the rider) hit something: where, the surface's normal there (pointing out
        /// of it, toward the body), how fast into it (m/s), whether it was the snow, how the body was moving, and
        /// what it hit (null: nothing to mark, an animal).
        /// </summary>
        public void Hit(Vector3 at, Vector3 normal, float into, bool ground, Vector3 bodyVel, Collider other = null)
        {
            if (!On || stains == null) return;
            float hard = ground ? GroundHard : ObstacleHard;
            if (into < hard) return;
            float k = Mathf.Clamp01((into - hard) / 10f);
            normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            // (on the snow, the drops keep the body's way along the snow, not its fall into it)
            Burst(at, normal, k, ground ? new Vector3(bodyVel.x, 0f, bodyVel.z) : bodyVel, ground ? 0.55f : 0.2f);
            if (wound <= 0f) pools = 0;   // a new crash
            wound = Mathf.Max(wound, 0.45f + 0.55f * k);
            if (!ground && other == null) return;   // nothing to mark (an animal)
            if (clock - lastSplatTime < 1f && (at - lastSplat).sqrMagnitude < 0.64f) return;   // bouncing on the same spot
            lastSplat = at; lastSplatTime = clock;
            Vector3 flat = new Vector3(bodyVel.x, 0f, bodyVel.z);
            float thrown = Mathf.Clamp(flat.magnitude / 14f, 0f, 1.3f);
            if (ground)
            {
                var g = OnSnow(at);
                bool fatal = into > 40f;
                float size = fatal ? 0.6f : 0.24f + 0.3f * k;
                Stain(g, flat, size, thrown * (fatal ? 0.4f : 1f), 0.85f + 0.15f * k, fatal ? 2.2f : 1.4f, Random.Range(0.08f, 0.3f), 0f);
                if (fatal)
                {
                    // the body is down a hole of its own shape: the blood is thrown out over the snow heaped round it
                    Burst(at + normal * 0.3f, normal, 1f, flat * 0.3f, 1f);
                    float a0 = Random.Range(0f, 360f);
                    for (int i = 0; i < 3; i++)
                    {
                        var dir = Quaternion.Euler(0f, a0 + i * 120f + Random.Range(-30f, 30f), 0f) * Vector3.forward;
                        var rim = OnSnow(g + dir * Random.Range(1.1f, 1.6f));
                        Stain(rim, dir, Random.Range(0.22f, 0.36f), 0.9f, 0.9f, 1.6f, Random.Range(0.05f, 0.2f), 0f);
                    }
                }
                ink = Mathf.Max(ink, 0.55f + 0.45f * k);
                StartTrail();
            }
            else
            {
                // on the trunk or the rock face as drawn, not on the rough collider round it
                float round = 1f; Vector3 sAt = at, sN = normal;
                bool ours = forest != null && forest.SurfaceOf(other, at + normal * 0.4f, out sAt, out sN, out round);
                if (ours) { at = sAt; normal = sN; } else round = 100f;   // (a sign, a pole: flat enough)
                bool rock = other.name.StartsWith("Rock");
                bool trunk = ours && !rock && Mathf.Abs(normal.y) < 0.3f;
                // (not up in a fir's branches: the drops fall through them onto the snow)
                if (rock && ours && rockMat != null) OnRock(at, normal, 0.12f + 0.08f * k, other, k);
                else if (trunk || rock || (!ours && Mathf.Abs(normal.y) < 0.6f))
                    Wrapped(at, normal, Mathf.Max(0.05f, round), trunk, 0.16f + 0.16f * k, other, k);
                // and some at its foot, run and shaken off
                var below = OnSnow(at + new Vector3(normal.x, 0f, normal.z).normalized * Random.Range(0.25f, 0.55f));
                Stain(below, flat + new Vector3(normal.x, 0f, normal.z) * 3f, 0.14f + 0.1f * k, 0.3f, 0.7f, 1.2f, Random.Range(0.1f, 0.3f), 0f);
                ink = Mathf.Max(ink, 0.35f + 0.35f * k);
            }
        }

        /// <summary>
        /// Each physics step while a part of the body ploughs the snow: where it is, how fast the body moves, and how
        /// hard it presses in (m/s times how deep). Lays the drag trail.
        /// </summary>
        public void Slide(Vector3 at, Vector3 vel, float press)
        {
            if (!On || ink <= 0f || stains == null) return;
            lastSlide = clock;
            var g = OnSnow(at);
            if (!trailing) { trailing = true; head = g; NewSegment(g); return; }
            head = Vector3.Lerp(head, g, 0.35f);   // (both the chest and the hips plough: the trail follows between them)
            Vector3 d = head - rowAt; d.y = 0f;
            float len = d.magnitude;
            if (len > 2.2f) { EndTrail(); trailing = true; head = g; NewSegment(g); return; }   // came down further on
            if (len < 0.2f) return;
            Vector3 dir = d / len;
            float speed = new Vector2(vel.x, vel.z).magnitude;
            // wider pressed in hard; thinner, and breaking up, as it slows and runs dry
            float slow = Mathf.Clamp01(speed / 4f);
            along += len;
            // it comes on over the first metre (no edge across the snow where it began)
            float start = Mathf.Clamp01(along / 1.2f);
            float half = (0.07f + 0.11f * Mathf.Sqrt(ink)) * (0.6f + 0.4f * Mathf.Clamp01(press / 5f)) * Mathf.Lerp(0.6f, 1f, slow) * Mathf.Lerp(0.55f, 1f, start);
            float amount = ink * Mathf.Lerp(0.45f, 1f, slow) * Mathf.Lerp(0.25f, 1f, start);
            TrailLaid += len;
            ink = Mathf.Max(0f, ink - len / SmearLength * Mathf.Lerp(1.4f, 0.8f, slow));
            AddRow(head, dir, half, amount);
            if (ink <= 0f) EndTrail();
        }

        /// <summary>
        /// Every frame of a crash while the body is down (or in the air): where its middle is and how it moves.
        /// Drips between the bounces; once it has stopped, a pool spreading slowly under it.
        /// </summary>
        public void Body(Vector3 centre, Vector3 vel, float dt)
        {
            if (!On || stains == null || wound <= 0f) return;
            wound = Mathf.Max(0f, wound - dt / 16f);
            float up = centre.y - Slope.DrawnHeight(centre.x, centre.z);
            if (up > 0.7f && Random.value < dt * 9f * wound)
            {
                // a drip falling from the body in the air, carried with it
                Vector3 p = centre + Random.insideUnitSphere * 0.2f;
                Throw(drips, p, vel * Random.Range(0.85f, 1f) + Random.insideUnitSphere * 0.4f, Random.Range(0.035f, 0.055f), true);
            }
            float speed = vel.magnitude;
            stillFor = speed < 0.6f && up < 0.6f && up > -0.35f ? stillFor + dt : 0f;
            if (stillFor > 0.4f && pools < 2 && wound > 0.15f && (pools == 0 || (centre - poolAt).sqrMagnitude > 1f))
            {
                poolAt = centre; pools++;
                var n = Slope.Normal(centre.x, centre.z);
                Vector3 down = Vector3.ProjectOnPlane(Vector3.down, n);
                var g = OnSnow(centre + down.normalized * 0.12f);
                Stain(g, down, 0.2f + 0.25f * wound, 0.35f, 0.95f, 7f, 0.02f, 1f);
            }
        }

        /// <summary>
        /// A limb torn off (see RiderBodyLimbs): a burst at the wound, both ends, and a stain on the snow under it;
        /// the body then smears as it slides, as after a hard hit on the snow.
        /// </summary>
        public void Sever(Vector3 at, Vector3 dir, Vector3 bodyVel)
        {
            if (!On || stains == null) return;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;
            Burst(at, dir, 1f, bodyVel, 0.5f);
            Burst(at, -dir, 0.7f, bodyVel, 0.5f);
            var g = OnSnow(at + Random.insideUnitSphere * 0.3f);
            Stain(g, new Vector3(bodyVel.x, 0f, bodyVel.z), 0.42f, Mathf.Clamp(bodyVel.magnitude / 14f, 0f, 1.2f), 1f, 1.4f, 0.1f, 0f);
            lastSplat = g; lastSplatTime = clock;
            ink = 1f; wound = 1f;
            StartTrail();
        }

        /// <summary>A wound still pumping: `n` drops out along `dir` (and a drip running off), carried with `vel`.</summary>
        public void Gush(Vector3 at, Vector3 dir, int n, float speed, Vector3 vel)
        {
            if (!On || drops == null || n <= 0) return;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;
            for (int i = 0; i < n; i++)
                Throw(drops, at + Random.insideUnitSphere * 0.02f, (dir + Random.insideUnitSphere * 0.35f).normalized * speed * Random.Range(0.6f, 1.2f) + vel, Random.Range(0.03f, 0.06f), Random.value < 0.5f);
            if (Random.value < 0.3f) Throw(drips, at, vel * 0.5f + Vector3.down * 0.2f, 0.045f, true);
        }

        /// <summary>A pool on the snow under a wound lying there (the caller spaces them out).</summary>
        public void Pool(Vector3 at, float size)
        {
            if (!On || stains == null) return;
            var g = OnSnow(at);
            var n = Slope.Normal(g.x, g.z);
            Stain(g, Vector3.ProjectOnPlane(Vector3.down, n), size, 0.25f, 0.9f, 3.5f, 0.02f, 0.6f);
        }

        public void Clear()
        {
            ink = wound = 0f; trailing = false; seg = null; lastSplatTime = -9f; pools = 0; stillFor = 0f;
            falling.Clear();
            TrailLaid = 0f;
            if (stains != null)
            {
                foreach (var s in stains) Kill(s);
                foreach (var s in trails) Kill(s);
                foreach (var s in spots) Kill(s);
                foreach (var r in rocks) { r.live = false; r.on = null; r.pieces.Clear(); }
                anyBounds = false;
                Flush();
            }
            if (drops != null) drops.Clear();
            if (drips != null) drips.Clear();
        }

        /// <summary>The middle of the marks within 10 m of a point (for the test tools' cameras).</summary>
        public Vector3 MarksMiddle(Vector3 near)
        {
            Vector3 sum = Vector3.zero; float wsum = 0f;
            void Add(Slot[] all, float w) { foreach (var s in all) if (s.live && (s.at - near).sqrMagnitude < 100f) { sum += s.at * w; wsum += w; } }
            if (stains == null) return near;
            Add(stains, 4f); Add(trails, 2f); Add(spots, 0.2f);
            return wsum > 0f ? sum / wsum : near;
        }

        /// <summary>The stain on a trunk or rock nearest a point, if any (for the test tools).</summary>
        public bool ObstacleMark(Vector3 near, out Vector3 at, out Vector3 normal)
        {
            at = normal = Vector3.zero; float best = 1e9f;
            if (stains == null) return false;
            foreach (var r in rocks)
            {
                if (!r.live) continue;
                float d = (r.at - near).sqrMagnitude;
                if (d < best) { best = d; at = r.at; normal = r.mpb.GetMatrix("_BloodProj").inverse.GetColumn(2).normalized; }
            }
            foreach (var s in stains)
            {
                if (!s.live || s.on == null) continue;
                float d = (s.at - near).sqrMagnitude;
                if (d < best) { best = d; at = s.at; normal = verts[s.v0 + StainSide * StainSide / 2].nrm; }
            }
            return best < 1e8f;
        }

        // ---------------------------------------------------------------- the spray

        /// <summary>
        /// A spray at a hit: drops thrown off the surface and on with the body (`carry`: how much of its way of going
        /// they keep), each landing as a spot where its arc meets the snow; and a few heavy drips.
        /// </summary>
        void Burst(Vector3 at, Vector3 normal, float k, Vector3 bodyVel, float carry)
        {
            int n = Mathf.RoundToInt(16f + 34f * k);
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = (normal * Random.Range(0.5f, 1f) + Random.insideUnitSphere * 0.75f).normalized;
                Vector3 v = dir * Random.Range(1.2f, 4f) * (0.7f + 0.6f * k) + bodyVel * (carry * Random.Range(0.5f, 1.1f));
                Throw(drops, at + Random.insideUnitSphere * 0.05f, v, Random.Range(0.025f, 0.065f), Random.value < 0.75f);
            }
            int d = 2 + Mathf.RoundToInt(3f * k);
            for (int i = 0; i < d; i++)
                Throw(drips, at + normal * 0.03f + Random.insideUnitSphere * 0.08f, normal * Random.Range(0.1f, 0.5f) + Vector3.down * Random.Range(0f, 0.3f) + bodyVel * 0.1f, Random.Range(0.04f, 0.06f), true);
        }

        /// <summary>One drop in flight: where its arc meets the snow, it dies and (if `mark`) leaves a spot there.</summary>
        void Throw(ParticleSystem ps, Vector3 p, Vector3 v, float size, bool mark)
        {
            // where its arc meets the snow: against the slope's plane under it, then once more against the plane
            // where that lands (cheap: three heights a drop, where stepping it along took sixty)
            float g = Physics.gravity.y * 1.1f;
            float t = 0f; Vector3 q = p;
            bool landed = Arc(p, v, g, p, ref t) && Arc(p, v, g, p + v * t + Vector3.up * (0.5f * g * t * t), ref t);
            if (landed) q = p + v * t + Vector3.up * (0.5f * g * t * t);
            Vector3 u = v + Vector3.up * (g * t);
            var ep = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false, position = p, velocity = v, startSize = size,
                startLifetime = landed ? Mathf.Max(0.05f, t) : Random.Range(0.6f, 1f)
            };
            ps.Emit(ep, 1);
            if (!landed || !mark) return;
            Vector3 flat = new Vector3(u.x, 0f, u.z);
            float stretch = Mathf.Clamp(flat.magnitude / Mathf.Max(1f, -u.y) - 0.25f, 0f, 1.4f) * 0.6f;
            falling.Add(new Falling { at = clock + t, p = q, dir = flat, size = size * Random.Range(1.7f, 2.6f), stretch = stretch });
        }

        /// <summary>When (t) a drop thrown from p at v meets the snow's plane under `near`; false if it never does within 3 s.</summary>
        static bool Arc(Vector3 p, Vector3 v, float g, Vector3 near, ref float t)
        {
            float h = Slope.DrawnHeight(near.x, near.z) - Course.CrevasseCut(near.x, near.z);
            Vector3 n = Slope.Normal(near.x, near.z);
            float a = 0.5f * g * n.y, b = Vector3.Dot(v, n), c = Vector3.Dot(p - new Vector3(near.x, h, near.z), n);
            if (c < -0.05f) return false;   // starts under the snow
            float disc = b * b - 4f * a * c;
            if (disc < 0f || a >= 0f) return false;
            float r = (-b - Mathf.Sqrt(disc)) / (2f * a);
            if (r < 0f || r > 3f) return false;
            t = r;
            return true;
        }

        // ---------------------------------------------------------------- the drag trail

        void StartTrail() { trailing = false; seg = null; }

        void EndTrail()
        {
            if (seg != null && segRow > 0)
            {
                // run it out to nothing at the last row
                int last = segRow - 1;
                for (int c = 0; c < TrailCols; c++) { var v = verts[seg.v0 + last * TrailCols + c]; v.uv1.z = 0f; verts[seg.v0 + last * TrailCols + c] = v; }
                Dirty(seg.v0 + last * TrailCols, TrailCols);
            }
            trailing = false; seg = null;
        }

        /// <summary>A trail starts here: nothing laid until the body has moved on from it.</summary>
        void NewSegment(Vector3 at)
        {
            seg = Take(trails);
            seg.live = true; seg.born = clock; seg.at = at; seg.on = null;
            segRow = 0;
            rowAt = at; rowDir = Vector3.forward; rowHalf = 0.08f; rowAmount = 0f;
            along = rowAlong = 0f; trailSeed = Random.Range(0f, 50f);
        }

        void AddRow(Vector3 at, Vector3 dir, float half, float amount)
        {
            if (seg == null) NewSegment(rowAt);
            if (segRow >= TrailRows)
            {
                // this piece is full: carry on in a new one, starting from its last row (no gap, no seam)
                seg = Take(trails); seg.live = true; seg.born = clock; seg.at = at; seg.on = null; segRow = 0;
                WriteRow(rowAt, rowDir, rowHalf, rowAmount, rowAlong);
            }
            // the first row of a trail: where it began, thin and dry
            if (segRow == 0) WriteRow(rowAt, dir, half * 0.6f, 0f, rowAlong);
            WriteRow(at, dir, half, amount, along);
            // the rows not yet laid sit on this one (drawing nothing)
            for (int r = segRow; r < TrailRows; r++)
                for (int c = 0; c < TrailCols; c++) verts[seg.v0 + r * TrailCols + c] = verts[seg.v0 + (segRow - 1) * TrailCols + c];
            Dirty(seg.v0, TrailRows * TrailCols);
            seg.at = at;
            rowAt = at; rowDir = dir; rowHalf = half; rowAmount = amount; rowAlong = along;
            Grow(at, half * 3f);
        }

        void WriteRow(Vector3 at, Vector3 dir, float half, float amount, float at_)
        {
            var n = Slope.Normal(at.x, at.z);
            Vector3 f = Vector3.ProjectOnPlane(dir, n).normalized;
            if (f.sqrMagnitude < 0.5f) f = Vector3.forward;
            // mitre with the row before, so a turn doesn't fold the ribbon over itself
            Vector3 avg = segRow > 0 ? (rowDir + f).normalized : f;
            Vector3 right = Vector3.Cross(n, avg.sqrMagnitude > 0.1f ? avg : f).normalized;
            var lamp = Lamp(at);
            for (int c = 0; c < TrailCols; c++)
            {
                float x = TrailX[c];
                Vector3 p = at + right * (x * half);
                p = OnSnow(p);
                p.y += Lift;
                verts[seg.v0 + segRow * TrailCols + c] = new V
                {
                    pos = p, nrm = n,
                    uv0 = new Vector4(x, at_, trailSeed, (float)Kind.Trail),
                    uv1 = new Vector4(clock, 1.1f, amount, snowAcc - 0.04f),
                    uv2 = new Vector4(0f, 1f, 0f, half),
                    uv3 = lamp
                };
            }
            segRow++;
        }

        // ---------------------------------------------------------------- stains and drops

        /// <summary>
        /// A stain on the snow: centre, the way it was thrown (on the snow), radius (m), how far it is drawn out ahead,
        /// how much blood, how long it takes to soak in (s), the powder over it, and whether it is a pool still filling.
        /// </summary>
        void Stain(Vector3 c, Vector3 way, float radius, float thrown, float amount, float soak, float powder, float rest)
        {
            var s = Take(stains);
            var n = Slope.Normal(c.x, c.z);
            Vector3 f = Vector3.ProjectOnPlane(way, n);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(Vector3.forward, n);
            f.Normalize();
            Vector3 r = Vector3.Cross(n, f).normalized;
            float seed = Random.Range(0f, 50f);
            float ext = 2.5f + rest * 0.5f;
            float y0 = -ext, y1 = ext * (1f + thrown) + 0.2f;
            var lamp = Lamp(c);
            for (int j = 0; j < StainSide; j++)
                for (int i = 0; i < StainSide; i++)
                {
                    float x = Mathf.Lerp(-ext, ext, i / (float)(StainSide - 1));
                    float y = Mathf.Lerp(y0, y1, j / (float)(StainSide - 1));
                    Vector3 p = OnSnow(c + (r * x + f * y) * radius);
                    p.y += Lift;
                    verts[s.v0 + j * StainSide + i] = new V
                    {
                        pos = p, nrm = n,
                        uv0 = new Vector4(x, y, seed, (float)Kind.Stain),
                        uv1 = new Vector4(clock, soak, amount, snowAcc - powder),
                        uv2 = new Vector4(thrown, 1f, rest, 0f),
                        uv3 = lamp
                    };
                }
            s.live = true; s.born = clock; s.at = c; s.on = null;
            Dirty(s.v0, s.n);
            Grow(c, radius * (ext + y1));
        }

        /// <summary>
        /// A stain on a trunk (wrapped round it, `round` its radius) or a rock (bent over it): local +y up its face,
        /// so the runs drip down.
        /// </summary>
        void Wrapped(Vector3 at, Vector3 normal, float round, bool trunk, float radius, Collider on, float k)
        {
            var s = Take(stains);
            if (trunk) { normal.y = 0f; normal.Normalize(); }
            Vector3 right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(normal, right).normalized;
            radius = Mathf.Min(radius, round * (trunk ? 0.5f : 0.6f));
            Vector3 axis = at - normal * round;
            float seed = Random.Range(0f, 50f);
            float ext = 2.5f;
            var lamp = Lamp(at);
            for (int j = 0; j < StainSide; j++)
                for (int i = 0; i < StainSide; i++)
                {
                    float x = Mathf.Lerp(-ext, ext, i / (float)(StainSide - 1));
                    float y = Mathf.Lerp(-ext * 1.5f, ext, j / (float)(StainSide - 1));
                    Vector3 p, nr;
                    if (trunk)
                    {
                        float th = Mathf.Clamp(x * radius / round, -1.5f, 1.5f);
                        nr = normal * Mathf.Cos(th) + right * Mathf.Sin(th);
                        p = axis + nr * (round + 0.012f) + Vector3.up * (y * radius);
                        // onto the trunk's own post (its flats sit on the drawn bark)
                        if (on != null && on.Raycast(new Ray(p + nr * 0.6f, -nr), out var hit, 1.2f)) { p = hit.point + hit.normal * 0.01f; nr = hit.normal; }
                    }
                    else
                    {
                        nr = (normal * round + (right * x + up * y) * radius).normalized;
                        p = axis + nr * (round + 0.02f);
                    }
                    verts[s.v0 + j * StainSide + i] = new V
                    {
                        pos = p, nrm = nr,
                        uv0 = new Vector4(x, y, seed, (float)Kind.OnObstacle),
                        uv1 = new Vector4(clock, 0.9f, 0.85f + 0.15f * k, snowAcc),
                        uv2 = new Vector4(0f, 1f, 0f, 0f),
                        uv3 = lamp
                    };
                }
            s.live = true; s.born = clock; s.at = at; s.on = on;
            Dirty(s.v0, s.n);
            Grow(at, radius * 4f);
        }

        /// <summary>A stain on a rock, drawn over its own mesh: projected from out of its face at `at`, +y up the face.</summary>
        void OnRock(Vector3 at, Vector3 normal, float radius, Collider on, float k)
        {
            RockMark r = null;
            foreach (var x in rocks) if (!x.live) { r = x; break; }
            if (r == null) { r = rocks[0]; foreach (var x in rocks) if (x.born < r.born) r = x; }
            r.pieces.Clear();
            if (!forest.RockPieces(on, r.pieces)) { r.live = false; return; }
            Vector3 right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            Vector3 up = Vector3.Cross(normal, right.normalized).normalized;
            // (x and y in stain radii; z, out of the face, in metres: where the drawn rock really is, off the shape
            // fitted inside its bounds, is not known to the centimetre)
            var proj = Matrix4x4.TRS(at, Quaternion.LookRotation(normal, up), new Vector3(radius, radius, 1f)).inverse;
            r.mpb.SetMatrix("_BloodProj", proj);
            r.mpb.SetVector("_BloodMark", new Vector4(clock, 0.9f, 0.7f + 0.15f * k, Random.Range(0f, 50f)));
            r.mpb.SetVector("_BloodLamp", Lamp(at));
            r.live = true; r.born = clock; r.at = at; r.on = on;
        }

        /// <summary>A drop landed: a spot, drawn out ahead (+y) by how low it came in.</summary>
        void Spot(Vector3 at, Vector3 way, float size, float stretch)
        {
            var s = Take(spots);
            var n = Slope.Normal(at.x, at.z);
            Vector3 f = Vector3.ProjectOnPlane(way, n);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(Random.onUnitSphere, n);
            f.Normalize();
            Vector3 r = Vector3.Cross(n, f).normalized;
            float y1 = 1f + 2.4f * stretch + 0.3f;
            float seed = Random.Range(0f, 50f);
            var lamp = Lamp(at);
            for (int k = 0; k < 4; k++)
            {
                float x = (k & 1) == 0 ? -1f : 1f, y = (k & 2) == 0 ? -1f : y1;
                Vector3 p = OnSnow(at + (r * x + f * y) * size);
                p.y += Lift;
                verts[s.v0 + k] = new V
                {
                    pos = p, nrm = n,
                    uv0 = new Vector4(x, y, seed, (float)Kind.Drop),
                    uv1 = new Vector4(clock, 0.35f, Random.Range(0.75f, 1f), snowAcc - 0.02f),
                    uv2 = new Vector4(stretch, 1f, 0f, 0f),
                    uv3 = lamp
                };
            }
            s.live = true; s.born = clock; s.at = at; s.on = null;
            Dirty(s.v0, 4);
            Grow(at, size * (1f + y1));
        }

        // ---------------------------------------------------------------- slots

        /// <summary>A free slot, else the one best lost: far behind the camera first, then the oldest.</summary>
        Slot Take(Slot[] all)
        {
            Slot best = null; float bestScore = -1f;
            float cz = Cam() != null ? cam.transform.position.z : 0f;
            foreach (var s in all)
            {
                if (!s.live) return s;
                if (s == seg) continue;
                float score = (clock - s.born) + (s.at.z < cz - 25f ? 1e4f : 0f);
                if (score > bestScore) { bestScore = score; best = s; }
            }
            best = best ?? all[0];
            Kill(best);
            return best;
        }

        void Kill(Slot s)
        {
            if (!s.live && s.on == null) return;
            s.live = false; s.on = null;
            var z = default(V);
            z.pos = s.at; z.uv2.y = 0f;
            for (int i = 0; i < s.n; i++) verts[s.v0 + i] = z;
            Dirty(s.v0, s.n);
        }

        /// <summary>Take away marks far up the hill of the camera, and stains on a tree that is no longer there.</summary>
        void Sweep()
        {
            if (Cam() == null) return;
            float cz = cam.transform.position.z;
            bool any = false;
            void Check(Slot[] all) { foreach (var s in all) { if (!s.live) continue; if (s.at.z < cz - Behind || (s.on != null && (!s.on.enabled || !s.on.gameObject.activeInHierarchy))) { if (s != seg) Kill(s); } else any = true; } }
            Check(stains); Check(trails); Check(spots);
            foreach (var r in rocks)
                if (r.live && (r.at.z < cz - Behind || r.on == null || !r.on.enabled || !r.on.gameObject.activeInHierarchy)) { r.live = false; r.pieces.Clear(); }
            if (!any) { anyBounds = false; mr.enabled = false; }
        }

        Camera Cam() { if (cam == null) cam = Camera.main; return cam; }

        void Dirty(int v0, int n) { dirty0 = Mathf.Min(dirty0, v0); dirty1 = Mathf.Max(dirty1, v0 + n - 1); }

        void Grow(Vector3 at, float r)
        {
            var b = new Bounds(at, Vector3.one * (2f * r + 0.6f));
            if (!anyBounds) { bounds = b; anyBounds = true; } else bounds.Encapsulate(b);
            mr.enabled = true;
        }

        void Flush()
        {
            if (dirty1 < dirty0 || mesh == null) return;
            const MeshUpdateFlags F = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
            mesh.SetVertexBufferData(verts, dirty0, dirty0, dirty1 - dirty0 + 1, 0, F);
            if (anyBounds) mesh.bounds = bounds;
            dirty0 = int.MaxValue; dirty1 = -1;
        }

        // ---------------------------------------------------------------- the pieces

        static Vector3 OnSnow(Vector3 p) => new Vector3(p.x, Slope.DrawnHeight(p.x, p.z) - Course.CrevasseCut(p.x, p.z), p.z);

        /// <summary>The floodlight that lights this spot most (its lens) and how much of the light here is its, roughly.</summary>
        static Vector4 Lamp(Vector3 p)
        {
            if (NightLights.Instance == null) return Vector4.zero;
            Vector4 best = Vector4.zero;
            foreach (var l in NightLights.Lamps)
            {
                Vector3 L = l.head - p;
                float d2 = L.sqrMagnitude;
                if (d2 > l.range * l.range || d2 < 1e-3f) continue;
                float d = Mathf.Sqrt(d2);
                float f = d2 / (l.range * l.range), sm = Mathf.Clamp01(1f - f * f); sm *= sm;
                float cos = Vector3.Dot(-L / d, l.dir);
                float ang = Mathf.Clamp01((cos - l.CosOuter) / Mathf.Max(1e-3f, l.CosInner - l.CosOuter)); ang *= ang;
                float e = Mathf.Clamp01(64f / d2 * sm * ang);
                if (e > best.w) best = new Vector4(l.head.x, l.head.y, l.head.z, e);
            }
            return best;
        }

        /// <summary>
        /// The marks' noise, made once: four tileable layers. r: soft blotches (a stain's lobes), g: fine and sharp
        /// (the capillary fingers, the clothes' striations), b: middling (where powder lies over it), a: the snow's grain.
        /// </summary>
        static Texture2D noise;
        static Texture2D Noise()
        {
            if (noise == null) noise = Resources.Load<Texture2D>("Blood/BloodNoise");   // baked (BloodNoiseBake)
            if (noise == null) noise = MakeNoise();
            return noise;
        }

        public static Texture2D MakeNoise()
        {
            const int S = 256;
            var px = new Color32[S * S];
            var rng = new System.Random(1234);
            float[][] lat = new float[9][];
            for (int o = 0; o < 9; o++) { int p = 4 << o; if (p > S) p = S; lat[o] = new float[p * p]; for (int i = 0; i < lat[o].Length; i++) lat[o][i] = (float)rng.NextDouble(); }
            float Val(int o, float x, float y)
            {
                int p = Mathf.Min(4 << o, S);
                float fx = x * p / S, fy = y * p / S;
                int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
                float tx = fx - ix, ty = fy - iy;
                tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
                int x0 = ix % p, y0 = iy % p, x1 = (ix + 1) % p, y1 = (iy + 1) % p;
                var L = lat[o];
                float a = Mathf.Lerp(L[y0 * p + x0], L[y0 * p + x1], tx), b = Mathf.Lerp(L[y1 * p + x0], L[y1 * p + x1], tx);
                return Mathf.Lerp(a, b, ty);
            }
            float Fbm(float x, float y, int o0, int o1)
            {
                float s = 0f, w = 0.5f, ws = 0f;
                for (int o = o0; o <= o1; o++) { s += Val(o, x, y) * w; ws += w; w *= 0.55f; }
                return s / ws;
            }
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float r = Fbm(x, y, 0, 4), g = Fbm(x, y, 3, 6), b = Fbm(x, y, 2, 5), a = Fbm(x, y, 5, 6);
                    // stretch each to use the whole range (fbm bunches up round the middle)
                    byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt((0.5f + (v - 0.5f) * 2.1f) * 255f), 0, 255);
                    px[y * S + x] = new Color32(B(r), B(g), B(b), B(a));
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true, true) { name = "BloodNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 2 };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
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
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Fade(ParticleSystem ps)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            c.color = g;
        }
    }
}
