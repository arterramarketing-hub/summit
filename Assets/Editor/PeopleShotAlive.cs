using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Summit;

public static partial class PeopleShot
{
    // ---------------------------------------------------------------- the mountain's other riders

    static MountainRiders mr;
    static SkiLift aliveLift; static MethodInfo aliveLUpd;

    /// <summary>The riders, as the game draws them: run for a while round the camera (the player out of the way, so
    /// they stop, sit and fall as they do when you aren't near), then the camera put where it shows them.</summary>
    static void RideFor(float seconds, Vector3 eye, Vector3 look)
    {
        Aim(eye, look, cam.fieldOfView);
        for (float t = 0f; t < seconds; t += 1f / 30f) mr.Step(1f / 30f, cam);
    }

    static void Frame(string file, Vector3 eye, Vector3 look, float fov)
    {
        // the player (whom terrain and forest stream round) by the camera, standing still: no one yields to him
        var back = look - eye; back.y = 0f; back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.forward;
        rider.ResetAt(eye.x - back.x * 10f, eye.z - back.z * 10f); rider.Stop();   // (behind the camera, out of the picture)
        terrain.Prewarm(); props.Prewarm();
        cam.GetComponent<ChaseCamera>().enabled = false;
        Aim(eye, look, fov);
        if (aliveLift != null) { SkiLift.TestClock = 400.0; aliveLUpd.Invoke(aliveLift, null); }
        mr.Step(1f / 60f, cam);
        Shoot(file);
        var sb = new System.Text.StringBuilder();
        int n = 0;
        foreach (var r in mr.All) if (r.active && r.drawn) { n++; if (r.d < 120f) sb.Append($"{r.look.name}:{r.act}{(r.sitW > 0.5f ? " sat" : "")} {r.d:0}m {r.pos.y - Slope.DrawnHeight(r.pos.x, r.pos.z):0.00}m up{(r.near != null ? $" person lod{r.lod}" : $" baked tier{r.tier}")}  "); }
        Log($"{file}: {n} riders drawn, {mr.PeopleShown} as people, {mr.DrawCalls} draws, {mr.Triangles} triangles; near: {sb}");
    }

    static void RiderShots()
    {
        aliveLift = Object.FindFirstObjectByType<SkiLift>();
        aliveLUpd = typeof(SkiLift).GetMethod("Update", NP);
        steps.Add(() =>
        {
            typeof(SummitGame).GetMethod("ToRiding", NP).Invoke(game, null);
            rider.input = null;
            mr = MountainRiders.Current;
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
        });
        // the chase view above a lift's bottom (the busiest stretch), as you'd see it riding
        float zl = 700f;
        foreach (var l in LiftLine.Lifts) if (l.bottom.z > 300f) { zl = l.bottom.z; break; }
        void Chase(string name, float z, float clock)
        {
            steps.Add(() =>
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                float x = Slope.Centre(z);
                var at = new Vector3(x, Slope.DrawnHeight(x, z), z);
                RideFor(40f, at + new Vector3(0f, 3.2f, -6.5f), at + new Vector3(0f, 1.2f, 30f));
                Frame($"riders-{name}.png", at + new Vector3(0f, 3.2f, -6.5f), at + new Vector3(0f, 0.6f, 25f), 62f);
            });
        }
        // (on a plain cruising stretch, as most of the run is)
        float zc = zl - 450f;
        for (float z = 120f; z < zl; z += 10f)
        {
            bool plain = true;
            for (float zz = z; zz < z + 120f && plain; zz += 10f) if (Course.At(zz) != Course.Section.Cruise || Course.InGlacier(zz)) plain = false;
            if (plain) { zc = z + 20f; break; }
        }
        Chase("chase-day", zc, 1.4f);
        // the run from above, down to a lift's bottom
        steps.Add(() =>
        {
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            float z = zl - 330f, x = Slope.Centre(z);
            var eye = new Vector3(x + 12f, Slope.DrawnHeight(x, z) + 26f, z);
            var look = new Vector3(Slope.Centre(z + 200f), Slope.DrawnHeight(Slope.Centre(z + 200f), z + 200f), z + 200f);
            RideFor(30f, eye, look);
            Frame("riders-wide-day.png", eye, look, 55f);
        });
        // up close: a rider carving, friends stopped at the edge, someone sat in the snow, a beginner, a fall
        void Close(string name, System.Func<MountainRiders.Rider, bool> want, float dist, float side0, float up, float fov, float clock = 1.4f)
        {
            steps.Add(() =>
            {
                TimeOfDay.TestClock = clock; dayNight.ApplyNow();
                float z = zl - 400f, x = Slope.Centre(z);
                var eye0 = new Vector3(x, Slope.DrawnHeight(x, z) + 4f, z - 20f);
                Aim(eye0, eye0 + new Vector3(0f, -0.3f, 1f), 60f);
                MountainRiders.Rider found = null;
                for (float t = 0f; t < 240f && found == null; t += 1f / 30f)
                {
                    mr.Step(1f / 30f, cam);
                    foreach (var r in mr.All) if (r.active && want(r)) { found = r; break; }
                }
                if (found == null) { Log($"riders-{name}: nobody doing that in 4 minutes"); return; }
                var r0 = found;
                // looking at it from beside its way
                var dir = new Vector3(Mathf.Sin(r0.heading), 0f, Mathf.Cos(r0.heading));
                var sideV = new Vector3(dir.z, 0f, -dir.x);
                float side = side0;
                if (Vector3.Dot(sideV * side, new Vector3(Slope.Centre(r0.pos.z) - r0.pos.x, 0f, 0f)) < 0f) side = -side;   // (on the run's side of it, not off over the edge)
                var target = r0.pos + Vector3.up * (r0.sitW > 0.5f ? 0.5f : 0.95f);
                var eye = target + sideV * side - dir * dist + Vector3.up * up;
                eye.y = Mathf.Max(eye.y, Slope.DrawnHeight(eye.x, eye.z) + 1.2f);
                for (int i = 0; i < 4; i++) { Aim(eye, target, fov); mr.Step(1f / 60f, cam); }
                eye = r0.pos + Vector3.up * (r0.sitW > 0.5f ? 0.5f : 0.95f) + sideV * side - dir * dist + Vector3.up * up;
                eye.y = Mathf.Max(eye.y, Slope.DrawnHeight(eye.x, eye.z) + 1.2f);
                Frame($"riders-{name}.png", eye, r0.pos + Vector3.up * (r0.sitW > 0.5f ? 0.5f : 0.95f), fov);
                Log($"riders-{name}: {r0.look.name} ({(r0.ski ? "skis" : "board")}, skill {r0.skill:0.00}) {r0.act} at {r0.speed:0.0} m/s{(r0.group != null ? $", one of {r0.group.members.Count}" : "")}");
            });
        }
        Close("carve", r => r.act == MountainRiders.Act.Ride && r.speed > 9f && Mathf.Abs(r.carve) > 0.5f && !r.ski && r.air <= 0f, 6.5f, 2.5f, 0.6f, 45f);
        Close("ski", r => r.act == MountainRiders.Act.Ride && r.speed > 9f && r.ski && r.air <= 0f, 6.5f, -2.5f, 0.6f, 45f);
        Close("group", r => r.act == MountainRiders.Act.Stop && r.speed <= 0f && r.group != null && r.group.members.Count >= 3 && r.t > 2f, 9f, 7f, 1.2f, 50f);
        Close("sitting", r => r.sitW > 0.95f && r.act == MountainRiders.Act.Stop && !r.ski, 4.5f, 3f, 0.6f, 45f);
        Close("fall", r => r.act == MountainRiders.Act.Fall && r.sitW > 0.6f, 5f, 3f, 0.8f, 45f);
        Close("beginner", r => r.act == MountainRiders.Act.Ride && r.skill < 0.25f && r.speed > 2f, 7f, 3f, 1.0f, 45f);
        Chase("chase-dusk", zc, 2.6f);
        // and from the camp: the run below it
        steps.Add(() =>
        {
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            float x = Slope.Centre(-50f);
            var eye = new Vector3(x + 4f, Slope.DrawnHeight(x, -50f) + 3f, -50f);
            var look = new Vector3(Slope.Centre(120f), Slope.DrawnHeight(Slope.Centre(120f), 120f), 120f);
            RideFor(30f, eye, look);
            Frame("riders-from-camp.png", eye, look, 55f);
        });
        steps.Add(() => { SkiLift.TestClock = -1.0; });
    }

    // ---------------------------------------------------------------- the camp's people, near to far

    /// <summary>
    /// Two people of the camp talking, from 4 m out to 90 m (the full model, the simplified one, far off), each picture
    /// framed to the same size; after the lifts have run 20 minutes (their riders taking palette rows all the while: what
    /// took the camp's outfits from them).
    /// </summary>
    static void CampNpcShots()
    {
        aliveLift = Object.FindFirstObjectByType<SkiLift>();
        aliveLUpd = typeof(SkiLift).GetMethod("Update", NP);
        CampLife.Agent a = null;
        steps.Add(() =>
        {
            life = camp.GetComponent<CampLife>();
            TimeOfDay.TestClock = 1.4f; dayNight.ApplyNow();
            float cx = Slope.Centre(Slope.PadLip);
            camp.TeleportTo(new Vector3(cx - 16f, 0f, -45f), 90f);
            int rows0 = PeopleKit.RowsMade;
            for (int f = 0; f < 20 * 60 * 30; f++)
            {
                SkiLift.TestClock = 300.0 + f / 30.0;
                aliveLUpd.Invoke(aliveLift, null);
                if (f % 3 == 0) { life.Step(0.1f, cam.transform.position); life.Tick(0.1f); }
            }
            Log($"campnpc: the lifts ran 20 minutes: {PeopleKit.RowsMade - rows0} palette rows taken by their riders (the palette has {PeopleKit.Rows})");
            a = Find(x => x.doing == CampLife.Doing.Chat && x.partner != null && x.partner.doing == CampLife.Doing.Chat && (x.pos - x.partner.pos).magnitude < 1.8f && x.p.gameObject.activeSelf, 300f);
            if (a == null) Log("campnpc: nobody talking");
        });
        foreach (float d in new[] { 4f, 12f, 25f, 50f, 90f })
        {
            float dist = d;
            steps.Add(() =>
            {
                if (a == null) return;
                var mid = (a.pos + a.partner.pos) * 0.5f + Vector3.up * 1.0f;
                var across = Vector3.Cross(Vector3.up, (a.partner.pos - a.pos).normalized);
                var eye = mid + across * dist + Vector3.up * (0.4f + dist * 0.08f);
                eye.y = Mathf.Max(eye.y, Slope.DrawnHeight(eye.x, eye.z) + 1.2f);
                // the hero out of the picture, behind the camera
                camp.TeleportTo(eye + across * 3f, 0f, false);
                cam.GetComponent<ChaseCamera>().enabled = false;
                Aim(eye, mid, 2f * Mathf.Atan(1.8f / dist) * Mathf.Rad2Deg);
                life.Step(1f / 60f, cam.transform.position);   // (their levels of detail for this distance)
                Aim(eye, mid, 2f * Mathf.Atan(1.8f / dist) * Mathf.Rad2Deg);
                Shoot($"campnpc-{dist:0}m.png");
                Log($"campnpc-{dist:0}m: {a.p.Look.jacket.name} and {a.partner.p.Look.jacket.name}, levels of detail {a.p.Lod} and {a.partner.p.Lod}");
            });
        }
        steps.Add(() => { SkiLift.TestClock = -1.0; });
    }
}
