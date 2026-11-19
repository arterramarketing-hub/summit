using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The chairlifts you see: detachable four-seat chairs running up one side of a line of steel towers and
    /// back down the other, between a station at each end. Where they run is LiftLine's business (a pure function
    /// of the day's mountain); this draws them near the rider and makes them solid.
    ///
    /// The models are made in Blender by Tools/MakeLift.py (Resources/Lift/Lift.fbx), every part painted from one
    /// small palette picture, so the whole lift is one material:
    ///   - Towers (a mast stretched to each tower's height, a head with its crossarm and platforms, a safety pad,
    ///     two sheave trains set along the cable), chairs, the riders on them and the stations are drawn in
    ///     instanced batches, one draw call a part. Within NearRange the full models, casting shadows; beyond it
    ///     plain versions, out to FarRange, where the fog has all but taken them.
    ///   - The cable is one small mesh for each span (both ropes, sagging), built as the span comes into range
    ///     and freed behind the rider, with the towers' and stations' colliders.
    ///   - The chairs have no state: each frame every chair's place on the loop is worked out from the clock
    ///     (LiftLine.Lift.Loop), with a slow swing of its own and a kick that dies away as it comes off each
    ///     tower's sheaves. More than half the chairs going up carry one to four seated riders, in jackets of
    ///     eight colours, skis on the footrest or a board hanging; hardly any coming down do. Who sits where is
    ///     drawn afresh each time a chair leaves the bottom station.
    ///
    /// Solid: a tower's mast and pad, and each station, are colliders marked as obstacles, so riding into one off
    /// the piste is a crash like a tree ("Lift tower", "Lift station"). A chair is solid too: a jump high enough to
    /// reach one is a crash ("Chairlift!", or a shield spent), found by a simple test against the few nearest
    /// chairs. The cable itself is not: it is a few centimetres thick, and being clotheslined by something you
    /// cannot see coming would only feel unfair. Nothing hangs within 9 m of the piste's snow (LiftLine).
    ///
    /// At dusk and night the work lamps under each crossarm, the red beacon on each tower and the stations'
    /// windows and floodlights glow (the palette's glow picture, brought up with DayNight.Darkness), and one real
    /// light sits at whichever lamp is nearest the rider.
    /// </summary>
    public class SkiLift : MonoBehaviour
    {
        public const float NearRange = 140f;       // full towers and shadows (the render asset's shadow distance)
        public const float ChairNear = 100f;       // full chairs and riders
        public const float FarRange = 650f;        // nothing of the lift is drawn or built past here
        public float behind = 150f;                // kept this far up the hill behind the rider
        public const float ColliderRange = 170f;

        /// <summary>The one call the game makes: the lifts of whatever mountain is current, round this rider.</summary>
        public static SkiLift Create(Transform target, Shader lit)
        {
            var s = new GameObject("SkiLift").AddComponent<SkiLift>();
            s.target = target;
            s.rider = target != null ? target.GetComponent<Rider>() : null;
            s.Build(lit);
            return s;
        }

        Transform target;
        Rider rider;
        Material paint, cableMat;
        bool ready;
        int seed = int.MinValue;

        // ---------------------------------------------------------------- the models

        class Part
        {
            public Mesh mesh; public Matrix4x4 local = Matrix4x4.identity; public bool shadows;
            public readonly List<Matrix4x4> list = new List<Matrix4x4>();
            public int Tris => mesh != null ? (int)(mesh.GetIndexCount(0) / 3) : 0;
        }
        Part mast, mastLod, head, headLod, pad, sheaves, sheavesLod, chair, chairLod, station;
        readonly Part[] riders = new Part[4], ridersLod = new Part[4];
        readonly List<Part> parts = new List<Part>();

        void Build(Shader lit)
        {
            var prefab = Resources.Load<GameObject>("Lift/Lift");
            if (prefab == null) { Debug.LogWarning("SkiLift: Resources/Lift/Lift.fbx is missing"); return; }
            var found = new Dictionary<string, Part>();
            var rootInv = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                // relative to the root, keeping the root's own rotation (how Blender's axes come in), as Forest does
                found[mf.name] = new Part { mesh = mf.sharedMesh, local = Matrix4x4.Rotate(prefab.transform.localRotation) * rootInv * mf.transform.localToWorldMatrix };
            }
            System.Func<string, bool, Part> P = (name, shadows) =>
            {
                if (!found.TryGetValue(name, out var p)) { Debug.LogWarning("SkiLift: no part called " + name + " in Lift.fbx"); p = new Part(); }
                p.shadows = shadows;
                parts.Add(p);
                return p;
            };
            mast = P("Mast", true); mastLod = P("MastLod", false);
            head = P("Head", true); headLod = P("HeadLod", false);
            pad = P("Pad", false);
            sheaves = P("Sheaves", false); sheavesLod = P("SheavesLod", false);
            chair = P("Chair", true); chairLod = P("ChairLod", false);
            station = P("Station", true);
            for (int i = 0; i < 4; i++)
            {
                riders[i] = P("Riders" + (char)('A' + i), false);
                ridersLod[i] = P("Riders" + (char)('A' + i) + "Lod", false);
            }
            if (chair.mesh == null || mast.mesh == null) return;

            // The file is made in the game's axes, a chair facing +z with its seat below its grip. If the import
            // turned it round, turn every part back (and say so: the importer settings in Lift.fbx.meta have changed)
            var cb = chair.mesh.bounds;
            Vector3 cc = chair.local.MultiplyPoint3x4(cb.center);
            if (cc.y > -0.5f) Debug.LogWarning("SkiLift: Lift.fbx did not come in y-up (a chair's middle is at " + cc + ")");
            else if (cc.z < 0f)
            {
                Debug.LogWarning("SkiLift: Lift.fbx came in facing backward; turned round");
                var turn = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));
                foreach (var p in parts) p.local = turn * p.local;
            }

            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp != null) lit = urp;   // (whatever the snow is drawn with these days, the lift wants the plain lit shader)
            var paintTex = Resources.Load<Texture2D>("Lift/LiftPaint");
            var glowTex = Resources.Load<Texture2D>("Lift/LiftGlow");
            paint = new Material(lit) { name = "LiftPaint", enableInstancing = true };
            paint.SetColor("_BaseColor", Color.white);
            if (paintTex != null) paint.SetTexture("_BaseMap", paintTex);
            paint.SetFloat("_Smoothness", 0.42f); paint.SetFloat("_Metallic", 0.15f);
            paint.EnableKeyword("_EMISSION");
            if (glowTex != null) paint.SetTexture("_EmissionMap", glowTex);
            paint.SetColor("_EmissionColor", Color.black);
            cableMat = new Material(lit) { name = "LiftCable" };
            cableMat.SetColor("_BaseColor", new Color(0.13f, 0.13f, 0.14f));
            cableMat.SetFloat("_Smoothness", 0.5f); cableMat.SetFloat("_Metallic", 0.6f);
            cableMat.SetFloat("_Cull", 0f);

            var lg = new GameObject("LiftLamp");
            lg.transform.SetParent(transform, false);
            lamp = lg.AddComponent<Light>();
            lamp.type = LightType.Point; lamp.color = new Color(1f, 0.9f, 0.74f); lamp.range = 34f; lamp.shadows = LightShadows.None;
            lamp.enabled = false;
            ready = true;
            if (System.Environment.GetEnvironmentVariable("SUMMIT_LIFTCHECK") == "1") Debug.Log("SkiLift\n" + LiftLine.Report());
        }

        // ---------------------------------------------------------------- what is built near the rider

        class Live
        {
            public LiftLine.Lift lift;
            public GameObject root;
            public GameObject[] span, tower;        // each span's cable; each tower's collider
            public GameObject stationTop, stationBottom;
        }
        readonly List<Live> live = new List<Live>();
        float refreshClock;
        Vector3 refreshedAt = new Vector3(0f, -1e9f, 0f);

        void Update()
        {
            if (!ready || target == null) return;
            if (seed != Slope.Seed) { Clear(); seed = Slope.Seed; }
            Vector3 p = target.position;
            refreshClock -= Time.unscaledDeltaTime;
            if (refreshClock <= 0f || (p - refreshedAt).sqrMagnitude > 12f * 12f)
            {
                refreshClock = 0.25f; refreshedAt = p;
                Refresh(p);
            }
            Chairs(p);
            Draw();
            Glow(p);
            Sound(p);
        }

        /// <summary>A new mountain: everything built for the old one goes.</summary>
        public void Clear()
        {
            foreach (var lv in live) Free(lv);
            live.Clear();
            foreach (var part in parts) part.list.Clear();
            refreshClock = 0f;
            clackTower = -1;
        }

        void Free(Live lv)
        {
            if (lv.root == null) return;
            foreach (var mf in lv.root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) Destroy(mf.sharedMesh);   // the cables' meshes are ours
            Destroy(lv.root);
            lv.root = null;
        }

        bool InRange(float zMin, float zMax, float z) => zMax > z - behind && zMin < z + FarRange;

        void Refresh(Vector3 p)
        {
            var lifts = LiftLine.Lifts;
            // lifts coming into range, and going out of it
            for (int i = live.Count - 1; i >= 0; i--)
                if (!InRange(live[i].lift.ZMin, live[i].lift.ZMax, p.z)) { Free(live[i]); live.RemoveAt(i); }
            foreach (var l in lifts)
            {
                if (!InRange(l.ZMin, l.ZMax, p.z)) continue;
                bool have = false;
                foreach (var lv in live) if (lv.lift == l) have = true;
                if (have) continue;
                var root = new GameObject("Lift " + l.k);
                root.transform.SetParent(transform, false);
                live.Add(new Live { lift = l, root = root, span = new GameObject[l.Spans], tower = new GameObject[l.s.Length] });
            }

            foreach (var part in parts) if (part != chair && part != chairLod && System.Array.IndexOf(riders, part) < 0 && System.Array.IndexOf(ridersLod, part) < 0) part.list.Clear();
            foreach (var lv in live)
            {
                var l = lv.lift;
                Vector3 axis = l.Axis, fwd = l.dir * Mathf.Cos(l.lean) - Vector3.up * Mathf.Sin(l.lean);
                // the cable, span by span
                for (int i = 0; i < l.Spans; i++)
                {
                    float za = l.top.z + l.dir.z * l.nodeS[i], zb = l.top.z + l.dir.z * l.nodeS[i + 1];
                    bool want = InRange(Mathf.Min(za, zb), Mathf.Max(za, zb), p.z);
                    if (want && lv.span[i] == null) lv.span[i] = Cable(lv, i);
                    else if (!want && lv.span[i] != null) { Destroy(lv.span[i].GetComponent<MeshFilter>().sharedMesh); Destroy(lv.span[i]); lv.span[i] = null; }
                }
                // the towers: drawn, and solid when near
                for (int i = 0; i < l.s.Length; i++)
                {
                    Vector3 f = l.foot[i], h = l.head[i];
                    bool drawn = InRange(f.z, f.z, p.z);
                    float d2 = (f - p).sqrMagnitude;
                    bool solid = drawn && d2 < ColliderRange * ColliderRange;
                    if (solid && lv.tower[i] == null) lv.tower[i] = TowerCollider(lv, i, axis, fwd);
                    else if (!solid && lv.tower[i] != null) { Destroy(lv.tower[i]); lv.tower[i] = null; }
                    // (an invincible rider switches off what it rides through: back on once it has gone by)
                    if (lv.tower[i] != null && d2 > 8f * 8f) foreach (var c in lv.tower[i].GetComponents<Collider>()) if (!c.enabled) c.enabled = true;
                    if (!drawn) continue;
                    bool near = d2 < NearRange * NearRange;
                    var mastM = Frame(f - axis * 1.5f, l.across, axis * (l.h[i] + 1.5f), fwd);
                    (near ? mast : mastLod).list.Add(mastM * (near ? mast : mastLod).local);
                    (near ? head : headLod).list.Add(Frame(h, l.across, axis, fwd) * (near ? head : headLod).local);
                    if (d2 < 320f * 320f) pad.list.Add(Frame(f, l.across, axis, fwd) * pad.local);
                    // the sheave trains: along the cable as it crosses the tower, under it; or over it, where the
                    // cable would otherwise lift off (a hollow in the line)
                    int j = i + 1;
                    float lenIn = l.nodeS[j] - l.nodeS[j - 1], lenOut = l.nodeS[j + 1] - l.nodeS[j];
                    float slopeIn = (l.nodeY[j] - l.nodeY[j - 1] + 4f * LiftLine.Sag(lenIn)) / lenIn;
                    float slopeOut = (l.nodeY[j + 1] - l.nodeY[j] - 4f * LiftLine.Sag(lenOut)) / lenOut;
                    Vector3 t = (l.dir + Vector3.up * (0.5f * (slopeIn + slopeOut))).normalized;
                    Vector3 n = Vector3.Cross(t, l.across).normalized;   // square to the cable, upward
                    float k = slopeOut > slopeIn + 0.01f ? -1f : 1f;      // a hold-down train is the same one turned over
                    var train = near ? sheaves : sheavesLod;
                    for (int side = -1; side <= 1; side += 2)
                        train.list.Add(Frame(h + l.across * (side * LiftLine.Half), l.across * side, n * k, t * (side * k)) * train.local);
                }
                // the stations
                bool topIn = InRange(l.top.z - 8f, l.top.z + 8f, p.z), bottomIn = InRange(l.bottom.z - 8f, l.bottom.z + 8f, p.z);
                if (topIn) station.list.Add(Frame(l.top, l.across, Vector3.up, l.dir) * station.local);
                if (bottomIn) station.list.Add(Frame(l.bottom, -l.across, Vector3.up, -l.dir) * station.local);
                Stream(ref lv.stationTop, topIn && (l.top - p).sqrMagnitude < 240f * 240f, lv, l.top, l.dir);
                Stream(ref lv.stationBottom, bottomIn && (l.bottom - p).sqrMagnitude < 240f * 240f, lv, l.bottom, -l.dir);
            }
        }

        /// <summary>A matrix from a place and three axes (the second may carry a stretch).</summary>
        static Matrix4x4 Frame(Vector3 at, Vector3 x, Vector3 y, Vector3 z)
        {
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            m.SetColumn(3, new Vector4(at.x, at.y, at.z, 1f));
            return m;
        }

        // ---------------------------------------------------------------- the cable

        /// <summary>
        /// One span's cable: both ropes from one support to the next, hanging in their sag. The first and last
        /// spans carry on level into their station, to the bullwheel.
        /// </summary>
        GameObject Cable(Live lv, int i)
        {
            var l = lv.lift;
            const float R = 0.035f;
            float sa = l.nodeS[i], sb = l.nodeS[i + 1];
            int m = Mathf.Max(4, Mathf.CeilToInt((sb - sa) / 8f));
            var pts = new List<Vector3>();
            var v = new List<Vector3>(); var tri = new List<int>();
            Vector3 origin = l.At(sa, 0f, l.nodeY[i]);
            for (int side = -1; side <= 1; side += 2)
            {
                pts.Clear();
                if (i == 0) pts.Add(l.At(-LiftLine.WheelBack, side * LiftLine.Half, l.nodeY[0]));
                for (int q = 0; q <= m; q++)
                {
                    float s = Mathf.Lerp(sa, sb, q / (float)m);
                    pts.Add(l.At(s, side * LiftLine.Half, l.CableY(i, s)));
                }
                if (i == l.Spans - 1) pts.Add(l.At(l.plan + LiftLine.WheelBack, side * LiftLine.Half, l.nodeY[l.Spans]));
                int b = v.Count;
                for (int q = 0; q < pts.Count; q++)
                {
                    Vector3 t = (pts[Mathf.Min(q + 1, pts.Count - 1)] - pts[Mathf.Max(q - 1, 0)]).normalized;
                    Vector3 up = Vector3.Cross(t, l.across).normalized;
                    Vector3 c = pts[q] - origin;
                    v.Add(c + up * R); v.Add(c + l.across * R); v.Add(c - up * R); v.Add(c - l.across * R);
                }
                for (int q = 0; q < pts.Count - 1; q++)
                    for (int e = 0; e < 4; e++)
                    {
                        int a0 = b + q * 4 + e, a1 = b + q * 4 + (e + 1) % 4, b0 = a0 + 4, b1 = a1 + 4;
                        tri.Add(a0); tri.Add(b0); tri.Add(a1);
                        tri.Add(a1); tri.Add(b0); tri.Add(b1);
                    }
            }
            var mesh = new Mesh { name = "LiftCable" };
            mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Cable " + i);
            go.transform.SetParent(lv.root.transform, false);
            go.transform.position = origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = cableMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return go;
        }

        // ---------------------------------------------------------------- solid things

        GameObject TowerCollider(Live lv, int i, Vector3 axis, Vector3 fwd)
        {
            var l = lv.lift;
            var go = new GameObject("LiftTower");
            go.transform.SetParent(lv.root.transform, false);
            go.transform.SetPositionAndRotation(l.foot[i], Quaternion.LookRotation(fwd, axis));
            // the pad round its foot, and the mast above it
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1; cap.radius = 0.82f; cap.height = 3.4f; cap.center = new Vector3(0f, 0.6f, 0f);
            var up = go.AddComponent<CapsuleCollider>();
            up.direction = 1; up.radius = 0.42f; up.height = l.h[i] + 1f; up.center = new Vector3(0f, l.h[i] * 0.5f, 0f);
            go.AddComponent<Obstacle>().label = "Lift tower";
            return go;
        }

        void Stream(ref GameObject go, bool want, Live lv, Vector3 deck, Vector3 outward)
        {
            if (want && go == null)
            {
                go = new GameObject("LiftStation");
                go.transform.SetParent(lv.root.transform, false);
                go.transform.SetPositionAndRotation(deck, Quaternion.LookRotation(outward, Vector3.up));
                // the deck and all that stands under it, down into the snow; the terminal overhead on its column; the hut
                const float legs = 14f;
                var b = go.AddComponent<BoxCollider>();
                b.center = new Vector3(0f, -legs * 0.5f, 0f); b.size = new Vector3(2f * LiftLine.StationHalfWid, legs, 2f * LiftLine.StationHalfLen);
                b = go.AddComponent<BoxCollider>();
                b.center = new Vector3(0f, LiftLine.StationCable + 1.6f, -0.15f); b.size = new Vector3(LiftLine.Gauge + 2.3f, 3.1f, 11.6f);
                b = go.AddComponent<BoxCollider>();
                b.center = new Vector3(0f, 2.5f, -0.2f); b.size = new Vector3(0.9f, 5f, 1.1f);
                b = go.AddComponent<BoxCollider>();
                b.center = new Vector3(LiftLine.StationHalfWid - 0.85f, 1.3f, 3.2f); b.size = new Vector3(1.6f, 2.6f, 2.5f);
                go.AddComponent<Obstacle>().label = "Lift station";
            }
            else if (!want && go != null) { Destroy(go); go = null; }
            else if (go != null) foreach (var c in go.GetComponents<Collider>()) if (!c.enabled && (deck - target.position).sqrMagnitude > 20f * 20f) c.enabled = true;
        }

        // ---------------------------------------------------------------- the chairs

        /// <summary>
        /// Every chair of every lift in range, from the clock alone: where it is on its loop, which way it hangs,
        /// who is on it. And the one way a chair can touch the rider: by being jumped into.
        /// </summary>
        void Chairs(Vector3 p)
        {
            chair.list.Clear(); chairLod.list.Clear();
            for (int i = 0; i < 4; i++) { riders[i].list.Clear(); ridersLod[i].list.Clear(); }
            double clock = Time.timeAsDouble;
            float t = (float)(clock % 3600.0);
            bool canHit = rider != null && rider.Running && !rider.Crashed;
            foreach (var lv in live)
            {
                var l = lv.lift;
                double moved = clock * LiftLine.Speed;
                for (int c = 0; c < l.chairs; c++)
                {
                    double along = c * (double)l.spacing + moved;
                    int lap = (int)System.Math.Floor(along / l.loop);
                    l.Loop((float)(along - lap * (double)l.loop), out Vector3 pos, out Vector3 travel, out bool up, out float since);
                    if (pos.z < p.z - behind || pos.z > p.z + FarRange) continue;
                    Vector3 flat = new Vector3(travel.x, 0f, travel.z);
                    if (flat.sqrMagnitude < 1e-6f) continue;
                    // it hangs straight down from its grip, swinging a little: its own slow sway, and a kick fore and aft
                    // coming off a tower's sheaves that dies away over the next twenty metres
                    float kick = since < 60f ? 3.2f * Mathf.Exp(-since / 9f) * Mathf.Sin(since * 0.42f) : 0f;
                    float pitch = 1.6f * Mathf.Sin(t * 1.3f + c * 1.7f) + kick;
                    float roll = 1.1f * Mathf.Sin(t * 1.05f + c * 2.9f);
                    if (since < 4f) pos.y += 0.035f * Mathf.Exp(-since / 1.2f) * Mathf.Sin(since * 5.2f);   // ticking over the wheels
                    var rot = Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(pitch, 0f, roll);
                    var m = Matrix4x4.TRS(pos, rot, Vector3.one);
                    float d2 = (pos - p).sqrMagnitude;
                    bool near = d2 < ChairNear * ChairNear;
                    (near ? chair : chairLod).list.Add(m * (near ? chair : chairLod).local);
                    // who is aboard: drawn when the chair left the bottom station, and gone at the top
                    float h = Slope.Hash01(c + l.k * 977, lap, 151);
                    int who = up ? (h < 0.56f ? Mathf.Min(3, (int)(h / 0.56f * 4f)) : -1) : (h > 0.95f ? 2 : -1);
                    if (who >= 0) { var r = near ? riders[who] : ridersLod[who]; r.list.Add(m * r.local); }
                    // jumped into: the seat, its frame and whoever is on it, as a box under the grip
                    if (canHit && d2 < 8f * 8f)
                    {
                        Vector3 q = Quaternion.Inverse(rot) * (p + Vector3.up * 0.75f - pos);
                        if (Mathf.Abs(q.x) < 1.4f && q.y > -4.5f && q.y < -0.9f && Mathf.Abs(q.z - 0.2f) < 1.1f)
                        {
                            canHit = false;
                            rider.Breakthrough(1f, 0f, 99f, 0f, "Chairlift!");   // a crash, unless a shield takes it; invincible, straight through
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- drawing

        void Draw()
        {
            foreach (var part in parts)
            {
                if (part.mesh == null || part.list.Count == 0) continue;
                var rp = new RenderParams(paint) { shadowCastingMode = part.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                for (int i = 0; i < part.list.Count; i += 1000)
                    Graphics.RenderMeshInstanced(rp, part.mesh, 0, part.list, Mathf.Min(1000, part.list.Count - i), i);
            }
        }

        /// <summary>What the lift asks the GPU for this frame: draw calls (instanced batches and cable spans) and triangles.</summary>
        public void Stats(out int draws, out long tris, out int instances)
        {
            draws = 0; tris = 0; instances = 0;
            foreach (var part in parts)
            {
                if (part.mesh == null || part.list.Count == 0) continue;
                draws += (part.list.Count + 999) / 1000; tris += (long)part.Tris * part.list.Count; instances += part.list.Count;
            }
            foreach (var lv in live)
                foreach (var g in lv.span)
                    if (g != null) { draws++; tris += g.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3; }
        }

        // ---------------------------------------------------------------- dusk and night

        DayNight dayNight;
        Light lamp;
        float glowShown = -1f, lampK, findClock;
        Vector3 lampAt;
        bool lampSet;

        void Glow(Vector3 p)
        {
            if (dayNight == null || sound == null)
            {
                // (both are made by the game after this is; looked for once a second until they are there)
                findClock -= Time.unscaledDeltaTime;
                if (findClock <= 0f)
                {
                    findClock = 1f;
                    if (dayNight == null) dayNight = Object.FindFirstObjectByType<DayNight>();
                    if (sound == null) sound = Object.FindFirstObjectByType<SoundMix>();
                }
            }
            float dark = dayNight != null ? dayNight.Darkness : 0f;
            float glow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.27f, 0.62f, dark));
            if (Mathf.Abs(glow - glowShown) > 0.01f)
            {
                glowShown = glow;
                paint.SetColor("_EmissionColor", Color.white * (2.6f * glow));
            }
            // one real light, at the nearest lamp ahead: a station's floodlights, or the work lamps under a crossarm
            bool any = false; Vector3 best = Vector3.zero; float bestD = 220f * 220f, power = 0f;
            if (glow > 0.02f)
                foreach (var lv in live)
                {
                    var l = lv.lift;
                    for (int i = -2; i < l.head.Length; i++)
                    {
                        Vector3 at = i == -2 ? l.top + Vector3.up * 4.4f + l.dir * 4.5f : i == -1 ? l.bottom + Vector3.up * 4.4f - l.dir * 4.5f : l.head[i] + l.Axis * 0.3f;
                        float d = (at - p).sqrMagnitude;
                        if (at.z < p.z - 25f || d >= bestD) continue;
                        bestD = d; best = at; power = i < 0 ? 26f : 70f; any = true;
                    }
                }
            // (it fades out, moves, and fades in again, rather than jumping from one lamp to the next)
            bool move = any && (!lampSet || (best - lampAt).sqrMagnitude > 1f);
            lampK = Mathf.MoveTowards(lampK, any && !move ? 1f : 0f, Time.deltaTime * 3f);
            if (move && lampK <= 0.001f) { lampAt = best; lampSet = true; lampPower = power; lamp.transform.position = best; }
            lamp.intensity = lampPower * glow * lampK;
            lamp.enabled = lamp.intensity > 0.05f;
        }
        float lampPower;

        // ---------------------------------------------------------------- sound

        SoundMix sound;
        int clackTower = -1, clackUp, clackDown;
        LiftLine.Lift clackLift;

        /// <summary>A hum from the nearest point of the nearest line, and a clack each time a chair crosses the nearest tower.</summary>
        void Sound(Vector3 p)
        {
            if (sound == null) return;
            LiftLine.Lift nearest = null; float bestD = 130f * 130f; Vector3 at = p;
            foreach (var lv in live)
            {
                var l = lv.lift;
                float along = Mathf.Clamp((p.x - l.top.x) * l.dir.x + (p.z - l.top.z) * l.dir.z, 0f, l.plan);
                Vector3 q = l.At(along, 0f, Mathf.Lerp(l.top.y, l.bottom.y, along / l.plan) + 10f);
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; nearest = l; at = q; }
            }
            sound.LiftHum(at, nearest != null && Time.timeScale > 0f ? 1f : 0f);
            if (nearest == null) { clackTower = -1; return; }
            int tower = -1; float td = 110f * 110f;
            for (int i = 0; i < nearest.head.Length; i++)
            {
                float d = (nearest.head[i] - p).sqrMagnitude;
                if (d < td) { td = d; tower = i; }
            }
            if (tower < 0) { clackTower = -1; return; }
            // how many chairs have crossed this tower so far, each way: when the count goes up, one just has
            double moved = Time.timeAsDouble * LiftLine.Speed;
            int n = nearest.Spans;
            int up = (int)System.Math.Floor((moved - nearest.cum[n - tower]) / nearest.spacing);
            int down = (int)System.Math.Floor((moved - nearest.cum[n + 5 + tower]) / nearest.spacing);
            if (tower == clackTower && nearest == clackLift)
            {
                if (up != clackUp) sound.LiftClack(nearest.head[tower] + nearest.across * LiftLine.Half, 0.5f);
                if (down != clackDown) sound.LiftClack(nearest.head[tower] - nearest.across * LiftLine.Half, 0.4f);
            }
            clackTower = tower; clackLift = nearest; clackUp = up; clackDown = down;
        }
    }
}
