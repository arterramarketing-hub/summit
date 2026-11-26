using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Every trick, triggered, landed and scored: on each ride (the snowboard, skis, the toboggan, the snow tube) a big
/// air (a Super Jump's pop) for each of its tricks, the rotation committed as flicks commit it and the grab as a tap
/// takes it (Rider.ForceTrick / ForceGrab: the first grab of an air is otherwise the jump's own), then held a moment
/// and let go. Each must land, not crash, not be sketchy, come out under the name it should and pay points. Also:
/// the same rotations from the stick itself (flicks), a grab held on into a tweak and on into the next grab, and a
/// harder grab paying more than a plain one. A frame of each trick at the top of its air goes into a sheet
/// ($TRICKTEST_OUT/tricks_sheet.png, in the order the log lists them).
///
/// Run: Unity -batchmode -projectPath . -executeMethod TrickTest.Run -logFile trick.log   (lines start TRICK; a
/// failure says FAIL, and the last line TRICK PASS / TRICK FAIL)
/// </summary>
public static class TrickTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Camera cam;
    static string crash; static int landings, fails;
    static readonly List<TrickLink> tricks = new List<TrickLink>();
    static readonly List<string> frames = new List<string>();
    static string outDir;

    struct Case { public string want; public int halves, flips, grab; }
    static Case C(string want, int halves = 0, int flips = 0, int grab = -1) => new Case { want = want, halves = halves, flips = flips, grab = grab };

    public static void Run()
    {
        outDir = System.Environment.GetEnvironmentVariable("TRICKTEST_OUT") ?? "TrickShots";
        Directory.CreateDirectory(outDir);
        try
        {
            Boot();
            // ---- the snowboard (regular: frontside turns left, so a negative spin)
            Ride(RideKind.Snowboard, false);
            Stance();
            Case[] board =
            {
                C("Frontside 180", -1), C("Backside 360", 2), C("Frontside 540", -3), C("Backside 720", 4),
                C("Backflip", 0, -1), C("Frontflip", 0, 1), C("Rodeo 540", 3, -1), C("Backside Cork 720", 4, -1), C("Misty 540", 3, 1),
            };
            foreach (var c in board) Air("board", c);
            var bg = Rides.Of(RideKind.Snowboard).Grabs;
            for (int i = 0; i < bg.Length; i++) Air("board", C(bg[i], 0, 0, i));
            Air("board", C("Backside 360 Truck Driver", 2, 0, 10));
            // ---- skis: spins counted plain, the old school among the grabs
            Ride(RideKind.Skis, false);
            Case[] skis = { C("360", 2), C("Backflip", 0, -1), C("Cork 720", 4, -1), C("Rodeo 540", 3, -1), C("Misty 360", 2, 1) };
            foreach (var c in skis) Air("skis", c);
            var sg = Rides.Of(RideKind.Skis).Grabs;
            for (int i = 0; i < sg.Length; i++) Air("skis", C(sg[i], 0, 0, i));
            Air("skis", C("Backflip Iron Cross", 0, -1, 6));
            // ---- the toboggan
            Ride(RideKind.Toboggan, false);
            var tg = Rides.Of(RideKind.Toboggan).Grabs;
            for (int i = 0; i < tg.Length; i++) Air("sled", C(tg[i], 0, 0, i));
            Air("sled", C("360", 2));
            // ---- the snow tube: twirls, the bail roll, the starfish and hands up
            Ride(RideKind.Snowboard, true);
            Air("tube", C("360 Twirl", 2));
            Air("tube", C("Bail Roll", 0, -1));
            Air("tube", C("Starfish", 0, 0, 0));
            Air("tube", C("Hands Up", 0, 0, 1));
            Ride(RideKind.Snowboard, false);

            // ---- from the stick: two flicks left are a frontside 360, a flick down a backflip
            Stick("stick FS 360", "Frontside 360", -1f, 0f, 2);
            Stick("stick backflip", "Backflip", 0f, -1f, 1);
            // ---- a grab held on: tweaked, then on into the next grab
            HoldOn();
            // ---- a harder shape pays more than a plain one, the same air otherwise
            int indy = Rider.TrickPoints(0, 0, 1, 0.5f, Rides.Of(RideKind.Snowboard).GrabBonus[0]);
            int truck = Rider.TrickPoints(0, 0, 1, 0.5f, Rides.Of(RideKind.Snowboard).GrabBonus[10]);
            Say($"points: Indy {indy}, Truck Driver {truck}" + (truck > indy ? "" : " FAIL (no more for the harder grab)"));
            if (truck <= indy) fails++;

            Sheet();
            Say(fails == 0 ? "PASS" : $"FAIL ({fails})");
        }
        catch (System.Exception e) { Debug.LogException(e); Say("FAIL (exception)"); fails++; }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0 ? 0 : 1);
    }

    static void Boot()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = (Rider)typeof(SummitGame).GetField("rider", NP).GetValue(game);
        body = (RiderBody)typeof(SummitGame).GetField("body", NP).GetValue(game);
        terrain = (SlopeStreamer)typeof(SummitGame).GetField("terrain", NP).GetValue(game);
        props = (Forest)typeof(SummitGame).GetField("props", NP).GetValue(game);
        cam = Camera.main;
        r.Link += l => { if (l.kind == LinkKind.Trick) tricks.Add(l); };
        r.Crash += why => crash = why;
        r.Landed += impact => landings++;
    }

    static void Ride(RideKind kind, bool tube) { body.SetRide(kind); r.SetRide(body.Ride); r.Tube = tube; body.SetTube(tube); }

    static void Start()
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        float z = Course.First + 110f, x = Slope.Centre(z);
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * 18f;
        terrain.Prewarm(); props.Prewarm();
        crash = null; landings = 0; tricks.Clear();
    }

    static void Step(int n)
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        game.TestPoints(Dt);
        if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
        if (n % 2 == 0 || r.Crashed) body.Apply(r.Crashed ? Dt : 2f * Dt);
    }

    /// <summary>Ride to a Super Jump's pop and fly it, with `act` each step in the air (air time, step); true if it landed.</summary>
    static bool Fly(System.Action<float, int> act, out float air)
    {
        Start();
        bool popped = false, flew = false; air = 0f; int n = 0;
        float z0 = r.Z;
        for (float t = 0f; t < 14f; t += Dt, n++)
        {
            if (!popped && !r.Airborne && t > 1f) { r.AddSuperJump(30f); r.QueuePop(1f); popped = true; }
            r.TrickX = r.TrickY = 0f; r.GrabHeld = false;
            if (popped && r.Airborne) { if (!flew) landings = 0; flew = true; air = r.AirTime; act(r.AirTime, n); }
            r.Steer = r.Airborne ? 0f : Mathf.Clamp((Slope.Centre(r.Z + 12f) - r.transform.position.x) * 0.06f - r.Heading * 0.8f, -1f, 1f);
            Step(n);
            if (flew && (landings > 0 || r.Crashed)) break;
        }
        // ride on a moment: the trick is paid as the air ends
        for (int k = 0; k < 60 && !r.Crashed; k++, n++) { r.TrickX = r.TrickY = 0f; r.GrabHeld = false; Step(n); }
        return flew && !r.Crashed;
    }

    static void Air(string ride, Case c)
    {
        bool forced = false, shot = false;
        bool ok = Fly((a, n) =>
        {
            if (!forced && a > 0.08f)
            {
                forced = true;
                if (c.halves != 0 || c.flips != 0) r.ForceTrick(c.halves, c.flips);
                if (c.grab >= 0) r.ForceGrab(c.grab);
            }
            if (c.grab >= 0) r.GrabHeld = a > 0.08f && a < 0.62f && a < r.TrickTime - 0.5f;
            if (!shot && forced && r.TrickTime > 0f && a > (c.grab >= 0 ? 0.55f : 0.45f * r.TrickTime))
            {
                shot = true;
                body.Apply(0f);
                string file = $"{frames.Count:D2}_{ride}_{c.want.Replace(' ', '_')}.png";
                Shoot(file); frames.Add(file);
            }
        }, out float air);
        Check($"{ride,-5} {c.want}", c.want, ok, air);
    }

    static void Check(string label, string want, bool ok, float air)
    {
        var t = tricks.Count > 0 ? tricks[tricks.Count - 1] : default;
        string got = tricks.Count > 0 ? t.name : "-";
        string why = !ok ? "crashed '" + crash + "'" : tricks.Count == 0 ? "no trick" : t.sketchy ? "sketchy" : got != want ? "named '" + got + "'" : t.points <= 0 ? "no points" : null;
        if (why != null) fails++;
        Say($"{label,-36} air {air:0.00}s -> {got} +{(tricks.Count > 0 ? t.points : 0)}" + (why == null ? "" : " FAIL (" + why + ")"));
    }

    static void Stick(string label, string want, float x, float y, int flicks)
    {
        int done = 0; float last = -9f; bool down = false;
        bool ok = Fly((a, n) =>
        {
            // a flick: pushed past FlickAt for a few steps, then let back to the middle
            if (done < flicks && a > 0.1f + done * 0.2f && !down) { down = true; last = a; }
            if (down && a - last > 0.06f) { down = false; done++; }
            r.TrickX = down ? x : 0f; r.TrickY = down ? y : 0f;
        }, out float air);
        Check(label, want, ok, air);
    }

    static void HoldOn()
    {
        float was = Rider.GrabStepHold; Rider.GrabStepHold = 0.4f;   // (a Super Jump's 1.7 s: the same steps, sooner)
        try { HoldOnNow(); } finally { Rider.GrabStepHold = was; }
    }

    static void HoldOnNow()
    {
        var g = Rides.Of(RideKind.Snowboard);
        string a = g.Grabs[0], b = g.Grabs[1];
        bool forced = false;
        bool ok = Fly((t, n) =>
        {
            if (!forced && t > 0.08f) { forced = true; r.ForceGrab(0); }
            r.GrabHeld = t > 0.08f && t < 0.12f + 2.2f * Rider.GrabStepHold && t < r.TrickTime - 0.45f;
        }, out float air);
        string want = a + " to " + b;
        if (air < 0.12f + 2.2f * Rider.GrabStepHold + 0.45f) { Say($"held grab: air {air:0.00}s too short to hold on (skipped)"); return; }
        Check("board held on", want, ok, air);
    }

    static void Say(string s) => Debug.Log("TRICK " + s);

    /// <summary>
    /// Which way the snowboarder stands, from the body itself (the names hang on it): the chest to the right of the
    /// way they go and the left foot forward is regular, and a frontside spin then turns to the left (the chest opening
    /// down the hill first). Checked a quarter of the way into a frontside 180.
    /// </summary>
    static void Stance()
    {
        var h = (HeroRig)typeof(RiderBody).GetField("hero", NP).GetValue(body);
        if (h == null || h.Pack == null || h.UpperChest == null) { Say("stance: no hero rig to read (skipped)"); return; }
        float chestRight = 0f, leftAhead = 0f, opens = 0f; bool forced = false, read = false;
        Fly((a, n) =>
        {
            Vector3 v = r.Velocity; v.y = 0f; Vector3 fwd = v.normalized, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 chest = h.UpperChest.position - h.Pack.position; chest.y = 0f; chest.Normalize();
            if (a > 0.05f && a < 0.07f) { chestRight = Vector3.Dot(chest, right); leftAhead = Vector3.Dot(h.Foot[0].position - h.Foot[1].position, fwd); }
            if (!forced && a > 0.08f) { forced = true; r.ForceTrick(-1, 0); }
            if (forced && !read && Mathf.Abs(r.SpinNow) > Mathf.PI * 0.25f) { read = true; opens = Vector3.Dot(chest, fwd); }
        }, out _);
        bool regular = chestRight > 0.3f && leftAhead > 0f;
        bool ok = regular && opens > 0.3f;
        Say($"stance: chest to the right of travel {chestRight:0.00}, left foot ahead {leftAhead:0.00} m ({(regular ? "regular" : "goofy?")}); " +
            $"a quarter into a frontside 180 the chest faces down the hill {opens:0.00}" + (ok ? "" : " FAIL (frontside is named the wrong way)"));
        if (!ok) fails++;
    }

    // ---------------------------------------------------------------- the frames

    static void Shoot(string file)
    {
        const int w = 360, h = 360;
        Vector3 v = r.Velocity; v.y = 0f;
        Vector3 fwd = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, fwd);
        Vector3 p = r.transform.position + Vector3.up * 0.4f;
        cam.transform.position = p + side * 3.2f + fwd * 1.8f + Vector3.up * 1.1f;   // (ahead and above, on the chest's side: the hands on the gear show)
        cam.transform.LookAt(p); cam.fieldOfView = 40f;
        bool hide = props != null && props.gameObject.activeSelf;
        if (hide) props.gameObject.SetActive(false);
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = 1f;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var go = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = m; go.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            go.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; baked.Add(go);
        }
        cam.Render();
        foreach (var go in baked) Object.DestroyImmediate(go);
        foreach (var sk in skins) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        if (hide) props.gameObject.SetActive(true);
    }

    static void Sheet()
    {
        if (frames.Count == 0) return;
        const int s = 360, cols = 8;
        int rows = (frames.Count + cols - 1) / cols;
        var sheet = new Texture2D(s * cols, s * rows, TextureFormat.RGB24, false);
        var px = new Color[s * cols * s * rows];
        for (int i = 0; i < px.Length; i++) px[i] = Color.black;
        sheet.SetPixels(px);
        for (int k = 0; k < frames.Count; k++)
        {
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(Path.Combine(outDir, frames[k])));
            sheet.SetPixels((k % cols) * s, (rows - 1 - k / cols) * s, s, s, t.GetPixels());
            Object.DestroyImmediate(t);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(outDir, "tricks_sheet.png"), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        Say($"sheet: {frames.Count} frames, {cols} across, in the order above -> {Path.Combine(outDir, "tricks_sheet.png")}");
    }
}
