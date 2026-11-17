using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Everything you see of the rider: the board, the person on it, the groove left
    /// in the snow and the powder thrown off the edge. Reads the physics after it has
    /// been interpolated for this frame, so it never stutters.
    /// Uses a humanoid model from Resources/Rider when there is one, posed into a
    /// snowboard stance through Unity's rig-independent muscle space; otherwise a
    /// figure made from simple shapes.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class RiderBody : MonoBehaviour
    {
        public Rider rider;
        Transform pivot, roll, upper, trailAnchor;
        SnowTrail trail;
        ParticleSystem spray;
        ParticleSystem.EmissionModule sprayEm, wakeEm;
        ParticleSystem wake;
        Vector3 normal = Vector3.up;
        float lean, lastTravel, yawRate, crashSpin;
        Vector3 crashAxis;

        // humanoid
        Animator anim;
        HumanPoseHandler poser;
        HumanPose pose;
        Transform model, footL, footR, thighL, shinL, thighR, shinR;
        int mKneeL = -1, mKneeR, mHipL, mHipR, mSpreadL, mSpreadR, mSpine, mChest, mArmL, mArmR, mArmFL, mArmFR, mElbowL, mElbowR, mHead, mNeck, mTwist;
        const float BoardTop = 0.05f;
        SnowMarks marks;
        Transform shield;
        Material shieldMat;
        float comp, compV, shieldFlash, shieldShown;
        Shader jacketShader;

        public void Build(Rider r, Material jacket, Material pants, Material board, Material skin, Material dark,
                          Material trailMat, Material sprayMat)
        {
            rider = r;
            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(transform, false);
            roll = new GameObject("Roll").transform;
            roll.SetParent(pivot, false);

            // the board: a deck with kicked-up nose and tail
            Part(PrimitiveType.Cube, roll, new Vector3(0f, 0.025f, 0f), new Vector3(0.29f, 0.03f, 1.30f), Vector3.zero, board);
            Part(PrimitiveType.Cylinder, roll, new Vector3(0f, 0.025f, 0.65f), new Vector3(0.29f, 0.015f, 0.29f), Vector3.zero, board);
            Part(PrimitiveType.Cylinder, roll, new Vector3(0f, 0.025f, -0.65f), new Vector3(0.29f, 0.015f, 0.29f), Vector3.zero, board);
            Part(PrimitiveType.Cube, roll, new Vector3(0f, 0.06f, 0.83f), new Vector3(0.27f, 0.025f, 0.16f), new Vector3(-28f, 0f, 0f), board);
            Part(PrimitiveType.Cube, roll, new Vector3(0f, 0.06f, -0.83f), new Vector3(0.27f, 0.025f, 0.16f), new Vector3(28f, 0f, 0f), board);
            // bindings
            Part(PrimitiveType.Cube, roll, new Vector3(0f, 0.07f, 0.27f), new Vector3(0.22f, 0.06f, 0.13f), new Vector3(0f, 12f, 0f), dark);
            Part(PrimitiveType.Cube, roll, new Vector3(0f, 0.07f, -0.27f), new Vector3(0.22f, 0.06f, 0.13f), new Vector3(0f, -12f, 0f), dark);

            upper = new GameObject("Body").transform;
            upper.SetParent(roll, false);
            var prefab = Resources.Load<GameObject>("Rider/Character");
            jacketShader = jacket != null ? jacket.shader : null;
            if (prefab != null && (SetUpHumanoid(prefab) || SetUpRig(prefab))) { }
            else ShapesFigure(jacket, pants, skin, dark);

            // a groove pressed into the snow behind the board
            trail = new SnowTrail(jacket != null ? jacket.shader : Shader.Find("Universal Render Pipeline/Lit"));

            // powder off the edge
            var sg = new GameObject("Spray");
            sg.transform.SetParent(pivot, false);
            sg.transform.localPosition = new Vector3(0f, 0.08f, -0.45f);
            sg.transform.localRotation = Quaternion.Euler(-30f, 180f, 0f);
            spray = sg.AddComponent<ParticleSystem>();
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = spray.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.45f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.gravityModifier = 0.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            main.playOnAwake = false;
            var shape = spray.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.15f;
            var col = spray.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = spray.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.9f));
            sprayEm = spray.emission;
            sprayEm.rateOverTime = 0f;
            var pr = sg.GetComponent<ParticleSystemRenderer>();
            pr.material = sprayMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            spray.Play();

            // a wake of kicked-up powder that hangs in the air behind you
            var wg = new GameObject("Wake");
            wg.transform.SetParent(pivot, false);
            wg.transform.localPosition = new Vector3(0f, 0.15f, -0.7f);
            wake = wg.AddComponent<ParticleSystem>();
            wake.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var wm = wake.main;
            wm.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.0f);
            wm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            wm.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            wm.startColor = new Color(1f, 1f, 1f, 0.55f);
            wm.gravityModifier = -0.02f;
            wm.simulationSpace = ParticleSystemSimulationSpace.World;
            wm.maxParticles = 600;
            wm.playOnAwake = false;
            var wsh = wake.shape; wsh.shapeType = ParticleSystemShapeType.Sphere; wsh.radius = 0.25f;
            var wc = wake.colorOverLifetime; wc.enabled = true;
            var wgd = new Gradient();
            wgd.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0.25f, 0.5f), new GradientAlphaKey(0f, 1f) });
            wc.color = wgd;
            var wsz = wake.sizeOverLifetime; wsz.enabled = true;
            wsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 2.4f));
            wakeEm = wake.emission; wakeEm.rateOverTime = 0f;
            var wr = wg.GetComponent<ParticleSystemRenderer>();
            wr.material = sprayMat;
            wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wake.Play();

            marks = new SnowMarks(transform.parent, Imprint(trailMat, CraterTexture()));
            BuildShield(trailMat);
            rider.Landed += impact =>
            {
                if (impact > 5f) spray.Emit(Mathf.Clamp((int)(impact * 2.5f), 8, 50));
                comp = Mathf.Min(0.6f, comp + impact * 0.035f);
                if (impact > 3f) marks.Add(transform.position + Vector3.down * Rider.R, normal, 0.9f + impact * 0.06f, rider.Heading);
            };
            rider.ShieldHit += () => { shieldFlash = 1f; spray.Emit(40); };
            rider.Crash += _ =>
            {
                spray.Emit(70);
                crashSpin = 0f;
                marks.Add(transform.position + Vector3.down * Rider.R, normal, 2.2f, Random.value * 6f);
                crashAxis = new Vector3(Random.Range(-1f, 1f), 0.2f, Random.Range(0.4f, 1f)).normalized;
            };
            rider.Scraped += _ => spray.Emit(25);
        }

        // ---------------------------------------------------------------- the person

        bool SetUpHumanoid(GameObject prefab)
        {
            var go = Instantiate(prefab, upper, false);
            go.name = "Character";
            anim = go.GetComponentInChildren<Animator>();
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman) { DestroyImmediate(go); anim = null; return false; }
            anim.enabled = false;   // we pose it ourselves
            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            model = go.transform;
            var tex = Resources.Load<Texture2D>("Rider/Outfit") ?? Resources.Load<Texture2D>("Rider/ClothedLightSkin");
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (tex != null)
                {
                    var mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mm = new Material(jacketShader != null ? jacketShader : mats[i].shader);
                        mm.SetTexture("_BaseMap", tex);
                        mm.SetColor("_BaseColor", Color.white);
                        mm.SetFloat("_Smoothness", 0.2f);
                        mats[i] = mm;
                    }
                    smr.sharedMaterials = mats;
                }
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // stand side-on across the board, left foot forward: turn the model until its
            // left hip points at the nose (+Z), whichever way the file was authored
            var hipL = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var hipR = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (hipL != null && hipR != null)
            {
                Vector3 left = upper.InverseTransformDirection(hipL.position - hipR.position);
                left.y = 0f;
                if (left.sqrMagnitude > 1e-6f)
                    model.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(left, Vector3.forward, Vector3.up), 0f) * model.localRotation;
            }
            // scale to about 1.75 m tall
            float h = HeightOf(go);
            if (h > 0.01f) model.localScale *= 1.75f / h;

            Gear(anim.GetBoneTransform(HumanBodyBones.Head), anim.GetBoneTransform(HumanBodyBones.LeftHand), anim.GetBoneTransform(HumanBodyBones.RightHand),
                 anim.GetBoneTransform(HumanBodyBones.LeftUpperArm), anim.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                 anim.GetBoneTransform(HumanBodyBones.RightUpperArm), anim.GetBoneTransform(HumanBodyBones.RightLowerArm), true);
            poser = new HumanPoseHandler(anim.avatar, anim.transform);
            poser.GetHumanPose(ref pose);
            footL = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            footR = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            thighL = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); shinL = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            thighR = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg); shinR = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);

            string[] names = HumanTrait.MuscleName;
            int Find(string n) => System.Array.IndexOf(names, n);
            mKneeL = Find("Left Lower Leg Stretch"); mKneeR = Find("Right Lower Leg Stretch");
            mHipL = Find("Left Upper Leg Front-Back"); mHipR = Find("Right Upper Leg Front-Back");
            mSpreadL = Find("Left Upper Leg In-Out"); mSpreadR = Find("Right Upper Leg In-Out");
            mSpine = Find("Spine Front-Back"); mChest = Find("Chest Front-Back");
            mArmL = Find("Left Arm Down-Up"); mArmR = Find("Right Arm Down-Up");
            mArmFL = Find("Left Arm Front-Back"); mArmFR = Find("Right Arm Front-Back");
            mElbowL = Find("Left Forearm Stretch"); mElbowR = Find("Right Forearm Stretch");
            mHead = Find("Head Left-Right"); mNeck = Find("Neck Left-Right");
            mTwist = Find("Spine Twist Left-Right");
            return true;
        }

        RiderRig rig;

        /// <summary>A Quaternius-style model whose feet are separate from its legs: posed by RiderRig.</summary>
        bool SetUpRig(GameObject prefab)
        {
            var go = Instantiate(prefab, upper, false);
            go.name = "Character";
            var r = new RiderRig(go.transform);
            if (!r.Ok) { DestroyImmediate(go); return false; }
            foreach (var a in go.GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            model = go.transform;
            // winter colours: a red shell jacket, charcoal snow pants, dark pack
            Shader sh = jacketShader != null ? jacketShader : Shader.Find("Universal Render Pipeline/Lit");
            foreach (var smr in go.GetComponentsInChildren<Renderer>())
            {
                var mats = smr.sharedMaterials;
                bool pack = smr.name.Contains("Backpack");
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = new Material(sh);
                    Color c = mats[i].color;
                    string n = mats[i].name;
                    if (pack) c = n.StartsWith("Gold") ? c : n.Contains("Green") ? new Color(0.15f, 0.17f, 0.2f) : new Color(0.08f, 0.08f, 0.1f);
                    else if (n.StartsWith("Green")) c = new Color(0.82f, 0.18f, 0.10f);
                    else if (n.StartsWith("LightGreen")) c = new Color(0.95f, 0.30f, 0.16f);
                    else if (n.StartsWith("Brown2")) c = new Color(0.12f, 0.13f, 0.17f);
                    else if (n.StartsWith("Brown")) c = new Color(0.17f, 0.19f, 0.24f);
                    m.SetColor("_BaseColor", c);
                    m.SetFloat("_Smoothness", n.StartsWith("Skin") || n.StartsWith("Hair") ? 0.25f : 0.35f);
                    mats[i] = m;
                }
                smr.sharedMaterials = mats;
                if (smr is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // side-on, left foot forward, about 1.75 m tall
            Vector3 left = upper.InverseTransformDirection(r.ThighL.position - r.ThighR.position); left.y = 0f;
            if (left.sqrMagnitude > 1e-8f)
                model.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(left, Vector3.forward, Vector3.up), 0f) * model.localRotation;
            float h = HeightOf(go);
            if (h > 0.01f) model.localScale *= 1.75f / h;
            r.CaptureBind();
            Gear(r.Head, r.HandL, r.HandR, r.UpperL, r.LowerL, r.UpperR, r.LowerR, false);
            rig = r;
            return true;
        }

        void PoseRig(float dt)
        {
            float t = Time.time;
            compV += (-comp * 140f - compV * 16f) * dt;
            comp = Mathf.Clamp(comp + compV * dt, -0.15f, 0.6f);
            bool idle = !rider.Running, crashed = rider.Crashed, air = rider.Airborne;
            bool grab = air && rider.TrickGrab > 0 && rider.TrickProgress > 0.1f && rider.TrickProgress < 0.95f;
            bool flip = air && Mathf.Abs(rider.TrickFlip) > 0f;
            float toe = Mathf.Clamp(rider.Lean / Rider.MaxLean, -1f, 1f);
            float chatterV = idle || air ? 0f : (Mathf.PerlinNoise(t * 9f, 1.3f) - 0.5f) * 0.1f * Mathf.Clamp01(rider.Speed / 30f);
            float crouch = idle ? 0.15f + 0.04f * Mathf.Sin(t * 2.1f) : 0.45f + 0.35f * rider.Tuck + comp + Mathf.Abs(toe) * 0.2f + chatterV;
            if (grab || flip) crouch = 1f;
            Vector3 bl = roll.TransformPoint(new Vector3(0f, BoardTop + 0.09f, 0.27f));
            Vector3 br = roll.TransformPoint(new Vector3(0f, BoardTop + 0.09f, -0.27f));
            rig.Pose(roll, bl, br, dt, crouch, toe, rider.Tuck, grab, flip, idle, crashed, t);
        }

        static float HeightOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.size.y / Mathf.Max(1e-4f, go.transform.lossyScale.y) * go.transform.localScale.y;
        }

        // ---- gear: a helmet, goggles, jacket sleeves and gloves, fixed to the bones so they move with them

        void Gear(Transform headBone, Transform handL, Transform handR, Transform uL, Transform lL, Transform uR, Transform lR, bool sleeves)
        {
            Shader sh = jacketShader != null ? jacketShader : Shader.Find("Universal Render Pipeline/Lit");
            Material M(Color c, float smooth, Color emit = default)
            {
                var m = new Material(sh);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                if (emit.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit); }
                return m;
            }
            var jacketM = M(new Color(0.93f, 0.31f, 0.14f), 0.35f);
            var gloveM = M(new Color(0.09f, 0.10f, 0.12f), 0.3f);
            var lensM = M(new Color(1f, 0.55f, 0.1f), 0.95f, new Color(0.35f, 0.15f, 0f));
            var strapM = M(new Color(0.1f, 0.1f, 0.12f), 0.2f);

            if (sleeves)
            {
                Limb(uL, lL, 0.075f, jacketM); Limb(lL, handL, 0.065f, jacketM);
                Limb(uR, lR, 0.075f, jacketM); Limb(lR, handR, 0.065f, jacketM);
            }
            else
            {
                // jacket cuffs over the forearms
                Limb(lL, handL, 0.06f, jacketM); Limb(lR, handR, 0.06f, jacketM);
            }
            Blob(handL, Vector3.zero, new Vector3(0.10f, 0.10f, 0.10f), gloveM, PrimitiveType.Sphere);
            Blob(handR, Vector3.zero, new Vector3(0.10f, 0.10f, 0.10f), gloveM, PrimitiveType.Sphere);

            var beanieM = M(new Color(0.08f, 0.55f, 0.62f), 0.1f);
            var pomM = M(new Color(0.97f, 0.97f, 0.97f), 0.05f);
            var frameM = M(new Color(0.06f, 0.06f, 0.07f), 0.4f);
            var head = headBone;
            if (head != null)
            {
                Vector3 fwd = upper.TransformDirection(Vector3.right);   // the rider faces the toe edge
                Vector3 up = upper.up;
                // knit beanie with a rolled cuff and a pompom
                Blob(head, up * 0.15f - fwd * 0.01f, new Vector3(0.245f, 0.22f, 0.25f), beanieM, PrimitiveType.Sphere);
                Blob(head, up * 0.115f - fwd * 0.005f, new Vector3(0.255f, 0.035f, 0.26f), beanieM, PrimitiveType.Cylinder);
                Blob(head, up * 0.27f - fwd * 0.01f, new Vector3(0.09f, 0.09f, 0.09f), pomM, PrimitiveType.Sphere);
                // big ski goggles over the eyes: a mirrored lens in a black frame, strap round the back
                Blob(head, up * 0.075f + fwd * 0.118f, new Vector3(0.06f, 0.095f, 0.235f), frameM, PrimitiveType.Cube, fwd);
                Blob(head, up * 0.075f + fwd * 0.135f, new Vector3(0.035f, 0.075f, 0.215f), lensM, PrimitiveType.Cube, fwd);
                Blob(head, up * 0.075f, new Vector3(0.235f, 0.025f, 0.24f), strapM, PrimitiveType.Cylinder);
            }
        }

        /// <summary>A capsule along a bone, sized in metres, that moves with the bone.</summary>
        void Limb(Transform from, Transform to, float radius, Material m)
        {
            if (from == null || to == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            Vector3 a = from.position, b = to.position;
            float len = Vector3.Distance(a, b);
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, (b - a).normalized));
            go.transform.localScale = new Vector3(radius * 2f, len * 0.5f + radius, radius * 2f);
            go.transform.SetParent(from, true);
        }

        void Blob(Transform bone, Vector3 offset, Vector3 size, Material m, PrimitiveType type, Vector3 face = default)
        {
            if (bone == null) return;
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            Quaternion rot = face.sqrMagnitude > 0f ? Quaternion.LookRotation(Vector3.ProjectOnPlane(upper.forward, upper.up), upper.up) : upper.rotation;
            go.transform.SetPositionAndRotation(bone.position + offset, rot);
            go.transform.localScale = size;
            go.transform.SetParent(bone, true);
        }

        // ---- animation: a live pose built every frame from what the rider is doing

        float[] want, rest;
        float chatter;

        void Pose(float dt)
        {
            if (poser == null) return;
            float[] m = pose.muscles;
            if (want == null) { want = new float[m.Length]; rest = (float[])m.Clone(); }
            System.Array.Copy(rest, want, m.Length);
            float t = Time.time;
            // landings squash the legs, which spring back
            compV += (-comp * 140f - compV * 16f) * dt;
            comp = Mathf.Clamp(comp + compV * dt, -0.15f, 0.6f);

            float tuck = rider.Tuck, carve = rider.Carve, steer = rider.Steer, speed01 = Mathf.Clamp01(rider.Speed / 30f);
            bool idle = !rider.Running, crashed = rider.Crashed, air = rider.Airborne;
            bool grab = air && rider.TrickGrab > 0 && rider.TrickProgress > 0.1f && rider.TrickProgress < 0.95f;
            bool flip = air && Mathf.Abs(rider.TrickFlip) > 0f;
            // the board chatters a little at speed: tiny fast flex in the knees
            chatter = idle || air ? 0f : (Mathf.PerlinNoise(t * 9f, 1.3f) - 0.5f) * 0.08f * speed01;
            float toe = Mathf.Clamp(steer, -1f, 1f);   // + = toe-side (right) turn, - = heel-side
            float crouch = idle ? 0.25f + 0.03f * Mathf.Sin(t * 2.1f)
                         : Mathf.Lerp(0.5f, 0.9f, tuck) + comp + Mathf.Abs(toe) * 0.18f + chatter;
            if (grab || flip) crouch = 1f;

            // legs: bent and loaded; toe-side drives the knees forward, heel-side sits the hips back
            W(mKneeL, Mathf.Lerp(1f, -0.95f, crouch)); W(mKneeR, Mathf.Lerp(1f, -0.95f, crouch));
            W(mHipL, Mathf.Lerp(0.05f, 0.9f, crouch) - toe * 0.15f); W(mHipR, Mathf.Lerp(0.05f, 0.9f, crouch) - toe * 0.15f);
            W(mSpreadL, 0.12f); W(mSpreadR, 0.12f);
            // body: upright and open to the hill when cruising, folded when tucked, leaning into toe turns
            W(mSpine, Mathf.Lerp(0.05f, 0.5f, tuck) + Mathf.Max(0f, toe) * 0.35f - Mathf.Max(0f, -toe) * 0.1f + comp * 0.4f);
            W(mChest, Mathf.Lerp(0.0f, 0.3f, tuck) + toe * 0.12f);
            // shoulders turn into the next turn before the board does
            W(mTwist, 0.3f + toe * 0.25f);
            // arms: lead arm out over the nose, rear arm low and relaxed; both drop in a tuck
            float sway = Mathf.Sin(t * 1.4f) * 0.04f;
            W(mArmL, Mathf.Lerp(0.15f, -0.55f, tuck) + carve * 0.2f + sway);
            W(mArmFL, Mathf.Lerp(0.55f, 0.5f, tuck) - toe * 0.25f);
            W(mElbowL, 0.6f);
            W(mArmR, Mathf.Lerp(-0.35f, -0.6f, tuck) + Mathf.Max(0f, toe) * 0.5f - sway);
            W(mArmFR, Mathf.Lerp(-0.1f, 0.45f, tuck) + toe * 0.3f);
            W(mElbowR, 0.4f);
            // head: eyes down the hill over the front shoulder; looking around while waiting
            W(mNeck, 0.55f);
            W(mHead, idle ? 0.35f + 0.35f * Mathf.Sin(t * 0.5f) : 0.65f + toe * 0.1f);
            if (air)
            {
                // arms out wide for balance; a grab on longer air
                W(mArmL, 0.45f); W(mArmR, 0.3f); W(mArmFL, 0.2f); W(mArmFR, -0.2f);
                if (Mathf.Abs(rider.TrickSpin) > 0.3f && rider.TrickProgress < 0.15f) { W(mTwist, 0.3f - Mathf.Sign(rider.TrickSpin) * 0.6f); }   // wind up against the spin
                if (flip) { W(mKneeL, -1f); W(mKneeR, -1f); W(mHipL, 1f); W(mHipR, 1f); }
                if (grab) { W(mArmR, -0.95f); W(mArmFR, 0.75f); W(mElbowR, 0.9f); }
            }
            if (crashed)
            {
                W(mArmL, Mathf.Sin(t * 9f) * 0.9f); W(mArmR, Mathf.Sin(t * 8f + 1f) * 0.9f);
                W(mArmFL, Mathf.Sin(t * 7f) * 0.8f); W(mArmFR, Mathf.Sin(t * 6f + 2f) * 0.8f);
                W(mKneeL, Mathf.Sin(t * 5f) * 0.6f); W(mKneeR, Mathf.Sin(t * 6f + 1f) * 0.6f);
            }
            // ease every muscle toward its target, so nothing snaps
            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * (crashed ? 20f : 11f));
            for (int i = 0; i < m.Length; i++) m[i] = Mathf.Lerp(m[i], want[i], k);
            poser.SetHumanPose(ref pose);
            PlantFeet();
            if (!crashed)
            {
                // lock each foot onto its binding (left foot forward, at the nose end)
                Vector3 tl = roll.TransformPoint(new Vector3(0f, BoardTop + 0.09f, 0.27f));
                Vector3 tr = roll.TransformPoint(new Vector3(0f, BoardTop + 0.09f, -0.27f));
                // if a binding is out of reach, sink the hips until both feet can get there
                float shortL = Reach(thighL, shinL, footL, tl), shortR = Reach(thighR, shinR, footR, tr);
                float sink = Mathf.Max(0f, Mathf.Max(shortL, shortR) + 0.02f);
                if (sink > 0f) model.position -= roll.up * sink;
                LegIK(thighL, shinL, footL, tl);
                LegIK(thighR, shinR, footR, tr);
            }
        }

        void W(int i, float v) { if (i >= 0 && i < want.Length) want[i] = Mathf.Clamp(v, -1f, 1f); }

        /// <summary>How far the target is beyond the leg's full stretch (negative when reachable).</summary>
        static float Reach(Transform hip, Transform knee, Transform foot, Vector3 target)
        {
            if (hip == null || knee == null || foot == null) return 0f;
            float len = Vector3.Distance(hip.position, knee.position) + Vector3.Distance(knee.position, foot.position);
            return Vector3.Distance(hip.position, target) - len * 0.97f;
        }

        /// <summary>Two-bone IK: bend the knee to the right reach, then swing the thigh so the foot lands on the target.</summary>
        static void LegIK(Transform hip, Transform knee, Transform foot, Vector3 target)
        {
            if (hip == null || knee == null || foot == null) return;
            Vector3 h = hip.position, k = knee.position, f = foot.position;
            float a = Vector3.Distance(h, k), b = Vector3.Distance(k, f);
            float c = Mathf.Clamp(Vector3.Distance(h, target), Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            Vector3 kh = h - k, kf = f - k;
            float cur = Vector3.Angle(kh, kf);
            float want = Mathf.Acos(Mathf.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 axis = Vector3.Cross(kf, kh);
            if (axis.sqrMagnitude > 1e-8f)
                knee.rotation = Quaternion.AngleAxis(cur - want, axis.normalized) * knee.rotation;
            hip.rotation = Quaternion.FromToRotation(foot.position - h, target - h) * hip.rotation;
        }

        /// <summary>Move the model so its feet stand on the bindings, centred on the board.</summary>
        void PlantFeet()
        {
            if (footL == null || footR == null) return;
            Vector3 l = roll.InverseTransformPoint(footL.position), r = roll.InverseTransformPoint(footR.position);
            Vector3 mid = (l + r) * 0.5f;
            Vector3 want = new Vector3(0f, BoardTop + 0.07f + (mid.y - Mathf.Min(l.y, r.y)), 0f);
            model.position += roll.TransformVector(want - mid);
        }

        static void Set(float[] m, int i, float v) { if (i >= 0 && i < m.Length) m[i] = Mathf.Clamp(v, -1f, 1f); }

        /// <summary>A lit, see-through copy of a particle material with the given texture.</summary>
        static Material Imprint(Material basis, Texture2D tex)
        {
            // the base material lives in Resources so the lit particle shader is part of the build
            var baseMat = Resources.Load<Material>("ImprintBase");
            var sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
            var m = baseMat != null ? new Material(baseMat) : sh != null ? new Material(sh) : new Material(basis);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_Smoothness", 0.1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 2600;
            return m;
        }

        /// <summary>Across the track: bright packed lips at the edges, a shaded groove between.</summary>
        static Texture2D GrooveTexture()
        {
            const int n = 32;
            var t = new Texture2D(4, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n, e = Mathf.Abs(v - 0.5f) * 2f;   // 0 centre, 1 edge
                Color groove = new Color(0.50f, 0.60f, 0.86f), lip = new Color(1.08f, 1.08f, 1.1f);
                Color c = e < 0.62f ? Color.Lerp(groove * 0.92f, groove, e / 0.62f) : Color.Lerp(groove, lip, Mathf.Clamp01((e - 0.62f) / 0.2f));
                c.a = e < 0.82f ? 0.85f : Mathf.Lerp(0.85f, 0f, (e - 0.82f) / 0.18f);
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, c);
            }
            t.Apply();
            return t;
        }

        /// <summary>A round dent: a shaded hollow with a bright rim of thrown-up snow.</summary>
        static Texture2D CraterTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy * 1.8f) * (0.9f + 0.1f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f));
                    Color hollow = new Color(0.60f, 0.68f, 0.88f), rim = new Color(1.06f, 1.06f, 1.08f);
                    Color c = r < 0.6f ? hollow : Color.Lerp(hollow, rim, Mathf.Clamp01((r - 0.6f) / 0.2f));
                    c.a = r < 0.8f ? 0.8f : Mathf.Clamp01(1f - (r - 0.8f) / 0.2f) * 0.8f;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        void BuildShield(Material basis)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "ShieldBubble";
            shield = go.transform;
            shield.SetParent(transform, false);
            shield.localPosition = new Vector3(0f, 0.55f, 0f);
            shieldMat = new Material(basis);
            shieldMat.SetColor("_BaseColor", new Color(0.4f, 0.75f, 1f, 0f));
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = shieldMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
        }

        void ShapesFigure(Material jacket, Material pants, Material skin, Material dark)
        {
            Part(PrimitiveType.Capsule, upper, new Vector3(0f, 0.42f, 0.24f), new Vector3(0.17f, 0.38f, 0.17f), Vector3.zero, pants);
            Part(PrimitiveType.Capsule, upper, new Vector3(0f, 0.42f, -0.24f), new Vector3(0.17f, 0.38f, 0.17f), Vector3.zero, pants);
            Part(PrimitiveType.Capsule, upper, new Vector3(0f, 1.02f, 0f), new Vector3(0.28f, 0.36f, 0.40f), Vector3.zero, jacket);
            Part(PrimitiveType.Capsule, upper, new Vector3(0.04f, 1.12f, 0.40f), new Vector3(0.12f, 0.24f, 0.12f), new Vector3(70f, 0f, 0f), jacket);
            Part(PrimitiveType.Capsule, upper, new Vector3(0.04f, 1.12f, -0.40f), new Vector3(0.12f, 0.24f, 0.12f), new Vector3(-70f, 0f, 0f), jacket);
            Part(PrimitiveType.Sphere, upper, new Vector3(0f, 1.54f, 0f), new Vector3(0.27f, 0.29f, 0.27f), Vector3.zero, skin);
            Part(PrimitiveType.Sphere, upper, new Vector3(-0.01f, 1.60f, 0f), new Vector3(0.29f, 0.24f, 0.29f), Vector3.zero, jacket);
            Part(PrimitiveType.Cube, upper, new Vector3(0.12f, 1.55f, 0f), new Vector3(0.07f, 0.08f, 0.22f), Vector3.zero, dark);
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        // ---------------------------------------------------------------- per frame

        public void ResetVisual()
        {
            normal = Slope.Normal(rider.transform.position.x, rider.transform.position.z);
            lean = 0f; yawRate = 0f; lastTravel = rider.Heading;
            pivot.localRotation = Quaternion.identity;
            trail.Clear();
            spray.Clear();
            marks.Clear();
            comp = 0f; compV = 0f; shieldFlash = 0f;
            Apply(0f);
        }

        void LateUpdate() { Apply(Time.deltaTime); }

        /// <summary>Draw the rider for this frame (public so the test harness can call it).</summary>
        public void Apply(float dt)
        {
            if (rider == null) return;
            Vector3 p = rider.transform.position;
            pivot.localPosition = Vector3.down * Rider.R;

            // tilt with the snow, blending to level as you leave it
            Vector3 n = Vector3.Slerp(Slope.Normal(p.x, p.z), Vector3.up, Mathf.Clamp01(rider.AboveGround / 1.5f));
            normal = dt <= 0f ? n : Vector3.Slerp(normal, n, 1f - Mathf.Exp(-dt * 14f));

            if (rider.Crashed)
            {
                crashSpin += dt;
                pivot.Rotate(crashAxis, 520f * dt * Mathf.Clamp01(1.2f - crashSpin), Space.World);
                if (poser != null) Pose(dt);
                else if (rig != null) PoseRig(dt);
            }
            else
            {
                float yaw = rider.Heading;
                Vector3 up = normal;
                if (rider.Grinding)
                {
                    // 50-50 along a box; across the rail for a boardslide
                    var fd = rider.GrindFeature.Dir;
                    yaw = Mathf.Atan2(fd.x, fd.z) + (rider.GrindFeature.kind == Course.Kind.Rail ? Mathf.PI * 0.5f : 0f);
                    up = Vector3.up;
                }
                var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                Vector3 fp = Vector3.ProjectOnPlane(f, up);
                Quaternion baseRot = fp.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fp.normalized, up) : pivot.rotation;
                // an automatic trick: spin about the body's up axis and flip about the board, round the middle of the body
                Quaternion rot = baseRot;
                if (rider.Airborne && rider.TrickName != null)
                {
                    float k = rider.TrickProgress;
                    rot = baseRot * Quaternion.AngleAxis(rider.TrickSpin * k * Mathf.Rad2Deg, Vector3.up)
                                  * Quaternion.AngleAxis(rider.TrickFlip * k * Mathf.Rad2Deg, Vector3.forward);
                }
                Vector3 mid = new Vector3(0f, 0.9f, 0f);
                pivot.rotation = rot;
                pivot.position = p + Vector3.down * Rider.R + baseRot * mid - rot * mid;

                // the lean IS the steering readout: the body is on exactly the edge the thumb asked for
                float leanDeg = rider.Lean * Mathf.Rad2Deg;
                roll.localRotation = Quaternion.Euler(0f, 0f, -leanDeg * 0.75f);
                upper.localRotation = Quaternion.Euler(0f, 0f, -leanDeg * 0.25f);
                if (poser != null) Pose(dt);
                else if (rig != null) PoseRig(dt);
                else upper.localScale = new Vector3(1f, 1f - 0.24f * rider.Tuck, 1f);
            }

            // the track: a groove pressed into the snow under the board
            trail.Feed(p + Vector3.down * Rider.R, normal, rider.Heading, rider.Running && !rider.Crashed && !rider.Airborne && !rider.Grinding,
                       Course.At(p.z) == Course.Section.Pipe ? 0.6f : Mathf.Lerp(0.9f, 1.25f, Slope.Rough(p.x, p.z)), rider.Carve);

            sprayEm.rateOverTime = rider.Running && !rider.Crashed && !rider.Airborne
                ? rider.Carve * rider.Speed * 7f + Mathf.Max(0f, rider.Speed - 22f) * 1.5f + (rider.BoostTime > 0f ? 60f : 0f)
                : 0f;

            wakeEm.rateOverTime = rider.Running && !rider.Crashed && !rider.Airborne ? Mathf.Clamp(rider.Speed * 2.2f, 0f, 80f) : 0f;

            // shield bubble: fuller with each shield held, flashes when it saves you
            shieldFlash = Mathf.MoveTowards(shieldFlash, 0f, dt * 2f);
            shieldShown = Mathf.Lerp(shieldShown, rider.Shields, 1f - Mathf.Exp(-dt * 6f));
            float a = Mathf.Clamp01(shieldShown) * (0.10f + 0.05f * shieldShown) + shieldFlash * 0.5f;
            bool show = a > 0.01f;
            if (shield.gameObject.activeSelf != show) shield.gameObject.SetActive(show);
            if (show)
            {
                float pulse = 1f + 0.03f * Mathf.Sin(Time.time * 5f) + shieldFlash * 0.25f;
                shield.localScale = Vector3.one * 2.3f * pulse;
                shieldMat.SetColor("_BaseColor", new Color(0.45f, 0.78f, 1f, a));
            }
        }
    }
}
