using UnityEngine;

namespace Summit
{
    /// <summary>Marks a collider the rider can hit (a tree, a rock, an animal).</summary>
    public class Obstacle : MonoBehaviour
    {
        public string label = "Tree";
        public bool always;   // knocks you down however lightly it touches (animals)
        /// <summary>What the invincible rider smashes through: trees, rocks and animals. Everything else (cabins, cars,
        /// fences, lamp posts, lift towers and stations) stays solid.</summary>
        public bool Smashable => always || label == "Tree" || label == "Rock";
    }
}
