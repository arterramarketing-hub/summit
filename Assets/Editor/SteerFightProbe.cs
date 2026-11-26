using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// steerfight: what resists or lags the steering on the snow, measured (a probe, nothing passes or fails). On flat,
/// steep and side-hill ground found on the mountain (seed 1234, the run's own snow), at held speeds, the finger thrown
/// from riding straight to a whole drag and to half the thumb's throw, each way, held 1.5 s, then let go; and from a
/// whole drag one way flipped to the other. Each against the same ride with the finger off (so what the hill does on
/// its own is taken out). Per trial: how soon the way the board goes (the travel heading) has turned 1 and 3 degrees,
/// how far by 0.25 / 0.5 / 1 s, the fastest it turns, where it settles against the fall line and against what the
/// drag asks for (Rider.SideFull), the drawn board's heading, and the chase camera's yaw (its own formula: 0.7 of
/// the travel, SmoothDamp ChaseCamera.YawEase).
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod SteerFightProbe.Run -logFile fight.log
/// </summary>
public static class SteerFightProbe
{
    const float Dt = 1f / 120f;
    static Rider r;

    struct Site { public string kind; public float x, z, pitch, fall; }

    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        Physics.simulationMode = SimulationMode.Script;
        TestPrefs.Pin();
        Slope.Init(1234);
        r = new GameObject("Rider").AddComponent<Rider>();
        try
        {
            Debug.Log($"FIGHT chain: RiderInput low-pass 60/s, its dead zone 0.025 + Curve (half the throw {RiderInput.Curve(0.5f):0.00}); Rider.Shape dead zone {Rider.DeadZone:0.00}, expo {Rider.InputExpo:0.0} " +
                      $"(half the thumb's throw asks for {Rider.Shape(RiderInput.Curve(0.5f)):0.00} of a whole drag, a quarter {Rider.Shape(RiderInput.Curve(0.25f)):0.00}); ramp {Rider.RampTime:0.00} s, settle {Rider.SettleTime:0.00} s, edge least {Rider.EdgeLeast:0} m/s^2; " +
                      $"a whole drag {Rider.SideShare:0.00} of the speed ({Rider.SideLeast:0}..{Rider.SideCap:0} m/s), never past {Rider.SideAngle * Mathf.Rad2Deg:0} deg off the fall line; fall line lag {Rider.FallLineLag:0.00} s; " +
                      $"drawn nose {Rider.YawShare:0.0} of the line, capped {Rider.YawCap * Mathf.Rad2Deg:0} deg, {Rider.YawQuick:0}/s; camera {ChaseCamera.TravelShare:0.00} of the travel, ease {ChaseCamera.YawEase:0.00} s; tuck asks 0.7");
            var sites = FindSites();
            foreach (var s in sites)
            {
                Debug.Log($"FIGHT site {s.kind}: x-c={s.x - Slope.Centre(s.z):0} z={s.z:0}, pitch {s.pitch:0} deg, the snow falls {s.fall:+0;-0} deg off the run's line (the hill's fall line at the board {Rider_FallAt(s) * Mathf.Rad2Deg:+0;-0} deg)");
                foreach (float v in s.kind == "flat" ? new[] { 10f, 18f } : new[] { 12f, 22f, 30f })
                {
                    foreach (float drag in new[] { 1f, -1f, RiderInput.Curve(0.5f), -RiderInput.Curve(0.5f) })
                        Trial(s, v, drag, false);
                    Trial(s, v, 1f, true);
                    Trial(s, v, -1f, true);
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("FIGHT exception " + e); }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static float Rider_FallAt(Site s)
    {
        var m = typeof(Rider).GetMethod("FallAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (float)m.Invoke(null, new object[] { s.x, s.z, 0f });
    }

    static (float pitch, float fall) Ground(float x, float z)
    {
        Vector3 n = Vector3.zero;
        for (int i = -1; i <= 1; i++) for (int j = 0; j <= 3; j++) n += Slope.Normal(x + i * 3f, z + j * 8f);
        n.Normalize();
        Vector3 down = Vector3.ProjectOnPlane(Vector3.down, n);
        return (Mathf.Acos(n.y) * Mathf.Rad2Deg, Mathf.Atan2(down.x, down.z) * Mathf.Rad2Deg);
    }

    /// <summary>The first clean stretch of each kind down the mountain: on the snow for the whole of every trial.</summary>
    static List<Site> FindSites()
    {
        var got = new List<Site>();
        var tried = new Dictionary<string, int> { { "flat", 0 }, { "steep", 0 }, { "sidehill", 0 } };
        foreach (string kind in new[] { "flat", "steep", "sidehill" })
        {
            bool done = false;
            for (float z = 250f; z < Slope.FinishZ - 300f && !done; z += 10f)
            {
                if (Mathf.Abs(Slope.RunHeading(z)) > 0.02f) continue;
                for (float off = -24f; off <= 24f && !done; off += 4f)
                {
                    float x = Slope.Centre(z) + off;
                    if (Mathf.Abs(off) > Slope.Half(z) - 6f) continue;
                    var (pitch, fall) = Ground(x, z);
                    bool fits = kind == "flat" ? pitch > 2f && pitch < 12f && Mathf.Abs(fall) < 15f
                              : kind == "steep" ? pitch > 22f && Mathf.Abs(fall) < 12f
                              : pitch > 8f && Mathf.Abs(fall) > 15f && Mathf.Abs(fall) < 75f;
                    if (!fits) continue;
                    var s = new Site { kind = kind, x = x, z = z, pitch = pitch, fall = fall };
                    if (Clean(s)) { got.Add(s); done = true; } else tried[kind]++;
                }
            }
            if (!done) Debug.Log($"FIGHT no clean {kind} stretch found ({tried[kind]} of that kind left the snow)");
        }
        return got;
    }

    static bool Clean(Site s)
    {
        foreach (float v in s.kind == "flat" ? new[] { 10f, 18f } : new[] { 12f, 22f })
            foreach (float d in new[] { 1f, -1f, 0f })
            {
                Place(s, v);
                for (int i = 0; i < 120 * 3; i++)
                {
                    r.Steer = i < 180 ? d : -d; Step(v);
                    if (r.Airborne || r.Crashed || r.Grinding) return false;
                }
            }
        return true;
    }

    static void Place(Site s, float v)
    {
        r.ResetAt(s.x, s.z);
        r.Begin();
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(s.x, s.z)).normalized * v;
        for (int i = 0; i < 36; i++) { r.Steer = 0f; Step(v); }   // (0.3 s hands off: onto the snow, going down the run's line)
    }

    static void Step(float v)
    {
        r.Tick(Dt);
        if (v > 0f && !r.Airborne && !r.Crashed)
        {
            var vel = r.Body.linearVelocity; float h = vel.magnitude;
            if (h > 0.5f) r.Body.linearVelocity = vel * (v / h);
        }
        Physics.Simulate(Dt);
    }

    const int N = 120 * 5 / 2;   // 2.5 s: 1.5 held, 1 let go (or 1 one way, 1.5 the other)
    static float Travel() { var u = r.Velocity; return Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg; }

    /// <summary>One ride: the finger thrown to `drag` (in the input's units: RiderInput.Steer) at t=0 and held 1.5 s then let
    /// go; or (flip) held at -drag for 1 s first, then thrown to drag. Logged against the same ride hands off.</summary>
    static void Trial(Site s, float v, float drag, bool flip)
    {
        float[] base_ = new float[N], line = new float[N], nose = new float[N], fall = new float[N], cam = new float[N], camB = new float[N], side = new float[N];
        float full = 0f;
        for (int pass = 0; pass < 2; pass++)
        {
            Place(s, v);
            float steer = 0f, cy = 0f, cyv = 0f, t0 = flip ? 1f : 0f;
            int pre = flip ? 120 : 0;
            // (flip: a second held the other way first)
            for (int i = 0; i < pre; i++) { steer = Mathf.Lerp(steer, pass == 0 ? 0f : -drag, 1f - Mathf.Exp(-Dt * 60f)); r.Steer = steer; Step(v); }
            cy = Travel() * Mathf.Deg2Rad * ChaseCamera.TravelShare;
            for (int i = 0; i < N; i++)
            {
                float to = pass == 0 ? 0f : (flip ? drag : (i < 180 ? drag : 0f));
                steer = Mathf.Lerp(steer, to, 1f - Mathf.Exp(-Dt * 60f));   // (RiderInput's own low-pass)
                r.Steer = steer; Step(v);
                float tr = Travel();
                cy = Mathf.SmoothDamp(cy, Mathf.Clamp(tr * Mathf.Deg2Rad, -1.4f, 1.4f) * ChaseCamera.TravelShare, ref cyv, ChaseCamera.YawEase, Mathf.Infinity, Dt);
                if (pass == 0) { base_[i] = tr; camB[i] = cy * Mathf.Rad2Deg; }
                else { line[i] = tr; nose[i] = r.Heading * Mathf.Rad2Deg; fall[i] = r.FallLine * Mathf.Rad2Deg; cam[i] = cy * Mathf.Rad2Deg; side[i] = r.Side; }
                if (pass == 1 && i == 100) full = r.SideFull(r.Speed);
                if (r.Airborne || r.Crashed) { Debug.Log($"FIGHT {s.kind} v{v:0} drag {drag:+0.00;-0.00}{(flip ? " flip" : "")}: left the snow at {i * Dt:0.00} s (skipped)"); return; }
            }
        }
        // the response: this ride's travel heading over the hands-off one's, the drag's way
        float sg = Mathf.Sign(drag);
        float Resp(int i) => (line[i] - base_[i]) * sg;
        float CamResp(int i) => (cam[i] - camB[i]) * sg;
        float at(float deg, System.Func<int, float> f, int from = 0, int to = 180) { for (int i = from; i < to; i++) if (f(i) >= deg) return (i + 1) * Dt; return -1f; }
        float peakRate = 0f; for (int i = 1; i < 180; i++) peakRate = Mathf.Max(peakRate, (Resp(i) - Resp(i - 1)) / Dt);
        int last = 179;
        float asked = Rider.Shape(drag);
        float want = Mathf.Asin(Mathf.Clamp(Mathf.Abs(asked) * full / v, 0f, 1f)) * Mathf.Rad2Deg;   // (what the drag asks for, off the fall line)
        float offFall = (line[last] - fall[last]) * sg, noseOff = (nose[last] - fall[last]) * sg, baseOff = base_[last];
        var sb = new StringBuilder();
        sb.Append($"FIGHT {s.kind} pitch {s.pitch:0} fall {s.fall:+0;-0} v{v:0} drag {(drag > 0 ? "right" : "left")} {Mathf.Abs(drag):0.00}{(flip ? " FLIPPED from the other way" : "")} (asks {Mathf.Abs(asked):0.00} of a whole drag, {want:0} deg off the fall line): ");
        if (flip)
        {
            // from a whole drag held the other way: the travel heading's swing back (from where it was at the flip)
            float w0 = line[0] * sg; float back(int i) => line[i] * sg - w0;
            sb.Append($"turning back 1 deg in {T(at(1f, back))}, 5 deg {T(at(5f, back))}, 15 deg {T(at(15f, back))}; through the hands-off line in {T(at(0f, i => Resp(i), 0, N))}; ");
            sb.Append($"after 1 s {(line[119] - line[0]) * sg:0} deg swung, settled {(line[N - 1] - fall[N - 1]) * sg:0} deg off the fall line");
        }
        else
        {
            sb.Append($"the travel 1 deg in {T(at(1f, Resp))}, 3 deg {T(at(3f, Resp))}; {Resp(29):0.0} / {Resp(59):0.0} / {Resp(119):0.0} / {Resp(last):0.0} deg at 0.25 / 0.5 / 1 / 1.5 s, fastest {peakRate:0} deg/s; ");
            sb.Append($"at 1.5 s going {offFall:0} deg off the fall line (asked {want:0}; hands off it went {baseOff:+0;-0} deg off the run's line), sideways {side[last] * sg:0.0} m/s of {Mathf.Abs(asked) * full:0.0} asked; ");
            sb.Append($"the drawn board {noseOff:0} deg off the fall line ({(nose[last] - line[last]) * sg:+0;-0} vs the travel); camera 3 deg in {T(at(3f, CamResp))}, {CamResp(59):0} / {CamResp(last):0} deg at 0.5 / 1.5 s; ");
            float rel = Resp(last + 120);   // (1 s after letting go)
            sb.Append($"let go: 1 s on still {rel:0} deg the drag's way of the hands-off ride");
        }
        Debug.Log(sb.ToString());
    }

    static string T(float t) => t < 0f ? "never" : $"{t:0.00} s";
}
