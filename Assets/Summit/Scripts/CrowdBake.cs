using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// What the two crowds drawn far off share (LiftRiders on the chairs, MountainRiders on the runs): a stand-in
    /// person dressed in a plain look, posed by the crowd, frozen into a simplified mesh (PeopleLod.Bake), and the
    /// meshes drawn many at once, a palette row each (PeopleKit.LodMaterial). Each crowd poses its own.
    /// </summary>
    public static class CrowdBake
    {
        /// <summary>
        /// The look a bake is made in: a person from seed at height, built average, a beanie (with its pompom) or a
        /// helmet, no hair, no pack and no face showing, on skis or a board. (The colours come from each rider's row.)
        /// </summary>
        public static PersonLook Look(int seed, float height, bool beanie, PersonGear gear)
        {
            var look = PersonLook.Random(seed);
            look.height = height; look.build = 1f; look.shoulders = 1f;
            look.head = beanie ? PersonHead.Beanie : PersonHead.Helmet;
            look.hasPompom = beanie;
            look.hairStyle = PersonHair.None; look.pack = null; look.faceShows = false;
            look.gear = gear;
            return look;
        }

        /// <summary>The crowd's one stand-in, made the first time (named name) and dressed in look; null if it can't be.</summary>
        public static Person Sitter(ref Person p, PersonLook look, string name)
        {
            if (p == null) { p = Person.Make(look, PeopleKit.Root, name); if (p == null) return null; p.Shadows(false); p.Windless = true; }
            else { p.gameObject.SetActive(true); p.Dress(look); }
            return p;
        }

        /// <summary>
        /// The stand-in, posed, frozen into a mesh in the frame toLocal takes it to (cell: how coarse), then put away.
        /// what names it in the people log (SUMMIT_PEOPLE_LOG=1).
        /// </summary>
        public static Mesh Bake(Person p, Matrix4x4 toLocal, float cell, string name, string what)
        {
            var m = PeopleLod.Bake(p, toLocal, cell, name);
            if (System.Environment.GetEnvironmentVariable("SUMMIT_PEOPLE_LOG") == "1") Debug.Log($"{what}: {m.vertexCount} vertices, {m.GetIndexCount(0) / 3} triangles");
            p.SetLod(2);
            p.gameObject.SetActive(false);
            return m;
        }

        /// <summary>
        /// Draw each tier's poses many at once: mats[tier][pose] the places, rows[tier][pose] their palette rows,
        /// mesh(tier, pose) the bake (none: not drawn), bounds the world bounds to cull by (null: Unity's own).
        /// Adds the draw calls and triangles drawn.
        /// </summary>
        public static void Draw(List<Matrix4x4>[][] mats, float[][][] rows, MaterialPropertyBlock[][] mpbs,
                                System.Func<int, int, Mesh> mesh, System.Func<List<Matrix4x4>, Bounds> bounds,
                                ref int drawCalls, ref long triangles)
        {
            var mat = PeopleKit.LodMaterial;
            for (int t = 0; t < mats.Length; t++)
                for (int v = 0; v < mats[t].Length; v++)
                {
                    var list = mats[t][v];
                    if (list.Count == 0) continue;
                    var m = mesh(t, v);
                    if (m == null) continue;
                    var mpb = mpbs[t][v];
                    mpb.SetFloatArray("_Row", rows[t][v]);
                    var rp = new RenderParams(mat) { matProps = mpb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
                    if (bounds != null) rp.worldBounds = bounds(list);
                    Graphics.RenderMeshInstanced(rp, m, 0, list);
                    drawCalls++;
                    triangles += (long)(m.GetIndexCount(0) / 3) * list.Count;
                }
        }
    }
}
