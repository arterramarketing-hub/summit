using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// The board graphics (Wardrobe.Cat.Graphic, BoardArt): checks, and contact sheets of every design.
///
/// -executeMethod BoardArtShot.Test     (-nographics is fine) the stock, the textures' import, the closet's store
///     (buying with points, too dear, wearing, each rider's own, a plain colour taking it off, kept across a restart),
///     and the uvs laid on the deck, the skis and the poles. Logs BOARDART lines; exits 1 on a failure.
/// -executeMethod BoardArtShot.Closet   (graphics) every design in the closet: on the board, on the skis, and turned
///     round to its base. Writes BoardArt_closet*.png into BOARDART_OUT (or the project folder).
/// -executeMethod BoardArtShot.Riding   (graphics) every design on a run: the chase camera and a closer look, on the
///     board, and on Paul's skis. Writes BoardArt_riding*.png.
/// </summary>
public static class BoardArtShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static SummitGame game; static RiderBody body; static MetaUi meta; static Camp camp; static ClosetStage stage; static Camera cam;
    static int fails;
    static T F<T>(string n) => (T)typeof(SummitGame).GetField(n, NP).GetValue(game);
    static void Check(bool ok, string what) { if (!ok) fails++; Debug.Log($"BOARDART {(ok ? "ok  " : "FAIL")} {what}"); }
    static string OutDir() { var d = System.Environment.GetEnvironmentVariable("BOARDART_OUT"); if (string.IsNullOrEmpty(d)) d = "."; Directory.CreateDirectory(d); return d; }
    static Wardrobe.Item[] Designs => Wardrobe.Items(Wardrobe.Cat.Graphic);
    static int Row(string art) { for (int i = 0; i < Designs.Length; i++) if (Designs[i].art == art) return i; return -1; }

    static void Boot(bool riding)
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game, riding);
        body = F<RiderBody>("body"); meta = F<MetaUi>("meta"); camp = F<Camp>("camp"); stage = meta.Stage; cam = Camera.main;
    }

    // ---------------------------------------------------------------- the checks

    public static void Test()
    {
        int code = 0;
        try
        {
            Boot(false);
            Stock();
            Store();
            Uvs();
        }
        catch (System.Exception e) { Debug.LogError("BOARDART exception " + e); code = 1; }
        TestPrefs.Restore();
        Debug.Log($"BOARDART done: {fails} failure(s)");
        TestHarness.Exit(code != 0 || fails > 0 ? 1 : 0);
    }

    static void Stock()
    {
        var d = Designs;
        Check(d.Length - 1 >= 12 && d.Length - 1 <= 20, $"{d.Length - 1} designs");
        Check(d[0].art == null && d[0].cost == 0, "the first row is PLAIN, free");
        var names = new HashSet<string>(); int rare = 0, premium = 0, maxCommon = 0, minPremium = int.MaxValue, maxPremium = 0, minRare = int.MaxValue;
        for (int i = 1; i < d.Length; i++)
        {
            var it = d[i];
            Check(names.Add(it.name), $"{it.name}: a name of its own");
            var tex = BoardArt.Texture(it.art);
            Check(tex != null && tex.width <= 512 && tex.height <= 512 && tex.width >= 256, $"{it.name}: {it.art} loads ({(tex != null ? tex.width + "x" + tex.height + " " + tex.format : "missing")})");
            var imp = AssetImporter.GetAtPath("Assets/Summit/Resources/BoardArt/" + it.art + ".png") as TextureImporter;
            var ios = imp != null ? imp.GetPlatformTextureSettings("iPhone") : null;
            Check(imp != null && imp.mipmapEnabled && ios.overridden && ios.format == TextureImporterFormat.ASTC_4x4 && ios.maxTextureSize <= 512,
                  $"{it.name}: on the phone ASTC 4x4, at most 512, with mips");
            Check(it.cost > 0, $"{it.name}: {it.cost} points, tier {it.tier}");
            if (it.tier == 0) maxCommon = Mathf.Max(maxCommon, it.cost);
            if (it.tier == 1) { premium++; minPremium = Mathf.Min(minPremium, it.cost); maxPremium = Mathf.Max(maxPremium, it.cost); }
            if (it.tier == 2) { rare++; minRare = Mathf.Min(minRare, it.cost); }
            Check(BoardArt.Thumb(i - 1) != null, $"{it.name}: a thumbnail");
        }
        Check(rare >= 2 && premium >= 2 && minPremium > maxCommon && minRare > maxPremium, $"the rare ones cost more: everyday up to {maxCommon}, premium {minPremium}-{maxPremium} ({premium}), rare from {minRare} ({rare})");
        var timp = AssetImporter.GetAtPath("Assets/Summit/Resources/BoardArt/BoardArtThumbs.png") as TextureImporter;
        Check(timp != null && timp.GetPlatformTextureSettings("iPhone").format == TextureImporterFormat.ASTC_4x4, "the thumbnails: ASTC 4x4");
    }

    static void Store()
    {
        int G = (int)Wardrobe.Cat.Graphic;
        int topo = Row("Topo"), fox = Row("Fox"), volt = Row("Volt"), prism = Row("Prism");
        Progress.Grant(600);
        meta.ShowShop(1);
        meta.ClosetTry(Wardrobe.Cat.Graphic, fox);
        Check(meta.TryOn != null && meta.TryOn[G] == fox && body.ShownGraphic == fox && BoardTex() == "Fox", $"try-on: FOX shows on the board before it's bought ({BoardTex()})");
        meta.ClosetBuy();
        Check(!Progress.Owns(Wardrobe.Cat.Graphic, fox) && Progress.Points == 600 && Progress.Worn(Wardrobe.Cat.Graphic) == 0, "too dear: FOX (1,400) with 600 points is not bought");
        meta.ClosetTry(Wardrobe.Cat.Graphic, topo);
        meta.ClosetBuy();
        Check(Progress.Owns(Wardrobe.Cat.Graphic, topo) && Progress.Points == 200 && Progress.Worn(Wardrobe.Cat.Graphic) == topo, $"bought TOPO (400): owned, worn, {Progress.Points} points left");
        meta.ClosetTry(Wardrobe.Cat.Graphic, volt);
        meta.ClosetBuy();
        Check(!Progress.Owns(Wardrobe.Cat.Graphic, volt) && Progress.Points == 200, "too dear: VOLT (550) with 200");
        meta.ClosetBack();
        Check(body.ShownGraphic == topo && BoardTex() == "Topo", "BACK: the board wears what was bought (TOPO), not the try-on");

        Progress.Add(4800);   // (5,000)
        meta.ShowShop(1);
        meta.ClosetTry(Wardrobe.Cat.Graphic, prism);
        meta.ClosetBuy();
        Check(Progress.Owns(Wardrobe.Cat.Graphic, prism) && Progress.Points == 1000 && Progress.Worn(Wardrobe.Cat.Graphic) == prism, $"bought PRISM (4,000): {Progress.Points} left");
        meta.ClosetTry(Wardrobe.Cat.Graphic, topo);
        int before = Progress.Points;
        meta.ClosetBuy();
        Check(Progress.Worn(Wardrobe.Cat.Graphic) == topo && Progress.Points == before, "an owned one is worn again for nothing");
        meta.ClosetBack();

        // each rider's own
        Progress.Wear(Wardrobe.Cat.Rider, 2);   // Val
        Check(Progress.Worn(Wardrobe.Cat.Graphic) == 0, "Val rides plain until he's given one");
        Progress.Wear(Wardrobe.Cat.Graphic, prism);
        Progress.Wear(Wardrobe.Cat.Rider, Wardrobe.PaulRow);
        Check(Progress.Worn(Wardrobe.Cat.Graphic) == 0 && Progress.Ride == RideKind.Skis, "Paul: plain, on his skis");
        Progress.Wear(Wardrobe.Cat.Graphic, topo);
        body.Wear(Wardrobe.Worn()); body.SetRide(RideKind.Skis);
        Check(BoardTex() == "Topo" && SkiTex() == "Topo", $"Paul's skis wear TOPO ({SkiTex()})");
        Progress.Wear(Wardrobe.Cat.Rider, 0);
        Check(Progress.Worn(Wardrobe.Cat.Graphic) == topo, "the summit rider still has TOPO");
        Progress.Wear(Wardrobe.Cat.Rider, 2);
        Check(Progress.Worn(Wardrobe.Cat.Graphic) == prism, "Val still has PRISM");
        // in the closet, trying a rider on shows their own graphic
        Progress.Wear(Wardrobe.Cat.Rider, 0);
        meta.ShowShop(1);
        meta.ClosetTry(Wardrobe.Cat.Rider, 2);
        Check(meta.TryOn[G] == prism && body.ShownGraphic == prism, "trying Val on: his PRISM shows");
        meta.ClosetTry(Wardrobe.Cat.Board, 0);
        Check(meta.TryOn[G] == 0 && BoardTex() == "", "trying a plain colour on: the graphic comes off for the try-on");
        meta.ClosetBack();
        Check(body.ShownGraphic == topo, "BACK: TOPO again");

        // kept across a restart (the saved game read afresh)
        var loaded = typeof(Progress).GetField("loaded", BindingFlags.NonPublic | BindingFlags.Static);
        loaded.SetValue(null, false);
        Check(Progress.Worn(Wardrobe.Cat.Graphic) == topo && Progress.GraphicFor(2) == prism && Progress.GraphicFor(Wardrobe.PaulRow) == topo && Progress.GraphicFor(1) == 0
              && Progress.Owns(Wardrobe.Cat.Graphic, prism) && !Progress.Owns(Wardrobe.Cat.Graphic, fox) && Progress.Points == 1000,
              $"kept across a restart: summit rider {Progress.GraphicFor(0)}, Val {Progress.GraphicFor(2)}, Paul {Progress.GraphicFor(Wardrobe.PaulRow)}, {Progress.Points} points");

        // a plain colour bought under BOARDS takes the graphic off (that rider's only)
        Progress.Wear(Wardrobe.Cat.Board, 1);
        body.Wear(Wardrobe.Worn());
        Check(Progress.Worn(Wardrobe.Cat.Graphic) == 0 && Progress.GraphicFor(2) == prism && BoardTex() == "", "a plain RED board: the summit rider's graphic off, Val's kept");
        Check(Progress.Points == 850, $"the RED board cost 150 ({Progress.Points})");
        // no real money anywhere: every price is points (the store has nothing else to pay with)
        Progress.ResetAll();
    }

    static string BoardTex()
    {
        foreach (var r in body.BoardGear.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && body.Lower(m)) { var t = m.GetTexture("_BaseMap"); return t != null ? t.name : ""; }
        return "?";
    }
    static string SkiTex()
    {
        if (body.SkiGear == null) return "?";
        foreach (var r in body.SkiGear.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && body.Lower(m)) { var t = m.GetTexture("_BaseMap"); return t != null ? t.name : ""; }
        return "?";
    }

    /// <summary>The deck's top faces on the top sheet, its underside on the base, its rails on the rail colour; a ski's top on the ski's strip; the poles all rail.</summary>
    static void Uvs()
    {
        Progress.Wear(Wardrobe.Cat.Rider, 0);
        body.Wear(Wardrobe.Worn());
        MeshFilter deck = null, ski = null, pole = null;
        foreach (var mf in body.BoardGear.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.name.EndsWith("(art)")) deck = mf;
        if (body.SkiGear != null) foreach (var mf in body.SkiGear.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.name.EndsWith("(art)")) ski = mf;
        foreach (var mf in body.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Pole") && mf.sharedMesh.name.EndsWith("(art)")) pole = mf;
        Check(deck != null, $"the deck's mesh is laid out ({(deck != null ? deck.sharedMesh.name : "none")})");
        Check(ski != null, $"the skis' mesh is laid out ({(ski != null ? ski.sharedMesh.name : "none")})");
        Check(pole != null, $"the poles' mesh is laid out ({(pole != null ? pole.sharedMesh.name : "none")})");
        if (deck != null) Faces("deck", deck, 128f / 256f, 1f, 48f / 256f, 124f / 256f, true);
        if (ski != null) Faces("ski", ski, 8f / 256f, 40f / 256f, -1f, -1f, false);
        if (pole != null)
        {
            var m = pole.sharedMesh; int bad = 0, n = 0;
            var uv = m.uv; var mr = pole.GetComponent<MeshRenderer>();
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (!body.Lower(mr.sharedMaterials[s])) continue;
                foreach (var i in m.GetTriangles(s)) { n++; if (Vector2.Distance(uv[i], BoardArt.RailUv) > 1e-4f) bad++; }
            }
            Check(n > 0 && bad == 0, $"the poles' shafts: {n} corners on the rail colour, {bad} elsewhere");
        }
    }

    static void Faces(string what, MeshFilter mf, float topLo, float topHi, float baseLo, float baseHi, bool hasBase)
    {
        var m = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>();
        var v = m.vertices; var uv = m.uv; var t = mf.transform;
        // "up" in the deck's own frame: the bindings and boots stand on its top
        int top = 0, bas = 0, rail = 0, wrong = 0; float uMin = 1f, uMax = 0f;
        for (int s = 0; s < m.subMeshCount; s++)
        {
            if (!body.Lower(mr.sharedMaterials[s])) continue;
            var tr = m.GetTriangles(s);
            for (int i = 0; i < tr.Length; i += 3)
            {
                float vv = (uv[tr[i]].y + uv[tr[i + 1]].y + uv[tr[i + 2]].y) / 3f;
                bool onRail = Vector2.Distance(uv[tr[i]], BoardArt.RailUv) < 1e-4f;
                if (onRail) { rail++; continue; }
                if (vv >= topLo && vv <= topHi) top++;
                else if (hasBase && vv >= baseLo && vv <= baseHi) bas++;
                else wrong++;
                for (int k = 0; k < 3; k++) { uMin = Mathf.Min(uMin, uv[tr[i + k]].x); uMax = Mathf.Max(uMax, uv[tr[i + k]].x); }
            }
        }
        Check(top > 0 && wrong == 0 && (!hasBase || bas > 0) && uMin < 0.01f && uMax > 0.99f,
              $"the {what}: {top} faces on its top sheet, {bas} on the base, {rail} on the rail colour, {wrong} astray; nose to tail u {uMin:0.000}-{uMax:0.000}");
        // the top sheet faces the way the bindings stand: the face's normal and its region agree
        int disagree = 0, n = m.normals.Length > 0 ? 1 : 0;
        if (n > 0)
        {
            var nr = m.normals;
            Vector3 upL = t.InverseTransformDirection(body.BoardGear.up);
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (!body.Lower(mr.sharedMaterials[s])) continue;
                var tr = m.GetTriangles(s);
                for (int i = 0; i < tr.Length; i += 3)
                {
                    float vv = (uv[tr[i]].y + uv[tr[i + 1]].y + uv[tr[i + 2]].y) / 3f;
                    float up = Vector3.Dot((nr[tr[i]] + nr[tr[i + 1]] + nr[tr[i + 2]]).normalized, upL);
                    if (vv >= topLo && vv <= topHi && up < (hasBase ? 0.2f : -0.2f)) disagree++;   // (a ski's print runs up its turned-up tip)
                    if (hasBase && vv >= baseLo && vv <= baseHi && up > -0.2f) disagree++;
                }
            }
            Check(disagree == 0, $"the {what}: the top sheet faces up and the base down, as ridden ({disagree} faces the wrong way)");
        }
    }

    // ---------------------------------------------------------------- the closet's sheets

    const int CW = 590, CH = 1278;
    static RenderTexture crt;
    static readonly List<CampInput.Touch> none = new List<CampInput.Touch>();

    public static void Closet()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);
        string dir = OutDir();
        try
        {
            Boot(false);
            ChaseCamera.TestDt = Dt;
            TimeOfDay.TestClock = 1.4f; F<DayNight>("dayNight").ApplyNow();
            crt = new RenderTexture(CW, CH, 24);
            cam.targetTexture = crt; cam.aspect = CW / (float)CH;
            meta.UseCamera(cam);
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < 20; i++) CFrame();
            Progress.Grant(3000);
            meta.ShowShop(1);
            Settle(20);
            for (int i = 0; i < 3; i++) CShoot();   // (the first renders of a run are artifacts)
            var d = Designs;
            var tiles = new List<Texture2D>();
            for (int i = 1; i < d.Length; i++) { meta.ClosetTry(Wardrobe.Cat.Graphic, i); Settle(6); stage.RideTurn = 0f; stage.Pose(0f); tiles.Add(CShoot()); }
            Sheet(tiles, CW, CH, 6, 0.5f, Path.Combine(dir, "BoardArt_closet_board.png"));
            // the list scrolled to the rare ones, one highlighted, and the button's price
            meta.ClosetTry(Wardrobe.Cat.Graphic, d.Length - 1); Settle(10); stage.RideTurn = 0f; stage.Pose(0f);
            Save(CShoot(), Path.Combine(dir, "BoardArt_closet_rare.png"));
            meta.ClosetTry(Wardrobe.Cat.Graphic, 2); Settle(10); meta.ClosetBuy(); Settle(4); stage.RideTurn = 0f; stage.Pose(0f);
            Save(CShoot(), Path.Combine(dir, "BoardArt_closet_bought.png"));
            meta.ClosetTry(Wardrobe.Cat.Graphic, 1); Settle(10); stage.RideTurn = 0f; stage.Pose(0f);
            Save(CShoot(), Path.Combine(dir, "BoardArt_closet_first.png"));
            // turned round to the base
            tiles.Clear();
            for (int i = 1; i < d.Length; i++) { meta.ClosetTry(Wardrobe.Cat.Graphic, i); Settle(2); stage.RideTurn = 180f; stage.Pose(0f); tiles.Add(CShoot()); }
            Sheet(tiles, CW, CH, 6, 0.5f, Path.Combine(dir, "BoardArt_closet_base.png"));
            // on the skis
            meta.ClosetBack();
            Progress.PickRide(RideKind.Skis);
            meta.ShowShop(1);
            Settle(10);
            tiles.Clear();
            for (int i = 1; i < d.Length; i++) { meta.ClosetTry(Wardrobe.Cat.Graphic, i); Settle(4); stage.RideTurn = 0f; stage.Pose(0f); tiles.Add(CShoot()); }
            Sheet(tiles, CW, CH, 6, 0.5f, Path.Combine(dir, "BoardArt_closet_skis.png"));
            meta.ClosetBack();
        }
        catch (System.Exception e) { Debug.LogError("BOARDART exception " + e); }
        ChaseCamera.TestDt = 0f; TimeOfDay.TestClock = -1f;
        QualitySettings.SetQualityLevel(quality, false);
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void CFrame()
    {
        if (meta.ClosetOpen) meta.ClosetStep(none, Dt);
        else camp.Walk(Vector2.zero, Dt);
        body.Apply(Dt);
        if (stage != null && stage.Open) stage.Pose(Dt);
        camp.CameraStep(Dt);
        typeof(Camp).GetMethod("LateUpdate", NP).Invoke(camp, null);
    }
    static void Settle(int n) { for (int i = 0; i < n; i++) CFrame(); }

    static Texture2D CShoot()
    {
        cam.targetTexture = crt; cam.aspect = CW / (float)CH;
        int keptMask = cam.cullingMask;
        cam.cullingMask |= 1 << meta.gameObject.layer;
        Canvas.ForceUpdateCanvases();
        var t = Render(CW, CH, crt);
        cam.cullingMask = keptMask;
        return t;
    }

    /// <summary>A render as the camera stands (skinned meshes baked: batch renders don't skin them).</summary>
    static Texture2D Render(int w, int h, RenderTexture rt)
    {
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        var block = new MaterialPropertyBlock();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterials = sk.sharedMaterials;
            for (int i = 0; i < sk.sharedMaterials.Length; i++) { sk.GetPropertyBlock(block, i); if (!block.isEmpty) mr.SetPropertyBlock(block, i); }
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; off.Add(sk); baked.Add(g);
        }
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        cam.Render(); cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in off) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = null;
        return tex;
    }

    static void Save(Texture2D t, string file) { File.WriteAllBytes(file, t.EncodeToPNG()); Debug.Log("BOARDART wrote " + file); }

    /// <summary>Tiles in a grid, `cols` across, each scaled by `k`, on dark grey.</summary>
    static void Sheet(List<Texture2D> tiles, int w, int h, int cols, float k, string file)
    {
        int tw = Mathf.RoundToInt(w * k), th = Mathf.RoundToInt(h * k), gap = 6;
        int rows = (tiles.Count + cols - 1) / cols;
        var sheet = new Texture2D(cols * tw + (cols + 1) * gap, rows * th + (rows + 1) * gap, TextureFormat.RGB24, false);
        var fill = new Color32[sheet.width * sheet.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(20, 20, 22, 255);
        sheet.SetPixels32(fill);
        for (int n = 0; n < tiles.Count; n++)
        {
            int c = n % cols, r = n / cols;
            var src = tiles[n];
            var px = new Color[tw * th];
            for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++) px[y * tw + x] = src.GetPixelBilinear((x + 0.5f) / tw, (y + 0.5f) / th);
            sheet.SetPixels(gap + c * (tw + gap), sheet.height - (r + 1) * (th + gap), tw, th, px);
        }
        sheet.Apply();
        Save(sheet, file);
    }

    // ---------------------------------------------------------------- on a run

    static Rider rider; static SlopeStreamer terrain; static Forest props; static ChaseCamera chase; static MethodInfo late; static Atmosphere atmo; static CourseDressing dress;
    static int step;
    const int RW = 300, RH = 420;

    public static void Riding()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);
        string dir = OutDir();
        try
        {
            Boot(true);
            rider = F<Rider>("rider"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
            Physics.simulationMode = SimulationMode.Script;
            dress = Object.FindFirstObjectByType<CourseDressing>();
            chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
            atmo = Object.FindFirstObjectByType<Atmosphere>();
            TimeOfDay.TestClock = 1.4f; F<DayNight>("dayNight").ApplyNow();
            TestBoot.NewMountain(game, 1000, terrain, props);
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            ChaseCamera.TestDt = 4 * Dt;
            foreach (var (who, set, k, name) in new[] { (0, 0, RideKind.Snowboard, "board"), (Wardrobe.PaulRow, 3, RideKind.Skis, "skis") })
            {
                var chaseTiles = new List<Texture2D>(); var closeTiles = new List<Texture2D>();
                var o = (int[])Wardrobe.Worn().Clone();
                o[(int)Wardrobe.Cat.Rider] = who; o[(int)Wardrobe.Cat.Outfit] = set; o[(int)Wardrobe.Cat.Jacket] = 0;
                body.SetRide(RideKind.Snowboard); rider.SetRide(RideKind.Snowboard);
                body.Wear(o); body.SetRide(k); rider.SetRide(body.Ride);
                Run();
                for (int i = 1; i < Designs.Length; i++)
                {
                    o[(int)Wardrobe.Cat.Graphic] = i;
                    body.Wear(o);
                    chaseTiles.Add(Chase());
                    closeTiles.Add(Close(k == RideKind.Skis));
                }
                Sheet(chaseTiles, CropW, CropH, 6, 1.5f, Path.Combine(dir, $"BoardArt_riding_{name}_chase.png"));
                Sheet(closeTiles, RW, RH, 6, 1f, Path.Combine(dir, $"BoardArt_riding_{name}_close.png"));
                // in a turn, up on an edge: the chase camera then sees the top sheet (toe side) or the base (heel side)
                foreach (float steer in new[] { 1f, -1f })
                {
                    chaseTiles.Clear();
                    Run(); Turn(steer);
                    for (int i = 1; i < Designs.Length; i++) { o[(int)Wardrobe.Cat.Graphic] = i; body.Wear(o); chaseTiles.Add(Chase()); }
                    Sheet(chaseTiles, CropW, CropH, 6, 1.5f, Path.Combine(dir, $"BoardArt_riding_{name}_turn{(steer > 0 ? "R" : "L")}.png"));
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("BOARDART exception " + e); }
        ChaseCamera.TestDt = 0f; TimeOfDay.TestClock = -1f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        QualitySettings.SetQualityLevel(quality, false);
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>Under way down the first stretch, then held still for the shots.</summary>
    static void Run()
    {
        float z = Course.First + 140f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 12f;
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); chase.Snap();
        step = 0;
        for (float t = 0f; t < 1.2f; t += Dt)
        {
            rider.Steer = Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);
            rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (step % 4 == 0) { body.Apply(4 * Dt); late.Invoke(chase, null); }
            step++;
        }
        if (rider.Crashed) Debug.Log("BOARDART crashed before the shots");
        body.Apply(Dt);
    }

    /// <summary>
    /// The chase camera as the player has it, at a phone's pixels (1170 x 2532, rendered at half that), cropped round the
    /// rider: what of the graphic the player actually sees.
    /// </summary>
    const int PW = 585, PH = 1266, CropW = 200, CropH = 280;
    /// <summary>Hard into a turn for a moment, then held there for the shots.</summary>
    static void Turn(float steer)
    {
        for (float t = 0f; t < 0.55f; t += Dt)
        {
            rider.Steer = steer;
            rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (step % 4 == 0) { body.Apply(4 * Dt); late.Invoke(chase, null); }
            step++;
        }
        body.Apply(Dt);
        Debug.Log($"BOARDART turn {steer}: lean {rider.Lean:0.00}");
    }

    static Texture2D Chase()
    {
        cam.fieldOfView = 60f;
        for (int i = 0; i < 30; i++) late.Invoke(chase, null);
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
        DynamicGI.UpdateEnvironment();
        var rt = new RenderTexture(PW, PH, 24);
        var full = Render(PW, PH, rt);
        Vector3 vp = cam.WorldToViewportPoint(rider.transform.position + Vector3.up * 0.6f);
        cam.targetTexture = null; rt.Release();
        int x0 = Mathf.Clamp(Mathf.RoundToInt(vp.x * PW) - CropW / 2, 0, PW - CropW);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(vp.y * PH) - CropH / 2, 0, PH - CropH);
        Debug.Log($"BOARDART chase: the rider at {vp.x:0.00},{vp.y:0.00} of the screen");
        var crop = new Texture2D(CropW, CropH, TextureFormat.RGB24, false);
        crop.SetPixels(full.GetPixels(x0, y0, CropW, CropH)); crop.Apply();
        Object.DestroyImmediate(full);
        return crop;
    }

    /// <summary>Closer, from the side and above, where the top sheet shows.</summary>
    static Texture2D Close(bool skis)
    {
        chase.enabled = false;
        Vector3 p = rider.transform.position;
        Vector3 f = rider.Velocity; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, f);
        Vector3 at = p + Vector3.up * 0.25f;
        Vector3 from = at - f * (skis ? 1.6f : 1.2f) + side * (skis ? 1.3f : 1.9f) + Vector3.up * 1.9f;
        cam.fieldOfView = 40f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
        var t = RShoot();
        chase.enabled = true;
        return t;
    }

    static Texture2D RShoot()
    {
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
        DynamicGI.UpdateEnvironment();
        var rt = new RenderTexture(RW, RH, 24);
        var t = Render(RW, RH, rt);
        cam.targetTexture = null; rt.Release();
        return t;
    }
}
