using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// What happens to things an invincible rider runs through:
    ///  - a tree snaps at the base and topples away from you, dumping its snow, with a spray of splinters;
    ///    where its crown hits the snow it throws up another burst
    ///  - a rock bursts into tumbling chunks and grit
    ///  - the bear goes cartwheeling (Wildlife.Knock)
    /// The broken pieces lie in the snow for a few seconds, then sink away.
    /// </summary>
    public class Smasher : MonoBehaviour
    {
        SnowFX fx;
        Forest forest;
        Wildlife wild;
        ParticleSystem splinters, grit;
        readonly List<Forest.Piece> pieces = new List<Forest.Piece>();

        class Fallen
        {
            public List<Forest.Piece> parts;
            public Vector3 origin, pivot, axis, slide; public float angle, spin, maxAngle, t, height, snow; public bool landed;
            public MaterialPropertyBlock props;   // its buried part hidden, held still against the wind, its snow going
        }
        class Chunk { public Forest.Piece part; public Vector3 centre, pos, vel, spinAxis; public float scale, spin, angle, t; }
        readonly List<Fallen> fallen = new List<Fallen>();
        readonly List<Chunk> chunks = new List<Chunk>();

        public void Build(SnowFX snow, Forest trees, Wildlife animals, Material soft)
        {
            fx = snow; forest = trees; wild = animals;
            // splinters: thin streaks of pale broken wood and dark bark
            splinters = System("Splinters", soft, 300);
            var sm = splinters.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.17f);
            sm.gravityModifier = 1.2f;
            var sr = splinters.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = 0.05f; sr.lengthScale = 3f;
            // grit: chips of broken rock
            grit = System("RockGrit", soft, 300);
            var gm = grit.main;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            gm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            gm.gravityModifier = 1.4f;
        }

        ParticleSystem System(string name, Material m, int max)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max; main.playOnAwake = false; main.startSpeed = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var c = ps.colorOverLifetime; c.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            c.color = fade;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>The rider smashed into this. vel is the rider's velocity going in.</summary>
        public void Hit(Obstacle ob, Vector3 at, Vector3 vel)
        {
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            Vector3 dir = flat.sqrMagnitude > 0.1f ? flat.normalized : Vector3.forward;
            if (ob.always) { wild?.Knock(vel); fx?.Impact(at, 12f, flat * 0.3f); return; }
            pieces.Clear();
            if (forest == null || !forest.Fell(ob.gameObject, pieces, out Vector3 basePos, out float size)) return;
            if (ob.label == "Rock") Shatter(basePos, size, dir, flat);
            else Topple(basePos, size, dir, flat, at);
        }

        void Topple(Vector3 basePos, float size, Vector3 dir, Vector3 flat, Vector3 at)
        {
            // it goes over sideways (a little forward): out of your way, and its crown sweeps clear of the camera
            // that follows a few metres behind you along the same line
            dir = Quaternion.Euler(0f, (Random.value < 0.5f ? -1f : 1f) * Random.Range(70f, 88f), 0f) * dir;
            float h = 8.5f * size;
            var props = new MaterialPropertyBlock();
            float cut = pieces.Count > 0 ? pieces[0].cut : 0f;
            props.SetFloat(CutY, cut > 0.01f ? cut : -10000f);
            props.SetFloat(Sway, 0f); props.SetFloat(SnowLeft, 1f);
            fallen.Add(new Fallen
            {
                parts = new List<Forest.Piece>(pieces), origin = basePos, pivot = basePos, axis = Vector3.Cross(Vector3.up, dir).normalized,
                slide = dir * Mathf.Min(6f, flat.magnitude * 0.25f), spin = 0.6f + flat.magnitude * 0.05f,
                maxAngle = 84f * Mathf.Deg2Rad, height = h, snow = 1f, props = props
            });
            // the branches dump their snow (a heap of it left where it stood), and the trunk bursts into splinters
            Vector3 hit = new Vector3(basePos.x, Mathf.Max(at.y, basePos.y + 0.6f), basePos.z);
            fx?.TreeDump(basePos, h, Mathf.Max(1f, h * 0.17f), 16f);
            TreeBreak.Instance?.AddHeap(basePos, Mathf.Max(0.4f, h * 0.025f));
            fx?.Impact(hit, 10f, flat * 0.2f);
            Burst(splinters, hit, dir, 110, flat.magnitude);
        }

        void Shatter(Vector3 basePos, float size, Vector3 dir, Vector3 flat)
        {
            float h = 1.3f * size;
            Vector3 centre = basePos + Vector3.up * h * 0.35f;
            int n = 6 + Mathf.RoundToInt(size * 3f);
            for (int i = 0; i < n; i++)
            {
                var part = pieces[i % pieces.Count];
                Vector3 off = Random.insideUnitSphere * h * 0.35f;
                chunks.Add(new Chunk
                {
                    part = part, centre = centre, pos = centre + off, scale = Random.Range(0.28f, 0.45f),
                    vel = flat * Random.Range(0.25f, 0.6f) + (off.normalized + Vector3.up * 1.2f) * Random.Range(2.5f, 6f),
                    spinAxis = Random.onUnitSphere, spin = Random.Range(200f, 700f), angle = Random.value * 360f
                });
            }
            fx?.Impact(centre, 14f, flat * 0.3f);
            Burst(grit, centre, dir, 90, flat.magnitude);
        }

        /// <summary>Splinters of broken wood (a sign ridden through).</summary>
        public void Splinters(Vector3 at, Vector3 dir, int n, float speed) { if (splinters != null) Burst(splinters, at, dir, n, speed); }

        void Burst(ParticleSystem ps, Vector3 at, Vector3 dir, int n, float speed)
        {
            var ep = new ParticleSystem.EmitParams();
            bool wood = ps == splinters;
            for (int i = 0; i < n; i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.3f;
                ep.velocity = (Random.insideUnitSphere * 1.1f + Vector3.up * 0.9f + dir * 0.6f).normalized * Random.Range(3f, 8f) + dir * speed * 0.35f;
                ep.startColor = wood ? (Random.value < 0.6f ? new Color(0.93f, 0.80f, 0.58f) : new Color(0.32f, 0.22f, 0.15f))
                                     : Color.Lerp(new Color(0.30f, 0.30f, 0.32f), new Color(0.62f, 0.62f, 0.64f), Random.value);
                ps.Emit(ep, 1);
            }
        }

        void Update() { Step(Time.deltaTime); Draw(); }

        /// <summary>Moves everything on (public so the test tools can drive it).</summary>
        public void Step(float dt)
        {
            for (int i = fallen.Count - 1; i >= 0; i--)
            {
                var f = fallen[i];
                f.t += dt;
                if (!f.landed)
                {
                    // a toppling pole: gravity's pull grows as it leans over
                    f.spin += 1.5f * 22f / f.height * Mathf.Sin(Mathf.Max(f.angle, 0.05f)) * dt;
                    f.angle += f.spin * dt;
                    if (f.angle >= f.maxAngle)
                    {
                        f.angle = f.maxAngle; f.landed = true;
                        Vector3 tipL = f.pivot + Quaternion.AngleAxis(f.angle * Mathf.Rad2Deg, f.axis) * Vector3.up * f.height;
                        fx?.TopLands(f.pivot, tipL, 14f);
                    }
                }
                // the snow lets go as it goes over: shed along the crown, a curtain of powder behind it (SnowFX)
                if (f.snow > 0.12f)
                {
                    Vector3 up = Quaternion.AngleAxis(f.angle * Mathf.Rad2Deg, f.axis) * Vector3.up;
                    Vector3 tip = f.pivot + up * f.height;
                    Vector3 tipVel = Vector3.Cross(f.axis, up) * (f.landed ? 0f : f.spin * f.height);
                    fx?.TreeTrail(f.pivot + up * f.height * 0.15f, tip, tipVel, dt, f.snow);
                    f.snow = Mathf.Max(0.12f, f.snow - dt / (f.landed ? 0.4f : 1.4f));
                    f.props.SetFloat(SnowLeft, f.snow);
                }
                // the snapped base skids a little way on
                f.pivot += f.slide * dt;
                f.slide *= Mathf.Exp(-dt * 3f);
                f.pivot.y = Mathf.Min(f.pivot.y, Slope.DrawnHeight(f.pivot.x, f.pivot.z));
                if (f.t > 5f) f.pivot.y -= dt * 2.5f;   // then it sinks away
                if (f.t > 6.5f) fallen.RemoveAt(i);
            }
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                var c = chunks[i];
                c.t += dt;
                c.vel.y -= 22f * dt;
                c.pos += c.vel * dt;
                c.angle += c.spin * dt;
                float g = Slope.DrawnHeight(c.pos.x, c.pos.z) + 0.1f;
                if (c.pos.y < g)
                {
                    c.pos.y = g;
                    if (c.vel.y < 0f) c.vel = new Vector3(c.vel.x * 0.55f, -c.vel.y * 0.25f, c.vel.z * 0.55f);
                    c.spin *= 0.6f;
                }
                if (c.t > 3.5f) c.pos.y -= dt * 1.2f;
                if (c.t > 4.5f) chunks.RemoveAt(i);
            }
        }

        public void Draw()
        {
            foreach (var f in fallen)
            {
                var rot = Matrix4x4.TRS(f.pivot, Quaternion.AngleAxis(f.angle * Mathf.Rad2Deg, f.axis), Vector3.one);
                // (the pieces' own matrices put them at the tree's original spot; take that out, turn, put back)
                foreach (var p in f.parts) Render(p, rot * Matrix4x4.Translate(-f.origin) * p.m, f.props);
            }
            foreach (var c in chunks)
                Render(c.part, Matrix4x4.TRS(c.pos, Quaternion.AngleAxis(c.angle, c.spinAxis), Vector3.one * c.scale) * Matrix4x4.Translate(-c.centre) * c.part.m);
        }

        static MaterialPropertyBlock cutProps;
        static readonly int CutY = Shader.PropertyToID("_CutY"), Sway = Shader.PropertyToID("_Sway"), SnowLeft = Shader.PropertyToID("_Snow");

        static void Render(Forest.Piece p, Matrix4x4 m, MaterialPropertyBlock props)
        {
            Graphics.RenderMesh(new RenderParams(p.mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, matProps = props }, p.mesh, p.sub, m);
        }

        static void Render(Forest.Piece p, Matrix4x4 m)
        {
            var rp = new RenderParams(p.mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            if (p.cut > 0.01f)
            {
                // a tree half buried in the snow: only the part that showed goes over, not the trunk that was under it
                if (cutProps == null) cutProps = new MaterialPropertyBlock();
                cutProps.SetFloat(CutY, p.cut);
                rp.matProps = cutProps;
            }
            Graphics.RenderMesh(rp, p.mesh, p.sub, m);
        }

        public void Clear()
        {
            fallen.Clear(); chunks.Clear();
            if (splinters != null) { splinters.Clear(); grit.Clear(); }
        }
    }
}
