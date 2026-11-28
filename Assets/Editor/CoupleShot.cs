using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// Val and Breanna in the camp (CampCouple): pictures of them walking hand in hand, at the lookout, by the fire and
/// in the chalet, of the one left alone waving at the hero, by day and by night; and a long walk about the camp that
/// measures their feet (how far a planted foot slides), their joined hands, and any time they got stuck.
/// Batch: -executeMethod CoupleShot.Run  (writes CoupleShots/*.png; logs COUPLE lines)
/// </summary>
public static class CoupleShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static SummitGame game; static Camp camp; static CampCouple couple; static Camera cam; static RiderBody body;
    static DayNight dayNight;
    static MethodInfo tUpd, fUpd, aLate; static SlopeStreamer terrain; static Forest props; static Atmosphere atmo;
    static string outDir = "CoupleShots";

    public static void Run()
    {
        TestPrefs.Pin();
        Directory.CreateDirectory(outDir);
        int code = 0;
        try
        {
            Progress.Wear(Wardrobe.Cat.Rider, 0);   // the summit rider: both of them free
            SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
            EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
            game = Object.FindFirstObjectByType<SummitGame>();
            TestBoot.Boot(game, false);   // the game opens in the camp
            camp = Object.FindFirstObjectByType<Camp>();
            body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
            dayNight = Object.FindFirstObjectByType<DayNight>();
            atmo = Object.FindFirstObjectByType<Atmosphere>(); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
            tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
            cam = Camera.main;
            CampCouple.Manual = true;
            couple = camp.Couple;
            if (couple == null) throw new System.Exception("the camp made no couple");
            Day(true);
            // the hero out of the way, by the snowman, watching
            camp.Teleport(8f, -60f, 200f);
            for (int i = 0; i < 30; i++) Frame();
            Log($"start: {couple.State}");
            // (the first renders of a run come out wrong while the shaders compile)
            for (int i = 0; i < 3; i++) ShootPair("warmup", 4f, 0f, 1.2f);
            // each of them, close and from as far as the camp's camera usually is: their faces show
            Wait(1f);
            ShootOne(1, "Face_Val", 1.4f, 15f, 1.55f); ShootOne(2, "Face_Breanna", 1.4f, -15f, 1.55f); ShootOne(3, "Face_Paul", 1.4f, 10f, 1.55f);
            ShootOne(1, "Camp_Val", 7.5f, 25f, 2.4f, 1.0f); ShootOne(2, "Camp_Breanna", 7.5f, -25f, 2.4f, 1.0f); ShootOne(3, "Camp_Paul", 7.5f, 20f, 2.4f, 1.0f);

            // ---- together: off to the fire, hand in hand
            couple.Send("Fire");
            Until(() => couple.Holding > 0.95f && Walking(1) && Walking(2), 12f);
            Wait(1.2f);
            ShootPair("Together_Walk_Day", 3.6f, 30f, 1.1f);
            ShootPair("Together_Walk_Day_Hands", 2.4f, 0f, 0.9f, 38f);
            ShootPair("Together_Walk_Day_Behind", 4.0f, 165f, 1.4f);
            ShootPair("Together_Walk_Day_Far", 16f, 25f, 3f, 28f);
            Until(() => couple.State.Contains("Breanna Fire"), 40f);
            Wait(3f);
            ShootPair("Together_Fire_Day", 3.2f, 40f, 1.4f);
            ShootPair("Together_Fire_Day_Hands", 2.2f, 100f, 1.0f, 40f);
            // to the lookout: side by side, looking at the view; she leans in
            couple.Send("Lookout");
            Until(() => couple.State.Contains("Breanna Lookout"), 60f);
            Wait(2.5f); couple.LeanNow(); Wait(1.4f);
            ShootPair("Together_Lookout_Day", 3.0f, -35f, 1.5f, 40f);
            ShootPair("Together_Lookout_Day_Back", 3.6f, -20f, 1.3f);
            // the chalet: at the French doors, looking out
            couple.Send("Chalet");
            Until(() => couple.State.Contains("Breanna Chalet"), 80f);
            Wait(2f);
            ShootPair("Together_Chalet_Day", 4.5f, 180f, 1.2f, 40f, true);
            // night: the fire, and walking
            Day(false);
            couple.Send("Fire");
            Until(() => Walking(1) && couple.Holding > 0.95f, 20f);
            Wait(1.5f);
            ShootPair("Together_Walk_Night", 3.6f, 30f, 1.1f);
            Until(() => couple.State.Contains("Breanna Fire"), 60f);
            Wait(3f);
            ShootPair("Together_Fire_Night", 3.2f, 40f, 1.4f);
            Day(true);

            // ---- feet, hands and getting stuck, over a long while together
            Measure("together", 150f);

            // ---- Breanna played: Val alone, waving at her now and then
            Progress.Wear(Wardrobe.Cat.Rider, 3);
            for (int i = 0; i < 20; i++) Frame();
            Log($"Breanna played: {couple.State} (Breanna shown: {couple.Of(2) != null})");
            Wait(8f);
            var v = couple.Of(1);
            if (v != null)
            {
                // the hero a few metres in front of him, in the camera's view
                var at = v.position + v.forward * 6f;
                camp.TeleportTo(new Vector3(at.x, Slope.DrawnHeight(at.x, at.z), at.z), Mathf.Atan2(-v.forward.x, -v.forward.z) * Mathf.Rad2Deg);
                for (int i = 0; i < 20; i++) Frame();
                couple.WaveNow();
                Wait(0.9f);
                var hp = camp.WalkerPosition;
                Look(hp - (couple.Of(1).position - hp).normalized * 2.4f + Vector3.up * 1.8f + Vector3.Cross(Vector3.up, couple.Of(1).position - hp).normalized * 0.8f, couple.Of(1).position + Vector3.up * 1.2f, 40f);
                Shoot("Alone_Val_Waves_Day");
                Day(false); couple.WaveNow(); Wait(2.6f); couple.WaveNow(); Wait(0.9f);
                hp = camp.WalkerPosition;
                // from the side, both of them in the picture
                var vp = couple.Of(1).position; var mid = (vp + hp) * 0.5f;
                Look(mid + Vector3.Cross(Vector3.up, vp - hp).normalized * 4.5f + Vector3.up * 1.6f, mid + Vector3.up * 1.1f, 45f);
                Shoot("Alone_Val_Waves_Night");
                Day(true);
            }
            Measure("Val alone", 50f);
            // ---- Val played: Breanna alone
            Progress.Wear(Wardrobe.Cat.Rider, 2);
            Wait(4f);
            Log($"Val played: {couple.State}");
            ShootOne(2, "Alone_Breanna_Day", 3.4f, 30f);
            Measure("Breanna alone", 50f);
            // ---- the summit rider again: Val comes out of the chalet and joins her
            Progress.Wear(Wardrobe.Cat.Rider, 0);
            float t0 = 0f;
            for (; t0 < 90f && !couple.State.Contains("(together)"); t0 += Dt) Frame();
            Log($"rejoined after {t0:0.0} s: {couple.State}");
            Wait(3f);
            ShootPair("Together_Again", 3.6f, 180f, 1.4f);
            // the cost of a frame of their lives, both near, both far
            Cost();
        }
        catch (System.Exception e) { Debug.LogError("COUPLE exception " + e); code = 1; }
        CampCouple.Manual = false;
        TimeOfDay.TestClock = -1f;
        Progress.Wear(Wardrobe.Cat.Rider, 0);
        TestHarness.Exit(code);
    }

    // ---------------------------------------------------------------------------------------------- the measurements

    /// <summary>
    /// A while of their lives: each foot's slide while planted (the middle of each stance, walking at full stride),
    /// how far apart the joined hands are, and how often someone had to find a way round.
    /// </summary>
    static void Measure(string label, float secs)
    {
        int stuck0 = couple.Stuck;
        var slips = new List<float>(); float worst = 0f; int steps = 0;
        var stanceSlip = new float[3, 2]; var last = new Vector3[3, 2]; var inStance = new bool[3, 2];
        float gapSum = 0f, gapWorst = 0f; int gapN = 0; float walked = 0f; var lastPos = new Vector3[3]; float stillFor = 0f, worstStill = 0f;
        string worstWhere = "";
        for (float t = 0f; t < secs; t += Dt)
        {
            Frame();
            for (int who = 1; who <= 2; who++)
            {
                var root = couple.Of(who); var rig = couple.RigOf(who);
                if (root == null || rig == null) continue;
                if (lastPos[who] != Vector3.zero) walked += Flat(root.position - lastPos[who]).magnitude;
                lastPos[who] = root.position;
                float ph = Phase(who), wk = WalkW(who);
                for (int f = 0; f < 2; f++)
                {
                    // the middle of this foot's stance (the left heel strikes at a quarter, the right at three quarters)
                    float u = Mathf.Repeat(ph - 0.25f - 0.5f * f, 1f);
                    bool mid = u > 0.08f && u < 0.5f && wk > 0.97f;
                    var fp = rig.Foot[f].position;
                    if (mid && inStance[who, f]) stanceSlip[who, f] += Flat(fp - last[who, f]).magnitude;
                    if (!mid && inStance[who, f])
                    {
                        slips.Add(stanceSlip[who, f]); steps++;
                        if (stanceSlip[who, f] > worst) { worst = stanceSlip[who, f]; worstWhere = $"{(who == 1 ? "Val" : "Breanna")} {(f == 0 ? "left" : "right")} at {couple.State}"; }
                        stanceSlip[who, f] = 0f;
                    }
                    inStance[who, f] = mid; last[who, f] = fp;
                }
            }
            if (couple.Holding > 0.95f)
            {
                var a = couple.RigOf(1).Hand[1].position; var b = couple.RigOf(2).Hand[0].position;
                float g = Vector3.Distance(a, b); gapSum += g; gapN++; gapWorst = Mathf.Max(gapWorst, g);
            }
            // not getting anywhere while meaning to walk
            bool going = couple.State.Contains("to ") || couple.State.Contains("joining");
            stillFor = going && WalkW(1) + WalkW(2) < 0.2f ? stillFor + Dt : 0f;
            worstStill = Mathf.Max(worstStill, stillFor);
        }
        slips.Sort();
        float median = slips.Count > 0 ? slips[slips.Count / 2] : 0f, p90 = slips.Count > 0 ? slips[slips.Count * 9 / 10] : 0f;
        Log($"walk {label}: {secs:0} s, {walked:0} m walked, {steps} planted steps; slide per step median {median * 100f:0.0} cm, 90% under {p90 * 100f:0.0} cm, worst {worst * 100f:0.0} cm ({worstWhere}); " +
            $"joined hands {(gapN > 0 ? gapSum / gapN * 100f : 0f):0.0} cm apart on average, worst {gapWorst * 100f:0.0} cm; found a way round {couple.Stuck - stuck0} times; longest standing still while meaning to walk {worstStill:0.0} s; now {couple.State}");
    }

    static float Phase(int who) => couple.PhaseOf(who);
    static float WalkW(int who) => couple.WalkOf(who);
    static bool Walking(int who) => WalkW(who) > 0.9f;

    static void Cost()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 120; i++) couple.Step(Dt);
        Log($"cost: {sw.Elapsed.TotalMilliseconds / 120.0:0.000} ms a frame (editor, both posed every frame)");
    }

    // ---------------------------------------------------------------------------------------------- stepping and shooting

    static void Frame()
    {
        camp.Walk(Vector2.zero, Dt);
        body.PhysicsStep();
        body.Apply(Dt);
        couple.Step(Dt);
        camp.CameraStep(Dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        aLate.Invoke(atmo, null);
    }

    static void Wait(float secs) { for (float t = 0f; t < secs; t += Dt) Frame(); }

    static void Until(System.Func<bool> done, float secs)
    {
        float t = 0f;
        for (; t < secs && !done(); t += Dt) Frame();
        if (t >= secs) Log($"(waited {secs:0} s and gave up: {couple.State})");
    }

    static void Day(bool day)
    {
        TimeOfDay.TestClock = day ? 1.4f : 3.4f;
        if (dayNight != null) dayNight.ApplyNow();
        camp.SetNight(day ? 0f : 1f);
    }

    /// <summary>The camera on the couple: from in front (bearing 0 is the way they face), at a distance and height.</summary>
    static void ShootPair(string name, float dist, float bearing, float height, float fov = 34f, bool through = false)
    {
        var a = couple.Of(1); var b = couple.Of(2);
        if (a == null || b == null) { Log($"{name}: not both there ({couple.State})"); return; }
        var mid = (a.position + b.position) * 0.5f;
        var fwd = Quaternion.Euler(0f, bearing, 0f) * b.forward;
        Look(mid + fwd * dist + Vector3.up * height, mid + Vector3.up * 1.0f, fov);
        Shoot(name);
    }

    static void ShootOne(int who, string name, float dist, float bearing, float height = 1.2f, float at = -1f)
    {
        var a = couple.Of(who);
        if (a == null) { Log($"{name}: not there ({couple.State})"); return; }
        var fwd = Quaternion.Euler(0f, bearing, 0f) * a.forward;
        float aim = at >= 0f ? at : height > 1.4f ? height - 0.05f : 1.0f;
        Look(a.position + fwd * dist + Vector3.up * height, a.position + Vector3.up * aim, 34f);
        Shoot(name);
    }

    static void Look(Vector3 from, Vector3 at, float fov)
    {
        cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov;
    }

    static void Shoot(string file)
    {
        int w = 900, h = 1100;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        // (skinned meshes don't skin in a batch render: draw each as it stands)
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g);
        }
        cam.Render();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file + ".png"), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Log($"shot {file}: {couple.State}");
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static T F<T>(string n) where T : Object => (T)typeof(SummitGame).GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(game);
    static void Log(string s) => Debug.Log("COUPLE " + s);
}
