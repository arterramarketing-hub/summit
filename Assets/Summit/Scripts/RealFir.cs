using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Realistic snowy firs, made of photo-scanned branch cards (Resources/RealTrees/FirCards.png, built by
    /// Tools/MakeFirCards.swift from Poly Haven's CC0 "Fir Tree 01" twigs).
    ///
    /// Up close, each tree is a trunk and tiers of drooping branches; every branch is two cards - one upright
    /// with a cap of snow along its top (what you see from beside the tree) and one lying flat, loaded with
    /// snow (what you see from above, riding down towards it). About 300 triangles and one material a tree.
    ///
    /// Far away, each tree is an impostor: a picture of the same tree, taken once at start-up, on two crossed
    /// quads (4 triangles).
    /// </summary>
    public static class RealFir
    {
        public const int Variants = 4;
        public static Mesh[] Meshes, Impostors;
        public static float[] Heights;
        public static Material Cards;
        public static Material[] ImpostorMats;
        static RenderTexture[] shots;

        public static bool Ready => Meshes != null && Cards != null;

        /// <summary>Builds the tree meshes and takes their impostor pictures (once).</summary>
        public static bool Load()
        {
            if (Ready) return true;
            var cards = Resources.Load<Material>("RealTrees/FirCards");
            var imp = Resources.Load<Material>("RealTrees/FirImpostor");
            if (cards == null || imp == null) return false;
            Cards = new Material(cards) { enableInstancing = true };
            ImpostorDay = imp.GetColor("_BaseColor");
            Meshes = new Mesh[Variants]; Heights = new float[Variants];
            for (int i = 0; i < Variants; i++)
            {
                Meshes[i] = Build(1000 + i * 77);
                Heights[i] = Meshes[i].bounds.size.y;
            }
            BakeImpostors(imp);
            return true;
        }

        /// <summary>
        /// A realistic fir standing on its own, outside the forest (the camp, the trees the start tape is tied to):
        /// `height` metres tall from `at`, turned by `yaw` degrees. Null if the trees could not be loaded.
        /// </summary>
        public static GameObject Plant(Transform parent, Vector3 at, float height, float yaw, int variant, string name = "RealFir")
        {
            if (!Load()) return null;
            int v = ((variant % Variants) + Variants) % Variants;
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            g.transform.localScale = Vector3.one * (height / Heights[v]);
            g.GetComponent<MeshFilter>().sharedMesh = Meshes[v];
            g.GetComponent<MeshRenderer>().sharedMaterial = Cards;
            return g;
        }

        /// <summary>
        /// The time of day's light on a tree, relative to the neutral daylight the impostors were baked in
        /// (DayNight sets it every frame), so the far pictures match the lit near trees in every phase.
        /// </summary>
        public static Color PhaseTint = Color.white;

        /// <summary>The impostors are unlit pictures taken in daylight: put the time of day's light on them.</summary>
        public static void Dusk()
        {
            if (ImpostorMats == null) return;
            var k = PhaseTint;
            if (Mathf.Abs(k.r - lastDusk.r) + Mathf.Abs(k.g - lastDusk.g) + Mathf.Abs(k.b - lastDusk.b) < 0.004f) return;
            lastDusk = k;
            var c = ImpostorDay * k; c.a = ImpostorDay.a;
            foreach (var m in ImpostorMats) m.SetColor("_BaseColor", c);
        }
        static Color lastDusk = new Color(-1f, -1f, -1f);
        static Color ImpostorDay = Color.white;

        // ---- the atlas: 2 columns x 4 rows of 512x256 cells

        // row 0: upright with a snow cap; row 1: flat, a dusting; row 2: flat, heavy snow; row 3: bark (whole width)
        static Rect Cell(int col, int row)
        {
            const float px = 1f / 1024f;
            float u0 = col * 0.5f + px, u1 = col * 0.5f + 0.5f - px;
            float vTop = 1f - row * 0.25f - px, vBot = 1f - (row + 1) * 0.25f + px;
            return Rect.MinMaxRect(u0, vBot, u1, vTop);
        }
        const float StemUpright = 0.44f;   // where the stem runs across an upright card (0 bottom .. 1 top)
        const float StemStart = 12f / 512f, StemEnd = 498f / 512f;   // where the frond's stem starts and ends along a card

        // ---- building one tree

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            /// <summary>A card: `along` runs from the trunk end of the cell to its tip, `across` from the cell's bottom edge to its top.</summary>
            public void Card(Vector3 origin, Vector3 along, Vector3 across, Rect cell, float stemV)
            {
                int s = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float a = (k & 1) == 0 ? 0f : 1f, b = (k & 2) == 0 ? 0f : 1f;
                    var p = origin + along * a + across * (b - stemV);
                    v.Add(p);
                    // soft, rounded lighting: the normal leans out from the trunk and up, whatever way the card faces
                    var radial = new Vector3(p.x, 0f, p.z);
                    radial = radial.sqrMagnitude > 1e-4f ? radial.normalized : Vector3.Cross(across, along).normalized;
                    n.Add((radial * 0.65f + Vector3.up * 0.6f).normalized);
                    uv.Add(new Vector2(Mathf.Lerp(cell.xMin, cell.xMax, a), Mathf.Lerp(cell.yMin, cell.yMax, b)));
                }
                t.Add(s); t.Add(s + 2); t.Add(s + 1);
                t.Add(s + 1); t.Add(s + 2); t.Add(s + 3);
            }
        }

        static Mesh Build(int seed)
        {
            var rs = Random.state;
            Random.InitState(seed);
            var b = new Builder();
            const float H = 10f;

            // the trunk: six sides, tapering, bark from the bottom strip of the atlas
            {
                var bark = Cell(0, 3); bark.xMax = 958f / 1024f;
                const int sides = 6; float y0 = -0.3f, y1 = H * 0.8f, r0 = H * 0.022f, r1 = H * 0.004f;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    int s = b.v.Count;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    b.v.Add(d0 * r0 + Vector3.up * y0); b.v.Add(d1 * r0 + Vector3.up * y0); b.v.Add(d0 * r1 + Vector3.up * y1); b.v.Add(d1 * r1 + Vector3.up * y1);
                    b.n.Add(d0); b.n.Add(d1); b.n.Add(d0); b.n.Add(d1);
                    float va = Mathf.Lerp(bark.yMin, bark.yMax, i / (float)sides), vb = Mathf.Lerp(bark.yMin, bark.yMax, (i + 1) / (float)sides);
                    b.uv.Add(new Vector2(bark.xMin, va)); b.uv.Add(new Vector2(bark.xMin, vb)); b.uv.Add(new Vector2(bark.xMax, va)); b.uv.Add(new Vector2(bark.xMax, vb));
                    b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 1); b.t.Add(s + 1); b.t.Add(s + 2); b.t.Add(s + 3);
                }
            }

            // the core: a dark cone inside the crown, so you never see straight through the tree
            {
                // textured with the dense middle of a frond (row 1), so up close it is needles, not a flat colour
                var core = Rect.MinMaxRect(0.06f, 0.59f, 0.42f, 0.66f);
                const int sides = 7; float y0 = H * 0.12f, y1 = H * 0.78f, r0 = H * 0.11f;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    int s = b.v.Count;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    b.v.Add(d0 * r0 + Vector3.up * y0); b.v.Add(d1 * r0 + Vector3.up * y0); b.v.Add(Vector3.up * y1);
                    b.n.Add((d0 + Vector3.up * 0.5f).normalized); b.n.Add((d1 + Vector3.up * 0.5f).normalized); b.n.Add(Vector3.up);
                    b.uv.Add(new Vector2(core.xMin, core.yMin)); b.uv.Add(new Vector2(core.xMin, core.yMax)); b.uv.Add(new Vector2(core.xMax, core.center.y));
                    b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 1);
                }
            }

            // tiers of branches, broad at the bottom and narrowing to a spire
            int tiers = 16 + Random.Range(0, 3);
            float twist = Random.value * 360f;
            for (int i = 0; i < tiers; i++)
            {
                float f = i / (tiers - 1f);
                float y = H * (0.07f + 0.85f * Mathf.Pow(f, 0.92f)) + Random.Range(-0.12f, 0.12f);
                float reach = H * 0.26f * Mathf.Pow(1f - f, 0.95f) + 0.35f;
                int count = Mathf.RoundToInt(Mathf.Lerp(8f, 4f, f));
                twist += 137.5f;
                for (int j = 0; j < count; j++)
                {
                    float az = (twist + j * 360f / count + Random.Range(-18f, 18f)) * Mathf.Deg2Rad;
                    var outH = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                    float droop = Mathf.Lerp(30f, 8f, f) + Random.Range(-6f, 6f);
                    var dir = Quaternion.AngleAxis(droop, Vector3.Cross(outH, Vector3.up)) * outH;   // tip down
                    float len = reach * Random.Range(0.85f, 1.12f) / Mathf.Cos(droop * Mathf.Deg2Rad);
                    float cardLen = len / (StemEnd - StemStart);
                    var origin = Vector3.up * y - dir * (cardLen * StemStart);
                    var side = Vector3.Cross(Vector3.up, dir).normalized;
                    var up = Vector3.Cross(dir, side).normalized;
                    float wid = cardLen * 0.5f;
                    int col = Random.Range(0, 2);
                    // upright card, snow cap on top
                    b.Card(origin, dir * cardLen, up * wid * 0.85f, Cell(col, 0), StemUpright);
                    // flat card, rolled a little, mostly loaded with snow (a dusting near the top)
                    if (Random.value < Mathf.Lerp(0.9f, 0.5f, f))
                    {
                        var roll = Quaternion.AngleAxis(Random.Range(-15f, 15f), dir);
                        int row = Random.value < Mathf.Lerp(0.85f, 0.35f, f) ? 2 : 1;
                        b.Card(origin, dir * cardLen, roll * side * wid, Cell(1 - col, row), 0.5f);
                    }
                }
            }
            // the leader: two crossed fronds pointing up
            {
                float y = H * 0.86f, len = H * 0.16f;
                for (int k = 0; k < 2; k++)
                {
                    var side = k == 0 ? Vector3.right : Vector3.forward;
                    b.Card(Vector3.up * (y - len * StemStart / (StemEnd - StemStart)), Vector3.up * len / (StemEnd - StemStart), side * len * 0.45f, Cell(k, 1), 0.5f);
                }
            }

            var m = new Mesh { name = "RealFir" + seed };
            m.SetVertices(b.v); m.SetNormals(b.n); m.SetUVs(0, b.uv); m.SetTriangles(b.t, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            Random.state = rs;
            return m;
        }

        // ---- impostors: a picture of each tree, side on, on two crossed quads

        const int ShotW = 256, ShotH = 512, BakeLayer = 31;

        static void BakeImpostors(Material impostorTemplate)
        {
            Impostors = new Mesh[Variants]; ImpostorMats = new Material[Variants]; shots = new RenderTexture[Variants];
            var go = new GameObject("FirImpostorCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.13f, 0.12f, 0f);   // dark green, clear: what the edges fade into
            cam.cullingMask = 1 << BakeLayer;
            cam.allowHDR = false; cam.allowMSAA = false;
            cam.nearClipPlane = 1f; cam.farClipPlane = 200f;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderShadows = false; data.renderPostProcessing = false;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            // bake under a fixed, neutral daylight: the time of day at launch must not be frozen into the pictures
            var sun = RenderSettings.sun;
            if (sun == null) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            Color sunCol = sun != null ? sun.color : Color.white; float sunI = sun != null ? sun.intensity : 1f;
            Quaternion sunRot = sun != null ? sun.transform.rotation : Quaternion.identity;
            var ambMode = RenderSettings.ambientMode; Color ambCol = RenderSettings.ambientLight;
            var ambProbe = RenderSettings.ambientProbe;
            if (sun != null) { sun.color = new Color(1f, 0.96f, 0.9f); sun.intensity = 1.15f; sun.transform.rotation = Quaternion.Euler(48f, -30f, 0f); }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.68f);
            var at = new Vector3(0f, 4000f, 0f);   // well away from everything
            for (int i = 0; i < Variants; i++)
            {
                var bnd = Meshes[i].bounds;
                float half = Mathf.Max(bnd.extents.y, Mathf.Max(bnd.extents.x, bnd.extents.z) * 2f) * 1.02f;   // the cell is twice as tall as wide
                var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = true, name = "FirImpostor" + i };
                rt.Create();
                cam.targetTexture = rt;
                cam.aspect = ShotW / (float)ShotH;
                cam.orthographicSize = half;
                cam.transform.SetPositionAndRotation(at + new Vector3(0f, bnd.center.y, -60f), Quaternion.identity);
                var rp = new RenderParams(Cards) { layer = BakeLayer, camera = cam, receiveShadows = false, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off };
                Graphics.RenderMesh(rp, Meshes[i], 0, Matrix4x4.Translate(at));
                cam.Render();
                shots[i] = rt;
                var mat = new Material(impostorTemplate) { enableInstancing = true };
                mat.SetTexture("_BaseMap", rt); mat.mainTexture = rt;
                ImpostorMats[i] = mat;
                Impostors[i] = Cross(bnd.center.y, half);
            }
            RenderSettings.fog = fog;
            if (sun != null) { sun.color = sunCol; sun.intensity = sunI; sun.transform.rotation = sunRot; }
            RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol; RenderSettings.ambientProbe = ambProbe;
            cam.targetTexture = null;
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
        }

        static Mesh Cross(float cy, float half)
        {
            float w = half * ShotW / ShotH;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int k = 0; k < 2; k++)
            {
                var side = k == 0 ? Vector3.right : Vector3.forward;
                int s = v.Count;
                v.Add(-side * w + Vector3.up * (cy - half)); v.Add(side * w + Vector3.up * (cy - half));
                v.Add(-side * w + Vector3.up * (cy + half)); v.Add(side * w + Vector3.up * (cy + half));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                for (int q = 0; q < 4; q++) n.Add(Vector3.up);
                t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s + 1); t.Add(s + 2); t.Add(s + 3);
            }
            var m = new Mesh { name = "FirImpostor" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
