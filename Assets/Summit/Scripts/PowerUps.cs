using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Pickups floating over the piste: green soda cans (a speed boost) and shields
    /// (each one absorbs a crash; up to three stack). Where they sit is a pure function
    /// of the slot, so a run is the same for everyone on the same day; anything you
    /// collect stays collected until the next run.
    /// </summary>
    public class PowerUps : MonoBehaviour
    {
        public enum Kind { Soda, Shield }
        public const float Spacing = 38f;
        public Rider rider;
        public System.Action<Kind> Collected;

        Material canBody, canBand, canTop, bubble, core, glow;
        readonly Dictionary<int, GameObject> live = new Dictionary<int, GameObject>();
        readonly Dictionary<int, Kind> kindOf = new Dictionary<int, Kind>();
        readonly HashSet<int> taken = new HashSet<int>();
        readonly Queue<GameObject>[] spare = { new Queue<GameObject>(), new Queue<GameObject>() };
        readonly List<int> drop = new List<int>();

        public static bool At(int slot, out Vector3 pos, out Kind kind)
        {
            pos = Vector3.zero; kind = Kind.Soda;
            if (slot < 1) return false;
            if (Slope.Hash01(slot, 11, 21) > 0.72f) return false;
            float z = slot * Spacing + (Slope.Hash01(slot, 12, 21) - 0.5f) * 14f;
            float x = Slope.Centre(z) + (Slope.Hash01(slot, 13, 21) - 0.5f) * 2f * (Slope.PisteHalf - 4f);
            if (Slope.OnPad(x, z) || Mathf.Abs(Slope.Kicker(x, z)) > 0.01f) return false;
            kind = Slope.Hash01(slot, 14, 21) < 0.3f ? Kind.Shield : Kind.Soda;
            pos = new Vector3(x, Slope.DrawnHeight(x, z) + 1.25f, z);
            return true;
        }

        public void Build(Shader lit, Material plain, Material soft)
        {
            canBody = Mat(lit, new Color(0.10f, 0.72f, 0.18f), 0.75f, new Color(0.05f, 0.45f, 0.08f));
            canBand = Mat(lit, new Color(0.98f, 0.82f, 0.12f), 0.6f, new Color(0.5f, 0.35f, 0.02f));
            canTop = Mat(lit, new Color(0.85f, 0.87f, 0.9f), 0.9f, Color.black);
            core = Mat(lit, new Color(0.35f, 0.75f, 1f), 0.8f, new Color(0.25f, 0.6f, 1.2f));
            bubble = new Material(plain);
            bubble.SetColor("_BaseColor", new Color(0.45f, 0.75f, 1f, 0.28f));
            glow = new Material(soft);
            glow.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.55f));
        }

        static Material Mat(Shader sh, Color c, float smooth, Color emit)
        {
            var m = new Material(sh);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0.3f);
            if (emit.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emit);
            }
            return m;
        }

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
            if (rider == null || canBody == null) return;
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
            var root = new GameObject(kind == Kind.Soda ? "Soda" : "Shield");
            root.transform.SetParent(transform, false);
            if (kind == Kind.Soda)
            {
                // a big, bright soda can, so it reads from far up the slope
                Part(PrimitiveType.Cylinder, root.transform, Vector3.zero, new Vector3(0.48f, 0.42f, 0.48f), canBody);
                Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.49f, 0.12f, 0.49f), canBand);
                Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.43f, 0f), new Vector3(0.42f, 0.02f, 0.42f), canTop);
                Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, -0.43f, 0f), new Vector3(0.42f, 0.02f, 0.42f), canTop);
            }
            else
            {
                Part(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 1.1f, bubble);
                var c = Part(PrimitiveType.Cube, root.transform, Vector3.zero, Vector3.one * 0.38f, core);
                c.localRotation = Quaternion.Euler(45f, 0f, 45f);
            }
            // a soft halo behind it
            var halo = new GameObject("Halo");
            halo.transform.SetParent(root.transform, false);
            var ps = halo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.startLifetime = 1.2f; main.startSpeed = 0.3f; main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startColor = kind == Kind.Soda ? new Color(0.6f, 1f, 0.4f, 0.8f) : new Color(0.6f, 0.85f, 1f, 0.8f);
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
