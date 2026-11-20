using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The start gate, where every run begins (SummitGame.BuildStartGate; the camp's DROP IN is under it): the
    /// mountain's own landmark, built like a resort's start arch rather than a race banner. Two squared timber posts
    /// in steel shoes, a header beam on them with knee braces, and under it a board of dark planks carved with MT.
    /// VERTIGO in the title's lettering (TitleArt.BuildLine, lit by Resources/GateBoard.shader): ice-blue letters with
    /// snow loaded on them. Snow lies along the beam and the board's top edge and is drifted round the posts' feet,
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
        const int TexW = 1024, TexH = 256;
        const float CapFrac = 0.45f, BaseFrac = 0.25f;   // the letters' caps and foot on the board

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
            board = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Lit")) { name = "GateBoard" };
            var st = TitleArt.Styles[0];   // CORNICE: the title's heavy slanted ice with the snow loaded on it
            board.SetVector("_Word", new Vector4(BaseFrac, BaseFrac + CapFrac, 0f, 0f));
            board.SetVector("_Size", new Vector4(BoardW, BoardH, 0f, 0f));
            board.SetFloat("_Spread", TitleArt.Spread);
            board.SetColor("_Top", Color.Lerp(st.top, Color.white, 0.15f)); board.SetColor("_Bottom", st.bottom); board.SetColor("_SnowCol", st.snowCol);
            if (letters != null) board.SetTexture("_MainTex", letters);
            else if (job == null) job = System.Threading.Tasks.Task.Run(() => TitleArt.BuildLine(st, "MT. VERTIGO", TexW, TexH, CapFrac, BaseFrac));
            return board;
        }

        static Material Bulb()
        {
            if (bulb != null) return bulb;
            var sh = Resources.Load<Shader>("StudioBackdrop");   // unlit: the bulb is its own light
            bulb = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Lit")) { name = "GateBulb" };
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
            letters = TitleArt.ToTexture(job.Result, TexW, TexH, "GateLetters");
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
