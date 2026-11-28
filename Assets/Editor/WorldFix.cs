using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Checks for the world fixes (branch worldfix).
///   HamletCheck: every hamlet on many mountains, every cycle: how far each building's walls (and the lamp post, the
///            fence) stand outside the groomed piste, measured square to the piste's curving centre line.
///            -executeMethod WorldFix.HamletCheck -logFile x.log (no scene needed)
///   HamletShots: the first hamlets of a few mountains, from the riding camera up the hill and from above.
///            -executeMethod WorldFix.HamletShots (no -nographics). $WORLDFIX_TAG names the shots, $WORLDFIX_SHOTS
///            the folder (else the project folder).
/// </summary>
public static class WorldFix
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>How far outside the groomed piste (m) a point is, square to the centre line (negative: on it).</summary>
    public static float OutsidePiste(float x, float z)
    {
        float best = float.MaxValue;
        for (float dz = -40f; dz <= 40f; dz += 0.5f)
        {
            float zz = z + dz, cx = Slope.Centre(zz);
            float d = Mathf.Sqrt((x - cx) * (x - cx) + dz * dz);
            best = Mathf.Min(best, d);
        }
        return best - Slope.PisteHalf;
    }

    static float SiteClearance(Summit.Hamlets.Site s)
    {
        var k = Summit.Hamlets.Kinds[s.kind];
        float r = s.yaw * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
        float best = float.MaxValue;
        for (int i = 0; i <= 4; i++)
            for (int j = 0; j <= 4; j++)
            {
                float lx = -k.hx + i * k.hx * 0.5f, lz = Mathf.Lerp(k.zBack, k.zFront, j * 0.25f);
                float wx = s.x + lx * c + lz * sn, wz = s.z - lx * sn + lz * c;
                best = Mathf.Min(best, OutsidePiste(wx, wz));
            }
        return best;
    }

    public static void HamletCheck()
    {
        var byKind = new Dictionary<string, float>();
        var counts = new Dictionary<string, int>();
        int hamlets = 0, close = 0; float worst = float.MaxValue; string worstAt = "";
        var hist = new int[12];
        for (int seed = 1; seed <= 300; seed++)
        {
            Slope.Init(seed * 37 + 11);
            for (int cyc = 0; cyc <= Summit.Hamlets.LastCycle; cyc++)
            {
                var h = Summit.Hamlets.Of(cyc);
                if (h == null) continue;
                hamlets++;
                float hMin = float.MaxValue;
                foreach (var s in h.sites)
                {
                    var k = Summit.Hamlets.Kinds[s.kind];
                    float d = SiteClearance(s);
                    if (!byKind.ContainsKey(k.name) || d < byKind[k.name]) byKind[k.name] = d;
                    counts[k.name] = (counts.TryGetValue(k.name, out var n) ? n : 0) + 1;
                    if (k.name == "Lamp") continue;
                    hMin = Mathf.Min(hMin, d);
                    if (d < worst) { worst = d; worstAt = $"seed {seed * 37 + 11} cycle {cyc} {k.name} at z {s.z:0} x-c {s.x - Slope.Centre(s.z):0.0}"; }
                }
                hist[Mathf.Clamp(Mathf.FloorToInt(hMin / 2f), 0, hist.Length - 1)]++;
                if (hMin < 8f) close++;
            }
        }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in byKind) sb.Append($" {kv.Key} {kv.Value:0.0} m ({counts[kv.Key]});");
        Debug.Log($"WORLDFIX hamlets: {hamlets} on 300 mountains; nearest wall per kind outside the piste:{sb}");
        var hs = new System.Text.StringBuilder();
        for (int i = 0; i < hist.Length; i++) hs.Append($" {i * 2}-{i * 2 + 2} m: {hist[i]};");
        Debug.Log($"WORLDFIX hamlets' nearest building outside the piste:{hs} under 8 m: {close}; the nearest of all {worst:0.0} m ({worstAt})");
        Debug.Log($"WORLDFIX fence {Summit.Hamlets.FenceOff:0.0} m outside the piste; margin {Summit.Hamlets.Margin:0.0} m");
        TestHarness.Exit(0);
    }

    public static void HamletShots()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var terrain = game.terrain; var props = game.props; var dressing = Get<CourseDressing>("dressing");
        var rider = game.rider; var body = game.body;
        var cam = Camera.main; var atmo = Object.FindFirstObjectByType<Atmosphere>();
        if (cam.TryGetComponent<ChaseCamera>(out var chase)) chase.enabled = false;
        var hamlets = Object.FindFirstObjectByType<Summit.Hamlets>();
        var hamUpd = typeof(Summit.Hamlets).GetMethod("Update", NP);
        string tag = System.Environment.GetEnvironmentVariable("WORLDFIX_TAG"); if (string.IsNullOrEmpty(tag)) tag = "now";
        string dir = System.Environment.GetEnvironmentVariable("WORLDFIX_SHOTS"); if (string.IsNullOrEmpty(dir)) dir = ".";
        bool warmed = false;
        foreach (int seed in new[] { 2131, 1077, 4021 })
        {
            TestBoot.NewMountain(game, seed, terrain, props);
            int shots = 0;
            for (int cyc = 0; cyc <= Summit.Hamlets.LastCycle && shots < 2; cyc++)
            {
                var h = Summit.Hamlets.Of(cyc);
                if (h == null) continue;
                shots++;
                float zt = h.z0 - 35f, xt = Slope.Centre(zt);
                rider.ResetAt(xt, zt); body.ResetVisual(); rider.Stop();
                terrain.Prewarm(); props.Prewarm(); dressing.Rebuild();
                for (int i = 0; i < 4; i++) hamUpd?.Invoke(hamlets, null);
                if (!warmed) { Shoot(cam, atmo, Path.Combine(dir, "HamletWarmup.png")); warmed = true; }
                // riding past it: from up the hill on the piste, looking down along the run
                cam.fieldOfView = 70f;
                cam.transform.position = new Vector3(xt, Slope.Height(xt, zt) + 3.5f, zt);
                float zl = (h.z0 + h.z1) * 0.5f;
                cam.transform.LookAt(new Vector3(Slope.Centre(zl) + h.side * 8f, Slope.Height(Slope.Centre(zl), zl) + 1f, zl));
                Shoot(cam, atmo, Path.Combine(dir, $"Hamlet_{tag}_s{seed}_c{cyc}_ride.png"));
                // from above: the piste's edge and the buildings
                float zm = (h.z0 + h.z1) * 0.5f, xm = Slope.Centre(zm) + h.side * 22f;
                cam.orthographic = true; cam.orthographicSize = 55f;
                cam.transform.SetPositionAndRotation(new Vector3(xm, Slope.Height(xm, zm) + 150f, zm), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                Shoot(cam, atmo, Path.Combine(dir, $"Hamlet_{tag}_s{seed}_c{cyc}_plan.png"));
                cam.orthographic = false;
                float near = float.MaxValue;
                foreach (var s in h.sites) if (Summit.Hamlets.Kinds[s.kind].name != "Lamp") near = Mathf.Min(near, SiteClearance(s));
                Debug.Log($"WORLDFIX shot seed {seed} cycle {cyc}: side {h.side}, z {h.z0:0}..{h.z1:0}, nearest building {near:0.0} m outside the piste");
            }
        }
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>
    /// What draws the white wedges on a gorge's wall (SpireShot.Crags' gorge view): the view rendered with each group
    /// of renderers (by name) hidden in turn, and how much of a patch of the wall changes.
    /// </summary>
    public static void Probe()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        var game = TestHarness.Boot();
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var rider = game.rider; var body = game.body;
        var terrain = game.terrain; var props = game.props; var dayNight = game.dayNight;
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var cam = Camera.main; cam.GetComponent<ChaseCamera>().enabled = false;
        int gorgeSeed = 1; for (int s = 1; s < 2000; s++) { Slope.Init(s); if (Ridge.Built(0) && Ridge.FormOf(0) == Ridge.Form.Valley) { gorgeSeed = s; break; } }
        Slope.Init(gorgeSeed); terrain.Rebuild(); props.Rebuild(); dress.Rebuild();
        float zg = Course.First + Ridge.At + 220f, hg = Ridge.HalfWidth(zg);
        var eye = new Vector3(Slope.Centre(zg) - hg * 0.3f, Slope.Height(Slope.Centre(zg), zg) + 1.8f, zg);
        var at = new Vector3(Slope.Centre(zg + 35f) + hg + 10f, Slope.Height(Slope.Centre(zg + 35f), zg + 35f) + 22f, zg + 35f);
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        rider.ResetAt(eye.x - 2f, eye.z - 2f); body.ResetVisual();
        for (int k = 0; k < 5; k++) { terrain.Prewarm(); props.Prewarm(); dress.Prewarm(); }
        cam.fieldOfView = 64f; cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
        Shoot(cam, atmo, "Probe_warm.png");
        var all = Grab(cam, atmo);
        var groups = new Dictionary<string, List<Renderer>>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            string g = r.name.Split(' ')[0];
            if (!groups.TryGetValue(g, out var l)) groups[g] = l = new List<Renderer>();
            l.Add(r);
        }
        // the crags' two submeshes apart: the drifts (submesh 1) painted red, then the rock (submesh 0)
        var red = new Material(Shader.Find("Universal Render Pipeline/Unlit")); red.SetColor("_BaseColor", Color.red);
        if (groups.TryGetValue("Near", out var nears))
            for (int pass = 0; pass < 2; pass++)
            {
                var keep = new List<Material[]>();
                foreach (var r in nears) { keep.Add(r.sharedMaterials); var m = r.sharedMaterials; if (m.Length > pass) m[pass] = red; r.sharedMaterials = m; }
                var px2 = Grab(cam, atmo);
                for (int i = 0; i < nears.Count; i++) nears[i].sharedMaterials = keep[i];
                int reds = 0, whiteToRed = 0;
                for (int y = 220; y < 640; y++)
                    for (int x = 270; x < 570; x++)
                    {
                        int i = (1278 - 1 - y) * 590 + x;
                        if (px2[i].r > 200 && px2[i].g < 60 && px2[i].b < 60) { reds++; var a = all[i]; if (a.r > 200 && a.g > 200 && a.b > 200) whiteToRed++; }
                    }
                Debug.Log($"WORLDFIX probe: crag submesh {pass} ({(pass == 0 ? "rock" : "drift")}) covers {reds} pixels of the patch, {whiteToRed} of the white ones");
                File.WriteAllBytes($"Probe_sub{pass}.png", ToPng(px2));
            }
        foreach (var kv in groups)
        {
            foreach (var r in kv.Value) r.enabled = false;
            var px = Grab(cam, atmo);
            foreach (var r in kv.Value) r.enabled = true;
            int changed = 0, white = 0;
            // the patch of wall with the wedges (SpireShot's frame: x 270..570, y 220..640 from the top)
            for (int y = 220; y < 640; y++)
                for (int x = 270; x < 570; x++)
                {
                    int i = (1278 - 1 - y) * 590 + x;
                    var a = all[i]; var b = px[i];
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 30) { changed++; if (a.r > 200 && a.g > 200 && a.b > 200) white++; }
                }
            if (changed > 200) Debug.Log($"WORLDFIX probe: hiding '{kv.Key}' ({kv.Value.Count} renderers, e.g. {kv.Value[0].sharedMaterial?.name} / {kv.Value[0].sharedMaterial?.shader.name}) changes {changed} pixels of the patch, {white} of them white");
        }
        TestHarness.Exit(0);
    }

    static byte[] ToPng(Color32[] px)
    {
        var t = new Texture2D(590, 1278, TextureFormat.RGB24, false); t.SetPixels32(px); t.Apply();
        var b = t.EncodeToPNG(); Object.DestroyImmediate(t); return b;
    }

    static Color32[] Grab(Camera cam, Atmosphere atmo)
    {
        if (atmo != null) typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        var px = tex.GetPixels32();
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(tex); rt.Release();
        return px;
    }

    static void Shoot(Camera cam, Atmosphere atmo, string file)
    {
        DynamicGI.UpdateEnvironment();
        if (atmo != null) typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(tex); rt.Release();
    }
}
