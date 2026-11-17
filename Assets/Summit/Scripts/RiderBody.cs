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
        TrailRenderer trail;
        ParticleSystem spray;
        ParticleSystem.EmissionModule sprayEm;
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
            if (prefab != null && SetUpHumanoid(prefab)) { }
            else ShapesFigure(jacket, pants, skin, dark);

            // a groove in the snow behind the board
            trailAnchor = new GameObject("Trail").transform;
            trailAnchor.SetParent(transform, false);
            trail = trailAnchor.gameObject.AddComponent<TrailRenderer>();
            trail.alignment = LineAlignment.TransformZ;
            trail.time = 30f;
            trail.minVertexDistance = 0.25f;
            trail.widthMultiplier = 0.5f;
            trail.material = Imprint(trailMat, GrooveTexture());
            trail.textureMode = LineTextureMode.Stretch;
            trail.generateLightingData = true;
            var tg = new Gradient();
            tg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = tg;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = true;
            trail.numCapVertices = 0;

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

            Gear();
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

        static float HeightOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.size.y / Mathf.Max(1e-4f, go.transform.lossyScale.y) * go.transform.localScale.y;
        }

        // ---- gear: a helmet, goggles, jacket sleeves and gloves, fixed to the bones so they move with them

        void Gear()
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
            var helmetM = M(new Color(0.96f, 0.97f, 1f), 0.75f);
            var gloveM = M(new Color(0.09f, 0.10f, 0.12f), 0.3f);
            var lensM = M(new Color(1f, 0.55f, 0.1f), 0.95f, new Color(0.35f, 0.15f, 0f));
            var strapM = M(new Color(0.1f, 0.1f, 0.12f), 0.2f);

            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            Limb(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), 0.075f, jacketM);
            Limb(B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), 0.065f, jacketM);
            Limb(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), 0.075f, jacketM);
            Limb(B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), 0.065f, jacketM);
            Blob(B(HumanBodyBones.LeftHand), Vector3.zero, new Vector3(0.10f, 0.10f, 0.10f), gloveM, PrimitiveType.Sphere);
            Blob(B(HumanBodyBones.RightHand), Vector3.zero, new Vector3(0.10f, 0.10f, 0.10f), gloveM, PrimitiveType.Sphere);

            var head = B(HumanBodyBones.Head);
            if (head != null)
            {
                Vector3 fwd = upper.TransformDirection(Vector3.right);   // the rider faces the toe edge
                Vector3 up = upper.up;
                Blob(head, up * 0.13f - fwd * 0.01f, new Vector3(0.27f, 0.24f, 0.29f), helmetM, PrimitiveType.Sphere);
                Blob(head, up * 0.09f + fwd * 0.115f, new Vector3(0.07f, 0.075f, 0.21f), lensM, PrimitiveType.Cube, fwd);
                Blob(head, up * 0.09f, new Vector3(0.245f, 0.03f, 0.25f), strapM, PrimitiveType.Cylinder);
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

        void Pose(float dt)
        {
            if (poser == null) return;
            float[] m = pose.muscles;
            float t = Time.time;
            // landings squash the legs, which spring back
            compV += (-comp * 140f - compV * 16f) * dt;
            comp = Mathf.Clamp(comp + compV * dt, -0.15f, 0.6f);

            float tuck = rider.Tuck, carve = rider.Carve, steer = rider.Steer;
            bool idle = !rider.Running, crashed = rider.Crashed;
            bool grab = rider.Airborne && rider.AirTime > 0.3f;
            float crouch = idle ? 0.22f + 0.03f * Mathf.Sin(t * 2.1f)
                         : Mathf.Lerp(0.48f, 0.9f, tuck) + comp + carve * 0.12f;
            if (grab) crouch = Mathf.Max(crouch, 0.95f);
            // toe-side turns (right) drive the knees and chest forward; heel-side sits back
            float toe = Mathf.Clamp(steer, -1f, 1f);
            Set(m, mKneeL, Mathf.Lerp(1f, -0.95f, crouch));
            Set(m, mKneeR, Mathf.Lerp(1f, -0.95f, crouch));
            Set(m, mHipL, Mathf.Lerp(0f, 0.85f, crouch) - toe * 0.1f);
            Set(m, mHipR, Mathf.Lerp(0f, 0.85f, crouch) - toe * 0.1f);
            Set(m, mSpreadL, 0.15f);
            Set(m, mSpreadR, 0.15f);
            Set(m, mSpine, Mathf.Lerp(0.1f, 0.55f, tuck) + toe * 0.25f + comp * 0.4f);
            Set(m, mChest, Mathf.Lerp(0.05f, 0.35f, tuck) + toe * 0.15f);
            // arms: out for balance, swinging against the turn; tucked when racing; one reaches for the board in the air
            float sway = Mathf.Sin(t * 1.3f) * 0.05f;
            Set(m, mArmL, Mathf.Lerp(-0.05f, -0.6f, tuck) + carve * 0.25f + sway);
            Set(m, mArmR, Mathf.Lerp(-0.05f, -0.6f, tuck) + carve * 0.25f - sway);
            Set(m, mArmFL, Mathf.Lerp(0.2f, 0.55f, tuck) - toe * 0.35f);
            Set(m, mArmFR, Mathf.Lerp(0.2f, 0.55f, tuck) + toe * 0.35f);
            Set(m, mElbowL, 0.35f);
            Set(m, mElbowR, 0.35f);
            if (grab) { Set(m, mArmR, -0.95f); Set(m, mArmFR, 0.75f); Set(m, mElbowR, 0.9f); Set(m, mArmL, 0.35f); }
            // look down the hill over the front shoulder; glance around while waiting
            Set(m, mTwist, 0.35f);
            Set(m, mNeck, 0.6f);
            Set(m, mHead, idle ? 0.4f + 0.35f * Mathf.Sin(t * 0.5f) : 0.7f);
            if (crashed)
            {
                // flailing tumble
                Set(m, mArmL, Mathf.Sin(t * 9f) * 0.9f); Set(m, mArmR, Mathf.Sin(t * 8f + 1f) * 0.9f);
                Set(m, mArmFL, Mathf.Sin(t * 7f) * 0.8f); Set(m, mArmFR, Mathf.Sin(t * 6f + 2f) * 0.8f);
                Set(m, mKneeL, Mathf.Sin(t * 5f) * 0.6f); Set(m, mKneeR, Mathf.Sin(t * 6f + 1f) * 0.6f);
            }
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
                Color groove = new Color(0.62f, 0.70f, 0.88f), lip = new Color(1.06f, 1.06f, 1.08f);
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
            }
            else
            {
                rider.SpinVisual = Mathf.Lerp(rider.SpinVisual, 0f, 1f - Mathf.Exp(-dt * 10f));
                float yaw = rider.Heading + rider.Spin + rider.SpinVisual;
                var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                Vector3 fp = Vector3.ProjectOnPlane(f, normal);
                if (fp.sqrMagnitude > 1e-4f) pivot.rotation = Quaternion.LookRotation(fp.normalized, normal);

                // lean into the turn by the angle that balances it against gravity
                float th = rider.TravelHeading;
                float rate = dt > 0f ? Mathf.DeltaAngle(lastTravel * Mathf.Rad2Deg, th * Mathf.Rad2Deg) * Mathf.Deg2Rad / dt : 0f;
                lastTravel = th;
                yawRate = Mathf.Lerp(yawRate, rate, 1f - Mathf.Exp(-dt * 10f));
                float want = rider.Airborne ? 0f : Mathf.Clamp(Mathf.Atan2(rider.Speed * yawRate, 9.81f) * Mathf.Rad2Deg, -45f, 45f);
                lean = Mathf.Lerp(lean, want, 1f - Mathf.Exp(-dt * 14f));
                roll.localRotation = Quaternion.Euler(0f, 0f, -lean * 0.5f);
                upper.localRotation = Quaternion.Euler(0f, 0f, -lean * 0.4f);
                if (poser != null) Pose(dt);
                else upper.localScale = new Vector3(1f, 1f - 0.24f * rider.Tuck, 1f);
            }

            // the groove lies flat on the snow just above it
            var tf = new Vector3(Mathf.Sin(rider.Heading), 0f, Mathf.Cos(rider.Heading));
            trailAnchor.position = p + Vector3.down * Rider.R + normal * 0.03f;
            trailAnchor.rotation = Quaternion.LookRotation(normal, Vector3.ProjectOnPlane(tf, normal) + Vector3.forward * 1e-4f);
            trail.emitting = rider.Running && !rider.Crashed && rider.AboveGround < 0.12f;

            sprayEm.rateOverTime = rider.Running && !rider.Crashed && !rider.Airborne
                ? rider.Carve * rider.Speed * 7f + Mathf.Max(0f, rider.Speed - 22f) * 1.5f + (rider.BoostTime > 0f ? 60f : 0f)
                : 0f;

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
