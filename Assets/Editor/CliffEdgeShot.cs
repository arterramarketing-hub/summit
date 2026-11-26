using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// treesound: what marks a cliff's edge, at phone size by day: the cliff band's bench (its one rope line on bamboo
/// poles, the crossed "closed" poles at its ends, the black wands at its low places) seen along the edge from up the
/// bench and from its far end, and a deadly wall's rope along the piste. Logs what stands along each edge.
///   Unity -batchmode -projectPath . -executeMethod CliffEdgeShot.Run -logFile x.log   (no -nographics: it renders)
/// Writes CliffEdge_*.png and CliffEdge_sheet.png into $CLIFF_OUT (or cliff-shots/).
/// </summary>
public static class CliffEdgeShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const int W = 590, H = 1278;
    const float Dt = 1f / 30f;
    static SummitGame game; static Camera cam; static Rider rider; static SlopeStreamer terrain; static Forest props;
    static DayNight dayNight; static Atmosphere atmo; static Weather weather; static CourseDressing dress; static PisteKit kit;
    static MethodInfo aLate;
    static string outDir;
    static readonly List<string> shots = new List<string>();

    public static void Run()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        outDir = System.Environment.GetEnvironmentVariable("CLIFF_OUT") ?? "cliff-shots";
        Directory.CreateDirectory(outDir);
        TestPrefs.Pin();
        ShaderUtil.allowAsyncCompilation = false;
        Weather.TestChoice = 1; TimeOfDay.TestClock = 1.4f;
        game = Object.FindFirstObjectByType<SummitGame>();
        try
        {
            TestBoot.Boot(game);
            T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
            rider = Get<Rider>("rider"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
            dayNight = Get<DayNight>("dayNight"); weather = Get<Weather>("weather"); dress = Get<CourseDressing>("dressing");
            cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>(); kit = PisteKit.Instance;
            aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
            Physics.simulationMode = SimulationMode.Script;
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            var chase = cam.GetComponent<ChaseCamera>(); if (chase != null) chase.enabled = false;
            Debug.Log($"CLIFFSHOT seed {Slope.Seed}: {PisteKit.Stats()}");

            bool band = false;
            for (int c = 0; c <= Course.CycleOf(Slope.FinishZ) && !band; c++)
            {
                if (!CliffBand.Of(c, out var b)) continue;
                // the band's rope poles, in order down the bench
                var poles = new List<PisteKit.Item>(); int closed = 0;
                foreach (var it in PisteKit.Items)
                {
                    if (it.foot.z < b.z1 - 20f || it.foot.z > b.z2 + 25f) continue;
                    if (it.kind == PisteKit.Kind.RopePole) poles.Add(it);
                    if (it.kind == PisteKit.Kind.Closed) closed++;
                }
                if (poles.Count < 4) continue;
                poles.Sort((p, q) => p.foot.z.CompareTo(q.foot.z));
                int tapes = 0; foreach (var t in Object.FindObjectsByType<CautionTape>(FindObjectsSortMode.None)) if (t.transform.position.z > b.z1 - 20f && t.transform.position.z < b.z2 + 25f) tapes++;
                Debug.Log($"CLIFFSHOT band cycle {c} (height {b.height:0} m): {poles.Count} rope poles, {closed} crossed-pole markers, {tapes} caution tapes along it");
                // a crossed pair at the line's start, and the rope going away from it along the lip: from up the bench,
                // at head height a few metres in from the rope
                PisteKit.Item x = poles[0]; float bestX = 1e9f;
                foreach (var it in PisteKit.Items)
                    if (it.kind == PisteKit.Kind.Closed && Mathf.Abs(it.foot.z - poles[0].foot.z) < bestX) { bestX = Mathf.Abs(it.foot.z - poles[0].foot.z); x = it; }
                float zx = x.foot.z;
                // from the air just off the lip, a little up the hill, looking back along the edge: the crossed pair, the
                // rope on its poles going away along the lip and the face falling away below it
                float zb = Mathf.Max(zx, poles[0].foot.z);
                Vector2 lip0 = CliffBand.PointAt(b, zb - 6f, CliffBand.EdgeAt(zb - 6f));
                Vector2 eD = CliffBand.PointAt(b, zb - 14f, CliffBand.EdgeAt(zb - 14f) + 7f);
                Vector2 aD = CliffBand.PointAt(b, zb + 14f, CliffBand.EdgeAt(zb + 14f) - 2.6f);
                float bench = Slope.DrawnHeight(lip0.x, lip0.y);
                Vector3 eyeD = new Vector3(eD.x, bench + 6f, eD.y);
                Spot("band_drone", new Vector3(lip0.x, 0f, lip0.y), eyeD, Ground(aD, -1f));
                // standing on the bench close to the crossed pair, the rope's first span beyond it
                Vector2 e3 = CliffBand.PointAt(b, zx - 6f, CliffBand.EdgeAt(zx - 6f) - 5f);
                Spot("band_marker", new Vector3(e3.x, 0f, e3.y), Ground(e3, 2.6f), x.foot + Vector3.up * 0.6f);
                // further along, the rope's middle from the air off the lip, side-on
                var mid = poles[Mathf.Min(poles.Count - 1, poles.Count / 2)];
                float zm = mid.foot.z;
                Vector2 e4 = CliffBand.PointAt(b, zm, CliffBand.EdgeAt(zm) + 9f);
                Vector3 eye4 = new Vector3(e4.x, mid.foot.y + 7f, e4.y);
                Spot("band_side", new Vector3(mid.foot.x, 0f, mid.foot.z), eye4, mid.foot + Vector3.up * 0.3f);
                band = true;
            }
            if (!band) Debug.LogWarning("CLIFFSHOT: no cliff band with a rope line on this mountain");
            for (int c = 0; c <= Course.CycleOf(Slope.FinishZ); c++)
            {
                if (!Cliffs.WallOf(c, out var w)) continue;
                int poles = 0, closed = 0;
                foreach (var it in PisteKit.Items)
                {
                    if (it.foot.z < w.z0 - 25f || it.foot.z > w.z2 || Mathf.Sign(it.foot.x - Slope.Centre(it.foot.z)) != w.side) continue;
                    if (it.kind == PisteKit.Kind.RopePole) poles++;
                    if (it.kind == PisteKit.Kind.Closed) closed++;
                }
                Debug.Log($"CLIFFSHOT wall cycle {c}: {poles} rope poles, {closed} crossed-pole markers along the piste on its side");
                float z = w.z0 + 10f;
                Vector3 eye = new Vector3(Slope.Centre(z - 14f) + w.side * (Slope.PisteHalf - 5f), 0f, z - 14f);
                Vector3 at = new Vector3(Slope.Centre(z + 12f) + w.side * (w.edge + 2f), 0f, z + 12f);
                Spot("wall", eye, Ground(new Vector2(eye.x, eye.z), 1.7f), Ground(new Vector2(at.x, at.z), -1f));
                break;
            }
            Sheet("CliffEdge_sheet.png");
        }
        catch (System.Exception e) { Debug.LogError("CLIFFSHOT exception " + e); }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static Vector3 Ground(Vector2 p, float up) => new Vector3(p.x, Slope.DrawnHeight(p.x, p.y) + up, p.y);

    static void Spot(string name, Vector3 riderAt, Vector3 eye, Vector3 at)
    {
        rider.ResetAt(riderAt.x, riderAt.z); rider.Begin();
        rider.Body.linearVelocity = Vector3.zero;
        rider.transform.position += Vector3.down * 200f;   // (out of the picture)
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); kit.Prewarm();
        for (float t = 0f; t < 1.5f; t += Dt)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            TreeWind.Advance(Dt, eye); weather.Step(Dt); aLate.Invoke(atmo, null); dayNight.ApplyNow();
        }
        foreach (var t in Object.FindObjectsByType<CautionTape>(FindObjectsSortMode.None)) typeof(CautionTape).GetMethod("LateUpdate", NP)?.Invoke(t, null);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
        cam.fieldOfView = 62f;
        typeof(Forest).GetMethod("Draw", NP | BindingFlags.Public)?.Invoke(props, null);
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        string file = $"CliffEdge_{name}.png";
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
        Object.DestroyImmediate(tex);
        shots.Add(file);
        Debug.Log($"CLIFFSHOT wrote {file}: eye {eye} looking at {at}");
    }

    static void Sheet(string file)
    {
        if (shots.Count == 0) return;
        int w = W / 2, h = H / 2;
        var sheet = new Texture2D(w * shots.Count, h, TextureFormat.RGB24, false);
        for (int k = 0; k < shots.Count; k++)
        {
            var t = new Texture2D(2, 2);
            if (!t.LoadImage(File.ReadAllBytes(Path.Combine(outDir, shots[k])))) continue;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    sheet.SetPixel(k * w + x, y, t.GetPixel(x * 2, y * 2));
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), sheet.EncodeToPNG());
        Debug.Log($"CLIFFSHOT wrote {file}");
    }
}
