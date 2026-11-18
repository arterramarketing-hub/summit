using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What marks the cliffs (see Cliffs), built by CourseDressing with the rest of a cycle and thrown away with it.
    ///   Jump cliffs: a sign up the in-run, and a row of short orange wands along the lip, so the line you are
    ///   about to fly off reads from a long way up.
    ///   Deadly cliffs: a sign at the side of the piste before the wall, and orange and black poles along its top
    ///   with caution tape strung between them, the way the crevasses and the start's drop are marked.
    /// Nothing here has a collider (the tape tears as you ride through it, like the start's).
    /// </summary>
    public static class CliffDressing
    {
        static Material signJump, signCliffs, tape;

        public static void Build(Transform root, int cycle, Shader lit, Material poleOrange, Material poleBlack, Material post, Material signBack, Rider rider)
        {
            if (signJump == null)
            {
                signJump = CourseDressing.Sign(lit, "Signs/SignJump");
                signCliffs = CourseDressing.Sign(lit, "Signs/SignCliffs");
                tape = CourseDressing.Sign(lit, "Signs/Tape");
            }
            for (int k = 0; k < 2; k++)
            {
                if (!Cliffs.JumpOf(cycle, k, out var j)) continue;
                Sign(root, j.Start - 18f, signJump, k == 0 ? 1f : -1f, post, signBack);
                // wands along the lip, across the piste and a little way into the rough either side
                for (float a = -Slope.PisteHalf - 4f; a <= Slope.PisteHalf + 4.01f; a += 4f)
                {
                    float x = Slope.Centre(j.z) + a, z = Cliffs.LipZ(j, a) - 0.6f;
                    float y = Slope.DrawnHeight(x, z);
                    Pole(root, new Vector3(x, y + 0.35f, z), new Vector3(0.035f, 0.4f, 0.035f), Mathf.Abs(a) > Slope.PisteHalf ? poleBlack : poleOrange);
                }
            }
            if (Cliffs.WallOf(cycle, out var w))
            {
                Sign(root, w.z0 - 28f, signCliffs, w.side, post, signBack);
                // along the top, from where it turns the corner to where it has died down to nothing much
                Vector3 last = Vector3.zero;
                bool have = false;
                for (float z = w.z0 + 4f; z < w.z2 - 20f; z += 11f)
                {
                    if (Cliffs.WallHeight(w, z) < 6f) { have = false; continue; }
                    float x = Slope.Centre(z) + w.side * (w.edge - 1.2f);
                    float y = Slope.DrawnHeight(x, z) - 0.05f;
                    Pole(root, new Vector3(x, y + 0.8f, z), new Vector3(0.06f, 0.8f, 0.06f), poleOrange);
                    Pole(root, new Vector3(x, y + 1.45f, z), new Vector3(0.065f, 0.12f, 0.065f), poleBlack);
                    var tie = new Vector3(x, y + 1.15f, z);
                    if (have && rider != null) CautionTape.Make(root, last, tie, rider, tape);
                    last = tie; have = true;
                }
            }
        }

        static void Sign(Transform root, float z, Material face, float side, Material post, Material back)
        {
            float x = Slope.Centre(z) + side * (Slope.PisteHalf - 1f);
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
