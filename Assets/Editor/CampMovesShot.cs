using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The camp's controls: one stick anywhere walks (and the camera follows round behind), a second finger held turns
/// the camera by hand, a tap jumps, a flick rolls. First the touch logic on its own (CampInput), fed made-up fingers:
/// what each finger is taken as. Then the real thing: scripted fingers through Camp.Step while the game runs, walking
/// with one thumb, tapping a second finger to jump, holding it to swing the camera round, flicking to roll; rendered
/// as the phone shows it (sticks and all) to CampMoves/seq_moves.
/// Run: Unity -batchmode -projectPath . -executeMethod CampMovesShot.Run -logFile moves.log   (lines start MOVES)
/// </summary>
public static class CampMovesShot
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    const int W = 590, H = 1278;
    static SummitGame game; static Camp camp; static MetaUi meta; static RiderBody body; static Rider rider;
    static SlopeStreamer terrain; static Forest props; static Camera cam; static Atmosphere atmo;
    static MethodInfo tUpd, fUpd, aLate;
    static int fails;
    static string outDir = "CampMoves";

    static void Log(string s) => Debug.Log("MOVES " + s);
    static void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }

    public static void Run()
    {
        Directory.CreateDirectory(outDir);
        try
        {
            InputProof();
            HeroBake.Bake();
            SummitImport.EnsureSky(); SummitImport.EnsureBackdrop(); CampAssets.Ensure();
            EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
            game = Object.FindFirstObjectByType<SummitGame>();
            TestBoot.Boot(game, false);
            T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
            camp = Get<Camp>("camp"); meta = Get<MetaUi>("meta"); body = Get<RiderBody>("body"); rider = Get<Rider>("rider");
            terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
            var hud = Get<Hud>("hud");
            cam = Camera.main; atmo = Object.FindFirstObjectByType<Atmosphere>();
            tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); fUpd = typeof(Forest).GetMethod("Update", NP); aLate = typeof(Atmosphere).GetMethod("LateUpdate", NP);
            ChaseCamera.TestDt = Dt;
            hud.SetRun(0f, 0, 1f, 120f, 160f, false); hud.SetPower(0f, 0, false); hud.SetFalls(3, 3, false); hud.SetAltitude(0f, false); hud.SetMedical(0f, false);
            var hc = hud.GetComponent<Canvas>(); hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 0.6f;
            var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            if (vol != null && vol.profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion ld)) ld.intensity.Override(0f);
            // the whole run is drawn into a phone-sized picture, so "the screen" is that picture: fingers are in its pixels
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt; cam.aspect = W / (float)H;
            meta.UseCamera(cam);
            Scripted(rt);
        }
        catch (System.Exception e) { Debug.LogError("MOVES exception " + e); fails++; }
        Camp.TestTouches = null;
        ChaseCamera.TestDt = 0f;
        Log($"done: {fails} failure(s)");
        EditorApplication.Exit(0);
    }

    // ---- the touch logic alone
    static void InputProof()
    {
        const float w = 1170f, h = 2532f;   // an iPhone's screen, in pixels
        string Hit(Vector2 p) => p.y > h - 220f && p.x > w - 450f ? "missions" : (p.y > 230f && p.y < 370f && Mathf.Abs(p.x - w * 0.5f) < 330f ? "context" : null);
        CampInput Fresh() => new CampInput();
        var list = new List<CampInput.Touch>();
        // play a script of fingers: each frame, the fingers down; collect what came of it
        (bool tap, bool swipe, bool moved, bool looked, string button, bool context, Vector2 swipeDir, string last) Play(CampInput ci, System.Func<int, List<CampInput.Touch>> frame, int frames)
        {
            bool tap = false, sw = false, mv = false, lk = false, ctx = false; string btn = null; Vector2 sd = Vector2.zero;
            for (int f = 0; f < frames; f++)
            {
                ci.Step(frame(f), Dt, w, h, Hit, false);
                tap |= ci.Tapped; sw |= ci.Swiped; mv |= ci.MoveShown && ci.Move.sqrMagnitude > 0.01f; lk |= ci.LookShown; ctx |= ci.ContextTapped;
                if (ci.Button != null) btn = ci.Button;
                if (ci.Swiped) sd = ci.SwipeDir;
            }
            return (tap, sw, mv, lk, btn, ctx, sd, ci.LastGesture);
        }
        List<CampInput.Touch> One(int id, Vector2 p) { list.Clear(); list.Add(new CampInput.Touch(id, p)); return list; }
        List<CampInput.Touch> None() { list.Clear(); return list; }

        // 1. left thumb dragged and held: the move stick, never a tap or a flick
        var r = Play(Fresh(), f => f < 50 ? One(1, new Vector2(300f, 700f) + new Vector2(0f, Mathf.Min(f, 15) * 14f)) : None(), 55);
        Check($"left drag and hold walks (moved={r.moved} tap={r.tap} swipe={r.swipe} last={r.last})", r.moved && !r.tap && !r.swipe && r.last == "move");
        // 2. one thumb on the right, held and dragged: the move stick too (one stick, anywhere), never the camera
        r = Play(Fresh(), f => f < 50 ? One(2, new Vector2(880f, 1200f) + new Vector2(Mathf.Min(f, 25) * 8f, 0f)) : None(), 55);
        Check($"one thumb on the right walks, it is not a camera stick (moved={r.moved} look={r.looked} tap={r.tap} last={r.last})", r.moved && !r.looked && !r.tap && r.last == "move");
        // 2b. a second finger pressed and held while the first walks: the camera stick, and the walk carries on
        var ci2 = Fresh(); bool looked2 = false, walked2 = true, lookEarly = false;
        for (int f = 0; f < 60; f++)
        {
            list.Clear(); list.Add(new CampInput.Touch(20, new Vector2(300f, 700f + Mathf.Min(f, 10) * 20f)));
            if (f >= 20 && f < 55) list.Add(new CampInput.Touch(21, new Vector2(850f + Mathf.Clamp(f - 34, 0, 15) * 8f, 1500f)));   // held still a moment, then dragged
            ci2.Step(list, Dt, w, h, Hit, false);
            if (f > 12 && !(ci2.MoveShown && ci2.Move.y > 0.5f)) walked2 = false;
            if (ci2.LookShown && f < 20 + 8) lookEarly = true;   // not before it has been held a moment
            if (ci2.LookShown && ci2.Look.x > 0.3f) looked2 = true;
        }
        Check($"a second finger held turns the camera while the first walks (look={looked2}, walk held={walked2}, too early={lookEarly})", looked2 && walked2 && !lookEarly);
        // 3. a quick tap on the right: a tap (a jump), and the camera never moved
        r = Play(Fresh(), f => f < 6 ? One(3, new Vector2(800f, 1400f) + new Vector2(f * 1.5f, 0f)) : None(), 10);
        Check($"quick tap on the right is a tap (tap={r.tap} look={r.looked} last={r.last})", r.tap && !r.looked && !r.swipe);
        // 4. a quick tap on the left: a tap too (the move stick never got going)
        r = Play(Fresh(), f => f < 5 ? One(4, new Vector2(250f, 900f)) : None(), 8);
        Check($"quick tap on the left is a tap (tap={r.tap} moved={r.moved})", r.tap && !r.moved);
        // 5. a flick up on the right, let go: a swipe, up the screen, and no camera turn on the way
        r = Play(Fresh(), f => f < 9 ? One(5, new Vector2(820f, 900f) + new Vector2(f * 4f, f * 60f)) : None(), 12);
        Check($"flick up on the right is a swipe up (swipe={r.swipe} dir={r.swipeDir} look={r.looked})", r.swipe && r.swipeDir.y > 0.9f && !r.looked);
        // 6. a flick on the left (the move side) is a roll too
        r = Play(Fresh(), f => f < 8 ? One(6, new Vector2(300f, 900f) + new Vector2(f * 55f, 0f)) : None(), 11);
        Check($"flick right on the left side is a swipe right (swipe={r.swipe} dir={r.swipeDir})", r.swipe && r.swipeDir.x > 0.9f);
        // 7. a slow drag on the right that lets go is a walk, not a roll
        r = Play(Fresh(), f => f < 30 ? One(7, new Vector2(820f, 900f) + new Vector2(0f, f * 9f)) : None(), 33);
        Check($"slow drag on the right is not a swipe (swipe={r.swipe} moved={r.moved} last={r.last})", !r.swipe && r.moved);
        // 8. walking with the left thumb while the right taps: both at once
        var ci8 = Fresh(); bool tapWhileMoving = false, movedThrough = true;
        for (int f = 0; f < 40; f++)
        {
            list.Clear(); list.Add(new CampInput.Touch(10, new Vector2(300f, 700f + Mathf.Min(f, 10) * 20f)));
            if (f >= 20 && f < 25) list.Add(new CampInput.Touch(11, new Vector2(900f, 1500f)));
            ci8.Step(list, Dt, w, h, Hit, false);
            if (f > 12 && !(ci8.MoveShown && ci8.Move.y > 0.5f)) movedThrough = false;
            if (ci8.Tapped && ci8.MoveShown) tapWhileMoving = true;
        }
        Check($"tap with the right thumb while walking with the left (tap while moving={tapWhileMoving}, walk held={movedThrough})", tapWhileMoving && movedThrough);
        // 9. buttons take their own touches: no tap, no walk
        r = Play(Fresh(), f => f < 4 ? One(12, new Vector2(w - 200f, h - 100f)) : None(), 6);
        Check($"a button takes its touch (button={r.button} tap={r.tap})", r.button == "missions" && !r.tap);
        // 10. the context button opens on a tap's lift, never on touching down, and is no jump
        r = Play(Fresh(), f => f < 5 ? One(13, new Vector2(w * 0.5f, 300f)) : None(), 7);
        Check($"the context button opens on lifting a tap (context={r.context} tap={r.tap} button={r.button})", r.context && !r.tap && r.button == null);
        // 11. a finger already down when you arrive is ignored until it lifts
        var ci11 = Fresh(); var down = new List<CampInput.Touch> { new CampInput.Touch(14, new Vector2(300f, 700f)) };
        ci11.IgnoreCurrent(down);
        bool any = false;
        for (int f = 0; f < 10; f++) { ci11.Step(f < 6 ? down : new List<CampInput.Touch>(), Dt, w, h, Hit, false); any |= ci11.Tapped || ci11.MoveShown; }
        Check($"the touch that brought you into the camp does nothing (anything={any})", !any);
    }

    // ---- the real thing, scripted fingers through Camp.Step
    static void Scripted(RenderTexture rt)
    {
        camp.SetNight(0f);
        camp.Teleport(-4f, -58f, 90f);
        var fingers = new List<CampInput.Touch>();
        Camp.TestTouches = fingers;
        for (int i = 0; i < 30; i++) Frame(rt, null);   // settle (the first frames come out half lit)
        string seq = Path.Combine(outDir, "seq_moves"); Directory.CreateDirectory(seq);
        int shot = 0; int jumps = 0, lands = 0, rolls = 0, furrows = 0; float camYaw0 = cam.transform.eulerAngles.y;
        camp.Jumped += _ => jumps++; camp.Landed += (a, b, c) => { lands++; Log($"landed {c:0.0} m/s at {a}"); };
        camp.Rolled += (a, b) => { rolls++; Log($"roll toward {b}"); }; camp.Furrowed += (a, b) => furrows++;
        Camp.TestTouches = new List<CampInput.Touch>(); camp.Step(Dt, meta);   // (a frame first: where the walk stick sits)
        Vector2 L0 = camp.Controls.Home ?? new Vector2(W * 0.25f, H * 0.22f), R0 = new Vector2(W * 0.74f, H * 0.42f);
        bool peakShot = false, rollShot = false; Vector3 landAt = Vector3.zero, rollFrom = Vector3.zero; float lastKind = 0;
        float yawLook0 = 0f;
        for (float t = 0f; t < 7.2f; t += Dt)
        {
            fingers.Clear();
            // the left thumb: down at 0.3 s, pushed up the screen (walk, then nearly a run), held throughout
            if (t > 0.3f && t < 6.6f) fingers.Add(new CampInput.Touch(1, L0 + new Vector2(0f, Mathf.Min(1f, (t - 0.3f) / 0.4f) * (t > 3.8f ? 150f : 95f))));
            // a tap with the right thumb at 1.3 s: jump
            if (t > 1.3f && t < 1.38f) fingers.Add(new CampInput.Touch(2, R0));
            // hold the right thumb and drag it right, 2.7 to 3.6 s: the camera swings round
            if (t > 2.7f && t < 3.6f) fingers.Add(new CampInput.Touch(3, R0 + new Vector2(Mathf.Clamp01((t - 2.9f) / 0.3f) * 90f, 0f)));
            // a flick up with the right thumb at 4.6 s, let go: roll the way the camera looks
            if (t > 4.6f && t < 4.76f) fingers.Add(new CampInput.Touch(4, R0 + new Vector2(0f, (t - 4.6f) / 0.16f * 260f)));
            if (Mathf.Abs(t - 2.7f) < Dt * 0.5f) yawLook0 = cam.transform.eulerAngles.y;
            if (Mathf.Abs(t - 3.2f) < Dt * 0.5f)
            {
                // where the sticks are drawn against where the thumbs are (the picture's pixels)
                foreach (var n in new[] { "stickRing", "lookRing" })
                {
                    var rtf = (RectTransform)typeof(MetaUi).GetField(n, NP).GetValue(meta);
                    Log($"{n} active={rtf.gameObject.activeSelf} anchored={rtf.anchoredPosition} drawn at {cam.WorldToScreenPoint(rtf.position)}");
                }
                var root = (RectTransform)meta.transform;
                Log($"thumbs: move {camp.Controls.MoveOrigin} look {camp.Controls.LookOrigin}; canvas rect {root.rect.size} scale {root.localScale} cam {cam.pixelRect}");
            }
            if (Mathf.Abs(t - 3.65f) < Dt * 0.5f) Log($"camera swung {Mathf.DeltaAngle(yawLook0, cam.transform.eulerAngles.y):0} deg round with the look stick");
            bool render = Mathf.RoundToInt(t / Dt) % 2 == 0;
            Frame(rt, render ? Path.Combine(seq, (shot++).ToString("D3") + ".png") : null);
            var g = camp.Controls.LastGesture;
            if (camp.MoveKind != lastKind) { Log($"t={t:0.00} move {lastKind} -> {camp.MoveKind} (last gesture {g})"); lastKind = camp.MoveKind; }
            if (camp.MoveKind == 1 && !peakShot && camp.MoveTime > 0.45f) { peakShot = true; Frame(rt, Path.Combine(outDir, "JumpPeak.png")); }
            if (camp.MoveKind == 2 && !rollShot && camp.MoveTime > 0.38f) { rollShot = true; rollFrom = camp.WalkerPosition; Frame(rt, Path.Combine(outDir, "RollMid.png")); }
            if (lands == 1 && landAt == Vector3.zero) landAt = camp.WalkerPosition;
        }
        fingers.Clear();
        for (int i = 0; i < 40; i++) Frame(rt, null);
        Check($"a tap while walking jumped and landed (jumps={jumps}, landings={lands})", jumps == 1 && lands == 1);
        Check($"a flick rolled (rolls={rolls}) and pressed a furrow (marks={furrows})", rolls == 1 && furrows >= 2);
        Check($"the footprints carried on ({camp.FootprintCount})", camp.FootprintCount > 4);
        // one thumb walks and turns: pushed to the side, the hero turns that way and the camera comes round behind
        // (no second finger anywhere near it)
        camp.Teleport(-4f, -58f, 90f);
        for (int i = 0; i < 20; i++) Frame(rt, null);
        float yawA = cam.transform.eulerAngles.y;
        for (float t = 0f; t < 3f; t += Dt)
        {
            fingers.Clear();
            fingers.Add(new CampInput.Touch(30, L0 + new Vector2(Mathf.Min(1f, t / 0.3f) * 80f, 20f)));   // the thumb on the walk stick (fixed, bottom left)
            Frame(rt, Mathf.Abs(t - 2.5f) < Dt * 0.5f ? Path.Combine(outDir, "OneStickTurn.png") : null);
        }
        fingers.Clear();
        float swung = Mathf.DeltaAngle(yawA, cam.transform.eulerAngles.y);
        Check($"one thumb pushed sideways turns the hero and the camera follows round behind (camera swung {swung:0} deg)", Mathf.Abs(swung) > 35f);
        // turned by hand with a second finger, it stays put for a moment instead of swinging straight back
        for (int i = 0; i < 20; i++) Frame(rt, null);
        float yawB = cam.transform.eulerAngles.y, yawHeld = 0f;
        for (float t = 0f; t < 2.2f; t += Dt)
        {
            fingers.Clear();
            fingers.Add(new CampInput.Touch(31, L0 + new Vector2(0f, Mathf.Min(1f, t / 0.3f) * 95f)));
            if (t > 0.4f && t < 1.3f) fingers.Add(new CampInput.Touch(32, R0 + new Vector2(Mathf.Clamp01((t - 0.6f) / 0.3f) * 90f, 0f)));
            Frame(rt, null);
            if (Mathf.Abs(t - 1.7f) < Dt * 0.5f) yawHeld = cam.transform.eulerAngles.y;   // once the camera has caught up with the turn
        }
        fingers.Clear();
        float byHand = Mathf.DeltaAngle(yawB, yawHeld), drift = Mathf.DeltaAngle(yawHeld, cam.transform.eulerAngles.y);
        Check($"a second finger held swings the camera by hand ({byHand:0} deg), and it holds there for a moment after (drifted {drift:0} deg in 0.5 s)", Mathf.Abs(byHand) > 25f && Mathf.Abs(drift) < 6f);
        // the marks, from above
        Look(landAt + new Vector3(0.6f, 3.2f, -1.6f), landAt, 45f); Frame(rt, Path.Combine(outDir, "JumpPrints.png"), false);
        Look(rollFrom + new Vector3(1.4f, 4.2f, -2.2f), rollFrom, 50f); Frame(rt, Path.Combine(outDir, "RollFurrow.png"), false);
        Log($"steps: {camp.FootstepsState}");
    }

    /// <summary>A stick as MetaUi draws it: a faint ring round where the thumb went down, a brighter knob under it.</summary>
    static void Stick(Texture2D t, Vector2 o, Vector2 p, float r, Color c)
    {
        Vector2 k = o + Vector2.ClampMagnitude(p - o, r);
        int x0 = Mathf.Max(0, (int)(o.x - r - 2)), x1 = Mathf.Min(t.width - 1, (int)(o.x + r + 2));
        int y0 = Mathf.Max(0, (int)(o.y - r - 2)), y1 = Mathf.Min(t.height - 1, (int)(o.y + r + 2));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), o), dk = Vector2.Distance(new Vector2(x, y), k);
                float a = 0f;
                if (Mathf.Abs(d - r) < 3f) a = 0.45f; else if (d < r) a = 0.12f;
                if (dk < r * 0.45f) a = Mathf.Max(a, 0.65f);
                if (a > 0f) t.SetPixel(x, y, Color.Lerp(t.GetPixel(x, y), c, a));
            }
    }
    static void Look(Vector3 from, Vector3 at, float fov) { cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov; }

    static void Frame(RenderTexture rt, string file, bool step = true)
    {
        if (step)
        {
            camp.Step(Dt, meta);
            body.PhysicsStep();
            body.Apply(Dt);
            camp.CameraStep(Dt);
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            aLate.Invoke(atmo, null);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name != "Snowfall") ps.Simulate(Dt, false, false, false);
        }
        if (file == null) return;
        // the sticks: drawn on the picture where the phone draws them (its menus are an overlay there; drawn through a
        // camera, as here, the canvas's rings land off their thumbs, so they're hidden and painted on below)
        meta.SetStick(false, Vector2.zero, Vector2.zero); meta.SetLookStick(false, Vector2.zero, Vector2.zero);
        Canvas.ForceUpdateCanvases();
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
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        var ci = camp.Controls;
        float r = 150f * Mathf.Sqrt(W / 1080f * H / 1920f);   // MetaUi's 150-unit stick, at this picture's scale
        if (ci.MoveShown && step) Stick(tex, ci.MoveOrigin, ci.MovePos, r, new Color(1f, 1f, 1f));
        if (ci.LookShown && step) Stick(tex, ci.LookOrigin, ci.LookPos, r, new Color(0.75f, 0.88f, 1f));
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        RenderTexture.active = null;
    }
}
