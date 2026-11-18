using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Bandages on what broke, for the rest of the run: a broken arm or leg is wrapped from end to end, both its
    /// halves, thick over the clothes (as wide as the sleeve or trouser leg really is, measured from the mesh), with
    /// gauze turns and cuffs at the ends, so two broken legs read as legs bandaged up from hip to boot. With gore on,
    /// blood has soaked through: dark red patches with a rusty edge, heaviest over the break; with gore off they
    /// are clean white. The pause menu's switch changes them on the spot (RiderBodyLimbs.FollowGore). A limb that is
    /// gone isn't wrapped (its stump keeps its cap). All of it goes in ResetVisual.
    /// </summary>
    public partial class RiderBody
    {
        static Texture2D[] stainHeavy, stainLight;
        Material[] bloodyHeavy, bloodyLight;

        void Bandage(Transform bone)
        {
            if (bone == null || bandaged.Contains(bone) || rig == null) return;
            if (ragdoll != null && ragdoll.Gone(bone)) return;   // nothing left there to wrap
            BandageMats();
            bool limb = bone == rig.UpperL || bone == rig.LowerL || bone == rig.UpperR || bone == rig.LowerR
                     || bone == rig.ThighL || bone == rig.ShinL || bone == rig.ThighR || bone == rig.ShinR;
            if (limb)
            {
                // the whole limb, both halves: the half that broke soaked through, the other spotted
                var top = UpperOf(bone);
                float rTop = 0f, rLow = 0f; int wrapped = 0;
                foreach (var seg in new[] { top, ChildOf(top) })
                {
                    if (seg == null || bandaged.Contains(seg) || (ragdoll != null && ragdoll.Gone(seg))) continue;
                    var child = ChildOf(seg);
                    if (child == null) continue;
                    bandaged.Add(seg);
                    bool leg = seg == rig.ThighL || seg == rig.ShinL || seg == rig.ThighR || seg == rig.ShinR;
                    bool thigh = seg == rig.ThighL || seg == rig.ThighR;
                    float r = LimbRadius(seg, child);
                    if (r <= 0.02f) r = (leg ? (thigh ? 0.105f : 0.09f) : 0.07f) * 1.2f;   // (no mesh to measure)
                    r = r * 1.05f + 0.01f;                                               // over the cloth, not in it
                    bool shin = leg && !thigh;
                    WrapLimb(seg, seg.position, child.position, r, shin ? 0.05f : 0.03f, shin ? 0.86f : 0.97f, seg == bone);
                    if (seg == top) rTop = r; else rLow = r;
                    wrapped++;
                }
                // and over the knee or elbow between them, so the bend shows no gap
                var mid = ChildOf(top);
                if (wrapped > 0 && mid != null && rTop > 0f && rLow > 0f && !(ragdoll != null && ragdoll.Gone(mid)))
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.name = "BandageLight" + (Mathf.Abs(mid.GetInstanceID()) % 2);
                    g.GetComponent<MeshRenderer>().sharedMaterial = BandageMat(g.name, blood != null && blood.On);
                    float r = Mathf.Min(rTop, rLow) * 1.02f;   // (the thinner of the two: it sits inside the thicker's cuff)
                    g.transform.SetPositionAndRotation(mid.position, Quaternion.FromToRotation(Vector3.up, (ChildOf(mid).position - mid.position).normalized));
                    g.transform.localScale = new Vector3(r * 2f, r * 1.7f, r * 2f);
                    g.transform.SetParent(mid, true);
                    bandages.Add(g);
                }
                return;
            }
            bandaged.Add(bone);
            if (bone == rig.Head)
            {
                // round the jaw and over the top of the head, under the beanie
                Vector3 up = rig.Neck != null ? (rig.Head.position - rig.Neck.position).normalized : Vector3.up;
                Vector3 c = rig.Head.position + up * 0.03f;
                Wrap(bone, c - up * 0.03f, c + up * 0.03f, 0.118f, 0f, 1f, true);
                var side = Vector3.Cross(up, upper.forward).normalized;
                Wrap(bone, c - side * 0.012f, c + side * 0.012f, 0.13f, 0f, 1f, false, false);
            }
            else
            {
                // ribs: strapped round the chest
                Vector3 up = rig.Neck != null ? (rig.Neck.position - rig.Chest.position).normalized : Vector3.up;
                Wrap(bone, rig.Chest.position - up * 0.04f, rig.Chest.position + up * 0.12f, 0.19f, 0.05f, 0.95f, false);
            }
        }

        /// <summary>
        /// A limb segment wrapped from t0 to t1 along a..b: a layer over all of it, a second over the middle (over
        /// the break if this is the half that broke), slanting gauze turns, and a cuff at each end.
        /// </summary>
        void WrapLimb(Transform bone, Vector3 a, Vector3 b, float r, float t0, float t1, bool broke)
        {
            Vector3 p0 = Vector3.Lerp(a, b, t0), p1 = Vector3.Lerp(a, b, t1), d = p1 - p0;
            float len = Mathf.Max(0.02f, d.magnitude);
            Vector3 dir = d / len;
            var rot = Quaternion.FromToRotation(Vector3.up, dir);
            Vector3 across = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            int v = Mathf.Abs(bone.GetInstanceID()) % 2;
            string main = (broke ? "BandageHeavy" : "BandageLight") + v;
            Piece(bone, (p0 + p1) * 0.5f, rot, r, len * 0.5f, main);
            Piece(bone, Vector3.Lerp(p0, p1, broke ? 0.5f : 0.45f), rot * Quaternion.Euler(0f, 70f, 0f), r * 1.035f, len * (broke ? 0.24f : 0.16f), main);
            // the gauze going round, each turn slanting the other way to the last
            int k = Mathf.Clamp(Mathf.RoundToInt(len / 0.042f), 4, 14);
            for (int i = 1; i <= k; i++)
            {
                var tilt = Quaternion.AngleAxis((i % 2 == 0 ? 13f : -13f), across) * rot;
                Piece(bone, Vector3.Lerp(p0, p1, i / (k + 1f)), tilt, r * 1.045f, 0.0065f, "BandageLine");
            }
            Piece(bone, p0 + dir * 0.012f, rot, r * 1.06f, 0.013f, "BandageLine");
            Piece(bone, p1 - dir * 0.012f, rot, r * 1.06f, 0.013f, "BandageLine");
        }

        /// <summary>A plain wrap round the segment a..b (from t0 to t1 along it), with wrap lines, fixed to the bone (head, ribs).</summary>
        void Wrap(Transform bone, Vector3 a, Vector3 b, float r, float t0, float t1, bool heavy, bool lines = true)
        {
            BandageMats();
            Vector3 p0 = Vector3.Lerp(a, b, t0), p1 = Vector3.Lerp(a, b, t1), d = p1 - p0;
            float len = Mathf.Max(0.02f, d.magnitude);
            var rot = Quaternion.FromToRotation(Vector3.up, d.normalized);
            Piece(bone, (p0 + p1) * 0.5f, rot, r, len * 0.5f, (heavy ? "BandageHeavy" : "BandageLight") + "0");
            if (!lines) return;
            int k = Mathf.Clamp(Mathf.RoundToInt(len / 0.06f), 2, 6);
            for (int i = 1; i <= k; i++)
                Piece(bone, Vector3.Lerp(p0, p1, i / (k + 1f)) + d.normalized * (i % 2 == 0 ? 0.004f : -0.004f), rot, r * 1.06f, 0.006f, "BandageLine");
        }

        GameObject Piece(Transform bone, Vector3 at, Quaternion rot, float radius, float half, string kind)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = kind;
            g.GetComponent<MeshRenderer>().sharedMaterial = BandageMat(kind, blood != null && blood.On);
            g.transform.SetPositionAndRotation(at, rot);
            g.transform.localScale = new Vector3(radius * 2f, half, radius * 2f);
            g.transform.SetParent(bone, true);
            bandages.Add(g);
            return g;
        }

        Material BandageMat(string kind, bool gore)
        {
            if (kind == "BandageLine") return bandageLine;
            if (!gore) return bandageMat;
            int v = kind[kind.Length - 1] == '1' ? 1 : 0;
            return kind.StartsWith("BandageHeavy") ? bloodyHeavy[v] : bloodyLight[v];
        }

        /// <summary>Gore switched on or off: the bandages already on soak through, or come clean.</summary>
        void RestainBandages(bool gore)
        {
            if (bandageMat == null) return;
            foreach (var g in bandages)
                if (g != null && g.name.StartsWith("Bandage")) g.GetComponent<MeshRenderer>().sharedMaterial = BandageMat(g.name, gore);
        }

        void BandageMats()
        {
            if (bandageMat != null) return;
            var sh = jacketShader != null ? jacketShader : Shader.Find("Universal Render Pipeline/Lit");
            bandageMat = new Material(sh) { name = "Bandage" }; bandageMat.SetColor("_BaseColor", new Color(0.97f, 0.96f, 0.91f)); bandageMat.SetFloat("_Smoothness", 0.08f);
            bandageLine = new Material(sh) { name = "BandageLine" }; bandageLine.SetColor("_BaseColor", new Color(0.88f, 0.86f, 0.80f)); bandageLine.SetFloat("_Smoothness", 0.05f);
            if (stainHeavy == null)
            {
                stainHeavy = new[] { Stain(11, true), Stain(23, true) };
                stainLight = new[] { Stain(37, false), Stain(41, false) };
            }
            Material M(Texture2D t)
            {
                var m = new Material(sh) { name = "BandageBloody" };
                m.SetTexture("_BaseMap", t); m.mainTexture = t;
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.12f);
                return m;
            }
            bloodyHeavy = new[] { M(stainHeavy[0]), M(stainHeavy[1]) };
            bloodyLight = new[] { M(stainLight[0]), M(stainLight[1]) };
        }

        /// <summary>
        /// Gauze with blood soaked through (u round the limb, v along it): heavy is a few big patches over the
        /// middle, light a few small spots anywhere. Dark at the heart of each patch, a rusty halo where it spreads.
        /// </summary>
        static Texture2D Stain(int seed, bool heavy)
        {
            const int W = 128, H = 128;
            var st = Random.state;
            Random.InitState(seed);
            int n = heavy ? Random.Range(4, 7) : Random.Range(2, 4);
            var blobs = new Vector4[n];   // u, v, size u, size v
            for (int i = 0; i < n; i++)
            {
                float v = heavy ? Random.Range(0.32f, 0.68f) : Random.Range(0.15f, 0.85f);
                float su = heavy ? Random.Range(0.13f, 0.26f) : Random.Range(0.05f, 0.09f);
                float sv = heavy ? Random.Range(0.10f, 0.22f) : Random.Range(0.04f, 0.08f);
                blobs[i] = new Vector4(Random.value, v, su, sv);
            }
            float ox = Random.value * 50f, oy = Random.value * 50f;
            Random.state = st;
            var px = new Color32[W * H];
            Color gauze = new Color(0.96f, 0.95f, 0.90f), halo = new Color(0.66f, 0.24f, 0.20f), core = new Color(0.38f, 0.02f, 0.03f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    float f = 0f;
                    foreach (var b in blobs)
                    {
                        float du = Mathf.Abs(u - b.x); du = Mathf.Min(du, 1f - du);   // round the limb: it wraps
                        float dv = v - b.y;
                        f += Mathf.Exp(-(du * du / (b.z * b.z) + dv * dv / (b.w * b.w)));
                    }
                    // ragged, as it wicks through the weave
                    float nz = Mathf.PerlinNoise(u * 9f + ox, v * 9f + oy) * 0.6f + Mathf.PerlinNoise(u * 23f + oy, v * 23f + ox) * 0.4f;
                    f *= 0.55f + 0.9f * nz;
                    float weave = 1f - 0.035f * ((x % 4 < 2) ^ (y % 4 < 2) ? 1f : 0f);
                    Color c = gauze * weave;
                    c = Color.Lerp(c, halo, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.42f, f)));
                    c = Color.Lerp(c, core, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.85f, f)));
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = heavy ? "BandageSoaked" : "BandageSpotted", wrapMode = TextureWrapMode.Repeat };
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }
    }
}
