using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Young, thin trees and dead snags snap when you hit them fast enough (big old trunks stand: see
    /// Forest.Snappable). The trunk breaks at chest height, leaving a jagged stump in the snow; the top falls
    /// over, dumping its snow, and lies where it lands. You still go down, but you carry on through where it
    /// stood instead of stopping dead against it: the thrown body keeps RiderKeep of your speed. A shield takes
    /// the hit instead (you plough on, slowed, wobbling), and the thrown body snaps them too.
    ///
    /// The forest is drawn in instanced batches; only the hit tree is taken out of it (Forest.Snap), and its
    /// stump is drawn there by the forest from then on. The falling tops are a small pool of two-point rods
    /// (butt and tip, a fixed distance apart, gravity, the snow under each end), drawn with the tree's own mesh.
    /// </summary>
    public class TreeBreak : MonoBehaviour
    {
        public static TreeBreak Instance { get; private set; }

        public const float SnapSpeed = 22f;     // m/s into the trunk (79 km/h): a young tree snaps
        public const float SnagSpeed = 19.5f;   // (70 km/h): a dead snag, dry and brittle, goes sooner
        public const float RiderKeep = 0.6f;    // the thrown body carries this share of your speed on through
        public const float ShieldKeep = 0.75f;  // a shield took it: you plough on with this share
        public const float BodyKeep = 0.6f;     // a tumbling body through one keeps this share

        /// <summary>A tree snapped: where it broke, how fast it was hit (m/s), and whether the rider (not the thrown body) did it.</summary>
        public System.Action<Vector3, float, bool> Snapped;

        Forest forest; SnowFX fx; Smasher smasher;
        readonly Collider[] near = new Collider[24];
        readonly List<Forest.Piece> pieces = new List<Forest.Piece>();

        class Top
        {
            public bool on, landed;
            public readonly List<Forest.Piece> parts = new List<Forest.Piece>();
            public Vector3 a0, u0, a, b, va, vb, stump;
            public float len, girth, stumpTop, t, snow, landedAt;
            public MaterialPropertyBlock props;
        }
        const int Pool = 6;
        readonly Top[] tops = new Top[Pool];

        public void Build(Forest trees, SnowFX snow, Smasher smash)
        {
            forest = trees; fx = snow; smasher = smash;
            for (int i = 0; i < Pool; i++) tops[i] = new Top { props = new MaterialPropertyBlock() };
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------------------------------------------------------------- hits

        /// <summary>
        /// Each physics step while riding (Rider.Tick): a snappable tree about to be hit this fast snaps before the
        /// board meets it. You go down (or a shield takes it), and carry on. True if one snapped.
        /// </summary>
        public bool RiderAhead(Rider rider, Vector3 p, Vector3 v, float dt)
        {
            var flat = new Vector3(v.x, 0f, v.z);
            if (flat.sqrMagnitude < SnagSpeed * SnagSpeed) return false;   // too slow to snap anything
            int k = Physics.OverlapCapsuleNonAlloc(p, p + v * dt + Vector3.up * 0.5f, Rider.R + 0.35f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++)
            {
                if (near[i].attachedRigidbody == rider.Body) continue;
                if (!TrySnap(near[i], p, flat, out float into, out Vector3 at)) continue;
                rider.CrashCarry = RiderKeep;   // read by the thrown body as it is thrown (RiderBody)
                rider.Breakthrough(ShieldKeep, 0.8f, into, 0f, "Tree snapped!");
                rider.CrashCarry = 1f;
                Snapped?.Invoke(at, into, true);
                return true;
            }
            return false;
        }

        /// <summary>Each physics step of a thrown body (Ragdoll): one tumbling into a tree this fast snaps it. True if one snapped.</summary>
        public bool BodyAhead(Vector3 p, Vector3 v)
        {
            var flat = new Vector3(v.x, 0f, v.z);
            if (flat.sqrMagnitude < SnagSpeed * SnagSpeed) return false;
            int k = Physics.OverlapCapsuleNonAlloc(p, p + v * 0.03f, 0.6f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++)
            {
                if (!TrySnap(near[i], p, flat, out float into, out Vector3 at)) continue;
                Snapped?.Invoke(at, into, false);
                return true;
            }
            return false;
        }

        bool TrySnap(Collider col, Vector3 from, Vector3 flat, out float into, out Vector3 at)
        {
            into = 0f; at = Vector3.zero;
            if (forest == null || col == null || !col.enabled) return false;
            var ob = col.GetComponent<Obstacle>();
            if (ob == null || ob.always || ob.label != "Tree") return false;
            if (!forest.Snappable(col.gameObject, out bool snag)) return false;
            // the speed into the trunk, not past it: a glancing blow is the forest's usual scrape or crash
            Vector3 to = col.transform.position - from; to.y = 0f;
            into = to.sqrMagnitude > 1e-4f ? Vector3.Dot(flat, to.normalized) : flat.magnitude;
            if (into < (snag ? SnagSpeed : SnapSpeed)) return false;
            pieces.Clear();
            float breakAt = Random.Range(0.55f, 0.95f) + (snag ? 0.3f : 0f);   // about chest height
            if (!forest.Snap(col.gameObject, breakAt, pieces, out var s)) return false;
            at = s.foot + s.up * s.at;
            Throw(s, flat, into);
            // the branches dump their snow (and leave a heap of it round the stump), and the trunk bursts into
            // splinters where it went
            Vector3 dir = flat.normalized;
            fx?.TreeDump(s.foot, s.vis, Mathf.Max(1f, s.vis * 0.17f), 16f);
            AddHeap(s.foot, Mathf.Max(0.5f, s.girth));
            fx?.Impact(at, 9f, flat * 0.2f);
            smasher?.Splinters(at, dir, 120, into);
            return true;
        }

        void Throw(Forest.Snapped s, Vector3 flat, float into)
        {
            // the oldest top makes way if all are in use
            Top top = null;
            foreach (var t in tops) if (!t.on) { top = t; break; }
            if (top == null) { top = tops[0]; foreach (var t in tops) if (t.t > top.t) top = t; }
            top.on = true; top.landed = false; top.t = 0f; top.snow = 1f; top.landedAt = -1f;
            top.parts.Clear(); top.parts.AddRange(pieces);
            top.u0 = s.up;
            top.a0 = top.a = s.foot + s.up * s.at;
            top.len = Mathf.Max(1f, s.vis - s.at);
            top.b = top.a + s.up * top.len;
            top.girth = s.girth; top.stump = s.foot; top.stumpTop = top.a0.y;
            float cut = pieces.Count > 0 ? pieces[0].cut : 0f;
            top.props.SetFloat(CutY, cut > 0.01f ? cut : -10000f);
            top.props.SetFloat(Sway, 0f);   // a broken top lies still (TreeWind)
            top.props.SetFloat(SnowLeft, 1f);
            // rammed: the butt is knocked on off the stump and the crown goes over ahead and to one side, away
            // from the camera following the body along the same line (and the body is long gone by the time it lands)
            Vector3 dir = flat.normalized, side = Vector3.Cross(Vector3.up, dir) * (Random.value < 0.5f ? -1f : 1f);
            Vector3 fall = (side * 0.6f + dir * 0.8f).normalized;
            top.va = dir * into * 0.3f + Vector3.up * 0.8f;
            top.vb = fall * into * Random.Range(0.4f, 0.48f);
        }

        // ---------------------------------------------------------------- the falling tops

        const float Gravity = 14f;   // a little more than real: a tall top takes its time going over

        void Update() { Step(Time.deltaTime); Draw(); }

        /// <summary>How many tops are lying or falling, and how many still moving (for the test tools).</summary>
        public int Count { get { int n = 0; foreach (var t in tops) if (t.on) n++; return n; } }
        public int Moving { get { int n = 0; foreach (var t in tops) if (t.on && (t.va.sqrMagnitude > 0.04f || t.vb.sqrMagnitude > 0.04f)) n++; return n; } }

        /// <summary>The first top's butt and tip (for the test tools).</summary>
        public bool TopEnds(out Vector3 butt, out Vector3 tip)
        {
            foreach (var t in tops) if (t.on) { butt = t.a; tip = t.b; return true; }
            butt = tip = Vector3.zero; return false;
        }

        /// <summary>Moves everything on (public so the test tools can drive it).</summary>
        public void Step(float dt)
        {
            if (dt <= 0f) return;
            Vector3 far = forest != null && forest.target != null ? forest.target.position : Vector3.zero;
            foreach (var top in tops)
            {
                if (!top.on) continue;
                top.t += dt;
                // lying still well behind you: done with
                if (forest != null && forest.target != null && (top.a - far).sqrMagnitude > 240f * 240f) { top.on = false; continue; }
                // the snow lets go as it goes over: shed along the crown, a curtain of powder behind it (SnowFX)
                if (top.snow > 0.12f)
                {
                    fx?.TreeTrail(top.a, top.b, top.vb, dt, top.snow);
                    top.snow = Mathf.Max(0.12f, top.snow - dt / (top.landed ? 0.4f : 1.4f));
                    top.props.SetFloat(SnowLeft, top.snow);
                }
                if (top.va.sqrMagnitude + top.vb.sqrMagnitude < 1e-4f && top.t > 1f) continue;   // at rest
                int n = Mathf.CeilToInt(dt / (1f / 90f));
                float h = dt / n;
                for (int s = 0; s < n; s++) Sub(top, h);
            }
        }

        void Sub(Top top, float h)
        {
            Vector3 pa = top.a, pb = top.b;
            top.va.y -= Gravity * h; top.vb.y -= Gravity * h;
            top.a += top.va * h; top.b += top.vb * h;
            // the rod stays its length
            for (int it = 0; it < 2; it++)
            {
                Vector3 d = top.b - top.a; float l = d.magnitude;
                if (l > 1e-4f) { Vector3 c = d * ((l - top.len) / l * 0.5f); top.a += c; top.b -= c; }
                // the butt rests on the stump while it is over it, then on the snow; the crown on the snow
                bool onStump = Flat(top.a - top.stump) < top.girth * 1.4f;
                float ga = Mathf.Max(Slope.DrawnHeight(top.a.x, top.a.z) + top.girth, onStump ? top.stumpTop : float.MinValue);
                if (top.a.y < ga) top.a.y = ga;
                float gb = Slope.DrawnHeight(top.b.x, top.b.z) + 0.35f;
                if (top.b.y < gb) top.b.y = gb;
                // the middle of the crown on the snow too: it lies on its branches, not through them
                Vector3 m = Vector3.Lerp(top.a, top.b, 0.4f);
                float gm = Slope.DrawnHeight(m.x, m.z) + 0.25f;
                if (m.y < gm) { float lift = gm - m.y; top.a.y += lift * 0.6f; top.b.y += lift * 0.4f; }
            }
            Vector3 na = (top.a - pa) / h, nb = (top.b - pb) / h;
            // on the snow: it skids to a stop
            bool aDown = top.a.y <= Slope.DrawnHeight(top.a.x, top.a.z) + top.girth + 0.02f;
            bool bDown = top.b.y <= Slope.DrawnHeight(top.b.x, top.b.z) + 0.37f;
            if (bDown && !top.landed && top.vb.y < -3f)
            {
                // the crown hits the snow: it throws up a cloud along its length
                top.landed = true; top.landedAt = top.t;
                float force = Mathf.Min(16f, -top.vb.y);
                fx?.TopLands(top.a, top.b, force);
            }
            if (aDown) na *= Mathf.Exp(-h * 10f);
            if (bDown) nb *= Mathf.Exp(-h * 12f);
            top.va = na; top.vb = nb;
            // lying in the snow, slowed right down: the branches dig in and it stays put, even on a steep pitch
            if (aDown && bDown && na.sqrMagnitude + nb.sqrMagnitude < 2.5f && top.t > 0.8f) top.va = top.vb = Vector3.zero;
        }

        static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);

        public void Draw()
        {
            DrawHeaps();
            foreach (var top in tops)
            {
                if (!top.on) continue;
                // the pieces' own matrices put them where the tree stood: take the break point out, turn, put back
                var rot = Quaternion.FromToRotation(top.u0, (top.b - top.a).normalized);
                var M = Matrix4x4.TRS(top.a, rot, Vector3.one) * Matrix4x4.Translate(-top.a0);
                foreach (var p in top.parts)
                {
                    var rp = new RenderParams(p.mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, matProps = top.props };
                    Graphics.RenderMesh(rp, p.mesh, p.sub, M * p.m);
                }
                // the broken end: splintered wood across the open trunk, teeth down
                if (StumpMesh != null && woodMat != null)
                {
                    var cap = M * Matrix4x4.TRS(top.a0 + top.u0 * (0.3f * StumpTop / 0.86f), Quaternion.FromToRotation(Vector3.up, -top.u0), new Vector3(top.girth * 0.95f, 0.3f / StumpTop, top.girth * 0.95f));
                    Graphics.RenderMesh(new RenderParams(woodMat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, StumpMesh, 1, cap);
                }
            }
        }

        public void Clear() { foreach (var t in tops) t.on = false; heaps.Clear(); }

        // ---------------------------------------------------------------- the snow left at a broken tree's foot

        readonly List<Matrix4x4> heaps = new List<Matrix4x4>();
        static Mesh heapMesh;
        static Material heapMat;

        /// <summary>The snow off a snapped or smashed tree, heaped round where it stood (girth: the trunk's radius).</summary>
        public void AddHeap(Vector3 foot, float girth)
        {
            if (heaps.Count >= 24) heaps.RemoveAt(0);
            float r = Mathf.Clamp(girth * 3f, 1.1f, 1.8f) * Random.Range(0.9f, 1.15f);
            float g = Slope.DrawnHeight(foot.x, foot.z);
            heaps.Add(Matrix4x4.TRS(new Vector3(foot.x, g - 0.05f, foot.z), Quaternion.FromToRotation(Vector3.up, Slope.Normal(foot.x, foot.z)) * Quaternion.Euler(0f, Random.value * 360f, 0f),
                                    new Vector3(r, r * Random.Range(0.32f, 0.4f), r * Random.Range(0.8f, 1f))));
        }

        void DrawHeaps()
        {
            if (heaps.Count == 0) return;
            if (heapMesh == null) heapMesh = BuildHeap();
            if (heapMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) return;
                heapMat = new Material(sh) { name = "SnowHeap", enableInstancing = true };
                heapMat.SetColor("_BaseColor", new Color(0.93f, 0.95f, 0.98f)); heapMat.SetFloat("_Smoothness", 0.18f);
            }
            Graphics.RenderMeshInstanced(new RenderParams(heapMat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, heapMesh, 0, heaps);
        }

        /// <summary>A lumpy, low dome of snow, radius 1 and about 1 high (scaled down flat where it is used).</summary>
        static Mesh BuildHeap()
        {
            var rs = Random.state; Random.InitState(9173);
            const int rings = 6, segs = 14;
            var v = new List<Vector3>(); var t = new List<int>();
            var bump = new float[segs];
            for (int j = 0; j < segs; j++) bump[j] = Random.Range(0.85f, 1.15f);
            for (int i = 0; i <= rings; i++)
            {
                float a = i / (float)rings * Mathf.PI * 0.5f;
                for (int j = 0; j < segs; j++)
                {
                    float b = j / (float)segs * Mathf.PI * 2f, lump = Mathf.Lerp(bump[j], 1f, i / (float)rings);
                    v.Add(new Vector3(Mathf.Cos(b) * Mathf.Cos(a) * lump, Mathf.Sin(a) * Random.Range(0.9f, 1.1f), Mathf.Sin(b) * Mathf.Cos(a) * lump));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a0 = i * segs + j, a1 = i * segs + (j + 1) % segs, b0 = a0 + segs, b1 = a1 + segs;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
            var m = new Mesh { name = "SnowHeap" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            Random.state = rs;
            return m;
        }

        static readonly int CutY = Shader.PropertyToID("_CutY"), Sway = Shader.PropertyToID("_Sway"), SnowLeft = Shader.PropertyToID("_Snow");

        // ---------------------------------------------------------------- stumps

        /// <summary>The stump mesh's height at its break line (the teeth reach a little above and below).</summary>
        public const float StumpTop = 0.86f;
        static Mesh StumpMesh;
        static Material woodMat, barkMat, classicBark;

        /// <summary>The forest's stumps of snapped trees, in one instanced batch per material (Forest.Draw).</summary>
        public static void DrawStumps(List<Matrix4x4> at, bool realistic, Material bark)
        {
            if (!Materials(realistic, bark)) return;
            var b = realistic ? barkMat : classicBark;
            var rpw = new RenderParams(woodMat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            var rpb = new RenderParams(b) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            Graphics.RenderMeshInstanced(rpb, StumpMesh, 0, at);
            Graphics.RenderMeshInstanced(rpw, StumpMesh, 1, at);
        }

        static bool Materials(bool realistic, Material bark)
        {
            if (StumpMesh == null) StumpMesh = BuildStump();
            if (woodMat == null && bark != null)
            {
                // fresh-broken wood: pale and warm against the dark bark
                woodMat = new Material(bark) { name = "SnappedWood", enableInstancing = true };
                woodMat.SetTexture("_BaseMap", null); woodMat.SetTexture("_MainTex", null);
                woodMat.SetColor("_BaseColor", new Color(0.62f, 0.50f, 0.36f)); woodMat.SetColor("_Color", new Color(0.62f, 0.50f, 0.36f));
                woodMat.SetFloat("_Smoothness", 0.05f);
                woodMat.SetFloat("_Cull", 0f);   // both sides: the teeth and splinters are single sheets
            }
            if (classicBark == null && bark != null) { classicBark = new Material(bark) { name = "StumpBark", enableInstancing = true }; classicBark.SetFloat("_Cull", 0f); }
            if (barkMat == null && RealFir.Ready) barkMat = RealFir.Standalone;   // the realistic trunks' own bark (atlas)
            if (barkMat == null) barkMat = classicBark;
            return woodMat != null && (realistic ? barkMat : classicBark) != null;
        }

        /// <summary>
        /// A jagged stump, radius 1, from y 0 up to about StumpTop: bark round the side (sub-mesh 0, on the
        /// realistic trees' atlas strip), and torn pale wood across the top in teeth and splinters (sub-mesh 1).
        /// </summary>
        static Mesh BuildStump()
        {
            var rs = Random.state; Random.InitState(4711);
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var bark = new List<int>(); var wood = new List<int>();
            const int sides = 9;
            var rim = new float[sides];
            for (int i = 0; i < sides; i++) rim[i] = StumpTop + (i % 2 == 0 ? 0.1f : -0.1f) + Random.Range(-0.08f, 0.1f);
            // the bark strip in the realistic atlas (RealFir: row 3, 958 px wide), a short run of it along the height
            const float u0 = 0.05f, u1 = 0.13f, v0 = 0.002f, v1 = 0.248f;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                int s = v.Count;
                v.Add(d0); v.Add(d1); v.Add(d0 * 0.97f + Vector3.up * rim[i]); v.Add(d1 * 0.97f + Vector3.up * rim[j]);
                n.Add(d0); n.Add(d1); n.Add(d0); n.Add(d1);
                float va = Mathf.Lerp(v0, v1, i / (float)sides), vb = Mathf.Lerp(v0, v1, (i + 1) / (float)sides);
                uv.Add(new Vector2(u0, va)); uv.Add(new Vector2(u0, vb)); uv.Add(new Vector2(u1, va)); uv.Add(new Vector2(u1, vb));
                bark.Add(s); bark.Add(s + 2); bark.Add(s + 1); bark.Add(s + 1); bark.Add(s + 2); bark.Add(s + 3);
            }
            // the torn top: a fan from a low middle up to the ragged rim, and a few long splinters standing up
            Vector3 mid = Vector3.up * (StumpTop - 0.12f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.97f + Vector3.up * rim[i];
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.97f + Vector3.up * rim[j];
                Tri(v, n, uv, wood, mid, p0, p1);
            }
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 97f + Random.Range(0f, 40f)) * Mathf.Deg2Rad, w = Random.Range(0.25f, 0.45f);
                var c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(0.35f, 0.75f);
                var t = new Vector3(-c.z, 0f, c.x).normalized * w;
                var tip = c * 1.05f + Vector3.up * (StumpTop + Random.Range(0.3f, 0.6f));
                Vector3 b0 = c - t + Vector3.up * (StumpTop - 0.1f), b1 = c + t + Vector3.up * (StumpTop - 0.1f);
                Tri(v, n, uv, wood, b0, tip, b1);
                Tri(v, n, uv, wood, b0, b1, tip);
            }
            var m = new Mesh { name = "SnappedStump" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = 2; m.SetTriangles(bark, 0); m.SetTriangles(wood, 1);
            m.RecalculateBounds();
            Random.state = rs;
            return m;
        }

        static void Tri(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c)
        {
            int s = v.Count;
            var nn = Vector3.Cross(b - a, c - a).normalized;
            if (nn.y < 0f) nn = -nn;   // lit as if facing up and out (it is drawn from both sides by the bark shader; the wood faces the sky)
            v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
            uv.Add(new Vector2(0.1f, 0.1f)); uv.Add(new Vector2(0.12f, 0.12f)); uv.Add(new Vector2(0.11f, 0.1f));
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
        }
    }
}
