using System.Reflection;
using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>
/// Headless physics check: rides a minute down the mountain and logs speed and airtime.
/// Run: Unity -batchmode -projectPath . -executeMethod SimTest.Run -logFile sim.log
/// </summary>
public static class SimTest
{
    public static void Run()
    {
        MountainField.Init(1234);
        var ig = new GameObject("in"); var input = ig.AddComponent<RiderInput>();
        var rg = new GameObject("rider"); var r = rg.AddComponent<Rider>();
        r.input = input;
        r.Build(null, null, null, null, null, null, null);
        var step = typeof(Rider).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance);
        var steerProp = typeof(RiderInput).GetProperty("Steer");
        // a row of tuning values to try: gravity, airGravity, dragOpen, dragTuck, lipScale, lipMaskLow
        var configs = new[] { new[] { Rider.Gravity, Rider.AirGravity, Rider.DragOpen, Rider.DragTuck, MountainField.LipScale, MountainField.LipMaskLow } };
        Rider.TakeoffClearance = 0.25f;
        foreach (var c in configs)
        foreach (float steerAmp in new[] { 0f, 0.25f })
        {
            Rider.Gravity = c[0]; Rider.AirGravity = c[1]; Rider.DragOpen = c[2]; Rider.DragTuck = c[3]; MountainField.LipScale = c[4]; MountainField.LipMaskLow = c[5];
            r.ResetAt(MountainField.DriftX(0f), 0f); r.Begin();
            int takeoffs = 0, bigAirs = 0, tiny = 0, airFrames = 0; bool wasAir = false; float maxSpeed = 0f, sumSpeed = 0f, airStart = 0f;
            int n = 120 * 60;
            for (int i = 0; i < n; i++)
            {
                float t = i / 120f;
                // weave gently around the good line
                float s = steerAmp * Mathf.Sin(t * 0.9f) + Mathf.Clamp((MountainField.DriftX(r.Z) - r.X) * 0.02f - r.Heading, -1f, 1f) * 0.5f;
                steerProp.SetValue(input, Mathf.Clamp(s, -1f, 1f));
                step.Invoke(r, new object[] { 1f / 120f, false });
                if (r.Airborne && !wasAir) { takeoffs++; airStart = t; }
                if (!r.Airborne && wasAir) { if (t - airStart > 0.4f) bigAirs++; else if (t - airStart < 0.15f) tiny++; }
                if (r.Airborne) airFrames++;
                wasAir = r.Airborne;
                maxSpeed = Mathf.Max(maxSpeed, r.Speed); sumSpeed += r.Speed;
            }
            Debug.Log($"SIM g={c[0]} ag={c[1]} d={c[2]} lip={c[4]} mask={c[5]} steer={steerAmp} tiny<0.15s={tiny} takeoffs/min={takeoffs} airs>0.4s={bigAirs} avg={sumSpeed / n * 3.6f:0} km/h max={maxSpeed * 3.6f:0} km/h dist={r.Distance:0} m crashed={r.Crashed} air%={100f * airFrames / n:0}");
        }
        // responsiveness: from riding straight at speed, full steer; time until travel turns 20 degrees
        r.ResetAt(MountainField.DriftX(0f), 0f); r.Begin();
        for (int i = 0; i < 120 * 6; i++) { steerProp.SetValue(input, 0f); step.Invoke(r, new object[] { 1f / 120f, false }); }
        float h0 = r.TravelHeading, t20 = -1f;
        for (int i = 0; i < 240; i++)
        {
            steerProp.SetValue(input, 1f); step.Invoke(r, new object[] { 1f / 120f, false });
            if (t20 < 0f && Mathf.Abs(r.TravelHeading - h0) > 20f * Mathf.Deg2Rad) t20 = i / 120f;
        }
        Debug.Log($"SIM response: 20deg turn of travel in {t20:0.00}s at {r.Speed * 3.6f:0} km/h, slip {(r.Heading - r.TravelHeading) * Mathf.Rad2Deg:0} deg");
        EditorApplication.Exit(0);
    }
}
