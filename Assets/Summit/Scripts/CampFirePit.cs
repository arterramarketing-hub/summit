using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// The camp's fire and the corner round it (campfire2):
    ///   - the ring: fourteen of the mountain's photo-scanned boulders (Resources/ScanRocks) as stones a hand and a half
    ///     across, set touching round the fire and sunk into the snow, a little sooty, no snow on them (the fire's warmth);
    ///   - its bed: a dished disc of pale ash, charcoal heaped in the middle glowing between its pieces;
    ///   - the fire: four logs laid square with a teepee of six sticks inside, charred, their cracks glowing; flames from
    ///     a flipbook of tongues, a glow at their root, sparks going up in ones and in bursts, a thin smoke; the fire's
    ///     light and the embers' glow flicker together (CampFireFlicker), the smoke darkening with the evening;
    ///   - round it the three log benches, each a half log flat side up on two rounds, snow on their ends and a blanket
    ///     folded on the back one; in the back corner (away from the paths in) a woodpile between stakes with snow on
    ///     it, and a chopping block with an axe in it and chips round it.
    /// The wood is one material over one atlas (Camp/Fire/WoodAtlas: bark, a sawn end, a split face), the logs built
    /// here as meshes with each face mapped into its part (a log's end shows its rings, its side its bark, a split
    /// piece its split), merged into one mesh per frame; the burning wood the same over Camp/Fire/CharLog. The
    /// textures are made by Tools/MakeCampfireTex.py. Everything solid is a Block (walkers stop at it) and comes and
    /// goes with the fire (Progress.Up.FirePit), except the benches, which were always there.
    /// </summary>
    public partial class Camp
    {
        // made once and kept (never through Made: Unmake's note)
        Material fireWood, charWood, fireBedMat, fireStone, blanketMat;
        Color charGlow, bedGlow;
        static readonly Rect BarkUV = new Rect(0f, 0f, 0.5f, 1f), EndUV = new Rect(0.5f, 0.5f, 0.5f, 0.5f), SplitUV = new Rect(0.5f, 0f, 0.5f, 0.5f), AllUV = new Rect(0f, 0f, 1f, 1f);
        const float BarkTile = 0.6f;   // metres of bark the atlas's bark covers, round and along

        void FireMaterials()
        {
            if (fireWood != null) return;
            Material Make(string tex, Color c, float smooth)
            {
                var m = new Material(lit) { enableInstancing = true };
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
                var tx = Resources.Load<Texture2D>(tex);
                if (tx != null) m.SetTexture("_BaseMap", tx);
                return m;
            }
            void Glow(Material m, string tex, Color c)
            {
                // (URP Lit's _EMISSION: kept for the build by ShaderKeep's Lit__EMISSION)
                m.EnableKeyword("_EMISSION");
                var tx = Resources.Load<Texture2D>(tex);
                if (tx != null) m.SetTexture("_EmissionMap", tx);
                m.SetColor("_EmissionColor", c);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            fireWood = Make("Camp/Fire/WoodAtlas", Color.white, 0.1f);
            charWood = Make("Camp/Fire/CharLog", Color.white, 0.18f);
            Glow(charWood, "Camp/Fire/CharLogGlow", charGlow = new Color(0.95f, 0.85f, 0.75f));
            fireBedMat = Make("Camp/Fire/FireBed", Color.white, 0.04f);
            Glow(fireBedMat, "Camp/Fire/FireBedGlow", bedGlow = new Color(1.4f, 1.2f, 1.0f));
            blanketMat = Make("Camp/LodgeTex/Check_Albedo", new Color(0.95f, 0.9f, 0.88f), 0.04f);
            // the ring's stones: the slope's scanned-rock shader with no snow on them, a shade sootier
            var sh = Resources.Load<Shader>("ScanRockLit");
            var albedo = Resources.Load<Texture2D>("ScanRocks/ScanRocksAlbedo");
            var normal = Resources.Load<Texture2D>("ScanRocks/ScanRocksNormal");
            if (sh != null && sh.isSupported && albedo != null && normal != null)
            {
                fireStone = new Material(sh) { name = "FireStone", enableInstancing = true };
                fireStone.SetTexture("_BaseMap", albedo);
                fireStone.SetTexture("_BumpMap", normal);
                fireStone.SetColor("_BaseColor", new Color(0.9f, 0.88f, 0.86f));
                fireStone.SetVector("_Snow", new Vector4(5f, 6f, 0f, 0f));
                fireStone.SetVector("_Drift", new Vector4(-5f, -4f, 0f, 0f));
            }
            else fireStone = stone;
        }

        // ---------------------------------------------------------------- the fire

        Transform FirePit(Transform t)
        {
            t.name = "FirePit";
            FireMaterials();
            var pit = t.gameObject;
            FireRing(t);
            // the bed, its edge tucked under the stones
            var bed = FirePart(t, "FireBed", Made(FireBedMesh(0.68f, (x, z) => FireGround(t, new Vector3(x, 0f, z)))), fireBedMat);
            bed.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            // the fire: two logs laid across, two on them the other way, a teepee of sticks inside, all burning
            var w = new Wood();
            int seed = 40;
            // (on the snow where each end lies: the camp's ground isn't level)
            Vector3 On(Vector3 l) => l + Vector3.up * (FireGround(t, l) + 0.05f);
            w.Round(On(new Vector3(-0.44f, 0.075f, -0.27f)), On(new Vector3(0.43f, 0.06f, -0.25f)), 0.07f, AllUV, AllUV, 0.5f, true, true, ++seed);
            w.Round(On(new Vector3(-0.42f, 0.06f, 0.26f)), On(new Vector3(0.44f, 0.075f, 0.28f)), 0.065f, AllUV, AllUV, 0.5f, true, true, ++seed);
            w.Round(On(new Vector3(-0.27f, 0.2f, -0.45f)), On(new Vector3(-0.24f, 0.19f, 0.44f)), 0.062f, AllUV, AllUV, 0.5f, true, true, ++seed);
            w.Round(On(new Vector3(0.25f, 0.19f, -0.44f)), On(new Vector3(0.27f, 0.205f, 0.45f)), 0.068f, AllUV, AllUV, 0.5f, true, true, ++seed);
            // (thin, and crossing at different heights past the middle: no solid cone)
            for (int k = 0; k < 5; k++)
            {
                float a = (k * 72f + 17f + 14f * Mathf.Sin(k * 2.3f)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = new Vector3(-dir.z, 0f, dir.x);
                float r = 0.022f + 0.01f * Mathf.PerlinNoise(k * 1.7f, 0.4f);
                w.Round(On(dir * (0.22f + 0.04f * (k % 2)) + Vector3.up * 0.06f), On(-dir * (0.07f + 0.03f * (k % 3)) + side * 0.04f * (k % 2 == 0 ? 1f : -1f) + Vector3.up * (0.5f + 0.06f * (k % 3))),
                        r, AllUV, AllUV, 0.5f, false, true, ++seed, 8, 0.12f);
            }
            FirePart(t, "FireLogs", Made(w.Mesh("FireLogs")), charWood);
            // the flames, sparks and smoke; the light; their flicker
            var flick = pit.AddComponent<CampFireFlicker>();
            flick.camp = this;
            flick.lamp = Lamp(t, new Vector3(0f, 1.1f, 0f), 9f, 2.2f);
            flick.lamp.color = new Color(1f, 0.72f, 0.45f);
            flick.glows = new[] { charWood, fireBedMat };
            flick.glowBase = new[] { charGlow, bedGlow };
            FireFx(t, flick);
            Block(t, Vector3.zero, new Vector2(1.1f, 1.1f), pit);
            // the back corner, away from the paths in from the lodge (left) and the open front: the woodpile across
            // the corner between the right bench and the back one, the chopping block at its end
            Woodpile(FireSpot(t, new Vector3(2.75f, 0f, -2.75f), -45f, "Woodpile"), pit);
            ChoppingBlock(FireSpot(t, new Vector3(3.55f, 0f, -1.85f), 20f, "ChoppingBlock"), pit);
            return t;
        }

        /// <summary>A frame on the snow at a place in t's frame (turned by yaw degrees more).</summary>
        Transform FireSpot(Transform t, Vector3 local, float yaw, string name)
        {
            var f = new GameObject(name).transform;
            f.SetParent(t, false);
            var w = t.TransformPoint(local);
            w.y = Slope.DrawnHeight(w.x, w.z);
            f.SetPositionAndRotation(w, t.rotation * Quaternion.Euler(0f, yaw, 0f));
            return f;
        }

        /// <summary>How high the snow is at a point in f's frame (in that frame's height).</summary>
        static float FireGround(Transform f, Vector3 local)
        {
            var w = f.TransformPoint(local);
            return Slope.DrawnHeight(w.x, w.z) - f.position.y;
        }

        static GameObject FirePart(Transform parent, string name, Mesh mesh, Material m)
        {
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            g.transform.SetParent(parent, false);
            g.GetComponent<MeshFilter>().sharedMesh = mesh;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        /// <summary>A round of log standing from a to b (the fire's stumps): bark round it, its rings on top.</summary>
        void FireRound(Transform parent, Vector3 a, Vector3 b, float r, int seed)
        {
            FireMaterials();
            var w = new Wood();
            w.Round(a, b, r, BarkUV, EndUV, BarkTile, false, true, seed);
            FirePart(parent, "Round", Made(w.Mesh("Round")), fireWood);
        }

        // the scanned boulders, each as its mesh, where it sits in its model (the Blender axis fix kept) and its box there
        struct Rock { public Mesh mesh; public Matrix4x4 local; public Vector3 lo, hi; }
        static List<Rock> rocks;
        static List<Rock> Rocks()
        {
            if (rocks != null) return rocks;
            rocks = new List<Rock>();
            foreach (var prefab in Resources.LoadAll<GameObject>("ScanRocks"))
            {
                var rootInv = prefab.transform.worldToLocalMatrix;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var local = Matrix4x4.Rotate(prefab.transform.localRotation) * rootInv * mf.transform.localToWorldMatrix;
                    var b = mf.sharedMesh.bounds;
                    Vector3 lo = Vector3.one * 1e9f, hi = -lo;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = local.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c);
                    }
                    rocks.Add(new Rock { mesh = mf.sharedMesh, local = local, lo = lo, hi = hi });
                    break;   // (one mesh a rock)
                }
            }
            return rocks;
        }

        void FireRing(Transform t)
        {
            var ring = new GameObject("Ring").transform;
            ring.SetParent(t, false);
            var rk = Rocks();
            const int N = 14; const float R = 0.86f;
            var rnd = new System.Random(1407);
            float F() => (float)rnd.NextDouble();
            for (int i = 0; i < N; i++)
            {
                float a = (i + (F() - 0.5f) * 0.25f) / N * Mathf.PI * 2f;
                var at = new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R);
                if (rk.Count == 0)
                {
                    P(PrimitiveType.Sphere, ring, at + Vector3.up * 0.06f, new Vector3(0.36f, 0.22f, 0.3f), new Vector3(0f, i * 37f, 0f), stone);
                    continue;
                }
                var rock = rk[(i * 5 + 3) % rk.Count];
                var size = rock.hi - rock.lo;
                // its longer side round the ring, give or take
                bool xLong = size.x >= size.z;
                float yaw = -a * Mathf.Rad2Deg + (xLong ? 90f : 0f) + (F() - 0.5f) * 40f;
                float along = Mathf.Max(size.x, size.z), across = Mathf.Min(size.x, size.z);
                float s = (0.36f + 0.08f * F()) / along;
                s = Mathf.Clamp(s, 0.15f / size.y, 0.3f / size.y);          // a stone 15 to 30 cm high
                s = Mathf.Min(s, 0.42f / across);
                var stoneT = new GameObject("Stone").transform;
                stoneT.SetParent(ring, false);
                float ground = FireGround(t, at);
                // sunk a third into the snow, its middle over its place
                var mid = new Vector3((rock.lo.x + rock.hi.x) * 0.5f, rock.lo.y, (rock.lo.z + rock.hi.z) * 0.5f);
                var rot = Quaternion.Euler((F() - 0.5f) * 14f, yaw, (F() - 0.5f) * 14f);
                stoneT.localPosition = at + Vector3.up * (ground - size.y * s * 0.3f) - rot * (mid * s);
                stoneT.localRotation = rot;
                stoneT.localScale = Vector3.one * s;
                var g = new GameObject("Rock", typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(stoneT, false);
                g.transform.localPosition = rock.local.GetColumn(3);
                g.transform.localRotation = rock.local.rotation;
                g.transform.localScale = rock.local.lossyScale;
                g.GetComponent<MeshFilter>().sharedMesh = rock.mesh;
                g.GetComponent<MeshRenderer>().sharedMaterial = fireStone;
            }
        }

        /// <summary>The fire's bed: a disc of radius R on the snow (ground: its height at a point of the frame), raised a
        /// hand's breadth in the middle (the charcoal heaped there) and still a few centimetres at its edge, under the
        /// stones. (Lower than about 5 cm over Slope.DrawnHeight the camp's drawn snow showed through it in the shots,
        /// inside the ring, though the terrain's mesh there is at DrawnHeight: not understood; see campfire2's notes.)</summary>
        static Mesh FireBedMesh(float R, System.Func<float, float, float> ground)
        {
            const int rings = 8, segs = 36;
            var v = new List<Vector3> { new Vector3(0f, ground(0f, 0f) + 0.12f, 0f) };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tri = new List<int>();
            for (int i = 1; i <= rings; i++)
            {
                float rho = i / (float)rings;
                for (int j = 0; j < segs; j++)
                {
                    float a = j / (float)segs * Mathf.PI * 2f;
                    float x = Mathf.Cos(a) * rho * R, z = Mathf.Sin(a) * rho * R;
                    float y = ground(x, z) + 0.085f + 0.035f * Mathf.Exp(-(rho / 0.4f) * (rho / 0.4f)) - 0.055f * Mathf.SmoothStep(0.8f, 1f, rho)
                              + 0.008f * (Mathf.PerlinNoise(x * 6f + 3.1f, z * 6f + 7.3f) - 0.5f) * (1f - rho * 0.5f);
                    v.Add(new Vector3(x, y, z));
                    uv.Add(new Vector2(0.5f + x / (2f * R) * 0.98f, 0.5f + z / (2f * R) * 0.98f));
                }
            }
            for (int j = 0; j < segs; j++) { int a = 1 + j, b = 1 + (j + 1) % segs; tri.Add(0); tri.Add(b); tri.Add(a); }
            for (int i = 1; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = 1 + (i - 1) * segs + j, b = 1 + (i - 1) * segs + (j + 1) % segs, c = a + segs, d = b + segs;
                    tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(b); tri.Add(d); tri.Add(c);
                }
            var m = new Mesh { name = "FireBed" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            FaceUp(m);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Turn every triangle of a mesh lying roughly flat to face up (Unity's front faces wind clockwise).</summary>
        static void FaceUp(Mesh m)
        {
            var v = m.vertices; var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
                if (Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y < 0f) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
            m.triangles = t;
        }

        void FireFx(Transform t, CampFireFlicker flick)
        {
            if (soft == null) return;
            ParticleSystem Ps(string name, Vector3 at, Material m)
            {
                var go = new GameObject(name);
                go.transform.SetParent(t, false); go.transform.localPosition = at;
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
                var mn = ps.main; mn.simulationSpace = ParticleSystemSimulationSpace.World; mn.playOnAwake = true;
                return ps;
            }
            Gradient Grad(Color a, Color b, Color c, float a0, float a1, float a2, float a3)
            {
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 0.45f), new GradientColorKey(c, 1f) },
                          new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 0.15f), new GradientAlphaKey(a2, 0.6f), new GradientAlphaKey(a3, 1f) });
                return g;
            }
            // the tongues: a 2 x 2 flipbook, each flame its own, alpha blended (they show by day too)
            var flameMat = Made(new Material(soft));
            var tongues = Resources.Load<Texture2D>("Camp/Fire/Flame");
            if (tongues != null) flameMat.SetTexture("_BaseMap", tongues);
            flameMat.SetColor("_BaseColor", new Color(1.5f, 1.3f, 1.15f, 1f));
            var fl = Ps("Flames", new Vector3(0f, 0.12f, 0f), flameMat);
            var main = fl.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.55f, 1f), new Color(1f, 0.62f, 0.28f, 1f));
            main.maxParticles = 60;
            var sh = fl.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.19f;
            var em = fl.emission; em.rateOverTime = 30f;
            var col = fl.colorOverLifetime; col.enabled = true;
            col.color = Grad(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.62f, 0.22f), new Color(0.8f, 0.25f, 0.06f), 0f, 0.8f, 0.55f, 0f);
            var sz = fl.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.35f)));
            var nz = fl.noise; nz.enabled = true; nz.strength = 0.25f; nz.frequency = 1.5f; nz.scrollSpeed = 0.8f; nz.quality = ParticleSystemNoiseQuality.Low;
            var tsa = fl.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            // a glow at their root and the sparks: the soft dot, added on
            var glowMat = Made(new Material(soft));
            glowMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glowMat.SetFloat("_DstBlend", (float)BlendMode.One);
            glowMat.SetColor("_BaseColor", new Color(1.2f, 1f, 0.9f, 1f));
            var gl = Ps("FireGlow", new Vector3(0f, 0.18f, 0f), glowMat);
            main = gl.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 0.95f);
            main.startColor = new Color(1f, 0.55f, 0.15f, 0.35f);
            main.maxParticles = 20;
            sh = gl.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 5f; sh.radius = 0.15f;
            em = gl.emission; em.rateOverTime = 10f;
            col = gl.colorOverLifetime; col.enabled = true;
            col.color = Grad(Color.white, Color.white, Color.white, 0f, 1f, 0.7f, 0f);
            // the flames' hot core: the same tongues, smaller and yellower, added on (bright at night, pale by day)
            var coreMat = Made(new Material(glowMat));
            if (tongues != null) coreMat.SetTexture("_BaseMap", tongues);
            var co = Ps("FlameCore", new Vector3(0f, 0.1f, 0f), coreMat);
            main = co.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            main.startColor = new Color(1f, 0.78f, 0.4f, 0.75f);
            main.maxParticles = 30;
            sh = co.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 4f; sh.radius = 0.13f;
            em = co.emission; em.rateOverTime = 20f;
            col = co.colorOverLifetime; col.enabled = true;
            col.color = Grad(new Color(1f, 0.95f, 0.75f), new Color(1f, 0.7f, 0.3f), new Color(1f, 0.4f, 0.1f), 0f, 1f, 0.6f, 0f);
            nz = co.noise; nz.enabled = true; nz.strength = 0.15f; nz.frequency = 1.8f; nz.scrollSpeed = 0.8f; nz.quality = ParticleSystemNoiseQuality.Low;
            tsa = co.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            var sp = Ps("Sparks", new Vector3(0f, 0.3f, 0f), glowMat);
            main = sp.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
            main.startColor = new Color(1f, 0.8f, 0.45f, 1f);
            main.gravityModifier = -0.05f;
            main.maxParticles = 80;
            sh = sp.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 15f; sh.radius = 0.15f;
            em = sp.emission; em.rateOverTime = 5f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0.6f, 4, 10, 0, 1.7f) { probability = 0.7f } });
            col = sp.colorOverLifetime; col.enabled = true;
            col.color = Grad(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.45f, 0.12f), new Color(0.6f, 0.1f, 0.02f), 1f, 1f, 0.9f, 0f);
            nz = sp.noise; nz.enabled = true; nz.strength = 0.5f; nz.frequency = 1.2f; nz.scrollSpeed = 0.5f; nz.quality = ParticleSystemNoiseQuality.Low;
            var spr = sp.GetComponent<ParticleSystemRenderer>();
            spr.renderMode = ParticleSystemRenderMode.Stretch; spr.velocityScale = 0.04f; spr.lengthScale = 1.5f;
            // a thin smoke going up from over the flames
            Smoke(t, new Vector3(0f, 1.0f, 0f));
            var smoke = t.Find("Smoke");
            if (smoke != null)
            {
                var ps = smoke.GetComponent<ParticleSystem>();
                var sem = ps.emission; sem.rateOverTime = 4f;
                flick.smoke = smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            }
            foreach (var ps in new[] { fl, co, gl, sp }) ps.Play();
        }

        // ---------------------------------------------------------------- round it

        void Benches()
        {
            // log seats round the fire: left, right and behind; the front stays open to the view
            var t = Frame(0f, -71f, 0f);
            t.name = "Benches";
            FireMaterials();
            var w = new Wood();
            int seed = 10;
            foreach (var c in new[] { new Vector3(-2.9f, 0f, 0f), new Vector3(2.9f, 0f, 0f), new Vector3(0f, 0f, -2.9f) })
            {
                var along = c.z != 0f ? Vector3.right : Vector3.forward;
                float g0 = FireGround(t, c - along * 0.72f), g1 = FireGround(t, c + along * 0.72f);
                foreach (var (s, g) in new[] { (-0.72f, g0), (0.72f, g1) })
                {
                    var l = c + along * s;
                    w.Round(new Vector3(l.x, g - 0.1f, l.z), new Vector3(l.x, Mathf.Max(g0, g1) + 0.25f, l.z), 0.17f, BarkUV, EndUV, BarkTile, false, true, ++seed);
                }
                // the seat: a half log, flat side up, its top at knee height, on the rounds
                float top = Mathf.Max(g0, g1) + 0.45f;
                w.Slice(c - along * 1.1f + Vector3.up * top, c + along * 1.1f + Vector3.up * top, 0.2f, -Mathf.PI / 2f, Mathf.PI / 2f, Vector3.down, ++seed);
                Block(t, c, c.z != 0f ? new Vector2(1.2f, 0.3f) : new Vector2(0.3f, 1.2f));
                // snow lying on its ends, where nobody sits
                foreach (float e in new[] { -0.96f, 0.96f })
                    SnowLump(t, c + along * e + Vector3.up * (top + 0.005f), new Vector3(along.x != 0f ? 0.26f : 0.34f, 0.045f, along.z != 0f ? 0.26f : 0.34f));
                if (c.z != 0f)
                {
                    // a blanket folded on the back one, hanging down its front
                    var b = c + Vector3.left * 0.55f;
                    P(PrimitiveType.Cube, t, b + Vector3.up * (top + 0.03f), new Vector3(0.5f, 0.06f, 0.36f), new Vector3(0f, 4f, 0f), blanketMat);
                    P(PrimitiveType.Cube, t, b + new Vector3(0f, top - 0.12f, 0.205f), new Vector3(0.5f, 0.27f, 0.025f), new Vector3(-6f, 4f, 0f), blanketMat);
                }
            }
            FirePart(t, "BenchWood", Made(w.Mesh("BenchWood")), fireWood);
        }

        /// <summary>A pillow of snow lying on something (flat: nothing to walk into).</summary>
        void SnowLump(Transform t, Vector3 at, Vector3 size)
        {
            var s = P(PrimitiveType.Sphere, t, at, size, Vector3.zero, snowCap, false);
            s.name = "SnowLump";
        }

        /// <summary>Split firewood stacked between four stakes on two rails, along f's x, its ends to the fire.</summary>
        void Woodpile(Transform f, GameObject pit)
        {
            var w = new Wood();
            var rnd = new System.Random(77);
            float F() => (float)rnd.NextDouble();
            int seed = 100;
            float g = Mathf.Min(FireGround(f, new Vector3(-0.8f, 0f, 0f)), FireGround(f, new Vector3(0.8f, 0f, 0f)));
            foreach (float z in new[] { -0.13f, 0.13f })
                w.Round(new Vector3(-0.82f, g + 0.05f, z), new Vector3(0.82f, g + 0.06f, z), 0.055f, BarkUV, EndUV, BarkTile, true, true, ++seed);
            foreach (float x in new[] { -0.76f, 0.76f })
                foreach (float z in new[] { -0.2f, 0.2f })
                    w.Round(new Vector3(x, g - 0.2f, z), new Vector3(x + (F() - 0.5f) * 0.04f, g + 0.92f + F() * 0.08f, z), 0.035f, BarkUV, EndUV, BarkTile, false, true, ++seed, 8);
            float y = g + 0.19f, topY = y;
            for (int row = 0; row < 5; row++)
            {
                float half = row < 4 ? 0.66f : 0.45f;
                int n = row < 4 ? 9 : 6;
                for (int i = 0; i < n; i++)
                {
                    float x = -half + 2f * half * i / (n - 1) + (F() - 0.5f) * 0.03f;
                    float r = 0.07f + 0.025f * F(), len = 0.38f + 0.06f * F(), dz = (F() - 0.5f) * 0.05f;
                    float span = F() < 0.35f ? Mathf.PI : F() < 0.5f ? Mathf.PI * 2f / 3f : Mathf.PI / 2f;
                    float t0 = F() * Mathf.PI * 2f;
                    var c = new Vector3(x, y + (F() - 0.5f) * 0.02f, dz);
                    w.SliceCentred(c - Vector3.forward * len * 0.5f, c + Vector3.forward * len * 0.5f, r, t0, t0 + span, Vector3.down, ++seed);
                }
                topY = y;
                y += 0.145f;
            }
            FirePart(f, "WoodpileWood", Made(w.Mesh("WoodpileWood")), fireWood);
            // snow on top, and a cap on each stake
            SnowLump(f, new Vector3(0f, topY + 0.07f, 0f), new Vector3(1.25f, 0.17f, 0.44f));
            SnowLump(f, new Vector3(-0.35f, topY + 0.12f, 0.03f), new Vector3(0.5f, 0.12f, 0.34f));
            Block(f, Vector3.zero, new Vector2(0.86f, 0.3f), pit);
        }

        /// <summary>A chopping block, an axe left in it, split pieces against it and chips on the snow round it.</summary>
        void ChoppingBlock(Transform f, GameObject pit)
        {
            var w = new Wood();
            float g = FireGround(f, Vector3.zero);
            const float top = 0.44f;
            w.Round(new Vector3(0f, g - 0.12f, 0f), new Vector3(0f, g + top, 0f), 0.24f, BarkUV, EndUV, BarkTile, false, true, 300);
            // two split pieces leant against it, a few chips about
            w.SliceCentred(new Vector3(0.3f, g + 0.02f, -0.1f), new Vector3(0.25f, g + 0.36f, -0.12f), 0.075f, 0.3f, 2.2f, Vector3.right, 301);
            w.SliceCentred(new Vector3(0.05f, g + 0.02f, 0.31f), new Vector3(0.02f, g + 0.35f, 0.26f), 0.07f, 1.1f, 2.7f, Vector3.forward, 302);
            var rnd = new System.Random(5);
            for (int i = 0; i < 9; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = 0.32f + (float)rnd.NextDouble() * 0.4f;
                var c = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                c.y = FireGround(f, c) + 0.008f;
                var along = Quaternion.Euler(0f, (float)rnd.NextDouble() * 180f, 0f) * Vector3.forward * (0.03f + 0.03f * (float)rnd.NextDouble());
                w.SliceCentred(c - along, c + along, 0.025f, 0f, 1.6f, Vector3.down, 310 + i);
            }
            FirePart(f, "BlockWood", Made(w.Mesh("BlockWood")), fireWood);
            // the axe: its blade in the block's top, the handle up and out
            var axe = new GameObject("Axe").transform;
            axe.SetParent(f, false);
            axe.localPosition = new Vector3(0.04f, g + top + 0.02f, -0.02f);
            axe.localRotation = Quaternion.Euler(0f, 35f, -32f);
            P(PrimitiveType.Cube, axe, new Vector3(0f, 0.02f, 0f), new Vector3(0.16f, 0.1f, 0.03f), Vector3.zero, metal);
            P(PrimitiveType.Cube, axe, new Vector3(0f, 0.075f, 0f), new Vector3(0.05f, 0.04f, 0.045f), Vector3.zero, metal);
            Log(axe, new Vector3(0f, 0.06f, 0f), new Vector3(0f, 0.68f, 0f), 0.038f, wood);
            Block(f, Vector3.zero, new Vector2(0.32f, 0.32f), pit);
        }

        // ---------------------------------------------------------------- wood, as meshes

        /// <summary>Logs, posts and split pieces, their faces mapped into the wood atlas, gathered into one mesh.</summary>
        class Wood
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> tri = new List<int>();

            int V(Vector3 p, Vector3 nn, Vector2 t) { v.Add(p); n.Add(nn); uv.Add(t); return v.Count - 1; }
            /// (each triangle turned to face the way its corners' normals point: Unity's front faces wind clockwise)
            void Tri(int a, int b, int c)
            {
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), n[a] + n[b] + n[c]) < 0f) { int s = b; b = c; c = s; }
                tri.Add(a); tri.Add(b); tri.Add(c);
            }
            void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

            static void Frame(Vector3 ax, Vector3 down, out Vector3 p, out Vector3 q)
            {
                p = down - ax * Vector3.Dot(down, ax);
                if (p.sqrMagnitude < 1e-6f) p = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right);
                p.Normalize();
                q = Vector3.Cross(ax, p);
            }
            static Vector2 In(Rect r, float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);
            /// (a point on a sawn end: rings round the axis, the bark at the rim)
            static Vector2 EndAt(Rect r, float ang, float rel) => In(r, 0.5f + Mathf.Cos(ang) * 0.47f * rel, 0.5f + Mathf.Sin(ang) * 0.47f * rel);

            /// <summary>A round log from a to b, radius r (a little out of round), its side over `side` (along: a tile
            /// every `tile` metres), its ends (capped if asked) over `end`.</summary>
            public void Round(Vector3 a, Vector3 b, float r, Rect side, Rect end, float tile, bool capA, bool capB, int seed, int sides = 12, float wobble = 0.06f)
            {
                var ax = b - a; float len = ax.magnitude; ax /= len;
                Frame(ax, Vector3.down, out var p, out var q);
                int wraps = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * r / tile), 1, 4);
                int per = Mathf.CeilToInt(sides / (float)wraps); sides = per * wraps;
                int segs = Mathf.Clamp(Mathf.CeilToInt(len / 0.4f), 1, 8);
                float Rad(int j, int i) => r * (1f + wobble * 2f * (Mathf.PerlinNoise(seed * 3.7f + (j % sides) * 0.9f, i * 0.7f + seed * 0.31f) - 0.5f));
                for (int k = 0; k < wraps; k++)
                {
                    int b0 = v.Count;
                    for (int jj = 0; jj <= per; jj++)
                    {
                        int j = k * per + jj;
                        float th = j / (float)sides * Mathf.PI * 2f;
                        var dir = p * Mathf.Cos(th) + q * Mathf.Sin(th);
                        for (int i = 0; i <= segs; i++)
                        {
                            float s = i / (float)segs;
                            V(a + ax * len * s + dir * Rad(j, i), dir, In(side, jj / (float)per, len * s / tile));
                        }
                    }
                    for (int jj = 0; jj < per; jj++)
                        for (int i = 0; i < segs; i++)
                        {
                            int c0 = b0 + jj * (segs + 1) + i, c1 = c0 + segs + 1;
                            Quad(c0, c1, c1 + 1, c0 + 1);
                        }
                }
                for (int e = 0; e < 2; e++)
                {
                    if (e == 0 ? !capA : !capB) continue;
                    var o = e == 0 ? a : b; var nn = e == 0 ? -ax : ax; int i = e == 0 ? 0 : segs;
                    int c = V(o, nn, In(end, 0.5f, 0.5f));
                    int f0 = v.Count;
                    for (int j = 0; j <= sides; j++)
                    {
                        float th = j / (float)sides * Mathf.PI * 2f;
                        float rr = Rad(j, i);
                        V(o + (p * Mathf.Cos(th) + q * Mathf.Sin(th)) * rr, nn, EndAt(end, th, rr / r));
                    }
                    for (int j = 0; j < sides; j++) Tri(c, f0 + j, f0 + j + 1);
                }
            }

            /// <summary>A piece split from a log of radius r: the slice between angles t0 and t1 (radians round the
            /// axis from `down`), a to b along the old log's axis: bark round its outside, the split on its two flat
            /// faces, the rings on its ends.</summary>
            public void Slice(Vector3 a, Vector3 b, float r, float t0, float t1, Vector3 down, int seed)
            {
                var ax = b - a; float len = ax.magnitude; ax /= len;
                Frame(ax, down, out var p, out var q);
                int steps = Mathf.Max(2, Mathf.CeilToInt((t1 - t0) / (Mathf.PI / 8f)));
                float uScale = Mathf.Min(1f / BarkTile, 1f / Mathf.Max(1e-3f, r * (t1 - t0)));
                Vector3 Dir(float th) => p * Mathf.Cos(th) + q * Mathf.Sin(th);
                Vector3 Tan(float th) => -p * Mathf.Sin(th) + q * Mathf.Cos(th);
                // its outside
                int b0 = v.Count;
                for (int k = 0; k <= steps; k++)
                {
                    float th = Mathf.Lerp(t0, t1, k / (float)steps);
                    var d = Dir(th);
                    float rr = r * (1f + 0.05f * (Mathf.PerlinNoise(seed * 1.3f + k * 0.8f, 0.5f) - 0.5f));
                    float u = (th - t0) * r * uScale;
                    V(a + d * rr, d, In(BarkUV, u, 0f));
                    V(b + d * rr, d, In(BarkUV, u, len / BarkTile));
                }
                for (int k = 0; k < steps; k++) Quad(b0 + 2 * k, b0 + 2 * k + 1, b0 + 2 * k + 3, b0 + 2 * k + 2);
                // its two split faces
                foreach (var (th, sgn) in new[] { (t0, -1f), (t1, 1f) })
                {
                    var nn = Tan(th) * sgn; var d = Dir(th);
                    int c0 = V(a, nn, In(SplitUV, 0f, 0f)), c1 = V(b, nn, In(SplitUV, 0f, 1f));
                    int c2 = V(b + d * r, nn, In(SplitUV, 1f, 1f)), c3 = V(a + d * r, nn, In(SplitUV, 1f, 0f));
                    Quad(c0, c1, c2, c3);
                }
                // its ends
                for (int e = 0; e < 2; e++)
                {
                    var o = e == 0 ? a : b; var nn = e == 0 ? -ax : ax;
                    int c = V(o, nn, In(EndUV, 0.5f, 0.5f));
                    int f0 = v.Count;
                    for (int k = 0; k <= steps; k++)
                    {
                        float th = Mathf.Lerp(t0, t1, k / (float)steps);
                        V(o + Dir(th) * r, nn, EndAt(EndUV, th, 1f));
                    }
                    for (int k = 0; k < steps; k++) Tri(c, f0 + k, f0 + k + 1);
                }
            }

            /// <summary>The same, moved so the piece's own middle (not the old log's axis) is on the line a to b.</summary>
            public void SliceCentred(Vector3 a, Vector3 b, float r, float t0, float t1, Vector3 down, int seed)
            {
                var ax = (b - a).normalized;
                Frame(ax, down, out var p, out var q);
                float span = t1 - t0, mid = (t0 + t1) * 0.5f;
                float d = span >= Mathf.PI * 2f - 1e-3f ? 0f : 4f * r * Mathf.Sin(span * 0.5f) / (3f * span);   // (a sector's centroid)
                var off = (p * Mathf.Cos(mid) + q * Mathf.Sin(mid)) * d;
                Slice(a - off, b - off, r, t0, t1, down, seed);
            }

            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
