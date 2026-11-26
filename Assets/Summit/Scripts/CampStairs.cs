using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// (campstairs) Stairs up the bank between the gate and the camp, where the paved path used to run straight up it as
    /// a ramp at 40 to 60 per cent. Three straight flights of fourteen risers, 16 cm each on a 31 cm going (two risers
    /// and a going make 63 cm: an easy stride), a 1.2 m landing between flights and a short deck at the top; heavy timber
    /// treads on dark-stained risers, black steel strings, posts and handrails both sides (the lodge's own materials), a
    /// lamp at each landing. Above and below them the path stays a paved ramp, where the bank is gentle (7 to 15 per cent).
    ///
    /// The bank under them is graded to suit (Slope.Summit asks StairBed): its noise taken out and its surface laid just
    /// under the treads (never up through them: the terrain's 1.5 m triangles are allowed for), easing back into the
    /// natural bank either side, so the flights and landings are the same on every mountain.
    ///
    /// Walking: one floor the length of the stairs whose height rises a riser across each nosing and lies level over each
    /// tread's middle, where a foot lands; the feet are set on the treads themselves (StairFeet) and the steps are timed
    /// to them (StairPhase): two treads a step walking or running (quicker), one slowly.
    /// </summary>
    public partial class Camp
    {
        // ---------------------------------------------------------------- the layout (seed-independent)

        /// <summary>The stairs' middle line (dx from the run's centre), the bottom riser's and the top riser's z, half the treads' width.</summary>
        public const float StairX = -2.5f, StairBottomZ = -47f, StairTopZ = -61.5f, StairHalf = 1.2f;
        public const int StairFlights = 3, StairFlightRisers = 14;
        /// <summary>Each tread's going (front to back), and the deck at the top (past the last riser).</summary>
        public const float StairGoing = 0.31f, StairTopDeck = 1.0f;
        public static int StairRisers => StairFlights * StairFlightRisers;
        /// <summary>From the bottom riser to the top one.</summary>
        public static float StairLength => StairBottomZ - StairTopZ;
        public static float StairLanding => (StairLength - StairFlights * (StairFlightRisers - 1) * StairGoing) / (StairFlights - 1);
        static float FlightPeriod => (StairFlightRisers - 1) * StairGoing + StairLanding;
        /// <summary>Each riser's height: the bank's own rise over the stairs, shared out.</summary>
        public static float StairRise => (Slope.SummitBank(StairTopZ) - Slope.SummitBank(StairBottomZ)) / StairRisers;
        static float StairY0 => Slope.SummitBank(StairBottomZ);

        /// <summary>How far up the stairs (m, from the bottom riser) a world z is.</summary>
        static float StairU(float z) => StairBottomZ - z;

        /// <summary>Where riser j (0 the bottom one) stands, metres up from the bottom one.</summary>
        public static float StairRiserAt(int j)
        {
            int f = j / StairFlightRisers, k = j % StairFlightRisers;
            return f * FlightPeriod + k * StairGoing;
        }

        /// <summary>How many risers are at or below u (m up the stairs).</summary>
        static int RisersBelow(float u)
        {
            if (u < 0f) return 0;
            int f = Mathf.Min(StairFlights - 1, Mathf.FloorToInt(u / FlightPeriod));
            float s = u - f * FlightPeriod;
            return f * StairFlightRisers + Mathf.Min(StairFlightRisers, Mathf.FloorToInt(s / StairGoing + 1e-4f) + 1);
        }

        /// <summary>The tread's own height (above the summit's base) at u: stepwise, a riser at each riser line.</summary>
        static float TreadRel(float u) => StairY0 + RisersBelow(u) * StairRise;

        // ---------------------------------------------------------------- the bank under them (for Slope.Summit)

        // the graded bed: full across the stairs and a terrain triangle beyond them each side and each end (3.6 m), easing
        // back into the natural bank over the next 3.4 m
        const float BedIn = 3.6f, BedOut = 7f, BedEndIn = 3.2f, BedEndOut = 6.2f, BedStep = 0.05f;
        static float[] bedTable;
        const float BedU0 = -7f;

        /// <summary>
        /// How much of the summit's ground at (a: x from the run's centre, z) is the stairs' graded bed (0 to 1), and the
        /// bed's height there above the summit's base (Slope.SummitBase).
        /// </summary>
        public static float StairBed(float a, float z, out float rel)
        {
            rel = 0f;
            float lat = Mathf.Abs(a - StairX), u = StairU(z);
            if (lat >= BedOut || u <= -BedEndOut || u >= StairLength + StairTopDeck + BedEndOut) return 0f;
            float wl = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BedIn, BedOut, lat));
            float wa = u < 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-BedEndIn, -BedEndOut, u))
                              : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StairLength + StairTopDeck + BedEndIn, StairLength + StairTopDeck + BedEndOut, u));
            if (bedTable == null) MakeBed();
            float f = (u - BedU0) / BedStep;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, bedTable.Length - 2);
            rel = Mathf.Lerp(bedTable[i], bedTable[i + 1], Mathf.Clamp01(f - i));
            return wl * wa;
        }

        static void MakeBed()
        {
            int n = Mathf.CeilToInt((StairLength + StairTopDeck + BedEndOut - BedU0) / BedStep) + 2;
            var t = new float[n];
            for (int i = 0; i < n; i++) t[i] = BedAt(BedU0 + i * BedStep);
            bedTable = t;
        }

        const float Approach = 4.6f;   // (the paved ramp from the stairs' foot down to the gate's level)

        /// <summary>
        /// The bed at u: below the stairs an even ramp to the gate's level, past the top deck the bank's own (noise-free) shape; under them the line
        /// through the risers' feet, laid 24 cm lower (a terrain triangle 1.5 m across, bridging where a landing turns up
        /// into a flight, bulges up to 15 cm over the line it is drawn from), coming up to meet the bank at both ends.
        /// </summary>
        static float BedAt(float u)
        {
            float L = StairLength, R = StairRise, y0 = StairY0;
            int N = StairRisers;
            // below them, an even ramp down to the gate's level (the bank's own shape bunched its fall up at their foot:
            // 27 per cent there), then the bank
            if (u <= -Approach) return Slope.SummitBank(StairBottomZ - u);
            if (u <= 0f) return Mathf.Lerp(Slope.SummitBank(StairBottomZ + Approach), y0, (u + Approach) / Approach);
            const float off = 0.24f, after = 1.6f;
            if (u <= L)
            {
                int j = Mathf.Max(0, RisersBelow(u) - 1);
                float r0 = StairRiserAt(j), line = y0 + j * R;
                if (j + 1 < N) { float r1 = StairRiserAt(j + 1); line = Mathf.Lerp(line, line + R, (u - r0) / (r1 - r0)); }
                // (and level with the path under the first treads, so the snow there doesn't rise toward the first
                // riser across a terrain triangle: the path runs level up to it)
                return Mathf.Min(line - off * Mathf.Clamp01(u / 0.6f), y0 - 0.02f + 0.8f * Mathf.Max(0f, u - 1f));
            }
            if (u >= L + after) return Slope.SummitBank(StairBottomZ - u);
            // past the top riser: from under the last tread, up under the top deck, to the bank past it
            float s = Mathf.SmoothStep(0f, 1f, (u - L) / after);
            return Mathf.Lerp(y0 + (N - 1) * R - off, Slope.SummitBank(StairBottomZ - u), s);
        }

        // ---------------------------------------------------------------- walking on them

        static float[] walkTable;
        const float WalkStep = 0.02f;
        static float WalkU0 => -0.5f * StairGoing;
        static float WalkU1 => StairLength + StairTopDeck;

        /// <summary>
        /// The height a walker's body goes at (above the summit's base) at u: up a riser across each nosing, eased so
        /// it lies level over each tread's middle, where the feet land.
        /// </summary>
        static float WalkRel(float u)
        {
            float y = StairY0, w = 0.5f * StairGoing;
            for (int j = 0; j < StairRisers; j++)
            {
                float r = StairRiserAt(j);
                if (u >= r + w) { y += StairRise; continue; }
                if (u <= r - w) break;
                y += StairRise * Mathf.SmoothStep(0f, 1f, (u - r + w) / (2f * w));
            }
            return y;
        }

        /// <summary>Where the stairs are (world): the bottom riser's middle on the base's level, and the base's height.</summary>
        static Vector3 StairOrigin => new Vector3(Cx + StairX, Slope.SummitBase, StairBottomZ);

        /// <summary>A point (world) is over the stairs' treads (from the bottom riser to the end of the top deck, between the strings).</summary>
        public static bool OnStairs(Vector3 w, float margin = 0f)
        {
            float u = StairU(w.z);
            return Mathf.Abs(w.x - (Cx + StairX)) < StairHalf + margin && u > -margin && u < StairLength + StairTopDeck + margin;
        }

        /// <summary>The tread under a point (world), stepwise (for setting the feet on), or NaN off the stairs.</summary>
        public static float TreadUnder(Vector3 w)
        {
            if (!OnStairs(w, 0.05f)) return float.NaN;
            float u = StairU(w.z);
            return Slope.SummitBase + (u >= StairLength ? StairY0 + StairRisers * StairRise : TreadRel(u));
        }

        /// <summary>
        /// The walk cycle's phase (radians, mod pi: either foot) at which a walker here, going the way f (world), has a
        /// foot come down wholly on a tread (its heel and toes both on it: StairFeet.Heel, Toe), taking `per` treads a
        /// step, the foot coming down `lead` metres ahead of the body; NaN on a landing or off the stairs.
        /// </summary>
        public static float StairPhase(Vector3 at, Vector3 f, int per, float lead)
        {
            // (on them, or coming to them along them: in step before the first tread)
            float u = StairU(at.z);
            if (Mathf.Abs(at.x - (Cx + StairX)) > StairHalf + 0.3f || u < -2.5f || u > StairLength + StairTopDeck + 2.5f) return float.NaN;
            if (Mathf.Abs(f.z) < 0.5f) return float.NaN;
            float sgn = -Mathf.Sign(f.z);   // (up the stairs is -z)
            float foot = u + sgn * lead;
            // the flight the foot is on, or (on a landing, or not yet on them) the next it comes to
            int flight = Mathf.Clamp(Mathf.FloorToInt(foot / FlightPeriod), 0, StairFlights - 1);
            if (foot > flight * FlightPeriod + (StairFlightRisers - 1) * StairGoing + 0.05f && sgn > 0f) flight = Mathf.Min(StairFlights - 1, flight + 1);
            float r0 = flight * FlightPeriod;
            // the ankle where the foot's middle (between its heel and its toes) is over the tread's middle
            float ankle = r0 + 0.5f * StairGoing - sgn * 0.5f * (StairFeet.Toe - StairFeet.Heel);
            // the left heel strikes at pi/2; a step on, the right at 3pi/2: pi a step, `per` treads a step
            return Mathf.PI * 0.5f + Mathf.PI * sgn * (u - (ankle - sgn * lead)) / (per * StairGoing);
        }

        /// <summary>How far ahead of the body a foot comes down on the stairs (m, at scale 1, the walk clip's full stride).</summary>
        public const float StairLeadWalk = 0.34f;   // (measured: StairsTest)

        // ---------------------------------------------------------------- building them

        /// <summary>The stairs (Rebuild, before the path that runs to their ends).</summary>
        void Stairs()
        {
            float R = StairRise, L = StairLength, W = StairHalf, U = StairGoing, y0 = StairY0;
            int N = StairRisers;
            var t = new GameObject("Stairs").transform;
            t.SetParent(root, false);
            t.position = StairOrigin;   // (x across, y above the summit's base, z = -u: up the stairs is -z)
            var deck = Resources.Load<Material>("Camp/LodgeTex/Materials/Decking");
            var board = Resources.Load<Material>("Camp/LodgeTex/Materials/Cladding");
            var steel = Resources.Load<Material>("Camp/LodgeTex/Materials/Steel");
            if (deck == null) deck = wood; if (board == null) board = darkWood; if (steel == null) steel = metal;
            var treads = new Bits(); var risers = new Bits(); var iron = new Bits();
            float Bed(float u) => BedAt(u);

            // the treads: a 6 cm slab each with a 2 cm nosing over the riser below; dark-stained risers; filled in
            // under, down into the bed (the strings hide the sides)
            const float slab = 0.06f, nose = 0.025f;
            for (int j = 0; j < N; j++)
            {
                float r = StairRiserAt(j), y = y0 + (j + 1) * R;
                bool last = j == N - 1, topOfFlight = j % StairFlightRisers == StairFlightRisers - 1;
                float back = last ? r + StairTopDeck : topOfFlight ? StairRiserAt(j + 1) : r + U;   // (a landing, the top deck: one long tread)
                treads.Box(new Vector3(-W, y - slab, -back), new Vector3(W, y, -(r - nose)));
                float low = y - R;
                risers.Box(new Vector3(-W + 0.01f, low - 0.02f, -(r + 0.04f)), new Vector3(W - 0.01f, y - slab, -r));
                // under the tread: down to the bed (its lowest under it), so no gap shows past the strings' ends
                float bed = Mathf.Min(Bed(r), Bed(back)) - 0.12f;
                if (bed < y - slab - 0.02f) risers.Box(new Vector3(-W + 0.01f, bed, -back), new Vector3(W - 0.01f, y - slab, -(r + 0.04f)));
            }

            // the strings, both sides: steel plates from just over the nosings' line down into the bed
            var us = new List<float>();
            for (float u = -0.36f; u <= L + StairTopDeck + 1e-3f; u += 0.31f) us.Add(u);
            for (int j = 0; j < N; j++) { us.Add(StairRiserAt(j)); if (j % StairFlightRisers == 0) us.Add(StairRiserAt(j) - U); }
            us.Add(L + StairTopDeck);
            us.Sort();
            for (int s = -1; s <= 1; s += 2)
            {
                float x0 = s * (W + 0.005f), x1 = s * (W + 0.025f);
                for (int i = 0; i + 1 < us.Count; i++)
                {
                    float a = us[i], b = us[i + 1];
                    if (b - a < 1e-3f) continue;
                    float ta = PitchLine(a) + 0.1f, tb = PitchLine(b) + 0.1f, ba = Bed(a) - 0.15f, bb = Bed(b) - 0.15f;
                    iron.Quad(new Vector3(x1, ba, -a), new Vector3(x1, bb, -b), new Vector3(x1, tb, -b), new Vector3(x1, ta, -a), new Vector3(s, 0f, 0f));    // outside
                    iron.Quad(new Vector3(x0, ba, -a), new Vector3(x0, bb, -b), new Vector3(x0, tb, -b), new Vector3(x0, ta, -a), new Vector3(-s, 0f, 0f));    // inside
                    iron.Quad(new Vector3(x0, ta, -a), new Vector3(x0, tb, -b), new Vector3(x1, tb, -b), new Vector3(x1, ta, -a), Vector3.up);    // its top edge
                }
                // the ends
                foreach (var e in new[] { us[0], us[us.Count - 1] })
                    iron.Quad(new Vector3(x0, Bed(e) - 0.15f, -e), new Vector3(x1, Bed(e) - 0.15f, -e), new Vector3(x1, PitchLine(e) + 0.1f, -e), new Vector3(x0, PitchLine(e) + 0.1f, -e), e > 1f ? Vector3.back : Vector3.forward);
            }

            // handrails both sides: square steel posts about every 1.2 m, a round rail 92 cm over the nosings' line and a
            // second at knee height, running on 30 cm past the top and bottom risers, level there
            var rails = new GameObject("Rails").transform; rails.SetParent(t, false);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (W + 0.06f);
                var posts = new List<float> { -0.3f };
                for (int f = 0; f < StairFlights; f++)
                {
                    float r0 = f * FlightPeriod, r1 = r0 + (StairFlightRisers - 1) * U;
                    int k = Mathf.Max(1, Mathf.RoundToInt((r1 - r0) / 1.25f));
                    for (int i = 0; i <= k; i++) posts.Add(Mathf.Lerp(r0, r1, i / (float)k));
                }
                posts.Add(L + StairTopDeck - 0.05f);
                for (int i = 0; i < posts.Count; i++)
                {
                    float u = posts[i], top = PitchLine(u) + 0.92f;
                    iron.Box(new Vector3(x - 0.025f, Bed(u) - 0.1f, -u - 0.025f), new Vector3(x + 0.025f, top, -u + 0.025f));
                }
                // the rails: straight runs between the line's corners
                var corners = new List<float> { -0.3f, -U };
                for (int f = 0; f < StairFlights; f++)
                {
                    float r0 = f * FlightPeriod, r1 = r0 + (StairFlightRisers - 1) * U;
                    if (f > 0) corners.Add(r0 - U);
                    corners.Add(r1);
                }
                corners.Add(L + StairTopDeck - 0.05f);
                foreach (var h in new[] { 0.92f, 0.45f })
                    for (int i = 0; i + 1 < corners.Count; i++)
                    {
                        float a = corners[i], b = corners[i + 1];
                        Log(rails, new Vector3(x, PitchLine(a) + h, -a), new Vector3(x, PitchLine(b) + h, -b), 0.045f, steel);
                    }
                // nobody through them, on the stairs or the snow beside
                Block(t, new Vector3(x, 0f, -(L + StairTopDeck - 0.35f) * 0.5f), new Vector2(0.06f, (L + StairTopDeck + 0.35f) * 0.5f));
            }
            Combine(rails);

            StairMesh("StairTreads", treads, deck, t); StairMesh("StairRisers", risers, board, t); StairMesh("StairSteel", iron, steel, t);

            // the walking: one floor the length of them, its height from the table
            if (walkTable == null)
            {
                int n = Mathf.CeilToInt((WalkU1 - WalkU0) / WalkStep) + 2;
                walkTable = new float[n];
                // (indexed from the floor's -z end: the top)
                for (int i = 0; i < n; i++) walkTable[i] = WalkRel(WalkU1 - i * WalkStep);
            }
            float len = WalkU1 - WalkU0;
            boxes.Add(new Box
            {
                c = new Vector2(Cx + StairX, StairBottomZ - (WalkU0 + WalkU1) * 0.5f), h = new Vector2(W, len * 0.5f), cos = 1f, sin = 0f,
                floor = true, top = Slope.SummitBase, prof = walkTable, profStep = WalkStep,
            });

            // a lamp at each landing and at the top and the bottom, outside the rail, on alternate sides (the bottom's on
            // the right: the missions board is on the left there, with its own light)
            var lampsAt = new List<float> { -0.6f };
            for (int f = 1; f < StairFlights; f++) lampsAt.Add(f * FlightPeriod - StairLanding * 0.5f);
            lampsAt.Add(L + StairTopDeck * 0.5f);
            var posts2 = new GameObject("StairLamps").transform; posts2.SetParent(root, false);
            for (int i = 0; i < lampsAt.Count; i++)
            {
                float u = lampsAt[i];
                var at = new Vector3(Cx + StairX + (i % 2 == 0 ? 1f : -1f) * (W + 0.5f), 0f, StairBottomZ - u);
                at.y = Slope.DrawnHeight(at.x, at.z);
                LampPost(posts2, at, i == 1 || i == lampsAt.Count - 1);
            }
            Combine(posts2);
            foreach (var r in posts2.GetComponentsInChildren<MeshRenderer>())
                if (r.sharedMaterial == bulb) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            StairLamps = lampsAt.Count;
        }

        /// <summary>Test tools: lamp posts by the stairs.</summary>
        public int StairLamps { get; private set; }

        /// <summary>The line over the nosings (above the summit's base) at u: along each flight's pitch, level over a landing.</summary>
        static float PitchLine(float u)
        {
            float R = StairRise, U = StairGoing, y0 = StairY0;
            if (u <= -U) return y0;
            if (u >= StairLength) return y0 + StairRisers * R;
            for (int f = 0; f < StairFlights; f++)
            {
                float r0 = f * FlightPeriod, r1 = r0 + (StairFlightRisers - 1) * U;
                float yb = y0 + f * StairFlightRisers * R;   // (the level below the flight's first riser)
                if (u <= r1) return Mathf.Max(yb, yb + R + (u - r0) * R / U);
                if (f + 1 < StairFlights && u < (f + 1) * FlightPeriod - U) return yb + StairFlightRisers * R;   // (the landing)
            }
            return y0 + StairRisers * R;
        }

        void StairMesh(string name, Bits b, Material m, Transform parent)
        {
            var mesh = Made(new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
            mesh.SetVertices(b.v); mesh.SetNormals(b.n); mesh.SetUVs(0, b.uv); mesh.SetTriangles(b.t, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// <summary>Flat-shaded boxes and quads, uvs a metre a repeat (laid out in the frame they're made in).</summary>
        class Bits
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            /// <summary>A quad a, b, c, d (in order round it), facing `outward`.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                var nn = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(nn, outward) < 0f) { var x = b; b = d; d = x; nn = -nn; }
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(nn);
                // (a metre a repeat, laid on the plane the face mostly faces)
                foreach (var p in new[] { a, b, c, d })
                    uv.Add(Mathf.Abs(nn.y) > 0.7f ? new Vector2(p.x, p.z) : Mathf.Abs(nn.x) > 0.7f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                // (Unity's front faces: the cross of the first two edges points out)
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            public void Box(Vector3 lo, Vector3 hi)
            {
                Vector3 P(int x, int y, int z) => new Vector3(x == 0 ? lo.x : hi.x, y == 0 ? lo.y : hi.y, z == 0 ? lo.z : hi.z);
                Quad(P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), Vector3.up);
                Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3.down);
                Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), Vector3.back);
                Quad(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), Vector3.forward);
                Quad(P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), Vector3.left);
                Quad(P(1, 0, 0), P(1, 0, 1), P(1, 1, 1), P(1, 1, 0), Vector3.right);
            }
        }
    }
}
