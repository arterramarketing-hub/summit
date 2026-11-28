using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What marks the cliffs (see Cliffs), built by CourseDressing with the rest of a cycle and thrown away with it.
    ///   Jump cliffs: a sign up the in-run, and a row of short orange wands along the lip, so the line you are
    ///   about to fly off reads from a long way up.
    ///   Deadly cliffs and the cliff band: a sign at the side of the piste before them. The one line along their
    ///   edge is PisteKit's rope on bamboo poles with crossed poles at its ends (one line per edge, not a tape too).
    /// Nothing here has a collider.
    /// </summary>
    public static class CliffDressing
    {
        static Material signJump, signCliffs, signLedges, signBand;

        public static void Build(Transform root, int cycle, Shader lit, Material poleOrange, Material poleBlack, Material post, Material signBack, Rider rider)
        {
            foreach (var _ in Steps(root, cycle, lit, poleOrange, poleBlack, post, signBack, rider)) { }
        }

        /// <summary>Build, a piece at a time (each jump's wands, each length of caution tape), for a cycle built over several frames.</summary>
        public static IEnumerable<bool> Steps(Transform root, int cycle, Shader lit, Material poleOrange, Material poleBlack, Material post, Material signBack, Rider rider)
        {
            if (signJump == null)
            {
                signJump = CourseDressing.Sign(lit, "Signs/SignJump");
                signCliffs = CourseDressing.Sign(lit, "Signs/SignCliffs");
                signLedges = CourseDressing.Sign(lit, "Signs/SignLedges");
                signBand = CourseDressing.Sign(lit, "Signs/SignBand");
            }
            for (int k = 0; k < 2; k++)
            {
                if (!Cliffs.JumpOf(cycle, k, out var j)) continue;
                Sign(root, j.Start - 18f, signJump, k == 0 ? 1f : -1f, post, signBack);
                // wands along the lip, across the piste and a little way into the rough either side (none on a cornice:
                // its curl and the shadow under it are the line, Cornices)
                if (!Cliffs.Cornice(j))
                for (float a = -Slope.PisteHalf - 4f; a <= Slope.PisteHalf + 4.01f; a += 4f)
                {
                    float x = Slope.Centre(j.z) + a, z = Cliffs.LipZ(j, a) - 0.6f;
                    float y = Slope.DrawnHeight(x, z);
                    Pole(root, new Vector3(x, y + 0.35f, z), new Vector3(0.035f, 0.4f, 0.035f), Mathf.Abs(a) > Slope.PisteHalf ? poleBlack : poleOrange);
                }
                yield return true;
            }
            if (Cliffs.LedgeOf(cycle, out var L))
            {
                // the way in: a double black diamond at the piste's edge where the strip opens, on its side
                float zs = L.Start - 22f;
                SignAt(root, Slope.Centre(zs) + L.side * (Slope.PisteHalf + 2.5f), zs, signLedges, post, signBack);
                // short wands along each lip, across the strip (black: off the piste)
                for (int i = 0; i < L.count && !Cliffs.Cornice(L); i++)
                {
                    for (float b = -L.half + 1f; b <= L.half - 0.99f; b += 3.5f)
                    {
                        float z = Cliffs.LedgeLipZ(L, i, b) - 0.6f;
                        float x = Slope.Centre(z) + L.side * (L.off + b);
                        float y = Slope.DrawnHeight(x, z);
                        Pole(root, new Vector3(x, y + 0.35f, z), new Vector3(0.035f, 0.4f, 0.035f), poleBlack);
                    }
                    yield return true;
                }
            }
            // (blind roll-overs: crossed poles at the lip on both edges of the piste are PisteKit's closed markers)
            if (CliffBand.Of(cycle, out var band))
            {
                // the way in: a sign at the edge of the run on the band's side. Along the lip, black wands at each low
                // place (the optional air); where the band stands tall the one line is PisteKit's rope on bamboo poles,
                // a step in from the edge, with the crossed "closed" poles at its ends (no caution tape as well)
                float zs = band.z0 - 30f;
                Sign(root, zs, signBand, -band.side, post, signBack);
                for (float zc = band.z1 - 8f; zc < band.z2 + 12f; zc += 5f)
                {
                    if (!CliffBand.Low(band, zc)) continue;
                    var at = CliffBand.PointAt(band, zc, CliffBand.Edge - 1.2f);
                    float y = Slope.DrawnHeight(at.x, at.y) - 0.05f;
                    Pole(root, new Vector3(at.x, y + 0.35f, at.y), new Vector3(0.035f, 0.4f, 0.035f), poleBlack);
                }
                yield return true;
            }
            // a deadly wall: its sign. The one line along it is PisteKit's rope on the piste's edge on the wall's side,
            // with the crossed poles at its ends (no caution tape along the top as well)
            if (Cliffs.WallOf(cycle, out var w)) Sign(root, w.z0 - 28f, signCliffs, w.side, post, signBack);
        }

        static void Sign(Transform root, float z, Material face, float side, Material post, Material back)
            => SignAt(root, Slope.Centre(z) + side * (Slope.PisteHalf - 1f), z, face, post, back);

        static void SignAt(Transform root, float x, float z, Material face, Material post, Material back)
        {
            float y = Slope.DrawnHeight(x, z);
            const float W = 6f, H = 1.5f, lift = 2.2f;
            for (int s = -1; s <= 1; s += 2)
                Block(PrimitiveType.Cylinder, root, new Vector3(x + s * W * 0.45f, y + lift * 0.5f + 0.4f, z), new Vector3(0.12f, (lift + 0.8f) * 0.5f, 0.12f), post);
            Block(PrimitiveType.Cube, root, new Vector3(x, y + lift + H * 0.5f, z + 0.05f), new Vector3(W + 0.15f, H + 0.15f, 0.08f), back);
            // the printed face looks up the slope at the rider (a quad shows its face to -Z)
            var q = Block(PrimitiveType.Quad, root, new Vector3(x, y + lift + H * 0.5f, z - 0.01f), new Vector3(W, H, 1f), face);
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Pole(Transform root, Vector3 pos, Vector3 scale, Material m) => Block(PrimitiveType.Cylinder, root, pos, scale, m).name = "CliffPole";

        static GameObject Block(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }
    }
}
