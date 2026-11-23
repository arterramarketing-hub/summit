using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// Night skiing: floodlights on poles down the piste (zig-zagging from side to side about every 46 m, as on a lit
    /// hill), towers beside the kickers and the park's features aimed back up at the air over the lips, lamps on the
    /// lift stations and every other lift tower, a pole at each hamlet's gap in its fence and two at the drop-in gate.
    /// They come on with the dark (DayNight.Darkness) and are off by day, when only the poles stand there.
    ///
    /// None of it is real-time light except two lamps: each lamp is drawn as
    ///   - a pool: a mesh lying on the snow under the lamp, on the terrain's own 1.5 m grid and triangles, drawn
    ///     additively (Resources/NightLights/LightPool.shader). The shader works out the lamp's light per pixel the
    ///     way URP lights a spot (1/d^2 with its range falloff, the cone's soft edge, the snow's facing), so the
    ///     runs read as islands of white light with a soft edge into the dark, and it sparkles where it is lit;
    ///   - a haze: a soft additive cone of light in the air (LightCone.shader), faint in clear air and thick in falling
    ///     snow and fog (Weather), brightest looking into the beam;
    ///   - a glare: a billboard on the lamp itself, a hot core and a soft bloom, blazing when it faces the camera.
    /// The falling snow is lit by the eight lamps nearest the camera in its own shader (LitFlake.shader: a small
    /// array of lamps, per vertex): flakes in a beam light up white and twinkle, flakes outside it stay dark.
    /// The two lamps nearest the rider are also real (unshadowed) spot lights, so the rider, the kickers, the trees
    /// and the people in a beam are lit or silhouetted by it; their pools hand the snow over to them (_NLReal).
    ///
    /// Lamps are laid out once per mountain (a pure function of the seed, the course, the lifts and the hamlets) and
    /// drawn in 200 m bins down the hill: each bin's pools, cones, glare and poles are four meshes, built on a worker
    /// thread as the rider comes within 700 m of them and dropped behind. Per frame: a few property sets.
    /// </summary>
    [DefaultExecutionOrder(310)]
    public class NightLights : MonoBehaviour
    {
        public enum Kind { Piste, Jump, Feature, Station, Tower, Hamlet, Camp }

        public struct Lamp
        {
            public Kind kind;
            public int id;
            public Vector3 foot, head, dir;   // the pole's foot on the snow, the lens, and where it points
            public float outer, inner;       // the cone's full angles (degrees), as a URP spot's
            public float range, power;       // URP's spot: power * colour / d^2 * (1 - (d / range)^4)^2
            public bool pole;                // on its own pole (false: on a lift, or a second head on a pole)
            public float CosOuter => Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad);
            public float CosInner => Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>The lamps' light (linear): a cold LED white.</summary>
        public static readonly Color LightColour = new Color(0.86f, 0.93f, 1f);
        /// <summary>The snow's albedo as the pool shader takes it (Snow.mat's colour; URP's diffuse keeps 0.96 of it).</summary>
        const float SnowAlbedo = 0.86f;

        public const float Spacing = 46f;                     // between the piste's poles (they alternate sides)
        public const float PoleOff = Slope.PisteHalf + 5f;    // a pole's foot from the piste's centre line
        public const float PoleH = 13f, BinLen = 200f;
        public const float Ahead = 700f, Behind = 150f;       // bins drawn this far down and up the hill from the rider
        public const float PoleRoom = 3f;                     // no tree within this of a pole (Forest asks Taken)

        /// <summary>The test tools switch the lights off for a before/after.</summary>
        public static bool Disabled;
        /// <summary>The test tools force them on (1) or off (0) whatever the clock says (-1: the clock).</summary>
        public static float TestOn = -1f;

        // ---------------------------------------------------------------- the layout

        static Lamp[] lamps = new Lamp[0];
        static Vector2[] poles = new Vector2[0];   // the poles' feet (x, z), by z
        static int laidFor = int.MinValue;
        static bool laying;

        /// <summary>Every lamp on today's mountain, by z (laid out the first time it is asked for).</summary>
        public static Lamp[] Lamps { get { Lay(); return lamps; } }

        /// <summary>True within PoleRoom of a pole: the forest leaves that ground alone.</summary>
        public static bool Taken(float x, float z)
        {
            Lay();
            int lo = 0, hi = poles.Length;
            while (lo < hi) { int m = (lo + hi) >> 1; if (poles[m].y < z - PoleRoom) lo = m + 1; else hi = m; }
            for (int i = lo; i < poles.Length && poles[i].y <= z + PoleRoom; i++)
            {
                float dx = poles[i].x - x, dz = poles[i].y - z;
                if (dx * dx + dz * dz < PoleRoom * PoleRoom) return true;
            }
            return false;
        }

        static void Lay()
        {
            if (laidFor == Slope.Seed || laying) return;
            laying = true;
            laidFor = Slope.Seed;
            try
            {
                var list = new List<Lamp>();
                Fails.Clear();
                LayCamp(list);
                LayJumps(list);
                LayFeatures(list);
                LayHamlets(list);
                LayPiste(list);
                LayLifts(list);
                list.Sort((a, b) => a.head.z.CompareTo(b.head.z));
                var feet = new List<Vector2>();
                for (int i = 0; i < list.Count; i++)
                {
                    var l = list[i]; l.id = i; list[i] = l;
                    if (l.pole) feet.Add(new Vector2(l.foot.x, l.foot.z));
                }
                feet.Sort((a, b) => a.y.CompareTo(b.y));
                lamps = list.ToArray();
                poles = feet.ToArray();
            }
            finally { laying = false; }
        }

        /// <summary>Somewhere a pole can't stand: the pad, the course's features and crevasses, a lift's swath, a hamlet.</summary>
        static bool Blocked(float x, float z, bool hamlet = false)
        {
            if (Slope.OnPad(x, z) || Course.Keep(x, z, 2f) || LiftLine.Taken(x, z, 4f)) return true;
            if (!hamlet && Hamlets.Taken(x, z)) return true;
            if (Mathf.Abs(Slope.Kicker(x, z)) > 0.01f) return true;
            return false;
        }

        static bool PoleNear(List<Lamp> list, float x, float z, float r)
        {
            foreach (var l in list)
                if (l.pole && (l.foot.x - x) * (l.foot.x - x) + (l.foot.z - z) * (l.foot.z - z) < r * r) return true;
            return false;
        }

        /// <summary>A lamp at head aimed at target, its power set so the snow there is lit to `want` (URP's units).</summary>
        static Lamp Aim(Kind kind, Vector3 foot, Vector3 head, Vector3 target, float outer, float inner, float range, float want, bool pole)
        {
            var l = new Lamp { kind = kind, foot = foot, head = head, outer = outer, inner = inner, range = range, pole = pole };
            l.dir = (target - head).normalized;
            // set so the brightest snow under it is lit to `want`: rays through its cone (the middle, and two rings
            // round it) marched to the snow, the light where each lands worked out as the pool shader works it out
            float best = 0f;
            Vector3 up = Mathf.Abs(l.dir.y) > 0.95f ? Vector3.forward : Vector3.up;
            Vector3 ax = Vector3.Cross(l.dir, up).normalized, ay = Vector3.Cross(ax, l.dir);
            float co = l.CosOuter, ci = l.CosInner;
            for (int ring = 0; ring < 3; ring++)
            {
                float off = ring == 0 ? 0f : (ring == 1 ? 0.5f * inner : 0.25f * (inner + outer)) * 0.5f * Mathf.Deg2Rad;
                int n = ring == 0 ? 1 : 8;
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2f / n;
                    Vector3 d = (l.dir * Mathf.Cos(off) + (ax * Mathf.Cos(a) + ay * Mathf.Sin(a)) * Mathf.Sin(off)).normalized;
                    for (float t = 1f; t < range; t += 1.5f)
                    {
                        Vector3 p = head + d * t;
                        if (p.y >= Slope.Height(p.x, p.z)) continue;
                        float d2 = Mathf.Max(t * t, 1f), f = d2 / (range * range);
                        float sm = Mathf.Clamp01(1f - f * f); sm *= sm;
                        float ang = Mathf.Clamp01((Vector3.Dot(d, l.dir) - co) / Mathf.Max(ci - co, 0.001f));
                        float ndl = Mathf.Max(0f, Vector3.Dot(Slope.Normal(p.x, p.z), -d));
                        best = Mathf.Max(best, sm * ang * ang * ndl / d2);
                        break;
                    }
                }
            }
            l.power = want / (SnowAlbedo * 0.96f * Mathf.Max(best, 1e-5f));
            return l;
        }

        static bool TryPole(List<Lamp> list, Kind kind, float x, float z, float h, Vector3 target, float outer, float inner, float range, float want, bool hamlet = false)
        {
            if (Blocked(x, z, hamlet)) { Fail("blocked"); return false; }
            float fy = Slope.Height(x, z);
            // the foot near the piste's own level, not down a drop or up a bank
            float cz = Slope.Centre(z);
            if (Mathf.Abs(fy - Slope.Height(cz + Mathf.Clamp(x - cz, -Slope.PisteHalf, Slope.PisteHalf), z)) > 7f) { Fail("bank"); return false; }
            if (PoleNear(list, x, z, 11f)) { Fail("near"); return false; }
            var foot = new Vector3(x, fy, z);
            list.Add(Aim(kind, foot, foot + Vector3.up * h, target, outer, inner, range, want, true));
            return true;
        }

        /// <summary>Why poles were not put up, by reason (the test tools log it).</summary>
        public static readonly Dictionary<string, int> Fails = new Dictionary<string, int>();
        static void Fail(string why) { Fails.TryGetValue(why, out int n); Fails[why] = n + 1; }

        /// <summary>Where a lamp's beam (its middle) meets the snow, or its range's end in the air.</summary>
        public static Vector3 Hit(in Lamp l, out bool landed)
        {
            for (float d = 1f; d < l.range; d += 0.75f)
            {
                Vector3 p = l.head + l.dir * d;
                if (p.y < Slope.Height(p.x, p.z)) { landed = true; return p; }
            }
            landed = false;
            return l.head + l.dir * l.range * 0.8f;
        }

        static Vector3 Ground(float x, float z) => new Vector3(x, Slope.Height(x, z), z);

        static void LayPiste(List<Lamp> list)
        {
            int i = 0;
            for (float z = 26f; z < Slope.FinishZ - 30f; z += Spacing, i++)
            {
                var sec = Course.At(z);
                if (sec == Course.Section.Danger) { Fail("wild"); continue; }   // the glacier stays dark
                if (sec == Course.Section.Band || CliffBand.Near(z, 20f, out _)) { Fail("band"); continue; }
                if (sec == Course.Section.Bowl || Bowl.Near(z, 10f, out _)) { Fail("bowl"); continue; }   // (the bowl stays dark: its walls are far off)   // (a cliff band's traverse: no poles on its bench)
                if (Cliffs.JumpNear(z, 30f, out _)) { Fail("cliff"); continue; }
                float side = (i & 1) == 0 ? 1f : -1f;
                for (int k = 0; k < 2; k++, side = -side)
                {
                    float x = Slope.Centre(z) + side * (PoleOff - Slope.PisteHalf + Slope.HalfAt(z));   // (out past the run's own edge: wide below a cliff band)
                    // aimed across the piste and a little back up it, toward the riders coming down
                    float tz = z - 7f;
                    var target = Ground(Slope.Centre(tz) - side * 2f, tz);
                    if (TryPole(list, Kind.Piste, x, z, PoleH, target, 130f, 22f, 50f, 0.9f)) break;
                }
            }
        }

        static void LayJumps(List<Lamp> list)
        {
            // the cruise kickers (Slope.Kicker's own layout): a tower beside the landing aimed back up at the air
            // over the lip, so a rider in the air crosses the beam with the light coming at the camera
            for (int cell = 0; ; cell++)
            {
                float z0 = 60f + cell * Slope.KickPeriod + 30f;
                if (z0 > Slope.FinishZ - 260f) break;
                if (Slope.Hash01(cell, 3, 9) < 0.25f) continue;
                if (Course.At(z0) != Course.Section.Cruise || Course.At(z0 + Slope.KickRamp + Slope.KickDrop) != Course.Section.Cruise) continue;
                if (Cliffs.JumpNear(z0, 30f, out _)) continue;
                float off = (Slope.Hash01(cell, 4, 9) - 0.5f) * 18f;
                float cx = Slope.Centre(z0) + off;
                Jump(list, cx, z0 + Slope.KickRamp, off >= 0f ? -1f : 1f, false);
            }
            // the park's two big kickers: towers both sides
            for (int k = 0; k <= Course.CycleOf(Slope.FinishZ); k++)
            {
                float zs = Course.First + k * Course.Cycle + Course.ParkAt;
                foreach (float at in Course.ParkKickers)
                {
                    float z0 = zs + at;
                    if (Course.At(z0) != Course.Section.Park || z0 > Slope.FinishZ - 260f) continue;
                    float lipZ = z0 + Slope.KickRamp * 1.15f;
                    Jump(list, Slope.Centre(z0), lipZ, 1f, true);
                    Jump(list, Slope.Centre(z0), lipZ, -1f, true);
                }
            }
        }

        static void Jump(List<Lamp> list, float cx, float lipZ, float side, bool park)
        {
            Vector3 lip = Ground(cx, lipZ);
            // the air a rider flies through, a little past and over the lip
            Vector3 air = lip + new Vector3(0f, park ? 3.5f : 2.8f, 4f);
            float z = lipZ + (park ? 18f : 15f);
            float x = Slope.Centre(z) + side * (Slope.PisteHalf + 4f);
            if (TryPole(list, Kind.Jump, x, z, park ? 16f : 15f, air, 90f, 18f, 52f, park ? 1.1f : 1.0f)) return;
            x = Slope.Centre(z) - side * (Slope.PisteHalf + 4f);
            TryPole(list, Kind.Jump, x, z, park ? 16f : 15f, air, 90f, 18f, 52f, park ? 1.1f : 1.0f);
        }

        static void LayFeatures(List<Lamp> list)
        {
            for (int k = 0; k <= Course.CycleOf(Slope.FinishZ); k++)
            {
                var feats = Course.FeaturesOf(k);
                if (feats == null) continue;
                foreach (var f in feats)
                {
                    Vector3 mid = (f.a + f.b) * 0.5f;
                    if (mid.z > Slope.FinishZ - 260f) continue;
                    float side = mid.x >= Slope.Centre(mid.z) ? 1f : -1f;
                    float z = mid.z + 8f;
                    float x = Slope.Centre(z) + side * (Slope.PisteHalf + 3.5f);
                    TryPole(list, Kind.Feature, x, z, 11f, mid + Vector3.up * 1.2f, 120f, 24f, 40f, 0.85f);
                }
            }
        }

        static void LayHamlets(List<Lamp> list)
        {
            for (int c = 0; c <= Hamlets.LastCycle; c++)
            {
                var h = Hamlets.Of(c);
                if (h == null) continue;
                float z = h.gapZ + 3f;
                float x = Slope.Centre(z) + h.side * (Slope.PisteHalf + 1.8f);
                var toPiste = Ground(Slope.Centre(z - 4f), z - 4f);
                if (!TryPole(list, Kind.Hamlet, x, z, 11f, toPiste, 130f, 24f, 42f, 0.75f, true)) continue;
                // a second head on the same pole, aimed out over the yard toward the houses
                var p = list[list.Count - 1];
                float yx = Slope.Centre(z) + h.side * (Slope.PisteHalf + Mathf.Max(10f, h.near * 0.5f + 8f));
                var yard = Ground(yx, z + 2f);
                var l2 = Aim(Kind.Hamlet, p.head, p.head + h.side * Vector3.right * 0.6f, yard, 100f, 34f, 30f, 0.6f, false);
                l2.foot = l2.head;
                list.Add(l2);
            }
        }

        static void LayCamp(List<Lamp> list)
        {
            // two tall floods either side of the drop-in gate, lighting the pad and the start
            float cx = Slope.Centre(Slope.PadLip);
            float z = Slope.GateZ + 4f;
            foreach (float side in new[] { -1f, 1f })
            {
                float x = cx + side * (Slope.PadHalf + 2.5f);
                var foot = Ground(x, z);
                var target = Ground(cx - side * 3f, Slope.GateZ + 16f);
                list.Add(Aim(Kind.Camp, foot, foot + Vector3.up * 12f, target, 130f, 24f, 48f, 0.75f, true));
            }
        }

        static void LayLifts(List<Lamp> list)
        {
            foreach (var l in LiftLine.Lifts)
            {
                if (l == null || l.head == null) continue;
                // a flood on each station's roof, over the side toward the piste: the loading and the unloading
                foreach (var st in new[] { l.top, l.bottom })
                {
                    float toward = Slope.Centre(st.z) > st.x ? 1f : -1f;
                    var head = st + Vector3.up * (LiftLine.StationCable + 3f) + Vector3.right * toward * 2f;
                    var target = Ground(st.x + toward * 13f, st.z + 3f);
                    var lamp = Aim(Kind.Station, head, head, target, 110f, 40f, 30f, 0.85f, false);
                    list.Add(lamp);
                }
                // every other tower: a lamp under its head shining down the mast onto the snow at its foot
                for (int i = 0; i < l.head.Length; i += 2)
                {
                    if (l.foot == null || i >= l.foot.Length) break;
                    Vector3 head = l.head[i] - l.Axis * 1.6f;
                    float h = head.y - l.foot[i].y;
                    if (h < 5f) continue;
                    var target = l.foot[i] + l.dir * 3f;
                    target.y = Slope.Height(target.x, target.z);
                    list.Add(Aim(Kind.Tower, head, head, target, 96f, 34f, h + 16f, 0.7f, false));
                }
            }
        }

        // ---------------------------------------------------------------- set up

        Rider rider;
        Camera cam;
        DayNight dayNight;
        Atmosphere atmo;
        Camp camp;
        Material spray, poleMat, glowMat, poolMat, coneMat, flakeMat;
        Texture2D noiseTex;
        readonly Dictionary<int, Bin> bins = new Dictionary<int, Bin>();
        readonly Light[] real = new Light[2];
        readonly List<Renderer> flakeRenderers = new List<Renderer>();
        Weather weatherSeen;
        int builtFor = int.MinValue;

        public static NightLights Instance { get; private set; }

        /// <summary>Called by the game once the day/night and the atmosphere are set up (SummitGame.Awake).</summary>
        public static NightLights Build(Rider r, Camera camera, DayNight dn, Atmosphere a, Material sprayMat)
        {
            var go = new GameObject("NightLights");
            var nl = go.AddComponent<NightLights>();
            nl.rider = r; nl.cam = camera; nl.dayNight = dn; nl.atmo = a; nl.spray = sprayMat;
            nl.camp = Object.FindFirstObjectByType<Camp>();
            nl.Setup();
            Instance = nl;
            return nl;
        }

        static Shader Find(string name)
        {
            var s = Resources.Load<Shader>("NightLights/" + name);
            if (s == null) Debug.LogWarning("NightLights: no shader " + name);
            return s;
        }

        static Material Mat(Shader s, string name, int queue)
        {
            if (s == null) return null;
            return new Material(s) { name = name, renderQueue = queue };
        }

        void Setup()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            poleMat = new Material(lit) { name = "LightPole", enableInstancing = true };
            poleMat.SetColor("_BaseColor", new Color(0.34f, 0.35f, 0.37f));
            poleMat.SetFloat("_Smoothness", 0.45f); poleMat.SetFloat("_Metallic", 0.6f);
            noiseTex = Noise();
            // the pools straight after the opaque world (before the trees' alpha cards, the snowfall and the haze)
            poolMat = Mat(Find("LightPool"), "LightPool", (int)RenderQueue.GeometryLast + 1);
            coneMat = Mat(Find("LightCone"), "LightCone", (int)RenderQueue.Transparent + 40);
            if (coneMat != null) coneMat.SetTexture("_Noise", noiseTex);
            glowMat = Mat(Find("LampGlare"), "LampGlare", (int)RenderQueue.Transparent + 60);
            var fs = Find("LitFlake");
            if (fs != null && spray != null)
            {
                flakeMat = new Material(fs) { name = "LitFlake", renderQueue = spray.renderQueue };
                if (spray.HasProperty("_BaseMap")) flakeMat.SetTexture("_BaseMap", spray.GetTexture("_BaseMap"));
                flakeMat.SetColor("_BaseColor", spray.GetColor("_BaseColor"));
            }
            if (flakeMat != null) BuildBeamSnow();
            for (int i = 0; i < real.Length; i++)
            {
                var l = new GameObject("FloodLight" + i).AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.type = LightType.Spot; l.shadows = LightShadows.None; l.color = LightColour.gamma;
                l.renderMode = LightRenderMode.ForcePixel;
                l.enabled = false;
                real[i] = l;
            }
            DayNight.Applied += OnApplied;
            Slope.Changing += Settle;
            Shader.SetGlobalVector(RealId, new Vector4(-1f, 0f, -1f, 0f));
            Shader.SetGlobalFloat(OnId, 0f);
        }

        void OnDestroy()
        {
            DayNight.Applied -= OnApplied;
            Slope.Changing -= Settle;
            Settle();
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat(OnId, 0f);
            Shader.SetGlobalInt(CountId, 0);
        }

        /// <summary>A small tiling value noise (the haze's grain of falling snow).</summary>
        static Texture2D Noise()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.R8, true) { name = "NightLightsNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new byte[n * n];
            var rnd = new System.Random(7);
            var g = new float[16 * 16];
            for (int i = 0; i < g.Length; i++) g[i] = (float)rnd.NextDouble();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0f, a = 0.6f;
                    for (int o = 0, s = 4; o < 3; o++, s *= 2, a *= 0.5f)
                    {
                        float fx = x * s / (float)n, fy = y * s / (float)n;
                        int ix = (int)fx, iy = (int)fy; float tx = fx - ix, ty = fy - iy;
                        tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
                        float G(int xx, int yy) => g[(yy % s) * 16 + (xx % s)];
                        v += a * Mathf.Lerp(Mathf.Lerp(G(ix, iy), G(ix + 1, iy), tx), Mathf.Lerp(G(ix, iy + 1), G(ix + 1, iy + 1), tx), ty);
                    }
                    px[y * n + x] = (byte)Mathf.Clamp(v / 1.05f * 255f, 0f, 255f);
                }
            t.SetPixelData(px, 0); t.Apply(true, true);
            return t;
        }

        // ---------------------------------------------------------------- bins

        class Bin
        {
            public int key;
            public GameObject root;
            public Work work;
            public bool ready;
            public MeshRenderer pole, glow, pool, cone;
            public readonly List<Mesh> meshes = new List<Mesh>();
        }

        /// <summary>A bin's meshes, worked out off the main thread (the mountain is a pure function: Slope).</summary>
        class Work
        {
            public Lamp[] lamps;
            public volatile bool done;
            public System.Exception error;
            public readonly MeshData pole = new MeshData(), glow = new MeshData(), pool = new MeshData(), cone = new MeshData();
            public void Run()
            {
                try { foreach (var l in lamps) { Pole(pole, l); Glare(glow, l); Pool(pool, l); Cone(cone, l); } }
                catch (System.Exception e) { error = e; }
                done = true;
            }
        }

        class MeshData
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector4> u0 = new List<Vector4>(), u1 = new List<Vector4>(), u2 = new List<Vector4>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();
            public Mesh Make(string name)
            {
                if (v.Count == 0) return null;
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                if (n.Count == v.Count) m.SetNormals(n);
                if (c.Count == v.Count) m.SetColors(c);
                if (u0.Count == v.Count) m.SetUVs(0, u0);
                if (u1.Count == v.Count) m.SetUVs(1, u1);
                if (u2.Count == v.Count) m.SetUVs(2, u2);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static readonly WaitCallback runOnWorker = o => ((Work)o).Run();

        void Stream(bool now)
        {
            if (Disabled) { foreach (var b in bins.Values) Free(b); bins.Clear(); return; }
            if (builtFor != Slope.Seed) { Settle(); builtFor = Slope.Seed; }
            float z = rider != null ? rider.transform.position.z : 0f;
            int k0 = Mathf.FloorToInt((z - Behind) / BinLen), k1 = Mathf.FloorToInt((z + Ahead) / BinLen);
            drop.Clear();
            foreach (var kv in bins) if (kv.Key < k0 - 1 || kv.Key > k1 + 1) drop.Add(kv.Key);
            foreach (int k in drop) { Free(bins[k]); bins.Remove(k); }
            var all = Lamps;
            for (int k = k0; k <= k1; k++)
            {
                if (bins.ContainsKey(k)) continue;
                var list = new List<Lamp>();
                float lo = k * BinLen, hi = lo + BinLen;
                foreach (var l in all) if (l.head.z >= lo && l.head.z < hi) list.Add(l);
                var b = new Bin { key = k, work = new Work { lamps = list.ToArray() } };
                bins[k] = b;
                if (now) b.work.Run(); else ThreadPool.QueueUserWorkItem(runOnWorker, b.work);
            }
            // finished work into the scene: all of it now, or one bin a frame
            foreach (var b in bins.Values)
            {
                if (b.ready || !b.work.done) continue;
                Land(b);
                if (!now) break;
            }
        }
        readonly List<int> drop = new List<int>();

        void Land(Bin b)
        {
            b.ready = true;
            var w = b.work;
            if (w.error != null) { Debug.LogException(w.error); return; }
            b.root = new GameObject("Lights " + b.key);
            b.root.transform.SetParent(transform, false);
            b.pole = Part(b, w.pole, "Poles", poleMat, true);
            b.glow = Part(b, w.glow, "Glare", glowMat, false);
            b.pool = Part(b, w.pool, "Pools", poolMat, false);
            b.cone = Part(b, w.cone, "Haze", coneMat, false);
            // the poles are solid (the lamps on lifts are the lifts' own)
            foreach (var l in w.lamps)
            {
                if (!l.pole) continue;
                var g = new GameObject("LightPole");
                g.transform.SetParent(b.root.transform, false);
                g.transform.position = l.foot + Vector3.up * ((l.head.y - l.foot.y) * 0.5f);
                var cc = g.AddComponent<CapsuleCollider>();
                cc.radius = 0.2f; cc.height = l.head.y - l.foot.y + 1f;
                g.AddComponent<Obstacle>().label = "Light Pole";
            }
            b.work = new Work { lamps = w.lamps, done = true };   // (its lists go: only the lamps are kept)
            lastOn = -1f;   // (show or hide the new bin's light at once)
        }

        MeshRenderer Part(Bin b, MeshData d, string name, Material m, bool shadows)
        {
            if (m == null) return null;
            var mesh = d.Make(name);
            if (mesh == null) return null;
            b.meshes.Add(mesh);
            var g = new GameObject(name);
            g.transform.SetParent(b.root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = shadows ? ReflectionProbeUsage.BlendProbes : ReflectionProbeUsage.Off;
            return mr;
        }

        void Free(Bin b)
        {
            if (!b.ready && b.work != null) while (!b.work.done) Thread.Sleep(0);
            foreach (var m in b.meshes) Kill(m);
            if (b.root != null) Kill(b.root);
        }

        /// <summary>A new mountain: wait for the workers, then everything goes (it is built again for the new one).</summary>
        void Settle()
        {
            foreach (var b in bins.Values) Free(b);
            bins.Clear();
        }

        static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        /// <summary>Everything round the rider built now (the test tools; a new mountain's first frame).</summary>
        public void Prewarm() { Stream(true); OnApplied(dayNight); }

        // ---------------------------------------------------------------- per frame

        static readonly int BeamId = Shader.PropertyToID("_NLBeam"), GainId = Shader.PropertyToID("_NLGain");
        const float EveningBoost = 0.9f;
        /// <summary>The pools' and lamp heads' extra strength in the evening (1 at night and by day).</summary>
        public float Gain { get; private set; } = 1f;
        static readonly int OnId = Shader.PropertyToID("_NLOn"), HazeId = Shader.PropertyToID("_NLHaze"), ColId = Shader.PropertyToID("_NLColor"),
            RealId = Shader.PropertyToID("_NLReal"), CountId = Shader.PropertyToID("_NLCount"), PosId = Shader.PropertyToID("_NLPos"),
            DirId = Shader.PropertyToID("_NLDir"), ConeId = Shader.PropertyToID("_NLCone"), SparkId = Shader.PropertyToID("_NLSpark");
        const int MaxNear = 8;
        readonly Vector4[] nearPos = new Vector4[MaxNear], nearDir = new Vector4[MaxNear], nearCone = new Vector4[MaxNear];
        readonly int[] pick = new int[MaxNear]; readonly float[] pickD = new float[MaxNear];
        float lastOn = -1f, streamAt = -1e9f, lastZ = -1e9f;
        Color lastTint = new Color(-1f, 0f, 0f);

        /// <summary>How much the lamps are on now, 0 by day to 1 at night (they come on through the dusk).</summary>
        public float On { get; private set; }
        /// <summary>How much the beams show in the air (haze, lit falling snow), 0..1: less than On until it is dark.</summary>
        public float Beam { get; private set; }

        /// <summary>
        /// The lamps (their heads and pools): like a resort's, switched on before the sun goes down. Through the evening
        /// side of the day (from the end of the day into the night) they come on with the first of the dusk and are
        /// full by golden hour; in the morning they go off as the dawn comes up (as they always did).
        /// </summary>
        public static float LampLevel(float clock, float dark)
        {
            float c = Mathf.Repeat(clock, 4f);
            bool evening = c >= 1.6f && c < 3.8f;   // (at both ends of it the two agree: 0 in the day's hold, 1 at night)
            return evening ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.28f, dark))
                           : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.85f, dark));
        }

        /// <summary>The beams' share of the lamps: a faint haze and a few lit flakes against an evening sky, all of it at night.</summary>
        public static float BeamLevel(float dark) => 0.12f + 0.88f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, dark));
        /// <summary>The haze's strength now (Weather: falling snow and fog thicken it).</summary>
        public float Haze { get; private set; }
        /// <summary>The lamps the falling snow is lit by this frame, and the real lights' lamps (the test tools log them).</summary>
        public int NearCount { get; private set; }
        public int RealLamp(int i) => realId[i];
        readonly int[] realId = { -1, -1 };

        void OnApplied(DayNight dn)
        {
            if (dn != dayNight || cam == null) return;
            float dark = dn.Darkness;
            float on = TestOn >= 0f ? TestOn : LampLevel(TimeOfDay.Clock, dark);
            if (Disabled) on = 0f;
            On = on;
            // the beams in the air come on more slowly: faint while the sky is still light, all of it in the dark
            float beam = on * (TestOn >= 0f ? 1f : BeamLevel(dark));
            Beam = beam;
            Shader.SetGlobalFloat(BeamId, beam);
            // against the evening's still-bright snow and sky the pools and lamp heads need more to read: up to 1.9x
            // the pools and 2.2x the glare while there is sky light, back to as they were by full night
            Gain = 1f + EveningBoost * on * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.95f, dark)));
            Shader.SetGlobalFloat(GainId, Gain);
            // the night between the lamps: the moon's light on the snow brought down while they are on (not in the
            // camp, which has its own evening), so the lit runs read as islands of light in the dark
            float dim = on * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, dark)) * NightDim;
            if (dim > 0.001f && (camp == null || !camp.Active))
            {
                float g = 1f - dim;
                RenderSettings.ambientSkyColor *= g; RenderSettings.ambientEquatorColor *= g; RenderSettings.ambientGroundColor *= g;
                if (RenderSettings.sun != null) RenderSettings.sun.intensity *= 1f - dim * 1.2f;
            }

            // stream the bins as the rider goes (every 10 m, or a few times a second)
            float z = rider != null ? rider.transform.position.z : 0f;
            float t = Time.realtimeSinceStartup;
            if (Mathf.Abs(z - lastZ) > 10f || t - streamAt > 0.3f || !Application.isPlaying) { lastZ = z; streamAt = t; Stream(false); }
            else foreach (var b in bins.Values) if (!b.ready && b.work.done) { Land(b); break; }

            // the weather: faint in clear air, a glowing wall of light in falling snow
            var w = Weather.Active;
            float snow = w != null ? w.Now.snow : Mathf.Clamp01((atmo != null ? atmo.SnowAmount : 1f) * 0.15f);
            float fog = w != null ? Mathf.Clamp01(w.Now.fog / 0.0095f) : 0f;
            Haze = 0.22f + 1.0f * snow + 0.45f * fog;
            Shader.SetGlobalFloat(HazeId, Haze);
            Shader.SetGlobalFloat(SparkId, 1f);
            Shader.SetGlobalColor(ColId, LightColour);
            Shader.SetGlobalFloat(OnId, on);

            bool show = on > 0.004f;
            if (Mathf.Abs(on - lastOn) > 0.0001f && (show != (lastOn > 0.004f) || lastOn < 0f))
                foreach (var b in bins.Values)
                {
                    if (b.glow != null) b.glow.enabled = show;
                    if (b.pool != null) b.pool.enabled = show;
                    if (b.cone != null) b.cone.enabled = show;
                }
            lastOn = on;

            Flakes(show);
            if (!show)
            {
                foreach (var l in real) if (l.enabled) l.enabled = false;
                Shader.SetGlobalInt(CountId, 0);
                Shader.SetGlobalVector(RealId, new Vector4(-1f, 0f, -1f, 0f));
                NearCount = 0; realId[0] = realId[1] = -1;
                return;
            }
            Near(cam.transform.position, beam);
            Real(rider != null ? rider.transform.position : cam.transform.position, on);
            BeamSnow(beam, snow, fog, w);
        }

        /// <summary>How far down the moonlight on the snow goes while the lamps are on (a gamma-space factor).</summary>
        const float NightDim = 0.4f;

        // ---------------------------------------------------------------- snow in the beams

        ParticleSystem beamSnow;
        float beamCarry;

        void BuildBeamSnow()
        {
            var go = new GameObject("BeamSnow");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2400; main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.15f), new GradientAlphaKey(0.95f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = flakeMat; pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
            pr.SetActiveVertexStreams(FlakeStreams);
            ps.Play();
            beamSnow = ps;
        }

        /// <summary>
        /// Snow falling through the beams near the camera: flakes put straight into the nearest lamps' cones (where the
        /// box of snowfall round the camera is too thin to make a wall of light), lit by the flake shader like the rest.
        /// A sprinkle of glittering crystals in clear air, a blizzard of light in a storm.
        /// </summary>
        void BeamSnow(float on, float snow, float fog, Weather w)
        {
            if (beamSnow == null) return;
            float dt = Application.isPlaying ? Time.deltaTime : ChaseCamera.TestDt;
            int lampsN = Mathf.Min(NearCount, 4);
            if (dt <= 0f || lampsN == 0) return;
            float rate = (160f + 1700f * snow + 300f * fog) * on;
            beamCarry += rate * dt;
            int n = Mathf.Min((int)beamCarry, 120);
            beamCarry -= (int)beamCarry;
            if (n <= 0) return;
            Vector3 c = cam.transform.position, cf = cam.transform.forward;
            float fall = w != null ? Mathf.Max(0.6f, w.Now.fall) : 1.5f;
            float windK = 1f + 7f * (w != null ? w.Now.wind : 0.2f);
            Vector3 wind = new Vector3(TreeWind.Dir.x, 0f, TreeWind.Dir.y) * windK;
            float big = w != null ? w.Now.flake : 0.3f;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int e = 0; e < n; e++)
            {
                var l = lamps[pick[e % lampsN]];
                float half = (l.outer * 0.22f + l.inner * 0.28f) * Mathf.Deg2Rad;
                float drop = l.head.y - Slope.Height(l.head.x, l.head.z);
                float len = Mathf.Min(l.range * 0.85f, l.dir.y < -0.1f ? drop / -l.dir.y * 1.2f : l.range * 0.85f);
                Vector3 up = Mathf.Abs(l.dir.y) > 0.95f ? Vector3.forward : Vector3.up;
                Vector3 ax = Vector3.Cross(l.dir, up).normalized, ay = Vector3.Cross(ax, l.dir);
                for (int tries = 0; tries < 3; tries++)
                {
                    float sAlong = Mathf.Lerp(1.5f, len, Mathf.Sqrt(Random.value));
                    float rad = sAlong * Mathf.Tan(half) * Mathf.Sqrt(Random.value);
                    float a = Random.value * Mathf.PI * 2f;
                    Vector3 p = l.head + l.dir * sAlong + (ax * Mathf.Cos(a) + ay * Mathf.Sin(a)) * rad;
                    Vector3 v = p - c;
                    float d2 = v.sqrMagnitude;
                    if (d2 > 55f * 55f || Vector3.Dot(v, cf) < 1.5f) continue;
                    if (p.y < Slope.Height(p.x, p.z) + 0.2f) continue;
                    ep.position = p;
                    ep.velocity = wind * Random.Range(0.6f, 1.4f) + Vector3.down * fall * Random.Range(0.7f, 1.4f);
                    ep.startSize = Random.Range(0.035f, 0.08f) * (1f + big) * (1f + Mathf.Sqrt(d2) * 0.012f);
                    ep.startLifetime = Random.Range(1.1f, 2f);
                    ep.startColor = new Color(1f, 1f, 1f, 0.95f);
                    beamSnow.Emit(ep, 1);
                    break;
                }
            }
        }

        /// <summary>The falling snow (Atmosphere's, and the weather's near flakes) drawn with the lit flake shader.</summary>
        void Flakes(bool show)
        {
            if (flakeMat == null) return;
            if (flakeRenderers.Count == 0 && atmo != null)
            {
                var s = atmo.transform.Find("Snowfall");
                if (s != null) Adopt(s.GetComponent<ParticleSystemRenderer>());
            }
            if (Weather.Active != weatherSeen)
            {
                weatherSeen = Weather.Active;
                if (weatherSeen != null)
                    foreach (var pr in weatherSeen.GetComponentsInChildren<ParticleSystemRenderer>(true))
                        if (pr.name == "NearSnow") Adopt(pr);
            }
            // the time of day's tint on the soft particles (DayNight sets it on the spray material)
            if (spray != null)
            {
                Color c = spray.GetColor("_BaseColor");
                if (c != lastTint) { lastTint = c; flakeMat.SetColor("_BaseColor", c); }
            }
        }

        static readonly List<ParticleSystemVertexStream> FlakeStreams = new List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
              ParticleSystemVertexStream.Center, ParticleSystemVertexStream.StableRandomX };

        void Adopt(ParticleSystemRenderer pr)
        {
            if (pr == null || flakeRenderers.Contains(pr)) return;
            if (pr.sharedMaterial != spray) return;   // (only the soft snow: not the lens flakes or the dust)
            pr.sharedMaterial = flakeMat;
            pr.SetActiveVertexStreams(FlakeStreams);
            flakeRenderers.Add(pr);
        }

        /// <summary>The lamps nearest the camera whose light reaches the air round it: the falling snow's lamps.</summary>
        void Near(Vector3 c, float on)
        {
            int n = 0;
            foreach (var b in bins.Values)
            {
                if (!b.ready) continue;
                foreach (var l in b.work.lamps)
                {
                    float reach = l.range + 30f;
                    float d2 = (l.head - c).sqrMagnitude;
                    if (d2 > reach * reach) continue;
                    // keep the nearest MaxNear (insertion into a short sorted list)
                    int at = n < MaxNear ? n++ : MaxNear;
                    if (at == MaxNear) { if (d2 >= pickD[MaxNear - 1]) continue; at = MaxNear - 1; }
                    while (at > 0 && pickD[at - 1] > d2) { pickD[at] = pickD[at - 1]; pick[at] = pick[at - 1]; at--; }
                    pickD[at] = d2; pick[at] = l.id;
                }
            }
            var all = lamps;
            for (int i = 0; i < n; i++)
            {
                var l = all[pick[i]];
                float co = l.CosOuter, ci = l.CosInner;
                float a = 1f / Mathf.Max(ci - co, 0.001f);
                nearPos[i] = new Vector4(l.head.x, l.head.y, l.head.z, l.power * on);
                nearDir[i] = new Vector4(l.dir.x, l.dir.y, l.dir.z, 1f / (l.range * l.range));
                nearCone[i] = new Vector4(a, -co * a, l.id, 0f);   // (z: its id, for _NLReal: PisteKit.shader)
            }
            for (int i = n; i < MaxNear; i++) { nearPos[i] = Vector4.zero; nearDir[i] = new Vector4(0f, -1f, 0f, 1f); nearCone[i] = Vector4.zero; }
            NearCount = n;
            Shader.SetGlobalInt(CountId, n);
            Shader.SetGlobalVectorArray(PosId, nearPos);
            Shader.SetGlobalVectorArray(DirId, nearDir);
            Shader.SetGlobalVectorArray(ConeId, nearCone);
        }

        /// <summary>
        /// The two lamps lighting the rider most become real spot lights (no shadows): the rider, kickers, trees and
        /// people in their beams are lit as they cross them. Each fades out before the next lamp takes its place, and
        /// its pool gives the snow over to it by as much (the pool shader leaves out a real lamp's share).
        /// </summary>
        void Real(Vector3 p, float on)
        {
            // the three lamps lighting the point most (by their light there, not by distance alone)
            int a = -1, b = -1, c = -1; float la = 0f, lb = 0f, lc = 0f;
            foreach (var bin in bins.Values)
            {
                if (!bin.ready) continue;
                foreach (var l in bin.work.lamps)
                {
                    float s = Reach(l, p);
                    if (s <= lc) continue;
                    if (s > la) { c = b; lc = lb; b = a; lb = la; a = l.id; la = s; }
                    else if (s > lb) { c = b; lc = lb; b = l.id; lb = s; }
                    else { c = l.id; lc = s; }
                }
            }
            // a lamp is handed over as smoothly as the third's reach rises to meet it
            float wa = a < 0 ? 0f : Mathf.Clamp01((la - lc) / Mathf.Max(0.02f, la * 0.5f));
            float wb = b < 0 ? 0f : Mathf.Clamp01((lb - lc) / Mathf.Max(0.02f, lb * 0.5f));
            Set(0, a, wa * on); Set(1, b, wb * on);
            Shader.SetGlobalVector(RealId, new Vector4(a, a < 0 ? 0f : wa, b, b < 0 ? 0f : wb));
        }

        /// <summary>How much of a lamp's light reaches p (its range falloff and cone; the range is what matters here).</summary>
        static float Reach(in Lamp l, Vector3 p)
        {
            Vector3 d = p - l.head;
            float d2 = d.sqrMagnitude, r2 = l.range * l.range;
            if (d2 >= r2) return 0f;
            float f = d2 / r2; float smooth = (1f - f * f); smooth *= smooth;
            float cd = Vector3.Dot(d / Mathf.Sqrt(Mathf.Max(d2, 1e-4f)), l.dir);
            float co = l.CosOuter, ci = l.CosInner;
            float ang = Mathf.Clamp01((cd - co) / Mathf.Max(ci - co, 0.001f));
            return smooth * Mathf.Max(ang * ang, 0.05f);   // (a lamp just behind its cone still lights the rider's back a little)
        }

        void Set(int i, int id, float w)
        {
            var light = real[i];
            realId[i] = w > 0.001f ? id : -1;
            if (id < 0 || w <= 0.001f) { if (light.enabled) light.enabled = false; return; }
            var l = lamps[id];
            if (!light.enabled) light.enabled = true;
            light.transform.SetPositionAndRotation(l.head, Quaternion.LookRotation(l.dir));
            light.spotAngle = l.outer; light.innerSpotAngle = l.inner; light.range = l.range;
            light.intensity = l.power * w * Gain;
        }

        // ---------------------------------------------------------------- the meshes (worker thread)

        /// <summary>The pole (an eight-sided taper), its cross-arm and the lamp's housing; a bracket alone on a lift.</summary>
        static void Pole(MeshData m, in Lamp l)
        {
            Vector3 f = new Vector3(l.dir.x, 0f, l.dir.z);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            if (l.pole)
            {
                Vector3 top = l.head - Vector3.up * 0.25f - f * 0.25f;
                Tube(m, l.foot - Vector3.up * 1f, top, 0.17f, 0.09f, 8);
                Box(m, top + f * 0.05f, Quaternion.LookRotation(f), new Vector3(1.4f, 0.12f, 0.12f));   // the cross-arm
            }
            // the housing: a flat box facing along the beam
            var rot = Quaternion.LookRotation(l.dir, Mathf.Abs(l.dir.y) > 0.95f ? f : Vector3.up);
            Box(m, l.head - l.dir * 0.2f, rot, new Vector3(0.95f, 0.6f, 0.32f));
        }

        static void Tube(MeshData m, Vector3 a, Vector3 b, float ra, float rb, int seg)
        {
            int v0 = m.v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i * Mathf.PI * 2f / seg;
                var o = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                m.v.Add(a + o * ra); m.n.Add(o);
                m.v.Add(b + o * rb); m.n.Add(o);
            }
            for (int i = 0; i < seg; i++)
            {
                int p = v0 + i * 2;
                m.t.Add(p); m.t.Add(p + 1); m.t.Add(p + 2);
                m.t.Add(p + 2); m.t.Add(p + 1); m.t.Add(p + 3);
            }
        }

        static readonly Vector3[] BoxN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        static void Box(MeshData m, Vector3 c, Quaternion r, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            foreach (var n in BoxN)
            {
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 s = Vector3.Cross(n, u);
                int v0 = m.v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k == 1 || k == 2) ? 1f : -1f, sv = k >= 2 ? 1f : -1f;
                    Vector3 p = n + u * su + s * sv;
                    m.v.Add(c + r * Vector3.Scale(p, h));
                    m.n.Add(r * n);
                }
                m.t.Add(v0); m.t.Add(v0 + 1); m.t.Add(v0 + 2); m.t.Add(v0); m.t.Add(v0 + 2); m.t.Add(v0 + 3);
            }
        }

        /// <summary>The glare: one quad at the lens, turned to the camera in the shader (uv0: corner, size).</summary>
        static void Glare(MeshData m, in Lamp l)
        {
            Vector3 c = l.head + l.dir * 0.3f;
            float size = l.kind == Kind.Jump ? 2.2f : l.kind == Kind.Tower ? 1.4f : 1.8f;
            int v0 = m.v.Count;
            for (int k = 0; k < 4; k++)
            {
                float x = (k == 1 || k == 2) ? 1f : -1f, y = k >= 2 ? 1f : -1f;
                m.v.Add(c);
                m.u0.Add(new Vector4(x, y, size, 0f));
                m.u1.Add(new Vector4(l.dir.x, l.dir.y, l.dir.z, 1f));
            }
            m.t.Add(v0); m.t.Add(v0 + 2); m.t.Add(v0 + 1); m.t.Add(v0); m.t.Add(v0 + 3); m.t.Add(v0 + 2);
        }

        /// <summary>
        /// The pool: the terrain's own grid squares (Slope.Quad, split the way SlopeStreamer splits them) wherever the
        /// lamp's light on them is worth drawing, a few centimetres up off the snow. Each corner carries the lamp, so the
        /// shader lights every pixel exactly (uv0: lens and power; uv1: aim and cos of the outer cone; uv2: cos of the
        /// inner cone, 1 / range^2, the lamp's id).
        /// </summary>
        static void Pool(MeshData m, Lamp l)
        {
            const float q = Slope.Quad, Lift = 0.07f;
            float r = Mathf.Sqrt(Mathf.Max(1f, l.range * l.range - 9f));
            int i0 = Mathf.FloorToInt((l.head.x - r) / q), i1 = Mathf.CeilToInt((l.head.x + r) / q);
            int j0 = Mathf.FloorToInt((l.head.z - r) / q), j1 = Mathf.CeilToInt((l.head.z + r) / q);
            int w = i1 - i0 + 1, hgt = j1 - j0 + 1;
            var hs = new float[w * hgt];
            var lit = new float[w * hgt];
            float co = l.CosOuter, ci = l.CosInner, r2 = l.range * l.range;
            for (int j = 0; j < hgt; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = (i0 + i) * q, z = (j0 + j) * q;
                    float dx = x - l.head.x, dz = z - l.head.z;
                    int k = j * w + i;
                    hs[k] = float.NaN; lit[k] = 0f;
                    if (dx * dx + dz * dz > r2) continue;
                    // (a cheap test first: well behind the lamp, whatever the height)
                    if ((dx * l.dir.x + dz * l.dir.z) < -6f && l.dir.y > -0.9f) continue;
                    float h = Slope.Height(x, z);
                    hs[k] = h;
                    Vector3 d = new Vector3(dx, h - l.head.y, dz);
                    float d2 = d.sqrMagnitude;
                    if (d2 >= r2) continue;
                    float cd = Vector3.Dot(d, l.dir) / Mathf.Sqrt(d2);
                    if (cd <= co) continue;
                    float f = d2 / r2; float sm = 1f - f * f; sm *= sm;
                    float ang = Mathf.Clamp01((cd - co) / Mathf.Max(ci - co, 0.001f));
                    lit[k] = l.power * sm * ang * ang / Mathf.Max(d2, 1f);
                }
            const float Least = 0.012f;
            var index = new int[w * hgt];
            for (int k = 0; k < index.Length; k++) index[k] = -1;
            int Vert(int i, int j)
            {
                int k = j * w + i;
                if (index[k] >= 0) return index[k];
                float x = (i0 + i) * q, z = (j0 + j) * q;
                float h = float.IsNaN(hs[k]) ? Slope.Height(x, z) : hs[k];
                Vector3 n = Slope.Normal(x, z);
                index[k] = m.v.Count;
                m.v.Add(new Vector3(x, h, z) + n * Lift);
                m.n.Add(n);
                m.u0.Add(new Vector4(l.head.x, l.head.y, l.head.z, l.power));
                m.u1.Add(new Vector4(l.dir.x, l.dir.y, l.dir.z, co));
                m.u2.Add(new Vector4(ci, 1f / r2, l.id, 0f));
                return index[k];
            }
            for (int j = 0; j < hgt - 1; j++)
                for (int i = 0; i < w - 1; i++)
                {
                    int k = j * w + i;
                    if (Mathf.Max(Mathf.Max(lit[k], lit[k + 1]), Mathf.Max(lit[k + w], lit[k + w + 1])) < Least) continue;
                    if (float.IsNaN(hs[k]) && float.IsNaN(hs[k + 1]) && float.IsNaN(hs[k + w]) && float.IsNaN(hs[k + w + 1])) continue;
                    // as the terrain splits its squares: (x0,z0)-(x0+q,z0)-(x0,z0+q) and (x0+q,z0+q)-(x0,z0+q)-(x0+q,z0)
                    int a = Vert(i, j), b = Vert(i + 1, j), c = Vert(i, j + 1), d = Vert(i + 1, j + 1);
                    m.t.Add(a); m.t.Add(c); m.t.Add(b);
                    m.t.Add(d); m.t.Add(b); m.t.Add(c);
                }
        }

        /// <summary>
        /// The haze: a cone of rings out from the lens along the beam to where it meets the snow, each corner's alpha
        /// the light the air there scatters (brightest just out of the lamp, thinning with distance), and nothing
        /// below the snow, so it fades into the ground instead of cutting through it. Normals for the shader's soft
        /// edge. (color.a: the air's light; uv0: the lens and the lamp's id; uv1: the aim.)
        /// </summary>
        static void Cone(MeshData m, in Lamp l)
        {
            float half = (l.outer * 0.25f + l.inner * 0.25f) * Mathf.Deg2Rad;
            float tan = Mathf.Tan(half);
            // as far as the beam goes before the snow (or its range)
            float len = l.range * 0.9f;
            for (float s = 2f; s < l.range; s += 1f)
            {
                Vector3 p = l.head + l.dir * s;
                if (p.y < Slope.Height(p.x, p.z)) { len = Mathf.Min(len, s * 1.15f); break; }
            }
            Vector3 up = Mathf.Abs(l.dir.y) > 0.95f ? Vector3.forward : Vector3.up;
            Vector3 ax = Vector3.Cross(l.dir, up).normalized, ay = Vector3.Cross(ax, l.dir);
            // the snow under the beam as a plane (where it lands), for the shader's fade into it
            Vector3 gh = Hit(l, out _);
            gh.y = Slope.Height(gh.x, gh.z);
            Vector3 gn = Slope.Normal(gh.x, gh.z);
            var plane = new Vector4(gn.x, gn.y, gn.z, Vector3.Dot(gn, gh));
            float[] rings = { 0f, 0.05f, 0.12f, 0.22f, 0.34f, 0.48f, 0.63f, 0.8f, 1f };
            const int seg = 14;
            float glow = l.kind == Kind.Jump ? 1.25f : l.kind == Kind.Tower ? 0.7f : 1f;
            int v0 = m.v.Count;
            for (int ri = 0; ri < rings.Length; ri++)
            {
                float s = rings[ri] * len;
                float rad = 0.25f + s * tan;
                // single scattering: light ~ 1/d^2 times the beam's width ~ d: ~1/d; in from nothing at the lens
                float a = Mathf.Min(1f, 3.5f / (s + 2.5f)) * Mathf.SmoothStep(0f, 1f, rings[ri] / 0.08f) * (1f - Mathf.SmoothStep(0.62f, 1f, rings[ri])) * glow;
                for (int i = 0; i <= seg; i++)
                {
                    float ang = i * Mathf.PI * 2f / seg;
                    Vector3 o = ax * Mathf.Cos(ang) + ay * Mathf.Sin(ang);
                    Vector3 p = l.head + l.dir * s + o * rad;
                    float above = p.y - Slope.Height(p.x, p.z);
                    float ground = Mathf.Clamp01(above / 2f);
                    m.v.Add(p);
                    m.n.Add((o * Mathf.Cos(half) - l.dir * Mathf.Sin(half)).normalized);
                    m.c.Add(new Color(1f, 1f, 1f, a * ground * ground));
                    m.u0.Add(new Vector4(l.head.x, l.head.y, l.head.z, l.id));
                    m.u1.Add(new Vector4(l.dir.x, l.dir.y, l.dir.z, 0f));
                    m.u2.Add(plane);
                }
            }
            for (int ri = 0; ri < rings.Length - 1; ri++)
                for (int i = 0; i < seg; i++)
                {
                    int p = v0 + ri * (seg + 1) + i, n = p + seg + 1;
                    m.t.Add(p); m.t.Add(n); m.t.Add(p + 1);
                    m.t.Add(p + 1); m.t.Add(n); m.t.Add(n + 1);
                }
        }

        // ---------------------------------------------------------------- the test tools

        /// <summary>Bins drawn, meshes' vertices and triangles, lamps in them (the test tools log it).</summary>
        public string Stats()
        {
            int nb = 0, nl = 0; long verts = 0, tris = 0; int draws = 0;
            foreach (var b in bins.Values)
            {
                if (!b.ready) continue;
                nb++; nl += b.work.lamps.Length;
                foreach (var m in b.meshes) { verts += m.vertexCount; tris += m.GetIndexCount(0) / 3; draws++; }
            }
            return $"bins {nb}, lamps {nl} (of {lamps.Length}), meshes {draws}, verts {verts}, tris {tris}, on {On:0.00}, beam {Beam:0.00}, haze {Haze:0.00}, near {NearCount}, real {realId[0]}/{realId[1]}, " +
                   $"lit snowfall systems {flakeRenderers.Count}, beam flakes {(beamSnow != null ? beamSnow.particleCount : 0)}";
        }
    }
}
