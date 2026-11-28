using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Icy stretches: now and then a stretch of the run is hard ice, wind-scoured or melted and frozen again, most of
    /// the way across the groomed run for 45 to 100 m. The run's open snow (fair, and where the run keeps its special
    /// snow: not the start, the park, the danger stretch or a cliff band) is cut into pieces of up to SectionLen m, and
    /// each piece gets one chance of one, a mountain's own (the seed's): better where the run is steep, where the wind
    /// gets at it (above the tree line) and where it is in the shade (facing away from the day's sun, between canyon
    /// walls or under a cliff). What is left between its scoured patches is hard-pack: the groomed run.
    ///
    /// The ice rides as Surfaces' ice does (the least grip, it chatters and scrapes, the board slides on longer), and the
    /// terrain draws it (SnowSurfaceLook.hlsl): blue-grey and polished, in streaks down the fall line, no grooming lines.
    ///
    /// Fair (Fair): no ice of any kind (these stretches, the steep pitches' patches, the lift lines') at a roll-over's
    /// lip or landing, a blind roll and what lies out of sight past its lip, a rock step, a jump cliff or a kicker and
    /// their in-runs and landings, a deadly wall's or a ledge line's stretch, a ridge with a cliff beside it, or near a
    /// crevasse. An icy stretch starts on open snow with SeenFrom m of it above its fade, so it is seen coming, never
    /// past a crest; where it runs on over a lip or a landing, Fair cuts its ice out there.
    /// </summary>
    public static partial class Surfaces
    {
        /// <summary>One chance of an icy stretch for every this many metres of the run's open snow.</summary>
        public const float SectionLen = 110f;
        /// <summary>The chance, out of 1: IceChance0, and up to these more for a steep, wind-blown, shaded stretch (the most IceChanceMost).</summary>
        public const float IceChance0 = 0.15f, IceChanceSteep = 0.3f, IceChanceWind = 0.2f, IceChanceShade = 0.25f, IceChanceMost = 0.85f;
        /// <summary>A stretch's length (from IceLenMin to IceLenMax), and the metres its ice comes in and goes over at each end.</summary>
        public const float IceLenMin = 45f, IceLenMax = 100f, IceFade = 14f;
        /// <summary>How much of the snow is ice in the scoured patches of an icy stretch, and how far across the run it reaches
        /// (all of it to IceSheetBand of the half-width, gone by its edge).</summary>
        public const float IceSheetMost = 0.95f, IceSheetBand = 0.78f;
        /// <summary>The day's sun the shade is reckoned by (DayNight's at midday: from up the hill and to the left).</summary>
        static readonly Vector3 ToSun = -(Quaternion.Euler(50f, 50f, 0f) * Vector3.forward);

        /// <summary>An icy stretch: from z0 to z1 (its ice coming in and going over IceFade at each end), and why it is there.</summary>
        public struct IceSection { public float z0, z1, steep, wind, shade, chance; }

        static IceSection[] sections = new IceSection[0];
        static int sectionsStamp = -1;

        /// <summary>Today's mountain's icy stretches, down the hill.</summary>
        public static IceSection[] IceSections { get { Fresh(); if (sectionsStamp != stamp) LaySections(); return sections; } }

        /// <summary>The chances along the run (the test tools): each open piece's chance (x) and whether it got one (y).</summary>
        public static readonly List<Vector2> IceChances = new List<Vector2>();

        static void LaySections()
        {
            sectionsStamp = stamp;
            var list = new List<IceSection>();
            IceChances.Clear();
            // the open snow, in spans (each long enough for a stretch to be seen coming and start on it)
            float need = IceFade + SeenFrom + StartsOn;
            float span0 = float.NaN;
            for (float z = Slope.StartZ; z <= Slope.FinishZ - 60f; z += 2f)
            {
                bool open = Room(z) >= 0.999f;
                if (open && float.IsNaN(span0)) span0 = z;
                if ((!open || z + 2f > Slope.FinishZ - 60f) && !float.IsNaN(span0))
                {
                    float span1 = open ? z : z - 2f;
                    // each piece of it: one chance
                    for (float a = span0; span1 - a >= need; a += SectionLen)
                    {
                        float b = Mathf.Min(a + SectionLen, span1);
                        if (b - a < need) break;
                        int key = Mathf.RoundToInt(a);
                        Why(a, a + SectionLen, out float steep, out float wind, out float shade);
                        float chance = Mathf.Min(IceChanceMost, IceChance0 + IceChanceSteep * steep + IceChanceWind * wind + IceChanceShade * shade);
                        bool hit = Slope.Hash01(key, 5, 613) < chance;
                        IceChances.Add(new Vector2(chance, hit ? 1f : 0f));
                        if (!hit) continue;
                        // its start: seen coming from SeenFrom above its fade, on the piece's open snow; its length, on
                        // past the piece if it likes (Fair cuts it out round what it runs on into), most of it open
                        float z0 = a + IceFade + SeenFrom + (b - a - need) * 0.5f * Slope.Hash01(key, 7, 613);
                        float len = Mathf.Lerp(IceLenMin, IceLenMax, Slope.Hash01(key, 6, 613));
                        while (len > IceLenMin && OpenShare(z0, z0 + len) < MostOpen) len -= 5f;
                        if (OpenShare(z0, z0 + len) < MostOpen) continue;
                        list.Add(new IceSection { z0 = z0, z1 = z0 + len, steep = steep, wind = wind, shade = shade, chance = chance });
                        a = Mathf.Max(a, z0 + len + IceFade - SectionLen);   // (the next piece starts past it)
                    }
                    span0 = float.NaN;
                }
            }
            sections = list.ToArray();
        }

        /// <summary>The metres of fair, open snow above an icy stretch's fade, where a rider sees it coming from, and into it
        /// (its ice starts on open snow); and how much of its length must be fair.</summary>
        public const float SeenFrom = 22f, StartsOn = 10f, MostOpen = 0.55f;

        /// <summary>Where ice may lie and is kept (raw: by the features, not by the rows).</summary>
        static float Room(float z) => FairAt(z) * KeepAt(z);

        static float OpenShare(float z0, float z1)
        {
            int n = 0, open = 0;
            for (float z = z0; z <= z1; z += 2f, n++) if (Room(z) > 0.99f) open++;
            return open / (float)Mathf.Max(1, n);
        }

        /// <summary>0..1: where ice may lie at z and is kept (the test tools: an icy stretch's ice is only there).</summary>
        public static float IceRoom(float z) { Row(z, out _, out _, out float keep, out _, out float fair); return keep * fair; }

        /// <summary>A stretch's steepness, wind and shade, 0..1 each.</summary>
        static void Why(float a, float b, out float steep, out float wind, out float shade)
        {
            float pitch = 0f, lit = 0f, walls = 0f, alpine = 0f; int n = 0;
            for (float z = a + 5f; z < b; z += 10f, n++)
            {
                pitch += (Slope.FallLine(z - 3f) - Slope.FallLine(z + 3f)) / 6f;
                lit += Vector3.Dot(Slope.Normal(Slope.Centre(z), z), ToSun);
                alpine += 1f - Smooth((Forest.Low(z) - (Forest.TreeLine - 0.03f)) / (Forest.LineBand - Forest.TreeLine + 0.03f));
                walls = Mathf.Max(walls, Mathf.Max(Ridge.Depth(z), Cliffs.WallNear(z, 0f, out _) ? 1f : 0f));
            }
            pitch /= n; lit /= n; alpine /= n;
            steep = Smooth((pitch - 0.62f) / (0.92f - 0.62f));
            wind = Mathf.Clamp01(alpine);
            // (the run faces down the hill, away from the day's sun: a typical pitch is lit about 0.4; steeper, or turned
            // away, it is in the shade)
            shade = Mathf.Max(1f - Smooth((lit - 0.25f) / (0.5f - 0.25f)), 0.8f * walls);
        }

        /// <summary>0..1: how much of an icy stretch's ice lies at z (its fades at either end).</summary>
        static float SectionAt(float z)
        {
            var all = IceSections;
            for (int i = 0; i < all.Length; i++)
            {
                if (z < all[i].z0 - IceFade || z > all[i].z1 + IceFade) continue;
                return Smooth(Mathf.Min(z - (all[i].z0 - IceFade), all[i].z1 + IceFade - z) / IceFade);
            }
            return 0f;
        }

        /// <summary>An icy stretch's ice at (x, z), before the run's own rules (groomed, kept, fair): mostly ice, in scoured
        /// patches down the fall line, most of the way across.</summary>
        static float SectionIce(float x, float z, float aa, float half)
        {
            float s = SectionAt(z);
            if (s <= 0f) return 0f;
            float across = 1f - Smooth((aa / Mathf.Max(half, 1f) - IceSheetBand) / (1f - IceSheetBand));
            // (scoured streaks: long down the hill, a few metres across; hard-pack between them)
            float scour = Smooth((Slope.Noise(x * 0.11f + 4.3f, z * 0.022f + 8.9f) - 0.12f) / 0.3f);
            return s * across * Mathf.Lerp(0.55f, 1f, scour) * IceSheetMost;
        }

        // ---------------------------------------------------------------- fair: where no ice may lie

        /// <summary>
        /// 1 where ice may lie, 0 where none may (eased over FairRamp m): a roll's lip and landing, a blind roll and what
        /// lies out of sight past it, a rock step, a jump cliff or kicker and its landing, a deadly wall, a ledge line, a
        /// ridge, a crevasse. Worked out by the metre with the rest of a row (Row).
        /// </summary>
        public static float Fair(float z) { Row(z, out _, out _, out _, out _, out float fair); return fair; }

        const float FairRamp = 12f;

        static float Out(float z, float a, float b) => Smooth(Mathf.Max(a - z, z - b) / FairRamp);

        static float FairAt(float z)
        {
            float f = 1f;
            // the rolls: from above the lip to past the landing; a blind one on past where it is out of sight, a step's below
            int s = Mathf.FloorToInt(z / Slope.DropSeg);
            for (int k = s - 2; k <= s + 1; k++)
                if (Slope.DropOf(k, out var d))
                {
                    float top = d.LipZ - 12f, bottom = d.FootZ + 15f;
                    if (d.blind) bottom = Mathf.Max(bottom, d.LipZ + 60f);
                    if (d.step) bottom = Mathf.Max(bottom, d.stepZ + 30f);
                    f *= Out(z, top, bottom);
                }
            // a jump cliff: its in-run, face and landing
            // (each asked for far enough out that its ramp is all there: no step where it stops being found)
            // (every jump cliff near: two can be near at once, and JumpNear gives only the first)
            int cyc = Course.CycleOf(z);
            for (int c = cyc - 1; c <= cyc + 1; c++)
                for (int k = 0; k < 2; k++)
                    if (Cliffs.JumpOf(c, k, out var j)) f *= Out(z, j.Start - 20f, j.End + 20f);
            // the deadly wall, and a ledge line beside the run
            if (Cliffs.WallNear(z, 25f + FairRamp + 4f, out var w)) f *= Out(z, w.z0 - 25f, w.z2 + 10f);
            if (Cliffs.LedgeNear(z, 20f + FairRamp + 4f, out var L)) f *= Out(z, L.Start - 20f, L.End + 20f);
            // a ridge with a cliff beside it
            float rw = Ridge.Weight(z);
            if (rw > 0f && (Ridge.CliffSide(z, -1f) || Ridge.CliffSide(z, 1f))) f *= 1f - rw;
            // a kicker: its in-run and landing
            if (Slope.NextKicker(z - 40f - FairRamp - 4f, out var kick) && kick.z < z + 30f + FairRamp + 4f) f *= Out(z, kick.z - 30f, kick.z + 40f);
            // a crevasse's lips
            var cv = Course.Crevasses;
            for (int i = 0; i < cv.Length; i++)
                if (z > cv[i].z0 - 15f - FairRamp - 4f && z < cv[i].z1 + 15f + FairRamp + 4f) f *= Out(z, cv[i].z0 - 15f, cv[i].z1 + 15f);
            return f;
        }
    }
}
