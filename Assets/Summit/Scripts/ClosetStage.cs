using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The closet's try-on studio: a soft spot on snow in front of a dark curved wall, high above the mountain where
    /// nothing else is, with the hero standing in it. While the closet is open the game's own camera is moved here and
    /// draws only the studio's layer (the camp isn't drawn at all, so the closet costs less than the camp does), and is
    /// put back as it was when it closes. Nothing is built or drawn while it's shut but the studio's few pieces, kept
    /// switched off.
    ///
    /// The hero here is a copy of the rider's own visuals (the clothes, gear and what is worn on them) made as the
    /// closet opens, with its scripts, colliders and lights stripped. It shares the rider's materials, so whatever the
    /// closet paints on the rider (a try-on, through RiderBody.Wear) shows on it at once. It stands in the camp's
    /// standing clip with a breath and a look round, and turns when dragged. The ride (board, skis or toboggan) stands
    /// beside it.
    /// </summary>
    public class ClosetStage : MonoBehaviour
    {
        /// <summary>The layer the studio is on (nothing else uses it).</summary>
        public const int Layer = 30;
        /// <summary>The stage that is showing, or null (the camp's camera hands over to it).</summary>
        public static ClosetStage Showing { get; private set; }

        static readonly Vector3 At = new Vector3(0f, 3000f, 0f);   // far above everything, out of every other light's reach
        const float Fov = 30f;

        RiderBody body;
        Camera cam;
        Transform stage, spin, holder, copy;
        readonly Transform[] gear = new Transform[3];   // copies of the board, the skis, the toboggan (as RideKind)
        RideKind showRide;
        HeroRig rig;
        int idStand = -1;
        float yaw = -22f, yawVel, t;
        bool dragging;
        Transform chest, head;

        // the camera as it was, to put back
        int keptMask; float keptFov, keptNear; Vector3 keptPos; Quaternion keptRot; bool keptPost; bool keptSet;

        public void Setup(RiderBody b, Camera c) { body = b; cam = c; }

        public bool Open => Showing == this;

        /// <summary>Open: build the studio (once), copy the rider into it, and take the camera.</summary>
        public void Show(RideKind ride)
        {
            if (body == null || cam == null) return;
            if (stage == null) BuildStudio();
            stage.gameObject.SetActive(true);
            CopyRider();
            showRide = ride;
            ShowRide(ride);
            yaw = -22f; yawVel = 0f; t = 0f;
            if (!Open)
            {
                keptMask = cam.cullingMask; keptFov = cam.fieldOfView; keptNear = cam.nearClipPlane;
                keptPos = cam.transform.position; keptRot = cam.transform.rotation;
                var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                keptPost = data != null && data.renderPostProcessing;
                keptSet = true;
            }
            Showing = this;
            cam.cullingMask = 1 << Layer;
            Pose(0f);
            Frame(cam);
        }

        /// <summary>Close: let the camera go back to the camp as it was, and throw the copy away.</summary>
        public void Hide()
        {
            if (Showing == this) Showing = null;
            if (keptSet && cam != null)
            {
                cam.cullingMask = keptMask; cam.fieldOfView = keptFov; cam.nearClipPlane = keptNear;
                cam.transform.SetPositionAndRotation(keptPos, keptRot);
                keptSet = false;
            }
            if (copy != null) Kill(holder.gameObject);
            copy = null; holder = null; rig = null;
            for (int i = 0; i < gear.Length; i++) { if (gear[i] != null) Kill(gear[i].gameObject); gear[i] = null; }
            if (stage != null) stage.gameObject.SetActive(false);
        }

        /// <summary>Which ride stands beside the hero (the one tried on in the closet's RIDE list, or the one you have).</summary>
        /// (Shown only when it's what the closet is on: the board's colours or the ride; otherwise it'd crowd the hero.)
        public void ShowRide(RideKind k, bool shown = true)
        {
            showRide = k; rideShown = shown;
            for (int i = 0; i < gear.Length; i++) if (gear[i] != null) gear[i].gameObject.SetActive(shown && i == (int)k);
            // without the skis' or the toboggan's files there is only the board to show
            if (shown && (int)k < gear.Length && gear[(int)k] == null && gear[0] != null) gear[0].gameObject.SetActive(true);
        }

        bool rideShown = true;

        /// <summary>The GRAPHICS tab: the ride standing beside the hero turns slowly round, its top sheet then its base.</summary>
        public void SpinRide(bool on)
        {
            if (on && !spinRide) spinAngle = 0f;
            spinRide = on;
        }
        bool spinRide;
        float spinAngle;
        /// <summary>Test tools: how far round the ride has turned (0: its top sheet to the camera).</summary>
        public float RideTurn { get => spinAngle; set => spinAngle = value; }
        /// <summary>The rider was rebuilt (another rider or set of clothes tried on): copy them afresh, as they stand now.</summary>
        public void Recopy()
        {
            if (!Open) return;
            CopyRider();
            ShowRide(showRide, rideShown);
            Pose(0f);
        }

        /// <summary>Turn the hero by a drag (degrees); let go and it carries on a little.</summary>
        public void Turn(float deg, float dt)
        {
            yaw += deg;
            if (dt > 0f) yawVel = Mathf.Lerp(yawVel, deg / dt, 0.5f);
            dragging = true;
        }
        public void Release() { dragging = false; }
        public float Yaw { get => yaw; set { yaw = value; yawVel = 0f; facing = float.NaN; } }

        /// <summary>Turn the hero round to this (the pack is on their back); a drag takes over from it.</summary>
        public void Face(float deg) { facing = deg; yawVel = 0f; }
        float facing = float.NaN;

        void LateUpdate()
        {
            if (!Open) return;
            float dt = ChaseCamera.TestDt > 0f ? ChaseCamera.TestDt : Time.unscaledDeltaTime;
            Pose(dt);
        }

        /// <summary>A frame of the hero standing in the studio (public for the test tools).</summary>
        public void Pose(float dt)
        {
            if (spin == null) return;
            t += dt;
            if (dragging) facing = float.NaN;
            else if (!float.IsNaN(facing)) yaw = Mathf.LerpAngle(yaw, facing, 1f - Mathf.Exp(-dt * 5f));
            else { yaw += yawVel * dt; yawVel *= Mathf.Exp(-dt * 3.5f); }
            spin.localRotation = Quaternion.Euler(0f, yaw, 0f);   // 0: facing the camera (it stands out along +z, looking back)
            if (rig != null && idStand >= 0)
            {
                rig.Begin();
                rig.Want(idStand, 1f);
                if (dt <= 0f) rig.Snap();
                rig.Evaluate(Mathf.Max(dt, 0f));
            }
            // a breath, a little sway, and a look round: on top of the clip (or of the previous rider's still pose)
            if (chest != null) chest.localRotation *= Quaternion.Euler(Mathf.Sin(t * 1.7f) * 1.3f, 0f, 0f);
            if (head != null)
            {
                float look = Mathf.Sin(t * 0.43f) * 14f + Mathf.Sin(t * 1.13f) * 3f;
                head.rotation = Quaternion.AngleAxis(look, Vector3.up) * Quaternion.AngleAxis(Mathf.Sin(t * 0.31f) * 4f, spin.right) * head.rotation;
            }
            if (holder != null) holder.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.8f) * 0.8f);   // the weight shifting foot to foot
            spinAngle = spinRide ? Mathf.Repeat(spinAngle + dt * 50f, 360f) : Mathf.LerpAngle(spinAngle, 0f, 1f - Mathf.Exp(-dt * 6f));
            for (int i = 0; i < gear.Length; i++) if (gear[i] != null) gear[i].localRotation = Quaternion.Euler(0f, spinAngle, 0f);
        }

        /// <summary>
        /// The camera, every frame the closet is open: level with the hips, far enough back that the whole hero fits the
        /// upper two thirds of a portrait screen, and off to one side, so the hero stands right of the closet's list.
        /// (Looking back along -z, the screen's right is the world's -x: the camera stands at +x of the hero.)
        /// </summary>
        public void Frame(Camera c)
        {
            if (c == null) return;
            float aspect = c.aspect > 0f ? c.aspect : 0.46f;
            float h = 1.85f / 0.52f;                                    // the hero fills about half the screen's height
            float d = h * 0.5f / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float w = h * aspect;
            float x = (0.775f - 0.5f) * w;                              // the hero's middle at 77% across, clear of the list
            float y = (0.5f - 0.19f) * h;                               // the feet 19% up from the bottom
            var pos = At + new Vector3(x, y, d);
            c.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.back, Vector3.up));
            c.fieldOfView = Fov;
            c.nearClipPlane = 0.3f;
        }

        // ---------------------------------------------------------------- the copy of the rider

        void CopyRider()
        {
            if (holder != null) Kill(holder.gameObject);
            for (int i = 0; i < gear.Length; i++) { if (gear[i] != null) Kill(gear[i].gameObject); gear[i] = null; }
            rig = null; chest = head = null;
            var upper = body.Upper;
            if (upper == null) return;
            // where the rider's feet are and which way they face (the toe edge, +x, is the way they walk)
            Vector3 facing = upper.right; facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            facing.Normalize();
            copy = Instantiate(upper.gameObject, upper.position, upper.rotation).transform;
            copy.name = "ClosetHero";
            copy.localScale = upper.lossyScale;
            Strip(copy.gameObject);
            Vector3 feet;
            var heroT = Find(copy, "Hero");
            var root = heroT != null ? Find(heroT, "Root") : null;
            if (heroT != null && root != null)
            {
                var table = Resources.Load<TextAsset>("Rider/HeroAnim");
                var r = table != null ? new HeroRig(heroT, table) : null;
                if (r != null && r.Ok)
                {
                    rig = r; idStand = r.Id("Stand");
                    rig.Begin(); rig.Want(idStand, 1f); rig.Snap(); rig.Evaluate(0f);
                    chest = r.UpperChest != null ? r.UpperChest : r.Chest; head = r.Head;
                }
                feet = root.position;   // the clips stand the Root bone on the ground
            }
            else
            {
                // the previous rider: no clips to stand it in, so it stays as it was in the camp; its feet are its lowest point
                var b = Bounds(copy);
                feet = new Vector3(b.center.x, b.min.y, b.center.z);
            }
            holder = new GameObject("Holder").transform;
            holder.SetPositionAndRotation(feet, Quaternion.LookRotation(facing, Vector3.up));
            copy.SetParent(holder, true);
            holder.SetParent(spin, false);
            holder.localPosition = Vector3.zero; holder.localRotation = Quaternion.identity;
            SetLayer(holder);

            // the rides, stood on end beside the hero (on their tails, the deck to the camera); the toboggan on its back end
            Transform[] src = { body.BoardGear, body.SkiGear, body.SledGear };
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == null) continue;
                var g = Instantiate(src[i].gameObject, src[i].position, src[i].rotation).transform;
                g.name = "Closet" + src[i].name;
                g.gameObject.SetActive(true);
                Strip(g.gameObject);
                // each one's length runs along its own z, its top along its y (as under the rider's roll)
                var upright = Quaternion.AngleAxis(i == 2 ? 4f : 7f, Vector3.forward) * Quaternion.LookRotation(Vector3.up, Quaternion.Euler(0f, i == 2 ? -30f : -15f, 0f) * Vector3.forward);
                g.rotation = upright;
                var b = Bounds(g);
                // (the skis run along their own x, the way the rider faces: turn that up instead)
                if (b.size.y < Mathf.Max(b.size.x, b.size.z)) { g.rotation = upright * Quaternion.Euler(0f, -90f, 0f); b = Bounds(g); }
                g.SetParent(stage, true);
                b = Bounds(g);
                // stood in front of the hero, a little to the list's side, as if being shown to you
                Vector3 spot = At + new Vector3(i == 2 ? 0.22f : 0.2f, 0f, i == 2 ? 0.55f : 0.45f);
                g.position += new Vector3(spot.x - b.center.x, At.y - b.min.y, spot.z - b.center.z);
                // on a turntable of its own (the GRAPHICS tab turns it round about its middle: SpinRide)
                b = Bounds(g);
                var pv = new GameObject("Turn" + src[i].name).transform;
                pv.SetPositionAndRotation(new Vector3(b.center.x, At.y, b.center.z), Quaternion.identity);
                pv.SetParent(stage, true);
                g.SetParent(pv, true);
                SetLayer(pv);
                gear[i] = pv;
            }
        }

        static void Kill(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        static Transform Find(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Only what is drawn stays: no scripts, colliders, joints, lights, sounds or particles on the copy.</summary>
        static void Strip(GameObject go)
        {
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            for (int pass = 0; pass < 3; pass++)
            {
                bool left = false;
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                    if (c is Behaviour bh) bh.enabled = false;
                    DestroyImmediate(c);
                    if (c != null) left = true;   // (one another needs: it goes on the next pass)
                }
                if (!left) break;
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
        }

        static void SetLayer(Transform t) { foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = Layer; }

        // ---------------------------------------------------------------- the studio

        void BuildStudio()
        {
            stage = new GameObject("ClosetStudio").transform;
            stage.SetParent(transform, false);
            stage.position = At;
            spin = new GameObject("Turn").transform;
            spin.SetParent(stage, false);
            var sh = Resources.Load<Shader>("StudioBackdrop");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            // the floor: a soft bright spot round the feet, a darker touch right under them, fading out to the wall's foot
            var floor = Piece("Floor", FloorMesh(9f), Paint(sh, FloorTexture(128)));
            floor.SetParent(stage, false);
            // the wall: up from where the floor fades, curving round so there's no corner, darkening as it rises
            var wall = Piece("Wall", WallMesh(), Paint(sh, WallTexture(64)));
            wall.SetParent(stage, false);
            // lights for the hero only: a key from high front left, a cool fill from the right, a warm rim from behind
            // (screen left is +x here)
            AddLight("Key", LightType.Spot, new Vector3(2.2f, 4.2f, 3.6f), new Vector3(0f, 1.0f, 0f), new Color(1f, 0.96f, 0.9f), 26f, 12f, 48f);
            AddLight("Fill", LightType.Point, new Vector3(-2.6f, 1.6f, 2.8f), Vector3.zero, new Color(0.7f, 0.8f, 1f), 5f, 9f, 0f);
            AddLight("Rim", LightType.Point, new Vector3(-0.6f, 2.8f, -1.8f), Vector3.zero, new Color(1f, 0.82f, 0.62f), 9f, 6f, 0f);
            stage.gameObject.SetActive(false);
            SetLayer(stage);
        }

        void AddLight(string name, LightType type, Vector3 at, Vector3 aim, Color col, float intensity, float range, float angle)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(stage, false);
            l.transform.localPosition = at;
            if (type == LightType.Spot) { l.transform.LookAt(stage.TransformPoint(aim)); l.spotAngle = angle; l.innerSpotAngle = angle * 0.4f; }
            l.type = type; l.color = col; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;   // (the phone's settings have no shadows but the sun's)
            l.cullingMask = 1 << Layer;
            l.renderMode = LightRenderMode.ForcePixel;
        }

        static Transform Piece(string name, Mesh m, Material mat)
        {
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = m;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g.transform;
        }

        static Material Paint(Shader sh, Texture2D tex)
        {
            var m = new Material(sh);
            m.SetTexture("_MainTex", tex); m.SetTexture("_BaseMap", tex);
            m.SetColor("_Tint", Color.white); m.SetColor("_BaseColor", Color.white);
            return m;
        }

        static Mesh FloorMesh(float r)
        {
            // a disc in rings, uv the floor's own x,z (so the spot is round on it), centred on the hero
            const int seg = 48, rings = 6;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            v.Add(Vector3.zero); uv.Add(new Vector2(0.5f, 0.5f));
            for (int k = 1; k <= rings; k++)
            {
                float rr = r * Mathf.Pow(k / (float)rings, 1.5f);
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    v.Add(p); uv.Add(new Vector2(0.5f + p.x / (2f * r), 0.5f + p.z / (2f * r)));
                }
            }
            for (int i = 0; i < seg; i++) { tri.Add(0); tri.Add(1 + (i + 1) % seg); tri.Add(1 + i); }
            for (int k = 1; k < rings; k++)
                for (int i = 0; i < seg; i++)
                {
                    int a = 1 + (k - 1) * seg + i, b = 1 + (k - 1) * seg + (i + 1) % seg, c = 1 + k * seg + i, d = 1 + k * seg + (i + 1) % seg;
                    tri.Add(a); tri.Add(b); tri.Add(d); tri.Add(a); tri.Add(d); tri.Add(c);
                }
            var m = new Mesh { name = "ClosetFloor" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh WallMesh()
        {
            // a ring of wall round the back of the stage (behind the hero, from the camera's side): from the floor's edge
            // it curves up in a quarter circle and goes on up; uv.y climbs it, so its texture darkens with height
            const int across = 40, up = 14;
            const float r0 = 7.5f, bend = 2.5f, top = 16f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int j = 0; j <= up; j++)
            {
                float s = j / (float)up, rad, y;
                if (s < 0.5f) { float a = s * Mathf.PI; rad = r0 - bend + Mathf.Sin(a) * bend; y = bend - Mathf.Cos(a) * bend; }   // the cove
                else { rad = r0; y = bend + (s - 0.5f) * 2f * (top - bend); }
                for (int i = 0; i <= across; i++)
                {
                    float a = Mathf.Lerp(-170f, -10f, i / (float)across) * Mathf.Deg2Rad;   // the far half: z < 0
                    v.Add(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad));
                    uv.Add(new Vector2(i / (float)across, y / top));
                }
            }
            for (int j = 0; j < up; j++)
                for (int i = 0; i < across; i++)
                {
                    int a = j * (across + 1) + i, b = a + 1, c = a + across + 1, d = c + 1;
                    tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d);
                }
            var m = new Mesh { name = "ClosetWall" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static readonly Color Deep = new Color(0.07f, 0.09f, 0.15f), Haze = new Color(0.30f, 0.36f, 0.47f);

        static Texture2D FloorTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ClosetFloor" };
            var px = new Color[n * n];
            Color snow = new Color(0.93f, 0.95f, 0.98f), shade = new Color(0.62f, 0.67f, 0.76f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n - 0.5f, w = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(u * u + w * w) * 2f * 9f;   // metres from the hero
                    // the spot: bright out to a metre and a half, then down to the haze by the wall's foot (5 m)
                    Color c = Color.Lerp(snow, Haze, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.1f, 5f, r)));
                    // under the feet: a soft contact shadow (the studio's lights cast none)
                    c = Color.Lerp(shade, c, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.65f, r)));
                    px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(false, true);
            return t;
        }

        static Texture2D WallTexture(int n)
        {
            var t = new Texture2D(4, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ClosetWall" };
            var px = new Color[4 * n];
            for (int y = 0; y < n; y++)
            {
                float s = (y + 0.5f) / n;
                // from the floor's haze at its foot, a glow behind the hero's shoulders, up into the deep blue
                Color c = Color.Lerp(Haze, Deep, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.45f, s)));
                c += new Color(0.06f, 0.07f, 0.09f) * Mathf.Exp(-Mathf.Pow((s - 0.16f) / 0.08f, 2f));
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            t.SetPixels(px); t.Apply(false, true);
            return t;
        }
    }
}
