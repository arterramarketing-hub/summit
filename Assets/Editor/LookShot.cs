using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Renders the colour filters and lens types: replays the start of a run as StartShot does, stops at a few
/// moments, and renders the same frame once per filter and per lens. Images go to LookShots/ in the project.
/// </summary>
public static class LookShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        Random.InitState(7);   // the same run every time, so renders from two versions show the same frame
        TestBoot.Boot(game);
        var rider = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        var props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var fUpd = typeof(Forest).GetMethod("Update", NP);
        var applyLook = typeof(SummitGame).GetMethod("ApplyLook", NP);
        var applyCam = typeof(SummitGame).GetMethod("ApplyCamera", NP);
        var fFilter = typeof(SummitGame).GetField("filterLevel", NP);
        var fStrength = typeof(SummitGame).GetField("filterStrength", NP);
        var fLens = typeof(SummitGame).GetField("lensLevel", NP);
        Directory.CreateDirectory("LookShots");

        void Look(int filter, int strength) { fFilter.SetValue(game, filter); fStrength.SetValue(game, strength); applyLook.Invoke(game, null); }
        void Lens(int level)
        {
            fLens.SetValue(game, level); applyCam.Invoke(game, null);
            for (int i = 0; i < 120; i++) late.Invoke(chase, null);   // let the field of view settle on the new lens
            aLate.Invoke(atmo, null);
        }

        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float[] shots = { 5f, 9.5f };
        ChaseCamera.TestDt = 1f / 60f;
        int next = 0;
        const float dt = 1f / 60f;
        bool warmed = false;
        for (float t = 0f; next < shots.Length; t += dt)
        {
            for (int k = 0; k < 2; k++) { rider.Tick(dt / 2f); body.PhysicsStep(); Physics.Simulate(dt / 2f); }
            tUpd.Invoke(terrain, null);
            fUpd.Invoke(props, null);
            body.Apply(dt);
            late.Invoke(chase, null);
            foreach (var tp in Object.FindObjectsByType<CautionTape>(FindObjectsSortMode.None)) typeof(CautionTape).GetMethod("LateUpdate", NP).Invoke(tp, null);
            aLate.Invoke(atmo, null);
            if (t < shots[next]) continue;

            var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            var baked = new System.Collections.Generic.List<GameObject>();
            foreach (var sk in skins) { var m = new Mesh(); sk.BakeMesh(m); var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation); sk.enabled = false; baked.Add(g); }
            if (!warmed) { Shoot(cam, "LookShots/warmup.png"); warmed = true; }   // the first frame shows placeholder materials

            // every filter at full strength, and CINEMATIC at half, all through the default WIDE lens
            Lens(1);
            for (int f = 0; f < LookGrade.Names.Length; f++) { Look(f, 10); Shoot(cam, $"LookShots/t{next}_filter_{Slug(LookGrade.Names[f])}.png"); }
            Look(1, 5); Shoot(cam, $"LookShots/t{next}_filter_cinematic_50.png");

            // the lenses, under NATURAL so only the lens changes
            Look(0, 10);
            Lens(0); Shoot(cam, $"LookShots/t{next}_lens_standard.png");
            Lens(1); Shoot(cam, $"LookShots/t{next}_lens_wide_default.png");
            Lens(2); Shoot(cam, $"LookShots/t{next}_lens_wider.png");
            Lens(3); Shoot(cam, $"LookShots/t{next}_lens_fisheye.png");
            Look(0, 10); Lens(1);

            foreach (var g in baked) Object.DestroyImmediate(g);
            foreach (var sk in skins) sk.enabled = true;
            Debug.Log($"LOOKSHOT {next} t={t:0.00} rider={rider.transform.position} cam={cam.transform.position}");
            next++;
        }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestHarness.Exit(0);
    }

    static string Slug(string s) => s.ToLowerInvariant().Replace(' ', '_');

    static void Shoot(Camera cam, string file)
    {
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
