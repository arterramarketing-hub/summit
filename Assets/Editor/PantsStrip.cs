using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The rider's clothes in the wind (RiderBody.Loosen, Flutter): three riders in their own clothes (the summit rider in
/// the summit kit, Val in his pyjamas, Paul in his puffer), standing in the camp as a storm's gust comes through, and
/// riding down the piste at speed (from beside, and from the game's camera behind). For each a sheet of 12 frames, a
/// wind map (the pixels the wind moves at any moment, each frame drawn again with the trousers', then the jacket's,
/// wind shapes at nought: trousers yellow, jacket cyan), and PANTS lines: how far the wind moves the trousers and the jacket in each
/// frame (the cloth as drawn against the same pose with the wind's shapes at nought), so the legs' share can be read.
/// Run: Unity -batchmode -projectPath . -executeMethod PantsStrip.Run -logFile p.log (no -nographics).
/// PANTS_OUT: the folder, PANTS_TAG: a prefix (before/after).
/// </summary>
public static class PantsStrip
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    const int W = 360, H = 480, Frames = 12;
    static SummitGame game; static Rider rider; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camp camp; static Camera cam; static ChaseCamera chase; static Atmosphere atmo;
    static MethodInfo late, aLate, tUpd, fUpd;
    static int step;
    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static void Log(string s) => Debug.Log("PANTS " + s);

    static readonly (string name, int look)[] Riders = { ("summit", 0), ("val", 11), ("paul", 33) };

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("PANTS_OUT") ?? ".";
        string tag = System.Environment.GetEnvironmentVariable("PANTS_TAG") ?? "now";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game, false);
        rider = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        camp = F<Camp>("camp");
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>(); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP);
        float storm = TreeWind.ForWeather(1f, 1f), calm = TreeWind.ForWeather(0.2f, 0f);

        // ---- standing in the camp as a storm's gust comes through
        camp.SetNight(0f);
        foreach (var (name, look) in Riders)
        {
            body.SetCharacter(look);
            camp.Teleport(-4f, -58f, 90f);
            TreeWind.Set(10f, storm);
            for (int i = 0; i < 40; i++) CampStep();
            var p = camp.WalkerPosition;
            float onset = Onset(p, storm);
            float clock = onset - 1.5f;
            var yaw = Quaternion.Euler(0f, camp.WalkerYaw, 0f);
            var fwd = yaw * Vector3.forward; var right = yaw * Vector3.right;
            Strip(Path.Combine(outDir, $"{tag}_{name}_standing_gust"), () =>
            {
                for (int k = 0; k < 15; k++) { clock += 1f / 60f; TreeWind.Set(clock, storm); CampStep(); }
                var q = camp.WalkerPosition;
                // from the side, across the wind, on the legs
                Look(q + right * 2.2f + fwd * 0.4f + Vector3.up * 0.75f, q + Vector3.up * 0.6f, 34f);
                return $"gust {TreeWind.Gust(q.x, q.z):0.00} (storm {storm:0.00})";
            });
        }

        // ---- riding down the piste at speed, on a calm day
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { true });
        camp.SetNight(0f);
        Physics.simulationMode = SimulationMode.Script;
        ChaseCamera.TestDt = 4 * Dt;
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        foreach (var (name, look) in Riders)
        {
            body.SetCharacter(look);
            float z = Slope.FinishZ * 0.25f;
            rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 24f;
            terrain.Prewarm(); props.Prewarm(); chase.Snap();
            float clock = 50f; TreeWind.Set(clock, calm);
            for (float t = 0f; t < 1.5f; t += Dt) { clock += Dt; TreeWind.Set(clock, calm); rider.Steer = Hold(); Step(); }
            Strip(Path.Combine(outDir, $"{tag}_{name}_riding"), () =>
            {
                for (int k = 0; k < 8; k++) { clock += Dt; TreeWind.Set(clock, calm); rider.Steer = Hold(); Step(); }
                var q = rider.transform.position; var v = rider.Velocity; v.y = 0f; var f = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
                var side = Vector3.Cross(Vector3.up, f);
                Look(q - side * 2.4f + f * 0.6f + Vector3.up * 0.9f, q + Vector3.up * 0.6f, 34f);
                return $"speed {rider.Speed * 3.6f:0} km/h";
            });
            // and as the game's camera sees it, from behind (narrowed on the rider)
            Strip(Path.Combine(outDir, $"{tag}_{name}_riding_gamecam"), () =>
            {
                for (int k = 0; k < 8; k++) { clock += Dt; TreeWind.Set(clock, calm); rider.Steer = Hold(); Step(); }
                late.Invoke(chase, null);
                var q = rider.transform.position;
                cam.transform.LookAt(q + Vector3.up * 0.7f); cam.fieldOfView = 16f;
                return $"speed {rider.Speed * 3.6f:0} km/h, game camera";
            });
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TreeWind.Still();
        TestHarness.Exit(0);
    }

    /// <summary>The first time a gust at p rises through 0.5 after a calm.</summary>
    static float Onset(Vector3 p, float k)
    {
        bool calm = false;
        for (float c = 20f; c < 900f; c += 0.1f)
        {
            TreeWind.Set(c, k);
            float g = TreeWind.Gust(p.x, p.z);
            if (g < 0.1f) calm = true;
            if (calm && g > 0.5f) return c;
        }
        return 30f;
    }

    static float Hold() => Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);

    static void Step()
    {
        rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (step % 4 == 0) { body.Apply(4 * Dt); late.Invoke(chase, null); aLate.Invoke(atmo, null); }
        step++;
    }

    static void CampStep()
    {
        camp.Walk(Vector2.zero, 1f / 60f);
        body.PhysicsStep();
        body.Apply(1f / 60f);
        camp.CameraStep(1f / 60f);
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
        aLate.Invoke(atmo, null);
    }

    static void Look(Vector3 from, Vector3 at, float fov) { cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov; }

    static bool WindShape(string n) => n.StartsWith("Billow") || n.StartsWith("Flap") || n.StartsWith("Wind");

    /// <summary>How far the wind moves this cloth now: mean and most of its vertices from the same pose with the wind's shapes at nought, in cm (the mesh scaled so the cloth is as tall as drawn).</summary>
    static (float mean, float max) Moved(SkinnedMeshRenderer smr) => Moved(smr, null, out _);
    /// <summary>As Moved; `prev` the offsets a frame before (and `flicker` how far they changed since, cm: what reads as motion).</summary>
    static (float mean, float max) Moved(SkinnedMeshRenderer smr, Vector3[] prev, out float flicker)
    {
        flicker = 0f;
        var mesh = smr.sharedMesh; if (mesh == null) return (0f, 0f);
        var a = new Mesh(); smr.BakeMesh(a, true);
        var keep = new List<(int, float)>();
        for (int i = 0; i < mesh.blendShapeCount; i++) if (WindShape(mesh.GetBlendShapeName(i))) { keep.Add((i, smr.GetBlendShapeWeight(i))); smr.SetBlendShapeWeight(i, 0f); }
        var b = new Mesh(); smr.BakeMesh(b, true);
        foreach (var (i, w) in keep) smr.SetBlendShapeWeight(i, w);
        var va = a.vertices; var vb = b.vertices;
        float h = b.bounds.size.y, worldH = smr.bounds.size.y;
        float k = h > 1e-6f ? worldH / h : 1f;   // drawn metres per baked unit
        double sum = 0; float mx = 0f;
        double fl = 0;
        for (int i = 0; i < va.Length; i++)
        {
            var off = (va[i] - vb[i]) * k; float d = off.magnitude; sum += d; mx = Mathf.Max(mx, d);
            if (prev != null && prev.Length == va.Length) { fl += (off - prev[i]).magnitude; prev[i] = off; }
        }
        if (prev != null && prev.Length == va.Length) flicker = (float)(fl / Mathf.Max(1, va.Length)) * 100f;
        Object.DestroyImmediate(a); Object.DestroyImmediate(b);
        return ((float)(sum / Mathf.Max(1, va.Length)) * 100f, mx * 100f);
    }

    /// <summary>Set this cloth's wind shapes to nought (and back): returns what they were.</summary>
    static List<(int, float)> Zero(SkinnedMeshRenderer smr)
    {
        var keep = new List<(int, float)>(); var mesh = smr != null ? smr.sharedMesh : null;
        for (int i = 0; mesh != null && i < mesh.blendShapeCount; i++) if (WindShape(mesh.GetBlendShapeName(i))) { keep.Add((i, smr.GetBlendShapeWeight(i))); smr.SetBlendShapeWeight(i, 0f); }
        return keep;
    }
    static void Restore(SkinnedMeshRenderer smr, List<(int, float)> keep) { foreach (var (i, w) in keep) smr.SetBlendShapeWeight(i, w); }

    static void Strip(string file, System.Func<string> next)
    {
        var sheet = new Texture2D(W * 6, H * 2, TextureFormat.RGB24, false);
        Color[] first = null; var legsPx = new bool[W * H]; var topPx = new bool[W * H];
        float legsSum = 0f, bodySum = 0f, flL = 0f, flT = 0f; int legsSeen = 0, topSeen = 0;
        Vector3[] prevL = null, prevT = null;
        int Diff(Color[] a, Color[] b, bool[] mark) { int n = 0; for (int p = 0; p < a.Length; p++) { var d = a[p] - b[p]; if (Mathf.Abs(d.r) + Mathf.Abs(d.g) + Mathf.Abs(d.b) > 0.12f) { n++; mark[p] = true; } } return n; }
        for (int i = 0; i < Frames; i++)
        {
            string what = next();
            SkinnedMeshRenderer legs = null, top = null;
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.name.StartsWith("HeroLegs")) legs = smr;
                if (smr.name.StartsWith("HeroBody")) top = smr;
            }
            if (legs != null && (prevL == null || prevL.Length != legs.sharedMesh.vertexCount)) prevL = new Vector3[legs.sharedMesh.vertexCount];
            if (top != null && (prevT == null || prevT.Length != top.sharedMesh.vertexCount)) prevT = new Vector3[top.sharedMesh.vertexCount];
            float fl = 0f, ft = 0f;
            var ml = legs != null ? Moved(legs, prevL, out fl) : (0f, 0f); var mb = top != null ? Moved(top, prevT, out ft) : (0f, 0f);
            legsSum += ml.Item1; bodySum += mb.Item1;
            if (i > 0) { flL += fl; flT += ft; }
            var px = Shoot();
            if (first == null) first = px;
            // the same instant with the trousers', then the jacket's, wind shapes at nought: the pixels each moves
            int nl = 0, nt = 0;
            if (legs != null) { var k = Zero(legs); var still = Shoot(); Restore(legs, k); nl = Diff(px, still, legsPx); }
            if (top != null) { var k = Zero(top); var still = Shoot(); Restore(top, k); nt = Diff(px, still, topPx); }
            legsSeen += nl; topSeen += nt;
            sheet.SetPixels((i % 6) * W, (1 - i / 6) * H, W, H, px);
            Log($"{Path.GetFileName(file)} frame {i:00} {what}: trousers moved by the wind {ml.Item1:0.0} cm (most {ml.Item2:0.0}), {nl} px on screen; jacket {mb.Item1:0.0} cm (most {mb.Item2:0.0}), {nt} px");
        }
        Log($"{Path.GetFileName(file)} MEAN trousers {legsSum / Frames:0.0} cm (changing {flL / (Frames - 1):0.0} cm a frame), {legsSeen / Frames} px a frame on screen; jacket {bodySum / Frames:0.0} cm (changing {flT / (Frames - 1):0.0} cm a frame), {topSeen / Frames} px");
        sheet.Apply(); File.WriteAllBytes(file + "_sheet.png", sheet.EncodeToPNG());
        // where the wind moves each on screen at any moment: the trousers yellow, the jacket cyan, over the first frame
        var map = new Texture2D(W, H, TextureFormat.RGB24, false); var mc = new Color[W * H];
        for (int p = 0; p < mc.Length; p++)
            mc[p] = legsPx[p] ? new Color(1f, 0.85f, 0.2f) : topPx[p] ? new Color(0.2f, 0.85f, 1f) : first[p] * 0.35f;
        map.SetPixels(mc); map.Apply(); File.WriteAllBytes(file + "_wind.png", map.EncodeToPNG());
        Object.DestroyImmediate(sheet); Object.DestroyImmediate(map);
    }

    /// <summary>The frame (skinned meshes baked first: batchmode does not skin them for a camera render).</summary>
    static Color[] Shoot()
    {
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m; g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            baked.Add(g); sk.enabled = false; hidden.Add(sk);
        }
        bool hideForest = props != null && props.gameObject.activeSelf;
        if (hideForest) props.gameObject.SetActive(false);
        cam.Render();
        if (hideForest) props.gameObject.SetActive(true);
        foreach (var sk in hidden) sk.enabled = true;
        foreach (var g in baked) { Object.DestroyImmediate(g.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(g); }
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        var px = tex.GetPixels();
        RenderTexture.active = null; cam.targetTexture = null; rt.Release();
        Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
        return px;
    }
}
