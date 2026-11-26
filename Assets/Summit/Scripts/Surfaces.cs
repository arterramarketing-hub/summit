using UnityEngine;

namespace Summit
{
    /// <summary>The kinds of snow underfoot (Surfaces).</summary>
    public enum Snow { Corduroy, Powder, Ice, Crud, Crust, Slush }

    /// <summary>
    /// What the snow is at one spot: a share of each kind, 0..1, adding up to no more than 1. What is left of 1 is
    /// corduroy (the groomed, packed snow everything else is measured against).
    /// </summary>
    public struct SnowMix
    {
        public float powder, ice, crud, crust, slush;
        /// <summary>0..1: how far off the groomed run this is (feel): the powder here is deep, not fresh snow lying on
        /// the run. Not a share of its own: it says what kind of powder the powder is.</summary>
        public float deep;
        public float Corduroy => Mathf.Max(0f, 1f - powder - ice - crud - crust - slush);
        /// <summary>The share of deep powder here (off the run).</summary>
        public float Deep => Mathf.Min(powder, deep);

        public float this[Snow s] =>
            s == Snow.Powder ? powder : s == Snow.Ice ? ice : s == Snow.Crud ? crud : s == Snow.Crust ? crust : s == Snow.Slush ? slush : Corduroy;

        /// <summary>All of one kind (the test tools).</summary>
        public static SnowMix Only(Snow s) => new SnowMix
        { powder = s == Snow.Powder ? 1f : 0f, ice = s == Snow.Ice ? 1f : 0f, crud = s == Snow.Crud ? 1f : 0f, crust = s == Snow.Crust ? 1f : 0f, slush = s == Snow.Slush ? 1f : 0f, deep = s == Snow.Powder ? 1f : 0f };

        /// <summary>The kind there is most of here.</summary>
        public Snow Most
        {
            get
            {
                Snow best = Snow.Corduroy; float most = Corduroy;
                if (powder > most) { most = powder; best = Snow.Powder; }
                if (ice > most) { most = ice; best = Snow.Ice; }
                if (crud > most) { most = crud; best = Snow.Crud; }
                if (crust > most) { most = crust; best = Snow.Crust; }
                if (slush > most) best = Snow.Slush;
                return best;
            }
        }

        /// <summary>`over` laid on top of this: it takes its share from every other kind alike.</summary>
        void Lay(float over) { float k = 1f - over; powder *= k; ice *= k; crud *= k; crust *= k; slush *= k; }
        public void LayIce(float w) { w = Mathf.Clamp01(w); Lay(w); ice += w; }
        public void LayCrud(float w) { w = Mathf.Clamp01(w); Lay(w); crud += w; }
        public void LayCrust(float w) { w = Mathf.Clamp01(w); Lay(w); crust += w; }
        public void LaySlush(float w) { w = Mathf.Clamp01(w); Lay(w); slush += w; }
    }

    /// <summary>
    /// How the snow at one spot rides (Surfaces.Feel): all of it scales the board's own numbers, none of it pushes.
    ///   friction  on Rider.Friction (the snow's drag at any speed)
    ///   drag      added to the air's drag, per (m/s)^2: snow the board has to plough
    ///   grip      how well an edge holds on it (1 = corduroy, which holds best; ice the least): with less, the board
    ///             answers the finger's push a little later and slides on a little longer once it is let go
    ///   bite      how firm it is under an edge (1 = corduroy; powder is soft): with less, it answers a little later
    ///   chatter   0..1: how much the board chatters (the look and the sound only)
    /// </summary>
    public struct SnowFeel
    {
        public float friction, drag, grip, bite, chatter;
        /// <summary>Deep powder's drag per (m/s)^2 at full depth (feel): the rider sinks into it (Rider.GroundStep), so it
        /// comes on over a moment, never in a step.</summary>
        public float deep;
        public static readonly SnowFeel Firm = new SnowFeel { friction = 1f, drag = 0f, grip = 1f, bite = 1f, chatter = 0f };
        /// <summary>Bare rock under the board: an edge has little to bite.</summary>
        public static readonly SnowFeel Rock = new SnowFeel { friction = 1f, drag = 0f, grip = 0.35f, bite = 0.5f, chatter = 0f };
    }

    /// <summary>
    /// The snow underfoot, by place (mountain audit E1): Surface(x, z) says what the snow is anywhere on today's
    /// mountain, and Feel what that does to the board. The rider's friction, grip, spray and sound and the terrain's
    /// look all read it; nothing else decides what snow is where.
    ///
    ///   corduroy   the groomed run: fast, and it holds an edge best. The board's own numbers.
    ///   powder     off the run, all of it, and on a snowy day over the run as well: it drags (less at speed, when the
    ///              board floats), it is soft under an edge (the board washes out further before it holds, and holds
    ///              less), it throws the most snow and it is quiet.
    ///   ice        scraped patches down the middle of the run where it is steepest, under the lift lines, and now
    ///              and then a whole icy stretch (SurfacesIce): the fastest snow, with the least edge: it lets go
    ///              soonest, and it chatters. It shines. Never at a lip or a landing (SurfacesIce: Fair).
    ///   crud       tracked-out snow round the lift stations and along the hamlets: a little slower, and it chatters.
    ///   crust      wind crust and sastrugi: the alpine ridges, the rims of the bowl and the open snow above the tree
    ///              line, on a windy day (a little of it is always there).
    ///   slush      the last stretch above the finish in the afternoon and the warm evening light: sticky and slow.
    ///
    /// Two rules:
    ///  - every share is a smooth function of the place, so the snow changes under the board over metres, never in a
    ///    step. Nothing here is a yes or no.
    ///  - the snow never pushes, and it never lets go (slide: Rider.GroundStep). Grip and bite only set how soon
    ///    the board answers the finger's push and how long a slide lasts once it is let go: ice and powder are a
    ///    little slower both ways. The sideways speed a held drag gives is the same on every kind.
    ///
    /// The day's part (Day: fresh snow, wind, warmth) comes from Weather and the clock through Step, once a frame. The
    /// test tools, which run neither, get a calm, cold, clear day unless they set it.
    /// </summary>
    public static partial class Surfaces
    {
        /// <summary>Off: corduroy everywhere (the mountain as it rode before).</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("SUMMIT_NO_SURFACES") != "1";
        /// <summary>The test tools: this snow everywhere, whatever the place.</summary>
        public static SnowMix? Force;

        /// <summary>The day: fresh snow lying (0 none .. 1 a snowy day), wind (0 calm .. 1 a gale), warmth (0 cold .. 1 a warm afternoon).</summary>
        public struct Weathering { public float fresh, wind, warm; }
        public static Weathering Day;

        // ---------------------------------------------------------------- how each kind rides
        //                                                 corduroy  powder    ice    crud   crust   slush
        public static readonly float[] Friction = {          1f,     2.0f,   0.45f,  1.2f,   1.15f,  2.3f };
        // (drag per (m/s)^2, on top of the air's. Ice takes a little off: the part of the board's own drag that is
        // corduroy's)
        public static readonly float[] Plough   = {          0f,  0.0042f, -0.0003f, 0.0003f, 0.0002f, 0.0022f };
        public static readonly float[] Grip     = {          1f,     1f,     0.45f,  0.9f,   0.8f,   0.7f };
        public static readonly float[] Bite     = {          1f,     1f,      0.8f,  0.9f,   0.8f,   0.5f };
        public static readonly float[] Chatter  = {          0f,       0f,    0.5f,    1f,   0.7f,     0f };
        /// <summary>Powder floats the board at speed: its plough is this much of itself from FloatTo m/s up (all of it under FloatFrom).</summary>
        public const float Float = 0.4f, FloatFrom = 8f, FloatTo = 22f;
        /// <summary>Old powder (no fresh snow on it) drags this much of what a snowy day's does.</summary>
        public const float OldPowder = 0.7f;
        /// <summary>
        /// Deep powder, off the groomed run and through the trees (feel): far more drag than fresh snow lying on the run
        /// (DeepPlough per (m/s)^2, at full depth), so a board settles at a tree-weaving pace there and the groomed run
        /// is where the speed is. It floats a little at speed (DeepFloat of itself from FloatTo up) and is a little less
        /// deep on a day with no fresh snow (DeepOld). It comes on over the run's edge (EdgeBlend), so leaving the run is
        /// sinking into the snow, not hitting a wall.
        /// </summary>
        public static float DeepPlough = 0.021f, DeepFloat = 0.85f, DeepOld = 0.85f;
        /// <summary>The most deep powder ever slows the board, in gravities (so riding into it fast is sinking in hard, never
        /// a wall), and how long the board takes to sink into it, and to come up out of it (s).</summary>
        public static float DeepMost = 0.5f, DeepSinkIn = 1.3f, DeepSinkOut = 0.5f;

        public static SnowFeel Feel(in SnowMix m, float speed)
        {
            float c = m.Corduroy;
            float deep = Mathf.Lerp(OldPowder, 1f, Day.fresh);
            float afloat = Mathf.Lerp(1f, Float, Mathf.Clamp01((speed - FloatFrom) / (FloatTo - FloatFrom)));
            return new SnowFeel
            {
                friction = c + m.powder * (1f + (Friction[1] - 1f) * deep) + m.ice * Friction[2] + m.crud * Friction[3] + m.crust * Friction[4] + m.slush * Friction[5],
                drag = (m.powder - m.Deep) * Plough[1] * deep * afloat
                     + m.ice * Plough[2] + m.crud * Plough[3] + m.crust * Plough[4] + m.slush * Plough[5],
                deep = DeepPlough * Mathf.Lerp(DeepOld, 1f, Day.fresh) * Mathf.Lerp(1f, DeepFloat, Mathf.Clamp01((speed - FloatFrom) / (FloatTo - FloatFrom))),
                grip = c + m.powder * Grip[1] + m.ice * Grip[2] + m.crud * Grip[3] + m.crust * Grip[4] + m.slush * Grip[5],
                bite = c + m.powder * Bite[1] + m.ice * Bite[2] + m.crud * Bite[3] + m.crust * Bite[4] + m.slush * Bite[5],
                chatter = m.ice * Chatter[2] + m.crud * Chatter[3] + m.crust * Chatter[4],
            };
        }

        // ---------------------------------------------------------------- where each kind lies

        /// <summary>Metres over which the groomed run's edge blends into the snow beside it (as Slope.Rough).</summary>
        public const float EdgeBlend = 10f;
        /// <summary>A snowy day lays this much powder on the groomed run itself.</summary>
        public const float FreshOnRun = 0.5f;
        // ice: where the run's own pitch (rise over run down the fall line) is over IceFrom, all of it by IceFull; in
        // the middle IceBand of the run's half-width (gone by IceEdge of it); in patches
        public const float IceFrom = 0.77f, IceFull = 0.86f, IceBand = 0.45f, IceEdge = 0.85f, IceMost = 0.9f;
        // under a lift line: within LiftIceIn m of it (gone by LiftIceOut), in patches
        public const float LiftIceIn = 2.5f, LiftIceOut = 9.5f, LiftIceMost = 0.8f;
        // crud: round a lift's stations (all of it within StationIn m, gone by StationOut) and along a hamlet
        public const float StationIn = 14f, StationOut = 44f, CrudMost = 0.8f;
        // crust: always this much of what a gale lays
        public const float CalmCrust = 0.2f;
        // slush: from this far above the finish (m down the hill), all of it SlushRamp further down
        public const float SlushAbove = 640f, SlushRamp = 400f, SlushMost = 0.9f;

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>The snow at (x, z).</summary>
        public static SnowMix Surface(float x, float z)
        {
            if (Force.HasValue) return Force.Value;
            if (!On) return default;
            var m = Place(x, z);
            float slush = Slushy(z);
            if (slush > 0f) m.LaySlush(slush);
            return m;
        }

        /// <summary>The snow at (x, z) before the day's slush is laid on it (which is the same right across the hill: the
        /// terrain's shader lays it itself).</summary>
        static SnowMix Place(float x, float z)
        {
            if (Force.HasValue) { var f = Force.Value; f.slush = 0f; return f; }
            if (!On) return default;
            Row(z, out float pitch, out float alpine, out float keep, out float half, out float fair);

            float a = Slope.Across(x, z), aa = Mathf.Abs(a);
            float rough = Smooth((aa - half) / EdgeBlend);
            // (a lift hill's cleared lane and the hill itself are packed, as the run is)
            float lane = Mathf.Max(Lane(x, z), LiftTop.Groomed(x, z));   // (and the Summit top's bench and its ways: LiftTop)
            rough *= 1f - lane;
            float groomed = 1f - rough;

            var m = new SnowMix();
            // ---- powder: off the run, and fresh snow over it
            m.powder = Mathf.Clamp01(rough + groomed * FreshOnRun * Day.fresh);
            m.deep = rough;

            // ---- crust: the wind's, on what stands in it
            float wind = Mathf.Lerp(CalmCrust, 1f, Day.wind);
            float place = Mathf.Max(Ridge.Weight(z) * Mathf.Lerp(0.5f, 1f, alpine), rough * alpine * 0.5f);
            if (Bowl.Near(z, 0f, out var bowl))
            {
                // (the rim: from most of the way up the walls, out over the top)
                float reach = half / 0.85f;
                float inZ = Smooth((z - bowl.zA) / 30f) * Smooth((bowl.zE - z) / 40f);
                place = Mathf.Max(place, inZ * Smooth((aa - 0.7f * reach) / (0.3f * reach)));
            }
            if (place > 0f)
            {
                // (sastrugi: in bands across the wind, never a sheet)
                float band = 0.55f + 0.45f * Slope.Noise(x * 0.045f + z * 0.02f + 11.3f, z * 0.06f - x * 0.015f + 5.9f);
                m.LayCrust(place * wind * band * keep);
            }
            // (a cornice's face, its crown and the top of its landing: scoured by the wind that built it, whatever the day)
            float cf = Cliffs.CorniceFace(x, z);
            if (cf > 0f) m.LayCrust(cf * (0.7f + 0.2f * Slope.Noise(x * 0.11f + 2.3f, z * 0.09f + 7.1f)));
            // (a freeride line's rollers, lips and spines: wind-packed, its ripples showing, so the shapes read on white snow: Lines)
            float nf = Summit.Lines.NaturalShare(x, z);
            if (nf > 0f) m.LayCrust(nf * (0.6f + 0.25f * Slope.Noise(x * 0.13f + 5.1f, z * 0.1f + 1.7f)));

            // ---- crud: where people are
            float crud = Mathf.Max(Stations(x, z), Hamlet(x, z, a, half));
            if (crud > 0f)
                m.LayCrud(crud * CrudMost * (0.65f + 0.35f * Slope.Noise(x * 0.21f + 3.7f, z * 0.17f + 9.1f)) * (1f - 0.6f * Day.fresh) * keep);

            // ---- ice: scraped where the run is steepest, down its middle, and under the lift lines
            float steep = pitch;   // (0..1: Row)
            float ice = 0f;
            if (steep > 0f && groomed > 0f)
            {
                float mid = 1f - Smooth((aa / Mathf.Max(half, 1f) - IceBand) / (IceEdge - IceBand));
                float patch = Smooth((Slope.Noise(x * 0.07f + 7.7f, z * 0.03f + 3.1f) - 0.3f) / 0.3f);
                ice = steep * mid * patch * groomed * IceMost;
            }
            float under = UnderLift(x, z);
            if (under > 0f)
                ice = Mathf.Max(ice, under * LiftIceMost * Smooth((Slope.Noise(x * 0.06f + 1.9f, z * 0.05f + 6.3f) - 0.3f) / 0.3f) * (1f - lane) * (1f - crud));
            // (an icy stretch: SurfacesIce)
            if (groomed > 0f) ice = Mathf.Max(ice, SectionIce(x, z, aa, half) * groomed * (1f - lane) * (1f - crud));
            // (none where it is not fair: a lip, a landing, what is out of sight past a blind roll: SurfacesIce.Fair)
            if (ice > 0f) m.LayIce(ice * (1f - 0.7f * Day.fresh) * keep * fair);
            return m;
        }

        /// <summary>How much of the snow at z is slush (the same everywhere across the hill; the terrain's shader works it out too).</summary>
        public static float Slushy(float z) => SlushMost * Day.warm * (1f - Day.fresh) * Smooth((z - (Slope.FinishZ - SlushAbove)) / SlushRamp);

        // ---------------------------------------------------------------- what only depends on how far down the hill

        // every metre of the mountain, worked out the first time it is asked for: how steep the run's own pitch is there (for its ice), how
        // alpine it is, how much of the special snow (ice, crud, crust) is kept (none at the start, in the park,
        // or along a cliff band's traverse and ledge, where the run needs every bit of its edge), and half the
        // groomed run's width (Slope.Half, eased over 12 m: where the run's width steps, the snow does not)
        const int Rows = 5632, RowBack = 512;
        static readonly float[] rowPitch = new float[Rows], rowAlpine = new float[Rows], rowKeep = new float[Rows], rowHalf = new float[Rows], rowFair = new float[Rows];
        static readonly int[] rowStamp = new int[Rows];
        static int stamp = 1, stampSeed = int.MinValue;
        static bool hooked;

        static void Fresh()
        {
            if (!hooked) { hooked = true; Slope.Changing += () => { stamp++; liftsFor = null; }; }
            if (stampSeed != Slope.Seed) { stampSeed = Slope.Seed; stamp++; liftsFor = null; }
        }

        static void Row(float z, out float pitch, out float alpine, out float keep, out float half, out float fair)
        {
            Fresh();
            float f = z + RowBack;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, Rows - 2);
            float t = Mathf.Clamp01(f - i);
            if (rowStamp[i] != stamp) Fill(i);
            if (rowStamp[i + 1] != stamp) Fill(i + 1);
            pitch = Mathf.Lerp(rowPitch[i], rowPitch[i + 1], t);
            alpine = Mathf.Lerp(rowAlpine[i], rowAlpine[i + 1], t);
            keep = Mathf.Lerp(rowKeep[i], rowKeep[i + 1], t);
            half = Mathf.Lerp(rowHalf[i], rowHalf[i + 1], t);
            fair = Mathf.Lerp(rowFair[i], rowFair[i + 1], t);
        }

        static void Fill(int i)
        {
            float z = i - RowBack;
            rowStamp[i] = stamp;
            // (how steep, as the ice goes by it: 0 under IceFrom, 1 from IceFull, eased over 16 m down the hill, so a
            // roll-over's ice comes in over a few board lengths, not at its lip)
            float steep = 0f;
            for (int k = -4; k <= 4; k++)
            {
                float zk = z + 2f * k, pitch = (Slope.FallLine(zk - 3f) - Slope.FallLine(zk + 3f)) / 6f;
                steep += Smooth((pitch - IceFrom) / (IceFull - IceFrom)) * (k == -4 || k == 4 ? 0.5f : 1f);
            }
            rowPitch[i] = Smooth(steep / 8f / 0.5f);   // (all of it where half of those 16 m are that steep)
            float half = 0f;
            for (int k = -3; k <= 3; k++) half += Slope.Half(z + 2f * k) * (k == -3 || k == 3 ? 0.5f : 1f);
            rowHalf[i] = half / 6f;
            rowAlpine[i] = 1f - Smooth((Forest.Low(z) - (Forest.TreeLine - 0.03f)) / (Forest.LineBand - Forest.TreeLine + 0.03f));
            rowKeep[i] = KeepAt(z);
            rowFair[i] = FairAt(z);   // (whether ice may lie here at all: SurfacesIce)
        }

        /// <summary>How much of the special snow (ice, crud, crust) is kept at z: none at the start, in the park and the
        /// danger stretch, along a cliff band's traverse and ledge, where the run needs every bit of its edge, or along a
        /// feature line's stretch.</summary>
        static float KeepAt(float z)
        {
            // none at the start (the gate, the first turns), fading in over 60 m
            float keep = Smooth((z - (Slope.StartZ + 40f)) / 60f);
            // none in the park (25 m to fade either end)
            float u = Mathf.Repeat(z - Course.First, Course.Cycle);
            if (z >= Course.First) keep *= Smooth(Mathf.Max(Course.ParkAt - 15f - u, u - (Course.DangerAt + 15f)) / 25f);
            // none along a cliff band or an angled ledge (from its turn in to where the run is back on the fall line)
            if (CliffBand.Near(z, 40f, out var b)) keep *= Smooth(Mathf.Max(b.z0 - 15f - z, z - (b.z3 + 15f)) / 25f);
            // none along a feature line's stretch: groomed, like the park (Lines)
            if (z >= Course.First)
                for (int c = Course.CycleOf(z) - 1; c <= Course.CycleOf(z); c++)
                    if (Summit.Lines.Window(c, out float la, out float lb, out _)) keep *= Smooth(Mathf.Max(la - 15f - z, z - (lb + 15f)) / 25f);
            return keep;
        }

        // ---------------------------------------------------------------- the lifts and the hamlets

        struct Line { public Vector2 top, dir, across; public float plan, zMin, zMax; public bool hill; public Vector2 laneA, laneB; public float laneHalf; }
        static LiftLine.Lift[] liftsFor;
        static Line[] lines = new Line[0];

        static Line[] Lines()
        {
            Fresh();
            var lifts = LiftLine.Lifts;
            if (ReferenceEquals(lifts, liftsFor)) return lines;
            liftsFor = lifts;
            lines = new Line[lifts.Length];
            for (int i = 0; i < lifts.Length; i++)
            {
                var l = lifts[i];
                var n = new Line
                {
                    top = new Vector2(l.top.x, l.top.z), dir = new Vector2(l.dir.x, l.dir.z), across = new Vector2(l.across.x, l.across.z), plan = l.plan,
                    zMin = l.ZMin - StationOut, zMax = l.ZMax + StationOut, hill = l.hill != null,
                };
                if (l.hill != null)
                {
                    // (the lane in from the run's edge, on over the hill to where its fall runs out)
                    n.laneA = new Vector2(l.hill.laneFrom.x, l.hill.laneFrom.z);
                    var crest = new Vector2(l.hill.crest.x, l.hill.crest.z);
                    Vector2 on = crest - n.laneA;
                    n.laneB = crest + (on.sqrMagnitude > 1e-4f ? on.normalized : Vector2.up) * Mathf.Max(0f, l.hill.fall);
                    n.laneHalf = l.hill.Reach + 2f;
                    n.zMin = Mathf.Min(n.zMin, Mathf.Min(n.laneA.y, n.laneB.y) - n.laneHalf - 12f);
                    n.zMax = Mathf.Max(n.zMax, Mathf.Max(n.laneA.y, n.laneB.y) + n.laneHalf + 12f);
                }
                lines[i] = n;
            }
            return lines;
        }

        /// <summary>1 on a lift hill's cleared lane and the hill itself, 0 a little way off it.</summary>
        static float Lane(float x, float z)
        {
            var all = Lines();
            float most = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].hill || z < all[i].zMin || z > all[i].zMax) continue;
                Vector2 p = new Vector2(x, z), ab = all[i].laneB - all[i].laneA;
                float t = Mathf.Clamp01(Vector2.Dot(p - all[i].laneA, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                float d = (p - (all[i].laneA + ab * t)).magnitude;
                most = Mathf.Max(most, 1f - Smooth((d - all[i].laneHalf) / 10f));
            }
            return most;
        }

        /// <summary>1 under a lift's line, 0 a few metres to either side of it (and past its stations).</summary>
        static float UnderLift(float x, float z)
        {
            var all = Lines();
            float most = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                if (z < all[i].zMin || z > all[i].zMax) continue;
                float dx = x - all[i].top.x, dz = z - all[i].top.y;
                float along = dx * all[i].dir.x + dz * all[i].dir.y, side = Mathf.Abs(dx * all[i].across.x + dz * all[i].across.y);
                if (side > LiftIceOut) continue;
                float ends = Smooth(Mathf.Min(along, all[i].plan - along) / 12f);
                most = Mathf.Max(most, ends * (1f - Smooth((side - LiftIceIn) / (LiftIceOut - LiftIceIn))));
            }
            return most;
        }

        /// <summary>1 at a lift's station, 0 by StationOut from it.</summary>
        static float Stations(float x, float z)
        {
            var all = Lines();
            float most = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                if (z < all[i].zMin || z > all[i].zMax) continue;
                float dx = x - all[i].top.x, dz = z - all[i].top.y;
                float d0 = Mathf.Sqrt(dx * dx + dz * dz);
                float bx = dx - all[i].dir.x * all[i].plan, bz = dz - all[i].dir.y * all[i].plan;
                float d = Mathf.Min(d0, Mathf.Sqrt(bx * bx + bz * bz));
                if (d < StationOut) most = Mathf.Max(most, 1f - Smooth((d - StationIn) / (StationOut - StationIn)));
            }
            return most;
        }

        /// <summary>1 along a hamlet: its side of the run from a little short of the middle, out past its last house.</summary>
        static float Hamlet(float x, float z, float a, float half)
        {
            float most = 0f;
            int c = Course.CycleOf(z);
            for (int cc = c - 1; cc <= c + 1; cc++)
            {
                var h = Hamlets.Of(cc);
                if (h == null || z < h.z0 - 45f || z > h.z1 + 45f) continue;
                float inZ = Smooth(Mathf.Min(z - (h.z0 - 45f), h.z1 + 45f - z) / 30f);
                float s = a * h.side;
                float inX = Smooth((s + 2f) / (0.5f * half)) * (1f - Smooth((s - (h.far + 8f)) / 12f));
                most = Mathf.Max(most, inZ * inX);
            }
            return most;
        }
    }
}
