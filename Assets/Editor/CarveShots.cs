using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// Frame strips of the steering (slide) from the chase camera (needs the GPU: never with -nographics):
///   Unity -batchmode -projectPath . -executeMethod CarveShots.Run -logFile shots.log     (CARVE_OUT=dir, default carve-renders)
///  - Carve: side to side on open piste at 79 km/h, 60% of the drag each way;
///  - Skid: the whole drag at 100 km/h, flipped the other way after half a second, then let go (the ease back);
///  - Slalom: the racer from SteerTest through gates of stand-in trees (3 m gaps), 5 m side to side every 9 m, at 79 km/h;
///  - Band: the bot riding a cliff band's traverse from 79 km/h.
/// Each strip is 12 frames, left to right and top to bottom; the log has what the board was doing at each (CARVE-FRAME).
/// One mountain (seed 1000): in a batch run the editor never ends a frame, and a second mountain's props pile on the first's.
/// </summary>
public static class CarveShots
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 120f;
    const int W = 236, H = 512, Frames = 12;
    static SummitGame game; static Rider r; static RiderBody body; static SlopeStreamer terrain; static Forest props;
    static ChaseCamera chase; static MethodInfo camStep; static Camera cam;
    static string outDir;

    public static void Run()
    {
        game = TestHarness.Boot();
        Physics.simulationMode = SimulationMode.Script;
        r = game.rider;
        body = game.body;
        terrain = game.terrain;
        props = game.props;
        chase = Object.FindFirstObjectByType<ChaseCamera>();
        camStep = typeof(ChaseCamera).GetMethod("LateUpdate", NP);
        outDir = System.Environment.GetEnvironmentVariable("CARVE_OUT") ?? "carve-renders";
        System.IO.Directory.CreateDirectory(outDir);
        SummitImport.EnsureSnowTextures(); SummitImport.EnsureSky(); SummitImport.EnsureBackdrop();
        TestBoot.NewMountain(game, 1000, terrain, props);
        var dress = Object.FindFirstObjectByType<CourseDressing>();
        if (dress != null) { var tgt = dress.target; dress.target = null; dress.Rebuild(); dress.target = tgt; dress.Prewarm(); }
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        r.input = null;
        cam = Camera.main;
        chase.enabled = false;   // (stepped by hand, on the test's own clock)
        try
        {
            float z = OpenStretch(Course.First + 60f, 150f);
            // 1. side to side: 60% of the drag each way, 0.75 s a side
            Strip("Carve", Slope.Centre(z), z, 22f, 22f, 0.5f, 0.15f, (t, n) => t < 0.5f ? 0f : 0.6f * (Mathf.Sin((t - 0.5f) * Mathf.PI * 2f / 1.5f) > 0f ? 1f : -1f));
            // 2. the whole drag one way at 100 km/h, flipped after half a second, then let go
            Strip("Skid", Slope.Centre(z), z, 28f, 0f, 0.5f, 0.1f, (t, n) => t < 0.6f ? 0f : t < 1.1f ? 1f : t < 1.5f ? -1f : 0f);
            // 3. the tree slalom: gates 3 m wide, 5 m side to side every 9 m
            {
                const float spacing = 9f, swing = 2.5f, v = 22f; const int gates = 8;
                float g0 = z + 34f, amp = swing - 1.1f;
                var made = Gates(z, g0, spacing, swing, gates);
                try
                {
                    Strip("Slalom", Slope.Centre(z) - amp, z, v, v, 1.2f, 0.16f, (t, n) =>
                    {
                        Vector3 p = r.Body.position, vel = r.Body.linearVelocity;
                        float sp = Mathf.Max(5f, r.Speed), om = Mathf.PI * sp / spacing, zl = p.z + sp * 0.06f;
                        float baseX = Slope.Centre(p.z) , baseV = (Slope.Centre(p.z + 1f) - baseX) * vel.z;
                        float xd = p.z < g0 - spacing ? -amp : -amp * Mathf.Cos(Mathf.PI * (p.z - g0) / spacing);
                        float vd = zl < g0 - spacing ? 0f : amp * om * Mathf.Sin(Mathf.PI * (zl - g0) / spacing);
                        return CarveBot.ForSide(r, baseV + vd + 10f * (xd - (p.x - baseX)));
                    });
                }
                finally { foreach (var g in made) Object.DestroyImmediate(g); }
            }
            // 4. a cliff band's traverse, carved: the bot's edge into the hill
            for (int c = 0; c < 8; c++)
            {
                if (!CliffBand.Of(c, out var b) || b.ledge) continue;
                float z0 = b.z0 - 60f;
                Strip("Band", Slope.Centre(z0), z0, 22f, 0f, 1.6f, 0.5f, (t, n) =>   // (from the way in: the turn onto the bench, then along it)
                {
                    float zA = r.Z + 12f * Slope.Squash(r.Z), want = Slope.Centre(zA), lead = Mathf.Atan2(want - r.X, zA - r.Z);
                    return Mathf.Clamp((lead - r.TravelHeading) * 1.5f + (want - r.X) * 0.02f, -1f, 1f);
                }, $"band cycle {c}, {(b.side > 0 ? "the hill on the right" : "the hill on the left")}");
                break;
            }
        }
        catch (System.Exception e) { Debug.LogError("CARVE-SHOTS exception " + e); }
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        TestHarness.Exit(0);
    }

    /// <summary>Open cruising piste with `len` m clear down the middle: no crevasse, nothing standing within 12 m, no air.</summary>
    static float OpenStretch(float z, float len)
    {
        for (; z < Course.First + 3f * Course.Cycle; z += 10f)
        {
            bool bad = false;
            for (float zz = z - 5f; zz < z + len && !bad; zz += 2f)
            {
                float x = Slope.Centre(zz);
                bad = Course.At(zz) != Course.Section.Cruise || !Course.Solid(x, zz, 10f) || Course.CrevasseNear(x, zz, 8f) || Slope.Normal(x, zz).y < 0.7f;
            }
            if (bad) continue;
            r.ResetAt(Slope.Centre(z), z); terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
            float zm = z + len * 0.5f;
            foreach (var c in Physics.OverlapBox(new Vector3(Slope.Centre(zm), Slope.Height(Slope.Centre(zm), zm), zm), new Vector3(14f, 60f, len * 0.5f + 5f)))
                if (c.GetComponentInParent<Obstacle>() != null) { bad = true; break; }
            if (bad) continue;
            // (and a straight run of it stays on the snow)
            r.ResetAt(Slope.Centre(z), z); r.Begin(); r.Body.linearVelocity = Vector3.forward * 22f;
            for (int i = 0; i < 120 * 5 && r.Z < z + len && !bad; i++) { r.Steer = 0f; r.Tick(Dt); Physics.Simulate(Dt); if (r.Airborne || r.Crashed || r.Grinding) bad = true; }
            if (!bad) return z;
        }
        return Course.First + 120f;
    }

    /// <summary>Stand-in trees (a trunk and a dark crown, nothing to hit) as gates 3 m wide down the run from g0.</summary>
    static List<GameObject> Gates(float z, float g0, float spacing, float swing, int gates)
    {
        var made = new List<GameObject>();
        var bark = new Material(Mats.LitShader); bark.color = new Color(0.25f, 0.17f, 0.11f);
        var green = new Material(Mats.LitShader); green.color = new Color(0.07f, 0.22f, 0.12f);
        for (int k = 0; k < gates; k++)
        {
            float gz = g0 + k * spacing, gx = Slope.Centre(gz) - swing * Mathf.Cos(Mathf.PI * k);
            foreach (float s in new[] { -1.5f, 1.5f })
            {
                float x = gx + s * 1.25f;   // (the trunks stand a touch outside the 3 m gap)
                var root = new GameObject("CarveGate"); root.transform.position = new Vector3(x, Slope.DrawnHeight(x, gz), gz);
                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(trunk.GetComponent<Collider>());
                trunk.transform.SetParent(root.transform, false); trunk.transform.localPosition = Vector3.up * 0.8f; trunk.transform.localScale = new Vector3(0.2f, 0.8f, 0.2f);
                trunk.GetComponent<MeshRenderer>().sharedMaterial = bark;
                for (int j = 0; j < 3; j++)
                {
                    var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(crown.GetComponent<Collider>());
                    crown.transform.SetParent(root.transform, false); crown.transform.localPosition = Vector3.up * (1.4f + 0.8f * j);
                    crown.transform.localScale = new Vector3(1.2f - 0.35f * j, 1.2f, 1.2f - 0.35f * j);
                    crown.GetComponent<MeshRenderer>().sharedMaterial = green;
                }
                made.Add(root);
            }
        }
        return made;
    }

    /// <summary>
    /// One ride from (x, z) at v0 (its speed held at `hold` m/s if that is not 0), the finger from `finger(t, step)`;
    /// 12 frames of the chase camera from `first` s on, `every` s apart, into Carve_{name}.png.
    /// </summary>
    static void Strip(string name, float x, float z, float v0, float hold, float first, float every, System.Func<float, int, float> finger, string note = null)
    {
        r.ResetAt(x, z); body.ResetVisual(); r.Begin();
        r.Heading = 0f; r.Steer = 0f;
        r.Body.linearVelocity = Vector3.ProjectOnPlane(Vector3.forward, Slope.Normal(x, z)).normalized * v0;
        terrain.Prewarm(); props.Prewarm(); Physics.SyncTransforms();
        chase.rider = r; chase.Snap();
        body.Apply(Dt); cam.Render();   // (a first frame nobody keeps: the outfit and the snow's map are set by it)
        var strip = new Texture2D(W * 6, H * 2, TextureFormat.RGB24, false);
        var rt = new RenderTexture(W, H, 24);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        int taken = 0, n = 0; float steer = 0f, skidMost = 0f; int skids = 0; bool wasSliding = false;
        for (float t = 0f; taken < Frames && t < 40f && !r.Crashed; t += Dt, n++)
        {
            steer = Mathf.Lerp(steer, Mathf.Clamp(finger(t, n), -1f, 1f), 1f - Mathf.Exp(-Dt * 60f));
            r.Steer = steer;
            r.TestContacts(Dt);
            r.Tick(Dt);
            if (hold > 0f && !r.Airborne && !r.Crashed) { var vel = r.Body.linearVelocity; float h = vel.magnitude; if (h > 0.5f) r.Body.linearVelocity = vel * (hold / h); }
            body.PhysicsStep(); Physics.Simulate(Dt);
            if (n % 24 == 0) { terrain.Prewarm(); props.Prewarm(); }
            if (r.Sliding && !wasSliding) skids++; wasSliding = r.Sliding; skidMost = Mathf.Max(skidMost, r.Skid);
            if (n % 2 != 1) continue;
            body.Apply(2 * Dt);
            ChaseCamera.TestDt = 2 * Dt; camStep.Invoke(chase, null);
            Surfaces.Step(r.Body.position, r.RunNow, 2 * Dt, true);
            if (t + 1e-4f < first + taken * every) continue;
            cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            strip.SetPixels((taken % 6) * W, (1 - taken / 6) * H, W, H, tex.GetPixels());
            Debug.Log($"CARVE-FRAME {name} {taken + 1}/{Frames} t={t:0.00}: {r.Speed * 3.6f:0} km/h, finger {r.Steer:+0.00;-0.00}, edge {r.Lean * Mathf.Rad2Deg:+0;-0} deg, board {r.Heading * Mathf.Rad2Deg:+0;-0} deg, going {r.TravelHeading * Mathf.Rad2Deg:+0;-0} deg (slip {r.Slip * Mathf.Rad2Deg:+0;-0}), {(r.Airborne ? "in the air" : r.Sliding ? "THROWN ACROSS" : "sliding")}, sideways {r.Side:+0.0;-0.0} m/s, fall line {r.FallLine * Mathf.Rad2Deg:+0;-0} deg, skid {r.Skid:0.00}, push {r.SideEdge:+0;-0} gravity {r.SideSlope:+0.0;-0.0} m/s^2 across the path, x-centre {r.X - Slope.Centre(r.Z):+0.0;-0.0}");
            taken++;
        }
        cam.targetTexture = null; RenderTexture.active = null; ChaseCamera.TestDt = 0f;
        strip.Apply();
        string file = $"{outDir}/Carve_{name}.png";
        System.IO.File.WriteAllBytes(file, strip.EncodeToPNG());
        Debug.Log($"CARVE-STRIP {file}: {taken} frames{(note != null ? " (" + note + ")" : "")}; thrown across {skids} time(s), skid {skidMost:0.00} at most{(r.Crashed ? "; CRASHED" : "")}");
        Object.DestroyImmediate(strip); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        if (r.Crashed) { r.RequestUp = true; for (int i = 0; i < 400 && r.Crashed; i++) { TestHarness.Step(r, body, Dt); body.Apply(Dt); if (r.CanGetUp) r.RequestUp = true; } }
    }
}
