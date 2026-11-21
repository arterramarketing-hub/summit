using UnityEngine;

namespace Summit
{
    /// <summary>Marks a collider the rider can hit (a tree, a rock, an animal).</summary>
    public class Obstacle : MonoBehaviour
    {
        public string label = "Tree";
        public bool always;   // knocks you down however lightly it touches (animals)
        /// <summary>A roof's solid, under the snow that is ridden (Roofs): riding past it on top is no close call.</summary>
        public bool roof;
        /// <summary>What the invincible rider smashes through: trees, rocks and animals. Everything else (cabins, cars,
        /// fences, lamp posts, lift towers and stations) stays solid.</summary>
        public bool Smashable => always || label == "Tree" || label == "Rock";

        /// <summary>
        /// The point of a collider's surface nearest p, as Collider.ClosestPoint gives it; a solid that is not convex (a
        /// crag's broken rock, the ice in a crevasse) it cannot answer for, so for one of those: where a level line from
        /// p toward its middle meets it (or, missing it, where a line to its middle does; or its box).
        /// </summary>
        public static Vector3 Nearest(Collider c, Vector3 p)
        {
            if (!(c is MeshCollider m) || m.convex) return c.ClosestPoint(p);
            var b = c.bounds;
            Vector3 level = new Vector3(b.center.x, p.y, b.center.z) - p, mid = b.center - p;
            float reach = mid.magnitude + b.extents.magnitude;
            if (level.sqrMagnitude > 1e-6f && c.Raycast(new Ray(p, level.normalized), out var hit, reach)) return hit.point;
            if (mid.sqrMagnitude > 1e-6f && c.Raycast(new Ray(p, mid.normalized), out hit, reach)) return hit.point;
            return c.ClosestPointOnBounds(p);
        }

        /// <summary>The collider of this obstacle the rider last hit (Rider.HitCollider) if it is one of its own, else its first.</summary>
        public Collider HitBy(Rider r) => r != null && r.HitCollider != null && r.HitCollider.GetComponentInParent<Obstacle>() == this ? r.HitCollider : GetComponentInChildren<Collider>();
    }
}
