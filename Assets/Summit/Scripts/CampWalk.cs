using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Summit
{
    // Camp, part: walking the rider round it, the camp's people's paths, footsteps and footprints, the controls and the camera
    public partial class Camp
    {
        // ---------------------------------------------------------------- walking

        Rider rider; RiderBody body; Camera cam;
        Vector3 pos; float yaw, speed, camYaw, camPitch, lookBlend, showBlend, inBlend;
        // a jump or a roll under way: 0 none, 1 jump, 2 roll; how long it has been going; height off the ground
        int moveKind; float moveT, jumpY, jumpV, rollSpeed, nextFurrow; bool airborne;
        Vector3 rollDir;
        public const float JumpCrouch = 0.12f, JumpPush = 3.4f, JumpLength = 31f / 30f, RollLength = 0.8f;   // (the lengths of the hero's CampJump and CampRoll clips)
        /// <summary>The hero left the snow (a jump).</summary>
        public System.Action<Vector3> Jumped;
        /// <summary>Back down from a jump: where, going which way (m/s), how hard (m/s down).</summary>
        public System.Action<Vector3, Vector3, float> Landed;
        /// <summary>A roll started: where, which way.</summary>
        public System.Action<Vector3, Vector3> Rolled;
        /// <summary>The rolling body pressing into the snow: where, which way (m/s). A few times a roll.</summary>
        public System.Action<Vector3, Vector3> Furrowed;
        /// <summary>A jump or a roll under way (0 none, 1 jump, 2 roll), and how far into it (s).</summary>
        public int MoveKind => moveKind;
        public float MoveTime => moveT;
        bool snap;
        float lookRest = 99f;   // seconds since the camera was last turned by hand
        bool padMode;           // a controller's sticks are in use (two sticks: the camera stays where the right one leaves it)
        public Vector3 WalkerPosition => pos;
        /// <summary>The way the walker faces (degrees).</summary>
        public float WalkerYaw => yaw * Mathf.Rad2Deg;

        /// <summary>Put the rider in the camp on foot, by the lodge, and take over the camera.</summary>
        public void Enter(Rider r, RiderBody b, Camera c, bool fromGate = false)
        {
            rider = r; body = b; cam = c;
            Active = true;
            // (camplayout) arrive where the Summit lift unloads, beside the lodge's wing, facing out past it toward the
            // gate and Mt. Vertigo: the loop is lift, lodge, fire, gate. Back up from the start pad: at the gate, facing
            // the camp. With no Summit lift: on the lodge's terrace at its door, facing out.
            var path = LiftEnds.SummitPath();
            Vector3 face;
            if (fromGate) { pos = Ground(0f, Slope.GateZ - 5f); face = Ground(-3f, -72f); }
            // (a few metres along its path in, past the unload's boards, the camera clear of them behind; facing just left
            // of the gate, past the lodge's front corner)
            else if (path.Count > 1) { pos = path[Mathf.Min(6, path.Count - 1)]; face = Ground(-6f, Slope.GateZ); }
            else if (UseLodge && chalet != null) { pos = ChaletPoint(4.5f, 0f, 9.6f); face = ChaletPoint(4.5f, 0f, 40f); }
            else { pos = Ground(-1.5f, -64f); face = lodgeApex; }
            var toFace = face - pos;
            yaw = Mathf.Atan2(toFace.x, toFace.z); camYaw = yaw; speed = 0f;
            snap = true;
            Near = Station.None;
            body.SetWalking(true);
            ClearFootprints();   // a fresh visit: fresh snow
            falling = false; fadeIn = false; fade = 0f; fadeUi?.SetFade(0f);
            CampShadows(true);
            // a finger already down (the tap on TO CAMP or < CAMP that brought you here) is not a walk or a tap here:
            // ignore it until it lifts
            input.IgnoreCurrent(Touches());
            EdgeGestures(true);
            moveKind = 0; moveT = 0f; jumpY = 0f; camPitch = 0f; lookRest = 99f;
            body.CampPose = this;   // sitting at the fire, a mug in hand (CampFire.cs)
            if (alyssa != null) alyssa.enabled = true;   // (still while you're out on the mountain: nothing to pose)
            Place();
            lastStep = body.WalkPhase;
            Redress();
        }

        /// <summary>
        /// (lifttop) Into the camp on foot at a given place, facing a given way (degrees): where the hero's glide off the
        /// Summit lift stopped (LiftArrive). Soft: the camera eases on from where it is, as it already sits behind them.
        /// </summary>
        public void EnterAt(Rider r, RiderBody b, Camera c, Vector3 at, float yawDeg, bool soft)
        {
            Enter(r, b, c);
            pos = new Vector3(at.x, Slope.DrawnHeight(at.x, at.z), at.z);
            yaw = yawDeg * Mathf.Deg2Rad; camYaw = yaw; speed = 0f;
            snap = !soft;
            Place();
            lastStep = body.WalkPhase;
        }

        public void Leave()
        {
            if (!Active) return;
            Active = false;
            Showcase = false;
            EndActs(fadeUi);
            if (alyssa != null) alyssa.enabled = false;
            if (body != null) body.CampPose = null;
            CampShadows(false);
            if (stepPool != null) { foreach (var src in stepPool) src.Stop(); stepVol = 0f; }
            if (body != null) body.SetWalking(false);
            EdgeGestures(false);
            fadeUi?.SetTapCue(false);
        }

#if UNITY_IOS && !UNITY_EDITOR
        UnityEngine.iOS.SystemGestureDeferMode gesturesWere;
        bool gesturesHeld;
#endif
        /// <summary>
        /// In camp the walk stick sits down in the bottom corner, by the bottom edge: there iOS holds the
        /// touch back for its own home swipe and may take it away mid-walk. So in camp the edges are the game's (a
        /// swipe in from an edge goes to the game; a second one still goes home), and as they were when you leave.
        /// </summary>
        void EdgeGestures(bool camp)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (camp == gesturesHeld) return;
            gesturesHeld = camp;
            if (camp) { gesturesWere = UnityEngine.iOS.Device.deferSystemGesturesMode; UnityEngine.iOS.Device.deferSystemGesturesMode = UnityEngine.iOS.SystemGestureDeferMode.All; }
            else UnityEngine.iOS.Device.deferSystemGesturesMode = gesturesWere;
#endif
        }

        /// <summary>A jump: a crouch, the push off, the air, and a landing that soaks it up. Not mid-jump or mid-roll.</summary>
        public void Jump()
        {
            if (!Active || moveKind != 0 || falling) return;
            moveKind = 1; moveT = 0f; jumpY = 0f; jumpV = 0f; airborne = false;
        }

        /// <summary>A forward roll the way of a swipe on the screen (up = away from the camera); you come up running.</summary>
        public void Roll(Vector2 screenDir)
        {
            if (!Active || moveKind != 0 || falling || screenDir.sqrMagnitude < 1e-4f) return;
            float head = camYaw + Mathf.Atan2(screenDir.x, screenDir.y);
            yaw = head;   // turn straight into it
            rollDir = new Vector3(Mathf.Sin(head), 0f, Mathf.Cos(head));
            rollSpeed = Mathf.Max(speed, 4.6f);
            moveKind = 2; moveT = 0f; nextFurrow = 0.14f;
            Rolled?.Invoke(pos, rollDir);
        }

        /// <summary>The jump or roll under way, each frame: true while it has the walker (no steering, no footsteps).</summary>
        bool MoveStep(float dt)
        {
            if (moveKind == 0) return false;
            moveT += dt;
            if (moveKind == 1)
            {
                // the crouch, then off the snow; in the air you carry on the way you were going
                if (!airborne && jumpY <= 0f && moveT >= JumpCrouch && moveT < JumpCrouch + 0.1f)
                { airborne = true; jumpV = JumpPush; Jumped?.Invoke(pos); }
                if (airborne)
                {
                    jumpV -= 9.81f * dt; jumpY += jumpV * dt;
                    if (jumpY <= 0f)
                    {
                        float hard = -jumpV;
                        jumpY = 0f; airborne = false;
                        var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                        Landed?.Invoke(pos, f * speed, hard);
                        if (onSnow)
                        {
                            // two boots pressed in deep, side by side
                            var side = new Vector3(-f.z, 0f, f.x);
                            AddPrint(pos + side * 0.12f + f * 0.04f, f, true, 1f);
                            AddPrint(pos - side * 0.12f - f * 0.03f, f, false, 1f);
                        }
                    }
                }
                if (moveT >= JumpLength && !airborne) { moveKind = 0; return false; }
                return airborne;   // on the ground (the crouch and the landing) you can still steer
            }
            // the roll: tucked and over, carried along, slowing a little; you come up running
            float k = moveT / RollLength;
            speed = Mathf.Lerp(rollSpeed, Mathf.Max(rollSpeed * 0.8f, MaxSpeed), k);
            if (onSnow && moveT >= nextFurrow && moveT < RollLength * 0.85f) { nextFurrow += 0.17f; Furrowed?.Invoke(pos, rollDir * speed); }
            if (moveT >= RollLength) { moveKind = 0; speed = Mathf.Max(speed, MaxSpeed * 1.05f); return false; }
            return true;
        }

        /// <summary>Walk with a stick (x right, y away from the camera; length 0..1). True once you've walked through the gate.</summary>
        public bool Walk(Vector2 stick, float dt)
        {
            if (!Active) return false;
            bool held = MoveStep(dt);
            float mag = held ? 0f : Mathf.Clamp01(stick.magnitude);
            if (mag > 0.08f)
            {
                // the stick is relative to the camera: up the screen is the way it looks
                float head = camYaw + Mathf.Atan2(stick.x, stick.y);
                yaw = Mathf.LerpAngle(yaw * Mathf.Rad2Deg, head * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 10f)) * Mathf.Deg2Rad;
            }
            // pushed right out, the walk breaks into a run (and eases back down); on the stairs, a brisk walk, and a
            // run no more than a quicker one (CampStairs)
            float want = mag > 0.92f ? RunSpeed : mag * MaxSpeed;
            onStairs = OnStairs(pos, 0.1f);
            if (onStairs) { stairRun = mag > 0.92f; want = Mathf.Min(want, stairRun ? StairRunSpeed : StairWalkSpeed); }
            if (!held) speed = Mathf.MoveTowards(speed, want, dt * (want > speed ? 9f : 14f));
            var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 next = pos + f * speed * dt;
            // (no edge to the camp: you can walk anywhere; only real things stop you, and the mountain's edges
            // give way under you, see Collapse)
            next = OffTrees(next);
            var p2 = new Vector2(next.x, next.z);
            float feet = pos.y;
            for (int it = 0; it < 2; it++)
                foreach (var b in boxes)
                {
                    if (b.floor || (b.owner != null && !b.owner.activeInHierarchy)) continue;
                    if (b.lo > feet + 1.7f || b.hi < feet + 0.1f) continue;   // above your head or under your feet
                    p2 = PushOut(b, p2, 0.38f);
                }
            if (Couple != null) p2 = Couple.PushOut(p2, 0.38f);   // (and round Val and Breanna, if they're about)
            if (PushWalker != null) p2 = PushWalker(p2);   // (the camp's people: you don't walk through them)
            p2 = OffPiles(p2, feet);   // (nor into a pile of snow too steep to walk up: CampSnow.cs)
            pos = new Vector3(p2.x, feet, p2.y);
            Place();
            // the nearest station you're standing at
            Near = Station.None; OnLookout = false;
            float best = float.MaxValue;
            foreach (var s in spots)
            {
                if (s.root != null && !s.root.gameObject.activeInHierarchy) continue;   // (not built yet: the fire before it's bought)
                var d = new Vector2(s.at.x - pos.x, s.at.z - pos.z).magnitude;
                if (d < s.radius && d < best && Mathf.Abs(s.at.y - pos.y) < 1.5f) { best = d; Near = s.st; }   // on its floor
            }
            onCampLookout = Near == Station.Lookout;
            if (Near == Station.Lookout) { OnLookout = true; Near = Station.None; }
            if (Near == Station.None && UseLodge && LodgeLookoutAt(pos)) OnLookout = true;   // (the lodge's deck on the wing's roof: CampLodge)
            // feet on the snow: footsteps and prints (a jump or a roll makes its own marks)
            if (moveKind == 0) { Footsteps(dt); Footprints(dt); }
            else lastStep = body.WalkPhase;
            if (onSnow) EdgeCheck(f);
            body.CampMove = moveKind; body.CampMoveT = moveT;
            return false;
        }

        // ---- the camp's own people (CampCouple): where things are, and walking round what's in the way as the hero does

        /// <summary>Val and Breanna (made in Build).</summary>
        public CampCouple Couple { get; private set; }
        /// <summary>A point on the snow at (dx from the run's centre, z).</summary>
        public static Vector3 GroundAt(float dx, float z) => Ground(dx, z);
        /// <summary>
        /// A step from one place toward another for someone of radius r: round the trees and walls, onto the floors,
        /// decks and steps, as the walker goes (the walker's own state is left as it was).
        /// </summary>
        public Vector3 StepTo(Vector3 from, Vector3 to, float r)
        {
            bool keep = onSnow;
            var p2 = new Vector2(to.x, to.z);
            var t = OffTrees(new Vector3(to.x, from.y, to.z)); p2 = new Vector2(t.x, t.z);
            for (int it = 0; it < 2; it++)
                foreach (var b in boxes)
                {
                    if (b.floor || (b.owner != null && !b.owner.activeInHierarchy)) continue;
                    if (b.lo > from.y + 1.7f || b.hi < from.y + 0.1f) continue;
                    p2 = PushOut(b, p2, r);
                }
            var q = new Vector3(p2.x, from.y, p2.y);
            q.y = FloorAt(q, from.y);
            onSnow = keep;
            return q;
        }

        // ---------------------------------------------------------------- for the camp's people (CampLife.cs)

        /// <summary>The camp was built (or rebuilt for a new mountain): its root, which anything added to the camp goes under.</summary>
        public System.Action<Transform> Built;
        /// <summary>Moves the walker out of anyone standing in the way (set by CampLife).</summary>
        public System.Func<Vector2, Vector2> PushWalker;
        /// <summary>How fast the walker is going (m/s).</summary>
        public float WalkerSpeed => speed;

        /// <summary>A point pushed out of everything solid in the camp at a body's height, for a body of radius r.</summary>
        public Vector2 PushOutOfThings(Vector2 p, float r, float feet)
        {
            for (int it = 0; it < 2; it++)
                foreach (var b in boxes)
                {
                    if (b.floor || (b.owner != null && !b.owner.activeInHierarchy)) continue;
                    if (b.lo > feet + 1.7f || b.hi < feet + 0.1f) continue;
                    p = PushOut(b, p, r);
                }
            return p;
        }

        /// <summary>What someone stands on at p coming from height feet (the snow, a deck, a step).</summary>
        public float FloorHeight(Vector3 p, float feet)
        {
            bool was = onSnow;
            float y = FloorAt(p, feet);
            onSnow = was;
            return y;
        }

        /// <summary>Something solid added to the camp (a bench, a stand): a box on the snow, half sizes, turned by yaw.</summary>
        public void AddBlock(Vector3 at, Vector2 half, float yawDeg)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            boxes.Add(new Box { c = new Vector2(at.x, at.z), h = half, cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw), top = at.y, lo = at.y - 1000f, hi = at.y + 1000f });
        }

        static Vector2 PushOut(Box b, Vector2 p, float r)
        {
            var d = p - b.c;
            // into the box's own frame
            var q = new Vector2(d.x * b.cos - d.y * b.sin, d.x * b.sin + d.y * b.cos);
            var cl = new Vector2(Mathf.Clamp(q.x, -b.h.x, b.h.x), Mathf.Clamp(q.y, -b.h.y, b.h.y));
            var off = q - cl;
            if (off.sqrMagnitude > r * r) return p;
            if (off.sqrMagnitude < 1e-6f)
            {
                // inside: out the nearest side
                float ex = b.h.x - Mathf.Abs(q.x), ez = b.h.y - Mathf.Abs(q.y);
                if (ex < ez) q.x = Mathf.Sign(q.x) * (b.h.x + r); else q.y = Mathf.Sign(q.y) * (b.h.y + r);
            }
            else q = cl + off.normalized * r;
            return b.c + new Vector2(q.x * b.cos + q.y * b.sin, -q.x * b.sin + q.y * b.cos);
        }

        /// <summary>
        /// What you stand on at p, coming from height feet: the highest floor no more than a step above you
        /// (so the mezzanine overhead doesn't lift you up, but stairs and the terrace do, a step at a time).
        /// </summary>
        float FloorAt(Vector3 p, float feet)
        {
            float y = Slope.DrawnHeight(p.x, p.z);
            onSnow = true;
            foreach (var b in boxes)
            {
                if (!b.floor) continue;
                var d = new Vector2(p.x, p.z) - b.c;
                var q = new Vector2(d.x * b.cos - d.y * b.sin, d.x * b.sin + d.y * b.cos);
                if (Mathf.Abs(q.x) > b.h.x || Mathf.Abs(q.y) > b.h.y) continue;
                // a ramp (stairs) falls by drop from its -z end to its +z end; or its own height along it (CampStairs)
                float top = b.prof != null ? b.top + Prof(b, q.y + b.h.y) : b.top - b.drop * (q.y + b.h.y) / (2f * b.h.y);
                if (top <= feet + 0.55f && top > y) { y = top; onSnow = false; }   // on a floor, a deck or steps
            }
            float pile = PileTop(p.x, p.z);   // (up on a pile of snow: CampSnow.cs)
            if (pile > y && pile <= feet + 0.55f) { y = pile; onSnow = true; }
            return y;
        }

        static float Prof(in Box b, float s)
        {
            float f = Mathf.Clamp(s / b.profStep, 0f, b.prof.Length - 1.001f);
            int i = (int)f;
            return Mathf.Lerp(b.prof[i], b.prof[i + 1], f - i);
        }

        void Place()
        {
            pos.y = FloorAt(pos, pos.y);
            var p = pos + Vector3.up * (Rider.R + jumpY);
            rider.Body.position = p;
            rider.transform.position = p;
            // the model faces the board's toe edge (+X): turn it a quarter so it faces where it walks
            rider.Heading = yaw - Mathf.PI * 0.5f;
            body.WalkPace = Mathf.Clamp01(speed / MaxSpeed);
            body.Cadence = Cadence;
            body.RunBlend = Mathf.Clamp01((speed - MaxSpeed) / (RunSpeed - MaxSpeed));
            body.FeetGround = pos.y; body.FeetFacing = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            body.Upright = OnStairs(pos, 0.3f);
            body.PhaseLock = float.NaN;
            if (onStairs && speed > 0.3f)
            {
                // a foot on every second tread (every one, slowly), the walk blended in as far as makes that its
                // stride: each foot coming down wholly on a tread. (A run up them is the same stride, quicker: the run
                // clip's two feet don't come down evenly spaced, so a run's every third tread had one foot on a nosing.)
                int per = speed < 1.3f ? 1 : 2;
                float step = per * StairGoing;
                body.WalkPace = PeopleLod.BlendForStep(step);
                body.RunBlend = 0f;
                body.Cadence = speed / (2f * step);
                float lead = StairLeadWalk * Mathf.Min(1f, step / (0.5f * PeopleLod.WalkCycle));
                body.PhaseLock = StairPhase(pos, new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)), per, lead);
            }
        }

        /// <summary>The station under a tap on the screen, if any.</summary>
        public Station StationAt(Vector2 screen)
        {
            if (cam == null) return Station.None;
            var ray = cam.ScreenPointToRay(screen);
            int nh = Physics.RaycastNonAlloc(ray, rayHits, 80f, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < nh; i++)
                foreach (var s in spots)
                    if (rayHits[i].collider.transform == s.root) return s.st;
            return Station.None;
        }

        // ---------------------------------------------------------------- footsteps

        // One footstep each time a foot lands, on the beat of the feet at any pace. The player's own recording of
        // boots crunching through snow (Tools/Source/user_snow_steps.mp3) is cut into single steps by Tools/CutSteps.py
        // (Resources/Audio/Steps: each clip's crunch starts 4 ms in). The walk cycle says when each heel strikes
        // (the left at pi/2, the right at 3pi/2, as the footprints and the hero's walk and run clips have it), so each
        // step is scheduled for the exact moment the cycle will reach the next strike, never a frame late. Running,
        // they come harder and a touch higher; indoors on the chalet's floor, softer and duller.
        AudioSource[] stepPool;
        AudioLowPassFilter[] stepTones;
        AudioClip[] stepClips;
        int stepNext, stepClipLast = -1, stepPlanned = int.MinValue;
        float stepIn, stepPhLast, stepRate, stepVol;
        /// <summary>Test tools: a step sound was planned: seconds from now until it sounds, which foot, which strike.</summary>
        public System.Action<float, bool, int> StepPlanned;

        void Footsteps(float dt)
        {
            if (stepPool == null)
            {
                stepClips = Resources.LoadAll<AudioClip>("Audio/Steps");
                System.Array.Sort(stepClips, (a, b) => string.CompareOrdinal(a.name, b.name));
                if (stepClips.Length == 0) return;
                foreach (var clip in stepClips) clip.LoadAudioData();   // loaded now, so no step is late waiting for it
                var g = new GameObject("Footsteps");
                g.transform.SetParent(transform, false);
                // a few voices, so a step's tail rings on under the next one
                stepPool = new AudioSource[4]; stepTones = new AudioLowPassFilter[4];
                for (int i = 0; i < stepPool.Length; i++)
                {
                    var v = new GameObject("Step" + i); v.transform.SetParent(g.transform, false);
                    var voice = v.AddComponent<AudioSource>();
                    voice.playOnAwake = false; voice.loop = false; voice.spatialBlend = 0f;   // your own feet: in both ears
                    stepPool[i] = voice;
                    stepTones[i] = v.AddComponent<AudioLowPassFilter>(); stepTones[i].cutoffFrequency = 22000f;
                }
            }
            float run = Mathf.Clamp01((speed - MaxSpeed) / (RunSpeed - MaxSpeed));
            stepIn = Mathf.MoveTowards(stepIn, Indoors ? 1f : 0f, dt * 4f);
            RiderBody.Shelter = stepIn;   // (and no wind in the clothes indoors)
            for (int i = 0; i < stepTones.Length; i++) stepTones[i].cutoffFrequency = Mathf.Lerp(22000f, 1400f, stepIn);
            // how fast the cycle turns (it moves on in the body's own frame update, after this one)
            float ph = body.WalkPhase;
            if (dt > 0f)
            {
                float r = (ph - stepPhLast) / dt;
                stepRate = r >= 0f && r < 40f ? Mathf.Lerp(stepRate, r, 0.5f) : 0f;   // a reset cycle jumps back
            }
            stepPhLast = ph;
            bool walking = speed > 0.3f && body.WalkPace > 0.12f && stepRate > 0.5f;
            stepVol = walking ? Mathf.Lerp(0.72f, 0.95f, run) * Mathf.Lerp(1f, 0.45f, stepIn) * Mathf.Lerp(0.7f, 1f, body.WalkPace) : 0f;
            if (!walking) { stepPlanned = int.MinValue; return; }
            // the next strike: the phase will be ph + rate * dt when the body shows this frame
            float shown = ph + stepRate * dt;
            int k = Mathf.CeilToInt((shown - Mathf.PI * 0.5f) / Mathf.PI);
            if (k <= stepPlanned) k = stepPlanned + 1;
            float strike = Mathf.PI * 0.5f + k * Mathf.PI;
            float wait = (strike - shown) / stepRate;   // seconds after this frame is shown
            if (wait > dt + 0.06f) return;             // not yet: it gets planned a frame or so ahead
            stepPlanned = k;
            bool left = (k % 2 + 2) % 2 == 0;
            // never the same crunch twice running
            int c = Random.Range(0, stepClips.Length);
            if (c == stepClipLast) c = (c + 1) % stepClips.Length;
            stepClipLast = c;
            var src = stepPool[stepNext]; stepNext = (stepNext + 1) % stepPool.Length;
            src.clip = stepClips[c];
            src.volume = stepVol * Random.Range(0.88f, 1f) * (left ? 1f : 0.94f);
            src.pitch = Mathf.Lerp(0.98f, 1.08f, run) * Random.Range(0.96f, 1.04f);
            src.PlayScheduled(AudioSettings.dspTime + Mathf.Max(0f, wait));
            StepPlanned?.Invoke(Mathf.Max(0f, wait), left, k);
        }

        /// <summary>Test tools: how the footsteps are playing right now.</summary>
        public string FootstepsState => stepPool == null ? "no clips" : $"clips={stepClips.Length} vol={stepVol:0.00} rate={stepRate / (2f * Mathf.PI):0.00} cycles/s planned={stepPlanned} cutoff={stepTones[0].cutoffFrequency:0}";

        // ---------------------------------------------------------------- footprints

        // A boot print for every step, left and right in turn as each heel strikes (RiderRig's walk cycle), along
        // the way you're going. Running, the strides are longer and the prints deeper (darker). None on floors,
        // decks or steps. Up to 200 at once in one mesh, the oldest fading after a minute and a half; a new visit
        // starts on clean snow. (Not pressed into SnowLayer: in the camp its touched cells draw as lighter, faceted
        // patches that don't match the camp's snow, so a print was a pale polygon round a dent.)
        const int MaxPrints = 200;
        const float PrintLife = 90f, PrintFade = 30f;
        Mesh printMesh;
        Vector3[] printV;
        Color[] printC;
        float[] printBorn, printAlpha;
        int printNext;
        float lastStep, fadeTick, printClock;
        bool onSnow = true;

        void Footprints(float dt)
        {
            printClock += dt;
            if (printMesh == null) BuildPrints();
            // a heel strikes each time the cycle passes pi/2 (left) or 3pi/2 (right)
            float ph = body.WalkPhase;
            if (ph < lastStep - 0.01f) lastStep = ph;   // the cycle was reset
            if (speed > 0.25f && onSnow)
            {
                float k0 = Mathf.Floor((lastStep - Mathf.PI * 0.5f) / Mathf.PI), k1 = Mathf.Floor((ph - Mathf.PI * 0.5f) / Mathf.PI);
                if (k1 > k0) Step(Mathf.RoundToInt(k1) % 2 == 0);
            }
            lastStep = ph;
            // fade the old ones, a few times a second
            fadeTick -= dt;
            if (fadeTick <= 0f)
            {
                fadeTick = 0.25f;
                bool changed = false;
                for (int i = 0; i < MaxPrints; i++)
                {
                    if (printAlpha[i] <= 0f) continue;
                    float age = printClock - printBorn[i];
                    float a = printAlpha[i] * Mathf.Clamp01(1f - (age - PrintLife) / PrintFade);
                    if (age > PrintLife + PrintFade) { printAlpha[i] = 0f; a = 0f; }
                    var c = new Color(1f, 1f, 1f, a);
                    if (printC[i * 4] != c) { printC[i * 4] = printC[i * 4 + 1] = printC[i * 4 + 2] = printC[i * 4 + 3] = c; changed = true; }
                }
                if (changed) printMesh.colors = printC;
            }
        }

        /// <summary>Put a foot down: a print where it lands, in front of you and to its side.</summary>
        void Step(bool left)
        {
            float run = body.RunAmount, amp = Mathf.Clamp01(speed / MaxSpeed);
            var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var side = new Vector3(-f.z, 0f, f.x);   // to your left
            float stride = Mathf.Lerp(0.36f, 0.62f, run) * amp;
            Vector3 at = pos + f * stride + side * (left ? 0.11f : -0.11f);
            AddPrint(at, f, left, Mathf.Lerp(0.7f, 0.95f, run));
        }

        /// <summary>
        /// Walk cycles a second (two steps each) for the speed you're going: about a metre a step walking, nearly two
        /// running, so the feet keep up with the ground and the prints fall a stride apart.
        /// </summary>
        float Cadence => speed / (2f * Mathf.Lerp(1.0f, 1.8f, Mathf.Clamp01((speed - MaxSpeed) / (RunSpeed - MaxSpeed))));

        void AddPrint(Vector3 at, Vector3 f, bool left, float alpha)
        {
            int i = printNext;
            printNext = (printNext + 1) % MaxPrints;
            var r = new Vector3(f.z, 0f, -f.x);   // to the right
            const float hw = 0.07f, hl = 0.15f;
            Vector3 P(float u, float v) { var p = at + r * u + f * v; p.y = Slope.DrawnHeight(p.x, p.z) + 0.012f; return p; }
            printV[i * 4] = P(-hw, -hl); printV[i * 4 + 1] = P(hw, -hl); printV[i * 4 + 2] = P(hw, hl); printV[i * 4 + 3] = P(-hw, hl);
            // the texture is a left boot: a right one is it mirrored
            float u0 = left ? 0f : 1f, u1 = left ? 1f : 0f;
            var uv = printMesh.uv;
            uv[i * 4] = new Vector2(u0, 0f); uv[i * 4 + 1] = new Vector2(u1, 0f); uv[i * 4 + 2] = new Vector2(u1, 1f); uv[i * 4 + 3] = new Vector2(u0, 1f);
            printBorn[i] = printClock; printAlpha[i] = alpha;
            var c = new Color(1f, 1f, 1f, alpha);
            printC[i * 4] = printC[i * 4 + 1] = printC[i * 4 + 2] = printC[i * 4 + 3] = c;
            printMesh.vertices = printV; printMesh.uv = uv; printMesh.colors = printC;
            printMesh.RecalculateBounds();
        }

        void BuildPrints()
        {
            printV = new Vector3[MaxPrints * 4];
            printC = new Color[MaxPrints * 4];
            printBorn = new float[MaxPrints]; printAlpha = new float[MaxPrints];
            var tri = new int[MaxPrints * 6];
            for (int i = 0; i < MaxPrints; i++)
            {
                int v = i * 4;
                tri[i * 6] = v; tri[i * 6 + 1] = v + 2; tri[i * 6 + 2] = v + 1;
                tri[i * 6 + 3] = v; tri[i * 6 + 4] = v + 3; tri[i * 6 + 5] = v + 2;
            }
            printMesh = new Mesh { name = "Footprints" };
            printMesh.MarkDynamic();
            printMesh.vertices = printV; printMesh.uv = new Vector2[MaxPrints * 4]; printMesh.colors = printC;
            printMesh.triangles = tri;
            var go = new GameObject("Footprints", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = printMesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Imprint(Resources.Load<Texture2D>("Camp/Boot"));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void ClearFootprints()
        {
            if (printMesh != null)
            {
                System.Array.Clear(printV, 0, printV.Length);
                System.Array.Clear(printC, 0, printC.Length);
                System.Array.Clear(printAlpha, 0, printAlpha.Length);
                printMesh.vertices = printV; printMesh.colors = printC;
                printMesh.RecalculateBounds();
            }
            printNext = 0;
        }

        /// <summary>Test tools: how many prints are on the snow.</summary>
        public int FootprintCount { get { int n = 0; if (printAlpha != null) foreach (var a in printAlpha) if (a > 0f) n++; return n; } }

        // ---------------------------------------------------------------- the controls

        readonly CampInput input = new CampInput();
        readonly List<CampInput.Touch> touchList = new List<CampInput.Touch>();
        /// <summary>Test tools: touches to use instead of the screen's (null: the real ones).</summary>
        public static List<CampInput.Touch> TestTouches;
        /// <summary>Test tools: the camp's touch controls.</summary>
        public CampInput Controls => input;

        /// <summary>
        /// One frame in camp. One thumb on the stick in the bottom left corner walks, and the camera follows round
        /// behind; a finger held down anywhere else turns the camera by hand; a quick tap jumps (or, standing on a station, opens it), a flick rolls; the
        /// menus take their own taps. A plugged-in controller has its two sticks: left walks, right turns the camera.
        /// True when it's time to drop in.
        /// </summary>
        public bool Step(float dt, MetaUi ui)
        {
            fadeUi = ui;
            if (fadeIn) { fade = Mathf.MoveTowards(fade, 0f, dt / 0.6f); ui.SetFade(fade); fadeIn = fade > 0f; }
            ui.SetTapCue(falling && !fallTapped);
            if (falling) { FallStep(dt); ui.SetStick(false, Vector2.zero, Vector2.zero); ui.SetLookStick(false, Vector2.zero, Vector2.zero); ui.SetContext(Station.None); return false; }
            var touches = Touches();
            if (ui.ClosetOpen)
            {
                // the closet takes every finger (its list scrolls, its hero turns); nothing walks or looks meanwhile
                ui.ClosetStep(touches, dt);
                ui.SetStick(false, Vector2.zero, Vector2.zero); ui.SetLookStick(false, Vector2.zero, Vector2.zero);
                Showcase = false;
                return false;
            }
            float sw = cam != null ? cam.pixelWidth : Screen.width, sh = cam != null ? cam.pixelHeight : Screen.height;
            input.Home = ui.StickHome(CampInput.RadiusFor(sw, sh));   // the walk stick: fixed, bottom left
            input.Step(touches, dt, sw, sh, ui.Hit, ui.PanelOpen);
            if (input.Button != null) ui.Act(input.Button);
            if (ActStep(dt, ui, touches)) return false;   // talking, roasting, at the stove (CampFire.cs)
            if (input.ContextTapped && Open(Near, ui)) return true;
            if (input.Tapped && !ui.PanelOpen)
            {
                // standing still, a tap on a station opens it (the gate drops you in); otherwise it's a jump
                var st = speed < 0.5f ? StationAt(input.TapAt) : Station.None;
                if (st != Station.None) { if (Open(st, ui)) return true; }
                else if (!Sip()) Jump();   // (a mug in hand: a tap is a sip, CampCocoa)
            }
            if (input.Swiped && !ui.PanelOpen && !HoldingMug) Roll(input.SwipeDir);
            if (KeyJump() && !Sip()) Jump();
            if (KeyRoll() && !HoldingMug) Roll(Vector2.up);
            // a controller: its sticks take over until the screen is touched again
            bool padUsed = Pad(out Vector2 padMove, out Vector2 padLook, out bool padJump, out bool padRoll);
            if (padUsed) padMode = true; else if (touches.Count > 0) padMode = false;
            if (ui.PanelOpen) { padMove = padLook = Vector2.zero; }
            else
            {
                // the bottom button: standing at a station it opens it (the gate drops you in), otherwise a jump
                if (padJump) { if (speed < 0.5f && Near != Station.None) { if (Open(Near, ui)) return true; } else if (!Sip()) Jump(); }
                if (padRoll && !HoldingMug) Roll(Vector2.up);
            }
            Look(input.Look + padLook, dt);
            lookRest = input.Look.sqrMagnitude > 0.0025f ? 0f : lookRest + dt;
            // (always there, at rest with the knob in the middle until a thumb takes it)
            Vector2 home = input.Home ?? input.MoveOrigin;
            ui.SetStick(!ui.PanelOpen, input.MoveShown ? input.MoveOrigin : home, input.MoveShown ? input.MovePos : home, input.Radius, input.MoveShown);
            ui.SetLookStick(input.LookShown && !ui.PanelOpen, input.LookOrigin, input.LookPos, input.Radius);
            var stick = ui.PanelOpen ? Vector2.zero : Vector2.ClampMagnitude(input.Move + padMove + Keys(), 1f);
            if (HoldingMug) stick = Vector2.ClampMagnitude(stick, 0.9f);   // a full mug: you walk, you don't run
            Showcase = ui.PanelOpen && ui.ShopTab == 1;
            if (Walk(stick, dt)) return true;
            ui.SetContext(Near);
            return false;
        }

        /// <summary>
        /// Turn the camera round the hero with the look stick: sideways swings it round, up and down tilts it a
        /// little, within limits.
        /// </summary>
        public void Look(Vector2 look, float dt)
        {
            if (look.sqrMagnitude < 0.0025f) return;
            camYaw += look.x * 2.6f * dt;
            camPitch = Mathf.Clamp(camPitch - look.y * 40f * dt, -12f, 22f);
        }

        /// <summary>Every finger on the screen (the mouse is one finger, for the editor).</summary>
        List<CampInput.Touch> Touches()
        {
            if (TestTouches != null) return TestTouches;
            touchList.Clear();
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                    if (t.press.isPressed) touchList.Add(new CampInput.Touch(t.touchId.ReadValue(), t.position.ReadValue()));
            var ms = Mouse.current;
            if (touchList.Count == 0 && ms != null && ms.leftButton.isPressed) touchList.Add(new CampInput.Touch(-1, ms.position.ReadValue()));
#else
            for (int i = 0; i < UnityEngine.Input.touchCount; i++) { var t = UnityEngine.Input.GetTouch(i); touchList.Add(new CampInput.Touch(t.fingerId, t.position)); }
            if (touchList.Count == 0 && UnityEngine.Input.GetMouseButton(0)) touchList.Add(new CampInput.Touch(-1, UnityEngine.Input.mousePosition));
#endif
            return touchList;
        }

        /// <summary>
        /// A plugged-in controller, twin sticks: the left one walks, the right one turns the camera, the bottom face
        /// button jumps and the right one rolls. True while any of it is in use.
        /// </summary>
        static bool Pad(out Vector2 move, out Vector2 look, out bool jump, out bool roll)
        {
            move = look = Vector2.zero; jump = roll = false;
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            if (g == null) return false;
            Vector2 Dead(Vector2 v) { float m = v.magnitude; return m < 0.15f ? Vector2.zero : v / m * Mathf.Clamp01((m - 0.15f) / 0.8f); }
            move = Dead(g.leftStick.ReadValue()); look = Dead(g.rightStick.ReadValue());
            jump = g.buttonSouth.wasPressedThisFrame; roll = g.buttonEast.wasPressedThisFrame;
            return move != Vector2.zero || look != Vector2.zero || jump || roll;
#else
            return false;
#endif
        }

        static bool KeyJump()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k.spaceKey.wasPressedThisFrame;
#else
            return UnityEngine.Input.GetKeyDown(KeyCode.Space);
#endif
        }

        static bool KeyRoll()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k.rKey.wasPressedThisFrame;
#else
            return UnityEngine.Input.GetKeyDown(KeyCode.R);
#endif
        }

        bool Open(Station st, MetaUi ui)
        {
            if (OpenFireStation(st, ui)) return false;
            if (st == Station.DropIn) LeaveMug();   // (it waits on the gate's post)
            if (st == Station.Missions) ui.ShowMissions();
            else if (st == Station.Upgrades) ui.ShowShop(0);
            else if (st == Station.Style) ui.ShowShop(1);
            return st == Station.DropIn;
        }

        /// <summary>WASD / arrow keys, for the editor.</summary>
        static Vector2 Keys()
        {
            var v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return v;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1f;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1f;
            if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1f;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1f;
            v = Vector2.ClampMagnitude(v, 1f) * (k.shiftKey.isPressed ? 1f : 0.85f);   // shift to run
#else
            v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
            return Vector2.ClampMagnitude(v, 1f);
        }

        // ---------------------------------------------------------------- camera

        void LateUpdate()
        {
            if (Active) CameraStep(Time.deltaTime);
            WaveFlags(ChaseCamera.TestDt > 0f ? ChaseCamera.TestDt : Time.deltaTime);
        }

        /// <summary>Move the camera (public for the test tools, which have no frame time).</summary>
        public void CameraStep(float dt)
        {
            if (cam == null) return;
            if (ChaseCamera.TestDt > 0f) dt = ChaseCamera.TestDt;
            if (falling) { FallCamera(dt); return; }
            if (ClosetStage.Showing != null) { ClosetStage.Showing.Frame(cam); snap = true; return; }   // the closet's studio has the camera
            float k(float rate) => snap ? 1f : 1f - Mathf.Exp(-dt * rate);
            // one thumb walks and turns: the camera swings round behind you as you go, as it first did. Turned by
            // hand (a second finger held), it stays where you left it for a moment, then eases back to following.
            // With a controller's two sticks it stays where the right stick leaves it
            float follow = padMode ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.2f, 2.6f, lookRest));
            if (speed > 0.4f && follow > 0f)
                camYaw = Mathf.LerpAngle(camYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, k(1.4f) * Mathf.Clamp01(speed / MaxSpeed) * follow) * Mathf.Deg2Rad;
            lookBlend = Mathf.Lerp(lookBlend, OnLookout && speed < 0.5f ? 1f : 0f, k(2f));
            showBlend = Mathf.Lerp(showBlend, Showcase ? 1f : 0f, k(4f));
            var f = new Vector3(Mathf.Sin(camYaw), 0f, Mathf.Cos(camYaw));
            // indoors the camera comes in close and low, under the ceilings
            inBlend = Mathf.Lerp(inBlend, Indoors ? 1f : 0f, k(3f));
            Vector3 target = pos + Vector3.up * (Mathf.Lerp(1.25f, 1.35f, inBlend) + jumpY * 0.6f);
            Vector3 want = target - f * Mathf.Lerp(6.4f, 4.6f, inBlend) + Vector3.up * Mathf.Lerp(2.7f, 1.3f, inBlend);
            // the look stick's tilt: higher and looking down, or lower and level, round the hero
            var tiltAxis = new Vector3(f.z, 0f, -f.x);
            want = target + Quaternion.AngleAxis(camPitch, tiltAxis) * (want - target);
            Vector3 look = target + f * Mathf.Lerp(4f, 3f, inBlend) + Vector3.up * Mathf.Lerp(0.15f, 0.1f, inBlend);
            // at the lookout, stand back and take in the whole mountain below
            // (the camp's lookout faces out over the cliff's drop; the lodge's deck down the mountain)
            var lf = onCampLookout ? new Vector3(lookFwd.x, 0f, lookFwd.z).normalized : Vector3.forward;
            var lr = new Vector3(lf.z, 0f, -lf.x);
            Vector3 lookPos = pos - lr * 0.8f + Vector3.up * 4.2f - lf * 5.6f, lookAt = pos + Vector3.down * 55f + lf * 190f;
            want = Vector3.Lerp(want, lookPos, lookBlend); look = Vector3.Lerp(look, lookAt, lookBlend);
            // picking an outfit: face the rider, close, with room for the menu below
            var front = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 showPos = pos + front * Mathf.Lerp(6f, 5f, inBlend) + Vector3.up * 1.4f, showAt = pos - Vector3.up * Mathf.Lerp(0.1f, 0.3f, inBlend);
            want = Vector3.Lerp(want, showPos, showBlend); look = Vector3.Lerp(look, showAt, showBlend);
            // never through a wall, a floor or the roof: come in in front of it
            Vector3 dir = want - target;
            float reach = dir.magnitude;
            int nh = Physics.RaycastNonAlloc(target, dir / Mathf.Max(reach, 1e-4f), rayHits, reach, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < nh; i++)
                if (cameraWalls.Contains(rayHits[i].collider) && rayHits[i].distance - 0.3f < reach)
                    reach = Mathf.Max(0.6f, rayHits[i].distance - 0.3f);
            // pulled in close: rise a little and look down, so you don't fill the screen
            float pulled = Mathf.Clamp01((dir.magnitude - reach) / Mathf.Max(1f, dir.magnitude));
            want = target + dir.normalized * reach + Vector3.up * (0.9f * pulled * (1f - showBlend));
            if (!Indoors) want.y = Mathf.Max(want.y, Slope.DrawnHeight(want.x, want.z) + 0.8f);
            Vector3 cp = snap ? want : Vector3.Lerp(cam.transform.position, want, k(6f));
            cam.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(look - cp, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(62f, 64f, inBlend), 54f, lookBlend), Mathf.Lerp(42f, 50f, inBlend), showBlend);
            snap = false;
        }

        /// <summary>Test tools: stand here, facing this way.</summary>
        public void Teleport(float dx, float z, float yawDeg, bool snapCam = true) => TeleportTo(Ground(dx, z), yawDeg, snapCam);

        /// <summary>Test tools: what's pushing on the walker here (blocks within reach, and their heights).</summary>
        public string DebugHere()
        {
            var sb = new System.Text.StringBuilder();
            var p2 = new Vector2(pos.x, pos.z);
            foreach (var b in boxes)
            {
                if (b.floor) continue;
                if ((PushOut(b, p2, 0.4f) - p2).sqrMagnitude > 1e-6f) sb.Append($"[block c={b.c} h={b.h} y={b.lo:0.0}..{b.hi:0.0}] ");
            }
            // and what else the walk pushes you out of, a step on the way you face (round ones, you stand right at their edge):
            // Val and Breanna, the camp's people, trees and the like, snow piles
            var f = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var ahead = p2 + f * 0.15f;
            if (Couple != null && (Couple.PushOut(ahead, 0.38f) - ahead).sqrMagnitude > 1e-6f) sb.Append("[Val or Breanna] ");
            if (PushWalker != null && (PushWalker(ahead) - ahead).sqrMagnitude > 1e-6f) sb.Append("[the camp's people] ");
            if ((OffTrees(new Vector3(ahead.x, pos.y, ahead.y)) - new Vector3(ahead.x, pos.y, ahead.y)).sqrMagnitude > 1e-6f) sb.Append("[a tree or the like] ");
            if ((OffPiles(ahead, pos.y) - ahead).sqrMagnitude > 1e-6f) sb.Append("[a snow pile] ");
            return $"feet={pos.y:0.00} speed={speed:0.00} " + sb;
        }

        /// <summary>Test tools: stand at a world point (on whatever floor is there), facing this way.</summary>
        public void TeleportTo(Vector3 at, float yawDeg, bool snapCam = true)
        {
            pos = at; yaw = yawDeg * Mathf.Deg2Rad; camYaw = yaw; speed = 0f;
            snap = snapCam;
            Place();
            Walk(Vector2.zero, 0f);
        }
    }
}
