using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Which of the rider's clothes move in the wind (RiderBody.Loosen): every rider in every set of clothes the closet
/// offers (and the summit kit cut for a woman), built as the game builds them, each skinned mesh listed with its
/// shaders, its size, whether it was given the wind's shapes (Billow, the crosswind's, FlapA, FlapB) and which shapes of
/// its own come first (the collar's, the hood's: a wind that set shapes 0-2 by number drove those instead), and a scale
/// check (the renderer's scale against the size the mesh is drawn at). Exits 1 if any jacket or trousers is left
/// without the wind's shapes. (The camp's couple, CampCouple, dress their own bodies and are not covered.)
/// Batch: -executeMethod ClothAudit.Run -logFile x.log   (-nographics is fine; CLOTHAUDIT_OUT: a file for the table)
/// </summary>
public static class ClothAudit
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Is this cloth a jacket (top) or trousers (legs)? Every model's outfit is HeroBody and HeroLegs (RiderBody.Dress).</summary>
    static string Kind(SkinnedMeshRenderer smr) => smr.name.StartsWith("HeroBody") ? "top" : smr.name.StartsWith("HeroLegs") ? "legs" : "";

    public static void Run()
    {
        var log = new StringBuilder();
        void Say(string l) { Debug.Log("CLOTHAUDIT " + l); log.AppendLine(l); }
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game, false);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        var modelF = typeof(RiderBody).GetField("model", NP);
        string[] who = { "summit rider", "Val", "Breanna", "Paul" };
        int bad = 0;
        var looks = new List<int>();
        for (int w = 0; w < 4; w++) for (int o = 0; o < Wardrobe.OutfitModels.Length; o++) looks.Add(w + 10 * o);
        looks.Add(100);   // the summit kit cut for a woman
        looks.Add(1010);  // Val's pyjamas, trousers dyed
        Say("| rider | clothes | mesh | shaders | verts | height (m) | wind shapes | its own shapes first | old Flutter (shapes 0-2) reached the wind's |");
        Say("|---|---|---|---|---|---|---|---|---|");
        foreach (int look in (System.Environment.GetEnvironmentVariable("CLOTHAUDIT_QUICK") == "1" ? new List<int> { 0, 10, 20 } : looks))
        {
            body.SetCharacter(look);
            var model = (Transform)modelF.GetValue(body);
            if (model == null) { Say($"| look {look} | (not built) |"); continue; }
            int o = (look / 10) % 10;
            string wear = o == 0 ? ((look / 100) % 10 == 1 ? "summit kit (cut for a woman)" : "summit kit") : Wardrobe.OutfitModels[o];
            if (look >= 1000) wear += ", trousers dyed";
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string k = Kind(smr);
                if (k == "") continue;
                var sm = smr.sharedMesh;
                bool shapes = sm != null && sm.GetBlendShapeIndex("Billow") >= 0 && sm.GetBlendShapeIndex("FlapA") >= 0;
                // the mesh's own shapes, ahead of the wind's (the collar's, the hood's): what a wind that set shapes 0-2 drove
                var own = new List<string>();
                for (int i = 0; sm != null && i < sm.blendShapeCount; i++) { string bn = sm.GetBlendShapeName(i); if (bn == "Billow") break; own.Add(bn); }
                bool windAt0 = sm != null && sm.GetBlendShapeIndex("Billow") == 0;
                var mats = new List<string>(); foreach (var m in smr.sharedMaterials) mats.Add(m != null ? m.shader.name.Replace("Universal Render Pipeline/", "") : "-");
                var b = smr.sharedMesh != null ? smr.sharedMesh.bounds : new Bounds();
                float hm = b.size.y * smr.transform.lossyScale.y;
                // as drawn: the mesh skinned onto its bones (world metres), against the mesh's own units
                var baked = new Mesh(); smr.BakeMesh(baked, true);
                var bb = baked.bounds; Object.DestroyImmediate(baked);
                float drawn = bb.size.y * smr.transform.lossyScale.y;
                var mw = ClothWind.MeshToWorld(smr);
                var back = body.transform.TransformDirection(Vector3.back);
                var oldDir = smr.transform.InverseTransformDirection(back).normalized;      // the old Loosen's "wind", taken as mesh space
                var drawnAs = mw.MultiplyVector(oldDir).normalized;                           // where that really points as drawn
                Say($"  scale check {smr.name}: mesh units tall {b.size.y:0.000}, lossyScale {smr.transform.lossyScale.x:0.000}, drawn {mw.GetColumn(1).magnitude:0.0} m per unit; the old wind 'back' drawn {Vector3.Angle(drawnAs, back):0} deg off back (points {drawnAs})");
                Say($"| {who[look % 10]} | {wear} | {smr.name} ({k}) | {string.Join(", ", mats)} | {(smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0)} | {hm:0.00} | {(shapes ? "yes" : "**no**")} | {(own.Count > 0 ? string.Join(", ", own) : "-")} | {(windAt0 ? "yes" : "**no**")} |");
                if (!shapes) bad++;
            }
        }
        Say(bad == 0 ? "PASS: every jacket and pair of trousers has the wind's shapes" : $"FAIL: {bad} cloth meshes without the wind's shapes");
        var outFile = System.Environment.GetEnvironmentVariable("CLOTHAUDIT_OUT");
        if (!string.IsNullOrEmpty(outFile)) System.IO.File.WriteAllText(outFile, log.ToString());
        EditorApplication.Exit(bad == 0 ? 0 : 1);
    }

    /// <summary>
    /// The camp's people in a storm's gust (ClothWind): the camp crowd, the lodge's own (inside and out), the couple, Alyssa
    /// and the hero, stepped for a few seconds as a gust comes through; every skinned mesh with the wind's shapes listed
    /// with how hard its shapes are pushed, near or far, indoors or out. Exits 1 if no one out of doors and near moves, or
    /// anyone indoors does. Batch: -executeMethod ClothAudit.Camp (-nographics is fine; CLOTHAUDIT_OUT: a file).
    /// </summary>
    public static void Camp()
    {
        var log = new StringBuilder();
        void Say(string l) { Debug.Log("CAMPWIND " + l); log.AppendLine(l); }
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        var camp = (Summit.Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
        var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        Summit.Progress.Grant(0, Summit.Progress.Up.FirePit);   // (Alyssa comes with the fire)
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        camp.Redress();
        camp.SetNight(0f);
        var cam = Camera.main;
        var life = CampLife.Current; var folk = LodgeFolk.Current;
        var couple = Object.FindFirstObjectByType<CampCouple>(); var alyssa = Object.FindFirstObjectByType<Alyssa>();
        float storm = TreeWind.ForWeather(1f, 1f);
        // a gust at the camp: from the calm before it to its height
        var at = cam.transform.position; float onset = 30f; bool calm = false;
        for (float c = 20f; c < 900f; c += 0.1f) { TreeWind.Set(c, storm); float g = TreeWind.Gust(at.x, at.z); if (g < 0.1f) calm = true; if (calm && g > 0.6f) { onset = c; break; } }
        const float dt = 1f / 30f;
        float clock = onset - 1f;
        var most = new Dictionary<SkinnedMeshRenderer, float>();
        for (int f = 0; f < 120; f++)
        {
            clock += dt; TreeWind.Set(clock, storm);
            camp.Walk(Vector2.zero, dt); body.Apply(dt); camp.CameraStep(dt);
            life?.Step(dt, cam.transform.position); life?.Tick(dt);
            folk?.Step(dt);
            couple?.Step(dt);
            foreach (var al in Object.FindObjectsByType<Alyssa>(FindObjectsSortMode.None)) al.Step(dt);   // (every one the camp has made)
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                var m = smr.sharedMesh; if (m == null || m.GetBlendShapeIndex("Billow") < 0) continue;
                float w = 0f;
                foreach (var nm in new[] { "Billow", "WindNose", "WindToe", "WindHeel", "FlapA", "FlapB" }) w = Mathf.Max(w, smr.GetBlendShapeWeight(m.GetBlendShapeIndex(nm)));
                most[smr] = Mathf.Max(most.TryGetValue(smr, out var o) ? o : 0f, w);
            }
        }
        Say($"a storm's gust at the camp (strength {storm:0.00}, gust {TreeWind.Gust(at.x, at.z):0.00}): every cloth mesh with the wind's shapes, its hardest push (0..100)");
        Say("| who | mesh | near the camera | indoors | drawn | hardest push |");
        Say("|---|---|---|---|---|---|");
        int outMoving = 0, outNear = 0, inMoving = 0;
        foreach (var kv in most)
        {
            var smr = kv.Key; if (smr == null) continue;
            var t = smr.transform; string who = t.name;
            for (var q = t.parent; q != null; q = q.parent) { if (q.GetComponent<Person>() != null || q.name == "Val" || q.name == "Breanna" || q.name == "Paul" || q.GetComponent<Alyssa>() != null || q.GetComponent<RiderBody>() != null) { who = q.name; break; } }
            var p = smr.GetComponentInParent<Person>();
            bool inside = camp.InsideAt(smr.rootBone != null ? smr.rootBone.position : t.position);
            float dist = Vector3.Distance(cam.transform.position, smr.rootBone != null ? smr.rootBone.position : t.position);
            bool near = dist < 40f && (p == null || p.Lod == 0) && smr.enabled && smr.gameObject.activeInHierarchy;
            Say($"| {who} | {smr.name} | {(near ? $"yes ({dist:0} m)" : $"no ({dist:0} m{(p != null ? $", lod {p.Lod}" : "")})")} | {(inside ? "yes" : "no")} | {(smr.enabled && smr.gameObject.activeInHierarchy ? "yes" : "no")} | {kv.Value:0} |");
            if (near && !inside) { outNear++; if (kv.Value > 5f) outMoving++; }
            if (near && inside && kv.Value > 1f) inMoving++;
        }
        bool ok = outNear > 0 && outMoving == outNear && inMoving == 0;
        Say($"{(ok ? "PASS" : "FAIL")}: near and out of doors {outMoving} of {outNear} moving; indoors moving {inMoving}");
        var outFile = System.Environment.GetEnvironmentVariable("CLOTHAUDIT_OUT");
        if (!string.IsNullOrEmpty(outFile)) System.IO.File.WriteAllText(outFile, log.ToString());
        TreeWind.Still();
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
