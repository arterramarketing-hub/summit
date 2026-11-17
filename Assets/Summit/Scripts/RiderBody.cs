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
    public partial class RiderBody : MonoBehaviour
    {
        public Rider rider;
        Transform pivot, roll, upper, trailAnchor;
        SnowTrail trail;
        ParticleSystem spray;
        ParticleSystem.EmissionModule sprayEm, wakeEm;
        ParticleSystem wake;
        Vector3 normal = Vector3.up;
        float lean, lastTravel, yawRate, crashSpin;
        Transform boardRoot;
        ParticleSystem bow;
        ParticleSystem.EmissionModule bowEm;
        Ragdoll ragdoll;
        bool ragdollPending;
        Vector3 lastVel;
        /// <summary>The thrown body or board hit something: impact speed, and whether it was the board.</summary>
        public System.Action<float, bool> RagdollHit;
        /// <summary>A bone gave way: which limb ("Left arm", "Ribs", ...), and where.</summary>
        public System.Action<string, Vector3> BoneBroke;
        public SnowFX fx;
        readonly System.Collections.Generic.List<Transform> brokenThisCrash = new System.Collections.Generic.List<Transform>();
        readonly System.Collections.Generic.HashSet<Transform> bandaged = new System.Collections.Generic.HashSet<Transform>();
        readonly System.Collections.Generic.List<GameObject> bandages = new System.Collections.Generic.List<GameObject>();
        Material bandageMat, bandageLine;
        float crashClock, stillFor, visYaw, nextDragCrater;
        SnowTrail bodyTrail;
        Craters craters;
        /// <summary>Where the thrown body is (its pelvis).</summary>
        public Vector3 RagdollCentre => ragdoll != null && ragdoll.Active ? ragdoll.Centre : transform.position;
        /// <summary>How far the thrown body is below the snow surface (in a crevasse), metres.</summary>
        public float RagdollBelowSnow { get; private set; }
        /// <summary>The thrown body is still tumbling or sliding.</summary>
        public bool RagdollMoving { get; private set; }
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
        /// <summary>Snow caked on the jacket and trousers after a fall (null on a model without them).</summary>
        public SnowyClothes snowy;
        /// <summary>Getting up after a fall: where the body came to rest.</summary>
        public System.Action<Vector3> FallRested;

        public void Build(Rider r, Material jacket, Material pants, Material board, Material skin, Material dark,
                          Material trailMat, Material sprayMat)
        {
            rider = r;
            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(transform, false);
            roll = new GameObject("Roll").transform;
            roll.SetParent(pivot, false);

            // the board: a deck with kicked-up nose and tail (its own object, so a crash can throw it away)
            boardRoot = new GameObject("Board").transform;
            boardRoot.SetParent(roll, false);
            Part(PrimitiveType.Cube, boardRoot, new Vector3(0f, 0.025f, 0f), new Vector3(0.29f, 0.03f, 1.30f), Vector3.zero, board);
            Part(PrimitiveType.Cylinder, boardRoot, new Vector3(0f, 0.025f, 0.65f), new Vector3(0.29f, 0.015f, 0.29f), Vector3.zero, board);
            Part(PrimitiveType.Cylinder, boardRoot, new Vector3(0f, 0.025f, -0.65f), new Vector3(0.29f, 0.015f, 0.29f), Vector3.zero, board);
            Part(PrimitiveType.Cube, boardRoot, new Vector3(0f, 0.06f, 0.83f), new Vector3(0.27f, 0.025f, 0.16f), new Vector3(-28f, 0f, 0f), board);
            Part(PrimitiveType.Cube, boardRoot, new Vector3(0f, 0.06f, -0.83f), new Vector3(0.27f, 0.025f, 0.16f), new Vector3(28f, 0f, 0f), board);
            // bindings
            Part(PrimitiveType.Cube, boardRoot, new Vector3(0f, 0.07f, 0.27f), new Vector3(0.22f, 0.06f, 0.13f), new Vector3(0f, 12f, 0f), dark);
            Part(PrimitiveType.Cube, boardRoot, new Vector3(0f, 0.07f, -0.27f), new Vector3(0.22f, 0.06f, 0.13f), new Vector3(0f, -12f, 0f), dark);

            BuildTube(board, dark);

            upper = new GameObject("Body").transform;
            upper.SetParent(roll, false);
            var prefab = Resources.Load<GameObject>("Rider/Character");
            jacketShader = jacket != null ? jacket.shader : null;
            if (prefab != null && (SetUpHumanoid(prefab) || SetUpRig(prefab))) { }
            else ShapesFigure(jacket, pants, skin, dark);
            // what the outfit colours paint (see RiderBodyWalk)
            FindJacket(upper.gameObject); boardMat = board; if (jacket != null) { jacketMats.Add(jacket); jacketShade.Add(1f); }

            // a groove pressed into the snow behind the board
            trail = new SnowTrail(jacket != null ? jacket.shader : Shader.Find("Universal Render Pipeline/Lit"));
            // the furrow a tumbling body ploughs through the snow
            bodyTrail = new SnowTrail(jacket != null ? jacket.shader : Shader.Find("Universal Render Pipeline/Lit"));
            craters = new Craters(jacket != null ? jacket.shader : Shader.Find("Universal Render Pipeline/Lit"));

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

            // the nose ploughing through the snow: a bow wave of powder thrown up and out either side
            var bg = new GameObject("Bow");
            bg.transform.SetParent(roll, false);
            bg.transform.localPosition = new Vector3(0f, 0.04f, 0.72f);
            bg.transform.localRotation = Quaternion.Euler(-62f, 0f, 0f);   // mostly up, leaning forward
            bow = bg.AddComponent<ParticleSystem>();
            bow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var bm = bow.main;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.75f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            bm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
            bm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.80f, 0.86f, 0.96f, 0.95f));   // some puffs in shade, so it reads against the snow
            bm.gravityModifier = 0.9f;
            bm.simulationSpace = ParticleSystemSimulationSpace.World;
            bm.maxParticles = 700;
            bm.playOnAwake = false;
            var bsh = bow.shape;
            bsh.shapeType = ParticleSystemShapeType.Cone;
            bsh.angle = 55f;          // a wide fan: out to both sides of the nose
            bsh.radius = 0.12f;
            bsh.radiusThickness = 0.2f;
            var biv = bow.inheritVelocity;   // the snow starts moving with the board, then falls behind
            biv.enabled = true; biv.mode = ParticleSystemInheritVelocityMode.Initial; biv.curve = 0.55f;
            var bcol = bow.colorOverLifetime; bcol.enabled = true;
            var bgd = new Gradient();
            bgd.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            bcol.color = bgd;
            var bsz = bow.sizeOverLifetime; bsz.enabled = true;
            bsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.8f));
            bowEm = bow.emission; bowEm.rateOverTime = 0f;
            var bpr = bg.GetComponent<ParticleSystemRenderer>();
            bpr.material = sprayMat;
            bpr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bow.Play();

            marks = new SnowMarks(transform.parent, Imprint(trailMat, CraterTexture()), 80);
            BuildShield(trailMat);
            rider.Landed += impact =>
            {
                if (impact > 5f) spray.Emit(Mathf.Clamp((int)(impact * 2.5f), 8, 50));
                comp = Mathf.Min(0.6f, comp + impact * 0.035f);
                if (impact > 3f) craters.Add(transform.position + Vector3.down * Rider.R, rider.Velocity, 0.45f + impact * 0.035f, Mathf.Min(0.22f, 0.04f + impact * 0.012f));
                if (impact > 2.5f) fx?.Landing(transform.position + Vector3.down * Rider.R, normal, impact, rider.Velocity);
            };
            rider.ShieldHit += () => { shieldFlash = 1f; spray.Emit(40); };
            rider.Crash += why =>
            {
                spray.Emit(70);
                if (why.StartsWith("Crevasse")) lastVel *= 0.4f;   // a drop, not a throw
                crashSpin = 0f;
                ragdollPending = rig != null;   // thrown on the next physics step, not inside the collision
                craters.Add(transform.position + Vector3.down * Rider.R, lastVel, 1.0f, 0.2f);
                crashAxis = new Vector3(Random.Range(-1f, 1f), 0.2f, Random.Range(0.4f, 1f)).normalized;
            };
            rider.Scraped += _ => spray.Emit(25);
            rider.HitObstacle += OnRiderHitObstacle; rider.Crash += OnCrashBleed;   // blood on a hard hit (RiderBodyBlood)
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
            snowy = new SnowyClothes();
            if (!snowy.Ok) snowy = null;
            foreach (var smr in go.GetComponentsInChildren<Renderer>())
            {
                var mats = smr.sharedMaterials;
                bool pack = smr.name.Contains("Backpack");
                bool clothes = smr is SkinnedMeshRenderer && (smr.name.Contains("Body") || smr.name.Contains("Legs"));
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
                    // the jacket and trousers can be caked in snow (the skin, hair and pack stay as they are)
                    if (clothes && snowy != null && !pack && (n.StartsWith("Green") || n.StartsWith("LightGreen") || n.StartsWith("Brown")))
                    { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); m = snowy.Make(c, 0.35f); }
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
            snowy?.SetFrame(model, r.BodyBone != null ? r.BodyBone : r.Hips);
            Gear(r.Head, r.HandL, r.HandR, r.UpperL, r.LowerL, r.UpperR, r.LowerR, false);
            snowy?.BakeRigid(go.transform);
            Loosen(go);
            rig = r;
            return true;
        }

        // ---- baggy clothes: the jacket and pants are loosened, and given shapes the wind can blow into

        readonly System.Collections.Generic.List<SkinnedMeshRenderer> cloth = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        float billow, flapPhase;

        void Loosen(GameObject go)
        {
            // the wind comes from the nose of the board and streams out past the tail
            Vector3 windWorld = roll.TransformDirection(Vector3.back);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                bool jacket = smr.name.Contains("Body"), pants = smr.name.Contains("Legs");
                if (!jacket && !pants) continue;
                var src = smr.sharedMesh;
                if (src == null || !src.isReadable) continue;
                var mesh = Instantiate(src);
                mesh.name = src.name + " (loose)";
                float m = 1f / Mathf.Max(1e-5f, smr.transform.lossyScale.x);   // metres to mesh units
                Vector3 wind = smr.transform.InverseTransformDirection(windWorld).normalized;
                var v = mesh.vertices; var n = mesh.normals;
                if (n == null || n.Length != v.Length) { mesh.RecalculateNormals(); n = mesh.normals; }
                // vertices split at seams share a position: give them one averaged normal, or pushing them
                // out would open cracks along every seam
                var sum = new System.Collections.Generic.Dictionary<Vector3Int, Vector3>();
                Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
                for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); sum[k] = (sum.TryGetValue(k, out var acc) ? acc : Vector3.zero) + n[i]; }
                var sn = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) sn[i] = sum[Key(v[i])].normalized;
                n = sn;
                var billowD = new Vector3[v.Length]; var flapA = new Vector3[v.Length]; var flapB = new Vector3[v.Length];
                float baggy = jacket ? 0.022f : 0.018f;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 pm = v[i] / m;   // position in metres, for noise
                    float loose = 0.55f + 0.45f * Mathf.PerlinNoise(pm.x * 4f + pm.y * 3f + 11f, pm.z * 4f - pm.y * 2f + 5f);
                    // looser all over: puffed out along the surface
                    v[i] += n[i] * (baggy * loose * m);
                    // the side away from the wind balloons out and streams back; the windward side stays pressed flat
                    float lee = Mathf.Max(0f, Vector3.Dot(n[i], wind));
                    billowD[i] = (n[i] * 0.035f * lee + wind * 0.05f * (0.3f + 0.7f * lee)) * loose * m;
                    float ra = Mathf.PerlinNoise(pm.x * 7f + pm.z * 3f, pm.y * 7f + 2f) - 0.5f;
                    float rb = Mathf.PerlinNoise(pm.y * 6f + 31f, pm.z * 6f + pm.x * 2f + 17f) - 0.5f;
                    float flutter = (0.3f + 0.7f * lee) * 0.03f * loose * m;
                    flapA[i] = n[i] * ra * 2f * flutter + wind * ra * flutter * 0.5f;
                    flapB[i] = n[i] * rb * 2f * flutter - wind * rb * flutter * 0.5f;
                }
                mesh.vertices = v;
                mesh.AddBlendShapeFrame("Billow", 100f, billowD, null, null);
                mesh.AddBlendShapeFrame("FlapA", 100f, flapA, null, null);
                mesh.AddBlendShapeFrame("FlapB", 100f, flapB, null, null);
                mesh.RecalculateBounds();
                snowy?.Bake(smr, mesh);
                smr.sharedMesh = mesh;
                cloth.Add(smr);
            }
        }

        /// <summary>Clothes in the wind: they balloon with speed and flap faster the faster you go.</summary>
        void Flutter(float dt)
        {
            if (cloth.Count == 0) return;
            float s = Mathf.Clamp01(rider.Speed / 30f) * (rider.Crashed ? 0.3f : 1f) + (rider.Airborne ? 0.15f : 0f);
            billow = Mathf.Lerp(billow, Mathf.Clamp01(s), 1f - Mathf.Exp(-dt * 3f));
            flapPhase += dt * Mathf.PI * 2f * (3f + 9f * billow);
            float amp = 100f * billow;
            float a = amp * (0.5f + 0.5f * Mathf.Sin(flapPhase)), b = amp * (0.5f + 0.5f * Mathf.Sin(flapPhase * 1.37f + 1.3f));
            foreach (var smr in cloth)
            {
                if (smr == null) continue;
                smr.SetBlendShapeWeight(0, 100f * billow);
                smr.SetBlendShapeWeight(1, a);
                smr.SetBlendShapeWeight(2, b);
            }
        }

        // ---- the snow tube: an inflatable ring with two handles, the rider sat in it facing down the hill

        Transform tubeRoot;
        /// <summary>The one ragdoll, built once on the skeleton at rest (switched on at a crash).</summary>
        void MakeRagdoll()
        {
            if (ragdoll != null || rig == null) return;
            ragdoll = new Ragdoll(rig, boardRoot, rider.GetComponent<Collider>());
            ragdoll.Hit += OnRagdollHit;
            ragdoll.Drag += OnRagdollDrag;
            ragdoll.Ploughed += (bone, down, v) => snowy?.Plough(down, v, Time.fixedDeltaTime);   // snow packed onto the clothes
            ragdoll.Struck += OnRagdollStruck; ragdoll.Ploughed += BleedAlong;   // blood on a hard hit (RiderBodyBlood)
            ragdoll.Prebuild();
        }
        float tubeSpin, tubeSpinRate;
        public bool Tube { get; private set; }

        void BuildTube(Material accent, Material dark)
        {
            tubeRoot = new GameObject("Tube").transform;
            tubeRoot.SetParent(roll, false);
            var sh = accent != null ? accent.shader : Shader.Find("Universal Render Pipeline/Lit");
            var rubber = new Material(sh); rubber.SetColor("_BaseColor", new Color(0.95f, 0.22f, 0.12f)); rubber.SetFloat("_Smoothness", 0.55f);
            var band = new Material(sh); band.SetColor("_BaseColor", new Color(0.98f, 0.97f, 0.94f)); band.SetFloat("_Smoothness", 0.4f);
            var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(tubeRoot, false);
            ring.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            ring.GetComponent<MeshFilter>().sharedMesh = Torus(0.62f, 0.22f, 40, 16);
            ring.GetComponent<MeshRenderer>().sharedMaterial = rubber;
            var stripe = new GameObject("Stripe", typeof(MeshFilter), typeof(MeshRenderer));
            stripe.transform.SetParent(tubeRoot, false);
            stripe.transform.localPosition = new Vector3(0f, 0.43f, 0f);
            stripe.GetComponent<MeshFilter>().sharedMesh = Torus(0.62f, 0.035f, 40, 8);
            stripe.GetComponent<MeshRenderer>().sharedMaterial = band;
            // handles either side of the rider (the rider faces +X here, so the sides are +-Z)
            for (int s = -1; s <= 1; s += 2)
                Part(PrimitiveType.Cylinder, tubeRoot, new Vector3(-0.05f, 0.47f, s * 0.6f), new Vector3(0.05f, 0.14f, 0.05f), new Vector3(0f, 0f, 90f), dark);
            tubeRoot.gameObject.SetActive(false);
        }

        static Mesh Torus(float R, float r, int seg, int side)
        {
            var v = new Vector3[(seg + 1) * (side + 1)]; var t = new int[seg * side * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= side; j++)
                {
                    float b = j / (float)side * Mathf.PI * 2f;
                    v[i * (side + 1) + j] = c * (R + r * Mathf.Cos(b)) + Vector3.up * r * Mathf.Sin(b);
                }
            }
            int k = 0;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a = i * (side + 1) + j, b = a + 1, c = a + side + 1, d = c + 1;
                    t[k++] = a; t[k++] = b; t[k++] = c; t[k++] = b; t[k++] = d; t[k++] = c;
                }
            var m = new Mesh { name = "Tube" }; m.vertices = v; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Board or tube.</summary>
        public void SetTube(bool on)
        {
            Tube = on;
            if (ragdoll != null && ragdoll.Active) ragdoll.End();
            boardRoot.gameObject.SetActive(!on);
            tubeRoot.gameObject.SetActive(on);
            MakeRagdoll();
            if (ragdoll == null) return;
            if (on) ragdoll.SetBoard(tubeRoot, new Vector3(1.6f, 0.44f, 1.6f), new Vector3(0f, 0.22f, 0f));
            else ragdoll.SetBoard(boardRoot, new Vector3(0.29f, 0.07f, 1.55f), new Vector3(0f, 0.04f, 0f));
            tubeSpin = 0f; tubeSpinRate = 0f;
        }

        /// <summary>A bounce off something: the tube spins away from it.</summary>
        public void TubeKick(float into) { tubeSpinRate += (Random.value < 0.5f ? -1f : 1f) * Mathf.Clamp(into * 0.5f, 2f, 7f); }

        void PoseTube(float dt)
        {
            Flutter(dt);
            float t = Time.time;
            // feet out over the front of the ring, hands on the handles, hips down in the hole
            Vector3 fl = roll.TransformPoint(new Vector3(0.78f, 0.36f, 0.14f)), fr = roll.TransformPoint(new Vector3(0.78f, 0.36f, -0.14f));
            Vector3 hl = roll.TransformPoint(new Vector3(-0.05f, 0.5f, 0.6f)), hr = roll.TransformPoint(new Vector3(-0.05f, 0.5f, -0.6f));
            float seat = Mathf.Max(0f, rig.HipHeight + BoardTop - 0.32f);
            rig.PoseSit(roll, seat, fl, fr, hl, hr, Mathf.Clamp(rider.Steer, -1f, 1f), dt, rider.Crashed, t);
        }

        void PoseRig(float dt)
        {
            if (Tube) { PoseTube(dt); return; }
            if (Walking) { rig.PoseWalk(roll, walkPhase, walkAmp, Time.time); return; }   // on foot in the camp
            Flutter(dt);
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
            var jacketM = snowy != null ? snowy.Make(new Color(0.93f, 0.31f, 0.14f), 0.35f) : M(new Color(0.93f, 0.31f, 0.14f), 0.35f);   // the cuffs get snowy too
            gearJacket = jacketM;
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

            var frameM = M(new Color(0.06f, 0.06f, 0.07f), 0.4f);
            var head = headBone;
            if (head != null)
            {
                Vector3 fwd = upper.TransformDirection(Vector3.right);   // the rider faces the toe edge
                Vector3 up = upper.up;
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
            if (Walking) WalkMuscles(t);
            // ease every muscle toward its target, so nothing snaps
            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * (crashed ? 20f : 11f));
            for (int i = 0; i < m.Length; i++) m[i] = Mathf.Lerp(m[i], want[i], k);
            poser.SetHumanPose(ref pose);
            PlantFeet();
            if (!crashed && !Walking)
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
            tubeSpin = 0f; tubeSpinRate = 0f;
            normal = Slope.Normal(rider.transform.position.x, rider.transform.position.z);
            lean = 0f; yawRate = 0f; lastTravel = rider.Heading;
            pivot.localRotation = Quaternion.identity;
            if (ragdoll != null) ragdoll.End();
            ragdollPending = false;
            foreach (var g in bandages) if (g != null) { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }
            bandages.Clear(); bandaged.Clear(); brokenThisCrash.Clear();
            trail.Clear();
            bodyTrail.Clear();
            RagdollBelowSnow = 0f;
            RagdollMoving = false;
            spray.Clear();
            bow.Clear();
            marks.Clear();
            craters.Clear();
            snowy?.Clear();
            comp = 0f; compV = 0f; shieldFlash = 0f;
            Apply(0f);
        }

        void LateUpdate() { Apply(Time.deltaTime); }

        void FixedUpdate() { PhysicsStep(); }

        static Vector3 OnSnow(Vector3 at) => new Vector3(at.x, Slope.DrawnHeight(at.x, at.z), at.z);

        void OnRagdollDrag(Vector3 at, float v)
        {
            if (v < 1.2f) return;
            if (v > 2f && Time.time > nextDragCrater) { nextDragCrater = Time.time + 0.2f; craters.Add(OnSnow(at), ragdoll != null ? ragdoll.Velocity : Vector3.zero, 0.3f + 0.025f * v, 0.04f + 0.008f * v); }
            if (Random.value < 0.15f) fx?.Puff(at, v * 0.3f);
        }

        void OnRagdollHit(float v, bool isBoard, Vector3 at, Transform bone)
        {
            RagdollHit?.Invoke(v, isBoard);
            if (!isBoard) snowy?.Hit(at, bone != null ? bone.position : RagdollCentre, v);
            if (v > 2.2f)
            {
                // a crater where it landed, and snow thrown everywhere
                craters.Add(OnSnow(at), ragdoll != null ? ragdoll.Velocity : Vector3.zero, Mathf.Min(1.1f, 0.35f + 0.055f * v), Mathf.Min(0.22f, 0.05f + 0.015f * v));
                if (v > 3.5f) fx?.Impact(at, v, ragdoll != null ? ragdoll.Velocity : Vector3.zero);
                else fx?.Puff(at, v);
            }
            if (isBoard || bone == null || rig == null || LimbOf(bone) == null || brokenThisCrash.Contains(bone)) return;
            // the first real hit breaks whatever took it; a second only on a big one
            if ((brokenThisCrash.Count == 0 && v > 3.2f) || (brokenThisCrash.Count == 1 && v > 9f)) Break(bone, at);
        }

        void Break(Transform bone, Vector3 at)
        {
            brokenThisCrash.Add(bone);
            fx?.Crack(at);
            BoneBroke?.Invoke(LimbOf(bone), at);
        }

        string LimbOf(Transform b)
        {
            if (b == rig.UpperL || b == rig.LowerL) return "Left arm";
            if (b == rig.UpperR || b == rig.LowerR) return "Right arm";
            if (b == rig.ThighL || b == rig.ShinL) return "Left leg";
            if (b == rig.ThighR || b == rig.ShinR) return "Right leg";
            if (b == rig.Head) return "Head";
            if (b == rig.Chest || b == rig.BodyBone || b == rig.Hips) return "Ribs";
            return null;
        }

        // ---- bandages: wrapped round each limb that broke, for the rest of the run

        void Bandage(Transform bone)
        {
            if (bone == null || bandaged.Contains(bone)) return;
            bandaged.Add(bone);
            if (bandageMat == null)
            {
                var sh = jacketShader != null ? jacketShader : Shader.Find("Universal Render Pipeline/Lit");
                bandageMat = new Material(sh); bandageMat.SetColor("_BaseColor", new Color(0.97f, 0.96f, 0.91f)); bandageMat.SetFloat("_Smoothness", 0.08f);
                bandageLine = new Material(sh); bandageLine.SetColor("_BaseColor", new Color(0.82f, 0.80f, 0.74f)); bandageLine.SetFloat("_Smoothness", 0.05f);
            }
            Transform child = bone == rig.UpperL ? rig.LowerL : bone == rig.LowerL ? rig.HandL : bone == rig.UpperR ? rig.LowerR : bone == rig.LowerR ? rig.HandR
                            : bone == rig.ThighL ? rig.ShinL : bone == rig.ShinL ? rig.EndL : bone == rig.ThighR ? rig.ShinR : bone == rig.ShinR ? rig.EndR : null;
            if (child != null)
            {
                bool leg = bone == rig.ThighL || bone == rig.ShinL || bone == rig.ThighR || bone == rig.ShinR;
                float r = leg ? (bone == rig.ThighL || bone == rig.ThighR ? 0.105f : 0.09f) : 0.07f;
                Wrap(bone, bone.position, child.position, r, 0.2f, 0.75f);
            }
            else if (bone == rig.Head)
            {
                // round the jaw and over the top of the head, under the beanie
                Vector3 up = rig.Neck != null ? (rig.Head.position - rig.Neck.position).normalized : Vector3.up;
                Vector3 c = rig.Head.position + up * 0.03f;
                Wrap(bone, c - up * 0.03f, c + up * 0.03f, 0.118f, 0f, 1f);
                var side = Vector3.Cross(up, upper.forward).normalized;
                Wrap(bone, c - side * 0.012f, c + side * 0.012f, 0.13f, 0f, 1f, false);
            }
            else
            {
                // ribs: strapped round the chest
                Vector3 up = rig.Neck != null ? (rig.Neck.position - rig.Chest.position).normalized : Vector3.up;
                Wrap(bone, rig.Chest.position - up * 0.04f, rig.Chest.position + up * 0.12f, 0.19f, 0.05f, 0.95f);
            }
        }

        /// <summary>A bandage round the segment a..b (from t0 to t1 along it), with wrap lines, fixed to the bone.</summary>
        void Wrap(Transform bone, Vector3 a, Vector3 b, float r, float t0, float t1, bool lines = true)
        {
            Vector3 p0 = Vector3.Lerp(a, b, t0), p1 = Vector3.Lerp(a, b, t1), d = p1 - p0;
            float len = Mathf.Max(0.02f, d.magnitude);
            var rot = Quaternion.FromToRotation(Vector3.up, d.normalized);
            GameObject Cyl(Vector3 at, float radius, float half, Material m)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.GetComponent<MeshRenderer>().sharedMaterial = m;
                g.transform.SetPositionAndRotation(at, rot);
                g.transform.localScale = new Vector3(radius * 2f, half, radius * 2f);
                g.transform.SetParent(bone, true);
                bandages.Add(g);
                return g;
            }
            Cyl((p0 + p1) * 0.5f, r, len * 0.5f, bandageMat);
            if (!lines) return;
            int k = Mathf.Clamp(Mathf.RoundToInt(len / 0.06f), 2, 6);
            for (int i = 1; i <= k; i++)
                Cyl(Vector3.Lerp(p0, p1, i / (k + 1f)) + d.normalized * (i % 2 == 0 ? 0.004f : -0.004f), r * 1.06f, 0.006f, bandageLine);
        }

        /// <summary>The avalanche carrying the thrown body along in its debris (see Ragdoll.Carry).</summary>
        public void Carry(Vector3 flow, float rate, float dt) { if (ragdoll != null && ragdoll.Active) ragdoll.Carry(flow, rate, dt); }

        /// <summary>Per physics step (public so the test harness can call it).</summary>
        public void PhysicsStep()
        {
            if (rider == null) return;
            if (ragdollPending && rider.Crashed)
            {
                ragdollPending = false;
                if (ragdoll == null)
                    MakeRagdoll();
                if (ragdoll.Active)
                {
                    // crashed again before the last one was tidied away: patch up, then throw again
                    ragdoll.End();
                    foreach (var b in brokenThisCrash) Bandage(b);
                }
                ragdoll.Begin(lastVel);
                stillFor = 0f;
                fx?.Impact(transform.position, Mathf.Max(6f, lastVel.magnitude * 0.6f), lastVel * 0.6f);   // the wipeout itself
                brokenThisCrash.Clear();
                crashClock = 0f;
            }
            if (ragdoll != null && ragdoll.Active)
            {
                crashClock += Time.fixedDeltaTime;
                // a wipeout always costs something: if nothing hit hard enough, the landing did it
                if (brokenThisCrash.Count == 0 && crashClock > 0.9f && rig != null)
                {
                    var pick = new[] { rig.LowerL, rig.LowerR, rig.ShinL, rig.ShinR, rig.ThighL, rig.UpperR };
                    var b = pick[Random.Range(0, pick.Length)];
                    Break(b, b.position);
                }
                ragdoll.Steer = rider.Crashed ? rider.Steer : 0f;   // steer the fall with the same thumb
                ragdoll.PhysicsStep();
                rider.CrashFollow = ragdoll.Centre;
                rider.HasCrashFollow = rider.Crashed;
                // stay down while still sliding or tumbling, and a beat after it stops
                if (ragdoll.Speed > 0.8f) stillFor = 0f; else stillFor += Time.fixedDeltaTime;
                // you can tap to get up once the body has (nearly) come to rest, and never while it is
                // falling into a crevasse
                rider.HoldCrash = rider.Crashed && (ragdoll.Speed > 2.5f || RagdollBelowSnow > 0.5f);
                RagdollMoving = rider.HoldCrash;
            }
        }

        /// <summary>Draw the rider for this frame (public so the test harness can call it).</summary>
        public void Apply(float dt)
        {
            if (rider == null) return;
            Vector3 p = rider.transform.position;
            pivot.localPosition = Vector3.down * Rider.R;

            // tilt with the snow, blending to level as you leave it
            Vector3 n = Vector3.Slerp(Slope.Normal(p.x, p.z), Vector3.up, Mathf.Clamp01(rider.AboveGround / 1.5f));
            normal = dt <= 0f ? n : Vector3.Slerp(normal, n, 1f - Mathf.Exp(-dt * 14f));

            if (!rider.Crashed) lastVel = rider.Velocity;
            if (ragdoll != null && ragdoll.Active)
            {
                if (rider.Crashed)
                {
                    ragdoll.LateStep();
                    // a furrow where the body slides and rolls through the snow
                    Vector3 c = ragdoll.Centre, vel = ragdoll.Velocity;
                    float ground = Slope.DrawnHeight(c.x, c.z);
                    RagdollBelowSnow = Mathf.Max(0f, ground - c.y);
                    bool inSnow = c.y - ground < 0.45f && RagdollBelowSnow < 1f && vel.sqrMagnitude > 0.25f;
                    bodyTrail.Feed(new Vector3(c.x, ground, c.z), Slope.Normal(c.x, c.z), Mathf.Atan2(vel.x, vel.z), inSnow, 2.0f, 0f, 0.35f, dt);
                }
                else
                {
                    FallRested?.Invoke(ragdoll.Centre);   // a flag where you came to rest
                    ragdoll.End(); spray.Emit(30);   // back up on the board, patched up
                    RagdollMoving = false; rider.HoldCrash = false;
                    bodyTrail.Feed(Vector3.zero, Vector3.up, 0f, false, 1f, 0f, 0f, dt);
                    RagdollBelowSnow = 0f;
                    foreach (var b in brokenThisCrash) Bandage(b);
                    brokenThisCrash.Clear();
                }
            }
            snowy?.Step(dt, rider.Crashed, rider.Speed);
            if (rider.Crashed && ((ragdoll != null && ragdoll.Active) || ragdollPending)) { }
            else if (rider.Crashed)
            {
                crashSpin += dt;
                pivot.Rotate(crashAxis, 520f * dt * Mathf.Clamp01(1.2f - crashSpin), Space.World);
                if (poser != null) Pose(dt);
                else if (rig != null) PoseRig(dt);
            }
            else
            {
                // the board's yaw on screen follows the physics a touch behind, so a correction at landing
                // or a grind turns it smoothly instead of snapping
                visYaw = dt <= 0f ? rider.Heading : Mathf.LerpAngle(visYaw * Mathf.Rad2Deg, rider.Heading * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 16f)) * Mathf.Deg2Rad;
                float yaw = visYaw;
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
                if (Tube)
                {
                    // the rider faces down the line (the model faces the board's +X), and the tube turns slowly
                    // after a bounce
                    tubeSpinRate *= Mathf.Exp(-dt * 0.8f);
                    tubeSpin += tubeSpinRate * dt;
                    rot = baseRot * Quaternion.Euler(0f, -90f + tubeSpin * Mathf.Rad2Deg, 0f);
                }
                Vector3 mid = new Vector3(0f, 0.9f, 0f);
                pivot.rotation = rot;
                pivot.position = p + Vector3.down * Rider.R + baseRot * mid - rot * mid;

                // the lean IS the steering readout: the body is on exactly the edge the thumb asked for
                float leanDeg = rider.Lean * Mathf.Rad2Deg;
                roll.localRotation = Tube ? Quaternion.Euler(leanDeg * 0.5f, 0f, 0f) : Quaternion.Euler(0f, 0f, -leanDeg * 0.75f);
                upper.localRotation = Quaternion.Euler(0f, 0f, -leanDeg * 0.25f);
                if (Walking) WalkFrame(dt);   // on foot in the camp
                if (poser != null) Pose(dt);
                else if (rig != null) PoseRig(dt);
                else upper.localScale = new Vector3(1f, 1f - 0.24f * rider.Tuck, 1f);
            }

            // the track: a groove pressed into the snow under the board (none in snow the avalanche is churning)
            trail.Feed(p + Vector3.down * Rider.R, normal, rider.TravelHeading, rider.Running && !rider.Crashed && !rider.Airborne && !rider.Grinding && rider.Undercut < 0.05f,
                       Tube ? 1.25f : Course.At(p.z) == Course.Section.Pipe ? 0.6f : Mathf.Lerp(0.9f, 1.25f, Slope.Rough(p.x, p.z)),
                       Tube ? 0f : rider.Lean / Rider.MaxLean, Tube ? 0.5f : rider.Skid, dt);   // a tube leaves a wide, shallow chute

            // bow wave off the nose: more with speed, deeper snow and when the edge bites
            bool riding = rider.Running && !rider.Crashed && !rider.Airborne && !rider.Grinding;
            bowEm.rateOverTime = riding && rider.Speed > 3f && !Tube
                ? rider.Speed * (4.5f + 4f * Slope.Rough(p.x, p.z)) * (1f + 1.2f * Mathf.Abs(rider.Lean) / Rider.MaxLean + rider.Skid)
                : 0f;

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
