using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Slow zones, marked the way resorts mark them: a fluorescent SLOW banner strung between two bamboo stakes on each
    /// side of the piste, angled in like a funnel, so the middle of the run (14 m of it) stays open. They stand where a resort would
    /// put them: on the run-in below the start, above a hamlet, above a lift's bottom station, and before the finish.
    /// At most one per stretch of the course besides those, and not every stretch has one, so they stay occasional.
    /// Each banner is a Breakable: ridden into, it tears off its stakes and flaps away (Breakables decides what that
    /// costs you, which is next to nothing). Built with the rest of a course cycle's dressing (CourseDressing).
    /// </summary>
    public static class SlowZones
    {
        public const float W = 3.6f, H = 0.75f, Hem = 0.7f, StakeTall = 1.75f;   // the banner, how high it hangs, its stakes
        public const float Out = 9f, Angle = 20f;     // each banner's middle from the piste's centre line, and how far it turns in
        static Material print, bamboo, tape;
        static Shader madeFor;

        /// <summary>A zone: where down the run (z), and why it is there.</summary>
        public struct Zone { public float z; public string why; }

        /// <summary>Cycle k's slow zones (none to two), a pure function of the mountain (the tests ask for them too).</summary>
        public static List<Zone> Of(int k)
        {
            var list = new List<Zone>();
            if (k < 0) return list;
            float zb = Course.First + k * Course.Cycle;
            // below the start: where the run-in leaves the pad, before the course proper begins
            if (k == 0) Add(list, Course.First - 18f, "start", 0);
            // before the finish arch, where everyone slows to stop
            float fz = Slope.FinishZ - 60f;
            if (Course.CycleOf(fz) == k) Add(list, fz, "finish", 0);
            // one more in this stretch, if it has a place for one (and it is not one of the stretches left plain), well
            // clear of the start's and the finish's
            if (k > 0 && Slope.Hash01(k, 11, 733) < 0.8f && zb < Course.ClearFrom)
            {
                var want = new List<Zone>();
                var h = Hamlets.Of(k);
                if (h != null) want.Add(new Zone { z = h.fenceZ0 - 25f, why = "hamlet" });
                foreach (var l in LiftLine.Lifts)
                    if (l.k == k && Mathf.Abs(l.offBottom) < Slope.PisteHalf + 90f) want.Add(new Zone { z = l.bottom.z - 35f, why = "lift" });
                foreach (var w in want)
                {
                    if (w.z > Course.ClearFrom - 30f) continue;
                    bool clear = true;
                    foreach (var o in list) if (Mathf.Abs(o.z - w.z) < 150f) clear = false;
                    if (clear && Add(list, w.z, w.why, 3)) break;
                }
            }
            return list;
        }

        // the first of z, z - 15, z + 15 ... that has room for at least one banner; tries: how many steps either way
        static bool Add(List<Zone> list, float z, string why, int tries)
        {
            for (int i = 0; i <= tries * 2; i++)
            {
                float zz = z + (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 15f;
                if (Room(zz, -1f) || Room(zz, 1f)) { list.Add(new Zone { z = zz, why = why }); return true; }
            }
            return false;
        }

        // a banner's middle and its turn (degrees about up) on side s (-1 left, +1 right) of the zone at z: angled in at
        // the bottom, like a funnel, and turned with the run
        static void Place(float z, float s, out Vector3 at, out float yaw)
        {
            float x = Slope.Centre(z) + s * Out;
            at = new Vector3(x, 0f, z);
            float run = Mathf.Atan2(Slope.Centre(z + 5f) - Slope.Centre(z - 5f), 10f) * Mathf.Rad2Deg;
            yaw = run + s * Angle;
        }

        /// <summary>Is there room for side s's banner of the zone at z: nothing it would stand in or block.</summary>
        public static bool Room(float z, float s)
        {
            Place(z, s, out Vector3 at, out float yaw);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (Ridge.Has(z - 20f) || Ridge.Has(z + 20f)) return false;
            // not on top of a section sign (they stand 30 m before each section, at the side)
            float zb = Course.CycleBase(z);
            foreach (float sec in new[] { Course.ParkAt, Course.DangerAt, Course.PipeAt, Ridge.At })
                if (Mathf.Abs(z - (zb + sec - 30f)) < 20f) return false;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 p = at + rot * new Vector3(i * (W * 0.5f + 0.5f), 0f, 0f);
                if (Mathf.Abs(p.x - Slope.Centre(p.z)) > Slope.PisteHalf - 1f) return false;
                if (Course.Keep(p.x, p.z, 3f) || Slope.Kicker(p.x, p.z) > 0f || Hamlets.Taken(p.x, p.z) || LiftLine.Taken(p.x, p.z, 3f)) return false;
                if (Course.CrevasseNear(p.x, p.z, 8f)) return false;
            }
            return true;
        }

        /// <summary>Build cycle k's slow zones under root.</summary>
        public static void Build(Transform root, int k, Shader lit)
        {
            var zones = Of(k);
            if (zones.Count == 0) return;
            Materials(lit);
            // (the signs this stretch already has, like a cliff jump's: a banner does not go right beside one)
            var signs = root.GetComponentsInChildren<Breakable>();
            foreach (var zn in zones)
                for (int s = -1; s <= 1; s += 2)
                {
                    if (!Room(zn.z, s)) continue;
                    Place(zn.z, s, out Vector3 at, out float yaw);
                    bool crowded = false;
                    foreach (var b in signs)
                    {
                        Vector3 d = b.transform.position - at; d.y = 0f;
                        if (b.kind == Breakable.Kind.Sign && d.magnitude < 16f) crowded = true;
                    }
                    if (crowded) continue;
                    Banner(root, at, yaw, zn.why);
                }
        }

        static void Materials(Shader lit)
        {
            if (print != null && madeFor == lit) return;
            madeFor = lit;
            // the print, lit like the signs (it shines the headlamp back: a banner is seen at night)
            print = CourseDressing.Sign(lit, "Signs/BannerSlow");
            bamboo = new Material(lit);
            bamboo.SetColor("_BaseColor", new Color(0.78f, 0.66f, 0.40f)); bamboo.SetFloat("_Smoothness", 0.45f);
            tape = new Material(lit);
            tape.SetColor("_BaseColor", new Color(0.06f, 0.06f, 0.07f)); tape.SetFloat("_Smoothness", 0.3f);
        }

        static void Banner(Transform root, Vector3 at, float yaw, string why)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            // each stake pushed into the snow under its end; the banner hung between them, level with their snow
            var feet = new Vector3[2];
            float ground = 0f;
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = at + rot * new Vector3((i == 0 ? -1f : 1f) * (W * 0.5f + 0.06f), 0f, 0f);
                p.y = Slope.DrawnHeight(p.x, p.z);
                feet[i] = p; ground += p.y * 0.5f;
            }
            var h = new GameObject("SlowBanner " + why).transform;
            h.SetParent(root, false);
            h.SetPositionAndRotation(new Vector3(at.x, ground, at.z), rot);
            var stakes = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var st = new GameObject("Stake").transform;
                st.SetParent(h, false);
                st.SetPositionAndRotation(feet[i] + Vector3.down * 0.15f, rot);
                Part(PrimitiveType.Cylinder, st, new Vector3(0f, StakeTall * 0.5f, 0f), new Vector3(0.05f, StakeTall * 0.5f, 0.05f), bamboo, true);
                // black tape round it where the cloth is tied, and a cap
                foreach (float y in new[] { Hem + 0.15f, Hem + H + 0.15f - 0.08f })
                    Part(PrimitiveType.Cylinder, st, new Vector3(0f, y, 0f), new Vector3(0.058f, 0.035f, 0.058f), tape, false);
                Part(PrimitiveType.Cylinder, st, new Vector3(0f, StakeTall - 0.02f, 0f), new Vector3(0.06f, 0.025f, 0.06f), tape, false);
                stakes[i] = st;
            }
            // the cloth: its printed side looks up the hill (a quad shows its face to -Z), and the same print seen
            // through from below
            Vector3 mid = new Vector3(0f, Hem + H * 0.5f, 0f);
            var front = Part(PrimitiveType.Quad, h, mid, new Vector3(W, H, 1f), print, true);
            var back = Part(PrimitiveType.Quad, h, mid + new Vector3(0f, 0f, 0.004f), new Vector3(W, H, 1f), print, false);
            back.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Breakable.Banner(h, front, back, stakes, W, H, Hem, StakeTall);
        }

        static Transform Part(PrimitiveType t, Transform parent, Vector3 local, Vector3 scale, Material m, bool shadow)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
