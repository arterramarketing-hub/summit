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
        Transform model, footL, footR;
        int mKneeL = -1, mKneeR, mHipL, mHipR, mSpreadL, mSpreadR, mSpine, mChest, mArmL, mArmR, mArmFL, mArmFR, mElbowL, mElbowR, mHead, mNeck, mTwist;
        const float BoardTop = 0.05f;
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
            trail.time = 12f;
            trail.minVertexDistance = 0.3f;
            trail.widthMultiplier = 0.34f;
            trail.material = trailMat;
            trail.startColor = new Color(0.56f, 0.65f, 0.83f, 0.8f);
            trail.endColor = new Color(0.70f, 0.78f, 0.92f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
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

            rider.Landed += impact => { if (impact > 5f) spray.Emit(Mathf.Clamp((int)(impact * 2.5f), 8, 50)); };
            rider.Crash += _ =>
            {
                spray.Emit(70);
                crashSpin = 0f;
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
            var tex = Resources.Load<Texture2D>("Rider/ClothedLightSkin");
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

            poser = new HumanPoseHandler(anim.avatar, anim.transform);
            poser.GetHumanPose(ref pose);
            footL = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            footR = anim.GetBoneTransform(HumanBodyBones.RightFoot);

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

        void Pose(float tuck, float carve, float air)
        {
            if (poser == null) return;
            float[] m = pose.muscles;
            float crouch = Mathf.Lerp(0.5f, 0.9f, tuck) * (1f - 0.25f * air);
            Set(m, mKneeL, Mathf.Lerp(1f, -0.9f, crouch));
            Set(m, mKneeR, Mathf.Lerp(1f, -0.9f, crouch));
            Set(m, mHipL, Mathf.Lerp(0f, 0.8f, crouch));
            Set(m, mHipR, Mathf.Lerp(0f, 0.8f, crouch));
            Set(m, mSpreadL, 0.15f);
            Set(m, mSpreadR, 0.15f);
            Set(m, mSpine, Mathf.Lerp(0.1f, 0.6f, tuck));
            Set(m, mChest, Mathf.Lerp(0.05f, 0.4f, tuck));
            // arms out for balance; tucked in when racing
            Set(m, mArmL, Mathf.Lerp(-0.1f, -0.55f, tuck) + carve * 0.2f);
            Set(m, mArmR, Mathf.Lerp(-0.1f, -0.55f, tuck) + carve * 0.2f);
            Set(m, mArmFL, Mathf.Lerp(0.2f, 0.5f, tuck));
            Set(m, mArmFR, Mathf.Lerp(0.2f, 0.5f, tuck));
            Set(m, mElbowL, 0.3f);
            Set(m, mElbowR, 0.3f);
            // look where you are going: down the board, over the front (left) shoulder
            Set(m, mTwist, 0.35f);
            Set(m, mNeck, 0.6f);
            Set(m, mHead, 0.7f);
            poser.SetHumanPose(ref pose);

            PlantFeet();
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
                if (poser != null) Pose(rider.Tuck, rider.Carve, rider.Airborne ? 1f : 0f);
                else upper.localScale = new Vector3(1f, 1f - 0.24f * rider.Tuck, 1f);
            }

            // the groove lies flat on the snow just above it
            var tf = new Vector3(Mathf.Sin(rider.Heading), 0f, Mathf.Cos(rider.Heading));
            trailAnchor.position = p + Vector3.down * Rider.R + normal * 0.03f;
            trailAnchor.rotation = Quaternion.LookRotation(normal, Vector3.ProjectOnPlane(tf, normal) + Vector3.forward * 1e-4f);
            trail.emitting = rider.Running && !rider.Crashed && rider.AboveGround < 0.12f;

            sprayEm.rateOverTime = rider.Running && !rider.Crashed && !rider.Airborne
                ? rider.Carve * rider.Speed * 7f + Mathf.Max(0f, rider.Speed - 22f) * 1.5f
                : 0f;
        }
    }
}
