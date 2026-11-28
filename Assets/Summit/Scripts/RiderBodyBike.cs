using UnityEngine;

namespace Summit
{
    /// <summary>
    /// (mtb) The mountain bike's body: a fat bike (Resources/Rider/Rides.fbx: BikeFrame, BikeFork, BikeWheelF/R,
    /// BikeCrank, BikePedalL/R, built by Tools/MakeRidesBike.py with the rider's Bike_ clips), under the roll like the
    /// other rides, turned to face the way the rider goes.
    ///  - The wheels spin with the speed; the bar steers a little with the lean, and in a barspin goes right round.
    ///  - The cranks follow the feet: the clips stand the rider on the pedals, and the crank's angle is read off
    ///    whichever foot is on its pedal (as the tool reads it), so pedalling (Bike_Pedal, played in step with the
    ///    physics' own stroke: Rider.PedalT) turns them, and every other pose leaves them where the feet are. The pedals
    ///    sit at the arms' ends, level.
    ///  - The hands are held on the grips wherever the bar is (steered, spun, the bike thrown about in a trick), except
    ///    in the no-hander and the barspin, when they let go.
    ///  - A tabletop, a whip or a superman turns the bike about BikePivot by that trick's turn (MakeRidesBike's
    ///    TRICK_TURN), as far as the trick is in; the clip's feet are on its pedals where that leaves them.
    ///  - Leaning into a turn the bike leans under the rider; braked into a skid its back steps out (Rider.BikeSkid).
    ///  - In a crash the bike flies off (the ragdoll throws it) and is back under the rider when they get up.
    /// The tool's axes (z up, the rider facing -y, x their left) are the bike's own here as (-x, z, -y): z its nose,
    /// y up, x its right; a turn by an angle about a tool axis is the turn by minus that about the axis as it is here
    /// (the one is right-handed, the other left).
    /// </summary>
    public partial class RiderBody
    {
        // the tool's numbers (Tools/MakeRidesBike.py)
        static readonly Vector3 TFrontAxle = new Vector3(0f, -0.75f, 0.32f), TRearAxle = new Vector3(0f, 0.44f, 0.32f), TBB = new Vector3(0f, 0.02f, 0.27f);
        static readonly Vector3 THtTop = new Vector3(0f, -0.47f, 0.86f), THtBot = new Vector3(0f, -0.53f, 0.70f), TPivot = new Vector3(0f, -0.42f, 0.90f);
        const float TCrank = 0.17f, TQHalf = 0.118f, TGripX = 0.345f, TGripY = -0.495f, TGripZ = 0.943f, TPedBehind = 0.085f, TPedAbove = 0.107f, TWheelR = 0.37f;
        /// <summary>The turns the tricks give the bike: yaw about up, roll about the bike's length, pitch about its width (deg, the tool's).</summary>
        static readonly Vector3[] BikeTrickTurn = { new Vector3(10f, -48f, 0f), new Vector3(40f, 22f, 0f), new Vector3(0f, 0f, 6f), Vector3.zero, Vector3.zero };
        /// <summary>How far the bar turns at full lean (deg), and how fast it spins round in a barspin (deg/s).</summary>
        public static float BikeSteerTurn = 10f, BarspinRate = 900f;
        static readonly Vector3 BikeBox = new Vector3(0.55f, 1.05f, 1.95f);

        Transform bikeRoot, bikeTrick, bikeFork, bikeWheelF, bikeWheelR, bikeCrank, bikeGripL, bikeGripR;
        readonly Transform[] bikePedal = new Transform[2];
        readonly Vector3[] bikePedalRest = new Vector3[2];
        Quaternion bikeForkRest = Quaternion.identity, bikeCrankRest = Quaternion.identity, bikeWheelFRest = Quaternion.identity, bikeWheelRRest = Quaternion.identity;
        Vector3 bikeOffset, bikeSteerAxis, bikeBoxAt;
        bool bikeGear;
        float bikeWheelTurn, bikeBarspin, bikeCrankNow, bikeSkidYaw;
        int idBkIdle, idBkRide, idBkPedal, idBkLeanL, idBkLeanR, idBkTuck, idBkAir, idBkBump;
        readonly int[] idBkTrick = new int[5];

        public Transform BikeGear => bikeRoot;
        /// <summary>The test tools: the crank's angle now (deg, 0 the left arm forward, 90 down), the bar's turn, the barspin's.</summary>
        public float BikeCrank => bikeCrankNow;
        public float BikeBarspin => bikeBarspin;
        public Transform BikeFrameNow => bikeTrick;

        /// <summary>A point of the tool's in the bike's own frame (the root's).</summary>
        Vector3 BikeP(Vector3 t) => new Vector3(-t.x, t.z, -t.y) + bikeOffset;
        static Vector3 BikeDir(Vector3 t) => new Vector3(-t.x, t.z, -t.y);
        static Vector3 PedalAt(int sd, float a)
        {
            float r = a * Mathf.Deg2Rad;
            return TBB + new Vector3(sd * TQHalf, 0f, 0f) + new Vector3(0f, -Mathf.Cos(r), -Mathf.Sin(r)) * (TCrank * sd);
        }
        /// <summary>The bike's turn for trick i (TrickGrab - 1), k of the way: the tool's yaw, roll, pitch, as turns here.</summary>
        static Quaternion BikeTrickQ(int i, float k)
        {
            if (i < 0 || i >= BikeTrickTurn.Length) return Quaternion.identity;
            var t = BikeTrickTurn[i] * k;
            // (tool z -> up: -yaw about up; tool y -> -nose: +roll about the nose; tool x -> -right: +pitch about the right)
            return Quaternion.AngleAxis(-t.x, Vector3.up) * Quaternion.AngleAxis(t.y, Vector3.forward) * Quaternion.AngleAxis(t.z, Vector3.right);
        }

        /// <summary>SetUpVehicles' part: the bike's pieces out of the file, each on its own pivot.</summary>
        void SetUpBike(HeroRig h, GameObject go, Vector3 origin, Quaternion square, Vector3 deck)
        {
            Transform Find(string n) { foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
            idBkIdle = h.Id("Bike_Idle"); idBkRide = h.Id("Bike_Ride"); idBkPedal = h.Id("Bike_Pedal"); idBkLeanL = h.Id("Bike_LeanL"); idBkLeanR = h.Id("Bike_LeanR");
            idBkTuck = h.Id("Bike_Tuck"); idBkAir = h.Id("Bike_Air"); idBkBump = h.Id("Bike_Bump");
            string[] tn = { "Tabletop", "Whip", "Superman", "NoHander", "Barspin" };
            for (int i = 0; i < tn.Length; i++) idBkTrick[i] = h.Id("Bike_" + tn[i]);
            Transform frame = Find("BikeFrame"), fork = Find("BikeFork"), wf = Find("BikeWheelF"), wr = Find("BikeWheelR"), crank = Find("BikeCrank"), pl = Find("BikePedalL"), pr = Find("BikePedalR");
            if (frame == null || fork == null || wf == null || wr == null || crank == null || pl == null || pr == null || idBkIdle < 0 || idBkRide < 0 || idBkPedal < 0 || idBkLeanL < 0 || idBkAir < 0)
            { Debug.LogWarning("Rides: no mountain bike in Rides.fbx or its clips in HeroAnim.bytes (run Tools/MakeRides.py)"); return; }
            bikeRoot = new GameObject("Bike").transform; bikeRoot.SetParent(roll, false);
            bikeTrick = new GameObject("Frame").transform; bikeTrick.SetParent(bikeRoot, false);
            // each piece placed as the hero's gear is (as the sled and the car), then under the bike's frame
            Transform Place(Transform mesh, string name)
            {
                var holder = new GameObject(name).transform;
                holder.SetPositionAndRotation(origin, square);
                mesh.SetParent(holder, true);
                holder.SetParent(bikeTrick, true);
                holder.localRotation = RideTurn * holder.localRotation;
                holder.localPosition = deck + RideTurn * (holder.localPosition - deck);
                return holder;
            }
            Transform Pivot(Transform holder, Transform mesh, string name, Vector3 at)
            {
                var pv = new GameObject(name).transform;
                pv.SetParent(bikeTrick, false);
                pv.localPosition = at; pv.localRotation = Quaternion.identity;
                holder.SetParent(pv, true);
                return pv;
            }
            Vector3 Mid(Transform mesh) { var r = mesh.GetComponent<Renderer>(); return bikeTrick.InverseTransformPoint(r != null ? r.bounds.center : mesh.position); }
            var hf = Place(frame, "BikeFrame"); var hk = Place(fork, "BikeFork"); var hwf = Place(wf, "BikeWheelF"); var hwr = Place(wr, "BikeWheelR");
            var hc = Place(crank, "BikeCrank"); var hpl = Place(pl, "BikePedalL"); var hpr = Place(pr, "BikePedalR");
            // the tool's frame here: from the front wheel's middle (where the tool drew its axle)
            Vector3 front = Mid(wf), rear = Mid(wr);
            bikeOffset = front - BikeDir(TFrontAxle);
            if ((BikeP(TRearAxle) - rear).sqrMagnitude > 0.03f * 0.03f)
                Debug.LogWarning($"Rides: the bike's wheels are not where the tool drew them (rear {rear}, expected {BikeP(TRearAxle)}): its import has turned or scaled it");
            bikeWheelF = Pivot(hwf, wf, "PivotWheelF", BikeP(TFrontAxle));
            bikeWheelR = Pivot(hwr, wr, "PivotWheelR", BikeP(TRearAxle));
            bikeCrank = Pivot(hc, crank, "PivotCrank", BikeP(TBB));
            bikeSteerAxis = BikeDir(THtTop - THtBot).normalized;
            bikeFork = Pivot(hk, fork, "PivotFork", BikeP(THtBot));
            bikePedal[0] = hpl; bikePedal[1] = hpr;
            bikePedalRest[0] = hpl.localPosition; bikePedalRest[1] = hpr.localPosition;
            bikeForkRest = bikeFork.localRotation; bikeCrankRest = bikeCrank.localRotation; bikeWheelFRest = bikeWheelF.localRotation; bikeWheelRRest = bikeWheelR.localRotation;
            // where the hands hold on: on the fork, so they go where the bar goes
            bikeGripL = new GameObject("GripL").transform; bikeGripL.SetParent(bikeFork, false); bikeGripL.position = bikeTrick.TransformPoint(BikeP(new Vector3(TGripX, TGripY, TGripZ)));
            bikeGripR = new GameObject("GripR").transform; bikeGripR.SetParent(bikeFork, false); bikeGripR.position = bikeTrick.TransformPoint(BikeP(new Vector3(-TGripX, TGripY, TGripZ)));
            bikeBoxAt = BikeP(new Vector3(0f, -0.15f, 0.48f));
            bikeGear = true;
        }

        void DropBike()
        {
            if (bikeRoot != null) DestroyImmediate(bikeRoot.gameObject);
            bikeRoot = bikeTrick = bikeFork = bikeWheelF = bikeWheelR = bikeCrank = bikeGripL = bikeGripR = null;
            bikePedal[0] = bikePedal[1] = null;
            bikeGear = false; bikeBarspin = 0f; bikeCrankNow = 0f; bikeSkidYaw = 0f;
        }

        /// <summary>The bike's clips for PoseVehicle: idle, ride, the leans, the tuck, the air, the trick in hand, the landing.</summary>
        void BikeClips(out int iIdle, out int iRide, out int iL, out int iR, out int iTuck, out int iAir, out int iTrick, out int iBump)
        {
            iIdle = idBkIdle; iRide = idBkRide; iL = idBkLeanL; iR = idBkLeanR; iTuck = idBkTuck >= 0 ? idBkTuck : idBkRide; iAir = idBkAir; iBump = idBkBump;
            int g = rider.TrickGrab - 1;
            iTrick = g >= 0 && g < idBkTrick.Length && idBkTrick[g] >= 0 ? idBkTrick[g] : idBkAir;
        }

        /// <summary>Pedalling (on the snow, the cranks going round): the pedal clip in step with the physics' stroke, over the ride.</summary>
        void BikePedalling(HeroRig h, float w)
        {
            if (idBkPedal < 0 || w <= 0f) return;
            h.SetTime(idBkPedal, rider.PedalT * h.Length(idBkPedal));
            h.Want(idBkPedal, w);
        }

        /// <summary>The bike tipped under the rider in a turn, its back stepped out in a skid (VehicleRoll's).</summary>
        void BikeRoll(float leanDeg, float dt)
        {
            float want = rider.BikeSkid * 24f * Mathf.Sign(rider.Lean == 0f ? 1f : rider.Lean);
            bikeSkidYaw = dt <= 0f ? want : Mathf.Lerp(bikeSkidYaw, want, 1f - Mathf.Exp(-dt * 8f));
            roll.localRotation = Quaternion.Euler(0f, bikeSkidYaw, -leanDeg * 0.8f);
            roll.localPosition = Vector3.zero;
        }

        /// <summary>
        /// Before the hero's layer: the bike thrown about for the trick in hand, and the hands held on its grips wherever
        /// they are (not in the no-hander or the barspin). d's board is the bike's root (not turned with it).
        /// </summary>
        void BikeHold(ref HeroRig.Drive d, bool down, float dt)
        {
            if (bikeTrick == null) return;
            int g = rider.TrickGrab - 1;
            float k = rider.Airborne && g >= 0 ? rider.GrabPose : 0f;
            var q = BikeTrickQ(g, k);
            Vector3 pv = BikeP(TPivot);
            bikeTrick.localRotation = q;
            bikeTrick.localPosition = pv - q * pv;
            // the bar: steered with the lean, spun round in a barspin (on round to a whole turn when it is let go)
            bool spin = g == 4 && k > 0.4f;
            if (spin) bikeBarspin += BarspinRate * dt;
            else if (bikeBarspin > 0f)
            {
                float to = Mathf.Ceil(bikeBarspin / 360f - 1e-3f) * 360f;
                bikeBarspin = Mathf.MoveTowards(bikeBarspin, to, BarspinRate * dt);
                if (bikeBarspin >= to - 1e-3f) bikeBarspin = 0f;
            }
            float steer = down ? 0f : Mathf.Clamp(rider.Lean / Rider.MaxLean, -1f, 1f) * BikeSteerTurn;
            bikeFork.localRotation = Quaternion.AngleAxis(steer + bikeBarspin, bikeSteerAxis) * bikeForkRest;
            // (the shoulders turn with the bar, as a rider's do: the grip going forward is reached for, not out of reach)
            d.twist += -steer * 1.1f;
            // the hands on the grips (the clip's own hands' turn kept), unless the trick lets go
            bool free = down || ((g == 3 || g == 4) && k > 0.3f);
            if (free || hero == null) { d.holdL.ok = d.holdR.ok = false; return; }
            var h = hero;
            Quaternion dR = d.board.rotation * d.rootRotInBoard * Quaternion.Inverse(h.Root.rotation);
            Vector3 rootTarget = d.board.TransformPoint(d.rootInBoard);
            HeroRig.Hold At(Transform grip, Transform hand)
            {
                Vector3 wrist = grip.position + grip.up * 0.065f;   // (a hand's depth above where the fingers close)
                Vector3 p = h.Root.position + Quaternion.Inverse(dR) * (wrist - rootTarget);
                return new HeroRig.Hold { ok = true, pos = h.Root.InverseTransformPoint(p), rot = Quaternion.Inverse(h.Root.rotation) * hand.rotation };
            }
            d.holdL = At(bikeGripL, h.Hand[0]); d.holdR = At(bikeGripR, h.Hand[1]);
            d.handL = d.handR = 1f;
        }

        /// <summary>After the hero's layer: the wheels spun, the cranks set to the feet, the pedals at the arms' ends, level.</summary>
        void BikeFrame(float dt)
        {
            if (bikeTrick == null || hero == null || rider.Crashed) return;
            if (rider.Running) bikeWheelTurn = Mathf.Repeat(bikeWheelTurn + rider.Speed / TWheelR * Mathf.Rad2Deg * dt, 360f);
            bikeWheelF.localRotation = Quaternion.AngleAxis(bikeWheelTurn, Vector3.right) * bikeWheelFRest;
            bikeWheelR.localRotation = Quaternion.AngleAxis(bikeWheelTurn, Vector3.right) * bikeWheelRRest;
            // the crank's angle, off whichever foot is on its pedal (the left arm's way: 0 forward, 90 down)
            float best = 0.05f; float a = bikeCrankNow; bool found = false;
            for (int s = 0; s < 2; s++)
            {
                Vector3 local = bikeTrick.InverseTransformPoint(hero.Foot[s].position) - bikeOffset;
                Vector3 tool = new Vector3(-local.x, -local.z, local.y);                 // (back to the tool's axes)
                int sd = s == 0 ? 1 : -1;
                Vector3 an = tool - new Vector3(0f, TPedBehind, TPedAbove) - TBB - new Vector3(sd * TQHalf, 0f, 0f);
                float off = Mathf.Abs(new Vector2(an.y, an.z).magnitude - TCrank) + Mathf.Abs(an.x) * 0.5f;
                if (off < best)
                {
                    best = off; found = true;
                    a = sd > 0 ? Mathf.Atan2(-an.z, -an.y) * Mathf.Rad2Deg : Mathf.Atan2(an.z, an.y) * Mathf.Rad2Deg;
                }
            }
            if (found) bikeCrankNow = a;
            bikeCrank.localRotation = Quaternion.AngleAxis(bikeCrankNow, Vector3.right) * bikeCrankRest;
            for (int s = 0; s < 2; s++)
            {
                int sd = s == 0 ? 1 : -1;
                bikePedal[s].localPosition = bikePedalRest[s] + (BikeP(PedalAt(sd, bikeCrankNow)) - BikeP(PedalAt(sd, 0f)));
            }
        }

        /// <summary>What flies off in a crash: the whole bike.</summary>
        bool BikeRagdoll()
        {
            if (Ride != RideKind.Bike || !bikeGear) return false;
            ragdoll.SetBoard(bikeTrick, BikeBox, bikeBoxAt);
            ragdoll.SetExtras(null);
            return true;
        }
    }
}
