using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;
using BodyPart = Summit.BodyPart;

/// <summary>
/// The medical receipt, from scripted falls: a light tumble, a tree, a rock, a cliff, a crevasse, the avalanche,
/// and a whole run of three falls (its merged end-screen receipt). Each hit goes in the way the ragdoll reports it
/// (Ragdoll.Hit then Ragdoll.Struck, so RiderBody breaks the bone, the blood and the limbs have their say, and the
/// receipt hears RiderBody.Injured), with the real colliders: the ragdoll's own snow slab, a tree, a rock, a lift
/// tower. Each receipt is printed line by line with its total and checked against the game's medical figure. The
/// whole set is run twice and must come out the same.
///
/// Run: Unity -batchmode -nographics -quit -projectPath . -executeMethod ReceiptTest.Run -logFile receipt.log
///      (lines start RECEIPT)
/// </summary>
public static class ReceiptTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static List<string> lines;
    static int fails;
    static Collider snow, tree, rock, steel;

    public static void Run()
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
        try
        {
            TestBoot.NewMountain(game, 1000, terrain, props);
            var a = Pass();
            var b = Pass();
            int diff = 0;
            for (int i = 0; i < Mathf.Max(a.Count, b.Count); i++)
                if (i >= a.Count || i >= b.Count || a[i] != b[i]) { diff++; if (diff < 6) Debug.Log($"RECEIPT DIFF {i}: '{(i < a.Count ? a[i] : "-")}' vs '{(i < b.Count ? b[i] : "-")}'"); }
            Debug.Log(diff == 0 && fails == 0 ? $"RECEIPT PASS ({a.Count} lines, the same both times)" : $"RECEIPT FAIL ({diff} lines differ, {fails} checks failed)");
        }
        catch (System.Exception e) { Debug.LogError("RECEIPT exception " + e); }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(0);
    }

    static List<string> Pass()
    {
        lines = new List<string>();
        Random.InitState(1234);   // (RiderBodyLimbs picks which limb a fatal fall takes)

        Fall("light tumble, snowboard, on the piste", RideKind.Snowboard, "Slammed!", () =>
        {
            Strike(BodyPart.Pelvis, snow, 3.0f, Vector3.up);     // sat down hard: nothing yet
            Strike(BodyPart.ForearmL, snow, 6.0f, Vector3.up);   // the hand out to break it: the first real hit, it breaks
            Strike(BodyPart.ArmR, snow, 7.0f, Vector3.up);
            Strike(BodyPart.Head, snow, 4.0f, Vector3.up);
        });

        Fall("tree at speed, skis", RideKind.Skis, "Tree", () =>
        {
            var c = body.BoneOf(BodyPart.Chest).position;
            Vector3 n = TreeNormal(c);
            Strike(BodyPart.Chest, tree, 15f, n, side: -1);      // left side into the trunk: breaks; the lung
            Strike(BodyPart.Head, tree, 9f, n);
            Strike(BodyPart.ThighR, snow, 11f, Vector3.up);      // a big second hit: the knee
            Strike(BodyPart.ShinL, snow, 6f, Vector3.up);
        });

        Fall("rock garden, snowboard, face first", RideKind.Snowboard, "Rock", () =>
        {
            Strike(BodyPart.Head, rock, 12f, Vector3.up, face: true);   // face into the rock: breaks
            Strike(BodyPart.ShinL, rock, 14f, Vector3.up);               // the second break
            Strike(BodyPart.ArmR, snow, 8f, Vector3.up);
            Strike(BodyPart.Chest, rock, 9f, Vector3.up, side: 1);
        });

        Fall("lift tower, snowboard", RideKind.Snowboard, "Lift tower", () =>
        {
            Strike(BodyPart.Chest, steel, 21f, Vector3.up, side: 1);    // right side into the steel: the liver
            Strike(BodyPart.ArmL, steel, 13f, Vector3.up);
            Strike(BodyPart.Pelvis, snow, 9f, Vector3.up);
        });

        Fall("off a cliff, snowboard, 130 m", RideKind.Snowboard, "Cliff", () =>
        {
            Strike(BodyPart.Pelvis, snow, 34f, Vector3.up);
            Strike(BodyPart.ThighL, snow, 31f, Vector3.up);       // (hard enough to take the leg: RiderBodyLimbs)
            Strike(BodyPart.Chest, snow, 30f, Vector3.up);
            Strike(BodyPart.Head, snow, 26f, Vector3.up);
            body.Killed?.Invoke(130f);                            // the game's own fatal-fall event
        });

        Fall("down a crevasse, skis", RideKind.Skis, "Crevasse", () =>
        {
            SetBelowSnow(4f);                                     // in it: the walls are ice
            Strike(BodyPart.ShinR, snow, 9f, Vector3.up);
            Strike(BodyPart.Head, snow, 7f, Vector3.up);
            Strike(BodyPart.Chest, snow, 6f, Vector3.up);
            game.Bill.Crevasse();                                 // the game's call once the body is 3 m down
            SetBelowSnow(0f);
        });

        Fall("the avalanche, snowboard", RideKind.Snowboard, Avalanche.Cause, () =>
        {
            Strike(BodyPart.Chest, snow, 8f, Vector3.up);
            Strike(BodyPart.ShinR, snow, 7f, Vector3.up);
            game.Bill.Avalanche();                                // buried: the game's call as the end screen comes up
        });

        // hits taken while still riding: signs and SLOW banners' stakes (Breakables.RiderHit, the game's own event)
        Say("==== ridden through, still riding (snowboard)");
        Begin(RideKind.Snowboard);
        var breaks = (Breakables)typeof(SummitGame).GetField("breaks", NP).GetValue(game);
        Vector3 at = r.transform.position;
        foreach (var (kind, speed, part, cost) in new[] {
            (Breakable.Kind.Sign, 14f, "Shoulder", Breakables.SignCost(16f)), (Breakable.Kind.Sign, 26f, "Head", Breakables.SignCost(30f)),
            (Breakable.Kind.Sign, 20f, "Chest", Breakables.SignCost(25f)), (Breakable.Kind.Sign, 18f, "Legs", Breakables.SignCost(18f)),
            (Breakable.Kind.Banner, 17f, "Shin", Breakables.StakeCost(17f)), (Breakable.Kind.Banner, 24f, "Shin", Breakables.StakeCost(24f)) })
        {
            int before = game.Bill.All.Count;
            breaks.RiderHit?.Invoke(kind, speed, at, part, cost);
            for (int i = before; i < game.Bill.All.Count; i++)
            {
                var it = game.Bill.All[i];
                Say($"   {kind,-6} {part,-8} {speed,4:0} m/s -> {it.Name(true),-30} {"$" + it.cost.ToString("N0"),8}" + (it.soft != null ? $"   (blood off: {it.soft})" : ""));
            }
            if (game.Bill.All.Count == before) { fails++; Say($"   !! {kind} {part}: nothing on the receipt"); }
        }
        Say($"   {"  TOTAL",-58} {"$" + game.Bill.RunTotal.ToString("N0"),8}");
        Check();

        // a whole run: three falls, then the end screen's merged receipt
        Say("==== a run of three falls (snowboard) and its end-screen receipt");
        Begin(RideKind.Snowboard);
        Crash("Tree"); Strike(BodyPart.ForearmR, snow, 6f, Vector3.up); Strike(BodyPart.ArmR, snow, 7f, Vector3.up); Up();
        Crash("Slammed!"); Strike(BodyPart.ShinL, snow, 7f, Vector3.up); Strike(BodyPart.Head, snow, 10f, Vector3.up); Up();
        Crash("Tree"); Strike(BodyPart.Chest, tree, 13f, Vector3.up); Strike(BodyPart.ForearmL, snow, 6f, Vector3.up);
        game.Bill.Rest(); game.Bill.Close();
        foreach (bool gore in new[] { true, false })
        {
            Say(gore ? "-- end screen (blood on)" : "-- end screen (blood off)");
            foreach (var it in game.Bill.Merged(gore)) Say($"   {it.Name(gore) + (it.count > 1 ? "  x" + it.count : ""),-40} {"$" + it.cost.ToString("N0"),12}");
        }
        Say($"   {"TOTAL",-40} {"$" + game.Bill.RunTotal.ToString("N0"),12}");
        Check();

        // the closet: another rider in other clothes is a new body, so a new ragdoll; its hits must still reach the
        // receipt (each Strike is one RiderBody.Injured, and each fall bills its diagnoses), then back as worn
        var worn = Wardrobe.Worn();
        foreach (int who in new[] { 2, Wardrobe.PaulRow })   // (Val, then Paul: each a new body)
        {
            var o = (int[])worn.Clone();
            o[(int)Wardrobe.Cat.Rider] = who; o[(int)Wardrobe.Cat.Outfit] = Wardrobe.OwnOutfit(who);
            Swap(o);
            int heard = 0;
            System.Action<Injury> count = _ => heard++;
            body.Injured += count;
            Fall($"after the closet: rider {who} in outfit {o[(int)Wardrobe.Cat.Outfit]}, skis", RideKind.Skis, "Tree", () =>
            {
                Strike(BodyPart.Head, snow, 5f, Vector3.up);       // the first real hit: it breaks (RiderBody)
                Strike(BodyPart.Chest, tree, 13f, Vector3.up, side: -1);
                Strike(BodyPart.ShinR, snow, 9f, Vector3.up);
            });
            body.Injured -= count;
            int dx = 0;
            foreach (var it in game.Bill.Lines) if (!it.service) dx++;
            Say($"   (heard {heard} hits, {dx} diagnosis lines)");
            if (heard < 3 || dx == 0) { fails++; Say("   !! the new body's hits never reached the receipt"); }
        }
        Swap(worn, false);
        return lines;
    }

    /// <summary>The closet's swap (SummitGame.ApplyOutfit's body.Wear): a new body and ragdoll when the look changes.</summary>
    static void Swap(int[] o, bool check = true)
    {
        int built = body.Built;
        body.Wear(o);
        if (check && body.Built == built) { fails++; Say("   !! the swap built no new body"); }
        snow = null;   // (a new body's old ragdoll took its snow slab with it: found again on the next Begin)
    }

    // ---------------------------------------------------------------- a fall

    delegate void Hits();

    static void Fall(string label, RideKind ride, string why, Hits hits)
    {
        Say("==== " + label);
        Begin(ride);
        Random.InitState(label.Length * 7919);   // (the game's own draws in a fall: the limb a fatal fall takes, a wipeout's break)
        Crash(why);
        hits();
        game.Bill.Rest();
        foreach (bool gore in new[] { true, false })
        {
            if (!gore) { bool soft = false; foreach (var it in game.Bill.Lines) if (it.soft != null) soft = true; if (!soft) break; Say("-- with the blood off:"); }
            foreach (var it in game.Bill.Lines)
                if (gore || it.soft != null) Say($"   {(it.service ? " " : "*")} {it.Name(gore),-40} {"$" + it.cost.ToString("N0"),12}   g{it.grade}");
        }
        Say($"   {"  TOTAL",-42} {"$" + game.Bill.FallTotal.ToString("N0"),12}   ({game.Bill.Lines.Count} lines)");
        Check();
    }

    static void Begin(RideKind ride)
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        body.SetRide(ride); r.SetRide(body.Ride); r.Tube = false; body.SetTube(false);
        float z = Course.First + 110f, x = Slope.Centre(z);
        r.input = null;
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * 20f;
        terrain.Prewarm(); props.Prewarm();
        for (int i = 0; i < 6; i++) Step();
        if (snow == null) FindSurfaces();
    }

    static void Crash(string why)
    {
        r.DoCrash(why);
        Step();   // (the ragdoll takes over on the next physics step)
        Step();
        if (snow == null) FindSurfaces();
    }

    /// <summary>Up again: the receipt's services, closed, as when the rider stands up.</summary>
    static void Up()
    {
        game.Bill.Rest(); game.Bill.Close();
        r.ResetAt(r.transform.position.x, r.Z + 5f); body.ResetVisual(); r.Begin();
        for (int i = 0; i < 4; i++) Step();
    }

    static void Step()
    {
        r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
        game.TestPoints(Dt);
    }

    /// <summary>One hit, as the ragdoll reports it: Hit (the break), then Struck (the blood, the limbs, the receipt).</summary>
    static void Strike(BodyPart part, Collider what, float v, Vector3 n, bool face = false, int side = 0)
    {
        var rd = (Ragdoll)typeof(RiderBody).GetField("ragdoll", NP).GetValue(body);
        var bone = body.BoneOf(part);
        if (what == null) { Say($"   (no such surface near: {part} hits the snow instead)"); what = snow; }
        if (rd == null || !rd.Active || bone == null) { Say($"   !! no ragdoll to hit ({part})"); fails++; return; }
        // aim the surface so the hit lands where it is meant to: face first, or on the left or right of the torso
        Vector3 across = body.BoneOf(BodyPart.ArmR).position - body.BoneOf(BodyPart.ArmL).position;
        Vector3 up = body.BoneOf(BodyPart.Head).position - rd.Centre;
        Vector3 front = Vector3.Cross(across, up).normalized;
        if (face) n = -front;
        if (side != 0) n = -side * across.normalized;
        if (!face && part == BodyPart.Head && Vector3.Dot(front, -n) > 0.5f) n = front;   // (a hit meant for the back of the head)
        Vector3 at = bone.position;
        rd.LastHitObject = what != null && what.GetComponentInParent<Obstacle>() != null;
        rd.Hit?.Invoke(v, false, at, bone);
        rd.Struck?.Invoke(what, at, n, v, bone);
    }

    static Vector3 TreeNormal(Vector3 at)
    {
        if (tree == null) return Vector3.up;
        Vector3 d = at - tree.bounds.center; d.y = 0f;
        return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.back;
    }

    static void SetBelowSnow(float m) =>
        typeof(RiderBody).GetField("<RagdollBelowSnow>k__BackingField", NP).SetValue(body, m);

    static void FindSurfaces()
    {
        var rd = (Ragdoll)typeof(RiderBody).GetField("ragdoll", NP).GetValue(body);
        if (rd != null)
        {
            var gb = (Rigidbody)typeof(Ragdoll).GetField("groundBody", NP).GetValue(rd);
            if (gb != null) snow = gb.GetComponent<Collider>();
        }
        foreach (var ob in Object.FindObjectsByType<Obstacle>(FindObjectsSortMode.InstanceID))
        {
            var c = ob.GetComponentInChildren<Collider>();
            if (c == null) continue;
            if (ob.label == "Tree" && tree == null) tree = c;
            else if (ob.label == "Rock" && rock == null) rock = c;
            else if (ob.label == "Lift tower" && steel == null) steel = c;
        }
        if (steel == null)
        {
            // no lift tower streamed in near the start: a stand-in, a steel post with the tower's label
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = "ReceiptTest steel post"; g.transform.position = r.transform.position + new Vector3(30f, 0f, 0f);
            g.AddComponent<Obstacle>().label = "Lift tower";
            steel = g.GetComponent<Collider>();
        }
        if (snow == null || tree == null || rock == null)
            Debug.Log($"RECEIPT surfaces: snow {snow != null} tree {tree != null} rock {rock != null} steel {steel != null}");
    }

    /// <summary>The game's figure is the receipt's: every dollar on screen is a line on it.</summary>
    static void Check()
    {
        float medical = (float)typeof(SummitGame).GetField("medical", NP).GetValue(game);
        if (Mathf.RoundToInt(medical) != game.Bill.RunTotal) { fails++; Say($"   !! medical ${medical:N0} but the receipt's run total is ${game.Bill.RunTotal:N0}"); }
    }

    static void Say(string s) { lines.Add(s); Debug.Log("RECEIPT " + s); }
}
