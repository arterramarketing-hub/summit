using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The rider on foot in the summit camp (no board: it waits at the gate), and the outfit colours. Kept apart from RiderBody's riding
    /// code: RiderBody only calls WalkFrame and WalkMuscles while Walking is set.
    /// </summary>
    public partial class RiderBody
    {
        /// <summary>On foot in the camp: the board is strapped to the back and the legs walk.</summary>
        public bool Walking { get; private set; }
        /// <summary>0 standing still .. 1 full walking pace (the camp sets it every frame).</summary>
        public float WalkPace;
        /// <summary>0 walking .. 1 running flat out (the camp sets it every frame).</summary>
        public float RunBlend;
        /// <summary>Walk cycles a second (two steps each), from how fast you're going; 0: work it out from the pace.</summary>
        public float Cadence;
        float runAmp, cadence;
        /// <summary>Thrown from on foot (the camp's edge giving way): the velocity the coming crash throws the body with.</summary>
        public void Tumble(Vector3 velocity) { lastVel = velocity; }

        /// <summary>The walk cycle (radians): the left heel strikes at pi/2, the right at 3pi/2 (see RiderRig.PoseWalk).</summary>
        public float WalkPhase => walkPhase;
        /// <summary>How far into a run the legs are (0 walking .. 1 running), eased.</summary>
        public float RunAmount => runAmp;
        float walkPhase, walkAmp;
        /// <summary>A jump or a roll in the camp (0 none, 1 jump, 2 roll), and how far into it (s): the camp sets them.</summary>
        public int CampMove; public float CampMoveT;
        Vector3 boardHomePos; Quaternion boardHomeRot; bool boardHomeSet, tubeBeforeWalk;

        public void SetWalking(bool on)
        {
            if (Walking == on) return;
            if (!boardHomeSet) { boardHomePos = boardRoot.localPosition; boardHomeRot = boardRoot.localRotation; boardHomeSet = true; }
            Walking = on;
            if (on)
            {
                // nobody walks round camp sitting in a tube; and the board stays at the gate: on foot with both
                // hands free, so both arms swing (it is back on the feet for the run)
                tubeBeforeWalk = Tube;
                if (Tube) SetTube(false);
                ShowRide();   // nothing carried: the board (or the skis and poles) waits at the gate
            }
            else
            {
                boardRoot.localPosition = boardHomePos;
                boardRoot.localRotation = boardHomeRot;
                ShowRide();
                walkPhase = 0f; walkAmp = 0f; WalkPace = 0f; RunBlend = 0f; runAmp = 0f;
                if (tubeBeforeWalk) SetTube(true);
            }
        }

        /// <summary>Upright on foot: no edge lean, a bob in step, a slight lean into the walk.</summary>
        void WalkFrame(float dt)
        {
            walkAmp = Mathf.MoveTowards(walkAmp, WalkPace, dt * 4f);
            runAmp = Mathf.MoveTowards(runAmp, RunBlend, dt * 3f);   // eases into and out of the run
            // strides come quicker as you speed up: about one a second walking, nearly three running
            float want = Cadence > 0f ? Cadence : Mathf.Lerp(Mathf.Lerp(0.55f, 1.0f, walkAmp), 1.45f, runAmp);
            cadence = Mathf.MoveTowards(cadence, want, dt * 4f);
            walkPhase += dt * Mathf.PI * 2f * cadence * (walkAmp > 0.02f ? 1f : 0f);
            roll.localRotation = Quaternion.identity; roll.localPosition = Vector3.zero;
            upper.localRotation = Quaternion.identity;
            if (CampMove == 2)
            {
                // a forward roll: the whole body turns head over heels round its middle (about a ball's height up),
                // the tuck itself is the pose
                float k = Mathf.Clamp01((CampMoveT - 0.1f) / 0.56f);
                float ang = 360f * k * k * (3f - 2f * k);
                var pv = new Vector3(0f, 0.45f, 0f);
                var rot = Quaternion.AngleAxis(-ang, Vector3.forward);   // the top tips the way you're going (+X)
                roll.localRotation = rot; roll.localPosition = pv - rot * pv;
            }
            // the humanoid's feet are planted at a board's height: stand them on the snow instead
            // (the rig plants its own feet, see RiderRig.PoseWalk)
            if (poser != null) pivot.position += pivot.up * (Mathf.Abs(Mathf.Sin(walkPhase)) * Mathf.Lerp(0.05f, 0.09f, runAmp) * walkAmp - BoardTop - 0.02f);
        }

        /// <summary>Muscle targets for a walk cycle (over the snowboard pose RiderBody built this frame).</summary>
        void WalkMuscles(float t)
        {
            float a = walkAmp, r = runAmp, s = Mathf.Sin(walkPhase), c = Mathf.Cos(walkPhase);
            // legs swing opposite each other; the leg coming forward bends at the knee (much more running)
            float sw = Mathf.Lerp(0.7f, 1f, r);
            W(mHipL, 0.08f + 0.2f * r + sw * s * a); W(mHipR, 0.08f + 0.2f * r - sw * s * a);
            W(mKneeL, 0.95f - a * (0.15f + Mathf.Lerp(1.2f, 1.8f, r) * Mathf.Max(0f, c)));
            W(mKneeR, 0.95f - a * (0.15f + Mathf.Lerp(1.2f, 1.8f, r) * Mathf.Max(0f, -c)));
            W(mSpreadL, 0.02f); W(mSpreadR, 0.02f);
            W(mSpine, 0.06f + 0.08f * a + 0.25f * r); W(mChest, 0.02f + 0.1f * r);
            W(mTwist, -0.15f * s * a);
            // arms down at the sides, swinging against the legs
            W(mArmL, -0.8f); W(mArmR, -0.8f);
            W(mArmFL, -Mathf.Lerp(0.55f, 0.9f, r) * s * a + 0.05f); W(mArmFR, Mathf.Lerp(0.55f, 0.9f, r) * s * a + 0.05f);
            W(mElbowL, Mathf.Lerp(0.7f, -0.3f, r)); W(mElbowR, Mathf.Lerp(0.7f, -0.3f, r));   // running: both arms bent and pumping
            // standing about: look round the camp
            W(mNeck, 0f);
            W(mHead, (1f - a) * 0.25f * Mathf.Sin(t * 0.45f));
        }

        // ---- outfit colours (bought at the board rack)

        Material boardMat, gearJacket;
        readonly System.Collections.Generic.List<Material> jacketMats = new System.Collections.Generic.List<Material>();
        readonly System.Collections.Generic.List<float> jacketShade = new System.Collections.Generic.List<float>();

        bool ownMats;

        /// <summary>Paint the jacket and the board.</summary>
        public void SetColours(Color jacket, Color board)
        {
            if (!ownMats) OwnMaterials();
            if (boardMat != null) boardMat.SetColor("_BaseColor", board);
            if (gearJacket != null) gearJacket.SetColor("_BaseColor", jacket);
            // each jacket material keeps its own shade (the trim is lighter than the body)
            for (int i = 0; i < jacketMats.Count; i++) jacketMats[i].SetColor("_BaseColor", jacket * (i < jacketShade.Count ? jacketShade[i] : 1f));
        }

        // ---- the closet's outfit (Wardrobe): the jacket's and trousers' colours and patterns, the gear's swatches,
        // the board's colour and finish

        Material pantsMat;                  // the hero's trousers (the previous rider's aren't painted)
        Texture2D gearPalette;              // the hero's gear palette, this rider's own copy
        Color[] palBase, palNow;
        readonly System.Collections.Generic.Dictionary<Material, float> ownSheen = new System.Collections.Generic.Dictionary<Material, float>();
        // where each gear category sits in the palette (Tools/MakeHero.py's PAL): its colour, its trim, an accent
        static readonly int[][] PaletteCells =
        {
            null, null,
            new[] { 1, 11 },        // helmet, its vents
            new[] { 3, 2 },         // the goggles' lens, their frame
            new[] { 4, 5 },         // gloves, cuffs
            new[] { 6 },            // boots
            new[] { 16, 17, 18 },   // the pack, its side panels, its accent
        };

        /// <summary>The GRAPHICS row on the board now (0: none, its plain colour).</summary>
        public int ShownGraphic { get; private set; }

        /// <summary>The parts of the rider the closet can show (ClosetStage clones them for its preview).</summary>
        public Transform Upper => upper;
        public Transform BoardGear => boardRoot;
        /// <summary>Everything drawn of the rider on a run: the body and whatever they ride (board, skis and poles, sled, tube).</summary>
        public Transform Drawn => roll;
        /// <summary>What takes the Invincible rainbow's second colour (PowerAura): the trousers and the board or skis' paint.</summary>
        public bool Lower(Material m) => m != null && (m == pantsMat || m == boardMat || m == boardPaint || m == tubeBand);
        public Transform SkiGear => skiRoot;
        public Transform SledGear => sledRoot;

        /// <summary>Put an outfit on (an item for each Wardrobe category; the ride is left to SetRide).</summary>
        public void Wear(int[] o)
        {
            if (o == null || o.Length < Wardrobe.Count) return;
            SetCharacter(Wardrobe.Look(o));   // who is riding, in which clothes: a new body if that changed (RiderBodyHero)
            if (!ownMats) OwnMaterials();
            // (on another rider's set of clothes the first row is that set as made: Paul's black puffer, his red trousers)
            var j = Wardrobe.Shown(Wardrobe.Cat.Jacket, o[(int)Wardrobe.Cat.Jacket], heroOutfit);
            if (gearJacket != null) Cloth(gearJacket, j, 1f);
            for (int i = 0; i < jacketMats.Count; i++) Cloth(jacketMats[i], j, i < jacketShade.Count ? jacketShade[i] : 1f);
            if (pantsMat != null) Cloth(pantsMat, Wardrobe.Shown(Wardrobe.Cat.Pants, o[(int)Wardrobe.Cat.Pants], heroOutfit), 1f);
            WearFleece(o);   // (pyjamas: the fleece as made, or dyed by the jacket's row, RiderBodyHero)
            var b = Wardrobe.Get(Wardrobe.Cat.Board, o[(int)Wardrobe.Cat.Board]);
            // a graphic, when one is on, in place of the plain colour (on the hero's own deck and skis: the previous
            // rider's board was never laid out for one)
            var art = !Wardrobe.Classic && hero != null ? Wardrobe.Get(Wardrobe.Cat.Graphic, o[(int)Wardrobe.Cat.Graphic]) : null;
            if (boardMat != null)
            {
                bool printed = BoardArt.Put(boardMat, art, b.a);
                if (printed) b = art;
                Sheen(boardMat, b.smooth);
                if (boardMat.HasProperty("_Metallic")) boardMat.SetFloat("_Metallic", b.metal);
            }
            ShownGraphic = art != null && art.art != null ? o[(int)Wardrobe.Cat.Graphic] : 0;
            BoardArt.Put(tubeBand, art, TubeBandWhite);   // (the tube's stripe: no uvs, so the design's rail colour)
            if (gearPalette != null)
            {
                System.Array.Copy(palBase, palNow, palBase.Length);
                for (int c = 0; c < PaletteCells.Length; c++)
                {
                    var cells = PaletteCells[c];
                    if (cells == null) continue;
                    var it = Wardrobe.Get((Wardrobe.Cat)c, o[c]);
                    for (int k = 0; k < cells.Length; k++)
                    {
                        Color col = k == 0 ? it.a : k == 1 ? it.b : it.c;
                        int px = PalettePixel(cells[k]);
                        if (col.a <= 0f || px < 0) continue;   // (a trim the item leaves as it is)
                        float smooth = k == 0 && it.smooth >= 0f ? it.smooth : palBase[px].a;   // the alpha is the sheen
                        palNow[px] = new Color(col.r, col.g, col.b, smooth);
                    }
                }
                WearShoes(o);   // (pyjamas: their own shoes take the boots' colour, RiderBodyHero)
                gearPalette.SetPixels(palNow);
                gearPalette.Apply(false);
            }
        }

        // MakeHero.py writes the palette 8 swatches wide and 8 rows high (a size the importer leaves as it is: scaled,
        // each row bled into the next), and the gear's uvs sit in the middle of each swatch
        public const int PaletteCols = 8, PaletteRows = 8;   // (Alyssa reads them too)
        int PalettePixel(int cell)
        {
            if (gearPalette == null) return -1;
            int w = gearPalette.width, h = gearPalette.height;
            int x = Mathf.FloorToInt((cell % PaletteCols + 0.5f) / PaletteCols * w), y = Mathf.FloorToInt((cell / PaletteCols + 0.5f) / PaletteRows * h);
            int i = y * w + x;
            return x < w && y < h && i < palNow.Length ? i : -1;
        }

        void Cloth(Material m, Wardrobe.Item it, float shade)
        {
            m.SetColor("_BaseColor", it.a * shade);
            m.SetColor("_Color2", (it.b.a > 0f ? it.b : it.a) * shade);
            m.SetFloat("_Pattern", (float)it.pattern);
            m.SetFloat("_PatternScale", it.scale);
            m.SetFloat("_PatternAt", it.at);
            Sheen(m, it.smooth);
        }

        /// <summary>An item's sheen, or the material's own when it has none.</summary>
        void Sheen(Material m, float s)
        {
            if (!m.HasProperty("_Smoothness")) return;
            if (!ownSheen.TryGetValue(m, out float own)) { own = m.GetFloat("_Smoothness"); ownSheen[m] = own; }
            m.SetFloat("_Smoothness", s >= 0f ? s : own);
        }

        /// <summary>A copy of the gear palette this rider can repaint (null if the texture can't be read).</summary>
        Texture2D OwnPalette(Texture2D pal)
        {
            try
            {
                if (gearPalette != null) { if (Application.isPlaying) Destroy(gearPalette); else DestroyImmediate(gearPalette); }   // (a rider swapped for another: the old body's copy goes)
                palBase = pal.GetPixels();
                palNow = (Color[])palBase.Clone();
                gearPalette = new Texture2D(pal.width, pal.height, TextureFormat.RGBA32, false) { filterMode = pal.filterMode, wrapMode = TextureWrapMode.Clamp, name = "HeroPalette (own)" };
                gearPalette.SetPixels(palBase);
                gearPalette.Apply(false);
                return gearPalette;
            }
            catch (UnityException) { gearPalette = null; return null; }   // (not readable: the gear keeps its colours)
        }

        /// <summary>
        /// Swap the shared board and jacket materials on this rider for copies, so painting them never
        /// writes into the project's material assets (in the editor that would stick after Play).
        /// </summary>
        void OwnMaterials()
        {
            ownMats = true;
            var copies = new System.Collections.Generic.Dictionary<Material, Material>();
            // (the snowy cloth is already this rider's own, made at start, not a project asset: it must stay the
            // very material SnowyClothes writes the snow to. Copied, the jacket wore a copy that never got any,
            // and stayed clean through every fall while the trousers caked up)
            Material Copy(Material m) { if (snowy != null && snowy.Owns(m)) return m; if (!copies.TryGetValue(m, out var c)) { c = new Material(m); copies[m] = c; } return c; }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] != null && (ms[i] == boardMat || jacketMats.Contains(ms[i]))) { ms[i] = Copy(ms[i]); changed = true; }
                if (changed) r.sharedMaterials = ms;
            }
            if (boardMat != null) boardMat = Copy(boardMat);
            for (int i = 0; i < jacketMats.Count; i++) jacketMats[i] = Copy(jacketMats[i]);
        }

        /// <summary>The model's own jacket materials (by name), so a new colour reaches the body too.</summary>
        void FindJacket(GameObject model)
        {
            jacketMats.Clear(); jacketShade.Clear();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string n = m.name.ToLowerInvariant();
                    // the model's materials have no useful names: the jacket is the orange-red one
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    bool orange = c.r > 0.6f && c.g < 0.45f && c.b < 0.3f;
                    if ((orange || n.Contains("jacket") || n.Contains("coat")) && !jacketMats.Contains(m))
                    {
                        jacketMats.Add(m);
                        jacketShade.Add(orange ? Mathf.Clamp(c.maxColorComponent / 0.93f, 0.7f, 1.1f) : 1f);
                    }
                }
        }

        /// <summary>For the test tools: the names of the rider model's materials.</summary>
        public string MaterialNames()
        {
            var sb = new System.Text.StringBuilder();
            if (upper != null)
                foreach (var r in upper.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials)
                        if (m != null) sb.Append(r.name).Append(':').Append(m.name).Append(' ').Append(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "").Append(" | ");
            return sb.ToString();
        }
    }
}
