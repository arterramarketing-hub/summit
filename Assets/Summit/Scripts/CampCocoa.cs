using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Hot chocolate in the lodge: a corner of the kitchen's back counter with a little camp stove and a pot of milk
    /// on it, a tin of cocoa, a jar of mini marshmallows and mugs. MAKE HOT CHOCOLATE there: the pot is poured into a
    /// mug, it's stirred, it steams, and (if you've had a s'more at the fire) three marshmallows go on top; then you
    /// pick it up and carry it about the camp, steam rising off it.
    /// Each tap is a sip: the mug comes up to your mouth and down again, and the level inside drops; after eight it's
    /// finished and the mug is gone. With a mug in your hand a tap sips (never jumps), a swipe doesn't roll, and you
    /// walk (never run), so nothing spills. Dropping in at the gate with it, you leave it on the gate's post.
    /// </summary>
    public partial class Camp
    {
        public const int Sips = 8;
        Transform cocoaCorner, mug, mugFill, mugTops, mugSteam, workMug, pot, stream, spoon;
        Vector3 cocoaStand, cocoaFace;
        float cocoaT, sipT = -1f, finishT = -1f, level;
        int sipsLeft;
        Material mugMat, cocoaMat, mallowMat, steelMat, steamMat;

        // the making, in seconds: step up, pour, stir, marshmallows on, pick it up
        const float PourAt = 0.6f, StirAt = 2.4f, TopAt = 4.4f, LiftAt = 5.4f, MadeAt = 6.2f;

        public bool HoldingMug => mug != null && act != Act.Cocoa && !mugParked;
        bool mugParked;
        /// <summary>The mug set down on the snow (test tools).</summary>
        public bool MugParked => mug != null && mugParked;

        /// <summary>Set the mug down, standing, at a spot on the ground (roasting needs both hands).</summary>
        void ParkMug(Vector3 at)
        {
            if (mug == null) return;
            mugParked = true; sipT = -1f;
            mug.SetParent(root, true);
            mug.SetPositionAndRotation(new Vector3(at.x, Slope.DrawnHeight(at.x, at.z), at.z), Quaternion.Euler(0f, 20f, 0f));
        }

        /// <summary>And pick it up again.</summary>
        void UnparkMug()
        {
            if (mug == null || !mugParked) return;
            mugParked = false;
            mug.SetParent(rider.transform, true);
        }
        bool CocoaMaking => act == Act.Cocoa;
        /// <summary>Sips left in the mug in your hand (test tools).</summary>
        public int SipsLeft => mug != null ? sipsLeft : 0;
        public bool MugOnPost { get; private set; }
        public float MugLevel => level;

        /// <summary>
        /// Where the station goes: one place and one facing (where you stand to make it, and the way you face the
        /// counter). In the chalet it stands against the back wall to the right of the kitchen counter, clear of the
        /// island, the missions board and the plant; a new building only has to change this.
        /// </summary>
        void CocoaCorner()
        {
            if (chalet == null) return;
            CocoaStation(chalet.TransformPoint(new Vector3(4.6f, 0f, -4.4f)), chalet.TransformDirection(Vector3.back));
        }

        // the station's own frame: you stand at its origin on the floor facing +Z; its sideboard is in front of you
        const float Top = 0.94f;                                   // the sideboard's worktop
        static readonly Vector3 PotHome = new Vector3(0f, Top + 0.085f, 1.08f), WorkMugAt = new Vector3(0f, Top, 0.84f);

        /// <summary>
        /// The whole hot-chocolate station, self-contained: a small oak sideboard with a slate top, a black camp stove
        /// with a pot of milk steaming on it, a tin of cocoa, a jar of mini marshmallows and two mugs waiting, and the
        /// MAKE HOT CHOCOLATE spot in front of it. `stand`: where you stand to make it, on the floor; `facing`: toward the sideboard.
        /// </summary>
        void CocoaStation(Vector3 stand, Vector3 facing)
        {
            mugMat = Marshmallow.Plain(new Color(0.93f, 0.90f, 0.82f), 0.55f);       // cream glazed stoneware
            cocoaMat = Marshmallow.Plain(new Color(0.30f, 0.16f, 0.09f), 0.6f);
            mallowMat = Marshmallow.Plain(new Color(0.98f, 0.97f, 0.94f), 0.2f);
            steelMat = Marshmallow.Plain(new Color(0.55f, 0.57f, 0.6f), 0.7f);
            var black = Marshmallow.Plain(new Color(0.08f, 0.08f, 0.09f), 0.35f);
            var tin = Marshmallow.Plain(new Color(0.55f, 0.12f, 0.1f), 0.5f);
            var glass = Marshmallow.Plain(new Color(0.8f, 0.9f, 0.95f), 0.9f);
            var oak = Marshmallow.Plain(new Color(0.58f, 0.40f, 0.24f), 0.25f);
            var slate = Marshmallow.Plain(new Color(0.22f, 0.23f, 0.25f), 0.4f);
            facing.y = 0f; facing.Normalize();
            cocoaStand = stand; cocoaFace = facing;
            cocoaCorner = new GameObject("CocoaStation").transform;
            cocoaCorner.SetParent(root, false);
            cocoaCorner.SetPositionAndRotation(stand, Quaternion.LookRotation(facing, Vector3.up));
            Transform C(PrimitiveType p, Vector3 at, Vector3 sz, Material m) => Marshmallow.Prim(p, cocoaCorner, at, sz, m);
            // the sideboard: oak, a slate top, two doors' worth of shadow line down its front
            var body = C(PrimitiveType.Cube, new Vector3(0f, 0.45f, 1.02f), new Vector3(1.4f, 0.9f, 0.55f), oak);
            body.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            C(PrimitiveType.Cube, new Vector3(0f, Top - 0.02f, 1.02f), new Vector3(1.46f, 0.04f, 0.6f), slate);
            C(PrimitiveType.Cube, new Vector3(0f, 0.45f, 0.744f), new Vector3(0.012f, 0.8f, 0.004f), black);
            Block(cocoaCorner, new Vector3(0f, 0f, 1.02f), new Vector2(0.73f, 0.3f), null, false, 0f, 0f, -0.5f, 1f);   // (at its own height: not the mezzanine above)
            // the stove: a black camp stove with a ring glowing under the pot
            C(PrimitiveType.Cube, PotHome + new Vector3(0f, -0.045f, 0f), new Vector3(0.36f, 0.08f, 0.3f), black);
            C(PrimitiveType.Cylinder, PotHome + new Vector3(0f, -0.003f, 0f), new Vector3(0.16f, 0.004f, 0.16f), ember);
            pot = new GameObject("Pot").transform; pot.SetParent(cocoaCorner, false); pot.localPosition = PotHome;
            Marshmallow.Prim(PrimitiveType.Cylinder, pot, new Vector3(0f, 0.065f, 0f), new Vector3(0.19f, 0.065f, 0.19f), steelMat);
            Marshmallow.Prim(PrimitiveType.Cylinder, pot, new Vector3(0f, 0.128f, 0f), new Vector3(0.17f, 0.003f, 0.17f), cocoaMat);
            Marshmallow.Prim(PrimitiveType.Cube, pot, new Vector3(0.19f, 0.11f, 0f), new Vector3(0.2f, 0.018f, 0.03f), black);   // its handle, to your right
            // the cocoa tin and the marshmallow jar on the left, two mugs waiting on the right
            C(PrimitiveType.Cylinder, new Vector3(-0.42f, Top + 0.07f, 1.12f), new Vector3(0.11f, 0.07f, 0.11f), tin);
            C(PrimitiveType.Cylinder, new Vector3(-0.42f, Top + 0.145f, 1.12f), new Vector3(0.115f, 0.006f, 0.115f), steelMat);
            C(PrimitiveType.Cylinder, new Vector3(-0.6f, Top + 0.09f, 0.95f), new Vector3(0.12f, 0.09f, 0.12f), glass);
            for (int i = 0; i < 6; i++) C(PrimitiveType.Cube, new Vector3(-0.6f + (i % 3 - 1) * 0.03f, Top + 0.03f + (i / 3) * 0.035f, 0.95f + ((i * 7) % 3 - 1) * 0.025f), Vector3.one * 0.026f, mallowMat);
            Mug(cocoaCorner, new Vector3(0.48f, Top, 1.1f), false);
            Mug(cocoaCorner, new Vector3(0.58f, Top, 0.9f), false);
            // steam off the pot, gently, all the time
            Steam(pot, new Vector3(0f, 0.14f, 0f), 3f, 0.6f);
            var tap = new GameObject("TAP_Cocoa").transform;
            tap.SetParent(cocoaCorner, false); tap.localPosition = new Vector3(0f, Top + 0.1f, 1.0f);
            var bc = tap.gameObject.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(1.4f, 0.5f, 0.6f);
            spots.Add(new Spot { st = Station.Cocoa, at = stand, radius = 1.1f, root = tap });
        }

        /// <summary>A mug at `at` (its foot) under `parent`, with its handle; optionally its cocoa and the marshmallows on it.</summary>
        Transform Mug(Transform parent, Vector3 at, bool full)
        {
            var m = new GameObject("Mug").transform; m.SetParent(parent, false); m.localPosition = at;
            // (a little larger than life, so it reads in the hand at the camp camera's distance)
            // hollow: a base and a wall of staves round it, so the cocoa inside shows and its level can go down
            Marshmallow.Prim(PrimitiveType.Cylinder, m, new Vector3(0f, 0.004f, 0f), new Vector3(0.12f, 0.004f, 0.12f), mugMat);
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                var w = Marshmallow.Prim(PrimitiveType.Cube, m, new Vector3(Mathf.Cos(a) * 0.055f, 0.06f, Mathf.Sin(a) * 0.055f), new Vector3(0.011f, 0.12f, 0.024f), mugMat);
                w.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }
            var hd = Marshmallow.Prim(PrimitiveType.Cube, m, new Vector3(0.075f, 0.06f, 0f), new Vector3(0.025f, 0.07f, 0.018f), mugMat);
            Marshmallow.Prim(PrimitiveType.Cube, hd, new Vector3(-0.6f, 0.45f, 0f), new Vector3(1.6f, 0.25f, 1f), mugMat);
            Marshmallow.Prim(PrimitiveType.Cube, hd, new Vector3(-0.6f, -0.45f, 0f), new Vector3(1.6f, 0.25f, 1f), mugMat);
            if (full)
            {
                mugFill = Marshmallow.Prim(PrimitiveType.Cylinder, m, new Vector3(0f, 0.1f, 0f), new Vector3(0.098f, 0.003f, 0.098f), cocoaMat);
                mugTops = new GameObject("Marshmallows").transform; mugTops.SetParent(mugFill, true);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f;
                    var b = Marshmallow.Prim(PrimitiveType.Cube, m, new Vector3(Mathf.Cos(a) * 0.022f, 0.108f, Mathf.Sin(a) * 0.022f), Vector3.one * 0.022f, mallowMat);
                    b.localRotation = Quaternion.Euler(0f, i * 40f, 8f);
                    b.SetParent(mugTops, true);
                }
                mugTops.gameObject.SetActive(false);
            }
            return m;
        }

        Transform Steam(Transform parent, Vector3 at, float rate, float size)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(parent, false); go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f * size, 0.1f * size);
            main.startColor = new Color(1f, 1f, 1f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // it trails behind as you walk
            main.maxParticles = 30;
            main.gravityModifier = -0.04f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.03f;
            var em = ps.emission; em.rateOverTime = rate;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.35f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
            // one steam material for all of them (not a new one per pot and mug each time the camp is built)
            if (steamMat == null && soft != null) { steamMat = new Material(soft) { name = "Steam" }; steamMat.SetColor("_BaseColor", Color.white); }
            if (steamMat != null) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = steamMat;
            ps.Play();
            return go.transform;
        }

        // ---------------------------------------------------------------- making it

        void StartCocoa()
        {
            if (cocoaCorner == null) return;
            if (mug != null) { Destroy(mug.gameObject); mug = null; }   // (a fresh one: the old mug goes)
            mugParked = false;
            ClearMove();
            act = Act.Cocoa; cocoaT = 0f;
            pos = cocoaStand; pos.y = FloorAt(pos, pos.y);
            yaw = Mathf.Atan2(cocoaFace.x, cocoaFace.z);
            camYaw = yaw + 0.5f;
            speed = 0f;
            // the mug it goes into, on the counter in front of you
            workMug = Mug(cocoaCorner, WorkMugAt, true);
            level = 0f; mugFill.localPosition = new Vector3(0f, 0.012f, 0f);
            stream = Marshmallow.Prim(PrimitiveType.Cylinder, cocoaCorner, Vector3.zero, new Vector3(0.012f, 0.1f, 0.012f), cocoaMat);
            stream.gameObject.SetActive(false);
            spoon = Marshmallow.Prim(PrimitiveType.Cylinder, cocoaCorner, Vector3.zero, new Vector3(0.012f, 0.09f, 0.012f), steelMat);
            spoon.gameObject.SetActive(false);
        }

        void CocoaStep(float dt)
        {
            cocoaT += dt;
            float t = cocoaT;
            // pour: the pot comes up off the stove and tips toward the mug, a stream of cocoa, the mug filling
            float pour = Mathf.Clamp01((t - PourAt) / 0.4f) * (1f - Mathf.Clamp01((t - (StirAt - 0.3f)) / 0.3f));
            pot.localPosition = PotHome + new Vector3(0f, 0.16f, -0.08f) * pour;   // up off the stove and over the mug
            pot.localRotation = Quaternion.Euler(-48f * pour, 0f, 0f);   // tipped toward the mug (toward you)
            bool pouring = pour > 0.8f;
            stream.gameObject.SetActive(pouring);
            if (pouring)
            {
                Vector3 lip = pot.TransformPoint(new Vector3(0f, 0.13f, -0.1f)), into = workMug.TransformPoint(new Vector3(0f, 0.11f, 0f));
                stream.position = (lip + into) * 0.5f;
                stream.up = (lip - into).normalized;
                stream.localScale = new Vector3(0.012f, (lip - into).magnitude * 0.5f, 0.012f);
            }
            level = Mathf.Clamp01((t - PourAt - 0.4f) / (StirAt - PourAt - 0.8f));
            mugFill.localPosition = new Vector3(0f, Mathf.Lerp(0.012f, 0.105f, level), 0f);
            // stir: the spoon round and round in it
            bool stir = t >= StirAt && t < TopAt;
            spoon.gameObject.SetActive(stir);
            if (stir)
            {
                float a = (t - StirAt) * 9f;
                spoon.position = workMug.TransformPoint(new Vector3(Mathf.Cos(a) * 0.02f, 0.15f, Mathf.Sin(a) * 0.02f));
                spoon.rotation = Quaternion.Euler(12f * Mathf.Cos(a), 0f, 12f * Mathf.Sin(a));
            }
            if (t >= StirAt && mugSteam == null) mugSteam = Steam(workMug, new Vector3(0f, 0.12f, 0f), 6f, 0.7f);
            // a s'more at the fire earns marshmallows on top
            if (t >= TopAt + 0.3f && HadSmore && !mugTops.gameObject.activeSelf) mugTops.gameObject.SetActive(true);
            // pick it up: from here it's in your hand
            if (t >= MadeAt)
            {
                mug = workMug; workMug = null;
                mug.SetParent(rider.transform, true);
                Destroy(stream.gameObject); Destroy(spoon.gameObject);
                pot.localPosition = PotHome; pot.localRotation = Quaternion.identity;
                sipsLeft = Sips; sipT = -1f; finishT = -1f;
                act = Act.None;
            }
        }

        // ---------------------------------------------------------------- carrying it, sipping it

        /// <summary>A tap with a mug in hand: a sip (true: the tap was taken, no jump). Not while one's already going.</summary>
        public bool Sip()
        {
            if (!HoldingMug) return false;
            if (sipT < 0f && finishT < 0f && sipsLeft > 0) sipT = 0f;
            return true;
        }

        const float SipLen = 1.05f;

        void MugStep(float dt)
        {
            if (mug == null || act == Act.Cocoa) return;
            if (sipT >= 0f)
            {
                float before = sipT;
                sipT += dt;
                // the sip itself at the top of the lift: the level drops by one share
                if (before < SipLen * 0.55f && sipT >= SipLen * 0.55f) { sipsLeft--; level = sipsLeft / (float)Sips; }
                if (sipT >= SipLen) { sipT = -1f; if (sipsLeft <= 0) finishT = 0f; }
            }
            if (finishT >= 0f)
            {
                // finished: lowered, and gone
                finishT += dt;
                mug.localScale = Vector3.one * Mathf.Clamp01(1f - (finishT - 0.3f) / 0.35f);
                if (finishT > 0.7f) { Destroy(mug.gameObject); mug = null; mugSteam = null; finishT = -1f; return; }
            }
            if (mugFill != null) mugFill.localPosition = new Vector3(0f, Mathf.Lerp(0.012f, 0.105f, level), 0f);
            if (mugFill != null) mugFill.gameObject.SetActive(level > 0.01f);
            if (mugTops != null && level < 0.3f) mugTops.gameObject.SetActive(false);   // (the marshmallows go with the first sips)
            if (mugSteam != null) { var em = mugSteam.GetComponent<ParticleSystem>().emission; em.rateOverTime = 6f * Mathf.Clamp01(level + 0.2f); }
        }

        /// <summary>Dropping in with a mug: it's left standing on the gate's left post, still steaming.</summary>
        void LeaveMug()
        {
            if (mug == null) return;
            float cx = Slope.Centre(Slope.PadLip), x = cx - StartGate.PostX, z = Slope.GateZ;
            mug.SetParent(root, true);
            mug.position = new Vector3(x, Slope.DrawnHeight(cx, z) + 0.735f, z - 0.05f);
            mug.rotation = Quaternion.Euler(0f, 30f, 0f);
            mug.localScale = Vector3.one;
            mug = null; mugSteam = null; sipT = -1f; finishT = -1f;
            MugOnPost = true;
        }

        void EndCocoa()
        {
            if (workMug != null) { Destroy(workMug.gameObject); workMug = null; }
            if (stream != null) Destroy(stream.gameObject);
            if (spoon != null) Destroy(spoon.gameObject);
            if (pot != null) { pot.localPosition = PotHome; pot.localRotation = Quaternion.identity; }
            if (mug != null) { Destroy(mug.gameObject); mug = null; }
            mugSteam = null; sipT = -1f; finishT = -1f; mugParked = false;
        }

        /// <summary>The hands at the stove, or the mug in the right hand (lifted to the mouth for a sip).</summary>
        void PoseCocoa(HeroRig h, Transform model, float dt)
        {
            Vector3 face = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)), right = Vector3.Cross(Vector3.up, face);
            Vector3 chest = h.Chest.position, mouth = CampPoses.Mouth(h, face, 1f);
            Vector3 elbowR = h.UpperArm[1].position + right * 0.35f - Vector3.up * 0.35f - face * 0.1f;
            if (act == Act.Cocoa)
            {
                float t = cocoaT;
                // right hand: on the pot's handle while pouring, on the spoon while stirring, then round the mug
                Vector3 handle = pot.TransformPoint(new Vector3(0.24f, 0.11f, 0f));
                Vector3 stir = spoon != null && spoon.gameObject.activeSelf ? spoon.position + Vector3.up * 0.09f : handle;
                Vector3 r = t < StirAt ? handle : t < TopAt ? stir : workMug != null ? workMug.TransformPoint(new Vector3(0.1f, 0.07f, 0f)) : handle;
                float reach = Mathf.Clamp01((t - 0.2f) / 0.4f);
                Vector3 rest = chest + face * 0.2f + right * 0.15f - Vector3.up * 0.35f;
                h.ArmTo(1, Vector3.Lerp(rest, r, reach), elbowR);
                // left hand steadies the mug (or, for the marshmallows, drops them in from the jar's side)
                Vector3 l = workMug != null ? workMug.TransformPoint(new Vector3(-0.09f, 0.06f, 0f)) : rest - right * 0.3f;
                h.ArmTo(0, Vector3.Lerp(rest - right * 0.3f, l, reach), h.UpperArm[0].position - right * 0.35f - Vector3.up * 0.35f);
                return;
            }
            if (mug == null) return;
            // carrying: the mug held in front at chest height, the elbow bent; a sip brings it up and tips it
            float s = sipT >= 0f ? Mathf.Sin(Mathf.Clamp01(sipT / SipLen) * Mathf.PI) : 0f;
            if (finishT >= 0f) s = 0f;
            Vector3 hold = chest + face * 0.26f + right * 0.24f - Vector3.up * 0.2f;   // (out to the side a little: seen past you from behind)
            Vector3 lift = mouth + face * 0.08f + right * 0.03f - Vector3.up * 0.05f;
            h.ArmTo(1, Vector3.Lerp(hold, lift, s), elbowR);
            Vector3 hand = h.Hand[1].position;
            mug.position = hand + face * 0.05f - right * 0.06f - Vector3.up * 0.07f;
            // upright, handle toward the hand; tipped toward the mouth at the top of a sip
            mug.rotation = Quaternion.AngleAxis(-58f * s, right) * Quaternion.LookRotation(face, Vector3.up);   // (its handle, +X, to the right)
        }
    }
}
