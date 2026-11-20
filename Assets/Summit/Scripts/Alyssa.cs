using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Alyssa, at the camp's fire (Camp.Campfire), making s'mores. She is built from the hero's own model and clips
    /// (Resources/Rider/Hero, HeroRig) so she belongs to the same world, but dressed and built as herself: a teal
    /// parka, plum trousers, cream mittens, a mustard knitted beanie with a pom-pom instead of the helmet, a long
    /// auburn braid down her back, no pack, a little smaller than the hero.
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
            var prefab = Resources.Load<GameObject>("Rider/Hero");
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
            // no board, no pack (she isn't riding today): the snowboard hidden, the pack folded away into its bone
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) if (tr.name == "Snowboard") tr.gameObject.SetActive(false);
            // she's in gloves: the model's bare hands stay hidden under them (as the hero's do in the summit kit)
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.name.StartsWith("HeroHands")) smr.gameObject.SetActive(false);
            if (h.Pack != null) h.Pack.localScale *= 0.01f;
            Dress(go);
            // at rest, facing her way: then the hair and pom-pom go on where the head is
            h.Begin(); h.Want(idStand, 1f); h.Snap(); h.Evaluate(0f);
            Place();
            h.CaptureRest(face, Vector3.up);
            Hair();
            mm = new Marshmallow(transform, Marshmallow.Plain(new Color(0.55f, 0.47f, 0.33f), 0.15f));
            BuildPhone();
            cracker = Marshmallow.Plain(new Color(0.78f, 0.56f, 0.32f), 0.1f);
            chocolate = Marshmallow.Plain(new Color(0.25f, 0.13f, 0.07f), 0.45f);
            t = 4f;   // somewhere in the middle of a roast, for whoever walks up first
            built = true;
            Step(0f);
        }

        // ---------------------------------------------------------------- her look

        // the gear palette's swatches (Tools/MakeHero.py PAL): 0 skin, 1 helmet, 2 goggle frame, 3 lens, 4 glove, 5 cuff,
        // 6 boot, 7 sole, 8 strap, 11 helmet vent, 12 nose, 16..18 the pack, 19 seam, 20 zip, 21 neck gaiter,
        // 22 its band, 23 chin strap
        static readonly Color Skin = new Color(0.88f, 0.60f, 0.47f);     // fair and warm (linear, as the palette holds it)
        static Vector2 SwatchUv(int cell) => new Vector2((cell % RiderBody.PaletteCols + 0.5f) / RiderBody.PaletteCols, (cell / RiderBody.PaletteCols + 0.5f) / RiderBody.PaletteRows);

        /// <summary>
        /// Dressed as she is in her photos: an oversized light-grey hoodie (plain: no lettering), black insulated snow
        /// pants, black gloves, tan boots, a white ribbed beanie in place of the helmet. The hero's goggles and neck gaiter
        /// cover the face; on her they're skin, so her face shows (eyes, brows and a smile go on it in Face()).
        /// </summary>
        void Dress(GameObject go)
        {
            var jacket = Marshmallow.Plain(new Color(0.80f, 0.79f, 0.77f), 0.08f);   // light grey fleece, matt
            var pants = Marshmallow.Plain(new Color(0.07f, 0.07f, 0.08f), 0.38f);    // black nylon, a little sheen
            var gear = Gear();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    mats[i] = n.StartsWith("Jacket") ? jacket : n.StartsWith("Pants") ? pants : gear;
                }
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = false;   // (her bounds follow her bones; nothing to skin while she's out of view)
                smr.quality = SkinQuality.Bone4;
            }
        }

        // her gear's material and her own copy of the palette: made once for the game (the camp is rebuilt for every
        // new mountain, and a new Alyssa with it)
        static Material gearCache;
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
                    Color skin = Skin.gamma, grey = L(0.60f, 0.58f, 0.55f);
                    foreach (int c in new[] { 0, 12 }) Cell(c, skin, 0.25f);                             // (any skin the hero shows)
                    Cell(1, L(0.90f, 0.90f, 0.88f), 0.02f); Cell(11, L(0.82f, 0.82f, 0.80f), 0.02f);    // a white ribbed beanie
                    Cell(4, L(0.03f, 0.03f, 0.035f), 0.15f); Cell(5, L(0.05f, 0.05f, 0.055f), 0.1f);    // black gloves
                    Cell(6, L(0.55f, 0.36f, 0.20f), 0.12f); Cell(7, L(0.70f, 0.58f, 0.42f), 0.1f);      // tan suede boots, pale soles
                    foreach (int c in new[] { 8, 16, 17, 18, 20 }) Cell(c, grey, 0.08f);                // (the pack's straps and the zip: the hoodie's grey)
                    Cell(19, L(0.48f, 0.47f, 0.45f), 0.08f);                                            // its seams
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

        /// <summary>
        /// Find a swatch on her, standing at rest: the middle of every vertex painted with it, and how far it stands
        /// out in front (along her facing) and to either side. The model has no face bones, so this is how her face is placed.
        /// </summary>
        bool Swatch(int cell, out Vector3 centre, out float front, out float width, out float low)
        {
            centre = Vector3.zero; front = float.MinValue; width = 0f; low = float.MaxValue; int n = 0;
            var uvWant = SwatchUv(cell);
            var pts = new System.Collections.Generic.List<Vector3>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = new Mesh(); smr.BakeMesh(m);
                var v = m.vertices; var uv = m.uv;
                if (uv == null || uv.Length != v.Length) { DestroyImmediate(m); continue; }
                var tr = smr.transform;
                for (int i = 0; i < v.Length; i++)
                    if ((uv[i] - uvWant).sqrMagnitude < 0.0004f) pts.Add(tr.position + tr.rotation * v[i]);
                DestroyImmediate(m);
            }
            foreach (var p in pts) { centre += p; n++; }
            if (n == 0) return false;
            centre /= n;
            foreach (var p in pts)
            {
                Vector3 d = p - centre;
                front = Mathf.Max(front, Vector3.Dot(d, face));
                width = Mathf.Max(width, Mathf.Abs(Vector3.Dot(d, right)));
                low = Mathf.Min(low, d.y);
            }
            return true;
        }

        /// <summary>
        /// The hero's goggles and neck gaiter cover the whole face; on her own copy of the mesh their vertices are folded
        /// back into the head (toward the middle of the beanie and gaiter), out of sight, so a face can go there.
        /// </summary>
        void FoldFaceCover()
        {
            var cover = new System.Collections.Generic.HashSet<int> { 2, 3, 21, 22, 23 };
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var src = smr.sharedMesh; if (src == null) continue;
                var uv = src.uv; var v = src.vertices;
                if (uv == null || uv.Length != v.Length) continue;
                // which vertices are the cover, and the middle of the head they fold toward (in the mesh's own space)
                var hit = new System.Collections.Generic.List<int>(); Vector3 mid = Vector3.zero; int n = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    int cell = -1;
                    foreach (int c in cover) if ((uv[i] - SwatchUv(c)).sqrMagnitude < 0.0004f) { cell = c; break; }
                    bool head = (uv[i] - SwatchUv(1)).sqrMagnitude < 0.0004f;
                    if (cell >= 0) hit.Add(i);
                    if (cell >= 0 || head) { mid += v[i]; n++; }
                }
                if (hit.Count == 0 || n == 0) continue;
                FaceUncovered = true;
                mid /= n;
                if (!folded.TryGetValue(src, out var m) || m == null)
                {
                    m = Instantiate(src); m.name = src.name + " (Alyssa)";
                    foreach (int i in hit) v[i] = Vector3.Lerp(v[i], mid, 0.8f);
                    m.vertices = v; m.RecalculateBounds();
                    folded[src] = m;
                }
                smr.sharedMesh = m;
            }
        }

        /// <summary>The hero's goggles and gaiter were found on the mesh and folded away (test tools).</summary>
        public bool FaceUncovered { get; private set; }
        static readonly System.Collections.Generic.Dictionary<Mesh, Mesh> folded = new System.Collections.Generic.Dictionary<Mesh, Mesh>();   // (made once each)
        Transform faceObj;
        /// <summary>The way her face looks (test tools).</summary>
        public Vector3 FaceDir => faceObj != null ? faceObj.forward : face;

        /// <summary>
        /// Her face, her goggles pushed up on the beanie, and her hair: two long dark braids from under the beanie,
        /// worn in front of the shoulders. All on the head bone (the braids' lower halves on the chest), placed from
        /// where the beanie is on the mesh.
        /// </summary>
        void Hair()
        {
            var skinM = Marshmallow.Plain(new Color(0.95f, 0.80f, 0.71f), 0.3f);   // fair, warm (as a colour, not the palette's linear value)
            var hair = Marshmallow.Plain(new Color(0.075f, 0.055f, 0.045f), 0.35f);   // near-black brown
            var eyeM = Marshmallow.Plain(new Color(0.20f, 0.11f, 0.06f), 0.85f);      // brown eyes, bright
            var browM = Marshmallow.Plain(new Color(0.12f, 0.08f, 0.06f), 0.2f);
            var lipM = Marshmallow.Plain(new Color(0.66f, 0.32f, 0.32f), 0.4f);
            var cheekM = Marshmallow.Plain(new Color(0.96f, 0.72f, 0.66f), 0.25f);
            var frameM = Marshmallow.Plain(new Color(0.94f, 0.94f, 0.92f), 0.6f);    // white goggle frame
            var lensM = Marshmallow.Plain(new Color(0.16f, 0.12f, 0.09f), 0.95f);     // smoked lens
            var tieM = Marshmallow.Plain(new Color(0.02f, 0.02f, 0.02f), 0.3f);
            var head = h.Head; var chest = h.UpperChest != null ? h.UpperChest : h.Chest;
            Vector3 up = Vector3.up, f = face, r = right;
            float s = Size;
            Transform Prim(PrimitiveType p, Transform parent, Vector3 world, Quaternion rot, Vector3 size, Material m, bool shadow = false)
            {
                var b = Marshmallow.Prim(p, transform, world, size, m);
                b.rotation = rot;
                b.SetParent(parent, true);
                if (shadow) b.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                return b;
            }
            Quaternion facing = Quaternion.LookRotation(f, up);
            bool beanie = Swatch(1, out Vector3 cap, out float capFront, out float capW, out float capLow);
            if (!beanie) { cap = head.position + up * 0.12f * s; capFront = 0.11f * s; capW = 0.12f * s; capLow = -0.1f * s; }
            FoldFaceCover();
            // her face: an oval under the beanie's brim, and on it her eyes, brows, nose, cheeks and a smile
            float a = 0.069f * s, b = 0.09f * s, c = 0.074f * s;   // the oval's half width, height and depth
            float rim = cap.y + capLow;
            Vector3 fc = new Vector3(cap.x, rim - 0.035f * s, cap.z) + f * (capFront - c - 0.004f);
            faceObj = Prim(PrimitiveType.Sphere, head, fc, facing, new Vector3(2f * a, 2f * b, 2f * c), skinM, true);
            Vector3 Surf(float x, float y, float lift = 0f)
            {
                float k = 1f - (x * x) / (a * a) - (y * y) / (b * b);
                return fc + r * x + up * y + f * (c * Mathf.Sqrt(Mathf.Max(0f, k)) + lift);
            }
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 e = Surf(sd * 0.026f * s, 0.022f * s, -0.002f);
                Prim(PrimitiveType.Sphere, head, e, facing, new Vector3(0.02f, 0.016f, 0.009f) * s, eyeM);   // (a touch bold, so they carry at the camp camera's distance)
                Prim(PrimitiveType.Cube, head, Surf(sd * 0.027f * s, 0.043f * s, -0.001f), facing * Quaternion.Euler(0f, sd * 18f, -sd * 9f), new Vector3(0.026f, 0.006f, 0.006f) * s, browM);
                Prim(PrimitiveType.Sphere, head, Surf(sd * 0.04f * s, -0.012f * s, -0.004f), facing, new Vector3(0.024f, 0.014f, 0.006f) * s, cheekM);
            }
            Prim(PrimitiveType.Sphere, head, Surf(0f, 0.002f * s, 0.006f * s), facing, new Vector3(0.018f, 0.026f, 0.02f) * s, skinM);   // her nose
            // the smile: a wide curve, the corners up
            Vector3 mouth = Surf(0f, -0.04f * s, -0.002f);
            Prim(PrimitiveType.Capsule, head, mouth, facing * Quaternion.Euler(0f, 0f, 90f), new Vector3(0.01f, 0.016f, 0.006f) * s, lipM);
            for (int sd = -1; sd <= 1; sd += 2)
                Prim(PrimitiveType.Capsule, head, Surf(sd * 0.021f * s, -0.034f * s, -0.002f), facing * Quaternion.Euler(0f, sd * 20f, 90f + sd * 40f), new Vector3(0.008f, 0.01f, 0.006f) * s, lipM);
            // the goggles pushed up on the beanie: a white frame curving round its front, a smoked lens, a black strap round the back
            Vector3 gog = new Vector3(cap.x, rim + 0.06f * s, cap.z) + f * (capFront - 0.012f);
            Quaternion tilt = facing * Quaternion.Euler(-22f, 0f, 0f);
            Prim(PrimitiveType.Capsule, head, gog, tilt * Quaternion.Euler(0f, 0f, 90f), new Vector3(0.062f, 0.095f, 0.035f) * s, frameM, true);
            Prim(PrimitiveType.Capsule, head, gog + f * 0.01f * s, tilt * Quaternion.Euler(0f, 0f, 90f), new Vector3(0.046f, 0.08f, 0.03f) * s, lensM);
            float band = capW * 2.06f;
            Prim(PrimitiveType.Cylinder, head, new Vector3(cap.x, gog.y - 0.004f, cap.z), Quaternion.AngleAxis(-10f, r), new Vector3(band, 0.008f, band), tieM);
            // two braids from under the beanie's sides, over the front of the shoulders, down onto the chest
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 from = new Vector3(cap.x, rim + 0.005f, cap.z) + r * (sd * capW * 0.85f) - f * 0.015f;
                const int N = 22;
                for (int i = 0; i <= N; i++)
                {
                    float k = i / (float)N;
                    // down past the ear, out over the shoulder, then hanging forward on the chest
                    Vector3 at = from - up * (0.5f * k) * s + f * (0.13f * Mathf.SmoothStep(0f, 1f, k * 1.6f)) * s + r * (sd * 0.05f * Mathf.Sin(k * Mathf.PI)) * s
                               + r * (0.006f * ((i & 1) == 0 ? 1f : -1f)) * s;   // the plait's twist
                    float rad = Mathf.Lerp(0.034f, 0.024f, k) * s;
                    Prim(PrimitiveType.Sphere, k < 0.25f ? head : chest, at, facing * Quaternion.Euler(0f, 0f, ((i & 1) == 0 ? 25f : -25f)), new Vector3(rad, rad * 1.5f, rad), hair, true);
                    if (i == N)
                    {
                        Prim(PrimitiveType.Cylinder, chest, at - up * 0.02f * s, facing, new Vector3(0.018f, 0.006f, 0.018f) * s, tieM);
                        Prim(PrimitiveType.Capsule, chest, at - up * 0.06f * s, facing, new Vector3(0.028f, 0.04f, 0.022f) * s, hair);   // the loose end
                    }
                }
            }
        }

        // ---------------------------------------------------------------- her round

        void Update() { if (Application.isPlaying) Step(Time.deltaTime); }
        void OnDestroy() { Release(); }

        /// <summary>
        /// What she made for herself, gone with her: her marshmallow (and its material) and her phone's screen. The camp
        /// calls it before it is rebuilt (every run start; a change of the lodge's layout), as the editor's tools get no
        /// OnDestroy.
        /// </summary>
        public void Release()
        {
            mm?.Destroy(); mm = null;
            Marshmallow.Kill(screenMat); screenMat = null;
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
