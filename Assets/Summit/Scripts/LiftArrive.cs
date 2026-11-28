using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// (lifttop) The hero arriving at the camp as everyone does: on the Summit lift. Coming into the camp (from the start
    /// page, or back after a run) the hero rides the last span up on a chair of their own, the chairs ahead and behind
    /// full of the mountain's people; their hands go to the bar as it swings up on the last span, they stand up as the
    /// chair comes onto the deck and push off it as the mountain's people do (lifttop2: on a board the back foot out,
    /// skating; on skis the poles), glide off the deck's uphill end onto the pad and along the way to the gate, and skid
    /// to a stop; and the camp takes over (on foot, the gear left at the
    /// gate as ever: Camp). About ten seconds. The camera is a chase camera: behind and above the chair, the station and
    /// the camp coming up ahead and to the right; then behind the hero as they glide, settling where the camp's own
    /// camera sits so the hand-over doesn't show. A tap skips to the end. No words on screen.
    ///
    /// Nothing can leave the hero stuck in it: no Summit lift on the bench, no chair to ride, or anything going wrong
    /// along the way, and it ends at once, the hero standing where the glide would have stopped (or where the camp
    /// puts them without it); and it gives up after MaxTime whatever happens.
    /// </summary>
    public class LiftArrive : RiderBody.ICampPose
    {
        public static bool On = true;
        /// <summary>For the tests: fail on purpose this far in (0: never).</summary>
        public static float TestFailAt;
        /// <summary>How long the ride lasts before the chair reaches the unload (about), standing up, the most it can take.</summary>
        public const float RideTime = 2.6f, StandTime = 0.8f, StopTime = 0.6f, MaxTime = 16f;
        /// <summary>(lifttop2) The hero's chair's bar while they ride it (0 down .. 1 up; below 0 none: the chair's own, SkiLift.BarUp): swung up over the ride's last seconds, whichever chair it is.</summary>
        public static float HeroBar = -1f;
        public enum Phase { None, Ride, Stand, Glide, Stop, Done }
        public Phase Now { get; private set; }
        public float T { get; private set; }
        public bool Skipped { get; private set; }
        /// <summary>Why it didn't play (it fell back to the camp's own arrival), for the tests.</summary>
        public string Why { get; private set; } = "";
        /// <summary>Where it ends: the hero on the bench, facing (degrees) toward the camp.</summary>
        public Vector3 EndPos { get; private set; }
        public float EndYaw { get; private set; }
        public LiftLine.Lift Lift => l;
        public int Chair => chair;

        Rider rider; RiderBody body; Camera cam;
        LiftLine.Lift l; int chair, lap, seat = 1;
        double clock0; float rideFor;
        Vector3 campSide, outer;
        Matrix4x4 chairM;
        readonly List<Vector3> glide = new List<Vector3>(); readonly List<float> glideS = new List<float>();
        float glideLen, gs, speed, stopT, heading, glideT;
        Vector3 standAt, lookAhead, camVel;
        float camFace;
        bool skis, gear;
        int idStand = -1;

        /// <summary>Start it. False if it can't (Why says why): the caller puts the hero in the camp the usual way.</summary>
        public bool Begin(Rider r, RiderBody b, Camera c)
        {
            Now = Phase.None; Skipped = false; T = 0f; Why = "";
            if (!On) { Why = "off"; return false; }
            rider = r; body = b; cam = c;
            try
            {
                l = null;
                foreach (var x in LiftLine.Lifts) if (x.summit) l = x;
                if (l == null || l.cum == null || l.chairs <= 0) { Why = "no Summit lift"; return false; }
                if (!LiftTop.AtBench(l)) { Why = "the Summit lift isn't on the bench"; return false; }
                if (rider == null || body == null || cam == null) { Why = "no hero"; return false; }
                // the first chair that comes to the unload no sooner than RideTime + StandTime from now (so the ride is
                // never cut short; at most a chair's interval longer)
                float unload = LiftRiders.UnloadAt(l), want = RideTime + StandTime, best = float.MaxValue;
                clock0 = SkiLift.Now; chair = -1;
                for (int k = 0; k < l.chairs; k++)
                {
                    SkiLift.ChairAt(l, k, clock0, out float d, out _);
                    float ahead = Mathf.Repeat(unload - d, l.loop) / LiftLine.Speed;
                    if (ahead >= want && ahead < best) { best = ahead; chair = k; rideFor = ahead; }
                }
                if (chair < 0 || best > want + 6f) { Why = "no chair"; return false; }
                SkiLift.ChairAt(l, chair, clock0 + rideFor, out _, out lap);
                LiftRiders.HeroLift = l.id; LiftRiders.HeroChair = chair; LiftRiders.HeroLap = lap;
                // toward the camp, square to the line (LiftEnds' side)
                float a = l.top.x - Slope.Centre(l.top.z);
                campSide = l.across * (Vector3.Dot(l.across, Vector3.right) * (a > 0f ? -1f : 1f) >= 0f ? 1f : -1f);
                // the outer seat, away from the chairs coming down (the camera rides alongside, out there)
                var m0 = SkiLift.ChairAt(l, chair, clock0, out _, out _);
                float upSide = Vector3.Dot((Vector3)m0.GetColumn(3) - l.top, campSide);
                outer = campSide * (upSide >= 0f ? 1f : -1f);
                seat = Vector3.Dot(m0.MultiplyVector(Vector3.right), outer) > 0f ? 3 : 0;
                LayGlide();
                skis = body.OnSkis;
                rider.Body.isKinematic = true;
                body.SetWalking(true);
                body.CampPose = this;
                Now = Phase.Ride; T = 0f; camFace = float.NaN;
                Place(0f);
                Camera(0f, true);
                return true;
            }
            catch (System.Exception e)
            {
                Why = "failed: " + e.Message;
                Debug.LogWarning("LiftArrive: " + e);
                End();
                return false;
            }
        }

        /// <summary>The glide off: from where the seat is at the unload, off the deck's uphill end, round toward the camp, to a stop.</summary>
        void LayGlide()
        {
            var mU = SkiLift.ChairAt(l, chair, clock0 + rideFor, out _, out _);
            var seatU = (mU * LiftRiders.SeatLocal(seat)).GetColumn(3);
            float u0 = Vector3.Dot((Vector3)seatU - l.top, campSide), v0 = Vector3.Dot((Vector3)seatU - l.top, l.dir);
            Vector3 P(float v, float u) => l.top + l.dir * v + campSide * u;
            // (off the deck's uphill end, round toward the camp's side, and back the way the line comes: toward the
            // camp, down the way in)
            float us = Mathf.Max(u0, 0f);
            // (lifttop2: off the deck's uphill end and round to the right onto the way to the gate, along it a little)
            var key = new[] { P(v0, u0), P(v0 - 4f, u0), P(-7.4f, u0 + 1f), P(-9.4f, us + 4f), P(-9.8f, us + 9f), P(-9.3f, us + 14f), P(-8.5f, us + 18.5f) };
            glide.Clear(); glideS.Clear();
            for (int i = 0; i < key.Length - 1; i++)
            {
                Vector3 p0 = key[Mathf.Max(0, i - 1)], p1 = key[i], p2 = key[i + 1], p3 = key[Mathf.Min(key.Length - 1, i + 2)];
                for (int j = 0; j < 8; j++)
                {
                    float t = j / 8f, t2 = t * t, t3 = t2 * t;
                    glide.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            glide.Add(key[key.Length - 1]);
            float s = 0f;
            for (int i = 0; i < glide.Count; i++)
            {
                var g = glide[i]; g.y = Ground(g); glide[i] = g;
                if (i > 0) s += Vector2.Distance(new Vector2(glide[i - 1].x, glide[i - 1].z), new Vector2(g.x, g.z));
                glideS.Add(s);
            }
            glideLen = s;
            standAt = glide[0];
            EndPos = glide[glide.Count - 1];
            // facing the way they were going at the end: toward the camp, down the way in
            var to = glide[glide.Count - 1] - glide[glide.Count - 4]; to.y = 0f;
            EndYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        }

        /// <summary>The deck under a point while over it, else the snow.</summary>
        float Ground(Vector3 p)
        {
            var local = p - l.top;
            float v = Vector3.Dot(local, l.dir), u = Vector3.Dot(local, campSide);
            bool deck = Mathf.Abs(v) < LiftLine.StationHalfLen && Mathf.Abs(u) < LiftLine.StationHalfWid;
            float snow = Slope.DrawnHeight(p.x, p.z);
            return deck ? Mathf.Max(l.top.y, snow) : snow;
        }

        /// <summary>One frame. True once it's over (played out, skipped, or given up): the camp takes the hero.</summary>
        public bool Step(float dt, bool tapped)
        {
            if (Now == Phase.None || Now == Phase.Done) return true;
            try
            {
                T += dt;
                if (TestFailAt > 0f && T > TestFailAt) throw new System.Exception("a test's failure");
                if (tapped && T > 0.15f) { Skipped = true; Finish(); return true; }
                if (T > MaxTime) { Why = "too long"; Finish(); return true; }
                switch (Now)
                {
                    case Phase.Ride:
                        if (T >= rideFor - StandTime) Now = Phase.Stand;
                        break;
                    case Phase.Stand:
                        if (T >= rideFor)
                        {
                            // up and off: on the gear now, gliding (the ride's own stance: RiderBody, as at the gate)
                            Now = Phase.Glide; gs = 0f; glideT = 0f; speed = LiftLine.Speed * 0.9f;
                            body.CampPose = null;
                            body.SetWalking(false);
                            body.RideAfter = PushOff;   // (skating off on a board, the back foot out; pushing on the poles on skis)
                            var d0 = glide[Mathf.Min(1, glide.Count - 1)] - glide[0];
                            heading = Mathf.Atan2(d0.x, d0.z);
                        }
                        break;
                    case Phase.Glide:
                    {
                        // gliding at the chair's pace off the deck, then a skid to a stop over the last few metres
                        float stopLen = 4f, cruise = Mathf.Max(0.1f, glideLen - stopLen);
                        speed = gs < cruise ? Mathf.Lerp(LiftLine.Speed * 0.9f, 6f, gs / cruise) : Mathf.Max(0.35f, 6f * Mathf.Sqrt(Mathf.Max(0f, (glideLen - gs) / stopLen)));
                        gs += speed * dt; glideT += dt;
                        if (gs >= glideLen) { gs = glideLen; Now = Phase.Stop; stopT = 0f; }
                        break;
                    }
                    case Phase.Stop:
                        stopT += dt;
                        if (stopT >= StopTime) { Finish(); return true; }
                        break;
                }
                Place(dt);
                Camera(dt, false);
                return false;
            }
            catch (System.Exception e)
            {
                Why = "failed: " + e.Message;
                Debug.LogWarning("LiftArrive: " + e);
                Finish();
                return true;
            }
        }

        /// <summary>Over: the chair let go, the hero back on foot, standing where the glide stops.</summary>
        void Finish()
        {
            Now = Phase.Done;
            End();
        }

        /// <summary>Let go of everything it holds (safe to call any time).</summary>
        public void End()
        {
            LiftRiders.HeroLift = LiftRiders.HeroChair = LiftRiders.HeroLap = -1;
            HeroBar = -1f;
            if (body != null)
            {
                if (body.RideAfter == (System.Action<HeroRig, Transform, float>)PushOff) body.RideAfter = null;
                if (ReferenceEquals(body.CampPose, this)) body.CampPose = null;
                body.GearAt(null, Quaternion.identity);
            }
            if (Now != Phase.Done) Now = Phase.Done;
        }

        // ---------------------------------------------------------------- where the hero is

        Matrix4x4 ChairNow() => SkiLift.ChairAt(l, chair, clock0 + T, out _, out _);

        void Place(float dt)
        {
            if (Now == Phase.Ride || Now == Phase.Stand)
            {
                chairM = ChairNow();
                // (the bar: up over a second and a bit, ending as they stand)
                HeroBar = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rideFor - StandTime - 2.0f, rideFor - StandTime - 0.6f, T));
                var seatM = chairM * LiftRiders.SeatLocal(seat);
                Vector3 at = seatM.GetColumn(3);
                var fwd = chairM.MultiplyVector(Vector3.forward); fwd.y = 0f; fwd.Normalize();
                // (the camp's convention: the model faces the board's toe edge, a quarter turn from the heading)
                float yaw = Mathf.Atan2(fwd.x, fwd.z);
                float w = Now == Phase.Stand ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((T - (rideFor - StandTime)) / StandTime)) : 0f;
                var p = Vector3.Lerp(at, new Vector3(at.x, l.top.y, at.z), w);
                rider.Body.position = p + Vector3.up * Rider.R; rider.transform.position = p + Vector3.up * Rider.R;
                rider.Heading = yaw - Mathf.PI * 0.5f;
                body.WalkPace = 0f; body.RunBlend = 0f; body.Cadence = 0f;
                body.FeetGround = float.NaN; body.Upright = true; body.PhaseLock = float.NaN;
                return;
            }
            HeroBar = -1f;   // (off it: the chair's own, up through the station)
            // gliding: on the board (or the skis) along the way, the board's nose ahead; skidding round across it to stop
            Vector3 g = Along(gs, out Vector3 dir);
            float travel = Mathf.Atan2(dir.x, dir.z);
            float stopK = Now == Phase.Stop ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(glideLen - 4f, glideLen, gs));
            // facing the camp at the end: a board turned across the way (a skid), skis turned the same way as the body
            float face = EndYaw * Mathf.Deg2Rad;
            float endHeading = skis ? face : face - Mathf.PI * 0.5f;
            float want = Mathf.LerpAngle(travel * Mathf.Rad2Deg, endHeading * Mathf.Rad2Deg, stopK) * Mathf.Deg2Rad;
            heading = dt > 0f ? Mathf.LerpAngle(heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 10f)) * Mathf.Deg2Rad : want;
            rider.Body.position = g + Vector3.up * Rider.R; rider.transform.position = g + Vector3.up * Rider.R;
            rider.Heading = heading;
        }

        Vector3 Along(float s, out Vector3 dir)
        {
            int i = 1;
            while (i < glideS.Count - 1 && glideS[i] < s) i++;
            float t = Mathf.InverseLerp(glideS[i - 1], glideS[i], s);
            dir = glide[i] - glide[i - 1]; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = -l.dir;
            dir.Normalize();
            return Vector3.Lerp(glide[i - 1], glide[i], t);
        }

        // ---------------------------------------------------------------- the hero on the chair (RiderBody.CampPose)

        public bool Want(HeroRig h)
        {
            if (Now != Phase.Ride && Now != Phase.Stand) return false;
            if (idStand < 0) idStand = h.Id("Stand");
            h.Want(idStand, 1f);
            return true;
        }

        public void After(HeroRig h, Transform model, float dt)
        {
            if (Now != Phase.Ride && Now != Phase.Stand) return;
            var m = chairM;
            var seatTop = (m * LiftRiders.SeatLocal(seat)).GetColumn(3);
            var fwd = m.MultiplyVector(Vector3.forward); fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            // standing up: from the seat to the deck under it, over StandTime
            float w = Now == Phase.Stand ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((T - (rideFor - StandTime)) / StandTime)) : 0f;
            Vector3 standPos = model.position; Quaternion standRot = model.rotation;
            // (on a board the body comes round side-on as it stands, as it will ride)
            var standFace = skis ? fwd : Quaternion.AngleAxis(90f, Vector3.up) * fwd;   // (side-on to the way, as the board's rider stands)
            CampPoses.Stand(h, model, new Vector3(seatTop.x, l.top.y, seatTop.z), Vector3.Slerp(fwd, standFace, w).normalized);
            standPos = model.position; standRot = model.rotation;
            CampPoses.Seat(h, model, seatTop, fwd);
            if (w > 0f) { model.position = Vector3.Lerp(model.position, standPos, w); model.rotation = Quaternion.Slerp(model.rotation, standRot, w); }
            // the gear: across the footrest under the feet (a board, both feet on it) or the skis on it, pointing on
            Vector3 rest = m.MultiplyPoint3x4(new Vector3(LiftRiders.SeatX[seat], LiftRiders.FootrestY + 0.035f, LiftRiders.FootrestZ - 0.05f));
            Quaternion restRot = skis ? Quaternion.LookRotation(fwd, Vector3.up) : Quaternion.LookRotation(-right, Vector3.up);
            Vector3 deck = new Vector3(seatTop.x, l.top.y + 0.01f, seatTop.z);
            Quaternion deckRot = skis ? Quaternion.LookRotation(fwd, Vector3.up) : Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 gp = Vector3.Lerp(rest, deck, w); Quaternion gr = Quaternion.Slerp(restRot, deckRot, w);
            gear = body.GearAt(gp, gr);
            if (w < 0.6f)
            {
                // the feet on the gear (or the footrest), the hands in the lap
                float k = 1f - w / 0.6f;
                Vector3 hips = h.Hips.position;
                var across = gr * Vector3.forward;   // (the board's length: across the chair)
                for (int sd = 0; sd < 2; sd++)
                {
                    float side = sd == 0 ? -1f : 1f;
                    Vector3 foot = skis ? gp + gr * new Vector3(side * 0.13f, 0.1f, 0f) : gp + across * (-side * 0.24f) + Vector3.up * 0.11f;
                    Vector3 cur = h.Foot[sd].position;
                    h.LegTo(sd, Vector3.Lerp(cur, foot, k), hips + fwd * 0.9f + Vector3.up * 0.4f + right * side * 0.12f);
                }
                if (w <= 0f)
                {
                    // the hands in the lap; up to the bar as it swings up nearing the station (the riders' way: LiftRiders)
                    SkiLift.ChairAt(l, chair, clock0 + T, out float dNow, out _);
                    float up = HeroBar >= 0f ? HeroBar : SkiLift.BarUp(l, dNow);
                    float lift = up > 0.02f && up < 0.98f ? Mathf.Clamp01(Mathf.Sin(up * Mathf.PI) * 1.6f) : 0f;
                    for (int sd = 0; sd < 2; sd++)
                    {
                        float side = sd == 0 ? -1f : 1f;
                        var lap = hips + fwd * 0.3f + right * side * 0.13f + Vector3.up * 0.12f;
                        var bar = m.MultiplyPoint3x4(SkiLift.BarPoint(LiftRiders.SeatX[seat] + side * 0.14f, up));
                        h.ArmTo(sd, Vector3.Lerp(lap, bar, lift), h.UpperArm[sd].position + right * side * 0.35f - Vector3.up * 0.3f);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- pushing off the deck (RiderBody.RideAfter)

        /// <summary>How far along the glide the pushing goes on (m), and a stride's length (s): the people's own (LiftEnds).</summary>
        const float PushFor = 7f, Stride = 0.8f;

        /// <summary>
        /// Off the chair as the mountain's people go (LiftEnds): on a board, the front foot strapped in and the back
        /// foot out of its binding, skating, planted on the toe side beside the board and swept back, lifted and
        /// brought forward again, a stride every 0.8 s; on skis, the poles planted ahead beside the boots and drawn
        /// back past the hips. Over the first few metres of the glide, then back on the gear (the board's back foot
        /// on it again) for the run in and the stop.
        /// </summary>
        void PushOff(HeroRig h, Transform model, float dt)
        {
            if (Now != Phase.Glide || h == null || !h.Ok) return;
            float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PushFor - 2f, PushFor, gs));
            if (k <= 0f) return;
            k *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(glideT / 0.25f));   // (eased in from the stand)
            Vector3 g = Along(gs, out Vector3 dir);
            g.y = Slope.DrawnHeight(g.x, g.z);
            var right = Vector3.Cross(Vector3.up, dir);
            float ph = Mathf.Repeat(glideT / Stride, 1f);
            if (!skis)
            {
                // the back foot: the one nearer the board's tail (the board's nose goes first)
                int back = Vector3.Dot(h.Foot[0].position - h.Foot[1].position, dir) < 0f ? 0 : 1;
                Vector3 front = h.Foot[1 - back].position;
                // (the toe edge's side: the board's own right, as the rig has it)
                var toe = body.RollFrame != null ? body.RollFrame.right : right; toe.y = 0f; toe = toe.sqrMagnitude > 1e-4f ? toe.normalized : right;
                // on the snow for the push (0..0.6), then up and forward
                float along = ph < 0.6f ? Mathf.Lerp(0.1f, -0.55f, ph / 0.6f) : Mathf.Lerp(-0.55f, 0.1f, (ph - 0.6f) / 0.4f);
                float up = ph < 0.6f ? 0f : 0.12f * Mathf.Sin((ph - 0.6f) / 0.4f * Mathf.PI);
                var ankle = front + dir * (along - 0.15f) + toe * 0.3f;
                ankle.y = Slope.DrawnHeight(ankle.x, ankle.z) + 0.09f + up;
                h.LegTo(back, Vector3.Lerp(h.Foot[back].position, ankle, k), h.Hips.position + toe * 0.55f + dir * (along * 0.4f) + Vector3.down * 0.25f);
            }
            else
            {
                // both poles at once: planted ahead (0..0.55, pushed back past the hips), then swung forward again
                float push = ph < 0.55f ? ph / 0.55f : 1f - (ph - 0.55f) / 0.45f;
                for (int s = 0; s < 2; s++)
                {
                    float sd = s == 0 ? -1f : 1f;
                    var hand = g + Vector3.up * (1.0f - 0.18f * push) + dir * (0.42f - 0.7f * push) + right * sd * 0.3f;
                    // (the pole goes with the hand; planted, its tip on the snow swings it back as the hand goes by: RiderBodyTouch)
                    h.ArmTo(s, Vector3.Lerp(h.Hand[s].position, hand, k), hand + right * sd * 0.35f - dir * 0.3f + Vector3.down * 0.15f);
                }
            }
        }

        // ---------------------------------------------------------------- the camera

        void Camera(float dt, bool snap)
        {
            float k(float rate) => snap || dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * rate);
            Vector3 want, look;
            Vector3 hero = rider.transform.position - Vector3.up * Rider.R;
            // 1. the hero's own eyes, seated: knees, the gear on the footrest and the bar, the line ahead, the station
            // and the camp coming up (looking a little to the camp's side as it nears)
            var seatP = Now == Phase.Ride || Now == Phase.Stand ? (Vector3)(chairM * LiftRiders.SeatLocal(seat)).GetColumn(3) : hero;
            var cf = chairM.MultiplyVector(Vector3.forward); cf.y = 0f; cf = cf.sqrMagnitude > 1e-6f ? cf.normalized : -l.dir;
            float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, rideFor - StandTime, T));
            float sway = Mathf.Sin(T * 0.9f) * 0.02f;
            Vector3 povWant = seatP + Vector3.up * (0.95f + sway) + cf * 0.24f;
            Vector3 povLook = seatP + cf * 14f + campSide * (near * 4f) + Vector3.up * Mathf.Lerp(-14f, -2.4f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, near)));
            // 2. pulled back, up over the station, to a place out on the bench beside it, looking at them as they stand
            // up and glide out from under it
            // (uphill of the station's hut, which stands at its downhill end on that side: it would hide them)
            Vector3 vantage = l.top + l.dir * -6.5f + outer * 14f + Vector3.up * 4.2f;
            vantage.y = Mathf.Max(vantage.y, Slope.DrawnHeight(vantage.x, vantage.z) + 2f);
            Vector3 vantageLook = hero + Vector3.up * 0.9f;
            // 3. behind them as the camp's own camera sits (Camp.CameraStep), facing the way they stop: the camp
            var fe = new Vector3(Mathf.Sin(EndYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(EndYaw * Mathf.Deg2Rad));
            Vector3 chaseWant = hero + Vector3.up * 1.25f - fe * 6.4f + Vector3.up * 2.7f;
            Vector3 chaseLook = hero + Vector3.up * 1.4f + fe * 4f;
            chaseWant.y = Mathf.Max(chaseWant.y, Slope.DrawnHeight(chaseWant.x, chaseWant.z) + 0.8f);
            float toBench = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rideFor - StandTime - 1.5f, rideFor - StandTime + 0.5f, T));
            float toChase = Now == Phase.Stop ? 1f : Now == Phase.Glide ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(glideLen * 0.5f, glideLen, gs)) : 0f;
            want = Vector3.Lerp(Vector3.Lerp(povWant, vantage, toBench), chaseWant, toChase) + Vector3.up * (Mathf.Sin(toBench * Mathf.PI) * 5f);
            look = Vector3.Lerp(Vector3.Lerp(povLook, vantageLook, toBench), chaseLook, toChase);
            if (toBench < 0.02f) { snap = true; }   // (the eyes are the hero's: no lag)
            camFace = EndYaw * Mathf.Deg2Rad;
            var cp = snap ? want : Vector3.SmoothDamp(cam.transform.position, want, ref camVel, 0.22f, 60f, Mathf.Max(dt, 1e-4f));
            if (snap) camVel = Vector3.zero;
            lookAhead = snap ? look : Vector3.Lerp(lookAhead, look, k(6f));
            cam.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(lookAhead - cp, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 62f, k(3f));
        }

        /// <summary>Playing now.</summary>
        public bool Active() => Now != Phase.None && Now != Phase.Done;

        /// <summary>For the tests and pictures: where the chair and the hero are.</summary>
        public Vector3 HeroAt => rider != null ? rider.transform.position - Vector3.up * Rider.R : Vector3.zero;
    }
}
