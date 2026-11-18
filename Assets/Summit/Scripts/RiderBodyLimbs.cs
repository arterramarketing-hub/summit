using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Limbs torn off by the most brutal hits: a forearm or a lower leg, or on the very worst a whole arm or leg
    /// (never the head). Rare: it takes a hit far beyond a broken bone (a limb already broken goes sooner), into a
    /// tree, rock or sign at speed, the hardest slams into the snow, or a fall off a cliff.
    ///
    /// The ragdoll lets go of the part (Ragdoll.Sever: its joint goes and it tumbles away on its own), and every
    /// skinned mesh of the rider (jacket, trousers, gear) is split by its bone weights: each vertex goes with
    /// whichever side holds most of it, keeps only that side's bones, and the triangles that bridged the two are
    /// dropped. So the sleeve and glove fly off with the forearm, deforming as before, and the body keeps the rest.
    /// Both open ends get a cap: a wet red stump with the end of a bone, bleeding for a few seconds (gore on), or
    /// a clean cap in the clothing's own colour, popped off like a toy's (gore off).
    /// The game hears LimbLost (the bill). The rider still gets up and rides on without it: on getting up the
    /// torn-off piece is baked where it lies and left on the snow (with its glove, boot, bandage and cap), its
    /// triangles leave the rider's meshes, its ragdoll parts stay asleep in every later crash, and the hand IK lets go
    /// of a hand that isn't there. The stump keeps its cap. Everything goes back together in ResetVisual (ToReady).
    /// Works on any skinned rider the ragdoll can throw (the hero and the previous one alike).
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>Limbs can come off at all.</summary>
        public static bool Limbs = true;
        // impact speeds into a surface (m/s) that tear a limb off: a forearm or lower leg against a tree, rock or
        // sign, or the snow (far above the bone breaks at 3.2 and 9, and blood at 7 and 9.5). The upper arm or
        // thigh takes `UpperMore` more and then goes whole; a forearm or shin hit `WholeMore` beyond its own
        // threshold takes the whole limb with it. A limb broken already (this crash or earlier in the run) goes
        // at `BrokenEase` of these.
        public static float SeverObstacle = 19f, SeverSnow = 23f, UpperMore = 4f, WholeMore = 8f, BrokenEase = 0.8f;
        // the rider's own impact into a tree, rock or sign (m/s into it) that takes the limb nearest the hit with
        // it, and the whole limb. The rider's sphere takes that hit, so the thrown body never meets the tree as hard
        public static float SeverRider = 20f, SeverRiderWhole = 25f;
        /// <summary>A fall off a cliff this far (m) beyond the mountain's line tears a limb off; twice as far, a whole one.</summary>
        public static float SeverFall = 40f;
        public static int MaxSevers = 2;   // in one crash
        /// <summary>A limb came off: which ("Left forearm", "Right leg"...), where, and whether it was the whole limb.</summary>
        public System.Action<string, Vector3, bool> LimbLost;
        /// <summary>How many limbs have come off this run.</summary>
        public int LimbsLost => severed.Count;
        /// <summary>The torn-off pieces left lying on the snow (for the test tools).</summary>
        public IEnumerable<GameObject> LostPieces { get { foreach (var s in severed) if (s.piece != null) yield return s.piece; } }
        /// <summary>Is this hand (0 left, 1 right) gone with its forearm or arm, after getting up? (the IK lets go of it)</summary>
        public bool HandGone(int i) => ragdoll != null && rig != null && ragdoll.Gone(i == 0 ? rig.HandL : rig.HandR);
        /// <summary>How long the last sever's mesh split took (ms, for the test tools: it happens in one frame).</summary>
        public static float LastSeverMs;
        /// <summary>The stumps and the torn-off pieces' ends (for the test tools).</summary>
        public IEnumerable<GameObject> LimbCaps { get { foreach (var s in severed) { if (s.capBody != null) yield return s.capBody; if (s.capLimb != null) yield return s.capLimb; } } }

        class Skin
        {
            public SkinnedMeshRenderer smr;
            public Mesh original, work;
            public Vector3[] verts;
            public byte[] bpv0; public BoneWeight1[] w0; public int[][] tris0;   // as built
            public byte[] bpv; public BoneWeight1[] w; public int[][] tris;      // as cut so far this run
        }
        class Lost
        {
            public Transform bone; public bool whole, gore; public GameObject capBody, capLimb; public float t; public Vector3 poolBody, poolLimb; public int pools;
            public Material cloth;                                                   // the cap's colour with gore off
            public readonly Dictionary<Skin, bool[]> side = new Dictionary<Skin, bool[]>();   // which vertices went with it
            public GameObject piece;                                                 // once the rider is up: what lies on the snow
        }
        readonly List<GameObject> hiddenOnLimb = new List<GameObject>();   // gear, bandages and caps that were on a lost limb

        readonly List<Skin> skins = new List<Skin>();
        readonly List<Lost> severed = new List<Lost>();
        static Mesh domeMesh;
        Material fleshMat, boneMat;
        bool limbHooks;
        // the rider's last hit, held until the crash it causes; then the limb nearest it goes once the body is thrown
        Collider riderHitCol; float riderHitInto; int riderHitFrame = -1;
        bool doomed; Vector3 doomAt, doomN; float doomInto;

        void OnLimbObstacle(Obstacle ob, Vector3 centre, float into)
        {
            riderHitCol = ob != null ? ob.GetComponentInChildren<Collider>() : null; riderHitInto = into; riderHitFrame = Time.frameCount;
        }

        void OnLimbCrash(string why)
        {
            doomed = false;
            var col = riderHitCol; riderHitCol = null;   // one hit, one crash
            if (!Limbs || riderHitFrame != Time.frameCount || col == null || riderHitInto < SeverRider) return;
            Vector3 me = rider.transform.position + Vector3.up * 0.8f;
            doomAt = col.ClosestPoint(me);
            doomN = me - doomAt; doomN.y *= 0.3f;
            doomN = doomN.sqrMagnitude > 1e-4f ? doomN.normalized : -rider.Velocity.normalized;
            doomInto = riderHitInto; doomed = true;
        }

        /// <summary>Once the body is thrown after a brutal hit: the arm or leg nearest what it hit comes off.</summary>
        void Doom()
        {
            if (!doomed || ragdoll == null || !ragdoll.Active || !rider.Crashed) return;
            doomed = false;
            bool whole = doomInto >= SeverRiderWhole;
            Transform best = null; float bd = 1e9f;
            foreach (var b in new[] { rig.LowerL, rig.LowerR, rig.ShinL, rig.ShinR })
            {
                float d = (b.position - doomAt).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            if (best != null) Sever(whole ? UpperOf(best) : best, whole, doomN);
        }

        /// <summary>
        /// Read every skinned mesh's weights and triangles, and make the copy a cut is made on, ahead of time (when
        /// the ragdoll is built, at load): done at the first sever it cost ~6 ms in one frame; a sever is then ~1 ms.
        /// </summary>
        void PrepareSkins()
        {
            if (skins.Count > 0 || model == null) return;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                if (m == null || !m.isReadable || m.bindposes.Length == 0) continue;
                var s = new Skin { smr = smr, original = m, verts = m.vertices };
                s.bpv0 = m.GetBonesPerVertex().ToArray();
                s.w0 = m.GetAllBoneWeights().ToArray();
                s.tris0 = new int[m.subMeshCount][];
                for (int i = 0; i < m.subMeshCount; i++) s.tris0[i] = m.GetTriangles(i);
                s.work = Instantiate(m); s.work.name = m.name + " (cut)";
                skins.Add(s);
            }
        }

        /// <summary>Hook the ragdoll's hits and the fatal falls up to the limbs (once, when the ragdoll is built).</summary>
        void HookLimbs()
        {
            if (limbHooks || ragdoll == null) return;
            limbHooks = true;
            if (Limbs) PrepareSkins();
            ragdoll.Struck += OnLimbStruck;
            Killed += OnLimbFall;
        }

        void OnLimbStruck(Collider other, Vector3 at, Vector3 normal, float into, Transform bone)
        {
            if (!Limbs || rig == null || ragdoll == null || !ragdoll.Active || bone == null || !rider.Crashed) return;
            bool lower = bone == rig.LowerL || bone == rig.LowerR || bone == rig.ShinL || bone == rig.ShinR;
            bool upper = bone == rig.UpperL || bone == rig.UpperR || bone == rig.ThighL || bone == rig.ThighR;
            if (!lower && !upper) return;
            float need = ragdoll.IsSnow(other) ? SeverSnow : SeverObstacle;
            if (WasBroken(bone)) need *= BrokenEase;
            if (upper) need += UpperMore;
            if (into < need) return;
            Transform cut = bone; bool whole = upper;
            if (lower && into >= need + WholeMore) { cut = UpperOf(bone); whole = true; }
            Sever(cut, whole, normal);
        }

        void OnLimbFall(float fell)
        {
            if (!Limbs || rig == null || fell < SeverFall) return;
            var pick = fell >= SeverFall * 2f ? new[] { rig.UpperL, rig.UpperR, rig.ThighL, rig.ThighR } : new[] { rig.LowerL, rig.LowerR, rig.ShinL, rig.ShinR };
            Sever(pick[Random.Range(0, pick.Length)], fell >= SeverFall * 2f, Vector3.up);
        }

        bool WasBroken(Transform bone)
        {
            string limb = LimbOf(bone);
            foreach (var b in brokenThisCrash) if (LimbOf(b) == limb) return true;
            foreach (var b in bandaged) if (LimbOf(b) == limb) return true;
            return false;
        }

        Transform UpperOf(Transform b) => b == rig.LowerL ? rig.UpperL : b == rig.LowerR ? rig.UpperR : b == rig.ShinL ? rig.ThighL : b == rig.ShinR ? rig.ThighR : b;
        Transform ChildOf(Transform b) => b == rig.UpperL ? rig.LowerL : b == rig.LowerL ? rig.HandL : b == rig.UpperR ? rig.LowerR : b == rig.LowerR ? rig.HandR
                                        : b == rig.ThighL ? rig.ShinL : b == rig.ShinL ? rig.EndL : b == rig.ThighR ? rig.ShinR : b == rig.ShinR ? rig.EndR : null;

        string LimbName(Transform b)
        {
            bool left = b == rig.UpperL || b == rig.LowerL || b == rig.ThighL || b == rig.ShinL;
            string side = left ? "Left " : "Right ";
            if (b == rig.UpperL || b == rig.UpperR) return side + "arm";
            if (b == rig.ThighL || b == rig.ThighR) return side + "leg";
            if (b == rig.LowerL || b == rig.LowerR) return side + "forearm";
            return side + "lower leg";
        }

        /// <summary>Tear this limb off at this bone, now (the test tools call it directly). False if it can't come off.</summary>
        public bool Sever(Transform cut, bool whole, Vector3 normal)
        {
            int thisCrash = 0; foreach (var l in severed) if (l.piece == null) thisCrash++;
            if (!Limbs || ragdoll == null || !ragdoll.Active || cut == null || thisCrash >= MaxSevers || !ragdoll.Attached(cut)) return false;
            var child = ChildOf(cut);
            if (child == null) return false;
            var rb = cut.GetComponent<Rigidbody>();
            Vector3 joint = cut.position, vel = rb != null ? rb.linearVelocity : ragdoll.Velocity;
            // thrown out away from the body, off whatever it hit
            Vector3 outward = (cut.position - ragdoll.Centre); outward.y = Mathf.Max(0f, outward.y);
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Random.onUnitSphere;
            Vector3 kick = outward * Random.Range(3f, 5f) + normal.normalized * 2f + Vector3.up * 2.5f;
            if (!ragdoll.Sever(cut, kick)) return false;

            bool gore = blood != null && blood.On;
            var lost = new Lost { bone = cut, whole = whole, gore = gore };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            SplitSkins(cut, child, lost, gore);
            LastSeverMs = (float)clock.Elapsed.TotalMilliseconds;
            severed.Add(lost);
            if (!brokenThisCrash.Contains(cut)) brokenThisCrash.Add(cut);   // (no random break on top: this was the injury)
            Vector3 dir = (child.position - cut.position).normalized;
            fx?.Crack(joint);
            if (gore) blood.Sever(joint, -dir, vel);
            else { fx?.Puff(joint, 7f); fx?.Puff(joint + dir * 0.1f, 5f); }   // a clean pop, like a toy's
            LimbLost?.Invoke(LimbName(cut), joint, whole);
            return true;
        }

        // ---- the meshes

        void SplitSkins(Transform cut, Transform child, Lost lost, bool gore)
        {
            PrepareSkins();
            // what goes with it: the bone and everything under it, plus the foot of a leg (the previous rider's feet
            // are their own bones, carried on the shins) and the hand of an arm
            var set = new HashSet<Transform>(cut.GetComponentsInChildren<Transform>(true));
            bool leg = cut == rig.ThighL || cut == rig.ShinL || cut == rig.ThighR || cut == rig.ShinR;
            bool left = cut == rig.ThighL || cut == rig.ShinL || cut == rig.UpperL || cut == rig.LowerL;
            if (leg) set.Add(left ? rig.FootL : rig.FootR); else set.Add(left ? rig.HandL : rig.HandR);

            // the cap goes where the biggest hole is
            Skin best = null; int bestRing = 0; Vector3 bodyC = default, limbC = default; float bodyR = 0f, limbR = 0f; int bodyBone = -1, limbBone = -1; Material cloth = null;
            foreach (var s in skins)
            {
                if (s.bpv == null) { s.bpv = s.bpv0; s.w = s.w0; s.tris = s.tris0; }
                if (!Cut(s, set, out var ring, out var bc, out var lc, out var br, out var lr, out var bb, out var lb, out var mat, out var side)) continue;
                lost.side[s] = side;
                if (ring > bestRing) { bestRing = ring; best = s; bodyC = bc; limbC = lc; bodyR = br; limbR = lr; bodyBone = bb; limbBone = lb; cloth = mat; }
            }
            if (best == null) return;
            lost.cloth = cloth;
            var bones = best.smr.bones; var bind = best.original.bindposes;
            Vector3 axis = (limbC - bodyC).sqrMagnitude > 1e-10f ? (limbC - bodyC).normalized : Vector3.up;
            if (bodyBone >= 0 && bones[bodyBone] != null) lost.capBody = Cap(bones[bodyBone], bind[bodyBone], bodyC, axis, bodyR, gore, cloth);
            if (limbBone >= 0 && bones[limbBone] != null) lost.capLimb = Cap(bones[limbBone], bind[limbBone], limbC, -axis, limbR, gore, cloth);
        }

        /// <summary>
        /// Split one skinned mesh at the bones in `set`: reweight each vertex to the side holding most of it, drop
        /// the triangles across. Out: how many vertices ring the cut, the middle of each ring (mesh space), how far
        /// out they reach, the bone holding most of each ring, and the material most of the cut was.
        /// </summary>
        bool Cut(Skin s, HashSet<Transform> set, out int ring, out Vector3 bodyC, out Vector3 limbC, out float bodyR, out float limbR,
                 out int bodyBone, out int limbBone, out Material mat, out bool[] side)
        {
            side = null;
            ring = 0; bodyC = limbC = Vector3.zero; bodyR = limbR = 0f; bodyBone = limbBone = -1; mat = null;
            var bones = s.smr.bones;
            var inSet = new bool[bones.Length];
            bool any = false;
            for (int i = 0; i < bones.Length; i++) { inSet[i] = bones[i] != null && set.Contains(bones[i]); any |= inSet[i]; }
            if (!any) return false;
            int nv = s.bpv.Length;
            side = new bool[nv];
            int o = 0; bool anyLimb = false;
            for (int v = 0; v < nv; v++)
            {
                float k = 0f;
                for (int j = 0; j < s.bpv[v]; j++) if (inSet[s.w[o + j].boneIndex]) k += s.w[o + j].weight;
                side[v] = k >= 0.5f; anyLimb |= side[v];
                o += s.bpv[v];
            }
            if (!anyLimb) return false;
            // each vertex keeps only its own side's bones, renormalised (in the same order: heaviest first)
            var nb = new byte[nv]; var nw = new List<BoneWeight1>(s.w.Length);
            o = 0;
            for (int v = 0; v < nv; v++)
            {
                int c = s.bpv[v], start = nw.Count; float sum = 0f;
                for (int j = 0; j < c; j++) { var bw = s.w[o + j]; if (inSet[bw.boneIndex] == side[v]) { nw.Add(bw); sum += bw.weight; } }
                if (nw.Count == start || sum <= 1e-6f) { nw.RemoveRange(start, nw.Count - start); for (int j = 0; j < c; j++) nw.Add(s.w[o + j]); sum = 1f; }
                else for (int j = start; j < nw.Count; j++) { var bw = nw[j]; bw.weight /= sum; nw[j] = bw; }
                nb[v] = (byte)(nw.Count - start);
                o += c;
            }
            // triangles: kept if all three corners are on one side; the ones across ring the cut on both sides
            var ringBody = new HashSet<int>(); var ringLimb = new HashSet<int>();
            var subHits = new int[s.tris.Length];
            var newTris = new int[s.tris.Length][];
            for (int m = 0; m < s.tris.Length; m++)
            {
                var t = s.tris[m]; var keep = new List<int>(t.Length);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    if (side[a] == side[b] && side[b] == side[c]) { keep.Add(a); keep.Add(b); keep.Add(c); continue; }
                    subHits[m]++;
                    foreach (int x in new[] { a, b, c }) (side[x] ? ringLimb : ringBody).Add(x);
                }
                newTris[m] = keep.ToArray();
            }
            s.bpv = nb; s.w = nw.ToArray(); s.tris = newTris;
            if (s.work == null) { s.work = Instantiate(s.original); s.work.name = s.original.name + " (cut)"; }
            var na = new NativeArray<byte>(s.bpv, Allocator.Temp); var nwa = new NativeArray<BoneWeight1>(s.w, Allocator.Temp);
            s.work.SetBoneWeights(na, nwa);
            na.Dispose(); nwa.Dispose();
            for (int m = 0; m < s.tris.Length; m++) s.work.SetTriangles(s.tris[m], m, false);
            if (s.smr.sharedMesh != s.work) s.smr.sharedMesh = s.work;

            ring = ringBody.Count + ringLimb.Count;
            if (ringBody.Count == 0 || ringLimb.Count == 0) return true;
            Vector3 Mid(HashSet<int> r) { Vector3 p = Vector3.zero; foreach (int i in r) p += s.verts[i]; return p / r.Count; }
            bodyC = Mid(ringBody); limbC = Mid(ringLimb);
            Vector3 ax = (limbC - bodyC).normalized;
            float Reach(HashSet<int> r, Vector3 c) { float d = 0f; foreach (int i in r) d += Vector3.ProjectOnPlane(s.verts[i] - c, ax).magnitude; return d / r.Count; }
            bodyR = Reach(ringBody, bodyC); limbR = Reach(ringLimb, limbC);
            int Heaviest(HashSet<int> r)
            {
                var tally = new float[bones.Length];
                var starts = new int[nv]; int q = 0; for (int v = 0; v < nv; v++) { starts[v] = q; q += s.bpv[v]; }
                foreach (int v in r) for (int j = 0; j < s.bpv[v]; j++) tally[s.w[starts[v] + j].boneIndex] += s.w[starts[v] + j].weight;
                int best = -1; for (int i = 0; i < tally.Length; i++) if (tally[i] > 0f && (best < 0 || tally[i] > tally[best])) best = i;
                return best;
            }
            bodyBone = Heaviest(ringBody); limbBone = Heaviest(ringLimb);
            int sub = 0; for (int m = 1; m < subHits.Length; m++) if (subHits[m] > subHits[sub]) sub = m;
            var mats = s.smr.sharedMaterials; mat = sub < mats.Length ? mats[sub] : null;
            return true;
        }

        /// <summary>A cap over one end of the cut, on the bone that carries that end: centre and axis in mesh space.</summary>
        GameObject Cap(Transform bone, Matrix4x4 bind, Vector3 centre, Vector3 axis, float reach, bool gore, Material cloth)
        {
            if (domeMesh == null) domeMesh = Dome();
            if (fleshMat == null)
            {
                var sh = jacketShader != null ? jacketShader : Shader.Find("Universal Render Pipeline/Lit");
                fleshMat = new Material(sh) { name = "Stump" }; fleshMat.SetColor("_BaseColor", new Color(0.50f, 0.05f, 0.06f)); fleshMat.SetFloat("_Smoothness", 0.62f);
                boneMat = new Material(sh) { name = "StumpBone" }; boneMat.SetColor("_BaseColor", new Color(0.93f, 0.90f, 0.80f)); boneMat.SetFloat("_Smoothness", 0.35f);
            }
            Vector3 c = bind.MultiplyPoint3x4(centre), up = bind.MultiplyVector(axis);
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            float r = bind.MultiplyVector(side * reach).magnitude;   // as wide as the ring (its skirt hides the ragged edge)
            var go = new GameObject("Stump", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(bone, false);
            go.transform.localPosition = c;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up.normalized);
            go.transform.localScale = new Vector3(r, r * 0.32f, r);
            go.GetComponent<MeshFilter>().sharedMesh = domeMesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = gore || cloth == null ? fleshMat : Plain(cloth);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (gore)
            {
                // the end of the bone, sticking out of the middle
                var b = new GameObject("StumpBone", typeof(MeshFilter), typeof(MeshRenderer));
                b.transform.SetParent(go.transform, false);
                b.transform.localPosition = new Vector3(0.08f, 0.6f, -0.05f);
                b.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
                b.GetComponent<MeshFilter>().sharedMesh = domeMesh;
                var br = b.GetComponent<MeshRenderer>(); br.sharedMaterial = boneMat; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }

        readonly Dictionary<Material, Material> plain = new Dictionary<Material, Material>();

        /// <summary>
        /// The cloth's colour on a plain lit material, a shade darker (the inside of the sleeve): the snowy cloth
        /// shader reads the cap's missing snow channels as caked white.
        /// </summary>
        Material Plain(Material cloth)
        {
            if (plain.TryGetValue(cloth, out var m)) return m;
            var sh = jacketShader != null ? jacketShader : Shader.Find("Universal Render Pipeline/Lit");
            Color c = cloth.HasProperty("_BaseColor") ? cloth.GetColor("_BaseColor") : cloth.HasProperty("_Color") ? cloth.GetColor("_Color") : Color.grey;
            m = new Material(sh) { name = "StumpCloth" };
            m.SetColor("_BaseColor", new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, 1f));
            m.SetFloat("_Smoothness", 0.2f);
            plain[cloth] = m;
            return m;
        }

        /// <summary>A unit dome on +Y (rim at y=0, radius 1, top at y=1) with a short skirt down inside the rim.</summary>
        static Mesh Dome()
        {
            const int seg = 16, rings = 5;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            // skirt ring at y=-0.7, then rim, then up to the pole
            for (int r = -1; r <= rings; r++)
            {
                float th = r < 0 ? Mathf.PI * 0.5f : Mathf.PI * 0.5f * (1f - r / (float)rings);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    float rad = Mathf.Sin(th), y = r < 0 ? -0.7f : Mathf.Cos(th);
                    if (r == rings) rad = 0f;
                    // a lumpy wound, not a neat ball
                    float lump = r >= 0 && r < rings ? 1f + 0.08f * Mathf.Sin(a * 3f + r) : 1f;
                    v.Add(new Vector3(Mathf.Cos(a) * rad * lump, y, Mathf.Sin(a) * rad * lump));
                    n.Add(r < 0 ? new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) : new Vector3(Mathf.Cos(a) * Mathf.Sin(th), Mathf.Cos(th), Mathf.Sin(a) * Mathf.Sin(th)));
                }
            }
            int row = seg + 1;
            for (int r = 0; r < rings + 1; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a = r * row + i, b = a + 1, c = a + row, d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            var m = new Mesh { name = "StumpDome" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---- every frame: the wounds bleed for a while

        void LimbsStep(float dt)
        {
            if (doomed) Doom();
            FollowGore();
            if (severed.Count == 0 || dt <= 0f) return;
            foreach (var s in severed)
            {
                s.t += dt;
                if (!s.gore || blood == null || !blood.On || s.t > 4.5f) continue;
                // pumping, fading over a few seconds
                float beat = Mathf.Exp(-s.t * 0.55f) * (0.45f + 0.55f * Mathf.Max(0f, Mathf.Sin(s.t * Mathf.PI * 2f * 1.4f)));
                Bleed(s, s.capBody, beat, 2.2f, ref s.poolBody);
                Bleed(s, s.capLimb, beat * 0.5f, 1.2f, ref s.poolLimb);
            }
        }

        void Bleed(Lost s, GameObject cap, float beat, float speed, ref Vector3 lastPool)
        {
            if (cap == null) return;
            var t = cap.transform;
            var rb = t.GetComponentInParent<Rigidbody>();
            Vector3 vel = rb != null && !rb.isKinematic ? rb.linearVelocity : Vector3.zero;
            int n = Mathf.RoundToInt(beat * 2.4f + Random.value * 0.6f);
            blood.Gush(t.position + t.up * t.lossyScale.y * 0.5f, t.up, n, speed * (0.5f + beat), vel);
            // and it pools on the snow under the wound while it lies there
            Vector3 p = t.position;
            // (one per spot, a new one only once it has moved on, and a handful in all: the marks are a shared pool of 40)
            if (beat > 0.12f && s.pools < 8 && p.y - Slope.DrawnHeight(p.x, p.z) < 0.45f && vel.sqrMagnitude < 4f && (p - lastPool).sqrMagnitude > 0.25f)
            { lastPool = p; s.pools++; blood.Pool(p, 0.22f + 0.3f * beat); }
        }

        static void Kill(GameObject g) { if (g == null) return; if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }

        /// <summary>
        /// Getting up (call before the ragdoll ends, with the pieces still where they fell): every limb torn off in
        /// this crash is baked as it lies into a still object on the snow, with whatever was on it (glove, boot,
        /// bandage, its cap), and its triangles leave the rider. Its bones go home with the rest, with nothing on them.
        /// </summary>
        void DetachLimbs()
        {
            if (rig == null) return;
            HashSet<Transform> bones = null;
            foreach (var l in severed)
            {
                if (l.piece != null || l.bone == null) continue;
                var piece = new GameObject("LostLimb");
                l.piece = piece;
                // the skin that went with it, as it lies now
                foreach (var kv in l.side)
                {
                    Skin s = kv.Key; bool[] side = kv.Value;
                    if (s.smr == null || s.tris == null) continue;
                    var baked = new Mesh { name = "LostLimb" };
                    s.smr.BakeMesh(baked);
                    int subs = Mathf.Min(baked.subMeshCount, s.tris.Length);
                    bool any = false;
                    var body = new int[s.tris.Length][];
                    for (int m = 0; m < s.tris.Length; m++)
                    {
                        var t = s.tris[m]; var mine = new List<int>(); var rest = new List<int>(t.Length);
                        for (int i = 0; i + 2 < t.Length; i += 3)
                        {
                            bool on = side[t[i]] && side[t[i + 1]] && side[t[i + 2]];
                            var to = on ? mine : rest; to.Add(t[i]); to.Add(t[i + 1]); to.Add(t[i + 2]);
                        }
                        if (m < subs) baked.SetTriangles(mine, m, false);
                        any |= mine.Count > 0;
                        body[m] = rest.ToArray();
                    }
                    s.tris = body;
                    for (int m = 0; m < s.tris.Length; m++) s.work.SetTriangles(s.tris[m], m, false);
                    if (!any) { DestroyImmediate(baked); continue; }
                    baked = Compact(baked);
                    var g = new GameObject(s.smr.name, typeof(MeshFilter), typeof(MeshRenderer));
                    g.transform.SetParent(piece.transform, false);
                    g.transform.SetPositionAndRotation(s.smr.transform.position, s.smr.transform.rotation);
                    g.GetComponent<MeshFilter>().sharedMesh = baked;
                    var mr = g.GetComponent<MeshRenderer>();
                    mr.sharedMaterials = s.smr.sharedMaterials;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                // whatever hangs on its bones (glove, boot, bandage, the cap over its end): copied onto the piece,
                // and the original put away until the run is over
                if (bones == null) { bones = new HashSet<Transform>(); foreach (var s in skins) if (s.smr != null) foreach (var b in s.smr.bones) if (b != null) bones.Add(b); }
                var under = new HashSet<Transform>(l.bone.GetComponentsInChildren<Transform>(true));
                foreach (var t in under)
                {
                    if (t == null) continue;   // (inside something already copied and put away)
                    if (bones.Contains(t) || t.parent == null || !(under.Contains(t.parent) && bones.Contains(t.parent)) || !t.gameObject.activeSelf) continue;
                    if (t.GetComponentInChildren<Renderer>() == null) continue;
                    var copy = Instantiate(t.gameObject, t.position, t.rotation);
                    copy.transform.localScale = t.lossyScale;
                    copy.transform.SetParent(piece.transform, true);
                    bool cap = false;
                    foreach (var o in severed)
                    {
                        if (o.capLimb == t.gameObject) { o.capLimb = copy; cap = true; }
                        if (o.capBody == t.gameObject) { o.capBody = copy; cap = true; }
                    }
                    t.gameObject.SetActive(false);
                    if (cap) Kill(t.gameObject);              // a cap only ever belongs where it was cut
                    else hiddenOnLimb.Add(t.gameObject);      // the rest come back when the limb does
                }
            }
        }

        /// <summary>Only the vertices its triangles use (a baked piece starts out with the whole rider's).</summary>
        static Mesh Compact(Mesh m)
        {
            int subs = m.subMeshCount, n = m.vertexCount;
            var map = new int[n]; for (int i = 0; i < n; i++) map[i] = -1;
            var tris = new int[subs][]; int used = 0;
            for (int k = 0; k < subs; k++) { tris[k] = m.GetTriangles(k); foreach (int i in tris[k]) if (map[i] < 0) map[i] = used++; }
            var keep = new int[used]; for (int i = 0; i < n; i++) if (map[i] >= 0) keep[map[i]] = i;
            T[] Pick<T>(T[] a) { if (a == null || a.Length != n) return null; var o = new T[used]; for (int i = 0; i < used; i++) o[i] = a[keep[i]]; return o; }
            var c = new Mesh { name = m.name };
            c.vertices = Pick(m.vertices);
            var nr = Pick(m.normals); if (nr != null) c.normals = nr;
            var tg = Pick(m.tangents); if (tg != null) c.tangents = tg;
            var cl = Pick(m.colors); if (cl != null) c.colors = cl;
            var uv = new List<Vector4>();
            for (int ch = 0; ch < 4; ch++) { uv.Clear(); m.GetUVs(ch, uv); if (uv.Count == n) c.SetUVs(ch, Pick(uv.ToArray())); }
            c.subMeshCount = subs;
            for (int k = 0; k < subs; k++) { var t = tris[k]; for (int i = 0; i < t.Length; i++) t[i] = map[t[i]]; c.SetTriangles(t, k, false); }
            c.RecalculateBounds();
            Object.DestroyImmediate(m);
            return c;
        }

        /// <summary>Everything back together: the meshes whole, the stumps and lost pieces gone, every part back on the ragdoll (ResetVisual, with the ragdoll ended).</summary>
        void RestoreLimbs()
        {
            foreach (var s in severed) { Kill(s.capBody); Kill(s.capLimb); Kill(s.piece); }
            severed.Clear();
            foreach (var g in hiddenOnLimb) if (g != null) g.SetActive(true);   // (the old rider's glove, say; caps and bandages are gone already)
            hiddenOnLimb.Clear();
            foreach (var s in skins)
            {
                s.bpv = null; s.w = null; s.tris = null;
                if (s.smr != null && s.smr.sharedMesh != s.original) s.smr.sharedMesh = s.original;
            }
            ragdoll?.Reattach();
        }

        // ---- gore switched in the pause menu mid-run: stumps and bandages follow it

        bool goreShown = true;

        void FollowGore()
        {
            bool on = blood != null && blood.On;
            if (on == goreShown) return;
            goreShown = on;
            foreach (var l in severed)
                foreach (var cap in new[] { l.capBody, l.capLimb })
                {
                    if (cap == null) continue;
                    var mr = cap.GetComponent<MeshRenderer>();
                    if (mr != null) mr.sharedMaterial = on || l.cloth == null ? fleshMat : Plain(l.cloth);
                    var bn = cap.transform.Find("StumpBone");
                    if (bn != null) bn.gameObject.SetActive(on);
                    else if (on && fleshMat != null)
                    {
                        // a cap made clean has no bone end: add one
                        var b = new GameObject("StumpBone", typeof(MeshFilter), typeof(MeshRenderer));
                        b.transform.SetParent(cap.transform, false);
                        b.transform.localPosition = new Vector3(0.08f, 0.6f, -0.05f);
                        b.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
                        b.GetComponent<MeshFilter>().sharedMesh = domeMesh;
                        var br = b.GetComponent<MeshRenderer>(); br.sharedMaterial = boneMat; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }
            RestainBandages(on);
        }

        // ---- how thick a limb is, for the bandages round it

        readonly Dictionary<Transform, float> limbRadius = new Dictionary<Transform, float>();

        /// <summary>
        /// How far out the clothes reach round this bone (world metres): the outer tenth of the vertices it carries
        /// most of, measured from the line to its child, between its two ends. 0 if no mesh says.
        /// </summary>
        float LimbRadius(Transform bone, Transform child)
        {
            if (limbRadius.TryGetValue(bone, out float got)) return got;
            PrepareSkins();
            Vector3 axisL = bone.InverseTransformPoint(child.position);
            float len = axisL.magnitude; Vector3 ax = len > 1e-6f ? axisL / len : Vector3.up;
            var d = new List<float>();
            foreach (var s in skins)
            {
                var bs = s.smr.bones; int bi = System.Array.IndexOf(bs, bone);
                if (bi < 0) continue;
                var bind = s.original.bindposes[bi];
                int o = 0;
                for (int v = 0; v < s.bpv0.Length; v++)
                {
                    float w = 0f;
                    for (int j = 0; j < s.bpv0[v]; j++) if (s.w0[o + j].boneIndex == bi) w += s.w0[o + j].weight;
                    o += s.bpv0[v];
                    if (w < 0.5f) continue;
                    Vector3 p = bind.MultiplyPoint3x4(s.verts[v]);
                    float along = Vector3.Dot(p, ax);
                    if (along < len * 0.1f || along > len * 0.9f) continue;
                    d.Add((p - ax * along).magnitude);
                }
            }
            float r = 0f;
            if (d.Count > 8) { d.Sort(); r = d[Mathf.Min(d.Count - 1, (int)(d.Count * 0.9f))] * Mathf.Abs(bone.lossyScale.x); }
            limbRadius[bone] = r;
            return r;
        }
    }
}
