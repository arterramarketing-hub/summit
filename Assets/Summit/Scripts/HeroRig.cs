using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The hero's animation: clips authored in Blender (Tools/MakeHero.py, baked by the editor into
    /// Resources/Rider/HeroAnim.bytes) blended by weight, then a procedural layer on top that ties the body to
    /// what the rider is really doing:
    ///   - the feet stay locked on the bindings (two-bone IK, keeping the knee where the clip bent it),
    ///   - a grabbing hand stays on the board, and hands stay on the tube's handles,
    ///   - the hips sink with a landing, the back leans with the carve, the shoulders turn into a turn,
    ///   - the head looks where you're going,
    ///   - the hood, and the hands on their arms, lag and settle on springs (secondary motion),
    ///   - the forearm takes half the wrist's twist, so the sleeve doesn't wring.
    /// Everything is plain transform maths on about fifty bones, no Animator: a few hundred quaternion blends
    /// and four IK solves a frame. It works in world space from the bones themselves, so it doesn't care
    /// how the file's bones are oriented.
    /// </summary>
    public class HeroRig
    {
        public readonly Transform Model;
        public bool Ok { get; private set; }

        // ---- the skeleton
        readonly Transform[] bone;          // in the clip table's order
        readonly Quaternion[] bindRot;
        readonly Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
        int hipsIndex = -1;
        public Transform Root, Hips, Spine, Chest, UpperChest, Neck, Head, Hood, Pack;
        public readonly Transform[] UpperArm = new Transform[2], LowerArm = new Transform[2], Twist = new Transform[2], Hand = new Transform[2],
                                    UpperLeg = new Transform[2], LowerLeg = new Transform[2], Foot = new Transform[2];
        readonly Quaternion[] twistBind = new Quaternion[2], handBind = new Quaternion[2];
        Vector3 faceLocal;             // the way the face looks, in the head bone's frame
        Vector3 hoodTipLocal;          // a point down the hood, in its bone's frame
        Vector3 packTipLocal;          // the bottom of the backpack, in its bone's frame

        // ---- the clips
        class Clip { public string name; public int samples, frames; public bool loop; public float fps; public Quaternion[] rot; public Vector3[] hips; }
        readonly List<Clip> clips = new List<Clip>();
        readonly Dictionary<string, int> clipIds = new Dictionary<string, int>();
        float[] weight, target, time, rate;
        bool[] held;     // a clip whose time is set from outside (the walk, by the stride), not advanced here
        Quaternion[] acc;

        public HeroRig(Transform model, TextAsset table)
        {
            Model = model;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            Transform F(string n) => byName.TryGetValue(n, out var t) ? t : null;
            Root = F("Root"); Hips = F("Hips"); Spine = F("Spine"); Chest = F("Chest"); UpperChest = F("UpperChest");
            Neck = F("Neck"); Head = F("Head"); Hood = F("Hood"); Pack = F("Backpack");
            for (int s = 0; s < 2; s++)
            {
                string sd = s == 0 ? "Left" : "Right";
                UpperArm[s] = F(sd + "UpperArm"); LowerArm[s] = F(sd + "LowerArm"); Twist[s] = F(sd + "ForearmTwist"); Hand[s] = F(sd + "Hand");
                UpperLeg[s] = F(sd + "UpperLeg"); LowerLeg[s] = F(sd + "LowerLeg"); Foot[s] = F(sd + "Foot");
            }
            if (table == null || Root == null || Hips == null || Head == null) return;
            var names = Read(table.bytes);
            if (names == null) return;
            bone = new Transform[names.Count]; bindRot = new Quaternion[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                bone[i] = F(names[i]);
                if (bone[i] == null) { Debug.LogWarning("HeroRig: no bone " + names[i]); return; }
                bindRot[i] = bone[i].localRotation;
                if (names[i] == "Hips") hipsIndex = i;
            }
            acc = new Quaternion[bone.Length];
            weight = new float[clips.Count]; target = new float[clips.Count]; time = new float[clips.Count];
            rate = new float[clips.Count]; held = new bool[clips.Count];
            for (int i = 0; i < clips.Count; i++) rate[i] = 1f;
            for (int s = 0; s < 2; s++) { if (Twist[s] != null) twistBind[s] = Twist[s].localRotation; if (Hand[s] != null) handBind[s] = Hand[s].localRotation; }
            Ok = hipsIndex >= 0 && UpperLeg[0] != null && UpperArm[0] != null;
        }

        List<string> Read(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (r.ReadInt32() != 0x4F524548 || r.ReadInt32() != 1) { Debug.LogWarning("HeroRig: not a hero clip table"); return null; }
                int nb = r.ReadInt32();
                var names = new List<string>(nb);
                for (int i = 0; i < nb; i++) names.Add(r.ReadString());
                while (r.BaseStream.Position < r.BaseStream.Length)
                {
                    var c = new Clip { name = r.ReadString() };
                    c.samples = r.ReadInt32(); c.frames = r.ReadInt32(); c.loop = r.ReadBoolean(); c.fps = r.ReadSingle();
                    c.rot = new Quaternion[c.samples * nb]; c.hips = new Vector3[c.samples];
                    const float k = 1f / 32767f;
                    for (int f = 0; f < c.samples; f++)
                    {
                        for (int b = 0; b < nb; b++)
                            c.rot[f * nb + b] = new Quaternion(r.ReadInt16() * k, r.ReadInt16() * k, r.ReadInt16() * k, r.ReadInt16() * k);
                        c.hips[f] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    }
                    clipIds[c.name] = clips.Count;
                    clips.Add(c);
                }
                return names;
            }
        }

        public Transform Bone(string n) => byName.TryGetValue(n, out var t) ? t : null;
        public int Id(string name) => clipIds.TryGetValue(name, out var i) ? i : -1;
        public float Length(int id) => id < 0 ? 0f : clips[id].frames / clips[id].fps;
        public int ClipCount => clips.Count;

        /// <summary>Call once the model is placed at rest: where the face and the hood are, in their bones' frames.</summary>
        public void CaptureRest(Vector3 facing, Vector3 up)
        {
            faceLocal = Quaternion.Inverse(Head.rotation) * facing;
            if (Hood != null) hoodTipLocal = Hood.InverseTransformPoint(Hood.position - facing * 0.07f - up * 0.08f);
            if (Pack != null) packTipLocal = Pack.InverseTransformPoint(Pack.position - facing * 0.03f - up * 0.26f);
        }

        // ------------------------------------------------------------------------------------------- blending

        /// <summary>Start a frame's wishes: every clip's target weight back to nothing.</summary>
        public void Begin() { System.Array.Clear(target, 0, target.Length); System.Array.Clear(held, 0, held.Length); }

        /// <summary>How much of this clip to show this frame (weights are shared out among everything wanted).</summary>
        public void Want(int id, float w) { if (id >= 0 && w > 0f) target[id] += w; }

        /// <summary>Play a clip from its start (a one-shot: the pop, the landing).</summary>
        public void Restart(int id) { if (id >= 0) time[id] = 0f; }
        /// <summary>Put a clip at this time (s): one played in step with something else (a paddle stroke: RiderBodyVehicle).</summary>
        public void SetTime(int id, float t) { if (id >= 0) time[id] = Mathf.Clamp(t, 0f, Length(id)); }

        /// <summary>Hold a clip at this point of its cycle (0..1) instead of letting it run.</summary>
        public void At(int id, float t01) { if (id < 0) return; held[id] = true; time[id] = Mathf.Repeat(t01, 1f) * Length(id); }

        public float TimeOf(int id) => id < 0 ? 0f : time[id];

        /// <summary>Snap straight to the wanted weights (the first frame, or after a teleport).</summary>
        public void Snap() { System.Array.Copy(target, weight, weight.Length); }

        /// <summary>Ease the weights toward what was wanted, run the clips on, and write the blended pose to the bones.</summary>
        public void Evaluate(float dt, float blendSpeed = 9f)
        {
            if (!Ok) return;
            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * blendSpeed);
            float sum = 0f;
            for (int c = 0; c < clips.Count; c++)
            {
                weight[c] = Mathf.Lerp(weight[c], target[c], k);
                if (weight[c] < 1e-3f && target[c] <= 0f) weight[c] = 0f;
                sum += weight[c];
                if (!held[c] && weight[c] > 0f)
                {
                    float len = Length(c);
                    time[c] += dt * rate[c];
                    if (clips[c].loop) time[c] = Mathf.Repeat(time[c], Mathf.Max(1e-4f, len)); else time[c] = Mathf.Min(time[c], len);
                }
            }
            if (sum <= 1e-5f) return;
            int nb = bone.Length;
            for (int b = 0; b < nb; b++) acc[b] = new Quaternion(0f, 0f, 0f, 0f);
            Vector3 hp = Vector3.zero;
            for (int c = 0; c < clips.Count; c++)
            {
                float w = weight[c] / sum;
                if (w <= 0f) continue;
                var cl = clips[c];
                float f = time[c] * cl.fps;
                int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, cl.frames);
                int i1 = Mathf.Min(i0 + 1, cl.frames);
                float u = Mathf.Clamp01(f - i0);
                int o0 = i0 * nb, o1 = i1 * nb;
                for (int b = 0; b < nb; b++)
                {
                    var q = Nlerp(cl.rot[o0 + b], cl.rot[o1 + b], u);
                    var a = acc[b];
                    // keep every contribution on the same side of the 4D sphere as the bind pose, or two
                    // nearly equal rotations with opposite signs would cancel out
                    var r = bindRot[b];
                    float d = q.x * r.x + q.y * r.y + q.z * r.z + q.w * r.w;
                    float s = d < 0f ? -w : w;
                    acc[b] = new Quaternion(a.x + q.x * s, a.y + q.y * s, a.z + q.z * s, a.w + q.w * s);
                }
                hp += Vector3.Lerp(cl.hips[i0], cl.hips[i1], u) * w;
            }
            for (int b = 0; b < nb; b++)
            {
                var a = acc[b];
                float n = Mathf.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z + a.w * a.w);
                bone[b].localRotation = n > 1e-6f ? new Quaternion(a.x / n, a.y / n, a.z / n, a.w / n) : bindRot[b];
            }
            bone[hipsIndex].localPosition = hp;
        }

        static Quaternion Nlerp(Quaternion a, Quaternion b, float t)
        {
            if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            var q = new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
        }

        // ------------------------------------------------------------------------------------------- the procedural layer

        /// <summary>What the procedural layer is told each frame.</summary>
        public struct Drive
        {
            public Transform board;         // the frame the clips were made against: the board, the tube, or the ground
            public Vector3 rootInBoard;     // where the clips' Root bone belongs in that frame
            public Quaternion rootRotInBoard;
            public bool feet;               // lock the feet where the clip put them on the board
            public float handL, handR;      // 0..1: lock this hand where the clip put it on the board (a grab, a handle)
            public float drop;              // m: the hips sink this far (landing, chatter)
            public float lean;              // deg: the upper body tips toward the toe edge (+) or the heel edge (-)
            public float twist;             // deg: the shoulders turn toward the nose (+) or the tail (-)
            public float side;              // deg: the chest rolls (the tube's weight shift)
            public Vector3 look;            // world: where the head wants to look (zero: leave it to the clip)
            public float lookWeight;
            public float lookReach;         // deg the neck and the head may each turn toward it (0: the usual glance, 55)
            public Vector3 up, toe, nose;   // the board's axes in the world
            public Hold holdL, holdR;       // where a locked hand holds on, from its own clip (not from the blend)
        }

        /// <summary>A hand's grip on the board, relative to the Root bone, as one clip has it.</summary>
        public struct Hold { public bool ok; public Vector3 pos; public Quaternion rot; }

        /// <summary>
        /// Where this clip puts a hand, relative to the Root bone (the board). A grab blends in and out, so the
        /// blended hand is somewhere between the air pose and the board: the IK must reach for the grab's own spot.
        /// </summary>
        public Hold CaptureHold(int clip, int hand)
        {
            if (!Ok || clip < 0) return default;
            Begin(); Want(clip, 1f); time[clip] = 0f; Snap(); Evaluate(0f);
            var h = new Hold { ok = true, pos = Root.InverseTransformPoint(Hand[hand].position), rot = Quaternion.Inverse(Root.rotation) * Hand[hand].rotation };
            Begin(); System.Array.Clear(weight, 0, weight.Length);
            return h;
        }

        // springs
        Vector3 hoodP, hoodV; bool hoodInit;
        Vector3 packP, packV;
        readonly Vector3[] handOff = new Vector3[2], handVel = new Vector3[2];
        Vector3 lastChest, chestVel; bool chestInit;

        /// <summary>Settle the springs where the pose is now (after a teleport or the ragdoll).</summary>
        public void ResetSprings() { hoodInit = false; chestInit = false; handOff[0] = handOff[1] = handVel[0] = handVel[1] = Vector3.zero; }

        public void Layer(Drive d, float dt)
        {
            if (!Ok) return;
            // 1. where the clip put the feet and hands, as points on the board: the real board may sit
            //    elsewhere (it is lifted in a grab, the body leans off it), so map them onto it
            Quaternion dR = Quaternion.identity; Vector3 rootTarget = Root.position;
            if (d.board != null)
            {
                dR = d.board.rotation * d.rootRotInBoard * Quaternion.Inverse(Root.rotation);
                rootTarget = d.board.TransformPoint(d.rootInBoard);
            }
            Vector3 Map(Vector3 p) => rootTarget + dR * (p - Root.position);
            Vector3 ankleL = Map(Foot[0].position), ankleR = Map(Foot[1].position);
            Quaternion footL = dR * Foot[0].rotation, footR = dR * Foot[1].rotation;
            Vector3 kneeL = LowerLeg[0].position, kneeR = LowerLeg[1].position;
            Vector3 wristL = Map(d.holdL.ok ? Root.TransformPoint(d.holdL.pos) : Hand[0].position);
            Vector3 wristR = Map(d.holdR.ok ? Root.TransformPoint(d.holdR.pos) : Hand[1].position);
            Quaternion handLr = dR * (d.holdL.ok ? Root.rotation * d.holdL.rot : Hand[0].rotation);
            Quaternion handRr = dR * (d.holdR.ok ? Root.rotation * d.holdR.rot : Hand[1].rotation);
            Vector3 elbowL = LowerArm[0].position, elbowR = LowerArm[1].position;

            // 2. the body over the board: hips sink, the back leans and turns, the chest rolls
            if (d.drop != 0f) Hips.position -= d.up * d.drop;
            if (d.lean != 0f || d.twist != 0f || d.side != 0f)
            {
                float tl = Mathf.Tan(d.lean * Mathf.Deg2Rad), tw = d.twist * Mathf.Deg2Rad, ts = Mathf.Tan(d.side * Mathf.Deg2Rad);
                var lean = Quaternion.FromToRotation(d.up, (d.up + d.toe * tl).normalized);
                var turn = Quaternion.FromToRotation(d.toe, (d.toe * Mathf.Cos(tw) + d.nose * Mathf.Sin(tw)).normalized);
                var roll = Quaternion.FromToRotation(d.up, (d.up + d.nose * ts).normalized);
                var all = turn * lean * roll;
                // shared down the back: each bone takes its part, and passes it on to the ones above
                Spin(Spine, all, 0.3f); Spin(Chest, all, 0.35f); Spin(UpperChest, all, 0.35f);
            }

            // 3. the head looks where you're going (limited, and only so far off what the clip wanted)
            if (d.lookWeight > 0f && d.look.sqrMagnitude > 1e-4f)
            {
                Vector3 want = d.look.normalized;
                // (a look far round, over the shoulder riding switch: turned about the up axis only, and the way a neck
                // turns, past the chest's side rather than the back of the neck; a turn of nearly 180 degrees as one
                // FromToRotation would tip the head any old way)
                float tw = d.twist * Mathf.Deg2Rad;
                Vector3 chest = d.toe * Mathf.Cos(tw) + d.nose * Mathf.Sin(tw);
                for (int pass = 0; pass < 2; pass++)
                {
                    var tr = pass == 0 ? Neck : Head;
                    Vector3 cur = Head.rotation * faceLocal;
                    float k = d.lookWeight * (pass == 0 ? 0.35f : 0.75f);
                    if (d.lookReach > 55f)
                    {
                        Vector3 cf = Vector3.ProjectOnPlane(cur, d.up), wf = Vector3.ProjectOnPlane(want, d.up);
                        float ang = Vector3.SignedAngle(cf, wf, d.up);
                        if (Mathf.Abs(ang) > 60f && Vector3.Dot(Quaternion.AngleAxis(ang * 0.5f, d.up) * cf, chest) < 0f) ang -= 360f * Mathf.Sign(ang);
                        ang = Mathf.Clamp(ang, -d.lookReach, d.lookReach);
                        tr.rotation = Quaternion.AngleAxis(ang * k, d.up) * tr.rotation;
                        continue;
                    }
                    var delta = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(cur, want), 55f);
                    tr.rotation = Quaternion.Slerp(Quaternion.identity, delta, k) * tr.rotation;
                }
            }

            // 4. secondary motion: the hands hang on springs from the moving chest, the hood flaps
            float sdt = Mathf.Min(dt, 1f / 30f);
            Vector3 chestNow = UpperChest.position;
            Vector3 accel = Vector3.zero;
            if (!chestInit || sdt <= 0f) { lastChest = chestNow; chestVel = Vector3.zero; chestInit = true; }
            else
            {
                Vector3 v = (chestNow - lastChest) / sdt;
                accel = Vector3.ClampMagnitude((v - chestVel) / sdt, 60f);
                chestVel = v; lastChest = chestNow;
            }
            for (int s = 0; s < 2; s++)
            {
                if (sdt > 0f)
                {
                    // stiff and well damped: the hands trail a sudden move by a few centimetres and settle
                    handVel[s] += (-handOff[s] * 160f - handVel[s] * 18f - accel * 0.9f) * sdt;
                    handOff[s] = Vector3.ClampMagnitude(handOff[s] + handVel[s] * sdt, 0.07f);
                }
                if (handOff[s].sqrMagnitude > 1e-8f)
                {
                    Vector3 sh = UpperArm[s].position, h = Hand[s].position;
                    UpperArm[s].rotation = Quaternion.FromToRotation(h - sh, h + handOff[s] - sh) * UpperArm[s].rotation;
                }
            }
            // the hood flaps loosely; the pack is strapped on, so it only sways and settles
            Dangle(Hood, hoodTipLocal, ref hoodP, ref hoodV, 90f, 7f, 2.5f, 0.06f, sdt);
            Dangle(Pack, packTipLocal, ref packP, ref packV, 140f, 11f, 3f, 0.045f, sdt);
            hoodInit = true;

            // 5. the feet back on the board, the knees bending where the clip had them
            if (d.feet)
            {
                Limb(UpperLeg[0], LowerLeg[0], Foot[0], ankleL, kneeL); Foot[0].rotation = footL;
                Limb(UpperLeg[1], LowerLeg[1], Foot[1], ankleR, kneeR); Foot[1].rotation = footR;
            }
            // 6. a hand that holds the board (or a handle) stays on it
            if (d.handL > 0.01f) Reach(0, wristL, elbowL, handLr, d.handL);
            if (d.handR > 0.01f) Reach(1, wristR, elbowR, handRr, d.handR);

            // 7. the forearm twist bone takes half the wrist's twist about the forearm
            for (int s = 0; s < 2; s++)
            {
                if (Twist[s] == null) continue;
                Vector3 axis = Hand[s].localPosition.normalized;   // along the forearm, in its own frame
                Quaternion delta = Hand[s].localRotation * Quaternion.Inverse(handBind[s]);
                Vector3 v = new Vector3(delta.x, delta.y, delta.z);
                Vector3 p = Vector3.Project(v, axis);
                var tw = new Quaternion(p.x, p.y, p.z, delta.w);
                float n = Mathf.Sqrt(tw.x * tw.x + tw.y * tw.y + tw.z * tw.z + tw.w * tw.w);
                tw = n > 1e-6f ? new Quaternion(tw.x / n, tw.y / n, tw.z / n, tw.w / n) : Quaternion.identity;
                // half the twist, but never more than a forearm can turn: past that the sleeve would wring
                tw.ToAngleAxis(out float ang, out Vector3 ax);
                if (ang > 180f) ang -= 360f;
                Twist[s].localRotation = Quaternion.AngleAxis(Mathf.Clamp(ang * 0.5f, -45f, 45f), ax) * twistBind[s];
            }
        }

        /// <summary>A damped spring on a bone's tip: it trails the body's movement and settles back (a damped transform).</summary>
        void Dangle(Transform b, Vector3 tipLocal, ref Vector3 p, ref Vector3 v, float k, float damp, float grav, float max, float dt)
        {
            if (b == null) return;
            Vector3 rest = b.TransformPoint(tipLocal);
            if (!hoodInit || dt <= 0f) { p = rest; v = Vector3.zero; }
            else
            {
                v += ((rest - p) * k - v * damp + Vector3.down * grav) * dt;
                p += v * dt;
                Vector3 off = p - rest;
                if (off.magnitude > max) { p = rest + off.normalized * max; v *= 0.5f; }
            }
            b.rotation = Quaternion.FromToRotation(rest - b.position, p - b.position) * b.rotation;
        }

        static void Spin(Transform t, Quaternion all, float share)
        {
            if (t == null) return;
            t.rotation = Quaternion.Slerp(Quaternion.identity, all, share) * t.rotation;
        }

        void Reach(int s, Vector3 wrist, Vector3 elbowHint, Quaternion handRot, float w)
        {
            Vector3 target = Vector3.Lerp(Hand[s].position, wrist, w);
            Limb(UpperArm[s], LowerArm[s], Hand[s], target, elbowHint);
            Hand[s].rotation = Quaternion.Slerp(Hand[s].rotation, handRot, w);
        }

        // ---- for poses made outside the clips (the camp's fire and its mugs: CampPose, Alyssa)

        /// <summary>Put a hand's wrist (0 left, 1 right) at a point, the elbow bending toward the hint.</summary>
        public void ArmTo(int s, Vector3 wrist, Vector3 elbowHint) { if (Ok) Limb(UpperArm[s], LowerArm[s], Hand[s], wrist, elbowHint); }
        /// <summary>Put an ankle at a point, the knee bending toward the hint.</summary>
        public void LegTo(int s, Vector3 ankle, Vector3 kneeHint) { if (Ok) Limb(UpperLeg[s], LowerLeg[s], Foot[s], ankle, kneeHint); }
        /// <summary>The way the hips face, level (from the line through the hip joints).</summary>
        public Vector3 HipsForward() { Vector3 l = UpperLeg[0].position - UpperLeg[1].position; l.y = 0f; return Vector3.Cross(Vector3.up, l).normalized; }

        /// <summary>Two-bone IK: the end reaches the target; the middle joint bends toward where the hint is.</summary>
        static void Limb(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint)
        {
            Vector3 h = upper.position, k = lower.position, e = end.position;
            float a = (k - h).magnitude, b = (e - k).magnitude;
            Vector3 d = target - h;
            float c = Mathf.Clamp(d.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-4f);
            Vector3 dn = d.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(hint - h, dn);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(k - h, dn);
            pole.Normalize();
            float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            Vector3 knee = h + dn * (cosA * a) + pole * (Mathf.Sqrt(1f - cosA * cosA) * a);
            upper.rotation = Quaternion.FromToRotation(k - h, knee - h) * upper.rotation;
            k = lower.position; e = end.position;
            lower.rotation = Quaternion.FromToRotation(e - k, h + dn * c - k) * lower.rotation;
        }
    }
}
