using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Throws the ragdoll into every kind of solid thing on the mountain (chalet walls, corners and steps, lift towers
/// and stations, cars, lamp posts, signs and fences, rocks and crags, tree trunks, the start gate's tape trees),
/// head-on, glancing, into the gap between two things, and slowly against them on the slope, over a few mountains,
/// and reports for each throw how long until the player can get up (a fall with falls left) and until the run ends
/// (the last fall: the game's own wipeout timer), whether the body jittered on the spot, and how far it sank into
/// what it hit. Every throw must end.
/// Run: Unity -batchmode -nographics -projectPath . -executeMethod RagStuckTest.Run
///   RAG_SEEDS (default 3), RAG_PER (throws per kind and way per mountain, default 3), RAG_KINDS (comma list to limit)
/// </summary>
public static class RagStuckTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    const float MaxT = 20f;           // s: a throw still down after this is stuck
    const float WipeoutTimer = 2.0f;  // the game's last-fall timer (SummitGame: wipeoutTimer = 2.0f), run down while the body is not moving

    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static Hamlets hamlets; static SkiLift lifts; static CourseDressing dress;
    static MethodInfo hamUpd, liftUpd;

    class Result { public string kind, way, where; public float up, end, jitter, pen, penEnd, restSpeed, latePelvis, lateBoard; public bool stuck, frozen, dead; public int unwedged; public float shift, beginShift; public bool atThrow; }
    /// <summary>A thing found in the census: streamed things come and go, so it is found again, by place, when thrown at.</summary>
    struct Spot { public Vector3 at; public string name; }
    static readonly List<Result> results = new List<Result>();
    static System.Action<float> stepHook, rideHook;   // (rideHook: each step of riding away after getting up)   // each step of a throw (Shot draws the frames)
    static int traceLeft = 2;

    /// <summary>
    /// The same throws, then up again: where the rider is put back on the snow after each (Rider.Recover), how far it
    /// is from anything solid, and whether it can ride away (five seconds, hands off: it must get going and never sit
    /// pinned against something). Run: -executeMethod RagStuckTest.GetUp (SUMMIT_PREFS="summit_ride=1" for skis).
    /// </summary>
    public static void GetUp()
    {
        getUpMode = true;
        Rider.SafeGetUp = System.Environment.GetEnvironmentVariable("RAG_SAFEUP") != "0";   // 0: getting up as it was, for the before numbers
        Run();
        Rider.SafeGetUp = true;
    }
    static bool getUpMode;
    static string lastWhy = "";
    static Vector3 lastRest;   // where the body lay when the rider got up
    class Up { public string kind, way, where, near; public float clear, moved, stuck, shift; public bool crashed, never; public int unpinned; }
    static readonly List<Up> ups = new List<Up>();
    public const float Margin = 1.0f;   // m from the rider's middle to anything solid: closer is a bad place to be put

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.Crash += why => lastWhy = why;
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP); liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("RAG_SEEDS"), out int sv) ? sv : 3;
        int per = int.TryParse(System.Environment.GetEnvironmentVariable("RAG_PER"), out int pv) ? pv : 2;
        var only = System.Environment.GetEnvironmentVariable("RAG_KINDS");
        Ragdoll.Unstick = System.Environment.GetEnvironmentVariable("RAG_UNSTICK") != "0";   // 0: the game as it was, for the before numbers
        Debug.Log($"RAGSTUCK unstick {(Ragdoll.Unstick ? "on" : "off")}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (int seed = 0; seed < seeds; seed++)
            {
                NewMountain(seed);
                var targets = Census(seed);
                foreach (var kv in targets)
                {
                    if (!string.IsNullOrEmpty(only) && !only.Contains(kv.Key)) continue;
                    var list = kv.Value;
                    // a spread of them, the same ones every run
                    var rng = new System.Random(seed * 31 + kv.Key.Length);
                    for (int i = 0; i < Mathf.Min(per, list.Count); i++)
                    {
                        var spot = list[rng.Next(list.Count)];
                        float side = rng.Next(2) == 0 ? 1f : -1f;
                        // "contact" ways crash the way the game does: the board's sphere touching it, the body thrown from
                        // there (Rider.OnCollisionEnter); the others are thrown from a few metres back
                        foreach (var (way, yaw, v) in new[] { ("contact head-on", 0f, 18f), ("contact glancing", 35f * side, 24f), ("contact slow", 0f, 8f),
                                                              ("head-on", 0f, 16f), ("slope-side", 80f * side, 4f) })
                        {
                            var col = Find(kv.Key, spot);
                            if (col == null) { Debug.Log($"RAGSTUCK {kv.Key}: {spot.name} at {spot.at} not there again"); continue; }
                            Throw(kv.Key, way, col, yaw, v, seed);
                        }
                    }
                    // wedged: into the gap between this kind and the nearest other solid thing
                    int w = 0;
                    for (int i = 0; i < list.Count && w < per; i++)
                    {
                        var spot = list[(i * 7919) % list.Count];
                        var col = Find(kv.Key, spot);
                        if (col == null || !Gap(col, out var mid)) continue;
                        Wedge(kv.Key, col, mid, seed); w++;
                    }
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("RAGSTUCK exception " + e); }
        Summary();
        Debug.Log($"RAGSTUCK took {sw.Elapsed.TotalSeconds:0} s");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Ragdoll.Unstick = true;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static T F<T>(string name) => (T)typeof(SummitGame).GetField(name, NP).GetValue(game);

    static void NewMountain(int seed)
    {
        TestBoot.NewMountain(game, 7000 + seed * 613, terrain, props);
        r.ResetAt(Slope.Centre(Slope.PadLip), Slope.StartZ);
        dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) { dress.Rebuild(); dress.Prewarm(); }
    }

    /// <summary>Everything that streams round the rider, as its Update would (batchmode calls none).</summary>
    static void Stream(int hamletSteps)
    {
        terrain.Prewarm(); props.Prewarm(); dress?.Prewarm();
        if (hamlets != null) for (int i = 0; i < hamletSteps; i++) hamUpd.Invoke(hamlets, null);
        if (lifts != null) liftUpd.Invoke(lifts, null);
        typeof(Forest).GetMethod("Update", NP).Invoke(props, null);
        Sweep();
        Physics.SyncTransforms();
    }

    /// <summary>What the game streams out it Destroys, which edit mode refuses: put old hamlets, lifts and cycles away for real.</summary>
    static void Sweep()
    {
        var keep = new HashSet<GameObject>();
        if (lifts != null)
        {
            foreach (var lv in (System.Collections.IEnumerable)typeof(SkiLift).GetField("live", NP).GetValue(lifts))
                foreach (var f in lv.GetType().GetFields())
                {
                    var v = f.GetValue(lv);
                    if (v is GameObject g && g != null) keep.Add(g);
                    else if (v is GameObject[] ga) foreach (var x in ga) if (x != null) keep.Add(x);
                }
            foreach (Transform t in Children(lifts.transform)) if (t.name.StartsWith("Lift ") && !keep.Contains(t.gameObject)) Object.DestroyImmediate(t.gameObject);
            foreach (var st in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
                if (st != null && st.name == "LiftStation" && st.transform.IsChildOf(lifts.transform) && !keep.Contains(st.gameObject)) Object.DestroyImmediate(st.gameObject);
        }
        if (hamlets != null)
        {
            keep.Clear();
            foreach (var b in ((System.Collections.IDictionary)typeof(Hamlets).GetField("built", NP).GetValue(hamlets)).Values)
                keep.Add((GameObject)b.GetType().GetField("root").GetValue(b));
            foreach (Transform t in Children(hamlets.transform)) if (t.name.StartsWith("Hamlet ") && !keep.Contains(t.gameObject)) Object.DestroyImmediate(t.gameObject);
        }
        if (dress != null)
        {
            keep.Clear();
            foreach (GameObject g in ((System.Collections.IDictionary)typeof(CourseDressing).GetField("built", NP).GetValue(dress)).Values) keep.Add(g);
            foreach (Transform t in Children(dress.transform)) if (t.name.StartsWith("Course ") && !keep.Contains(t.gameObject)) Object.DestroyImmediate(t.gameObject);
        }
    }

    static List<Transform> Children(Transform t) { var l = new List<Transform>(); foreach (Transform c in t) l.Add(c); return l; }

    static bool Solid(Collider c)
    {
        if (c == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) return false;
        if (c.attachedRigidbody != null && (c.attachedRigidbody == r.Body || !c.attachedRigidbody.isKinematic)) return false;
        if (Ragdoll.Of(c) != null) return false;
        string nm = c.gameObject.name;
        if (nm.StartsWith("RagdollGround") || nm.StartsWith("BoardGround") || nm.StartsWith("LimbGround") || nm.Contains("Slab")) return false;
        return true;
    }

    /// <summary>What kind of thing a collider is, for the report (null: not something to throw the body at).</summary>
    static string KindOf(Collider c)
    {
        if (!Solid(c)) return null;
        var brk = c.GetComponentInParent<Breakable>();
        if (brk != null) return brk.kind == Breakable.Kind.Fence ? "fence" : brk.kind == Breakable.Kind.Sign ? "sign" : null;
        var ob = c.GetComponentInParent<Obstacle>();
        string nm = c.gameObject.name;
        if (ob == null) return nm.StartsWith("COL_") ? "cabin " + nm.Substring(4) : null;
        switch (ob.label)
        {
            case "Tree": return Mathf.Abs(c.bounds.center.z - Slope.PadLip) < 30f ? "tape tree" : "trunk";
            case "Rock": return c.transform.IsChildOf(props.transform) ? "rock" : "crag";   // (the forest's boulders; crags and cliff rocks are the course's)
            case "Lift tower": return "lift tower";
            case "Lift station": return "lift station";
            case "Car!": return "car";
            case "Lamp post!": return "lamp post";
            case "Cabin!": return nm == "COL_car" ? "car" : nm.StartsWith("COL_post") ? "lamp post" : nm.StartsWith("COL_") ? "cabin " + nm.Substring(4) : "cabin";
            default: return ob.label.TrimEnd('!').ToLower();
        }
    }

    /// <summary>Every solid thing within reach of the piste over the first two cycles, by kind.</summary>
    static SortedDictionary<string, List<Spot>> Census(int seed)
    {
        var by = new SortedDictionary<string, List<Spot>>();
        var seen = new HashSet<Collider>();
        float z0 = Slope.PadLip - 40f, z1 = Course.First + 2f * Course.Cycle;
        for (float z = z0; z < z1; z += 15f)
        {
            float cx = Slope.Centre(z);
            r.ResetAt(cx, z);
            Stream(z == z0 ? 120 : 8);
            foreach (var c in Physics.OverlapBox(new Vector3(cx, Slope.Height(cx, z + 7.5f), z + 7.5f), new Vector3(Slope.PisteHalf + 40f, 400f, 7.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!seen.Add(c)) continue;
                string k = KindOf(c);
                if (k == null) continue;
                // only what stands on the snow within a throw's reach of it
                Vector3 bc = c.bounds.center;
                float ground = Slope.DrawnHeight(bc.x, bc.z);
                if (c.bounds.min.y > ground + 1.5f || c.bounds.max.y < ground - 0.5f) continue;
                if (!by.TryGetValue(k, out var l)) by[k] = l = new List<Spot>();
                l.Add(new Spot { at = bc, name = c.name });
            }
        }
        string s = "";
        foreach (var kv in by) s += $"{kv.Key} {kv.Value.Count}, ";
        Debug.Log($"RAGSTUCK seed{seed} census: {s}");
        return by;
    }

    /// <summary>Stream the mountain in round a spot from the census and find its collider again.</summary>
    static Collider Find(string kind, Spot s)
    {
        r.ResetAt(s.at.x, s.at.z - 8f);
        Stream(40);
        foreach (var c in Physics.OverlapSphere(s.at, 0.6f, ~0, QueryTriggerInteraction.Ignore))
            if (c.name == s.name && KindOf(c) == kind && (c.bounds.center - s.at).sqrMagnitude < 0.04f) return c;
        return null;
    }

    /// <summary>Somewhere in the snow next to another solid thing, a body's width or two from this one.</summary>
    static bool Gap(Collider a, out Vector3 mid)
    {
        mid = default;
        Vector3 ac = a.bounds.center;
        foreach (var b in Physics.OverlapSphere(ac, a.bounds.extents.magnitude + 2.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (b == a || KindOf(b) == null) continue;
            Vector3 pa = a.ClosestPoint(b.bounds.center), pb = b.ClosestPoint(pa);
            pa = a.ClosestPoint(pb);
            float d = Vector3.Distance(new Vector3(pa.x, 0f, pa.z), new Vector3(pb.x, 0f, pb.z));
            if (d < 0.35f || d > 1.6f) continue;
            mid = (pa + pb) * 0.5f;
            mid.y = Slope.DrawnHeight(mid.x, mid.z);
            return true;
        }
        return false;
    }

    /// <summary>Throw the body at a collider from uphill, turned by yaw (degrees), at speed v.</summary>
    static void Throw(string kind, string way, Collider col, float yaw, float v, int seed)
    {
        Vector3 bc = col.bounds.center;
        float ground = Slope.DrawnHeight(bc.x, bc.z);
        // the side it is met from: down the hill, turned
        Vector3 d = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 probe = new Vector3(bc.x, ground + 0.7f, bc.z) - d * (col.bounds.extents.magnitude + 4f);
        Physics.SyncTransforms();
        Vector3 hit = col.ClosestPoint(probe);
        if (way.StartsWith("contact"))
        {
            // sweep the board's sphere in along the line until it touches
            Vector3 from = new Vector3(hit.x, 0f, hit.z) - d * 6f;
            from.y = Slope.DrawnHeight(from.x, from.z) + Rider.R + 0.05f;
            Vector3 dd = (new Vector3(hit.x, Slope.DrawnHeight(hit.x, hit.z) + Rider.R + 0.05f, hit.z) - from).normalized;
            float best = -1f;
            foreach (var h in Physics.SphereCastAll(from, Rider.R, dd, 8f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider == col && h.distance > 0f && (best < 0f || h.distance < best)) best = h.distance;
            if (best < 0f) { Debug.Log($"RAGSTUCK {kind} | {way}: the board passes {col.name} by (under or over it)"); return; }
            Vector3 touch = from + dd * (best - 0.01f);
            Throw(kind, way, touch, d, v, seed, col);
            return;
        }
        Throw(kind, way, hit, d, v, seed, col);
    }

    static void Wedge(string kind, Collider col, Vector3 mid, int seed)
    {
        Throw(kind, "wedged", mid + Vector3.up * 0.7f, Vector3.forward, 10f, seed, col);
    }

    static void Throw(string kind, string way, Vector3 at, Vector3 d, float v, int seed, Collider col)
    {
        bool contact = way.StartsWith("contact");
        // start a few metres back along the line, on the snow, clear of anything (or, touching: right there)
        float back = contact ? 0f : Mathf.Clamp(v * 0.35f, 1.5f, 5f);
        Vector3 s = at - d * back;
        float sg = Slope.DrawnHeight(s.x, s.z);
        if (!contact && Physics.CheckSphere(new Vector3(s.x, sg + Rider.R + 0.3f, s.z), 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            // something in the way of the run-up: start closer
            s = at - d * 1.0f; sg = Slope.DrawnHeight(s.x, s.z);
        }
        r.Steer = 0f;
        // the same throw whatever ran before it (so a single kind or seed can be run again on its own)
        int h = 4242 + seed * 7919 + Mathf.RoundToInt(at.x * 10f) * 31 + Mathf.RoundToInt(at.z * 10f) * 17;
        foreach (char ch in kind + way) h = h * 31 + ch;
        Random.InitState(h);
        typeof(SummitGame).GetField("falls", NP).SetValue(game, 0);   // a fall with falls left: get-up is timed too
        r.ResetAt(s.x, s.z); body.ResetVisual(); r.Begin();
        Stream(4);
        Vector3 vel = Vector3.ProjectOnPlane(d, Slope.Normal(s.x, s.z)).normalized * v;
        r.Body.linearVelocity = vel;
        if (contact) { r.Body.position = s; r.transform.position = s; }
        else for (int i = 0; i < 3; i++) { r.Body.linearVelocity = vel; r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); }
        r.Body.linearVelocity = vel;
        body.Apply(Dt);   // (the body takes the board's speed into the throw)
        r.DoCrash("test");

        var res = new Result { kind = kind, way = way, up = -1f, end = -1f };
        int unwedged0 = Ragdoll.Unwedged, unwedgedSeen = Ragdoll.Unwedged;
        var traceSeed = System.Environment.GetEnvironmentVariable("RAG_TRACE_SEED");
        bool trace = traceLeft > 0 && way == System.Environment.GetEnvironmentVariable("RAG_TRACE") && (string.IsNullOrEmpty(traceSeed) || traceSeed == seed.ToString())
                     && (System.Environment.GetEnvironmentVariable("RAG_TRACE_KIND") ?? kind) == kind;
        if (trace) { traceLeft--; Debug.Log($"RAGTRACE --- {kind} | {way} into {col.name} (box centre {col.bounds.center}, size {col.bounds.size}); slab under the pelvis"); }
        var rbs = new List<Rigidbody>();
        var hist = new Queue<Vector3>(); var boardHist = new Queue<Vector3>();
        int window = Mathf.RoundToInt(0.5f / Dt);
        float wipe = WipeoutTimer, jitterRun = 0f, penSum = 0f; int penN = 0; string deepNote = "";
        float t = 0f;
        for (int n = 0; t < MaxT; n++, t += Dt)
        {
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt);
            if (n % 24 == 0) Stream(1);
            stepHook?.Invoke(t);
            // the thrown body's own parts and its board only (lift chairs and the like move too)
            if (rbs.Count == 0) foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (!rb.isKinematic && rb != r.Body && Ragdoll.Of(rb.GetComponent<Collider>()) != null) rbs.Add(rb);
            var rag = body.RagdollForTest;
            if (rag == null || !rag.Active) break;
            Vector3 c = rag.Centre;
            if (float.IsNaN(c.x)) { res.where = "NaN"; break; }
            // the game's two endings: tap to get up (a fall with falls left), and the last fall's timer
            if (res.up < 0f && r.CanGetUp) res.up = t;
            if (rag.Frozen) res.frozen = true;
            if (Ragdoll.Unwedged != unwedgedSeen)
            {
                unwedgedSeen = Ragdoll.Unwedged;
                if (n == 0) { res.beginShift = Mathf.Max(res.beginShift, Ragdoll.LastUnwedge); res.atThrow = true; }   // (as it was thrown: Begin)
                else res.shift = Mathf.Max(res.shift, Ragdoll.LastUnwedge);
            }
            if (t > 3f && (res.up < 0f || res.end < 0f))
            {
                res.latePelvis = Mathf.Max(res.latePelvis, body.RagdollForTest.Velocity.magnitude);
                foreach (var rb in rbs) if (rb != null && rb.name == "Board") res.lateBoard = Mathf.Max(res.lateBoard, rb.linearVelocity.magnitude);
            }
            if (!body.RagdollMoving) wipe -= Dt;
            if (res.end < 0f && wipe <= 0f) res.end = t;
            // jitter: the body reads as moving (fast enough to hold the ending back) while going nowhere
            // (the body and the board each judged by its own progress: a board sliding off down the hill is not jitter)
            Vector3 bc = rag.BoardCentre;
            hist.Enqueue(c); if (hist.Count > window) hist.Dequeue();
            boardHist.Enqueue(bc); if (boardHist.Count > window) boardHist.Dequeue();
            bool bodyNowhere = hist.Count == window && (c - hist.Peek()).magnitude < 0.3f;
            bool boardNowhere = boardHist.Count == window && (bc - boardHist.Peek()).magnitude < 0.3f;
            float limbs = 0f, boardV = 0f;
            foreach (var rb in rbs) if (rb != null) { if (rb.name == "Board") boardV = rb.linearVelocity.magnitude; else limbs = Mathf.Max(limbs, rb.linearVelocity.magnitude); }
            bool twitch = (bodyNowhere && (rag.Velocity.magnitude > 1.2f || limbs > 4f)) || (boardNowhere && boardV > 4f);
            if (t > 1f && twitch) { jitterRun += Dt; res.jitter = Mathf.Max(res.jitter, jitterRun); }
            else jitterRun = 0f;
            // sinking into what it hit (and anything solid near): the deepest any part is inside, over the last two seconds
            if (t > 1f && n % 6 == 0)
            {
                float deepest = 0f;
                foreach (var rb in rbs)
                {
                    if (rb == null) continue;
                    var pc = rb.GetComponent<Collider>();
                    if (pc == null || !pc.enabled) continue;
                    foreach (var o in Physics.OverlapSphere(rb.position, 1f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (KindOf(o) == null) continue;
                        if (Physics.ComputePenetration(pc, pc.transform.position, pc.transform.rotation, o, o.transform.position, o.transform.rotation, out var pdir, out float depth))
                        {
                            if (depth > deepest) deepNote = $"{rb.name} {depth:0.00} m into {o.name} (out {pdir.x:0.0},{pdir.y:0.0},{pdir.z:0.0}) v={rb.linearVelocity.magnitude:0.0} t={t:0.0} sleeping={rb.IsSleeping()}";
                            deepest = Mathf.Max(deepest, depth);
                        }
                    }
                }
                penSum += deepest; penN++; res.penEnd = deepest;
                res.pen = Mathf.Max(res.pen, deepest);
            }
            if (trace && n % 12 == 0)
            {
                string line = $"t={t:0.00} speed={body.RagdollSpeed:0.00}";
                foreach (var rb in rbs)
                {
                    if (rb == null) continue;
                    var pc = rb.GetComponent<Collider>();
                    float dmax = 0f; string what = "";
                    if (pc != null && pc.enabled)
                        foreach (var o in Physics.OverlapSphere(rb.position, 1f, ~0, QueryTriggerInteraction.Ignore))
                            if (KindOf(o) != null && Physics.ComputePenetration(pc, pc.transform.position, pc.transform.rotation, o, o.transform.position, o.transform.rotation, out _, out float dd) && dd > dmax) { dmax = dd; what = o.name; }
                    if (dmax > 0.05f || rb.name.Contains("Hips") || rb.name == "Board") line += $" | {rb.name} p=({rb.position.x:0.00},{rb.position.y:0.00},{rb.position.z:0.00}) v={rb.linearVelocity.magnitude:0.0} pen={dmax:0.00} {what}";
                }
                Debug.Log("RAGTRACE " + line);
            }
            if (res.up >= 0f && res.end >= 0f && t > Mathf.Max(res.up, res.end) + 2f) break;
        }
        res.dead = body.Dead;
        res.stuck = (res.up < 0f && !res.dead) || res.end < 0f;   // (a fatal fall ends the run: there is no getting up from it)
        res.unwedged = Ragdoll.Unwedged - unwedged0;
        res.restSpeed = body.RagdollSpeed;
        Vector3 cc = body.RagdollCentre;
        res.where = $"seed{seed} at ({cc.x:0.0},{cc.z:0.0}) x-c={cc.x - Slope.Centre(cc.z):0.0} {col.name}";
        results.Add(res);
        if (res.penEnd > 0.15f) Debug.Log($"RAGSTUCK-PEN {kind} | {way}: last deepest {deepNote}; dead={body.Dead}");
        Debug.Log($"RAGSTUCK {kind} | {way} v{v:0}: up {Fmt(res.up)} end {Fmt(res.end)} jitter {res.jitter:0.0}s pen {res.pen:0.00}m (end {res.penEnd:0.00}) unwedged {res.unwedged} (at the throw {res.beginShift:0.00} m, later {res.shift:0.00} m) frozen {res.frozen} dead {res.dead} speed-at-stop {res.restSpeed:0.0} m/s late pelvis {res.latePelvis:0.0} board {res.lateBoard:0.0}{(res.stuck ? "  STUCK" : "")}  {res.where}");
        // put the body away before the next throw
        lastRest = body.RagdollCentre;
        r.RequestUp = true; r.StayDown = false; r.HoldCrash = false;
        for (int i = 0; i < 400 && r.Crashed; i++) { r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); r.RequestUp = true; r.HoldCrash = false; }
        if (getUpMode) GetUpCheck(kind, way, seed);
    }

    /// <summary>Just got up: how clear is it, and can it ride away?</summary>
    static void GetUpCheck(string kind, string way, int seed)
    {
        var u = new Up { kind = kind, way = way };
        if (r.Crashed) { u.never = true; ups.Add(u); Debug.Log($"RAGUP {kind} | {way}: never got up"); return; }
        body.Apply(Dt);   // (the thrown body is put away on the frame you get up)
        Physics.SyncTransforms();
        Vector3 p = r.Body.position;
        u.clear = Clearance(p, out u.near);
        u.shift = new Vector2(p.x - lastRest.x, p.z - lastRest.z).magnitude;
        u.where = $"seed{seed} at ({p.x:0.0},{p.z:0.0}) x-c={p.x - Slope.Centre(p.z):0.0}";
        // hands off for five seconds: it must get going (batchmode fires no collision callbacks, so a wall it rides into
        // stops it without a crash: exactly the pinned rider)
        float t = 0f; int n = 0, unpinned0 = Rider.Unpinned;
        for (; t < 5f; t += Dt, n++)
        {
            r.Steer = 0f;
            r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 4 == 0) body.Apply(4 * Dt);
            if (n % 24 == 0) Stream(1);
            rideHook?.Invoke(t);
            if (r.Crashed) { u.crashed = true; break; }
            if (t > 0.8f && r.Speed < 1f) u.stuck += Dt;
        }
        u.unpinned = Rider.Unpinned - unpinned0;
        Vector3 q = r.Body.position;
        u.moved = new Vector2(q.x - p.x, q.z - p.z).magnitude;
        ups.Add(u);
        bool bad = u.clear < Margin || u.stuck > 1f || (u.moved < 6f && !u.crashed);
        Debug.Log($"RAGUP {kind} | {way}: up {u.shift:0.0} m from the body, clear {u.clear:0.00} m of {u.near}, then {u.moved:0.0} m in {t:0.0} s, stuck {u.stuck:0.0} s, eased free {u.unpinned}x{(u.crashed ? " crashed (" + lastWhy + ")" : "")}{(bad ? "  BAD" : "")}  {u.where}");
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 2400 && r.Crashed; i++) { r.Tick(Dt); body.PhysicsStep(); Physics.Simulate(Dt); body.Apply(Dt); r.RequestUp = true; r.HoldCrash = false; } }
    }

    /// <summary>How close the standing rider (snow to head height) is to anything solid, and what that is.</summary>
    static float Clearance(Vector3 p, out string what)
    {
        what = "nothing";
        float g = Slope.DrawnHeight(p.x, p.z), best = 9f;
        foreach (float h in new[] { 0.2f, 0.9f, 1.6f })
        {
            var s = new Vector3(p.x, g + h, p.z);
            foreach (var c in Physics.OverlapSphere(s, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!Solid(c) || c.gameObject.layer == CourseDressing.IceLayer) continue;
                if (c is MeshCollider mc && !mc.convex) continue;
                float d = (c.ClosestPoint(s) - s).magnitude;
                if (d < best) { best = d; what = c.name + (c.transform.parent != null ? " (" + c.transform.parent.name + ")" : ""); }
            }
        }
        return best;
    }

    /// <summary>
    /// The user's screenshot, before and after: the body thrown glancing into a chalet's wall by its landing (the case
    /// that locked inside the wall), with the fix off and then on, seen by the game's own chase camera and from beside
    /// the wall. Needs the GPU (no -nographics). Writes RagStuck{Before,After}{Chase,Side}{n}.png in the project folder.
    /// </summary>
    public static void Shot()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP); liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); var fUpd = typeof(Forest).GetMethod("Update", NP);
        ChaseCamera.TestDt = Dt;
        try
        {
            NewMountain(0);
            var spots = Census(0)["cabin body"];
            // the same pick as Run's first cabin throw, glancing (the one that locked in the wall)
            var rng = new System.Random(0 * 31 + "cabin body".Length);
            var spot = spots[rng.Next(spots.Count)];
            float side = rng.Next(2) == 0 ? 1f : -1f;
            foreach (bool on in new[] { false, true })
            {
                Ragdoll.Unstick = on;
                string tag = on ? "After" : "Before";
                var col = Find("cabin body", spot);
                if (col == null) { Debug.LogError("RAGSHOT the chalet is not there"); break; }
                int shot = 0;
                stepHook = t =>
                {
                    tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
                    if (hamlets != null) hamUpd.Invoke(hamlets, null);
                    if (lifts != null) liftUpd.Invoke(lifts, null);
                    chase.enabled = true; late.Invoke(chase, null);
                    typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
                    float[] at = { 0.2f, 0.5f, 1.2f, 3.5f };
                    if (shot < at.Length && t >= at[shot])
                    {
                        Shoot(cam, $"RagStuck{tag}Chase{shot}.png");
                        if (shot == at.Length - 1)
                        {
                            // from beside the wall, looking along it at the body
                            Vector3 c = body.RagdollCentre, n = col.ClosestPoint(c) - c; n.y = 0f;
                            Vector3 along = Vector3.Cross(Vector3.up, n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.forward);
                            Vector3 from = c - n.normalized * 2.2f + along * 2.6f + Vector3.up * 1.3f;
                            var keepPos = cam.transform.position; var keepRot = cam.transform.rotation; float keepFov = cam.fieldOfView;
                            chase.enabled = false; cam.fieldOfView = 50f;
                            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(c + Vector3.up * 0.2f - from));
                            Shoot(cam, $"RagStuck{tag}Side.png");
                            cam.transform.SetPositionAndRotation(keepPos, keepRot); cam.fieldOfView = keepFov; chase.enabled = true;
                        }
                        shot++;
                    }
                };
                Throw("cabin body", "contact glancing", col, 35f * side, 24f, 0);
                stepHook = null;
            }
        }
        catch (System.Exception e) { Debug.LogError("RAGSHOT exception " + e); }
        stepHook = null;
        Ragdoll.Unstick = true;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>
    /// The user's get-up screenshot, before and after: crashes into hamlet cabins (and the lamp posts among them), up
    /// again with the old look round (Rider.SafeGetUp off) until one leaves the rider pinned against a wall, then the
    /// same throw with the fix. The game's chase camera, three seconds into riding away. Needs the GPU. For skis, as in
    /// the screenshot: SUMMIT_PREFS="summit_ride=1". Writes GetUp{Before,After}{n}.png in the project folder.
    /// </summary>
    public static void GetUpShot()
    {
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        r = F<Rider>("rider"); body = F<RiderBody>("body"); terrain = F<SlopeStreamer>("terrain"); props = F<Forest>("props");
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        r.Crash += why => lastWhy = why;
        hamlets = Object.FindFirstObjectByType<Hamlets>(); lifts = Object.FindFirstObjectByType<SkiLift>();
        hamUpd = typeof(Hamlets).GetMethod("Update", NP); liftUpd = typeof(SkiLift).GetMethod("Update", NP);
        var cam = Camera.main; var chase = cam.GetComponent<ChaseCamera>();
        var late = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        var atmo = Object.FindFirstObjectByType<Atmosphere>();
        var tUpd = typeof(SlopeStreamer).GetMethod("Update", NP); var fUpd = typeof(Forest).GetMethod("Update", NP);
        ChaseCamera.TestDt = Dt;
        getUpMode = true;
        System.Action frame = () =>
        {
            tUpd.Invoke(terrain, null); fUpd.Invoke(props, null);
            if (hamlets != null) hamUpd.Invoke(hamlets, null);
            if (lifts != null) liftUpd.Invoke(lifts, null);
            chase.enabled = true; late.Invoke(chase, null);
            typeof(Atmosphere).GetMethod("LateUpdate", NP).Invoke(atmo, null);
        };
        int found = 0;
        try
        {
            NewMountain(0);
            var census = Census(0);
            foreach (var kind in new[] { "lamp post", "cabin stair", "cabin body", "cabin deck" })
            {
                if (found >= 2 || !census.TryGetValue(kind, out var spots)) continue;
                var rng = new System.Random(0 * 31 + kind.Length);   // (Run's picks)
                for (int i = 0; i < 2 && found < 2; i++)
                {
                    var spot = spots[rng.Next(spots.Count)];
                    float side = rng.Next(2) == 0 ? 1f : -1f;
                    foreach (var (way, yaw, v) in new[] { ("contact glancing", 35f * side, 24f), ("contact head-on", 0f, 18f) })
                    {
                        if (found >= 2) break;
                        string tag = "";
                        bool shot = false;
                        stepHook = t => frame();
                        rideHook = t =>
                        {
                            frame();
                            if (!shot && t >= 3f) { shot = true; Shoot(cam, $"GetUp{tag}{found}.png"); }
                        };
                        Rider.SafeGetUp = false; tag = "Before";
                        var col = Find(kind, spot);
                        if (col == null) continue;
                        Throw(kind, way, col, yaw, v, 0);
                        var before = ups[ups.Count - 1];
                        if (before.stuck < 1f) continue;   // not one that pins: try the next
                        Rider.SafeGetUp = true; tag = "After"; shot = false;
                        col = Find(kind, spot);
                        Throw(kind, way, col, yaw, v, 0);
                        var after = ups[ups.Count - 1];
                        Debug.Log($"RAGSHOT get-up {found}: {kind} {way}: before stuck {before.stuck:0.0} s, moved {before.moved:0.0} m; after stuck {after.stuck:0.0} s, moved {after.moved:0.0} m, eased free {after.unpinned}x");
                        found++;
                    }
                }
            }
        }
        catch (System.Exception e) { Debug.LogError("RAGSHOT exception " + e); }
        stepHook = null; rideHook = null; getUpMode = false;
        Rider.SafeGetUp = true;
        ChaseCamera.TestDt = 0f;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    static void Shoot(Camera cam, string file)
    {
        DynamicGI.UpdateEnvironment();
        // batchmode renders do not re-skin meshes: bake them to see the real pose
        var baked = new List<GameObject>(); var hidden = new List<SkinnedMeshRenderer>();
        foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; hidden.Add(sk); baked.Add(g);
        }
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render(); cam.Render();   // (the first frame after a jump is not right)
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        foreach (var g in baked) Object.DestroyImmediate(g);
        foreach (var sk in hidden) sk.enabled = true;
        Debug.Log("RAGSHOT wrote " + file);
    }

    static string Fmt(float t) => t < 0f ? "NEVER" : t.ToString("0.0") + "s";

    static void Summary()
    {
        if (getUpMode)
        {
            var upKinds = new SortedDictionary<string, List<Up>>();
            foreach (var x in ups) { if (!upKinds.TryGetValue(x.kind, out var l)) upKinds[x.kind] = l = new List<Up>(); l.Add(x); }
            int close = 0, pinned = 0, never = 0, crashed = 0, eased = 0;
            foreach (var x in ups) if (x.unpinned > 0) eased++;
            Debug.Log($"RAGUP-SUM kind | get-ups | put within {Margin} m of something | could not ride away | crashed riding away | never got up | closest");
            foreach (var kv in upKinds)
            {
                int c = 0, pn = 0, nv = 0, cr = 0; float closest = 9f;
                foreach (var x in kv.Value)
                {
                    if (x.never) { nv++; continue; }
                    if (x.clear < Margin) c++;
                    if (x.stuck > 1f || (x.moved < 6f && !x.crashed)) pn++;
                    if (x.crashed) cr++;
                    closest = Mathf.Min(closest, x.clear);
                }
                close += c; pinned += pn; never += nv; crashed += cr;
                Debug.Log($"RAGUP-SUM {kv.Key} | {kv.Value.Count} | {c} | {pn} | {cr} | {nv} | {closest:0.00} m");
            }
            var shifts = new List<float>();
            foreach (var x in ups) if (!x.never) shifts.Add(x.shift);
            shifts.Sort();
            if (shifts.Count > 0) Debug.Log($"RAGUP got up from the body: median {shifts[shifts.Count / 2]:0.0} m, 90% {shifts[(int)(shifts.Count * 0.9f)]:0.0} m, most {shifts[shifts.Count - 1]:0.0} m");
            Debug.Log($"RAGUP done: {ups.Count} get-ups, {close} put within {Margin} m of something, {pinned} could not ride away, {crashed} crashed riding away, {never} never got up, {eased} eased free of something");
        }
        var kinds = new SortedDictionary<string, List<Result>>();
        foreach (var x in results) { if (!kinds.TryGetValue(x.kind, out var l)) kinds[x.kind] = l = new List<Result>(); l.Add(x); }
        int stuck = 0, jit = 0, sunk = 0, froze = 0, unw = 0;
        Debug.Log("RAGSTUCK-SUM kind | throws | stuck | jittered>1s | left inside>0.15m | lifted clear | laid still | get-up mean/max | end mean/max");
        foreach (var kv in kinds)
        {
            int s = 0, j = 0, k = 0, fz = 0, uw = 0; float um = 0f, ux = 0f, em = 0f, ex = 0f; int un = 0, en = 0;
            foreach (var x in kv.Value)
            {
                if (x.stuck) s++;
                if (x.jitter > 1f) j++;
                if (x.penEnd > 0.15f) k++;
                if (x.frozen) fz++;
                if (x.unwedged > 0) uw++;
                if (x.up >= 0f) { um += x.up; ux = Mathf.Max(ux, x.up); un++; }
                if (x.end >= 0f) { em += x.end; ex = Mathf.Max(ex, x.end); en++; }
            }
            stuck += s; jit += j; sunk += k; froze += fz; unw += uw;
            Debug.Log($"RAGSTUCK-SUM {kv.Key} | {kv.Value.Count} | {s} | {j} | {k} | {uw} | {fz} | {(un > 0 ? um / un : 0f):0.0}/{ux:0.0}s | {(en > 0 ? em / en : 0f):0.0}/{ex:0.0}s");
        }
        foreach (bool begin in new[] { true, false })
        {
            var shifts = new List<float>();
            foreach (var x in results) if (begin ? x.atThrow : x.shift > 0f) shifts.Add(begin ? x.beginShift : x.shift);
            shifts.Sort();
            if (shifts.Count > 0) Debug.Log($"RAGSTUCK lifted clear {(begin ? "as thrown" : "later, pinned")}: {shifts.Count} throws, by median {shifts[shifts.Count / 2]:0.00} m, 90% {shifts[(int)(shifts.Count * 0.9f)]:0.00} m, most {shifts[shifts.Count - 1]:0.00} m");
        }
        Debug.Log($"RAGSTUCK done: {results.Count} throws, {stuck} stuck, {jit} jittered, {sunk} left inside something, {unw} lifted clear, {froze} laid still");
    }
}
