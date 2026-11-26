using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Summit;

public static partial class PeopleTest
{
    // ---------------------------------------------------------------- one look at every distance

    /// <summary>
    /// Is everyone in the camp the same person at every distance? Each camp, lodge and couple person is drawn on their
    /// own, from three sides, at the distances where the game changes how it draws people:
    ///   - 14, 25, 50, 90 m: the full model (what you see close up) against the simplified body (CampLife, LodgeFolk,
    ///     MountainRiders past 14-15 m);
    ///   - 25, 50 m: against a baked pose at the lifts' mid detail (LiftRiders 15-75 m);
    ///   - 90 m: against the far bakes (LiftRiders 75-250 m, MountainRiders 90-220 m). (Further off a person is a few
    ///     pixels high: there, as everywhere, what is checked is that their row of the palette holds exactly what the
    ///     full model wears, and that every distance mesh carries the clothes' rest pose: RowDiff.)
    /// Each picture is as many pixels as the phone gives the person at that distance (2000 high, the game's field of
    /// view), lit by plain white light (no sun, lamps, reflections or fog: what is compared is the outfit, not the two
    /// shaders' lighting). Every pixel inside an even patch of colour in one must find a pixel of the same colour (within
    /// 0.07) in the other within the simplification's reach (its cell); more than LodLooksMaxBad of them not finding
    /// one is a different outfit. Then
    /// one person is dressed in every patterned jacket and pair of trousers in the closet, and drawn the same way.
    /// -executeMethod PeopleTest.LodLooks (graphics needed). Logs/people-lodlooks.txt, pictures in Logs/lodlooks/;
    /// exit 1 on any difference.
    /// </summary>
    public static void LodLooks()
    {
        EditorSceneManager.OpenScene("Assets/Summit/Summit.unity");
        var game = Object.FindFirstObjectByType<SummitGame>();
        TestPrefs.Pin();
        TestBoot.Boot(game);
        var camp = (Summit.Camp)typeof(SummitGame).GetField("camp", NP).GetValue(game);
        typeof(SummitGame).GetMethod("ToHub", NP).Invoke(game, new object[] { true });
        var life = camp.GetComponent<CampLife>();
        if (life == null || life.Agents.Count == 0) { Debug.LogError("LODLOOKS no camp life"); TestHarness.Exit(1); return; }
        var main = Camera.main;
        float gameFov = main != null ? main.fieldOfView : 60f;
        // a minute of the camp's life: everyone about, doing what they do
        const float dt = 1f / 30f;
        for (int f = 0; f < 60 * 30; f++)
        {
            life.Step(dt, main.transform.position); life.Tick(dt);
            if (LodgeFolk.Current != null) LodgeFolk.Current.Step(dt);
            if (camp.Couple != null) camp.Couple.Step(dt);
        }
        var people = new List<Person>();
        foreach (var p in Object.FindObjectsByType<Person>(FindObjectsSortMode.None))
            if (p.transform.parent != PeopleKit.Root && p.gameObject.activeInHierarchy && p.Look != null) people.Add(p);
        people.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // the phone's own quality level (its renderer), and plain white light: the outfit, not the lighting
        QualitySettings.SetQualityLevel(0, true);
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.enabled = false;
        foreach (var rp in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) rp.enabled = false;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white;
        var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(Color.white); RenderSettings.ambientProbe = sh;
        RenderSettings.reflectionIntensity = 0f; RenderSettings.fog = false;
        var camGo = new GameObject("LodLooksCam");
        var cam = camGo.AddComponent<Camera>(); cam.enabled = false;
        cam.cullingMask = 1 << ShotLayer; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0f, 1f, 0f, 1f);
        cam.allowMSAA = false; cam.allowHDR = false; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;
        var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = false; cd.antialiasing = AntialiasingMode.None; cd.renderShadows = false;
        string dir = "Logs/lodlooks"; Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "*.png")) File.Delete(old);

        var sb = new StringBuilder();
        sb.AppendLine($"LODLOOKS {people.Count} people (CampLife {life.Agents.Count}); the game's field of view {gameFov:0.#} deg, {PhoneHeight} pixels high; a pixel 'not found' when no pixel within the cell's reach is within {Tol:0.00} of its colour; fail past {LodLooksMaxBad:P0}");
        int checks = 0, fails = 0;
        var worst = new Dictionary<string, float>();
        void Note(string what, float bad) { if (!worst.TryGetValue(what, out float w) || bad > w) worst[what] = bad; }

        // every way of drawing a person at a distance, against the full model
        bool Check(Person p, string who, bool pictures)
        {
            bool ok = true;
            p.SetLod(0); p.Apply(0f);
            string rowDiff = RowDiff(p);
            checks++;
            if (rowDiff != null) { fails++; ok = false; sb.AppendLine($"LODLOOKS FAIL {who} ({Wears(p.Look)}): far off its row isn't what the full model wears: {rowDiff}"); }
            foreach (float d in Distances)
            {
                foreach (var rep in Reps)
                {
                    if (d < rep.from || d > rep.to) continue;
                    float worstBad = 0f; Color32[] wa = null, wb = null; int wn = 0, seen = 0;
                    for (int side = 0; side < 3; side++)
                    {
                        int n = Aim(cam, p, d, side * 120f, gameFov, out float px);
                        var a = Draw(cam, p, n, null, 0f);
                        var b = Draw(cam, p, n, rep.name, rep.cell);
                        int reach = Mathf.Max(1, Mathf.CeilToInt(rep.reach / px));
                        float bad = Unmatched(a, b, n, reach, out int looked);
                        seen += looked;
                        checks++;
                        if (bad > worstBad || wa == null) { worstBad = bad; wa = a; wb = b; wn = n; }
                    }
                    Note($"pixels looked at, {rep.name} at {d:0} m (fewest)", -seen);
                    Note($"{rep.name} at {d:0} m", worstBad);
                    bool fail = worstBad > LodLooksMaxBad;
                    if (fail) { fails++; ok = false; sb.AppendLine($"LODLOOKS FAIL {who} ({Wears(p.Look)}): {rep.name} at {d:0} m, {worstBad:P1} of its pixels not found in the full model's"); }
                    if (fail || pictures) Picture(Path.Combine(dir, $"{who}-{d:0}m-{rep.name}.png"), wa, wb, wn);
                }
            }
            return ok;
        }

        int bad0 = 0;
        foreach (var p in people)
        {
            int lod = p.Lod;
            bool patterned = (p.Look.jacket != null && p.Look.jacket.Patterned) || (p.Look.pants != null && p.Look.pants.Patterned);
            if (!Check(p, $"{people.IndexOf(p):00}-{p.name}", patterned)) bad0++;
            if (lod >= 0) p.SetLod(lod);
        }
        sb.AppendLine($"LODLOOKS the camp's people: {people.Count - bad0} of {people.Count} the same at every distance");

        // and every patterned jacket and pair of trousers in the closet, on one of them
        int closet = 0, badCloset = 0;
        if (people.Count > 0)
        {
            var p = people[0];
            var keep = p.Look;
            var clone = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var cat in new[] { Wardrobe.Cat.Jacket, Wardrobe.Cat.Pants })
            {
                foreach (var it in Wardrobe.Items(cat))
                {
                    if (it == null || !it.Patterned) continue;
                    var l = (PersonLook)clone.Invoke(keep, null);
                    if (cat == Wardrobe.Cat.Jacket) l.jacket = it; else l.pants = it;
                    l.name = null;
                    p.Dress(l);
                    closet++;
                    string who = $"closet-{(cat == Wardrobe.Cat.Jacket ? "jacket" : "pants")}-{it.name.Replace(' ', '_').Replace('\'', '_')}";
                    if (!Check(p, who, true)) badCloset++;
                }
            }
            p.Dress(keep);
        }
        sb.AppendLine($"LODLOOKS the closet's patterns: {closet - badCloset} of {closet} the same at every distance");
        foreach (var kv in worst)
            sb.AppendLine(kv.Key.StartsWith("pixels") ? $"LODLOOKS   {kv.Key}: {-kv.Value:0}" : $"LODLOOKS   worst {kv.Key}: {kv.Value:P1} of pixels not found");
        sb.AppendLine($"LODLOOKS {checks} pictures compared, {fails} different");
        bool pass = fails == 0 && people.Count > 0;
        sb.AppendLine(pass ? "LODLOOKS everyone is the same person at every distance" : "LODLOOKS DIFFERENT");
        Debug.Log(sb.ToString());
        File.WriteAllText("Logs/people-lodlooks.txt", sb.ToString());
        Object.DestroyImmediate(camGo);
        TestPrefs.Restore();
        TestHarness.Exit(pass ? 0 : 1);
    }

    const int ShotLayer = 31, PhoneHeight = 2000;
    const float Tol = 0.07f;
    public const float LodLooksMaxBad = 0.04f;
    // (past 90 m a person is a few pixels high, 11 at 250 m: what tells their outfit there is their row, RowDiff, the
    // same row every distance draws from; the distant bakes' 34 cm cells change the shape, not the colours)
    static readonly float[] Distances = { 14f, 25f, 50f, 90f };
    // what is drawn instead of the full model, where (from .. to m), and how far the simplification moves a surface
    struct Rep { public string name; public float cell, from, to, reach; }
    static readonly Rep[] Reps =
    {
        new Rep { name = "simplified", cell = 0f, from = 14f, to = 110f, reach = PeopleLod.SkinnedCell * 0.8f },
        new Rep { name = "baked-mid", cell = 0.075f, from = 15f, to = 75f, reach = 0.075f * 0.8f },
        new Rep { name = "baked-far", cell = 0.16f, from = 75f, to = 250f, reach = 0.16f * 0.8f },
    };

    static string Wears(PersonLook l) => $"{(l.jacket != null ? l.jacket.name : "-")} / {(l.pants != null ? l.pants.name : "-")}";

    /// <summary>The camera d m from the person, round from their front by an angle, a little above, as many pixels
    /// across the 2.4 m square round them as the phone would give it; out: a pixel's size there (m).</summary>
    static int Aim(Camera cam, Person p, float d, float angle, float gameFov, out float px)
    {
        float phonePx = 2f * d * Mathf.Tan(gameFov * 0.5f * Mathf.Deg2Rad) / PhoneHeight;
        int n = Mathf.Clamp(Mathf.RoundToInt(2.4f / phonePx), 12, 360);
        px = 2.4f / n;
        var target = p.transform.position + Vector3.up * 0.95f;
        var dir = Quaternion.AngleAxis(angle, Vector3.up) * p.transform.forward;
        dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
        dir = (dir + Vector3.up * 0.14f).normalized;
        cam.transform.SetPositionAndRotation(target + dir * d, Quaternion.LookRotation(-dir));
        cam.fieldOfView = 2f * Mathf.Atan(1.2f / d) * Mathf.Rad2Deg;
        cam.aspect = 1f;
        return n;
    }

    /// <summary>
    /// The person drawn on their own: the full model (rep null), the simplified body, or a pose baked at a cell size
    /// (as the lifts and the mountain's riders draw them). Skinned meshes are baked for the picture (batch mode doesn't
    /// skin), keeping the clothes' rest pose.
    /// </summary>
    static Color32[] Draw(Camera cam, Person p, int n, string rep, float cell)
    {
        var made = new List<Object>(); var off = new List<Renderer>(); var layers = new List<(GameObject, int)>();
        void Layer(GameObject g) { layers.Add((g, g.layer)); g.layer = ShotLayer; }
        GameObject Plain(Mesh m, Material[] mats, MaterialPropertyBlock mpb, Matrix4x4 at)
        {
            var g = new GameObject("lodlooks", typeof(MeshFilter), typeof(MeshRenderer)); g.layer = ShotLayer;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.Off;
            if (mpb != null) mr.SetPropertyBlock(mpb);
            g.transform.SetPositionAndRotation(at.GetColumn(3), at.rotation);
            made.Add(g); made.Add(m);
            return g;
        }
        if (rep == null || rep == "simplified")
        {
            if (rep == null) p.SetLod(0);
            else { PeopleLod.Skinned(p.Look); p.SetLod(1); }
            p.Apply(0f);
            foreach (var sk in p.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!sk.enabled || sk.sharedMesh == null) continue;
                var m = new Mesh(); sk.BakeMesh(m);
                // (the clothes' rest pose, where their patterns are drawn, if the baking left it out)
                var r2 = new List<Vector3>(); m.GetUVs(2, r2);
                if (r2.Count == 0 && sk.sharedMesh.vertexCount == m.vertexCount) { var s2 = new List<Vector3>(); var s3 = new List<Vector3>(); sk.sharedMesh.GetUVs(2, s2); sk.sharedMesh.GetUVs(3, s3); if (s2.Count == m.vertexCount) { m.SetUVs(2, s2); m.SetUVs(3, s3); } }
                var mpb = new MaterialPropertyBlock(); sk.GetPropertyBlock(mpb);
                Plain(m, sk.sharedMaterials, mpb, Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one));
            }
            foreach (var mr in p.Rigid) if (mr != null && mr.enabled && mr.gameObject.activeInHierarchy) Layer(mr.gameObject);
        }
        else
        {
            p.SetLod(0); p.Apply(0f);
            var m = PeopleLod.Bake(p, Matrix4x4.identity, cell, "LodLooks" + rep);
            var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Row", PeopleKit.Row(p.Look));
            PeopleKit.Flush();
            Plain(m, new[] { PeopleKit.LodMaterial }, mpb, Matrix4x4.identity);
        }
        var rt = RenderTexture.GetTemporary(n, n, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, n, n), 0, 0); tex.Apply();
        var px = tex.GetPixels32();
        RenderTexture.active = null; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(tex);
        foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
        foreach (var (g, l) in layers) if (g != null) g.layer = l;
        return px;
    }

    static bool Covered(Color32 c) => !(c.r == 0 && c.g == 255 && c.b == 0);

    /// <summary>
    /// The share of either picture's pixels that find no pixel of the same colour in the other within reach: the larger
    /// of the two ways round. Only pixels inside an even patch of colour count (all 8 round them the same colour,
    /// within the tolerance): an outline, the edge between two parts, a zip a centimetre wide, a stray pixel of
    /// face at 250 m are the shape and detail, which the simplification moves by up to its cell; the patches are the
    /// outfit. out looked: how many pixels were looked at.
    /// </summary>
    static float Unmatched(Color32[] a, Color32[] b, int n, int reach, out int looked)
    {
        int seen = 0;
        float tol2 = Tol * Tol * 255f * 255f;
        bool Near(Color32 p, Color32 q) { float dr = p.r - q.r, dg = p.g - q.g, db = p.b - q.b; return dr * dr + dg * dg + db * db <= tol2; }
        float One(Color32[] x, Color32[] y)
        {
            int total = 0, bad = 0;
            for (int j = 1; j < n - 1; j++)
                for (int i = 1; i < n - 1; i++)
                {
                    int k = j * n + i;
                    bool even = Covered(x[k]);
                    for (int dj = -1; dj <= 1 && even; dj++) for (int di = -1; di <= 1 && even; di++) { var c = x[k + dj * n + di]; even = Covered(c) && Near(c, x[k]); }
                    if (!even) continue;
                    bool any = false, found = false;
                    for (int dj = -reach; dj <= reach && !found; dj++)
                    {
                        int jj = j + dj; if (jj < 0 || jj >= n) continue;
                        for (int di = -reach; di <= reach; di++)
                        {
                            int ii = i + di; if (ii < 0 || ii >= n) continue;
                            var c = y[jj * n + ii];
                            if (!Covered(c)) continue;
                            any = true;
                            float dr = c.r - x[k].r, dg = c.g - x[k].g, db = c.b - x[k].b;
                            if (dr * dr + dg * dg + db * db <= tol2) { found = true; break; }
                        }
                    }
                    if (!any) continue;   // (past the other's outline: the shape, not the outfit)
                    total++;
                    if (!found) bad++;
                }
            seen += total;
            return total >= 8 ? bad / (float)total : 0f;   // (fewer: too small to tell an outfit by)
        }
        float r = Mathf.Max(One(a, b), One(b, a));
        looked = seen;
        return r;
    }

    /// <summary>
    /// Does the look's row of the palette (what PeopleLit draws far off) hold exactly what the full model wears: the
    /// jacket's and trousers' colours, sheen and pattern as their SnowyCloth materials have them, and each of the gear's
    /// swatches as its painted palette? And do the simplified body and a baked pose carry the clothes' rest pose (where
    /// the patterns are drawn)? Null if so, else what differs.
    /// </summary>
    static string RowDiff(Person p)
    {
        var diff = new StringBuilder();
        bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
        Color Lin(Color c) => lin ? c.linear : c;
        bool Same(Color a, Color b, float t) => Mathf.Abs(a.r - b.r) <= t && Mathf.Abs(a.g - b.g) <= t && Mathf.Abs(a.b - b.b) <= t;
        int row = PeopleKit.Row(p.Look);
        var px = PeopleKit.Atlas.GetPixels(0, row, PeopleKit.Slots, 1);
        void Cloth(Material m, int main, int second, int kind, string what)
        {
            if (m == null || !m.HasProperty("_Pattern")) { diff.Append($"{what}: no cloth material; "); return; }
            if (!Same(px[main], Lin(m.GetColor("_BaseColor")), 0.003f)) diff.Append($"{what} colour {px[main]} not {Lin(m.GetColor("_BaseColor"))}; ");
            if (Mathf.Abs(px[main].a - m.GetFloat("_Smoothness")) > 0.003f) diff.Append($"{what} sheen; ");
            float pattern = m.GetFloat("_Pattern");
            if (Mathf.Abs(px[kind].r - pattern) > 0.01f) diff.Append($"{what} pattern {px[kind].r:0.##} not {pattern:0.##}; ");
            if (pattern > 0.5f)
            {
                if (!Same(px[second], Lin(m.GetColor("_Color2")), 0.003f)) diff.Append($"{what} second colour; ");
                if (Mathf.Abs(px[kind].g - m.GetFloat("_PatternScale")) > 0.01f) diff.Append($"{what} pattern's scale {px[kind].g:0.###} not {m.GetFloat("_PatternScale"):0.###}; ");
                if (Mathf.Abs(px[kind].b - m.GetFloat("_PatternAt")) > 0.002f) diff.Append($"{what} colour block's height {px[kind].b:0.###} not {m.GetFloat("_PatternAt"):0.###}; ");
            }
        }
        Cloth(p.BodySkin != null ? p.BodySkin.sharedMaterial : null, PeopleKit.SJacket, PeopleKit.SJacketB, PeopleKit.SJacketP, "jacket");
        Cloth(p.LegsSkin != null ? p.LegsSkin.sharedMaterial : null, PeopleKit.SPants, PeopleKit.SPantsB, PeopleKit.SPantsP, "trousers");
        // the gear: every swatch the distance keeps, against the full model's own painted palette
        var gm = p.GearSkin != null ? p.GearSkin.sharedMaterial : null;
        var pal = gm != null ? gm.mainTexture as Texture2D : null;
        if (pal == null) diff.Append("no gear palette; ");
        else
        {
            var gp = pal.GetPixels();
            int[] cells = { PeopleKit.CSkin, PeopleKit.CHelmet, PeopleKit.CVent, PeopleKit.CLens, PeopleKit.CFrame, PeopleKit.CStrap, PeopleKit.CGlove, PeopleKit.CCuff,
                            PeopleKit.CBoot, PeopleKit.CPack, PeopleKit.CPackSide, PeopleKit.CPackAccent, PeopleKit.CHair, PeopleKit.CPompom, PeopleKit.CGaiter,
                            PeopleKit.CGaiterBand, PeopleKit.CLip };
            foreach (int c in cells)
            {
                int i = PeopleKit.PalettePixel(c, pal.width, pal.height), s = PeopleKit.SlotOfCell(c);
                if (i < 0 || s < 0) { diff.Append($"swatch {c} not kept far off; "); continue; }
                if (!Same(px[s], Lin(gp[i]), 0.003f) || Mathf.Abs(px[s].a - gp[i].a) > 0.003f) diff.Append($"swatch {c} (slot {s}) {px[s]} not {Lin(gp[i])}; ");
            }
        }
        // the rest pose on the distance meshes
        var simple = PeopleLod.Skinned(p.Look);
        var r2 = new List<Vector3>(); simple.GetUVs(2, r2);
        int clothed = 0; foreach (var v in r2) if (v.sqrMagnitude > 1e-6f) clothed++;
        if (r2.Count != simple.vertexCount || clothed < simple.vertexCount / 10) diff.Append($"the simplified body has no rest pose ({clothed} of {simple.vertexCount}); ");
        var baked = PeopleLod.Bake(p, Matrix4x4.identity, 0.16f, "LodLooksRest");
        var b2 = new List<Vector3>(); baked.GetUVs(2, b2);
        clothed = 0; foreach (var v in b2) if (v.sqrMagnitude > 1e-6f) clothed++;
        if (b2.Count != baked.vertexCount || clothed < baked.vertexCount / 10) diff.Append($"a baked pose has no rest pose ({clothed} of {baked.vertexCount}); ");
        Object.DestroyImmediate(baked);
        return diff.Length > 0 ? diff.ToString() : null;
    }

    /// <summary>The two pictures side by side (full model left), scaled up to 320 high.</summary>
    static void Picture(string file, Color32[] a, Color32[] b, int n)
    {
        int s = Mathf.Max(1, 320 / n), w = n * s;
        var tex = new Texture2D(w * 2 + 4, w, TextureFormat.RGB24, false);
        var px = new Color32[(w * 2 + 4) * w];
        for (int y = 0; y < w; y++)
            for (int x = 0; x < w * 2 + 4; x++)
            {
                Color32 c = new Color32(255, 255, 255, 255);
                if (x < w) c = a[(y / s) * n + x / s];
                else if (x >= w + 4) c = b[(y / s) * n + (x - w - 4) / s];
                if (!Covered(c)) c = new Color32(200, 206, 214, 255);
                px[y * (w * 2 + 4) + x] = c;
            }
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
