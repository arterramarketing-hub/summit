using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The start page's other view (StartPage.Home: JUMP): a still from a ski film come to life. Bright white snow and a
    /// pale sky, a kicker cut into the slope, and your rider (a copy of them as they are, in what they wear) coming
    /// down to it, popping off the lip and putting down a trick (a backside 360 with an indy, a backflip with a mute,
    /// a frontside 540 with a method, in turn), slowing as they hang at the top of it, then landing in a burst of snow
    /// and riding away. The camera is a film operator's: low and well off to the side on a long lens, panning with the
    /// rider so they stand out against the sky, a little unsteady in the hand.
    ///
    /// Like the closet's studio (ClosetStage) it is far above everything else, on a layer of its own, and the game's
    /// camera draws only that layer while the page shows. The snow is unlit (JumpSnow.shader: shaded against the
    /// scene's own sun, never fogged), so it is the same clean white at any time of day; StartPage holds the day
    /// while it shows, so the rider is lit by daylight too. The copy is posed by the game's own clips (HeroRig), its
    /// feet held to the board and a grabbing hand to the board's edge, exactly as on a run.
    /// </summary>
    public class StartJump : MonoBehaviour
    {
        /// <summary>The layer the scene is on (nothing else uses it).</summary>
        public const int Layer = 28;
        static readonly Vector3 At = new Vector3(0f, 4500f, 0f);

        // ---- the hill (stage space: the rider goes along +x, the camera stands at -z)
        const float LipX = -1f, Kicker = 2.3f, KickRun = 6.5f;   // the lip, its height above the slope, the ramp's length
        const float X0 = -140f, X1 = 170f, Step = 0.25f;
        static readonly float[] profile = BuildProfile();
        // ---- the ride
        const float StartX = -36f, Approach = 10.5f, TakeOff = 12.8f, OutSpeed = 11.5f, OutTime = 2.6f, Gap = 0.5f;
        static readonly Vector3 G = new Vector3(0f, -9.81f, 0f);
        // ---- the camera
        const float Fov = 28f;
        static readonly Vector3 CamAt = new Vector3(6f, 0f, -26f);

        // the tricks, in turn: degrees of spin (+ backside), whole flips (+ back), and the grab (RiderBodyHero's order:
        // Indy, Melon, Mute, Method, Stalefish, TailGrab)
        static readonly (float spin, float flip, int grab)[] Tricks = { (360f, 0f, 0), (0f, 1f, 2), (-540f, 0f, 3) };

        public bool Showing { get; private set; }
        /// <summary>Seconds into the loop (the test tools set it to look at any moment of it).</summary>
        public float Clock;
        /// <summary>Which trick the loop is on (0.. Tricks).</summary>
        public int Trick;

        Transform stage, holder, board, model, skyT;
        Material snowMat, skyMat;
        HeroRig rig;
        Camera cam;
        ParticleSystem trail, burst;
        Material spray;
        Vector3 ridePos; Quaternion baseRot, rootRot;
        int idRide, idToe, idHeel, idPop, idAir, idLand, idFlip, idWindL, idWindR, idSpinL, idSpinR;
        readonly int[] idGrab = new int[6];
        readonly HeroRig.Hold[] grabHold = new HeroRig.Hold[6];
        Vector3 lipPos, v0, landPos; float airTime, approachTime;
        Vector3 aim, aimVel; bool aimSet;
        float comp, compV, lift;
        int lastPhase = -1;

        public void Setup(Camera c, Material sprayMat) { cam = c; spray = sprayMat; }

        // ---------------------------------------------------------------- showing

        /// <summary>Build the scene (once) and copy the rider into it. False if there is no rider with clips to copy.</summary>
        public bool Show(RiderBody body)
        {
            if (cam == null || body == null || body.HeroModel == null) return false;
            if (stage == null && !BuildScene()) return false;   // (its shaders missing: the page shows the flight)
            stage.gameObject.SetActive(true);
            if (!CopyRider(body)) { stage.gameObject.SetActive(false); return false; }
            Showing = true;
            Clock = 0f; Trick = 0; aimSet = false; lastPhase = -1;
            Frame(0f, 0f);
            return true;
        }

        public void Hide()
        {
            Showing = false;
            if (holder != null) Kill(holder.gameObject);
            holder = null; board = null; model = null; rig = null;
            if (stage != null) stage.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- the loop

        /// <summary>
        /// One frame: the rider along the loop, posed, and the camera on them. `leave` 0..1: the page is going (the
        /// lens pushes in on the rider).
        /// </summary>
        public void Frame(float dt, float leave)
        {
            if (!Showing || rig == null) return;
            OrientToSun();
            float loop = approachTime + airTime + OutTime + Gap;
            // the hang at the top of the trick is slow motion: the clock runs slower through the middle of the air
            float air = (Clock - approachTime) / airTime;
            float rate = air > 0f && air < 1f ? Mathf.Lerp(1f, 0.42f, Sm(0.08f, 0.34f, air) * (1f - Sm(0.62f, 0.9f, air))) : 1f;
            Clock += dt * rate;
            if (Clock >= loop) { Clock -= loop; Trick = (Trick + 1) % Tricks.Length; aimSet = false; }
            Pose(Clock, dt);
            Shoot(dt, leave);
        }

        /// <summary>
        /// Test tools: play trick `trick` from the top of the loop to `airAt` through its air (0 the lip, 1 the landing;
        /// below 0 seconds before the lip, past 1 seconds after the landing), frame by frame as the game would.
        /// </summary>
        public void Seek(int trick, float airAt)
        {
            Trick = trick % Tricks.Length; Clock = 0f; aimSet = false; lastPhase = -1;
            float t = airAt < 0f ? approachTime + airAt : airAt <= 1f ? approachTime + airAt * airTime : approachTime + airTime + (airAt - 1f);
            const float dt = 1f / 60f;
            Frame(0f, 0f);
            for (int i = 0; i < 2000 && Clock + 1e-4f < t; i++)
            {
                float step = dt;
                Frame(step, 0f);
                trail.Simulate(step, true, false, false); burst.Simulate(step, true, false, false);
                if (Trick != trick % Tricks.Length) break;
            }
        }
        public float AirTime => airTime;

        /// <summary>Where the rider is and how they're turned at `t` into the loop, and their pose.</summary>
        void Pose(float t, float dt)
        {
            var tr = Tricks[Trick];
            int phase; Vector3 p; Quaternion r; float f = 0f, spin = 0f, flip = 0f, grab = 0f, speed;
            if (t < approachTime)
            {
                // down the slope to the kicker and up it: on the snow, the board along the surface
                phase = 0;
                float x = StartX + Approach * t;
                p = Surface(x); speed = Approach;
                r = OnSnow(x, 0f);
            }
            else if (t < approachTime + airTime)
            {
                phase = 1;
                float ta = t - approachTime; f = ta / airTime;
                p = lipPos + v0 * ta + 0.5f * G * ta * ta;
                speed = (v0 + G * ta).magnitude;
                spin = tr.spin * Ease(Sm(0.05f, 0.86f, f));
                flip = tr.flip * 360f * Ease(Sm(0.1f, 0.84f, f));
                grab = Sm(0.16f, 0.3f, f) * (1f - Sm(0.66f, 0.8f, f));
                // the board's pitch goes from the lip's to the landing's as the rider flies
                Vector3 along = Vector3.Slerp(v0.normalized, Tangent(landPos.x), Sm(0.15f, 0.92f, f));
                // a flip turns about the slope's width (+z here: the head goes back), a spin about the world's up
                r = Quaternion.AngleAxis(spin, Vector3.up) * Quaternion.AngleAxis(flip, Vector3.forward) * Quaternion.LookRotation(along, Vector3.up);
            }
            else
            {
                // landed (riding switch after a half turn) and away down the hill
                phase = 2;
                float to = t - approachTime - airTime;
                float x = landPos.x + OutSpeed * to - 1.2f * to * to * 0.5f;
                p = Surface(x); speed = OutSpeed - 1.2f * to;
                float rest = Mathf.Repeat(tr.spin, 360f);
                r = Quaternion.AngleAxis(rest, Normal(x, 0f)) * OnSnow(x, 0f);
            }
            holder.localPosition = p;
            holder.localRotation = r;

            // ---- the body, from the clips
            var h = rig;
            h.Begin();
            model.localPosition = ridePos; model.localRotation = baseRot;
            var d = new HeroRig.Drive
            {
                board = board, rootInBoard = new Vector3(0f, 0.05f, 0f), rootRotInBoard = rootRot,
                up = board.up, toe = board.right, nose = board.forward, feet = true,
            };
            float blend = 9f, liftWant = 0f;
            if (phase != lastPhase)
            {
                if (phase == 1) { h.Restart(idPop); Puff(lipPos, 14, 2.5f); }
                if (phase == 2) { h.Restart(idLand); compV = -2.4f; Puff(p, 70, 5.5f); }
                if (phase == 0) { h.ResetSprings(); }
            }
            float ft = phase == 0 ? t : phase == 1 ? t - approachTime : t - approachTime - airTime;
            if (phase == 0)
            {
                // a few small carves on the way in, then square to the lip and down into the legs for the pop
                float toLip = approachTime - t;
                float carve = Mathf.Sin(t * 2.1f) * 0.35f * Sm(0.6f, 1.6f, toLip);
                h.Want(idRide, 1f - Mathf.Abs(carve));
                h.Want(carve > 0f ? idToe : idHeel, Mathf.Abs(carve));
                d.lean = carve * 10f;
                d.drop = 0.16f * Sm(0.45f, 0.1f, toLip) - 0.0f;
                d.look = Vector3.right * 4f + Vector3.down;
                d.lookWeight = 0.5f;
            }
            else if (phase == 1)
            {
                blend = 11f;
                bool flipping = Mathf.Abs(tr.flip) > 0f, spinning = Mathf.Abs(tr.spin) > 0f;
                float fl = flipping ? Sm(0.04f, 0.18f, f) * (1f - Sm(0.85f, 0.97f, f)) : 0f;
                float wind = spinning ? 1f - Sm(0f, 0.14f, f) : 0f;
                float wrap = spinning ? Sm(0.08f, 0.2f, f) * (1f - Sm(0.85f, 0.97f, f)) : 0f;
                float restW = 1f - Mathf.Max(grab, fl);
                h.Want(idAir, Mathf.Max(0.05f, restW * (1f - Mathf.Max(wind, wrap))));
                h.Want(tr.spin < 0f ? idWindL : idWindR, wind * restW);
                h.Want(tr.spin < 0f ? idSpinL : idSpinR, wrap * restW);
                int gi = tr.grab;
                if (grab > 0f && idGrab[gi] >= 0)
                {
                    h.Want(idGrab[gi], grab * 3f);
                    if (RiderBody.GrabHand(gi) == 0) { d.handL = grab; d.holdL = grabHold[gi]; } else { d.handR = grab; d.holdR = grabHold[gi]; }
                }
                if (fl > 0f) h.Want(idFlip, fl * 1.5f);
                liftWant = 0.11f * Mathf.Max(grab, fl);
                if (ft < h.Length(idPop)) h.Want(idPop, 2f * (1f - Sm(0.55f, 1f, ft / Mathf.Max(0.01f, h.Length(idPop)))));
                // spotting the landing as it comes round
                d.look = (landPos - p) + Vector3.right * 3f; d.lookWeight = 0.35f + 0.4f * Sm(0.55f, 0.85f, f);
            }
            else
            {
                h.Want(idRide, 1f);
                if (ft < h.Length(idLand)) h.Want(idLand, 2f * (1f - Sm(0.3f, 1f, ft / Mathf.Max(0.01f, h.Length(idLand)))));
                d.look = Vector3.right * 4f + Vector3.down; d.lookWeight = 0.4f;
            }
            // the legs take the landing and spring back
            compV += (-comp * 140f - compV * 16f) * dt;
            comp = Mathf.Clamp(comp + compV * dt, -0.15f, 0.6f);
            d.drop += Mathf.Max(0f, comp) * 0.18f;
            lift = dt <= 0f ? liftWant : Mathf.Lerp(lift, liftWant, 1f - Mathf.Exp(-dt * 12f));
            board.localPosition = Vector3.up * lift;
            if (dt <= 0f || phase != lastPhase && phase == 0) h.Snap();
            h.Evaluate(Mathf.Max(dt, 0f), blend);
            h.Layer(d, Mathf.Max(dt, 0f));
            lastPhase = phase;

            // ---- the snow: the trail of spray off the tail on the snow, the shadow under the rider
            var em = trail.emission;
            em.rateOverTime = phase == 1 ? 0f : 26f * Mathf.Clamp01(speed / 10f);
            trail.transform.localPosition = p - (r * Vector3.forward) * 0.5f;
            float ground = Height(p.x, p.z);
            float above = Mathf.Max(0f, p.y - ground);
            snowMat.SetVector("_Blob", new Vector4(p.x + above * 0.25f, ground, p.z + above * 0.35f, 0.85f + above * 0.22f));
            snowMat.SetFloat("_BlobA", 0.3f * Mathf.Exp(-above * 0.3f));
        }

        /// <summary>The camera: a long lens low off to the side, panning with the rider (a cut back for each new run).</summary>
        void Shoot(float dt, float leave)
        {
            Vector3 want = holder.localPosition + Vector3.up * 0.9f;
            // the operator leads the rider a touch, and keeps them a little low in the frame on the way in
            if (!aimSet) { aim = want; aimVel = Vector3.zero; aimSet = true; }
            else aim = Vector3.SmoothDamp(aim, want, ref aimVel, 0.16f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
            Vector3 pos = CamAt; pos.y = Height(CamAt.x, CamAt.z) + 1.1f;
            // breathing in the hand
            float tt = Time.unscaledTime;
            var shake = Quaternion.Euler(0.22f * Mathf.Sin(tt * 1.3f) + 0.08f * Mathf.Sin(tt * 3.7f), 0.25f * Mathf.Sin(tt * 0.9f + 1f), 0.15f * Mathf.Sin(tt * 0.7f));
            float k = Ease(Mathf.Clamp01(leave));
            Vector3 look = Vector3.Lerp(aim, holder.localPosition + Vector3.up * 0.9f, k);
            var rot = stage.rotation * Quaternion.LookRotation(look - pos, Vector3.up) * shake;
            cam.transform.SetPositionAndRotation(stage.TransformPoint(pos), rot);
            cam.fieldOfView = Mathf.Lerp(Fov, 7f, k * k);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 3000f;
            skyT.position = cam.transform.position;
            snowMat.SetVector("_Cam", pos);
        }

        /// <summary>The scene turns so the sun is behind the camera's left shoulder: the rider's front is lit.</summary>
        void OrientToSun()
        {
            var sun = RenderSettings.sun;
            Vector3 toSun = sun != null ? -sun.transform.forward : new Vector3(-0.4f, 0.7f, -0.6f);
            Vector3 hz = toSun; hz.y = 0f;
            if (hz.sqrMagnitude < 1e-4f) hz = Vector3.back;
            hz = Quaternion.Euler(0f, 38f, 0f) * hz.normalized;
            stage.rotation = Quaternion.LookRotation(-hz, Vector3.up);   // stage -z (the camera's side) toward the sun
            Vector3 local = stage.InverseTransformDirection(toSun);
            if (local.y < 0.35f) { local.y = 0.35f; }   // (a low or set sun: the snow still lit from above)
            snowMat.SetVector("_SunDir", local.normalized);
        }

        // ---------------------------------------------------------------- the hill

        /// <summary>The slope's fall line: how steep (rise per metre, + down the hill) at `x`, before the kicker.</summary>
        static float Fall(float x)
        {
            float s = Mathf.Lerp(0.30f, 0.66f, Sm(3f, 10f, x));   // the approach, then the knuckle and the landing
            return Mathf.Lerp(s, 0.2f, Sm(24f, 40f, x));          // and the run-out
        }

        static float[] BuildProfile()
        {
            int n = Mathf.CeilToInt((X1 - X0) / Step) + 1;
            var h = new float[n];
            // zero at the lip's foot, falling with the slope
            float y = 0f; h[0] = 0f;
            for (int i = 1; i < n; i++) { float x = X0 + (i - 0.5f) * Step; y -= Fall(x) * Step; h[i] = y; }
            float at = h[Mathf.RoundToInt((LipX - X0) / Step)];
            for (int i = 0; i < n; i++) h[i] -= at;
            return h;
        }

        static float Base(float x)
        {
            float u = Mathf.Clamp((x - X0) / Step, 0f, profile.Length - 1.001f);
            int i = (int)u;
            return Mathf.Lerp(profile[i], profile[i + 1], u - i);
        }

        /// <summary>The kicker's rise over the slope, at `x` along its line.</summary>
        static float Kick(float x)
        {
            if (x <= LipX - KickRun || x >= LipX + 2.8f) return 0f;
            if (x <= LipX) return Kicker * Mathf.Pow((x - (LipX - KickRun)) / KickRun, 2.4f);
            float u = (x - LipX) / 2.8f;
            return Kicker * (1f - u * u * (3f - 2f * u)) * (1f - 0.25f * u);   // the knuckle behind it, rounded off
        }

        /// <summary>The snow's height at (x, z), stage space.</summary>
        public static float Height(float x, float z)
        {
            float az = Mathf.Abs(z);
            float h = Base(x) + Kick(x) * (1f - Sm(1.8f, 3.3f, az));
            // drifts and rolls away from the kicker's line
            h += (Mathf.PerlinNoise(x * 0.045f + 3.1f, z * 0.05f + 7.7f) - 0.5f) * 1.4f * Sm(5f, 14f, az);
            h += (Mathf.PerlinNoise(x * 0.21f + 1.7f, z * 0.19f + 2.3f) - 0.5f) * 0.12f * Sm(3.5f, 6f, az);
            return h;
        }

        static Vector3 Surface(float x) => new Vector3(x, Height(x, 0f), 0f);
        static Vector3 Tangent(float x) => new Vector3(0.1f, Height(x + 0.05f, 0f) - Height(x - 0.05f, 0f), 0f).normalized;
        static Vector3 Normal(float x, float z)
        {
            const float e = 0.08f;
            float dx = Height(x + e, z) - Height(x - e, z), dz = Height(x, z + e) - Height(x, z - e);
            return new Vector3(-dx, 2f * e, -dz).normalized;
        }
        /// <summary>The board along the snow at x: nose down the line, the toe edge to the camera (-z).</summary>
        static Quaternion OnSnow(float x, float z) => Quaternion.LookRotation(Tangent(x), Normal(x, z));

        /// <summary>The flight off the lip, found once: where it lands, and when.</summary>
        void Plan()
        {
            approachTime = (LipX - StartX) / Approach;
            lipPos = Surface(LipX);
            v0 = Tangent(LipX) * TakeOff;
            float ta = 0.05f;
            for (; ta < 6f; ta += 1f / 240f)
            {
                Vector3 p = lipPos + v0 * ta + 0.5f * G * ta * ta;
                if (p.x > LipX + 3f && p.y <= Height(p.x, 0f)) break;
            }
            airTime = ta;
            landPos = lipPos + v0 * ta + 0.5f * G * ta * ta;
            landPos.y = Height(landPos.x, 0f);
            snowMat.SetVector("_Track", new Vector4(LipX, landPos.x, 0.16f, 0f));
            Debug.Log($"StartJump: air {airTime:0.00} s, lands {landPos.x - LipX:0.0} m on, {lipPos.y - landPos.y:0.0} m below the lip");
        }

        // ---------------------------------------------------------------- building

        bool BuildScene()
        {
            // both shaders ship in Resources (nothing found by name, so the phone build's shader check can see them)
            var sh = Resources.Load<Shader>("JumpSnow");
            var bs = Resources.Load<Shader>("StudioBackdrop");
            if (sh == null || bs == null) { Debug.LogWarning("StartJump: its shaders are missing"); return false; }
            stage = new GameObject("JumpScene").transform;
            stage.SetParent(transform, false);
            stage.position = At;
            // the snow
            snowMat = new Material(sh) { name = "JumpSnow" };
            snowMat.SetTexture("_Grain", Grain(128));
            // white as a ski film's snow: pure in the sun, the shade only just blue, the far snow into a white haze
            // (the camera draws it without the game's grading while it shows: StartPage)
            snowMat.SetColor("_Lit", Bright * Color.white); snowMat.SetColor("_Shade", Bright * new Color(0.86f, 0.9f, 0.96f));
            snowMat.SetColor("_Haze", Bright * Haze);
            var g = Piece("Snow", HillMesh(), snowMat);
            g.SetParent(stage, false);
            // the sky: a sphere round the camera, white haze at the horizon, a soft blue overhead
            skyMat = new Material(bs) { name = "JumpSky" };
            var st = SkyTexture(512, 256);
            skyMat.SetTexture("_MainTex", st); skyMat.SetTexture("_BaseMap", st);
            skyMat.SetColor("_Tint", Bright * Color.white); skyMat.SetColor("_BaseColor", Color.white);
            skyT = Piece("Sky", SkyMesh(1200f), skyMat);
            skyT.SetParent(stage, false);
            // the spray: a trail off the tail, and bursts at the pop and the landing
            trail = Spray("Trail", false);
            burst = Spray("Burst", true);
            Plan();
            SetLayer(stage);
            stage.gameObject.SetActive(false);
            return true;
        }

        ParticleSystem Spray(string name, bool bursts)
        {
            var go = new GameObject(name);
            go.transform.SetParent(stage, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(bursts ? 0.9f : 0.5f, bursts ? 1.8f : 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(bursts ? 1.5f : 0.6f, bursts ? 5.5f : 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(bursts ? 0.35f : 0.25f, bursts ? 1.1f : 0.6f);
            main.startColor = new Color(1f, 1f, 1f, 0.75f);
            main.gravityModifier = 0.35f;
            main.maxParticles = 400;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.useUnscaledTime = true;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = bursts ? 0.8f : 0.25f;
            var em = ps.emission; em.rateOverTime = 0f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = spray;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void Puff(Vector3 at, int n, float up)
        {
            burst.transform.localPosition = at;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < n; i++)
            {
                var v = new Vector3(Random.Range(0.5f, 3.5f), Random.Range(0.6f, 1f) * up, Random.Range(-2f, 2f));
                ep.velocity = stage.TransformDirection(v);
                burst.Emit(ep, 1);
            }
        }

        bool CopyRider(RiderBody body)
        {
            if (holder != null) Kill(holder.gameObject);
            var roll = body.RollFrame; var srcModel = body.HeroModel;
            if (roll == null || srcModel == null) return false;
            var path = PathTo(roll, srcModel);
            var c = Instantiate(roll.gameObject).transform;
            c.name = "JumpRider";
            Strip(c.gameObject);
            holder = c;
            holder.SetParent(stage, false);
            holder.localScale = roll.lossyScale;
            // only the board and the person: no skis, toboggan or anything else the rider carries about
            for (int i = 0; i < holder.childCount; i++)
            {
                var ch = holder.GetChild(i);
                bool keep = ch.name == "Board" || ch.name == "Body";
                ch.gameObject.SetActive(keep);
                if (keep) { ch.localPosition = Vector3.zero; ch.localRotation = Quaternion.identity; }
            }
            board = holder.Find("Board");
            model = Follow(holder, path);
            if (board == null || model == null) { Hide(); return false; }
            foreach (Transform b in board) if (b.name == "Snowboard") b.gameObject.SetActive(true);
            var table = Resources.Load<TextAsset>("Rider/HeroAnim");
            var h = table != null ? new HeroRig(model, table) : null;
            if (h == null || !h.Ok) { Hide(); return false; }
            rig = h;
            ridePos = body.HeroRidePos; baseRot = body.HeroBaseRot; rootRot = body.HeroRootRot;
            idRide = h.Id("Ride"); idToe = h.Id("Carve_Toe"); idHeel = h.Id("Carve_Heel");
            idPop = h.Id("Ollie_Pop"); idAir = h.Id("Air"); idLand = h.Id("Land"); idFlip = h.Id("Flip_Tuck");
            idWindL = h.Id("Spin_WindL"); idWindR = h.Id("Spin_WindR"); idSpinL = h.Id("Spin_AirL"); idSpinR = h.Id("Spin_AirR");
            string[] gn = { "Indy", "Melon", "Mute", "Method", "Stalefish", "TailGrab" };
            for (int i = 0; i < gn.Length; i++) { idGrab[i] = h.Id("Grab_" + gn[i]); grabHold[i] = h.CaptureHold(idGrab[i], RiderBody.GrabHand(i)); }
            // at rest on the deck, to take the face's and hood's frames from (as the rider was made)
            holder.localPosition = Surface(StartX); holder.localRotation = OnSnow(StartX, 0f);
            model.localPosition = ridePos; model.localRotation = baseRot;
            h.Begin(); h.Want(idRide, 1f); h.Snap(); h.Evaluate(0f);
            var body2 = holder.Find("Body");
            h.CaptureRest(body2.TransformDirection(Vector3.right), body2.up);
            SetLayer(holder);
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return true;
        }

        static List<int> PathTo(Transform root, Transform t)
        {
            var p = new List<int>();
            while (t != null && t != root) { p.Insert(0, t.GetSiblingIndex()); t = t.parent; }
            return t == root ? p : null;
        }
        static Transform Follow(Transform root, List<int> path)
        {
            if (path == null) return null;
            var t = root;
            foreach (int i in path) { if (i >= t.childCount) return null; t = t.GetChild(i); }
            return t;
        }

        // ---------------------------------------------------------------- meshes and pictures

        static Mesh HillMesh()
        {
            var xs = new List<float>(); var zs = new List<float>();
            for (float x = X0 + 2f; x < -14f; x += 2.5f) xs.Add(x);
            for (float x = -14f; x < 32f; x += 0.4f) xs.Add(x);
            for (float x = 32f; x <= X1 - 2f; x += 2.5f) xs.Add(x);
            for (float z = -70f; z < -12f; z += 3f) zs.Add(z);
            for (float z = -12f; z < -4f; z += 0.8f) zs.Add(z);
            for (float z = -4f; z < 4f; z += 0.2f) zs.Add(z);
            for (float z = 4f; z < 14f; z += 0.8f) zs.Add(z);
            for (float z = 14f; z < 340f; z += 4f) zs.Add(z);
            int nx = xs.Count, nz = zs.Count;
            var v = new Vector3[nx * nz]; var n = new Vector3[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = xs[i], z = zs[j];
                    v[j * nx + i] = new Vector3(x, Height(x, z), z);
                    n[j * nx + i] = Normal(x, z);
                }
            var tri = new int[(nx - 1) * (nz - 1) * 6];
            int k = 0;
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    tri[k++] = a; tri[k++] = c; tri[k++] = b; tri[k++] = b; tri[k++] = c; tri[k++] = d;
                }
            var m = new Mesh { name = "JumpHill", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v; m.normals = n; m.triangles = tri; m.RecalculateBounds();
            return m;
        }

        static Mesh SkyMesh(float r)
        {
            const int lon = 48, lat = 24;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int j = 0; j <= lat; j++)
            {
                float a = Mathf.Lerp(-90f, 90f, j / (float)lat) * Mathf.Deg2Rad;
                for (int i = 0; i <= lon; i++)
                {
                    float b = i / (float)lon * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(b)) * r);
                    uv.Add(new Vector2(i / (float)lon, j / (float)lat));
                }
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + 1, c = a + lon + 1, d = c + 1;
                    tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(b); tri.Add(d); tri.Add(c);
                }
            var m = new Mesh { name = "JumpSky" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>The scene's white (drawn straight, without the game's grading: StartPage turns it off for the jump).</summary>
        const float Bright = 1f;
        static readonly Color Haze = new Color(0.98f, 0.985f, 0.995f), Zenith = new Color(0.74f, 0.83f, 0.94f);

        static Texture2D SkyTexture(int w, int hgt)
        {
            var t = new Texture2D(w, hgt, TextureFormat.RGBA32, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, name = "JumpSky" };
            var px = new Color[w * hgt];
            for (int x = 0; x < w; x++)
            {
                                for (int y = 0; y < hgt; y++)
                {
                    float v = (y + 0.5f) / hgt;
                    float up = Mathf.Clamp01((v - 0.5f) * 2f);
                    Color c = Color.Lerp(Haze, Zenith, Mathf.Pow(up, 0.75f));
                    if (v <= 0.49f) c = Haze;
                    px[y * w + x] = c;
                }
            }
            t.SetPixels(px); t.Apply(false, true);
            return t;
        }

        static Texture2D Grain(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.R8, true) { wrapMode = TextureWrapMode.Repeat, name = "JumpGrain" };
            var px = new Color[n * n];
            var rnd = new System.Random(11);
            for (int i = 0; i < px.Length; i++) { float g = 0.8f + (float)(rnd.NextDouble() - 0.5) * 0.5f; px[i] = new Color(g, g, g, 1f); }
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        static Transform Piece(string name, Mesh m, Material mat)
        {
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g.transform;
        }

        static void Kill(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
        static void SetLayer(Transform t) { foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = Layer; }

        /// <summary>Only what is drawn stays: no scripts, colliders, joints, lights, sounds or particles on the copy.</summary>
        static void Strip(GameObject go)
        {
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            for (int pass = 0; pass < 3; pass++)
            {
                bool left = false;
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                    if (c is Behaviour bh) bh.enabled = false;
                    DestroyImmediate(c);
                    if (c != null) left = true;
                }
                if (!left) break;
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
        }

        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
