using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Summit;

public static partial class PeopleTest
{
    // ---------------------------------------------------------------- outfits

    /// <summary>
    /// Does everyone in the camp keep one outfit? The camp for SUMMIT_OUTFIT_MINUTES of game time (20), the lifts
    /// running as at the camp (their riders take rows of the shared palette as they come, a new look each lap), the
    /// camera going round the camp from 3 m out to 150 m and back (through the full model, the simplified one and
    /// out of range), the time of day going round. Each frame, for every person in the camp (CampLife's, the lodge's,
    /// the couple's): the look they wear, and what is drawn of it: the full model's meshes, cloth colours and gear
    /// palette, or the simplified body's mesh and its row of the palette (and whether that row still holds their look's
    /// colours). Any of that changing while they are in sight is a flicker. -executeMethod PeopleTest.Outfits
    /// (no graphics needed). Logs/people-outfits.txt; exit 1 on any flicker.
    /// </summary>
    public static void Outfits()
    {
        var game = TestHarness.Boot();
        var camp = game.camp;
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        ChaseCamera.TestDt = 1f / 30f;
        var life = camp.GetComponent<CampLife>();
        if (life == null || life.Agents.Count == 0) { Debug.LogError("OUTFITS no camp life"); TestHarness.Exit(1); return; }
        var lift = Object.FindFirstObjectByType<SkiLift>();
        var lUpd = typeof(SkiLift).GetMethod("Update", NP);
        var fNext = typeof(PeopleKit).GetField("nextRow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var fRowLook = typeof(PeopleKit).GetField("rowLook", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var mColours = typeof(PeopleKit).GetMethod("RowColours", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        float minutes = float.TryParse(System.Environment.GetEnvironmentVariable("SUMMIT_OUTFIT_MINUTES"), out float mm) ? mm : 20f;
        const float dt = 1f / 30f;
        int frames = Mathf.RoundToInt(minutes * 60f / dt);
        var cam = Camera.main;
        var centre = Vector3.zero; foreach (var a in life.Agents) centre += a.pos; centre /= life.Agents.Count;
        var mpb = new MaterialPropertyBlock();
        var state = new Dictionary<Person, Track>();
        var people = new List<Person>();
        int rowsTaken = 0, lastNext = (int)fNext.GetValue(null);
        int lod0Frames = 0, lod1Frames = 0, lookChanged = 0, fullChanged = 0, simpleChanged = 0, rowNotTheirs = 0, liftShown = 0;
        var firstFlicker = new List<string>();
        void Flicker(string what) { if (firstFlicker.Count < 12) firstFlicker.Add(what); }
        var want = new Color[PeopleKit.Slots];
        for (int f = 0; f < frames; f++)
        {
            float t = f * dt;
            // the camera round the camp: in and out through the levels of detail, and round
            float r = Mathf.Lerp(3f, 150f, Mathf.PingPong(t / 60f, 1f));
            float ang = t * 2f * Mathf.PI / 75f;
            var at = centre + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r;
            at.y = Mathf.Max(Slope.DrawnHeight(at.x, at.z), centre.y) + 1.7f + r * 0.12f;
            cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(centre - at));
            TimeOfDay.TestClock = Mathf.Repeat(t / 90f, 4f);
            // the lifts, as at the camp
            SkiLift.TestClock = 300.0 + t;
            lUpd.Invoke(lift, null);
            liftShown += LiftRiders.MidShown + LiftRiders.FarShown + LiftRiders.DistantShown + LiftRiders.NearShown;
            int nx = (int)fNext.GetValue(null);
            rowsTaken += (nx - lastNext + PeopleKit.Rows) % PeopleKit.Rows; lastNext = nx;
            // the camp
            life.Step(dt, at); life.Tick(dt);
            if (LodgeFolk.Current != null) LodgeFolk.Current.Step(dt);
            if (camp.Couple != null) camp.Couple.Step(dt);
            if (f % 30 == 0)
            {
                // everyone but the lifts' own (who change on purpose with the chair's riders)
                people.Clear();
                foreach (var p in Object.FindObjectsByType<Person>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (p.transform.parent != PeopleKit.Root) people.Add(p);
            }
            // what everyone shows
            foreach (var p in people)
            {
                if (p == null) continue;
                if (!state.TryGetValue(p, out var s)) state[p] = s = new Track { look = p.Look };
                bool shown = p.gameObject.activeInHierarchy && p.Lod >= 0 && p.Lod < 2;
                if (p.Look != s.look)
                {
                    // (someone new out of the lodge's door is CampLife's way: only one in sight is a flicker)
                    if (s.shown && shown) { lookChanged++; Flicker($"{t / 60f:0.0} min {p.name}: a new look while in sight"); }
                    s = state[p] = new Track { look = p.Look };
                }
                if (shown && p.Lod == 0)
                {
                    lod0Frames++;
                    string sig = Full(p);
                    if (s.full == null) s.full = sig;
                    else if (sig != s.full) { fullChanged++; Flicker($"{t / 60f:0.0} min {p.name}: the full model's clothes changed"); s.full = sig; }
                }
                else if (shown && p.Lod == 1 && p.LodSkin != null)
                {
                    lod1Frames++;
                    p.LodSkin.GetPropertyBlock(mpb);
                    int row = Mathf.RoundToInt(mpb.GetFloat("_Row"));
                    var px = PeopleKit.Atlas.GetPixels(0, row, PeopleKit.Slots, 1);
                    string sig = (p.LodSkin.sharedMesh != null ? p.LodSkin.sharedMesh.GetInstanceID() : 0) + ":" + Colours(px);
                    var owner = ((PersonLook[])fRowLook.GetValue(null))[row];
                    bool theirs = owner == p.Look;
                    if (mColours != null) { mColours.Invoke(null, new object[] { p.Look, want }); theirs &= Same(want, px); }
                    if (!theirs) { rowNotTheirs++; if (!s.toldRow) { s.toldRow = true; Flicker($"{t / 60f:0.0} min {p.name}: far off in row {row}, which holds {(owner != null ? owner.name ?? "another look" : "nobody's look")}"); } }
                    if (s.simple == null) s.simple = sig;
                    else if (sig != s.simple) { simpleChanged++; Flicker($"{t / 60f:0.0} min {p.name}: the simplified body's colours changed (row {row})"); s.simple = sig; }
                }
                s.shown = shown;
            }
        }
        TimeOfDay.TestClock = -1f; SkiLift.TestClock = -1.0;
        var sb = new StringBuilder();
        sb.AppendLine($"OUTFITS {minutes:0} min of game time at the camp, {frames} frames at 30 fps, {state.Count} people seen (CampLife {life.Agents.Count})");
        sb.AppendLine($"OUTFITS the lifts drew {liftShown / (float)frames:0} riders a frame on average; palette rows taken by new looks: {rowsTaken} ({rowsTaken / minutes:0} a minute; the palette has {PeopleKit.Rows})");
        sb.AppendLine($"OUTFITS person-frames drawn: full model {lod0Frames}, simplified {lod1Frames}");
        sb.AppendLine($"OUTFITS a new look while in sight: {lookChanged}; full model's clothes changed: {fullChanged}; simplified body's colours changed: {simpleChanged}; drawn far off in a row not holding their look: {rowNotTheirs} person-frames");
        foreach (var w in firstFlicker) sb.AppendLine("OUTFITS   " + w);
        bool ok = lookChanged == 0 && fullChanged == 0 && simpleChanged == 0 && rowNotTheirs == 0;
        sb.AppendLine(ok ? "OUTFITS everyone kept one outfit" : "OUTFITS FLICKER");
        Debug.Log(sb.ToString());
        System.IO.File.WriteAllText("Logs/people-outfits.txt", sb.ToString());
        ChaseCamera.TestDt = 0f;
        TestPrefs.Restore();
        TestHarness.Exit(ok ? 0 : 1);
    }

    class Track { public PersonLook look; public string full, simple; public bool shown, toldRow; }

    /// <summary>The same colours, as near as the palette's 8 bits a channel keep them.</summary>
    static bool Same(Color[] a, Color[] b)
    {
        for (int i = 0; i < Mathf.Min(a.Length, b.Length); i++)
            if (Mathf.Abs(a[i].r - b[i].r) > 0.006f || Mathf.Abs(a[i].g - b[i].g) > 0.006f || Mathf.Abs(a[i].b - b[i].b) > 0.006f || Mathf.Abs(a[i].a - b[i].a) > 0.006f) return false;
        return true;
    }

    static string Colours(Color[] px)
    {
        var sb = new StringBuilder();
        foreach (var c in px) sb.Append(((Color32)c).GetHashCode().ToString("x")).Append('.');
        return sb.ToString();
    }

    /// <summary>The full model as drawn: each skin's mesh, its cloth's colours and pattern, the gear's palette.</summary>
    static string Full(Person p)
    {
        var sb = new StringBuilder();
        foreach (var s in new[] { p.BodySkin, p.LegsSkin, p.GearSkin, p.HeadSkin })
        {
            if (s == null) continue;
            sb.Append(s.sharedMesh != null ? s.sharedMesh.GetInstanceID() : 0).Append(',');
            var m = s.sharedMaterial;
            if (m == null) { sb.Append("none;"); continue; }
            if (m.HasProperty("_BaseColor")) sb.Append((Color32)m.GetColor("_BaseColor"));
            if (m.HasProperty("_Color2")) sb.Append((Color32)m.GetColor("_Color2")).Append(m.GetFloat("_Pattern"));
            if (m.mainTexture is Texture2D tx && tx.isReadable) sb.Append(Colours(tx.GetPixels()));
            sb.Append(';');
        }
        return sb.ToString();
    }
}
