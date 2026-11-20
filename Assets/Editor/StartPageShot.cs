using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders the start page (StartPage.cs) as the game builds it: the bare fly-around from points round the circle,
/// the page with its title and choices, and the settings open on each tab, by day and by night (and the bare view
/// at dawn and dusk). Also logs what the far view costs to draw beside a frame of a run.
/// Batch: -executeMethod StartPageShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes startmenu-shots/*.png in the project folder (or into $STARTPAGE_OUT).
/// </summary>
public static class StartPageShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;   // an iPhone's shape (19.5:9)
    static SummitGame game; static StartPage start; static Camera cam, uiCam; static Atmosphere atmo; static DayNight dayNight;
    static MethodInfo aLate;
    static string outDir;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("STARTPAGE_OUT") ?? "startmenu-shots";
        Directory.CreateDirectory(outDir);
        game = Object.FindFirstObjectByType<SummitGame>();
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt;
        typeof(SummitGame).GetMethod("Awake", NP).Invoke(game, null);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>(); dayNight = Get<DayNight>("dayNight");
        aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        cam.aspect = W / (float)H;
        // the default lens and no filter, whatever the editor's own save holds
        typeof(SummitGame).GetField("lensLevel", NP).SetValue(game, 1);
        typeof(SummitGame).GetField("lensMode", NP).SetValue(game, 0);
        typeof(SummitGame).GetField("filterLevel", NP).SetValue(game, 0);
        typeof(SummitGame).GetField("halation", NP).SetValue(game, 0);
        typeof(SummitGame).GetMethod("ApplyCamera", NP).Invoke(game, null);
        typeof(SummitGame).GetMethod("ApplyLook", NP).Invoke(game, null);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        typeof(SummitGame).GetMethod("ToTitle", NP).Invoke(game, null);
        sw.Stop();
        start = Get<StartPage>("start");
        Debug.Log($"STARTPAGE built in {sw.ElapsedMilliseconds} ms (the far view, its first shadow, the page): seed={Slope.Seed} far tris={start.Far.Triangles} verts={start.Far.Vertices} reach={start.Far.Reach:0} m");
        Debug.Log($"STARTPAGE summit={FarMountain.SummitPoint} floor={FarMountain.FloorY:0} middle={FarMountain.Middle} cam far={cam.farClipPlane:0} near={cam.nearClipPlane:0} mask={cam.cullingMask}");
        Debug.Log("STARTPAGE fonts: " + string.Join(", ", System.Array.FindAll(Font.GetOSInstalledFontNames(), n => n.Contains("Avenir") || n.Contains("Helvetica Neue"))));

        // the page is an overlay on the phone: here a second camera draws it, flat, over the picture
        var ug = new GameObject("StartPageShot UI Camera");
        uiCam = ug.AddComponent<Camera>();
        uiCam.clearFlags = CameraClearFlags.SolidColor; uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f); uiCam.cullingMask = 1 << 5; uiCam.nearClipPlane = 0.1f; uiCam.farClipPlane = 10f;
        uiCam.enabled = false; uiCam.aspect = W / (float)H; uiCam.fieldOfView = 40f;
        var ud = ug.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        ud.renderPostProcessing = false; ud.renderShadows = false;
        start.UseCamera(uiCam, 1f); start.UiLayer = 5;

        var lensFx = Get<LensFX>("lensFx");
        lensFx.Strength = 1f;
        for (int i = 0; i < 3; i++) Shoot(null, false);   // (the first frames: placeholder materials, no grading LUT yet)

        float lap = StartPage.OrbitSeconds;
        foreach (var (name, clock) in new[] { ("day", 1.4f), ("night", 3.4f), ("morning", 0.4f), ("evening", 2.4f) })
        {
            TimeOfDay.TestClock = clock;
            dayNight.ApplyNow();
            start.Far.Shade(-RenderSettings.sun.transform.forward, -1);
            bool main = name == "day" || name == "night";
            int views = main ? 6 : 3;
            for (int k = 0; k < views; k++)
            {
                start.Clock = lap * k / views;
                start.Preview(0f, false, 0);
                Shoot($"fly_{name}_{k}.png", false);
            }
            if (!main) continue;
            // the page as it stands once it has come up, then the settings on every tab
            start.Clock = 6f;
            start.Preview(30f, false, 0);
            Shoot($"page_{name}.png", true);
            for (int t = 0; t < start.TabCount; t++)
            {
                start.Preview(30f, true, t);
                Shoot($"settings_{name}_{t}.png", true);
            }
        }
        // the photo the mountain is modelled on, and the mountain from the side the photo was taken (down the valley)
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); start.Far.Shade(-RenderSettings.sun.transform.forward, -1);
        PhotoMatch();
        // the title's directions (TitleArt.Styles): each by day, by night, part way through its reveal, and close up
        for (int k = 0; k < TitleArt.Styles.Length; k++)
        {
            var tw = System.Diagnostics.Stopwatch.StartNew();
            start.SetTitleStyle(k, true);
            Debug.Log($"STARTPAGE title {TitleArt.Styles[k].name}: built in {tw.ElapsedMilliseconds} ms ({TitleArt.Width}x{TitleArt.Height} RGBA, {TitleArt.Width * TitleArt.Height * 4 / 1024} KB)");
            string nm = TitleArt.Styles[k].name.ToLowerInvariant();
            foreach (var (when, clock) in new[] { ("day", 1.4f), ("night", 3.4f) })
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow(); start.Far.Shade(-RenderSettings.sun.transform.forward, -1);
                start.Clock = 6f; start.Preview(30f, false, 0);
                Shoot($"title_{nm}_{when}.png", true);
            }
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); start.Far.Shade(-RenderSettings.sun.transform.forward, -1);
            foreach (float t in new[] { 1.1f, 2.6f })
            {
                start.Clock = 6f; start.Preview(2.6f + t, false, 0);
                Shoot($"title_{nm}_reveal_{t * 10f:0}.png", true);
            }
            Sheet($"sheet_title_{nm}.png", $"title_{nm}_day.png", $"title_{nm}_night.png", $"title_{nm}_reveal_11.png", $"title_{nm}_reveal_26.png");
            Crop($"title_{nm}_day.png", $"title_{nm}_day_close.png"); Crop($"title_{nm}_night.png", $"title_{nm}_night_close.png");
        }
        start.SetTitleStyle(TitleArt.DefaultStyle, true);
        // half-way up: the title in, the choices coming
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); start.Far.Shade(-RenderSettings.sun.transform.forward, -1);
        start.Clock = 3.6f; start.Preview(3.6f, false, 0);
        Shoot("page_day_easing_in.png", true);

        // the way in: the lens pushing in on the summit as the cloud closes
        start.Clock = 6f;
        foreach (float t in new[] { 0.3f, 0.6f, 0.8f })
        {
            start.PreviewLeaving(t, 2);
            Shoot($"leaving_{t * 100f:0}.png", true);
        }
        start.Preview(30f, false, 0); start.Tick(0f);
        Sheet("sheet_leaving.png", "page_day.png", "leaving_30.png", "leaving_60.png", "leaving_80.png");
        Sheet("sheet_fly_day.png", "fly_day_0.png", "fly_day_1.png", "fly_day_2.png", "fly_day_3.png", "fly_day_4.png", "fly_day_5.png");
        Sheet("sheet_fly_night.png", "fly_night_0.png", "fly_night_1.png", "fly_night_2.png", "fly_night_3.png", "fly_night_4.png", "fly_night_5.png");
        Sheet("sheet_fly_dawn_dusk.png", "fly_morning_0.png", "fly_morning_1.png", "fly_morning_2.png", "fly_evening_0.png", "fly_evening_1.png", "fly_evening_2.png");
        Sheet("sheet_page.png", "page_day.png", "settings_day_0.png", "settings_day_1.png", "settings_day_2.png", "settings_day_3.png", "page_night.png", "settings_night_1.png");

        Cost();
        Flow();
        Leaks();
        // back to day before leaving: the night dims the Spray and Trail material assets, and nothing here restores them
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow();
        TimeOfDay.TestClock = -1f;
        EditorApplication.Exit(0);
    }

    /// <summary>What a frame of the far view costs to draw, beside a frame of a run from the start gate.</summary>
    static void Cost()
    {
        start.Clock = 6f; start.Preview(0f, false, 0);
        Shoot(null, false);
        Debug.Log($"STARTPAGE COST fly-around: {Seen()} (far view meshes: {start.Far.Triangles} tris; plus the sky)");
        start.Hide();
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        var chase = cam.GetComponent<ChaseCamera>(); var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        ChaseCamera.TestDt = 1f / 60f;
        for (int i = 0; i < 200; i++) { tUpd.Invoke(terrain, null); body.Apply(1f / 60f); late.Invoke(chase, null); aLate.Invoke(atmo, null); }
        var forest = Object.FindFirstObjectByType<Forest>();
        typeof(Forest).GetMethod("Update", NP)?.Invoke(forest, null);
        Shoot(null, false); Shoot("cost_run_reference.png", false);
        Debug.Log($"STARTPAGE COST run (start gate): {Seen()} (plus the instanced trees and rocks, the particles, the shadow pass, the sky)");
        int trees = 0, rocks = 0;
        var fpos = cam.transform.position;
        for (int cz = Mathf.FloorToInt((fpos.z - 40f) / Forest.Cell); cz < Mathf.FloorToInt((fpos.z + 570f) / Forest.Cell); cz++)
            for (int cx = Mathf.FloorToInt((fpos.x - 290f) / Forest.Cell); cx < Mathf.FloorToInt((fpos.x + 290f) / Forest.Cell); cx++)
            { var k = Forest.At(cx, cz, out _, out _, out _); if (k == Forest.Kind.Tree) trees++; else if (k == Forest.Kind.Rock) rocks++; }
        Debug.Log($"STARTPAGE COST run: trees standing in the forest's drawn range={trees} rocks={rocks} (drawn instanced, each a model of its own)");
        ChaseCamera.TestDt = 0f;
    }

    /// <summary>
    /// The page's logic, driven as the game drives it: every setting stepped and put back, then each way in and the
    /// way back from the camp, checking the camera is handed back as it was found.
    /// </summary>
    static void Flow()
    {
        string St() => typeof(SummitGame).GetField("state", NP).GetValue(game).ToString();
        void Call(string m, params object[] a)
        {
            var mi = a.Length == 1 && a[0] is bool ? typeof(SummitGame).GetMethod(m, NP, null, new[] { typeof(bool) }, null) : typeof(SummitGame).GetMethod(m, NP, null, System.Type.EmptyTypes, null);
            mi.Invoke(game, a);
        }
        string Cam() => $"mask={cam.cullingMask} near={cam.nearClipPlane:0.##} far={cam.farClipPlane:0} fov={cam.fieldOfView:0}";
        string before = Cam();
        Call("ToTitle");
        Debug.Log($"STARTFLOW title: state={St()} showing={start.Showing} {Cam()} (the game's camera was {before})");
        // every setting: a step on and a step back (a toggle: twice), reading it each time
        for (int i = 0; i < start.RowCount; i++)
        {
            string was = start.RowText(i);
            start.RowStep(i, 1); string then = start.RowText(i);
            if (then == was && !start.RowToggle(i)) { start.RowStep(i, -1); then = start.RowText(i); start.RowStep(i, 1); }   // (already at its top: down and back)
            else start.RowStep(i, start.RowToggle(i) ? 1 : -1);
            string back = start.RowText(i);
            Debug.Log($"STARTFLOW setting {i}: {was}  ->  {then.Substring(then.IndexOf('=') + 2)}  ->  {back.Substring(back.IndexOf('=') + 2)}{(was != back ? "   NOT RESTORED" : "")}{(was == then ? "   (no change: at an end, or greyed)" : "")}");
        }
        // the three controls, by touch
        const float dt = 1f / 60f;
        int Tap(string control, int frames)
        {
            int go = start.Step(dt, true, start.ScreenPoint(control));
            for (int f = 0; f < frames && go == 0; f++) { start.Tick(dt); go = start.Step(dt, false, Vector2.zero); }
            return go;
        }
        start.Preview(30f, false, 0);
        int g = Tap("settings", 40);
        Debug.Log($"STARTFLOW tap settings: go={g} state={St()}");
        start.Step(dt, true, new Vector2(W * 0.5f, H * 0.9f));   // (the page here is drawn by the picture's camera: up in the view, off the sheet, puts it away)
        for (int f = 0; f < 40; f++) { start.Tick(dt); start.Step(dt, false, Vector2.zero); }
        g = Tap("run", 200);
        Debug.Log($"STARTFLOW tap GO FOR A RUN: go={g} cloud={start.CoverAlpha:0.00}");
        start.Hide(); Call("ToReady", false);
        for (int f = 0; f < 80; f++) start.Tick(dt);
        Debug.Log($"STARTFLOW at the gate: state={St()} showing={start.Showing} cloud={start.CoverAlpha:0.00} {Cam()} chase={cam.GetComponent<ChaseCamera>().enabled}");
        // back up to the camp, and from its peak chip to the start page, through cloud
        Call("ToHub", false);
        var meta = (MetaUi)typeof(SummitGame).GetField("meta", NP).GetValue(game);
        meta.Act("title");
        Debug.Log($"STARTFLOW camp: state={St()} asked={meta.TitleAsked} {Cam()}");
        meta.TitleAsked = false;
        start.Through(() => Call("ToTitle"));
        for (int f = 0; f < 40; f++) start.Tick(dt);
        Debug.Log($"STARTFLOW back at the title: state={St()} showing={start.Showing} busy={start.Busy} cloud={start.CoverAlpha:0.00} {Cam()}");
        start.Preview(30f, false, 0);
        g = Tap("camp", 200);
        start.Hide(); Call("ToHub", false);
        for (int f = 0; f < 80; f++) start.Tick(dt);
        var camp = Object.FindFirstObjectByType<Camp>();
        Debug.Log($"STARTFLOW tap VISIT CAMP: go={g} state={St()} camp.Active={camp.Active} cloud={start.CoverAlpha:0.00} {Cam()}");
    }

    /// <summary>
    /// Tools/art/rainier-extended.jpg (its mountain) above the far view from the matching side, framed the same way.
    /// </summary>
    static void PhotoMatch()
    {
        const int PW = 1400, PH = 520;
        var sheet = new Texture2D(PW, PH * 2, TextureFormat.RGB24, false);
        var photo = new Texture2D(2, 2);
        photo.LoadImage(File.ReadAllBytes("Tools/art/rainier-extended.jpg"));
        // the mountain's part of the photo: x 1500..5700 of 6516, y (from the top) 520..2080 of 2691
        float u0 = 1500f / 6516f, u1 = 5700f / 6516f, v1 = 1f - 520f / 2691f, v0 = 1f - 2080f / 2691f;
        var px = new Color[PW * PH];
        for (int y = 0; y < PH; y++)
            for (int x = 0; x < PW; x++)
                px[y * PW + x] = photo.GetPixelBilinear(Mathf.Lerp(u0, u1, (x + 0.5f) / PW), Mathf.Lerp(v0, v1, (y + 0.5f) / PH));
        sheet.SetPixels(0, PH, PW, PH, px);
        Object.DestroyImmediate(photo);
        // the far view from straight down the valley, through a lens that frames it as the photo is framed
        start.Clock = (0f - StartPage.StartAzimuth) / 360f * StartPage.OrbitSeconds;
        start.Preview(0f, false, 0);
        start.Frame(0f); aLate.Invoke(atmo, null);
        float keptFov = cam.fieldOfView; var keptRot = cam.transform.rotation;
        var mid = FarMountain.Middle;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(mid.x, FarMountain.FloorY + 1500f, mid.z) - cam.transform.position, Vector3.up);
        cam.fieldOfView = 15.5f;
        var rt = RenderTexture.GetTemporary(PW, PH, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt; cam.aspect = PW / (float)PH;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(PW, PH, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, PW, PH), 0, 0); tex.Apply();
        sheet.SetPixels(0, 0, PW, PH, tex.GetPixels());
        RenderTexture.active = null; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
        cam.aspect = W / (float)H; cam.fieldOfView = keptFov; cam.transform.rotation = keptRot;
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(outDir, "sheet_photo_match.png"), sheet.EncodeToPNG());
        Object.DestroyImmediate(tex); Object.DestroyImmediate(sheet);
        Debug.Log($"STARTPAGE sheet_photo_match.png: the photo above, the far view below, from {cam.transform.position}");
    }

    /// <summary>
    /// Nothing of the run's or the camp's screens may show on the start page, and the avalanche's bar only while
    /// riding with the slide on its way. Runs the game's own Update once in each state (that is what sets the HUD)
    /// and lists every HUD and camp-menu graphic left showing. A render misses these: the page's pictures are taken
    /// without the overlay canvases.
    /// </summary>
    static void Leaks()
    {
        var hud = (Hud)typeof(SummitGame).GetField("hud", NP).GetValue(game);
        var meta = (MetaUi)typeof(SummitGame).GetField("meta", NP).GetValue(game);
        var update = typeof(SummitGame).GetMethod("Update", NP);
        var bar = hud.transform.Find("Safe/SlideBar");
        string St() => typeof(SummitGame).GetField("state", NP).GetValue(game).ToString();
        string Showing(Component root)
        {
            var sb = new System.Text.StringBuilder(); int n = 0;
            foreach (var g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
            {
                if (!g.enabled || !g.gameObject.activeInHierarchy) continue;
                float a = g.color.a;
                foreach (var cg in g.GetComponentsInParent<CanvasGroup>()) a *= cg.alpha;
                if (a < 0.02f) continue;
                if (g is UnityEngine.UI.Text t && string.IsNullOrEmpty(t.text)) continue;
                n++; sb.Append(' ').Append(g.transform.parent.name).Append('/').Append(g.name);
            }
            return n + (n > 0 ? ":" + sb : "");
        }
        void Check(string where, bool barWanted, bool nothingWanted)
        {
            try { update.Invoke(game, null); }
            catch (System.Exception e) { Debug.Log($"STARTLEAK {where}: Update threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
            string h = Showing(hud), m = Showing(meta);
            bool barOn = bar != null && bar.gameObject.activeInHierarchy;
            bool ok = barOn == barWanted && (!nothingWanted || (h.StartsWith("0") && m.StartsWith("0")));
            Debug.Log($"STARTLEAK {where}: state={St()} avalanche bar={(bar == null ? "NOT FOUND" : barOn ? "showing" : "hidden")} hud showing={h} | camp menus showing={m}   {(ok ? "OK" : "LEAK")}");
        }
        void Call(string mth, params object[] a)
        {
            var mi = a.Length == 1 ? typeof(SummitGame).GetMethod(mth, NP, null, new[] { typeof(bool) }, null) : typeof(SummitGame).GetMethod(mth, NP, null, System.Type.EmptyTypes, null);
            mi.Invoke(game, a);
        }
        Call("ToTitle"); Check("start page", false, true);
        start.Hide(); Call("ToReady", false); Check("ready, before the run", false, false);
        Call("ToRiding"); Check("riding, the slide not yet released", false, false);
        // the slide on its way: as the game shows it
        hud.SetRun(40f, 100, 1f, 80f, 160f, true);
        Debug.Log($"STARTLEAK riding, the slide 80 m behind: avalanche bar={(bar != null && bar.gameObject.activeInHierarchy ? "showing" : "hidden")}   {(bar != null && bar.gameObject.activeInHierarchy ? "OK" : "LEAK")}");
        typeof(SummitGame).GetMethod("ToEnd", NP).Invoke(game, new object[] { "WIPED OUT", false, false });
        Check("end screen", false, false);
        Call("ToHub", false); Check("camp", false, false);
        Call("ToTitle"); Check("start page again, after a run", false, true);
    }

    /// <summary>What the camera would draw: the renderers in its view on the layers it sees, and their triangles.</summary>
    static string Seen()
    {
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        int n = 0, mats = 0; long tris = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || (cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
            Mesh m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
            if (m == null) continue;
            n++; mats += r.sharedMaterials.Length;
            for (int sub = 0; sub < m.subMeshCount; sub++) tris += m.GetIndexCount(sub) / 3;
        }
        return $"renderers in view={n} draws (one per material)={mats} triangles={tris}";
    }

    static void Shoot(string file, bool ui)
    {
        start.Frame(0f);
        aLate.Invoke(atmo, null);
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        Canvas.ForceUpdateCanvases();
        cam.Render();
        if (file != null)
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            if (ui) Over(tex);
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            RenderTexture.active = null;
            Debug.Log($"STARTPAGE {file} clock={start.Clock:0.0} cam={cam.transform.position} fov={cam.fieldOfView:0.0} sun={-RenderSettings.sun.transform.forward}");
        }
        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
    }

    /// <summary>
    /// Lay the page over the picture as the phone's overlay canvas does: drawn on its own, on clear, and blended
    /// here. (Blended onto clear, a pixel's colour comes out times its alpha and its alpha squared.)
    /// </summary>
    static void Over(Texture2D picture)
    {
        // (in floats: in bytes the squared alpha of a faint shade rounds to nothing, and its fade ends at a line)
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        uiCam.targetTexture = rt; uiCam.Render(); uiCam.targetTexture = null;
        RenderTexture.active = rt;
        var page = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
        page.ReadPixels(new Rect(0, 0, W, H), 0, 0); page.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        var a = picture.GetPixels(); var b = page.GetPixels();
        for (int i = 0; i < a.Length; i++)
        {
            float cover = Mathf.Sqrt(b[i].a);
            Color under = a[i].linear, over = b[i];
            a[i] = new Color(over.r + under.r * (1f - cover), over.g + under.g * (1f - cover), over.b + under.b * (1f - cover), 1f).gamma;
        }
        picture.SetPixels(a); picture.Apply();
        Object.DestroyImmediate(page);
    }

    /// <summary>The title's part of a picture, at twice the size.</summary>
    static void Crop(string from, string to)
    {
        var t = new Texture2D(2, 2);
        t.LoadImage(File.ReadAllBytes(Path.Combine(outDir, from)));
        int y0 = Mathf.RoundToInt(H * 0.66f), hh = Mathf.RoundToInt(H * 0.3f);
        var c = new Texture2D(W * 2, hh * 2, TextureFormat.RGB24, false);
        for (int y = 0; y < hh * 2; y++)
            for (int x = 0; x < W * 2; x++)
                c.SetPixel(x, y, t.GetPixelBilinear((x + 0.5f) / (W * 2f), (y0 + (y + 0.5f) / 2f) / H));
        c.Apply();
        File.WriteAllBytes(Path.Combine(outDir, to), c.EncodeToPNG());
        Object.DestroyImmediate(t); Object.DestroyImmediate(c);
    }

    /// <summary>Several pictures side by side, to look over at once.</summary>
    static void Sheet(string file, params string[] names)
    {
        var sheet = new Texture2D(W * names.Length, H, TextureFormat.RGB24, false);
        for (int k = 0; k < names.Length; k++)
        {
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(Path.Combine(outDir, names[k])));
            sheet.SetPixels(k * W, 0, W, H, t.GetPixels());
            Object.DestroyImmediate(t);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }
}
