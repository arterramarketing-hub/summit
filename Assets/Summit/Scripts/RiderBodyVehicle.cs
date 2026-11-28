using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The fun rides' bodies (rides): the kayak and its paddle and the rally car (Resources/Rider/Rides.fbx, built by
    /// Tools/MakeRidesVehicles.py with the rider's Kayak_, Car_ and Bear_ clips), and the bear (the wildlife's own,
    /// Resources/Animals/Bear.fbx, its gallop made of shapes). Like the toboggan (RiderBodyRide), each is under the roll
    /// and turned to face the way the rider goes; RiderBodyHero hands a frame to PoseVehicle when one is on.
    ///
    /// The kayak: the rider sits in its spray skirt (the legs, out of sight inside the hull, are not drawn: HideLegs); the paddle is held in both hands, its shaft through both grips
    /// (placed each frame from the hands), so whatever the clips do with the arms the paddle goes with them. A stroke
    /// (Rider.PaddleSide) plays its clip in step with the physics' own stroke. Spun out (Rider.SpinOut), the hull and
    /// the rider go round together (RiderBody.Apply turns the whole rider).
    ///
    /// The car: the driver in the bucket seat, hands on the steering wheel, which turns with the steering; the front
    /// wheels steer, all four spin with the speed and each is held on the snow under it on its spring, while the body
    /// rolls on its springs to the outside of a turn (Rider.Tip) and bounces off a landing (RiderBody.Apply).
    ///
    /// The bear: astride its back with a fist in its fur. The gallop's shapes are played from the physics' own cycle
    /// (Rider.Gait); the bear's back rises and falls and surges through each stride, measured once from the model (a
    /// table of where the top of its back is through the cycle), so the rider is carried up and down with it and the
    /// bear is moved under the seat to take out the surge. In a crash the rider is thrown and the bear runs on by
    /// itself, slows and stops, and is back under them when they get up.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>The top of the bear's back under the seat, over the Root (Tools/MakeRidesVehicles.py: BEAR_SADDLE).</summary>
        public const float BearSaddle = 1.22f;
        /// <summary>The steering wheel's turn at full lock (deg: MakeRidesVehicles' CAR_STEER_TURN), and its column's tilt.</summary>
        public const float CarSteerTurn = 85f, CarSteerTilt = 24f;
        /// <summary>Each hand's grip on the paddle, from the shaft's middle (GRIP_HALF), and how far under the wrist the fist closes.</summary>
        const float GripHalf = 0.34f, GripBelow = 0.065f;
        const float WheelRadius = 0.31f;
        /// <summary>Where in the gallop the bear stands while it waits (its feet under it).</summary>
        public static float BearIdleGait = 0.6f;

        Transform kayakRoot, kayakHull, paddle, carRoot, carBody, steerPivot, bearRoot, bearSurge, bearYaw, bearWrap, bearDummy;
        readonly Transform[] wheel = new Transform[4];          // FL FR RL RR: each a pivot at its middle
        readonly Vector3[] wheelRest = new Vector3[4];
        Quaternion steerRest = Quaternion.identity;
        Vector3 steerAxisLocal;
        bool kayakGear, carGear, bearGear;
        float wheelTurn, carBounce, carBounceV, carRollNow;
        Animation bearAnim; AnimationState bearState; float bearLen;
        float[] bearLift, bearRun;                                // the top of its back over the cycle: up (m) and along (m), from the mean
        bool bearLoose; Vector3 bearLooseV; float bearLooseGait;

        // the rider's clips on each
        int idKIdle, idKRide, idKLeanL, idKLeanR, idKTuck, idKAir, idKUp, idKStrokeL, idKStrokeR;
        int idCIdle, idCDrive, idCSteerL, idCSteerR, idCAir, idCWave, idCBump;
        int idBIdle, idBRide, idBLeanL, idBLeanR, idBTuck, idBAir, idBRodeo, idBBump;

        // what flies off in a crash (each piece's own frame: z along it, y up)
        static readonly Vector3 KayakBox = new Vector3(0.62f, 0.36f, 3.05f), KayakBoxAt = new Vector3(0f, 0.12f, 0.07f);
        static readonly Vector3 PaddleBox = new Vector3(2.16f, 0.19f, 0.05f), PaddleBoxAt = Vector3.zero;
        static readonly Vector3 CarBox = new Vector3(1.62f, 1.36f, 3.46f), CarBoxAt = new Vector3(0f, 0.63f, 0.07f);

        public Transform KayakGear => kayakRoot;
        public Transform CarGear => carRoot;
        public Transform BearGear => bearRoot;
        /// <summary>Is this ride's gear there (built: its meshes, its clips)?</summary>
        public bool HasRide(RideKind k) => k switch
        {
            RideKind.Skis => rideGear, RideKind.Toboggan => sledGear, RideKind.Kayak => kayakGear, RideKind.Car => carGear, RideKind.Bear => bearGear, RideKind.Bike => bikeGear, _ => true,
        };
        bool OnVehicle => RidingGear && Rides.Of(Ride).Vehicle;
        /// <summary>On a kayak, a car or a bear now (not on foot, not in the tube): for the sound.</summary>
        public bool OnVehicleNow => OnVehicle;

        /// <summary>SetUpRides' part for the fun rides: the hero placed side-on at rest, the file's pieces in `go` where the tool drew them.</summary>
        void SetUpVehicles(HeroRig h, GameObject go, Vector3 origin, Quaternion square, Vector3 deck)
        {
            Transform Find(string n) { foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
            // each piece in a holder turned as the sled is: the hero's facing round to the nose, about the deck
            Transform Hold(string name, Transform under, Transform mesh)
            {
                var holder = new GameObject(name).transform;
                holder.SetPositionAndRotation(origin, square);
                mesh.SetParent(holder, true);
                holder.SetParent(under, true);
                holder.localRotation = RideTurn * holder.localRotation;
                holder.localPosition = deck + RideTurn * (holder.localPosition - deck);
                return holder;
            }
            Transform Root(string name) { var r = new GameObject(name).transform; r.SetParent(roll, false); return r; }

            // ---- the kayak and its paddle
            idKIdle = h.Id("Kayak_Idle"); idKRide = h.Id("Kayak_Ride"); idKLeanL = h.Id("Kayak_LeanL"); idKLeanR = h.Id("Kayak_LeanR");
            idKTuck = h.Id("Kayak_Tuck"); idKAir = h.Id("Kayak_Air"); idKUp = h.Id("Kayak_PaddleUp"); idKStrokeL = h.Id("Kayak_StrokeL"); idKStrokeR = h.Id("Kayak_StrokeR");
            Transform hullMesh = Find("Kayak"), paddleMesh = Find("Paddle");
            if (hullMesh != null && paddleMesh != null && idKIdle >= 0 && idKRide >= 0 && idKStrokeL >= 0 && idKStrokeR >= 0 && idKLeanL >= 0 && idKAir >= 0)
            {
                kayakRoot = Root("Kayak");
                kayakHull = Hold("Hull", kayakRoot, hullMesh);
                paddle = Hold("Paddle", kayakRoot, paddleMesh);
                kayakGear = true;
            }
            else Debug.LogWarning("Rides: no kayak in Rides.fbx or its clips in HeroAnim.bytes (run Tools/MakeRides.py)");

            // ---- the rally car: its body, the steering wheel on its column, a pivot in the middle of each wheel
            idCIdle = h.Id("Car_Idle"); idCDrive = h.Id("Car_Drive"); idCSteerL = h.Id("Car_SteerL"); idCSteerR = h.Id("Car_SteerR");
            idCAir = h.Id("Car_Air"); idCWave = h.Id("Car_Wave"); idCBump = h.Id("Car_Bump");
            Transform bodyMesh = Find("CarBody"), steerMesh = Find("CarSteer");
            string[] wn = { "CarWheelFL", "CarWheelFR", "CarWheelRL", "CarWheelRR" };
            var wm = new Transform[4];
            bool all = bodyMesh != null && steerMesh != null && idCIdle >= 0 && idCDrive >= 0 && idCSteerL >= 0 && idCSteerR >= 0 && idCAir >= 0;
            for (int i = 0; i < 4; i++) { wm[i] = Find(wn[i]); all &= wm[i] != null; }
            if (all)
            {
                carRoot = Root("Car");
                carBody = Hold("Body", carRoot, bodyMesh);
                Transform Pivot(string name, Transform mesh)
                {
                    var r = mesh.GetComponent<Renderer>();
                    var pv = new GameObject(name).transform;
                    pv.SetParent(carBody, false);
                    pv.position = r != null ? r.bounds.center : mesh.position;
                    pv.localRotation = Quaternion.identity;
                    mesh.SetParent(pv, true);
                    return pv;
                }
                for (int i = 0; i < 4; i++) { var held = Hold(wn[i], carRoot, wm[i]); held.SetParent(carBody, true); wheel[i] = Pivot("Pivot" + wn[i], wm[i]); wheelRest[i] = wheel[i].localPosition; DestroyImmediate(held.gameObject); }
                var sh = Hold("CarSteer", carRoot, steerMesh); sh.SetParent(carBody, true);
                steerPivot = Pivot("PivotSteer", steerMesh); DestroyImmediate(sh.gameObject);
                steerRest = steerPivot.localRotation;
                // the column: in the tool's axes (0, cos, sin) from the wheel toward the driver; here z is the nose, y up
                float tl = CarSteerTilt * Mathf.Deg2Rad;
                steerAxisLocal = new Vector3(0f, Mathf.Sin(tl), -Mathf.Cos(tl));
                carGear = true;
            }
            else Debug.LogWarning("Rides: no rally car in Rides.fbx or its clips in HeroAnim.bytes (run Tools/MakeRides.py)");

            // ---- the bear (the wildlife's model) and what the rider does on it
            idBIdle = h.Id("Bear_Idle"); idBRide = h.Id("Bear_Ride"); idBLeanL = h.Id("Bear_LeanL"); idBLeanR = h.Id("Bear_LeanR");
            idBTuck = h.Id("Bear_Tuck"); idBAir = h.Id("Bear_Air"); idBRodeo = h.Id("Bear_Rodeo"); idBBump = h.Id("Bear_Bump");
            if (idBIdle >= 0 && idBRide >= 0 && idBLeanL >= 0 && idBLeanR >= 0 && idBAir >= 0) BuildBear();
            else Debug.LogWarning("Rides: the bear's clips are not in HeroAnim.bytes (run Tools/MakeRides.py)");

            // ---- the mountain bike (mtb: RiderBodyBike)
            SetUpBike(h, go, origin, square, deck);

            if (!vehHooked)
            {
                vehHooked = true;   // (once: these act on whichever body is built now)
                rider.Landed += impact =>
                {
                    if (hero == null || !OnVehicle || impact <= 1.5f) return;
                    hero.Restart(Ride == RideKind.Car ? idCBump : Ride == RideKind.Bear ? idBBump : Ride == RideKind.Bike ? idBkBump : -1);
                    if (Ride == RideKind.Car) carBounceV -= Mathf.Clamp(impact * 0.09f, 0.3f, 1.6f);   // onto its springs
                };
            }
        }
        bool vehHooked;

        /// <summary>The bear: the wildlife's model under the seat, its gallop measured once (its back through the cycle).</summary>
        void BuildBear()
        {
            var prefab = Resources.Load<GameObject>("Animals/Bear");
            if (prefab == null) return;
            bearRoot = new GameObject("Bear").transform; bearRoot.SetParent(roll, false);
            bearSurge = new GameObject("BearSurge").transform; bearSurge.SetParent(bearRoot, false);
            bearYaw = new GameObject("BearYaw").transform; bearYaw.SetParent(bearSurge, false);
            // (the file's head is toward -z: Wildlife.BearYawFix)
            bearYaw.localRotation = Quaternion.Euler(0f, Wildlife.BearYawFix, 0f);
            // (the file's clip keys the model root's own place and scale, x100: the sizing goes on a wrapper above it)
            bearWrap = new GameObject("BearWrap").transform; bearWrap.SetParent(bearYaw, false);
            var model = Instantiate(prefab, bearWrap, false);
            model.name = "BearModel";
            foreach (var c in model.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            bearAnim = model.GetComponentInChildren<Animation>();
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (bearAnim == null || smr == null) { DestroyImmediate(bearRoot.gameObject); bearRoot = null; return; }
            foreach (AnimationState st in bearAnim) { bearState = st; break; }   // (one clip: the gallop)
            if (bearState == null) { DestroyImmediate(bearRoot.gameObject); bearRoot = null; return; }
            var vmat = Resources.Load<Material>("VertexColor");   // (the wildlife bear's own: nothing new to draw it)
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (vmat != null) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = vmat; r.sharedMaterials = ms; }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            smr.updateWhenOffscreen = true;
            bearLen = Mathf.Max(0.05f, bearState.length);
            bearState.enabled = true; bearState.weight = 1f; bearState.speed = 0f; bearState.wrapMode = WrapMode.Loop;
            bearAnim.Play(bearState.name);

            // measure the back through the cycle: the top of the back over its middle (the saddle), tracked by the
            // same few vertices from the first sample on. (A baked skin with no bones comes out in its renderer's own
            // space without that renderer's scale, but it is drawn with it: the clip's x100 on the model's root. Taken
            // as baked, the bear was sized 100 times too big; the shot tools bake too, so only the phone showed it: rides2)
            const int N = 24;
            var baked = new Mesh();
            var verts = new System.Collections.Generic.List<Vector3>();
            int[] track = null;
            var saddle = new Vector3[N];
            var floor = new float[N];
            for (int i = 0; i < N; i++)
            {
                bearState.time = bearLen * i / N; bearAnim.Sample();
                smr.BakeMesh(baked, true);
                baked.GetVertices(verts);
                var tr = smr.transform;
                if (track == null)
                {
                    var pts = new Vector3[verts.Count];
                    Vector3 lo = Vector3.one * 1e9f, hi = -lo;
                    for (int k = 0; k < verts.Count; k++) { pts[k] = bearYaw.InverseTransformPoint(tr.localToWorldMatrix.MultiplyPoint3x4(verts[k])); lo = Vector3.Min(lo, pts[k]); hi = Vector3.Max(hi, pts[k]); }
                    float cx = (lo.x + hi.x) * 0.5f, cz = (lo.z + hi.z) * 0.5f, w = hi.x - lo.x, len = hi.z - lo.z;
                    // the back's middle: a little behind the middle of its length (its head is at +z), near its centre line
                    var cand = new System.Collections.Generic.List<int>();
                    for (int k = 0; k < pts.Length; k++)
                        if (Mathf.Abs(pts[k].x - cx) < 0.12f * w && pts[k].z > cz - 0.14f * len && pts[k].z < cz + 0.04f * len) cand.Add(k);
                    cand.Sort((a, b) => pts[b].y.CompareTo(pts[a].y));
                    track = cand.GetRange(0, Mathf.Min(6, cand.Count)).ToArray();
                    if (track.Length == 0) break;
                }
                Vector3 s = Vector3.zero;
                foreach (int k in track) s += bearYaw.InverseTransformPoint(tr.localToWorldMatrix.MultiplyPoint3x4(verts[k]));
                saddle[i] = s / track.Length;
                float lowest = float.MaxValue;
                foreach (var vv in verts) lowest = Mathf.Min(lowest, bearYaw.InverseTransformPoint(tr.localToWorldMatrix.MultiplyPoint3x4(vv)).y);
                floor[i] = lowest;
            }
            DestroyImmediate(baked);
            if (track == null || track.Length == 0) { DestroyImmediate(bearRoot.gameObject); bearRoot = null; return; }
            Vector3 mean = Vector3.zero;
            foreach (var s in saddle) mean += s;
            mean /= N;
            // sized so its back is at BearSaddle over the Root on average, and stood with that under the seat (the hips
            // are 6 cm behind the Root), its feet on the snow (the file's ground is its own y = 0)
            // (its ground: where its lowest paw is through the cycle, the middle of them: a paw is on the snow most of the time)
            System.Array.Sort(floor);
            float ground = floor[N / 2];
            float scale = mean.y - ground > 1e-3f ? (BoardTop + BearSaddle) / (mean.y - ground) : 1f;
            bearWrap.localScale = Vector3.one * scale;
            Vector3 m = Quaternion.Euler(0f, Wildlife.BearYawFix, 0f) * new Vector3(mean.x, 0f, mean.z) * scale;   // (the saddle in the surge's frame)
            bearWrap.localPosition = Quaternion.Inverse(bearYaw.localRotation) * new Vector3(-m.x, 0f, -0.06f - m.z) - Vector3.up * (ground * scale);
            bearLift = new float[N]; bearRun = new float[N];
            for (int i = 0; i < N; i++)
            {
                bearLift[i] = (saddle[i].y - mean.y) * scale;
                bearRun[i] = (Quaternion.Euler(0f, Wildlife.BearYawFix, 0f) * (saddle[i] - mean)).z * scale;
            }
            // (left stood waiting, not at the last sample's bound: the closet's copy is taken as it is: rides2)
            bearState.time = BearIdleGait * bearLen; bearAnim.Sample();
            bearDummy = new GameObject("BearThrown").transform; bearDummy.SetParent(roll, false);   // (what the ragdoll throws: nothing to see)
            bearGear = true;
        }

        /// <summary>The bear's back through its cycle at g (0..1): how far up from its mean (m), how far ahead.</summary>
        void BearBack(float g, out float up, out float ahead)
        {
            up = ahead = 0f;
            if (bearLift == null) return;
            int n = bearLift.Length;
            float x = Mathf.Repeat(g, 1f) * n;
            int i = (int)x % n, j = (i + 1) % n; float f = x - Mathf.Floor(x);
            up = Mathf.Lerp(bearLift[i], bearLift[j], f); ahead = Mathf.Lerp(bearRun[i], bearRun[j], f);
        }

        void DropVehicles()
        {
            foreach (var t in new[] { kayakRoot, carRoot, bearRoot, bearDummy }) if (t != null) DestroyImmediate(t.gameObject);
            kayakRoot = kayakHull = paddle = carRoot = carBody = steerPivot = bearRoot = bearSurge = bearYaw = bearWrap = bearDummy = null;
            for (int i = 0; i < 4; i++) wheel[i] = null;
            bearAnim = null; bearState = null; bearLift = bearRun = null; bearLoose = false;
            kayakGear = carGear = bearGear = false;
            DropBike();
            if (legsHidden) HideLegs(false);
            legSkins = null; legsHidden = false;
        }

        /// <summary>ShowRide's part: the vehicle ridden, and none on foot or in the tube.</summary>
        void ShowVehicles(bool on)
        {
            if (kayakRoot != null) kayakRoot.gameObject.SetActive(on && Ride == RideKind.Kayak);
            if (!(on && Ride == RideKind.Kayak)) HideLegs(false);
            if (carRoot != null) carRoot.gameObject.SetActive(on && Ride == RideKind.Car);
            if (bearRoot != null) bearRoot.gameObject.SetActive((on || bearLoose) && Ride == RideKind.Bear);
            if (bikeRoot != null) bikeRoot.gameObject.SetActive(on && Ride == RideKind.Bike);
        }

        /// <summary>RideRagdoll's part: what flies off in a crash. True if it was one of these.</summary>
        bool VehicleRagdoll()
        {
            if (BikeRagdoll()) return true;   // (the whole bike flies off: RiderBodyBike)
            if (Ride == RideKind.Kayak && kayakGear)
            {
                ragdoll.SetBoard(kayakHull, KayakBox, KayakBoxAt);
                ragdoll.SetExtras(new[] { new Ragdoll.Loose { t = paddle, size = PaddleBox, center = PaddleBoxAt, mass = 1f } });
                return true;
            }
            if (Ride == RideKind.Car && carGear)
            {
                ragdoll.SetBoard(carBody, CarBox, CarBoxAt);
                ragdoll.SetExtras(null);
                return true;
            }
            if (Ride == RideKind.Bear && bearGear)
            {
                // the bear is not thrown: it runs on by itself (BearLooseStep). The ragdoll is given an empty to throw
                ragdoll.SetBoard(bearDummy, new Vector3(0.2f, 0.2f, 0.2f), Vector3.zero);
                ragdoll.SetExtras(null);
                return true;
            }
            return false;
        }

        /// <summary>Apply's part: the vehicle's tilt and bounce (on the roll, so the rider goes with it).</summary>
        void VehicleRoll(float leanDeg, float dt)
        {
            upper.localRotation = Quaternion.identity;
            switch (Ride)
            {
                case RideKind.Kayak:
                    // a hull has no edge: it tips a little onto its side, the rider leans over the rudder (in the clips)
                    roll.localRotation = Quaternion.Euler(0f, 0f, -leanDeg * 0.15f);
                    roll.localPosition = Vector3.zero;
                    break;
                case RideKind.Car:
                {
                    // over on its springs to the outside of the turn (Rider.Tip), and a bounce (landings: carBounceV)
                    carBounceV += (-carBounce * 120f - carBounceV * 9f) * dt;
                    carBounce = Mathf.Clamp(carBounce + carBounceV * dt, -0.14f, 0.12f);
                    float tip = rider.Airborne ? 0f : Mathf.Clamp(rider.Tip, -2.4f, 2.4f);
                    float want = -tip * 3.2f;   // (Tip + to the right: the body rolls to the right, which is - about z)
                    carRollNow = dt <= 0f ? want : Mathf.Lerp(carRollNow, want, 1f - Mathf.Exp(-dt * 8f));
                    roll.localRotation = Quaternion.Euler(0f, 0f, carRollNow);
                    roll.localPosition = Vector3.up * carBounce;
                    break;
                }
                case RideKind.Bear:
                    // an animal banks into its turn
                    roll.localRotation = Quaternion.Euler(0f, 0f, -leanDeg * 0.35f);
                    roll.localPosition = Vector3.zero;
                    break;
                case RideKind.Bike:
                    BikeRoll(leanDeg, dt);   // (leant under the rider; its back out in a skid: RiderBodyBike)
                    break;
            }
        }

        /// <summary>The rider on a kayak, a car or a bear, posed for this frame (PoseHero's place).</summary>
        void PoseVehicle(float dt, bool down)
        {
            var h = hero;
            model.localRotation = RideTurn * heroBaseRot;
            Transform root = Ride == RideKind.Kayak ? kayakRoot : Ride == RideKind.Car ? carRoot : Ride == RideKind.Bike ? bikeRoot : bearRoot;
            // on the bear: carried up and down with its back
            float bob = 0f, surge = 0f;
            if (Ride == RideKind.Bear)
            {
                if (bearLoose && !down) BearHome();
                if (!down)
                {
                    float g = rider.Running ? rider.Gait : BearIdleGait;
                    BearBack(g, out bob, out surge);
                    bob *= 0.85f;   // (the rider's seat soaks a little of it up)
                    if (bearState != null) bearState.time = g * bearLen;
                    if (bearSurge != null) bearSurge.localPosition = new Vector3(0f, 0f, -surge);
                }
            }
            model.localPosition = rideModelPos + Vector3.up * bob;
            HideLegs(Ride == RideKind.Kayak && !down);
            var d = new HeroRig.Drive
            {
                board = bearLoose ? roll : root, rootInBoard = new Vector3(0f, BoardTop + bob, 0f), rootRotInBoard = RideTurn * heroRootRot,
                up = roll.up, toe = roll.forward, nose = -roll.right, feet = !down,
            };
            float blend = 9f;
            Flutter(dt);
            bool idle = !rider.Running, air = rider.Airborne;
            float turn = Mathf.Clamp(rider.Lean / Rider.MaxLean, -1f, 1f);   // + = turning right
            int iIdle, iRide, iL, iR, iTuck, iAir, iTrick, iBump;
            switch (Ride)
            {
                case RideKind.Kayak: iIdle = idKIdle; iRide = idKRide; iL = idKLeanL; iR = idKLeanR; iTuck = idKTuck; iAir = idKAir; iTrick = idKUp; iBump = -1; break;
                case RideKind.Car: iIdle = idCIdle; iRide = idCDrive; iL = idCSteerL; iR = idCSteerR; iTuck = idCDrive; iAir = idCAir; iTrick = idCWave; iBump = idCBump; break;
                case RideKind.Bike: BikeClips(out iIdle, out iRide, out iL, out iR, out iTuck, out iAir, out iTrick, out iBump); break;
                default: iIdle = idBIdle; iRide = idBRide; iL = idBLeanL; iR = idBLeanR; iTuck = idBTuck; iAir = idBAir; iTrick = idBRodeo; iBump = idBBump; break;
            }
            if (down) h.Want(idFlail, 1f);
            else if (idle) h.Want(iIdle, 1f);
            else if (air)
            {
                blend = 11f;
                float free = rider.TrickGrab > 0 ? rider.GrabPose : 0f;   // (the trick: paddle up, a wave out of the window, a rodeo hand)
                h.Want(iAir, Mathf.Max(0.05f, 1f - free));
                h.Want(iTrick, free * 3f);
            }
            else
            {
                float tuck = rider.Tuck, tw = Mathf.Abs(turn);
                // (the bike pedalled: its pedal clip in step with the stroke, in place of the ride and the tuck)
                float pedal = Ride == RideKind.Bike && rider.Pedalling ? 1f : 0f;
                h.Want(iRide, (1f - tw) * (1f - tuck) * (1f - pedal));
                h.Want(iTuck, (1f - tw) * tuck * (1f - pedal));
                if (pedal > 0f) BikePedalling(h, 1f - tw * 0.6f);
                h.Want(turn > 0f ? iR : iL, tw);
                // a paddle stroke, in step with the physics' own (Rider.PaddleSide, PaddleT)
                if (Ride == RideKind.Kayak && rider.PaddleSide != 0)
                {
                    int id = rider.PaddleSide > 0 ? idKStrokeL : idKStrokeR;
                    h.SetTime(id, rider.PaddleT * h.Length(id));
                    h.Want(id, 3f * Mathf.Clamp01(Mathf.Min(rider.PaddleT / 0.1f, (1f - rider.PaddleT) / 0.15f)));
                }
                if (iBump >= 0 && landT < h.Length(iBump)) h.Want(iBump, Mathf.Max(0.4f, landK) * 2f * (1f - Sm(0.4f, 1f, landT / h.Length(iBump))));
                Vector3 v = rider.Velocity; v.y = 0f;
                if (v.sqrMagnitude > 9f && tuck < 0.5f && Ride != RideKind.Car)
                {
                    d.look = Quaternion.AngleAxis(rider.YawRate * Mathf.Rad2Deg * 0.35f, Vector3.up) * v + Vector3.down * v.magnitude * 0.1f;
                    d.lookWeight = 0.45f;
                }
            }
            popT += dt; landT += dt;
            heroLift = 0f;
            if (dt <= 0f) h.Snap();
            h.Evaluate(dt, blend);
            if (Ride == RideKind.Bike) BikeHold(ref d, down, dt);   // (the bike thrown about in a trick, the hands on its grips: RiderBodyBike)
            h.Layer(d, dt);
            if (Ride == RideKind.Kayak) PaddleFrame();
            else if (Ride == RideKind.Car) CarFrame(dt, turn);
            else if (Ride == RideKind.Bike) BikeFrame(dt);
            KeepOffSnow(dt, false);   // nothing through the snow (RiderBodyTouch)
        }

        /// <summary>The paddle between the two fists: its middle half way between the grips, its shaft through both.</summary>
        void PaddleFrame()
        {
            if (paddle == null || hero == null || rider.Crashed) return;
            Vector3 up = roll.up;
            Vector3 gl = hero.Hand[0].position - up * GripBelow, gr = hero.Hand[1].position - up * GripBelow;
            Vector3 u = gl - gr;
            if (u.sqrMagnitude < 1e-4f) return;
            u.Normalize();
            // the tool draws the left blade toward its -x here and the blades' power faces toward -z: the shaft along the
            // grips, the faces back toward the rider
            Vector3 x = -u, f = roll.forward - u * Vector3.Dot(roll.forward, u);
            if (f.sqrMagnitude < 1e-4f) f = up;
            f.Normalize();
            paddle.SetPositionAndRotation((gl + gr) * 0.5f, Quaternion.LookRotation(f, Vector3.Cross(f, x)));
        }

        /// <summary>The car's wheels and steering wheel: the steering, the wheels spun by the speed, each on the snow under it.</summary>
        void CarFrame(float dt, float turn)
        {
            if (carBody == null || rider.Crashed) return;
            float steer = -turn * CarSteerTurn;   // (+: the wheel's top to the driver's left, a left turn)
            if (steerPivot != null) steerPivot.localRotation = Quaternion.AngleAxis(-steer, steerAxisLocal) * steerRest;
            if (rider.Running) wheelTurn = Mathf.Repeat(wheelTurn + rider.Speed / WheelRadius * Mathf.Rad2Deg * dt * (rider.Airborne && rider.Throttle > 0.5f ? 1.4f : 1f), 360f);
            Vector3 up = carBody.up;
            for (int i = 0; i < 4; i++)
            {
                if (wheel[i] == null) continue;
                float yaw = i < 2 ? turn * 24f : 0f;
                wheel[i].localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(wheelTurn, 0f, 0f);
                // on its spring: the tyre on the snow under it, as far as the spring goes (hanging down in the air)
                wheel[i].localPosition = wheelRest[i];
                Vector3 c = wheel[i].position;
                float give;
                if (rider.Airborne || !rider.Running) give = rider.Airborne ? -0.08f : 0f;
                else give = (Slope.DrawnHeight(c.x, c.z) + WheelRadius - c.y) / Mathf.Max(0.5f, up.y);
                give = Mathf.Clamp(give, -0.12f, 0.14f);
                wheel[i].localPosition = wheelRest[i] + carBody.InverseTransformVector(up * give);
            }
        }

        // ---------------------------------------------------------------- the bear, thrown off

        /// <summary>Thrown off: the bear runs on by itself on the snow, slowing to a stop.</summary>
        void BearLooseStep(float dt)
        {
            if (bearRoot == null) return;
            if (!bearLoose)
            {
                bearLoose = true;
                var v = lastVel; v.y = 0f;   // (going as the rider was when it happened: what the ragdoll is thrown with)
                bearLooseV = v; bearLooseGait = rider.Gait;
                bearRoot.SetParent(null, true);
                bearRoot.gameObject.SetActive(true);
                if (bearSurge != null) bearSurge.localPosition = Vector3.zero;
            }
            float sp = bearLooseV.magnitude;
            if (sp > 0.05f) bearLooseV = bearLooseV * Mathf.Max(0f, sp - 7f * dt) / sp;
            Vector3 p = bearRoot.position + bearLooseV * dt;
            p.y = Slope.DrawnHeight(p.x, p.z);
            Vector3 n = Slope.Normal(p.x, p.z);
            Vector3 f = sp > 0.3f ? bearLooseV / sp : bearRoot.forward;
            f = Vector3.ProjectOnPlane(f, n);
            if (f.sqrMagnitude > 1e-4f) bearRoot.SetPositionAndRotation(p, Quaternion.LookRotation(f.normalized, n));
            else bearRoot.position = p;
            // the gallop slows with it, and it stands
            if (sp > 0.6f) bearLooseGait = Mathf.Repeat(bearLooseGait + Mathf.Clamp(sp / Rider.BearStride, 0.4f, 2.6f) * dt, 1f);
            else bearLooseGait = Mathf.MoveTowards(bearLooseGait, BearIdleGait, dt * 0.6f);
            if (bearState != null) bearState.time = bearLooseGait * bearLen;
        }

        /// <summary>Back up: the bear is back under the rider.</summary>
        void BearHome()
        {
            bearLoose = false;
            if (bearRoot == null) return;
            bearRoot.SetParent(roll, false);
            bearRoot.localPosition = Vector3.zero; bearRoot.localRotation = Quaternion.identity; bearRoot.localScale = Vector3.one;
            ShowRide();
        }

        /// <summary>Where the loose bear is (or the rider's, while ridden): for the test tools.</summary>
        public Vector3 BearAt => bearRoot != null ? bearRoot.position : transform.position;

        // ---------------------------------------------------------------- the kayaker's legs
        // Sat in the kayak the legs are under its deck, out of sight; baggy trousers on straight legs in a narrow hull
        // would poke out through its sides as the body turns with a stroke. So while sat in it they are not drawn (and
        // not skinned): back as soon as the rider is thrown out of it, or off it.
        SkinnedMeshRenderer[] legSkins;
        bool legsHidden;
        void HideLegs(bool hide)
        {
            if (legsHidden == hide) return;
            if (legSkins == null && model != null)
            {
                var all = new System.Collections.Generic.List<SkinnedMeshRenderer>();
                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name.StartsWith("HeroLegs")) all.Add(r);
                legSkins = all.ToArray();
            }
            if (legSkins == null) return;
            foreach (var r in legSkins) if (r != null) r.enabled = !hide;
            legsHidden = hide;
        }
        /// <summary>Test tools: the legs are not drawn (sat in the kayak).</summary>
        public bool LegsHidden => legsHidden;
        public bool BearLoose => bearLoose;
    }
}
