using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The start page's other view (StartPage.Home: JUMP): a still from a ski film come to life. Bright white snow and a
    /// pale sky, a kicker cut into the slope, and your rider (a copy of them as they are, in what they wear) coming
    /// down to it, popping off the lip and putting down a trick (a backside 360 with an indy, a backflip with a mute,
    /// a frontside 540 with a method, in turn), in slow motion from the lip to the landing, then landing in a burst of
    /// snow and riding away. Each loop is on the other gear: the snowboard, then skis with a pole in each hand (their
    /// grabs a safety, a mute and a tail), never the two at once. The edges of the frame are soft (JumpEdgeBlur), the
    /// rider sharp in the middle. The camera is a film operator's: low and well off to the side on a long lens, panning with the
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

        // the tricks, in turn: degrees of spin (+ backside), whole flips (+ back), the board's grab (RiderBodyHero's
        // order: Indy, Melon, Mute, Method, Stalefish, TailGrab) and the skis' (RiderBodyRide's: Safety, Mute, Tail)
        static readonly (float spin, float flip, int grab, int skiGrab)[] Tricks = { (360f, 0f, 0, 0), (0f, 1f, 2, 1), (-540f, 0f, 3, 2) };
        // ---- the slow motion: how slow the clock runs through the air (eased in over the lip, out into the landing)
        const float Slow = 0.3f;

        public bool Showing { get; private set; }
        /// <summary>Seconds into the loop (the test tools set it to look at any moment of it).</summary>
        public float Clock;
        /// <summary>Which trick the loop is on (0.. Tricks).</summary>
        public int Trick;
        /// <summary>On skis (and poles) this loop, or the snowboard: never both. Each loop is the other one.</summary>
        public bool OnSkis => onSkis;
        /// <summary>How fast the loop's clock runs this frame (1 on the snow, Slow in the air). Only this scene's own
        /// clock: Time.timeScale and the game's time are never touched.</summary>
        public float Rate { get; private set; } = 1f;
        static int visits;   // each visit to the page starts on the other one

        Transform stage, holder, board, model, skyT;
        Transform skisT; readonly Transform[] skiT = new Transform[2], poleT = new Transform[2];
        bool hasSkis, onSkis;
        Vector3 skiModelPos; readonly Vector3[] skiOff = new Vector3[2]; readonly Quaternion[] skiTurn = new Quaternion[2];
        int idSRide, idSTurnL, idSTurnR, idSPop, idSAir, idSLand, idSWindL, idSWindR, idSSpinL, idSSpinR, idSFlip;
        readonly int[] idSGrab = new int[3];
        readonly HeroRig.Hold[] sGrabHold = new HeroRig.Hold[3];
        // ---- the blur at the edges of the frame (JumpEdgeBlur: one pass over URP's copy of the opaque picture)
        Material blurMat; Transform blurT;
        UnityEngine.Rendering.Universal.UniversalAdditionalCameraData camData;
        bool colorKept; UnityEngine.Rendering.Universal.CameraOverrideOption colorWas;
        Vector2 centre; bool centreSet;
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
            Clock = 0f; Trick = 0; aimSet = false; lastPhase = -1; Rate = 1f; centreSet = false;
            SetGear(hasSkis && (visits++ & 1) == 1);
            // the blur reads URP's copy of the opaque picture: asked for on this camera while the page shows (the
            // phone's pipeline doesn't make one otherwise), put back as it was on Hide
            if (blurMat != null && !colorKept)
            {
                camData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (camData != null) { colorWas = camData.requiresColorOption; camData.requiresColorOption = UnityEngine.Rendering.Universal.CameraOverrideOption.On; colorKept = true; }
            }
            if (blurT != null) blurT.gameObject.SetActive(colorKept);
            Frame(0f, 0f);
            return true;
        }

        public void Hide()
        {
            Showing = false;
            if (holder != null) Kill(holder.gameObject);
            holder = null; board = null; model = null; rig = null; skisT = null;
            for (int s = 0; s < 2; s++) { skiT[s] = null; poleT[s] = null; }
            if (colorKept && camData != null) camData.requiresColorOption = colorWas;
            colorKept = false;
            Rate = 1f;
            if (stage != null) stage.gameObject.SetActive(false);
        }

        /// <summary>The snowboard, or the skis and the poles in the hands: one or the other, never both.</summary>
        void SetGear(bool skis)
        {
            onSkis = skis && hasSkis;
            if (board != null) board.gameObject.SetActive(!onSkis);
            if (skisT != null) skisT.gameObject.SetActive(onSkis);
            for (int s = 0; s < 2; s++) if (poleT[s] != null) poleT[s].gameObject.SetActive(onSkis);
            if (rig != null) { rig.ResetSprings(); lastPhase = -1; }
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
            // the air is slow motion: the clock eases down as the rider comes up the kicker, hangs at Slow the whole
            // way through the trick, and eases back up into the landing. Only this scene's clock (and its spray, and
            // the camera's pan with it) runs slow: the game's time is never touched
            Rate = SlowAt(Clock - approachTime);
            float sdt = dt * Rate;
            Clock += sdt;
            if (Clock >= loop) { Clock -= loop; Trick = (Trick + 1) % Tricks.Length; aimSet = false; SetGear(!onSkis); }
            var tm = trail.main; tm.simulationSpeed = Rate;
            var bm = burst.main; bm.simulationSpeed = Rate;
            Pose(Clock, sdt);
            Shoot(sdt, leave);
        }

        /// <summary>The clock's rate at `u` seconds (loop time) from the lip.</summary>
        float SlowAt(float u)
        {
            float into = Sm(-0.35f, 0.1f * airTime, u), outOf = Sm(0.86f * airTime, airTime + 0.3f, u);
            return Mathf.Lerp(1f, Slow, into * (1f - outOf));
        }

        /// <summary>
        /// Test tools: play trick `trick` from the top of the loop to `airAt` through its air (0 the lip, 1 the landing;
        /// below 0 seconds before the lip, past 1 seconds after the landing), frame by frame as the game would.
        /// `skis` 0 the snowboard, 1 the skis (-1 as it is).
        /// </summary>
        public void Seek(int trick, float airAt, int skis = -1)
        {
            if (skis >= 0) SetGear(skis == 1);
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

            // ---- the body, from the clips: the board's (side-on to the way they go), or the skis' (facing it)
            var h = rig;
            bool sk = onSkis;
            int cRide = sk ? idSRide : idRide, cPop = sk ? idSPop : idPop, cAir = sk ? idSAir : idAir, cLand = sk ? idSLand : idLand;
            int cFlip = sk ? idSFlip : idFlip, cWindL = sk ? idSWindL : idWindL, cWindR = sk ? idSWindR : idWindR;
            int cSpinL = sk ? idSSpinL : idSpinL, cSpinR = sk ? idSSpinR : idSpinR;
            h.Begin();
            Transform gear = sk ? skisT : board;
            HeroRig.Drive d;
            if (sk)
            {
                model.localPosition = skiModelPos; model.localRotation = RiderBody.SkiFacing * baseRot;
                d = new HeroRig.Drive
                {
                    board = skisT, rootInBoard = new Vector3(0f, RiderBody.DeckTop, 0f), rootRotInBoard = RiderBody.SkiFacing * rootRot,
                    up = holder.up, toe = holder.forward, nose = -holder.right, feet = true,
                };
            }
            else
            {
                model.localPosition = ridePos; model.localRotation = baseRot;
                d = new HeroRig.Drive
                {
                    board = board, rootInBoard = new Vector3(0f, RiderBody.DeckTop, 0f), rootRotInBoard = rootRot,
                    up = board.up, toe = board.right, nose = board.forward, feet = true,
                };
            }
            float blend = 9f, liftWant = 0f;
            if (phase != lastPhase)
            {
                if (phase == 1) { h.Restart(cPop); Puff(lipPos, 14, 2.5f); }
                if (phase == 2) { h.Restart(cLand); compV = -2.4f; Puff(p, 70, 5.5f); }
                if (phase == 0) { h.ResetSprings(); }
            }
            float ft = phase == 0 ? t : phase == 1 ? t - approachTime : t - approachTime - airTime;
            if (phase == 0)
            {
                // a few small carves (or turns) on the way in, then square to the lip and down into the legs for the pop
                float toLip = approachTime - t;
                float carve = Mathf.Sin(t * 2.1f) * 0.35f * Sm(0.6f, 1.6f, toLip);
                h.Want(cRide, 1f - Mathf.Abs(carve));
                if (sk) h.Want(carve > 0f ? idSTurnR : idSTurnL, Mathf.Abs(carve));
                else { h.Want(carve > 0f ? idToe : idHeel, Mathf.Abs(carve)); d.lean = carve * 10f; }
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
                h.Want(cAir, Mathf.Max(0.05f, restW * (1f - Mathf.Max(wind, wrap))));
                h.Want(tr.spin < 0f ? cWindL : cWindR, wind * restW);
                h.Want(tr.spin < 0f ? cSpinL : cSpinR, wrap * restW);
                int gi = sk ? tr.skiGrab : tr.grab;
                int gClip = sk ? idSGrab[gi] : idGrab[gi];
                if (grab > 0f && gClip >= 0)
                {
                    h.Want(gClip, grab * 3f);
                    var hold = sk ? sGrabHold[gi] : grabHold[gi];
                    int hand = sk ? RiderBody.SkiGrabHand(gi) : RiderBody.GrabHand(gi);
                    if (hand == 0) { d.handL = grab; d.holdL = hold; } else { d.handR = grab; d.holdR = hold; }
                }
                if (fl > 0f) h.Want(cFlip, fl * 1.5f);
                liftWant = 0.11f * Mathf.Max(grab, fl);
                if (ft < h.Length(cPop)) h.Want(cPop, 2f * (1f - Sm(0.55f, 1f, ft / Mathf.Max(0.01f, h.Length(cPop)))));
                // spotting the landing as it comes round
                d.look = (landPos - p) + Vector3.right * 3f; d.lookWeight = 0.35f + 0.4f * Sm(0.55f, 0.85f, f);
            }
            else
            {
                h.Want(cRide, 1f);
                if (ft < h.Length(cLand)) h.Want(cLand, 2f * (1f - Sm(0.3f, 1f, ft / Mathf.Max(0.01f, h.Length(cLand)))));
                d.look = Vector3.right * 4f + Vector3.down; d.lookWeight = 0.4f;
            }
            // the legs take the landing and spring back
            compV += (-comp * 140f - compV * 16f) * dt;
            comp = Mathf.Clamp(comp + compV * dt, -0.15f, 0.6f);
            d.drop += Mathf.Max(0f, comp) * 0.18f;
            lift = dt <= 0f ? liftWant : Mathf.Lerp(lift, liftWant, 1f - Mathf.Exp(-dt * 12f));
            gear.localPosition = Vector3.up * lift;
            if (dt <= 0f || phase != lastPhase && phase == 0) h.Snap();
            h.Evaluate(Mathf.Max(dt, 0f), blend);
            h.Layer(d, Mathf.Max(dt, 0f));
            // each ski goes with its own foot (as on a run: RiderBodyRide.PoseSkis)
            if (sk)
                for (int s = 0; s < 2; s++)
                    if (skiT[s] != null) { Transform ft2 = h.Foot[s]; skiT[s].SetPositionAndRotation(ft2.position + ft2.rotation * skiOff[s], ft2.rotation * skiTurn[s]); }
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
            else aim = Vector3.SmoothDamp(aim, want, ref aimVel, 0.07f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
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
            // the blur's sharp middle on the rider (the operator keeps them near the middle anyway: this holds it on
            // them as the pan lags or leads)
            if (blurMat != null && blurT != null)
            {
                blurT.position = cam.transform.position;
                Vector3 vp = cam.WorldToViewportPoint(holder.position + stage.up * 0.9f);
                var c = new Vector2(Mathf.Clamp(vp.x, 0.2f, 0.8f), Mathf.Clamp(vp.y, 0.2f, 0.8f));
                if (vp.z <= 0f) c = new Vector2(0.5f, 0.5f);
                centre = !centreSet || dt <= 0f ? c : Vector2.Lerp(centre, c, 1f - Mathf.Exp(-dt * 10f));
                centreSet = true;
                blurMat.SetVector("_Centre", new Vector4(centre.x, centre.y, 0f, 0f));
                // and a little more of it as the lens pushes in on the way into the game
                blurMat.SetFloat("_Radius", BlurRadius * (1f + 1.5f * k));
            }
        }
        const float BlurRadius = 0.02f;
        /// <summary>The blurred edges on or off (the test tools compare the two).</summary>
        public bool Blur { get => blurT != null && blurT.gameObject.activeSelf; set { if (blurT != null) blurT.gameObject.SetActive(value && colorKept); } }

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
            // wind-sculpted swells: long soft waves across the wind, steeper on their lee side
            float sw = Mathf.Sin(x * 0.11f + z * 0.24f + Mathf.PerlinNoise(x * 0.03f, z * 0.03f) * 4f);
            h += (sw + 0.35f * sw * sw) * 0.28f * Sm(3.5f, 9f, az);
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
            snowMat.SetColor("_Lit", Bright * Color.white); snowMat.SetColor("_Shade", Bright * new Color(0.76f, 0.83f, 0.94f));
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
            // the blur at the edges: a screen-filling quad drawn last (no shader, no blur; the scene still shows)
            var eb = Resources.Load<Shader>("JumpEdgeBlur");
            if (eb != null && eb.isSupported)
            {
                blurMat = new Material(eb) { name = "JumpEdgeBlur" };
                var qm = new Mesh { name = "JumpEdgeBlur" };
                qm.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
                qm.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                qm.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);   // (placed at the camera: never culled)
                blurT = Piece("EdgeBlur", qm, blurMat);
                blurT.SetParent(stage, false);
            }
            else Debug.LogWarning("StartJump: no JumpEdgeBlur shader: the jump without its blurred edges");
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
            // only the person and what they ride down the jump (the board, or the skis): no toboggan or anything else
            // the rider carries about (SetGear shows one of the two)
            for (int i = 0; i < holder.childCount; i++)
            {
                var ch = holder.GetChild(i);
                bool keep = ch.name == "Board" || ch.name == "Body" || ch.name == "Skis";
                ch.gameObject.SetActive(keep);
                if (keep) { ch.localPosition = Vector3.zero; ch.localRotation = Quaternion.identity; }
            }
            board = holder.Find("Board");
            model = Follow(holder, path);
            if (board == null || model == null) { Hide(); return false; }
            foreach (Transform b in board) if (b.name == "Snowboard") b.gameObject.SetActive(true);
            // the skis (under the roll) and the poles (in the hands): only if the rider has them
            skisT = holder.Find("Skis");
            skiT[0] = skisT != null ? skisT.Find("SkiL") : null; skiT[1] = skisT != null ? skisT.Find("SkiR") : null;
            foreach (var t in holder.GetComponentsInChildren<Transform>(true)) { if (t.name == "PoleL") poleT[0] = t; else if (t.name == "PoleR") poleT[1] = t; }
            for (int s = 0; s < 2; s++)
            {
                if (poleT[s] != null) poleT[s].gameObject.SetActive(false);
                skiOff[s] = body.HasSkis ? body.SkiOffset(s) : Vector3.zero; skiTurn[s] = body.HasSkis ? body.SkiTurnOf(s) : Quaternion.identity;
                if (skiT[s] != null) skiT[s].gameObject.SetActive(true);
            }
            if (skisT != null) skisT.gameObject.SetActive(false);
            skiModelPos = body.SkiModelPos;
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
            // the skis' clips (RiderBodyRide's)
            idSRide = h.Id("Ski_Ride"); idSTurnL = h.Id("Ski_TurnL"); idSTurnR = h.Id("Ski_TurnR");
            idSPop = h.Id("Ski_Pop"); idSAir = h.Id("Ski_Air"); idSLand = h.Id("Ski_Land"); idSFlip = h.Id("Ski_Flip");
            idSWindL = h.Id("Ski_WindL"); idSWindR = h.Id("Ski_WindR"); idSSpinL = h.Id("Ski_SpinL"); idSSpinR = h.Id("Ski_SpinR");
            string[] sg = { "Safety", "Mute", "Tail" };
            for (int i = 0; i < sg.Length; i++) { idSGrab[i] = h.Id("Ski_Grab_" + sg[i]); sGrabHold[i] = h.CaptureHold(idSGrab[i], RiderBody.SkiGrabHand(i)); }
            hasSkis = body.HasSkis && skiT[0] != null && skiT[1] != null && poleT[0] != null && poleT[1] != null
                      && idSRide >= 0 && idSAir >= 0 && idSPop >= 0 && idSLand >= 0 && idSTurnL >= 0 && idSTurnR >= 0;
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
            // soft occlusion in the dips (in the vertices' red): how far the snow lies below its surroundings
            var col = new Color[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = xs[i], z = zs[j], hh = v[j * nx + i].y;
                    float around = 0f;
                    for (int a = 0; a < 6; a++)
                    {
                        float ang = a * Mathf.PI / 3f;
                        around += Height(x + Mathf.Cos(ang) * 2.5f, z + Mathf.Sin(ang) * 2.5f) + Height(x + Mathf.Cos(ang + 0.5f) * 6f, z + Mathf.Sin(ang + 0.5f) * 6f);
                    }
                    float dip = around / 12f - hh;
                    float ao = 1f - Mathf.Clamp01(dip * 2.2f);
                    col[j * nx + i] = new Color(ao, ao, ao, 1f);
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
            m.vertices = v; m.normals = n; m.colors = col; m.triangles = tri; m.RecalculateBounds();
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
