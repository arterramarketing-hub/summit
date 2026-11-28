using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Summit
{
    // Camp, part: the cliff edge, and the edges giving way under you
    public partial class Camp
    {
        // ---------------------------------------------------------------- the cliff edge

        // The camp's right-hand side is the top of a sheer cliff (Slope.Summit): a cable railing along the edge, as
        // at a summit lookout (square grey posts every 2.5 m on a concrete kerb, six taut steel cables), and three
        // tall flagpoles inside it with red-and-white flags streaming in the wind off the drop. The railing is one
        // mesh per material; the flags are small cloth grids moved on the CPU each frame (a few hundred vertices).
        const float EdgeX = 24.1f, EdgeZ0 = -108f, EdgeZ1 = -41f;
        Material railMetal, kerbMat, flagMat, mistMat;
        readonly List<Mesh> flagMeshes = new List<Mesh>();
        readonly List<Vector3> flagRoots = new List<Vector3>();
        readonly List<Renderer> flagRends = new List<Renderer>();
        Vector3[] flagWork;
        float flagClock;
        const int FlagNx = 14, FlagNy = 7;
        const float FlagL = 2.6f, FlagH = 1.3f;
        static readonly Vector3 Wind = new Vector3(0.15f, 0f, 1f).normalized;   // off the mountain, down past the camp

        void CliffEdge()
        {
            if (railMetal == null)
            {
                railMetal = M(new Color(0.52f, 0.54f, 0.56f), 0.55f); railMetal.SetFloat("_Metallic", 0.7f);
                kerbMat = M(new Color(0.66f, 0.65f, 0.62f), 0.15f);
                flagMat = M(Color.white, 0.1f, Resources.Load<Texture2D>("Camp/Flag")); flagMat.SetFloat("_Cull", 0f);
            }
            var t = new GameObject("CliffRail").transform; t.SetParent(root, false);
            int n0 = Mathf.RoundToInt((EdgeZ1 - EdgeZ0) / 2.5f);
            // (a gap where the lookout's deck goes out over the lip, a post either side of it at the deck's sides: its own
            // rail takes over there)
            float e0 = LookoutZ - LookoutHalfW - 0.1f, e1 = LookoutZ + LookoutHalfW + 0.1f;
            var zs = new List<float> { e0, e1 };
            for (int i = 0; i <= n0; i++) { float z = Mathf.Lerp(EdgeZ0, EdgeZ1, i / (float)n0); if (z < e0 - 0.8f || z > e1 + 0.8f) zs.Add(z); }
            zs.Sort();
            int n = zs.Count - 1;
            var posts = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) posts[i] = Ground(EdgeX, zs[i]);
            for (int i = 0; i <= n; i++)
            {
                P(PrimitiveType.Cube, t, posts[i] + Vector3.up * 0.6f, new Vector3(0.08f, 1.22f, 0.08f), Vector3.zero, railMetal);
                P(PrimitiveType.Cube, t, posts[i] + Vector3.up * 1.22f, new Vector3(0.11f, 0.03f, 0.11f), Vector3.zero, railMetal);   // the cap
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 a = posts[i], b = posts[i + 1];
                if (zs[i] == e0 && zs[i + 1] == e1) continue;   // (the deck)
                // the kerb, and six cables pulled tight between the posts
                var k = Log(t, a + Vector3.up * 0.1f, b + Vector3.up * 0.1f, 0.3f, kerbMat);
                k.localScale = new Vector3(0.36f, k.localScale.y, 0.24f);
                for (int c = 0; c < 6; c++)
                {
                    float h = 0.24f + c * 0.18f;
                    Log(t, a + Vector3.up * h, b + Vector3.up * h, 0.012f, railMetal);
                }
            }
            Combine(t);
            // you walk up to it, not through it (the camp's edge stops you first anyway); either side of the deck
            float g0 = LookoutZ - LookoutHalfW - 0.2f, g1 = LookoutZ + LookoutHalfW + 0.2f;
            Block(Frame(EdgeX + 0.4f, (EdgeZ0 - 1f + g0) * 0.5f, 0f), Vector3.zero, new Vector2(0.45f, (g0 - EdgeZ0 + 1f) * 0.5f));
            Block(Frame(EdgeX + 0.4f, (g1 + EdgeZ1 + 1f) * 0.5f, 0f), Vector3.zero, new Vector2(0.45f, (EdgeZ1 + 1f - g1) * 0.5f));

            // three flagpoles just inside the rail
            flagMeshes.Clear(); flagRoots.Clear(); flagRends.Clear();
            var poleM = M(new Color(0.88f, 0.89f, 0.9f), 0.6f); poleM.SetFloat("_Metallic", 0.5f);
            var gold = M(new Color(0.9f, 0.72f, 0.3f), 0.8f); gold.SetFloat("_Metallic", 0.9f);
            foreach (var z in new[] { -77.5f, -72.5f, -59.5f })   // (either side of the lookout's deck: camplayout)
            {
                var g = Ground(EdgeX - 1.6f, z);
                Log(t.parent, g + Vector3.down * 0.3f, g + Vector3.up * 6f, 0.11f, poleM);
                P(PrimitiveType.Sphere, t.parent, g + Vector3.up * 6.08f, Vector3.one * 0.16f, Vector3.zero, gold);
                P(PrimitiveType.Cylinder, t.parent, g + Vector3.up * 0.15f, new Vector3(0.5f, 0.15f, 0.5f), Vector3.zero, kerbMat);
                Block(Frame(EdgeX - 1.6f, z, 0f), Vector3.zero, new Vector2(0.25f, 0.25f));
                var top = g + Vector3.up * 5.85f;
                flagRoots.Add(top);
                var fo = new GameObject("Flag", typeof(MeshFilter), typeof(MeshRenderer));
                fo.transform.SetParent(root, false);
                var mesh = FlagMesh();
                fo.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = fo.GetComponent<MeshRenderer>(); mr.sharedMaterial = flagMat;
                flagMeshes.Add(mesh); flagRends.Add(mr);
                // the middle one has a lamp at its foot, lighting all three from below at night
                if (z > -75f && z < -70f) Lamp(t.parent, g + Vector3.up * 1.2f + Vector3.left * 0.8f, 11f, 1.8f);
            }
            WaveFlags(0f);
        }

        /// <summary>A flag's cloth: a grid, both sides (each with its own normals), u along the fly, v up.</summary>
        Mesh FlagMesh()
        {
            int nv = (FlagNx + 1) * (FlagNy + 1);
            var uv = new Vector2[nv * 2];
            var tri = new List<int>();
            for (int j = 0; j <= FlagNy; j++)
                for (int i = 0; i <= FlagNx; i++)
                {
                    var c = new Vector2(i / (float)FlagNx, j / (float)FlagNy);
                    uv[j * (FlagNx + 1) + i] = c; uv[nv + j * (FlagNx + 1) + i] = c;
                }
            for (int j = 0; j < FlagNy; j++)
                for (int i = 0; i < FlagNx; i++)
                {
                    int a = j * (FlagNx + 1) + i, b = a + 1, c = a + FlagNx + 1, d = c + 1;
                    tri.AddRange(new[] { a, c, b, b, c, d });
                    tri.AddRange(new[] { nv + a, nv + b, nv + c, nv + b, nv + d, nv + c });   // the other side
                }
            var m = Made(new Mesh { name = "Flag" });
            m.MarkDynamic();
            m.vertices = new Vector3[nv * 2]; m.uv = uv; m.SetTriangles(tri, 0);
            return m;
        }

        /// <summary>
        /// The flags stream out hard downwind: waves run from the pole to the fly end, growing as they go, with a
        /// quicker flutter on top, the cloth sagging a little and the fly end whipping.
        /// </summary>
        void WaveFlags(float dt)
        {
            if (flagMeshes.Count == 0) return;
            bool seen = false;
            foreach (var r in flagRends) if (r != null && (r.isVisible || dt <= 0f || ChaseCamera.TestDt > 0f)) seen = true;
            if (!seen) return;
            flagClock += dt;
            int nv = (FlagNx + 1) * (FlagNy + 1);
            if (flagWork == null || flagWork.Length != nv * 2) flagWork = new Vector3[nv * 2];
            var side = Vector3.Cross(Vector3.up, Wind);
            for (int f = 0; f < flagMeshes.Count; f++)
            {
                float ph = f * 1.7f, t = flagClock;
                for (int j = 0; j <= FlagNy; j++)
                    for (int i = 0; i <= FlagNx; i++)
                    {
                        float u = i / (float)FlagNx, v = j / (float)FlagNy;
                        float amp = 0.22f * Mathf.Pow(u, 1.1f);
                        float wave = Mathf.Sin(u * 9.5f - t * 11f + ph + v * 0.9f) + 0.35f * Mathf.Sin(u * 23f - t * 27f + ph * 2f + v * 2.1f);
                        float along = u * FlagL * (1f - 0.04f * Mathf.Abs(wave) * u);
                        var p = Wind * along + Vector3.up * (v * FlagH - FlagH - 0.18f * u * u + 0.05f * u * Mathf.Sin(t * 7f + u * 6f + ph))
                              + side * (amp * wave);
                        flagWork[j * (FlagNx + 1) + i] = flagRoots[f] + p;
                        flagWork[nv + j * (FlagNx + 1) + i] = flagRoots[f] + p;
                    }
                var m = flagMeshes[f];
                m.vertices = flagWork;
                m.RecalculateNormals();
                m.RecalculateBounds();
            }
        }

        /// <summary>Merge everything under t into one mesh per material (the railing's two hundred pieces: two draws).</summary>
        void Combine(Transform t)
        {
            var byMat = new Dictionary<Material, List<CombineInstance>>();
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                var mat = mf.GetComponent<MeshRenderer>().sharedMaterial;
                if (!byMat.TryGetValue(mat, out var list)) byMat[mat] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = t.worldToLocalMatrix * mf.transform.localToWorldMatrix });
            }
            for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i).gameObject; if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            foreach (var kv in byMat)
            {
                var mesh = Made(new Mesh { name = "Combined", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var g = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(t, false);
                g.GetComponent<MeshFilter>().sharedMesh = mesh;
                g.GetComponent<MeshRenderer>().sharedMaterial = kv.Key;
            }
        }

        /// <summary>Cloud drifting below the summit, over the drop on the right and down the slopes on the left.</summary>
        void Mist()
        {
            if (mistMat == null && soft != null)
            {
                mistMat = new Material(soft);
                mistMat.SetTexture("_BaseMap", Resources.Load<Texture2D>("Camp/Cloud"));
                mistMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.75f));
            }
            if (mistMat == null) return;
            float top = Ground(0f, -70f).y;
            MistBank(new Vector3(Cx + 200f, top - 150f, -120f), new Vector3(300f, 220f, 420f), 26, 70f, 160f);   // over the drop
            MistBank(new Vector3(Cx - 190f, top - 150f, -90f), new Vector3(200f, 120f, 320f), 12, 50f, 110f);   // below the left slopes
        }

        void MistBank(Vector3 centre, Vector3 size, int count, float small, float big)
        {
            var go = new GameObject("Mist");
            go.transform.SetParent(root, false);
            go.transform.position = centre;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 240f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(small, big);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
            var em = ps.emission; em.rateOverTime = count / 240f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 0.4f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(1.5f, 1.5f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mistMat;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.maxParticleSize = 3f;
            ps.Play();
        }

        // ---------------------------------------------------------------- the edges give way

        // There is no fence round the camp. Walk too near any real drop (the cliff past the ends of its railing, the
        // start pad's lip, the summit's slopes where they steepen into rock) and the snow's edge breaks off under you:
        // a cornice splits away with a crack and a burst of powder, and you go down with it, thrown as a crash
        // throws you on the run (RiderBody's ragdoll), tumbling all the way. The camera watches from the top, then
        // follows, and stays on the body for as long as you like: when you tap (at any moment, even mid-fall) the
        // screen fades and you're back at the chalet. It never moves on by itself. The game adds the screams, the "nooo" and the thump (Collapsed, Rescued, RiderBody.Killed).
        /// <summary>The edge broke under the walker: where, and which way it fell.</summary>
        public System.Action<Vector3, Vector3> Collapsed;
        /// <summary>Back at the chalet after a fall.</summary>
        public System.Action Rescued;
        public bool Falling => falling;
        const float EdgeDrop = 1.3f;   // more than this lower within a stride of you is an edge (about 55 degrees)
        bool falling, fallTapped;
        float fallT, restFor, fade;
        readonly ResumeTap fallTap = new ResumeTap();
        /// <summary>Test tools: tapped to come back from a fall (the fade is under way).</summary>
        public bool FallTapped => fallTapped;
        Vector3 fallDir, fallCamFrom, fallLook;
        GameObject cornice;
        Material fracture;
        Vector3 fallEdge;
        MetaUi fadeUi;
        readonly Collider[] near = new Collider[12];

        /// <summary>Stand clear of the trees and rocks round you (the forest's own colliders): real things block you.</summary>
        Vector3 OffTrees(Vector3 p)
        {
            int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * 0.9f, 0.45f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = near[i];
                if (c == null || c.GetComponentInParent<Obstacle>() == null) continue;
                Vector3 at = p + Vector3.up * 0.9f, cp = c.ClosestPoint(at);
                var d = at - cp; d.y = 0f;
                float m = d.magnitude;
                if (m < 1e-4f) { d = p - c.bounds.center; d.y = 0f; m = d.magnitude; if (m < 1e-4f) continue; }
                if (m < 0.45f) p += d / m * (0.45f - m);
            }
            return p;
        }

        /// <summary>Is there a drop beside you, in the way you're walking or right round your feet? Then it goes.</summary>
        void EdgeCheck(Vector3 f)
        {
            if (falling) return;
            float here = Slope.DrawnHeight(pos.x, pos.z), worst = 0f;
            Vector3 dir = f;
            if (speed > 0.2f)
            {
                float ahead = here - Slope.DrawnHeight(pos.x + f.x * 1.3f, pos.z + f.z * 1.3f);
                if (ahead > worst) { worst = ahead; dir = f; }
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float drop = here - Slope.DrawnHeight(pos.x + d.x * 0.85f, pos.z + d.z * 0.85f);
                if (drop > worst) { worst = drop; dir = d; }
            }
            if (worst > EdgeDrop) Collapse(dir);
        }

        /// <summary>The cornice breaks: a chunk of the lip falls away, and you with it.</summary>
        public void Collapse(Vector3 dir)
        {
            if (falling || rider == null) return;
            falling = true; fallT = 0f; restFor = 0f; fallTapped = false;
            // (the thumb that was walking you to the edge isn't a tap when it lifts)
            var now = Touches();
            fallTap.Arm(now.Count > 0, RiderInput.SpaceHeld());
            dir.y = 0f; fallDir = dir.normalized;
            speed = 0f;
            fallCamFrom = cam != null ? cam.transform.position : pos + Vector3.up * 3f;
            fallLook = pos + Vector3.up;
            fallEdge = pos;
            // the chunk: lumpy snow, half a metre deep, falling out and away (it only has to look right as it goes)
            if (cornice != null) { if (Application.isPlaying) Destroy(cornice); else DestroyImmediate(cornice); }
            cornice = new GameObject("Cornice");
            cornice.transform.SetParent(root, false);
            cornice.transform.SetPositionAndRotation(pos + fallDir * 0.7f + Vector3.down * 0.35f, Quaternion.LookRotation(fallDir));
            P(PrimitiveType.Sphere, cornice.transform, Vector3.zero, new Vector3(2.4f, 0.8f, 1.8f), Vector3.zero, snowCap);
            P(PrimitiveType.Sphere, cornice.transform, new Vector3(0.7f, -0.2f, 0.3f), new Vector3(1.3f, 0.7f, 1.2f), new Vector3(0f, 30f, 0f), snowCap);
            P(PrimitiveType.Sphere, cornice.transform, new Vector3(-0.8f, 0.05f, -0.1f), new Vector3(1.1f, 0.6f, 1.1f), new Vector3(0f, 70f, 0f), snowCap);
            // the fresh break on its back face: packed, icy, blue-grey, so it reads against the snow it came from
            if (fracture == null) fracture = M(new Color(0.55f, 0.66f, 0.82f), 0.55f);
            P(PrimitiveType.Cube, cornice.transform, new Vector3(0f, -0.05f, -0.55f), new Vector3(2.2f, 0.7f, 0.5f), new Vector3(-12f, 0f, 0f), fracture);
            P(PrimitiveType.Cube, cornice.transform, new Vector3(0.6f, -0.1f, -0.45f), new Vector3(0.9f, 0.55f, 0.4f), new Vector3(-8f, 25f, 4f), fracture);
            var rb = cornice.AddComponent<Rigidbody>();
            rb.mass = 300f; rb.linearDamping = 0.1f;
            rb.linearVelocity = fallDir * 1.4f + Vector3.down * 0.6f;
            rb.angularVelocity = Vector3.Cross(Vector3.up, fallDir) * 1.6f;
            // and you: thrown out after it, as a crash throws you on the run
            body.Tumble(fallDir * 2.2f + Vector3.up * 0.8f);
            rider.DoCrash("Cornice");
            Collapsed?.Invoke(pos, fallDir);
        }

        void FallStep(float dt)
        {
            fallT += dt;
            Footsteps(dt);   // (fades the steps out)
            // a tap (whenever: still falling, or lying at the bottom): fade, and back to the chalet. Until then the camera
            // stays on the body
            var ts = Touches();
            if (fallTap.Step(ts.Count > 0, ts.Count > 0 ? ts[0].pos : Vector2.zero, RiderInput.SpaceHeld(), dt, cam != null ? Mathf.Min(cam.pixelWidth, cam.pixelHeight) : Mathf.Min(Screen.width, Screen.height)))
                fallTapped = true;
            restFor = body.RagdollMoving || fallT < 2.5f ? 0f : restFor + dt;
            bool done = fallTapped;
            fade = Mathf.MoveTowards(fade, done ? 1f : 0f, dt / 0.35f);
            if (fadeUi != null) fadeUi.SetFade(fade);
            if (cornice != null && fallT > 9f) { if (Application.isPlaying) Destroy(cornice); else DestroyImmediate(cornice); }
            if (done && fade >= 1f) Rescue();
        }

        /// <summary>Back on your feet at the chalet, patched up (the fade lifts as the camp comes back).</summary>
        void Rescue()
        {
            falling = false;
            if (cornice != null) { if (Application.isPlaying) Destroy(cornice); else DestroyImmediate(cornice); }
            var at = stairsFoot + Quaternion.Euler(0f, ChaletYaw, 0f) * Vector3.forward * 2f;
            rider.ResetAt(at.x, at.z);
            body.ResetVisual();
            body.SetWalking(true);
            pos = Ground(at.x - Cx, at.z);
            yaw = ChaletYaw * Mathf.Deg2Rad; camYaw = yaw; speed = 0f;   // looking out from the chalet
            snap = true;
            Place();
            lastStep = body.WalkPhase;
            fadeIn = true;
            Rescued?.Invoke();
        }
        bool fadeIn;

        /// <summary>The camera during a fall: holding at the top a moment, looking over after the body, then going down with it.</summary>
        void FallCamera(float dt)
        {
            Vector3 c = body.RagdollCentre;
            // a moment out over the edge, looking down after it (any longer and the cliff's mist fills the view); then
            // down with it, out in the open air off the face and a little above, looking back at it against the rock
            // (from behind, the lip it went over hides it)
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1.9f, fallT));
            Vector3 side = Vector3.Cross(Vector3.up, fallDir);
            Vector3 over = fallEdge - fallDir * 1.2f + Vector3.up * 3.2f + side * 1.5f;
            Vector3 follow = c + fallDir * 6f + Vector3.up * 3.5f + side * 4f;
            Vector3 want = Vector3.Lerp(over, follow, s);
            want.y = Mathf.Max(want.y, Slope.DrawnHeight(want.x, want.z) + 1.5f);
            // keep the body in sight: where the slope between it and the camera would hide it (a lip it went over, a
            // buttress), lift the camera until the line clears (seven height samples, only while falling)
            Vector3 from = c + Vector3.up * 0.4f;
            float lift = 0f;
            for (int i = 1; i <= 7; i++)
            {
                float t = i / 8f;
                var q = Vector3.Lerp(from, want, t);
                float under = Slope.DrawnHeight(q.x, q.z) + 0.5f - q.y;
                if (under > 0f) lift = Mathf.Max(lift, under / t);
            }
            want.y += Mathf.Min(lift, 25f);
            float k = 1f - Mathf.Exp(-dt * 5f);
            var cp = Vector3.Lerp(cam.transform.position, want, k);
            fallLook = Vector3.Lerp(fallLook, c, 1f - Mathf.Exp(-dt * 8f));
            cam.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(fallLook - cp, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 60f, k);
        }
    }
}
