using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The cordon above the drop: caution tape tied chest-high between the last pair of warning signs, and from
    /// each of their outer posts on across the mountain, tree to tree, so the whole slope below reads as
    /// closed off. The start keeps itself clear of forest, so the trees it is tied to are planted here, with
    /// the same models and the same collision as the forest's own.
    /// </summary>
    public static class StartTape
    {
        public const float Chest = 1.35f;                 // tie height above the snow
        static readonly float[] TreeOff = { 15f, 25f, 36f, 48f, 61f, 75f };   // from the centre line, each way

        /// <summary>
        /// left/right: the two sign frames (as built by the game), postX: their posts' offset from each sign's
        /// middle. Every span tears when ridden through; torn is called for each.
        /// </summary>
        public static void Build(Transform root, Transform left, Transform right, float postX, Rider rider, Material tape, System.Action torn)
        {
            float z = (left.position.z + right.position.z) * 0.5f;
            float cx = (left.position.x + right.position.x) * 0.5f;
            Vector3 Post(Transform sign, float localX) => sign.TransformPoint(new Vector3(localX, 0f, 0f)) + Vector3.up * Chest;
            // across the run: the inner posts (the left sign's +x post, the right sign's -x post)
            Span(root, Post(left, postX), Post(right, -postX), rider, tape, torn);
            // the forest's own look: realistic firs, or the original models
            bool real = Forest.Realistic && RealFir.Load();
            var prefabs = real ? new GameObject[0] : Resources.LoadAll<GameObject>("Trees");
            for (int side = -1; side <= 1; side += 2)
            {
                var sign = side < 0 ? left : right;
                Vector3 from = Post(sign, side * postX);   // the outer post
                for (int k = 0; k < TreeOff.Length; k++)
                {
                    float x = cx + side * (TreeOff[k] + (Slope.Hash01(k, side, 81) - 0.5f) * 3f);
                    float tz = z + (Slope.Hash01(k, side, 82) - 0.5f) * 5f;
                    Vector3 trunk = new Vector3(x, Slope.DrawnHeight(x, tz), tz);
                    float size = 0.85f + 0.3f * Slope.Hash01(k, side, 83);
                    float yaw = Slope.Hash01(k, side, 84) * 360f;
                    if (real)
                    {
                        // grown and buried like the forest's trees here: the tape is tied into what shows
                        Forest.PlantLike(root, trunk, Slope.Hash01(k, side, 83), yaw, "TapeTree", out float vis, out float rad);
                        Trunk(root, trunk, vis, rad);
                    }
                    else if (prefabs.Length > 0) PlantTree(root, prefabs[(k * 2 + (side > 0 ? 1 : 0)) % prefabs.Length], trunk, size, yaw);
                    // tied round the trunk on the side facing the last tie
                    Vector3 tie = trunk + Vector3.up * Chest;
                    Vector3 toward = from - tie; toward.y = 0f;
                    tie += toward.normalized * 0.32f * size;
                    Span(root, from, tie, rider, tape, torn);
                    from = tie - toward.normalized * 0.64f * size;   // and out the far side of the trunk
                }
            }
        }

        static void Span(Transform root, Vector3 a, Vector3 b, Rider rider, Material tape, System.Action torn)
        {
            var t = CautionTape.Make(root, a, b, rider, tape);
            t.Torn += _ => torn?.Invoke();
        }

        /// <summary>A pine like the forest's: 8.5 m at size 1, base sunk a little into the snow, and solid.</summary>
        static void PlantTree(Transform root, GameObject prefab, Vector3 at, float size, float yaw)
        {
            var g = Object.Instantiate(prefab, root);
            TreeWind.SwayAll(g);   // moves in the wind like the forest's
            g.name = "TapeTree";
            g.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation;   // keep the model's own axis fix
            g.transform.position = Vector3.zero;
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float k = 8.5f * size / Mathf.Max(0.01f, b.size.y);
                g.transform.localScale = prefab.transform.localScale * k;
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                g.transform.position = new Vector3(at.x - b.center.x, at.y - 0.25f - b.min.y, at.z - b.center.z);
            }
            foreach (var c in g.GetComponentsInChildren<Collider>()) { if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c); }
            Trunk(root, at, 8.5f * size, 0.38f * size);
        }

        /// <summary>The same trunk the forest gives its trees, so riding into one is a crash like any other tree.</summary>
        static void Trunk(Transform root, Vector3 at, float h, float radius)
        {
            var hit = new GameObject("TreeHit");
            hit.transform.SetParent(root, false);
            hit.transform.position = at;
            var cap = hit.AddComponent<CapsuleCollider>();
            cap.direction = 1; cap.radius = radius; cap.height = Mathf.Max(h * 0.9f, radius * 2f); cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
            hit.AddComponent<Obstacle>().label = "Tree";
        }
    }
}
