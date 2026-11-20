using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The whole of Mount Vertigo as it is seen from kilometres away (the start page's helicopter, StartPage.cs): one
    /// coarse mesh of the mountain and one of the country round it out to the haze, in place of the terrain the
    /// game streams (which is only ever a strip a few hundred metres wide round the rider).
    ///
    /// The mountain is the one in the game's backdrop photograph (Tools/art/rainier-extended.jpg, Mount Rainier):
    /// a broad glaciated volcano, far wider than it is tall. A rounded summit dome; flanks that start steep and
    /// ease out into long shoulders (steeper and rockier on one side, a long snow shoulder on the other, as the
    /// photo has them); cleavers of rock running down the fall line with glaciers lying between them, cliff
    /// bands across them, a sharp lesser peak standing off one flank; and forest from the ragged snow line down.
    ///
    /// The run is the real one: along the piste the heights are Slope.Height's, so the summit camp's plateau, the
    /// start pad, the ridges, gorges and cliffs of this mountain's course are all where the game has them, and a
    /// new mountain (a new seed) gives a new far view. The run falls far more steeply than a volcano's flank
    /// does, all the way down, so it lies in a great trough cut into the mountain's front: a valley whose head is
    /// the summit and whose sides climb back to the shoulders.
    ///
    /// Drawn on its own layer by Resources/FarMountain.shader (snow, ice, rock and forest per pixel, the day's
    /// light, its own haze). The mountain's shadow is worked out here, per vertex, for where the sun or moon
    /// stands, a slice each frame when the light has moved, so a low sun throws its shadow across the valley.
    /// </summary>
    public class FarMountain : MonoBehaviour
    {
        public const int Layer = 29;
        public const float Cell = 64f;
        const int CellsX = 90, CellsBack = 88, CellsFront = 104;   // the fine grid: either side of the summit, behind it, down the run
        const int RingSegs = 128, RingsNear = 66, Rings = 78;       // the country round it: squares growing outward
        const float RingGrow = 0.026f, RingGrowFar = 0.1f;   // fine out to where the helicopter flies, then coarse to the haze

        // ---------------------------------------------------------------- the shape (a pure function of the mountain's seed)

        // the flank's profile: Steep at the top, easing over EaseLength to Shoulder, under a dome DomeRadius across
        const float Steep = 0.5f, Shoulder = 0.23f, EaseLength = 2000f, DomeRadius = 420f;
        /// <summary>How far out the helicopter flies (StartPage): the country is kept low under its circle.</summary>
        public const float FlownRadius = 19000f;

        static Vector2[] knob; static float[] knobH, knobR;
        static float sx, sz, topY, floorY, lobeX, lobeZ;
        static int shapeSeed = int.MinValue;

        /// <summary>Where the summit is, and how low the valley floor lies.</summary>
        public static Vector3 SummitPoint { get { Setup(); return new Vector3(sx, topY, sz); } }
        public static float FloorY { get { Setup(); return floorY; } }
        /// <summary>The middle of the massif on the ground (what the helicopter circles).</summary>
        public static Vector3 Middle { get { Setup(); return new Vector3(sx, floorY, sz + 500f); } }
        /// <summary>Half the width of the mountain's snow and ice, for framing it (its forested shoulders run on far wider).</summary>
        public const float HalfAcross = 3400f, HalfAlong = 3400f;

        static void Setup()
        {
            if (shapeSeed == Slope.Seed && knob != null) return;
            shapeSeed = Slope.Seed;
            sx = Slope.Centre(Slope.PadLip); sz = -100f;
            topY = Slope.Height(sx, sz);
            floorY = Slope.FallLine(Slope.FinishZ + 4000f);
            float J(int i) => Slope.Hash01(i, 5, 97) - 0.5f;
            // The long shoulder runs out to the rider's left (-x) and a little back, the steep rocky side is the
            // camp's cliff side (+x): seen from down the valley, as the photo is, the steep skyline is on the left
            // and the long snow shoulder on the right
            float la = (-105f + 20f * J(1)) * Mathf.Deg2Rad;
            lobeX = Mathf.Sin(la); lobeZ = Mathf.Cos(la);
            // lesser tops: a sharp rock peak standing off the steep flank (the photo's, on its left), and two
            // shoulders that bench the skyline
            knob = new Vector2[3]; knobH = new float[3]; knobR = new float[3];
            float[] at = { 62f, -128f, 168f }, far = { 4300f, 3000f, 2500f }, high = { 520f, 230f, 260f }, wide = { 520f, 900f, 800f };
            for (int i = 0; i < 3; i++)
            {
                float a = (at[i] + 14f * J(20 + i)) * Mathf.Deg2Rad, d = far[i] * (1f + 0.25f * J(30 + i));
                knob[i] = new Vector2(sx + Mathf.Sin(a) * d, sz + Mathf.Cos(a) * d);
                knobH[i] = high[i] * (1f + 0.4f * J(40 + i)); knobR[i] = wide[i] * (1f + 0.3f * J(50 + i));
            }
        }

        static float SMax(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
            return Mathf.Lerp(b, a, h) + k * h * (1f - h);
        }

        static float S01(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        /// <summary>The mountain itself, without the run's own strip.</summary>
        static float Massif(float x, float z)
        {
            float dx = x - sx, dz = z - sz;
            float r = Mathf.Sqrt(dx * dx + dz * dz);
            if (r > 16000f) return floorY - 3000f;   // (far below the country round it out there)
            float inv = 1f / Mathf.Max(r, 1f), ux = dx * inv, uz = dz * inv;
            // never a cone: it reaches further one way than the other, and wanders round between
            float reach = 1f + 0.24f * (ux * lobeX + uz * lobeZ) + 0.26f * (Slope.Noise(ux * 1.6f + 4f, uz * 1.6f + 9f) - 0.5f);
            float re = r / reach;
            float drop = (Steep * EaseLength * (1f - Mathf.Exp(-re / EaseLength)) + Shoulder * re) * (re * re / (re * re + DomeRadius * DomeRadius));
            float grow = Mathf.Clamp01((r - 250f) / 1100f);
            // cleavers and the glaciers' beds between them, running down from the dome (round the mountain with no
            // seam), standing higher and cut deeper the further down
            float ribs = Slope.Ridged(ux * 3.4f + 5f + r * 0.00012f, uz * 3.4f + 2f);
            float fine = Slope.Ridged(ux * 9f + 1f, uz * 9f + 6f + r * 0.0004f);
            drop += grow * ((0.4f - ribs) * (120f + r * 0.075f) + (0.4f - fine) * (45f + r * 0.02f));
            // cliff bands across the fall line, every few hundred metres of height, where the lava's layers break
            // through: here and there, never a ring right round
            float layer = drop / 430f + 1.4f * Slope.Noise(x * 0.0005f + 2f, z * 0.0005f + 7f);
            float lf = layer - Mathf.Floor(layer);
            float band = S01(0.42f, 0.85f, Slope.Noise(x * 0.00035f + 8f, z * 0.00035f + 1f)) * grow;
            drop += band * 430f * 0.24f * (Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.54f, lf)) - lf);
            drop += (Slope.Noise(x * 0.0009f + 3f, z * 0.0009f) - 0.5f) * 300f * grow + (Slope.Noise(x * 0.0031f, z * 0.0031f + 4f) - 0.5f) * 70f * grow;
            for (int i = 0; i < knob.Length; i++)
            {
                float kx = x - knob[i].x, kz = z - knob[i].y, q = (kx * kx + kz * kz) / (knobR[i] * knobR[i]);
                if (q < 9f) drop -= knobH[i] * Mathf.Exp(-q * (i == 0 ? 1.6f : 1f));
            }
            // the run's trough: the fall line's own plane down the front, its sides climbing back to the shoulders
            if (z > -300f)
            {
                float a = Mathf.Abs(x - Slope.Centre(Mathf.Clamp(z, Slope.PadLip, Slope.FinishZ)));
                float side = a - (430f + 0.2f * Mathf.Max(0f, z));
                float wall = side > 0f ? side * (0.55f + 0.25f * (Slope.Noise(z * 0.0012f + 5f, Mathf.Sign(x - sx) * 3f) - 0.5f)) : 0f;
                float front = Mathf.Min(topY + Slope.Fall * z, topY - floorY) - wall;
                drop = SMax(drop, front, 240f);
            }
            return topY - drop;
        }

        /// <summary>The country round the mountain: lowland at its foot, forested ridges, and far ranges.</summary>
        static float Around(float x, float z)
        {
            float dx = x - sx, dz = z - (sz + 500f);
            float r = Mathf.Sqrt(dx * dx + dz * dz);
            float y = floorY + (Slope.Noise(x * 0.0009f + 11f, z * 0.0009f) - 0.5f) * 70f * S01(8000f, 11000f, r);
            float hills = S01(8500f, 12500f, r);
            if (hills > 0f)
            {
                // (rounded: the mesh out here is too coarse to carry a sharp crest without sawing it)
                float big = Slope.Noise(x * 0.00023f + 1.7f, z * 0.00023f), mid = Slope.Noise(x * 0.00062f + 4f, z * 0.00062f + 2f);
                // and low where the helicopter flies its circle (StartPage), so nothing looms under it
                float flown = 1f - 0.6f * S01(FlownRadius - 4500f, FlownRadius - 2000f, r) * (1f - S01(FlownRadius + 4000f, FlownRadius + 8000f, r));
                y += hills * flown * (big * big * 950f + mid * mid * 280f + Slope.Noise(x * 0.0017f, z * 0.0017f + 5f) * 60f - 120f);
            }
            float far = S01(42000f, 62000f, r);
            if (far > 0f)
                y += far * (Slope.Ridged(x * 0.00006f + 9f, z * 0.00006f + 3f) * 1200f + Slope.Ridged(x * 0.00018f, z * 0.00018f + 8f) * 1000f
                            + Slope.Ridged(x * 0.0005f + 2f, z * 0.0005f) * 260f);
            return y;
        }

        /// <summary>How much of the game's own height is used here: all of it along the run, none a few hundred metres off it.</summary>
        static float StripWeight(float x, float z)
        {
            float a = Mathf.Abs(x - Slope.Centre(Mathf.Clamp(z, Slope.PadLip, Slope.FinishZ)));
            if (a >= 540f || z <= -330f || z >= Slope.FinishZ + 520f) return 0f;
            // (round the summit only the camp's own plateau: further out the game's summit falls away far faster than
            // any mountain, and the two blended stood up as a tower on the dome)
            float top = S01(-40f, 260f, z);
            return (1f - S01(Mathf.Lerp(34f, 190f, top), Mathf.Lerp(130f, 540f, top), a)) * (1f - S01(Slope.FinishZ + 120f, Slope.FinishZ + 520f, z)) * S01(-330f, -170f, z);
        }

        /// <summary>The far view's ground at (x, z).</summary>
        public static float Height(float x, float z)
        {
            Setup();
            float y = SMax(Massif(x, z), Around(x, z), 220f);
            float w = StripWeight(x, z);
            return w > 0f ? Mathf.Lerp(y, Slope.Height(x, z), w) : y;
        }

        // ---------------------------------------------------------------- the meshes

        Mesh core, ring;
        Material mat;
        Texture2D noise;
        int nx, nz, builtSeed = int.MinValue;
        float x0, z0;
        float[] coreH;
        Vector3[] ringV, coreV;
        Color32[] coreC, ringC;
        float[] shade;      // the shadow being worked out (core, then ring)
        Vector3 bakedSun, bakingSun;
        int bakeAt = -1;
        static readonly int FloorId = Shader.PropertyToID("_FloorY"), TopId = Shader.PropertyToID("_TopY"), PhaseId = Shader.PropertyToID("_Phase"),
                            FinishId = Shader.PropertyToID("_FinishZ"), LightsId = Shader.PropertyToID("_Lights"), NoiseId = Shader.PropertyToID("_Noise");

        public int Triangles => (core != null ? (int)(core.GetIndexCount(0) / 3) : 0) + (ring != null ? (int)(ring.GetIndexCount(0) / 3) : 0);
        public int Vertices => (core != null ? core.vertexCount : 0) + (ring != null ? ring.vertexCount : 0);
        /// <summary>How far the country round the mountain goes (the camera's far plane must reach its corners).</summary>
        public float Reach { get; private set; }

        /// <summary>Build (or rebuild, for a new mountain) the far view. `fallback` is used if the far shader is missing.</summary>
        public void Build(Shader fallback)
        {
            if (builtSeed == Slope.Seed && core != null) return;
            builtSeed = Slope.Seed;
            Setup();
            if (mat == null)
            {
                var sh = Resources.Load<Shader>("FarMountain");
                bool own = sh != null && sh.isSupported;
                mat = new Material(own ? sh : fallback) { name = "FarMountain" };
                if (own) { noise = MakeNoise(); mat.SetTexture(NoiseId, noise); }
                else { mat.SetColor("_BaseColor", new Color(0.86f, 0.89f, 0.95f)); mat.SetFloat("_Smoothness", 0.1f); }
            }
            mat.SetFloat(FloorId, floorY); mat.SetFloat(TopId, topY);
            mat.SetFloat(PhaseId, (Slope.Seed % 628) / 100f);   // (Slope's own phase: the piste is drawn where it is ridden)
            mat.SetFloat(FinishId, Slope.FinishZ);
            BuildCore();
            BuildRing();
            bakedSun = Vector3.zero; bakeAt = -1;
        }

        MeshRenderer Holder(string name, ref Mesh mesh)
        {
            var t = transform.Find(name);
            if (t != null) return t.GetComponent<MeshRenderer>();
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mr;
        }

        void BuildCore()
        {
            Holder("FarCore", ref core);
            nx = CellsX * 2 + 1; nz = CellsBack + CellsFront + 1;
            x0 = sx - CellsX * Cell; z0 = sz - CellsBack * Cell;
            coreH = new float[nx * nz];
            var v = new Vector3[nx * nz]; var n = new Vector3[nx * nz];
            coreC = new Color32[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = x0 + i * Cell, z = z0 + j * Cell, y = Height(x, z);
                    coreH[j * nx + i] = y;
                    v[j * nx + i] = new Vector3(x, y, z);
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int il = Mathf.Max(i - 1, 0), ir = Mathf.Min(i + 1, nx - 1), jd = Mathf.Max(j - 1, 0), ju = Mathf.Min(j + 1, nz - 1);
                    float hl = coreH[j * nx + il], hr = coreH[j * nx + ir], hd = coreH[jd * nx + i], hu = coreH[ju * nx + i];
                    n[j * nx + i] = new Vector3(-(hr - hl) / ((ir - il) * Cell), 1f, -(hu - hd) / ((ju - jd) * Cell)).normalized;
                    // hollows hold shade and blue, crests catch the light: from how far the ground sits below its neighbours
                    int i2l = Mathf.Max(i - 3, 0), i2r = Mathf.Min(i + 3, nx - 1), j2d = Mathf.Max(j - 3, 0), j2u = Mathf.Min(j + 3, nz - 1);
                    float lap = (coreH[j * nx + i2l] + coreH[j * nx + i2r] + coreH[j2d * nx + i] + coreH[j2u * nx + i]) * 0.25f - coreH[j * nx + i];
                    coreC[j * nx + i] = new Color32(255, 255, (byte)(255f * Mathf.Clamp01(0.82f - lap * 0.011f)), 255);
                }
            // a skirt hanging from the grid's edge: the country round it tucks in under that edge a little low, and
            // from outside, without the skirt, the sky would show through the slot between the two
            int edge = 2 * (nx + nz) - 4, grid = nx * nz;
            var rim = new int[edge];
            int e = 0;
            for (int i = 0; i < nx; i++) rim[e++] = i;
            for (int j = 1; j < nz; j++) rim[e++] = j * nx + nx - 1;
            for (int i = nx - 2; i >= 0; i--) rim[e++] = (nz - 1) * nx + i;
            for (int j = nz - 2; j >= 1; j--) rim[e++] = j * nx;
            System.Array.Resize(ref v, grid + edge); System.Array.Resize(ref n, grid + edge); System.Array.Resize(ref coreC, grid + edge);
            for (int k = 0; k < edge; k++) { v[grid + k] = v[rim[k]] + Vector3.down * 90f; n[grid + k] = n[rim[k]]; coreC[grid + k] = coreC[rim[k]]; }
            var t = new int[(nx - 1) * (nz - 1) * 6 + edge * 12];
            int q = 0;
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    t[q++] = a; t[q++] = c; t[q++] = b; t[q++] = b; t[q++] = c; t[q++] = d;
                }
            for (int k = 0; k < edge; k++)
            {
                int a = rim[k], b = rim[(k + 1) % edge], c = grid + k, d = grid + (k + 1) % edge;
                t[q++] = a; t[q++] = c; t[q++] = b; t[q++] = b; t[q++] = c; t[q++] = d;   // seen from either side
                t[q++] = a; t[q++] = b; t[q++] = c; t[q++] = b; t[q++] = d; t[q++] = c;
            }
            coreV = v;
            core.Clear();
            core.vertices = v; core.normals = n; core.colors32 = coreC; core.triangles = t;
            core.RecalculateBounds();
        }

        /// <summary>
        /// Squares growing outward from the fine grid's edge to the haze. The first tucks in under the grid's edge
        /// (a cell in, a little low), so the two meshes need not share vertices and no sky shows between them.
        /// </summary>
        void BuildRing()
        {
            Holder("FarRing", ref ring);
            int per = RingSegs * 4;
            float cx = sx, cz = z0 + (nz - 1) * Cell * 0.5f, hx = CellsX * Cell, hz = (nz - 1) * Cell * 0.5f;
            ringV = new Vector3[per * Rings]; var n = new Vector3[per * Rings];
            ringC = new Color32[per * Rings];
            float e = -Cell;
            for (int k = 0; k < Rings; k++)
            {
                float ex = hx + e, ez = hz + e;
                for (int p = 0; p < per; p++)
                {
                    int side = p / RingSegs; float u = (p % RingSegs) / (float)RingSegs * 2f - 1f;
                    // round the square: +z edge left to right, +x edge front to back, -z edge right to left, -x edge back to front
                    float x = side == 0 ? u * ex : side == 1 ? ex : side == 2 ? -u * ex : -ex;
                    float z = side == 0 ? ez : side == 1 ? -u * ez : side == 2 ? -ez : u * ez;
                    x += cx; z += cz;
                    ringV[k * per + p] = new Vector3(x, Height(x, z), z);
                    ringC[k * per + p] = new Color32(255, 255, 230, 255);
                }
                e = k == 0 ? 110f : e + Mathf.Max(110f, (hx + e) * (k < RingsNear ? RingGrow : RingGrowFar));
                Reach = Mathf.Max(ex, ez) * 1.42f + Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz));
            }
            for (int k = 0; k < Rings; k++)
                for (int p = 0; p < per; p++)
                {
                    Vector3 a = ringV[k * per + (p + 1) % per] - ringV[k * per + (p + per - 1) % per];
                    Vector3 b = ringV[Mathf.Min(k + 1, Rings - 1) * per + p] - ringV[Mathf.Max(k - 1, 0) * per + p];
                    var c = Vector3.Cross(b, a);
                    n[k * per + p] = (c.y < 0f ? -c : c).normalized;
                }
            for (int p = 0; p < per; p++) ringV[p].y -= 30f;   // (lowered only now: the light falls on it as on the true ground)
            var t = new int[(Rings - 1) * per * 6];
            int q = 0;
            for (int k = 0; k < Rings - 1; k++)
                for (int p = 0; p < per; p++)
                {
                    int a = k * per + p, b = k * per + (p + 1) % per, c = a + per, d = b + per;
                    t[q++] = a; t[q++] = c; t[q++] = b; t[q++] = b; t[q++] = c; t[q++] = d;
                }
            ring.Clear();
            ring.vertices = ringV; ring.normals = n; ring.colors32 = ringC; ring.triangles = t;
            ring.RecalculateBounds();
            // which way a triangle faces depends on which way round the square went: make them all face up
            var tn = ring.triangles;
            Vector3 f0 = Vector3.Cross(ringV[tn[1]] - ringV[tn[0]], ringV[tn[2]] - ringV[tn[0]]);
            if (f0.y < 0f) { for (int i = 0; i < tn.Length; i += 3) { int s = tn[i + 1]; tn[i + 1] = tn[i + 2]; tn[i + 2] = s; } ring.triangles = tn; }
        }

        // ---------------------------------------------------------------- the mountain's shadow

        float CoreAt(float x, float z)
        {
            float gx = (x - x0) / Cell, gz = (z - z0) / Cell;
            int i = (int)gx, j = (int)gz;
            if (gx < 0f || gz < 0f || i >= nx - 1 || j >= nz - 1) return float.NegativeInfinity;
            float u = gx - i, v = gz - j;
            int a = j * nx + i;
            return Mathf.Lerp(Mathf.Lerp(coreH[a], coreH[a + 1], u), Mathf.Lerp(coreH[a + nx], coreH[a + nx + 1], u), v);
        }

        const float Soft = 0.04f;   // the shadow's edge: softer the further its caster

        float ShadeAt(Vector3 p, Vector2 d, float rise, bool inCore)
        {
            float res = 1f, t = Cell * 0.9f, end = 17000f;
            if (!inCore)
            {
                // the country round it is shaded only by the mountain: where the ray crosses the fine grid
                float xa = x0, xb = x0 + (nx - 1) * Cell, za = z0, zb = z0 + (nz - 1) * Cell;
                float t0 = 0f, t1 = 60000f;
                if (Mathf.Abs(d.x) > 1e-5f) { float a = (xa - p.x) / d.x, b = (xb - p.x) / d.x; t0 = Mathf.Max(t0, Mathf.Min(a, b)); t1 = Mathf.Min(t1, Mathf.Max(a, b)); }
                else if (p.x < xa || p.x > xb) return 1f;
                if (Mathf.Abs(d.y) > 1e-5f) { float a = (za - p.z) / d.y, b = (zb - p.z) / d.y; t0 = Mathf.Max(t0, Mathf.Min(a, b)); t1 = Mathf.Min(t1, Mathf.Max(a, b)); }
                else if (p.z < za || p.z > zb) return 1f;
                if (t1 <= t0) return 1f;
                if (p.y + 20f + rise * t0 > topY + 40f) return 1f;   // (already above the summit where it reaches the mountain)
                float step = (t1 - t0) / 18f;
                for (int k = 0; k < 18; k++)
                {
                    float tt = t0 + (k + 0.5f) * step;
                    float h = CoreAt(p.x + d.x * tt, p.z + d.y * tt);
                    if (float.IsNegativeInfinity(h)) continue;
                    res = Mathf.Min(res, (p.y + 20f + rise * tt - h) / (tt * Soft));
                    if (res < -1f) break;
                }
                return Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * res);
            }
            for (int k = 0; k < 32 && t < end; k++)
            {
                float h = CoreAt(p.x + d.x * t, p.z + d.y * t);
                if (float.IsNegativeInfinity(h)) break;
                res = Mathf.Min(res, (p.y + 9f + rise * t - h) / (t * Soft));
                if (res < -1f || p.y + rise * t > topY + 40f) break;
                t *= 1.2f;
            }
            return Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * res);
        }

        /// <summary>
        /// Keep the baked shadow up with the light: `toSun` points at the sun or moon. Works through the vertices for at
        /// most `budgetMs` milliseconds a call (so it costs the frame that and no more, however slow the phone), and when
        /// it has been round them all fades from the shadow before to the new one, over about as long as the next
        /// round will take: the shadow moves smoothly behind a light that moves all the time (the time-lapse). A
        /// negative budget does it all now, with no fade.
        /// </summary>
        public void Shade(Vector3 toSun, float dt = 0f, float budgetMs = 1f)
        {
            if (core == null || coreH == null) return;
            int total = coreC.Length + ringC.Length;
            if (budgetMs > 0f && shadeBlend < 1f)
            {
                shadeBlend = Mathf.MoveTowards(shadeBlend, 1f, dt / Mathf.Max(0.1f, lastRound));
                mat.SetFloat(BlendId, shadeBlend);
            }
            if (bakeAt < 0)
            {
                if (bakedSun != Vector3.zero && Vector3.Angle(bakedSun, toSun) < 0.3f) return;
                bakingSun = toSun; bakeAt = 0; roundTime = 0f;
                if (shade == null || shade.Length != total) shade = new float[total];
            }
            roundTime += dt;
            Vector2 d = new Vector2(bakingSun.x, bakingSun.z);
            float hl = d.magnitude;
            bool overhead = hl < 0.02f;
            float rise = overhead ? 0f : bakingSun.y / hl;
            if (!overhead) d /= hl;
            watch.Restart();
            long limit = budgetMs > 0f ? (long)(budgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0) : long.MaxValue;
            while (bakeAt < total)
            {
                int stop = Mathf.Min(total, bakeAt + 48);
                for (; bakeAt < stop; bakeAt++)
                    shade[bakeAt] = overhead ? 1f : bakeAt < coreC.Length ? ShadeAt(coreV[bakeAt], d, rise, true) : ShadeAt(ringV[bakeAt - coreC.Length], d, rise, false);
                if (watch.ElapsedTicks > limit) break;
            }
            LastMs = (float)(watch.ElapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (bakeAt < total) return;
            // round done: what is showing now becomes the shadow faded from (r), the new one what it fades to (a)
            bool fade = budgetMs > 0f;
            for (int i = 0; i < total; i++)
            {
                ref Color32 c = ref (i < coreC.Length ? ref coreC[i] : ref ringC[i - coreC.Length]);
                byte now = (byte)(255f * shade[i]);
                c.r = fade ? (byte)Mathf.Lerp(c.r, c.a, shadeBlend) : now;
                c.a = now;
            }
            core.colors32 = coreC; ring.colors32 = ringC;
            shadeBlend = fade ? 0f : 1f;
            mat.SetFloat(BlendId, shadeBlend);
            if (fade) lastRound = Mathf.Max(roundTime, 0.1f);
            Rounds++;
            bakedSun = bakingSun; bakeAt = -1;
        }
        float shadeBlend = 1f, lastRound = 0.5f, roundTime;
        readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        static readonly int BlendId = Shader.PropertyToID("_ShadeBlend");
        /// <summary>Milliseconds the last call to Shade spent, and how many rounds of the shadow have been finished.</summary>
        public float LastMs { get; private set; }
        public int Rounds { get; private set; }

        /// <summary>How dark it is (0 day .. 1 night): the lights down in the valleys come on.</summary>
        public void SetNight(float darkness)
        {
            if (mat != null && mat.HasProperty(LightsId)) mat.SetFloat(LightsId, Mathf.Clamp01((darkness - 0.45f) / 0.4f) * 1.6f);
        }

        void OnDestroy()
        {
            foreach (var o in new Object[] { core, ring, mat, noise })
                if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
        }

        // ---------------------------------------------------------------- the noise the shader breaks everything up with

        /// <summary>A small tiling texture: r a soft value noise, g and b its slope in x and z, a a finer noise.</summary>
        static Texture2D MakeNoise()
        {
            const int N = 256;
            var r = new float[N * N]; var a = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    r[y * N + x] = Fbm(u, v, 5, 4, 1);
                    a[y * N + x] = Fbm(u, v, 11, 3, 7);
                }
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float gx = r[y * N + (x + 1) % N] - r[y * N + (x + N - 1) % N], gy = r[((y + 1) % N) * N + x] - r[((y + N - 1) % N) * N + x];
                    px[y * N + x] = new Color32((byte)(255f * Mathf.Clamp01(r[y * N + x])), (byte)(255f * Mathf.Clamp01(0.5f + gx * 7f)),
                                                (byte)(255f * Mathf.Clamp01(0.5f + gy * 7f)), (byte)(255f * Mathf.Clamp01(a[y * N + x])));
                }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "FarNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        static float Fbm(float u, float v, int cells, int octaves, int salt)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Lattice(u * cells, v * cells, cells, salt * 31 + o);
                norm += amp; amp *= 0.5f; cells *= 2;
            }
            // (stretched: summed octaves bunch round the middle, and the shader's thresholds want the whole range)
            return Mathf.Clamp01((sum / norm - 0.5f) * 1.7f + 0.5f);
        }

        static float Lattice(float x, float y, int period, int salt)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f); fy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period, ya = ((y0 % period) + period) % period, yb = (ya + 1) % period;
            return Mathf.Lerp(Mathf.Lerp(Hash(xa, ya, salt), Hash(xb, ya, salt), fx), Mathf.Lerp(Hash(xa, yb, salt), Hash(xb, yb, salt), fx), fy);
        }

        static float Hash(int x, int y, int salt)
        {
            unchecked
            {
                int h = (x + salt * 7919) * 374761393 + (y - salt * 104729) * 668265263;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return ((uint)(h ^ (int)((uint)h >> 16))) / 4294967296f;
            }
        }
    }
}
