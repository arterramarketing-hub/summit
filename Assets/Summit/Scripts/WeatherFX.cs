using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// What the weather adds to the world besides its light (Weather.cs), all of it scaled by the Look and off at zero:
    ///  - flakes on the lens: snow (or fog's droplets) landing on the camera and melting, more at speed and into the wind;
    ///  - ground drift: snow snaking down the slope ahead in the wind, a hand's breadth to half a metre off the surface;
    ///  - diamond dust: ice crystals glittering in the air on the coldest clear mornings and nights;
    ///  - the cloud sea's deck: a sheet of cloud filling the valley below the run, thinning to nothing at its shore,
    ///    that you ride down into;
    ///  - on the start page (its own layer, FarMountain.Layer): falling snow before the helicopter, the far mountain's
    ///    haze and valley mist (a sea of cloud), and a cap of cloud and a plume of spindrift on its summit.
    /// Particles are emitted by hand where they are wanted (no box full of unused ones); the deck is one mesh.
    /// </summary>
    public class WeatherFX
    {
        readonly Weather weather;
        readonly Rider rider;
        readonly Camera cam;
        readonly Transform root;
        readonly Texture2D cloudTex;
        ParticleSystem lens, drift, dust, farSnow, near;
        Material lensMat, dustMat, deckMat, farCloudMat;
        Material spray;   // the game's soft particle (Atmosphere's snowfall): DayNight tints it for the time of day
        Transform deck; Mesh deckMesh; Vector2 deckAt = new Vector2(1e9f, 0f); float deckY = float.NaN;
        readonly System.Func<StartPage> getStartPage;   // (handed over by the game: never searched for)
        StartPage startPage; FarMountain far; Material farMat;
        float farHaze0 = -1f, farMist0, farMistH0;
        ParticleSystem farCloud;
        float lensCarry, driftCarry, dustCarry, nearCarry;

        public bool OnStartPage => startPage != null && startPage.Showing;
        /// <summary>For the test tools' log.</summary>
        public string State => $"deck {(deck != null ? deck.gameObject.activeSelf + " at " + deck.position : "none")} mat {(deckMat != null)}; far cloud {(farCloud != null ? farCloud.particleCount : -1)}; far mat {(farMat != null)}";

        /// <summary>0 below e0, 1 above e1, smooth between (Mathf.SmoothStep is an interpolation, not this).</summary>
        static float Edge(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        public WeatherFX(Weather w, Rider r, Camera c, Texture2D cloud, System.Func<StartPage> startPageOf)
        {
            weather = w; rider = r; cam = c; root = w.transform; cloudTex = cloud; getStartPage = startPageOf;
            var atmo = Object.FindFirstObjectByType<Atmosphere>();
            var fall = atmo != null ? atmo.transform.Find("Snowfall") : null;
            spray = fall != null ? fall.GetComponent<ParticleSystemRenderer>().sharedMaterial : null;
            snowfall = fall != null ? fall.GetComponent<ParticleSystem>() : null;
            var unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (unlit == null || cam == null) return;   // (never stop the game for weather)
            lensMat = Particle(unlit, Droplet(), false);
            dustMat = Particle(unlit, Sparkle(), true);
            lens = System("LensFlakes", lensMat, 120, ParticleSystemSimulationSpace.Local);
            lens.transform.SetParent(cam.transform, false);
            var lm = lens.main; lm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 3.4f);
            Fade(lens, new[] { 0f, 0.05f, 0.6f, 1f }, new[] { 0f, 0.75f, 0.55f, 0f });
            var ls = lens.sizeOverLifetime; ls.enabled = true; ls.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.7f));
            drift = System("GroundDrift", spray != null ? spray : lensMat, 900, ParticleSystemSimulationSpace.World);
            var dm = drift.main; dm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            Fade(drift, new[] { 0f, 0.2f, 0.7f, 1f }, new[] { 0f, 0.5f, 0.4f, 0f });
            var dr = drift.GetComponent<ParticleSystemRenderer>();
            dr.renderMode = ParticleSystemRenderMode.Stretch; dr.velocityScale = 0.09f; dr.lengthScale = 1.2f; dr.cameraVelocityScale = 0f;
            dust = System("DiamondDust", dustMat, 300, ParticleSystemSimulationSpace.World);
            var um = dust.main; um.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.6f);
            var us = dust.sizeOverLifetime; us.enabled = true;   // a twinkle: flash up and gone
            us.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(0.5f, 0.2f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0f)));
            // the snow close to the lens: what makes falling snow read on a phone (the far box is all specks)
            near = System("NearSnow", spray != null ? spray : lensMat, 1500, ParticleSystemSimulationSpace.World);
            var nm = near.main; nm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            Fade(near, new[] { 0f, 0.12f, 0.8f, 1f }, new[] { 0f, 0.95f, 0.9f, 0f });
            var nr = near.GetComponent<ParticleSystemRenderer>();
            nr.renderMode = ParticleSystemRenderMode.Stretch; nr.lengthScale = 1f; nr.velocityScale = 0f; nr.cameraVelocityScale = 0.012f;
            BuildDeck();
        }

        public void Destroy()
        {
            foreach (var o in new Object[] { lensMat, dustMat, deckMat, farCloudMat, deckMesh })
                if (o != null) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
            if (lens != null) { if (Application.isPlaying) Object.Destroy(lens.gameObject); else Object.DestroyImmediate(lens.gameObject); }
            RestoreFar();
        }

        // ---------------------------------------------------------------- per frame

        public void Step(Weather.Look l, float dt, float gust)
        {
            if (cam == null || lens == null) return;
            if (startPage == null && getStartPage != null) startPage = getStartPage();
            bool page = OnStartPage;
            // the cloud drifts with the wind (racing in the start page's time-lapse)
            weather.Drift(dt > 0f ? dt : Time.unscaledDeltaTime, (0.004f + 0.02f * l.wind) * (TimeOfDay.Shown >= 0f ? 14f : 1f));

            Vector3 cp = cam.transform.position, f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            Vector3 side = new Vector3(f.z, 0f, -f.x);
            Vector3 wind3 = new Vector3(TreeWind.Dir.x, 0f, TreeWind.Dir.y);
            float sp01 = rider != null && rider.Running ? Mathf.Clamp01(rider.Speed / 30f) : 0f;

            if (!page && dt > 0f)
            {
                // flakes on the lens: more at speed, and more facing into the wind
                float into = Mathf.Clamp01(0.5f - 0.5f * Vector3.Dot(cam.transform.forward, wind3));
                lensCarry += dt * l.lens * (2f + 14f * sp01 + 8f * into * l.wind) * (l.snow > 0.05f || l.fog > 0.004f ? 1f : 0.3f);
                int n = (int)lensCarry; lensCarry -= n;
                if (n > 0) LensFlakes(n, l);
                // snow close by: flakes drifting past the lens, driven streaks in a storm, rushing past at speed
                float speedK = rider != null && rider.Running ? rider.Speed : 0f;
                nearCarry += dt * l.snow * Mathf.Lerp(700f, 380f, l.flake) * (1f + speedK / 9f);
                n = Mathf.Min((int)nearCarry, 80); nearCarry -= (int)nearCarry;
                if (n > 0) NearSnow(n, l, gust, speedK);
                // ground drift: snow snaking down the slope ahead, harder in a gust
                driftCarry += dt * l.drift * (200f + 320f * gust);
                n = Mathf.Min((int)driftCarry, 40); driftCarry -= (int)driftCarry;
                if (n > 0) Drift(n, cp, f, side, wind3, gust);
                // diamond dust: tiny glitter in the air close by
                dustCarry += dt * l.dust * 110f;
                n = Mathf.Min((int)dustCarry, 20); dustCarry -= (int)dustCarry;
                if (n > 0) Dust(n, cp, f, side);
            }
            Deck(l, cp, page);
            FarWeather(l, dt, page);
            if (!warmed && !page) { warmed = true; Warm(); }
            else if (warmFrames > 0 && --warmFrames == 0 && warmQuad != null) warmQuad.gameObject.SetActive(false);
        }

        void LensFlakes(int n, Weather.Look l)
        {
            const float d = 0.3f;   // in front of the lens: past the near plane in both views (0.2 chase, 0.06 first person)
            float hh = d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), hw = hh * cam.aspect;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = new Vector3(Random.Range(-hw, hw) * 0.95f, Random.Range(-hh, hh) * 0.95f, d);
                // fog leaves fine droplets; snow, flakes of all sizes, a few big ones
                float big = l.snow > 0.3f && Random.value < 0.15f ? 1.7f : 1f;
                ep.startSize = hh * 2f * Random.Range(0.008f, 0.02f) * big * Mathf.Lerp(0.75f, 1.1f, l.flake);
                ep.velocity = new Vector3(0f, -Random.Range(0f, 0.004f), 0f);   // some begin to run
                ep.rotation = Random.Range(0f, 360f);
                lens.Emit(ep, 1);
            }
        }

        Vector4 nearLook = new Vector4(-1f, 0f, 0f, 0f);
        void NearSnow(int n, Weather.Look l, float gust, float speed)
        {
            var want = new Vector4(l.streak, 0f, 0f, 0f);
            if ((want - nearLook).sqrMagnitude > 1e-4f)
            {
                nearLook = want;
                near.GetComponent<ParticleSystemRenderer>().velocityScale = 0.022f * l.streak;
            }
            var t = cam.transform;
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float windSpeed = (1f + 13f * l.wind * l.wind) * (0.7f + 0.6f * gust);
            Vector3 drift = new Vector3(TreeWind.Dir.x, 0f, TreeWind.Dir.y) * windSpeed + Vector3.down * l.fall;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                float u = Random.value, d = 1.2f + (12f + speed * 0.35f) * u * u;
                Vector3 p = t.position + t.forward * d + t.right * (Random.Range(-1.15f, 1.15f) * d * tan * cam.aspect) + t.up * (Random.Range(-1.1f, 1.3f) * d * tan);
                // (blown back upwind a little, so it has crossed the view by the time it fades)
                ep.position = p - drift * 0.35f;
                ep.velocity = drift + Random.insideUnitSphere * (0.4f + 0.8f * l.wind);
                ep.startSize = Random.Range(0.012f, 0.03f) * Mathf.Lerp(1f, 2f, l.flake);
                near.Emit(ep, 1);
            }
        }

        void Drift(int n, Vector3 cp, Vector3 f, Vector3 side, Vector3 wind3, float gust)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                float u = Random.value, dist = 2f + 26f * u * u;
                Vector3 p = cp + f * dist + side * (Random.Range(-1f, 1f) * (2f + dist * 0.7f));
                float g = Slope.DrawnHeight(p.x, p.z);
                Vector3 nrm = Slope.Normal(p.x, p.z);
                p.y = g + Random.Range(0.03f, 0.45f) * Random.value;
                // along the snow, the way the wind blows
                Vector3 along = Vector3.ProjectOnPlane(wind3 + side * Random.Range(-0.15f, 0.15f), nrm).normalized;
                ep.position = p;
                ep.velocity = along * Random.Range(5f, 10f) * (0.8f + 0.6f * gust) + nrm * Random.Range(0f, 0.6f);
                ep.startSize = Random.Range(0.15f, 0.5f);
                drift.Emit(ep, 1);
            }
        }

        void Dust(int n, Vector3 cp, Vector3 f, Vector3 side)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                float dist = Random.Range(1.5f, 18f);
                ep.position = cp + f * dist + side * Random.Range(-dist * 0.6f, dist * 0.6f) + Vector3.up * Random.Range(-2.5f, 3f);
                ep.velocity = new Vector3(Random.Range(-0.2f, 0.4f), Random.Range(-0.25f, 0.05f), Random.Range(-0.2f, 0.2f));
                ep.startSize = Random.Range(0.012f, 0.035f) * (0.6f + dist * 0.05f);
                dust.Emit(ep, 1);
            }
        }

        /// <summary>The weather's colours this frame (Weather, after DayNight): the deck, the far cloud, the lens.</summary>
        public void Light(Color cloudLit, Color fog, Weather.Look l)
        {
            if (deckMat != null)
            {
                // (gamma: SetColor turns them linear)
                deckMat.SetColor("_Color", new Color(cloudLit.r, cloudLit.g, cloudLit.b, 1f).gamma * new Color(1f, 1f, 1f, Mathf.Clamp01(l.deck)));
                Color sh = Color.Lerp(fog, cloudLit, 0.35f) * 0.8f; sh.a = 1f;
                deckMat.SetColor("_Shade", sh.gamma);
            }
            if (farCloudMat != null)
            {
                farCloudMat.SetColor("_Color", new Color(cloudLit.r, cloudLit.g, cloudLit.b, 1f).gamma);
                Color sh = Color.Lerp(fog, cloudLit, 0.3f) * 0.76f; sh.a = 1f;
                farCloudMat.SetColor("_Shade", sh.gamma);
            }
            // the droplets take the light's tint (the game's soft particle carries it: dim and blue by moonlight)
            Color t = spray != null && spray.HasProperty("_BaseColor") ? spray.GetColor("_BaseColor") : Color.white;
            if (lensMat != null) lensMat.SetColor("_BaseColor", new Color(t.r, t.g, t.b, 1f));
            var sun = RenderSettings.sun;
            float shine = sun != null ? Mathf.Clamp01(sun.intensity / 1.2f) : 0.3f;
            if (dustMat != null) dustMat.SetColor("_BaseColor", new Color(1.8f * shine + 0.25f, 1.8f * shine + 0.25f, 1.9f * shine + 0.3f, 1f));
            Vector2 s = weather.CloudScroll;
            if (deckMat != null) deckMat.SetVector("_Scroll", new Vector4(s.x * 0.6f, s.y * 0.6f, -s.y * 1.4f, s.x * 1.4f));
            if (farCloudMat != null) farCloudMat.SetVector("_Scroll", new Vector4(s.x * 3f, s.y * 3f, -s.y * 5f, s.x * 5f));
        }

        // ---------------------------------------------------------------- the cloud sea's deck

        const int DeckN = 40; const float DeckStep = 75f, DeckSnap = 150f;

        void BuildDeck()
        {
            var sh = Resources.Load<Shader>("WeatherCloud");
            if (sh == null || !sh.isSupported) return;
            deckMat = new Material(sh) { name = "CloudDeck" };
            deckMat.SetTexture("_CloudTex", cloudTex);
            deckMat.SetFloat("_WorldUV", 1f); deckMat.SetFloat("_UVScale", 0.0011f); deckMat.SetFloat("_Haze", 0.00034f);
            farCloudMat = new Material(sh) { name = "FarCloudWeather" };
            farCloudMat.SetTexture("_CloudTex", cloudTex);
            farCloudMat.SetFloat("_WorldUV", 0f); farCloudMat.SetFloat("_Haze", 0.00002f);
            deckMesh = new Mesh { name = "CloudDeck" };
            var v = new Vector3[DeckN * DeckN]; var c = new Color[v.Length]; var t = new int[(DeckN - 1) * (DeckN - 1) * 6];
            float half = (DeckN - 1) * DeckStep * 0.5f;
            for (int j = 0; j < DeckN; j++)
                for (int i = 0; i < DeckN; i++) { v[j * DeckN + i] = new Vector3(i * DeckStep - half, 0f, j * DeckStep - half); c[j * DeckN + i] = Color.clear; }
            int q = 0;
            for (int j = 0; j < DeckN - 1; j++)
                for (int i = 0; i < DeckN - 1; i++)
                {
                    int a = j * DeckN + i, b = a + 1, d = a + DeckN, e = d + 1;
                    t[q++] = a; t[q++] = d; t[q++] = b; t[q++] = b; t[q++] = d; t[q++] = e;
                }
            deckMesh.vertices = v; deckMesh.colors = c; deckMesh.triangles = t;
            deckMesh.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 50f, half * 2f));
            var go = new GameObject("CloudDeck");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = deckMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = deckMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            deck = go.transform;
            go.SetActive(false);
            // a small clear quad in the deck's material, drawn once to get its GPU state made (Warm)
            var wq = new GameObject("CloudWarm");
            wq.transform.SetParent(root, false);
            var qm = new Mesh { name = "CloudWarmQuad" };
            qm.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            qm.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            qm.colors = new[] { Color.clear, Color.clear, Color.clear, Color.clear };
            qm.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            wq.AddComponent<MeshFilter>().sharedMesh = qm;
            var wr = wq.AddComponent<MeshRenderer>();
            wr.sharedMaterial = deckMat; wr.shadowCastingMode = ShadowCastingMode.Off; wr.receiveShadows = false;
            wq.SetActive(false);
            warmQuad = wq.transform;
        }

        /// <summary>Rows of the deck's grid worked out per frame when it moves (40 rows: done in 14 frames, about half
        /// a millisecond each on the editor's CPU, where the whole grid at once took over 3).</summary>
        const int DeckRowsPerFrame = 3;
        Color[] deckCols, deckNext;
        int deckRow = -1; Vector2 deckNextAt; float deckNextY;

        void Deck(Weather.Look l, Vector3 cp, bool page)
        {
            if (deck == null) return;
            float above = cp.y - weather.SeaTop;
            float fade = Edge(50f, 180f, above);
            if (deckMat != null) deckMat.SetFloat("_Fade", fade);
            bool on = l.deck * fade > 0.01f && !page;
            if (deck.gameObject.activeSelf != on) deck.gameObject.SetActive(on);
            if (!on) { deckRow = -1; return; }
            // a grid round the camera, moved in steps; where the mountain stands above the sea's top it thins to nothing.
            // The new grid is worked out a few rows a frame and put in place whole once it is done
            var at = new Vector2(Mathf.Round(cp.x / DeckSnap) * DeckSnap, Mathf.Round(cp.z / DeckSnap) * DeckSnap);
            float y = weather.SeaTop;
            if (deckRow < 0)
            {
                if (at == deckAt && Mathf.Abs(y - deckY) < 0.5f) return;
                deckRow = 0; deckNextAt = at; deckNextY = y;
            }
            long t0 = PerfMarks.Now;
            if (deckNext == null) { deckNext = new Color[DeckN * DeckN]; deckCols = new Color[DeckN * DeckN]; }
            float half = (DeckN - 1) * DeckStep * 0.5f;
            int end = Mathf.Min(DeckN, deckRow + DeckRowsPerFrame);
            for (int j = deckRow; j < end; j++)
                for (int i = 0; i < DeckN; i++)
                {
                    float x = deckNextAt.x + i * DeckStep - half, z = deckNextAt.y + j * DeckStep - half;
                    float below = deckNextY - Slope.Height(x, z);
                    float dx = i * DeckStep - half, dz = j * DeckStep - half;
                    float r = Mathf.Sqrt(dx * dx + dz * dz) / half;
                    deckNext[j * DeckN + i] = new Color(1f, 1f, 1f, Edge(0f, 70f, below) * (1f - Edge(0.75f, 1f, r)));
                }
            deckRow = end;
            if (deckRow >= DeckN)
            {
                (deckCols, deckNext) = (deckNext, deckCols);
                deckMesh.colors = deckCols;
                deck.position = new Vector3(deckNextAt.x, deckNextY, deckNextAt.y);
                deckAt = deckNextAt; deckY = deckNextY; deckRow = -1;
            }
            PerfMarks.Mark("weather deck rows", t0);
        }

        // ---------------------------------------------------------------- shaders ready before they are needed

        /// <summary>
        /// The first time a weather's particles or its deck are drawn the phone has to make their GPU state, a hitch
        /// in the middle of a run (the first snow, the first cloud sea). So once, the first frame the game's own world
        /// is drawn (the camp, or the run in device tests), each is drawn once, invisible: a particle with no alpha in
        /// front of the lens in every system, and a clear quad in the deck's material.
        /// </summary>
        void Warm()
        {
            long t0 = PerfMarks.Now;
            var t = cam.transform;
            var ep = new ParticleSystem.EmitParams
            {
                position = t.position + t.forward * 3f, velocity = Vector3.zero, startSize = 0.05f, startLifetime = 0.2f,
                startColor = new Color32(255, 255, 255, 0), applyShapeToPosition = false,
            };
            foreach (var ps in new[] { near, drift, dust, snowfall }) if (ps != null) ps.Emit(ep, 1);
            if (lens != null) { ep.position = new Vector3(0f, 0f, 0.3f); lens.Emit(ep, 1); }
            if (warmQuad != null)
            {
                warmQuad.SetPositionAndRotation(t.position + t.forward * 5f, Quaternion.LookRotation(t.forward));
                warmQuad.gameObject.SetActive(true);
            }
            warmFrames = 2;
            PerfMarks.Mark("weather warm-up", t0);
        }
        bool warmed; int warmFrames;
        Transform warmQuad;
        ParticleSystem snowfall;

        // ---------------------------------------------------------------- the start page

        void FarWeather(Weather.Look l, float dt, bool page)
        {
            if (!page)
            {
                if (farSnow != null && farSnow.gameObject.activeSelf) { farSnow.Clear(); farSnow.gameObject.SetActive(false); }
                if (farCloud != null && farCloud.particleCount > 0) farCloud.Clear();
                return;
            }
            if (far == null) far = startPage.Far;
            if (far != null && farMat == null)
                foreach (var mr in far.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_Mist")) { farMat = mr.sharedMaterial; break; }
            if (farMat != null)
            {
                if (farHaze0 < 0f) { farHaze0 = farMat.GetFloat("_Haze"); farMist0 = farMat.GetFloat("_Mist"); farMistH0 = farMat.GetFloat("_MistH"); }
                // the far country gone into the weather's haze; the valleys' mist thickening; a sea of cloud filling them
                float overcast = Mathf.Max(l.cloud, l.veil);
                float thick = Mathf.Clamp01(l.fog / 0.0095f);
                farMat.SetFloat("_Haze", farHaze0 * (1f + 1.5f * l.veil + 0.8f * l.cloud + 7f * thick * thick));
                float mist = farMist0 * (1f + 2.5f * overcast), mistH = farMistH0 * (1f + 0.25f * overcast);
                if (l.sea > 0.01f) { mist = Mathf.Lerp(mist, 0.5f, l.sea); mistH = Mathf.Lerp(mistH, 88f, l.sea); }
                farMat.SetFloat("_Mist", mist); farMat.SetFloat("_MistH", mistH);
            }
            FarSnow(l);
            FarCloud(l, dt);
        }

        void RestoreFar()
        {
            if (farMat != null && farHaze0 >= 0f) { farMat.SetFloat("_Haze", farHaze0); farMat.SetFloat("_Mist", farMist0); farMat.SetFloat("_MistH", farMistH0); }
        }

        Vector4 farSnowLook = new Vector4(-1f, 0f, 0f, 0f);
        void FarSnow(Weather.Look l)
        {
            if (spray == null) return;
            if (farSnow == null)
            {
                farSnow = System("FarSnow", spray, 7000, ParticleSystemSimulationSpace.World);
                farSnow.gameObject.layer = FarMountain.Layer;
                var m = farSnow.main; m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7f); m.loop = true;
                var sh = farSnow.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(150f, 100f, 150f);
                var vel = farSnow.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                Fade(farSnow, new[] { 0f, 0.12f, 0.85f, 1f }, new[] { 0f, 0.85f, 0.85f, 0f });
                farSnow.Play();
            }
            bool on = l.snow > 0.02f;
            if (farSnow.gameObject.activeSelf != on) { farSnow.gameObject.SetActive(on); if (on) farSnow.Play(); }
            if (!on) return;
            Vector3 f = cam.transform.forward;
            farSnow.transform.SetPositionAndRotation(cam.transform.position + f * 125f, Quaternion.LookRotation(f));
            var em = farSnow.emission; em.rateOverTime = l.snow * Mathf.Lerp(1100f, 650f, l.flake);
            var want = new Vector4(l.flake, l.fall, l.streak, l.wind);
            if ((want - farSnowLook).sqrMagnitude < 1e-4f) return;
            farSnowLook = want;
            var mm = farSnow.main; mm.startSize = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.18f, 0.45f, l.flake), Mathf.Lerp(0.4f, 1.0f, l.flake));
            var v = farSnow.velocityOverLifetime;
            float w = 1f + 14f * l.wind * l.wind;
            v.x = new ParticleSystem.MinMaxCurve(TreeWind.Dir.x * w * 0.6f, TreeWind.Dir.x * w * 1.4f);
            v.z = new ParticleSystem.MinMaxCurve(TreeWind.Dir.y * w * 0.6f, TreeWind.Dir.y * w * 1.4f);
            v.y = new ParticleSystem.MinMaxCurve(-l.fall * 1.6f, -l.fall * 0.8f);
            var pr = farSnow.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = l.streak > 0.4f ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            pr.velocityScale = 0.05f * l.streak; pr.lengthScale = 1f;
        }

        ParticleSystem.Particle[] farParts = new ParticleSystem.Particle[6];

        /// <summary>
        /// The cap on the start page's summit and the plume off it: six big soft billboards (a particle system, so the
        /// helicopter's camera draws them as it does the far snow), placed by hand each frame.
        /// </summary>
        void FarCloud(Weather.Look l, float dt)
        {
            if (farCloudMat == null) return;
            if (farCloud == null)
            {
                farCloud = System("FarCloud", farCloudMat, farParts.Length, ParticleSystemSimulationSpace.World);
                farCloud.gameObject.layer = FarMountain.Layer;
                var m = farCloud.main; m.startSize3D = true; m.startRotation3D = false; m.startLifetime = 100f;
                var r = farCloud.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard; r.maxParticleSize = 4f; r.minParticleSize = 0f;
            }
            float clear = 1f - l.cloud;
            float cap = l.cap * clear, plume = l.plume * clear;
            int n = 0;
            if (cap > 0.01f || plume > 0.01f)
            {
                Vector3 top = FarMountain.SummitPoint;
                Vector3 wind3 = new Vector3(TreeWind.Dir.x, 0f, TreeWind.Dir.y);
                // a smooth cap sitting on the summit
                if (cap > 0.01f) Part(ref n, top + Vector3.up * 80f, new Vector2(1900f, 560f), cap, 0f);
                // the plume: puffs streaming off the summit downwind, spreading and thinning
                float t = Time.unscaledTime;
                for (int i = 0; i < 5 && plume > 0.01f; i++)
                {
                    float s = (i + 0.5f) / 5f;
                    float sway = Mathf.Sin(t * 0.4f + i * 1.7f) * 60f * s;
                    Vector3 at = top + wind3 * (300f + 1500f * s) + Vector3.up * (40f - 260f * s) + Vector3.Cross(Vector3.up, wind3) * sway;
                    Part(ref n, at, new Vector2(700f + 1300f * s, 300f + 380f * s), plume * (1f - s * 0.75f) * 0.75f, 37f * i + 11f);
                }
            }
            farCloud.SetParticles(farParts, n);
        }

        void Part(ref int n, Vector3 at, Vector2 size, float alpha, float rot)
        {
            var p = new ParticleSystem.Particle
            {
                position = at, startSize3D = new Vector3(size.x, size.y, 1f), rotation = rot,
                startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)), startLifetime = 100f, remainingLifetime = 100f,
            };
            farParts[n++] = p;
        }

        // ---------------------------------------------------------------- making the parts

        ParticleSystem System(string name, Material m, int max, ParticleSystemSimulationSpace space)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = space; main.maxParticles = max; main.playOnAwake = false; main.startSpeed = 0f;
            main.startColor = Color.white;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Fade(ParticleSystem ps, float[] at, float[] alpha)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            var ak = new GradientAlphaKey[at.Length];
            for (int i = 0; i < at.Length; i++) ak[i] = new GradientAlphaKey(alpha[i], at[i]);
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, ak);
            c.color = g;
        }

        static Material Particle(Shader sh, Texture2D tex, bool additive)
        {
            var m = new Material(sh);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>A flake or droplet on the lens, out of focus: a soft disc with a brighter rim where it bends the light.</summary>
        static Texture2D Droplet()
        {
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "LensDrop" };
            float c = (n - 1) * 0.5f;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    // a little irregular, as a half-melted flake is
                    float ang = Mathf.Atan2(dy, dx), r = Mathf.Sqrt(dx * dx + dy * dy) * (1f + 0.08f * Mathf.Sin(ang * 3f) + 0.05f * Mathf.Sin(ang * 5f + 1f));
                    float body = Edge(1f, 0.82f, r);
                    float rim = Edge(0.55f, 0.92f, r) * body;
                    float a = body * (0.45f + 0.3f * rim);
                    float v = 0.86f + 0.14f * rim;
                    px[y * n + x] = new Color(v, v, v, a);
                }
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        /// <summary>A four-point glint with a soft core (as SnowFX's).</summary>
        static Texture2D Sparkle()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DustGlint" };
            float c = (n - 1) * 0.5f;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Abs(x - c) / c, dy = Mathf.Abs(y - c) / c, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(1f - d * 2.2f);
                    float rays = Mathf.Clamp01(1f - dx * 1.05f) * Mathf.Clamp01(1f - dy * 9f) + Mathf.Clamp01(1f - dy * 1.05f) * Mathf.Clamp01(1f - dx * 9f);
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(core * core * 1.5f + rays * rays));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }
    }
}
