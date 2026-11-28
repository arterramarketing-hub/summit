using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Wood breaking, recorded (treesfx: it was made from noise through resonators, and sounded like blocks falling),
    /// in layers SoundMix plays together (PlayTree: pitched and levelled by the size of what broke and how fast it was
    /// hit, a different take each time):
    ///   Twig:  a thin twig's sharp crack (now and then a second break)        Resources/Audio/TreeSfx/tree_twig_*
    ///   Limb:  a limb's deeper snap and splintering                            tree_limb_*
    ///   Trunk: a trunk giving: a heavy, low crack, often doubled, splinters    tree_trunk_*
    ///   Thud:  what's broken landing: a low whump into the snow                tree_thud_*
    ///   Snow:  a soft dump of snow off the branches                            tree_snow_*
    /// Cut from three CC0 recordings on Freesound (CREDITS.txt): twigs breaking (Elements-Library, 683793) for the twigs
    /// and splinters, a wood break (Deathscyp, 443293) for the limbs and, slowed and doubled, the trunks, and snow falling
    /// from trees (Barbamiz, 116136) for the snow and, its low end only, the thuds and the trunks' weight.
    /// </summary>
    public static class TreeSounds
    {
        public static AudioClip[] Twigs() => Takes("tree_twig_");
        public static AudioClip[] Limbs() => Takes("tree_limb_");
        public static AudioClip[] Trunks() => Takes("tree_trunk_");
        public static AudioClip[] Thuds() => Takes("tree_thud_");
        public static AudioClip[] Snow() => Takes("tree_snow_");

        static AudioClip[] all;

        /// <summary>The takes of a layer (by their names' prefix), in order.</summary>
        static AudioClip[] Takes(string prefix)
        {
            if (all == null) all = Resources.LoadAll<AudioClip>("Audio/TreeSfx");
            var list = new List<AudioClip>();
            foreach (var c in all) if (c != null && c.name.StartsWith(prefix)) list.Add(c);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (list.Count == 0) Debug.LogWarning("TreeSounds: no takes for " + prefix + " in Resources/Audio/TreeSfx");
            return list.ToArray();
        }
    }
}
