using UnityEngine;

namespace Summit
{
    /// <summary>
    /// A strip of caution tape tied chest-high between two posts or trees. It hangs nearly taut and flutters
    /// in the wind; when the rider rides through it, it catches, stretches, snaps, and the two torn halves
    /// spring back toward their ties and stream in the wind instead of lying in the snow.
    /// A little rope of points (Verlet), drawn as a ribbon with the printed tape on both faces.
    /// </summary>
    public class CautionTape : MonoBehaviour
    {
        const int N = 34;
        const float Width = 0.13f, Tile = 2.1f;   // ribbon height; metres per repeat of the print
        readonly Vector3[] p = new Vector3[N], prev = new Vector3[N];
        float rest;
        int broken = -1;                           // the link that tore (-1: whole)
        int pull = -1;                             // the link the rider is dragging along before it snaps
        float pullZ;                               // where the tape hung across the run
        const float SnapStretch = 2.4f;            // how far past its line the rider drags it before it gives
        Rider rider;
        Mesh mesh;
        Vector3[] verts; Vector2[] uvs; int[] tris;
        float t0;
        float recoil;                              // 0..1 after a snap: the stretched plastic shrinking back

        public bool Broken => broken >= 0;
        public System.Action<Vector3> Torn;

        public static CautionTape Make(Transform parent, Vector3 a, Vector3 b, Rider rider, Material mat)
        {
            var go = new GameObject("CautionTape", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var tape = go.AddComponent<CautionTape>();
            tape.rider = rider;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tape.Init(a, b);
            return tape;
        }

        void Init(Vector3 a, Vector3 b)
        {
            rest = (b - a).magnitude / (N - 1) * 0.995f;   // tied under tension: a shade shorter than the gap, so it hangs taut
            for (int i = 0; i < N; i++)
            {
                float u = i / (N - 1f);
                p[i] = prev[i] = Vector3.Lerp(a, b, u) + Vector3.down * (0.12f * 4f * u * (1f - u));
            }
            mesh = new Mesh { name = "Tape" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            verts = new Vector3[N * 4]; uvs = new Vector2[N * 4];
            tris = new int[(N - 1) * 12];
            t0 = Random.value * 10f;
            for (int k = 0; k < 40; k++) Step(1f / 60f);   // settle into its sag before anyone sees it
            Draw();
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }   // the gate is rebuilt each mountain: no leaking meshes

        void LateUpdate()
        {
            float dt = Mathf.Min(ChaseCamera.TestDt > 0f ? ChaseCamera.TestDt : Time.deltaTime, 1f / 30f);
            if (dt <= 0f) return;
            // well past it (or far from it), it's out of sight: stop simulating it
            if (rider != null && Mathf.Abs(rider.transform.position.z - p[N / 2].z) > 150f) return;
            if (broken < 0 && rider != null && rider.Running) { if (pull < 0) CheckHit(); else CheckSnap(); }
            if (broken >= 0) recoil = Mathf.MoveTowards(recoil, 1f, dt * 2.5f);
            Step(dt);
            Draw();
        }

        void CheckHit()
        {
            Vector3 r = rider.transform.position;
            float z = (p[0].z + p[N - 1].z) * 0.5f;
            if (r.z < z - 0.4f || r.z > z + 1.5f) return;
            // which link the rider went through (they must be between the posts and not flying high over it)
            float lo = Mathf.Min(p[0].x, p[N - 1].x), hi = Mathf.Max(p[0].x, p[N - 1].x);
            if (r.x < lo || r.x > hi) return;
            int k = Mathf.Clamp(Mathf.FloorToInt((r.x - p[0].x) / (p[N - 1].x - p[0].x) * (N - 1)), 1, N - 3);
            if (r.y - p[k].y > 1.2f || p[k].y - r.y > 2.2f) return;   // flying over it, or ducking under
            // caught on the rider: it stretches round them, dragged downhill, until it snaps
            pull = k; pullZ = z;
        }

        Vector3 Grab(int i)
        {
            // where the tape is held on the rider: across their shins, either side of the middle
            Vector3 r = rider.transform.position + Vector3.up * 1.05f;   // across the chest
            Vector3 along = (p[N - 1] - p[0]).normalized;
            return r + along * (i == pull ? -0.15f : 0.15f);
        }

        void CheckSnap()
        {
            Vector3 r = rider.transform.position;
            float lo = Mathf.Min(p[0].x, p[N - 1].x) - 1f, hi = Mathf.Max(p[0].x, p[N - 1].x) + 1f;
            bool gone = r.z - pullZ > SnapStretch || rider.Crashed || r.x < lo || r.x > hi || r.z < pullZ - 1f;
            if (!gone) return;
            int k = pull;
            pull = -1;
            broken = k;
            // it snaps back toward its posts, harder the further it was stretched, with a flick upward
            float stretch = Mathf.Clamp01((r.z - pullZ) / SnapStretch);
            for (int i = 1; i < N - 1; i++)
            {
                Vector3 post = i <= k ? p[0] : p[N - 1];
                float d = Mathf.Abs(i - (k + 0.5f));
                float w = Mathf.Exp(-d / 6f);
                Vector3 v = (post - p[i]) * (2.5f * stretch) * w + Vector3.up * 2f * w + rider.Velocity * 0.2f * w;
                prev[i] = p[i] - v * (1f / 60f);
            }
            Torn?.Invoke(r);
        }

        void Step(float dt)
        {
            float t = Time.time + t0;
            for (int i = 1; i < N - 1; i++)
            {
                Vector3 vel = (p[i] - prev[i]) * (broken < 0 ? 0.96f : 0.985f);
                prev[i] = p[i];
                // wind blowing down the mountain, gusting, and a flutter that runs along the tape
                float gust = 0.6f + 0.4f * Mathf.Sin(t * 0.9f) + 0.25f * Mathf.Sin(t * 2.3f + 1f);
                Vector3 wind = new Vector3(0.6f * Mathf.Sin(t * 5.1f + i * 0.9f), 1.6f * Mathf.Sin(t * 7.3f + i * 1.3f),
                                           (broken < 0 ? 1.2f : 6f) * gust);
                p[i] += vel + (Vector3.down * 9.8f + wind) * dt * dt;
            }
            // the loose ends after the tear are free; the posts' ends stay tied on
            if (broken >= 0)
            {
                for (int s = 0; s < 2; s++)
                {
                    int i = s == 0 ? broken : broken + 1;
                    Vector3 vel = (p[i] - prev[i]) * 0.985f; prev[i] = p[i];
                    float gust = 0.6f + 0.4f * Mathf.Sin(t * 0.9f);
                    p[i] += vel + (Vector3.down * 9.8f + new Vector3(0f, 2f * Mathf.Sin(t * 8f + i), 6f * gust)) * dt * dt;
                }
            }
            if (pull >= 0) { p[pull] = Grab(pull); p[pull + 1] = Grab(pull + 1); prev[pull] = p[pull]; prev[pull + 1] = p[pull + 1]; }
            for (int it = 0; it < (broken < 0 ? 28 : 10); it++)   // (enough passes that a tied tape really holds its tension)
                for (int i = 0; i < N - 1; i++)
                {
                    if (i == broken) continue;
                    Vector3 d = p[i + 1] - p[i];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    // once snapped, the stretched plastic shrinks back, so each half hangs short off its tie
                    float r = rest * Mathf.Lerp(1f, 0.28f, recoil);
                    Vector3 c = d * (0.5f * (len - r) / len);
                    bool pinA = i == 0 || (pull >= 0 && (i == pull || i == pull + 1));
                    bool pinB = i + 1 == N - 1 || (pull >= 0 && (i + 1 == pull || i + 1 == pull + 1));
                    if (pinA && pinB) continue;
                    if (pinA) p[i + 1] -= 2f * c;
                    else if (pinB) p[i] += 2f * c;
                    else { p[i] += c; p[i + 1] -= c; }
                }
            // never through the snow
            for (int i = 1; i < N - 1; i++)
            {
                float g = Slope.DrawnHeight(p[i].x, p[i].z) + 0.03f;
                if (p[i].y < g) { p[i].y = g; prev[i] = Vector3.Lerp(prev[i], p[i], 0.5f); }
            }
        }

        void Draw()
        {
            float t = Time.time + t0;
            float u = 0f;
            for (int i = 0; i < N; i++)
            {
                Vector3 dir = i < N - 1 ? p[i + 1] - p[i] : p[i] - p[i - 1];
                if (i == broken) dir = p[i] - p[Mathf.Max(0, i - 1)];
                dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.right;
                Vector3 up = Vector3.up - dir * Vector3.Dot(Vector3.up, dir);
                up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.forward;
                // the tape twists as it flutters (more once it is loose)
                float twist = (broken < 0 ? 0.25f : 0.9f) * Mathf.Sin(t * 6f + i * 0.6f);
                up = Quaternion.AngleAxis(twist * Mathf.Rad2Deg, dir) * up;
                Vector3 local = transform.InverseTransformPoint(p[i]);
                Vector3 h = transform.InverseTransformVector(up * (Width * 0.5f));
                if (i > 0 && i - 1 != broken) u += rest / Tile;   // the print stretches with the plastic
                else if (i > 0) u += 0.5f;
                // front face and back face (separate vertices, so each lights from its own side)
                verts[i * 4] = local - h; verts[i * 4 + 1] = local + h;
                verts[i * 4 + 2] = local - h; verts[i * 4 + 3] = local + h;
                uvs[i * 4] = new Vector2(u, 0f); uvs[i * 4 + 1] = new Vector2(u, 1f);
                uvs[i * 4 + 2] = new Vector2(-u, 0f); uvs[i * 4 + 3] = new Vector2(-u, 1f);   // reads the right way round from behind
            }
            int n = 0;
            for (int i = 0; i < N - 1; i++)
            {
                if (i == broken) { for (int k = 0; k < 12; k++) tris[n++] = 0; continue; }
                int a = i * 4, b = (i + 1) * 4;
                tris[n++] = a; tris[n++] = a + 1; tris[n++] = b;
                tris[n++] = b; tris[n++] = a + 1; tris[n++] = b + 1;
                tris[n++] = a + 2; tris[n++] = b + 2; tris[n++] = a + 3;
                tris[n++] = b + 2; tris[n++] = b + 3; tris[n++] = a + 3;
            }
            mesh.vertices = verts; mesh.uv = uvs;
            if (mesh.GetIndexCount(0) != (uint)tris.Length) mesh.triangles = tris; else mesh.SetTriangles(tris, 0);   // (reading mesh.triangles allocates)
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
