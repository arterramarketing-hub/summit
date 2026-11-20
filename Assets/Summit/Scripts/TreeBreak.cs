using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// What a rider meets in a tree, from the tree as it is drawn (Forest.Tree):
    ///  - its trunk is solid (Forest's hit post, nothing wider): ridden into, you crash, unless a fast enough hit
    ///    snaps a young trunk or a dead snag (SnapSpeed: you still go down, but carry on through);
    ///  - a trunk thinner than Forest.ThinGirth snaps and you ride on: a small tree at the snow, or the thin top of a
    ///    tall one met in the air (the top comes away and falls with its snow, the tree stands on, broken off);
    ///  - its branches are no wall: riding into them tears some out (a gap stays in the tree), brings their snow
    ///    and needles down on that side, cracks, costs a little speed and knocks you off your line and balance, the
    ///    more the deeper in you went.
    /// Each physics step (Rider.Tick) looks at the trees round the rider before the step moves it, so a thin trunk
    /// goes before the board meets its post. A thrown body snaps them too.
    ///
    /// The forest is drawn in instanced batches; only a hit tree is taken out of them (Forest.Snap, ClipTop,
    /// TearBranches), and drawn on its own from then on with its damage. The falling tops are a small pool of
    /// two-point rods (butt and tip, a fixed distance apart, gravity, the snow under each end), drawn with the
    /// tree's own mesh; the torn-off branches a small pool of sprigs, drawn in one instanced batch.
    /// </summary>
    public class TreeBreak : MonoBehaviour
    {
        public static TreeBreak Instance { get; private set; }

        public const float SnapSpeed = 22f;     // m/s into the trunk (79 km/h): a young tree snaps
        public const float SnagSpeed = 19.5f;   // (70 km/h): a dead snag, dry and brittle, goes sooner
        public const float RiderKeep = 0.6f;    // the thrown body carries this share of your speed on through
        public const float ShieldKeep = 0.75f;  // a shield took it: you plough on with this share
        public const float BodyKeep = 0.6f;     // a tumbling body through one keeps this share

        /// <summary>Faster than this (m/s into it) a thin trunk snaps; slower, it bends and its post stops you (a scrape).</summary>
        public const float ThinSpeed = 4f;
        /// <summary>A brush through branches: speed lost on contact (share, at the tips .. right in by the trunk), and
        /// as you push on through them (per second, at full depth).</summary>
        public const float BrushNick = 0.012f, BrushNickDeep = 0.03f, BrushDrag = 0.5f;

        /// <summary>A tree snapped: where it broke, how fast it was hit (m/s), and whether the rider (not the thrown body) did it.</summary>
        public System.Action<Vector3, float, bool> Snapped;
        /// <summary>A thin trunk ridden through, no crash: where it broke, how fast (m/s into it), and whether it was a
        /// tall tree's top (or a small tree).</summary>
        public System.Action<Vector3, float, bool> Broke;
        /// <summary>Branches brushed through, no crash: where, the way out of the tree toward the rider, and how deep in
        /// (0 the tips .. 1 by the trunk). Again as you go deeper into the same tree.</summary>
        public System.Action<Vector3, Vector3, float> Brushed;

        Forest forest; SnowFX fx; Smasher smasher;
        readonly Collider[] near = new Collider[24];
        readonly List<Forest.Piece> pieces = new List<Forest.Piece>();
        readonly List<Forest.Tree> trees = new List<Forest.Tree>();

        // the trees being brushed through: the deepest in so far, and when last touched
        struct Brush { public float deepest, last, speedIn, lost; }
        readonly Dictionary<Vector2Int, Brush> brushing = new Dictionary<Vector2Int, Brush>();
        readonly List<Vector2Int> stale = new List<Vector2Int>();
        float clock;

        /// <summary>A tree's branches left behind: how deep in you went, how fast you came in and the speed they cost (m/s), for the test tools.</summary>
        public System.Action<float, float, float> BrushDone;
        /// <summary>What the rider has done to the forest this run (for the test tools).</summary>
        public int Brushes, DeepBrushes, Tops, SmallTrees, TrunkSnaps;
        /// <summary>The speed the brushes have cost this run, m/s in all (for the test tools).</summary>
        public float BrushLoss;

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
            for (int i = 0; i < SprigPool; i++) sprigs[i] = new Sprig();
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------------------------------------------------------------- hits

        /// <summary>
        /// Each physics step while riding (Rider.Tick), before the step moves the rider: what it is about to meet in
        /// the trees. Thin trunks snap, branches are brushed through (costing speed and knocking the line), and a
        /// young trunk or snag hit fast enough snaps (you go down, or a shield takes it, and carry on). Hands back
        /// the velocity to ride on with. Invincible, the trunks are smashed (Rider.SmashAhead): here the branches
        /// and tops still break round you, at no cost.
        /// </summary>
        public Vector3 RiderAhead(Rider rider, Vector3 p, Vector3 v, float dt)
        {
            if (!Profile) return Ahead(rider, p, v, dt);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            v = Ahead(rider, p, v, dt);
            ProfileTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; ProfileCalls++;
            return v;
        }

        /// <summary>The test tools: time RiderAhead (Stopwatch ticks in all, and how many calls).</summary>
        public static bool Profile; public static long ProfileTicks; public static int ProfileCalls;

        Vector3 Ahead(Rider rider, Vector3 p, Vector3 v, float dt)
        {
            if (forest == null) return v;
            clock += dt;
            bool smash = rider.InvincibleTime > 0f;
            var flat = new Vector3(v.x, 0f, v.z);
            Vector3 q = p + v * dt;   // where the board will be at the end of this step
            forest.TreesNear(q, trees);
            for (int i = 0; i < trees.Count; i++)
            {
                var t = trees[i];
                // the nearest the board comes to the trunk over this step (a fast board crosses a thin one in a step)
                Vector3 seg = q - p; seg.y = 0f;
                Vector3 toAxis = t.at - p; toAxis.y = 0f;
                float along = seg.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(toAxis, seg) / seg.sqrMagnitude) : 1f;
                Vector3 rel = Vector3.Lerp(p, q, along) - t.at;
                float h = Vector3.Dot(rel, t.up);
                if (h < -0.6f || h > t.top + 1.4f) continue;
                Vector3 out_ = rel - t.up * h; float d = out_.magnitude;
                Vector3 away = d > 1e-3f ? out_ / d : -flat.normalized;
                float into = Mathf.Max(0f, -Vector3.Dot(flat, new Vector3(away.x, 0f, away.z).normalized));
                float trunk = Forest.TrunkAt(t, h);
                // a thin trunk at the board: a small tree, or (up off the snow, above the solid post) a tall tree's top.
                // In the top, the short branches round its leader break with it
                bool thinHere = h <= t.top && (t.girth < Forest.ThinGirth || h > t.solid);
                float reach = trunk + Rider.R + 0.05f + (h > t.solid ? 0.45f * Forest.CrownAt(t, h) : 0f);
                if (thinHere && d < reach && (into > ThinSpeed || h > t.solid + 0.3f) && !(smash && h <= t.solid))
                {
                    if (BreakThin(t, h, flat, Mathf.Max(into, flat.magnitude * 0.5f), true))
                    {
                        if (!smash)
                        {
                            float k = Mathf.Clamp01(trunk / Forest.ThinGirth);
                            float keep = (h > t.solid ? 0.93f : 0.9f) - 0.12f * k;
                            v += new Vector3(away.x, 0f, away.z) * -0.5f;   // you plough on through where it stood, a little thrown
                            // the speed goes over a fifth of a second as the board ploughs through (as a sign's does)
                            rider.Breakthrough(keep, 0.35f + 0.4f * k, 0f, float.MaxValue, "Tree");
                        }
                        trees.Clear();
                        return v;
                    }
                }
                // the branches, from the board to the shoulders: how far into them, at the deepest
                float depth = 0f; Vector3 deepAt = q; float deepH = h;
                for (int s = 0; s < 3; s++)
                {
                    float hs = h + s * 0.55f;
                    float c = Forest.CrownAt(t, hs);
                    if (c <= 0.05f) continue;
                    Vector3 o = rel + Vector3.up * (s * 0.55f) - t.up * hs;
                    float ds = new Vector3(o.x, 0f, o.z).magnitude;
                    float dep = (c + 0.2f - ds) / (c + 0.2f - trunk);
                    if (dep > depth) { depth = dep; deepAt = q + Vector3.up * (s * 0.55f); deepH = hs; }
                }
                if (depth <= 0.04f) continue;
                depth = Mathf.Clamp01(depth);
                v = BrushStep(rider, t, v, dt, depth, deepAt, deepH, away, smash);
                flat = new Vector3(v.x, 0f, v.z);
            }
            trees.Clear();
            // forget the trees left behind
            stale.Clear();
            foreach (var kv in brushing) if (clock - kv.Value.last > 0.6f) stale.Add(kv.Key);
            foreach (var k in stale) { var b = brushing[k]; BrushDone?.Invoke(b.deepest, b.speedIn, b.lost); brushing.Remove(k); }
            if (smash) return v;
            // a young trunk or a snag, hit fast: it snaps, but you go down (Snappable; the solid post you would hit)
            if (flat.sqrMagnitude < SnagSpeed * SnagSpeed) return v;   // too slow to snap anything
            int n = Physics.OverlapCapsuleNonAlloc(p, p + v * dt + Vector3.up * 0.5f, Rider.R + 0.35f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (near[i].attachedRigidbody == rider.Body) continue;
                if (!TrySnap(near[i], p, flat, out float into, out Vector3 at)) continue;
                TrunkSnaps++;
                rider.CrashCarry = RiderKeep;   // read by the thrown body as it is thrown (RiderBody)
                rider.Breakthrough(ShieldKeep, 0.8f, into, 0f, "Tree snapped!");
                rider.CrashCarry = 1f;
                Snapped?.Invoke(at, into, true);
                return v;
            }
            return v;
        }

        /// <summary>One step in a tree's branches, `depth` in (0 the tips .. 1 the trunk): the first touch and every
        /// push deeper tear branches out (a gap left in the tree, needles, snow, sprigs flying, the event the game
        /// sounds and shakes on); on through them, they drag.</summary>
        Vector3 BrushStep(Rider rider, in Forest.Tree t, Vector3 v, float dt, float depth, Vector3 at, float h, Vector3 away, bool smash)
        {
            bool fresh = !brushing.TryGetValue(t.cell, out var b);
            bool deeper = !fresh && depth > b.deepest + 0.28f;
            b.last = clock;
            if (fresh) b.speedIn = v.magnitude;
            if (fresh || deeper)
            {
                b.deepest = Mathf.Max(b.deepest, depth);
                Brushes++; if (depth > 0.5f) DeepBrushes++;
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                float speed = flat.magnitude;
                // where the branches were met: on the crown's skin, on the line from the trunk out to the rider
                float c = Forest.CrownAt(t, h);
                Vector3 hit = t.at + t.up * h + new Vector3(away.x, 0f, away.z).normalized * Mathf.Max(Forest.TrunkAt(t, h) + 0.2f, c * (1f - 0.6f * depth));
                hit = Vector3.Lerp(hit, at, 0.35f);
                forest.TearBranches(t, hit, 0.6f + 0.9f * depth);
                TreeWind.Shake(t.at, flat, 3f + 9f * depth);
                Vector3 side = new Vector3(away.x, 0f, away.z).normalized;
                fx?.BranchShower(hit, side, flat, Mathf.Max(0.8f, c), Mathf.Min(4f, t.top - h + 0.5f), depth);
                smasher?.Twigs(hit, flat.sqrMagnitude > 0.1f ? flat.normalized : side, Mathf.RoundToInt(14f + 40f * depth), speed);
                Fling(t, hit, side, flat, 1 + Mathf.RoundToInt(3f * depth));
                if (!smash)
                {
                    // a nick of speed and a shove off your line, away from the trunk, and a wobble, all by how deep
                    float nick = Mathf.Lerp(BrushNick, BrushNickDeep, depth);
                    BrushLoss += v.magnitude * nick; b.lost += v.magnitude * nick;
                    v *= 1f - nick;
                    float shove = 0.35f + 1.6f * depth;
                    float before = Mathf.Atan2(v.x, v.z);
                    v += side * shove;
                    float turned = Mathf.DeltaAngle(before * Mathf.Rad2Deg, Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    if (!rider.Airborne) rider.Heading += Mathf.Clamp(turned, -0.15f, 0.15f);
                    rider.Breakthrough(1f, 0.12f + 0.5f * depth, 0f, float.MaxValue, "Tree");
                }
                Brushed?.Invoke(hit, side, depth);
            }
            if (!smash)
            {
                // pushing on through them
                float s0 = v.magnitude;
                v *= Mathf.Exp(-BrushDrag * depth * dt);
                BrushLoss += s0 - v.magnitude; b.lost += s0 - v.magnitude;
            }
            brushing[t.cell] = b;
            return v;
        }

        /// <summary>A thin trunk met at h metres up: a small tree snaps there (a stump left), a tall one loses its top
        /// above. The top falls with its snow, splinters fly. True if it broke.</summary>
        bool BreakThin(in Forest.Tree t, float h, Vector3 flat, float into, bool byRider)
        {
            pieces.Clear();
            bool top = t.girth >= Forest.ThinGirth;
            float at = top ? Mathf.Max(t.solid, h - 0.3f) : Mathf.Clamp(h + 0.25f, 0.35f, Mathf.Max(0.35f, t.top - 1f));
            if (t.top - at < 0.8f) return false;   // only the very tip: a brush
            Forest.Snapped s;
            if (top ? !forest.ClipTop(t.cell, at, pieces, out s) : !forest.Snap(t.cell, at, pieces, out s)) return false;
            if (top) Tops++; else SmallTrees++;
            Vector3 dir = flat.sqrMagnitude > 0.1f ? flat.normalized : Vector3.forward;
            Vector3 brk = s.foot + s.up * s.at;
            Throw(s, flat, Mathf.Max(into, 8f));
            float crown = 0f;
            for (int i = 1; i <= 4; i++) crown = Mathf.Max(crown, Forest.CrownAt(t, t.top * i / 5f));
            if (top) fx?.TreeDump(t.at, t.top, Mathf.Max(0.8f, crown), 10f);   // the whole tree shakes as its top goes
            else { fx?.TreeDump(s.foot, s.vis, Mathf.Max(0.8f, crown), 12f); AddHeap(s.foot, Mathf.Max(0.4f, s.girth)); }
            TreeWind.Shake(t.at, flat, 12f);
            fx?.Impact(brk, top ? 5f : 7f, flat * 0.2f);
            smasher?.Splinters(brk, dir, top ? 50 : 70, into * 0.7f);
            smasher?.Twigs(brk, dir, 40, into);
            Fling(t, brk + Vector3.up * 0.4f, Vector3.Cross(Vector3.up, dir), flat, 4);
            Broke?.Invoke(brk, into, top);
            return true;
        }

        /// <summary>Each physics step of a thrown body (Ragdoll): one tumbling into a tree this fast snaps it, and a
        /// thin trunk goes much sooner. True if one snapped.</summary>
        public bool BodyAhead(Vector3 p, Vector3 v)
        {
            var flat = new Vector3(v.x, 0f, v.z);
            if (forest != null && flat.sqrMagnitude > ThinSpeed * ThinSpeed * 2.25f)
            {
                forest.TreesNear(p, trees);
                foreach (var t in trees)
                {
                    Vector3 rel = p + v * 0.03f - t.at;
                    float h = Vector3.Dot(rel, t.up);
                    if (h < 0f || h > t.top) continue;
                    if (!(t.girth < Forest.ThinGirth || h > t.solid)) continue;
                    Vector3 o = rel - t.up * h;
                    if (o.magnitude > Forest.TrunkAt(t, h) + 0.45f) continue;
                    if (!BreakThin(t, h, flat, flat.magnitude, false)) continue;
                    trees.Clear();
                    Snapped?.Invoke(p, flat.magnitude, false);
                    return true;
                }
                trees.Clear();
            }
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
            float cut = pieces.Count > 0 ? pieces[0].cut : 0f, above = pieces.Count > 0 ? pieces[0].top : 0f;
            top.props.SetFloat(CutY, cut > 0.01f ? cut : -10000f);
            top.props.SetFloat(CutTop, above > 0.01f ? above : 1e6f);
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

        // ---------------------------------------------------------------- torn-off branches

        /// <summary>A sprig: a branch end torn off, tumbling down and lying on the snow. Drawn from the tree's own
        /// atlas (a frond card, crossed), so it is the same needles and snow as the branch it came from.</summary>
        class Sprig { public bool on, landed, pine, dead; public Vector3 pos, vel, axis; public Quaternion rot; public float spin, size, t; }
        const int SprigPool = 40;
        readonly Sprig[] sprigs = new Sprig[SprigPool];
        int nextSprig;
        readonly List<Matrix4x4> firSprigs = new List<Matrix4x4>(), pineSprigs = new List<Matrix4x4>(), deadSprigs = new List<Matrix4x4>();
        static Mesh firSprig, pineSprig, deadSprig;
        MaterialPropertyBlock sprigProps;

        /// <summary>How many torn-off branches are flying or lying (for the test tools).</summary>
        public int SprigCount { get { int n = 0; foreach (var s in sprigs) if (s != null && s.on) n++; return n; } }

        void Fling(in Forest.Tree t, Vector3 at, Vector3 side, Vector3 flat, int n)
        {
            if (!t.realistic || !RealFir.Ready) return;
            bool pine = RealFir.AtlasOf(t.idx) == 1;
            for (int i = 0; i < n; i++)
            {
                var s = sprigs[nextSprig]; nextSprig = (nextSprig + 1) % SprigPool;
                s.on = true; s.landed = false; s.pine = pine; s.dead = t.snag; s.t = 0f;
                s.pos = at + Random.insideUnitSphere * 0.4f;
                // carried along with you, thrown out of the tree and up a little
                s.vel = flat * Random.Range(0.35f, 0.75f) + side * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(1f, 3.5f) + Random.insideUnitSphere * 1.2f;
                s.size = Random.Range(0.55f, 1.1f);
                s.rot = Random.rotation; s.axis = Random.onUnitSphere; s.spin = Random.Range(240f, 720f) * (Random.value < 0.5f ? -1f : 1f);
            }
        }

        void StepSprigs(float dt)
        {
            Vector3 far = forest != null && forest.target != null ? forest.target.position : Vector3.zero;
            foreach (var s in sprigs)
            {
                if (s == null || !s.on) continue;
                s.t += dt;
                if (forest != null && forest.target != null && (s.pos - far).sqrMagnitude > 200f * 200f) { s.on = false; continue; }
                if (s.landed) continue;
                s.vel.y -= 9.8f * dt;
                s.vel *= Mathf.Exp(-dt * 1.6f);   // a branch end is all needles: the air holds it back
                s.pos += s.vel * dt;
                s.rot = Quaternion.AngleAxis(s.spin * dt, s.axis) * s.rot;
                float g = Slope.DrawnHeight(s.pos.x, s.pos.z) + 0.04f;
                if (s.pos.y < g && s.vel.y < 0f)
                {
                    // down on the snow: it lies there, flat on it, turned as it came down
                    s.pos.y = g; s.landed = true;
                    Vector3 f = s.rot * Vector3.right; f = Vector3.ProjectOnPlane(f, Vector3.up);
                    s.rot = Quaternion.FromToRotation(Vector3.up, Slope.Normal(s.pos.x, s.pos.z)) * Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward) * Quaternion.Euler(0f, 90f, Random.Range(-12f, 12f));
                }
            }
        }

        void DrawSprigs()
        {
            if (!RealFir.Ready) return;
            firSprigs.Clear(); pineSprigs.Clear(); deadSprigs.Clear();
            foreach (var s in sprigs)
                if (s != null && s.on) (s.dead && s.pine ? deadSprigs : s.pine ? pineSprigs : firSprigs).Add(Matrix4x4.TRS(s.pos, s.rot, Vector3.one * s.size));
            if (firSprigs.Count + pineSprigs.Count + deadSprigs.Count == 0) return;
            if (firSprig == null)
            {
                // the firs' atlas: row 1, a flat frond with a dusting of snow; the pines': "bough_a", and a dead
                // snag's bare "dead_a" (Tools/MakeTrees.py: the atlas layouts)
                firSprig = SprigMesh(0f, 0.5f, 0.5f, 0.75f, "FirSprig");
                pineSprig = SprigMesh(0f, 0.375f, 0.5f, 0.625f, "PineSprig");
                deadSprig = SprigMesh(0f, 0.25f, 0.5f, 0.375f, "DeadSprig");
            }
            if (sprigProps == null) { sprigProps = new MaterialPropertyBlock(); sprigProps.SetFloat(Sway, 0f); sprigProps.SetFloat(SnowLeft, 0.7f); }
            for (int a = 0; a < 3; a++)
            {
                var list = a == 0 ? firSprigs : a == 1 ? pineSprigs : deadSprigs;
                if (list.Count == 0) continue;
                var mat = RealFir.StandaloneOfAtlas(a == 0 ? 0 : 1);
                if (mat == null) continue;
                var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, matProps = sprigProps };
                Graphics.RenderMeshInstanced(rp, a == 0 ? firSprig : a == 1 ? pineSprig : deadSprig, 0, list);
            }
        }

        /// <summary>A branch end: two crossed cards (flat and upright), 1 m from the torn stem (x 0) to the tip, 0.5 m
        /// across, cut from the atlas cell u0..u1, v0..v1 (the stem along the cell's middle).</summary>
        static Mesh SprigMesh(float u0, float v0, float u1, float v1, string name)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int k = 0; k < 2; k++)
            {
                Vector3 across = k == 0 ? Vector3.forward * 0.25f : Vector3.up * 0.25f, nn = k == 0 ? Vector3.up : Vector3.forward;
                int s0 = v.Count;
                v.Add(-across); v.Add(Vector3.right - across); v.Add(across); v.Add(Vector3.right + across);
                uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u1, v0)); uv.Add(new Vector2(u0, v1)); uv.Add(new Vector2(u1, v1));
                for (int i = 0; i < 4; i++) n.Add(nn);
                t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 1); t.Add(s0 + 1); t.Add(s0 + 2); t.Add(s0 + 3);
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTriangles(t, 0);
            var col = new Color32[v.Count]; for (int i = 0; i < col.Length; i++) col[i] = new Color32(230, 230, 230, 255);
            m.SetColors(col);
            m.RecalculateBounds();
            return m;
        }

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
            StepSprigs(dt);
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
            DrawSprigs();
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

        public void Clear()
        {
            foreach (var t in tops) t.on = false;
            foreach (var s in sprigs) if (s != null) s.on = false;
            heaps.Clear(); brushing.Clear();
            Brushes = DeepBrushes = Tops = SmallTrees = TrunkSnaps = 0; BrushLoss = 0f;
        }

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

        static readonly int CutTop = Shader.PropertyToID("_CutTop"), CutY = Shader.PropertyToID("_CutY"), Sway = Shader.PropertyToID("_Sway"), SnowLeft = Shader.PropertyToID("_Snow");

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
