using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Summit
{
    /// <summary>
    /// Everything around the ride that is not the mountain itself:
    /// falling snow, speed lines and a vignette that grow with speed, and a ring
    /// of distant peaks on the horizon. Built in code, so there is nothing to wire up.
    /// Runs after the chase camera so it can sit exactly where the camera ends up.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class Atmosphere : MonoBehaviour
    {
        Camera cam;
        Rider rider;
        ParticleSystem snow, streaks, mist;
        ParticleSystem.EmissionModule snowEm, streakEm;
        Transform backdrop;
        Vignette vignette;
        float baseVignette = 0.2f, speed01;

        public void Build(Camera camera, Rider r, Material flake)
        {
            cam = camera;
            rider = r;
            snow = MakeSnow(flake);
            mist = MakeMist(flake);
            streaks = MakeStreaks(flake);
            backdrop = MakeBackdrop();

            var vol = Object.FindFirstObjectByType<Volume>();
            if (vol != null && vol.profile != null && vol.profile.TryGet(out vignette))
                baseVignette = vignette.intensity.value;
        }

        // ---------------------------------------------------------------- snowfall

        ParticleSystem MakeSnow(Material mat)
        {
            var go = new GameObject("Snowfall");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.17f);
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // flakes stay put in the world, so speed shows as rush
            main.maxParticles = 14000;
            main.playOnAwake = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(56f, 22f, 70f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);     // a steady cross-wind
            vel.y = new ParticleSystem.MinMaxCurve(-2.6f, -1.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.25f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.9f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            snowEm = ps.emission;
            snowEm.rateOverTime = 2300f;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.material = mat;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- mist

        ParticleSystem MakeMist(Material mat)
        {
            var go = new GameObject("Mist");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(18f, 34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.94f, 0.96f, 1f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 70;
            main.playOnAwake = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(140f, 12f, 120f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.2f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.13f, 0.3f), new GradientAlphaKey(0.13f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var em = ps.emission;
            em.rateOverTime = 6f;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.material = mat;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.maxParticleSize = 3f;
            ps.Play();
            ps.Simulate(10f, true, false);
            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- speed lines

        ParticleSystem MakeStreaks(Material mat)
        {
            var go = new GameObject("SpeedLines");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 17f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // fire back toward the lens
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 0.42f;
            main.startSpeed = 42f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
            main.startColor = new Color(1f, 1f, 1f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 300;
            main.playOnAwake = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 0f;
            shape.radius = 4.2f;
            shape.radiusThickness = 0.35f;   // a ring round the edge of the view, never across the rider
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            streakEm = ps.emission;
            streakEm.rateOverTime = 0f;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.material = mat;
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.velocityScale = 0.06f;
            pr.lengthScale = 2f;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- distant peaks

        Transform MakeBackdrop()
        {
            var root = new GameObject("Backdrop").transform;
            root.SetParent(transform, false);
            // Sprites/Default ignores fog, so the peaks stay visible past the haze; drawn just after
            // the sky and before other see-through things, and hidden wherever the mountain is nearer
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("UI/Default");
            // three ranges, each a clear step darker and crisper as it comes closer: that
            // stepping is what makes the distance read
            Ring(root, "FarPeaks", new Material(sh) { renderQueue = 2508 }, 885f, 110f, 330f, 3.1f,
                 new Color(0.70f, 0.76f, 0.88f), new Color(0.97f, 0.98f, 1.00f));
            Ring(root, "MidPeaks", new Material(sh) { renderQueue = 2509 }, 760f, 50f, 210f, 7.7f,
                 new Color(0.44f, 0.52f, 0.68f), new Color(0.94f, 0.96f, 1.00f));
            Ring(root, "NearRidge", new Material(sh) { renderQueue = 2510 }, 640f, 10f, 120f, 12.3f,
                 new Color(0.20f, 0.25f, 0.37f), new Color(0.86f, 0.86f, 0.92f));
            return root;
        }

        static void Ring(Transform parent, string name, Material mat, float radius, float minH, float maxH,
                         float seed, Color rock, Color cap)
        {
            const int N = 360, Rows = 5;
            var v = new Vector3[(N + 1) * Rows];
            var c = new Color[v.Length];
            var t = new int[N * (Rows - 1) * 6];
            Color fog = RenderSettings.fogColor;
            var tops = new float[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float u = i / (float)N * 24f;
                // ridged noise along the horizon: sharp peaks, softer cols
                float h = 0f, amp = 1f, f = 1f, norm = 0f;
                for (int o = 0; o < 4; o++)
                {
                    float n = Mathf.PerlinNoise(u * f + seed, seed * 1.7f + o * 13.1f);
                    h += (1f - Mathf.Abs(n * 2f - 1f)) * amp;
                    norm += amp; amp *= 0.5f; f *= 2.1f;
                }
                h = h / norm;
                tops[i] = Mathf.Lerp(minH, maxH, h * h);
            }
            for (int i = 0; i <= N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                float top = tops[i];
                // faces turned toward the sun are lighter, the others in blue shadow
                float slope = (tops[Mathf.Min(N, i + 6)] - tops[Mathf.Max(0, i - 6)]) * 0.012f;
                float lit = Mathf.Clamp(0.5f + slope, 0f, 1f);
                Color rockLit = Color.Lerp(rock * 0.7f, Color.Lerp(rock, cap, 0.3f), lit); rockLit.a = 1f;
                Color capLit = Color.Lerp(Color.Lerp(cap, rock, 0.45f), cap, lit); capLit.a = 1f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
                float snowLine = top - Mathf.Lerp(20f, 55f, Mathf.InverseLerp(minH, maxH, top));
                int k = i * Rows;
                v[k] = dir + Vector3.up * top;                 c[k] = capLit;           // peak
                v[k + 1] = dir + Vector3.up * snowLine;        c[k + 1] = capLit;       // snow line
                v[k + 2] = dir + Vector3.up * (snowLine - 70f); c[k + 2] = rockLit;     // bare rock below the snow
                // below the rock the range dissolves into the haze: fade it out so the sky shows, no curtain
                Color fogClear = fog; fogClear.a = 0f;
                Color rockFade = Color.Lerp(rockLit, fog, 0.5f); rockFade.a = 0.5f;
                v[k + 3] = dir + Vector3.up * (snowLine - 130f); c[k + 3] = rockFade;
                v[k + 4] = dir + Vector3.up * (snowLine - 220f); c[k + 4] = fogClear;
                if (i < N)
                    for (int r = 0; r < Rows - 1; r++)
                    {
                        int q = (i * (Rows - 1) + r) * 6, p0 = k + r, p1 = k + Rows + r;
                        t[q] = p0; t[q + 1] = p1; t[q + 2] = p0 + 1;
                        t[q + 3] = p0 + 1; t[q + 4] = p1; t[q + 5] = p1 + 1;
                    }
            }
            var mesh = new Mesh { name = name, vertices = v, colors = c, triangles = t };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 3f);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ---------------------------------------------------------------- per frame

        void LateUpdate()
        {
            if (cam == null || rider == null) return;
            Vector3 cp = cam.transform.position;

            // the peaks are infinitely far away: they travel with the camera
            backdrop.position = cp;

            // keep the snow box ahead of the camera, where the rider is heading
            Vector3 fwd = cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            snow.transform.position = cp + fwd * 22f + Vector3.up * 6f;
            // banks of mist sit low on the slope ahead and drift across it
            mist.transform.position = cp + fwd * 70f + Vector3.down * 18f;

            float s = rider.Running ? Mathf.Clamp01((rider.Speed * 3.6f - 55f) / 60f) : 0f;   // 55 to 115 km/h
            if (rider.Running && rider.BoostTime > 0f) s = Mathf.Max(s, 0.9f);
            speed01 = Mathf.Lerp(speed01, s, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            streakEm.rateOverTime = speed01 * speed01 * 110f;
            snowEm.rateOverTime = 2300f + 900f * speed01;
            if (vignette != null) vignette.intensity.value = baseVignette + 0.17f * speed01;
        }
    }
}
