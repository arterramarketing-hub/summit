using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The start gate, where every run begins (SummitGame.BuildStartGate; the camp's DROP IN is under it): the
    /// mountain's own landmark, built like a resort's start arch rather than a race banner. Two squared timber posts
    /// in steel shoes, a header beam on them with knee braces, and under it a board of dark planks carved with MT.
    /// VERTIGO routed into it in a slab serif and painted cream, as a ski area's sign is made (SignArt.cs, lit by
    /// Resources/GateBoard.shader), ELEV. 10,000 FT under it between two little routed mountains, snow caught in the
    /// cuts. Snow lies along the beam and the board's top edge and is drifted round the posts' feet,
    /// and two lamps hang off the beam over the board, lighting it at night.
    /// The posts are solid, as a tree is (ride into one and you fall); everything above them is out of reach, so
    /// the posts are its only colliders. The board faces up the hill, at the camp and at the rider waiting below it.
    /// </summary>
    public static class StartGate
    {
        /// <summary>Where it stands down the in-run: 36 m above the lip.</summary>
        public static float Z => Slope.GateZ;
        /// <summary>The posts' centres either side of the run's centre line, and their half width (timber with its steel shoe).</summary>
        public const float PostX = 4.3f, PostHalf = 0.28f;
        // the timber's section, the beam's height, and the board (4:1, its foot well above a rider's head)
        const float Post = 0.42f, Beam = 6.1f, BoardW = 7f, BoardH = 1.75f, BoardFoot = 3.7f;

        // the lettering is built once, off the main thread (as the start page builds its title); the board shows bare
        // timber for the moment it takes, then the letters come up on it
        static System.Threading.Tasks.Task<Color32[]> job;
        static Texture2D letters;
        static Material board, bulb;

        /// <summary>
        /// Builds the gate under `root` across the run at the centre line `cx`. mat(colour, smoothness) gives the lit
        /// materials (the game's own, shared with the rest of the start).
        /// </summary>
        public static Transform Build(Transform root, float cx, System.Func<Color, float, Material> mat)
        {
            float gz = Z, gy = Slope.DrawnHeight(cx, gz);
            var gate = new GameObject("Gate").transform;
            gate.SetParent(root, false);
            gate.position = new Vector3(cx, gy, gz);
            var timber = mat(new Color(0.33f, 0.22f, 0.14f), 0.15f);
            var dark = mat(new Color(0.22f, 0.15f, 0.10f), 0.15f);
            var steel = mat(new Color(0.16f, 0.17f, 0.19f), 0.55f);
            var snow = mat(new Color(0.94f, 0.96f, 0.99f), 0.35f);
            var shade = mat(new Color(0.10f, 0.20f, 0.16f), 0.5f);   // the lamps' enamelled shades, a dark alpine green

            Transform Part(PrimitiveType t, Vector3 at, Vector3 scale, Material m, float rollZ = 0f, bool shadows = true)
            {
                var go = GameObject.CreatePrimitive(t);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(gate, false);
                go.transform.localPosition = at;
                go.transform.localRotation = Quaternion.Euler(0f, 0f, rollZ);
                go.transform.localScale = scale;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = m;
                if (!shadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go.transform;
            }

            // the snow falls away down the in-run, so each post's foot is set into it where that post stands
            float span = 2f * PostX + 1.3f;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * PostX;
                // the post, run half a metre into the snow, up to the beam
                Part(PrimitiveType.Cube, new Vector3(x, (Beam - 0.5f) * 0.5f, 0f), new Vector3(Post, Beam + 0.5f, Post), timber);
                // its steel shoe, bolted through, and a drift heaped against it
                Part(PrimitiveType.Cube, new Vector3(x, 0.25f, 0f), new Vector3(PostHalf * 2f, 0.9f, PostHalf * 2f), steel);
                Part(PrimitiveType.Cube, new Vector3(x, 0.71f, 0f), new Vector3(PostHalf * 2f + 0.02f, 0.03f, PostHalf * 2f + 0.02f), snow, 0f, false);
                Part(PrimitiveType.Sphere, new Vector3(x - s * 0.15f, -0.05f, -0.25f), new Vector3(1.5f, 0.55f, 1.2f), snow);
                // a knee brace from the post up into the beam, clear of the board's top corner
                Vector3 a = new Vector3(x - s * Post * 0.5f, Beam - 0.95f, 0f), b = new Vector3(x - s * 1.1f, Beam, 0f);
                Part(PrimitiveType.Cube, (a + b) * 0.5f, new Vector3(0.2f, Vector3.Distance(a, b) + 0.15f, 0.2f), timber, s * Mathf.Atan2(Mathf.Abs(b.x - a.x), b.y - a.y) * Mathf.Rad2Deg);
                // the board's hanger, a steel rod from the beam
                Part(PrimitiveType.Cylinder, new Vector3(s * 2.6f, (BoardFoot + BoardH + Beam) * 0.5f, 0f), new Vector3(0.05f, (Beam - BoardFoot - BoardH) * 0.5f + 0.05f, 0.05f), steel);
                // a lamp on a gooseneck off the beam's face, its shade over the board
                float lx = s * 2f;
                Part(PrimitiveType.Cylinder, new Vector3(lx, Beam + 0.25f, -0.55f), new Vector3(0.06f, 0.4f, 0.06f), steel, 0f).localRotation = Quaternion.Euler(90f, 0f, 0f);
                Part(PrimitiveType.Sphere, new Vector3(lx, Beam + 0.18f, -0.98f), new Vector3(0.5f, 0.22f, 0.5f), shade);
                Part(PrimitiveType.Sphere, new Vector3(lx, Beam + 0.08f, -0.98f), new Vector3(0.2f, 0.12f, 0.2f), Bulb(), 0f, false);
                Part(PrimitiveType.Sphere, new Vector3(lx, Beam + 0.3f, -0.98f), new Vector3(0.36f, 0.08f, 0.36f), snow, 0f, false);
            }
            // the header beam, its ends standing out past the posts, and the snow lying along it
            Part(PrimitiveType.Cube, new Vector3(0f, Beam + 0.21f, 0f), new Vector3(span, 0.42f, 0.5f), dark);
            Part(PrimitiveType.Capsule, new Vector3(0f, Beam + 0.5f, 0f), new Vector3(0.28f, span * 0.5f + 0.05f, 0.56f), snow, 90f);
            // the board: planks on a backing, the carved face toward the hill, and a line of snow on its top edge
            float by = BoardFoot + BoardH * 0.5f;
            Part(PrimitiveType.Cube, new Vector3(0f, by, 0.09f), new Vector3(BoardW + 0.14f, BoardH + 0.14f, 0.18f), dark);
            var face = Part(PrimitiveType.Quad, new Vector3(0f, by, -0.006f), new Vector3(BoardW, BoardH, 1f), Board(), 0f, false);
            face.name = "GateBoard";
            Part(PrimitiveType.Capsule, new Vector3(0f, BoardFoot + BoardH + 0.1f, 0.06f), new Vector3(0.12f, BoardW * 0.5f + 0.04f, 0.24f), snow, 90f);

            // the posts are what you can hit: solid, a crash like a tree
            for (int s = -1; s <= 1; s += 2)
            {
                var hit = new GameObject("GatePost");
                hit.transform.SetParent(gate, false);
                hit.transform.localPosition = new Vector3(s * PostX, Beam * 0.5f, 0f);
                hit.AddComponent<BoxCollider>().size = new Vector3(PostHalf * 2f, Beam + 1f, PostHalf * 2f);
                hit.AddComponent<Obstacle>().label = "Gate";
            }

            // and its lamps: a light shining down on the board from in front, on at dusk
            var lamp = new GameObject("GateLamp").AddComponent<Light>();
            lamp.transform.SetParent(gate, false);
            // (out in front and aimed nearly straight down: its cone takes in the board and the snow where you wait,
            // not the run below the gate)
            lamp.transform.localPosition = new Vector3(0f, Beam + 0.3f, -1.8f);
            lamp.transform.LookAt(gate.TransformPoint(new Vector3(0f, 0f, -1f)));
            lamp.type = LightType.Spot; lamp.spotAngle = 95f; lamp.innerSpotAngle = 50f; lamp.range = 11f;
            lamp.color = new Color(1f, 0.78f, 0.52f); lamp.shadows = LightShadows.None;
            lamp.renderMode = LightRenderMode.ForcePixel;   // the board is one quad: lit per vertex it would get nothing
            gate.gameObject.AddComponent<GateLamps>().lamp = lamp;
            return gate;
        }

        static Material Board()
        {
            if (board != null) return board;
            var sh = Resources.Load<Shader>("GateBoard");
            board = new Material(sh != null ? sh : Mats.LitShader) { name = "GateBoard" };
            board.SetVector("_Size", new Vector4(BoardW, BoardH, 0f, 0f));
            if (letters != null) board.SetTexture("_MainTex", letters);
            else if (job == null) job = System.Threading.Tasks.Task.Run(() => SignArt.Build());   // (routed resort lettering, SignArt.cs)
            return board;
        }

        static Material Bulb()
        {
            if (bulb != null) return bulb;
            var sh = Resources.Load<Shader>("StudioBackdrop");   // unlit: the bulb is its own light
            bulb = new Material(sh != null ? sh : Mats.LitShader) { name = "GateBulb" };
            return bulb;
        }

        /// <summary>The lettering, once it is built (each frame until then; the test tools call it to wait for it).</summary>
        public static bool Letters(bool wait = false)
        {
            if (letters != null) return true;
            if (job == null) return false;
            if (wait) job.Wait();
            if (!job.IsCompleted) return false;
            if (job.IsFaulted) { Debug.LogException(job.Exception); job = null; return false; }
            letters = SignArt.ToTexture(job.Result, "GateLetters");
            job = null;
            if (board != null) board.SetTexture("_MainTex", letters);
            return true;
        }

        /// <summary>The lamps by the time of day: the bulbs glow warm, and the light comes on as it gets dark.</summary>
        public static void Lamps(Light lamp, float night)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.7f, night));
            if (lamp != null) { lamp.intensity = 9f * k; lamp.enabled = k > 0.01f; }
            if (bulb != null) bulb.SetColor("_Tint", Color.Lerp(new Color(0.85f, 0.8f, 0.68f), new Color(2.2f, 1.7f, 1.1f), k));
        }

        // ---------------------------------------------------------------- the site round it

        /// <summary>
        /// The start, rebuilt for every new mountain (SummitGame.RebuildGate): this gate, flags along the lip you jump
        /// from and its wooden kicker edge, the finish arch at the foot of the mountain, the safety signs before the
        /// lip and the caution tape (StartTape; clack plays as it tears). Its materials are made on sh, once each,
        /// into mats, which the caller keeps across rebuilds, so they neither leak nor pile up in DayNight's list of
        /// reflective signs. Returns the root.
        /// </summary>
        public static Transform BuildSite(Shader sh, System.Collections.Generic.Dictionary<string, Material> mats, Rider rider, System.Action clack)
        {
            Material M(Color c)
            {
                string k = "c" + c;
                if (!mats.TryGetValue(k, out var m)) { m = Mats.Lit(c, 0.3f, sh); mats[k] = m; }
                return m;
            }
            var red = M(new Color(0.85f, 0.12f, 0.10f)); var white = M(new Color(0.95f, 0.95f, 0.97f)); var wood = M(new Color(0.45f, 0.30f, 0.18f));
            Material GateSign(string path)
            {
                if (!mats.TryGetValue(path, out var m)) { m = CourseDressing.Sign(sh, path); mats[path] = m; }
                return m;
            }
            var root = new GameObject("StartGate").transform;
            float cx = Slope.Centre(Slope.PadLip);
            // the gate, where every run starts
            Build(root, cx, (c, smooth) =>
            {
                string k = "c" + c + smooth;
                if (!mats.TryGetValue(k, out var m)) { m = Mats.Lit(c, smooth, sh); mats[k] = m; }
                return m;
            });
            // flags lining the lip and a wooden kicker edge
            for (int i = 0; i < 6; i++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float z = Slope.PadLip - 2f - i * 6f, x = cx + sgn * (Slope.PadHalf - 1f);
                    float y = Slope.DrawnHeight(x, z);
                    var flag = Holder("LipFlag", root, new Vector3(x, y, z));
                    Block(PrimitiveType.Cylinder, flag, new Vector3(x, y + 0.9f, z), new Vector3(0.06f, 0.9f, 0.06f), white);
                    Block(PrimitiveType.Cube, flag, new Vector3(x - sgn * 0.3f, y + 1.55f, z), new Vector3(0.6f, 0.4f, 0.03f), (i % 2 == 0) ? red : white);
                    Breakable.Flag(flag, 1.8f);
                }
            // the finish line at the base of the mountain: a blue arch across the run
            {
                float fz = Slope.FinishZ + 30f, fx = Slope.Centre(fz), fy = Slope.DrawnHeight(fx, fz);
                var blue = M(new Color(0.08f, 0.30f, 0.70f));
                var arch = Holder("FinishArch", root, new Vector3(fx, fy, fz));
                var archBrk = Breakable.Sign(arch);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    archBrk.Post(Block(PrimitiveType.Cube, arch, new Vector3(fx + sgn * 9f, fy + 2.6f, fz), new Vector3(0.45f, 5.2f, 0.45f), blue));
                var archFrame = Block(PrimitiveType.Cube, arch, new Vector3(fx, fy + 5.0f, fz + 0.06f), new Vector3(18.6f, 1.6f, 0.08f), blue);
                var fb = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(fb.GetComponent<Collider>());
                fb.transform.SetParent(arch, false);
                fb.transform.SetPositionAndRotation(new Vector3(fx, fy + 5.0f, fz - 0.01f), Quaternion.identity);
                fb.transform.localScale = new Vector3(18.2f, 1.4f, 1f);
                fb.GetComponent<MeshRenderer>().sharedMaterial = GateSign("Signs/BannerFinish");
                archBrk.Board(archFrame, fb.transform);
                archBrk.Arm();
            }
            // the last chance: safety signs either side of the in-run before the lip, and caution tape strung
            // chest-high between the last pair and on across the mountain, tree to tree (ride through it and it tears)
            var post = M(new Color(0.55f, 0.57f, 0.6f));
            WarningSign(root, cx, Slope.PadLip - 22f, -1f, GateSign("Signs/SignTurnAround"), post);
            WarningSign(root, cx, Slope.PadLip - 22f, 1f, GateSign("Signs/SignRemote"), post);
            var wl = WarningSign(root, cx, Slope.PadLip - 11f, -1f, GateSign("Signs/SignWarning"), post);
            var wr = WarningSign(root, cx, Slope.PadLip - 11f, 1f, GateSign("Signs/SignDangerStart"), post);
            StartTape.Build(root, wl, wr, SignPostX, rider, GateSign("Signs/Tape"), clack);
            float ly = Slope.DrawnHeight(cx, Slope.PadLip - 0.3f);
            Block(PrimitiveType.Cube, root, new Vector3(cx, ly - 0.15f, Slope.PadLip - 0.2f), new Vector3(2f * Slope.PadHalf - 4f, 0.35f, 0.3f), wood);
            return root;
        }

        const float SignW = 3.6f, SignPostX = SignW * 0.4f;

        /// <summary>A 3:2 safety sign on two posts beside the in-run (side -1 / 1), turned in toward the rider coming down.</summary>
        static Transform WarningSign(Transform root, float cx, float z, float side, Material face, Material post)
        {
            const float W = SignW, H = W / 1.5f, lift = 1.3f;
            float x = cx + side * 6.5f;
            var t = new GameObject("WarningSign").transform;
            t.SetParent(root, false);
            t.SetPositionAndRotation(new Vector3(x, Slope.DrawnHeight(x, z), z), Quaternion.Euler(0f, side * 28f, 0f));
            Transform Local(PrimitiveType type, Vector3 at, Vector3 scale, Material m)
            {
                var go = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(t, false);
                go.transform.localPosition = at; go.transform.localScale = scale;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                if (type == PrimitiveType.Quad) go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go.transform;
            }
            var brk = Breakable.Sign(t);
            // posts run a little into the snow, so they stand on the bank whatever its tilt
            float top = lift + H;
            for (int s = -1; s <= 1; s += 2)
                brk.Post(Local(PrimitiveType.Cylinder, new Vector3(s * SignPostX, top * 0.5f - 0.3f, 0.08f), new Vector3(0.1f, (top + 0.6f) * 0.5f, 0.1f), post));
            var back = Local(PrimitiveType.Cube, new Vector3(0f, lift + H * 0.5f, 0.03f), new Vector3(W + 0.1f, H + 0.1f, 0.04f), post);
            brk.Board(back, Local(PrimitiveType.Quad, new Vector3(0f, lift + H * 0.5f, -0.001f), new Vector3(W, H, 1f), face));
            brk.Arm();
            return t;
        }

        static Transform Block(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        /// <summary>An empty holder at a spot on the snow, for something that can be broken or knocked down (Breakable).</summary>
        static Transform Holder(string name, Transform parent, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = at;
            return t;
        }
    }

    /// <summary>Keeps the start gate's lamps with the time of day, and puts its lettering up when it is ready.</summary>
    public class GateLamps : MonoBehaviour
    {
        public Light lamp;
        void Update() => Apply();
        public void Apply()
        {
            StartGate.Letters();
            StartGate.Lamps(lamp, Shader.GetGlobalFloat("_SummitNight"));
        }
    }
}
