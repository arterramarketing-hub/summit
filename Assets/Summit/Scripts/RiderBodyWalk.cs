using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The rider on foot in the summit camp (no board: it waits at the gate), and the outfit colours. Kept apart from RiderBody's riding
    /// code: RiderBody only calls WalkFrame and WalkMuscles while Walking is set.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>On foot in the camp: the board is strapped to the back and the legs walk.</summary>
        public bool Walking { get; private set; }
        /// <summary>0 standing still .. 1 full walking pace (the camp sets it every frame).</summary>
        public float WalkPace;
        /// <summary>0 walking .. 1 running flat out (the camp sets it every frame).</summary>
        public float RunBlend;
        /// <summary>Walk cycles a second (two steps each), from how fast you're going; 0: work it out from the pace.</summary>
        public float Cadence;
        float runAmp, cadence;
        /// <summary>Thrown from on foot (the camp's edge giving way): the velocity the coming crash throws the body with.</summary>
        public void Tumble(Vector3 velocity) { lastVel = velocity; }

        /// <summary>The walk cycle (radians): the left heel strikes at pi/2, the right at 3pi/2 (see RiderRig.PoseWalk).</summary>
        public float WalkPhase => walkPhase;
        /// <summary>How far into a run the legs are (0 walking .. 1 running), eased.</summary>
        public float RunAmount => runAmp;
        float walkPhase, walkAmp;
        /// <summary>A jump or a roll in the camp (0 none, 1 jump, 2 roll), and how far into it (s): the camp sets them.</summary>
        public int CampMove; public float CampMoveT;
        Vector3 boardHomePos; Quaternion boardHomeRot; bool boardHomeSet, tubeBeforeWalk;

        public void SetWalking(bool on)
        {
            if (Walking == on) return;
            if (!boardHomeSet) { boardHomePos = boardRoot.localPosition; boardHomeRot = boardRoot.localRotation; boardHomeSet = true; }
            Walking = on;
            if (on)
            {
                // nobody walks round camp sitting in a tube; and the board stays at the gate: on foot with both
                // hands free, so both arms swing (it is back on the feet for the run)
                tubeBeforeWalk = Tube;
                if (Tube) SetTube(false);
                ShowRide();   // nothing carried: the board (or the skis and poles) waits at the gate
            }
            else
            {
                boardRoot.localPosition = boardHomePos;
                boardRoot.localRotation = boardHomeRot;
                ShowRide();
                walkPhase = 0f; walkAmp = 0f; WalkPace = 0f; RunBlend = 0f; runAmp = 0f;
                if (tubeBeforeWalk) SetTube(true);
            }
        }

        /// <summary>Upright on foot: no edge lean, a bob in step, a slight lean into the walk.</summary>
        void WalkFrame(float dt)
        {
            walkAmp = Mathf.MoveTowards(walkAmp, WalkPace, dt * 4f);
            runAmp = Mathf.MoveTowards(runAmp, RunBlend, dt * 3f);   // eases into and out of the run
            // strides come quicker as you speed up: about one a second walking, nearly three running
            float want = Cadence > 0f ? Cadence : Mathf.Lerp(Mathf.Lerp(0.55f, 1.0f, walkAmp), 1.45f, runAmp);
            cadence = Mathf.MoveTowards(cadence, want, dt * 4f);
            walkPhase += dt * Mathf.PI * 2f * cadence * (walkAmp > 0.02f ? 1f : 0f);
            roll.localRotation = Quaternion.identity; roll.localPosition = Vector3.zero;
            upper.localRotation = Quaternion.identity;
            if (CampMove == 2)
            {
                // a forward roll: the whole body turns head over heels round its middle (about a ball's height up),
                // the tuck itself is the pose
                float k = Mathf.Clamp01((CampMoveT - 0.1f) / 0.56f);
                float ang = 360f * k * k * (3f - 2f * k);
                var pv = new Vector3(0f, 0.45f, 0f);
                var rot = Quaternion.AngleAxis(-ang, Vector3.forward);   // the top tips the way you're going (+X)
                roll.localRotation = rot; roll.localPosition = pv - rot * pv;
            }
            // the humanoid's feet are planted at a board's height: stand them on the snow instead
            // (the rig plants its own feet, see RiderRig.PoseWalk)
            if (poser != null) pivot.position += pivot.up * (Mathf.Abs(Mathf.Sin(walkPhase)) * Mathf.Lerp(0.05f, 0.09f, runAmp) * walkAmp - BoardTop - 0.02f);
        }

        /// <summary>Muscle targets for a walk cycle (over the snowboard pose RiderBody built this frame).</summary>
        void WalkMuscles(float t)
        {
            float a = walkAmp, r = runAmp, s = Mathf.Sin(walkPhase), c = Mathf.Cos(walkPhase);
            // legs swing opposite each other; the leg coming forward bends at the knee (much more running)
            float sw = Mathf.Lerp(0.7f, 1f, r);
            W(mHipL, 0.08f + 0.2f * r + sw * s * a); W(mHipR, 0.08f + 0.2f * r - sw * s * a);
            W(mKneeL, 0.95f - a * (0.15f + Mathf.Lerp(1.2f, 1.8f, r) * Mathf.Max(0f, c)));
            W(mKneeR, 0.95f - a * (0.15f + Mathf.Lerp(1.2f, 1.8f, r) * Mathf.Max(0f, -c)));
            W(mSpreadL, 0.02f); W(mSpreadR, 0.02f);
            W(mSpine, 0.06f + 0.08f * a + 0.25f * r); W(mChest, 0.02f + 0.1f * r);
            W(mTwist, -0.15f * s * a);
            // arms down at the sides, swinging against the legs
            W(mArmL, -0.8f); W(mArmR, -0.8f);
            W(mArmFL, -Mathf.Lerp(0.55f, 0.9f, r) * s * a + 0.05f); W(mArmFR, Mathf.Lerp(0.55f, 0.9f, r) * s * a + 0.05f);
            W(mElbowL, Mathf.Lerp(0.7f, -0.3f, r)); W(mElbowR, Mathf.Lerp(0.7f, -0.3f, r));   // running: both arms bent and pumping
            // standing about: look round the camp
            W(mNeck, 0f);
            W(mHead, (1f - a) * 0.25f * Mathf.Sin(t * 0.45f));
        }

        // ---- outfit colours (bought at the board rack)

        Material boardMat, gearJacket;
        readonly System.Collections.Generic.List<Material> jacketMats = new System.Collections.Generic.List<Material>();
        readonly System.Collections.Generic.List<float> jacketShade = new System.Collections.Generic.List<float>();

        bool ownMats;

        /// <summary>Paint the jacket and the board.</summary>
        public void SetColours(Color jacket, Color board)
        {
            if (!ownMats) OwnMaterials();
            if (boardMat != null) boardMat.SetColor("_BaseColor", board);
            if (gearJacket != null) gearJacket.SetColor("_BaseColor", jacket);
            // each jacket material keeps its own shade (the trim is lighter than the body)
            for (int i = 0; i < jacketMats.Count; i++) jacketMats[i].SetColor("_BaseColor", jacket * (i < jacketShade.Count ? jacketShade[i] : 1f));
        }

        /// <summary>
        /// Swap the shared board and jacket materials on this rider for copies, so painting them never
        /// writes into the project's material assets (in the editor that would stick after Play).
        /// </summary>
        void OwnMaterials()
        {
            ownMats = true;
            var copies = new System.Collections.Generic.Dictionary<Material, Material>();
            Material Copy(Material m) { if (!copies.TryGetValue(m, out var c)) { c = new Material(m); copies[m] = c; } return c; }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] != null && (ms[i] == boardMat || jacketMats.Contains(ms[i]))) { ms[i] = Copy(ms[i]); changed = true; }
                if (changed) r.sharedMaterials = ms;
            }
            if (boardMat != null) boardMat = Copy(boardMat);
            for (int i = 0; i < jacketMats.Count; i++) jacketMats[i] = Copy(jacketMats[i]);
        }

        /// <summary>The model's own jacket materials (by name), so a new colour reaches the body too.</summary>
        void FindJacket(GameObject model)
        {
            jacketMats.Clear(); jacketShade.Clear();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string n = m.name.ToLowerInvariant();
                    // the model's materials have no useful names: the jacket is the orange-red one
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    bool orange = c.r > 0.6f && c.g < 0.45f && c.b < 0.3f;
                    if ((orange || n.Contains("jacket") || n.Contains("coat")) && !jacketMats.Contains(m))
                    {
                        jacketMats.Add(m);
                        jacketShade.Add(orange ? Mathf.Clamp(c.maxColorComponent / 0.93f, 0.7f, 1.1f) : 1f);
                    }
                }
        }

        /// <summary>For the test tools: the names of the rider model's materials.</summary>
        public string MaterialNames()
        {
            var sb = new System.Text.StringBuilder();
            if (upper != null)
                foreach (var r in upper.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials)
                        if (m != null) sb.Append(r.name).Append(':').Append(m.name).Append(' ').Append(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "").Append(" | ");
            return sb.ToString();
        }
    }
}
