using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Pickups floating over the piste:
    ///  - Mountain Zoom, a can of green citrus soda: a speed boost
    ///  - shields: each one absorbs a crash; up to three stack
    ///  - Super Jump, a spring: for a while your jumps launch you far higher
    ///  - Invincibility, a gold star: for a while you smash through whatever you hit
    /// Where they sit, and which is which, is a pure function of the slot, so a run is the
    /// same for everyone on the same day; anything you collect stays collected until the next run.
    /// </summary>
    public class PowerUps : MonoBehaviour
    {
        // (Soda is the Mountain Zoom can; the old name is kept so saved code and tests still read)
        public enum Kind { Soda, Shield, SuperJump, Invincible }
        public const float Spacing = 38f;
        public Rider rider;
        public System.Action<Kind> Collected;

        Material canLabel, canTop, bubble, core, glow, spring, arrow, star;
        Mesh canMesh, lidMesh, rimMesh, coilMesh, starMesh, arrowMesh;
        readonly Dictionary<int, GameObject> live = new Dictionary<int, GameObject>();
        readonly Dictionary<int, Kind> kindOf = new Dictionary<int, Kind>();
        readonly HashSet<int> taken = new HashSet<int>();
        readonly Queue<GameObject>[] spare = { new Queue<GameObject>(), new Queue<GameObject>(), new Queue<GameObject>(), new Queue<GameObject>() };
        readonly List<int> drop = new List<int>();

        public static bool At(int slot, out Vector3 pos, out Kind kind)
        {
            pos = Vector3.zero; kind = Kind.Soda;
            if (slot < 1) return false;
            if (Slope.Hash01(slot, 11, 21) > 0.72f) return false;
            float z = slot * Spacing + (Slope.Hash01(slot, 12, 21) - 0.5f) * 14f;
            float x = Slope.Centre(z) + (Slope.Hash01(slot, 13, 21) - 0.5f) * 2f * (Slope.PisteHalf - 4f);
            if (Slope.OnPad(x, z) || Mathf.Abs(Slope.Kicker(x, z)) > 0.01f) return false;
            if (Course.CrevasseNear(x, z, 2f)) return false;
            if (Course.NearFeature(x, z, 0f)) return false;   // never floating over a rail or a box
            // the mix: the boost is the common one, invincibility the rare one
            float k = Slope.Hash01(slot, 14, 21);
            kind = k < 0.44f ? Kind.Soda : k < 0.70f ? Kind.Shield : k < 0.87f ? Kind.SuperJump : Kind.Invincible;
            if (Ridge.Weight(z) > 0f) x = Ridge.OnCrest(z, (Slope.Hash01(slot, 13, 21) - 0.5f) * 2f);   // on the crest, never over the drop
            pos = new Vector3(x, Slope.DrawnHeight(x, z) + 1.25f, z);
            return true;
        }

        public void Build(Shader lit, Material plain, Material soft)
        {
            // the can's printed label glows a little, so it reads from far up the slope even in shade; glossy, so the
            // sun lays a bright band down its side that stays put while the can spins under it
            var label = Resources.Load<Texture2D>("PowerUps/MountainZoomLabel");
            canLabel = Mat(lit, Color.white, 0.9f, new Color(0.35f, 0.35f, 0.35f));
            if (label != null) { canLabel.SetTexture("_BaseMap", label); canLabel.SetTexture("_EmissionMap", label); }
            canTop = Mat(lit, Color.white, 0.85f, Color.black);
            canTop.SetFloat("_Metallic", 0.8f);
            var lid = Resources.Load<Texture2D>("PowerUps/CanLid");
            if (lid != null) canTop.SetTexture("_BaseMap", lid);
            spring = Mat(lit, new Color(1f, 0.32f, 0.62f), 0.85f, new Color(0.7f, 0.08f, 0.35f));
            spring.SetFloat("_Metallic", 0.7f);
            arrow = Mat(lit, new Color(1f, 0.95f, 0.9f), 0.5f, new Color(0.9f, 0.75f, 0.85f));
            star = Mat(lit, new Color(1f, 0.80f, 0.15f), 0.9f, new Color(1.1f, 0.65f, 0.05f));
            star.SetFloat("_Metallic", 0.6f);
            canMesh = Shapes.Tube(0.27f, 0.95f, 40, true);
            lidMesh = Shapes.Disc(0.255f, 32);
            rimMesh = Shapes.Tube(0.262f, 0.028f, 40, false);
            coilMesh = Shapes.Coil(0.36f, 0.075f, 4.5f, 0.8f, 18, 8);
            starMesh = Shapes.Star(0.62f, 0.27f, 0.22f);
            arrowMesh = Shapes.Chevron(0.55f, 0.32f, 0.16f, 0.14f);
            core = Mat(lit, new Color(0.35f, 0.75f, 1f), 0.8f, new Color(0.25f, 0.6f, 1.2f));
            bubble = new Material(plain);
            bubble.SetColor("_BaseColor", new Color(0.45f, 0.75f, 1f, 0.28f));
            glow = new Material(soft);
            glow.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.55f));
        }

        static Material Mat(Shader sh, Color c, float smooth, Color emit) => Mats.Lit(c, smooth, sh, 0.3f, emit: emit);

        public void ResetRun()
        {
            taken.Clear();
            foreach (var kv in live) if (kv.Value != null) Recycle(kv.Key, kv.Value);
            live.Clear();
        }

        void Recycle(int slot, GameObject go)
        {
            go.SetActive(false);
            spare[(int)kindOf[slot]].Enqueue(go);
        }

        void Update()
        {
            if (rider == null || canLabel == null) return;
            Vector3 rp = rider.transform.position;
            int first = Mathf.FloorToInt(rp.z / Spacing) - 1, last = first + 9;

            drop.Clear();
            foreach (var kv in live) if (kv.Key < first || kv.Key > last) drop.Add(kv.Key);
            foreach (int k in drop) { if (live[k] != null) Recycle(k, live[k]); live.Remove(k); }

            float t = Time.time;
            for (int slot = Mathf.Max(1, first); slot <= last; slot++)
            {
                if (taken.Contains(slot)) continue;
                if (!live.TryGetValue(slot, out var go))
                {
                    if (!At(slot, out Vector3 p, out Kind kind)) { live[slot] = null; continue; }
                    kindOf[slot] = kind;
                    go = spare[(int)kind].Count > 0 ? spare[(int)kind].Dequeue() : Make(kind);
                    go.SetActive(true);
                    go.transform.position = p;
                    live[slot] = go;
                }
                if (go == null) continue;
                // spin and bob
                Vector3 basePos;
                At(slot, out basePos, out _);
                go.transform.SetPositionAndRotation(basePos + Vector3.up * (0.18f * Mathf.Sin(t * 2.4f + slot)),
                                                    Quaternion.Euler(0f, t * 140f + slot * 40f, 0f));
                // pick up
                Vector3 d = basePos - rp;
                if (rider.Running && !rider.Crashed && d.x * d.x + d.z * d.z < 1.9f * 1.9f && Mathf.Abs(d.y) < 2.6f)
                {
                    taken.Add(slot);
                    Recycle(slot, go);
                    live.Remove(slot);
                    Collected?.Invoke(kindOf[slot]);
                }
            }
        }

        GameObject Make(Kind kind)
        {
            var root = new GameObject(kind == Kind.Soda ? "MountainZoom" : kind.ToString());
            root.transform.SetParent(transform, false);
            if (kind == Kind.Soda)
            {
                // a big, bright can of Mountain Zoom (its label is made by Tools/MakeCans.swift and carries the design
                // twice round, so it shows whichever way the can has spun)
                var can = new GameObject("Can").transform;
                can.SetParent(root.transform, false);
                Mesh(can, canMesh, canLabel, new Vector3(0f, -0.475f, 0f), Quaternion.identity);
                Mesh(can, lidMesh, canTop, new Vector3(0f, 0.476f, 0f), Quaternion.identity);
                Mesh(can, lidMesh, canTop, new Vector3(0f, -0.476f, 0f), Quaternion.Euler(180f, 0f, 0f));
                // the rolled rim round the lid, and the ring-pull tab lying on it
                Mesh(can, rimMesh, canTop, new Vector3(0f, 0.468f, 0f), Quaternion.identity);
                var tab = Part(PrimitiveType.Cube, can, new Vector3(0f, 0.484f, 0.05f), new Vector3(0.08f, 0.012f, 0.14f), canTop);
                tab.localRotation = Quaternion.Euler(-5f, 0f, 0f);
            }
            else if (kind == Kind.Shield)
            {
                Part(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 1.1f, bubble);
                var c = Part(PrimitiveType.Cube, root.transform, Vector3.zero, Vector3.one * 0.38f, core);
                c.localRotation = Quaternion.Euler(45f, 0f, 45f);
            }
            else if (kind == Kind.SuperJump)
            {
                // a pink spring with a white arrow over it: up!
                Mesh(root.transform, coilMesh, spring, new Vector3(0f, -0.55f, 0f), Quaternion.identity);
                Mesh(root.transform, arrowMesh, arrow, new Vector3(0f, 0.55f, 0f), Quaternion.identity);
            }
            else
            {
                // a fat gold star
                Mesh(root.transform, starMesh, star, Vector3.zero, Quaternion.identity);
            }
            // a soft halo behind it
            var halo = new GameObject("Halo");
            halo.transform.SetParent(root.transform, false);
            var ps = halo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.startLifetime = 1.2f; main.startSpeed = 0.3f; main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startColor = kind == Kind.Soda ? new Color(0.6f, 1f, 0.4f, 0.8f) : kind == Kind.Shield ? new Color(0.6f, 0.85f, 1f, 0.8f)
                            : kind == Kind.SuperJump ? new Color(1f, 0.55f, 0.8f, 0.8f) : new Color(1f, 0.9f, 0.4f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            var em = ps.emission; em.rateOverTime = 10f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.5f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = halo.GetComponent<ParticleSystemRenderer>();
            pr.material = glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return root;
        }

        static void Mesh(Transform parent, Mesh mesh, Material m, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(pos, rot);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
