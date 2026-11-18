using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What you see of a ridge besides the terrain itself: rock outcrops breaking out of the cliffs and piled at
    /// their feet, and orange marker poles along the crest's edge where it drops to a cliff, so the line reads
    /// at speed. Built by CourseDressing with the rest of a cycle, and thrown away with it.
    /// The rocks have no collider: by the time you reach the rock you are already falling. The marker poles can be
    /// knocked over (Breakable).
    /// </summary>
    public static class RidgeDressing
    {
        static GameObject[] rocks;

        static GameObject[] Rocks()
        {
            if (rocks == null) rocks = Resources.LoadAll<GameObject>("Rocks");
            return rocks;
        }

        public static void Build(Transform root, int cycle, Material poleOrange, Material poleBlack)
        {
            float z0 = Course.First + cycle * Course.Cycle + Ridge.At, z1 = Course.First + cycle * Course.Cycle + Ridge.End;
            var models = Rocks();
            // the models' own rock is pale; on a cliff they wear the cliff's own rock, and keep their snow caps
            var rock = RockFace.Material(poleOrange.shader);
            // rock: along the full crest and into the ramps, where the cliffs are already tall
            int i = 0;
            for (float z = z0 + Ridge.RampIn * 0.5f; z < z1 - Ridge.RampOut * 0.4f; i++)
            {
                z += 6f + 7f * Slope.Hash01(cycle, i, 71);
                float w = Ridge.Weight(z);
                if (w < 0.35f) continue;
                for (int s = -1; s <= 1; s += 2)
                {
                    if (!Ridge.CliffSide(z, s)) continue;
                    float half = Ridge.HalfWidth(z), reach = Ridge.Reach(z, s);
                    // nothing reaches out past a few metres short of the cliff's foot: a fall ends past it, and
                    // that is where you get up (Ridge.SafeX), so no one stands up inside a rock
                    float clear = reach - Ridge.Clear;
                    float h1 = Slope.Hash01(cycle * 7 + s, i, 72), h2 = Slope.Hash01(cycle * 7 + s, i, 73), h3 = Slope.Hash01(cycle * 7 + s, i, 74);
                    // the taller the wall, the bigger its rocks: a 165 m wall wears crags, not pebbles
                    float big = 1f + Ridge.DepthHere(z, s) / 70f;
                    // outcrops on the face, kept below the crest so they never block the line: one high, one lower down
                    float e = Ridge.Shoulder + 2.5f + h1 * Mathf.Max(1f, reach - half - Ridge.Shoulder - 8f);
                    Outcrop(root, models, rock, s, half + e, clear, z + (h2 - 0.5f) * 4f, (2.2f + 4f * h2) * big, (int)(h3 * 977f));
                    if (big > 1.6f) Outcrop(root, models, rock, s, half + Ridge.Shoulder + (0.35f + 0.5f * h3) * (reach - half - Ridge.Shoulder), clear, z + 5f, (2f + 3f * h1) * big, (int)(h1 * 491f));
                    // and a boulder fallen to the foot of the cliff, every so often
                    if (h3 < 0.45f) Outcrop(root, models, rock, s, clear, clear, z + 3f, (2.5f + 3f * h1) * Mathf.Sqrt(big), (int)(h2 * 613f));
                }
            }
            // marker poles along the edge above a cliff, every 12 m
            for (float z = z0 + Ridge.RampIn * 0.6f; z < z1 - Ridge.RampOut * 0.5f; z += 12f)
            {
                if (Ridge.Weight(z) < 0.6f) continue;
                for (int s = -1; s <= 1; s += 2)
                {
                    if (!Ridge.CliffSide(z, s)) continue;
                    float x = Slope.Centre(z) + s * (Ridge.HalfWidth(z) + 0.4f);
                    float y = Slope.DrawnHeight(x, z) - 0.05f;   // planted a little into the snow
                    // on a holder at its foot, so it can be knocked over (Breakable)
                    var holder = new GameObject("RidgeMarker").transform;
                    holder.SetParent(root, false);
                    holder.position = new Vector3(x, y, z);
                    Pole(holder, new Vector3(x, y + 0.75f, z), new Vector3(0.05f, 0.75f, 0.05f), poleOrange);
                    Pole(holder, new Vector3(x, y + 1.38f, z), new Vector3(0.055f, 0.1f, 0.055f), poleBlack);
                    Breakable.Flag(holder, 1.48f);
                }
            }
        }

        /// <summary>
        /// A rock on side s of the crest, its middle d out from the centre line but pulled in so that no part of it
        /// reaches past `limit` (however it ends up turned).
        /// </summary>
        static void Outcrop(Transform root, GameObject[] models, Material rock, float s, float d, float limit, float z, float size, int pick)
        {
            // a holder, so the model keeps its own root rotation (the Blender axis fix) under ours
            var g = new GameObject("RidgeRock");
            g.transform.SetParent(root, false);
            if (models != null && models.Length > 0) Object.Instantiate(models[pick % models.Length], g.transform, false);
            else
            {
                var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sph.transform.SetParent(g.transform, false);
            }
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            g.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var b = Bounds(g);
            float tall = Mathf.Max(0.05f, b.size.y);
            // how far it reaches out from its pivot, per metre of size: any way round, and leaned out with a face
            // that may be 70 degrees
            float reach = (new Vector2(b.center.x, b.center.z).magnitude + new Vector2(b.extents.x, b.extents.z).magnitude) / tall + 0.65f;
            d = Mathf.Min(d, limit - reach * size);
            float x = Slope.Centre(z) + s * d, y = Slope.Height(x, z);
            // keep its top well below the crest beside it
            size = Mathf.Min(size, (Ridge.Top(z) - Ridge.Crown - 1.2f - y) / 0.6f);
            if (size < 1.2f || d < Ridge.HalfWidth(z) + Ridge.Shoulder + 1f) { Object.DestroyImmediate(g); return; }
            Vector3 n = Slope.Normal(x, z);
            // stand it half in the face: leaned with the slope, yawed at random, sunk a third of its height
            var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, n, 0.6f)) * Quaternion.Euler(0f, pick * 37f % 360f, 0f);
            // scale it to the size wanted, from the model's own height
            float k = size / tall;
            g.transform.localScale = new Vector3(k, k * 0.8f, k);
            g.transform.SetPositionAndRotation(new Vector3(x, y - size * 0.35f - b.min.y * k * 0.8f, z), rot);
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++) if (mats[m] != null && mats[m].name.StartsWith("Rock")) mats[m] = rock;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static void Pole(Transform root, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "RidgePole";
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
    }
}
