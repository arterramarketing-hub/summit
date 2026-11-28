using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// The one-finger controls, driven by a scripted finger through the real RiderInput and Rider. Each JUMP style
/// (DOUBLE TAP, TAP, FLICK UP), with drag steering and with tilt steering:
/// - on the snow: a single tap, a double tap, a nervous lift-and-retouch, a flick up, a second finger's tap;
/// - in a big air: three single taps while holding a spin, a double tap;
/// - on a rail: a deliberate switch-up, then a single tap and a double tap with the finger held at full lock.
/// Each line prints what fired: jumps off the snow, pops off a rail, grab steps, switch-ups. Lines start INPUT;
/// Then the camp's walk stick (CampInput, fed made-up fingers on an iPhone-sized screen): it stays the thumb's until
/// it lifts, past the rim at full strength with the base following, through other fingers' taps, buttons and the
/// look stick, through a dropped frame, from the station button, and nothing after it lifts. And the fixed stick the
/// camp uses (bottom left): only a finger on it walks, its base stays put, a finger anywhere else looks, taps and flicks.
/// /// expectations that fail are marked "!!" and counted.
///
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod InputTest.Run -logFile input.log
/// </summary>
public static class InputTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider r; static RiderBody body; static RiderInput input; static SlopeStreamer terrain; static Forest props;
    static MethodInfo riderUpdate; static FieldInfo grabField;
    static int jumps, pops, switches, fails, checks;
    static string grindNames = "";
    static Vector2 centre;

    public static void Run()
    {
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        r = game.rider;
        body = game.body;
        input = game.input;
        terrain = game.terrain;
        props = game.props;
        riderUpdate = typeof(Rider).GetMethod("Update", NP);
        grabField = typeof(Rider).GetField("grabLevel", NP);
        r.Jumped += () => { if (wasGrinding) pops++; else jumps++; };
        r.Link += l => { if (l.kind == LinkKind.Grind && onRailWatch) grindNames += " " + l.name; };
        RiderInput.TestDrive = true;
        centre = new Vector2(Mathf.Max(Screen.width, 320) * 0.5f, Mathf.Max(Screen.height, 480) * 0.4f);
        try
        {
            TestBoot.NewMountain(game, 1000, terrain, props);
            foreach (bool tilt in new[] { false, true })
                foreach (JumpStyle style in new[] { JumpStyle.DoubleTap, JumpStyle.Tap, JumpStyle.FlickUp })
                {
                    input.useTilt = tilt; input.jumpStyle = style;
                    Debug.Log($"INPUT ==== {(tilt ? "TILT" : "DRAG")} steering, JUMP: {RiderInput.JumpNames[(int)style]}");
                    Snow(style, tilt);
                    Air(style, tilt);
                    Rail(style, tilt);
                }
            Debug.Log("INPUT ==== CAMP walk stick (floating, as the logic still allows)");
            CampStick();
            Debug.Log("INPUT ==== CAMP walk stick, fixed in the bottom left (as the camp has it)");
            CampStickFixed();
            Debug.Log(fails == 0 ? $"INPUT PASS ({checks} checks)" : $"INPUT FAIL ({fails} of {checks} checks)");
        }
        catch (System.Exception e) { Debug.LogError("INPUT exception " + e); fails++; }
        RiderInput.TestDrive = false; RiderInput.TestFingers = 0;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(fails == 0);
    }

    static bool wasGrinding, onRailWatch;

    // ---------------------------------------------------------------- the stepping

    static void Step(int fingers, Vector2 pos)
    {
        RiderInput.TestFingers = fingers; RiderInput.TestPos = pos;
        wasGrinding = r.Grinding;
        input.TestStep(Dt);
        riderUpdate.Invoke(r, null);
        TestHarness.Step(r, body, Dt);
        game.TestPoints(Dt);
        // a switch-up or a press: the stance on the rail changed while still on it
        if (r.Grinding)
        {
            int st = (int)quarterF.GetValue(r) + 4 * ((int)pressF.GetValue(r) + 1);
            if (onRailWatch && wasGrinding && st != stance) switches++;
            stance = st;
        }
    }
    static FieldInfo quarterF = typeof(Rider).GetField("grindQuarter", NP), pressF = typeof(Rider).GetField("grindPress", NP);
    static int stance;

    static void Frames(float secs, int fingers, Vector2 pos) { for (float t = 0f; t < secs - 1e-4f; t += Dt) Step(fingers, pos); }

    // a finger: a tap (down 0.08 s, then up for `up` s), a double tap, a flick up
    static void Tap(Vector2 at, float up = 0.12f) { Frames(0.08f, 1, at); Frames(up, 0, at); }
    static void DoubleTap(Vector2 at) { Tap(at, 0.12f); Frames(0.08f, 1, at); }
    static void Flick(Vector2 from)
    {
        float hgt = Mathf.Max(Screen.height, 480);
        for (float t = 0f; t < 0.1f; t += Dt) Step(1, from + Vector2.up * (hgt * 0.14f * t / 0.1f));
        Frames(0.1f, 1, from + Vector2.up * hgt * 0.14f);
    }

    static void Start(float x, float z, float v0)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
        terrain.Prewarm(); props.Prewarm();
        RiderInput.TestFingers = 0;
        input.TestStep(Dt); input.SwallowCurrentPress();
        jumps = pops = switches = 0; grindNames = ""; onRailWatch = false;
    }

    static void Expect(string label, bool ok, string got)
    {
        checks++;
        if (!ok) fails++;
        Debug.Log($"INPUT {(ok ? "  " : "!!")} {label,-58} {got}");
    }

    static string Fired() => $"jumps {jumps}, pops {pops}, switch-ups {switches}";

    // ---------------------------------------------------------------- on the snow

    static void Snow(JumpStyle style, bool tilt)
    {
        float z = Course.First + 110f;
        // (steering by hand on the snow: the rider goes where the finger says; down the piste straight is fine)
        void Fresh() { Start(Slope.Centre(z), z, 16f); r.input = input; Frames(0.5f, 1, centre); jumps = 0; }

        Fresh(); Frames(0.35f, 0, centre); Tap(centre, 0.5f); Frames(0.6f, 1, centre);
        Expect("snow: a single tap", jumps == (style == JumpStyle.Tap ? 1 : 0), Fired());

        Fresh(); Frames(0.3f, 0, centre); DoubleTap(centre); Frames(0.6f, 1, centre);
        Expect("snow: a double tap", style == JumpStyle.DoubleTap ? jumps == 1 : style == JumpStyle.Tap ? jumps >= 1 : jumps == 0, Fired());

        // steering a long while, the finger lifted and put back nervously, twice, slowly
        Fresh(); Frames(1f, 1, centre + Vector2.right * 40f); Frames(0.18f, 0, centre); Frames(0.6f, 1, centre + Vector2.right * 40f);
        Frames(0.35f, 0, centre); Frames(0.6f, 1, centre);
        Expect("snow: a nervous lift-and-retouch", jumps == 0, Fired());

        Fresh(); Flick(centre); Frames(0.6f, 1, centre);
        Expect("snow: a flick up while steering", jumps == (style == JumpStyle.FlickUp ? 1 : 0), Fired());

        if (!tilt)
        {
            // the old two-handed way: a second finger's quick tap, whatever the setting
            Fresh(); Frames(0.08f, 2, centre); Frames(0.6f, 1, centre);
            Expect("snow: a second finger's tap", jumps == 1, Fired());
        }
    }

    // ---------------------------------------------------------------- in the air

    static void Air(JumpStyle style, bool tilt)
    {
        foreach (bool dbl in new[] { false, true })
        {
            // the run's first jump cliff: the long air the taps need (three taps and a push to full lock are 1.8 s; a
            // kicker gives about that and no more). (It always was the jump cliff: the kicker this asked for, at z 102,
            // was one never built, on the cliff's in-run, until NextKicker stopped handing out kickers that are not built.)
            Vector3 at;
            if (Cliffs.JumpOf(0, 0, out var jc)) { float lz = Cliffs.LipZ(jc, 0f); at = new Vector3(Slope.Centre(lz), Slope.Height(Slope.Centre(lz), lz), lz); }
            else Slope.NextKicker(100f, out at);
            Start(at.x, at.z - 30f, 30f);
            float dx = at.x - Slope.Centre(at.z - 30f);
            // the bot rides it in; the finger takes over as the board leaves the lip, holding a spin (drag) or still (tilt)
            for (float t = 0f; t < 6f && !(r.Airborne && r.Z > at.z && r.AirTime > 0.1f); t += Dt)
            {
                r.Steer = Mathf.Clamp((Slope.Centre(r.Z + 12f) + dx - r.transform.position.x) * 0.06f - r.Heading * 0.8f, -1f, 1f);
                Step(0, centre);
            }
            if (!r.Airborne) { Expect("air: got airborne off the kicker", false, "never left the snow"); continue; }
            r.input = input;
            float radius = Mathf.Max(60f, Mathf.Max(Screen.width, 320) * input.stickRadiusFrac);
            // drag: the finger lands and pushes over to full lock, the spin, and stays there between taps (each tap
            // a moment off the glass and straight back); tilt: the finger only comes down to tap
            Vector2 hold = centre;
            int rest = tilt ? 0 : 1;
            if (!tilt) { Frames(0.05f, 1, centre); hold = centre + Vector2.right * radius; Frames(0.15f, 1, hold); }
            jumps = 0;
            int spin0 = SpinHalves();
            if (!dbl)
            {
                for (int k = 0; k < 3; k++) { Frames(0.06f, 0, hold); Frames(0.08f, 1, hold); Frames(0.06f, 0, hold); Frames(0.3f, rest, hold); }
                int grabs = (int)grabField.GetValue(r);
                Expect("air: three taps" + (tilt ? "" : " while holding the spin"), grabs == 3 && jumps == 0 && r.Airborne,
                       $"grab steps {grabs}, spin {spin0} -> {SpinHalves()} half turns, {Fired()}{(r.Airborne ? "" : " (landed before the taps were done)")}");
            }
            else
            {
                Frames(0.06f, 0, hold); DoubleTap(hold); Frames(0.3f, rest, hold);
                int grabs = (int)grabField.GetValue(r);
                // (in the air every quick press is a grab, the bigger for each: a double tap is two grab taps, or one
                // when the second touch stays down to steer; it is never a jump, now or at the landing)
                Expect("air: a double tap (grab taps only, no jump)", grabs == (tilt ? 2 : 1) && jumps == 0, $"grab steps {grabs}, {Fired()}");
            }
            for (float t = 0f; t < 6f && r.Airborne && !r.Crashed; t += Dt) Step(rest, hold);
            Frames(0.4f, rest, hold);
            Expect(dbl ? "air: ... and nothing queued for the landing" : "air: ... and nothing queued for the landing", jumps == 0, Fired() + (r.Crashed ? " (crashed on landing)" : ""));
        }
    }

    static int SpinHalves() => (int)typeof(Rider).GetField("spinHalves", NP).GetValue(r);

    // ---------------------------------------------------------------- on a rail

    static Course.Feature Feature(Course.Kind kind)
    {
        for (int c = 0; c < 3; c++)
            foreach (var f in Course.FeaturesOf(c)) if (f.kind == kind) return f;
        return default;
    }

    static bool OntoRail(Course.Feature f)
    {
        Start(f.a.x, f.a.z - 18f, 14f);
        for (float t = 0f; t < 6f && !r.Grinding; t += Dt)
        {
            float tx = r.Z < f.a.z ? f.a.x : Mathf.Lerp(f.a.x, f.b.x, Mathf.InverseLerp(f.a.z, f.b.z, r.Z));
            r.Steer = Mathf.Clamp((tx - r.transform.position.x) * 0.4f - r.Heading * 1.5f, -1f, 1f);
            Step(0, centre);
        }
        return r.Grinding;
    }

    static void Rail(JumpStyle style, bool tilt)
    {
        var f = Feature(Course.Kind.Rail);
        foreach (bool dbl in new[] { false, true })
        {
            if (!OntoRail(f)) { Expect("rail: got onto it", false, "never boarded"); continue; }
            r.input = input; onRailWatch = true; switches = 0; pops = 0; jumps = 0;
            float radius = Mathf.Max(60f, Mathf.Max(Screen.width, 320) * input.stickRadiusFrac);
            if (!tilt) Frames(0.05f, 1, centre);
            Vector2 lockPos = centre;
            if (!tilt)
            {
                // a deliberate switch-up: the finger flicked over to full lock, and held there
                lockPos = centre + Vector2.right * radius;
                for (float t = 0f; t < 0.06f; t += Dt) Step(1, Vector2.Lerp(centre, lockPos, t / 0.06f));
                Frames(0.12f, 1, lockPos);
            }
            int deliberate = switches;
            if (!r.Grinding) { Expect("rail: still on it after the switch-up", false, "fell off"); continue; }
            // a single tap, then the finger back down after a beat (longer than a double tap's gap); or a double tap
            if (!dbl) { Frames(0.06f, 0, lockPos); Tap(lockPos, 0.35f); Frames(0.2f, tilt ? 0 : 1, lockPos); }
            else { Frames(0.06f, 0, lockPos); DoubleTap(lockPos); Frames(0.2f, tilt ? 0 : 1, lockPos); }
            bool stillOn = r.Grinding || pops > 0;
            int extra = switches - deliberate;
            bool wantPop = dbl || style == JumpStyle.Tap;
            Expect(dbl ? "rail: a double tap" + (tilt ? "" : " at full lock") : "rail: a single tap" + (tilt ? "" : " at full lock"), (pops == 1) == wantPop && extra == 0 && (stillOn || !wantPop),
                   $"pops {pops}, switch-ups from it {extra} (the deliberate one: {deliberate}),{grindNames}{(stillOn ? "" : " (ran off the end first)")}");
            onRailWatch = false;
            for (float t = 0f; t < 3f && (r.Grinding || r.Airborne); t += Dt) Step(1, lockPos);
        }
    }

    // ---------------------------------------------------------------- the camp's walk stick

    static void CampStick()
    {
        const float w = 1170f, h = 2532f, cdt = 1f / 60f;   // an iPhone's screen, in pixels
        // the menus up top, the station button low in the middle (as the camp's are)
        string Hit(Vector2 p) => p.y > h - 220f && p.x > w - 450f ? "missions" : (p.y > 230f && p.y < 370f && Mathf.Abs(p.x - w * 0.5f) < 330f ? "context" : null);
        var ci = new CampInput();
        var list = new List<CampInput.Touch>();
        bool panel = false;
        void Frame(params (int id, Vector2 p)[] fs) { list.Clear(); foreach (var f in fs) list.Add(new CampInput.Touch(f.id, f.p)); ci.Step(list, cdt, w, h, Hit, panel); }
        float R() => ci.Radius;
        string Got() => $"move ({ci.Move.x:0.00},{ci.Move.y:0.00}) |{ci.Move.magnitude:0.00}| shown {ci.MoveShown}, fingers {ci.Count}, last {ci.LastGesture}";
        var a = new Vector2(400f, 700f);

        // the thumb down and dragged straight up past the rim, three times over: the base follows, full strength
        Frame((1, a));
        for (int f = 1; f <= 30; f++) Frame((1, a + Vector2.up * R() * 3f * f / 30f));
        var thumb = a + Vector2.up * R() * 3f;
        Expect("camp: dragged 3 radii past the rim, still the stick", ci.MoveShown && ci.Move.magnitude > 0.99f && ci.Move.y > 0.99f, Got());
        Expect("camp: the base followed the thumb to the rim", Mathf.Abs((ci.MovePos - ci.MoveOrigin).magnitude - R()) < 1f && ci.MoveOrigin.y > a.y + R() * 1.9f, $"base {ci.MoveOrigin}, thumb {ci.MovePos}, radius {R():0}");
        // exactly at the rim, sideways: full strength
        var side = ci.MoveOrigin + Vector2.right * R();
        Frame((1, side));
        Expect("camp: at the rim, full strength", ci.Move.magnitude > 0.99f, Got());
        thumb = side;

        // another finger taps the open snow, then a button, then holds and drags (the look stick): the walk holds
        bool held = true; bool tapped = false, button = false, looked = false;
        for (int f = 0; f < 6; f++) { Frame((1, thumb), (2, new Vector2(900f, 1500f))); held &= ci.Move.magnitude > 0.99f; }
        Frame((1, thumb)); tapped = ci.Tapped; held &= ci.Move.magnitude > 0.99f;
        Frame((1, thumb), (3, new Vector2(w - 200f, h - 100f))); button = ci.Button == "missions"; held &= ci.Move.magnitude > 0.99f;
        Frame((1, thumb));
        for (int f = 0; f < 40; f++) { Frame((1, thumb), (4, new Vector2(800f + f * 6f, 1400f))); held &= ci.Move.magnitude > 0.99f; looked |= ci.LookShown; }
        Frame((1, thumb)); held &= ci.Move.magnitude > 0.99f;
        Expect("camp: a tap, a button, the look stick: none takes the walk", held && tapped && button && looked, $"held {held}, tap {tapped}, button {button}, look {looked}; {Got()}");

        // the phone drops the touch for a frame and it comes back (a new touch) by the same spot: still walking
        Frame();
        bool dipped = ci.Move == Vector2.zero;
        Frame((5, thumb + new Vector2(6f, -4f)));
        Expect("camp: a touch dropped a frame picks the stick up again", dipped && ci.MoveShown && ci.Move.magnitude > 0.97f && !ci.Tapped, Got());
        for (int f = 0; f < 20; f++) Frame((5, thumb + new Vector2(6f, -4f)));

        // let go: nothing walks, nothing stays bound, and a quick tap afterwards is a tap (a jump), not a walk
        Frame();
        bool gone = ci.Move == Vector2.zero && !ci.MoveShown && ci.Count == 0 && !ci.Tapped;
        bool stillGone = true;
        for (int f = 0; f < 30; f++) { Frame(); stillGone &= ci.Move == Vector2.zero && !ci.MoveShown; }
        Expect("camp: lifted, it lets go at once and stays let go", gone && stillGone, Got());
        bool tap2 = false; bool walked = false;
        for (int f = 0; f < 5; f++) { Frame((6, thumb)); walked |= ci.Move != Vector2.zero; }
        Frame(); tap2 = ci.Tapped;
        Expect("camp: a quick tap after the walk is a tap", tap2 && !walked, Got());

        // a thumb that lands on the station button and drags off walks (and doesn't press it)
        for (int f = 0; f < 30; f++) Frame();
        var btn = new Vector2(w * 0.5f, 300f);
        Frame((7, btn));
        for (int f = 1; f <= 20; f++) Frame((7, btn + Vector2.up * R() * 1.5f * f / 20f));
        bool walks = ci.MoveShown && ci.Move.magnitude > 0.99f;
        Frame();
        Expect("camp: a drag from the station button walks", walks && !ci.ContextTapped, Got());
        // (a tap on it still presses it)
        for (int f = 0; f < 30; f++) Frame();
        Frame((8, btn)); Frame((8, btn)); Frame();
        Expect("camp: a tap on the station button still presses it", ci.ContextTapped, Got());

        // a panel opening mid-walk: the thumb keeps its stick (the camp stands still while the panel is up), and
        // walks again when it closes; a finger put down on the panel never walks
        for (int f = 0; f < 30; f++) Frame();
        Frame((9, a)); for (int f = 1; f <= 10; f++) Frame((9, a + Vector2.up * R() * f / 10f));
        panel = true; for (int f = 0; f < 10; f++) Frame((9, a + Vector2.up * R()), (10, new Vector2(600f, 1200f)));
        bool bound = ci.Move.magnitude > 0.99f;
        // closed: the thumb walks again; the finger put down on the panel, now dragged on the open snow, doesn't
        panel = false;
        bool again = true;
        for (int f = 0; f < 20; f++) { Frame((9, a + Vector2.up * R()), (10, new Vector2(600f + f * 15f, 1200f))); again &= ci.Move.magnitude > 0.99f && !ci.LookShown; }
        Frame();
        Expect("camp: through a panel the thumb keeps its stick", bound && again && ci.Count == 0, $"bound {bound}, again {again}; {Got()}");
    }
    static void CampStickFixed()
    {
        const float w = 1170f, h = 2532f, cdt = 1f / 60f;   // an iPhone's screen, in pixels
        string Hit(Vector2 p) => p.y > h - 220f && p.x > w - 450f ? "missions" : null;
        var ci = new CampInput();
        var list = new List<CampInput.Touch>();
        float R = CampInput.RadiusFor(w, h);
        var home = new Vector2(43f + R, 43f + R);   // (where MetaUi puts it: the margin in from the corner of the safe area)
        ci.Home = home;
        void Frame(params (int id, Vector2 p)[] fs) { list.Clear(); foreach (var f in fs) list.Add(new CampInput.Touch(f.id, f.p)); ci.Step(list, cdt, w, h, Hit, false); }
        string Got() => $"move ({ci.Move.x:0.00},{ci.Move.y:0.00}) |{ci.Move.magnitude:0.00}| shown {ci.MoveShown}, look {ci.LookShown}, base {ci.MoveOrigin}, fingers {ci.Count}, last {ci.LastGesture}";
        void Lift() { for (int f = 0; f < 20; f++) Frame(); }

        // nothing down: nothing walks
        Frame();
        Expect("fixed: at rest, nothing walks", ci.Move == Vector2.zero && !ci.MoveShown, Got());
        // a thumb on the stick, a little off its centre, pushed up past the rim: full strength up, the base where it was
        var a = home + new Vector2(10f, -8f);
        Frame((1, a));
        for (int f = 1; f <= 30; f++) Frame((1, a + Vector2.up * R * 3f * f / 30f));
        Expect("fixed: pushed up past the rim, full strength up", ci.MoveShown && ci.Move.magnitude > 0.99f && ci.Move.y > 0.98f, Got());
        Expect("fixed: the base stays put, the knob at the rim", (ci.MoveOrigin - home).magnitude < 0.5f && Mathf.Abs((ci.MovePos - home).magnitude - R) < 1f, Got());
        // half way, to the right: half strength that way
        Frame((1, home + Vector2.right * R * 0.5f));
        Expect("fixed: half way right, half strength right", Mathf.Abs(ci.Move.x - 0.5f) < 0.02f && Mathf.Abs(ci.Move.y) < 0.02f, Got());
        // walking, another finger taps the snow, then holds and drags (the look stick): the walk holds
        bool held = true, tapped = false, looked = false;
        for (int f = 0; f < 6; f++) { Frame((1, home + Vector2.up * R), (2, new Vector2(800f, 1500f))); held &= ci.Move.y > 0.99f; }
        Frame((1, home + Vector2.up * R)); tapped = ci.Tapped;
        for (int f = 0; f < 40; f++) { Frame((1, home + Vector2.up * R), (3, new Vector2(800f + f * 6f, 1400f))); held &= ci.Move.y > 0.99f; looked |= ci.LookShown && ci.Look.x > 0.2f; }
        Frame((1, home + Vector2.up * R));
        Expect("fixed: a tap and the look stick beside it: the walk holds", held && tapped && looked, $"held {held}, tap {tapped}, look {looked}; {Got()}");
        // the touch dropped a frame and back by the same spot: still walking
        Frame(); Frame((4, home + Vector2.up * R + new Vector2(5f, 3f)));
        Expect("fixed: a touch dropped a frame picks the stick up again", ci.MoveShown && ci.Move.magnitude > 0.97f, Got());
        Lift();
        Expect("fixed: lifted, nothing walks", ci.Move == Vector2.zero && !ci.MoveShown && ci.Count == 0, Got());

        // a finger off the stick, on its own: it never walks; held and dragged, it looks
        bool walked = false; looked = false;
        var off = new Vector2(w * 0.6f, h * 0.45f);
        for (int f = 0; f < 50; f++) { Frame((5, off + Vector2.right * Mathf.Min(f, 30) * 8f)); walked |= ci.Move != Vector2.zero; looked |= ci.LookShown && ci.Look.x > 0.5f; }
        Lift();
        Expect("fixed: one finger off the stick looks, never walks", looked && !walked, $"look {looked}, walked {walked}; {Got()}");
        // just outside the stick's reach: not the stick
        walked = false;
        var outside = home + new Vector2(1f, 1f).normalized * R * (CampInput.HomeReach + 0.2f);
        for (int f = 0; f < 30; f++) { Frame((6, outside + Vector2.up * Mathf.Min(f, 15) * 10f)); walked |= ci.MoveShown; }
        Lift();
        Expect("fixed: a finger just outside its reach isn't the stick", !walked, Got());
        // the edge of its reach still is
        var edge = home + Vector2.right * R * (CampInput.HomeReach - 0.1f);
        for (int f = 0; f < 10; f++) Frame((7, edge));
        Expect("fixed: a thumb at the edge of its reach walks (full strength that way)", ci.MoveShown && ci.Move.x > 0.99f, Got());
        Lift();
        // a quick tap and a flick off the stick: a jump and a roll
        Frame((8, off)); Frame((8, off)); Frame();
        bool tap = ci.Tapped;
        Lift();
        bool swipe = false;
        for (int f = 0; f <= 8; f++) Frame((9, off + Vector2.up * R * 1.2f * f / 8f));
        Frame(); swipe = ci.Swiped && ci.SwipeDir.y > 0.9f;
        Lift();
        Expect("fixed: off the stick, a tap jumps and a flick rolls", tap && swipe, $"tap {tap}, swipe {swipe}; {Got()}");
        // a button still takes its own touch
        Frame((10, new Vector2(w - 200f, h - 100f)));
        Expect("fixed: a button still takes its touch", ci.Button == "missions" && !ci.MoveShown, Got());
        Lift();
    }
}
