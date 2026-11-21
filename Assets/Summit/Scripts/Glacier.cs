using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Lays out the glacier's crevasses (Course.Crevasses) the way real ones lie (crevasses.md): fields of transverse
    /// cracks where the run steepens, chevrons along the piste's edges, a bergschrund at the foot of the summit face,
    /// and now and then a fan of splaying cracks where the run flattens. Only on the glacier (Course.GlacierTop to
    /// GlacierBottom, the piste and its margins) and never on anything else built there: the start pad and the camp,
    /// the park, kickers, jump cliffs, the deadly cliff and its terrace, a ridge, a hamlet. (Not the lifts: they are
    /// laid round the crevasses, through Course.Keep.)
    ///
    /// Fair by construction: a crack no wider than 2 x Course.CarryHalf is carried across at a normal riding speed (the
    /// board flies on over an opening, see Rider), so every crack whose wide part crosses the piste either leaves a
    /// lane of 6 m or more past it, or gets a snow bridge on the piste.
    /// </summary>
    public static class Glacier
    {
        public static bool On = true;
        /// <summary>For the test tools: why candidate cracks were turned down, and what was laid, on the last layout.</summary>
        public static readonly Dictionary<string, int> Rejected = new Dictionary<string, int>();
        public static string Spots = "";
        static string why;
        const float Lane = 6f;          // the narrowest way past a wide crack that counts as a lane
        const float Gap = 3f;           // the least snow between two cracks' lips

        public static void Build(List<Course.Crevasse> list)
        {
            Rejected.Clear(); Spots = "";
            if (!On) return;
            Bergschrund(list);
            Fields(list);
            Chevrons(list);
            Splays(list);
        }

        // ---------------------------------------------------------------- the pieces

        static int salt;
        static float H(int a, int b) => Slope.Hash01(a, b, 600 + salt);

        /// <summary>How much the run steepens past z (the fall line's drop per metre after it, less before it).</summary>
        static float Steepening(float z, float span = 18f)
        {
            float before = (Slope.FallLine(z - span) - Slope.FallLine(z)) / span;
            float after = (Slope.FallLine(z) - Slope.FallLine(z + span)) / span;
            return after - before;
        }

        static void Bergschrund(List<Course.Crevasse> list)
        {
            salt = 1;
            if (H(0, 0) > 0.9f) return;   // (now and then there is none)
            // across the run at the foot of the summit face, where the snow pulls away from what stays frozen to the
            // mountain's top: long, wide, deep, bowed down the hill, with snow bridges on the piste
            float len0 = 2f * (Course.GlacierHalf - 3f);
            for (float len = len0; len >= 28f; len -= 12f)
                for (float z = Course.GlacierTop + 6f; z < Course.GlacierTop + 32f; z += 2f)
                {
                    float c = Slope.Centre(z) + (H(1, 0) - 0.5f) * (len0 - len);
                    var b = Make(Course.CrevasseKind.Bergschrund, c, z, 0.06f * (H(2, 0) - 0.5f), len,
                                 1.8f + 0.8f * H(3, 0), 30f, 2.5f + 2.5f * H(4, 0), H(5, 0) * 40f);
                    // one bridge on the piste for certain, a second as often as not
                    float mid = b.Along(Slope.Centre(z), z);
                    float s1 = 5f + 3f * H(6, 0), u1 = Mathf.Clamp(mid + (H(7, 0) - 0.5f) * 24f, 8f, b.len - 8f - s1);
                    b.b0 = u1; b.b1 = u1 + s1;
                    if (H(8, 0) < 0.5f)
                    {
                        float s2 = 4f + 3f * H(9, 0);
                        float u2 = u1 < b.len * 0.5f ? Mathf.Min(b.len - 8f - s2, u1 + s1 + 10f + 10f * H(10, 0)) : Mathf.Max(8f, u1 - 10f - 10f * H(10, 0) - s2);
                        b.c0 = u2; b.c1 = u2 + s2;
                    }
                    if (Fits(list, b)) { list.Add(b); return; }
                }
        }

        static void Fields(List<Course.Crevasse> list)
        {
            salt = 2;
            // the run is busy (the park, a ridge's crest, jump cliffs, kickers, hamlets): the glacier shows in the open
            // stretches between them. A field in each open stretch, where the run steepens most over a convex break
            // in it; a long stretch can hold two, well apart. As many as the mountain has room for, never a fixed count
            var stretches = new List<Vector2>();
            float start = float.NaN;
            for (float z = Course.GlacierTop + 30f; z <= Course.GlacierBottom; z += 2f)
            {
                bool ok = Clear(Slope.Centre(z), z, false) && z + 2f <= Course.GlacierBottom;
                if (ok && float.IsNaN(start)) start = z;
                if (!ok && !float.IsNaN(start)) { if (z - start >= 30f) stretches.Add(new Vector2(start, z)); start = float.NaN; }
            }
            int field = 0;
            foreach (var st in stretches)
            {
                Spots += $" [{st.x:0}-{st.y:0}]";
                var picks = new List<float>();
                for (int round = 0; round < 2; round++)
                {
                    float best = float.MinValue, zb = 0f;
                    for (float z = st.x + 10f; z <= st.y - 10f; z += 2f)
                    {
                        bool far = true;
                        foreach (float p in picks) if (Mathf.Abs(p - z) < 85f) far = false;
                        if (!far) continue;
                        float k = Steepening(z);
                        if (k > best) { best = k; zb = z; }
                    }
                    if (best == float.MinValue || (round > 0 && best < 0.03f)) break;
                    picks.Add(zb);
                }
                foreach (float zf in picks)
                {
                    Spots += $" {zf:0}:{Steepening(zf):0.000}";
                    if (Field(list, field, zf, Mathf.Clamp01(Steepening(zf) / 0.12f), st.x, st.y)) field++;
                }
            }
        }

        /// <summary>A field of transverse cracks round zf; k (0..1) how sharply the run bends there. True if any were laid.</summary>
        static bool Field(List<Course.Crevasse> list, int field, float zf, float k, float za, float zb)
        {
            int start = list.Count;
            float extent = 22f + 40f * k + 14f * H(field, 2);
            float skew = (H(field, 3) - 0.5f) * 0.2f;          // the whole field leans a little one way
            float z = Mathf.Max(za + 3f, zf - extent * 0.35f), end = Mathf.Min(zb - 3f, zf + extent * 0.65f);
            for (int i = 0; z < end; i++)
            {
                int key = field * 64 + i;
                float hl = H(key, 4);
                float len = 14f + 46f * hl * Mathf.Sqrt(hl);         // mostly 15-35 m, now and then most of the piste
                float room = Course.GlacierHalf - 2f - len * 0.5f;
                float off = room > 0f ? (H(key, 5) - 0.5f) * 2f * Mathf.Min(room, Slope.PisteHalf) : 0f;
                float ang = skew + (H(key, 6) - 0.5f) * 0.38f;    // within about 11 degrees of straight across
                bool wide = H(key, 7) < 0.18f + 0.22f * k;
                float half = wide ? 1.6f + 1.0f * H(key, 8) : 0.35f + 1.0f * H(key, 8);
                var c = Make(Course.CrevasseKind.Transverse, Slope.Centre(z) + off, z, ang, len, half,
                             18f + 12f * H(key, 9), (0.8f + 2.5f * H(key, 10)) * len / 40f, H(key, 11) * 40f);
                c.field = field;
                // snow bridges: a span over part of it, or now and then all of it (a hidden crack: only its sag shows)
                float hb = H(key, 12);
                float taper = Mathf.Min(6f, len * 0.3f);
                if (hb < 0.12f && len > 10f) { c.b0 = 1f; c.b1 = len - 1f; }
                else if (hb < 0.45f && len > 2f * taper + 3f)
                {
                    float span = Mathf.Min(2.5f + 3.5f * H(key, 13), len - 2f * taper);
                    float u = taper + (len - 2f * taper - span) * H(key, 14);
                    c.b0 = u; c.b1 = u + span;
                }
                if (!Fits(list, c))
                {
                    // shorter, and toward the middle; then shorter still, off to one side
                    c = Make(c.kind, Slope.Centre(z) + off * 0.5f, z, ang, len * 0.6f, half, c.depth, c.bow * 0.6f, c.phase);
                    c.field = field;
                    if (!Fits(list, c))
                    {
                        float side = H(key, 16) < 0.5f ? -1f : 1f;
                        c = Make(c.kind, Slope.Centre(z) + side * (4f + 8f * H(key, 17)), z, ang, Mathf.Max(10f, len * 0.4f), half, c.depth, c.bow * 0.4f, c.phase);
                        c.field = field;
                        if (!Fits(list, c)) { z += 5f + 9f * H(key, 15); continue; }
                    }
                }
                Fair(ref c);
                list.Add(c);
                z += 5f + 9f * H(key, 15);   // 5 to 14 m on to the next: never evenly spaced
            }
            return list.Count > start;
        }

        static void Chevrons(List<Course.Crevasse> list)
        {
            salt = 3;
            for (int side = -1; side <= 1; side += 2)
                for (float zs = Course.GlacierTop + 30f; zs < Course.GlacierBottom - 40f; zs += 55f)
                {
                    int key = (int)zs * 2 + (side > 0 ? 1 : 0);
                    if (H(key, 0) > 0.6f) continue;
                    int n = 3 + Mathf.FloorToInt(4f * H(key, 1));
                    float a = 0.70f + 0.25f * H(key, 2);           // 40-54 degrees off the fall line
                    float z = zs + 10f * H(key, 3);
                    for (int i = 0; i < n; i++, z += 5f + 7f * H(key * 8 + i, 4))
                    {
                        int k = key * 8 + i;
                        float len = 7f + 9f * H(k, 5);
                        // from out in the margin, in toward the middle and up the hill
                        float sx = -side * Mathf.Sin(a), sz = -Mathf.Cos(a);
                        float ox = Slope.Centre(z) + side * (Slope.PisteHalf + 7f + 3f * H(k, 6));
                        float cx = ox + sx * len * 0.5f, cz = z + sz * len * 0.5f;
                        var c = Make(Course.CrevasseKind.Chevron, cx, cz, Mathf.Atan2(sz, sx), len, 0.25f + 0.45f * H(k, 7),
                                     10f + 8f * H(k, 8), 0.3f * (H(k, 9) - 0.5f), H(k, 10) * 40f, true);
                        if (Fits(list, c)) list.Add(c);
                    }
                }
        }

        static void Splays(List<Course.Crevasse> list)
        {
            salt = 4;
            if (H(0, 0) > 0.6f) return;
            // where the run flattens, low on the glacier: a fan of cracks down the fall line
            float best = 0f, zb = 0f;
            for (float z = Course.GlacierBottom - 600f; z < Course.GlacierBottom - 40f; z += 4f)
            {
                if (!Clear(Slope.Centre(z), z, false) || !Clear(Slope.Centre(z + 30f), z + 30f, false)) continue;
                float s = -Steepening(z, 24f);
                if (s > best) { best = s; zb = z; }
            }
            if (best < 0.01f) return;
            int n = 2 + Mathf.FloorToInt(3f * H(1, 0));
            for (int i = 0; i < n; i++)
            {
                float f = n == 1 ? 0f : i / (n - 1f) - 0.5f;               // -0.5 .. 0.5 across the fan
                float off = f * 2f * 13f + (H(i, 2) - 0.5f) * 3f;
                float spread = f * (0.25f + 0.3f * H(2, 1));                // fanning out down the hill
                float len = 15f + 17f * H(i, 3);
                var c = Make(Course.CrevasseKind.Splay, Slope.Centre(zb) + off, zb + len * 0.5f, Mathf.PI * 0.5f - spread, len,
                             0.35f + 0.55f * H(i, 4), 12f + 10f * H(i, 5), 0.5f * (H(i, 6) - 0.5f), H(i, 7) * 40f);
                if (Fits(list, c)) list.Add(c);
            }
        }

        // ---------------------------------------------------------------- building and checking one

        /// <summary>
        /// A crack centred on (cx, cz), its line at `ang` radians from straight across the slope (+x), turned toward +z
        /// (down the hill) for a positive angle; or, if `absolute`, `ang` is the direction's own angle from +x.
        /// </summary>
        static Course.Crevasse Make(Course.CrevasseKind kind, float cx, float cz, float ang, float len, float half, float depth,
                                    float bow, float phase, bool absolute = false)
        {
            float dx = Mathf.Cos(ang), dz = Mathf.Sin(ang);
            var c = new Course.Crevasse
            {
                kind = kind, len = len, half = half, depth = depth, bow = bow, phase = phase,
                dx = dx, dz = dz, ax = cx - dx * len * 0.5f, az = cz - dz * len * 0.5f, field = -1,
            };
            // bounds: its line, bowed either way, and its lips (the dark band, the apron) a few metres out
            float pad = Mathf.Abs(bow) + half + 4f;
            float bx = c.ax + dx * len, bz = c.az + dz * len;
            c.x0 = Mathf.Min(c.ax, bx) - pad; c.x1 = Mathf.Max(c.ax, bx) + pad;
            c.z0 = Mathf.Min(c.az, bz) - pad; c.z1 = Mathf.Max(c.az, bz) + pad;
            return c;
        }

        /// <summary>Is every part of it, lips and all, on open glacier and clear of every crack already laid?</summary>
        static bool Fits(List<Course.Crevasse> list, Course.Crevasse c)
        {
            for (float u = -2f; u <= c.len + 2f; u += 2f)
            {
                Course.Opening(c, Mathf.Clamp(u, 0f, c.len), out float mid);
                for (int s = -1; s <= 1; s++)
                {
                    var q = c.Point(u, mid + s * (c.half + 2f));
                    if (!Clear(q.x, q.y, c.kind == Course.CrevasseKind.Chevron)) { Reject(c.kind + " " + why); return false; }
                    foreach (var e in list) if (Within(e, q.x, q.y, c.half + Gap)) { Reject(c.kind + " overlap"); return false; }
                }
            }
            return true;
        }

        /// <summary>For the test tools: the stretches of the run (down its middle, z from..to) open for crevasses.</summary>
        public static string FreeStretches(float z0, float z1)
        {
            var sb = new System.Text.StringBuilder();
            float start = float.NaN;
            for (float z = z0; z <= z1; z += 2f)
            {
                bool ok = Clear(Slope.Centre(z), z, false);
                if (ok && float.IsNaN(start)) start = z;
                if ((!ok || z + 2f > z1) && !float.IsNaN(start)) { sb.Append($" {start:0}-{z:0}({why})"); start = float.NaN; }
            }
            return sb.ToString();
        }

        static void Reject(string r) { Rejected.TryGetValue(r, out int n); Rejected[r] = n + 1; }

        static bool Within(Course.Crevasse e, float x, float z, float margin)
        {
            if (x < e.x0 - margin || x > e.x1 + margin || z < e.z0 - margin || z > e.z1 + margin) return false;
            float u = e.Along(x, z);
            if (u < -margin || u > e.len + margin) return false;
            Course.Opening(e, Mathf.Clamp(u, 0f, e.len), out float mid);
            return Mathf.Abs(e.Across(x, z) - mid) < e.half + margin;
        }

        /// <summary>Open glacier at (x, z): nothing else built there. Chevrons may reach a little further out, into the margin.</summary>
        static bool Clear(float x, float z, bool margin)
        {
            why = "zone";
            if (z < Course.GlacierTop || z > Course.GlacierBottom) return false;
            if (Mathf.Abs(x - Slope.Centre(z)) > Course.GlacierHalf + (margin ? 4f : 0f)) return false;
            why = "pad"; if (Slope.OnPad(x, z)) return false;                                      // the start, the camp
            why = "park"; if (Course.At(z) == Course.Section.Park || Course.At(z + 25f) == Course.Section.Park || Course.At(z - 12f) == Course.Section.Park) return false;
            if (Course.PipeWeight(z) > 0f) return false;
            why = "ridge"; if (Ridge.Has(z) || Ridge.Keep(x, z, 4f)) return false;
            why = "cliff"; if (Cliffs.Keep(x, z, 4f) || Deadly.Below(x, z)) return false;
            why = "kicker"; if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f || Mathf.Abs(Slope.Kicker(x, z + 18f)) > 0.01f || Mathf.Abs(Slope.Kicker(x, z - 10f)) > 0.01f) return false;
            why = "hamlet"; if (Hamlets.Taken(x, z)) return false;
            why = "steep"; if (Slope.Normal(x, z).y < 0.72f) return false;                        // rock, or a face too steep for snow to lie
            return true;
        }

        /// <summary>
        /// A crack whose wide part (too wide to carry across) reaches over the piste must leave a lane of Lane metres
        /// past it there, or have a snow bridge on the piste: if it has neither, it gets a bridge where the piste is.
        /// </summary>
        static void Fair(ref Course.Crevasse c)
        {
            if (c.half <= Course.CarryHalf) return;
            float zc = c.az + c.dz * c.len * 0.5f, ctr = Slope.Centre(zc), lo = ctr - Slope.PisteHalf + 1f, hi = ctr + Slope.PisteHalf - 1f;
            // which metres of the piste the wide, unbridged part covers
            var blocked = new bool[Mathf.CeilToInt(hi - lo) + 1];
            bool any = false;
            for (float u = 0f; u <= c.len; u += 0.5f)
            {
                float open = Course.Opening(c, u, out float mid);
                if (open <= Course.CarryHalf || c.Bridged(u)) continue;
                var q = c.Point(u, mid);
                int i = Mathf.RoundToInt(q.x - lo);
                if (i < 0 || i >= blocked.Length) continue;
                blocked[i] = true; any = true;
            }
            if (!any) return;
            int run = 0, longest = 0;
            for (int i = 0; i < blocked.Length; i++) { run = blocked[i] ? 0 : run + 1; longest = Mathf.Max(longest, run); }
            if (longest >= Lane) return;
            // no lane: a bridge 5-7 m long where the crack crosses the middle of the piste
            float um = Mathf.Clamp(c.Along(ctr, zc), 6f, c.len - 13f);
            float span = 5f + 2f * Mathf.Repeat(c.phase, 1f);
            if (c.b1 <= c.b0) { c.b0 = um; c.b1 = um + span; }
            else { c.c0 = um; c.c1 = um + span; }
        }
    }
}
