using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// The closet at the phone's size and quality: open on several categories, things tried on before they're bought,
/// the list flicked, the hero dragged round to the side, a purchase, BACK (the try-on gone again), and the previous
/// rider's smaller closet. Drives the closet through its own touch handling where it can.
/// Batch: -executeMethod ClosetShot.Run  (writes Closet_*.png in the project folder)
/// </summary>
public static class ClosetShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float dt = 1f / 60f;
    static Camera cam; static MetaUi meta; static Camp camp; static RiderBody body; static ClosetStage stage; static RenderTexture rt;
    static readonly List<CampInput.Touch> none = new List<CampInput.Touch>();

    static readonly string[] Keys = { "summit_points", "summit_jacket", "summit_jackets", "summit_boardcol", "summit_boards", "summit_ride" };

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        int quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's
        // a fresh closet (and put the player's things back afterwards)
        var kept = new Dictionary<string, int>();
        foreach (var k in AllKeys()) if (PlayerPrefs.HasKey(k)) kept[k] = PlayerPrefs.GetInt(k);
        foreach (var k in AllKeys()) PlayerPrefs.DeleteKey(k);
        bool oldHero = PlayerPrefs.GetInt(RiderBody.OldHeroPref, 0) == 1;
        PlayerPrefs.SetInt(RiderBody.OldHeroPref, 0);
        try
        {
            Boot();
            Progress.Grant(2400);
            Progress.Wear(Wardrobe.Cat.Jacket, 1);   // a few things already bought: a cobalt jacket, black gloves are free
            Progress.Wear(Wardrobe.Cat.Helmet, 1);
            Debug.Log($"CLOSET hero={body.Hero != null} classic={Wardrobe.Classic} points={Progress.Points}");

            meta.ShowShop(1);   // the board rack's station opens the closet
            Settle(30);
            for (int i = 0; i < 3; i++) Shoot("Closet_warmup.png");   // (the first renders of a run are artifacts)
            Shoot("Closet1_jackets.png");
            Log("open");

            // flick the list up: it scrolls on and settles
            Drag(new Vector2(0.3f, 0.30f), new Vector2(0.3f, 0.62f), 8);
            Settle(40);
            Shoot("Closet2_jackets_flicked.png");
            Log("flicked");

            // try on before buying: the camo jacket, not owned
            meta.ClosetTry(Wardrobe.Cat.Jacket, 14);
            Settle(20);
            Shoot("Closet3_tryon_camo.png");
            Log("try camo");
            meta.ClosetTry(Wardrobe.Cat.Jacket, 12);
            Settle(10);
            Shoot("Closet3b_tryon_stripes.png");
            meta.ClosetTry(Wardrobe.Cat.Jacket, 16);
            Settle(10);
            Shoot("Closet3c_tryon_plaid.png");

            // tap a tab, then a row: the pants, tartan
            TapTab(Wardrobe.Cat.Pants);
            Settle(10);
            meta.ClosetTry(Wardrobe.Cat.Pants, 10);
            Settle(10);
            Shoot("Closet4_pants_tartan.png");
            Log("pants");

            meta.ClosetTry(Wardrobe.Cat.Helmet, 7);
            Settle(10);
            Shoot("Closet5_helmet_gold.png");
            meta.ClosetTry(Wardrobe.Cat.Goggles, 3);
            Settle(10);
            Shoot("Closet6_goggles_ice.png");
            meta.ClosetTry(Wardrobe.Cat.Gloves, 3);
            Settle(10);
            Shoot("Closet7_gloves_orange.png");
            meta.ClosetTry(Wardrobe.Cat.Pack, 1);
            Settle(60);   // (turning round to show the pack)
            Shoot("Closet8_pack_red.png");
            meta.ClosetTry(Wardrobe.Cat.Board, 12);
            Settle(60);
            Shoot("Closet9_board_chrome.png");
            meta.ClosetTry(Wardrobe.Cat.Ride, 1);
            Settle(10);
            Shoot("Closet10_ride_skis.png");
            Log("ride");

            // who you ride as, and whose clothes: each try-on builds the rider anew and the studio copies them afresh
            meta.ClosetTry(Wardrobe.Cat.Rider, 2); Settle(20); Shoot("Closet10b_rider_val.png");
            meta.ClosetTry(Wardrobe.Cat.Rider, 3); Settle(20); Shoot("Closet10c_rider_breanna.png");
            meta.ClosetTry(Wardrobe.Cat.Rider, 1); Settle(20); Shoot("Closet10d_rider_female.png");
            meta.ClosetTry(Wardrobe.Cat.Outfit, 2); Settle(20); Shoot("Closet10e_outfit_breanna.png");
            // Paul, in his own puffer and in his hoodie and vest; Breanna in her fleece
            // (PLAY AS Paul first: an outfit is tried on whoever is being played)
            meta.ClosetTry(Wardrobe.Cat.Rider, Wardrobe.PaulRow); Settle(20); Shoot("Closet10h_rider_paul.png");
            meta.ClosetBuy();
            Debug.Log($"CLOSET Paul played: ride {Progress.Ride}");
            meta.ClosetTry(Wardrobe.Cat.Outfit, 4); Settle(20); Shoot("Closet10i_paul_hoodie.png");
            stage.Yaw = 160f; Settle(5); Shoot("Closet10j_paul_hoodie_back.png"); stage.Yaw = -22f;
            meta.ClosetTry(Wardrobe.Cat.Rider, 0); meta.ClosetBuy(); Progress.PickRide(RideKind.Snowboard);
            meta.ClosetTry(Wardrobe.Cat.Rider, 3); Settle(20); Shoot("Closet10k_breanna_fleece.png");
            stage.Yaw = 160f; Settle(5); Shoot("Closet10l_breanna_back.png"); stage.Yaw = -22f;
            // Val's pyjamas worn: the first jacket row is the fleece as made, any other dyes it
            meta.ClosetTry(Wardrobe.Cat.Outfit, 1); meta.ClosetBuy();
            meta.ClosetTry(Wardrobe.Cat.Jacket, 0); Settle(20); Shoot("Closet10f_pyjamas_as_made.png");
            meta.ClosetTry(Wardrobe.Cat.Jacket, 16); Settle(20); Shoot("Closet10g_pyjamas_dyed.png");
            Debug.Log($"CLOSET riders: built {body.Built} bodies, outfit worn={Progress.Worn(Wardrobe.Cat.Outfit)} rider worn={Progress.Worn(Wardrobe.Cat.Rider)}");
            meta.ClosetTry(Wardrobe.Cat.Outfit, 0); meta.ClosetBuy();
            Settle(10);

            // drag the hero round to the side (through the closet's own touch handling), then let go
            meta.ClosetTry(Wardrobe.Cat.Jacket, 14);
            Drag(new Vector2(0.80f, 0.55f), new Vector2(0.62f, 0.55f), 20);
            Settle(30);
            Debug.Log($"CLOSET turned to {stage.Yaw:0}°");
            Shoot("Closet11_side.png");
            stage.Yaw = 160f; Settle(5);
            Shoot("Closet11b_back.png");
            stage.Yaw = -22f;

            // buy the camo (BUY, by a tap on the button)
            int before = Progress.Points;
            TapAt(new Vector2(0.72f, 0.046f));
            Settle(10);
            Debug.Log($"CLOSET bought camo: owns={Progress.Owns(Wardrobe.Cat.Jacket, 14)} worn={Progress.Worn(Wardrobe.Cat.Jacket)} points {before} -> {Progress.Points}");
            Shoot("Closet12_bought.png");
            // an unaffordable one, and BUY: refused
            meta.ClosetTry(Wardrobe.Cat.Helmet, 8);
            TapAt(new Vector2(0.72f, 0.046f));
            Settle(5);
            Shoot("Closet13_too_dear.png");
            Debug.Log($"CLOSET chrome helmet refused: owns={Progress.Owns(Wardrobe.Cat.Helmet, 8)}");

            // BACK, with the gold goggles tried on and not bought: they come off again
            meta.ClosetTry(Wardrobe.Cat.Goggles, 7);
            TapAt(new Vector2(0.2f, 0.046f));
            Settle(30);
            Debug.Log($"CLOSET after BACK: open={meta.ClosetOpen} tryOn={(meta.TryOn != null)} goggles worn={Progress.Worn(Wardrobe.Cat.Goggles)} camMask={cam.cullingMask}");
            camp.TeleportTo(Camp.UseLodge ? camp.StationPoint(Camp.Station.Style) : camp.ChaletPoint(-3.3f, 3.6f, -3.4f), camp.ChaletYaw + 180f);
            meta.UseCamera(cam);
            for (int i = 0; i < 60; i++) Frame();
            Shoot("Closet14_back_in_camp.png");

            // the previous rider: jackets, boards and the ride only
            RiderBody.UseHero = false;
            Boot();
            Debug.Log($"CLOSET classic: hero={body.Hero != null} classic={Wardrobe.Classic}");
            meta.ShowShop(1);
            Settle(20);
            Shoot("Closet15_classic.png");
            meta.ClosetBack();
            RiderBody.UseHero = true;
        }
        finally
        {
            foreach (var k in AllKeys()) PlayerPrefs.DeleteKey(k);
            foreach (var kv in kept) PlayerPrefs.SetInt(kv.Key, kv.Value);
            PlayerPrefs.SetInt(RiderBody.OldHeroPref, oldHero ? 1 : 0);
            PlayerPrefs.Save();
            ChaseCamera.TestDt = 0f;
            TimeOfDay.TestClock = -1f;
            QualitySettings.SetQualityLevel(quality, false);
        }
        EditorApplication.Exit(0);
    }

    static IEnumerable<string> AllKeys()
    {
        foreach (var k in Keys) yield return k;
        foreach (var k in Progress.SetKeys()) yield return k;
        foreach (var k in Progress.RiderKeys()) yield return k;
        for (int c = 0; c < Wardrobe.Count; c++) { yield return "summit_wear_" + (Wardrobe.Cat)c; yield return "summit_own_" + (Wardrobe.Cat)c; }
    }

    static void Boot()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game, false);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        body = Get<RiderBody>("body"); camp = Get<Camp>("camp"); meta = Get<MetaUi>("meta");
        stage = meta.Stage;
        cam = Camera.main;
        ChaseCamera.TestDt = dt;
        TimeOfDay.TestClock = 1.4f; Get<DayNight>("dayNight").ApplyNow();
        // the canvas and the camera at the phone's shape from the start (the closet lays itself out as it opens)
        if (rt == null) rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        meta.UseCamera(cam);
        Canvas.ForceUpdateCanvases();
        for (int i = 0; i < 20; i++) Frame();
    }

    /// <summary>A frame in the camp (the closet's touch handling with nothing down, its hero, the camera).</summary>
    static void Frame(List<CampInput.Touch> touches = null)
    {
        if (meta.ClosetOpen) meta.ClosetStep(touches ?? none, dt);
        else camp.Walk(Vector2.zero, dt);
        body.Apply(dt);
        if (stage != null && stage.Open) stage.Pose(dt);
        camp.CameraStep(dt);
        typeof(Camp).GetMethod("LateUpdate", NP).Invoke(camp, null);
    }

    static void Settle(int n) { for (int i = 0; i < n; i++) Frame(); }

    static Vector2 Px(Vector2 f) => new Vector2(f.x * W, f.y * H);

    /// <summary>A finger from one point to another (fractions of the screen) over n frames, then lifted.</summary>
    static void Drag(Vector2 from, Vector2 to, int n)
    {
        var l = new List<CampInput.Touch>();
        for (int i = 0; i <= n; i++) { l.Clear(); l.Add(new CampInput.Touch(7, Px(Vector2.Lerp(from, to, i / (float)n)))); Frame(l); }
        Frame();
    }

    static void TapAt(Vector2 f)
    {
        var l = new List<CampInput.Touch> { new CampInput.Touch(8, Px(f)) };
        Frame(l); Frame(l); Frame();
    }

    static void TapTab(Wardrobe.Cat c)
    {
        // find the tab on the screen by its label
        foreach (var t in meta.GetComponentsInChildren<UnityEngine.UI.Text>())
            if (t.text == Wardrobe.Title(c) && t.transform.parent.name == "Tab")
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, t.transform.position);
                var l = new List<CampInput.Touch> { new CampInput.Touch(9, p) };
                Frame(l); Frame(l); Frame();
                Debug.Log($"CLOSET tapped tab {c} at {p}: now {meta.ClosetCat}");
                return;
            }
        Debug.LogWarning("CLOSET no tab " + c);
    }

    static void Log(string what) => Debug.Log($"CLOSET {what}: cat={meta.ClosetCat} item={meta.ClosetItem} tryOn={(meta.TryOn != null ? string.Join(",", meta.TryOn) : "-")} yaw={stage.Yaw:0}");

    static void Shoot(string file)
    {
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        int keptMask = cam.cullingMask;
        cam.cullingMask |= 1 << meta.gameObject.layer;   // (the canvas draws with this camera here; on the phone it's an overlay)
        Canvas.ForceUpdateCanvases();
        // skinned meshes don't skin in a batch render: draw a baked copy of each, on its own layer
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; off.Add(sk); baked.Add(g);
        }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in off) sk.enabled = true;
        cam.cullingMask = keptMask;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = null;
    }
}
