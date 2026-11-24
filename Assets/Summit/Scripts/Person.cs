using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// One of the mountain's people: the hero's model and clips (HeroRig) dressed from a PersonLook, posed each
    /// frame from what whoever owns them asks: stand, walk or run, sit (on a chair, a bench, the snow), ride a board
    /// or ski; look at something, point at it, hold a phone up, put the hands somewhere. The owner sets the fields
    /// and calls Apply(dt) once a frame; nothing here runs by itself, so idle people cost nothing.
    ///
    /// Two levels of detail on one skeleton: the full model (three skinned meshes in the closet's own materials,
    /// patterns and all) up close, and one simplified skinned mesh in the shared slot-coloured material (PeopleLit)
    /// further off. SetLod(2) hides both and stops the posing (far off, the lifts draw their riders as instanced
    /// baked meshes instead: PeopleLod, LiftRiders).
    ///
    /// The person's own transform is where they are and the way they face (+z): on the ground between the feet
    /// standing or walking, on the seat under the hips sitting, on the board's deck riding (RideFrame turns it so
    /// the nose leads: the clips ride side-on).
    /// </summary>
    public class Person : MonoBehaviour
    {
        public enum Mode { Stand, Walk, Sit, Ride, Ski }
        public enum Held { None, Underfoot, Placed }

        public PersonLook Look { get; private set; }
        public HeroRig Rig { get; private set; }
        public Transform Model { get; private set; }
        public SkinnedMeshRenderer BodySkin, LegsSkin, GearSkin, HeadSkin, HandsSkin, LodSkin;
        /// <summary>How far indoors they are, 0 out in the weather .. 1 inside (the lodge's own set it): no wind in the clothes in there.</summary>
        public float Shelter;
        /// <summary>No wind in the clothes at all (a body posed for baking: LiftRiders).</summary>
        public bool Windless;
        ClothWind.Air air;
        readonly SkinnedMeshRenderer[] skins = new SkinnedMeshRenderer[4];
        public SkinnedMeshRenderer[] Skins => skins;
        public readonly List<MeshRenderer> Rigid = new List<MeshRenderer>();
        public MeshRenderer Board, Phone;
        public readonly MeshRenderer[] Skis = new MeshRenderer[2], Poles = new MeshRenderer[2];
        public int Lod { get; private set; } = -1;
        public float Scale => Look != null ? Look.height : 1f;

        // ---- what the owner asks for, each frame
        public Mode mode = Mode.Stand;
        public float pace;            // 0 standing .. 1 walking (riding/skiing: 0 idle, 1 moving)
        public float run;             // 0 walking .. 1 running
        public float carve;           // riding: -1 heel edge .. 1 toe edge
        public float phase;           // the walk cycle, 0..1 (Advance moves it with the ground covered)
        public Vector3 lookAt; public float lookWeight;   // a point the head turns to (weight 0: the clip's own)
        public float turnChest;       // 0..1: how much of a look the shoulders join in
        public float clipRate = 1f;   // the idle clip's breathing and weight shifts, quicker or slower
        // sitting (mode Sit): the transform is the seat's surface under the hips, its forward the way they face
        public float sitW = 1f;       // 0 standing up .. 1 down (the owner eases it, for sitting down and getting up)
        public float lean = 6f;       // degrees the back leans back from upright (negative: forward, over the knees)
        public float hipsForward;     // m: the hips this far forward of the seat point (perched, or a pack on the back)
        public bool feetDown;         // the feet go down to footY (a bench, the snow); otherwise they hang (a chair)
        public float footY;           // the floor's height (world y) when feetDown
        public float feetForward;     // m: the feet this far ahead of the knees (on the snow, legs out)
        public float kneesApart = 0.5f;
        public float kneesShift;      // the knees (and feet) a little to one side (+: right), clear of something
        public float swing;           // hanging feet: a slow swing (radians, either way)
        // hands: where each goes (world), how much (0 the clip's own .. 1 there)
        public readonly Vector3[] handAt = new Vector3[2];
        public readonly float[] handW = new float[2];
        public Vector3 pointAt; public float pointW; public int pointHand = 1;
        public float phoneUp;         // 0..1: holding a phone up to take a picture (in the right hand)
        // the gear: the board under the feet, or placed (boardFrame: the deck's middle on top, x to the nose, y out
        // of the deck); skis on the boots; poles from the fists (both in one hand, or one each), pointing polesDir
        public Held held = Held.None;
        public Matrix4x4 boardFrame = Matrix4x4.identity;
        public bool skisOn, polesOn, polesInOneHand;
        public Vector3 polesDir = Vector3.down;
        // sitting with the board strapped to one foot (a chairlift): which foot (-1 none), the nose's pitch down
        // (degrees) and how far it turns out from straight ahead; the board hangs from that foot (held = Placed)
        public int boardFoot = -1;
        public int boardInHand = -1;  // carried under an arm: which hand holds its edge (-1 none; held = Placed)
        public bool skisOnShoulder;   // the skis carried on the right shoulder, the right hand holding them, the poles in the left
        public float crouch;          // riding stance: how far down (0..1: strapping in, reaching for a binding)
        public float boardPitch = -20f;   // (nose up: the binding is ahead of the board's middle, so the tail hangs)
        public float boardTurn;           // degrees the nose turns from straight ahead (+: to the right)
        public float toesDown = 24f;  // hanging feet: how far the toes drop (degrees)

        int idStand, idWalk, idRun, idRideIdle, idRide, idToe, idHeel, idSkiIdle, idSkiRide;
        readonly Quaternion[] footRest = new Quaternion[2], handBind = new Quaternion[2], twistBind = new Quaternion[2];
        Vector3 headFace;
        Vector3 boardPos0; Quaternion boardRot0; Vector3 boardScale0;
        Material jacketM, pantsM, gearM, boardM;
        Texture2D pal; Color[] palPx;
        MaterialPropertyBlock mpb;

        static readonly Quaternion Turn = Quaternion.Euler(0f, 180f, 0f);   // the model faces its -z; the person faces +z

        // ---------------------------------------------------------------- building

        /// <summary>A person with this look, under parent (null: the people's own root). Null if the hero can't be had.</summary>
        public static Person Make(PersonLook look, Transform parent = null, string name = null)
        {
            if (!PeopleKit.Load()) return null;
            var go = new GameObject(name ?? (string.IsNullOrEmpty(look.name) ? "Person" : look.name));
            go.transform.SetParent(parent != null ? parent : PeopleKit.Root, false);
            var p = go.AddComponent<Person>();
            if (!p.Build(look)) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); return null; }
            return p;
        }

        bool Build(PersonLook look)
        {
            var go = Instantiate(PeopleKit.Prefab, transform, false);
            go.name = "Model";
            Model = go.transform;
            Model.localRotation = Turn;
            foreach (var a in go.GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            Rig = new HeroRig(Model, PeopleKit.Clips);
            if (!Rig.Ok) return false;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string n = smr.name;
                if (n.StartsWith("HeroBody")) BodySkin = smr; else if (n.StartsWith("HeroLegs")) LegsSkin = smr;
                else if (n.StartsWith("HeroGear")) GearSkin = smr; else if (n.StartsWith("HeroHead")) HeadSkin = smr;
                else if (n.StartsWith("HeroHands")) HandsSkin = smr;
                smr.updateWhenOffscreen = false;
                smr.quality = SkinQuality.Bone4;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (BodySkin == null || LegsSkin == null || GearSkin == null) return false;
            if (HandsSkin != null) HandsSkin.gameObject.SetActive(false);   // (the summit kit has gloves: the bare hands stay unseen)
            skins[0] = BodySkin; skins[1] = LegsSkin; skins[2] = GearSkin; skins[3] = HeadSkin;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.name == "Snowboard") { Board = mr; boardPos0 = mr.transform.localPosition; boardRot0 = mr.transform.localRotation; boardScale0 = mr.transform.localScale; }
            idStand = Rig.Id("Stand"); idWalk = Rig.Id("Walk"); idRun = Rig.Id("Run");
            idRideIdle = Rig.Id("Ride_Idle"); idRide = Rig.Id("Ride"); idToe = Rig.Id("Carve_Toe"); idHeel = Rig.Id("Carve_Heel");
            idSkiIdle = Rig.Id("Ski_Idle"); idSkiRide = Rig.Id("Ski_Ride");
            // at rest: the feet's, hands' and face's turn, for setting them square later
            for (int s = 0; s < 2; s++)
            {
                footRest[s] = Quaternion.Inverse(transform.rotation) * Rig.Foot[s].rotation;
                handBind[s] = Rig.Hand[s].localRotation;
                twistBind[s] = Rig.Twist[s] != null ? Rig.Twist[s].localRotation : Quaternion.identity;
            }
            headFace = Quaternion.Inverse(Rig.Head.rotation) * transform.forward;
            Rig.CaptureRest(transform.forward, transform.up);
            if (PeopleKit.SkiMesh != null && PeopleKit.PoleMesh != null)
                for (int s = 0; s < 2; s++)
                {
                    Skis[s] = Piece("Ski" + (s == 0 ? "L" : "R"), PeopleKit.SkiMesh);
                    Poles[s] = Piece("Pole" + (s == 0 ? "L" : "R"), PeopleKit.PoleMesh);
                }
            Phone = Piece("Phone", PhoneMesh());
            Phone.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Dress(look);
            SetLod(0);
            return true;
        }

        MeshRenderer Piece(string n, Mesh mesh)
        {
            var g = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer));
            g.transform.SetParent(transform, false);
            g.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            g.SetActive(false);
            Rigid.Add(mr);
            return mr;
        }

        static Mesh phoneMesh;
        static Mesh PhoneMesh()
        {
            if (phoneMesh != null) return phoneMesh;
            // a phone: a thin dark slab (the gear palette's sole black), upright in front of the face
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            phoneMesh = Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
            if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
            var v = phoneMesh.vertices; var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) { v[i] = Vector3.Scale(v[i], new Vector3(0.15f, 0.072f, 0.009f)); uv[i] = PeopleKit.CellUv(PeopleKit.CSole); }
            phoneMesh.vertices = v; phoneMesh.uv = uv; phoneMesh.RecalculateBounds(); phoneMesh.name = "Phone";
            return phoneMesh;
        }

        /// <summary>Put a look on: the body's size, the gear's shape, every colour. (A pooled person can change look.)</summary>
        public void Dress(PersonLook look)
        {
            // the look's row of the people's palette (its colours far off) is kept for as long as it's worn
            if (look != Look) { PeopleKit.Hold(look); PeopleKit.Let(Look); }
            Look = look;
            Model.localScale = Vector3.one * look.height;
            // girth through the hips (and so the legs and everything above), the shoulders on top; the head kept round
            var chest = Rig.UpperChest != null ? Rig.UpperChest : Rig.Chest;
            if (Rig.Hips != null) Rig.Hips.localScale = new Vector3(look.build, 1f, 1f);
            if (chest != null) chest.localScale = new Vector3(look.shoulders, 1f, 1f);
            if (Rig.Neck != null) Rig.Neck.localScale = new Vector3(1f / (look.build * look.shoulders), 1f, 1f);
            // the summit kit as cut for them (a man's or a woman's: one skeleton, so either fits these bones)
            var cut = PeopleKit.CutOf(look);
            BodySkin.sharedMesh = cut.Body; LegsSkin.sharedMesh = cut.Legs; GearSkin.sharedMesh = PeopleKit.Gear(look);
            if (HeadSkin != null) HeadSkin.sharedMesh = PeopleKit.Head(look);
            if (jacketM == null)
            {
                // the clothes: the closet's cloth (its patterns drawn on the rest pose), with no snow on them
                var t = PeopleKit.ClothTemplate;
                jacketM = t != null ? new Material(t) : new Material(PeopleKit.Lit);
                pantsM = t != null ? new Material(t) : new Material(PeopleKit.Lit);
                jacketM.name = "PersonJacket"; pantsM.name = "PersonPants";
                gearM = new Material(PeopleKit.Lit) { name = "PersonGear" };
                pal = new Texture2D(PeopleKit.PaletteSrc.width, PeopleKit.PaletteSrc.height, TextureFormat.RGBA32, false) { filterMode = PeopleKit.PaletteSrc.filterMode, wrapMode = TextureWrapMode.Clamp, name = "PersonPalette" };
                palPx = new Color[pal.width * pal.height];
                gearM.SetTexture("_BaseMap", pal); gearM.mainTexture = pal;
                gearM.SetColor("_BaseColor", Color.white);
                gearM.SetFloat("_Smoothness", 1f);
                gearM.SetFloat("_SmoothnessTextureChannel", 1f);
                gearM.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                boardM = new Material(PeopleKit.Lit) { name = "PersonBoard" };
            }
            Cloth(jacketM, look.jacket, 0.35f);
            Cloth(pantsM, look.pants, 0.2f);
            PeopleKit.PaintPalette(look, palPx, pal.width, pal.height);
            pal.SetPixels(palPx); pal.Apply(false);
            var b = look.board ?? Wardrobe.Get(Wardrobe.Cat.Board, 0);
            boardM.SetColor("_BaseColor", b.a);
            boardM.SetFloat("_Smoothness", b.smooth >= 0f ? b.smooth : 0.6f);
            boardM.SetFloat("_Metallic", b.metal);
            BodySkin.sharedMaterial = jacketM; LegsSkin.sharedMaterial = pantsM; GearSkin.sharedMaterial = gearM;
            if (HeadSkin != null) HeadSkin.sharedMaterial = gearM;
            if (Board != null) Board.sharedMaterials = Mats(PeopleKit.BoardSrcMats);
            for (int i = 0; i < 2; i++)
            {
                if (Skis[i] != null) Skis[i].sharedMaterials = Mats(PeopleKit.SkiSrcMats);
                if (Poles[i] != null) Poles[i].sharedMaterials = Mats(PeopleKit.PoleSrcMats);
            }
            if (Phone != null) Phone.sharedMaterial = gearM;
            // the simplified body for distance: the same skeleton, one mesh, the one shared material
            if (LodSkin == null)
            {
                var g = new GameObject("Simple");
                g.transform.SetParent(Model, false);
                g.transform.localPosition = GearSkin.transform.localPosition; g.transform.localRotation = GearSkin.transform.localRotation; g.transform.localScale = GearSkin.transform.localScale;
                LodSkin = g.AddComponent<SkinnedMeshRenderer>();
                LodSkin.bones = GearSkin.bones; LodSkin.rootBone = GearSkin.rootBone;
                LodSkin.sharedMaterial = PeopleKit.LodMaterial;
                LodSkin.quality = SkinQuality.Bone2;
                LodSkin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                LodSkin.enabled = false;
                mpb = new MaterialPropertyBlock();
            }
            // (the simplified body is made when it's first wanted, a shape a frame: PeopleLod.BuildPending)
            simpleFor = -1;
            if (Lod == 1) { Lod = -1; SetLod(1); }
        }
        int simpleFor = -1;

        /// <summary>The simplified body for this look, if it's made yet (asked for if not).</summary>
        bool Simple()
        {
            if (simpleFor != Look.ShapeKey || LodSkin.sharedMesh == null)
            {
                if (!PeopleLod.Has(Look)) { PeopleLod.Want(Look); return false; }
                LodSkin.sharedMesh = PeopleLod.Skinned(Look);
                LodSkin.localBounds = GearSkin.localBounds;
                simpleFor = Look.ShapeKey; simpleRow = -1;
            }
            // its colours: the look's row of the shared palette (held while worn: Dress), asked for each time
            int r = PeopleKit.Row(Look);
            if (r != simpleRow)
            {
                simpleRow = r;
                mpb.SetFloat("_Row", r);
                LodSkin.SetPropertyBlock(mpb);
                PeopleKit.Flush();
            }
            return true;
        }
        int simpleRow = -1;

        Material[] Mats(Material[] src)
        {
            if (src == null) return new[] { gearM };
            var m = new Material[src.Length];
            for (int i = 0; i < m.Length; i++) m[i] = src[i] != null && src[i].name.StartsWith("Board") ? boardM : gearM;
            return m;
        }

        static void Cloth(Material m, Wardrobe.Item it, float smooth)
        {
            if (it == null) return;
            m.SetColor("_BaseColor", it.a);
            if (m.HasProperty("_Color2"))
            {
                m.SetColor("_Color2", it.b.a > 0f ? it.b : it.a);
                m.SetFloat("_Pattern", (float)it.pattern);
                m.SetFloat("_PatternScale", it.scale);
                m.SetFloat("_PatternAt", it.at);
                m.SetFloat("_Snow", 0f);
                m.SetVector("_SnowUp", Vector3.up);
            }
            m.SetFloat("_Smoothness", it.smooth >= 0f ? it.smooth : smooth);
        }

        /// <summary>0: the full model; 1: the simplified one; 2: neither (and Apply does nothing).</summary>
        public void SetLod(int lod)
        {
            if (lod == 1 && !Simple()) lod = 0;   // (not made yet: the full model meanwhile)
            if (lod == Lod) return;
            Lod = lod;
            foreach (var s in skins) if (s != null) s.enabled = lod == 0;
            if (LodSkin != null) LodSkin.enabled = lod == 1;
            if (lod >= 2) foreach (var r in Rigid) if (r != null && r.gameObject.activeSelf) r.gameObject.SetActive(false);
            if (Board != null) Board.enabled = lod < 2 && held != Held.None;
        }

        /// <summary>Shadows on or off (riders on a chair are in the air: theirs fall far below, and cost).</summary>
        public void Shadows(bool on)
        {
            var m = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var s in skins) if (s != null) s.shadowCastingMode = m;
            if (LodSkin != null) LodSkin.shadowCastingMode = m;
            foreach (var r in Rigid) if (r != null && r != Phone) r.shadowCastingMode = m;
            if (Board != null) Board.shadowCastingMode = m;
        }

        /// <summary>The way the face looks now.</summary>
        public Vector3 Facing => Rig.Head.rotation * headFace;
        public Vector3 HeadPosition => Rig.Head.position;

        // ---------------------------------------------------------------- walking and riding

        /// <summary>
        /// Move the walk cycle on by the ground covered (m), so the feet never slide: one cycle (two steps) is as
        /// long as the clip's planted foot travels in it, scaled with the body.
        /// </summary>
        public void Advance(float metres)
        {
            // (part way into the walk the clip's stride is that much shorter: a slow walk takes short steps)
            float cycle = Mathf.Lerp(PeopleLod.WalkCycle, PeopleLod.RunCycle, run) * Scale * PeopleLod.StrideAt(Mathf.Max(pace, 0.15f));
            phase = Mathf.Repeat(phase + metres / Mathf.Max(0.1f, cycle), 1f);
        }

        /// <summary>How fast this body walks (or runs) at the clip's own cadence (m/s).</summary>
        public float NaturalSpeed => Mathf.Lerp(PeopleLod.WalkCycle / Mathf.Max(0.01f, Rig.Length(idWalk)), PeopleLod.RunCycle / Mathf.Max(0.01f, Rig.Length(idRun)), run) * Scale;

        /// <summary>Stand on the board here, its nose toward travel (the clips ride side-on, facing the toe edge).</summary>
        public void RideFrame(Vector3 at, Vector3 travel, Vector3 up)
        {
            Vector3 right = -travel.normalized;
            transform.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.Cross(right, up).normalized, up));
        }

        // ---------------------------------------------------------------- posing

        bool snapped;
        readonly Quaternion[] legClip = new Quaternion[6];

        /// <summary>Pose the body for this frame (dt 0: snap straight to it).</summary>
        public void Apply(float dt)
        {
            if (Lod >= 2 || Rig == null) return;
            var h = Rig;
            h.Begin();
            Model.localPosition = Vector3.zero;
            Model.localRotation = Turn;
            switch (mode)
            {
                case Mode.Walk:
                case Mode.Stand:
                {
                    float p = mode == Mode.Walk ? pace : 0f;
                    h.Want(idStand, 1f - p);
                    h.Want(idWalk, p * (1f - run));
                    h.Want(idRun, p * run);
                    h.At(idWalk, phase); h.At(idRun, phase);
                    break;
                }
                case Mode.Sit:
                    h.Want(idStand, 1f);
                    break;
                case Mode.Ride:
                {
                    float c = Mathf.Clamp(carve, -1f, 1f), m = Mathf.Clamp01(pace);
                    h.Want(idRideIdle, 1f - m);
                    h.Want(idRide, m * (1f - Mathf.Abs(c)));
                    h.Want(c > 0f ? idToe : idHeel, m * Mathf.Abs(c));
                    break;
                }
                case Mode.Ski:
                    h.Want(idSkiIdle, 1f - Mathf.Clamp01(pace));
                    h.Want(idSkiRide, Mathf.Clamp01(pace));
                    break;
            }
            if (!snapped || dt <= 0f) { h.Snap(); snapped = true; }
            h.Evaluate(dt * clipRate, 7f);
            if (mode == Mode.Sit) Sit();
            if ((mode == Mode.Ride || mode == Mode.Ski) && crouch > 0f) Crouch();
            var d = new HeroRig.Drive { feet = false, board = null };
            if (lookWeight > 0f)
            {
                d.look = lookAt - h.Head.position; d.lookWeight = Mathf.Clamp01(lookWeight);
                if (turnChest > 0f) Chest(d.look, turnChest * d.lookWeight);
            }
            h.Layer(d, Lod == 0 ? dt : 0f);
            if (mode == Mode.Sit) SitLegs();
            if (mode == Mode.Walk && Lod == 0 && dt > 0f) LockFeet(); else locked[0] = locked[1] = false;
            Hands();
            Gear();
            Wind(dt);
        }

        /// <summary>The weather's wind in the jacket and trousers (ClothWind), up close only (further off the plain body
        /// shows, which has none).</summary>
        void Wind(float dt)
        {
            if (Lod != 0 || dt <= 0f || BodySkin == null) return;
            var cut = PeopleKit.CutOf(Look);
            if (cut == null || !cut.LegsWind.Ok) return;
            Vector3 back = Model.rotation * Vector3.forward, right = Model.rotation * Vector3.left;   // (the model faces -z)
            air.Step(dt, Windless ? Vector3.zero : ClothWind.WeatherAt(transform.position, 1f - Mathf.Clamp01(Shelter)), back, right);
            air.Apply(BodySkin, cut.BodyWind);
            // (sitting, the trousers lie on the seat: wind in them billowed them down through it, a bench's or a stool's)
            if (mode == Mode.Sit) cut.LegsWind.Still(LegsSkin); else air.Apply(LegsSkin, cut.LegsWind);
        }

        // a foot on the ground stays where it landed (the clip's own drift and the turns would slide it)
        readonly bool[] locked = new bool[2];
        readonly Vector3[] lockAt = new Vector3[2];
        void LockFeet()
        {
            var h = Rig;
            float ground = transform.position.y, s = Scale;
            for (int i = 0; i < 2; i++)
            {
                Vector3 a = h.Foot[i].position;
                bool contact = a.y - ground < 0.1f * s;
                if (!contact) { locked[i] = false; continue; }
                if (!locked[i]) { locked[i] = true; lockAt[i] = a; continue; }
                Vector3 want = new Vector3(lockAt[i].x, a.y, lockAt[i].z);
                if ((want - a).sqrMagnitude > 0.1f * 0.1f) { lockAt[i] = a; continue; }   // (too far: a sharp turn, a jump: let it go)
                var rot = h.Foot[i].rotation;
                Limb(h.UpperLeg[i], h.LowerLeg[i], h.Foot[i], want, h.LowerLeg[i].position + SF * 0.3f);
                h.Foot[i].rotation = rot;
            }
        }

        /// <summary>The shoulders turn part of the way to a look (the head does the rest).</summary>
        void Chest(Vector3 look, float w)
        {
            var up = transform.up;
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, up), l = Vector3.ProjectOnPlane(look, up);
            if (l.sqrMagnitude < 1e-4f) return;
            float ang = Mathf.Clamp(Vector3.SignedAngle(f, l, up), -75f, 75f) * w;
            Turnby(Rig.Spine, up, ang * 0.3f); Turnby(Rig.Chest, up, ang * 0.3f); Turnby(Rig.UpperChest, up, ang * 0.25f);
        }
        static void Turnby(Transform t, Vector3 axis, float deg) { if (t != null) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation; }

        // ---- sitting

        Vector3 SF => transform.forward;
        Vector3 SU => transform.up;
        Vector3 SR => transform.right;

        void Sit()
        {
            var h = Rig;
            float s = Scale, w = Mathf.Clamp01(sitW);
            // the hips onto the seat: the pelvis's middle a hand's height over it
            Vector3 want = transform.position + SU * (0.185f * s) + SF * hipsForward;
            Model.position += (want - h.Hips.position) * w;
            // the pelvis rolls back, the lower back takes some of it up again: the back ends at `lean`
            const float tilt = 14f;
            Turnby(h.Hips, SR, -tilt * w);
            float fix = (tilt - lean) * w;
            Turnby(h.Spine, SR, fix * 0.55f);
            Turnby(h.Chest, SR, fix * 0.45f);
        }

        void SitLegs()
        {
            var h = Rig;
            float s = Scale, w = Mathf.Clamp01(sitW);
            for (int i = 0; i < 2; i++)
            {
                legClip[i * 3] = h.UpperLeg[i].rotation; legClip[i * 3 + 1] = h.LowerLeg[i].rotation; legClip[i * 3 + 2] = h.Foot[i].rotation;
                float side = i == 0 ? -1f : 1f;   // (the left leg is on the person's -x)
                Vector3 J = h.UpperLeg[i].position;
                // thighs along the seat, a little apart, falling a few degrees to the knee
                Vector3 thigh = (SF * 0.99f - SU * 0.04f + SR * (side * 0.09f * kneesApart + kneesShift)).normalized;
                Vector3 K = J + thigh * (PeopleKit.ThighLen * s);
                Aim(h.UpperLeg[i], h.LowerLeg[i].position, K);
                float shin = PeopleKit.ShinLen * s;
                if (feetDown)
                {
                    // feet flat on the floor, ahead of the knees by feetForward
                    Vector3 flat = Vector3.ProjectOnPlane(SF, Vector3.up).normalized;
                    Vector3 A = new Vector3(K.x, footY + PeopleKit.AnkleUp * s, K.z) + flat * (0.05f + feetForward) + SR * side * 0.03f;
                    if ((A - K).magnitude < shin || feetForward > 0f) Limb(h.UpperLeg[i], h.LowerLeg[i], h.Foot[i], A, K + SF * 0.2f + SU * 0.4f);
                    else Aim(h.LowerLeg[i], h.Foot[i].position, A);
                    h.Foot[i].rotation = Quaternion.LookRotation(flat, Vector3.up) * footRest[i];
                }
                else
                {
                    // hanging: the shins down and a touch forward, swinging a little, the toes dropping; a free foot
                    // beside a hanging board drawn back off it
                    float sw = 0.27f + swing * (i == 0 ? 1f : -0.8f) - (boardFoot >= 0 && boardFoot != i ? 0.18f : 0f);
                    Vector3 A = K + (-SU * Mathf.Cos(sw) + SF * Mathf.Sin(sw)) * shin;
                    Aim(h.LowerLeg[i], h.Foot[i].position, A);
                    if (boardFoot == i)
                    {
                        // strapped in across the board, which hangs ahead: the foot turned in to it
                        BoardOnFoot(i, out Vector3 nose, out Vector3 deck);
                        Vector3 flatNose = Vector3.ProjectOnPlane(nose, SU).normalized;
                        Vector3 toes = Vector3.ProjectOnPlane(Quaternion.AngleAxis(-side * 75f, SU) * flatNose, deck).normalized;
                        h.Foot[i].rotation = Quaternion.LookRotation(toes, deck) * footRest[i];
                    }
                    else h.Foot[i].rotation = transform.rotation * Quaternion.AngleAxis(toesDown, Vector3.right) * footRest[i];
                }
                if (w < 1f)
                {
                    h.UpperLeg[i].rotation = Quaternion.Slerp(legClip[i * 3], h.UpperLeg[i].rotation, w);
                    h.LowerLeg[i].rotation = Quaternion.Slerp(legClip[i * 3 + 1], h.LowerLeg[i].rotation, w);
                    h.Foot[i].rotation = Quaternion.Slerp(legClip[i * 3 + 2], h.Foot[i].rotation, w);
                }
            }
        }

        /// <summary>Down in the stance, the feet kept where they stand, the chest over the knees.</summary>
        void Crouch()
        {
            var h = Rig;
            float k = Mathf.Clamp01(crouch);
            Vector3 a0 = h.Foot[0].position, a1 = h.Foot[1].position;
            Quaternion r0 = h.Foot[0].rotation, r1 = h.Foot[1].rotation;
            h.Hips.position -= SU * (0.3f * k * Scale);
            Turnby(h.Spine, SR, 22f * k); Turnby(h.Chest, SR, 14f * k);
            for (int i = 0; i < 2; i++)
            {
                Vector3 a = i == 0 ? a0 : a1;
                Limb(h.UpperLeg[i], h.LowerLeg[i], h.Foot[i], a, h.LowerLeg[i].position + SF * 0.4f + SU * 0.1f);
                h.Foot[i].rotation = i == 0 ? r0 : r1;
            }
        }

        /// <summary>The board hanging from a foot: its nose (ahead, turned out to that foot's side, pitched down) and its deck's up.</summary>
        void BoardOnFoot(int foot, out Vector3 nose, out Vector3 deck)
        {
            Vector3 flat = Quaternion.AngleAxis(boardTurn, SU) * SF;
            float p = boardPitch * Mathf.Deg2Rad;
            nose = flat * Mathf.Cos(p) - SU * Mathf.Sin(p);
            deck = SU * Mathf.Cos(p) + flat * Mathf.Sin(p);
        }

        // ---- hands

        void Hands()
        {
            var h = Rig;
            float sc = Scale;
            if (boardInHand >= 0)
            {
                // a board under the arm: the hand low at the hip, holding its edge
                float side = boardInHand == 0 ? -1f : 1f;
                handAt[boardInHand] = transform.position + SU * (0.78f * sc) + SR * (side * 0.27f * sc) + SF * (0.04f * sc);
                handW[boardInHand] = 1f;
            }
            if (skisOnShoulder)
            {
                // skis on the right shoulder: the right hand out in front holding them; poles in the left, as a stick
                handAt[1] = transform.position + SU * (1.36f * sc) + SR * (0.14f * sc) + SF * (0.3f * sc);
                handW[1] = 1f;
            }
            for (int i = 0; i < 2; i++)
            {
                float w = handW[i];
                Vector3 target = handAt[i];
                if (phoneUp > 0f)
                {
                    // the phone held up at arm's length in front of the eyes, the left hand steadying it
                    Vector3 f = Vector3.ProjectOnPlane(Facing, SU).normalized;
                    Vector3 eye = h.Head.position + SU * 0.02f;
                    Vector3 at = i == 1 ? eye + f * (0.38f * Scale) - SU * 0.03f + SR * 0.035f : eye + f * (0.36f * Scale) - SU * 0.07f - SR * 0.05f;
                    target = w > 0f ? Vector3.Lerp(target, at, phoneUp) : at;
                    w = Mathf.Max(w, phoneUp);
                }
                if (pointW > 0f && i == pointHand)
                {
                    // the arm out at it, a little bent
                    Vector3 sh = h.UpperArm[i].position, dir = (pointAt - sh).normalized;
                    target = Vector3.Lerp(w > 0f ? target : h.Hand[i].position, sh + dir * (0.5f * Scale), pointW);
                    w = Mathf.Max(w, pointW);
                }
                if (w <= 0.001f) continue;
                Vector3 hand = Vector3.Lerp(h.Hand[i].position, target, Mathf.Clamp01(w));
                float sideSign = i == 0 ? -1f : 1f;
                Vector3 hint = h.UpperArm[i].position - SU * 0.3f + SR * sideSign * (mode == Mode.Sit ? 0.03f : 0.28f) - SF * 0.12f;
                Limb(h.UpperArm[i], h.LowerArm[i], h.Hand[i], hand, hint);
                h.Hand[i].localRotation = handBind[i];
                if (h.Twist[i] != null) h.Twist[i].localRotation = twistBind[i];
                if (pointW > 0f && i == pointHand)
                {
                    var idx = h.Bone(i == 0 ? "LeftIndexProximal" : "RightIndexProximal");
                    if (idx != null) Aim(h.Hand[i], idx.position, h.Hand[i].position + (pointAt - h.Hand[i].position).normalized * 0.2f);
                }
            }
        }

        // ---- the board, skis, poles and phone

        void Gear()
        {
            var h = Rig;
            if (Board != null)
            {
                bool on = Lod < 2 && held != Held.None;
                if (Board.enabled != on) Board.enabled = on;
                if (held == Held.Underfoot)
                {
                    // as the hero's file lays it out: under the feet, the Root bone on its deck
                    Board.transform.localPosition = boardPos0; Board.transform.localRotation = boardRot0; Board.transform.localScale = boardScale0;
                }
                else if (held == Held.Placed)
                {
                    if (boardInHand >= 0)
                    {
                        // under the arm: on edge along the body, the nose ahead and a little up, the hand on its top edge
                        float side = boardInHand == 0 ? -1f : 1f;
                        Vector3 nose = (SF * 0.97f + SU * 0.25f).normalized, deck = SR * side;
                        Vector3 origin = h.Hand[boardInHand].position - SU * (0.13f * Scale) + SR * (side * 0.035f) - nose * 0.05f;
                        boardFrame = Matrix4x4.TRS(origin, Quaternion.LookRotation(Vector3.Cross(nose, deck), deck), Vector3.one);
                    }
                    else if (boardFoot >= 0)
                    {
                        // hanging from the foot: the front binding (a quarter metre toward the nose) under the boot
                        BoardOnFoot(boardFoot, out Vector3 nose, out Vector3 deck);
                        Vector3 origin = h.Foot[boardFoot].position - deck * (PeopleKit.AnkleUp * Scale) - nose * 0.25f;
                        boardFrame = Matrix4x4.TRS(origin, Quaternion.LookRotation(Vector3.Cross(nose, deck), deck), Vector3.one);
                    }
                    var m = boardFrame * PeopleKit.BoardLocal;
                    Board.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                }
            }
            for (int i = 0; i < 2; i++)
            {
                if (Skis[i] != null)
                {
                    bool on = (skisOn || skisOnShoulder) && Lod < 2;
                    if (Skis[i].gameObject.activeSelf != on) Skis[i].gameObject.SetActive(on);
                    if (on && skisOnShoulder)
                    {
                        // the pair on the shoulder, bases together: tips ahead and down, tails up behind
                        Vector3 along = (SF * 0.93f - SU * 0.36f).normalized;
                        Vector3 sh = h.UpperArm[1].position + SU * (0.07f * Scale) - SR * 0.02f;
                        Vector3 mid = sh - along * 0.25f + SR * (i == 0 ? -0.012f : 0.012f);
                        Vector3 up = (i == 0 ? -SR : SR);
                        var m = Matrix4x4.TRS(mid, Quaternion.LookRotation(along, up) * Turn, Vector3.one) * PeopleKit.SkiLocal;
                        Skis[i].transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                        Skis[i].transform.localScale = PeopleKit.SkiLocal.lossyScale;
                    }
                    else if (on)
                    {
                        // each ski under its boot, flat to the sole: the ski's middle 9 cm ahead of the ankle
                        Quaternion sole = h.Foot[i].rotation * Quaternion.Inverse(footRest[i]);   // z the toes' way, y up out of the sole
                        Vector3 under = h.Foot[i].position - sole * Vector3.up * (PeopleKit.AnkleUp * Scale) + sole * Vector3.forward * (0.09f * Scale);
                        var m = Matrix4x4.TRS(under, sole * Turn, Vector3.one) * PeopleKit.SkiLocal;
                        Skis[i].transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                        Skis[i].transform.localScale = PeopleKit.SkiLocal.lossyScale;
                    }
                }
                if (Poles[i] != null)
                {
                    bool on = (polesOn || skisOnShoulder) && Lod < 2;
                    if (Poles[i].gameObject.activeSelf != on) Poles[i].gameObject.SetActive(on);
                    if (on)
                    {
                        // a pole from the fist: the grip in the hand, the shaft pointing polesDir (mirrored for the left)
                        int hand = skisOnShoulder ? 0 : polesInOneHand ? 1 : i;
                        var fingers = h.Bone(hand == 0 ? "LeftIndexProximal" : "RightIndexProximal");
                        Vector3 fist = fingers != null ? h.Hand[hand].position + (fingers.position - h.Hand[hand].position).normalized * (0.07f * Scale) : h.Hand[hand].position;
                        bool one = polesInOneHand || skisOnShoulder;
                        Vector3 grip = fist + SR * (one ? (i == 0 ? -0.02f : 0.02f) : 0f);
                        Vector3 down = polesDir.sqrMagnitude > 1e-4f ? polesDir.normalized : Vector3.down;
                        if (!one && i == 0) down = Vector3.Reflect(down, SR);
                        Vector3 across = Vector3.ProjectOnPlane(SF, down);
                        if (across.sqrMagnitude < 1e-4f) across = Vector3.ProjectOnPlane(SR, down);
                        var m = Matrix4x4.TRS(grip, Quaternion.LookRotation(across.normalized, -down), Vector3.one) * PeopleKit.PoleLocal;
                        Poles[i].transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                        Poles[i].transform.localScale = PeopleKit.PoleLocal.lossyScale;
                    }
                }
            }
            if (Phone != null)
            {
                bool on = phoneUp > 0.5f && Lod < 2;
                if (Phone.gameObject.activeSelf != on) Phone.gameObject.SetActive(on);
                if (on)
                {
                    Vector3 f = Vector3.ProjectOnPlane(Facing, SU).normalized;
                    Vector3 at = Vector3.Lerp(h.Hand[1].position, h.Hand[0].position, 0.4f) + f * 0.035f + SU * 0.04f;
                    Phone.transform.SetPositionAndRotation(at, Quaternion.LookRotation(f, SU));
                }
            }
        }

        // ---------------------------------------------------------------- IK

        /// <summary>Turn a bone so a point it carries (its child's joint) heads for target.</summary>
        public static void Aim(Transform bone, Vector3 carried, Vector3 target)
        {
            Vector3 a = carried - bone.position, b = target - bone.position;
            if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(a, b) * bone.rotation;
        }

        /// <summary>Two-bone IK: the end reaches the target; the middle joint bends toward where the hint is.</summary>
        public static void Limb(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint)
        {
            Vector3 hp = upper.position, k = lower.position, e = end.position;
            float a = (k - hp).magnitude, b = (e - k).magnitude;
            Vector3 dd = target - hp;
            float c = Mathf.Clamp(dd.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-4f);
            Vector3 dn = dd.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(hint - hp, dn);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(k - hp, dn);
            pole.Normalize();
            float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            Vector3 knee = hp + dn * (cosA * a) + pole * (Mathf.Sqrt(1f - cosA * cosA) * a);
            upper.rotation = Quaternion.FromToRotation(k - hp, knee - hp) * upper.rotation;
            k = lower.position; e = end.position;
            lower.rotation = Quaternion.FromToRotation(e - k, hp + dn * c - k) * lower.rotation;
        }

        void OnDestroy()
        {
            void Kill(Object o) { if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); } }
            Kill(jacketM); Kill(pantsM); Kill(gearM); Kill(boardM); Kill(pal);
            PeopleKit.Let(Look);
        }
    }
}
