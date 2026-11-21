using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The camp's fire: two stumps pulled up close to it, Alyssa on one making s'mores (Alyssa.cs), and the other for
    /// you. TALK by her opens a conversation (CampTalk, MetaUiTalk); ROAST A MARSHMALLOW at the free stump (or "can I try
    /// one?" in the talk) sits you down with a stick of your own:
    ///   - hold a finger anywhere and the marshmallow goes down into the flames (browning fast); let go and it rises to
    ///     just above the embers (slowly, evenly);
    ///   - too long in the flames and it catches: tap to blow it out (it chars while it burns);
    ///   - TAKE IT OUT when it looks right. It's slid onto a cracker with a square of chocolate, squashed, and eaten.
    /// How it came out is only ever shown on the marshmallow itself (pale, golden, toasted, burnt); a few points come
    /// with it, more for a golden one, and that's all. Your next hot chocolate gets marshmallows on top (CampCocoa).
    /// Activities take the camp's input while they run (ActStep, called from Step): nothing walks, jumps or rolls.
    /// </summary>
    public partial class Camp : RiderBody.ICampPose
    {
        enum Act { None, Talk, Roast, Cocoa }
        Act act;
        Alyssa alyssa;
        Vector3 fireAt, heroSeat, heroFace = Vector3.left, roastStand;
        Marshmallow heroMallow;
        Material stickMat, crackerMat, chocMat;

        /// <summary>Alyssa (test tools).</summary>
        public Alyssa Alyssa => alyssa;
        /// <summary>Talking, roasting or making hot chocolate: the camp's input goes to that.</summary>
        public bool Busy => act != Act.None;
        public string Activity => act.ToString();
        /// <summary>A s'more eaten this visit: the hot chocolate gets marshmallows.</summary>
        public bool HadSmore { get; private set; }
        /// <summary>How the last marshmallow came out (test tools; never shown as words).</summary>
        public string LastRoast { get; private set; } = "";
        public Marshmallow HeroMallow => heroMallow;
        public float RoastHeat => closeK;

        void Campfire()
        {
            Marshmallow.Soft = soft;
            if (firePit == null) return;
            fireAt = firePit.transform.position;
            var t = new GameObject("FireSeats").transform;
            t.SetParent(root, false); t.position = fireAt;
            fireSeats = t;
            if (stickMat == null)
            {
                stickMat = Marshmallow.Plain(new Color(0.55f, 0.47f, 0.33f), 0.15f);
                crackerMat = Marshmallow.Plain(new Color(0.78f, 0.56f, 0.32f), 0.1f);
                chocMat = Marshmallow.Plain(new Color(0.25f, 0.13f, 0.07f), 0.45f);
            }
            // two stumps pulled up close, either side of the fire (inside the ring of benches, which stay for sitting
            // round it): Alyssa's on the left as you come from the lodge, yours on the right
            Vector3 Stump(float side)
            {
                Vector3 at = fireAt + Vector3.right * side * 1.75f;
                at.y = Slope.DrawnHeight(at.x, at.z);
                Vector3 l = t.InverseTransformPoint(at);
                Log(t, l + Vector3.down * 0.25f, l + Vector3.up * 0.42f, 0.5f, logs);
                P(PrimitiveType.Cylinder, t, l + Vector3.up * 0.425f, new Vector3(0.47f, 0.012f, 0.47f), Vector3.zero, darkWood);   // the cut face
                Block(t, l, new Vector2(0.27f, 0.27f));
                return at + Vector3.up * 0.43f;
            }
            Vector3 hers = Stump(-1f);
            heroSeat = Stump(1f);
            heroFace = Vector3.left;
            alyssa = Alyssa.Create(root, fireAt, hers, Vector3.right);
            alyssa.enabled = Active;
            alyssa.Hero = rider != null ? rider.transform : null;   // (she gets you in her selfies, when you're close)   // (built for a new mountain before a run, too: still until you're back in the camp)
            // her and her legs are solid; tapping her (standing still) talks
            Block(t, t.InverseTransformPoint(hers + Vector3.right * 0.3f), new Vector2(0.45f, 0.4f));
            var tapA = new GameObject("TAP_Talk").transform;
            tapA.SetParent(alyssa.transform, false); tapA.position = hers + Vector3.up * 0.35f + Vector3.right * 0.2f;
            var ca = tapA.gameObject.AddComponent<BoxCollider>(); ca.isTrigger = true; ca.size = new Vector3(1f, 1.5f, 0.9f);
            Vector3 talkAt = alyssa.TalkSpot; talkAt.y = Slope.DrawnHeight(talkAt.x, talkAt.z);
            spots.Add(new Spot { st = Station.Talk, at = talkAt, radius = 1.5f, root = tapA });
            // where you stand to sit down at your stump: just behind it, away from the fire
            roastStand = heroSeat - heroFace * 0.6f; roastStand.y = Slope.DrawnHeight(roastStand.x, roastStand.z);
            var tapR = new GameObject("TAP_Roast").transform;
            tapR.SetParent(t, false); tapR.position = heroSeat;
            var cr = tapR.gameObject.AddComponent<BoxCollider>(); cr.isTrigger = true; cr.size = new Vector3(0.8f, 1f, 0.8f);
            spots.Add(new Spot { st = Station.Roast, at = roastStand, radius = 1.2f, root = tapR });
        }

        Transform fireSeats;

        /// <summary>
        /// Alyssa and the stumps are there when the fire is (it's bought in the camp's shop, FIRE PIT): nobody sits
        /// round a fire that isn't there. From Redress.
        /// </summary>
        void FireRedress()
        {
            bool on = firePit != null && firePit.activeSelf;
            if (alyssa != null && alyssa.gameObject.activeSelf != on) alyssa.gameObject.SetActive(on);
            if (fireSeats != null && fireSeats.gameObject.activeSelf != on) fireSeats.gameObject.SetActive(on);
        }

        // ---------------------------------------------------------------- the hooks from Step, Open, Enter and Leave

        /// <summary>Open one of the fire's or the kitchen's stations (from Open). True if it was one.</summary>
        bool OpenFireStation(Station st, MetaUi ui)
        {
            if (st == Station.Talk) { StartTalk(ui); return true; }
            if (st == Station.Roast) { StartRoast(); return true; }
            if (st == Station.Cocoa) { StartCocoa(); return true; }
            return false;
        }

        /// <summary>A frame of whatever's going on at the fire or the stove. True: it took the frame (Step stops there).</summary>
        bool ActStep(float dt, MetaUi ui, List<CampInput.Touch> touches)
        {
            MugStep(dt);
            ui.Quiet = act != Act.None;   // (no controls hint while you're busy at the fire or the stove)
            if (act == Act.None) { ui.ContextLabel = null; ui.Sipping = HoldingMug; return false; }
            ui.SetStick(false, Vector2.zero, Vector2.zero); ui.SetLookStick(false, Vector2.zero, Vector2.zero);
            ui.Sipping = false;
            if (act == Act.Talk)
            {
                if (!ui.TalkStep(dt, input.Tapped, input.Button))
                {
                    alyssa.TalkingTo = null; act = Act.None;
                    if (ui.TalkRoast) StartRoast();
                }
            }
            else if (act == Act.Roast) RoastStep(dt, ui, touches);
            else if (act == Act.Cocoa) CocoaStep(dt);
            speed = 0f;
            Walk(Vector2.zero, dt);   // (stands the body where it is: the pose below moves it onto the stump)
            Near = Station.None;
            ui.SetContext(Station.None);
            return true;
        }

        /// <summary>Everything put down when the camp is left (dropping in, the start page).</summary>
        void EndActs(MetaUi ui = null)
        {
            if (act == Act.Talk && alyssa != null) alyssa.TalkingTo = null;
            if (ui != null) { ui.EndTalk(); ui.ContextLabel = null; ui.Sipping = false; ui.Quiet = false; }
            if (heroMallow != null) { heroMallow.Destroy(); heroMallow = null; }
            act = Act.None;
            EndCocoa();
        }

        // ---------------------------------------------------------------- talking

        /// <summary>An activity starts: whatever jump or roll was under way ends where you stand.</summary>
        void ClearMove() { moveKind = 0; moveT = 0f; jumpY = 0f; jumpV = 0f; airborne = false; }

        void StartTalk(MetaUi ui)
        {
            if (alyssa == null) return;
            ClearMove();
            act = Act.Talk;
            alyssa.TalkingTo = rider.transform;
            Vector3 at = alyssa.TalkSpot; at.y = FloorAt(at, pos.y);
            pos = at;
            Vector3 to = alyssa.Seat - pos;
            yaw = Mathf.Atan2(to.x, to.z);
            camYaw = yaw + 0.55f;   // over your shoulder, her face and the fire in view
            speed = 0f;
            ui.ShowTalk(CampTalk.Next());
        }

        // ---------------------------------------------------------------- roasting your own

        enum Roast { Sit, Hold, Pull, Build, Eat, Up }
        Roast rp;
        float rpT, closeK, blowT, sitW;
        static readonly float[] RoastLen = { 0.8f, 0f, 0.9f, 1.6f, 3.4f, 0.7f };

        void StartRoast()
        {
            ClearMove();
            act = Act.Roast; rp = Roast.Sit; rpT = 0f; closeK = 0.15f; blowT = 0f; sitW = 0f;
            // two hands for this (the stick, then the s'more): a mug in hand goes down on the snow by the stump
            if (mug != null) ParkMug(heroSeat - Vector3.Cross(Vector3.up, heroFace) * 0.5f);
            if (heroMallow == null) heroMallow = new Marshmallow(root, stickMat);
            heroMallow.Fresh(); heroMallow.SetStickVisible(true);
            pos = roastStand; pos.y = FloorAt(pos, pos.y);
            yaw = Mathf.Atan2(heroFace.x, heroFace.z);
            camYaw = yaw - 0.35f;   // behind you, a little to the side: the fire, your marshmallow and Alyssa across it
            speed = 0f;
        }

        /// <summary>The test tools' finger: held (the marshmallow down in the flames) or not.</summary>
        public static bool TestHold;

        void RoastStep(float dt, MetaUi ui, List<CampInput.Touch> touches)
        {
            rpT += dt;
            bool held = TestHold;
            foreach (var tc in touches) if (ui.Hit(tc.pos) == null) held = true;
            ui.ContextLabel = rp == Roast.Hold ? "TAKE IT OUT" : null;
            switch (rp)
            {
                case Roast.Sit:
                    sitW = Mathf.Clamp01(rpT / RoastLen[0]);
                    if (rpT >= RoastLen[0]) Next(Roast.Hold);
                    break;
                case Roast.Hold:
                    // held: down into the flames; let go: up over the embers. Alight: a tap blows it out
                    closeK = Mathf.MoveTowards(closeK, held && blowT <= 0f ? 1f : 0.15f, dt * 2.2f);
                    if (heroMallow.Alight && input.Tapped) blowT = 0.6f;
                    if (blowT > 0f) { blowT -= dt; if (blowT < 0.25f) heroMallow.BlowOut(); }
                    heroMallow.Cook(dt, blowT > 0f ? 0f : Mathf.Lerp(0.6f, 3f, closeK));
                    if (input.ContextTapped) { heroMallow.BlowOut(); blowT = 0f; Next(Roast.Pull); }
                    break;
                case Roast.Pull:
                    closeK = Mathf.MoveTowards(closeK, 0f, dt * 3f);
                    if (rpT >= RoastLen[2]) { heroMallow.MakeSmore(rider.transform, crackerMat, chocMat); Next(Roast.Build); }
                    break;
                case Roast.Build:
                    heroMallow.Squash(Mathf.Clamp01((rpT - 0.4f) / 0.8f));
                    if (rpT > 1.1f) heroMallow.SetStickVisible(false);
                    if (rpT >= RoastLen[3]) Next(Roast.Eat);
                    break;
                case Roast.Eat:
                    heroMallow.Bite(rpT < 1.1f ? 1f : rpT < 2.2f ? 0.62f : rpT < 3.2f ? 0.3f : 0f);
                    if (rpT >= RoastLen[4])
                    {
                        // how it came out: a few points, more for a golden one (no words: the marshmallow said it)
                        LastRoast = Marshmallow.Grade(heroMallow.Toast);
                        int pts = LastRoast == "golden" ? 30 : LastRoast == "toasted" ? 15 : LastRoast == "pale" ? 6 : 3;
                        Progress.Add(pts);
                        ui.Refresh();
                        HadSmore = true;
                        Next(Roast.Up);
                    }
                    break;
                case Roast.Up:
                    sitW = 1f - Mathf.Clamp01(rpT / RoastLen[5]);
                    if (rpT >= RoastLen[5]) { heroMallow.Destroy(); heroMallow = null; act = Act.None; ui.ContextLabel = null; UnparkMug(); }
                    break;
            }
            if (heroMallow != null) heroMallow.Refresh(dt);
        }

        void Next(Roast r) { rp = r; rpT = 0f; }

        /// <summary>Test tools: take the marshmallow out now (TAKE IT OUT), or blow it out.</summary>
        public void TestTakeOut() { if (act == Act.Roast && rp == Roast.Hold) { heroMallow.BlowOut(); Next(Roast.Pull); } }
        public void TestBlow() { if (act == Act.Roast && heroMallow != null && heroMallow.Alight) blowT = 0.6f; }
        public bool RoastHolding => act == Act.Roast && rp == Roast.Hold;

        /// <summary>Over the embers, or (held) down in the flames: where the stick points while it's out.</summary>
        Vector3 OverFire() => fireAt + Vector3.up * Mathf.Lerp(0.85f, 0.4f, closeK) - heroFace * Mathf.Lerp(0.3f, 0.08f, closeK);

        /// <summary>How far the stick's tipped up to look (sitting down, taking it out) or to blow on it (alight).</summary>
        float RoastLift(out float blow)
        {
            blow = rp == Roast.Hold ? Mathf.Clamp01(blowT / 0.15f) : 0f;
            return rp == Roast.Sit ? 1f - sitW : rp == Roast.Pull ? Mathf.Clamp01(rpT / 0.5f) : rp == Roast.Hold ? blow : 1f;
        }

        // ---------------------------------------------------------------- the hero's pose (RiderBody.CampPose)

        public bool Want(HeroRig h)
        {
            if (act == Act.Talk || act == Act.Roast || CocoaMaking) { h.Want(h.Id("Stand"), 1f); return true; }
            return false;
        }

        public void After(HeroRig h, Transform model, float dt)
        {
            if (act == Act.Roast) PoseRoast(h, model);
            else if (act == Act.Cocoa || HoldingMug) PoseCocoa(h, model, dt);
        }

        void PoseRoast(HeroRig h, Transform model)
        {
            Vector3 standPos = model.position; Quaternion standRot = model.rotation;
            CampPoses.Seat(h, model, heroSeat, heroFace);
            float w = Mathf.SmoothStep(0f, 1f, sitW);
            if (w < 1f) { model.position = Vector3.Lerp(standPos, model.position, w); model.rotation = Quaternion.Slerp(standRot, model.rotation, w); }
            if (w > 0.35f) CampPoses.FoldLegs(h, heroFace, fireAt.y, 1f);
            Vector3 right = Vector3.Cross(Vector3.up, heroFace);
            Vector3 chest = h.Chest.position, hips = h.Hips.position;
            float lift = RoastLift(out float blow);
            bool stick = heroMallow != null && heroMallow.Stick.gameObject.activeSelf;
            Vector3 rest = chest + heroFace * 0.25f + right * 0.12f - Vector3.up * 0.25f;
            Vector3 handR = stick ? CampPoses.StickHand(h, lift, 1f) : rest;
            // the left hand: in the lap; round the s'more; up to the mouth for each bite
            Vector3 lap = hips + heroFace * 0.32f - right * 0.1f + Vector3.up * 0.05f;
            Vector3 front = chest + heroFace * 0.32f - Vector3.up * 0.12f;
            Vector3 mouth = CampPoses.Mouth(h, heroFace, 1f);
            Vector3 handL = lap;
            if (rp == Roast.Build) { handL = front - right * 0.02f; handR = Vector3.Lerp(handR, front + right * 0.06f, Mathf.Clamp01(rpT / 0.6f)); }
            if (rp == Roast.Eat)
            {
                float bite = 0.5f + 0.5f * Mathf.Cos(rpT / 1.1f * Mathf.PI * 2f + Mathf.PI);
                handL = Vector3.Lerp(front, mouth - right * 0.03f, bite);
            }
            if (w > 0.2f)
            {
                h.ArmTo(1, Vector3.Lerp(rest, handR, w), h.UpperArm[1].position + right * 0.4f - Vector3.up * 0.3f);
                h.ArmTo(0, Vector3.Lerp(rest - right * 0.24f, handL, w), h.UpperArm[0].position - right * 0.4f - Vector3.up * 0.3f);
            }
            if (heroMallow != null)
            {
                if (stick) CampPoses.HoldStick(h, heroMallow, CampPoses.StickAim(h, OverFire(), lift, blow, 1f), rpT * 80f);
                if (heroMallow.Smore != null) { heroMallow.Smore.position = h.Hand[0].position + heroFace * 0.06f + Vector3.up * 0.03f; heroMallow.Smore.rotation = Quaternion.LookRotation(heroFace, Vector3.up); }
            }
        }
    }
}
