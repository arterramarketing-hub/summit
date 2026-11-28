using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What is built round a station while the rider is near (liftlook), with its colliders (SkiLift.Stream):
    ///   - At a top, its unload wing (LiftLine.UnloadWing): a slab of packed snow on a galvanised deck, level with the
    ///     station's, LiftLine.WingWidth wide beside it, from its back end to past its front, ending in a lip of snow.
    ///     Ridden as two roofs (Roofs: the level wing, with snow banked against its back where the snow comes near, and
    ///     the lip), solid as drawn under its snow; its legs stand on concrete footings on the snow, braced, padded at
    ///     the foot. Its outer fascia and the lip's face are painted in hazard stripes.
    ///   - Every station's legs: a concrete footing where each meets the snow and an orange pad round its foot.
    ///   - Beside a top, on the far side from its wing: orange safety netting along the edge of the cleared ground and a
    ///     slatted wind fence behind the station, both solid.
    /// One mesh a station, in submeshes by material (the snow, the galvanised steel, the concrete, the stripes, the
    /// netting, the palette for the pads, the planks), UVs in metres (Dress).
    /// </summary>
    public partial class SkiLift
    {
        const float SlabTop = -0.12f, SlabBottom = -0.46f;   // the wing's deck under its snow

        /// <summary>A box collider in a station's frame, marked as the station (ground: its top is ridden, Roofs).</summary>
        static GameObject StationBox(Transform t, string name, Vector3 at, Vector3 size, Quaternion rot, bool ground, string label = "Lift station")
        {
            var g = new GameObject(name);
            g.transform.SetParent(t, false);
            g.transform.localPosition = at; g.transform.localRotation = rot;
            g.AddComponent<BoxCollider>().size = size;
            var ob = g.AddComponent<Obstacle>(); ob.label = label; ob.roof = ground;
            return g;
        }

        /// <summary>The snow's height under a point of a station's frame, in that frame (its deck at 0).</summary>
        static float SnowY(Transform t, float x, float z)
        {
            var p = t.TransformPoint(new Vector3(x, 0f, z));
            return Slope.DrawnHeight(p.x, p.z) - t.position.y;
        }

        // ---------------------------------------------------------------- the unload wing (liftlook)

        /// <summary>The wing's snow height over the deck at z (its frame): level, then up the lip.</summary>
        static float WingTop(LiftLine.Wing w, float z) => z <= w.z1 ? 0f : LiftLine.LipRise * Mathf.Clamp01((z - w.z1) / LiftLine.LipLen);

        /// <summary>
        /// How far out from the wing's edge (x0 or x1, out = +1 toward its outer side, -1 toward the line) its side banks
        /// down to the snow at Roofs.ApronSlope before WingDepth under its top (0 if it does not: there the side hangs,
        /// snow-clad, to WingDepth, carried by the trestle). Inward it may reach no nearer the line than ChairReach.
        /// </summary>
        static float BankTo(Transform t, LiftLine.Wing w, float s, float out_, float z)
        {
            float top = WingTop(w, z), edge = out_ > 0f ? w.x1 : w.x0;
            float most = out_ > 0f ? LiftLine.WingDepth / Roofs.ApronSlope : w.x0 - LiftLine.ChairReach;
            for (float d = 0.25f; d <= most + 0.001f; d += 0.25f)
            {
                float y = top - Roofs.ApronSlope * d;
                if (y <= SnowY(t, s * (edge + out_ * d), z)) return d;
            }
            return 0f;
        }

        /// <summary>Is the wing's inner side open here (past the station's front, or behind its back)?</summary>
        static bool InnerOpen(float z) => z < -LiftLine.StationHalfLen || z > LiftLine.StationHalfLen;

        /// <summary>The trestle's bents: along the wing every 5 m or so, where the snow is far enough under its deck.</summary>
        static List<float> Bents(Transform t, LiftLine.Wing w, float s)
        {
            var at = new List<float>();
            float a = w.z0 + 1.5f, b = w.Lip - 0.8f;
            int n = Mathf.Max(2, Mathf.CeilToInt((b - a) / 5f) + 1);
            for (int i = 0; i < n; i++) at.Add(Mathf.Lerp(a, b, i / (n - 1f)));
            return at;
        }
        static float[] BentX(LiftLine.Wing w, float s) => new[] { s * (w.x0 + 0.8f), s * (w.x0 + LiftLine.WingWidth * 0.5f), s * (w.x1 - 0.8f) };
        const float PostW = 0.6f, BraceW = 0.3f;

        /// <summary>The wing's ground (two roofs, their sides banked where the snow is near) and its solids.</summary>
        void Wing(Live lv, Transform t, LiftLine.Wing w, int open)
        {
            float s = open, xa = s > 0f ? w.x0 : -w.x1, xb = s > 0f ? w.x1 : -w.x0;
            var none = new Vector4[0];
            var flat = new Roofs.Shape
            {
                gable = false, a = 0f, x0 = xa, x1 = xb, z0 = w.z0, z1 = w.z1, px0 = xa, px1 = xb, pz0 = w.z0, pz1 = w.z1,
                stacks = none, label = "Lift deck", points = 0, banked = true,
            };
            float gz = LiftLine.LipRise / LiftLine.LipLen;
            var lip = new Roofs.Shape
            {
                gable = false, a = -gz * w.z1, gz = gz, x0 = xa, x1 = xb, z0 = w.z1, z1 = w.Lip, px0 = xa, px1 = xb, pz0 = w.z1, pz1 = w.Lip,
                stacks = none, label = "Lift deck", points = 0, banked = true,
            };
            // its sides' banks (the shape's x1 is its +x side: the outer one where s > 0)
            foreach (var sh in new[] { flat, lip })
            {
                int n = Mathf.RoundToInt((sh.z1 - sh.z0) / Roofs.RampStep) + 1;
                var outer = new float[n]; var inner = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float z = sh.z0 + i * Roofs.RampStep;
                    outer[i] = BankTo(t, w, s, 1f, z);
                    inner[i] = InnerOpen(z) ? BankTo(t, w, s, -1f, z) : 0f;
                }
                if (s > 0f) { sh.apronOut = outer; sh.apronIn = inner; } else { sh.apronOut = inner; sh.apronIn = outer; }
            }
            lv.wingTop = Roofs.Add(flat, t, "LiftWing");
            lv.lipTop = Roofs.Add(lip, t, "LiftWingLip");
            Drifts(lv, lv.wingTop, false);
            // solid as drawn under its snow: the deck from Roofs.Margin under the snow down to WingDepth
            float cx = (xa + xb) * 0.5f, wide = xb - xa, deep = LiftLine.WingDepth - Roofs.Margin;
            StationBox(t, "Wing", new Vector3(cx, -(Roofs.Margin + LiftLine.WingDepth) * 0.5f, (w.z0 + w.z1) * 0.5f), new Vector3(wide, deep, w.z1 - w.z0), Quaternion.identity, true);
            float ang = Mathf.Atan(gz) * Mathf.Rad2Deg, len = Mathf.Sqrt(LiftLine.LipLen * LiftLine.LipLen + LiftLine.LipRise * LiftLine.LipRise);
            var rot = Quaternion.Euler(-ang, 0f, 0f);
            var mid = new Vector3(cx, LiftLine.LipRise * 0.5f, (w.z1 + w.Lip) * 0.5f);
            StationBox(t, "WingLip", mid + rot * new Vector3(0f, -(Roofs.Margin + LiftLine.WingDepth) * 0.5f, 0f), new Vector3(wide, deep, len), rot, true);
            // the trestle: its posts and their braces, solid as drawn
            foreach (float z in Bents(t, w, s))
            {
                var xs = BentX(w, s);
                var bot = new float[xs.Length];
                for (int i = 0; i < xs.Length; i++)
                {
                    float snow = SnowY(t, xs[i], z), top = WingTop(w, z) - LiftLine.WingDepth;
                    bot[i] = snow - 0.3f;
                    if (top - bot[i] < 0.8f) { bot[i] = float.NaN; continue; }
                    StationBox(t, "WingPost", new Vector3(xs[i], (top + bot[i]) * 0.5f, z), new Vector3(PostW, top - bot[i], PostW), Quaternion.identity, false);
                }
                for (int i = 0; i + 1 < xs.Length; i++)
                    foreach (var (a, b) in Braces(xs[i], bot[i], xs[i + 1], bot[i + 1], WingTop(w, z) - LiftLine.WingDepth))
                        Beam(t, new Vector3(a.x, a.y, z), new Vector3(b.x, b.y, z), BraceW);
            }
        }

        /// <summary>The X braces between two posts at x a and b (bottoms ya and yb: NaN, no post), from the deck down in
        /// panels of at most 6 m to the shorter post's foot: each panel's two diagonals (in the bent's plane, as x, y).</summary>
        static List<(Vector2, Vector2)> Braces(float xa, float ya, float xb, float yb, float top)
        {
            var list = new List<(Vector2, Vector2)>();
            if (float.IsNaN(ya) || float.IsNaN(yb)) return list;
            float low = Mathf.Max(ya, yb) + 0.4f;
            if (top - low < 1.2f) return list;
            int panels = Mathf.Max(1, Mathf.CeilToInt((top - low) / 6f));
            for (int k = 0; k < panels; k++)
            {
                float y0 = Mathf.Lerp(top, low, k / (float)panels), y1 = Mathf.Lerp(top, low, (k + 1f) / panels);
                list.Add((new Vector2(xa, y0), new Vector2(xb, y1)));
                list.Add((new Vector2(xb, y0), new Vector2(xa, y1)));
            }
            return list;
        }

        /// <summary>The station's dressing: its legs' footings and pads; at a top, its wing as drawn and the fences beside it.</summary>
        void Dressing(Transform t, LiftLine.Wing w, int open)
        {
            const int Galv = 1, Concrete = 2, Hazard = 3, Net = 4, Palette = 5, Planks = 6, Timber = 7;
            var d = new Dress(8);
            float W = LiftLine.StationHalfWid, L = LiftLine.StationHalfLen;
            // footings and pads under the station's own legs, where they meet the snow
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz++)
                {
                    float x = sx * (W - 0.5f), z = sz * (L - 0.5f), snow = SnowY(t, x, z);
                    if (snow < -14.2f || snow > -0.7f) continue;
                    Foot(d, x, z, snow, 0.5f, Concrete, Palette);
                }
            if (w != null)
            {
                float s = open, xo = s * w.x1, xi = s * w.x0;
                float xa = Mathf.Min(xo, xi), xb = Mathf.Max(xo, xi);
                float r = LiftLine.LipRise, zl = w.Lip, dep = LiftLine.WingDepth;
                SnowSkin(t, w, s);
                // the timber fascia under the cornice, down to the deck's underside, wherever a side hangs
                for (int j = 0; j + 1 < hangZ.Count; j++)
                {
                    float za = hangZ[j], zb = hangZ[j + 1];
                    if (zb > zl + 0.01f) break;
                    float ta = WingTop(w, za), tb = WingTop(w, zb);
                    if (hangOut[j] && hangOut[j + 1])
                    {
                        float x = s * (w.x1 + FasciaOut);
                        d.Quad(Timber, new Vector3(x, ta - dep, za), new Vector3(x, tb - dep, zb), new Vector3(x, tb - Cornice + 0.1f, zb), new Vector3(x, ta - Cornice + 0.1f, za), new Vector3(s, 0f, 0f), 2f);
                    }
                    if (hangIn[j] && hangIn[j + 1])
                    {
                        float x = s * (w.x0 - FasciaOut);
                        d.Quad(Timber, new Vector3(x, ta - dep, za), new Vector3(x, tb - dep, zb), new Vector3(x, tb - Cornice + 0.1f, zb), new Vector3(x, ta - Cornice + 0.1f, za), new Vector3(-s, 0f, 0f), 2f);
                    }
                }
                {
                    // (its end, under the lip's nose; and its back where it stands in the air behind)
                    float ex0 = s * (w.x0 - FasciaOut), ex1 = s * (w.x1 + FasciaOut);
                    d.Quad(Timber, new Vector3(ex0, r - dep, zl + FasciaOut), new Vector3(ex1, r - dep, zl + FasciaOut), new Vector3(ex1, r - Cornice, zl + FasciaOut), new Vector3(ex0, r - Cornice, zl + FasciaOut), Vector3.forward, 2f);
                    if (!w.access)
                        d.Quad(Timber, new Vector3(ex0, -dep, w.z0 - FasciaOut), new Vector3(ex1, -dep, w.z0 - FasciaOut), new Vector3(ex1, -Cornice, w.z0 - FasciaOut), new Vector3(ex0, -Cornice, w.z0 - FasciaOut), Vector3.back, 2f);
                }
                // under its snow: a timber deck WingDepth deep (its underside, out to where its snow-clad sides hang)
                float ua = s > 0f ? w.x0 - FasciaOut : -(w.x1 + FasciaOut), ub = s > 0f ? w.x1 + FasciaOut : -(w.x0 - FasciaOut);
                d.Quad(Timber, new Vector3(ua, -dep, w.z0), new Vector3(ub, -dep, w.z0), new Vector3(ub, r - dep, zl + 0.3f), new Vector3(ua, r - dep, zl + 0.3f), Vector3.down, 2f);
                // the lip marked: a striped board on a post at each of its corners, facing the way riders come
                foreach (float x in new[] { xi + s * 0.3f, xo - s * 0.3f })
                {
                    d.Box(Galv, new Vector3(x, r + 0.55f, zl - 0.25f), new Vector3(0.08f, 1.1f, 0.08f), 2f);
                    d.Box(Hazard, new Vector3(x, r + 0.95f, zl - 0.3f), new Vector3(0.6f, 0.34f, 0.04f), 0.5f);
                }
                // the trestle: heavy timber posts on concrete footings in bents across it, X-braced in each bent and
                // along its outer side, ledgers across at each panel
                List<float> bents = Bents(t, w, s);
                var xs = BentX(w, s);
                var prevBot = new float[xs.Length]; float prevZ = float.NaN;
                foreach (float z in bents)
                {
                    float top = WingTop(w, z) - dep;
                    var bot = new float[xs.Length];
                    for (int i = 0; i < xs.Length; i++)
                    {
                        float snow = SnowY(t, xs[i], z);
                        bot[i] = snow - 0.3f;
                        if (top - bot[i] < 0.8f) { bot[i] = float.NaN; continue; }
                        d.Box(Timber, new Vector3(xs[i], (top + bot[i]) * 0.5f, z), new Vector3(PostW, top - bot[i], PostW), 2f);
                        d.Box(Concrete, new Vector3(xs[i], snow - 0.05f, z), new Vector3(1.5f, 0.7f, 1.5f), 1f);
                    }
                    for (int i = 0; i + 1 < xs.Length; i++)
                    {
                        var br = Braces(xs[i], bot[i], xs[i + 1], bot[i + 1], top);
                        foreach (var (a, b) in br) d.Bar(Timber, new Vector3(a.x, a.y, z), new Vector3(b.x, b.y, z), BraceW, 2f);
                        for (int k = 1; k < br.Count; k += 2)   // (a ledger across under each panel)
                            d.Box(Timber, new Vector3((xs[i] + xs[i + 1]) * 0.5f, br[k].Item2.y, z), new Vector3(Mathf.Abs(xs[i + 1] - xs[i]), BraceW, BraceW), 2f);
                    }
                    // along the outer side, from the last bent
                    int o = xs.Length - 1;
                    if (!float.IsNaN(prevZ) && !float.IsNaN(bot[o]) && !float.IsNaN(prevBot[o]))
                        foreach (var (a, b) in Braces(prevZ, prevBot[o], z, bot[o], Mathf.Min(top, WingTop(w, prevZ) - dep)))
                            d.Bar(Timber, new Vector3(xs[o], a.y, a.x), new Vector3(xs[o], b.y, b.x), BraceW, 2f);
                    prevBot = bot; prevZ = z;
                }
                // beside it, the other side of the station: orange netting along the cleared ground's edge, and a slatted
                // wind fence behind the station (both solid)
                // (out past the unloaders' flat where it is on that side: LiftEnds keeps it on the run's side unless the
                // wing is there)
                float far = -s, out0 = w.runSide ? 2.5f : 10.5f;
                Fence(t, d, Net, far * (W + out0), -L + 4f, -L - 14f, 1.0f, 0.1f, 1f, "Safety net");
                Fence(t, d, Planks, far * (W + out0 + 4f), -L - 4f, -L - 16f, 1.5f, 0.2f, 2f, "Wind fence", true);
            }
            if (d.Empty) return;
            var g = new GameObject("LiftStationDressing");
            g.transform.SetParent(t, false);
            g.AddComponent<MeshFilter>().sharedMesh = d.Make("LiftStationDressing");
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { snowMat, galvMat, concreteMat, hazardMat, netMat, paint, deckMat, timberMat };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>
        /// The wing's snow, drawn with the terrain's own snow (so the run's corduroy and its light are on it: the lane is
        /// groomed, LiftLine.UnloadGroom): across it a slight crown; at its sides the snow rolls over the edge and banks
        /// down to the snow (feathered in) or hangs, snow-clad, WingDepth to the timber under it; at its back it eases down
        /// into the snow behind; at its end the lip rises and its nose rolls over. One grid of rows along it, world
        /// positioned, UVs as the terrain's (x and z over 9 m).
        /// </summary>
        void SnowSkin(Transform t, LiftLine.Wing w, float s)
        {
            const float Crown = 0.05f;
            int nIn = 7, nTop = 11, nOut = 10;
            int cols = nIn + nTop + nOut;
            var zs = new List<float>();
            // (behind it: eased down into the snow where the snow meets it; where it stands in the air behind, a face hanging
            // straight down from its back edge)
            if (w.access) for (float z = w.z0 - 4f; z < w.z0 - 0.01f; z += 1f) zs.Add(z);
            else zs.Add(w.z0 - 0.15f);
            for (float z = w.z0; z < w.z1 - 0.01f; z += 1f) zs.Add(z);
            for (float z = w.z1; z <= w.Lip + 0.01f; z += 0.5f) zs.Add(z);
            zs.Add(w.Lip + 0.18f); zs.Add(w.Lip + 0.32f); zs.Add(w.Lip + CurlOut);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
            float reachOut = LiftLine.WingDepth / Roofs.ApronSlope, reachIn = w.x0 - LiftLine.ChairReach;
            hangOut.Clear(); hangIn.Clear(); hangZ.Clear();
            foreach (float z in zs)
            {
                bool back = z < w.z0 - 0.01f, nose = z > w.Lip + 0.01f;
                // (each side: banked down to the snow where it reaches it, else a cornice over the fascia)
                float bankOut = back || nose ? 0f : BankTo(t, w, s, 1f, z), bankIn = back || nose || !InnerOpen(z) ? 0f : BankTo(t, w, s, -1f, z);
                bool hOut = !back && !nose && bankOut <= 0f, hIn = !back && !nose && InnerOpen(z) && bankIn <= 0f;
                hangOut.Add(hOut); hangIn.Add(hIn); hangZ.Add(z);
                float top = back && !w.access ? -Cornice : back ? -Roofs.RampSlope * (w.z0 - z) : nose ? LiftLine.LipRise - (z > w.Lip + 0.3f ? (z > w.Lip + 0.35f ? Cornice + 0.08f : 0.34f) : 0.1f) : WingTop(w, z);
                bool hangNose = false;
                // (along a row: its inner side's bank or hang, across its top, its outer side's)
                for (int c = 0; c < cols; c++)
                {
                    float x, y;
                    float lump = 0f, feather = -1f;   // (how much the snow's lumps move this point; past 0, how far into a bank's toe)
                    if (c < nIn)
                    {
                        // inner side: only where it is open, else folded onto the inner edge
                        float k = (nIn - 1 - c) / (float)(nIn - 1);   // 1 at the far end of the bank, 0 at the edge
                        bool openHere = InnerOpen(z) && !back;
                        if (openHere && hIn) { var cu = Curl(k); x = w.x0 - cu.x; y = top + cu.y; lump = k; }
                        else
                        {
                            float dd = openHere ? k * Mathf.Max(bankIn, 0.01f) : 0f;
                            x = w.x0 - dd;
                            y = Side(top, dd, k, openHere ? reachIn : 0f, true);
                            if (openHere) { lump = Mathf.Sin(k * Mathf.PI); feather = k; }
                        }
                    }
                    else if (c < nIn + nTop)
                    {
                        float k = (c - nIn) / (float)(nTop - 1);
                        x = Mathf.Lerp(w.x0, w.x1, k);
                        y = top + (back || hangNose ? 0f : Crown * (1f - (2f * k - 1f) * (2f * k - 1f)) - Crown * 0.5f);
                    }
                    else
                    {
                        float k = (c - nIn - nTop + 1) / (float)nOut;   // 0+ at the edge, 1 at the bank's foot
                        if (hOut) { var cu = Curl(k); x = w.x1 + cu.x; y = top + cu.y; lump = k; }
                        else
                        {
                            float dd = back ? k * 2f : nose ? k * CurlOut : k * (bankOut + 1.2f);
                            x = w.x1 + dd;
                            y = back ? top : nose ? top - Cornice * k : Side(top, dd, k, reachOut, false);
                            if (!back && !nose) { lump = Mathf.Sin(k * Mathf.PI); feather = k; }
                        }
                    }
                    // (the snow's lumps: wind-shaped, a metre or two across, on its banks and cornices, never on its top)
                    if (lump > 0f)
                    {
                        var wp = t.TransformPoint(new Vector3(s * x, 0f, z));
                        float n1 = Mathf.PerlinNoise(wp.x * 0.31f + 17f, wp.z * 0.31f) - 0.5f, n2 = Mathf.PerlinNoise(wp.x * 0.9f, wp.z * 0.9f + 5f) - 0.5f;
                        y += (n1 * 0.5f + n2 * 0.18f) * lump;
                        x += (n1 * 0.45f) * lump;
                    }
                    float snow = SnowY(t, s * x, z);
                    // (a bank's toe feathered into the snow over its last third)
                    if (feather > 0.62f) y = Mathf.Lerp(y, snow + 0.03f, Mathf.SmoothStep(0f, 1f, (feather - 0.62f) / 0.38f));
                    // (into the snow where it meets it: lying on it just over its surface, feathered)
                    if (y < snow + 0.03f) y = snow + 0.03f;
                    if (hangNose && c >= nIn && c < nIn + nTop) y = Mathf.Max(snow + 0.03f, WingTop(w, w.Lip) - LiftLine.WingDepth);
                    var p = t.TransformPoint(new Vector3(s * x, y, z));
                    v.Add(p - t.position);
                    uv.Add(new Vector2(p.x / 9f, p.z / 9f));
                    col.Add(Color.white);
                }
            }
            int rows = zs.Count;
            for (int j = 0; j + 1 < rows; j++)
                for (int c = 0; c + 1 < cols; c++)
                {
                    int a = j * cols + c, b = a + 1, e = a + cols, f = e + 1;
                    // (facing up and out whichever side the wing is on: the frame's x is mirrored for s < 0)
                    if (s > 0f) { tri.Add(a); tri.Add(e); tri.Add(b); tri.Add(b); tri.Add(e); tri.Add(f); }
                    else { tri.Add(a); tri.Add(b); tri.Add(e); tri.Add(b); tri.Add(f); tri.Add(e); }
                }
            var mesh = new Mesh { name = "LiftWingSnow" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetColors(col); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var g = new GameObject("LiftWingSnow");
            g.transform.SetParent(t, false);
            g.transform.SetPositionAndRotation(t.position, Quaternion.identity);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TerrainSnow();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>A wing's side at dd out from its edge (k of the way to `reach`): the snow rolls over the edge and down
        /// at ApronSlope, to WingDepth at most (then it hangs straight down to it, snow-clad).</summary>
        static float Side(float top, float dd, float k, float reach, bool inner)
        {
            if (reach <= 0f) return top;
            // (a rounded roll for the first metre, then the bank's straight slope)
            float roll = dd < 1f ? Roofs.ApronSlope * dd * dd / 2f : Roofs.ApronSlope * (dd - 0.5f);
            return Mathf.Max(top - roll, top - LiftLine.WingDepth);
        }

        /// <summary>A cornice: where a side hangs (the snow far under it), the snow rolls over the edge and stops Cornice
        /// under the top, a little out past the timber fascia under it. k: 0 at the edge, 1 at the curl's end.</summary>
        static Vector2 Curl(float k)
        {
            float a = k * Mathf.PI * 0.5f;
            return new Vector2(CurlOut * Mathf.Sin(a), -Cornice * (1f - Mathf.Cos(a)) - 0.08f * k);
        }
        const float Cornice = 0.55f, CurlOut = 0.4f, FasciaOut = 0.25f;

        // (which rows of the last wing's snow hang at either side: the timber fascia goes under those, Dressing)
        readonly List<bool> hangOut = new List<bool>(), hangIn = new List<bool>(); readonly List<float> hangZ = new List<float>();

        Material terrainSnow;
        /// <summary>The terrain's own near snow material (SlopeStreamer), or the lift's groomed snow if there is none.</summary>
        Material TerrainSnow()
        {
            if (terrainSnow == null)
            {
                var ss = Object.FindFirstObjectByType<SlopeStreamer>();
                if (ss != null) terrainSnow = ss.NearSnow;
                if (terrainSnow == null) terrainSnow = snowMat;
            }
            return terrainSnow;
        }

        /// <summary>A concrete footing where a leg (w square) meets the snow, and an orange pad round its foot.</summary>
        static void Foot(Dress d, float x, float z, float snow, float w, int concrete, int palette)
        {
            d.Box(concrete, new Vector3(x, snow - 0.05f, z), new Vector3(w + 0.55f, 0.5f, w + 0.55f), 1f);
            d.Box(palette, new Vector3(x, snow + 1.0f, z), new Vector3(w + 0.22f, 1.6f, w + 0.22f), -3f);   // (the palette's pad)
            d.Box(palette, new Vector3(x, snow + 1.0f, z), new Vector3(w + 0.26f, 0.12f, w + 0.26f), -1f);   // (its strap)
        }

        /// <summary>A fence on the snow along the station's frame at x, from z a to b: posts every 3 m, panels between
        /// them h high from `lift` over the snow, each panel solid. A panel is left out where the ground is not free: the
        /// run, a lane, kept ground, another lift, a hamlet.</summary>
        void Fence(Transform t, Dress d, int sub, float x, float a, float b, float h, float lift, float tile, string label, bool slats = false)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(b - a) / 3f));
            for (int i = 0; i <= n; i++)
            {
                float z = Mathf.Lerp(a, b, i / (float)n), snow = SnowY(t, x, z);
                d.Box(1, new Vector3(x, snow + (h + lift + 0.4f) * 0.5f - 0.2f, z), new Vector3(0.09f, h + lift + 0.4f, 0.09f), 2f);   // (a galvanised post)
                if (i == 0) continue;
                float zp = Mathf.Lerp(a, b, (i - 1) / (float)n), sp = SnowY(t, x, zp);
                if (!FenceGround(t, x, z) || !FenceGround(t, x, zp)) continue;
                Vector3 p0 = new Vector3(x, sp + lift, zp), p1 = new Vector3(x, snow + lift, z);
                if (slats)
                {
                    // (a wind fence: upright boards with gaps between, the wind let through)
                    int k = Mathf.Max(2, Mathf.RoundToInt(Mathf.Abs(z - zp) / 0.24f));
                    for (int j = 0; j < k; j++)
                    {
                        float f = (j + 0.5f) / k;
                        var q = Vector3.Lerp(p0, p1, f);
                        d.Box(sub, q + Vector3.up * (h * 0.5f), new Vector3(0.03f, h, 0.13f), tile);
                    }
                    d.Box(sub, (p0 + p1) * 0.5f + Vector3.up * (h * 0.8f), new Vector3(0.05f, 0.09f, Mathf.Abs(z - zp)), tile);   // (a rail behind them)
                }
                else
                {
                    d.Quad(sub, p0, p1, p1 + Vector3.up * h, p0 + Vector3.up * h, Vector3.right, tile);
                    d.Quad(sub, p0, p1, p1 + Vector3.up * h, p0 + Vector3.up * h, Vector3.left, tile);
                }
                var c = (p0 + p1) * 0.5f + Vector3.up * (h * 0.5f);
                var along = p1 - p0;
                StationBox(t, label, c, new Vector3(0.12f, h, along.magnitude), Quaternion.LookRotation(along.normalized, Vector3.up), false, label);
            }
        }

        static bool FenceGround(Transform t, float x, float z)
        {
            var p = t.TransformPoint(new Vector3(x, 0f, z));
            if (Mathf.Abs(Slope.Across(p.x, p.z)) < Slope.Half(p.z) + 2.5f) return false;
            if (LiftLine.InUnload(p.x, p.z, 0.5f) || LiftLine.HillOrLane(p.x, p.z, 1f) || Course.Keep(p.x, p.z, 1f)) return false;
            if (Hamlets.Taken(p.x, p.z) || Slope.OnPad(p.x, p.z)) return false;
            return true;
        }

        /// <summary>
        /// A mesh in submeshes by material, in a station's frame. UVs: metres over `tile` (box-projected: a level face by x
        /// and z, an upright one by its width and its height), or, for a negative tile, a colour of the palette (cell
        /// -tile... with -1 the dark strap and -3 the pad).
        /// </summary>
        class Dress
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] tri;
            public Dress(int subs) { tri = new List<int>[subs]; for (int i = 0; i < subs; i++) tri[i] = new List<int>(); }
            public bool Empty => v.Count == 0;

            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float tile)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a), outward) < 0f) { var t = b; b = d; d = t; }
                int n = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                if (tile < 0f)
                {
                    int cell = Mathf.RoundToInt(-tile);
                    var p = new Vector2((cell % 8 + 0.5f) / 8f, 1f - (cell / 8 + 0.5f) / 8f);
                    uv.Add(p); uv.Add(p); uv.Add(p); uv.Add(p);
                }
                else
                {
                    var nn = Vector3.Cross(b - a, c - a);
                    float ax = Mathf.Abs(nn.x), ay = Mathf.Abs(nn.y), az = Mathf.Abs(nn.z);
                    foreach (var q in new[] { a, b, c, d })
                        uv.Add(ay >= ax && ay >= az ? new Vector2(q.x, q.z) / tile : ax >= az ? new Vector2(q.z, q.y) / tile : new Vector2(q.x, q.y) / tile);
                }
                var tr = tri[sub];
                tr.Add(n); tr.Add(n + 1); tr.Add(n + 2); tr.Add(n); tr.Add(n + 2); tr.Add(n + 3);
            }

            /// <summary>An upright box (its frame's axes), centre and size.</summary>
            public void Box(int sub, Vector3 c, Vector3 size, float tile)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(sub, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up, tile);
                Quad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down, tile);
                Quad(sub, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right, tile);
                Quad(sub, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), Vector3.left, tile);
                Quad(sub, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward, tile);
                Quad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), Vector3.back, tile);
            }

            /// <summary>A bar from a to b, w square.</summary>
            public void Bar(int sub, Vector3 a, Vector3 b, float w, float tile)
            {
                Vector3 dir = (b - a).normalized;
                Vector3 s = Vector3.Cross(Vector3.up, dir); if (s.sqrMagnitude < 1e-4f) s = Vector3.right;
                s = s.normalized * (w * 0.5f);
                Vector3 u = Vector3.Cross(dir, s).normalized * (w * 0.5f);
                Quad(sub, a - s + u, a + s + u, b + s + u, b - s + u, u, tile);
                Quad(sub, a - s - u, a + s - u, b + s - u, b - s - u, -u, tile);
                Quad(sub, a + s - u, a + s + u, b + s + u, b + s - u, s, tile);
                Quad(sub, a - s - u, a - s + u, b - s + u, b - s - u, -s, tile);
            }

            public Mesh Make(string name)
            {
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetUVs(0, uv);
                mesh.subMeshCount = tri.Length;
                for (int i = 0; i < tri.Length; i++) mesh.SetTriangles(tri[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
