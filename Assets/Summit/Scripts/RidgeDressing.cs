using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Orange marker poles along the crest's edge where it drops to a cliff, so the line reads at speed. Built by
    /// CourseDressing with the rest of a cycle, and thrown away with it. The poles can be knocked over (Breakable).
    /// The rock breaking out of the cliffs (outcrops, buttresses, talus) is CliffSpires.
    /// </summary>
    public static class RidgeDressing
    {
        public static void Build(Transform root, int cycle, Material poleOrange, Material poleBlack)
        {
            float z0 = Course.First + cycle * Course.Cycle + Ridge.At, z1 = Course.First + cycle * Course.Cycle + Ridge.End;
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
