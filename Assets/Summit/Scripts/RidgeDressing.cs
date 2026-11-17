using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What you see of a ridge besides the terrain itself: rock outcrops breaking out of the cliffs and piled at
    /// their feet, and orange marker poles along the crest's edge where it drops to a cliff, so the line reads
    /// at speed. Built by CourseDressing with the rest of a cycle, and thrown away with it.
    /// Nothing here has a collider: by the time you reach the rock you are already falling.
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
                    float h1 = Slope.Hash01(cycle * 7 + s, i, 72), h2 = Slope.Hash01(cycle * 7 + s, i, 73), h3 = Slope.Hash01(cycle * 7 + s, i, 74);
                    // an outcrop on the face, kept below the crest so it never blocks the line
                    float e = Ridge.Shoulder + 2.5f + h1 * Mathf.Max(1f, reach - half - Ridge.Shoulder - 8f);
                    Outcrop(root, models, rock, Slope.Centre(z) + s * (half + e), z + (h2 - 0.5f) * 4f, 2.2f + 4f * h2, (int)(h3 * 977f));
                    // and a boulder fallen to the foot of the cliff, every so often
                    if (h3 < 0.45f) Outcrop(root, models, rock, Slope.Centre(z) + s * (reach - 2f + h1 * 5f), z + 3f, 3f + 4f * h1, (int)(h2 * 613f));
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
                    float y = Slope.Height(x, z);
                    Pole(root, new Vector3(x, y + 0.75f, z), new Vector3(0.05f, 0.75f, 0.05f), poleOrange);
                    Pole(root, new Vector3(x, y + 1.38f, z), new Vector3(0.055f, 0.1f, 0.055f), poleBlack);
                }
            }
        }

        static void Outcrop(Transform root, GameObject[] models, Material rock, float x, float z, float size, int pick)
        {
            float y = Slope.Height(x, z);
            // keep its top well below the crest beside it
            size = Mathf.Min(size, (Ridge.Top(z) - Ridge.Crown - 1.2f - y) / 0.6f);
            if (size < 1.2f) return;
            Vector3 n = Slope.Normal(x, z);
            // stand it half in the face: leaned with the slope, yawed at random, sunk a third of its height
            var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, n, 0.6f)) * Quaternion.Euler(0f, pick * 37f % 360f, 0f);
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
            // scale it to the size wanted, from the model's own height
            var b = Bounds(g);
            float k = size / Mathf.Max(0.05f, b.size.y);
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
