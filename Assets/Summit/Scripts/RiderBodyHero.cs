using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The hero (Resources/Rider/Hero.fbx, built by Tools/MakeHero.py): set up on the rider, and posed each frame
    /// from the Blender clips plus the procedural layer (see HeroRig). Kept apart from RiderBody's own code:
    /// RiderBody only tries SetUpHero first and hands PoseRig over to PoseHero when it worked.
    /// The old rider is still there behind the switch, for comparison: RiderBody.UseHero = false (or the
    /// PlayerPrefs key "summit_oldhero" = 1) builds the previous model instead.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>Build the new hero (true) or the previous rider (false). Read when the rider is built.</summary>
        public static bool UseHero = true;
        public const string OldHeroPref = "summit_oldhero";

        HeroRig hero;
        /// <summary>The hero's rig, when the hero is the rider (for the test tools).</summary>
        public HeroRig Hero => hero;
        Vector3 heroRidePos;            // the model's place under the body with the Root bone on the deck
        Quaternion heroRootRot;         // the Root bone's turn in the board's frame
        int idIdle, idRide, idToe, idHeel, idTuck, idPop, idAir, idLand, idFlip, idFlail, idSit, idStand, idWalk, idRun;
        int idWindL, idWindR, idSpinL, idSpinR;
        readonly int[] idGrab = new int[6];
        // which hand holds the board in each grab (0 left, 1 right): Indy, Melon, Mute, Method, Stalefish, Tail
        static readonly int[] grabHand = { 1, 0, 0, 0, 1, 1 };
        float popT = 9f, landT = 9f, landK, heroLift;
        readonly HeroRig.Hold[] grabHold = new HeroRig.Hold[6];
        HeroRig.Hold sitL, sitR;
        bool heroWasDown;

        bool SetUpHero()
        {
            if (!UseHero || PlayerPrefs.GetInt(OldHeroPref, 0) == 1) return false;
            var prefab = Resources.Load<GameObject>("Rider/Hero");
            var table = Resources.Load<TextAsset>("Rider/HeroAnim");
            if (prefab == null || table == null) return false;
            var go = Instantiate(prefab, upper, false);
            go.name = "Hero";
            var h = new HeroRig(go.transform, table);
            var r = new RiderRig(go.transform);
            if (!h.Ok || !r.Ok) { Debug.LogWarning($"Hero not usable (clips {h.Ok}, bones {r.Ok}): the previous rider instead"); DestroyImmediate(go); return false; }
            foreach (var a in go.GetComponentsInChildren<Animator>()) a.enabled = false;   // posed by HeroRig
            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            model = go.transform;

            // side-on across the board: the left hip toward the nose (+Z), so the rider faces the toe edge (+X)
            Vector3 left = upper.InverseTransformDirection(h.UpperLeg[0].position - h.UpperLeg[1].position); left.y = 0f;
            if (left.sqrMagnitude > 1e-8f)
                model.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(left, Vector3.forward, Vector3.up), 0f) * model.localRotation;
            // the clips were made with the Root bone on the deck between the bindings: put it there
            model.localPosition += new Vector3(0f, BoardTop, 0f) - upper.InverseTransformPoint(h.Root.position);
            heroRidePos = model.localPosition;
            heroRootRot = Quaternion.Inverse(roll.rotation) * h.Root.rotation;
            h.CaptureRest(upper.TransformDirection(Vector3.right), upper.up);

            // materials: the jacket and trousers can take snow (SnowyClothes), the gear is one palette texture
            // (colour, and smoothness in its alpha: the lens shines, the gloves don't)
            Shader sh = jacketShader != null ? jacketShader : Shader.Find("Universal Render Pipeline/Lit");
            snowy = new SnowyClothes();
            if (!snowy.Ok) snowy = null;
            Material Cloth(Color c, float smooth)
            {
                if (snowy != null) return snowy.Make(c, smooth);
                var m = new Material(sh); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); return m;
            }
            var jacketM = Cloth(new Color(0.93f, 0.31f, 0.14f), 0.35f);
            var pantsM = Cloth(new Color(0.17f, 0.19f, 0.24f), 0.2f);
            var gearM = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? sh);
            var pal = Resources.Load<Texture2D>("Rider/HeroPalette");
            if (pal != null) { gearM.SetTexture("_BaseMap", pal); gearM.mainTexture = pal; }
            gearM.SetColor("_BaseColor", Color.white);
            gearM.SetFloat("_Smoothness", 1f);
            gearM.SetFloat("_SmoothnessTextureChannel", 1f);
            gearM.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    mats[i] = n.StartsWith("Jacket") ? jacketM : n.StartsWith("Pants") ? pantsM : gearM;
                }
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true;
                smr.quality = SkinQuality.Bone4;   // the weights are painted for up to four bones
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // the hero's own board and bindings take the place of the plain one (still the game's board object,
            // painted by the board rack and thrown in a crash)
            Transform deck = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Snowboard") deck = t;
            if (deck != null)
            {
                foreach (Transform c in boardRoot) c.gameObject.SetActive(false);
                deck.SetParent(boardRoot, true);
                var mr = deck.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mats[i] != null && mats[i].name.StartsWith("Board") ? boardPaint : gearM;
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }

            r.CaptureBind();
            snowy?.SetFrame(model, h.Hips);
            snowy?.BakeRigid(go.transform);
            Loosen(go, 0.35f);   // already baggy: only a little looser, but the wind shapes are wanted
            rig = r; hero = h;

            idIdle = h.Id("Ride_Idle"); idRide = h.Id("Ride"); idToe = h.Id("Carve_Toe"); idHeel = h.Id("Carve_Heel"); idTuck = h.Id("Tuck");
            idPop = h.Id("Ollie_Pop"); idAir = h.Id("Air"); idLand = h.Id("Land"); idFlip = h.Id("Flip_Tuck"); idFlail = h.Id("Flail");
            idSit = h.Id("Sit"); idStand = h.Id("Stand"); idWalk = h.Id("Walk"); idRun = h.Id("Run");
            idWindL = h.Id("Spin_WindL"); idWindR = h.Id("Spin_WindR"); idSpinL = h.Id("Spin_AirL"); idSpinR = h.Id("Spin_AirR");
            string[] g = { "Indy", "Melon", "Mute", "Method", "Stalefish", "TailGrab" };
            for (int i = 0; i < g.Length; i++) { idGrab[i] = h.Id("Grab_" + g[i]); grabHold[i] = h.CaptureHold(idGrab[i], grabHand[i]); }
            sitL = h.CaptureHold(idSit, 0); sitR = h.CaptureHold(idSit, 1);
            r.RestoreBind();
            rider.Jumped += () => { popT = 0f; hero.Restart(idPop); };
            rider.Landed += impact => { if (impact > 1.5f) { landT = 0f; landK = Mathf.Clamp01(impact / 7f); hero.Restart(idLand); } };
            return true;
        }

        /// <summary>The board material the hero's deck wears (the game's own, so the board rack paints it).</summary>
        Material boardPaint;

        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        void PoseHero(float dt)
        {
            var h = hero;
            h.Begin();
            float t = Time.time;
            bool down = rider.Crashed;
            if (heroWasDown && !down) h.ResetSprings();   // back up after the ragdoll: no spring kick from the jump
            heroWasDown = down;
            var d = new HeroRig.Drive
            {
                board = roll, rootInBoard = new Vector3(0f, BoardTop, 0f), rootRotInBoard = heroRootRot,
                up = roll.up, toe = roll.right, nose = roll.forward, feet = true,
            };
            model.localPosition = heroRidePos;
            float lift = 0f, blend = 9f;

            if (Tube)
            {
                Flutter(dt);
                h.Want(down ? idFlail : idSit, 1f);
                d.handL = d.handR = down ? 0f : 1f;
                d.holdL = sitL; d.holdR = sitR;
                d.side = Mathf.Clamp(rider.Steer, -1f, 1f) * 14f;   // a weight shift to steer
            }
            else if (Walking)
            {
                // on foot: the Root bone stands on the snow, the clips stride in time with the camp's footsteps
                model.localPosition = heroRidePos - new Vector3(0f, BoardTop, 0f);
                h.Want(idStand, 1f - walkAmp);
                h.Want(idWalk, walkAmp * (1f - runAmp));
                h.Want(idRun, walkAmp * runAmp);
                float ph = walkPhase / (2f * Mathf.PI);   // the left heel strikes at a quarter, as in the clips
                h.At(idWalk, ph); h.At(idRun, ph);
                d.board = null; d.feet = false;
            }
            else
            {
                Flutter(dt);
                // landings squash the legs, which spring back (the same spring as the previous rider's)
                compV += (-comp * 140f - compV * 16f) * dt;
                comp = Mathf.Clamp(comp + compV * dt, -0.15f, 0.6f);
                bool idle = !rider.Running, air = rider.Airborne;
                float toe = Mathf.Clamp(rider.Lean / Rider.MaxLean, -1f, 1f);
                float speed01 = Mathf.Clamp01(rider.Speed / 30f);
                if (down) h.Want(idFlail, 1f);
                else if (idle) h.Want(idIdle, 1f);
                else if (air)
                {
                    blend = 11f;
                    float k = rider.TrickProgress;
                    bool flip = Mathf.Abs(rider.TrickFlip) > 0f;
                    float spin = rider.TrickSpin;
                    float grab = rider.TrickGrab > 0 ? Sm(0.08f, 0.22f, k) * (1f - Sm(0.8f, 0.95f, k)) : 0f;
                    float fl = flip ? Sm(0.04f, 0.18f, k) * (1f - Sm(0.85f, 0.97f, k)) : 0f;
                    float sp = Mathf.Abs(spin) > 0.3f ? 1f : 0f;
                    float wind = sp * (1f - Sm(0f, 0.14f, k));
                    float wrap = sp * Sm(0.08f, 0.2f, k) * (1f - Sm(0.85f, 0.97f, k));
                    float rest = 1f - Mathf.Max(grab, fl);
                    h.Want(idAir, Mathf.Max(0.05f, rest * (1f - Mathf.Max(wind, wrap))));
                    // a spin: wound up against it at take-off, then wrapped tight (TrickSpin < 0 turns to the left)
                    h.Want(spin < 0f ? idWindL : idWindR, wind * rest);
                    h.Want(spin < 0f ? idSpinL : idSpinR, wrap * rest);
                    if (grab > 0f && rider.TrickGrab <= idGrab.Length)
                    {
                        int gi = rider.TrickGrab - 1;
                        h.Want(idGrab[gi], grab * 3f);
                        if (grabHand[gi] == 0) { d.handL = grab; d.holdL = grabHold[gi]; } else { d.handR = grab; d.holdR = grabHold[gi]; }
                    }
                    if (fl > 0f) h.Want(idFlip, fl * 1.5f);
                    lift = 0.11f * Mathf.Max(grab, fl);   // the board comes up to the body
                    if (popT < h.Length(idPop)) h.Want(idPop, 2f * (1f - Sm(0.55f, 1f, popT / h.Length(idPop))));
                }
                else
                {
                    float tuck = rider.Tuck, tw = Mathf.Abs(toe);
                    h.Want(idRide, (1f - tw) * (1f - tuck));
                    h.Want(toe > 0f ? idToe : idHeel, tw * (1f - tuck));
                    h.Want(idTuck, tuck);
                    if (landT < h.Length(idLand)) h.Want(idLand, landK * 2f * (1f - Sm(0.3f, 1f, landT / h.Length(idLand))));
                    if (rider.Grinding) h.Want(idAir, 0.6f);   // arms out for balance on a rail
                    // the back leans into the turn with the sideways pull (scaled down: it reads, it doesn't topple),
                    // the shoulders lead the board round, and the eyes go where the turn is taking you
                    float lateral = rider.Speed * rider.YawRate;
                    d.lean = Mathf.Clamp(Mathf.Atan2(lateral, 9.81f) * Mathf.Rad2Deg * 0.45f, -15f, 15f);
                    d.twist = Mathf.Clamp(-rider.YawRate * Mathf.Rad2Deg * 0.12f, -14f, 14f);
                    Vector3 v = rider.Velocity; v.y = 0f;
                    if (v.sqrMagnitude > 9f)
                    {
                        d.look = Quaternion.AngleAxis(rider.YawRate * Mathf.Rad2Deg * 0.35f, Vector3.up) * v + Vector3.down * v.magnitude * 0.15f;
                        d.lookWeight = 0.5f;
                    }
                    float chatter = (Mathf.PerlinNoise(t * 9f, 1.3f) - 0.5f) * 0.02f * speed01;
                    d.drop = chatter;
                }
                d.drop += Mathf.Max(0f, comp) * 0.18f;
                d.feet = !down;
                popT += dt; landT += dt;
            }
            // the board rises to meet the knees in a grab or a flip (and is home again otherwise)
            if (!Walking && !Tube && !down)
            {
                heroLift = dt <= 0f ? lift : Mathf.Lerp(heroLift, lift, 1f - Mathf.Exp(-dt * 12f));
                boardRoot.localPosition = Vector3.up * heroLift;
                d.board = boardRoot;
            }
            if (dt <= 0f) h.Snap();
            h.Evaluate(dt, blend);
            h.Layer(d, dt);
            if (Walking)
            {
                // the board hangs from the right hand, swinging with it
                boardRoot.position = h.Hand[1].position + roll.rotation * new Vector3(0.075f, 0f, -0.03f);
            }
        }
    }
}
