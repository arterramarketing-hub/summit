using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// A layer of snow over the ground round the rider that bodies really push into. A 24 m patch on a 12.5 cm grid
    /// keeps, for every point, how far the snow there has been pushed down or piled up, how packed it is, and a
    /// stain. Whatever presses in (the thrown body's parts and board, each a capsule) carves its own shape out of
    /// the snow, a bit deeper the harder it hits; the snow it moves is not lost but heaped up in soft rims either
    /// side and ahead of the way it is going, so a sliding body ploughs a furrow with a bow wave, and the heaps slump
    /// until they are no steeper than loose snow will stand.
    ///
    /// Cheap by design: the grid is plain arrays on the CPU and each press touches only the few dozen points under
    /// a part; the GPU draws the whole patch as one fixed mesh in one draw call and lifts it into shape from two
    /// buffers (see SnowLayer.shader). The patch follows the rider in metre steps; the buffers wrap round, so a step
    /// writes only the strip that came into view, and a press uploads only the rows it changed. With nothing marked
    /// in view the layer is switched off altogether.
    ///
    /// Marks persist while they are in the patch and flatten away over its last metre and a half as it moves on.
    /// </summary>
    public class SnowLayer : MonoBehaviour
    {
        public static SnowLayer Instance { get; private set; }

        public const float Cell = 0.125f;   // metres between grid points
        const int Shown = 192;              // cells per side drawn (24 m)
        const int Slots = 200;              // per side in the buffers: a few spare, so a step never overwrites what is shown
        const int FadeCells = 12;           // marks flatten out over the patch's last 1.5 m
        const int Wander = 8;               // cells the rider can move off the patch's anchor before it steps after them
        const float Behind = 0.62f;         // how much of the patch is up the hill of the rider (marks are left behind you)
        const float Lift = 0.006f;          // disturbed snow sits this far over the terrain, so the two never fight
        const float MaxPile = 0.13f;        // no rim grows higher than this (m)
        const float Talus = Cell * 0.5f;    // the steepest loose, freshly pushed snow will stand: 27 degrees
        const float Soft = 1.3f;            // a dent's rounded edge reaches this far out, in radii
        const float Settle = 0.1f;          // per physics step where snow is moving: how much a heap spreads to each lower neighbour

        // tuning, public so a harness can try values
        public static float SlideSink = 0.03f;      // m: how far a sliding part presses below itself, at a walk
        public static float SlideSinkFast = 0.07f;  // m: extra at 8 m/s and over
        public static float HitSink = 0.04f;        // m of hole per m/s of speed into the snow, over 1.2 m/s
        public static float MaxHitSink = 0.26f;
        public static float Spill = 0.85f;          // of the snow pushed out of the way, how much heaps up (the rest is packed down)

        /// <summary>Blood and the like: what a stain looks like on the snow (alpha = how strongly it shows).</summary>
        public static Color StainColour = new Color(0.55f, 0.03f, 0.04f, 0.9f);
        /// <summary>
        /// Something pressed into the snow this physics step: where (on the snow, under its middle), its velocity,
        /// and how much snow it moved (m of depth). A sliding body fires this all along its furrow, so a listener
        /// can tint the furrow (see Stain).
        /// </summary>
        public System.Action<Vector3, Vector3, float> Pressed;

        Vector4[] ground = new Vector4[Slots * Slots];   // x: terrain height, y/z: terrain normal x/z, w: 1 snow, 0 rock or a hole
        Vector4[] snow = new Vector4[Slots * Slots];     // x: height change (m), y: packed 0..1, z: stain 0..1
        GraphicsBuffer groundBuf, snowBuf;
        Material mat, look;
        MeshRenderer mr;
        int ox = int.MinValue, oz;                       // the patch's first cell
        // rows of the snow buffer changed since the last upload, and the box round every mark (world cells)
        int dx0, dz0, dx1 = -1, dz1 = -1;
        int mx0, mz0, mx1 = -1, mz1 = -1;
        bool built;
        bool stale = true;                               // the patch's snow has not been laid where it now is
        // the shader reads these as globals: the SRP batcher only sends a material's own (cbuffer) values
        static readonly int IdGround = Shader.PropertyToID("_SnowBase"), IdSnow = Shader.PropertyToID("_SnowDyn"),
            IdGrid = Shader.PropertyToID("_SnowGrid"), IdParams = Shader.PropertyToID("_SnowParams"), IdStain = Shader.PropertyToID("_SnowStain");

        public static SnowLayer Create(Material terrainSnow)
        {
            var l = new GameObject("SnowLayer").AddComponent<SnowLayer>();
            l.Build(terrainSnow);
            return l;
        }

        void Build(Material terrainSnow)
        {
            Instance = this;
            look = terrainSnow;
            var template = Resources.Load<Material>("SnowLayer");   // keeps the shader in the build
            if (template == null || terrainSnow == null) { enabled = false; return; }
            mat = new Material(template);
            mat.CopyPropertiesFromMaterial(terrainSnow);
            mat.name = "SnowLayer";
            groundBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Slots * Slots, 16);
            snowBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Slots * Slots, 16);
            Shader.SetGlobalBuffer(IdGround, groundBuf);
            Shader.SetGlobalBuffer(IdSnow, snowBuf);
            Shader.SetGlobalVector(IdParams, new Vector4(Cell, Lift, FadeCells, 0f));

            // the grid: cell indices in uv, metres in the position (the shader puts each point on the terrain)
            int n = Shown + 1;
            var v = new Vector3[n * n];
            var uv = new Vector2[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++) { v[j * n + i] = new Vector3(i * Cell, 0f, j * Cell); uv[j * n + i] = new Vector2(i, j); }
            var tri = new int[Shown * Shown * 6];
            int t = 0;
            for (int j = 0; j < Shown; j++)
                for (int i = 0; i < Shown; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    tri[t++] = a; tri[t++] = c; tri[t++] = b;
                    tri[t++] = b; tri[t++] = c; tri[t++] = d;
                }
            var mesh = new Mesh { name = "SnowLayer" };
            mesh.indexFormat = n * n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = v; mesh.uv = uv; mesh.triangles = tri;
            mesh.UploadMeshData(true);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // the terrain under it casts the snow's shadows
            mr.receiveShadows = true;
            mr.enabled = false;
            RenderPipelineManager.beginContextRendering += BeforeRender;
            built = true;
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginContextRendering -= BeforeRender;
            groundBuf?.Release(); snowBuf?.Release();
            groundBuf = snowBuf = null;
            if (Instance == this) Instance = null;
        }

        static int Mod(int a) { a %= Slots; return a < 0 ? a + Slots : a; }
        static int Slot(int cx, int cz) => Mod(cz) * Slots + Mod(cx);
        bool InPatch(int cx, int cz) => cx >= ox && cx <= ox + Shown && cz >= oz && cz <= oz + Shown;

        /// <summary>Forget every mark (a new run).</summary>
        public void Clear()
        {
            if (!built) return;
            System.Array.Clear(snow, 0, snow.Length);
            mx1 = dx1 = sx1 = -1; mx0 = dx0 = sx0 = 0;
            mr.enabled = false;
            stale = true;   // laid afresh when something next presses in
        }

        // ---------------------------------------------------------------- following the rider

        /// <summary>Keep the patch round this point (the rider, or the thrown body): called every physics step.</summary>
        public void Focus(Vector3 p)
        {
            if (!built) return;
            int tx = Mathf.FloorToInt(p.x / Cell), tz = Mathf.FloorToInt(p.z / Cell);
            int nx = tx - Shown / 2, nz = tz - Mathf.RoundToInt(Shown * Behind);
            if (ox != int.MinValue && Mathf.Abs(nx - ox) <= Wander && Mathf.Abs(nz - oz) <= Wander) return;
            Move(nx, nz, p.y);
        }

        void Move(int nx, int nz, float y)
        {
            bool fresh = stale || ox == int.MinValue || Mathf.Abs(nx - ox) > Shown || Mathf.Abs(nz - oz) > Shown;
            int px = ox, pz = oz;
            ox = nx; oz = nz;
            // every mark has scrolled away: nothing to draw, and nothing to lay until something presses in again
            // (so riding along with no marks about costs nothing)
            if (mx1 < mx0 || mx1 < ox || mx0 > ox + Shown || mz1 < oz || mz0 > oz + Shown) { mx1 = -1; mx0 = 0; mr.enabled = false; stale = true; }
            else if (fresh) Fill(ox, oz, ox + Shown, oz + Shown);
            else
            {
                // only the strips that came into view: whole columns, then whole rows
                if (ox > px) Fill(Mathf.Max(px + Shown + 1, ox), oz, ox + Shown, oz + Shown);
                else if (ox < px) Fill(ox, oz, Mathf.Min(px - 1, ox + Shown), oz + Shown);
                if (oz > pz) Fill(ox, Mathf.Max(pz + Shown + 1, oz), ox + Shown, oz + Shown);
                else if (oz < pz) Fill(ox, oz, ox + Shown, Mathf.Min(pz - 1, oz + Shown));
            }
            transform.position = new Vector3(ox * Cell, 0f, oz * Cell);
            float side = Shown * Cell;
            mr.localBounds = new Bounds(new Vector3(side * 0.5f, y, side * 0.5f), new Vector3(side, 60f, side));
            Shader.SetGlobalVector(IdGrid, new Vector4(ox, oz, Slots, Shown));
        }

        /// <summary>Lay the patch's snow now if it has not been (the first press after riding with no marks about).</summary>
        bool Ready()
        {
            if (!built || ox == int.MinValue) return false;
            if (stale)
            {
                System.Array.Clear(snow, 0, snow.Length);
                Fill(ox, oz, ox + Shown, oz + Shown);
                stale = false;
            }
            return true;
        }

        // the terrain grid over a strip being filled, with a border for its normals
        float[] gh = new float[0];
        bool[] gcut = new bool[0];
        float[] gnx = new float[0], gnz = new float[0];
        sbyte[] grock = new sbyte[0];   // per terrain quad: bit 0 lower triangle rock, bit 1 upper, -1 not worked out yet

        /// <summary>
        /// Lay fresh, untouched snow over cells cx0..cx1, cz0..cz1: the height and smooth normal of the terrain's
        /// own triangles under each one (so the layer sits exactly on what is drawn), none on rock or over a hole.
        /// </summary>
        void Fill(int cx0, int cz0, int cx1, int cz1)
        {
            if (cx1 < cx0 || cz1 < cz0) return;
            float q = Slope.Quad;
            int gi0 = Mathf.FloorToInt(cx0 * Cell / q) - 1, gi1 = Mathf.FloorToInt(cx1 * Cell / q) + 2;
            int gj0 = Mathf.FloorToInt(cz0 * Cell / q) - 1, gj1 = Mathf.FloorToInt(cz1 * Cell / q) + 2;
            int gw = gi1 - gi0 + 1, gd = gj1 - gj0 + 1;
            if (gh.Length < gw * gd) { gh = new float[gw * gd]; gcut = new bool[gw * gd]; grock = new sbyte[gw * gd]; }
            bool danger = false;
            for (int j = gj0; j <= gj1 && !danger; j++) danger = Course.At(j * q) == Course.Section.Danger;
            for (int j = 0; j < gd; j++)
                for (int i = 0; i < gw; i++)
                {
                    float x = (gi0 + i) * q, z = (gj0 + j) * q;
                    gh[j * gw + i] = Slope.Height(x, z);
                    gcut[j * gw + i] = danger && Course.CrevasseCut(x, z) > 0.25f;
                    grock[j * gw + i] = -1;
                }

            // the terrain's smooth normal at each grid point (x and z of (nx, 1, nz)), as its mesh works it out
            if (gnx.Length < gw * gd) { gnx = new float[gw * gd]; gnz = new float[gw * gd]; }
            for (int j = 1; j < gd - 1; j++)
                for (int i = 1; i < gw - 1; i++)
                {
                    int g = j * gw + i;
                    gnx[g] = -(gh[g + 1] - gh[g - 1]) / (2f * q);
                    gnz[g] = -(gh[g + gw] - gh[g - gw]) / (2f * q);
                }

            for (int cz = cz0; cz <= cz1; cz++)
            {
                float fz = cz * Cell / q;
                int qj = Mathf.FloorToInt(fz), lj = qj - gj0;
                float v = fz - qj;
                bool ridge = Ridge.Has(qj * q) || Ridge.Has((qj + 1) * q);
                int row = Mod(cz) * Slots;
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    float fx = cx * Cell / q;
                    int qi = Mathf.FloorToInt(fx);
                    float u = fx - qi;
                    int a = lj * gw + qi - gi0;
                    bool upper = u + v > 1f;
                    // the height and normal of the terrain triangle under this point (the mesh splits each quad
                    // corner b to corner c)
                    float y, nx, nz;
                    if (!upper)
                    {
                        float wa = 1f - u - v;
                        y = gh[a] * wa + gh[a + 1] * u + gh[a + gw] * v;
                        nx = gnx[a] * wa + gnx[a + 1] * u + gnx[a + gw] * v;
                        nz = gnz[a] * wa + gnz[a + 1] * u + gnz[a + gw] * v;
                    }
                    else
                    {
                        float wd = u + v - 1f, wc = 1f - u, wb = 1f - v;
                        y = gh[a + gw + 1] * wd + gh[a + gw] * wc + gh[a + 1] * wb;
                        nx = gnx[a + gw + 1] * wd + gnx[a + gw] * wc + gnx[a + 1] * wb;
                        nz = gnz[a + gw + 1] * wd + gnz[a + gw] * wc + gnz[a + 1] * wb;
                    }
                    // a ridge crest's rounded edge, where the terrain cuts its quads into narrower strips (Ridge.Fine)
                    if (ridge && Ridge.Fine(qi, qj)) y = Ridge.MeshHeight(cx * Cell, cz * Cell);
                    float inv = 1f / Mathf.Sqrt(nx * nx + 1f + nz * nz);
                    bool hole = danger && (gcut[a] || gcut[a + 1] || gcut[a + gw] || gcut[a + gw + 1]);
                    if (grock[a] < 0) grock[a] = Rock(qi, qj, a, gw);
                    bool rock = (grock[a] & (upper ? 2 : 1)) != 0;
                    int s = row + Mod(cx);
                    ground[s] = new Vector4(y, nx * inv, nz * inv, hole || rock ? 0f : 1f);
                    snow[s] = Vector4.zero;
                }
            }
            Upload(groundBuf, ground, cx0, cz0, cx1, cz1);
            Upload(snowBuf, snow, cx0, cz0, cx1, cz1);
        }

        /// <summary>Which of a terrain quad's two triangles the terrain draws as bare rock (the same test it uses).</summary>
        sbyte Rock(int qi, int qj, int a, int gw)
        {
            float q = Slope.Quad, x = qi * q, z = qj * q;
            var pa = new Vector3(x, gh[a], z); var pb = new Vector3(x + q, gh[a + 1], z);
            var pc = new Vector3(x, gh[a + gw], z + q); var pd = new Vector3(x + q, gh[a + gw + 1], z + q);
            sbyte r = 0;
            if (RockFace.IsRock(Vector3.Cross(pc - pa, pb - pa), (pa.x + pb.x + pc.x) / 3f, (pa.z + pb.z + pc.z) / 3f)) r |= 1;
            if (RockFace.IsRock(Vector3.Cross(pc - pb, pd - pb), (pb.x + pc.x + pd.x) / 3f, (pb.z + pc.z + pd.z) / 3f)) r |= 2;
            return r;
        }

        /// <summary>
        /// Send rows cz0..cz1 to the GPU: whole rows, so it is one copy (two where they wrap round in the buffer).
        /// A few hundred kilobytes at most; many small copies cost far more than the bytes.
        /// </summary>
        static void Upload(GraphicsBuffer buf, Vector4[] data, int cx0, int cz0, int cx1, int cz1)
        {
            int rows = Mathf.Min(Slots, cz1 - cz0 + 1);
            if (cx1 < cx0 || rows <= 0) return;
            int r0 = Mod(cz0), first = Mathf.Min(rows, Slots - r0);
            buf.SetData(data, r0 * Slots, r0 * Slots, first * Slots);
            if (rows > first) buf.SetData(data, 0, 0, (rows - first) * Slots);
        }

        void BeforeRender(ScriptableRenderContext ctx, List<Camera> cams) => Flush();

        /// <summary>Send this frame's marks to the GPU (done by itself before the frame is drawn).</summary>
        public void Flush()
        {
            if (!built || ox == int.MinValue) return;
            if (dx1 >= dx0) { Upload(snowBuf, snow, dx0, dz0, dx1, dz1); dx1 = -1; dx0 = 0; }
            // follow the terrain material, in case something retints the snow
            if (look != null) { mat.SetColor("_BaseColor", look.GetColor("_BaseColor")); mat.SetFloat("_Smoothness", look.GetFloat("_Smoothness")); }
            Shader.SetGlobalColor(IdStain, StainColour);
        }

        // ---------------------------------------------------------------- pressing in

        /// <summary>The top of the snow at (x, z): terrain, lift, and whatever has been done to it there.</summary>
        public float SurfaceAt(float x, float z)
        {
            int cx = Mathf.RoundToInt(x / Cell), cz = Mathf.RoundToInt(z / Cell);
            if (!built || ox == int.MinValue || stale || !InPatch(cx, cz)) return Slope.DrawnHeight(x, z);
            int s = Slot(cx, cz);
            return ground[s].x + Lift + snow[s].x;
        }

        // the rim cells of the press being worked out, and their shares
        int[] rimCell = new int[4096];
        float[] rimShare = new float[4096];
        int sx0, sz0, sx1 = -1, sz1 = -1;   // what this physics step has touched, for the slump

        /// <summary>
        /// Press a capsule into the snow: core line a..b, footprint radius r (rDown: how far its underside reaches
        /// below the core line, r for anything round), velocity vel, pressing a further `sink` below its own
        /// underside, packing the snow it leaves to `pack` (0..1). The snow it moves heaps up round it, mostly to the
        /// sides and ahead of where it is going. Returns how much it moved (m of depth, summed over the cells).
        /// </summary>
        public float Press(Vector3 a, Vector3 b, float r, float rDown, Vector3 vel, float sink, float pack)
        {
            if (!Ready()) return 0f;
            // a part thinner than the grid still leaves a mark the grid can hold
            float rf = Mathf.Max(r, Cell * 1.1f);
            float rim = Mathf.Max(0.28f, rf * 1.7f);
            float wall = rf * Soft;          // the dent's soft edge reaches a little past the part
            float reach = wall + rim;
            int cx0 = Mathf.Max(ox + 1, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / Cell));
            int cx1 = Mathf.Min(ox + Shown - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach) / Cell));
            int cz0 = Mathf.Max(oz + 1, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach) / Cell));
            int cz1 = Mathf.Min(oz + Shown - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + reach) / Cell));
            if (cx1 < cx0 || cz1 < cz0) return 0f;

            float abx = b.x - a.x, abz = b.z - a.z, ab2 = abx * abx + abz * abz;
            float vx = vel.x, vz = vel.z, speed = Mathf.Sqrt(vx * vx + vz * vz);
            if (speed > 1e-3f) { vx /= speed; vz /= speed; }
            float moving = Mathf.Clamp01(speed / 2f);
            float moved = 0f;
            int nRim = 0;
            float shareSum = 0f;
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    float x = cx * Cell, z = cz * Cell;
                    float t = ab2 > 1e-8f ? Mathf.Clamp01(((x - a.x) * abx + (z - a.z) * abz) / ab2) : 0f;
                    float px = a.x + abx * t, pz = a.z + abz * t;
                    float ex = x - px, ez = z - pz, d = Mathf.Sqrt(ex * ex + ez * ez);
                    if (d > reach) continue;
                    int s = Slot(cx, cz);
                    if (ground[s].w < 0.5f) continue;   // rock, or over a hole
                    if (d < wall)
                    {
                        // under it: carve down to its underside, the walls rounded off (snow gives way round an edge,
                        // and a sheer wall on a 12.5 cm grid would show its steps)
                        float e = d / rf, e2 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, Soft, e));
                        float under = Mathf.Lerp(a.y, b.y, t) - (rDown + sink) * e2;
                        var c = snow[s];
                        float top = ground[s].x + Lift + c.x;
                        if (top > under)
                        {
                            float dv = top - under;
                            c.x -= dv;
                            moved += dv;
                            c.y = Mathf.Max(c.y, pack * e2);   // packed hardest in the middle, fading out up the walls
                            snow[s] = c;
                        }
                    }
                    else if (nRim < rimCell.Length)
                    {
                        // round it: a soft hump just outside the edge, heavier ahead and to the sides than behind
                        float f = (d - wall) / rim;
                        float hump = Mathf.Sin(Mathf.PI * Mathf.Pow(f, 0.7f));
                        float along = d > 1e-4f ? (ex * vx + ez * vz) / d : 0f;
                        float w = hump * (1f + moving * (1.1f * Mathf.Max(0f, along) - 0.9f * Mathf.Max(0f, -along)));
                        if (w <= 0f) continue;
                        rimCell[nRim] = s; rimShare[nRim] = w; nRim++;
                        shareSum += w;
                    }
                }
            if (moved > 0f && shareSum > 0f)
            {
                // the same volume, spread over the rim: each rim point gets its share as height
                float k = moved * Spill / shareSum;
                for (int i = 0; i < nRim; i++)
                {
                    int s = rimCell[i];
                    var c = snow[s];
                    c.x = Mathf.Min(pileCap, c.x + k * rimShare[i]);
                    snow[s] = c;
                }
            }
            if (moved > 0f)
            {
                Touch(cx0, cz0, cx1, cz1);
                Pressed?.Invoke(new Vector3((a.x + b.x) * 0.5f, SurfaceAt((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f), (a.z + b.z) * 0.5f), vel, moved);
            }
            return moved;
        }

        /// <summary>
        /// Tint the snow round a point (blood soaking in, say): amount 0..1 at the middle, fading to the edge. Shows
        /// in StainColour.
        /// </summary>
        public void Stain(Vector3 at, float radius, float amount)
        {
            if (!Ready()) return;
            int cx0 = Mathf.Max(ox + 1, Mathf.FloorToInt((at.x - radius) / Cell)), cx1 = Mathf.Min(ox + Shown - 1, Mathf.CeilToInt((at.x + radius) / Cell));
            int cz0 = Mathf.Max(oz + 1, Mathf.FloorToInt((at.z - radius) / Cell)), cz1 = Mathf.Min(oz + Shown - 1, Mathf.CeilToInt((at.z + radius) / Cell));
            if (cx1 < cx0 || cz1 < cz0) return;
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    float dx = cx * Cell - at.x, dz = cz * Cell - at.z;
                    float e = 1f - (dx * dx + dz * dz) / (radius * radius);
                    if (e <= 0f) continue;
                    int s = Slot(cx, cz);
                    var c = snow[s];
                    c.z = Mathf.Min(1f, c.z + amount * e);
                    snow[s] = c;
                }
            Dirty(cx0, cz0, cx1, cz1);
        }

        /// <summary>Cells changed by a press: they need sending to the GPU, and slumping at the end of the step.</summary>
        void Touch(int cx0, int cz0, int cx1, int cz1)
        {
            if (sx1 < sx0) { sx0 = cx0; sz0 = cz0; sx1 = cx1; sz1 = cz1; }
            else { sx0 = Mathf.Min(sx0, cx0); sz0 = Mathf.Min(sz0, cz0); sx1 = Mathf.Max(sx1, cx1); sz1 = Mathf.Max(sz1, cz1); }
            Dirty(cx0, cz0, cx1, cz1);
        }

        void Dirty(int cx0, int cz0, int cx1, int cz1)
        {
            if (dx1 < dx0) { dx0 = cx0; dz0 = cz0; dx1 = cx1; dz1 = cz1; }
            else { dx0 = Mathf.Min(dx0, cx0); dz0 = Mathf.Min(dz0, cz0); dx1 = Mathf.Max(dx1, cx1); dz1 = Mathf.Max(dz1, cz1); }
            if (mx1 < mx0) { mx0 = cx0; mz0 = cz0; mx1 = cx1; mz1 = cz1; }
            else { mx0 = Mathf.Min(mx0, cx0); mz0 = Mathf.Min(mz0, cz0); mx1 = Mathf.Max(mx1, cx1); mz1 = Mathf.Max(mz1, cz1); }
            mr.enabled = true;
        }

        /// <summary>
        /// Heaps slump: freshly moved snow settles onto its lower neighbours, fast anywhere it stands steeper than
        /// loose snow can (never back into a dent: the packed walls of a hole hold). Only round what moved snow this
        /// step, so old marks keep their shape.
        /// </summary>
        void Slump()
        {
            if (sx1 < sx0) return;
            int cx0 = Mathf.Max(ox + 1, sx0 - 1), cx1 = Mathf.Min(ox + Shown - 1, sx1 + 1);
            int cz0 = Mathf.Max(oz + 1, sz0 - 1), cz1 = Mathf.Min(oz + Shown - 1, sz1 + 1);
            sx1 = -1; sx0 = 0;
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int s = Slot(cx, cz);
                    float h = snow[s].x;
                    if (h <= 0.002f) continue;
                    h = Spread(s, h, Slot(cx + 1, cz)); h = Spread(s, h, Slot(cx - 1, cz));
                    h = Spread(s, h, Slot(cx, cz + 1)); h = Spread(s, h, Slot(cx, cz - 1));
                }
            Dirty(cx0, cz0, cx1, cz1);
        }

        float Spread(int s, float h, int n)
        {
            var c = snow[n];
            if (c.x < -0.02f || ground[n].w < 0.5f) return h;
            float diff = h - c.x;
            if (diff <= 0f) return h;
            // freshly moved snow settles a little into its neighbours (rounds off the rims, and the steps a grid
            // would leave in them); more where it stands steeper than loose snow will
            float m = diff * Settle + Mathf.Max(0f, diff - Talus) * 0.25f;
            c.x += m; snow[n] = c;
            var me = snow[s]; me.x -= m; snow[s] = me;
            return me.x;
        }

        // ---------------------------------------------------------------- the thrown body

        float[] lastDown = new float[16];
        float pileCap = MaxPile;

        /// <summary>
        /// A fatal fall: the body driven `depth` metres down into the snow in its own shape, and the snow it
        /// threw out heaped high round the hole, much higher than a slide's rims.
        /// </summary>
        public void Bury(Ragdoll body, float depth)
        {
            if (!built || body == null || ox == int.MinValue) return;
            pileCap = MaxPile * 2.5f;
            for (int i = 0; i < body.ShapeCount; i++)
            {
                if (!body.Capsule(i, out Vector3 a, out Vector3 b, out float r, out float rDown, out Vector3 v, out _)) continue;
                Vector3 mid = (a + b) * 0.5f;
                float top = SurfaceAt(mid.x, mid.z);
                // down from the surface, whatever height the part has bounced to
                Vector3 lift = Vector3.up * (top + r - Mathf.Min(a.y, b.y));
                // (a little wider than the part: walls the 12.5 cm grid can round off, rather than stepped shafts)
                Press(a + lift, b + lift, r * 1.6f, rDown, v, depth, 1f);
            }
            pileCap = MaxPile;
            Slump();
        }

        /// <summary>
        /// Every physics step of a crash: each part of the body and the board presses its own shape into the snow
        /// where it lies. Sliding, a little below itself (more the faster it goes): a furrow. Hitting, a hole as
        /// deep as it came in hard.
        /// </summary>
        public void PressBody(Ragdoll body)
        {
            if (!built || body == null || ox == int.MinValue) return;
            int n = body.ShapeCount;
            if (lastDown.Length < n) lastDown = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!body.Capsule(i, out Vector3 a, out Vector3 b, out float r, out float rDown, out Vector3 v, out float mass)) continue;
                float low = Mathf.Min(a.y, b.y) - rDown;
                Vector3 mid = (a + b) * 0.5f;
                float top = SurfaceAt(mid.x, mid.z);
                float down = Mathf.Max(0f, -v.y);
                float wasDown = lastDown[i];
                lastDown[i] = down;
                if (low > top + 0.01f) continue;   // in the air
                // heavier parts sink further; the speed it came in at (last step, before the snow stopped it) digs the hole
                float heavy = Mathf.Clamp01(mass / 12f);
                float flat = new Vector2(v.x, v.z).magnitude;
                float sink = (SlideSink + SlideSinkFast * Mathf.Clamp01(flat / 8f)) * (0.5f + 0.5f * heavy);
                float hit = Mathf.Max(down, wasDown);
                sink += Mathf.Min(MaxHitSink, Mathf.Max(0f, hit - 1.2f) * HitSink) * (0.6f + 0.4f * heavy);
                float pack = Mathf.Clamp01(0.35f + flat * 0.05f + hit * 0.06f);
                Press(a, b, r, rDown, v, sink, pack);
            }
            Slump();
        }
    }
}
