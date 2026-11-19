using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Hamlets beside the piste: a handful of alpine cabins in a clearing, so that now and then the run passes
    /// through somewhere lived in. The buildings are modelled in Blender by Tools/MakeCabins.py
    /// (Resources/Cabins): a gabled chalet, a tall modern lodge, four small cabins on steel stilts, a car on a
    /// parking deck and a lamp post. A post-and-rail fence runs along the piste's edge beside them.
    ///
    /// Where: in the cruise that opens a cycle (the 200 m before the park), on one side of the run, the nearest
    /// wall at least 5 m outside the groomed piste. The first cycle always has one, a few seconds after the
    /// drop-in; after that about three cycles in five do, so there are long stretches of wilderness between.
    /// Like everything on the mountain, where they stand is a pure function of position and the day's seed
    /// (Of, Taken): each building looks, within a few metres of its place in the layout, for the ground that
    /// falls least under it (the bench beside a jump cliff's in-run, when there is one), and is left out if the
    /// hill is too steep for its base there.
    ///
    /// Seated: the floor goes level on the highest snow under the house, so the uphill wall stands in the hill,
    /// and the stone base or the stilts, modelled well down below the floor, take up the fall of the slope.
    /// Nothing is cut into the mountain: the terrain and the ride are exactly as they were.
    ///
    /// Solid: every building is an Obstacle (Rider crashes into it like a tree); the fence is not: each span gives way
    /// at a touch (Breakable, Kind.Fence) and is back for the next run. Forest asks
    /// Taken() so no tree or rock grows in a house, in the fence or between the houses and the piste.
    ///
    /// Cheap: a hamlet is built as the rider comes within StreamAhead of it and freed StreamBehind past it. Each
    /// building is one mesh per material from one shared set of materials; beyond NearRange it is swapped for a
    /// far version of a hundred triangles and one material, and its yard is put away. Only the near versions
    /// cast shadows. The windows are one glowing material turned up at dusk (DayNight.Darkness); a hamlet has
    /// two real lamps at most.
    /// </summary>
    public class Hamlets : MonoBehaviour
    {
        // ---------------------------------------------------------------- what can be built

        /// <summary>A building: its footprint in its own frame (x across, z out of the front, downhill), and the
        /// most the ground may fall under it (its base or stilts go further down than that).</summary>
        public class Kind { public string name, label; public float hx, zBack, zFront, maxDrop; public bool house; }

        // (the footprints are printed by Tools/MakeCabins.py; keep them in step with the models)
        public static readonly Kind[] Kinds =
        {
            new Kind { name = "Chalet", label = "Cabin!", hx = 5.65f, zBack = -3.90f, zFront = 3.90f, maxDrop = 7.6f, house = true },
            new Kind { name = "Lodge", label = "Cabin!", hx = 4.10f, zBack = -3.35f, zFront = 3.35f, maxDrop = 7.2f, house = true },
            new Kind { name = "CabinA", label = "Cabin!", hx = 2.40f, zBack = -2.40f, zFront = 3.40f, maxDrop = 6.0f, house = true },
            new Kind { name = "CabinB", label = "Cabin!", hx = 2.40f, zBack = -2.60f, zFront = 3.70f, maxDrop = 6.0f, house = true },
            new Kind { name = "CabinC", label = "Cabin!", hx = 3.00f, zBack = -2.20f, zFront = 3.00f, maxDrop = 6.0f, house = true },
            new Kind { name = "CabinD", label = "Cabin!", hx = 2.20f, zBack = -2.20f, zFront = 2.10f, maxDrop = 6.0f, house = true },
            new Kind { name = "CarDeck", label = "Car!", hx = 3.10f, zBack = -1.70f, zFront = 1.70f, maxDrop = 5.0f },
            new Kind { name = "Lamp", label = "Lamp post!", hx = 0.20f, zBack = -0.20f, zFront = 0.20f, maxDrop = 99f },
        };
        const int Chalet = 0, Lodge = 1, CarDeck = 6, Lamp = 7;

        // the layout: how far down from the hamlet's top each building stands, how far out from the piste's centre
        // line, and how far up or down the hill it may move to find better ground
        struct Slot { public int kind; public float s, d, reach; }
        static readonly Slot[] Slots =
        {
            new Slot { kind = Chalet, s = 0f, d = 34.5f, reach = 12f },
            new Slot { kind = Lodge, s = 25f, d = 32.5f, reach = 8f },
            new Slot { kind = 2, s = 45f, d = 30f, reach = 5f },
            new Slot { kind = 3, s = 53f, d = 42.5f, reach = 5f },
            new Slot { kind = 4, s = 62f, d = 31f, reach = 5f },
            new Slot { kind = 5, s = 73f, d = 40.5f, reach = 5f },
            new Slot { kind = CarDeck, s = -15f, d = 33f, reach = 5f },
            new Slot { kind = CarDeck, s = 37f, d = 45.5f, reach = 4f },
        };

        public const float Chance = 0.6f;          // of a hamlet in a cycle after the first
        public const float MinLocal = 12f, MaxLocal = 194f;   // the stretch of a cycle a hamlet may stand in (its opening cruise)
        public const float Margin = 5f;            // the nearest wall is at least this far outside the groomed piste
        public const float FenceOff = 3.2f;        // the fence, outside the groomed piste's edge
        public const float Room = 4.5f;            // no tree or rock within this of a building
        const int MinHouses = 3;

        public struct Site { public int kind; public float x, z, yaw, drop; }

        /// <summary>One hamlet: which side of the run, its buildings, and the fence along the piste.</summary>
        public class Hamlet
        {
            public int cycle;
            public float side;             // -1 left of the run, +1 right
            public Site[] sites;
            public float z0, z1;           // the top and bottom of its buildings
            public float fenceZ0, fenceZ1, gapZ;   // the fence, and the middle of the gap in it
            public float near;             // the nearest building's reach toward the piste (from its centre line)
            public float far;              // and the furthest's the other way
            public int houses;
        }

        // ---------------------------------------------------------------- where (a pure function of the mountain)

        static readonly Dictionary<int, Hamlet> plans = new Dictionary<int, Hamlet>();
        static int plansSeed = int.MinValue;

        /// <summary>The hamlet of a cycle, or null if it has none.</summary>
        public static Hamlet Of(int cycle)
        {
            if (cycle < 0) return null;
            if (plansSeed != Slope.Seed) { plans.Clear(); plansSeed = Slope.Seed; }
            if (plans.TryGetValue(cycle, out var h)) return h;
            h = Plan(cycle);
            plans[cycle] = h;
            return h;
        }

        /// <summary>The last cycle that can hold one (the stretch before the finish is kept clear).</summary>
        public static int LastCycle => Mathf.Max(0, Course.CycleOf(Course.ClearFrom));

        static Hamlet Plan(int cycle)
        {
            if (cycle > 0 && Slope.Hash01(cycle, 1, 131) >= Chance) return null;
            float zb = Course.First + cycle * Course.Cycle;
            if (zb + MaxLocal > Course.ClearFrom - 20f) return null;
            // where its top stands within the cruise, and which side: the day's choice first, then the others,
            // keeping whichever seats the most (a jump cliff's rock band or a ridge's last canyon can rule a spot out)
            float a0 = 40f + 70f * Slope.Hash01(cycle, 2, 131);
            float side0 = Slope.Hash01(cycle, 3, 131) < 0.5f ? -1f : 1f;
            Hamlet best = null; float bestScore = 0f;
            int tries = cycle == 0 ? 6 : 4;
            for (int t = 0; t < tries; t++)
            {
                float a = 40f + Mathf.Repeat(a0 - 40f + (t / 2) * 26f, 70f);
                float side = (t & 1) == 0 ? side0 : -side0;
                var h = Lay(cycle, zb + a, side, out float score);
                if (h != null && score > bestScore) { best = h; bestScore = score; }
                if (best != null && best.houses >= 5) break;   // good enough: most of the family found ground
            }
            return best;
        }

        static readonly List<Site> laid = new List<Site>();

        static Hamlet Lay(int cycle, float top, float side, out float score)
        {
            score = 0f;
            laid.Clear();
            int houses = 0; float drops = 0f; bool chalet = false, lodge = false;
            for (int i = 0; i < Slots.Length; i++)
            {
                var sl = Slots[i]; var k = Kinds[sl.kind];
                // turned a little toward the piste, so its front and one side show from the run
                float yaw = -side * (sl.kind == CarDeck || sl.kind == Chalet ? 8f : 14f + 12f * Slope.Hash01(cycle, 20 + i, 131));
                bool found = false; Site pick = default; float cost = float.MaxValue;
                for (int j = 0; j < 5; j++)
                {
                    float ds = j == 0 ? 0f : (j <= 2 ? 0.5f : 1f) * sl.reach * ((j & 1) == 1 ? -1f : 1f);
                    for (int m = 0; m < 3; m++)
                    {
                        float z = top + sl.s + ds;
                        float x = Slope.Centre(z) + side * (sl.d + m * 3f);
                        if (!Ground(k, x, z, yaw, out float drop) || drop > k.maxDrop) continue;
                        if (Crowds(k, x, z)) continue;
                        float c = drop + 0.04f * Mathf.Abs(ds) + 0.12f * m;
                        if (c < cost) { cost = c; found = true; pick = new Site { kind = sl.kind, x = x, z = z, yaw = yaw, drop = drop }; }
                    }
                }
                if (!found) continue;
                laid.Add(pick);
                if (k.house) { houses++; drops += pick.drop; chalet |= sl.kind == Chalet; lodge |= sl.kind == Lodge; }
            }
            if (houses < MinHouses) return null;
            var h = new Hamlet { cycle = cycle, side = side, houses = houses, z0 = float.MaxValue, z1 = float.MinValue, near = float.MaxValue, far = 0f };
            foreach (var s in laid)
            {
                var k = Kinds[s.kind];
                float r = Radius(k), a = Mathf.Abs(s.x - Slope.Centre(s.z));
                h.z0 = Mathf.Min(h.z0, s.z - r); h.z1 = Mathf.Max(h.z1, s.z + r);
                h.near = Mathf.Min(h.near, a - r); h.far = Mathf.Max(h.far, a + r);
            }
            // the fence: from above the top building to below the bottom one, inside the cruise; a gap part way down
            float zb = Course.First + cycle * Course.Cycle;
            h.fenceZ0 = Mathf.Max(h.z0 - 10f, zb + 4f);
            h.fenceZ1 = Mathf.Min(h.z1 + 8f, zb + Course.ParkAt - 4f);
            h.gapZ = Mathf.Lerp(h.fenceZ0, h.fenceZ1, 0.3f + 0.3f * Slope.Hash01(cycle, 4, 131));
            // a lamp post inside the fence, by the gap
            float lz = h.gapZ - 5f, lx = Slope.Centre(lz) + side * (Slope.PisteHalf + FenceOff + 1.2f);
            laid.Add(new Site { kind = Lamp, x = lx, z = lz, yaw = side > 0f ? 180f : 0f });
            h.sites = laid.ToArray();
            score = houses * 10f + (chalet ? 8f : 0f) + (lodge ? 5f : 0f) - drops * 0.3f;
            return h;
        }

        static float Radius(Kind k) => Mathf.Max(k.hx, Mathf.Max(-k.zBack, k.zFront)) + 1f;

        /// <summary>A point of a building's own frame in the world (yaw in degrees, Unity's sense).</summary>
        static void ToWorld(float x, float z, float yaw, float lx, float lz, out float wx, out float wz)
        {
            float r = yaw * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            wx = x + lx * c + lz * s; wz = z - lx * s + lz * c;
        }

        /// <summary>Can this building stand here, and how far does the ground fall under it?</summary>
        static bool Ground(Kind k, float x, float z, float yaw, out float drop)
        {
            drop = 0f;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    ToWorld(x, z, yaw, -k.hx + i * k.hx, Mathf.Lerp(k.zBack, k.zFront, j * 0.5f), out float wx, out float wz);
                    if (!Open(wx, wz)) return false;
                    float y = Slope.Height(wx, wz);
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
            drop = hi - lo;
            return true;
        }

        /// <summary>Ground a building may stand on: the calm cruise at the top of a cycle, clear of the piste, the
        /// start, the ridges' canyons and the deadly cliffs.</summary>
        static bool Open(float x, float z)
        {
            if (z < Course.First + MinLocal || z > Course.ClearFrom - 20f) return false;
            float u = Mathf.Repeat(z - Course.First, Course.Cycle);
            if (u < MinLocal || u > MaxLocal) return false;
            if (Course.At(z) != Course.Section.Cruise) return false;
            if (Mathf.Abs(x - Slope.Centre(z)) < Slope.PisteHalf + Margin) return false;
            if (Slope.OnPad(x, z)) return false;
            // beside a ridge's last canyon (it runs on, filling in, into the top of the next cycle): not on its slopes
            // while it is still a canyon; once it is down to a dip the ground is the mountain's own again
            if (Ridge.InCanyons(z))
            {
                float a = x - Slope.Centre(z), side = a < 0f ? -1f : 1f;
                if (Ridge.DepthHere(z, side) > 2.5f && Mathf.Abs(a) < Ridge.Reach(z, side) + 9f) return false;
            }
            if (Cliffs.WallCut(x, z) < -0.2f || Cliffs.OnWallFace(x, z) || Deadly.Below(x, z)) return false;
            // nor across a jump cliff's rock band where it runs out into the rough (its outcrops stand there: CliffSpires);
            // the bench above it and the snow below it are fine, and the bench is the best ground there is
            if (Cliffs.JumpNear(z, 6f, out var j))
            {
                float across = x - Slope.Centre(j.z), below = z - Cliffs.LipZ(j, across);
                if (below > -3f && below < j.face + 3f && Mathf.Abs(across) < Slope.PisteHalf + 10f + Cliffs.JumpBand) return false;
            }
            return true;
        }

        static bool Crowds(Kind k, float x, float z)
        {
            float r = Radius(k);
            foreach (var s in laid)
            {
                float rr = r + Radius(Kinds[s.kind]) + 1.5f;
                float dx = s.x - x, dz = s.z - z;
                if (dx * dx + dz * dz < rr * rr) return true;
            }
            return false;
        }

        /// <summary>
        /// Is this spot taken by a hamlet? Forest asks before it plants a tree or sets down a rock: not in a building
        /// (or within Room of one), not in the fence, and not on the open ground between the houses and the piste,
        /// so the hamlet stands in a clearing that the run looks into. Everywhere else the forest is as it was.
        /// </summary>
        public static bool Taken(float x, float z)
        {
            if (z < Course.First) return false;
            float u = Mathf.Repeat(z - Course.First, Course.Cycle);
            if (u > MaxLocal + 30f) return false;   // (most of the mountain: nothing more to ask)
            var h = Of(Course.CycleOf(z));
            if (h == null || z < h.z0 - Room - 12f || z > h.z1 + Room + 10f) return false;
            float a = (x - Slope.Centre(z)) * h.side;
            if (a < Slope.PisteHalf - 3f || a > h.far + Room) return false;
            // the strip along the piste, fence and all: open from a little above the top house to the bottom one
            if (a < h.near + 1f && z > h.fenceZ0 - 2f && z < h.fenceZ1 + 2f) return true;
            foreach (var s in h.sites)
            {
                var k = Kinds[s.kind];
                float r = s.yaw * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
                float dx = x - s.x, dz = z - s.z;
                float lx = dx * c - dz * sn, lz = dx * sn + dz * c;   // into the building's own frame
                // (a little more room in front, for the balcony and the eaves, and behind for the banked snow)
                if (Mathf.Abs(lx) < k.hx + Room && lz > k.zBack - Room - 1f && lz < k.zFront + Room + 1.5f) return true;
            }
            return false;
        }

        /// <summary>The floor's height for a building here: level on the highest snow under it, a touch sunk in.</summary>
        public static float Seat(Site s)
        {
            var k = Kinds[s.kind];
            if (s.kind == Lamp) return Slope.DrawnHeight(s.x, s.z);
            float hi = float.MinValue;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 4; j++)
                {
                    ToWorld(s.x, s.z, s.yaw, -k.hx + i * k.hx * 0.5f, Mathf.Lerp(k.zBack, k.zFront, j * 0.25f), out float wx, out float wz);
                    hi = Mathf.Max(hi, Slope.DrawnHeight(wx, wz));
                }
            return hi - 0.15f;
        }

        // ---------------------------------------------------------------- building them near the rider

        public Transform target;
        public const float StreamAhead = 560f, StreamBehind = 60f;   // built this far ahead of the rider, freed this far past
        public const float NearRange = 150f, NearSlack = 15f;        // nearer than this: the full model, casting shadows
        public const float YardRange = 240f;                         // the fence, cars and lamp post are drawn out to here
        public const float LampRange = 130f, SmokeRange = 260f;

        Shader lit;
        Material soft, larch, darkWood, stone, metal, snow, glass, farMat, smokeMat;
        readonly Material[] paints = new Material[3];
        readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        readonly Dictionary<int, Built> built = new Dictionary<int, Built>();
        readonly List<int> drop = new List<int>();
        DayNight dayNight;
        int seed = int.MinValue;
        float glowShown = -1f, nextCheck;

        class House { public Vector3 at; public Renderer[] near; public Renderer far; public bool isNear, yard, shown; }
        class Built
        {
            public GameObject root; public Hamlet plan; public Vector3 centre;
            public readonly List<House> houses = new List<House>();
            public readonly List<Light> lamps = new List<Light>();
            public readonly List<ParticleSystem> smoke = new List<ParticleSystem>();
            public readonly List<BoxCollider> solid = new List<BoxCollider>();
            public readonly List<Mesh> meshes = new List<Mesh>();
            public Renderer[] fence; public bool fenceShown = true, smoking = true;
            public int next, porches;      // the next building to set down (one a frame), and the porch lamps lit so far
        }

        /// <summary>Called once where the game sets up the mountain (SummitGame): hamlets then look after themselves,
        /// following the rider and noticing a new mountain by its seed.</summary>
        public static Hamlets Create(Transform rider, Shader litShader, Material softParticles)
        {
            var h = new GameObject("Hamlets").AddComponent<Hamlets>();
            h.target = rider; h.lit = litShader; h.soft = softParticles;
            h.MakeMaterials();
            return h;
        }

        const float LampPower = 2.4f;
        static readonly Color GlowWarm = new Color(1f, 0.9f, 0.78f) * 1.5f;

        void MakeMaterials()
        {
            Material M(Color c, float smooth, Texture2D tex = null, float metallic = 0f)
            {
                var m = new Material(lit);
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metallic);
                if (tex != null) m.SetTexture("_BaseMap", tex);
                m.enableInstancing = true;
                return m;
            }
            Material Shining(string day, string night, float smooth)
            {
                var m = M(Color.white, smooth, Resources.Load<Texture2D>(day));
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", Resources.Load<Texture2D>(night));
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                return m;
            }
            // (the tints are Tools/MakeCabins.py's LARCH_TINT and the rest: the far version's swatches are painted from them)
            larch = M(new Color(1f, 0.86f, 0.66f), 0.15f, Resources.Load<Texture2D>("Camp/Oak")); larch.name = "Larch";
            darkWood = M(new Color(0.62f, 0.5f, 0.42f), 0.15f, Resources.Load<Texture2D>("Camp/Wood")); darkWood.name = "DarkWood";
            stone = M(new Color(0.95f, 0.95f, 0.95f), 0.1f, Resources.Load<Texture2D>("Camp/Stone")); stone.name = "Stone";
            metal = M(new Color(0.17f, 0.18f, 0.2f), 0.55f, null, 0.6f); metal.name = "Metal";
            snow = M(new Color(0.92f, 0.95f, 1f), 0.35f); snow.name = "Snow";
            paints[0] = M(new Color(0.62f, 0.1f, 0.08f), 0.6f); paints[1] = M(new Color(0.1f, 0.24f, 0.5f), 0.6f); paints[2] = M(new Color(0.75f, 0.76f, 0.78f), 0.6f);
            glass = Shining("Cabins/Windows", "Cabins/WindowsGlow", 0.85f); glass.name = "Glass";
            farMat = Shining("Cabins/Far", "Cabins/FarGlow", 0.2f); farMat.name = "Far";
            if (soft != null) { smokeMat = new Material(soft); smokeMat.SetColor("_BaseColor", Color.white); }
        }

        Material MatFor(string name, int pick)
        {
            switch (name)
            {
                case "Larch": return larch;
                case "DarkWood": return darkWood;
                case "Stone": return stone;
                case "Metal": return metal;
                case "Snow": return snow;
                case "Glass": return glass;
                case "Far": return farMat;
                case "Paint": return paints[pick % paints.Length];
                default: return darkWood;
            }
        }

        void Update()
        {
            if (target == null || lit == null) return;
            if (seed != Slope.Seed)
            {
                // a new mountain: everything built for the old one goes, and the whole run's hamlets are planned now,
                // while the game is setting up, so none is worked out in the middle of a run
                Clear();
                seed = Slope.Seed;
                for (int k = 0; k <= LastCycle; k++) Of(k);
            }
            Vector3 p = target.position;
            int c = Course.CycleOf(p.z);
            for (int k = Mathf.Max(0, c - 1); k <= c + 1; k++)
            {
                var h = Of(k);
                bool want = h != null && p.z > h.z0 - StreamAhead && p.z < h.z1 + StreamBehind;
                if (want && !built.ContainsKey(k)) built[k] = Begin(h);
            }
            foreach (var b in built.Values) if (b.next <= b.plan.sites.Length) Step(b);
            drop.Clear();
            foreach (var kv in built)
                if (p.z < kv.Value.plan.z0 - StreamAhead - 40f || p.z > kv.Value.plan.z1 + StreamBehind) drop.Add(kv.Key);
            foreach (int k in drop) { Free(built[k]); built.Remove(k); }

            float glow = Glow();
            if (Mathf.Abs(glow - glowShown) > 0.004f)
            {
                glowShown = glow;
                glass.SetColor("_EmissionColor", GlowWarm * glow);
                farMat.SetColor("_EmissionColor", GlowWarm * glow);
            }
            bool check = Time.unscaledTime >= nextCheck;
            if (check) nextCheck = Time.unscaledTime + 0.5f;
            foreach (var b in built.Values) Show(b, p, glow, check);
        }

        /// <summary>How brightly the windows shine: nothing by day, a little in the morning, fully from dusk on.</summary>
        float Glow()
        {
            if (dayNight == null && Time.unscaledTime >= nextCheck) dayNight = Object.FindFirstObjectByType<DayNight>();
            return dayNight != null ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.7f, dayNight.Darkness)) : 0f;
        }

        /// <summary>Near or far versions, the yard, the lamps and the smoke, by how far the rider is.</summary>
        void Show(Built b, Vector3 p, float glow, bool check)
        {
            foreach (var h in b.houses)
            {
                float dx = h.at.x - p.x, dz = h.at.z - p.z, d2 = dx * dx + dz * dz;
                float range = h.isNear ? NearRange + NearSlack : NearRange;
                bool near = d2 < range * range, shown = !h.yard || d2 < YardRange * YardRange;
                if (near == h.isNear && shown == h.shown) continue;
                h.isNear = near; h.shown = shown;
                foreach (var r in h.near) if (r != null) r.enabled = near && shown;
                if (h.far != null) h.far.enabled = !near && shown;
            }
            float cx = b.centre.x - p.x, cz = b.centre.z - p.z, c2 = cx * cx + cz * cz;
            bool fence = c2 < (YardRange + 40f) * (YardRange + 40f);
            if (fence != b.fenceShown && b.fence != null) { b.fenceShown = fence; foreach (var r in b.fence) r.enabled = fence; }
            foreach (var l in b.lamps)
            {
                float lx = l.transform.position.x - p.x, lz = l.transform.position.z - p.z;
                bool on = glow > 0.05f && lx * lx + lz * lz < LampRange * LampRange;
                if (l.enabled != on) l.enabled = on;
                if (on) l.intensity = LampPower * glow;
            }
            bool smoking = c2 < SmokeRange * SmokeRange;
            if (smoking != b.smoking)
            {
                b.smoking = smoking;
                foreach (var s in b.smoke) { if (smoking) s.Play(); else s.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            }
            // an invincible rider switches off whatever it runs through (Rider.Smash) and the forest puts its own
            // back; a house is not smashed, so its walls come back once the rider is clear of them
            if (check)
                foreach (var col in b.solid)
                {
                    if (col == null || col.enabled) continue;
                    // (a switched-off collider has no bounds to ask: measure to its box by hand)
                    Vector3 q = col.transform.InverseTransformPoint(p), half = col.size * 0.5f;
                    Vector3 off = new Vector3(Mathf.Max(0f, Mathf.Abs(q.x) - half.x), Mathf.Max(0f, Mathf.Abs(q.y) - half.y), Mathf.Max(0f, Mathf.Abs(q.z) - half.z));
                    if (off.sqrMagnitude > 49f) col.enabled = true;
                }
        }

        void Clear()
        {
            foreach (var b in built.Values) Free(b);
            built.Clear();
        }

        void Free(Built b)
        {
            foreach (var m in b.meshes) Kill(m);
            if (b.root != null) Kill(b.root);
        }

        static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        GameObject Prefab(string name)
        {
            if (prefabs.TryGetValue(name, out var g)) return g;
            g = Resources.Load<GameObject>("Cabins/" + name);
            if (g == null) Debug.LogWarning("Hamlets: Resources/Cabins/" + name + ".fbx is missing");
            prefabs[name] = g;
            return g;
        }

        Built Begin(Hamlet h)
        {
            var b = new Built { plan = h, root = new GameObject("Hamlet " + h.cycle) };
            b.root.transform.SetParent(transform, false);
            float zm = (h.z0 + h.z1) * 0.5f, xm = Slope.Centre(zm) + h.side * (h.near + h.far) * 0.5f;
            b.centre = new Vector3(xm, Slope.Height(xm, zm), zm);
            return b;
        }

        /// <summary>Sets down one more of a hamlet's buildings, then its fence: a piece a frame, so a hamlet coming
        /// into range half a kilometre off never costs a frame of the ride.</summary>
        void Step(Built b)
        {
            var h = b.plan;
            int i = b.next++;
            if (i < h.sites.Length)
            {
                var s = h.sites[i];
                // real lamps: the lamp post's, and the first house's porch light (the rest only glow)
                bool lampHere = s.kind == Lamp || (Kinds[s.kind].house && b.porches == 0);
                if (Place(b, s, i, lampHere) && lampHere && s.kind != Lamp) b.porches++;
            }
            else
            {
                Fence(b, h);
                if (b.fence != null) foreach (var r in b.fence) r.enabled = b.fenceShown;   // (put away already, if it is far off)
            }
        }

        /// <summary>One building: its model set on the snow, painted, made solid, its chimney and lamp added.</summary>
        bool Place(Built b, Site s, int index, bool lamp)
        {
            var kind = Kinds[s.kind];
            var prefab = Prefab(kind.name);
            if (prefab == null) return false;
            var t = new GameObject(kind.name).transform;
            t.SetParent(b.root.transform, false);
            t.SetPositionAndRotation(new Vector3(s.x, Seat(s), s.z), Quaternion.Euler(0f, s.yaw, 0f));
            var model = Instantiate(prefab, t, false).transform;
            model.name = "Model";
            // (keep the importer's own rotation and scale on the root: it's how Blender's units and axes come in)
            model.localPosition = Vector3.zero;
            // the file is made with its front on +Z: check, and turn it round if the axes came in otherwise (as Camp does)
            var front = Find(model, "MARK_front");
            if (front != null && front.TryGetComponent<Renderer>(out var fr))
            {
                var fl = t.InverseTransformPoint(fr.bounds.center); fl.y = 0f;
                float turn = fl.sqrMagnitude > 0.0025f ? Vector3.SignedAngle(fl, Vector3.forward, Vector3.up) : 0f;
                if (Mathf.Abs(turn) > 1f) { Debug.LogWarning("Hamlets: " + kind.name + " came in turned " + turn.ToString("0") + " degrees"); model.RotateAround(t.position, t.up, turn); }
            }
            // (as far off as it is being built it will be its far version, its yard put away: Show sees to it this frame)
            var house = new House { at = t.position, yard = !kind.house, isNear = true, shown = true };
            var near = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("__"))
                {
                    // a helper box: its extents, in the building's own frame
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var mb = mf.sharedMesh.bounds;
                        Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
                        for (int c = 0; c < 8; c++)
                        {
                            var q = t.InverseTransformPoint(r.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                            lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
                        }
                        Helper(b, t, kind, r.name.Substring(0, r.name.IndexOf("__")), lo, hi, lamp);
                    }
                    r.enabled = false;
                    Kill(r.gameObject);
                    continue;
                }
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = MatFor(ms[i] != null ? ms[i].name.Split(' ')[0] : r.name, b.plan.cycle + index);
                r.sharedMaterials = ms;
                bool isFar = r.name.StartsWith("Far");
                bool glassy = r.name.StartsWith("Glass");
                r.shadowCastingMode = isFar || glassy || !kind.house ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                if (isFar) { r.receiveShadows = false; house.far = r; r.enabled = false; }
                else near.Add(r);
            }
            house.near = near.ToArray();
            b.houses.Add(house);
            return true;
        }

        static Transform Find(Transform t, string prefix)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name.StartsWith(prefix)) return c;
            return null;
        }

        /// <summary>What one of a model's helper boxes means (see Tools/MakeCabins.py).</summary>
        void Helper(Built b, Transform t, Kind kind, string name, Vector3 lo, Vector3 hi, bool lamp)
        {
            Vector3 c = (lo + hi) * 0.5f, size = hi - lo;
            if (name.StartsWith("COL_"))
            {
                var g = new GameObject(name);
                g.transform.SetParent(t, false); g.transform.localPosition = c;
                var box = g.AddComponent<BoxCollider>(); box.size = size;
                g.AddComponent<Obstacle>().label = kind.label;
                b.solid.Add(box);
            }
            else if (name == "FX_smoke")
            {
                var ps = Smoke(t, c);
                b.smoke.Add(ps);
                if (b.smoking) ps.Play();   // (not while the hamlet is too far off to see it)
            }
            else if (name.StartsWith("LIGHT_") && lamp)
            {
                var l = new GameObject("Lamp").AddComponent<Light>();
                l.transform.SetParent(t, false); l.transform.localPosition = c;
                l.type = LightType.Point; l.color = new Color(1f, 0.8f, 0.6f); l.range = 13f; l.intensity = 0f; l.shadows = LightShadows.None;
                l.enabled = false;
                b.lamps.Add(l);
            }
        }

        /// <summary>A thin thread of chimney smoke, leaning with the wind (as the camp's fire does).</summary>
        ParticleSystem Smoke(Transform t, Vector3 at)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(t, false); go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startColor = new Color(0.84f, 0.86f, 0.9f, 0.4f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24; main.playOnAwake = false;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.12f;
            var em = ps.emission; em.rateOverTime = 3.5f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.3f, 0.6f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.45f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (smokeMat != null) r.sharedMaterial = smokeMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        // ---------------------------------------------------------------- the fence along the piste

        const float FenceStep = 3f, FenceGap = 6f, PostTop = 1.2f;

        /// <summary>
        /// Posts every 3 m and two rails between them, following the snow, with snow on the posts and along the top
        /// rail: two meshes for the whole fence (wood, snow). It breaks at the gap and wherever the ground steps
        /// away under it (a jump cliff's rock band). Each span gives way at a touch (a Breakable): its rails and caps
        /// are taken out of the meshes, and so is a post once neither side of it has a rail left; all of it is put
        /// back for the next run. So a standing fence is still two draw calls however many spans it has.
        /// </summary>
        void Fence(Built b, Hamlet h)
        {
            var wood = new Shape(); var caps = new Shape();
            var holder = new GameObject("Fence").transform;
            holder.SetParent(b.root.transform, false);
            var parts = new FenceParts { wood = wood, caps = caps, mat = darkWood };
            bool have = false; Vector3 last = Vector3.zero;
            int lastPost = -1;
            for (float z = h.fenceZ0; z <= h.fenceZ1; z += FenceStep)
            {
                float x = Slope.Centre(z) + h.side * (Slope.PisteHalf + FenceOff);
                var p = new Vector3(x, Slope.DrawnHeight(x, z), z);
                bool gap = Mathf.Abs(z - h.gapZ) < FenceGap * 0.5f || Course.At(z) != Course.Section.Cruise || Cliffs.OnJumpFace(x, z);
                if (gap) { have = false; continue; }
                int post = parts.posts.Count;
                var postPiece = new Breakable.Plank { at = p + Vector3.up * (PostTop * 0.5f - 0.3f), rot = Quaternion.identity, size = new Vector3(0.15f, PostTop + 0.6f, 0.15f), mat = darkWood };
                parts.posts.Add(new FenceParts.Part { wood = wood.Box(postPiece.at, postPiece.rot, postPiece.size), caps = caps.Box(p + Vector3.up * (PostTop + 0.06f), Quaternion.identity, new Vector3(0.2f, 0.12f, 0.2f)), plank = postPiece });
                if (have && Mathf.Abs(p.y - last.y) < FenceStep * 1.4f)
                {
                    Vector3 d = p - last; float len = d.magnitude;
                    var rot = Quaternion.LookRotation(d / len, Vector3.up);
                    Vector3 mid = (p + last) * 0.5f;
                    var top = new Breakable.Plank { at = mid + Vector3.up * 1.0f, rot = rot, size = new Vector3(0.07f, 0.13f, len), mat = darkWood };
                    var low = new Breakable.Plank { at = mid + Vector3.up * 0.55f, rot = rot, size = new Vector3(0.07f, 0.13f, len), mat = darkWood };
                    int w0 = wood.Box(top.at, top.rot, top.size); wood.Box(low.at, low.rot, low.size);
                    int c0 = caps.Box(mid + Vector3.up * 1.09f, rot, new Vector3(0.1f, 0.06f, len - 0.25f));
                    int span = parts.spans.Count;
                    parts.spans.Add(new FenceParts.Span { wood = w0, woodBoxes = 2, caps = c0, a = lastPost, b = post, top = top, low = low });
                    // (a little thicker than the rails, so brushing the fence with the board takes it down)
                    var g = new GameObject("FenceSpan");
                    g.transform.SetParent(holder, false);
                    g.transform.SetPositionAndRotation(mid + Vector3.up * 0.6f, rot);
                    Breakable.Span(g.transform, new Vector3(0.5f, 1.3f, len), () => parts.Break(span), () => parts.Mend(span));
                }
                have = true; last = p; lastPost = post;
            }
            var rw = wood.Renderer(holder, "FenceWood", darkWood, b.meshes);
            var rs = caps.Renderer(holder, "FenceSnow", snow, b.meshes);
            b.fence = rw != null && rs != null ? new[] { rw, rs } : rw != null ? new[] { rw } : null;
            parts.Ready(rw, rs);
        }

        /// <summary>
        /// Which boxes of a fence's two meshes belong to which post and span, so a broken span can be taken out of
        /// them (its corners all moved onto one point: nothing left to draw) and put back.
        /// </summary>
        class FenceParts
        {
            public struct Part { public int wood, caps; public Breakable.Plank plank; }
            public struct Span { public int wood, woodBoxes, caps, a, b; public Breakable.Plank top, low; }
            public Shape wood, caps; public Material mat;
            public readonly List<Part> posts = new List<Part>();
            public readonly List<Span> spans = new List<Span>();
            bool[] broken; bool[] postGone;
            Mesh woodMesh, capsMesh;
            Vector3[] woodFull, capsFull, woodNow, capsNow;

            public void Ready(Renderer rw, Renderer rs)
            {
                broken = new bool[spans.Count]; postGone = new bool[posts.Count];
                woodMesh = rw != null ? rw.GetComponent<MeshFilter>().sharedMesh : null;
                capsMesh = rs != null ? rs.GetComponent<MeshFilter>().sharedMesh : null;
                if (woodMesh != null) { woodFull = woodMesh.vertices; woodNow = (Vector3[])woodFull.Clone(); woodMesh.MarkDynamic(); }
                if (capsMesh != null) { capsFull = capsMesh.vertices; capsNow = (Vector3[])capsFull.Clone(); capsMesh.MarkDynamic(); }
            }

            // a post stands while it has a rail on either side still up (or never had one)
            bool PostStands(int post)
            {
                bool any = false;
                for (int i = 0; i < spans.Count; i++)
                    if (spans[i].a == post || spans[i].b == post) { any = true; if (!broken[i]) return true; }
                return !any;
            }

            public List<Breakable.Plank> Break(int span)
            {
                if (broken == null || broken[span]) return null;
                broken[span] = true;
                var s = spans[span];
                var planks = new List<Breakable.Plank> { s.top, s.low };
                Hide(woodNow, s.wood, s.woodBoxes); Hide(capsNow, s.caps, 1);
                foreach (int post in new[] { s.a, s.b })
                {
                    if (post < 0 || postGone[post] || PostStands(post)) continue;
                    postGone[post] = true;
                    Hide(woodNow, posts[post].wood, 1); Hide(capsNow, posts[post].caps, 1);
                    planks.Add(posts[post].plank);
                }
                Upload();
                return planks;
            }

            public void Mend(int span)
            {
                if (broken == null || !broken[span]) return;
                broken[span] = false;
                var s = spans[span];
                Show(woodNow, woodFull, s.wood, s.woodBoxes); Show(capsNow, capsFull, s.caps, 1);
                foreach (int post in new[] { s.a, s.b })
                    if (post >= 0 && postGone[post]) { postGone[post] = false; Show(woodNow, woodFull, posts[post].wood, 1); Show(capsNow, capsFull, posts[post].caps, 1); }
                Upload();
            }

            const int PerBox = 24;
            static void Hide(Vector3[] v, int box, int count)
            {
                if (v == null) return;
                int i0 = box * PerBox;
                for (int i = i0; i < i0 + count * PerBox && i < v.Length; i++) v[i] = v[i0];
            }
            static void Show(Vector3[] v, Vector3[] full, int box, int count)
            {
                if (v == null) return;
                System.Array.Copy(full, box * PerBox, v, box * PerBox, Mathf.Min(count * PerBox, v.Length - box * PerBox));
            }
            void Upload()
            {
                if (woodMesh != null) woodMesh.vertices = woodNow;
                if (capsMesh != null) capsMesh.vertices = capsNow;
            }
        }

        /// <summary>Boxes gathered into one mesh.</summary>
        class Shape
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> tri = new List<int>();
            static readonly Vector3[] Faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

            /// <summary>Adds a box; returns its number (each box is 24 corners, in order).</summary>
            public int Box(Vector3 centre, Quaternion rot, Vector3 size)
            {
                int box = v.Count / 24;
                for (int f = 0; f < 6; f++)
                {
                    Vector3 nf = Faces[f];
                    // two axes across the face with a x b = nf, so corners 0, 1, 2 go round clockwise seen from outside (Unity's front)
                    Vector3 a = f < 2 ? Vector3.up : f < 4 ? Vector3.forward : Vector3.right;
                    Vector3 bb = Vector3.Cross(nf, a);
                    int s = v.Count;
                    for (int c = 0; c < 4; c++)
                    {
                        float sa = c == 0 || c == 3 ? -0.5f : 0.5f, sb = c < 2 ? -0.5f : 0.5f;
                        Vector3 local = Vector3.Scale(nf * 0.5f + a * sa + bb * sb, size);
                        v.Add(centre + rot * local);
                        n.Add(rot * nf);
                        // the wood's grain along the longer way of the face
                        float ua = Vector3.Dot(local, a), ub = Vector3.Dot(local, bb);
                        uv.Add(new Vector2((ua + Vector3.Dot(centre, Vector3.one) * 0.37f) * 0.6f, ub * 0.6f));
                    }
                    tri.Add(s); tri.Add(s + 1); tri.Add(s + 2); tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
                }
                return box;
            }

            public Renderer Renderer(Transform parent, string name, Material m, List<Mesh> made)
            {
                if (v.Count == 0) return null;
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
                mesh.RecalculateBounds();
                made.Add(mesh);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return r;
            }
        }
    }
}
