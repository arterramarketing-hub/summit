using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What the hero rides besides the snowboard: a pair of skis with two poles, and a wooden toboggan
    /// (Resources/Rider/Rides.fbx and the Ski_ and Sled_ clips, built by Tools/MakeRides.py). Kept apart from
    /// the snowboard's code: RiderBodyHero hands a frame over to PoseSkis or PoseSled when one is on, and
    /// RiderBody asks RidingGear where they differ (which way a flip turns, the bank, the tracks in the snow).
    ///
    /// On the toboggan the rider sits on the battens, feet first, a hand on a batten's end either side. Leaning
    /// is in the clips: the inside hand goes down into the snow behind the hip and that heel swings out and
    /// drags (and leaves its scuff beside the sled's wide flat track). It lies back to tuck, hangs on in the air
    /// or lets go altogether for a no-hander, and folds over its knees when it lands and skips. In a crash the
    /// rider is thrown and the sled tumbles on by itself.
    ///
    /// On skis the rider faces the way they go, so the model is turned a quarter turn from the board's side-on
    /// stance, about the Root bone. Each ski is carried by its own foot (the clips move the legs separately: in a
    /// turn the inside leg is drawn up and forward and the outside one is long, which is what keeps both skis on
    /// the snow while the rider is banked over), and each pole by its hand: the clips turn the hands so the poles
    /// point where they should (trailing, planted, clamped under the arms in a tuck). In a crash the skis and
    /// poles come off and tumble on their own (Ragdoll's loose gear).
    ///
    /// Nothing here knows how the files' axes run: the gear is put where the hero's own bones say, at rest.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>What the rider is on (set from Progress.Ride by the game; Rider.Ride is the physics' copy).</summary>
        public RideKind Ride { get; private set; }
        /// <summary>True while the rider is on gear other than the snowboard (not on foot, not in the tube).</summary>
        public bool RidingGear => rideGear && Ride != RideKind.Snowboard && !Tube && !Walking;

        bool rideGear;                                  // the gear and its clips are there
        Transform skiRoot;                              // under the roll, like the board: the frame the ski clips were made against
        readonly Transform[] ski = new Transform[2], pole = new Transform[2];   // 0 left, 1 right
        readonly Vector3[] skiOff = new Vector3[2];     // where a ski sits from its foot bone, in the bone's frame
        readonly Quaternion[] skiTurn = new Quaternion[2];
        Quaternion heroBaseRot = Quaternion.identity;   // the model side-on, as the board has it
        Vector3 rideModelPos;                           // the model facing forward, the Root bone still on the deck
        static readonly Quaternion RideTurn = Quaternion.Euler(0f, -90f, 0f);   // the toe edge (+X) round to the nose (+Z)
        SnowTrail[] sideTrail;                          // the skis' two tracks; or the scuff of the toboggan's dragged heel
        bool sledGear;                                  // the toboggan and its clips are there
        Transform sledRoot, sled;                       // under the roll; the sled itself (thrown in a crash)
        int idSledIdle, idSledRide, idSledLeanL, idSledLeanR, idSledTuck, idSledAir, idSledNoHands, idSledBump;
        bool skisHooked, sledHooked;                    // the rider's Jumped and Landed are listened to once, whichever body is built
        static readonly Vector3 SledBox = new Vector3(0.47f, 0.10f, 1.42f), SledBoxAt = new Vector3(0f, -0.02f, -0.04f);

        int idSkiIdle, idSkiRide, idSkiTurnL, idSkiTurnR, idSkiTuck, idSkiPlantL, idSkiPlantR, idSkiPop, idSkiAir, idSkiLand;
        int idSkiWindL, idSkiWindR, idSkiSpinL, idSkiSpinR, idSkiFlip;
        readonly int[] idSkiGrab = new int[3];
        // which hand holds the ski in each grab (0 left, 1 right): Safety, Mute, Tail (as Ride.cs names them)
        static readonly int[] skiGrabHand = { 1, 0, 1 };
        readonly HeroRig.Hold[] skiGrabHold = new HeroRig.Hold[3];
        float plantT = 9f; int plantSide, lastTurnSide;
        PoleSwing poleSwing;                            // the poles hang and swing from the hands (PoleSwing)

        // the boxes the loose gear tumbles with (in each piece's own frame: z down a ski, y up a pole)
        static readonly Vector3 SkiBox = new Vector3(0.10f, 0.06f, 1.62f), SkiBoxAt = new Vector3(0f, -0.01f, 0.09f);
        static readonly Vector3 PoleBox = new Vector3(0.03f, 1.16f, 0.03f), PoleBoxAt = new Vector3(0f, -0.5f, 0f);

        /// <summary>For a copy of the rider on skis (the start page's jump, StartJump): whether there are skis at all,
        /// the model's place facing forward, its turn from side-on, the deck's height, each ski's place from its foot
        /// bone, and which hand holds the ski in each ski grab (Safety, Mute, Tail).</summary>
        public bool HasSkis => rideGear;
        public Vector3 SkiModelPos => rideModelPos;
        public static Quaternion SkiFacing => RideTurn;
        public static float DeckTop => BoardTop;
        public Vector3 SkiOffset(int s) => skiOff[s];
        public Quaternion SkiTurnOf(int s) => skiTurn[s];
        /// <summary>A pole's grip in its hand at rest (a copy of the rider starts its own PoleSwing from it).</summary>
        public Quaternion PoleRest(int s) => poleSwing != null ? poleSwing.Rest(s) : Quaternion.identity;
        public static int SkiGrabHand(int i) => skiGrabHand[i];

        /// <summary>
        /// Called by SetUpHero with the hero placed side-on and its bones at rest: build the skis and poles on
        /// them. Without the gear or its clips the rider simply stays on the snowboard.
        /// </summary>
        void SetUpRides(HeroRig h, Material gearM)
        {
            heroBaseRot = model.localRotation;
            var deck = new Vector3(0f, BoardTop, 0f);
            rideModelPos = deck + RideTurn * (heroRidePos - deck);
            var prefab = Resources.Load<GameObject>("Rider/Rides");
            if (prefab == null) return;
            idSkiIdle = h.Id("Ski_Idle"); idSkiRide = h.Id("Ski_Ride"); idSkiTurnL = h.Id("Ski_TurnL"); idSkiTurnR = h.Id("Ski_TurnR");
            idSkiTuck = h.Id("Ski_Tuck"); idSkiPlantL = h.Id("Ski_PlantL"); idSkiPlantR = h.Id("Ski_PlantR");
            idSkiPop = h.Id("Ski_Pop"); idSkiAir = h.Id("Ski_Air"); idSkiLand = h.Id("Ski_Land");
            idSkiWindL = h.Id("Ski_WindL"); idSkiWindR = h.Id("Ski_WindR"); idSkiSpinL = h.Id("Ski_SpinL"); idSkiSpinR = h.Id("Ski_SpinR");
            idSkiFlip = h.Id("Ski_Flip");
            string[] g = { "Safety", "Mute", "Tail" };
            for (int i = 0; i < g.Length; i++) idSkiGrab[i] = h.Id("Ski_Grab_" + g[i]);
            if (idSkiIdle < 0 || idSkiRide < 0 || idSkiTurnL < 0 || idSkiTurnR < 0 || idSkiTuck < 0 || idSkiAir < 0)
            { Debug.LogWarning("Rides: the ski clips are not in HeroAnim.bytes (run Tools/MakeRides.py): snowboard only"); return; }

            // the gear's file is made in the same space as the hero's, so placed as the hero is, each piece is
            // where the tool drew it: the ski and the pole at the Root bone (the middle of the deck), the ski's
            // tip the way the rider faces, the pole upright
            var go = Instantiate(prefab, upper, false);
            go.name = "Rides";
            go.transform.localPosition = model.localPosition; go.transform.localRotation = model.localRotation; go.transform.localScale = model.localScale;
            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            Transform skiMesh = null, poleMesh = null, sledMesh = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) { if (t.name == "Ski") skiMesh = t; else if (t.name == "Pole") poleMesh = t; else if (t.name == "Sled") sledMesh = t; }
            Transform fingersL = h.Bone("LeftIndexProximal"), fingersR = h.Bone("RightIndexProximal");
            if (skiMesh == null || poleMesh == null || fingersL == null || fingersR == null || h.Foot[0] == null || h.Foot[1] == null || h.Hand[0] == null || h.Hand[1] == null)
            { Debug.LogWarning("Rides: no Ski or Pole in Rides.fbx: snowboard only"); DestroyImmediate(go); return; }
            // the sled's own materials: pale ash slats, darker battens, a hemp rope
            Shader lit = jacketShader != null ? jacketShader : Shader.Find("Universal Render Pipeline/Lit");
            Material Plain(Color col, float smooth) { var m = new Material(lit); m.SetColor("_BaseColor", col); m.SetFloat("_Smoothness", smooth); return m; }
            Material wood = Plain(new Color(0.66f, 0.47f, 0.26f), 0.3f), woodDark = Plain(new Color(0.38f, 0.24f, 0.12f), 0.3f), rope = Plain(new Color(0.84f, 0.78f, 0.60f), 0.1f);
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                // the graphics' uvs: a ski's top sheet; a pole's shaft takes the design's rail colour (before the skis are copied)
                if (mr.name == "Ski") BoardArt.Map(mr, BoardArt.Kind.Ski);
                else if (mr.name == "Pole") BoardArt.Map(mr, BoardArt.Kind.Solid);
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string mn = mats[i] != null ? mats[i].name : "";
                    mats[i] = mn.StartsWith("Board") ? boardPaint : mn.StartsWith("WoodDark") ? woodDark : mn.StartsWith("Wood") ? wood : mn.StartsWith("Rope") ? rope : gearM;
                }
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            Vector3 origin = h.Root.position, up = upper.up, facing = upper.TransformDirection(Vector3.right);
            Quaternion square = Quaternion.LookRotation(facing, up);   // z the way the rider faces, y up
            // the tool draws the ski's tip toward the rider's front: if the file came in turned, say so
            var sb = skiMesh.GetComponent<MeshRenderer>() != null ? skiMesh.GetComponent<MeshRenderer>().bounds.size : Vector3.zero;
            if (Mathf.Abs(Vector3.Dot(sb, facing)) < Mathf.Abs(Vector3.Dot(sb, upper.forward)) || Mathf.Abs(Vector3.Dot(sb, facing)) < Mathf.Abs(Vector3.Dot(sb, up)))
                Debug.LogWarning("Rides: the ski in Rides.fbx does not lie along the rider's facing; check the file's import settings against Hero.fbx");

            skiRoot = new GameObject("Skis").transform;
            skiRoot.SetParent(roll, false);
            for (int s = 0; s < 2; s++)
            {
                // a ski: under the middle of its boot (the ankle is 9 cm behind that), flat on the deck's plane
                var mesh = s == 0 ? Instantiate(skiMesh.gameObject, skiMesh.parent).transform : skiMesh;
                var holder = new GameObject(s == 0 ? "SkiL" : "SkiR").transform;
                holder.SetPositionAndRotation(origin, square);
                mesh.SetParent(holder, true);
                Vector3 ankle = h.Foot[s].position;
                Vector3 at = ankle - up * Vector3.Dot(ankle - origin, up) + facing * 0.09f;
                holder.SetPositionAndRotation(at, square);
                skiOff[s] = Quaternion.Inverse(h.Foot[s].rotation) * (at - ankle);
                skiTurn[s] = Quaternion.Inverse(h.Foot[s].rotation) * square;
                holder.SetParent(skiRoot, true);
                ski[s] = holder;

                // a pole: its grip in the fist, the top of the handle out of the thumb side. At rest that is
                // "forward, squared off against the hand's own length" (as the tool has it); from then on the
                // pole simply goes with the hand, and the clips turn the hands
                var pm = s == 0 ? Instantiate(poleMesh.gameObject, poleMesh.parent).transform : poleMesh;
                var grip = new GameObject(s == 0 ? "PoleL" : "PoleR").transform;
                grip.SetPositionAndRotation(origin, square);
                pm.SetParent(grip, true);
                Transform hand = h.Hand[s];
                Vector3 along = ((s == 0 ? fingersL : fingersR).position - hand.position).normalized;
                Vector3 top = (facing - along * Vector3.Dot(facing, along)).normalized;
                grip.SetPositionAndRotation(hand.position + along * 0.08f, Quaternion.FromToRotation(up, top) * square);
                grip.SetParent(hand, true);
                pole[s] = grip;
            }
            poleSwing = new PoleSwing(pole[0], pole[1]);
            // the toboggan: its seat (the tops of the battens) at the Root bone, the curl the way the rider faces
            idSledIdle = h.Id("Sled_Idle"); idSledRide = h.Id("Sled_Ride"); idSledLeanL = h.Id("Sled_LeanL"); idSledLeanR = h.Id("Sled_LeanR");
            idSledTuck = h.Id("Sled_Tuck"); idSledAir = h.Id("Sled_Air"); idSledNoHands = h.Id("Sled_NoHands"); idSledBump = h.Id("Sled_Bump");
            if (sledMesh != null && idSledIdle >= 0 && idSledRide >= 0 && idSledLeanL >= 0 && idSledLeanR >= 0 && idSledTuck >= 0 && idSledAir >= 0 && idSledNoHands >= 0)
            {
                sledRoot = new GameObject("Toboggan").transform;
                sledRoot.SetParent(roll, false);
                sled = new GameObject("Sled").transform;
                sled.SetPositionAndRotation(origin, square);
                sledMesh.SetParent(sled, true);
                sled.SetParent(sledRoot, true);
                // that was with the hero stood side-on, as on the board. On the sled the rider is turned to face
                // the way they go (RideTurn, in PoseSled), and nothing carries the sled round with them as the
                // feet carry the skis: turn it the same way about the middle of the deck, once, here
                sled.localRotation = RideTurn * sled.localRotation;
                sled.localPosition = deck + RideTurn * (sled.localPosition - deck);
                if (!sledHooked)
                {
                    sledHooked = true;   // (once: it acts on whichever body is built now)
                    rider.Landed += impact => { if (hero != null && impact > 1.5f) hero.Restart(idSledBump); };
                }
                sledGear = true;
            }
            DestroyImmediate(go);   // (emptied: the meshes now live on their holders)

            for (int i = 0; i < g.Length; i++) skiGrabHold[i] = h.CaptureHold(idSkiGrab[i], skiGrabHand[i]);
            if (!skisHooked)
            {
                skisHooked = true;   // (once: these act on whichever body is built now)
                rider.Jumped += () => { if (hero != null) hero.Restart(idSkiPop); };
                rider.Landed += impact => { if (hero != null && impact > 1.5f) hero.Restart(idSkiLand); };
            }
            rideGear = true;
            ShowRide();
        }

        /// <summary>
        /// The old body's skis and toboggan, gone before the new body builds its own (SetCharacter). The poles were on
        /// the old body's hands and went with it. Anything the ragdoll threw is already back on its roots (Ragdoll.End).
        /// </summary>
        void DropRides()
        {
            if (skiRoot != null) DestroyImmediate(skiRoot.gameObject);
            if (sledRoot != null) DestroyImmediate(sledRoot.gameObject);
            skiRoot = sledRoot = sled = null;
            for (int s = 0; s < 2; s++) { ski[s] = null; pole[s] = null; }
            poleSwing = null;
            rideGear = sledGear = false;
        }

        /// <summary>Snowboard, skis or toboggan (without the gear, always the snowboard).</summary>
        public void SetRide(RideKind k)
        {
            if (!rideGear || (k == RideKind.Toboggan && !sledGear)) k = RideKind.Snowboard;
            if (Ride == k && rideSet) return;
            rideSet = true;
            Ride = k;
            if (ragdoll != null && ragdoll.Active) ragdoll.End();
            plantT = 9f; lastTurnSide = 0;
            ShowRide();
            MakeRagdoll();
            RideRagdoll();
        }
        bool rideSet;

        /// <summary>Show what is being ridden, and nothing on foot (the gear waits at the gate) or in the tube.</summary>
        void ShowRide()
        {
            bool on = !Walking && !Tube;
            bool skis = rideGear && Ride == RideKind.Skis, onSled = sledGear && Ride == RideKind.Toboggan;
            boardRoot.gameObject.SetActive(on && !skis && !onSled);
            if (!rideGear) return;
            if (sledRoot != null) sledRoot.gameObject.SetActive(on && onSled);
            skiRoot.gameObject.SetActive(on && skis);
            for (int s = 0; s < 2; s++) if (pole[s] != null) pole[s].gameObject.SetActive(on && skis && !HandGone(s));
        }

        /// <summary>What flies off with the rider in a crash: the board; or both skis and both poles; or the sled.</summary>
        void RideRagdoll()
        {
            if (ragdoll == null || Tube) return;
            if (sledGear && Ride == RideKind.Toboggan)
            {
                ragdoll.SetBoard(sled, SledBox, SledBoxAt);
                ragdoll.SetExtras(null);
                return;
            }
            if (rideGear && Ride == RideKind.Skis)
            {
                ragdoll.SetBoard(ski[0], SkiBox, SkiBoxAt);
                ragdoll.SetExtras(new[]
                {
                    new Ragdoll.Loose { t = ski[1], size = SkiBox, center = SkiBoxAt, mass = 2.5f },
                    new Ragdoll.Loose { t = pole[0], size = PoleBox, center = PoleBoxAt, mass = 0.5f },
                    new Ragdoll.Loose { t = pole[1], size = PoleBox, center = PoleBoxAt, mass = 0.5f },
                });
            }
            else
            {
                ragdoll.SetBoard(boardRoot, new Vector3(0.29f, 0.07f, 1.55f), new Vector3(0f, 0.04f, 0f), true);   // strapped to both feet
                ragdoll.SetExtras(null);
            }
        }

        /// <summary>The skier, posed for this frame (PoseHero's place, when skis are on).</summary>
        void PoseSkis(float dt, bool down)
        {
            var h = hero;
            float t = Time.time;
            // facing the way they go, the Root bone on the deck's plane between the skis
            model.localRotation = RideTurn * heroBaseRot;
            model.localPosition = rideModelPos;
            var d = new HeroRig.Drive
            {
                board = skiRoot, rootInBoard = new Vector3(0f, BoardTop, 0f), rootRotInBoard = RideTurn * heroRootRot,
                up = roll.up, toe = roll.forward, nose = -roll.right, feet = !down,
            };
            float lift = 0f, blend = 9f;
            Vector3 aimL = PoleSwing.Trail, aimR = PoleSwing.Trail; float keepL = 0f, keepR = 0f, hang = PoleSwing.HangSnow;   // where the poles go (PoleSwing)
            Flutter(dt);
            // landings squash the legs, which spring back (the same spring as on the board)
            compV += (-comp * 140f - compV * 16f) * dt;
            comp = Mathf.Clamp(comp + compV * dt, -0.15f, 0.6f);
            bool idle = !rider.Running, air = rider.Airborne;
            float turn = Mathf.Clamp(rider.Lean / Rider.MaxLean * StanceSign, -1f, 1f);   // + = turning right (the skier's own right: skiing backwards, the world's left)
            float speed01 = Mathf.Clamp01(rider.Speed / 30f);
            if (down) h.Want(idFlail, 1f);
            else if (idle) { h.Want(idSkiIdle, 1f); aimL = aimR = PoleSwing.Stand; }
            else if (air)
            {
                blend = 11f;
                float k = rider.TrickProgress;
                bool flip = Mathf.Abs(rider.TrickFlip) > 0f;
                float spin = rider.TrickSpin;
                float grab = rider.TrickGrab > 0 ? rider.GrabPose : 0f;
                float fl = flip ? Sm(0.04f, 0.18f, k) * (1f - Sm(0.85f, 0.97f, k)) : 0f;
                float sp = Mathf.Abs(spin) > 0.3f ? 1f : 0f;
                float wind = sp * (1f - Sm(0f, 0.14f, k));
                float wrap = sp * Sm(0.08f, 0.2f, k) * (1f - Sm(0.85f, 0.97f, k));
                float rest = 1f - Mathf.Max(grab, fl);
                h.Want(idSkiAir, Mathf.Max(0.05f, rest * (1f - Mathf.Max(wind, wrap))));
                h.Want(spin < 0f ? idSkiWindL : idSkiWindR, wind * rest);
                h.Want(spin < 0f ? idSkiSpinL : idSkiSpinR, wrap * rest);
                if (grab > 0f && rider.TrickGrab <= idSkiGrab.Length)
                {
                    int gi = rider.TrickGrab - 1;
                    h.Want(idSkiGrab[gi], grab * 3f);
                    if (skiGrabHand[gi] == 0) { d.handL = grab; d.holdL = skiGrabHold[gi]; } else { d.handR = grab; d.holdR = skiGrabHold[gi]; }
                }
                if (fl > 0f) h.Want(idSkiFlip, fl * 1.5f);
                lift = 0.11f * Mathf.Max(grab, fl);   // the skis come up to the body
                // the poles: planted as they leave the lip and let go, then out and swinging for balance;
                // in with the ball in a flip; the grabbing hand's hanging behind; forward, ready, for the landing
                PoleSwing.InAir(popT, t, k, fl, grab, rider.TrickGrab > 0 && rider.TrickGrab <= skiGrabHand.Length ? skiGrabHand[rider.TrickGrab - 1] : -1, out aimL, out aimR);
                hang = PoleSwing.HangAir * (1f - fl);
                if (popT < h.Length(idSkiPop)) h.Want(idSkiPop, 2f * (1f - Sm(0.55f, 1f, popT / h.Length(idSkiPop))));
            }
            else
            {
                // the turn is always shown in full for the lean there is, tucked or not: its legs are what
                // keep both skis on the snow under a banked rider
                float tuck = rider.Tuck, tw = Mathf.Abs(turn);
                h.Want(idSkiRide, (1f - tw) * (1f - tuck));
                h.Want(idSkiTuck, (1f - tw) * tuck);
                h.Want(turn > 0f ? idSkiTurnR : idSkiTurnL, tw);
                if (landT < h.Length(idSkiLand)) h.Want(idSkiLand, landK * 2f * (1f - Sm(0.3f, 1f, landT / h.Length(idSkiLand))));
                if (rider.Grinding) h.Want(idSkiAir, 0.6f);   // arms out for balance on a rail
                // a pole plant as each new turn begins: the inside pole touches down and the skis come round it
                int side = turn > 0.12f ? 1 : turn < -0.12f ? -1 : 0;
                if (side != 0 && side != lastTurnSide)
                {
                    lastTurnSide = side;
                    if (rider.Speed > 6f && tuck < 0.3f && !rider.Grinding) { plantT = 0f; plantSide = side; h.Restart(side > 0 ? idSkiPlantR : idSkiPlantL); }
                }
                int idPlant = plantSide > 0 ? idSkiPlantR : idSkiPlantL;
                if (idPlant >= 0 && plantT < h.Length(idPlant))
                {
                    float pw = 1.4f * (1f - Sm(0.6f, 1f, plantT / h.Length(idPlant))) * (1f - tuck);
                    h.Want(idPlant, pw);
                    // the planting pole goes where the clip reaches it (forward, the tip in the snow)
                    if (plantSide > 0) keepR = Mathf.Clamp01(pw); else keepL = Mathf.Clamp01(pw);
                }
                PoleSwing.OnSnow(tuck, turn, landT, out aimL, out aimR);
                // the shoulders lead a little, and the eyes go where the turn is taking you
                d.twist = Mathf.Clamp(-rider.YawRate * Mathf.Rad2Deg * 0.06f, -8f, 8f);
                Vector3 v = rider.Velocity; v.y = 0f;
                if (v.sqrMagnitude > 9f)
                {
                    d.look = Quaternion.AngleAxis(rider.YawRate * Mathf.Rad2Deg * 0.35f, Vector3.up) * v + Vector3.down * v.magnitude * 0.15f;
                    d.lookWeight = 0.5f;
                }
                SwitchLook(ref d, v);
                d.drop = (Mathf.PerlinNoise(t * 9f, 1.3f) - 0.5f) * 0.02f * speed01;
            }
            d.drop += Mathf.Max(0f, comp) * 0.18f;
            popT += dt; landT += dt; plantT += dt;
            if (!down)
            {
                heroLift = dt <= 0f ? lift : Mathf.Lerp(heroLift, lift, 1f - Mathf.Exp(-dt * 12f));
                skiRoot.localPosition = Vector3.up * heroLift;
            }
            if (dt <= 0f) h.Snap();
            h.Evaluate(dt, blend);
            if (HandGone(0)) d.handL = 0f;
            if (HandGone(1)) d.handR = 0f;
            h.Layer(d, dt);
            if (!Walking && RideAfter != null) RideAfter(h, model, dt);   // (lifttop2: pushing off a lift's deck on the poles)
            // each ski goes with its own foot; a pole is not carried in a hand that is gone
            for (int s = 0; s < 2; s++)
            {
                Transform f = h.Foot[s];
                ski[s].SetPositionAndRotation(f.position + f.rotation * skiOff[s], f.rotation * skiTurn[s]);
                bool held = !HandGone(s);
                if (pole[s].gameObject.activeSelf != held) pole[s].gameObject.SetActive(held);
            }
            // the poles from the hands: hanging, trailing, swinging (the clips' own directions sat near level)
            if (!down && poleSwing != null) poleSwing.Apply(roll.forward, roll.up, aimL, aimR, keepL, keepR, hang, dt);
            else if (poleSwing != null) poleSwing.Reset();
            KeepOffSnow(dt, true);   // nothing through the snow, the poles' tips on it (RiderBodyTouch)
        }

        /// <summary>
        /// The narrow tracks beside the middle one: the skis' two (in place of the board's one trench), or, by the
        /// toboggan's wide flat track, the scuff of whichever heel is dragging in a turn.
        /// </summary>
        void FeedSideTracks(Vector3 at, bool onSnow, float scale, float dt)
        {
            if (sideTrail == null) return;
            bool skis = onSnow && RidingGear && Ride == RideKind.Skis, onSled = onSnow && RidingGear && Ride == RideKind.Toboggan;
            float travel = rider.TravelHeading;
            var dir = new Vector3(Mathf.Sin(travel), 0f, Mathf.Cos(travel));
            Vector3 right = Vector3.Cross(normal, Vector3.ProjectOnPlane(dir, normal).normalized);
            float turn = rider.Lean / Rider.MaxLean;
            for (int s = 0; s < 2; s++)
            {
                sideTrail[s].Mix = rider.Underfoot; sideTrail[s].Bite = rider.Bite * 0.6f;
                if (skis && ski[s] != null)
                    sideTrail[s].Feed(at + right * Vector3.Dot(ski[s].position - at, right), normal, travel, true, scale * 0.42f, turn, rider.Skid * 0.5f, dt);
                else if (onSled && hero != null && (s == 0 ? turn < -0.3f : turn > 0.3f))   // the left heel in a left turn, the right in a right
                    sideTrail[s].Feed(at + right * Vector3.Dot(hero.Foot[s].position - at, right), normal, travel, true, scale * 0.5f, 0f, 0.3f, dt);
                else sideTrail[s].Feed(at, normal, travel, false, scale, 0f, 0f, dt);
            }
        }

        /// <summary>The rider on the toboggan, posed for this frame (PoseHero's place, when the sled is on).</summary>
        void PoseSled(float dt, bool down)
        {
            var h = hero;
            // sat facing the way they go, the Root bone on the seat
            model.localRotation = RideTurn * heroBaseRot;
            model.localPosition = rideModelPos;
            var d = new HeroRig.Drive
            {
                board = sledRoot, rootInBoard = new Vector3(0f, BoardTop, 0f), rootRotInBoard = RideTurn * heroRootRot,
                up = roll.up, toe = roll.forward, nose = -roll.right, feet = !down,
            };
            float blend = 9f;
            Flutter(dt);
            bool idle = !rider.Running, air = rider.Airborne;
            float turn = Mathf.Clamp(rider.Lean / Rider.MaxLean, -1f, 1f);   // + = turning right
            if (down) h.Want(idFlail, 1f);
            else if (idle) h.Want(idSledIdle, 1f);
            else if (air)
            {
                // hanging on with the knees up; a "grab" is letting go of everything instead
                blend = 11f;
                float k = rider.TrickProgress;
                float free = rider.TrickGrab > 0 ? rider.GrabPose : 0f;
                h.Want(idSledAir, Mathf.Max(0.05f, 1f - free));
                h.Want(idSledNoHands, free * 3f);
            }
            else
            {
                float tuck = rider.Tuck, tw = Mathf.Abs(turn);
                h.Want(idSledRide, (1f - tw) * (1f - tuck));
                h.Want(idSledTuck, (1f - tw) * tuck);
                h.Want(turn > 0f ? idSledLeanR : idSledLeanL, tw);
                // a landing (and each skip after it) folds the rider over their knees
                if (idSledBump >= 0 && landT < h.Length(idSledBump)) h.Want(idSledBump, Mathf.Max(0.4f, landK) * 2f * (1f - Sm(0.4f, 1f, landT / h.Length(idSledBump))));
                Vector3 v = rider.Velocity; v.y = 0f;
                if (v.sqrMagnitude > 9f && tuck < 0.5f)
                {
                    d.look = Quaternion.AngleAxis(rider.YawRate * Mathf.Rad2Deg * 0.35f, Vector3.up) * v + Vector3.down * v.magnitude * 0.1f;
                    d.lookWeight = 0.5f;
                }
            }
            popT += dt; landT += dt;
            heroLift = 0f;
            if (dt <= 0f) h.Snap();
            h.Evaluate(dt, blend);
            h.Layer(d, dt);
            KeepOffSnow(dt, false);   // nothing through the snow (RiderBodyTouch)
        }
    }
}
