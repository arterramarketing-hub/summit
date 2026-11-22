using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Progress = Summit.Progress;

/// <summary>
/// Everyone's face, close: Alyssa at the fire, Val, Breanna and Paul in the camp, the hero, and some of the camp's and
/// the lodge's own people. Each one from the front, three-quarters, and from as far as the camp's camera usually is,
/// by day (and the named ones by night), at the phone's quality level.
/// Batch (with graphics): -executeMethod FaceShot.Run   (writes FACES_OUT or FaceShots/*.png; logs FACE lines)
/// </summary>
public static class FaceShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static SummitGame game; static Camp camp; static CampCouple couple; static Camera cam; static RiderBody body;
    static DayNight dayNight; static MethodInfo tUpd, fUpd, aLate; static SlopeStreamer terrain; static Forest props; static Atmosphere atmo;
    static string outDir;
    static int fails;

    public static void Run()
    {
        outDir = System.Environment.GetEnvironmentVariable("FACES_OUT");
        if (string.IsNullOrEmpty(outDir)) outDir = "FaceShots";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        int code = 0;
        try
        {
            QualitySettings.SetQualityLevel(0, true);   // Mobile: what the phone draws with
            Progress.Wear(Wardrobe.Cat.Rider, 0);
            Progress.Grant(0, Progress.Up.FirePit);
            SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
            EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
            game = Object.FindFirstObjectByType<SummitGame>();
            TestBoot.Boot(game, false);
            camp = Object.FindFirstObjectByType<Camp>();
            body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
            dayNight = Object.FindFirstObjectByType<DayNight>();
            atmo = Object.FindFirstObjectByType<Atmosphere>(); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
            tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
            cam = Camera.main;
            var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
            camp.Redress();
            CampCouple.Manual = true;
            couple = camp.Couple;
            Day(true);
            camp.Teleport(8f, -60f, 200f);
            for (int i = 0; i < 40; i++) Frame();
            for (int i = 0; i < 3; i++) Shoot("warmup");

            // ---- Alyssa, at the fire (her round: warming her hands, face up)
            var al = camp.Alyssa;
            Check(al != null, "Alyssa is at the fire");
            if (al != null)
            {
                al.RoundTime = 24f; al.Step(0f);
                for (int i = 0; i < 10; i++) { al.Step(Dt); Frame(); }
                Vector3 f = al.FaceDir; f.y = 0f; f.Normalize();
                Faces("Alyssa", al.HeadBone, f, true);
            }
            // ---- Val, Breanna, Paul
            if (couple != null)
            {
                Wait(1f);
                string[] names = { "", "Val", "Breanna", "Paul" };
                for (int who = 1; who <= 3; who++)
                {
                    var root = couple.Of(who); var rig = couple.RigOf(who);
                    if (root == null || rig == null) { Log($"{names[who]}: not shown"); continue; }
                    Faces(names[who], rig.Head, root.forward, true);
                }
            }
            // ---- the hero (the summit rider on foot in the camp)
            if (body.Hero != null)
            {
                var hd = body.Hero.Head;
                Vector3 f = -hd.forward; f.y = 0f; f.Normalize();   // (the hero's head bone looks back along its z)
                Faces("Hero", hd, f, false);
            }
            // ---- the camp's own people and the lodge's
            var life = CampLife.Current;
            int n = 0;
            if (life != null)
                foreach (var a in life.Agents)
                {
                    if (a.p == null || !a.p.gameObject.activeInHierarchy || n >= 4) continue;
                    int lod = a.p.Lod; a.p.SetLod(0); a.p.Apply(0f);   // (close up, as when the hero walks up to them: their full model)
                    Faces($"Camp{n}_{(a.p.Look.faceShows ? "face" : "warmer")}", a.p.Rig.Head, Flat(a.p.Facing), false);
                    a.p.SetLod(lod);
                    n++;
                }
            var folk = camp.GetComponent<LodgeFolk>();
            n = 0;
            if (folk != null)
                foreach (var fk in folk.All)
                {
                    if (fk.p == null || n >= 3) continue;
                    int lod = fk.p.Lod; fk.p.SetLod(0); fk.p.Apply(0f);
                    Faces($"Lodge{n}_{(fk.p.Look.faceShows ? "face" : "warmer")}", fk.p.Rig.Head, Flat(fk.p.Facing), false);
                    fk.p.SetLod(lod);
                    n++;
                }
        }
        catch (System.Exception e) { Debug.LogError("FACE exception " + e); code = 1; }
        CampCouple.Manual = false;
        TimeOfDay.TestClock = -1f;
        TestPrefs.Restore();
        Debug.Log($"FACE done: {fails} failure(s)");
        EditorApplication.Exit(code != 0 || fails > 0 ? 1 : 0);
    }

    /// <summary>One person's face: front, three-quarters left, and from the camp camera's distance; night too.</summary>
    static void Faces(string who, Transform head, Vector3 facing, bool night)
    {
        Vector3 c = head.position + Vector3.up * 0.045f;   // (the head bone is at the neck; the face's middle is above it)
        Vector3 side = Vector3.Cross(Vector3.up, facing);
        Look(c + facing * 0.7f + Vector3.up * 0.02f, c, 30f); Shoot($"{who}_front");
        Look(c + (facing * 0.85f + side * 0.53f).normalized * 0.65f + Vector3.up * 0.04f, c, 30f); Shoot($"{who}_threequarter");
        Look(c + (facing * 0.9f - side * 0.3f).normalized * 4.2f + Vector3.up * 0.9f, c - Vector3.up * 0.4f, 34f); Shoot($"{who}_camp");
        if (night) { Day(false); Look(c + facing * 0.7f + Vector3.up * 0.02f, c, 30f); Shoot($"{who}_night"); Day(true); }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    static void Frame()
    {
        camp.Walk(Vector2.zero, Dt);
        body.PhysicsStep();
        body.Apply(Dt);
        couple?.Step(Dt);
        camp.CameraStep(Dt);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        aLate.Invoke(atmo, null);
    }

    static void Wait(float secs) { for (float t = 0f; t < secs; t += Dt) Frame(); }

    static void Day(bool day)
    {
        TimeOfDay.TestClock = day ? 1.4f : 3.4f;
        if (dayNight != null) dayNight.ApplyNow();
        camp.SetNight(day ? 0f : 1f);
    }

    static void Look(Vector3 from, Vector3 at, float fov) { cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov; }

    static void Shoot(string file)
    {
        int w = 600, h = 600;   // (a phone's pixels for a close-up of a head: a third of its width)
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = 1f;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
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
    }

    static void Check(bool ok, string what) { if (!ok) fails++; Log($"{(ok ? "ok  " : "FAIL")} {what}"); }
    static T F<T>(string n) where T : Object => (T)typeof(SummitGame).GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(game);
    static void Log(string s) => Debug.Log("FACE " + s);
}
