using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using Debug = UnityEngine.Debug;

/// <summary>
/// What terrain streaming, the course dressing and the snow trail cost the main thread as you ride (editor CPU, so a
/// guide to the phone, not its numbers), and whether they still build the same thing.
///
/// Run: a point rides down the piste at 30 m/s (weaving 30 m either side, so new columns are needed as well as new
/// rows) for 70 s of game time, one frame every 1/60 s of real time (so any background work gets the same wall clock it
/// would on a device); each frame the terrain, the course dressing and a snow trail fed the point are updated and timed.
/// Logs each one's worst frame, its 99th percentile, the frames over 2 and 4 ms, bytes allocated per frame, and how many
/// frames had a chunk missing inside the visible area.
///
/// Dump: writes every terrain chunk of a stretch of mountain (full resolution) and every mesh and collider of the first
/// course cycles to $STREAM_DUMP; with $STREAM_BASE set to an earlier dump, compares the two and logs the largest
/// difference in each part (vertices, normals, uvs, colours, triangles, tangents).
/// Batch: -executeMethod StreamPerf.Run / StreamPerf.Dump -logFile x.log
/// </summary>
public static class StreamPerf
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly double TickMs = 1000.0 / Stopwatch.Frequency;
    const int Seed = 2131;

    static (SummitGame game, SlopeStreamer terrain, CourseDressing dressing, Forest props) Boot()
    {
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        TestPrefs.Pin();
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        var terrain = game.terrain; var props = game.props; var dressing = Get<CourseDressing>("dressing");
        TestBoot.NewMountain(game, Seed, terrain, props);
        return (game, terrain, dressing, props);
    }

    public static void Run()
    {
        var (game, terrain, dressing, _) = Boot();
        PerfMarks.On = false;
        var point = new GameObject("StreamPerfPoint").transform;
        float z = 0f;
        point.position = new Vector3(Slope.Centre(z), Slope.Height(Slope.Centre(z), z), z);
        terrain.target = point; dressing.target = point;
        terrain.Rebuild(); dressing.Rebuild();
        var trail = new SnowTrail(Mats.LitShader);
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var dUpd = typeof(CourseDressing).GetMethod("Update", NP);
        var live = (Dictionary<Vector2Int, GameObject>)typeof(SlopeStreamer).GetField("live", NP).GetValue(terrain);
        // $STREAM_FAR: the streamer's farRows (999: every chunk at full resolution)
        if (int.TryParse(System.Environment.GetEnvironmentVariable("STREAM_FAR"), out int farRows)) typeof(SlopeStreamer).GetField("farRows")?.SetValue(terrain, farRows);

        // settle: let anything queued at the start finish (not counted)
        for (int i = 0; i < 120; i++) { tUpd.Invoke(terrain, null); dUpd.Invoke(dressing, null); System.Threading.Thread.Sleep(16); }

        var times = new Dictionary<string, List<double>> { ["terrain"] = new List<double>(), ["course"] = new List<double>(), ["trail"] = new List<double>(), ["all three"] = new List<double>() };
        var bytes = new Dictionary<string, long> { ["terrain"] = 0, ["course"] = 0, ["trail"] = 0 };
        int missingFrames = 0, missingMax = 0, frames = 0;
        const float Dt = 1f / 60f, Speed = 30f;
        long frameStart = Stopwatch.GetTimestamp();
        long frameTicks = (long)(Stopwatch.Frequency * Dt);
        for (float t = 0f; t < 70f; t += Dt, frames++)
        {
            z += Speed * Dt;
            float x = Slope.Centre(z) + 30f * Mathf.Sin(t * 0.35f);
            point.position = new Vector3(x, Slope.Height(x, z), z);
            double sum = 0;
            double Job(string job, System.Action a)
            {
                long b0 = System.GC.GetTotalMemory(false);
                long t0 = Stopwatch.GetTimestamp(); a(); double ms = (Stopwatch.GetTimestamp() - t0) * TickMs;
                bytes[job] += System.Math.Max(0L, System.GC.GetTotalMemory(false) - b0);   // (the editor's Mono counts no per-thread bytes; a collection mid-job reads as 0)
                times[job].Add(ms); return ms;
            }
            sum += Job("terrain", () => tUpd.Invoke(terrain, null));
            double cms = Job("course", () => dUpd.Invoke(dressing, null));
            sum += cms;
            if (cms > 20.0)
            {
                var builtNow = (Dictionary<int, GameObject>)typeof(CourseDressing).GetField("built", NP).GetValue(dressing);
                var ah = typeof(CourseDressing).GetField("ahead", NP)?.GetValue(dressing);
                Debug.Log($"STREAMPERF course frame of {cms:0.0} ms at z {z:0} (cycle {Course.CycleOf(z)}): built [{string.Join(",", builtNow.Keys)}], building ahead: {(ah == null ? "none" : ah.GetType().GetField("k").GetValue(ah).ToString())}");
            }
            sum += Job("trail", () => trail.Feed(point.position, Slope.Normal(x, z), 0.3f * Mathf.Cos(t * 0.35f), true, 1f, Mathf.Sin(t * 1.3f), 0.2f + 0.2f * Mathf.Sin(t), Dt));
            times["all three"].Add(sum);
            // chunks inside the visible area that are not there yet
            int cx = Mathf.FloorToInt(x / terrain.chunkSize), cz = Mathf.FloorToInt(z / terrain.chunkSize), miss = 0;
            for (int dz = -terrain.behindChunks; dz <= terrain.aheadChunks; dz++)
                for (int dx = -terrain.sideChunks; dx <= terrain.sideChunks; dx++)
                    if (!live.TryGetValue(new Vector2Int(cx + dx, cz + dz), out var go) || go == null || !go.activeSelf) miss++;
            if (miss > 0) missingFrames++;
            missingMax = Mathf.Max(missingMax, miss);
            // hold the frame to 1/60 s of real time
            frameStart += frameTicks;
            long now = Stopwatch.GetTimestamp();
            if (frameStart > now) System.Threading.Thread.Sleep((int)((frameStart - now) * TickMs));
            else frameStart = now;
        }
        foreach (var kv in times)
        {
            var l = kv.Value; l.Sort();
            double mean = 0; foreach (var v in l) mean += v; mean /= l.Count;
            int over2 = 0, over4 = 0; foreach (var v in l) { if (v > 2.0) over2++; if (v > 4.0) over4++; }
            string b = bytes.TryGetValue(kv.Key, out var bb) ? $", {bb / frames} B/frame" : "";
            Debug.Log($"STREAMPERF {kv.Key}: worst {l[l.Count - 1]:0.00} ms, p99 {l[(int)(l.Count * 0.99)]:0.00} ms, p95 {l[(int)(l.Count * 0.95)]:0.00} ms, mean {mean:0.000} ms, frames over 2 ms {over2}, over 4 ms {over4}{b}");
        }
        long tris = 0;
        foreach (var kv in live) if (kv.Value != null && kv.Value.activeSelf) { var m = kv.Value.GetComponent<MeshFilter>().sharedMesh; tris += (m.GetIndexCount(0) + m.GetIndexCount(1)) / 3; }
        Debug.Log($"STREAMPERF terrain triangles drawn at the end (snow and rock): {tris}");
        int far = 0;
        if (typeof(SlopeStreamer).GetField("lodOf", NP)?.GetValue(terrain) is Dictionary<Vector2Int, int> lodOf) foreach (var kv in lodOf) if (kv.Value > 0 && live.ContainsKey(kv.Key)) far++;
        Debug.Log($"STREAMPERF {frames} frames, rider z {z:0}: frames with a chunk missing in view {missingFrames} (most at once {missingMax}); {live.Count} chunks live, {far} at half resolution");
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>Each piece of a course cycle's build, timed (the most one frame of the cycle ahead can cost).</summary>
    public static void Pieces()
    {
        var (game, terrain, dressing, _) = Boot();
        var begin = typeof(CourseDressing).GetMethod("Begin", NP);
        var finish = typeof(CourseDressing).GetMethod("Finish", NP);
        for (int k = 1; k <= 4; k++)
        {
            var b = begin.Invoke(dressing, new object[] { k });
            var steps = (IEnumerator<bool>)b.GetType().GetField("steps").GetValue(b);
            var sb = new System.Text.StringBuilder();
            double total = 0;
            while (true)
            {
                long t0 = Stopwatch.GetTimestamp(); bool more = steps.MoveNext(); double ms = (Stopwatch.GetTimestamp() - t0) * TickMs;
                total += ms; sb.Append($" {ms:0.0}");
                if (!more) break;
            }
            long f0 = Stopwatch.GetTimestamp(); var root = (GameObject)finish.Invoke(dressing, new object[] { b }); double fm = (Stopwatch.GetTimestamp() - f0) * TickMs;
            Debug.Log($"STREAMPIECES cycle {k}: pieces (ms){sb}; colliders on {fm:0.00} ms; total {total + fm:0.0} ms");
            Object.DestroyImmediate(root);
        }
        long s0 = Stopwatch.GetTimestamp();
        var whole = (GameObject)typeof(CourseDressing).GetMethod("BuildCycle", NP).Invoke(dressing, new object[] { 2 });
        Debug.Log($"STREAMPIECES cycle 2 in one go: {(Stopwatch.GetTimestamp() - s0) * TickMs:0.0} ms");
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>
    /// The snow trail's own normals (worked out a row at a time) against Mesh.RecalculateNormals on the same mesh, after
    /// every point of a few hundred metres of carving and skidding. Batch: -executeMethod StreamPerf.Trail
    /// </summary>
    public static void Trail()
    {
        var trail = new SnowTrail(Mats.LitShader);
        var meshField = typeof(SnowTrail).GetField("currentMesh", NP);
        double worst = 0; int checks = 0;
        var p = Vector3.zero; float heading = 0f;
        for (int i = 0; i < 1500; i++)
        {
            float t = i / 60f;
            heading = 0.4f * Mathf.Sin(t * 0.7f);
            p += new Vector3(Mathf.Sin(heading), -0.3f, Mathf.Cos(heading)) * 0.5f;
            bool on = (i / 200) % 4 != 3;   // now and then off the snow (a jump), which starts a new piece
            var n = new Vector3(0.1f * Mathf.Sin(t), 1f, -0.3f).normalized;
            trail.Feed(p, n, heading, on, 1f + 0.3f * Mathf.Sin(t * 0.3f), Mathf.Sin(t * 1.3f), 0.5f + 0.5f * Mathf.Sin(t * 0.9f), 1f / 60f);
            var m = (Mesh)meshField.GetValue(trail);
            if (m == null || m.vertexCount == 0 || m.GetIndexCount(0) == 0) continue;
            var own = m.normals;
            var copy = Object.Instantiate(m);
            copy.RecalculateNormals();
            var unity = copy.normals;
            for (int k = 0; k < own.Length; k++) worst = System.Math.Max(worst, Vector3.Angle(own[k], unity[k]));
            Object.DestroyImmediate(copy);
            checks++;
        }
        Debug.Log($"STREAMTRAIL {checks} meshes checked: the trail's normals are at most {worst:0.0000} degrees off RecalculateNormals'");
        TestHarness.Exit(0);
    }

    // ---------------------------------------------------------------- does half resolution show?

    /// <summary>
    /// The chase view down the hill at a few places, with every chunk at full resolution and with the far rows at half
    /// (farRows from $LOOK_ROWS, default 5): StreamLook_{spot}_{full|half}.png in the project folder, and how many pixels
    /// differ. Batch: -executeMethod StreamPerf.Look (no -nographics).
    /// </summary>
    public static void Look()
    {
        var (game, terrain, dressing, props) = Boot();
        int rows = int.TryParse(System.Environment.GetEnvironmentVariable("LOOK_ROWS"), out var lr) ? lr : 5;
        var cam = Camera.main; var atmo = Object.FindFirstObjectByType<Atmosphere>();
        if (cam.TryGetComponent<ChaseCamera>(out var chase)) chase.enabled = false;
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP);
        var point = new GameObject("StreamLookPoint").transform;
        terrain.target = point; dressing.target = point; props.target = point;
        var farField = typeof(SlopeStreamer).GetField("farRows");
        // (a first shot to warm the lighting up: the very first render comes out darker)
        point.position = new Vector3(Slope.Centre(600f), Slope.Height(Slope.Centre(600f), 600f), 600f);
        terrain.Rebuild(); props.Prewarm(); dressing.Rebuild();
        Shoot(cam, atmo, "StreamLook_warm.png"); Shoot(cam, atmo, "StreamLook_warm.png");
        foreach (float z in new[] { 330f, 1250f, 1700f, 2400f, 3300f })
        {
            float x = Slope.Centre(z);
            point.position = new Vector3(x, Slope.Height(x, z), z);
            var shots = new List<Color32[]>();
            foreach (int far in new[] { 999, rows })
            {
                farField?.SetValue(terrain, far);
                terrain.Rebuild(); props.Prewarm(); dressing.Rebuild();
                // let every chunk in view (and the far ones' rebuilds) finish
                for (int i = 0; i < 400; i++) { tUpd.Invoke(terrain, null); System.Threading.Thread.Sleep(5); }
                // from up high, looking a long way down the hill, so the far rows fill much of the frame
                cam.transform.position = new Vector3(x, Slope.Height(x, z - 10f) + 30f, z - 10f);
                cam.transform.LookAt(new Vector3(Slope.Centre(z + 300f), Slope.Height(Slope.Centre(z + 300f), z + 300f), z + 300f));
                shots.Add(Shoot(cam, atmo, $"StreamLook_z{z:0}_{(far > 100 ? "full" : "half" + far)}.png"));
            }
            int differ = 0, big = 0; double sum = 0;
            for (int i = 0; i < shots[0].Length; i++)
            {
                var a = shots[0][i]; var b = shots[1][i];
                int d = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                if (d > 2) differ++; if (d > 12) big++; sum += d;
            }
            Debug.Log($"STREAMLOOK z {z:0}: farRows {rows} vs full: {differ} of {shots[0].Length} pixels differ by more than 2/255, {big} by more than 12/255, mean {sum / shots[0].Length:0.000}/255");
        }
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static Color32[] Shoot(Camera cam, Atmosphere atmo, string file)
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
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex); rt.Release();
        return px;
    }

    // ---------------------------------------------------------------- the same thing built?

    public static void Dump()
    {
        var (game, terrain, dressing, _) = Boot();
        string path = System.Environment.GetEnvironmentVariable("STREAM_DUMP");
        var parts = new List<(string name, float[] data)>();
        var mesh = new Mesh();
        // the new builder has a test hook; the old one is Fill(mesh, key)
        var hook = typeof(SlopeStreamer).GetMethod("BuildForTest", BindingFlags.Public | BindingFlags.Instance);
        var fill = typeof(SlopeStreamer).GetMethod("Fill", NP);
        for (int kz = -3; kz <= 40; kz++)
            for (int kx = -3; kx <= 3; kx++)
            {
                var key = new Vector2Int(kx + Mathf.FloorToInt(Slope.Centre(kz * 48f) / 48f), kz);
                if (hook != null) hook.Invoke(terrain, new object[] { mesh, key });
                else fill.Invoke(terrain, new object[] { mesh, key });
                AddMesh(parts, $"chunk {key.x},{key.y}", mesh);
            }
        for (int k = 0; k < 3; k++)
        {
            var go = (GameObject)typeof(CourseDressing).GetMethod("BuildCycle", NP).Invoke(dressing, new object[] { k });
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.name.StartsWith("Crevasse")) AddMesh(parts, Unique($"cycle {k} {Path(mf.transform)} mesh"), mf.sharedMesh);
            foreach (var mc in go.GetComponentsInChildren<MeshCollider>(true))
                if (mc.sharedMesh != null) { string nm = Unique($"cycle {k} {Path(mc.transform)} collider"); AddMesh(parts, nm, mc.sharedMesh); parts.Add((nm + " options", new float[] { (int)mc.cookingOptions, mc.convex ? 1 : 0, mc.gameObject.layer })); }
            var tr = new List<float>(); int count = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) { count++; tr.Add(t.position.x); tr.Add(t.position.y); tr.Add(t.position.z); tr.Add(t.lossyScale.x); tr.Add(t.lossyScale.y); tr.Add(t.lossyScale.z); }
            parts.Add(($"cycle {k} transforms ({count})", tr.ToArray()));
            int cols = 0; foreach (var c in go.GetComponentsInChildren<Collider>(true)) cols++;
            parts.Add(($"cycle {k} collider count", new float[] { cols }));
            Object.DestroyImmediate(go);
        }
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(parts.Count);
            foreach (var (name, data) in parts) { w.Write(name); w.Write(data.Length); foreach (var f in data) w.Write(f); }
        }
        Debug.Log($"STREAMDUMP wrote {parts.Count} parts to {path}");
        string basePath = System.Environment.GetEnvironmentVariable("STREAM_BASE");
        if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath)) Compare(basePath, parts);
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static string Path(Transform t) => t.parent != null && !t.parent.name.StartsWith("Course ") ? Path(t.parent) + "/" + t.name : t.name;
    static readonly Dictionary<string, int> seen = new Dictionary<string, int>();
    static string Unique(string name) { seen.TryGetValue(name, out int n); seen[name] = n + 1; return n == 0 ? name : name + " #" + n; }

    static void AddMesh(List<(string, float[])> parts, string name, Mesh m)
    {
        var l = new List<float>();
        foreach (var v in m.vertices) { l.Add(v.x); l.Add(v.y); l.Add(v.z); }
        parts.Add((name + " vertices", l.ToArray())); l.Clear();
        foreach (var v in m.normals) { l.Add(v.x); l.Add(v.y); l.Add(v.z); }
        parts.Add((name + " normals", l.ToArray())); l.Clear();
        foreach (var v in m.uv) { l.Add(v.x); l.Add(v.y); }
        parts.Add((name + " uvs", l.ToArray())); l.Clear();
        foreach (var v in m.colors) { l.Add(v.r); l.Add(v.g); l.Add(v.b); l.Add(v.a); }
        parts.Add((name + " colours", l.ToArray())); l.Clear();
        for (int s = 0; s < m.subMeshCount; s++) foreach (var i in m.GetTriangles(s)) l.Add(i + s * 1e7f);
        parts.Add((name + " triangles", l.ToArray())); l.Clear();
        foreach (var v in m.tangents) { l.Add(v.x); l.Add(v.y); l.Add(v.z); l.Add(v.w); }
        parts.Add((name + " tangents", l.ToArray()));
    }

    static void Compare(string basePath, List<(string name, float[] data)> now)
    {
        var old = new Dictionary<string, float[]>();
        using (var r = new BinaryReader(File.OpenRead(basePath)))
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) { string name = r.ReadString(); int len = r.ReadInt32(); var d = new float[len]; for (int j = 0; j < len; j++) d[j] = r.ReadSingle(); old[name] = d; }
        }
        var worst = new Dictionary<string, (double diff, string where)>();
        int missing = 0, sizeDiff = 0, same = 0, wFlip = 0;
        foreach (var (name, data) in now)
        {
            string kind = name.Contains(" #") ? name.Substring(0, name.LastIndexOf(" #")) : name;
            kind = kind.Substring(kind.LastIndexOf(' ') + 1);
            if (name.Contains("transforms") || name.Contains("count")) kind = name.Contains("transforms") ? "transforms" : "collider count";
            if (name.Contains("options")) kind = "collider options";
            kind = (name.StartsWith("chunk") ? "chunk " : "cycle ") + kind;
            if (!old.TryGetValue(name, out var o)) { missing++; if (missing <= 10) Debug.Log($"STREAMDUMP new part not in the base: {name}"); continue; }
            if (o.Length != data.Length) { sizeDiff++; if (sizeDiff <= 20) Debug.Log($"STREAMDUMP size differs: {name}: {o.Length} -> {data.Length}"); continue; }
            double d = 0;
            if (kind.EndsWith("tangents"))
            {
                // direction (xyz) and handedness (w) apart; corners no triangle uses are left out of the direction
                double dw = 0;
                for (int i = 0; i < data.Length; i++) { if (i % 4 == 3) { if (data[i] != o[i]) { dw++; wFlip++; } } else d = System.Math.Max(d, System.Math.Abs(data[i] - o[i])); }
                if (!worst.TryGetValue(kind + " w flipped", out var ww) || dw > ww.diff) worst[kind + " w flipped"] = (dw, name);
            }
            else for (int i = 0; i < data.Length; i++) d = System.Math.Max(d, System.Math.Abs(data[i] - o[i]));
            if (d == 0) same++;
            if (!worst.TryGetValue(kind, out var w) || d > w.diff) worst[kind] = (d, name);
        }
        foreach (var kv in worst) Debug.Log($"STREAMDUMP largest difference in {kv.Key}: {kv.Value.diff:G4} ({kv.Value.where})");
        Debug.Log($"STREAMDUMP compared {now.Count} parts with {old.Count} in the base: {same} identical, {sizeDiff} of a different size, {missing} not in the base; tangent handedness flipped at {wFlip} corners");
    }
}
