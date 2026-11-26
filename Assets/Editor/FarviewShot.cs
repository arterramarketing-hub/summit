using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Pop-in check for the far view (Farview.cs): rides a clear midday run down the mountain at the phone's quality, a
/// chase-camera view each frame, and counts what appears or vanishes in a single frame.
///
/// Each frame is rendered twice from the same camera at the same moment: first with the world as it was last frame
/// (A), then after everything that streams has had its frame (B: the terrain, the forest, the course, the hamlets,
/// the lifts, the far field). Any difference between A and B is the streaming's own doing, not the camera's. A
/// pixel changed by more than 24/255 counts only where a whole 2x2 block changed (a fade flips scattered pixels); a
/// frame with 3 such blocks or more is a pop frame, and a pop event is a pop frame after one without.
/// Something let in through the band changes nothing in a frame (the band moves with the rider, and A already has it
/// where B does), so what this counts is only what appears or goes at once.
///
/// Run (graphics: never -nographics; no -quit, it steps one editor frame at a time and quits by itself):
///   FARVIEW_TAG=after FARVIEW_OUT=dir Unity -batchmode -projectPath . -executeMethod FarviewShot.Run -logFile x.log
///   FARVIEW_OFF=1   everything as it was before the band (the "before")
///   FARVIEW_SECONDS ride length (60)       FARVIEW_SEED mountain (2131)      FARVIEW_STRIP=1 save a frame strip
///   FARVIEW_PROBE_T=28   at that moment also the far field alone, the terrain alone, and all but the far field
/// Writes {tag}_still_*.png (every 5 s of the ride, and a high view), {tag}_strip_*.png (t 20..23 s), {tag}_event_*.png
/// and {tag}_pop_*.png (each pop event's first frame, and the worst: A, B and what changed in red) and FARVIEW lines
/// in the log: pop events, pop frames and popped tiles per minute, and what the main view draws (draws, triangles).
/// </summary>
public static class FarviewShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 393, H = 852;
    const float Dt = 1f / 30f, Speed = 30f, Fov = 86f;

    static SummitGame game; static Rider rider; static SlopeStreamer terrain; static Forest props; static CourseDressing dress;
    static Hamlets hamlets; static SkiLift lift; static Atmosphere atmo; static DayNight dayNight; static Farview farview; static Camera cam;
    static MethodInfo tUpd, fUpd, fDraw, dUpd, hUpd, lUpd, lDraw, aLate;
    static string tag, outDir;
    static float seconds;
    static bool off, strip;
    static RenderTexture rt; static Texture2D tex;
    static Color32[] shotA;
    static int quality;

    // the ride
    static float t, z0;
    static int step, pops, popTiles, farPops, frames;
    static readonly List<(int tiles, float t, int frame)> worst = new List<(int, float, int)>();
    static readonly Dictionary<int, (Color32[] a, Color32[] b)> keep = new Dictionary<int, (Color32[], Color32[])>();
    static long drawSum, triSum, setPassSum; static int statFrames;
    static Unity.Profiling.ProfilerRecorder drawRec, triRec, passRec;

    public static void Run()
    {
        tag = System.Environment.GetEnvironmentVariable("FARVIEW_TAG") ?? "farview";
        outDir = System.Environment.GetEnvironmentVariable("FARVIEW_OUT") ?? ".";
        off = System.Environment.GetEnvironmentVariable("FARVIEW_OFF") == "1";
        strip = System.Environment.GetEnvironmentVariable("FARVIEW_STRIP") == "1";
        seconds = float.TryParse(System.Environment.GetEnvironmentVariable("FARVIEW_SECONDS"), out var sc) ? sc : 60f;
        int seed = int.TryParse(System.Environment.GetEnvironmentVariable("FARVIEW_SEED"), out int sd) ? sd : 2131;
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); SummitImport.EnsureRealTrees();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        quality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(0, true);   // the phone's
        PlayerPrefs.SetInt(Forest.TreesPref, 1);
        Farview.TestOn = !off; Farview.Disabled = off; Farview.TestClock = 0.0;
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Rider>("rider"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props"); dayNight = Get<DayNight>("dayNight");
        dress = Object.FindFirstObjectByType<CourseDressing>(); hamlets = Object.FindFirstObjectByType<Hamlets>();
        lift = Object.FindFirstObjectByType<SkiLift>(); atmo = Object.FindFirstObjectByType<Atmosphere>();
        farview = Farview.Instance;
        cam = Camera.main;
        var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); fDraw = typeof(Forest).GetMethod("Draw", NP);
        dUpd = typeof(CourseDressing).GetMethod("Update", NP); hUpd = typeof(Hamlets).GetMethod("Update", NP);
        lUpd = typeof(SkiLift).GetMethod("Update", NP); lDraw = typeof(SkiLift).GetMethod("Draw", NP);
        aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        Physics.simulationMode = SimulationMode.Script;
        typeof(SummitGame).GetMethod("ToRiding", NP)?.Invoke(game, null);
        rider.input = null;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Clear();
        TestBoot.NewMountain(game, seed, terrain, props);
        SkiLift.TestClock = 100.0;
        LiftRiders.On = false;   // (the people on the chairs animate by the editor's clock: they would show as changes between A and B)
        TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        rt = new RenderTexture(W, H, 24) { antiAliasing = 1 };
        tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        drawRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
        triRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
        passRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
        z0 = Slope.StartZ + 40f;
        Place(0f);
        terrain.Rebuild(); props.Rebuild(); if (dress != null) dress.Rebuild();
        terrain.Prewarm(); props.Prewarm(); if (dress != null) dress.Prewarm();
        if (farview != null && farview.far != null) farview.far.Step(true);
        Log($"start: {(off ? "band OFF (before)" : "band ON")}, seed {seed}, {seconds:0} s at {Speed} m/s, {W}x{H}");
        if (farview != null && farview.far != null)
        {
            var fr = farview.far.GetComponent<MeshRenderer>(); var fm = farview.far.GetComponent<MeshFilter>().sharedMesh;
            Log($"far field: active {farview.far.gameObject.activeInHierarchy}, renderer {fr.enabled}, shader {fr.sharedMaterial?.shader?.name} supported {fr.sharedMaterial?.shader?.isSupported}, mesh {(fm != null ? fm.vertexCount : -1)} verts, bounds {fr.bounds}, rider {rider.transform.position}, On {Farview.On}");
        }
        var chunk = terrain.GetComponentInChildren<MeshRenderer>();
        if (chunk != null) foreach (var m in chunk.sharedMaterials) Log($"chunk material {m.name}: {m.shader.name} supported {m.shader.isSupported} keys [{string.Join(",", m.shaderKeywords)}] queue {m.renderQueue}");
        Log($"globals: centre {Shader.GetGlobalVector("_FarviewCentre")} band {Shader.GetGlobalVector("_FarviewBand")}");
        step = -150;   // (settling: the streaming catches up with the start, the first renders warm up)
        EditorApplication.update += Tick;
    }

    static void Log(string s) => Debug.Log("FARVIEW " + tag + " " + s);

    static Vector3 PathAt(float tt)
    {
        float z = z0 + Speed * tt, x = Slope.Centre(z) + 30f * Mathf.Sin(tt * 0.35f);
        return new Vector3(x, Slope.Height(x, z), z);
    }

    /// <summary>The rider at time tt, and a chase camera behind and above, looking down the line of travel.</summary>
    static void Place(float tt)
    {
        Vector3 p = PathAt(tt), q = PathAt(tt + 0.5f);
        var h = q - p; h.y = 0f; h.Normalize();
        rider.transform.position = p + Vector3.up * 0.3f;
        Vector3 eye = p - h * 9f; eye.y = Mathf.Max(eye.y, Slope.Height(eye.x, eye.z)) + 4.6f;
        Vector3 look = p + h * 30f; look.y = Slope.Height(look.x, look.z) + 1.5f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        cam.fieldOfView = Fov;
        Farview.TestClock = tt;
    }

    static void Updates()
    {
        tUpd.Invoke(terrain, null);
        fUpd.Invoke(props, null);   // (draws too)
        if (dress != null) dUpd.Invoke(dress, null);
        if (hamlets != null) hUpd.Invoke(hamlets, null);
        if (lift != null) lUpd.Invoke(lift, null);   // (draws too)
        if (farview != null) { farview.Apply(); farview.Step(); if (farview.far != null) farview.far.Step(false); }
        Talus();
    }

    static void DrawsOnly()
    {
        fDraw.Invoke(props, null);
        if (lift != null) lDraw.Invoke(lift, null);
        Talus();
    }

    static void Talus()
    {
        var tt = typeof(CliffSpires).GetNestedType("TalusDraw", BindingFlags.NonPublic);
        if (tt == null) return;
        var list = tt.GetField("live", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IList;
        var draw = tt.GetMethod("Draw");
        if (list != null) foreach (var o in list) draw.Invoke(o, null);
    }

    static Color32[] Shoot(string file = null)
    {
        DynamicGI.UpdateEnvironment();
        if (atmo != null) aLate.Invoke(atmo, null);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        if (file != null) File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        return tex.GetPixels32();
    }

    static void Tick()
    {
        try { Advance(); }
        catch (System.Exception e) { Debug.LogError("FARVIEW exception " + e); Finish(); }
    }

    // one editor frame: A (old world, new camera) on even steps, B (the world's frame) on odd ones, so the instanced
    // draws queued for one are flushed before the other
    static void Advance()
    {
        if (step < 0)
        {
            Place(0f);
            Updates();
            System.Threading.Thread.Sleep(8);
            Shoot();
            step++;
            if (step == 0)
            {
                // a still from high above, looking a long way down the hill, before the ride
                var p = PathAt(0f);
                var eye = p + new Vector3(0f, 60f, -20f);
                var look = PathAt(14f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                Shoot(); Shoot($"{tag}_still_high.png");
                Place(0f);
            }
            return;
        }
        bool isA = (step & 1) == 0;
        if (isA)
        {
            t = (step / 2) * Dt;
            if (t > seconds) { Finish(); return; }
            Place(t);
            if (farview != null) farview.Apply();
            DrawsOnly();
            shotA = Shoot();
        }
        else
        {
            Updates();
            var b = Shoot();
            frames++;
            Stats();
            Compare(shotA, b);
            int f = step / 2;
            if (f % 150 == 75) File.WriteAllBytes(Path.Combine(outDir, $"{tag}_still_t{t:00}.png"), tex.EncodeToPNG());
            if (probeT >= 0f && Mathf.Abs(t - probeT) < Dt * 0.5f) ProbeNow();
            if (strip && t >= 20f && t < 23f && f % 3 == 0) File.WriteAllBytes(Path.Combine(outDir, $"{tag}_strip_{f:0000}.png"), tex.EncodeToPNG());
            // (the background builds get a frame's worth of real time, as on the phone)
            System.Threading.Thread.Sleep(10);
        }
        step++;
    }

    static float probeT = float.TryParse(System.Environment.GetEnvironmentVariable("FARVIEW_PROBE_T"), out var pt) ? pt : -1f;

    /// <summary>The same frame again: the far field alone, the terrain alone, and everything without the band.</summary>
    static void ProbeNow()
    {
        int mask = cam.cullingMask;
        var far = farview != null ? farview.far : null;
        if (far != null) { far.gameObject.layer = 30; cam.cullingMask = 1 << 30; Shoot($"{tag}_probe_far.png"); far.gameObject.layer = 0; }
        foreach (Transform c in terrain.transform) c.gameObject.layer = 31;
        cam.cullingMask = 1 << 31; Shoot($"{tag}_probe_terrain.png");
        foreach (Transform c in terrain.transform) c.gameObject.layer = 0;
        cam.cullingMask = mask;
        if (far != null) { far.gameObject.SetActive(false); Shoot($"{tag}_probe_nofar.png"); far.gameObject.SetActive(true); }
    }

    /// <summary>What the main view asks to draw this frame (no shadow passes): every renderer in the camera's
    /// frustum, a draw per material, and the forest's and the lifts' instanced batches.</summary>
    static void Count(out int draws, out long tris)
    {
        draws = 0; tris = 0;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || (cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
            if (lift != null && r.transform.IsChildOf(lift.transform)) continue;   // (the lifts count their own: below)
            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            var m = mf.sharedMesh;
            for (int i = 0; i < m.subMeshCount; i++) { draws++; tris += m.GetIndexCount(i) / 3; }
        }
        props.Stats(out int fd, out long ft, out _, out _, out _); draws += fd; tris += ft;
        if (lift != null) { lift.Stats(out int ld, out long lt, out _); draws += ld; tris += lt; }
    }
    static long countDraws, countTris; static int counted;

    static void Stats()
    {
        if (frames % 30 == 15) { Count(out int d, out long tr); countDraws += d; countTris += tr; counted++; }
        if (drawRec.Valid && drawRec.LastValue > 0) { drawSum += drawRec.LastValue; triSum += triRec.LastValue; setPassSum += passRec.LastValue; statFrames++; }
        else if (UnityStats.drawCalls > 0) { drawSum += UnityStats.drawCalls; triSum += UnityStats.triangles; setPassSum += UnityStats.setPassCalls; statFrames++; }
    }

    static bool[] changed;

    // A pixel that changed by more than 24/255 is part of a pop only if the 2x2 block it heads changed whole: a fade
    // in progress flips pixels one at a time in a scattered pattern (a dither), a thing appearing or vanishing
    // changes them all at once. A 12x12 tile holding such a block has popped; a frame with 3 blocks or more is a pop.
    static void Compare(Color32[] a, Color32[] b)
    {
        const int T = 12, D = 24;
        if (changed == null) changed = new bool[W * H];
        for (int k = 0; k < W * H; k++)
        {
            var p = a[k]; var q = b[k];
            changed[k] = Mathf.Abs(p.r - q.r) > D || Mathf.Abs(p.g - q.g) > D || Mathf.Abs(p.b - q.b) > D;
        }
        int blocks = 0, farBlocks = 0;
        var tileHit = new HashSet<int>();
        for (int y = 0; y < H - 1; y++)
            for (int x = 0; x < W - 1; x++)
            {
                int k = y * W + x;
                if (!changed[k] || !changed[k + 1] || !changed[k + W] || !changed[k + W + 1]) continue;
                blocks++; if (y > H * 0.45f) farBlocks++;   // (rows count from the bottom: the upper part is the distance)
                tileHit.Add((y / T) * (W / T + 1) + x / T);
            }
        int f = step / 2;
        if (blocks < 3) return;
        pops++; popTiles += tileHit.Count; if (farBlocks >= 3) farPops++;
        // a pop event: a pop frame after one without (a fade under way, by its own clock, gives a run of them)
        if (f != lastPopFrame + 1) { events++; if (blocks >= 10 && saved < 40) { saved++; Save(f, a, b); } }
        lastPopFrame = f;
        worst.Add((tileHit.Count, t, f));
        worst.Sort((x, y) => y.tiles.CompareTo(x.tiles));
        if (worst.Count > 6) { int dropF = worst[6].frame; worst.RemoveAt(6); keep.Remove(dropF); }
        if (worst.Exists(w => w.frame == f)) keep[f] = (a, b);
        if (popLog.Count < 400) popLog.Add($"{t:0.00}:{blocks}{(farBlocks >= 3 ? "f" : "")}");
    }
    static readonly List<string> popLog = new List<string>();
    static int lastPopFrame = -10, events, saved;

    static void Save(int f, Color32[] a, Color32[] b)
    {
        var img = new Texture2D(W * 3, H, TextureFormat.RGB24, false);
        var px = new Color32[W * 3 * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = a[y * W + x]; var q = b[y * W + x];
                px[y * W * 3 + x] = p; px[y * W * 3 + W + x] = q;
                bool d = Mathf.Abs(p.r - q.r) > 24 || Mathf.Abs(p.g - q.g) > 24 || Mathf.Abs(p.b - q.b) > 24;
                px[y * W * 3 + 2 * W + x] = d ? new Color32(255, 0, 0, 255) : new Color32((byte)(q.r / 3), (byte)(q.g / 3), (byte)(q.b / 3), 255);
            }
        img.SetPixels32(px); img.Apply();
        File.WriteAllBytes(Path.Combine(outDir, $"{tag}_event_{f:0000}.png"), img.EncodeToPNG());
        Object.DestroyImmediate(img);
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        float minutes = Mathf.Max(frames * Dt, 1e-3f) / 60f;
        Log($"ride: {frames} frames ({frames * Dt:0.0} s): pop events {events} ({events / minutes:0.0}/min); pop frames {pops} ({pops / minutes:0.0}/min), of them with a pop in the far part {farPops} ({farPops / minutes:0.0}/min), popped tiles {popTiles} ({popTiles / minutes:0}/min)");
        foreach (var w in worst) Log($"  worst: t {w.t:0.00} s, {w.tiles} tiles");
        Log("each pop (t: blocks, f = in the far part): " + string.Join(" ", popLog));
        if (statFrames > 0) Log($"render (per frame, mean): {drawSum / statFrames} draw calls, {setPassSum / statFrames} set-pass calls, {triSum / statFrames} triangles ({statFrames} frames measured)");
        else Log("render: no render counters in this editor");
        if (counted > 0) Log($"main view (counted, mean of {counted} frames a second apart, no shadow passes): {countDraws / counted} draws, {countTris / counted} triangles");
        terrain.BandStats(out int edge, out int hidden, out int handing);
        Log($"terrain at the end: {edge} chunks with the band, {hidden} past it (not drawn); far field {(farview != null && farview.far != null ? farview.far.Triangles : 0)} triangles");
        foreach (var kv in keep)
        {
            var img = new Texture2D(W * 3, H, TextureFormat.RGB24, false);
            var px = new Color32[W * 3 * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = kv.Value.a[y * W + x]; var q = kv.Value.b[y * W + x];
                    px[y * W * 3 + x] = p; px[y * W * 3 + W + x] = q;
                    bool d = Mathf.Abs(p.r - q.r) > 24 || Mathf.Abs(p.g - q.g) > 24 || Mathf.Abs(p.b - q.b) > 24;   // (every changed pixel shown)
                    px[y * W * 3 + 2 * W + x] = d ? new Color32(255, 0, 0, 255) : new Color32((byte)(q.r / 3), (byte)(q.g / 3), (byte)(q.b / 3), 255);
                }
            img.SetPixels32(px); img.Apply();
            File.WriteAllBytes(Path.Combine(outDir, $"{tag}_pop_{kv.Key:0000}.png"), img.EncodeToPNG());
            Object.DestroyImmediate(img);
        }
        drawRec.Dispose(); triRec.Dispose(); passRec.Dispose();
        SkiLift.TestClock = -1.0; LiftRiders.On = true;
        TimeOfDay.TestClock = 1f + TimeOfDay.HeldAt; dayNight.ApplyNow(); TimeOfDay.TestClock = -1f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Farview.TestOn = false; Farview.Disabled = false; Farview.TestClock = -1.0;
        QualitySettings.SetQualityLevel(quality, false);
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }
}
