using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The board's track as real geometry: a cut in the snow, shaped by the snow it is in (Mix, the snow underfoot)
    /// and by how hard the edge is pushing (Bite) at the angle it is on (edge):
    ///   corduroy  a crisp, shallow carved line: a compressed, darker, slightly glossy floor and a thin sharp wall on
    ///             the outside of the turn, a mere lip on the inside
    ///   powder    a wide, deep trench: tall crumbly walls, clumps thrown out along the sides, a shadowed interior
    ///   ice       barely a scrape: a narrow polished line, no walls
    ///   crud, crust, slush: between (crust breaks into plates, slush is wet and dark)
    /// Skidding sweeps it wide and low; carving cuts it narrow with the tall wall to the outside of the turn.
    /// Drawn with Resources/SnowTrack.shader in the snow's own texture (so its walls are the snow round them), lit by
    /// the floodlights too, and faded by age and distance pixel by pixel (no popping).
    /// Built as a chain of small meshes; only the newest one is rebuilt as you ride, and in it only the newest row of
    /// the track (and the normals of the row before it, which the new row's faces touch).
    /// </summary>
    public class SnowTrail
    {
        const int PerChunk = 56, MaxChunks = 24, Across = 15;
        const float Step = 0.25f;

        struct Pt { public Vector3 p, r, n; public float s, edge, skid, d, bite, born; public SnowMix mix; }

        /// <summary>The snow under this stretch of the track (set before Feed: the rider's Underfoot).</summary>
        public SnowMix Mix;
        /// <summary>0..1: how hard the edge is pushing (Rider.Bite), set before Feed.</summary>
        public float Bite;
        /// <summary>The clock the track ages on (seconds): the game's, or a test tool's own (&gt;= 0).</summary>
        public static double TestClock = -1.0;
        public static float Clock => TestClock >= 0.0 ? (float)TestClock : Time.time;
        static readonly int ClockId = Shader.PropertyToID("_TrackClock");

        readonly Material mat;
        readonly Transform root;
        readonly List<Pt> pts = new List<Pt>(PerChunk + 1);
        readonly Queue<GameObject> chunks = new Queue<GameObject>();
        readonly Queue<GameObject> spare = new Queue<GameObject>();
        GameObject current;
        Mesh currentMesh;
        bool wasOn;
        float travelled;
        Vector3[] v = new Vector3[(PerChunk + 1) * Across];
        Vector3[] nrm = new Vector3[(PerChunk + 1) * Across];
        Vector2[] c = new Vector2[(PerChunk + 1) * Across];
        Color[] col = new Color[(PerChunk + 1) * Across];
        int rowsBuilt;   // the rows of the current chunk already worked out (each depends only on its own point)
        // one side's profile, centre outward: how far out, and how high (reused from row to row)
        readonly float[] profOut = new float[7], profUp = new float[7];
        // across the track (left to right): frayed edge, outer slope, clod, crest, lip, wall, floor ... and back
        static readonly float[] U = { 0.0f, 0.12f, 0.22f, 0.27f, 0.33f, 0.38f, 0.43f, 0.5f, 0.57f, 0.62f, 0.67f, 0.73f, 0.78f, 0.88f, 1.0f };
        int[] tri;

        /// <summary>
        /// Across the track: snow, bright thrown-up crest, shaded inner wall, packed floor, and back.
        /// Bottom row: a clean carve (dark, polished cut); top row: a skid (scraped, lighter smear).
        /// </summary>
        public static Texture2D Strip()
        {
            const int n = 128;
            var t = new Texture2D(n, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int row = 0; row < 2; row++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, e = Mathf.Abs(u - 0.5f) * 2f;   // 0 centre .. 1 edge
                    Color snow = new Color(0.90f, 0.92f, 0.97f), crest = new Color(0.97f, 0.98f, 1f);
                    // packed snow is shaded, not blue: only the faintest cool tint in the cut
                    Color wall = row == 0 ? new Color(0.84f, 0.87f, 0.93f) : new Color(0.88f, 0.90f, 0.95f);
                    Color floor = row == 0 ? new Color(0.74f, 0.78f, 0.86f) : new Color(0.83f, 0.86f, 0.92f);
                    Color col = e < 0.26f ? Color.Lerp(floor, wall, Mathf.SmoothStep(0f, 1f, e / 0.26f))
                              : e < 0.44f ? Color.Lerp(wall, crest, Mathf.SmoothStep(0f, 1f, (e - 0.26f) / 0.18f))
                              : Color.Lerp(crest, snow, (e - 0.44f) / 0.56f);
                    t.SetPixel(x, row, col);
                }
            t.Apply();
            return t;
        }

        float sEdge, sSkid, sBite;
        readonly bool tracked;
        Material snowSeen;
        Color colourSeen;

        /// <summary>The snow's own textures and colour (the terrain's material, Surfaces.Snow), followed as the weather changes it.</summary>
        void Snowed()
        {
            var snow = Surfaces.Snow;
            if (!tracked || snow == null) return;
            if (snow != snowSeen)
            {
                snowSeen = snow;
                if (snow.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", snow.GetTexture("_BaseMap")); mat.SetTextureScale("_BaseMap", snow.GetTextureScale("_BaseMap")); mat.SetTextureOffset("_BaseMap", snow.GetTextureOffset("_BaseMap")); }
                if (snow.HasProperty("_DetailAlbedoMap")) { mat.SetTexture("_DetailAlbedoMap", snow.GetTexture("_DetailAlbedoMap")); mat.SetTextureScale("_DetailAlbedoMap", snow.GetTextureScale("_DetailAlbedoMap")); mat.SetTextureOffset("_DetailAlbedoMap", snow.GetTextureOffset("_DetailAlbedoMap")); }
            }
            var c0 = snow.HasProperty("_BaseColor") ? snow.GetColor("_BaseColor") : Color.white;
            if (c0 != colourSeen) { colourSeen = c0; mat.SetColor("_BaseColor", c0); }
        }

        public SnowTrail(Shader lit)
        {
            var track = Resources.Load<Shader>("SnowTrack");
            tracked = track != null && track.isSupported;
            mat = new Material(tracked ? track : lit) { name = "SnowTrack" };
            if (!tracked)
            {
                mat.SetTexture("_BaseMap", Strip());
                mat.SetFloat("_Smoothness", 0.12f);   // matte, so every lump in the berm shades
            }
            mat.SetColor("_BaseColor", Color.white);
            root = new GameObject("SnowTrail").transform;
            tri = new int[PerChunk * (Across - 1) * 6];
            int t = 0;
            for (int i = 0; i < PerChunk; i++)
                for (int j = 0; j < Across - 1; j++)
                {
                    int a = i * Across + j, b = a + 1, d = a + Across, e = d + 1;
                    tri[t++] = a; tri[t++] = d; tri[t++] = b;
                    tri[t++] = b; tri[t++] = d; tri[t++] = e;
                }
        }

        public void Clear()
        {
            while (chunks.Count > 0) Recycle(chunks.Dequeue());
            if (current != null) Recycle(current);
            current = null; pts.Clear(); wasOn = false;
        }

        void Recycle(GameObject g) { g.SetActive(false); spare.Enqueue(g); }

        /// <summary>
        /// Called every frame with the board's contact point. travel: the direction you are moving
        /// (the track runs along it, not along the board); edge: -1..1 how far onto which edge
        /// (positive = turning right); skid: 0..1 how sideways the board is sliding.
        /// </summary>
        public void Feed(Vector3 at, Vector3 normal, float travel, bool onSnow, float scale, float edge, float skid, float dt)
        {
            Shader.SetGlobalFloat(ClockId, Clock);
            Snowed();
            if (!onSnow) { if (wasOn) Break(); wasOn = false; sEdge = 0f; sSkid = 0f; sBite = 0f; return; }
            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * 10f);
            sEdge = Mathf.Lerp(sEdge, Mathf.Clamp(edge, -1f, 1f), k);
            sSkid = Mathf.Lerp(sSkid, Mathf.Clamp01(skid), k);
            sBite = Mathf.Lerp(sBite, Mathf.Clamp01(Bite), k);
            var dir = new Vector3(Mathf.Sin(travel), 0f, Mathf.Cos(travel));
            var right = Vector3.Cross(normal, Vector3.ProjectOnPlane(dir, normal).normalized);
            var pt = new Pt { p = at, r = right, n = normal, s = scale, edge = sEdge, skid = sSkid, d = travelled, bite = sBite, born = Clock, mix = Mix };
            if (!wasOn || pts.Count == 0) { Begin(pt); wasOn = true; return; }
            float d = Vector3.Distance(pts[pts.Count - 1].p, at);
            if (d < Step) return;
            travelled += d;
            pt.d = travelled;
            pts.Add(pt);
            if (pts.Count > PerChunk) { var last = pts[pts.Count - 1]; Seal(); Begin(last); return; }
            Rebuild();
        }

        void Begin(Pt first)
        {
            current = spare.Count > 0 ? spare.Dequeue() : NewChunk();
            current.SetActive(true);
            currentMesh = current.GetComponent<MeshFilter>().sharedMesh;
            currentMesh.Clear();
            pts.Clear();
            pts.Add(first);
            rowsBuilt = 0;
        }

        void Break()
        {
            if (current != null && pts.Count > 1) Seal();
            else if (current != null) Recycle(current);
            current = null; pts.Clear();
        }

        void Seal()
        {
            Rebuild();
            chunks.Enqueue(current);
            while (chunks.Count > MaxChunks) Recycle(chunks.Dequeue());
            var keep = pts[pts.Count - 1];
            pts.Clear();
            pts.Add(keep);
            current = null;
        }

        GameObject NewChunk()
        {
            var g = new GameObject("Track");
            g.transform.SetParent(root, false);
            var m = new Mesh();
            m.MarkDynamic();
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            return g;
        }

        // ---- noise along the track, so the berms are lumpy and never repeat

        static float Hash(int i, int seed)
        {
            uint h = (uint)(i * 374761393 + seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
        }

        /// <summary>Smooth 1-D value noise, 0..1, features about `len` metres long.</summary>
        static float N(float d, float len, int seed)
        {
            float x = d / len;
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash(i, seed), Hash(i + 1, seed), f);
        }

        /// <summary>Lumps at three sizes, centred on 1: about 0.45 .. 1.6.</summary>
        static float Lumps(float d, int seed) =>
            0.35f + 0.55f * N(d, 1.9f, seed) + 0.45f * N(d, 0.6f, seed + 7) + 0.3f * N(d, 0.26f, seed + 13);

        /// <summary>A clod of snow broken off the crest: 0 most of the way, bumps up now and then.</summary>
        static float Clod(float d, int seed)
        {
            float x = d / 0.5f;
            int i = Mathf.FloorToInt(x);
            float h = Hash(i, seed);
            if (h < 0.55f) return 0f;
            float f = x - i;
            return (h - 0.55f) / 0.45f * Mathf.Sin(f * Mathf.PI);
        }

        /// <summary>The cut's shape for the snow it is in: blended from each kind's by how much of it there is.</summary>
        struct Shape
        {
            public float hw, skidW, wallOut, wallIn, berm, crumb, clump, floorTint, wallTint, gloss, shade, lip;
            public static Shape operator *(Shape a, float k) => new Shape { hw = a.hw * k, skidW = a.skidW * k, wallOut = a.wallOut * k, wallIn = a.wallIn * k, berm = a.berm * k, crumb = a.crumb * k, clump = a.clump * k, floorTint = a.floorTint * k, wallTint = a.wallTint * k, gloss = a.gloss * k, shade = a.shade * k, lip = a.lip * k };
            public static Shape operator +(Shape a, Shape b) => new Shape { hw = a.hw + b.hw, skidW = a.skidW + b.skidW, wallOut = a.wallOut + b.wallOut, wallIn = a.wallIn + b.wallIn, berm = a.berm + b.berm, crumb = a.crumb + b.crumb, clump = a.clump + b.clump, floorTint = a.floorTint + b.floorTint, wallTint = a.wallTint + b.wallTint, gloss = a.gloss + b.gloss, shade = a.shade + b.shade, lip = a.lip + b.lip };
        }
        //                                          floor half-width, skid's extra, outer wall, inner wall, berm, crumble, clumps, floor tint, wall tint, gloss, shade, lip
        static readonly Shape Cord   = new Shape { hw = 0.06f, skidW = 0.30f, wallOut = 0.028f, wallIn = 0.010f, berm = 0.05f, crumb = 0.08f, clump = 0.00f, floorTint = 0.90f, wallTint = 1.00f, gloss = 0.55f, shade = 0.06f, lip = 1.0f };
        static readonly Shape Powder = new Shape { hw = 0.20f, skidW = 0.32f, wallOut = 0.15f,  wallIn = 0.11f,  berm = 0.30f, crumb = 0.55f, clump = 1.00f, floorTint = 0.80f, wallTint = 0.97f, gloss = 0.06f, shade = 0.30f, lip = 0.0f };
        static readonly Shape Ice    = new Shape { hw = 0.035f, skidW = 0.16f, wallOut = 0.004f, wallIn = 0.002f, berm = 0.02f, crumb = 0.00f, clump = 0.00f, floorTint = 1.04f, wallTint = 1.00f, gloss = 0.30f, shade = 0.00f, lip = 0.0f };
        static readonly Shape Crud   = new Shape { hw = 0.14f, skidW = 0.30f, wallOut = 0.08f,  wallIn = 0.05f,  berm = 0.22f, crumb = 0.65f, clump = 0.70f, floorTint = 0.86f, wallTint = 0.97f, gloss = 0.10f, shade = 0.18f, lip = 0.0f };
        static readonly Shape Crust  = new Shape { hw = 0.10f, skidW = 0.28f, wallOut = 0.06f,  wallIn = 0.04f,  berm = 0.16f, crumb = 0.40f, clump = 0.60f, floorTint = 0.88f, wallTint = 1.00f, gloss = 0.25f, shade = 0.12f, lip = 0.5f };
        static readonly Shape Slush  = new Shape { hw = 0.10f, skidW = 0.30f, wallOut = 0.06f,  wallIn = 0.04f,  berm = 0.12f, crumb = 0.25f, clump = 0.30f, floorTint = 0.80f, wallTint = 0.90f, gloss = 0.60f, shade = 0.10f, lip = 0.3f };

        static Shape ShapeOf(SnowMix m)
        {
            float cord = m.Corduroy, sum = cord + m.powder + m.ice + m.crud + m.crust + m.slush;
            if (sum < 1e-4f) return Cord;
            // (a little powder lying on the run is not a trench: the deep kind is, Rider's Deep)
            float deep = Mathf.Clamp01(m.powder * (0.45f + 0.55f * m.deep));
            float light = m.powder - deep;
            var s = Cord * (cord + light * 0.6f) + Powder * deep + Crud * (m.crud + light * 0.4f) + Ice * m.ice + Crust * m.crust + Slush * m.slush;
            return s * (1f / sum);
        }

        void Rebuild()
        {
            int n = pts.Count;
            if (n < 2 || currentMesh == null) return;
            for (int i = rowsBuilt; i < n; i++)
            {
                var q = pts[i];
                float a = Mathf.Abs(q.edge), sk = q.skid, s = q.s, bite = q.bite;
                var sh = ShapeOf(q.mix);
                // the cut: narrow when carving, a wide sweep when the board is sideways; a hard push opens it a little
                float hw = (sh.hw * (1f - 0.25f * a + 0.3f * bite) + sh.skidW * sk) * s;
                // the outside of the turn takes the tall wall (a carve's), the inside a lower one; skidding flattens both
                // (a hard push builds the crisp wall of a carve most; deep snow is already as deep as it goes)
                float push = 1f + (1.4f * bite + 0.6f * a) * (1f - 0.7f * sh.crumb);
                float outWall = Mathf.Min(sh.wallOut * push * (1f - 0.45f * sk), 0.2f) * s, inWall = Mathf.Min(sh.wallIn * (1f + 0.4f * bite) * (1f - 0.3f * a) * (1f - 0.3f * sk), 0.15f) * s;
                float bermW = sh.berm * s * (1f + 1.2f * sk);
                bool outLeft = q.edge >= 0f;   // turning right throws snow to the left (-r)
                float shelf = 0.025f * a * s * (1f - sh.crumb);   // a carve's floor slants up toward the outside edge

                Vector3 o = q.p + q.n * 0.012f;
                int k = i * Across;
                var uv = new Vector2(q.born, 1f);
                Color floor = new Color(sh.floorTint * (1f - sh.shade), sh.floorTint * (1f - sh.shade * 0.85f), sh.floorTint * (1f - sh.shade * 0.55f), Mathf.Clamp01(sh.gloss));
                v[k + 7] = o + q.n * shelf * 0.4f;
                c[k + 7] = uv; col[k + 7] = floor;
                for (int side = -1; side <= 1; side += 2)
                {
                    bool outside = (side < 0) == outLeft;
                    int seed = side < 0 ? 11 : 37;
                    float lump = 1f + (Lumps(q.d, seed) - 1f) * (0.3f + sh.crumb), widen = Lumps(q.d + 17.3f, seed + 3);
                    float clod = Clod(q.d, seed + 5) * sh.clump;
                    float wall = (outside ? outWall : inWall) * lump;
                    float bw = bermW * (0.65f + 0.5f * widen) * (outside ? 1f : 0.7f);
                    float shf = outside ? shelf : 0f;
                    float fray = (N(q.d, 0.3f, seed + 9) - 0.5f) * 0.16f * s * (0.3f + sh.crumb);   // a ragged outer edge
                    float jag = (N(q.d, 0.22f, seed + 15) - 0.5f) * 0.25f * (0.2f + sh.crumb);      // the crest wanders in and out
                    // a crisp cut has a near-vertical wall (a thin sharp edge); a crumbly one leans back
                    float lean = Mathf.Lerp(0.012f, 0.35f * wall + 0.02f, Mathf.Clamp01(sh.crumb * 1.6f)) * (outside ? 1f : 1.4f);
                    Vector3 r = q.r * side;
                    // centre outward: floor, the foot of the wall, the wall's top, the crest, the berm (and its clumps), its toe, the snow
                    profOut[0] = hw * 0.55f;                           profUp[0] = 0.004f + shf * 0.7f;
                    profOut[1] = hw * 0.97f;                           profUp[1] = 0.006f + shf;
                    profOut[2] = hw + lean;                            profUp[2] = wall * (0.92f + 0.1f * sh.lip) + shf;
                    profOut[3] = hw + lean + bw * (0.18f + jag);       profUp[3] = wall * (1.0f + 0.25f * clod) + shf * 0.5f;
                    profOut[4] = hw + lean + bw * (0.45f + 0.2f * clod); profUp[4] = wall * (0.5f + 0.45f * clod);
                    profOut[5] = hw + lean + bw * (0.75f - jag);       profUp[5] = wall * (0.18f + 0.35f * clod + 0.2f * N(q.d, 0.3f, seed + 31) * sh.crumb);
                    profOut[6] = hw + lean + bw * 1.05f + fray;        profUp[6] = 0.003f;
                    for (int j = 0; j < profOut.Length; j++)
                    {
                        int cc = side < 0 ? 6 - j : 8 + j;
                        v[k + cc] = o + r * profOut[j] + q.n * profUp[j];
                        c[k + cc] = uv;
                        // the floor and the foot of the wall in its shade, the wall's top and the thrown snow bright and
                        // matte, a little jitter along the crumbly crest so the snow is not one flat tone
                        float jit = j >= 3 ? (N(q.d, 0.35f, seed + 21 + j) - 0.5f) * 0.06f * sh.crumb : 0f;
                        float t = j == 0 ? 0f : j == 1 ? 0.15f : j == 2 ? 0.85f : 1f;
                        float w = Mathf.Lerp(1f, sh.wallTint, t) + jit;
                        col[k + cc] = Color.Lerp(floor, new Color(w, w, w, 0.12f), t);
                    }
                }
            }
            // the normals of the new rows and of the row before them (the new faces touch it), as RecalculateNormals
            // makes them: each corner's faces summed, the bigger ones counting for more
            for (int i = Mathf.Max(0, rowsBuilt - 1); i < n; i++) RowNormals(i, n);
            rowsBuilt = n;
            currentMesh.Clear();
            int vc = n * Across;
            currentMesh.SetVertices(v, 0, vc);
            currentMesh.SetNormals(nrm, 0, vc);
            currentMesh.SetUVs(0, c, 0, vc);
            currentMesh.SetColors(col, 0, vc);
            currentMesh.SetTriangles(tri, 0, (n - 1) * (Across - 1) * 6, 0);
            currentMesh.RecalculateBounds();
        }

        /// <summary>Row i's normals from the faces either side of it (n rows so far), wound as `tri` winds them.</summary>
        void RowNormals(int i, int n)
        {
            int k = i * Across;
            for (int j = 0; j < Across; j++) nrm[k + j] = Vector3.zero;
            for (int strip = i - 1; strip <= i; strip++)
            {
                if (strip < 0 || strip >= n - 1) continue;
                for (int j = 0; j < Across - 1; j++)
                {
                    int a = strip * Across + j, b = a + 1, d = a + Across, e = d + 1;
                    Face(a, d, b, k); Face(b, d, e, k);
                }
            }
            for (int j = 0; j < Across; j++) nrm[k + j] = nrm[k + j].normalized;
        }

        // a face's normal added to those of its corners that are on the row from k
        void Face(int a, int b, int d, int k)
        {
            Vector3 f = Vector3.Cross(v[b] - v[a], v[d] - v[a]);
            if (a >= k && a < k + Across) nrm[a] += f;
            if (b >= k && b < k + Across) nrm[b] += f;
            if (d >= k && d < k + Across) nrm[d] += f;
        }
    }
}
