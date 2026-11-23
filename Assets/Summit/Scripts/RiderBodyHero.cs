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

        /// <summary>Test tools: build this rider whatever is saved (-1: the saved one). 0 the summit rider, 1 the buddy.</summary>
        public static int TestCharacter = -1;
        int heroChar = -1, heroLook = -1; bool heroHooked;
        int wantLook = -1;              // the look SetCharacter asked for (a closet try-on isn't what's saved)
        int heroOutfit;                 // the set of clothes on: 0 the summit kit, 1 Val's pyjamas, 2 Breanna's
        Material fleeceMain;            // the pyjama fleece the JACKETS rows dye (Val's blue half, all of Breanna's)
        /// <summary>How many times the hero has been built: it goes up when a rider or outfit swap makes a new body.</summary>
        public int Built { get; private set; }
        // Val's face moves: a blink and a lift of the brows, shapes that come with his model (none on the summit rider)
        SkinnedMeshRenderer faceSmr; int blinkShape = -1, browShape = -1; float nextBlink, blinkT = 1f, nextBrows, browsT = 9f;
        // In the camp the people from photos show their faces: goggles pushed up onto the beanie, the balaclava pulled
        // down round the neck (shapes on their models). On a run they come back down and up again, eased over a moment.
        readonly System.Collections.Generic.List<(SkinnedMeshRenderer smr, int shape)> campShapes = new System.Collections.Generic.List<(SkinnedMeshRenderer, int)>();
        float campLook = 1f;
        int idStandVal = -1;
        HeroRig hero;
        /// <summary>The hero's rig, when the hero is the rider (for the test tools).</summary>
        public HeroRig Hero => hero;
        /// <summary>The bone the chest flashlight is strapped to (it tumbles with the body after a fall).</summary>
        public Transform ChestBone => hero != null ? (hero.UpperChest != null ? hero.UpperChest : hero.Chest) : rig != null ? rig.Chest : null;
        Vector3 heroRidePos;            // the model's place under the body with the Root bone on the deck
        Quaternion heroRootRot;         // the Root bone's turn in the board's frame
        int idIdle, idRide, idToe, idHeel, idTuck, idPop, idAir, idLand, idFlip, idFlail, idSit, idStand, idWalk, idRun;
        int idWindL, idWindR, idSpinL, idSpinR, idCampJump, idCampRoll;
        readonly int[] idGrab = new int[6];
        // which hand holds the board in each grab (0 left, 1 right): Indy, Melon, Mute, Method, Stalefish, Tail
        static readonly int[] grabHand = { 1, 0, 0, 0, 1, 1 };
        float popT = 9f, landT = 9f, landK, heroLift;
        readonly HeroRig.Hold[] grabHold = new HeroRig.Hold[6];
        HeroRig.Hold sitL, sitR;
        bool heroWasDown;

        /// <summary>
        /// Put another rider's clothes on this one. Every rider's model is the same skeleton with the person (head,
        /// hands) and the outfit (jacket, trousers, boots and the rest) as separate meshes, so the outfit meshes of
        /// the other model are moved over and told to follow this skeleton's bones, found by name.
        /// </summary>
        static void Dress(GameObject go, string wear)
        {
            var from = Resources.Load<GameObject>(wear);
            if (from == null) return;
            bool Outfit(string n) => n.StartsWith("HeroBody") || n.StartsWith("HeroLegs") || n.StartsWith("HeroGear");
            var bones = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
            Transform under = go.transform;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (Outfit(smr.name)) { under = smr.transform.parent; DestroyImmediate(smr.gameObject); }
            var donor = Instantiate(from);
            foreach (var smr in donor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!Outfit(smr.name)) continue;
                var bs = smr.bones;
                for (int i = 0; i < bs.Length; i++) bs[i] = bs[i] != null && bones.TryGetValue(bs[i].name, out var b) ? b : go.transform;
                smr.bones = bs;
                if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var rb)) smr.rootBone = rb;
                smr.transform.SetParent(under, false);
            }
            DestroyImmediate(donor);
        }

        /// <summary>
        /// A pose the camp makes on top of the hero's own (sitting at the fire with a roasting stick, at the stove,
        /// holding a mug: Camp, CampPoses). Want: true to take the clips over (the standing idle, no walk); After: runs
        /// once the frame's pose is done, to move the model and place the limbs.
        /// </summary>
        public interface ICampPose { bool Want(HeroRig h); void After(HeroRig h, Transform model, float dt); }
        public ICampPose CampPose;

        bool SetUpHero()
        {
            if (!UseHero || PlayerPrefs.GetInt(OldHeroPref, 0) == 1) return false;
            // who you ride as: both are built on one skeleton, so they share every clip
            // (the look: who + 10 x what they wear + 100 x how the summit kit is cut, see Progress.RiderLook)
            int look = TestCharacter >= 0 ? TestCharacter : wantLook >= 0 ? wantLook : Progress.RiderLook;
            int who = look % 10, outfit = (look / 10) % 10, cut = (look / 100) % 10;
            bool dyed = look >= 1000;   // pyjama trousers in a wardrobe colour: plain snowy cloth in place of their print
            string[] people = { "Rider/Hero", "Rider/Buddy", "Rider/Breanna", "Rider/Paul" };
            if (who >= people.Length) who = 0;
            var prefab = Resources.Load<GameObject>(people[who]);
            if (prefab == null) { who = 0; prefab = Resources.Load<GameObject>("Rider/Hero"); }
            var table = Resources.Load<TextAsset>("Rider/HeroAnim");
            if (prefab == null || table == null) return false;
            heroChar = who; heroLook = look; heroOutfit = outfit; Built++;
            var go = Instantiate(prefab, upper, false);
            go.name = "Hero";
            // what they wear: the summit kit (cut for a man or a woman), or one of the others' sets (Wardrobe.OutfitModels).
            // Another rider's clothes are hung on this one's bones (Dress). The summit kit has gloves, so the bare hands
            // stay unseen.
            if (outfit >= Wardrobe.OutfitModels.Length) outfit = 0;
            string wear = outfit == 0 ? (cut == 1 ? "Rider/HeroF" : "Rider/Hero") : "Rider/" + Wardrobe.OutfitModels[outfit];
            if (wear != people[who]) Dress(go, wear);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name.StartsWith("HeroHands")) smr.gameObject.SetActive(outfit != 0);
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
            Material Cloth(Color c, float smooth, float holds)
            {
                if (snowy != null) return snowy.Make(c, smooth, holds);
                var m = new Material(sh); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); return m;
            }
            var jacketM = Cloth(new Color(0.93f, 0.31f, 0.14f), 0.35f, 0.85f);   // (it was 1.5 while a bug kept the jacket clean: see RiderBodyWalk.OwnMaterials)
            var pantsM = Cloth(new Color(0.17f, 0.19f, 0.24f), 0.2f, 0.5f);    // (white on the dark trousers shows far more)
            pantsMat = pantsM;
            var gearM = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? sh);
            var pal = Resources.Load<Texture2D>("Rider/HeroPalette");
            if (pal != null)
            {
                // this rider's own copy of the palette: the closet repaints the helmet, goggles, gloves, boots and pack in it
                var mine = OwnPalette(pal);
                gearM.SetTexture("_BaseMap", mine != null ? mine : pal); gearM.mainTexture = mine != null ? mine : pal;
            }
            gearM.SetColor("_BaseColor", Color.white);
            gearM.SetFloat("_Smoothness", 1f);
            gearM.SetFloat("_SmoothnessTextureChannel", 1f);
            gearM.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            Material fleeceBlue = null, fleeceWhite = null, printM = null, faceM = null, knitM = null, hoodieM = null;
            faceSmr = null; blinkShape = browShape = -1; campShapes.Clear();
            Material Face(Shader lit)
            {
                // Val's painted face (cheeks, the beard's shadow, lids): plain lit skin with its picture
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? lit);
                var tex = Resources.Load<Texture2D>(who == 2 ? "Rider/BreannaFace" : who == 3 ? "Rider/PaulFace" : "Rider/BuddyFace");
                if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.3f);
                FaceGlow(m, tex);
                return m;
            }
            Material Fleece(Color c, float holds)
            {
                var m = Cloth(c, 0.12f, holds); m.SetFloat("_Fuzz", 1f);   // a soft pile on the fleece (SnowyCloth)
                return m;
            }
            Material Print(Shader lit)
            {
                // the pyjama print: a plain lit cloth with its picture (it takes no snow: the snowy cloth has no picture)
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? lit);
                var tex = Resources.Load<Texture2D>(outfit == 2 ? "Rider/BreannaPants" : "Rider/BuddyPants");
                if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.12f);
                return m;
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    // (the buddy's fleece and printed bottoms; the summit rider's jacket and trousers; the rest is gear)
                    mats[i] = n.StartsWith("FleeceBlue") ? (fleeceBlue ??= Fleece(new Color(0.08f, 0.24f, 0.72f), 1.3f))
                            : n.StartsWith("FleeceWhite") ? (fleeceWhite ??= Fleece(new Color(0.93f, 0.90f, 0.82f), 1.5f))
                            : n.StartsWith("PantsPrint") ? (dyed ? pantsM : (printM ??= Print(sh)))
                            : n.StartsWith("FaceSkin") ? (faceM ??= Face(sh))
                            // (Paul's white turtleneck and his grey hoodie: snowy cloth in their own colours)
                            : n.StartsWith("Knit") ? (knitM ??= Cloth(new Color(0.92f, 0.91f, 0.88f), 0.05f, 1.2f))
                            : n.StartsWith("Hoodie") ? (hoodieM ??= Cloth(new Color(0.50f, 0.51f, 0.53f), 0.05f, 1.3f))
                            : n.StartsWith("Jacket") ? jacketM : n.StartsWith("Pants") ? pantsM : gearM;
                }
                smr.sharedMaterials = mats;
                foreach (var nm in new[] { "GogglesUp", "MaskDown" })
                {
                    int cs = smr.sharedMesh != null ? smr.sharedMesh.GetBlendShapeIndex(nm) : -1;
                    if (cs < 0) continue;
                    // (Paul's balaclava is cut for his face, which Breanna's fits inside; on Val's bigger head or over the
                    // summit rider's own neck warmer it would clip, so on them it stays down round the neck)
                    if (nm == "MaskDown" && who <= 1) { smr.SetBlendShapeWeight(cs, 100f); continue; }
                    campShapes.Add((smr, cs));
                }
                FitShapes(smr, who, outfit);
                int bs = smr.sharedMesh != null ? smr.sharedMesh.GetBlendShapeIndex("Blink") : -1;
                if (bs >= 0) { faceSmr = smr; blinkShape = bs; browShape = smr.sharedMesh.GetBlendShapeIndex("Brows"); }
                smr.updateWhenOffscreen = true;
                smr.quality = SkinQuality.Bone4;   // the weights are painted for up to four bones
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            fleeceMain = outfit == 1 ? fleeceBlue : outfit == 2 || outfit == 5 ? fleeceWhite : null;
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
                    BoardArt.Map(mr, BoardArt.Kind.Deck);   // (the graphics' uvs on its top sheet and base: by the file's own material names)
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mats[i] != null && mats[i].name.StartsWith("Board") ? boardPaint : gearM;
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }

            CampLook(0f, true);   // (built in the camp: faces shown at once; built for a run: goggles already down)
            r.CaptureBind();
            snowy?.SetFrame(model, h.Hips);
            snowy?.BakeRigid(go.transform);
            Loosen(go, h.Hips, 0.35f);   // already baggy: only a little looser, but the wind shapes are wanted
            SetUpRides(h, gearM);   // the skis and poles and the toboggan, on the bones at rest (RiderBodyRide)
            rig = r; hero = h;

            idIdle = h.Id("Ride_Idle"); idRide = h.Id("Ride"); idToe = h.Id("Carve_Toe"); idHeel = h.Id("Carve_Heel"); idTuck = h.Id("Tuck");
            idPop = h.Id("Ollie_Pop"); idAir = h.Id("Air"); idLand = h.Id("Land"); idFlip = h.Id("Flip_Tuck"); idFlail = h.Id("Flail");
            idSit = h.Id("Sit"); idStand = h.Id("Stand"); idStandVal = h.Id("Stand_Val"); idWalk = h.Id("Walk"); idRun = h.Id("Run");
            idCampJump = h.Id("CampJump"); idCampRoll = h.Id("CampRoll");
            idWindL = h.Id("Spin_WindL"); idWindR = h.Id("Spin_WindR"); idSpinL = h.Id("Spin_AirL"); idSpinR = h.Id("Spin_AirR");
            string[] g = { "Indy", "Melon", "Mute", "Method", "Stalefish", "TailGrab" };
            for (int i = 0; i < g.Length; i++) { idGrab[i] = h.Id("Grab_" + g[i]); grabHold[i] = h.CaptureHold(idGrab[i], grabHand[i]); }
            sitL = h.CaptureHold(idSit, 0); sitR = h.CaptureHold(idSit, 1);
            r.RestoreBind();
            if (!heroHooked)
            {
                heroHooked = true;   // (once: these act on whichever rider is built now)
                rider.Jumped += () => { popT = 0f; hero?.Restart(idPop); };
                rider.Landed += impact => { if (impact > 1.5f && hero != null) { landT = 0f; landK = Mathf.Clamp01(impact / 7f); hero.Restart(idLand); } };
            }
            return true;
        }

        /// <summary>
        /// Swap the rider for another character (the camp's outfit rack): the model, its ragdoll, its snowy clothes and
        /// its deck are rebuilt; where you are and what you're doing carry over. Nothing happens for the same one.
        /// </summary>
        public void SetCharacter(int who)
        {
            if (hero == null || who == heroLook || TestCharacter >= 0) return;   // (who: the whole look, Wardrobe.Look)
            wantLook = who;
            RestoreLimbs();   // (every limb back on before the old body goes: RiderBodyLimbs keeps its meshes)
            if (ragdoll != null) { ragdoll.Dispose(); ragdoll = null; }
            ragdollPending = false;
            // the old body goes at once (not at the end of the frame: the new one is found by searching under the same parent)
            for (int i = boardRoot.childCount - 1; i >= 0; i--) if (boardRoot.GetChild(i).name == "Snowboard") DestroyImmediate(boardRoot.GetChild(i).gameObject);
            DestroyImmediate(model.gameObject);
            DropRides();   // its skis and toboggan too (SetUpRides builds the new body's: RiderBodyRide)
            cloth.Clear(); bandages.Clear(); bandaged.Clear(); brokenThisCrash.Clear(); breaksTold = 0;   // (the receipt hooks the new ragdoll itself: HookInjuries)
            // what the limbs code read from the old body's meshes, and its hooks on the old ragdoll
            skins.Clear(); limbRadius.Clear(); limbHooks = false; Killed -= OnLimbFall;
            hero = null; rig = null; snowy = null; model = null;
            if (boardMat != null) boardPaint = boardMat;   // the deck keeps the colour the rack painted
            var keepUpper = upper.localRotation; var keepRoll = roll.localRotation; var keepRollPos = roll.localPosition;
            upper.localRotation = Quaternion.identity; roll.localRotation = Quaternion.identity; roll.localPosition = Vector3.zero;
            var pivotRot = pivot.rotation; pivot.rotation = Quaternion.identity;   // built standing upright, as at the start
            bool ok = SetUpHero();
            pivot.rotation = pivotRot;
            upper.localRotation = keepUpper; roll.localRotation = keepRoll; roll.localPosition = keepRollPos;
            if (!ok) { Debug.LogWarning("Rider swap failed"); return; }
            // the outfit colours find the new body's jacket (the game's own jacket material stays in the list)
            var game = jacketMats.Count > 0 ? jacketMats[jacketMats.Count - 1] : null;
            FindJacket(upper.gameObject);
            if (game != null && !jacketMats.Contains(game)) { jacketMats.Add(game); jacketShade.Add(1f); }
            ownMats = false;
            bool walking = Walking;
            SetTube(Tube);   // (builds the new ragdoll, and shows the board or the tube)
            if (walking) boardRoot.gameObject.SetActive(false);
            Apply(0f);
        }

        /// <summary>
        /// Pyjamas and the closet. Their fleece is as it was made while the first JACKETS row is worn (Val's blue,
        /// Breanna's cream Nordic knit); any other row dyes it, pattern and all. Val's cream lower half stays cream.
        /// </summary>
        void WearFleece(int[] o)
        {
            if (fleeceMain == null) return;
            Cloth(fleeceMain, Wardrobe.Shown(Wardrobe.Cat.Jacket, o[(int)Wardrobe.Cat.Jacket], heroOutfit), 1f);
        }

        // where each set's own shoes sit in the palette (Tools/MakeHero.py's PAL), by Wardrobe outfit: Val's sneakers,
        // Breanna's slipper boots, Paul's ski boots (both his sets), and her slipper boots again with her fleece
        static readonly int[] ShoeCell = { -1, 27, 41, 47, 47, 41 };
        /// <summary>The pyjamas' shoes take the colour of any BOOTS row but the first (theirs as made).</summary>
        void WearShoes(int[] o)
        {
            int item = o[(int)Wardrobe.Cat.Boots];
            if (heroOutfit <= 0 || heroOutfit >= ShoeCell.Length || item == 0) return;
            int px = PalettePixel(ShoeCell[heroOutfit]);
            if (px < 0) return;
            var c = Wardrobe.Get(Wardrobe.Cat.Boots, item).a;
            palNow[px] = new Color(c.r, c.g, c.b, palBase[px].a);
        }

        /// <summary>
        /// A face keeps a little light of its own (its own picture, faintly), so in the camp's shade and by night it still
        /// reads as that person from the camp's camera, and isn't a dark oval under a cap.
        /// </summary>
        public static void FaceGlow(Material m, Texture tex)
        {
            if (tex == null) return;
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", new Color(0.16f, 0.16f, 0.16f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        /// <summary>
        /// Someone else's clothes, fitted to this wearer (shapes made by Tools/MakeHero.py): the collar folded down below
        /// a jaw it would stand up through, a hood tucked away under long hair, long hair lifted clear of a bulkier jacket.
        /// The clothes' owner wears them as made.
        /// </summary>
        static void FitShapes(SkinnedMeshRenderer smr, int who, int outfit)
        {
            var m = smr.sharedMesh;
            if (m == null) return;
            int owner = outfit >= 0 && outfit < Wardrobe.OutfitOwner.Length ? Wardrobe.OutfitOwner[outfit] : 0;
            void Set(string nm, bool on) { int i = m.GetBlendShapeIndex(nm); if (i >= 0) smr.SetBlendShapeWeight(i, on ? 100f : 0f); }
            Set("CollarDown", who != owner);
            Set("HoodAway", who == 2);
            Set("HairOut", who == 2 && owner != 2);
            Set("HairPack", who == 2 && outfit == 0);   // (the summit kit's backpack)
        }

        /// <summary>The camp look (faces shown) on foot, the run look on the board; eased between.</summary>
        void CampLook(float dt, bool snap = false)
        {
            if (campShapes.Count == 0) return;
            float want = Walking ? 1f : 0f;
            campLook = snap ? want : Mathf.MoveTowards(campLook, want, dt * 1.1f);
            float w = 100f * Mathf.SmoothStep(0f, 1f, campLook);
            foreach (var (smr, shape) in campShapes) if (smr != null) smr.SetBlendShapeWeight(shape, w);
        }

        /// <summary>The board material the hero's deck wears (the game's own, so the board rack paints it).</summary>
        Material boardPaint;

        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>
        /// Riding switch the clips still face the way the rider stands, so the eyes and shoulders are turned round to
        /// where they are going: the shoulders opened toward the line, and the head round over the shoulder that now
        /// leads (further than the usual glance can turn: HeroRig's lookReach).
        /// </summary>
        void SwitchLook(ref HeroRig.Drive d, Vector3 v)
        {
            if (!SwitchNow) return;
            d.twist += -35f;
            if (v.sqrMagnitude < 1f) v = rider.transform.forward;
            d.look = v.normalized + Vector3.down * 0.15f;
            d.lookWeight = 1f; d.lookReach = 110f;
        }

        void PoseHero(float dt)
        {
            var h = hero;
            h.Begin();
            float t = Time.time;
            CampLook(dt);
            if (faceSmr != null)
            {
                // a blink every few seconds (a sixth of a second, shut in the middle), and now and then the brows go up for a moment
                if (t >= nextBlink) { blinkT = 0f; nextBlink = t + Random.Range(2.2f, 5.5f); }
                if (t >= nextBrows) { browsT = 0f; nextBrows = t + Random.Range(6f, 13f); }
                blinkT += dt; browsT += dt;
                faceSmr.SetBlendShapeWeight(blinkShape, 100f * Mathf.Clamp01(1f - Mathf.Abs(blinkT / 0.085f - 1f)));
                if (browShape >= 0) faceSmr.SetBlendShapeWeight(browShape, 100f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(browsT / 0.2f, (1.1f - browsT) / 0.3f))));
            }
            bool down = rider.Crashed;
            if (heroWasDown && !down) h.ResetSprings();   // back up after the ragdoll: no spring kick from the jump
            heroWasDown = down;
            if (RidingGear) { if (Ride == RideKind.Toboggan) PoseSled(dt, down); else PoseSkis(dt, down); return; }   // on skis or the toboggan: their own stance and clips (RiderBodyRide)
            model.localRotation = heroBaseRot;                 // side-on (skis turn the model to face forward)
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
                Flutter(dt);   // (the weather's wind in the clothes, standing about in the camp)
                // on foot: the Root bone stands on the snow, the clips stride in time with the camp's footsteps
                model.localPosition = heroRidePos - new Vector3(0f, BoardTop, 0f);
                int move = CampMove == 1 ? idCampJump : CampMove == 2 ? idCampRoll : -1;
                if (CampPose != null && CampPose.Want(h)) blend = 6f;   // the camp's own pose (on a stump at the fire, at the stove)
                else if (move >= 0)
                {
                    // a jump or a roll: its clip, held at the camp's own clock for it, so the push off and the
                    // landing happen exactly when the camp lifts and drops the body
                    h.Want(move, 1f);
                    h.At(move, Mathf.Clamp01(CampMoveT / Mathf.Max(0.01f, h.Length(move))) * 0.999f);
                    blend = 16f;
                }
                else
                {
                    // (Val has his own way of standing about: hands in his fleece pocket, rocking on his heels)
                    h.Want(heroChar == 1 && idStandVal >= 0 ? idStandVal : idStand, 1f - walkAmp);
                    h.Want(idWalk, walkAmp * (1f - runAmp));
                    h.Want(idRun, walkAmp * runAmp);
                    float ph = walkPhase / (2f * Mathf.PI);   // the left heel strikes at a quarter, as in the clips
                    h.At(idWalk, ph); h.At(idRun, ph);
                }
                d.board = null; d.feet = false;
            }
            else
            {
                Flutter(dt);
                // landings squash the legs, which spring back (the same spring as the previous rider's)
                compV += (-comp * 140f - compV * 16f) * dt;
                comp = Mathf.Clamp(comp + compV * dt, -0.15f, 0.6f);
                bool idle = !rider.Running, air = rider.Airborne;
                float toe = Mathf.Clamp(rider.Lean / Rider.MaxLean * StanceSign, -1f, 1f);   // (switch: the edges are the other way about)
                float speed01 = Mathf.Clamp01(rider.Speed / 30f);
                if (down) h.Want(idFlail, 1f);
                else if (idle) h.Want(idIdle, 1f);
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
                    d.lean = Mathf.Clamp(Mathf.Atan2(lateral, 9.81f) * Mathf.Rad2Deg * 0.45f, -15f, 15f) * StanceSign;
                    d.twist = Mathf.Clamp(-rider.YawRate * Mathf.Rad2Deg * 0.12f, -14f, 14f);
                    Vector3 v = rider.Velocity; v.y = 0f;
                    if (v.sqrMagnitude > 9f)
                    {
                        d.look = Quaternion.AngleAxis(rider.YawRate * Mathf.Rad2Deg * 0.35f, Vector3.up) * v + Vector3.down * v.magnitude * 0.15f;
                        d.lookWeight = 0.5f;
                    }
                    SwitchLook(ref d, v);
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
            if (HandGone(0)) d.handL = 0f;   // no reaching for the board with a hand that's gone (RiderBodyLimbs)
            if (HandGone(1)) d.handR = 0f;
            h.Layer(d, dt);
            if (Walking && CampPose != null) CampPose.After(h, model, dt);
        }
    }
}
