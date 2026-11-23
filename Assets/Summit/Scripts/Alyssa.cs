using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Alyssa, at the camp's fire (Camp.Campfire), making s'mores. She is built on the hero's skeleton and clips
    /// (HeroRig) so she belongs to the same world, with her own model (Resources/Rider/Alyssa, from Tools/MakeHero.py,
    /// as Val, Breanna and Paul have) made after her photos: her face, two long dark plaits,
    /// a white beanie with her goggles pushed up on it, a light grey hoodie, black snow pants and gloves; no pack, a
    /// little smaller than the hero.
    /// She sits on a stump close to the fire, facing it, and goes round her s'more over and over (about 32 s): a
    /// fresh marshmallow on the stick, held over the embers and turned; pulled out to look at it; back in, closer; it
    /// flares up, she whips it out and blows it out; she slides it onto a cracker with a square of chocolate,
    /// squashes it under the other cracker, eats it in three bites; then warms her hands until the next one.
    /// Talking to her (CampTalk) she looks round at you and holds her marshmallow back off the heat meanwhile.
    /// Her pose is the hero's standing idle with the legs folded onto the stump and the arms placed by IK (HeroRig.ArmTo),
    /// so no new clips are needed. Kept in her own file so the camp's shared people builder can take her over later.
    /// </summary>
    public partial class Alyssa : MonoBehaviour   // (her selfies: AlyssaSelfie.cs)
    {
        public const string Name = "Alyssa";
        public const float Cycle = 32f;
        /// <summary>Her size against the hero's.</summary>
        public const float Size = 0.93f;

        HeroRig h;
        Transform model;
        Marshmallow mm;
        Material cracker, chocolate;
        int idStand;
        Vector3 fire, seat, face, right;   // the fire's centre on the ground, the top of her stump, the way she faces, her right
        float t, lastT;                    // where she is in her round
        bool built;

        /// <summary>The hero she's talking to (her head turns to them, the marshmallow comes off the heat), or null.</summary>
        public Transform TalkingTo;
        /// <summary>Where to stand to talk to her (on the snow, beside her stump, facing her).</summary>
        public Vector3 TalkSpot => seat + right * 1.1f - face * 0.4f;
        public Vector3 Seat => seat;
        /// <summary>Her head (test tools: a close look at her face).</summary>
        public Transform HeadBone => h != null ? h.Head : transform;
        /// <summary>The marshmallow she's roasting now (test tools).</summary>
        public Marshmallow Mallow => mm;
        /// <summary>Where she is in her round, seconds (test tools set it to show a moment).</summary>
        public float RoundTime { get => t; set { t = Mathf.Repeat(value, Cycle); lastT = t; } }

        public static Alyssa Create(Transform parent, Vector3 fireCentre, Vector3 seatTop, Vector3 facing)
        {
            var go = new GameObject(Name);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<Alyssa>();
            a.fire = fireCentre; a.seat = seatTop;
            a.face = facing; a.face.y = 0f; a.face.Normalize();
            a.right = Vector3.Cross(Vector3.up, a.face);
            a.Build();
            return a;
        }

        void Build()
        {
            var prefab = Resources.Load<GameObject>("Rider/Alyssa");
            var table = Resources.Load<TextAsset>("Rider/HeroAnim");
            if (prefab == null || table == null) return;
            var go = Instantiate(prefab, transform, false);
            go.name = "AlyssaModel";
            model = go.transform;
            foreach (var an in go.GetComponentsInChildren<Animator>()) an.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            h = new HeroRig(model, table);
            if (!h.Ok) { DestroyImmediate(go); return; }
            idStand = h.Id("Stand");
            // her build: a head shorter than the hero, a touch slighter
            model.localScale *= Size;   // (on top of the file's own import scale)
            // no board (she isn't riding today): every rider's file comes with one
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) if (tr.name == "Snowboard") tr.gameObject.SetActive(false);
            if (h.Pack != null) h.Pack.localScale *= 0.01f;
            Dress(go);
            // at rest, facing her way
            h.Begin(); h.Want(idStand, 1f); h.Snap(); h.Evaluate(0f);
            Place();
            h.CaptureRest(face, Vector3.up);
            faceLocal = Quaternion.Inverse(h.Head.rotation) * face;
            mm = new Marshmallow(transform, Marshmallow.Plain(new Color(0.55f, 0.47f, 0.33f), 0.15f));
            BuildPhone();
            cracker = Marshmallow.Plain(new Color(0.78f, 0.56f, 0.32f), 0.1f);
            chocolate = Marshmallow.Plain(new Color(0.25f, 0.13f, 0.07f), 0.45f);
            t = 4f;   // somewhere in the middle of a roast, for whoever walks up first
            built = true;
            Step(0f);
        }

        // ---------------------------------------------------------------- her look

        // the gear palette's swatches (Tools/MakeHero.py PAL): 4 glove, 5 cuff, 19 seam
        static Vector2 SwatchUv(int cell) => new Vector2((cell % RiderBody.PaletteCols + 0.5f) / RiderBody.PaletteCols, (cell / RiderBody.PaletteCols + 0.5f) / RiderBody.PaletteRows);

        /// <summary>
        /// Her own model (Tools/MakeHero.py --character alyssa: her face, her plaits, her white beanie and goggles, made
        /// after her photos), dressed as she is in them: an oversized light-grey hoodie, black snow pants, black gloves.
        /// Her goggles are pushed up on the beanie, so her face shows; she blinks now and then.
        /// </summary>
        void Dress(GameObject go)
        {
            var jacket = Marshmallow.Plain(new Color(0.80f, 0.79f, 0.77f), 0.08f);   // light grey fleece, matt
            var pants = Marshmallow.Plain(new Color(0.07f, 0.07f, 0.08f), 0.38f);    // black nylon, a little sheen
            var gear = Gear();
            var faceM = FaceMaterial();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                // her hoodie and snow pants: shapes for the weather's wind to blow into (ClothWind; on her own copy of the mesh)
                var src = smr.sharedMesh;
                var kinds = src != null && src.isReadable ? ClothWind.KindsByMaterial(src, smr.sharedMaterials) : null;
                if (kinds != null)
                {
                    var own = Instantiate(src); own.name = src.name + " (wind)";
                    var shapes = ClothWind.Add(own, ClothWind.MeshToWorld(smr), model.TransformDirection(Vector3.forward), model.TransformDirection(Vector3.left), model.up, false, kinds);
                    smr.sharedMesh = own;
                    cloth.Add((smr, shapes));
                }
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    mats[i] = n.StartsWith("Jacket") ? jacket : n.StartsWith("Pants") ? pants : n.StartsWith("FaceSkin") ? faceM : gear;
                }
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = false;   // (her bounds follow her bones; nothing to skin while she's out of view)
                smr.quality = SkinQuality.Bone4;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var m = smr.sharedMesh; if (m == null) continue;
                int up = m.GetBlendShapeIndex("GogglesUp");
                if (up >= 0) { smr.SetBlendShapeWeight(up, 100f); FaceUncovered = true; }
                int bs = m.GetBlendShapeIndex("Blink");
                if (bs >= 0) { faceSmr = smr; blinkShape = bs; }
            }
        }

        // her face's and her gear's materials: made once for the game (the camp is rebuilt for every new mountain, and a
        // new Alyssa with it)
        static Material gearCache, faceCache;
        static Material FaceMaterial()
        {
            if (faceCache != null) return faceCache;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Marshmallow.Lit()) { name = "AlyssaFace" };
            var tex = Resources.Load<Texture2D>("Rider/AlyssaFace");
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.3f);
            RiderBody.FaceGlow(m, tex);   // (as the other riders' faces: readable in the fire's shade and by night)
            faceCache = m;
            return m;
        }
        static Material Gear()
        {
            if (gearCache != null) return gearCache;
            var gear = new Material(Marshmallow.Lit()) { name = "AlyssaGear" };
            var pal = Resources.Load<Texture2D>("Rider/HeroPalette");
            Texture2D mine = null;
            if (pal != null)
            {
                try
                {
                    var px = pal.GetPixels();
                    int w = pal.width, hgt = pal.height;
                    void Cell(int cell, Color c, float smooth)
                    {
                        Vector2 uv = SwatchUv(cell);
                        int x = Mathf.FloorToInt(uv.x * w), y = Mathf.FloorToInt(uv.y * hgt);
                        if (x < w && y < hgt) px[y * w + x] = new Color(c.r, c.g, c.b, smooth);
                    }
                    Color L(float r, float g, float b) => new Color(r, g, b).gamma;   // (the palette holds the linear values, stored as sRGB)
                    Cell(4, L(0.03f, 0.03f, 0.035f), 0.15f); Cell(5, L(0.05f, 0.05f, 0.055f), 0.1f);    // black gloves
                    Cell(19, L(0.48f, 0.47f, 0.45f), 0.08f);                                            // the hoodie's seams
                    mine = new Texture2D(w, hgt, TextureFormat.RGBA32, false) { filterMode = pal.filterMode, wrapMode = TextureWrapMode.Clamp, name = "AlyssaPalette" };
                    mine.SetPixels(px); mine.Apply(false);
                }
                catch (UnityException) { mine = null; }
                gear.SetTexture("_BaseMap", mine != null ? mine : pal);
            }
            gear.SetColor("_BaseColor", Color.white);
            gear.SetFloat("_Smoothness", 1f);
            gear.SetFloat("_SmoothnessTextureChannel", 1f);
            gear.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            gearCache = gear;
            return gear;
        }

        /// <summary>Her goggles are up on her beanie, her face showing (test tools).</summary>
        public bool FaceUncovered { get; private set; }
        Vector3 faceLocal;                 // the way her face looks, in the head bone's frame
        /// <summary>The way her face looks (test tools).</summary>
        public Vector3 FaceDir => h != null ? h.Head.rotation * faceLocal : face;
        // a blink now and then (her model's "Blink" shape)
        SkinnedMeshRenderer faceSmr; int blinkShape = -1; float nextBlink = 1.7f, blinkT = 1f;
        readonly System.Collections.Generic.List<(SkinnedMeshRenderer smr, ClothWind.Shapes shapes)> cloth = new System.Collections.Generic.List<(SkinnedMeshRenderer, ClothWind.Shapes)>();
        ClothWind.Air air;
        void Blink(float dt)
        {
            if (faceSmr == null || blinkShape < 0) return;
            blinkT += dt; nextBlink -= dt;
            if (nextBlink <= 0f) { blinkT = 0f; nextBlink = 2.2f + 3.4f * Mathf.PerlinNoise(t * 0.37f, 0.5f); }
            faceSmr.SetBlendShapeWeight(blinkShape, 100f * Mathf.Clamp01(1f - Mathf.Abs(blinkT / 0.085f - 1f)));
        }

        // ---------------------------------------------------------------- her round

        void Update() { if (Application.isPlaying) Step(Time.deltaTime); }
        void OnDestroy() { Release(); }

        /// <summary>
        /// What she made for herself, gone with her: her marshmallow (and its material), her phone's screen and her clothes'
        /// meshes (with the wind's shapes). The camp
        /// calls it before it is rebuilt (every run start; a change of the lodge's layout), as the editor's tools get no
        /// OnDestroy.
        /// </summary>
        public void Release()
        {
            mm?.Destroy(); mm = null;
            Marshmallow.Kill(screenMat); screenMat = null;
            foreach (var (smr, _) in cloth) if (smr != null && smr.sharedMesh != null && smr.sharedMesh.name.EndsWith(" (wind)")) Marshmallow.Kill(smr.sharedMesh);   // (her own copies)
            cloth.Clear();
        }

        /// <summary>How many times she's been moved on (test tools: still while you're out on the mountain).</summary>
        public static int Steps;

        static float Sm(float a, float b, float x) { float k = Mathf.Clamp01((x - a) / (b - a)); return k * k * (3f - 2f * k); }
        static float Window(float t, float a, float b, float ease = 0.5f) => Sm(a, a + ease, t) * (1f - Sm(b - ease, b, t));

        /// <summary>Move her round on by dt and pose her (public for the test tools, which have no frames).</summary>
        public void Step(float dt)
        {
            if (!built) return;
            Steps++;
            // the weather's wind in her hoodie and snow pants (out at the fire), close enough to see
            if (cloth.Count > 0 && dt > 0f)
            {
                var c = Camera.main;
                Vector3 at = h != null && h.Hips != null ? h.Hips.position : model.position;   // (where she is drawn: her own object stays at the camp's)
                if (c == null || (c.transform.position - at).sqrMagnitude < 45f * 45f)
                {
                    air.Step(dt, ClothWind.WeatherAt(at), model.TransformDirection(Vector3.forward).normalized, model.TransformDirection(Vector3.left).normalized);
                    foreach (var (smr, shapes) in cloth) air.Apply(smr, shapes);
                }
            }
            lastT = t;
            t += dt;
            if (t >= Cycle) { t -= Cycle; lastT = -1f; }
            bool talking = TalkingTo != null;
            bool Passed(float at) => lastT < at && t >= at;

            // what's happening, as weights over her round
            float roast = Window(t, 0.5f, 13.2f, 0.8f);          // the stick out at the fire
            float check = Window(t, 7.6f, 9.6f, 0.6f);           // pulled out to have a look
            float closer = Window(t, 9.6f, 11.1f, 0.4f);         // back in, closer
            float blow = Window(t, 11.0f, 12.9f, 0.3f);          // flared: out, and blown out
            float build = Window(t, 13.0f, 15.2f, 0.5f);         // the crackers together
            float eat = Window(t, 15.2f, 22.4f, 0.5f);           // the s'more up to her mouth, three times
            float warm = Window(t, 23f, 30.6f, 1.2f);            // hands out to the fire
            if (talking) { roast *= 0.35f; closer = 0f; }

            // the marshmallow
            if (Passed(0.2f) || (lastT < 0f && t < 1f)) { mm.Fresh(); mm.SetStickVisible(true); }
            float k = talking ? 0f : Mathf.Lerp(0.3f, 0.75f, closer) * (1f - check) * (1f - blow);
            if (t > 0.5f && t < 13f) mm.Cook(dt, Mathf.Lerp(0.6f, 3f, k) * roast);
            if (Passed(11.05f)) mm.Light();
            if (Passed(11.75f)) mm.BlowOut();
            if (Passed(13.6f)) mm.MakeSmore(transform, cracker, chocolate);
            if (mm.Smore != null) mm.Squash(Sm(13.8f, 14.6f, t));
            if (Passed(14.5f)) mm.SetStickVisible(false);
            if (mm.Smore != null) mm.Bite(t < 16f ? 1f : t < 18f ? 0.62f : t < 20f ? 0.3f : 0f);

            SelfieStep(dt, talking);
            Blink(dt);
            Pose(dt, roast, check, blow, build, eat, warm * (1f - SelfieWeight), k);
            mm.Refresh(0f);
        }

        void Place() => CampPoses.Seat(h, model, seat, face);

        void Pose(float dt, float roast, float check, float blow, float build, float eat, float warm, float k)
        {
            h.Begin();
            h.Want(idStand, 1f);
            if (dt <= 0f) h.Snap();
            h.Evaluate(dt);
            var d = new HeroRig.Drive { up = Vector3.up, toe = face, nose = -right };
            // she watches her marshmallow, or the fire, or whoever she's talking to
            Vector3 eye = h.Head.position;
            d.look = TalkingTo != null ? TalkingTo.position + Vector3.up * 1.5f - eye : eat > 0.5f ? face - Vector3.up * 0.3f : roast > 0.3f || check > 0.3f || blow > 0.3f ? lastTip - eye : fire - eye;
            d.look = SelfieLook(eye, d.look);   // (her head to the phone, taking a selfie)
            d.lookWeight = 0.85f;
            d.lean = 14f + 10f * roast + 10f * blow;   // hunched toward the warmth; leaning in to blow
            h.Layer(d, dt);
            Place();
            float s = Size;
            Vector3 hips = h.Hips.position, chest = h.Chest.position;
            CampPoses.FoldLegs(h, face, fire.y, s);   // legs folded in front of the stump, feet flat on the snow
            // the right hand on the stick: by the knee with it out over the fire, up at the chest to look or to blow
            float lift = Mathf.Max(check, blow), stickOut = Mathf.Max(roast, lift);
            Vector3 rest = chest + face * 0.25f * s + right * 0.12f * s - Vector3.up * 0.25f * s;
            Vector3 handR = Vector3.Lerp(rest, CampPoses.StickHand(h, lift, s), stickOut);
            // the left: in her lap, round the s'more, up to her mouth, or both hands out to the fire
            Vector3 lap = hips + face * 0.32f * s - right * 0.1f * s + Vector3.up * 0.05f;
            Vector3 mouth = CampPoses.Mouth(h, face, s);
            float bite = eat * (0.5f + 0.5f * Mathf.Cos((t - 15.2f) / 2f * Mathf.PI * 2f + Mathf.PI));   // up for a bite every two seconds
            Vector3 front = chest + face * 0.32f * s - Vector3.up * 0.12f * s;
            Vector3 handL = Vector3.Lerp(lap, front - right * 0.02f, build);
            handL = Vector3.Lerp(handL, Vector3.Lerp(front, mouth - right * 0.03f, bite), eat);
            Vector3 warmL = chest + face * 0.5f * s - right * 0.14f * s - Vector3.up * 0.15f, warmR = warmL + right * 0.28f * s;
            handL = Vector3.Lerp(handL, warmL, warm);
            handR = Vector3.Lerp(Vector3.Lerp(handR, front + right * 0.06f, build), warmR, warm);
            h.ArmTo(1, handR, h.UpperArm[1].position + right * 0.4f - Vector3.up * 0.3f);
            h.ArmTo(0, handL, h.UpperArm[0].position - right * 0.4f - Vector3.up * 0.3f);
            // the stick, out at the fire (closer: lower, into the flames), tipped up, or upright at rest
            Vector3 over = fire + Vector3.up * Mathf.Lerp(0.85f, 0.42f, k) - face * Mathf.Lerp(0.3f, 0.1f, k);
            Vector3 aim = Vector3.Slerp(CampPoses.StickRest(h), CampPoses.StickAim(h, over, lift, blow, s), stickOut);
            lastTip = CampPoses.HoldStick(h, mm, aim, t * 70f);
            PoseSelfie(s);   // the phone and the peace sign, over the rest
            if (mm.Smore != null) { mm.Smore.position = h.Hand[0].position + face * 0.06f * s + Vector3.up * 0.03f; mm.Smore.rotation = Quaternion.LookRotation(face, Vector3.up); }
        }

        Vector3 lastTip;
    }
}
