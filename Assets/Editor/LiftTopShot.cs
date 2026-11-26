using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The Summit lift's top (LiftTop; SUMMIT_LIFTTOP=none draws it as it was before lifttop), drawn from the same places every time so the versions can be compared: arriving on a chair; standing at
/// the unload; the station and its slope from the side; looking down into the camp from the top; the top from the
/// camp; from high above the camp's front; and straight down. The lift and its unload run a while first, so its chairs
/// carry people and some have come off and ride in.
/// Batch (graphics): -executeMethod LiftTopShot.Run; SUMMIT_LIFTTOP_OUT: the folder (LiftTopShots). Logs the top's
/// numbers (LIFTTOP lines).
/// </summary>
public static class LiftTopShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static SummitGame game; static Camp camp; static Camera cam;
    static SlopeStreamer terrain; static Forest props; static Atmosphere atmo; static DayNight dayNight;
    static MethodInfo tUpd, fUpd, aLate, lUpd;
    static SkiLift lift;
    static string outDir, tag;
    static readonly List<System.Action> steps = new List<System.Action>();
    static int at;
    const float dt = 1f / 60f;

    public static void Run()
    {
        string v = (System.Environment.GetEnvironmentVariable("SUMMIT_LIFTTOP") ?? "").Trim().ToLowerInvariant();
        LiftTop.On = v != "none" && v != "off" && v != "before";
        tag = System.Environment.GetEnvironmentVariable("SUMMIT_LIFTTOP_TAG") ?? (LiftTop.On ? "after" : "before");
        outDir = System.Environment.GetEnvironmentVariable("SUMMIT_LIFTTOP_OUT") ?? "LiftTopShots";
        Directory.CreateDirectory(outDir);
        SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        QualitySettings.SetQualityLevel(0, true);
        TestPrefs.Pin();
        game = Object.FindFirstObjectByType<SummitGame>();
        TestBoot.Boot(game, false);
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        camp = Get<Camp>("camp"); terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props"); dayNight = Get<DayNight>("dayNight");
        cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>();
        tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
        lift = Object.FindFirstObjectByType<SkiLift>(); lUpd = typeof(SkiLift).GetMethod("Update", NP);
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
        if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
        ChaseCamera.TestDt = dt;
        steps.Clear(); at = 0;
        Shots();
        steps.Add(() => { ChaseCamera.TestDt = 0f; TimeOfDay.TestClock = -1f; SkiLift.TestClock = -1.0; TestPrefs.Restore(); });
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (at < steps.Count)
        {
            try { steps[at++](); }
            catch (System.Exception e) { Debug.LogError("LIFTTOP exception " + e); at = steps.Count; }
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    static void Log(string s) => Debug.Log("LIFTTOP " + s);

    static LiftLine.Lift top;
    static Vector3 st, dir, side;
    static List<Vector3> path;
    static float lapClock = 400.0f;

    static void Shots()
    {
        steps.Add(() =>
        {
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow(); camp.SetNight(0f);
            foreach (var l in LiftLine.Lifts) if (l.summit) top = l;
            if (top == null) { Log("no Summit lift"); at = steps.Count; return; }
            float cx = Slope.Centre(Slope.PadLip);
            st = top.top; dir = top.dir;
            float a0 = st.x - Slope.Centre(st.z);
            side = top.across * (Vector3.Dot(top.across, Vector3.right) * (a0 > 0f ? -1f : 1f) >= 0f ? 1f : -1f);
            if (side.x < 0f) side = -side;   // (toward the camp: + x)
            path = LiftEnds.SummitPath();
            float plateau = Slope.PlateauAt(st.z);
            Log($"{tag}: station at ({st.x - cx:0.0}, {st.z:0.0}), deck {st.y - plateau:+0.0;-0.0} m over the plateau there; the line to ({top.bottom.x - cx:0}, {top.bottom.z:0}), {top.tries} tries; {LiftTop.Describe()}");
            if (path.Count > 1)
            {
                float len = 0f; for (int i = 1; i < path.Count; i++) len += Vector2.Distance(new Vector2(path[i - 1].x, path[i - 1].z), new Vector2(path[i].x, path[i].z));
                Log($"{tag}: the way in, {path.Count} points, {len:0} m, from ({path[0].x - cx:0.0}, {path[0].z:0.0}) at {path[0].y - plateau:+0.0;-0.0} m to ({path[path.Count - 1].x - cx:0.0}, {path[path.Count - 1].z:0.0}) at {path[path.Count - 1].y - Slope.PlateauAt(path[path.Count - 1].z):+0.0;-0.0} m");
            }
            // the station's deck against the snow round it: how far down the drop is off its open sides and ends
            var sb = new System.Text.StringBuilder();
            foreach (var (u, v, name) in new[] { (7f, 0f, "camp side"), (-7f, 0f, "outer side"), (0f, -LiftLine.StationHalfLen - 3f, "uphill end"), (0f, LiftLine.StationHalfLen + 3f, "downhill end"), (12f, -LiftLine.StationHalfLen - 6f, "unload flat") })
            {
                var p = st + side * u + dir * v;
                sb.Append($"{name} {Slope.DrawnHeight(p.x, p.z) - st.y:+0.0;-0.0} m; ");
            }
            Log($"{tag}: the snow beside the deck: {sb}");
            var prof = new System.Text.StringBuilder();
            foreach (var (ps, ph, pn) in LiftTop.FarProfile()) prof.Append($"{ps:0}:{ph:0.0}({pn:0.0}) ");
            Log($"{tag}: the far side's ground, metres along: height over the lip (the mountain's own): {prof}");
            if (System.Environment.GetEnvironmentVariable("SUMMIT_LIFTTOP_GRID") == "1") Grid();
        });
        // the lift and its ends, running: chairs full, people coming off and riding in
        steps.Add(() =>
        {
            if (top == null) return;
            Run(70f, st + side * 6f);
        });
        // 1. arriving on a chair: seated on one coming up the last span before the station, looking up the line at it
        // and the camp beyond
        steps.Add(() =>
        {
            if (top == null) return;
            int span = top.nodeS.Length > 2 && top.nodeS[1] - top.nodeS[0] < 14f ? 1 : 0;
            float s0 = Mathf.Lerp(top.nodeS[span], top.nodeS[span + 1], span == 0 ? 0.6f : 0.35f);
            // (on the line's middle, between the chairs going up and those coming down: a seated rider's eye height
            // under the cable, clear of every chair)
            var eye = top.At(s0, 0f, top.CableY(span, s0) - 2.3f);
            Place(st + dir * 60f, 0f);
            Frame("1-arrive-chair", eye, st - dir * 10f + side * 8f + Vector3.up * 1.5f, 62f, true);
        });
        // 2. at the unload: standing on its flat, looking along the way in
        steps.Add(() =>
        {
            if (top == null || path.Count < 4) return;
            var me = path[Mathf.Min(1, path.Count - 1)];
            var fwd = path[Mathf.Min(6, path.Count - 1)] - me; fwd.y = 0f; fwd.Normalize();
            Place(me, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
            Frame("2-unload", me - fwd * 5.5f + Vector3.up * 3f, me + fwd * 22f + Vector3.down * 1.5f, 62f, true, false);
        });
        // 3. the station and its slope from the side: out over the shoulder, looking across at it with the camp beyond
        steps.Add(() =>
        {
            if (top == null) return;
            var eye = st - side * 58f + dir * 14f; eye.y = Mathf.Max(Slope.DrawnHeight(eye.x, eye.z) + 4f, st.y + 6f);
            Place(st + side * 60f, 0f);
            Frame("3-side", eye, st + side * 14f + Vector3.up * 0.5f, 50f, false);
        });
        // 4. looking down into the camp from the top: at the pad's edge where the way in leaves it, eye height
        steps.Add(() =>
        {
            if (top == null || path.Count < 4) return;
            var me = path[Mathf.Min(3, path.Count - 1)];
            float cx = Slope.Centre(Slope.PadLip);
            var to = new Vector3(cx - 2f, Slope.DrawnHeight(cx - 2f, -66f), -66f);
            Place(me - (to - me).normalized * 6f, 0f);
            Frame("4-down-into-camp", me + Vector3.up * 1.7f, to + Vector3.up * 0.5f, 55f, false);
        });
        // 5. the top from the camp: from beside the fire, looking back up at it
        steps.Add(() =>
        {
            if (top == null) return;
            float cx = Slope.Centre(Slope.PadLip);
            var eye = new Vector3(cx + 4f, 0f, -62f); eye.y = Slope.DrawnHeight(eye.x, eye.z) + 1.7f;
            Place(eye + Vector3.right * 4f, 0f);
            Frame("5-from-camp", eye, st + side * 4f + Vector3.up * 2f, 55f, false);
        });
        // 6. from high above the camp's front left: the top, its way in, the lodge, the plaza
        steps.Add(() =>
        {
            if (top == null) return;
            float cx = Slope.Centre(Slope.PadLip);
            var eye = new Vector3(cx + 6f, Slope.PlateauAt(-60f) + 42f, -30f);
            Place(new Vector3(cx + 10f, 0f, -50f), 0f);
            Frame("6-overview", eye, new Vector3(cx - 30f, Slope.PlateauAt(-88f), -88f), 50f, false);
        });
        // 8. the split: from above the station's uphill end, down the line: the share riding into the camp (right), the
        // rest round behind the station and off down the far side (left)
        steps.Add(() =>
        {
            if (top == null) return;
            var eye = st - dir * 20f + Vector3.up * 13f - side * 3f;
            Place(st - dir * 40f, 0f);
            Frame("8-split", eye, st + dir * 22f + side * 6f, 64f, false);
            Split();
        });
        // 9-12. the far side, where those who don't ride into the camp go: from the camp's left edge, from a chair on the
        // line below, from the run below the lip, and from out over the shoulder
        steps.Add(() =>
        {
            if (top == null) return;
            var eye = G(-24f, -82f) + Vector3.up * 1.7f;
            Place(G(-10f, -70f), 0f);
            Frame("9-far-from-camp", eye, G(-78f, -40f) + Vector3.down * 4f, 60f, false);
        });
        steps.Add(() =>
        {
            if (top == null) return;
            float s = Mathf.Lerp(top.nodeS[0], top.nodeS[top.nodeS.Length - 1], 0.16f);
            int span = 0; while (span + 2 < top.nodeS.Length && top.nodeS[span + 1] < s) span++;
            var eye = top.At(s, 0f, top.CableY(span, s) - 2.3f);
            Place(G(-10f, -70f), 0f);
            Frame("10-far-from-lift", eye, G(-72f, -60f), 60f, false);
        });
        steps.Add(() =>
        {
            if (top == null) return;
            var eye = G(-6f, 70f) + Vector3.up * 1.8f;
            Place(G(-10f, -70f), 0f);
            Frame("11-far-from-run", eye, G(-62f, -20f) + Vector3.up * 2f, 60f, false);
        });
        steps.Add(() =>
        {
            if (top == null) return;
            var eye = G(-190f, 10f); eye.y = Mathf.Max(eye.y, Slope.PlateauAt(-60f)) + 30f;
            Place(G(-10f, -70f), 0f);
            Frame("12-far-wide", eye, G(-60f, -30f), 55f, false);
        });
        // the hero's arrival on the lift (only with the top on its bench): frames from the chair to the camp's hand-over
        steps.Add(() =>
        {
            if (top == null || !LiftTop.On) return;
            var f = typeof(SummitGame).GetField("arriveNext", NP); f.SetValue(game, true);
            cam.GetComponent<ChaseCamera>().enabled = false;
            typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { false });
            var arrive = (LiftArrive)typeof(SummitGame).GetField("arrive", NP).GetValue(game);
            Log($"{tag}: the arrival: {(arrive.Now == LiftArrive.Phase.Ride ? "playing" : "not playing: " + arrive.Why)}");
            arriveT = 0f; arriveShot = 0;
        });
        for (int i = 0; i < ArriveAt.Length; i++)
        {
            int k = i;
            steps.Add(() =>
            {
                if (top == null || !LiftTop.On) return;
                var arrive = (LiftArrive)typeof(SummitGame).GetField("arrive", NP).GetValue(game);
                var step = typeof(SummitGame).GetMethod("ArriveStep", NP);
                var body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
                while (arriveT < ArriveAt[k])
                {
                    arriveT += 1f / 30f; lapClock += 1f / 30f; SkiLift.TestClock = lapClock;
                    lUpd.Invoke(lift, null);
                    if (LiftEnds.Instance != null) LiftEnds.Instance.Step(1f / 30f, cam.transform.position);
                    if (!arrive.Active()) { camp.Walk(Vector2.zero, 1f / 30f); camp.CameraStep(1f / 30f); }
                    else step.Invoke(game, new object[] { 1f / 30f, false });
                    body.Apply(1f / 30f);
                }
                terrain.Prewarm(); props.Prewarm();
                Log($"{tag}: arrival at {arriveT:0.0} s: {arrive.Now}, hero at {arrive.HeroAt}");
                Shoot($"{tag}-arrive-{++arriveShot}-{arriveT:0.0}s.png", 590, 1278);
            });
        }
        // 7. straight down
        steps.Add(() =>
        {
            if (top == null) return;
            float cx = Slope.Centre(Slope.PadLip);
            Place(new Vector3(cx + 10f, 0f, -50f), 0f);
            var c = new Vector3(cx - 24f, Slope.PlateauAt(-82f), -82f);
            cam.orthographic = true; cam.orthographicSize = 50f;
            // (the cables are drawn a pixel or more wide whatever the distance: from straight above, as bands; not here)
            var cables = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) if (r.enabled && r.name.StartsWith("Cable")) { r.enabled = false; cables.Add(r); }
            // the version's pad (blue), the way in (green) and the run-out on the far side (orange), on the snow
            var marks = new List<Object>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); marks.Add(mat);
            Color[] col = { new Color(0.2f, 0.55f, 1f), new Color(0.1f, 0.8f, 0.25f), new Color(1f, 0.55f, 0.1f), new Color(0.85f, 0.2f, 0.85f) };
            foreach (var (kind, pts) in LiftTop.Lines())
            {
                var m = new Material(mat) { name = "PlanLine" + kind }; m.SetColor("_BaseColor", col[kind]); m.color = col[kind]; marks.Add(m);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(g.GetComponent<Collider>());
                    var a = pts[i] + Vector3.up * 0.4f; var b = pts[i + 1] + Vector3.up * 0.4f;
                    g.transform.position = (a + b) * 0.5f; g.transform.rotation = Quaternion.LookRotation((b - a).sqrMagnitude > 1e-6f ? b - a : Vector3.forward);
                    g.transform.localScale = new Vector3(kind == 0 ? 0.35f : 0.6f, 0.1f, (b - a).magnitude + 0.1f);
                    g.GetComponent<MeshRenderer>().sharedMaterial = m; g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    marks.Add(g);
                }
            }
            planUp = true;
            Frame("7-plan", c + Vector3.up * 120f, c, 50f, false);
            planUp = false;
            foreach (var o in marks) if (o != null) Object.DestroyImmediate(o);
            foreach (var r in cables) r.enabled = true;
            cam.orthographic = false;
        });
    }

    /// <summary>The lift running for a while round a point: its chairs and its ends' people.</summary>
    static void Run(float seconds, Vector3 eye)
    {
        terrain.Prewarm(); props.Prewarm();
        var ends = LiftEnds.Instance;
        for (float t = 0f; t < seconds; t += 1f / 30f)
        {
            lapClock += 1f / 30f;
            SkiLift.TestClock = lapClock;
            lUpd.Invoke(lift, null);
            if (ends != null) ends.Step(1f / 30f, eye);
        }
    }

    /// <summary>A point on the snow, in camp terms (metres off the run's line at the lip, z).</summary>
    static Vector3 G(float a, float z) { float x = Slope.Centre(Slope.PadLip) + a; return new Vector3(x, Slope.DrawnHeight(x, z), z); }

    /// <summary>For laying out the far side: the ground round the top and down the shoulder, as a grid of heights over the lip's level, and what's where.</summary>
    static void Grid()
    {
        float cx = Slope.Centre(Slope.PadLip), y0 = Slope.DrawnHeight(cx, Slope.PadLip);
        for (float z = -140f; z <= 240f; z += 10f)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"z{z,5:0} run {Slope.Centre(z) - cx,6:0.0}±{Slope.HalfAt(z),4:0}: ");
            for (float a = -180f; a <= 30f; a += 10f)
            {
                float x = cx + a, h = Slope.DrawnHeight(x, z) - y0;
                var n = Slope.Normal(x, z);
                string f = Course.Keep(x, z, 1f) ? "k" : Deadly.Below(x, z) ? "d" : Cliffs.WallCut(x, z) < -0.05f ? "w" : LiftLine.HillOrLane(x, z, 1.5f) ? "h" : n.y < 0.64f ? "s" : RockFace.IsRock(n, x, z) ? "r" : ".";
                sb.Append($"{h,6:0}{f}");
            }
            Log("grid " + sb);
        }
        var tw = new System.Text.StringBuilder();
        for (int i = 0; i < top.nodeS.Length; i++) { var p = top.At(top.nodeS[i], 0f, 0f); tw.Append($"({p.x - cx:0},{p.z:0}) "); }
        Log("grid line nodes " + tw);
    }

    static void Place(Vector3 p, float yaw)
    {
        camp.TeleportTo(new Vector3(p.x, Slope.DrawnHeight(p.x, p.z), p.z), yaw, false);
    }

    static bool planUp;
    static float arriveT; static int arriveShot;
    static readonly float[] ArriveAt = { 0.2f, 1.8f, 3.4f, 4.6f, 5.4f, 6.6f, 8.0f, 9.4f, 10.8f, 12.4f };

    /// <summary>Who of those off the lift is where: riding into the camp, riding off the far side, strapping in.</summary>
    static void Split()
    {
        var ends = LiftEnds.Instance; if (ends == null) return;
        var f = typeof(LiftEnds).GetField("ends", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) return;
        int into = 0, away = 0, other = 0;
        foreach (var e in (System.Collections.IEnumerable)f.GetValue(ends))
        {
            var end = (LiftEnds.End)e;
            if (end.lift != top || !end.top) continue;
            foreach (var a in end.people)
                if (a.job == LiftEnds.Job.Push) { if (a.intoCamp) into++; else away++; }
                else if (a.job == LiftEnds.Job.Unload || a.job == LiftEnds.Job.Strap) { if (a.intoCamp) into++; else other++; }
        }
        Log($"{tag}: off the lift now: {into} riding into the camp, {away} riding off the far side, {other} strapping in for the far side");
    }

    static void Frame(string name, Vector3 eye, Vector3 look, float fov, bool portrait, bool hideHero = true)
    {
        cam.GetComponent<ChaseCamera>().enabled = false;
        cam.transform.SetPositionAndRotation(eye, planUp ? Quaternion.LookRotation(Vector3.down, Vector3.forward) : Quaternion.LookRotation(look - eye, Vector3.up));
        cam.fieldOfView = fov;
        terrain.Prewarm(); props.Prewarm();
        // (the lift and its people a moment on, round where we look)
        Run(1.5f, Vector3.Lerp(eye, look, 0.3f));
        Shoot($"{tag}-{name}.png", portrait ? 590 : 1600, portrait ? 1278 : 1000);
    }

    static void Shoot(string file, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        tUpd.Invoke(terrain, null); fUpd.Invoke(props, null); aLate.Invoke(atmo, null);
        // (skinned meshes don't skin in batchmode: bake each into a plain mesh for the picture)
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterials = sk.sharedMaterials; mr.shadowCastingMode = sk.shadowCastingMode;
            var mpb = new MaterialPropertyBlock(); sk.GetPropertyBlock(mpb); mr.SetPropertyBlock(mpb);
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; off.Add(sk); baked.Add(g);
        }
        cam.Render();
        foreach (var g in baked) { var mf = g.GetComponent<MeshFilter>(); if (mf != null) Object.DestroyImmediate(mf.sharedMesh); Object.DestroyImmediate(g); }
        foreach (var sk in off) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Log($"{file} cam={cam.transform.position}");
    }
}
