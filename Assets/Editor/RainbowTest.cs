using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Summit;

/// <summary>
/// The Invincible rainbow (PowerAura): the rider's own materials flashing through the colours.
///
/// Run (checks, no GPU needed):
///   Unity -batchmode -nographics -quit -projectPath . -executeMethod RainbowTest.Run -logFile rainbow.log
///   Every RIDER row in every OUTFIT, on the snowboard, skis, toboggan and in the tube (a patterned jacket on every other
///   one, for SnowyCloth's second colour). For each: every visible material of the rider is painted while it lasts; the
///   colours step about ten times a second and go through the whole rainbow; the last two seconds step slower and show
///   the rider's own colours about half the time; when it runs out no property block is left and every material is the
///   same object with the same colours as before. Then a crash, a restart and a rider swap mid-rainbow, and the cost
///   of a frame (managed bytes allocated must be 0). Nothing of the old aura is left: no aura or spark particles, no
///   extra light. Lines start RAINBOW; the last is RAINBOW PASS or RAINBOW FAIL.
///
/// Run (renders, with graphics: no -nographics):
///   Unity -batchmode -quit -projectPath . -executeMethod RainbowTest.Shots -logFile rainbowshot.log
///   RAINBOW_OUT=folder (default: the project folder). Writes RainbowShot_*.png: for each of four riders a strip of their
///   own colours then the six steps, the last two seconds, and the chase camera's view.
/// </summary>
public static class RainbowTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static SummitGame game; static Rider rider; static RiderBody body; static PowerAura aura;
    static SlopeStreamer terrain; static Forest props;
    static PropertyInfo invProp;
    static int fails, firstSlots;
    static readonly MaterialPropertyBlock probe = new MaterialPropertyBlock();

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);
    static void SetLeft(float s) => invProp.SetValue(rider, s);
    static void Fail(string what) { fails++; if (fails < 40) Debug.Log("RAINBOW FAIL " + what); }

    static void Open()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        rider = F<Rider>("rider"); body = F<RiderBody>("body"); aura = F<PowerAura>("aura");
        terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        invProp = typeof(Rider).GetProperty("InvincibleTime");
    }

    // ---- the checks

    public static void Run()
    {
        Open();
        try
        {
            TestBoot.NewMountain(game, 1000, terrain, props);
            float z = Course.First + 110f;
            rider.ResetAt(Slope.Centre(z), z); rider.Begin();
            OldAuraGone();
            int patterned = -1;
            var jackets = Wardrobe.Items(Wardrobe.Cat.Jacket);
            for (int i = 0; i < jackets.Length; i++) if (jackets[i].Patterned) { patterned = i; break; }
            int riders = Wardrobe.Items(Wardrobe.Cat.Rider).Length, cases = 0;
            for (int r = 0; r < riders; r++)
                for (int set = 0; set < Wardrobe.OutfitCount; set++)
                {
                    var o = (int[])Wardrobe.Worn().Clone();
                    o[(int)Wardrobe.Cat.Rider] = r; o[(int)Wardrobe.Cat.Outfit] = set;
                    o[(int)Wardrobe.Cat.Jacket] = (r + set) % 2 == 1 && patterned >= 0 ? patterned : 0;
                    // a board graphic on most looks (every design in turn, and now and then none): it flashes too
                    o[(int)Wardrobe.Cat.Graphic] = (r * Wardrobe.OutfitCount + set) % Wardrobe.Items(Wardrobe.Cat.Graphic).Length;
                    body.Wear(o);
                    for (int k = 0; k < 4; k++) { Case($"rider{r} outfit{set} jacket{o[(int)Wardrobe.Cat.Jacket]} {RideName(k)}", k, cases == 0); cases++; }
                }
            Ride(0);
            // what is under the rider after all those swaps
            var names = new Dictionary<string, int>();
            foreach (Transform c in body.Drawn) { names.TryGetValue(c.name, out int n); names[c.name] = n + 1; }
            var list = new List<string>(); foreach (var kv in names) list.Add($"{kv.Key} x{kv.Value}");
            Debug.Log($"RAINBOW under the rider after {riders * Wardrobe.OutfitCount} looks: {string.Join(", ", list)}");
            Crash();
            Restart();
            Swap();
            Debug.Log($"RAINBOW graphics: {artCases} cases with a board graphic seen, {artSlots} printed slots, all painted over the print");
            Debug.Log(fails == 0 ? $"RAINBOW PASS: {cases} riders, outfits and rides, and the crash, restart and swap; 0 failures"
                                 : $"RAINBOW FAIL: {fails} failure(s) over {cases} cases");
        }
        catch (System.Exception e) { Debug.LogError("RAINBOW exception " + e); }
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static string RideName(int k) => k == 3 ? "tube" : ((RideKind)k).ToString();

    static void Ride(int k)
    {
        bool tube = k == 3;
        body.SetRide(tube ? RideKind.Snowboard : (RideKind)k); rider.SetRide(body.Ride);
        rider.Tube = tube; body.SetTube(tube);
    }

    /// <summary>Every material slot of everything drawn of the rider, as it is now.</summary>
    struct Was { public Renderer r; public int i; public Material m; public Color a, b; public bool hasB; }
    static List<Was> Snapshot()
    {
        var all = new List<Was>();
        foreach (var r in body.Drawn.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                var m = ms[i];
                if (m == null) continue;
                bool hasA = m.HasProperty("_BaseColor"), hasB = m.HasProperty("_Color2");
                all.Add(new Was { r = r, i = i, m = m, a = hasA ? m.GetColor("_BaseColor") : default, b = hasB ? m.GetColor("_Color2") : default, hasB = hasB });
            }
        }
        return all;
    }

    static bool Blocked(Renderer r, int i) { r.GetPropertyBlock(probe, i); return !probe.isEmpty; }

    /// <summary>How many of the slots that are seen are painted, and how many are seen.</summary>
    static (int painted, int seen) Coverage()
    {
        int p = 0, s = 0;
        foreach (var w in Snapshot())
        {
            if (!w.r.enabled || !w.r.gameObject.activeInHierarchy || w.r.shadowCastingMode == ShadowCastingMode.ShadowsOnly || !w.m.HasProperty("_BaseColor")) continue;
            s++;
            if (Blocked(w.r, w.i)) p++;
        }
        return (p, s);
    }

    /// <summary>Left as it was before: no property block anywhere, the same materials, the same colours, bit for bit.</summary>
    static void Restored(string tag, List<Was> before)
    {
        var now = Snapshot();
        int blocked = 0, changed = 0;
        foreach (var w in now) if (Blocked(w.r, w.i) || w.r.HasPropertyBlock()) blocked++;
        if (before != null)
        {
            if (now.Count != before.Count) changed += Mathf.Abs(now.Count - before.Count);
            for (int i = 0; i < Mathf.Min(now.Count, before.Count); i++)
            {
                var a = before[i]; var b = now[i];
                if (a.r != b.r || a.i != b.i || a.m != b.m || a.a != b.a || (a.hasB && a.b != b.b)) changed++;
            }
        }
        if (blocked > 0 || changed > 0) Fail($"{tag}: not restored ({blocked} slot(s) still blocked, {changed} changed)");
    }

    static void Case(string tag, int k, bool first)
    {
        Ride(k);
        float z = Course.First + 110f;
        rider.ResetAt(Slope.Centre(z), z); rider.Begin(); aura.Clear();
        var before = Snapshot();
        Restored(tag + " before", null);
        int lights = Lights();
        SetLeft(8f);
        float t = 0f; int frames = 0, fastFrames = 0, fastPainted = 0, warnFrames = 0, warnOwn = 0, fastChanges = 0, warnChanges = 0;
        int worstCover = int.MaxValue, seenAtWorst = 0;
        var colours = new HashSet<Color>();
        Color last = default; bool lastPainted = false;
        var probeR = FirstSeen(out int probeI);
        long bytes = 0; var watch = new System.Diagnostics.Stopwatch();
        bool artChecked = false;
        while (rider.InvincibleTime > 0f)
        {
            float left = rider.InvincibleTime;
            if (!artChecked && aura.Painted && left > 2f) { artChecked = true; ArtFlashes(tag, k); }
            long b0 = System.GC.GetAllocatedBytesForCurrentThread();
            watch.Start(); aura.Step(t); watch.Stop();
            if (frames > 5) bytes += System.GC.GetAllocatedBytesForCurrentThread() - b0;
            Color c = default;
            if (aura.Painted && probeR != null) { probeR.GetPropertyBlock(probe, probeI); c = probe.GetColor("_BaseColor"); }
            bool changedNow = aura.Painted != lastPainted || (aura.Painted && c != last);
            if (left > 2f)
            {
                fastFrames++;
                if (aura.Painted) { fastPainted++; colours.Add(c); }
                if (changedNow && frames > 0) fastChanges++;
                if (frames % 7 == 0)
                {
                    var (p, s) = Coverage();
                    if (p < worstCover) { worstCover = p; seenAtWorst = s; }
                    if (p != s) Fail($"{tag}: {p} of {s} seen slots painted at {left:0.00} s left");
                }
            }
            else { warnFrames++; if (!aura.Painted) warnOwn++; if (changedNow) warnChanges++; }
            if (lights != Lights()) Fail($"{tag}: the lights changed while invincible");
            last = c; lastPainted = aura.Painted;
            t += Dt; frames++;
            SetLeft(Mathf.Max(0f, left - Dt));
        }
        aura.Step(t);   // the frame it runs out
        Restored(tag, before);
        float fastSecs = fastFrames * Dt, warnSecs = warnFrames * Dt;
        float fastRate = fastChanges / fastSecs, warnRate = warnChanges / warnSecs, ownShare = warnOwn / (float)Mathf.Max(1, warnFrames);
        if (fastPainted != fastFrames) Fail($"{tag}: painted {fastPainted} of {fastFrames} frames before the last two seconds");
        if (colours.Count < 6) Fail($"{tag}: only {colours.Count} colours");
        if (fastRate < 8f || fastRate > 12f) Fail($"{tag}: {fastRate:0.0} colour changes a second (want about 10)");
        if (warnRate >= fastRate || ownShare < 0.3f || ownShare > 0.7f) Fail($"{tag}: the last two seconds: {warnRate:0.0} changes a second, own colours {ownShare:P0} of the time");
        if (bytes != 0) Fail($"{tag}: {bytes} bytes allocated by the rainbow");
        if (first) firstSlots = aura.Slots;
        else if (aura.Slots > firstSlots * 2) Fail($"{tag}: {aura.Slots} slots (the first rider had {firstSlots}): painting what isn't drawn");
        if (first || fails > 0)
            Debug.Log($"RAINBOW {tag}: {aura.Slots} slots, seen painted {worstCover}/{seenAtWorst}, {colours.Count} colours, {fastRate:0.0} changes/s, " +
                      $"last 2 s {warnRate:0.0} changes/s and own colours {ownShare:P0}, {bytes} B allocated, {watch.Elapsed.TotalMilliseconds * 1000.0 / frames:0.0} us a frame");
        else Debug.Log($"RAINBOW {tag}: {aura.Slots} slots, {seenAtWorst} seen all painted, {colours.Count} colours, {fastRate:0.0}/s, last 2 s {warnRate:0.0}/s own {ownShare:P0}, {bytes} B");
    }

    /// <summary>
    /// A board graphic on: every seen slot printed with it (the deck, the skis, the tube's trim) is painted too, tinted
    /// over the print (a picture's tint, not a flat colour), and on the trousers' side of the rainbow.
    /// </summary>
    static int artSlots, artCases;
    static void ArtFlashes(string tag, int k)
    {
        if (body.ShownGraphic <= 0) return;
        var key = Wardrobe.Get(Wardrobe.Cat.Graphic, body.ShownGraphic).art;
        int printed = 0;
        foreach (var w in Snapshot())
        {
            if (!w.r.enabled || !w.r.gameObject.activeInHierarchy || w.r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
            var tex = w.m.HasProperty("_BaseMap") ? w.m.GetTexture("_BaseMap") : null;
            if (tex == null || tex.name != key) continue;
            printed++;
            if (!Blocked(w.r, w.i)) { Fail($"{tag}: the {key} print on {w.r.name} isn't painted"); continue; }
            w.r.GetPropertyBlock(probe, w.i);
            var c = probe.GetColor("_BaseColor");
            if (c.r > 0.99f && c.g > 0.99f && c.b > 0.99f) Fail($"{tag}: the {key} print on {w.r.name} is painted white");
            if (!body.Lower(w.m)) Fail($"{tag}: the {key} print on {w.r.name} isn't on the board's side of the rainbow");
        }
        if (printed == 0 && k != (int)RideKind.Toboggan) Fail($"{tag}: {key} on, but nothing drawn wears it");
        if (printed > 0) { artSlots += printed; artCases++; }
    }

    /// <summary>A seen slot of the rider's jacket-side colour (not the trousers or board), to follow the colour on.</summary>
    static Renderer FirstSeen(out int index)
    {
        index = 0;
        foreach (var w in Snapshot())
            if (w.r.enabled && w.r.gameObject.activeInHierarchy && w.r is SkinnedMeshRenderer && w.m.HasProperty("_BaseColor") && !body.Lower(w.m)) { index = w.i; return w.r; }
        return null;
    }

    static int Lights()
    {
        int n = 0;
        foreach (var l in body.GetComponentsInChildren<Light>(true)) if (l.enabled && l.gameObject.activeInHierarchy) n++;
        return n;
    }

    static void OldAuraGone()
    {
        foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true))
            if (ps.name == "Aura" || ps.name == "AuraSparks") Fail("the old aura's particles are still built: " + ps.name);
        // invincible and riding: nothing of PowerAura's emits (no super jump)
        SetLeft(8f);
        for (int i = 0; i < 30; i++) aura.Step(i * Dt);
        foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true))
            if (ps.transform.parent == aura.transform && ps.emission.rateOverTime.constant > 0f) Fail($"{ps.name} emits while invincible");
        SetLeft(0f); aura.Step(31 * Dt);
        Debug.Log("RAINBOW old aura: no Aura or AuraSparks particles; " + aura.GetComponentsInChildren<ParticleSystem>(true).Length + " particle systems on the rider");
    }

    // ---- ends other than running out

    static void Paint(float seconds, ref float t) { for (float s = 0f; s < seconds; s += Dt) { aura.Step(t); t += Dt; } }

    static void Crash()
    {
        float z = Course.First + 110f;
        rider.ResetAt(Slope.Centre(z), z); rider.Begin(); aura.Clear();
        var before = Snapshot();
        SetLeft(8f); float t = 0f; Paint(1f, ref t);
        if (!aura.Painted) Fail("crash: not painted before the crash");
        rider.DoCrash("RAINBOW test");
        aura.Step(t);
        Restored("crash", before);
        Debug.Log($"RAINBOW crash: painted before, restored the frame of the crash ({rider.InvincibleTime:0.0} s were left)");
    }

    static void Restart()
    {
        float z = Course.First + 110f;
        rider.ResetAt(Slope.Centre(z), z); rider.Begin(); aura.Clear();
        SetLeft(8f); float t = 0f; Paint(1.3f, ref t);
        if (!aura.Painted) Fail("restart: not painted before the restart");
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        Restored("restart (before any frame)", null);   // (ToReady clears it at once)
        Debug.Log($"RAINBOW restart: restored by the restart itself, invincible left {rider.InvincibleTime:0.0} s");
    }

    static void Swap()
    {
        float z = Course.First + 110f;
        var o = (int[])Wardrobe.Worn().Clone();
        o[(int)Wardrobe.Cat.Rider] = 0; o[(int)Wardrobe.Cat.Outfit] = 0; body.Wear(o); Ride(0);
        rider.ResetAt(Slope.Centre(z), z); rider.Begin(); aura.Clear();
        SetLeft(8f); float t = 0f; Paint(1f, ref t);
        int built = body.Built;
        o[(int)Wardrobe.Cat.Rider] = 2; o[(int)Wardrobe.Cat.Outfit] = 1; body.Wear(o);
        if (body.Built == built) Fail("swap: the body wasn't rebuilt");
        var before = Snapshot();   // (the board, tube and gear outlive the swap: still painted until the next frame)
        int fresh = 0;
        foreach (var w in before) if (w.r.transform.IsChildOf(body.Drawn.Find("Body")) && w.r is SkinnedMeshRenderer && Blocked(w.r, w.i)) fresh++;
        if (fresh > 0) Fail($"swap: {fresh} slot(s) of the new body came with a property block");
        Paint(0.5f, ref t);
        var (p, s) = Coverage();
        if (p != s) Fail($"swap: {p} of {s} seen slots painted on the new body");
        SetLeft(0f); aura.Step(t);
        Restored("swap", before);
        Debug.Log($"RAINBOW swap: the new body painted {p}/{s}, then restored");
        o[(int)Wardrobe.Cat.Rider] = 0; o[(int)Wardrobe.Cat.Outfit] = 0; body.Wear(o);
    }

    // ---- the renders

    static Camera cam; static ChaseCamera chase; static MethodInfo late; static Atmosphere atmo; static CourseDressing dress;
    static int step;
    const int W = 300, H = 420;

    public static void Shots()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        Open();
        string outDir = System.Environment.GetEnvironmentVariable("RAINBOW_OUT");
        if (string.IsNullOrEmpty(outDir)) outDir = ".";
        Directory.CreateDirectory(outDir);
        Physics.simulationMode = SimulationMode.Script;
        dress = Object.FindFirstObjectByType<CourseDressing>();
        cam = Camera.main; chase = cam.GetComponent<ChaseCamera>(); late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        atmo = Object.FindFirstObjectByType<Atmosphere>();
        try
        {
            TestBoot.NewMountain(game, 1000, terrain, props);
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            ChaseCamera.TestDt = 4 * Dt;
            // (rider row, outfit, ride)
            var looks = new[] { (0, 0, 0, "SummitRider_Board"), (2, 1, 0, "Val_Pyjamas_Board"), (3, 5, 2, "Breanna_Fleece_Toboggan"), (Wardrobe.PaulRow, 3, 1, "Paul_Puffer_Skis") };
            foreach (var (r, set, k, name) in looks)
            {
                var o = (int[])Wardrobe.Worn().Clone();
                o[(int)Wardrobe.Cat.Rider] = r; o[(int)Wardrobe.Cat.Outfit] = set; o[(int)Wardrobe.Cat.Jacket] = 0;
                Ride(0);   // (swapped on the board: a swap leaves the old body's skis and sled under the rider, shown if they were)
                body.Wear(o); Ride(k);
                Strip(name, outDir);
            }
        }
        catch (System.Exception e) { Debug.LogError("RAINBOW exception " + e); }
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static void Strip(string name, string outDir)
    {
        float z = Course.First + 140f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin(); rider.Body.linearVelocity = Vector3.forward * 12f;
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm(); chase.Snap(); aura.Clear();
        step = 0;
        for (float t = 0f; t < 1.2f; t += Dt) { rider.Steer = Hold(); Tick(); }
        if (rider.Crashed) Debug.Log($"RAINBOW {name}: crashed before the shots");
        // the rider held still from here: their own colours, then one step of the rainbow a frame
        body.Apply(Dt);
        var tiles = new List<Texture2D>();
        float at = 100f;
        aura.Clear(); SetLeft(0f); aura.Step(at);
        tiles.Add(Front());
        SetLeft(8f);
        aura.Step(at);
        for (int i = 0; i < 6; i++) { tiles.Add(Front()); at += 0.1001f; aura.Step(at); }
        Save(tiles, Path.Combine(outDir, $"RainbowShot_{name}.png"));
        // the last two seconds, every 0.15 s
        tiles.Clear();
        float left = 2f;
        while (left > 0.05f && tiles.Count < 12)
        {
            SetLeft(left); aura.Step(at); tiles.Add(Front());
            at += 0.15f; left -= 0.15f;
        }
        Save(tiles, Path.Combine(outDir, $"RainbowShot_{name}_last2s.png"));
        // as the player sees it: the chase camera, own colours then three steps
        tiles.Clear();
        aura.Clear(); SetLeft(0f); aura.Step(at); tiles.Add(Chase());
        SetLeft(8f); aura.Step(at);
        for (int i = 0; i < 3; i++) { tiles.Add(Chase()); at += 0.1001f; aura.Step(at); }
        Save(tiles, Path.Combine(outDir, $"RainbowShot_{name}_chase.png"));
        SetLeft(0f); aura.Step(at);
        Restored(name + " after the shots", null);
    }

    static float Hold() => Mathf.Clamp((Slope.Centre(rider.Z + 12f) - rider.transform.position.x) * 0.06f - rider.Heading * 0.8f, -1f, 1f);

    static void Tick()
    {
        rider.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        if (step % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (step % 4 == 0) { body.Apply(4 * Dt); late.Invoke(chase, null); }
        step++;
    }

    static Texture2D Front()
    {
        chase.enabled = false;
        Vector3 p = rider.transform.position;
        Vector3 f = rider.Velocity; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, f);
        Vector3 at = p + Vector3.up * 0.55f;
        Vector3 from = at + f * 3.1f + side * 1.5f + Vector3.up * 0.35f;
        cam.fieldOfView = 34f;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
        var t = Shoot();
        chase.enabled = true;
        return t;
    }

    static Texture2D Chase()
    {
        cam.fieldOfView = 60f;
        for (int i = 0; i < 30; i++) late.Invoke(chase, null);   // (settled behind the rider again after the front shots)
        return Shoot();
    }

    static Texture2D Shoot()
    {
        typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        typeof(Forest).GetMethod("Draw", NP).Invoke(props, null);
        // batchmode renders do not re-skin meshes: bake them, keeping each material's property block (the rainbow)
        var baked = new List<GameObject>(); var skins = new List<SkinnedMeshRenderer>();
        var block = new MaterialPropertyBlock();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            skins.Add(sk);
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr = g.GetComponent<MeshRenderer>();
            mr.sharedMaterials = sk.sharedMaterials;
            for (int i = 0; i < sk.sharedMaterials.Length; i++) { sk.GetPropertyBlock(block, i); if (!block.isEmpty) mr.SetPropertyBlock(block, i); }
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(g);
        }
        DynamicGI.UpdateEnvironment();
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt; cam.aspect = W / (float)H;
        cam.Render(); cam.Render();   // (the first render after a move can come out unlit)
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        cam.targetTexture = null; RenderTexture.active = null; rt.Release();
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in skins) sk.enabled = true;
        return tex;
    }

    static void Save(List<Texture2D> tiles, string file)
    {
        var strip = new Texture2D(W * tiles.Count + 4 * (tiles.Count - 1), H, TextureFormat.RGB24, false);
        var fill = new Color32[strip.width * strip.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(24, 24, 24, 255);
        strip.SetPixels32(fill);
        for (int i = 0; i < tiles.Count; i++) strip.SetPixels(i * (W + 4), 0, W, H, tiles[i].GetPixels());
        strip.Apply();
        File.WriteAllBytes(file, strip.EncodeToPNG());
        Debug.Log("RAINBOW wrote " + file);
    }
}
