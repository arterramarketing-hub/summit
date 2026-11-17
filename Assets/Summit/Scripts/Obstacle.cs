using UnityEngine;

namespace Summit
{
    /// <summary>Marks a collider the rider can hit (a tree, a rock, an animal).</summary>
    public class Obstacle : MonoBehaviour
    {
        public string label = "Tree";
        public bool always;   // knocks you down however lightly it touches (animals)
    }
}
