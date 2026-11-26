using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

/// <summary>
/// (ridercrash) Running into the mountain's other riders, as the game plays it (the rider's own step and its look ahead
/// for what it meets, the mountain's riders' own step): the first hit of a run knocks the other rider over (thrown,
/// limp: a ragdoll) and the hero stays up, slowed and staggered, with the warning (the red pulse, the marker); the
/// second hit in the same run brings the hero down too; a new run gives the free hit back; someone who has only just
/// started riding can't be run into; and the knocked rider gets up again (or goes, unseen). Logs RIDERCRASH lines;
/// exits 1 on any failure. Batch: -executeMethod RiderCrashTest.Run; with SUMMIT_RIDERCRASH_OUT (and graphics) it
/// also writes frames of the first hit there.
/// </summary>
public static class RiderCrashTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static int fails, checks;
    static SummitGame game; static Summit.Rider rider; static RiderBody body; static MountainRiders mr; static Hud hud; static Camera cam;
    static SlopeStreamer terrain; static Forest props;
    static string shots;
    static float speedBefore, speedAfter, closest;

    static void Check(string what, bool ok, string got = "")
    {
        checks++; if (!ok) fails++;
        Debug.Log($"RIDERCRASH {(ok ? "PASS" : "FAIL")} {what} {got}");
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        Physics.simulationMode = SimulationMode.Script;
        T Get<T>(string f) => (T)typeof(SummitGame).GetField(f, NP).GetValue(game);
        rider = Get<Summit.Rider>("rider"); body = Get<RiderBody>("body"); hud = Get<Hud>("hud");
        terrain = Get<SlopeStreamer>("terrain"); props = Get<Forest>("props");
        mr = MountainRiders.Current; cam = Camera.main;
        shots = System.Environment.GetEnvironmentVariable("SUMMIT_RIDERCRASH_OUT");
        if (!string.IsNullOrEmpty(shots)) Directory.CreateDirectory(shots);
        RiderInput.TestDrive = true; RiderInput.TestFingers = 0;
        MountainRiders.TestWant = 0;
        try
        {
            if (mr == null) throw new System.Exception("no MountainRiders");
            NewRun();
            // someone who has only just started riding can't be run into (nobody appears into you)
            int k0 = MountainRiders.Knocked;
            var fresh = Ahead(1.2f, true, true);
            Ride(1.0f, out _);
            Check("someone just started riding isn't hit", MountainRiders.Knocked == k0 && !rider.Crashed && !rider.FreeHitUsed, $"knocked {MountainRiders.Knocked - k0}");
            // the first hit: they go down, you stay up, warned
            NewRun();
            var first = Ahead(14f, false);
            bool hit = Ride(4f, out float atHit, first, 0);
            float before = speedBefore, after = speedAfter;
            Check("the first hit happens", hit, $"knocked {MountainRiders.Knocked}");
            Check("the other rider is thrown (a ragdoll)", first.down != null && first.down.Active, $"down={(first.down != null)}");
            Check("the hero stays up", !rider.Crashed, $"crashed={rider.Crashed}");
            Check("the free hit is used", rider.FreeHitUsed);
            Check("the hero loses speed", after < before * 0.9f, $"{before:0.0} -> {after:0.0} m/s");
            typeof(SummitGame).GetMethod("HitWarningStep", NP).Invoke(game, new object[] { Dt });
            Check("the warning: the red pulse", game.HitPulseNow > 0f, $"pulse {game.HitPulseNow:0.00}");
            Check("the warning: the marker", hud.HitMarkShown);
            Check("the warning: the game heard it", SummitGame.FreeHits > 0, $"{SummitGame.FreeHits}");
            // the thrown rider: up again, or gone while nobody sees
            int up0 = MountainRiders.GotUp, gone0 = MountainRiders.GoneDown;
            // (the hero held where they are meanwhile: the slope itself has no collider)
            var heroAt = rider.Body.position; var heroV = rider.Body.linearVelocity; rider.Body.isKinematic = true;
            for (float t = 0f; t < 30f && first.down != null; t += Dt) { mr.PhysicsStep(); Physics.Simulate(Dt); Look(first.pos); mr.Step(Dt, cam); }
            rider.Body.isKinematic = false; rider.Body.position = heroAt; rider.transform.position = heroAt; rider.Body.linearVelocity = heroV;
            Check("the thrown rider gets up (or goes unseen)", first.down == null && (MountainRiders.GotUp > up0 || MountainRiders.GoneDown > gone0), $"up {MountainRiders.GotUp - up0}, gone {MountainRiders.GoneDown - gone0}");
            // the second hit in the same run: the hero goes down too
            var second = Ahead(7f, true);
            Debug.Log($"RIDERCRASH second: hero {rider.Body.position} v {rider.Body.linearVelocity} kin {rider.Body.isKinematic} running {rider.Running} crashed {rider.Crashed}; them {second.pos} solid {(second.solid != null)} drawn {second.drawn} d {second.d:0.0} act {second.act}");
            bool hit2 = Ride(4f, out _, second, -1);
            Debug.Log($"RIDERCRASH second after: hero {rider.Body.position} closest {closest:0.00} m; them {second.pos} solid {(second.solid != null)}");
            Check("the second hit happens", hit2 && second.down != null);
            Check("the second hit brings the hero down", rider.Crashed, $"crashed={rider.Crashed}");
            // a new run: the free hit back
            NewRun();
            typeof(SummitGame).GetMethod("HitWarningStep", NP).Invoke(game, new object[] { Dt });
            Check("a new run gives the free hit back", !rider.FreeHitUsed && !hud.HitMarkShown);
            var third = Ahead(14f, false);
            Ride(4f, out _, third, -1);
            Check("and the next hit is free again", !rider.Crashed && rider.FreeHitUsed && third.down != null);
        }
        catch (System.Exception e) { Debug.LogError("RIDERCRASH exception " + e); fails++; }
        RiderInput.TestDrive = false; RiderInput.TestFingers = 0; MountainRiders.TestWant = -1;
        Debug.Log(fails == 0 ? $"RIDERCRASH PASS ({checks} checks)" : $"RIDERCRASH FAIL ({fails} of {checks})");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        TestPrefs.Restore();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>A new run, riding straight down the run at z 320.</summary>
    static void NewRun()
    {
        typeof(SummitGame).GetMethod("ToReady", NP, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { false });
        typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
        rider.input = null;
        float z = 320f;
        rider.ResetAt(Slope.Centre(z), z); body.ResetVisual(); rider.Begin();
        rider.Body.linearVelocity = Vector3.forward * 11f;
        terrain.Prewarm(); props.Prewarm();
    }

    /// <summary>One rider on the mountain, `ahead` metres in the hero's way, standing; given time on the snow unless fresh.</summary>
    static MountainRiders.Rider Ahead(float ahead, bool ski, bool fresh = false)
    {
        var p = rider.Body.position; var v = rider.Body.linearVelocity; v.y = 0f;
        var dir = v.sqrMagnitude > 0.5f ? v.normalized : Vector3.forward;
        var at = p + dir * ahead; at.y = Slope.DrawnHeight(at.x, at.z);
        var r = mr.TestOnly(at, ski);
        if (fresh) mr.TestFresh(r);
        Look(at); mr.Step(Dt, cam);
        return r;
    }

    static void Look(Vector3 at)
    {
        var p = rider.transform.position;
        var f = at - p; f.y = 0f; if (f.sqrMagnitude < 0.01f) f = Vector3.forward; f.Normalize();
        cam.transform.SetPositionAndRotation(p - f * 6.5f + Vector3.up * 2.6f, Quaternion.LookRotation(at + Vector3.up * 0.8f - (p - f * 6.5f + Vector3.up * 2.6f), Vector3.up));
    }

    /// <summary>Ride on for up to `seconds`, the game's way; true once someone was knocked over. Frames of the first hit if asked (shot 0).</summary>
    static bool Ride(float seconds, out float atHit, MountainRiders.Rider target = null, int shot = -1)
    {
        int k0 = MountainRiders.Knocked; atHit = -1f; closest = 99f;
        float shotNext = 0f; int shotN = 0;
        for (float t = 0f; t < seconds; t += Dt)
        {
            for (int k = 0; k < 2; k++)
            {
                rider.TestContacts(Dt / 2f);
                rider.Tick(Dt / 2f); body.PhysicsStep(); mr.PhysicsStep(); Physics.Simulate(Dt / 2f);
            }
            body.Apply(Dt);
            Look(target != null ? target.pos : rider.transform.position + Vector3.forward * 10f);
            mr.Step(Dt, cam);
            game.CrashTapStep(Dt);
            if (target != null) closest = Mathf.Min(closest, Vector2.Distance(new Vector2(rider.Body.position.x, rider.Body.position.z), new Vector2(target.pos.x, target.pos.z)));
            if (atHit < 0f && MountainRiders.Knocked > k0) atHit = t;
            if (atHit < 0f) speedBefore = rider.Speed;
            else if (t <= atHit + 0.25f) speedAfter = rider.Speed;
            if (shot == 0 && shots != null && target != null)
            {
                float d = Vector3.Distance(rider.transform.position, target.pos);
                if ((d < 9f && atHit < 0f && shotN == 0) || (atHit >= 0f && t >= atHit + shotNext && shotN < 5))
                {
                    if (atHit >= 0f) shotNext += shotN == 1 ? 0.15f : 0.35f;
                    typeof(SummitGame).GetMethod("HitWarningStep", NP).Invoke(game, new object[] { Dt });
                    Shoot($"ridercrash-{++shotN}.png");
                }
            }
            if (atHit >= 0f && t > atHit + 1.6f) break;
        }
        return MountainRiders.Knocked > k0;
    }

    static void Shoot(string file)
    {
        terrain.Prewarm(); props.Prewarm();
        int w = 590, h = 1278;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.aspect = w / (float)h;
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var baked = new List<GameObject>(); var off = new List<SkinnedMeshRenderer>();
        foreach (var sk in skins)
        {
            if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
            var m = new Mesh(); sk.BakeMesh(m);
            var g = new GameObject("b", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = sk.gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr2 = g.GetComponent<MeshRenderer>(); mr2.sharedMaterials = sk.sharedMaterials;
            var mpb = new MaterialPropertyBlock(); sk.GetPropertyBlock(mpb); mr2.SetPropertyBlock(mpb);
            g.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            sk.enabled = false; off.Add(sk); baked.Add(g);
        }
        Canvas.ForceUpdateCanvases();
        cam.Render();
        foreach (var g in baked) { Object.DestroyImmediate(g.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(g); }
        foreach (var sk in off) sk.enabled = true;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(shots, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("RIDERCRASH frame " + file);
    }
}
