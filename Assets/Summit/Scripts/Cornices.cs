using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The cornices (Cliffs.Cornice): on half the jump cliffs and half the ledge lines, the wind has built the lip out into
    /// a rounded curl of packed snow that overhangs the face, with an undercut beneath it, its edge wandering along the
    /// ridge (now further out, now pulled back), and here and there broken: a notch in the curl where it has been cut,
    /// with its blocks lying rounded at the foot of the face, out at the sides of the line. Built by CourseDressing with
    /// the rest of a cycle, one mesh per lip, in the ground's own snow (the same grain, sparkle and wind crust: Surfaces
    /// lays the crust on the face and the crown, so they run on into each other). The undercut's soft blue-grey is the
    /// light: the curl shades it from the sun and only the sky lights it.
    ///
    /// Only the look: the heightfield lip under it is the same lip as before (Cliffs), so the takeoff, the flight and the
    /// landing ride exactly as they did. The curl's top runs on from the in-run's own line and falls away faster than any
    /// board leaving the lip at a walking pace or more, so a rider never flies through it; the blocks lie out of the
    /// line. Nothing here has a collider.
    ///
    /// Cheap: one mesh and one draw per lip (about 2,000 vertices across a jump cliff), the blocks three shared shapes.
    /// </summary>
    public static class Cornices
    {
        const float Step = 0.75f;   // metres between the curl's sections along the lip
        const int N = 16;           // points in a section
        static Material snow;
        static Mesh[] blocks;

        public static void Build(Transform root, int cycle)
        {
            foreach (var _ in Steps(root, cycle)) { }
        }

        /// <summary>Build, a lip at a time, for a cycle built over several frames.</summary>
        public static IEnumerable<bool> Steps(Transform root, int cycle)
        {
            if (!Cliffs.CorniceOn || cycle < 0) yield break;
            if (snow == null)
            {
                var t = Object.FindFirstObjectByType<SlopeStreamer>();
                if (t == null || t.snowMaterial == null) yield break;
                snow = Surfaces.Terrain(t.snowMaterial, true);
            }
            if (blocks == null) blocks = new[] { Block(1), Block(2), Block(3) };
            for (int k = 0; k < 2; k++)
            {
                if (!Cliffs.JumpOf(cycle, k, out var j) || !Cliffs.Cornice(j)) continue;
                float c = Slope.Centre(j.z), reach = Slope.PisteHalf + 6f + Cliffs.JumpBand;
                int salt = cycle * 31 + k * 7 + 1;
                Lip(root, "Cornice", salt, -reach, reach, j.drop,
                    a => new Vector3(c + a, 0f, Cliffs.LipZ(j, a)), Cliffs.JumpAcross, j.face,
                    // cut here and there out at the sides, beyond the piste's edge (never in the line)
                    Slope.PisteHalf + 2f, Slope.PisteHalf + 13f);
                yield return true;
            }
            if (Cliffs.LedgeOf(cycle, out var L) && Cliffs.Cornice(L))
            {
                var line = L;
                for (int i = 0; i < L.count; i++)
                {
                    int ii = i;
                    float reach = L.half + Cliffs.LedgeFade;
                    Lip(root, "CorniceLedge", cycle * 31 + 17 + i * 5, -reach, reach, L[i].drop,
                        b => { float z = Cliffs.LedgeLipZ(line, ii, b); return new Vector3(Slope.Centre(z) + line.side * (line.off + b), 0f, z); },
                        b => Cliffs.LedgeAcrossAt(line, b), L[i].face,
                        L.half + 1f, L.half + Cliffs.LedgeFade * 0.6f);
                    yield return true;
                }
            }
        }

        static float H(int a, int b, int salt) => Slope.Hash01(a, b, salt);

        /// <summary>
        /// One lip's curl, from t0 to t1 across it: at(t) is where the lip is (its x and z), fade(t) how much of the lip
        /// there is there (the curl grows and shrinks with it). Cut, now and then, out between cutFrom and cutTo either side.
        /// </summary>
        static void Lip(Transform root, string name, int salt, float t0, float t1, float drop,
                        System.Func<float, Vector3> at, System.Func<float, float> fade, float face, float cutFrom, float cutTo)
        {
            // the cuts: none, one or two, each a notch in the curl with its blocks below it
            var cuts = new List<float>();
            for (int s = 0; s < 2; s++)
                if (H(salt, 40 + s, 91) < 0.45f)
                {
                    float side = H(salt, 42 + s, 91) < 0.5f ? -1f : 1f;
                    cuts.Add(side * Mathf.Lerp(cutFrom, cutTo, H(salt, 44 + s, 91)));
                }
            int n = Mathf.CeilToInt((t1 - t0) / Step) + 1;
            var V = new Vector3[n * N];
            var uv = new Vector2[n * N];
            var sec = new Vector2[N];
            float big = Mathf.Clamp(drop / 3.6f, 0.65f, 1.6f);
            for (int s = 0; s < n; s++)
            {
                float t = Mathf.Min(t1, t0 + s * Step);
                Vector3 p = at(t);
                // how big the curl is here: with the lip across the run, wandering along it, and pulled back at a cut
                float w = fade(t);
                float wander = 0.6f * Slope.Noise(t * 0.11f + salt * 1.7f, salt * 0.37f + 3.1f) + 0.4f * Slope.Noise(t * 0.37f + salt * 2.9f, salt * 0.61f + 8.3f);
                float cut = 1f;
                foreach (float cc in cuts) cut = Mathf.Min(cut, 1f - 0.65f * Mathf.Exp(-Mathf.Pow((t - cc) / 3.2f, 2f)));
                float size = big * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(w * 1.4f - 0.1f)) * cut;
                Section(p.x, p.z, size, wander, Slope.Noise(t * 0.23f + salt * 0.9f, 5.7f), sec);
                // (the snow's own grain mapped as the ground maps it, x and z over 9 m, so the crown runs on into the
                // in-run with no seam; down the nose and under it, on by how far below the crown, so it never smears)
                float crownY = sec[4].y;
                for (int i = 0; i < N; i++)
                {
                    var v = new Vector3(p.x, sec[i].y, p.z + sec[i].x);
                    V[s * N + i] = v;
                    uv[s * N + i] = new Vector2(v.x / 9f, (v.z + Mathf.Max(0f, crownY - v.y) * 0.9f) / 9f);
                }
            }
            var T = new List<int>((n - 1) * (N - 1) * 6);
            for (int s = 0; s + 1 < n; s++)
                for (int i = 0; i + 1 < N; i++)
                {
                    int a = s * N + i, b = a + 1, c = a + N, d = c + 1;
                    T.Add(a); T.Add(b); T.Add(c); T.Add(b); T.Add(d); T.Add(c);
                }
            // (faces out: the top's first triangle looks up)
            if (Vector3.Cross(V[N * (n / 2) + 5] - V[N * (n / 2) + 4], V[N * (n / 2) + N + 4] - V[N * (n / 2) + 4]).y < 0f)
                for (int q = 0; q < T.Count; q += 3) { int tmp = T[q + 1]; T[q + 1] = T[q + 2]; T[q + 2] = tmp; }
            var mesh = new Mesh { name = name, indexFormat = V.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = V;
            mesh.SetTriangles(T, 0);
            mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = snow;
            var own = go.AddComponent<OwnMesh>(); own.mesh = mesh;

            // the blocks broken off at each cut, rounded, lying at the foot of the face and a little down the landing
            int pick = salt * 101;
            foreach (float cc in cuts)
            {
                int count = 3 + (int)(4f * H(pick, 1, 92));
                for (int b = 0; b < count; b++, pick++)
                {
                    float t = cc + (H(pick, 2, 92) - 0.5f) * 5f;
                    // (out of the line: never back in toward the middle, past where the cut was made)
                    if (Mathf.Abs(t) < cutFrom - 1f) t = Mathf.Sign(cc) * (cutFrom - 1f + H(pick, 7, 92));
                    Vector3 p = at(t);
                    float z = p.z + face * (0.75f + 0.25f * H(pick, 3, 92)) + 6f * H(pick, 4, 92) * H(pick, 4, 92);
                    float sz = (0.35f + 0.75f * H(pick, 5, 92)) * big;
                    var gy = Slope.DrawnHeight(p.x, z);
                    var nn = Slope.Normal(p.x, z);
                    var g = new GameObject("CorniceBlock");
                    g.transform.SetParent(go.transform, false);
                    g.transform.SetPositionAndRotation(new Vector3(p.x, gy - 0.3f * sz, z),
                        Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, nn, 0.7f)) * Quaternion.Euler((H(pick, 8, 92) - 0.5f) * 40f, H(pick, 6, 92) * 360f, (H(pick, 9, 92) - 0.5f) * 30f));
                    g.transform.localScale = new Vector3(sz * (1.1f + 0.5f * H(pick, 10, 92)), sz * (0.7f + 0.3f * H(pick, 11, 92)), sz);
                    g.AddComponent<MeshFilter>().sharedMesh = blocks[(int)(H(pick, 12, 92) * 2.999f)];
                    g.AddComponent<MeshRenderer>().sharedMaterial = snow;
                }
            }
        }

        /// <summary>
        /// One section of the curl, at x across the hill with its lip at lipZ, in (u: metres on down the hill from the lip,
        /// y: height), from the back (buried in the in-run) over the crown and out to the nose, round it and back in under
        /// it to the face: the hollow under the nose is the undercut. Size 0 lays it flat, buried, where the lip runs out.
        /// </summary>
        static void Section(float x, float lipZ, float size, float n1, float n2, Vector2[] p)
        {
            float F(float u) => Slope.DrawnHeight(x, lipZ + u);
            float h0 = F(0f);
            float g = (h0 - F(-1.5f)) / 1.5f;   // the in-run's line at the lip
            // (the back: on the in-run, rising out of it to a soft crown at the lip)
            p[0] = new Vector2(-2.8f, F(-2.8f) - 0.3f);
            p[1] = new Vector2(-1.8f, F(-1.8f) + 0.01f);
            p[2] = new Vector2(-0.9f, F(-0.9f) + 0.03f * size + 0.01f);
            p[3] = new Vector2(-0.35f, F(-0.35f) + 0.06f * size + 0.01f);
            if (size < 0.04f)
            {
                // (no curl here: the section laid flat, a little under the ground, so the mesh runs out into it)
                for (int i = 4; i < N; i++) { float u = -0.35f - 0.02f * (i - 3); p[i] = new Vector2(u, F(u) - 0.05f - 0.01f * (i - 3)); }
                return;
            }
            float R = Mathf.Min(1f, size * (0.48f + 0.22f * n2));     // the nose's roundness: a fat, soft pillow
            float reach = size * (1.1f + 1.3f * n1);                  // how far out the curl stands
            const float k = 0.2f;                                     // how fast its top falls away (faster than a board leaving at a jog)
            float crown = h0 + 0.1f * size;
            float Top(float u) => crown + g * u - k * u * u;
            // (the nose stands clear of the face under it, with room for the hollow: reach on out until it does)
            for (int t = 0; t < 16 && F(reach) > Top(reach) - 2f * R - 0.5f * size; t++) reach += 0.2f;
            float uN = Mathf.Max(0.2f, reach - R);
            var C = new Vector2(uN, Top(uN) - R);
            p[4] = new Vector2(0f, crown);
            p[5] = new Vector2(uN * 0.4f, Top(uN * 0.4f));
            p[6] = new Vector2(uN * 0.78f, Top(uN * 0.78f));
            // round the nose, a little fuller below (wind-packed snow sags)
            float[] ang = { 65f, 25f, -15f, -55f, -100f };
            for (int i = 0; i < 5; i++)
            {
                float rad = ang[i] * Mathf.Deg2Rad;
                float r = R * (ang[i] < 0f ? 1.12f : 1f);
                p[7 + i] = C + new Vector2(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r);
            }
            Vector2 nb = p[11];
            // where the underside meets the face: higher than the nose's bottom (the hollow), found down the face
            float meet = nb.y + 0.45f * size + 0.15f;
            float lo = -0.2f, hi = reach;
            for (int it = 0; it < 14; it++) { float mid = (lo + hi) * 0.5f; if (F(mid) > meet) lo = mid; else hi = mid; }
            float uJ = Mathf.Min(lo, nb.x - 0.1f);
            var J = new Vector2(uJ, F(uJ) - 0.02f);
            // the underside: in from the nose almost level, then up into the hollow to the face
            var Q = new Vector2(Mathf.Lerp(nb.x, uJ, 0.7f), nb.y + 0.1f * (J.y - nb.y));
            for (int i = 0; i < 2; i++)
            {
                float t = (i + 1) / 3f;
                p[12 + i] = (1 - t) * (1 - t) * nb + 2f * (1 - t) * t * Q + t * t * J;
            }
            p[14] = J;
            p[15] = new Vector2(uJ - 0.2f, F(uJ - 0.2f) - 0.3f);   // (on into the face, under it)
        }

        /// <summary>A rounded block of broken cornice: a ball pushed out of round, a few soft facets where it broke.</summary>
        static Mesh Block(int salt)
        {
            const int R = 6, S = 10;
            var V = new List<Vector3>(); var T = new List<int>();
            for (int i = 0; i <= R; i++)
                for (int j = 0; j < S; j++)
                {
                    float th = Mathf.PI * i / R, ph = 2f * Mathf.PI * j / S;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    // (squarish where it broke: pushed toward a cube, and lumpy)
                    var cube = new Vector3(Mathf.Clamp(d.x * 1.4f, -1f, 1f), Mathf.Clamp(d.y * 1.4f, -1f, 1f), Mathf.Clamp(d.z * 1.4f, -1f, 1f));
                    var q = Vector3.Lerp(d, cube, 0.45f) * (0.88f + 0.24f * H(i * 13 + salt, j, 95));
                    V.Add(q * 0.5f);
                }
            for (int i = 0; i < R; i++)
                for (int j = 0; j < S; j++)
                {
                    int a = i * S + j, b = i * S + (j + 1) % S, c = a + S, d = b + S;
                    T.Add(a); T.Add(b); T.Add(c); T.Add(b); T.Add(d); T.Add(c);
                }
            var m = new Mesh { name = "CorniceBlock" + salt };
            m.SetVertices(V); m.SetTriangles(T, 0);
            // (faces out)
            m.RecalculateNormals();
            if (Vector3.Dot(m.normals[S * 3], V[S * 3]) < 0f) { for (int q = 0; q < T.Count; q += 3) { int t = T[q + 1]; T[q + 1] = T[q + 2]; T[q + 2] = t; } m.SetTriangles(T, 0); m.RecalculateNormals(); }
            var uv = new Vector2[V.Count];
            for (int i = 0; i < V.Count; i++) uv[i] = new Vector2(V[i].x + V[i].z, V[i].y) * 0.5f;
            m.uv = uv;
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A lip's curl goes with it when the cycle is thrown away.</summary>
        class OwnMesh : MonoBehaviour
        {
            public Mesh mesh;
            void OnDestroy() { if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }
        }
    }
}
