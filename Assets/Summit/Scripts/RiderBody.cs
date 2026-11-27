using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Everything you see of the rider: the board, the person on it, the groove left
    /// in the snow and the powder thrown off the edge. Reads the physics after it has
    /// been interpolated for this frame, so it never stutters.
    /// The person is the hero (RiderBodyHero: Resources/Rider/Hero and the others, posed by HeroRig over RiderRig).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public partial class RiderBody : MonoBehaviour
    {
        public Rider rider;
        Transform pivot, roll, upper, trailAnchor;
        SnowTrail trail;
        ParticleSystem spray;
        ParticleSystem.EmissionModule sprayEm, wakeEm;
        float skidBurst;        // the board was just thrown across at this speed (Rider.Skidded): a burst of spray is owed
        bool wasUp;             // in the air last frame (a puff of the cornice's snow as the board leaves its lip)
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
        /// <summary>The tumbling body struck something (a tree, rock, sign): what, where, how hard (m/s into it).</summary>
        public System.Action<Collider, Vector3, float> RagdollStruck;
        /// <summary>A bone gave way: which limb ("Left arm", "Ribs", ...), and where.</summary>
        public System.Action<string, Vector3> BoneBroke;
        public SnowFX fx;
        readonly System.Collections.Generic.List<Transform> brokenThisCrash = new System.Collections.Generic.List<Transform>();
        readonly System.Collections.Generic.HashSet<Transform> bandaged = new System.Collections.Generic.HashSet<Transform>();
        readonly System.Collections.Generic.List<GameObject> bandages = new System.Collections.Generic.List<GameObject>();
        Material bandageMat, bandageLine;
        float crashClock, stillFor, visYaw, nextDragCrater;
        Craters craters;
        /// <summary>Where the thrown body is (its pelvis).</summary>
        public Vector3 RagdollCentre => ragdoll != null && ragdoll.Active ? ragdoll.Centre : transform.position;
        /// <summary>How far the thrown body is below the snow surface (in a crevasse), metres.</summary>
        public float RagdollBelowSnow { get; private set; }
        /// <summary>The thrown body is still tumbling or sliding.</summary>
        public bool RagdollMoving { get; private set; }
        Vector3 crashAxis;

        Transform model;
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
            jacketShader = jacket != null ? jacket.shader : null;
            boardPaint = board;   // the hero's own deck wears the game's board material (RiderBodyHero)
            if (!SetUpHero()) Debug.LogError("RiderBody: the hero could not be built (Resources/Rider/Hero, HeroAnim)");   // (RiderBodyHero)
            // what the outfit colours paint (see RiderBodyWalk)
            FindJacket(upper.gameObject); boardMat = board; if (jacket != null) { jacketMats.Add(jacket); jacketShade.Add(1f); }

            // a groove pressed into the snow behind the board
            trail = new SnowTrail(jacket != null ? jacket.shader : Shader.Find("Universal Render Pipeline/Lit"));
            sideTrail = new[] { new SnowTrail(jacket != null ? jacket.shader : Shader.Find("Universal Render Pipeline/Lit")),
                                new SnowTrail(jacket != null ? jacket.shader : Shader.Find("Universal Render Pipeline/Lit")) };   // the skis' two tracks (RiderBodyRide)
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
            SetUpSpray(sprayMat, jacket != null ? jacket.shader : Shader.Find("Universal Render Pipeline/Lit"));   // lit, soft, by the snow (RiderBodySpray)

            marks = new SnowMarks(transform.parent, Imprint(trailMat, CraterTexture()), 80);
            BuildShield(trailMat);
            rider.Skidded += speed => skidBurst = Mathf.Max(skidBurst, speed);
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
                if (why.StartsWith("Crevasse")) lastVel = Course.CrevasseDrop(transform.position, lastVel);   // a drop, not a throw
                lastVel *= rider.CrashCarry;   // through a snapped tree: on, a little slower (TreeBreak)
                crashSpin = 0f;
                ragdollPending = rig != null;   // thrown on the next physics step, not inside the collision
                if (rig == null) craters.Add(transform.position + Vector3.down * Rider.R, lastVel, 1.0f, 0.2f);   // a thrown body digs its own (SnowLayer)
                crashAxis = new Vector3(Random.Range(-1f, 1f), 0.2f, Random.Range(0.4f, 1f)).normalized;
            };
            rider.Scraped += _ => spray.Emit(25);
            rider.HitObstacle += OnRiderHitObstacle; rider.Crash += OnCrashBleed;   // blood on a hard hit (RiderBodyBlood)
            rider.HitObstacle += OnLimbObstacle; rider.Crash += OnLimbCrash;        // a limb torn off by a brutal one (RiderBodyLimbs)
        }

        // ---------------------------------------------------------------- the person

        RiderRig rig;

        // ---- baggy clothes: the jacket and trousers are loosened, and given shapes the air can blow into (ClothWind)

        struct WindCloth { public SkinnedMeshRenderer smr; public ClothWind.Shapes shapes; }
        readonly System.Collections.Generic.List<WindCloth> cloth = new System.Collections.Generic.List<WindCloth>();
        ClothWind.Air clothAir;
        Transform clothFrame; Vector3 clothTail, clothToe;   // the body's own frame (the hips) and, in it, the ways the shapes blow

        /// <summary>How far indoors the rider is on foot in the camp, 0 out in the weather .. 1 inside the lodge (Camp sets
        /// it as it walks you): the weather's wind does not blow in there, nor in the closet.</summary>
        public static float Shelter;

        /// <param name="hips">the bone the clothes' wind is reckoned in (it turns with the body: riding, walking, sitting)</param>
        void Loosen(GameObject go, Transform hips, float puff = 1f)   // puff: how much looser (the hero's clothes are already baggy)
        {
            // the shapes blow four ways, fixed to the body as it stands built: to the tail (the air from riding: it comes
            // from the nose of the board and streams out past the tail), to the nose, and across to the toe and heel edges
            Vector3 tailW = roll.TransformDirection(Vector3.back), toeW = roll.TransformDirection(Vector3.right), upW = roll.TransformDirection(Vector3.up);
            clothFrame = hips != null ? hips : roll;
            clothTail = clothFrame.InverseTransformDirection(tailW); clothToe = clothFrame.InverseTransformDirection(toeW);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                bool jacket = smr.name.Contains("Body"), pants = smr.name.Contains("Legs");
                if (!jacket && !pants) continue;
                var src = smr.sharedMesh;
                if (src == null || !src.isReadable) continue;
                var mesh = Instantiate(src);
                mesh.name = src.name + " (loose)";
                float m = 1f / Mathf.Max(1e-5f, smr.transform.lossyScale.x);   // metres to mesh units
                var v = mesh.vertices; var n = mesh.normals;
                if (n == null || n.Length != v.Length) { mesh.RecalculateNormals(); n = mesh.normals; }
                // vertices split at seams share a position: give them one averaged normal, or pushing them
                // out would open cracks along every seam
                var sum = new System.Collections.Generic.Dictionary<Vector3Int, Vector3>();
                Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
                for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); sum[k] = (sum.TryGetValue(k, out var acc) ? acc : Vector3.zero) + n[i]; }
                float baggy = (jacket ? 0.022f : 0.018f) * puff;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 pm = v[i] / m;   // position in metres, for noise
                    float loose = 0.55f + 0.45f * Mathf.PerlinNoise(pm.x * 4f + pm.y * 3f + 11f, pm.z * 4f - pm.y * 2f + 5f);
                    // looser all over: puffed out along the surface
                    v[i] += sum[Key(v[i])].normalized * (baggy * loose * m);
                }
                mesh.vertices = v;
                var shapes = ClothWind.Add(mesh, ClothWind.MeshToWorld(smr), tailW, toeW, upW, pants);
                mesh.RecalculateBounds();
                snowy?.Bake(smr, mesh);
                smr.sharedMesh = mesh;
                cloth.Add(new WindCloth { smr = smr, shapes = shapes });
            }
        }

        /// <summary>
        /// Clothes in the air (ClothWind): the air from riding (from the nose, more the faster you go) and the weather's
        /// wind where the rider is (the trees' gusts at the same moment), added together and read in the body's own
        /// frame, so a crosswind or a gust pushes the clothes sideways or back even standing in the camp, and riding flat
        /// out the air from the nose rules. None indoors.
        /// </summary>
        void Flutter(float dt)
        {
            if (cloth.Count == 0) return;
            Vector3 tail = clothFrame != null ? clothFrame.TransformDirection(clothTail) : roll.TransformDirection(Vector3.back);
            Vector3 toe = clothFrame != null ? clothFrame.TransformDirection(clothToe) : roll.TransformDirection(Vector3.right);
            float ride = Walking ? 0f : rider.Speed * (rider.Crashed ? 0.3f : 1f);   // the air from riding, m/s (none on foot)
            Vector3 at = clothFrame != null ? clothFrame.position : transform.position;
            float outside = ClosetStage.Showing != null ? 0f : Walking ? 1f - Mathf.Clamp01(Shelter) : 1f;
            clothAir.Step(dt, tail * ride + ClothWind.WeatherAt(at, outside), tail, toe, rider.Airborne && !Walking ? 0.15f : 0f);
            foreach (var c in cloth) clothAir.Apply(c.smr, c.shapes);
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
            ragdoll.Struck += (c, at, n, into, bone) => RagdollStruck?.Invoke(c, at, into);
            ragdoll.Prebuild();
            HookLimbs();   // limbs torn off by the worst hits (RiderBodyLimbs)
            HookInjuries();   // each hit, for the medical receipt (RiderBodyInjury)
        }
        float tubeSpin, tubeSpinRate;
        public bool Tube { get; private set; }
        Material tubeBand;
        static readonly Color TubeBandWhite = new Color(0.98f, 0.97f, 0.94f);

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
            tubeBand = band;   // (a board graphic paints it in the design's rail colour: RiderBodyWalk.Wear)
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
            ShowRide();   // the board (or the skis) when not in the tube (RiderBodyRide)
            tubeRoot.gameObject.SetActive(on);
            MakeRagdoll();
            if (ragdoll == null) return;
            if (on) { ragdoll.SetBoard(tubeRoot, new Vector3(1.6f, 0.44f, 1.6f), new Vector3(0f, 0.22f, 0f)); ragdoll.SetExtras(null); }
            else RideRagdoll();   // the board, or the skis and poles
            tubeSpin = 0f; tubeSpinRate = 0f;
        }

        /// <summary>A bounce off something: the tube spins away from it.</summary>
        public void TubeKick(float into) { tubeSpinRate += (Random.value < 0.5f ? -1f : 1f) * Mathf.Clamp(into * 0.5f, 2f, 7f); }

        /// <summary>Riding switch, as drawn (Rider.Switch: snowboard and skis only).</summary>
        bool SwitchNow => rider != null && rider.Switch && !Tube;
        /// <summary>+1 riding the normal way, -1 switch: the board's toe and heel edges (a skier's left and right) are
        /// the other way about in the world.</summary>
        float StanceSign => SwitchNow ? -1f : 1f;

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
            RestoreLimbs();   // every limb back on (RiderBodyLimbs)
            ragdollPending = false;
            foreach (var g in bandages) if (g != null) { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }
            bandages.Clear(); bandaged.Clear(); brokenThisCrash.Clear();
            trail.Clear();
            if (sideTrail != null) foreach (var st in sideTrail) st.Clear();
            ClearSpray();
            RagdollBelowSnow = 0f;
            RagdollMoving = false;
            Dead = false;
            spray.Clear();
            if (sparks != null) sparks.Clear();
            bow.Clear();
            marks.Clear();
            craters.Clear();
            SnowLayer.Instance?.Clear();
            snowy?.Clear();
            comp = 0f; compV = 0f; shieldFlash = 0f;
            Apply(0f);
        }

        void LateUpdate() { Apply(Time.deltaTime); }

        void FixedUpdate() { PhysicsStep(); }

        void OnRagdollDrag(Vector3 at, float v)
        {
            if (v < 1.2f) return;
            if (v > 4f && Random.value < 0.06f) fx?.Puff(at, v * 0.25f);   // the furrow itself is in the SnowLayer
        }

        /// <summary>The ragdoll's latest hit was against an object (tree, rock, sign), not the snow.</summary>
        public bool RagdollHitObject => ragdoll != null && ragdoll.LastHitObject;

        /// <summary>How fast the fallen body is going (m/s): the ragdoll once it has taken over, the rider until then.</summary>
        public float RagdollSpeed => ragdoll != null && ragdoll.Active ? ragdoll.Speed : rider != null ? rider.Speed : 0f;

        void OnRagdollHit(float v, bool isBoard, Vector3 at, Transform bone)
        {
            RagdollHit?.Invoke(v, isBoard);
            if (!isBoard) snowy?.Hit(at, bone != null ? bone.position : RagdollCentre, v);
            // the hole is pressed by the part itself (SnowLayer); only a genuinely hard hit throws up a little powder
            if (v > 5f && Time.time > nextDragCrater) { nextDragCrater = Time.time + 0.15f; fx?.Thud(at, v, ragdoll != null ? ragdoll.Velocity : Vector3.zero); }
            if (isBoard || bone == null || rig == null || LimbOf(bone) == null || brokenThisCrash.Contains(bone) || !ragdoll.Attached(bone)) return;   // (a torn-off limb breaks nothing more)
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

        // (Bandage: RiderBodyBandage.cs)

        /// <summary>A dent in the snow, stretched along travel (the camp's roll: a body-shaped furrow, as a fall makes).</summary>
        public void SnowMark(Vector3 at, Vector3 travel, float size, float depth) => craters.Add(at, travel, size, depth);

        /// <summary>The avalanche carrying the thrown body along in its debris (see Ragdoll.Carry).</summary>
        public void Carry(Vector3 flow, float rate, float dt) { if (ragdoll != null && ragdoll.Active) ragdoll.Carry(flow, rate, dt); }

        /// <summary>Per physics step (public so the test harness can call it).</summary>
        public void PhysicsStep()
        {
            if (rider == null) return;
            SnowLayer.Instance?.Focus(RagdollCentre);
            if (ragdollPending && rider.Crashed)
            {
                ragdollPending = false;
                if (ragdoll == null)
                    MakeRagdoll();
                if (ragdoll.Active)
                {
                    // crashed again before the last one was tidied away: patch up, then throw again
                    DetachLimbs(); ragdoll.End(); MendLimbs();   // (a limb lost is patched back on, bandaged: RiderBodyLimbs)
                    foreach (var b in brokenThisCrash) Bandage(b);
                }
                ragdoll.Begin(lastVel);
                FallBegin();   // how far it falls from here, and whether that kills (RiderBodyFall)
                stillFor = 0f;
                fx?.Thud(transform.position, Mathf.Max(6f, lastVel.magnitude * 0.6f), lastVel * 0.6f);   // the wipeout itself
                brokenThisCrash.Clear(); breaksTold = 0;   // (breaksTold: the receipt's count, RiderBodyInjury)
                crashClock = 0f;
                ResetSettle();   // every fall ends (RiderBodySettle)
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
                ragdoll.Steer = rider.Crashed && !rider.StayDown ? rider.Steer : 0f;   // steer the fall with the same thumb (not once it's over, or the slide has you)
                ragdoll.PhysicsStep();
                FallStep();
                SettleStep();
                SnowLayer.Instance?.PressBody(ragdoll);   // every part presses its own shape into the snow
                rider.CrashFollow = ragdoll.Centre;
                rider.HasCrashFollow = rider.Crashed;
                // stay down while still sliding or tumbling, and a beat after it stops
                if (ragdoll.Speed > 0.8f) stillFor = 0f; else stillFor += Time.fixedDeltaTime;
                // a tap gets you up at once, even mid-tumble, unless the fall isn't decided yet: still dropping off
                // something high (it may kill: RiderBodyFall), or sinking into a crevasse (deep enough ends the run).
                // Then the tap waits for the answer (SummitGame)
                rider.HoldCrash = rider.Crashed && (FallPending || (RagdollBelowSnow > 0.5f && stillFor < 1.2f));
                RagdollMoving = rider.Crashed && ragdoll.Speed > 2.5f;
            }
        }

        /// <summary>Draw the rider for this frame (public so the test harness can call it).</summary>
        public void Apply(float dt)
        {
            if (rider == null) return;
            Vector3 p = rider.transform.position;
            pivot.localPosition = Vector3.down * Rider.R;

            // tilt with the snow, blending to level as you leave it
            Vector3 n = Vector3.Slerp(rider.GroundNormal(p), Vector3.up, Mathf.Clamp01(rider.AboveGround / 1.5f));   // (a roof's pitch, on a roof)
            if (Walking && Upright) n = Vector3.up;   // (on the camp's stairs: upright, not tipped to the bank under them)
            normal = dt <= 0f ? n : Vector3.Slerp(normal, n, 1f - Mathf.Exp(-dt * 14f));

            if (!rider.Crashed) lastVel = rider.Velocity;
            if (ragdoll != null && ragdoll.Active)
            {
                if (rider.Crashed)
                {
                    ragdoll.LateStep();
                    Vector3 c = ragdoll.Centre;
                    float ground = Slope.DrawnHeight(c.x, c.z);
                    RagdollBelowSnow = Mathf.Max(0f, ground - c.y);
                }
                else
                {
                    FallRested?.Invoke(ragdoll.Centre);   // a flag where you came to rest
                    DetachLimbs(); ragdoll.End(); MendLimbs(); spray.Emit(30);   // back up on the board, patched up (a lost limb left on the snow, the rider's bandaged: RiderBodyLimbs)
                    RagdollMoving = false; rider.HoldCrash = false;
                    RagdollBelowSnow = 0f;
                    foreach (var b in brokenThisCrash) Bandage(b);
                    brokenThisCrash.Clear();
                }
            }
            snowy?.Step(dt, rider.Crashed, rider.Speed);
            LimbsStep(dt);   // the wounds bleed for a while (RiderBodyLimbs)
            BloodStep(dt);   // drips and a pool after a hard crash (RiderBodyBlood)
            if (rider.Crashed && OnVehicle && Ride == RideKind.Bear) BearLooseStep(dt);   // thrown off the bear: it runs on by itself (RiderBodyVehicle)
            if (rider.Crashed) HideLegs(false);   // (thrown out of the kayak: the legs are seen again)
            if (rider.Crashed && ((ragdoll != null && ragdoll.Active) || ragdollPending)) { }
            else if (rider.Crashed)
            {
                crashSpin += dt;
                pivot.Rotate(crashAxis, 520f * dt * Mathf.Clamp01(1.2f - crashSpin), Space.World);
                if (hero != null) PoseHero(dt);
            }
            else
            {
                // the board's yaw on screen follows the physics a touch behind, so a correction at landing
                // or a grind turns it smoothly instead of snapping
                visYaw = dt <= 0f ? rider.Heading : Mathf.LerpAngle(visYaw * Mathf.Rad2Deg, rider.Heading * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 16f)) * Mathf.Deg2Rad;
                float yaw = visYaw + (RidingGear && Ride == RideKind.Kayak ? rider.SpinOut : 0f);   // (a kayak spun out: the hull and the rider in it go round: RiderVehicle)
                Vector3 up = normal;
                if (rider.Grinding)
                {
                    // 50-50 along a box; across the rail for a boardslide
                    var fd = rider.GrindFeature.Dir;
                    // (and whatever it has been switched to since: Rider.GrindYaw; a lift's launch ramp is ridden straight)
                    yaw = Mathf.Atan2(fd.x, fd.z) + (rider.GrindLive ? rider.GrindYaw : 0f);
                    up = Vector3.up;
                }
                var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                Vector3 fp = Vector3.ProjectOnPlane(f, up);
                Quaternion baseRot = fp.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fp.normalized, up) : pivot.rotation;
                // riding switch: the rider (and the board under them) turned round, going tail first (Rider.Switch)
                Quaternion stance = SwitchNow ? baseRot * Quaternion.AngleAxis(180f, Vector3.up) : baseRot;
                // the trick being turned: spin about the body's up axis and flip about the board, round the middle of the body
                Quaternion rot = stance;
                Vector3 flipAxis = RidingGear ? Vector3.right : Vector3.forward;   // (a skier faces the way they go: a flip is head over heels along the line)
                if (rider.Airborne && rider.TrickName != null)
                {
                    float tw = rider.GrabTweak;   // a tweaked grab: the board poked out and across
                    rot = stance * Quaternion.AngleAxis(rider.SpinNow * Mathf.Rad2Deg, Vector3.up)
                                  * Quaternion.AngleAxis(rider.FlipNow * Mathf.Rad2Deg, flipAxis)
                                  * Quaternion.Euler(9f * tw, 24f * tw, 0f);
                }
                // just landed short of the line (a half turn lands switch): the board swings round under the rider
                else if (!rider.Airborne && !rider.Grinding && (rider.LandSpin != 0f || rider.LandFlip != 0f))
                    rot = stance * Quaternion.AngleAxis(rider.LandSpin * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(rider.LandFlip * Mathf.Rad2Deg, flipAxis);
                // a press on a rail: the nose (or the tail) down, the other end up
                else if (rider.GrindLive && rider.GrindPress != 0f)
                    rot = stance * Quaternion.AngleAxis(rider.GrindPress * 11f, Vector3.right);
                if (Tube)
                {
                    // the rider faces down the line (the model faces the board's +X), and the tube turns slowly
                    // after a bounce
                    tubeSpinRate *= Mathf.Exp(-dt * 0.8f);
                    tubeSpin += tubeSpinRate * dt;
                    // (and spun in the air by the stick: Rider.SpinNow, with what a landing leaves of it swinging out)
                    float spun = rider.Airborne ? rider.SpinNow : rider.LandSpin;
                    // (and rolled over with it, head over heels down the line: the bail roll)
                    float rolled = rider.Airborne ? rider.FlipNow : rider.Grinding ? 0f : rider.LandFlip;
                    rot = baseRot * Quaternion.AngleAxis(rolled * Mathf.Rad2Deg, Vector3.right) * Quaternion.Euler(0f, -90f + (tubeSpin + spun) * Mathf.Rad2Deg, 0f);
                }
                Vector3 mid = new Vector3(0f, 0.9f, 0f);
                pivot.rotation = rot;
                pivot.position = p + Vector3.down * (Rider.R + rider.Sink) + baseRot * mid - rot * mid;   // (sunk a little into deep powder: Rider.Sink)

                // the lean IS the steering readout: the body is on exactly the edge the thumb asked for (turned round,
                // riding switch, the board's own sides are the other way about: the same lean in the world)
                float leanDeg = rider.Lean * Mathf.Rad2Deg * StanceSign;
                roll.localRotation = Tube ? Quaternion.Euler(leanDeg * 0.5f, 0f, 0f) : Quaternion.Euler(0f, 0f, -leanDeg * 0.75f);
                roll.localPosition = Tube || RidingGear ? Vector3.zero : Vector3.up * EdgeLift(leanDeg * 0.75f);   // (onto its edge, not about its middle: RiderBodyTouch)
                upper.localRotation = Quaternion.Euler(0f, 0f, -leanDeg * 0.25f - rider.BodyLean * StanceSign);   // (and the body into the slide: Rider.BodyLean)
                if (RidingGear) upper.localRotation = Quaternion.identity;   // a skier's upper body angles back out of the turn (in the clips)
                if (RidingGear && Rides.Of(Ride).Vehicle) VehicleRoll(leanDeg, dt);   // a kayak, a car, a bear (RiderBodyVehicle)
                else if (RidingGear && Ride == RideKind.Toboggan)
                {
                    // a toboggan has no edge to stand on: it stays nearly flat and the rider leans over its side
                    roll.localRotation = Quaternion.Euler(0f, 0f, -leanDeg * 0.2f);
                    upper.localRotation = Quaternion.Euler(0f, 0f, -leanDeg * 0.3f);
                }
                if (Walking) WalkFrame(dt);   // on foot in the camp
                if (hero != null) PoseHero(dt);
            }

            // the track: a groove pressed into the snow under the board (none in snow the avalanche is churning)
            bool tracking = rider.Running && !rider.Crashed && !rider.Airborne && !rider.Grinding && rider.Undercut < 0.05f;
            bool onSkis = RidingGear && (Ride == RideKind.Skis || Ride == RideKind.Car || Ride == RideKind.Bear);   // two narrow tracks instead of the one trench (the car's: its tyres; the bear's: its paws)
            bool onSled = RidingGear && (Ride == RideKind.Toboggan || Ride == RideKind.Kayak);   // a wide flat track (the sled's, a kayak's hull), and the dragged heel's scuff beside it
            FeedSideTracks(p + Vector3.down * Rider.R, tracking, Mathf.Lerp(0.9f, 1.25f, Mathf.Clamp01(rider.Underfoot.powder + 0.5f * rider.Underfoot.slush)) * (1f - 0.3f * rider.Underfoot.ice), dt);
            trail.Mix = rider.Underfoot; trail.Bite = rider.Bite;   // (the cut's shape: the snow it is in, how hard the edge pushes)
            trail.Feed(p + Vector3.down * Rider.R, normal, rider.TravelHeading, tracking && !onSkis,
                       (Tube ? 1.25f : Mathf.Lerp(0.9f, 1.25f, Mathf.Clamp01(rider.Underfoot.powder + 0.5f * rider.Underfoot.slush)) * (1f - 0.3f * rider.Underfoot.ice)) * (onSled ? 1.6f : 1f),
                       Tube || onSled ? 0f : rider.Lean / Rider.MaxLean, Tube ? 0.5f : onSled ? rider.Skid * 0.3f : rider.Skid, dt);   // a tube leaves a wide, shallow chute

            // the snow the board throws: the edge's crystals and clumps, the bow wave, the wake, a dragged hand (RiderBodySpray)
            var snow = rider.Underfoot;
            float hard = snow.ice;
            SprayFrame(p + Vector3.down * Rider.R, dt);

            GrindSparks(p, dt);   // a few sparks off a rail (RiderBodySparks)
            // thrown across from one way to the other (Rider.Skidded): a burst of snow off the edge, by how fast the board was going
            if (skidBurst > 0f)
            {
                if (rider.Running && !rider.Crashed && !rider.Airborne && !Tube) spray.Emit(Mathf.RoundToInt(Mathf.Clamp(skidBurst * 1.6f, 12f, 70f) * (1f + snow.powder + 0.4f * snow.slush - 0.6f * hard)));
                skidBurst = 0f;
            }
            // off a cornice's lip: the curl's crown sprays off the board's tail (Cliffs.CorniceFace: only at the moment it leaves)
            if (rider.Airborne && !wasUp && rider.Running && !rider.Crashed && !Tube && Cliffs.CorniceFace(p.x, p.z + 0.8f) > 0.4f)
                spray.Emit(Mathf.RoundToInt(Mathf.Clamp(rider.Speed * 1.4f, 14f, 45f)));
            wasUp = rider.Airborne;


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
